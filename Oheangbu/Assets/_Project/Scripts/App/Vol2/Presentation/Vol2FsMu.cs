using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>무 (earth, ㅜ ring, 한 번 · 민다) - free showcase batch 2, concept mu_a (the ground heaves up in a thick rolling circular ridge of soil and rock all round
    /// the caster, like a stone ripple bursting outward from a dropped weight; the bear on the ring is thrown outward by it; a huge donut of dust rolls out, rocks
    /// fly, and (the CSV's end) the ridge sinks while the ground round it lifts as plates, nothing falling). Unlike 모's fan (slabs erupting in rows) and 머's
    /// single wall, it is one travelling earth wave, closed round the caster.</summary>
    public sealed class Vol2FsMu : Vol2FsRing
    {
        static readonly Color Soil = new Color(0.62f, 0.42f, 0.33f), Ink = new Color(0.04f, 0.03f, 0.025f);
        ParticleSystem _rocks, _clods, _dust, _donut, _drops; Light _light;
        Mesh _ridgeMesh, _crackMesh, _streakMesh, _strokeMesh;
        readonly Vol2FsStrips _crack = new Vol2FsStrips(), _streak = new Vol2FsStrips(), _strokes = new Vol2FsStrips();
        sealed class Plate { public Transform T; public Vector3 Base, Out; public float Tilt, At; }
        readonly List<Plate> _plates = new List<Plate>();
        struct Throw { public float Ang, Len, W, At, Rnd; }
        readonly List<Throw> _throws = new List<Throw>();
        const float RollSeconds = 0.42f, PlatesAt = 0.55f;
        float[] _lump;
        bool _burst;
        protected override float ExpandSeconds => 0.14f;
        protected override float LookSeconds => RollSeconds + 1.5f;
        protected override float StruckAt(float ang) => ExpandSeconds;

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsB1.M(tex, tint, intensity, addMix);

        protected override void OnBegin()
        {
            var root = Root.transform;
            var soil = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.86f, 0.58f, 0.46f), 0f, 0.08f);
            Vol2Fs.MeshObject(root, "Ridge", soil, out _ridgeMesh).shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var stone = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.78f, 0.6f, 0.5f), 0f, 0.12f);
            for (int k = 0; k < 16; k++)
            {
                var p = new Plate { T = Vol2FsB1.Prop(root, "Plate", Kit.SharedMesh("mu_plate" + (k % 4), () => Vol2FsB1.Slab(90 + k % 4, new Vector3(1.05f, 0.3f, 0.8f), 0.88f)), stone).transform };
                p.T.gameObject.SetActive(false);
                _plates.Add(p);
            }
            _rocks = Vol2Fs.Build(new Vol2FsPs { Mat = stone, Mesh = Kit.SharedMesh("mu_rock", () => Vol2FsB1.Rock(95, 1f, 0.8f, 8, 6)), Life0 = 1.0f, Life1 = 1.7f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.1f, Size1 = 0.32f,
                Shape = ParticleSystemShapeType.Sphere, Radius = 0.2f, Gravity = 2.2f, Drag = 0.15f, Spin = 300f, Max = 300 }, root, "Rocks");
            var clod = M("fs_ink", new Color(0.3f, 0.2f, 0.15f, 1f), 1f, 0f); clod.Soft = 0.1f; clod.NoiseAmt = 0.3f;
            _clods = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(clod), Life0 = 0.7f, Life1 = 1.4f, Speed0 = 2f, Speed1 = 7f, Size0 = 0.05f, Size1 = 0.16f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.3f, Gravity = 2.4f, Tiles = 2, Max = 1200 }, root, "Clods");
            var dust = M("fs_puff", Soil, 1f, 0f); dust.Opacity = 0.72f; dust.Erode = 0.35f; dust.NoiseAmt = 0.55f;
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dust), Life0 = 0.9f, Life1 = 1.7f, Speed0 = 0.4f, Speed1 = 1.6f, Size0 = 0.6f, Size1 = 1.3f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.3f, Drag = 1.4f, Tiles = 2,
                SizeStart = 0.4f, SizePeak = 1.2f, SizePeakAt = 0.4f, SizeEnd = 1.7f, Max = 400, SortFudge = 8 }, root, "Dust");
            _donut = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dust), Life0 = 1.2f, Life1 = 2.0f, Speed0 = 0f, Speed1 = 0.2f, Size0 = 1.2f, Size1 = 2.2f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.2f, Drag = 2.2f, Tiles = 2,
                SizeStart = 0.35f, SizePeak = 1.1f, SizePeakAt = 0.35f, SizeEnd = 1.6f, Max = 260, SortFudge = 10 }, root, "DustDonut");
            var drop = M("fs_ink", Ink, 1f, 0f); drop.Edge = new Color(0.9f, 0.55f, 0.35f, 1f); drop.EdgeWidth = 0.12f; drop.NoiseAmt = 0.3f; drop.Soft = 0.1f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.08f, Size1 = 0.26f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.2f, Gravity = 2.2f, Drag = 0.4f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 400 }, root, "InkDrops");
            foreach (var ps in new[] { _rocks, _clods, _dust, _donut, _drops }) ps.Play(false);
            var crack = M("fs_crack", new Color(0.08f, 0.05f, 0.04f, 1f), 1f, 0f); crack.Soft = 0.001f; crack.NoiseAmt = 0.4f;
            Vol2Fs.MeshObject(root, "Cracks", Kit.Mat(crack), out _crackMesh);
            var st = M("fs_puff", Soil, 1f, 0f); st.Opacity = 0.6f; st.Soft = 0.05f;
            Vol2Fs.MeshObject(root, "ShoveStreaks", Kit.Mat(st), out _streakMesh);
            Vol2Fs.MeshObject(root, "InkThrows", Kit.InkSolid("fs_stroke2", Ink, new Color(1.2f, 0.75f, 0.5f, 1f), 0.06f, 0.5f, 0.26f, 1.5f), out _strokeMesh);
            _light = Vol2Fs.PointLight(root, "Flash", new Color(1f, 0.82f, 0.62f), 7f);
            _lump = new float[73];
            for (int k = 0; k < _lump.Length; k++) _lump[k] = 0.85f + 0.3f * Mathf.PerlinNoise(k * 0.25f, Seed * 0.01f);
            _lump[72] = _lump[0];
        }

        protected override void OnLaunch(long nowUs)
        {
            _light.transform.position = Apex + F * (R + 0.6f) + Vector3.up * 0.8f;     // r2: off the caster (r1 lit him white)
            for (int k = 0; k < 9; k++)
                _throws.Add(new Throw { Ang = Mathf.Lerp(-1.9f, 1.9f, (k + 0.5f) / 9f) + 0.15f * (Vol2Fs.Hash(Seed, k + 600) - 0.5f), Len = Mathf.Lerp(1.6f, 2.8f, Vol2Fs.Hash(Seed, k + 610)), W = Mathf.Lerp(0.35f, 0.6f, Vol2Fs.Hash(Seed, k + 620)),
                    At = ExpandSeconds + 0.04f * Vol2Fs.Hash(Seed, k + 630), Rnd = Vol2Fs.Hash(Seed, k + 640) });
            for (int k = 0; k < _plates.Count; k++)
            {
                float ang = Vol2FsB2.Angle(k, _plates.Count, Seed + 3, 0.5f);
                var p = _plates[k]; p.Out = Dir(ang); p.Base = OnBand(ang, R + 0.9f + 0.35f * Vol2Fs.Hash(Seed, k + 700)); p.Tilt = Mathf.Lerp(20f, 40f, Vol2Fs.Hash(Seed, k + 710)) * (1f - 0.6f * Back(ang)); p.At = PlatesAt + 0.08f * Vol2Fs.Hash(Seed, k + 720);
            }
        }

        protected override void OnStruck(Struck s) { }

        void WriteRidge(float rc, float h, float half)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>();
            const int SN = 72, CN = 8;
            if (h > 0.01f)
            {
                for (int i = 0; i <= SN; i++)
                {
                    float ang = (i / (float)SN * 2f - 1f) * Mathf.PI;
                    float lump = _lump[i], back = Back(ang);
                    Vector3 d = Dir(ang);
                    for (int j = 0; j <= CN; j++)
                    {
                        float x = j / (float)CN * 2f - 1f;                                  // -1 inner foot .. 1 outer foot
                        float prof = Mathf.Pow(Mathf.Max(0f, 1f - x * x), 1.2f) * (1f + 0.1f * Mathf.Sin(x * 3f + ang * 5f));
                        float r = rc + x * half * (x > 0f ? 1.25f : 0.8f);                  // the front of the wave is longer
                        Vector3 p = Apex + d * r; p.y = Ground - 0.12f + prof * h * lump * (1f - 0.55f * back);
                        v.Add(p); uv.Add(new Vector2(ang * 2f, x));
                    }
                }
                for (int i = 0; i < SN; i++)
                    for (int j = 0; j < CN; j++)
                    {
                        int a = i * (CN + 1) + j, b = a + 1, c = a + CN + 1, dd = c + 1;
                        idx.Add(a); idx.Add(b); idx.Add(c); idx.Add(b); idx.Add(dd); idx.Add(c);
                    }
            }
            _ridgeMesh.Clear(); _ridgeMesh.SetVertices(v); _ridgeMesh.SetUVs(0, uv); _ridgeMesh.SetTriangles(idx, 0); _ridgeMesh.RecalculateNormals(); _ridgeMesh.RecalculateBounds();
        }

        protected override void OnRing(float t, float a, bool ended)
        {
            var cam = CamPos;
            // the earth wave: rises out of the ground near the feet, rolls out through the band (its crest on the band at the strike), slumps behind it
            float roll = Mathf.Clamp01(a / RollSeconds);
            float rc = Mathf.Lerp(0.7f, R + 0.9f, 1f - Mathf.Pow(1f - roll, 1.6f));
            float h = 1.1f * Vol2Fs.Smooth(0f, 0.08f, a) * (1f - Vol2Fs.Smooth(RollSeconds * 0.55f, RollSeconds + 0.25f, a));
            WriteRidge(rc, h, Mathf.Lerp(0.45f, 0.8f, roll));
            if (!_burst && a >= ExpandSeconds)
            {
                _burst = true;
                for (int k = 0; k < 64; k++)
                {
                    float ang = Vol2FsB2.Angle(k, 64, Seed + 9, 0.8f);
                    Vector3 p = OnBand(ang, R), d = Dir(ang);
                    float back = Back(ang);
                    Vol2FsB1.EmitV(_rocks, p + Vector3.up * 0.4f, (d * Random.Range(2f, 6f) + Vector3.up * Random.Range(3f, 7f)) * (1f - 0.6f * back));
                    Vol2FsB1.Emit(_clods, p, 6);
                    if (k % 2 == 0) Vol2FsB1.Emit(_drops, p + Vector3.up * 0.3f, 1);
                }
                _light.intensity = 5f;
            }
            _light.intensity = Mathf.Max(0f, _light.intensity - Time.deltaTime * 20f);
            // the dust donut rolling out with the wave
            if (a < RollSeconds + 0.1f && Time.frameCount % 1 == 0)
                for (int k = 0; k < 5; k++)
                {
                    float ang = Random.Range(-Mathf.PI, Mathf.PI);
                    Vector3 p = OnBand(ang, rc + 0.3f) + Vector3.up * 0.4f;
                    Vol2FsB1.EmitV(_donut, p, Dir(ang) * Random.Range(2f, 4.5f) * (1f - 0.5f * Back(ang)) + Vector3.up * Random.Range(0.3f, 1.2f));
                }
            // the ink thrown outward off the crest (front and sides)
            _strokes.Clear();
            foreach (var th in _throws)
            {
                float s = a - th.At; if (s <= 0f) continue;
                float grow = Vol2Fs.EaseOutBack(Mathf.Clamp01(s / 0.14f)), life = 1f - Vol2Fs.Smooth(0.3f, 1.0f, s);
                if (life <= 0.01f) continue;
                Vector3 b = OnBand(th.Ang, R + 0.2f) + Vector3.up * 0.6f, d = (Dir(th.Ang) + Vector3.up * 0.55f).normalized;
                var pts = new Vector3[7]; var w = new float[7]; var al = new float[7];
                for (int k = 0; k < 7; k++) { float u = k / 6f; pts[k] = b + d * th.Len * grow * u + Vector3.down * 0.5f * th.Len * u * u * Vol2Fs.Smooth(0.05f, 0.6f, s); w[k] = th.W * (0.55f + 0.75f * Mathf.Sin(Mathf.Min(1f, u * 1.4f + 0.1f) * Mathf.PI)) * (1f - 0.55f * u); al[k] = life; }
                _strokes.Strip(pts, w, null, Color.white, al, th.Rnd, cam, 0f, 1f);
            }
            _strokes.Write(_strokeMesh);
            // cracks round the band
            _crack.Clear();
            float cg = Vol2Fs.Smooth(ExpandSeconds, ExpandSeconds + 0.25f, a) * (1f - Vol2Fs.Smooth(1.2f, 1.9f, a));
            if (cg > 0.01f) Vol2FsB2.GroundRing(_crack, Apex, F, Ground, Inner - 0.6f, Outer + 1.4f, 64, null, 0.53f, cam, 0.02f, 0f, 6f);
            _crack.Write(_crackMesh);
            // the plates round the ring: lifted as the wave sinks, settled back after (nothing falls)
            foreach (var p in _plates)
            {
                float pa = a - p.At;
                float up = Vol2Fs.EaseOutBack(Mathf.Clamp01(pa / 0.18f)) * (1f - Vol2Fs.Smooth(0.45f, 0.85f, pa));
                p.T.gameObject.SetActive(pa > -0.02f && pa < 1.0f);
                Vector3 axis = Vector3.Cross(Vector3.up, p.Out);
                p.T.SetPositionAndRotation(p.Base + Vector3.up * (0.12f * up - 0.2f), Quaternion.AngleAxis(p.Tilt * up, axis) * Quaternion.LookRotation(p.Out));
                if (pa > 0f && pa < 0.03f) { Vol2FsB1.Emit(_dust, p.Base, 1); Vol2FsB1.Emit(_clods, p.Base, 6); }
            }
            // the struck body: a burst of dust and earth, shove streaks outward
            _streak.Clear();
            foreach (var s in Hits)
            {
                float sa = a - s.At; if (sa < 0f) continue;
                var c = CentreNow(s); Vector3 outw = c - Apex; outw.y = 0f; outw = outw.sqrMagnitude > 1e-4f ? outw.normalized : F;
                if (!s.Started) { s.Started = true; Vol2FsB1.Emit(_dust, c, 14); Vol2FsB1.Emit(_clods, c, 40); for (int k = 0; k < 20; k++) Vol2FsB1.EmitV(_rocks, c, (outw + Random.insideUnitSphere * 0.6f + Vector3.up * 0.5f).normalized * Random.Range(3f, 8f)); }
                float life = 1f - Vol2Fs.Smooth(0.15f, 0.6f, sa);
                if (life > 0.01f)
                    for (int k = 0; k < 5; k++)
                    {
                        Vector3 o = Vector3.up * (0.2f + 0.25f * k) + Vector3.Cross(Vector3.up, outw) * (k - 2f) * 0.18f;
                        Vector3 p0 = c - outw * 0.3f + o, p1 = c + outw * (1.2f + 1.6f * Vol2Fs.Smooth(0f, 0.3f, sa)) + o;
                        _streak.Strip(new[] { p0, Vector3.Lerp(p0, p1, 0.5f), p1 }, new[] { 0.05f, 0.18f, 0.05f }, null, Color.white, new[] { 0f, life, 0f }, 0.1f * k, cam, 0f, 1f);
                    }
            }
            _streak.Write(_streakMesh);
        }
    }
}
