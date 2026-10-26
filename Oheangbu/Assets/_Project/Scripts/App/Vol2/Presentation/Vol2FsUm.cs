using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>움 (water, ㅜ ring, 심는다) - free showcase batch 5, concept um_a (a circle of trembling water beads with ink curling inside lies on the 2 m band;
    /// where the bear stepped over it the beads burst upward one after another into a ring of splashes racing round the circle, ink flung through the
    /// water, both bears on the band drenched). Unlike 우's standing water pillars, 욱's ring river and 운's outward ripples, nothing flows while it waits:
    /// only the beads tremble. Executor facts only: the launch = the beads well up out of the band -> the wait (each bead wobbles and swells on the
    /// heartbeat, the ink inside turns, the ink arc inside the band burns down; the last 0.4 s they quiver hard) -> Consumed = from the crossing body's angle
    /// each bead bursts into a leaping splash sheet and spray racing round (RaceDelay), ink curls flung through it, foam on the ground -> every struck body
    /// takes a splash crown and stays drenched (the 4 s wet is the status sink's; mirrored with drips) while the band's wet ground dries as the water runs
    /// down (CSV 끝: 아래로 흘러내림); Expired = the beads slump and run into the ground in small wet spots that dry.</summary>
    public sealed class Vol2FsUm : Vol2FsSeedRing
    {
        static readonly Color Sea = new Color(0.32f, 0.64f, 1f);
        const float WetSeconds = 4f, SplashSeconds = 1.0f;
        ParticleSystem _spray, _drips, _foam, _inkIn, _glints;
        Material _water, _sheetMat;
        Mesh _beadMesh, _sheetMesh, _inkMesh, _wetMesh;
        readonly Vol2FsStrips _sheet = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _wet = new Vol2FsStrips();
        readonly List<bool> _burst = new List<bool>();
        readonly List<List<Vol2FsB1.Stroke>> _curls = new List<List<Vol2FsB1.Stroke>>();
        readonly List<(Struck S, float At)> _soaked = new List<(Struck, float)>();
        Light[] _lights;
        protected override int SeedCount => 22;
        protected override float RaceSeconds => 0.36f;
        protected override float FireSeconds => WetSeconds;
        protected override float ExpireSeconds => 1.4f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _water = Vol2FsB1.Water(Kit, 1.5f, 0.35f);
            _sheetMat = Vol2FsB1.Water(Kit, 1.6f, 0.4f, "fs_streak");
            var spray = M("fs_splash", new Color(0.5f, 0.78f, 0.95f, 1f), 1.15f, 0.45f);      // r3: blue-teal spray (r2: white popcorn) spray.NoiseAmt = 0.3f; spray.Soft = 0.1f;
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(spray), Life0 = 0.5f, Life1 = 1.1f, Speed0 = 1.5f, Speed1 = 6f, Size0 = 0.12f, Size1 = 0.38f, Radius = 0.2f, Gravity = 1.6f, Drag = 0.6f, Tiles = 2, Spin = 120f, Max = 1200 }, root, "Spray");
            var drip = M("fs_dot", new Color(0.7f, 0.9f, 1f, 1f), 1.6f, 0.6f);
            _drips = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drip), Life0 = 0.35f, Life1 = 0.6f, Speed0 = 0f, Speed1 = 0.4f, Size0 = 0.03f, Size1 = 0.06f, Radius = 0.1f, Gravity = 1.6f, Stretch = true, StretchVel = 0.06f, Max = 600 }, root, "Drips");
            var foam = M("fs_puff", new Color(0.62f, 0.82f, 0.92f, 1f), 1.0f, 0.3f); foam.Opacity = 0.5f; foam.NoiseAmt = 0.5f;
            _foam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(foam), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 0.6f, Speed1 = 1.8f, Size0 = 0.25f, Size1 = 0.55f, Shape = ParticleSystemShapeType.Circle, Radius = 0.25f, ShapeRot = new Vector3(-90f, 0f, 0f), Drag = 3f, Tiles = 2, SizeEnd = 1.6f, Max = 400, SortFudge = 6 }, root, "Foam");
            var ink = M("fs_ink", InkC, 1f, 0f); ink.NoiseAmt = 0.3f; ink.Soft = 0.15f;
            _inkIn = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ink), Life0 = 0.25f, Life1 = 0.4f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.03f, Size1 = 0.06f, Tiles = 2, Max = 400, SortFudge = -4 }, root, "InkInside");
            _glints = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.8f, 0.95f, 1f), 2.6f, 1f)), Life0 = 0.08f, Life1 = 0.14f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.08f, Size1 = 0.14f, Max = 200 }, root, "Glints");
            foreach (var ps in new[] { _spray, _drips, _foam, _inkIn, _glints }) ps.Play(false);
            Vol2Fs.MeshObject(root, "Beads", _water, out _beadMesh);
            Vol2Fs.MeshObject(root, "Sheets", _sheetMat, out _sheetMesh);
            Vol2Fs.MeshObject(root, "InkCurls", Kit.InkSolid("fs_stroke2", InkC, new Color(0.4f, 0.75f, 1.4f, 1f), 0.1f, 0.4f, 0.3f, 1.3f), out _inkMesh);
            var wet = M("fs_wet", new Color(0.15f, 0.2f, 0.24f, 1f), 1f, 0f); wet.Opacity = 0.6f; wet.NoiseAmt = 0.4f; wet.Soft = 0.02f; wet.Scroll = Vector2.zero;
            Vol2Fs.MeshObject(root, "Wet", Kit.Mat(wet), out _wetMesh);
            _lights = new Light[3];
            for (int k = 0; k < 3; k++) _lights[k] = Vol2Fs.PointLight(root, "Sea" + k, Sea, 3f);
        }

        protected override void Planted(Pod p)
        {
            for (int k = 0; k < SeedAng.Count; k++) { _burst.Add(false); if (Front(SeedAng[k]) > 0.3f) Vol2FsB1.Emit(_spray, OnBand(SeedAng[k], R), 2); }
        }

        protected override void Fired(Pod p)
        {
            for (int q = 0; q < 8; q++)
            {
                float a = q / 8f * Mathf.PI * 2f - Mathf.PI;
                _curls.Add(Vol2FsB1.Burst(Seed + q * 7, 2, Tangent(a), Dir(a), 0.9f, 1.8f, 0.14f, 0.26f, 1.1f, 0.5f));
            }
        }

        protected override void Hit(Pod p, Struck s) { if (!s.World) _soaked.Add((s, SinceFire(p, Now(NowUs)) + RaceDelay(AngleOf(FeetOf(s))))); }

        /// <summary>Many small wobbling water spheres in one mesh (vertex colour = the fresnel rim brightening, alpha = life), for the water particle shader.</summary>
        static void Beads(Mesh mesh, List<(Vector3 C, float R, float Life, float Wob, float Ph)> beads, Vector3 cam)
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var uv = new List<Vector3>(); var idx = new List<int>();
            const int lon = 10, lat = 7;
            foreach (var b in beads)
            {
                if (b.R < 0.005f || b.Life <= 0.01f) continue;
                int start = v.Count;
                for (int y = 0; y <= lat; y++)
                    for (int x = 0; x <= lon; x++)
                    {
                        float th = y / (float)lat * Mathf.PI, ph = x / (float)lon * Mathf.PI * 2f;
                        Vector3 d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th) * 0.8f, Mathf.Sin(th) * Mathf.Sin(ph));
                        if (d.y < -0.35f) d.y = -0.35f;      // sits on the ground: a flattened bottom
                        Vector3 pt = b.C + d * b.R * (1f + b.Wob * Mathf.Sin(b.Ph + d.x * 4f + d.y * 3f));
                        float fres = 1f - Mathf.Abs(Vector3.Dot(d.normalized, (cam - pt).normalized));
                        float br = Mathf.Lerp(0.55f, 1.5f, fres * fres);
                        v.Add(pt); c.Add(new Color(br, br, br, b.Life * Mathf.Lerp(0.6f, 1f, fres))); uv.Add(new Vector3(x / (float)lon * 2f, y / (float)lat, b.Ph * 0.1f));
                    }
                for (int y = 0; y < lat; y++)
                    for (int x = 0; x < lon; x++)
                    {
                        int a = start + y * (lon + 1) + x, bb = a + 1, cc = a + lon + 1, d = cc + 1;
                        idx.Add(a); idx.Add(bb); idx.Add(cc); idx.Add(bb); idx.Add(d); idx.Add(cc);
                    }
            }
            mesh.Clear(); mesh.SetVertices(v); mesh.SetColors(c); mesh.SetUVs(0, uv); mesh.SetTriangles(idx, 0); mesh.RecalculateBounds();
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos; var p = The;
            _sheet.Clear(); _ink.Clear(); _wet.Clear();
            if (p == null || !p.Started) { _beadMesh.Clear(); Flush(); return; }
            float life = PodLife(p, t, 0.35f), beat = Beat(p), warn = PodWarn(p, t), ex = PodExit(p, t), s = SinceFire(p, t);
            bool worked = Worked(p), withered = Withered(p);
            float grow = Mathf.Clamp01(PodAge(p, t) / 0.35f);
            var beads = new List<(Vector3, float, float, float, float)>();
            for (int k = 0; k < SeedAng.Count; k++)
            {
                float a = SeedAng[k], sc = SeedScale(a);
                if (worked && !_burst[k] && s >= RaceDelay(a))
                {
                    _burst[k] = true;
                    Vector3 b = OnBand(a, R); float hgt = Vol2FsPolish.RingHeight(a) * (Front(a) < 0.25f ? 0.4f : 1f);
                    for (int j = 0; j < 22; j++) Vol2FsB1.EmitV(_spray, b + Vector3.up * 0.1f, (Vector3.up * Random.Range(3f, 6.5f) + Dir(a) * Random.Range(0.3f, 1.6f) + Tangent(a) * Random.Range(-0.8f, 0.8f)) * hgt, Random.Range(0.15f, 0.4f), -1f);
                    Vol2FsB1.Emit(_foam, b + Vector3.up * 0.03f, 3);
                }
                bool on = !p.Ended || withered && ex < 0.6f || worked && !_burst[k];
                if (!on) continue;
                float slump = withered ? Vol2Fs.Smooth(0f, 0.6f, ex) : 0f;
                float r = 0.12f * sc * grow * (1f + 0.12f * beat + 0.08f * warn) * (1f - 0.7f * slump);
                Vector3 c = OnBand(a, R) + Vector3.up * (r * 0.7f);
                float ph = t * (5f + 22f * warn) * (1f - Held01) + k * 1.7f;
                beads.Add((c, r, Mathf.Max(life, withered ? 1f - slump : 0f) * 0.9f, 0.05f + 0.18f * warn, ph));
                if (!p.Ended && Time.frameCount % 3 == k % 3 && Front(a) > 0.3f) { float ia = t * 3f + k; Vol2FsB1.EmitV(_inkIn, c + new Vector3(Mathf.Cos(ia), Mathf.Sin(ia * 1.3f) * 0.4f, Mathf.Sin(ia)) * r * 0.45f, Vector3.zero, -1f, -1f); }
                if (!p.Ended && Time.frameCount % 5 == k % 5 && Front(a) > 0.3f) Vol2FsB1.EmitV(_glints, c + Vector3.up * r * 0.5f, Vector3.zero, 0.08f + 0.08f * beat, 0.1f);
                if (withered && Time.frameCount % 6 == k % 6) Vol2FsB1.EmitV(_drips, c, Vector3.down * 0.3f, -1f, -1f);
            }
            Beads(_beadMesh, beads, cam);
            if (!p.Ended) for (int k = 0; k < 3; k++) { _lights[k].transform.position = OnBand(-1.2f + 1.2f * k, R) + Vector3.up * 0.4f; _lights[k].intensity = life * (0.3f + 0.8f * beat + warn) * (1f - 0.6f * Held01); }
            else if (!worked) for (int k = 0; k < 3; k++) _lights[k].intensity = 0f;
            if (withered)
            {
                float wspot = Vol2Fs.Smooth(0.2f, 0.5f, ex) * (1f - Vol2Fs.Smooth(0.75f, 1f, ex));
                if (wspot > 0.01f) Vol2FsB2.GroundRing(_wet, Apex, F, Ground, R - 0.2f, R + 0.2f, 72, a => wspot * Mathf.Lerp(0.4f, 1f, Front(a)), 0.47f, cam, 0.015f, 0f, 6f);
            }
            if (worked)
            {
                // the splash sheets: per bead a leaping curved sheet that rises and falls (SplashSeconds), ink curls flung through them
                int n = SeedAng.Count;
                for (int k = 0; k < n; k++)
                {
                    float a = SeedAng[k], since = s - RaceDelay(a);
                    if (since <= 0f || since > SplashSeconds) continue;
                    float hgt = Vol2FsPolish.RingHeight(a) * (Front(a) < 0.25f ? 0.35f : 1f) * (1.5f + 0.8f * Vol2Fs.Hash(Seed, k));
                    float rise = Vol2Fs.Smooth(0f, 0.3f, since), fall = Vol2Fs.Smooth(0.35f, SplashSeconds, since);
                    Vector3 b = OnBand(a, R), o = Dir(a);
                    float top = hgt * rise * (1f - fall);
                    var pts = new[] { b, b + Vector3.up * top * 0.5f + o * 0.15f, b + Vector3.up * top + o * 0.45f * rise, b + Vector3.up * top * 0.85f + o * 0.8f * rise };
                    float al = 0.8f * (1f - Vol2Fs.Smooth(0.6f, SplashSeconds, since));
                    _sheet.Strip(pts, new[] { 0.5f, 0.42f, 0.32f, 0.2f }, null, Color.white, new[] { al, al, al * 0.8f, al * 0.4f }, Vol2Fs.Hash(Seed, k + 9), cam, 0f, 1.5f);
                    if (fall > 0f && Time.frameCount % 3 == k % 3) Vol2FsB1.EmitV(_drips, b + Vector3.up * top + o * 0.5f, Vector3.down * 2f, -1f, -1f);
                }
                for (int q = 0; q < _curls.Count; q++)
                {
                    float a = q / (float)_curls.Count * Mathf.PI * 2f - Mathf.PI;
                    if (Front(a) < 0.3f) continue;
                    float since = s - RaceDelay(a);
                    if (since > 0f) Vol2FsB1.DrawBurst(_ink, _curls[q], OnBand(a, R) + Vector3.up * 0.6f, since, cam, 0.5f, 0.1f);
                }
                // the wet band runs off and dries over the wet's 4 s
                float w = Vol2Fs.Smooth(0.1f, 0.4f, s) * (1f - Vol2Fs.Smooth(WetSeconds - 0.8f, WetSeconds, s));
                if (w > 0.01f) Vol2FsB2.GroundRing(_wet, Apex, F, Ground, R - 0.55f, R + 0.65f, 72, a => w * (s >= RaceDelay(a) ? 1f : 0f) * Mathf.Lerp(0.4f, 1f, Front(a)), 0.47f, cam, 0.015f, 0f, 6f);
                foreach (var (st, at) in _soaked)
                {
                    float b = s - at; if (b < 0f) continue;
                    var bb = st.T != null ? BodyBounds(st.T, st.T.position, 0.5f) : st.B;
                    if (b - Time.deltaTime < 0f) { HitStop(t, 0.06f); Vol2FsB1.Emit(_spray, bb.center, 90); Vol2FsB1.Emit(_foam, G(bb.center), 10); }
                    if (b < WetSeconds && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_drips, bb.center + new Vector3(Random.Range(-bb.extents.x, bb.extents.x), Random.Range(-0.3f, bb.extents.y), Random.Range(-bb.extents.z, bb.extents.z)), Vector3.down * 0.3f, -1f, -1f);
                }
                for (int k = 0; k < 3; k++) _lights[k].intensity = 2.5f * (1f - Vol2Fs.Smooth(0.1f, 1.2f, s));
            }
            Flush();
        }

        void Flush() { _sheet.Write(_sheetMesh); _ink.Write(_inkMesh); _wet.Write(_wetMesh); }
    }
}
