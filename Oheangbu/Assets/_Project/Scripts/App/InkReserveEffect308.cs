using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // WP-08 water buff with the mieum final: the ink pool carries a temporary capacity coefficient while it runs
    // (InkPool.SetTemporaryCapacity, multiplied with the equipment coefficient). Widening keeps the amount of ink, so the
    // cast gives nothing by itself. When the buff ends, ink above the normal maximum stays as a reserve that dwindles at a
    // fixed rate and is spent first (InkReserveState308). Nothing is saved; a clear puts the coefficient back to 1.
    public sealed class InkReserveEffect308 : SelfBuffEffect308
    {
        public const string HandlerId = "buff.reserve";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("duration", .1f, 600f), new SpellParamSpec("reserve.capacity", 1f, 3f), new SpellParamSpec("reserve.decay", .001f, 1f),
        };
        readonly InkReserveState308 _state = new InkReserveState308();
        InkPool _ink;
        float _decay;

        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        public float Reserve => _state.Reserve;

        public override bool Prepare(in SpellCastContext ctx) => base.Prepare(ctx) && ctx.Host.Ink != null;

        protected override bool OnActivating(in SpellCastContext ctx, float duration)
        {
            _ink = ctx.Host.Ink; _decay = ctx.Row.F("reserve.decay", 0f);
            _state.Begin(ctx.Row.F("reserve.capacity", 1f));
            _ink.SetTemporaryCapacity(_state.Capacity);
            return true;
        }

        protected override void OnTick(in SpellTickContext ctx)
        {
            if (_ink == null || _state.Idle) return;
            if (_state.Expanded)
            {
                if (Active(ctx.Now)) return;
                _state.Expire(_ink.Value);
            }
            else _state.Decay(_ink.Value, _decay, ctx.Delta);
            _ink.SetTemporaryCapacity(_state.Capacity);
        }

        protected override void OnCleared(SpellClearReason reason)
        {
            _state.Clear();
            if (_ink != null) _ink.SetTemporaryCapacity(1f);
            _ink = null; _decay = 0f;
        }
    }
}
