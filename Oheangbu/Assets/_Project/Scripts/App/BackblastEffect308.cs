using System.Collections.Generic;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // cone.backblast (SPEC-SPELL-120-308 WP-03): the base glyph's cone, then a larger burst at the far end of the cone.
    //  - primary = the host's cone judgement, unchanged, presented by the stroke adapter like the base glyph;
    //  - second stage = a circle at the end of the cone's axis, blast.delay seconds after the cone lands, with the cast
    //    power x blast.power (the sheet keeps blast.power above 1: the burst is the bigger hit). It bursts whether or not
    //    the cone hit anyone, and reaches blast.radius beyond the cone's range.
    // Who is hit is decided at the burst (BackblastRule308), on the enemies as they are then.
    public sealed class BackblastEffect308 : ISpellEffect
    {
        public const string HandlerId = "cone.backblast";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("blast.radius", .1f, 20f),
            new SpellParamSpec("blast.delay", 0f, 10f),
            new SpellParamSpec("blast.power", 1f, 5f),
        };

        sealed class Stage
        {
            public TwoStageCast308 Cast;
            public BackblastPlan308 Plan;
        }

        readonly List<Stage> _stages = new List<Stage>();
        readonly List<SpellHitOrder> _orders = new List<SpellHitOrder>();
        readonly TwoStageClock308 _clock = new TwoStageClock308();

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;
        public int PendingStages => _stages.Count;

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null;

        public bool Commit(in SpellCastContext ctx)
        {
            var seen = TwoStageCast308.Take(ctx);
            var plan = ctx.Host.PlanCone(ctx.Cast, true);
            if (plan == null) return false;
            var area = plan.Area;
            if (area == null) return true;
            _stages.Add(new Stage
            {
                Cast = seen,
                Plan = BackblastRule308.Plan(area.Point, area.Direction, area.Length, ctx.Now + area.Delay, ctx.Cast.Power,
                    ctx.Row.F("blast.radius", 0f), ctx.Row.F("blast.delay", 0f), ctx.Row.F("blast.power", 0f), ctx.Cast.Element),
            });
            return true;
        }

        public void Tick(in SpellTickContext ctx)
        {
            float judged = _clock.Advance(ctx.Now);
            for (int i = _stages.Count - 1; i >= 0; i--)
            {
                if (i >= _stages.Count) continue;
                var stage = _stages[i];
                if (!TwoStageRule308.Due(stage.Plan.At, judged)) continue;
                _stages.RemoveAt(i);
                _orders.Clear();
                BackblastRule308.Resolve(stage.Plan, stage.Cast.AtCast, stage.Cast.Now(), _orders);
                stage.Cast.Apply(ctx.Host, _orders);
            }
        }

        public void Clear(SpellClearReason reason)
        {
            _stages.Clear(); _orders.Clear(); _clock.Reset();
        }
    }
}
