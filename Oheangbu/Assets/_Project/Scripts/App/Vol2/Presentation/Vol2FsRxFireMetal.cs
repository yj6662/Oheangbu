using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>화>금 달군 쇠 (꺾음: fire overcomes metal) - free showcase batch 8, concept fm_b (the enemy's blade thicket turns forge-red, curling ribbons of fire
    /// and ink burst round it, the ground under it glows; heat pulses scorch the bear). Facts only: the commit consumes the shape and the enemy's metal patch (no
    /// child); the product is the LASTING damage - every real tick of the ledger (OnDamageTick, 4 x 0.5 s for Dur 2 s) is one heat pulse from the blades to the
    /// bear; when the damage has run out the iron cools dark and sinks.</summary>
    public sealed class Vol2FsRxFireMetal : Vol2FsReact
    {
        static readonly Color Orange = new Color(1f, 0.32f, 0.07f), Rim = new Color(1.7f, 0.36f, 0.06f, 1f);      // r2: a dark-red rim (r1 4 / 1.2 read as yellow paper)
        Mesh _inkMesh, _glowMesh, _waveMesh;
        readonly Vol2FsStrips _ink = new Vol2FsStrips(), _glow = new Vol2FsStrips(), _wave = new Vol2FsStrips();
        List<Vol2FsB1.Stroke> _burst;
        ParticleSystem _licks, _embers, _smoke, _sparks;
        Light _light; Vol2FsCutAct _act;
        readonly List<MeshRenderer> _blades = new List<MeshRenderer>();
        Material _hot, _cold; bool _isHot, _isCold;
        readonly List<float> _pulses = new List<float>();

        protected override float Burst => 2.2f;

        protected override void OnBuild()
        {
            var root = Root.transform;
            _hot = Kit.Metal(1.15f, 1f, new Color(1.8f, 0.38f, 0.06f), new Color(1f, 0.5f, 0.36f));
            _cold = Kit.Metal(0.45f, 0f, default, new Color(0.42f, 0.38f, 0.38f));
            if (Patch != null) foreach (var p in Patch.Props) if (p != null && p.name.StartsWith("Blade")) { var r = p.GetComponent<MeshRenderer>(); if (r != null) _blades.Add(r); }
            Vol2Fs.MeshObject(root, "InkFire", Kit.InkSolid("fs_stroke2", Vol2FsRxBreakKit.Ink, Rim, 0.18f, 0.5f, 0.28f, 1.6f), out _inkMesh);
            var glow = M("fs_dot", Orange, 2.2f, 1f); glow.Soft = 0.6f; glow.NoiseAmt = 0.35f;
            Vol2Fs.MeshObject(root, "ForgeGlow", Kit.Mat(glow), out _glowMesh);
            var wave = M("fs_ring", new Color(1f, 0.55f, 0.2f), 2.6f, 1f); wave.NoiseAmt = 0.3f; wave.Soft = 0.1f;
            Vol2Fs.MeshObject(root, "HeatWave", Kit.Mat(wave), out _waveMesh);
            _burst = Vol2FsB1.Burst(Seed, 14, Side, Dir, 2.8f, 4.8f, 0.3f, 0.6f, 0.75f, 0.15f);
            var licks = Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick(1.45f, 0.6f, 1.1f)), 0.45f, 0.95f, 0.5f, 0.95f, 1.6f, 600);
            licks.Loop = true; licks.Rate = 0f; licks.Radius = Radius * 1.05f; licks.ShapeScale = new Vector3(1f, 0.15f, 1f);
            _licks = Vol2Fs.Build(licks, root, "Licks"); _licks.transform.position = G(P) + Vector3.up * 0.2f;
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.55f, 0.18f), 4f, 1f)), Loop = true, Life0 = 0.7f, Life1 = 1.6f, Speed0 = 0.5f, Speed1 = 2.2f, Size0 = 0.025f, Size1 = 0.06f, Gravity = -0.5f, Drag = 0.6f, Noise = 1f, NoiseFreq = 1.5f, Radius = Radius * 0.8f, ShapeScale = new Vector3(1f, 0.3f, 1f), Max = 500 }, root, "Embers");
            _embers.transform.position = G(P) + Vector3.up * 0.3f;
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsPolish.Soot(0.45f)), Life0 = 1.2f, Life1 = 2f, Speed0 = 0.3f, Speed1 = 0.8f, Size0 = 0.5f, Size1 = 1.1f, Radius = Radius * 0.6f, ShapeScale = new Vector3(1f, 0.2f, 1f), VelOverLife = new Vector3(0f, 0.9f, 0f), SizeStart = 0.5f, SizePeak = 1f, SizeEnd = 1.8f, Max = 120 }, root, "Smoke");
            _smoke.transform.position = G(P) + Vector3.up * 0.5f;
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.75f, 0.35f), 5f, 1f)), Life0 = 0.25f, Life1 = 0.6f, Speed0 = 4f, Speed1 = 11f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.2f, Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.4f, Max = 600 }, root, "Sparks");
            foreach (var ps in new[] { _licks, _embers, _smoke, _sparks }) ps.Play(false);
            var em = _licks.emission; em.rateOverTime = 120f * Radius;
            var ee = _embers.emission; ee.rateOverTime = 60f;
            Vector3 c = G(P) + Vector3.up * 0.5f;
            for (int k = 0; k < 160; k++) Vol2FsB1.EmitV(_sparks, c + Random.insideUnitSphere * 0.3f, (Random.onUnitSphere + Vector3.up * 0.6f).normalized * Random.Range(4f, 11f), -1f, -1f);
            _light = Vol2Fs.PointLight(root, "Forge", new Color(1f, 0.45f, 0.15f), 7f); _light.transform.position = c + Vector3.up * 0.4f; _light.intensity = 10f;
            _act = new Vol2FsCutAct(Kit, root, Body.Fire, Seed, AccA);
            HitStop(0f, 0.08f);
        }

        protected override void OnDamageTick(int k, float t)
        {
            _pulses.Add(t);
            if (_light != null) _light.intensity = 9f;
            foreach (var s in Bodies)
            {
                if (!Vol2FsRxBreakKit.Now(s, out var b, out var feet)) continue;
                if (k == 0 && s.T != null) _act.Begin(s.T, b, feet, t, s.Dir);
                for (int i = 0; i < 40; i++) Vol2FsB1.EmitV(_sparks, b.center + Random.insideUnitSphere * 0.3f, (Random.onUnitSphere + Vector3.up * 0.5f).normalized * Random.Range(2.5f, 7f), -1f, -1f);
                // r2: the heat reads ON the bear - tongues lick up its body on every tick
                for (int i = 0; i < 14; i++) Vol2FsB1.EmitV(_licks, new Vector3(b.center.x, b.min.y + 0.1f, b.center.z) + Vol2FsRxBreakKit.Flat(Random.insideUnitSphere) * Mathf.Max(b.extents.x, b.extents.z) * 0.9f, Vector3.up * Random.Range(1f, 2.4f), -1f, -1f);
            }
            for (int i = 0; i < 40; i++) Vol2FsB1.EmitV(_embers, G(P) + Vector3.up * 0.4f + Random.insideUnitSphere * Radius * 0.6f, Vector3.up * Random.Range(1f, 3f) + Random.insideUnitSphere, -1f, -1f);
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            // 0.15 s: the blades flush forge-red; 0.4 s: their edges blunt (the tips shrink); after the damage has run out (Dur) they cool dark and sink
            if (!_isHot && t > 0.06f && _hot != null) { _isHot = true; foreach (var r in _blades) if (r != null) r.sharedMaterial = _hot; }
            if (!_isCold && t > Dur + 0.25f && _cold != null) { _isCold = true; foreach (var r in _blades) if (r != null) r.sharedMaterial = _cold; }
            if (Patch != null)
            {
                float blunt = Vol2Fs.Smooth(0.15f, 0.4f, t), sag = Vol2Fs.Smooth(Dur + 0.3f, Dur + 1.3f, t);
                for (int i = 0; i < Patch.Props.Count; i++)
                {
                    var tr = Patch.Props[i]; if (tr == null || !tr.name.StartsWith("Blade")) continue;
                    Vector3 s0 = Patch.RestScale[i];
                    float big = 1f + 0.6f * Vol2Fs.Smooth(0f, 0.12f, t);      // r2: the heated thicket swells (r1 the blades hid under the fire)
                    tr.localScale = new Vector3(s0.x * big * (1f + 0.3f * blunt), s0.y * big * (1f - 0.22f * blunt - 0.3f * sag), s0.z * big * (1f + 0.3f * blunt));
                }
                Patch.Pose(1f, 0.55f * sag, 1f);
            }
            if (t > Dur + 0.1f) { var em = _licks.emission; em.rateOverTime = 0f; var ee = _embers.emission; ee.rateOverTime = 0f; }
            if (t > Dur + 0.25f && t < Dur + 0.35f) { var sm = _smoke.emission; sm.rateOverTime = 0f; Vol2FsB1.Emit(_smoke, G(P) + Vector3.up * 0.4f, 18); }
            // the glowing forge bed under the blades (pulses with every tick)
            _glow.Clear();
            float heat = Vol2Fs.Smooth(0f, 0.12f, t) * (1f - Vol2Fs.Smooth(Dur, Dur + 0.9f, t));
            float pulse = 0f; foreach (float p in _pulses) pulse = Mathf.Max(pulse, 1f - Vol2Fs.Smooth(0f, 0.35f, t - p));
            if (heat > 0.01f) _glow.Quad(G(P) + Vector3.up * 0.04f, Side, Dir, Radius * (4.2f + 1.0f * pulse), new Color(1f, 1f, 1f, heat * (0.75f + 0.25f * pulse)), 0.3f);
            _glow.Write(_glowMesh);
            // a heat wave runs from the blades to each body on every real tick
            _wave.Clear();
            foreach (float p in _pulses)
            {
                float a = t - p; if (a < 0f || a > 0.45f) continue;
                float u = Vol2Fs.Smooth(0f, 0.3f, a), life = 1f - Vol2Fs.Smooth(0.2f, 0.45f, a);
                Vol2FsB4A.GroundArc(_wave, P, Ground + 0.03f, Radius * (0.8f + 2.6f * u), 0.34f * (1f - 0.5f * u), 0f, Mathf.PI * 2f, life, 0.3f + p, cam, 40);
                foreach (var s in Bodies)
                {
                    if (!Vol2FsRxBreakKit.Now(s, out var b, out _)) continue;
                    Vol2FsB4.Arc(_wave, G(P) + Vector3.up * 0.6f, b.center, 0.15f, Mathf.Max(0f, u - 0.35f), u, 0.3f, life, 0.5f, cam);
                }
            }
            _wave.Write(_waveMesh);
            _ink.Clear(); Vol2FsB1.DrawBurst(_ink, _burst, G(P) + Vector3.up * 0.5f, t, cam, 0.25f, 0.1f); _ink.Write(_inkMesh);
            if (_light != null) _light.intensity = Mathf.MoveTowards(_light.intensity, t < Dur ? 2.5f : 0f, Time.deltaTime * 16f);
            _act.Tick(t, cam, Ground);
        }
    }
}
