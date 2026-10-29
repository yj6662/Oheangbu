using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>몬 (earth, ㅗ fan, 번진다) - free showcase batch 4, concept mon_a (plough furrows: the swing ploughs the fan into raised furrows of churned clods with
    /// glowing cracks; at the fan's edge the furrows keep RUNNING outward over the ground to the bears outside and heave up under them, shoving them; black brush
    /// sweeps over the furrows; cracked mounds at their feet). Low running ridges of clods - not 모's standing slab wave, not 목's columns. Beats, all executor facts:
    /// charge -> the launch = five furrows plough out across the fan (the spec's apex / depth / half angle), the body inside is shoved (Instant) -> when the fan has
    /// finished the executor spreads from its edge: every "Spread" contact = one furrow running on from hop.FromP to that neighbour (its clods / width by the
    /// share) and a heave under it -> its Mark (3 s) = a cracked mound of tilted clods at the feet; warning = it shudders, dust trickles -> natural expiry (CSV:
    /// 가라앉으며 둘레 땅이 판으로 들림) = the mound sinks and a ring of ground plates lifts round it and settles; other reasons = shorter exits.</summary>
    public sealed class Vol2FsMon : Vol2FsSpread
    {
        static readonly Color Soil = new Color(0.42f, 0.27f, 0.18f), Gold = new Color(1f, 0.62f, 0.25f), Ink = new Color(0.03f, 0.025f, 0.02f);
        ParticleSystem _charge, _dust, _clods, _bigDust, _grit; Light _chargeLight; Light[] _fanLights;
        Material _rock, _rockDark; Mesh[] _rocks; Mesh _slab;
        Mesh _crackMesh, _swoopMesh; readonly Vol2FsStrips _crack = new Vol2FsStrips(), _swoop = new Vol2FsStrips();

        sealed class Clod { public Transform T; public Vector3 P; public Quaternion R; public float At, Size, Sink; public bool Kicked; }
        readonly List<Clod> _fanClods = new List<Clod>();
        struct Furrow { public Vector3 A, B; public float Rnd; }
        readonly List<Furrow> _furrows = new List<Furrow>();
        sealed class HopLook { public readonly List<Clod> Clods = new List<Clod>(); public readonly List<Clod> Heave = new List<Clod>(); public Vector3 A, B; public bool Heaved; }
        sealed class MarkLook { public readonly List<Clod> Mound = new List<Clod>(); public readonly List<Clod> Plates = new List<Clod>(); public float ExitAt = -1f; }

        protected override float HopSpeed => 9f;
        protected override float HopMin => 0.28f;
        protected override float HopMax => 0.5f;
        protected override float HopStagger => 0.12f;
        protected override float MarkExpireSeconds => 1.1f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB1.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _rock = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.62f, 0.46f, 0.36f), 0f, 0.18f, new Color(0.12f, 0.07f, 0.04f), "fs_rock");
            _rockDark = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.45f, 0.33f, 0.26f), 0f, 0.15f, new Color(0.9f, 0.45f, 0.12f), "fs_vein");
            _rocks = new Mesh[4];
            for (int k = 0; k < 4; k++) { int kk = k; _rocks[k] = Kit.SharedMesh("b4mon_rock" + k, () => Vol2FsB1.Rock(700 + kk, 0.5f, 0.7f, 10, 7)); }
            _slab = Kit.SharedMesh("b4mon_slab", () => Vol2FsB1.Slab(91, new Vector3(0.9f, 0.22f, 0.7f), 0.8f));
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.55f, 0.4f, 0.3f, 1f), 0.5f)), Rate = 40f, Loop = true, Life0 = 0.2f, Life1 = 0.35f, Speed0 = -1.4f, Speed1 = -2.2f, Size0 = 0.12f, Size1 = 0.3f, Radius = 0.6f, World = false, Tiles = 2, Max = 60 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", Gold, 3f);
            _charge.transform.position = Tip(); _charge.Play(false);
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.55f, 0.4f, 0.3f, 1f), 0.5f)), Life0 = 0.8f, Life1 = 1.6f, Speed0 = 0.5f, Speed1 = 2f, Size0 = 0.5f, Size1 = 1.1f, Radius = 0.3f, Drag = 1.2f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.2f, SizePeakAt = 0.4f, SizeEnd = 1.6f, VelOverLife = new Vector3(0f, 0.6f, 0f), Max = 600, SortFudge = 8 }, root, "Dust");
            _bigDust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.6f, 0.45f, 0.33f, 1f), 0.6f)), Life0 = 1f, Life1 = 1.8f, Speed0 = 1.5f, Speed1 = 4f, Size0 = 1f, Size1 = 1.9f, Radius = 0.5f, Drag = 1.6f, Tiles = 2,
                SizeStart = 0.4f, SizePeak = 1.2f, SizePeakAt = 0.35f, SizeEnd = 1.7f, Max = 200, SortFudge = 8 }, root, "BigDust");
            _clods = Vol2Fs.Build(new Vol2FsPs { Mat = _rock, Mesh = _rocks[1], Life0 = 0.8f, Life1 = 1.4f, Speed0 = 2.5f, Speed1 = 6f, Size0 = 0.08f, Size1 = 0.24f, Shape = ParticleSystemShapeType.Cone, Angle = 35f, Radius = 0.25f,
                ShapeRot = new Vector3(-90f, 0f, 0f), Gravity = 2.2f, Spin = 360f, AlphaHoldTo = 0.9f, Max = 600 }, root, "Clods");
            var grit = M("fs_ink", new Color(0.3f, 0.2f, 0.14f, 1f), 1f, 0f); grit.NoiseAmt = 0.4f; grit.Soft = 0.1f;
            _grit = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(grit), Life0 = 0.5f, Life1 = 1.1f, Speed0 = 1.5f, Speed1 = 5f, Size0 = 0.04f, Size1 = 0.12f, Shape = ParticleSystemShapeType.Cone, Angle = 45f, Radius = 0.2f,
                ShapeRot = new Vector3(-90f, 0f, 0f), Gravity = 2.4f, Tiles = 2, AlphaHoldTo = 0.7f, Max = 900 }, root, "Grit");
            foreach (var ps in new[] { _dust, _bigDust, _clods, _grit }) ps.Play(false);
            var crack = M("fs_crack", new Color(1f, 0.7f, 0.35f, 1f), 2.2f, 0.6f); crack.Soft = 0.001f; crack.NoiseAmt = 0.3f; crack.Opacity = 0.9f;
            Vol2Fs.MeshObject(root, "Cracks", Kit.Mat(crack), out _crackMesh);
            Vol2Fs.MeshObject(root, "InkSwoops", Kit.InkSolid("fs_stroke2", Ink, new Color(0.9f, 0.55f, 0.25f, 1f), 0.08f, 0.5f, 0.3f, 1.5f), out _swoopMesh);
            _fanLights = new Light[2];
            for (int k = 0; k < 2; k++) _fanLights[k] = Vol2Fs.PointLight(root, "CrackLight" + k, Gold, 5f);
        }

        Clod NewClod(string name, Vector3 p, float size, float at, bool dark, int seed, Mesh mesh = null)
        {
            var r = Vol2FsB1.Prop(Root.transform, name, mesh ?? _rocks[seed & 3], dark ? _rockDark : _rock);
            var c = new Clod { T = r.transform, P = p, Size = size, At = at, R = Quaternion.Euler(Vol2Fs.Hash(seed, 1) * 360f, Vol2Fs.Hash(seed, 2) * 360f, Vol2Fs.Hash(seed, 3) * 360f) };
            r.transform.localScale = Vector3.zero;
            return c;
        }

        /// <summary>A clod rising out of the ground at its time (overshoot), held, then sinking by <paramref name="sink"/> 0..1.</summary>
        void Pose(Clod c, float t, float sink)
        {
            float a = Vol2Fs.EaseOutBack(Mathf.Clamp01((t - c.At) / 0.22f));
            if (t < c.At) { c.T.localScale = Vector3.zero; return; }
            c.T.position = c.P + Vector3.up * (c.Size * (0.25f * a - 0.45f) - c.Size * 0.9f * sink);
            c.T.rotation = c.R;
            c.T.localScale = Vector3.one * c.Size * Mathf.Clamp01(a + 0.2f) * (1f - 0.3f * sink);
        }

        protected override void OnCharge(float t, float k) { _charge.transform.position = Tip(); _chargeLight.intensity = 3f * k; }
        protected override void OnLaunch(long nowUs) { _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f; }

        protected override void OnHit(long nowUs)
        {
            int seed = Seed + 71;
            // five furrows ploughed out from a quarter of the depth to the edge, clods heaving in a moving front
            for (int f = 0; f < 5; f++)
            {
                float v = (f / 4f) * 2f - 1f;
                Vector3 a = Vol2FsB3.InFan(Apex, F, Depth, Half, 0.3f, v * 0.9f, Ground), b = Vol2FsB3.InFan(Apex, F, Depth, Half, 1f, v * 0.95f, Ground);
                _furrows.Add(new Furrow { A = a, B = b, Rnd = Vol2Fs.Hash(seed, f) });
                int n = 9;
                for (int k = 0; k < n; k++)
                {
                    float u = (k + 0.5f) / n;
                    Vector3 side = Vector3.Cross(Vector3.up, FlatN(b - a));
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector3 p = Vector3.Lerp(a, b, u) + side * s * (0.22f + 0.1f * Vol2Fs.Hash(seed, f * 40 + k * 2 + s));
                        _fanClods.Add(NewClod("FurrowClod", p, Mathf.Lerp(0.35f, 0.6f, Vol2Fs.Hash(seed, f * 50 + k + s)) * Mathf.Lerp(0.8f, 1.1f, u), HitAt + 0.32f * u + 0.03f * Vol2Fs.Hash(seed, f + k), k % 3 == 0, seed + f * 97 + k * 7 + s));
                    }
                }
            }
            for (int k = 0; k < 2; k++) _fanLights[k].transform.position = Vol2FsB3.InFan(Apex, F, Depth, Half, 0.7f, k == 0 ? -0.5f : 0.5f, Ground) + Vector3.up * 0.4f;
        }

        protected override void OnStruck(Struck s)
        {
            Vector3 c = CentreNow(s), feet = G(FeetOf(s)), away = FlatN(feet - Inst.Origin);
            Vol2FsB1.Emit(_bigDust, feet + Vector3.up * 0.3f, 3); Vol2FsB1.Emit(_clods, feet, 30); Vol2FsB1.Emit(_grit, feet, 60);
            for (int k = 0; k < 5; k++) Vol2FsB1.EmitV(_dust, c + Random.insideUnitSphere * 0.4f, away * Random.Range(3f, 6f) + Vector3.up * 0.4f, 0.6f, 0.7f);      // the shove
        }

        protected override void OnHop(Hop h)
        {
            var hl = new HopLook(); float k = ShareScale(h.Share);
            hl.A = h.FromP; hl.B = G(FeetOf(h)) - FlatN(FeetOf(h) - h.FromP) * 0.3f;
            Vector3 dir = FlatN(hl.B - hl.A), side = Vector3.Cross(Vector3.up, dir);
            float len = Vector3.Distance(hl.A, hl.B); int n = Mathf.Clamp(Mathf.RoundToInt(len * 3.2f), 4, 12);
            for (int j = 0; j < n; j++)
            {
                float u = (j + 0.5f) / n;
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 p = Vector3.Lerp(hl.A, hl.B, u) + side * s * (0.2f + 0.08f * Vol2Fs.Hash(Seed, (int)h.TargetId + j * 3 + s)) * k;
                    var c = NewClod("RunClod", p, Mathf.Lerp(0.35f, 0.55f, Vol2Fs.Hash(Seed, (int)h.TargetId * 3 + j + s)) * k, h.Start + h.Travel * u, j % 2 == 0, Seed + (int)h.TargetId * 31 + j * 5 + s);
                    hl.Clods.Add(c);
                }
            }
            // the heave under the neighbour: slabs tilting up round its feet
            Vector3 feet = G(FeetOf(h));
            for (int j = 0; j < 6; j++)
            {
                float a = j / 6f * Mathf.PI * 2f + Vol2Fs.Hash(Seed, j + (int)h.TargetId);
                Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var c = NewClod("Heave", feet + o * 0.75f * k, (0.9f + 0.3f * Vol2Fs.Hash(Seed, j + 200)) * k, h.Start + h.Travel, true, Seed + j * 13 + (int)h.TargetId, _slab);
                c.R = Quaternion.LookRotation(o, Vector3.up) * Quaternion.Euler(-35f - 20f * Vol2Fs.Hash(Seed, j + 300), 0f, 0f);
                hl.Heave.Add(c);
            }
            h.Data = hl;
        }

        protected override void OnLand(Hop h)
        {
            Vector3 feet = G(FeetOf(h)), c = CentreNow(h), away = FlatN(feet - h.FromP); float k = ShareScale(h.Share);
            Vol2FsB1.Emit(_bigDust, feet + Vector3.up * 0.3f, Mathf.RoundToInt(4 * k)); Vol2FsB1.Emit(_clods, feet, Mathf.RoundToInt(30 * k)); Vol2FsB1.Emit(_grit, feet, 70);
            for (int j = 0; j < 4; j++) Vol2FsB1.EmitV(_dust, c + Random.insideUnitSphere * 0.4f, away * Random.Range(3f, 6f) + Vector3.up * 0.5f, 0.6f, 0.7f);      // the shove (the executor's knockback is the rules')
        }

        protected override void OnMark(Mark m)
        {
            var ml = new MarkLook(); float k = ShareScale(m.Share);
            for (int j = 0; j < 7; j++)
            {
                var p = Vol2FsB3.Spiral(m.Centre, m.Radius * 1.1f * k, j, 7, Ground);
                ml.Mound.Add(NewClod("Mound", p, (0.45f + 0.25f * Vol2Fs.Hash(Seed + (int)m.Id, j)) * k, MarkStart(m) + 0.03f * j, j % 2 == 0, Seed + (int)m.Id * 7 + j));
            }
            m.Data = ml;
        }

        protected override void OnMarkEnd(Mark m)
        {
            if (!(m.Data is MarkLook ml)) return;
            ml.ExitAt = m.EndAt;
            if (m.Why == Vol2MaterialEnd.Aborted) return;
            Vol2FsB1.Emit(_dust, m.Centre + Vector3.up * 0.2f, 4);
            if (Natural(m))
            {
                // CSV: 가라앉으며 둘레 땅이 판으로 들림 - a ring of plates lifts round the sinking mound, then settles
                for (int j = 0; j < 8; j++)
                {
                    float a = j / 8f * Mathf.PI * 2f; Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var c = NewClod("Plate", m.Centre + o * 1.25f, 0.85f * ShareScale(m.Share), m.EndAt + 0.05f, false, Seed + j * 17 + (int)m.Id, _slab);
                    c.R = Quaternion.LookRotation(o, Vector3.up) * Quaternion.Euler(-22f, 0f, 0f);
                    ml.Plates.Add(c);
                }
            }
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            bool marks = false; float lastEnd = 0f; foreach (var mk in MarkList) { if (mk.EndAt < 0f) marks = true; else lastEnd = Mathf.Max(lastEnd, mk.EndAt); }
            float fieldEnd = marks || h < 1.2f ? -1f : Mathf.Max(lastEnd, HitAt + 1.2f);
            float sinkF = fieldEnd < 0f ? 0f : Vol2Fs.Smooth(fieldEnd, fieldEnd + 0.8f, t);
            foreach (var c in _fanClods)
            {
                Pose(c, t, sinkF);
                if (!c.Kicked && t >= c.At) { c.Kicked = true; Vol2FsB1.Emit(_grit, c.P, 3); if (Random.value < 0.15f) Vol2FsB1.Emit(_dust, c.P + Vector3.up * 0.2f, 1); }
            }
            // the furrows' glowing cracks and the ink swoops of the brush over them
            _crack.Clear(); _swoop.Clear();
            float reach = Vol2Fs.Smooth(0f, 0.35f, h), cl = (1f - Vol2Fs.Smooth(0.6f, 1.6f, h)) * 0.9f + 0.1f * (1f - sinkF);
            foreach (var f in _furrows) if (cl > 0.01f) Vol2FsB4.Runner(_crack, f.A, Vector3.Lerp(f.A, f.B, 1f), Ground, 0f, reach, 0.35f, cl, f.Rnd, cam, 0.06f, f.Rnd * 6f, 10, 0.03f);
            float sw = 1f - Vol2Fs.Smooth(0.4f, 1.1f, h);
            for (int s = 0; s < 3 && sw > 0.01f; s++)
            {
                float v = (s - 1) * 0.7f;
                Vector3 a = Vol2FsB3.InFan(Apex, F, Depth, Half, 0.35f, v, Ground) + Vector3.up * 0.3f, b = Vol2FsB3.InFan(Apex, F, Depth, Half, 0.95f, v * 1.15f + (s - 1) * 0.3f, Ground) + Vector3.up * 0.5f;
                Vol2FsB4.Arc(_swoop, a, b, 0.6f, 0f, reach, 0.4f, sw, 0.3f * s, cam, 12, 0.15f, s);
            }
            for (int k = 0; k < _fanLights.Length; k++) _fanLights[k].intensity = (h < 0.2f ? 4f * h / 0.2f : 2.5f) * (1f - Vol2Fs.Smooth(0.8f, 1.8f, h));
            // the running furrows to the neighbours, their heave, the ink sweep over each
            foreach (var hop in Hops)
            {
                if (!(hop.Data is HopLook hl)) continue;
                float g = HopK(hop, t), ex = hop.Mark != null ? MarkExit(hop.Mark, t) : 0f;
                float sink = hop.Mark != null && hop.Mark.EndAt >= 0f ? ex : (t > hop.Start + 4f ? 1f : 0f);
                foreach (var c in hl.Clods) { Pose(c, t, sink); if (!c.Kicked && t >= c.At) { c.Kicked = true; Vol2FsB1.Emit(_grit, c.P, 4); Vol2FsB1.Emit(_clods, c.P, 1); if (Random.value < 0.2f) Vol2FsB1.Emit(_dust, c.P + Vector3.up * 0.2f, 1); } }
                foreach (var c in hl.Heave) Pose(c, t, Mathf.Max(sink, Vol2Fs.Smooth(hop.Start + hop.Travel + 0.6f, hop.Start + hop.Travel + 1.1f, t)));
                if (g > 0f && hop.Mark != null && MarkLife(hop.Mark, t) + (g < 1f ? 1f : 0f) > 0.01f)
                {
                    float life = g < 1f ? 1f : 1f - Vol2Fs.Smooth(0f, 0.6f, t - hop.Start - hop.Travel);
                    Vol2FsB4.Runner(_crack, hl.A, hl.B, Ground, 0f, g, 0.3f * ShareScale(hop.Share), Mathf.Max(0.15f, life) * (1f - ex), 0.5f + hop.Order * 0.1f, cam, 0.12f, hop.Order, 12, 0.03f);
                    if (life > 0.01f) Vol2FsB4.Arc(_swoop, hl.A + Vector3.up * 0.4f, CentreNow(hop) + Vector3.up * 0.3f, 0.5f, Mathf.Max(0f, g - 0.6f), g, 0.42f * ShareScale(hop.Share), life, 0.7f + hop.Order * 0.2f, cam, 12, 0.12f, hop.Order * 2f);
                }
            }
            // the marks: the cracked mound (shudders in the warning), then the sink and the plates
            foreach (var m in MarkList)
            {
                if (!(m.Data is MarkLook ml)) continue;
                float warn = MarkWarn(m, t), ex = MarkExit(m, t);
                foreach (var c in ml.Mound)
                {
                    Pose(c, t, ex);
                    if (warn > 0f && t >= c.At) c.T.position += new Vector3(Mathf.Sin(t * 60f + c.Size * 9f), 0f, Mathf.Cos(t * 53f + c.Size * 7f)) * 0.025f * warn;
                }
                if (warn > 0.05f && Time.frameCount % 8 == 0) Vol2FsB1.Emit(_dust, m.Centre + Vector3.up * 0.3f, 1);
                foreach (var c in ml.Plates)
                {
                    float a = t - c.At; if (a < 0f) { c.T.localScale = Vector3.zero; continue; }
                    float up = Vol2Fs.EaseOutBack(Mathf.Clamp01(a / 0.18f)) * (1f - Vol2Fs.Smooth(0.45f, MarkExpireSeconds, a));
                    c.T.position = c.P + Vector3.down * 0.12f; c.T.rotation = Quaternion.Slerp(Quaternion.LookRotation(c.R * Vector3.forward, Vector3.up), c.R, up);
                    c.T.localScale = Vector3.one * c.Size * Mathf.Clamp01(up * 1.5f + 0.01f);
                }
                if (m.EndAt < 0f && MarkLife(m, t) > 0.3f && Time.frameCount % 10 == (int)(m.Id % 10)) Vol2FsB1.Emit(_grit, m.Centre + Vector3.up * 0.4f, 2);
            }
            _crack.Write(_crackMesh); _swoop.Write(_swoopMesh);
        }
    }
}
