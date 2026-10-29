using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>숙 (metal, ㅜ ring, 남긴다) - free showcase batch 3, concept suk_b (a circle of swords planted point-down round the caster, their blades humming with
    /// white-gold light, a bright ring of light on the ground connecting them; the bear on the ring is cut). Unlike 수's spinning saw of knives (one strike) and 속's
    /// stake field it is a ring of PLANTED SWORDS that stays; the taut blade-lines between neighbouring hilts are the CSV's 날선 (and to any other metal residue
    /// within 3 m, read from the registry). Beats, executor facts only: the launch = the swords drop out of the air point-first round the band and stab in, the light
    /// ring runs round -> they stand humming for the residue material's life; a body the strike or the ring touched = white cut lines and sparks -> the last 0.4 s =
    /// the light flickers, the blades shiver -> natural expiry (CSV: 쨍하게 끊기며 파편이 옆으로 튐) = the blades snap with a ring and the shards fly SIDEWAYS.</summary>
    public sealed class Vol2FsSuk : Vol2FsLeave
    {
        static readonly Color White = new Color(1f, 0.97f, 0.9f), Gold = new Color(1f, 0.85f, 0.55f), Ink = new Color(0.03f, 0.03f, 0.035f);
        ParticleSystem _lines, _sparks, _shards, _glints, _flash; Light _chargeLight; Light[] _lights;
        Material _steel, _steelHot;
        sealed class Sw { public Transform T; public Vector3 Foot; public float Ang, At, Rnd, Back, Tilt; public bool Landed; }
        readonly List<Sw> _sw = new List<Sw>();
        Mesh _ringMesh, _wireMesh, _slashMesh, _glowMesh; readonly Vol2FsStrips _ringS = new Vol2FsStrips(), _wireS = new Vol2FsStrips(), _slashS = new Vol2FsStrips(), _glowS = new Vol2FsStrips();
        readonly List<(Vector3 C, Vector3 Side, float T)> _cuts = new List<(Vector3, Vector3, float)>();
        bool _built, _snapped;
        const float SwordScale = 1.55f, SwordLen = 1.55f;
        protected override float ExpireSeconds => 1.0f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _steel = Vol2FsB1.Lit(Kit, "fs_steel", new Color(0.82f, 0.86f, 0.95f), 0.5f, 0.82f, new Color(0.5f, 0.56f, 0.68f), "fs_steel");      // r2: less self-light
            _steelHot = Vol2FsB1.Lit(Kit, "fs_steel", Color.white, 0.4f, 0.8f, new Color(1.2f, 1.15f, 1.05f) * 1.6f, "fs_steel");
            var line = M("fs_streak", White, 4f, 1f); line.NoiseAmt = 0.2f;
            _lines = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(line), Rate = 60f, Loop = true, Life0 = 0.12f, Life1 = 0.2f, Speed0 = -9f, Speed1 = -14f, Size0 = 0.025f, Size1 = 0.045f, Radius = 1.2f, Stretch = true, StretchVel = 0.03f, World = false, Max = 60 }, root, "Lines");
            _chargeLight = Vol2Fs.PointLight(_lines.transform, "ChargeLight", White, 3.5f);
            _lines.transform.position = Tip(); _lines.Play(false);
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 6f, 1f)), Life0 = 0.25f, Life1 = 0.7f, Speed0 = 3f, Speed1 = 12f, Size0 = 0.025f, Size1 = 0.055f, Radius = 0.1f, Gravity = 1f, Drag = 1.6f, Stretch = true, StretchVel = 0.035f, Max = 1500 }, root, "Sparks");
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = _steelHot, Mesh = Kit.SharedMesh("shard0", () => Vol2FsB1.Shard(5)), Life0 = 0.8f, Life1 = 1.3f, Speed0 = 1f, Speed1 = 2f, Size0 = 0.1f, Size1 = 0.26f, Radius = 0.05f, Gravity = 1.3f, Drag = 0.7f, Spin = 900f, Max = 300 }, root, "Shards");
            _glints = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", White, 7f, 1f)), Life0 = 0.08f, Life1 = 0.16f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.14f, Size1 = 0.3f, Radius = 0.01f, Max = 160, SortFudge = -10 }, root, "Glints");
            _flash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", White, 6f, 1f)), Life0 = 0.12f, Life1 = 0.16f, Speed0 = 0f, Speed1 = 0f, Size0 = 1f, Size1 = 1.4f, Radius = 0.01f, SizeStart = 0.3f, SizePeak = 1f, SizePeakAt = 0.2f, SizeEnd = 0.8f, Max = 12, SortFudge = -30 }, root, "Flash");
            foreach (var ps in new[] { _sparks, _shards, _glints, _flash }) ps.Play(false);
            var ring = M("fs_ring", Gold, 3.4f, 1f); ring.NoiseAmt = 0.35f; ring.NoiseScale = 2f; ring.Soft = 0.001f; ring.Scroll = new Vector2(1.2f, 0f);
            Vol2Fs.MeshObject(root, "LightRing", Kit.Mat(ring), out _ringMesh);
            var wire = M("fs_streak", Gold, 4.5f, 1f); wire.NoiseAmt = 0.15f;
            Vol2Fs.MeshObject(root, "Wires", Kit.Mat(wire), out _wireMesh);
            var sl = M("fs_streak", White, 5f, 1f); sl.NoiseAmt = 0.1f;
            Vol2Fs.MeshObject(root, "Slashes", Kit.Mat(sl), out _slashMesh);
            Vol2Fs.MeshObject(root, "BladeGlow", Kit.Mat(sl), out _glowMesh);
            _lights = new Light[3];
            for (int k = 0; k < 3; k++) _lights[k] = Vol2Fs.PointLight(root, "RingLight" + k, new Color(1f, 0.93f, 0.8f), 5f);
        }

        protected override void OnCharge(float t, float k) { _lines.transform.position = Tip(); _chargeLight.intensity = 3.5f * k; }
        protected override void OnLaunch(long nowUs) { _lines.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f; }

        protected override void OnHit(long nowUs)
        {
            if (_built) return;
            _built = true;
            var root = Root.transform;
            var mesh = Kit.SharedMesh("sword", () => Vol2FsB1.Sword());
            int seed = Seed + 271;
            const int N = 12;
            for (int k = 0; k < N; k++)
            {
                float ang = Vol2FsB2.Angle(k, N, seed, 0.2f), back = Back(ang);
                if (Front(ang) < 0.15f) continue;                                  // r2: none on the back / far sides
                var r = Vol2FsB1.Prop(root, "Sword" + k, mesh, k % 3 == 0 ? _steelHot : _steel);
                r.gameObject.SetActive(false);
                _sw.Add(new Sw { T = r.transform, Foot = OnBand(ang, R), Ang = ang, At = 0.04f + 0.18f * Mathf.Abs(ang) / Mathf.PI, Rnd = Vol2Fs.Hash(seed, k), Back = back, Tilt = (Vol2Fs.Hash(seed, k + 10) - 0.5f) * 14f });
            }
            for (int k = 0; k < 3; k++) _lights[k].transform.position = OnBand(-1.1f + 1.1f * k, R) + Vector3.up * 1f;
        }

        void Cut(Struck s) { var c = CentreNow(s); _cuts.Add((c, Vector3.Cross(Vector3.up, FlatN(c - CamPos)), Inst.Rel(NowUs))); Vol2FsB1.Emit(_sparks, c, 140); Vol2FsB1.Emit(_flash, c, 1); }
        protected override void OnStruck(Struck s) { Cut(s); }
        protected override void OnTouch(Struck s) { if (!Hits.Exists(h => h.Id == s.Id && Mathf.Abs(h.At - s.At) < 0.1f)) Cut(s); }

        protected override void OnResidueEnd()
        {
            if (_snapped) return;
            _snapped = true;
            foreach (var s in _sw)
            {
                if (!s.Landed) continue;
                Vector3 side = Vector3.Cross(Vector3.up, Dir(s.Ang));
                Vector3 mid = s.Foot + Vector3.up * SwordLen * SwordScale * 0.4f;
                int n = NaturalEnd ? 5 : 1;
                for (int j = 0; j < n; j++) Vol2FsB1.EmitV(_shards, mid + Vector3.up * Random.Range(-0.4f, 0.6f), NaturalEnd ? side * (Random.value < 0.5f ? -1f : 1f) * Random.Range(4f, 10f) + Vector3.up * Random.Range(0.5f, 2.5f) : Random.insideUnitSphere * 1.2f, Random.Range(0.1f, 0.24f), -1f);
                if (NaturalEnd) { Vol2FsB1.EmitV(_sparks, mid, side * Random.Range(-10f, 10f) + Vector3.up * 2f, -1f, -1f); if (s.Rnd < 0.35f) Vol2FsB1.Emit(_flash, mid, 1); }
                s.T.gameObject.SetActive(false);
            }
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            float warn = Warn(NowUs), ex = ExitK(t), pulse = DrawPulse(t);
            float noRes = Res == null && EndRel >= 0f ? Vol2Fs.Smooth(EndRel, EndRel + 0.5f, t) : 0f;
            _glowS.Clear();
            foreach (var s in _sw)
            {
                if (_snapped) break;
                float a = h - s.At;
                if (a < -0.14f) continue;
                float fall = Mathf.Clamp01((a + 0.14f) / 0.14f);
                if (!s.T.gameObject.activeSelf) s.T.gameObject.SetActive(noRes < 1f);
                if (!s.Landed && fall >= 1f) { s.Landed = true; Vol2FsB1.Emit(_sparks, s.Foot, 20); Vol2FsB1.Emit(_flash, s.Foot + Vector3.up * 0.2f, s.Rnd < 0.4f ? 1 : 0); }
                Quaternion shiver = warn > 0f ? Quaternion.Euler((Mathf.PerlinNoise(t * 47f, s.Rnd * 9f) - 0.5f) * 7f * warn, 0f, (Mathf.PerlinNoise(s.Rnd * 7f, t * 43f) - 0.5f) * 7f * warn) : Quaternion.identity;
                // the sword mesh points along +Z from its grip end: point-down = +Z toward the ground, the point 0.3 m in the ground
                Vector3 down = shiver * (Quaternion.AngleAxis(s.Tilt, Vector3.Cross(Vector3.up, Dir(s.Ang))) * Vector3.down);
                float L = SwordLen * SwordScale;
                Vector3 point = s.Foot + Vector3.down * 0.3f + Vector3.up * 3.5f * (1f - fall) * (1f - fall);
                s.T.SetPositionAndRotation(point - down * L, Quaternion.LookRotation(down, Dir(s.Ang)));
                s.T.localScale = Vector3.one * SwordScale;
                if (s.Landed && noRes < 1f)
                {
                    float l = (0.6f + 0.4f * Mathf.Sin(t * 6f + s.Rnd * 12f)) * (1f + pulse) * (warn > 0f ? 0.4f + 0.6f * Mathf.PerlinNoise(t * 40f, s.Rnd * 3f) : 1f) * (1f - noRes);
                    Vector3 b0 = point - down * L * 0.72f, b1 = point - down * 0.15f;
                    _glowS.Strip(new[] { b0, (b0 + b1) * 0.5f, b1 }, new[] { 0.06f, 0.1f, 0.03f }, null, Color.white, new[] { l * 0.7f, l, l * 0.6f }, s.Rnd, cam, 0f, 1f);
                    if (Random.value < 0.006f + 0.04f * warn) Vol2FsB1.Emit(_glints, Vector3.Lerp(b0, b1, Random.value), 1);
                }
            }
            _glowS.Write(_glowMesh);
            // the light ring on the ground (runs round from the front) and the blade-lines between neighbouring hilts
            _ringS.Clear(); _wireS.Clear();
            float run = Vol2Fs.Smooth(0.05f, 0.3f, h);
            float rl = run * (1f - (_snapped ? 1f : 0f)) * (1f - noRes) * (warn > 0f ? 0.5f + 0.5f * Mathf.PerlinNoise(t * 30f, 0.5f) : 1f) * (1f + 0.5f * pulse);
            if (rl > 0.01f) Vol2FsB2.GroundRing(_ringS, Apex, F, Ground, R - 0.1f, R + 0.1f, 96, a => rl * (Mathf.Abs(a) <= run * Mathf.PI ? 1f : 0f) * (0.2f + 0.8f * Front(a)), 0.21f, cam, 0.03f, 0f, 6f);
            if (!_snapped && noRes < 1f)
            {
                for (int k = 0; k < _sw.Count; k++)
                {
                    var A = _sw[k]; var B = _sw[(k + 1) % _sw.Count];
                    if (!A.Landed || !B.Landed || Vector3.Distance(A.Foot, B.Foot) > 3f) continue;
                    float hA = SwordLen * SwordScale * 0.82f - 0.3f;
                    Vector3 pa = A.Foot + Vector3.up * hA, pb = B.Foot + Vector3.up * hA;
                    float l = (0.7f + 0.3f * Mathf.Sin(t * 20f + A.Rnd * 30f)) * (warn > 0f ? (Mathf.PerlinNoise(t * 40f, A.Rnd * 5f) > warn * 0.7f ? 1f : 0.1f) : 1f) * (1f - noRes);
                    _wireS.Strip(new[] { pa, (pa + pb) * 0.5f, pb }, new[] { 0.02f, 0.03f, 0.02f }, null, Color.white, new[] { l, l, l }, A.Rnd, cam, 0f, 1f);
                }
                var co = Inst.Model.Coordinator;
                if (co != null && Res != null)
                    foreach (var m in co.Materials.Alive)
                    {
                        if (m == Res || m.Body != Body.Metal || m.Kind != Vol2MaterialKind.Residue || m.Footprint == null || m.Attached) continue;
                        Vector3 c = m.Footprint.Centre; c.y = Ground + 0.8f;
                        Sw best = null; float bd = 3f;
                        foreach (var s in _sw) { if (!s.Landed) continue; Vector3 dd = s.Foot - c; dd.y = 0f; if (dd.magnitude < bd) { bd = dd.magnitude; best = s; } }
                        if (best == null) continue;
                        Vector3 pa = best.Foot + Vector3.up * 1.2f;
                        _wireS.Strip(new[] { pa, (pa + c) * 0.5f, c }, new[] { 0.03f, 0.045f, 0.03f }, null, Color.white, new[] { 1f, 1f, 1f }, 0.9f, cam, 0f, 1f);
                    }
            }
            _ringS.Write(_ringMesh); _wireS.Write(_wireMesh);
            _slashS.Clear();
            foreach (var c in _cuts)
            {
                float a = t - c.T, l = 1f - Vol2Fs.Smooth(0.08f, 0.45f, a); if (l <= 0.01f) continue;
                for (int j = 0; j < 2; j++)
                {
                    Vector3 d = (c.Side * Mathf.Cos(0.3f + 0.6f * j) + Vector3.up * Mathf.Sin(0.3f + 0.6f * j) * (j == 0 ? 1f : -1f)).normalized; float s = 0.8f * Vol2Fs.EaseOutBack(a / 0.06f);
                    _slashS.Strip(new[] { c.C - d * s, c.C, c.C + d * s }, new[] { 0.015f, 0.09f, 0.015f }, null, Color.white, new[] { l, l, l }, 0.3f + 0.3f * j, cam, 0f, 1f);
                }
            }
            _slashS.Write(_slashMesh);
            for (int k = 0; k < _lights.Length; k++) _lights[k].intensity = _snapped ? Mathf.Max(0f, 6f * (1f - ex * 3f)) : 2.4f * run * (1f - noRes) * (1f + pulse) * (warn > 0f ? 0.6f + 0.4f * Mathf.Sin(t * 50f) : 1f);
        }
    }
}
