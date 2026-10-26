using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>모 (earth, ㅗ fan, 한 번 · 민다) - free showcase batch 1, concept mo_b (+ mo_a's rolling timing): the earth of the whole fan heaves - big slabs of
    /// rock and packed earth burst up row by row from the caster's feet outward like a rolling wave, tilted outward like opening fan ribs, ramming every body
    /// inside backwards in clods, chunks and dust, black ink flung up off the crest in straight spikes; then the slabs sink and the ground round the fan lifts
    /// in plates and settles (the 끝 clause). Heavy, no glow. The shove itself is the executor's.</summary>
    public sealed class Vol2FsMo : Vol2FsSweep
    {
        const float WaveSeconds = 0.36f, HoldSeconds = 0.8f, SinkSeconds = 0.5f;
        static readonly Color Ink = new Color(0.04f, 0.03f, 0.025f), Soil = new Color(0.36f, 0.22f, 0.15f), Dust = new Color(0.5f, 0.38f, 0.29f);

        sealed class Slab { public Transform T; public Vector3 Base, Out; public float At, Tilt, Rise, Spin; public bool Burst, Last; }
        readonly List<Slab> _slabs = new List<Slab>(), _rim = new List<Slab>();
        ParticleSystem _charge, _chunks, _clods, _dust, _speed; Light _chargeLight;
        Mesh _inkMesh, _crackMesh; readonly Vol2FsStrips _ink = new Vol2FsStrips(), _crack = new Vol2FsStrips();
        struct Spike { public Vector3 Base, Dir; public float At, Len, W, Life, Rnd; }
        readonly List<Spike> _spikes = new List<Spike>();
        bool _sunk;

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsB1.M(tex, tint, intensity, addMix);
        protected override float LookSeconds => WaveSeconds + HoldSeconds + SinkSeconds + 0.9f;
        protected override float StruckAt(float radial01, float angle01) => WaveSeconds * radial01;

        protected override void OnBegin()
        {
            var root = Root.transform;
            var chunk = Kit.SharedMesh("chunk0", () => Vol2FsB1.Rock(23, 0.5f, 0.7f, 7, 5));
            var rock = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.85f, 0.78f, 0.7f), 0f, 0.12f);
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = rock, Mesh = chunk, Rate = 45f, Loop = true, Life0 = 0.25f, Life1 = 0.35f, Speed0 = -3f, Speed1 = -5f, Size0 = 0.05f, Size1 = 0.14f, Radius = 1.1f, World = false, Spin = 300f, Max = 40 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", new Color(1f, 0.8f, 0.6f), 3f);
            _charge.transform.position = Tip();
            _charge.Play(false);
            _chunks = Vol2Fs.Build(new Vol2FsPs { Mat = rock, Mesh = chunk, Life0 = 0.9f, Life1 = 1.6f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.1f, Size1 = 0.42f, Shape = ParticleSystemShapeType.Cone, Angle = 35f, Radius = 0.4f, ShapeRot = new Vector3(-90f, 0f, 0f),
                Gravity = 2.2f, Drag = 0.3f, Spin = 500f, Max = 300 }, root, "Chunks");
            var clod = M("fs_ink", Soil, 1f, 0f); clod.NoiseAmt = 0.4f; clod.Soft = 0.1f;
            _clods = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(clod), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.07f, Size1 = 0.26f, Shape = ParticleSystemShapeType.Cone, Angle = 40f, Radius = 0.5f, ShapeRot = new Vector3(-90f, 0f, 0f),
                Gravity = 2.6f, Tiles = 2, AlphaHoldTo = 0.7f, Max = 800 }, root, "Clods");
            var dust = M("fs_puff", Dust, 1f, 0f); dust.Opacity = 0.7f; dust.NoiseAmt = 0.6f; dust.Erode = 0.35f;
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dust), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 0.6f, Speed1 = 2.2f, Size0 = 1.1f, Size1 = 2.2f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.5f, Drag = 1.4f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.25f, SizePeakAt = 0.4f, SizeEnd = 1.7f, VelOverLife = new Vector3(0f, 0.5f, 0f), Max = 160, SortFudge = 10 }, root, "Dust");
            var streak = M("fs_streak", Dust, 1f, 0f); streak.Opacity = 0.9f; streak.NoiseAmt = 0.4f;
            _speed = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(streak), Life0 = 0.3f, Life1 = 0.55f, Speed0 = 9f, Speed1 = 15f, Size0 = 0.22f, Size1 = 0.45f, Radius = 0.4f, Drag = 2f, Stretch = true, StretchVel = 0.09f, StretchLen = 2f, Max = 120 }, root, "Speed");
            foreach (var ps in new[] { _chunks, _clods, _dust, _speed }) ps.Play(false);
        }

        protected override void OnCharge(float t, float k)
        {
            _charge.transform.position = Tip();
            _chargeLight.intensity = 2.5f * k;
        }

        protected override void OnLaunch(long nowUs)
        {
            var root = Root.transform;
            _charge.Stop(false, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f;
            var slabMat = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.55f, 0.42f, 0.34f), 0f, 0.1f);
            // rows of slabs: radius 1.3 .. depth, more across at the far rows; each rises when the wave reaches its radius
            int rows = 5;
            for (int r = 0; r < rows; r++)
            {
                float rad = Mathf.Lerp(2.0f, Depth * 1.0f, r / (rows - 1f));
                float arc = 2f * Half * rad;
                int n = Mathf.Max(2, Mathf.RoundToInt(arc / 1.15f));
                for (int k = 0; k < n; k++)
                {
                    int id = r * 31 + k;
                    float u = (k + 0.5f + 0.35f * (Vol2Fs.Hash(Seed, id) - 0.5f)) / n;
                    float ang = Mathf.Lerp(-Half, Half, u);
                    Vector3 o = Dir(ang);
                    float rr = rad + 0.35f * (Vol2Fs.Hash(Seed, id + 7) - 0.5f);
                    Vector3 b = Apex + o * rr; b.y = Ground - 0.5f;
                    float scale = Mathf.Lerp(0.45f, 1.3f, r / (rows - 1f));      // r2: low near the caster, tall far out (r1 walled the view)
                    var mesh = Kit.SharedMesh("slabm" + (id % 6), () => Vol2FsB1.Slab(200 + id % 6, new Vector3(1.0f, 1.6f, 0.45f), 0.3f));      // r2: jagged pointed shards (r1 read as boxes)
                    var pr = Vol2FsB1.Prop(root, "Slab", mesh, slabMat);
                    pr.transform.localScale = new Vector3(Mathf.Lerp(0.8f, 1.2f, Vol2Fs.Hash(Seed, id + 11)), Mathf.Lerp(0.7f, 1.25f, Vol2Fs.Hash(Seed, id + 13)) * scale, Mathf.Lerp(0.8f, 1.3f, Vol2Fs.Hash(Seed, id + 17))) ;
                    pr.transform.position = b;
                    _slabs.Add(new Slab { T = pr.transform, Base = b, Out = o, At = WaveSeconds * (rr / Depth) + 0.02f * Vol2Fs.Hash(Seed, id + 19), Tilt = Mathf.Lerp(45f, 72f, Vol2Fs.Hash(Seed, id + 23)), Rise = 0.5f, Last = r == rows - 1, Spin = (Vol2Fs.Hash(Seed, id + 29) - 0.5f) * 60f });
                }
            }
            // the rim plates (the 끝: the ground round the fan lifts in plates as it sinks)
            var plateMat = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.55f, 0.36f, 0.27f), 0f, 0.06f);
            for (int k = 0; k < 14; k++)
            {
                float u = (k + 0.5f) / 14f; float ang = Mathf.Lerp(-Half * 1.25f, Half * 1.25f, u);
                Vector3 o = Dir(ang);
                Vector3 b = Apex + o * (Depth + 0.8f + 0.4f * Vol2Fs.Hash(Seed, k + 900)); b.y = Ground - 0.22f;
                var mesh = Kit.SharedMesh("slab" + (k % 5), () => Vol2FsB1.Slab(100 + k % 5, new Vector3(1.0f, 0.35f, 0.75f)));
                var pr = Vol2FsB1.Prop(root, "RimPlate", mesh, plateMat);
                pr.transform.position = b;
                _rim.Add(new Slab { T = pr.transform, Base = b, Out = o, At = WaveSeconds + HoldSeconds + 0.1f + 0.15f * Vol2Fs.Hash(Seed, k + 910), Tilt = Mathf.Lerp(12f, 26f, Vol2Fs.Hash(Seed, k + 920)), Rise = 0.18f, Spin = 0f });
            }
            // ink spikes flung straight up off the crest (mo_b), along the far half of the fan
            for (int k = 0; k < 7; k++)
            {
                float u = (k + 0.3f + 0.4f * Vol2Fs.Hash(Seed, k + 940)) / 7f; float ang = Mathf.Lerp(-Half * 1.1f, Half * 1.1f, u);
                Vector3 o = Dir(ang);
                float rr = Mathf.Lerp(Depth * 0.55f, Depth, Vol2Fs.Hash(Seed, k + 950));
                Vector3 b = Apex + o * rr; b.y = Ground + 0.6f;
                Vector3 d = (Vector3.up * 1.6f + o * 0.9f + Vector3.Cross(Vector3.up, o) * (Vol2Fs.Hash(Seed, k + 960) - 0.5f) * 0.9f).normalized;
                _spikes.Add(new Spike { Base = b, Dir = d, At = WaveSeconds * rr / Depth + 0.04f, Len = Mathf.Lerp(2.2f, 4.2f, Vol2Fs.Hash(Seed, k + 970)), W = Mathf.Lerp(0.3f, 0.6f, Vol2Fs.Hash(Seed, k + 980)), Life = 0.8f + 0.4f * Vol2Fs.Hash(Seed, k + 990), Rnd = Vol2Fs.Hash(Seed, k + 999) });
            }
            Vol2Fs.MeshObject(root, "InkSpikes", Kit.InkSolid("fs_stroke2", Ink, new Color(0.3f, 0.18f, 0.1f, 1f), 0.1f, 0.5f, 0.28f, 1.6f), out _inkMesh);
            var crack = M("fs_crack", new Color(0.05f, 0.03f, 0.02f, 1f), 1f, 0f); crack.Soft = 0.001f; crack.NoiseAmt = 0.3f; crack.Opacity = 0.95f;
            Vol2Fs.MeshObject(root, "Fissures", Kit.Mat(crack), out _crackMesh);
            Note = "launch fan depth " + Depth.ToString("0.0") + " slabs " + _slabs.Count;
        }

        protected override void OnStruck(Struck s) { }

        static void Pose(Slab s, float u, float sink)
        {
            Vector3 axis = Vector3.Cross(Vector3.up, s.Out);
            s.T.position = s.Base + Vector3.up * (0.5f + s.Rise) * u - Vector3.up * 0.4f * sink;
            s.T.rotation = Quaternion.AngleAxis(s.Tilt * Mathf.Min(1f, u), axis) * Quaternion.AngleAxis(s.Spin, Vector3.up) * Quaternion.LookRotation(s.Out, Vector3.up);
        }

        protected override void OnSweep(float t, float a, bool ended)
        {
            var cam = CamPos;
            float sink = Vol2Fs.Smooth(WaveSeconds + HoldSeconds, WaveSeconds + HoldSeconds + SinkSeconds, a);
            foreach (var s in _slabs)
            {
                float u = a < s.At ? 0f : Vol2Fs.EaseOutBack((a - s.At) / 0.13f);
                if (!s.Last) u *= 1f - 0.85f * Vol2Fs.Smooth(s.At + 0.3f, s.At + 0.65f, a);      // r2 (mo_a): the wave passes - inner rows fall back, the far crest stands
                Pose(s, u * (1f - sink * 0.9f), sink);
                if (!s.Burst && a >= s.At)
                {
                    s.Burst = true;
                    Vector3 g = s.Base; g.y = Ground + 0.1f;
                    Vol2FsB1.Emit(_chunks, g, 5); Vol2FsB1.Emit(_clods, g, 16); Vol2FsB1.Emit(_dust, g, 2);
                }
            }
            if (sink > 0.05f && !_sunk) { _sunk = true; foreach (var s in _slabs) if (Random.value < 0.5f) Vol2FsB1.Emit(_dust, new Vector3(s.Base.x, Ground + 0.2f, s.Base.z), 1); }
            foreach (var p in _rim)
            {
                float u = a < p.At ? 0f : Vol2Fs.EaseOutBack((a - p.At) / 0.18f) * (1f - Vol2Fs.Smooth(p.At + 0.5f, p.At + 0.9f, a));
                Vector3 axis = Vector3.Cross(Vector3.up, p.Out);
                p.T.gameObject.SetActive(a >= p.At && u > 0.02f);      // r3: hidden until they lift (r2 showed planks lying at the sides)
                p.T.position = p.Base + Vector3.up * (0.22f + p.Rise) * Mathf.Max(0f, u);
                p.T.rotation = Quaternion.AngleAxis(p.Tilt * Mathf.Max(0f, u), axis) * Quaternion.LookRotation(p.Out, Vector3.up);
                if (!p.Burst && a >= p.At) { p.Burst = true; Vol2FsB1.Emit(_clods, new Vector3(p.Base.x, Ground + 0.05f, p.Base.z), 5); }
            }
            // ink spikes
            _ink.Clear();
            foreach (var s in _spikes)
            {
                float e = a - s.At; if (e <= 0f) continue;
                float grow = Vol2Fs.EaseOutBack(e / 0.14f), life = 1f - Vol2Fs.Smooth(0.25f, s.Life, e);
                if (life <= 0.01f) continue;
                var pts = new Vector3[7]; var w = new float[7]; var al = new float[7];
                for (int k = 0; k < 7; k++)
                {
                    float u = k / 6f;
                    pts[k] = s.Base + s.Dir * s.Len * grow * u + Vector3.down * 0.6f * u * u * Vol2Fs.Smooth(0.1f, 0.8f, e);
                    w[k] = s.W * (0.6f + 0.7f * Mathf.Sin(Mathf.Min(1f, u * 1.4f + 0.1f) * Mathf.PI)) * (1f - 0.6f * u);
                    al[k] = life;
                }
                _ink.Strip(pts, w, null, Color.white, al, s.Rnd, cam, 0f, 1f);
            }
            _ink.Write(_inkMesh);
            // fissures across the fan
            _crack.Clear();
            float cl = 1f - Vol2Fs.Smooth(LookSeconds - 0.8f, LookSeconds, a);
            if (cl > 0.01f && a > 0.02f)
            {
                Vector3 c = Apex + F * Depth * 0.55f; c.y = Ground + 0.03f;
                _crack.Quad(c, Right, Fwd, Depth * 1.5f * Vol2Fs.Smooth(0f, WaveSeconds, a), new Color(1f, 1f, 1f, cl), 0.71f);
            }
            _crack.Write(_crackMesh);
            // the struck bodies: a dust blast and streaks flung away from the caster as the wave reaches them
            foreach (var s in Hits)
                if (!s.Started && a >= s.At)
                {
                    s.Started = true;
                    Vector3 c = CentreNow(s); Vector3 o = c - Apex; o.y = 0f; o.Normalize(); Vector3 side = Vector3.Cross(Vector3.up, o);
                    Vol2FsB1.Emit(_dust, c, 10); Vol2FsB1.Emit(_clods, c, 60);
                    for (int k = 0; k < 18; k++) Vol2FsB1.EmitV(_speed, c + side * (Random.value - 0.5f) * 1.2f + Vector3.up * (Random.value - 0.4f), (o + side * (Random.value - 0.5f) * 0.5f + Vector3.up * 0.2f).normalized * Random.Range(9f, 15f));
                    for (int k = 0; k < 10; k++) Vol2FsB1.EmitV(_chunks, c, (o + Vector3.up * Random.Range(0.3f, 1f) + side * (Random.value - 0.5f)).normalized * Random.Range(4f, 9f));
                }
        }
    }
}
