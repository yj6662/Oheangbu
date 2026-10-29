using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>수>금 옮겨붙음 (water catches onto the enemy's metal) - free showcase batch 8, concept am_a (water floods every seam between the broken enemy blades
    /// and stands in their place as blades of clear water). Facts only: at the commit the enemy's metal patch and the water shape are consumed and a water CHILD
    /// (mine) stands in the patch's footprint for its live 10 s. The look: a burst of spray as the water hits the steel; the enemy's dull blades sink and dissolve
    /// while, at the very same feet (the patch's own blade layout, same seed), taller blades of clear blue-green WATER shoot up - each a tapering sheet with a
    /// bright rim and a thread of ink swirling inside - plus a crown of smaller ones, a wet sheen spreading round them; drips fall from the tips and the blades
    /// ripple, lower over the child's life (monotone) and in its last second. Expired = the blades run down into the ground and leave a wet stain that fades
    /// (CSV 수 끝: 흘러내림); Consumed / ChildConsumed = they dry in place in 0.3 s.</summary>
    public sealed class Vol2FsRxWaterMetal : Vol2FsReact
    {
        static readonly Color Teal = new Color(0.4f, 0.9f, 1f), Foam = new Color(0.85f, 0.97f, 1f);
        sealed class Blade { public Vector3 Foot, Axis, Side; public float H, W, At, Phase; }
        readonly List<Blade> _blades = new List<Blade>();
        Mesh _waterMesh, _rimMesh, _inkMesh, _wetMesh;
        readonly Vol2FsStrips _water = new Vol2FsStrips(), _rim = new Vol2FsStrips(), _inkS = new Vol2FsStrips(), _wet = new Vol2FsStrips();
        ParticleSystem _splash, _spray, _drips, _mist;
        Light _light;

        protected override float Burst => 1.8f;

        protected override void OnBuild()
        {
            var root = Root.transform;
            bool dark = EnemyIgniter;
            var blades = new List<(Vector3 Foot, Vector3 Out, float H, float Lean)>();
            Vol2FsRxTransferKit.MetalBlades(V.ContactId, Radius, blades);
            int k = 0;
            foreach (var b in blades)
            {
                Vector3 axis = (Vector3.up + b.Out * b.Lean * 1.2f).normalized;
                // r2: 1.6x wider spread, ~2.7x the old blades' height, wide sheets (r1: thin pale spikes ~1 m)
                _blades.Add(new Blade { Foot = G(P + b.Foot * 1.6f), Axis = axis, Side = Vector3.Cross(axis, b.Out).normalized, H = b.H * 2.7f, W = 0.5f + 0.15f * Vol2Fs.Hash(Seed, k), At = 0.04f + 0.025f * k, Phase = k * 1.3f });
                k++;
            }
            // a crown of smaller water blades round them (between the old ones)
            for (int j = 0; j < 9; j++)
            {
                float a = (j / 9f + 0.05f * Vol2Fs.Hash(Seed, j + 200)) * Mathf.PI * 2f; Vector3 outw = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 axis = (Vector3.up + outw * 0.9f).normalized;
                _blades.Add(new Blade { Foot = G(P + outw * Radius * 1.5f), Axis = axis, Side = Vector3.Cross(axis, outw).normalized, H = 1.0f + 0.7f * Vol2Fs.Hash(Seed, j + 210), W = 0.36f, At = 0.14f + 0.025f * j, Phase = j * 2.1f + 5f });
            }
            // r2: teal water with a foam rim (r1 WaterDeep read white / icy)
            var wm = Vol2FsMat.Make("fs_water", dark ? new Color(0.35f, 0.45f, 0.55f) : new Color(0.45f, 0.95f, 1f), dark ? 0.8f : 1.42f, 0.36f); wm.Ramp = "fs_ramp_water"; wm.RampScale = 1.15f; wm.NoiseAmt = 0.3f; wm.NoiseScale = 1.2f; wm.Erode = 0.1f; wm.Soft = 0.3f;
            wm.Scroll = new Vector2(0f, -1.2f); wm.Edge = new Color(0.6f, 0.95f, 1f, 1f); wm.EdgeWidth = 0.1f; wm.Opacity = 0.95f;
            var water = Kit.Mat(wm);
            Vol2Fs.MeshObject(root, "WaterBlades", water, out _waterMesh);
            var rim = M("fs_streak", dark ? new Color(0.35f, 0.45f, 0.55f) : Foam, dark ? 1.0f : 2.8f, 1f); rim.NoiseAmt = 0.3f; rim.Soft = 0.05f; rim.Scroll = new Vector2(0f, -2f);
            Vol2Fs.MeshObject(root, "BladeRims", Kit.Mat(rim), out _rimMesh);
            Vol2Fs.MeshObject(root, "InkThreads", Kit.InkSolid("fs_stroke2", InkC, Teal * 1.2f, 0.12f, 0.5f, 0.4f, 1.3f), out _inkMesh);
            var wet = M("fs_wet", dark ? new Color(0.08f, 0.08f, 0.12f, 1f) : new Color(0.1f, 0.18f, 0.22f, 1f), 1f, 0f); wet.Opacity = 0.7f; wet.NoiseAmt = 0.3f; wet.Soft = 0.25f;
            Vol2Fs.MeshObject(root, "WetSheen", Kit.Mat(wet), out _wetMesh);
            var sp = Vol2FsMat.Make("fs_splash", new Color(0.4f, 0.85f, 0.95f), 1.2f, 0.35f); sp.NoiseAmt = 0.3f; sp.Soft = 0.2f; sp.Erode = 0.3f;
            _splash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(sp), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.12f, Size1 = 0.28f, Gravity = 1.6f, Drag = 0.6f, Max = 500 }, root, "Splash");
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Foam, 2.6f, 0.7f)), Life0 = 0.3f, Life1 = 0.7f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 2f, Drag = 0.6f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.2f, Max = 700 }, root, "Spray");
            _drips = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Teal, 2.2f, 0.7f)), Life0 = 0.35f, Life1 = 0.6f, Speed0 = 0f, Speed1 = 0.2f, Size0 = 0.025f, Size1 = 0.05f, Gravity = 2.6f, Stretch = true, StretchVel = 0.06f, StretchLen = 1.2f, Max = 400 }, root, "Drips");
            var mist = M("fs_puff", new Color(0.45f, 0.75f, 0.85f), 1f, 0.2f); mist.Opacity = 0.2f; mist.NoiseAmt = 0.6f; mist.Erode = 0.35f;
            _mist = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mist), Life0 = 0.8f, Life1 = 1.5f, Speed0 = 0.3f, Speed1 = 1.2f, Size0 = 0.5f, Size1 = 1f, SizeStart = 0.5f, SizePeak = 1.1f, SizeEnd = 1.5f, Drag = 1.2f, Max = 120 }, root, "Mist");
            foreach (var ps in new[] { _splash, _spray, _drips, _mist }) ps.Play(false);
            _light = Vol2Fs.PointLight(root, "WaterLight", Teal, 6f); _light.transform.position = P + Vector3.up * 1.5f;
            Vector3 hit = G(P - Dir * Radius * 0.5f) + Vector3.up * 0.4f;
            for (int j = 0; j < 30; j++) Vol2FsB1.EmitV(_splash, hit + Random.insideUnitSphere * 0.3f, (Random.onUnitSphere + Vector3.up * 0.9f + Dir * 0.4f).normalized * Random.Range(2f, 6.5f), -1f, -1f);
            for (int j = 0; j < 120; j++) Vol2FsB1.EmitV(_spray, hit, (Random.onUnitSphere + Vector3.up * 0.5f).normalized * Random.Range(3f, 8f), -1f, -1f);
            for (int j = 0; j < 3; j++) Vol2FsB1.EmitV(_mist, G(P) + Random.insideUnitSphere * Radius + Vector3.up * 0.3f, Random.insideUnitSphere + Vector3.up * 0.5f, -1f, -1f);
            HitStop(0f, 0.07f);
        }

        protected override void OnChildEnd(Vol2MaterialEnd why, float t)
        {
            if (!Vol2FsRxTransferKit.Natural(why)) return;
            foreach (var b in _blades) for (int k = 0; k < 4; k++) Vol2FsB1.EmitV(_splash, b.Foot + Vector3.up * 0.15f, (Random.insideUnitSphere + Vector3.up * 0.3f) * 1.5f, -1f, -1f);
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            float exitK = Vol2FsRxTransferKit.Exit01(t, ChildEndAt, ChildWhy);
            bool natural = ChildEndAt >= 0f && Vol2FsRxTransferKit.Natural(ChildWhy);
            float left = HasChild && ChildExpireRel > 0f ? ChildExpireRel - t : 10f;
            float level = Mathf.Lerp(1f, 0.7f, Vol2Fs.Smooth(0.6f, 5f, t)) * Mathf.Lerp(0.55f, 1f, Vol2Fs.Smooth(0f, 1f, left));
            // the enemy's blades sink and dissolve under the water
            if (Patch != null)
            {
                float k = Vol2Fs.Smooth(0.05f, 0.55f, t);
                Patch.Pose(Mathf.Max(0.02f, 1f - k), k * 0.4f, 1f);
                if (t > 0.9f && Patch.Go != null && Patch.Go.activeSelf) Patch.SetActive(false);
            }
            _water.Clear(); _rim.Clear(); _inkS.Clear(); _wet.Clear();
            if (HasChild)
            {
                int n = 9; var pts = new Vector3[n]; var w = new float[n]; var al = new float[n]; var rw = new float[n]; var iw = new float[n];
                foreach (var b in _blades)
                {
                    float a = (t - b.At) / 0.14f;
                    if (a <= 0f) continue;
                    float g = Vol2Fs.EaseOutBack(Mathf.Clamp01(a));
                    float h = b.H * g * level, life = 1f;
                    if (ChildEndAt >= 0f) { if (natural) h *= 1f - exitK; else life = 1f - exitK; }
                    if (h < 0.02f || life < 0.01f) continue;
                    if (a >= 1f && a < 1.12f) for (int k = 0; k < 8; k++) Vol2FsB1.EmitV(_spray, b.Foot + b.Axis * h, (b.Axis + Random.insideUnitSphere * 0.6f) * Random.Range(1.5f, 4f), -1f, -1f);
                    for (int k = 0; k < n; k++)
                    {
                        float u = k / (float)(n - 1);
                        float wav = Mathf.Sin(t * 5f + b.Phase + u * 4f) * 0.05f * u;
                        pts[k] = b.Foot + b.Axis * h * u + b.Side * wav + Vector3.up * 0.02f;
                        float taper = Mathf.Lerp(1f, 0.04f, Mathf.Pow(Mathf.Max(0f, u), 0.9f));
                        w[k] = b.W * taper; rw[k] = 0.11f * taper + 0.02f; iw[k] = 0.05f * taper;
                        al[k] = life * (0.8f + 0.2f * u);
                    }
                    _water.Strip(pts, w, null, Color.white, al, 0.2f + b.Phase * 0.07f, cam);
                    // the bright rim along one edge, the ink thread inside
                    var edge = new Vector3[n]; for (int k = 0; k < n; k++) edge[k] = pts[k] + b.Side * w[k] * 0.42f;
                    _rim.Strip(edge, rw, null, Color.white, al, 0.5f + b.Phase * 0.05f, cam);
                    var thread = new Vector3[n]; for (int k = 0; k < n; k++) thread[k] = pts[k] - b.Side * w[k] * 0.12f * Mathf.Sin(t * 3f + k + b.Phase) + (cam - pts[k]).normalized * 0.02f;
                    _inkS.Strip(thread, iw, null, Color.white, al, 0.8f + b.Phase * 0.03f, cam);
                    if (Random.value < 0.06f * life) Vol2FsB1.EmitV(_drips, pts[n - 2], Vector3.down * 0.3f, -1f, -1f);
                }
            }
            // the wet sheen round the cluster: spreads, stays, fades after the end
            float wetLife = (HasChild ? Vol2Fs.Smooth(0.05f, 0.4f, t) * (natural ? 1f - Vol2Fs.Smooth(0.3f, 1f, exitK) : 1f - exitK) : 1f - Vol2Fs.Smooth(0.3f, 0.6f, t)) * 0.9f;
            if (wetLife > 0.01f) _wet.Quad(G(P) + Vector3.up * 0.025f, Side, Dir, Radius * 4.2f * (0.6f + 0.4f * Vol2Fs.Smooth(0f, 0.5f, t)), new Color(1f, 1f, 1f, wetLife), 0.3f);
            if (natural && exitK < 0.8f && Random.value < 0.5f) Vol2FsB1.EmitV(_splash, G(P) + Random.insideUnitSphere * Radius + Vector3.up * 0.1f, Random.insideUnitSphere + Vector3.up * 0.5f, -1f, -1f);
            _water.Write(_waterMesh); _rim.Write(_rimMesh); _inkS.Write(_inkMesh); _wet.Write(_wetMesh);
            if (_light != null) _light.intensity = (HasChild ? 6f * level * (1f - exitK) : 0f) + (t < 0.15f ? 8f * (1f - t / 0.15f) : 0f);
        }
    }
}
