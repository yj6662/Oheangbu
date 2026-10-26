using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>낙 (fire, ㅏ path, 남긴다) - free showcase batch 3, concept nak_a (a burning TREE of fire stands where the bolt hit: a twisted trunk of glowing black
    /// ink-coal splitting into branches of flame 2 - 3 m up, embers drifting, an orange pool of light on the red ground). Beats, all executor facts: charge -> a
    /// burning ink-coal bolt flies to the predicted first body -> the hit (flame burst, fire-rimmed ink strokes, embers) -> the residue material appears at the
    /// landing = the coal tree grows up out of the ground and burns there (flames off every branch, embers, smoke, flickering light) for the material's life; a body
    /// drawing from it = a flare -> the last 0.4 s before its expiry = the flames gutter -> natural expiry (CSV: 끝부터 타 재로 날림) = the branches burn back from
    /// their tips into rising ash; consumed / cleansed / replaced = their own shorter exits.</summary>
    public sealed class Vol2FsNak : Vol2FsLeave
    {
        static readonly Color Orange = new Color(1f, 0.45f, 0.1f), Hot = new Color(1f, 0.8f, 0.4f), Ink = new Color(0.035f, 0.025f, 0.02f);
        ParticleSystem _charge, _trail, _fire, _tall, _embers, _smoke, _ash, _drops, _steam;
        Light _chargeLight, _boltLight, _treeLight;
        Mesh _streakMesh, _burstMesh, _scorchMesh, _treeMesh; readonly Vol2FsStrips _streak = new Vol2FsStrips(), _burstS = new Vol2FsStrips(), _scorch = new Vol2FsStrips();
        readonly Vol2FsTubes _tree = new Vol2FsTubes(); MeshRenderer _treeR;
        List<Vol2FsB1.Stroke> _burst;
        readonly List<float> _branchAt = new List<float>();
        Vector3 _bolt, _base; bool _treeBuilt, _exitFx;
        protected override float LobHeight => 0.35f;
        protected override float ExpireSeconds => 1.5f;

        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.8f, 0.5f)), Rate = 70f, Loop = true, Life0 = 0.18f, Life1 = 0.3f, Speed0 = -1.6f, Speed1 = -2.6f, Size0 = 0.12f, Size1 = 0.28f, Radius = 0.6f, World = false, Tiles = 2, Max = 90 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", Orange, 4f);
            _charge.transform.position = Tip(); _charge.Play(false);
            _trail = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(2f, 0.5f)), Rate = 0f, RatePerMeter = 22f, Loop = true, Life0 = 0.25f, Life1 = 0.5f, Speed0 = 0.2f, Speed1 = 0.8f, Size0 = 0.3f, Size1 = 0.6f, Radius = 0.12f, Tiles = 2,
                VelOverLife = new Vector3(0f, 1.2f, 0f), SizeStart = 1f, SizeEnd = 0.3f, Max = 200 }, root, "Trail");
            _boltLight = Vol2Fs.PointLight(_trail.transform, "BoltLight", Orange, 5f);
            _fire = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.6f, 0.35f)), Life0 = 0.35f, Life1 = 0.7f, Speed0 = 0.2f, Speed1 = 0.9f, Size0 = 0.3f, Size1 = 0.7f, Radius = 0.12f, Tiles = 2,
                VelOverLife = new Vector3(0f, 2.2f, 0f), Noise = 0.9f, NoiseFreq = 1.3f, SizeStart = 0.5f, SizePeak = 1.2f, SizeEnd = 0.35f, Max = 4000 }, root, "Fire");
            _tall = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.7f, 0.35f)), Life0 = 0.5f, Life1 = 0.9f, Speed0 = 1f, Speed1 = 3f, Size0 = 0.9f, Size1 = 1.6f, Radius = 0.25f, Tiles = 2,
                VelOverLife = new Vector3(0f, 3f, 0f), Noise = 1f, NoiseFreq = 0.9f, SizeStart = 0.4f, SizePeak = 1.2f, SizePeakAt = 0.35f, SizeEnd = 0.5f, Max = 400 }, root, "TallFire");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Hot, 5f, 1f)), Life0 = 0.8f, Life1 = 2f, Speed0 = 0.6f, Speed1 = 3f, Size0 = 0.025f, Size1 = 0.06f, Radius = 0.2f, Gravity = -0.25f,
                Noise = 1.4f, NoiseFreq = 1.6f, Stretch = true, StretchVel = 0.05f, Max = 1500 }, root, "Embers");
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Smoke(0.5f)), Life0 = 1.4f, Life1 = 2.4f, Speed0 = 0.3f, Speed1 = 0.9f, Size0 = 0.9f, Size1 = 1.6f, Radius = 0.3f, Drag = 0.8f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.3f, SizePeakAt = 0.5f, SizeEnd = 1.8f, VelOverLife = new Vector3(0f, 1.1f, 0f), Max = 260, SortFudge = 12 }, root, "Smoke");
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Ash()), Life0 = 1.2f, Life1 = 2.4f, Speed0 = 0.2f, Speed1 = 1f, Size0 = 0.04f, Size1 = 0.12f, Radius = 0.25f, VelOverLife = new Vector3(0f, 1.6f, 0f),
                Noise = 1.2f, NoiseFreq = 1f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.6f, Max = 1200 }, root, "Ash");
            var drop = M("fs_ink", Ink, 1f, 0f); drop.Edge = new Color(1.8f, 0.6f, 0.1f, 1f); drop.EdgeWidth = 0.18f; drop.Soft = 0.1f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.5f, Life1 = 1.1f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.08f, Size1 = 0.28f, Radius = 0.25f, Gravity = 1.8f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 300 }, root, "InkDrops");
            var steam = M("fs_puff", new Color(0.92f, 0.92f, 0.9f, 1f), 1f, 0f); steam.Opacity = 0.5f; steam.NoiseAmt = 0.6f; steam.Erode = 0.4f;
            _steam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(steam), Life0 = 0.8f, Life1 = 1.4f, Speed0 = 0.5f, Speed1 = 1.5f, Size0 = 0.8f, Size1 = 1.4f, Radius = 0.5f, Tiles = 2, VelOverLife = new Vector3(0f, 1.6f, 0f), SizeEnd = 1.8f, Max = 80 }, root, "Steam");
            foreach (var ps in new[] { _fire, _tall, _embers, _smoke, _ash, _drops, _steam }) ps.Play(false);
            var streak = M("fs_streak", Color.white, 3f, 0.9f); streak.Ramp = "fs_ramp_fire"; streak.RampScale = 1.3f; streak.NoiseAmt = 0.4f;
            Vol2Fs.MeshObject(root, "Streak", Kit.Mat(streak), out _streakMesh);
            Vol2Fs.MeshObject(root, "InkBurst", Kit.InkSolid("fs_stroke2", Ink, new Color(2.4f, 0.8f, 0.12f, 1f), 0.12f, 0.5f, 0.28f, 1.6f), out _burstMesh);
            var sc = M("fs_scorch", new Color(0.05f, 0.03f, 0.02f, 1f), 1f, 0f); sc.Soft = 0.001f; sc.NoiseAmt = 0.4f; sc.Erode = 0.3f; sc.Opacity = 0.9f; sc.Edge = new Color(2.2f, 0.6f, 0.08f, 1f); sc.EdgeWidth = 0.14f;
            Vol2Fs.MeshObject(root, "Scorch", Kit.Mat(sc), out _scorchMesh);
            // the tree: ink-coal bark with glowing cracks (URP Lit, emissive vein mask)
            _treeR = Vol2Fs.MeshObject(root, "FireTree", Kit.Bark(new Color(0.24f, 0.17f, 0.13f), new Color(4.5f, 1.5f, 0.25f), 0.1f, "fs_bark", "fs_bark_glow"), out _treeMesh);
            _treeR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            _treeLight = Vol2Fs.PointLight(root, "TreeLight", Orange, 7f);
        }

        protected override void OnCharge(float t, float k)
        {
            _charge.transform.position = Tip();
            _chargeLight.intensity = 4f * k;
        }

        protected override void OnLaunch(long nowUs)
        {
            _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f;
            _bolt = From; _trail.transform.position = From; _trail.Play(false); _boltLight.intensity = 5f;
        }

        protected override void OnFly(float t, float f, float prev)
        {
            _bolt = Line(f);
            _trail.transform.position = _bolt;
            Vol2FsB1.Emit(_fire, _bolt, 3); Vol2FsB1.Emit(_embers, _bolt, 2);
            if (Time.frameCount % 2 == 0) Vol2FsB1.Emit(_drops, _bolt, 1);
            DrawStreak(f, 1f);
        }

        void DrawStreak(float f, float life)
        {
            _streak.Clear();
            if (life > 0.01f && f > 0.02f)
            {
                var pts = new Vector3[10]; var w = new float[10]; var al = new float[10];
                for (int k = 0; k < 10; k++) { float u = k / 9f * f; pts[k] = Line(u); w[k] = 0.32f * (0.3f + 0.7f * u / Mathf.Max(0.01f, f)); al[k] = life * (0.25f + 0.75f * u / Mathf.Max(0.01f, f)); }
                _streak.Strip(pts, w, null, Color.white, al, 0.33f, CamPos, 0f, 1f);
            }
            _streak.Write(_streakMesh);
        }

        protected override void OnHit(long nowUs)
        {
            _trail.Stop(true, ParticleSystemStopBehavior.StopEmitting); _boltLight.intensity = 0f;
            Vector3 at = HasTarget ? HitPoint : AimPoint;
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(at - Inst.Origin));
            _burst = Vol2FsB1.Burst(Seed + 11, 11, side, FlatN(at - Inst.Origin), 1.1f, 2.4f, 0.2f, 0.42f, 0.7f, 0.2f);
            Vol2FsB1.Emit(_fire, at, 70); Vol2FsB1.Emit(_tall, at, 8); Vol2FsB1.Emit(_embers, at, 220); Vol2FsB1.Emit(_drops, at, 60); Vol2FsB1.Emit(_smoke, at + Vector3.up * 0.5f, 6);
            _treeLight.transform.position = at; _treeLight.intensity = 14f;
        }

        protected override void OnStruck(Struck s) { Vol2FsB1.Emit(_fire, CentreNow(s), 30); }

        protected override void OnResidue() { BuildTree(); }

        void BuildTree()
        {
            if (_treeBuilt) return;
            _treeBuilt = true;
            _base = ResCentre; _base.y = Ground;
            int seed = Seed + 77;
            Vector3 away = FlatN(_base - Inst.Origin), side = Vector3.Cross(Vector3.up, away);
            // the trunk: twisting up 1.4 m, then six main branches with forks - a gnarled coal tree, 2.4 - 3 m tall
            Vector3 b0 = _base + Vector3.down * 0.15f, b1 = _base + Vector3.up * 0.8f + side * 0.22f, b2 = _base + Vector3.up * 1.55f - side * 0.18f + away * 0.12f, b3 = _base + Vector3.up * 2.0f + side * 0.08f;
            var trunk = _tree.Add(new[] { b0, b1, b2, b3 }, 0.46f, 0.2f, 22); trunk.Knots = 0.6f; trunk.Sharp = 0.3f; trunk.Sides = 9;
            _branchAt.Add(0f);
            for (int k = 0; k < 7; k++)
            {
                float a = (k / 7f + 0.08f * Vol2Fs.Hash(seed, k)) * Mathf.PI * 2f;
                Vector3 o = Mathf.Cos(a) * side + Mathf.Sin(a) * away;
                float h0 = Mathf.Lerp(1.1f, 2.0f, Vol2Fs.Hash(seed, k + 10)), len = Mathf.Lerp(1.3f, 2.1f, Vol2Fs.Hash(seed, k + 20));
                Vector3 s0 = Vector3.Lerp(b1, b3, Mathf.InverseLerp(0.8f, 2.0f, h0)), s1 = s0 + o * len * 0.45f + Vector3.up * len * 0.45f, s2 = s1 + o * len * 0.4f + Vector3.up * len * 0.55f + side * 0.2f * (Vol2Fs.Hash(seed, k + 30) - 0.5f);
                Vector3 s3 = s2 + o * len * 0.15f + Vector3.up * len * 0.35f;
                var br = _tree.Add(new[] { s0, s1, s2, s3 }, Mathf.Lerp(0.15f, 0.21f, Vol2Fs.Hash(seed, k + 40)), 0.02f, 16); br.Knots = 0.5f; br.Sharp = 0.35f; br.Sides = 6;
                _branchAt.Add(0.08f + 0.05f * k);
                // a fork off each branch
                Vector3 f0 = Vector3.Lerp(s1, s2, 0.5f), f1 = f0 + (o * 0.3f - away * 0.25f + side * (k % 2 == 0 ? 0.3f : -0.3f)) + Vector3.up * 0.55f;
                var fk = _tree.Add(new[] { f0, f1, f1 + Vector3.up * 0.3f }, 0.08f, 0.012f, 8); fk.Sharp = 0.25f; fk.Sides = 5;
                _branchAt.Add(0.18f + 0.05f * k);
            }
            _treeLight.transform.position = _base + Vector3.up * 1.5f;
            Vol2FsB1.Emit(_embers, _base + Vector3.up * 0.4f, 120); Vol2FsB1.Emit(_fire, _base + Vector3.up * 0.3f, 40);
        }

        protected override void OnTouch(Struck s)
        {
            // a body drew from the burning tree: a flare licks over it
            Vol2FsB1.Emit(_tall, _base + Vector3.up * 1f, 6); Vol2FsB1.Emit(_embers, CentreNow(s), 90); Vol2FsB1.Emit(_fire, CentreNow(s), 30);
        }

        protected override void OnChanged(string what)
        {
            if (what == "wet") Vol2FsB1.Emit(_steam, _base + Vector3.up * 1f, 10);
            if (what == "extended" || what == "thickened") Vol2FsB1.Emit(_tall, _base + Vector3.up * 1.4f, 10);
        }

        protected override void OnResidueEnd()
        {
            if (ResWhy == Vol2MaterialEnd.Cleansed || ResWhy == Vol2MaterialEnd.ChildConsumed) Vol2FsB1.Emit(_steam, _base + Vector3.up * 1.2f, 24);
            if (ResWhy == Vol2MaterialEnd.Consumed || ResWhy == Vol2MaterialEnd.HitCancelled || ResWhy == Vol2MaterialEnd.Replaced) { Vol2FsB1.Emit(_embers, _base + Vector3.up * 1.2f, 160); Vol2FsB1.Emit(_smoke, _base + Vector3.up * 1.2f, 6); }
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            // the streak back to the brush fades fast
            DrawStreak(1f, 1f - Vol2Fs.Smooth(0.05f, 0.5f, h));
            // the hit's burst of fire-rimmed ink
            _burstS.Clear();
            if (_burst != null) Vol2FsB1.DrawBurst(_burstS, _burst, HitPoint, h, cam, 0.5f, 0.1f);
            _burstS.Write(_burstMesh);
            float ra = ResAge(t), warn = Warn(NowUs), ex = ExitK(t), pulse = DrawPulse(t);
            // the struck body burns for the body action's 2 s (데운다: mirror only - the burn itself is the state sink's)
            foreach (var s in Hits)
                if (t - s.At < 2f && Time.frameCount % 2 == 0) { var c = CentreNow(s); Vol2FsB1.EmitV(_fire, c + Random.insideUnitSphere * 0.35f, Vector3.up * 1.2f, 0.45f + 0.3f * Random.value, -1f); if (Time.frameCount % 6 == 0) Vol2FsB1.Emit(_smoke, c + Vector3.up * 0.6f, 1); }
            if (!_treeBuilt)
            {
                _treeLight.intensity = Mathf.Max(0f, 14f * (1f - h / 0.5f));
                return;
            }
            // the tree grows up (0.55 s, branch by branch), then stands; it burns back from the tips on expiry
            float grow = ra < 0f ? 0f : Vol2Fs.EaseOutBack(ra / 0.55f);
            for (int k = 0; k < _tree.Tubes.Count; k++)
            {
                float g = ra < 0f ? 0f : Mathf.Clamp01(Vol2Fs.EaseOutBack((ra - _branchAt[k]) / 0.45f));
                float burnBack = NaturalEnd ? Vol2Fs.Smooth(0.05f + 0.25f * (k == 0 ? 1f : 0f), 0.95f, ex) : Vol2Fs.Smooth(0f, 0.6f, ex);
                _tree.Tubes[k].Grow = g * (1f - burnBack);
            }
            _tree.Write(_treeMesh);
            float alive = grow * (1f - ex);
            // flames off the branch tips and along the trunk, smoke from the crown, embers drifting
            if (alive > 0.05f && ResEndAt < 0f)
            {
                int every = warn > 0.01f ? 3 : 1;
                for (int k = 1; k < _tree.Tubes.Count; k++)
                {
                    if ((k + Time.frameCount) % every != 0) continue;
                    var tb = _tree.Tubes[k];
                    if (warn > 0.01f && Random.value < warn * 0.6f) continue;           // the flames gutter before the expiry
                    // r2: small tongues all along the branch (r1: big blobs at the tips read as a fire cloud, not a burning tree)
                    float save = tb.Grow; tb.Grow = save * Random.Range(0.25f, 1f); Vector3 along = Vol2FsTubes.TipOf(tb); tb.Grow = save;
                    Vol2FsB1.EmitV(_fire, along + Random.insideUnitSphere * 0.05f, Vector3.up * 0.8f + Random.insideUnitSphere * 0.2f, (0.3f + 0.25f * Random.value) * (1f + 0.6f * pulse), -1f);      // r3: bigger tongues (r2 read as bare black antlers)
                    Vector3 tip = Vol2FsTubes.TipOf(tb);
                    Vol2FsB1.EmitV(_fire, tip + Random.insideUnitSphere * 0.05f, Vector3.up * 1.1f, (0.38f + 0.25f * Random.value) * (1f + 0.6f * pulse), -1f);
                    if (k % 3 == 0) Vol2FsB1.Emit(_embers, tip, 1);
                }
                if (Time.frameCount % 5 == 0) Vol2FsB1.EmitV(_tall, _base + Vector3.up * (0.3f + 0.7f * Random.value) + Random.insideUnitSphere * 0.2f, Vector3.up * 1.2f, (0.55f + 0.3f * Random.value) * (1f - 0.6f * warn) * (1f + 0.5f * pulse), -1f);
                if (Time.frameCount % 7 == 0) Vol2FsB1.Emit(_smoke, _base + Vector3.up * 2.4f, 1);
                if (Time.frameCount % 2 == 0) Vol2FsB1.Emit(_embers, _base + Vector3.up * 0.2f + Random.insideUnitSphere * 0.6f, 1);
            }
            // natural expiry: the branches burn back from the tips into rising ash (CSV 끝부터 타 재로 날림)
            if (Exiting)
            {
                if (!_exitFx) { _exitFx = true; if (NaturalEnd) Vol2FsB1.Emit(_smoke, _base + Vector3.up * 1.8f, 5); }
                if (ex < 1f)
                    for (int k = 1; k < _tree.Tubes.Count; k++)
                        if (_tree.Tubes[k].Grow > 0.02f && (k + Time.frameCount) % 2 == 0) { Vector3 tip = Vol2FsTubes.TipOf(_tree.Tubes[k]); Vol2FsB1.Emit(_ash, tip, NaturalEnd ? 3 : 1); if (NaturalEnd) Vol2FsB1.Emit(_embers, tip, 1); }
            }
            // light and the scorched, glowing ground under it
            float flick = 0.75f + 0.25f * Mathf.PerlinNoise(t * 9f, 3.1f);
            float warnFlick = warn > 0f ? Mathf.Lerp(1f, 0.35f + 0.65f * Mathf.PerlinNoise(t * 31f, 1.7f), warn) : 1f;
            _treeLight.intensity = Mathf.Max(h < 0.5f ? 14f * (1f - h / 0.5f) : 0f, 7.5f * alive * flick * warnFlick * (1f + 0.8f * pulse));
            _scorch.Clear();
            float sl = Vol2Fs.Smooth(0f, 0.4f, Mathf.Max(0f, ra)) * (1f - Vol2Fs.Smooth(0.4f, 1f, ex));
            if (sl > 0.01f && ra >= 0f) _scorch.Quad(new Vector3(_base.x, Ground + 0.025f, _base.z), Right, Fwd, 3.4f, new Color(1f, 1f, 1f, sl), 0.53f);
            _scorch.Write(_scorchMesh);
        }
    }
}
