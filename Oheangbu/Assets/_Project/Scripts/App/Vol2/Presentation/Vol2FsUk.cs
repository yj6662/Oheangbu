using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>욱 (water, ㅜ ring, 남긴다) - free showcase batch 3, concept uk_b (a ribbon of water flows round and round the caster on the ring like a river in a circle,
    /// rising waist-high in arching branches of water that fall back into it, ink swirling in it; the bear crossing it is soaked). Unlike 우's geysers that crash
    /// down once (columns) and 옥's marsh it is a circular RIVER that keeps flowing. Beats, executor facts only: the launch = water wells up out of the band and races
    /// round it -> it flows for the residue material's life (the current visibly running round, arches leaping out of it and back, foam, drops); a body the strike or
    /// the ring touched = a splash and it drips for its 4 s wetness (a mirror) -> the last 0.4 s = the arches falter, the flow slows -> natural expiry (CSV: 아래로
    /// 흘러내림) = the arches fall, the river sinks down into the ground, leaving a wet ring.</summary>
    public sealed class Vol2FsUk : Vol2FsLeave
    {
        static readonly Color Ink = new Color(0.02f, 0.03f, 0.04f), Foam = new Color(0.9f, 0.95f, 0.95f);
        ParticleSystem _charge, _spray, _drops, _foam, _mist, _drip; Light _chargeLight; Light[] _lights;
        Mesh _riverMesh, _arcMesh, _inkMesh, _wetMesh, _foamMesh;
        readonly Vol2FsStrips _river = new Vol2FsStrips(), _arcs = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _wet = new Vol2FsStrips(), _foamS = new Vol2FsStrips();
        struct Arc { public float A0, Span, H, W, Ph, Rnd, Back; }
        readonly List<Arc> _arcList = new List<Arc>();
        bool _built; float _flow;
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
            _foam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(foam), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 0.2f, Speed1 = 1f, Size0 = 0.25f, Size1 = 0.5f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.2f, Drag = 1.5f, Tiles = 2, Max = 900 }, root, "Foam");
            var mist = M("fs_puff", new Color(0.8f, 0.88f, 0.9f, 1f), 1.1f, 0.2f); mist.Opacity = 0.3f; mist.Erode = 0.4f;
            _mist = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mist), Life0 = 1.2f, Life1 = 2f, Speed0 = 0.1f, Speed1 = 0.5f, Size0 = 0.9f, Size1 = 1.6f, Radius = 0.3f, Drag = 1.5f, Tiles = 2, Max = 100, SortFudge = 8 }, root, "Mist");
            _drip = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(water), Rate = 0f, Loop = true, Life0 = 0.4f, Life1 = 0.8f, Speed0 = 0f, Speed1 = 0.4f, Size0 = 0.1f, Size1 = 0.22f, Radius = 0.5f, Gravity = 2f, Tiles = 2, Stretch = true, StretchVel = 0.08f, Max = 400 }, root, "Drip");
            foreach (var ps in new[] { _spray, _drops, _foam, _mist, _drip }) ps.Play(false);
            Vol2Fs.MeshObject(root, "River", Vol2FsB1.Water(Kit, 1.8f, 0.38f, "fs_water"), out _riverMesh);
            var flow = M("fs_water", Color.white, 1.9f, 0.4f); flow.Ramp = "fs_ramp_water"; flow.RampScale = 1.6f; flow.NoiseAmt = 0.45f; flow.NoiseScale = 1.4f; flow.Erode = 0.22f; flow.Soft = 0.15f; flow.Scroll = new Vector2(-1.8f, 0f); flow.Edge = new Color(0.9f, 1f, 1f, 1f) * 0.6f; flow.EdgeWidth = 0.12f;
            Vol2Fs.MeshObject(root, "Arches", Kit.Mat(flow), out _arcMesh);
            Vol2Fs.MeshObject(root, "InkInRiver", Kit.InkSolid("fs_stroke2", Ink, new Color(0.5f, 0.9f, 1f, 1f) * 0.8f, 0.05f, 0.45f, 0.26f, 1.4f), out _inkMesh);
            Vol2Fs.MeshObject(root, "FoamLine", Vol2FsB1.Water(Kit, 1.6f, 0.3f, "fs_splash"), out _foamMesh);
            var wet = M("fs_wet", new Color(0.05f, 0.04f, 0.04f, 1f), 1f, 0f); wet.Soft = 0.001f; wet.Opacity = 0.75f; wet.NoiseAmt = 0.3f;
            Vol2Fs.MeshObject(root, "WetRing", Kit.Mat(wet), out _wetMesh);
            _lights = new Light[2];
            for (int k = 0; k < 2; k++) _lights[k] = Vol2Fs.PointLight(root, "RiverLight" + k, new Color(0.6f, 0.85f, 1f), 6f);
        }

        protected override void OnCharge(float t, float k) { _charge.transform.position = Tip(); _chargeLight.intensity = 3f * k; }
        protected override void OnLaunch(long nowUs) { _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f; }

        protected override void OnHit(long nowUs)
        {
            if (_built) return;
            _built = true;
            int seed = Seed + 291;
            const int N = 11;
            for (int k = 0; k < N; k++)
            {
                float a0 = Vol2FsB2.Angle(k, N, seed, 0.4f), back = 1f - Front(a0);
                if (back > 0.9f) continue;                                         // r2: arches only on the front and the near sides
                _arcList.Add(new Arc { A0 = a0, Span = Mathf.Lerp(0.45f, 0.8f, Vol2Fs.Hash(seed, k)), H = Mathf.Lerp(0.8f, 1.45f, Vol2Fs.Hash(seed, k + 10)) * (1f - 0.6f * back), W = Mathf.Lerp(0.16f, 0.28f, Vol2Fs.Hash(seed, k + 20)), Ph = Vol2Fs.Hash(seed, k + 30) * 6.28f, Rnd = Vol2Fs.Hash(seed, k + 40), Back = back });
            }
            for (int k = 0; k < 2; k++) _lights[k].transform.position = OnBand(k == 0 ? -0.7f : 0.7f, R) + Vector3.up * 1f;
        }

        protected override void OnStruck(Struck s) { Vol2FsB1.Emit(_spray, CentreNow(s), 40); Vol2FsB1.Emit(_drops, CentreNow(s), 100); }
        protected override void OnTouch(Struck s) { Vol2FsB1.Emit(_spray, CentreNow(s), 24); }
        protected override void OnResidueEnd() { if (!NaturalEnd) Vol2FsB1.Emit(_mist, OnBand(0f, R) + Vector3.up * 0.5f, 6); }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            float warn = Warn(NowUs), ex = ExitK(t), pulse = DrawPulse(t);
            float noRes = Res == null && EndRel >= 0f ? Vol2Fs.Smooth(EndRel, EndRel + 0.8f, t) : 0f;
            float sinkK = NaturalEnd ? Vol2Fs.Smooth(0.2f, 1f, ex) : Vol2Fs.Smooth(0f, 0.5f, ex);
            float archFall = NaturalEnd ? Vol2Fs.Smooth(0f, 0.5f, ex) : Vol2Fs.Smooth(0f, 0.3f, ex);
            float well = Vol2Fs.Smooth(0f, 0.25f, h);
            float run = Vol2Fs.Smooth(0f, 0.35f, h);                               // the water races round from the front
            _flow += Time.deltaTime * (1.4f - 0.9f * warn) * (1f - archFall);
            _river.Clear(); _arcs.Clear(); _ink.Clear(); _wet.Clear(); _foamS.Clear();
            float rl = well * (1f - sinkK) * (1f - noRes);
            if (rl > 0.01f)
            {
                // the river band: the water texture scrolls ROUND the ring (u along it), the near arc lower / fainter
                Vol2FsB2.GroundRing(_river, Apex, F, Ground, R - 0.42f, R + 0.42f, 96, a => rl * (Mathf.Abs(a) <= run * Mathf.PI ? 1f : 0f) * (1f - 0.4f * Back(a)), 0.37f, cam, 0.06f + 0.06f * (1f - sinkK), _flow, _flow + 6f);
                Vol2FsB2.GroundRing(_foamS, Apex, F, Ground, R + 0.3f, R + 0.5f, 96, a => rl * 0.8f * (Mathf.Abs(a) <= run * Mathf.PI ? 1f : 0f) * (1f - 0.5f * Back(a)), 0.53f, cam, 0.1f, _flow * 1.3f, _flow * 1.3f + 8f);
                // ink currents in the river (dark threads running round)
                for (int j = 0; j < 3; j++)
                {
                    var pts = new List<Vector3>(); var w = new List<float>(); var al = new List<float>(); var sd = new List<Vector3>();
                    float start = -Mathf.PI * run + ((_flow * 0.7f + j * 2.1f) % (Mathf.PI * 2f)) - Mathf.PI;
                    for (int k = 0; k < 12; k++)
                    {
                        float a = start + k * 0.12f; if (Mathf.Abs(Mathf.DeltaAngle(0f, a * Mathf.Rad2Deg)) * Mathf.Deg2Rad > run * Mathf.PI) continue;
                        var p = OnBand(a, R + 0.2f * Mathf.Sin(k * 0.8f + j)); p.y = Ground + 0.09f; pts.Add(p); w.Add(0.12f * Mathf.Sin((k + 0.5f) / 12f * Mathf.PI)); al.Add(rl * 0.8f * (1f - 0.5f * Back(a))); sd.Add(Dir(a));
                    }
                    if (pts.Count >= 2) _ink.Strip(pts, w, sd, Color.white, al, 0.2f + 0.3f * j, cam, 0f, 1f);
                }
            }
            // the arches leaping out of the river and falling back into it, further along the flow
            foreach (var arc in _arcList)
            {
                if (Mathf.Abs(arc.A0) > run * Mathf.PI) continue;
                float phase = Mathf.Repeat(t * 0.5f + arc.Ph, 1f);               // each arch rises, holds and re-forms on its own beat
                float life = Mathf.Sin(Mathf.Clamp01(phase / 0.9f) * Mathf.PI);
                float hh = arc.H * (0.75f + 0.25f * life) * well * (1f - archFall) * (1f - noRes) * (1f - 0.5f * warn * (0.6f + 0.4f * Mathf.Sin(t * 20f + arc.Ph))) * (1f + 0.25f * pulse);
                if (hh < 0.05f) continue;
                var pts = new Vector3[10]; var w = new float[10]; var al = new float[10];
                for (int k = 0; k < 10; k++)
                {
                    float u = k / 9f, a = arc.A0 + arc.Span * u;
                    var p = OnBand(a, R + 0.15f * Mathf.Sin(u * Mathf.PI)); p.y = Ground + 0.05f + hh * Mathf.Sin(u * Mathf.PI);
                    pts[k] = p; w[k] = arc.W * (1f - 0.35f * Mathf.Abs(u - 0.5f) * 2f); al[k] = 0.9f;
                }
                _arcs.Strip(pts, w, null, Color.white, al, arc.Rnd, cam, 0f, 2f);
                if ((Time.frameCount + (int)(arc.Rnd * 10)) % 4 == 0 && !Exiting) Vol2FsB1.EmitV(_drops, pts[9], Vector3.down * 0.5f + Random.insideUnitSphere * 0.5f, -1f, -1f);
                if ((Time.frameCount + (int)(arc.Rnd * 10)) % 10 == 0 && !Exiting) Vol2FsB1.Emit(_foam, pts[9] + Vector3.down * 0.05f, 1);
                if (Exiting && NaturalEnd && Time.frameCount % 3 == 0) Vol2FsB1.EmitV(_spray, pts[4], Vector3.down * 1.5f, -1f, -1f);
            }
            float wl = well * (1f - Vol2Fs.Smooth(0.8f, 1f, ex)) * (1f - noRes);
            if (wl > 0.01f) Vol2FsB2.GroundRing(_wet, Apex, F, Ground, R - 0.65f, R + 0.7f, 72, a => wl * (Mathf.Abs(a) <= run * Mathf.PI ? 1f : 0f), 0.27f, cam, 0.025f, 0f, 4f);
            _river.Write(_riverMesh); _arcs.Write(_arcMesh); _ink.Write(_inkMesh); _wet.Write(_wetMesh); _foamS.Write(_foamMesh);
            if (!Exiting && rl > 0.3f && Time.frameCount % 25 == 0) Vol2FsB1.Emit(_mist, OnBand(Random.Range(-1.8f, 1.8f), R) + Vector3.up * 0.3f, 1);
            bool drip = false;
            foreach (var s in Hits) if (t - s.At < 4f) { drip = true; _drip.transform.position = CentreNow(s); }
            foreach (var s in Touches) if (t - s.At < 4f) { drip = true; _drip.transform.position = CentreNow(s); }
            var em = _drip.emission; em.rateOverTime = drip ? 40f : 0f;
            for (int k = 0; k < _lights.Length; k++) _lights[k].intensity = 2.2f * well * (1f - sinkK) * (1f - noRes) * (1f + pulse);
        }
    }
}
