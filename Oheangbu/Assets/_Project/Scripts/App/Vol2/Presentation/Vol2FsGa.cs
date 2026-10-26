using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>가 (wood, ㅏ path, 한 번 · 붙든다) - free showcase batch 1, concept ga_a (a thorned living vine shot from the brush like a harpoon: two braided
    /// vines twist round the line to the first bear, an ink-and-leaf splash bursts at the hit, the vines whip into tight coils round its body and hold it; the
    /// end unravels into fine rootlets reaching forward). The hold is shown for the slow's own 2 s (the status sink owns the slow; the look only mirrors it).</summary>
    public sealed class Vol2FsGa : Vol2FsShot
    {
        const float HoldSeconds = 2.0f, PullSeconds = 0.45f, UnravelSeconds = 0.6f;
        static readonly Color Jade = new Color(0.45f, 1f, 0.35f), Ink = new Color(0.025f, 0.035f, 0.02f);
        static readonly Color Rim = new Color(0.5f, 1.6f, 0.35f, 1f);

        ParticleSystem _charge, _chargeLeaves, _leaves, _drops, _sparks, _dirt, _headGlow; Light _chargeLight, _headLight, _hitLight;
        readonly Vol2FsTubes _braid = new Vol2FsTubes(); Mesh _braidMesh;
        readonly Vol2FsTubes _coil = new Vol2FsTubes(); Mesh _coilMesh; MeshRenderer _coilR;
        readonly Vol2FsTubes _rootlets = new Vol2FsTubes(); Mesh _rootletMesh; MeshRenderer _rootletR;
        readonly Vol2FsLeaves _coilLeaves = new Vol2FsLeaves(); Mesh _coilLeafMesh; MeshRenderer _coilLeafR;
        Mesh _burstMesh; readonly Vol2FsStrips _burstS = new Vol2FsStrips(); List<Vol2FsB1.Stroke> _burst;
        Mesh _stainMesh; readonly Vol2FsStrips _stain = new Vol2FsStrips();
        Material _vine;
        Vector3 _feet0; bool _rootletsBuilt; float _unravelAt = -1f;

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsB1.M(tex, tint, intensity, addMix);
        protected override float LobHeight => 1.1f;      // r2: a whip's arch (r1's braid was end-on behind the shoulder)

        protected override void OnBegin()
        {
            var root = Root.transform;
            _vine = Kit.Bark(new Color(0.8f, 1f, 0.6f), new Color(0.35f, 1.3f, 0.25f) * 2.2f, 0.35f, "fs_vine", "fs_vein");      // r2: brighter (r1 read as a black coil)
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 3.5f, 1f)), Rate = 90f, Loop = true, Life0 = 0.18f, Life1 = 0.32f, Speed0 = -1.8f, Speed1 = -3f, Size0 = 0.05f, Size1 = 0.13f, Radius = 0.65f, World = false, Max = 120 }, root, "Charge");
            var leaf = M("fs_leaf", new Color(0.9f, 1f, 0.75f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _chargeLeaves = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Rate = 22f, Loop = true, Life0 = 0.3f, Life1 = 0.45f, Speed0 = -1.2f, Speed1 = -2f, Size0 = 0.1f, Size1 = 0.18f, Radius = 0.7f, Tiles = 2, Spin = 400f, Orbital = 4f, World = false, Max = 30 }, _charge.transform, "ChargeLeaves");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", Jade, 3.5f);
            _charge.transform.position = Tip();
            _charge.Play(true);
            // shared pools (emitted at points)
            _leaves = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 1.2f, Life1 = 2.4f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.14f, Size1 = 0.3f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.3f, Gravity = 0.3f, Drag = 1.5f,
                Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 400 }, root, "Leaves");
            var drop = M("fs_ink", Ink, 1f, 0f); drop.Edge = new Color(0.5f, 1.4f, 0.3f, 1f); drop.EdgeWidth = 0.16f; drop.NoiseAmt = 0.3f; drop.Soft = 0.1f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.7f, Life1 = 1.4f, Speed0 = 4f, Speed1 = 11f, Size0 = 0.1f, Size1 = 0.4f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.25f, Gravity = 2f, Drag = 0.4f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 400 }, root, "InkDrops");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 5f, 1f)), Life0 = 0.4f, Life1 = 1f, Speed0 = 1.5f, Speed1 = 6f, Size0 = 0.03f, Size1 = 0.07f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.3f,
                Drag = 1.4f, Stretch = true, StretchVel = 0.04f, Noise = 1.3f, NoiseFreq = 2f, Max = 500 }, root, "Sparks");
            var dirt = M("fs_ink", new Color(0.3f, 0.17f, 0.11f, 1f), 1f, 0f); dirt.NoiseAmt = 0.4f; dirt.Soft = 0.1f;
            _dirt = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dirt), Life0 = 0.5f, Life1 = 1f, Speed0 = 2f, Speed1 = 5f, Size0 = 0.06f, Size1 = 0.2f, Shape = ParticleSystemShapeType.Cone, Angle = 40f, Radius = 0.3f,
                ShapeRot = new Vector3(-90f, 0f, 0f), Gravity = 2.4f, Tiles = 2, AlphaHoldTo = 0.7f, Max = 300 }, root, "Dirt");
            foreach (var ps in new[] { _leaves, _drops, _sparks, _dirt }) ps.Play(false);
        }

        protected override void OnCharge(float t, float k)
        {
            _charge.transform.position = Tip();
            _chargeLight.intensity = 3.5f * k;
            _charge.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.2f, k);
        }

        protected override void OnLaunch(long nowUs)
        {
            var root = Root.transform;
            _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f;
            BuildBraid(From, HitPoint, 1f);
            Vol2Fs.MeshObject(root, "Braid", _vine, out _braidMesh);
            _headGlow = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 5f, 1f)), Rate = 90f, Loop = true, Life0 = 0.08f, Life1 = 0.12f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.7f, Size1 = 0.9f, Radius = 0.02f, World = false, Max = 20, SortFudge = -20 }, root, "HeadGlow");
            _headLight = Vol2Fs.PointLight(_headGlow.transform, "HeadLight", Jade, 5f); _headLight.intensity = 4f;
            _headGlow.transform.position = From;
            _headGlow.Play(false);
        }

        /// <summary>The braid: three thorned vines twisting round the line from <paramref name="a"/> to <paramref name="b"/> (3 turns), fat at the brush, thin at the tip.</summary>
        void BuildBraid(Vector3 a, Vector3 b, float radiusK)
        {
            _braid.Tubes.Clear();
            Vector3 axis = (b - a); float len = axis.magnitude; axis /= Mathf.Max(1e-4f, len);
            Vector3 n = Vector3.Cross(axis, Vector3.up).normalized; if (n == Vector3.zero) n = Vector3.right;
            Vector3 bn = Vector3.Cross(axis, n);
            for (int v = 0; v < 3; v++)
            {
                var pts = new Vector3[16];
                for (int k = 0; k < 16; k++)
                {
                    float u = k / 15f;
                    float ang = u * Mathf.PI * 2f * 3f + v * Mathf.PI * 2f / 3f;
                    float rad = 0.27f * radiusK * Mathf.Sin(Mathf.Clamp01(u * 1.3f) * Mathf.PI * 0.5f + 0.4f);
                    Vector3 on = Vector3.Lerp(a, b, u) + Vector3.up * 4f * LobHeight * u * (1f - u) * (a == From ? 1f : 0.08f);      // polish: taut once it holds (ga_a)
                    pts[k] = on + (n * Mathf.Cos(ang) + bn * Mathf.Sin(ang)) * rad;
                }
                var tube = _braid.Add(pts, 0.13f, 0.065f, 60); tube.Knots = 0.35f; tube.Sharp = 0.3f; tube.Sides = 6;
            }
            // thorns along the braid (short sharp cones off the vines)
            int nThorn = Mathf.Clamp(Mathf.RoundToInt(len * 5f), 6, 40);
            for (int k = 0; k < nThorn; k++)
            {
                float u = (k + 0.5f) / nThorn;
                int v = k % 3;
                var vine = _braid.Tubes[v];
                Vector3 p = Vol2FsTubes.CatmullRom(vine.Ctrl, u);
                Vector3 on = Vector3.Lerp(a, b, u) + Vector3.up * 4f * LobHeight * u * (1f - u) * (a == From ? 1f : 0.3f);
                Vector3 outw = (p - on).normalized; if (outw == Vector3.zero) outw = Vector3.up;
                Vector3 dir = (outw * 0.8f - axis * 0.5f).normalized;
                var th = _braid.Add(new[] { p, p + dir * 0.09f, p + dir * 0.17f }, 0.03f, 0.004f, 3); th.Sharp = 0.12f; th.Sides = 4;
            }
        }

        protected override void OnFly(float t, float f, float prev)
        {
            float len = Vector3.Distance(From, HitPoint);
            foreach (var tube in _braid.Tubes) tube.Grow = f * 1.02f;
            _braid.Write(_braidMesh);
            Vector3 head = Line(f);
            _headGlow.transform.position = head;
            int n = Mathf.CeilToInt((f - prev) * len * 4f);
            for (int k = 0; k < n; k++) { var p = Line(Mathf.Lerp(prev, f, (k + 0.5f) / n)); Vol2FsB1.Emit(_leaves, p, 1); Vol2FsB1.Emit(_sparks, p, 3); Vol2FsB1.Emit(_drops, p, 1); }
        }

        protected override void OnHit(long nowUs)
        {
            var root = Root.transform;
            _headGlow.Stop(false, ParticleSystemStopBehavior.StopEmitting); _headLight.intensity = 0f;
            foreach (var tube in _braid.Tubes) tube.Grow = 1f;
            _feet0 = FeetNow();
            // the splash of ink and leaves (ga_a): solid strokes with a jade rim, drops, leaves, sparks, soil
            Vector3 fwd = Flat(HitPoint - From), side = Vector3.Cross(Vector3.up, fwd);
            _burst = Vol2FsB1.Burst(Seed, 9, side, fwd, 1.3f, 2.5f, 0.17f, 0.3f, 0.75f, 0.5f);      // polish: a fuller splash (ga_a)      // r2: a small splash (r1 repeated the 나 star)
            Vol2Fs.MeshObject(root, "InkSplash", Kit.InkSolid("fs_stroke2", Ink, Rim, 0.14f, 0.5f, 0.28f, 1.6f), out _burstMesh);
            Vol2FsB1.Emit(_drops, HitPoint, 170); Vol2FsB1.Emit(_leaves, HitPoint, 200); Vol2FsB1.Emit(_sparks, HitPoint, 120); Vol2FsB1.Emit(_dirt, _feet0, 40);
            _hitLight = Vol2Fs.PointLight(root, "HitLight", Jade, 8f); _hitLight.transform.position = HitPoint;
            // the coils: three helical vines round the body (feet -> shoulders, ~2.5 turns each) + thorns, and leaves sprouting on them
            _coilR = Vol2Fs.MeshObject(root, "Coils", _vine, out _coilMesh);
            _coilR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var c = TB.center; float rx = TB.extents.x * 1.1f + 0.16f, rz = TB.extents.z * 1.1f + 0.16f, hy = TB.size.y * 1.15f;
            float g = _feet0.y;
            for (int v = 0; v < 3; v++)
            {
                var pts = new Vector3[22];
                float a0 = v * Mathf.PI * 2f / 3f + Vol2Fs.Hash(Seed, v);
                for (int k = 0; k < 22; k++)
                {
                    float u = k / 21f; float a = a0 + u * Mathf.PI * 2f * 2.4f * (v == 1 ? -1f : 1f);
                    float bulge = 1f + 0.12f * Mathf.Sin(u * Mathf.PI);
                    pts[k] = new Vector3(c.x + Mathf.Cos(a) * rx * bulge, g + 0.05f + u * hy * 1.02f, c.z + Mathf.Sin(a) * rz * bulge);
                }
                var tube = _coil.Add(pts, 0.12f, 0.07f, 70); tube.Knots = 0.3f; tube.Sharp = 0.25f; tube.Sides = 7;
            }
            for (int k = 0; k < 24; k++)
            {
                var vine = _coil.Tubes[k % 3]; float u = (k / 3 + 0.5f) / 8f;
                Vector3 p = Vol2FsTubes.CatmullRom(vine.Ctrl, u);
                Vector3 outw = new Vector3(p.x - c.x, 0.15f, p.z - c.z).normalized;
                var th = _coil.Add(new[] { p, p + outw * 0.1f, p + outw * 0.2f }, 0.035f, 0.004f, 3); th.Sharp = 0.14f; th.Sides = 4;
            }
            _coilLeafR = Vol2Fs.MeshObject(root, "CoilLeaves", Kit.Leaf(new Color(0.75f, 0.9f, 0.55f), new Color(0.12f, 0.35f, 0.05f)), out _coilLeafMesh);
            for (int k = 0; k < 24; k++)
            {
                var vine = _coil.Tubes[k % 3]; float u = 0.2f + 0.75f * Vol2Fs.Hash(Seed, k + 900);
                Vector3 p = Vol2FsTubes.CatmullRom(vine.Ctrl, u);
                Vector3 outw = new Vector3(p.x - c.x, 0f, p.z - c.z).normalized;
                _coilLeaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = p, Normal = Vector3.Cross(outw, Vector3.up), Up = (outw + Vector3.up * 0.6f).normalized, Size = 0.36f, Grow = 0f, Cell = k % 4 });
            }
            var stain = M("fs_ink", new Color(0.03f, 0.05f, 0.025f, 1f), 1f, 0f); stain.Soft = 0.001f; stain.NoiseAmt = 0.4f; stain.Opacity = 0.75f; stain.Edge = new Color(0.3f, 0.8f, 0.2f, 1f); stain.EdgeWidth = 0.06f;
            Vol2Fs.MeshObject(root, "Stain", Kit.Mat(stain), out _stainMesh);
            _rootletR = Vol2Fs.MeshObject(root, "Rootlets", _vine, out _rootletMesh);
        }

        void BuildRootlets()
        {
            _rootletsBuilt = true;
            Vector3 feet = FeetNow(); Vector3 fwd = Flat(HitPoint - From), side = Vector3.Cross(Vector3.up, fwd);
            for (int k = 0; k < 11; k++)
            {
                float s = (k / 10f - 0.5f) * 1.6f;
                Vector3 b = feet + side * s * 0.6f + fwd * 0.2f; b.y = Ground + 0.03f;
                Vector3 dir = (fwd + side * s * 0.6f).normalized;
                float len = 1.1f + 1.1f * Vol2Fs.Hash(Seed, k + 700);
                var pts = new Vector3[6];
                for (int j = 0; j < 6; j++)
                {
                    float u = j / 5f;
                    pts[j] = b + dir * len * u + side * Mathf.Sin(u * 7f + k) * 0.12f; pts[j].y = Ground + 0.03f + 0.04f * Mathf.Sin(u * 9f + k);
                }
                var tb = _rootlets.Add(pts, 0.035f, 0.008f, 18); tb.Knots = 0.5f; tb.Sharp = 0.3f; tb.Sides = 5;
            }
        }

        protected override void OnAfter(float t, float h, bool ended)
        {
            var cam = CamPos;
            _hitLight.intensity = h < 0.05f ? Mathf.Lerp(0f, 10f, h / 0.05f) : 10f * Mathf.Exp(-(h - 0.05f) * 5f);
            // the tether: the brush end of the braid lets go and is pulled into the coils (the vine is reeled into the body)
            float pull = Vol2Fs.Smooth(1.3f, 1.3f + PullSeconds, h);      // r2: the taut tether stays longer (the concept's read); polish: held to 1.3 s, its brush end on the brush
            Vector3 body = BodyNow();
            Vector3 shift = FeetNow() - _feet0;
            if (pull < 1f)
            {
                Vector3 a = Vector3.Lerp(Tip(), HitPoint + shift, pull);
                BuildBraid(a, HitPoint + shift, 1f - 0.5f * pull);
                if (pull <= 0f && Time.frameCount % 2 == 0) { float u = Random.value; Vol2FsB1.Emit(_sparks, Vector3.Lerp(a, HitPoint + shift, u), 2); }      // the strained vine sheds jade sparks
                foreach (var tube in _braid.Tubes) tube.Grow = 1f;
                _braid.Write(_braidMesh);
            }
            else if (_braid.Tubes.Count > 0) { _braid.Tubes.Clear(); _braid.Write(_braidMesh); }
            // the coils: grow fast, hold for the slow's seconds, then unravel (withdraw) while rootlets reach forward along the ground
            float grow = Vol2Fs.EaseOutBack(h / 0.28f);
            float unravel = Vol2Fs.Smooth(HoldSeconds, HoldSeconds + UnravelSeconds, h);
            if (unravel > 0f && _unravelAt < 0f) { _unravelAt = t; Vol2FsB1.Emit(_leaves, body, 30); Vol2FsB1.Emit(_dirt, FeetNow(), 20); BuildRootlets(); }
            foreach (var tube in _coil.Tubes) tube.Grow = Mathf.Clamp01(grow) * (1f - unravel);
            _coilR.transform.position = shift;
            _coil.Write(_coilMesh);
            for (int k = 0; k < _coilLeaves.Items.Count; k++) { var l = _coilLeaves.Items[k]; l.Grow = Vol2Fs.Smooth(0.2f + 0.02f * k, 0.5f + 0.02f * k, h) * (1f - unravel); _coilLeaves.Items[k] = l; }
            _coilLeafR.transform.position = shift;
            _coilLeaves.Write(_coilLeafMesh);
            // rootlets: grow forward, then sink
            if (_rootletsBuilt)
            {
                float rg = Vol2Fs.Smooth(0f, 0.5f, t - _unravelAt);
                float fade = Vol2Fs.Smooth(0.9f, 1.6f, t - _unravelAt);
                foreach (var tb in _rootlets.Tubes) tb.Grow = rg;
                _rootlets.Write(_rootletMesh);
                _rootletR.transform.position = Vector3.down * 0.12f * fade;
            }
            // the ink splash
            _burstS.Clear();
            Vol2FsB1.DrawBurst(_burstS, _burst, HitPoint + shift, h, cam, 0.4f, 0.1f);
            _burstS.Write(_burstMesh);
            // a dark green ink stain under the body (dries with the hold)
            _stain.Clear();
            float sl = 1f - Vol2Fs.Smooth(HoldSeconds, HoldSeconds + 1f, h);
            if (sl > 0.01f) _stain.Quad(new Vector3(_feet0.x, Ground + 0.02f, _feet0.z), Right, Fwd, 3.0f * Vol2Fs.Smooth(0f, 0.2f, h), new Color(1f, 1f, 1f, sl), 0.21f);
            _stain.Write(_stainMesh);
            // while held: the vines pulse a few jade sparks and a leaf now and then
            if (unravel <= 0f && h > 0.3f && Time.frameCount % 4 == 0) { Vol2FsB1.Emit(_sparks, body + Random.insideUnitSphere * 0.6f, 2); if (Time.frameCount % 16 == 0) Vol2FsB1.Emit(_leaves, body, 1); }
        }

        protected override float Linger => HoldSeconds + UnravelSeconds + 2.5f;

        public override bool Done(long nowUs)
        {
            if (Root == null || Gone) return true;
            float t = Now(nowUs);
            if (IsHit && t - HitAt < HoldSeconds + UnravelSeconds + 1.6f) return false;      // the hold + unravel + rootlets play out (the look's own seconds)
            return base.Done(nowUs);
        }
    }
}
