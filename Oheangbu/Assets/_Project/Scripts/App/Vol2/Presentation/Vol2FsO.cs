using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>오 (water, ㅗ fan, 한 번 · 적신다) - free showcase batch 1, concept o_a (a tidal fan: a curling wave of clear blue-green water with black ink swirling
    /// in its face rises from the brush and rolls out across the fan, its white foaming crest breaks over every body inside, spray and mist hang in the air,
    /// the ground under the fan is left dark and glistening; the soaked bodies drip - water runs down). The wet is shown for its 4 s (the status sink owns it).</summary>
    public sealed class Vol2FsO : Vol2FsSweep
    {
        const float RollSeconds = 0.55f, WetSeconds = 4f;
        const int Across = 40, Prof = 16;
        static readonly Color Aqua = new Color(0.6f, 0.9f, 1f), Foam = new Color(0.92f, 0.98f, 1f), Ink = new Color(0.02f, 0.03f, 0.04f);

        Mesh _waveMesh, _inkMesh, _wetMesh; readonly Vol2FsStrips _ink = new Vol2FsStrips(), _wet = new Vol2FsStrips();
        ParticleSystem _inflow, _drops, _crest, _mist, _foam, _splash; Light _light;
        readonly List<Vector3> _v = new List<Vector3>(); readonly List<Color> _c = new List<Color>(); readonly List<Vector3> _uv = new List<Vector3>(); readonly List<int> _i = new List<int>();
        struct Swirl { public float Ang, Span, S, W, Phase; }
        readonly List<Swirl> _swirls = new List<Swirl>();
        sealed class Soak { public ParticleSystem Drip; }
        bool _crashed;

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsB1.M(tex, tint, intensity, addMix);
        protected override float LookSeconds => WetSeconds + 0.6f;
        protected override float StruckAt(float radial01, float angle01) => RollSeconds * 0.55f * Mathf.Clamp01((radial01 * Depth - 1.7f) / Mathf.Max(0.5f, Depth - 1.7f)) + 0.1f;

        protected override void OnBegin()
        {
            var root = Root.transform;
            var drop = M("fs_dot", Aqua, 2.2f, 0.6f); drop.NoiseAmt = 0.1f;
            _inflow = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Rate = 140f, Loop = true, Life0 = 0.2f, Life1 = 0.3f, Speed0 = -2.5f, Speed1 = -4f, Size0 = 0.04f, Size1 = 0.09f, Radius = 1.2f, Orbital = 6f, World = false, Max = 90 }, root, "Inflow");
            _inflow.transform.position = Tip();
            _inflow.Play(false);
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.6f, Life1 = 1.3f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.04f, Size1 = 0.12f, Radius = 0.3f, Gravity = 2.2f, Drag = 0.3f, Stretch = true, StretchVel = 0.03f, Max = 1500 }, root, "Drops");
            var spl = M("fs_splash", Foam, 0.9f, 0.55f); spl.Opacity = 0.4f;      // polish: lighter spray (r6 / r2: white flecks crusted the whole face - icy) spl.NoiseAmt = 0.3f; spl.Erode = 0.25f; spl.Soft = 0.1f;
            _crest = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(spl), Life0 = 0.3f, Life1 = 0.55f, Speed0 = 1f, Speed1 = 3f, Size0 = 0.3f, Size1 = 0.65f, Radius = 0.2f, Gravity = 1.2f, Tiles = 2, SizeStart = 0.5f, SizePeak = 1.2f, SizePeakAt = 0.4f, SizeEnd = 1.2f, Spin = 80f, Max = 500 }, root, "Crest");
            _splash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(spl), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.8f, Size1 = 1.8f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.4f, Gravity = 1.6f, Tiles = 2,
                SizeStart = 0.4f, SizePeak = 1.2f, SizePeakAt = 0.4f, SizeEnd = 1.3f, Spin = 60f, Max = 200 }, root, "Splash");
            var mist = M("fs_puff", new Color(0.85f, 0.93f, 0.97f, 1f), 1f, 0.15f); mist.Opacity = 0.45f; mist.NoiseAmt = 0.6f; mist.Erode = 0.4f;
            _mist = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mist), Life0 = 1f, Life1 = 2f, Speed0 = 0.3f, Speed1 = 1.2f, Size0 = 0.9f, Size1 = 1.9f, Radius = 0.4f, Drag = 1.2f, Tiles = 2, SizeStart = 0.5f, SizePeak = 1.2f, SizeEnd = 1.8f, Max = 200, SortFudge = 10 }, root, "Mist");
            var foam = M("fs_splash", Foam, 0.9f, 0.4f); foam.Opacity = 0.6f; foam.NoiseAmt = 0.5f; foam.Erode = 0.35f; foam.Soft = 0.02f;
            _foam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(foam), Life0 = 1.4f, Life1 = 2.6f, Speed0 = 0.3f, Speed1 = 1.5f, Size0 = 0.7f, Size1 = 1.4f, Radius = 0.3f, Drag = 1.5f, Horizontal = true, Tiles = 2, SizeStart = 0.6f, SizePeak = 1.1f, SizeEnd = 1.4f, AlphaHoldTo = 0.5f, Max = 300 }, root, "Foam");
            foreach (var ps in new[] { _drops, _crest, _splash, _mist, _foam }) ps.Play(false);
        }

        protected override void OnCharge(float t, float k) { _inflow.transform.position = Tip(); }

        protected override void OnLaunch(long nowUs)
        {
            var root = Root.transform;
            _inflow.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            Vol2Fs.MeshObject(root, "Wave", Vol2FsB1.Water(Kit, 1.4f, 0.35f), out _waveMesh);
            Vol2Fs.MeshObject(root, "WaveInk", Kit.InkSolid("fs_stroke2", Ink, new Color(0.35f, 0.5f, 0.55f, 1f), 0.08f, 0.5f, 0.3f, 1.6f), out _inkMesh);
            var wet = M("fs_stroke2", new Color(0.05f, 0.04f, 0.04f, 1f), 1f, 0f); wet.Soft = 0.001f; wet.NoiseAmt = 0.45f; wet.Erode = 0.3f; wet.Opacity = 0.7f; wet.Edge = new Color(0.5f, 0.6f, 0.65f, 1f); wet.EdgeWidth = 0.05f;
            Vol2Fs.MeshObject(root, "WetFan", Kit.Mat(wet), out _wetMesh);
            for (int k = 0; k < 9; k++)
                _swirls.Add(new Swirl { Ang = Mathf.Lerp(-Half * 1.4f, Half * 1.4f, (k + 0.5f) / 9f) + 0.1f * (Vol2Fs.Hash(Seed, k) - 0.5f), Span = Mathf.Lerp(0.25f, 0.5f, Vol2Fs.Hash(Seed, k + 10)), S = Mathf.Lerp(0.35f, 0.72f, Vol2Fs.Hash(Seed, k + 20)),
                    W = Mathf.Lerp(0.2f, 0.38f, Vol2Fs.Hash(Seed, k + 30)), Phase = Vol2Fs.Hash(Seed, k + 40) * 6f });
            _light = Vol2Fs.PointLight(root, "Light", new Color(0.6f, 0.85f, 1f), 7f);
            for (int k = 0; k < 40; k++) { var d = (F + Vector3.up * 0.4f + Right * (Vol2Fs.Hash(Seed, k + 3000) - 0.5f) * 1.4f).normalized; Vol2FsB1.EmitV(_drops, Tip(), d * Mathf.Lerp(4f, 9f, Vol2Fs.Hash(Seed, k + 3010))); }
        }

        protected override void OnStruck(Struck s) { }

        // the wave's state at a seconds after the launch
        float R(float a) => Mathf.Lerp(2.6f, Depth * 1.05f, Vol2Fs.Smooth(0f, RollSeconds, a));      // r2: forms out in front (r1 stood up in the camera); polish: further out still (r6 hung over the caster like an umbrella)
        float Crash(float a) => Vol2Fs.Smooth(RollSeconds * 0.85f, RollSeconds + 0.35f, a);
        float H(float a) => 3.0f * Vol2Fs.Smooth(0f, 0.22f, a) * (1f - 0.88f * Crash(a));
        float Curl(float a) => Vol2Fs.Smooth(0.12f, RollSeconds * 0.9f, a);

        /// <summary>A point of the wave sheet: across 0..1 (angle), s 0..1 (back foot -> crest -> curling lip).</summary>
        Vector3 Sheet(float across, float s, float a, out float bright)
        {
            float ang = Mathf.Lerp(-Half * 1.55f, Half * 1.55f, across);      // polish: a long wall across the front, not a dome round the caster
            float endFall = 1f - Mathf.Pow(Mathf.Abs(across * 2f - 1f), 3f);
            float h = H(a) * endFall * (0.85f + 0.15f * Mathf.Sin(across * 9f + a * 6f)) * (1.2f - 0.4f * across) /* o_a: the left end rears highest */, c = Curl(a), cr = Crash(a), r = R(a) - 0.25f * (1f - endFall);
            // profile control (radial offset, height) - back foot, face, crest, lip curling over and down; the crash throws the lip forward and down
            var ctrl = new[]
            {
                new Vector2(-1.0f, 0f), new Vector2(-0.6f, 0.3f * h), new Vector2(-0.4f, 0.78f * h), new Vector2(0f, h),
                new Vector2((0.35f * c + 0.6f * cr) * Mathf.Max(0.6f, h), h * (1f - 0.08f * c)), new Vector2((0.6f * c + 1.0f * cr) * Mathf.Max(0.6f, h), h * (1f - 0.42f * c - 0.4f * cr)), new Vector2((0.55f * c + 1.2f * cr) * Mathf.Max(0.6f, h), h * Mathf.Max(0f, 1f - 0.82f * c - 0.2f * cr))
            };
            float f = s * (ctrl.Length - 1); int i = Mathf.Min(ctrl.Length - 2, Mathf.FloorToInt(f)); float u = f - i;
            Vector2 p0 = ctrl[Mathf.Max(0, i - 1)], p1 = ctrl[i], p2 = ctrl[i + 1], p3 = ctrl[Mathf.Min(ctrl.Length - 1, i + 2)];
            Vector2 q = 0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u + (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u);
            bright = Mathf.Lerp(0.5f, 1.35f, Vol2Fs.Smooth(0.5f, 0.8f, s)) * (1f + 0.35f * cr);      // polish: a deeper body, white only on the lip
            Vector3 d = Dir(ang);
            Vector3 p = Apex + d * ((r + q.x) / Mathf.Pow(Mathf.Max(0.55f, Mathf.Cos(ang)), 0.7f)); p.y = Ground + Mathf.Max(0f, q.y) + 0.02f;      // polish: the arc flattened toward a straight wall
            return p;
        }

        protected override void OnSweep(float t, float a, bool ended)
        {
            var cam = CamPos;
            float life = 1f - Vol2Fs.Smooth(RollSeconds + 0.15f, RollSeconds + 0.75f, a);
            _v.Clear(); _c.Clear(); _uv.Clear(); _i.Clear();
            if (life > 0.01f)
            {
                for (int x = 0; x <= Across; x++)
                    for (int y = 0; y <= Prof; y++)
                    {
                        float ax = x / (float)Across, s = y / (float)Prof;
                        var p = Sheet(ax, s, a, out float b);
                        float edgeA = Mathf.Clamp01(Mathf.Min(ax, 1f - ax) * 8f);
                        _v.Add(p); _c.Add(new Color(b, b, b, life * edgeA * Mathf.Lerp(0.75f, 1f, s))); _uv.Add(new Vector3(ax * 4f + a * 0.8f, Mathf.Lerp(0.3f, 1f, s), 0.29f));      // polish: the sheet's broken (icicle-like) edge only on the lip, not the foot
                    }
                for (int x = 0; x < Across; x++)
                    for (int y = 0; y < Prof; y++)
                    {
                        int i0 = x * (Prof + 1) + y, i1 = i0 + 1, j0 = i0 + Prof + 1, j1 = j0 + 1;
                        _i.Add(i0); _i.Add(j0); _i.Add(i1); _i.Add(i1); _i.Add(j0); _i.Add(j1);
                    }
            }
            _waveMesh.Clear(); _waveMesh.SetVertices(_v); _waveMesh.SetColors(_c); _waveMesh.SetUVs(0, _uv); _waveMesh.SetTriangles(_i, 0); _waveMesh.RecalculateBounds();
            // foam and spray off the crest while it rolls; the crash throws a wall of splash, mist and foam over the fan
            if (life > 0.05f && a > 0.08f)
                for (int k = 0; k < 2; k++)
                {
                    float ax = Random.value; var p = Sheet(ax, 0.86f + 0.14f * Random.value, a, out _);      // polish: spray off the lip only
                    Vol2FsB1.Emit(_crest, p, 1);
                    Vector3 d = Dir(Mathf.Lerp(-Half * 1.55f, Half * 1.55f, ax));
                    Vol2FsB1.EmitV(_drops, p, (d * Random.Range(1f, 3.5f) + Vector3.up * Random.Range(1f, 4f)));
                }
            if (life > 0.05f && a > 0.05f && Time.frameCount % 2 == 0) { var fp0 = Sheet(Random.value, 0.04f, a, out _); Vol2FsB1.Emit(_foam, new Vector3(fp0.x, Ground + 0.05f, fp0.z), 1); }      // polish: churned foam at the wave's foot
            if (!_crashed && a >= RollSeconds * 0.9f)
            {
                _crashed = true;
                for (int k = 0; k < 40; k++)
                {
                    float ax = (k + 0.5f) / 40f; var p = Sheet(ax, 1f, a, out _); p.y = Ground + 0.2f;
                    Vol2FsB1.Emit(_splash, p, 1); Vol2FsB1.Emit(_foam, new Vector3(p.x, Ground + 0.05f, p.z), 1);
                    if (k % 3 == 0) Vol2FsB1.Emit(_mist, p + Vector3.up * 0.6f, 1);
                    for (int j = 0; j < 6; j++) Vol2FsB1.EmitV(_drops, p, (Dir(Mathf.Lerp(-Half * 1.55f, Half * 1.55f, ax)) * Random.Range(1f, 4f) + Vector3.up * Random.Range(2f, 6f)));
                }
            }
            if (_crashed && a < RollSeconds + 0.9f && Time.frameCount % 3 == 0)
            {
                float ax = Random.value; Vector3 p = Apex + Dir(Mathf.Lerp(-Half, Half, ax)) * Random.Range(1f, Depth); p.y = Ground + 0.05f;
                Vol2FsB1.Emit(_foam, p, 1);
            }
            // ink swirls riding in the wave face
            _ink.Clear();
            if (life > 0.01f)
                foreach (var sw in _swirls)
                {
                    var pts = new Vector3[8]; var w = new float[8]; var al = new float[8];
                    float ax0 = Mathf.InverseLerp(-Half * 1.55f, Half * 1.55f, sw.Ang);
                    for (int k = 0; k < 8; k++)
                    {
                        float u = k / 7f;
                        float ax = Mathf.Clamp01(ax0 + (u - 0.5f) * sw.Span * 0.5f);
                        float s = Mathf.Clamp01(sw.S + 0.18f * Mathf.Sin(u * 4f + sw.Phase + a * 5f));
                        var p = Sheet(ax, s, a, out _);
                        pts[k] = p + (cam - p).normalized * 0.06f;
                        w[k] = sw.W * Mathf.Sin(u * Mathf.PI) * (0.4f + 0.6f * Vol2Fs.Smooth(0.05f, 0.25f, a)); al[k] = life;
                    }
                    _ink.Strip(pts, w, null, Color.white, al, sw.Phase, cam, 0f, 1f);
                }
            _ink.Write(_inkMesh);
            // the ground left wet (a dark glistening fan), drying after the wet's seconds
            _wet.Clear();
            float wl = 1f - Vol2Fs.Smooth(WetSeconds - 0.6f, WetSeconds + 0.4f, a);
            float reach = R(a);
            if (wl > 0.01f && a > 0.05f)
            {
                var fp = new List<Vector3>(); var fw = new List<float>(); var fa = new List<float>(); var fs = new List<Vector3>();
                for (int k = 0; k < 22; k++)
                {
                    float u = k / 21f; Vector3 d = Dir(Mathf.Lerp(-Half * 1.2f, Half * 1.2f, u));
                    float r0 = 0.5f, r1 = Mathf.Max(r0 + 0.2f, reach + 0.3f);
                    Vector3 p = Apex + d * (r0 + r1) * 0.5f; p.y = Ground + 0.025f;
                    fp.Add(p); fw.Add(r1 - r0); fa.Add(wl); fs.Add(d);
                }
                _wet.Strip(fp, fw, fs, Color.white, fa, 0.77f, cam, 0f, 1f);
            }
            _wet.Write(_wetMesh);
            _light.transform.position = Apex + F * R(a) + Vector3.up * 1.4f; _light.intensity = 2.5f * life;
            // the struck bodies: a splash crown when the wave reaches them, then they drip while wet
            foreach (var s in Hits)
            {
                Vector3 c = CentreNow(s);
                if (!s.Started && a >= s.At)
                {
                    s.Started = true;
                    Vol2FsB1.Emit(_splash, c + Vector3.up * 0.3f, 10); Vol2FsB1.Emit(_mist, c, 4);
                    for (int k = 0; k < 90; k++) { float an = Random.value * 6.283f; Vol2FsB1.EmitV(_drops, c, new Vector3(Mathf.Cos(an), Random.Range(0.8f, 2f), Mathf.Sin(an)).normalized * Random.Range(3f, 8f)); }
                    var drop = M("fs_dot", Aqua, 2f, 0.6f); drop.NoiseAmt = 0.1f;
                    var tr = new GameObject("Drip").transform; tr.SetParent(Root.transform, false);
                    var ps = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Rate = 60f, Loop = true, Life0 = 0.4f, Life1 = 0.7f, Speed0 = 0f, Speed1 = 0.3f, Size0 = 0.03f, Size1 = 0.07f, Radius = 1f,
                        ShapeScale = new Vector3(s.B.extents.x, s.B.extents.y * 0.6f, s.B.extents.z), Gravity = 1.6f, Stretch = true, StretchVel = 0.05f, Max = 200 }, tr, "Drip");
                    ps.Play(false);
                    s.Data = new Soak { Drip = ps };
                }
                if (s.Data is Soak sk)
                {
                    sk.Drip.transform.position = c + Vector3.up * s.B.extents.y * 0.3f;
                    float e = a - s.At;
                    var em = sk.Drip.emission; em.rateOverTime = 60f * (1f - Vol2Fs.Smooth(1f, WetSeconds, e));
                    if (e > WetSeconds && sk.Drip.isEmitting) sk.Drip.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }
    }
}
