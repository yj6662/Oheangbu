using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>간 (wood, ㅏ path, 번진다) - free showcase batch 4, concept gan_a (liana leaps: the vine bolt bursts into a knot of tendrils on the first bear and
    /// living lianas ARC through the air from it to every neighbour, each landing in a leafy snare round the legs; jade sparks and leaves drift along the arcs).
    /// Beats, all executor facts: charge -> a seed-vine bolt flies to the predicted first body -> the hit (tendril knot, leaves, ink) -> every "Spread" contact =
    /// one liana arcing from the struck body to that neighbour in the executor's order, its thickness by the share (1.0 split n ways: thinner the more neighbours)
    /// -> its Mark material = the snare + a leafy sprout patch at the feet, from creation to end; warning = the leaves droop -> natural expiry (CSV: 잔뿌리로
    /// 풀림, 앞으로 뻗으며) = the snare and the liana unravel and rootlets reach forward along the ground; other reasons = their shorter exits.</summary>
    public sealed class Vol2FsGan : Vol2FsSpread
    {
        static readonly Color Jade = new Color(0.45f, 1f, 0.35f), Ink = new Color(0.025f, 0.035f, 0.02f), Rim = new Color(0.5f, 1.6f, 0.35f, 1f);
        ParticleSystem _charge, _leaves, _sparks, _drops, _dirt; Light _chargeLight, _boltLight, _hitLight;
        Material _vine, _leafMat;
        readonly Vol2FsTubes _bolt = new Vol2FsTubes(); Mesh _boltMesh;
        readonly Vol2FsTubes _knot = new Vol2FsTubes(); Mesh _knotMesh;
        Mesh _burstMesh; readonly Vol2FsStrips _burstS = new Vol2FsStrips(); List<Vol2FsB1.Stroke> _burst;
        Vector3 _hitAt;

        sealed class HopLook { public Vol2FsTubes Liana = new Vol2FsTubes(); public Mesh LianaMesh; public MeshRenderer LianaR; public Vol2FsLeaves Leaves = new Vol2FsLeaves(); public Mesh LeafMesh; public Light L; public Vector3 A, B; }
        sealed class MarkLook { public Vol2FsTubes Snare = new Vol2FsTubes(); public Mesh SnareMesh; public Vol2FsLeaves Sprouts = new Vol2FsLeaves(); public Mesh SproutMesh; public Vol2FsTubes Roots = new Vol2FsTubes(); public Mesh RootMesh; public MeshRenderer RootR; public bool RootsBuilt; public float ExitAt = -1f; public Mesh StainMesh; public Vol2FsStrips Stain = new Vol2FsStrips(); }

        protected override float LobHeight => 0.7f;
        protected override float HopSpeed => 9f;
        protected override float HopMax => 0.5f;
        protected override float MarkExpireSeconds => 0.9f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB1.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _vine = Kit.Bark(new Color(0.6f, 0.95f, 0.45f), new Color(0.35f, 1.3f, 0.25f) * 4.2f, 0.35f, "fs_vine", "fs_vein");      // r2: brighter (r1 lianas read as dark antlers at the camera A distance)
            _leafMat = Kit.Leaf(new Color(0.55f, 0.85f, 0.35f), new Color(0.1f, 0.4f, 0.05f));
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 3.5f, 1f)), Rate = 90f, Loop = true, Life0 = 0.18f, Life1 = 0.32f, Speed0 = -1.8f, Speed1 = -3f, Size0 = 0.05f, Size1 = 0.13f, Radius = 0.65f, World = false, Max = 120 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", Jade, 3.5f);
            _charge.transform.position = Tip(); _charge.Play(true);
            var leaf = M("fs_leaf", new Color(0.8f, 1f, 0.6f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leaves = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 1.2f, Life1 = 2.4f, Speed0 = 1f, Speed1 = 4f, Size0 = 0.12f, Size1 = 0.26f, Radius = 0.25f, Gravity = 0.25f, Drag = 1.5f,
                Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 600 }, root, "Leaves");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 5f, 1f)), Life0 = 0.4f, Life1 = 1.1f, Speed0 = 0.5f, Speed1 = 3f, Size0 = 0.03f, Size1 = 0.07f, Radius = 0.2f,
                Drag = 1.4f, Stretch = true, StretchVel = 0.04f, Noise = 1.3f, NoiseFreq = 2f, Max = 900 }, root, "Sparks");
            var drop = M("fs_ink", Ink, 1f, 0f); drop.Edge = Rim; drop.EdgeWidth = 0.16f; drop.NoiseAmt = 0.3f; drop.Soft = 0.1f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.08f, Size1 = 0.3f, Radius = 0.25f, Gravity = 2f, Drag = 0.4f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 300 }, root, "InkDrops");
            var dirt = M("fs_ink", new Color(0.3f, 0.17f, 0.11f, 1f), 1f, 0f); dirt.NoiseAmt = 0.4f; dirt.Soft = 0.1f;
            _dirt = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dirt), Life0 = 0.5f, Life1 = 1f, Speed0 = 1.5f, Speed1 = 4f, Size0 = 0.05f, Size1 = 0.16f, Shape = ParticleSystemShapeType.Cone, Angle = 40f, Radius = 0.3f,
                ShapeRot = new Vector3(-90f, 0f, 0f), Gravity = 2.4f, Tiles = 2, AlphaHoldTo = 0.7f, Max = 300 }, root, "Dirt");
            foreach (var ps in new[] { _leaves, _sparks, _drops, _dirt }) ps.Play(false);
            Vol2Fs.MeshObject(root, "Bolt", _vine, out _boltMesh);
            Vol2Fs.MeshObject(root, "Knot", _vine, out _knotMesh).shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            Vol2Fs.MeshObject(root, "InkBurst", Kit.InkSolid("fs_stroke2", Ink, Rim, 0.14f, 0.5f, 0.28f, 1.6f), out _burstMesh);
            _boltLight = Vol2Fs.PointLight(root, "BoltLight", Jade, 5f);
            _hitLight = Vol2Fs.PointLight(root, "HitLight", Jade, 8f);
        }

        protected override void OnCharge(float t, float k)
        {
            _charge.transform.position = Tip();
            _chargeLight.intensity = 3.5f * k;
            _charge.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.2f, k);
        }

        protected override void OnLaunch(long nowUs)
        {
            _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f;
            // the bolt: two vines twisting round the lobbed line (a seed-vine, thinner than 가's harpoon braid)
            Vector3 axis = (HitPoint - From).normalized; Vector3 n = Vector3.Cross(axis, Vector3.up).normalized; if (n == Vector3.zero) n = Vector3.right; Vector3 bn = Vector3.Cross(axis, n);
            for (int v = 0; v < 2; v++)
            {
                var pts = new Vector3[14];
                for (int k = 0; k < 14; k++) { float u = k / 13f, ang = u * Mathf.PI * 2f * 2f + v * Mathf.PI; pts[k] = Line(u) + (n * Mathf.Cos(ang) + bn * Mathf.Sin(ang)) * 0.12f * Mathf.Sin(Mathf.Clamp01(u * 1.4f) * Mathf.PI * 0.5f + 0.3f); }
                var tb = _bolt.Add(pts, 0.07f, 0.035f, 50); tb.Knots = 0.3f; tb.Sharp = 0.3f; tb.Sides = 6;
            }
            _boltLight.intensity = 4f;
        }

        protected override void OnFly(float t, float f, float prev)
        {
            foreach (var tb in _bolt.Tubes) tb.Grow = f * 1.02f;
            _bolt.Write(_boltMesh);
            Vector3 head = Line(f); _boltLight.transform.position = head;
            Vol2FsB1.Emit(_sparks, head, 4); if (Time.frameCount % 2 == 0) Vol2FsB1.Emit(_leaves, head, 1);
        }

        protected override void OnHit(long nowUs)
        {
            _boltLight.intensity = 0f;
            _hitAt = HasTarget ? HitPoint : AimPoint;
            Vector3 fwd = FlatN(_hitAt - Inst.Origin), side = Vector3.Cross(Vector3.up, fwd);
            _burst = Vol2FsB1.Burst(Seed + 3, 6, side, fwd, 0.7f, 1.4f, 0.12f, 0.22f, 0.8f, 0.3f);
            Vol2FsB1.Emit(_leaves, _hitAt, 110); Vol2FsB1.Emit(_sparks, _hitAt, 140); Vol2FsB1.Emit(_drops, _hitAt, 50); Vol2FsB1.Emit(_dirt, G(_hitAt), 30);
            _hitLight.transform.position = _hitAt;
            // the knot of tendrils on the first body: short curling tendrils bursting out of the hit (the spread's source)
            var c = TB.center; float rx = TB.extents.x + 0.15f;
            for (int k = 0; k < 9; k++)
            {
                float a = k / 9f * Mathf.PI * 2f + Vol2Fs.Hash(Seed, k) * 0.5f;
                Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 p0 = c + o * rx * 0.6f + Vector3.up * (Vol2Fs.Hash(Seed, k + 10) - 0.3f) * 0.6f;
                Vector3 p1 = p0 + o * 0.45f + Vector3.up * 0.35f, p2 = p1 + Vector3.Cross(o, Vector3.up) * 0.3f + Vector3.up * 0.2f, p3 = p2 - o * 0.15f + Vector3.up * 0.1f;
                var tb = _knot.Add(new[] { p0, p1, p2, p3 }, 0.07f, 0.01f, 14); tb.Knots = 0.3f; tb.Sharp = 0.25f; tb.Sides = 6;
            }
        }

        protected override void OnHop(Hop h)
        {
            var root = Root.transform;
            var hl = new HopLook();
            hl.LianaR = Vol2Fs.MeshObject(root, "Liana_" + h.TargetId, _vine, out hl.LianaMesh);
            hl.LianaR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            Vol2Fs.MeshObject(root, "LianaLeaves_" + h.TargetId, _leafMat, out hl.LeafMesh);
            hl.L = Vol2Fs.PointLight(root, "LianaLight", Jade, 4f);
            // the liana: a high looping arc from the source body (a curl near its top) to the neighbour's shoulders; two strands twisted; thickness by the share
            float k = ShareScale(h.Share);
            Vector3 a = h.FromP + Vector3.up * 1.0f, b = CentreNow(h) + Vector3.up * 0.15f;
            hl.A = a; hl.B = b;
            Vector3 d = b - a; Vector3 side = Vector3.Cross(Vector3.up, FlatN(d)); float span = d.magnitude;
            float lift = Mathf.Clamp(span * 0.55f, 1.0f, 2.2f) + 0.4f * Vol2Fs.Hash(Seed, (int)h.TargetId);
            float curl = (h.Order % 2 == 0 ? 1f : -1f) * 0.45f;
            for (int v = 0; v < 2; v++)
            {
                var pts = new Vector3[18];
                for (int j = 0; j < 18; j++)
                {
                    float u = j / 17f;
                    Vector3 on = Vector3.Lerp(a, b, u) + Vector3.up * 4f * lift * u * (1f - u) + side * curl * Mathf.Sin(u * Mathf.PI * 2f) * Mathf.Sin(u * Mathf.PI);
                    float tw = u * Mathf.PI * 2f * 3f + v * Mathf.PI;
                    Vector3 axis = (Vector3.Lerp(a, b, Mathf.Min(1f, u + 0.05f)) - Vector3.Lerp(a, b, u)).normalized;
                    Vector3 n = Vector3.Cross(axis, Vector3.up).normalized; if (n == Vector3.zero) n = side; Vector3 bn = Vector3.Cross(axis, n);
                    pts[j] = on + (n * Mathf.Cos(tw) + bn * Mathf.Sin(tw)) * 0.13f * k;
                }
                var tb = hl.Liana.Add(pts, 0.13f * k, 0.06f * k, 60); tb.Knots = 0.35f; tb.Sharp = 0.35f; tb.Sides = 6;
            }
            // leaves along the liana (they unfold as it passes)
            for (int j = 0; j < 22; j++)
            {
                float u = (j + 0.5f) / 22f;
                Vector3 p = Vol2FsTubes.CatmullRom(hl.Liana.Tubes[j % 2].Ctrl, u);
                Vector3 nrm = Quaternion.AngleAxis(Vol2Fs.Hash(Seed + (int)h.TargetId, j) * 360f, (b - a).normalized) * side;
                hl.Leaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = p, Normal = nrm, Up = (Vector3.up + nrm * 0.4f).normalized, Size = 0.45f * k, Grow = 0f, Cell = j % 4 });
            }
            h.Data = hl;
        }

        protected override void OnLand(Hop h)
        {
            Vector3 c = CentreNow(h);
            Vol2FsB1.Emit(_leaves, c, Mathf.RoundToInt(60 * ShareScale(h.Share))); Vol2FsB1.Emit(_sparks, c, 70); Vol2FsB1.Emit(_dirt, G(FeetOf(h)), 16);
        }

        protected override void OnMark(Mark m)
        {
            var root = Root.transform; var ml = new MarkLook();
            Vol2Fs.MeshObject(root, "Snare_" + m.Id, _vine, out ml.SnareMesh).shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            Vol2Fs.MeshObject(root, "Sprouts_" + m.Id, _leafMat, out ml.SproutMesh);
            ml.RootR = Vol2Fs.MeshObject(root, "Rootlets_" + m.Id, _vine, out ml.RootMesh);
            var stain = M("fs_moss", new Color(0.35f, 0.6f, 0.25f, 1f), 1f, 0f); stain.Soft = 0.001f; stain.NoiseAmt = 0.4f; stain.Opacity = 0.85f; stain.Edge = new Color(0.25f, 0.9f, 0.2f, 1f); stain.EdgeWidth = 0.08f;
            Vol2Fs.MeshObject(root, "Moss_" + m.Id, Kit.Mat(stain), out ml.StainMesh);
            // the snare: 4 tendrils spiralling up the legs (lower half of the body) + sprouts on the mark's disc
            var tg = Target(m.Hop != null ? m.Hop.TargetId : 0); var tr = m.Hop != null ? m.Hop.T : null;
            var bb = BodyBounds(tr, m.Centre, tg != null ? tg.Radius : 0.5f);
            float k = ShareScale(m.Share);
            float rx = bb.extents.x + 0.12f, rz = bb.extents.z + 0.12f, hy = bb.size.y * 0.65f;
            for (int v = 0; v < 4; v++)
            {
                var pts = new Vector3[16]; float a0 = v * Mathf.PI * 0.5f + Vol2Fs.Hash(Seed, (int)m.Id + v);
                for (int j = 0; j < 16; j++) { float u = j / 15f, a = a0 + u * Mathf.PI * 2f * 1.6f * (v % 2 == 0 ? 1f : -1f); pts[j] = new Vector3(bb.center.x + Mathf.Cos(a) * rx, Ground + 0.03f + u * hy, bb.center.z + Mathf.Sin(a) * rz); }
                var tb = ml.Snare.Add(pts, 0.075f * k, 0.03f * k, 50); tb.Knots = 0.3f; tb.Sharp = 0.25f; tb.Sides = 6;
            }
            for (int j = 0; j < 18; j++)
            {
                var p = Vol2FsB3.Spiral(m.Centre, m.Radius * 1.3f, j, 18, Ground + 0.02f);
                float ang = Vol2Fs.Hash(Seed + (int)m.Id, j) * 360f; Vector3 nrm = Quaternion.AngleAxis(ang, Vector3.up) * Vector3.forward;
                ml.Sprouts.Items.Add(new Vol2FsLeaves.Leaf { Pos = p, Normal = nrm, Up = (Vector3.up * 1.4f + Vector3.Cross(nrm, Vector3.up) * 0.3f).normalized, Size = (0.22f + 0.14f * Vol2Fs.Hash(Seed, j + 40)) * k, Grow = 0f, Cell = j % 4 });
            }
            m.Data = ml;
        }

        void BuildRoots(Mark m, MarkLook ml)
        {
            ml.RootsBuilt = true;
            Vector3 fwd = FlatN(m.Centre - Inst.Origin), side = Vector3.Cross(Vector3.up, fwd);
            for (int k = 0; k < 8; k++)
            {
                float s = (k / 7f - 0.5f) * 1.4f; Vector3 b = m.Centre + side * s * 0.5f + fwd * 0.2f;
                Vector3 dir = (fwd + side * s * 0.6f).normalized; float len = 0.8f + 0.8f * Vol2Fs.Hash(Seed + (int)m.Id, k);
                var pts = new Vector3[6];
                for (int j = 0; j < 6; j++) { float u = j / 5f; pts[j] = b + dir * len * u + side * Mathf.Sin(u * 7f + k) * 0.1f; pts[j].y = Ground + 0.03f + 0.03f * Mathf.Sin(u * 9f + k); }
                var tb = ml.Roots.Add(pts, 0.03f, 0.006f, 16); tb.Knots = 0.5f; tb.Sharp = 0.3f; tb.Sides = 5;
            }
        }

        protected override void OnMarkEnd(Mark m)
        {
            if (!(m.Data is MarkLook ml)) return;
            ml.ExitAt = m.EndAt;
            Vector3 c = m.Centre + Vector3.up * 0.4f;
            if (Natural(m)) { BuildRoots(m, ml); Vol2FsB1.Emit(_leaves, c, 20); }
            else if (m.Why != Vol2MaterialEnd.Aborted) { Vol2FsB1.Emit(_leaves, c, 40); Vol2FsB1.Emit(_sparks, c, 40); }
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            // the bolt withers into the knot after the hit
            float bw = 1f - Vol2Fs.Smooth(0.05f, 0.35f, h);
            foreach (var tb in _bolt.Tubes) tb.Grow = bw;
            _bolt.Write(_boltMesh);
            _hitLight.intensity = h < 0.05f ? 10f * h / 0.05f : 10f * Mathf.Exp(-(h - 0.05f) * 4f);
            // the knot on the first body: grows, holds while any liana stands, withers
            bool anyMark = false; foreach (var mk in MarkList) if (mk.EndAt < 0f || MarkExit(mk, t) < 1f) anyMark = true;
            float knot = Vol2Fs.EaseOutBack(Mathf.Clamp01(h / 0.25f)) * (anyMark || h < 1.2f ? 1f : 1f - Vol2Fs.Smooth(0f, 0.6f, t - LastMarkEnd()));
            foreach (var tb in _knot.Tubes) tb.Grow = Mathf.Clamp01(knot);
            _knot.Write(_knotMesh);
            _burstS.Clear(); if (_burst != null) Vol2FsB1.DrawBurst(_burstS, _burst, _hitAt, h, cam, 0.4f, 0.1f); _burstS.Write(_burstMesh);
            // the lianas: grow along their arc in the hop's time, stand while the mark stands, unravel from the source end with the mark's exit
            foreach (var hop in Hops)
            {
                if (!(hop.Data is HopLook hl)) continue;
                float g = HopK(hop, t);
                float ex = hop.Mark != null ? MarkExit(hop.Mark, t) : (t > hop.Start + 3.5f ? 1f : 0f);
                float warn = hop.Mark != null ? MarkWarn(hop.Mark, t) : 0f;
                foreach (var tb in hl.Liana.Tubes) tb.Grow = g * (1f - ex);
                hl.Liana.Write(hl.LianaMesh);
                for (int j = 0; j < hl.Leaves.Items.Count; j++)
                {
                    var l = hl.Leaves.Items[j]; float u = (j + 0.5f) / hl.Leaves.Items.Count;
                    l.Grow = Vol2Fs.Smooth(u, u + 0.25f, g) * (1f - Vol2Fs.Smooth(0f, 0.5f, ex)) * (1f - 0.35f * warn); hl.Leaves.Items[j] = l;
                }
                hl.Leaves.Write(hl.LeafMesh);
                // the growing tip sheds sparks and leaves; a soft jade light rides it
                if (g > 0f && g < 1f)
                {
                    Vector3 tip = Vol2FsTubes.TipOf(hl.Liana.Tubes[0]);
                    Vol2FsB1.Emit(_sparks, tip, 9); if (Time.frameCount % 2 == 0) Vol2FsB1.Emit(_leaves, tip, 1);
                    hl.L.transform.position = tip; hl.L.intensity = 6f * ShareScale(hop.Share);
                }
                else hl.L.intensity = Mathf.MoveTowards(hl.L.intensity, g >= 1f && ex <= 0f ? 1.2f * (1f - warn) : 0f, Time.deltaTime * 8f);
                if (g >= 1f && ex <= 0f && Time.frameCount % 9 == (int)(hop.TargetId % 9)) { var p = hl.Liana.Tubes.Count > 0 ? Vol2FsTubes.CatmullRom(hl.Liana.Tubes[0].Ctrl, Random.value) : hl.B; Vol2FsB1.Emit(_sparks, p, 1); }
            }
            // the marks: snare + sprouts + moss patch; natural expiry unravels into rootlets reaching forward
            foreach (var m in MarkList)
            {
                if (!(m.Data is MarkLook ml)) continue;
                float life = MarkLife(m, t, 0.3f), warn = MarkWarn(m, t), ex = MarkExit(m, t);
                float grow = Vol2Fs.EaseOutBack(Mathf.Clamp01(MarkAge(m, t) / 0.35f));
                foreach (var tb in ml.Snare.Tubes) tb.Grow = Mathf.Clamp01(grow) * (1f - ex);
                ml.Snare.Write(ml.SnareMesh);
                for (int j = 0; j < ml.Sprouts.Items.Count; j++) { var l = ml.Sprouts.Items[j]; l.Grow = Vol2Fs.Smooth(0.03f * j, 0.25f + 0.03f * j, Mathf.Max(0f, MarkAge(m, t))) * (1f - ex) * (1f - 0.4f * warn); ml.Sprouts.Items[j] = l; }
                ml.Sprouts.Write(ml.SproutMesh);
                if (ml.RootsBuilt)
                {
                    float rg = Vol2Fs.Smooth(0f, 0.45f, t - ml.ExitAt), sink = Vol2Fs.Smooth(0.5f, 0.9f, t - ml.ExitAt);
                    foreach (var tb in ml.Roots.Tubes) tb.Grow = rg;
                    ml.Roots.Write(ml.RootMesh); ml.RootR.transform.position = Vector3.down * 0.12f * sink;
                }
                ml.Stain.Clear();
                if (life > 0.01f) ml.Stain.Quad(m.Centre + Vector3.up * 0.02f, Right, Fwd, m.Radius * 3.2f * ShareScale(m.Share), new Color(1f, 1f, 1f, life * (1f - 0.5f * warn)), 0.3f + m.Id % 7 * 0.1f);
                ml.Stain.Write(ml.StainMesh);
                if (life > 0.2f && ex <= 0f && Time.frameCount % 6 == (int)(m.Id % 6)) Vol2FsB1.Emit(_sparks, m.Centre + Vector3.up * Random.Range(0.1f, 0.9f), 1);
            }
        }

        float LastMarkEnd() { float e = -1f; foreach (var m in MarkList) if (m.EndAt > e) e = m.EndAt; return e < 0f ? 0f : e; }
    }
}
