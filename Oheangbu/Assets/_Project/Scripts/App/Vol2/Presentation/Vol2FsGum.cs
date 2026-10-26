using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>굼 (wood, ㅜ ring, 심는다) - free showcase batch 5, concept gum_a (a circle of dormant jade buds and curled sprouts lies on the 2 m band; where the
    /// bear stepped over it, vines lash up out of the ring in great looping arcs that race round the circle and coil round both bears on the band, leaves and
    /// ink flying). Unlike 구's crown of thorns standing up at the cast, nothing stands while it waits: only the buds breathe. Executor facts only: the launch =
    /// the buds sprout out of the band (a puff of leaves) -> the wait (the buds' jade pulse quickens to the live expiry, the ink arc inside the band burns
    /// down; the last 0.4 s the sprouts twitch) -> Consumed = from the crossing body's angle the vines whip up in loops round the ring (RaceDelay), an ink
    /// splash, leaves -> every struck body is coiled for the bind's 2 s (the status sink owns the slow; mirrored) -> the loops sink, the coils loosen into
    /// rootlets reaching outward along the ground (CSV 끝: 잔뿌리로 풀림(앞으로 뻗으며)); Expired = the buds wilt brown and a few short rootlets creep out
    /// and sink.</summary>
    public sealed class Vol2FsGum : Vol2FsSeedRing
    {
        static readonly Color Jade = new Color(0.4f, 1f, 0.45f);
        const float HoldSeconds = 2f, LoopHold = 0.9f, UnravelSeconds = 0.9f;
        ParticleSystem _leaves, _sparks, _drops, _glow;
        Material _vine, _dry, _leafMat;
        Mesh _budMesh, _loopMesh, _coilMesh, _rootMesh, _inkMesh, _leafMesh;
        MeshRenderer _budR;
        readonly Vol2FsTubes _buds = new Vol2FsTubes(), _loops = new Vol2FsTubes(), _coils = new Vol2FsTubes(), _roots = new Vol2FsTubes();
        readonly Vol2FsStrips _ink = new Vol2FsStrips();
        readonly Vol2FsLeaves _budLeaves = new Vol2FsLeaves();
        readonly List<float> _loopAng = new List<float>();
        readonly List<(Vol2FsTubes.Tube T, float At)> _coilAt = new List<(Vol2FsTubes.Tube, float)>();
        Light[] _lights;
        bool _rootsBuilt;
        protected override float FireSeconds => HoldSeconds + UnravelSeconds + 0.5f;
        protected override float ExpireSeconds => 1.3f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _vine = Kit.Bark(new Color(0.8f, 1f, 0.6f), new Color(0.35f, 1.3f, 0.3f) * 4f, 0.3f, "fs_vine", "fs_vein");      // r3: brighter jade veins (r2: the vines read black)
            _dry = Kit.Bark(new Color(0.55f, 0.42f, 0.28f), Color.black, 0.2f, "fs_vine", "fs_vein");
            _leafMat = Kit.Leaf(new Color(0.55f, 1f, 0.5f), Jade * 0.6f);
            var leaf = M("fs_leaf", new Color(0.85f, 1f, 0.7f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leaves = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 1.5f, Speed1 = 5f, Size0 = 0.12f, Size1 = 0.26f, Radius = 0.25f, Gravity = 0.35f, Drag = 1.5f, Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 700 }, root, "Leaves");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 4.5f, 1f)), Life0 = 0.4f, Life1 = 1f, Speed0 = 0.6f, Speed1 = 4f, Size0 = 0.03f, Size1 = 0.07f, Radius = 0.2f, Drag = 1.4f, Stretch = true, StretchVel = 0.04f, Noise = 1.2f, NoiseFreq = 2f, Max = 600 }, root, "Sparks");
            _glow = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 2.6f, 1f)), Life0 = 0.1f, Life1 = 0.16f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.2f, Size1 = 0.3f, Max = 200 }, root, "BudGlow");
            var drop = M("fs_ink", InkC, 1f, 0f); drop.Edge = new Color(0.5f, 1.4f, 0.35f, 1f); drop.EdgeWidth = 0.16f; drop.NoiseAmt = 0.3f; drop.Soft = 0.1f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.08f, Size1 = 0.26f, Radius = 0.2f, Gravity = 2f, Drag = 0.4f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 400 }, root, "Drops");
            foreach (var ps in new[] { _leaves, _sparks, _drops, _glow }) ps.Play(false);
            _budR = Vol2Fs.MeshObject(root, "Buds", _vine, out _budMesh);
            Vol2Fs.MeshObject(root, "BudLeaves", _leafMat, out _leafMesh);
            Vol2Fs.MeshObject(root, "Loops", _vine, out _loopMesh);
            Vol2Fs.MeshObject(root, "Coils", _vine, out _coilMesh);
            Vol2Fs.MeshObject(root, "Rootlets", _vine, out _rootMesh);
            Vol2Fs.MeshObject(root, "InkLoops", Kit.InkSolid("fs_stroke2", InkC, new Color(0.45f, 1.5f, 0.35f, 1f), 0.12f, 0.45f, 0.3f, 1.3f), out _inkMesh);
            _lights = new Light[3];
            for (int k = 0; k < 3; k++) _lights[k] = Vol2Fs.PointLight(root, "Jade" + k, Jade, 3.5f);
        }

        protected override void Planted(Pod p)
        {
            // the buds: a seed knob + two curled sprouts each, and two leaf cards (one mesh for the whole ring)
            int seed = Seed + 41;
            for (int k = 0; k < SeedAng.Count; k++)
            {
                float a = SeedAng[k], sc = SeedScale(a);
                Vector3 b = OnBand(a, R + (Vol2Fs.Hash(seed, k) - 0.5f) * 0.25f), o = Dir(a), tg = Tangent(a);
                _buds.Add(new[] { b, b + Vector3.up * 0.07f * sc, b + Vector3.up * 0.13f * sc }, 0.07f * sc, 0.05f * sc, 4).Sides = 6;
                for (int s = 0; s < 2; s++)
                {
                    float sd = s == 0 ? 1f : -1f; var pts = new Vector3[6];
                    for (int i = 0; i < 6; i++) { float u = i / 5f, ang = u * 3.6f; pts[i] = b + Vector3.up * (0.08f + 0.22f * u) * sc + (tg * sd * Mathf.Sin(ang) + o * (1f - Mathf.Cos(ang)) * 0.4f) * 0.14f * sc * (0.4f + u); }
                    var tb = _buds.Add(pts, 0.02f * sc, 0.006f, 12); tb.Sides = 5;
                }
                _budLeaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = b + Vector3.up * 0.06f, Normal = o + Vector3.up * 0.4f, Up = Vector3.up + tg * 0.6f, Size = 0.16f * sc, Grow = 0f, Cell = k % 4 });
            }
            Vol2FsB1.Emit(_leaves, OnBand(0f, R), 8);
            for (int k = 0; k < 10; k++) Vol2FsB1.Emit(_leaves, OnBand(Random.Range(-2.6f, 2.6f), R), 2);
        }

        protected override void Fired(Pod p)
        {
            int seed = Seed + 77, n = 15;
            for (int k = 0; k < n; k++)
            {
                float a = Vol2FsB2.Angle(k, n, seed, 0.35f);
                float hgt = Vol2FsPolish.RingHeight(a) * (1.4f + 1.1f * Vol2Fs.Hash(seed, k)), sd = Vol2Fs.Hash(seed, k + 9) < 0.5f ? 1f : -1f;
                if (Front(a) < 0.2f) hgt *= 0.4f;
                Vector3 b = OnBand(a, R), o = Dir(a), tg = Tangent(a) * sd;
                var ctrl = new[] { b, b + Vector3.up * hgt * 0.55f + o * 0.15f, b + Vector3.up * hgt + o * 0.6f + tg * 0.35f, b + Vector3.up * hgt * 0.95f + o * 1.25f + tg * 0.75f, b + Vector3.up * hgt * 0.55f + o * 1.55f + tg * 0.6f, b + Vector3.up * hgt * 0.35f + o * 1.2f + tg * 0.2f };
                var tb = _loops.Add(ctrl, 0.09f, 0.025f, 36); tb.Knots = 0.3f; tb.Sides = 6;
                _loopAng.Add(a);
            }
            for (int k = 0; k < SeedAng.Count; k++) if (Front(SeedAng[k]) > 0.3f) Vol2FsB1.Emit(_leaves, OnBand(SeedAng[k], R) + Vector3.up * 0.2f, 4);
        }

        protected override void Hit(Pod p, Struck s)
        {
            if (s.World) return;
            int seed = Seed + (int)(s.Id % 97);
            var b = s.B; float at = SinceFire(p, Now(NowUs)) + RaceDelay(AngleOf(FeetOf(s)));
            for (int k = 0; k < 5; k++)
            {
                var ctrl = Vol2FsB4F.Coil(b, Ground, 9, k * 1.26f + Vol2Fs.Hash(seed, k), 1.5f + 0.5f * Vol2Fs.Hash(seed, k + 9), 0.95f, 0.1f);
                var tb = _coils.Add(ctrl, 0.08f, 0.03f, 40); tb.Knots = 0.3f; tb.Sides = 6;
                _coilAt.Add((tb, at + 0.05f * k));
            }
            Vol2FsB1.Emit(_leaves, b.center, 60); Vol2FsB1.Emit(_drops, b.center, 30);
        }

        void BuildRoots(int n, float len, bool all)
        {
            _rootsBuilt = true; int seed = Seed + 133;
            for (int k = 0; k < n; k++)
            {
                float a = Vol2FsB2.Angle(k, n, seed, 0.4f);
                if (Front(a) < 0.3f) continue;
                Vector3 o = Dir(a), b = OnBand(a, R), sideV = Tangent(a);
                float L0 = len * (0.6f + 0.6f * Vol2Fs.Hash(seed, k + 3));
                var pts = new Vector3[6];
                for (int i = 0; i < 6; i++) { float u = i / 5f; pts[i] = b + o * L0 * u + sideV * Mathf.Sin(u * 5f + k) * 0.12f + Vector3.up * (0.04f + 0.06f * (1f - u)); }
                var tb = _roots.Add(pts, all ? 0.05f : 0.035f, 0.008f, 18); tb.Sides = 5; tb.Knots = 0.4f;
            }
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos; var p = The;
            _ink.Clear();
            if (p == null || !p.Started) { _buds.Write(_budMesh); return; }
            float life = PodLife(p, t, 0.35f), beat = Beat(p), warn = PodWarn(p, t);
            bool worked = Worked(p), withered = Withered(p);
            float ex = PodExit(p, t), s = SinceFire(p, t);
            // the buds: grow at the launch; spent where the eruption passes; wilt at the expiry
            float budGrow = Mathf.Clamp01(PodAge(p, t) / 0.35f);
            foreach (var tb in _buds.Tubes) tb.Grow = budGrow * (withered ? 1f - 0.6f * ex : 1f);
            if (worked)
            {
                for (int k = 0; k < _buds.Tubes.Count; k++) { int seedK = k / 3; float a = SeedAng[Mathf.Min(seedK, SeedAng.Count - 1)]; _buds.Tubes[k].Grow = s >= RaceDelay(a) ? 0f : 1f; }
            }
            _buds.Write(_budMesh);
            if (withered && _budR.sharedMaterial != _dry) _budR.sharedMaterial = _dry;
            _budR.gameObject.SetActive(!(p.Ended && !withered) || worked);
            for (int i = 0; i < _budLeaves.Items.Count; i++)
            {
                var lf = _budLeaves.Items[i]; float a = SeedAng[i];
                lf.Grow = budGrow * (worked ? (s >= RaceDelay(a) ? 0f : 1f) : withered ? 1f - ex : (p.Ended ? 0f : 1f + 0.15f * beat));
                _budLeaves.Items[i] = lf;
            }
            _budLeaves.Write(_leafMesh);
            if (!p.Ended)
            {
                // the jade heartbeat on each bud (the warning: brighter, twitching)
                if (Time.frameCount % 2 == 0)
                    foreach (float a in SeedAng) Vol2FsB1.EmitV(_glow, OnBand(a, R) + Vector3.up * 0.1f + (warn > 0f ? Random.insideUnitSphere * 0.03f : Vector3.zero), Vector3.zero, (0.12f + 0.2f * beat + 0.12f * warn) * SeedScale(a) * life * (1f - 0.5f * Held01), 0.12f);
                for (int k = 0; k < 3; k++) { _lights[k].transform.position = OnBand(-1.2f + 1.2f * k, R) + Vector3.up * 0.4f; _lights[k].intensity = life * (0.3f + 1.1f * beat + warn) * (1f - 0.6f * Held01); }
                if (warn > 0f && Time.frameCount % 3 == 0) Vol2FsB1.Emit(_sparks, OnBand(Random.Range(-2.4f, 2.4f), R), 2);
            }
            if (worked)
            {
                // the loops racing round from the crossing, holding, then sinking
                for (int k = 0; k < _loops.Tubes.Count; k++)
                {
                    float d = RaceDelay(_loopAng[k]), a = s - d;
                    float g = Vol2Fs.Smooth(0f, 0.22f, a) * (1f - Vol2Fs.Smooth(LoopHold, LoopHold + 0.5f, a));
                    _loops.Tubes[k].Grow = g;
                    if (a >= 0f && a - Time.deltaTime < 0f) { Vector3 b = OnBand(_loopAng[k], R); Vol2FsB1.Emit(_leaves, b + Vector3.up * 0.4f, 18); Vol2FsB1.Emit(_sparks, b, 24); Vol2FsB1.Emit(_drops, b + Vector3.up * 0.3f, 8); }
                    // an ink loop riding beside the vine, flung wider and fading
                    if (g > 0.05f)
                    {
                        var tb = _loops.Tubes[k]; var pts = new Vector3[8]; var w = new float[8]; var al = new float[8];
                        float inkLife = (1f - Vol2Fs.Smooth(0.25f, 0.75f, a)) * Vol2Fs.Smooth(0f, 0.1f, a);
                        for (int i = 0; i < 8; i++) { float u = i / 7f * g; pts[i] = Vol2FsTubes.CatmullRom(tb.Ctrl, u) + Dir(_loopAng[k]) * 0.25f * u; w[i] = 0.22f * (1f - 0.7f * u); al[i] = inkLife * (1f - 0.3f * u); }
                        if (inkLife > 0.01f) _ink.Strip(pts, w, null, Color.white, al, 0.13f + k * 0.07f, cam);
                    }
                }
                _loops.Write(_loopMesh);
                float loose = Vol2Fs.Smooth(HoldSeconds, HoldSeconds + UnravelSeconds, s);
                foreach (var c in _coilAt) c.T.Grow = Vol2Fs.Smooth(c.At, c.At + 0.25f, s) * (1f - loose);
                _coils.Write(_coilMesh);
                if (s > HoldSeconds && !_rootsBuilt) BuildRoots(14, 1.3f, true);
                float lt = 2.2f * (1f - Vol2Fs.Smooth(0f, 0.8f, s)) + (s < HoldSeconds ? 0.5f : 0f);
                for (int k = 0; k < 3; k++) _lights[k].intensity = lt;
                if (s < HoldSeconds && Time.frameCount % 4 == 0) foreach (var c in _coilAt) { Vol2FsB1.Emit(_sparks, Vol2FsTubes.TipOf(c.T), 1); break; }
            }
            if (withered && ex > 0.3f && !_rootsBuilt) BuildRoots(7, 0.7f, false);
            if (_rootsBuilt)
            {
                float r = worked ? Vol2Fs.Smooth(HoldSeconds, HoldSeconds + 0.6f, s) * (1f - Vol2Fs.Smooth(HoldSeconds + UnravelSeconds, HoldSeconds + UnravelSeconds + 0.5f, s))
                                 : Vol2Fs.Smooth(0.3f, 0.75f, ex) * (1f - Vol2Fs.Smooth(0.85f, 1f, ex));
                foreach (var tb in _roots.Tubes) tb.Grow = r;
                _roots.Write(_rootMesh);
            }
            if (p.Ended && !worked) for (int k = 0; k < 3; k++) _lights[k].intensity = 0f;
            _ink.Write(_inkMesh);
        }
    }
}
