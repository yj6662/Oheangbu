using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>곤 (wood, ㅗ fan, 번진다) - free showcase batch 4, concept gon_a (creeping runners: the swing lays a carpet of moss, dark ink and creeping roots over
    /// the fan; from the fan's outer EDGE thick braided runner-vines creep fast along the ground, glowing jade in their cracks, out to the bears standing outside
    /// it and coil up their legs). Ground-bound, unlike 간's lianas leaping through the air. Beats, all executor facts: charge -> the launch = the carpet and its
    /// roots race out over the fan (the spec's apex / depth / half angle), the body inside is snared (Instant) -> when the fan's emitter has finished the
    /// executor spreads from the edge: every "Spread" contact = one runner creeping from hop.FromP (the nearest fan point) to that neighbour, thickness by its
    /// share -> its Mark (3 s) = the runner's coil on the legs + a moss patch with sprouts; warning = the jade dims, leaves droop -> natural expiry (CSV: 잔뿌리로
    /// 풀림, 앞으로 뻗으며) = the coil unwinds and rootlets reach forward along the ground; other reasons = shorter exits.</summary>
    public sealed class Vol2FsGon : Vol2FsSpread
    {
        static readonly Color Jade = new Color(0.5f, 1f, 0.3f), Ink = new Color(0.025f, 0.035f, 0.02f), Rim = new Color(0.55f, 1.6f, 0.3f, 1f);
        ParticleSystem _charge, _leaves, _sparks, _dirt; Light _chargeLight; Light[] _fanLights;
        Material _vine, _leafMat, _runnerMat; Mesh _trailMesh; readonly Vol2FsStrips _trail = new Vol2FsStrips();
        readonly Vol2FsTubes _roots = new Vol2FsTubes(); Mesh _rootMesh; readonly List<float> _rootAt = new List<float>();
        readonly Vol2FsTubes _snareA = new Vol2FsTubes(); Mesh _snareAMesh;
        Mesh _mossMesh, _inkMesh; readonly Vol2FsStrips _moss = new Vol2FsStrips(), _ink = new Vol2FsStrips();
        struct Streak { public float V, U0, U1, W, Rnd; }
        readonly List<Streak> _streaks = new List<Streak>();

        sealed class HopLook { public Vol2FsTubes Runner = new Vol2FsTubes(); public Mesh RunnerMesh; public Vol2FsLeaves Leaves = new Vol2FsLeaves(); public Mesh LeafMesh; public Light L; }
        sealed class MarkLook { public Vol2FsTubes Coil = new Vol2FsTubes(); public Mesh CoilMesh; public Vol2FsLeaves Sprouts = new Vol2FsLeaves(); public Mesh SproutMesh; public Vol2FsTubes Roots = new Vol2FsTubes(); public Mesh RootMesh; public MeshRenderer RootR; public bool RootsBuilt; public float ExitAt = -1f; public Mesh PatchMesh; public Vol2FsStrips Patch = new Vol2FsStrips(); }

        protected override float HopSpeed => 7f;
        protected override float HopMin => 0.3f;
        protected override float HopMax => 0.55f;
        protected override float HopStagger => 0.12f;
        protected override float MarkExpireSeconds => 0.9f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB1.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _vine = Kit.Bark(new Color(0.55f, 0.62f, 0.4f), new Color(0.4f, 1.4f, 0.25f) * 2.6f, 0.3f, "fs_vine", "fs_vein");
            _leafMat = Kit.Leaf(new Color(0.5f, 0.82f, 0.32f), new Color(0.1f, 0.4f, 0.05f));
            // r2: the runners brighter than the fan's roots, and a jade glow along the ground under each (r1: the spread to the bears did not read)
            _runnerMat = Kit.Bark(new Color(0.5f, 0.72f, 0.38f), new Color(0.45f, 1.5f, 0.28f) * 4.5f, 0.3f, "fs_vine", "fs_vein");
            var trail = M("fs_streak", new Color(0.45f, 1f, 0.3f, 1f), 2.2f, 1f); trail.NoiseAmt = 0.45f; trail.Scroll = new Vector2(-1.5f, 0f);
            Vol2Fs.MeshObject(root, "RunnerGlow", Kit.Mat(trail), out _trailMesh);
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 3.5f, 1f)), Rate = 90f, Loop = true, Life0 = 0.18f, Life1 = 0.32f, Speed0 = -1.8f, Speed1 = -3f, Size0 = 0.05f, Size1 = 0.13f, Radius = 0.65f, World = false, Max = 120 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", Jade, 3.5f);
            _charge.transform.position = Tip(); _charge.Play(true);
            var leaf = M("fs_leaf", new Color(0.75f, 1f, 0.55f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leaves = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 1.2f, Life1 = 2.4f, Speed0 = 1f, Speed1 = 3.5f, Size0 = 0.12f, Size1 = 0.26f, Radius = 0.25f, Gravity = 0.25f, Drag = 1.5f,
                Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 700 }, root, "Leaves");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 4.5f, 1f)), Life0 = 0.4f, Life1 = 1.1f, Speed0 = 0.4f, Speed1 = 2.5f, Size0 = 0.03f, Size1 = 0.07f, Radius = 0.15f,
                Drag = 1.4f, Stretch = true, StretchVel = 0.04f, Noise = 1.3f, NoiseFreq = 2f, Max = 900 }, root, "Sparks");
            var dirt = M("fs_ink", new Color(0.3f, 0.17f, 0.11f, 1f), 1f, 0f); dirt.NoiseAmt = 0.4f; dirt.Soft = 0.1f;
            _dirt = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dirt), Life0 = 0.5f, Life1 = 1f, Speed0 = 1f, Speed1 = 3f, Size0 = 0.05f, Size1 = 0.15f, Shape = ParticleSystemShapeType.Cone, Angle = 40f, Radius = 0.2f,
                ShapeRot = new Vector3(-90f, 0f, 0f), Gravity = 2.4f, Tiles = 2, AlphaHoldTo = 0.7f, Max = 500 }, root, "Dirt");
            foreach (var ps in new[] { _leaves, _sparks, _dirt }) ps.Play(false);
            var moss = M("fs_moss", new Color(0.22f, 0.3f, 0.15f, 1f), 1f, 0f); moss.Soft = 0.001f; moss.NoiseAmt = 0.45f; moss.NoiseScale = 1.6f; moss.Opacity = 0.5f; moss.Edge = new Color(0.2f, 0.5f, 0.12f, 1f); moss.EdgeWidth = 0.04f;      // r2: a faint low moss
            Vol2Fs.MeshObject(root, "Moss", Kit.Mat(moss), out _mossMesh);
            var ink = M("fs_stroke", Ink, 1f, 0f); ink.Soft = 0.001f; ink.NoiseAmt = 0.4f; ink.Opacity = 0.9f; ink.Edge = new Color(0.4f, 1.2f, 0.25f, 1f); ink.EdgeWidth = 0.05f;
            Vol2Fs.MeshObject(root, "InkStreaks", Kit.Mat(ink), out _inkMesh);
            Vol2Fs.MeshObject(root, "FanRoots", _vine, out _rootMesh);
            Vol2Fs.MeshObject(root, "SnareA", _vine, out _snareAMesh).shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            _fanLights = new Light[2];
            for (int k = 0; k < 2; k++) _fanLights[k] = Vol2Fs.PointLight(root, "FanLight" + k, Jade, 5f);
        }

        protected override void OnCharge(float t, float k)
        {
            _charge.transform.position = Tip();
            _chargeLight.intensity = 3.5f * k;
            _charge.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.2f, k);
        }

        protected override void OnLaunch(long nowUs) { _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f; }

        protected override void OnHit(long nowUs)
        {
            int seed = Seed + 41;
            // the roots of the carpet: meandering roots radiating over the fan from a quarter out, growing with the sweep
            for (int k = 0; k < 13; k++)
            {
                float v = (k / 12f) * 2f - 1f + (Vol2Fs.Hash(seed, k) - 0.5f) * 0.1f;
                Vector3 a = Vol2FsB3.InFan(Apex, F, Depth, Half, 0.22f + 0.08f * Vol2Fs.Hash(seed, k + 20), v * 0.95f, Ground);
                Vector3 b = Vol2FsB3.InFan(Apex, F, Depth, Half, 0.85f + 0.15f * Vol2Fs.Hash(seed, k + 40), v, Ground);
                var tb = _roots.Add(Vol2FsB4F.GroundLine(a, b, Ground, 9, 0.25f, k * 1.7f, 0.04f), 0.07f, 0.02f, 30); tb.Knots = 0.5f; tb.Sharp = 0.4f; tb.Sides = 6;
                _rootAt.Add(0.05f * Vol2Fs.Hash(seed, k + 60));
                // a fork off each
                Vector3 m = Vector3.Lerp(a, b, 0.55f); Vector3 fd = Quaternion.AngleAxis((k % 2 == 0 ? 35f : -35f), Vector3.up) * FlatN(b - a);
                var fk = _roots.Add(Vol2FsB4F.GroundLine(m, m + fd * 0.9f, Ground, 5, 0.1f, k, 0.04f), 0.04f, 0.01f, 12); fk.Sharp = 0.3f; fk.Sides = 5;
                _rootAt.Add(0.15f + 0.05f * Vol2Fs.Hash(seed, k + 80));
            }
            // flat ink streaks (the brush's sweep laid on the ground), radiating
            for (int k = 0; k < 0; k++) _streaks.Add(      // r2: no flat ink streaks (r1: with the moss they read as dark-green flags by the caster)
new Streak { V = (k / 8f) * 2f - 1f + (Vol2Fs.Hash(seed, k + 100) - 0.5f) * 0.12f, U0 = 0.25f + 0.1f * Vol2Fs.Hash(seed, k + 110), U1 = 0.75f + 0.3f * Vol2Fs.Hash(seed, k + 120), W = 0.35f + 0.3f * Vol2Fs.Hash(seed, k + 130), Rnd = Vol2Fs.Hash(seed, k + 140) });
            for (int k = 0; k < 2; k++) _fanLights[k].transform.position = Vol2FsB3.InFan(Apex, F, Depth, Half, 0.7f, k == 0 ? -0.5f : 0.5f, Ground) + Vector3.up * 0.7f;
            for (int k = 0; k < 10; k++) Vol2FsB1.Emit(_leaves, Vol2FsB3.InFan(Apex, F, Depth, Half, Random.Range(0.35f, 1f), Random.Range(-0.9f, 0.9f), Ground) + Vector3.up * 0.3f, 5);
        }

        protected override void OnStruck(Struck s)
        {
            // the body inside the fan: snared at once (the instant 0.7 - its hold mirror is the state sink's)
            for (int v = 0; v < 2; v++) { var tb = _snareA.Add(Vol2FsB4F.Coil(s.B, Ground, 18, v * 3.1f + Vol2Fs.Hash(Seed, v), 2.4f * (v == 1 ? -1f : 1f), 0.5f, 0.03f), 0.06f, 0.025f, 60); tb.Knots = 0.3f; tb.Sharp = 0.25f; tb.Sides = 6; }
            Vol2FsB1.Emit(_leaves, CentreNow(s), 50); Vol2FsB1.Emit(_sparks, CentreNow(s), 60);
        }

        protected override void OnHop(Hop h)
        {
            var root = Root.transform; var hl = new HopLook();
            Vol2Fs.MeshObject(root, "Runner_" + h.TargetId, _runnerMat, out hl.RunnerMesh).shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            Vol2Fs.MeshObject(root, "RunnerLeaves_" + h.TargetId, _leafMat, out hl.LeafMesh);
            hl.L = Vol2Fs.PointLight(root, "RunnerLight", Jade, 3.5f);
            float k = ShareScale(h.Share);
            Vector3 a = h.FromP, b = G(FeetOf(h)) - FlatN(FeetOf(h) - h.FromP) * 0.25f;
            // three braided runners hugging the ground, a little hump in the middle (they shoulder up through the soil)
            for (int v = 0; v < 3; v++)
            {
                var pts = Vol2FsB4F.GroundLine(a, b, Ground, 12, 0.35f, v * 2.1f + h.Order, 0.06f + 0.03f * v, 0.12f);
                var tb = hl.Runner.Add(pts, (0.14f - 0.03f * v) * k, 0.06f * k, 50); tb.Knots = 0.45f; tb.Sharp = 0.35f; tb.Sides = 6;
            }
            for (int j = 0; j < 10; j++)
            {
                float u = (j + 0.5f) / 10f; Vector3 p = Vol2FsTubes.CatmullRom(hl.Runner.Tubes[j % 3].Ctrl, u) + Vector3.up * 0.05f;
                Vector3 nrm = Quaternion.AngleAxis(Vol2Fs.Hash(Seed + (int)h.TargetId, j) * 360f, Vector3.up) * Vector3.forward;
                hl.Leaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = p, Normal = nrm, Up = (Vector3.up * 1.3f + Vector3.Cross(nrm, Vector3.up) * 0.4f).normalized, Size = 0.26f * k, Grow = 0f, Cell = j % 4 });
            }
            h.Data = hl;
        }

        protected override void OnLand(Hop h) { Vector3 c = CentreNow(h); Vol2FsB1.Emit(_leaves, c, Mathf.RoundToInt(45 * ShareScale(h.Share))); Vol2FsB1.Emit(_sparks, G(FeetOf(h)) + Vector3.up * 0.2f, 60); Vol2FsB1.Emit(_dirt, G(FeetOf(h)), 20); }

        protected override void OnMark(Mark m)
        {
            var root = Root.transform; var ml = new MarkLook();
            Vol2Fs.MeshObject(root, "Coil_" + m.Id, _vine, out ml.CoilMesh).shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            Vol2Fs.MeshObject(root, "Sprouts_" + m.Id, _leafMat, out ml.SproutMesh);
            ml.RootR = Vol2Fs.MeshObject(root, "Rootlets_" + m.Id, _vine, out ml.RootMesh);
            var moss = M("fs_moss", new Color(0.3f, 0.55f, 0.2f, 1f), 1f, 0f); moss.Soft = 0.001f; moss.NoiseAmt = 0.4f; moss.Opacity = 0.85f; moss.Edge = new Color(0.35f, 1.2f, 0.2f, 1f); moss.EdgeWidth = 0.08f;
            Vol2Fs.MeshObject(root, "MossPatch_" + m.Id, Kit.Mat(moss), out ml.PatchMesh);
            var tg = m.Hop != null ? Target(m.Hop.TargetId) : null;
            var bb = BodyBounds(m.Hop != null ? m.Hop.T : null, m.Centre, tg != null ? tg.Radius : 0.5f);
            float k = ShareScale(m.Share);
            // r2: two tight coils (r1: four loose ones read as wicker baskets)
            for (int v = 0; v < 2; v++) { var tb = ml.Coil.Add(Vol2FsB4F.Coil(bb, Ground, 18, v * 3.1f + Vol2Fs.Hash(Seed, (int)m.Id + v), 2.2f * (v % 2 == 0 ? 1f : -1f), 0.5f, 0.03f), 0.06f * k, 0.025f * k, 60); tb.Knots = 0.3f; tb.Sharp = 0.25f; tb.Sides = 6; }
            for (int j = 0; j < 16; j++)
            {
                var p = Vol2FsB3.Spiral(m.Centre, m.Radius * 1.4f, j, 16, Ground + 0.02f);
                Vector3 nrm = Quaternion.AngleAxis(Vol2Fs.Hash(Seed + (int)m.Id, j) * 360f, Vector3.up) * Vector3.forward;
                ml.Sprouts.Items.Add(new Vol2FsLeaves.Leaf { Pos = p, Normal = nrm, Up = (Vector3.up * 1.4f + Vector3.Cross(nrm, Vector3.up) * 0.3f).normalized, Size = (0.2f + 0.14f * Vol2Fs.Hash(Seed, j + 40)) * k, Grow = 0f, Cell = j % 4 });
            }
            m.Data = ml;
        }

        protected override void OnMarkEnd(Mark m)
        {
            if (!(m.Data is MarkLook ml)) return;
            ml.ExitAt = m.EndAt;
            if (m.Why == Vol2MaterialEnd.Aborted) return;
            if (Natural(m))
            {
                // CSV: 잔뿌리로 풀림 (앞으로 뻗으며) - rootlets reaching forward from the feet
                ml.RootsBuilt = true;
                Vector3 fwd = FlatN(m.Centre - Inst.Origin), side = Vector3.Cross(Vector3.up, fwd);
                for (int k = 0; k < 8; k++)
                {
                    float s = (k / 7f - 0.5f) * 1.4f; Vector3 b = m.Centre + side * s * 0.5f + fwd * 0.2f;
                    Vector3 dir = (fwd + side * s * 0.6f).normalized; float len = 0.8f + 0.8f * Vol2Fs.Hash(Seed + (int)m.Id, k);
                    var tb = ml.Roots.Add(Vol2FsB4F.GroundLine(b, b + dir * len, Ground, 6, 0.1f, k, 0.03f), 0.03f, 0.006f, 16); tb.Knots = 0.5f; tb.Sharp = 0.3f; tb.Sides = 5;
                }
                Vol2FsB1.Emit(_leaves, m.Centre + Vector3.up * 0.4f, 18);
            }
            else { Vol2FsB1.Emit(_leaves, m.Centre + Vector3.up * 0.4f, 40); Vol2FsB1.Emit(_sparks, m.Centre + Vector3.up * 0.4f, 40); }
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            // the fan: carpet of moss + ink streaks + roots race out in 0.35 s, stand while anything of the spell stands, fade after
            float reach = Mathf.Lerp(0.25f, 1f, Vol2Fs.Smooth(0f, 0.35f, h));
            bool alive = false; float lastEnd = 0f;
            foreach (var mk in MarkList) { if (mk.EndAt < 0f) alive = true; else lastEnd = Mathf.Max(lastEnd, mk.EndAt); }
            float fieldFade = alive || h < 1.4f ? 0f : Vol2Fs.Smooth(Mathf.Max(lastEnd, HitAt + 1.4f), Mathf.Max(lastEnd, HitAt + 1.4f) + 0.9f, t);
            _moss.Clear(); Vol2FsB4F.FanCarpet(_moss, Apex, F, Depth, Half, Ground, reach, 1f - fieldFade, 0.37f, cam, 0.03f); _moss.Write(_mossMesh);
            _ink.Clear();
            var side = new List<Vector3>(); var pts = new List<Vector3>(); var w = new List<float>(); var al = new List<float>();
            foreach (var s in _streaks)
            {
                float u1 = Mathf.Min(s.U1, reach); if (u1 <= s.U0 + 0.02f) continue;
                pts.Clear(); w.Clear(); al.Clear(); side.Clear();
                for (int k = 0; k < 6; k++)
                {
                    float u = Mathf.Lerp(s.U0, u1, k / 5f);
                    var p = Vol2FsB3.InFan(Apex, F, Depth, Half, u, s.V, Ground); p.y = Ground + 0.035f;
                    pts.Add(p); w.Add(s.W * (0.4f + 0.6f * Mathf.Sin(k / 5f * Mathf.PI)) * (1f - 0.4f * k / 5f)); al.Add(1f - fieldFade); side.Add(Vector3.Cross(Vector3.up, FlatN(p - Apex)));
                }
                _ink.Strip(pts, w, side, Color.white, al, s.Rnd, cam, 0f, 1f);
            }
            _ink.Write(_inkMesh);
            for (int k = 0; k < _roots.Tubes.Count; k++) _roots.Tubes[k].Grow = Mathf.Clamp01((Vol2Fs.Smooth(0f, 0.4f, h - _rootAt[k])) * (1f - fieldFade));
            _roots.Write(_rootMesh);
            for (int k = 0; k < _fanLights.Length; k++) _fanLights[k].intensity = (h < 0.3f ? 6f * h / 0.3f : 3f) * (1f - fieldFade);
            if (h < 0.4f && Time.frameCount % 2 == 0) Vol2FsB1.Emit(_dirt, Vol2FsB3.InFan(Apex, F, Depth, Half, reach, Random.Range(-0.9f, 0.9f), Ground), 3);
            // the body inside: its snare for the hold mirror (2 s), then unwinds
            float sa = Vol2Fs.EaseOutBack(Mathf.Clamp01(h / 0.3f)) * (1f - Vol2Fs.Smooth(2f, 2.5f, h));
            foreach (var tb in _snareA.Tubes) tb.Grow = Mathf.Clamp01(sa);
            _snareA.Write(_snareAMesh);
            // the runners: creep from the edge in the hop's time, stand with the mark, withdraw on its exit
            _trail.Clear();
            foreach (var hop in Hops)
            {
                if (!(hop.Data is HopLook hl)) continue;
                float g = HopK(hop, t), ex = hop.Mark != null ? MarkExit(hop.Mark, t) : (t > hop.Start + 3.5f ? 1f : 0f), warn = hop.Mark != null ? MarkWarn(hop.Mark, t) : 0f;
                foreach (var tb in hl.Runner.Tubes) tb.Grow = g * (1f - ex);
                float gl = g < 1f ? 1f : 0.55f * (1f - warn);
                if (g > 0f) Vol2FsB4.Runner(_trail, hop.FromP, G(FeetOf(hop)), Ground, Mathf.Max(0f, g - 0.6f), g, 0.55f * ShareScale(hop.Share), gl * (1f - ex), 0.3f + hop.Order * 0.2f, cam, 0.3f, hop.Order, 14, 0.06f);
                hl.Runner.Write(hl.RunnerMesh);
                for (int j = 0; j < hl.Leaves.Items.Count; j++) { var l = hl.Leaves.Items[j]; float u = (j + 0.5f) / hl.Leaves.Items.Count; l.Grow = Vol2Fs.Smooth(u, u + 0.25f, g) * (1f - Vol2Fs.Smooth(0f, 0.5f, ex)) * (1f - 0.4f * warn); hl.Leaves.Items[j] = l; }
                hl.Leaves.Write(hl.LeafMesh);
                if (g > 0f && g < 1f)
                {
                    Vector3 tip = Vol2FsTubes.TipOf(hl.Runner.Tubes[0]);
                    Vol2FsB1.Emit(_sparks, tip, 4); Vol2FsB1.Emit(_dirt, tip, 1); if (Time.frameCount % 2 == 0) Vol2FsB1.Emit(_leaves, tip + Vector3.up * 0.1f, 1);
                    hl.L.transform.position = tip + Vector3.up * 0.3f; hl.L.intensity = 3.5f * ShareScale(hop.Share);
                }
                else hl.L.intensity = Mathf.MoveTowards(hl.L.intensity, g >= 1f && ex <= 0f ? 1f * (1f - warn) : 0f, Time.deltaTime * 8f);
            }
            _trail.Write(_trailMesh);
            // the marks: the coil on the legs + a moss patch with sprouts; natural expiry = rootlets reach forward and sink
            foreach (var m in MarkList)
            {
                if (!(m.Data is MarkLook ml)) continue;
                float age = MarkAge(m, t), life = MarkLife(m, t, 0.3f), warn = MarkWarn(m, t), ex = MarkExit(m, t);
                float grow = age < 0f ? 0f : Vol2Fs.EaseOutBack(Mathf.Clamp01(age / 0.35f));
                foreach (var tb in ml.Coil.Tubes) tb.Grow = Mathf.Clamp01(grow) * (1f - ex);
                ml.Coil.Write(ml.CoilMesh);
                for (int j = 0; j < ml.Sprouts.Items.Count; j++) { var l = ml.Sprouts.Items[j]; l.Grow = Vol2Fs.Smooth(0.03f * j, 0.25f + 0.03f * j, Mathf.Max(0f, age)) * (1f - ex) * (1f - 0.4f * warn); ml.Sprouts.Items[j] = l; }
                ml.Sprouts.Write(ml.SproutMesh);
                if (ml.RootsBuilt)
                {
                    float rg = Vol2Fs.Smooth(0f, 0.45f, t - ml.ExitAt), sink = Vol2Fs.Smooth(0.5f, 0.9f, t - ml.ExitAt);
                    foreach (var tb in ml.Roots.Tubes) tb.Grow = rg;
                    ml.Roots.Write(ml.RootMesh); ml.RootR.transform.position = Vector3.down * 0.12f * sink;
                }
                ml.Patch.Clear();
                if (life > 0.01f) ml.Patch.Quad(m.Centre + Vector3.up * 0.025f, Right, Fwd, m.Radius * 3.4f * ShareScale(m.Share), new Color(1f, 1f, 1f, life * (1f - 0.5f * warn)), 0.2f + m.Id % 7 * 0.1f);
                ml.Patch.Write(ml.PatchMesh);
                if (life > 0.2f && ex <= 0f && Time.frameCount % 6 == (int)(m.Id % 6)) Vol2FsB1.Emit(_sparks, m.Centre + Vector3.up * Random.Range(0.1f, 0.8f), 1);
            }
        }
    }
}
