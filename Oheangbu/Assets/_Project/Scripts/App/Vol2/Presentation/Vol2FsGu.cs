using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>구 (wood, ㅜ ring, 한 번 · 붙든다) - free showcase batch 2, concept gu_a (a crown of thorns: a full circle of thick thorned vines whips up out of a
    /// glowing jade-gold crack on the 2 m ring round the caster, leaning out and curling at their tips; the bear on the ring is caught, vines coiling round its legs
    /// and body; leaves, ink and soil spray up all round; the caster stands in the empty middle). The vines sink back into the crack; the struck body's snare holds
    /// for the 2 s slow (a mirror of the rules) and then (the CSV's end) unravels into rootlets reaching forward.</summary>
    public sealed class Vol2FsGu : Vol2FsRing
    {
        static readonly Color Jade = new Color(0.55f, 1.0f, 0.35f), Gold = new Color(1f, 0.85f, 0.4f), Ink = new Color(0.03f, 0.035f, 0.02f);
        ParticleSystem _leaves, _soil, _drops, _sparks, _dust; Light _light, _light2;
        Mesh _vineMesh, _crackMesh, _snareMesh, _rootMesh;
        readonly Vol2FsTubes _vines = new Vol2FsTubes(), _snare = new Vol2FsTubes(), _roots = new Vol2FsTubes();
        readonly Vol2FsStrips _crack = new Vol2FsStrips();
        struct V { public Vol2FsTubes.Tube T; public float At, Ang; public bool Burst; }
        readonly List<V> _list = new List<V>();
        const float GrowSeconds = 0.16f, HoldSeconds = 0.75f, SinkSeconds = 0.5f, SnareSeconds = 2f;
        protected override float LookSeconds => ExpandSeconds + SnareSeconds + 0.4f;

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsB1.M(tex, tint, intensity, addMix);

        protected override void OnBegin()
        {
            var root = Root.transform;
            var vine = Kit.Bark(new Color(0.62f, 0.78f, 0.42f), Jade * 2.6f, 0.18f, "fs_vine", "fs_vein");      // r3: greener (r2 read black grass)
            Vol2Fs.MeshObject(root, "ThornCrown", vine, out _vineMesh);
            Vol2Fs.MeshObject(root, "Snare", vine, out _snareMesh);
            Vol2Fs.MeshObject(root, "Rootlets", Kit.Bark(new Color(0.3f, 0.26f, 0.2f), Jade * 0.4f, 0.15f), out _rootMesh);
            var crack = M("fs_crack", new Color(0.85f, 1f, 0.45f, 1f), 5f, 1f); crack.Soft = 0.001f; crack.NoiseAmt = 0.5f; crack.NoiseScale = 2f;
            Vol2Fs.MeshObject(root, "GlowCrack", Kit.Mat(crack), out _crackMesh);
            var leaf = M("fs_leaf", new Color(0.55f, 0.85f, 0.35f, 1f), 1f, 0f); leaf.Soft = 0.05f; leaf.NoiseAmt = 0.2f;
            _leaves = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 0.9f, Life1 = 1.8f, Speed0 = 1.5f, Speed1 = 5f, Size0 = 0.08f, Size1 = 0.2f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.25f, Gravity = 0.5f, Drag = 1.2f,
                Noise = 1.2f, NoiseFreq = 1.4f, Tiles = 2, Spin = 400f, AlphaHoldTo = 0.6f, Max = 700 }, root, "Leaves");
            var clod = M("fs_ink", new Color(0.3f, 0.2f, 0.15f, 1f), 1f, 0f); clod.Soft = 0.1f; clod.NoiseAmt = 0.3f;
            _soil = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(clod), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 2f, Speed1 = 5f, Size0 = 0.05f, Size1 = 0.13f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.2f, Gravity = 2.4f, Tiles = 2, Max = 700 }, root, "Soil");
            var drop = M("fs_ink", Ink, 1f, 0f); drop.Edge = new Color(0.6f, 1.6f, 0.3f, 1f); drop.EdgeWidth = 0.15f; drop.NoiseAmt = 0.3f; drop.Soft = 0.1f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 3f, Speed1 = 7f, Size0 = 0.08f, Size1 = 0.24f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.2f, Gravity = 2f, Drag = 0.4f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 500 }, root, "InkDrops");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 5f, 1f)), Life0 = 0.5f, Life1 = 1.2f, Speed0 = 0.5f, Speed1 = 2.5f, Size0 = 0.025f, Size1 = 0.06f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.2f, Gravity = -0.3f,
                Noise = 1.4f, NoiseFreq = 2f, Stretch = true, StretchVel = 0.04f, Max = 700 }, root, "Sparks");
            var dust = M("fs_puff", new Color(0.42f, 0.3f, 0.24f, 1f), 1f, 0f); dust.Opacity = 0.55f; dust.Erode = 0.35f;
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dust), Life0 = 0.8f, Life1 = 1.5f, Speed0 = 0.3f, Speed1 = 1.2f, Size0 = 0.5f, Size1 = 1.0f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.3f, Drag = 1.2f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.2f, SizeEnd = 1.6f, Max = 120, SortFudge = 10 }, root, "Dust");
            foreach (var ps in new[] { _leaves, _soil, _drops, _sparks, _dust }) ps.Play(false);
            _light = Vol2Fs.PointLight(root, "JadeLight", Jade, 6f); _light2 = Vol2Fs.PointLight(root, "JadeLight2", Jade, 4f);
        }

        protected override void OnLaunch(long nowUs)
        {
            // the vines of the crown: rooted on the band, rising and leaning out, their tips curling sideways; the near arc (toward the camera) stays low
            const int N = 30;
            for (int k = 0; k < N; k++)
            {
                float ang = Vol2FsB2.Angle(k, N, Seed);
                float back = Back(ang);
                Vector3 d = Dir(ang), side = Vector3.Cross(Vector3.up, d) * (k % 2 == 0 ? 1f : -1f);
                float r = Mathf.Lerp(Inner, Outer, Vol2Fs.Hash(Seed, k + 30));
                float hgt = Mathf.Lerp(1.0f, 2.0f, Vol2Fs.Hash(Seed, k + 60)) * (1f - 0.75f * back);
                Vector3 b = OnBand(ang, r) - Vector3.up * 0.15f;
                var pts = new[] { b, b + Vector3.up * hgt * 0.45f + d * 0.15f + side * 0.1f, b + Vector3.up * hgt * 0.85f + d * 0.45f + side * 0.3f, b + Vector3.up * hgt * 0.95f + d * 0.75f + side * 0.6f, b + Vector3.up * hgt * 0.8f + d * 0.8f + side * 0.85f };
                var t = _vines.Add(pts, Mathf.Lerp(0.06f, 0.1f, Vol2Fs.Hash(Seed, k + 90)), 0.015f, 22); t.Knots = 0.45f; t.Sharp = 0.4f; t.Sides = 7;
                _list.Add(new V { T = t, At = ExpandSeconds * (0.3f + 0.7f * Vol2Fs.Hash(Seed, k + 120)), Ang = ang });
                // thorns along it
                for (int j = 1; j <= 3; j++)
                {
                    Vector3 p = Vol2FsTubes.CatmullRom(pts, j / 4f), o = (Random.onUnitSphere + d * 0.5f).normalized;
                    var th = _vines.Add(new[] { p, p + o * 0.18f }, 0.035f, 0.004f, 3); th.Sharp = 0.15f; th.Sides = 4;
                    _list.Add(new V { T = th, At = ExpandSeconds + 0.04f + 0.03f * j, Ang = ang });
                }
            }
            // a low tangle of vines running round the band
            for (int k = 0; k < 4; k++)
            {
                var pts = new Vector3[13];
                for (int j = 0; j < 13; j++) { float ang = (j / 12f) * Mathf.PI * 1.1f - Mathf.PI * 0.55f + k * 1.57f; pts[j] = OnBand(ang, R + 0.18f * Mathf.Sin(j * 1.7f + k)) + Vector3.up * (0.12f + 0.15f * Mathf.Abs(Mathf.Sin(j * 2.3f + k))); }
                var t = _vines.Add(pts, 0.07f, 0.04f, 40); t.Knots = 0.5f; t.Sides = 7;
                _list.Add(new V { T = t, At = 0.02f * k, Ang = 0f });
            }
            _light.transform.position = OnBand(0f, R) + Vector3.up * 1.0f; _light2.transform.position = Apex + Vector3.up * 0.4f;
        }

        protected override void OnStruck(Struck s) { }

        protected override void OnRing(float t, float a, bool ended)
        {
            var cam = CamPos;
            float sink = Vol2Fs.Smooth(ExpandSeconds + GrowSeconds + HoldSeconds, ExpandSeconds + GrowSeconds + HoldSeconds + SinkSeconds, a);
            foreach (var v in _list)
            {
                float g = Vol2Fs.EaseOutBack(Mathf.Clamp01((a - v.At) / GrowSeconds));
                v.T.Grow = (a < v.At ? 0f : g) * (1f - sink);
            }
            for (int k = 0; k < _list.Count; k++)
            {
                var v = _list[k]; if (v.Burst || a < v.At) continue;
                v.Burst = true; _list[k] = v;
                Vector3 b = v.T.Ctrl[0]; b.y = Ground;
                if (v.T.Ctrl.Length > 2) { Vol2FsB1.Emit(_soil, b, 6); Vol2FsB1.Emit(_leaves, b + Vector3.up * 0.6f, 3); Vol2FsB1.Emit(_drops, b + Vector3.up * 0.3f, 2); Vol2FsB1.Emit(_sparks, b, 4); if (k % 4 == 0) Vol2FsB1.Emit(_dust, b, 1); }
            }
            if (sink > 0.02f && sink < 0.1f && Time.frameCount % 2 == 0) foreach (var v in _list) if (v.T.Ctrl.Length > 2 && Random.value < 0.25f) Vol2FsB1.Emit(_leaves, Vol2FsTubes.TipOf(v.T), 1);
            _vines.Write(_vineMesh);
            // the glowing crack round the band
            _crack.Clear();
            float cl = Vol2Fs.Smooth(0f, ExpandSeconds, a) * (1f - Vol2Fs.Smooth(0.6f, 1.6f, a));
            if (cl > 0.01f) Vol2FsB2.GroundRing(_crack, Apex, F, Ground, Inner - 0.45f, Outer + 0.45f, 64, null, 0.37f, cam, 0.03f, 0f, 6f);
            _crack.Write(_crackMesh);
            float li = Vol2Fs.Smooth(0f, ExpandSeconds, a) * (1f - Vol2Fs.Smooth(0.5f, 1.4f, a));
            _light.intensity = 6f * li; _light2.intensity = 3f * li;
            // the snare on each struck body: holds the 2 s slow, then unravels into rootlets reaching forward
            _snare.Tubes.Clear(); _roots.Tubes.Clear();
            foreach (var s in Hits)
            {
                float sa = a - s.At; if (sa <= 0f) continue;
                if (!s.Started) { s.Started = true; Vol2FsB1.Emit(_leaves, CentreNow(s), 40); Vol2FsB1.Emit(_drops, CentreNow(s), 30); Vol2FsB1.Emit(_sparks, CentreNow(s), 40); }
                var c = CentreNow(s); var feet = FeetNow(s);
                float rx = s.B.extents.x * 1.05f, rz = s.B.extents.z * 1.05f, hy = s.B.size.y;
                float rel = Vol2Fs.Smooth(SnareSeconds - 0.45f, SnareSeconds, sa);
                float grow = Mathf.Clamp01(Vol2Fs.EaseOutBack(sa / 0.25f)) * (1f - rel);
                for (int k = 0; k < 7; k++)
                {
                    float a0 = k / 7f * Mathf.PI * 2f + Vol2Fs.Hash((int)s.Id, k);
                    var pts = new Vector3[7];
                    for (int j = 0; j < 7; j++)
                    {
                        float u = j / 6f, an = a0 + u * 2.6f * (k % 2 == 0 ? 1f : -1f), shrink = 1f - 0.25f * u + 0.5f * rel;
                        pts[j] = new Vector3(c.x + Mathf.Cos(an) * rx * shrink, feet.y + u * hy * 0.95f, c.z + Mathf.Sin(an) * rz * shrink);
                    }
                    pts[0].y = feet.y - 0.1f;
                    var tb = _snare.Add(pts, 0.055f, 0.022f, 18); tb.Knots = 0.45f; tb.Sharp = 0.3f; tb.Grow = grow; tb.Sides = 6;
                }
                // rootlets: from the feet forward (away from the caster), as the snare lets go
                float run = Vol2Fs.Smooth(SnareSeconds - 0.4f, SnareSeconds + 0.1f, sa), rs = Vol2Fs.Smooth(SnareSeconds + 0.2f, SnareSeconds + 0.6f, sa);
                Vector3 outw = feet - Apex; outw.y = 0f; outw = outw.sqrMagnitude > 1e-4f ? outw.normalized : F;
                for (int k = 0; k < 8 && run > 0f; k++)
                {
                    float an = (k / 7f - 0.5f) * 1.4f; Vector3 d = Quaternion.AngleAxis(an * Mathf.Rad2Deg, Vector3.up) * outw;
                    Vector3 e = feet + d * Mathf.Lerp(1.2f, 2.2f, Vol2Fs.Hash((int)s.Id, k + 40)); e.y = Ground + 0.02f;
                    var tb = _roots.Add(new[] { feet + Vector3.up * 0.05f, Vector3.Lerp(feet, e, 0.5f) + Vector3.up * 0.1f, e }, 0.028f, 0.007f, 10); tb.Knots = 0.5f; tb.Grow = run * (1f - rs); tb.Sides = 5;
                }
            }
            _snare.Write(_snareMesh); _roots.Write(_rootMesh);
        }
    }
}
