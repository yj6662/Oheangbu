using System.Collections.Generic;
using Oheangbu.App;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>D308-153b route P rebuilt from the concept uks_1 (2026-10-29, Fable): the water ring 우 as a Korean-pattern WAVE SCREEN. One band of seigaiha (청해파) scales
    /// stands round the caster on a near-vertical cylinder (radius R, the upper half leaning a little inward); its whole wave shape is a 2-D silhouette cut by the shader
    /// Oheangbu/SpellFx/PatternWave in the sheet's own (u along the ring, y up the sheet) space: rounded swells (7 crests over the front three quarters), at each crest a tube
    /// hooking SIDEWAYS along the ring with a spiral roundel at its tip (the cloud-scroll curls of the concept), the camera-side arc kept low. Two or three black ink ribbons
    /// sweep along the sheet's inner face. Where a contact lands the same functions make a wave twice the band's height with a big hook, and the geometry there bends its
    /// upper part ~100 deg inward so the lip hangs over the target; it then collapses and its scales fall away. Everything is built every tick from the scope-clock age (no
    /// coroutine, no Time, no Physics); the meshes are laid in WORLD space under children at the world origin. Look only: never moves a gameplay object. (r1)</summary>
    public sealed class SpellFxPatternWavesPart : MonoBehaviour, ISpellFxPart
    {
        [SerializeField] Shader patternShader;
        [SerializeField] Texture2D inkTex;
        [SerializeField] float radius = 2.2f;
        [SerializeField] float lifeSeconds = 1.8f;

        // ---- tunables (metres / seconds on the 1.8 s base timeline; lifeSeconds stretches the whole timeline) ----
        const float BaseLife = 1.8f;
        const int NU = 192, NV = 9, MaxCrest = 24;                   // sheet columns round the circle, rows up the sheet, the shader's crest array size
        const float ScaleR = 0.3f;                                    // seigaiha scale radius (rounded so that a whole number of scales closes the ring)
        const float Sink = 0.18f;                                     // the foot sits this far below the cast point (uneven ground)
        const float HFront = 1.45f, HSide = 1.2f, HBack = 0.45f;      // base band height in front of / beside / behind (camera side) the caster
        const float LeanFrontDeg = 28f, LeanSideDeg = 42f, LeanPow = 2f;   // the sheet leans inward by this at its top (~ g^LeanPow), more at the sides: the side crests show a face to the camera (r4)
        const float RiseEnd = 0.3f;                                   // rise with ease-back 0 .. RiseEnd
        const float ErodeA = 1.0f, ErodeB = 1.75f;                    // scales vanish between these moments
        const float SinkA = 1.05f, SinkB = 1.6f, SinkTo = 0.45f;      // the band sinks to 45 % by SinkB
        const float Drift = 0.35f;                                    // m/s the lattice and the crests travel along the ring (the hooks point the way they travel)
        const float TopMargin = 0.12f;                                // the mesh reaches this far above the silhouette

        // ---- crests ----
        const int CrestCount = 7;
        const float CrestArc = 2.5f;                                  // crests over theta = -CrestArc .. +CrestArc (none on the low camera-side arc)
        const float AmpMin = 0.5f, AmpMax = 0.75f;                    // swell height over the base band (m)
        const float WBackMin = 0.6f, WBackMax = 0.85f;                // the gentle back slope's half-width (m); the hollow side is max(0.95 rho, 0.5 back)
        const float RhoPerAmp = 0.85f;                                // hook tube radius = this x amp (r1: 0.55 read as a small ear)
        const float HookA = 0.08f, HookB = 0.38f;                     // the hook grows from nothing between these moments

        // ---- hit wave ----
        const int MaxHits = 2;
        const float HitAmp = 1.4f, HitWBack = 1.1f, HitRho = 0.85f;  // twice the band: 1.45 + 1.4 = 2.85 m at the front; a big hook
        const float HitRise = 0.22f;                                  // seconds to rear up (ease-back)
        const float HitCollA = 0.65f, HitCollB = 1.05f;               // collapses between these moments after the hit (r1: 0.5 - 0.95 was gone before it read)
        const float HitErA = 0.95f, HitErB = 1.3f, HitErW = 1.2f;     // its scales fall away locally (over +-HitErW m) - r3: only after the collapse (r2 at 0.6 s: a ragged hole in the standing wall)
        const float HitPhiDeg = 75f, HitPhiHalfW = 0.6f;              // the sheet's upper part bends inward by this over +-HitPhiHalfW rad: the lip hangs over the target
        const float HitPhiA = 0.22f, HitPhiB = 0.5f;                  // r4: the fold comes AFTER the rear-up (r3: folding while rising made a pointed crease and hid the hook)

        // ---- ink ribbons ----
        const int InkCount = 3, InkSegs = 28;
        const float InkInset = 0.07f;                                 // ribbons sit this far in front of the sheet (toward the ring centre)
        const float InkBackLimit = 0.6f;                              // not drawn where the band is the low camera-side arc

        static readonly int ModeId = Shader.PropertyToID("_Mode"), TexId = Shader.PropertyToID("_MainTex"), ScaleRId = Shader.PropertyToID("_ScaleR"), CellsId = Shader.PropertyToID("_Cells"),
            ShiftId = Shader.PropertyToID("_Shift"), ErodeId = Shader.PropertyToID("_Erode"), BaseId = Shader.PropertyToID("_Base"), RingId = Shader.PropertyToID("_Ring"),
            CrestNId = Shader.PropertyToID("_CrestN"), CrestId = Shader.PropertyToID("_Crest"), CrestXId = Shader.PropertyToID("_CrestX"),
            SrcId = Shader.PropertyToID("_SrcBlend"), DstId = Shader.PropertyToID("_DstBlend"), ZWriteId = Shader.PropertyToID("_ZWrite");

        // ---- state ----
        Vector3 _o, _f, _r;
        float _spin = 1f, _R = 2.2f, _L = 13.8f, _scaleR = ScaleR, _tScale = 1f;
        int _cells;
        bool _dead;
        Mesh _wallMesh, _inkMesh;
        Material _wallMat, _inkMat;
        Crest[] _crests;
        Rib[] _ribs;
        readonly List<Hit> _hits = new List<Hit>();
        readonly Vector4[] _ca = new Vector4[MaxCrest], _cx = new Vector4[MaxCrest];
        int _cn;
        readonly Vector3[] _pp = new Vector3[NV];              // scratch: the profile points of one column
        readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<Color> _c = new List<Color>();
        readonly List<int> _t = new List<int>();

        struct Crest { public float Th0, Amp, WBack, Seed; }
        struct Rib { public float Th0, Span, Width, At, YF, Lift; }
        sealed class Hit { public float T0, ThH, Seed; }

        // ---------------------------------------------------------------- small maths
        static float Sm(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
        static float EaseBack(float t) { t = Mathf.Clamp01(t); const float c1 = 1.70158f, c3 = c1 + 1f; return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f); }
        static float Wrap(float a) => Mathf.Repeat(a + Mathf.PI, 2f * Mathf.PI) - Mathf.PI;
        static float Back(float th) => Sm(0.55f, 1f, Mathf.Abs(th) / Mathf.PI);
        Vector3 Dir(float th) => _f * Mathf.Cos(th) + _r * Mathf.Sin(th);
        Vector3 Tan(float th) => -_f * Mathf.Sin(th) + _r * Mathf.Cos(th);
        float WrapU(float du) => du - _L * Mathf.Round(du / _L);
        float RiseSink(float t) => Mathf.Max(0f, EaseBack(t / RiseEnd)) * (1f - (1f - SinkTo) * Sm(SinkA, SinkB, t));

        /// <summary>The base band height above the ground at angle th (the shader's BaseH, without the rise x sink).</summary>
        static float BaseH(float th)
        {
            float a = Mathf.Abs(th) / Mathf.PI;
            return Mathf.Lerp(Mathf.Lerp(HFront, HSide, Sm(0.15f, 0.55f, a)), HBack, Back(th));
        }

        /// <summary>The silhouette's highest point at angle th (mirrors the shader: base + swells, and the hook circles over the hollows) + the margin: the mesh must cover the cut.</summary>
        float TopAt(float th, float rs)
        {
            float u = th * _R;
            float H = BaseH(th) * rs, top = H;
            for (int k = 0; k < _cn; k++)
            {
                Vector4 c = _ca[k];
                float du = WrapU(u - c.x);
                float s = c.w >= 0f ? 1f : -1f, rho = Mathf.Abs(c.w);
                float wB = Mathf.Max(c.z, 0.05f);
                float w = du * s > 0f ? Mathf.Max(0.95f * rho, 0.5f * wB) : wB;
                float q = du / w, b = Mathf.Clamp01(1f - q * q);
                H += c.y * b * b;
                if (rho > 0.02f && Mathf.Abs(du) < rho)
                {
                    float Hi = BaseH(c.x / _R) * rs + c.y;
                    top = Mathf.Max(top, Hi - rho + Mathf.Sqrt(rho * rho - du * du));
                }
            }
            return Mathf.Max(top, H) + TopMargin;
        }

        /// <summary>The sheet's inward turning angle at row fraction g (0 foot .. 1 top): the lean, plus the hit waves' lip bending over the target.</summary>
        float PhiAt(float th, float g, float t)
        {
            float lean = Mathf.Lerp(LeanFrontDeg, LeanSideDeg, Sm(0.25f, 0.6f, Mathf.Abs(th) / Mathf.PI));
            float phi = lean * Mathf.Deg2Rad * Mathf.Pow(g, LeanPow);
            foreach (var h in _hits)
            {
                float a = t - h.T0;
                if (a < 0f) continue;
                float m = Sm(0f, 0.6f, 1f - Mathf.Abs(Wrap(th - h.ThH)) / HitPhiHalfW);
                if (m <= 0f) continue;
                float k = Sm(HitPhiA, HitPhiB, a) * (1f - Sm(HitCollA, HitCollB, a));
                phi += HitPhiDeg * Mathf.Deg2Rad * m * k * Sm(0.45f, 1f, g);
            }
            return phi;
        }

        /// <summary>Lays the column's profile into _pp[0..NV-1] (world positions, uniform in arc length) and returns the arc length S from the foot (R, -Sink) to the top.</summary>
        float Prof(float th, float t, float topY)
        {
            const int seg = NV - 1;
            float S = topY + Sink;
            Vector3 nin = -Dir(th);
            Vector3 p = _o + Dir(th) * _R; p.y = _o.y - Sink;
            _pp[0] = p;
            float step = S / seg;
            for (int j = 1; j <= seg; j++)
            {
                float ph = PhiAt(th, (j - 0.5f) / seg, t);
                p += (Vector3.up * Mathf.Cos(ph) + nin * Mathf.Sin(ph)) * step;
                _pp[j] = p;
            }
            return S;
        }

        /// <summary>The surface point at arc fraction g (needs Prof to have just run for this column) and the inward-facing normal there.</summary>
        void Surf(float th, float g, float t, out Vector3 pos, out Vector3 nrm, out Vector3 tng)
        {
            float x = Mathf.Clamp01(g) * (NV - 1);
            int j = Mathf.Min((int)x, NV - 2);
            pos = Vector3.Lerp(_pp[j], _pp[j + 1], x - j);
            float ph = PhiAt(th, g, t);
            Vector3 nin = -Dir(th);
            tng = Vector3.up * Mathf.Cos(ph) + nin * Mathf.Sin(ph);
            nrm = nin * Mathf.Cos(ph) - Vector3.up * Mathf.Sin(ph);
        }

        // ---------------------------------------------------------------- ISpellFxPart
        public void OnBegin(SpellFxCast cast)
        {
            _o = cast.Origin; _f = cast.Forward.sqrMagnitude > 1e-6f ? cast.Forward.normalized : Vector3.forward; _r = Vector3.Cross(Vector3.up, _f);
            _spin = (cast.CastId & 1) == 0 ? 1f : -1f;
            _R = cast.Radius > 0.5f ? cast.Radius : radius;
            _L = 2f * Mathf.PI * _R;
            _tScale = BaseLife / Mathf.Max(0.3f, lifeSeconds);
            _cells = Mathf.Max(8, Mathf.RoundToInt(_L / (2f * ScaleR)));
            _scaleR = _L / (2f * _cells);                                           // a whole number of scales round the ring: the seam closes
            var sh = patternShader != null ? patternShader : Shader.Find("Oheangbu/SpellFx/PatternWave");
            if (sh == null) { Debug.LogWarning("SpellFxPatternWavesPart: shader Oheangbu/SpellFx/PatternWave not found; nothing is drawn."); _dead = true; return; }

            _wallMat = new Material(sh) { hideFlags = HideFlags.DontSave };
            _wallMat.SetFloat(ModeId, 0f); _wallMat.SetFloat(ScaleRId, _scaleR); _wallMat.SetFloat(CellsId, _cells);
            _wallMat.SetVector(RingId, new Vector4(_R, _L, 0f, 0f));
            _wallMat.SetVectorArray(CrestId, _ca); _wallMat.SetVectorArray(CrestXId, _cx);
            _inkMat = new Material(sh) { hideFlags = HideFlags.DontSave };
            _inkMat.SetFloat(ModeId, 2f); if (inkTex != null) _inkMat.SetTexture(TexId, inkTex);
            _inkMat.SetFloat(SrcId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha); _inkMat.SetFloat(DstId, (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); _inkMat.SetFloat(ZWriteId, 0f);
            _inkMat.renderQueue = 3000; _inkMat.SetOverrideTag("RenderType", "Transparent");
            _wallMesh = MakeObject("Wave", _wallMat); _inkMesh = MakeObject("Ink", _inkMat);

            var rnd = new System.Random((int)(cast.CastId * 7919 + 31));
            _crests = new Crest[CrestCount];
            for (int i = 0; i < CrestCount; i++)
                _crests[i] = new Crest
                {
                    Th0 = Mathf.Lerp(-CrestArc, CrestArc, (i + 0.5f) / CrestCount) + ((float)rnd.NextDouble() - 0.5f) * 0.14f,
                    Amp = Mathf.Lerp(AmpMin, AmpMax, (float)rnd.NextDouble()),
                    WBack = Mathf.Lerp(WBackMin, WBackMax, (float)rnd.NextDouble()),
                    Seed = (float)rnd.NextDouble(),
                };
            _ribs = new Rib[InkCount];
            float[] starts = { -1.9f, -0.1f, 1.5f };
            for (int i = 0; i < InkCount; i++)
                _ribs[i] = new Rib
                {
                    Th0 = starts[i % starts.Length] + ((float)rnd.NextDouble() - 0.5f) * 0.3f,
                    Span = Mathf.Lerp(100f, 140f, (float)rnd.NextDouble()) * Mathf.Deg2Rad,
                    Width = Mathf.Lerp(0.24f, 0.36f, (float)rnd.NextDouble()),
                    At = 0.12f + 0.05f * i, YF = Mathf.Lerp(0.55f, 0.8f, (float)rnd.NextDouble()), Lift = Mathf.Lerp(0.4f, 0.7f, (float)rnd.NextDouble()),      // r3: higher and broader (r2 sat behind the caster)
                };
            VfxAfterFog308.Route(transform);          // the transparent ink goes after the fog; the cutout sheet writes depth and stays where it is
        }

        public void OnTick(SpellFxCast cast, float age)
        {
            if (_dead || _wallMat == null) return;
            float t = age * _tScale;
            float rs = RiseSink(t);
            FillCrests(t, rs);
            _wallMat.SetFloat(ErodeId, Sm(ErodeA, ErodeB, t));
            _wallMat.SetFloat(ShiftId, _spin * Drift * t);
            _wallMat.SetVector(BaseId, new Vector4(HFront, HSide, HBack, rs));
            _wallMat.SetFloat(CrestNId, _cn);
            _wallMat.SetVectorArray(CrestId, _ca); _wallMat.SetVectorArray(CrestXId, _cx);
            BuildWall(t, rs);
            BuildInk(t);
        }

        public void OnHit(SpellFxCast cast, SpellFxHit hit)
        {
            if (_dead || _wallMat == null || _hits.Count >= MaxHits) return;
            Vector3 feet = hit.Feet.sqrMagnitude > 1e-6f ? hit.Feet : new Vector3(hit.Position.x, _o.y, hit.Position.z);
            Vector3 rel = feet - _o; rel.y = 0f;
            float thH = Mathf.Atan2(Vector3.Dot(rel, _r), Vector3.Dot(rel, _f));            // the hit angle round the cast origin (Dir(th) = f cos + r sin)
            _hits.Add(new Hit { T0 = Mathf.Max(0f, cast.AgeAt(hit.NowUs) * _tScale), ThH = thH, Seed = 0.37f + 0.29f * _hits.Count });
        }

        public void OnEnd(SpellFxCast cast, SpellFxEndReason reason)
        {
            if (reason != SpellFxEndReason.Aborted) return;        // Done / TimedOut: the 1.8 s animation plays out through the player's tail
            _dead = true;
            if (_wallMesh != null) _wallMesh.Clear(); if (_inkMesh != null) _inkMesh.Clear();
        }

        // ---------------------------------------------------------------- the crest array (the shader's silhouette parameters)
        void FillCrests(float t, float rs)
        {
            _cn = 0;
            float drift = _spin * Drift * t;
            float grow = Sm(HookA, HookB, t);
            for (int i = 0; i < _crests.Length && _cn < MaxCrest; i++)
            {
                var c = _crests[i];
                float u = WrapU(c.Th0 * _R + drift);
                float back = Back(u / _R);
                float amp = c.Amp * rs * (1f - 0.7f * back);
                _ca[_cn] = new Vector4(u, amp, c.WBack, _spin * RhoPerAmp * amp * grow);
                _cx[_cn] = new Vector4(0f, 1f, c.Seed, 0f);
                _cn++;
            }
            foreach (var h in _hits)
            {
                if (_cn >= MaxCrest) break;
                float a = t - h.T0;
                if (a < 0f || a > HitCollB + 0.3f) continue;
                // r2 note: the hit's hollow-side width follows the hook (0.95 rho) like every crest
                float ramp = Mathf.Min(1.06f, EaseBack(a / HitRise));
                float coll = 1f - Sm(HitCollA, HitCollB, a);
                float amp = HitAmp * ramp * coll * (1f - (1f - SinkTo) * Sm(SinkA, SinkB, t));
                float rho = HitRho * Mathf.Min(1f, ramp) * coll;                        // r3: the hook rises with the swell (r2: it lagged, the first 0.3 s were a spike)
                _ca[_cn] = new Vector4(h.ThH * _R, amp, HitWBack, _spin * rho);
                _cx[_cn] = new Vector4(Sm(HitErA, HitErB, a), HitErW, h.Seed, 0f);
                _cn++;
            }
            for (int k = _cn; k < MaxCrest; k++) { _ca[k] = Vector4.zero; _cx[k] = Vector4.zero; }
        }

        // ---------------------------------------------------------------- scaffolding
        Mesh MakeObject(string name, Material m)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false); go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave }; mesh.MarkDynamic(); mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            return mesh;
        }

        // ---------------------------------------------------------------- the sheet
        void BuildWall(float t, float rs)
        {
            _v.Clear(); _n.Clear(); _uv.Clear(); _t.Clear();
            if (t < BaseLife - 0.02f && rs > 0.01f)
            {
                for (int iu = 0; iu <= NU; iu++)
                {
                    float th = (iu / (float)NU * 2f - 1f) * Mathf.PI;
                    float top = TopAt(th, rs);
                    float S = Prof(th, t, top);
                    float u = th * _R;
                    for (int iv = 0; iv < NV; iv++)
                    {
                        float g = iv / (NV - 1f);
                        Surf(th, g, t, out _, out Vector3 sn, out _);
                        _v.Add(_pp[iv]); _n.Add(sn);
                        _uv.Add(new Vector2(u, g * S - Sink));                                   // y = arc length up the sheet from the GROUND (the foot is Sink below it)
                    }
                }
                for (int iu = 0; iu < NU; iu++)
                    for (int iv = 0; iv < NV - 1; iv++)
                    {
                        int a = iu * NV + iv, a1 = a + 1, c2 = a + NV, d = c2 + 1;
                        _t.Add(a); _t.Add(c2); _t.Add(a1); _t.Add(a1); _t.Add(c2); _t.Add(d);
                    }
            }
            _wallMesh.Clear();
            if (_v.Count > 0) { _wallMesh.SetVertices(_v); _wallMesh.SetNormals(_n); _wallMesh.SetUVs(0, _uv); _wallMesh.SetTriangles(_t, 0); _wallMesh.RecalculateBounds(); }
        }

        // ---------------------------------------------------------------- ink ribbons along the sheet's inner face
        void BuildInk(float t)
        {
            _v.Clear(); _uv.Clear(); _c.Clear(); _t.Clear();
            float fade = 1f - Sm(1.0f, 1.55f, t);
            float rs = RiseSink(t);
            if (fade > 0.01f && rs > 0.05f)
                foreach (var rib in _ribs)
                {
                    float g = Sm(rib.At, rib.At + 0.4f, t);
                    if (g < 0.02f) continue;
                    float drift = _spin * 0.35f * Mathf.Max(0f, t - rib.At);
                    int prev = -1;
                    for (int j = 0; j < InkSegs; j++)
                    {
                        float s = j / (InkSegs - 1f);                      // 0 = tail, 1 = the growing head
                        float th = Wrap(rib.Th0 + drift + _spin * rib.Span * g * s);
                        if (Back(th) > InkBackLimit) { prev = -1; continue; }
                        float top = TopAt(th, rs);
                        float S = Prof(th, t, top);
                        float band = BaseH(th) * rs;                                   // ride at a fraction of the base band, the head lifting a little
                        float yy = band * Mathf.Clamp(rib.YF + 0.4f * rib.Lift * s * s * g, 0.3f, 1.05f);
                        float gf = Mathf.Clamp01((yy + Sink) / S);
                        Surf(th, gf, t, out Vector3 sp, out Vector3 sn, out Vector3 st);
                        Vector3 c = sp + sn * InkInset;
                        float wd = rib.Width * 0.5f * (0.35f + 0.65f * Sm(0f, 0.2f, s));
                        float a = fade * Sm(0f, 0.08f, s);
                        int k0 = _v.Count;
                        _v.Add(c - st * wd); _v.Add(c + st * wd);
                        _uv.Add(new Vector2(1f - s, 0f)); _uv.Add(new Vector2(1f - s, 1f));      // the wet head of the stroke (u = 0 of the texture) leads
                        _c.Add(new Color(1f, 1f, 1f, a)); _c.Add(new Color(1f, 1f, 1f, a));
                        if (prev >= 0) { _t.Add(prev); _t.Add(k0); _t.Add(prev + 1); _t.Add(prev + 1); _t.Add(k0); _t.Add(k0 + 1); }
                        prev = k0;
                    }
                }
            _inkMesh.Clear();
            if (_v.Count > 0) { _inkMesh.SetVertices(_v); _inkMesh.SetUVs(0, _uv); _inkMesh.SetColors(_c); _inkMesh.SetTriangles(_t, 0); _inkMesh.RecalculateBounds(); }
            _c.Clear();
        }

        void OnDestroy()
        {
            if (_wallMesh != null) Destroy(_wallMesh); if (_inkMesh != null) Destroy(_inkMesh);
            if (_wallMat != null) Destroy(_wallMat); if (_inkMat != null) Destroy(_inkMat);
        }
    }
}
