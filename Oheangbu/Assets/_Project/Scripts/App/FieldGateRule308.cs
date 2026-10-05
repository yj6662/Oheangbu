// PURE308
using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // One world gate as a rule sees it (SPEC-SPELL-120-308 WP-12). Kind = which field glyph opens it; Radius = how far the
    // thing reaches from its pivot; Busy = a cast is already opening it.
    public readonly struct FieldGateSnap
    {
        public readonly int Id, Kind;
        public readonly Vector3 Position;
        public readonly float Radius;
        public readonly bool Open, Busy;

        public FieldGateSnap(int id, int kind, Vector3 position, float radius, bool open, bool busy)
        { Id = id; Kind = kind; Position = position; Radius = radius; Open = open; Busy = busy; }
    }

    // The three path-opening field glyphs (burn the vines, cut the boulder, cleanse the pollution) share one rule:
    // the cast opens the nearest closed gate of its own kind that the caster faces and can reach, and only outside combat.
    public static class FieldGateRule308
    {
        public const int None = -1;

        // Index of the gate the cast opens, or None. Reach is measured to the gate's edge (distance to its pivot minus its
        // radius). The caster must face it within halfAngle of the flat forward, unless standing inside it. Nearest wins;
        // a tie goes to the lower id.
        public static int Pick(IReadOnlyList<FieldGateSnap> gates, int kind, Vector3 player, Vector3 forward, float reach, float halfAngle)
        {
            int best = None; float bestGap = float.PositiveInfinity;
            for (int i = 0; gates != null && i < gates.Count; i++)
            {
                var gate = gates[i];
                if (gate.Kind != kind || gate.Open || gate.Busy) continue;
                float gap = Mathf.Max(0f, Vector3.Distance(player, gate.Position) - gate.Radius);
                if (gap > reach || !Faces(player, forward, gate, halfAngle)) continue;
                if (best != None && (gap > bestGap || (gap == bestGap && gate.Id > gates[best].Id))) continue;
                best = i; bestGap = gap;
            }
            return best;
        }

        public static bool Faces(Vector3 player, Vector3 forward, in FieldGateSnap gate, float halfAngle)
        {
            Vector3 to = AreaGeometry.Flat(gate.Position - player);
            if (to.magnitude <= gate.Radius) return true;                 // standing in it
            Vector3 ahead = AreaGeometry.Flat(forward);
            if (ahead.sqrMagnitude < 1e-4f) return false;
            return Vector3.Angle(ahead, to) <= halfAngle;
        }

        // "Non-combat only". The cast is refused while a living enemy hunts the player (engaged: the predicate the wood lift
        // and the earth bridge already use) or stands within radius of the caster (radius 0 = the hunt alone decides).
        public static bool InCombat(SpellActorSnap[] actors, IReadOnlyList<bool> engaged, Vector3 player, float radius)
        {
            for (int i = 0; actors != null && i < actors.Length; i++)
            {
                if (!actors[i].Alive) continue;
                if (engaged != null && i < engaged.Count && engaged[i]) return true;
                if (radius > 0f && AreaGeometry.InCircle(player, actors[i].Position, radius, out _)) return true;
            }
            return false;
        }
    }
}
