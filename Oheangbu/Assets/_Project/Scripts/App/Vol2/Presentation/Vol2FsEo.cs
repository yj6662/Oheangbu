using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>어 (water, ㅓ guard, 한 번) - free showcase batch 2, concept eo_a (a spinning disc of clear water swirls round the brush in front of the caster like a
    /// whirlpool shield, ink spiralling inside it, droplets flung off its rim; the paw strikes the whirlpool and is swept aside: a huge crown splash at the contact,
    /// ripples running over the disc, then (the CSV's end) the water falls and runs down onto the ground). Without a parry the disc spins down and pours away.</summary>
    public sealed class Vol2FsEo : Vol2FsAttach
    {
        static readonly Color Ink = new Color(0.02f, 0.03f, 0.04f), Foam = new Color(0.9f, 0.95f, 0.95f);
        ParticleSystem _drops, _splash, _foam, _fall, _mist; Light _light;
        Mesh _discMesh, _inkMesh, _crownMesh, _rippleMesh, _wetMesh;
        readonly Vol2FsStrips _ink = new Vol2FsStrips(), _crown = new Vol2FsStrips(), _ripple = new Vol2FsStrips(), _wet = new Vol2FsStrips();
        Vector3 _c, _n, _a0, _a1; float _spin, _lastT = -1f; bool _poured;
        struct Sheet { public float Ang, H, W, Delay, Rnd; }
        readonly List<Sheet> _sheets = new List<Sheet>();
        const float DiscR = 1.05f;

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsB1.M(tex, tint, intensity, addMix);

        protected override void OnBegin()
        {
            var root = Root.transform;
            Vol2Fs.MeshObject(root, "Whirlpool", Vol2FsB1.Water(Kit, 1.7f, 0.4f, "fs_swirl"), out _discMesh);
            Vol2Fs.MeshObject(root, "InkSpirals", Kit.InkSolid("fs_stroke2", Ink, new Color(0.5f, 0.9f, 1f, 1f) * 0.8f, 0.05f, 0.45f, 0.26f, 1.4f), out _inkMesh);
            Vol2Fs.MeshObject(root, "Crown", Vol2FsB1.Water(Kit, 1.8f, 0.35f), out _crownMesh);
            var rip = M("fs_ring", new Color(0.85f, 0.97f, 1f, 1f), 2.2f, 0.85f); rip.Soft = 0.01f; rip.NoiseAmt = 0.3f;
            Vol2Fs.MeshObject(root, "Ripples", Kit.Mat(rip), out _rippleMesh);
            var wet = M("fs_wet", new Color(0.05f, 0.04f, 0.04f, 1f), 1f, 0f); wet.Soft = 0.001f; wet.Opacity = 0.7f; wet.NoiseAmt = 0.3f;
            Vol2Fs.MeshObject(root, "Wet", Kit.Mat(wet), out _wetMesh);
            var water = M("fs_splash", Color.white, 1.6f, 0.3f); water.Ramp = "fs_ramp_water"; water.RampScale = 1.5f; water.NoiseAmt = 0.4f; water.Soft = 0.1f;
            _splash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(water), Life0 = 0.5f, Life1 = 1.1f, Speed0 = 2f, Speed1 = 7f, Size0 = 0.3f, Size1 = 0.8f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.2f, Gravity = 1.6f, Drag = 0.6f, Tiles = 2,
                SizeStart = 0.4f, SizePeak = 1.2f, SizeEnd = 0.8f, Spin = 120f, Max = 600 }, root, "Splash");
            var drop = M("fs_dot", new Color(0.8f, 0.95f, 1f), 2.2f, 0.6f);
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.5f, Life1 = 1.2f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.03f, Size1 = 0.08f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.1f, Gravity = 1.8f,
                Stretch = true, StretchVel = 0.03f, Max = 1200 }, root, "Drops");
            var foam = M("fs_puff", Foam, 1.3f, 0.2f); foam.Opacity = 0.75f; foam.Erode = 0.4f; foam.NoiseAmt = 0.6f;
            _foam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(foam), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 0.8f, Speed1 = 2.5f, Size0 = 0.25f, Size1 = 0.6f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.15f, Drag = 1.5f, Tiles = 2, Max = 300 }, root, "Foam");
            _fall = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(water), Life0 = 0.5f, Life1 = 0.9f, Speed0 = 0f, Speed1 = 0.6f, Size0 = 0.2f, Size1 = 0.45f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.1f, Gravity = 2f, Tiles = 2,
                Stretch = true, StretchVel = 0.08f, StretchLen = 1.4f, Max = 500 }, root, "Fall");
            var mist = M("fs_puff", new Color(0.8f, 0.88f, 0.9f, 1f), 1.1f, 0.2f); mist.Opacity = 0.35f; mist.Erode = 0.4f;
            _mist = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mist), Life0 = 0.8f, Life1 = 1.5f, Speed0 = 0.3f, Speed1 = 1f, Size0 = 0.6f, Size1 = 1.2f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.3f, Drag = 1.5f, Tiles = 2, Max = 60, SortFudge = 8 }, root, "Mist");
            foreach (var ps in new[] { _splash, _drops, _foam, _fall, _mist }) ps.Play(false);
            _light = Vol2Fs.PointLight(root, "WaterLight", new Color(0.6f, 0.85f, 1f), 4f);
        }

        void SetFrame(Vector3 cam)
        {
            _c = Shaft(0.78f) + Away * 0.18f;
            _n = (Away * 0.8f + (_c - cam).normalized * 0.45f).normalized;                 // the whirlpool faces the strike, a little turned to the eye
            _a0 = Vector3.Cross(_n, Vector3.up).normalized; _a1 = Vector3.Cross(_n, _a0);
        }

        protected override void OnGuard(float t, float open)
        {
            var cam = CamPos;
            float dt = _lastT < 0f ? 0f : Mathf.Max(0f, t - _lastT); _lastT = t;
            SetFrame(cam);
            float collapse = IsParried ? Vol2Fs.Smooth(0.3f, 0.75f, t - ParryAt) : Vol2Fs.Smooth(WinEnd, WinEnd + 0.4f, t);
            float o = Mathf.Clamp01(Vol2Fs.EaseOutBack(Mathf.Clamp01((t - WinStart) / 0.12f))) * (1f - collapse * 0.4f);
            _spin += dt * (14f - 8f * collapse);
            float life = (1f - collapse) * Vol2Fs.Smooth(WinStart, WinStart + 0.05f, t);
            float rip = IsParried ? t - ParryAt : -1f;
            // the disc: a polar grid, the swirl texture turning, the parry's ripples as a wobble, the collapse sagging down
            var v = new List<Vector3>(); var col = new List<Color>(); var uv = new List<Vector3>(); var idx = new List<int>();
            const int RN = 7, SN = 40;
            if (life > 0.01f)
                for (int i = 0; i <= RN; i++)
                    for (int j = 0; j <= SN; j++)
                    {
                        float r = i / (float)RN, ph = j / (float)SN * Mathf.PI * 2f;
                        float wob = rip >= 0f ? 0.08f * Mathf.Sin(r * 14f - rip * 30f) * Mathf.Exp(-rip * 3f) : 0f;
                        Vector3 p = _c + (_a0 * Mathf.Cos(ph) + _a1 * Mathf.Sin(ph)) * r * DiscR * o + _n * (wob - 0.12f * r * r) + Vector3.down * collapse * collapse * r * 1.4f;
                        v.Add(p);
                        float a = life * Mathf.Clamp01((1f - r) * 6f) * (0.55f + 0.45f * r);
                        col.Add(new Color(1f, 1f, 1f, a));
                        float tw = ph + _spin + r * 2.5f;
                        uv.Add(new Vector3(0.5f + 0.5f * r * Mathf.Cos(tw), 0.5f + 0.5f * r * Mathf.Sin(tw), 0.23f));
                    }
            for (int i = 0; i < RN && life > 0.01f; i++)
                for (int j = 0; j < SN; j++)
                {
                    int a = i * (SN + 1) + j, b = a + 1, c = a + SN + 1, d = c + 1;
                    idx.Add(a); idx.Add(c); idx.Add(b); idx.Add(b); idx.Add(c); idx.Add(d);
                }
            _discMesh.Clear(); _discMesh.SetVertices(v); _discMesh.SetColors(col); _discMesh.SetUVs(0, uv); _discMesh.SetTriangles(idx, 0); _discMesh.RecalculateBounds();
            // two ink spirals turning inside it
            _ink.Clear();
            if (life > 0.01f)
                for (int s = 0; s < 3; s++)
                {
                    var pts = new Vector3[12]; var w = new float[12]; var al = new float[12]; var sd = new Vector3[12];
                    for (int k = 0; k < 12; k++)
                    {
                        float u = k / 11f, r = Mathf.Lerp(0.15f, 0.92f, u) * DiscR * o, ph = _spin * 1.1f + s * 2.09f + u * 4.2f;
                        Vector3 d = _a0 * Mathf.Cos(ph) + _a1 * Mathf.Sin(ph);
                        pts[k] = _c + d * r - _n * (0.05f + 0.1f * u * u) + Vector3.down * collapse * collapse * u * 1.4f;
                        sd[k] = d; w[k] = 0.09f * Mathf.Sin(u * Mathf.PI) + 0.01f; al[k] = life;
                    }
                    _ink.Strip(pts, w, sd, Color.white, al, s * 0.3f, cam, 0f, 1f);
                }
            _ink.Write(_inkMesh);
            // droplets flung off the rim; water running down the sleeve
            if (life > 0.2f && Time.frameCount % 1 == 0)
                for (int k = 0; k < 3; k++)
                {
                    float ph = Random.Range(0f, Mathf.PI * 2f); Vector3 d = _a0 * Mathf.Cos(ph) + _a1 * Mathf.Sin(ph); Vector3 tg = Vector3.Cross(_n, d);
                    Vol2FsB1.EmitV(_drops, _c + d * DiscR * o, (tg * 5f + d * 1.5f) * Random.Range(0.6f, 1.2f));
                }
            if (life > 0.2f && Time.frameCount % 3 == 0) Vol2FsB1.EmitV(_fall, Shaft(Random.Range(-0.2f, 0.4f)), Vector3.down * 0.5f, 0.12f, 0.5f);
            if (collapse > 0.05f && life > 0.05f) for (int k = 0; k < 4; k++) { float ph = Random.Range(0f, Mathf.PI * 2f); Vol2FsB1.EmitV(_fall, _c + (_a0 * Mathf.Cos(ph) + _a1 * Mathf.Sin(ph)) * DiscR * o * Random.value + Vector3.down * collapse * 0.8f, Vector3.down * 1.2f); }
            if (collapse > 0.5f && !_poured) { _poured = true; Vector3 g = _c; g.y = Ground + 0.05f; Vol2FsB1.Emit(_foam, g, 20); Vol2FsB1.Emit(_splash, g, 30); }
            // the wet patch it pours onto
            _wet.Clear();
            float wl = Vol2Fs.Smooth(0f, 0.3f, collapse) * (1f - Vol2Fs.Smooth(1.6f, 2.6f, t - (IsParried ? ParryAt : WinEnd)));
            if (wl > 0.01f) { Vector3 g = _c; g.y = Ground + 0.025f; _wet.Quad(g, Vector3.Cross(Vector3.up, Away), Away, 2.6f * Vol2Fs.Smooth(0f, 0.6f, collapse), new Color(1f, 1f, 1f, wl), 0.47f); }
            _wet.Write(_wetMesh);
            _light.transform.position = _c - _n * 0.4f;
            _light.intensity = 2.4f * life + (IsParried ? 6f * Mathf.Exp(-(t - ParryAt) * 5f) : 0f);
        }

        protected override void OnParry()
        {
            Vector3 view = (CamPos - ParryPoint).normalized;
            Vol2FsB1.Emit(_splash, ParryPoint, 140); Vol2FsB1.Emit(_foam, ParryPoint, 40); Vol2FsB1.Emit(_mist, ParryPoint, 8);
            for (int k = 0; k < 220; k++) Vol2FsB1.EmitV(_drops, ParryPoint, (Random.onUnitSphere + Vector3.up * 0.6f + view * 0.3f).normalized * Random.Range(3f, 9f));
            for (int k = 0; k < 16; k++) _sheets.Add(new Sheet { Ang = (k + Random.value * 0.6f) / 16f * Mathf.PI * 2f, H = Random.Range(0.6f, 1.5f), W = Random.Range(0.25f, 0.5f), Delay = Random.Range(0f, 0.05f), Rnd = Random.value });
        }

        protected override void OnReaction(float t, float h)
        {
            var cam = CamPos;
            // the crown: sheets of water thrown up and out round the contact, then falling
            _crown.Clear();
            Vector3 n = (_n + Vector3.up * 0.2f).normalized, a0 = Vector3.Cross(n, Vector3.up).normalized, a1 = Vector3.Cross(n, a0);
            foreach (var s in _sheets)
            {
                float a = h - s.Delay; if (a <= 0f) continue;
                float rise = Vol2Fs.Smooth(0f, 0.22f, a), fall = Vol2Fs.Smooth(0.3f, 0.8f, a), life = 1f - Vol2Fs.Smooth(0.45f, 0.95f, a);
                if (life <= 0.01f) continue;
                Vector3 d = a0 * Mathf.Cos(s.Ang) + a1 * Mathf.Sin(s.Ang);
                var pts = new Vector3[6]; var w = new float[6]; var al = new float[6];
                for (int k = 0; k < 6; k++)
                {
                    float u = k / 5f;
                    pts[k] = ParryPoint + d * (0.15f + s.H * rise * u) + (-n * 0.5f + Vector3.up * 0.4f) * s.H * rise * u * u * 0.6f + Vector3.down * fall * u * 0.9f;
                    w[k] = s.W * (1f - 0.6f * u) * rise; al[k] = life * (1f - 0.3f * u);
                }
                _crown.Strip(pts, w, null, Color.white, al, s.Rnd, cam, 0f, 1f);
            }
            _crown.Write(_crownMesh);
            // ripples running out over the disc from the contact
            _ripple.Clear();
            for (int k = 0; k < 3; k++)
            {
                float a = h - k * 0.09f; if (a <= 0f) continue;
                float r = Mathf.Lerp(0.1f, DiscR * 1.25f, Mathf.Clamp01(a / 0.5f)), l = 1f - Vol2Fs.Smooth(0.2f, 0.55f, a);
                if (l <= 0.01f) continue;
                var pts = new List<Vector3>(); var w = new List<float>(); var al = new List<float>(); var sd = new List<Vector3>();
                for (int j = 0; j <= 32; j++) { float ph = j / 32f * Mathf.PI * 2f; Vector3 d = _a0 * Mathf.Cos(ph) + _a1 * Mathf.Sin(ph); pts.Add(_c + d * r + _n * 0.03f); w.Add(0.12f); al.Add(l); sd.Add(d); }
                _ripple.Strip(pts, w, sd, Color.white, al, 0.2f * k, cam, 0f, 4f);
            }
            _ripple.Write(_rippleMesh);
        }
    }
}
