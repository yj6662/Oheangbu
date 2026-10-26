using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>솟 (metal, ㅗ fan, 가른다 - the PEAK: the one 가른다 that leaves a material) - free showcase batch 6, concept sot_c (one great ink-and-steel crescent sweeps
    /// the fan; every gouged crack line across it is lined with a thin upright razor edge of white-gold steel standing knee-high out of the ground - a field of
    /// glinting lines; the wall cut, the bears stripped, the walker cut as it steps in). Executor facts only: the launch = the cut: the crescent sweeps right
    /// edge to left (a presentation of the one tick) and the radial gouges open behind it; the METAL is the executor's own Residue material (0.35, 4 s, the
    /// fan's footprint): from its creation the edges rise out of the gouges, glint and hum while it stands (they tremble in its warning window), and every
    /// "Residue" contact - a body drawing its share, the walker included - flashes the nearest edge and cuts that body (sparks, a bright slash); the material's
    /// end is the edges' end, by its reason: Consumed / Expired = they snap with shards flung to the side (CSV 끝: 쨍하게 끊기며 파편이 옆으로 튐), anything
    /// else = they sink. Nothing here keeps the edges a moment longer than the material.</summary>
    public sealed class Vol2FsSot : Vol2FsCutFan
    {
        static readonly Color Gold = new Color(1f, 0.88f, 0.6f);
        ParticleSystem _sparks, _shards, _glints;
        Material _steel; Mesh _edgeMesh, _sweepMesh, _sweepInkMesh, _cutMesh, _rimMesh; readonly Vol2FsStrips _sweep = new Vol2FsStrips(), _sweepInk = new Vol2FsStrips(), _cut = new Vol2FsStrips(), _rim = new Vol2FsStrips();
        sealed class Edge { public Transform T; public Vector3 Mid, Dir; public float Len, H, OpenAt, Flash = -1f; public bool Snapped; }
        readonly List<Edge> _edges = new List<Edge>();
        readonly List<(Vector3 A, Vector3 B, Vector3 Bow, float At)> _slashes = new List<(Vector3, Vector3, Vector3, float)>();
        bool _snapped;
        protected override Color CutGlow => new Color(1.7f, 1.45f, 0.9f);
        protected override int GougeCount => 7;
        protected override float SweepSeconds => 0.1f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _steel = Kit.Metal(1.25f, 0.18f, new Color(0.06f, 0.05f, 0.03f)) ?? Vol2FsB1.Lit(Kit, "fs_steel", Color.white, 1f, 0.9f, new Color(0.04f, 0.05f, 0.07f));      // r3: bright mirror with a warm edge
            _edgeMesh = Kit.SharedMesh("sotEdge", () => EdgeMesh(9));
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 5f, 1f)), Life0 = 0.25f, Life1 = 0.7f, Speed0 = 5f, Speed1 = 14f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.4f, Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.5f, Max = 1400 }, root, "Sparks");
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = _steel, Mesh = Kit.SharedMesh("shard0", () => Vol2FsB1.Shard(5)), Life0 = 0.7f, Life1 = 1.3f, Speed0 = 4f, Speed1 = 10f, Size0 = 0.09f, Size1 = 0.24f, Gravity = 2f, Drag = 0.35f, Spin = 900f, Max = 700 }, root, "Shards");
            _glints = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Color.Lerp(Gold, Color.white, 0.35f), 3f, 1f)), Life0 = 0.08f, Life1 = 0.16f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.1f, Size1 = 0.22f, Max = 160 }, root, "Glints");
            foreach (var ps in new[] { _sparks, _shards, _glints }) ps.Play(false);
            var sw = M("fs_streak", Gold, 2.4f, 1f); sw.NoiseAmt = 0.15f; sw.Soft = 0.02f; sw.Erode = 0.1f;
            Vol2Fs.MeshObject(root, "Sweep", Kit.Mat(sw), out _sweepMesh);
            Vol2Fs.MeshObject(root, "SweepInk", Kit.InkSolid("fs_stroke2", InkC, new Color(1.4f, 1.2f, 0.8f, 1f), 0.08f, 0.4f, 0.3f, 1.3f), out _sweepInkMesh);
            Vol2Fs.MeshObject(root, "Cuts", Kit.Mat(sw), out _cutMesh);
            var rim = M("fs_streak", Gold, 3f, 1f); rim.NoiseAmt = 0.1f; rim.Soft = 0.02f;
            Vol2Fs.MeshObject(root, "EdgeRims", Kit.Mat(rim), out _rimMesh);
        }

        /// <summary>A thin upright plate along +X (0..1), height 1 with a jagged razor top, 0.03 thick, pivot at its bottom start.</summary>
        static Mesh EdgeMesh(int seed)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>();
            int n = 14; float th = 0.015f;
            for (int k = 0; k <= n; k++)
            {
                float u = k / (float)n, top = (0.75f + 0.25f * Vol2Fs.Hash(seed, k)) * Mathf.Sin(Mathf.Clamp01(u * 0.92f + 0.04f) * Mathf.PI * 0.5f + Mathf.PI * 0.25f) * 1.1f;
                if (k == 0 || k == n) top *= 0.3f;
                v.Add(new Vector3(u, 0f, -th)); v.Add(new Vector3(u, top, 0f)); v.Add(new Vector3(u, 0f, th));
                uv.Add(new Vector2(u, 0f)); uv.Add(new Vector2(u, 1f)); uv.Add(new Vector2(u, 0f));
            }
            for (int k = 0; k < n; k++)
            {
                int a = k * 3, b = a + 3;
                idx.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });             // the -z face
                idx.AddRange(new[] { a + 2, b + 2, a + 1, a + 1, b + 2, b + 1 });     // the +z face
            }
            var m = new Mesh { name = "FsEdge" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(idx, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // r3: the material is created on the launch tick BEFORE the look lays its gouges (r2 built no edges at all - only the gouges' hot lines showed):
        // the panels are built as soon as both the metal and the gouges exist
        protected override void OnMetal() { BuildEdges(); }
        bool _built;

        /// <summary>r3: standing blade panels - two per gouge (0.35 and 0.72 of it), 1.2 - 1.5 m tall, 0.6 - 0.75 m wide, turned ~50 degrees off the gouge so
        /// their mirror face shows to the shoulder camera (r2: knee-high plates along the radial lines were edge-on = invisible).</summary>
        void BuildEdges()
        {
            if (_built || Metal == null || Gouges.Count == 0) return;
            _built = true;
            int n = 0;
            foreach (var g in Gouges)
            {
                Vector3 d = g.B - g.A; d.y = 0f; float len = d.magnitude; if (len < 0.4f) continue;
                d /= len;
                foreach (float u in new[] { 0.35f, 0.72f })
                {
                    float w = 0.6f + 0.15f * Vol2Fs.Hash(Seed, n + 900), twist = (n % 2 == 0 ? 1f : -1f) * (42f + 16f * Vol2Fs.Hash(Seed, n + 910));
                    Vector3 pd = Quaternion.AngleAxis(twist, Vector3.up) * d;
                    Vector3 centre = G(g.A + d * len * u);
                    var r = Vol2FsB1.Prop(Root.transform, "Edge" + _edges.Count, _edgeMesh, _steel, false);
                    var e = new Edge { T = r.transform, Mid = centre - pd * w * 0.5f, Dir = pd, Len = w, H = 1.2f + 0.3f * Vol2Fs.Hash(Seed, n + 920), OpenAt = Mathf.Max(g.OpenAt + 0.03f * u, MetalAt) };
                    e.T.gameObject.SetActive(false);
                    _edges.Add(e); n++;
                }
            }
        }

        protected override void Sweep(float t, float k, float prevK)
        {
            Vector3 p = InFan(0.62f, SweepAng(k), 0.7f);
            for (int i = 0; i < 6; i++) Vol2FsB1.EmitV(_sparks, p, (Dir(SweepAng(k) - 0.6f) * 6f + Random.insideUnitSphere * 3f + Vector3.up), -1f, -1f);
        }

        protected override void OnStruck(Struck s)
        {
            var b = s.T != null ? BodyBounds(s.T, s.T.position, 0.5f) : s.B;
            Vector3 toCam = FlatN(CamPos - b.center), side = Vector3.Cross(Vector3.up, toCam), c = b.center + toCam * (Mathf.Max(b.extents.x, b.extents.z) + 0.2f);
            float t = Now(NowUs);
            if (s.Kind == "Residue")
            {
                // the nearest edge flashes and cuts the body that walked in (its share of the metal)
                Edge best = null; float bd = 1e9f;
                foreach (var e in _edges) { if (e.Snapped) continue; float dd = DistToEdge(e, FeetOf(s)); if (dd < bd) { bd = dd; best = e; } }
                if (best != null) best.Flash = t;
                _slashes.Add((c - side * 0.9f + Vector3.down * 0.5f, c + side * 0.9f + Vector3.up * 0.5f, Vector3.up * 0.2f, t));
                for (int i = 0; i < 70; i++) Vol2FsB1.EmitV(_sparks, G(FeetOf(s)) + Vector3.up * 0.3f, (Random.insideUnitSphere + Vector3.up * 0.6f).normalized * Random.Range(4f, 12f), -1f, -1f);
                HitStop(t, 0.06f);
                return;
            }
            if (s.Kind == "Instant") _slashes.Add((c + side * 1.0f - Vector3.up * 0.6f, c - side * 1.0f + Vector3.up * 0.7f, -Vector3.up * 0.15f, t));
        }

        float DistToEdge(Edge e, Vector3 p) { Vector3 q = G(p) - e.Mid; float u = Mathf.Clamp(Vector3.Dot(q, e.Dir), 0f, e.Len); return (q - e.Dir * u).magnitude; }

        protected override void OnMetalEnd()
        {
            // Consumed / Expired: the edges snap, shards to the side; another end: they only sink (Live)
            if (MetalWhy != Vol2MaterialEnd.Consumed && MetalWhy != Vol2MaterialEnd.Expired) return;
            _snapped = true;
            foreach (var e in _edges)
            {
                if (e.T == null || !e.T.gameObject.activeSelf) continue;
                e.Snapped = true; e.T.gameObject.SetActive(false);
                Vector3 side = Vector3.Cross(Vector3.up, e.Dir);
                for (int k = 0; k < 20; k++)
                {
                    Vector3 at = e.Mid + e.Dir * e.Len * Random.value + Vector3.up * Random.Range(0.1f, e.H);
                    Vol2FsB1.EmitV(_shards, at, side * (k % 2 == 0 ? 1f : -1f) * Random.Range(3f, 7f) + Vector3.up * Random.Range(0.5f, 2.5f), -1f, -1f);
                }
                for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_sparks, e.Mid + e.Dir * e.Len * Random.value + Vector3.up * Random.Range(0.2f, e.H), (side * (k % 2 == 0 ? 1f : -1f) + Random.insideUnitSphere * 0.5f).normalized * Random.Range(4f, 10f), -1f, -1f);
                Vol2FsB1.EmitV(_glints, e.Mid + e.Dir * e.Len * 0.5f + Vector3.up * e.H * 0.6f, Vector3.zero, 0.22f, 0.12f);
            }
            HitStop(MetalEndAt, 0.05f);
            FlashLight(InFan(0.6f, 0f, 0.5f), 6f, MetalEndAt + 0.2f);
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos;
            _sweep.Clear(); _sweepInk.Clear(); _cut.Clear(); _rim.Clear();
            // the crescent: thin-to-wide, its leading end at the sweep, its tail thinning; the ink body under it; a lagging phantom
            // r2: drawn in 0.1 s, then lingering 0.9 s with two phantoms (Phantom Blade principles 3 + 5): the ink body under the white-gold blade
            SweepArc(_sweepInk, null, t, Depth * 0.6f, 0.62f, 0.75f, 0.21f, 0.9f, 0.15f, 0.15f, 0.9f);
            SweepArc(_sweep, _sweep, t, Depth * 0.6f, 0.72f, 0.5f, 0.41f, 0.9f, 0.15f, 0.15f, 0.9f);
            // the standing edges: rise with their gouge, glint while the metal stands, tremble in its warning, sink if it ends some other way
            BuildEdges();
            float ml = MetalLife(t), warn = MetalWarn(t);
            foreach (var e in _edges)
            {
                if (e.Snapped) continue;
                float a = t - e.OpenAt;
                float rise = a < 0f ? 0f : Vol2Fs.EaseOutBack(Mathf.Clamp01(a / 0.1f));
                float life = MetalEndAt >= 0f && !_snapped ? ml : rise;
                bool on = a >= 0f && life > 0.01f;
                e.T.gameObject.SetActive(on);
                if (!on) continue;
                Vector3 jit = warn > 0f ? Random.insideUnitSphere * 0.015f * warn : Vector3.zero;
                e.T.SetPositionAndRotation(e.Mid + jit, Quaternion.LookRotation(Vector3.Cross(e.Dir, Vector3.up), Vector3.up));
                e.T.localScale = new Vector3(e.Len, e.H * Mathf.Max(0.01f, life), 1f);
                float fl = e.Flash < 0f ? 0f : 1f - Mathf.Clamp01((t - e.Flash) / 0.25f);
                if ((Time.frameCount + _edges.IndexOf(e) * 7) % 23 == 0 || fl > 0.5f) Vol2FsB1.EmitV(_glints, e.Mid + e.Dir * e.Len * Random.value + Vector3.up * e.H * 0.8f, Vector3.zero, 0.12f + 0.12f * fl, 0.12f);
                // the gold rim along its razor top (the mirror steel reads dark against the paper sky from the shoulder)
                var rp = new Vector3[5]; var rw = new float[5]; var ra = new float[5];
                float[] ru = { 0.08f, 0.3f, 0.5f, 0.7f, 0.92f }, rh = { 0.62f, 0.86f, 0.92f, 0.9f, 0.72f };
                for (int i = 0; i < 5; i++) { rp[i] = e.Mid + e.Dir * e.Len * ru[i] + Vector3.up * e.H * life * rh[i] + jit; rw[i] = (0.05f + 0.06f * fl) * (i == 0 || i == 4 ? 0.5f : 1f); ra[i] = 0.9f; }
                _rim.Strip(rp, rw, null, Color.white, ra, 0.3f + e.Len, cam);
            }
            foreach (var s in _slashes)
            {
                float a = t - s.At; if (a < 0f || a > 0.6f) continue;
                float reveal = Vol2Fs.Smooth(0f, 0.05f, a), life = 1f - Vol2Fs.Smooth(0.12f, 0.6f, a);
                Vol2FsB6.Slash(_cut, s.A, s.B, s.Bow, 0.26f, reveal, life, 0.37f, cam);
            }
            _sweep.Write(_sweepMesh); _sweepInk.Write(_sweepInkMesh); _cut.Write(_cutMesh); _rim.Write(_rimMesh);
        }
    }
}
