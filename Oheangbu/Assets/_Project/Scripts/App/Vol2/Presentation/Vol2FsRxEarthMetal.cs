using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>토>금 먹임 - free showcase batch 8, concept em_b (ore bloom): the earth shot pours over the enemy's dull blades and hardens into a crust of ore; the crust
    /// splits with a gold flash and a crown of bright new steel crystals of the caster's own bursts up out of it (a geode of blades, tallest in the middle, white-gold
    /// light, black ink sweeps). The child (metal, 10 s) stands glinting; Expired = the crystals snap with shards flying to the side and sink; Consumed /
    /// ChildConsumed = they dull and shrink in place 0.3 s.</summary>
    public sealed class Vol2FsRxEarthMetal : Vol2FsReact
    {
        sealed class Crystal { public MeshRenderer R; public Vector3 At, Lean; public float H, W, Delay, Spin; }
        readonly List<Crystal> _cr = new List<Crystal>();
        readonly List<MeshRenderer> _crust = new List<MeshRenderer>(); readonly List<Vector3> _crustAt = new List<Vector3>();
        ParticleSystem _rocks, _dust, _sparks, _glint, _shards; Light _light;
        Mesh _inkMesh, _sealMesh, _sealGlowMesh, _flashMesh;
        readonly Vol2FsStrips _ink = new Vol2FsStrips(), _seal = new Vol2FsStrips(), _sealGlow = new Vol2FsStrips(), _flash = new Vol2FsStrips();
        List<Vol2FsB1.Stroke> _burst;
        bool _mine, _split, _snapped; float _lifeS = 10f;
        protected override float Burst => 2.2f;

        protected override void OnBuild()
        {
            _mine = !EnemyIgniter; _lifeS = V.ChildLifeUs > 0 ? V.ChildLifeUs / 1e6f : 10f;
            var root = Root.transform;
            Vol2Fs.MeshObject(root, "Ink", Vol2FsRxFeedKit.InkMat(Kit), out _inkMesh);
            Vol2Fs.MeshObject(root, "Seal", Vol2FsRxFeedKit.InkMat(Kit), out _sealMesh);
            Vol2Fs.MeshObject(root, "SealGlow", Vol2FsRxFeedKit.GlowMat(Kit, _mine ? new Color(1f, 0.86f, 0.55f) : new Color(0.45f, 0.4f, 0.5f), 2.2f), out _sealGlowMesh);
            Vol2Fs.MeshObject(root, "Flash", Vol2FsRxFeedKit.GlowMat(Kit, new Color(1f, 0.85f, 0.5f), 3.2f), out _flashMesh);
            if (!HasChild) return;
            // the ore crust: rough lumps poured over the patch
            var crustMat = Vol2FsB1.Lit(Kit, "fs_rock_crack", new Color(0.46f, 0.38f, 0.3f), 0f, 0.15f, new Color(1.6f, 1.1f, 0.4f), "fs_rock_crack");
            for (int k = 0; k < 7; k++)
            {
                float a = k * 0.9f + Vol2Fs.Hash(Seed, k) * 0.6f, r = Radius * (k == 0 ? 0f : 0.5f + 0.7f * Vol2Fs.Hash(Seed, k + 5));
                Vector3 at = G(P + (Dir * Mathf.Cos(a) + Side * Mathf.Sin(a)) * r);
                var lump = Vol2FsB1.Prop(root, "Ore" + k, Kit.SharedMesh("rxfeedOre" + (k % 3), () => Vol2FsB1.Rock(870 + k % 3, 0.5f, 0.7f, 12, 9)), crustMat);
                lump.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, Vol2Fs.Hash(Seed, k + 30) * 360f, 0f));
                lump.transform.localScale = Vector3.zero;
                _crust.Add(lump); _crustAt.Add(at);
            }
            // the crown of steel crystals (a geode of blades): tallest in the middle, leaning out
            // r3: darker reflective chrome with a gold edge (r2 bright 1.3 / hot 0.55 read as flat white paper spikes)
            var steel = Kit.Metal(_mine ? 0.72f : 0.55f, _mine ? 0.28f : 0f, _mine ? new Color(0.09f, 0.06f, 0.015f) : new Color(0.01f, 0.006f, 0.014f), _mine ? new Color(0.86f, 0.84f, 0.9f) : new Color(0.7f, 0.66f, 0.78f)) ?? Vol2FsB1.Lit(Kit, "fs_steel", Color.white, 1f, 0.85f);
            var spike = Kit.SharedMesh("rxfeedCrystal", () => Vol2FsB6.Spike(0.3f, 0.12f, 0.28f, 0f));
            int n = 19;
            for (int k = 0; k < n; k++)
            {
                float u = k / (float)n, a = u * Mathf.PI * 2f * 2.3f + Vol2Fs.Hash(Seed, k + 40) * 0.5f;
                float r = Radius * (k < 3 ? 0.15f * k : 0.35f + 0.95f * Vol2Fs.Hash(Seed, k + 50));      // r2: the crown x1.6 wide / tall (r1 ~1 m)
                Vector3 outw = Dir * Mathf.Cos(a) + Side * Mathf.Sin(a);
                var c = new Crystal { At = G(P + outw * r), Lean = (Vector3.up + outw * (0.15f + 0.8f * r / Mathf.Max(0.1f, Radius * 1.3f))).normalized, H = Mathf.Lerp(3.8f, 1.3f, Mathf.Clamp01(r / (Radius * 1.3f))) * (0.8f + 0.35f * Vol2Fs.Hash(Seed, k + 60)), W = 1.3f + 0.8f * Vol2Fs.Hash(Seed, k + 70), Delay = 0.03f * (k % 5) + 0.06f * Mathf.Clamp01(r / Radius), Spin = Vol2Fs.Hash(Seed, k + 80) * 180f };
                c.R = Vol2FsB1.Prop(root, "Crystal" + k, spike, steel);
                c.R.transform.localScale = Vector3.zero;
                _cr.Add(c);
            }
            _rocks = Vol2Fs.Build(new Vol2FsPs { Mat = crustMat, Mesh = Kit.SharedMesh("actRock", () => Vol2FsB1.Rock(17, 0.5f)), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.08f, Size1 = 0.22f, Gravity = 2.4f, Drag = 0.3f, Spin = 600f, Max = 150 }, root, "CrustBits");
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.6f, 0.5f, 0.4f, 1f), 0.3f)), Life0 = 0.7f, Life1 = 1.5f, Speed0 = 0.6f, Speed1 = 3f, Size0 = 0.4f, Size1 = 0.9f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.7f, Drag = 1.4f, Max = 200 }, root, "Dust");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.85f, 0.5f), 5f, 1f)), Life0 = 0.25f, Life1 = 0.6f, Speed0 = 6f, Speed1 = 14f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.2f, Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.4f, Max = 500 }, root, "GoldSparks");
            _glint = Vol2FsRxFeedKit.At(Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.95f, 0.8f), 4f, 1f)), Loop = true, Rate = 0f, Life0 = 0.15f, Life1 = 0.35f, Speed0 = 0f, Speed1 = 0.1f, Size0 = 0.1f, Size1 = 0.26f, Radius = Radius * 1.2f, ShapeScale = new Vector3(1f, 1.6f, 1f), SizeStart = 0.2f, SizePeak = 1f, SizePeakAt = 0.4f, SizeEnd = 0f, Max = 60 }, root, "Glints"), P + Vector3.up * 0.9f);
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = steel, Mesh = Kit.SharedMesh("rxfeedShard", () => Vol2FsB1.Shard(5)), Life0 = 0.5f, Life1 = 1f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.08f, Size1 = 0.2f, Gravity = 2f, Drag = 0.5f, Spin = 900f, Max = 160 }, root, "Shards");
            _glint.Play(false);
            _light = Vol2Fs.PointLight(root, "OreLight", new Color(1f, 0.82f, 0.5f), 10f);
            _light.transform.position = P + Vector3.up * 1.0f;
            _burst = Vol2FsB1.Burst(Seed, 11, Side, Dir, 2.6f, 5f, 0.12f, 0.3f, 0.85f, 0.25f);
            for (int k = 0; k < 10; k++) Vol2FsB1.EmitV(_dust, P + Random.insideUnitSphere * Radius * 0.6f + Vector3.up * 0.2f, (Random.onUnitSphere + Vector3.up * 0.4f).normalized * Random.Range(0.6f, 2.5f));
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            if (!HasChild) { if (Patch != null) Vol2FsRxFeedKit.Consume(Patch, t, 0f, Mathf.Max(0.1f, DryRel), 0.2f); return; }
            bool dried = ChildWhy == Vol2MaterialEnd.Consumed || ChildWhy == Vol2MaterialEnd.ChildConsumed;
            float outK = Vol2FsRxFeedKit.Out(t, ChildEndAt, dried ? 0.3f : 0.45f);
            // the enemy's blades go under the crust
            Vol2FsRxFeedKit.Consume(Patch, t, 0.05f, 0.32f, 1.0f);
            // the crust pours over (0 - 0.28 s), then splits (0.32 s): the lumps fly apart and are gone
            float pour = Vol2Fs.EaseOutBack(Vol2Fs.Smooth(0f, 0.28f, t));
            bool split = t >= 0.32f;
            for (int k = 0; k < _crust.Count; k++)
            {
                float s = split ? 0f : Mathf.Max(0f, pour) * Radius * (k == 0 ? 1.8f : 1.1f + 0.5f * Vol2Fs.Hash(Seed, k));
                _crust[k].transform.localScale = new Vector3(s, s * 0.7f, s);
            }
            if (split && !_split)
            {
                _split = true; HitStop(t, 0.09f);
                for (int k = 0; k < 60; k++) Vol2FsB1.EmitV(_rocks, P + Random.insideUnitSphere * Radius * 0.5f + Vector3.up * 0.3f, (Random.onUnitSphere + Vector3.up * 0.7f).normalized * Random.Range(2f, 6.5f));
                for (int k = 0; k < 220; k++) Vol2FsB1.EmitV(_sparks, P + Vector3.up * 0.5f, (Random.onUnitSphere + Vector3.up * 0.6f).normalized * Random.Range(5f, 14f));
                for (int k = 0; k < 12; k++) Vol2FsB1.EmitV(_dust, G(P + Random.insideUnitSphere * Radius * 1.2f), (Random.onUnitSphere + Vector3.up * 0.1f).normalized * Random.Range(1f, 3f));
            }
            // the crystals burst up (0.32 - 0.6 s, overshoot), stand, and snap at the child's end
            float snapK = dried ? 0f : Vol2Fs.Smooth(ChildEndAt < 0f ? 999f : ChildEndAt, (ChildEndAt < 0f ? 999f : ChildEndAt) + 0.35f, t);
            if (!dried && ChildEndAt >= 0f && !_snapped)
            {
                _snapped = true;
                foreach (var c in _cr) for (int j = 0; j < 4; j++) Vol2FsB1.EmitV(_shards, c.At + c.Lean * c.H * Random.Range(0.3f, 0.9f), Vector3.Cross(Vector3.up, c.Lean).normalized * Random.Range(-6f, 6f) + Vector3.up * Random.Range(1f, 3f));
                Vol2FsB1.Emit(_sparks, P + Vector3.up * 0.8f, 120);
            }
            float shrinkDry = dried ? 1f - outK : 0f;
            foreach (var c in _cr)
            {
                float g = Mathf.Max(0f, Vol2Fs.EaseOutBack(Vol2Fs.Smooth(0.32f + c.Delay, 0.55f + c.Delay, t))) * (1f - shrinkDry);
                float h = c.H * g * (1f - snapK * 0.85f);
                c.R.transform.SetPositionAndRotation(c.At + Vector3.down * snapK * 0.25f, Quaternion.FromToRotation(Vector3.up, c.Lean) * Quaternion.Euler(0f, c.Spin + t * 4f, 0f));
                c.R.transform.localScale = new Vector3(c.W * g, Mathf.Max(0.001f, h), c.W * g);
            }
            Vol2FsRxFeedKit.Rate(_glint, (_mine ? 30f : 4f) * Vol2Fs.Smooth(0.5f, 0.8f, t) * outK * (1f - snapK));
            if (_light != null) _light.intensity = (_mine ? 15f : 3f) * (Vol2Fs.Smooth(0.3f, 0.36f, t) * (0.3f + 0.7f * (1f - Vol2Fs.Smooth(0.4f, 1.6f, t)))) * outK + (_mine ? 1.5f : 0f) * snapK * (1f - Vol2Fs.Smooth(0f, 0.4f, t - (ChildEndAt < 0f ? 0f : ChildEndAt)));
            // the gold flash at the split: a ring and a vertical streak, thin to wide
            _flash.Clear();
            float fl = 1f - Vol2Fs.Smooth(0.34f, 0.8f, t);
            if (t > 0.3f && fl > 0.01f)
            {
                Vol2FsB4A.GroundArc(_flash, P, Ground + 0.04f, Radius * (0.8f + 2.6f * Vol2Fs.Smooth(0.32f, 0.6f, t)), 0.3f * fl, 0f, Mathf.PI * 2f, fl, 0.4f, cam, 40);
                Vol2FsB6.Slash(_flash, P + Vector3.up * 0.1f, P + Vector3.up * 4.6f, Vector3.zero, 0.9f * fl, 1f, fl, 0.2f, cam);
            }
            _flash.Write(_flashMesh);
            // the ink sweeps at the split
            _ink.Clear();
            float hh = t - 0.32f;
            if (hh > 0f && hh < 2f) Vol2FsB1.DrawBurst(_ink, _burst, P + Vector3.up * 0.9f, hh, cam, 0.3f, 0.1f);
            _ink.Write(_inkMesh);
            _seal.Clear(); _sealGlow.Clear();
            Vol2FsRxFeedKit.Seal(_seal, _sealGlow, P, Ground, Radius * 1.9f, Vol2Fs.Smooth(0.4f, 0.85f, t), Vol2FsRxFeedKit.Left(t, ChildExpireRel, _lifeS), outK, cam, Seed * 0.1f);
            _seal.Write(_sealMesh); _sealGlow.Write(_sealGlowMesh);
        }

        protected override void OnChildEnd(Vol2MaterialEnd why, float t) { Note += " | metal child " + (why == Vol2MaterialEnd.Expired ? "snapped" : "dulled in place"); }
    }
}
