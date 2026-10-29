using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>막 (earth, ㅏ path, 남긴다) - free showcase batch 3, concept mak_a (a jagged CRAG of dark rock tears up out of the ground where the strike hit,
    /// stone spurs branching upward like a petrified bush, dust pouring off its cracks, the bear shoved back off it). Unlike 마's rolling boulder it is a stone
    /// LANCE that flies and a crag that STAYS (it blocks shots and can be climbed - the rule's, not this look's). Beats, executor facts only: charge (grit pulled to
    /// the tip) -> the stone lance flies to the predicted first body -> the hit (dust, rock chunks, low heavy ink) -> the residue material appears = the crag tears up
    /// spur by spur and stands, grit trickling, for the material's life; a body drawing from it = a shudder and a dust puff -> the last 0.4 s = it trembles, pebbles
    /// fall -> natural expiry (CSV: 가라앉으며 둘레 땅이 판으로 들림) = it sinks into the ground while the earth round it lifts as plates and settles.</summary>
    public sealed class Vol2FsMak : Vol2FsLeave
    {
        static readonly Color Dust = new Color(0.55f, 0.42f, 0.34f, 1f), Soil = new Color(0.3f, 0.18f, 0.12f, 1f), Ink = new Color(0.03f, 0.025f, 0.02f);
        ParticleSystem _gather, _trailDust, _chunks, _bits, _cloud, _clods, _ring, _grit;
        Transform _lance; MeshRenderer _lanceR; Material _rock, _rockDark;
        Mesh _burstMesh, _crackMesh; readonly Vol2FsStrips _burstS = new Vol2FsStrips(), _crackS = new Vol2FsStrips();
        List<Vol2FsB1.Stroke> _burst;
        sealed class Spur { public Transform T; public Vector3 Base; public Quaternion Rot; public float H, At; }
        readonly List<Spur> _spurs = new List<Spur>();
        readonly List<Spur> _plates = new List<Spur>();
        Vector3 _base, _away, _side; bool _built, _exitFx; Light _light;
        protected override float LobHeight => 0.25f;
        protected override float ExpireSeconds => 1.3f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _rock = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.86f, 0.68f, 0.55f), 0f, 0.1f);
            _rockDark = Vol2FsB1.Lit(Kit, "fs_rock_crack", new Color(0.7f, 0.55f, 0.45f), 0f, 0.08f);      // r2: both lighter (r1 read as black obsidian)
            var chunk = Kit.SharedMesh("chunk0", () => Vol2FsB1.Rock(23, 0.5f, 0.7f, 7, 5));
            _lanceR = Vol2FsB1.Prop(root, "Lance", Kit.SharedMesh("lance3", () => Vol2FsB1.Rock(41, 0.5f, 0.85f, 12, 8)), _rockDark); _lance = _lanceR.transform;
            _lance.localScale = Vector3.one * 0.02f; _lance.position = Tip();
            _gather = Vol2Fs.Build(new Vol2FsPs { Mat = _rock, Mesh = chunk, Rate = 50f, Loop = true, Life0 = 0.2f, Life1 = 0.28f, Speed0 = -4f, Speed1 = -6f, Size0 = 0.05f, Size1 = 0.14f, Radius = 1.1f, World = false, Spin = 300f, Max = 50 }, _lance, "Gather");
            _gather.Play(false);
            _trailDust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(Dust, 0.5f)), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 0.3f, Speed1 = 1f, Size0 = 0.5f, Size1 = 1f, Radius = 0.2f, Drag = 1f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.2f, SizeEnd = 1.7f, Max = 160, SortFudge = 10 }, root, "TrailDust");
            _chunks = Vol2Fs.Build(new Vol2FsPs { Mat = _rock, Mesh = chunk, Life0 = 0.9f, Life1 = 1.6f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.1f, Size1 = 0.4f, Radius = 0.4f, Gravity = 2.2f, Drag = 0.3f, Spin = 500f, AlphaHoldTo = 0.9f, Max = 140 }, root, "Chunks");
            _bits = Vol2Fs.Build(new Vol2FsPs { Mat = _rock, Mesh = chunk, Life0 = 0.6f, Life1 = 1.2f, Speed0 = 0.2f, Speed1 = 1.2f, Size0 = 0.03f, Size1 = 0.09f, Radius = 0.2f, Gravity = 2.4f, Spin = 400f, Max = 260 }, root, "Grit");
            _cloud = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(Dust, 0.75f)), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 1f, Speed1 = 4f, Size0 = 1.4f, Size1 = 3f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.7f, Drag = 2f, Tiles = 2,
                SizeStart = 0.4f, SizePeak = 1.2f, SizePeakAt = 0.4f, SizeEnd = 1.6f, VelOverLife = new Vector3(0f, 0.4f, 0f), Max = 80, SortFudge = 10 }, root, "Cloud");
            var clod = M("fs_ink", Soil, 1f, 0f); clod.NoiseAmt = 0.4f; clod.Soft = 0.1f;
            _clods = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(clod), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.08f, Size1 = 0.26f, Radius = 0.4f, Gravity = 2.6f, Tiles = 2, AlphaHoldTo = 0.7f, Max = 400 }, root, "Clods");
            var ring = M("fs_ring", Dust, 1f, 0f); ring.Opacity = 0.8f; ring.NoiseAmt = 0.5f; ring.Soft = 0.02f;
            _ring = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ring), Life0 = 0.6f, Life1 = 0.6f, Speed0 = 0f, Speed1 = 0f, Size0 = 5.5f, Size1 = 5.5f, Radius = 0.01f, Horizontal = true, SizeStart = 0.1f, SizePeak = 0.85f, SizePeakAt = 0.5f, SizeEnd = 1f, AlphaHoldTo = 0.3f, Max = 4 }, root, "Ring");
            var grit = M("fs_puff", Dust, 1f, 0f); grit.Opacity = 0.35f; grit.NoiseAmt = 0.6f; grit.Erode = 0.4f;
            _grit = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(grit), Life0 = 1f, Life1 = 1.8f, Speed0 = 0f, Speed1 = 0.2f, Size0 = 0.3f, Size1 = 0.6f, Radius = 0.05f, Tiles = 2, VelOverLife = new Vector3(0f, -0.7f, 0f), SizeEnd = 2f, Max = 120 }, root, "DustFall");
            foreach (var ps in new[] { _trailDust, _chunks, _bits, _cloud, _clods, _ring, _grit }) ps.Play(false);
            Vol2Fs.MeshObject(root, "InkBurst", Kit.InkSolid("fs_stroke2", Ink, new Color(0.3f, 0.18f, 0.1f, 1f), 0.1f, 0.5f, 0.3f, 1.6f), out _burstMesh);
            var cr = M("fs_crack", new Color(0.08f, 0.05f, 0.04f, 1f), 1f, 0f);      // r2: fs_crack has alpha (fs_rock_crack drew a black square) cr.Soft = 0.001f; cr.NoiseAmt = 0.4f; cr.Erode = 0.3f; cr.Opacity = 0.9f;
            Vol2Fs.MeshObject(root, "Cracks", Kit.Mat(cr), out _crackMesh);
            _light = Vol2Fs.PointLight(root, "DustLight", new Color(1f, 0.75f, 0.55f), 5f);
        }

        Vector3 Hold() => Tip() + Fwd * 0.5f + Vector3.up * 0.25f;

        protected override void OnCharge(float t, float k)
        {
            _lance.position = Hold();
            float s = Mathf.Lerp(0.05f, 1f, Vol2Fs.Smooth(0f, 1f, k));
            _lance.localScale = new Vector3(0.45f * s, 0.45f * s, 1.3f * s);
            _lance.rotation = Quaternion.LookRotation(Fwd, Vector3.up) * Quaternion.AngleAxis(t * 400f, Vector3.forward);
        }

        protected override void OnLaunch(long nowUs)
        {
            _gather.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            From = _lance.position;
            _away = FlatN(HitPoint - From); _side = Vector3.Cross(Vector3.up, _away);
        }

        protected override void OnFly(float t, float f, float prev)
        {
            Vector3 p = Line(f), q = Line(Mathf.Min(1f, f + 0.05f));
            _lance.position = p;
            _lance.rotation = Quaternion.LookRotation((q - p).sqrMagnitude > 1e-6f ? (q - p).normalized : _away, Vector3.up) * Quaternion.AngleAxis(t * 900f, Vector3.forward);
            Vol2FsB1.Emit(_trailDust, p, 1); if (Time.frameCount % 2 == 0) Vol2FsB1.Emit(_bits, p, 2);
        }

        protected override void OnHit(long nowUs)
        {
            _lanceR.enabled = false;
            Vector3 at = HasTarget ? HitPoint : AimPoint;
            _away = FlatN(at - Inst.Origin); _side = Vector3.Cross(Vector3.up, _away);
            // low, heavy ink thrown forward and sideways (not 나's star burst)
            _burst = new List<Vol2FsB1.Stroke>();
            for (int k = 0; k < 9; k++)
            {
                float a = Mathf.Lerp(-1.25f, 1.25f, k / 8f) + 0.12f * (Vol2Fs.Hash(Seed, k) - 0.5f);
                Vector3 d = (_away * Mathf.Cos(a) + _side * Mathf.Sin(a)) + Vector3.up * (0.12f + 0.25f * Vol2Fs.Hash(Seed, k + 5));
                _burst.Add(new Vol2FsB1.Stroke { Dir = d.normalized, Len = 1.3f + 1.5f * Vol2Fs.Hash(Seed, k + 9), Width = 0.3f + 0.25f * Vol2Fs.Hash(Seed, k + 13), Delay = 0.02f * Vol2Fs.Hash(Seed, k + 17), Life = 0.9f + 0.4f * Vol2Fs.Hash(Seed, k + 21), Rnd = Vol2Fs.Hash(Seed, k + 25) });
            }
            Vol2FsB1.Emit(_chunks, at, 30); Vol2FsB1.Emit(_clods, at, 60); Vol2FsB1.Emit(_cloud, new Vector3(at.x, Ground + 0.2f, at.z), 6);
            Vol2FsB1.Emit(_ring, new Vector3(at.x, Ground + 0.05f, at.z), 1);
            _light.transform.position = at + Vector3.up * 0.4f; _light.intensity = 6f;
        }

        protected override void OnResidue() { Build(); }

        void Build()
        {
            if (_built) return;
            _built = true;
            _base = ResCentre; _base.y = Ground;
            var root = Root.transform;
            int seed = Seed + 31;
            // 12 jagged spurs: the tallest in the middle (2.3 m), leaning outward, a petrified bush of stone
            for (int k = 0; k < 17; k++)
            {
                float a = (k / 17f + 0.07f * Vol2Fs.Hash(seed, k)) * Mathf.PI * 2f;
                float ring = k == 0 ? 0f : Mathf.Lerp(0.25f, 1.15f, Vol2Fs.Hash(seed, k + 10));
                Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float h = k == 0 ? 2.8f : Mathf.Lerp(1.0f, 2.4f, Vol2Fs.Hash(seed, k + 20)) * (1f - 0.3f * ring);
                float w = Mathf.Lerp(0.45f, 0.75f, Vol2Fs.Hash(seed, k + 30)) * (k == 0 ? 1.35f : 1f);
                var mesh = Kit.SharedMesh("spur" + (k % 5), () => Vol2FsB1.Slab(300 + k % 5, new Vector3(1f, 1f, 0.8f), 0.18f));
                var r = Vol2FsB1.Prop(root, "Spur" + k, mesh, k % 3 == 0 ? _rockDark : _rock);
                var s = new Spur { T = r.transform, Base = _base + o * ring + Vector3.down * 0.1f, H = h, At = 0.03f * k };
                Vector3 lean = (Vector3.up + o * Mathf.Lerp(0.15f, 0.55f, ring) + _side * 0.1f * (Vol2Fs.Hash(seed, k + 40) - 0.5f)).normalized;
                s.Rot = Quaternion.FromToRotation(Vector3.up, lean) * Quaternion.AngleAxis(Vol2Fs.Hash(seed, k + 50) * 360f, Vector3.up);
                s.T.localScale = new Vector3(w, 0.001f, w * 0.85f);
                s.T.SetPositionAndRotation(s.Base, s.Rot);
                _spurs.Add(s);
            }
            // the ground plates round it (lifted only on the natural end)
            for (int k = 0; k < 10; k++)
            {
                float a = (k / 10f) * Mathf.PI * 2f + 0.3f * Vol2Fs.Hash(seed, k + 60);
                Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var r = Vol2FsB1.Prop(root, "Plate" + k, Kit.SharedMesh("plate" + (k % 3), () => Vol2FsB1.Slab(400 + k % 3, new Vector3(1f, 0.22f, 0.8f), 0.9f)), _rock);
                var p = new Spur { T = r.transform, Base = _base + o * 1.5f + Vector3.down * 0.2f, Rot = Quaternion.LookRotation(o, Vector3.up) };
                p.T.localScale = new Vector3(Mathf.Lerp(0.7f, 1.1f, Vol2Fs.Hash(seed, k + 70)), 1f, 0.8f);
                p.T.SetPositionAndRotation(p.Base, p.Rot);
                r.enabled = false;
                _plates.Add(p);
            }
            Vol2FsB1.Emit(_chunks, _base + Vector3.up * 0.5f, 24); Vol2FsB1.Emit(_cloud, _base + Vector3.up * 0.2f, 5); Vol2FsB1.Emit(_clods, _base + Vector3.up * 0.3f, 40);
        }

        protected override void OnTouch(Struck s) { Vol2FsB1.Emit(_cloud, _base + Vector3.up * 0.3f, 3); Vol2FsB1.Emit(_bits, _base + Vector3.up * 1.6f, 20); }
        protected override void OnResidueEnd()
        {
            if (NaturalEnd) { Vol2FsB1.Emit(_cloud, _base + Vector3.up * 0.2f, 6); foreach (var p in _plates) p.T.GetComponent<MeshRenderer>().enabled = true; }
            else { Vol2FsB1.Emit(_chunks, _base + Vector3.up * 0.8f, 30); Vol2FsB1.Emit(_cloud, _base + Vector3.up * 0.5f, 5); }
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            _burstS.Clear();
            if (_burst != null) Vol2FsB1.DrawBurst(_burstS, _burst, HitPoint + Vector3.down * 0.3f, h, cam, 0.25f, 0.1f);
            _burstS.Write(_burstMesh);
            _light.intensity = Mathf.Max(0f, 6f * (1f - h / 0.4f));
            if (!_built) return;
            float ra = ResAge(t), warn = Warn(NowUs), ex = ExitK(t), pulse = DrawPulse(t);
            // the spurs tear up (0.25 s each, staggered), tremble before the expiry, sink on the end (or crumble on a consume)
            float sink = NaturalEnd ? Vol2Fs.Smooth(0f, 0.85f, ex) : Vol2Fs.Smooth(0f, 0.6f, ex);
            foreach (var s in _spurs)
            {
                float g = ra < 0f ? 0f : Vol2Fs.EaseOutBack((ra - s.At) / 0.25f);
                Vector3 shake = Vector3.zero;
                if (warn > 0f) shake += new Vector3(Mathf.PerlinNoise(t * 37f, s.H) - 0.5f, 0f, Mathf.PerlinNoise(s.H, t * 41f) - 0.5f) * 0.09f * warn;
                if (pulse > 0f) shake += new Vector3(Mathf.Sin(t * 70f + s.H), 0f, Mathf.Cos(t * 63f)) * 0.04f * pulse;
                float hh = s.H * Mathf.Max(0.001f, g) * (NaturalEnd ? 1f : 1f - sink);
                s.T.localScale = new Vector3(s.T.localScale.x, Mathf.Max(0.001f, hh), s.T.localScale.z);
                s.T.position = s.Base + shake + Vector3.down * s.H * sink * (NaturalEnd ? 1.05f : 0.3f);
            }
            // idle: grit trickles down the spurs, dust hangs at the foot; more pebbles fall in the warning
            if (ra > 0.4f && !Exiting)
            {
                if (Time.frameCount % (warn > 0f ? 2 : 9) == 0) { var s = _spurs[Random.Range(0, _spurs.Count)]; Vol2FsB1.Emit(_bits, s.Base + s.Rot * Vector3.up * s.H * 0.9f, warn > 0f ? 3 : 1); }
                if (Time.frameCount % 14 == 0) Vol2FsB1.Emit(_grit, _base + Vector3.up * (1.2f + Random.value), 1);
            }
            // the natural end: the earth round it lifts as plates and settles (nothing falls)
            if (Exiting && NaturalEnd)
            {
                if (!_exitFx) { _exitFx = true; Vol2FsB1.Emit(_ring, _base + Vector3.up * 0.05f, 1); }
                float lift = Mathf.Sin(Mathf.Clamp01(ex / 0.85f) * Mathf.PI);
                for (int k = 0; k < _plates.Count; k++)
                {
                    var p = _plates[k];
                    Vector3 o = p.Rot * Vector3.forward;
                    float tilt = 26f * lift * (0.8f + 0.4f * Vol2Fs.Hash(Seed, k + 90));
                    p.T.SetPositionAndRotation(p.Base + Vector3.up * (0.18f * lift), Quaternion.AngleAxis(-tilt, Vector3.Cross(Vector3.up, o)) * p.Rot);
                    if (ex > 0.97f) p.T.GetComponent<MeshRenderer>().enabled = false;
                }
                if (Time.frameCount % 3 == 0 && ex < 0.8f) Vol2FsB1.Emit(_cloud, _base + Random.insideUnitSphere * 1.2f + Vector3.up * 0.1f, 1);
            }
            if (ex >= 0.999f) foreach (var s in _spurs) s.T.gameObject.SetActive(false);
            // cracks round the foot
            _crackS.Clear();
            float cl = Vol2Fs.Smooth(0f, 0.3f, Mathf.Max(0f, ra)) * (1f - Vol2Fs.Smooth(0.6f, 1f, ex));
            if (cl > 0.01f && ra >= 0f) _crackS.Quad(new Vector3(_base.x, Ground + 0.02f, _base.z), Right, Fwd, 3.6f + 0.6f * warn, new Color(1f, 1f, 1f, cl), 0.29f);
            _crackS.Write(_crackMesh);
        }
    }
}
