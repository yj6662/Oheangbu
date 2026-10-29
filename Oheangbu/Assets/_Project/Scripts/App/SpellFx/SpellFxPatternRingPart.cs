using System.Collections.Generic;
using Oheangbu.App;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>D308-153b route P (SPEC-SPELL-FX-RESTART-308 section 7) for 우: the water ring drawn as Korean traditional pattern, not as water. r5: one band round the caster painted with
    /// seigaiha scales by Oheangbu/SpellFx/Pattern (mode 0) that is made of WAVES - it swells into 7 peaks whose upper part curls inward and over like a breaking lip, the scales following the
    /// curling surface; a small cloud-scroll card sits at each lip tip as the foam curl; 2 - 3 black brush ribbons sweep along the surface; where a contact lands the wall itself rears up and
    /// crashes over it (a per-hit bump through the same height / curl functions), with thrown tumbling roundels. Everything is built every tick from the scope-clock age (no coroutine, no Time
    /// call, no Physics); the meshes are laid in WORLD space under children at the world origin. The look only: never moves a gameplay object.</summary>
    public sealed class SpellFxPatternRingPart : MonoBehaviour, ISpellFxPart
    {
        [SerializeField] Shader patternShader;
        [SerializeField] Texture2D crestTex, burstTexA, burstTexB, inkTex;
        [SerializeField] float radius = 2.2f;
        [SerializeField] float lifeSeconds = 1.8f;

        // ---- tunables (metres / seconds on the 1.8 s base timeline; lifeSeconds stretches the whole timeline) ----
        const float BaseLife = 1.8f;
        const int NU = 200, NV = 16;                                  // wall columns round the circle, rows up the (curling) band
        const float ScaleR = 0.32f;                                   // seigaiha scale radius (the shader's _ScaleR)
        const float Sink = 0.25f;                                     // the band reaches this far below the cast point (uneven ground)
        const float LeanDeg = 7f;                                     // the straight part of the band leans outward by this much
        const float HFront = 1.6f, HSide = 1.3f, HBack = 0.5f;        // base wall height in front of / beside / behind (camera side) the caster
        const float RiseEnd = 0.35f;                                  // rise with ease-back 0 .. RiseEnd
        const float ErodeA = 1.0f, ErodeB = 1.75f;                    // scales vanish between these moments
        const float SinkA = 1.0f, SinkB = 1.6f, SinkTo = 0.4f;        // the band sinks to 40 % by SinkB
        const float SpinRate = 0.16f;                                 // rad/s the pattern, the swell and the crest peaks turn round the ring

        // ---- r5 crest peaks (curling waves) ----
        const int PeakCount = 7;                                      // crest peaks round the ring
        const float PeakArc = 2.45f;                                  // peaks spread over theta = -PeakArc .. +PeakArc (front 3/4; none on the low camera-side arc)
        const float PeakAmpMin = 0.5f, PeakAmpMax = 0.8f;             // extra height of a peak over the base wall (m); fades toward the camera-side arc
        const float PeakHalfWMinDeg = 13f, PeakHalfWMaxDeg = 17f;     // half width of a peak (full width 26 - 34 deg)
        const float PeakHeightPow = 1.5f;                             // peak profile = (1 - |d|/halfW)^this : 1 = a triangle, bigger = a sharper tip
        const float PeakPhiMinDeg = 95f, PeakPhiMaxDeg = 125f;        // total inward turning of the band at its lip (> 90 = the lip hangs down)
        const float CurlPow = 3.0f;                                   // turning angle ~ g^CurlPow along the band (g 0 bottom .. 1 top): bigger = straighter below, a sharper lip on top
        const float CurlA = 0.08f, CurlB = 0.45f;                     // the curl grows from 0 to full between these moments (after the wall's own rise lag)
        const float CapSizeMin = 0.42f, CapSizeMax = 0.6f;            // the one cloud-scroll card at each lip tip (m)
        const float CapBackFrac = 0.15f;                              // the card starts this fraction of its size back along the lip (overlaps the sheet), then runs beyond the tip
        const float CapExtraCurlDeg = 24f;                            // the card keeps turning the way the lip turns

        // ---- r5 hit wave ----
        const int MaxBursts = 2, MinOrbs = 5, MaxOrbs = 7;
        const float BurstLife = 1.1f, Gravity = 7.5f;
        const float HitHeight = 2.8f;                                 // the wall rears up to this above the cast point at the hit angle (overrides the low camera-side arc too)
        const float HitHalfDeg = 25f;                                 // half width of the hit wave (+-25 deg), a flat-ish crown with falling flanks
        const float HitRise = 0.25f;                                  // seconds to rear up (ease-back)
        const float HitPhiDeg = 165f, HitPow = 2.2f;                  // the hit lip curls this far inward: right over the target
        const float HitCollA = 0.5f, HitCollB = 0.95f;                // seconds after the hit: the wave collapses between these moments
        const float HitErA = 0.45f, HitErB = 0.95f;                   // its scales erode locally between these moments (on top of the global erosion)
        const float HitCapSize = 0.8f;                                // the cloud-scroll card on the big lip tip
        const float OrbSizeMin = 0.12f, OrbSizeMax = 0.22f;           // thrown roundels

        // ---- ink ribbons ----
        const int InkCount = 3, InkSegs = 26;
        const float InkBack = 0.6f;                                   // a ribbon is not drawn where the wall is the low camera-side arc
        const float InkInset = 0.07f;                                 // ribbons sit this far in front of the sheet (toward the ring centre)

        const int CapRows = 6;

        static readonly int ModeId = Shader.PropertyToID("_Mode"), TexId = Shader.PropertyToID("_MainTex"), ScaleRId = Shader.PropertyToID("_ScaleR"), CellsId = Shader.PropertyToID("_Cells"),
            ErodeId = Shader.PropertyToID("_Erode"), SrcId = Shader.PropertyToID("_SrcBlend"), DstId = Shader.PropertyToID("_DstBlend"), ZWriteId = Shader.PropertyToID("_ZWrite");

        // ---- state ----
        Vector3 _o, _f, _r;
        float _spin = 1f, _R = 2.2f, _tScale = 1f;
        int _cells;
        bool _dead;
        Mesh _wallMesh, _inkMesh;
        Material _wallMat, _inkMat;
        Batch _crest, _orbA, _orbB;
        Peak[] _peaks;
        Rib[] _ribs;
        readonly List<Burst> _bursts = new List<Burst>();
        readonly Vector3[] _pp = new Vector3[NV];              // scratch: the profile points of one column
        readonly float[] _colH = new float[NU + 1];
        // scratch for the wall and the ribbons
        readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>(), _uv1 = new List<Vector2>();
        readonly List<Color> _c = new List<Color>();
        readonly List<int> _t = new List<int>();

        struct Peak { public float Th0, Amp, HalfW, Phi, Size, Rnd; }
        struct Rib { public float Th0, Span, Width, At, YF, Lift; }
        struct Orb { public Vector3 Pos0, Vel, Axis; public float Spin, Size, Rnd; public bool A; }
        sealed class Burst { public float T0, ThH; public Vector3 Face; public Orb[] Orbs; public float CapRnd; }
        /// <summary>The state of one wall column: apex height above the cast point, total inward turning of its lip, the turning exponent, local erosion 0..1.</summary>
        struct Col { public float H, Phi, Pow, Er; }

        /// <summary>One mesh + one material for all the cards that share a texture.</summary>
        sealed class Batch
        {
            public Mesh Mesh; public Material Mat;
            public readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<int> T = new List<int>();
            public void Clear() { V.Clear(); N.Clear(); UV.Clear(); C.Clear(); T.Clear(); }
            public void Apply()
            {
                Mesh.Clear();
                if (V.Count == 0) return;
                Mesh.SetVertices(V); Mesh.SetNormals(N); Mesh.SetUVs(0, UV); Mesh.SetColors(C); Mesh.SetTriangles(T, 0); Mesh.RecalculateBounds();
            }
        }

        // ---------------------------------------------------------------- small maths
        static float Sm(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
        static float EaseBack(float t) { t = Mathf.Clamp01(t); const float c1 = 1.70158f, c3 = c1 + 1f; return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f); }
        static float Wrap(float a) => Mathf.Repeat(a + Mathf.PI, 2f * Mathf.PI) - Mathf.PI;
        static float Back(float th) => Sm(0.55f, 1f, Mathf.Abs(th) / Mathf.PI);
        Vector3 Dir(float th) => _f * Mathf.Cos(th) + _r * Mathf.Sin(th);
        Vector3 Tan(float th) => -_f * Mathf.Sin(th) + _r * Mathf.Cos(th);
        float Rot(float t) => _spin * SpinRate * t;
        float WallLag(float th) => 0.06f * Sm(0.1f, 1f, Mathf.Abs(th) / Mathf.PI);

        /// <summary>The base wall's height above the cast point at angle th (0 = the aim) and time t: front 1.6, side 1.3, the camera-side arc 0.5; a 5- and 8-lobe swell that turns round the
        /// ring (integer lobes: the seam closes); rise with ease-back, then the sink. (The crest peaks and the hit wave are added in Eval.)</summary>
        float Height(float th, float t)
        {
            float a = Mathf.Abs(th) / Mathf.PI;
            float hmax = Mathf.Lerp(Mathf.Lerp(HFront, HSide, Sm(0.15f, 0.55f, a)), HBack, Back(th));
            float psi = th - Rot(t);
            float swell = 1f + (0.11f * Mathf.Sin(5f * psi) + 0.05f * Mathf.Sin(8f * psi + 1.3f)) * (1f - 0.6f * Back(th));
            float rise = EaseBack((t - WallLag(th)) / RiseEnd);
            float sink = 1f - (1f - SinkTo) * Sm(SinkA, SinkB, t);
            return Mathf.Max(0f, hmax * swell * rise * sink);
        }

        /// <summary>Everything that shapes column th at time t: the base height + the crest peaks (+ their curl) + the per-hit waves (rear up, curl strongly, collapse, erode). Periodic in th.</summary>
        Col Eval(float th, float t)
        {
            float lag = WallLag(th), back = Back(th);
            float rise = Mathf.Max(0f, EaseBack((t - lag) / RiseEnd));
            float sink = 1f - (1f - SinkTo) * Sm(SinkA, SinkB, t);
            float h = Height(th, t), phi = 0f, pw = CurlPow, er = 0f;
            float grow = Sm(CurlA, CurlB, t - lag);
            float rot = Rot(t);
            for (int i = 0; i < _peaks.Length; i++)
            {
                float b = 1f - Mathf.Abs(Wrap(th - (_peaks[i].Th0 + rot))) / _peaks[i].HalfW;
                if (b <= 0f) continue;
                float fade = 1f - 0.6f * back;
                h += _peaks[i].Amp * fade * Mathf.Pow(b, PeakHeightPow) * rise * sink;
                phi = Mathf.Max(phi, _peaks[i].Phi * Sm(0.05f, 0.8f, b) * grow * (1f - 0.5f * back));
            }
            for (int k = 0; k < _bursts.Count; k++)
            {
                var bu = _bursts[k];
                float a = t - bu.T0;
                if (a < 0f || a > BurstLife) continue;
                float b = 1f - Mathf.Abs(Wrap(th - bu.ThH)) / (HitHalfDeg * Mathf.Deg2Rad);
                if (b <= 0f) continue;
                float wt = Sm(0f, 0.65f, b);
                float coll = 1f - Sm(HitCollA, HitCollB, a);
                float ramp = Mathf.Min(1.08f, EaseBack(a / HitRise));
                float target = HitHeight * sink;
                h += Mathf.Max(0f, target - h) * wt * ramp * coll;                   // overrides the low arc too: the target does not depend on the base height
                float kh = wt * Sm(0.02f, 0.3f, a) * coll;
                phi = Mathf.Lerp(phi, HitPhiDeg * Mathf.Deg2Rad, kh);
                pw = Mathf.Lerp(pw, HitPow, kh);
                er = Mathf.Max(er, Sm(HitErA, HitErB, a) * Sm(0.05f, 0.4f, b));
            }
            return new Col { H = h, Phi = phi, Pow = pw, Er = er };
        }

        /// <summary>The band's turning angle at row fraction g (0 bottom .. 1 top): leans outward at the foot, turns inward (toward the ring centre) by c.Phi in total, mostly near the top.</summary>
        static float PhiAt(in Col c, float g) => -LeanDeg * Mathf.Deg2Rad + c.Phi * Mathf.Pow(g, c.Pow);

        /// <summary>Lays the column's profile into _pp[0..NV-1] (world positions, uniform in arc length) and returns the arc length S. The curve starts at the foot (R, -Sink) and turns by PhiAt;
        /// S is chosen so that its highest point is exactly the column's apex height H (the curled lip is longer than the height).</summary>
        float Prof(float th, in Col c)
        {
            const int seg = NV - 1;
            Vector3 nin = -Dir(th);
            float y = 0f, apex = 0f;
            for (int j = 0; j < seg; j++) { y += Mathf.Cos(PhiAt(c, (j + 0.5f) / seg)) / seg; if (y > apex) apex = y; }
            float S = (c.H + Sink) / Mathf.Max(apex, 0.3f);
            Vector3 p = _o + Dir(th) * _R; p.y = _o.y - Sink;
            _pp[0] = p;
            float step = S / seg;
            for (int j = 1; j <= seg; j++)
            {
                float ph = PhiAt(c, (j - 0.5f) / seg);
                p += (Vector3.up * Mathf.Cos(ph) + nin * Mathf.Sin(ph)) * step;
                _pp[j] = p;
            }
            return S;
        }

        /// <summary>The surface point at row fraction g (needs Prof to have just run for this column), the inward-facing surface normal there, and the tangent up the sheet.</summary>
        void Surf(float th, in Col c, float g, out Vector3 pos, out Vector3 nrm, out Vector3 tng)
        {
            float x = Mathf.Clamp01(g) * (NV - 1);
            int j = Mathf.Min((int)x, NV - 2);
            pos = Vector3.Lerp(_pp[j], _pp[j + 1], x - j);
            float ph = PhiAt(c, g);
            Vector3 nin = -Dir(th);
            tng = Vector3.up * Mathf.Cos(ph) + nin * Mathf.Sin(ph);
            nrm = nin * Mathf.Cos(ph) - Vector3.up * Mathf.Sin(ph);
        }

        /// <summary>The lip tip of column th: position, direction the sheet is heading there, and the normal on the side it keeps turning toward. False when the column is too low.</summary>
        bool Lip(float th, float t, out Vector3 tip, out Vector3 tng, out Vector3 nrm)
        {
            var c = Eval(th, t);
            Prof(th, c);
            Surf(th, c, 1f, out tip, out nrm, out tng);
            return c.H >= 0.05f;
        }

        // ---------------------------------------------------------------- ISpellFxPart
        public void OnBegin(SpellFxCast cast)
        {
            _o = cast.Origin; _f = cast.Forward.sqrMagnitude > 1e-6f ? cast.Forward.normalized : Vector3.forward; _r = Vector3.Cross(Vector3.up, _f);
            _spin = (cast.CastId & 1) == 0 ? 1f : -1f;
            _R = cast.Radius > 0.5f ? cast.Radius : radius;
            _tScale = BaseLife / Mathf.Max(0.3f, lifeSeconds);
            _cells = Mathf.Max(8, Mathf.RoundToInt(2f * Mathf.PI * _R / (2f * ScaleR)));      // a whole number of scales round the ring: the seam closes
            var sh = patternShader != null ? patternShader : Shader.Find("Oheangbu/SpellFx/Pattern");
            if (sh == null) { Debug.LogWarning("SpellFxPatternRingPart: shader Oheangbu/SpellFx/Pattern not found; nothing is drawn."); _dead = true; return; }

            _wallMat = MakeMat(sh, 0f, null);
            _wallMat.SetFloat(ScaleRId, ScaleR); _wallMat.SetFloat(CellsId, _cells);
            _inkMat = MakeMat(sh, 2f, inkTex);
            _inkMat.SetFloat(SrcId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha); _inkMat.SetFloat(DstId, (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); _inkMat.SetFloat(ZWriteId, 0f);
            _inkMat.renderQueue = 3000; _inkMat.SetOverrideTag("RenderType", "Transparent");
            _wallMesh = MakeObject("Wall", _wallMat); _inkMesh = MakeObject("Ink", _inkMat);
            _crest = MakeBatch("Crests", sh, crestTex); _orbA = MakeBatch("RoundelsA", sh, burstTexA != null ? burstTexA : crestTex); _orbB = MakeBatch("RoundelsB", sh, burstTexB != null ? burstTexB : crestTex);

            var rnd = new System.Random((int)(cast.CastId * 7919 + 31));
            _peaks = new Peak[PeakCount];
            for (int i = 0; i < PeakCount; i++)
            {
                float th = Mathf.Lerp(-PeakArc, PeakArc, (i + 0.5f) / PeakCount) + ((float)rnd.NextDouble() - 0.5f) * 0.16f;
                _peaks[i] = new Peak
                {
                    Th0 = th,
                    Amp = Mathf.Lerp(PeakAmpMin, PeakAmpMax, (float)rnd.NextDouble()),
                    HalfW = Mathf.Lerp(PeakHalfWMinDeg, PeakHalfWMaxDeg, (float)rnd.NextDouble()) * Mathf.Deg2Rad,
                    Phi = Mathf.Lerp(PeakPhiMinDeg, PeakPhiMaxDeg, (float)rnd.NextDouble()) * Mathf.Deg2Rad,
                    Size = Mathf.Lerp(CapSizeMin, CapSizeMax, (float)rnd.NextDouble()),
                    Rnd = (float)rnd.NextDouble(),
                };
            }
            _ribs = new Rib[InkCount];
            float[] starts = { -1.9f, -0.1f, 1.5f };
            for (int i = 0; i < InkCount; i++)
                _ribs[i] = new Rib
                {
                    Th0 = starts[i % starts.Length] + ((float)rnd.NextDouble() - 0.5f) * 0.3f,
                    Span = Mathf.Lerp(100f, 140f, (float)rnd.NextDouble()) * Mathf.Deg2Rad,
                    Width = Mathf.Lerp(0.18f, 0.30f, (float)rnd.NextDouble()),
                    At = 0.15f + 0.05f * i, YF = Mathf.Lerp(0.38f, 0.6f, (float)rnd.NextDouble()), Lift = Mathf.Lerp(0.3f, 0.55f, (float)rnd.NextDouble()),
                };
            VfxAfterFog308.Route(transform);          // the transparent ink goes after the fog; the cutout pieces write depth and stay where they are
        }

        public void OnTick(SpellFxCast cast, float age)
        {
            if (_dead || _wallMat == null) return;
            float t = age * _tScale;
            _wallMat.SetFloat(ErodeId, Sm(ErodeA, ErodeB, t));
            BuildWall(t);
            BuildInk(t);
            _crest.Clear(); _orbA.Clear(); _orbB.Clear();
            BuildCaps(t);
            BuildBursts(t);
            _crest.Apply(); _orbA.Apply(); _orbB.Apply();
        }

        public void OnHit(SpellFxCast cast, SpellFxHit hit)
        {
            if (_dead || _crest == null || _bursts.Count >= MaxBursts) return;
            Vector3 feet = hit.Feet.sqrMagnitude > 1e-6f ? hit.Feet : new Vector3(hit.Position.x, _o.y, hit.Position.z);
            Vector3 rel = feet - _o; rel.y = 0f;
            float thH = Mathf.Atan2(Vector3.Dot(rel, _r), Vector3.Dot(rel, _f));            // the hit angle round the cast origin (Dir(th) = f cos + r sin)
            Vector3 face = -rel;
            face = face.sqrMagnitude > 1e-4f ? face.normalized : -_f;                  // the roundels face the caster (the camera side)
            var rnd = new System.Random((int)(cast.CastId * 104729 + (_bursts.Count + 1) * 977));
            var b = new Burst { T0 = Mathf.Max(0f, cast.AgeAt(hit.NowUs) * _tScale), ThH = thH, Face = face, CapRnd = (float)rnd.NextDouble() };
            int n = rnd.Next(MinOrbs, MaxOrbs + 1);
            b.Orbs = new Orb[n];
            for (int i = 0; i < n; i++)
            {
                float ang = (float)rnd.NextDouble() * Mathf.PI * 2f, sp = Mathf.Lerp(1.2f, 2.8f, (float)rnd.NextDouble());
                Vector3 ax = new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f);
                ax = ax.sqrMagnitude > 1e-4f ? ax.normalized : Vector3.up;
                b.Orbs[i] = new Orb
                {
                    Pos0 = feet + Vector3.up * 0.5f + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 0.15f,
                    Vel = new Vector3(Mathf.Cos(ang) * sp, Mathf.Lerp(2.5f, 4.5f, (float)rnd.NextDouble()), Mathf.Sin(ang) * sp),
                    Axis = ax,
                    Spin = Mathf.Lerp(7f, 14f, (float)rnd.NextDouble()) * (rnd.NextDouble() < 0.5 ? -1f : 1f),      // a tumble (rad/s) about a random axis: the card turns edge-on, never a flat sticker
                    Size = Mathf.Lerp(OrbSizeMin, OrbSizeMax, (float)rnd.NextDouble()), Rnd = (float)rnd.NextDouble(), A = rnd.NextDouble() < 0.5,
                };
            }
            _bursts.Add(b);
        }

        public void OnEnd(SpellFxCast cast, SpellFxEndReason reason)
        {
            if (reason != SpellFxEndReason.Aborted) return;        // Done / TimedOut: the 1.8 s animation plays out through the player's tail
            _dead = true;
            if (_wallMesh != null) _wallMesh.Clear(); if (_inkMesh != null) _inkMesh.Clear();
            if (_crest != null) { _crest.Mesh.Clear(); _orbA.Mesh.Clear(); _orbB.Mesh.Clear(); }
        }

        // ---------------------------------------------------------------- scaffolding (the style of SpellFxRingWavePart)
        Material MakeMat(Shader sh, float mode, Texture tex)
        {
            var m = new Material(sh) { hideFlags = HideFlags.DontSave };
            m.SetFloat(ModeId, mode);
            if (tex != null) m.SetTexture(TexId, tex);
            return m;
        }

        Mesh MakeObject(string name, Material m)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false); go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave }; mesh.MarkDynamic(); mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            return mesh;
        }

        Batch MakeBatch(string name, Shader sh, Texture tex)
        {
            var b = new Batch { Mat = MakeMat(sh, 1f, tex) };
            b.Mesh = MakeObject(name, b.Mat);
            return b;
        }

        // ---------------------------------------------------------------- the wall
        void BuildWall(float t)
        {
            _v.Clear(); _n.Clear(); _uv.Clear(); _uv1.Clear(); _t.Clear();
            if (t < BaseLife - 0.02f)
            {
                float L = _cells * 2f * ScaleR;                 // metres round the ring = a whole number of scales
                float shift = Rot(t) * _R;                        // the pattern turns with the swell
                for (int iu = 0; iu <= NU; iu++)
                {
                    float f = iu / (float)NU, th = (f * 2f - 1f) * Mathf.PI;
                    var c = Eval(th, t);
                    float S = Prof(th, c);
                    _colH[iu] = c.H;
                    float u = f * L - shift;
                    for (int iv = 0; iv < NV; iv++)
                    {
                        float g = iv / (NV - 1f);
                        Surf(th, c, g, out Vector3 sp, out Vector3 sn, out _);
                        _v.Add(_pp[iv]); _n.Add(sn);                                        // _n holds the analytic normal for now; refined below from the real surface
                        _uv.Add(new Vector2(u, g * S));                                      // v = arc length up the curved surface: the scales follow the curl without stretching
                        _uv1.Add(new Vector2(S, c.Er));                                      // x = the band's total length (top clip + cream rim), y = local erosion (hit waves)
                    }
                }
                // normals from the actual surface (cross of the two finite differences); the analytic one only picks the side and is the fallback
                for (int iu = 0; iu <= NU; iu++)
                {
                    int u0 = Mathf.Max(0, iu - 1), u1 = Mathf.Min(NU, iu + 1);
                    for (int iv = 0; iv < NV; iv++)
                    {
                        int v0 = Mathf.Max(0, iv - 1), v1 = Mathf.Min(NV - 1, iv + 1);
                        Vector3 du = _v[u1 * NV + iv] - _v[u0 * NV + iv], dv = _v[iu * NV + v1] - _v[iu * NV + v0];
                        Vector3 nn = Vector3.Cross(du, dv), rf = _n[iu * NV + iv];
                        if (nn.sqrMagnitude > 1e-12f) { nn.Normalize(); if (Vector3.Dot(nn, rf) < 0f) nn = -nn; _n[iu * NV + iv] = nn; }
                    }
                }
                for (int iu = 0; iu < NU; iu++)
                {
                    if (_colH[iu] < 0.02f && _colH[iu + 1] < 0.02f) continue;
                    for (int iv = 0; iv < NV - 1; iv++)
                    {
                        int a = iu * NV + iv, a1 = a + 1, c2 = a + NV, d = c2 + 1;
                        _t.Add(a); _t.Add(c2); _t.Add(a1); _t.Add(a1); _t.Add(c2); _t.Add(d);
                    }
                }
            }
            _wallMesh.Clear();
            if (_v.Count > 0)
            {
                _wallMesh.SetVertices(_v); _wallMesh.SetNormals(_n); _wallMesh.SetUVs(0, _uv); _wallMesh.SetUVs(1, _uv1); _wallMesh.SetTriangles(_t, 0); _wallMesh.RecalculateBounds();
            }
            _c.Clear();
        }

        // ---------------------------------------------------------------- one cloud-scroll card at each lip tip
        void BuildCaps(float t)
        {
            float rot = Rot(t);
            foreach (var pk in _peaks)
            {
                float th = Wrap(pk.Th0 + rot);
                float lag = WallLag(th);
                float p = EaseBack((t - lag - 0.2f) / 0.3f);         // after the curl has started to form
                if (p < 0.02f) continue;
                float er = Sm(ErodeA + 0.12f * pk.Rnd, ErodeB - 0.05f, t);
                if (er >= 0.995f) continue;
                if (!Lip(th, t, out Vector3 tip, out Vector3 tng, out Vector3 nrm)) continue;
                float size = pk.Size * p;
                float curl = CapExtraCurlDeg * Mathf.Deg2Rad * Sm(0.1f, 0.9f, t - lag);
                AddCard(_crest, tip - tng * (CapBackFrac * size) + nrm * 0.02f, Tan(th), tng, nrm, size, size * 0.95f, curl, new Color(er, pk.Rnd, 0f, 1f));
            }
        }

        // ---------------------------------------------------------------- ink ribbons
        void BuildInk(float t)
        {
            _v.Clear(); _uv.Clear(); _c.Clear(); _t.Clear();
            float fade = 1f - Sm(1.0f, 1.55f, t);
            if (fade > 0.01f)
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
                        var col = Eval(th, t);
                        if (col.H < 0.2f || Back(th) > InkBack) { prev = -1; continue; }
                        Prof(th, col);
                        float gf = Mathf.Clamp(rib.YF + 0.3f * rib.Lift * s * s * g, 0.2f, 0.78f);      // about mid height of the sheet, the head rising a little
                        Surf(th, col, gf, out Vector3 sp, out Vector3 sn, out Vector3 st);
                        Vector3 c = sp + sn * InkInset;                    // just in front of the sheet (its inward-facing side)
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

        // ---------------------------------------------------------------- the burst where a contact lands (the wave itself is in Eval; this adds its lip card and the roundels)
        void BuildBursts(float t)
        {
            foreach (var b in _bursts)
            {
                float a = t - b.T0;
                if (a < 0f || a > BurstLife) continue;
                // the big lip tip
                {
                    float p = EaseBack((a - 0.1f) / 0.25f);
                    float er = Sm(HitErA + 0.05f, HitErB, a);
                    if (p >= 0.02f && er < 0.995f && Lip(b.ThH, t, out Vector3 tip, out Vector3 tng, out Vector3 nrm))
                    {
                        float size = HitCapSize * p;
                        float curl = CapExtraCurlDeg * Mathf.Deg2Rad * 1.4f * Sm(0.05f, 0.4f, a);
                        AddCard(_crest, tip - tng * (CapBackFrac * size) + nrm * 0.02f, Tan(b.ThH), tng, nrm, size, size * 0.95f, curl, new Color(er, b.CapRnd, 0f, 1f));
                    }
                }
                Vector3 tan = Vector3.Cross(Vector3.up, b.Face);
                foreach (var o in b.Orbs)
                {
                    float er = Sm(0.15f, 0.6f, a);
                    if (er >= 0.995f) continue;
                    Vector3 pos = o.Pos0 + o.Vel * a + 0.5f * a * a * Vector3.down * Gravity;
                    float size = o.Size * Sm(0f, 0.06f, a);
                    if (size < 0.01f) continue;
                    Quaternion q = Quaternion.AngleAxis((o.Spin * a + o.Rnd * 6.28f) * Mathf.Rad2Deg, o.Axis);
                    AddQuad(o.A ? _orbA : _orbB, pos, q * tan, q * Vector3.up, q * b.Face, size, new Color(er, o.Rnd, 0f, 1f));
                }
            }
        }

        // ---------------------------------------------------------------- card geometry
        /// <summary>A card standing at <paramref name="basePos"/> (the middle of its lower edge), <paramref name="w"/> wide along <paramref name="tan"/>, <paramref name="h"/> long along <paramref name="up"/>;
        /// as a strip it bends toward <paramref name="face"/> by up to <paramref name="curl"/> radians at the tip (the curl grows toward the top).</summary>
        static void AddCard(Batch b, Vector3 basePos, Vector3 tan, Vector3 up, Vector3 face, float w, float h, float curl, Color col)
        {
            const int rows = CapRows;
            int b0 = b.V.Count; Vector3 p = basePos, half = tan * (w * 0.5f); float step = h / rows;
            for (int j = 0; j <= rows; j++)
            {
                float s = j / (float)rows;
                if (j > 0) { float pm = curl * Mathf.Pow((j - 0.5f) / rows, 1.6f); p += (up * Mathf.Cos(pm) + face * Mathf.Sin(pm)) * step; }
                float psi = curl * Mathf.Pow(s, 1.6f);
                Vector3 nrm = (face * Mathf.Cos(psi) - up * Mathf.Sin(psi)).normalized;
                b.V.Add(p - half); b.V.Add(p + half); b.N.Add(nrm); b.N.Add(nrm);
                b.UV.Add(new Vector2(0f, s)); b.UV.Add(new Vector2(1f, s)); b.C.Add(col); b.C.Add(col);
                if (j > 0) { int a = b0 + (j - 1) * 2; b.T.Add(a); b.T.Add(a + 2); b.T.Add(a + 1); b.T.Add(a + 1); b.T.Add(a + 2); b.T.Add(a + 3); }
            }
        }

        /// <summary>A flat square card with the given in-plane axes (<paramref name="ax"/> across, <paramref name="ay"/> up the texture) and normal <paramref name="face"/>, centred at <paramref name="centre"/>.</summary>
        static void AddQuad(Batch b, Vector3 centre, Vector3 ax, Vector3 ay, Vector3 face, float size, Color col)
        {
            ax *= size * 0.5f; ay *= size * 0.5f;
            int b0 = b.V.Count;
            b.V.Add(centre - ax - ay); b.V.Add(centre + ax - ay); b.V.Add(centre - ax + ay); b.V.Add(centre + ax + ay);
            for (int i = 0; i < 4; i++) { b.N.Add(face); b.C.Add(col); }
            b.UV.Add(new Vector2(0f, 0f)); b.UV.Add(new Vector2(1f, 0f)); b.UV.Add(new Vector2(0f, 1f)); b.UV.Add(new Vector2(1f, 1f));
            b.T.Add(b0); b.T.Add(b0 + 2); b.T.Add(b0 + 1); b.T.Add(b0 + 1); b.T.Add(b0 + 2); b.T.Add(b0 + 3);
        }

        void OnDestroy()
        {
            if (_wallMesh != null) Destroy(_wallMesh); if (_inkMesh != null) Destroy(_inkMesh);
            if (_wallMat != null) Destroy(_wallMat); if (_inkMat != null) Destroy(_inkMat);
            foreach (var b in new[] { _crest, _orbA, _orbB })
            {
                if (b == null) continue;
                if (b.Mesh != null) Destroy(b.Mesh);
                if (b.Mat != null) Destroy(b.Mat);
            }
        }
    }
}
