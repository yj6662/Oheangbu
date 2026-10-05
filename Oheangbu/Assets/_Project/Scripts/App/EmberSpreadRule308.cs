// PURE308
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    // The ember a confirmed hit leaves in its target (SPEC-SPELL-120-308 WP-04, handler hit.spread). It sits in its
    // carrier and does nothing by itself. When the carrier falls, or when the ember's time is up, it moves to the nearest
    // other enemy and deals the damage of the hit that planted it; it may do that a limited number of times.
    public struct EmberSpread308
    {
        public Vector3 At;        // last known position of the carrier (where the ember leaves from)
        public uint Life;         // the carrier's life the ember sits in
        public float Until;       // when the ember leaves a carrier that is still standing
        public float Power;       // damage of the hit that planted it (equipment scale already inside)
        public int Left;          // moves the ember still has
    }

    public enum EmberStep308 { Stay, Jump, End }

    public static class EmberSpreadRule308
    {
        // The ember right after its hit was confirmed. No moves = no ember (the caller keeps nothing).
        public static bool Plant(Vector3 at, uint life, float now, float power, float timer, int moves, out EmberSpread308 ember)
        {
            ember = new EmberSpread308 { At = at, Life = life, Until = now + timer, Power = power, Left = moves };
            return moves > 0 && !float.IsNaN(power) && power > 0f && !float.IsNaN(timer) && timer > 0f;
        }

        // One look at the ember. carrierId = the carrier's snapshot id, or -1 when it is no longer among the targets.
        //   Stay  the carrier stands and the time is not up.
        //   Jump  the ember moved to nextId (the nearest living enemy inside radius of where it was, never the carrier it
        //         leaves) and that enemy takes ember.Power now. The ember now sits in nextId with a fresh timer and one move less.
        //   End   nobody in reach, or no move left: the ember is gone without damage.
        // defeated = the ember left because its carrier fell (or was replaced by another life), not because of time.
        // from = where the ember was when it left (the carrier's place now, or the last place a fallen carrier stood).
        public static EmberStep308 Step(ref EmberSpread308 ember, SpellActorSnap[] actors, int carrierId, float now, float timer, float radius,
            List<int> scratch, out int nextId, out bool defeated, out Vector3 from)
        {
            nextId = -1;
            bool standing = DerivedHitRule308.TryFind(actors, carrierId, out var carrier) && carrier.Alive && carrier.Life == ember.Life;
            defeated = !standing;
            if (standing) ember.At = carrier.Position;
            from = ember.At;
            if (standing && now < ember.Until) return EmberStep308.Stay;
            if (ember.Left <= 0) return EmberStep308.End;
            DerivedHitRule308.Near(actors, ember.At, radius, new[] { carrierId }, scratch);
            if (scratch.Count == 0 || !DerivedHitRule308.TryFind(actors, scratch[0], out var next)) return EmberStep308.End;
            nextId = next.Id;
            ember.At = next.Position; ember.Life = next.Life; ember.Until = now + timer; ember.Left--;
            return EmberStep308.Jump;
        }
    }
}
