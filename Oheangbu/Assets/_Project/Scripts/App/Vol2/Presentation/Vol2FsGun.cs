using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>군 (wood, ㅜ ring, 번진다) - free showcase batch 4, concept gun_a (racing roots: a low ring of vines circles the caster; from the bear on the ring,
    /// roots RACE outward through the ground - raised lines of torn earth - to the bears farther out and burst up around them as snaring vines; leafy patches stay at
    /// their feet). Unlike 간 (lianas leaping through the air) the spread runs UNDER the ground; unlike 구 (a thorn crown) the ring is a low creeping vine band.
    /// Beats, all executor facts: the ring band for the timed emitter's life (3 s) -> the body on the band (Instant) is snared -> every "Spread" contact = a root
    /// tearing outward along the ground from that body to the neighbour, in the executor's order, thickness by share -> its Mark (3 s) = a vine snare + moss patch;
    /// warning = the leaves droop -> natural expiry (CSV 잔뿌리로 풀림, 앞으로 뻗으며) = the snare lets go and rootlets reach forward.</summary>
    public sealed class Vol2FsGun : Vol2FsSpread
    {
        static readonly Color Jade = new Color(0.45f, 1f, 0.35f), Soil = new Color(0.28f, 0.16f, 0.1f);
        ParticleSystem _charge, _leaves, _sparks, _dirt, _dust; Light _chargeLight, _ringLight;
        Material _vine, _rootMat, _leafMat;
        readonly Vol2FsTubes _ring = new Vol2FsTubes(); Mesh _ringMesh;
        readonly Vol2FsTubes _snareA = new Vol2FsTubes(); Mesh _snareAMesh;
        readonly List<float> _ringAng = new List<float>();
        Mesh _moatMesh; readonly Vol2FsStrips _moat = new Vol2FsStrips();
        Struck _first;

        sealed class HopLook { public Vol2FsTubes Root = new Vol2FsTubes(); public Mesh RootMesh; public Mesh TearMesh; public Vol2FsStrips Tear = new Vol2FsStrips(); public Mesh GlowMesh; public Vol2FsStrips Glow = new Vol2FsStrips(); public Light L; public float Phase; }
        sealed class MarkLook { public Vol2FsTubes Snare = new Vol2FsTubes(); public Mesh SnareMesh; public Vol2FsLeaves Sprouts = new Vol2FsLeaves(); public Mesh SproutMesh; public Vol2FsTubes Roots = new Vol2FsTubes(); public Mesh RootMesh; public MeshRenderer RootR; public bool RootsBuilt; public float ExitAt = -1f; public Mesh MossMesh; public Vol2FsStrips Moss = new Vol2FsStrips(); }

        protected override float HopSpeed => 7f;
        protected override float HopMin => 0.3f;
        protected override float HopMax => 0.55f;
        protected override float HopStagger => 0.12f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB1.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _vine = Kit.Bark(new Color(0.7f, 0.95f, 0.5f), new Color(0.35f, 1.3f, 0.25f) * 2.2f, 0.3f, "fs_vine", "fs_vein");
            _rootMat = Kit.Bark(new Color(0.7f, 0.95f, 0.5f), new Color(0.35f, 1.3f, 0.25f) * 4.2f, 0.3f, "fs_vine", "fs_vein");      // r2: the racing roots glow brighter jade
            _leafMat = Kit.Leaf(new Color(0.5f, 0.82f, 0.32f), new Color(0.08f, 0.38f, 0.05f));
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 3.5f, 1f)), Rate = 80f, Loop = true, Life0 = 0.18f, Life1 = 0.32f, Speed0 = -1.8f, Speed1 = -3f, Size0 = 0.05f, Size1 = 0.12f, Radius = 0.6f, World = false, Max = 120 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", Jade, 3f);
            _charge.transform.position = Tip(); _charge.Play(true);
            var leaf = M("fs_leaf", new Color(0.75f, 1f, 0.55f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leaves = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 1.0f, Life1 = 2.2f, Speed0 = 1f, Speed1 = 3.5f, Size0 = 0.12f, Size1 = 0.24f, Radius = 0.25f, Gravity = 0.3f, Drag = 1.5f,
                Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 600 }, root, "Leaves");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 4.5f, 1f)), Life0 = 0.4f, Life1 = 1.0f, Speed0 = 0.5f, Speed1 = 2.5f, Size0 = 0.03f, Size1 = 0.06f, Radius = 0.2f,
                Drag = 1.4f, Stretch = true, StretchVel = 0.04f, Noise = 1.3f, NoiseFreq = 2f, Max = 700 }, root, "Sparks");
            var dirt = M("fs_ink", Soil, 1f, 0f); dirt.NoiseAmt = 0.4f; dirt.Soft = 0.1f;
            _dirt = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dirt), Life0 = 0.5f, Life1 = 1f, Speed0 = 1.5f, Speed1 = 4f, Size0 = 0.05f, Size1 = 0.16f, Shape = ParticleSystemShapeType.Cone, Angle = 35f, Radius = 0.15f,
                ShapeRot = new Vector3(-90f, 0f, 0f), Gravity = 2.4f, Tiles = 2, AlphaHoldTo = 0.7f, Max = 900 }, root, "Dirt");
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.45f, 0.32f, 0.24f, 1f), 0.35f)), Life0 = 0.8f, Life1 = 1.6f, Speed0 = 0.2f, Speed1 = 0.8f, Size0 = 0.4f, Size1 = 0.8f, Radius = 0.2f, Drag = 1f, Tiles = 2,
                SizeStart = 0.5f, SizeEnd = 1.6f, VelOverLife = new Vector3(0f, 0.4f, 0f), Max = 200, SortFudge = 8 }, root, "Dust");
            foreach (var ps in new[] { _leaves, _sparks, _dirt, _dust }) ps.Play(false);
            Vol2Fs.MeshObject(root, "VineRing", _vine, out _ringMesh).shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            Vol2Fs.MeshObject(root, "SnareA", _vine, out _snareAMesh).shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var moat = M("fs_moss", new Color(0.32f, 0.55f, 0.22f, 1f), 1f, 0f); moat.Soft = 0.001f; moat.NoiseAmt = 0.45f; moat.Opacity = 0.8f; moat.Edge = new Color(0.25f, 0.9f, 0.2f, 1f); moat.EdgeWidth = 0.07f;
            Vol2Fs.MeshObject(root, "MossBand", Kit.Mat(moat), out _moatMesh);
            _ringLight = Vol2Fs.PointLight(root, "RingLight", Jade, 5f);
        }

        protected override void OnCharge(float t, float k)
        {
            _charge.transform.position = Tip();
            _chargeLight.intensity = 3f * k;
        }

        protected override void OnLaunch(long nowUs)
        {
            _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f;
            // the vine band: three creeping vines following the band (front arc and sides only - the camera is inside the ring), weaving up and down
            // r2: front arc only (±55°, the camera is inside the ring - r1's strands swept across the frame's bottom corners): each strand creeps from the front out to a side
            for (int v = 0; v < 6; v++)
            {
                float side = v % 2 == 0 ? 1f : -1f, end = (0.85f + 0.12f * Vol2Fs.Hash(Seed, v + 500)) * Vol2FsB4R.Span * 0.86f;
                var pts = new Vector3[16];
                for (int k = 0; k < 16; k++)
                {
                    float ang = side * Mathf.Lerp(-0.08f, end, k / 15f);
                    float r = Mathf.Lerp(Inner, Outer, 0.3f + 0.2f * Mathf.Sin(ang * 5f + v * 2.1f) + 0.12f * (v / 2));
                    var p = OnBand(ang, r);
                    p.y += 0.04f + 0.1f * Mathf.Max(0f, Mathf.Sin(ang * 7f + v * 1.7f + Seed));
                    pts[k] = p;
                }
                var tb = _ring.Add(pts, 0.07f, 0.02f, 60); tb.Knots = 0.4f; tb.Sharp = 0.5f; tb.Sides = 6;
                _ringAng.Add(0f);
            }
            // upright curling tendrils on the front arc
            for (int k = 0; k < 12; k++)
            {
                float ang = Mathf.Lerp(-0.9f, 0.9f, (k + 0.5f) / 12f) + 0.08f * (Vol2Fs.Hash(Seed, k) - 0.5f);
                float fr = Vol2FsB4R.Front55(ang); if (fr < 0.15f) continue;
                Vector3 b = OnBand(ang, Mathf.Lerp(Inner, Outer, Vol2Fs.Hash(Seed, k + 30))), o = Dir(ang);
                float h = (0.35f + 0.4f * Vol2Fs.Hash(Seed, k + 60)) * fr;
                Vector3 p1 = b + Vector3.up * h * 0.6f + o * 0.12f, p2 = p1 + Vector3.up * h * 0.4f + Vector3.Cross(Vector3.up, o) * 0.15f, p3 = p2 - o * 0.1f;
                var tb = _ring.Add(new[] { b, p1, p2, p3 }, 0.045f, 0.008f, 12); tb.Sharp = 0.2f; tb.Sides = 5;
                _ringAng.Add(ang);
            }
            _ringLight.transform.position = OnBand(0f, R) + Vector3.up * 0.6f;
        }

        protected override void OnHit(long nowUs)
        {
            for (int k = 0; k < 9; k++) { float ang = Mathf.Lerp(-0.9f, 0.9f, k / 8f); Vol2FsB1.Emit(_leaves, OnBand(ang, R) + Vector3.up * 0.2f, 3); Vol2FsB1.Emit(_dirt, OnBand(ang, R), 4); }
        }

        protected override void OnStruck(Struck s)
        {
            if (_first != null) return;
            _first = s;
            // the body on the band is snared: four vines spiral up its legs
            var bb = s.B; float rx = bb.extents.x + 0.12f, rz = bb.extents.z + 0.12f, hy = bb.size.y * 0.75f;
            for (int v = 0; v < 4; v++)
            {
                var pts = new Vector3[16]; float a0 = v * Mathf.PI * 0.5f + Vol2Fs.Hash(Seed, v + 300);
                for (int j = 0; j < 16; j++) { float u = j / 15f, a = a0 + u * Mathf.PI * 2f * 1.7f * (v % 2 == 0 ? 1f : -1f); pts[j] = new Vector3(bb.center.x + Mathf.Cos(a) * rx, Ground + 0.03f + u * hy, bb.center.z + Mathf.Sin(a) * rz); }
                var tb = _snareA.Add(pts, 0.08f, 0.035f, 50); tb.Knots = 0.3f; tb.Sharp = 0.25f; tb.Sides = 6;
            }
            Vol2FsB1.Emit(_leaves, CentreNow(s), 60); Vol2FsB1.Emit(_sparks, CentreNow(s), 60); Vol2FsB1.Emit(_dirt, FeetOf(s), 24);
        }

        protected override void OnHop(Hop h)
        {
            var root = Root.transform; var hl = new HopLook { Phase = Vol2Fs.Hash(Seed, (int)h.TargetId) * 6f };
            Vol2Fs.MeshObject(root, "Root_" + h.TargetId, _rootMat, out hl.RootMesh).shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var tear = M("fs_rock_crack", new Color(0.12f, 0.07f, 0.05f, 1f), 1f, 0f); tear.Soft = 0.001f; tear.NoiseAmt = 0.5f; tear.Opacity = 0.9f; tear.Edge = new Color(0.3f, 0.9f, 0.2f, 1f); tear.EdgeWidth = 0.06f;
            Vol2Fs.MeshObject(root, "Tear_" + h.TargetId, Kit.Mat(tear), out hl.TearMesh);
            var glow = M("fs_streak", Jade, 2.6f, 1f); glow.NoiseAmt = 0.45f; glow.Soft = 0.001f; glow.Scroll = new Vector2(-1.5f, 0f);
            Vol2Fs.MeshObject(root, "RootGlow_" + h.TargetId, Kit.Mat(glow), out hl.GlowMesh);
            hl.L = Vol2Fs.PointLight(root, "RootLight", Jade, 3.5f);
            // two roots braiding just at the ground from the source body to the neighbour, humping up through the soil now and then
            float k = ShareScale(h.Share);
            Vector3 a = G(h.FromP), b = G(FeetOf(h)); Vector3 d = FlatN(b - a), sd = Vector3.Cross(Vector3.up, d);
            float len = Vector3.Distance(a, b);
            // gun_a: a thick bundle of three roots, humping up through the torn soil
            for (int v = 0; v < 3; v++)
            {
                var pts = new Vector3[20];
                for (int j = 0; j < 20; j++)
                {
                    float u = j / 19f;
                    Vector3 p = Vector3.Lerp(a, b, u) + sd * (Mathf.Sin(u * 9f + hl.Phase + v * 2.1f) * 0.18f + (v - 1) * 0.07f);
                    p.y = Ground + 0.02f + Mathf.Max(0f, Mathf.Sin(u * len * 2.2f + v * 1.3f + hl.Phase)) * 0.16f * k;
                    pts[j] = p;
                }
                var tb = hl.Root.Add(pts, 0.11f * k, 0.055f * k, 70); tb.Knots = 0.45f; tb.Sharp = 0.3f; tb.Sides = 6;
            }
            h.Data = hl;
        }

        protected override void OnLand(Hop h)
        {
            Vector3 c = CentreNow(h), f = G(FeetOf(h));
            Vol2FsB1.Emit(_dirt, f, 40); Vol2FsB1.Emit(_dust, f + Vector3.up * 0.2f, 4); Vol2FsB1.Emit(_leaves, c, 50); Vol2FsB1.Emit(_sparks, c, 50);
        }

        protected override void OnMark(Mark m)
        {
            var root = Root.transform; var ml = new MarkLook();
            Vol2Fs.MeshObject(root, "Snare_" + m.Id, _vine, out ml.SnareMesh).shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            Vol2Fs.MeshObject(root, "Sprouts_" + m.Id, _leafMat, out ml.SproutMesh);
            ml.RootR = Vol2Fs.MeshObject(root, "Rootlets_" + m.Id, _vine, out ml.RootMesh);
            var moss = M("fs_moss", new Color(0.35f, 0.6f, 0.25f, 1f), 1f, 0f); moss.Soft = 0.001f; moss.NoiseAmt = 0.4f; moss.Opacity = 0.85f; moss.Edge = new Color(0.25f, 0.9f, 0.2f, 1f); moss.EdgeWidth = 0.08f;
            Vol2Fs.MeshObject(root, "Moss_" + m.Id, Kit.Mat(moss), out ml.MossMesh);
            var tg = Target(m.Hop != null ? m.Hop.TargetId : 0); var tr = m.Hop != null ? m.Hop.T : null;
            var bb = BodyBounds(tr, m.Centre, tg != null ? tg.Radius : 0.5f);
            float k = ShareScale(m.Share);
            float rx = bb.extents.x + 0.12f, rz = bb.extents.z + 0.12f, hy = bb.size.y * 0.7f;
            // the root bursts up round the body as five vines (the claws of the root that reached it)
            for (int v = 0; v < 5; v++)
            {
                var pts = new Vector3[16]; float a0 = v * Mathf.PI * 0.4f + Vol2Fs.Hash(Seed, (int)m.Id + v);
                for (int j = 0; j < 16; j++) { float u = j / 15f, a = a0 + u * Mathf.PI * 2f * 1.3f * (v % 2 == 0 ? 1f : -1f); float rr = 1f + 0.25f * (1f - u); pts[j] = new Vector3(bb.center.x + Mathf.Cos(a) * rx * rr, Ground + 0.02f + u * hy, bb.center.z + Mathf.Sin(a) * rz * rr); }
                var tb = ml.Snare.Add(pts, 0.085f * k, 0.03f * k, 50); tb.Knots = 0.35f; tb.Sharp = 0.25f; tb.Sides = 6;
            }
            for (int j = 0; j < 16; j++)
            {
                var p = Vol2FsB3.Spiral(m.Centre, m.Radius * 1.4f, j, 16, Ground + 0.02f);
                Vector3 nrm = Quaternion.AngleAxis(Vol2Fs.Hash(Seed + (int)m.Id, j) * 360f, Vector3.up) * Vector3.forward;
                ml.Sprouts.Items.Add(new Vol2FsLeaves.Leaf { Pos = p, Normal = nrm, Up = (Vector3.up * 1.4f + Vector3.Cross(nrm, Vector3.up) * 0.3f).normalized, Size = (0.22f + 0.14f * Vol2Fs.Hash(Seed, j + 40)) * k, Grow = 0f, Cell = j % 4 });
            }
            m.Data = ml;
        }

        void BuildRoots(Mark m, MarkLook ml)
        {
            ml.RootsBuilt = true;
            Vector3 fwd = FlatN(m.Centre - Apex), side = Vector3.Cross(Vector3.up, fwd);
            for (int k = 0; k < 8; k++)
            {
                float s = (k / 7f - 0.5f) * 1.4f; Vector3 b = m.Centre + side * s * 0.5f + fwd * 0.2f;
                Vector3 dir = (fwd + side * s * 0.6f).normalized; float len = 0.8f + 0.8f * Vol2Fs.Hash(Seed + (int)m.Id, k + 20);
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
            // the vine band: creeps out round the ring from the front (0.25 s), stands the emitter's life, unravels after its end
            float life = HostLife(t, 0.25f, 0.6f), warn = HostWarn(t);
            for (int k = 0; k < _ring.Tubes.Count; k++)
            {
                float delay = k < 6 ? 0f : 0.08f + 0.12f * Mathf.Abs(_ringAng[k]) / 0.9f;
                float g = Vol2Fs.Smooth(delay, delay + 0.25f, h);
                _ring.Tubes[k].Grow = g * life;
            }
            _ring.Write(_ringMesh);
            _moat.Clear();
            if (life > 0.01f) Vol2FsB2.GroundRing(_moat, Apex, F, Ground, Inner - 0.05f, Outer + 0.1f, 60, a => Vol2FsB4R.Front55(a) * life * (1f - 0.4f * warn), 0.37f, cam, 0.02f, 0f, 4f);
            _moat.Write(_moatMesh);
            _ringLight.intensity = 2.2f * life * (1f - 0.5f * warn);
            if (life > 0.3f && warn > 0.01f && Time.frameCount % 3 == 0) Vol2FsB1.Emit(_leaves, OnBand(Random.Range(-0.9f, 0.9f), R) + Vector3.up * 0.3f, 1);
            // the snare on the body on the band: the slow's 2 s (a mirror - the status sink owns it)
            if (_first != null)
            {
                float a = t - _first.At, g = Mathf.Clamp01(Vol2Fs.EaseOutBack(a / 0.3f)) * (1f - Vol2Fs.Smooth(2f, 2.5f, a));
                foreach (var tb in _snareA.Tubes) tb.Grow = g;
                _snareA.Write(_snareAMesh);
            }
            // the roots tearing outward through the ground
            foreach (var hop in Hops)
            {
                if (!(hop.Data is HopLook hl)) continue;
                float g = HopK(hop, t), ex = hop.Mark != null ? MarkExit(hop.Mark, t) : (t > hop.Start + 3.5f ? 1f : 0f);
                foreach (var tb in hl.Root.Tubes) tb.Grow = g * (1f - ex);
                hl.Root.Write(hl.RootMesh);
                hl.Tear.Clear();
                float tl = (1f - Vol2Fs.Smooth(0f, 0.6f, ex));
                if (g > 0f && tl > 0.01f) Vol2FsB4.Runner(hl.Tear, hop.FromP, G(FeetOf(hop)), Ground, 0f, g, 0.55f * ShareScale(hop.Share), tl, 0.2f + hop.Order * 0.17f, cam, 0.12f, hl.Phase, 16, 0.015f);
                hl.Tear.Write(hl.TearMesh);
                // r2: the jade glow running along the roots (r1's roots read dark): a bright line along the run, strongest at the head
                hl.Glow.Clear();
                float gl = tl * (0.55f + 0.45f * (1f - Vol2Fs.Smooth(0f, 0.8f, t - hop.Start - hop.Travel)));
                if (g > 0f && gl > 0.01f) Vol2FsB4.Runner(hl.Glow, hop.FromP, G(FeetOf(hop)), Ground, Mathf.Max(0f, g - 0.6f), g, 0.32f * ShareScale(hop.Share), gl, 0.5f + hop.Order * 0.13f, cam, 0.12f, hl.Phase, 16, 0.06f);
                hl.Glow.Write(hl.GlowMesh);
                if (g > 0f && g < 1f)
                {
                    Vector3 head = Vector3.Lerp(G(hop.FromP), G(FeetOf(hop)), g);
                    Vol2FsB1.Emit(_dirt, head, 3); if (Time.frameCount % 3 == 0) Vol2FsB1.Emit(_dust, head + Vector3.up * 0.1f, 1); Vol2FsB1.Emit(_sparks, head + Vector3.up * 0.1f, 5);
                    hl.L.transform.position = head + Vector3.up * 0.3f; hl.L.intensity = 4.5f;
                }
                else { hl.L.intensity = Mathf.MoveTowards(hl.L.intensity, 0f, Time.deltaTime * 6f); if (g >= 1f && tl > 0.3f && Time.frameCount % 3 == 0) Vol2FsB1.Emit(_sparks, Vector3.Lerp(G(hop.FromP), G(FeetOf(hop)), Random.value) + Vector3.up * 0.1f, 1); }
            }
            // the marks
            foreach (var m in MarkList)
            {
                if (!(m.Data is MarkLook ml)) continue;
                float ml01 = MarkLife(m, t, 0.3f), mw = MarkWarn(m, t), ex = MarkExit(m, t), age = MarkAge(m, t);
                float grow = age < 0f ? 0f : Mathf.Clamp01(Vol2Fs.EaseOutBack(age / 0.3f));
                foreach (var tb in ml.Snare.Tubes) tb.Grow = grow * (1f - ex);
                ml.Snare.Write(ml.SnareMesh);
                for (int j = 0; j < ml.Sprouts.Items.Count; j++) { var l = ml.Sprouts.Items[j]; l.Grow = Vol2Fs.Smooth(0.03f * j, 0.25f + 0.03f * j, Mathf.Max(0f, age)) * (1f - ex) * (1f - 0.4f * mw); ml.Sprouts.Items[j] = l; }
                ml.Sprouts.Write(ml.SproutMesh);
                if (ml.RootsBuilt)
                {
                    float rg = Vol2Fs.Smooth(0f, 0.45f, t - ml.ExitAt), sink = Vol2Fs.Smooth(0.5f, 0.9f, t - ml.ExitAt);
                    foreach (var tb in ml.Roots.Tubes) tb.Grow = rg;
                    ml.Roots.Write(ml.RootMesh); ml.RootR.transform.position = Vector3.down * 0.12f * sink;
                }
                ml.Moss.Clear();
                if (ml01 > 0.01f) ml.Moss.Quad(m.Centre + Vector3.up * 0.02f, Right, Fwd, m.Radius * 3.2f * ShareScale(m.Share), new Color(1f, 1f, 1f, ml01 * (1f - 0.5f * mw)), 0.3f + m.Id % 7 * 0.1f);
                ml.Moss.Write(ml.MossMesh);
                if (ml01 > 0.2f && ex <= 0f && Time.frameCount % 6 == (int)(m.Id % 6)) Vol2FsB1.Emit(_sparks, m.Centre + Vector3.up * Random.Range(0.1f, 0.9f), 1);
            }
        }
    }
}
