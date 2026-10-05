using System;
using System.Collections.Generic;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // The five basic judgement shapes as registered handlers, for new rows that need nothing more (SPEC-SPELL-120-308
    // section 4). The judgement itself is the host's Plan* body, the same code the 36 existing glyphs run. The existing
    // glyphs do not come through here: their rows carry a legacy feature and keep the old switch.
    public abstract class CoreShapeEffect308 : ISpellEffect
    {
        static readonly SpellParamSpec[] NoParams = Array.Empty<SpellParamSpec>();
        public abstract string Id { get; }
        public IReadOnlyList<SpellParamSpec> Params => NoParams;
        protected abstract CastPlan Plan(in SpellCastContext ctx);

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null;
        // The stroke adapter keeps presenting these rows (feedAdapter = true), exactly as it does for the existing glyphs.
        public bool Commit(in SpellCastContext ctx) => Plan(ctx) != null;
        public void Tick(in SpellTickContext ctx) { }
        public void Clear(SpellClearReason reason) { }
    }

    public sealed class CoreSingleEffect308 : CoreShapeEffect308
    {
        public const string HandlerId = "core.single";
        public override string Id => HandlerId;
        protected override CastPlan Plan(in SpellCastContext ctx) => ctx.Host.PlanSingle(ctx.Cast, true);
    }

    public sealed class CoreConeEffect308 : CoreShapeEffect308
    {
        public const string HandlerId = "core.cone";
        public override string Id => HandlerId;
        protected override CastPlan Plan(in SpellCastContext ctx) => ctx.Host.PlanCone(ctx.Cast, true);
    }

    public sealed class CoreCircleEffect308 : CoreShapeEffect308
    {
        public const string HandlerId = "core.circle";
        public override string Id => HandlerId;
        protected override CastPlan Plan(in SpellCastContext ctx) => ctx.Host.PlanCircle(ctx.Cast, true);
    }

    public sealed class CorePathEffect308 : CoreShapeEffect308
    {
        public const string HandlerId = "core.path";
        public override string Id => HandlerId;
        protected override CastPlan Plan(in SpellCastContext ctx) => ctx.Host.PlanPath(ctx.Cast, true);
    }

    public sealed class CoreVolleyEffect308 : CoreShapeEffect308
    {
        public const string HandlerId = "core.volley";
        public override string Id => HandlerId;
        protected override CastPlan Plan(in SpellCastContext ctx) => ctx.Host.PlanVolley(ctx.Cast, true);
    }
}
