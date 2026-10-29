using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>목>수 옮겨붙음 (wood catches onto the enemy's water) - free showcase batch 8, concept wa_a (the dark water dries in long streaks and a dense bed of
    /// jade reeds of the caster's own springs up from them; sweeping black ink strokes arc round it). Facts only: at the commit the enemy's water patch and the
    /// wood shape are consumed and a wood CHILD (mine) stands in the patch's footprint for its live 10 s. The look: the dark pool shrinks and dries into cracked
    /// streaks radiating from where the wood came in; along those streaks a bed of tall reeds (thin jade stems, long blade leaves, glowing tips) shoots up in a
    /// wave, tallest in the middle, leaning out, long black ink strokes sweeping up round it, seed motes drifting; the reeds stand and slowly dim over the child's
    /// life (monotone) and in its last second. Expired = the reeds wither from their tips and fold down, loosening into rootlets reaching forward over the ground
    /// (CSV 목 끝); Consumed / ChildConsumed = they dry in place in 0.3 s.</summary>
    public sealed class Vol2FsRxWoodWater : Vol2FsReact
    {
        static readonly Color Jade = new Color(0.45f, 1f, 0.55f);
        readonly Vol2FsTubes _reeds = new Vol2FsTubes(), _roots = new Vol2FsTubes();
        readonly Vol2FsLeaves _leaves = new Vol2FsLeaves();
        readonly List<float> _reedAt = new List<float>(), _leafAt = new List<float>();
        readonly List<int> _leafReed = new List<int>();
        readonly List<(Vector3 A, Vector3 B)> _streaks = new List<(Vector3, Vector3)>();
        Mesh _reedMesh, _rootMesh, _leafMesh, _streakMesh, _inkMesh;
        readonly Vol2FsStrips _streak = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _blades = new Vol2FsStrips();
        List<Vol2FsB1.Stroke> _strokes;
        ParticleSystem _motes, _splash, _leafFall, _steam;
        Light _light;
        bool _rootsBuilt;

        protected override float Burst => 1.8f;

        protected override void OnBuild()
        {
            var root = Root.transform;
            bool dark = EnemyIgniter;
            // the dried streaks: from the entry side across the pool, fanning
            Vector3 entry = G(P - Dir * Radius * 0.9f);
            for (int k = 0; k < 7; k++)
            {
                float a = (k - 3) * 0.36f + (Vol2Fs.Hash(Seed, k) - 0.5f) * 0.2f;
                Vector3 d = Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * Dir;
                _streaks.Add((entry + d * 0.2f, G(entry + d * Radius * (2.6f + 0.8f * Vol2Fs.Hash(Seed, k + 10)))));
            }
            // the reeds: along the streaks, tallest in the middle of the pool, leaning outward a little, a wave from the entry
            // r2: a 1.7x wider bed of 2.2 - 3.4 m reeds with long blade leaves drawn as ribbons (r1: ~1 m, big pale leaf cards)
            int n = 40;
            for (int k = 0; k < n; k++)
            {
                var s = _streaks[k % _streaks.Count];
                float u = 0.15f + 0.8f * Vol2Fs.Hash(Seed, k + 30);
                Vector3 foot = Vector3.Lerp(s.A, s.B, u) + Side * (Vol2Fs.Hash(Seed, k + 40) - 0.5f) * 0.18f;
                Vector3 off = foot - G(P); float r01 = Mathf.Clamp01(off.magnitude / Mathf.Max(0.1f, Radius * 1.8f));
                Vector3 outw = off.sqrMagnitude > 1e-4f ? off.normalized : Dir;
                float h = Mathf.Lerp(3.4f, 1.6f, r01) * (0.75f + 0.4f * Vol2Fs.Hash(Seed, k + 50));
                Vector3 lean = outw * (0.15f + 0.45f * r01) * h + Side * (Vol2Fs.Hash(Seed, k + 60) - 0.5f) * 0.3f;
                Vector3 bend = Vector3.Cross(Vector3.up, outw) * (Vol2Fs.Hash(Seed, k + 70) - 0.5f) * 0.25f;
                var tb = _reeds.Add(new[] { foot + Vector3.down * 0.05f, foot + Vector3.up * h * 0.35f + lean * 0.15f, foot + Vector3.up * h * 0.7f + lean * 0.5f + bend, foot + Vector3.up * h + lean + bend * 1.6f }, 0.03f + 0.015f * Vol2Fs.Hash(Seed, k + 80), 0.005f, 18);
                tb.Sides = 5; tb.Sharp = 0.3f; tb.Grow = 0f; tb.NodeEvery = 0.32f;
                _reedAt.Add(0.03f + (foot - entry).magnitude / 10f + 0.04f * Vol2Fs.Hash(Seed, k + 90));
                // long blade leaves off the stem
                for (int j = 0; j < 2; j++)
                {
                    float lu = 0.3f + 0.35f * j + 0.1f * Vol2Fs.Hash(Seed, k * 3 + j);
                    Vector3 lp = Vol2FsTubes.CatmullRom(tb.Ctrl, lu);
                    Vector3 ln = Quaternion.AngleAxis(Vol2Fs.Hash(Seed, k * 7 + j) * 360f, Vector3.up) * Vector3.forward;
                    _leaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = lp, Normal = ln, Up = (Vector3.up * 1.2f + Vector3.Cross(ln, Vector3.up) * (j == 0 ? 0.6f : -0.6f)).normalized, Size = 0.26f + 0.12f * Vol2Fs.Hash(Seed, k + j + 300), Grow = 0f, Cell = (k + j) % 4 });
                    _leafAt.Add(lu); _leafReed.Add(k);
                }
            }
            var bark = dark ? Kit.Bark(new Color(0.32f, 0.36f, 0.3f), Color.black, 0.2f, "fs_bamboo", "fs_vein") : Kit.Bark(new Color(0.75f, 1f, 0.62f), Jade * 4f, 0.3f, "fs_bamboo", "fs_bamboo");
            Vol2Fs.MeshObject(root, "Reeds", bark, out _reedMesh);
            Vol2Fs.MeshObject(root, "Rootlets", bark, out _rootMesh);
            var blade = M("fs_streak", dark ? new Color(0.3f, 0.36f, 0.3f) : new Color(0.5f, 1f, 0.55f), dark ? 0.9f : 1.7f, 0.35f); blade.NoiseAmt = 0.25f; blade.Soft = 0.05f; blade.Erode = 0.12f;
            Vol2Fs.MeshObject(root, "ReedBlades", Kit.Mat(blade), out _leafMesh);
            var st = M("fs_crack", dark ? new Color(0.2f, 0.18f, 0.22f) : new Color(0.2f, 0.16f, 0.12f), 1f, 0f); st.NoiseAmt = 0.35f; st.Soft = 0.1f; st.Opacity = 0.9f;
            Vol2Fs.MeshObject(root, "DryStreaks", Kit.Mat(st), out _streakMesh);
            Vol2Fs.MeshObject(root, "InkSweeps", Kit.InkSolid("fs_stroke", InkC, Jade * 1.2f, 0.12f, 0.5f, 0.35f, 1.2f), out _inkMesh);
            _motes = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, dark ? 1.2f : 3.5f, 1f)), Life0 = 1.0f, Life1 = 2.0f, Speed0 = 0.1f, Speed1 = 0.5f, Size0 = 0.025f, Size1 = 0.06f, Noise = 0.8f, NoiseFreq = 0.8f, VelOverLife = new Vector3(0f, 0.3f, 0f), Max = 300 }, root, "SeedMotes");
            _splash = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Water(Kit, 1.3f, 0.35f, "fs_splash"), Life0 = 0.35f, Life1 = 0.7f, Speed0 = 1.5f, Speed1 = 4.5f, Size0 = 0.12f, Size1 = 0.3f, Gravity = 1.6f, Drag = 0.6f, Max = 300 }, root, "Splash");
            var leaf = M("fs_leaf", new Color(0.85f, 1f, 0.7f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leafFall = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 0.9f, Life1 = 1.6f, Speed0 = 0.5f, Speed1 = 2.5f, Size0 = 0.1f, Size1 = 0.22f, Gravity = 0.35f, Drag = 1.5f, Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 300 }, root, "LeafFall");
            var mist = M("fs_puff", new Color(0.7f, 0.8f, 0.85f), 1f, 0.2f); mist.Opacity = 0.3f; mist.NoiseAmt = 0.6f; mist.Erode = 0.35f;
            _steam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mist), Life0 = 0.7f, Life1 = 1.3f, Speed0 = 0.3f, Speed1 = 0.9f, Size0 = 0.4f, Size1 = 0.8f, SizeStart = 0.5f, SizePeak = 1.1f, SizeEnd = 1.5f, VelOverLife = new Vector3(0f, 0.4f, 0f), Max = 80 }, root, "DryingMist");
            foreach (var ps in new[] { _motes, _splash, _leafFall, _steam }) ps.Play(false);
            _light = Vol2Fs.PointLight(root, "JadeLight", Jade, 7f); _light.transform.position = P + Vector3.up * 1.6f;
            // the contact: the water bursts up as the wood drinks it, mist off the drying pool, sweeping ink strokes
            for (int k = 0; k < 50; k++) Vol2FsB1.EmitV(_splash, entry + Vector3.up * 0.2f + Random.insideUnitSphere * 0.3f, (Random.onUnitSphere + Vector3.up).normalized * Random.Range(1.5f, 5f), -1f, -1f);
            for (int k = 0; k < 10; k++) Vol2FsB1.EmitV(_steam, G(P) + Random.insideUnitSphere * Radius + Vector3.up * 0.2f, Vector3.up * 0.5f, -1f, -1f);
            _strokes = Vol2FsB1.Burst(Seed, 12, Side, Dir, 2.2f, 4.2f, 0.1f, 0.24f, 1.3f, 0.2f);
            HitStop(0f, 0.06f);
        }

        void BuildRoots()
        {
            _rootsBuilt = true;
            for (int k = 0; k < 9; k++)
            {
                float a = (k - 4) * 0.4f; Vector3 d = Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * -Dir;        // reaching forward (toward the caster's side, CSV 목 끝)
                Vector3 s = G(P) + d * Radius * 0.5f, e = G(P) + d * Radius * (2.6f + 0.8f * Vol2Fs.Hash(Seed, k + 500));
                Vector3 m = Vector3.Lerp(s, e, 0.5f) + Vector3.Cross(Vector3.up, d) * (Vol2Fs.Hash(Seed, k + 510) - 0.5f) * 0.5f;
                var tb = _roots.Add(new[] { s + Vector3.up * 0.02f, m + Vector3.up * 0.03f, e }, 0.03f, 0.004f, 10); tb.Sides = 5; tb.Sharp = 0.2f; tb.Knots = 0.3f; tb.Grow = 0f;
            }
        }

        protected override void OnChildEnd(Vol2MaterialEnd why, float t)
        {
            if (!Vol2FsRxTransferKit.Natural(why)) return;
            BuildRoots();
            for (int k = 0; k < 60; k++) Vol2FsB1.EmitV(_leafFall, G(P) + Random.insideUnitSphere * Radius + Vector3.up * Random.Range(0.4f, 1.5f), Random.insideUnitSphere + Vector3.up * 0.3f, -1f, -1f);
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            float exitK = Vol2FsRxTransferKit.Exit01(t, ChildEndAt, ChildWhy);
            bool natural = ChildEndAt >= 0f && Vol2FsRxTransferKit.Natural(ChildWhy);
            float left = HasChild && ChildExpireRel > 0f ? ChildExpireRel - t : 10f;
            float vigor = Mathf.Lerp(1f, 0.6f, Vol2Fs.Smooth(0.6f, 5f, t)) * Mathf.Lerp(0.5f, 1f, Vol2Fs.Smooth(0f, 1f, left));
            // the enemy's pool dries: it shrinks, its ripples stop
            if (Patch != null)
            {
                float k = Vol2Fs.Smooth(0.03f, 0.6f, t);
                Patch.StopParticles();
                Patch.Pose(1f, k * 0.03f, Mathf.Max(0.05f, 1f - 0.85f * k));
                if (t > 1.0f && Patch.Go != null && Patch.Go.activeSelf) Patch.SetActive(false);
            }
            for (int i = 0; i < _reeds.Tubes.Count; i++)
            {
                var tb = _reeds.Tubes[i];
                if (!HasChild) { tb.Grow = 0f; continue; }
                float a = (t - _reedAt[i]) / 0.28f;
                float g = Mathf.Clamp01(a); g = 1f - (1f - g) * (1f - g);
                if (ChildEndAt >= 0f) g *= natural ? 1f - Vol2Fs.Smooth(0f, 0.85f, exitK) : 1f - exitK;
                if (a > 0f && a < 0.15f && Random.value < 0.4f) Vol2FsB1.EmitV(_splash, tb.Ctrl[0] + Vector3.up * 0.1f, Vector3.up * Random.Range(1f, 2.5f) + Random.insideUnitSphere * 0.5f, -1f, -1f);
                tb.Grow = g;
            }
            _reeds.Write(_reedMesh);
            // the long blade leaves: ribbons from the stem, arching out and drooping (grow with their reed)
            _blades.Clear();
            for (int i = 0; i < _leaves.Items.Count; i++)
            {
                var l = _leaves.Items[i];
                float g = Mathf.Clamp01((_reeds.Tubes[_leafReed[i]].Grow - _leafAt[i]) / 0.25f);
                if (g <= 0.01f) continue;
                Vector3 outw = Vector3.Cross(l.Normal, Vector3.up); outw.y = 0f; outw = outw.sqrMagnitude > 1e-4f ? outw.normalized : Side;
                float len = l.Size * 3.2f * g;
                int m = 7; var pts = new Vector3[m]; var wd = new float[m]; var al = new float[m];
                for (int k = 0; k < m; k++)
                {
                    float u = k / (float)(m - 1);
                    pts[k] = l.Pos + outw * len * u + Vector3.up * len * (0.7f * u - 0.75f * u * u);
                    wd[k] = 0.09f * Mathf.Sin(Mathf.Clamp01(u * 0.9f + 0.08f) * Mathf.PI); al[k] = 0.95f;
                }
                _blades.Strip(pts, wd, null, Color.white, al, 0.13f * i, cam);
            }
            _blades.Write(_leafMesh);
            if (_rootsBuilt) { float rg = Vol2Fs.Smooth(0f, 0.7f, exitK); foreach (var r in _roots.Tubes) r.Grow = rg; _roots.Write(_rootMesh); }
            if (HasChild && ChildEndAt < 0f && Random.value < 0.25f * vigor) Vol2FsB1.EmitV(_motes, G(P) + Random.insideUnitSphere * Radius * 1.7f + Vector3.up * Random.Range(0.8f, 3f), Random.insideUnitSphere * 0.3f, -1f, -1f);
            _streak.Clear(); _ink.Clear();
            float stLife = HasChild ? Vol2Fs.Smooth(0.02f, 0.3f, t) * (1f - exitK) * 0.95f : 1f - Vol2Fs.Smooth(0.3f, 0.6f, t);
            int si = 0;
            foreach (var s in _streaks) { Vol2FsB6.Crack(_streak, s.A, s.B, Ground, 0.2f, 0f, Mathf.Clamp01(t / 0.25f), stLife, 0.2f * si, Seed + si * 11, 0.1f, 0.03f); si++; }
            if (_strokes != null && t < 1.4f) Vol2FsB1.DrawBurst(_ink, _strokes, G(P) + Vector3.up * 0.2f, t, cam, 0.15f, 0.12f);
            _streak.Write(_streakMesh); _ink.Write(_inkMesh);
            if (_light != null) _light.intensity = (HasChild ? 3.5f * vigor * (1f - exitK) * Vol2Fs.Smooth(0.05f, 0.4f, t) : 0f) + (t < 0.12f ? 6f * (1f - t / 0.12f) : 0f);
        }
    }
}
