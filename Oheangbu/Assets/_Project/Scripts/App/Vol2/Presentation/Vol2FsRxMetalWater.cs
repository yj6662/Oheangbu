using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>금>수 먹임 - free showcase batch 8, concept ma_b (steel rain): the steel shot shatters over the enemy's dark pool into a fan of fine needles that rain down
    /// into it; every needle that strikes the water turns into a bright drop with a splash, the dark pool is driven out and the patch is refilled with clear, bright
    /// water of the caster's own (청록 sheet, bright rim, rings running out, glints; black ink sweeps fan up behind). The child (water, 10 s) lies glittering;
    /// Expired = it sinks into the ground leaving a dark wet stain; Consumed / ChildConsumed = it dries in place 0.3 s.</summary>
    public sealed class Vol2FsRxMetalWater : Vol2FsReact
    {
        sealed class Needle { public Vector3 From, To; public float At; public bool Hit; }
        readonly List<Needle> _needles = new List<Needle>();
        ParticleSystem _splash, _drops, _spray, _glint, _mist; Light _light;
        Mesh _poolMesh, _tealMesh, _rimMesh, _needleMesh, _inkMesh, _wetMesh, _sealMesh, _sealGlowMesh;
        readonly Vol2FsStrips _pool = new Vol2FsStrips(), _teal = new Vol2FsStrips(), _rim = new Vol2FsStrips(), _needle = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _wet = new Vol2FsStrips(), _seal = new Vol2FsStrips(), _sealGlow = new Vol2FsStrips();
        List<Vol2FsB1.Stroke> _burst;
        bool _mine; float _lifeS = 10f;
        protected override float Burst => 2.2f;

        protected override void OnBuild()
        {
            _mine = !EnemyIgniter; _lifeS = V.ChildLifeUs > 0 ? V.ChildLifeUs / 1e6f : 10f;
            var root = Root.transform;
            var wet = M("fs_wet", new Color(0.1f, 0.12f, 0.15f, 1f), 1f, 0f); wet.Opacity = 0.65f; wet.NoiseAmt = 0.3f; wet.Soft = 0.25f;
            Vol2Fs.MeshObject(root, "WetStain", Kit.Mat(wet), out _wetMesh);
            Vol2Fs.MeshObject(root, "Pool", _mine ? Vol2FsB1.Water(Kit, 1.5f, 0.35f) : Kit.Mat(M("fs_water", new Color(0.18f, 0.2f, 0.28f), 0.8f, 0.1f)), out _poolMesh);
            // r2: a teal light layer over the clear sheet (r1 read as a grey disc on the red ground) and a white-teal foam rim
            var teal = M("fs_swirl", new Color(0.18f, 0.72f, 0.82f), _mine ? 1.7f : 0.5f, 0.85f); teal.NoiseAmt = 0.35f; teal.Soft = 0.3f; teal.Scroll = new Vector2(0.3f, 0.1f);
            Vol2Fs.MeshObject(root, "Teal", Kit.Mat(teal), out _tealMesh);
            var rim = M("fs_ring", new Color(0.8f, 1f, 1f), _mine ? 2.6f : 0.9f, 0.75f); rim.NoiseAmt = 0.25f; rim.Soft = 0.1f;
            Vol2Fs.MeshObject(root, "Rim", Kit.Mat(rim), out _rimMesh);
            var nd = M("fs_streak", new Color(1f, 0.95f, 0.85f), 4f, 1f); nd.NoiseAmt = 0.1f; nd.Soft = 0.02f; nd.Erode = 0.08f;
            Vol2Fs.MeshObject(root, "Needles", Kit.Mat(nd), out _needleMesh);
            Vol2Fs.MeshObject(root, "Ink", Vol2FsRxFeedKit.InkMat(Kit), out _inkMesh);
            Vol2Fs.MeshObject(root, "Seal", Vol2FsRxFeedKit.InkMat(Kit), out _sealMesh);
            Vol2Fs.MeshObject(root, "SealGlow", Vol2FsRxFeedKit.GlowMat(Kit, _mine ? new Color(0.4f, 0.8f, 1f) : new Color(0.3f, 0.3f, 0.45f), 2.2f), out _sealGlowMesh);
            if (!HasChild) return;
            // the needles: from a burst point above the near edge of the patch, a fan of 34 that rain into the pool
            Vector3 top = P - Dir * Radius * 0.4f + Vector3.up * 3.4f;
            for (int k = 0; k < 44; k++)
            {
                float a = Vol2Fs.Hash(Seed, k) * Mathf.PI * 2f, r = Radius * Mathf.Sqrt(Mathf.Max(0f, Vol2Fs.Hash(Seed, k + 9))) * 1.9f;
                Vector3 to = G(P + (Dir * Mathf.Cos(a) + Side * Mathf.Sin(a)) * r) + Vector3.up * 0.02f;
                _needles.Add(new Needle { From = top + Random.insideUnitSphere * 0.5f, To = to, At = 0.06f + 0.22f * Vol2Fs.Hash(Seed, k + 30) });
            }
            _splash = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Water(Kit, 1.5f, 0.4f, "fs_splash"), Life0 = 0.35f, Life1 = 0.7f, Speed0 = 1.5f, Speed1 = 4f, Size0 = 0.12f, Size1 = 0.3f, Gravity = 1.8f, Drag = 0.6f, Max = 600 }, root, "Splash");
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.6f, 0.9f, 1f), 3f, 0.8f)), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 1.5f, Speed1 = 4.5f, Size0 = 0.025f, Size1 = 0.06f, Gravity = 2.4f, Max = 600 }, root, "Drops");
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.9f, 0.97f, 1f), 4f, 1f)), Life0 = 0.25f, Life1 = 0.5f, Speed0 = 5f, Speed1 = 12f, Size0 = 0.02f, Size1 = 0.04f, Gravity = 1.6f, Drag = 0.7f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.3f, Max = 400 }, root, "SteelSparks");
            _glint = Vol2FsRxFeedKit.At(Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.85f, 0.97f, 1f), 3.5f, 1f)), Loop = true, Rate = 0f, Life0 = 0.12f, Life1 = 0.3f, Speed0 = 0f, Speed1 = 0.05f, Size0 = 0.08f, Size1 = 0.2f, Radius = Radius * 1.8f, ShapeScale = new Vector3(1f, 0.02f, 1f), SizeStart = 0.2f, SizePeak = 1f, SizePeakAt = 0.4f, SizeEnd = 0f, Max = 60 }, root, "Glints"), P + Vector3.up * 0.06f);
            var mist = M("fs_puff", new Color(0.45f, 0.75f, 0.8f, 1f), 1f, 0.2f); mist.Opacity = 0.25f; mist.NoiseAmt = 0.6f; mist.Erode = 0.35f;
            _mist = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mist), Life0 = 0.8f, Life1 = 1.4f, Speed0 = 0.4f, Speed1 = 1.4f, Size0 = 0.5f, Size1 = 1f, SizeStart = 0.6f, SizePeak = 1f, SizeEnd = 1.6f, Drag = 1.5f, Max = 80 }, root, "Mist");
            _glint.Play(false);
            _light = Vol2Fs.PointLight(root, "WaterLight", new Color(0.4f, 0.85f, 0.95f), 9f);
            _light.transform.position = P + Vector3.up * 1.2f;
            _burst = Vol2FsB1.Burst(Seed, 9, Side, Dir, 2.2f, 4.2f, 0.08f, 0.2f, 0.95f, 0.2f);      // r3: thinner (r2 a heavy black star)
            for (int k = 0; k < 80; k++) Vol2FsB1.EmitV(_spray, top, (Random.onUnitSphere + Vector3.up * 0.3f).normalized * Random.Range(4f, 11f));
            HitStop(0f, 0.06f);
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            if (!HasChild) { if (Patch != null) Vol2FsRxFeedKit.Consume(Patch, t, 0f, Mathf.Max(0.1f, DryRel), 0.2f); return; }
            bool dried = ChildWhy == Vol2MaterialEnd.Consumed || ChildWhy == Vol2MaterialEnd.ChildConsumed;
            float outK = Vol2FsRxFeedKit.Out(t, ChildEndAt, dried ? 0.3f : 1.4f);
            Vol2FsRxFeedKit.Consume(Patch, t, 0.1f, 0.45f, 0.4f);
            // the needles fall (each 0.1 s), strike: a splash and a drop
            _needle.Clear();
            int hits = 0;
            foreach (var n in _needles)
            {
                float u = (t - n.At) / 0.12f;
                if (u < 0f) continue;
                if (u >= 1f)
                {
                    hits++;
                    if (!n.Hit)
                    {
                        n.Hit = true;
                        for (int k = 0; k < 8; k++) Vol2FsB1.EmitV(_splash, n.To + Vector3.up * 0.05f, (Random.insideUnitSphere * 0.6f + Vector3.up).normalized * Random.Range(2f, 5f));
                        for (int k = 0; k < 6; k++) Vol2FsB1.EmitV(_drops, n.To + Vector3.up * 0.05f, (Random.insideUnitSphere * 0.8f + Vector3.up).normalized * Random.Range(1.5f, 4.5f));
                    }
                    continue;
                }
                Vector3 head = Vector3.Lerp(n.From, n.To, u), tail = Vector3.Lerp(n.From, n.To, Mathf.Max(0f, u - 0.6f));
                _needle.Strip(new[] { tail, head }, new[] { 0.025f, 0.085f }, null, Color.white, new[] { 0.6f, 1f }, Vol2Fs.Hash(Seed, hits), cam);
            }
            _needle.Write(_needleMesh);
            if (hits == _needles.Count && hits > 0 && _mist.particleCount == 0 && t < 0.6f) { HitStop(t, 0.06f); for (int k = 0; k < 8; k++) Vol2FsB1.EmitV(_mist, P + Random.insideUnitSphere * Radius * 0.6f + Vector3.up * 0.2f, Random.insideUnitSphere + Vector3.up * 0.6f); }
            // the clear pool fills from the strikes (0.1 - 0.55 s), lies bright, sinks at the end
            float fill = Vol2Fs.Smooth(0.1f, 0.55f, t);
            float sink = dried ? 0f : 1f - outK;
            float dry = dried ? 1f - outK : 0f;
            float pr = Radius * 2.0f * fill * (1f - 0.35f * sink) * (1f - dry);
            _pool.Clear(); _teal.Clear(); _rim.Clear(); _wet.Clear();
            float pl = (1f - sink) * (1f - dry);
            if (pr > 0.05f && pl > 0.01f)
            {
                for (int k = 0; k < 4; k++) Vol2FsB4A.GroundArc(_pool, P, Ground + 0.02f + 0.002f * k, pr * (0.14f + 0.25f * k), pr * 0.3f, Seed * 0.2f + t * (0.25f + 0.1f * k), Mathf.PI * 2f, pl * 0.95f, 0.2f * k, cam, 40);
                for (int k = 0; k < 3; k++) Vol2FsB4A.GroundArc(_teal, P, Ground + 0.03f, pr * (0.2f + 0.32f * k), pr * 0.36f, -Seed * 0.3f - t * (0.4f + 0.15f * k), Mathf.PI * 2f, pl * 0.9f, 0.4f + 0.2f * k, cam, 44);
                Vol2FsB4A.GroundArc(_rim, P, Ground + 0.035f, pr, 0.16f, 0f, Mathf.PI * 2f, pl, 0.5f, cam, 56);
                // rings run out from the middle (one every 0.7 s) - the water moves
                for (int k = 0; k < 2; k++)
                {
                    float ph = Mathf.Repeat(t * 1.4f + k * 0.5f, 1f);
                    Vol2FsB4A.GroundArc(_rim, P, Ground + 0.04f, pr * (0.15f + 0.8f * ph), 0.07f, k * 1.3f, Mathf.PI * 2f, pl * (1f - ph) * 0.8f, 0.3f + k * 0.2f, cam, 40);
                }
            }
            if (sink > 0.01f) Vol2FsB4A.GroundArc(_wet, P, Ground + 0.015f, Radius * 1.0f, Radius * 2.0f, 0f, Mathf.PI * 2f, Mathf.Min(1f, sink * 3f) * (1f - Vol2Fs.Smooth(0.6f, 1f, sink)), 0.4f, cam, 40);
            _pool.Write(_poolMesh); _teal.Write(_tealMesh); _rim.Write(_rimMesh); _wet.Write(_wetMesh);
            Vol2FsRxFeedKit.Rate(_glint, (_mine ? 40f : 4f) * fill * pl);
            if (_light != null) _light.intensity = (_mine ? 9f : 2f) * fill * (0.5f + 0.5f * (1f - Vol2Fs.Smooth(0.6f, 2f, t))) * pl;
            // the ink fans up behind the rain
            _ink.Clear();
            float h = t - 0.12f;
            if (h > 0f && h < 2f) Vol2FsB1.DrawBurst(_ink, _burst, P + Vector3.up * 0.5f + Dir * 0.3f, h, cam, 0.25f, 0.12f);
            _ink.Write(_inkMesh);
            _seal.Clear(); _sealGlow.Clear();
            Vol2FsRxFeedKit.Seal(_seal, _sealGlow, P, Ground, Radius * 2.25f, Vol2Fs.Smooth(0.35f, 0.8f, t), Vol2FsRxFeedKit.Left(t, ChildExpireRel, _lifeS), outK, cam, Seed * 0.1f);
            _seal.Write(_sealMesh); _sealGlow.Write(_sealGlowMesh);
        }

        protected override void OnChildEnd(Vol2MaterialEnd why, float t) { Note += " | water child " + (why == Vol2MaterialEnd.Expired ? "sank into the ground" : "dried in place"); }
    }
}
