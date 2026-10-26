using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>뭄 (earth, ㅜ ring, 심는다 - the PEAK: the ground hardens) - free showcase batch 5, concept mum_b (a circle of small stone seeds lies on the 2 m band;
    /// when the CASTER steps onto it the ring hardens into a round stone platform with a carved fret rim - KCISA Pattern_25, fs_seal25, read only - glowing amber
    /// in its seams, and the bears inside are held by heavy stone shackles round their paws: they cannot dash or jump). Executor facts only: the launch = the
    /// pebbles settle on the band in a puff of grit -> the wait (ember seams breathe on the heartbeat, grit trickles UP, the ink arc inside the band burns
    /// down; the last 0.4 s they rattle) -> Consumed by the caster stepping onto the band (Struck.World; the hub's caster place - in the lab the hub's override)
    /// = the hardening races round from the caster's angle: rampart stones rise, the fret frieze lights, the floor inside turns to stone, dust and heavy ink
    /// sweep round -> the After material (Residue "hardground", 8 s) = the platform stands from its creation to its end, every body standing inside its outer
    /// edge wears stone locks round the paws (a presentation reading of the registry - the dash / jump block itself is Vol2SeedWorld's), the last 0.4 s it
    /// trembles -> its end = the rampart sinks while the ground round the ring lifts as plates and settles (CSV 끝: 가라앉으며 둘레 땅이 판으로 들림(떨어지는
    /// 것 없음)); seed Expired (nobody stepped on) = the pebbles sink and the earth just outside the band lifts a little and settles.</summary>
    public sealed class Vol2FsMum : Vol2FsSeedRing
    {
        static readonly Color DustC = new Color(0.55f, 0.42f, 0.34f, 1f), Seam = new Color(1f, 0.58f, 0.22f);
        const int Walls = 22;
        ParticleSystem _chunks, _grit, _cloud, _upDust, _glow;
        Material _pebble, _wallMat, _plateMat;
        Mesh _friezeMesh, _floorMesh, _seamMesh, _inkMesh, _lockMesh;
        readonly Vol2FsStrips _frieze = new Vol2FsStrips(), _floor = new Vol2FsStrips(), _seam = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _lock = new Vol2FsStrips();
        readonly List<Transform> _pebbles = new List<Transform>();
        sealed class Wall { public Transform T; public float Ang, H, Rnd; public Vector3 Base; public Quaternion Rot; }
        readonly List<Wall> _walls = new List<Wall>();
        readonly List<(Transform T, Vector3 Base, Quaternion Rot)> _plates = new List<(Transform, Vector3, Quaternion)>();
        sealed class Lock { public long Id; public readonly List<Transform> Stones = new List<Transform>(); public float At; public Vector3 Feet; public float Radius; }
        readonly Dictionary<long, Lock> _locks = new Dictionary<long, Lock>();
        readonly List<List<Vol2FsB1.Stroke>> _swirls = new List<List<Vol2FsB1.Stroke>>();
        Light[] _lights;
        bool _platesBuilt;
        protected override int SeedCount => 22;
        protected override float RaceSeconds => 0.45f;
        protected override float FireSeconds => 0.8f;
        protected override float ExpireSeconds => 1.3f;
        protected override float AfterExitSeconds => 1.5f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _pebble = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.66f, 0.55f, 0.46f), 0f, 0.1f, Seam * 1.1f, "fs_vein");
            _wallMat = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.86f, 0.74f, 0.6f), 0f, 0.1f, Seam * 0.45f, "fs_vein");
            _plateMat = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.62f, 0.5f, 0.42f), 0f, 0.1f);
            var chunk = Kit.SharedMesh("chunk0", () => Vol2FsB1.Rock(23, 0.5f, 0.7f, 7, 5));
            _chunks = Vol2Fs.Build(new Vol2FsPs { Mat = _plateMat, Mesh = chunk, Life0 = 0.8f, Life1 = 1.4f, Speed0 = 1.5f, Speed1 = 5f, Size0 = 0.06f, Size1 = 0.2f, Radius = 0.25f, Gravity = 2.2f, Drag = 0.3f, Spin = 500f, Max = 400 }, root, "Chunks");
            _grit = Vol2Fs.Build(new Vol2FsPs { Mat = _plateMat, Mesh = chunk, Life0 = 0.5f, Life1 = 1f, Speed0 = 0.1f, Speed1 = 0.5f, Size0 = 0.02f, Size1 = 0.05f, Radius = 0.08f, Gravity = 1.6f, Spin = 400f, Max = 400 }, root, "Grit");
            _cloud = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(DustC, 0.55f)), Life0 = 1f, Life1 = 2f, Speed0 = 0.6f, Speed1 = 2.4f, Size0 = 0.8f, Size1 = 1.6f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.3f, Drag = 2f, Tiles = 2, SizeStart = 0.4f, SizePeak = 1.2f, SizeEnd = 1.7f, Max = 220, SortFudge = 10 }, root, "Cloud");
            _upDust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(DustC, 0.3f)), Life0 = 0.8f, Life1 = 1.4f, Speed0 = 0.1f, Speed1 = 0.3f, Size0 = 0.06f, Size1 = 0.15f, Radius = 0.08f, Tiles = 2, VelOverLife = new Vector3(0f, 0.45f, 0f), SizeEnd = 2f, Max = 300 }, root, "UpDust");
            _glow = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Seam, 2.2f, 1f)), Life0 = 0.1f, Life1 = 0.16f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.2f, Size1 = 0.3f, Max = 200 }, root, "SeamGlow");
            foreach (var ps in new[] { _chunks, _grit, _cloud, _upDust, _glow }) ps.Play(false);
            var fr = M("fs_seal25", new Color(0.9f, 0.45f, 0.16f, 1f), 0.95f, 0.45f); fr.NoiseAmt = 0.2f; fr.Erode = 0.15f; fr.Soft = 0.02f; fr.Scroll = Vector2.zero;
            Vol2Fs.MeshObject(root, "Frieze", Kit.Mat(fr), out _friezeMesh);
            var fl = M("fs_rock_crack", new Color(0.62f, 0.54f, 0.46f, 1f), 1f, 0f); fl.Opacity = 0.85f; fl.NoiseAmt = 0.35f; fl.Soft = 0.02f; fl.Scroll = Vector2.zero;
            Vol2Fs.MeshObject(root, "Floor", Kit.Mat(fl), out _floorMesh);
            var sm = M("fs_vein", Seam, 2.2f, 1f); sm.NoiseAmt = 0.3f; sm.Soft = 0.02f; sm.Scroll = Vector2.zero;
            Vol2Fs.MeshObject(root, "Seams", Kit.Mat(sm), out _seamMesh);
            Vol2Fs.MeshObject(root, "InkSweep", Kit.InkSolid("fs_stroke2", InkC, new Color(0.9f, 0.55f, 0.25f, 1f), 0.08f, 0.45f, 0.3f, 1.3f), out _inkMesh);
            var lk = M("fs_stroke2", new Color(0.2f, 0.15f, 0.12f, 1f), 1f, 0f); lk.Edge = Seam * 2f; lk.EdgeWidth = 0.35f; lk.NoiseAmt = 0.25f; lk.Soft = 0.05f;
            Vol2Fs.MeshObject(root, "LockRings", Kit.Mat(lk), out _lockMesh);
            _lights = new Light[3];
            for (int k = 0; k < 3; k++) _lights[k] = Vol2Fs.PointLight(root, "Seam" + k, Seam, 4f);
        }

        protected override void Planted(Pod p)
        {
            var root = Root.transform; int seed = Seed + 7;
            for (int k = 0; k < SeedAng.Count; k++)
            {
                var r = Vol2FsB1.Prop(root, "Pebble" + k, Kit.SharedMesh("mumPebble", () => Vol2FsB1.Rock(51, 0.5f, 0.75f, 9, 6)), _pebble, false);
                r.transform.SetPositionAndRotation(OnBand(SeedAng[k], R + (Vol2Fs.Hash(seed, k) - 0.5f) * 0.18f) + Vector3.up * 0.05f, Quaternion.Euler(0f, Vol2Fs.Hash(seed, k + 3) * 360f, 0f));
                r.transform.localScale = Vector3.one * 0.01f;
                _pebbles.Add(r.transform);
                Vol2FsB1.Emit(_grit, r.transform.position, 2);
            }
        }

        protected override void Fired(Pod p)
        {
            var root = Root.transform; int seed = Seed + 211;
            for (int k = 0; k < Walls; k++)
            {
                float a = (k + 0.5f) / Walls * Mathf.PI * 2f - Mathf.PI;
                var w = new Wall { Ang = a, Rnd = Vol2Fs.Hash(seed, k), H = 0.95f * Vol2FsPolish.RingHeight(a) * (0.9f + 0.25f * Vol2Fs.Hash(seed, k + 4)) };
                var pr = Vol2FsB1.Prop(root, "Rampart" + k, Kit.SharedMesh("mumWall" + (k % 3), () => Vol2FsB1.Slab(720 + k % 3, new Vector3(1f, 1f, 1f), 0.92f)), _wallMat);
                w.T = pr.transform; w.Base = OnBand(a, R);
                w.Rot = Quaternion.LookRotation(Dir(a), Vector3.up);
                w.T.localScale = new Vector3(2f * Mathf.PI * R / Walls * 1.12f, 0.001f, 0.42f);
                w.T.SetPositionAndRotation(w.Base, w.Rot);
                _walls.Add(w);
            }
            for (int q = 0; q < 6; q++)
            {
                float a = q / 6f * Mathf.PI * 2f - Mathf.PI;
                _swirls.Add(Vol2FsB1.Burst(Seed + q * 17, 2, Tangent(a), Dir(a), 1.4f, 2.6f, 0.26f, 0.48f, 0.12f, 0.6f));
            }
            Vector3 cp = G(CasterNow());
            Vol2FsB1.Emit(_cloud, cp, 6); Vol2FsB1.Emit(_chunks, cp + Vector3.up * 0.2f, 20);
        }

        void BuildPlates(float r)
        {
            _platesBuilt = true; var root = Root.transform;
            for (int k = 0; k < 16; k++)
            {
                float ang = Vol2FsB2.Angle(k, 16, Seed + 7, 0.3f);
                if (Front(ang) < 0.2f) continue;
                Vector3 o = Dir(ang);
                var pr = Vol2FsB1.Prop(root, "Plate" + k, Kit.SharedMesh("plate" + (k % 3), () => Vol2FsB1.Slab(400 + k % 3, new Vector3(1f, 0.22f, 0.8f), 0.9f)), _plateMat);
                pr.transform.localScale = new Vector3(Mathf.Lerp(0.7f, 1.1f, Vol2Fs.Hash(Seed, k + 70)), 1f, 0.8f);
                _plates.Add((pr.transform, OnBand(ang, r) + Vector3.down * 0.2f, Quaternion.LookRotation(o, Vector3.up)));
            }
        }

        void Locks(float t, float stand, float warn, Vector3 cam)
        {
            var inside = BodiesInside();
            foreach (var b in inside)
            {
                if (_locks.ContainsKey(b.Id)) continue;
                var l = new Lock { Id = b.Id, At = t, Feet = G(b.Feet), Radius = Mathf.Max(0.35f, b.Radius) };
                for (int k = 0; k < 5; k++)
                {
                    float a = k / 5f * Mathf.PI * 2f + Vol2Fs.Hash(Seed, (int)(b.Id % 50) + k);
                    var pr = Vol2FsB1.Prop(Root.transform, "Lock" + b.Id + "_" + k, Kit.SharedMesh("mumWall" + (k % 3), () => Vol2FsB1.Slab(720 + k % 3, Vector3.one, 0.92f)), _wallMat);
                    pr.transform.SetPositionAndRotation(l.Feet + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * l.Radius * 0.95f, Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f));
                    l.Stones.Add(pr.transform);
                }
                _locks[b.Id] = l;
                Vol2FsB1.Emit(_chunks, l.Feet + Vector3.up * 0.2f, 12); Vol2FsB1.Emit(_cloud, l.Feet, 1);
            }
            foreach (var l in _locks.Values)
            {
                var tr = TargetTransform(l.Id); if (tr != null) l.Feet = G(tr.position);
                float g = Vol2Fs.Smooth(l.At, l.At + 0.2f, t) * stand;
                for (int k = 0; k < l.Stones.Count; k++)
                {
                    float a = k / (float)l.Stones.Count * Mathf.PI * 2f;
                    var st = l.Stones[k];
                    st.position = l.Feet + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * l.Radius * 0.95f + Vector3.down * 0.25f * (1f - g) + (warn > 0f ? Random.insideUnitSphere * 0.02f * warn : Vector3.zero);
                    st.localScale = new Vector3(0.38f, 0.32f * Mathf.Max(0.01f, g), 0.22f);
                    st.gameObject.SetActive(g > 0.01f);
                }
                for (int r = 0; r < 2; r++)
                    if (g > 0.01f) Vol2FsB5.Halo(_lock, l.Feet + Vector3.up * (0.1f + 0.16f * r), Vector3.up, l.Radius * (1.02f - 0.1f * r), 0.07f, g * 0.9f, 0.3f + r * 0.2f + l.Id * 0.01f, cam, 28);
            }
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos; var p = The;
            _frieze.Clear(); _floor.Clear(); _seam.Clear(); _ink.Clear(); _lock.Clear();
            if (p == null || !p.Started) { Flush(); return; }
            float life = PodLife(p, t, 0.35f), beat = Beat(p), warn = PodWarn(p, t), ex = PodExit(p, t);
            bool worked = Worked(p), withered = Withered(p);
            float grow = Mathf.Clamp01(PodAge(p, t) / 0.3f), s = SinceFire(p, t);
            for (int k = 0; k < _pebbles.Count; k++)
            {
                float a = SeedAng[k];
                bool on = !p.Ended || withered && ex < 0.95f || worked && s < RaceDelay(a);
                var pb = _pebbles[k];
                pb.gameObject.SetActive(on);
                if (!on) continue;
                float sink = withered ? Vol2Fs.Smooth(0.1f, 0.7f, ex) : 0f;
                pb.localScale = Vector3.one * Mathf.Max(0.01f, SeedScale(a) * 0.17f * grow * (1f + 0.05f * beat));
                pb.position = OnBand(a, R) + Vector3.up * (0.05f - 0.12f * sink) + (warn > 0f ? Random.insideUnitSphere * 0.025f * warn : Vector3.zero);
                if (!p.Ended && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_glow, pb.position + Vector3.up * 0.05f, Vector3.zero, (0.1f + 0.18f * beat + 0.12f * warn) * SeedScale(a) * life * (1f - 0.5f * Held01), 0.12f);
                if (!p.Ended && Time.frameCount % 17 == k % 17 && Front(a) > 0.3f) Vol2FsB1.EmitV(_upDust, pb.position + Vector3.up * 0.08f, Vector3.up * 0.25f, -1f, -1f);
            }
            if (!p.Ended) for (int k = 0; k < 3; k++) { _lights[k].transform.position = OnBand(-1.2f + 1.2f * k, R) + Vector3.up * 0.3f; _lights[k].intensity = life * (0.3f + 1f * beat + warn) * (1f - 0.6f * Held01); }
            if (withered)
            {
                if (!_platesBuilt) BuildPlates(R + 0.7f);
                float lift = Mathf.Sin(Mathf.Clamp01((ex - 0.25f) / 0.7f) * Mathf.PI) * 0.6f;
                MovePlates(lift, ex < 0.97f && ex > 0.2f);
                for (int k = 0; k < 3; k++) _lights[k].intensity = 0f;
            }
            if (worked)
            {
                // the platform stands from the trigger (the After material is made on the same tick) to the After's end + its exit
                float stand = After != null ? AfterLife(t, 0.3f, AfterExitSeconds) : 1f - Vol2Fs.Smooth(0.8f, 1.6f, s);
                float aw = AfterWarn(t), aex = AfterExit(t, AfterExitSeconds);
                float sinkK = Vol2Fs.Smooth(0f, 0.8f, aex);
                foreach (var w in _walls)
                {
                    float since = s - RaceDelay(w.Ang);
                    float g = since <= 0f ? 0f : Vol2Fs.EaseOutBack(Mathf.Clamp01(since / 0.22f));
                    if (since > 0f && since - Time.deltaTime <= 0f && Front(w.Ang) > 0.3f) { Vol2FsB1.Emit(_chunks, w.Base + Vector3.up * 0.2f, 6); Vol2FsB1.Emit(_cloud, w.Base, 1); }
                    float hh = w.H * g * (1f - sinkK);
                    Vector3 shake = aw > 0f ? new Vector3(Mathf.PerlinNoise(t * 31f, w.Rnd * 9f) - 0.5f, 0f, Mathf.PerlinNoise(w.Rnd * 7f, t * 29f) - 0.5f) * 0.06f * aw : Vector3.zero;
                    w.T.SetPositionAndRotation(w.Base + shake, w.Rot);
                    w.T.localScale = new Vector3(w.T.localScale.x, Mathf.Max(0.001f, hh), w.T.localScale.z);
                    w.T.gameObject.SetActive(hh > 0.003f && aex < 0.999f);
                }
                // the frieze on the rampart's inner face, the seams at its foot, the hardened floor inside - all revealed with the race
                float front = Mathf.Atan2(F.x, F.z), lit = stand * (1f - sinkK) * (1f + 0.6f * aw * (Mathf.Sin(t * 40f) > 0f ? 1f : 0f));
                if (lit > 0.01f)
                {
                    int n = 64; var pts = new List<Vector3>(); var wd = new List<float>(); var al = new List<float>(); var side = new List<Vector3>();
                    for (int i = 0; i <= n; i++)
                    {
                        float a = (i / (float)n) * Mathf.PI * 2f - Mathf.PI;
                        float since = s - RaceDelay(a), hgt = 0.95f * Vol2FsPolish.RingHeight(a);
                        float on = since > 0.1f ? Vol2Fs.Smooth(0.1f, 0.35f, since) : 0f;
                        pts.Add(OnBand(a, R - 0.24f) + Vector3.up * hgt * 0.55f); side.Add(Vector3.up); wd.Add(hgt * 0.5f); al.Add(lit * on * Mathf.Lerp(0.3f, 1f, Front(a)));
                    }
                    _frieze.Strip(pts, wd, side, Color.white, al, 0.2f, cam, 0f, 22f);
                    Vol2FsB2.GroundRing(_seam, Apex, F, Ground, R - 0.36f, R - 0.22f, 72, a => lit * (s - RaceDelay(a) > 0.05f ? 1f : 0f) * (0.6f + 0.4f * Mathf.Sin(t * 3f + a * 4f)), 0.61f, cam, 0.02f, 0f, 8f);
                    Vol2FsB2.GroundRing(_floor, Apex, F, Ground, 0.25f, R - 0.2f, 72, a => lit * Vol2Fs.Smooth(0.15f, 0.5f, s - RaceDelay(a)) * 0.9f, 0.73f, cam, 0.012f, 0f, 4f);
                }
                // the ink sweeping round with the hardening
                for (int q = 0; q < _swirls.Count; q++)
                {
                    float a = q / (float)_swirls.Count * Mathf.PI * 2f - Mathf.PI;
                    if (Front(a) < 0.3f) continue;
                    float since = s - RaceDelay(a);
                    if (since > 0f) Vol2FsB1.DrawBurst(_ink, _swirls[q], OnBand(a, R + 0.2f) + Vector3.up * 0.25f, since, cam, 0.12f, 0.12f);
                }
                if (stand > 0.01f && sinkK < 0.99f) Locks(t, stand * (1f - sinkK), aw, cam);
                foreach (var l in _locks.Values) if (sinkK >= 0.99f) foreach (var st in l.Stones) st.gameObject.SetActive(false);
                // its end: the rampart sinks while the ground round the ring lifts as plates and settles
                if (AfterEndAt >= 0f)
                {
                    if (!_platesBuilt) { BuildPlates(R + 1.0f); foreach (var w in _walls) if (Front(w.Ang) > 0.3f) Vol2FsB1.Emit(_cloud, w.Base, 1); }
                    float lift = Mathf.Sin(Mathf.Clamp01(aex / 0.85f) * Mathf.PI);
                    MovePlates(lift, aex < 0.97f);
                    if (aex < 0.7f && Time.frameCount % 4 == 0) Vol2FsB1.Emit(_cloud, OnBand(Random.Range(-2f, 2f), R + 1f), 1);
                }
                for (int k = 0; k < 3; k++) _lights[k].intensity = (3f * (1f - Vol2Fs.Smooth(0.2f, 1f, s)) + 0.8f) * stand * (1f - sinkK);
            }
            Flush();
        }

        void MovePlates(float lift, bool on)
        {
            foreach (var pl in _plates)
            {
                Vector3 o = pl.Rot * Vector3.forward;
                pl.T.SetPositionAndRotation(pl.Base + Vector3.up * 0.18f * lift, Quaternion.AngleAxis(-26f * lift, Vector3.Cross(Vector3.up, o)) * pl.Rot);
                pl.T.gameObject.SetActive(on);
            }
        }

        void Flush() { _frieze.Write(_friezeMesh); _floor.Write(_floorMesh); _seam.Write(_seamMesh); _ink.Write(_inkMesh); _lock.Write(_lockMesh); }
    }
}
