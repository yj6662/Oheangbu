using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>숭 (metal, ㅜ ring, 돌아온다) - free showcase batch 7, concept sung_a (a ring of overlapping mirror-steel blades opens outward round the caster like a folding
    /// fan to 4 m and folds back in, ink strokes between the ribs, white-gold light, sparks off the cut bears). Executor facts only: thirty-six steel blades (the
    /// kit's chrome) laid low like the ribs of a fan, their tips riding the band's radius (the report's legs: 2 m -> 4 m and back) - they fan open flat going out
    /// and stand up as they fold home; a white-gold edge glints along each tip; ink sweeps between the ribs; front blades brighter (the camera stands inside); the
    /// first touch = sparks flung sideways + the shared cut, the return touch = a thinner spray (x0.5); a shot taken is sliced; the turn = a ring of sparks along
    /// the tips. Home: the blades snap with a ring, shards flung to the side (CSV 끝: 쨍하게 끊기며 파편이 옆으로 튐).</summary>
    public sealed class Vol2FsSung : Vol2FsBackRing
    {
        static readonly Color Gold = new Color(1f, 0.86f, 0.55f);
        const int Blades = 36;
        readonly Transform[] _blades = new Transform[Blades];
        Mesh _edgeMesh, _inkMesh; readonly Vol2FsStrips _edge = new Vol2FsStrips(), _ink = new Vol2FsStrips();
        ParticleSystem _sparks, _shards;
        float _snapAt = -1f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            // r2: r1 ribs read as flat matte white paper teeth - a darker chrome (reflections carry the light) with a white-gold hot edge, a warm-grey tint
            // r3: still white-grey in r2 - darker chrome still (tint 0.6 / 0.58 / 0.55, bright 0.6), a warmer hot edge
            var steel = Kit.Metal(0.6f, 0.65f, new Color(0.07f, 0.05f, 0.02f), new Color(0.6f, 0.58f, 0.55f)) ?? Vol2FsB1.Lit(Kit, "fs_steel", new Color(0.55f, 0.53f, 0.5f), 1f, 0.95f);
            var mesh = Kit.SharedMesh("sungRib", () => Vol2FsB6.Spike(0.26f, 0.05f, 0.35f, 0.08f));
            for (int k = 0; k < Blades; k++) { _blades[k] = Vol2FsB1.Prop(root, "Rib" + k, mesh, steel, false).transform; _blades[k].gameObject.SetActive(false); }
            var e = M("fs_streak", new Color(1f, 0.72f, 0.35f) * 1.6f, 3.4f, 1f); e.NoiseAmt = 0.15f; e.Soft = 0.02f; e.Erode = 0.08f;
            Vol2Fs.MeshObject(root, "RibEdges", Kit.Mat(e), out _edgeMesh);
            Vol2Fs.MeshObject(root, "RibInk", Kit.InkSolid("fs_stroke2", InkC, Gold * 1.4f, 0.14f, 0.45f, 0.3f, 1.4f), out _inkMesh);
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Color.Lerp(Gold, Color.white, 0.5f), 5f, 1f)), Life0 = 0.2f, Life1 = 0.55f, Speed0 = 5f, Speed1 = 13f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.4f, Drag = 0.9f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.4f, Max = 1200 }, root, "Sparks");
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = steel, Mesh = Kit.SharedMesh("shard0", () => Vol2FsB1.Shard(5)), Life0 = 0.6f, Life1 = 1.1f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.05f, Size1 = 0.14f, Gravity = 2f, Drag = 0.4f, Spin = 900f, Max = 400 }, root, "Shards");
            _sparks.Play(false); _shards.Play(false);
        }

        float Ang(int k) => k * Vol2ReturnGeometry.Tau / Blades;

        void SparkSide(Vector3 at, int n, float s0 = 5f, float s1 = 13f)
        {
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(CamPos - at));
            for (int k = 0; k < n; k++) Vol2FsB1.EmitV(_sparks, at, (side * (k % 2 == 0 ? 1f : -1f) + Random.insideUnitSphere * 0.7f + Vector3.up * 0.3f).normalized * Random.Range(s0, s1), -1f, -1f);
        }

        protected override void OnTurn()
        {
            float t = Now(NowUs), r = RingNow(t);
            for (int k = 0; k < Blades; k += 2) { if (Front(Ang(k)) < 0.3f) continue; Vector3 p = RingPoint(Ang(k), r) + Vector3.up * 0.3f; for (int i = 0; i < 4; i++) Vol2FsB1.EmitV(_sparks, p, (Dir(Ang(k)) + Random.insideUnitSphere * 0.5f + Vector3.up * 0.4f).normalized * Random.Range(4f, 10f), -1f, -1f); }
        }

        protected override void OnStruck(Struck s) { SparkSide(CentreNow(s), 90); }
        protected override void OnReturnHit(Struck s) { SparkSide(CentreNow(s), 40, 4f, 9f); }
        protected override void OnShot(Shot s) { SparkSide(s.Pos, 50); for (int k = 0; k < 6; k++) Vol2FsB1.EmitV(_shards, s.Pos, Random.onUnitSphere * 4f, -1f, -1f); }

        protected override void OnHome()
        {
            _snapAt = Now(NowUs);
            float r = RingNow(_snapAt);
            for (int k = 0; k < Blades; k += 2)
            {
                float a = Ang(k); if (Front(a) < 0.3f) continue;
                Vector3 p = RingPoint(a, r * 0.8f) + Vector3.up * 0.5f, sd = Vector3.Cross(Vector3.up, Dir(a));
                for (int i = 0; i < 2; i++) Vol2FsB1.EmitV(_shards, p, sd * (i == 0 ? 1f : -1f) * Random.Range(3f, 7f) + Vector3.up * Random.Range(0.5f, 2.5f), -1f, -1f);
                for (int i = 0; i < 4; i++) Vol2FsB1.EmitV(_sparks, p, (sd * (i % 2 == 0 ? 1f : -1f) + Random.insideUnitSphere * 0.6f).normalized * Random.Range(4f, 10f), -1f, -1f);
            }
        }

        protected override void DrawRing(float t, float r, float k01, bool back, float life)
        {
            var cam = CamPos;
            _edge.Clear(); _ink.Clear();
            bool show = Launched && life > 0.01f && _snapAt < 0f;
            float open = Vol2Fs.Smooth(LaunchAt, LaunchAt + 0.1f, t);
            float stand = back ? Vol2Fs.Smooth(TurnRel, HomeRel > TurnRel ? HomeRel : TurnRel + 0.5f, t) : 0f;
            float inner = Mathf.Max(0.7f, r * 0.5f);
            for (int k = 0; k < Blades; k++)
            {
                var b = _blades[k];
                b.gameObject.SetActive(show);
                if (!show) continue;
                float a = Ang(k), fr = Front(a);
                float len = Mathf.Max(0.2f, (r - inner) * 1.15f * open);
                float tilt = Mathf.Lerp(12f, 55f, stand);              // lies low going out, stands up folding home
                Vector3 outDir = Dir(a + 0.12f);                        // a little sweep, like overlapping fan ribs
                Vector3 axis = Quaternion.AngleAxis(-tilt, Vector3.Cross(Vector3.up, outDir)) * outDir;
                Vector3 basePt = RingPoint(a, inner) + Vector3.up * 0.06f;
                b.SetPositionAndRotation(basePt, Quaternion.LookRotation(Vector3.Cross(axis, Vector3.Cross(Vector3.up, outDir)).normalized, axis));
                b.localScale = new Vector3(1.2f, len, 1f);
                Vector3 tip = basePt + axis * len, sdv = Vector3.Cross(Vector3.up, outDir) * 0.13f;
                // r2: an ink underside (a dark feathered stroke on the ground under every rib) and gold glints running along both edges
                {
                    Vector3 g0 = basePt, g1 = basePt + outDir * len * 0.9f; g0.y = Ground + 0.025f; g1.y = Ground + 0.025f;
                    _ink.Strip(new[] { g0, Vector3.Lerp(g0, g1, 0.5f) + sdv * 0.6f, g1 }, new[] { 0.12f, 0.48f, 0.06f }, null, Color.white, new[] { life * 0.8f, life * 0.9f, life * 0.6f }, 0.15f + k * 0.07f, cam);
                }
                if (fr > 0.15f)
                {
                    float run = Mathf.Repeat(t * 1.6f + k * 0.13f, 1f);
                    for (int e2 = -1; e2 <= 1; e2 += 2)
                    {
                        Vector3 eb = basePt + sdv * e2 * 0.8f, et = tip;
                        Vector3 em = Vector3.Lerp(eb, et, 0.3f + 0.6f * run);
                        _edge.Strip(new[] { eb, em, et }, new[] { 0.018f, 0.065f, 0.012f }, null, Color.white, new[] { life * fr * 0.3f, life * fr, life * fr * 0.6f }, 0.4f + k * 0.03f + e2 * 0.1f, cam);
                    }
                }
            }
            // the ink sweeps between the ribs: a few curved dark strokes along the ring, ahead brighter
            if (show)
                for (int s = 0; s < 6; s++)
                {
                    float a0 = s * Vol2ReturnGeometry.Tau / 6f + 0.3f * Mathf.Max(0f, t - LaunchAt);
                    int n = 12; var pts = new Vector3[n]; var w = new float[n]; var al = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        float u = i / (float)(n - 1), a = a0 + u * 0.8f;
                        pts[i] = RingPoint(a, Mathf.Lerp(inner, r * 0.95f, u)) + Vector3.up * 0.05f;
                        w[i] = 0.35f * Mathf.Max(0f, Mathf.Sin(u * Mathf.PI)) + 0.03f; al[i] = life * (0.3f + 0.7f * Front(a));
                    }
                    _ink.Strip(pts, w, null, Color.white, al, 0.2f + s * 0.11f, cam);
                }
            _edge.Write(_edgeMesh); _ink.Write(_inkMesh);
        }
    }
}
