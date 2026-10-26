using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>Batch 4 F2 (ㅗ fan 번진다: 곤 논 몬 손 온) helpers: the fan carpet from a quarter out (the near band stays out of the shoulder camera's face, batch 3),
    /// a ground-hugging meander, a body coil. Presentation only.</summary>
    public static class Vol2FsB4F
    {
        /// <summary>A fan-shaped carpet lying on the ground, revealed out to <paramref name="reach"/> (0..1 of the depth), starting a quarter out.</summary>
        public static void FanCarpet(Vol2FsStrips s, Vector3 apex, Vector3 f, float depth, float half, float ground, float reach, float life, float rnd, Vector3 cam, float lift = 0.03f, float u0 = 0.25f, float widen = 1.05f, float u1 = 3f)
        {
            if (life <= 0.01f || reach <= u0 + 0.01f) return;
            var pts = new List<Vector3>(); var w = new List<float>(); var al = new List<float>(); var side = new List<Vector3>();
            float r0 = depth * u0, r1 = depth * reach;
            for (int k = 0; k <= 16; k++)
            {
                float v = (k / 16f) * 2f - 1f;
                Vector3 d = Quaternion.AngleAxis(v * half * widen * Mathf.Rad2Deg, Vector3.up) * f;
                var p = apex + d * (r0 + r1) * 0.5f; p.y = ground + lift;
                pts.Add(p); w.Add(r1 - r0); al.Add(life); side.Add(d);
            }
            s.Strip(pts, w, side, Color.white, al, rnd, cam, 0f, u1);
        }

        /// <summary>Control points of a meandering line lying on the ground from a to b (n points), lift above the ground.</summary>
        public static Vector3[] GroundLine(Vector3 a, Vector3 b, float ground, int n, float meander, float phase, float lift = 0.05f, float hump = 0f)
        {
            var pts = new Vector3[n];
            Vector3 d = b - a; d.y = 0f; Vector3 dir = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward; Vector3 side = Vector3.Cross(Vector3.up, dir);
            for (int k = 0; k < n; k++)
            {
                float u = k / (float)(n - 1);
                var p = Vector3.Lerp(a, b, u) + side * Mathf.Sin(u * 6.3f + phase) * meander * Mathf.Sin(u * Mathf.PI);
                p.y = ground + lift + hump * Mathf.Sin(u * Mathf.PI) + 0.04f * Mathf.Sin(u * 11f + phase);
                pts[k] = p;
            }
            return pts;
        }

        /// <summary>A helix round a body's lower part (feet -> <paramref name="upTo"/> of its height), <paramref name="turns"/> turns.</summary>
        public static Vector3[] Coil(Bounds b, float ground, int n, float a0, float turns, float upTo, float pad = 0.12f)
        {
            var pts = new Vector3[n];
            float rx = b.extents.x + pad, rz = b.extents.z + pad, hy = b.size.y * upTo;
            for (int k = 0; k < n; k++) { float u = k / (float)(n - 1), a = a0 + u * Mathf.PI * 2f * turns; pts[k] = new Vector3(b.center.x + Mathf.Cos(a) * rx, ground + 0.03f + u * hy, b.center.z + Mathf.Sin(a) * rz); }
            return pts;
        }

        /// <summary>Quadratic Bezier.</summary>
        public static Vector3 Bez(Vector3 a, Vector3 b, Vector3 c, float u) { float v = 1f - u; return v * v * a + 2f * v * u * b + u * u * c; }
    }
}
