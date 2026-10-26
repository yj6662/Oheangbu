using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App
{
    // D308-150 V12 free showcase batch 1 (2026-10-26, the user: "쇼케이스 방향이 낫다. 이 방식으로 다른 술식도 만들어 가자."): P0 가 마 사 아 · F0 노 모 소 오.
    // The two beat skeletons (a path shot and a fan sweep, read only from the executor's facts on the scope clock, like 나 / 고) and the extra materials /
    // meshes of the batch (lit stone and steel, procedural rock / slab / sword / shard meshes, a water surface for the particle shader).
    // Presentation only: nothing here is read by a rule, nothing decides or delays anything.

    /// <summary>The batch 1 toolbox: lit materials and procedural meshes, cached on the kit (no statics holding Unity objects).</summary>
    public static class Vol2FsB1
    {
        public static Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsMat.Make(tex, tint, intensity, addMix);

        /// <summary>URP Lit, opaque: stone (metallic 0) or steel (metallic ~1).</summary>
        public static Material Lit(Vol2FsKit kit, string tex, Color tint, float metallic, float smooth, Color emission = default, string emissionTex = null)
        {
            string key = "lit|" + tex + "|" + tint + "|" + metallic + "|" + smooth + "|" + emission + "|" + emissionTex;
            return kit.Custom(key, () =>
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Fs_Lit_" + tex };
                m.SetTexture("_BaseMap", kit.Tex(tex)); m.SetColor("_BaseColor", tint); m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smooth);
                if (emission.maxColorComponent > 0.001f)
                {
                    if (emissionTex != null) m.SetTexture("_EmissionMap", kit.Tex(emissionTex));
                    m.SetColor("_EmissionColor", emission); m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }
                return m;
            });
        }

        /// <summary>A water surface for the particle shader: the water sheet texture coloured by the water ramp, partly additive (glints) and partly covering.</summary>
        public static Material Water(Vol2FsKit kit, float intensity = 1.6f, float addMix = 0.35f, string tex = "fs_water")
        {
            var w = M(tex, Color.white, intensity, addMix); w.Ramp = "fs_ramp_water"; w.RampScale = 1.6f; w.NoiseAmt = 0.45f; w.NoiseScale = 1.4f; w.Erode = 0.2f; w.Soft = 0.15f;
            w.Scroll = new Vector2(-0.9f, 0f); w.Edge = new Color(0.9f, 1f, 1f, 1f) * 0.6f; w.EdgeWidth = 0.12f;
            return kit.Mat(w);
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------------
        // meshes
        // ---------------------------------------------------------------------------------------------------------------------------------------------

        /// <summary>A lumpy boulder / chunk: a UV sphere pushed by value noise, flat-ish facets (no smoothing across the big lumps).</summary>
        public static Mesh Rock(int seed, float radius, float squash = 0.85f, int lon = 14, int lat = 10)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>();
            for (int y = 0; y <= lat; y++)
                for (int x = 0; x <= lon; x++)
                {
                    float th = y / (float)lat * Mathf.PI, ph = x / (float)lon * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    int xi = x % lon;
                    float n = 0.72f + 0.42f * Vol2Fs.Hash(seed, xi * 31 + y * 7) + 0.18f * Mathf.Sin(ph * 3f + seed) * Mathf.Sin(th * 2f);
                    if (y == 0 || y == lat) n = 0.85f;
                    v.Add(new Vector3(d.x, d.y * squash, d.z) * radius * n);
                    uv.Add(new Vector2(x / (float)lon * 2f, y / (float)lat));
                }
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int a = y * (lon + 1) + x, b = a + 1, c = a + lon + 1, d = c + 1;
                    idx.Add(a); idx.Add(b); idx.Add(c); idx.Add(b); idx.Add(d); idx.Add(c);
                }
            return Facet(v, uv, idx, "FsRock");
        }

        /// <summary>A rough slab of earth / rock: a box with jittered corners and a broken top, its pivot at the middle of its bottom face (so it can rise and tilt).</summary>
        public static Mesh Slab(int seed, Vector3 size, float top = 1f)
        {
            float hx = size.x * 0.5f, hz = size.z * 0.5f, h = size.y; float tx = hx * top, tz = hz * Mathf.Lerp(1f, top, 0.6f), sh = (Vol2Fs.Hash(seed, 13) - 0.5f) * hx * (1f - top);
            Vector3 J(int k, float amt) => new Vector3(Vol2Fs.Hash(seed, k) - 0.5f, Vol2Fs.Hash(seed, k + 50) - 0.5f, Vol2Fs.Hash(seed, k + 90) - 0.5f) * amt;
            var c = new Vector3[8];
            c[0] = new Vector3(-hx, 0f, -hz) + J(1, 0.08f); c[1] = new Vector3(hx, 0f, -hz) + J(2, 0.08f); c[2] = new Vector3(hx, 0f, hz) + J(3, 0.08f); c[3] = new Vector3(-hx, 0f, hz) + J(4, 0.08f);
            c[4] = new Vector3(sh - tx * 0.9f, h * (0.8f + 0.3f * Vol2Fs.Hash(seed, 5)), -tz * 0.85f) + J(6, 0.12f); c[5] = new Vector3(sh + tx * 0.85f, h * (0.75f + 0.3f * Vol2Fs.Hash(seed, 7)), -tz * 0.9f) + J(8, 0.12f);
            c[6] = new Vector3(sh + tx * 0.9f, h * (0.9f + 0.2f * Vol2Fs.Hash(seed, 9)), tz * 0.8f) + J(10, 0.12f); c[7] = new Vector3(sh - tx * 0.8f, h * (0.85f + 0.25f * Vol2Fs.Hash(seed, 11)), tz * 0.9f) + J(12, 0.12f);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>();
            void Quad(int a, int b, int d, int e)
            {
                int s = v.Count; v.Add(c[a]); v.Add(c[b]); v.Add(c[d]); v.Add(c[e]);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                idx.Add(s); idx.Add(s + 2); idx.Add(s + 1); idx.Add(s); idx.Add(s + 3); idx.Add(s + 2);
            }
            Quad(0, 1, 5, 4); Quad(1, 2, 6, 5); Quad(2, 3, 7, 6); Quad(3, 0, 4, 7); Quad(4, 5, 6, 7); Quad(3, 2, 1, 0);
            return Facet(v, uv, idx, "FsSlab");
        }

        /// <summary>A straight double-edged sword along +Z (pivot at the grip's end): diamond-section blade with a point, a guard and a grip. Length ~1.55 m.</summary>
        public static Mesh Sword(float bladeLen = 1.15f, float width = 0.075f, float thick = 0.016f)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c2, float ua, float ub, float uc)
            {
                int s = v.Count; v.Add(a); v.Add(b); v.Add(c2); uv.Add(new Vector2(ua, 0f)); uv.Add(new Vector2(ub, 1f)); uv.Add(new Vector2(uc, 0.5f));
                idx.Add(s); idx.Add(s + 1); idx.Add(s + 2);
            }
            void Box(Vector3 mn, Vector3 mx)
            {
                Vector3[] p = { new Vector3(mn.x, mn.y, mn.z), new Vector3(mx.x, mn.y, mn.z), new Vector3(mx.x, mx.y, mn.z), new Vector3(mn.x, mx.y, mn.z),
                                new Vector3(mn.x, mn.y, mx.z), new Vector3(mx.x, mn.y, mx.z), new Vector3(mx.x, mx.y, mx.z), new Vector3(mn.x, mx.y, mx.z) };
                int[][] f = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 }, new[] { 1, 2, 6, 5 }, new[] { 0, 4, 7, 3 } };
                foreach (var q in f) { Tri(p[q[0]], p[q[1]], p[q[2]], 0, 1, 0.5f); Tri(p[q[0]], p[q[2]], p[q[3]], 0, 1, 0.5f); }
            }
            float g0 = 0.36f;                              // the grip length
            Box(new Vector3(-0.018f, -0.018f, 0f), new Vector3(0.018f, 0.018f, g0));                 // grip
            Box(new Vector3(-0.085f, -0.025f, g0), new Vector3(0.085f, 0.025f, g0 + 0.035f));        // guard
            float z0 = g0 + 0.035f, z1 = z0 + bladeLen * 0.86f, z2 = z0 + bladeLen;
            // blade: four long faces (diamond section), then the point
            Vector3 L0 = new Vector3(-width * 0.5f, 0f, z0), R0 = new Vector3(width * 0.5f, 0f, z0), U0 = new Vector3(0f, thick, z0), D0 = new Vector3(0f, -thick, z0);
            Vector3 L1 = new Vector3(-width * 0.42f, 0f, z1), R1 = new Vector3(width * 0.42f, 0f, z1), U1 = new Vector3(0f, thick * 0.8f, z1), D1 = new Vector3(0f, -thick * 0.8f, z1);
            Vector3 T = new Vector3(0f, 0f, z2);
            void Face(Vector3 a0, Vector3 b0, Vector3 a1, Vector3 b1) { Tri(a0, a1, b0, 0, 1, 0); Tri(b0, a1, b1, 0, 1, 1); }
            Face(L0, U0, L1, U1); Face(U0, R0, U1, R1); Face(R0, D0, R1, D1); Face(D0, L0, D1, L1);
            Tri(L1, T, U1, 0, 1, 0.5f); Tri(U1, T, R1, 0, 1, 0.5f); Tri(R1, T, D1, 0, 1, 0.5f); Tri(D1, T, L1, 0, 1, 0.5f);
            Tri(L0, U0, R0, 0, 1, 0.5f); Tri(L0, R0, D0, 0, 1, 0.5f);
            var m = new Mesh { name = "FsSword" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(idx, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>A long thin blade (the fan of knives): a flat lozenge along +Z, pivot at its back end, unit length.</summary>
        public static Mesh Blade()
        {
            var v = new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(-0.035f, 0f, 0.3f), new Vector3(0f, 0.008f, 0.3f), new Vector3(0.035f, 0f, 0.3f), new Vector3(0f, -0.008f, 0.3f), new Vector3(0f, 0f, 1f) };
            var tris = new[] { 0, 2, 1, 0, 3, 2, 0, 4, 3, 0, 1, 4, 1, 2, 5, 2, 3, 5, 3, 4, 5, 4, 1, 5 };
            var fv = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>();
            for (int k = 0; k < tris.Length; k++) { fv.Add(v[tris[k]]); uv.Add(new Vector2(v[tris[k]].z, k % 3 == 1 ? 1f : 0f)); idx.Add(k); }
            var m = new Mesh { name = "FsBlade" };
            m.SetVertices(fv); m.SetUVs(0, uv); m.SetTriangles(idx, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>A thin sharp shard (steel / stone splinter): a flattened irregular tetrahedron-ish prism, unit size.</summary>
        public static Mesh Shard(int seed)
        {
            Vector3 a = new Vector3(0f, 0f, 0.5f), b = new Vector3(-0.22f - 0.1f * Vol2Fs.Hash(seed, 1), 0f, -0.3f), c = new Vector3(0.2f + 0.1f * Vol2Fs.Hash(seed, 2), 0f, -0.4f);
            Vector3 u = new Vector3(0f, 0.05f, -0.05f), d = new Vector3(0f, -0.05f, -0.05f);
            var p = new[] { a, b, c, u, d };
            var tris = new[] { 0, 3, 1, 0, 2, 3, 1, 3, 2, 0, 1, 4, 0, 4, 2, 1, 2, 4 };
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>();
            for (int k = 0; k < tris.Length; k++) { v.Add(p[tris[k]]); uv.Add(new Vector2(k % 2, k % 3 * 0.5f)); idx.Add(k); }
            var m = new Mesh { name = "FsShard" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(idx, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static Mesh Facet(List<Vector3> v, List<Vector2> uv, List<int> idx, string name)
        {
            var fv = new List<Vector3>(idx.Count); var fu = new List<Vector2>(idx.Count); var fi = new List<int>(idx.Count);
            for (int k = 0; k < idx.Count; k++) { fv.Add(v[idx[k]]); fu.Add(uv[idx[k]]); fi.Add(k); }
            var m = new Mesh { name = name };
            m.SetVertices(fv); m.SetUVs(0, fu); m.SetTriangles(fi, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>A static mesh object placed under a parent with its own transform (rocks, slabs, swords): the mesh is the kit's (shared), not destroyed with it.</summary>
        public static MeshRenderer Prop(Transform parent, string name, Mesh mesh, Material mat, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off; r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        /// <summary>A UV sphere / dome written into a per-cast mesh with vertex colours (rgb = fresnel brightening toward the camera's grazing rim, a = life),
        /// for the water orb / dome / wave through the particle shader. <paramref name="shape"/> moves each unit-sphere point (wobble, collapse).</summary>
        public static void WaterSphere(Mesh mesh, Vector3 centre, float radius, float yMin, Vector3 camPos, float life, System.Func<Vector3, Vector3> shape, int lon = 28, int lat = 16, float rimBoost = 1.6f)
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var uv = new List<Vector3>(); var idx = new List<int>();
            for (int y = 0; y <= lat; y++)
                for (int x = 0; x <= lon; x++)
                {
                    float th = y / (float)lat * Mathf.PI, ph = x / (float)lon * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    if (d.y < yMin) d.y = yMin;
                    Vector3 p = centre + (shape != null ? shape(d) : d) * radius;
                    Vector3 view = (camPos - p).normalized;
                    float fres = 1f - Mathf.Abs(Vector3.Dot(d.normalized, view));
                    float b = Mathf.Lerp(0.55f, rimBoost, fres * fres);
                    v.Add(p); c.Add(new Color(b, b, b, life * Mathf.Lerp(0.6f, 1f, fres))); uv.Add(new Vector3(x / (float)lon * 3f, y / (float)lat, 0.37f));
                }
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int a = y * (lon + 1) + x, b = a + 1, cc = a + lon + 1, d = cc + 1;
                    idx.Add(a); idx.Add(b); idx.Add(cc); idx.Add(b); idx.Add(d); idx.Add(cc);
                }
            mesh.Clear(); mesh.SetVertices(v); mesh.SetColors(c); mesh.SetUVs(0, uv); mesh.SetTriangles(idx, 0); mesh.RecalculateBounds();
        }

        /// <summary>Burst strokes (the 나 hit's solid ink strokes, any colour of rim): directions in the plane facing the camera, kept low.</summary>
        public struct Stroke { public Vector3 Dir; public float Len, Width, Delay, Life, Rnd; }

        public static List<Stroke> Burst(int seed, int n, Vector3 side, Vector3 fwd, float len0, float len1, float w0, float w1, float upBias = 0.55f, float fwdBias = 0.35f)
        {
            var list = new List<Stroke>();
            for (int k = 0; k < n; k++)
            {
                float a = (k / (float)n + Vol2Fs.Hash(seed, k) * 0.05f) * Mathf.PI * 2f;
                float up = Mathf.Sin(a);
                Vector3 d = side * Mathf.Cos(a) * 1.15f + Vector3.up * (up > 0f ? up * upBias : up * 0.25f) + fwd * (fwdBias * (Vol2Fs.Hash(seed, k + 50) - 0.3f));
                list.Add(new Stroke { Dir = d.normalized, Len = len0 + (len1 - len0) * Vol2Fs.Hash(seed, k + 100), Width = w0 + (w1 - w0) * Vol2Fs.Hash(seed, k + 200), Delay = 0.025f * Vol2Fs.Hash(seed, k + 300), Life = 0.85f + 0.5f * Vol2Fs.Hash(seed, k + 400), Rnd = Vol2Fs.Hash(seed, k + 500) });
            }
            return list;
        }

        /// <summary>Writes burst strokes h seconds after the burst at <paramref name="at"/> (fat wet head at the centre, flung thin tip).</summary>
        public static void DrawBurst(Vol2FsStrips strip, List<Stroke> burst, Vector3 at, float h, Vector3 cam, float droopK = 0.35f, float grow0 = 0.12f)
        {
            var pts = new Vector3[7]; var w = new float[7]; var al = new float[7];
            foreach (var s in burst)
            {
                float a = h - s.Delay; if (a <= 0f) continue;
                float grow = Vol2Fs.EaseOutBack(a / grow0);
                float life = 1f - Vol2Fs.Smooth(0.25f, s.Life, a);
                if (life <= 0.01f) continue;
                Vector3 p0 = at + s.Dir * 0.15f;
                Vector3 droop = Vector3.down * droopK * s.Len * (0.3f + 0.7f * s.Rnd) * Vol2Fs.Smooth(0.05f, 0.6f, a);
                Vector3 curl = Vector3.Cross(s.Dir, (cam - at).normalized).normalized * s.Len * 0.35f * (s.Rnd - 0.5f);
                for (int k = 0; k < 7; k++)
                {
                    float u = k / 6f;
                    pts[k] = p0 + s.Dir * s.Len * grow * u + (droop + curl) * u * u;
                    w[k] = s.Width * (0.55f + 0.75f * Mathf.Sin(Mathf.Min(1f, u * 1.4f + 0.1f) * Mathf.PI)) * (1f - 0.55f * u);
                    al[k] = life * (1f - 0.3f * u * Vol2Fs.Smooth(0.2f, 0.8f, a));
                }
                strip.Strip(pts, w, null, Color.white, al, s.Rnd, cam, 0f, 1f);
            }
        }

        public static void Emit(ParticleSystem ps, Vector3 at, int count) { if (ps != null && count > 0) ps.Emit(new ParticleSystem.EmitParams { position = at, applyShapeToPosition = true }, count); }
        public static void EmitV(ParticleSystem ps, Vector3 at, Vector3 vel, float size = -1f, float life = -1f)
        {
            if (ps == null) return;
            var e = new ParticleSystem.EmitParams { position = at, velocity = vel, applyShapeToPosition = false };
            if (size > 0f) e.startSize = size;
            if (life > 0f) e.startLifetime = life;
            ps.Emit(e, 1);
        }
    }

    /// <summary>The path-shot skeleton of 가 / 마 / 사 / 아 (P0, ㅏ 한 번): charge at the brush tip until the launch (the emitter's StartUs) -> fly to the predicted first
    /// body (Vol2LegacyPathRecipe.SingleCue: the target and the impact clock the vol.2 path head takes) -> the hit at launch + impact clock -> after-state until
    /// the cast ends (+ the look's own linger). The flight follows a shallow lob (seen from behind the shoulder a straight line is end-on).</summary>
    public abstract class Vol2FsShot : Vol2FsRun
    {
        protected bool Launched, IsHit; protected float ImpactRel = -1f, HitAt = -1f, EndRel = -1f, LastFrac;
        protected Vector3 From, HitPoint, AimPoint; protected long TargetId; protected Transform TargetT; protected Bounds TB; protected bool HasTarget;
        protected int Seed;
        protected virtual float LobHeight => 0.6f;
        /// <summary>Seconds the look keeps going after the cast's end at most (particles still alive are allowed to finish before that).</summary>
        protected virtual float Linger => 3f;
        protected virtual float MinLinger => 0.8f;
        protected Vector3 Line(float s) => Vector3.Lerp(From, HitPoint, s) + Vector3.up * 4f * LobHeight * s * (1f - s);
        protected Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Fwd; }

        public override void Began(long nowUs) { Seed = (int)(Inst.Cast.Value & 0x7fff); OnBegin(); }
        protected abstract void OnBegin();
        /// <summary>Before the launch: <paramref name="k"/> 0..1 toward the launch.</summary>
        protected abstract void OnCharge(float t, float k);
        protected abstract void OnLaunch(long nowUs);
        /// <summary>In flight: f = 0..1 of the flight now, prev = last frame's.</summary>
        protected abstract void OnFly(float t, float f, float prev);
        protected abstract void OnHit(long nowUs);
        /// <summary>After the hit: h = seconds since the hit; ended = the cast has ended (its executor's own end).</summary>
        protected abstract void OnAfter(float t, float h, bool ended);
        protected virtual void OnCastEnded(float t) { }

        void Launch(long nowUs)
        {
            Launched = true;
            var cue = Vol2LegacyPathRecipe.SingleCue(Inst, new Vol2LegacyCue { Letter = Inst.Log.Glyph.Letter, Body = Inst.Log.Glyph.Body, Origin = Inst.Log.Spec.Origin, Forward = Inst.Log.Spec.Forward, PathLength = Inst.Log.Spec.PathLength }, nowUs);
            From = Tip();
            ImpactRel = Now(nowUs) + Mathf.Max(0.02f, cue.ImpactClock);
            HasTarget = cue.HasTarget; TargetId = cue.TargetId;
            if (HasTarget)
            {
                TargetT = TargetTransform(TargetId);
                var t = Target(TargetId);
                TB = BodyBounds(TargetT, cue.TargetPoint, t != null ? t.Radius : 0.5f);
                AimPoint = TB.center + Vector3.up * TB.extents.y * 0.15f;
                Vector3 toward = From - AimPoint; toward.y = 0f;
                HitPoint = AimPoint + toward.normalized * Mathf.Min(TB.extents.x, TB.extents.z) * 0.6f;
            }
            else { AimPoint = cue.FallbackPoint + Vector3.up * 1.0f; HitPoint = AimPoint; TB = new Bounds(cue.FallbackPoint + Vector3.up * 0.6f, Vector3.one * 1.2f); }
            Note = "launch target=" + TargetId + " impact=" + cue.ImpactClock.ToString("0.000") + "s";
            OnLaunch(nowUs);
        }

        protected float Frac(long nowUs) => ImpactRel > LaunchRel ? Mathf.Clamp01((Now(nowUs) - LaunchRel) / (ImpactRel - LaunchRel)) : 1f;

        /// <summary>The struck body's centre now (it may be shoved / walk), else where it was.</summary>
        protected Vector3 BodyNow() => TargetT != null ? BodyBounds(TargetT, TargetT.position, 0.5f).center : TB.center;
        protected Vector3 FeetNow() => TargetT != null ? TargetT.position : new Vector3(TB.center.x, Ground, TB.center.z);

        /// <summary>r2: the struck body is gone (destroyed - e.g. the lab capture's teardown, which aborts the casts but leaves the presenter model's instances
        /// alive): the look hides at once instead of lingering into the next scene.</summary>
        protected bool Gone;
        bool _hadTarget;

        public override void Tick(long nowUs)
        {
            if (Root == null || Gone) return;
            if (_hadTarget && TargetT == null) { Gone = true; Root.SetActive(false); Note += " | body gone"; return; }
            float t = Now(nowUs);
            if (!Launched)
            {
                float k = LaunchRel > 0.02f ? Mathf.Clamp01(t / LaunchRel) : 1f;
                OnCharge(t, k);
                if (t >= LaunchRel - 1e-4f) { Launch(nowUs); _hadTarget = TargetT != null; } else return;
            }
            if (!IsHit)
            {
                float f = Frac(nowUs);
                OnFly(t, f, LastFrac);
                LastFrac = f;
                if (t >= ImpactRel) { IsHit = true; HitAt = t; OnHit(nowUs); }
                else return;
            }
            bool ended = Inst.CastIsEnded || Inst.Log.Finished;
            if (ended && EndRel < 0f) { EndRel = t; OnCastEnded(t); }
            OnAfter(t, t - HitAt, ended);
        }

        public override bool Done(long nowUs)
        {
            if (Root == null || Gone) return true;
            float t = Now(nowUs);
            if (!IsHit) return t > LaunchRel + 3f;
            if (EndRel < 0f) return false;
            if (t > EndRel + Linger) return true;
            return t > EndRel + MinLinger && !ParticlesAlive();
        }
    }

    /// <summary>The fan-sweep skeleton of 노 / 모 / 소 / 오 (F0, ㅗ 한 번): charge at the brush tip until the launch -> the sweep over the fan of the vol.2 spec (its
    /// apex / forward / depth / half angle) -> every body the executor touched (Contact) gets its own struck state, timed by where it stands in the fan (the
    /// contact already happened: presentation only) -> the after-state until the cast ends (+ the look's own linger).</summary>
    public abstract class Vol2FsSweep : Vol2FsRun
    {
        protected sealed class Struck { public long Id; public Transform T; public bool HadT; public Bounds B; public Vector3 Feet; public float At, Radial, Angle; public bool Started, Ended; public object Data; }
        protected readonly List<Struck> Hits = new List<Struck>();
        protected bool Launched; protected float LaunchAt = -1f, EndRel = -1f;
        protected Vector3 Apex, F; protected float Depth, Half; protected int Seed;
        protected virtual float Linger => 3f;
        protected virtual float MinLinger => 1.2f;
        protected Vector3 Dir(float ang) => Quaternion.AngleAxis(ang * Mathf.Rad2Deg, Vector3.up) * F;
        /// <summary>The struck body's start (seconds after the launch) from its radial / angular place in the fan.</summary>
        protected abstract float StruckAt(float radial01, float angle01);

        public override void Began(long nowUs)
        {
            var spec = Inst.Log.Spec;
            Apex = spec.Origin; F = spec.Forward; F.y = 0f; F = F.sqrMagnitude > 1e-6f ? F.normalized : Fwd; Depth = Mathf.Max(1f, spec.FanDepth); Half = Mathf.Max(0.1f, spec.FanHalfAngleRad);
            Seed = (int)(Inst.Cast.Value & 0x7fff);
            OnBegin();
        }
        protected abstract void OnBegin();
        protected abstract void OnCharge(float t, float k);
        protected abstract void OnLaunch(long nowUs);
        /// <summary>a = seconds since the launch.</summary>
        protected abstract void OnSweep(float t, float a, bool ended);
        protected abstract void OnStruck(Struck s);
        protected virtual void OnCastEnded(float t) { }
        /// <summary>The look's own end after the launch (the cast may end earlier; then this decides).</summary>
        protected abstract float LookSeconds { get; }

        public override void Contact(Vol2ContactRecord rec)
        {
            if (!rec.BudgetAccepted || Hits.Exists(s => s.Id == rec.TargetId)) return;
            var t = Target(rec.TargetId); var tr = TargetTransform(rec.TargetId);
            if (t == null) return;
            var s = new Struck { Id = rec.TargetId, T = tr, HadT = tr != null, Feet = t.Position, B = BodyBounds(tr, t.Position, t.Radius) };
            Vector3 d = t.Position - Apex; d.y = 0f;
            s.Radial = Mathf.Clamp01(d.magnitude / Depth);
            s.Angle = Mathf.Clamp01((Vector3.SignedAngle(F, d, Vector3.up) * Mathf.Deg2Rad / Half + 1f) * 0.5f);
            s.At = StruckAt(s.Radial, s.Angle);
            Hits.Add(s);
            OnStruck(s);
        }

        protected Vector3 CentreNow(Struck s) => s.T != null ? BodyBounds(s.T, s.T.position, 0.5f).center : s.B.center;

        /// <summary>r2: a struck body is gone (destroyed - the lab capture's teardown): the look hides at once (see Vol2FsShot.Gone).</summary>
        protected bool Gone;

        public override void Tick(long nowUs)
        {
            if (Root == null || Gone) return;
            foreach (var h in Hits) if (h.HadT && h.T == null) { Gone = true; Root.SetActive(false); Note += " | body gone"; return; }
            float t = Now(nowUs);
            if (!Launched)
            {
                float k = LaunchRel > 0.02f ? Mathf.Clamp01(t / LaunchRel) : 1f;
                OnCharge(t, k);
                if (t >= LaunchRel - 1e-4f) { Launched = true; LaunchAt = t; OnLaunch(nowUs); } else return;
            }
            bool ended = Inst.CastIsEnded || Inst.Log.Finished;
            if (ended && EndRel < 0f) { EndRel = t; OnCastEnded(t); }
            OnSweep(t, t - LaunchAt, ended);
        }

        public override bool Done(long nowUs)
        {
            if (Root == null || Gone) return true;
            float t = Now(nowUs);
            if (!Launched) return t > LaunchRel + 3f;
            float a = t - LaunchAt;
            if (a < LookSeconds) return false;
            if (EndRel < 0f) return false;
            if (t > EndRel + Linger && a > LookSeconds + 2.5f) return true;
            return t > EndRel + MinLinger && !ParticlesAlive();
        }
    }
}
