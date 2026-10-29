using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>순 (metal, ㅜ ring, 번진다) - free showcase batch 4, concept sun_a (radiating light lines: a hoop of fine humming steel wire hangs round the caster at
    /// the ring; from the bear on the ring razor-thin lines of white-silver light shoot OUTWARD like spokes to the bears farther out and cut them with bright
    /// flashes; little steel spikes stay at their feet). Unlike 수 (a spinning saw of knives) and 숙 (swords planted in a circle) the ring is a taut wire and the
    /// spread is a straight line of light pulled out from the struck body.
    /// Beats, all executor facts: the wire hoop for the timed emitter's life (3 s), sparks running round it -> the body on the band (Instant) = an X of light and
    /// sparks -> every "Spread" contact = a spoke of light shot from that body to the neighbour (executor order), its brightness / thickness by share; on arrival a
    /// cut flash -> its Mark (3 s) = steel spikes stuck round the feet, the spoke still taut between the two; warning = they flicker -> natural expiry (CSV 쨍하게
    /// 끊기며 파편이 옆으로 튐) = the spoke and spikes snap with a ringing flash, shards flying sideways.</summary>
    public sealed class Vol2FsSun : Vol2FsSpread
    {
        static readonly Color Silver = new Color(0.85f, 0.92f, 1f), Gold = new Color(1f, 0.88f, 0.6f), Ink = new Color(0.03f, 0.03f, 0.035f);
        ParticleSystem _charge, _sparks, _shards, _glints; Light _chargeLight, _ringLight;
        Material _steel, _steelHot;
        readonly Vol2FsTubes _hoop = new Vol2FsTubes(); Mesh _hoopMesh;
        Mesh _lightMesh, _flashMesh, _glowRingMesh; readonly Vol2FsStrips _light = new Vol2FsStrips(), _flash = new Vol2FsStrips(), _glowRing = new Vol2FsStrips();
        readonly List<(Vector3 At, Vector3 A, Vector3 B, float T, float Size)> _cuts = new List<(Vector3, Vector3, Vector3, float, float)>();
        Struck _first;

        sealed class HopLook { public Vol2FsTubes Wire = new Vol2FsTubes(); public Mesh WireMesh; public MeshRenderer WireR; public bool Snapped; }
        sealed class MarkLook { public readonly List<Transform> Spikes = new List<Transform>(); public readonly List<Vector3> P = new List<Vector3>(); public readonly List<Quaternion> Q = new List<Quaternion>(); public bool Snapped; }

        protected override float HopSpeed => 22f;
        protected override float HopMin => 0.12f;
        protected override float HopMax => 0.22f;
        protected override float HopStagger => 0.1f;
        protected override float MarkExpireSeconds => 0.5f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB1.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _steel = Vol2FsB4.Steel(Kit, 1f); _steelHot = Vol2FsB4.Steel(Kit, 1.8f, true);
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Silver, 4f, 1f)), Rate = 80f, Loop = true, Life0 = 0.15f, Life1 = 0.28f, Speed0 = -2f, Speed1 = -3f, Size0 = 0.03f, Size1 = 0.08f, Radius = 0.6f, World = false, Stretch = true, StretchVel = 0.05f, Max = 120 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", Silver, 3f);
            _charge.transform.position = Tip(); _charge.Play(true);
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 5f, 1f)), Life0 = 0.25f, Life1 = 0.6f, Speed0 = 3f, Speed1 = 9f, Size0 = 0.02f, Size1 = 0.05f, Radius = 0.1f, Gravity = 1.4f, Drag = 0.6f,
                Stretch = true, StretchVel = 0.035f, Max = 1500 }, root, "Sparks");
            _glints = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Silver, 3.5f, 1f)), Life0 = 0.15f, Life1 = 0.3f, Speed0 = 0f, Speed1 = 0.2f, Size0 = 0.08f, Size1 = 0.16f, Radius = 0.02f, Max = 200 }, root, "Glints");
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = _steelHot, Mesh = Kit.SharedMesh("b4sun_shard", () => Vol2FsB1.Shard(51)), Life0 = 0.6f, Life1 = 1.1f, Speed0 = 3f, Speed1 = 7f, Size0 = 0.06f, Size1 = 0.16f,
                Radius = 0.1f, Gravity = 1.6f, Spin = 900f, Max = 400 }, root, "Shards");
            foreach (var ps in new[] { _sparks, _glints, _shards }) ps.Play(false);
            Vol2Fs.MeshObject(root, "WireHoop", _steel, out _hoopMesh);
            var lt = M("fs_streak", Silver, 6f, 1f);      // r2: brighter spokes lt.NoiseAmt = 0.2f; lt.Soft = 0.01f; lt.Scroll = new Vector2(-3f, 0f);
            Vol2Fs.MeshObject(root, "LightLines", Kit.Mat(lt), out _lightMesh);
            var fl = M("fs_streak", Gold, 5f, 1f); fl.NoiseAmt = 0.15f; fl.Soft = 0.01f;
            Vol2Fs.MeshObject(root, "CutFlashes", Kit.Mat(fl), out _flashMesh);
            var gr = M("fs_ring", Silver, 1.6f, 1f); gr.NoiseAmt = 0.4f; gr.Soft = 0.001f; gr.Scroll = new Vector2(1.5f, 0f);
            Vol2Fs.MeshObject(root, "HoopShadowLight", Kit.Mat(gr), out _glowRingMesh);
            _ringLight = Vol2Fs.PointLight(root, "RingLight", Silver, 5f);
        }

        protected override void OnCharge(float t, float k) { _charge.transform.position = Tip(); _chargeLight.intensity = 3f * k; }

        protected override void OnLaunch(long nowUs)
        {
            _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f;
            // the hoop: two fine wires round the front arc at hip height, sagging a little between invisible stays; the sides sink to the ground
            for (int v = 0; v < 2; v++)
            {
                var pts = new Vector3[40];
                for (int k = 0; k < 40; k++)
                {
                    float ang = Mathf.Lerp(-1.05f, 1.05f, k / 39f);                    // r2: the front arc only (r1's hoop sides crossed the frame's bottom)
                    var p = OnBand(ang, R + v * 0.12f);
                    float sag = Mathf.Abs(Mathf.Sin(ang * 4f)) * 0.08f;
                    p.y += (0.85f + v * 0.18f - sag) * Vol2FsB4R.Front55(ang) + 0.05f;
                    pts[k] = p;
                }
                var tb = _hoop.Add(pts, 0.018f, 0.018f, 160); tb.Sides = 5; tb.Sharp = 0.02f;
            }
            _ringLight.transform.position = OnBand(0f, R) + Vector3.up * 1.2f;
        }

        protected override void OnHit(long nowUs) { }

        void Cut(Vector3 at, float size, Vector3 toward)
        {
            Vector3 sd = Vector3.Cross(Vector3.up, FlatN(toward));
            _cuts.Add((at, sd * 0.7f + Vector3.up * 0.7f, sd * 0.7f - Vector3.up * 0.7f, -1f, size));
            Vol2FsB1.Emit(_sparks, at, Mathf.RoundToInt(90 * size)); Vol2FsB1.Emit(_glints, at, 4);
        }

        protected override void OnStruck(Struck s)
        {
            if (_first != null) return;
            _first = s;
            Cut(CentreNow(s), 1.1f, CentreNow(s) - Inst.Origin);
        }

        protected override void OnHop(Hop h)
        {
            var hl = new HopLook();
            hl.WireR = Vol2Fs.MeshObject(Root.transform, "Spoke_" + h.TargetId, _steelHot, out hl.WireMesh);
            Vector3 a = h.FromP + Vector3.up * 0.95f, b = CentreNow(h);
            var tb = hl.Wire.Add(new[] { a, Vector3.Lerp(a, b, 0.5f), b }, 0.035f * ShareScale(h.Share), 0.025f * ShareScale(h.Share), 30); tb.Sides = 5; tb.Sharp = 0.05f;
            h.Data = hl;
        }

        protected override void OnLand(Hop h) { Cut(CentreNow(h), ShareScale(h.Share), CentreNow(h) - h.FromP); }

        protected override void OnMark(Mark m)
        {
            var ml = new MarkLook();
            float k = ShareScale(m.Share);
            for (int j = 0; j < 6; j++)
            {
                var r = Vol2FsB1.Prop(Root.transform, "Spike", Kit.SharedMesh("b4sun_blade", Vol2FsB1.Blade), _steel, false);
                r.gameObject.SetActive(false);
                float ang = j / 6f * Mathf.PI * 2f + Vol2Fs.Hash(Seed + (int)m.Id, j);
                Vector3 o = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                Vector3 p = m.Centre + o * (m.Radius + 0.15f * Vol2Fs.Hash(Seed, j + 9)) + Vector3.down * 0.12f;
                // blade pointing up and out of the ground, leaning outward
                Quaternion q = Quaternion.LookRotation((Vector3.up * 1.6f + o * 0.8f).normalized, o);
                r.transform.localScale = new Vector3(2.4f, 2.4f, (0.55f + 0.3f * Vol2Fs.Hash(Seed, j + 20)) * k);
                ml.Spikes.Add(r.transform); ml.P.Add(p); ml.Q.Add(q);
            }
            m.Data = ml;
        }

        protected override void OnMarkEnd(Mark m)
        {
            if (!(m.Data is MarkLook ml) || m.Why == Vol2MaterialEnd.Aborted) return;
            // snaps with a ringing flash, shards sideways
            ml.Snapped = true;
            Vector3 c = m.Centre + Vector3.up * 0.4f, sd = Vector3.Cross(Vector3.up, FlatN(m.Centre - Apex));
            for (int k = 0; k < 18; k++) Vol2FsB1.EmitV(_shards, c + Random.insideUnitSphere * 0.3f, (sd * (k % 2 == 0 ? 1f : -1f) * Random.Range(3f, 6f) + Vector3.up * Random.Range(0.5f, 2f)));
            Vol2FsB1.Emit(_sparks, c, 80); Vol2FsB1.Emit(_glints, c, 6);
            _cuts.Add((c, sd * 0.9f, -sd * 0.9f, -1f, 0.8f));
            if (m.Hop != null && m.Hop.Data is HopLook hl && !hl.Snapped)
            {
                hl.Snapped = true;
                Vector3 mid = Vector3.Lerp(m.Hop.FromP + Vector3.up * 0.95f, c, 0.5f);
                for (int k = 0; k < 8; k++) Vol2FsB1.EmitV(_shards, mid + Random.insideUnitSphere * 0.4f, Random.insideUnitSphere * 3f + Vector3.up);
                Vol2FsB1.Emit(_sparks, mid, 40);
            }
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            float life = HostLife(t, 0.15f, 0.25f), warn = HostWarn(t);
            float flick = warn > 0.01f ? (Random.value < warn * 0.5f ? 0.2f : 1f) : 1f;
            foreach (var tb in _hoop.Tubes) tb.Grow = Vol2Fs.Smooth(0f, 0.18f, h) * (life > 0.02f ? 1f : 0f);
            _hoop.Write(_hoopMesh);
            _ringLight.intensity = 2.5f * life * flick;
            _glowRing.Clear();
            if (life > 0.01f) Vol2FsB2.GroundRing(_glowRing, Apex, F, Ground, R - 0.05f, R + 0.05f, 72, a => Vol2FsB4R.Front55(a) * life * 0.6f * flick, 0.19f, cam, 0.03f, 0f, 8f);
            _glowRing.Write(_glowRingMesh);
            // sparks running round the hoop
            if (life > 0.2f && Time.frameCount % 2 == 0) { float ang = Mathf.Repeat(t * 1.2f, 1.9f) - 0.95f; var p = OnBand(ang, R); p.y += 0.85f * Vol2FsB4R.Front55(ang) + 0.05f; Vol2FsB1.Emit(_glints, p, 1); Vol2FsB1.EmitV(_sparks, p, Random.insideUnitSphere * 1.5f, 0.03f, 0.3f); }
            // when the hoop's time is over: it parts with a short sparkle
            if (EmitterEnd > 0f && t >= EmitterEnd && t < EmitterEnd + Time.deltaTime * 1.5f) for (int k = 0; k < 8; k++) { float ang = Mathf.Lerp(-0.9f, 0.9f, k / 7f); var p = OnBand(ang, R); p.y += 0.85f * Vol2FsB4R.Front55(ang); Vol2FsB1.Emit(_sparks, p, 6); }
            // the spokes of light: a bright head shot from the struck body to the neighbour, leaving a taut wire; snaps at the mark's end
            _light.Clear(); _flash.Clear();
            foreach (var hop in Hops)
            {
                if (!(hop.Data is HopLook hl)) continue;
                float g = HopK(hop, t), k = ShareScale(hop.Share);
                Vector3 a = hop.FromP + Vector3.up * 0.95f, b = CentreNow(hop);
                bool standing = !hl.Snapped && (hop.Mark == null || hop.Mark.EndAt < 0f);
                float mw = hop.Mark != null ? MarkWarn(hop.Mark, t) : 0f;
                hl.Wire.Tubes.Clear();                                           // rebuilt each frame: the neighbour may be shoved
                if (standing && g > 0f) { var nt = hl.Wire.Add(new[] { a, Vector3.Lerp(a, b, 0.5f), b }, 0.035f * k, 0.025f * k, 30); nt.Sides = 5; nt.Sharp = 0.05f; nt.Grow = g; }
                hl.Wire.Write(hl.WireMesh);
                if (g > 0f && g <= 1f && t - hop.Start < hop.Travel + 0.25f)
                {
                    float tail = Mathf.Max(0f, g - 0.35f), fade = 1f - Vol2Fs.Smooth(0f, 0.25f, t - hop.Start - hop.Travel);
                    Vector3 p0 = Vector3.Lerp(a, b, tail), p1 = Vector3.Lerp(a, b, g);
                    _light.Strip(new[] { p0, Vector3.Lerp(p0, p1, 0.5f), p1 }, new[] { 0.1f * k, 0.3f * k, 0.48f * k }, null, Color.white, new[] { 0.2f * fade, 0.7f * fade, fade }, 0.3f, cam, 0f, 1f);
                    if (g < 1f) Vol2FsB1.Emit(_sparks, p1, 3);
                }
                else if (standing && g >= 1f)
                {
                    float hum = (0.55f + 0.2f * Mathf.Sin(t * 40f + hop.Order)) * (warn > 0f ? 1f : 1f) * (mw > 0.01f && Random.value < mw * 0.6f ? 0.1f : 1f);
                    _light.Strip(new[] { a, Vector3.Lerp(a, b, 0.5f), b }, new[] { 0.12f * k, 0.17f * k, 0.12f * k }, null, Color.white, new[] { hum, hum, hum }, 0.6f, cam, 0f, 1f);
                }
            }
            // the cut flashes (an X of light)
            for (int i = 0; i < _cuts.Count; i++)
            {
                var c = _cuts[i]; if (c.T < 0f) { c.T = t; _cuts[i] = c; }
                float a = t - c.T, l = 1f - Vol2Fs.Smooth(0.05f, 0.35f, a); if (l <= 0.01f) continue;
                float g = Vol2Fs.Smooth(0f, 0.06f, a) * c.Size;
                _flash.Strip(new[] { c.At - c.A * g, c.At, c.At + c.A * g }, new[] { 0.02f, 0.1f * c.Size, 0.02f }, null, Color.white, new[] { l, l, l }, 0.1f, cam, 0f, 1f);
                _flash.Strip(new[] { c.At - c.B * g, c.At, c.At + c.B * g }, new[] { 0.02f, 0.08f * c.Size, 0.02f }, null, Color.white, new[] { l, l, l }, 0.4f, cam, 0f, 1f);
            }
            _light.Write(_lightMesh); _flash.Write(_flashMesh);
            // the spikes at the feet
            foreach (var m in MarkList)
            {
                if (!(m.Data is MarkLook ml)) continue;
                float age = MarkAge(m, t), mw = MarkWarn(m, t);
                for (int j = 0; j < ml.Spikes.Count; j++)
                {
                    var tr = ml.Spikes[j];
                    float rise = age < 0f ? 0f : Vol2Fs.EaseOutBack(Mathf.Clamp01((age - 0.03f * j) / 0.1f));
                    bool on = rise > 0.01f && !ml.Snapped && !(mw > 0.01f && Random.value < mw * 0.35f);
                    if (tr.gameObject.activeSelf != on) tr.gameObject.SetActive(on);
                    if (on) tr.SetPositionAndRotation(ml.P[j] + Vector3.down * (1f - rise) * 0.4f, ml.Q[j]);
                }
                if (age > 0f && !ml.Snapped && Time.frameCount % 7 == (int)(m.Id % 7)) Vol2FsB1.Emit(_glints, ml.P[Random.Range(0, ml.P.Count)] + Vector3.up * 0.35f, 1);
            }
        }
    }
}
