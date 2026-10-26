using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>우 (water, ㅜ ring, 한 번 · 적신다) - free showcase batch 2, concept u_b (a ring of geyser jets shoots up out of the ground on the 2 m ring round the caster
    /// like a fountain crown, black ink ribbons swirling up round the columns; the bear on the ring is soaked as the water crashes down on it; the columns fall and
    /// (the CSV's end) the water runs down onto the ground in a wet ring with foam). Unlike 오's single rolling wave and 아's orb, it is many vertical columns.</summary>
    public sealed class Vol2FsU : Vol2FsRing
    {
        static readonly Color Ink = new Color(0.02f, 0.03f, 0.04f), Foam = new Color(0.9f, 0.95f, 0.95f);
        ParticleSystem _spray, _drops, _foam, _mist, _drip; Light _light;
        Mesh _colMesh, _ribbonMesh, _wetMesh, _crownMesh;
        readonly Vol2FsStrips _cols = new Vol2FsStrips(), _ribbons = new Vol2FsStrips(), _wet = new Vol2FsStrips(), _crowns = new Vol2FsStrips();
        struct G { public float Ang, H, W, At, Rnd; public bool Burst; }
        readonly List<G> _g = new List<G>();
        const float RiseSeconds = 0.3f, HoldSeconds = 0.35f, FallSeconds = 0.5f;
        protected override float LookSeconds => ExpandSeconds + RiseSeconds + HoldSeconds + FallSeconds + 0.6f;

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsB1.M(tex, tint, intensity, addMix);

        protected override void OnBegin()
        {
            var root = Root.transform;
            Vol2Fs.MeshObject(root, "Geysers", Vol2FsB1.Water(Kit, 1.8f, 0.35f), out _colMesh);
            Vol2Fs.MeshObject(root, "InkRibbons", Kit.InkSolid("fs_stroke2", Ink, new Color(0.5f, 0.9f, 1f, 1f) * 0.8f, 0.05f, 0.45f, 0.26f, 1.4f), out _ribbonMesh);
            Vol2Fs.MeshObject(root, "Crowns", Vol2FsB1.Water(Kit, 1.7f, 0.35f, "fs_splash"), out _crownMesh);
            var wet = M("fs_wet", new Color(0.05f, 0.04f, 0.04f, 1f), 1f, 0f); wet.Soft = 0.001f; wet.Opacity = 0.7f; wet.NoiseAmt = 0.3f;
            Vol2Fs.MeshObject(root, "WetRing", Kit.Mat(wet), out _wetMesh);
            var water = M("fs_splash", Color.white, 1.6f, 0.3f); water.Ramp = "fs_ramp_water"; water.RampScale = 1.5f; water.NoiseAmt = 0.4f; water.Soft = 0.1f;
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(water), Life0 = 0.5f, Life1 = 1.1f, Speed0 = 1f, Speed1 = 4f, Size0 = 0.35f, Size1 = 0.9f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.2f, Gravity = 1.6f, Drag = 0.5f, Tiles = 2,
                SizeStart = 0.4f, SizePeak = 1.2f, SizeEnd = 0.8f, Spin = 120f, Max = 1400 }, root, "Spray");
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.8f, 0.95f, 1f), 2.2f, 0.6f)), Life0 = 0.6f, Life1 = 1.4f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.03f, Size1 = 0.08f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.1f, Gravity = 1.8f,
                Stretch = true, StretchVel = 0.03f, Max = 2000 }, root, "Drops");
            var foam = M("fs_puff", Foam, 1.3f, 0.2f); foam.Opacity = 0.75f; foam.Erode = 0.4f; foam.NoiseAmt = 0.6f;
            _foam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(foam), Life0 = 0.5f, Life1 = 1.1f, Speed0 = 0.5f, Speed1 = 2f, Size0 = 0.3f, Size1 = 0.7f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.2f, Drag = 1.5f, Tiles = 2, Max = 600 }, root, "Foam");
            var mist = M("fs_puff", new Color(0.8f, 0.88f, 0.9f, 1f), 1.1f, 0.2f); mist.Opacity = 0.35f; mist.Erode = 0.4f;
            _mist = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mist), Life0 = 1.0f, Life1 = 1.8f, Speed0 = 0.3f, Speed1 = 1f, Size0 = 0.8f, Size1 = 1.6f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.3f, Drag = 1.5f, Tiles = 2, Max = 120, SortFudge = 8 }, root, "Mist");
            _drip = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(water), Rate = 0f, Loop = true, Life0 = 0.4f, Life1 = 0.8f, Speed0 = 0f, Speed1 = 0.4f, Size0 = 0.1f, Size1 = 0.22f, Radius = 0.5f, Gravity = 2f, Tiles = 2, Stretch = true, StretchVel = 0.08f, Max = 300 }, root, "Drip");
            foreach (var ps in new[] { _spray, _drops, _foam, _mist, _drip }) ps.Play(false);
            _light = Vol2Fs.PointLight(root, "WaterLight", new Color(0.6f, 0.85f, 1f), 7f);
        }

        protected override void OnLaunch(long nowUs)
        {
            const int N = 14;
            for (int k = 0; k < N; k++)
            {
                float ang = Vol2FsB2.Angle(k, N, Seed, 0.5f), back = Back(ang);
                _g.Add(new G { Ang = ang, H = Mathf.Lerp(2.2f, 3.4f, Vol2Fs.Hash(Seed, k + 20)) * (1f - 0.7f * back), W = Mathf.Lerp(0.45f, 0.7f, Vol2Fs.Hash(Seed, k + 30)) * (1f - 0.3f * back), At = ExpandSeconds * Vol2Fs.Hash(Seed, k + 40), Rnd = Vol2Fs.Hash(Seed, k + 50) });
            }
            _light.transform.position = Apex + F * R + Vector3.up * 1.4f;
        }

        protected override void OnStruck(Struck s) { }

        protected override void OnRing(float t, float a, bool ended)
        {
            var cam = CamPos;
            _cols.Clear(); _ribbons.Clear(); _crowns.Clear();
            for (int k = 0; k < _g.Count; k++)
            {
                var g = _g[k]; float s = a - g.At; if (s <= 0f) continue;
                float rise = Vol2Fs.EaseOutBack(Mathf.Clamp01(s / RiseSeconds)), fall = Vol2Fs.Smooth(RiseSeconds + HoldSeconds, RiseSeconds + HoldSeconds + FallSeconds, s);
                float h = g.H * rise * (1f - fall), life = 1f - Vol2Fs.Smooth(RiseSeconds + HoldSeconds + FallSeconds * 0.6f, RiseSeconds + HoldSeconds + FallSeconds + 0.1f, s);
                Vector3 b = OnBand(g.Ang, R), outw = Dir(g.Ang), side = Vector3.Cross(Vector3.up, outw);
                if (!g.Burst) { g.Burst = true; _g[k] = g; Vol2FsB1.Emit(_foam, b, 8); Vol2FsB1.Emit(_spray, b + Vector3.up * 0.3f, 8); Vol2FsB1.Emit(_mist, b + Vector3.up * 0.8f, 1); }
                if (life <= 0.01f || h < 0.05f) continue;
                // the column (a water sheet facing the eye), wobbling, its top bursting into a crown of spray
                var pts = new Vector3[8]; var w = new float[8]; var al = new float[8];
                for (int j = 0; j < 8; j++)
                {
                    float u = j / 7f;
                    pts[j] = b + Vector3.up * h * u + side * Mathf.Sin(u * 5f + t * 9f + g.Rnd * 6f) * 0.08f + outw * 0.15f * u * u;
                    w[j] = g.W * (1.1f - 0.45f * u) * (1f + 0.25f * fall); al[j] = life * (0.85f - 0.25f * u);
                }
                _cols.Strip(pts, w, null, Color.white, al, g.Rnd, cam, 0f, 1f);
                // the crown at the top: four sheets curling out and down
                Vector3 top = pts[7];
                for (int c = 0; c < 4; c++)
                {
                    float ca = c * Mathf.PI * 0.5f + g.Rnd * 3f; Vector3 d = (outw * Mathf.Cos(ca) + side * Mathf.Sin(ca)).normalized;
                    var cp = new[] { top, top + d * 0.35f + Vector3.up * 0.25f, top + d * 0.7f + Vector3.down * 0.2f * (1f + fall) };
                    _crowns.Strip(cp, new[] { 0.3f, 0.26f, 0.08f }, null, Color.white, new[] { life, life * 0.8f, life * 0.3f }, g.Rnd + c * 0.1f, cam, 0f, 1f);
                }
                if ((k + Time.frameCount) % 2 == 0) { Vol2FsB1.EmitV(_drops, top, (Random.onUnitSphere * 2f + Vector3.up * 2.5f + outw * 1.5f)); Vol2FsB1.EmitV(_spray, top, Random.onUnitSphere * 1.2f + Vector3.up * 0.5f, -1f, 0.6f); }
                if (fall > 0f && (k + Time.frameCount) % 2 == 0) Vol2FsB1.EmitV(_spray, top, Vector3.down * 2f + outw * 0.5f, -1f, 0.6f);
                // an ink ribbon spiralling up round the column (front and sides)
                if (Back(g.Ang) < 0.4f && k % 2 == 0)
                {
                    var rp = new Vector3[10]; var rw = new float[10]; var ra = new float[10];
                    for (int j = 0; j < 10; j++)
                    {
                        float u = j / 9f, sp = u * 7f + t * 6f + g.Rnd * 6f;
                        rp[j] = b + Vector3.up * (h * 1.05f * u) + (side * Mathf.Cos(sp) + outw * Mathf.Sin(sp)) * (0.32f + 0.25f * u);
                        rw[j] = 0.13f * Mathf.Sin(u * Mathf.PI) + 0.02f; ra[j] = life;
                    }
                    _ribbons.Strip(rp, rw, null, Color.white, ra, g.Rnd * 0.7f + 0.2f, cam, 0f, 1f);
                }
            }
            _cols.Write(_colMesh); _ribbons.Write(_ribbonMesh); _crowns.Write(_crownMesh);
            // the wet ring the water runs down into
            _wet.Clear();
            float wl = Vol2Fs.Smooth(ExpandSeconds, ExpandSeconds + 0.5f, a) * (1f - Vol2Fs.Smooth(LookSeconds, LookSeconds + 1.5f, a));
            if (wl > 0.01f) Vol2FsB2.GroundRing(_wet, Apex, F, Ground, Inner - 0.6f * wl, Outer + 0.9f * wl, 64, null, 0.29f, cam, 0.02f, 0f, 5f);
            _wet.Write(_wetMesh);
            float fallAll = Vol2Fs.Smooth(ExpandSeconds + RiseSeconds + HoldSeconds, ExpandSeconds + RiseSeconds + HoldSeconds + FallSeconds, a);
            if (fallAll > 0.05f && fallAll < 0.95f && Time.frameCount % 2 == 0) for (int k = 0; k < 3; k++) { float ang = Random.Range(-Mathf.PI, Mathf.PI); Vol2FsB1.Emit(_foam, OnBand(ang, R + Random.Range(-0.3f, 0.6f)), 1); }
            _light.intensity = 4.5f * Vol2Fs.Smooth(0f, ExpandSeconds, a) * (1f - fallAll);
            // the struck bodies: the water crashes down on them; they drip (the 4 s soak, a mirror of the rules) for as long as the look stands
            foreach (var s in Hits)
            {
                float sa = a - s.At; if (sa < 0f) continue;
                var c = CentreNow(s);
                if (!s.Started)
                {
                    s.Started = true;
                    for (int k = 0; k < 120; k++) Vol2FsB1.EmitV(_spray, c + Vector3.up * Random.Range(0.6f, 1.6f) + Random.insideUnitSphere * 0.4f, Vector3.down * Random.Range(1f, 4f) + Random.insideUnitSphere, -1f, Random.Range(0.4f, 0.8f));
                    Vol2FsB1.Emit(_foam, FeetNow(s), 30); Vol2FsB1.Emit(_drops, c, 80);
                    var em = _drip.emission; em.rateOverTime = 45f;
                }
                _drip.transform.position = c + Vector3.up * 0.3f;
                if (sa > 4f) { var em = _drip.emission; em.rateOverTime = 0f; }
            }
        }
    }
}
