using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>멈 (earth, ㅓ attachment, 심는다 - the PEAK: the stagger exemption) - free showcase batch 5, concept meom_b (the caster's footing is seeded with stone;
    /// at the hit stone roots lock the feet into the ground so the caster does not stagger, and a ring of jagged earth spikes bursts out all round, shoving the
    /// bears away, glowing cracks running under them). Unlike 머 (one slab wall in front on a parry) and 무 (a rolling mound) it is a 360-degree crown of spikes
    /// that answers a blow the caster TAKES. Executor facts only: the start = a stone seed sets into the sleeve -> the wait (its ember seams breathe on the
    /// heartbeat, grit trickles off it, the ink dial and the 2 m ground arc burn down; the last 0.4 s it grinds) -> Consumed (the player really lost HP) = stone
    /// roots and a stone collar grip the caster's feet (held longer and brighter when the executor granted that hit's stagger exemption - its "Exempt" fact),
    /// the spike crown tears up from the inside out to the reach (dust, chunks, low heavy ink), every struck bear (OnStrike) takes a dust blast and shove
    /// streaks (the push is the executor's), then the spikes sink while the earth outside the ring lifts as plates and settles (CSV 끝: 가라앉으며 둘레 땅이
    /// 판으로 들림); Expired = the seed crumbles and the ground round the feet lifts a little and settles.</summary>
    public sealed class Vol2FsMeom : Vol2FsSeedWear
    {
        static readonly Color DustC = new Color(0.55f, 0.42f, 0.34f, 1f), Seam = new Color(1f, 0.5f, 0.18f);
        const float StandSeconds = 1.1f, SinkSeconds = 0.9f;
        ParticleSystem _chunks, _grit, _cloud, _ringDust, _speed;
        Material _stone, _spikeMat, _plateMat;
        Transform _seed; Light _seam, _blastLight;
        Mesh _crackMesh, _inkMesh; readonly Vol2FsStrips _crackS = new Vol2FsStrips(), _inkS = new Vol2FsStrips(); List<Vol2FsB1.Stroke> _ink;
        sealed class Spike { public Transform T; public Vector3 Base; public Quaternion Rot; public float H, At, W; }
        readonly List<Spike> _spikes = new List<Spike>();
        readonly List<(Transform T, Vector3 Base, Quaternion Rot, float Up)> _collar = new List<(Transform, Vector3, Quaternion, float)>();
        readonly List<(Transform T, Vector3 Base, Quaternion Rot)> _plates = new List<(Transform, Vector3, Quaternion)>();
        bool _exempt;
        protected override float SeedU => 0.08f;
        protected override float FireSeconds => StandSeconds + SinkSeconds + 1.3f;
        protected override float ExpireSeconds => 1.3f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _stone = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.62f, 0.52f, 0.44f), 0f, 0.1f, Seam * 0.9f, "fs_vein");
            _spikeMat = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.9f, 0.78f, 0.64f), 0f, 0.1f, Seam * 0.9f, "fs_vein");
            _plateMat = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.62f, 0.5f, 0.42f), 0f, 0.1f);
            var chunk = Kit.SharedMesh("chunk0", () => Vol2FsB1.Rock(23, 0.5f, 0.7f, 7, 5));
            _chunks = Vol2Fs.Build(new Vol2FsPs { Mat = _plateMat, Mesh = chunk, Life0 = 0.9f, Life1 = 1.6f, Speed0 = 2.5f, Speed1 = 7f, Size0 = 0.08f, Size1 = 0.28f, Radius = 0.3f, Gravity = 2.2f, Drag = 0.3f, Spin = 500f, Max = 400 }, root, "Chunks");
            _grit = Vol2Fs.Build(new Vol2FsPs { Mat = _plateMat, Mesh = chunk, Life0 = 0.5f, Life1 = 1f, Speed0 = 0.05f, Speed1 = 0.3f, Size0 = 0.015f, Size1 = 0.04f, Radius = 0.05f, Gravity = 1.8f, Spin = 400f, Max = 300 }, root, "Grit");
            _cloud = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(DustC, 0.6f)), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 1f, Speed1 = 3.5f, Size0 = 0.9f, Size1 = 2f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.5f, Drag = 2f, Tiles = 2, SizeStart = 0.4f, SizePeak = 1.2f, SizeEnd = 1.7f, Max = 200, SortFudge = 10 }, root, "Cloud");
            _ringDust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(DustC, 0.5f)), Life0 = 0.7f, Life1 = 1.2f, Speed0 = 4f, Speed1 = 8f, Size0 = 0.5f, Size1 = 1.1f, Drag = 3f, Tiles = 2, SizeEnd = 2f, Max = 300, SortFudge = 8 }, root, "RingDust");
            var streak = M("fs_streak", DustC, 1f, 0f); streak.Opacity = 0.9f; streak.NoiseAmt = 0.4f;
            _speed = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(streak), Life0 = 0.3f, Life1 = 0.55f, Speed0 = 8f, Speed1 = 14f, Size0 = 0.2f, Size1 = 0.45f, Radius = 0.4f, Drag = 2f, Stretch = true, StretchVel = 0.09f, StretchLen = 2f, Max = 120 }, root, "Shove");
            foreach (var ps in new[] { _chunks, _grit, _cloud, _ringDust, _speed }) ps.Play(false);
            var cr = M("fs_crack", new Color(0.08f, 0.05f, 0.04f, 1f), 1f, 0f); cr.Soft = 0.001f; cr.NoiseAmt = 0.4f; cr.Erode = 0.3f; cr.Opacity = 0.9f; cr.Edge = Seam * 1.4f; cr.EdgeWidth = 0.2f;
            Vol2Fs.MeshObject(root, "Cracks", Kit.Mat(cr), out _crackMesh);
            Vol2Fs.MeshObject(root, "InkLow", Kit.InkSolid("fs_stroke2", InkC, new Color(0.9f, 0.55f, 0.25f, 1f), 0.08f, 0.45f, 0.3f, 1.3f), out _inkMesh);
            var r = Vol2FsB1.Prop(root, "Seed", Kit.SharedMesh("meomSeed", () => Vol2FsB1.Rock(131, 0.5f, 0.85f, 12, 8)), _stone, false);
            _seed = r.transform; _seed.localScale = Vector3.one * 0.01f; _seed.gameObject.SetActive(false);
            _seam = Vol2Fs.PointLight(root, "SeedSeam", Seam, 1.8f);
            _blastLight = Vol2Fs.PointLight(root, "Blast", Seam, 6f);
        }

        protected override void OnPlant(Pod p) { _seed.gameObject.SetActive(true); Vol2FsB1.Emit(_grit, SeedPoint(), 14); }

        protected override void Fired(Pod p)
        {
            var root = Root.transform; Vector3 feet = FireFeet;
            _seed.gameObject.SetActive(false); _seam.intensity = 0f;
            _blastLight.transform.position = feet + Vector3.up * 0.6f; _blastLight.intensity = 6f;
            int seed = Seed + 211;
            // the spike crown: two rings, inner first, leaning out (low on the short arc behind the caster)
            for (int ring = 0; ring < 2; ring++)
            {
                int n = ring == 0 ? 11 : 15;
                for (int k = 0; k < n; k++)
                {
                    float ang = Vol2FsB2.Angle(k, n, seed + ring * 50, 0.5f), front = Front(ang);
                    if (front < 0.12f) continue;
                    Vector3 o = Dir(ang);
                    float rad = ring == 0 ? Mathf.Lerp(0.9f, 1.2f, Vol2Fs.Hash(seed, k)) : Mathf.Lerp(1.6f, Reach, Vol2Fs.Hash(seed, k + 30));
                    var mesh = Kit.SharedMesh("meomSpike" + (k % 4), () => Vol2FsB1.Slab(700 + k % 4, new Vector3(1f, 1f, 0.7f), 0.15f));
                    var pr = Vol2FsB1.Prop(root, "Spike" + ring + "_" + k, mesh, _spikeMat);
                    var s = new Spike { T = pr.transform, Base = feet + o * rad + Vector3.down * 0.1f, H = Mathf.Lerp(ring == 0 ? 0.9f : 1.3f, ring == 0 ? 1.4f : 2.1f, Vol2Fs.Hash(seed, k + 60)) * Mathf.Lerp(0.45f, 1f, front), At = ring * 0.07f + 0.02f * Vol2Fs.Hash(seed, k + 90), W = Mathf.Lerp(0.45f, 0.75f, Vol2Fs.Hash(seed, k + 120)) };
                    s.Rot = Quaternion.LookRotation(Vector3.Cross(Vector3.up, o), Vector3.up) * Quaternion.AngleAxis(-(22f + 18f * Vol2Fs.Hash(seed, k + 150)), Vector3.forward);
                    // lean outward: tilt about the tangent so the tip goes away from the caster
                    s.Rot = Quaternion.AngleAxis(25f + 15f * Vol2Fs.Hash(seed, k + 180), Vector3.Cross(Vector3.up, o)) * Quaternion.LookRotation(o, Vector3.up);
                    s.T.SetPositionAndRotation(s.Base, s.Rot); s.T.localScale = new Vector3(s.W, 0.001f, s.W * 0.8f);
                    _spikes.Add(s);
                }
            }
            // the stone collar round the feet: chips that climb the legs (the rooted stance)
            for (int k = 0; k < 9; k++)
            {
                float ang = k / 9f * Mathf.PI * 2f; Vector3 o = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                var pr = Vol2FsB1.Prop(root, "Collar" + k, Kit.SharedMesh("meomChip" + (k % 3), () => Vol2FsB1.Slab(760 + k % 3, new Vector3(1f, 1f, 0.5f), 0.4f)), _stone);
                _collar.Add((pr.transform, feet + o * 0.28f, Quaternion.LookRotation(o, Vector3.up) * Quaternion.AngleAxis(-12f, Vector3.right), 0.35f + 0.25f * Vol2Fs.Hash(seed, k + 200)));
            }
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(CamPos - feet));
            _ink = Vol2FsB1.Burst(Seed + 13, 12, side, -FlatN(CamPos - feet), 1.4f, 3.0f, 0.28f, 0.5f, 0.12f, 0.15f);
            for (int k = 0; k < 90; k++) { float a = k / 90f * Mathf.PI * 2f; Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)); Vol2FsB1.EmitV(_ringDust, feet + d * 0.5f + Vector3.up * 0.15f, d * Random.Range(5f, 9f), -1f, -1f); }
            Vol2FsB1.Emit(_cloud, feet, 8);
        }

        protected override void OnStrike(Pod p, Struck s)
        {
            if (s.World) return;
            Vector3 c = CentreNow(s), away = FlatN(c - FireFeet);
            Vol2FsB1.Emit(_cloud, c, 3); Vol2FsB1.Emit(_chunks, c, 20);
            for (int k = 0; k < 14; k++) Vol2FsB1.EmitV(_speed, c + Random.insideUnitSphere * 0.4f, (away + Random.insideUnitSphere * 0.3f) * Random.Range(8f, 14f), -1f, -1f);
        }

        protected override void OnPodEnd(Pod p)
        {
            if (Worked(p)) return;
            var root = Root.transform; Vector3 feet = FeetNow();
            Vol2FsB1.Emit(_grit, SeedPoint(), Withered(p) ? 40 : 10);
            if (!Withered(p)) return;
            for (int k = 0; k < 7; k++)
            {
                float ang = Vol2FsB2.Angle(k, 7, Seed + 5, 0.4f); if (Front(ang) < 0.3f) continue;
                Vector3 o = Dir(ang);
                var pr = Vol2FsB1.Prop(root, "Plate" + k, Kit.SharedMesh("plate" + (k % 3), () => Vol2FsB1.Slab(400 + k % 3, new Vector3(1f, 0.22f, 0.8f), 0.9f)), _plateMat);
                pr.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
                _plates.Add((pr.transform, feet + o * 0.9f + Vector3.down * 0.2f, Quaternion.LookRotation(o, Vector3.up)));
            }
        }

        void PlatesOutside()
        {
            var root = Root.transform; Vector3 feet = FireFeet;
            for (int k = 0; k < 14; k++)
            {
                float ang = Vol2FsB2.Angle(k, 14, Seed + 7, 0.3f); if (Front(ang) < 0.2f) continue;
                Vector3 o = Dir(ang);
                var pr = Vol2FsB1.Prop(root, "OutPlate" + k, Kit.SharedMesh("plate" + (k % 3), () => Vol2FsB1.Slab(400 + k % 3, new Vector3(1f, 0.22f, 0.8f), 0.9f)), _plateMat);
                pr.transform.localScale = new Vector3(Mathf.Lerp(0.7f, 1.1f, Vol2Fs.Hash(Seed, k + 70)), 1f, 0.8f);
                _plates.Add((pr.transform, feet + o * (Reach + 0.8f) + Vector3.down * 0.2f, Quaternion.LookRotation(o, Vector3.up)));
            }
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos; var p = The;
            _crackS.Clear(); _inkS.Clear();
            if (p != null && !Worked(p))
            {
                float life = PodLife(p, t, 0.35f), beat = Beat(p), warn = PodWarn(p, t), ex = PodExit(p, t);
                bool on = p.Started && (!p.Ended || (Withered(p) && ex < 0.3f));
                _seed.gameObject.SetActive(on);
                if (on)
                {
                    Vector3 c = SeedPoint() + (warn > 0f ? Random.insideUnitSphere * 0.012f * warn : Vector3.zero);
                    _seed.SetPositionAndRotation(c, Quaternion.Euler(20f, t * 20f, 10f));
                    _seed.localScale = Vector3.one * Mathf.Max(0.01f, (Withered(p) ? 1f - ex * 3f : life) * (0.2f + 0.015f * beat));
                    _seam.transform.position = c; _seam.intensity = p.Ended ? 0f : life * (0.4f + 1.4f * beat + warn) * (1f - 0.6f * Held01);
                    if (!p.Ended && Time.frameCount % (warn > 0f ? 2 : 6) == 0) Vol2FsB1.EmitV(_grit, c, Random.insideUnitSphere * 0.2f, -1f, -1f);
                }
                if (_plates.Count > 0) MovePlates(Vol2Fs.Smooth(0f, 0.9f, ex), 0.12f);
                _crackS.Write(_crackMesh); _inkS.Write(_inkMesh);
                return;
            }
            if (p == null) { _crackS.Write(_crackMesh); _inkS.Write(_inkMesh); return; }
            float a = SinceFire(p, t);
            if (!_exempt && HasFact("Exempt")) _exempt = true;
            Vector3 feet = FireFeet;
            if (_ink != null) Vol2FsB1.DrawBurst(_inkS, _ink, feet + Vector3.up * 0.3f, a, cam, 0.12f, 0.08f);
            float sink = Vol2Fs.Smooth(StandSeconds, StandSeconds + SinkSeconds, a);
            foreach (var s in _spikes)
            {
                float sa = a - s.At, g = sa <= 0f ? 0f : Vol2Fs.EaseOutBack(sa / 0.14f);
                if (sa > 0f && sa < 0.04f) { Vol2FsB1.Emit(_chunks, s.Base + Vector3.up * 0.3f, 4); Vol2FsB1.Emit(_ringDust, s.Base, 2); }
                float hh = s.H * g;
                Vector3 shake = a < StandSeconds && a > 0.2f ? Random.insideUnitSphere * 0.01f : Vector3.zero;
                s.T.position = s.Base + shake + s.Rot * Vector3.down * hh * sink;
                s.T.localScale = new Vector3(s.W, Mathf.Max(0.001f, hh), s.W * 0.8f);
                s.T.gameObject.SetActive(hh > 0.002f && sink < 0.995f);
            }
            // the rooted stance: the collar climbs the legs, held longer when that hit's stagger was exempted
            float hold = _exempt ? 1.2f : 0.45f, up = Vol2Fs.Smooth(0f, 0.12f, a) * (1f - Vol2Fs.Smooth(hold, hold + 0.4f, a));
            Vector3 feetNow = FeetNow();
            foreach (var c in _collar)
            {
                c.T.SetPositionAndRotation(c.Base - FireFeet + feetNow + Vector3.down * 0.05f, c.Rot);
                c.T.localScale = new Vector3(0.22f, Mathf.Max(0.001f, c.Up * up), 0.14f);
                c.T.gameObject.SetActive(up > 0.01f);
            }
            if (up > 0.5f && Time.frameCount % 4 == 0) Vol2FsB1.Emit(_grit, feetNow + Vector3.up * 0.4f, 2);
            float cl = Vol2Fs.Smooth(0f, 0.15f, a) * (1f - Vol2Fs.Smooth(StandSeconds + SinkSeconds, StandSeconds + SinkSeconds + 0.6f, a));
            if (cl > 0.01f) Vol2FsB2.GroundRing(_crackS, feet, F, Ground, 0.15f, Reach + 0.3f, 64, ang => cl * Mathf.Lerp(0.3f, 1f, Front(ang)), 0.43f, cam, 0.02f, 0f, 6f);
            _blastLight.intensity = 6f * (1f - Vol2Fs.Smooth(0f, 0.6f, a)) + (_exempt ? 1.5f : 0.6f) * up;
            if (a > StandSeconds && _plates.Count == 0) PlatesOutside();
            if (_plates.Count > 0) MovePlates(Mathf.Clamp01((a - StandSeconds) / 1.3f), 0.2f);
            if (a > StandSeconds && a < StandSeconds + SinkSeconds && Time.frameCount % 3 == 0) Vol2FsB1.Emit(_cloud, feet + Dir(Random.Range(-2f, 2f)) * (Reach + 0.6f), 1);
            _crackS.Write(_crackMesh); _inkS.Write(_inkMesh);
        }

        void MovePlates(float k01, float lift)
        {
            float k = Mathf.Sin(Mathf.Clamp01(k01) * Mathf.PI);
            foreach (var pl in _plates)
            {
                Vector3 o = pl.Rot * Vector3.forward;
                pl.T.SetPositionAndRotation(pl.Base + Vector3.up * lift * k, Quaternion.AngleAxis(-26f * k, Vector3.Cross(Vector3.up, o)) * pl.Rot);
                pl.T.gameObject.SetActive(k01 < 0.98f);
            }
        }
    }
}
