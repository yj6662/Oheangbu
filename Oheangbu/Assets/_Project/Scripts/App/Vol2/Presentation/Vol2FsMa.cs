using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>마 (earth, ㅏ path, 한 번 · 민다) - free showcase batch 1, concept ma_a (a boulder rammed along a low line: rock and dust gather into a cracked boulder at
    /// the brush, it is hurled ploughing a furrow, and SLAMS the first bear - the boulder bursts into chunks, black ink strokes and a dust wall fling AWAY from the
    /// caster, the ground round the hit heaves up in broken tilted plates that settle back). Heavy, no glow. The shove itself is the executor's (the look never
    /// moves a body).</summary>
    public sealed class Vol2FsMa : Vol2FsShot
    {
        static readonly Color Ink = new Color(0.04f, 0.03f, 0.025f), Soil = new Color(0.36f, 0.22f, 0.15f), Dust = new Color(0.5f, 0.38f, 0.29f);

        Transform _boulder; MeshRenderer _boulderR; ParticleSystem _gather, _gatherDust, _trailDust, _trailBits, _chunks, _dustCloud, _clods, _ring, _speed;
        Mesh _furrowMesh; readonly Vol2FsStrips _furrow = new Vol2FsStrips();
        Mesh _trailInk; readonly Vol2FsStrips _trailS = new Vol2FsStrips();
        Mesh _burstMesh; readonly Vol2FsStrips _burstS = new Vol2FsStrips(); List<Vol2FsB1.Stroke> _burst;
        Mesh _crackMesh; readonly Vol2FsStrips _crackS = new Vol2FsStrips();
        sealed class Plate { public Transform T; public Vector3 Base, Out; public float Tilt, Rise, Delay; }
        readonly List<Plate> _plates = new List<Plate>();
        Material _rock;
        Vector3 _fwd, _side;
        Light _hitLight; bool _settleDust;

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsB1.M(tex, tint, intensity, addMix);

        protected override float LobHeight => 0.35f;
        Vector3 Hold() => Tip() + Fwd * 1.1f + Vector3.up * 0.6f;

        protected override void OnBegin()
        {
            var root = Root.transform;
            _rock = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.62f, 0.56f, 0.5f), 0f, 0.12f);
            var rockMesh = Kit.SharedMesh("rock0", () => Vol2FsB1.Rock(17, 0.85f, 0.86f, 16, 11));
            _boulderR = Vol2FsB1.Prop(root, "Boulder", rockMesh, _rock); _boulder = _boulderR.transform;
            _boulder.position = Hold(); _boulder.localScale = Vector3.one * 0.05f;
            // rock chunks and soil pulled into the forming boulder
            var chunk = Kit.SharedMesh("chunk0", () => Vol2FsB1.Rock(23, 0.5f, 0.7f, 7, 5));
            _gather = Vol2Fs.Build(new Vol2FsPs { Mat = _rock, Mesh = chunk, Rate = 70f, Loop = true, Life0 = 0.22f, Life1 = 0.3f, Speed0 = -4f, Speed1 = -6f, Size0 = 0.08f, Size1 = 0.22f, Radius = 1.4f, World = false, Spin = 300f, Max = 60 }, _boulder, "Gather");
            var dust = M("fs_puff", Dust, 1f, 0f); dust.Opacity = 0.5f; dust.NoiseAmt = 0.6f; dust.Erode = 0.35f;
            _gatherDust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dust), Rate = 25f, Loop = true, Life0 = 0.25f, Life1 = 0.4f, Speed0 = -3f, Speed1 = -4f, Size0 = 0.4f, Size1 = 0.8f, Radius = 1.2f, Tiles = 2, World = false, Max = 30 }, _boulder, "GatherDust");
            _gather.Play(false); _gatherDust.Play(false);
            // shared pools
            _trailDust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dust), Life0 = 0.8f, Life1 = 1.5f, Speed0 = 0.3f, Speed1 = 1.2f, Size0 = 0.7f, Size1 = 1.4f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.4f, Drag = 1f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.2f, SizeEnd = 1.7f, VelOverLife = new Vector3(0f, 0.4f, 0f), Max = 200, SortFudge = 10 }, root, "TrailDust");
            _trailBits = Vol2Fs.Build(new Vol2FsPs { Mat = _rock, Mesh = chunk, Life0 = 0.6f, Life1 = 1.1f, Speed0 = 1.5f, Speed1 = 4f, Size0 = 0.05f, Size1 = 0.14f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.3f, Gravity = 2.5f, Spin = 400f, Max = 200 }, root, "TrailBits");
            _chunks = Vol2Fs.Build(new Vol2FsPs { Mat = _rock, Mesh = chunk, Life0 = 1.0f, Life1 = 1.8f, Speed0 = 4f, Speed1 = 10f, Size0 = 0.12f, Size1 = 0.5f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.4f, Gravity = 2.2f, Drag = 0.3f, Spin = 500f, AlphaHoldTo = 0.9f, Max = 120 }, root, "Chunks");
            var cloud = M("fs_puff", Dust, 1f, 0f); cloud.Opacity = 0.8f; cloud.NoiseAmt = 0.6f; cloud.Erode = 0.35f;
            _dustCloud = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(cloud), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 1.5f, Speed1 = 5f, Size0 = 1.6f, Size1 = 3.4f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.8f, Drag = 2f, Tiles = 2,
                SizeStart = 0.4f, SizePeak = 1.2f, SizePeakAt = 0.4f, SizeEnd = 1.6f, VelOverLife = new Vector3(0f, 0.5f, 0f), Max = 60, SortFudge = 10 }, root, "DustCloud");
            var clod = M("fs_ink", Soil, 1f, 0f); clod.NoiseAmt = 0.4f; clod.Soft = 0.1f;
            _clods = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(clod), Life0 = 0.6f, Life1 = 1.3f, Speed0 = 3f, Speed1 = 9f, Size0 = 0.08f, Size1 = 0.3f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.4f, Gravity = 2.6f, Tiles = 2, AlphaHoldTo = 0.7f, Max = 400 }, root, "Clods");
            var ring = M("fs_ring", Dust, 1f, 0f); ring.Opacity = 0.8f; ring.NoiseAmt = 0.5f; ring.Soft = 0.02f;
            _ring = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ring), Life0 = 0.6f, Life1 = 0.6f, Speed0 = 0f, Speed1 = 0f, Size0 = 6.5f, Size1 = 6.5f, Radius = 0.01f, Horizontal = true, SizeStart = 0.1f, SizePeak = 0.85f, SizePeakAt = 0.5f, SizeEnd = 1f, AlphaHoldTo = 0.3f, Max = 2 }, root, "Ring");
            // dust streaks flung away behind the struck body (the shove)
            var streak = M("fs_streak", Dust, 1f, 0f); streak.Opacity = 0.9f; streak.NoiseAmt = 0.4f;
            _speed = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(streak), Life0 = 0.3f, Life1 = 0.55f, Speed0 = 9f, Speed1 = 16f, Size0 = 0.25f, Size1 = 0.5f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.5f, Drag = 2f, Stretch = true, StretchVel = 0.09f, StretchLen = 2f, Max = 80 }, root, "Speed");
            foreach (var ps in new[] { _trailDust, _trailBits, _chunks, _dustCloud, _clods, _ring, _speed }) ps.Play(false);
        }

        protected override void OnCharge(float t, float k)
        {
            _boulder.position = Hold() + Vector3.up * 0.05f * Mathf.Sin(t * 30f) * k;
            _boulder.localScale = Vector3.one * Mathf.Lerp(0.08f, 1f, Vol2Fs.Smooth(0f, 1f, k));
            _boulder.rotation = Quaternion.AngleAxis(t * 160f, Vector3.up) * Quaternion.AngleAxis(t * 90f, Vector3.right);
        }

        protected override void OnLaunch(long nowUs)
        {
            var root = Root.transform;
            _gather.Stop(false, ParticleSystemStopBehavior.StopEmitting); _gatherDust.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            From = _boulder.position;                        // the boulder leaves from where it was formed
            _boulder.localScale = Vector3.one;
            _fwd = Flat(HitPoint - From); _side = Vector3.Cross(Vector3.up, _fwd);
            var fur = M("fs_stroke2", new Color(0.12f, 0.07f, 0.05f, 1f), 1f, 0f); fur.Soft = 0.001f; fur.NoiseAmt = 0.55f; fur.Erode = 0.3f; fur.Opacity = 0.85f;
            Vol2Fs.MeshObject(root, "Furrow", Kit.Mat(fur), out _furrowMesh);
            Vol2Fs.MeshObject(root, "TrailInk", Kit.InkSolid("fs_stroke2", Ink, new Color(0.25f, 0.16f, 0.1f, 1f), 0.1f, 0.5f, 0.3f, 1.6f), out _trailInk);
            Vol2FsB1.Emit(_trailDust, From, 6); Vol2FsB1.Emit(_trailBits, From, 10);
        }

        protected override void OnFly(float t, float f, float prev)
        {
            Vector3 p = Line(f);
            _boulder.position = p;
            float len = Vector3.Distance(From, HitPoint);
            _boulder.rotation = Quaternion.AngleAxis(f * len / 0.62f * Mathf.Rad2Deg, _side) * Quaternion.AngleAxis(37f, Vector3.up);
            int n = Mathf.CeilToInt((f - prev) * len * 3f);
            for (int k = 0; k < n; k++)
            {
                var q = Line(Mathf.Lerp(prev, f, (k + 0.5f) / n)); var g = new Vector3(q.x, Ground + 0.05f, q.z);
                Vol2FsB1.Emit(_trailDust, g, 1); Vol2FsB1.Emit(_trailBits, g, 3); Vol2FsB1.Emit(_clods, g, 3);
            }
            DrawTrail(f, 0f, CamPos);
        }

        /// <summary>The furrow ploughed under the flight (ground strip) and two black ink streaks dragged behind the boulder (ma_a's ink slashes).</summary>
        void DrawTrail(float f, float since, Vector3 cam)
        {
            _furrow.Clear(); _trailS.Clear();
            float life = 1f - Vol2Fs.Smooth(0.3f, 1.4f, since);
            if (f > 0.02f && life > 0.01f)
            {
                var pts = new List<Vector3>(); var w = new List<float>(); var al = new List<float>(); var sd = new List<Vector3>();
                for (int k = 0; k < 12; k++)
                {
                    float u = k / 11f * f; var q = Line(u); q.y = Ground + 0.025f;
                    pts.Add(q); w.Add(Mathf.Lerp(0.5f, 1.3f, u)); al.Add(life * Mathf.Clamp01(u * 4f)); sd.Add(_side);
                }
                _furrow.Strip(pts, w, sd, Color.white, al, 0.61f, cam, 1f, 0f);
                float inkLife = 1f - Vol2Fs.Smooth(0.05f, 0.6f, since);
                for (int s = -1; s <= 1 && inkLife > 0.01f; s += 2)
                {
                    var ip = new List<Vector3>(); var iw = new List<float>(); var ia = new List<float>();
                    for (int k = 0; k < 10; k++)
                    {
                        float u = k / 9f; float at = Mathf.Max(0f, f - 0.55f * (1f - u));
                        ip.Add(Line(at) + _side * s * 0.45f * (1f - u) + Vector3.up * (0.1f + 0.25f * (1f - u)));
                        iw.Add(0.35f * Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI * 0.9f + 0.1f)); ia.Add(inkLife * Mathf.Clamp01(u * 2.5f));
                    }
                    _trailS.Strip(ip, iw, null, Color.white, ia, 0.3f + s * 0.2f, cam, 1f, 0f);
                }
            }
            _furrow.Write(_furrowMesh); _trailS.Write(_trailInk);
        }

        protected override void OnHit(long nowUs)
        {
            var root = Root.transform;
            _boulderR.enabled = false;                       // the boulder bursts
            Vector3 feet = FeetNow(); Vector3 g = new Vector3(feet.x, Ground, feet.z);
            // chunks, clods, dust - biased AWAY from the caster (the shove)
            for (int k = 0; k < 46; k++)
            {
                Vector3 d = (_fwd * (0.5f + Vol2Fs.Hash(Seed, k)) + _side * (Vol2Fs.Hash(Seed, k + 40) - 0.5f) * 2.2f + Vector3.up * (0.4f + Vol2Fs.Hash(Seed, k + 80))).normalized;
                Vol2FsB1.EmitV(_chunks, HitPoint + d * 0.3f, d * Mathf.Lerp(4f, 10f, Vol2Fs.Hash(Seed, k + 120)), Mathf.Lerp(0.12f, 0.55f, Vol2Fs.Hash(Seed, k + 160)), Mathf.Lerp(1f, 1.8f, Vol2Fs.Hash(Seed, k + 200)));
            }
            Vol2FsB1.Emit(_clods, HitPoint, 160); Vol2FsB1.Emit(_dustCloud, HitPoint + _fwd * 0.6f, 26); Vol2FsB1.Emit(_dustCloud, g, 12);
            for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_speed, HitPoint + _side * (Vol2Fs.Hash(Seed, k + 300) - 0.5f) * 1.2f + Vector3.up * (Vol2Fs.Hash(Seed, k + 330) - 0.3f), (_fwd + _side * (Vol2Fs.Hash(Seed, k + 360) - 0.5f) * 0.6f).normalized * Mathf.Lerp(9f, 16f, Vol2Fs.Hash(Seed, k + 390)));
            Vol2FsB1.EmitV(_ring, g + Vector3.up * 0.06f, Vector3.zero);
            // ink strokes flung away (brown-black, no glow)
            _burst = ForwardSplats();      // r2: ink thrown AWAY and low (r1's star repeated the 나 burst)
            Vol2Fs.MeshObject(root, "InkBurst", Kit.InkSolid("fs_stroke2", Ink, new Color(0.3f, 0.18f, 0.1f, 1f), 0.1f, 0.5f, 0.28f, 1.6f), out _burstMesh);
            // ground: fissures and the heaving plates round the hit
            var crack = M("fs_crack", new Color(0.05f, 0.03f, 0.02f, 1f), 1f, 0f); crack.Soft = 0.001f; crack.NoiseAmt = 0.3f; crack.Opacity = 0.95f;
            Vol2Fs.MeshObject(root, "Fissures", Kit.Mat(crack), out _crackMesh);
            var slabMat = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.62f, 0.42f, 0.32f), 0f, 0.08f);
            for (int k = 0; k < 11; k++)
            {
                float a = (k + 0.4f * Vol2Fs.Hash(Seed, k + 500)) / 11f * Mathf.PI * 2f;
                Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float r = Mathf.Lerp(1.2f, 2.6f, Vol2Fs.Hash(Seed, k + 520));
                var mesh = Kit.SharedMesh("slab" + (k % 5), () => Vol2FsB1.Slab(100 + k % 5, new Vector3(1.0f, 0.35f, 0.75f)));
                var pr = Vol2FsB1.Prop(root, "Plate", mesh, slabMat);
                var pl = new Plate { T = pr.transform, Base = g + o * r - Vector3.up * 0.26f, Out = o, Tilt = Mathf.Lerp(30f, 60f, Vol2Fs.Hash(Seed, k + 540)), Rise = Mathf.Lerp(0.5f, 1.0f, Vol2Fs.Hash(Seed, k + 560)), Delay = 0.03f * r };
                pl.T.localScale = new Vector3(Mathf.Lerp(1.3f, 2.0f, Vol2Fs.Hash(Seed, k + 580)), 0.7f, Mathf.Lerp(1.1f, 1.7f, Vol2Fs.Hash(Seed, k + 600)));
                _plates.Add(pl);
                Vol2FsB1.Emit(_clods, g + o * r, 6);
            }
            _hitLight = Vol2Fs.PointLight(root, "HitLight", new Color(1f, 0.85f, 0.65f), 6f); _hitLight.transform.position = HitPoint;
        }

        protected override void OnAfter(float t, float h, bool ended)
        {
            var cam = CamPos;
            _hitLight.intensity = h < 0.04f ? 4f * h / 0.04f : 4f * Mathf.Exp(-(h - 0.04f) * 8f);
            DrawTrail(1f, h, cam);
            _burstS.Clear(); Vol2FsB1.DrawBurst(_burstS, _burst, HitPoint, h, cam, 0.45f, 0.09f); _burstS.Write(_burstMesh);
            // plates: heave (fast), hold, settle back and sink
            float settle = Vol2Fs.Smooth(1.1f, 1.7f, h);
            foreach (var p in _plates)
            {
                float u = Vol2Fs.EaseOutBack((h - p.Delay) / 0.16f);
                u = Mathf.Max(0f, u) * (1f - settle);
                Vector3 axis = Vector3.Cross(Vector3.up, p.Out);
                p.T.position = p.Base + Vector3.up * (p.Rise + 0.26f) * u - Vector3.up * 0.1f * settle;
                p.T.rotation = Quaternion.AngleAxis(p.Tilt * u, axis) * Quaternion.LookRotation(p.Out, Vector3.up);
            }
            if (h > 1.1f && !_settleDust && (_settleDust = true)) foreach (var p in _plates) Vol2FsB1.Emit(_trailDust, p.Base + Vector3.up * 0.3f, 1);
            _crackS.Clear();
            float cl = 1f - Vol2Fs.Smooth(1.6f, 2.6f, h);
            if (cl > 0.01f) _crackS.Quad(new Vector3(HitPoint.x, Ground + 0.03f, HitPoint.z), Right, Fwd, 5.2f * Vol2Fs.Smooth(0f, 0.12f, h), new Color(1f, 1f, 1f, cl), 0.44f);
            _crackS.Write(_crackMesh);
        }

        /// <summary>r2: thick ink splats thrown forward and low (the shove's direction), not a star.</summary>
        List<Vol2FsB1.Stroke> ForwardSplats()
        {
            var list = new List<Vol2FsB1.Stroke>();
            for (int k = 0; k < 9; k++)
            {
                float s = (k / 8f - 0.5f) * 2f;
                Vector3 d = (_fwd * 1.1f + _side * s * 1.3f + Vector3.up * (0.15f + 0.35f * Vol2Fs.Hash(Seed, k + 800))).normalized;
                list.Add(new Vol2FsB1.Stroke { Dir = d, Len = Mathf.Lerp(2.2f, 4.2f, Vol2Fs.Hash(Seed, k + 810)), Width = Mathf.Lerp(0.5f, 0.85f, Vol2Fs.Hash(Seed, k + 820)), Delay = 0.03f * Vol2Fs.Hash(Seed, k + 830), Life = 0.9f + 0.5f * Vol2Fs.Hash(Seed, k + 840), Rnd = Vol2Fs.Hash(Seed, k + 850) });
            }
            return list;
        }

        protected override float Linger => 3f;

        public override bool Done(long nowUs)
        {
            if (Root == null || Gone) return true;
            if (IsHit && Now(nowUs) - HitAt < 2.6f) return false;
            return base.Done(nowUs);
        }
    }
}
