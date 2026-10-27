using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>금>금 비화 - free showcase batch 8, concept mm_a (doubled edges): the steel shape rings into the enemy's blade cluster; beside every old blade a
    /// parallel new edge shoots up with a ringing white-gold flash, and a wider ring of taller blades (to ~1.7 m) leans out round it - the steel thicket
    /// twice as dense, black ink splashing from its foot, sparks running up the edges. It stays the enemy's steel (dark ink pool, dull tint). Glints run on
    /// the edges for the thickened patch's renewed life; Expired = the blades sink back into the pool one after another, sparking; any other end = at once.</summary>
    public sealed class Vol2FsRxMetalMetal : Vol2FsReact
    {
        sealed class Blade { public Transform T; public Vector3 Base, Axis; public float H, At, Spin; public bool Flashed; }
        readonly List<Blade> _blades = new List<Blade>();
        readonly Vol2FsStrips _flash = new Vol2FsStrips(), _ink = new Vol2FsStrips();
        Mesh _flashMesh, _inkMesh; ParticleSystem _sparks, _glint; Vol2FsRxSameFx _fx;
        List<Vol2FsB1.Stroke> _splash; bool _sinkSparks;

        protected override void OnBuild()
        {
            _fx = new Vol2FsRxSameFx(Kit, Root.transform, AccA, 5f);
            var steel = Kit.Metal(1.05f, 0f, new Color(0.012f, 0.008f, 0.016f), new Color(0.82f, 0.78f, 0.88f)) ?? Vol2FsB1.Lit(Kit, "fs_steel", new Color(0.7f, 0.7f, 0.75f), 1f, 0.8f);
            var spike = Kit.SharedMesh("b8spike", () => Vol2FsB6.Spike(0.22f, 0.045f, 0.3f, 0.12f));
            void Add(Vector3 b, Vector3 axis, float h, float at, int k)
            {
                var r = Vol2FsB1.Prop(Root.transform, "SameBlade" + k, spike, steel);
                r.transform.position = b;
                float spin = Vol2Fs.Hash(Seed, k + 300) * 180f;
                r.transform.rotation = Quaternion.FromToRotation(Vector3.up, axis) * Quaternion.Euler(0f, spin, 0f);
                r.transform.localScale = new Vector3(2f, 0.001f, 2f);
                _blades.Add(new Blade { T = r.transform, Base = b, Axis = axis, H = h, At = at, Spin = spin });
            }
            int n = 0;
            // the doubled edges: beside every blade of the enemy's patch, a parallel new one (a little taller)
            if (Patch != null)
                for (int i = 0; i < Patch.Props.Count; i++)
                {
                    var pr = Patch.Props[i];
                    if (pr == null || !pr.name.StartsWith("Blade")) continue;
                    Vector3 axis = pr.rotation * Vector3.up, b = pr.position;
                    Vector3 off = Vector3.Cross(axis, Vector3.up); off = off.sqrMagnitude > 1e-4f ? off.normalized : Side;
                    Add(b + off * 0.2f, axis, pr.lossyScale.y * 1.6f + 0.25f, 0.01f * n, n); n++;      // r2: x1.6, wider (r1 thin)
                }
            // the wider ring: taller blades leaning out
            for (int k = 0; k < 18; k++)
            {
                float a = (k / 18f + 0.05f * Vol2Fs.Hash(Seed, k)) * Mathf.PI * 2f, r = Radius * (1.2f + 1.3f * Vol2Fs.Hash(Seed, k + 10));
                Vector3 o = Dir * Mathf.Cos(a) + Side * Mathf.Sin(a);
                float h = 1.5f + 1.2f * Vol2Fs.Hash(Seed, k + 20);
                Add(G(P + o * r) + Vector3.down * 0.05f, (Vector3.up + o * (0.3f + 0.35f * Vol2Fs.Hash(Seed, k + 30))).normalized, h, 0.03f + 0.009f * k, 100 + k);
            }
            Vol2Fs.MeshObject(Root.transform, "SameRing", Vol2FsB4A.PhantomMat(Kit, AccA, 3.2f), out _flashMesh);
            Vol2Fs.MeshObject(Root.transform, "SameInk", Kit.InkSolid("fs_stroke2", InkC, new Color(0.35f, 0.2f, 0.45f), 0.12f, 0.45f, 0.3f, 1.2f), out _inkMesh);
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.88f, 0.6f), 5f, 1f)), Life0 = 0.18f, Life1 = 0.45f, Speed0 = 4f, Speed1 = 11f, Size0 = 0.03f, Size1 = 0.065f, Gravity = 1.5f, Drag = 0.9f, Stretch = true, StretchVel = 0.06f, StretchLen = 1.6f, Max = 900 }, Root.transform, "SameSparks");
            _glint = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.95f, 0.8f), 6f, 1f)), Life0 = 0.12f, Life1 = 0.22f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.22f, Size1 = 0.4f, SizeStart = 0.2f, SizePeak = 1f, SizePeakAt = 0.3f, SizeEnd = 0f, Max = 60 }, Root.transform, "SameGlint");
            foreach (var ps in new[] { _sparks, _glint }) ps.Play(false);
            _splash = Vol2FsB1.Burst(Seed, 20, Side, Dir, 1.1f, 2.6f, 0.12f, 0.28f, 0.3f, 0.2f);
            for (int k = 0; k < 260; k++) Vol2FsB1.EmitV(_sparks, P + Vector3.up * 0.7f - Dir * Radius * 0.4f, (Random.onUnitSphere + Vector3.up * 0.6f).normalized * Random.Range(5f, 14f), -1f, -1f);
            HitStop(0f, 0.07f);
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            float pop = Vol2FsRxSameFx.Pop(t, 0.2f), exit = Vol2FsRxSameFx.Exit(t, ContactEndAt, ContactWhy, 1.0f), near = Vol2FsRxSameFx.Near(t, ContactExpireRel, 2f);
            bool expired = ContactWhy == Vol2MaterialEnd.Expired;
            _flash.Clear();
            for (int i = 0; i < _blades.Count; i++)
            {
                var b = _blades[i];
                if (b.T == null) continue;
                // each blade shoots up in 0.08 s (very fast), rings with a vertical flash; Expired: they sink back one after another
                float up = Vol2FsRxSameFx.Pop(t - b.At, 0.08f);
                float down = expired ? Vol2Fs.Smooth(ContactEndAt + 0.04f * (i % 9), ContactEndAt + 0.04f * (i % 9) + 0.5f, t) : exit;
                float s = Mathf.Max(0.001f, Mathf.Min(up, 1.06f) * (1f - (ContactEndAt >= 0f ? down : 0f)));
                b.T.localScale = new Vector3(2f, b.H * s, 2f);
                Vector3 tip = b.Base + b.Axis * b.H * s;
                if (!b.Flashed && up > 0.5f)
                {
                    b.Flashed = true;
                    for (int k = 0; k < 10; k++) Vol2FsB1.EmitV(_sparks, tip, (b.Axis + Random.insideUnitSphere * 0.8f).normalized * Random.Range(3f, 8f), -1f, -1f);
                }
                // the ring of the new edge: a white-gold streak running up the blade, fading 0.25 s
                float ring = t - b.At;
                if (ring > 0f && ring < 0.3f)
                {
                    float life = 1f - Vol2Fs.Smooth(0.05f, 0.3f, ring);
                    _flash.Strip(new[] { b.Base, b.Base + b.Axis * b.H * 0.5f, tip + b.Axis * 0.15f }, new[] { 0.04f, 0.15f, 0.02f }, null, Color.white, new[] { life, life, life }, 0.3f + 0.01f * i, cam);
                }
            }
            if (exit > 0f && !_sinkSparks) { _sinkSparks = true; for (int k = 0; k < 80; k++) Vol2FsB1.EmitV(_sparks, G(P) + Vector3.up * 0.3f + Random.insideUnitSphere * Radius * 2.5f, (Random.onUnitSphere + Vector3.up).normalized * Random.Range(2f, 6f), -1f, -1f); }
            // glints running on the edges for the renewed life (a little faster toward its end)
            if (exit <= 0f && t > 0.35f && _blades.Count > 0 && Random.value < 0.18f + 0.3f * near)
            {
                var b = _blades[Random.Range(0, _blades.Count)];
                if (b.T != null) Vol2FsB1.EmitV(_glint, b.Base + b.Axis * b.H * Random.Range(0.4f, 0.95f), Vector3.zero, -1f, -1f);
            }
            // the ink splash at the foot of the thicket (0 - 0.8 s)
            _ink.Clear();
            if (t < 1.2f) Vol2FsB1.DrawBurst(_ink, _splash, G(P) + Vector3.up * 0.25f, t, cam, 0.5f, 0.1f);
            _ink.Write(_inkMesh);
            _flash.Write(_flashMesh);
            if (Patch != null) Patch.Pose((1f + 0.35f * pop) * (1f - 0.95f * exit), exit * 0.3f, 1f + 0.4f * pop);
            _fx.Draw(t, P, Dir, Ground, Radius, Radius * 2.6f, cam, 2.2f);
        }
    }
}
