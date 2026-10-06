// PURE308
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // One piece of cover a spell put into the world (SPEC-SPELL-120-308 WP-13). Flat geometry, like every area judgement.
    //   rock  a circle that stands where the rock fell (Centre, Radius)
    //   wall  a line across the path of a wave, at the wave front (Centre = the front's point now, Direction = the way the
    //         wave travels, Radius = half the width of the wall). It covers who is behind it from who is ahead of it.
    public struct SpellCover308
    {
        public long Token;          // the owner id of the screens this piece hangs on enemies
        public bool Wall;
        public Vector3 Centre, Direction;
        public float Radius;
        public float From, Until;   // stands while From <= now < Until
        public int Fx;              // presenter handle id (0 = nothing shown)
    }

    // "Cover": a ranged strike from an enemy to the player is stopped when the straight line between them is cut by a
    // piece of cover. The rule only answers that question; the enemy -> player doorway asks the enemy's strike veil, which
    // the cover handlers keep up to date (EnemyStrikeVeil308.Screen). Melee strikes are never stopped by cover.
    public static class CoverRule308
    {
        public static bool Stands(in SpellCover308 piece, float now) => now >= piece.From && now < piece.Until;

        // Does the piece cut the line from the shooter to the one shot at, at this time?
        public static bool Blocks(in SpellCover308 piece, Vector3 shooter, Vector3 shotAt, float now)
        {
            if (!Stands(piece, now) || !(piece.Radius > 0f)) return false;
            return piece.Wall ? WallCuts(piece, shooter, shotAt) : RockCuts(piece, shooter, shotAt);
        }

        // The rock cuts the line when the line passes through its circle and neither end stands inside it (someone inside
        // the rock's circle has nothing between itself and the other).
        static bool RockCuts(in SpellCover308 rock, Vector3 shooter, Vector3 shotAt)
        {
            Vector3 a = AreaGeometry.Flat(shooter - rock.Centre), b = AreaGeometry.Flat(shotAt - rock.Centre);
            float radius = rock.Radius;
            if (a.magnitude <= radius || b.magnitude <= radius) return false;
            Vector3 line = b - a;
            float length = line.sqrMagnitude;
            if (length < 1e-6f) return false;
            float t = Mathf.Clamp01(-Vector3.Dot(a, line) / length);     // the point of the line nearest to the centre
            return (a + line * t).magnitude <= radius;
        }

        // The wall cuts the line when the shooter is ahead of it (the side the wave runs towards), the one shot at is
        // behind it, and the line crosses within the wall's width.
        static bool WallCuts(in SpellCover308 wall, Vector3 shooter, Vector3 shotAt)
        {
            Vector3 ahead = AreaGeometry.Flat(wall.Direction);
            if (ahead.sqrMagnitude < 1e-6f) return false;
            ahead = ahead.normalized;
            Vector3 a = AreaGeometry.Flat(shooter - wall.Centre), b = AreaGeometry.Flat(shotAt - wall.Centre);
            float sideA = Vector3.Dot(a, ahead), sideB = Vector3.Dot(b, ahead);
            if (!(sideA > 0f) || !(sideB < 0f)) return false;
            Vector3 crossing = a + (b - a) * (sideA / (sideA - sideB));
            return crossing.magnitude <= wall.Radius;                     // the crossing lies on the wall's line: its distance from the centre is its offset along the wall
        }

        // Where the rock of an air cast lands: `range` along the flat aim direction from the caster.
        public static Vector3 AirLanding(Vector3 caster, Vector3 forward, float range)
        {
            Vector3 flat = AreaGeometry.Flat(forward);
            flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
            return caster + flat * Mathf.Max(0f, range);
        }

        // Where a thrown rock takes root. An unguided rock (the fall of its body: Spellcraft Bible chapter 4) roots on the
        // spot it was thrown at, whether its target still stands there or walked away. A guided shot roots on the spot its
        // target stands on at the moment of the landing; a target that is gone by then (dead, disabled, another life)
        // leaves the spot the rock was thrown at.
        public static Vector3 RockLanding(Vector3 thrownAt, bool guided, bool targetStillThere, Vector3 targetNow)
            => guided && targetStillThere ? targetNow : thrownAt;

        // The rooted rock: a circle around the landing point that stands from the landing for `stays` seconds.
        public static SpellCover308 RockOf(long token, Vector3 landing, float radius, float landedAt, float stays)
            => new SpellCover308 { Token = token, Centre = landing, Radius = radius, From = landedAt, Until = landedAt + Mathf.Max(0f, stays) };

        // The wall of a wave at this time: at the wave front, across the corridor.
        public static SpellCover308 WallOf(in PathFront308 front, long token, float now)
        {
            return new SpellCover308
            {
                Token = token, Wall = true, Centre = front.Point(now), Direction = front.Direction, Radius = front.HalfWidth,
                From = front.StartsAt, Until = front.EndsAt,
            };
        }
    }
}
