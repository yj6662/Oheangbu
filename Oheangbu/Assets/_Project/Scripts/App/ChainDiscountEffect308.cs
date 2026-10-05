using System.Collections.Generic;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // WP-08 water buff with the nieun final: while it runs, a cast whose element differs from the cast accepted just before
    // it costs less ink (ChainState308). The last accepted element is tracked for every accepted cast, the buff's own cast
    // included; a cast of the same element, or the first cast after a clear, pays the full cost.
    public sealed class ChainDiscountEffect308 : SelfBuffEffect308, ISpellCostHook, ISpellAcceptHook
    {
        public const string HandlerId = "buff.chain";
        static readonly SpellParamSpec[] Specs = { new SpellParamSpec("duration", .1f, 600f), new SpellParamSpec("chain.discount", 0f, .9f) };
        readonly ChainState308 _chain = new ChainState308();
        float _discount;

        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;

        protected override bool OnActivating(in SpellCastContext ctx, float duration)
        {
            _discount = ctx.Row.F("chain.discount", 0f);
            return true;
        }

        public float CostScale(in SpellCastInfo cast, float now) => _chain.CostScale(Active(now), cast.Cast.Element, _discount);

        public void OnCastAccepted(in SpellCastContext ctx) { _chain.Record(ctx.Cast.Element); }

        protected override void OnCleared(SpellClearReason reason) { _chain.Clear(); _discount = 0f; }
    }
}
