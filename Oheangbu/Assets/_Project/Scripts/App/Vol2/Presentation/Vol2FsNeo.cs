using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>너 (fire, ㅓ guard, 한 번) - free showcase batch 2, concept neo_a (a blazing wheel of flame petals spins round the brush shaft in front of the sleeve;
    /// the bear's paw strikes it and the wheel answers with a huge fire PEONY bursting open at the contact - layered petals of flame and black ink blooming outward,
    /// scorching the paw; the petals burn from their tips into ash rising, the CSV's end). Without a parry the wheel spins down into embers.</summary>
    public sealed class Vol2FsNeo : Vol2FsAttach
    {
        static readonly Color Orange = new Color(1f, 0.45f, 0.1f), Hot = new Color(1f, 0.8f, 0.4f), Ink = new Color(0.035f, 0.025f, 0.02f);
        ParticleSystem _flames, _embers, _ash, _smoke, _drops, _scorch; Light _light;
        Mesh _wheelMesh, _wheelInkMesh, _fireMesh, _inkMesh;
        readonly Vol2FsStrips _wheel = new Vol2FsStrips(), _wheelInk = new Vol2FsStrips(), _fire = new Vol2FsStrips(), _ink = new Vol2FsStrips();
        struct Petal { public float Ang, Len, Width, Delay, Curl, Life, Rnd; public int Layer; }
        readonly List<Petal> _petals = new List<Petal>();
        Transform _paw; Vector3 _pawLocal, _bloomN, _bloomR, _bloomU, _wheelC;
        bool _ashed;

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsB1.M(tex, tint, intensity, addMix);
        Vol2FsMat FireMat(string tex, float intensity, float addMix) { var m = M(tex, Color.white, intensity, addMix); m.Ramp = "fs_ramp_fire"; m.RampScale = 1.6f; m.NoiseAmt = 0.6f; m.Erode = 0.28f; m.Soft = 0.05f; return m; }

        protected override void OnBegin()
        {
            var root = Root.transform;
            Vol2Fs.MeshObject(root, "FireWheel", Kit.Mat(FireMat("fs_petal", 2.6f, 0.75f)), out _wheelMesh);
            Vol2Fs.MeshObject(root, "WheelInk", Kit.InkSolid("fs_petal", Ink, new Color(3f, 0.9f, 0.15f, 1f), 0.12f, 0.45f, 0.3f, 1.4f), out _wheelInkMesh);
            // 2026-10-27 per-glyph polish: the peony reads by its PETALS (r4 / the material pass: 40 big flipbook flames at the parry made one yellow fireball) -
            // orange-red petal fire (short ramp), more and longer petals in four layers, a hot small core, the flames only small licking tongues
            var petal = FireMat("fs_petal", 1.75f, 0.45f); petal.RampScale = 1.15f; petal.NoiseAmt = 0.5f;
            Vol2Fs.MeshObject(root, "PeonyFire", Kit.Mat(petal), out _fireMesh);
            Vol2Fs.MeshObject(root, "PeonyInk", Kit.InkSolid("fs_petal", Ink, new Color(3.2f, 1.0f, 0.18f, 1f), 0.1f, 0.5f, 0.28f, 1.3f), out _inkMesh);
            _flames = Vol2Fs.Build(Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick(1.5f, 0.5f)), 0.18f, 0.36f, 0.25f, 0.5f, 2f, 900, 2f), root, "Flames");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Hot, 5f, 1f)), Life0 = 0.5f, Life1 = 1.4f, Speed0 = 1.5f, Speed1 = 6f, Size0 = 0.025f, Size1 = 0.06f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.15f, Gravity = -0.3f,
                Noise = 1.6f, NoiseFreq = 2f, Stretch = true, StretchVel = 0.05f, Max = 900 }, root, "Embers");
            var ash = M("fs_ink", new Color(0.5f, 0.47f, 0.44f, 1f), 1f, 0f); ash.NoiseAmt = 0.3f; ash.Edge = new Color(1.8f, 0.55f, 0.1f, 1f); ash.EdgeWidth = 0.25f; ash.Soft = 0.05f;
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ash), Life0 = 1.2f, Life1 = 2.4f, Speed0 = 0.2f, Speed1 = 0.9f, Size0 = 0.04f, Size1 = 0.11f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.1f, VelOverLife = new Vector3(0f, 1.3f, 0f),
                Noise = 1.2f, NoiseFreq = 1f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.6f, Max = 700 }, root, "Ash");
            var smoke = M("fs_puff", new Color(0.08f, 0.06f, 0.05f, 1f), 1f, 0f); smoke.Opacity = 0.55f; smoke.NoiseAmt = 0.6f; smoke.Erode = 0.35f;
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(smoke), Life0 = 1.0f, Life1 = 1.8f, Speed0 = 0.4f, Speed1 = 1.2f, Size0 = 0.6f, Size1 = 1.2f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.25f, Drag = 1f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.2f, SizePeakAt = 0.5f, SizeEnd = 1.7f, VelOverLife = new Vector3(0f, 1f, 0f), Max = 80, SortFudge = 12 }, root, "Smoke");
            var drop = M("fs_ink", Ink, 1f, 0f); drop.Edge = new Color(2.5f, 0.7f, 0.12f, 1f); drop.EdgeWidth = 0.2f; drop.NoiseAmt = 0.3f; drop.Soft = 0.1f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.7f, Life1 = 1.3f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.08f, Size1 = 0.28f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.2f, Gravity = 2f, Drag = 0.4f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 300 }, root, "InkDrops");
            var paw = Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick(1.5f, 0.5f)), 0.22f, 0.42f, 0.3f, 0.6f, 2.2f, 250, 2.1f); paw.Loop = true; paw.Radius = 0.22f;
            _scorch = Vol2Fs.Build(paw, root, "PawFire");
            foreach (var ps in new[] { _flames, _embers, _ash, _smoke, _drops, _scorch }) ps.Play(false);
            _light = Vol2Fs.PointLight(root, "FireLight", Orange, 6f);
        }

        /// <summary>One flat petal in the plane (n = its normal): from c along d (len), its width bulging in the middle, the tip curling toward <paramref name="curlDir"/>.</summary>
        static void PetalStrip(Vol2FsStrips s, Vector3 c, Vector3 d, Vector3 n, float len, float width, Vector3 curlDir, float curl, float alpha, float rnd, Vector3 cam)
        {
            var pts = new Vector3[7]; var w = new float[7]; var al = new float[7]; var side = new Vector3[7];
            Vector3 sd = Vector3.Cross(n, d).normalized;
            for (int k = 0; k < 7; k++)
            {
                float u = k / 6f;
                pts[k] = c + d * len * u + curlDir * curl * len * u * u;
                w[k] = width * Mathf.Pow(Mathf.Sin(Mathf.Clamp01(u * 0.92f + 0.04f) * Mathf.PI), 0.7f) * (1.05f - 0.35f * u);
                al[k] = alpha * (1f - 0.25f * u);
                side[k] = sd;
            }
            s.Strip(pts, w, side, Color.white, al, rnd, cam, 0f, 1f);
        }

        protected override void OnGuard(float t, float open)
        {
            var cam = CamPos;
            _wheelC = Shaft(0.66f);
            Vector3 n = (Away + (cam - _wheelC).normalized * 0.6f).normalized;            // the wheel faces the strike, turned a little to the eye
            Vector3 a0 = Vector3.Cross(n, Vector3.up).normalized, a1 = Vector3.Cross(n, a0);
            float spin = t * 13f;
            float o = Mathf.Clamp01(Vol2Fs.EaseOutBack(open));
            _wheel.Clear(); _wheelInk.Clear();
            if (o > 0.01f)
                for (int k = 0; k < 8; k++)
                {
                    float ang = spin + k * Mathf.PI * 0.25f;
                    Vector3 d = (a0 * Mathf.Cos(ang) + a1 * Mathf.Sin(ang));
                    Vector3 tang = Vector3.Cross(n, d);
                    PetalStrip(_wheel, _wheelC + d * 0.1f, d, n, 0.78f * o, 0.34f * o, -tang, 0.55f, open, k * 0.13f, cam);
                    if (k % 2 == 0) PetalStrip(_wheelInk, _wheelC + d * 0.18f + n * 0.02f, (d + tang * 0.35f).normalized, n, 0.62f * o, 0.2f * o, -tang, 0.7f, open, k * 0.21f + 0.5f, cam);
                    if (open > 0.3f && (k + Time.frameCount) % 3 == 0) Vol2FsB1.Emit(_flames, _wheelC + d * 0.75f * o, 1);
                }
            _wheel.Write(_wheelMesh); _wheelInk.Write(_wheelInkMesh);
            // fire runs on the sleeve and the shaft
            if (open > 0.2f && Time.frameCount % 2 == 0) { Vol2FsB1.Emit(_flames, Shaft(Random.Range(-0.15f, 0.6f)), 1); Vol2FsB1.Emit(_embers, _wheelC, 2); }
            if (!IsParried && open < 0.5f && open > 0.05f && t > WinEnd) Vol2FsB1.Emit(_embers, _wheelC, 4);
            _light.transform.position = _wheelC + n * 0.3f;
            _light.intensity = 5f * o + (IsParried ? 16f * Mathf.Exp(-(t - ParryAt) * 4f) : 0f);
        }

        protected override void OnParry()
        {
            var cam = CamPos;
            Vector3 view = (cam - ParryPoint).normalized;
            _bloomN = (view * 0.75f - Away * 0.25f).normalized;                          // the flower opens toward the eye, a little back at the bear
            _bloomR = Vector3.Cross(Vector3.up, _bloomN).normalized; _bloomU = Vector3.Cross(_bloomN, _bloomR);
            int[] counts = { 9, 14, 12, 8 }; float[] lens = { 2.5f, 1.7f, 1.1f, 0.55f }; float[] wid = { 0.95f, 0.66f, 0.48f, 0.32f };      // polish: four layers, longer
            for (int L = 0; L < 4; L++)
                for (int k = 0; k < counts[L]; k++)
                {
                    float ang = (k + 0.5f * L + 0.3f * Vol2Fs.Hash(Seed, L * 40 + k)) / counts[L] * Mathf.PI * 2f;
                    _petals.Add(new Petal { Layer = L, Ang = ang, Len = lens[L] * Mathf.Lerp(0.75f, 1.15f, Vol2Fs.Hash(Seed, L * 40 + k + 300)), Width = wid[L] * Mathf.Lerp(0.8f, 1.1f, Vol2Fs.Hash(Seed, L * 40 + k + 600)),
                        Delay = 0.025f * L + 0.02f * Vol2Fs.Hash(Seed, L * 40 + k + 900), Curl = Mathf.Lerp(0.25f, 0.6f, Vol2Fs.Hash(Seed, L * 40 + k + 1200)), Life = Mathf.Lerp(0.85f, 1.25f, Vol2Fs.Hash(Seed, L * 40 + k + 1500)) - 0.08f * L, Rnd = Vol2Fs.Hash(Seed, L * 40 + k + 1800) });
                }
            _paw = Vol2FsB2.NearestBone(Attacker, ParryPoint);
            if (_paw != null) _pawLocal = _paw.InverseTransformPoint(ParryPoint + Away * 0.1f);
            Vol2FsB1.Emit(_embers, ParryPoint, 260); Vol2FsB1.Emit(_drops, ParryPoint, 70);
            for (int k = 0; k < 18; k++) Vol2FsB1.EmitV(_flames, ParryPoint, (Random.onUnitSphere + _bloomN * 0.5f).normalized * Random.Range(2f, 5f), Random.Range(0.22f, 0.4f), 0.4f);
            Vol2FsB1.Emit(_smoke, ParryPoint + Vector3.up * 0.4f, 6);
            var em = _scorch.emission; em.rateOverTime = 70f;
        }

        protected override void OnReaction(float t, float h)
        {
            var cam = CamPos;
            _fire.Clear(); _ink.Clear();
            foreach (var p in _petals)
            {
                float a = h - p.Delay; if (a <= 0f) continue;
                float grow = Vol2Fs.EaseOutBack(Mathf.Clamp01(a / 0.2f));
                float life = 1f - Vol2Fs.Smooth(0.5f, p.Life + 0.15f, a);      // polish: the open flower holds a little longer
                if (life <= 0.01f) continue;
                Vector3 d = _bloomR * Mathf.Cos(p.Ang) + _bloomU * Mathf.Sin(p.Ang);
                Vector3 dir = (d * (0.85f + 0.1f * p.Layer) + _bloomN * (0.25f + 0.15f * p.Layer)).normalized;   // the inner layers stand up toward the eye (a cup)
                Vector3 n = Vector3.Cross(dir, Vector3.Cross(_bloomN, dir)).normalized; if (n.sqrMagnitude < 1e-4f) n = _bloomN;
                float len = p.Len * grow * (1f + 0.15f * Vol2Fs.Smooth(0.2f, 0.8f, a));
                if (p.Layer == 0) PetalStrip(_ink, ParryPoint + dir * 0.1f, dir, n, len, p.Width * grow, -_bloomN + Vector3.down * 0.3f, p.Curl, life, p.Rnd, cam);
                else PetalStrip(_fire, ParryPoint + dir * 0.05f, dir, n, len, p.Width * grow, -_bloomN, p.Curl, life, p.Rnd, cam);
                // the petals burn from their tips into ash
                if (a > 0.35f && life < 0.9f && (Time.frameCount + (int)(p.Rnd * 50)) % 4 == 0) Vol2FsB1.Emit(_ash, ParryPoint + dir * len * 0.9f, 1);
            }
            _fire.Write(_fireMesh); _ink.Write(_inkMesh);
            if (h > 0.55f && !_ashed) { _ashed = true; Vol2FsB1.Emit(_ash, ParryPoint, 60); Vol2FsB1.Emit(_smoke, ParryPoint + Vector3.up * 0.6f, 4); }
            // the paw keeps burning a little while
            Vector3 c = _paw != null ? _paw.TransformPoint(_pawLocal) : AttackerNow();
            _scorch.transform.position = c;
            if (h > 0.9f && _scorch.emission.rateOverTime.constant > 0f) { var em = _scorch.emission; em.rateOverTime = 0f; Vol2FsB1.Emit(_ash, c, 20); }
        }
    }
}
