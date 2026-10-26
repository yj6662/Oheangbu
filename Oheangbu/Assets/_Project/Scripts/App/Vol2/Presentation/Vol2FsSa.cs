using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>사 (metal, ㅏ path, 한 번 · 벤다) - free showcase batch 1, concept sa_b (a thrown sword: light lines converge on the brush and a straight steel sword
    /// forms there, it is thrown point first leaving a razor-thin white-gold streak from the brush to the first bear, pierces it - an X of white light opens on
    /// the body, star rays, sparks - and the sword snaps with a ring into glittering shards flung SIDEWAYS with cut threads of ink). Cold, fast, sharp.</summary>
    public sealed class Vol2FsSa : Vol2FsShot
    {
        static readonly Color White = new Color(1f, 0.97f, 0.9f), Gold = new Color(1f, 0.85f, 0.55f), Ink = new Color(0.03f, 0.03f, 0.035f);

        Transform _sword; MeshRenderer _swordR; ParticleSystem _lines, _sparks, _shards, _glints, _flash; Light _chargeLight, _hitLight;
        Mesh _streakMesh, _haloMesh; readonly Vol2FsStrips _streakS = new Vol2FsStrips(), _haloS = new Vol2FsStrips();
        Mesh _xMesh; readonly Vol2FsStrips _xS = new Vol2FsStrips();
        Mesh _threadMesh; readonly Vol2FsStrips _threadS = new Vol2FsStrips();
        Mesh _edgeMesh; readonly Vol2FsStrips _edgeS = new Vol2FsStrips();
        struct Ray { public Vector3 Dir; public float Len, Delay, Width; }
        readonly List<Ray> _rays = new List<Ray>();
        readonly List<Vol2FsB1.Stroke> _threads = new List<Vol2FsB1.Stroke>();
        Vector3 _fwd, _side; float _snapAt = 0.12f; bool _snapped;

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsB1.M(tex, tint, intensity, addMix);
        protected override float LobHeight => 0.12f;
        const float SW = 1.6f;      // r2: the sword at 1.6x (2.5 m) - the r1 sword (1.55 m) did not read from behind the shoulder
        Vector3 Hold() => Tip() + Fwd * 0.35f + Vector3.up * 0.15f;

        protected override void OnBegin()
        {
            var root = Root.transform;
            var steel = Vol2FsB1.Lit(Kit, "fs_steel", new Color(0.9f, 0.92f, 0.95f), 1f, 0.92f, new Color(0.35f, 0.33f, 0.3f), null);
            _swordR = Vol2FsB1.Prop(root, "Sword", Kit.SharedMesh("sword", () => Vol2FsB1.Sword()), steel, false); _sword = _swordR.transform;
            _sword.position = Hold(); _sword.localScale = new Vector3(SW, SW, 0.01f);
            // light lines converging on the brush tip
            var line = M("fs_streak", White, 4f, 1f); line.NoiseAmt = 0.2f;
            _lines = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(line), Rate = 60f, Loop = true, Life0 = 0.12f, Life1 = 0.2f, Speed0 = -9f, Speed1 = -14f, Size0 = 0.025f, Size1 = 0.045f, Radius = 1.3f, Stretch = true, StretchVel = 0.03f, StretchLen = 1f, World = false, Max = 60 }, root, "Lines");
            _lines.transform.position = Hold();
            _chargeLight = Vol2Fs.PointLight(_lines.transform, "ChargeLight", White, 3.5f);
            _lines.Play(false);
            // the glowing edge along the blade (a thin streak strip, drawn every frame along the sword)
            var edge = M("fs_streak", White, 5f, 1f); edge.NoiseAmt = 0.1f;
            Vol2Fs.MeshObject(root, "Edge", Kit.Mat(edge), out _edgeMesh);
            // shared pools
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 6f, 1f)), Life0 = 0.25f, Life1 = 0.7f, Speed0 = 6f, Speed1 = 18f, Size0 = 0.025f, Size1 = 0.06f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.15f,
                Gravity = 1f, Drag = 1.6f, Stretch = true, StretchVel = 0.035f, Max = 600 }, root, "Sparks");
            var shardMat = Vol2FsB1.Lit(Kit, "fs_steel", new Color(1f, 1f, 1f), 1f, 0.95f, new Color(1.2f, 1.15f, 1.05f), null);
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = shardMat, Mesh = Kit.SharedMesh("shard0", () => Vol2FsB1.Shard(5)), Life0 = 0.8f, Life1 = 1.4f, Speed0 = 5f, Speed1 = 12f, Size0 = 0.12f, Size1 = 0.32f, Radius = 0.2f, Gravity = 1.2f, Drag = 0.8f, Spin = 900f, Max = 80 }, root, "Shards");
            _glints = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", White, 7f, 1f)), Life0 = 0.08f, Life1 = 0.16f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.25f, Size1 = 0.5f, Radius = 0.01f, Max = 200, SortFudge = -10 }, root, "Glints");
            _flash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", White, 6f, 1f)), Life0 = 0.12f, Life1 = 0.16f, Speed0 = 0f, Speed1 = 0f, Size0 = 1.4f, Size1 = 1.8f, Radius = 0.01f, SizeStart = 0.3f, SizePeak = 1f, SizePeakAt = 0.2f, SizeEnd = 0.8f, Max = 6, SortFudge = -30 }, root, "Flash");
            foreach (var ps in new[] { _sparks, _shards, _glints, _flash }) ps.Play(false);
        }

        void PoseSword(Vector3 tipPos, Vector3 dir, float spin)
        {
            // the sword mesh points along +Z from its grip; place it so its POINT is at tipPos
            var rot = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.AngleAxis(spin, Vector3.forward);
            _sword.rotation = rot;
            _sword.position = tipPos - dir * 1.55f * _sword.localScale.z;
        }

        void DrawEdge(float glow)
        {
            _edgeS.Clear();
            if (_swordR.enabled && glow > 0.01f)
            {
                Vector3 d = _sword.forward; float L = 1.55f * _sword.localScale.z;
                var pts = new[] { _sword.position + d * 0.4f * _sword.localScale.z, _sword.position + d * L * 0.7f, _sword.position + d * L };
                _edgeS.Strip(pts, new[] { 0.06f, 0.11f, 0.05f }, null, Color.white, new[] { glow, glow, glow }, 0.2f, CamPos, 0f, 1f);
            }
            _edgeS.Write(_edgeMesh);
        }

        protected override void OnCharge(float t, float k)
        {
            _lines.transform.position = Hold();
            _chargeLight.intensity = 3.5f * k;
            // the sword forms point-up over the shoulder, growing out of the light
            float s = Vol2Fs.Smooth(0.15f, 1f, k);
            _sword.localScale = new Vector3(SW, SW, Mathf.Max(0.01f, s) * SW);
            Vector3 dir = Vector3.Slerp(Vector3.up, Fwd, Vol2Fs.Smooth(0.6f, 1f, k));
            PoseSword(Hold() + dir * 1.2f * s, dir, t * 200f);
            DrawEdge(0.4f + 0.6f * k);
            if (Time.frameCount % 3 == 0) Vol2FsB1.Emit(_glints, _sword.position + _sword.forward * Random.Range(0.4f, 1.5f) * s, 1);
        }

        protected override void OnLaunch(long nowUs)
        {
            var root = Root.transform;
            _lines.Stop(false, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f;
            _sword.localScale = Vector3.one * SW;
            _fwd = Flat(HitPoint - From); _side = Vector3.Cross(Vector3.up, _fwd);
            var st = M("fs_streak", Gold, 6f, 1f); st.NoiseAmt = 0.15f;
            Vol2Fs.MeshObject(root, "Streak", Kit.Mat(st), out _streakMesh);
            var halo = M("fs_streak", new Color(1f, 0.8f, 0.5f), 1.6f, 1f); halo.NoiseAmt = 0.4f; halo.Scroll = new Vector2(-3f, 0f);
            Vol2Fs.MeshObject(root, "StreakHalo", Kit.Mat(halo), out _haloMesh);
            Vol2FsB1.Emit(_sparks, From, 20);
        }

        void DrawStreak(float f, float since)
        {
            _streakS.Clear(); _haloS.Clear();
            float life = 1f - Vol2Fs.Smooth(0.15f, 0.9f, since);
            if (f > 0.01f && life > 0.01f)
            {
                const int N = 16; var p = new Vector3[N]; var w = new float[N]; var a = new float[N]; var hw = new float[N];
                float thin = 1f - 0.6f * Vol2Fs.Smooth(0f, 0.6f, since);
                for (int k = 0; k < N; k++) { float u = k / (N - 1f); p[k] = Line(f * u); w[k] = 0.09f * thin; a[k] = life * Mathf.Clamp01(0.3f + u); hw[k] = 0.5f * thin; }
                _streakS.Strip(p, w, null, Color.white, a, 0.1f, CamPos, 0f, 1f);
                _haloS.Strip(p, hw, null, Color.white, a, 0.6f, CamPos, 0f, 1f);
            }
            _streakS.Write(_streakMesh); _haloS.Write(_haloMesh);
        }

        protected override void OnFly(float t, float f, float prev)
        {
            Vector3 p = Line(f), d = (Line(Mathf.Min(1f, f + 0.02f)) - Line(Mathf.Max(0f, f - 0.02f))).normalized;
            PoseSword(p, d, t * 1400f);
            DrawEdge(1f);
            DrawStreak(f, 0f);
            float len = Vector3.Distance(From, HitPoint);
            int n = Mathf.CeilToInt((f - prev) * len * 3f);
            for (int k = 0; k < n; k++) { var q = Line(Mathf.Lerp(prev, f, (k + 0.5f) / n)); Vol2FsB1.Emit(_sparks, q, 2); Vol2FsB1.Emit(_glints, q, 1); }
        }

        protected override void OnHit(long nowUs)
        {
            var root = Root.transform;
            // the sword runs in (its point a little past the hit) and stays an instant before it snaps
            PoseSword(HitPoint + _fwd * 0.5f, (HitPoint - From).normalized, 0f);
            Vol2FsB1.EmitV(_flash, HitPoint, Vector3.zero);
            Vector3 cam = CamPos, toCam = (cam - HitPoint).normalized;
            Vector3 sx = Vector3.Cross(toCam, Vector3.up).normalized, sy = Vector3.Cross(sx, toCam).normalized;
            for (int k = 0; k < 12; k++)
            {
                float a = (k / 12f + 0.04f * Vol2Fs.Hash(Seed, k)) * Mathf.PI * 2f;
                _rays.Add(new Ray { Dir = (sx * Mathf.Cos(a) * 1.4f + sy * Mathf.Sin(a)).normalized, Len = Mathf.Lerp(1.4f, 3.4f, Vol2Fs.Hash(Seed, k + 30)), Delay = 0.015f * Vol2Fs.Hash(Seed, k + 60), Width = Mathf.Lerp(0.03f, 0.07f, Vol2Fs.Hash(Seed, k + 90)) });
            }
            var x = M("fs_streak", White, 7f, 1f); x.NoiseAmt = 0.1f;
            Vol2Fs.MeshObject(root, "SlashX", Kit.Mat(x), out _xMesh);
            Vol2Fs.MeshObject(root, "InkThreads", Kit.InkSolid("fs_stroke2", Ink, new Color(1.4f, 1.3f, 1.1f, 1f), 0.08f, 0.5f, 0.3f, 2f), out _threadMesh);
            for (int k = 0; k < 10; k++)
            {
                float s = k % 2 == 0 ? 1f : -1f;
                Vector3 d = (_side * s * (1f + Vol2Fs.Hash(Seed, k + 120)) + Vector3.up * (Vol2Fs.Hash(Seed, k + 140) - 0.35f) * 1.2f + _fwd * 0.3f * Vol2Fs.Hash(Seed, k + 150)).normalized;
                _threads.Add(new Vol2FsB1.Stroke { Dir = d, Len = Mathf.Lerp(1.8f, 3.6f, Vol2Fs.Hash(Seed, k + 160)), Width = Mathf.Lerp(0.07f, 0.14f, Vol2Fs.Hash(Seed, k + 180)), Delay = 0.02f * Vol2Fs.Hash(Seed, k + 200), Life = 0.7f + 0.4f * Vol2Fs.Hash(Seed, k + 220), Rnd = Vol2Fs.Hash(Seed, k + 240) });
            }
            for (int k = 0; k < 160; k++)
            {
                float s = Vol2Fs.Hash(Seed, k + 400) < 0.5f ? 1f : -1f;
                Vector3 d = (_side * s * (0.6f + Vol2Fs.Hash(Seed, k + 420)) + Vector3.up * (Vol2Fs.Hash(Seed, k + 440) - 0.3f) + _fwd * (Vol2Fs.Hash(Seed, k + 460) - 0.4f) * 0.8f).normalized;
                Vol2FsB1.EmitV(_sparks, HitPoint, d * Mathf.Lerp(7f, 20f, Vol2Fs.Hash(Seed, k + 480)));
            }
            _hitLight = Vol2Fs.PointLight(root, "HitLight", new Color(0.9f, 0.95f, 1f), 9f); _hitLight.transform.position = HitPoint - _fwd * 0.6f + Vector3.up * 0.3f;
        }

        void Snap()
        {
            _snapped = true; _swordR.enabled = false;
            Vector3 c = _sword.position + _sword.forward * 0.8f * SW;
            for (int k = 0; k < 36; k++)
            {
                float s = k % 2 == 0 ? 1f : -1f;
                Vector3 p = c + _sword.forward * Mathf.Lerp(-0.7f, 0.7f, Vol2Fs.Hash(Seed, k + 600));
                Vector3 d = (_side * s * (0.8f + 0.6f * Vol2Fs.Hash(Seed, k + 620)) + Vector3.up * (Vol2Fs.Hash(Seed, k + 640) * 0.9f - 0.2f) + _fwd * (Vol2Fs.Hash(Seed, k + 660) - 0.5f) * 0.4f).normalized;
                Vol2FsB1.EmitV(_shards, p, d * Mathf.Lerp(5f, 13f, Vol2Fs.Hash(Seed, k + 680)), Mathf.Lerp(0.1f, 0.34f, Vol2Fs.Hash(Seed, k + 700)), Mathf.Lerp(0.8f, 1.4f, Vol2Fs.Hash(Seed, k + 720)));
                Vol2FsB1.Emit(_glints, p, 1);
            }
            Vol2FsB1.EmitV(_flash, c, Vector3.zero);
        }

        protected override void OnAfter(float t, float h, bool ended)
        {
            var cam = CamPos;
            if (!_snapped && h >= _snapAt) Snap();
            _hitLight.intensity = h < 0.03f ? 14f * h / 0.03f : 14f * Mathf.Exp(-(h - 0.03f) * 7f);
            DrawEdge(_snapped ? 0f : 1f);
            DrawStreak(1f, h);
            // the X of light on the body + the star rays (from the struck point, facing the camera)
            _xS.Clear();
            Vector3 c = HitPoint, toCam = (cam - c).normalized;
            Vector3 sx = Vector3.Cross(toCam, Vector3.up).normalized, sy = Vector3.Cross(sx, toCam).normalized;
            float open = Vol2Fs.Smooth(0f, 0.06f, h), xl = 1f - Vol2Fs.Smooth(0.12f, 0.55f, h);
            if (xl > 0.01f)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 d = (sx * 1f + sy * 0.85f * s).normalized;
                    float L = 1.9f * open;
                    _xS.Strip(new[] { c - d * L, c, c + d * L }, new[] { 0.05f, 0.16f * xl, 0.05f }, null, Color.white, new[] { xl, xl, xl }, 0.3f + s * 0.1f, cam, 0f, 1f);
                }
                foreach (var r in _rays)
                {
                    float a = h - r.Delay; if (a <= 0f) continue;
                    float g = Vol2Fs.Smooth(0f, 0.08f, a), rl = 1f - Vol2Fs.Smooth(0.08f, 0.4f, a);
                    if (rl <= 0.01f) continue;
                    _xS.Strip(new[] { c + r.Dir * 0.2f, c + r.Dir * (0.2f + r.Len * g) }, new[] { r.Width * rl, 0.005f }, null, Color.white, new[] { rl, rl }, 0.5f, cam, 0f, 1f);
                }
            }
            _xS.Write(_xMesh);
            _threadS.Clear(); Vol2FsB1.DrawBurst(_threadS, _threads, c, h, cam, 0.25f, 0.08f); _threadS.Write(_threadMesh);
        }

        protected override float Linger => 2.5f;
    }
}
