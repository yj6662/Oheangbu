using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>눔 (fire, ㅜ ring, 심는다) - free showcase batch 5, concept num_a (a circle of smouldering coals of burning ink lies on the 2 m band, breathing
    /// orange; where the bear stepped over it the coals erupt one after another into fountains of fire leaning outward, black ink swirls sweeping round the
    /// circle, both bears on the band scorched). Unlike 누's standing wall of fire and 눅's torch pillars, nothing burns while it waits: only the coals glow.
    /// Executor facts only: the launch = the coals drop onto the band in a spray of sparks -> the wait (each coal's ember glow breathes on the heartbeat,
    /// a thread of smoke rises, the ink arc inside the band burns down; the last 0.4 s the coals spit sparks) -> Consumed = from the crossing body's angle the
    /// coals burst into leaning fire fountains racing round the ring (RaceDelay), ink-and-fire swirls sweep round with the race, embers, soot -> every struck
    /// body burns its 2 s (the burn is the status sink's; mirrored) then the flames die from the tips into ash rising (CSV 끝: 끝부터 타 재로 날림(재가 일어
    /// 위로)), the band left scorched; Expired = the coals grey over and crumble into ash rising.</summary>
    public sealed class Vol2FsNum : Vol2FsSeedRing
    {
        static readonly Color Ember = new Color(1f, 0.45f, 0.12f);
        const float BurnSeconds = 2f, FountainSeconds = 0.9f;
        ParticleSystem _licks, _jets, _embers, _soot, _ash, _glow, _smoke;
        Material _coal, _coalCold;
        Mesh _swirlMesh, _scorchMesh; readonly Vol2FsStrips _swirl = new Vol2FsStrips(), _scorch = new Vol2FsStrips();
        readonly List<Transform> _coals = new List<Transform>();
        readonly List<bool> _burst = new List<bool>();
        readonly List<(Struck S, float At)> _burning = new List<(Struck, float)>();
        List<Vol2FsB1.Stroke>[] _swirls;
        Light[] _lights;
        protected override int SeedCount => 26;
        protected override float RaceSeconds => 0.38f;
        protected override float FireSeconds => BurnSeconds + 1.2f;
        protected override float ExpireSeconds => 1.3f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _coal = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.22f, 0.18f, 0.16f), 0f, 0.1f, Ember * 1.6f, "fs_vein");
            _coalCold = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.45f, 0.43f, 0.41f), 0f, 0.1f);
            _licks = Vol2Fs.Build(Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick()), 0.25f, 0.55f, 0.35f, 0.7f, 1.6f, 700), root, "Licks");
            var jet = Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick(1.5f, 0.6f, 1.15f)), 0.35f, 0.75f, 0.45f, 0.8f, 1.2f, 900); jet.Noise = 0.4f;
            _jets = Vol2Fs.Build(jet, root, "FountainLicks");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Ember, 3.5f, 1f)), Life0 = 0.6f, Life1 = 1.4f, Speed0 = 1f, Speed1 = 6f, Size0 = 0.025f, Size1 = 0.06f, Radius = 0.15f, Drag = 1f, Gravity = -0.2f, Noise = 1.2f, NoiseFreq = 1.5f, Max = 900 }, root, "Embers");
            _soot = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsPolish.Soot(0.38f)), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 0.3f, Speed1 = 1.2f, Size0 = 0.6f, Size1 = 1.4f, Radius = 0.3f, Drag = 1.5f, Tiles = 2, VelOverLife = new Vector3(0f, 0.9f, 0f), SizeEnd = 1.8f, Max = 160 }, root, "Soot");
            var ash = M("fs_ink", new Color(0.5f, 0.47f, 0.44f, 1f), 1f, 0f); ash.NoiseAmt = 0.3f; ash.Edge = new Color(1.6f, 0.5f, 0.1f, 1f); ash.EdgeWidth = 0.25f; ash.Soft = 0.05f;
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ash), Life0 = 1f, Life1 = 2f, Speed0 = 0.2f, Speed1 = 0.8f, Size0 = 0.04f, Size1 = 0.1f, Radius = 0.3f, Tiles = 2, VelOverLife = new Vector3(0f, 0.9f, 0f), Noise = 0.8f, Spin = 200f, Max = 600 }, root, "Ash");
            _glow = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Ember, 2.4f, 1f)), Life0 = 0.1f, Life1 = 0.16f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.2f, Size1 = 0.3f, Max = 200 }, root, "CoalGlow");
            var wisp = M("fs_puff", new Color(0.12f, 0.1f, 0.09f, 1f), 1f, 0f); wisp.Opacity = 0.25f; wisp.NoiseAmt = 0.6f;
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(wisp), Life0 = 1f, Life1 = 1.6f, Speed0 = 0.1f, Speed1 = 0.3f, Size0 = 0.1f, Size1 = 0.2f, Radius = 0.03f, Tiles = 2, VelOverLife = new Vector3(0f, 0.6f, 0f), SizeEnd = 3f, Noise = 0.3f, Max = 200 }, root, "Wisps");
            foreach (var ps in new[] { _licks, _jets, _embers, _soot, _ash, _glow, _smoke }) ps.Play(false);
            Vol2Fs.MeshObject(root, "Swirls", Kit.InkSolid("fs_stroke2", InkC, new Color(1.8f, 0.55f, 0.12f, 1f), 0.16f, 0.5f, 0.3f, 1.2f), out _swirlMesh);
            var sc = M("fs_scorch", new Color(0.05f, 0.035f, 0.03f, 1f), 1f, 0f); sc.Opacity = 0.75f; sc.NoiseAmt = 0.4f; sc.Soft = 0.02f;
            Vol2Fs.MeshObject(root, "Scorch", Kit.Mat(sc), out _scorchMesh);
            _lights = new Light[3];
            for (int k = 0; k < 3; k++) _lights[k] = Vol2Fs.PointLight(root, "Ember" + k, Ember, 4f);
        }

        protected override void Planted(Pod p)
        {
            var root = Root.transform; int seed = Seed + 5;
            for (int k = 0; k < SeedAng.Count; k++)
            {
                float a = SeedAng[k];
                var r = Vol2FsB1.Prop(root, "Coal" + k, Kit.SharedMesh("chunk0", () => Vol2FsB1.Rock(23, 0.5f, 0.7f, 7, 5)), _coal, false);
                r.transform.SetPositionAndRotation(OnBand(a, R + (Vol2Fs.Hash(seed, k) - 0.5f) * 0.2f) + Vector3.up * 0.04f, Quaternion.Euler(Vol2Fs.Hash(seed, k + 3) * 360f, Vol2Fs.Hash(seed, k + 6) * 360f, 0f));
                r.transform.localScale = Vector3.one * 0.01f;
                _coals.Add(r.transform); _burst.Add(false);
                Vol2FsB1.Emit(_embers, r.transform.position, 3);
            }
            // the swirls: two ink-and-fire strokes per quarter of the ring, flung round with the race (built now, drawn at the trigger)
            _swirls = new List<Vol2FsB1.Stroke>[8];
        }

        protected override void Fired(Pod p)
        {
            for (int q = 0; q < _swirls.Length; q++)
            {
                float a = q / (float)_swirls.Length * Mathf.PI * 2f - Mathf.PI;
                Vector3 c = OnBand(a, R);
                _swirls[q] = Vol2FsB1.Burst(Seed + q * 13, 3, Tangent(a) * (Mathf.DeltaAngle(TrigAng * Mathf.Rad2Deg, a * Mathf.Rad2Deg) >= 0f ? 1f : -1f), Dir(a), 1.2f, 2.4f, 0.22f, 0.42f, 0.9f, 0.6f);
            }
        }

        protected override void Hit(Pod p, Struck s) { if (!s.World) _burning.Add((s, SinceFire(p, Now(NowUs)) + RaceDelay(AngleOf(FeetOf(s))))); }

        protected override void Live(float t, float h)
        {
            var cam = CamPos; var p = The;
            _swirl.Clear(); _scorch.Clear();
            if (p == null || !p.Started) return;
            float life = PodLife(p, t, 0.35f), beat = Beat(p), warn = PodWarn(p, t), ex = PodExit(p, t), s = SinceFire(p, t);
            bool worked = Worked(p), withered = Withered(p);
            float grow = Mathf.Clamp01(PodAge(p, t) / 0.3f);
            for (int k = 0; k < _coals.Count; k++)
            {
                float a = SeedAng[k], sc = SeedScale(a) * (0.13f + 0.015f * beat);
                var c = _coals[k];
                bool on = !p.Ended || withered && ex < 0.6f || worked && !_burst[k];
                if (worked && !_burst[k] && s >= RaceDelay(a))
                {
                    _burst[k] = true; on = false;
                    Vector3 b = c.position, o = Dir(a); float hgt = Vol2FsPolish.RingHeight(a) * (Front(a) < 0.25f ? 0.45f : 1f);
                    Vol2FsB1.Emit(_embers, b, 30); if (Front(a) > 0.3f) Vol2FsB1.Emit(_soot, b + Vector3.up * 0.6f, 1);
                }
                if (withered && k == 0 && ex > 0.05f && ex < 0.1f) foreach (var cc in _coals) Vol2FsB1.Emit(_ash, cc.position, 6);
                c.gameObject.SetActive(on);
                if (!on) continue;
                c.localScale = Vector3.one * Mathf.Max(0.01f, sc * grow * (withered ? 1f - 0.8f * Vol2Fs.Smooth(0.2f, 0.6f, ex) : 1f));
                if (withered && ex > 0.15f && c.GetComponent<MeshRenderer>().sharedMaterial != _coalCold) c.GetComponent<MeshRenderer>().sharedMaterial = _coalCold;
                if (!p.Ended && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_glow, c.position + Vector3.up * 0.06f + (warn > 0f ? Random.insideUnitSphere * 0.03f : Vector3.zero), Vector3.zero, (0.14f + 0.22f * beat + 0.15f * warn) * SeedScale(a) * life * (1f - 0.5f * Held01), 0.12f);
                if (!p.Ended && Time.frameCount % 23 == k % 23 && Front(a) > 0.3f) Vol2FsB1.Emit(_smoke, c.position + Vector3.up * 0.1f, 1);
                if (!p.Ended && warn > 0f && Time.frameCount % 4 == k % 4) Vol2FsB1.EmitV(_embers, c.position, Vector3.up * 2f + Random.insideUnitSphere, -1f, 0.5f);
            }
            if (!p.Ended) for (int k = 0; k < 3; k++) { _lights[k].transform.position = OnBand(-1.2f + 1.2f * k, R) + Vector3.up * 0.3f; _lights[k].intensity = life * (0.4f + 1.2f * beat + warn) * (1f - 0.6f * Held01); }
            else if (!worked) for (int k = 0; k < 3; k++) _lights[k].intensity = 0f;
            if (!worked) { _swirl.Write(_swirlMesh); _scorch.Write(_scorchMesh); return; }
            // the fountains: each burst coal throws leaning tongues for FountainSeconds, the swirls sweep round with the race
            for (int k = 0; k < _coals.Count; k++)
            {
                float a = SeedAng[k], since = s - RaceDelay(a);
                if (since < 0f || since > FountainSeconds) continue;
                float hgt = Vol2FsPolish.RingHeight(a) * (Front(a) < 0.25f ? 0.4f : 1f), k01 = 1f - since / FountainSeconds;
                Vector3 b = OnBand(a, R), o = Dir(a), tg = Tangent(a);
                if (Time.frameCount % 2 == k % 2)
                    for (int j = 0; j < 2; j++)
                        Vol2FsB1.EmitV(_jets, b + Random.insideUnitSphere * 0.1f, (Vector3.up * Random.Range(3.5f, 6f) + o * Random.Range(0.8f, 2f) + tg * Random.Range(-0.6f, 0.6f)) * hgt * (0.5f + 0.5f * k01), Random.Range(0.35f, 0.7f) * (0.6f + 0.4f * hgt), Random.Range(0.4f, 0.75f));
                if (Time.frameCount % 3 == 0) Vol2FsB1.EmitV(_embers, b, (Vector3.up * 4f + o * 1.5f) * hgt + Random.insideUnitSphere * 1.5f, -1f, -1f);
            }
            for (int q = 0; q < _swirls.Length; q++)
            {
                if (_swirls[q] == null) continue;
                float a = q / (float)_swirls.Length * Mathf.PI * 2f - Mathf.PI;
                if (Front(a) < 0.3f) continue;
                float since = s - RaceDelay(a);
                if (since > 0f) Vol2FsB1.DrawBurst(_swirl, _swirls[q], OnBand(a, R) + Vector3.up * 0.3f, since, cam, 0.15f, 0.12f);
            }
            for (int k = 0; k < 3; k++) _lights[k].intensity = 4f * (1f - Vol2Fs.Smooth(0.2f, 1.2f, s)) + 0.5f;
            // the scorched band (stays through the burn, fades with the look)
            float sl = Vol2Fs.Smooth(0f, 0.4f, s) * (1f - Vol2Fs.Smooth(FireSeconds - 0.6f, FireSeconds, s));
            if (sl > 0.01f) Vol2FsB2.GroundRing(_scorch, Apex, F, Ground, Inner - 0.2f, Outer + 0.3f, 72, a => sl * (s >= RaceDelay(a) ? 1f : 0f) * Mathf.Lerp(0.4f, 1f, Front(a)), 0.53f, cam, 0.015f, 0f, 5f);
            // the bodies on the band burn their 2 s, then ash from the tips
            foreach (var (st, at) in _burning)
            {
                float b = s - at; if (b < 0f) continue;
                var bb = st.T != null ? BodyBounds(st.T, st.T.position, 0.5f) : st.B;
                if (b < BurnSeconds && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_licks, bb.center + new Vector3(Random.Range(-bb.extents.x, bb.extents.x), Random.Range(-0.2f, bb.extents.y), Random.Range(-bb.extents.z, bb.extents.z)), Vector3.up * 0.8f, Random.Range(0.3f, 0.55f), Random.Range(0.35f, 0.6f));
                if (b > BurnSeconds - 0.3f && b < BurnSeconds + 1f && Time.frameCount % 2 == 0) Vol2FsB1.Emit(_ash, bb.center + Vector3.up * bb.extents.y * Random.Range(0.2f, 1f), 3);
                if (b < BurnSeconds && Time.frameCount % 9 == 0) Vol2FsB1.Emit(_soot, bb.max, 1);
            }
            _swirl.Write(_swirlMesh); _scorch.Write(_scorchMesh);
        }
    }
}
