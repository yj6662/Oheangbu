using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>손 (metal, ㅗ fan, 번진다) - free showcase batch 4, concept son_b (metal dust: a whirling sheet of glittering steel dust and black ink swirls sweeps the
    /// fan and spills over its edges, drifting in spirals onto the bears outside, flashing with countless tiny cuts; glints left on the ground). A swirl of
    /// glitter - not 소's fan of flying knives, not 속's stake field. Beats, all executor facts: charge -> the launch = the glitter scythe sweeps the fan left to
    /// right with ink swirls on the ground, the body inside is cut (Instant) -> when the fan has finished the executor spreads from its edge: every "Spread"
    /// contact = one corkscrew of steel dust drifting from hop.FromP onto that neighbour (its density by the share), a flurry of small cut flashes on it ->
    /// its Mark (3 s) = a slowly turning ring of glints + steel needles stuck at the feet; warning = the glints flicker -> natural expiry (CSV: 쨍하게 끊기며
    /// 파편이 옆으로) = a ringing flash, the needles snap and shards fly sideways; other reasons = shorter exits.</summary>
    public sealed class Vol2FsSon : Vol2FsSpread
    {
        static readonly Color Silver = new Color(0.82f, 0.9f, 1f), WarmGlint = new Color(1f, 0.92f, 0.75f), Ink = new Color(0.03f, 0.03f, 0.035f);
        ParticleSystem _hopGlints, _charge, _glints, _dust, _sparks, _shards, _flash; Light _chargeLight, _sweepLight;
        Mesh _swirlMesh, _ribbonMesh, _cutMesh, _ringMesh; readonly Vol2FsStrips _swirl = new Vol2FsStrips(), _ribbon = new Vol2FsStrips(), _cut = new Vol2FsStrips(), _ring = new Vol2FsStrips();
        Material _steel; Mesh _needle;
        struct Cut { public Vector3 C, D; public float At, Len; }
        readonly List<Cut> _cuts = new List<Cut>();
        struct Swirl { public float V, R0, R1, A0, Rnd; }
        readonly List<Swirl> _swirls = new List<Swirl>();

        sealed class Drift { public Light L; public float Phase; public Vector3 A, B; }
        sealed class MarkLook { public readonly List<Transform> Needles = new List<Transform>(); public readonly List<Quaternion> Rot = new List<Quaternion>(); public readonly List<Vector3> Pos = new List<Vector3>(); public bool Snapped; }

        protected override float HopSpeed => 6f;
        protected override float HopMin => 0.45f;
        protected override float HopMax => 0.7f;
        protected override float HopStagger => 0.12f;
        protected override float MarkExpireSeconds => 0.6f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB1.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _steel = Vol2FsB4.Steel(Kit, 1.2f);
            _needle = Kit.SharedMesh("b4son_needle", () => Vol2FsB1.Blade());
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Silver, 3.5f, 1f)), Rate = 110f, Loop = true, Life0 = 0.15f, Life1 = 0.3f, Speed0 = -1.8f, Speed1 = -3f, Size0 = 0.025f, Size1 = 0.06f, Radius = 0.65f, World = false, Stretch = true, StretchVel = 0.03f, Max = 150 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", Silver, 3f);
            _charge.transform.position = Tip(); _charge.Play(true);
            // the glitter: tiny bright steel motes that catch the light (stretched a little), drifting with noise
            _glints = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Silver, 4f, 1f)), Life0 = 0.6f, Life1 = 1.4f, Speed0 = 0.2f, Speed1 = 1.2f, Size0 = 0.02f, Size1 = 0.05f, Radius = 0.15f, Drag = 1.2f,
                Noise = 1.5f, NoiseFreq = 1.4f, Stretch = true, StretchVel = 0.03f, Max = 5000 }, root, "Glints");
            var dust = M("fs_puff", new Color(0.55f, 0.58f, 0.62f, 1f), 1f, 0.15f); dust.Opacity = 0.35f; dust.NoiseAmt = 0.6f; dust.Erode = 0.35f;
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dust), Life0 = 0.7f, Life1 = 1.3f, Speed0 = 0.2f, Speed1 = 0.8f, Size0 = 0.5f, Size1 = 1f, Radius = 0.2f, Drag = 1f, Tiles = 2, SizeEnd = 1.5f, Max = 500, SortFudge = 6 }, root, "SteelDust");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", WarmGlint, 5f, 1f)), Life0 = 0.25f, Life1 = 0.6f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.02f, Size1 = 0.045f, Radius = 0.15f, Gravity = 1.2f, Drag = 0.8f,
                Stretch = true, StretchVel = 0.05f, Max = 1500 }, root, "Sparks");
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = _steel, Mesh = Kit.SharedMesh("b4son_shard", () => Vol2FsB1.Shard(17)), Life0 = 0.5f, Life1 = 1f, Speed0 = 3f, Speed1 = 7f, Size0 = 0.06f, Size1 = 0.14f, Radius = 0.1f, Gravity = 1.5f, Spin = 720f, AlphaHoldTo = 0.9f, Max = 300 }, root, "Shards");
            _flash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Silver, 2.2f, 1f)), Life0 = 0.08f, Life1 = 0.14f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.6f, Size1 = 0.9f, Radius = 0.01f, Max = 30, SortFudge = -20 }, root, "Flash");
            foreach (var ps in new[] { _glints, _dust, _sparks, _shards, _flash }) ps.Play(false);
            Vol2Fs.MeshObject(root, "InkSwirls", Kit.InkSolid("fs_stroke2", Ink, new Color(1.3f, 1.4f, 1.6f, 1f), 0.08f, 0.5f, 0.3f, 1.5f), out _swirlMesh);
            var rib = M("fs_streak", new Color(0.85f, 0.92f, 1f, 1f), 2.4f, 0.85f); rib.NoiseAmt = 0.5f; rib.Scroll = new Vector2(-2f, 0f);
            Vol2Fs.MeshObject(root, "SilverRibbons", Kit.Mat(rib), out _ribbonMesh);
            var cut = M("fs_streak", Color.white, 3.2f, 1f); cut.NoiseAmt = 0.2f;
            Vol2Fs.MeshObject(root, "Cuts", Kit.Mat(cut), out _cutMesh);
            // r2: the mark's ring is a faint thin silver line (r1: an opaque white ellipse disc under the bear)
            var ring = M("fs_streak", new Color(0.75f, 0.85f, 1f, 1f), 1.4f, 1f); ring.Soft = 0.001f; ring.NoiseAmt = 0.5f; ring.Scroll = new Vector2(0.6f, 0f);
            // r2: the hop's motes - bigger, brighter, longer-lived (r1: the corkscrew to the neighbours did not read)
            // r3: cool silver-blue filings, small and stretched along their motion (r2: white snow flakes over the bears)
            _hopGlints = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.55f, 0.72f, 1f), 3.5f, 1f)), Life0 = 0.7f, Life1 = 1.3f, Speed0 = 0.1f, Speed1 = 0.5f, Size0 = 0.018f, Size1 = 0.035f, Radius = 0.05f, Drag = 0.6f,
                Noise = 0.5f, NoiseFreq = 1.2f, Stretch = true, StretchVel = 0.08f, StretchLen = 1.5f, AlphaHoldTo = 0.5f, Max = 1500 }, root, "HopGlints");
            _hopGlints.Play(false);
            Vol2Fs.MeshObject(root, "GlintRings", Kit.Mat(ring), out _ringMesh);
            _sweepLight = Vol2Fs.PointLight(root, "SweepLight", Silver, 5f);
        }

        protected override void OnCharge(float t, float k)
        {
            _charge.transform.position = Tip(); _chargeLight.intensity = 3f * k;
            _charge.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.2f, k);
        }

        protected override void OnLaunch(long nowUs) { _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f; }

        protected override void OnHit(long nowUs)
        {
            int seed = Seed + 59;
            // ink swirls on the ground: curling arcs over the fan, from a quarter out (the near band stays clear of the camera)
            for (int k = 0; k < 7; k++)
                _swirls.Add(new Swirl { V = (k / 6f) * 2f - 1f, R0 = 0.35f + 0.25f * Vol2Fs.Hash(seed, k), R1 = 0.8f + 0.25f * Vol2Fs.Hash(seed, k + 10), A0 = Vol2Fs.Hash(seed, k + 20) * 0.6f, Rnd = Vol2Fs.Hash(seed, k + 30) });
        }

        protected override void OnStruck(Struck s) { AddCuts(CentreNow(s), s.B, 4, s.At, 1f); Vol2FsB1.Emit(_sparks, CentreNow(s), 90); }

        void AddCuts(Vector3 c, Bounds b, int n, float at, float k)
        {
            for (int j = 0; j < n; j++)
            {
                Vector3 o = new Vector3(Random.Range(-1f, 1f) * b.extents.x, Random.Range(-0.6f, 0.8f) * b.extents.y, Random.Range(-1f, 1f) * b.extents.z) * 0.8f;
                Vector3 d = (Random.onUnitSphere + Vector3.up * 0.3f).normalized;
                _cuts.Add(new Cut { C = c + o, D = d, At = at + 0.05f * j, Len = Random.Range(0.4f, 0.9f) * k });
            }
        }

        protected override void OnHop(Hop h)
        {
            var d = new Drift { L = Vol2Fs.PointLight(Root.transform, "DriftLight", Silver, 4f), Phase = Vol2Fs.Hash(Seed, (int)h.TargetId) * 6.28f, A = h.FromP + Vector3.up * 0.5f };
            d.B = CentreNow(h);
            h.Data = d;
        }

        Vector3 DriftAt(Drift d, float u, float r, float ph)
        {
            Vector3 axis = Vol2FsB4.ArcPoint(d.A, d.B, 0.6f, u);
            Vector3 dir = (Vol2FsB4.ArcPoint(d.A, d.B, 0.6f, Mathf.Min(1f, u + 0.02f)) - axis).normalized;
            Vector3 n = Vector3.Cross(dir, Vector3.up).normalized; if (n.sqrMagnitude < 1e-4f) n = Vector3.right; Vector3 bn = Vector3.Cross(dir, n);
            float a = u * Mathf.PI * 2f * 2.5f + ph;
            return axis + (n * Mathf.Cos(a) + bn * Mathf.Sin(a)) * r * Mathf.Sin(Mathf.Clamp01(u * 1.2f) * Mathf.PI * 0.9f + 0.15f);
        }

        protected override void OnLand(Hop h)
        {
            if (h.Data is Drift d) d.L.intensity = 0f;
            float k = ShareScale(h.Share);
            AddCuts(CentreNow(h), h.B, Mathf.RoundToInt(7 * k), h.Start + h.Travel, k);
            Vol2FsB1.Emit(_sparks, CentreNow(h), Mathf.RoundToInt(110 * k)); Vol2FsB1.Emit(_hopGlints, CentreNow(h), 25);
        }

        protected override void OnMark(Mark m)
        {
            var ml = new MarkLook(); float k = ShareScale(m.Share);
            for (int j = 0; j < 7; j++)
            {
                var p = Vol2FsB3.Spiral(m.Centre, m.Radius * 1.2f * k, j, 7, Ground);
                var r = Vol2FsB1.Prop(Root.transform, "Needle_" + m.Id, _needle, _steel, false);
                Vector3 o = FlatN(p - m.Centre + new Vector3(0.01f, 0f, 0.013f * j));
                var q = Quaternion.LookRotation((Vector3.up * 1.6f + o * 0.6f + Random.insideUnitSphere * 0.3f).normalized, o);
                r.transform.SetPositionAndRotation(p + Vector3.down * 0.05f, q); r.transform.localScale = Vector3.zero;
                ml.Needles.Add(r.transform); ml.Rot.Add(q); ml.Pos.Add(p + Vector3.down * 0.05f);
            }
            m.Data = ml;
        }

        protected override void OnMarkEnd(Mark m)
        {
            if (!(m.Data is MarkLook ml) || m.Why == Vol2MaterialEnd.Aborted) return;
            // a ringing snap: flash, shards out SIDEWAYS (flat), sparks
            Vector3 c = m.Centre + Vector3.up * 0.35f;
            Vol2FsB1.Emit(_flash, c, 2);
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(m.Centre - Inst.Origin));
            for (int j = 0; j < 22; j++) { float s = j % 2 == 0 ? 1f : -1f; Vol2FsB1.EmitV(_shards, c + Random.insideUnitSphere * 0.2f, (side * s * Random.Range(3f, 7f) + Vector3.up * Random.Range(0.3f, 1.5f) + Random.insideUnitSphere), -1f, -1f); }
            Vol2FsB1.Emit(_sparks, c, Natural(m) ? 80 : 40);
            ml.Snapped = true;
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            // the glitter scythe: a front sweeping the fan from left to right in 0.35 s, shedding steel motes and dust
            float sweep = Vol2Fs.Smooth(0f, 0.35f, h);
            if (h < 0.42f)
            {
                float ang = Mathf.Lerp(-Half * 1.05f, Half * 1.05f, sweep);
                for (int j = 0; j < 14; j++)
                {
                    float u = Mathf.Lerp(0.25f, 1.05f, Random.value);
                    Vector3 p = Apex + Dir(ang + Random.Range(-0.12f, 0.06f)) * Depth * u + Vector3.up * Random.Range(0.1f, 1.6f) * u;
                    Vol2FsB1.EmitV(_glints, p, Dir(ang + 1.3f) * Random.Range(1f, 3f) + Vector3.up * 0.3f, -1f, -1f);
                }
                if (Time.frameCount % 2 == 0) Vol2FsB1.Emit(_dust, Apex + Dir(ang) * Depth * Random.Range(0.4f, 1f) + Vector3.up * 0.6f, 1);
                _sweepLight.transform.position = Apex + Dir(ang) * Depth * 0.7f + Vector3.up * 0.8f;
                _sweepLight.intensity = 5f;
            }
            else _sweepLight.intensity = Mathf.MoveTowards(_sweepLight.intensity, 0f, Time.deltaTime * 10f);
            // the silver ribbons of the scythe (two arcs following the front) and the ink swirls left on the ground
            _ribbon.Clear(); _swirl.Clear();
            float rl = 1f - Vol2Fs.Smooth(0.35f, 0.8f, h);
            if (rl > 0.01f)
                for (int r = 0; r < 3; r++)
                {
                    var pts = new Vector3[14]; var w = new float[14]; var al = new float[14];
                    float a1 = Mathf.Lerp(-Half * 1.05f, Half * 1.05f, sweep), a0 = Mathf.Max(-Half * 1.05f, a1 - 1.1f);
                    for (int k = 0; k < 14; k++) { float v = k / 13f; pts[k] = Apex + Dir(Mathf.Lerp(a0, a1, v)) * Depth * (0.55f + 0.2f * r) + Vector3.up * (0.5f + 0.45f * r + 0.2f * Mathf.Sin(v * 6f + r)); w[k] = 0.16f * Mathf.Sin(v * Mathf.PI) + 0.02f; al[k] = rl * v; }
                    _ribbon.Strip(pts, w, null, Color.white, al, 0.2f * r, cam, 0f, 1f);
                }
            float sl = Vol2Fs.Smooth(0f, 0.3f, h) * (1f - Vol2Fs.Smooth(1.2f, 2.2f, h));
            if (sl > 0.01f)
                foreach (var s in _swirls)
                {
                    var pts = new Vector3[9]; var w = new float[9]; var al = new float[9]; var side = new Vector3[9];
                    for (int k = 0; k < 9; k++)
                    {
                        float v = k / 8f, rr = Mathf.Lerp(s.R0, s.R1, v), a = s.V * Half + s.A0 * (v - 0.5f) + v * v * 0.7f * (s.V >= 0f ? 1f : -1f);
                        var p = Apex + Dir(a) * Depth * rr; p.y = Ground + 0.04f;
                        pts[k] = p; w[k] = (0.3f + 0.15f * s.Rnd) * Mathf.Sin(Mathf.Clamp01(v * 0.9f + 0.1f) * Mathf.PI); al[k] = sl * Vol2Fs.Smooth(v - 0.3f, v, sweep * 1.3f); side[k] = Dir(a);
                    }
                    _swirl.Strip(pts, w, side, Color.white, al, s.Rnd, cam, 0f, 1f);
                }
            // the drifts: a corkscrew of glints (and a thin silver ribbon) from the fan's edge onto each neighbour
            foreach (var hop in Hops)
            {
                if (!(hop.Data is Drift d) || t < hop.Start) continue;
                d.B = CentreNow(hop);
                float g = HopK(hop, t), k = ShareScale(hop.Share);
                float off = Vol2Fs.Smooth(hop.Start + hop.Travel, hop.Start + hop.Travel + 0.4f, t);
                if (g < 1f)
                {
                    int n = Mathf.Max(1, Mathf.RoundToInt(5 * k));
                    for (int j = 0; j < n; j++)
                    {
                        float u = Mathf.Clamp01(g - Random.value * 0.3f);
                        Vector3 p = DriftAt(d, u, 0.35f * k, d.Phase + j * 2.1f), along = DriftAt(d, Mathf.Min(1f, u + 0.03f), 0.35f * k, d.Phase + j * 2.1f) - p;
                        Vol2FsB1.EmitV(_hopGlints, p, along.normalized * 2.5f + Random.insideUnitSphere * 0.2f, -1f, -1f);      // flying filings along the corkscrew
                    }
                    if (Time.frameCount % 2 == 0) Vol2FsB1.Emit(_dust, DriftAt(d, g, 0.2f, d.Phase), 1);
                    d.L.transform.position = DriftAt(d, g, 0f, 0f); d.L.intensity = 4f * k;
                }
                if (off < 1f)
                {
                    var pts = new Vector3[16]; var w = new float[16]; var al = new float[16];
                    for (int j = 0; j < 16; j++) { float v = j / 15f, u = Mathf.Lerp(Mathf.Max(0f, g - 0.5f), g, v); pts[j] = DriftAt(d, u, 0.3f * k, d.Phase); w[j] = 0.12f * k * Mathf.Sin(v * Mathf.PI) + 0.015f; al[j] = (1f - off) * v; }
                    _ribbon.Strip(pts, w, null, Color.white, al, 0.5f + hop.Order * 0.1f, cam, 0f, 1f);
                }
            }
            _ribbon.Write(_ribbonMesh); _swirl.Write(_swirlMesh);
            // the cut flashes (thin white slashes, 0.25 s each)
            _cut.Clear();
            foreach (var c in _cuts)
            {
                float a = t - c.At; if (a < 0f || a > 0.3f) continue;
                float grow = Vol2Fs.Smooth(0f, 0.05f, a), life = 1f - Vol2Fs.Smooth(0.08f, 0.3f, a);
                var pts = new[] { c.C - c.D * c.Len * 0.5f * grow, c.C, c.C + c.D * c.Len * 0.5f * grow }; var w = new[] { 0.01f, 0.05f, 0.01f }; var al = new[] { life, life, life };
                _cut.Strip(pts, w, null, Color.white, al, 0.5f, cam, 0f, 1f);
            }
            _cut.Write(_cutMesh);
            // the marks: a turning ring of glints and steel needles at the feet; warn = flicker; the snap shatters them
            _ring.Clear();
            foreach (var m in MarkList)
            {
                if (!(m.Data is MarkLook ml)) continue;
                float life = MarkLife(m, t, 0.2f), warn = MarkWarn(m, t), age = MarkAge(m, t), k = ShareScale(m.Share);
                float flick = warn > 0f ? (Mathf.PerlinNoise(t * 30f, m.Id) > 0.45f ? 1f : 0.25f) : 1f;
                if (life > 0.01f && !ml.Snapped)
                {
                    var pts = new List<Vector3>(); var w = new List<float>(); var al = new List<float>(); var side = new List<Vector3>();
                    for (int j = 0; j <= 24; j++) { float a = j / 24f * Mathf.PI * 2f; Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); pts.Add(m.Centre + o * 0.85f * k + Vector3.up * 0.04f); w.Add(0.035f); al.Add(0.6f * life * flick); side.Add(o); }
                    _ring.Strip(pts, w, side, Color.white, al, 0.3f + m.Id % 5 * 0.1f, cam, t * 0.3f, t * 0.3f + 3f);
                    if (Time.frameCount % 3 == (int)(m.Id % 3)) Vol2FsB1.EmitV(_glints, m.Centre + new Vector3(Random.Range(-0.8f, 0.8f), Random.Range(0.05f, 0.6f), Random.Range(-0.8f, 0.8f)) * k, Vector3.up * 0.3f, -1f, -1f);
                }
                for (int j = 0; j < ml.Needles.Count; j++)
                {
                    var tr = ml.Needles[j];
                    if (ml.Snapped || age < 0f) { tr.localScale = Vector3.zero; continue; }
                    float g = Vol2Fs.EaseOutBack(Mathf.Clamp01((age - 0.04f * j) / 0.18f));
                    tr.localScale = new Vector3(2.2f, 2.2f, 0.75f) * k * g;      // a needle ~0.75 m long, ~0.15 m wide
                    tr.position = ml.Pos[j] + Vector3.down * 0.25f * k * (1f - g);
                    if (warn > 0f) tr.rotation = ml.Rot[j] * Quaternion.Euler(Mathf.Sin(t * 70f + j) * 3f * warn, 0f, 0f);
                }
            }
            _ring.Write(_ringMesh);
        }
    }
}
