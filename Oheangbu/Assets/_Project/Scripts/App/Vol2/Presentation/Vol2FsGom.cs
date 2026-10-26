using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>곰 (wood, ㅗ fan, 심는다) - free showcase batch 5, concept gom_b (round green spore pods lie dormant over the fan like closed buds; where the bear
    /// stepped in they burst open in a chain, each pod flinging out snaring tendrils and leaves, the chain racing across the fan to the other bear). Not 고's
    /// bamboo sweep, 곡's growing grove or 곤's creeping runners: a field of closed pods that WAIT. Executor facts only: the start = the pods push up out of the
    /// ground (a puff of soil) -> the wait (each pod breathes on the seed's heartbeat with a jade glow through its veins, a few curled sprouts at its foot; the far
    /// edge's ink arc burns down; the last 0.4 s the pods quiver) -> Consumed = the burst runs as a wave from the entering bear: every pod splits into leaves and
    /// jade sparks and throws up two whipping tendrils; every struck body is coiled by vines for the bind's 2 s (the status sink owns the slow; mirrored) that then
    /// loosen into rootlets reaching forward (CSV 끝: 잔뿌리로 풀림(앞으로 뻗으며)); Expired = the pods shrivel brown from the far edge and send out short rootlets.</summary>
    public sealed class Vol2FsGom : Vol2FsSeedFan
    {
        static readonly Color Jade = new Color(0.4f, 1f, 0.45f);
        const float HoldSeconds = 2f, UnravelSeconds = 0.9f;
        ParticleSystem _leaves, _sparks, _dirt;
        Material _pod, _dryPod, _vine;
        Mesh _whipMesh, _coilMesh, _rootMesh, _sproutMesh, _burstMesh;
        readonly Vol2FsTubes _whips = new Vol2FsTubes(), _coils = new Vol2FsTubes(), _roots = new Vol2FsTubes(), _sprouts = new Vol2FsTubes();
        readonly Vol2FsStrips _burstS = new Vol2FsStrips();
        readonly List<(Vol2FsTubes.Tube T, float At)> _whipAt = new List<(Vol2FsTubes.Tube, float)>();
        readonly List<(Struck S, float At, int From, int To)> _held = new List<(Struck, float, int, int)>();
        readonly List<(Vector3 At, float T, List<Vol2FsB1.Stroke> B)> _bursts = new List<(Vector3, float, List<Vol2FsB1.Stroke>)>();
        Light _glow;
        bool _rootsBuilt;
        protected override float FireSeconds => HoldSeconds + UnravelSeconds + 0.4f;
        protected override float ExpireSeconds => 1.4f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _pod = Kit.Bark(new Color(0.6f, 0.95f, 0.5f), new Color(0.3f, 1.2f, 0.35f) * 3.5f, 0.35f, "fs_vine", "fs_vein");
            _dryPod = Kit.Bark(new Color(0.5f, 0.38f, 0.25f), Color.black, 0.2f, "fs_vine", "fs_vein");
            _vine = Kit.Bark(new Color(0.8f, 1f, 0.6f), new Color(0.35f, 1.3f, 0.3f) * 4f, 0.3f, "fs_vine", "fs_vein");
            var leaf = M("fs_leaf", new Color(0.85f, 1f, 0.7f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leaves = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 1f, Life1 = 2f, Speed0 = 2f, Speed1 = 5.5f, Size0 = 0.12f, Size1 = 0.26f, Radius = 0.2f, Gravity = 0.35f, Drag = 1.5f, Noise = 1.1f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 900 }, root, "Leaves");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 4.5f, 1f)), Life0 = 0.4f, Life1 = 1f, Speed0 = 1f, Speed1 = 5f, Size0 = 0.03f, Size1 = 0.07f, Radius = 0.2f, Drag = 1.4f, Stretch = true, StretchVel = 0.04f, Noise = 1.2f, NoiseFreq = 2f, Max = 700 }, root, "Sparks");
            var dirt = M("fs_ink", new Color(0.3f, 0.17f, 0.11f, 1f), 1f, 0f); dirt.NoiseAmt = 0.4f; dirt.Soft = 0.1f;
            _dirt = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dirt), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 1.5f, Speed1 = 4f, Size0 = 0.05f, Size1 = 0.16f, Shape = ParticleSystemShapeType.Cone, Angle = 40f, Radius = 0.15f, ShapeRot = new Vector3(-90f, 0f, 0f), Gravity = 2.4f, Tiles = 2, AlphaHoldTo = 0.7f, Max = 400 }, root, "Dirt");
            foreach (var ps in new[] { _leaves, _sparks, _dirt }) ps.Play(false);
            Vol2Fs.MeshObject(root, "Whips", _vine, out _whipMesh);
            Vol2Fs.MeshObject(root, "Coils", _vine, out _coilMesh);
            Vol2Fs.MeshObject(root, "Roots", _vine, out _rootMesh);
            Vol2Fs.MeshObject(root, "Sprouts", _vine, out _sproutMesh);
            Vol2Fs.MeshObject(root, "InkBursts", Kit.InkSolid("fs_stroke2", InkC, new Color(0.45f, 1.5f, 0.35f, 1f), 0.12f, 0.45f, 0.3f, 1.3f), out _burstMesh);
            _glow = Vol2Fs.PointLight(root, "FieldGlow", Jade, 6f);
        }

        protected override void Planted(Pod p)
        {
            var root = Root.transform;
            var podMesh = Kit.SharedMesh("gomPod", () => Vol2FsB1.Rock(331, 0.5f, 1.15f, 12, 9));
            foreach (var s in Spots)
            {
                var r = Vol2FsB1.Prop(root, "Pod" + s.K, podMesh, _pod, false);
                s.T = r.transform; s.T.localScale = Vector3.one * 0.01f;
                // a few curled sprouts at its foot
                for (int k = 0; k < 2; k++)
                {
                    float a = s.Rnd * 6.28f + k * 2.6f; Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var tb = _sprouts.Add(new[] { s.P + d * 0.12f, s.P + d * 0.2f + Vector3.up * 0.18f, s.P + d * 0.12f + Vector3.up * 0.3f, s.P + d * 0.04f + Vector3.up * 0.26f }, 0.02f, 0.005f, 10); tb.Sides = 5;
                }
                Vol2FsB1.Emit(_dirt, s.P, 3);
            }
            _glow.transform.position = InFan(0.65f, 0f) + Vector3.up * 0.6f;
        }

        protected override void SpotBurst(Spot s)
        {
            if (s.T != null) s.T.gameObject.SetActive(false);
            Vector3 c = s.P + Vector3.up * 0.2f;
            Vol2FsB1.Emit(_leaves, c, 22); Vol2FsB1.Emit(_sparks, c, 24); Vol2FsB1.Emit(_dirt, s.P, 6);
            for (int k = 0; k < 2; k++)
            {
                float a = s.Rnd * 6.28f + k * 3.1f; Vector3 d = new Vector3(Mathf.Cos(a), 1.6f, Mathf.Sin(a)).normalized;
                Vector3 bend = Vector3.Cross(d, Vector3.up).normalized * 0.35f;
                float L = 1.1f + 0.8f * Vol2Fs.Hash(Seed, s.K + 50 + k);
                var tb = _whips.Add(new[] { s.P, s.P + d * L * 0.4f + bend * 0.2f, s.P + d * L * 0.8f + bend * 0.5f, s.P + d * L + bend * 0.2f + Vector3.down * 0.3f }, 0.055f, 0.01f, 14); tb.Sides = 5;
                _whipAt.Add((tb, s.BurstAt));
            }
            if (s.K % 4 == 0)
            {
                Vector3 side = Vector3.Cross(Vector3.up, FlatN(CamPos - c)).normalized;
                _bursts.Add((c, s.BurstAt, Vol2FsB1.Burst(Seed + s.K, 6, side, -FlatN(CamPos - c), 0.6f, 1.4f, 0.1f, 0.22f, 0.7f, 0.2f)));
            }
        }

        protected override void OnStruckBody(Pod p, Struck s)
        {
            var b = s.T != null ? BodyBounds(s.T, s.T.position, 0.5f) : s.B; int from = _coils.Tubes.Count; int seed = Seed + (int)(s.Id & 0xff);
            for (int k = 0; k < 5; k++)
            {
                var ctrl = Vol2FsB4F.Coil(b, Ground, 9, k * 1.26f + Vol2Fs.Hash(seed, k), 1.5f + 0.5f * Vol2Fs.Hash(seed, k + 9), 0.9f, 0.1f);
                var tb = _coils.Add(ctrl, 0.08f, 0.03f, 36); tb.Knots = 0.3f; tb.Sides = 6;
            }
            // the coil starts when the wave reaches that body
            float at = WaveAt(FeetOf(s)); if (at < 0f) at = Now(NowUs);
            _held.Add((s, at, from, _coils.Tubes.Count));
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos; var p = The;
            _burstS.Clear();
            float beat = p != null ? Beat(p) : 0f, warn = p != null ? PodWarn(p, t) : 0f, life = p != null ? PodLife(p, t, 0.4f, 0.01f) : 0f;
            bool waiting = p != null && p.Started && !p.Ended;
            // the pods: up out of the ground, breathing, quivering at the warning, shrivelling at the expiry
            foreach (var s in Spots)
            {
                if (s.T == null || s.Burst) continue;
                float w = SpotWither(s, t);
                float grow = p != null ? Vol2Fs.Smooth(s.U * 0.25f, s.U * 0.25f + 0.35f, PodAge(p, t)) : 0f;
                bool on = p != null && p.Started && (waiting || (Withered(p) && w < 0.99f));
                s.T.gameObject.SetActive(on);
                if (!on) continue;
                if (Withered(p) && s.T.GetComponent<MeshRenderer>().sharedMaterial != _dryPod) s.T.GetComponent<MeshRenderer>().sharedMaterial = _dryPod;
                float sz = (0.42f + 0.12f * s.Rnd) * grow * (1f + 0.1f * beat + 0.08f * warn) * (1f - 0.6f * w);
                s.T.localScale = new Vector3(sz, sz * (1f - 0.5f * w), sz);
                s.T.position = s.P + Vector3.up * sz * 0.35f + (warn > 0f ? Random.insideUnitSphere * 0.02f * warn : Vector3.zero);
                s.T.rotation = Quaternion.Euler(0f, s.Rnd * 360f, 6f * Mathf.Sin(t * 2f + s.K));
                if (waiting && Time.frameCount % 23 == s.K % 23) Vol2FsB1.EmitV(_sparks, s.P + Vector3.up * 0.3f, Vector3.up * 0.4f, -1f, 0.7f);
            }
            float sproutK = p == null ? 0f : waiting ? life : Withered(p) ? 1f - Vol2Fs.Smooth(0.2f, 0.8f, PodExit(p, t)) : Mathf.Max(0f, 1f - SinceFire(p, t) * 3f);
            foreach (var tb in _sprouts.Tubes) tb.Grow = sproutK;
            _sprouts.Write(_sproutMesh);
            _glow.intensity = waiting ? life * (0.6f + 1.6f * beat + warn) * (1f - 0.6f * Held01) : p != null && p.FiredAt >= 0f ? 3f * Mathf.Max(0f, 1f - SinceFire(p, t) * 1.5f) : 0f;
            // the whips: lash up as the wave passes, fall back
            foreach (var (tb, at) in _whipAt) { float a = t - at; tb.Grow = Vol2Fs.Smooth(0f, 0.12f, a) * (1f - Vol2Fs.Smooth(0.45f, 0.85f, a)); }
            _whips.Write(_whipMesh);
            foreach (var (at, t0, b) in _bursts) Vol2FsB1.DrawBurst(_burstS, b, at, t - t0, cam, 0.4f, 0.1f);
            _burstS.Write(_burstMesh);
            // the coils on the struck bodies: hold the bind's 2 s, then loosen into rootlets reaching forward
            foreach (var (s, at, from, to) in _held)
            {
                float a = t - at, grow = Vol2Fs.Smooth(0f, 0.3f, a), loose = Vol2Fs.Smooth(HoldSeconds, HoldSeconds + UnravelSeconds, a);
                for (int i = from; i < to; i++) _coils.Tubes[i].Grow = grow * (1f - loose);
                if (a > 0f && a < HoldSeconds && Time.frameCount % 5 == 0) Vol2FsB1.Emit(_sparks, CentreNow(s) + Random.insideUnitSphere * 0.4f, 1);
                if (a > 0f && a < 0.05f) Vol2FsB1.Emit(_leaves, CentreNow(s), 50);
            }
            _coils.Write(_coilMesh);
            if (!_rootsBuilt && _held.Count > 0 && t - _held[0].At > HoldSeconds) BuildRoots(_held, 5, 1.3f);
            if (!_rootsBuilt && p != null && Withered(p) && PodExit(p, t) > 0.3f) BuildRootsFromSpots();
            if (_rootsBuilt)
            {
                float a0 = _held.Count > 0 ? t - _held[0].At - HoldSeconds : (p != null ? (PodExit(p, t) - 0.3f) * ExpireSeconds : 0f);
                float r = Vol2Fs.Smooth(0f, 0.6f, a0), sink = Vol2Fs.Smooth(UnravelSeconds, UnravelSeconds + 0.4f, a0);
                foreach (var tb in _roots.Tubes) tb.Grow = r * (1f - sink);
                _roots.Write(_rootMesh);
            }
        }

        void AddRoot(Vector3 feet, Vector3 fwd, float len, int k, float r0)
        {
            var pts = new Vector3[6];
            for (int i = 0; i < 6; i++) { float u = i / 5f; pts[i] = G(feet) + fwd * len * u + Vector3.Cross(Vector3.up, fwd) * Mathf.Sin(u * 5f + k) * 0.12f + Vector3.up * (0.04f + 0.05f * (1f - u)); }
            var tb = _roots.Add(pts, r0, 0.008f, 18); tb.Sides = 5; tb.Knots = 0.4f;
        }

        void BuildRoots(List<(Struck S, float At, int From, int To)> held, int n, float len)
        {
            _rootsBuilt = true;
            foreach (var (s, at, from, to) in held)
            {
                Vector3 feet = FeetOf(s), fwd = FlatN(feet - Apex);
                for (int k = 0; k < n; k++) AddRoot(feet, Quaternion.AngleAxis(((k / (float)(n - 1)) - 0.5f) * 80f, Vector3.up) * fwd, len * (0.7f + 0.5f * Vol2Fs.Hash(Seed, k + 7)), k, 0.05f);
            }
        }

        void BuildRootsFromSpots()
        {
            _rootsBuilt = true;
            foreach (var s in Spots) if (s.K % 3 == 0) AddRoot(s.P, FlatN(s.P - Apex), 0.6f, s.K, 0.03f);
        }
    }
}
