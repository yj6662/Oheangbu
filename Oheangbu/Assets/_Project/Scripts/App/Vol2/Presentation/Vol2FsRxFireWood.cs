using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>화>목 들불 (옮겨붙음: fire catches the enemy's wood) - free showcase batch 8, concept fw_c (the whole tangle outlined in fire: a burning lattice of
    /// black-cored flames standing exactly in the shape of the vines) with fw_b's running front (the fire races along the vines from where the projectile entered).
    /// Facts only: at the commit the enemy's wood patch and the fire shape are consumed and a fire CHILD (mine; the enemy's when an enemy fire acted) stands in the
    /// patch's own shape for its live 10 s. The look: an ignition front runs along every vine of the consumed tangle (the same arcs the patch drew - same seed) from
    /// the side the fire came in; behind it black ink tongues with hot orange cores lick up along the vine lines (no round pit, no ring, nothing at the bear's feet),
    /// the leaves flash off as embers, the vines char. The fire burns lower over the child's life (monotone), dims in its last second; Expired = it burns down to
    /// rising ash and the charred vines crumble; Consumed / ChildConsumed = it dries out in place in 0.3 s.</summary>
    public sealed class Vol2FsRxFireWood : Vol2FsReact
    {
        static readonly Color Orange = new Color(1f, 0.42f, 0.1f), Hot = new Color(1f, 0.8f, 0.45f);
        readonly List<Vector3[]> _arcs = new List<Vector3[]>();
        readonly List<float> _arcStart = new List<float>(), _arcLen = new List<float>();
        Mesh _tongueMesh, _bodyMesh, _coreMesh, _burstMesh;
        readonly Vol2FsStrips _tongues = new Vol2FsStrips(), _body = new Vol2FsStrips(), _core = new Vol2FsStrips(), _burst = new Vol2FsStrips();
        ParticleSystem _licks, _embers, _ash, _soot, _sparks;
        List<Vol2FsB1.Stroke> _ink;
        Light _light;
        const float Speed = 6.5f, Scale = 1.8f;
        bool _leavesOff, _ashed;

        protected override float Burst => 1.6f;

        protected override void OnBuild()
        {
            var root = Root.transform;
            bool dark = EnemyIgniter;
            Vol2FsRxTransferKit.WoodArcs(V.ContactId, Radius, _arcs);
            // r2: the fire stands in the tangle's shape but drawn 1.8x wide, 1.6x high (r1: ~1 m, under the acting glyph's own fireball)
            foreach (var a in _arcs) for (int k = 0; k < a.Length; k++) a[k] = new Vector3(a[k].x * Scale, Mathf.Max(0f, a[k].y) * 0.85f, a[k].z * Scale);      // r3: low (r2 x1.6 stacked into one bonfire mass)
            // where the fire came in: the patch rim facing the incoming direction; each vine starts burning at its point nearest to it, later the farther it is
            Vector3 entry = -Dir * Radius * Scale;
            foreach (var a in _arcs)
            {
                float best = float.MaxValue, len = 0f; int bi = 0;
                for (int k = 0; k < a.Length; k++) { float d = (a[k] - entry).magnitude; if (d < best) { best = d; bi = k; } if (k > 0) len += (a[k] - a[k - 1]).magnitude; }
                _arcStart.Add(0.04f + best / Speed); _arcLen.Add(Mathf.Max(0.3f, len));
            }
            var tongue = Kit.InkSolid("fs_stroke2", dark ? new Color(0.05f, 0.02f, 0.05f) : InkC, (dark ? new Color(0.7f, 0.2f, 0.35f) : Orange) * 1.8f, 0.2f, 0.55f, 0.33f, 1.4f);
            Vol2Fs.MeshObject(root, "BlackTongues", tongue, out _tongueMesh);
            var body = M("fs_flame", dark ? new Color(0.75f, 0.25f, 0.2f) : Orange, dark ? 1.2f : 1.8f, 0.85f); body.NoiseAmt = 0.6f; body.Erode = 0.28f; body.Soft = 0.1f; body.Scroll = new Vector2(0f, -1.6f);
            Vol2Fs.MeshObject(root, "FireBody", Kit.Mat(body), out _bodyMesh);
            var core = M("fs_streak", dark ? new Color(0.9f, 0.35f, 0.3f) : Hot, dark ? 1.4f : 2.6f, 1f); core.NoiseAmt = 0.35f; core.Soft = 0.04f; core.Scroll = new Vector2(-3f, 0f);
            Vol2Fs.MeshObject(root, "FireCore", Kit.Mat(core), out _coreMesh);
            Vol2Fs.MeshObject(root, "InkBurst", Kit.InkSolid("fs_stroke", InkC, Orange * 1.6f, 0.14f, 0.5f, 0.35f, 1.2f), out _burstMesh);
            _licks = Vol2Fs.Build(Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick(dark ? 1.0f : 1.4f, 0.55f, dark ? 0.9f : 1.1f)), 0.2f, 0.42f, 0.35f, 0.65f, 1.1f, 1400), root, "Licks");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.55f, 0.2f), 4f, 1f)), Life0 = 0.6f, Life1 = 1.6f, Speed0 = 0.4f, Speed1 = 2.6f, Size0 = 0.02f, Size1 = 0.05f, Gravity = -0.45f, Drag = 0.8f, Noise = 1f, NoiseFreq = 1.5f, Max = 1200 }, root, "Embers");
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Ash()), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 0.2f, Speed1 = 1.0f, Size0 = 0.04f, Size1 = 0.1f, Gravity = -0.3f, Noise = 0.8f, NoiseFreq = 1.2f, Spin = 200f, Max = 700 }, root, "Ash");
            _soot = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsPolish.Soot(0.45f)), Life0 = 1.0f, Life1 = 1.8f, Speed0 = 0.2f, Speed1 = 0.6f, Size0 = 0.45f, Size1 = 0.9f, SizeStart = 0.6f, SizePeak = 1.1f, SizeEnd = 1.6f, Gravity = -0.2f, Drag = 1f, Max = 260 }, root, "Soot");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Hot, 5f, 1f)), Life0 = 0.25f, Life1 = 0.6f, Speed0 = 3f, Speed1 = 9f, Size0 = 0.02f, Size1 = 0.045f, Gravity = 1.2f, Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.3f, Max = 400 }, root, "Sparks");
            foreach (var ps in new[] { _licks, _embers, _ash, _soot, _sparks }) ps.Play(false);
            _light = Vol2Fs.PointLight(root, "FireLight", Orange, 7f);
            _light.transform.position = P + Vector3.up * 0.6f;
            // the contact: a fast black-and-fire ink burst where the fire came in, sparks, a short hit-stop
            Vector3 hit = G(P - Dir * Radius * 0.6f) + Vector3.up * 0.3f;
            _ink = Vol2FsB1.Burst(Seed, 16, Side, Dir, 1.4f, 3.0f, 0.12f, 0.3f, 0.75f, 0.4f);
            for (int k = 0; k < 90; k++) Vol2FsB1.EmitV(_sparks, hit, (Random.onUnitSphere + Vector3.up * 0.6f + Dir * 0.5f).normalized * Random.Range(3f, 9f), -1f, -1f);
            for (int k = 0; k < 60; k++) Vol2FsB1.EmitV(_embers, hit + Random.insideUnitSphere * 0.3f, Random.onUnitSphere * Random.Range(1f, 4f) + Vector3.up, -1f, -1f);
            HitStop(0f, 0.07f);
        }

        float Burn(float t)
        {
            // the fire's strength: flares after ignition, then burns lower over the child's life (monotone), dims in its last second, out at the end
            float on = Vol2Fs.Smooth(0f, 0.25f, t);
            float settle = Mathf.Lerp(1f, 0.62f, Vol2Fs.Smooth(0.5f, 3.5f, t));
            float left = HasChild && ChildExpireRel > 0f ? ChildExpireRel - t : 10f;
            float warn = Mathf.Lerp(0.55f, 1f, Vol2Fs.Smooth(0f, 1.0f, left));
            float exit = 1f - Vol2FsRxTransferKit.Exit01(t, ChildEndAt, ChildWhy);
            if (!HasChild) exit = 1f - Vol2Fs.Smooth(0.25f, 0.55f, t);
            return on * settle * warn * exit;
        }

        protected override void OnChildEnd(Vol2MaterialEnd why, float t)
        {
            if (why == Vol2MaterialEnd.Expired) _ashed = true;      // burns down to ash (below)
            foreach (var ps in new[] { _licks, _soot }) if (ps != null) { var e = ps.emission; e.enabled = false; }
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            float burn = Burn(t);
            bool natural = ChildEndAt >= 0f && ChildWhy == Vol2MaterialEnd.Expired;
            float exitK = Vol2FsRxTransferKit.Exit01(t, ChildEndAt, ChildWhy);
            // the enemy's leaves flash off as embers when the front reaches them; the tangle chars (stays as the fire's shape), crumbles at the end
            if (Patch != null)
            {
                if (!_leavesOff && t > 0.18f)
                {
                    _leavesOff = true;
                    foreach (var p in Patch.Props) if (p != null && p.name == "Leaves") p.gameObject.SetActive(false);
                    for (int k = 0; k < 80; k++) Vol2FsB1.EmitV(_embers, P + Random.insideUnitSphere * Radius * 0.8f + Vector3.up * 0.3f, Vector3.up * Random.Range(0.5f, 2.5f) + Random.insideUnitSphere, -1f, -1f);
                }
                if (ChildEndAt >= 0f) { Patch.Pose(Mathf.Max(0.02f, 1f - exitK), exitK * 0.25f, 1f - exitK * 0.3f); }
                else if (!HasChild) Patch.Pose(Mathf.Max(0.02f, 1f - Vol2Fs.Smooth(0.2f, 0.5f, t)), 0f);
            }
            _tongues.Clear(); _body.Clear(); _core.Clear(); _burst.Clear();
            if (_ink != null && t < 1.2f) Vol2FsB1.DrawBurst(_burst, _ink, G(P - Dir * Radius * 0.6f) + Vector3.up * 0.3f, t, cam, 0.3f, 0.1f);
            var pts = new List<Vector3>(); var ch = new List<float>(); var chA = new List<float>(); var w = new List<float>(); var al = new List<float>(); var wc = new List<float>(); var ac = new List<float>();
            for (int i = 0; i < _arcs.Count; i++)
            {
                var arc = _arcs[i];
                float lit = Mathf.Clamp01((t - _arcStart[i]) * Speed / _arcLen[i]);
                if (lit <= 0.01f) continue;
                // the arc sampled from the end nearest the entry, up to the running front
                bool rev = (arc[0] - (-Dir * Radius * Scale)).sqrMagnitude > (arc[arc.Length - 1] - (-Dir * Radius * Scale)).sqrMagnitude;
                pts.Clear(); ch.Clear(); chA.Clear(); w.Clear(); al.Clear(); wc.Clear(); ac.Clear();
                int n = 14;
                for (int k = 0; k < n; k++)
                {
                    float u = k / (float)(n - 1) * lit;
                    Vector3 lp = Vol2FsTubes.CatmullRom(arc, rev ? 1f - u : u);
                    Vector3 wp = P + lp + Vector3.up * 0.05f;
                    pts.Add(wp);
                    float front = Mathf.Clamp01((lit - u) / 0.12f);
                    w.Add((0.09f + 0.03f * Mathf.Sin(t * 9f + k + i)) * front * Mathf.Max(0.2f, burn));
                    al.Add(Mathf.Clamp01(burn * 1.2f)); wc.Add(0.04f * front * burn); ch.Add(0.14f * Mathf.Clamp01(lit * 3f)); ac.Add(burn);
                    // black tongues licking UP from the vine line (every other sample)
                    if (k % 3 == 1 && burn > 0.02f)
                    {
                        float h = (0.3f + 0.35f * Vol2Fs.Hash(Seed + i, k)) * burn * (0.75f + 0.25f * Mathf.Sin(t * 11f + k * 1.7f + i));
                        Vol2FsB3.Plume(_tongues, wp, Vector3.up, Side, h, 0.17f * burn + 0.04f, 0.12f, t * 7f + k + i * 2.3f, Mathf.Clamp01(burn * 1.3f), Vol2Fs.Hash(Seed, i * 31 + k), cam, 7, 0.75f);
                    }
                }
                // r3: the char-black vine line itself under the orange core (the lattice reads as lines, not a mass)
                for (int k2 = 0; k2 < pts.Count; k2++) chA.Add(1f - exitK);
                _tongues.Strip(pts, ch, null, Color.white, chA, 0.17f + i * 0.09f, cam);
                _body.Strip(pts, w, null, Color.white, al, 0.31f + i * 0.07f, cam);
                _core.Strip(pts, wc, null, Color.white, ac, 0.63f + i * 0.05f, cam);
                // the running front: a bright knot of sparks / licks while it still runs
                if (lit < 1f && burn > 0.05f)
                {
                    Vector3 f = pts[pts.Count - 1];
                    Vol2FsB1.EmitV(_licks, f + Random.insideUnitSphere * 0.06f, Vector3.up * Random.Range(1f, 2.2f), -1f, -1f);
                    Vol2FsB1.EmitV(_sparks, f, (Random.onUnitSphere + Vector3.up).normalized * Random.Range(1.5f, 4f), -1f, -1f);
                }
                // licks / embers / soot along the burning part (thinning as it burns lower)
                for (int e = 0; e < 1; e++) if (burn > 0.05f && Random.value < 0.4f * burn)
                {
                    Vector3 q = pts[Random.Range(0, pts.Count)];
                    Vol2FsB1.EmitV(_licks, q + Random.insideUnitSphere * 0.08f, Vector3.up * Random.Range(0.6f, 1.6f), -1f, -1f);
                    if (Random.value < 0.5f) Vol2FsB1.EmitV(_embers, q, Random.insideUnitSphere * 0.8f + Vector3.up * 1.2f, -1f, -1f);
                    if (Random.value < 0.08f) Vol2FsB1.EmitV(_soot, q + Vector3.up * 0.4f, Vector3.up * 0.4f, -1f, -1f);
                }
                // Expired: the vine lines burn down to rising ash
                if (natural && exitK < 1f && Random.value < 0.8f) Vol2FsB1.EmitV(_ash, P + Vol2FsTubes.CatmullRom(arc, Random.value), Vector3.up * Random.Range(0.4f, 1.4f) + Random.insideUnitSphere * 0.3f, -1f, -1f);
            }
            _tongues.Write(_tongueMesh); _body.Write(_bodyMesh); _core.Write(_coreMesh); _burst.Write(_burstMesh);
            if (_light != null) _light.intensity = 10f * burn * (0.85f + 0.15f * Mathf.Sin(t * 23f)) + (t < 0.15f ? 14f * (1f - t / 0.15f) : 0f);
            if (_ashed && exitK >= 1f) _ashed = false;
        }
    }

    /// <summary>Batch 8 옮겨붙음 helpers: the contacted patch's own forms rebuilt from its material id (the same seed and formulas as Vol2FsPatch.Build, so the
    /// child stands exactly in the consumed patch's shape - 자식 꼴 = 접촉 재료의 꼴), the child's exit timing, the owner's ink.</summary>
    public static class Vol2FsRxTransferKit
    {
        static int SeedOf(long materialId) => (int)(materialId % 9973) + 17;

        /// <summary>The wood patch's seven vine arcs (control points, patch-local: add the patch centre).</summary>
        public static void WoodArcs(long materialId, float radius, List<Vector3[]> into)
        {
            int seed = SeedOf(materialId);
            for (int k = 0; k < 7; k++)
            {
                float a0 = Vol2Fs.Hash(seed, k) * Mathf.PI * 2f, a1 = a0 + 1.4f + 1.6f * Vol2Fs.Hash(seed, k + 20);
                float r0 = radius * (0.25f + 0.65f * Vol2Fs.Hash(seed, k + 40)), r1 = radius * (0.3f + 0.6f * Vol2Fs.Hash(seed, k + 60)), h = 0.38f + 0.45f * Vol2Fs.Hash(seed, k + 80);
                Vector3 A = new Vector3(Mathf.Cos(a0) * r0, -0.03f, Mathf.Sin(a0) * r0), B = new Vector3(Mathf.Cos(a1) * r1, -0.03f, Mathf.Sin(a1) * r1);
                Vector3 m = (A + B) * 0.5f + Vector3.up * h;
                into.Add(new[] { A, Vector3.Lerp(A, m, 0.55f) + Vector3.up * h * 0.25f, m, Vector3.Lerp(m, B, 0.45f) + Vector3.up * h * 0.25f, B });
            }
        }

        /// <summary>The metal patch's eight blades: patch-local foot, outward lean direction, height.</summary>
        public static void MetalBlades(long materialId, float radius, List<(Vector3 Foot, Vector3 Out, float H, float Lean)> into)
        {
            int seed = SeedOf(materialId);
            for (int k = 0; k < 8; k++)
            {
                float a = (k / 8f + 0.1f * Vol2Fs.Hash(seed, k)) * Mathf.PI * 2f, r = radius * (0.2f + 0.65f * Vol2Fs.Hash(seed, k + 10));
                Vector3 outw = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                into.Add((outw * r, outw, 0.7f + 0.55f * Vol2Fs.Hash(seed, k + 20), 0.25f + 0.3f * Vol2Fs.Hash(seed, k + 30)));
            }
        }

        /// <summary>The earth patch's six slabs: patch-local foot and outward direction.</summary>
        public static void EarthSlabs(long materialId, float radius, List<(Vector3 Foot, Vector3 Out)> into)
        {
            int seed = SeedOf(materialId);
            for (int k = 0; k < 6; k++)
            {
                float a = (k / 6f + 0.12f * Vol2Fs.Hash(seed, k)) * Mathf.PI * 2f, r = radius * (0.25f + 0.55f * Vol2Fs.Hash(seed, k + 10));
                Vector3 outw = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                into.Add((outw * r, outw));
            }
        }

        /// <summary>A natural end (Expired) plays out over 0.9 s; any other end (Consumed / ChildConsumed / ...) dries in place in 0.3 s.</summary>
        public static bool Natural(Vol2MaterialEnd why) => why == Vol2MaterialEnd.Expired;
        public static float Exit01(float t, float endAt, Vol2MaterialEnd why) => endAt < 0f ? 0f : Mathf.Clamp01((t - endAt) / (Natural(why) ? 0.9f : 0.3f));

        /// <summary>The child's grow-in (0..1) after the commit, its presence after its end, and the dim of its last second (monotone).</summary>
        public static float Presence(float t, float grow0, float grow1, float endAt, Vol2MaterialEnd why, bool hasChild)
        {
            if (!hasChild) return 0f;
            return Vol2Fs.Smooth(grow0, grow1, t) * (1f - Exit01(t, endAt, why));
        }
    }
}
