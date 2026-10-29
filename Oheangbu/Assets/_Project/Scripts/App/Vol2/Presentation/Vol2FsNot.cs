using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>놋 (fire, ㅗ fan, 가른다) - free showcase batch 6, concept not_a (five parallel claw slashes of white-hot flame rake across the fan, each leaving a molten
    /// glowing gouge in the ground, the wall cut into glowing halves, both bears' armour torn off in burning shards, black ink flung along the strokes).
    /// Executor facts only: the launch = the one cut: five claws rake diagonally across the fan almost together (0.03 s apart - a presentation of the one tick),
    /// each a thin-to-wide flame blade in the air racing over its own straight gouge (laid parallel here instead of the base's radial ones: the claw marks),
    /// sparks and flame tongues along it, a black ink stroke flung beside each; the gouges glow molten and smoulder while the strips stand; the split wall's
    /// faces drip fire; the stripped bodies' seams burn (the body action: 2 s, then ash rising - CSV 끝).</summary>
    public sealed class Vol2FsNot : Vol2FsCutFan
    {
        static readonly Color Hot = new Color(1f, 0.38f, 0.1f);      // r2: orange-red (r1: yellow paper wedges)
        const int Claws = 5;
        const float ClawGap = 0.03f, Rake = 0.13f;
        ParticleSystem _flame, _sparks, _smoke;
        Mesh _clawMesh, _coreMesh, _inkMesh; readonly Vol2FsStrips _claw = new Vol2FsStrips(), _core = new Vol2FsStrips(), _ink = new Vol2FsStrips();
        Vector3 _cd;
        protected override Color CutGlow => new Color(1.9f, 0.75f, 0.2f);
        protected override float CrackWidth => 0.46f;
        protected override float CrackJag => 0.03f;

        protected override void LayGouges()
        {
            // five parallel claw marks: running forward-left across the middle of the fan, 0.6 m apart, the near ends kept 1.7 m+ from the caster
            _cd = Dir(-0.95f); Vector3 perp = Vector3.Cross(Vector3.up, _cd);
            Vector3 c = InFan(0.62f, 0.05f);
            for (int k = 0; k < Claws; k++)
            {
                float off = (k - (Claws - 1) * 0.5f) * 0.62f, len = 5.2f - 0.5f * Mathf.Abs(k - 2);
                Vector3 mid = c + perp * off;
                Gouges.Add(new Gouge { A = G(mid - _cd * len * 0.5f), B = G(mid + _cd * len * 0.5f), Width = CrackWidth * (0.85f + 0.3f * Vol2Fs.Hash(Seed, k + 7)), OpenAt = LaunchAt + k * ClawGap, Rnd = Vol2Fs.Hash(Seed, k + 17), Seed = Seed + k * 13 });
            }
        }

        protected override void OnSetup()
        {
            var root = Root.transform;
            _flame = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.7f, 0.9f)), Life0 = 0.25f, Life1 = 0.55f, Speed0 = 0.5f, Speed1 = 1.6f, Size0 = 0.2f, Size1 = 0.42f, Gravity = -0.8f, Noise = 0.8f, NoiseFreq = 1.5f, SizeStart = 0.5f, SizePeak = 1f, SizeEnd = 0.3f, Max = 1000 }, root, "Flame");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.7f, 0.3f), 5f, 1f)), Life0 = 0.25f, Life1 = 0.7f, Speed0 = 3f, Speed1 = 10f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.6f, Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.5f, Max = 1400 }, root, "Sparks");
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Smoke(0.45f)), Life0 = 0.8f, Life1 = 1.6f, Speed0 = 0.3f, Speed1 = 1f, Size0 = 0.3f, Size1 = 0.7f, SizeStart = 0.5f, SizePeak = 1.1f, SizeEnd = 1.6f, Gravity = -0.15f, Drag = 1f, Max = 200 }, root, "Smoke");
            foreach (var ps in new[] { _flame, _sparks, _smoke }) ps.Play(false);
            var cl = M("fs_streak", Hot, 2.2f, 0.85f); cl.NoiseAmt = 0.3f; cl.Soft = 0.04f; cl.Erode = 0.14f;
            Vol2Fs.MeshObject(root, "Claws", Kit.Mat(cl), out _clawMesh);
            var co = M("fs_streak", new Color(1f, 0.72f, 0.42f), 2.6f, 1f); co.NoiseAmt = 0.15f; co.Soft = 0.02f;
            Vol2Fs.MeshObject(root, "ClawCore", Kit.Mat(co), out _coreMesh);
            Vol2Fs.MeshObject(root, "ClawInk", Kit.InkSolid("fs_stroke2", InkC, new Color(1.6f, 0.5f, 0.12f, 1f), 0.12f, 0.45f, 0.3f, 1.3f), out _inkMesh);
        }

        protected override void Sweep(float t, float k, float prevK) { }

        protected override void OnCleave(Cleave c)
        {
            for (int k = 0; k < 24; k++) Vol2FsB1.EmitV(_flame, c.Centre + Vector3.up * Random.Range(0.05f, 0.9f), Vector3.up * Random.Range(0.5f, 1.5f) + c.Side * Random.Range(-0.4f, 0.4f), Random.Range(0.2f, 0.4f), -1f);
            Vol2FsB1.Emit(_smoke, c.Centre + Vector3.up * 0.6f, 6);
        }

        protected override void OnStrip(Strip s)
        {
            var b = BodyNow(s);
            for (int k = 0; k < 20; k++) Vol2FsB1.EmitV(_flame, b.center + Vector3.Scale(Random.insideUnitSphere, b.extents), Vector3.up * Random.Range(1f, 2.5f), Random.Range(0.3f, 0.55f), -1f);
            Vol2FsB1.Emit(_smoke, b.center, 8);
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos; _claw.Clear(); _core.Clear(); _ink.Clear();
            Vector3 perp = Vector3.Cross(Vector3.up, _cd);
            for (int k = 0; k < Gouges.Count; k++)
            {
                var g = Gouges[k];
                float a = t - g.OpenAt; if (a < 0f) continue;
                // the claw: a flame blade 0.5 - 1.1 m up, racing A -> B in the rake time, then its tail thins away
                // r2: low over the ground (r1 floated as yellow wedges), fast rake, then a slow afterimage
                float head = Vol2Fs.Smooth(0f, Rake, a), tail = Vol2Fs.Smooth(Rake + 0.1f, Rake + 0.85f, a), life = 1f - Vol2Fs.Smooth(Rake + 0.3f, Rake + 0.95f, a);
                float y0 = 0.1f + 0.03f * k, y1 = 0.38f + 0.04f * k;
                if (life > 0.01f && head - tail > 0.01f)
                {
                    Vector3 pa = Vector3.Lerp(g.A, g.B, tail) + Vector3.up * Mathf.Lerp(y1, y0, tail), pb = Vector3.Lerp(g.A, g.B, head) + Vector3.up * Mathf.Lerp(y1, y0, head);
                    Vol2FsB6.Slash(_claw, pa, pb, Vector3.up * 0.08f, 0.24f, 1f, life, 0.2f + k * 0.13f, cam);
                    Vol2FsB6.Slash(_core, pa, pb, Vector3.up * 0.08f, 0.06f, 1f, life * (1f - Vol2Fs.Smooth(Rake, Rake + 0.4f, a)), 0.4f + k * 0.13f, cam);
                    Vol2FsB6.Slash(_claw, pa - _cd * 0.3f + Vector3.up * 0.12f, pb - _cd * 0.3f + Vector3.up * 0.12f, Vector3.up * 0.12f, 0.32f, 1f, life * 0.35f, 0.55f + k * 0.13f, cam);      // the phantoms
                    Vol2FsB6.Slash(_claw, pa - _cd * 0.6f + Vector3.up * 0.24f, pb - _cd * 0.6f + Vector3.up * 0.24f, Vector3.up * 0.16f, 0.4f, 1f, life * 0.18f, 0.75f + k * 0.13f, cam);
                    if (a < Rake + 0.05f)
                    {
                        Vector3 p = G(Vector3.Lerp(g.A, g.B, head));
                        for (int i = 0; i < 5; i++) Vol2FsB1.EmitV(_sparks, p + Vector3.up * 0.1f, (perp * (i % 2 == 0 ? 1f : -1f) * Random.Range(0.8f, 2f) + Vector3.up * Random.Range(0.5f, 1.5f) + _cd * 1.2f).normalized * Random.Range(3f, 9f), -1f, -1f);
                        Vol2FsB1.EmitV(_flame, p, Vector3.up * Random.Range(0.8f, 1.8f), Random.Range(0.25f, 0.45f), -1f);
                    }
                }
                // the black ink flung along each claw (lies on the ground beside it, then dries away)
                float il = Vol2Fs.Smooth(0f, Rake, a) * (1f - Vol2Fs.Smooth(1.8f, 3.4f, a));
                if (il > 0.01f) Vol2FsB6.Crack(_ink, g.A + perp * 0.48f, Vector3.Lerp(g.A, g.B, head) + perp * 0.48f, Ground, 0.12f + 0.05f * g.Rnd, 0f, 1f, il, g.Rnd + 0.5f, g.Seed + 3, 0.05f, 0.015f);
                // the gouge smoulders: little flames along it while it is open
                if (a > Rake && a < 2.4f && Time.frameCount % 6 == k % 6) Vol2FsB1.EmitV(_flame, G(Vector3.Lerp(g.A, g.B, Random.value)) + Vector3.up * 0.04f, Vector3.up * Random.Range(0.3f, 0.9f), Random.Range(0.12f, 0.24f), -1f);
            }
            _claw.Write(_clawMesh); _core.Write(_coreMesh); _ink.Write(_inkMesh);
        }
    }
}
