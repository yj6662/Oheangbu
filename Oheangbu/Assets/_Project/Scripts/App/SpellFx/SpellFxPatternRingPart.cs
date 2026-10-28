using System.Collections.Generic;
using Oheangbu.App;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>D308-153b route P (SPEC-SPELL-FX-RESTART-308 section 7) for 우: the water ring drawn as Korean traditional pattern, not as water. One band round the caster painted with
    /// seigaiha scales by Oheangbu/SpellFx/Pattern (mode 0), cloud-scroll crest cards standing on its top and curling, 2 - 3 black brush ribbons sweeping along it, and a burst of big
    /// crests + thrown roundels where a contact lands. Everything is built every tick from the scope-clock age (no coroutine, no Time call, no Physics); the meshes are laid in
    /// WORLD space under children at the world origin. The look only: never moves a gameplay object.</summary>
    public sealed class SpellFxPatternRingPart : MonoBehaviour, ISpellFxPart
    {
        [SerializeField] Shader patternShader;
        [SerializeField] Texture2D crestTex, burstTexA, burstTexB, inkTex;
        [SerializeField] float radius = 2.2f;
        [SerializeField] float lifeSeconds = 1.8f;

        // ---- tunables (metres / seconds on the 1.8 s base timeline; lifeSeconds stretches the whole timeline) ----
        const float BaseLife = 1.8f;
        const int NU = 200, NV = 3;                                   // wall columns round the circle, rows up the band
        const float ScaleR = 0.32f;      // r4: bigger scales (r3 at .22 read as fine lace at game distance)                                   // seigaiha scale radius (the shader's _ScaleR)
        const float Sink = 0.25f;                                     // the band reaches this far below the cast point (uneven ground)
        const float LeanDeg = 7f;                                     // the band leans outward by this much
        const float HFront = 1.6f, HSide = 1.3f, HBack = 0.5f;        // wall height in front of / beside / behind (camera side) the caster
        const float RiseEnd = 0.35f;                                  // rise with ease-back 0 .. RiseEnd
        const float ErodeA = 1.0f, ErodeB = 1.75f;                    // scales vanish between these moments
        const float SinkA = 1.0f, SinkB = 1.6f, SinkTo = 0.4f;        // the band sinks to 40 % by SinkB
        const float SpinRate = 0.16f;                                 // rad/s the pattern and the swell turn round the ring
        const int CapCount = 10, CapRows = 6;                         // crest cards on the wall top
        const float CapArc = 2.45f;                                   // cards spread over theta = -CapArc .. +CapArc (the front 3/4, none on the low arc behind)
        const int InkCount = 3, InkSegs = 26;
        const float InkBack = 0.6f;                                   // a ribbon is not drawn where the wall is the low camera-side arc
        const int MaxBursts = 2, BigCrests = 3, MinOrbs = 5, MaxOrbs = 7;
        const float BurstLife = 1.1f, Gravity = 7.5f;

        static readonly int ModeId = Shader.PropertyToID("_Mode"), TexId = Shader.PropertyToID("_MainTex"), ScaleRId = Shader.PropertyToID("_ScaleR"), CellsId = Shader.PropertyToID("_Cells"),
            ErodeId = Shader.PropertyToID("_Erode"), SrcId = Shader.PropertyToID("_SrcBlend"), DstId = Shader.PropertyToID("_DstBlend"), ZWriteId = Shader.PropertyToID("_ZWrite");

        // ---- state ----
        Vector3 _o, _f, _r;
        float _spin = 1f, _R = 2.2f, _tScale = 1f, _tanLean;
        int _cells;
        bool _dead;
        Mesh _wallMesh, _inkMesh;
        Material _wallMat, _inkMat;
        Batch _crest, _orbA, _orbB;
        Cap[] _caps;
        Rib[] _ribs;
        readonly List<Burst> _bursts = new List<Burst>();
        // scratch for the wall and the ribbons
        readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>(), _uv1 = new List<Vector2>();
        readonly List<Color> _c = new List<Color>();
        readonly List<int> _t = new List<int>();

        struct Cap { public float Th, Size, Curl, Rnd; }
        struct Rib { public float Th0, Span, Width, At, YF, Lift; }
        struct Crest { public Vector3 Pos, Face; public float Size, Delay, Curl, Rnd; }
        struct Orb { public Vector3 Pos0, Vel; public float Spin, Size, Rnd; public bool A; }
        sealed class Burst { public float T0; public Vector3 Face; public Crest[] Crests; public Orb[] Orbs; }

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

        /// <summary>The wall's height above the cast point at angle th (0 = the aim) and time t: front 1.6, side 1.3, the camera-side arc 0.5; a 5- and 8-lobe swell that turns round the
        /// ring (integer lobes: the seam closes); rise with ease-back, then the sink.</summary>
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

        // ---------------------------------------------------------------- ISpellFxPart
        public void OnBegin(SpellFxCast cast)
        {
            _o = cast.Origin; _f = cast.Forward.sqrMagnitude > 1e-6f ? cast.Forward.normalized : Vector3.forward; _r = Vector3.Cross(Vector3.up, _f);
            _spin = (cast.CastId & 1) == 0 ? 1f : -1f;
            _R = cast.Radius > 0.5f ? cast.Radius : radius;
            _tScale = BaseLife / Mathf.Max(0.3f, lifeSeconds);
            _tanLean = Mathf.Tan(LeanDeg * Mathf.Deg2Rad);
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
            _caps = new Cap[CapCount];
            for (int i = 0; i < CapCount; i++)
            {
                float th = Mathf.Lerp(-CapArc, CapArc, (i + 0.5f) / CapCount) + ((float)rnd.NextDouble() - 0.5f) * 0.16f;
                float size = Mathf.Clamp(Mathf.Lerp(1.15f, 0.75f, Mathf.Abs(th) / CapArc) * (0.9f + 0.2f * (float)rnd.NextDouble()), 0.7f, 1.2f);      // r4: bigger caps (r3: small badges)
                _caps[i] = new Cap { Th = th, Size = size, Curl = Mathf.Lerp(15f, 30f, (float)rnd.NextDouble()) * Mathf.Deg2Rad, Rnd = (float)rnd.NextDouble() };
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
            Vector3 face = _o - feet; face.y = 0f;
            face = face.sqrMagnitude > 1e-4f ? face.normalized : -_f;                  // the cards face the caster (the camera side)
            var rnd = new System.Random((int)(cast.CastId * 104729 + (_bursts.Count + 1) * 977));
            var b = new Burst { T0 = Mathf.Max(0f, cast.AgeAt(hit.NowUs) * _tScale), Face = face, Crests = new Crest[BigCrests] };
            for (int i = 0; i < BigCrests; i++)
            {
                float phi = (i - (BigCrests - 1) * 0.5f) * 40f;                        // a fan: -40, 0, +40 degrees
                Vector3 fi = Quaternion.AngleAxis(phi, Vector3.up) * face;
                float size = Mathf.Lerp(1.7f, 2.2f, (float)rnd.NextDouble()) * (i == 1 ? 1f : 0.85f);      // r4: taller than the 1.6 m wall
                b.Crests[i] = new Crest { Face = fi, Pos = feet + fi * 0.35f + Vector3.down * 0.05f, Size = size, Delay = 0.05f * i, Curl = Mathf.Lerp(32f, 45f, (float)rnd.NextDouble()) * Mathf.Deg2Rad, Rnd = (float)rnd.NextDouble() };
            }
            int n = rnd.Next(MinOrbs, MaxOrbs + 1);
            b.Orbs = new Orb[n];
            for (int i = 0; i < n; i++)
            {
                float ang = (float)rnd.NextDouble() * Mathf.PI * 2f, sp = Mathf.Lerp(1.2f, 2.8f, (float)rnd.NextDouble());
                b.Orbs[i] = new Orb
                {
                    Pos0 = feet + Vector3.up * 0.5f + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 0.15f,
                    Vel = new Vector3(Mathf.Cos(ang) * sp, Mathf.Lerp(2.5f, 4.5f, (float)rnd.NextDouble()), Mathf.Sin(ang) * sp),
                    Spin = Mathf.Lerp(4f, 9f, (float)rnd.NextDouble()) * (rnd.NextDouble() < 0.5 ? -1f : 1f),
                    Size = Mathf.Lerp(0.15f, 0.30f, (float)rnd.NextDouble()), Rnd = (float)rnd.NextDouble(), A = rnd.NextDouble() < 0.5,
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
                float lean = LeanDeg * Mathf.Deg2Rad;
                for (int iu = 0; iu <= NU; iu++)
                {
                    float f = iu / (float)NU, th = (f * 2f - 1f) * Mathf.PI;
                    float h = Height(th, t), tot = h + Sink;
                    Vector3 dir = Dir(th);
                    Vector3 nrm = (-dir * Mathf.Cos(lean) + Vector3.up * Mathf.Sin(lean)).normalized;
                    float u = f * L - shift;
                    for (int iv = 0; iv < NV; iv++)
                    {
                        float g = iv / (NV - 1f), up = g * tot;
                        Vector3 p = _o + dir * (_R + up * _tanLean); p.y = _o.y - Sink + up;
                        _v.Add(p); _n.Add(nrm); _uv.Add(new Vector2(u, up)); _uv1.Add(new Vector2(tot, 0f));
                    }
                }
                for (int iu = 0; iu < NU; iu++)
                {
                    if (_v[iu * NV + NV - 1].y - _o.y < 0.02f && _v[(iu + 1) * NV + NV - 1].y - _o.y < 0.02f) continue;
                    for (int iv = 0; iv < NV - 1; iv++)
                    {
                        int a = iu * NV + iv, a1 = a + 1, c = a + NV, d = c + 1;
                        _t.Add(a); _t.Add(c); _t.Add(a1); _t.Add(a1); _t.Add(c); _t.Add(d);
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

        // ---------------------------------------------------------------- crest caps on the wall top
        void BuildCaps(float t)
        {
            foreach (var cap in _caps)
            {
                float lag = WallLag(cap.Th);
                float p = EaseBack((t - 0.1f - lag) / 0.3f);
                if (p < 0.02f) continue;
                float er = Sm(ErodeA + 0.12f * cap.Rnd, ErodeB - 0.05f, t);
                if (er >= 0.995f) continue;
                float hc = Height(cap.Th, t);
                if (hc < 0.05f) continue;
                float size = cap.Size * p;
                float curl = cap.Curl * Sm(0.1f, 0.9f, t - lag);       // tips forward (toward the caster) as it curls
                Vector3 dir = Dir(cap.Th);
                float rtop = _R + (hc + Sink) * _tanLean - 0.03f;
                Vector3 b = _o + dir * rtop; b.y = _o.y + hc - 0.42f * size;      // r2: sunk well into the wall top (r1 at -0.14: floating badges)
                AddCard(_crest, b, Tan(cap.Th), Vector3.up, -dir, size, size * 0.95f, curl, new Color(er, cap.Rnd, 0f, 1f));
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
                        float h = Height(th, t);
                        if (h < 0.2f || Back(th) > InkBack) { prev = -1; continue; }
                        float y = h * rib.YF + 0.15f + rib.Lift * s * s * g;
                        float rad = _R + (y + Sink) * _tanLean - 0.06f;    // just inside the wall face
                        Vector3 c = _o + Dir(th) * rad; c.y = _o.y + y;
                        float wd = rib.Width * 0.5f * (0.35f + 0.65f * Sm(0f, 0.2f, s));
                        float a = fade * Sm(0f, 0.08f, s);
                        int k0 = _v.Count;
                        _v.Add(c - Vector3.up * wd); _v.Add(c + Vector3.up * wd);
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

        // ---------------------------------------------------------------- the burst where a contact lands
        void BuildBursts(float t)
        {
            foreach (var b in _bursts)
            {
                float a = t - b.T0;
                if (a < 0f || a > BurstLife) continue;
                foreach (var cr in b.Crests)
                {
                    float ca = a - cr.Delay;
                    float p = EaseBack(ca / 0.3f);
                    float er = Sm(0.6f, 1.05f, a);
                    if (p < 0.02f || er >= 0.995f) continue;
                    float size = cr.Size * p;
                    float curl = cr.Curl * Sm(0.05f, 0.35f, ca);             // rises, then curls over the target (the card stands behind it, leaning toward the caster)
                    AddCard(_crest, cr.Pos - Vector3.up * 0.1f * size, Vector3.Cross(Vector3.up, cr.Face), Vector3.up, cr.Face, size, size * 0.95f, curl, new Color(er, cr.Rnd, 0f, 1f));
                }
                Vector3 tan = Vector3.Cross(Vector3.up, b.Face);
                foreach (var o in b.Orbs)
                {
                    float er = Sm(0.15f, 0.6f, a);
                    if (er >= 0.995f) continue;
                    Vector3 pos = o.Pos0 + o.Vel * a + 0.5f * a * a * Vector3.down * Gravity;
                    float size = o.Size * Sm(0f, 0.06f, a);
                    if (size < 0.01f) continue;
                    AddQuad(o.A ? _orbA : _orbB, pos, tan, Vector3.up, b.Face, size, o.Spin * a + o.Rnd * 6.28f, new Color(er, o.Rnd, 0f, 1f));
                }
            }
        }

        // ---------------------------------------------------------------- card geometry
        /// <summary>A card standing at <paramref name="basePos"/> (the middle of its lower edge), <paramref name="w"/> wide along <paramref name="tan"/>, <paramref name="h"/> tall; as a strip it bends
        /// toward <paramref name="face"/> by up to <paramref name="curl"/> radians at the tip (the curl grows toward the top).</summary>
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

        /// <summary>A flat square card rolled by <paramref name="roll"/> radians about its normal (<paramref name="face"/>).</summary>
        static void AddQuad(Batch b, Vector3 centre, Vector3 tan, Vector3 up, Vector3 face, float size, float roll, Color col)
        {
            float cs = Mathf.Cos(roll) * size * 0.5f, sn = Mathf.Sin(roll) * size * 0.5f;
            Vector3 ax = tan * cs + up * sn, ay = -tan * sn + up * cs;
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
