using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>Batch 4 helpers of the A2 looks (건 넌 먼 선 언): ribbons wound round a body, S-curved hop paths, flat ground rings. Presentation only.</summary>
    public static class Vol2FsB4A
    {
        /// <summary>A ribbon wound round a body: an elliptic arc (radii rx, rz) at height y, from angle a0 over <paramref name="span"/> radians, its width
        /// standing vertical (side = up, a band wrapping the body) and its alpha fading to both ends.</summary>
        public static void Wrap(Vol2FsStrips s, Vector3 c, float rx, float rz, float y, float a0, float span, float width, float life, float rnd, Vector3 cam, float rise = 0f, int n = 18, float tilt = 0f)
        {
            if (life <= 0.01f || span <= 0.02f) return;
            var pts = new Vector3[n]; var w = new float[n]; var al = new float[n]; var side = new Vector3[n];
            for (int k = 0; k < n; k++)
            {
                float v = k / (float)(n - 1), a = a0 + span * v;
                float yy = y + rise * v + Mathf.Sin(a) * tilt;
                pts[k] = new Vector3(c.x + Mathf.Cos(a) * rx, yy, c.z + Mathf.Sin(a) * rz);
                w[k] = width * Mathf.Sin(Mathf.Clamp01(v) * Mathf.PI) * 0.85f + width * 0.15f;
                al[k] = life * (0.55f + 0.45f * Mathf.Sin(v * Mathf.PI));
                side[k] = Vector3.up;
            }
            s.Strip(pts, w, side, Color.white, al, rnd, cam, 0f, 1f);
        }

        /// <summary>The point at u of an S-curved hop a -> b (lob up, lateral swing of <paramref name="swing"/> m changing side half way).</summary>
        public static Vector3 SPath(Vector3 a, Vector3 b, float lob, float swing, float u)
        {
            Vector3 d = b - a; d.y = 0f; Vector3 side = d.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, d.normalized) : Vector3.right;
            return Vector3.Lerp(a, b, u) + Vector3.up * 4f * lob * u * (1f - u) + side * swing * Mathf.Sin(u * Mathf.PI * 2f) * Mathf.Sin(u * Mathf.PI);
        }

        /// <summary>A ribbon along an S-path, drawn from u0 to u1 (a travelling / growing segment), camera-facing, width tapering to both ends.</summary>
        public static void SRibbon(Vol2FsStrips s, Vector3 a, Vector3 b, float lob, float swing, float u0, float u1, float width, float life, float rnd, Vector3 cam, int n = 16, float wobble = 0f, float phase = 0f)
        {
            if (life <= 0.01f || u1 - u0 < 0.005f) return;
            var pts = new Vector3[n]; var w = new float[n]; var al = new float[n];
            for (int k = 0; k < n; k++)
            {
                float v = k / (float)(n - 1), u = Mathf.Lerp(u0, u1, v);
                pts[k] = SPath(a, b, lob, swing, u) + Vector3.up * Mathf.Sin(u * 11f + phase) * wobble * Mathf.Sin(u * Mathf.PI);
                w[k] = width * (0.3f + 0.7f * Mathf.Sin(Mathf.Clamp01(v) * Mathf.PI));
                al[k] = life * (0.5f + 0.5f * v);
            }
            s.Strip(pts, w, null, Color.white, al, rnd, cam, u0, u1);
        }

        /// <summary>A ring blade (chakram) in the XZ plane, radius 1 outside: a flat steel band thick at the inner rim, sharpening to a knife edge outside, with
        /// <paramref name="teeth"/> hooked points. Pivot at its centre.</summary>
        public static Mesh Chakram(int teeth = 8, float inner = 0.55f)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>();
            int n = teeth * 6;
            for (int k = 0; k <= n; k++)
            {
                float a = k / (float)n * Mathf.PI * 2f, u = k / (float)n;
                float tooth = Mathf.Repeat(k / 6f, 1f);
                float ro = 1f + 0.18f * Mathf.Pow(tooth, 3f);                  // a hooked point every sixth step
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(d * inner + Vector3.up * 0.045f); uv.Add(new Vector2(u * 4f, 0f));
                v.Add(d * inner - Vector3.up * 0.045f); uv.Add(new Vector2(u * 4f, 0.2f));
                v.Add(d * ((inner + ro) * 0.5f) + Vector3.up * 0.03f); uv.Add(new Vector2(u * 4f, 0.5f));
                v.Add(d * ((inner + ro) * 0.5f) - Vector3.up * 0.03f); uv.Add(new Vector2(u * 4f, 0.6f));
                v.Add(d * ro); uv.Add(new Vector2(u * 4f, 1f));
            }
            for (int k = 0; k < n; k++)
            {
                int a = k * 5, b = (k + 1) * 5;
                void Q(int p0, int p1, int q0, int q1) { idx.Add(a + p0); idx.Add(b + q0); idx.Add(a + p1); idx.Add(a + p1); idx.Add(b + q0); idx.Add(b + q1); }
                Q(2, 0, 2, 0);        // top inner face
                Q(4, 2, 4, 2);        // top bevel to the edge
                Q(1, 3, 1, 3);        // bottom inner face
                Q(3, 4, 3, 4);        // bottom bevel
                Q(0, 1, 0, 1);        // inner rim
            }
            var fv = new List<Vector3>(idx.Count); var fu = new List<Vector2>(idx.Count); var fi = new List<int>(idx.Count);
            for (int k = 0; k < idx.Count; k++) { fv.Add(v[idx[k]]); fu.Add(uv[idx[k]]); fi.Add(k); }
            // two-sided (the URP Lit steel culls back faces): every triangle again, reversed
            int c = fv.Count;
            for (int k = 0; k < c; k += 3) { fv.Add(fv[k]); fu.Add(fu[k]); fv.Add(fv[k + 2]); fu.Add(fu[k + 2]); fv.Add(fv[k + 1]); fu.Add(fu[k + 1]); fi.Add(c + k); fi.Add(c + k + 1); fi.Add(c + k + 2); }
            var m = new Mesh { name = "FsChakram" };
            m.SetVertices(fv); m.SetUVs(0, fu); m.SetTriangles(fi, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>r3 (the user's reference 2026-10-26 "팬텀 블레이드 제로의 vfx 효과를 참고해", Docs/Research/VFX-REF-PHANTOM-BLADE-ZERO.md, principles 2 + 5): the swing arc
        /// through the brush tip's REAL recent path as a blade-edged crescent - thin and sharp at its old end, widening, a short taper at the tip - optionally LAGGED
        /// (a phantom afterimage trailing <paramref name="lag"/> s behind). Nothing is simulated: only the recorded tip positions.</summary>
        public static void BladeTrail(Vol2FsStrips s, IList<(Vector3 P, float T)> path, float t, float span, float width, float life, float rnd, Vector3 cam, float lag = 0f, float minLen = 0.35f)
        {
            if (life <= 0.01f || path == null || path.Count < 3) return;
            var pts = new List<Vector3>(); var ages = new List<float>();
            foreach (var p in path) { float age = t - p.T - lag; if (age < 0f || age > span) continue; pts.Add(p.P); ages.Add(age); }
            if (pts.Count < 3 || (pts[pts.Count - 1] - pts[0]).magnitude < minLen) return;
            var w = new float[pts.Count]; var al = new float[pts.Count];
            for (int k = 0; k < pts.Count; k++)
            {
                float u = 1f - ages[k] / span;                                        // 0 = the oldest point, 1 = the newest
                w[k] = width * Mathf.Sin(Mathf.Pow(Mathf.Clamp01(u), 1.35f) * Mathf.PI * 0.9f);
                al[k] = life * (0.25f + 0.75f * u);
            }
            s.Strip(pts, w, null, Color.white, al, rnd, cam, 0f, 1f);
        }

        /// <summary>The two phantom afterimages of the swing (lag 0.04 s at alpha .35, 0.08 s at .18) into an accent-coloured translucent strip set.</summary>
        public static void Phantoms(Vol2FsStrips s, IList<(Vector3 P, float T)> path, float t, float span, float width, float life, Vector3 cam)
        {
            BladeTrail(s, path, t, span, width * 1.1f, life * 0.35f, 0.61f, cam, 0.04f);
            BladeTrail(s, path, t, span, width * 1.2f, life * 0.18f, 0.83f, cam, 0.08f);
        }

        /// <summary>The phantom material: the body's accent colour, translucent, soft (the particle shader, mostly additive).</summary>
        public static Material PhantomMat(Vol2FsKit kit, Color accent, float intensity = 1.3f)
        {
            var m = Vol2FsMat.Make("fs_streak", accent, intensity, 0.75f); m.NoiseAmt = 0.35f; m.Erode = 0.22f; m.Soft = 0.2f; m.Scroll = new Vector2(-1.5f, 0f);
            return kit.Mat(m);
        }

        /// <summary>A flat ring (annulus) on the ground at c, radius r, width w, over angles a0..a0+span.</summary>
        public static void GroundArc(Vol2FsStrips s, Vector3 c, float ground, float r, float w, float a0, float span, float life, float rnd, Vector3 cam, int n = 24)
        {
            if (life <= 0.01f) return;
            var pts = new Vector3[n]; var wd = new float[n]; var al = new float[n]; var side = new Vector3[n];
            for (int k = 0; k < n; k++)
            {
                float v = k / (float)(n - 1), a = a0 + span * v;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                pts[k] = new Vector3(c.x, ground + 0.03f, c.z) + d * r; wd[k] = w; al[k] = life; side[k] = d;
            }
            s.Strip(pts, wd, side, Color.white, al, rnd, cam, 0f, 1f);
        }
    }
}
