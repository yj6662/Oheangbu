using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>속 (metal, ㅗ fan, 남긴다) - free showcase batch 3, concept sok_a (across the fan dozens of upright steel stakes stand stuck in the ground at angles,
    /// linked to each other by taut thin glowing blade-lines like a web of wires; the bears are cut, sparks where they touched the lines). Unlike 소's blades that fly
    /// THROUGH the fan it is a field of stakes that STAY; the wires are the CSV's 날선 (3 m 안 금 재료끼리 날선으로 이어진다) drawn between this field's stakes and to
    /// any other metal residue within 3 m (the registry's). Beats, executor facts only: the launch = the stakes rain down out of the air and stab into the fan in a
    /// sweep, the wires snap taut -> they stand humming for the residue material's life; a body the strike or the field touched = cut lines and sparks -> the last
    /// 0.4 s = the wires flicker, the stakes shiver -> natural expiry (CSV: 쨍하게 끊기며 파편이 옆으로 튐) = the wires part with a ring and every stake shatters, the
    /// shards flying SIDEWAYS.</summary>
    public sealed class Vol2FsSok : Vol2FsLeave
    {
        static readonly Color White = new Color(1f, 0.97f, 0.9f), Gold = new Color(1f, 0.85f, 0.55f), Ink = new Color(0.03f, 0.03f, 0.035f);
        ParticleSystem _lines, _sparks, _shards, _glints, _flash, _ink; Light _chargeLight; Light[] _lights;
        Material _steel, _steelHot;
        sealed class Stake { public Transform T; public Vector3 Foot, Dir; public float H, At, Rnd; public bool Landed; }
        readonly List<Stake> _stakes = new List<Stake>();
        readonly List<(int A, int B)> _wires = new List<(int, int)>();
        Mesh _wireMesh, _slashMesh, _splatMesh; readonly Vol2FsStrips _wireS = new Vol2FsStrips(), _slashS = new Vol2FsStrips(), _splat = new Vol2FsStrips();
        readonly List<(Vector3 C, Vector3 Side, float T)> _cuts = new List<(Vector3, Vector3, float)>();
        bool _built, _snapped;
        protected override float ExpireSeconds => 1.0f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);
        static Quaternion Look(Vector3 d) => Quaternion.LookRotation(d, Mathf.Abs(d.normalized.y) > 0.95f ? Vector3.forward : Vector3.up);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _steel = Vol2FsB1.Lit(Kit, "fs_steel", new Color(0.82f, 0.86f, 0.95f), 0.5f, 0.82f, new Color(0.5f, 0.56f, 0.68f), "fs_steel");      // r2: less self-light
            _steelHot = Vol2FsB1.Lit(Kit, "fs_steel", new Color(0.95f, 0.93f, 0.88f), 0.6f, 0.85f, new Color(0.95f, 0.85f, 0.6f), "fs_steel");
            var line = M("fs_streak", White, 4f, 1f); line.NoiseAmt = 0.2f;
            _lines = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(line), Rate = 60f, Loop = true, Life0 = 0.12f, Life1 = 0.2f, Speed0 = -9f, Speed1 = -14f, Size0 = 0.025f, Size1 = 0.045f, Radius = 1.2f, Stretch = true, StretchVel = 0.03f, World = false, Max = 60 }, root, "Lines");
            _chargeLight = Vol2Fs.PointLight(_lines.transform, "ChargeLight", White, 3.5f);
            _lines.transform.position = Tip(); _lines.Play(false);
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 6f, 1f)), Life0 = 0.25f, Life1 = 0.7f, Speed0 = 3f, Speed1 = 12f, Size0 = 0.025f, Size1 = 0.055f, Radius = 0.1f, Gravity = 1f, Drag = 1.6f, Stretch = true, StretchVel = 0.035f, Max = 1500 }, root, "Sparks");
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = _steelHot, Mesh = Kit.SharedMesh("shard0", () => Vol2FsB1.Shard(5)), Life0 = 0.8f, Life1 = 1.3f, Speed0 = 1f, Speed1 = 2f, Size0 = 0.1f, Size1 = 0.26f, Radius = 0.05f, Gravity = 1.3f, Drag = 0.7f, Spin = 900f, Max = 300 }, root, "Shards");
            _glints = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", White, 7f, 1f)), Life0 = 0.08f, Life1 = 0.16f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.14f, Size1 = 0.3f, Radius = 0.01f, Max = 160, SortFudge = -10 }, root, "Glints");
            _flash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", White, 6f, 1f)), Life0 = 0.12f, Life1 = 0.16f, Speed0 = 0f, Speed1 = 0f, Size0 = 1f, Size1 = 1.4f, Radius = 0.01f, SizeStart = 0.3f, SizePeak = 1f, SizePeakAt = 0.2f, SizeEnd = 0.8f, Max = 12, SortFudge = -30 }, root, "Flash");
            var ink = M("fs_ink", Ink, 1f, 0f); ink.Soft = 0.1f; ink.NoiseAmt = 0.4f;
            _ink = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ink), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 1.5f, Speed1 = 4f, Size0 = 0.05f, Size1 = 0.18f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.1f, Gravity = 2f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 400 }, root, "InkSplash");
            foreach (var ps in new[] { _sparks, _shards, _glints, _flash, _ink }) ps.Play(false);
            var wire = M("fs_streak", Gold, 4.5f, 1f); wire.NoiseAmt = 0.15f;
            Vol2Fs.MeshObject(root, "Wires", Kit.Mat(wire), out _wireMesh);
            var sl = M("fs_streak", White, 5f, 1f); sl.NoiseAmt = 0.1f;
            Vol2Fs.MeshObject(root, "Slashes", Kit.Mat(sl), out _slashMesh);
            var sp = M("fs_ink", Ink, 1f, 0f); sp.Soft = 0.001f; sp.NoiseAmt = 0.45f; sp.Erode = 0.3f; sp.Opacity = 0.8f;
            Vol2Fs.MeshObject(root, "InkSplats", Kit.Mat(sp), out _splatMesh);
            _lights = new Light[2];
            for (int k = 0; k < 2; k++) _lights[k] = Vol2Fs.PointLight(root, "FieldLight" + k, new Color(1f, 0.93f, 0.8f), 5f);
        }

        protected override void OnCharge(float t, float k) { _lines.transform.position = Tip(); _chargeLight.intensity = 3.5f * k; }
        protected override void OnLaunch(long nowUs) { _lines.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f; }

        protected override void OnHit(long nowUs)
        {
            if (_built) return;
            _built = true;
            var root = Root.transform;
            var mesh = Kit.SharedMesh("blade", () => Vol2FsB1.Blade());
            int seed = Seed + 171;
            for (int k = 0; k < 24; k++)
            {
                float u = Mathf.Lerp(0.25f, 0.98f, Mathf.Sqrt(Vol2Fs.Hash(seed, k))), v = (Vol2Fs.Hash(seed, k + 50) * 2f - 1f) * 0.9f;
                Vector3 foot = Vol2FsB3.InFan(Apex, F, Depth, Half, u, v, Ground);
                Vector3 dir = (Vector3.up * 2.2f + (foot - Apex).normalized * 0.25f + Random.insideUnitSphere * 0.5f).normalized;
                var r = Vol2FsB1.Prop(root, "Stake" + k, mesh, k % 4 == 0 ? _steelHot : _steel);
                r.gameObject.SetActive(false);
                _stakes.Add(new Stake { T = r.transform, Foot = foot, Dir = dir, H = Mathf.Lerp(1.0f, 2.2f, Vol2Fs.Hash(seed, k + 100)) * Mathf.Lerp(0.7f, 1f, u), At = 0.02f + 0.3f * (v * 0.5f + 0.5f) + 0.05f * Vol2Fs.Hash(seed, k + 150), Rnd = Vol2Fs.Hash(seed, k + 200) });
            }
            // wires: each stake to its two nearest neighbours (within 3 m)
            for (int a = 0; a < _stakes.Count; a++)
            {
                int b1 = -1, b2 = -1; float d1 = 3f, d2 = 3f;
                for (int b = 0; b < _stakes.Count; b++)
                {
                    if (b == a) continue;
                    float d = Vector3.Distance(_stakes[a].Foot, _stakes[b].Foot);
                    if (d < d1) { d2 = d1; b2 = b1; d1 = d; b1 = b; } else if (d < d2) { d2 = d; b2 = b; }
                }
                foreach (var b in new[] { b1, b2 }) if (b >= 0 && !_wires.Contains((b, a)) && !_wires.Contains((a, b))) _wires.Add((a, b));
            }
            for (int k = 0; k < 2; k++) _lights[k].transform.position = Vol2FsB3.InFan(Apex, F, Depth, Half, 0.65f, k == 0 ? -0.5f : 0.5f, Ground) + Vector3.up * 1.2f;
        }

        void Cut(Struck s) { var c = CentreNow(s); _cuts.Add((c, Vector3.Cross(Vector3.up, FlatN(c - CamPos)), Inst.Rel(NowUs))); Vol2FsB1.Emit(_sparks, c, 120); Vol2FsB1.Emit(_flash, c, 1); }
        protected override void OnStruck(Struck s) { Cut(s); }
        protected override void OnTouch(Struck s) { if (!Hits.Exists(h => h.Id == s.Id && Mathf.Abs(h.At - s.At) < 0.1f)) Cut(s); }

        protected override void OnResidueEnd()
        {
            if (_snapped) return;
            _snapped = true;
            Vector3 side = Vector3.Cross(Vector3.up, F);
            foreach (var s in _stakes)
            {
                if (!s.Landed) continue;
                Vector3 mid = s.Foot + s.Dir * s.H * 0.5f;
                int n = NaturalEnd ? 4 : 1;
                for (int j = 0; j < n; j++)
                    Vol2FsB1.EmitV(_shards, s.Foot + s.Dir * s.H * Random.value, NaturalEnd ? side * (Random.value < 0.5f ? -1f : 1f) * Random.Range(4f, 10f) + Vector3.up * Random.Range(0.5f, 2.5f) : Random.insideUnitSphere * 1.2f, Random.Range(0.1f, 0.24f), -1f);
                if (NaturalEnd) { Vol2FsB1.EmitV(_sparks, mid, side * Random.Range(-10f, 10f) + Vector3.up * 2f, -1f, -1f); if (s.Rnd < 0.3f) Vol2FsB1.Emit(_flash, mid, 1); }
                s.T.gameObject.SetActive(false);
            }
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            float warn = Warn(NowUs), ex = ExitK(t), pulse = DrawPulse(t);
            float noRes = Res == null && EndRel >= 0f ? Vol2Fs.Smooth(EndRel, EndRel + 0.5f, t) : 0f;
            // the stakes fall out of the air and stab in (0.12 s each)
            foreach (var s in _stakes)
            {
                if (_snapped) break;
                float a = h - s.At;
                if (a < -0.12f) continue;
                float fall = Mathf.Clamp01((a + 0.12f) / 0.12f);
                Vector3 drop = Vector3.up * 3.2f * (1f - fall) * (1f - fall);
                if (!s.T.gameObject.activeSelf) s.T.gameObject.SetActive(true);
                if (!s.Landed && fall >= 1f) { s.Landed = true; Vol2FsB1.Emit(_sparks, s.Foot, 14); Vol2FsB1.Emit(_ink, s.Foot, 4); if (s.Rnd < 0.4f) Vol2FsB1.Emit(_glints, s.Foot + s.Dir * s.H, 1); }
                Quaternion shiver = Quaternion.identity;
                if (warn > 0f) shiver = Quaternion.Euler((Mathf.PerlinNoise(t * 47f, s.Rnd * 9f) - 0.5f) * 8f * warn, 0f, (Mathf.PerlinNoise(s.Rnd * 7f, t * 43f) - 0.5f) * 8f * warn);
                Vector3 d = shiver * s.Dir;
                // the blade mesh points along +Z from its back end: the stake's foot is a little under the ground
                s.T.SetPositionAndRotation(s.Foot - d * 0.25f + drop, Look(d));
                s.T.localScale = new Vector3(4.2f, 4.2f, (s.H + 0.25f) * (1f - noRes));
                if (s.Landed && Random.value < 0.006f + 0.04f * warn) Vol2FsB1.Emit(_glints, s.Foot + d * s.H * Random.Range(0.4f, 1f), 1);
            }
            // the wires snap taut between landed stakes (and to any metal residue within 3 m), hum, flicker in the warning
            _wireS.Clear();
            if (!_snapped && noRes < 1f)
            {
                foreach (var w in _wires)
                {
                    var A = _stakes[w.A]; var B = _stakes[w.B];
                    if (!A.Landed || !B.Landed) continue;
                    float l = (0.75f + 0.25f * Mathf.Sin(t * 20f + A.Rnd * 30f)) * (warn > 0f ? (Mathf.PerlinNoise(t * 40f, A.Rnd * 5f) > warn * 0.7f ? 1f : 0.1f) : 1f) * (1f + pulse) * (1f - noRes);
                    Vector3 pa = A.Foot + A.Dir * A.H * (0.45f + 0.35f * A.Rnd), pb = B.Foot + B.Dir * B.H * (0.45f + 0.35f * B.Rnd);
                    _wireS.Strip(new[] { pa, (pa + pb) * 0.5f, pb }, new[] { 0.025f, 0.035f, 0.025f }, null, Color.white, new[] { l, l, l }, A.Rnd, cam, 0f, 1f);
                }
                var co = Inst.Model.Coordinator;
                if (co != null && Res != null)
                    foreach (var m in co.Materials.Alive)
                    {
                        if (m == Res || m.Body != Body.Metal || m.Kind != Vol2MaterialKind.Residue || m.Footprint == null || m.Attached) continue;
                        Vector3 c = m.Footprint.Centre; c.y = Ground + 0.8f;
                        Stake best = null; float bd = 3f;
                        foreach (var s in _stakes) { if (!s.Landed) continue; Vector3 dd = s.Foot - c; dd.y = 0f; if (dd.magnitude < bd) { bd = dd.magnitude; best = s; } }
                        if (best == null) continue;
                        Vector3 pa = best.Foot + best.Dir * best.H * 0.6f;
                        _wireS.Strip(new[] { pa, (pa + c) * 0.5f, c }, new[] { 0.03f, 0.045f, 0.03f }, null, Color.white, new[] { 1f, 1f, 1f }, 0.9f, cam, 0f, 1f);
                    }
            }
            _wireS.Write(_wireMesh);
            _slashS.Clear();
            foreach (var c in _cuts)
            {
                float a = t - c.T, l = 1f - Vol2Fs.Smooth(0.08f, 0.45f, a); if (l <= 0.01f) continue;
                for (int j = 0; j < 2; j++)
                {
                    Vector3 d = (c.Side * Mathf.Cos(0.4f + 0.5f * j) + Vector3.up * Mathf.Sin(0.4f + 0.5f * j) * (j == 0 ? 1f : -1f)).normalized; float s = 0.8f * Vol2Fs.EaseOutBack(a / 0.06f);
                    _slashS.Strip(new[] { c.C - d * s, c.C, c.C + d * s }, new[] { 0.015f, 0.09f, 0.015f }, null, Color.white, new[] { l, l, l }, 0.3f + 0.3f * j, cam, 0f, 1f);
                }
            }
            _slashS.Write(_slashMesh);
            _splat.Clear();
            float il = Vol2Fs.Smooth(0.1f, 0.4f, h) * (1f - Vol2Fs.Smooth(0.3f, 1f, ex)) * (1f - noRes);
            if (il > 0.01f) foreach (var s in _stakes) if (s.Landed && s.Rnd < 0.5f) _splat.Quad(new Vector3(s.Foot.x, Ground + 0.02f, s.Foot.z), Right, Fwd, 0.7f + 0.5f * s.Rnd, new Color(1f, 1f, 1f, il), s.Rnd);
            _splat.Write(_splatMesh);
            for (int k = 0; k < _lights.Length; k++) _lights[k].intensity = _snapped ? Mathf.Max(0f, 6f * (1f - ex * 3f)) : 2.2f * Vol2Fs.Smooth(0.1f, 0.4f, h) * (1f - noRes) * (1f + pulse) * (warn > 0f ? 0.6f + 0.4f * Mathf.Sin(t * 50f) : 1f);
        }
    }
}
