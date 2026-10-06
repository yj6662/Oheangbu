using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // #308 effect handler contract (SPEC-SPELL-120-308 section 4). One file = one handler. A handler
    //  - has a parameterless constructor and gets the host and the presenter as call arguments (no circular injection),
    //  - reads every number through ctx.Row.F(key, ...) and declares the keys in Params (no balance number in the file),
    //  - never names a glyph, a prefab, a material or a Vfx120 type: presentation goes through ISpellPresenter only,
    //  - keeps lasting state (zones, marks, timers) in its own instance fields and drops it in Clear.

    // Faulted (appended last) = this one handler threw inside Commit: the host withdrew that cast and resets the handler.
    public enum SpellClearReason { PlayerDied, Rest, SceneLeave, Disabled, Faulted }

    public readonly struct SpellCastContext
    {
        public readonly SpellCast Cast;
        public readonly SpellRow Row;
        public readonly float Now;              // the wiring's scaled clock (Time.time)
        public readonly ISpellCastHost Host;
        public readonly ISpellPresenter Fx;
        // The first stage of a single shot flies as the glyph without a final flies (SpellEffectPrimary308.cs). Both are null
        // when that glyph's row names no handler for it: the first stage is then the host's lock-on single judgement.
        public readonly ISpellPrimaryStage308 Primary;
        public readonly SpellRow Ballistics;

        public SpellCastContext(SpellCast cast, SpellRow row, float now, ISpellCastHost host, ISpellPresenter fx,
            ISpellPrimaryStage308 primary = null, SpellRow ballistics = null)
        { Cast = cast; Row = row; Now = now; Host = host; Fx = fx; Primary = primary; Ballistics = primary != null ? ballistics : null; }
    }

    public readonly struct SpellTickContext
    {
        public readonly float Now, Delta;
        public readonly ISpellCastHost Host;
        public readonly ISpellPresenter Fx;

        public SpellTickContext(float now, float delta, ISpellCastHost host, ISpellPresenter fx)
        { Now = now; Delta = delta; Host = host; Fx = fx; }
    }

    // What a cost hook may look at.
    public readonly struct SpellCastInfo
    {
        public readonly SpellCast Cast;
        public readonly SpellRow Row;           // null for a bare cast handed to the wiring by an old caller
        public SpellCastInfo(SpellCast cast, SpellRow row) { Cast = cast; Row = row; }
    }

    // What a cast hook may change: power and projectile speed only. Recognition, kind, element and geometry stay.
    public struct SpellCastDraft
    {
        public readonly SpellCast Source;
        public readonly SpellRow Row;
        public float Power, SpeedMul;
        public SpellCastDraft(SpellCast source, SpellRow row) { Source = source; Row = row; Power = source.Power; SpeedMul = source.SpeedMul; }
    }

    public interface ISpellEffect
    {
        string Id { get; }                                  // equals the Handler column of the rule sheet
        IReadOnlyList<SpellParamSpec> Params { get; }       // every key read through row.F, with its range
        bool Prepare(in SpellCastContext ctx);              // validation only: no ink, no state, no presentation
        // Rules only: ink is already spent. false = the cast is withdrawn: the host takes back every hit this call scheduled
        // and restores the ink. Presentation asked for through ctx.Fx inside Commit is held back by the host until Commit
        // has returned true, and is dropped when it returns false or throws (a presenter can never change a result).
        bool Commit(in SpellCastContext ctx);
        void Tick(in SpellTickContext ctx);
        void Clear(SpellClearReason reason);
    }

    // ---- optional hooks: an effect class implements only the ones it needs ----

    // Ink cost multiplier of the cast being spent (multiplied with the other hooks and the existing discount).
    public interface ISpellCostHook { float CostScale(in SpellCastInfo cast, float now); }

    // Runs after the hold decay, before dispatch, for every cast (legacy rows too).
    public interface ISpellCastHook { void ModifyCast(ref SpellCastDraft draft, float now); }

    // Runs after CastAccepted, for every accepted cast (legacy rows too).
    public interface ISpellAcceptHook { void OnCastAccepted(in SpellCastContext ctx); }

    // Runs for every confirmed player-side hit on an enemy (applied damage > 0).
    public interface ISpellHitHook { void OnEnemyHit(in EnemyDamageResult result, in SpellTickContext ctx); }

    // Runs when the player really took damage. May keep the letter being drawn (interruptsDrawing = false); it must never
    // heal or give ink (no reward for being hit: retaliation only).
    public interface IPlayerHitHook { void OnPlayerHit(in PlayerHitInfo hit, in SpellTickContext ctx, ref bool interruptsDrawing); }
}
