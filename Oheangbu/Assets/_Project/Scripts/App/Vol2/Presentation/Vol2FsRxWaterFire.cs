using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>수>화 김 (꺾음: water overcomes fire) - free showcase batch 8, concept af_a (the water hits the enemy's fire: the flames die, the burning bed breaks into
    /// wet dark islands, and from BETWEEN them sparse tall plumes of steam shoot up, scalding the bear and veiling it for a moment). Facts only: the commit consumes
    /// the shape and the enemy's fire patch (no child); the product is ONE damage request (OnDamageTick k = 0: the scald) and Occl (0.3 s) of sight cover - the veil
    /// round the bear lives that long. Beat (plan_v03 김): 0.5 s rise -> ~1.2 s hold -> gone; sparse separate plumes, no ring of steam.</summary>
    public sealed class Vol2FsRxWaterFire : Vol2FsReact
    {
        static readonly Color Teal = new Color(0.3f, 0.78f, 0.9f), Steam = new Color(0.72f, 0.75f, 0.79f);      // r2: soft grey steam (r1 white popcorn)
        Material _wet, _wetCoal;
        readonly List<ParticleSystem> _plumes = new List<ParticleSystem>();
        readonly List<Transform> _islands = new List<Transform>();
        ParticleSystem _splash, _drops, _embers, _veil, _scald;
        Mesh _inkMesh, _ringMesh; readonly Vol2FsStrips _ink = new Vol2FsStrips(), _ring = new Vol2FsStrips();
        List<Vol2FsB1.Stroke> _burst;
        Light _light; bool _doused, _stopped;
        float _scaldAt = -1f;

        protected override float Burst => 2.3f;

        protected override void OnBuild()
        {
            var root = Root.transform;
            _wet = Kit.Custom("rxbreak_wetbed", () =>
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Fs_RxWetBed" };
                m.SetColor("_BaseColor", new Color(0.035f, 0.04f, 0.05f)); m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.9f);
                return m;
            });
            _wetCoal = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.1f, 0.1f, 0.11f), 0f, 0.6f);
            // the wet dark islands the fire bed breaks into
            for (int k = 0; k < 6; k++)
            {
                float a = (k / 6f + 0.12f * Vol2Fs.Hash(Seed, k)) * Mathf.PI * 2f, r = Radius * (0.5f + 1.1f * Vol2Fs.Hash(Seed, k + 10));
                var isl = Vol2FsB1.Prop(root, "Island" + k, Vol2FsB7.PoolMesh(Kit, (Seed + k * 7) % 97), _wet, false).transform;
                isl.position = G(P) + (Dir * Mathf.Cos(a) + Side * Mathf.Sin(a)) * r + Vector3.up * 0.015f;
                float s = Radius * (0.45f + 0.3f * Vol2Fs.Hash(Seed, k + 20)); isl.localScale = new Vector3(0.01f, 1f, 0.01f); isl.rotation = Quaternion.Euler(0f, a * 57f, 0f);
                _islands.Add(isl); isl.gameObject.name = "Island" + k + "_" + s.ToString("0.00");
            }
            var puff = M("fs_puff", Steam, 0.9f, 0.1f); puff.Opacity = 0.42f; puff.NoiseAmt = 0.3f; puff.Erode = 0.5f; puff.Soft = 0.8f; puff.Scroll = new Vector2(0f, -0.9f); puff.Scroll = new Vector2(0f, -0.6f);
            var steamMat = Kit.Mat(puff);
            // four sparse plumes between the islands (+ one by the bear when there is one)
            var spots = new List<Vector3>();
            // r3: four plumes in a row across the view (side to side, 1.1 R apart) so they stand SEPARATE from the shoulder (r2: round the patch they merged into one cluster)
            foreach (float off in new[] { -1.65f, -0.55f, 0.55f, 1.65f }) spots.Add(G(P) + Side * off * Radius + Dir * (Vol2Fs.Hash(Seed, (int)(off * 10f) + 30) - 0.5f) * Radius * 0.8f);
            for (int k = 0; k < spots.Count; k++)
            {
                float h = 1f + 0.4f * Vol2Fs.Hash(Seed, k + 60);
                var ps = Vol2Fs.Build(new Vol2FsPs { Mat = steamMat, Loop = true, Rate = 0f, Aspect = 2.2f, RandRot = 12f, Life0 = 1.0f * h, Life1 = 1.35f * h, Speed0 = 0.1f, Speed1 = 0.3f, Size0 = 0.75f, Size1 = 1.05f, Radius = 0.06f, Shape = ParticleSystemShapeType.Sphere,
                    VelOverLife = new Vector3(0f, 2.3f * h, 0f), SizeStart = 0.35f, SizePeak = 1f, SizePeakAt = 0.45f, SizeEnd = 1.7f, Noise = 0.12f, NoiseFreq = 0.4f, AlphaHoldTo = 0.35f, Max = 60 }, root, "Plume" + k);
                ps.transform.position = spots[k] + Vector3.up * 0.05f;
                ps.Play(false); _plumes.Add(ps);
            }
            _splash = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Water(Kit, 1.5f, 0.4f, "fs_splash"), Life0 = 0.4f, Life1 = 0.85f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.22f, Size1 = 0.5f, Gravity = 1.7f, Drag = 0.5f, Max = 400 }, root, "Splash");
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Teal, 2.6f, 0.7f)), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.025f, Size1 = 0.05f, Gravity = 2.6f, Stretch = true, StretchVel = 0.06f, StretchLen = 1.2f, Max = 500 }, root, "Drops");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.5f, 0.18f), 4f, 1f)), Life0 = 0.3f, Life1 = 0.8f, Speed0 = 1f, Speed1 = 4f, Size0 = 0.02f, Size1 = 0.05f, Gravity = -0.3f, Drag = 1.2f, Max = 200 }, root, "DyingEmbers");
            var veil = M("fs_puff", Steam, 0.95f, 0.08f); veil.Opacity = 0.62f; veil.NoiseAmt = 0.55f; veil.Erode = 0.35f;
            _veil = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(veil), Life0 = Mathf.Max(0.2f, Occl) + 0.25f, Life1 = Mathf.Max(0.2f, Occl) + 0.45f, Speed0 = 0.2f, Speed1 = 0.8f, Size0 = 0.8f, Size1 = 1.3f, SizeStart = 0.7f, SizePeak = 1.1f, SizeEnd = 1.5f, Radius = 0.35f, VelOverLife = new Vector3(0f, 0.6f, 0f), AlphaHoldTo = 0.55f, Max = 60 }, root, "Veil");
            _scald = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.55f, 0.35f), 1.3f, 1f)), Life0 = 0.15f, Life1 = 0.3f, Speed0 = 0f, Speed1 = 0.2f, Size0 = 0.7f, Size1 = 0.9f, SizeStart = 0.4f, SizePeak = 1f, SizePeakAt = 0.25f, SizeEnd = 0.6f, Max = 10, SortFudge = 10 }, root, "ScaldFlash");
            foreach (var ps in new[] { _splash, _drops, _embers, _veil, _scald }) ps.Play(false);
            Vol2Fs.MeshObject(root, "InkHiss", Kit.InkSolid("fs_stroke2", Vol2FsRxBreakKit.Ink, Teal * 2.2f, 0.15f, 0.5f, 0.28f, 1.5f), out _inkMesh);
            var rm = M("fs_ring", Teal, 1.1f, 0.45f); rm.NoiseAmt = 0.3f; rm.Soft = 0.12f;
            Vol2Fs.MeshObject(root, "WetRing", Kit.Mat(rm), out _ringMesh);
            _burst = Vol2FsB1.Burst(Seed, 14, Side, Dir, 2.4f, 4.0f, 0.3f, 0.52f, 0.5f, 0.2f);
            Vector3 c = G(P) + Vector3.up * 0.3f;
            for (int k = 0; k < 45; k++) Vol2FsB1.EmitV(_splash, c + Random.insideUnitSphere * Radius * 0.4f, (Random.onUnitSphere + Vector3.up * 1.1f).normalized * Random.Range(2f, 6f), -1f, -1f);
            for (int k = 0; k < 90; k++) Vol2FsB1.EmitV(_drops, c + Random.insideUnitSphere * Radius * 0.5f, (Random.onUnitSphere + Vector3.up * 0.9f).normalized * Random.Range(2f, 6.5f), -1f, -1f);
            Vol2FsB1.Emit(_embers, c, 60);
            _light = Vol2Fs.PointLight(root, "Hiss", new Color(0.75f, 0.92f, 1f), 6f); _light.transform.position = c + Vector3.up * 0.5f; _light.intensity = 6f;
            HitStop(0f, 0.07f);
        }

        protected override void OnDamageTick(int k, float t)
        {
            if (k != 0) return;
            _scaldAt = t;
            foreach (var s in Bodies)
            {
                if (!Vol2FsRxBreakKit.Now(s, out var b, out _)) continue;
                Vector3 head = new Vector3(b.center.x, b.max.y - 0.1f, b.center.z);
                Vol2FsB1.Emit(_veil, head, 6);
                Vol2FsB1.EmitV(_scald, b.center, Vector3.zero, -1f, -1f);
                for (int i = 0; i < 30; i++) Vol2FsB1.EmitV(_drops, b.center + Random.insideUnitSphere * 0.3f, (Random.onUnitSphere + Vector3.up * 0.5f).normalized * Random.Range(1.5f, 4f), -1f, -1f);
            }
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            // 0 - 0.15 s: the flames are drowned (their particles stop), the coals go wet and dark; the bed breaks into islands (0.25 s)
            if (!_doused && t > 0.03f && Patch != null)
            {
                _doused = true; Patch.StopParticles();
                foreach (var p in Patch.Props) if (p != null && p.name.StartsWith("Coal")) { var r = p.GetComponent<MeshRenderer>(); if (r != null) r.sharedMaterial = _wetCoal; }
                if (Patch.Pool != null) { var pr = Patch.Pool.GetComponent<MeshRenderer>(); if (pr != null) pr.sharedMaterial = _wet; }
            }
            float split = Vol2Fs.Smooth(0.05f, 0.35f, t), sink = Vol2Fs.Smooth(1.7f, 2.4f, t);
            if (Patch != null) Patch.Pose(1f - 0.5f * split, 0.1f * split + 0.3f * sink, 1f - 0.55f * split);
            for (int i = 0; i < _islands.Count; i++)
            {
                var isl = _islands[i]; if (isl == null) continue;
                float s = Radius * (0.45f + 0.3f * Vol2Fs.Hash(Seed, i + 20)) * split * (1f - sink);
                isl.localScale = new Vector3(Mathf.Max(0.01f, s), 1f, Mathf.Max(0.01f, s));
            }
            // the plumes: rise 0.5 s, hold ~1.2 s, then stop emitting (gone by ~2.4 s)
            if (!_stopped)
            {
                float rate = t < 0.5f ? Mathf.Lerp(16f, 10f, t / 0.5f) : 8f;
                foreach (var ps in _plumes) { var em = ps.emission; em.rateOverTime = t < 1.4f ? rate : 0f; }
                if (t >= 1.4f) _stopped = true;
            }
            _ink.Clear(); Vol2FsB1.DrawBurst(_ink, _burst, G(P) + Vector3.up * 0.35f, t, cam, 0.3f, 0.1f); _ink.Write(_inkMesh);
            _ring.Clear();
            float rl = 1f - Vol2Fs.Smooth(0.2f, 0.9f, t);
            if (rl > 0.01f) Vol2FsB4A.GroundArc(_ring, P, Ground + 0.02f, Radius * (0.9f + 2.3f * Vol2Fs.Smooth(0f, 0.35f, t)), 0.3f, 0f, Mathf.PI * 2f, rl, 0.4f, cam, 40);
            _ring.Write(_ringMesh);
            if (_light != null) _light.intensity = Mathf.MoveTowards(_light.intensity, _scaldAt >= 0f && t - _scaldAt < 0.15f ? 5f : 0f, Time.deltaTime * 18f);
        }
    }
}
