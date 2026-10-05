// PURE308
using UnityEngine;

namespace Oheangbu.App
{
    // The shot that comes back after a confirmed hit and returns a little ink and a little health, once
    // (SPEC-SPELL-120-308 WP-04, handler hit.cycle). This is a reward for hitting, never for being hit.
    public struct CycleReturn308
    {
        public float Ink;         // ink the return gives back (base capacity units, the unit of the cast cost)
        public float Hp01;        // share of the player's maximum health the return restores
        public int Fx;            // presenter handle id of the cast (0 = nothing shown)
    }

    public static class CycleRule308
    {
        // When the returning shot reaches the caster.
        public static float ReturnAt(float hitAt, float returnTime)
        {
            return hitAt + (float.IsNaN(returnTime) || returnTime < 0f ? 0f : returnTime);
        }

        // Ink the return gives back. It must stay below what the cast cost: a cycle shot never pays for itself. Data that
        // would refund the whole cost (or more) refunds nothing instead of being trusted.
        public static float Refund(float refundInk, float castCost)
        {
            if (float.IsNaN(refundInk) || float.IsNaN(castCost) || refundInk <= 0f) return 0f;
            return refundInk < castCost ? refundInk : 0f;
        }

        // Health the return restores.
        public static float Heal(float maxHp, float hp01)
        {
            if (float.IsNaN(maxHp) || float.IsNaN(hp01) || maxHp <= 0f) return 0f;
            return maxHp * Mathf.Clamp01(hp01);
        }

        // The return of one confirmed hit (one per cast: a single shot has one scheduled impact).
        public static CycleReturn308 Plan(float refundInk, float castCost, float hp01, int fx)
        {
            return new CycleReturn308 { Ink = Refund(refundInk, castCost), Hp01 = Mathf.Clamp01(float.IsNaN(hp01) ? 0f : hp01), Fx = fx };
        }
    }
}
