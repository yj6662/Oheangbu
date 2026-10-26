using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>각 (wood, ㅏ path, 남긴다 peak) - free showcase, concept gak_a (a low cage of gnarled branch ribs locks round the struck body's legs, roots
    /// radiate over the ground, a jade glow inside) + gak_c (the growth left where the cage opened). Beats, all executor facts on the scope clock:
    /// launch (the emitter's StartUs) -> a root runs along the ground with the vol.2 path head -> the first direct contact ("Instant") = the cage grows round
    /// that body and follows it -> the residue material appears where the cage lets go (the executor's 1.5 s hold) = the ribs loosen and sink, saplings and
    /// moss grow on the residue's disc -> the residue ends (its 10 s life, or burnt / consumed) = the growth withers.</summary>
    public sealed class Vol2FsGak : Vol2FsRun
    {
        static readonly Color Jade = new Color(0.5f, 1f, 0.32f), Ink = new Color(0.03f, 0.035f, 0.025f);

        ParticleSystem _charge, _headFx, _dirt, _dust, _leafFx, _motes, _drops, _burstLeaf; Light _chargeLight, _headLight, _cageLight;
        Vol2FsTubes _root = new Vol2FsTubes(); Mesh _rootMesh; Vol2FsTubes.Tube _runner;
        Mesh _crackMesh; readonly Vol2FsStrips _crack = new Vol2FsStrips();
        // cage
        readonly Vol2FsTubes _cage = new Vol2FsTubes(); Mesh _cageMesh; MeshRenderer _cageR;
        readonly Vol2FsTubes _roots = new Vol2FsTubes(); Mesh _rootsMesh; MeshRenderer _rootsR;
        readonly Vol2FsLeaves _cageLeaves = new Vol2FsLeaves(); Mesh _cageLeafMesh; MeshRenderer _cageLeafR;
        Mesh _stainMesh; readonly Vol2FsStrips _stain = new Vol2FsStrips();
        // growth (the residue)
        readonly Vol2FsTubes _grow = new Vol2FsTubes(); Mesh _growMesh;
        readonly Vol2FsLeaves _growLeaves = new Vol2FsLeaves(); Mesh _growLeafMesh;
        Mesh _mossMesh; readonly Vol2FsStrips _moss = new Vol2FsStrips();

        bool _launched; float _launchAt = -1f;
        ParticleSystem _glowPs; Mesh _burstMesh; readonly Vol2FsStrips _burstS = new Vol2FsStrips();
        readonly List<(Vector3 Dir, float Len, float Width, float Rnd)> _burst = new List<(Vector3, float, float, float)>();
        long _cageId; Transform _cageT; Vector3 _cageFeet, _cageAnchor; Bounds _cageB; float _cageAt = -1f, _releaseAt = -1f;
        Vol2Material _residue; Vector3 _resCentre; float _resRadius, _resAt = -1f, _resEnd = -1f;
        float _hitAlong = -1f;
        readonly List<float> _ribDelay = new List<float>();

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsMat.Make(tex, tint, intensity, addMix);
        Material BarkMat => Kit.Bark(new Color(0.72f, 0.62f, 0.46f), new Color(0.45f, 1.25f, 0.25f) * 1.4f);

        public override void Began(long nowUs)
        {
            var root = Root.transform;
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 3f, 1f)), Rate = 60f, Loop = true, Life0 = 0.2f, Life1 = 0.35f, Speed0 = -1.5f, Speed1 = -2.5f, Size0 = 0.05f, Size1 = 0.12f, Radius = 0.55f, World = false, Max = 80 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", Jade, 3f);
            _charge.transform.position = Tip();
            _charge.Play(false);
            // shared particle pools (emitted at points)
            var dirt = M("fs_ink", new Color(0.32f, 0.17f, 0.11f, 1f), 1f, 0f); dirt.NoiseAmt = 0.4f; dirt.Soft = 0.1f;
            _dirt = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dirt), Life0 = 0.5f, Life1 = 1f, Speed0 = 2f, Speed1 = 5f, Size0 = 0.06f, Size1 = 0.2f, Shape = ParticleSystemShapeType.Cone, Angle = 35f, Radius = 0.15f,
                ShapeRot = new Vector3(-90f, 0f, 0f), Gravity = 2.4f, Tiles = 2, AlphaHoldTo = 0.7f, Max = 500 }, root, "Dirt");
            var dust = M("fs_puff", new Color(0.45f, 0.33f, 0.26f, 1f), 1f, 0f); dust.Opacity = 0.45f; dust.NoiseAmt = 0.6f; dust.Erode = 0.35f;
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dust), Life0 = 0.8f, Life1 = 1.5f, Speed0 = 0.4f, Speed1 = 1.4f, Size0 = 0.7f, Size1 = 1.5f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.3f, Drag = 1f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.2f, SizeEnd = 1.6f, VelOverLife = new Vector3(0f, 0.4f, 0f), Max = 200, SortFudge = 10 }, root, "Dust");
            var leaf = M("fs_leaf", new Color(0.9f, 1f, 0.75f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leafFx = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 1.4f, Life1 = 2.6f, Speed0 = 1.5f, Speed1 = 4f, Size0 = 0.12f, Size1 = 0.26f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.4f, Gravity = 0.3f, Drag = 1.6f,
                Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 240f, AlphaHoldTo = 0.75f, Max = 300 }, root, "Leaves");
            var drop = M("fs_ink", Ink, 1f, 0f); drop.Edge = new Color(0.6f, 1.4f, 0.3f, 1f); drop.EdgeWidth = 0.15f; drop.Soft = 0.1f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 2.5f, Speed1 = 7f, Size0 = 0.08f, Size1 = 0.3f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.3f, Gravity = 1.8f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 300 }, root, "InkDrops");
            foreach (var ps in new[] { _dirt, _dust, _leafFx, _drops }) ps.Play(false);
        }

        void Launch(long nowUs)
        {
            _launched = true; _launchAt = Now(nowUs);
            _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f;
            var root = Root.transform;
            // the runner root: one long root lying along the path, revealed with the vol.2 head
            var spec = Inst.Log.Spec;
            var cue = Vol2LegacyPathRecipe.SingleCue(Inst, new Vol2LegacyCue { Letter = '각', Body = Inst.Log.Glyph.Body, Origin = spec.Origin, Forward = spec.Forward, PathLength = spec.PathLength }, nowUs);
            float len = cue.HasTarget ? Mathf.Max(1f, FlatDist(spec.Origin, cue.TargetPoint)) : spec.PathLength;
            _hitAlong = len;
            var pts = new Vector3[8];
            int seed = (int)(Inst.Cast.Value & 0x7fff);
            for (int k = 0; k < 8; k++)
            {
                float u = k / 7f;
                Vector3 p = spec.Origin + spec.Forward * (0.6f + (len - 0.6f) * u) + Right * Mathf.Sin(u * 9f + seed) * 0.18f;
                p.y = Ground + 0.06f + 0.08f * Mathf.Sin(u * 13f);
                pts[k] = p;
            }
            _runner = _root.Add(pts, 0.11f, 0.06f, 30); _runner.Knots = 0.5f; _runner.Sharp = 0.5f;
            Vol2Fs.MeshObject(root, "Runner", BarkMat, out _rootMesh);
            var crack = M("fs_stroke2", Ink, 1f, 0f); crack.Edge = new Color(0.5f, 1.3f, 0.3f, 1f); crack.EdgeWidth = 0.12f; crack.Soft = 0.001f; crack.NoiseAmt = 0.5f; crack.Erode = 0.25f;
            Vol2Fs.MeshObject(root, "PathInk", Kit.Mat(crack), out _crackMesh);
            _headFx = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 4f, 1f)), Rate = 60f, RatePerMeter = 10f, Loop = true, Life0 = 0.25f, Life1 = 0.5f, Speed0 = 0.5f, Speed1 = 2f, Size0 = 0.05f, Size1 = 0.14f,
                Radius = 0.2f, Stretch = true, StretchVel = 0.04f, Max = 200 }, root, "Head");
            _headLight = Vol2Fs.PointLight(_headFx.transform, "HeadLight", Jade, 4f); _headLight.intensity = 3f;
            _headFx.transform.position = pts[0];
            _headFx.Play(false);
            Note = "launch path to " + len.ToString("0.0") + " m";
        }

        static float FlatDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        public override void Contact(Vol2ContactRecord rec)
        {
            if (!rec.BudgetAccepted || rec.Kind != "Instant" || _cageAt >= 0f) return;
            var t = Target(rec.TargetId);
            _cageId = rec.TargetId; _cageT = TargetTransform(rec.TargetId);
            Watch(_cageT);                   // batch 2: the caged body destroyed (capture teardown) -> the look hides at once
            _cageFeet = t != null ? t.Position : Inst.Origin + Fwd * rec.Along;
            _cageB = BodyBounds(_cageT, _cageFeet, t != null ? t.Radius : 0.5f);
            _cageAt = Inst.Rel(rec.NowUs);
            BuildCage();
            Note = "cage on " + rec.TargetId;
        }

        void BuildCage()
        {
            var root = Root.transform;
            int seed = (int)(_cageId * 13 + (Inst.Cast.Value & 0xfff));
            var c = _cageB.center; float rx = _cageB.extents.x + 0.22f, rz = _cageB.extents.z + 0.22f, hy = _cageB.size.y;
            float g = _cageFeet.y;
            _cageAnchor = _cageFeet;
            // ribs: thick gnarled branches out of a ring round the body, arching up and curling in over its back
            int ribs = 11;
            for (int k = 0; k < ribs; k++)
            {
                float a = (k + 0.3f * Vol2Fs.Hash(seed, k)) / ribs * Mathf.PI * 2f;
                Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 b0 = new Vector3(c.x + o.x * rx * 1.3f, g - 0.12f, c.z + o.z * rz * 1.3f);
                float top = hy * Mathf.Lerp(1.25f, 1.65f, Vol2Fs.Hash(seed, k + 20));
                Vector3 b1 = new Vector3(c.x + o.x * rx * 1.05f, g + top * 0.45f, c.z + o.z * rz * 1.05f);
                Vector3 b2 = new Vector3(c.x + o.x * rx * 0.55f, g + top * 0.95f, c.z + o.z * rz * 0.55f);
                Vector3 b3 = new Vector3(c.x - o.x * rx * 0.1f, g + top * 0.9f, c.z - o.z * rz * 0.1f) + Vector3.Cross(Vector3.up, o) * 0.25f * (Vol2Fs.Hash(seed, k + 40) - 0.5f);
                var tube = _cage.Add(new[] { b0, b1, b2, b3 }, Mathf.Lerp(0.14f, 0.2f, Vol2Fs.Hash(seed, k + 60)), 0.05f, 16);
                tube.Knots = 0.55f; tube.Sharp = 0.35f; tube.Sides = 7;
                _ribDelay.Add(0.02f + 0.12f * Vol2Fs.Hash(seed, k + 80));
                // leaves at the curled tip
                for (int j = 0; j < 3; j++)
                {
                    Vector3 nrm = Quaternion.AngleAxis(Vol2Fs.Hash(seed, k * 5 + j) * 360f, Vector3.up) * Vector3.forward;
                    _cageLeaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = Vector3.Lerp(b2, b3, 0.4f + 0.3f * j), Normal = nrm, Up = (Vector3.up + nrm * 0.6f).normalized, Size = 0.2f, Grow = 0f, Cell = (k + j) % 4 });
                }
            }
            // roots radiating over the ground
            for (int k = 0; k < 10; k++)
            {
                float a = (k + 0.5f * Vol2Fs.Hash(seed, k + 100)) / 10f * Mathf.PI * 2f;
                Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float len = Mathf.Lerp(3.0f, 5.5f, Vol2Fs.Hash(seed, k + 120));
                var pts = new Vector3[5];
                for (int j = 0; j < 5; j++)
                {
                    float u = j / 4f;
                    Vector3 p = new Vector3(c.x, g, c.z) + o * (Mathf.Max(rx, rz) * 0.9f + len * u) + Vector3.Cross(Vector3.up, o) * Mathf.Sin(u * 5f + k) * 0.25f;
                    p.y = g + 0.04f + 0.05f * (1f - u);
                    pts[j] = p;
                }
                var tube = _roots.Add(pts, 0.2f, 0.03f, 16); tube.Knots = 0.5f; tube.Sharp = 0.6f;
            }
            _cageR = Vol2Fs.MeshObject(root, "Cage", BarkMat, out _cageMesh); _cageR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            _rootsR = Vol2Fs.MeshObject(root, "Roots", BarkMat, out _rootsMesh);
            _cageLeafR = Vol2Fs.MeshObject(root, "CageLeaves", Kit.Leaf(new Color(0.75f, 0.9f, 0.55f), new Color(0.12f, 0.35f, 0.05f)), out _cageLeafMesh);
            var stain = M("fs_ink", Ink, 1f, 0f); stain.Edge = new Color(0.4f, 1.1f, 0.25f, 1f); stain.EdgeWidth = 0.08f; stain.Soft = 0.001f; stain.NoiseAmt = 0.4f; stain.Opacity = 0.8f;
            Vol2Fs.MeshObject(root, "Stain", Kit.Mat(stain), out _stainMesh);
            // glow inside + rising motes
            _motes = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 3.5f, 1f)), Rate = 26f, Loop = true, Life0 = 0.6f, Life1 = 1.2f, Speed0 = 0.2f, Speed1 = 0.6f, Size0 = 0.04f, Size1 = 0.1f,
                Shape = ParticleSystemShapeType.Circle, Radius = Mathf.Max(rx, rz), ShapeRot = new Vector3(90f, 0f, 0f), VelOverLife = new Vector3(0f, 0.9f, 0f), Noise = 0.6f, Max = 100 }, root, "Motes");
            _motes.transform.position = new Vector3(c.x, g + 0.1f, c.z);
            _motes.Play(false);
            var glow = M("fs_dot", Jade, 1.1f, 1f); glow.Soft = 0.6f;
            _glowPs = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(glow), Rate = 24f, Loop = true, Life0 = 0.25f, Life1 = 0.35f, Speed0 = 0f, Speed1 = 0f, Size0 = 2.2f, Size1 = 2.8f, Radius = 0.2f, Max = 20, SortFudge = 20 }, root, "CageGlow");
            _glowPs.transform.position = new Vector3(c.x, g + hy * 0.5f, c.z);
            _glowPs.Play(false);
            // ink flung up out of the strike (solid strokes with a jade rim)
            Vol2Fs.MeshObject(root, "InkBurst", Kit.InkSolid("fs_stroke2", Ink, new Color(0.9f, 2.2f, 0.45f, 1f), 0.15f, 0.5f, 0.28f, 1.6f), out _burstMesh);
            Vector3 fwd2 = c - Inst.Origin; fwd2.y = 0f; fwd2.Normalize(); Vector3 side2 = Vector3.Cross(Vector3.up, fwd2);
            for (int k = 0; k < 9; k++)
            {
                float a = (k / 9f + 0.05f * Vol2Fs.Hash(seed, k + 700)) * Mathf.PI;          // the upper half: sideways and up
                Vector3 d = side2 * Mathf.Cos(a) * 1.2f + Vector3.up * Mathf.Sin(a) * 0.7f + fwd2 * 0.2f * (Vol2Fs.Hash(seed, k + 710) - 0.5f);
                _burst.Add((d.normalized, 1.4f + 1.6f * Vol2Fs.Hash(seed, k + 720), 0.22f + 0.22f * Vol2Fs.Hash(seed, k + 730), Vol2Fs.Hash(seed, k + 740)));
            }
            _cageLight = Vol2Fs.PointLight(root, "CageLight", Jade, 3.5f);
            _cageLight.transform.position = new Vector3(c.x, g + hy * 0.4f, c.z);
            // the burst of the strike landing
            Vector3 at = new Vector3(c.x, g + 0.3f, c.z);
            _dirt.Emit(new ParticleSystem.EmitParams { position = at, applyShapeToPosition = true }, 40);
            _dust.Emit(new ParticleSystem.EmitParams { position = new Vector3(c.x, g, c.z), applyShapeToPosition = true }, 6);
            _leafFx.Emit(new ParticleSystem.EmitParams { position = at + Vector3.up * 0.4f, applyShapeToPosition = true }, 26);
            _drops.Emit(new ParticleSystem.EmitParams { position = at + Vector3.up * 0.3f, applyShapeToPosition = true }, 30);
        }

        public override void MaterialCreated(Vol2Material m)
        {
            if (m.Kind != Vol2MaterialKind.Residue || _residue != null || m.Footprint == null) return;
            _residue = m;
            long now = m.CreatedUs;
            _resCentre = m.Footprint.Origin; _resRadius = Mathf.Max(0.8f, m.Footprint.Radius);
            _resAt = Inst.Rel(now); _releaseAt = _resAt;
            BuildGrowth();
            Note = "residue r=" + _resRadius.ToString("0.0") + " at " + _resAt.ToString("0.00");
        }

        public override void MaterialEnded(Vol2Material m, Vol2MaterialEnd why)
        {
            if (m != _residue) return;
            _resEnd = Inst.Rel(m.EndedUs);
            if (_leafFx != null) _leafFx.Emit(new ParticleSystem.EmitParams { position = _resCentre + Vector3.up * 0.4f, applyShapeToPosition = true }, 20);
        }

        void BuildGrowth()
        {
            var root = Root.transform;
            int seed = (int)(Inst.Cast.Value & 0x7fff) + 900;
            float g = _resCentre.y;
            for (int k = 0; k < 28; k++)
            {
                float a = Vol2Fs.Hash(seed, k) * Mathf.PI * 2f, r = _resRadius * Mathf.Sqrt(Vol2Fs.Hash(seed, k + 30)) * 1.6f;
                Vector3 b = _resCentre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r; b.y = g - 0.05f;
                float h = Mathf.Lerp(0.5f, 1.25f, Vol2Fs.Hash(seed, k + 60));
                Vector3 lean = (Vector3.up * 2f + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.6f).normalized;
                var tube = _grow.Add(new[] { b, b + lean * h * 0.5f + Vector3.right * 0.05f, b + lean * h }, 0.04f, 0.015f, 8);
                tube.Sharp = 0.15f; tube.Sides = 5; tube.Knots = 0.3f;
                for (int j = 0; j < 3; j++)
                {
                    Vector3 nrm = Quaternion.AngleAxis(Vol2Fs.Hash(seed, k * 3 + j + 200) * 360f, Vector3.up) * Vector3.forward;
                    _growLeaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = b + lean * h * (0.55f + 0.2f * j), Normal = nrm, Up = (Vector3.up + nrm).normalized, Size = 0.2f + 0.07f * j, Grow = 0f, Cell = (k + j) % 4 });
                }
            }
            // grass tufts: leaf cards standing in clumps
            for (int k = 0; k < 40; k++)
            {
                float a = Vol2Fs.Hash(seed, k + 400) * Mathf.PI * 2f, r = _resRadius * 1.7f * Mathf.Sqrt(Vol2Fs.Hash(seed, k + 430));
                Vector3 b = _resCentre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r; b.y = g;
                Vector3 nrm = Quaternion.AngleAxis(Vol2Fs.Hash(seed, k + 460) * 360f, Vector3.up) * Vector3.forward;
                _growLeaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = b, Normal = nrm, Up = Vector3.up, Size = 0.3f, Grow = 0f, Cell = k % 4 });
            }
            Vol2Fs.MeshObject(root, "Growth", BarkMat, out _growMesh);
            Vol2Fs.MeshObject(root, "GrowthLeaves", Kit.Leaf(new Color(0.8f, 0.95f, 0.55f), new Color(0.1f, 0.3f, 0.04f)), out _growLeafMesh);
            var moss = M("fs_moss", Color.white, 1f, 0f); moss.Soft = 0.001f; moss.NoiseAmt = 0.35f; moss.Erode = 0.25f; moss.Edge = new Color(0.3f, 0.8f, 0.15f, 1f); moss.EdgeWidth = 0.06f;
            Vol2Fs.MeshObject(root, "Moss", Kit.Mat(moss), out _mossMesh);
        }

        public override void Tick(long nowUs)
        {
            if (Root == null) return;
            float t = Now(nowUs);
            if (!_launched)
            {
                _charge.transform.position = Tip();
                _chargeLight.intensity = 3f * (LaunchRel > 0.02f ? Mathf.Clamp01(t / LaunchRel) : 1f);
                if (t >= LaunchRel - 1e-4f) Launch(nowUs); else return;
            }
            float a = t - _launchAt;
            // the runner follows the vol.2 head
            float head = Inst.HeadAlong(nowUs);
            float runnerGrow = _hitAlong > 0f ? Mathf.Clamp01((head - 0.6f) / Mathf.Max(0.1f, _hitAlong - 0.6f)) : 1f;
            if (_cageAt >= 0f) runnerGrow = 1f;
            float runnerFade = _releaseAt >= 0f ? Vol2Fs.Smooth(_releaseAt, _releaseAt + 0.6f, t) : 0f;
            _runner.Grow = runnerGrow * (1f - runnerFade);
            _root.Write(_rootMesh);
            Vector3 hp = Vol2FsTubes.TipOf(_runner);
            if (_headFx != null)
            {
                _headFx.transform.position = hp;
                if (runnerGrow >= 1f && _headFx.isEmitting) { _headFx.Stop(false, ParticleSystemStopBehavior.StopEmitting); _headLight.intensity = 0f; }
                else if (runnerGrow < 1f && runnerGrow > 0f) { _dirt.Emit(new ParticleSystem.EmitParams { position = hp, applyShapeToPosition = true }, 2); }
            }
            // ink along the path on the ground
            _crack.Clear();
            float pathLife = 1f - Vol2Fs.Smooth(0.5f, 1.6f, a);
            if (pathLife > 0.01f && runnerGrow > 0.02f)
            {
                var pts = new List<Vector3>(); var w = new List<float>(); var al = new List<float>(); var side = new List<Vector3>();
                for (int k = 0; k <= 12; k++)
                {
                    float u = k / 12f * runnerGrow;
                    Vector3 p = Vector3.Lerp(_runner.Ctrl[0], _runner.Ctrl[_runner.Ctrl.Length - 1], u); p.y = Ground + 0.02f;
                    pts.Add(p); w.Add(0.55f * (1f - 0.5f * u)); al.Add(pathLife); side.Add(Right);
                }
                _crack.Strip(pts, w, side, Color.white, al, 0.2f, CamPos, 0f, 1f);
            }
            _crack.Write(_crackMesh);
            // the cage
            if (_cageAt >= 0f && _cageMesh != null)
            {
                float ca = t - _cageAt;
                float loosen = _releaseAt >= 0f ? Vol2Fs.Smooth(_releaseAt, _releaseAt + 0.6f, t) : 0f;
                float wither = _resEnd >= 0f ? Vol2Fs.Smooth(_resEnd, _resEnd + 0.8f, t) : 0f;
                for (int k = 0; k < _cage.Tubes.Count; k++)
                {
                    float g = Vol2Fs.EaseOutBack((ca - _ribDelay[k]) / 0.32f);
                    _cage.Tubes[k].Grow = (ca < _ribDelay[k] ? 0f : g) * (1f - loosen) * (1f - wither);
                }
                foreach (var tb in _roots.Tubes) tb.Grow = Mathf.Clamp01(ca / 0.28f) * (1f - wither);
                // the cage follows the held body (it is pinned; this only keeps it honest if anything moves it)
                if (_cageT != null && _releaseAt < 0f) { Vector3 s = _cageT.position - _cageAnchor; _cageR.transform.position = s; _cageLeafR.transform.position = s; }
                _cage.Write(_cageMesh); _roots.Write(_rootsMesh);
                for (int k = 0; k < _cageLeaves.Items.Count; k++) { var l = _cageLeaves.Items[k]; l.Grow = Vol2Fs.Smooth(0.25f, 0.5f, ca) * (1f - wither) * (1f - loosen); /* r5: tip leaves sink with their ribs */ _cageLeaves.Items[k] = l; }
                _cageLeaves.Write(_cageLeafMesh);
                _stain.Clear();
                float sl = 1f - wither;
                if (sl > 0.01f) _stain.Quad(new Vector3(_cageB.center.x, _cageFeet.y + 0.02f, _cageB.center.z), Right, Fwd, (Mathf.Max(_cageB.size.x, _cageB.size.z) + 2.2f) * Vol2Fs.Smooth(0f, 0.25f, ca), new Color(1f, 1f, 1f, sl), 0.77f);
                _stain.Write(_stainMesh);
                _cageLight.intensity = (ca < 0.08f ? 9f * ca / 0.08f : Mathf.Lerp(9f, 2.6f, Mathf.Clamp01((ca - 0.08f) / 0.4f)) + 0.6f * Mathf.Sin(t * 7f)) * (1f - loosen * 0.7f) * (1f - wither);
                if (loosen > 0f && _motes.isEmitting) { _motes.Stop(false, ParticleSystemStopBehavior.StopEmitting); _glowPs.Stop(false, ParticleSystemStopBehavior.StopEmitting); }
                _burstS.Clear();
                var cam = CamPos; Vector3 hc = new Vector3(_cageB.center.x, _cageFeet.y + 0.5f, _cageB.center.z);
                var bp = new Vector3[6]; var bw = new float[6]; var ba = new float[6];
                foreach (var s in _burst)
                {
                    float grow = Vol2Fs.EaseOutBack(ca / 0.12f), life = 1f - Vol2Fs.Smooth(0.3f, 1.0f + 0.4f * s.Rnd, ca);
                    if (life <= 0.01f) continue;
                    for (int k = 0; k < 6; k++)
                    {
                        float u = k / 5f;
                        bp[k] = hc + s.Dir * (0.3f + s.Len * grow * u) + Vector3.down * 0.4f * s.Len * u * u * Vol2Fs.Smooth(0.05f, 0.7f, ca);
                        bw[k] = s.Width * (0.55f + 0.75f * Mathf.Sin(Mathf.Min(1f, u * 1.4f + 0.1f) * Mathf.PI)) * (1f - 0.55f * u);
                        ba[k] = life;
                    }
                    _burstS.Strip(bp, bw, null, Color.white, ba, s.Rnd, cam, 0f, 1f);
                }
                _burstS.Write(_burstMesh);
            }
            // the growth on the residue's disc
            if (_resAt >= 0f && _growMesh != null)
            {
                float ga = t - _resAt;
                float wither = _resEnd >= 0f ? Vol2Fs.Smooth(_resEnd, _resEnd + 0.8f, t) : 0f;
                for (int k = 0; k < _grow.Tubes.Count; k++) _grow.Tubes[k].Grow = Vol2Fs.EaseOutBack((ga - 0.03f * k) / 0.45f) * (1f - wither);
                _grow.Write(_growMesh);
                for (int k = 0; k < _growLeaves.Items.Count; k++) { var l = _growLeaves.Items[k]; l.Grow = Vol2Fs.Smooth(0.15f + 0.01f * (k % 9), 0.6f, ga) * (1f - wither); _growLeaves.Items[k] = l; }
                _growLeaves.Write(_growLeafMesh);
                _moss.Clear();
                float ml = Vol2Fs.Smooth(0f, 0.6f, ga) * (1f - wither);
                if (ml > 0.01f) _moss.Quad(new Vector3(_resCentre.x, _resCentre.y + 0.03f, _resCentre.z), Right, Fwd, _resRadius * 3.6f, new Color(1f, 1f, 1f, ml), 0.41f);
                _moss.Write(_mossMesh);
            }
        }

        public override bool Done(long nowUs)
        {
            if (Root == null) return true;
            float t = Now(nowUs);
            if (!_launched) return t > LaunchRel + 3f;
            if (_residue != null && _resEnd < 0f) return false;                 // the residue still stands (its end is the executor's)
            if (_cageAt >= 0f && _residue == null && t < _cageAt + 6f) return false;   // waiting for the cage to let go
            float end = _resEnd >= 0f ? _resEnd + 1f : _launchAt + 2f;
            return t > end && (!ParticlesAlive() || t > end + 2.5f);
        }
    }
}
