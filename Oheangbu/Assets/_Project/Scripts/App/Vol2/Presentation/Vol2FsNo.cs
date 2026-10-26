using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>노 (fire, ㅗ fan, 한 번 · 데운다) - free showcase batch 1, concept no_a (a fire breath fan: from the brush the whole fan in front bursts into a roaring
    /// wall of flame racing outward from the caster, black ink waves curl up out of the fire along its outer edge like breaking surf with burning rims, the
    /// ground under the fan glows with heat, every body inside catches fire and burns until the cast's 2 s burn ends; the fire dies from the edges into
    /// rising ash). It erupts radially (near -> far), not as a left-right sweep (고 / 소 sweep).</summary>
    public sealed class Vol2FsNo : Vol2FsSweep
    {
        const float RaceSeconds = 0.32f, BurnSeconds = 1.2f;
        static readonly Color Orange = new Color(1f, 0.42f, 0.08f), Hot = new Color(1f, 0.78f, 0.35f), Ink = new Color(0.035f, 0.025f, 0.02f), Rim = new Color(4f, 1.3f, 0.25f, 1f);

        ParticleSystem _charge, _fire, _tall, _embers, _smoke, _ash, _drops; Light _chargeLight, _fanLight, _fanLight2;
        Mesh _groundMesh, _scorchMesh, _wavesMesh; readonly Vol2FsStrips _ground = new Vol2FsStrips(), _scorch = new Vol2FsStrips(), _waves = new Vol2FsStrips();
        sealed class Pt { public Vector3 P; public float At, Edge; public bool Fired; }
        readonly List<Pt> _pts = new List<Pt>();
        struct Wave { public float Ang, At, H, W, Curl, Life; }
        readonly List<Wave> _wave = new List<Wave>();
        sealed class Burn { public ParticleSystem Fire, Smoke, Embers; public Light L; public Transform T; }
        bool _ashed;

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsB1.M(tex, tint, intensity, addMix);
        Vol2FsMat FireMat(float intensity, float addMix) { var m = M("fs_flame", Color.white, intensity, addMix); m.Ramp = "fs_ramp_fire"; m.RampScale = 1.7f; m.NoiseAmt = 0.65f; m.Erode = 0.28f; return m; }
        protected override float LookSeconds => RaceSeconds + BurnSeconds + 0.6f;
        protected override float StruckAt(float radial01, float angle01) => RaceSeconds * radial01 * 0.9f;

        protected override void OnBegin()
        {
            var root = Root.transform;
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(FireMat(2.4f, 0.65f)), Rate = 110f, Loop = true, Life0 = 0.18f, Life1 = 0.3f, Speed0 = -2.4f, Speed1 = -3.8f, Size0 = 0.2f, Size1 = 0.4f, Radius = 0.6f, Tiles = 2, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 0.3f, World = false, Max = 200 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", Orange, 4f);
            _charge.transform.position = Tip();
            _charge.Play(false);
            _fire = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(FireMat(1.9f, 0.45f)), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 0.4f, Speed1 = 1.4f, Size0 = 0.45f, Size1 = 0.95f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.25f, Tiles = 2,
                VelOverLife = new Vector3(0f, 2.4f, 0f), Noise = 0.9f, NoiseFreq = 1.2f, SizeStart = 0.5f, SizePeak = 1.25f, SizeEnd = 0.45f, Max = 1500 }, root, "Fire");
            _tall = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(FireMat(2.1f, 0.4f)), Life0 = 0.55f, Life1 = 1.0f, Speed0 = 1f, Speed1 = 3f, Size0 = 0.9f, Size1 = 1.7f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.3f, Tiles = 2,
                VelOverLife = new Vector3(0f, 3.2f, 0f), Noise = 1.1f, NoiseFreq = 0.9f, SizeStart = 0.4f, SizePeak = 1.2f, SizePeakAt = 0.35f, SizeEnd = 0.5f, Max = 500 }, root, "TallFire");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Hot, 5f, 1f)), Life0 = 0.6f, Life1 = 1.6f, Speed0 = 1.5f, Speed1 = 5f, Size0 = 0.03f, Size1 = 0.07f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.3f, Gravity = -0.3f,
                Noise = 1.5f, NoiseFreq = 2f, Stretch = true, StretchVel = 0.05f, Max = 1200 }, root, "Embers");
            var smoke = M("fs_puff", new Color(0.08f, 0.06f, 0.05f, 1f), 1f, 0f); smoke.Opacity = 0.6f; smoke.NoiseAmt = 0.6f; smoke.Erode = 0.35f;
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(smoke), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 0.5f, Speed1 = 1.4f, Size0 = 1.0f, Size1 = 1.9f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.4f, Drag = 1f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.3f, SizePeakAt = 0.5f, SizeEnd = 1.8f, VelOverLife = new Vector3(0f, 1.2f, 0f), Max = 200, SortFudge = 12 }, root, "Smoke");
            var ash = M("fs_ink", new Color(0.5f, 0.47f, 0.44f, 1f), 1f, 0f); ash.NoiseAmt = 0.3f; ash.Edge = new Color(1.6f, 0.5f, 0.1f, 1f); ash.EdgeWidth = 0.25f; ash.Soft = 0.05f;
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ash), Life0 = 1.4f, Life1 = 2.6f, Speed0 = 0.3f, Speed1 = 1.2f, Size0 = 0.04f, Size1 = 0.12f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.3f, VelOverLife = new Vector3(0f, 1.4f, 0f),
                Noise = 1.2f, NoiseFreq = 1f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.6f, Max = 600 }, root, "Ash");
            var drop = M("fs_ink", Ink, 1f, 0f); drop.Edge = new Color(2.5f, 0.7f, 0.12f, 1f); drop.EdgeWidth = 0.2f; drop.NoiseAmt = 0.3f; drop.Soft = 0.1f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.8f, Life1 = 1.5f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.1f, Size1 = 0.35f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.3f, Gravity = 2f, Drag = 0.4f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 400 }, root, "InkDrops");
            foreach (var ps in new[] { _fire, _tall, _embers, _smoke, _ash, _drops }) ps.Play(false);
        }

        protected override void OnCharge(float t, float k)
        {
            _charge.transform.position = Tip();
            _chargeLight.intensity = 4f * k;
            _charge.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.3f, k);
        }

        protected override void OnLaunch(long nowUs)
        {
            var root = Root.transform;
            _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f;
            // flame points: polar samples over the fan, timed by radius (the breath races outward); the outer rim is a wall
            int n = 110;
            for (int k = 0; k < n; k++)
            {
                float r = Mathf.Lerp(0.9f, Depth * 1.03f, Mathf.Sqrt(Vol2Fs.Hash(Seed, k)));
                float ang = Mathf.Lerp(-Half * 1.05f, Half * 1.05f, Vol2Fs.Hash(Seed, k + 1000));
                var p = Apex + Dir(ang) * r; p.y = Ground + 0.1f;
                _pts.Add(new Pt { P = p, At = RaceSeconds * (r / Depth) * 0.95f, Edge = Vol2Fs.Smooth(0.7f, 1f, r / Depth) });
            }
            for (int k = 0; k < 11; k++)
            {
                float u = (k + 0.5f) / 11f;
                _wave.Add(new Wave { Ang = Mathf.Lerp(-Half * 1.15f, Half * 1.15f, u) + 0.05f * (Vol2Fs.Hash(Seed, k + 2000) - 0.5f), At = RaceSeconds * 0.55f + 0.05f * Vol2Fs.Hash(Seed, k + 2010), H = Mathf.Lerp(2.3f, 3.6f, Vol2Fs.Hash(Seed, k + 2020)) * (1f - 0.35f * Mathf.Abs(u * 2f - 1f)),
                    W = Mathf.Lerp(1.3f, 2.0f, Vol2Fs.Hash(Seed, k + 2030)), Curl = (Vol2Fs.Hash(Seed, k + 2040) - 0.5f) * 0.8f, Life = 1.3f + 0.5f * Vol2Fs.Hash(Seed, k + 2050) });
            }
            var glow = M("fs_scorch", Color.white, 2.2f, 0.9f); glow.Ramp = "fs_ramp_fire"; glow.RampScale = 1.2f; glow.NoiseAmt = 0.6f; glow.NoiseScale = 2f; glow.Soft = 0.001f; glow.Scroll = new Vector2(0f, -0.6f);
            Vol2Fs.MeshObject(root, "HeatFan", Kit.Mat(glow), out _groundMesh);
            var sc = M("fs_stroke2", new Color(0.03f, 0.02f, 0.015f, 1f), 1f, 0f); sc.Soft = 0.001f; sc.NoiseAmt = 0.55f; sc.Erode = 0.3f; sc.Opacity = 0.85f; sc.Edge = new Color(1.8f, 0.45f, 0.06f, 1f); sc.EdgeWidth = 0.1f;
            Vol2Fs.MeshObject(root, "ScorchFan", Kit.Mat(sc), out _scorchMesh);
            Vol2Fs.MeshObject(root, "InkWaves", Kit.InkSolid("fs_stroke2", Ink, new Color(2.2f, 0.7f, 0.12f, 1f), 0.07f, 0.4f, 0.24f, 1.6f), out _wavesMesh);
            _fanLight = Vol2Fs.PointLight(root, "FanLight", Orange, 9f); _fanLight.transform.position = Apex + F * Depth * 0.55f + Vector3.up * 1.2f;
            _fanLight2 = Vol2Fs.PointLight(root, "FanLight2", Orange, 6f); _fanLight2.transform.position = Apex + F * 1.2f + Vector3.up * 0.8f;
            // the breath leaves the brush: a cone of flame out of the tip
            for (int k = 0; k < 30; k++) { var d = (F + Vector3.up * 0.1f + Right * (Vol2Fs.Hash(Seed, k + 3000) - 0.5f) * 1.2f).normalized; Vol2FsB1.EmitV(_fire, Tip(), d * Mathf.Lerp(6f, 12f, Vol2Fs.Hash(Seed, k + 3010)), Mathf.Lerp(0.5f, 1f, Vol2Fs.Hash(Seed, k + 3020)), 0.35f); }
        }

        protected override void OnStruck(Struck s) { }

        void StartBurn(Struck s)
        {
            s.Started = true;
            var b = new Burn { T = new GameObject("Burn").transform };
            b.T.SetParent(Root.transform, false);
            float rx = Mathf.Max(0.3f, s.B.extents.x * 0.8f), ry = Mathf.Max(0.3f, s.B.extents.y * 0.7f), rz = Mathf.Max(0.3f, s.B.extents.z * 0.8f);
            b.Fire = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(FireMat(2.4f, 0.55f)), Rate = 80f, Loop = true, Life0 = 0.35f, Life1 = 0.75f, Speed0 = 0.2f, Speed1 = 0.6f, Size0 = 0.4f, Size1 = 0.9f, Radius = 1f, ShapeScale = new Vector3(rx, ry, rz), Tiles = 2,
                VelOverLife = new Vector3(0f, 2.4f, 0f), Noise = 0.8f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 0.3f, Max = 250 }, b.T, "BurnFire");
            var smoke = M("fs_puff", new Color(0.08f, 0.06f, 0.05f, 1f), 1f, 0f); smoke.Opacity = 0.55f; smoke.NoiseAmt = 0.6f; smoke.Erode = 0.35f;
            b.Smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(smoke), Rate = 8f, Loop = true, Life0 = 1.2f, Life1 = 2f, Speed0 = 0.2f, Speed1 = 0.5f, Size0 = 0.7f, Size1 = 1.3f, Radius = 1f, ShapeScale = new Vector3(rx, ry, rz), Tiles = 2,
                VelOverLife = new Vector3(0f, 1.4f, 0f), SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.8f, Max = 40, SortFudge = 10 }, b.T, "BurnSmoke");
            b.Embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Hot, 5f, 1f)), Rate = 30f, Loop = true, Life0 = 0.6f, Life1 = 1.4f, Speed0 = 0.5f, Speed1 = 1.5f, Size0 = 0.03f, Size1 = 0.06f, Radius = 1f, ShapeScale = new Vector3(rx, ry, rz),
                VelOverLife = new Vector3(0f, 1.8f, 0f), Noise = 1.4f, NoiseFreq = 2f, Stretch = true, StretchVel = 0.05f, Max = 100 }, b.T, "BurnEmbers");
            b.L = Vol2Fs.PointLight(b.T, "BurnLight", Orange, 5f);
            b.T.position = CentreNow(s);
            foreach (var ps in new[] { b.Fire, b.Smoke, b.Embers }) ps.Play(false);
            s.Data = b;
            Vol2FsB1.Emit(_drops, CentreNow(s), 30); Vol2FsB1.Emit(_embers, CentreNow(s), 60);
        }

        protected override void OnCastEnded(float t)
        {
            foreach (var s in Hits) if (s.Data is Burn b) { b.Fire.Stop(false, ParticleSystemStopBehavior.StopEmitting); b.Smoke.Stop(false, ParticleSystemStopBehavior.StopEmitting); b.Embers.Stop(false, ParticleSystemStopBehavior.StopEmitting); s.Ended = true; Vol2FsB1.Emit(_ash, CentreNow(s), 40); }
        }

        protected override void OnSweep(float t, float a, bool ended)
        {
            var cam = CamPos;
            float burnOut = Vol2Fs.Smooth(RaceSeconds + BurnSeconds * 0.6f, RaceSeconds + BurnSeconds, a);
            // flames: each point bursts when the breath reaches it and keeps burning (fewer) until the burn-out; the rim burns tallest
            for (int k = 0; k < _pts.Count; k++)
            {
                var p = _pts[k];
                if (a < p.At) continue;
                float since = a - p.At;
                if (!p.Fired) { p.Fired = true; Vol2FsB1.Emit(_fire, p.P, 2); if (p.Edge > 0.3f) Vol2FsB1.Emit(_tall, p.P, 1); Vol2FsB1.Emit(_embers, p.P, 3); if (k % 6 == 0) Vol2FsB1.Emit(_smoke, p.P + Vector3.up * 0.8f, 1); }
                else if (burnOut < 1f && (k + Time.frameCount) % (p.Edge > 0.3f ? 9 : 20) == 0)      // r2: sparser (r1 was one white-hot blob)
                {
                    Vol2FsB1.Emit(_fire, p.P, 1);
                    if (p.Edge > 0.3f && burnOut < 0.5f) Vol2FsB1.Emit(_tall, p.P, 1);
                    if (k % 3 == 0) Vol2FsB1.Emit(_embers, p.P, 1);
                }
            }
            if (burnOut > 0.4f && !_ashed)
            {
                _ashed = true;                                // the fire dies from the edges into rising ash
                for (int k = 0; k < _pts.Count; k += 2) if (_pts[k].Edge > 0.2f) { Vol2FsB1.Emit(_ash, _pts[k].P + Vector3.up * 0.3f, 4); Vol2FsB1.Emit(_smoke, _pts[k].P + Vector3.up * 0.6f, k % 8 == 0 ? 1 : 0); }
            }
            // the heat glow and scorch on the ground (the fan's shape)
            float head = Mathf.Clamp01(a / RaceSeconds);
            _ground.Clear(); _scorch.Clear();
            float gl = 1f - Vol2Fs.Smooth(RaceSeconds + 0.3f, RaceSeconds + BurnSeconds + 0.3f, a);
            float sl = 1f - Vol2Fs.Smooth(RaceSeconds + BurnSeconds, RaceSeconds + BurnSeconds + 1.2f, a);
            if (head > 0.02f)
            {
                var fp = new List<Vector3>(); var fw = new List<float>(); var fa = new List<float>(); var fs = new List<Vector3>(); var sa = new List<float>();
                for (int k = 0; k < 22; k++)
                {
                    float u = k / 21f; float ang = Mathf.Lerp(-Half * 1.05f, Half * 1.05f, u);
                    Vector3 d = Dir(ang); float r0 = 0.6f, r1 = Mathf.Lerp(r0 + 0.2f, Depth * 1.05f, head);
                    Vector3 p = Apex + d * (r0 + r1) * 0.5f; p.y = Ground + 0.04f;
                    fp.Add(p); fw.Add(r1 - r0); fa.Add(gl); fs.Add(d); sa.Add(sl);
                }
                if (gl > 0.01f) _ground.Strip(fp, fw, fs, Color.white, fa, 0.17f, cam, 0f, 1f);
                for (int k = 0; k < fp.Count; k++) fp[k] += Vector3.down * 0.01f;
                if (sl > 0.01f) _scorch.Strip(fp, fw, fs, Color.white, sa, 0.83f, cam, 0f, 1f);
            }
            _ground.Write(_groundMesh); _scorch.Write(_scorchMesh);
            // the ink waves curling out of the fire along the outer edge
            _waves.Clear();
            foreach (var w in _wave)
            {
                float s = a - w.At; if (s <= 0f) continue;
                float grow = Vol2Fs.Smooth(0f, 0.22f, s), life = 1f - Vol2Fs.Smooth(0.35f, w.Life, s);
                if (life <= 0.01f) continue;
                Vector3 d = Dir(w.Ang), side = Vector3.Cross(Vector3.up, d);
                Vector3 b = Apex + d * Depth * 0.85f; b.y = Ground;
                var pts = new Vector3[9]; var wd = new float[9]; var al = new float[9];
                for (int k = 0; k < 9; k++)
                {
                    float u = k / 8f * grow;
                    float curlA = u * u * 2.6f;                                   // the crest curls forward (outward) and over
                    Vector3 up = Vector3.up * Mathf.Sin(Mathf.Min(curlA, Mathf.PI * 0.5f) + 0.0001f) * w.H * Mathf.Min(1f, u * 1.4f);
                    float sgn = w.Ang >= 0f ? 1f : -1f;      // r2: the crests curl out to the fan's sides (an outward curl is end-on from behind the shoulder)
                    Vector3 o = (d * 0.35f + side * sgn) * (Mathf.Sin(curlA) * 0.9f * w.H * u) - d * 0.3f * u + side * w.Curl * u;
                    pts[k] = b + Vector3.up * (u * w.H) + o + up * 0.2f;
                    wd[k] = w.W * (1.1f - 0.7f * u) * (0.6f + 0.4f * grow);
                    al[k] = life * (1f - 0.35f * u);
                }
                _waves.Strip(pts, wd, null, Color.white, al, w.Curl + 0.5f, cam, 0f, 1f);
                if (s < 0.25f && Time.frameCount % 2 == 0) Vol2FsB1.Emit(_drops, pts[8], 1);
            }
            _waves.Write(_wavesMesh);
            float li = a < RaceSeconds ? 9f * a / RaceSeconds : 9f * (1f - burnOut);
            _fanLight.intensity = li * (0.85f + 0.15f * Mathf.PerlinNoise(t * 8f, 1.1f)); _fanLight2.intensity = li * 0.6f;
            // the struck bodies burn from the moment the breath reaches them until the cast's end (its 2 s burn ticks)
            foreach (var s in Hits)
            {
                if (!s.Started && a >= s.At) StartBurn(s);
                if (s.Data is Burn b)
                {
                    b.T.position = CentreNow(s);
                    b.L.intensity = s.Ended ? Mathf.Max(0f, b.L.intensity - Time.deltaTime * 6f) : 3f + 1.5f * Mathf.PerlinNoise(t * 9f, s.Id);
                }
            }
        }
    }
}
