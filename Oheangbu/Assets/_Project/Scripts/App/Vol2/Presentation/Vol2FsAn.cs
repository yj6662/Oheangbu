using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>안 (water, ㅏ path, 번진다) - free showcase batch 4, concept an_a (water bridges: the water bolt drenches the first bear and from its splash, arching
    /// ropes of clear water with ink curling inside leap through the air to every neighbour, each landing in a heavy splash crown; rippling puddles at their feet).
    /// Beats, all executor facts: charge (droplets spiralling into the tip) -> a jet of water runs to the predicted first body -> the splash (crown, spray, drench)
    /// -> every "Spread" contact = one rope of water arching from the struck body to that neighbour in the executor's order (its thickness by the share: 1.0 split
    /// n ways), dripping as it goes -> the crown on the neighbour -> its Mark (3 s) = a rippling puddle and the body streaming wet; warning = the ripples slow
    /// -> natural expiry (CSV: 아래로 흘러내림) = the rope collapses downward, the water runs off the body and the puddle drains.</summary>
    public sealed class Vol2FsAn : Vol2FsSpread
    {
        static readonly Color Aqua = new Color(0.55f, 0.9f, 1f), Ink = new Color(0.02f, 0.03f, 0.04f);
        ParticleSystem _charge, _spray, _drops, _mist, _drips; Light _hitLight;
        Mesh _jetMesh, _ropeMesh, _coreMesh, _crownMesh, _puddleMesh;
        readonly Vol2FsStrips _jet = new Vol2FsStrips(), _ropes = new Vol2FsStrips(), _cores = new Vol2FsStrips(), _crowns = new Vol2FsStrips(), _puddles = new Vol2FsStrips();
        readonly List<(Vector3 At, float T, float K)> _crownAt = new List<(Vector3, float, float)>();
        Vector3 _hit;

        sealed class Rope { public Vector3 A, B; public float Lift, Phase; }

        protected override float LobHeight => 0.25f;
        protected override float HopSpeed => 9f;
        protected override float HopMin => 0.28f;
        protected override float HopMax => 0.5f;
        protected override float MarkExpireSeconds => 1.0f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB1.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            var drop = M("fs_dot", Aqua, 2.2f, 0.6f);
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Rate = 70f, Loop = true, Life0 = 0.2f, Life1 = 0.35f, Speed0 = -1.5f, Speed1 = -2.5f, Size0 = 0.04f, Size1 = 0.09f, Radius = 0.6f, Orbital = 5f, World = false, Max = 100 }, root, "Charge");
            _charge.transform.position = Tip(); _charge.Play(false);
            var spray = M("fs_splash", Color.white, 1.4f, 0.3f); spray.Ramp = "fs_ramp_water"; spray.RampScale = 1.4f; spray.Soft = 0.1f; spray.NoiseAmt = 0.4f;
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(spray), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.25f, Size1 = 0.6f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.3f, Gravity = 1.6f, Tiles = 2, SizeEnd = 1.4f, Max = 500 }, root, "Spray");
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.5f, Life1 = 1.1f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.03f, Size1 = 0.08f, Radius = 0.25f, Gravity = 2.4f, Stretch = true, StretchVel = 0.03f, Max = 1500 }, root, "Drops");
            var mist = M("fs_puff", new Color(0.82f, 0.9f, 0.95f, 1f), 1f, 0f); mist.Opacity = 0.35f; mist.NoiseAmt = 0.5f; mist.Erode = 0.4f;
            _mist = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mist), Life0 = 0.8f, Life1 = 1.4f, Speed0 = 0.4f, Speed1 = 1.2f, Size0 = 0.8f, Size1 = 1.4f, Radius = 0.4f, Drag = 1f, Tiles = 2, SizeEnd = 1.8f, Max = 120, SortFudge = 8 }, root, "Mist");
            _drips = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.3f, Life1 = 0.6f, Speed0 = 0f, Speed1 = 0.4f, Size0 = 0.025f, Size1 = 0.05f, Radius = 0.35f, Gravity = 2.5f, Stretch = true, StretchVel = 0.05f, Max = 800 }, root, "Drips");
            foreach (var ps in new[] { _spray, _drops, _mist, _drips }) ps.Play(false);
            Vol2Fs.MeshObject(root, "Jet", Vol2FsB4.WaterDeep(Kit, 1.6f, "fs_streak"), out _jetMesh);
            Vol2Fs.MeshObject(root, "Ropes", Vol2FsB4.WaterDeep(Kit, 1.5f, "fs_water"), out _ropeMesh);
            var core = M("fs_stroke", Ink, 1f, 0f); core.NoiseAmt = 0.5f; core.Erode = 0.3f; core.Soft = 0.05f; core.Opacity = 0.85f; core.Scroll = new Vector2(-1.4f, 0f);
            Vol2Fs.MeshObject(root, "InkCores", Kit.Mat(core), out _coreMesh);
            Vol2Fs.MeshObject(root, "Crowns", Vol2FsB4.WaterDeep(Kit, 1.6f, "fs_splash"), out _crownMesh);
            var wet = M("fs_wet", new Color(0.22f, 0.32f, 0.36f, 1f), 1f, 0.15f); wet.Soft = 0.001f; wet.NoiseAmt = 0.35f; wet.Opacity = 0.8f; wet.Edge = new Color(0.8f, 1f, 1.1f, 1f); wet.EdgeWidth = 0.1f; wet.Scroll = new Vector2(0.3f, 0.2f);
            Vol2Fs.MeshObject(root, "Puddles", Kit.Mat(wet), out _puddleMesh);
            _hitLight = Vol2Fs.PointLight(root, "HitLight", Aqua, 6f);
        }

        protected override void OnCharge(float t, float k) { _charge.transform.position = Tip(); }

        protected override void OnLaunch(long nowUs) { _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); }

        protected override void OnFly(float t, float f, float prev)
        {
            _jet.Clear(); Vol2FsB4.Arc(_jet, From, HitPoint, LobHeight, Mathf.Max(0f, f - 0.6f), f, 0.24f, 1f, 0.2f, CamPos, 12, 0.05f, t * 20f); _jet.Write(_jetMesh);
            Vol2FsB1.Emit(_drops, Line(f), 4);
        }

        protected override void OnHit(long nowUs)
        {
            _hit = HasTarget ? HitPoint : AimPoint;
            Splash(_hit, 1.15f);
            _hitLight.transform.position = _hit;
        }

        void Splash(Vector3 at, float k)
        {
            _crownAt.Add((at, -1f, k));
            Vol2FsB1.Emit(_spray, at, Mathf.RoundToInt(40 * k)); Vol2FsB1.Emit(_drops, at, Mathf.RoundToInt(180 * k)); Vol2FsB1.Emit(_mist, at, 4);
        }

        protected override void OnHop(Hop h)
        {
            var r = new Rope { Phase = Vol2Fs.Hash(Seed, (int)h.TargetId) * 6f };
            r.A = (h.SourceId == TargetId && HasTarget ? HitPoint : h.FromP + Vector3.up * 0.9f) + Vector3.up * 0.3f;      // r2: HitPoint (r1 used _hit, still zero when the spread contact came before the visual hit: ropes from the world origin)
            r.B = CentreNow(h) + Vector3.up * 0.25f;
            r.Lift = Mathf.Clamp(Vector3.Distance(r.A, r.B) * 0.45f, 0.9f, 1.8f);
            h.Data = r;
        }

        protected override void OnLand(Hop h) { Splash(CentreNow(h), ShareScale(h.Share)); }

        protected override void OnMarkEnd(Mark m)
        {
            if (m.Why == Vol2MaterialEnd.Aborted) return;
            Vector3 c = m.Hop != null ? CentreNow(m.Hop) : m.Centre + Vector3.up * 0.6f;
            if (Natural(m)) Vol2FsB1.Emit(_drips, c, 40); else { Vol2FsB1.Emit(_spray, c, 12); Vol2FsB1.Emit(_mist, c, 3); }
        }

        void Crown(Vector3 at, float a, float k, Vector3 cam)
        {
            float rise = Vol2Fs.Smooth(0f, 0.18f, a), life = 1f - Vol2Fs.Smooth(0.25f, 0.75f, a); if (life <= 0.01f) return;
            var pts = new Vector3[4]; var w = new float[4]; var al = new float[4];
            for (int j = 0; j < 12; j++)
            {
                float ang = j / 12f * Mathf.PI * 2f; Vector3 o = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                Vector3 b = at + o * 0.3f * k + Vector3.down * 0.35f;
                for (int q = 0; q < 4; q++) { float u = q / 3f; pts[q] = b + o * (0.15f + 0.65f * u) * k * rise + Vector3.up * (1.2f * u - 0.9f * u * u * Vol2Fs.Smooth(0.1f, 0.6f, a)) * k * rise; w[q] = 0.28f * k * (1f - 0.6f * u); al[q] = life * (1f - 0.3f * u); }
                _crowns.Strip(pts, w, null, Color.white, al, j * 0.08f, cam);
            }
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            _hitLight.intensity = h < 0.05f ? 5f * h / 0.05f : 5f * Mathf.Exp(-(h - 0.05f) * 4f);
            // the jet drains into the splash
            _jet.Clear(); float jl = 1f - Vol2Fs.Smooth(0f, 0.25f, h); if (jl > 0.01f) Vol2FsB4.Arc(_jet, From, _hit, LobHeight, Vol2Fs.Smooth(0f, 0.25f, h), 1f, 0.24f, jl, 0.2f, cam, 12); _jet.Write(_jetMesh);
            _crowns.Clear();
            for (int i = 0; i < _crownAt.Count; i++) { var c = _crownAt[i]; if (c.T < 0f) { c.T = t; _crownAt[i] = c; } Crown(c.At, t - c.T, c.K, cam); }
            // the struck first body streams wet (the 4 s drench mirror; the wet state is the sink's)
            foreach (var s in Direct) if (t - s.At < 4f && Time.frameCount % 2 == 0) Vol2FsB1.Emit(_drips, CentreNow(s), 1);
            // the ropes: arc out from the source over the hop's travel (a growing segment), stand flowing while the mark stands, sag and fall on its end
            _ropes.Clear(); _cores.Clear();
            foreach (var hop in Hops)
            {
                if (!(hop.Data is Rope r)) continue;
                float g = HopK(hop, t); if (g <= 0f) continue;
                float k = ShareScale(hop.Share);
                float ex = hop.Mark != null ? MarkExit(hop.Mark, t) : (t > hop.Start + 3.5f ? 1f : 0f);
                float warn = hop.Mark != null ? MarkWarn(hop.Mark, t) : 0f;
                float stand = 1f - Vol2Fs.Smooth(0f, 0.9f, ex);
                if (stand <= 0.01f) continue;
                r.B = CentreNow(hop) + Vector3.up * 0.25f;
                float lift = r.Lift * (1f - 0.8f * Vol2Fs.Smooth(0f, 0.8f, ex));      // the rope sags and falls (flows down)
                float u0 = g < 1f ? Mathf.Max(0f, g - 0.85f) : Vol2Fs.Smooth(0.4f, 0.9f, ex);
                float width = 0.42f * k * (1f - 0.25f * warn) * (0.85f + 0.15f * Mathf.Sin(t * 9f + r.Phase));
                Vol2FsB4.Arc(_ropes, r.A, r.B, lift, u0, g, width, stand, 0.15f + hop.Order * 0.21f, cam, 18, 0.06f, t * 6f + r.Phase);
                Vol2FsB4.Arc(_cores, r.A, r.B, lift, u0, g, width * 0.32f, stand * 0.9f, 0.4f + hop.Order * 0.17f, cam, 18, 0.12f, t * 9f + r.Phase + 1.3f);
                // drops fall off the rope; the head sheds spray
                if (Time.frameCount % 2 == 0) Vol2FsB1.Emit(_drops, Vol2FsB4.ArcPoint(r.A, r.B, lift, Random.Range(u0, g)), ex > 0f ? 5 : 1);
                if (g < 1f) Vol2FsB1.Emit(_drops, Vol2FsB4.ArcPoint(r.A, r.B, lift, g), 4);
            }
            _ropes.Write(_ropeMesh); _cores.Write(_coreMesh); _crowns.Write(_crownMesh);
            // the marks: rippling puddles (slower in the warning), the body dripping; natural expiry = the puddle drains (shrinks)
            _puddles.Clear();
            foreach (var m in MarkList)
            {
                float life = MarkLife(m, t, 0.2f), warn = MarkWarn(m, t), ex = MarkExit(m, t);
                if (life <= 0.01f) continue;
                float size = m.Radius * 3.2f * ShareScale(m.Share) * (Natural(m) ? 1f - 0.5f * ex : 1f);
                _puddles.Quad(m.Centre + Vector3.up * 0.022f, Right, Fwd, size, new Color(1f, 1f, 1f, life), 0.3f + m.Id % 5 * 0.11f);
                // ripple rings: a few widening rings (slower in the warning)
                float speed = Mathf.Lerp(1f, 0.35f, warn);
                for (int j = 0; j < 2; j++)
                {
                    float ph = Mathf.Repeat(t * 0.9f * speed + j * 0.5f + m.Id * 0.13f, 1f);
                    float ra = (1f - ph) * life;
                    Vol2FsB2.GroundRing(_puddles, m.Centre, Fwd, Ground, size * 0.5f * ph * 0.85f, size * 0.5f * ph * 0.85f + 0.06f, 28, _ => ra, 0.7f, cam, 0.03f);
                }
                if (m.Hop != null && m.EndAt < 0f && Time.frameCount % 2 == 0) Vol2FsB1.Emit(_drips, CentreNow(m.Hop), 1);
            }
            _puddles.Write(_puddleMesh);
        }
    }
}
