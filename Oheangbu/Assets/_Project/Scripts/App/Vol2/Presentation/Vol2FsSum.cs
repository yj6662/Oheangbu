using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>숨 (metal, ㅜ ring, 심는다) - free showcase batch 5, concept sum_b (a razor-thin steel thread hangs just above the ground round the 2 m band, glinting
    /// beads strung on it; the bear that crosses trips it: the thread snaps taut and whips up into great razor arcs of white-gold light and ink that race round
    /// the circle, cutting both bears). Unlike 수's spinning saw, 숙's planted swords and 순's spokes, nothing stands while it waits: only a hair of steel and its
    /// glints. Executor facts only: the launch = the thread is drawn round the band in one glinting sweep -> the wait (a glint runs round the thread on every
    /// heartbeat, the beads breathe, the ink arc inside the band burns down; the last 0.4 s the thread trembles and sparks) -> Consumed = from the crossing
    /// body's angle the thread whips up into razor arcs racing round (thin-to-wide light + an ink edge, sparks along them, RaceDelay) -> every struck body
    /// takes a bright X cut, a spark fountain and cut ink threads -> the arcs ring and snap, shards flying sideways (CSV 끝: 쨍하게 끊기며 파편이 옆으로 튐);
    /// Expired = the slack thread snaps where it lies, shards sideways.</summary>
    public sealed class Vol2FsSum : Vol2FsSeedRing
    {
        static readonly Color Gold = new Color(1f, 0.88f, 0.6f);
        const float ArcHold = 0.55f, SnapAt = 0.75f;
        ParticleSystem _sparks, _shards, _glints, _threads;
        Material _steel;
        Mesh _threadMesh, _arcMesh, _arcInkMesh, _xMesh;
        readonly Vol2FsStrips _thread = new Vol2FsStrips(), _arc = new Vol2FsStrips(), _arcInk = new Vol2FsStrips(), _x = new Vol2FsStrips();
        readonly List<(float Ang, float H, float Side)> _arcs = new List<(float, float, float)>();
        readonly List<(Struck S, float At)> _cuts = new List<(Struck, float)>();
        bool _snapped, _slackSnapped;
        Light[] _lights;
        protected override int SeedCount => 20;
        protected override float RaceSeconds => 0.28f;
        protected override float FireSeconds => 1.2f;
        protected override float ExpireSeconds => 0.8f;
        const float ThreadY = 0.16f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _steel = Vol2FsB1.Lit(Kit, "fs_steel", Color.white, 1f, 0.9f, new Color(0.05f, 0.06f, 0.08f));
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 5f, 1f)), Life0 = 0.25f, Life1 = 0.7f, Speed0 = 4f, Speed1 = 12f, Size0 = 0.02f, Size1 = 0.05f, Radius = 0.1f, Gravity = 1.4f, Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.5f, Max = 1200 }, root, "Sparks");
            var shard = Kit.SharedMesh("shard0", () => Vol2FsB1.Shard(5));
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = _steel, Mesh = shard, Life0 = 0.6f, Life1 = 1.1f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.05f, Size1 = 0.14f, Radius = 0.1f, Gravity = 2f, Drag = 0.4f, Spin = 900f, Max = 300 }, root, "Shards");
            _glints = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Color.white, 4f, 1f)), Life0 = 0.08f, Life1 = 0.14f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.2f, Size1 = 0.35f, Max = 300 }, root, "Glints");
            var thr = M("fs_stroke2", InkC, 1f, 0f); thr.NoiseAmt = 0.3f; thr.Soft = 0.05f;
            _threads = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(thr), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 3f, Speed1 = 7f, Size0 = 0.05f, Size1 = 0.1f, Radius = 0.1f, Drag = 2f, Gravity = 0.6f, Stretch = true, StretchVel = 0.08f, StretchLen = 2f, Max = 200 }, root, "InkThreads");
            foreach (var ps in new[] { _sparks, _shards, _glints, _threads }) ps.Play(false);
            var line = M("fs_streak", Gold, 2.4f, 1f); line.NoiseAmt = 0.1f; line.Soft = 0.02f; line.Scroll = Vector2.zero;
            Vol2Fs.MeshObject(root, "Thread", Kit.Mat(line), out _threadMesh);
            var arc = M("fs_streak", Gold, 3.6f, 1f); arc.NoiseAmt = 0.15f; arc.Soft = 0.02f; arc.Erode = 0.12f;
            Vol2Fs.MeshObject(root, "RazorArcs", Kit.Mat(arc), out _arcMesh);
            Vol2Fs.MeshObject(root, "RazorInk", Kit.InkSolid("fs_stroke2", InkC, new Color(1.6f, 1.4f, 0.9f, 1f), 0.1f, 0.4f, 0.3f, 1.3f), out _arcInkMesh);
            Vol2Fs.MeshObject(root, "XCuts", Kit.Mat(arc), out _xMesh);
            _lights = new Light[3];
            for (int k = 0; k < 3; k++) _lights[k] = Vol2Fs.PointLight(root, "Glint" + k, Gold, 3.5f);
        }

        protected override void Planted(Pod p) { for (int k = 0; k < SeedAng.Count; k++) if (Front(SeedAng[k]) > 0.3f) Vol2FsB1.EmitV(_glints, OnBand(SeedAng[k], R) + Vector3.up * ThreadY, Vector3.zero, 0.3f, 0.12f); }

        protected override void Fired(Pod p)
        {
            int seed = Seed + 61, n = 12;
            for (int k = 0; k < n; k++)
            {
                float a = Vol2FsB2.Angle(k, n, seed, 0.3f);
                float h = Vol2FsPolish.RingHeight(a) * (1.3f + 1.1f * Vol2Fs.Hash(seed, k)) * (Front(a) < 0.25f ? 0.4f : 1f);
                float side = Mathf.DeltaAngle(TrigAng * Mathf.Rad2Deg, a * Mathf.Rad2Deg) >= 0f ? 1f : -1f;      // each arc whips onward, away from the trip
                _arcs.Add((a, h, side));
            }
        }

        protected override void Hit(Pod p, Struck s) { if (!s.World) _cuts.Add((s, SinceFire(p, Now(NowUs)) + RaceDelay(AngleOf(FeetOf(s))) + 0.08f)); }

        /// <summary>A razor arc: from the band point a, rising in the vertical plane along the ring (onward = side) to height h and down again 1.6 m on.</summary>
        Vector3 ArcPoint(float a, float h, float side, float u)
        {
            Vector3 b = OnBand(a, R), tg = Tangent(a) * side, o = Dir(a);
            return b + tg * 1.6f * u + Vector3.up * (ThreadY + h * Mathf.Sin(u * Mathf.PI)) + o * 0.35f * Mathf.Sin(u * Mathf.PI);
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos; var p = The;
            _thread.Clear(); _arc.Clear(); _arcInk.Clear(); _x.Clear();
            if (p == null || !p.Started) { Flush(); return; }
            float life = PodLife(p, t, 0.35f), beat = Beat(p), warn = PodWarn(p, t), ex = PodExit(p, t), s = SinceFire(p, t);
            bool worked = Worked(p), withered = Withered(p);
            // the thread: drawn round in one sweep at the launch; while it waits a glint runs round it on each heartbeat
            float sweep = Mathf.Clamp01(PodAge(p, t) / 0.35f);
            float threadLife = !p.Ended ? life : withered ? (1f - Vol2Fs.Smooth(0f, 0.25f, ex)) : 0f;
            if (threadLife > 0.01f)
            {
                int n = 72; var pts = new List<Vector3>(); var wd = new List<float>(); var al = new List<float>();
                float front = Mathf.Atan2(F.x, F.z);
                for (int i = 0; i <= n; i++)
                {
                    float a = (i / (float)n - 0.5f) * Mathf.PI * 2f * sweep;
                    Vector3 pt = OnBand(a, R) + Vector3.up * (ThreadY + 0.012f * Mathf.Sin(a * 9f + t * 3f) + (warn > 0f ? 0.03f * warn * Mathf.Sin(t * 60f + a * 13f) : 0f));
                    pts.Add(pt); wd.Add(0.018f * (1f + 0.5f * warn)); al.Add(threadLife * Mathf.Lerp(0.35f, 1f, Front(a)) * (0.7f + 0.3f * beat));
                }
                _thread.Strip(pts, wd, null, Color.white, al, 0.27f, cam, 0f, 6f);
                if (!p.Ended)
                {
                    float run = (p.Phase - Mathf.Floor(p.Phase)) * Mathf.PI * 2f - Mathf.PI;
                    if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_glints, OnBand(run, R) + Vector3.up * ThreadY, Vector3.zero, 0.35f * Mathf.Lerp(0.5f, 1f, Front(run)) * (1f - 0.6f * Held01), 0.1f);
                    if (Time.frameCount % 3 == 0) foreach (float a in SeedAng) if (Front(a) > 0.2f) Vol2FsB1.EmitV(_glints, OnBand(a, R) + Vector3.up * ThreadY, Vector3.zero, (0.08f + 0.12f * beat + 0.1f * warn) * SeedScale(a) * life, 0.1f);
                    if (warn > 0f && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_sparks, OnBand(Random.Range(-2.6f, 2.6f), R) + Vector3.up * ThreadY, Random.onUnitSphere * 3f, -1f, -1f);
                    for (int k = 0; k < 3; k++) { _lights[k].transform.position = OnBand(-1.2f + 1.2f * k, R) + Vector3.up * 0.4f; _lights[k].intensity = life * (0.3f + 0.9f * beat + warn) * (1f - 0.6f * Held01); }
                }
            }
            if (withered && !_slackSnapped && ex > 0.05f)
            {
                _slackSnapped = true;
                for (int k = 0; k < 8; k++)
                {
                    float a = Vol2FsB2.Angle(k, 8, Seed + 3, 0.4f); if (Front(a) < 0.3f) continue;
                    Vector3 b = OnBand(a, R) + Vector3.up * ThreadY, tg = Tangent(a);
                    for (int j = 0; j < 4; j++) Vol2FsB1.EmitV(_shards, b, tg * (j % 2 == 0 ? 1f : -1f) * Random.Range(2f, 5f) + Vector3.up * Random.Range(0.5f, 2f), -1f, -1f);
                    for (int j = 0; j < 14; j++) Vol2FsB1.EmitV(_sparks, b, (tg * (j % 2 == 0 ? 1f : -1f) + Random.insideUnitSphere * 0.5f).normalized * Random.Range(3f, 8f), -1f, -1f);
                    Vol2FsB1.EmitV(_glints, b, Vector3.zero, 0.9f, 0.12f);
                }
            }
            if (p.Ended && !worked) for (int k = 0; k < 3; k++) _lights[k].intensity = 0f;
            if (worked)
            {
                foreach (var (a, hh, side) in _arcs)
                {
                    float since = s - RaceDelay(a);
                    if (since <= 0f || since > SnapAt + 0.05f) continue;
                    float grow = Vol2Fs.Smooth(0f, 0.16f, since), fade = 1f - Vol2Fs.Smooth(ArcHold, SnapAt, since);
                    int n = 14; var pts = new Vector3[n]; var w = new float[n]; var al = new float[n]; var ipts = new Vector3[n]; var iw = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        float u = i / (float)(n - 1) * grow;
                        pts[i] = ArcPoint(a, hh, side, u);
                        float head = u / Mathf.Max(0.01f, grow);
                        w[i] = Mathf.Lerp(0.015f, 0.14f, head) * (1f - 0.4f * Vol2Fs.Smooth(0.2f, ArcHold, since));      // thin at the trip, wide at the head
                        al[i] = fade * Mathf.Lerp(0.5f, 1f, head);
                        ipts[i] = pts[i] + Dir(a) * 0.08f + Vector3.down * 0.06f; iw[i] = w[i] * 1.6f;
                    }
                    _arc.Strip(pts, w, null, Color.white, al, 0.31f + a, cam);
                    _arcInk.Strip(ipts, iw, null, Color.white, al, 0.57f + a, cam);
                    if (since < 0.2f && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_sparks, pts[n - 1], (pts[n - 1] - pts[Mathf.Max(0, n - 3)]).normalized * 6f + Random.insideUnitSphere * 2f, -1f, -1f);
                }
                // the X cuts on the struck bodies
                foreach (var (st, at) in _cuts)
                {
                    float xa = s - at;
                    if (xa < 0f || xa > 0.6f) continue;
                    var bb = st.T != null ? BodyBounds(st.T, st.T.position, 0.5f) : st.B; Vector3 c = bb.center;
                    if (xa - Time.deltaTime < 0f)
                    {
                        HitStop(t, 0.07f);
                        for (int k = 0; k < 140; k++) Vol2FsB1.EmitV(_sparks, c, (Random.insideUnitSphere + Vector3.up * 0.6f).normalized * Random.Range(5f, 13f), -1f, -1f);
                        for (int k = 0; k < 8; k++) Vol2FsB1.EmitV(_threads, c, Random.onUnitSphere * Random.Range(3f, 7f), -1f, -1f);
                    }
                    float life2 = 1f - Vol2Fs.Smooth(0.12f, 0.6f, xa), open = Vol2Fs.Smooth(0f, 0.05f, xa);
                    Vector3 view = (cam - c).normalized, right = Vector3.Cross(Vector3.up, view).normalized, up = Vector3.Cross(view, right);
                    for (int k = 0; k < 2; k++)
                    {
                        Vector3 dir = (right * (k == 0 ? 1f : -1f) + up * 1.1f).normalized;
                        _x.Strip(new[] { c - dir * 1.1f * open, c, c + dir * 1.1f * open }, new[] { 0.02f, 0.15f * (1f - 0.5f * xa), 0.02f }, null, Color.white, new[] { life2, life2, life2 }, 0.3f + k * 0.4f, cam);
                    }
                }
                // ring and snap: every arc breaks at its foot, shards sideways
                if (!_snapped && s > SnapAt)
                {
                    _snapped = true;
                    foreach (var (a, hh, side) in _arcs)
                    {
                        if (Front(a) < 0.25f) continue;
                        Vector3 b = ArcPoint(a, hh, side, 0.5f), tg = Tangent(a);
                        for (int j = 0; j < 5; j++) Vol2FsB1.EmitV(_shards, b, tg * (j % 2 == 0 ? 1f : -1f) * Random.Range(3f, 7f) + Vector3.up * Random.Range(0f, 2f), -1f, -1f);
                        for (int j = 0; j < 20; j++) Vol2FsB1.EmitV(_sparks, b, (tg * (j % 2 == 0 ? 1f : -1f) + Random.insideUnitSphere * 0.5f).normalized * Random.Range(4f, 10f), -1f, -1f);
                        Vol2FsB1.EmitV(_glints, b, Vector3.zero, 1.1f, 0.12f);
                    }
                }
                for (int k = 0; k < 3; k++) _lights[k].intensity = 4f * (1f - Vol2Fs.Smooth(0.1f, SnapAt + 0.3f, s));
            }
            Flush();
        }

        void Flush() { _thread.Write(_threadMesh); _arc.Write(_arcMesh); _arcInk.Write(_arcInkMesh); _x.Write(_xMesh); }
    }
}
