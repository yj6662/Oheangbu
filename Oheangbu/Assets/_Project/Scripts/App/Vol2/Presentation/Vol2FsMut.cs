using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>뭇 (earth, ㅜ ring, 가른다) - free showcase batch 6, concept mut_b (a thin standing ring of whirling compressed sand and black ink grinds round the caster
    /// like a blade; the crossing bear's armour is ground off and it is thrown back; the wall on the ring cut in two with amber-lit faces). Executor facts only:
    /// the line's launch = the sand band whips up round the band from the front (the base's race) over an amber-lit gouge; the 10 s line stands as a knee-high
    /// grinding whirl (sand sheets and three ink strokes racing round, grit orbiting; the template's warning: the whirl stutters and sheds dust); every Instant
    /// (the bear on the band at the launch, the walker at 1.0 s) = a burst of grit and ink thrown out where it crossed (the shared push shoves it back); the
    /// emitter's EndUs = the whirl slows and sinks and the ground round the ring lifts in plates and settles (CSV 끝: 가라앉으며 둘레 땅이 판으로 들림).</summary>
    public sealed class Vol2FsMut : Vol2FsCutRing
    {
        static readonly Color Sand = new Color(0.86f, 0.66f, 0.42f), Amber = new Color(0.95f, 0.66f, 0.3f);
        ParticleSystem _grit, _dust, _orbit;
        Mesh _sandMesh, _inkMesh, _glowMesh; readonly Vol2FsStrips _sand = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _glowS = new Vol2FsStrips();
        Material _stone;
        sealed class Plate { public Transform T; public float Ang, Delay, Tilt; public Vector3 Pos; }
        readonly List<Plate> _plates = new List<Plate>();
        float _phase, _lastT = -1f;
        protected override Color CutGlow => new Color(1.5f, 0.95f, 0.45f);
        protected override float GougeWidth => 0.24f;
        protected override float EndSeconds => 1.4f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _stone = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.58f, 0.48f, 0.4f), 0f, 0.12f);
            _grit = Vol2Fs.Build(new Vol2FsPs { Mat = _stone, Mesh = Kit.SharedMesh("mutGrit", () => Vol2FsB1.Rock(13, 0.5f)), Life0 = 0.6f, Life1 = 1.1f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.04f, Size1 = 0.12f, Gravity = 2.2f, Drag = 0.3f, Spin = 600f, Max = 500 }, root, "Grit");
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.55f, 0.4f, 0.28f, 1f), 0.5f)), Life0 = 0.6f, Life1 = 1.3f, Speed0 = 0.3f, Speed1 = 1.5f, Size0 = 0.3f, Size1 = 0.7f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.6f, Drag = 1.3f, Max = 400 }, root, "Dust");
            _orbit = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Sand, 1.6f, 0.4f)), Life0 = 0.35f, Life1 = 0.7f, Speed0 = 0f, Speed1 = 0.2f, Size0 = 0.02f, Size1 = 0.05f, Drag = 0.2f, Stretch = true, StretchVel = 0.04f, StretchLen = 1.2f, Max = 900 }, root, "Orbit");
            foreach (var ps in new[] { _grit, _dust, _orbit }) ps.Play(false);
            var sand = M("fs_puff", Sand, 1.9f, 0.4f); sand.NoiseAmt = 0.55f; sand.NoiseScale = 1.6f; sand.Erode = 0.3f; sand.Soft = 0.25f; sand.Scroll = new Vector2(-2.6f, 0f);
            Vol2Fs.MeshObject(root, "SandWhirl", Kit.Mat(sand), out _sandMesh);
            Vol2Fs.MeshObject(root, "InkWhirl", Kit.InkSolid("fs_stroke2", InkC, Amber * 1.5f, 0.14f, 0.45f, 0.3f, 1.3f), out _inkMesh);
            var gl = M("fs_streak", Amber, 3.2f, 1f); gl.NoiseAmt = 0.3f; gl.Soft = 0.04f; gl.Scroll = new Vector2(-3f, 0f);
            Vol2Fs.MeshObject(root, "WhirlEdge", Kit.Mat(gl), out _glowMesh);
        }

        float BandH(float ang) => 0.56f * Mathf.Lerp(0.35f, 1f, Front(ang));

        /// <summary>A vertical band round the ring (pts at mid height, side = up): heights per angle, revealed by the race, ×<paramref name="k"/>.</summary>
        void Band(Vol2FsStrips s, float r, float lift, float hk, float k, float t, float rnd, Vector3 cam, float u1)
        {
            int n = 96; var pts = new List<Vector3>(n + 1); var wd = new List<float>(n + 1); var al = new List<float>(n + 1); var sd = new List<Vector3>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                float ang = -Mathf.PI + i / (float)n * Mathf.PI * 2f, h = BandH(ang) * hk * Reached(ang, t);
                pts.Add(OnBand(ang, r) + Vector3.up * (lift + h * 0.5f)); wd.Add(h); al.Add(k * (0.55f + 0.45f * Mathf.Sin(ang * 7f + _phase * 3f) * 0.5f + 0.225f)); sd.Add(Vector3.up);
            }
            s.Strip(pts, wd, sd, Color.white, al, rnd, cam, 0f, u1);
        }

        protected override void Cut(float t)
        {
            for (int k = 0; k < 30; k++) { float a = Random.Range(-Mathf.PI, Mathf.PI); if (Front(a) < 0.3f) continue; Vol2FsB1.EmitV(_dust, OnBand(a, R) + Vector3.up * 0.2f, Tangent(a) * 1.5f + Vector3.up * 0.6f, -1f, -1f); }
        }

        protected override void Crossed(Struck s, float ang)
        {
            var b = s.T != null ? BodyBounds(s.T, s.T.position, 0.5f) : s.B;
            Vector3 at = OnBand(ang, R) + Vector3.up * 0.3f, d = Dir(ang);
            for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_grit, at, (d * Random.Range(1.5f, 4f) + Tangent(ang) * Random.Range(-3f, 3f) + Vector3.up * Random.Range(1.5f, 4f)), -1f, -1f);
            for (int k = 0; k < 14; k++) Vol2FsB1.EmitV(_dust, at + Random.insideUnitSphere * 0.3f, d * 1.5f + Vector3.up * 0.7f + Random.insideUnitSphere * 0.5f, -1f, -1f);
            for (int k = 0; k < 14; k++) Vol2FsB1.EmitV(PInk, b.center, (Random.onUnitSphere + d * 0.6f).normalized * Random.Range(3f, 7f), -1f, -1f);
        }

        protected override void Ended()
        {
            if (_plates.Count > 0) return;
            for (int k = 0; k < 18; k++)
            {
                float ang = -Mathf.PI + (k + 0.5f) / 18f * Mathf.PI * 2f;
                if (Front(ang) < 0.3f) continue;
                float sz = 0.45f + 0.25f * Vol2Fs.Hash(Seed, k);
                var r = Vol2FsB1.Prop(Root.transform, "Plate" + k, Kit.SharedMesh("mutPlate" + (k % 3), () => Vol2FsB1.Slab(41 + k % 3, new Vector3(0.7f, 0.12f, 0.5f), 0.9f)), _stone);
                var p = new Plate { T = r.transform, Ang = ang, Delay = Mathf.Abs(Mathf.DeltaAngle(0f, ang * Mathf.Rad2Deg)) / 180f * 0.35f, Tilt = 20f + 18f * Vol2Fs.Hash(Seed, k + 9), Pos = OnBand(ang, R + 0.32f + 0.1f * Vol2Fs.Hash(Seed, k + 3)) };
                p.T.localScale = Vector3.one * sz; p.T.gameObject.SetActive(false);
                _plates.Add(p);
            }
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos;
            float dt = _lastT < 0f ? 0f : Mathf.Clamp(t - _lastT, 0f, 0.1f); _lastT = t;
            float stand = Standing(t), warn = LineWarn(t), end = EndK(t), flick = Flick(t);
            float speed = (1f - Vol2Fs.Smooth(0f, 0.6f, end)) * (1f - 0.5f * warn * (Mathf.Sin(t * 30f) > 0f ? 1f : 0f));
            _phase += dt * 2.6f * speed;
            _sand.Clear(); _ink.Clear(); _glowS.Clear();
            if (stand > 0.01f)
            {
                float sink = 1f - Vol2Fs.Smooth(0f, 0.7f, end);
                Band(_sand, R - 0.08f, 0.02f, 1f * sink, stand * 0.75f, t, 0.27f, cam, 8f);
                Band(_sand, R + 0.1f, 0.0f, 0.75f * sink, stand * 0.6f, t, 0.69f, cam, 6f);
                Band(_glowS, R, 0.0f, 0.3f * sink, stand * flick, t, 0.43f, cam, 10f);
                Band(_glowS, R - 0.02f, BandH(0f) * 0.55f * sink, 0.08f * sink, stand * flick * 0.7f, t, 0.91f, cam, 12f);
                // three ink strokes racing round the ring (the grinding edge)
                for (int k = 0; k < 3; k++)
                {
                    float c = _phase + k * Mathf.PI * 2f / 3f, span = 1.2f;
                    int n = 20; var pts = new Vector3[n]; var wd = new float[n]; var al = new float[n]; var sd = new Vector3[n];
                    for (int i = 0; i < n; i++)
                    {
                        float u = i / (float)(n - 1), ang = c - span * (1f - u), hh = BandH(ang) * sink * Reached(ang, t);
                        pts[i] = OnBand(ang, R + 0.04f) + Vector3.up * (0.06f + hh * 0.55f); sd[i] = Vector3.up;
                        wd[i] = hh * 0.85f * Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI * 0.9f + 0.1f); al[i] = stand * (0.3f + 0.7f * u);
                    }
                    _ink.Strip(pts, wd, sd, Color.white, al, 0.2f + 0.3f * k, cam, 0f, 2f);
                }
                if (Time.frameCount % 2 == 0)
                    for (int i = 0; i < 4; i++)
                    {
                        float a = Random.Range(-Mathf.PI, Mathf.PI); if (Random.value > Front(a) * 0.85f + 0.15f || Reached(a, t) < 0.5f) continue;
                        Vol2FsB1.EmitV(_orbit, OnBand(a, R) + Vector3.up * Random.Range(0.05f, BandH(a)), Tangent(a) * 4.5f * speed + Vector3.up * 0.3f, -1f, -1f);
                    }
                if (warn > 0f && Time.frameCount % 3 == 0) { float a = Random.Range(-1.8f, 1.8f); Vol2FsB1.EmitV(_dust, OnBand(a, R) + Vector3.up * 0.2f, Vector3.up * 0.5f + Dir(a) * 0.5f, -1f, -1f); }
            }
            _sand.Write(_sandMesh); _ink.Write(_inkMesh); _glowS.Write(_glowMesh);
            // the end clause: plates of ground lift round the ring and settle
            foreach (var p in _plates)
            {
                float a = (t - LineEndedAt) - p.Delay;
                if (a < 0f) { p.T.gameObject.SetActive(false); continue; }
                float lift = Vol2Fs.Smooth(0f, 0.25f, a) * (1f - Vol2Fs.Smooth(0.45f, 1.1f, a));
                bool on = a < 1.25f;
                p.T.gameObject.SetActive(on);
                if (!on) continue;
                Vector3 d = Dir(p.Ang);
                p.T.SetPositionAndRotation(p.Pos + Vector3.up * (0.05f * lift - 0.06f * Vol2Fs.Smooth(0.9f, 1.25f, a)), Quaternion.AngleAxis(-p.Tilt * lift, Tangent(p.Ang)) * Quaternion.LookRotation(d, Vector3.up));
                if (a < 0.08f && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_dust, p.Pos + Vector3.up * 0.1f, Vector3.up * 0.6f + d * 0.4f, -1f, -1f);
            }
        }
    }
}
