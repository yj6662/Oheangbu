using System.Collections.Generic;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // volley.heat (SPEC-SPELL-120-308 WP-02): the volley of the row; every cast of this handler made inside the window
    // raises the power of the next one (VolleyHeatRule308). The heat is the caster's own stack, kept in this instance and
    // dropped on death, rest, scene leave and disable. The power is raised through the cast hook, so the accepted cast, the
    // plan and the scheduled shots all carry the same heated power; the judgement and the presentation are the host's volley.
    public sealed class VolleyHeatEffect308 : ISpellEffect, ISpellCastHook
    {
        public const string HandlerId = "volley.heat";
        const string StepKey = "heat.step", MaxKey = "heat.max", WindowKey = "heat.window";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("heat.step", 0f, 2f),        // power added per stack, as a share of the cast power
            new SpellParamSpec("heat.max", 1f, 20f),        // stack cap
            new SpellParamSpec("heat.window", 0.1f, 60f),   // seconds a stack survives without another cast
        };
        VolleyHeat308 _heat;

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;

        // Runs for every cast before dispatch; only this handler's own rows are heated. Nothing changes here: a cast that
        // then fails (no ink, refused) leaves the heat as it was.
        public void ModifyCast(ref SpellCastDraft draft, float now)
        {
            if (draft.Row == null || draft.Row.Handler != HandlerId) return;
            draft.Power *= VolleyHeatRule308.Multiplier(_heat, now, draft.Row.F(WindowKey, 0f), draft.Row.F(StepKey, 0f));
        }

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null;

        // The cast counts whether or not a shot finds a target: firing is what heats the barrel.
        public bool Commit(in SpellCastContext ctx)
        {
            if (ctx.Host.PlanVolley(ctx.Cast, true) == null) return false;
            _heat = VolleyHeatRule308.AfterCast(_heat, ctx.Now, ctx.Row.F(WindowKey, 0f), Mathf.RoundToInt(ctx.Row.F(MaxKey, 0f)));
            return true;
        }

        public void Tick(in SpellTickContext ctx) { }
        public void Clear(SpellClearReason reason) { _heat = default; }
    }
}
