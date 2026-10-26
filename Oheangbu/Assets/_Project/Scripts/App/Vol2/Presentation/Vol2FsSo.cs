using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>소 (metal, ㅗ fan, 한 번 · 벤다) - free showcase batch 1, concept so_a (a fan of blades: like a folding fan snapping open, two dozen long thin steel
    /// blades spray out of the brush across the whole fan, each leaving a razor-thin bright streak, slicing every body inside - white cut lines, sparks, black
    /// ink spatter - and at the fan's edge the blades snap with a ring into shards flung sideways). Cold, fast; one instant (no lasting state).</summary>
    public sealed class Vol2FsSo : Vol2FsSweep
    {
        const float OpenSeconds = 0.16f, Speed = 15f;      // r2: slower, so the fan of blades is seen (r1: a white blur)
        static readonly Color White = new Color(1f, 0.97f, 0.9f), Gold = new Color(1f, 0.85f, 0.55f), Ink = new Color(0.03f, 0.03f, 0.035f);

        sealed class Blade { public Transform T; public MeshRenderer R; public Vector3 Dir, From; public float At, Len, Reach, Spin; public bool Snapped; }
        readonly List<Blade> _blades = new List<Blade>();
        ParticleSystem _lines, _sparks, _shards, _glints, _flash, _drops; Light _chargeLight, _light;
        Mesh _streakMesh, _cutMesh, _inkMesh, _edgeMesh; readonly Vol2FsStrips _streak = new Vol2FsStrips(), _cut = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _edge = new Vol2FsStrips();
        readonly List<Vol2FsB1.Stroke> _spatter = new List<Vol2FsB1.Stroke>(); readonly List<Vector3> _spatterAt = new List<Vector3>(); readonly List<float> _spatterT = new List<float>();
        Vector3 _from;

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsB1.M(tex, tint, intensity, addMix);
        protected override float LookSeconds => OpenSeconds + (Depth + 0.8f) / Speed + 1.2f;
        protected override float StruckAt(float radial01, float angle01) => OpenSeconds * angle01 + radial01 * Depth / Speed;

        protected override void OnBegin()
        {
            var root = Root.transform;
            var line = M("fs_streak", White, 4f, 1f); line.NoiseAmt = 0.2f;
            _lines = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(line), Rate = 70f, Loop = true, Life0 = 0.12f, Life1 = 0.2f, Speed0 = -9f, Speed1 = -14f, Size0 = 0.025f, Size1 = 0.045f, Radius = 1.3f, Stretch = true, StretchVel = 0.03f, World = false, Max = 70 }, root, "Lines");
            _chargeLight = Vol2Fs.PointLight(_lines.transform, "ChargeLight", White, 3.5f);
            _lines.transform.position = Tip();
            _lines.Play(false);
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 6f, 1f)), Life0 = 0.25f, Life1 = 0.7f, Speed0 = 6f, Speed1 = 16f, Size0 = 0.025f, Size1 = 0.06f, Radius = 0.15f, Gravity = 1f, Drag = 1.6f, Stretch = true, StretchVel = 0.035f, Max = 900 }, root, "Sparks");
            var shardMat = Vol2FsB1.Lit(Kit, "fs_steel", Color.white, 1f, 0.95f, new Color(1.2f, 1.15f, 1.05f), null);
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = shardMat, Mesh = Kit.SharedMesh("shard0", () => Vol2FsB1.Shard(5)), Life0 = 0.7f, Life1 = 1.3f, Speed0 = 5f, Speed1 = 11f, Size0 = 0.1f, Size1 = 0.28f, Radius = 0.2f, Gravity = 1.4f, Drag = 0.8f, Spin = 900f, Max = 200 }, root, "Shards");
            _glints = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", White, 7f, 1f)), Life0 = 0.08f, Life1 = 0.16f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.15f, Size1 = 0.3f, Radius = 0.01f, Max = 300, SortFudge = -10 }, root, "Glints");
            _flash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", White, 6f, 1f)), Life0 = 0.1f, Life1 = 0.14f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.7f, Size1 = 0.9f, Radius = 0.01f, SizeStart = 0.3f, SizePeak = 1f, SizePeakAt = 0.2f, SizeEnd = 0.8f, Max = 10, SortFudge = -30 }, root, "Flash");
            var drop = M("fs_ink", Ink, 1f, 0f); drop.Edge = new Color(1.2f, 1.15f, 1.0f, 1f); drop.EdgeWidth = 0.1f; drop.NoiseAmt = 0.3f; drop.Soft = 0.1f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 3f, Speed1 = 9f, Size0 = 0.08f, Size1 = 0.3f, Radius = 0.3f, Gravity = 2f, Drag = 0.4f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 400 }, root, "InkDrops");
            foreach (var ps in new[] { _sparks, _shards, _glints, _flash, _drops }) ps.Play(false);
        }

        protected override void OnCharge(float t, float k)
        {
            _lines.transform.position = Tip();
            _chargeLight.intensity = 3.5f * k;
        }

        protected override void OnLaunch(long nowUs)
        {
            var root = Root.transform;
            _lines.Stop(false, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f;
            _from = Tip();
            var steel = Vol2FsB1.Lit(Kit, "fs_steel", new Color(0.92f, 0.94f, 0.97f), 1f, 0.93f, new Color(0.8f, 0.82f, 0.86f), null);      // r4: a silver self-light (r3 mirrored the red ground: pink blades)
            var mesh = Kit.SharedMesh("blade", () => Vol2FsB1.Blade());
            int n = 24;
            for (int k = 0; k < n; k++)
            {
                float u = (k + 0.5f) / n;
                float ang = Mathf.Lerp(-Half * 1.35f, Half * 1.35f, u) + 0.04f * (Vol2Fs.Hash(Seed, k) - 0.5f);
                float tgtY = Ground + Mathf.Lerp(0.2f, 3.4f, Vol2Fs.Hash(Seed, k + 30));      // r3: the blades fan up as well as across (seen from behind, a flat fan is end-on)
                Vector3 far = Apex + Dir(ang) * (Depth + 0.6f); far.y = tgtY;
                Vector3 d = (far - _from).normalized;
                var r = Vol2FsB1.Prop(root, "Blade", mesh, steel, false);
                float len = Mathf.Lerp(2.0f, 3.0f, Vol2Fs.Hash(Seed, k + 60));
                r.transform.localScale = new Vector3(2.6f, 2.6f, len);
                r.enabled = false;
                _blades.Add(new Blade { T = r.transform, R = r, Dir = d, From = _from, At = OpenSeconds * u, Len = len, Reach = Vector3.Distance(_from, far), Spin = (Vol2Fs.Hash(Seed, k + 90) - 0.5f) * 90f });
            }
            var st = M("fs_streak", Gold, 5f, 1f); st.NoiseAmt = 0.2f;
            Vol2Fs.MeshObject(root, "Streaks", Kit.Mat(st), out _streakMesh);
            var edge = M("fs_streak", White, 5f, 1f); edge.NoiseAmt = 0.1f;
            Vol2Fs.MeshObject(root, "Edges", Kit.Mat(edge), out _edgeMesh);
            var cut = M("fs_streak", White, 7f, 1f); cut.NoiseAmt = 0.1f;
            Vol2Fs.MeshObject(root, "Cuts", Kit.Mat(cut), out _cutMesh);
            Vol2Fs.MeshObject(root, "InkSpatter", Kit.InkSolid("fs_stroke2", Ink, new Color(1.3f, 1.2f, 1.05f, 1f), 0.08f, 0.5f, 0.3f, 2f), out _inkMesh);
            _light = Vol2Fs.PointLight(root, "Light", new Color(0.9f, 0.95f, 1f), 9f); _light.transform.position = Apex + F * Depth * 0.5f + Vector3.up * 1.2f;
            Vol2FsB1.Emit(_sparks, _from, 40);
        }

        protected override void OnStruck(Struck s) { }

        protected override void OnSweep(float t, float a, bool ended)
        {
            var cam = CamPos;
            _streak.Clear(); _edge.Clear();
            foreach (var b in _blades)
            {
                float e = a - b.At; if (e < 0f) continue;
                float dist = Mathf.Min(b.Reach, e * Speed);
                Vector3 tip = b.From + b.Dir * dist;
                if (!b.Snapped)
                {
                    b.R.enabled = true;
                    float L = Mathf.Min(b.Len, dist + 0.05f);
                    b.T.localScale = new Vector3(2.6f, 2.6f, Mathf.Max(0.05f, L));
                    b.T.rotation = Quaternion.LookRotation(b.Dir, Vector3.up) * Quaternion.AngleAxis(b.Spin, Vector3.forward);
                    b.T.position = tip - b.Dir * L;
                    _edge.Strip(new[] { tip - b.Dir * L * 0.8f, tip - b.Dir * L * 0.3f, tip }, new[] { 0.04f, 0.08f, 0.03f }, null, Color.white, new[] { 1f, 1f, 1f }, b.Spin, cam, 0f, 1f);
                    if (dist >= b.Reach)
                    {
                        b.Snapped = true; b.R.enabled = false;          // snaps with a ring at the fan's edge: shards flung sideways
                        Vector3 side = Vector3.Cross(Vector3.up, b.Dir).normalized;
                        for (int k = 0; k < 5; k++) Vol2FsB1.EmitV(_shards, tip - b.Dir * Random.Range(0f, 0.8f), (side * (k % 2 == 0 ? 1f : -1f) * Random.Range(0.6f, 1.2f) + Vector3.up * Random.Range(-0.1f, 0.7f) + b.Dir * 0.3f).normalized * Random.Range(4f, 10f));
                        Vol2FsB1.Emit(_sparks, tip, 12); Vol2FsB1.Emit(_glints, tip, 2);
                        
                    }
                }
                // the streak left behind each blade (thin, bright, fading)
                float sl = 1f - Vol2Fs.Smooth(0.3f, 0.9f, e);
                if (sl > 0.01f && dist > 0.2f)
                {
                    var p = new[] { b.From + b.Dir * 0.3f, b.From + b.Dir * dist * 0.5f, tip };
                    _streak.Strip(p, new[] { 0.02f, 0.08f * sl, 0.05f }, null, Color.white, new[] { 0.2f * sl, sl, sl }, b.Spin * 0.01f, cam, 0f, 1f);
                }
            }
            _streak.Write(_streakMesh); _edge.Write(_edgeMesh);
            _light.intensity = a < OpenSeconds + 0.2f ? 7f : Mathf.Max(0f, 7f * (1f - (a - OpenSeconds - 0.2f) / 0.4f));
            // the struck bodies: cut lines across them, sparks and ink spatter, when the blades reach them
            _cut.Clear();
            foreach (var s in Hits)
            {
                Vector3 c = CentreNow(s);
                if (!s.Started && a >= s.At)
                {
                    s.Started = true;
                    Vector3 o = c - Apex; o.y = 0f; o.Normalize(); Vector3 side = Vector3.Cross(Vector3.up, o);
                    for (int k = 0; k < 70; k++) Vol2FsB1.EmitV(_sparks, c, (o * Random.Range(0.2f, 1f) + side * Random.Range(-1f, 1f) + Vector3.up * Random.Range(-0.3f, 0.8f)).normalized * Random.Range(6f, 16f));
                    Vol2FsB1.Emit(_drops, c, 30);
                    var list = Vol2FsB1.Burst(Seed + (int)s.Id, 5, side, o, 1.2f, 2.2f, 0.05f, 0.1f, 0.5f, 1.2f);
                    foreach (var st in list) { _spatter.Add(st); _spatterAt.Add(c); _spatterT.Add(a); }
                }
                if (s.Started)
                {
                    float e = a - s.At, cl = 1f - Vol2Fs.Smooth(0.08f, 0.45f, e), open = Vol2Fs.Smooth(0f, 0.05f, e);
                    if (cl > 0.01f)
                    {
                        Vector3 toCam = (cam - c).normalized, sx = Vector3.Cross(toCam, Vector3.up).normalized, sy = Vector3.Cross(sx, toCam).normalized;
                        for (int k = 0; k < 3; k++)
                        {
                            Vector3 d = (sx + sy * (0.25f * (k - 1) + 0.15f * Mathf.Sin(s.Id + k))).normalized; float L = (1.3f + 0.2f * k) * open;
                            Vector3 off = sy * 0.18f * (k - 1);
                            _cut.Strip(new[] { c + off - d * L, c + off, c + off + d * L }, new[] { 0.02f, 0.09f * cl, 0.02f }, null, Color.white, new[] { cl, cl, cl }, 0.3f + k * 0.2f, cam, 0f, 1f);
                        }
                    }
                }
            }
            _cut.Write(_cutMesh);
            _ink.Clear();
            var one = new List<Vol2FsB1.Stroke>(1);
            for (int k = 0; k < _spatter.Count; k++) { one.Clear(); one.Add(_spatter[k]); Vol2FsB1.DrawBurst(_ink, one, _spatterAt[k], a - _spatterT[k], cam, 0.3f, 0.07f); }
            _ink.Write(_inkMesh);
        }
    }
}
