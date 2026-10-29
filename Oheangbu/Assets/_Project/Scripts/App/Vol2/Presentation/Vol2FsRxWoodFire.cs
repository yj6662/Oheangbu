using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>Batch 8 먹임 (Feed) group helpers: the owner's seal under a child (an ink ring + the accent arc of the life left, read live from the child's
    /// ExpiresUs), the ink / glow materials, the exit factor. Presentation only.</summary>
    public static class Vol2FsRxFeedKit
    {
        public static readonly Color Ink = new Color(0.03f, 0.025f, 0.02f, 1f);
        public static Material InkMat(Vol2FsKit kit) { var m = Vol2FsMat.Make("fs_stroke2", Ink, 1f, 0f); m.NoiseAmt = 0.45f; m.Erode = 0.3f; m.Soft = 0.08f; return kit.Mat(m); }
        public static Material GlowMat(Vol2FsKit kit, Color acc, float i = 2.4f) { var m = Vol2FsMat.Make("fs_streak", acc, i, 1f); m.NoiseAmt = 0.25f; m.Soft = 0.05f; m.Erode = 0.15f; return kit.Mat(m); }

        /// <summary>The 점화자 표식 under the child: an ink ring and, just outside it, the accent arc of the life left (left01 = 1 at birth, 0 at the end).</summary>
        public static void Seal(Vol2FsStrips ink, Vol2FsStrips glow, Vector3 c, float ground, float r, float grow, float left01, float life, Vector3 cam, float a0)
        {
            if (life <= 0.01f || grow <= 0.01f) return;
            Vol2FsB4A.GroundArc(ink, c, ground + 0.01f, r * grow, Mathf.Max(0.12f, r * 0.09f), a0, Mathf.PI * 2f, life * 0.85f, 0.37f, cam, 48);
            if (glow != null && left01 > 0.01f) Vol2FsB4A.GroundArc(glow, c, ground + 0.02f, r * grow + Mathf.Max(0.07f, r * 0.06f), Mathf.Max(0.05f, r * 0.04f), a0, Mathf.PI * 2f * Mathf.Clamp01(left01), life, 0.71f, cam, 48);
        }

        /// <summary>1 while the child lives, then down to 0 over <paramref name="dur"/> s from its end.</summary>
        public static float Out(float t, float endAt, float dur) => endAt < 0f ? 1f : 1f - Vol2Fs.Smooth(endAt, endAt + Mathf.Max(0.05f, dur), t);

        public static void Rate(ParticleSystem ps, float r) { if (ps == null) return; var e = ps.emission; e.rateOverTime = Mathf.Max(0f, r); }

        public static ParticleSystem At(ParticleSystem ps, Vector3 p) { if (ps != null) ps.transform.position = p; return ps; }

        /// <summary>The enemy's patch used up in place over [t0, t1] (shrinks, sinks, its own particles stop), hidden after.</summary>
        public static void Consume(Vol2FsPatch patch, float t, float t0, float t1, float sinkK = 0.6f)
        {
            if (patch == null || patch.Go == null) return;
            float k = Vol2Fs.Smooth(t0, t1, t);
            if (t > t0) patch.StopParticles();
            if (k >= 0.999f) { if (patch.Go.activeSelf) patch.SetActive(false); return; }
            patch.Pose(Mathf.Max(0.02f, 1f - k), k * patch.Height * sinkK, 1f - 0.25f * k);
        }

        /// <summary>The life left of the child (1 .. 0), from its live expiry.</summary>
        public static float Left(float t, float expireRel, float lifeS) => expireRel < 0f || lifeS <= 0.01f ? 1f : Mathf.Clamp01((expireRel - t) / lifeS);
    }

    /// <summary>목>화 먹임 - free showcase batch 8, concept wf_b (kindling knot): the wood shot's fibres weave a basket round the enemy's fire, the fire eats them and
    /// bursts into a tall bonfire of the caster's own (bright orange tongues, black calligraphic ink sweeps, a swirl of fire on the ground), the enemy's dark fire
    /// scattering as violet-black ash. The child (fire, 10 s) burns to its end; Expired = it burns down to ash; Consumed / ChildConsumed = dries out in place 0.3 s.</summary>
    public sealed class Vol2FsRxWoodFire : Vol2FsReact
    {
        ParticleSystem _fire, _core, _flare, _embers, _ash, _soot, _sparks, _darkAsh; Light _light;
        Mesh _inkMesh, _sealMesh, _sealGlowMesh, _vineMesh, _swirlMesh;
        readonly Vol2FsStrips _ink = new Vol2FsStrips(), _seal = new Vol2FsStrips(), _sealGlow = new Vol2FsStrips(), _swirl = new Vol2FsStrips();
        readonly Vol2FsTubes _vines = new Vol2FsTubes();
        List<Vol2FsB1.Stroke> _burst;
        bool _mine, _lit, _ashed; float _lifeS = 10f;
        protected override float Burst => 2.2f;

        protected override void OnBuild()
        {
            _mine = !EnemyIgniter; _lifeS = V.ChildLifeUs > 0 ? V.ChildLifeUs / 1e6f : 10f;
            var root = Root.transform;
            Vol2Fs.MeshObject(root, "Ink", Vol2FsRxFeedKit.InkMat(Kit), out _inkMesh);
            Vol2Fs.MeshObject(root, "Seal", Vol2FsRxFeedKit.InkMat(Kit), out _sealMesh);
            Vol2Fs.MeshObject(root, "SealGlow", Vol2FsRxFeedKit.GlowMat(Kit, _mine ? new Color(1f, 0.45f, 0.12f) : new Color(0.45f, 0.15f, 0.3f), 2.4f), out _sealGlowMesh);
            Vol2Fs.MeshObject(root, "Swirl", Vol2FsRxFeedKit.GlowMat(Kit, new Color(1f, 0.42f, 0.1f), _mine ? 2.6f : 1.2f), out _swirlMesh);
            Vol2Fs.MeshObject(root, "Basket", Kit.Bark(new Color(0.8f, 1f, 0.6f), new Color(0.35f, 1.3f, 0.3f) * 7f, 0.3f, "fs_vine", "fs_vein"), out _vineMesh);
            if (!HasChild) return;
            // the basket: six fibres spiralling up round the old fire and closing over it
            for (int j = 0; j < 6; j++)
            {
                var pts = new Vector3[8];
                for (int k = 0; k < 8; k++)
                {
                    float u = k / 7f, a = j * 1.047f + u * 2.6f + (Vol2Fs.Hash(Seed, j) - 0.5f) * 0.4f;
                    float r = Radius * Mathf.Lerp(1.5f, 0.2f, u * u);      // r2: x1.6 (r1 ~1 m against the concept's 3 - 5 m)
                    pts[k] = P + (Dir * Mathf.Cos(a) + Side * Mathf.Sin(a)) * r + Vector3.up * (u * 2.1f - 0.02f);
                }
                var tb = _vines.Add(pts, 0.11f, 0.045f, 20); tb.Knots = 0.3f; tb.Sides = 6; tb.Grow = 0f;
            }
            var lick = Vol2FsPolish.Lick(_mine ? 1.65f : 1.1f, 0.65f, _mine ? 1.2f : 0.9f);
            // r3: many smaller uneven tongues in a rounded mound (r2: few 2 - 4 m billboards read as a flat sheet / box of flame from the side) + a narrow tall core
            var fire = Vol2FsPolish.Licks(Kit.Mat(lick), 0.4f, 0.95f, 0.45f, 0.9f, 2.6f, 1200);
            fire.Loop = true; fire.Rate = 0f; fire.Radius = Radius * 0.85f; fire.ShapeScale = new Vector3(1f, 0.45f, 1f); fire.Aspect = 1.9f;
            _fire = Vol2FsRxFeedKit.At(Vol2Fs.Build(fire, root, "Bonfire"), P + Vector3.up * 0.25f);
            var core = Vol2FsPolish.Licks(Kit.Mat(lick), 0.6f, 1.2f, 0.5f, 0.95f, 3.2f, 400);
            core.Loop = true; core.Rate = 0f; core.Radius = Radius * 0.35f; core.ShapeScale = new Vector3(1f, 0.3f, 1f); core.Aspect = 2.3f;
            _core = Vol2FsRxFeedKit.At(Vol2Fs.Build(core, root, "BonfireCore"), P + Vector3.up * 0.35f);
            _core.Play(false);
            var flareM = Vol2FsB3.Fire(1.25f, 0.75f); flareM.FlipHeat = 0.5f; flareM.RampScale = 1.05f;      // r3: orange-red (r2 a yellow-white paper blob)
            _flare = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(flareM), FlipScale = 1.4f, Life0 = 0.45f, Life1 = 0.8f, Speed0 = 1.5f, Speed1 = 3.5f, Size0 = 1.1f, Size1 = 1.9f, Radius = 0.5f, VelOverLife = new Vector3(0f, 3f, 0f), Drag = 1.2f, Max = 80 }, root, "Flare");
            _embers = Vol2FsRxFeedKit.At(Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.55f, 0.18f), 4f, 1f)), Loop = true, Rate = 0f, Life0 = 0.8f, Life1 = 1.8f, Speed0 = 0.5f, Speed1 = 2.2f, Size0 = 0.035f, Size1 = 0.08f, Radius = Radius * 1.0f, ShapeScale = new Vector3(1f, 0.2f, 1f), Gravity = -0.5f, Noise = 1f, NoiseFreq = 1.5f, Max = 500 }, root, "Embers"), P + Vector3.up * 0.3f);
            _soot = Vol2FsRxFeedKit.At(Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsPolish.Soot(0.5f)), Loop = true, Rate = 0f, Life0 = 1.6f, Life1 = 2.6f, Speed0 = 0.3f, Speed1 = 0.7f, Size0 = 0.7f, Size1 = 1.4f, Radius = Radius * 0.6f, VelOverLife = new Vector3(0f, 0.9f, 0f), SizeStart = 0.6f, SizePeak = 1f, SizeEnd = 1.8f, Max = 80 }, root, "Soot"), P + Vector3.up * 1.6f);
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Ash()), Life0 = 1.2f, Life1 = 2.4f, Speed0 = 0.3f, Speed1 = 1.3f, Size0 = 0.04f, Size1 = 0.1f, Radius = Radius * 0.6f, Gravity = -0.3f, Noise = 0.8f, NoiseFreq = 1.2f, Spin = 200f, Max = 300 }, root, "Ash");
            _darkAsh = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_ink", new Color(0.13f, 0.06f, 0.15f, 1f), 1f, 0f)), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 2.5f, Speed1 = 6f, Size0 = 0.06f, Size1 = 0.16f, Radius = 0.3f, Gravity = 0.8f, Drag = 1f, Spin = 400f, Max = 200 }, root, "EnemyAsh");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.75f, 0.35f), 5f, 1f)), Life0 = 0.2f, Life1 = 0.5f, Speed0 = 5f, Speed1 = 12f, Size0 = 0.02f, Size1 = 0.045f, Gravity = 1.2f, Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.3f, Max = 300 }, root, "Sparks");
            foreach (var ps in new[] { _fire, _embers, _soot }) ps.Play(false);
            _light = Vol2Fs.PointLight(root, "FireLight", new Color(1f, 0.55f, 0.25f), 11f);
            _light.transform.position = P + Vector3.up * 1.2f;
            _burst = Vol2FsB1.Burst(Seed, 12, Side, Dir, 2.6f, 5f, 0.16f, 0.4f, 0.7f, 0.3f);
            Vol2FsB1.Emit(_sparks, P + Vector3.up * 0.4f - Dir * Radius * 0.6f, 60);
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            if (!HasChild)
            {
                // a child met again (or no child): the patch dries in place, no direction
                if (Patch != null) Vol2FsRxFeedKit.Consume(Patch, t, 0f, Mathf.Max(0.1f, DryRel), 0.2f);
                return;
            }
            bool dried = ChildWhy == Vol2MaterialEnd.Consumed || ChildWhy == Vol2MaterialEnd.ChildConsumed;
            float outK = Vol2FsRxFeedKit.Out(t, ChildEndAt, dried ? 0.3f : 1.2f);
            // the enemy's fire eats the basket: it dies under it
            Vol2FsRxFeedKit.Consume(Patch, t, 0.12f, 0.5f, 0.3f);
            // the basket weaves (0 - 0.22 s) and burns away (0.35 - 0.95 s)
            float weave = Vol2Fs.Smooth(0f, 0.22f, t), burn = Vol2Fs.Smooth(0.35f, 0.95f, t);
            foreach (var tb in _vines.Tubes) tb.Grow = weave * (1f - burn);
            _vines.Write(_vineMesh);
            if (!_lit && t >= 0.28f)
            {
                _lit = true; HitStop(t, 0.08f);
                for (int k = 0; k < 12; k++) Vol2FsB1.EmitV(_flare, P + Vector3.up * (0.3f + 0.15f * k) + Random.insideUnitSphere * 0.25f, Vector3.up * Random.Range(1.5f, 3.5f) + Random.insideUnitSphere * 0.6f);
                for (int k = 0; k < 120; k++) Vol2FsB1.EmitV(_darkAsh, P + Vector3.up * 0.3f + Random.insideUnitSphere * 0.4f, (Random.onUnitSphere + Vector3.up * 0.6f).normalized * Random.Range(2.5f, 6.5f));
                Vol2FsB1.Emit(_sparks, P + Vector3.up * 0.6f, 120);
            }
            // the bonfire of mine: flares at the burst, steady, flickers, burns down at the end
            float lit = Vol2Fs.Smooth(0.25f, 0.4f, t);
            float flick = 0.85f + 0.15f * Mathf.PerlinNoise(t * 3.1f, Seed * 0.01f);
            float surge = 1f + 0.8f * (1f - Vol2Fs.Smooth(0.4f, 1.4f, t));
            Vol2FsRxFeedKit.Rate(_fire, 300f * lit * surge * flick * outK * (_mine ? 1f : 0.7f));
            Vol2FsRxFeedKit.Rate(_core, 70f * lit * flick * outK * (_mine ? 1f : 0.7f));
            Vol2FsRxFeedKit.Rate(_embers, 70f * lit * outK);
            Vol2FsRxFeedKit.Rate(_soot, 13f * lit * Mathf.Max(outK, 0.4f));
            if (_light != null) _light.intensity = (_mine ? 15f : 4f) * lit * surge * flick * outK;
            if (!_ashed && ChildEndAt >= 0f && !dried)
            {
                _ashed = true;
                for (int k = 0; k < 140; k++) Vol2FsB1.EmitV(_ash, P + Random.insideUnitSphere * Radius * 0.6f + Vector3.up * 0.4f, Vector3.up * Random.Range(0.4f, 1.6f) + Random.insideUnitSphere * 0.4f);
                Vol2FsRxFeedKit.Rate(_soot, 0f);
            }
            if (ChildEndAt >= 0f && t > ChildEndAt + 0.3f) { foreach (var ps in new[] { _fire, _core, _embers, _soot }) if (ps != null && ps.isEmitting) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
            // the black calligraphic sweeps at the ignition
            _ink.Clear();
            float h = t - 0.28f;
            if (h > 0f && h < 2.2f) Vol2FsB1.DrawBurst(_ink, _burst, P + Vector3.up * 1.0f, h, cam, 0.25f, 0.1f);
            _ink.Write(_inkMesh);
            // the swirl of fire on the ground: three arcs wheel out from the fire and settle into a thin burning ring for the child's life
            _swirl.Clear();
            float sw = Vol2Fs.Smooth(0.28f, 0.7f, t);
            if (t > 0.28f)
            {
                float fade = Mathf.Lerp(1f, 0.45f, Vol2Fs.Smooth(0.9f, 1.8f, t)) * outK;
                for (int k = 0; k < 3; k++)
                {
                    float rr = Radius * Mathf.Lerp(0.8f, 2.1f + 0.55f * k, sw), a0 = Seed * 0.3f + k * 2.1f + t * (1.4f - 0.3f * k);
                    float span = Mathf.Lerp(1.6f, 0.9f, Vol2Fs.Smooth(0.9f, 1.8f, t)) * (k == 0 ? 1.4f : 1f);
                    Vol2FsB4A.GroundArc(_swirl, P, Ground + 0.03f, rr, Mathf.Lerp(0.3f, 0.11f, Vol2Fs.Smooth(0.5f, 1.5f, t)), a0, span, fade * (k == 0 ? 1f : 1f - Vol2Fs.Smooth(1.2f, 2.2f, t)), 0.2f + 0.3f * k, cam, 28);
                }
            }
            _swirl.Write(_swirlMesh);
            // the owner's seal: ink ring + the life left
            _seal.Clear(); _sealGlow.Clear();
            Vol2FsRxFeedKit.Seal(_seal, _sealGlow, P, Ground, Radius * 1.9f, Vol2Fs.Smooth(0.35f, 0.8f, t), Vol2FsRxFeedKit.Left(t, ChildExpireRel, _lifeS), outK, cam, Seed * 0.1f);
            _seal.Write(_sealMesh); _sealGlow.Write(_sealGlowMesh);
        }

        protected override void OnChildEnd(Vol2MaterialEnd why, float t) { Note += " | fire child " + (why == Vol2MaterialEnd.Expired ? "burnt down to ash" : "dried out"); }
    }
}
