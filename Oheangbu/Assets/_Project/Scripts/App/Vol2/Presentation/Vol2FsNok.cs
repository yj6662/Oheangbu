using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>녹 (fire, ㅗ fan, 남긴다) - free showcase batch 3, concept nok_a (the whole fan is a field of standing fire: hundreds of flame stalks like burning reeds
    /// sway on smouldering black ground, taller at the far edge; the bears inside burn; ash and embers rise). Unlike 노's one-time fire breath it is a FIELD that stays
    /// and burns in place. Beats, executor facts only: the launch = the fire races out from the apex across the fan and the reeds of burning ink shoot up in its wake
    /// -> they burn and sway for the residue material's life (embers, smoke, the ground glowing under them); a body the strike or the field touched = it flares and
    /// burns for the 2 s heat (a mirror) -> the last 0.4 s = the flames gutter low -> natural expiry (CSV: 끝부터 타 재로 날림) = every reed burns down from its tip
    /// into rising ash, the ground cools to a scorch.</summary>
    public sealed class Vol2FsNok : Vol2FsLeave
    {
        static readonly Color Orange = new Color(1f, 0.45f, 0.1f), Hot = new Color(1f, 0.8f, 0.4f), Ink = new Color(0.035f, 0.025f, 0.02f);
        ParticleSystem _charge, _fire, _tall, _embers, _smoke, _ash; Light[] _lights; Light _chargeLight;
        Mesh _reedMesh, _bedMesh, _scorchMesh; readonly Vol2FsStrips _reedS = new Vol2FsStrips(), _bed = new Vol2FsStrips(), _scorch = new Vol2FsStrips();
        struct Reed { public float U, V, H, W, Ph, Rnd, At; public Vector3 P; }
        readonly List<Reed> _reeds = new List<Reed>();
        bool _built;
        protected override float ExpireSeconds => 1.5f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.8f, 0.5f)), Rate = 60f, Loop = true, Life0 = 0.18f, Life1 = 0.3f, Speed0 = -1.6f, Speed1 = -2.6f, Size0 = 0.12f, Size1 = 0.26f, Radius = 0.6f, World = false, Tiles = 2, Max = 80 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", Orange, 4f);
            _charge.transform.position = Tip(); _charge.Play(false);
            _fire = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.5f, 0.3f)), Life0 = 0.3f, Life1 = 0.6f, Speed0 = 0.1f, Speed1 = 0.6f, Size0 = 0.25f, Size1 = 0.55f, Radius = 0.08f, Tiles = 2,
                VelOverLife = new Vector3(0f, 2f, 0f), Noise = 0.9f, NoiseFreq = 1.3f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 0.3f, Max = 3000 }, root, "Fire");
            _tall = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.6f, 0.3f)), Life0 = 0.45f, Life1 = 0.8f, Speed0 = 0.5f, Speed1 = 2f, Size0 = 0.7f, Size1 = 1.3f, Radius = 0.15f, Tiles = 2,
                VelOverLife = new Vector3(0f, 2.8f, 0f), Noise = 1f, NoiseFreq = 0.9f, SizeStart = 0.4f, SizePeak = 1.2f, SizePeakAt = 0.35f, SizeEnd = 0.5f, Max = 500 }, root, "TallFire");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Hot, 5f, 1f)), Life0 = 0.8f, Life1 = 2f, Speed0 = 0.5f, Speed1 = 2.5f, Size0 = 0.025f, Size1 = 0.055f, Radius = 0.1f, Gravity = -0.25f,
                Noise = 1.4f, NoiseFreq = 1.6f, Stretch = true, StretchVel = 0.05f, Max = 2000 }, root, "Embers");
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Smoke(0.45f)), Life0 = 1.5f, Life1 = 2.5f, Speed0 = 0.2f, Speed1 = 0.8f, Size0 = 1f, Size1 = 1.8f, Radius = 0.3f, Drag = 0.8f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.3f, SizePeakAt = 0.5f, SizeEnd = 1.8f, VelOverLife = new Vector3(0f, 1f, 0f), Max = 220, SortFudge = 12 }, root, "Smoke");
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Ash()), Life0 = 1.2f, Life1 = 2.4f, Speed0 = 0.2f, Speed1 = 0.9f, Size0 = 0.035f, Size1 = 0.1f, Radius = 0.1f, VelOverLife = new Vector3(0f, 1.5f, 0f),
                Noise = 1.2f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.6f, Max = 1800 }, root, "Ash");
            foreach (var ps in new[] { _fire, _tall, _embers, _smoke, _ash }) ps.Play(false);
            Vol2Fs.MeshObject(root, "Reeds", Kit.InkSolid("fs_stroke2", Ink, new Color(2.8f, 0.9f, 0.15f, 1f), 0.18f, 0.5f, 0.3f, 1.4f), out _reedMesh);
            var bed = M("fs_scorch", Color.white, 3.2f, 0.85f); bed.Ramp = "fs_ramp_fire"; bed.RampScale = 1.1f; bed.NoiseAmt = 0.55f; bed.NoiseScale = 2f; bed.Soft = 0.001f; bed.Scroll = new Vector2(0.15f, 0.1f);
            Vol2Fs.MeshObject(root, "EmberBed", Kit.Mat(bed), out _bedMesh);
            var sc = M("fs_scorch", new Color(0.05f, 0.03f, 0.02f, 1f), 1f, 0f); sc.Soft = 0.001f; sc.NoiseAmt = 0.4f; sc.Erode = 0.3f; sc.Opacity = 0.9f; sc.Edge = new Color(1.6f, 0.45f, 0.06f, 1f); sc.EdgeWidth = 0.1f;
            Vol2Fs.MeshObject(root, "Scorch", Kit.Mat(sc), out _scorchMesh);
            _lights = new Light[3];
            for (int k = 0; k < 3; k++) _lights[k] = Vol2Fs.PointLight(root, "FieldLight" + k, Orange, 6f);
        }

        protected override void OnCharge(float t, float k) { _charge.transform.position = Tip(); _chargeLight.intensity = 4f * k; }
        protected override void OnLaunch(long nowUs) { _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f; }

        protected override void OnHit(long nowUs)
        {
            if (_built) return;
            _built = true;
            int seed = Seed + 131;
            for (int k = 0; k < 80; k++)
            {
                float u = Mathf.Lerp(0.18f, 1f, Mathf.Sqrt(Vol2Fs.Hash(seed, k))), v = (Vol2Fs.Hash(seed, k + 100) * 2f - 1f) * 0.95f;
                _reeds.Add(new Reed { U = u, V = v, P = Vol2FsB3.InFan(Apex, F, Depth, Half, u, v, Ground), H = Mathf.Lerp(0.7f, 2.3f, Vol2Fs.Hash(seed, k + 200)) * Mathf.Lerp(0.5f, 1f, u),
                    W = Mathf.Lerp(0.12f, 0.22f, Vol2Fs.Hash(seed, k + 300)), Ph = Vol2Fs.Hash(seed, k + 400) * 6.28f, Rnd = Vol2Fs.Hash(seed, k + 500), At = u * 0.3f + 0.04f * Vol2Fs.Hash(seed, k + 600) });
            }
            for (int k = 0; k < 3; k++) _lights[k].transform.position = Vol2FsB3.InFan(Apex, F, Depth, Half, 0.65f, (k - 1) * 0.6f, Ground) + Vector3.up * 0.9f;
        }

        protected override void OnStruck(Struck s) { Vol2FsB1.Emit(_tall, CentreNow(s), 6); Vol2FsB1.Emit(_embers, CentreNow(s), 80); }
        protected override void OnTouch(Struck s) { Vol2FsB1.Emit(_tall, CentreNow(s), 4); Vol2FsB1.Emit(_embers, CentreNow(s), 50); }
        protected override void OnResidueEnd() { if (!NaturalEnd) { foreach (var r in _reeds) if (r.Rnd < 0.3f) Vol2FsB1.Emit(_smoke, r.P + Vector3.up * 0.6f, 1); } }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            float warn = Warn(NowUs), ex = ExitK(t), pulse = DrawPulse(t);
            float noRes = Res == null && EndRel >= 0f ? Vol2Fs.Smooth(EndRel, EndRel + 0.8f, t) : 0f;
            float burnDown = NaturalEnd ? Vol2Fs.Smooth(0f, 0.85f, ex) : Vol2Fs.Smooth(0f, 0.35f, ex);
            float flame = (1f - burnDown) * (1f - noRes) * (1f - 0.55f * warn);
            _reedS.Clear();
            var pts = new Vector3[5]; var w = new float[5]; var al = new float[5];
            Vector3 side = Vector3.Cross(Vector3.up, F);
            for (int i = 0; i < _reeds.Count; i++)
            {
                var r = _reeds[i];
                float g = Vol2Fs.EaseOutBack(Mathf.Clamp01((h - r.At) / 0.3f));
                float hh = r.H * g * (1f - burnDown * (0.7f + 0.3f * r.Rnd)) * (1f - noRes);
                if (hh < 0.03f) continue;
                for (int k = 0; k < 5; k++)
                {
                    float u = k / 4f;
                    pts[k] = r.P + Vector3.up * hh * u + side * Mathf.Sin(t * 2.2f + r.Ph) * 0.12f * u * u + F * 0.05f * Mathf.Cos(t * 1.7f + r.Ph) * u;
                    w[k] = r.W * (1f - 0.75f * u); al[k] = 1f - 0.2f * u;
                }
                _reedS.Strip(pts, w, null, Color.white, al, r.Rnd, cam, 0f, 1f);
                Vector3 tip = pts[4];
                // flames off the tips (fewer in the warning), ash on the burn-down
                if (flame > 0.05f && (i + Time.frameCount) % (warn > 0f ? 5 : 2) == 0)
                    Vol2FsB1.EmitV(_fire, tip + Random.insideUnitSphere * 0.04f, Vector3.up * (0.6f + 0.5f * Random.value), (0.12f + 0.14f * Random.value) * (0.7f + 0.5f * r.U) * (1f + 0.4f * pulse) * flame, -1f);      // r2: small tongues (r1: one fireball over the fan)
                if (flame > 0.3f && r.Rnd < 0.06f && (i + Time.frameCount) % 8 == 0) Vol2FsB1.EmitV(_tall, tip, Vector3.up * 1.1f, (0.35f + 0.25f * Random.value) * flame, -1f);
                if (Exiting && ex < 0.95f && (i + Time.frameCount) % 3 == 0) Vol2FsB1.Emit(_ash, tip, NaturalEnd ? 2 : 1);
            }
            _reedS.Write(_reedMesh);
            if (flame > 0.05f && Time.frameCount % 2 == 0) Vol2FsB1.Emit(_embers, Vol2FsB3.InFan(Apex, F, Depth, Half, Random.Range(0.2f, 1f), Random.Range(-0.95f, 0.95f), Ground) + Vector3.up * 0.3f, 2);
            if (flame > 0.05f && Time.frameCount % 6 == 0) Vol2FsB1.Emit(_smoke, Vol2FsB3.InFan(Apex, F, Depth, Half, Random.Range(0.5f, 1f), Random.Range(-0.9f, 0.9f), Ground) + Vector3.up * 1.6f, 1);
            // the bodies burn for the 2 s heat of each touch (a mirror)
            foreach (var s in Hits) if (t - s.At < 2f && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_fire, CentreNow(s) + Random.insideUnitSphere * 0.35f, Vector3.up * 1.2f, 0.4f + 0.3f * Random.value, -1f);
            foreach (var s in Touches) if (t - s.At < 2f && Time.frameCount % 3 == 0) Vol2FsB1.EmitV(_fire, CentreNow(s) + Random.insideUnitSphere * 0.35f, Vector3.up * 1.2f, 0.4f + 0.3f * Random.value, -1f);
            // the glowing ember bed and the scorch it leaves (fan-shaped carpets)
            _bed.Clear(); _scorch.Clear();
            float spread = Vol2Fs.Smooth(0f, 0.3f, h);
            float bl = spread * flame;
            float sl = spread * (1f - Vol2Fs.Smooth(0.6f, 1f, ex)) * (1f - noRes);
            FanCarpet(_bed, spread, bl, 0.31f, 0.035f);
            FanCarpet(_scorch, spread, sl, 0.67f, 0.025f);
            _bed.Write(_bedMesh); _scorch.Write(_scorchMesh);
            float flick = 0.75f + 0.25f * Mathf.PerlinNoise(t * 9f, 1.3f);
            for (int k = 0; k < _lights.Length; k++) _lights[k].intensity = (h < 0.3f ? 8f * h / 0.3f : 5f) * flame * flick * (1f + 0.5f * pulse) + (Exiting && NaturalEnd ? 2f * (1f - ex) : 0f);
        }

        void FanCarpet(Vol2FsStrips s, float reach, float life, float rnd, float lift)
        {
            if (life <= 0.01f) return;
            var pts = new List<Vector3>(); var w = new List<float>(); var al = new List<float>(); var side = new List<Vector3>();
            for (int k = 0; k <= 14; k++) { float v = (k / 14f) * 2f - 1f; Vector3 d = Dir(v * Half * 1.05f); var p = Apex + d * Depth * reach * 0.63f; p.y = Ground + lift; pts.Add(p); w.Add(Depth * reach * 0.76f); /* r3: from 1/4 out */ al.Add(life); side.Add(d); }
            s.Strip(pts, w, side, Color.white, al, rnd, CamPos, 0f, 3f);
        }
    }
}
