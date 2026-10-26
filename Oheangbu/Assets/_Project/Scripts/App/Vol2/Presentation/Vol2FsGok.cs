using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>곡 (wood, ㅗ fan, 남긴다 PEAK) - free showcase batch 3, concept gok_a (across the whole fan a young GROVE springs up - saplings, small twisted trees and
    /// leafy vines, waist to head high - its outer edge visibly spreading further with new shoots; the bears inside are wrapped by vines and held; leaves drifting,
    /// a soft jade glow in the undergrowth). Unlike 고's one-time sweep of bamboo shoots it is a forest that STAYS and GROWS. Beats, executor facts only: the launch =
    /// a green sweep of ink and leaves crosses the fan and the trees sprout in its order -> the residue material's fan footprint grows to area x1.5 over its life
    /// (Vol2Footprint.ScaleAt - the executor's own number): saplings sprout at the advancing edge as it passes them -> every body the executor binds (the strike, and
    /// each body that walks in: "Residue" contacts) is wrapped by vines for the 1.5 s bind (a mirror) -> the last 0.4 s = the grove sheds leaves -> natural expiry
    /// (CSV 목 끝: 잔뿌리로 풀림, 앞으로 뻗으며) = the trees sink and unravel into rootlets reaching forward over the ground.</summary>
    public sealed class Vol2FsGok : Vol2FsLeave
    {
        static readonly Color Jade = new Color(0.5f, 1f, 0.4f), Ink = new Color(0.03f, 0.035f, 0.025f);
        ParticleSystem _charge, _leafFx, _motes, _dirt, _drops; Light _chargeLight, _glowL;
        readonly Vol2FsTubes _trees = new Vol2FsTubes(), _wraps = new Vol2FsTubes(), _roots = new Vol2FsTubes();
        readonly Vol2FsLeaves _leaves = new Vol2FsLeaves();
        Mesh _treeMesh, _leafMesh, _wrapMesh, _rootMesh, _mossMesh, _sweepMesh; readonly Vol2FsStrips _moss = new Vol2FsStrips(), _sweep = new Vol2FsStrips();
        sealed class Tree { public float U, V, At; public int First, Count, LeafFirst, LeafCount; public bool Sprouted; public float SproutAt = -1f; }
        readonly List<Tree> _grove = new List<Tree>();
        readonly List<(Struck S, int First, int Count, float At)> _bound = new List<(Struck, int, int, float)>();
        bool _built, _rootsMade;
        protected override float ExpireSeconds => 1.4f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);
        Material BarkMat => Kit.Bark(new Color(0.78f, 0.68f, 0.5f), new Color(0.16f, 0.3f, 0.08f), 0.18f, "fs_bark", "fs_bark");      // r4: lifted out of black

        protected override void OnBegin()
        {
            var root = Root.transform;
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 3f, 1f)), Rate = 60f, Loop = true, Life0 = 0.2f, Life1 = 0.35f, Speed0 = -1.5f, Speed1 = -2.5f, Size0 = 0.05f, Size1 = 0.12f, Radius = 0.55f, World = false, Max = 80 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", Jade, 3f);
            _charge.transform.position = Tip(); _charge.Play(false);
            var leaf = M("fs_leaf", new Color(0.9f, 1f, 0.75f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leafFx = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 1.4f, Life1 = 2.6f, Speed0 = 0.8f, Speed1 = 3f, Size0 = 0.1f, Size1 = 0.22f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.4f, Gravity = 0.25f, Drag = 1.6f,
                Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 240f, AlphaHoldTo = 0.75f, Max = 700 }, root, "Leaves");
            _motes = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 3.2f, 1f)), Life0 = 0.9f, Life1 = 1.6f, Speed0 = 0.1f, Speed1 = 0.4f, Size0 = 0.03f, Size1 = 0.08f, Radius = 0.3f, VelOverLife = new Vector3(0f, 0.5f, 0f), Noise = 0.6f, Max = 400 }, root, "Motes");
            var dirt = M("fs_ink", new Color(0.32f, 0.17f, 0.11f, 1f), 1f, 0f); dirt.NoiseAmt = 0.4f; dirt.Soft = 0.1f;
            _dirt = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dirt), Life0 = 0.5f, Life1 = 0.9f, Speed0 = 1.5f, Speed1 = 4f, Size0 = 0.05f, Size1 = 0.16f, Shape = ParticleSystemShapeType.Cone, Angle = 35f, Radius = 0.15f, ShapeRot = new Vector3(-90f, 0f, 0f), Gravity = 2.4f, Tiles = 2, AlphaHoldTo = 0.7f, Max = 600 }, root, "Dirt");
            var drop = M("fs_ink", Ink, 1f, 0f); drop.Edge = new Color(0.6f, 1.4f, 0.3f, 1f); drop.EdgeWidth = 0.15f; drop.Soft = 0.1f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.6f, Life1 = 1.1f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.08f, Size1 = 0.26f, Radius = 0.3f, Gravity = 1.8f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 300 }, root, "InkDrops");
            foreach (var ps in new[] { _leafFx, _motes, _dirt, _drops }) ps.Play(false);
            var tr = Vol2Fs.MeshObject(root, "Grove", BarkMat, out _treeMesh); tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            Vol2Fs.MeshObject(root, "GroveLeaves", Kit.Leaf(new Color(0.7f, 1f, 0.45f), new Color(0.3f, 0.8f, 0.12f)), out _leafMesh);
            Vol2Fs.MeshObject(root, "Wraps", BarkMat, out _wrapMesh);
            Vol2Fs.MeshObject(root, "Rootlets", BarkMat, out _rootMesh);
            var moss = M("fs_moss", Color.white, 1f, 0f); moss.Soft = 0.001f; moss.NoiseAmt = 0.35f; moss.Erode = 0.25f; moss.Edge = new Color(0.12f, 0.3f, 0.06f, 1f); moss.EdgeWidth = 0.04f; moss.Opacity = 0.7f;      // r3: no glowing edge (r2: bright green boards)
            Vol2Fs.MeshObject(root, "Moss", Kit.Mat(moss), out _mossMesh);
            Vol2Fs.MeshObject(root, "Sweep", Kit.InkSolid("fs_stroke2", Ink, new Color(0.9f, 2.2f, 0.45f, 1f), 0.12f, 0.5f, 0.28f, 1.6f), out _sweepMesh);
            _glowL = Vol2Fs.PointLight(root, "GroveLight", Jade, 6f);
        }

        protected override void OnCharge(float t, float k) { _charge.transform.position = Tip(); _chargeLight.intensity = 3f * k; }
        protected override void OnLaunch(long nowUs) { _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f; }
        protected override void OnHit(long nowUs) { Build(); }

        void Build()
        {
            if (_built) return;
            _built = true;
            int seed = Seed + 101;
            float maxScale = Mathf.Sqrt(1.5f);
            // 30 trees over the fan out to the grown edge (u up to sqrt 1.5): the outer ones sprout only when the footprint's edge reaches them
            for (int k = 0; k < 30; k++)
            {
                float u = Mathf.Lerp(0.3f, maxScale * 0.97f, Mathf.Sqrt(Vol2Fs.Hash(seed, k))), v = (Vol2Fs.Hash(seed, k + 50) * 2f - 1f) * 0.92f;
                var tr = new Tree { U = u, V = v, At = 0.05f + 0.25f * (v * 0.5f + 0.5f) + 0.05f * u, First = _trees.Tubes.Count, LeafFirst = _leaves.Items.Count };
                Vector3 b = Vol2FsB3.InFan(Apex, F, Depth, Half, u, v, Ground);
                float h = Mathf.Lerp(1.2f, 2.7f, Vol2Fs.Hash(seed, k + 100)) * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(u));
                Vector3 lean = (Vector3.up * 2f + (b - Apex).normalized * 0.4f + Random.insideUnitSphere * 0.3f).normalized;
                Vector3 b1 = b + lean * h * 0.45f + Vector3.Cross(Vector3.up, lean) * 0.12f, b2 = b + lean * h;
                var trunk = _trees.Add(new[] { b + Vector3.down * 0.1f, b1, b2 }, Mathf.Lerp(0.06f, 0.11f, h / 2.2f), 0.02f, 12); trunk.Knots = 0.5f; trunk.Sharp = 0.25f; trunk.Sides = 6;
                for (int j = 0; j < 3; j++)
                {
                    float a = (j / 3f + Vol2Fs.Hash(seed, k * 3 + j + 200)) * Mathf.PI * 2f;
                    Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 s0 = Vector3.Lerp(b1, b2, 0.3f + 0.25f * j), s1 = s0 + o * h * 0.3f + Vector3.up * h * 0.2f;
                    var br = _trees.Add(new[] { s0, s1, s1 + o * h * 0.1f + Vector3.up * h * 0.12f }, 0.035f, 0.01f, 6); br.Sharp = 0.2f; br.Sides = 5;
                    for (int q = 0; q < 3; q++)
                    {
                        Vector3 nrm = Quaternion.AngleAxis(Vol2Fs.Hash(seed, k * 9 + j * 3 + q) * 360f, Vector3.up) * Vector3.forward;
                        _leaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = Vector3.Lerp(s0, s1, 0.5f + 0.25f * q) + Vector3.up * 0.05f * q, Normal = nrm, Up = (Vector3.up + nrm * 0.5f).normalized, Size = 0.36f, Grow = 0f, Cell = (k + j + q) % 4 });
                    }
                }
                tr.Count = _trees.Tubes.Count - tr.First; tr.LeafCount = _leaves.Items.Count - tr.LeafFirst;
                _grove.Add(tr);
            }
            // the sweep: a low arc of ink with a jade rim crossing the fan, leaves and soil thrown
            for (int k = 0; k < 14; k++) { var p = Vol2FsB3.InFan(Apex, F, Depth, Half, 0.55f + 0.35f * Vol2Fs.Hash(seed, k + 400), (k / 13f) * 2f - 1f, Ground); Vol2FsB1.Emit(_leafFx, p + Vector3.up * 0.4f, 8); Vol2FsB1.Emit(_dirt, p, 6); }
            _glowL.transform.position = Vol2FsB3.InFan(Apex, F, Depth, Half, 0.6f, 0f, Ground) + Vector3.up * 1f;
        }

        void Bind(Struck s)
        {
            int first = _wraps.Tubes.Count;
            Vector3 c = s.B.center; float rx = Mathf.Min(0.55f, s.B.extents.x + 0.1f), rz = Mathf.Min(0.55f, s.B.extents.z + 0.1f), hy = s.B.size.y;      // r2: snug (r1: a barrel cage round the bear)
            float g = s.Feet.y;
            for (int j = 0; j < 4; j++)
            {
                var pts = new Vector3[10];
                for (int k = 0; k < 10; k++)
                {
                    float u = k / 9f, a = j * 1.57f + u * 9f;
                    pts[k] = new Vector3(c.x + Mathf.Cos(a) * rx, g + 0.05f + u * hy * 0.85f, c.z + Mathf.Sin(a) * rz);
                }
                var tb = _wraps.Add(pts, 0.06f, 0.02f, 30); tb.Knots = 0.4f; tb.Sharp = 0.3f; tb.Sides = 6;
            }
            _bound.Add((s, first, _wraps.Tubes.Count - first, s.At));
            Vol2FsB1.Emit(_leafFx, c, 30); Vol2FsB1.Emit(_drops, c, 20);
        }
        protected override void OnStruck(Struck s) { Bind(s); }
        protected override void OnTouch(Struck s) { if (!_bound.Exists(b => b.S.Id == s.Id && Mathf.Abs(b.At - s.At) < 0.1f)) Bind(s); }
        protected override void OnResidueEnd()
        {
            if (!NaturalEnd) { foreach (var g in _grove) if (g.Sprouted) Vol2FsB1.Emit(_leafFx, Vol2FsB3.InFan(Apex, F, Depth, Half, g.U, g.V, Ground) + Vector3.up * 0.8f, 3); return; }
            // rootlets reach forward from the fan's grown edge
            _rootsMade = true;
            float sc = FootScale(NowUs);
            for (int k = 0; k < 14; k++)
            {
                float v = (k / 13f) * 2f - 1f;
                Vector3 a = Vol2FsB3.InFan(Apex, F, Depth * sc, Half, 0.85f, v * 0.9f, Ground), o = FlatN(a - Apex);
                var pts = new Vector3[5];
                for (int j = 0; j < 5; j++) { float u = j / 4f; pts[j] = a + o * 2.4f * u + Vector3.Cross(Vector3.up, o) * Mathf.Sin(u * 5f + k) * 0.2f; pts[j].y = Ground + 0.03f + 0.05f * (1f - u); }
                var tb = _roots.Add(pts, 0.08f, 0.012f, 12); tb.Knots = 0.45f; tb.Sharp = 0.5f; tb.Sides = 5;
            }
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            float warn = Warn(NowUs), ex = ExitK(t), pulse = DrawPulse(t);
            float scale = Res != null ? FootScale(NowUs) : 1f;
            float sinkK = NaturalEnd ? Vol2Fs.Smooth(0f, 0.8f, ex) : Vol2Fs.Smooth(0f, 0.4f, ex);
            float leafDrop = Mathf.Max(warn * 0.5f, NaturalEnd ? Vol2Fs.Smooth(0f, 0.5f, ex) : Vol2Fs.Smooth(0f, 0.2f, ex));
            bool standing = Res == null ? EndRel < 0f || t < EndRel + 0.5f : true;
            float noRes = Res == null && EndRel >= 0f ? Vol2Fs.Smooth(EndRel, EndRel + 0.8f, t) : 0f;
            foreach (var g in _grove)
            {
                // a tree sprouts when the sweep passed it (in the fan) or the growing edge reached it (outside the first fan)
                if (!g.Sprouted && (g.U <= 1f ? h >= g.At : (Res != null && g.U <= scale))) { g.Sprouted = true; g.SproutAt = t; Vector3 p = Vol2FsB3.InFan(Apex, F, Depth, Half, g.U, g.V, Ground); Vol2FsB1.Emit(_dirt, p, 8); Vol2FsB1.Emit(_leafFx, p + Vector3.up * 0.5f, 4); }
                float gr = g.Sprouted ? Vol2Fs.EaseOutBack((t - g.SproutAt) / 0.45f) : 0f;
                float sway = 0f;
                for (int k = 0; k < g.Count; k++) _trees.Tubes[g.First + k].Grow = gr * (1f - sinkK) * (1f - noRes) * (k == 0 ? 1f : Mathf.Clamp01((t - g.SproutAt - 0.1f) / 0.35f));
                for (int k = 0; k < g.LeafCount; k++)
                {
                    var l = _leaves.Items[g.LeafFirst + k];
                    l.Grow = g.Sprouted ? Vol2Fs.Smooth(g.SproutAt + 0.2f, g.SproutAt + 0.6f, t) * (1f - Mathf.Clamp01(leafDrop * 1.4f - (k % 3) * 0.2f)) * (1f - sinkK) * (1f - noRes) : 0f;
                    _leaves.Items[g.LeafFirst + k] = l;
                }
                if (g.Sprouted && !Exiting && Random.value < 0.004f + 0.05f * warn) Vol2FsB1.Emit(_leafFx, Vol2FsB3.InFan(Apex, F, Depth, Half, g.U, g.V, Ground) + Vector3.up * 1.2f, 1);
                _ = sway;
            }
            _trees.Write(_treeMesh); _leaves.Write(_leafMesh);
            // the bound bodies: vines wrap for the 1.5 s bind (a mirror of the body action), then loosen
            for (int b = 0; b < _bound.Count; b++)
            {
                var bd = _bound[b]; float a = t - bd.At;
                float gw = Vol2Fs.Smooth(0f, 0.25f, a) * (1f - Vol2Fs.Smooth(1.5f, 1.9f, a));
                Vector3 shift = bd.S.T != null ? bd.S.T.position - bd.S.Feet : Vector3.zero;
                for (int k = 0; k < bd.Count; k++) _wraps.Tubes[bd.First + k].Grow = gw;
                _ = shift;
            }
            _wraps.Write(_wrapMesh);
            // jade motes rising from the undergrowth, the moss carpet of the fan (it widens with the footprint)
            if (standing && !Exiting && Time.frameCount % 2 == 0) { var p = Vol2FsB3.InFan(Apex, F, Depth * scale, Half, Random.Range(0.25f, 1f), Random.Range(-0.9f, 0.9f), Ground); Vol2FsB1.Emit(_motes, p + Vector3.up * 0.2f, 1); }
            _moss.Clear();
            float ml = Vol2Fs.Smooth(0f, 0.4f, h) * (1f - Vol2Fs.Smooth(0.5f, 1f, ex)) * (1f - noRes);
            if (ml > 0.01f)
            {
                var pts = new List<Vector3>(); var w = new List<float>(); var al = new List<float>(); var side = new List<Vector3>();
                for (int k = 0; k <= 12; k++) { float v = (k / 12f) * 2f - 1f; Vector3 d = Dir(v * Half); var p = Apex + d * Depth * scale * 0.63f; p.y = Ground + 0.03f; pts.Add(p); w.Add(Depth * scale * 0.76f); /* r3: from 1/4 out */ al.Add(ml); side.Add(d); }
                _moss.Strip(pts, w, side, Color.white, al, 0.41f, cam, 0f, 3f);
            }
            _moss.Write(_mossMesh);
            // the launch's sweep of ink
            _sweep.Clear();
            float sl = 1f - Vol2Fs.Smooth(0.25f, 0.9f, h);
            if (sl > 0.01f)
            {
                var pts = new Vector3[12]; var w = new float[12]; var al = new float[12];
                float reach = Vol2Fs.Smooth(0f, 0.3f, h);
                for (int k = 0; k < 12; k++) { float v = Mathf.Lerp(-1f, -1f + 2f * reach, k / 11f); pts[k] = Vol2FsB3.InFan(Apex, F, Depth, Half, 0.7f, v, Ground) + Vector3.up * (0.35f + 0.25f * Mathf.Sin(k * 0.6f)); w[k] = 0.35f * (0.4f + 0.6f * Mathf.Sin(k / 11f * Mathf.PI)); al[k] = sl; }
                _sweep.Strip(pts, w, null, Color.white, al, 0.52f, cam, 0f, 1f);
            }
            _sweep.Write(_sweepMesh);
            if (_rootsMade) { float rg = Vol2Fs.Smooth(0.1f, 0.7f, ex), rf = Vol2Fs.Smooth(0.8f, 1f, ex); foreach (var r in _roots.Tubes) r.Grow = rg * (1f - rf); _roots.Write(_rootMesh); }
            _glowL.intensity = 3.5f * Vol2Fs.Smooth(0f, 0.5f, h) * (1f - ex) * (1f - noRes) * (1f + pulse) * (1f - 0.4f * warn);
        }
    }
}
