using System.Collections.Generic;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // path.tide (SPEC-SPELL-120-308 WP-03): the base glyph's wave runs to the far end of its corridor and comes back.
    //  - primary = the host's path judgement, unchanged, presented by the stroke adapter like the base glyph (way out);
    //  - second stage = the returning front: return.pause seconds after the wave reaches the far end it runs back to the
    //    caster's spot at the outbound speed x return.speed, with the cast power x return.power and an attack identity of
    //    its own. An enemy is hit when the returning front reaches it, if it stands in the corridor then (TideRule308,
    //    PathFrontRule308): at most once on the way back, so at most twice by one cast.
    public sealed class TideEffect308 : ISpellEffect
    {
        public const string HandlerId = "path.tide";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("return.pause", 0f, 10f),
            new SpellParamSpec("return.speed", .1f, 10f),
            new SpellParamSpec("return.power", 0f, 5f),
        };

        readonly List<PathFrontStage308> _fronts = new List<PathFrontStage308>();
        readonly List<SpellHitOrder> _orders = new List<SpellHitOrder>();
        readonly TwoStageClock308 _clock = new TwoStageClock308();

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;
        public int PendingStages => _fronts.Count;

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null;

        public bool Commit(in SpellCastContext ctx)
        {
            var seen = TwoStageCast308.Take(ctx);
            var plan = ctx.Host.PlanPath(ctx.Cast, true);
            if (plan == null) return false;
            var area = plan.Area;
            if (area == null) return true;
            var back = TideRule308.Return(area.Point, area.Direction, area.Radius, area.Length, area.Speed,
                ctx.Now + area.Delay, ctx.Row.F("return.pause", 0f), ctx.Row.F("return.speed", 0f));
            if (back.Valid) _fronts.Add(new PathFrontStage308(seen, back, ctx.Cast.Power * ctx.Row.F("return.power", 0f)));
            return true;
        }

        public void Tick(in SpellTickContext ctx)
        {
            float judged = _clock.Advance(ctx.Now);
            for (int i = _fronts.Count - 1; i >= 0; i--)
            {
                if (i >= _fronts.Count) continue;
                var front = _fronts[i];
                if (front.Step(ctx.Host, judged, _orders)) _fronts.Remove(front);
            }
        }

        public void Clear(SpellClearReason reason)
        {
            _fronts.Clear(); _orders.Clear(); _clock.Reset();
        }
    }
}
