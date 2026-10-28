using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>A grid sheet for the showcase particle shader (position, vertex colour = life, uv = u along / v across / z random): the 우 wave wall and its foam cap.</summary>
    public sealed class Vol2FsSheet
    {
        readonly List<Vector3> _v = new List<Vector3>(4096);
        readonly List<Color> _c = new List<Color>(4096);
        readonly List<Vector3> _uv = new List<Vector3>(4096);
        readonly List<int> _i = new List<int>(16384);

        public void Clear() { _v.Clear(); _c.Clear(); _uv.Clear(); _i.Clear(); }

        /// <summary>One grid of <paramref name="nu"/> x <paramref name="nv"/> vertices (row-major: index = iu * nv + iv). A vertex with alpha &lt;= 0 on all four corners drops its quad.</summary>
        public void Grid(int nu, int nv, Vector3[] pos, float[] alpha, float[] u, float[] v, float random, float[] shade = null)
        {
            int b = _v.Count;
            for (int k = 0; k < nu * nv; k++)
            {
                var p = pos[k];
                if (float.IsNaN(p.x + p.y + p.z) || float.IsInfinity(p.x + p.y + p.z)) return;
            }
            for (int iu = 0; iu < nu; iu++)
                for (int iv = 0; iv < nv; iv++)
                {
                    int k = iu * nv + iv;
                    float s = shade != null ? shade[k] : 1f;
                    _v.Add(pos[k]); _c.Add(new Color(s, s, s, alpha[k])); _uv.Add(new Vector3(u[iu], v[iv], random));
                }
            for (int iu = 0; iu < nu - 1; iu++)
                for (int iv = 0; iv < nv - 1; iv++)
                {
                    int a0 = iu * nv + iv, a1 = a0 + 1, b0 = a0 + nv, b1 = b0 + 1;
                    if (alpha[a0] <= 0f && alpha[a1] <= 0f && alpha[b0] <= 0f && alpha[b1] <= 0f) continue;
                    _i.Add(b + a0); _i.Add(b + b0); _i.Add(b + a1); _i.Add(b + a1); _i.Add(b + b0); _i.Add(b + b1);
                }
        }

        public void Write(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = _v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(_v); mesh.SetColors(_c); mesh.SetUVs(0, _uv); mesh.SetTriangles(_i, 0);
            mesh.RecalculateBounds();
        }
    }

    /// <summary>우 (water, ㅜ ring, 한 번 · 적신다) - 2026-10-28 rebuilt to concept u_a (the user, 10-28: "시안에 비하면 너무 부족한 거 아니야? "우" 술식 하나만 시안대로 뽑아봐";
    /// batch 2 had followed u_b, a crown of geysers): a ring of breaking waves stands up out of the band round the caster - teal sheets with white foam
    /// crests curling inward, black ink ribbons running through the wave faces, spray and beads of water and ink thrown high - then the walls collapse inward
    /// as a foam wash over the wet ground inside the ring. The crests stand over the executor's band (RingRadius 2 m, the one instant strike); the drawn water
    /// is bigger than the band (outer foot to R + 1.2, inward lip / wash to R - 0.6 .. R - 1.8) but never moves where the contact landed. The near arc (toward
    /// the shoulder camera) stays low and fades within 2.5 m of the lens. The shared ink layer's ring enso is off for this look (it would draw a second ring
    /// under the wall); its swing, splash and contact ink stay.
    /// r4 (the user, 10-28: "시안대로면 물이 바닥에서 솟을 뿐만 아니라 강하게 회오리치는 걸로 보여"): the ring is a whirlpool - the wall comes up as a sweep running
    /// round the ring, its crests are breakers chasing each other with the flow (steep leading side, lip toppling forward), the water's texture and the ink
    /// ribbons travel round fast and ease to a churn, spray is flung off tangentially, foam streaks spiral in over the wet ground and the wash collapses
    /// inward with a twist. The flow direction is the seed's.</summary>
    public sealed class Vol2FsU : Vol2FsRing
    {
        static readonly Color Foam = new Color(0.93f, 0.95f, 0.94f);
        protected override bool OwnFrameGesture => true;

        const int NU = 132, NV = 16, NF = 14, Lobes = 5, Arms = 7;
        const float RiseSeconds = 0.32f, CurlFrom = 0.16f, CurlTo = 0.55f, WashFrom = 1.05f, WashTo = 1.7f, FadeFrom = 1.6f, FadeTo = 2.4f;
        const float SweepSeconds = 0.22f, SpinFast = 5.5f, SpinSlow = 1.6f, SpinTau = 0.9f;
        protected override float LookSeconds => ExpandSeconds + 2.6f;

        ParticleSystem _spray, _foam, _sheets, _inkDrops, _mist, _drip; Light _light;
        Mesh _waterMesh, _foamMesh, _inkMesh, _wetMesh, _glintMesh, _swirlMesh;
        readonly Vol2FsSheet _water = new Vol2FsSheet(), _foamSheet = new Vol2FsSheet();
        readonly Vol2FsStrips _ink = new Vol2FsStrips(), _wet = new Vol2FsStrips(), _glint = new Vol2FsStrips(), _swirl = new Vol2FsStrips();
        struct Rib { public float Th, Span, VA, VB, W, Rnd, At; public int Row; }
        readonly List<Rib> _ribs = new List<Rib>();
        readonly Vector3[] _pos = new Vector3[NU * NV], _fpos = new Vector3[NU * NF];
        readonly float[] _al = new float[NU * NV], _fal = new float[NU * NF], _u = new float[NU], _v = new float[NV], _fv = new float[NF], _sh = new float[NU * NV],
                                _fu = new float[NU], _fvs = new float[NF];
        float _lastA = -1f, _sheetClock, _spin = 1f;

        static float Frac(float x) => x - Mathf.Floor(x);
        float Since(float a) => Mathf.Max(0f, a - ExpandSeconds * 0.5f);
        /// <summary>How far the flow has turned (radians, signed) by look time a: fast as the wall comes up, easing to a steady churn.</summary>
        float Phase(float a) { float x = Since(a); return _spin * (SpinSlow * x + (SpinFast - SpinSlow) * SpinTau * (1f - Mathf.Exp(-x / SpinTau))); }
        /// <summary>The flow's angular speed (rad/s, signed) at look time a.</summary>
        float Omega(float a) => _spin * (SpinSlow + (SpinFast - SpinSlow) * Mathf.Exp(-Since(a) / SpinTau));

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsB1.M(tex, tint, intensity, addMix);

        protected override void OnBegin()
        {
            var root = Root.transform;
            // r2: the body is deep translucent teal (u_a), white only at the crest (r1: the water ramp sheet read as a white wall)
            // r4: the texture travels round with the flow through the grid's u (OnRing), so the material scrolls only upward
            var body = M("fs_water", new Color(0.30f, 0.40f, 0.42f), 1.1f, 0.12f); body.Opacity = 0.8f; body.Soft = 0.12f; body.NoiseAmt = 0.3f; body.NoiseScale = 1.3f; body.Erode = 0.15f;
            body.Edge = new Color(0.8f, 0.9f, 0.9f) * 0.45f; body.EdgeWidth = 0.1f; body.Scroll = new Vector2(0f, 0.15f);
            Vol2Fs.MeshObject(root, "WaveWall", Kit.Mat(body), out _waterMesh);
            var foam = M("fs_splash", Foam, 1.25f, 0.15f); foam.Opacity = 0.9f; foam.Erode = 0.3f; foam.NoiseAmt = 0.65f; foam.NoiseScale = 1.9f; foam.Soft = 0.06f; foam.Scroll = new Vector2(0f, 0.25f);
            var fr = Vol2Fs.MeshObject(root, "FoamCap", Kit.Mat(foam), out _foamMesh); fr.sortingOrder = 1;
            var swirl = M("fs_splash", Foam, 1.1f, 0.1f); swirl.Opacity = 0.75f; swirl.Erode = 0.35f; swirl.NoiseAmt = 0.55f; swirl.NoiseScale = 1.6f; swirl.Soft = 0.05f; swirl.Scroll = new Vector2(-1.4f, 0f);
            var sr = Vol2Fs.MeshObject(root, "SwirlFoam", Kit.Mat(swirl), out _swirlMesh); sr.sortingOrder = 1;
            Vol2Fs.MeshObject(root, "WaveInk", Kit.InkSolid("fs_sumi_stroke", Vol2FsInkStage.InkC, new Color(0.22f, 0.24f, 0.25f), 0.30f, 0.42f, 0.30f, 3.5f), out _inkMesh);
            var wet = M("fs_wet", new Color(0.05f, 0.04f, 0.035f, 1f), 1f, 0f); wet.Soft = 0.02f; wet.Opacity = 0.55f; wet.NoiseAmt = 0.25f; wet.Scroll = Vector2.zero;
            Vol2Fs.MeshObject(root, "WetGround", Kit.Mat(wet), out _wetMesh);
            var glint = M("fs_water", new Color(0.85f, 0.88f, 0.86f), 0.55f, 1f); glint.Opacity = 0.35f; glint.NoiseAmt = 0.5f; glint.Erode = 0.5f; glint.Scroll = new Vector2(0.05f, 0.02f);
            var gr = Vol2Fs.MeshObject(root, "WetGlint", Kit.Mat(glint), out _glintMesh); gr.sortingOrder = 1;
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.92f, 0.97f, 1f), 2.0f, 0.5f)), Life0 = 0.5f, Life1 = 1.15f, Speed0 = 0f, Speed1 = 0.1f, Size0 = 0.015f, Size1 = 0.045f,
                Gravity = 1.6f, Drag = 0.2f, Stretch = true, StretchVel = 0.02f, StretchLen = 1.0f, Max = 2400 }, root, "Spray");
            // r5: smaller, thinner puffs (r4 - r5 read as cotton balls over the wall)
            var puff = M("fs_puff", Foam, 1.3f, 0.2f); puff.Opacity = 0.5f; puff.Erode = 0.55f; puff.NoiseAmt = 0.7f;
            _foam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(puff), Life0 = 0.4f, Life1 = 0.85f, Speed0 = 0f, Speed1 = 0.1f, Size0 = 0.12f, Size1 = 0.3f, Drag = 1.2f, Gravity = 0.4f, Tiles = 2,
                SizeStart = 0.6f, SizePeak = 1.1f, SizeEnd = 1.4f, Max = 900 }, root, "Foam");
            var sheet = M("fs_splash", Foam, 1.2f, 0.2f); sheet.Opacity = 0.75f; sheet.Erode = 0.35f; sheet.NoiseAmt = 0.4f;
            _sheets = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(sheet), Life0 = 0.35f, Life1 = 0.6f, Speed0 = 0f, Speed1 = 0.1f, Size0 = 0.8f, Size1 = 1.5f, Gravity = 1.2f, Tiles = 2,
                SizeStart = 0.4f, SizePeak = 1.1f, SizePeakAt = 0.35f, SizeEnd = 1.25f, Spin = 40f, Max = 90 }, root, "SplashSheets");
            _inkDrops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.InkSolid("fs_sumi_drops", Vol2FsInkStage.InkC, Color.black, 0.05f, 0.1f, 0.35f, 1f), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 0f, Speed1 = 0.1f,
                Size0 = 0.025f, Size1 = 0.06f, Gravity = 1.4f, Tiles = 4, Stretch = true, StretchVel = 0.04f, StretchLen = 1.0f, Max = 400, NoFlip = true }, root, "InkBeads");
            var mist = M("fs_puff", new Color(0.82f, 0.88f, 0.9f, 1f), 1.1f, 0.2f); mist.Opacity = 0.3f; mist.Erode = 0.4f;
            _mist = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mist), Life0 = 1.0f, Life1 = 1.8f, Speed0 = 0f, Speed1 = 0.1f, Size0 = 1.0f, Size1 = 1.8f, Drag = 1.5f, Tiles = 2, Max = 120, SortFudge = 8 }, root, "Mist");
            var drip = Vol2FsB1.Water(Kit, 1.6f, 0.3f, "fs_splash");
            _drip = Vol2Fs.Build(new Vol2FsPs { Mat = drip, Rate = 0f, Loop = true, Life0 = 0.4f, Life1 = 0.8f, Speed0 = 0f, Speed1 = 0.4f, Size0 = 0.1f, Size1 = 0.22f, Radius = 0.5f, Gravity = 2f, Tiles = 2,
                Stretch = true, StretchVel = 0.08f, Max = 300 }, root, "Drip");
            foreach (var ps in new[] { _spray, _foam, _sheets, _inkDrops, _mist, _drip }) ps.Play(false);
            _light = Vol2Fs.PointLight(root, "WaterLight", new Color(0.7f, 0.88f, 0.95f), 7f);
            for (int iu = 0; iu < NU; iu++) _u[iu] = iu / (NU - 1f) * 10f;
            for (int iv = 0; iv < NV; iv++) _v[iv] = iv / (NV - 1f);
            for (int iv = 0; iv < NF; iv++) { _fv[iv] = iv / (NF - 1f); _fvs[iv] = 2.5f * _fv[iv]; }
        }

        protected override void OnLaunch(long nowUs)
        {
            _spin = Vol2Fs.Hash(Seed, 911) < 0.5f ? 1f : -1f;
            // the ink ribbons on the wave faces, sinuous over the upper face, hooking up into the crest (r2: on the side the shoulder camera sees - r1's low
            // strokes on the outer face sat behind the sheet). r4: every ribbon runs with the flow (its head leads), longer, and travels round with the water
            for (int k = 0; k < 24; k++)
                _ribs.Add(new Rib { Th = Vol2FsB2.Angle(k, 24, Seed + 31, 0.8f), Span = Mathf.Lerp(1.0f, 1.8f, Vol2Fs.Hash(Seed, k + 300)),
                    VA = Mathf.Lerp(0.5f, 0.65f, Vol2Fs.Hash(Seed, k + 302)), VB = Mathf.Lerp(0.8f, 0.95f, Vol2Fs.Hash(Seed, k + 303)),
                    W = Mathf.Lerp(0.3f, 0.55f, Vol2Fs.Hash(Seed, k + 304)), Rnd = Vol2Fs.Hash(Seed, k + 305), At = CurlFrom + 0.15f * Vol2Fs.Hash(Seed, k + 306),
                    Row = Vol2Fs.Hash(Seed, k + 307) < 0.5f ? 1 : 3 });
            _light.transform.position = Apex + F * R + Vector3.up * 1.4f;
            for (int k = 0; k < 24; k++) { float th = Random.Range(-Mathf.PI, Mathf.PI); if (Back(th) < 0.6f) Vol2FsB1.Emit(_mist, OnBand(th, R) + Vector3.up * 0.6f, 1); }
        }

        protected override void OnStruck(Struck s) { }

        // ------------------------------------------------------------------------------------------------ the wave
        /// <summary>The crest height at ring angle th and look time a (0 before the rise).</summary>
        float Height(float th, float a)
        {
            float k = Mathf.Abs(th) / Mathf.PI;
            float hmax = Mathf.Lerp(2.3f, 1.3f, Vol2Fs.Smooth(0.15f, 0.6f, k)) * (1f - 0.78f * Back(th));
            float sd = Seed * 0.37f, ph = th - Phase(a);
            // r4: the crests are breakers chasing round the ring - each lobe swells slowly behind and stands steep at its leading side, and the pattern
            // travels with the flow
            float s = Frac(_spin * ph * Lobes / (2f * Mathf.PI) + 0.31f * sd);
            float lobe = s < 0.78f ? Vol2Fs.Smooth(0f, 0.78f, s) : 1f - Vol2Fs.Smooth(0.78f, 1f, s);
            float bump = 0.5f + 0.42f * lobe + 0.1f * Mathf.Sin(7f * ph + 2f * sd) + 0.06f * Mathf.Sin(13f * ph + 5f * a);
            // the wall comes up as a sweep round the ring, starting in front and running with the flow
            float lag = Frac(_spin * th / (2f * Mathf.PI)) * SweepSeconds;
            float rise = Vol2Fs.EaseOutBack(Mathf.Clamp01((Since(a) - lag) / RiseSeconds));
            return Mathf.Max(0f, hmax * bump * rise);
        }

        // the breaking-wave profile (dr outward from the band centre in units of the horizontal scale, y in units of the height): outer foot -> face -> crest -> inward lip
        static readonly Vector2[] Prof = { new Vector2(1.10f, 0f), new Vector2(0.75f, 0.18f), new Vector2(0.42f, 0.48f), new Vector2(0.18f, 0.80f), new Vector2(0.02f, 0.97f),
                                           new Vector2(-0.10f, 1f), new Vector2(-0.32f, 0.93f), new Vector2(-0.48f, 0.76f), new Vector2(-0.52f, 0.55f) };

        static Vector2 ProfAt(float v)
        {
            float f = Mathf.Clamp01(v) * (Prof.Length - 1); int i = Mathf.Min(Prof.Length - 2, Mathf.FloorToInt(f)); float t = f - i;
            Vector2 p0 = Prof[Mathf.Max(0, i - 1)], p1 = Prof[i], p2 = Prof[i + 1], p3 = Prof[Mathf.Min(Prof.Length - 1, i + 2)];
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>A point of the wave surface at ring angle th, profile v (0 outer foot .. 1 lip tip).</summary>
        Vector3 Surface(float th, float v, float a, out float h)
        {
            h = Height(th, a);
            float curl = Vol2Fs.Smooth(CurlFrom, CurlTo, a), wash = Vol2Fs.Smooth(WashFrom, WashTo, a);
            float ks = 0.45f + 0.35f * Mathf.Max(h, 0.4f);
            Vector2 p = ProfAt(v);
            // before the curl the upper face stands as a rising sheet (the lip not yet thrown)
            float lipK = Vol2Fs.Smooth(0.5f, 0.75f, v);
            Vector2 up = new Vector2(-0.02f, 0.97f + (v - 0.62f) * 0.55f);
            p = Vector2.Lerp(p, Vector2.Lerp(p, up, lipK), 1f - curl);
            float dr = p.x * ks, y = p.y * h;
            // the collapse: the wall falls, the lip runs inward along the ground as a wash, the outer foot spreads out
            y *= 1f - 0.9f * wash;
            dr -= wash * 1.3f * Vol2Fs.Smooth(0.35f, 1f, v);
            dr += wash * 0.35f * (1f - Vol2Fs.Smooth(0f, 0.3f, v));
            // r4: the upper face leans forward with the flow (taller crests topple further), and the wash twists as it runs in
            float lean = _spin * ((0.12f + 0.2f * curl) * Mathf.Clamp01(h / 1.6f) * Vol2Fs.Smooth(0.4f, 1f, v) + 0.55f * wash * Vol2Fs.Smooth(0.3f, 1f, v));
            Vector3 q = Apex + Dir(th + lean) * Mathf.Max(0.15f, R + dr); q.y = Ground + Mathf.Max(0.02f, y);
            return q;
        }

        Vector3 FaceNormal(float th, float v, float a)
        {
            Vector3 p = Surface(th, v, a, out _), pv = Surface(th, Mathf.Min(1f, v + 0.04f), a, out _), pt = Surface(th + 0.03f, v, a, out _);
            Vector3 n = Vector3.Cross(pt - p, pv - p).normalized;
            Vector3 toCam = CamPos - p; if (Vector3.Dot(n, toCam) < 0f) n = -n;
            return n;
        }

        float CamFade(Vector3 p) => Vol2Fs.Smooth(1.5f, 2.6f, Vector3.Distance(p, CamPos));

        protected override void OnRing(float t, float a, bool ended)
        {
            var cam = CamPos;
            float dt = _lastA < 0f ? 0f : Mathf.Clamp(a - _lastA, 0f, 0.1f); _lastA = a;
            float life = 1f - Vol2Fs.Smooth(FadeFrom, FadeTo, a);
            float wash = Vol2Fs.Smooth(WashFrom, WashTo, a);
            // the wall and its foam cap
            _water.Clear(); _foamSheet.Clear();
            if (life > 0.01f && a > ExpandSeconds * 0.5f)
            {
                float uShift = Phase(a) / (2f * Mathf.PI) * 10f;      // r4: the water's texture travels round with the flow
                for (int iu = 0; iu < NU; iu++) { _u[iu] = iu / (NU - 1f) * 10f - uShift; _fu[iu] = 0.35f * _u[iu]; }
                for (int iu = 0; iu < NU; iu++)
                {
                    float th = (iu / (NU - 1f) * 2f - 1f) * Mathf.PI;
                    for (int iv = 0; iv < NV; iv++)
                    {
                        float v = _v[iv];
                        var p = Surface(th, v, a, out float h);
                        int k = iu * NV + iv;
                        _pos[k] = p;
                        float foot = Vol2Fs.Smooth(0f, 0.12f, v), tip = 1f - 0.25f * Vol2Fs.Smooth(0.85f, 1f, v);
                        _al[k] = h < 0.03f ? 0f : life * foot * tip * CamFade(p) * (0.75f + 0.25f * (1f - Back(th)));
                        _sh[k] = Mathf.Lerp(0.35f, 1.05f, Vol2Fs.Smooth(0.1f, 0.75f, v));      // r2: deep at the foot, glassy toward the crest
                    }
                    for (int iv = 0; iv < NF; iv++)
                    {
                        float v = Mathf.Lerp(0.25f, 0.97f, _fv[iv]);      // r3: white lace over the whole face, densest at the crest (r2: a flat green sheet)
                        var p = Surface(th, v, a, out float h) + (cam - _pos[iu * NV + NV / 2]).normalized * 0.04f;
                        int k = iu * NF + iv;
                        _fpos[k] = p;
                        float crest = Mathf.Clamp01(1f - Mathf.Abs(v - 0.74f) / 0.2f);
                        _fal[k] = h < 0.05f ? 0f : life * Mathf.Clamp01(0.42f + 0.58f * crest) * (1f - 0.5f * wash) * CamFade(p) * Vol2Fs.Smooth(0f, 0.12f, _fv[iv] + 0.05f);
                    }
                }
                _water.Grid(NU, NV, _pos, _al, _u, _v, 0.29f, _sh);
                _foamSheet.Grid(NU, NF, _fpos, _fal, _fu, _fvs, 0.61f);      // r4: the lace stretched along the flow (streaks, not blobs)
            }
            _water.Write(_waterMesh); _foamSheet.Write(_foamMesh);

            // the ink ribbons on the faces (drawn in along the wave as it curls, dried away with the wash)
            _ink.Clear();
            foreach (var r in _ribs)
            {
                float g = Mathf.Clamp01((a - r.At) / 0.35f); if (g <= 0.01f) continue;
                float dry = Vol2Fs.Smooth(WashFrom - 0.1f, WashFrom + 0.5f, a);
                const int n = 18; var pts = new Vector3[n]; var w = new float[n]; var al = new float[n]; var sd = new Vector3[n];
                for (int j = 0; j < n; j++)
                {
                    float u = j / (n - 1f) * g;
                    float th = r.Th + 0.85f * Phase(a) + _spin * r.Span * (u - g);
                    float v = Mathf.Lerp(r.VA, r.VB, Mathf.Pow(u, 1.6f)) + 0.05f * Mathf.Sin(u * Mathf.PI * 2.2f + r.Rnd * 6f);
                    var p = Surface(th, v, a, out _);
                    var nrm = FaceNormal(th, v, a);
                    pts[j] = p + nrm * 0.07f;
                    var pv = Surface(th, Mathf.Min(1f, v + 0.05f), a, out _);
                    sd[j] = (pv - p).normalized;
                    // r6: where the ring runs away from the lens (the side walls) a ribbon is foreshortened to its width - a black slab; it thins and fades there
                    Vector3 run = Vector3.Cross(Vector3.up, Dir(th)), look = (p - cam).normalized;
                    float across = Vol2Fs.Smooth(0.25f, 0.65f, Vector3.Cross(run, look).magnitude);
                    w[j] = r.W * (0.25f + 0.75f * Mathf.Sin(Mathf.Clamp01(u / Mathf.Max(0.01f, g)) * Mathf.PI)) * (1f - 0.6f * dry) * Mathf.Lerp(0.3f, 1f, across);
                    // r5: a face seen edge-on turns the ribbon into a black slab - it fades there
                    float face = Vol2Fs.Smooth(0.15f, 0.45f, Mathf.Abs(Vector3.Dot(nrm, (cam - p).normalized))) * Mathf.Lerp(0.35f, 1f, across);
                    al[j] = (1f - dry) * CamFade(p) * (1f - 0.7f * Back(th)) * face;
                }
                float v0 = (3 - r.Row) * 0.25f + 0.012f, v1 = (4 - r.Row) * 0.25f - 0.012f;
                _ink.Strip(pts, w, sd, Color.white, al, r.Rnd, cam, 0f, g, v0, v1);
            }
            _ink.Write(_inkMesh);

            // spray, foam and beads off the crests; splash sheets at the tallest crests; the wash's foam along the inner edge
            if (dt > 0f && life > 0.05f && a > ExpandSeconds * 0.5f)
            {
                float rate = dt * 60f;
                int samples = Mathf.RoundToInt(28f * rate * (1f - 0.6f * wash));
                float best = 0f; Vector3 bestP = Vector3.zero, bestOut = Vector3.zero;
                for (int s = 0; s < samples; s++)
                {
                    float th = Random.Range(-Mathf.PI, Mathf.PI);
                    var c = Surface(th, 0.7f, a, out float h);
                    if (h < 0.25f || CamFade(c) < 0.5f) continue;
                    Vector3 outw = Dir(th), tang = Vector3.Cross(Vector3.up, outw);
                    float hk = Mathf.Clamp01(h / 2f), flow = Omega(a) * R;      // r4: everything off the crest is flung along with the flow
                    for (int q = 0; q < 2; q++) Vol2FsB1.EmitV(_spray, c + Random.insideUnitSphere * 0.15f, Vector3.up * Random.Range(1.5f, 4.5f) * (0.5f + hk) + outw * Random.Range(-1.0f, 1.8f) + tang * (flow * Random.Range(0.3f, 0.65f) + Random.Range(-0.6f, 0.6f)), -1f, -1f);
                    if (Random.value < 0.45f) Vol2FsB1.EmitV(_foam, c + Random.insideUnitSphere * 0.2f, Vector3.up * Random.Range(0.4f, 1.4f) - outw * Random.Range(0.2f, 0.9f) + tang * flow * Random.Range(0.25f, 0.5f), -1f, -1f);
                    if (Random.value < 0.12f) Vol2FsB1.EmitV(_inkDrops, c, Vector3.up * Random.Range(2f, 5f) + outw * Random.Range(-1f, 1.5f) + tang * flow * Random.Range(0.1f, 0.25f), -1f, -1f);
                    if (h > best) { best = h; bestP = c; bestOut = outw; }
                }
                _sheetClock += dt;
                if (best > 0.8f && _sheetClock > 0.07f && wash < 0.5f) { _sheetClock = 0f; Vol2FsB1.EmitV(_sheets, bestP + Vector3.up * 0.2f, Vector3.up * Random.Range(1f, 2.5f) - bestOut * 0.6f, -1f, -1f); }
                if (wash > 0.02f && wash < 0.98f)
                    for (int s = 0; s < Mathf.RoundToInt(14f * rate); s++)
                    {
                        float th = Random.Range(-Mathf.PI, Mathf.PI);
                        var p = Surface(th, 1f, a, out _); p.y = Ground + 0.05f;
                        if (CamFade(p) < 0.5f) continue;
                        Vol2FsB1.EmitV(_foam, p, -Dir(th) * Random.Range(0.4f, 1.4f) + Vector3.up * 0.3f + Vector3.Cross(Vector3.up, Dir(th)) * Omega(a) * R * 0.3f, -1f, -1f);
                    }
            }

            // r4: foam streaks spiralling in over the wet ground (each arm runs with the flow and curls inward; its head is the inner end)
            _swirl.Clear();
            float sg = Vol2Fs.Smooth(0.22f, 0.5f, a) * (1f - Vol2Fs.Smooth(FadeFrom, FadeTo + 0.4f, a));
            if (sg > 0.01f)
                for (int k = 0; k < Arms; k++)
                {
                    float hk = Vol2Fs.Hash(Seed, k + 500), th0 = (k + 0.4f * hk) / Arms * 2f * Mathf.PI, inward = 0.8f + 0.5f * Vol2Fs.Hash(Seed, k + 501);
                    const int n = 24; var pts = new Vector3[n]; var w = new float[n]; var al = new float[n]; var sd = new Vector3[n];
                    for (int j = 0; j < n; j++)
                    {
                        float u = j / (n - 1f), th = th0 + 0.8f * Phase(a) + _spin * 1.5f * u, rr = Mathf.Lerp(R - 0.1f, R - inward - 0.4f * wash, u);
                        var p = Apex + Dir(th) * rr; p.y = Ground + 0.035f;
                        pts[j] = p;
                        w[j] = Mathf.Lerp(0.26f, 0.06f, u) * (0.6f + 0.4f * hk);
                        al[j] = sg * Mathf.Sin(Mathf.Clamp01(u * 1.15f) * Mathf.PI) * (1f - 0.6f * Back(th)) * CamFade(p);
                    }
                    for (int j = 0; j < n; j++) sd[j] = Vector3.Cross(Vector3.up, (pts[Mathf.Min(n - 1, j + 1)] - pts[Mathf.Max(0, j - 1)]).normalized).normalized;
                    _swirl.Strip(pts, w, sd, Color.white, al, hk, cam, 0f, 3f, 0f, 1f);
                }
            _swirl.Write(_swirlMesh);

            // the wet ground inside the ring (the water runs in with the wash) and its sheen
            _wet.Clear(); _glint.Clear();
            float wl = Vol2Fs.Smooth(0.25f, WashTo, a) * (1f - Vol2Fs.Smooth(LookSeconds + 0.5f, LookSeconds + 2.5f, a));
            if (wl > 0.01f)
            {
                float rIn = Mathf.Max(0.3f, R - 0.4f + 0.5f * wash);
                Vol2FsB2.GroundRing(_wet, Apex, F, Ground, 0.02f, rIn, 64, ang => wl * (0.85f + 0.15f * (1f - Back(ang))), 0.29f, cam, 0.02f, 0f, 5f);
                Vol2FsB2.GroundRing(_glint, Apex, F, Ground, 0.02f, rIn, 64, ang => wl * 0.8f * (1f - Back(ang)), 0.47f, cam, 0.03f, 0f, 3f);
            }
            _wet.Write(_wetMesh); _glint.Write(_glintMesh);
            _light.intensity = 4f * Vol2Fs.Smooth(0f, 0.3f, a) * (1f - wash);

            // the struck bodies: a wave breaks over them; they drip (the 4 s soak, a mirror of the rules) for as long as the look stands
            foreach (var s in Hits)
            {
                float sa = a - s.At; if (sa < 0f) continue;
                var c = CentreNow(s);
                if (!s.Started)
                {
                    s.Started = true;
                    for (int k = 0; k < 140; k++) Vol2FsB1.EmitV(_spray, c + Vector3.up * Random.Range(0.6f, 1.8f) + Random.insideUnitSphere * 0.5f, Vector3.down * Random.Range(1f, 4f) + Random.insideUnitSphere * 1.5f, -1f, Random.Range(0.4f, 0.9f));
                    for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_foam, FeetNow(s) + Random.insideUnitSphere * 0.5f, Random.insideUnitSphere * 1.2f + Vector3.up * 0.8f, -1f, -1f);
                    for (int k = 0; k < 3; k++) Vol2FsB1.EmitV(_sheets, c + Random.insideUnitSphere * 0.4f, Vector3.up * Random.Range(1f, 2f), -1f, -1f);
                    var em = _drip.emission; em.rateOverTime = 45f;
                }
                _drip.transform.position = c + Vector3.up * 0.3f;
                if (sa > 4f) { var em = _drip.emission; em.rateOverTime = 0f; }
            }
        }
    }
}
