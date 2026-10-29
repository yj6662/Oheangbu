using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>묵 (earth, ㅜ ring, 남긴다) - free showcase batch 3, concept muk_a (a circle of rough standing stones - menhirs 1.5 - 2.5 m - stands on the 2 m ring round
    /// the caster, hot seams in their cracks, ink running down them; the bear on the ring shoved outward by the stone that rose under it). Unlike 무's rolling ridge
    /// (a one-time shove) it is a HENGE that stays (it blocks and can be climbed - the rule's). Beats, executor facts only: the launch = the stones tear up out of the
    /// band in a ripple from the front, dust jets, chunks -> they stand for the residue material's life (grit trickling, seams breathing, ink drips); a body the
    /// strike or the ring touched = a dust explosion and shove streaks on it (the shove is the executor's) -> the last 0.4 s = the stones grind and tremble -> natural
    /// expiry (CSV: 가라앉으며 둘레 땅이 판으로 들림) = they sink back while the earth outside the ring lifts as plates and settles.</summary>
    public sealed class Vol2FsMuk : Vol2FsLeave
    {
        static readonly Color DustC = new Color(0.55f, 0.42f, 0.34f, 1f), Soil = new Color(0.3f, 0.18f, 0.12f, 1f), Seam = new Color(1f, 0.5f, 0.18f), Ink = new Color(0.03f, 0.025f, 0.02f);
        ParticleSystem _chunks, _bits, _cloud, _jets, _speed, _clods; Light[] _lights;
        Material _stone, _stoneSeam, _rock;
        sealed class Stone { public Transform T; public Vector3 Base; public Quaternion Rot; public float H, At, Ang, Rnd; }
        readonly List<Stone> _stones = new List<Stone>();
        readonly List<(Transform T, Vector3 Base, Quaternion Rot)> _plates = new List<(Transform, Vector3, Quaternion)>();
        Mesh _crackMesh, _dripMesh; readonly Vol2FsStrips _crack = new Vol2FsStrips(), _drip = new Vol2FsStrips();
        bool _built;
        protected override float ExpireSeconds => 1.4f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _stone = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.92f, 0.8f, 0.68f), 0f, 0.1f);
            _stoneSeam = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.86f, 0.72f, 0.6f), 0f, 0.1f, Seam * 0.45f, "fs_vein");      // r3: lighter stone, faint seams (r2: black slabs with lava veins)      // r2: vein mask (r1: white stones)
            _rock = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.62f, 0.5f, 0.42f), 0f, 0.1f);
            var chunk = Kit.SharedMesh("chunk0", () => Vol2FsB1.Rock(23, 0.5f, 0.7f, 7, 5));
            _chunks = Vol2Fs.Build(new Vol2FsPs { Mat = _rock, Mesh = chunk, Life0 = 0.9f, Life1 = 1.6f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.08f, Size1 = 0.3f, Radius = 0.25f, Gravity = 2.2f, Drag = 0.3f, Spin = 500f, Max = 260 }, root, "Chunks");
            _bits = Vol2Fs.Build(new Vol2FsPs { Mat = _rock, Mesh = chunk, Life0 = 0.5f, Life1 = 1f, Speed0 = 0.1f, Speed1 = 0.6f, Size0 = 0.02f, Size1 = 0.06f, Radius = 0.1f, Gravity = 2.4f, Spin = 400f, Max = 300 }, root, "Grit");
            _cloud = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(DustC, 0.6f)), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 0.5f, Speed1 = 2.5f, Size0 = 1.2f, Size1 = 2.4f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.4f, Drag = 2f, Tiles = 2,
                SizeStart = 0.4f, SizePeak = 1.2f, SizePeakAt = 0.4f, SizeEnd = 1.6f, VelOverLife = new Vector3(0f, 0.3f, 0f), Max = 140, SortFudge = 10 }, root, "Cloud");
            _jets = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(DustC, 0.5f)), Life0 = 0.5f, Life1 = 0.9f, Speed0 = 2f, Speed1 = 4f, Size0 = 0.4f, Size1 = 0.8f, Shape = ParticleSystemShapeType.Cone, Angle = 18f, Radius = 0.25f, ShapeRot = new Vector3(-90f, 0f, 0f), Drag = 2f, Tiles = 2, SizeEnd = 1.8f, Max = 200, SortFudge = 8 }, root, "DustJets");
            var streak = M("fs_streak", DustC, 1f, 0f); streak.Opacity = 0.9f; streak.NoiseAmt = 0.4f;
            _speed = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(streak), Life0 = 0.3f, Life1 = 0.55f, Speed0 = 8f, Speed1 = 14f, Size0 = 0.2f, Size1 = 0.45f, Radius = 0.4f, Drag = 2f, Stretch = true, StretchVel = 0.09f, StretchLen = 2f, Max = 100 }, root, "Shove");
            var clod = M("fs_ink", Soil, 1f, 0f); clod.NoiseAmt = 0.4f; clod.Soft = 0.1f;
            _clods = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(clod), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.06f, Size1 = 0.2f, Radius = 0.3f, Gravity = 2.6f, Tiles = 2, AlphaHoldTo = 0.7f, Max = 400 }, root, "Clods");
            foreach (var ps in new[] { _chunks, _bits, _cloud, _jets, _speed, _clods }) ps.Play(false);
            var cr = M("fs_crack", new Color(0.08f, 0.05f, 0.04f, 1f), 1f, 0f); cr.Soft = 0.001f; cr.NoiseAmt = 0.4f; cr.Erode = 0.3f; cr.Opacity = 0.9f;
            Vol2Fs.MeshObject(root, "Cracks", Kit.Mat(cr), out _crackMesh);
            Vol2Fs.MeshObject(root, "InkDrips", Kit.InkSolid("fs_stroke2", Ink, new Color(0.4f, 0.25f, 0.12f, 1f), 0.06f, 0.45f, 0.3f, 1.4f), out _dripMesh);
            _lights = new Light[3];
            for (int k = 0; k < 3; k++) _lights[k] = Vol2Fs.PointLight(root, "SeamLight" + k, Seam, 4f);
        }

        protected override void OnCharge(float t, float k) { if (Time.frameCount % 3 == 0) Vol2FsB1.Emit(_bits, Tip() + Random.insideUnitSphere * 0.4f, 1); }
        protected override void OnLaunch(long nowUs) { }

        protected override void OnHit(long nowUs)
        {
            if (_built) return;
            _built = true;
            var root = Root.transform;
            int seed = Seed + 251;
            const int N = 10;
            for (int k = 0; k < N; k++)
            {
                float ang = Vol2FsB2.Angle(k, N, seed, 0.35f), back = Back(ang);
                float front = Front(ang);
                if (front < 0.15f) continue;                                       // r2: none on the back / far sides
                var mesh = Kit.SharedMesh("menhir" + (k % 4), () => Vol2FsB1.Slab(600 + k % 4, new Vector3(1f, 1f, 0.6f), 0.55f));
                var r = Vol2FsB1.Prop(root, "Menhir" + k, mesh, Vol2Fs.Hash(seed, k + 5) < 0.4f ? _stoneSeam : _stone);
                Vector3 outw = Dir(ang);
                var s = new Stone { T = r.transform, Base = OnBand(ang, R) + Vector3.down * 0.1f, H = Mathf.Lerp(1.5f, 2.5f, Vol2Fs.Hash(seed, k)) * Mathf.Lerp(0.35f, 1f, front), At = 0.05f + 0.12f * Mathf.Abs(ang) / Mathf.PI, Ang = ang, Rnd = Vol2Fs.Hash(seed, k + 10) };
                s.Rot = Quaternion.LookRotation(Vector3.Cross(Vector3.up, outw), Vector3.up) * Quaternion.Euler((Vol2Fs.Hash(seed, k + 20) - 0.5f) * 10f, 0f, (Vol2Fs.Hash(seed, k + 30) - 0.5f) * 12f);
                float w = Mathf.Lerp(0.6f, 0.9f, Vol2Fs.Hash(seed, k + 40));
                s.T.localScale = new Vector3(w, 0.001f, w * 0.8f);
                s.T.SetPositionAndRotation(s.Base, s.Rot);
                _stones.Add(s);
            }
            for (int k = 0; k < 3; k++) _lights[k].transform.position = OnBand(-1.1f + 1.1f * k, R) + Vector3.up * 1.2f;
        }

        void Shove(Struck s)
        {
            Vector3 c = CentreNow(s), away = FlatN(c - Apex);
            Vol2FsB1.Emit(_cloud, c, 3); Vol2FsB1.Emit(_chunks, c, 14); Vol2FsB1.Emit(_clods, c, 30);
            for (int k = 0; k < 12; k++) Vol2FsB1.EmitV(_speed, c + Random.insideUnitSphere * 0.4f, (away + Random.insideUnitSphere * 0.3f) * Random.Range(8f, 14f), -1f, -1f);
        }
        protected override void OnStruck(Struck s) { Shove(s); }
        protected override void OnTouch(Struck s) { if (!Hits.Exists(h => h.Id == s.Id && Mathf.Abs(h.At - s.At) < 0.1f)) Shove(s); }

        protected override void OnResidueEnd()
        {
            if (!NaturalEnd) { foreach (var s in _stones) Vol2FsB1.Emit(_chunks, s.Base + Vector3.up * s.H * 0.5f, 3); return; }
            var root = Root.transform;
            for (int k = 0; k < 14; k++)
            {
                float ang = Vol2FsB2.Angle(k, 14, Seed + 7, 0.3f);
                if (Back(ang) > 0.85f) continue;
                Vector3 o = Dir(ang);
                var r = Vol2FsB1.Prop(root, "Plate" + k, Kit.SharedMesh("plate" + (k % 3), () => Vol2FsB1.Slab(400 + k % 3, new Vector3(1f, 0.22f, 0.8f), 0.9f)), _rock);
                r.transform.localScale = new Vector3(Mathf.Lerp(0.7f, 1.1f, Vol2Fs.Hash(Seed, k + 70)), 1f, 0.8f);
                _plates.Add((r.transform, OnBand(ang, R + 0.9f) + Vector3.down * 0.2f, Quaternion.LookRotation(o, Vector3.up)));
            }
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            float warn = Warn(NowUs), ex = ExitK(t), pulse = DrawPulse(t);
            float noRes = Res == null && EndRel >= 0f ? Vol2Fs.Smooth(EndRel, EndRel + 0.8f, t) : 0f;
            float sink = NaturalEnd ? Vol2Fs.Smooth(0f, 0.85f, ex) : Vol2Fs.Smooth(0f, 0.4f, ex);
            _drip.Clear();
            foreach (var s in _stones)
            {
                float a = h - s.At;
                float g = a <= 0f ? 0f : Vol2Fs.EaseOutBack(a / 0.2f);
                if (a > 0f && a < 0.05f) { Vol2FsB1.Emit(_jets, s.Base, 2); Vol2FsB1.Emit(_chunks, s.Base + Vector3.up * 0.4f, 4); Vol2FsB1.Emit(_cloud, s.Base, 1); }
                float hh = s.H * g * (1f - noRes);
                Vector3 shake = Vector3.zero;
                if (warn > 0f) shake = new Vector3(Mathf.PerlinNoise(t * 31f, s.Rnd * 9f) - 0.5f, 0f, Mathf.PerlinNoise(s.Rnd * 7f, t * 29f) - 0.5f) * 0.07f * warn;
                if (pulse > 0f) shake += Vector3.up * 0.05f * pulse * Mathf.Sin(t * 60f + s.Rnd * 10f);
                s.T.position = s.Base + shake + Vector3.down * hh * sink * 1.05f;
                s.T.localScale = new Vector3(s.T.localScale.x, Mathf.Max(0.001f, hh), s.T.localScale.z);
                bool on = ex < 0.999f && hh >= 0.002f;
                if (s.T.gameObject.activeSelf != on) s.T.gameObject.SetActive(on);
                // ink running down the face toward the caster
                if (on && Back(s.Ang) < 0.5f && g > 0.8f)
                {
                    Vector3 face = -Dir(s.Ang) * 0.32f;
                    float run = Vol2Fs.Smooth(0.2f, 1.2f, a) * (1f - sink);
                    var pts = new[] { s.T.position + Vector3.up * hh * 0.9f + face, s.T.position + Vector3.up * hh * Mathf.Lerp(0.9f, 0.3f, run) + face };
                    _drip.Strip(pts, new[] { 0.09f, 0.05f }, null, Color.white, new[] { 0.9f, 0.7f }, s.Rnd, cam, 0f, 1f);
                }
            }
            _drip.Write(_dripMesh);
            if (!Exiting && h > 0.6f && _stones.Count > 0 && Time.frameCount % (warn > 0f ? 1 : 7) == 0) { var s = _stones[Random.Range(0, _stones.Count)]; Vol2FsB1.Emit(_bits, s.Base + Vector3.up * s.H, warn > 0f ? 2 : 1); }
            if (_plates.Count > 0)
            {
                float lift = Mathf.Sin(Mathf.Clamp01(ex / 0.85f) * Mathf.PI);
                for (int k = 0; k < _plates.Count; k++)
                {
                    var p = _plates[k]; Vector3 o = p.Rot * Vector3.forward;
                    p.T.SetPositionAndRotation(p.Base + Vector3.up * 0.18f * lift, Quaternion.AngleAxis(-26f * lift, Vector3.Cross(Vector3.up, o)) * p.Rot);
                    p.T.gameObject.SetActive(ex < 0.97f);
                }
                if (ex < 0.7f && Time.frameCount % 4 == 0) Vol2FsB1.Emit(_cloud, OnBand(Random.Range(-2f, 2f), R + 0.9f), 1);
            }
            _crack.Clear();
            float cl = Vol2Fs.Smooth(0f, 0.3f, h) * (1f - Vol2Fs.Smooth(0.6f, 1f, ex)) * (1f - noRes);
            if (cl > 0.01f) Vol2FsB2.GroundRing(_crack, Apex, F, Ground, Inner - 0.5f, Outer + 0.6f, 72, a => cl * Front(a), 0.29f, cam, 0.02f, 0f, 5f);
            _crack.Write(_crackMesh);
            for (int k = 0; k < _lights.Length; k++) _lights[k].intensity = 0.6f * Vol2Fs.Smooth(0.2f, 0.6f, h) * (1f - sink) * (1f - noRes) * (0.7f + 0.3f * Mathf.Sin(t * 2.3f + k)) * (1f + pulse);
        }
    }
}
