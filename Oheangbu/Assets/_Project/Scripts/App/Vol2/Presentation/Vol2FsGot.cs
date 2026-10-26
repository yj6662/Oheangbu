using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>곳 (wood, ㅗ fan, 가른다) - free showcase batch 6, concept got_b (a fan of whip-thin jade lashes cracks out from the brush across the fan, each lash leaving
    /// a gouge in the ground, the wall cut through, both bears stripped - glowing jade bands wound round them - and bound). Executor facts only: the launch = the
    /// one cut: seven lashes whip out of the brush tip one after another across the fan (the sweep, right edge to left - a presentation of the one tick), each
    /// lands at the far edge with a crack of jade sparks and leaves its radial gouge (the base), then curls back and fades; the split wall parts (the shared
    /// halves, leaves out of the cut); every stripped body is wound by two glowing jade bands for its strip (the book's live end) and its feet bound by the vines
    /// of the body action (2 s, then rootlets reaching forward: CSV 끝).</summary>
    public sealed class Vol2FsGot : Vol2FsCutFan
    {
        static readonly Color Jade = new Color(0.4f, 1f, 0.5f);
        const int Lashes = 7;
        ParticleSystem _leaves, _sparks;
        Mesh _lashMesh, _lashCoreMesh, _bandMesh; readonly Vol2FsStrips _lash = new Vol2FsStrips(), _lashCore = new Vol2FsStrips(), _band = new Vol2FsStrips();
        sealed class Lash { public float Ang, At, Len, Bow, Rnd; public bool Landed; }
        readonly List<Lash> _lashes = new List<Lash>();
        protected override Color CutGlow => new Color(0.45f, 1.5f, 0.55f);
        protected override int GougeCount => Lashes;
        protected override float SweepSeconds => 0.12f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            var leaf = M("fs_leaf", new Color(0.85f, 1f, 0.7f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leaves = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 0.9f, Life1 = 1.8f, Speed0 = 1.5f, Speed1 = 5f, Size0 = 0.1f, Size1 = 0.24f, Gravity = 0.4f, Drag = 1.4f, Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 600 }, root, "Leaves");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 4.5f, 1f)), Life0 = 0.3f, Life1 = 0.8f, Speed0 = 2f, Speed1 = 7f, Size0 = 0.025f, Size1 = 0.06f, Drag = 1.3f, Gravity = 0.4f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.3f, Max = 900 }, root, "Sparks");
            foreach (var ps in new[] { _leaves, _sparks }) ps.Play(false);
            var l = M("fs_streak", Jade, 4.5f, 1f); l.NoiseAmt = 0.2f; l.Soft = 0.03f; l.Erode = 0.1f;
            Vol2Fs.MeshObject(root, "Lashes", Kit.Mat(l), out _lashMesh);
            Vol2Fs.MeshObject(root, "LashInk", Kit.InkSolid("fs_stroke2", InkC, new Color(0.45f, 1.5f, 0.4f, 1f), 0.14f, 0.4f, 0.3f, 1.3f), out _lashCoreMesh);
            var b = M("fs_stroke2", Jade, 2.6f, 1f); b.NoiseAmt = 0.25f; b.Soft = 0.05f;
            Vol2Fs.MeshObject(root, "Bands", Kit.Mat(b), out _bandMesh);
        }

        protected override void OnLaunch(long nowUs)
        {
            base.OnLaunch(nowUs);
            // one lash per gouge, cracking out when the sweep passes its angle
            foreach (var g in Gouges)
            {
                float ang = AngleOf(g.B);
                _lashes.Add(new Lash { Ang = ang, At = g.OpenAt - 0.04f, Len = Vector3.Distance(G(Tip()), g.B), Bow = (Vol2Fs.Hash(Seed, _lashes.Count + 5) - 0.5f) * 1.2f, Rnd = Vol2Fs.Hash(Seed, _lashes.Count + 9) });
            }
        }

        protected override void Sweep(float t, float k, float prevK)
        {
            Vector3 tip = Tip();
            for (int i = 0; i < 3; i++) Vol2FsB1.EmitV(_sparks, tip, (Dir(SweepAng(k)) * 4f + Random.insideUnitSphere * 1.5f), -1f, -1f);
        }

        Vector3 LashPoint(Lash l, Vector3 from, Vector3 to, float u)
        {
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(to - from));
            return Vector3.Lerp(from, to, u) + side * l.Bow * Mathf.Sin(u * Mathf.PI) + Vector3.up * (0.9f * Mathf.Sin(u * Mathf.PI) * (1f - u * 0.6f));
        }

        protected override void OnCleave(Cleave c)
        {
            Vector3 at = c.Centre + Vector3.up * 0.5f;
            Vol2FsB1.Emit(_leaves, at, 30); Vol2FsB1.Emit(_sparks, at, 60);
        }

        protected override void OnStrip(Strip s)
        {
            var b = BodyNow(s);
            Vol2FsB1.Emit(_leaves, b.center, 40); Vol2FsB1.Emit(_sparks, b.center, 80);
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos;
            _lash.Clear(); _lashCore.Clear(); _band.Clear();
            Vector3 tip = Tip();
            foreach (var l in _lashes)
            {
                float a = t - l.At; if (a < 0f) continue;
                Vector3 to = InFan(0.97f, l.Ang, 0.05f);
                // out: tip -> far edge in 0.1 s; hold; curl back toward the brush and thin away
                float reach = Vol2Fs.Smooth(0f, 0.1f, a), back = Vol2Fs.Smooth(0.6f, 1.2f, a), life = 1f - Vol2Fs.Smooth(0.8f, 1.4f, a);      // r2: held, then a slow curl back
                if (life <= 0.01f) continue;
                if (!l.Landed && reach >= 0.99f)
                {
                    l.Landed = true;
                    for (int i = 0; i < 18; i++) Vol2FsB1.EmitV(_sparks, to, (Random.onUnitSphere + Vector3.up * 0.6f).normalized * Random.Range(2f, 6f), -1f, -1f);
                    Vol2FsB1.Emit(_leaves, to + Vector3.up * 0.3f, 4);
                    Vol2FsB1.EmitV(PInk, to, Vector3.up * 2f + Random.insideUnitSphere, -1f, -1f);
                }
                int n = 16; var pts = new Vector3[n]; var wd = new float[n]; var wc = new float[n]; var al = new float[n];
                float u0 = back, u1 = reach;
                if (u1 - u0 < 0.02f) continue;
                for (int i = 0; i < n; i++)
                {
                    float v = i / (float)(n - 1), u = Mathf.Lerp(u0, u1, v);
                    pts[i] = LashPoint(l, tip, to, u);
                    float taper = Mathf.Sin(Mathf.Clamp01(v * 0.9f + 0.05f) * Mathf.PI) * (1f - 0.5f * u);
                    wd[i] = 0.34f * taper + 0.05f; wc[i] = 0.12f * taper + 0.03f;      // r2: thicker (r1 read as hairlines) al[i] = life * (0.45f + 0.55f * v);
                }
                _lash.Strip(pts, wd, null, Color.white, al, l.Rnd, cam, u0, u1);
                _lashCore.Strip(pts, wc, null, Color.white, al, l.Rnd + 0.3f, cam, u0, u1);
                // the phantom of the lash, lagging a little (Phantom Blade principle 5)
                if (a < 1f)
                {
                    var pp = new Vector3[n]; var pa = new float[n];
                    for (int i = 0; i < n; i++) { float u = Mathf.Lerp(u0, Mathf.Max(u0, u1 - 0.12f), i / (float)(n - 1)); pp[i] = LashPoint(l, tip, to, u) + Vector3.up * 0.12f; pa[i] = al[i] * 0.35f * (1f - a); }
                    _lash.Strip(pp, wd, null, Color.white, pa, l.Rnd + 0.6f, cam, u0, u1);
                }
            }
            // r2: the sweep itself as a jade crescent over the lashes, lingering with two phantoms
            SweepArc(_lash, _lash, t, Depth * 0.55f, 0.55f, 0.3f, 0.5f, 0.7f, 0.2f, 0.15f, 0.9f);
            // the jade bands round every stripped body, for its strip
            foreach (var s in Strips)
            {
                float life = StripLife(s, t); if (life <= 0.01f) continue;
                var b = BodyNow(s); float warn = StripWarn(s, t);
                float flick = warn > 0f ? (Mathf.Sin(t * 40f) > 0f ? 1f : 0.45f) : 1f;
                float rx = b.extents.x + 0.14f, rz = b.extents.z + 0.14f, spin = t * 2.2f + s.Index;
                Vol2FsB4A.Wrap(_band, b.center, rx, rz, b.center.y - b.extents.y * 0.35f, spin, Mathf.PI * 1.6f, 0.07f, life * flick, 0.3f + s.Index * 0.2f, cam, b.extents.y * 0.5f, 22, 0.12f);
                Vol2FsB4A.Wrap(_band, b.center, rx * 1.05f, rz * 1.05f, b.center.y + b.extents.y * 0.2f, -spin + 2f, Mathf.PI * 1.4f, 0.06f, life * flick * 0.85f, 0.6f + s.Index * 0.2f, cam, b.extents.y * 0.4f, 22, -0.1f);
                if (Time.frameCount % 4 == 0 && !s.Ended) Vol2FsB1.Emit(_sparks, b.center + Random.insideUnitSphere * 0.5f, 1);
            }
            _lash.Write(_lashMesh); _lashCore.Write(_lashCoreMesh); _band.Write(_bandMesh);
        }
    }
}
