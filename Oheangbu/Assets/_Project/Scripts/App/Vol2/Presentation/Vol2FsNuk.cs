using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>눅 (fire, ㅜ ring, 남긴다) - free showcase batch 3, concept nuk_a (a circle of standing pillars of fire - burning ink posts like tall torches - stands on the
    /// 2 m ring round the caster, linked low by a glowing line of embers; a body crossing the ring is scorched; smoke and embers rise from each pillar). Unlike 누's
    /// wall of flame (a one-time strike) it is a ring of TORCHES that stays. Beats, executor facts only: the launch = a white-hot line runs round the band and the
    /// coal posts stab up out of it and catch -> they burn for the residue material's life (flame columns, embers, a smouldering ring line); a body the strike or the
    /// ring touched = a flare on it and its 2 s burn (a mirror) -> the last 0.4 s = the flames gutter -> natural expiry (CSV: 끝부터 타 재로 날림) = every post
    /// burns down from its top into rising ash, the ring line cools to a scorch.</summary>
    public sealed class Vol2FsNuk : Vol2FsLeave
    {
        static readonly Color Orange = new Color(1f, 0.45f, 0.1f), Hot = new Color(1f, 0.8f, 0.4f), Ink = new Color(0.035f, 0.025f, 0.02f);
        ParticleSystem _charge, _fire, _tall, _embers, _smoke, _ash; Light _chargeLight; Light[] _lights;
        readonly Vol2FsTubes _posts = new Vol2FsTubes();
        Mesh _postMesh, _lineMesh, _scorchMesh, _plumeMesh; readonly Vol2FsStrips _line = new Vol2FsStrips(), _scorch = new Vol2FsStrips(), _plume = new Vol2FsStrips();
        sealed class Post { public float Ang, H, At, Back, Rnd; public Vector3 Base; }
        readonly List<Post> _list = new List<Post>();
        bool _built;
        protected override float ExpireSeconds => 1.4f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.8f, 0.5f)), Rate = 60f, Loop = true, Life0 = 0.18f, Life1 = 0.3f, Speed0 = -1.6f, Speed1 = -2.6f, Size0 = 0.12f, Size1 = 0.26f, Radius = 0.6f, World = false, Tiles = 2, Max = 80 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", Orange, 4f);
            _charge.transform.position = Tip(); _charge.Play(false);
            _fire = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.5f, 0.3f)), Life0 = 0.3f, Life1 = 0.6f, Speed0 = 0.1f, Speed1 = 0.6f, Size0 = 0.3f, Size1 = 0.6f, Radius = 0.08f, Tiles = 2,
                VelOverLife = new Vector3(0f, 2f, 0f), Noise = 0.9f, NoiseFreq = 1.3f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 0.3f, Max = 3000 }, root, "Fire");
            _tall = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.65f, 0.3f)), Life0 = 0.5f, Life1 = 0.9f, Speed0 = 0.5f, Speed1 = 2f, Size0 = 0.8f, Size1 = 1.4f, Radius = 0.15f, Tiles = 2,
                VelOverLife = new Vector3(0f, 3f, 0f), Noise = 1f, NoiseFreq = 0.9f, SizeStart = 0.4f, SizePeak = 1.2f, SizePeakAt = 0.35f, SizeEnd = 0.5f, Max = 700 }, root, "TallFire");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Hot, 5f, 1f)), Life0 = 0.8f, Life1 = 2f, Speed0 = 0.5f, Speed1 = 2.5f, Size0 = 0.025f, Size1 = 0.055f, Radius = 0.1f, Gravity = -0.25f,
                Noise = 1.4f, NoiseFreq = 1.6f, Stretch = true, StretchVel = 0.05f, Max = 2000 }, root, "Embers");
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Smoke(0.5f)), Life0 = 1.5f, Life1 = 2.5f, Speed0 = 0.2f, Speed1 = 0.8f, Size0 = 0.9f, Size1 = 1.6f, Radius = 0.2f, Drag = 0.8f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.3f, SizePeakAt = 0.5f, SizeEnd = 1.8f, VelOverLife = new Vector3(0f, 1.1f, 0f), Max = 260, SortFudge = 12 }, root, "Smoke");
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Ash()), Life0 = 1.2f, Life1 = 2.4f, Speed0 = 0.2f, Speed1 = 0.9f, Size0 = 0.035f, Size1 = 0.1f, Radius = 0.1f, VelOverLife = new Vector3(0f, 1.5f, 0f),
                Noise = 1.2f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.6f, Max = 1600 }, root, "Ash");
            foreach (var ps in new[] { _fire, _tall, _embers, _smoke, _ash }) ps.Play(false);
            var post = Vol2Fs.MeshObject(root, "Posts", Kit.Bark(new Color(0.14f, 0.1f, 0.08f), new Color(3.2f, 1.1f, 0.2f), 0.1f, "fs_bark", "fs_vein"), out _postMesh); post.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var line = M("fs_ring", Color.white, 2.6f, 0.9f); line.Ramp = "fs_ramp_fire"; line.RampScale = 1.2f; line.NoiseAmt = 0.55f; line.NoiseScale = 2f; line.Soft = 0.001f; line.Scroll = new Vector2(0.4f, 0f);
            Vol2Fs.MeshObject(root, "EmberLine", Kit.Mat(line), out _lineMesh);
            var sc = M("fs_stroke2", new Color(0.03f, 0.02f, 0.015f, 1f), 1f, 0f); sc.Soft = 0.001f; sc.NoiseAmt = 0.55f; sc.Erode = 0.3f; sc.Opacity = 0.85f; sc.Edge = new Color(1.8f, 0.45f, 0.06f, 1f); sc.EdgeWidth = 0.1f;
            Vol2Fs.MeshObject(root, "Scorch", Kit.Mat(sc), out _scorchMesh);
            Vol2Fs.MeshObject(root, "InkPlumes", Kit.InkSolid("fs_stroke2", Ink, new Color(2.4f, 0.75f, 0.12f, 1f), 0.07f, 0.45f, 0.26f, 1.5f), out _plumeMesh);
            _lights = new Light[4];
            for (int k = 0; k < 4; k++) _lights[k] = Vol2Fs.PointLight(root, "RingLight" + k, Orange, 6f);
        }

        protected override void OnCharge(float t, float k) { _charge.transform.position = Tip(); _chargeLight.intensity = 4f * k; }
        protected override void OnLaunch(long nowUs) { _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f; }

        protected override void OnHit(long nowUs)
        {
            if (_built) return;
            _built = true;
            int seed = Seed + 231;
            const int N = 9;
            for (int k = 0; k < N; k++)
            {
                float ang = Vol2FsB2.Angle(k, N, seed, 0.3f), back = Back(ang);
                float front = Front(ang);
                if (front < 0.15f) continue;                                       // r2: none on the back / far sides (the camera sits inside the ring)
                var p = new Post { Ang = ang, Back = 1f - front, H = Mathf.Lerp(1.4f, 2.1f, Vol2Fs.Hash(seed, k)) * Mathf.Lerp(0.35f, 1f, front), At = 0.08f + 0.1f * Mathf.Abs(ang) / Mathf.PI, Rnd = Vol2Fs.Hash(seed, k + 30), Base = OnBand(ang, R) };
                Vector3 side = Vector3.Cross(Vector3.up, Dir(ang));
                var tb = _posts.Add(new[] { p.Base + Vector3.down * 0.15f, p.Base + Vector3.up * p.H * 0.5f + side * 0.06f * (p.Rnd - 0.5f), p.Base + Vector3.up * p.H }, 0.13f, 0.07f, 12); tb.Knots = 0.5f; tb.Sharp = 0.12f; tb.Sides = 7;
                _list.Add(p);
            }
            for (int k = 0; k < 4; k++) _lights[k].transform.position = OnBand(-1.5f + k, R) + Vector3.up * 1.4f;
        }

        protected override void OnStruck(Struck s) { Vol2FsB1.Emit(_tall, CentreNow(s), 6); Vol2FsB1.Emit(_embers, CentreNow(s), 90); }
        protected override void OnTouch(Struck s) { Vol2FsB1.Emit(_tall, CentreNow(s), 4); Vol2FsB1.Emit(_embers, CentreNow(s), 60); }
        protected override void OnResidueEnd() { if (!NaturalEnd) foreach (var p in _list) Vol2FsB1.Emit(_smoke, p.Base + Vector3.up * p.H, 1); }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            float warn = Warn(NowUs), ex = ExitK(t), pulse = DrawPulse(t);
            float noRes = Res == null && EndRel >= 0f ? Vol2Fs.Smooth(EndRel, EndRel + 0.8f, t) : 0f;
            float burnDown = NaturalEnd ? Vol2Fs.Smooth(0f, 0.9f, ex) : Vol2Fs.Smooth(0f, 0.35f, ex);
            float flame = (1f - burnDown) * (1f - noRes) * (1f - 0.55f * warn);
            for (int k = 0; k < _list.Count; k++)
            {
                var p = _list[k];
                float g = Vol2Fs.EaseOutBack(Mathf.Clamp01((h - p.At) / 0.25f));
                float top = g * (1f - burnDown * (0.75f + 0.25f * p.Rnd)) * (1f - noRes);
                _posts.Tubes[k].Grow = top;
                if (top < 0.05f) continue;
                Vector3 tip = p.Base + Vector3.up * p.H * top;
                if (flame > 0.05f)
                {
                    // the flame column on the post's top (a torch) and tongues licking down its length
                    if ((k + Time.frameCount) % 3 == 0) Vol2FsB1.EmitV(_tall, tip + Random.insideUnitSphere * 0.06f, Vector3.up * (1.1f + Random.value), (0.4f + 0.25f * Random.value) * (1f - 0.5f * p.Back) * flame * (1f + 0.4f * pulse), -1f);      // r2: a torch flame, not a fireball
                    Vol2FsB1.EmitV(_fire, p.Base + Vector3.up * p.H * top * Random.value + Random.insideUnitSphere * 0.08f, Vector3.up * 1.1f, (0.14f + 0.14f * Random.value) * flame, -1f);
                    Vol2FsB1.EmitV(_fire, tip + Random.insideUnitSphere * 0.08f, Vector3.up * 1.2f, (0.2f + 0.16f * Random.value) * flame, -1f);
                    if ((k + Time.frameCount) % 3 == 0) Vol2FsB1.Emit(_embers, tip, 1);
                    if ((k + Time.frameCount) % 14 == 0 && p.Back < 0.5f) Vol2FsB1.Emit(_smoke, tip + Vector3.up * 0.8f, 1);
                }
                if (Exiting && ex < 0.95f && (k + Time.frameCount) % 2 == 0) { Vol2FsB1.Emit(_ash, tip, NaturalEnd ? 3 : 1); if (NaturalEnd) Vol2FsB1.Emit(_embers, tip, 1); }
            }
            _posts.Write(_postMesh);
            // the ember line round the band (flames along it, sparser), and the scorch it leaves
            if (flame > 0.05f && Time.frameCount % 2 == 0) { float a = Random.Range(-Mathf.PI, Mathf.PI); if (Back(a) < 0.7f || Random.value < 0.3f) Vol2FsB1.EmitV(_fire, OnBand(a, R) + Vector3.up * 0.05f, Vector3.up * 0.8f, 0.3f * flame, -1f); }
            _line.Clear(); _scorch.Clear(); _plume.Clear();
            float run = Vol2Fs.Smooth(0f, 0.12f, h);
            float ll = run * flame;
            float sl = run * (1f - Vol2Fs.Smooth(0.6f, 1f, ex)) * (1f - noRes);
            if (ll > 0.01f) Vol2FsB2.GroundRing(_line, Apex, F, Ground, R - 0.18f, R + 0.18f, 72, a => ll * (Mathf.Abs(a) <= run * Mathf.PI ? 1f : 0f) * (0.25f + 0.75f * Front(a)), 0.11f, cam, 0.04f, 0f, 5f);
            if (sl > 0.01f) Vol2FsB2.GroundRing(_scorch, Apex, F, Ground, Inner - 0.25f, Outer + 0.3f, 72, a => sl, 0.71f, cam, 0.025f, 0f, 4f);
            // a few twisting ink plumes off the front posts
            foreach (var p in _list)
            {
                if (p.Back > 0.3f || p.Rnd > 0.55f) continue;
                float s = h - p.At - 0.2f; if (s <= 0f || flame < 0.05f) continue;
                Vector3 b = p.Base + Vector3.up * p.H * 0.9f, side = Vector3.Cross(Vector3.up, Dir(p.Ang));
                Vol2FsB3.Plume(_plume, b, Vector3.up, side, 1.6f * Vol2Fs.Smooth(0f, 0.4f, s), 0.32f, 0.35f, t * 2f + p.Rnd * 9f, 0.75f * flame, p.Rnd, cam, 8, 0.5f);
            }
            _line.Write(_lineMesh); _scorch.Write(_scorchMesh); _plume.Write(_plumeMesh);
            // bodies burn for the 2 s of each touch (a mirror)
            foreach (var s in Hits) if (t - s.At < 2f && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_fire, CentreNow(s) + Random.insideUnitSphere * 0.35f, Vector3.up * 1.2f, 0.4f + 0.3f * Random.value, -1f);
            foreach (var s in Touches) if (t - s.At < 2f && Time.frameCount % 3 == 0) Vol2FsB1.EmitV(_fire, CentreNow(s) + Random.insideUnitSphere * 0.35f, Vector3.up * 1.2f, 0.4f + 0.3f * Random.value, -1f);
            float flick = 0.75f + 0.25f * Mathf.PerlinNoise(t * 9f, 0.7f);
            for (int k = 0; k < _lights.Length; k++) _lights[k].intensity = 5f * flame * run * flick * (1f + 0.5f * pulse) + (Exiting && NaturalEnd ? 1.5f * (1f - ex) : 0f);
        }
    }
}
