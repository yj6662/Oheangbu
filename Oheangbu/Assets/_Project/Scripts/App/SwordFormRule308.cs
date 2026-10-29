// PURE308
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App
{
    // What the bow does on one frame (SwordFormRule308.BowStep).
    public enum BowStep308 { None, Begin, Fire, Fizzle, Cancel }

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

        // ---- the combo of the two-handed sword (D308-127 debate 15, TEST) ----
        // This strike's place in the chain: 1 = the first, hits = the finisher; 0 = a row without a combo (hits < 2). The chain
        // goes back to 1 after the finisher, or when the strike comes later than window seconds after it could have come
        // (readyAt = when the previous strike allowed the next one). A strike that cuts air still counts: the rhythm is the body's.
        public static int ComboStep(int previous, float now, float readyAt, int hits, float window)
        {
            if (hits < 2) return 0;
            if (previous <= 0 || previous >= hits || now > readyAt + Mathf.Max(0f, window)) return 1;
            return previous + 1;
        }

        // The wait after a strike of that step: the finisher rests, the others take the row's interval.
        public static float ComboGap(int step, int hits, float interval, float rest) => hits >= 2 && step >= hits ? Mathf.Max(0f, rest) : Mathf.Max(0f, interval);

        // The power share of that step (only the finisher is heavier).
        public static float ComboShare(int step, int hits, float finish) => hits >= 2 && step >= hits ? Mathf.Max(0f, finish) : 1f;

        // A click this close before the next strike is allowed is kept and fires then (the click is read on its frame only).
        public static bool Buffered(float now, float readyAt, float buffer) => buffer > 0f && now < readyAt && now >= readyAt - buffer;

        // ---- the flail's ink hold (D308-127 debate 15, TEST) ----
        // One swing that landed on `landed` enemies holds the upkeep until the returned time: never shortened, never more than
        // cap seconds ahead of now. 0 enemies (or no per) leaves the running hold as it is.
        public static float HoldUntil(float running, float now, int landed, float per, float cap)
        {
            if (landed <= 0 || per <= 0f || cap <= 0f) return running;
            return Mathf.Max(running, now + Mathf.Min(cap, per * landed));
        }

        // ---- the horn bow (D308-127 debate 16, TEST) ----
        // What the bow does this frame. Not drawing: a held click (once the recovery allows it) begins the draw. Drawing: the
        // release fires when the draw lasted draw seconds, earlier it fizzles; a click that is neither held nor released
        // (the input doors shut: a menu, a dodge, a jump, death) cancels. There is no automatic shot: a full draw waits.
        public static BowStep308 BowStep(bool drawing, float now, float drawStart, float draw, bool held, bool released, float readyAt)
        {
            if (!drawing) return held && CanStrike(now, readyAt) ? BowStep308.Begin : BowStep308.None;
            if (released) return now - drawStart >= Mathf.Max(0f, draw) - 1e-4f ? BowStep308.Fire : BowStep308.Fizzle;
            return held ? BowStep308.None : BowStep308.Cancel;
        }

        // How far the bow is drawn (0..1; presentation and the movement scale read it, the shot only asks Fire).
        public static float Drawn01(float now, float drawStart, float draw) => draw <= 0f ? 1f : Mathf.Clamp01((now - drawStart) / draw);

        // The shot's target: the aimed enemy when it is alive and inside range (straight-line distance from the bow);
        // -1 = no target, the arrow flies range ahead and misses. The bow has no cone: it shoots what the reticle holds.
        public static int ShotTarget(SpellActorSnap[] actors, Vector3 origin, float range, int aimedId)
        {
            if (actors == null || aimedId < 0 || range <= 0f) return -1;
            for (int i = 0; i < actors.Length; i++)
                if (actors[i].Id == aimedId) return actors[i].Alive && (actors[i].Position - origin).sqrMagnitude <= range * range ? aimedId : -1;
            return -1;
        }

        // Seconds the arrow flies, fixed when it leaves the bow (the haste scale of that moment included, D308-123).
        public static float FlightTime(float distance, float speed, float speedScale)
        {
            float v = Mathf.Max(0f, speed) * Mathf.Max(0f, speedScale);
            if (!(v > 1e-3f) || float.IsInfinity(v)) return 0f;
            return Mathf.Max(0f, distance) / v;
        }

        // The ground speed multiplier of the form: move while the bow is drawn (Drawing and Ready), 1 otherwise.
        public static float MoveScale(bool drawing, float move) => drawing ? Mathf.Clamp(move, .05f, 1f) : 1f;

        // ---- weapon strikes (D308-127, TEST: the five forms became five weapons; numbers are row parameters) ----
        // Up to count enemies of one strike, in the order they take it: their ids and distances (horizontal; along the line
        // for a line weapon) are written to ids / distances, the return is how many. halfWidth > 0 = a line weapon (the spear): a corridor of that half width
        // and the reach along direction, nearest along the line first, so the second enemy is the one behind the first.
        // Otherwise the cone of the sword: the aimed enemy first, then the nearest (count 1 = SelectTarget).
        public static int SelectTargets(SpellActorSnap[] actors, Vector3 origin, Vector3 direction, float reach, float halfAngle,
            float halfWidth, int aimedId, int count, int[] ids, float[] distances)
        {
            if (actors == null || ids == null || distances == null || reach <= 0f) return 0;
            int limit = Mathf.Min(Mathf.Max(1, count), Mathf.Min(ids.Length, distances.Length));
            if (limit < 1) return 0;
            bool line = halfWidth > 0f;
            int n = 0, first = 0;
            if (!line)
                for (int i = 0; i < actors.Length; i++)
                    if (actors[i].Id == aimedId && actors[i].Alive &&
                        AreaGeometry.InCone(origin, direction, actors[i].Position, halfAngle, reach, out _, out float aimed))
                    { ids[0] = aimedId; distances[0] = aimed; n = first = 1; break; }
            for (int i = 0; i < actors.Length; i++)
            {
                if (!actors[i].Alive || (first > 0 && actors[i].Id == aimedId)) continue;
                float distance;
                if (line) { if (!AreaGeometry.InCorridor(origin, direction, actors[i].Position, halfWidth, reach, out distance, out _)) continue; }
                else if (!AreaGeometry.InCone(origin, direction, actors[i].Position, halfAngle, reach, out _, out distance)) continue;
                // kept sorted by distance (ties keep the snapshot order); past the limit the farthest one drops out
                int at = n;
                while (at > first && distances[at - 1] > distance) at--;
                if (at >= limit) continue;
                for (int k = Mathf.Min(n, limit - 1); k > at; k--) { ids[k] = ids[k - 1]; distances[k] = distances[k - 1]; }
                ids[at] = actors[i].Id; distances[at] = distance;
                if (n < limit) n++;
            }
            return n;
        }

        // The line weapons' aim correction: the line points at the aimed enemy when it stands inside the reach within
        // aimDegrees of forward; otherwise it runs straight ahead. (The cone has its own: the aimed enemy is taken first.)
        public static Vector3 StrikeDirection(SpellActorSnap[] actors, Vector3 origin, Vector3 forward, float reach, float aimDegrees, int aimedId)
        {
            if (actors == null || aimDegrees <= 0f || aimedId < 0) return forward;
            for (int i = 0; i < actors.Length; i++)
            {
                if (actors[i].Id != aimedId) continue;
                if (actors[i].Alive && AreaGeometry.InCone(origin, forward, actors[i].Position, aimDegrees, reach, out _, out float distance) && distance > 1e-3f)
                    return AreaGeometry.Flat(actors[i].Position - origin) / distance;
                break;
            }
            return forward;
        }

        // The share of the strike power one target takes. order 0 = the first enemy the strike meets; the ones behind it
        // take through (the spear goes on, weaker). An enemy closer than near also takes inner (it stands inside the
        // point: a spear needs room). 1 = the plain sword.
        public static float TargetShare(int order, float distance, float through, float near, float inner)
        {
            float share = order > 0 ? Mathf.Clamp01(through) : 1f;
            return near > 0f && distance < near ? share * Mathf.Clamp01(inner) : share;
        }

        // Counter strike =the sword's element overcomes the enemy's element (the parry table's relation). An enemy without
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
