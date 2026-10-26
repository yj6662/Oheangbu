using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>문 (earth, ㅜ ring, 번진다) - free showcase batch 4, concept mun_a (radial fissures: a ring of split, churned earth circles the caster; from the bear on
    /// the ring fissures split the ground OUTWARD to the bears farther out, jagged rock teeth tearing up along each crack behind its running head, and blast up
    /// under them in a burst of rock that shoves them away; cracked mounds stay at their feet). Unlike 무 (one rolling mound ring) the ring itself stays a low crack
    /// band, and the spread is a running fault line; unlike 만 (the path's crack chain) it starts from the ring and runs outward.
    /// Beats, all executor facts: the cracked band for the timed emitter's life (3 s) -> the body on the band (Instant) = the ground bucks under it -> every
    /// "Spread" contact = a fissure running from that body to the neighbour (executor order), teeth sized by share -> the land = a rock burst under it -> its Mark
    /// (3 s) = a cracked mound of rubble; warning = it trembles and sheds grit -> natural expiry (CSV 가라앉으며 둘레 땅이 판으로 들림) = it sinks while plates of
    /// ground round it lift and settle; other reasons = a quick crumble.</summary>
    public sealed class Vol2FsMun : Vol2FsSpread
    {
        static readonly Color Soil = new Color(0.62f, 0.42f, 0.33f), Ink = new Color(0.04f, 0.03f, 0.025f), Glow = new Color(0.9f, 0.5f, 0.25f);
        ParticleSystem _charge, _rocks, _clods, _dust; Light _chargeLight, _flash;
        Material _stone;
        Mesh _bandMesh, _crackMesh, _seamMesh; readonly Vol2FsStrips _band = new Vol2FsStrips(), _crack = new Vol2FsStrips(), _seam = new Vol2FsStrips();
        Struck _first; readonly List<Transform> _heaveA = new List<Transform>();

        sealed class Tooth { public Transform T; public Vector3 P, Lean; public float U, H; }
        sealed class HopLook { public readonly List<Tooth> Teeth = new List<Tooth>(); public readonly List<Tooth> Burst = new List<Tooth>(); public float Phase; public bool Popped; }
        sealed class MarkLook { public readonly List<Transform> Mound = new List<Transform>(); public readonly List<Vector3> MoundP = new List<Vector3>(); public readonly List<Tooth> Plates = new List<Tooth>(); public float ExitAt = -1f; }

        protected override float HopSpeed => 6.5f;
        protected override float HopMin => 0.3f;
        protected override float HopMax => 0.55f;
        protected override float HopStagger => 0.12f;
        protected override float MarkExpireSeconds => 1.0f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB1.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _stone = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.8f, 0.6f, 0.5f), 0f, 0.12f, new Color(0.3f, 0.18f, 0.11f), "fs_rock");      // r2: a little more self-light (r1 slabs read as dark brown boxes)
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Glow, 2.2f, 0.8f)), Rate = 60f, Loop = true, Life0 = 0.2f, Life1 = 0.35f, Speed0 = -1.5f, Speed1 = -2.5f, Size0 = 0.05f, Size1 = 0.12f, Radius = 0.6f, World = false, Max = 90 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", Glow, 3f);
            _charge.transform.position = Tip(); _charge.Play(true);
            _rocks = Vol2Fs.Build(new Vol2FsPs { Mat = _stone, Mesh = Kit.SharedMesh("b4mun_rock", () => Vol2FsB1.Rock(141, 1f, 0.8f, 8, 6)), Life0 = 0.9f, Life1 = 1.6f, Speed0 = 3f, Speed1 = 7f, Size0 = 0.1f, Size1 = 0.3f,
                Radius = 0.2f, Gravity = 2.2f, Drag = 0.15f, Spin = 300f, Max = 400 }, root, "Rocks");
            var clod = M("fs_ink", new Color(0.3f, 0.2f, 0.15f, 1f), 1f, 0f); clod.Soft = 0.1f; clod.NoiseAmt = 0.3f;
            _clods = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(clod), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.05f, Size1 = 0.15f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.25f, Gravity = 2.4f, Tiles = 2, Max = 1600 }, root, "Clods");
            var dust = M("fs_puff", Soil, 1f, 0f); dust.Opacity = 0.7f; dust.Erode = 0.35f; dust.NoiseAmt = 0.55f;
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dust), Life0 = 0.9f, Life1 = 1.7f, Speed0 = 0.3f, Speed1 = 1.4f, Size0 = 0.6f, Size1 = 1.3f, Radius = 0.3f, Drag = 1.4f, Tiles = 2,
                SizeStart = 0.4f, SizePeak = 1.2f, SizePeakAt = 0.4f, SizeEnd = 1.7f, Max = 500, SortFudge = 8 }, root, "Dust");
            foreach (var ps in new[] { _rocks, _clods, _dust }) ps.Play(false);
            var band = M("fs_rock_crack", new Color(0.1f, 0.06f, 0.045f, 1f), 1f, 0f); band.Soft = 0.001f; band.NoiseAmt = 0.45f; band.Opacity = 0.92f; band.Edge = new Color(0.9f, 0.5f, 0.25f, 1f); band.EdgeWidth = 0.05f;
            Vol2Fs.MeshObject(root, "CrackBand", Kit.Mat(band), out _bandMesh);
            var crack = M("fs_crack", new Color(0.07f, 0.045f, 0.035f, 1f), 1f, 0f); crack.Soft = 0.001f; crack.NoiseAmt = 0.45f; crack.Edge = new Color(1.0f, 0.55f, 0.28f, 1f); crack.EdgeWidth = 0.05f;
            Vol2Fs.MeshObject(root, "Fissures", Kit.Mat(crack), out _crackMesh);
            // mun_a: the fissures' seams glow a dull amber deep in the split (a narrow additive line under the crack)
            var seam = M("fs_streak", new Color(1f, 0.55f, 0.22f), 1.6f, 1f); seam.NoiseAmt = 0.5f; seam.NoiseScale = 2f; seam.Soft = 0.001f;
            Vol2Fs.MeshObject(root, "Seams", Kit.Mat(seam), out _seamMesh);
            _flash = Vol2Fs.PointLight(root, "Flash", new Color(1f, 0.8f, 0.6f), 6f);
        }

        Transform Slab(string name, int k, Vector3 size)
        {
            var r = Vol2FsB1.Prop(Root.transform, name, Kit.SharedMesh("b4mun_slab" + (k % 5) + "_" + size.x.ToString("0.0"), () => Vol2FsB1.Slab(160 + k % 5, size, 0.55f)), _stone);
            r.gameObject.SetActive(false);
            return r.transform;
        }

        /// <summary>r2: a rough rock spike (the lumpy rock mesh stretched up) instead of r1's box-like slab - its centre sits at the pose point, half sunk.</summary>
        Transform Rough(string name, int k, Vector3 scale)
        {
            var r = Vol2FsB1.Prop(Root.transform, name, Kit.SharedMesh("b4mun_rough" + (k % 4), () => Vol2FsB1.Rock(180 + k % 4, 1f, 0.75f, 9, 6)), _stone);
            r.transform.localScale = scale;
            r.gameObject.SetActive(false);
            return r.transform;
        }

        protected override void OnCharge(float t, float k) { _charge.transform.position = Tip(); _chargeLight.intensity = 3f * k; }

        protected override void OnLaunch(long nowUs)
        {
            _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f;
            _flash.transform.position = Apex + F * (R + 0.8f) + Vector3.up * 0.8f;
        }

        protected override void OnHit(long nowUs)
        {
            for (int k = 0; k < 10; k++) { float ang = Mathf.Lerp(-0.9f, 0.9f, k / 9f); Vector3 p = OnBand(ang, R); Vol2FsB1.Emit(_clods, p, 5); if (k % 3 == 0) Vol2FsB1.Emit(_dust, p + Vector3.up * 0.2f, 1); }
        }

        protected override void OnStruck(Struck s)
        {
            if (_first != null) return;
            _first = s;
            Vector3 f = G(FeetOf(s));
            Vol2FsB1.Emit(_rocks, f + Vector3.up * 0.3f, 18); Vol2FsB1.Emit(_dust, f + Vector3.up * 0.3f, 6); Vol2FsB1.Emit(_clods, f, 40);
            // the ground bucks under it: four plates tilt up round its feet
            for (int k = 0; k < 3; k++) { var tr = Rough("HeaveA", k, new Vector3(0.55f, 0.75f, 0.45f)); _heaveA.Add(tr); }
        }

        protected override void OnHop(Hop h)
        {
            var hl = new HopLook { Phase = Vol2Fs.Hash(Seed, (int)h.TargetId) * 6f };
            float k = ShareScale(h.Share);
            Vector3 a = G(h.FromP), b = G(FeetOf(h)); Vector3 d = FlatN(b - a), sd = Vector3.Cross(Vector3.up, d);
            float len = Vector3.Distance(a, b);
            int n = Mathf.Clamp(Mathf.RoundToInt(len * 1.4f), 3, 7);          // r2: fewer, rougher teeth
            for (int j = 0; j < n; j++)
            {
                float u = (j + 0.6f) / (n + 0.4f);
                var tr = Rough("Tooth", j + (int)h.TargetId, new Vector3(0.32f, 0.62f, 0.28f) * (0.8f + 0.4f * Vol2Fs.Hash(Seed, j + 77)));
                Vector3 p = Vector3.Lerp(a, b, u) + sd * (Mathf.Sin(u * 9f + hl.Phase) * 0.14f + ((j % 2) * 2 - 1) * 0.16f);
                p.y = Ground + 0.15f;
                hl.Teeth.Add(new Tooth { T = tr, P = p, U = u, H = (0.5f + 0.5f * Vol2Fs.Hash(Seed, j + (int)h.TargetId)) * k, Lean = (sd * ((j % 2) * 2 - 1) * 0.6f + d * 0.4f).normalized });
            }
            for (int j = 0; j < 4; j++)
            {
                float ang = j / 4f * Mathf.PI * 2f + hl.Phase;
                Vector3 o = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                var tr = Rough("Burst", j + 40, new Vector3(0.5f, 0.95f, 0.42f));
                hl.Burst.Add(new Tooth { T = tr, P = b + o * 0.65f + Vector3.up * 0.25f, U = 1f, H = (0.9f + 0.4f * Vol2Fs.Hash(Seed, j + 90)) * k, Lean = o });
            }
            h.Data = hl;
        }

        protected override void OnLand(Hop h)
        {
            Vector3 f = G(FeetOf(h)); float k = ShareScale(h.Share);
            Vol2FsB1.Emit(_rocks, f + Vector3.up * 0.4f, Mathf.RoundToInt(26 * k)); Vol2FsB1.Emit(_clods, f, Mathf.RoundToInt(60 * k)); Vol2FsB1.Emit(_dust, f + Vector3.up * 0.4f, Mathf.RoundToInt(8 * k));
            _flash.transform.position = f + Vector3.up * 1.0f - FlatN(f - Apex) * 0.6f; _flash.intensity = 5f;
        }

        protected override void OnMark(Mark m)
        {
            var ml = new MarkLook();
            float k = ShareScale(m.Share);
            for (int j = 0; j < 7; j++)
            {
                var r = Vol2FsB1.Prop(Root.transform, "Mound", Kit.SharedMesh("b4mun_lump" + (j % 3), () => Vol2FsB1.Rock(170 + j % 3, 1f, 0.6f, 10, 7)), _stone);
                r.gameObject.SetActive(false);
                var p = Vol2FsB3.Spiral(m.Centre, m.Radius * 0.9f, j, 7, Ground);
                ml.Mound.Add(r.transform); ml.MoundP.Add(p);
                r.transform.localScale = Vector3.one * (0.28f + 0.16f * Vol2Fs.Hash(Seed + (int)m.Id, j)) * k;
                r.transform.rotation = Quaternion.Euler(Vol2Fs.Hash(Seed, j) * 360f, Vol2Fs.Hash(Seed, j + 5) * 360f, 0f);
            }
            for (int j = 0; j < 6; j++)
            {
                float ang = j / 6f * Mathf.PI * 2f + 0.3f;
                Vector3 o = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                ml.Plates.Add(new Tooth { T = Slab("Plate", j + 70, new Vector3(0.8f, 0.2f, 0.6f)), P = m.Centre + o * (m.Radius + 0.75f), Lean = o, H = 1f });
            }
            m.Data = ml;
        }

        protected override void OnMarkEnd(Mark m)
        {
            if (!(m.Data is MarkLook ml)) return;
            ml.ExitAt = m.EndAt;
            if (m.Why == Vol2MaterialEnd.Aborted) return;
            Vol2FsB1.Emit(_dust, m.Centre + Vector3.up * 0.3f, Natural(m) ? 3 : 6); Vol2FsB1.Emit(_clods, m.Centre, Natural(m) ? 15 : 40);
        }

        static void Pose(Transform t, Vector3 p, Vector3 lean, float rise, float tiltDeg, float sink = 0f)
        {
            if (t == null) return;
            bool on = rise > 0.01f;
            if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
            if (!on) return;
            Vector3 axis = Vector3.Cross(Vector3.up, lean); if (axis.sqrMagnitude < 1e-4f) axis = Vector3.right;
            t.SetPositionAndRotation(p + Vector3.down * (sink + (1f - rise) * 0.5f), Quaternion.AngleAxis(tiltDeg, axis.normalized) * Quaternion.LookRotation(lean.sqrMagnitude > 1e-4f ? lean : Vector3.forward, Vector3.up));
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            _band.Clear(); _crack.Clear(); _seam.Clear();
            float life = HostLife(t, 0.12f, 0.5f), warn = HostWarn(t);
            if (life > 0.01f) { Vol2FsB2.GroundRing(_band, Apex, F, Ground, R - 0.14f, R + 0.14f, 72, a => Vol2FsB4R.Front55(a) * Mathf.Min(1f, life * 1.3f) * 0.75f, 0.53f, cam, 0.02f, 0f, 5f); Vol2FsB2.GroundRing(_seam, Apex, F, Ground, R - 0.04f, R + 0.04f, 72, a => Vol2FsB4R.Front55(a) * life * 0.6f, 0.47f, cam, 0.026f, 0f, 5f); }      // r2: a thin front crack only (r1's dark arc swept the frame's bottom)
            if (life > 0.2f && Time.frameCount % (warn > 0.01f ? 2 : 6) == 0) { float ang = Random.Range(-0.9f, 0.9f); if (Vol2FsB4R.Front55(ang) > 0.3f) Vol2FsB1.Emit(_clods, OnBand(ang, R), warn > 0.01f ? 3 : 1); }
            _flash.intensity = Mathf.MoveTowards(_flash.intensity, 0f, Time.deltaTime * 12f);
            // the ground bucking under the body on the band (the shove's beat)
            if (_first != null)
            {
                float a = t - _first.At, rise = Vol2Fs.EaseOutBack(Mathf.Clamp01(a / 0.16f)) * (1f - Vol2Fs.Smooth(0.9f, 1.4f, a));
                Vector3 f = G(FeetOf(_first)), o = FlatN(f - Apex);
                for (int k = 0; k < _heaveA.Count; k++) { Vector3 dir = Quaternion.AngleAxis(-55f + k * 55f, Vector3.up) * o; Pose(_heaveA[k], f + dir * 0.7f + Vector3.up * 0.15f, dir, rise, 35f * rise); }
            }
            // the fissures and their rock teeth, the burst under the neighbour
            foreach (var hop in Hops)
            {
                if (!(hop.Data is HopLook hl)) continue;
                float g = HopK(hop, t), ex = hop.Mark != null ? MarkExit(hop.Mark, t) : (t > hop.Start + 3.5f ? 1f : 0f);
                float fl = (1f - Vol2Fs.Smooth(0f, 0.7f, ex));
                if (g > 0f && fl > 0.01f)
                {
                    Vol2FsB4.Runner(_crack, hop.FromP, G(FeetOf(hop)), Ground, 0f, g, 0.75f * ShareScale(hop.Share), fl, 0.2f + hop.Order * 0.19f, cam, 0.16f, hl.Phase, 18, 0.018f);
                    float sg = fl * (0.55f + 0.45f * (1f - Vol2Fs.Smooth(0f, 1.2f, t - hop.Start - hop.Travel)));
                    Vol2FsB4.Runner(_seam, hop.FromP, G(FeetOf(hop)), Ground, 0f, g, 0.16f * ShareScale(hop.Share), sg, 0.4f + hop.Order * 0.13f, cam, 0.16f, hl.Phase, 18, 0.024f);
                }
                if (g > 0f && g < 1f)
                {
                    Vector3 head = Vector3.Lerp(G(hop.FromP), G(FeetOf(hop)), g);
                    Vol2FsB1.Emit(_clods, head, 4); if (Time.frameCount % 3 == 0) Vol2FsB1.Emit(_dust, head + Vector3.up * 0.15f, 1);
                }
                foreach (var tooth in hl.Teeth)
                {
                    float since = (g - tooth.U) * hop.Travel;
                    float rise = since <= 0f ? 0f : Vol2Fs.EaseOutBack(Mathf.Clamp01(since / 0.12f)) * (1f - Vol2Fs.Smooth(0.9f, 1.5f, t - hop.Start - hop.Travel));
                    Pose(tooth.T, tooth.P, tooth.Lean, rise, 25f, (1f - tooth.H) * 0.3f);
                }
                if (g >= 1f && !hl.Popped) hl.Popped = true;
                float bt = t - hop.Start - hop.Travel;
                foreach (var b in hl.Burst)
                {
                    float rise = bt <= 0f ? 0f : Vol2Fs.EaseOutBack(Mathf.Clamp01(bt / 0.14f)) * (1f - Vol2Fs.Smooth(0.6f, 1.1f, bt));
                    Pose(b.T, b.P, b.Lean, rise, 40f * rise, (1.2f - b.H) * 0.35f);
                }
            }
            // the marks: a cracked mound of rubble; trembles in the warning; natural expiry sinks it while plates round it lift and settle
            foreach (var m in MarkList)
            {
                if (!(m.Data is MarkLook ml)) continue;
                float age = MarkAge(m, t), mw = MarkWarn(m, t);
                float up = age < 0f ? 0f : Vol2Fs.EaseOutBack(Mathf.Clamp01(age / 0.2f));
                float sink = ml.ExitAt < 0f ? 0f : Natural(m) ? Vol2Fs.Smooth(0f, 0.8f, t - ml.ExitAt) : Vol2Fs.Smooth(0f, 0.35f, t - ml.ExitAt);
                Vector3 shake = mw > 0.01f ? Random.insideUnitSphere * 0.035f * mw : Vector3.zero;
                for (int j = 0; j < ml.Mound.Count; j++)
                {
                    var tr = ml.Mound[j]; bool on = up > 0.01f && sink < 0.99f;
                    if (tr.gameObject.activeSelf != on) tr.gameObject.SetActive(on);
                    if (on) tr.position = ml.MoundP[j] + Vector3.up * (0.05f * up - 0.45f * sink - (1f - up) * 0.3f) + shake;
                }
                if (mw > 0.3f && Time.frameCount % 4 == (int)(m.Id % 4)) Vol2FsB1.Emit(_clods, m.Centre + Vector3.up * 0.3f, 2);
                if (ml.ExitAt >= 0f && Natural(m))
                {
                    float pk = t - ml.ExitAt; float lift = Vol2Fs.Smooth(0.15f, 0.35f, pk) * (1f - Vol2Fs.Smooth(0.6f, 1.0f, pk));
                    foreach (var p in ml.Plates) Pose(p.T, p.P, p.Lean, lift > 0.01f ? 1f : 0f, 28f * lift);
                }
                else foreach (var p in ml.Plates) Pose(p.T, p.P, p.Lean, 0f, 0f);
                float cl = (age < 0f ? 0f : Vol2Fs.Smooth(0f, 0.15f, age)) * (1f - Vol2Fs.Smooth(0.3f, 1f, ml.ExitAt < 0f ? 0f : t - ml.ExitAt));
                if (cl > 0.01f) _crack.Quad(m.Centre + Vector3.up * 0.02f, Right, Fwd, 2.2f * ShareScale(m.Share), new Color(1f, 1f, 1f, cl), 0.37f + m.Id % 5 * 0.1f);
            }
            _band.Write(_bandMesh); _crack.Write(_crackMesh); _seam.Write(_seamMesh);
        }
    }
}
