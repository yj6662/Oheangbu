using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>목>토 뿌리 붕괴 (꺾음: wood overcomes earth) - free showcase batch 8, concept we_a (jade roots burst up through the enemy's slabs, the slabs tip in and
    /// collapse, the ground cracks outward, the bear's legs are caught in the broken ground). Facts only: the commit consumes the shape and the enemy's earth patch
    /// (no child); the product is the SLOW on the bodies standing in the footprint for Dur (2 s, bind x0.5) - the roots hold the bear exactly that long, then
    /// wither and sink with the broken patch.</summary>
    public sealed class Vol2FsRxWoodEarth : Vol2FsReact
    {
        static readonly Color Jade = new Color(0.35f, 1f, 0.55f);
        readonly Vol2FsTubes _roots = new Vol2FsTubes();
        Mesh _rootMesh, _inkMesh, _crackMesh; MeshRenderer _rootR;
        readonly Vol2FsStrips _ink = new Vol2FsStrips(), _cracks = new Vol2FsStrips();
        List<Vol2FsB1.Stroke> _burst;
        ParticleSystem _dust, _rocks, _jade, _leaf;
        Light _light; Vol2FsCutAct _act; bool _rootsDone, _withered;
        readonly List<Vector3> _crackEnds = new List<Vector3>();

        protected override float Burst => 2.2f;

        protected override void OnBuild()
        {
            var root = Root.transform;
            _rootR = Vol2Fs.MeshObject(root, "Roots", Kit.Bark(new Color(0.78f, 0.92f, 0.6f), new Color(0.3f, 1.2f, 0.4f) * 3f, 0.25f, "fs_vine", "fs_vein"), out _rootMesh);
            _rootR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            Vol2Fs.MeshObject(root, "Cracks", Kit.InkSolid("fs_stroke2", Vol2FsRxBreakKit.Ink, Jade * 2.2f, 0.18f, 0.45f, 0.3f, 1.3f), out _crackMesh);
            Vol2Fs.MeshObject(root, "InkBurst", Kit.InkSolid("fs_stroke2", Vol2FsRxBreakKit.Ink, Jade * 2.6f, 0.16f, 0.5f, 0.28f, 1.6f), out _inkMesh);
            // nine roots: out of the ground under the patch, up through the slabs, arcing down past the rim
            for (int k = 0; k < 11; k++)
            {
                float a = (k / 11f + 0.07f * Vol2Fs.Hash(Seed, k)) * Mathf.PI * 2f;
                Vector3 o = Dir * Mathf.Cos(a) + Side * Mathf.Sin(a);
                float h = 1.2f + 0.9f * Vol2Fs.Hash(Seed, k + 10), reach = Radius * (2.6f + 0.9f * Vol2Fs.Hash(Seed, k + 20));      // r2: x1.6 (r1 ~1 m against the concept's 3 - 5 m)
                var ctrl = new[] { G(P) + o * 0.08f - Vector3.up * 0.3f, G(P) + o * Radius * 0.35f + Vector3.up * h * 0.7f, G(P) + o * Radius * 0.85f + Vector3.up * h,
                                   G(P) + o * reach * 0.8f + Vector3.up * h * 0.45f, G(P) + o * reach - Vector3.up * 0.15f };
                var tb = _roots.Add(ctrl, 0.13f, 0.03f, 26); tb.Knots = 0.4f; tb.Sides = 7; tb.Sharp = 0.12f;
                _crackEnds.Add(G(P) + o * reach * 1.15f);
            }
            // r2: three roots run from the patch to each body and coil up its legs (the slow reads ON the bear)
            foreach (var s in Bodies)
                for (int k = 0; k < 3; k++)
                {
                    var coil = Vol2FsB4F.Coil(s.B, Ground - 0.05f, 8, k * 2.09f + 0.4f, 1.1f, 0.5f, 0.1f);
                    var ctrl = new Vector3[coil.Length + 2];
                    ctrl[0] = G(P) + (Side * (k - 1) * 0.3f) - Vector3.up * 0.25f; ctrl[1] = Vector3.Lerp(G(P), G(s.Feet), 0.5f) + Vector3.up * (0.5f + 0.2f * k);
                    for (int i = 0; i < coil.Length; i++) ctrl[i + 2] = coil[i];
                    var tb = _roots.Add(ctrl, 0.1f, 0.03f, 30); tb.Knots = 0.35f; tb.Sides = 7; tb.Sharp = 0.1f;
                }
            _burst = Vol2FsB1.Burst(Seed, 16, Side, Dir, 2.6f, 4.4f, 0.3f, 0.56f, 0.5f, 0.2f);
            var dust = Vol2FsB3.Dust(new Color(0.55f, 0.42f, 0.32f, 1f), 0.6f);
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dust), Life0 = 0.8f, Life1 = 1.6f, Speed0 = 1f, Speed1 = 3.5f, Size0 = 0.8f, Size1 = 1.6f, SizeStart = 0.5f, SizePeak = 1.2f, SizeEnd = 1.8f, Drag = 1.6f, Radius = 0.5f, Max = 200 }, root, "Dust");
            _rocks = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.58f, 0.5f, 0.46f), 0f, 0.12f), Mesh = Kit.SharedMesh("rxbreak_rock", () => Vol2FsB1.Rock(931, 0.5f)), Life0 = 0.8f, Life1 = 1.4f, Speed0 = 2.5f, Speed1 = 6f, Size0 = 0.06f, Size1 = 0.2f, Gravity = 2.4f, Drag = 0.25f, Spin = 500f, Max = 200 }, root, "Rocks");
            _jade = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 4f, 1f)), Life0 = 0.5f, Life1 = 1.2f, Speed0 = 0.6f, Speed1 = 3f, Size0 = 0.03f, Size1 = 0.08f, Drag = 1.3f, Noise = 1.2f, NoiseFreq = 1.8f, Max = 400 }, root, "Jade");
            var leaf = M("fs_leaf", new Color(0.85f, 1f, 0.7f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leaf = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 1f, Life1 = 1.8f, Speed0 = 1.5f, Speed1 = 4f, Size0 = 0.12f, Size1 = 0.24f, Gravity = 0.35f, Drag = 1.5f, Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 200 }, root, "Leaves");
            foreach (var ps in new[] { _dust, _rocks, _jade, _leaf }) ps.Play(false);
            Vector3 c = G(P) + Vector3.up * 0.3f;
            for (int k = 0; k < 26; k++) Vol2FsB1.EmitV(_dust, c + Random.insideUnitSphere * Radius * 0.6f, (Vol2FsRxBreakKit.Flat(Random.insideUnitSphere) * 2.6f + Vector3.up * 1.2f), -1f, -1f);
            for (int k = 0; k < 36; k++) Vol2FsB1.EmitV(_rocks, c + Random.insideUnitSphere * 0.4f, Vol2FsRxBreakKit.Flat(Random.insideUnitSphere) * Random.Range(1.5f, 4.5f) + Vector3.up * Random.Range(2f, 5f), -1f, -1f);
            Vol2FsB1.Emit(_jade, c, 300); Vol2FsB1.Emit(_leaf, c, 80);
            _light = Vol2Fs.PointLight(root, "Jade", Jade, 10f); _light.transform.position = c + Vector3.up * 0.9f; _light.intensity = 14f;
            _act = new Vol2FsCutAct(Kit, root, Body.Wood, Seed, AccA);
            foreach (var s in Bodies) if (s.T != null) _act.Begin(s.T, s.B, s.Feet, 0f, s.Dir);
            HitStop(0f, 0.09f);
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            // the roots shoot up in 0.15 s (상극 crack), then hold the ground while the slow lasts
            if (!_rootsDone)
            {
                float g = Vol2Fs.EaseOutBack(Mathf.Clamp01(t / 0.16f));
                foreach (var tb in _roots.Tubes) tb.Grow = g;
                _roots.Write(_rootMesh);
                if (t > 0.2f) _rootsDone = true;
            }
            // the slabs tip inward and sink (0.25 s fold), the patch goes down under the roots
            if (Patch != null)
            {
                float k = Vol2Fs.Smooth(0.05f, 0.4f, t);
                for (int i = 0; i < Patch.Props.Count; i++)
                {
                    var tr = Patch.Props[i]; if (tr == null) continue;
                    Vector3 rest = Patch.RestPos[i]; Vector3 inw = -Vol2FsRxBreakKit.Flat(rest);
                    tr.localPosition = rest + inw * 0.18f * k + Vector3.down * (0.32f * k + 0.25f * Vol2Fs.Smooth(Dur, Dur + 0.9f, t));
                    tr.localRotation = Quaternion.AngleAxis(-55f * k * (0.7f + 0.3f * Vol2Fs.Hash(Seed, i)), Vector3.Cross(Vector3.up, inw)) * Patch.RestRot[i];
                }
                Patch.Pose(1f, 0f, 1f + 0.35f * k);
            }
            // the roots wither and sink when the slow has run out
            if (_rootR != null)
            {
                float w = Vol2Fs.Smooth(Dur, Dur + 0.8f, t);
                _rootR.transform.localPosition = Vector3.down * 1.1f * w;
                if (w > 0.99f && !_withered) { _withered = true; _rootR.enabled = false; }
            }
            // cracks run out from the patch (the support broken), ink strokes burst
            _cracks.Clear();
            float life = 1f - Vol2Fs.Smooth(Dur, Dur + 0.7f, t);
            for (int i = 0; i < _crackEnds.Count; i++)
                Vol2FsB6.Crack(_cracks, G(P), _crackEnds[i], Ground + 0.01f, 0.14f, 0f, Vol2Fs.Smooth(0f, 0.25f, t), life, 0.2f + 0.07f * i, Seed + i * 7, 0.18f, 0.02f);
            foreach (var s in Bodies)
            {
                if (!Vol2FsRxBreakKit.Now(s, out var b, out var feet)) continue;
                Vol2FsB4A.GroundArc(_cracks, feet, Ground + 0.015f, Mathf.Max(b.extents.x, b.extents.z) + 0.25f, 0.16f, 0f, Mathf.PI * 2f, life * Vol2Fs.Smooth(0.05f, 0.25f, t), 0.7f, cam, 28);
            }
            _cracks.Write(_crackMesh);
            _ink.Clear(); Vol2FsB1.DrawBurst(_ink, _burst, G(P) + Vector3.up * 0.45f, t, cam, 0.3f, 0.1f); _ink.Write(_inkMesh);
            if (_light != null) _light.intensity = Mathf.MoveTowards(_light.intensity, t < Dur ? 1.2f : 0f, Time.deltaTime * 14f);
            _act.Tick(t, cam, Ground);
        }
    }

    /// <summary>Shared helpers of the five 꺾음 (Break) looks of batch 8.</summary>
    public static class Vol2FsRxBreakKit
    {
        public static readonly Color Ink = new Color(0.035f, 0.028f, 0.024f);
        public static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        /// <summary>A product body's bounds and feet now; false when the body went away (the lab's teardown).</summary>
        public static bool Now(Vol2FsReact.Struck s, out Bounds b, out Vector3 feet)
        {
            b = s.B; feet = s.Feet;
            if (s.T == null) return false;
            feet = s.T.position;
            if (float.IsNaN(feet.x + feet.y + feet.z) || float.IsInfinity(feet.x + feet.y + feet.z)) return false;
            b = Vol2FsRunBounds.Of(s.T, feet, Mathf.Max(0.3f, Mathf.Max(s.B.extents.x, s.B.extents.z)));
            return true;
        }

        /// <summary>A patch prop of this name (Vines / Thorns / Leaves / Flames / Coal0 ...), or null.</summary>
        public static Transform Prop(Vol2FsPatch p, string name)
        {
            if (p == null) return null;
            foreach (var t in p.Props) if (t != null && t.name == name) return t;
            return null;
        }
    }
}
