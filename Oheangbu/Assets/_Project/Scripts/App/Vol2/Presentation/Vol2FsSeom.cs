using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>섬 (metal, ㅓ attachment, 심는다) - free showcase batch 5, concept seom_b (a closed bud of steel petals sits on the brush shaft; when the caster is
    /// struck it opens in an instant into a lotus of razor steel petals round the caster, slicing every bear that close, sparks and shards). Unlike 서 (one crescent
    /// edge on the shaft), 수 (a saw of knives circling the ring), 석 (a glaive) and 선 (a chakram) it is a flower of blades that BLOOMS round the caster.
    /// Executor facts only: the start = the bud closes round the shaft with a glint -> the wait (the bud turns slowly, a glint runs round it on each heartbeat, the
    /// ink dial and the 2 m ground arc burn down; the last 0.4 s it trembles and rings) -> Consumed (the player really lost HP) = the lotus: two whorls of long
    /// petals spring open outward and slightly up to the reach in 0.12 s (bright edges, a hit-stop), a slash flare and a spark fountain on every struck bear
    /// (OnStrike), cut ink threads; after a beat every petal snaps with a ring of light and its shards fly SIDEWAYS (CSV 끝: 쨍하게 끊기며 파편이 옆으로 튐);
    /// Expired = the closed bud snaps on the shaft, shards sideways.</summary>
    public sealed class Vol2FsSeom : Vol2FsSeedWear
    {
        static readonly Color Gold = new Color(1f, 0.88f, 0.6f);
        const float OpenSeconds = 0.12f, SnapAt = 0.62f;
        ParticleSystem _sparks, _shards, _glints, _threads;
        Material _steel;
        Mesh _bladeMesh, _xMesh; readonly Vol2FsStrips _xS = new Vol2FsStrips();
        sealed class Petal { public Transform T; public Vector3 Dir; public float Len, Lift, Delay, Rnd; }
        readonly List<Petal> _bud = new List<Petal>(), _lotus = new List<Petal>();
        readonly List<Struck> _cut = new List<Struck>();
        Light _glint;
        bool _snapped;
        protected override float SeedU => 0.72f;
        protected override float FireSeconds => SnapAt + 1.0f;
        protected override float ExpireSeconds => 0.9f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _steel = Vol2FsB1.Lit(Kit, "fs_steel", Color.white, 1f, 0.9f, new Color(0.05f, 0.06f, 0.08f));
            _bladeMesh = Kit.SharedMesh("blade", Vol2FsB1.Blade);
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 5f, 1f)), Life0 = 0.25f, Life1 = 0.7f, Speed0 = 5f, Speed1 = 14f, Size0 = 0.02f, Size1 = 0.05f, Radius = 0.1f, Gravity = 1.4f, Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.5f, Max = 900 }, root, "Sparks");
            var shard = Kit.SharedMesh("shard0", () => Vol2FsB1.Shard(5));
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = _steel, Mesh = shard, Life0 = 0.6f, Life1 = 1.1f, Speed0 = 4f, Speed1 = 9f, Size0 = 0.06f, Size1 = 0.18f, Radius = 0.1f, Gravity = 2f, Drag = 0.4f, Spin = 900f, Max = 400 }, root, "Shards");
            _glints = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Color.white, 4f, 1f)), Life0 = 0.08f, Life1 = 0.14f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.2f, Size1 = 0.35f, Max = 120 }, root, "Glints");
            var thr = M("fs_stroke2", InkC, 1f, 0f); thr.NoiseAmt = 0.3f; thr.Soft = 0.05f;
            _threads = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(thr), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 3f, Speed1 = 7f, Size0 = 0.05f, Size1 = 0.1f, Radius = 0.1f, Drag = 2f, Gravity = 0.6f, Stretch = true, StretchVel = 0.08f, StretchLen = 2f, Max = 160 }, root, "InkThreads");
            foreach (var ps in new[] { _sparks, _shards, _glints, _threads }) ps.Play(false);
            var x = M("fs_streak", Gold, 4f, 1f); x.NoiseAmt = 0.15f; x.Soft = 0.02f; x.Erode = 0.1f;
            Vol2Fs.MeshObject(root, "Slashes", Kit.Mat(x), out _xMesh);
            _glint = Vol2Fs.PointLight(root, "Glint", Gold, 3f);
            for (int k = 0; k < 6; k++)
            {
                var r = Vol2FsB1.Prop(root, "Bud" + k, _bladeMesh, _steel, false);
                _bud.Add(new Petal { T = r.transform, Rnd = k / 6f });
                r.gameObject.SetActive(false);
            }
        }

        protected override void OnPlant(Pod p) { foreach (var b in _bud) b.T.gameObject.SetActive(true); Vol2FsB1.EmitV(_glints, SeedPoint(), Vector3.zero, 0.5f, 0.15f); }

        void Shatter(Vector3 at, Vector3 dir, int n)
        {
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(dir)); if (side == Vector3.zero) side = Right;
            for (int k = 0; k < n; k++) Vol2FsB1.EmitV(_shards, at + Random.insideUnitSphere * 0.15f, side * (k % 2 == 0 ? 1f : -1f) * Random.Range(3f, 8f) + Vector3.up * Random.Range(0.5f, 2.5f) + Random.insideUnitSphere, -1f, -1f);
            for (int k = 0; k < 14; k++) Vol2FsB1.EmitV(_sparks, at, (side * (k % 2 == 0 ? 1f : -1f) + Random.insideUnitSphere * 0.5f).normalized * Random.Range(4f, 11f), -1f, -1f);
        }

        protected override void Fired(Pod p)
        {
            var root = Root.transform; Vector3 c = FireFeet + Vector3.up * 0.95f;
            foreach (var b in _bud) b.T.gameObject.SetActive(false);
            int seed = Seed + 333;
            for (int layer = 0; layer < 2; layer++)
            {
                int n = layer == 0 ? 16 : 11;
                for (int k = 0; k < n; k++)
                {
                    float ang = (k + 0.5f * layer) / n * Mathf.PI * 2f - Mathf.PI, front = Front(ang);
                    if (front < 0.25f) continue;                                         // none straight back at the lens
                    var r = Vol2FsB1.Prop(root, "Petal" + layer + "_" + k, _bladeMesh, _steel, false);
                    _lotus.Add(new Petal { T = r.transform, Dir = Dir(ang), Len = Mathf.Lerp(layer == 0 ? 1.7f : 1.2f, layer == 0 ? Reach + 0.3f : 1.7f, Vol2Fs.Hash(seed, k + layer * 40)) * Mathf.Lerp(0.6f, 1f, front),
                        Lift = layer == 0 ? 0.25f + 0.25f * Vol2Fs.Hash(seed, k + 80) : -0.05f + 0.15f * Vol2Fs.Hash(seed, k + 90), Delay = 0.015f * k / n + layer * 0.03f, Rnd = Vol2Fs.Hash(seed, k + 120) });
                }
            }
            for (int k = 0; k < 120; k++) Vol2FsB1.EmitV(_sparks, c, (Random.insideUnitSphere + Vector3.up * 0.2f).normalized * Random.Range(5f, 13f), -1f, -1f);
            Vol2FsB1.EmitV(_glints, c, Vector3.zero, 0.7f, 0.1f);
            _glint.transform.position = c; _glint.intensity = 8f;
        }

        protected override void OnStrike(Pod p, Struck s)
        {
            if (s.World) return;
            _cut.Add(s);
            Vector3 c = CentreNow(s);
            for (int k = 0; k < 90; k++) Vol2FsB1.EmitV(_sparks, c, (Random.insideUnitSphere + Vector3.up * 0.5f).normalized * Random.Range(5f, 13f), -1f, -1f);
            for (int k = 0; k < 8; k++) Vol2FsB1.EmitV(_threads, c, Random.onUnitSphere * Random.Range(3f, 7f), -1f, -1f);
        }

        protected override void OnPodEnd(Pod p)
        {
            if (Worked(p)) return;
            foreach (var b in _bud) b.T.gameObject.SetActive(false);
            Shatter(SeedPoint(), ShaftAxis, Withered(p) ? 16 : 6);
            Vol2FsB1.EmitV(_glints, SeedPoint(), Vector3.zero, 1f, 0.12f);
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos; var p = The;
            _xS.Clear();
            if (p != null && !Worked(p))
            {
                if (p.Started && !p.Ended)
                {
                    float life = PodLife(p, t, 0.3f), beat = Beat(p), warn = PodWarn(p, t);
                    Vector3 c = SeedPoint(), ax = ShaftAxis, n0 = Vector3.Cross(ax, Vector3.up).normalized; if (n0 == Vector3.zero) n0 = Right;
                    float spin = t * 40f * (1f - Held01) + (warn > 0f ? Mathf.Sin(t * 60f) * 6f * warn : 0f);
                    for (int k = 0; k < _bud.Count; k++)
                    {
                        // closed petals hugging the shaft, tips pointing along it (a bud), a slight flare on the beat
                        Vector3 o = Quaternion.AngleAxis(spin + k * 60f, ax) * n0;
                        Vector3 dir = (ax + o * (0.25f + 0.08f * beat + 0.12f * warn)).normalized;
                        _bud[k].T.SetPositionAndRotation(c - ax * 0.12f + o * 0.03f, Quaternion.LookRotation(dir, o));
                        _bud[k].T.localScale = new Vector3(2.2f, 1f, 0.32f) * Mathf.Max(0.01f, life);
                    }
                    float ph = p.Phase - Mathf.Floor(p.Phase);
                    if (ph < 0.3f && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_glints, c + Quaternion.AngleAxis(ph * 1200f, ax) * n0 * 0.06f, Vector3.zero, 0.18f, 0.1f);
                    _glint.transform.position = c; _glint.intensity = life * (0.4f + 1.2f * beat + warn) * (1f - 0.6f * Held01);
                }
                else _glint.intensity = 0f;
                _xS.Write(_xMesh);
                return;
            }
            if (p == null) { _xS.Write(_xMesh); return; }
            float a = SinceFire(p, t);
            Vector3 centre = FireFeet + Vector3.up * 0.95f;
            if (a >= OpenSeconds && a - Time.deltaTime < OpenSeconds) HitStop(t, 0.08f);
            if (!_snapped && a >= SnapAt)
            {
                _snapped = true;
                foreach (var pt in _lotus) { Shatter(pt.T.position + pt.T.forward * pt.Len * 0.6f, pt.Dir, 3); pt.T.gameObject.SetActive(false); }
                Vol2FsB1.EmitV(_glints, centre, Vector3.zero, 0.8f, 0.1f); _glint.intensity = 6f;
            }
            foreach (var pt in _lotus)
            {
                if (_snapped) break;
                float open = Vol2Fs.EaseOutBack(Mathf.Clamp01((a - pt.Delay) / OpenSeconds));
                // from pointing up (the bud) to pointing out and a little up: the bloom
                Vector3 dir = Vector3.Slerp(Vector3.up, (pt.Dir + Vector3.up * pt.Lift).normalized, Mathf.Clamp01(open));
                Vector3 tangent = Vector3.Cross(Vector3.up, pt.Dir);
                pt.T.SetPositionAndRotation(centre + pt.Dir * 0.25f * open, Quaternion.LookRotation(dir, tangent));
                float tremble = a > OpenSeconds ? 1f + 0.03f * Mathf.Sin(t * 70f + pt.Rnd * 9f) : 1f;
                pt.T.localScale = new Vector3(3.2f, 1.4f, pt.Len * Mathf.Max(0.05f, Mathf.Clamp01(open + 0.1f))) * tremble;
                // r3: no glints on the petal tips (r2: white balls on every tip)
            }
            // the slash flares on the cut bodies
            foreach (var s in _cut)
            {
                float xa = t - s.At; if (xa < 0f || xa > 0.5f) continue;
                float life = 1f - Vol2Fs.Smooth(0.1f, 0.5f, xa), op = Vol2Fs.Smooth(0f, 0.05f, xa);
                Vector3 c = CentreNow(s), view = (cam - c).normalized, right = Vector3.Cross(Vector3.up, view).normalized, up = Vector3.Cross(view, right);
                Vector3 d = (right + up * 0.35f).normalized;
                _xS.Strip(new[] { c - d * 1.2f * op, c, c + d * 1.2f * op }, new[] { 0.02f, 0.14f, 0.02f }, null, Color.white, new[] { life, life, life }, 0.37f, cam);
            }
            if (!_snapped) _glint.intensity = Mathf.Max(0f, 8f * (1f - Vol2Fs.Smooth(0f, 0.4f, a)));
            else _glint.intensity = Mathf.Max(0f, 6f * (1f - Vol2Fs.Smooth(SnapAt, SnapAt + 0.4f, a)));
            _xS.Write(_xMesh);
        }
    }
}
