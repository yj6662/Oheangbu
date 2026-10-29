using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>성 (metal, ㅓ attachment, 돌아온다) - free showcase batch 7, concept seong_b (a ring of thin steel feather-blades circles the caster like a spinning skirt
    /// of light; the close bear is cut). Executor facts only: the orbiter = the lead feather (the biggest) at the executor's orbit point (1.6 m, one turn a
    /// second, 6 s), and a flock of mirror-steel feather blades strung out behind it over the last two thirds of the circle, each smaller and tilted further
    /// out, white-gold glints running along their edges, sparks shed from the lead; the first pass = sparks flung sideways + a gold slash (the shared cut
    /// action on the Instant), the second contact (a later turn) = one thinner slash; a shot taken = sliced (sparks, shards). Home (the host's end): the feathers
    /// snap with a ring and their shards fly sideways (CSV 끝: 쨍하게 끊기며 파편이 옆으로 튐).</summary>
    public sealed class Vol2FsSeong : Vol2FsBackOrbit
    {
        static readonly Color Gold = new Color(1f, 0.75f, 0.38f);
        const int Feathers = 20;
        readonly Transform[] _f = new Transform[Feathers];
        Mesh _glintMesh, _slashMesh, _inkMesh, _goldMesh; readonly Vol2FsStrips _glint = new Vol2FsStrips(), _slash = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _gold = new Vol2FsStrips();
        ParticleSystem _sparks, _shards;
        sealed class Cut { public Vector3 C, N; public float At; public bool Weak; }
        readonly List<Cut> _cuts = new List<Cut>();
        bool _snapped;
        protected override Color TrailGlow => new Color(1.5f, 1.25f, 0.8f);
        protected override float TrailArc => 4.2f;
        protected override float TrailWidth => 0.1f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            // r2 (r1 read as matte white paper): a darker chrome with a gold hot edge - steel against the pale sky
            // r3 (r2 still white-grey): darker warm chrome, warm gold hot edge
            var steel = Kit.Metal(0.75f, 0.6f, new Color(0.12f, 0.08f, 0.03f), new Color(0.6f, 0.58f, 0.55f)) ?? Vol2FsB1.Lit(Kit, "fs_steel", new Color(0.6f, 0.58f, 0.55f), 1f, 0.95f, new Color(0.25f, 0.2f, 0.12f));
            var mesh = Kit.SharedMesh("seongFeather", () => Vol2FsB6.Spike(0.2f, 0.03f, 0.4f, 0.12f));
            for (int k = 0; k < Feathers; k++) { _f[k] = Vol2FsB1.Prop(root, "Feather" + k, mesh, steel, false).transform; _f[k].gameObject.SetActive(false); }
            var g = M("fs_streak", Gold * 1.6f, 3.4f, 1f); g.NoiseAmt = 0.15f; g.Soft = 0.02f; g.Erode = 0.08f;
            Vol2Fs.MeshObject(root, "Glints", Kit.Mat(g), out _glintMesh);
            var s = M("fs_streak", new Color(1f, 0.9f, 0.68f), 2.6f, 0.9f); s.NoiseAmt = 0.15f; s.Soft = 0.02f; s.Erode = 0.1f;
            Vol2Fs.MeshObject(root, "Cuts", Kit.Mat(s), out _slashMesh);
            Vol2Fs.MeshObject(root, "FeatherInk", Kit.InkSolid("fs_stroke2", InkC, Gold * 1.4f, 0.14f, 0.45f, 0.3f, 1.4f), out _inkMesh);
            var gb = M("fs_streak", Gold * 1.3f, 2.4f, 1f); gb.NoiseAmt = 0.25f; gb.Soft = 0.03f; gb.Erode = 0.12f; gb.Scroll = new Vector2(-3f, 0f);
            Vol2Fs.MeshObject(root, "FeatherGold", Kit.Mat(gb), out _goldMesh);
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Color.Lerp(Gold, Color.white, 0.5f), 5f, 1f)), Life0 = 0.2f, Life1 = 0.55f, Speed0 = 5f, Speed1 = 13f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.4f, Drag = 0.9f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.4f, Max = 1200 }, root, "Sparks");
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = steel, Mesh = Kit.SharedMesh("shard0", () => Vol2FsB1.Shard(5)), Life0 = 0.6f, Life1 = 1.1f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.05f, Size1 = 0.14f, Gravity = 2f, Drag = 0.4f, Spin = 900f, Max = 300 }, root, "Shards");
            _sparks.Play(false); _shards.Play(false);
        }

        void SparkSide(Vector3 at, int n, float s0 = 5f, float s1 = 13f)
        {
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(CamPos - at));
            for (int k = 0; k < n; k++) Vol2FsB1.EmitV(_sparks, at, (side * (k % 2 == 0 ? 1f : -1f) + Random.insideUnitSphere * 0.7f + Vector3.up * 0.3f).normalized * Random.Range(s0, s1), -1f, -1f);
        }

        protected override void OnStruck(Struck s)
        {
            var c = CentreNow(s); bool weak = s.Kind == "Second";
            SparkSide(c, weak ? 40 : 90, weak ? 4f : 5f, weak ? 9f : 13f);
            _cuts.Add(new Cut { C = c + Vector3.up * (weak ? 0.15f : 0f), N = FlatN(CamPos - c), At = Now(NowUs), Weak = weak });
        }

        protected override void OnShot(Shot s) { SparkSide(s.Pos, 50); for (int k = 0; k < 6; k++) Vol2FsB1.EmitV(_shards, s.Pos, Random.onUnitSphere * 4f, -1f, -1f); }

        protected override void OnHome()
        {
            if (_snapped) return; _snapped = true;
            for (int k = 0; k < Feathers; k++)
            {
                if (_f[k] == null || !_f[k].gameObject.activeSelf) continue;
                Vector3 p = _f[k].position, side = Vector3.Cross(Vector3.up, FlatN(CamPos - p));
                for (int i = 0; i < 3; i++) Vol2FsB1.EmitV(_shards, p, side * (i % 2 == 0 ? 1f : -1f) * Random.Range(3f, 7f) + Vector3.up * Random.Range(0.5f, 2.5f), -1f, -1f);
                if (k % 2 == 0) SparkSide(p, 10, 3f, 8f);
                _f[k].gameObject.SetActive(false);
            }
        }

        protected override void DrawOrbiter(float t, Vector3 pos, Vector3 tangent, float psi, float life)
        {
            var cam = CamPos;
            _glint.Clear(); _slash.Clear(); _ink.Clear(); _gold.Clear();
            bool show = life > 0.01f && !_snapped;
            float band = FrontNow > 0.01f ? life / FrontNow : 0f;
            if (band > 0.01f && !_snapped)
            {
                // r2: the skirt reads as a band lying round the caster - a dark ink underside and a gold sheen band under the feathers, bright streaks over them
                Vector3 c0 = G(Apex);
                Vol2FsOrbitBand.Arc(_ink, c0, F, Ground, OrbitR + 0.1f, psi, 4.4f, OrbitH - 0.3f, 0.48f, band * 0.95f, 0.27f, cam, false, 44, 0.04f, t);
                Vol2FsOrbitBand.Arc(_gold, c0, F, Ground, OrbitR + 0.12f, psi, 4.2f, OrbitH - 0.1f, 0.2f, band * 0.8f, 0.55f, cam, false, 44, 0.05f, t);
                for (int k = 0; k < 3; k++) Vol2FsOrbitBand.Arc(_glint, c0, F, Ground, OrbitR - 0.05f + 0.15f * k, psi, 3.4f - 0.5f * k, OrbitH + 0.05f * k, 0.05f, band * (0.6f + 0.4f * Mathf.Max(0f, Mathf.Sin(t * 9f + k))), 0.4f + 0.2f * k, cam, true, 40, 0.04f, t + k);
            }
            for (int k = 0; k < Feathers; k++)
            {
                if (_f[k] == null) continue;
                float u = k / (float)(Feathers - 1);
                float a = psi - 4.4f * u;
                bool on = show && a >= 0f && Vol2FsOrbitBand.FrontOf(a) > 0.12f;
                _f[k].gameObject.SetActive(on);
                if (!on) continue;
                Vector3 p = OrbitPoint(a, OrbitH + 0.06f * Mathf.Sin(k * 1.7f + t * 7f) - 0.15f * u);
                Vector3 tan = Tangent(a), outw = FlatN(p - G(Apex));
                // a feather lies along the travel, its tip leading, its face tilted out from the caster (a spinning skirt)
                Vector3 up = (tan + outw * (0.25f + 0.5f * u)).normalized;
                Vector3 face = Vector3.Cross(up, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.Cross(up, Vector3.up).normalized : outw;
                face = Quaternion.AngleAxis(30f + 40f * u, up) * face;
                float sc = (1.35f - 0.6f * u) * band * Mathf.Lerp(0.4f, 1f, Vol2FsOrbitBand.FrontOf(a));
                _f[k].SetPositionAndRotation(p - up * 0.45f * sc, Quaternion.LookRotation(face, up));
                _f[k].localScale = new Vector3(sc * 1.4f, sc * 0.95f, sc * 1.4f);
                {
                    float gl = band * (1f - 0.7f * u) * (0.5f + 0.5f * Mathf.Max(0f, Mathf.Sin(t * 14f + k)));
                    _glint.Strip(new[] { p - up * 0.4f * sc, p + up * 0.45f * sc }, new[] { 0.08f * sc, 0.02f }, null, Color.white, new[] { gl, gl }, 0.3f + k * 0.05f, cam);
                }
            }
            if (show) for (int k = 0; k < 2; k++) if (Random.value < FrontNow) Vol2FsB1.EmitV(_sparks, pos + Random.insideUnitSphere * 0.2f, -tangent * Random.Range(2f, 5f) + Random.insideUnitSphere * 1.5f, -1f, -1f);
            foreach (var c in _cuts)
            {
                float a = t - c.At; if (a < 0f || a > 0.55f) continue;
                float open = Vol2Fs.Smooth(0f, 0.05f, a), lf = 1f - Vol2Fs.Smooth(0.15f, 0.55f, a);
                Vector3 right = Vector3.Cross(Vector3.up, c.N).normalized, at = c.C + c.N * 0.5f;
                Vector3 d = (right + Vector3.up * 0.35f).normalized;
                _slash.Strip(new[] { at - d * 1.0f * open, at, at + d * 1.0f * open }, new[] { 0.02f, c.Weak ? 0.07f : 0.14f, 0.02f }, null, Color.white, new[] { lf, lf, lf }, 0.3f, cam);
            }
            _glint.Write(_glintMesh); _slash.Write(_slashMesh); _ink.Write(_inkMesh); _gold.Write(_goldMesh);
        }

        protected override float Tail => 1.2f;
    }
}
