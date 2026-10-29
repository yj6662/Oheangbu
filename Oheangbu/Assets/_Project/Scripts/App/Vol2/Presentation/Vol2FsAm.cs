using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>암 (water, ㅏ path, 심는다) - free showcase batch 5, concept am_b (a small floating bubble of water with ink curling inside drifts over every bear
    /// the path passed; over the first bear it swells and bursts into a falling sheet of water that crashes over it, spray and foam everywhere, while the bubbles
    /// over the farther bears still float). Executor facts only: "Plant" = a bubble wells up over the body -> the wait (it bobs and wobbles on the heartbeat, the
    /// ink inside turns, the ink dial burns down; the last 0.4 s it quivers) -> Consumed = it swells for a heartbeat and bursts: a curtain of water pours down
    /// over the body, a splash crown and foam ring at its feet, the body stays drenched (the 4 s wet is the status sink's, mirrored with drips) and the ground
    /// darkens; the water runs down (CSV 끝: 아래로 흘러내림); Expired = the bubble sags and runs down over the body, a small wet patch.</summary>
    public sealed class Vol2FsAm : Vol2FsSeedPath
    {
        static readonly Color Sea = new Color(0.32f, 0.64f, 1f);
        const float WetSeconds = 4f;
        ParticleSystem _spray, _drips, _foam, _inkSwirl;
        Material _water, _sheetMat;
        Mesh _sheetMesh, _wetMesh; readonly Vol2FsStrips _sheetS = new Vol2FsStrips(), _wetS = new Vol2FsStrips();
        protected override float MarkLift => 0.62f;
        protected override float DialR => 0.42f;
        protected override float FireSeconds => 1.6f;
        protected override float ExpireSeconds => 1.2f;
        protected override float Tail => 0.4f;

        sealed class Look { public Mesh Orb; public MeshRenderer OrbR; public Vector3 Burst, Feet; public float Rnd; }

        protected override void OnSetup()
        {
            var root = Root.transform;
            _water = Vol2FsB1.Water(Kit, 1.5f, 0.35f);
            _sheetMat = Vol2FsB1.Water(Kit, 1.6f, 0.4f);
            var spray = M("fs_splash", new Color(0.5f, 0.78f, 0.95f, 1f), 1.2f, 0.45f); spray.NoiseAmt = 0.3f; spray.Soft = 0.1f;
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(spray), Life0 = 0.5f, Life1 = 1.1f, Speed0 = 2f, Speed1 = 7f, Size0 = 0.08f, Size1 = 0.26f, Radius = 0.3f, Gravity = 1.8f, Drag = 0.6f, Tiles = 2, Spin = 120f, Max = 700 }, root, "Spray");
            var drip = M("fs_dot", new Color(0.7f, 0.9f, 1f, 1f), 1.6f, 0.6f);
            _drips = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drip), Life0 = 0.35f, Life1 = 0.6f, Speed0 = 0f, Speed1 = 0.4f, Size0 = 0.03f, Size1 = 0.06f, Radius = 0.1f, Gravity = 1.6f, Stretch = true, StretchVel = 0.06f, Max = 400 }, root, "Drips");
            var foam = M("fs_puff", new Color(0.62f, 0.82f, 0.92f, 1f), 1.0f, 0.3f); foam.Opacity = 0.5f; foam.NoiseAmt = 0.5f;
            _foam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(foam), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 2f, Speed1 = 4f, Size0 = 0.3f, Size1 = 0.7f, Shape = ParticleSystemShapeType.Circle, Radius = 0.5f, ShapeRot = new Vector3(-90f, 0f, 0f), Drag = 3f, Tiles = 2, SizeEnd = 1.6f, Max = 200, SortFudge = 6 }, root, "Foam");
            var ink = M("fs_ink", InkC, 1f, 0f); ink.NoiseAmt = 0.3f; ink.Soft = 0.15f;
            _inkSwirl = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ink), Life0 = 0.25f, Life1 = 0.4f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.05f, Size1 = 0.09f, Tiles = 2, Max = 200, SortFudge = -4 }, root, "InkInside");
            foreach (var ps in new[] { _spray, _drips, _foam, _inkSwirl }) ps.Play(false);
            Vol2Fs.MeshObject(root, "Sheets", _sheetMat, out _sheetMesh);
            var wet = M("fs_wet", new Color(0.15f, 0.2f, 0.24f, 1f), 1f, 0f); wet.Opacity = 0.6f; wet.NoiseAmt = 0.4f; wet.Soft = 0.02f;
            var wr = Vol2Fs.MeshObject(root, "Wet", Kit.Mat(wet), out _wetMesh);
        }

        Look L(Pod p) => p.Data as Look;

        protected override void Planted(Pod p)
        {
            var l = new Look { Rnd = Vol2Fs.Hash(Seed, p.Index + 11) }; p.Data = l;
            l.OrbR = Vol2Fs.MeshObject(Root.transform, "Bubble" + p.Index, _water, out l.Orb);
            Vol2FsB1.Emit(_spray, MarkPoint(p), 14);
        }

        protected override void OnFire(Pod p)
        {
            var l = L(p); if (l == null) return;
            l.Burst = MarkPoint(p); l.Feet = G(HostFeet(p));
        }

        protected override void OnPodEnd(Pod p)
        {
            var l = L(p); if (l == null || Worked(p)) return;
            l.Feet = G(HostFeet(p)); l.Burst = MarkPoint(p);
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos; _sheetS.Clear(); _wetS.Clear();
            foreach (var p in Pods)
            {
                var l = L(p); if (l == null) continue;
                if (!Worked(p))
                {
                    float life = PodLife(p, t, 0.3f), beat = Beat(p), warn = PodWarn(p, t), ex = PodExit(p, t);
                    Vector3 c = MarkPoint(p) + Vector3.up * 0.06f * Mathf.Sin(t * 1.9f + l.Rnd * 6f);
                    if (Withered(p))
                    {
                        // the bubble sags and runs down over the body
                        float run = Vol2Fs.Smooth(0f, 0.6f, ex);
                        c = Vector3.Lerp(l.Burst, HostNow(p).center + Vector3.up * 0.4f, run);
                        if (Time.frameCount % 2 == 0) for (int k = 0; k < 2; k++) Vol2FsB1.EmitV(_drips, HostNow(p).center + Random.insideUnitSphere * 0.5f, Vector3.down * 0.5f, -1f, -1f);
                        float wet = Vol2Fs.Smooth(0.3f, 0.6f, ex) * (1f - Vol2Fs.Smooth(0.85f, 1f, ex));
                        if (wet > 0.01f) _wetS.Quad(l.Feet + Vector3.up * 0.02f, Vector3.right, Vector3.forward, 1.3f, new Color(1f, 1f, 1f, wet), l.Rnd);
                        life = 1f - run;
                    }
                    else if (p.Ended) life = 0f;
                    float r = 0.27f * (1f + 0.07f * beat + 0.1f * warn) * Mathf.Max(0f, life);
                    if (r > 0.01f)
                    {
                        float wob = 0.06f + 0.12f * warn, ph = t * (6f + 20f * warn) * (1f - Held01) + l.Rnd * 9f;
                        Vol2FsB1.WaterSphere(l.Orb, c, r, -1f, cam, Mathf.Clamp01(life) * 0.9f, d => d * (1f + wob * Mathf.Sin(ph + d.x * 4f + d.y * 3f)) + Vector3.down * (Withered(p) ? 0.4f * Mathf.Max(0f, d.y) : 0f), 20, 12, 1.5f);
                        // the ink turning inside it
                        if (!p.Ended) for (int k = 0; k < 2; k++) { float a = t * 3f + k * Mathf.PI + l.Rnd * 5f; Vol2FsB1.EmitV(_inkSwirl, c + new Vector3(Mathf.Cos(a), Mathf.Sin(a * 1.3f) * 0.5f, Mathf.Sin(a)) * r * 0.45f, Vector3.zero, -1f, -1f); }
                    }
                    else l.Orb.Clear();
                    continue;
                }
                float s = SinceFire(p, t);
                // swell (0.1 s) then burst
                float sw = Vol2Fs.Smooth(0f, 0.1f, s);
                if (s < 0.1f) Vol2FsB1.WaterSphere(l.Orb, l.Burst, Mathf.Lerp(0.3f, 0.9f, sw), -1f, cam, 0.9f, null, 20, 12, 1.5f);
                else if (l.Orb.vertexCount > 0)
                {
                    l.Orb.Clear(); HitStop(t, 0.07f);
                    Vol2FsB1.Emit(_spray, l.Burst, 70); Vol2FsB1.Emit(_foam, l.Feet + Vector3.up * 0.05f, 40);
                    for (int k = 0; k < 80; k++) Vol2FsB1.EmitV(_spray, l.Feet + Vector3.up * 0.1f, (new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized * Random.Range(2f, 5f) + Vector3.up * Random.Range(2f, 5f)), Random.Range(0.15f, 0.4f), -1f);
                }
                // the curtain: strips of falling water round the body from above it to the ground, 0.1 - 0.9 s
                float fall = s - 0.1f;
                if (fall > 0f && fall < 0.9f)
                {
                    // r3: the water pours OVER the body from the burst point (narrow at the top, spreading as it falls, splashing out at the feet) in uneven
                    // ropes of different widths and lengths, not a cylinder (r2 read as a blue lampshade)
                    var b = HostNow(p); float rr = Mathf.Max(b.extents.x, b.extents.z) * 1.25f;
                    Vector3 src = l.Burst, mid0 = new Vector3(b.center.x, b.max.y + 0.15f, b.center.z), foot = new Vector3(b.center.x, Ground, b.center.z);
                    for (int k = 0; k < 18; k++)
                    {
                        float hk = Vol2Fs.Hash(Seed, k), a = (k + 0.6f * hk) / 18f * Mathf.PI * 2f + l.Rnd;
                        Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        float lag = 0.12f * Vol2Fs.Hash(Seed, k + 30), f2 = fall - lag;
                        if (f2 <= 0f) continue;
                        float head = Mathf.Clamp01(f2 / 0.28f), tail = Vol2Fs.Smooth(0.35f, 0.85f, f2);
                        var pts = new List<Vector3>(); var wd = new List<float>(); var al = new List<float>();
                        for (int i = 0; i <= 6; i++)
                        {
                            float u = Mathf.Lerp(tail, head, i / 6f);
                            Vector3 q = u < 0.35f ? Vector3.Lerp(src + o * 0.2f, mid0 + o * rr * 0.8f, u / 0.35f) : Vector3.Lerp(mid0 + o * rr * 0.8f, foot + o * rr * (1.2f + 0.5f * hk), (u - 0.35f) / 0.65f);
                            q += Vector3.Cross(Vector3.up, o) * Mathf.Sin(u * 7f + k) * 0.06f;
                            pts.Add(q); wd.Add((0.18f + 0.35f * hk) * (0.6f + 0.8f * u)); al.Add(0.9f * (1f - Vol2Fs.Smooth(0.55f, 0.95f, f2)) * (0.6f + 0.4f * hk));
                        }
                        _sheetS.Strip(pts, wd, null, Color.white, al, hk, cam, 0f, 1.5f);
                        if (head >= 1f && f2 - 0.28f < 0.05f) for (int j = 0; j < 6; j++) Vol2FsB1.EmitV(_spray, foot + o * rr * 1.3f, (o * Random.Range(1.5f, 3.5f) + Vector3.up * Random.Range(1.5f, 3f)), Random.Range(0.1f, 0.25f), -1f);
                    }
                    if (Time.frameCount % 2 == 0) for (int j = 0; j < 2; j++) { float a = Random.value * Mathf.PI * 2f; Vol2FsB1.EmitV(_inkSwirl, Vector3.Lerp(src, mid0, Random.value) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rr * 0.7f, Vector3.down * 3f, 0.12f, 0.35f); }
                }
                // drenched: drips for the wet's 4 s, the ground dark under it
                if (s < WetSeconds && Time.frameCount % 2 == 0) { var b = HostNow(p); Vol2FsB1.EmitV(_drips, b.center + new Vector3(Random.Range(-b.extents.x, b.extents.x), Random.Range(-0.3f, b.extents.y), Random.Range(-b.extents.z, b.extents.z)), Vector3.down * 0.3f, -1f, -1f); }
                float w = Vol2Fs.Smooth(0.2f, 0.5f, s) * (1f - Vol2Fs.Smooth(WetSeconds - 0.5f, WetSeconds, s));
                if (w > 0.01f) _wetS.Quad(l.Feet + Vector3.up * 0.02f, Vector3.right, Vector3.forward, 2.6f, new Color(1f, 1f, 1f, w), l.Rnd);
            }
            _sheetS.Write(_sheetMesh); _wetS.Write(_wetMesh);
        }

        protected override float PodExitSeconds(Vol2MaterialEnd why) => why == Vol2MaterialEnd.Consumed ? WetSeconds : base.PodExitSeconds(why);
    }
}
