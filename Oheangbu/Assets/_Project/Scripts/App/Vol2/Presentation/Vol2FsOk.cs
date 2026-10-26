using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>옥 (water, ㅗ fan, 남긴다) - free showcase batch 3, concept ok_b (the fan is a living MARSH of swirling water, foam and floating ink, with slender
    /// waterspouts twisting up like stalks across it; the bears inside soaked). Unlike 오's rolling wave and 악's single fountain it is a standing flooded field with
    /// many thin twisting spouts. Beats, executor facts only: the launch = water floods out from the apex over the fan and the spouts twist up in its wake -> the
    /// marsh swirls for the residue material's life (ink drifting in it, foam at the edge, spouts swaying); a body the strike or the field touched = a splash and
    /// it drips for its 4 s wetness (a mirror) -> the last 0.4 s = the spouts falter -> natural expiry (CSV: 아래로 흘러내림) = the spouts fall back and the water
    /// drains down into the ground, leaving it dark and wet.</summary>
    public sealed class Vol2FsOk : Vol2FsLeave
    {
        static readonly Color Ink = new Color(0.02f, 0.03f, 0.04f), Foam = new Color(0.9f, 0.95f, 0.95f);
        ParticleSystem _charge, _spray, _drops, _foam, _mist, _drip; Light _chargeLight; Light[] _lights;
        Mesh _poolMesh, _foamMesh, _spoutMesh, _inkMesh, _wetMesh, _floatMesh;
        readonly Vol2FsStrips _pool = new Vol2FsStrips(), _foamS = new Vol2FsStrips(), _spouts = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _wet = new Vol2FsStrips(), _floatInk = new Vol2FsStrips();
        struct Spout { public Vector3 P; public float U, H, W, Ph, Rnd, At; }
        readonly List<Spout> _sp = new List<Spout>();
        bool _built;
        protected override float ExpireSeconds => 1.4f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            var water = M("fs_splash", Color.white, 1.6f, 0.3f); water.Ramp = "fs_ramp_water"; water.RampScale = 1.5f; water.NoiseAmt = 0.4f; water.Soft = 0.1f;
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(water), Rate = 60f, Loop = true, Life0 = 0.18f, Life1 = 0.28f, Speed0 = -2f, Speed1 = -3f, Size0 = 0.1f, Size1 = 0.22f, Radius = 0.6f, World = false, Tiles = 2, Orbital = 6f, Max = 80 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", new Color(0.6f, 0.85f, 1f), 3f);
            _charge.transform.position = Tip(); _charge.Play(false);
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(water), Life0 = 0.5f, Life1 = 1f, Speed0 = 1f, Speed1 = 3.5f, Size0 = 0.3f, Size1 = 0.7f, Radius = 0.2f, Gravity = 1.6f, Drag = 0.5f, Tiles = 2,
                SizeStart = 0.4f, SizePeak = 1.2f, SizeEnd = 0.8f, Spin = 120f, Max = 1600 }, root, "Spray");
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.8f, 0.95f, 1f), 2.2f, 0.6f)), Life0 = 0.5f, Life1 = 1.1f, Speed0 = 1f, Speed1 = 3f, Size0 = 0.03f, Size1 = 0.07f, Radius = 0.1f, Gravity = 1.8f,
                Stretch = true, StretchVel = 0.03f, Max = 2400 }, root, "Drops");
            var foam = M("fs_puff", Foam, 1.3f, 0.2f); foam.Opacity = 0.7f; foam.Erode = 0.4f; foam.NoiseAmt = 0.6f;
            _foam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(foam), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 0.2f, Speed1 = 1f, Size0 = 0.25f, Size1 = 0.55f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.2f, Drag = 1.5f, Tiles = 2, Max = 900 }, root, "Foam");
            var mist = M("fs_puff", new Color(0.8f, 0.88f, 0.9f, 1f), 1.1f, 0.2f); mist.Opacity = 0.3f; mist.Erode = 0.4f;
            _mist = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mist), Life0 = 1.2f, Life1 = 2f, Speed0 = 0.1f, Speed1 = 0.5f, Size0 = 0.9f, Size1 = 1.6f, Radius = 0.3f, Drag = 1.5f, Tiles = 2, Max = 120, SortFudge = 8 }, root, "Mist");
            _drip = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(water), Rate = 0f, Loop = true, Life0 = 0.4f, Life1 = 0.8f, Speed0 = 0f, Speed1 = 0.4f, Size0 = 0.1f, Size1 = 0.22f, Radius = 0.5f, Gravity = 2f, Tiles = 2, Stretch = true, StretchVel = 0.08f, Max = 400 }, root, "Drip");
            foreach (var ps in new[] { _spray, _drops, _foam, _mist, _drip }) ps.Play(false);
            var pool = M("fs_swirl", Color.white, 1.0f, 0.18f); pool.Ramp = "fs_ramp_water"; pool.RampScale = 1.15f; pool.NoiseAmt = 0.5f; pool.NoiseScale = 1.6f; pool.Soft = 0.02f; pool.Erode = 0.3f; pool.Opacity = 0.75f; pool.Scroll = new Vector2(-0.25f, 0.18f);      // r2: a darker, softer marsh (r1: a white-streaked sheet)
            Vol2Fs.MeshObject(root, "Marsh", Kit.Mat(pool), out _poolMesh);
            var swirl = M("fs_swirl", Color.white, 1.4f, 0.35f); swirl.Ramp = "fs_ramp_water"; swirl.RampScale = 1.3f; swirl.NoiseAmt = 0.3f; swirl.Soft = 0.02f;
            Vol2Fs.MeshObject(root, "FoamEdge", Vol2FsB1.Water(Kit, 1.6f, 0.3f, "fs_splash"), out _foamMesh);
            var flow = M("fs_water", Color.white, 1.9f, 0.4f); flow.Ramp = "fs_ramp_water"; flow.RampScale = 1.6f; flow.NoiseAmt = 0.45f; flow.NoiseScale = 1.4f; flow.Erode = 0.22f; flow.Soft = 0.15f; flow.Scroll = new Vector2(-1.8f, 0f); flow.Edge = new Color(0.9f, 1f, 1f, 1f) * 0.6f; flow.EdgeWidth = 0.12f;
            Vol2Fs.MeshObject(root, "Spouts", Kit.Mat(flow), out _spoutMesh);
            Vol2Fs.MeshObject(root, "SpoutInk", Kit.InkSolid("fs_stroke2", Ink, new Color(0.5f, 0.9f, 1f, 1f) * 0.8f, 0.05f, 0.45f, 0.26f, 1.4f), out _inkMesh);
            var fi = M("fs_stroke2", Ink, 1f, 0f); fi.Soft = 0.001f; fi.NoiseAmt = 0.5f; fi.Erode = 0.3f; fi.Opacity = 0.7f; fi.Scroll = new Vector2(0.2f, 0f);
            Vol2Fs.MeshObject(root, "FloatingInk", Kit.Mat(fi), out _floatMesh);
            var wet = M("fs_wet", new Color(0.05f, 0.04f, 0.04f, 1f), 1f, 0f); wet.Soft = 0.001f; wet.Opacity = 0.75f; wet.NoiseAmt = 0.3f;
            Vol2Fs.MeshObject(root, "Wet", Kit.Mat(wet), out _wetMesh);
            _lights = new Light[2];
            for (int k = 0; k < 2; k++) _lights[k] = Vol2Fs.PointLight(root, "MarshLight" + k, new Color(0.6f, 0.85f, 1f), 6f);
        }

        protected override void OnCharge(float t, float k) { _charge.transform.position = Tip(); _chargeLight.intensity = 3f * k; }
        protected override void OnLaunch(long nowUs) { _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f; }

        protected override void OnHit(long nowUs)
        {
            if (_built) return;
            _built = true;
            int seed = Seed + 191;
            for (int k = 0; k < 14; k++)
            {
                float u = Mathf.Lerp(0.3f, 0.95f, Mathf.Sqrt(Vol2Fs.Hash(seed, k))), v = (Vol2Fs.Hash(seed, k + 50) * 2f - 1f) * 0.88f;
                _sp.Add(new Spout { P = Vol2FsB3.InFan(Apex, F, Depth, Half, u, v, Ground), U = u, H = Mathf.Lerp(1.0f, 2.4f, Vol2Fs.Hash(seed, k + 100)) * Mathf.Lerp(0.6f, 1f, u), W = Mathf.Lerp(0.16f, 0.3f, Vol2Fs.Hash(seed, k + 150)),
                    Ph = Vol2Fs.Hash(seed, k + 200) * 6.28f, Rnd = Vol2Fs.Hash(seed, k + 250), At = u * 0.3f + 0.05f });
            }
            for (int k = 0; k < 2; k++) _lights[k].transform.position = Vol2FsB3.InFan(Apex, F, Depth, Half, 0.6f, k == 0 ? -0.5f : 0.5f, Ground) + Vector3.up * 1.2f;
            for (int k = 0; k < 12; k++) Vol2FsB1.Emit(_spray, Vol2FsB3.InFan(Apex, F, Depth, Half, 0.4f + 0.5f * Vol2Fs.Hash(seed, k + 300), (k / 11f) * 2f - 1f, Ground) + Vector3.up * 0.2f, 5);
        }

        protected override void OnStruck(Struck s) { Vol2FsB1.Emit(_spray, CentreNow(s), 40); Vol2FsB1.Emit(_drops, CentreNow(s), 100); }
        protected override void OnTouch(Struck s) { Vol2FsB1.Emit(_spray, CentreNow(s), 24); }
        protected override void OnResidueEnd() { if (!NaturalEnd) Vol2FsB1.Emit(_mist, Vol2FsB3.InFan(Apex, F, Depth, Half, 0.6f, 0f, Ground) + Vector3.up * 0.5f, 6); }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            float warn = Warn(NowUs), ex = ExitK(t), pulse = DrawPulse(t);
            float noRes = Res == null && EndRel >= 0f ? Vol2Fs.Smooth(EndRel, EndRel + 0.8f, t) : 0f;
            float collapse = NaturalEnd ? Vol2Fs.Smooth(0f, 0.6f, ex) : Vol2Fs.Smooth(0f, 0.35f, ex);
            float drain = NaturalEnd ? Vol2Fs.Smooth(0.3f, 1f, ex) : Vol2Fs.Smooth(0f, 0.6f, ex);
            float flood = Vol2Fs.Smooth(0f, 0.35f, h);
            _pool.Clear(); _foamS.Clear(); _spouts.Clear(); _ink.Clear(); _wet.Clear(); _floatInk.Clear();
            float pl = flood * (1f - drain) * (1f - noRes);
            if (pl > 0.01f) { FanCarpet(_pool, flood, pl, 0.37f, 0.04f, 1f); }
            float wl = flood * (1f - Vol2Fs.Smooth(0.8f, 1f, ex)) * (1f - noRes);
            if (wl > 0.01f) FanCarpet(_wet, flood, wl, 0.61f, 0.025f, 1.08f);
            // foam along the far arc and the edges
            if (pl > 0.05f)
            {
                var pts = new List<Vector3>(); var w = new List<float>(); var al = new List<float>();
                for (int k = 0; k <= 16; k++) { float v = (k / 16f) * 2f - 1f; var p = Apex + Dir(v * Half) * Depth * flood; p.y = Ground + 0.08f; pts.Add(p); w.Add(0.22f); al.Add(pl * (0.6f + 0.4f * Mathf.Sin(t * 3f + k))); }
                _foamS.Strip(pts, w, null, Color.white, al, 0.47f, cam, 0f, 4f);
            }
            // ink drifting on the water (slow curls)
            if (pl > 0.05f)
                for (int j = 0; j < 4; j++)
                {
                    var pts = new Vector3[8]; var w = new float[8]; var al = new float[8]; var sd = new Vector3[8];
                    for (int k = 0; k < 8; k++)
                    {
                        float u = 0.35f + 0.5f * (j / 3f), v = Mathf.Sin(t * 0.3f + j) * 0.3f + (k / 7f - 0.5f) * 0.9f;
                        var p = Vol2FsB3.InFan(Apex, F, Depth, Half, u + 0.08f * Mathf.Sin(k * 0.9f + t * 0.5f + j), v, Ground); p.y = Ground + 0.06f;
                        pts[k] = p; w[k] = 0.22f * Mathf.Sin((k + 0.5f) / 8f * Mathf.PI); al[k] = pl * 0.8f; sd[k] = F;
                    }
                    _floatInk.Strip(pts, w, sd, Color.white, al, 0.13f + 0.2f * j, cam, 0f, 1f);
                }
            // the twisting waterspouts (they fall back on the end)
            var side = Vector3.Cross(Vector3.up, F);
            foreach (var s in _sp)
            {
                float g = Vol2Fs.EaseOutBack(Mathf.Clamp01((h - s.At) / 0.4f));
                float falter = warn > 0f ? 1f - warn * (0.35f + 0.3f * Mathf.Sin(t * 22f + s.Ph)) : 1f;
                float hh = s.H * g * falter * (1f - collapse) * (1f - noRes) * (1f + 0.2f * pulse);
                if (hh < 0.05f) continue;
                var pts = new Vector3[8]; var w = new float[8]; var al = new float[8];
                for (int k = 0; k < 8; k++)
                {
                    float u = k / 7f;
                    pts[k] = s.P + Vector3.up * hh * u + (side * Mathf.Sin(u * 5f + t * 3f + s.Ph) + F * Mathf.Cos(u * 4f + t * 2.5f + s.Ph) * 0.6f) * 0.18f * u;
                    w[k] = s.W * (1f - 0.65f * u); al[k] = 0.95f * (1f - 0.25f * u);
                }
                _spouts.Strip(pts, w, null, Color.white, al, s.Rnd, cam, 0f, 2f);
                for (int k = 0; k < 8; k++) { pts[k] += side * 0.05f * Mathf.Sin(k * 1.4f + t * 7f); w[k] *= 0.3f; }
                _ink.Strip(pts, w, null, Color.white, al, s.Rnd + 0.5f, cam, 0f, 1f);
                if ((Time.frameCount + (int)(s.Rnd * 10)) % 6 == 0 && !Exiting) Vol2FsB1.EmitV(_drops, pts[7], Random.insideUnitSphere * 0.8f + Vector3.up * 0.4f, -1f, -1f);
                if (Exiting && NaturalEnd && Time.frameCount % 3 == 0) Vol2FsB1.EmitV(_spray, pts[7], Vector3.down * 1.5f, -1f, -1f);
            }
            if (!Exiting && pl > 0.3f && Time.frameCount % 3 == 0) Vol2FsB1.Emit(_foam, Vol2FsB3.InFan(Apex, F, Depth, Half, Random.Range(0.3f, 1f), Random.Range(-0.95f, 0.95f), Ground), 1);
            if (!Exiting && pl > 0.3f && Time.frameCount % 20 == 0) Vol2FsB1.Emit(_mist, Vol2FsB3.InFan(Apex, F, Depth, Half, Random.Range(0.4f, 1f), Random.Range(-0.8f, 0.8f), Ground) + Vector3.up * 0.4f, 1);
            _pool.Write(_poolMesh); _foamS.Write(_foamMesh); _spouts.Write(_spoutMesh); _ink.Write(_inkMesh); _wet.Write(_wetMesh); _floatInk.Write(_floatMesh);
            bool drip = false;
            foreach (var s in Hits) if (t - s.At < 4f) { drip = true; _drip.transform.position = CentreNow(s); }
            foreach (var s in Touches) if (t - s.At < 4f) { drip = true; _drip.transform.position = CentreNow(s); }
            var em = _drip.emission; em.rateOverTime = drip ? 40f : 0f;
            for (int k = 0; k < _lights.Length; k++) _lights[k].intensity = 2.2f * flood * (1f - collapse) * (1f - noRes) * (1f + pulse);
        }

        void FanCarpet(Vol2FsStrips s, float reach, float life, float rnd, float lift, float grow)
        {
            var pts = new List<Vector3>(); var w = new List<float>(); var al = new List<float>(); var side = new List<Vector3>();
            for (int k = 0; k <= 14; k++) { float v = (k / 14f) * 2f - 1f; Vector3 d = Dir(v * Half * 1.03f * grow); var p = Apex + d * Depth * reach * grow * 0.63f; p.y = Ground + lift; pts.Add(p); w.Add(Depth * reach * grow * 0.76f); /* r3: from 1/4 out */ al.Add(life); side.Add(d); }
            s.Strip(pts, w, side, Color.white, al, rnd, CamPos, 0f, 3f);
        }
    }
}
