using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // #308 first stage of a single shot (Spellcraft Bible chapter 4: how a shot flies belongs to the INITIAL consonant).
    // A glyph with a final keeps its own second effect and takes its first judgement from the handler of the glyph
    // without a final: a range (wood), an unguided fall (earth), a turn-limited guidance (water). So the burst glyph
    // misses a walking enemy exactly as its base glyph does. Which handler flies a body is data (the row of the glyph
    // without a final, PrimaryShotRule308); a body whose base row names no such handler keeps the wiring's lock-on single
    // judgement, as before.

    // What a first stage answers.
    public readonly struct SpellPrimaryShot308
    {
        public readonly bool Launched;        // false = the judgement refused the cast (the handler answers false from Commit)
        public readonly PlannedHit Hit;       // the hit that may land: target, expected landing time, the power it carries (equipment
                                              // scale inside), its attack id (the id OnEnemyHit reports). null = nothing can land
        public readonly float LatestImpact;   // the last moment that hit can still land: a watch for it may be dropped after this
        public readonly bool Held;            // true = decided in flight by the stage's handler (it may miss);
                                              // false = scheduled by the wiring (certain unless the target is gone)

        SpellPrimaryShot308(bool launched, PlannedHit hit, float latest, bool held)
        { Launched = launched; Hit = hit; LatestImpact = latest; Held = held; }

        public bool CanLand => Launched && Hit != null && Hit.Target != null && Hit.AttackId > 0;

        public static SpellPrimaryShot308 Refused => default;
        // Launched, and nothing can land: nobody was aimed at, or the target stood beyond the shot's reach.
        public static SpellPrimaryShot308 Nothing => new SpellPrimaryShot308(true, null, float.NegativeInfinity, false);
        public static SpellPrimaryShot308 Scheduled(PlannedHit hit) => new SpellPrimaryShot308(true, hit, hit.ImpactTime, false);
        public static SpellPrimaryShot308 InFlight(PlannedHit hit, float latestImpact) => new SpellPrimaryShot308(true, hit, latestImpact, true);
    }

    // A handler that can fly the first stage of another glyph of its body. `ballistics` is the row of the glyph without
    // a final: every number of the flight is read from it, never from the casting row.
    public interface ISpellPrimaryStage308
    {
        SpellPrimaryShot308 Launch(ISpellCastHost host, in SpellCast cast, SpellRow ballistics, float now, bool feedAdapter);
        // How many shots this stage holds in flight. The host notes it before a Commit ...
        int HeldShots { get; }
        // ... and a withdrawn cast drops every shot launched after that mark, so no hit of it can land with its ink given back
        // (the held counterpart of taking back the wiring's scheduled hits).
        void Recall(int mark);
    }

    public static class SpellPrimary308
    {
        // The first stage of this cast. feedAdapter = false when the effect presents the shot through ISpellPresenter.
        public static SpellPrimaryShot308 Plan(in SpellCastContext ctx, bool feedAdapter)
        {
            if (ctx.Primary != null && ctx.Ballistics != null) return ctx.Primary.Launch(ctx.Host, ctx.Cast, ctx.Ballistics, ctx.Now, feedAdapter);
            return LockOn(ctx.Host, ctx.Cast, feedAdapter);
        }

        // The wiring's own single judgement: a hit scheduled on the aimed enemy (the lock-on guidance of before #308).
        public static SpellPrimaryShot308 LockOn(ISpellCastHost host, in SpellCast cast, bool feedAdapter)
        {
            var plan = host.PlanSingle(cast, feedAdapter);
            if (plan == null) return SpellPrimaryShot308.Refused;
            var hit = plan.Hits.Count > 0 ? plan.Hits[0] : null;
            return hit != null && hit.Target != null ? SpellPrimaryShot308.Scheduled(hit) : SpellPrimaryShot308.Nothing;
        }
    }
}
