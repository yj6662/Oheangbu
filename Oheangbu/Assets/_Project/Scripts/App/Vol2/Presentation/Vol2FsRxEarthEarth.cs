using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>토>토 비화 - free showcase batch 8, concept ee_b (stacked strata): the earth shape slides into the enemy's stone ring and a new stepped tier of
    /// flat stone plates grinds up out of the ground round it - an inner terrace (~0.9 m) and a wider, lower outer terrace (to ~1.7 m out), dark violet-black
    /// ink in the seams between the plates, dust jetting from the seams, grit raining down. It stays the enemy's stone. It stands for the thickened patch's
    /// renewed life (a dust trickle off its edges toward the end); Expired = the terraces sink back into the ground in dust; any other end = they drop at once.</summary>
    public sealed class Vol2FsRxEarthEarth : Vol2FsReact
    {
        sealed class Plate { public Transform T; public Vector3 Rest; public float H, At; }
        readonly List<Plate> _plates = new List<Plate>();
        readonly Vol2FsStrips _seams = new Vol2FsStrips();
        Mesh _seamMesh, _hotMesh; readonly Vol2FsStrips _hot = new Vol2FsStrips(); ParticleSystem _dust, _grit, _trickle, _shards; Vol2FsRxSameFx _fx; bool _sank;

        protected override void OnBuild()
        {
            _fx = new Vol2FsRxSameFx(Kit, Root.transform, AccA, 4f);
            // r2: lighter stone with faint violet ink veins (r1: dark plates vanished against the ground and the bear)
            var stone = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.78f, 0.68f, 0.62f), 0f, 0.12f, new Color(0.3f, 0.14f, 0.42f), "fs_rock_crack");
            void Ring(int n, float r0, float r1, float h0, float h1, float w, float depth, float at0, int salt)
            {
                for (int k = 0; k < n; k++)
                {
                    float a = (k / (float)n + 0.04f * Vol2Fs.Hash(Seed, k + salt)) * Mathf.PI * 2f, r = Mathf.Lerp(r0, r1, Vol2Fs.Hash(Seed, k + salt + 10));
                    float h = Mathf.Lerp(h0, h1, Vol2Fs.Hash(Seed, k + salt + 20));
                    Vector3 o = Dir * Mathf.Cos(a) + Side * Mathf.Sin(a);
                    int sk = (Seed + k * 7 + salt) % 17;
                    var mesh = Kit.SharedMesh("b8sameplate" + sk, () => Vol2FsB1.Slab(sk * 3 + 101, Vector3.one, 0.96f));
                    var pr = Vol2FsB1.Prop(Root.transform, "Plate" + salt + "_" + k, mesh, stone);
                    pr.transform.rotation = Quaternion.LookRotation(o, Vector3.up) * Quaternion.Euler(-4f - 5f * Vol2Fs.Hash(Seed, k + salt + 30), 0f, (Vol2Fs.Hash(Seed, k + salt + 40) - 0.5f) * 8f);
                    pr.transform.localScale = new Vector3(w * (0.85f + 0.3f * Vol2Fs.Hash(Seed, k + salt + 50)), h, depth);
                    var rest = G(P + o * r);
                    pr.transform.position = rest + Vector3.down * (h + 0.1f);
                    _plates.Add(new Plate { T = pr.transform, Rest = rest, H = h, At = at0 + 0.006f * k });
                }
            }
            Ring(7, Radius * 1.15f, Radius * 1.4f, 1.15f, 1.45f, 1.25f, 0.8f, 0f, 0);              // the inner, higher terrace (r2: x1.7)
            Ring(12, Radius * 2.35f, Radius * 2.8f, 0.6f, 0.8f, 1.5f, 1.0f, 0.05f, 100);           // the outer, lower, wider terrace
            var seam = M("fs_ink", new Color(0.12f, 0.05f, 0.16f, 1f), 1f, 0f); seam.NoiseAmt = 0.45f; seam.Erode = 0.25f; seam.Soft = 0.15f;
            Vol2Fs.MeshObject(Root.transform, "SameSeams", Kit.Mat(seam), out _seamMesh);
            // r3: the seams light up amber as the strata grind up (0 - 1.2 s) - the stone's own moment, over the acting glyph's dust
            Vol2Fs.MeshObject(Root.transform, "SameSeamsLit", Vol2FsB4A.PhantomMat(Kit, AccA, 3.4f), out _hotMesh);
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.78f, 0.68f, 0.62f), 0f, 0.12f), Mesh = Kit.SharedMesh("b8sameshard", () => Vol2FsB1.Slab(77, new Vector3(1f, 0.35f, 0.7f), 0.9f)), Life0 = 0.6f, Life1 = 1.1f, Speed0 = 3f, Speed1 = 6f, Size0 = 0.12f, Size1 = 0.3f, Gravity = 2.4f, Drag = 0.2f, Spin = 400f, Max = 80 }, Root.transform, "SameShards");
            _shards.Play(false);
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.6f, 0.47f, 0.36f, 1f), 0.32f)), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 0.6f, Speed1 = 3f, Size0 = 0.3f, Size1 = 0.6f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.8f, Drag = 1.4f, Max = 260 }, Root.transform, "SameDust");
            _grit = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.5f, 0.42f, 0.38f), 0f, 0.1f), Mesh = Kit.SharedMesh("actRock", () => Vol2FsB1.Rock(17, 0.5f)), Life0 = 0.7f, Life1 = 1.3f, Speed0 = 2f, Speed1 = 4.5f, Size0 = 0.04f, Size1 = 0.11f, Gravity = 2.2f, Drag = 0.3f, Spin = 500f, Max = 200 }, Root.transform, "SameGrit");
            _trickle = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.5f, 0.4f, 0.32f, 1f), 0.35f)), Life0 = 1.0f, Life1 = 1.8f, Speed0 = 0.1f, Speed1 = 0.4f, Size0 = 0.2f, Size1 = 0.4f, SizeEnd = 1.6f, Gravity = 0.15f, Max = 80 }, Root.transform, "SameTrickle");
            foreach (var ps in new[] { _dust, _grit, _trickle }) ps.Play(false);
            // the merge: dust jets out of the seams as the stone grinds up, grit is thrown
            // r2: the dust stays low and outward along the seams (r1: one brown puff hid the stone)
            for (int k = 0; k < 60; k++) { float a = Random.value * Mathf.PI * 2f; Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); float r = Radius * (Random.value < 0.5f ? 1.75f : 3.2f); Vol2FsB1.EmitV(_dust, G(P + o * r) + Vector3.up * 0.1f, o * Random.Range(1.5f, 3.5f) + Vector3.up * Random.Range(0.1f, 0.6f), -1f, -1f); }
            for (int k = 0; k < 36; k++) { float a = k / 36f * Mathf.PI * 2f; Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); Vol2FsB1.EmitV(_shards, G(P + o * Radius * 1.5f) + Vector3.up * 0.4f, o * Random.Range(3f, 6f) + Vector3.up * Random.Range(1.5f, 3f), -1f, -1f); }
            for (int k = 0; k < 60; k++) Vol2FsB1.EmitV(_grit, G(P) + Vector3.up * 0.6f + Random.insideUnitSphere * Radius * 2f, Random.insideUnitSphere * 2.5f + Vector3.up * Random.Range(2f, 5f), -1f, -1f);
            HitStop(0f, 0.07f);
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            float pop = Vol2FsRxSameFx.Pop(t, 0.2f), exit = Vol2FsRxSameFx.Exit(t, ContactEndAt, ContactWhy, 1.1f), near = Vol2FsRxSameFx.Near(t, ContactExpireRel, 2f);
            foreach (var pl in _plates)
            {
                if (pl.T == null) continue;
                float up = Vol2FsRxSameFx.Pop(t - pl.At, 0.14f);
                float rise = Mathf.Min(up, 1.08f) - exit * 1.05f;
                // a grinding shudder while it rises
                float shake = t < 0.3f ? 0.025f * Mathf.Sin(t * 90f + pl.At * 50f) * (1f - Vol2Fs.Smooth(0.15f, 0.3f, t)) : 0f;
                pl.T.position = pl.Rest + Vector3.down * ((1f - rise) * (pl.H + 0.1f)) + Dir * shake;
            }
            if (Patch != null) Patch.Pose((1f + 0.5f * pop) * (1f - 0.9f * exit), exit * Patch.Height, 1f + 0.2f * pop);
            if (exit > 0f && !_sank)
            {
                _sank = true;
                int n = ContactWhy == Vol2MaterialEnd.Expired ? 60 : 25;
                for (int k = 0; k < n; k++) { float a = Random.value * Mathf.PI * 2f; Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); Vol2FsB1.EmitV(_dust, G(P + o * Radius * Random.Range(1f, 3f)) + Vector3.up * 0.2f, o * Random.Range(0.5f, 1.5f) + Vector3.up * 0.5f, -1f, -1f); }
            }
            if (exit <= 0f && t > 0.5f && Random.value < 0.05f + 0.35f * near && _plates.Count > 0)
            {
                var pl = _plates[Random.Range(0, _plates.Count)];
                if (pl.T != null) Vol2FsB1.EmitV(_trickle, pl.T.position + Vector3.up * pl.H * 0.9f, Vector3.down * 0.2f, -1f, -1f);
            }
            // the seams: dark ink between the terraces (and the plates), revealed as they rise
            _seams.Clear();
            float life = Mathf.Clamp01(pop) * (1f - exit);
            if (life > 0.01f)
            {
                Vol2FsB4A.GroundArc(_seams, P, Ground + 0.01f, Radius * 1.75f, 0.32f, 0f, Mathf.PI * 2f, life, 0.3f, cam, 56);
                Vol2FsB4A.GroundArc(_seams, P, Ground + 0.01f, Radius * 3.2f, 0.26f, 0.3f, Mathf.PI * 2f, life * 0.8f, 0.6f, cam, 64);
                for (int k = 0; k < 7; k++)
                {
                    float a = (k / 7f + 0.1f) * Mathf.PI * 2f; Vector3 o = Dir * Mathf.Cos(a) + Side * Mathf.Sin(a);
                    Vol2FsB6.Crack(_seams, P + o * Radius * 0.9f, P + o * Radius * 3.6f, Ground, 0.17f, 0f, Mathf.Clamp01(pop), life, 0.2f + 0.1f * k, Seed + k, 0.1f, 0.015f);
                }
            }
            _seams.Write(_seamMesh);
            _hot.Clear();
            float hot = Vol2Fs.Smooth(0f, 0.08f, t) * (1f - Vol2Fs.Smooth(0.5f, 1.3f, t)) * (1f - exit);
            if (hot > 0.01f)
            {
                Vol2FsB4A.GroundArc(_hot, P, Ground + 0.03f, Radius * 1.75f, 0.14f, 0f, Mathf.PI * 2f, hot, 0.2f, cam, 56);
                Vol2FsB4A.GroundArc(_hot, P, Ground + 0.03f, Radius * 3.2f, 0.11f, 0.3f, Mathf.PI * 2f, hot * 0.85f, 0.5f, cam, 64);
                for (int k = 0; k < 7; k++)
                {
                    float a = (k / 7f + 0.1f) * Mathf.PI * 2f; Vector3 o = Dir * Mathf.Cos(a) + Side * Mathf.Sin(a);
                    Vol2FsB6.Crack(_hot, P + o * Radius * 0.9f, P + o * Radius * 3.6f, Ground, 0.07f, 0f, Mathf.Clamp01(pop), hot, 0.3f + 0.1f * k, Seed + k, 0.1f, 0.03f);
                }
            }
            _hot.Write(_hotMesh);
            _fx.Draw(t, P, Dir, Ground, Radius, Radius * 3f, cam, 1.5f);
        }
    }
}
