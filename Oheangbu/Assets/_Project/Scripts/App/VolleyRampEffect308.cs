using System.Collections.Generic;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // volley.ramp (SPEC-SPELL-120-308 WP-02): the volley of the row, with every later shot carrying a larger share of the
    // cast power. Targets, launch times and impact times are the host's volley judgement, unchanged, and the stroke adapter
    // fires the shots of that plan as it does for the base volley. This handler only supplies the per-shot weights
    // (VolleyRampRule308) and keeps nothing between casts.
    public sealed class VolleyRampEffect308 : ISpellEffect
    {
        public const string HandlerId = "volley.ramp";
        const string FirstKey = "ramp.first", LastKey = "ramp.last";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("ramp.first", 0.01f, 10f),   // share weight of the first shot
            new SpellParamSpec("ramp.last", 0.01f, 10f),    // share weight of the last shot (must be larger)
        };
        readonly List<float> _weights = new List<float>();   // scratch buffer of the cast being committed

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;

        // The shot count must come from the row (the host's own fallback count is not this handler's to guess), and the two
        // end weights must rise. A row that cannot ramp is no cast: no ink is spent.
        public bool Prepare(in SpellCastContext ctx)
        {
            return ctx.Host != null && ctx.Row != null &&
                VolleyRampRule308.Valid(ctx.Cast.Area.Shots, ctx.Row.F(FirstKey, 0f), ctx.Row.F(LastKey, 0f));
        }

        public bool Commit(in SpellCastContext ctx)
        {
            if (!VolleyRampRule308.Weights(ctx.Cast.Area.Shots, ctx.Row.F(FirstKey, 0f), ctx.Row.F(LastKey, 0f), _weights)) return false;
            return ctx.Host.PlanVolley(ctx.Cast, true, _weights) != null;
        }

        public void Tick(in SpellTickContext ctx) { }
        public void Clear(SpellClearReason reason) { _weights.Clear(); }
    }
}
