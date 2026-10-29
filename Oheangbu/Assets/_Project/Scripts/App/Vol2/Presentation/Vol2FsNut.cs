using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>눗 (fire, ㅜ ring, 가른다) - free showcase batch 6, concept nut_b (a perfect circle burned into the ground as a molten trench, and a thin white-hot wire of
    /// flame hanging knee-high round the caster above it; the wall on the ring cut by the line in glowing halves, the bear crossing it flaring up, its armour
    /// bursting off in burning shards; black ink swept round in calligraphic arcs). Executor facts only: the line's launch = the molten ring (the base gouge in
    /// fire colour) and the white-hot wire race round from the front, two ink arcs fling out round the ring; the 10 s line stands (flames lick up out of the
    /// trench, the wire hums; the template's warning: the wire sputters and spits sparks); every Instant (the bear on the band at the launch, the walker at
    /// 1.0 s) = the wire flares where it crossed - a horizontal molten slash across the body, a fountain of sparks and fire; the emitter's EndUs = the wire burns
    /// away from its ends inward into ash rising (CSV 끝: 끝부터 타 재로 날림) and the trench cools dark.</summary>
    public sealed class Vol2FsNut : Vol2FsCutRing
    {
        static readonly Color Hot = new Color(1f, 0.55f, 0.18f);
        ParticleSystem _flame, _sparks, _ash, _smoke;
        Mesh _coreMesh, _haloMesh, _inkMesh, _flareMesh;
        readonly Vol2FsStrips _core = new Vol2FsStrips(), _halo = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _flare = new Vol2FsStrips();
        readonly List<(float Ang, float At, Vector3 C, float W)> _flares = new List<(float, float, Vector3, float)>();
        Light _light;
        protected override Color CutGlow => new Color(1.9f, 0.75f, 0.2f);
        protected override float GougeWidth => 0.26f;
        protected override float EndSeconds => 1.1f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _flame = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.7f, 0.9f)), Life0 = 0.3f, Life1 = 0.6f, Speed0 = 0.4f, Speed1 = 1.3f, Size0 = 0.16f, Size1 = 0.34f, Gravity = -0.7f, Noise = 0.8f, NoiseFreq = 1.5f, SizeStart = 0.5f, SizePeak = 1f, SizeEnd = 0.3f, Max = 900 }, root, "Flame");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.7f, 0.3f), 5f, 1f)), Life0 = 0.25f, Life1 = 0.7f, Speed0 = 2f, Speed1 = 9f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.4f, Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.4f, Max = 1200 }, root, "Sparks");
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Ash()), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 0.3f, Speed1 = 1.1f, Size0 = 0.04f, Size1 = 0.09f, Gravity = -0.3f, Noise = 0.8f, NoiseFreq = 1.2f, Spin = 200f, Max = 500 }, root, "Ash");
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Smoke(0.35f)), Life0 = 1f, Life1 = 1.8f, Speed0 = 0.2f, Speed1 = 0.7f, Size0 = 0.3f, Size1 = 0.6f, SizeStart = 0.5f, SizePeak = 1.1f, SizeEnd = 1.6f, Gravity = -0.12f, Drag = 1f, Max = 200 }, root, "Smoke");
            foreach (var ps in new[] { _flame, _sparks, _ash, _smoke }) ps.Play(false);
            var core = M("fs_streak", new Color(1f, 0.85f, 0.6f), 4.5f, 1f); core.NoiseAmt = 0.15f; core.Soft = 0.02f; core.Erode = 0.08f; core.Scroll = new Vector2(-3f, 0f);
            Vol2Fs.MeshObject(root, "WireCore", Kit.Mat(core), out _coreMesh);
            var halo = M("fs_streak", Hot, 2.2f, 1f); halo.NoiseAmt = 0.4f; halo.Soft = 0.2f; halo.Erode = 0.2f; halo.Scroll = new Vector2(-1.5f, 0f);
            Vol2Fs.MeshObject(root, "WireHalo", Kit.Mat(halo), out _haloMesh);
            Vol2Fs.MeshObject(root, "InkArcs", Kit.InkSolid("fs_stroke2", InkC, new Color(1.6f, 0.5f, 0.12f, 1f), 0.12f, 0.45f, 0.3f, 1.3f), out _inkMesh);
            var fl = M("fs_streak", Hot, 3.6f, 1f); fl.NoiseAmt = 0.2f; fl.Soft = 0.02f; fl.Erode = 0.1f;
            Vol2Fs.MeshObject(root, "Flares", Kit.Mat(fl), out _flareMesh);
            _light = Vol2Fs.PointLight(root, "RingFire", Hot, 5f);
        }

        float WireH(float ang) => 0.45f * Mathf.Lerp(0.5f, 1f, Front(ang));
        Vector3 WireAt(float ang) => OnBand(ang, R) + Vector3.up * WireH(ang);
        /// <summary>The end clause: the wire burns away from its ends (the back arc) toward the front; 1 = still standing at this angle.</summary>
        float Burnt(float ang, float t) { float e = EndK(t); if (e <= 0f) return 1f; float behind = 1f - Mathf.Abs(Mathf.DeltaAngle(0f, ang * Mathf.Rad2Deg)) / 180f; return Mathf.Clamp01((behind - e * 1.15f) / 0.08f); }

        protected override void Cut(float t)
        {
            _light.transform.position = Apex + F * R + Vector3.up * 0.6f; _light.intensity = 7f;
            for (int k = 0; k < 40; k++) { float a = Random.Range(-Mathf.PI, Mathf.PI); if (Front(a) < 0.3f) continue; Vol2FsB1.EmitV(_sparks, WireAt(a), Tangent(a) * (a > 0f ? 4f : -4f) + Vector3.up * Random.Range(0.5f, 2f), -1f, -1f); }
        }

        protected override void Crossed(Struck s, float ang)
        {
            var b = s.T != null ? BodyBounds(s.T, s.T.position, 0.5f) : s.B;
            _flares.Add((ang, Now(NowUs), b.center, Mathf.Max(b.extents.x, b.extents.z)));
            Vector3 at = WireAt(ang);
            for (int k = 0; k < 120; k++) Vol2FsB1.EmitV(_sparks, at, (Random.insideUnitSphere + Vector3.up * 0.6f + Dir(ang) * 0.4f).normalized * Random.Range(3f, 11f), -1f, -1f);
            for (int k = 0; k < 26; k++) Vol2FsB1.EmitV(_flame, b.center + Vector3.Scale(Random.insideUnitSphere, b.extents), Vector3.up * Random.Range(1f, 2.6f), Random.Range(0.28f, 0.5f), -1f);
            Vol2FsB1.Emit(_smoke, b.center, 8);
            _light.transform.position = b.center; _light.intensity = 9f;
        }

        protected override void Ended()
        {
            for (int k = 0; k < 30; k++) { float a = Random.Range(-Mathf.PI, Mathf.PI); if (Front(a) < 0.3f) continue; Vol2FsB1.EmitV(_ash, WireAt(a), Vector3.up * Random.Range(0.4f, 1.2f), -1f, -1f); }
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos;
            _core.Clear(); _halo.Clear(); _ink.Clear(); _flare.Clear();
            float stand = Launched ? Vol2Fs.Smooth(LaunchAt, LaunchAt + 0.05f, t) : 0f, warn = LineWarn(t), flick = Flick(t), end = EndK(t);
            if (stand > 0.01f && end < 1f)
            {
                int n = 120;
                var pts = new List<Vector3>(n + 1); var w = new List<float>(n + 1); var al = new List<float>(n + 1); var hw = new List<float>(n + 1);
                float hum = 0.85f + 0.15f * Mathf.Sin(t * 23f);
                for (int k = 0; k <= n; k++)
                {
                    float ang = -Mathf.PI + k / (float)n * Mathf.PI * 2f, rk = RingK(ang), live = Reached(ang, t) * Burnt(ang, t);
                    pts.Add(WireAt(ang) + Vector3.up * 0.012f * Mathf.Sin(ang * 17f + t * 9f));
                    w.Add(0.035f * rk); hw.Add(0.16f * rk); al.Add(stand * live * flick * hum);
                }
                _core.Strip(pts, w, null, Color.white, al, 0.21f, cam, 0f, 10f);
                _halo.Strip(pts, hw, null, Color.white, al, 0.63f, cam, 0f, 6f);
                // flames licking up out of the trench (the front and sides; few behind the caster)
                if (Time.frameCount % 2 == 0)
                    for (int i = 0; i < 3; i++)
                    {
                        float a = Random.Range(-Mathf.PI, Mathf.PI); if (Random.value > Front(a) * 0.9f + 0.1f || Reached(a, t) < 0.5f || Burnt(a, t) < 0.5f) continue;
                        Vol2FsB1.EmitV(_flame, OnBand(a, R) + Vector3.up * 0.04f, Vector3.up * Random.Range(0.4f, 1.1f), Random.Range(0.14f, 0.3f) * (1f + warn), -1f);
                    }
                if (warn > 0f && Time.frameCount % 2 == 0) { float a = Random.Range(-1.8f, 1.8f); Vol2FsB1.EmitV(_sparks, WireAt(a), Random.insideUnitSphere * 3f + Vector3.up * 1.5f, -1f, -1f); }
                // the wire's dying edge rains ash and embers
                if (end > 0f)
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        float edge = sgn * Mathf.PI * (1f - Mathf.Clamp01(end * 1.15f));
                        Vol2FsB1.EmitV(_ash, WireAt(edge), Vector3.up * Random.Range(0.4f, 1.2f) + Random.insideUnitSphere * 0.3f, -1f, -1f);
                        Vol2FsB1.EmitV(_sparks, WireAt(edge), Vector3.up * 1.5f + Random.insideUnitSphere * 1.5f, -1f, -1f);
                    }
            }
            // two calligraphic ink arcs flung round outside the ring at the launch
            float ia = t - LaunchAt;
            if (Launched && ia < 3f)
            {
                float grow = Vol2Fs.Smooth(0f, 0.16f, ia), life = 1f - Vol2Fs.Smooth(1.4f, 3f, ia);
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    int n = 22; var pts = new Vector3[n]; var wd = new float[n]; var al = new float[n]; var sd = new Vector3[n];
                    for (int k = 0; k < n; k++)
                    {
                        float u = k / (float)(n - 1), ang = sgn * (0.15f + 1.75f * u * grow);
                        float r = R + 0.55f + 0.5f * u;
                        pts[k] = OnBand(ang, r) + Vector3.up * 0.03f; sd[k] = Dir(ang);
                        wd[k] = 0.32f * Mathf.Sin(Mathf.Clamp01(u * 0.95f + 0.05f) * Mathf.PI) * (1f - 0.4f * u); al[k] = life;
                    }
                    _ink.Strip(pts, wd, sd, Color.white, al, sgn > 0 ? 0.31f : 0.73f, cam, 0f, 3f);
                }
            }
            // the crossing flare: a horizontal molten slash across the body at the wire's height
            foreach (var f in _flares)
            {
                float a = t - f.At; if (a < 0f || a > 1.05f) continue;
                Vector3 tan = Tangent(f.Ang), c = new Vector3(f.C.x, Ground + Mathf.Max(0.5f, f.C.y - Ground), f.C.z) + FlatN(cam - f.C) * (f.W + 0.15f);
                Vector3 a0 = c - tan * (f.W + 0.8f) - Vector3.up * 0.15f, a1 = c + tan * (f.W + 0.8f) + Vector3.up * 0.15f;
                Vol2FsB6.Slash(_flare, a0, a1, Vector3.up * 0.12f, 0.36f, Vol2Fs.Smooth(0f, 0.1f, a), 1f - Vol2Fs.Smooth(0.15f, 0.5f, a), 0.41f, cam);
                Vol2FsB6.Slash(_flare, a0 + Vector3.up * 0.1f, a1 + Vector3.up * 0.14f, Vector3.up * 0.18f, 0.48f, Vol2Fs.Smooth(0.03f, 0.14f, a), 0.35f * (1f - Vol2Fs.Smooth(0.2f, 0.8f, a)), 0.66f, cam);
                Vol2FsB6.Slash(_flare, a0 - Vector3.up * 0.1f, a1 - Vector3.up * 0.06f, Vector3.up * 0.24f, 0.58f, Vol2Fs.Smooth(0.06f, 0.18f, a), 0.18f * (1f - Vol2Fs.Smooth(0.3f, 1.0f, a)), 0.88f, cam);
            }
            _core.Write(_coreMesh); _halo.Write(_haloMesh); _ink.Write(_inkMesh); _flare.Write(_flareMesh);
            _light.intensity = Mathf.MoveTowards(_light.intensity, stand * (1f - end) * 2.2f * flick, Time.deltaTime * 12f);
            if (_light.intensity < 3f) _light.transform.position = Apex + F * R + Vector3.up * 0.6f;
        }
    }
}
