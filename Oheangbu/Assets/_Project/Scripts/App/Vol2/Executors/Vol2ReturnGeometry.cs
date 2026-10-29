using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>A point as a target: the carry of 앙 samples the path corridor with it, so a material of ANY footprint kind is tested by its own Touches.</summary>
    public sealed class Vol2ProbeTarget : IVol2Target
    {
        public long Id => 0;
        public long LifeRevision => 0;
        public Vector3 Position { get; set; }
        public Vector3 Previous => Position;
        public float Radius { get; set; }
        public bool Alive => true;
        public string Name => "probe";
    }

    /// <summary>Slice 7 geometry on the plane (x, z), pure: the orbit of 겅 넝 멍 성 엉 and the sweeps of the returning shapes. Angles are measured from the aim, positive
    /// toward the caster's right (clockwise from above): 0 = straight ahead, pi/2 = on the right.</summary>
    public static class Vol2ReturnGeometry
    {
        public const float Tau = 6.28318530718f;

        /// <summary>The angle of a point around the centre (0 = along forward, + toward the right) and its distance on the plane.</summary>
        public static float AngleOf(Vector3 centre, Vector3 forward, Vector3 pos, out float distance)
        {
            Vector3 rel = pos - centre; rel.y = 0f;
            distance = rel.magnitude;
            Vector3 f = Vol2ShapeGeometry.FlatUnit(forward);
            Vector3 right = Vector3.Cross(Vector3.up, f);
            return Mathf.Atan2(Vector3.Dot(rel, right), Vector3.Dot(rel, f));
        }

        /// <summary>The orbit point travels the circle of radius <paramref name="rho"/> from angle <paramref name="psi0"/> to <paramref name="psi1"/> (psi1 >= psi0, unbounded:
        /// 2 pi is one full turn). Does it touch the body disc at <paramref name="pos"/>? A full turn or more touches everything on the band. psiHit = the angle of the orbit
        /// when it first touched, clamped into the swept interval (its turn number is floor(psiHit / 2 pi)).</summary>
        public static bool OrbitTouches(Vector3 centre, Vector3 forward, float rho, float halfWidth, float psi0, float psi1, Vector3 pos, float bodyRadius, out float psiHit)
        {
            psiHit = psi1;
            float reach = halfWidth + bodyRadius;
            float beta = AngleOf(centre, forward, pos, out float d);
            if (Mathf.Abs(d - rho) > reach) return false;
            if (psi1 - psi0 >= Tau) return true;
            float delta = d <= reach ? Mathf.PI : Mathf.Asin(Mathf.Clamp01(reach / d));
            float lo = psi0 - delta, hi = psi1 + delta;
            int k = Mathf.CeilToInt((lo - beta) / Tau);
            float candidate = beta + k * Tau;
            if (candidate > hi) return false;
            psiHit = Mathf.Clamp(candidate, psi0, psi1);
            return true;
        }

        public static Vector3 PointOnOrbit(Vector3 centre, Vector3 forward, float rho, float psi)
        {
            Vector3 f = Vol2ShapeGeometry.FlatUnit(forward);
            Vector3 right = Vector3.Cross(Vector3.up, f);
            return centre + (f * Mathf.Cos(psi) + right * Mathf.Sin(psi)) * rho;
        }

        /// <summary>Is the footprint touched by a path corridor (half width <paramref name="halfWidth"/>) along the axis between <paramref name="aLo"/> and <paramref name="aHi"/>
        /// metres? Sampled every 0.25 m from the far end, so the answer is the entry the returning head meets FIRST (largest along). Any footprint kind answers by its own Touches.</summary>
        public static bool CorridorEntry(Vol2Footprint footprint, Vector3 origin, Vector3 forward, float aLo, float aHi, float halfWidth, long nowUs, out float along)
        {
            along = 0f;
            if (footprint == null || aHi < aLo) return false;
            var probe = new Vol2ProbeTarget { Radius = halfWidth };
            Vector3 f = Vol2ShapeGeometry.FlatUnit(forward);
            int steps = Mathf.Max(1, Mathf.CeilToInt((aHi - aLo) / 0.25f));
            for (int i = 0; i <= steps; i++)
            {
                float a = aHi - (aHi - aLo) * i / steps;
                probe.Position = origin + f * a;
                if (footprint.Touches(probe, nowUs)) { along = a; return true; }
            }
            return false;
        }

        /// <summary>Fan whose apex has travelled <paramref name="apexAlong"/> metres ahead of the cast point.</summary>
        public static bool FanTouches(Vol2ShapeSpec spec, float apexAlong, Vector3 pos, float radius)
            => Vol2ShapeGeometry.FanHit(spec.Origin + spec.Forward * apexAlong, spec.Forward, spec.FanHalfAngleRad, spec.FanDepth, pos, radius);

        /// <summary>Ring whose centre line has radius <paramref name="centreRadius"/> (the band keeps the spec's thickness).</summary>
        public static bool RingTouches(Vol2ShapeSpec spec, float centreRadius, Vector3 from, Vector3 to, float radius)
        {
            float half = spec.RingThickness * 0.5f;
            return Vol2ShapeGeometry.RingCrossed(spec.Origin, Mathf.Max(0f, centreRadius - half), centreRadius + half, from, to, radius);
        }

        public static float FlatDistance(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return (a - b).magnitude; }
    }
}
