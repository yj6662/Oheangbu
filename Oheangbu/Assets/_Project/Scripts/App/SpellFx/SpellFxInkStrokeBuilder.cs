using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    // 2026-10-31 (D308-161 / D308-162, PLAN-308-INK-STROKE-SPELLS sections 1, 2.1, 4.2; the user: "방어는 그런 육각면으로 하고, 공격은 다른 투사체를 사용해야지" -> "S1 몸 + S3 끝" -> "구현 시작, 97은 영상에서"):
    // the ink-stroke ribbon builder of the 110 attack glyphs. Nothing here is a singleton and nothing holds state between casts: the static members are readonly tables and pure helpers; every
    // mesh, curve and property block lives in an instance a part owns. All numbers are TEST (no measured look yet).
    //
    // SHADER CONTRACT (Oheangbu/SpellFx/InkStrokeA and .../InkStrokeB, written by another lane to the same contract):
    //   uv0.x = along the stroke, 0 = the tail (the brush side) .. 1 = the head (the target side) - ALWAYS 1 at the drawn head (a stroke that is still growing is built only as far as it has got,
    //           so the shader's wet head sits at the geometry's end; the part leaves _Reveal at 1);
    //   uv0.y = across, 0 .. 1 (0.5 = the centre line, where the core line is);
    //   uv1.x = the stroke's seed (0..1), uv1.y = the layer: 0 main stroke, 0.5 afterimage, 1 ground bleed mark;
    //   colour r = core-line weight (x _CoreK), g = wetness (0 dry .. 1 wet), b = reserved (0), a = alpha / dying.
    //   Per renderer group (a MaterialPropertyBlock on ONE shared material): _Reveal, _TailCut, _Dry, _CoreK, _AfterAlpha.
    // Every renderer is on layer 0 with shadows off (the cutout stroke writes depth: PLAN 4.3 - never the VfxAfterFog layer).

    /// <summary>One stroke's shape in metres (the width profile of 오의's head / torso / tail, PLAN 1.1 #3): a pressed wet head, a squeezed body, a thin dry tail, a point or a blunt cap at each end and a
    /// sideways flick of the point (삐침). Widths are full widths. Profiles are measured in METRES from each end, so a long stroke (강's loop) keeps its torso width in the middle.</summary>
    public struct InkStrokeShape
    {
        public float Length;                 // the flying stroke's length (m); 강 keeps its tail at the brush and is longer
        public float HeadW, TorsoW, TailW;   // full widths (m)
        public float HeadZone, TailZone;     // metres from the head / tail over which the width runs to the torso's
        public float HeadPoint, TailPoint;   // metres over which an end narrows to nothing (0 = a blunt end)
        public bool HeadRound;               // the head's end is a round cap (sqrt profile) rather than a point (smooth profile)
        public float FlickHead, FlickTail;   // the end's sideways bend at its very tip, as a fraction of the local width (삐침)
        public float Wet0, Wet1;             // colour g at the tail / the head
        public float Core;                   // colour r on the stroke body
        public int Split;                    // >1: the head's last SplitLen metres are drawn as this many bristle strands converging to one point (목's awl)
        public float SplitLen;

        public InkStrokeShape Scaled(float w, float l)
        {
            var s = this;
            s.HeadW *= w; s.TorsoW *= w; s.TailW *= w;
            s.Length *= l; s.HeadZone *= l; s.TailZone *= l; s.SplitLen *= l;
            return s;
        }

        /// <summary>The full width at <paramref name="dHead"/> metres from the head and <paramref name="dTail"/> from the tail (0 at a pointed end).</summary>
        public float WidthAt(float dHead, float dTail)
        {
            float w = TorsoW;
            if (HeadZone > 1e-4f && dHead < HeadZone) w = Mathf.Lerp(HeadW, TorsoW, InkMath.Smooth(dHead / HeadZone));
            if (TailZone > 1e-4f && dTail < TailZone) w = Mathf.Min(w, Mathf.Lerp(TailW, TorsoW, InkMath.Smooth(dTail / TailZone)));
            if (HeadPoint > 1e-4f && dHead < HeadPoint)
            {
                float q = Mathf.Clamp01(dHead / HeadPoint);
                w *= HeadRound ? Mathf.Sqrt(q) : InkMath.Smooth(q) * 0.85f + 0.15f * q;
            }
            if (TailPoint > 1e-4f && dTail < TailPoint) w *= InkMath.Smooth(Mathf.Clamp01(dTail / TailPoint));
            return Mathf.Max(0f, w);
        }

        /// <summary>The centre line's sideways shift (in widths) near an end: the point bends over like a brush's flick.</summary>
        public float FlickAt(float dHead, float dTail)
        {
            float f = 0f;
            if (HeadPoint > 1e-4f && dHead < HeadPoint) { float q = 1f - Mathf.Clamp01(dHead / HeadPoint); f += FlickHead * q * q; }
            if (TailPoint > 1e-4f && dTail < TailPoint) { float q = 1f - Mathf.Clamp01(dTail / TailPoint); f -= FlickTail * q * q; }
            return f;
        }
    }

    /// <summary>How a ribbon's width is laid at each sample.</summary>
    public enum InkFaceMode
    {
        /// <summary>In a fixed plane (normal N): width = N x tangent. The flying stroke (35 deg off the camera-facing plane, RiposteStroke308Path's yaw).</summary>
        Plane,
        /// <summary>Turned to the camera about the tangent (a thread, a cage branch, a tongue): never seen edge-on.</summary>
        Camera,
        /// <summary>Lying on a body round a vertical axis (the coil, a running drip): face = the horizontal outward direction.</summary>
        Radial,
        /// <summary>Lying on the ground (a bleed mark, a crack): face = up.</summary>
        Ground,
    }

    public struct InkFace
    {
        public InkFaceMode Mode;
        public Vector3 N;        // Plane: the plane normal; Radial: the axis point (world; only x, z read)
        public Vector3 Cam;      // the camera (world): every face is turned to it

        public static InkFace Plane(Vector3 n, Vector3 cam) => new InkFace { Mode = InkFaceMode.Plane, N = n.normalized, Cam = cam };
        public static InkFace Camera(Vector3 cam) => new InkFace { Mode = InkFaceMode.Camera, Cam = cam };
        public static InkFace Radial(Vector3 axis, Vector3 cam) => new InkFace { Mode = InkFaceMode.Radial, N = axis, Cam = cam };
        public static InkFace OnGround(Vector3 cam) => new InkFace { Mode = InkFaceMode.Ground, Cam = cam };

        /// <summary>The unit width direction and the unit face normal (turned to the camera) at point <paramref name="p"/> with tangent <paramref name="t"/>.</summary>
        public void At(Vector3 p, Vector3 t, out Vector3 side, out Vector3 normal)
        {
            Vector3 toCam = Cam - p;
            switch (Mode)
            {
                case InkFaceMode.Plane: normal = N; break;
                case InkFaceMode.Radial: { Vector3 o = p - N; o.y = 0f; normal = o.sqrMagnitude > 1e-6f ? o.normalized : (toCam.sqrMagnitude > 1e-6f ? toCam.normalized : Vector3.back); break; }
                case InkFaceMode.Ground: normal = Vector3.up; break;
                default: normal = toCam.sqrMagnitude > 1e-6f ? toCam.normalized : Vector3.back; break;
            }
            side = Vector3.Cross(normal, t);
            if (side.sqrMagnitude < 1e-8f) side = Vector3.Cross(Vector3.up, t);
            if (side.sqrMagnitude < 1e-8f) side = Vector3.right;
            side.Normalize();
            if (Mode == InkFaceMode.Camera || Mode == InkFaceMode.Plane)
            {
                // the true face normal of the ribbon (a camera ribbon is billboarded about its tangent only)
                Vector3 n2 = Vector3.Cross(t, side);
                if (n2.sqrMagnitude > 1e-8f) normal = n2.normalized;
            }
            if (Vector3.Dot(normal, toCam) < 0f) normal = -normal;
        }
    }

    /// <summary>A polyline by arclength (world space): the path of a stroke. At most a few dozen points.</summary>
    public sealed class InkCurve
    {
        readonly List<Vector3> _p = new List<Vector3>(48);
        readonly List<float> _s = new List<float>(48);

        public int Count => _p.Count;
        public float Length => _s.Count > 0 ? _s[_s.Count - 1] : 0f;
        public Vector3 First => _p.Count > 0 ? _p[0] : Vector3.zero;
        public Vector3 Last => _p.Count > 0 ? _p[_p.Count - 1] : Vector3.zero;
        public Vector3 this[int i] => _p[i];
        public void Clear() { _p.Clear(); _s.Clear(); }

        public void Add(Vector3 p)
        {
            if (_p.Count == 0) { _p.Add(p); _s.Add(0f); return; }
            float d = Vector3.Distance(_p[_p.Count - 1], p);
            if (d < 1e-4f) return;
            _p.Add(p); _s.Add(_s[_s.Count - 1] + d);
        }

        public void CopyFrom(InkCurve o) { _p.Clear(); _s.Clear(); _p.AddRange(o._p); _s.AddRange(o._s); }

        int Seg(float s)
        {
            int n = _p.Count;
            for (int i = 0; i < n - 1; i++) if (s <= _s[i + 1]) return i;
            return Mathf.Max(0, n - 2);
        }

        /// <summary>The point <paramref name="s"/> metres from the start (clamped).</summary>
        public Vector3 At(float s)
        {
            int n = _p.Count;
            if (n == 0) return Vector3.zero;
            if (n == 1) return _p[0];
            s = Mathf.Clamp(s, 0f, Length);
            int i = Seg(s);
            float l = _s[i + 1] - _s[i];
            return l < 1e-6f ? _p[i] : Vector3.Lerp(_p[i], _p[i + 1], (s - _s[i]) / l);
        }

        /// <summary>The unit direction of travel there (a short central difference: smooth across the polyline's corners).</summary>
        public Vector3 Dir(float s)
        {
            if (_p.Count < 2) return Vector3.forward;
            float L = Length, h = Mathf.Min(0.04f, L * 0.25f);
            Vector3 d = At(Mathf.Min(L, s + h)) - At(Mathf.Max(0f, s - h));
            if (d.sqrMagnitude < 1e-10f) { int i = Seg(Mathf.Clamp(s, 0f, L)); d = _p[i + 1] - _p[i]; }
            return d.sqrMagnitude < 1e-10f ? Vector3.forward : d.normalized;
        }

        /// <summary>Drops everything beyond <paramref name="s"/> metres (the point at s stays as the new end).</summary>
        public void TruncateTo(float s)
        {
            if (_p.Count < 2 || s >= Length) return;
            s = Mathf.Max(s, 0f);
            Vector3 end = At(s);
            int keep = 0;
            while (keep < _p.Count && _s[keep] < s - 1e-5f) keep++;
            _p.RemoveRange(keep, _p.Count - keep); _s.RemoveRange(keep, _s.Count - keep);
            Add(end);
        }

        public void AddQuadratic(Vector3 a, Vector3 c, Vector3 b, int n, bool skipFirst)
        {
            for (int i = skipFirst ? 1 : 0; i <= n; i++) Add(InkMath.Bezier(a, c, b, i / (float)n));
        }

        public void AddCubic(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int n, bool skipFirst)
        {
            for (int i = skipFirst ? 1 : 0; i <= n; i++) Add(InkMath.Cubic(a, b, c, d, i / (float)n));
        }
    }

    /// <summary>A mesh being filled: positions (world), normals, uv0, uv1, colours, triangles.</summary>
    public sealed class InkMeshBuffer
    {
        public readonly List<Vector3> V = new List<Vector3>(512), N = new List<Vector3>(512);
        public readonly List<Vector2> Uv0 = new List<Vector2>(512), Uv1 = new List<Vector2>(512);
        public readonly List<Color32> C = new List<Color32>(512);
        public readonly List<int> T = new List<int>(1536);

        public int Count => V.Count;
        public void Clear() { V.Clear(); N.Clear(); Uv0.Clear(); Uv1.Clear(); C.Clear(); T.Clear(); }

        public int Vert(Vector3 p, Vector3 n, Vector2 uv0, Vector2 uv1, float r, float g, float a)
        {
            V.Add(p); N.Add(n); Uv0.Add(uv0); Uv1.Add(uv1); C.Add(InkMath.Col(r, g, a));
            return V.Count - 1;
        }

        /// <summary>A triangle wound so its front faces <paramref name="front"/>.</summary>
        public void Tri(int a, int b, int c, Vector3 front)
        {
            if (Vector3.Dot(Vector3.Cross(V[b] - V[a], V[c] - V[a]), front) >= 0f) { T.Add(a); T.Add(b); T.Add(c); }
            else { T.Add(a); T.Add(c); T.Add(b); }
        }

        public void Flush(Mesh m)
        {
            m.Clear();
            if (V.Count == 0) return;
            m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, Uv0); m.SetUVs(1, Uv1); m.SetColors(C);
            m.SetTriangles(T, 0);
            m.RecalculateBounds();
        }
    }

    /// <summary>One renderer group of a cast: its own dynamic mesh and property block on ONE shared stroke material (no material instance). It has its own clock values (_TailCut, _Dry, _CoreK ...).
    /// The GameObject sits at the world origin (vertices are world positions); <see cref="Follow"/> moves it by a body's move since the hit (the earth push) - nothing is rebuilt for that.</summary>
    public sealed class InkLayer
    {
        static readonly int RevealId = Shader.PropertyToID("_Reveal"), TailCutId = Shader.PropertyToID("_TailCut"), DryId = Shader.PropertyToID("_Dry"),
            CoreKId = Shader.PropertyToID("_CoreK"), AfterAlphaId = Shader.PropertyToID("_AfterAlpha");

        public readonly GameObject Go;
        public readonly InkMeshBuffer Buf = new InkMeshBuffer();
        readonly Mesh _mesh;
        readonly MeshRenderer _r;
        readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();

        public float Reveal = 1f, TailCut, Dry, CoreK, AfterAlpha = 1f;

        public InkLayer(Transform parent, string name, Material shared)
        {
            Go = new GameObject(name);
            Go.transform.SetParent(parent, false);
            Go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Go.transform.localScale = Vector3.one;
            Go.layer = 0;
            _mesh = new Mesh { name = name }; _mesh.MarkDynamic();
            Go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _r = Go.AddComponent<MeshRenderer>();
            _r.sharedMaterial = shared;
            _r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; _r.receiveShadows = false;
            _r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; _r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            Go.SetActive(false);
        }

        public void Begin() => Buf.Clear();

        public void Flush()
        {
            Buf.Flush(_mesh);
            bool any = Buf.Count > 0;
            if (Go.activeSelf != any) Go.SetActive(any);
            if (!any) return;
            _r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(RevealId, Reveal); _mpb.SetFloat(TailCutId, TailCut); _mpb.SetFloat(DryId, Dry);
            _mpb.SetFloat(CoreKId, CoreK); _mpb.SetFloat(AfterAlphaId, AfterAlpha);
            _r.SetPropertyBlock(_mpb);
        }

        /// <summary>Moves the whole group by <paramref name="delta"/> (world) from where its vertices were written.</summary>
        public void Follow(Vector3 delta) { Go.transform.position = delta; }

        public void Hide() { Buf.Clear(); _mesh.Clear(); if (Go.activeSelf) Go.SetActive(false); }

        public void Destroy() { if (_mesh != null) Object.Destroy(_mesh); }
    }

    /// <summary>Pure helpers.</summary>
    public static class InkMath
    {
        public static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
        public static float Frac(float x) => x - Mathf.Floor(x);
        public static float Hash(float x) => Frac(Mathf.Sin(x * 127.1f + 311.7f) * 43758.5453f);
        /// <summary>Hand-drawn timing: <paramref name="t"/> held in steps of 1 / <paramref name="hz"/> (2 frames at 30; 0 = continuous).</summary>
        public static float Step(float t, float hz) => hz > 0.5f ? Mathf.Floor(t * hz) / hz : t;
        public static Color32 Col(float r, float g, float a) => new Color32(B(r), B(g), 0, B(a));
        static byte B(float x) => (byte)Mathf.RoundToInt(Mathf.Clamp01(x) * 255f);
        public static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t) { float m = 1f - t; return a * (m * m) + b * (2f * m * t) + c * (t * t); }
        public static Vector3 Cubic(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t) { float m = 1f - t; return a * (m * m * m) + b * (3f * m * m * t) + c * (3f * m * t * t) + d * (t * t * t); }
        public static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        /// <summary>A unit vector perpendicular to <paramref name="d"/>, as close to <paramref name="hint"/> as possible.</summary>
        public static Vector3 Perp(Vector3 d, Vector3 hint)
        {
            Vector3 p = hint - d * Vector3.Dot(hint, d);
            if (p.sqrMagnitude < 1e-8f) p = Vector3.Cross(d, Vector3.right);
            if (p.sqrMagnitude < 1e-8f) p = Vector3.Cross(d, Vector3.forward);
            return p.normalized;
        }
        /// <summary><paramref name="v"/> turned <paramref name="deg"/> about the unit axis <paramref name="axis"/>.</summary>
        public static Vector3 Rotate(Vector3 v, Vector3 axis, float deg) => Quaternion.AngleAxis(deg, axis) * v;
    }

    /// <summary>What the ribbon emitter writes into each vertex besides the geometry.</summary>
    public struct InkPaint
    {
        public float Seed, Layer;      // uv1
        public float Alpha;            // colour a (x the shape's own end fades)
        public float CoreMul;          // x the shape's Core -> colour r
        public float WidthMul;         // x every width
        public float Lateral;          // a constant sideways shift (m) of the whole ribbon (an afterimage's slash offset)
        public static InkPaint Main(float seed) => new InkPaint { Seed = seed, Layer = 0f, Alpha = 1f, CoreMul = 1f, WidthMul = 1f };
    }

    /// <summary>The ribbon emitter and the shape generators (paths, coils, cages, marks). Pure, static, no state.</summary>
    public static class InkStrokeBuilder
    {
        /// <summary>Flying-stroke width near the brush (P2 "손 근처 과대"): the shoulder camera is ~0.5 m from the hand, so a 0.3 m head there is bigger than the caster's head. x 0.3 at the tip -> x 1 by 1.5 m,
        /// and never more than 0.06 m of width per metre of camera distance below 2.5 m (easing to x 1 by 5 m). TEST.</summary>
        public static float NearCap(float distFromTip, float camDist, float width)
        {
            float k = Mathf.Lerp(0.3f, 1f, InkMath.Smooth(distFromTip / 1.5f));
            float w = width * k;
            float camCap = Mathf.Lerp(0.06f * camDist, 99f, InkMath.Smooth((camDist - 2.5f) / 2.5f));
            return Mathf.Min(w, camCap);
        }

        /// <summary>The ribbon of the stretch [<paramref name="s0"/>, <paramref name="s1"/>] of <paramref name="path"/> (s1 = the drawn head). uv0.x runs 0 at s0 to 1 at s1. <paramref name="tipCap"/> (world, or null)
        /// caps the width near the brush tip (<see cref="NearCap"/>). The head's last SplitLen metres become <see cref="InkStrokeShape.Split"/> converging strands when the shape asks for it.</summary>
        public static void Ribbon(InkMeshBuffer buf, InkCurve path, float s0, float s1, in InkStrokeShape shape, in InkFace face, in InkPaint paint, Vector3? tipCap = null)
        {
            float len = s1 - s0;
            if (len < 0.02f || path.Count < 2) return;
            bool split = shape.Split > 1 && shape.SplitLen > 0.05f && len > shape.SplitLen + 0.1f;
            float sMainEnd = split ? s1 - shape.SplitLen + 0.03f : s1;
            Strip(buf, path, s0, sMainEnd, s1, shape, face, paint, tipCap, 0f, 1f, 0f);
            if (!split) return;
            int n = shape.Split;
            float sSplit = s1 - shape.SplitLen;
            for (int k = 0; k < n; k++)
            {
                float c = n == 1 ? 0f : (k / (float)(n - 1) - 0.5f);           // -0.5 .. 0.5 across the width
                float v0 = k / (float)n, v1 = (k + 1f) / n;
                Strip(buf, path, sSplit, s1, s1, shape, face, paint, tipCap, v0, v1, c, s0, n);
            }
        }

        /// <summary>One strip. <paramref name="sHead"/> = the stroke's own head (for the width profile); [v0, v1] = the part of the width this strip covers (uv0.y); <paramref name="strandC"/> = a split strand's place
        /// across the width (-0.5 .. 0.5; its offset closes to 0 at the head: the awl's point). <paramref name="sTail"/> / <paramref name="strands"/> are only for a split strand.</summary>
        static void Strip(InkMeshBuffer buf, InkCurve path, float sa, float sb, float sHead, in InkStrokeShape shape, in InkFace face, in InkPaint paint, Vector3? tipCap,
            float v0, float v1, float strandC, float sTail = -1f, int strands = 1)
        {
            float span = sb - sa;
            if (span < 0.01f) return;
            if (sTail < 0f) sTail = sa;
            float full = Mathf.Max(0.02f, sHead - sTail);
            int n = Mathf.Clamp(Mathf.CeilToInt(span / 0.05f), 4, 64);
            int prevA = -1, prevB = -1;
            for (int i = 0; i <= n; i++)
            {
                float s = Mathf.Lerp(sa, sb, i / (float)n);
                Vector3 p = path.At(s), t = path.Dir(s);
                face.At(p, t, out Vector3 side, out Vector3 nrm);
                float dHead = sHead - s, dTail = s - sTail;
                float w = shape.WidthAt(dHead, dTail) * paint.WidthMul;
                if (tipCap.HasValue) w = NearCap(Vector3.Distance(tipCap.Value, p), Vector3.Distance(face.Cam, p), w);
                float u = Mathf.Clamp01((s - sTail) / full);
                // the flick bends the point over by a fraction of the TORSO's width (the local width is ~0 at the point itself)
                float flickW = shape.TorsoW * paint.WidthMul;
                if (tipCap.HasValue) flickW = NearCap(Vector3.Distance(tipCap.Value, p), Vector3.Distance(face.Cam, p), flickW);
                Vector3 c = p + side * (paint.Lateral + shape.FlickAt(dHead, dTail) * flickW);
                float a0, a1;
                if (strands > 1)
                {
                    // a bristle strand of the awl: its share of the width at the split's root, closing on the centre line at the head
                    float q = Mathf.Clamp01(dHead / Mathf.Max(0.05f, shape.SplitLen));
                    float spread = Mathf.Pow(q, 0.8f);
                    float sw = w / strands * 0.85f * Mathf.Lerp(0.35f, 1f, q);
                    Vector3 sc = c + side * (strandC * w * spread);
                    a0 = -sw * 0.5f; a1 = sw * 0.5f; c = sc;
                }
                else { a0 = -w * 0.5f; a1 = w * 0.5f; }
                float g = Mathf.Lerp(shape.Wet0, shape.Wet1, Mathf.Pow(u, 0.7f));
                float r = shape.Core * paint.CoreMul * Mathf.Lerp(0.55f, 1f, u);
                float al = paint.Alpha;
                var uv1 = new Vector2(paint.Seed, paint.Layer);
                int ia = buf.Vert(c + side * a0, nrm, new Vector2(u, v0), uv1, r, g, al);
                int ib = buf.Vert(c + side * a1, nrm, new Vector2(u, v1), uv1, r, g, al);
                if (prevA >= 0) { buf.Tri(prevA, prevB, ia, nrm); buf.Tri(prevB, ib, ia, nrm); }
                prevA = ia; prevB = ib;
            }
        }

        /// <summary>목's thorns (PLAN 1.6, judge r2 common handle 6): short HOOKED triangles on both edges of the stroke [s0, s1], pointing back toward the tail like fish-hooks, 5 - 8 per edge, each no longer than
        /// half the local width (and 7 cm). Colour r 0 (no core), the stroke's wetness. They are drawn with the stroke's own face.</summary>
        public static void Thorns(InkMeshBuffer buf, InkCurve path, float s0, float s1, float sHead, in InkStrokeShape shape, in InkFace face, in InkPaint paint, int perEdge, float seed, Vector3? tipCap = null)
        {
            float len = s1 - s0;
            if (len < 0.2f || perEdge <= 0) return;
            var uv1 = new Vector2(paint.Seed, paint.Layer);
            for (int e = 0; e < 2; e++)
            {
                float sign = e == 0 ? 1f : -1f;
                for (int j = 0; j < perEdge; j++)
                {
                    float jit = InkMath.Hash(seed * 31.7f + j * 7.3f + e * 3.1f);
                    float s = s0 + len * Mathf.Clamp01((j + 0.25f + 0.5f * jit) / perEdge);
                    if (sHead - s < 0.06f) continue;
                    Vector3 p = path.At(s), t = path.Dir(s);
                    face.At(p, t, out Vector3 side, out Vector3 nrm);
                    float dHead = sHead - s, dTail = s - s0;
                    float w = shape.WidthAt(dHead, dTail) * paint.WidthMul;
                    if (tipCap.HasValue) w = NearCap(Vector3.Distance(tipCap.Value, p), Vector3.Distance(face.Cam, p), w);
                    float L = Mathf.Min(0.5f * w, 0.07f) * Mathf.Lerp(0.7f, 1.1f, InkMath.Hash(seed + j * 1.7f + e));
                    if (L < 0.012f) continue;
                    Vector3 c = p + side * (paint.Lateral + shape.FlickAt(dHead, dTail) * shape.TorsoW * paint.WidthMul);
                    Vector3 edge = c + side * (sign * w * 0.45f);
                    float hw = L * 0.32f;
                    Vector3 b0 = edge - t * hw, b1 = edge + t * hw;
                    Vector3 m = edge + side * (sign * L * 0.6f) - t * (L * 0.2f);
                    Vector3 tip = m - t * (L * 0.55f) + side * (sign * L * 0.12f);           // the hook bends back toward the tail
                    float u = Mathf.Clamp01((s - s0) / Mathf.Max(0.05f, sHead - s0));
                    float g = Mathf.Lerp(shape.Wet0, shape.Wet1, u);
                    float v = e == 0 ? 1f : 0f;
                    int i0 = buf.Vert(b0, nrm, new Vector2(u, v), uv1, 0f, g, paint.Alpha);
                    int i1 = buf.Vert(b1, nrm, new Vector2(u, v), uv1, 0f, g, paint.Alpha);
                    int i2 = buf.Vert(m, nrm, new Vector2(u, v), uv1, 0f, g, paint.Alpha);
                    int i3 = buf.Vert(tip, nrm, new Vector2(u, v), uv1, 0f, g, paint.Alpha);
                    buf.Tri(i0, i1, i2, nrm); buf.Tri(i0, i2, i3, nrm);
                }
            }
        }

        /// <summary>A thin needle (바늘 선) from <paramref name="from"/> along <paramref name="dir"/>, <paramref name="len"/> long, <paramref name="width"/> at its root, a point at its end, facing the camera. Colour r 1 (it is all core).</summary>
        public static void Needle(InkMeshBuffer buf, Vector3 from, Vector3 dir, float len, float width, Vector3 cam, float seed, float alpha)
        {
            if (len < 0.01f || dir.sqrMagnitude < 1e-8f) return;
            dir.Normalize();
            var f = InkFace.Camera(cam);
            f.At(from, dir, out Vector3 side, out Vector3 nrm);
            var uv1 = new Vector2(seed, 0f);
            int a = buf.Vert(from - side * (width * 0.5f), nrm, new Vector2(0f, 0f), uv1, 1f, 0.4f, alpha);
            int b = buf.Vert(from + side * (width * 0.5f), nrm, new Vector2(0f, 1f), uv1, 1f, 0.4f, alpha);
            int c = buf.Vert(from + dir * len, nrm, new Vector2(1f, 0.5f), uv1, 1f, 0.2f, alpha);
            buf.Tri(a, b, c, nrm);
        }

        /// <summary>A flat polygon (an ink stone, a pool, a slab top) of <paramref name="corners"/> corners round <paramref name="centre"/> in the plane with normal <paramref name="normal"/>, radius <paramref name="radius"/> (corner radii
        /// jittered 0.7 - 1.15 by the seed). uv0 = planar (0..1 across), uv1 = (seed, layer).</summary>
        public static void Polygon(InkMeshBuffer buf, Vector3 centre, Vector3 normal, Vector3 hintAxis, float radius, int corners, float seed, float layer, float r, float g, float alpha, float aspect = 1f)
        {
            if (radius < 0.005f) return;
            normal = normal.normalized;
            Vector3 ax = InkMath.Perp(normal, hintAxis), ay = Vector3.Cross(normal, ax);
            var uv1 = new Vector2(seed, layer);
            int c0 = buf.Vert(centre, normal, new Vector2(0.5f, 0.5f), uv1, r, g, alpha);
            int first = -1, prev = -1;
            corners = Mathf.Clamp(corners, 3, 12);
            float rot = InkMath.Hash(seed * 9.1f) * 6.283f;
            for (int k = 0; k < corners; k++)
            {
                float ang = rot + k * 6.283f / corners + (InkMath.Hash(seed + k * 3.3f) - 0.5f) * 0.5f;
                float rad = radius * Mathf.Lerp(0.7f, 1.15f, InkMath.Hash(seed * 1.3f + k * 5.7f));
                float cx = Mathf.Cos(ang), cy = Mathf.Sin(ang) * aspect;
                Vector3 p = centre + (ax * cx + ay * cy) * rad;
                int i = buf.Vert(p, normal, new Vector2(0.5f + 0.5f * cx, 0.5f + 0.5f * cy), uv1, r, g, alpha);
                if (prev >= 0) buf.Tri(c0, prev, i, normal);
                else first = i;
                prev = i;
            }
            if (first >= 0 && prev >= 0) buf.Tri(c0, prev, first, normal);
        }

        /// <summary>토's ground slab (PLAN 1.6: "명중 때 땅 판 10 cm"): an irregular flat plate of ink round <paramref name="ground"/> lifted <paramref name="lift"/> metres with a skirt down to the ground (so it reads as a
        /// raised slab, not a decal), layer 0 (solid ink). <paramref name="alpha"/> fades it.</summary>
        public static void Slab(InkMeshBuffer buf, Vector3 ground, float radius, float lift, float seed, Vector3 cam, float alpha)
        {
            const int K = 7;
            var uv1 = new Vector2(seed, 0f);
            Vector3 top = ground + Vector3.up * lift;
            var ring = new Vector3[K];
            float rot = InkMath.Hash(seed * 4.1f) * 6.283f;
            for (int k = 0; k < K; k++)
            {
                float ang = rot + k * 6.283f / K + (InkMath.Hash(seed + k * 2.9f) - 0.5f) * 0.6f;
                float rad = radius * Mathf.Lerp(0.65f, 1.1f, InkMath.Hash(seed * 2.3f + k * 4.1f));
                ring[k] = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
            }
            int c0 = buf.Vert(top, Vector3.up, new Vector2(0.5f, 0.5f), uv1, 0f, 0.2f, alpha);
            int[] tops = new int[K];
            for (int k = 0; k < K; k++) tops[k] = buf.Vert(top + ring[k], Vector3.up, new Vector2(0.5f + ring[k].x / (2f * radius), 0.5f + ring[k].z / (2f * radius)), uv1, 0f, 0.2f, alpha);
            for (int k = 0; k < K; k++) buf.Tri(c0, tops[k], tops[(k + 1) % K], Vector3.up);
            if (lift < 0.005f) return;
            for (int k = 0; k < K; k++)
            {
                Vector3 a = top + ring[k], b = top + ring[(k + 1) % K];
                Vector3 out3 = ((ring[k] + ring[(k + 1) % K]) * 0.5f).normalized;
                if (Vector3.Dot(out3, cam - (a + b) * 0.5f) < 0f) continue;             // only the skirt faces the camera sees
                int i0 = buf.Vert(a, out3, new Vector2(0f, 1f), uv1, 0f, 0.1f, alpha), i1 = buf.Vert(b, out3, new Vector2(1f, 1f), uv1, 0f, 0.1f, alpha);
                int i2 = buf.Vert(a - Vector3.up * (lift + 0.02f), out3, new Vector2(0f, 0f), uv1, 0f, 0.1f, alpha), i3 = buf.Vert(b - Vector3.up * (lift + 0.02f), out3, new Vector2(1f, 0f), uv1, 0f, 0.1f, alpha);
                buf.Tri(i0, i1, i2, out3); buf.Tri(i1, i3, i2, out3);
            }
        }

        // ------------------------------------------------------------ paths

        /// <summary>The flying plane (PLAN 1.8 rule 1, from RiposteStroke308Path lines 85 - 93: its plane is turned PlaneYawDegrees off the plane that faces the caster): a plane that CONTAINS the line
        /// <paramref name="from"/> -> <paramref name="to"/> (the stroke has to go there), turned <paramref name="yawDeg"/> about that line away from the plane that faces the camera most. Returns its normal and the in-plane
        /// direction square to the line (the bulge's, chosen to rise).</summary>
        public static void FlightPlane(Vector3 from, Vector3 to, Vector3 cam, float yawDeg, out Vector3 normal, out Vector3 lateral)
        {
            Vector3 d = to - from; if (d.sqrMagnitude < 1e-6f) d = Vector3.forward; d.Normalize();
            Vector3 mid = (from + to) * 0.5f;
            Vector3 n0 = InkMath.Perp(d, cam - mid);                   // the plane through the line that faces the camera most
            normal = InkMath.Rotate(n0, d, yawDeg).normalized;
            lateral = Vector3.Cross(normal, d).normalized;
            if (lateral.y < 0f) lateral = -lateral;
        }

        /// <summary>A gentle arc: the straight line with a sine bulge of <paramref name="bulge"/> metres along <paramref name="lateral"/> (0 at both ends).</summary>
        public static void Arc(InkCurve into, Vector3 from, Vector3 to, Vector3 lateral, float bulge, int n, bool skipFirst = false)
        {
            for (int i = skipFirst ? 1 : 0; i <= n; i++)
            {
                float u = i / (float)n;
                into.Add(Vector3.Lerp(from, to, u) + lateral * (bulge * Mathf.Sin(Mathf.PI * u)));
            }
        }

        /// <summary>수's whip: an S along <paramref name="lateral"/> (one full sine of <paramref name="amp"/>), easing out at the head so it arrives straight; the last <paramref name="lash"/> of it snaps over (the lash).</summary>
        public static void Whip(InkCurve into, Vector3 from, Vector3 to, Vector3 lateral, float amp, float lash, int n)
        {
            for (int i = 0; i <= n; i++)
            {
                float u = i / (float)n;
                float off = amp * Mathf.Sin(2f * Mathf.PI * u) * (1f - 0.35f * u);
                if (u > 1f - lash) { float q = (u - (1f - lash)) / Mathf.Max(lash, 1e-3f); off += amp * 0.6f * Mathf.Sin(Mathf.PI * q) * (1f - q); }
                into.Add(Vector3.Lerp(from, to, u) + lateral * off);
            }
        }

        /// <summary>R7 (judge concept_r2: "표면 한 점 붙듦이 아니라 과녁 둘레 경로 생성기"): a coil round the struck body like a rope - from <paramref name="start"/> (world, the contact), <paramref name="turns"/> times round its axis
        /// on its REAL surface (the kit's <see cref="HexHolds.Surface"/>, one ray per sample - call it once at the hit, not per tick), rising <paramref name="rise"/> of the body's height per turn, <paramref name="outM"/> metres off
        /// the surface. <paramref name="dir"/> = +1 / -1 round the axis. Appended to <paramref name="into"/> in world space.</summary>
        public static void Coil(InkCurve into, HexSheet s, HexBody h, Vector3 start, float frac0, float turns, float rise, float outM, float dir, int samplesPerTurn)
        {
            into.Add(start);
            Vector3 cp = s.CamPlan;
            Vector2 toCam = new Vector2(cp.x - h.Centre.x, cp.z - h.Centre.y);
            if (toCam.sqrMagnitude < 1e-4f) toCam = s.ToCamPlan.sqrMagnitude > 1e-4f ? s.ToCamPlan : -Vector2.up;
            Vector3 sp = s.ToPlan(start);
            Vector2 rel = new Vector2(sp.x - h.Centre.x, sp.z - h.Centre.y);
            float az0 = rel.sqrMagnitude > 1e-6f ? Vector2.SignedAngle(toCam, rel) : 0f;
            int n = Mathf.Max(6, Mathf.CeilToInt(samplesPerTurn * turns));
            for (int i = 1; i <= n; i++)
            {
                float u = i / (float)n;
                float az = az0 + dir * 360f * turns * u;
                float frac = Mathf.Clamp(frac0 + rise * turns * u, 0.05f, 0.95f);
                HexHolds.Surface(s, h, az, frac, out Vector3 c, out Vector2 o);
                Vector3 w = s.PlanW(c) + (s.Frame.Right * o.x + s.Frame.Fwd * o.y) * outM;
                into.Add(w);
            }
        }

        /// <summary>A run down a body (수's 흘러내림, 각's coil going down to the ground): from <paramref name="start"/> down the surface at its azimuth to <paramref name="frac1"/> of the body's height, a slight sway.</summary>
        public static void Run(InkCurve into, HexSheet s, HexBody h, Vector3 start, float frac1, float sway, float seed, int n)
        {
            into.Add(start);
            Vector3 cp = s.CamPlan;
            Vector2 toCam = new Vector2(cp.x - h.Centre.x, cp.z - h.Centre.y);
            if (toCam.sqrMagnitude < 1e-4f) toCam = -Vector2.up;
            Vector3 sp = s.ToPlan(start);
            Vector2 rel = new Vector2(sp.x - h.Centre.x, sp.z - h.Centre.y);
            float az0 = rel.sqrMagnitude > 1e-6f ? Vector2.SignedAngle(toCam, rel) : 0f;
            float H = Mathf.Max(0.3f, h.Y1 - h.Y0);
            float frac0 = Mathf.Clamp01((sp.y - h.Y0) / H);
            for (int i = 1; i <= n; i++)
            {
                float u = i / (float)n;
                float az = az0 + sway * Mathf.Sin(u * 3.1f + seed * 6f);
                HexHolds.Surface(s, h, az, Mathf.Lerp(frac0, frac1, u), out Vector3 c, out Vector2 o);
                into.Add(s.PlanW(c) + (s.Frame.Right * o.x + s.Frame.Fwd * o.y) * 0.01f);
            }
        }

        /// <summary>각's cage branch: from <paramref name="foot"/> (on the ground) up to <paramref name="height"/>, leaning out along <paramref name="outward"/> at the waist and curling back in over the body at the top
        /// (a cage, not a fence), with a small sideways sway from the seed.</summary>
        public static void Branch(InkCurve into, Vector3 foot, Vector3 outward, float height, float seed, int n)
        {
            Vector3 o = InkMath.Flat(outward); if (o.sqrMagnitude < 1e-6f) o = Vector3.forward; o.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, o);
            float sw = (InkMath.Hash(seed * 3.7f) - 0.5f) * 0.12f;
            Vector3 a = foot, b = foot + o * 0.14f + Vector3.up * (height * 0.35f) + side * sw;
            Vector3 c = foot + o * 0.02f + Vector3.up * (height * 0.8f) - side * sw;
            Vector3 d = foot - o * (0.22f + 0.06f * InkMath.Hash(seed)) + Vector3.up * height;
            for (int i = 0; i <= n; i++) into.Add(InkMath.Cubic(a, b, c, d, i / (float)n));
        }

        /// <summary>A tongue of flame licking up (화): from <paramref name="root"/> rising <paramref name="len"/> metres, leaning along <paramref name="lean"/>, wavering with <paramref name="time"/> (stepped by the caller).</summary>
        public static void Tongue(InkCurve into, Vector3 root, Vector3 lean, float len, float seed, float time, int n)
        {
            Vector3 l = lean.sqrMagnitude > 1e-6f ? lean.normalized : Vector3.zero;
            Vector3 side = Vector3.Cross(Vector3.up, l.sqrMagnitude > 1e-6f ? l : Vector3.forward); if (side.sqrMagnitude < 1e-6f) side = Vector3.right; side.Normalize();
            for (int i = 0; i <= n; i++)
            {
                float u = i / (float)n;
                float wav = Mathf.Sin(u * 5.5f - time * 9f + seed * 7f) * 0.07f * u + Mathf.Sin(u * 2.3f + time * 4.1f + seed * 3f) * 0.04f * u;
                into.Add(root + Vector3.up * (len * u) + l * (len * 0.45f * u * u) + side * wav);
            }
        }
    }

    /// <summary>The brush tip's gathering ink before a 한 번 launch (D308-158 1, PLAN 1.4 "예고"): ink motes drawn into the tip (6 - 8 over the swing) - the hex preview's motes without its cells - plus a thin ink arc
    /// of the tip's last 0.15 s of travel, drawn by the part from <see cref="Trail"/>. One per cast, owned by the part.</summary>
    public sealed class InkTipGather
    {
        ParticleSystem _ps;
        GameObject _go;
        readonly List<Vector3> _trail = new List<Vector3>(32);
        readonly List<float> _times = new List<float>(32);

        public IReadOnlyList<Vector3> Trail => _trail;

        public void Show(Transform parent, Vector3 tipWorld, float since, float delay)
        {
            _trail.Add(tipWorld); _times.Add(since);
            while (_times.Count > 2 && since - _times[0] > 0.15f) { _times.RemoveAt(0); _trail.RemoveAt(0); }
            if (!Application.isPlaying) return;
            if (_ps == null)
            {
                _go = new GameObject("InkTipGather");
                _go.transform.SetParent(parent, false);
                int layer = VfxAfterFog308.Layer; if (layer >= 0) _go.layer = layer;
                _ps = _go.AddComponent<ParticleSystem>();
                _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = _ps.main; main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = 0.22f; main.startSpeed = 0f; main.startSize = 0.025f;
                main.startColor = new Color(0.03f, 0.03f, 0.035f, 0.9f); main.maxParticles = 24;
                var shape = _ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.16f; shape.radiusThickness = 0f;
                var em = _ps.emission; em.rateOverTime = 0f;
                var vel = _ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
                vel.x = new ParticleSystem.MinMaxCurve(0f); vel.y = new ParticleSystem.MinMaxCurve(0f); vel.z = new ParticleSystem.MinMaxCurve(0f);
                vel.radial = new ParticleSystem.MinMaxCurve(-0.72f);
                var sol = _ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.3f)));
                var r = _go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = SpellFxLabShot.InkBlotMaterial(); r.renderMode = ParticleSystemRenderMode.Billboard;
            }
            _go.transform.position = tipWorld;
            float p = Mathf.Clamp01(since / Mathf.Max(delay, 0.05f));
            var e = _ps.emission; e.rateOverTime = Mathf.Clamp(14f / Mathf.Max(delay, 0.15f), 20f, 80f) * p;
            if (!_ps.isPlaying) _ps.Play();
        }

        public void Stop()
        {
            _trail.Clear(); _times.Clear();
            if (_ps != null) { var e = _ps.emission; e.rateOverTime = 0f; _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
        }
    }
}
