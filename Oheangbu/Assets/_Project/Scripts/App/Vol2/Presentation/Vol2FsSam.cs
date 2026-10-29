using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>삼 (metal, ㅏ path, 심는다) - free showcase batch 5, concept sam_b (a small steel blade hangs point-down over every bear the path passed, slowly
    /// turning like a sword of judgement; over the first bear it drops like lightning and cuts the bear with a bright X-shaped slash, sparks and shards flying
    /// sideways, while the blades over the farther bears still hang and turn). Executor facts only: "Plant" = the blade appears with a glint -> the wait (it turns,
    /// a glint runs down it on every heartbeat, a hair-thin light thread hangs from its point to the body, the ink dial burns down; the last 0.4 s it trembles)
    /// -> Consumed = the drop and the X cut (white-gold slash flare, a fountain of sparks, steel shards sideways, cut ink threads) and the blade snaps
    /// (CSV 끝: 쨍하게 끊기며 파편이 옆으로 튐); Expired = the blade snaps where it hangs, shards sideways.</summary>
    public sealed class Vol2FsSam : Vol2FsSeedPath
    {
        static readonly Color Gold = new Color(1f, 0.88f, 0.6f);
        ParticleSystem _sparks, _shards, _glints, _threads;
        Material _steel;
        Mesh _xMesh, _lineMesh, _inkXMesh; readonly Vol2FsStrips _xS = new Vol2FsStrips(), _lineS = new Vol2FsStrips(), _inkX = new Vol2FsStrips();
        protected override float MarkLift => 1.25f;
        protected override float DialR => 0.34f;
        protected override float FireSeconds => 1.2f;
        protected override float ExpireSeconds => 0.8f;

        sealed class Look { public Transform Blade; public Light Glint; public float Spin; public Vector3 Cut, Drop0; public float SnapAt = -1f; }

        protected override void OnSetup()
        {
            var root = Root.transform;
            _steel = Vol2FsB1.Lit(Kit, "fs_steel", Color.white, 1f, 0.9f, new Color(0.05f, 0.06f, 0.08f));
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 5f, 1f)), Life0 = 0.25f, Life1 = 0.7f, Speed0 = 5f, Speed1 = 14f, Size0 = 0.02f, Size1 = 0.05f, Radius = 0.1f, Gravity = 1.4f, Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.5f, Max = 800 }, root, "Sparks");
            var shard = Kit.SharedMesh("shard0", () => Vol2FsB1.Shard(5));
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = _steel, Mesh = shard, Life0 = 0.6f, Life1 = 1.1f, Speed0 = 4f, Speed1 = 9f, Size0 = 0.06f, Size1 = 0.16f, Radius = 0.1f, Gravity = 2f, Drag = 0.4f, Spin = 900f, Max = 200 }, root, "Shards");
            _glints = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Color.white, 4f, 1f)), Life0 = 0.08f, Life1 = 0.14f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.25f, Size1 = 0.4f, Max = 60 }, root, "Glints");
            var thr = M("fs_stroke2", InkC, 1f, 0f); thr.NoiseAmt = 0.3f; thr.Soft = 0.05f;
            _threads = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(thr), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 3f, Speed1 = 7f, Size0 = 0.05f, Size1 = 0.1f, Radius = 0.1f, Drag = 2f, Gravity = 0.6f, Stretch = true, StretchVel = 0.08f, StretchLen = 2f, Max = 120 }, root, "InkThreads");
            foreach (var ps in new[] { _sparks, _shards, _glints, _threads }) ps.Play(false);
            var x = M("fs_streak", Gold, 4f, 1f); x.NoiseAmt = 0.15f; x.Soft = 0.02f; x.Erode = 0.1f;
            Vol2Fs.MeshObject(root, "InkX", Kit.InkSolid("fs_stroke2", InkC, new Color(1.4f, 1.2f, 0.8f, 1f), 0.08f, 0.4f, 0.3f, 1.3f), out _inkXMesh);
            Vol2Fs.MeshObject(root, "XCut", Kit.Mat(x), out _xMesh);
            var line = M("fs_streak", Gold, 2.2f, 1f); line.NoiseAmt = 0.1f; line.Soft = 0.02f;
            Vol2Fs.MeshObject(root, "Threads", Kit.Mat(line), out _lineMesh);
        }

        Look L(Pod p) => p.Data as Look;

        protected override void Planted(Pod p)
        {
            var l = new Look(); p.Data = l;
            var r = Vol2FsB1.Prop(Root.transform, "Blade" + p.Index, Kit.SharedMesh("samSword", () => Vol2FsB1.Sword(0.8f, 0.09f, 0.02f)), _steel, false);
            l.Blade = r.transform; l.Blade.localScale = Vector3.one * 0.01f; l.Spin = Vol2Fs.Hash(Seed, p.Index + 3) * 360f;
            l.Glint = Vol2Fs.PointLight(Root.transform, "Glint" + p.Index, Gold, 2f);
            Vol2FsB1.EmitV(_glints, MarkPoint(p), Vector3.zero, 0.6f, 0.15f);
        }

        /// <summary>Point-down: the sword mesh runs along +Z from its grip, so the grip is up and the point down.</summary>
        static Quaternion Down(float spin) => Quaternion.Euler(0f, spin, 0f) * Quaternion.LookRotation(Vector3.down, Vector3.forward);
        Vector3 BladeTop(Pod p) => MarkPoint(p) + Vector3.up * 0.3f;

        protected override void OnFire(Pod p)
        {
            var l = L(p); if (l == null) return;
            var hb = HostNow(p); l.Drop0 = BladeTop(p); l.Cut = hb.center + FlatN(CamPos - hb.center) * (Mathf.Max(hb.extents.x, hb.extents.z) + 0.35f) + Vector3.up * 0.15f;      // r3: the X in front of the body (r2: its lower half went into the bear)
        }

        void Snap(Look l, Vector3 at, int shards)
        {
            l.SnapAt = 0f; l.Blade.gameObject.SetActive(false);
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(CamPos - at));
            for (int k = 0; k < shards; k++) Vol2FsB1.EmitV(_shards, at + Random.insideUnitSphere * 0.15f, (side * (k % 2 == 0 ? 1f : -1f) * Random.Range(3f, 8f)) + Vector3.up * Random.Range(0.5f, 3f) + Random.insideUnitSphere, -1f, -1f);
            for (int k = 0; k < 60; k++) Vol2FsB1.EmitV(_sparks, at, (side * (k % 2 == 0 ? 1f : -1f) + Random.insideUnitSphere * 0.6f).normalized * Random.Range(4f, 12f), -1f, -1f);
            Vol2FsB1.EmitV(_glints, at, Vector3.zero, 0.5f, 0.1f);
        }

        protected override void OnPodEnd(Pod p)
        {
            var l = L(p); if (l == null || Worked(p)) return;
            Snap(l, l.Blade.position + Vector3.down * 0.2f, Withered(p) ? 14 : 6);
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos; _xS.Clear(); _lineS.Clear(); _inkX.Clear();
            foreach (var p in Pods)
            {
                var l = L(p); if (l == null) continue;
                if (!Worked(p))
                {
                    if (p.Ended) { l.Glint.intensity = 0f; continue; }
                    float life = PodLife(p, t, 0.25f), beat = Beat(p), warn = PodWarn(p, t);
                    l.Spin += Time.deltaTime * (40f + 160f * warn) * (1f - Held01);
                    Vector3 top = BladeTop(p) + Vector3.up * 0.04f * Mathf.Sin(t * 2.1f + p.Index);
                    l.Blade.SetPositionAndRotation(top + (warn > 0f ? Random.insideUnitSphere * 0.02f * warn : Vector3.zero), Down(l.Spin));
                    l.Blade.localScale = Vector3.one * Mathf.Max(0.01f, life * 1.2f);
                    l.Blade.gameObject.SetActive(p.Started);
                    // a glint runs down the blade on each beat; the hair-thin thread from the point to the body
                    float ph = p.Phase - Mathf.Floor(p.Phase);
                    if (ph < 0.25f && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_glints, Vector3.Lerp(top, top + Vector3.down * 0.9f, ph * 4f), Vector3.zero, 0.22f, 0.1f);
                    Vector3 tip = top + Vector3.down * 1.4f, body = HostTop(p);
                    _lineS.Strip(new[] { tip, body }, new[] { 0.012f, 0.004f }, null, Color.white, new[] { life * (0.35f + 0.4f * beat + 0.4f * warn), 0f }, 0.5f + p.Index * 0.1f, cam);
                    l.Glint.transform.position = top + Vector3.down * 0.4f; l.Glint.intensity = life * (0.4f + 1.2f * beat + warn) * (1f - 0.6f * Held01);
                    continue;
                }
                float s = SinceFire(p, t);
                // the drop: 0.07 s from where it hung through the body
                if (l.SnapAt < 0f)
                {
                    float d = Vol2Fs.Smooth(0f, 0.07f, s);
                    l.Blade.SetPositionAndRotation(Vector3.Lerp(l.Drop0, l.Cut + Vector3.up * 0.9f, d), Down(l.Spin));
                    l.Blade.localScale = Vector3.one * 1.2f;
                    if (s >= 0.07f)
                    {
                        HitStop(t, 0.08f);
                        Snap(l, l.Cut, 34);
                        for (int k = 0; k < 160; k++) Vol2FsB1.EmitV(_sparks, l.Cut, (Random.insideUnitSphere + Vector3.up * 0.6f).normalized * Random.Range(5f, 14f), -1f, -1f);
                        for (int k = 0; k < 10; k++) Vol2FsB1.EmitV(_threads, l.Cut, Random.onUnitSphere * Random.Range(3f, 7f), -1f, -1f);
                        l.Glint.intensity = 7f; l.Glint.transform.position = l.Cut;
                    }
                }
                // the X: two bright crossing strokes facing the camera, thin-to-wide, flaring then thinning away
                float xa = s - 0.07f;
                if (xa > 0f && xa < 0.6f)
                {
                    float life = 1f - Vol2Fs.Smooth(0.12f, 0.6f, xa), open = Vol2Fs.Smooth(0f, 0.05f, xa);
                    Vector3 view = (cam - l.Cut).normalized, right = Vector3.Cross(Vector3.up, view).normalized, up = Vector3.Cross(view, right);
                    for (int k = 0; k < 2; k++)
                    {
                        Vector3 dir = (right * (k == 0 ? 1f : -1f) + up * 1.15f).normalized;
                        Vector3 a = l.Cut - dir * 2.1f * open, b = l.Cut + dir * 2.1f * open;
                        _inkX.Strip(new[] { a - view * 0.05f, l.Cut - view * 0.05f, b - view * 0.05f }, new[] { 0.06f, 0.42f, 0.06f }, null, Color.white, new[] { life, life, life }, 0.7f + k * 0.2f, cam);
                        _xS.Strip(new[] { a, l.Cut, b }, new[] { 0.03f, 0.22f * (1f - 0.5f * xa), 0.03f }, null, Color.white, new[] { life, life, life }, 0.3f + k * 0.4f, cam);
                    }
                    l.Glint.intensity = 7f * life;
                }
            }
            _xS.Write(_xMesh); _lineS.Write(_lineMesh); _inkX.Write(_inkXMesh);
        }
    }
}
