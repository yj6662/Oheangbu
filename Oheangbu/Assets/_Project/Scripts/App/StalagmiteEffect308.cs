using System.Collections.Generic;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // path.stalagmite (SPEC-SPELL-120-308 WP-03): the base glyph's storm along a corridor, then a stone spine that rises
    // along the same corridor behind it.
    //  - primary = the host's path judgement, unchanged, presented by the stroke adapter like the base glyph;
    //  - second stage = a second front on the same corridor at the same speed, second.delay seconds behind the storm
    //    front, with the cast power x second.power. An enemy is hit when that front reaches it, if it stands in the
    //    corridor then (StalagmiteRule308, PathFrontRule308): once per enemy.
    public sealed class StalagmiteEffect308 : ISpellEffect
    {
        public const string HandlerId = "path.stalagmite";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("second.delay", 0f, 10f),
            new SpellParamSpec("second.power", 0f, 5f),
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
            var front = StalagmiteRule308.Front(area.Point, area.Direction, area.Radius, area.Length, area.Speed,
                ctx.Now + area.Delay, ctx.Row.F("second.delay", 0f));
            if (front.Valid) _fronts.Add(new PathFrontStage308(seen, front, ctx.Cast.Power * ctx.Row.F("second.power", 0f)));
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
