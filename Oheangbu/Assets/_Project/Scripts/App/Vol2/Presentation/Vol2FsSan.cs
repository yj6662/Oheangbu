using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>산 (metal, ㅏ path, 번진다) - free showcase batch 4, concept san_b (steel lightning: from the first struck bear, forked jagged threads of white-silver
    /// light fork out like lightning to every neighbour, cutting where they land with a sharp flash and sparks; glinting steel stays in the ground where it struck).
    /// Beats, all executor facts: charge (light threads drawn into the tip) -> a needle of steel flies to the predicted first body -> the cut (X flash, sparks) ->
    /// every "Spread" contact = one forked bolt of steel-light jumping from the struck body to that neighbour in the executor's order (its brightness / width by
    /// the share: 1.0 split n ways) -> the cut on it -> its Mark (3 s) = a burnt-in silver scar on the ground with three caltrops that keep crackling small arcs
    /// between them; warning = the arcs stutter -> natural expiry (CSV: 쨍하게 끊기며 파편이 옆으로 튐) = a ringing flash, the caltrops snap, shards fly sideways.</summary>
    public sealed class Vol2FsSan : Vol2FsSpread
    {
        static readonly Color Silver = new Color(0.85f, 0.92f, 1f), Gold = new Color(1f, 0.9f, 0.6f), Ink = new Color(0.03f, 0.03f, 0.035f);
        ParticleSystem _charge, _sparks, _shards, _flash, _ink; Light _hitLight;
        Material _steel, _steelHot; Mesh _needleMesh, _shardMesh, _caltropMesh;
        Transform _needle;
        Mesh _boltMesh, _cutMesh, _scarMesh, _streakMesh; readonly Vol2FsStrips _bolts = new Vol2FsStrips(), _cuts = new Vol2FsStrips(), _scars = new Vol2FsStrips(), _streak = new Vol2FsStrips();
        readonly List<(Vector3 At, float T, float K)> _cutAt = new List<(Vector3, float, float)>();
        Vector3 _hit;

        sealed class Arc { public int Salt; public float Next; }
        sealed class Scar { public List<Transform> Caltrops = new List<Transform>(); public bool Snapped; }

        protected override float LobHeight => 0.15f;
        protected override float HopSpeed => 30f;
        protected override float HopMin => 0.08f;
        protected override float HopMax => 0.16f;
        protected override float HopStagger => 0.1f;
        protected override float MarkExpireSeconds => 0.6f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB1.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _steel = Vol2FsB4.Steel(Kit, 1f); _steelHot = Vol2FsB4.Steel(Kit, 1.8f, true);
            _needleMesh = Kit.SharedMesh("b1_blade", Vol2FsB1.Blade);
            _shardMesh = Kit.SharedMesh("b4_san_shard", () => Vol2FsB1.Shard(41));
            _caltropMesh = Kit.SharedMesh("b4_san_caltrop", () => Vol2FsB1.Shard(57));
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Silver, 4f, 1f)), Rate = 80f, Loop = true, Life0 = 0.15f, Life1 = 0.28f, Speed0 = -2f, Speed1 = -3.2f, Size0 = 0.02f, Size1 = 0.05f, Radius = 0.6f, World = false, Stretch = true, StretchVel = 0.05f, Max = 120 }, root, "Charge");
            _charge.transform.position = Tip(); _charge.Play(false);
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 5f, 1f)), Life0 = 0.25f, Life1 = 0.7f, Speed0 = 3f, Speed1 = 9f, Size0 = 0.02f, Size1 = 0.045f, Radius = 0.1f, Gravity = 1.4f, Drag = 0.6f, Stretch = true, StretchVel = 0.035f, Max = 1500 }, root, "Sparks");
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = _steelHot, Mesh = _shardMesh, Life0 = 0.6f, Life1 = 1.1f, Speed0 = 3f, Speed1 = 7f, Size0 = 0.06f, Size1 = 0.16f, Radius = 0.1f, Gravity = 2f, Spin = 720f, Max = 200 }, root, "Shards");
            _flash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Silver, 3f, 1f)), Life0 = 0.08f, Life1 = 0.14f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.9f, Size1 = 1.3f, Radius = 0.02f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 0.2f, Max = 40, SortFudge = -10 }, root, "Flash");
            var ink = M("fs_ink", Ink, 1f, 0f); ink.Soft = 0.1f; ink.NoiseAmt = 0.3f;
            _ink = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ink), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.05f, Size1 = 0.16f, Radius = 0.1f, Gravity = 1.6f, Tiles = 2, Max = 300 }, root, "InkSpecks");
            foreach (var ps in new[] { _sparks, _shards, _flash, _ink }) ps.Play(false);
            var bolt = M("fs_streak", Silver, 5f, 1f); bolt.NoiseAmt = 0.3f; bolt.Erode = 0.1f; bolt.Soft = 0.05f;
            Vol2Fs.MeshObject(root, "SteelLightning", Kit.Mat(bolt), out _boltMesh);
            var cut = M("fs_streak", Color.white, 4.5f, 1f); cut.NoiseAmt = 0.2f; cut.Soft = 0.05f;
            Vol2Fs.MeshObject(root, "Cuts", Kit.Mat(cut), out _cutMesh);
            Vol2Fs.MeshObject(root, "Streak", Kit.Mat(bolt), out _streakMesh);
            var sc = M("fs_crack", new Color(0.6f, 0.68f, 0.8f, 1f), 1.6f, 0.7f); sc.Soft = 0.001f; sc.NoiseAmt = 0.3f; sc.Erode = 0.2f; sc.Edge = new Color(0.04f, 0.04f, 0.05f, 1f); sc.EdgeWidth = 0.2f;
            Vol2Fs.MeshObject(root, "Scars", Kit.Mat(sc), out _scarMesh);
            _hitLight = Vol2Fs.PointLight(root, "HitLight", Silver, 6f);
        }

        protected override void OnCharge(float t, float k) { _charge.transform.position = Tip(); _charge.transform.localScale = Vector3.one * Mathf.Lerp(1.3f, 0.6f, k); }

        protected override void OnLaunch(long nowUs)
        {
            _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _needle = Vol2FsB1.Prop(Root.transform, "Needle", _needleMesh, _steelHot, false).transform;
            _needle.localScale = new Vector3(2f, 2f, 1.3f); _needle.position = From;
        }

        protected override void OnFly(float t, float f, float prev)
        {
            Vector3 p = Line(f), q = Line(Mathf.Min(1f, f + 0.05f));
            _needle.position = p; if ((q - p).sqrMagnitude > 1e-6f) _needle.rotation = Quaternion.LookRotation(q - p);
            _streak.Clear(); Vol2FsB4.Arc(_streak, From, p, 0f, 0f, 1f, 0.06f, 1f, 0.3f, CamPos, 8); _streak.Write(_streakMesh);
        }

        protected override void OnHit(long nowUs)
        {
            if (_needle != null) _needle.gameObject.SetActive(false);
            _hit = HasTarget ? HitPoint : AimPoint;
            Cut(_hit, 1.1f);
            _hitLight.transform.position = _hit;
        }

        void Cut(Vector3 at, float k)
        {
            _cutAt.Add((at, -1f, k));
            Vol2FsB1.Emit(_sparks, at, Mathf.RoundToInt(260 * k));      // r3 (Phantom Blade Zero ref, principle 4): a spark shower on every cut Vol2FsB1.Emit(_flash, at, 2); Vol2FsB1.Emit(_ink, at, Mathf.RoundToInt(30 * k)); Vol2FsB1.Emit(_shards, at, Mathf.RoundToInt(8 * k));
        }

        protected override void OnHop(Hop h) { h.Data = new Arc { Salt = Seed + (int)h.TargetId * 7 }; }

        protected override void OnLand(Hop h) { Cut(CentreNow(h), ShareScale(h.Share)); }

        protected override void OnMark(Mark m)
        {
            var sc = new Scar(); var root = Root.transform;
            for (int j = 0; j < 3; j++)
            {
                float a = j / 3f * Mathf.PI * 2f + Vol2Fs.Hash(Seed + (int)m.Id, j); Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var c = Vol2FsB1.Prop(root, "Caltrop", _caltropMesh, _steel).transform;
                c.position = m.Centre + o * m.Radius * 0.9f + Vector3.up * 0.06f; c.rotation = Quaternion.LookRotation(Vector3.up + o * 0.6f, o);
                c.localScale = Vector3.one * 0.85f * ShareScale(m.Share);
                sc.Caltrops.Add(c);
            }
            m.Data = sc;
        }

        protected override void OnMarkEnd(Mark m)
        {
            if (!(m.Data is Scar sc) || m.Why == Vol2MaterialEnd.Aborted) { if (m.Data is Scar s2) foreach (var c in s2.Caltrops) if (c != null) c.gameObject.SetActive(false); return; }
            // a ringing snap: shards fly SIDEWAYS (flat), a white flash
            sc.Snapped = true;
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(m.Centre - Inst.Origin));
            foreach (var c in sc.Caltrops)
            {
                if (c == null) continue;
                for (int j = 0; j < 5; j++) Vol2FsB1.EmitV(_shards, c.position + Vector3.up * 0.1f, (side * (j % 2 == 0 ? 1f : -1f) * Random.Range(3f, 6f) + Vector3.up * Random.Range(0.5f, 1.5f)), 0.12f, 0.8f);
                Vol2FsB1.Emit(_sparks, c.position, 25); c.gameObject.SetActive(false);
            }
            Vol2FsB1.Emit(_flash, m.Centre + Vector3.up * 0.3f, 1);
        }

        /// <summary>A jagged, forked lightning path from a to b, re-rolled every few frames (salt changes) - the steel thread "cracks" across.</summary>
        void Lightning(Vector3 a, Vector3 b, float reach, float width, float life, int salt, Vector3 cam)
        {
            Vector3 d = b - a; float len = d.magnitude; if (len < 0.05f) return;
            Vector3 dir = d / len, s1 = Vector3.Cross(dir, Vector3.up).normalized, s2 = Vector3.Cross(dir, s1);
            int n = Mathf.Max(6, Mathf.RoundToInt(len * 5f));
            var pts = new List<Vector3>(); var w = new List<float>(); var al = new List<float>();
            for (int k = 0; k <= n; k++)
            {
                float u = k / (float)n; if (u > reach) break;
                float amp = Mathf.Sin(u * Mathf.PI) * 0.35f;
                Vector3 off = (k == 0 || k == n) ? Vector3.zero : s1 * (Vol2Fs.Hash(salt, k) - 0.5f) * 2f * amp + s2 * (Vol2Fs.Hash(salt, k + 50) - 0.5f) * 1.4f * amp;
                pts.Add(a + dir * len * u + off); w.Add(width * (0.6f + 0.4f * Vol2Fs.Hash(salt, k + 90))); al.Add(life);
                // forks: short side threads
                if (k > 0 && k < n && k % 3 == 1 && Vol2Fs.Hash(salt, k + 200) > 0.4f)
                {
                    var fp = new[] { pts[pts.Count - 1], pts[pts.Count - 1] + (dir * 0.25f + s1 * (Vol2Fs.Hash(salt, k + 300) - 0.5f) * 0.9f + s2 * 0.25f) * 0.9f, pts[pts.Count - 1] + (dir * 0.4f + s1 * (Vol2Fs.Hash(salt, k + 400) - 0.5f) * 1.4f - s2 * 0.2f) * 0.9f };
                    _bolts.Strip(fp, new[] { width * 0.6f, width * 0.4f, width * 0.15f }, null, Color.white, new[] { life, life * 0.8f, life * 0.4f }, 0.6f, cam);
                }
            }
            if (pts.Count >= 2) _bolts.Strip(pts, w, null, Color.white, al, 0.2f, cam);
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            _hitLight.intensity = h < 0.04f ? 9f * h / 0.04f : 9f * Mathf.Exp(-(h - 0.04f) * 7f);
            _streak.Clear(); float sl = 1f - Vol2Fs.Smooth(0f, 0.25f, h); if (sl > 0.01f) Vol2FsB4.Arc(_streak, From, _hit, 0f, 0f, 1f, 0.05f, sl, 0.3f, cam, 8); _streak.Write(_streakMesh);
            // the cut flashes: an X of two white slash lines on every body cut (the first and each landing)
            _cuts.Clear();
            for (int i = 0; i < _cutAt.Count; i++)
            {
                var c = _cutAt[i]; if (c.T < 0f) { c.T = t; _cutAt[i] = c; }
                float a = t - c.T, life = 1f - Vol2Fs.Smooth(0.05f, 0.4f, a); if (life <= 0.01f) continue;
                Vector3 toCam = (cam - c.At).normalized, r = Vector3.Cross(Vector3.up, toCam).normalized;
                float len = 1.4f * c.K * Vol2Fs.Smooth(0f, 0.06f, a);
                for (int x = -1; x <= 1; x += 2) { Vector3 dd = (r + Vector3.up * 0.75f * x).normalized; _cuts.Strip(new[] { c.At - dd * len * 0.5f, c.At, c.At + dd * len * 0.5f }, new[] { 0.01f, 0.09f * c.K, 0.01f }, null, Color.white, new[] { life, life, life }, 0.1f, cam); }
            }
            _cuts.Write(_cutMesh);
            // the steel lightning: it cracks across in the hop's travel, then keeps crackling (re-rolled) while its mark stands, fading; brightness by the share
            _bolts.Clear();
            foreach (var hop in Hops)
            {
                if (!(hop.Data is Arc arc)) continue;
                float g = HopK(hop, t); if (g <= 0f) continue;
                float k = ShareScale(hop.Share);
                float since = t - hop.Start - hop.Travel;
                float life = g < 1f ? 1f : since < 0.7f ? 0.75f + 0.25f * Mathf.PerlinNoise(t * 30f, hop.TargetId) : Mathf.Max(0f, 1f - (since - 0.7f) / 0.5f);      // r2: held 0.7 s, flickering (r1 vanished in 0.5 s, unread)
                bool marked = hop.Mark != null && hop.Mark.EndAt < 0f;
                if (marked && since > 1.2f) life = Mathf.PerlinNoise(t * 7f, hop.TargetId) > 0.62f ? 0.45f : 0f;           // an after-crackle now and then while the mark stands
                if (life <= 0.01f) continue;
                if (t >= arc.Next) { arc.Salt += 31; arc.Next = t + 0.05f; }
                Vector3 a = (hop.SourceId != 0 ? CentreOf(hop.SourceId, hop.FromP) : hop.FromP + Vector3.up * 0.9f), b = CentreNow(hop);
                Lightning(a, b, g, 0.17f * k, life, arc.Salt, cam);
                Lightning(a, b, g, 0.5f * k, life * 0.22f, arc.Salt, cam);                                      // r2: a wide faint halo round the bolt
                if (Time.frameCount % 2 == 0) Vol2FsB1.Emit(_sparks, Vector3.Lerp(a, b, Random.value * g), 2);
                if (g < 1f) Vol2FsB1.Emit(_sparks, Vector3.Lerp(a, b, g), 4);
            }
            // the marks: a silver scar; small arcs crackle between the caltrops; the warning makes them stutter
            _scars.Clear();
            foreach (var m in MarkList)
            {
                float life = MarkLife(m, t, 0.12f), warn = MarkWarn(m, t);
                if (life > 0.01f) _scars.Quad(m.Centre + Vector3.up * 0.02f, Right, Fwd, 2.6f * ShareScale(m.Share), new Color(1f, 1f, 1f, life * (warn > 0f ? (Mathf.PerlinNoise(t * 25f, m.Id) > 0.5f ? 1f : 0.35f) : 1f)), 0.2f + m.Id % 5 * 0.13f);
                if (!(m.Data is Scar sc) || m.EndAt >= 0f || MarkAge(m, t) < 0f) continue;
                foreach (var c in sc.Caltrops) if (c != null) c.gameObject.SetActive(true);
                bool fire = warn > 0f ? Random.value < 0.25f : Random.value < 0.5f;
                if (fire && sc.Caltrops.Count >= 2)
                {
                    int i0 = Random.Range(0, sc.Caltrops.Count), i1 = (i0 + 1) % sc.Caltrops.Count;
                    if (sc.Caltrops[i0] != null && sc.Caltrops[i1] != null) Lightning(sc.Caltrops[i0].position + Vector3.up * 0.15f, sc.Caltrops[i1].position + Vector3.up * 0.15f, 1f, 0.03f, 0.8f, Random.Range(0, 9999), cam);
                    if (Random.value < 0.3f) Vol2FsB1.Emit(_sparks, m.Centre + Vector3.up * 0.15f, 2);
                }
            }
            foreach (var m in MarkList) if (m.Data is Scar sc2 && MarkAge(m, t) < 0f) foreach (var c in sc2.Caltrops) if (c != null) c.gameObject.SetActive(false);
            _bolts.Write(_boltMesh); _scars.Write(_scarMesh);
        }

        Vector3 CentreOf(long id, Vector3 fallback)
        {
            foreach (var s in Direct) if (s.Id == id) return CentreNow(s);
            return fallback + Vector3.up * 0.9f;
        }
    }
}
