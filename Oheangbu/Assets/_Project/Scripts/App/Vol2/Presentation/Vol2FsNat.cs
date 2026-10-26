using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>낫 (fire, ㅏ path, 가른다) - free showcase batch 6, concept nat_b (a razor-thin white-hot line of flame races along the ground like a cutting torch,
    /// leaving a glowing molten gouge, slicing the wall in two - molten cut faces - and flaring up the bear's front, its crystal armour bursting off; ink flung
    /// out to the sides; the seam burns). Executor facts only: the run = a white-hot torch head racing along the path (a spray of sparks sideways, flame
    /// licking up out of the gouge behind it, ink strokes flung flat out to both sides), the split walls glow molten at their cut and drip fire, the Instant on
    /// the first body = the line flares straight up its front (a molten vertical slash) + a fountain of sparks, the strip book opens the seam (3 s), the body
    /// action burns it 2 s and ends in ash rising (CSV 끝: 끝부터 타 재로 날림).</summary>
    public sealed class Vol2FsNat : Vol2FsCutPath
    {
        static readonly Color Hot = new Color(1f, 0.55f, 0.18f);
        ParticleSystem _flame, _sparks, _smoke;
        Mesh _inkMesh, _flareMesh; readonly Vol2FsStrips _ink = new Vol2FsStrips(), _flare = new Vol2FsStrips();
        float _nextInk;
        readonly List<(Vector3 A, Vector3 B, float W, float At, float Rnd)> _strokes = new List<(Vector3, Vector3, float, float, float)>();
        readonly List<(Vector3 Base, float H, Vector3 Toward, float At)> _flares = new List<(Vector3, float, Vector3, float)>();
        Light _torch;
        protected override Color CutGlow => new Color(1.9f, 0.75f, 0.2f);
        protected override float CrackWidth => 0.42f;      // r2: wider (r1: the molten line hid behind the caster)
        protected override float CrackJag => 0.08f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _flame = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.7f, 0.9f)), Life0 = 0.25f, Life1 = 0.55f, Speed0 = 0.5f, Speed1 = 1.6f, Size0 = 0.2f, Size1 = 0.42f, Gravity = -0.8f, Noise = 0.8f, NoiseFreq = 1.5f, SizeStart = 0.5f, SizePeak = 1f, SizeEnd = 0.3f, Max = 900 }, root, "Flame");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.7f, 0.3f), 5f, 1f)), Life0 = 0.25f, Life1 = 0.7f, Speed0 = 3f, Speed1 = 10f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.6f, Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.5f, Max = 1200 }, root, "Sparks");
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Smoke(0.45f)), Life0 = 0.8f, Life1 = 1.6f, Speed0 = 0.3f, Speed1 = 1f, Size0 = 0.3f, Size1 = 0.7f, SizeStart = 0.5f, SizePeak = 1.1f, SizeEnd = 1.6f, Gravity = -0.15f, Drag = 1f, Max = 200 }, root, "Smoke");
            foreach (var ps in new[] { _flame, _sparks, _smoke }) ps.Play(false);
            Vol2Fs.MeshObject(root, "InkFlung", Kit.InkSolid("fs_stroke2", InkC, new Color(1.6f, 0.5f, 0.12f, 1f), 0.1f, 0.45f, 0.3f, 1.3f), out _inkMesh);
            var fl = M("fs_streak", Hot, 3.4f, 1f); fl.NoiseAmt = 0.2f; fl.Soft = 0.02f; fl.Erode = 0.1f;
            Vol2Fs.MeshObject(root, "Flare", Kit.Mat(fl), out _flareMesh);
            _torch = Vol2Fs.PointLight(root, "Torch", Hot, 4.5f);
        }

        protected override void OnLaunch(long nowUs) { base.OnLaunch(nowUs); _nextInk = StartAlong + 0.3f; _torch.intensity = 5f; }

        protected override void Run(float t, float along, float prev)
        {
            Vector3 side = Vector3.Cross(Vector3.up, F);
            Vector3 head = OnPath(along) + Vector3.up * 0.06f;
            _torch.transform.position = head + Vector3.up * 0.3f; _torch.intensity = 6f;
            int n = Mathf.Max(1, Mathf.CeilToInt((along - prev) * 10f));
            for (int k = 0; k < n; k++)
            {
                Vector3 p = OnPath(Mathf.Lerp(prev, along, (k + 0.5f) / n)) + Vector3.up * 0.05f;
                Vol2FsB1.EmitV(_flame, p, Vector3.up * Random.Range(0.6f, 1.6f), Random.Range(0.18f, 0.36f), -1f);
                for (int i = 0; i < 4; i++) Vol2FsB1.EmitV(_sparks, p, (side * (i % 2 == 0 ? 1f : -1f) * Random.Range(1f, 2.2f) + Vector3.up * Random.Range(0.4f, 1.4f) + F * Random.Range(0.2f, 1.2f)).normalized * Random.Range(3f, 9f), -1f, -1f);
            }
            // ink flung flat out to both sides of the cut, every half metre
            while (_nextInk <= along && _nextInk < AlongA + 0.2f)
            {
                int k = _strokes.Count;
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    // r2: longer and thicker (r1: they stayed under the caster), a little forward so they open like the concept's spray
                    // r3: irregular (r2: an even ladder of strokes): every other side skips, angles and lengths vary widely
                    int kk = k * 2 + (sgn > 0 ? 1 : 0);
                    if (Vol2Fs.Hash(Seed, kk + 900) < 0.3f) continue;
                    float len = 1.2f + 2.8f * Vol2Fs.Hash(Seed, kk);
                    Vector3 a = OnPath(_nextInk + 0.2f * Vol2Fs.Hash(Seed, kk + 11)) + side * sgn * 0.3f, b = a + (side * sgn * 0.9f + F * (-0.25f + 0.85f * Vol2Fs.Hash(Seed, kk + 7))).normalized * len;
                    _strokes.Add((a, b, 0.08f + 0.18f * Vol2Fs.Hash(Seed, kk + 40), t, Vol2Fs.Hash(Seed, kk + 80)));
                }
                _nextInk += 0.6f;
            }
        }

        protected override void OnStruck(Struck s)
        {
            if (s.Kind != "Instant") return;
            var b = s.T != null ? BodyBounds(s.T, s.T.position, 0.5f) : s.B;
            Vector3 toCam = FlatN(CamPos - b.center), at = G(b.center) + toCam * (Mathf.Max(b.extents.x, b.extents.z) * 0.8f);
            _flares.Add((at, b.size.y + 1.6f, toCam, Now(NowUs)));
            for (int k = 0; k < 140; k++) Vol2FsB1.EmitV(_sparks, at + Vector3.up * Random.Range(0f, b.size.y), (Random.insideUnitSphere + Vector3.up * 0.7f + toCam * 0.3f).normalized * Random.Range(4f, 12f), -1f, -1f);
            for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_flame, at + Vector3.up * Random.Range(0f, b.size.y), Vector3.up * Random.Range(1f, 3f) + Random.insideUnitSphere, Random.Range(0.3f, 0.6f), -1f);
            Vol2FsB1.Emit(_smoke, b.center, 10);
            _torch.transform.position = b.center; _torch.intensity = 9f;
        }

        protected override void OnCleave(Cleave c)
        {
            // molten cut faces: fire licks out of the gap and drips
            for (int k = 0; k < 24; k++) Vol2FsB1.EmitV(_flame, c.Centre + Vector3.up * Random.Range(0.05f, 0.9f), Vector3.up * Random.Range(0.5f, 1.5f) + c.Side * Random.Range(-0.4f, 0.4f), Random.Range(0.2f, 0.4f), -1f);
            Vol2FsB1.Emit(_smoke, c.Centre + Vector3.up * 0.6f, 6);
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos;
            _ink.Clear(); _flare.Clear();
            foreach (var s in _strokes)
            {
                float a = t - s.At; if (a < 0f) continue;
                float grow = Vol2Fs.Smooth(0f, 0.1f, a), life = 1f - Vol2Fs.Smooth(1.6f, 3.2f, a);
                if (life <= 0.01f) continue;
                Vector3 b = Vector3.Lerp(s.A, s.B, grow);
                Vol2FsB6.Crack(_ink, s.A, b, Ground, s.W, 0f, 1f, life, s.Rnd, Seed + (int)(s.Rnd * 100), 0.05f, 0.015f);
            }
            foreach (var f in _flares)
            {
                float a = t - f.At; if (a < 0f || a > 1.3f) continue;
                // fast -> held -> a slow afterimage (r2: longer, wider)
                float reveal = Vol2Fs.Smooth(0f, 0.07f, a), life = 1f - Vol2Fs.Smooth(0.3f, 1.3f, a);
                Vector3 top = f.Base + Vector3.up * f.H;
                Vol2FsB6.Slash(_flare, f.Base, top, f.Toward * 0.15f, 0.6f, reveal, life, 0.27f, cam);
                Vol2FsB6.Slash(_flare, f.Base + Vector3.up * 0.05f, top + Vector3.up * 0.3f, f.Toward * 0.3f, 0.85f, Vol2Fs.Smooth(0.03f, 0.12f, a), life * 0.3f, 0.71f, cam);
                if (a < 0.5f && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_flame, f.Base + Vector3.up * Random.Range(0f, f.H * reveal), Vector3.up * 1.5f, Random.Range(0.25f, 0.45f), -1f);
            }
            // the gouge keeps smouldering a while (little flames along it)
            float run = RunAlong(t);
            if (t - LaunchAt < 2.6f && run > StartAlong + 0.3f)
                for (int k = 0; k < 2; k++) Vol2FsB1.EmitV(_flame, OnPath(Random.Range(StartAlong, run)) + Vector3.up * 0.04f, Vector3.up * Random.Range(0.5f, 1.4f), Random.Range(0.22f, 0.45f), -1f);
            _ink.Write(_inkMesh); _flare.Write(_flareMesh);
            _torch.intensity = Mathf.MoveTowards(_torch.intensity, 0f, Time.deltaTime * 10f);
        }
    }
}
