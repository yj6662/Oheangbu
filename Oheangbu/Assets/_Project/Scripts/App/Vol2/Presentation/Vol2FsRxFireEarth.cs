using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>화>토 먹임 - free showcase batch 8, concept fe_a (kiln-fired ground): the fire wraps the enemy's slabs and bakes them down - the slabs crumble into the
    /// flames, a spiral of fire wheels round the patch and black ink sweeps fly flat over the ground - and a smooth hard baked plate of the caster's own is left
    /// (pale glaze, glowing kiln cracks that cool from white-orange to a dull ember red). The child (earth, 10 s): Expired = the plate cools grey, cracks and crumbles
    /// into the ground; Consumed / ChildConsumed = it dries out in place 0.3 s.</summary>
    public sealed class Vol2FsRxFireEarth : Vol2FsReact
    {
        ParticleSystem _wrap, _rocks, _smoke, _sparks, _dust, _embers, _flare; Light _light;
        MeshRenderer _plate; readonly List<MeshRenderer> _shards = new List<MeshRenderer>(); readonly List<Vector3> _shardAt = new List<Vector3>();
        Mesh _inkMesh, _spiralMesh, _sealMesh, _sealGlowMesh;
        readonly Vol2FsStrips _ink = new Vol2FsStrips(), _spiral = new Vol2FsStrips(), _seal = new Vol2FsStrips(), _sealGlow = new Vol2FsStrips();
        MaterialPropertyBlock _mpb; Color _hot = new Color(7f, 1.7f, 0.3f), _cool = new Color(1.4f, 0.28f, 0.04f);
        bool _mine, _baked, _crumbled; float _lifeS = 10f;
        protected override float Burst => 2.4f;

        protected override void OnBuild()
        {
            _mine = !EnemyIgniter; _lifeS = V.ChildLifeUs > 0 ? V.ChildLifeUs / 1e6f : 10f;
            var root = Root.transform;
            Vol2Fs.MeshObject(root, "Ink", Vol2FsRxFeedKit.InkMat(Kit), out _inkMesh);
            Vol2Fs.MeshObject(root, "Spiral", Vol2FsRxFeedKit.GlowMat(Kit, new Color(1f, 0.5f, 0.15f), _mine ? 2.8f : 1.2f), out _spiralMesh);
            Vol2Fs.MeshObject(root, "Seal", Vol2FsRxFeedKit.InkMat(Kit), out _sealMesh);
            Vol2Fs.MeshObject(root, "SealGlow", Vol2FsRxFeedKit.GlowMat(Kit, _mine ? new Color(0.95f, 0.62f, 0.3f) : new Color(0.4f, 0.2f, 0.3f), 2.2f), out _sealGlowMesh);
            if (!HasChild) return;
            // the baked plate: a low dome of glazed fired earth (pivot on the ground), with a few fused lumps
            // r3: a dark glazed fired-earth plate with glowing cracks on the plate's own radial uv (r2: the triplanar stone shader on the stretched disc read cream, then a
            // red checkered tile); URP Lit, made once per kit
            bool mine = _mine;
            var plateMat = Kit.Custom("rxfeedPlate|" + mine, () =>
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Fs_RxFeedPlate" };
                m.SetTexture("_BaseMap", Kit.Tex("fs_rock")); m.SetColor("_BaseColor", mine ? new Color(0.2f, 0.13f, 0.1f) : new Color(0.12f, 0.09f, 0.11f));
                m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.78f);
                m.SetTexture("_EmissionMap", Kit.Tex("fs_crack")); m.SetColor("_EmissionColor", new Color(2.5f, 0.7f, 0.15f)); m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                return m;
            });
            _plate = Vol2FsB1.Prop(root, "BakedPlate", Vol2FsB7.PoolMesh(Kit, Seed % 97 + 300), plateMat, false);
            _plate.transform.SetPositionAndRotation(P + Vector3.up * 0.01f, Quaternion.Euler(0f, Seed % 360, 0f));
            _plate.transform.localScale = new Vector3(0.01f, 4f, 0.01f);
            for (int k = 0; k < 5; k++)
            {
                float a = k * 1.256f + Vol2Fs.Hash(Seed, k), r = Radius * (0.45f + 0.9f * Vol2Fs.Hash(Seed, k + 7));
                var s = Vol2FsB1.Prop(root, "Fused" + k, Kit.SharedMesh("rxfeedLump" + (k % 3), () => Vol2FsB1.Rock(760 + k % 3, 0.5f, 0.45f, 12, 8)), plateMat, false);
                Vector3 at = G(P + (Dir * Mathf.Cos(a) + Side * Mathf.Sin(a)) * r);
                s.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, Vol2Fs.Hash(Seed, k + 20) * 360f, 0f));
                s.transform.localScale = Vector3.zero;
                _shards.Add(s); _shardAt.Add(at);
            }
            _mpb = new MaterialPropertyBlock();
            var lick = Vol2FsPolish.Lick(_mine ? 1.6f : 1.1f, 0.6f, 1.15f);
            var wrap = Vol2FsPolish.Licks(Kit.Mat(lick), 0.7f, 1.3f, 0.45f, 0.85f, 2.4f, 800);
            wrap.Loop = true; wrap.Rate = 0f; wrap.Shape = ParticleSystemShapeType.Circle; wrap.Radius = Radius * 1.6f; wrap.ShapeRot = new Vector3(90f, 0f, 0f);
            _wrap = Vol2FsRxFeedKit.At(Vol2Fs.Build(wrap, root, "WrapFlames"), P + Vector3.up * 0.1f);
            _rocks = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.5f, 0.42f, 0.4f), 0f, 0.12f), Mesh = Kit.SharedMesh("actRock", () => Vol2FsB1.Rock(17, 0.5f)), Life0 = 0.5f, Life1 = 1.0f, Speed0 = 1.5f, Speed1 = 4f, Size0 = 0.07f, Size1 = 0.18f, Gravity = 2.4f, Drag = 0.4f, Spin = 500f, Max = 120 }, root, "Crumble");
            _smoke = Vol2FsRxFeedKit.At(Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsPolish.Soot(0.45f)), Loop = true, Rate = 0f, Life0 = 1.4f, Life1 = 2.4f, Speed0 = 0.2f, Speed1 = 0.6f, Size0 = 0.5f, Size1 = 1.1f, Radius = Radius * 1.3f, ShapeScale = new Vector3(1f, 0.1f, 1f), VelOverLife = new Vector3(0f, 0.8f, 0f), SizeStart = 0.6f, SizePeak = 1f, SizeEnd = 1.7f, Max = 80 }, root, "KilnSmoke"), P + Vector3.up * 0.2f);
            _embers = Vol2FsRxFeedKit.At(Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.55f, 0.2f), 3.5f, 1f)), Loop = true, Rate = 0f, Life0 = 0.6f, Life1 = 1.4f, Speed0 = 0.2f, Speed1 = 0.9f, Size0 = 0.03f, Size1 = 0.07f, Radius = Radius * 1.9f, ShapeScale = new Vector3(1f, 0.05f, 1f), Gravity = -0.3f, Noise = 0.8f, NoiseFreq = 1.4f, Max = 200 }, root, "KilnEmbers"), P + Vector3.up * 0.05f);
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.7f, 0.3f), 5f, 1f)), Life0 = 0.25f, Life1 = 0.55f, Speed0 = 4f, Speed1 = 10f, Size0 = 0.02f, Size1 = 0.045f, Gravity = 1.4f, Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.3f, Max = 300 }, root, "Sparks");
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.5f, 0.45f, 0.42f, 1f), 0.5f)), Life0 = 0.8f, Life1 = 1.6f, Speed0 = 0.3f, Speed1 = 1.4f, Size0 = 0.3f, Size1 = 0.7f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.6f, Drag = 1.3f, Max = 120 }, root, "CoolDust");
            // r2: the firing flare - a wide low sheet of flame over the whole plate at the bake (it has to outshine 나's own hit)
            _flare = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.7f, 0.9f)), FlipScale = 1.6f, Life0 = 0.45f, Life1 = 0.8f, Speed0 = 0.5f, Speed1 = 2f, Size0 = 1.3f, Size1 = 2.4f, Radius = 0.3f, VelOverLife = new Vector3(0f, 1.6f, 0f), Drag = 1.2f, Max = 80 }, root, "KilnFlare");
            foreach (var ps in new[] { _wrap, _smoke, _embers }) ps.Play(false);
            _light = Vol2Fs.PointLight(root, "KilnLight", new Color(1f, 0.55f, 0.25f), 10f);
            _light.transform.position = P + Vector3.up * 0.8f;
            Vol2FsB1.Emit(_sparks, P + Vector3.up * 0.3f - Dir * Radius * 0.5f, 70);
            HitStop(0f, 0.07f);
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            if (!HasChild) { if (Patch != null) Vol2FsRxFeedKit.Consume(Patch, t, 0f, Mathf.Max(0.1f, DryRel), 0.2f); return; }
            bool dried = ChildWhy == Vol2MaterialEnd.Consumed || ChildWhy == Vol2MaterialEnd.ChildConsumed;
            float outK = Vol2FsRxFeedKit.Out(t, ChildEndAt, dried ? 0.3f : 1.3f);
            // the slabs crumble into the flames (0.1 - 0.5 s)
            if (Patch != null && !_crumbled && t > 0.12f)
            {
                _crumbled = true;
                for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_rocks, P + Random.insideUnitSphere * Radius * 0.6f + Vector3.up * 0.3f, (Random.onUnitSphere + Vector3.up * 0.9f).normalized * Random.Range(1.5f, 4f));
            }
            Vol2FsRxFeedKit.Consume(Patch, t, 0.1f, 0.5f, 0.9f);
            // the wrapping fire: a ring of tongues round the slabs, high at the burst, then low along the plate's rim while it is hot
            float wrapK = Vol2Fs.Smooth(0f, 0.12f, t) * (1f - 0.75f * Vol2Fs.Smooth(0.55f, 1.4f, t)) * (1f - Vol2Fs.Smooth(2.4f, 3.6f, t)) * outK;
            Vol2FsRxFeedKit.Rate(_wrap, 260f * wrapK);
            // the plate is baked (0.3 - 0.75 s): spreads, glows white-orange, cools over 3 s to a dull ember glow
            float bake = Vol2Fs.EaseOutBack(Vol2Fs.Smooth(0.3f, 0.75f, t));
            if (!_baked && t >= 0.3f)
            {
                _baked = true; HitStop(t, 0.07f); Vol2FsB1.Emit(_sparks, P + Vector3.up * 0.2f, 160);
                for (int k = 0; k < 18; k++) { float a = k * 0.35f; Vol2FsB1.EmitV(_flare, G(P + (Dir * Mathf.Cos(a) + Side * Mathf.Sin(a)) * Radius * (0.3f + 1.4f * Vol2Fs.Hash(Seed, k + 90))) + Vector3.up * 0.4f, Vector3.up * Random.Range(0.8f, 2.4f) + Random.insideUnitSphere * 0.5f); }
            }
            float crumble = dried ? 0f : Vol2Fs.Smooth(ChildEndAt < 0f ? 999f : ChildEndAt, (ChildEndAt < 0f ? 999f : ChildEndAt) + 1.2f, t);
            float shrink = dried ? 1f - outK : 0f;
            float pr = Radius * 2.1f * Mathf.Max(0f, bake) * (1f - shrink);      // r2: x1.8 (r1 the plate hid under the bear)
            if (_plate != null)
            {
                _plate.transform.localScale = new Vector3(Mathf.Max(0.01f, pr), 3f * (1f - crumble * 0.9f), Mathf.Max(0.01f, pr));
                _plate.transform.position = P + Vector3.up * (0.01f - crumble * 0.06f);
                float heat = 1f - Vol2Fs.Smooth(1.2f, 6f, t);
                Color em = Color.Lerp(_cool, _hot, heat) * (1f - crumble) * (_mine ? 1f : 0.25f) * (0.85f + 0.15f * Mathf.Sin(t * 5f + Seed));
                _plate.GetPropertyBlock(_mpb); _mpb.SetColor("_EmissionColor", em); _plate.SetPropertyBlock(_mpb);
                foreach (var s in _shards) { s.GetPropertyBlock(_mpb); _mpb.SetColor("_EmissionColor", em); s.SetPropertyBlock(_mpb); }
            }
            for (int k = 0; k < _shards.Count; k++)
            {
                float g = Mathf.Max(0f, Vol2Fs.EaseOutBack(Vol2Fs.Smooth(0.35f + 0.05f * k, 0.8f + 0.05f * k, t))) * (1f - shrink) * (1f - crumble);
                _shards[k].transform.localScale = new Vector3(0.9f, 0.45f, 0.75f) * (0.6f + 0.5f * Vol2Fs.Hash(Seed, k + 3)) * g * Radius;
                _shards[k].transform.position = _shardAt[k] + Vector3.down * crumble * 0.15f;
            }
            if (crumble > 0.01f && crumble < 0.3f) Vol2FsB1.Emit(_dust, P + Random.insideUnitSphere * Radius * 0.8f, 2);
            Vol2FsRxFeedKit.Rate(_smoke, 9f * Vol2Fs.Smooth(0.3f, 0.6f, t) * (1f - Vol2Fs.Smooth(3f, 5f, t)) * outK + 2f * outK);
            Vol2FsRxFeedKit.Rate(_embers, 30f * Vol2Fs.Smooth(0.3f, 0.6f, t) * (1f - Vol2Fs.Smooth(1.5f, 4f, t)) * outK);
            if (_light != null) _light.intensity = (_mine ? 13f : 3f) * (Vol2Fs.Smooth(0f, 0.15f, t) * (0.35f + 0.65f * (1f - Vol2Fs.Smooth(0.6f, 3f, t)))) * outK;
            // the spiral of fire wheels round the patch (concept: concentric glowing rings), then stays as the plate's hot rim lines
            _spiral.Clear();
            float sp = Vol2Fs.Smooth(0.05f, 0.6f, t);
            for (int k = 0; k < 4; k++)
            {
                float rr = Radius * (0.8f + 0.55f * k) * Mathf.Lerp(1.7f, 1f, sp), a0 = Seed * 0.2f + k * 1.6f - t * (2.2f - 0.35f * k);
                float span = Mathf.Lerp(Mathf.PI * 1.6f, Mathf.PI * (0.7f + 0.3f * k), Vol2Fs.Smooth(0.6f, 1.5f, t));
                float life = (Vol2Fs.Smooth(0.05f, 0.25f, t) * Mathf.Lerp(1f, 0.4f, Vol2Fs.Smooth(1f, 2.5f, t))) * (1f - Vol2Fs.Smooth(3f, 5f, t) * 0.6f) * outK * (1f - crumble);
                Vol2FsB4A.GroundArc(_spiral, P, Ground + 0.05f, rr, Mathf.Lerp(0.26f, 0.1f, Vol2Fs.Smooth(0.4f, 1.4f, t)), a0, span, life, 0.15f + 0.2f * k, cam, 44);
            }
            _spiral.Write(_spiralMesh);
            // the black ink sweeps flying flat round the plate at the burst (the concept's horizontal brush swooshes)
            _ink.Clear();
            float h = t - 0.05f;
            if (h > 0f && h < 1.6f)
            {
                for (int k = 0; k < 5; k++)
                {
                    float grow = Vol2Fs.EaseOutBack(Mathf.Clamp01(h / 0.22f)), life = 1f - Vol2Fs.Smooth(0.35f, 1.3f + 0.1f * k, h);
                    float rr = Radius * (2.2f + 0.6f * k) * (0.7f + 0.3f * grow), a0 = Seed * 0.7f + k * 1.25f + h * 0.8f;
                    Vol2FsB4A.GroundArc(_ink, P, Ground + 0.08f + 0.14f * k, rr, 0.4f - 0.04f * k, a0, Mathf.PI * (0.5f + 0.25f * grow), life, 0.3f + 0.13f * k, cam, 22);
                }
            }
            _ink.Write(_inkMesh);
            _seal.Clear(); _sealGlow.Clear();
            Vol2FsRxFeedKit.Seal(_seal, _sealGlow, P, Ground, Radius * 2.35f, Vol2Fs.Smooth(0.5f, 1f, t), Vol2FsRxFeedKit.Left(t, ChildExpireRel, _lifeS), outK, cam, Seed * 0.1f);
            _seal.Write(_sealMesh); _sealGlow.Write(_sealGlowMesh);
            if (ChildEndAt >= 0f && t > ChildEndAt + 0.3f) foreach (var ps in new[] { _wrap, _smoke, _embers }) if (ps != null && ps.isEmitting) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        protected override void OnChildEnd(Vol2MaterialEnd why, float t)
        {
            Note += " | earth child " + (why == Vol2MaterialEnd.Expired ? "cooled and crumbled" : "dried out");
            if (why == Vol2MaterialEnd.Expired) for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_dust, P + Random.insideUnitSphere * Radius + Vector3.up * 0.1f, Random.insideUnitSphere * 0.8f + Vector3.up * 0.3f);
        }
    }
}
