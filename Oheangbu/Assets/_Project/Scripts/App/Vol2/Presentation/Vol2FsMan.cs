using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>만 (earth, ㅏ path, 번진다) - free showcase batch 4, concept man_a (ground rupture chain: the earth bolt slams the first bear back and from its feet
    /// bright fault cracks RACE through the ground to every neighbour, and under each the ground bursts up in a heave of dust, soil and heavy ink that shoves it).
    /// Beats, all executor facts: charge (soil and grit pulled to the tip) -> a seamed clod of rock flies to the predicted first body -> the hit (dust, rock, low heavy
    /// ink, a crack star) -> every "Spread" contact = one glowing zig-zag fault running along the ground from the struck body to that neighbour in the executor's
    /// order (its brightness / width by the share: 1.0 split n ways) -> the heave under the neighbour -> its Mark (3 s) = a cracked mound with the fault still
    /// glowing; warning = it trembles and dust trickles -> natural expiry (CSV: 가라앉으며 둘레 땅이 판으로 들림) = the mound sinks and plates of ground lift round it.</summary>
    public sealed class Vol2FsMan : Vol2FsSpread
    {
        static readonly Color Amber = new Color(1f, 0.62f, 0.25f), Ink = new Color(0.04f, 0.03f, 0.025f), Soil = new Color(0.42f, 0.27f, 0.18f);
        ParticleSystem _charge, _dust, _column, _chunks, _grit, _drops, _sparks; Light _hitLight;
        Material _rock, _slabMat; Mesh _rockMesh;
        Transform _bolt; Light _boltLight;
        Mesh _faultMesh, _crackMesh, _burstMesh; readonly Vol2FsStrips _fault = new Vol2FsStrips(), _cracks = new Vol2FsStrips(), _burstS = new Vol2FsStrips();
        readonly List<(List<Vol2FsB1.Stroke> S, Vector3 At, float T)> _bursts = new List<(List<Vol2FsB1.Stroke>, Vector3, float)>();
        Vector3 _hit;

        sealed class Fault { public List<Vector3> Pts = new List<Vector3>(); public List<float> Acc = new List<float>(); public float Len; public bool Heaved; }
        sealed class Mound { public List<Transform> Lumps = new List<Transform>(); public List<Transform> Plates = new List<Transform>(); public List<Vector3> PlateDir = new List<Vector3>(); public bool Plated; }

        protected override float LobHeight => 0.3f;
        protected override float HopSpeed => 11f;
        protected override float HopMin => 0.2f;
        protected override float HopMax => 0.4f;
        protected override float MarkExpireSeconds => 1.1f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB1.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _rock = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.55f, 0.42f, 0.33f), 0f, 0.15f, new Color(0.9f, 0.45f, 0.15f) * 1.6f, "fs_vein");
            _slabMat = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.5f, 0.36f, 0.27f), 0f, 0.1f, new Color(0.12f, 0.08f, 0.05f), null);
            _rockMesh = Kit.SharedMesh("b4_man_rock", () => Vol2FsB1.Rock(77, 0.5f, 0.8f));
            var chunkMesh = Kit.SharedMesh("b4_man_chunk", () => Vol2FsB1.Rock(78, 0.5f, 0.7f, 8, 6));
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(Soil, 0.6f)), Rate = 50f, Loop = true, Life0 = 0.2f, Life1 = 0.35f, Speed0 = -1.6f, Speed1 = -2.6f, Size0 = 0.15f, Size1 = 0.3f, Radius = 0.7f, World = false, Tiles = 2, Max = 80 }, root, "Charge");
            _charge.transform.position = Tip(); _charge.Play(false);
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.5f, 0.36f, 0.26f), 0.6f)), Life0 = 0.9f, Life1 = 1.8f, Speed0 = 1f, Speed1 = 4.5f, Size0 = 0.8f, Size1 = 1.7f, Radius = 0.4f, Drag = 1.6f, Tiles = 2,
                SizeStart = 0.4f, SizePeak = 1.2f, SizePeakAt = 0.4f, SizeEnd = 1.6f, Gravity = -0.05f, Max = 500, SortFudge = 10 }, root, "Dust");
            // r2: a tall dust column for every heave (r1 read small at the camera A distance)
            _column = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.46f, 0.32f, 0.22f), 0.7f)), Life0 = 0.9f, Life1 = 1.5f, Speed0 = 0.5f, Speed1 = 1.5f, Size0 = 0.9f, Size1 = 1.6f, Radius = 0.35f, Drag = 0.8f, Tiles = 2,
                VelOverLife = new Vector3(0f, 3.4f, 0f), SizeStart = 0.5f, SizePeak = 1.3f, SizePeakAt = 0.35f, SizeEnd = 1.9f, Max = 300, SortFudge = 10 }, root, "Column");
            _chunks = Vol2Fs.Build(new Vol2FsPs { Mat = _slabMat, Mesh = chunkMesh, Life0 = 0.9f, Life1 = 1.6f, Speed0 = 3f, Speed1 = 7f, Size0 = 0.08f, Size1 = 0.26f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.3f, Gravity = 2.2f, Spin = 360f, Max = 300 }, root, "Chunks");
            var grit = M("fs_ink", new Color(0.3f, 0.2f, 0.13f, 1f), 1f, 0f); grit.NoiseAmt = 0.4f; grit.Soft = 0.1f;
            _grit = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(grit), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 1.5f, Speed1 = 5f, Size0 = 0.04f, Size1 = 0.13f, Shape = ParticleSystemShapeType.Cone, Angle = 30f, Radius = 0.2f, ShapeRot = new Vector3(-90f, 0f, 0f), Gravity = 2.6f, Tiles = 2, Max = 900 }, root, "Grit");
            var drop = M("fs_ink", Ink, 1f, 0f); drop.Edge = new Color(1.4f, 0.7f, 0.25f, 1f); drop.EdgeWidth = 0.1f; drop.Soft = 0.1f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.5f, Life1 = 1f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.1f, Size1 = 0.3f, Radius = 0.3f, Gravity = 2.4f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 300 }, root, "InkDrops");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Amber, 4f, 1f)), Life0 = 0.3f, Life1 = 0.7f, Speed0 = 2f, Speed1 = 5f, Shape = ParticleSystemShapeType.Cone, Angle = 25f, ShapeRot = new Vector3(-90f, 0f, 0f), Size0 = 0.025f, Size1 = 0.05f, Radius = 0.1f, Gravity = 1f, Stretch = true, StretchVel = 0.04f, Max = 600 }, root, "Sparks");
            foreach (var ps in new[] { _dust, _column, _chunks, _grit, _drops, _sparks }) ps.Play(false);
            // the fault: a bright molten line in a dark rim; the crack star decal; the heavy low ink
            var fault = M("fs_streak", new Color(1f, 0.7f, 0.35f, 1f), 4.5f, 0.9f); fault.NoiseAmt = 0.35f; fault.Edge = new Color(0.25f, 0.12f, 0.05f, 1f); fault.EdgeWidth = 0.2f; fault.Soft = 0.05f;
            Vol2Fs.MeshObject(root, "Faults", Kit.Mat(fault), out _faultMesh);
            var cr = M("fs_crack", new Color(0.06f, 0.035f, 0.02f, 1f), 1f, 0f); cr.Soft = 0.001f; cr.NoiseAmt = 0.3f; cr.Erode = 0.25f; cr.Edge = new Color(1.6f, 0.75f, 0.25f, 1f); cr.EdgeWidth = 0.1f;
            Vol2Fs.MeshObject(root, "Cracks", Kit.Mat(cr), out _crackMesh);
            Vol2Fs.MeshObject(root, "InkBursts", Kit.InkSolid("fs_stroke2", Ink, new Color(0.9f, 0.5f, 0.2f, 1f), 0.08f, 0.5f, 0.3f, 1.4f), out _burstMesh);
            _hitLight = Vol2Fs.PointLight(root, "HitLight", Amber, 7f);
        }

        protected override void OnCharge(float t, float k) { _charge.transform.position = Tip(); }

        protected override void OnLaunch(long nowUs)
        {
            _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _bolt = Vol2FsB1.Prop(Root.transform, "Clod", _rockMesh, _rock).transform;
            _bolt.position = From; _bolt.localScale = Vector3.one * 0.8f;
            _boltLight = Vol2Fs.PointLight(_bolt, "ClodLight", Amber, 3f); _boltLight.intensity = 2.5f;
        }

        protected override void OnFly(float t, float f, float prev)
        {
            Vector3 p = Line(f); _bolt.position = p; _bolt.rotation = Quaternion.Euler(f * 720f, f * 300f, 0f);
            Vol2FsB1.Emit(_dust, p, 1); Vol2FsB1.Emit(_grit, p, 3);
        }

        protected override void OnHit(long nowUs)
        {
            if (_bolt != null) _bolt.gameObject.SetActive(false);
            _hit = HasTarget ? HitPoint : AimPoint;
            Heave(_hit, G(FeetNow()), 1.1f, true);
            _hitLight.transform.position = G(_hit) + Vector3.up * 0.6f;
        }

        void Heave(Vector3 at, Vector3 feet, float k, bool first)
        {
            Vector3 fwd = FlatN(at - Inst.Origin), side = Vector3.Cross(Vector3.up, fwd);
            // low, heavy strokes thrown sideways and back (막 / 머's earth language: never the 나 star)
            _bursts.Add((Vol2FsB1.Burst(Seed + _bursts.Count * 13, first ? 9 : 7, side, fwd, 0.9f * k, 1.9f * k, 0.22f * k, 0.4f * k, 0.25f, 0.5f), feet + Vector3.up * 0.35f, -1f));
            Vol2FsB1.Emit(_dust, feet + Vector3.up * 0.3f, Mathf.RoundToInt(5 * k)); Vol2FsB1.Emit(_column, feet + Vector3.up * 0.2f, Mathf.RoundToInt(7 * k));      // r3: less dust (r2 beige blobs hid the faults) Vol2FsB1.Emit(_chunks, feet + Vector3.up * 0.2f, Mathf.RoundToInt(40 * k));
            Vol2FsB1.Emit(_grit, feet, Mathf.RoundToInt(90 * k)); Vol2FsB1.Emit(_drops, feet + Vector3.up * 0.5f, Mathf.RoundToInt(35 * k)); Vol2FsB1.Emit(_sparks, feet, Mathf.RoundToInt(40 * k));
        }

        protected override void OnHop(Hop h)
        {
            // a zig-zag fault from the source's feet to the neighbour's feet, with small side branches (the shape fixed per hop)
            var f = new Fault();
            Vector3 a = G(h.FromP), b = G(FeetOf(h)); Vector3 d = b - a; float len = d.magnitude; Vector3 dir = d / Mathf.Max(0.01f, len), side = Vector3.Cross(Vector3.up, dir);
            int n = Mathf.Max(6, Mathf.RoundToInt(len * 4f));
            for (int k = 0; k <= n; k++)
            {
                float u = k / (float)n, j = (k == 0 || k == n) ? 0f : (Vol2Fs.Hash(Seed + (int)h.TargetId, k) - 0.5f) * 0.55f;
                f.Pts.Add(a + dir * len * u + side * j);
            }
            float acc = 0f; f.Acc.Add(0f); for (int k = 1; k < f.Pts.Count; k++) { acc += Vector3.Distance(f.Pts[k - 1], f.Pts[k]); f.Acc.Add(acc); }
            f.Len = acc; h.Data = f;
        }

        protected override void OnLand(Hop h) { Heave(CentreNow(h), G(FeetOf(h)), ShareScale(h.Share), false); }

        protected override void OnMark(Mark m)
        {
            var md = new Mound(); var root = Root.transform;
            float k = ShareScale(m.Share);
            for (int j = 0; j < 7; j++)
            {
                var p = Vol2FsB3.Spiral(m.Centre, m.Radius * 1.1f, j, 7, Ground);
                var r = Vol2FsB1.Prop(root, "Lump", Kit.SharedMesh("b4_man_lump" + (j % 3), () => Vol2FsB1.Rock(90 + j % 3, 0.5f, 0.6f, 10, 7)), _slabMat).transform;
                r.position = p + Vector3.down * 0.3f; r.rotation = Quaternion.Euler(Vol2Fs.Hash(Seed, j) * 40f, Vol2Fs.Hash(Seed, j + 9) * 360f, 0f);
                r.localScale = Vector3.one * (0.5f + 0.4f * Vol2Fs.Hash(Seed + (int)m.Id, j)) * k;
                md.Lumps.Add(r);
            }
            m.Data = md;
        }

        void BuildPlates(Mark m, Mound md)
        {
            md.Plated = true; var root = Root.transform;
            for (int j = 0; j < 6; j++)
            {
                float a = j / 6f * Mathf.PI * 2f + Vol2Fs.Hash(Seed + (int)m.Id, j) * 0.4f; Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var pl = Vol2FsB1.Prop(root, "Plate", Kit.SharedMesh("b4_man_plate" + (j % 2), () => Vol2FsB1.Slab(300 + j % 2, new Vector3(0.7f, 0.12f, 0.55f), 0.8f)), _slabMat).transform;
                pl.position = m.Centre + o * (m.Radius * 1.5f); pl.rotation = Quaternion.LookRotation(o, Vector3.up);
                md.Plates.Add(pl); md.PlateDir.Add(o);
            }
        }

        protected override void OnMarkEnd(Mark m)
        {
            if (!(m.Data is Mound md) || m.Why == Vol2MaterialEnd.Aborted) return;
            if (Natural(m)) { BuildPlates(m, md); Vol2FsB1.Emit(_dust, m.Centre + Vector3.up * 0.2f, 8); }
            else { Vol2FsB1.Emit(_dust, m.Centre + Vector3.up * 0.3f, 12); Vol2FsB1.Emit(_chunks, m.Centre + Vector3.up * 0.3f, 14); }
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            _hitLight.intensity = h < 0.05f ? 9f * h / 0.05f : 9f * Mathf.Exp(-(h - 0.05f) * 4f);
            _burstS.Clear();
            for (int i = 0; i < _bursts.Count; i++) { var bs = _bursts[i]; if (bs.T < 0f) { bs.T = t; _bursts[i] = bs; } Vol2FsB1.DrawBurst(_burstS, bs.S, bs.At, t - bs.T, cam, 0.6f, 0.12f); }
            _burstS.Write(_burstMesh);
            // the faults: the molten line runs from the source to the neighbour (the hop's travel), glows while its mark stands, cools with the mark's exit
            _fault.Clear(); _cracks.Clear();
            float star = Vol2Fs.Smooth(0f, 0.12f, h) * (1f - Vol2Fs.Smooth(0f, 0.8f, t - LastEnd(h)));
            if (star > 0.01f) _cracks.Quad(G(_hit) + Vector3.up * 0.02f, Right, Fwd, 4.2f, new Color(1f, 1f, 1f, star), 0.17f);
            foreach (var hop in Hops)
            {
                if (!(hop.Data is Fault f)) continue;
                float g = HopK(hop, t); if (g <= 0f) continue;
                float ex = hop.Mark != null ? MarkExit(hop.Mark, t) : 0f, warn = hop.Mark != null ? MarkWarn(hop.Mark, t) : 0f;
                float glow = (1f - ex) * (g < 1f ? 1f : Mathf.Lerp(1f, 0.55f, Vol2Fs.Smooth(0f, 0.6f, t - hop.Start - hop.Travel))) * (1f - 0.4f * warn * Mathf.PerlinNoise(t * 20f, hop.TargetId));
                float k = ShareScale(hop.Share);
                float reach = g * f.Len;
                var pts = new List<Vector3>(); var w = new List<float>(); var al = new List<float>(); var sd = new List<Vector3>();
                for (int i = 0; i < f.Pts.Count; i++)
                {
                    if (f.Acc[i] > reach) { if (i > 0) { float u = Mathf.InverseLerp(f.Acc[i - 1], f.Acc[i], reach); var p = Vector3.Lerp(f.Pts[i - 1], f.Pts[i], u); pts.Add(p + Vector3.up * 0.03f); w.Add(0.4f * k); al.Add(glow); sd.Add(Vector3.Cross(Vector3.up, (f.Pts[i] - f.Pts[i - 1]).normalized)); } break; }
                    Vector3 tan = (f.Pts[Mathf.Min(f.Pts.Count - 1, i + 1)] - f.Pts[Mathf.Max(0, i - 1)]).normalized;
                    pts.Add(f.Pts[i] + Vector3.up * 0.03f); w.Add((0.34f + 0.12f * Mathf.Sin(i * 1.7f)) * k); al.Add(glow); sd.Add(Vector3.Cross(Vector3.up, tan));
                }
                if (pts.Count >= 2)
                {
                    _fault.Strip(pts, w, sd, Color.white, al, 0.1f + hop.Order * 0.2f, cam);
                    // r3: the flat fault was a hairline at the camera A's grazing angle - a low glowing seam standing up out of it (camera-facing)
                    var up = new List<Vector3>(pts.Count); var uw = new List<float>(pts.Count); var ua = new List<float>(pts.Count);
                    for (int i = 0; i < pts.Count; i++) { up.Add(pts[i] + Vector3.up * 0.14f); uw.Add(w[i] * 0.9f); ua.Add(al[i] * 0.85f); }
                    _fault.Strip(up, uw, null, Color.white, ua, 0.5f + hop.Order * 0.2f, cam);
                }
                // the running head throws grit and dust and sparks; the ground cracks open behind it
                if (g < 1f)
                {
                    Vector3 head = pts.Count > 0 ? pts[pts.Count - 1] : G(hop.FromP);
                    Vol2FsB1.Emit(_grit, head, 6); Vol2FsB1.Emit(_sparks, head, 8); if (Time.frameCount % 3 == 0) Vol2FsB1.Emit(_dust, head, 1);
                }
                if (glow > 0.01f) for (int i = 2; i < f.Pts.Count - 1; i += 3) if (f.Acc[i] <= reach) _cracks.Quad(f.Pts[i] + Vector3.up * 0.015f, Right, Fwd, 1.6f * k, new Color(1f, 1f, 1f, glow * 0.8f), 0.3f + i * 0.07f);
            }
            _fault.Write(_faultMesh);
            // the marks: cracked mounds (the lumps rise with the heave, tremble in the warning); natural expiry = they sink, the ground plates lift and settle
            foreach (var m in MarkList)
            {
                if (!(m.Data is Mound md)) continue;
                float age = MarkAge(m, t), warn = MarkWarn(m, t), ex = MarkExit(m, t);
                float up = age < 0f ? 0f : Vol2Fs.EaseOutBack(Mathf.Clamp01(age / 0.25f));
                float sink = Natural(m) ? Vol2Fs.Smooth(0f, 0.8f, ex) : Vol2Fs.Smooth(0f, 0.5f, ex);
                for (int j = 0; j < md.Lumps.Count; j++)
                {
                    var r = md.Lumps[j]; if (r == null) continue;
                    var p = Vol2FsB3.Spiral(m.Centre, m.Radius * 1.1f, j, 7, Ground);
                    Vector3 shake = warn > 0f ? Random.insideUnitSphere * 0.03f * warn : Vector3.zero;
                    r.position = p + Vector3.up * (-0.35f + 0.33f * up - 0.4f * sink) + shake;
                    r.gameObject.SetActive(up > 0.01f && sink < 0.99f);
                }
                if (warn > 0.2f && Time.frameCount % 5 == 0) Vol2FsB1.Emit(_grit, m.Centre + Vector3.up * 0.3f, 2);
                if (md.Plated)
                {
                    float lift = Vol2Fs.Smooth(0f, 0.35f, t - m.EndAt) * (1f - Vol2Fs.Smooth(0.5f, 1.1f, t - m.EndAt));
                    for (int j = 0; j < md.Plates.Count; j++)
                    {
                        var pl = md.Plates[j]; if (pl == null) continue;
                        Vector3 o = md.PlateDir[j];
                        pl.position = m.Centre + o * (m.Radius * 1.5f) + Vector3.up * (-0.08f + 0.12f * lift);
                        pl.rotation = Quaternion.LookRotation(o, Vector3.up) * Quaternion.Euler(-25f * lift, 0f, 0f);
                        pl.gameObject.SetActive(t - m.EndAt < 1.1f);
                    }
                }
            }
            _cracks.Write(_crackMesh);
        }

        float LastEnd(float h) { float e = -1f; foreach (var m in MarkList) { if (m.EndAt < 0f) return float.MaxValue; e = Mathf.Max(e, m.EndAt); } return e < 0f ? HitAt + 2.5f : e; }
    }
}
