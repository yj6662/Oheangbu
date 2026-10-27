using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>The 비화 (Same) group's shared bits (batch 8): the merge (the acting shape's last stretch folding INTO the patch - no attack burst), a thin joining
    /// ring and the fading ghost outline of the patch's old size, a short flash of light; the thicken pop (≤0.2 s) and the exit clock. Presentation only.</summary>
    public sealed class Vol2FsRxSameFx
    {
        readonly Vol2FsStrips _s = new Vol2FsStrips();
        readonly Mesh _mesh; readonly Light _light; readonly float _lightMax;
        public Vol2FsRxSameFx(Vol2FsKit kit, Transform root, Color acc, float lightMax = 5f)
        {
            Vol2Fs.MeshObject(root, "SameJoin", Vol2FsB4A.PhantomMat(kit, acc, 3.4f), out _mesh);
            _light = Vol2Fs.PointLight(root, "SameLight", acc, 9f); _lightMax = lightMax;
        }

        /// <summary>The thicken pop: 0 -> 1 in <paramref name="dur"/> with a little overshoot (EaseOutBack), never NaN.</summary>
        public static float Pop(float t, float dur = 0.2f) => t <= 0f ? 0f : Vol2Fs.EaseOutBack(Mathf.Clamp01(t / Mathf.Max(0.01f, dur)));
        /// <summary>The exit clock (0 before the contact's end; 1 = gone): Expired = the body's slow natural end, any other reason quick.</summary>
        public static float Exit(float t, float endAt, Vol2MaterialEnd why, float natural = 1f) => endAt < 0f ? 0f : Vol2Fs.Smooth(endAt, endAt + (why == Vol2MaterialEnd.Expired ? natural : 0.3f), t);
        /// <summary>0..1: how close the renewed life is to its end (the last <paramref name="span"/> s).</summary>
        public static float Near(float t, float expireRel, float span = 1.4f) => expireRel <= 0f ? 0f : 1f - Mathf.Clamp01((expireRel - t) / Mathf.Max(0.1f, span));

        /// <summary>r2: the merge is bigger and brighter than the acting glyph's own projectile (five wide fibres from 3.2 m out), a wide joining ring, a ring that
        /// RISES through the patch's new height ("one layer stacked on"), the ghost outline of the old size, a stronger flash.</summary>
        public void Draw(float t, Vector3 p, Vector3 dir, float ground, float radius, float grownRadius, Vector3 cam, float rise = 1.4f)
        {
            _s.Clear();
            // the merge: five fibres of the shape's last stretch converge into the patch (0 - 0.22 s)
            if (t < 0.3f)
            {
                float u = Mathf.Clamp01(t / 0.2f), life = 1f - Vol2Fs.Smooth(0.18f, 0.3f, t);
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                for (int k = -2; k <= 2; k++)
                {
                    Vector3 a = p - dir * 3.2f + side * 0.55f * k + Vector3.up * (1.2f + 0.18f * k), b = p + Vector3.up * 0.45f + side * 0.15f * k;
                    var pts = new Vector3[10]; var w = new float[10]; var al = new float[10];
                    for (int i = 0; i < 10; i++)
                    {
                        float v = Mathf.Lerp(Mathf.Max(0f, u - 0.6f), u, i / 9f);
                        pts[i] = Vector3.Lerp(a, b, v) + Vector3.up * 0.35f * Mathf.Sin(v * Mathf.PI) + side * 0.2f * k * Mathf.Sin(v * Mathf.PI);
                        w[i] = 0.24f * Mathf.Sin(Mathf.Clamp01(i / 9f) * Mathf.PI * 0.9f + 0.1f); al[i] = life;
                    }
                    _s.Strip(pts, w, null, Color.white, al, 0.2f + 0.13f * k, cam);
                }
            }
            // a wide joining ring runs out over the grown patch (not an explosion)
            if (t > 0.03f && t < 0.9f)
            {
                float k = Vol2Fs.Smooth(0.03f, 0.7f, t);
                Vol2FsB4A.GroundArc(_s, p, ground + 0.03f, Mathf.Lerp(radius * 0.5f, grownRadius * 1.2f, k), Mathf.Lerp(0.32f, 0.05f, k), 0f, Mathf.PI * 2f, 1f - k, 0.4f, cam, 56);
            }
            // a ring that rises through the new height: one layer stacked on
            if (t > 0.06f && t < 0.75f)
            {
                float k = Vol2Fs.Smooth(0.06f, 0.5f, t);
                Vol2FsB4A.GroundArc(_s, p, ground + rise * k, grownRadius * Mathf.Lerp(1.05f, 0.75f, k), Mathf.Lerp(0.2f, 0.06f, k), 0f, Mathf.PI * 2f, (1f - Vol2Fs.Smooth(0.45f, 0.75f, t)) * 0.9f, 0.55f, cam, 56);
            }
            // the ghost outline of the patch's OLD size: what it was before it thickened
            if (t > 0.08f && t < 1.9f)
            {
                float life = Vol2Fs.Smooth(0.08f, 0.2f, t) * (1f - Vol2Fs.Smooth(0.9f, 1.9f, t)) * 0.85f;
                Vol2FsB4A.GroundArc(_s, p, ground + 0.02f, radius, 0.09f, 0f, Mathf.PI * 2f, life, 0.7f, cam, 56);
            }
            _s.Write(_mesh);
            if (_light != null)
            {
                _light.transform.position = p + Vector3.up * 1.0f;
                _light.intensity = _lightMax * 1.6f * Mathf.Exp(-Mathf.Max(0f, t) * 5f);
            }
        }
    }
    /// <summary>목>목 비화 - free showcase batch 8, concept ww_b (grafting surge): the vine shape splits into fibres that graft onto the enemy's vines; in a heartbeat
    /// the whole tangle heaves up and outward - thick dark arching vines (jade sap glowing in their veins) rising 1.5 - 1.9 m and reaching 1.5 m out, new leaves,
    /// the ghost of its old size on the ground. It stays the enemy's tangle (dark ink bark). It stands for the thickened patch's renewed life; Expired = the
    /// vines wither from their tips and the leaves fall; any other end = it shrinks at once.</summary>
    public sealed class Vol2FsRxWoodWood : Vol2FsReact
    {
        readonly Vol2FsTubes _vines = new Vol2FsTubes(), _thorns = new Vol2FsTubes();
        readonly Vol2FsLeaves _leaves = new Vol2FsLeaves();
        readonly List<float> _leafAt = new List<float>(), _tubeAt = new List<float>();
        Mesh _vineMesh, _thornMesh, _leafMesh; Material _bark;
        ParticleSystem _jade, _leafFall, _dust; Vol2FsRxSameFx _fx;
        bool _fell;

        protected override void OnBuild()
        {
            _fx = new Vol2FsRxSameFx(Kit, Root.transform, AccA, 4f);
            _bark = Kit.Bark(new Color(0.52f, 0.46f, 0.52f), new Color(0.22f, 1.1f, 0.42f) * 3.6f, 0.2f, "fs_vine", "fs_vein");
            Vol2Fs.MeshObject(Root.transform, "SameVines", _bark, out _vineMesh);
            Vol2Fs.MeshObject(Root.transform, "SameThorns", _bark, out _thornMesh);
            Vol2Fs.MeshObject(Root.transform, "SameLeaves", Kit.Leaf(new Color(0.5f, 0.62f, 0.44f), new Color(0.2f, 0.6f, 0.3f) * 0.4f), out _leafMesh);
            int n = 11;      // r2: x1.7 (r1 ~1 m against the concept's 3 - 5 m)
            for (int k = 0; k < n; k++)
            {
                float a = (k / (float)n + 0.08f * Vol2Fs.Hash(Seed, k)) * Mathf.PI * 2f, h = 1.9f + 0.9f * Vol2Fs.Hash(Seed, k + 10), far = 1.9f + 0.8f * Vol2Fs.Hash(Seed, k + 20);
                Vector3 o = Dir * Mathf.Cos(a) + Side * Mathf.Sin(a);
                Vector3 b = G(P + o * Radius * 0.3f);
                var ctrl = new[] { b + Vector3.down * 0.06f, b + o * 0.2f + Vector3.up * 0.45f * h, b + o * 0.55f * far + Vector3.up * 1.0f * h, b + o * 0.95f * far + Vector3.up * 1.2f * h, b + o * 1.3f * far + Vector3.up * 0.95f * h, b + o * 1.45f * far + Vector3.up * 0.65f * h };
                var tb = _vines.Add(ctrl, 0.15f + 0.05f * Vol2Fs.Hash(Seed, k + 30), 0.025f, 30); tb.Knots = 0.4f; tb.Sides = 7; tb.Sharp = 0.18f;
                _tubeAt.Add(0.02f * k);
                // two twigs off each arch
                for (int j = 0; j < 2; j++)
                {
                    Vector3 s0 = Vol2FsTubes.CatmullRom(ctrl, 0.35f + 0.25f * j);
                    Vector3 to = (o * 0.4f + Vector3.up * 0.6f + Vector3.Cross(o, Vector3.up) * (j == 0 ? 0.5f : -0.5f)).normalized;
                    var tw = _vines.Add(new[] { s0, s0 + to * 0.5f, s0 + to * 0.9f + Vector3.up * 0.15f }, 0.055f, 0.01f, 10); tw.Sharp = 0.1f; tw.Sides = 5;
                    _tubeAt.Add(0.06f + 0.02f * k + 0.04f * j);
                    _leaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = s0 + to * 0.85f, Normal = to + Vector3.up * 0.2f, Up = (Vector3.up + to).normalized, Size = 0.5f, Grow = 0f, Cell = (k + j) % 4 }); _leafAt.Add(0.14f + 0.02f * k);
                }
                for (int j = 0; j < 5; j++)
                {
                    Vector3 q = Vol2FsTubes.CatmullRom(ctrl, 0.2f + 0.15f * j);
                    Vector3 sp = new Vector3(Vol2Fs.Hash(Seed, k * 9 + j) - 0.5f, 0.45f, Vol2Fs.Hash(Seed, k * 9 + j + 50) - 0.5f).normalized;
                    var th = _thorns.Add(new[] { q, q + sp * 0.32f }, 0.05f, 0.003f, 3); th.Sharp = 0.1f; th.Sides = 4;
                }
                for (int j = 0; j < 2; j++)
                {
                    Vector3 q = Vol2FsTubes.CatmullRom(ctrl, 0.6f + 0.3f * j);
                    _leaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = q, Normal = o + Vector3.up * 0.3f, Up = (Vector3.up + o * 0.5f).normalized, Size = 0.55f, Grow = 0f, Cell = (k + j + 1) % 4 }); _leafAt.Add(0.12f + 0.025f * k);
                }
            }
            var jade = M("fs_dot", AccA, 4.5f, 1f);
            _jade = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(jade), Life0 = 0.35f, Life1 = 0.8f, Speed0 = 0.5f, Speed1 = 3f, Size0 = 0.05f, Size1 = 0.11f, Drag = 1.6f, Noise = 1.2f, NoiseFreq = 2f, Max = 500 }, Root.transform, "SameJade");
            var leaf = M("fs_leaf", new Color(0.55f, 1f, 0.6f, 1f), 1.7f, 0.35f);      // r2: lit (r1's plain leaf sheet read as black blobs) leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leafFall = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 0.2f, Speed1 = 1.2f, Size0 = 0.16f, Size1 = 0.3f, Gravity = 0.2f, Drag = 1.2f, Noise = 1f, NoiseFreq = 0.8f, Tiles = 2, Spin = 240f, AlphaHoldTo = 0.75f, Max = 220 }, Root.transform, "SameLeafFall");
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.42f, 0.32f, 0.26f, 1f), 0.5f)), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 0.6f, Speed1 = 2.2f, Size0 = 0.3f, Size1 = 0.6f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.6f, Drag = 1.6f, Max = 120 }, Root.transform, "SameDust");
            foreach (var ps in new[] { _jade, _leafFall, _dust }) ps.Play(false);
            // the merge: fibres pour into the tangle, sap lights up, soil kicks up as it heaves
            for (int k = 0; k < 200; k++) Vol2FsB1.EmitV(_jade, P + Vector3.up * 0.5f - Dir * 0.4f, (Dir * 3f + Random.insideUnitSphere * 3.5f + Vector3.up * 2f), -1f, -1f);
            for (int k = 0; k < 24; k++) { Vector3 o = Random.insideUnitCircle; Vol2FsB1.EmitV(_dust, G(P + new Vector3(o.x, 0f, o.y) * Radius * 2f) + Vector3.up * 0.1f, new Vector3(o.x, 0.4f, o.y) * 1.8f, -1f, -1f); }
            HitStop(0f, 0.06f);
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            float pop = Vol2FsRxSameFx.Pop(t, 0.22f), exit = Vol2FsRxSameFx.Exit(t, ContactEndAt, ContactWhy, 1.1f), near = Vol2FsRxSameFx.Near(t, ContactExpireRel);
            bool expired = ContactWhy == Vol2MaterialEnd.Expired;
            for (int i = 0; i < _vines.Tubes.Count; i++)
            {
                float g = Vol2FsRxSameFx.Pop(t - _tubeAt[i], 0.2f);
                // Expired: the vines wither from their tips (grow shrinks), else they drop at once
                _vines.Tubes[i].Grow = Mathf.Max(0f, g * (1f - exit * (expired ? 0.85f : 1f)));
            }
            for (int i = 0; i < _thorns.Tubes.Count; i++) _thorns.Tubes[i].Grow = Mathf.Clamp01(pop) * (1f - exit);
            for (int i = 0; i < _leaves.Items.Count; i++) { var l = _leaves.Items[i]; l.Grow = Vol2FsRxSameFx.Pop(t - _leafAt[i], 0.25f) * (1f - Mathf.Max(exit, near * 0.35f)); _leaves.Items[i] = l; }
            _vines.Write(_vineMesh); _thorns.Write(_thornMesh); _leaves.Write(_leafMesh);
            if (Patch != null) Patch.Pose(1f + 0.7f * pop * (1f - exit), exit * Patch.Height, 1f + 0.4f * pop);
            // a few leaves drift off the thicker tangle, more toward its end; the wither drops them all
            if (exit > 0f && !_fell) { _fell = true; for (int k = 0; k < (expired ? 60 : 25); k++) Vol2FsB1.EmitV(_leafFall, G(P) + Vector3.up * Random.Range(0.6f, 2.6f) + Random.insideUnitSphere * Radius * 2f, Random.insideUnitSphere * 1.2f + Vector3.up * 0.3f, -1f, -1f); }
            if (exit <= 0f && t > 0.3f && Random.value < 0.06f + 0.3f * near) Vol2FsB1.EmitV(_leafFall, G(P) + Vector3.up * Random.Range(1.2f, 2.6f) + Random.insideUnitSphere * Radius * 2f, Random.insideUnitSphere * 0.4f, -1f, -1f);
            _fx.Draw(t, P, Dir, Ground, Radius, Radius * 2.8f, cam, 2.4f);
        }
    }
}
