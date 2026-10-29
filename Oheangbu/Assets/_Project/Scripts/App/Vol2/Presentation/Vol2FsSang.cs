using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>상 (metal, ㅏ path, 돌아온다) - free showcase batch 7, concept sang_a (a big bright mirror-steel crescent spins out along the path, cuts both bears, turns
    /// at the far end with a burst of sparks and flies back on a second line; an ink loop with a white-gold edge traces the whole trip, sparks streaming off it).
    /// Executor facts only: the head = a spinning mirror-steel crescent (2 m across, chrome + a white-gold edge) at the executor's head, tilted like a thrown
    /// boomerang; behind it the trip draws itself as a black ink ribbon with a gold edge along the trail the head really flew (out lane, the U where the path
    /// stopped, the back lane) that lingers then breaks off in shards; the turn = a spark burst + hit-stop; every touch = sparks flung sideways and a gold X cut
    /// (the shared cut action on the first); the return touch is a single thinner slash (x0.5); a shot taken is sliced (sparks). Home: the crescent snaps into the
    /// brush with a ring - shards to the side (CSV 끝: 쨍하게 끊기며 파편이 옆으로 튐).</summary>
    public sealed class Vol2FsSang : Vol2FsBackPath
    {
        static readonly Color Gold = new Color(1f, 0.86f, 0.55f);
        Transform _blade; Mesh _edgeMesh, _inkMesh, _goldMesh, _slashMesh;
        readonly Vol2FsStrips _edge = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _gold = new Vol2FsStrips(), _slash = new Vol2FsStrips();
        ParticleSystem _sparks, _shards;
        readonly List<(Vector3 P, float T)> _flown = new List<(Vector3, float)>(256);
        sealed class Cut { public Vector3 C, N; public float At; public bool Weak; }
        readonly List<Cut> _cuts = new List<Cut>();
        bool _snapped;
        protected override Color LoopGlow => new Color(1.5f, 1.25f, 0.8f);
        protected override float LoopTail => 1.2f;

        static Mesh CrescentMesh()
        {
            // a flat crescent (outer arc r 1, inner arc offset) in the x-z plane, two faces, normals up / down
            int n = 24; var v = new List<Vector3>(); var nr = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>();
            for (int side = 0; side < 2; side++)
            {
                int b = v.Count;
                for (int k = 0; k <= n; k++)
                {
                    float u = k / (float)n, a = Mathf.Lerp(-1.25f, 1.25f, u);
                    float w = Mathf.Max(0f, Mathf.Sin(u * Mathf.PI)) * 0.32f;
                    Vector3 o = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)), i = o * (1f - w) - new Vector3(0f, 0f, w * 0.4f);
                    float y = side == 0 ? 0.008f : -0.008f;
                    v.Add(o + Vector3.up * y); v.Add(i + Vector3.up * y);
                    nr.Add(side == 0 ? Vector3.up : Vector3.down); nr.Add(side == 0 ? Vector3.up : Vector3.down);
                    uv.Add(new Vector2(u, 1f)); uv.Add(new Vector2(u, 0f));
                }
                for (int k = 0; k < n; k++)
                {
                    int a0 = b + k * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                    if (side == 0) { idx.Add(a0); idx.Add(b0); idx.Add(a1); idx.Add(a1); idx.Add(b0); idx.Add(b1); }
                    else { idx.Add(a0); idx.Add(a1); idx.Add(b0); idx.Add(a1); idx.Add(b1); idx.Add(b0); }
                }
            }
            var m = new Mesh { name = "FsSangCrescent" }; m.SetVertices(v); m.SetNormals(nr); m.SetUVs(0, uv); m.SetTriangles(idx, 0); m.RecalculateBounds();
            return m;
        }

        protected override void OnSetup()
        {
            var root = Root.transform;
            var steel = Kit.Metal(1.0f, 0.4f, new Color(0.06f, 0.045f, 0.02f), new Color(0.72f, 0.68f, 0.6f)) ?? Vol2FsB1.Lit(Kit, "fs_steel", Color.white, 1f, 0.92f, new Color(1.1f, 1.05f, 1f));
            _blade = Vol2FsB1.Prop(root, "Crescent", Kit.SharedMesh("sangCrescent", CrescentMesh), steel, false).transform;
            _blade.gameObject.SetActive(false);
            var e = M("fs_streak", Gold * 1.6f, 3.4f, 1f); e.NoiseAmt = 0.15f; e.Soft = 0.02f; e.Erode = 0.08f;
            Vol2Fs.MeshObject(root, "CrescentEdge", Kit.Mat(e), out _edgeMesh);
            Vol2Fs.MeshObject(root, "TripInk", Kit.InkSolid("fs_stroke2", InkC, Gold * 1.5f, 0.14f, 0.45f, 0.3f, 1.4f), out _inkMesh);
            var g = M("fs_streak", Gold * 1.3f, 2.4f, 1f); g.NoiseAmt = 0.25f; g.Soft = 0.03f; g.Erode = 0.12f; g.Scroll = new Vector2(-3f, 0f);
            Vol2Fs.MeshObject(root, "TripGold", Kit.Mat(g), out _goldMesh);
            var s = M("fs_streak", new Color(1f, 0.9f, 0.68f), 2.6f, 0.9f); s.NoiseAmt = 0.15f; s.Soft = 0.02f; s.Erode = 0.1f;
            Vol2Fs.MeshObject(root, "Cuts", Kit.Mat(s), out _slashMesh);
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Color.Lerp(Gold, Color.white, 0.5f), 5f, 1f)), Life0 = 0.2f, Life1 = 0.55f, Speed0 = 5f, Speed1 = 13f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.4f, Drag = 0.9f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.4f, Max = 1200 }, root, "Sparks");
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = steel, Mesh = Kit.SharedMesh("shard0", () => Vol2FsB1.Shard(5)), Life0 = 0.6f, Life1 = 1.1f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.05f, Size1 = 0.14f, Gravity = 2f, Drag = 0.4f, Spin = 900f, Max = 300 }, root, "Shards");
            _sparks.Play(false); _shards.Play(false);
        }

        void SparkSide(Vector3 at, int n, float s0 = 5f, float s1 = 13f)
        {
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(CamPos - at));
            for (int k = 0; k < n; k++) Vol2FsB1.EmitV(_sparks, at, (side * (k % 2 == 0 ? 1f : -1f) + Random.insideUnitSphere * 0.7f + Vector3.up * 0.3f).normalized * Random.Range(s0, s1), -1f, -1f);
        }

        protected override void Run(float t, float along, float prev, bool back)
        {
            int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(along - prev) * 2f), 1, 12);
            for (int k = 0; k < n; k++)
            {
                Vector3 p = TripPoint(Mathf.Lerp(prev, along, (k + 0.5f) / n), back);
                for (int i = 0; i < 3; i++) Vol2FsB1.EmitV(_sparks, p + Random.insideUnitSphere * 0.6f, Random.insideUnitSphere * 3f - (back ? -F : F) * 2f, -1f, -1f);
            }
        }

        protected override void OnTurn() { Vector3 p = HeadPos(Now(NowUs)); SparkSide(p, 120, 4f, 12f); }

        protected override void OnStruck(Struck s)
        {
            var c = CentreNow(s); SparkSide(c, 90);
            _cuts.Add(new Cut { C = c, N = FlatN(CamPos - c), At = Now(NowUs) });
        }

        protected override void OnReturnHit(Struck s)
        {
            var c = CentreNow(s); SparkSide(c, 40, 4f, 9f);
            _cuts.Add(new Cut { C = c + Vector3.up * 0.15f, N = FlatN(CamPos - c), At = Now(NowUs), Weak = true });
        }

        protected override void OnShot(Shot s) { SparkSide(s.Pos, 50); for (int k = 0; k < 6; k++) Vol2FsB1.EmitV(_shards, s.Pos, Random.onUnitSphere * 4f, -1f, -1f); }

        protected override void OnHome()
        {
            if (_snapped) return; _snapped = true;
            Vector3 p = Tip();
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(CamPos - p));
            for (int k = 0; k < 16; k++) Vol2FsB1.EmitV(_shards, p + Random.insideUnitSphere * 0.2f, side * (k % 2 == 0 ? 1f : -1f) * Random.Range(3f, 8f) + Vector3.up * Random.Range(0.5f, 3f), -1f, -1f);
            SparkSide(p, 60);
        }

        protected override void DrawHead(float t, Vector3 pos, Vector3 dir, bool back, float life)
        {
            var cam = CamPos;
            if (life > 0.01f && (_flown.Count == 0 || (_flown[_flown.Count - 1].P - pos).sqrMagnitude > 1e-6f)) _flown.Add((pos, t));
            _edge.Clear(); _ink.Clear(); _gold.Clear(); _slash.Clear();
            _blade.gameObject.SetActive(life > 0.01f);
            if (life > 0.01f)
            {
                // a thrown crescent: spinning about an axis tilted toward the camera so its face reads
                Vector3 axis = Quaternion.AngleAxis(32f, Vector3.Cross(Vector3.up, dir)) * Vector3.up;
                var rot = Quaternion.AngleAxis(t * 1500f, axis) * Quaternion.FromToRotation(Vector3.up, axis);
                _blade.SetPositionAndRotation(pos, rot);
                _blade.localScale = Vector3.one * 1.5f * Vol2Fs.EaseOutBack(Mathf.Clamp01((t - LaunchAt) / 0.08f));
                // its bright edge (a ring of glints along the outer arc)
                int n = 16; var ep = new Vector3[n]; var ew = new float[n]; var ea = new float[n];
                for (int k = 0; k < n; k++) { float u = k / (float)(n - 1), a = Mathf.Lerp(-1.25f, 1.25f, u); ep[k] = _blade.TransformPoint(new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a))); ew[k] = 0.07f * Mathf.Max(0.1f, Mathf.Sin(u * Mathf.PI)); ea[k] = life; }
                _edge.Strip(ep, ew, null, Color.white, ea, 0.5f, cam);
            }
            // the trip: an ink ribbon with a gold edge along where the head flew; it breaks off from its tail after home
            float loop = LoopLife(t);
            if (_flown.Count >= 2 && loop > 0.01f)
            {
                float brk = IsHome ? Mathf.Clamp01((t - HomeAt) / LoopTail) : 0f;
                int from = Mathf.Min(_flown.Count - 2, Mathf.FloorToInt(brk * (_flown.Count - 1)));
                var pts = new List<Vector3>(); var wi = new List<float>(); var wg = new List<float>(); var ai = new List<float>();
                for (int k = from; k < _flown.Count; k++)
                {
                    float age = t - _flown[k].T, fresh = 1f - Vol2Fs.Smooth(0.05f, 0.9f, age);
                    pts.Add(_flown[k].P + Vector3.down * 0.15f); wi.Add(0.55f * (0.45f + 0.55f * fresh)); wg.Add(0.05f + 0.06f * fresh); ai.Add(loop * (0.5f + 0.5f * fresh));
                }
                if (pts.Count >= 2) { _ink.Strip(pts, wi, null, Color.white, ai, 0.27f, cam); for (int k = 0; k < pts.Count; k++) pts[k] += Vector3.up * 0.17f; _gold.Strip(pts, wg, null, Color.white, ai, 0.66f, cam); }
                if (IsHome && from > 0 && Time.frameCount % 3 == 0) Vol2FsB1.EmitV(_shards, _flown[from].P, Random.insideUnitSphere * 2f, -1f, -1f);
            }
            // the gold X on each struck body (one thinner stroke for the return)
            foreach (var c in _cuts)
            {
                float a = t - c.At; if (a < 0f || a > 0.55f) continue;
                float open = Vol2Fs.Smooth(0f, 0.05f, a), lf = 1f - Vol2Fs.Smooth(0.15f, 0.55f, a);
                Vector3 right = Vector3.Cross(Vector3.up, c.N).normalized, at = c.C + c.N * 0.5f;
                for (int k = 0; k < (c.Weak ? 1 : 2); k++)
                {
                    Vector3 d = (right * (k == 0 ? 1f : -1f) + Vector3.up * 1.1f).normalized;
                    _slash.Strip(new[] { at - d * 0.9f * open, at, at + d * 0.9f * open }, new[] { 0.02f, c.Weak ? 0.07f : 0.13f, 0.02f }, null, Color.white, new[] { lf, lf, lf }, 0.3f + k * 0.4f, cam);
                }
            }
            _edge.Write(_edgeMesh); _ink.Write(_inkMesh); _gold.Write(_goldMesh); _slash.Write(_slashMesh);
        }
    }
}
