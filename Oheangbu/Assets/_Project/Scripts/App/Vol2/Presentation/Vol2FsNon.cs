using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>논 (fire, ㅗ fan, 번진다 극치 · 역화) - free showcase batch 4, concept non_a (the brush flings a fan of fire-rimmed ink; from the fan's edge thick streams of
    /// flame run out to the bears outside it, and great ink-fire curls whirl BACK toward the caster) with non_c's second, darker ember layer for the return. The
    /// peak's rule is drawn as TWO PASSES: the forward streams (Spread 0.5 each) run out from the fan's edge; a heartbeat later the executor's back-spread
    /// (ReturnSpread 0.25 each, the same bodies in reverse) is a thinner crimson-ember curl that loops round each body and rushes back over the fan toward the
    /// caster. Beats, all executor facts: charge -> the launch = the fan of burning ink strokes and a low ember bed, the body inside burns (Instant) -> the
    /// forward hops (hop.FromP = the fan's nearest point) -> their marks (3 s): a burning scorch -> the back hops in the executor's reverse order -> their own
    /// weaker marks (a ring of ember curls on the same scorch) -> warning = the flames gutter -> natural expiry (CSV: 끝부터 타 재로 날림) = burns down into rising ash.</summary>
    public sealed class Vol2FsNon : Vol2FsSpread
    {
        static readonly Color Orange = new Color(1f, 0.45f, 0.1f), Crimson = new Color(1f, 0.18f, 0.06f), Hot = new Color(1f, 0.8f, 0.4f), Ink = new Color(0.035f, 0.025f, 0.02f);
        ParticleSystem _charge, _fire, _embers, _smoke, _ash, _redEmbers; Light _chargeLight; Light[] _fanLights;
        Mesh _strokeMesh, _bedMesh, _streamMesh, _backMesh, _inkMesh, _scorchMesh, _curlMesh;
        readonly Vol2FsStrips _stroke = new Vol2FsStrips(), _bed = new Vol2FsStrips(), _stream = new Vol2FsStrips(), _back = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _scorch = new Vol2FsStrips(), _curl = new Vol2FsStrips();
        struct FanStroke { public float V, Len, W, Rnd, Curl; }
        readonly List<FanStroke> _fanStrokes = new List<FanStroke>();

        sealed class Flow { public Vector3[] Ctrl; public Light L; public float Rnd; }

        protected override float HopSpeed => 8f;
        protected override float HopMin => 0.45f;
        protected override float HopMax => 0.6f;
        protected override float HopStagger => 0.12f;
        protected override float MarkExpireSeconds => 1.1f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.8f, 0.5f)), Rate = 70f, Loop = true, Life0 = 0.18f, Life1 = 0.3f, Speed0 = -1.6f, Speed1 = -2.6f, Size0 = 0.12f, Size1 = 0.28f, Radius = 0.6f, World = false, Tiles = 2, Max = 90 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", Orange, 4f);
            _charge.transform.position = Tip(); _charge.Play(false);
            _fire = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.6f, 0.35f)), Life0 = 0.3f, Life1 = 0.6f, Speed0 = 0.2f, Speed1 = 0.9f, Size0 = 0.28f, Size1 = 0.6f, Radius = 0.12f, Tiles = 2,
                VelOverLife = new Vector3(0f, 2f, 0f), Noise = 0.9f, NoiseFreq = 1.3f, SizeStart = 0.5f, SizePeak = 1.2f, SizeEnd = 0.35f, Max = 5000 }, root, "Fire");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.42f, 0.08f), 2.2f, 1f)), Life0 = 0.3f, Life1 = 0.7f, Speed0 = 0.6f, Speed1 = 2.5f, Size0 = 0.03f, Size1 = 0.06f, Radius = 0.2f, Gravity = -0.25f,      // r2: orange, short, few (r1: hundreds of white streaks)
                Noise = 1.2f, NoiseFreq = 1.6f, Max = 600 }, root, "Embers");
            _redEmbers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Crimson, 2f, 1f)), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 0.3f, Speed1 = 1.6f, Size0 = 0.03f, Size1 = 0.07f, Radius = 0.15f, Gravity = -0.15f,
                Noise = 1.2f, NoiseFreq = 1.4f, Max = 400 }, root, "RedEmbers");
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Smoke(0.45f)), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 0.3f, Speed1 = 0.9f, Size0 = 0.8f, Size1 = 1.4f, Radius = 0.3f, Drag = 0.8f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.3f, SizePeakAt = 0.5f, SizeEnd = 1.8f, VelOverLife = new Vector3(0f, 1.1f, 0f), Max = 200, SortFudge = 12 }, root, "Smoke");
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Ash()), Life0 = 1.2f, Life1 = 2.4f, Speed0 = 0.2f, Speed1 = 1f, Size0 = 0.04f, Size1 = 0.11f, Radius = 0.35f, VelOverLife = new Vector3(0f, 1.6f, 0f),
                Noise = 1.2f, NoiseFreq = 1f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.6f, Max = 1500 }, root, "Ash");
            foreach (var ps in new[] { _fire, _embers, _redEmbers, _smoke, _ash }) ps.Play(false);
            Vol2Fs.MeshObject(root, "FanStrokes", Kit.InkSolid("fs_stroke2", Ink, new Color(2.6f, 0.85f, 0.12f, 1f), 0.16f, 0.5f, 0.3f, 1.4f), out _strokeMesh);
            var bed = M("fs_scorch", Color.white, 2.6f, 0.8f); bed.Ramp = "fs_ramp_fire"; bed.RampScale = 1.1f; bed.NoiseAmt = 0.55f; bed.NoiseScale = 2f; bed.Soft = 0.001f; bed.Scroll = new Vector2(0.15f, 0.1f);
            Vol2Fs.MeshObject(root, "EmberBed", Kit.Mat(bed), out _bedMesh);
            var stream = M("fs_flame", Color.white, 2.6f, 0.75f); stream.Ramp = "fs_ramp_fire"; stream.RampScale = 1.3f; stream.NoiseAmt = 0.6f; stream.Erode = 0.25f; stream.Scroll = new Vector2(-1.4f, 0f);
            Vol2Fs.MeshObject(root, "Streams", Kit.Mat(stream), out _streamMesh);
            var back = M("fs_flame", new Color(1f, 0.35f, 0.22f, 1f), 2.2f, 0.7f); back.Ramp = "fs_ramp_fire"; back.RampScale = 0.75f; back.NoiseAmt = 0.65f; back.Erode = 0.3f; back.Scroll = new Vector2(1.6f, 0f);
            Vol2Fs.MeshObject(root, "Backdraft", Kit.Mat(back), out _backMesh);
            Vol2Fs.MeshObject(root, "InkRibbons", Kit.InkSolid("fs_stroke2", Ink, new Color(2.4f, 0.6f, 0.1f, 1f), 0.14f, 0.5f, 0.32f, 1.6f), out _inkMesh);
            Vol2Fs.MeshObject(root, "BackCurls", Kit.InkSolid("fs_stroke", Ink, new Color(2.2f, 0.25f, 0.08f, 1f), 0.2f, 0.5f, 0.3f, 1.4f), out _curlMesh);
            var sc = M("fs_scorch", new Color(0.05f, 0.03f, 0.02f, 1f), 1f, 0f); sc.Soft = 0.001f; sc.NoiseAmt = 0.4f; sc.Erode = 0.3f; sc.Opacity = 0.9f; sc.Edge = new Color(2.4f, 0.7f, 0.1f, 1f); sc.EdgeWidth = 0.16f;
            Vol2Fs.MeshObject(root, "Scorch", Kit.Mat(sc), out _scorchMesh);
            _fanLights = new Light[2];
            for (int k = 0; k < 2; k++) _fanLights[k] = Vol2Fs.PointLight(root, "FanLight" + k, Orange, 6f);
        }

        protected override void OnCharge(float t, float k) { _charge.transform.position = Tip(); _chargeLight.intensity = 4f * k; }
        protected override void OnLaunch(long nowUs) { _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f; }

        protected override void OnHit(long nowUs)
        {
            int seed = Seed + 53;
            // the fan of burning ink strokes flung out over the ground, each curling at its end (non_a)
            for (int k = 0; k < 11; k++)
                _fanStrokes.Add(new FanStroke { V = (k / 10f) * 2f - 1f + (Vol2Fs.Hash(seed, k) - 0.5f) * 0.1f, Len = 0.75f + 0.25f * Vol2Fs.Hash(seed, k + 10), W = 0.35f + 0.3f * Vol2Fs.Hash(seed, k + 20), Rnd = Vol2Fs.Hash(seed, k + 30), Curl = (k % 2 == 0 ? 1f : -1f) * (0.4f + 0.4f * Vol2Fs.Hash(seed, k + 40)) });
            for (int k = 0; k < 2; k++) _fanLights[k].transform.position = Vol2FsB3.InFan(Apex, F, Depth, Half, 0.7f, k == 0 ? -0.55f : 0.55f, Ground) + Vector3.up * 0.8f;
            for (int k = 0; k < 14; k++) Vol2FsB1.Emit(_fire, Vol2FsB3.InFan(Apex, F, Depth, Half, Random.Range(0.4f, 1f), Random.Range(-0.9f, 0.9f), Ground) + Vector3.up * 0.2f, 4);
        }

        protected override void OnStruck(Struck s) { Vol2FsB1.Emit(_fire, CentreNow(s), 40); Vol2FsB1.Emit(_embers, CentreNow(s), 25); }

        protected override void OnHop(Hop h)
        {
            var fl = new Flow { L = Vol2Fs.PointLight(Root.transform, h.Back ? "BackLight" : "StreamLight", h.Back ? Crimson : Orange, 5f), Rnd = Vol2Fs.Hash(Seed, (int)h.TargetId + (h.Back ? 50 : 0)) };
            Vector3 c = CentreNow(h);
            if (!h.Back)
            {
                // forward: a low thick stream from the fan's edge, bulging up a little, into the body
                Vector3 a = h.FromP + Vector3.up * 0.35f, b = c;
                Vector3 side = Vector3.Cross(Vector3.up, FlatN(b - a));
                fl.Ctrl = new[] { a, Vector3.Lerp(a, b, 0.35f) + Vector3.up * 0.7f + side * 0.3f, Vector3.Lerp(a, b, 0.7f) + Vector3.up * 0.55f - side * 0.25f, b };
            }
            else
            {
                // back: from beyond the body it loops round it and rushes back over the fan toward the caster (the backdraft)
                Vector3 o = FlatN(FeetOf(h) - Apex), sd = Vector3.Cross(Vector3.up, o) * (Vector3.Dot(Vector3.Cross(Vector3.up, F), o) > 0f ? 1f : -1f);
                // r2: a LOW dark backdraft sheet hugging the ground, sweeping round the body and back toward the caster (r1: too like the outward stream)
                Vector3 p0 = h.FromP + Vector3.up * 0.2f, p1 = c + o * 0.5f + sd * 0.8f + Vector3.up * 0.25f, p2 = c - o * 0.3f + sd * 0.6f + Vector3.up * 0.25f;
                Vector3 p3 = Vol2FsB3.InFan(Apex, F, Depth, Half, 0.62f, Mathf.Sign(Vector3.Dot(o, Vector3.Cross(Vector3.up, F))) * 0.35f, Ground) + Vector3.up * 0.2f;
                Vector3 p4 = Vol2FsB3.InFan(Apex, F, Depth, Half, 0.22f, 0f, Ground) + Vector3.up * 0.12f;
                fl.Ctrl = new[] { p0, p1, p2, p3, p4 };
            }
            h.Data = fl;
        }

        protected override void OnLand(Hop h)
        {
            if (h.Data is Flow fl) fl.L.intensity = 0f;
            Vector3 c = CentreNow(h);
            if (!h.Back) { Vol2FsB1.Emit(_fire, c, 45); Vol2FsB1.Emit(_embers, c, 30); }
            else { Vol2FsB1.Emit(_redEmbers, Vol2FsTubes.CatmullRom(((Flow)h.Data).Ctrl, 1f), 25); Vol2FsB1.Emit(_fire, c, 18); }
        }

        protected override void OnMarkEnd(Mark m)
        {
            if (m.Why == Vol2MaterialEnd.Aborted) return;
            Vector3 c = m.Centre + Vector3.up * 0.5f;
            if (!Natural(m)) { Vol2FsB1.Emit(_embers, c, 20); Vol2FsB1.Emit(_smoke, c, 3); }
        }

        /// <summary>A ribbon along a Catmull-Rom of <paramref name="ctrl"/> from u0 to u1.</summary>
        static void Along(Vol2FsStrips s, Vector3[] ctrl, float u0, float u1, float width, float life, float rnd, Vector3 cam, float wobble, float phase, int n = 16)
        {
            if (life <= 0.01f || u1 - u0 < 0.01f) return;
            var pts = new Vector3[n]; var w = new float[n]; var al = new float[n];
            for (int k = 0; k < n; k++)
            {
                float v = k / (float)(n - 1), u = Mathf.Lerp(u0, u1, v);
                pts[k] = Vol2FsTubes.CatmullRom(ctrl, u) + Vector3.up * Mathf.Sin(u * 14f + phase) * wobble;
                w[k] = width * (0.3f + 0.7f * Mathf.Sin(Mathf.Clamp01(v) * Mathf.PI)) * (0.6f + 0.4f * v);
                al[k] = life * (0.5f + 0.5f * v);
            }
            s.Strip(pts, w, null, Color.white, al, rnd, cam, u0 * 2f, u1 * 2f);
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            // the fan: burning ink strokes flung out (0.25 s), an ember bed that flares and cools to a scorch
            float reach = Vol2Fs.Smooth(0f, 0.25f, h);
            bool marks = false; float lastEnd = 0f; foreach (var mk in MarkList) { if (mk.EndAt < 0f) marks = true; else lastEnd = Mathf.Max(lastEnd, mk.EndAt); }
            float strokeLife = 1f - Vol2Fs.Smooth(0.9f, 1.8f, h);
            _stroke.Clear();
            var pts = new Vector3[7]; var w = new float[7]; var al = new float[7]; var side = new Vector3[7];
            foreach (var s in _fanStrokes)
            {
                if (strokeLife <= 0.01f) break;
                for (int k = 0; k < 7; k++)
                {
                    float v = k / 6f, u = Mathf.Lerp(0.28f, 0.28f + (s.Len - 0.28f) * reach, v);
                    Vector3 d = Dir(s.V * Half * 1.05f + s.Curl * v * v * 0.8f);
                    var p = Apex + d * Depth * u; p.y = Ground + 0.04f + 0.25f * v * v * reach;
                    pts[k] = p; w[k] = s.W * (0.4f + 0.6f * Mathf.Sin(v * Mathf.PI)) * (1f - 0.3f * v); al[k] = strokeLife * (1f - 0.25f * v); side[k] = Vector3.Cross(Vector3.up, d);
                }
                _stroke.Strip(pts, w, side, Color.white, al, s.Rnd, cam, 0f, 1f);
            }
            _stroke.Write(_strokeMesh);
            _bed.Clear(); Vol2FsB4F.FanCarpet(_bed, Apex, F, Depth, Half, Ground, Mathf.Lerp(0.25f, 1f, reach), (1f - Vol2Fs.Smooth(0.5f, 1.4f, h)), 0.31f, cam, 0.035f); _bed.Write(_bedMesh);
            for (int k = 0; k < _fanLights.Length; k++) _fanLights[k].intensity = (h < 0.2f ? 8f * h / 0.2f : 6f * Mathf.Exp(-(h - 0.2f) * 1.5f));
            if (h < 0.7f && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_fire, Vol2FsB3.InFan(Apex, F, Depth, Half, Random.Range(0.35f, reach), Random.Range(-0.95f, 0.95f), Ground) + Vector3.up * 0.1f, Vector3.up * 1.2f, 0.35f + 0.25f * Random.value, -1f);
            // the body in the fan burns for the instant's 2 s (mirror)
            foreach (var s in Direct) if (t - s.At < 2f && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_fire, CentreNow(s) + Random.insideUnitSphere * 0.35f, Vector3.up * 1.2f, 0.45f + 0.3f * Random.value, -1f);
            // the hops: forward streams (orange, thick) and the backdraft curls (crimson, thinner - its share is half)
            _stream.Clear(); _back.Clear(); _ink.Clear(); _curl.Clear();
            foreach (var hop in Hops)
            {
                if (!(hop.Data is Flow fl) || t < hop.Start) continue;
                float k = HopK(hop, t), sc = ShareScale(hop.Share);
                float hold = hop.Back ? 0.35f : 0.55f;                                                  // the stream hangs a moment after it lands, then burns off
                float off = Vol2Fs.Smooth(hop.Start + hop.Travel, hop.Start + hop.Travel + hold, t);
                float u0 = hop.Back ? Mathf.Max(0f, k - 0.45f) + off * 0.55f : off, u1 = k;
                if (!hop.Back)
                {
                    Along(_stream, fl.Ctrl, u0, u1, 0.75f * sc, 1f - off, fl.Rnd, cam, 0.06f, t * 9f);
                    Along(_ink, fl.Ctrl, Mathf.Max(u0, u1 - 0.6f), u1, 0.28f * sc, 1f - off, fl.Rnd + 0.3f, cam, 0.12f, t * 7f + 2f);
                }
                else
                {
                    Along(_curl, fl.Ctrl, u0, Mathf.Min(1f, u1 + 0.02f), 1.1f * sc, 1f - off, fl.Rnd + 0.6f, cam, 0.06f, t * 6f);      // the dark ink-and-crimson sheet
                    Along(_back, fl.Ctrl, Mathf.Max(u0, u1 - 0.4f), u1, 0.3f * sc, 1f - off, fl.Rnd, cam, 0.08f, t * 11f);      // a thin crimson flame lip on its front
                }
                if (k < 1f)
                {
                    Vector3 head = Vol2FsTubes.CatmullRom(fl.Ctrl, k);
                    if (hop.Back) { if (Time.frameCount % 2 == 0) Vol2FsB1.Emit(_redEmbers, head, 1); } else { if (Time.frameCount % 2 == 0) Vol2FsB1.Emit(_embers, head, 1); Vol2FsB1.EmitV(_fire, head, Vector3.up, 0.5f * sc, 0.3f); }
                    fl.L.transform.position = head; fl.L.intensity = (hop.Back ? 4f : 6f) * sc;
                }
            }
            _stream.Write(_streamMesh); _back.Write(_backMesh); _ink.Write(_inkMesh);
            // the marks: forward = a burning scorch at the feet; back = a ring of ember curls on it (weaker, crimson); natural expiry burns down into ash
            _scorch.Clear();
            foreach (var m in MarkList)
            {
                float age = MarkAge(m, t); if (age < 0f) continue;
                float life = MarkLife(m, t, 0.2f), warn = MarkWarn(m, t), ex = MarkExit(m, t), sc = ShareScale(m.Share);
                Vector3 c = m.Hop != null ? CentreNow(m.Hop) : m.Centre + Vector3.up * 0.6f;
                if (!m.Back)
                {
                    if (life > 0.01f) _scorch.Quad(m.Centre + Vector3.up * 0.025f, Right, Fwd, 2.2f * sc, new Color(1f, 1f, 1f, life), 0.4f + m.Id % 5 * 0.1f);
                    if (m.EndAt < 0f && !(warn > 0.01f && Random.value < warn * 0.7f))
                    {
                        if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_fire, c + Random.insideUnitSphere * 0.4f, Vector3.up * 1.3f, (0.45f + 0.3f * Random.value) * sc, -1f);
                        if (Time.frameCount % 6 == 0) Vol2FsB1.Emit(_embers, m.Centre + Vector3.up * 0.3f, 1);
                    }
                }
                else if (life > 0.01f)
                {
                    // the back mark: three ember curls turning round the feet (the second, weaker layer)
                    for (int j = 0; j < 3; j++)
                    {
                        float a0 = j * 2.1f + t * 1.8f + m.Id;
                        for (int k = 0; k < 7; k++)
                        {
                            float v = k / 6f, a = a0 + v * 2.2f, r = (0.55f + 0.5f * v) * sc;
                            pts[k] = m.Centre + new Vector3(Mathf.Cos(a) * r, 0.05f + 0.25f * v * (1f - v) * 4f * 0.3f, Mathf.Sin(a) * r);
                            w[k] = 0.22f * sc * Mathf.Sin(v * Mathf.PI); al[k] = life * (1f - 0.5f * warn); side[k] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        }
                        _curl.Strip(pts, w, side, Color.white, al, 0.13f * j + 0.05f * (m.Id % 9), cam, 0f, 1f);
                    }
                    if (m.EndAt < 0f && Time.frameCount % 3 == 0 && !(warn > 0.01f && Random.value < warn * 0.7f)) { Vol2FsB1.Emit(_redEmbers, m.Centre + Vector3.up * 0.2f, 1); Vol2FsB1.EmitV(_fire, c + Random.insideUnitSphere * 0.3f, Vector3.up, 0.3f * sc, -1f); }
                }
                if (m.EndAt >= 0f && Natural(m) && ex < 1f && Time.frameCount % 2 == 0) { Vol2FsB1.Emit(_ash, c + Random.insideUnitSphere * 0.4f, m.Back ? 1 : 3); Vol2FsB1.Emit(_embers, m.Centre + Vector3.up * 0.4f, 1); }
            }
            _scorch.Write(_scorchMesh); _curl.Write(_curlMesh);
        }
    }
}
