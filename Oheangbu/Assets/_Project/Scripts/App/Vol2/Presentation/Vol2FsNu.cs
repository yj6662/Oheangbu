using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>누 (fire, ㅜ ring, 한 번 · 데운다) - free showcase batch 2, concept nu_a (a roaring circular WALL of flame erupts on the 2 m ring round the caster out of
    /// a white-hot line in the ground, flames leaning outward, twisting columns of black ink smoke rising off its crest; the caster untouched in the empty middle;
    /// the bear on the ring is engulfed and keeps burning for the cast's 2 s heat; the wall burns out from its tips into ash rising, the CSV's end). Unlike 노's
    /// fan (fire racing outward with ink surf curling at its edge) it is a closed ring: a standing wall, a ground line, vertical ink plumes.</summary>
    public sealed class Vol2FsNu : Vol2FsRing
    {
        static readonly Color Orange = new Color(1f, 0.45f, 0.1f), Hot = new Color(1f, 0.8f, 0.4f), Ink = new Color(0.035f, 0.025f, 0.02f);
        ParticleSystem _fire, _tall, _embers, _smoke, _ash; Light[] _lights;
        Mesh _lineMesh, _scorchMesh, _plumeMesh; readonly Vol2FsStrips _line = new Vol2FsStrips(), _scorch = new Vol2FsStrips(), _plume = new Vol2FsStrips();
        sealed class Pt { public Vector3 P; public float Ang, Back; public bool Fired; }
        readonly List<Pt> _pts = new List<Pt>();
        struct Plume { public float Ang, H, W, At, Twist, Rnd; }
        readonly List<Plume> _plumes = new List<Plume>();
        sealed class Burn { public ParticleSystem Fire, Smoke, Embers; public Light L; public Transform T; }
        const float WallSeconds = 1.0f;
        bool _ashed;
        protected override float LookSeconds => ExpandSeconds + WallSeconds + 0.8f;

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsB1.M(tex, tint, intensity, addMix);
        Vol2FsMat FireMat(float intensity, float addMix) { var m = M("fs_flame", Color.white, intensity, addMix); m.Ramp = "fs_ramp_fire"; m.RampScale = 1.7f; m.NoiseAmt = 0.65f; m.Erode = 0.28f; return m; }

        protected override void OnBegin()
        {
            var root = Root.transform;
            // 2026-10-27 per-glyph polish: the wall is a line of tall licking tongues (r4 of batch 2 / the material pass: flipbook blobs of 0.9 - 2.3 m filled
            // the whole frame yellow from the camera standing at the band) - see Vol2FsPolish.Lick
            _fire = Vol2Fs.Build(Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick(1.4f, 0.5f)), 0.26f, 0.46f, 0.32f, 0.62f, 2.4f, 3000, 2.3f), root, "Fire");
            _tall = Vol2Fs.Build(Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick(1.5f, 0.45f, 1.2f)), 0.45f, 0.8f, 0.45f, 0.8f, 3.4f, 900, 2.7f), root, "TallFire");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Hot, 5f, 1f)), Life0 = 0.6f, Life1 = 1.6f, Speed0 = 1.5f, Speed1 = 4.5f, Size0 = 0.03f, Size1 = 0.07f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.2f, Gravity = -0.3f,
                Noise = 1.5f, NoiseFreq = 2f, Stretch = true, StretchVel = 0.05f, Max = 1500 }, root, "Embers");
            var smoke = Vol2FsPolish.Soot(0.5f);
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(smoke), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 0.5f, Speed1 = 1.4f, Size0 = 0.9f, Size1 = 1.7f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.3f, Drag = 1f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.3f, SizePeakAt = 0.5f, SizeEnd = 1.8f, VelOverLife = new Vector3(0f, 1.4f, 0f), Max = 250, SortFudge = 12 }, root, "Smoke");
            var ash = M("fs_ink", new Color(0.5f, 0.47f, 0.44f, 1f), 1f, 0f); ash.NoiseAmt = 0.3f; ash.Edge = new Color(1.6f, 0.5f, 0.1f, 1f); ash.EdgeWidth = 0.25f; ash.Soft = 0.05f;
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ash), Life0 = 1.4f, Life1 = 2.6f, Speed0 = 0.3f, Speed1 = 1.2f, Size0 = 0.04f, Size1 = 0.12f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.3f, VelOverLife = new Vector3(0f, 1.5f, 0f),
                Noise = 1.2f, NoiseFreq = 1f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.6f, Max = 900 }, root, "Ash");
            foreach (var ps in new[] { _fire, _tall, _embers, _smoke, _ash }) ps.Play(false);
            var line = M("fs_ring", Color.white, 3.2f, 0.95f); line.Ramp = "fs_ramp_fire"; line.RampScale = 1.2f; line.NoiseAmt = 0.55f; line.NoiseScale = 2f; line.Soft = 0.001f; line.Scroll = new Vector2(0.6f, 0f);
            Vol2Fs.MeshObject(root, "HotLine", Kit.Mat(line), out _lineMesh);
            var sc = M("fs_stroke2", new Color(0.03f, 0.02f, 0.015f, 1f), 1f, 0f); sc.Soft = 0.001f; sc.NoiseAmt = 0.55f; sc.Erode = 0.3f; sc.Opacity = 0.85f; sc.Edge = new Color(1.8f, 0.45f, 0.06f, 1f); sc.EdgeWidth = 0.1f;
            Vol2Fs.MeshObject(root, "ScorchRing", Kit.Mat(sc), out _scorchMesh);
            Vol2Fs.MeshObject(root, "InkPlumes", Kit.InkSolid("fs_stroke2", Ink, new Color(2.4f, 0.75f, 0.12f, 1f), 0.07f, 0.45f, 0.26f, 1.5f), out _plumeMesh);
            _lights = new Light[4];
            for (int k = 0; k < 4; k++) _lights[k] = Vol2Fs.PointLight(root, "RingLight" + k, Orange, 6f);
        }

        protected override void OnLaunch(long nowUs)
        {
            const int N = 120;      // polish: a dense closed line of tongues (r4: 72 big blobs)
            for (int k = 0; k < N; k++)
            {
                float ang = Vol2FsB2.Angle(k, N, Seed, 0.6f);
                _pts.Add(new Pt { P = OnBand(ang, Mathf.Lerp(R - 0.2f, R + 0.2f, Vol2Fs.Hash(Seed, k + 10))) + Vector3.up * 0.05f, Ang = ang, Back = Back(ang) });
            }
            for (int k = 0; k < 9; k++)
            {
                float ang = Mathf.Lerp(-1.3f, 1.3f, (k + 0.5f) / 9f) + 0.12f * (Vol2Fs.Hash(Seed, k + 200) - 0.5f);      // polish: the plumes stand on the far arc (r4's ±112° ones rose at the lens)
                _plumes.Add(new Plume { Ang = ang, H = Mathf.Lerp(2.4f, 3.6f, Vol2Fs.Hash(Seed, k + 210)), W = Mathf.Lerp(0.5f, 0.85f, Vol2Fs.Hash(Seed, k + 220)), At = ExpandSeconds + 0.05f + 0.12f * Vol2Fs.Hash(Seed, k + 230),
                    Twist = (Vol2Fs.Hash(Seed, k + 240) - 0.5f) * 3f, Rnd = Vol2Fs.Hash(Seed, k + 250) });
            }
            for (int k = 0; k < 4; k++) _lights[k].transform.position = OnBand(-1.6f + k * 1.07f, R) + Vector3.up * 1.1f;
        }

        protected override void OnStruck(Struck s) { }

        void StartBurn(Struck s)
        {
            s.Started = true;
            var b = new Burn { T = new GameObject("Burn").transform };
            b.T.SetParent(Root.transform, false);
            float rx = Mathf.Max(0.3f, s.B.extents.x * 0.8f), ry = Mathf.Max(0.3f, s.B.extents.y * 0.7f), rz = Mathf.Max(0.3f, s.B.extents.z * 0.8f);
            var bf = Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick(1.5f, 0.5f)), 0.3f, 0.55f, 0.35f, 0.7f, 2.4f, 300, 2.2f);      // polish: tongues over the body, not blobs
            bf.Rate = 140f; bf.Loop = true; bf.Radius = 1f; bf.ShapeScale = new Vector3(rx, ry, rz);
            b.Fire = Vol2Fs.Build(bf, b.T, "BurnFire");
            var smoke = M("fs_puff", new Color(0.08f, 0.06f, 0.05f, 1f), 1f, 0f); smoke.Opacity = 0.55f; smoke.NoiseAmt = 0.6f; smoke.Erode = 0.35f;
            b.Smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(smoke), Rate = 8f, Loop = true, Life0 = 1.2f, Life1 = 2f, Speed0 = 0.2f, Speed1 = 0.5f, Size0 = 0.7f, Size1 = 1.3f, Radius = 1f, ShapeScale = new Vector3(rx, ry, rz), Tiles = 2,
                VelOverLife = new Vector3(0f, 1.4f, 0f), SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.8f, Max = 40, SortFudge = 10 }, b.T, "BurnSmoke");
            b.Embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Hot, 5f, 1f)), Rate = 30f, Loop = true, Life0 = 0.6f, Life1 = 1.4f, Speed0 = 0.5f, Speed1 = 1.5f, Size0 = 0.03f, Size1 = 0.06f, Radius = 1f, ShapeScale = new Vector3(rx, ry, rz),
                VelOverLife = new Vector3(0f, 1.8f, 0f), Noise = 1.4f, NoiseFreq = 2f, Stretch = true, StretchVel = 0.05f, Max = 100 }, b.T, "BurnEmbers");
            b.L = Vol2Fs.PointLight(b.T, "BurnLight", Orange, 5f);
            b.T.position = CentreNow(s);
            foreach (var ps in new[] { b.Fire, b.Smoke, b.Embers }) ps.Play(false);
            s.Data = b;
            Vol2FsB1.Emit(_embers, CentreNow(s), 80); Vol2FsB1.Emit(_tall, CentreNow(s), 6);
        }

        protected override void OnCastEnded(float t)
        {
            foreach (var s in Hits) if (s.Data is Burn b) { b.Fire.Stop(false, ParticleSystemStopBehavior.StopEmitting); b.Smoke.Stop(false, ParticleSystemStopBehavior.StopEmitting); b.Embers.Stop(false, ParticleSystemStopBehavior.StopEmitting); s.Ended = true; Vol2FsB1.Emit(_ash, CentreNow(s), 40); }
        }

        protected override void OnRing(float t, float a, bool ended)
        {
            var cam = CamPos;
            float burnOut = Vol2Fs.Smooth(ExpandSeconds + WallSeconds * 0.55f, ExpandSeconds + WallSeconds, a);
            // the wall: every point flares when the eruption reaches the band, then keeps burning (sparser) until the burn-out; the near arc burns low
            for (int k = 0; k < _pts.Count; k++)
            {
                var p = _pts[k];
                if (a < ExpandSeconds * (0.6f + 0.4f * Vol2Fs.Hash(Seed, k + 400))) continue;
                Vector3 outw = (p.P - Apex); outw.y = 0f; outw.Normalize();
                float hf = Vol2FsPolish.RingHeight(p.Ang);      // polish: tall on the far arc, lower on the sides, a low glowing lick behind the caster
                if (!p.Fired)
                {
                    p.Fired = true;
                    for (int j = 0; j < 3; j++) Vol2FsB1.EmitV(_fire, p.P + Random.insideUnitSphere * 0.1f, outw * 0.4f + Vector3.up * (1.2f + 1.4f * hf), Random.Range(0.28f, 0.5f) * hf, -1f);
                    if (hf > 0.93f) Vol2FsB1.EmitV(_tall, p.P, outw * 0.5f + Vector3.up * 2.2f, Random.Range(0.55f, 0.85f), -1f);
                    Vol2FsB1.Emit(_embers, p.P, 3);
                }
                else if (burnOut < 1f && (k + Time.frameCount) % (hf > 0.93f ? 3 : 4) == 0)
                {
                    Vol2FsB1.EmitV(_fire, p.P + Random.insideUnitSphere * 0.08f, outw * 0.35f + Vector3.up * (0.6f + 0.8f * hf), Random.Range(0.24f, 0.46f) * hf * (1f - 0.5f * burnOut), -1f);
                    if (hf > 0.93f && burnOut < 0.6f && (k + Time.frameCount) % 2 == 0) Vol2FsB1.EmitV(_tall, p.P, outw * 0.45f + Vector3.up * 1.6f, Random.Range(0.5f, 0.85f), -1f);
                    if (k % 3 == 0) Vol2FsB1.Emit(_embers, p.P, 1);
                    if (k % 9 == 0 && hf > 0.93f && Time.frameCount % 4 == 0) Vol2FsB1.Emit(_smoke, p.P + Vector3.up * 1.8f, 1);
                }
            }
            if (burnOut > 0.35f && !_ashed) { _ashed = true; for (int k = 0; k < _pts.Count; k += 2) { Vol2FsB1.Emit(_ash, _pts[k].P + Vector3.up * 0.5f, 3); if (k % 8 == 0) Vol2FsB1.Emit(_smoke, _pts[k].P + Vector3.up * 0.8f, 1); } }
            // the white-hot line and the scorched ring left behind
            _line.Clear(); _scorch.Clear();
            float ll = Vol2Fs.Smooth(0f, ExpandSeconds, a) * (1f - Vol2Fs.Smooth(ExpandSeconds + WallSeconds * 0.6f, ExpandSeconds + WallSeconds + 0.4f, a));
            float sl = Vol2Fs.Smooth(ExpandSeconds, ExpandSeconds + 0.3f, a) * (1f - Vol2Fs.Smooth(ExpandSeconds + WallSeconds + 0.5f, ExpandSeconds + WallSeconds + 1.6f, a));
            float rr = Mathf.Lerp(0.4f, R, Mathf.Clamp01(a / ExpandSeconds));
            if (ll > 0.01f) Vol2FsB2.GroundRing(_line, Apex, F, Ground, rr - 0.32f, rr + 0.32f, 72, x => ll, 0.11f, cam, 0.04f, 0f, 5f);
            if (sl > 0.01f) Vol2FsB2.GroundRing(_scorch, Apex, F, Ground, Inner - 0.35f, Outer + 0.45f, 72, x => sl, 0.71f, cam, 0.025f, 0f, 4f);
            _line.Write(_lineMesh); _scorch.Write(_scorchMesh);
            // twisting ink plumes rising off the wall's crest (front and sides)
            _plume.Clear();
            foreach (var p in _plumes)
            {
                float s = a - p.At; if (s <= 0f) continue;
                float grow = Vol2Fs.Smooth(0f, 0.3f, s), life = 1f - Vol2Fs.Smooth(0.45f, 1.15f, s);
                if (life <= 0.01f) continue;
                Vector3 b = OnBand(p.Ang, R) + Vector3.up * 0.4f, outw = Dir(p.Ang), side = Vector3.Cross(Vector3.up, outw);
                var pts = new Vector3[9]; var wd = new float[9]; var al = new float[9];
                for (int k = 0; k < 9; k++)
                {
                    float u = k / 8f * grow;
                    pts[k] = b + Vector3.up * p.H * u + outw * (0.5f * u * u) + side * Mathf.Sin(u * 4f + p.Twist + s * 3f) * 0.35f * u;
                    wd[k] = p.W * (0.4f + 0.8f * Mathf.Sin(Mathf.Min(1f, u * 1.3f + 0.15f) * Mathf.PI)) * (1f - 0.5f * u);
                    al[k] = life * (1f - 0.5f * u);
                }
                _plume.Strip(pts, wd, null, Color.white, al, p.Rnd, cam, 0f, 1f);
            }
            _plume.Write(_plumeMesh);
            float li = a < ExpandSeconds ? 8f * a / ExpandSeconds : 8f * (1f - burnOut);
            for (int k = 0; k < _lights.Length; k++) _lights[k].intensity = li * (0.8f + 0.2f * Mathf.PerlinNoise(t * 8f, k));
            foreach (var s in Hits)
            {
                if (!s.Started && a >= s.At) StartBurn(s);
                if (s.Data is Burn b) { b.T.position = CentreNow(s); b.L.intensity = s.Ended ? Mathf.Max(0f, b.L.intensity - Time.deltaTime * 6f) : 3f + 1.5f * Mathf.PerlinNoise(t * 9f, s.Id); }
            }
        }
    }
}
