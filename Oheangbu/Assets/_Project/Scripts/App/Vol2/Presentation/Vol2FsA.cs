using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>아 (water, ㅏ path, 한 번 · 적신다) - free showcase batch 1, concept a_b (a water orb: droplets spiral into a wobbling sphere of water with ink curling
    /// inside at the brush, it is lobbed along the path trailing droplets and mist and bursts over the first bear into a heavy dome of water that collapses and
    /// pours down over it; a splash crown and spray, a dark wet stain with foam spreads on the ground, the soaked body keeps dripping - water runs down).
    /// The wet is shown for its 4 s (the status sink owns it; the look only mirrors it).</summary>
    public sealed class Vol2FsA : Vol2FsShot
    {
        const float WetSeconds = 4f;
        static readonly Color Aqua = new Color(0.6f, 0.9f, 1f), Foam = new Color(0.92f, 0.98f, 1f), Ink = new Color(0.02f, 0.03f, 0.04f);

        Mesh _orbMesh, _domeMesh, _inkMesh, _stainMesh;
        readonly Vol2FsStrips _ink = new Vol2FsStrips(), _stain = new Vol2FsStrips();
        ParticleSystem _inflow, _drops, _mist, _splash, _crown, _foam, _drip; Light _light;
        Vector3 _orbPos; float _orbR = 0.55f;
        Vector3 _domeC; float _domeR;
        struct Tendril { public float A0, Speed, Rad, Len, W; }
        readonly List<Tendril> _tendrils = new List<Tendril>();
        Vector3 _feet0;

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsB1.M(tex, tint, intensity, addMix);
        protected override float LobHeight => 0.75f;
        Vector3 Hold() => Tip() + Fwd * 0.75f + Vector3.up * 0.35f;

        protected override void OnBegin()
        {
            var root = Root.transform;
            var water = Vol2FsB1.Water(Kit, 1.3f, 0.45f);      // r2: clearer (r1 read as ice)
            Vol2Fs.MeshObject(root, "Orb", water, out _orbMesh);
            Vol2Fs.MeshObject(root, "Dome", Vol2FsB1.Water(Kit, 1.0f, 0.6f), out _domeMesh);      // polish: a clearer, see-through dome (r6 read as a faceted ice blob)
            // ink curling INSIDE the water (thin covering strips, no rim)
            var ink = M("fs_stroke2", Ink, 1f, 0f); ink.Soft = 0.05f; ink.NoiseAmt = 0.4f; ink.Opacity = 0.9f; ink.Erode = 0.25f;
            Vol2Fs.MeshObject(root, "InkInside", Kit.Mat(ink), out _inkMesh);
            var wet = M("fs_wet", new Color(0.05f, 0.04f, 0.04f, 1f), 1f, 0f); wet.Soft = 0.001f; wet.NoiseAmt = 0.35f; wet.Opacity = 0.7f; wet.Edge = new Color(0.55f, 0.65f, 0.7f, 1f); wet.EdgeWidth = 0.05f;
            Vol2Fs.MeshObject(root, "WetStain", Kit.Mat(wet), out _stainMesh);
            for (int k = 0; k < 6; k++) _tendrils.Add(new Tendril { A0 = k * 1.05f + Vol2Fs.Hash(Seed, k) * 0.5f, Speed = Mathf.Lerp(3f, 6f, Vol2Fs.Hash(Seed, k + 10)) * (k % 2 == 0 ? 1f : -1f), Rad = Mathf.Lerp(0.25f, 0.6f, Vol2Fs.Hash(Seed, k + 20)), Len = Mathf.Lerp(0.9f, 1.6f, Vol2Fs.Hash(Seed, k + 30)), W = Mathf.Lerp(0.06f, 0.12f, Vol2Fs.Hash(Seed, k + 40)) });
            // droplets spiralling into the brush tip
            var drop = M("fs_dot", Aqua, 2.2f, 0.6f); drop.NoiseAmt = 0.1f;
            _inflow = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Rate = 120f, Loop = true, Life0 = 0.2f, Life1 = 0.3f, Speed0 = -2.5f, Speed1 = -4f, Size0 = 0.04f, Size1 = 0.09f, Radius = 1.1f, Orbital = 6f, World = false, Max = 80 }, root, "Inflow");
            _inflow.transform.position = Hold();
            _inflow.Play(false);
            // shared pools
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.6f, Life1 = 1.3f, Speed0 = 3f, Speed1 = 9f, Size0 = 0.04f, Size1 = 0.12f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.3f, Gravity = 2.2f, Drag = 0.3f,
                Stretch = true, StretchVel = 0.03f, Max = 900 }, root, "Drops");
            var mist = M("fs_puff", new Color(0.85f, 0.93f, 0.97f, 1f), 1f, 0.15f); mist.Opacity = 0.4f; mist.NoiseAmt = 0.6f; mist.Erode = 0.4f;
            _mist = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mist), Life0 = 0.8f, Life1 = 1.6f, Speed0 = 0.3f, Speed1 = 1.5f, Size0 = 0.6f, Size1 = 1.4f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.4f, Drag = 1.2f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.2f, SizeEnd = 1.8f, Max = 120, SortFudge = 10 }, root, "Mist");
            var spl = M("fs_splash", Foam, 0.9f, 0.55f); spl.Opacity = 0.42f;      // polish: lighter spray (icy flecks) spl.NoiseAmt = 0.3f; spl.Erode = 0.25f; spl.Soft = 0.1f;
            _splash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(spl), Life0 = 0.35f, Life1 = 0.7f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.6f, Size1 = 1.5f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.4f, Gravity = 1.6f, Tiles = 2,
                SizeStart = 0.4f, SizePeak = 1.2f, SizePeakAt = 0.4f, SizeEnd = 1.3f, Spin = 60f, Max = 120 }, root, "Splash");
            _crown = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(spl), Life0 = 0.5f, Life1 = 0.8f, Speed0 = 0f, Speed1 = 0f, Size0 = 4.5f, Size1 = 5.2f, Radius = 0.01f, Horizontal = true, SizeStart = 0.15f, SizePeak = 1f, SizePeakAt = 0.55f, SizeEnd = 1.15f, AlphaHoldTo = 0.35f, Max = 4 }, root, "Crown");
            var foam = M("fs_splash", Foam, 0.9f, 0.4f); foam.Opacity = 0.6f; foam.NoiseAmt = 0.5f; foam.Erode = 0.35f; foam.Soft = 0.02f;
            _foam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(foam), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 0.5f, Speed1 = 2.2f, Size0 = 0.5f, Size1 = 1.1f, Shape = ParticleSystemShapeType.Circle, Radius = 0.8f, ShapeRot = new Vector3(90f, 0f, 0f), Drag = 1.5f, Horizontal = true, Tiles = 2,
                SizeStart = 0.6f, SizePeak = 1.1f, SizeEnd = 1.4f, AlphaHoldTo = 0.5f, Max = 120 }, root, "Foam");
            foreach (var ps in new[] { _drops, _mist, _splash, _crown, _foam }) ps.Play(false);
        }

        protected override void OnCharge(float t, float k)
        {
            _orbPos = Hold();
            _inflow.transform.position = _orbPos;
            float r = Mathf.Lerp(0.08f, _orbR, Vol2Fs.Smooth(0f, 1f, k));
            DrawOrb(t, r, Vector3.zero, 1f);
        }

        void DrawOrb(float t, float r, Vector3 vel, float life)
        {
            Vector3 c = _orbPos; Vector3 sd = vel.sqrMagnitude > 1e-4f ? vel.normalized : Vector3.zero;
            Vol2FsB1.WaterSphere(_orbMesh, c, r, -1f, CamPos, life, d =>
            {
                float wob = 1f + 0.09f * Mathf.Sin(t * 17f + d.x * 5f) * Mathf.Sin(t * 13f + d.y * 4f + d.z * 3f);
                Vector3 p = d * wob;
                if (sd != Vector3.zero) { float along = Vector3.Dot(p, sd); p += sd * along * 0.35f - sd * 0.12f; }    // stretched along the flight, a tail
                return p;
            });
            // the ink tendrils curling inside
            _ink.Clear();
            if (life > 0.01f)
                foreach (var td in _tendrils)
                {
                    var pts = new Vector3[8]; var w = new float[8]; var al = new float[8];
                    for (int k = 0; k < 8; k++)
                    {
                        float u = k / 7f, a = td.A0 + t * td.Speed + u * td.Len * 2.5f;
                        pts[k] = c + new Vector3(Mathf.Cos(a), Mathf.Sin(u * 3f + td.A0) * 0.6f, Mathf.Sin(a)) * td.Rad * r * (0.5f + 0.5f * u);
                        w[k] = td.W * r * 2f * Mathf.Sin(u * Mathf.PI); al[k] = life;
                    }
                    _ink.Strip(pts, w, null, Color.white, al, td.A0, CamPos, 0f, 1f);
                }
            _ink.Write(_inkMesh);
        }

        protected override void OnLaunch(long nowUs)
        {
            _inflow.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            From = _orbPos;
            _light = Vol2Fs.PointLight(Root.transform, "Light", new Color(0.6f, 0.85f, 1f), 5f); _light.intensity = 1.5f;
        }

        protected override void OnFly(float t, float f, float prev)
        {
            Vector3 p = Line(f), v = Line(Mathf.Min(1f, f + 0.05f)) - Line(Mathf.Max(0f, f - 0.05f));
            _orbPos = p; _light.transform.position = p;
            DrawOrb(t, _orbR, v, 1f);
            float len = Vector3.Distance(From, HitPoint);
            int n = Mathf.CeilToInt((f - prev) * len * 4f);
            for (int k = 0; k < n; k++) { var q = Line(Mathf.Lerp(prev, f, (k + 0.5f) / n)); Vol2FsB1.Emit(_drops, q, 6); Vol2FsB1.Emit(_mist, q, 1); }
        }

        protected override void OnHit(long nowUs)
        {
            _feet0 = FeetNow();
            _domeC = new Vector3(TB.center.x, Ground, TB.center.z);
            _domeR = Mathf.Max(TB.extents.x, TB.extents.z) + 1.0f;
            Vector3 g = new Vector3(_domeC.x, Ground + 0.05f, _domeC.z);
            Vol2FsB1.Emit(_splash, HitPoint, 40); Vol2FsB1.EmitV(_crown, g, Vector3.zero);
            for (int k = 0; k < 260; k++)
            {
                float a = Vol2Fs.Hash(Seed, k) * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0.6f + 1.2f * Vol2Fs.Hash(Seed, k + 300), Mathf.Sin(a)).normalized;
                Vol2FsB1.EmitV(_drops, HitPoint + d * 0.3f, d * Mathf.Lerp(3f, 10f, Vol2Fs.Hash(Seed, k + 600)));
            }
            Vol2FsB1.Emit(_mist, HitPoint, 14); Vol2FsB1.Emit(_foam, g, 26);
            var drop = M("fs_dot", Aqua, 2f, 0.6f); drop.NoiseAmt = 0.1f;
            // the soaked body drips (water runs down) while wet
            var tr = new GameObject("Drip").transform; tr.SetParent(Root.transform, false);
            _drip = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Rate = 70f, Loop = true, Life0 = 0.4f, Life1 = 0.7f, Speed0 = 0f, Speed1 = 0.3f, Size0 = 0.03f, Size1 = 0.07f, Shape = ParticleSystemShapeType.Sphere, Radius = 1f,
                ShapeScale = new Vector3(TB.extents.x, TB.extents.y * 0.6f, TB.extents.z), Gravity = 1.6f, Stretch = true, StretchVel = 0.05f, Max = 200 }, tr, "Drip");
            _drip.Play(false);
        }

        protected override void OnAfter(float t, float h, bool ended)
        {
            var cam = CamPos;
            Vector3 shift = FeetNow() - _feet0;
            _light.transform.position = HitPoint; _light.intensity = 3f * Mathf.Exp(-h * 3f);
            // the orb is gone; the dome rises round the body (0.1 s) and collapses outward / down (0.7 s)
            DrawOrb(t, _orbR * (1f - Vol2Fs.Smooth(0f, 0.08f, h)), Vector3.zero, 1f - Vol2Fs.Smooth(0f, 0.08f, h));
            float rise = Vol2Fs.Smooth(0f, 0.1f, h), col = Vol2Fs.Smooth(0.15f, 0.85f, h), life = 1f - Vol2Fs.Smooth(0.35f, 1.0f, h);
            if (life > 0.01f)
                Vol2FsB1.WaterSphere(_domeMesh, _domeC + shift, _domeR, -0.02f, cam, life, d =>
                {
                    float wob = 1f + 0.07f * Mathf.Sin(h * 22f + d.x * 6f + d.z * 4f);
                    Vector3 p = new Vector3(d.x, Mathf.Max(0f, d.y), d.z) * wob;
                    p.y *= rise * (1f - 0.92f * col) * 1.15f;
                    float spread = 1f + 0.55f * col * (1f - p.y);
                    p.x *= spread * (0.75f + 0.25f * rise); p.z *= spread * (0.75f + 0.25f * rise);
                    return p;
                }, 36, 18, 1.35f);
            else _domeMesh.Clear();
            // polish (a_b): ink curling INSIDE the dome over the body while it stands
            if (h > 0.08f && life > 0.01f)
            {
                _ink.Clear();
                Vector3 ic = _domeC + shift + Vector3.up * _domeR * 0.55f * rise * (1f - 0.8f * col);
                foreach (var td in _tendrils)
                {
                    var pts = new Vector3[9]; var w = new float[9]; var al = new float[9];
                    for (int k = 0; k < 9; k++)
                    {
                        float u = k / 8f, a = td.A0 + t * td.Speed * 0.6f + u * td.Len * 2.2f;
                        pts[k] = ic + new Vector3(Mathf.Cos(a), (u - 0.5f) * 0.9f + Mathf.Sin(u * 3f + td.A0) * 0.3f, Mathf.Sin(a)) * _domeR * (0.35f + 0.4f * u) * (1f + 0.4f * col);
                        w[k] = td.W * 1.6f * Mathf.Sin(u * Mathf.PI); al[k] = life;
                    }
                    _ink.Strip(pts, w, null, Color.white, al, td.A0, cam, 0f, 1f);
                }
                _ink.Write(_inkMesh);
            }
            if (h > 0.2f && h < 0.85f && Time.frameCount % 2 == 0)
            {
                for (int k = 0; k < 6; k++)
                {
                    float a = Random.value * Mathf.PI * 2f;
                    Vector3 rim = _domeC + shift + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * _domeR * (1f + 0.5f * col) + Vector3.up * (0.1f + 1.2f * (1f - col));
                    Vol2FsB1.EmitV(_drops, rim, new Vector3(Mathf.Cos(a), 0.4f, Mathf.Sin(a)) * Random.Range(1.5f, 4f));
                }
                Vol2FsB1.Emit(_foam, _domeC + shift + new Vector3(Random.Range(-1f, 1f), 0.04f, Random.Range(-1f, 1f)) * _domeR, 1);
            }
            // the wet stain spreads and stays while the body is wet, then dries
            _stain.Clear();
            float sl = 1f - Vol2Fs.Smooth(WetSeconds - 0.6f, WetSeconds + 0.6f, h);
            if (sl > 0.01f) _stain.Quad(new Vector3(_domeC.x, Ground + 0.02f, _domeC.z) + shift * 0.5f, Right, Fwd, (_domeR * 2.6f) * Vol2Fs.Smooth(0f, 0.6f, h), new Color(1f, 1f, 1f, sl), 0.47f);
            _stain.Write(_stainMesh);
            if (_drip != null)
            {
                _drip.transform.position = BodyNow() + Vector3.up * TB.extents.y * 0.3f;
                var em = _drip.emission; em.rateOverTime = 70f * (1f - Vol2Fs.Smooth(1f, WetSeconds, h));
                if (h > WetSeconds && _drip.isEmitting) _drip.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        protected override float Linger => WetSeconds + 1.5f;

        public override bool Done(long nowUs)
        {
            if (Root == null || Gone) return true;
            if (IsHit && Now(nowUs) - HitAt < WetSeconds + 0.8f) return false;
            return base.Done(nowUs);
        }
    }
}
