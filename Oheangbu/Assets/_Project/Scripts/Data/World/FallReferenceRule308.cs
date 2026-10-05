using System;
namespace Oheangbu.Data.World
{
    /// <summary>#308 D308-18 (3) "걸을 수 있는 땅에서만 갱신": when the fall reference height may follow the feet.
    /// Pure (no UnityEngine): the session, the editor rim probe and the offline table test call the same code.
    /// The numbers come from WorldTraversalTestProfile (data); this class holds none.</summary>
    public static class FallReferenceRule308
    {
        /// <summary>The slope limit the rule uses. field &lt;= 0 (or not finite) = the walker's own limit (CharacterController.slopeLimit).
        /// A field above the walker's limit is cut to it: ground the walker cannot walk is never "walkable" for the rule.</summary>
        public static float EffectiveSlopeDeg(float fieldDeg, float controllerDeg)
        {
            if (float.IsNaN(controllerDeg) || float.IsInfinity(controllerDeg) || controllerDeg < 0f) controllerDeg = 0f;
            if (float.IsNaN(fieldDeg) || float.IsInfinity(fieldDeg) || fieldDeg <= 0f) return controllerDeg;
            return fieldDeg < controllerDeg ? fieldDeg : controllerDeg;
        }

        /// <summary>normalY = the y of the unit ground normal. Walkable = its angle from straight up is within the limit.</summary>
        public static bool Walkable(float normalY, float limitDeg)
        {
            if (float.IsNaN(normalY)) return false;
            return normalY >= (float)Math.Cos(limitDeg * Math.PI / 180.0) - 1e-5f;
        }

        /// <summary>May the reference follow the feet this tick?
        /// walkableOnly false = the rule as it was coded before D308-18 (every grounded tick).
        /// grounded = the motor's locomotion-grounded flag (never true in a jump or a fall).
        /// seated = riding the vehicle: the walking reference never moves (the vehicle keeps its own).
        /// supportFound / supportNormalY / supportGap = the nearest support under the feet that passes the slope limit
        /// (supportFound false = nothing walkable under the feet: a steep face, a perch, air).</summary>
        public static bool Refreshes(bool walkableOnly, bool grounded, bool seated, bool supportFound, float supportNormalY, float supportGap,
            float maxGap, float fieldDeg, float controllerDeg)
        {
            if (seated || !grounded) return false;
            if (!walkableOnly) return true;
            if (!supportFound) return false;
            if (float.IsNaN(supportGap) || supportGap > maxGap) return false;
            return Walkable(supportNormalY, EffectiveSlopeDeg(fieldDeg, controllerDeg));
        }

        /// <summary>The order of WorldMacroPlaytestSession.TickTraversal: death is tested against the OLD reference, then the reference moves.</summary>
        public static bool Fatal(bool known, float reference, float feetY, float fatalHeight) => known && reference - feetY >= fatalHeight;
    }
}
