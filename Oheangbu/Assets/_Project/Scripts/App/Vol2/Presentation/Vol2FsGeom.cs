using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>검 (wood, ㅓ attachment, 심는다) - free showcase batch 5, concept geom_a (a seed pod sits on the caster's brush near the sleeve with tendrils winding
    /// up the shaft; when the caster is struck the pod bursts and a ring of thorned vines whips out across the ground and coils up the legs of every bear that
    /// close). Executor facts only: the start = the pod buds on the shaft (leaves, jade sparks) -> the wait (the pod breathes jade on the heartbeat, its tendrils
    /// creep up the shaft as the live expiry nears, the ink dial and the 2 m ground arc burn down; the last 0.4 s the tendrils twitch) -> Consumed (the player
    /// really lost HP) = the pod bursts: 12 vines lash out flat over the ground to the reach, an ink-and-leaf splash, and the vines coil up each struck bear
    /// (OnStrike) for the bind's 2 s (the status sink owns the slow; mirrored) -> the coils loosen into rootlets reaching forward (CSV 끝: 잔뿌리로 풀림
    /// (앞으로 뻗으며)); no bear near = the lashing ring alone; Expired = the pod browns, its tendrils slacken and two rootlets creep forward from the
    /// caster's feet and sink.</summary>
    public sealed class Vol2FsGeom : Vol2FsSeedWear
    {
        static readonly Color Jade = new Color(0.4f, 1f, 0.45f);
        const float HoldSeconds = 2f, UnravelSeconds = 0.9f;
        ParticleSystem _leaves, _sparks, _drops, _dirt;
        Material _vine, _dry, _leafMat;
        Transform _pod; MeshRenderer _podR; Light _glow;
        readonly Vol2FsTubes _tendrils = new Vol2FsTubes(), _lash = new Vol2FsTubes(), _roots = new Vol2FsTubes(); Mesh _tendrilMesh, _lashMesh, _rootMesh;
        readonly Vol2FsLeaves _podLeaves = new Vol2FsLeaves(); Mesh _podLeafMesh;
        Mesh _burstMesh; readonly Vol2FsStrips _burstS = new Vol2FsStrips(); List<Vol2FsB1.Stroke> _burst;
        sealed class Bind { public Vol2FsTubes Coil = new Vol2FsTubes(); public Mesh Mesh; public Vol2FsLeaves Leaves = new Vol2FsLeaves(); public Mesh LeafMesh; public Struck S; public float At; }
        readonly List<Bind> _binds = new List<Bind>();
        bool _rootsBuilt; Vector3 _burstAt;
        protected override float SeedU => 0.3f;
        protected override float FireSeconds => HoldSeconds + UnravelSeconds + 0.5f;
        protected override float ExpireSeconds => 1.4f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _vine = Kit.Bark(new Color(0.8f, 1f, 0.6f), new Color(0.35f, 1.3f, 0.3f) * 4f, 0.3f, "fs_vine", "fs_vein");      // r3: brighter jade veins (r2: the vines read black)
            _dry = Kit.Bark(new Color(0.55f, 0.42f, 0.28f), Color.black, 0.2f, "fs_vine", "fs_vein");
            _leafMat = Kit.Leaf(new Color(0.55f, 1f, 0.5f), Jade * 0.6f);
            var leaf = M("fs_leaf", new Color(0.85f, 1f, 0.7f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leaves = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.12f, Size1 = 0.28f, Radius = 0.3f, Gravity = 0.35f, Drag = 1.5f, Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 700 }, root, "Leaves");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 4.5f, 1f)), Life0 = 0.4f, Life1 = 1f, Speed0 = 1f, Speed1 = 5f, Size0 = 0.03f, Size1 = 0.07f, Radius = 0.2f, Drag = 1.4f, Stretch = true, StretchVel = 0.04f, Noise = 1.2f, NoiseFreq = 2f, Max = 600 }, root, "Sparks");
            var drop = M("fs_ink", InkC, 1f, 0f); drop.Edge = new Color(0.5f, 1.4f, 0.35f, 1f); drop.EdgeWidth = 0.16f; drop.NoiseAmt = 0.3f; drop.Soft = 0.1f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.08f, Size1 = 0.3f, Radius = 0.2f, Gravity = 2f, Drag = 0.4f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 300 }, root, "Drops");
            var dirt = M("fs_ink", new Color(0.3f, 0.17f, 0.11f, 1f), 1f, 0f); dirt.NoiseAmt = 0.4f; dirt.Soft = 0.1f;
            _dirt = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dirt), Life0 = 0.5f, Life1 = 1f, Speed0 = 1.5f, Speed1 = 4f, Size0 = 0.05f, Size1 = 0.16f, Radius = 0.2f, Gravity = 2.4f, Tiles = 2, AlphaHoldTo = 0.7f, Max = 400 }, root, "Dirt");
            foreach (var ps in new[] { _leaves, _sparks, _drops, _dirt }) ps.Play(false);
            // the pod: a seed body with a few thorns, built once round the origin (it rides the shaft)
            var pod = new Vol2FsTubes(); int seed = Seed + 5;
            pod.Add(new[] { Vector3.down * 0.07f, Vector3.zero, Vector3.up * 0.07f }, 0.075f, 0.05f, 4).Grow = 1f;
            for (int k = 0; k < 9; k++)
            {
                Vector3 d = new Vector3(Vol2Fs.Hash(seed, k) - 0.5f, Vol2Fs.Hash(seed, k + 40) - 0.5f, Vol2Fs.Hash(seed, k + 80) - 0.5f).normalized;
                var th = pod.Add(new[] { d * 0.05f, d * 0.12f, d * 0.19f }, 0.03f, 0.003f, 4); th.Sharp = 0.08f; th.Sides = 5; th.Grow = 1f;
            }
            var mesh = new Mesh { name = "GeomPod", hideFlags = HideFlags.DontSave }; pod.Write(mesh);
            var go = new GameObject("Pod"); go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh; _podR = go.AddComponent<MeshRenderer>(); _podR.sharedMaterial = _vine;
            _pod = go.transform; _pod.localScale = Vector3.one * 0.01f; go.SetActive(false);
            _glow = Vol2Fs.PointLight(root, "PodGlow", Jade, 2f);
            Vol2Fs.MeshObject(root, "Tendrils", _vine, out _tendrilMesh);
            Vol2Fs.MeshObject(root, "PodLeaves", _leafMat, out _podLeafMesh);
            Vol2Fs.MeshObject(root, "Lash", _vine, out _lashMesh);
            Vol2Fs.MeshObject(root, "Rootlets", _vine, out _rootMesh);
            Vol2Fs.MeshObject(root, "InkBurst", Kit.InkSolid("fs_stroke2", InkC, new Color(0.45f, 1.5f, 0.35f, 1f), 0.12f, 0.45f, 0.3f, 1.3f), out _burstMesh);
        }

        protected override void OnPlant(Pod p)
        {
            _pod.gameObject.SetActive(true);
            Vol2FsB1.Emit(_leaves, SeedPoint(), 12); Vol2FsB1.Emit(_sparks, SeedPoint(), 20);
        }

        /// <summary>The tendrils: three spirals winding up the shaft from the pod, as far as <paramref name="reach"/> (0..1 of the way to the tip).</summary>
        void WriteTendrils(float reach, float twitch, float t)
        {
            _tendrils.Tubes.Clear();
            Vector3 axis = ShaftAxis, n = Vector3.Cross(axis, Vector3.up).normalized; if (n == Vector3.zero) n = Vector3.right;
            Vector3 bn = Vector3.Cross(axis, n);
            for (int v = 0; v < 3; v++)
            {
                var pts = new Vector3[10];
                for (int k = 0; k < 10; k++)
                {
                    float u = k / 9f, at = Mathf.Lerp(SeedU, SeedU + (0.92f - SeedU) * reach, u), ang = u * 9f + v * 2.1f + twitch * Mathf.Sin(t * 35f + v);
                    pts[k] = Shaft(at) + (n * Mathf.Cos(ang) + bn * Mathf.Sin(ang)) * (0.035f + 0.015f * Mathf.Sin(u * 7f + v));
                }
                var tb = _tendrils.Add(pts, 0.016f, 0.006f, 30); tb.Grow = 1f; tb.Sides = 5;
            }
            _tendrils.Write(_tendrilMesh);
        }

        protected override void Fired(Pod p)
        {
            Vector3 c = SeedPoint(), feet = FireFeet; _burstAt = c;
            _pod.gameObject.SetActive(false); _glow.intensity = 5f; _glow.transform.position = c;
            _tendrils.Tubes.Clear(); _tendrils.Write(_tendrilMesh); _podLeaves.Items.Clear(); _podLeaves.Write(_podLeafMesh);
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(CamPos - c));
            _burst = Vol2FsB1.Burst(Seed + 3, 10, side, -FlatN(CamPos - c), 1.0f, 2.2f, 0.12f, 0.3f, 0.5f, 0.2f);
            Vol2FsB1.Emit(_leaves, c, 260); Vol2FsB1.Emit(_sparks, c, 240); Vol2FsB1.Emit(_drops, c, 90);
            // the lash: 12 vines flat over the ground from the feet out past the reach, sinuous, thorny tips
            int seed = Seed + 41;
            for (int k = 0; k < 12; k++)
            {
                float a = k / 12f * Mathf.PI * 2f + Vol2Fs.Hash(seed, k) * 0.4f;
                Vector3 d = Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * F, s = Vector3.Cross(Vector3.up, d);
                float len = Reach * (1.0f + 0.2f * Vol2Fs.Hash(seed, k + 20)), back = Mathf.Abs(Mathf.DeltaAngle(0f, a * Mathf.Rad2Deg)) / 180f;
                var pts = new Vector3[8];
                for (int i = 0; i < 8; i++) { float u = i / 7f; pts[i] = feet + d * (0.25f + len * u) + s * Mathf.Sin(u * 5f + k) * 0.25f + Vector3.up * (0.05f + 0.55f * Mathf.Sin(u * Mathf.PI) * (1f - back) + (u > 0.85f ? 0.3f * (u - 0.85f) * 6f : 0f)); }
                var tb = _lash.Add(pts, 0.075f, 0.02f, 24); tb.Knots = 0.3f; tb.Sides = 6;
            }
            for (int k = 0; k < 12; k++) Vol2FsB1.Emit(_dirt, feet + Quaternion.AngleAxis(k * 30f, Vector3.up) * F * Reach * 0.6f, 6);
        }

        protected override void OnStrike(Pod p, Struck s)
        {
            if (s.World) return;
            var b = new Bind { S = s, At = s.At };
            int seed = Seed + 7 * (_binds.Count + 1);
            var bb = s.T != null ? BodyBounds(s.T, s.T.position, 0.5f) : s.B;
            for (int k = 0; k < 4; k++) { var tb = b.Coil.Add(Vol2FsB4F.Coil(bb, Ground, 10, k * 1.6f + Vol2Fs.Hash(seed, k), 1.4f + 0.4f * Vol2Fs.Hash(seed, k + 3), 0.8f, 0.12f), 0.08f, 0.03f, 40); tb.Knots = 0.3f; tb.Sides = 6; }
            for (int k = 0; k < 16; k++)
            {
                var tb = b.Coil.Tubes[k % 4]; Vector3 at = Vol2FsTubes.CatmullRom(tb.Ctrl, 0.2f + 0.75f * Vol2Fs.Hash(seed, k + 50));
                b.Leaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = at, Normal = FlatN(at - bb.center) + Vector3.up * 0.3f, Up = Vector3.up + Random.onUnitSphere * 0.6f, Size = 0.17f, Grow = 0f, Cell = k % 4 });
            }
            Vol2Fs.MeshObject(Root.transform, "Coil" + _binds.Count, _vine, out b.Mesh);
            Vol2Fs.MeshObject(Root.transform, "CoilLeaves" + _binds.Count, _leafMat, out b.LeafMesh);
            _binds.Add(b);
            Vol2FsB1.Emit(_leaves, bb.center, 60); Vol2FsB1.Emit(_dirt, G(bb.center), 20); Vol2FsB1.Emit(_sparks, bb.center, 50);
        }

        protected override void OnPodEnd(Pod p)
        {
            if (Worked(p)) return;
            if (Withered(p)) { _podR.sharedMaterial = _dry; Vol2FsB1.Emit(_leaves, SeedPoint(), 6); }
            else { _pod.gameObject.SetActive(false); _tendrils.Tubes.Clear(); _tendrils.Write(_tendrilMesh); }
        }

        void BuildRoots(Vector3 from, int n, float len, int seed)
        {
            Vector3 fwd = FlatN(from - Apex); if (Vector3.Distance(G(from), G(Apex)) < 0.3f) fwd = F;
            for (int k = 0; k < n; k++)
            {
                float a = (k / (float)Mathf.Max(1, n - 1) - 0.5f) * 1.2f + (Vol2Fs.Hash(seed, k) - 0.5f) * 0.3f;
                Vector3 d = Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * fwd;
                float L0 = len * (0.7f + 0.5f * Vol2Fs.Hash(seed, k + 5));
                var pts = new Vector3[6];
                for (int i = 0; i < 6; i++) { float u = i / 5f; pts[i] = G(from) + d * L0 * u + Vector3.Cross(Vector3.up, d) * Mathf.Sin(u * 5f + k) * 0.12f + Vector3.up * (0.04f + 0.05f * (1f - u)); }
                var tb = _roots.Add(pts, 0.045f, 0.008f, 18); tb.Sides = 5; tb.Knots = 0.4f;
            }
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos; var p = The;
            _burstS.Clear();
            if (p != null && !Worked(p))
            {
                float life = PodLife(p, t, 0.35f), beat = Beat(p), warn = PodWarn(p, t), ex = PodExit(p, t);
                bool on = p.Started && (!p.Ended || (Withered(p) && ex < 0.95f));
                _pod.gameObject.SetActive(on);
                if (on)
                {
                    Vector3 c = SeedPoint() + (warn > 0f ? Random.insideUnitSphere * 0.015f * warn : Vector3.zero);
                    _pod.SetPositionAndRotation(c, Quaternion.LookRotation(ShaftAxis) * Quaternion.Euler(0f, 0f, t * 30f));
                    float s = (Withered(p) ? 1f - 0.5f * ex : life) * (1.5f + 0.15f * beat + 0.2f * warn);
                    _pod.localScale = Vector3.one * Mathf.Max(0.01f, s);
                    // the tendrils creep up the shaft as the wait runs out (the live expiry), they slacken when it withers
                    WriteTendrils(Mathf.Lerp(0.15f, 1f, 1f - Remain01(p, t)) * life * (1f - ex), warn * 0.3f, t);
                    _podLeaves.Items.Clear();
                    for (int k = 0; k < 4; k++) _podLeaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = c + Quaternion.AngleAxis(k * 90f + t * 20f, ShaftAxis) * Vector3.Cross(ShaftAxis, Vector3.up).normalized * 0.06f, Normal = Vector3.Cross(ShaftAxis, Vector3.up), Up = Quaternion.AngleAxis(k * 90f, ShaftAxis) * Vector3.up, Size = 0.14f, Grow = life * (1f - ex), Cell = k % 4 });
                    _podLeaves.Write(_podLeafMesh);
                    _glow.transform.position = c; _glow.intensity = p.Ended ? 0f : life * (0.5f + 1.5f * beat + warn) * (1f - 0.7f * Held01);
                    if (!p.Ended && Time.frameCount % 5 == 0) Vol2FsB1.EmitV(_sparks, c + Random.insideUnitSphere * 0.12f, Vector3.up * 0.4f, -1f, 0.6f);
                    if (Withered(p) && ex > 0.3f)
                    {
                        if (!_rootsBuilt) { _rootsBuilt = true; BuildRoots(FeetNow() + F * 0.4f, 2, 0.9f, Seed + 91); }
                        foreach (var tb in _roots.Tubes) tb.Grow = Vol2Fs.Smooth(0.3f, 0.75f, ex) * (1f - Vol2Fs.Smooth(0.85f, 1f, ex));
                        _roots.Write(_rootMesh);
                    }
                }
                else _glow.intensity = 0f;
                _burstS.Write(_burstMesh);
                return;
            }
            if (p == null) { _burstS.Write(_burstMesh); return; }
            float a = SinceFire(p, t);
            if (_burst != null) Vol2FsB1.DrawBurst(_burstS, _burst, _burstAt, a, cam, 0.45f, 0.1f);
            _burstS.Write(_burstMesh);
            _glow.intensity = 5f * (1f - Vol2Fs.Smooth(0f, 0.6f, a));
            // the lash ring: out in 0.18 s, lies there while the coils hold, pulled back in at the loosening
            float lash = Vol2Fs.Smooth(0f, 0.18f, a) * (1f - Vol2Fs.Smooth(HoldSeconds * 0.5f, HoldSeconds * 0.5f + 0.5f, a));
            foreach (var tb in _lash.Tubes) tb.Grow = lash;
            _lash.Write(_lashMesh);
            float loose = Vol2Fs.Smooth(HoldSeconds, HoldSeconds + UnravelSeconds, a);
            foreach (var b in _binds)
            {
                float ba = t - b.At, grow = Vol2Fs.Smooth(0.05f, 0.35f, ba);
                foreach (var tb in b.Coil.Tubes) tb.Grow = grow * (1f - loose);
                b.Coil.Write(b.Mesh);
                for (int i = 0; i < b.Leaves.Items.Count; i++) { var lf = b.Leaves.Items[i]; lf.Grow = Vol2Fs.Smooth(0.15f + 0.01f * i, 0.45f + 0.01f * i, ba) * (1f - loose); b.Leaves.Items[i] = lf; }
                b.Leaves.Write(b.LeafMesh);
                if (ba < HoldSeconds && Time.frameCount % 5 == 0) Vol2FsB1.Emit(_sparks, CentreNow(b.S) + Random.insideUnitSphere * 0.4f, 1);
            }
            if (a > HoldSeconds && !_rootsBuilt)
            {
                _rootsBuilt = true;
                if (_binds.Count == 0) BuildRoots(FireFeet + F * 0.5f, 4, 1.2f, Seed + 61);
                for (int i = 0; i < _binds.Count; i++) BuildRoots(FeetOf(_binds[i].S), 4, 1.3f, Seed + 61 + i * 9);
            }
            if (_rootsBuilt)
            {
                float r = Vol2Fs.Smooth(HoldSeconds, HoldSeconds + 0.6f, a), sink = Vol2Fs.Smooth(HoldSeconds + UnravelSeconds, HoldSeconds + UnravelSeconds + 0.4f, a);
                foreach (var tb in _roots.Tubes) tb.Grow = r * (1f - sink);
                _roots.Write(_rootMesh);
            }
        }
    }
}
