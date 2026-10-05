// PURE308
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App
{
    // Sword form (SPELL-BUFF, the five eo + siot glyphs): the brush becomes a sword of the glyph's element. While it lasts
    // no spell can be cast, a click is a sword strike, ink only goes down, and the form ends when the ink is gone.
    // A strike whose element overcomes the enemy's element is a counter strike: the third canon groggy source, and a small one.
    public static class SwordFormRule308
    {
        // ---- no spell while the brush is a sword ----
        // The form answers the cost hook with this multiplier. A price nobody can pay is the only veto the frozen dispatch
        // offers; lockScale comes from the row (0 or 1 = the price lock is off, e.g. once the draw mode itself is gated).
        public static float CostScale(bool active, float lockScale) => active && lockScale > 1f ? lockScale : 1f;

        // InkPool.TrySpend's own refusal rule, for the cases: a spend is refused when the pool holds less than the amount.
        public static bool SpendRefused(float ink, float cost, float costScale, float capacityMultiplier)
        {
            float amount = Mathf.Max(0f, cost * costScale) / Mathf.Max(1f, capacityMultiplier);
            return ink + 1e-4f < amount;
        }

        // ---- upkeep ----
        // Intake stop: ink does not come in while the form lasts. received = what the pool just reported as income (harvest
        // chunk, parry refund, any other gain); the pool is put back to what it held before. Returns what the pool must show.
        // (Natural regeneration never starts during the form: the upkeep is a spend every frame, and regeneration waits
        // for a pause after the last spend.)
        public static float IntakeStop(float ink, float received) => Mathf.Clamp01(ink - Mathf.Max(0f, received));

        public static float DrainAmount(float perSecond, float delta)
        {
            float amount = Mathf.Max(0f, perSecond) * Mathf.Max(0f, delta);
            return float.IsNaN(amount) || float.IsInfinity(amount) ? 0f : amount;
        }

        // The form ends with the ink.
        public static bool Exhausted(float ink) => ink <= 0f;

        // The form cannot outlive its ink: the longest it can still last, in seconds (0 = no upkeep, so no bound).
        // Presentation only (how long the sword may have to stay visible); the end itself is Exhausted.
        public static float LongestLife(float ink, float capacityMultiplier, float drainPerSecond)
        {
            if (drainPerSecond <= 0f || ink <= 0f || float.IsNaN(ink)) return 0f;
            return ink * Mathf.Max(1f, capacityMultiplier) / drainPerSecond;
        }

        // ---- strike ----
        public static bool CanStrike(float now, float nextStrikeAt) => now >= nextStrikeAt;
        public static float NextStrikeAt(float now, float interval) => now + Mathf.Max(0f, interval);

        // One enemy per strike (the glyphs are single-target): the aimed enemy when it stands inside the reach and the arc,
        // otherwise the nearest living enemy inside them. -1 = the strike cuts air. Horizontal judgement, like every area shape.
        public static int SelectTarget(SpellActorSnap[] actors, Vector3 origin, Vector3 forward, float reach, float halfAngle, int aimedId)
        {
            if (actors == null || reach <= 0f) return -1;
            int best = -1; float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < actors.Length; i++)
            {
                if (!actors[i].Alive) continue;
                if (!AreaGeometry.InCone(origin, forward, actors[i].Position, halfAngle, reach, out _, out float distance)) continue;
                if (actors[i].Id == aimedId) return actors[i].Id;
                if (distance < bestDistance) { best = actors[i].Id; bestDistance = distance; }
            }
            return best;
        }

        // Counter strike = the sword's element overcomes the enemy's element (the parry table's relation). An enemy without
        // an element can never be countered.
        public static bool IsCounter(Element sword, bool enemyHasElement, Element enemy)
        {
            return enemyHasElement && ElementRelations.Overcomes(sword, enemy);
        }

        // "A little groggy": every hitsPerGain-th counter strike on the same enemy is worth steps groggy steps; the strikes
        // between add nothing. progress is that enemy's running count. Returns the steps to add now (0 = none yet).
        public static int CounterGain(ref int progress, int hitsPerGain, int steps)
        {
            if (hitsPerGain < 1) hitsPerGain = 1;
            if (steps <= 0) { progress = 0; return 0; }
            progress++;
            if (progress < hitsPerGain) return 0;
            progress = 0;
            return steps;
        }
    }
}
