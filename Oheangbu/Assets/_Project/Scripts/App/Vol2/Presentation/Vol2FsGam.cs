using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>감 (wood, ㅏ path, 심는다) - free showcase batch 5, concept gam_a (a thorny burr-seed caught in the fur of every bear the path passed, curled
    /// tendrils and a slow jade heartbeat; on the first bear the burr explodes into whipping vines and leaves that coil round its legs and pin it, while the burrs
    /// on the farther bears still pulse). Executor facts only: the "Plant" contact = the burr lodges (a puff of leaves) -> the wait (the jade pulse quickens to
    /// the live expiry, the ink dial burns down, the last 0.4 s the tendrils twitch) -> Consumed = the burst (vines whip out, an ink-and-leaf splash, the coils
    /// hold for the bind's 2 s - the status sink owns the slow, the look mirrors it - then loosen into rootlets reaching forward along the ground: CSV
    /// 끝: 잔뿌리로 풀림(앞으로 뻗으며)); Expired = the burr browns, drops off and two short rootlets creep out and sink; HitCancelled = it drops at once.</summary>
    public sealed class Vol2FsGam : Vol2FsSeedPath
    {
        static readonly Color Jade = new Color(0.4f, 1f, 0.45f);
        const float HoldSeconds = 2f, UnravelSeconds = 0.9f;
        ParticleSystem _leaves, _sparks, _drops;
        Material _vine, _dry, _leafMat;
        Mesh _burstMesh; readonly Vol2FsStrips _burstS = new Vol2FsStrips();
        protected override float MarkLift => 0.3f;
        protected override float DialR => 0.42f;
        protected override float FireSeconds => HoldSeconds + UnravelSeconds + 0.4f;
        protected override float ExpireSeconds => 1.3f;

        sealed class Look
        {
            public Transform Burr; public MeshRenderer BurrR; public Light Glow;
            public Vol2FsTubes Coil = new Vol2FsTubes(), Whips = new Vol2FsTubes(), Roots = new Vol2FsTubes(); public Mesh CoilMesh, WhipMesh, RootMesh; public MeshRenderer RootR;
            public Vol2FsLeaves Leaves = new Vol2FsLeaves(); public Mesh LeafMesh;
            public List<Vol2FsB1.Stroke> Burst; public Vector3 BurstAt, Fall; public bool Built, RootsBuilt;
        }

        protected override void OnSetup()
        {
            var root = Root.transform;
            _vine = Kit.Bark(new Color(0.8f, 1f, 0.6f), new Color(0.35f, 1.3f, 0.3f) * 4f, 0.3f, "fs_vine", "fs_vein");      // r3: brighter jade veins (r2: the whips read black)
            _dry = Kit.Bark(new Color(0.55f, 0.42f, 0.28f), Color.black, 0.2f, "fs_vine", "fs_vein");
            _leafMat = Kit.Leaf(new Color(0.55f, 1f, 0.5f), Jade * 0.6f);
            var leaf = M("fs_leaf", new Color(0.85f, 1f, 0.7f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leaves = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.12f, Size1 = 0.28f, Radius = 0.3f, Gravity = 0.35f, Drag = 1.5f, Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 600 }, root, "Leaves");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 4.5f, 1f)), Life0 = 0.4f, Life1 = 1f, Speed0 = 1f, Speed1 = 5f, Size0 = 0.03f, Size1 = 0.07f, Radius = 0.25f, Drag = 1.4f, Stretch = true, StretchVel = 0.04f, Noise = 1.2f, NoiseFreq = 2f, Max = 500 }, root, "Sparks");
            var drop = M("fs_ink", InkC, 1f, 0f); drop.Edge = new Color(0.5f, 1.4f, 0.35f, 1f); drop.EdgeWidth = 0.16f; drop.NoiseAmt = 0.3f; drop.Soft = 0.1f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.08f, Size1 = 0.3f, Radius = 0.2f, Gravity = 2f, Drag = 0.4f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 300 }, root, "Drops");
            foreach (var ps in new[] { _leaves, _sparks, _drops }) ps.Play(false);
            Vol2Fs.MeshObject(root, "InkBurst", Kit.InkSolid("fs_stroke2", InkC, new Color(0.45f, 1.5f, 0.35f, 1f), 0.12f, 0.45f, 0.3f, 1.3f), out _burstMesh);
        }

        Look L(Pod p) => p.Data as Look;

        protected override void Planted(Pod p)
        {
            var root = Root.transform; var l = new Look(); p.Data = l;
            // the burr: a knot of thorns round a seed, built once in local space round the origin (the object rides the host)
            var burr = new Vol2FsTubes();
            int seed = Seed + p.Index * 31;
            for (int k = 0; k < 14; k++)
            {
                Vector3 d = new Vector3(Vol2Fs.Hash(seed, k) - 0.5f, Vol2Fs.Hash(seed, k + 40) * 0.9f - 0.2f, Vol2Fs.Hash(seed, k + 80) - 0.5f).normalized;
                var th = burr.Add(new[] { d * 0.05f, d * 0.16f, d * 0.27f }, 0.045f, 0.004f, 4); th.Sharp = 0.1f; th.Sides = 5; th.Grow = 1f;
            }
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI * 0.5f + Vol2Fs.Hash(seed, k + 120);
                var pts = new Vector3[7];
                for (int i = 0; i < 7; i++) { float u = i / 6f, r = 0.1f + 0.28f * u, ang = a + u * 4.2f; pts[i] = new Vector3(Mathf.Cos(ang) * r, 0.05f + 0.25f * u - 0.18f * u * u, Mathf.Sin(ang) * r); }
                var tn = burr.Add(pts, 0.022f, 0.006f, 14); tn.Grow = 1f; tn.Sides = 5;
            }
            burr.Add(new[] { Vector3.down * 0.06f, Vector3.zero, Vector3.up * 0.06f }, 0.1f, 0.07f, 4).Grow = 1f;      // the seed body
            var mesh = new Mesh { name = "GamBurr", hideFlags = HideFlags.DontSave };
            burr.Write(mesh);
            var go = new GameObject("Burr" + p.Index); go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            l.BurrR = go.AddComponent<MeshRenderer>(); l.BurrR.sharedMaterial = _vine;
            l.Burr = go.transform; l.Burr.localScale = Vector3.one * 0.01f;
            l.Glow = Vol2Fs.PointLight(go.transform, "Glow", Jade, 2.2f);
            Vol2FsB1.Emit(_leaves, HostTop(p), 10); Vol2FsB1.Emit(_sparks, HostTop(p), 18);
            Vol2Fs.MeshObject(root, "Coil" + p.Index, _vine, out l.CoilMesh);
            Vol2Fs.MeshObject(root, "Whips" + p.Index, _vine, out l.WhipMesh);
            l.RootR = Vol2Fs.MeshObject(root, "Roots" + p.Index, _vine, out l.RootMesh);
            Vol2Fs.MeshObject(root, "CoilLeaves" + p.Index, _leafMat, out l.LeafMesh);
        }

        protected override void OnFire(Pod p)
        {
            var l = L(p); if (l == null) return;
            var b = HostNow(p); Vector3 c = MarkPoint(p);
            l.BurstAt = c; l.Built = true;
            Vector3 side = Vector3.Cross(Vector3.up, (CamPos - c).normalized).normalized;
            l.Burst = Vol2FsB1.Burst(Seed + p.Index, 9, side, -FlatN(CamPos - c), 1.6f, 3.2f, 0.2f, 0.45f, 0.5f, 0.2f);
            Vol2FsB1.Emit(_leaves, c, 320); Vol2FsB1.Emit(_sparks, c, 220); Vol2FsB1.Emit(_drops, c, 90);
            float ground = Ground; int seed = Seed + p.Index * 7;
            // the coils: vines from the burr down round the body to the ground (Vol2FsB4F.Coil = a helix round the body's bounds)
            for (int k = 0; k < 5; k++)
            {
                var ctrl = Vol2FsB4F.Coil(b, ground, 9, k * 1.26f + Vol2Fs.Hash(seed, k), 1.6f + 0.5f * Vol2Fs.Hash(seed, k + 9), 0.95f, 0.1f);
                System.Array.Reverse(ctrl); ctrl[0] = c;                   // from the burr down
                var tb = l.Coil.Add(ctrl, 0.11f, 0.045f, 40); tb.Knots = 0.3f; tb.Sides = 6;
            }
            // the whips: lashing out of the burst and falling back
            for (int k = 0; k < 12; k++)
            {
                float a = k / 12f * Mathf.PI * 2f + Vol2Fs.Hash(seed, k + 30);
                Vector3 d = new Vector3(Mathf.Cos(a), 0.35f + 0.5f * Vol2Fs.Hash(seed, k + 50), Mathf.Sin(a)).normalized;
                Vector3 bend = Vector3.Cross(d, Vector3.up).normalized * 0.4f;
                var tb = l.Whips.Add(new[] { c, c + d * 1.2f + bend * 0.3f, c + d * 2.4f + bend * 0.8f, c + d * 3.3f + bend * 0.5f + Vector3.down * 0.7f }, 0.1f, 0.016f, 20); tb.Sides = 5;
            }
            for (int k = 0; k < 26; k++)
            {
                var tb = l.Coil.Tubes[k % 5]; Vector3 at = Vol2FsTubes.CatmullRom(tb.Ctrl, 0.15f + 0.8f * Vol2Fs.Hash(seed, k + 70));
                l.Leaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = at, Normal = FlatN(at - b.center) + Vector3.up * 0.3f, Up = Random.onUnitSphere + Vector3.up, Size = 0.18f + 0.1f * Vol2Fs.Hash(seed, k + 90), Grow = 0f, Cell = k % 4 });
            }
            l.Burr.gameObject.SetActive(false); l.Glow.intensity = 0f;
        }

        protected override void OnPodEnd(Pod p)
        {
            var l = L(p); if (l == null || Worked(p)) return;
            l.Fall = HostTop(p);
            if (Withered(p)) { Vol2FsB1.Emit(_leaves, l.Fall, 6); l.BurrR.sharedMaterial = _dry; }
            else l.Burr.gameObject.SetActive(false);
        }

        void BuildRoots(Pod p, Look l, Vector3 feet, int n, float len)
        {
            l.RootsBuilt = true; int seed = Seed + p.Index * 13;
            Vector3 fwd = FlatN(feet - Apex);
            for (int k = 0; k < n; k++)
            {
                float a = (k / (float)Mathf.Max(1, n - 1) - 0.5f) * 1.4f + (Vol2Fs.Hash(seed, k) - 0.5f) * 0.3f;
                Vector3 d = Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * fwd;
                float L0 = len * (0.7f + 0.5f * Vol2Fs.Hash(seed, k + 5));
                var pts = new Vector3[6];
                for (int i = 0; i < 6; i++) { float u = i / 5f; pts[i] = G(feet) + d * L0 * u + Vector3.Cross(Vector3.up, d) * Mathf.Sin(u * 5f + k) * 0.12f + Vector3.up * (0.04f + 0.06f * (1f - u)); }
                var tb = l.Roots.Add(pts, 0.05f, 0.008f, 18); tb.Sides = 5; tb.Knots = 0.4f;
            }
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos;
            _burstS.Clear();
            foreach (var p in Pods)
            {
                var l = L(p); if (l == null) continue;
                float life = PodLife(p, t, 0.3f);
                if (!Worked(p))
                {
                    float beat = Beat(p), warn = PodWarn(p, t), ex = PodExit(p, t);
                    Vector3 at = MarkPoint(p);
                    if (Withered(p)) at = Vector3.Lerp(l.Fall, G(l.Fall) + Vector3.up * 0.1f, Vol2Fs.Smooth(0f, 0.5f, ex));
                    l.Burr.position = at + (warn > 0f ? Random.insideUnitSphere * 0.03f * warn : Vector3.zero);
                    l.Burr.rotation = Quaternion.Euler(0f, t * 25f + p.Index * 70f, warn * 20f * Mathf.Sin(t * 30f));
                    float s = Mathf.Max(0.01f, (Withered(p) ? 1f : life) * (2.4f + 0.25f * beat + 0.35f * warn) * (1f - 0.4f * ex));
                    l.Burr.localScale = Vector3.one * s;
                    l.Burr.gameObject.SetActive(p.Started && (!p.Ended || (Withered(p) && ex < 0.99f)));
                    l.Glow.intensity = p.Ended ? 0f : life * (0.6f + 1.6f * beat + 1.2f * warn) * (1f - 0.7f * Held01);
                    if (!p.Ended && Time.frameCount % 5 == 0) Vol2FsB1.EmitV(_sparks, at + Random.insideUnitSphere * 0.2f, Vector3.up * 0.4f, -1f, 0.6f);
                    if (Withered(p) && ex > 0.35f)
                    {
                        if (!l.RootsBuilt) BuildRoots(p, l, HostFeet(p), 2, 0.7f);
                        foreach (var tb in l.Roots.Tubes) tb.Grow = Vol2Fs.Smooth(0.35f, 0.8f, ex) * (1f - Vol2Fs.Smooth(0.85f, 1f, ex));
                        l.Roots.Write(l.RootMesh);
                    }
                    continue;
                }
                // worked: the burst, the hold, the unravelling
                float a = SinceFire(p, t);
                if (l.Burst != null) Vol2FsB1.DrawBurst(_burstS, l.Burst, l.BurstAt, a, cam, 0.45f, 0.1f);
                float grow = Vol2Fs.Smooth(0.02f, 0.28f, a), loose = Vol2Fs.Smooth(HoldSeconds, HoldSeconds + UnravelSeconds, a);
                foreach (var tb in l.Coil.Tubes) tb.Grow = grow * (1f - loose);
                l.Coil.Write(l.CoilMesh);
                float whip = Vol2Fs.Smooth(0f, 0.12f, a) * (1f - Vol2Fs.Smooth(0.35f, 0.7f, a));
                foreach (var tb in l.Whips.Tubes) tb.Grow = whip;
                l.Whips.Write(l.WhipMesh);
                for (int i = 0; i < l.Leaves.Items.Count; i++) { var lf = l.Leaves.Items[i]; lf.Grow = Vol2Fs.Smooth(0.1f + 0.01f * i, 0.4f + 0.01f * i, a) * (1f - loose); l.Leaves.Items[i] = lf; }
                l.Leaves.Write(l.LeafMesh);
                if (a > HoldSeconds && !l.RootsBuilt) BuildRoots(p, l, HostFeet(p), 6, 1.4f);
                if (l.RootsBuilt)
                {
                    float r = Vol2Fs.Smooth(HoldSeconds, HoldSeconds + 0.6f, a), sink = Vol2Fs.Smooth(HoldSeconds + UnravelSeconds, HoldSeconds + UnravelSeconds + 0.4f, a);
                    foreach (var tb in l.Roots.Tubes) tb.Grow = r * (1f - sink);
                    l.Roots.Write(l.RootMesh);
                }
                if (a < HoldSeconds && Time.frameCount % 4 == 0) Vol2FsB1.Emit(_sparks, HostNow(p).center + Random.insideUnitSphere * 0.4f, 1);
            }
            _burstS.Write(_burstMesh);
        }
    }
}
