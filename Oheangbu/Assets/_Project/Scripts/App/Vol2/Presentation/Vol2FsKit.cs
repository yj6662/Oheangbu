using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App
{
    // D308-150 V10-V11 free showcase (2026-10-25, the user: "정말 아무 규칙없이 시안 뽑고 그 시안을 토대로 자유롭게 구현한 결과물도 한번만 보여주면 안돼?").
    // The toolbox of the showcase looks of 나 / 고 / 각: textures (procedural, Resources/FreeShowcase/fs_*), the ONE particle shader (Oheangbu/FreeShowcase/Particle,
    // premultiplied: additive light and covering ink in one), URP Lit bark / leaf materials, a Shuriken builder and the growing tube / ribbon mesh writers.
    // Presentation only: nothing here is read by a rule. One kit per installer (no statics holding Unity objects).

    /// <summary>A material recipe for the showcase particle shader.</summary>
    public struct Vol2FsMat
    {
        public string Tex, Ramp;
        public Color Tint, Edge;
        public float Intensity, AddMix, NoiseAmt, NoiseScale, Erode, EdgeWidth, Soft, Opacity, RampScale;
        /// <summary>2026-10-27 per-glyph polish: the fire flipbook's heat reach on the ramp (0 = the default 1; &lt; 1 = redder).</summary>
        public float FlipHeat;
        public Vector2 Scroll;
        public static Vol2FsMat Make(string tex, Color tint, float intensity, float addMix)
            => new Vol2FsMat { Tex = tex, Tint = tint, Intensity = intensity, AddMix = addMix, NoiseAmt = 0.5f, NoiseScale = 1f, Erode = 0.2f, EdgeWidth = 0.15f, Soft = 0.4f, Opacity = 1f, RampScale = 1.5f, Scroll = new Vector2(0f, -0.4f) };
        public string Key => Tex + "|" + Ramp + "|" + Tint + "|" + Edge + "|" + Intensity + "|" + AddMix + "|" + NoiseAmt + "|" + NoiseScale + "|" + Erode + "|" + EdgeWidth + "|" + Soft + "|" + Opacity + "|" + RampScale + "|" + Scroll + (FlipHeat > 0f ? "|fh" + FlipHeat : "");
    }

    public sealed class Vol2FsKit
    {
        public const string ShaderName = "Oheangbu/FreeShowcase/Particle", SolidShaderName = "Oheangbu/FreeShowcase/InkSolid";
        readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();
        readonly Dictionary<string, Texture2D> _tex = new Dictionary<string, Texture2D>();
        Shader _shader;
        public bool Ok { get; private set; }
        public string Problem { get; private set; } = "";

        public Vol2FsKit()
        {
            _shader = Shader.Find(ShaderName);
            if (_shader == null) { Problem = "no shader " + ShaderName; return; }
            if (Tex("fs_dot") == null) { Problem = "no textures under Resources/FreeShowcase"; return; }
            Ok = true;
        }

        public Texture2D Tex(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_tex.TryGetValue(name, out var t)) return t;
            t = Resources.Load<Texture2D>("FreeShowcase/" + name);
            _tex[name] = t;
            return t;
        }

        public Material Mat(Vol2FsMat m)
        {
            // 2026-10-27 ink-world pass: the world grade, once, here (graded before the cache key, so grade on / off never share a material)
            if (Vol2FsWorld.GradeOn)
            {
                m.Tint = Vol2FsWorld.Grade(m.Tint); m.Edge = Vol2FsWorld.Grade(m.Edge);
                if (m.Ramp == "fs_ramp_wood" && Tex("fs_ramp_wood_w") != null) m.Ramp = "fs_ramp_wood_w";
                if (m.Ramp == "fs_ramp_water" && Tex("fs_ramp_water_w") != null) m.Ramp = "fs_ramp_water_w";
            }
            string key = m.Key;
            if (_mats.TryGetValue(key, out var mat)) return mat;
            mat = new Material(_shader) { name = "Fs_" + m.Tex, hideFlags = HideFlags.DontSave };
            mat.SetTexture("_BaseMap", Tex(m.Tex));
            mat.SetTexture("_NoiseMap", Tex("fs_noise"));
            if (!string.IsNullOrEmpty(m.Ramp)) { mat.SetTexture("_RampMap", Tex(m.Ramp)); mat.SetFloat("_UseRamp", 1f); }
            mat.SetColor("_Tint", m.Tint); mat.SetFloat("_Intensity", m.Intensity); mat.SetFloat("_AddMix", m.AddMix);
            mat.SetFloat("_NoiseAmt", m.NoiseAmt); mat.SetFloat("_NoiseScale", m.NoiseScale); mat.SetVector("_NoiseScroll", new Vector4(m.Scroll.x, m.Scroll.y, 0f, 0f));
            mat.SetFloat("_Erode", Mathf.Max(0.01f, m.Erode)); mat.SetColor("_EdgeColor", m.Edge); mat.SetFloat("_EdgeWidth", Mathf.Max(0.01f, m.EdgeWidth));
            mat.SetFloat("_SoftFade", m.Soft); mat.SetFloat("_Opacity", m.Opacity); mat.SetFloat("_RampScale", m.RampScale);
            // 2026-10-27 common material pass: fire tongues and smoke / dust puffs carry the fire-smoke flipbook (used only by particle systems - Vol2Fs.Build
            // turns it on per renderer; a ribbon with the same material keeps the plain texture), water sheets draw as clear water.
            // r2: fire only - the smoke / dust / steam puffs keep their soft single sheet (r1 flipbook puffs read as flat torn sheets)
            bool fire = m.Tex == "fs_flame" && m.Ramp == "fs_ramp_fire";
            var flip = fire ? Tex(FlipTexName) : null;
            if (flip != null) { mat.SetTexture("_FlipTex", flip); mat.SetFloat("_FsKind", 1f); if (m.FlipHeat > 0f) mat.SetFloat("_FlipHeat", m.FlipHeat); }
            // 2026-10-27 per-glyph polish: fire stays orange-red - the batches' intensity 2 - 3 with a 1.6 - 1.7 ramp read yellow-white from the shoulder
            if (fire) { mat.SetFloat("_Intensity", Mathf.Min(m.Intensity, 1.8f)); mat.SetFloat("_RampScale", Mathf.Min(m.RampScale, 1.3f)); }
            if (m.Ramp == "fs_ramp_water" && (m.Tex == "fs_water" || m.Tex == "fs_swirl" || m.Tex == "fs_streak")) { mat.SetFloat("_WaterMode", 1f); mat.SetFloat("_WaterRim", WaterRim); if (Vol2FsWorld.GradeOn) mat.SetColor("_WaterDeep", new Color(0.025f, 0.10f, 0.15f, 1f)); }
            _mats[key] = mat;
            return mat;
        }

        /// <summary>2026-10-27 per-glyph polish: clear water's white fringe narrowed (FsParticle _WaterRim; 1 = the material pass).</summary>
        public const float WaterRim = 0.55f;
        public const string FlipTexName = "fs_fire_flip", MetalShaderName = "Oheangbu/FreeShowcase/Metal", StoneShaderName = "Oheangbu/FreeShowcase/Stone";

        /// <summary>2026-10-27 common material pass: showcase steel (Oheangbu/FreeShowcase/Metal - unlit chrome in the transparent queue, drawn after the ink
        /// wash so it stays silver on the red 적로 ground; see the shader's header). <paramref name="hot"/> 0..1 = a white-gold hot edge, <paramref name="accent"/> =
        /// a faint self-light. Falls back to null when the shader is missing (the caller keeps its URP Lit steel).</summary>
        public Material Metal(float bright = 1f, float hot = 0f, Color accent = default, Color tint = default)
        {
            if (tint == default) tint = Color.white;
            accent = Vol2FsWorld.Grade(accent);
            string key = "metal|" + (Vol2FsWorld.GradeOn ? "w|" : "") + bright + "|" + hot + "|" + accent + "|" + tint;
            if (_mats.TryGetValue(key, out var mat)) return mat;
            var sh = Shader.Find(MetalShaderName);
            if (sh == null) return null;
            mat = new Material(sh) { name = "Fs_Metal", hideFlags = HideFlags.DontSave };
            mat.SetTexture("_BaseMap", Tex("fs_steel")); mat.SetColor("_Tint", tint); mat.SetFloat("_Bright", bright); mat.SetFloat("_Hot", Mathf.Clamp01(hot)); mat.SetColor("_Accent", accent);
            mat.SetFloat("_Chrome", Vol2FsWorld.GradeOn ? 1f : 0f);      // 2026-10-27 ink-world pass: silver two-tone chrome (1) vs last round's dark world (0)
            _mats[key] = mat;
            return mat;
        }

        /// <summary>2026-10-27 common material pass: showcase stone (Oheangbu/FreeShowcase/Stone - opaque, wrapped sun + camera fill + rim, triplanar; the ink
        /// wash still grades it and draws its lines). Null when the shader is missing (the caller keeps its URP Lit stone).</summary>
        public Material Stone(string tex, Color tint, Color emission, string emissionTex)
        {
            tint = Vol2FsWorld.Grade(tint); emission = Vol2FsWorld.Grade(emission);
            string key = "stone|" + tex + "|" + tint + "|" + emission + "|" + emissionTex;
            if (_mats.TryGetValue(key, out var mat)) return mat;
            var sh = Shader.Find(StoneShaderName);
            if (sh == null) return null;
            mat = new Material(sh) { name = "Fs_Stone_" + tex, hideFlags = HideFlags.DontSave };
            mat.SetTexture("_BaseMap", Tex(tex)); mat.SetColor("_BaseColor", tint);
            if (emission.maxColorComponent > 0.001f)
            {
                // the batches' flat "self-light" (a backlit-black workaround: no mask, or the rock itself as the mask) is mostly the fill's job now
                bool vein = !string.IsNullOrEmpty(emissionTex) && emissionTex != tex && emissionTex != "fs_rock";
                mat.SetTexture("_EmissionMap", vein ? Tex(emissionTex) : Texture2D.whiteTexture);
                mat.SetColor("_EmissionColor", vein ? emission : emission * 0.35f);
            }
            _mats[key] = mat;
            return mat;
        }

        /// <summary>URP Lit bark (tinted), with an emissive crack mask when <paramref name="glow"/> has any colour.</summary>
        public Material Bark(Color tint, Color glow, float smooth = 0.18f, string tex = "fs_bark", string glowTex = "fs_vein")
        {
            tint = Vol2FsWorld.Grade(tint); glow = Vol2FsWorld.Grade(glow);
            string key = "bark|" + tint + "|" + glow + "|" + tex + "|" + glowTex;
            if (_mats.TryGetValue(key, out var mat)) return mat;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            mat = new Material(sh) { name = "Fs_Bark", hideFlags = HideFlags.DontSave };
            mat.SetTexture("_BaseMap", Tex(tex)); mat.SetColor("_BaseColor", tint); mat.SetFloat("_Smoothness", smooth); mat.SetFloat("_Metallic", 0f);
            if (glow.maxColorComponent > 0.001f)
            {
                mat.SetTexture("_EmissionMap", Tex(glowTex)); mat.SetColor("_EmissionColor", glow); mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            _mats[key] = mat;
            return mat;
        }

        /// <summary>Solid (alpha-clipped, depth-writing) ink with a burning rim: Oheangbu/FreeShowcase/InkSolid.</summary>
        public Material InkSolid(string tex, Color tint, Color edge, float edgeWidth = 0.12f, float noiseAmt = 0.5f, float cutoff = 0.35f, float noiseScale = 1f)
        {
            tint = Vol2FsWorld.Grade(tint); edge = Vol2FsWorld.Grade(edge);
            string key = "solid|" + tex + "|" + tint + "|" + edge + "|" + edgeWidth + "|" + noiseAmt + "|" + cutoff + "|" + noiseScale;
            if (_mats.TryGetValue(key, out var mat)) return mat;
            var sh = Shader.Find(SolidShaderName);
            if (sh == null) sh = _shader;
            mat = new Material(sh) { name = "Fs_InkSolid", hideFlags = HideFlags.DontSave };
            mat.SetTexture("_BaseMap", Tex(tex)); mat.SetTexture("_NoiseMap", Tex("fs_noise"));
            mat.SetColor("_Tint", tint); mat.SetColor("_EdgeColor", edge); mat.SetFloat("_EdgeWidth", edgeWidth); mat.SetFloat("_NoiseAmt", noiseAmt); mat.SetFloat("_Cutoff", cutoff); mat.SetFloat("_NoiseScale", noiseScale);
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            _mats[key] = mat;
            return mat;
        }

        /// <summary>URP Lit two-sided alpha-clipped leaves (the leaf atlas: 2 x 2 cells).</summary>
        public Material Leaf(Color tint, Color glow)
        {
            tint = Vol2FsWorld.Grade(tint); glow = Vol2FsWorld.Grade(glow);
            string key = "leaf|" + tint + "|" + glow;
            if (_mats.TryGetValue(key, out var mat)) return mat;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            mat = new Material(sh) { name = "Fs_Leaf", hideFlags = HideFlags.DontSave };
            // 2026-10-27 common material pass: batches 0 - 4 leaves read pale cream (the sky's sheen on a 0.3 smooth card, then the ink wash maps the bright
            // value toward paper). A deeper jade-green albedo with no sheen keeps a green the wash's 비색 band keeps; a little jade self-light stands in for
            // light through the leaf (backlit cards never go black).
            // r2 (r1 read olive-khaki): less red, a little more blue - toward the jade / 비색 side the wash keeps
            var deep = new Color(tint.r * 0.42f, tint.g * 0.82f, tint.b * 1.1f, tint.a);
            mat.SetTexture("_BaseMap", Tex("fs_leaf")); mat.SetColor("_BaseColor", deep); mat.SetFloat("_Smoothness", 0.06f);
            mat.SetFloat("_SpecularHighlights", 0f); mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            mat.SetFloat("_EnvironmentReflections", 0f); mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            mat.SetFloat("_AlphaClip", 1f); mat.SetFloat("_Cutoff", 0.45f); mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Cull", 0f); mat.doubleSidedGI = true;
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            // r2: a uniform jade self-light (r1 used the yellow-green leaf map as the emission mask: it carried no blue)
            var through = new Color(0.012f, 0.07f, 0.05f);
            mat.SetColor("_EmissionColor", new Color(glow.r * 0.45f, glow.g * 0.75f, glow.b * 0.75f + glow.g * 0.25f) + through); mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            _mats[key] = mat;
            return mat;
        }

        /// <summary>batch 1: any other material, made once per key and owned (destroyed) by the kit.</summary>
        public Material Custom(string key, System.Func<Material> make)
        {
            if (_mats.TryGetValue("custom|" + key, out var mat) && mat != null) return mat;
            mat = make();
            if (mat != null) mat.hideFlags = HideFlags.DontSave;
            _mats["custom|" + key] = mat;
            return mat;
        }

        /// <summary>batch 1: meshes made once per key (rock, slab, blade, shard), owned by the kit.</summary>
        public Mesh SharedMesh(string key, System.Func<Mesh> make)
        {
            if (_meshes.TryGetValue(key, out var m) && m != null) return m;
            m = make(); if (m != null) m.hideFlags = HideFlags.DontSave;
            _meshes[key] = m;
            return m;
        }
        readonly Dictionary<string, Mesh> _meshes = new Dictionary<string, Mesh>();
        /// <summary>Is this one of the kit's shared meshes (a cast's Dispose must not destroy it)?</summary>
        public bool Owns(Mesh m) { foreach (var x in _meshes.Values) if (x == m) return true; return false; }

        public void Dispose()
        {
            foreach (var m in _mats.Values) if (m != null) Object.Destroy(m);
            _mats.Clear();
            foreach (var m in _meshes.Values) if (m != null) Object.Destroy(m);
            _meshes.Clear();
        }
    }

    /// <summary>A particle system description; <see cref="Vol2Fs.Build"/> makes it (stopped, playOnAwake off).</summary>
    public sealed class Vol2FsPs
    {
        public Material Mat;
        public float Life0 = 0.5f, Life1 = 1f, Speed0 = 0f, Speed1 = 1f, Size0 = 0.3f, Size1 = 0.6f;
        public Color C0 = Color.white, C1 = Color.white;
        public int Burst; public float Rate, RatePerMeter; public float Duration = 1f; public bool Loop;
        public ParticleSystemShapeType Shape = ParticleSystemShapeType.Sphere; public float Radius = 0.1f, Angle = 25f, Arc = 360f; public Vector3 ShapeScale = Vector3.one, ShapeRot;
        public float Gravity, Drag, Noise, NoiseFreq = 1f;
        public bool World = true;
        /// <summary>Size over life: start -> peak (at <see cref="SizePeakAt"/>) -> end, multipliers.</summary>
        public float SizeStart = 1f, SizePeak = 1f, SizeEnd = 1f, SizePeakAt = 0.3f;
        /// <summary>Alpha over life (the shader's erosion: 1 = whole, 0 = eaten).</summary>
        public float AlphaHoldTo = 0.25f;
        public Color EndTint = Color.white;
        public int Tiles = 1;
        public bool Stretch; public float StretchVel = 0.06f, StretchLen = 1f;
        public bool Horizontal;
        public float Spin, RandRot = 360f;
        public Vector3 VelOverLife; public float Radial, Orbital;
        public int Max = 600;
        public float Inherit;
        public int SortFudge;
        /// <summary>batch 1: mesh particles (rock chunks, steel shards) - tumbling in 3D, lit when <see cref="Mat"/> is a lit material.</summary>
        public Mesh Mesh;
        /// <summary>2026-10-27 per-glyph polish: this system never plays the kit's fire flipbook (a fire material keeps its plain tongue sheet here).</summary>
        public bool NoFlip;
        /// <summary>2026-10-27 per-glyph polish: the flipbook's size multiplier for this system (0 = the kit's default).</summary>
        public float FlipScale;
        /// <summary>2026-10-27 per-glyph polish: height / width of each billboard (1 = square; 2 = a tall licking tongue).</summary>
        public float Aspect = 1f;
    }

    public static class Vol2Fs
    {
        static readonly List<ParticleSystemVertexStream> Streams = new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV, ParticleSystemVertexStream.StableRandomX };
        static readonly List<ParticleSystemVertexStream> MeshStreams = new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV };
        /// <summary>2026-10-27: size of a flipbook fire / puff particle against the old single-sheet one (the flipbook cells keep a margin).</summary>
        public const float FlipFireScale = 1.45f, FlipPuffScale = 1.2f;
        /// <summary>2026-10-27 per-glyph polish: the automatic licking tongue of a fire system (height / width, width against its written size).</summary>
        public const float AutoLickAspect = 2.1f, AutoLickWidth = 0.72f;

        public static ParticleSystem Build(Vol2FsPs d, Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.duration = Mathf.Max(0.05f, d.Duration); main.loop = d.Loop;
            main.startLifetime = new ParticleSystem.MinMaxCurve(d.Life0, d.Life1);
            main.startSpeed = new ParticleSystem.MinMaxCurve(d.Speed0, d.Speed1);
            main.startSize = new ParticleSystem.MinMaxCurve(d.Size0, d.Size1);
            if (d.Mesh == null && Mathf.Abs(d.Aspect - 1f) > 0.01f)
            {
                // 2026-10-27 per-glyph polish: tall billboards (licking flame tongues, water jets); EmitV keeps the ratio (Vol2FsB1.EmitV)
                main.startSize3D = true;
                main.startSizeX = new ParticleSystem.MinMaxCurve(d.Size0, d.Size1);
                main.startSizeY = new ParticleSystem.MinMaxCurve(d.Size0 * d.Aspect, d.Size1 * d.Aspect);
                main.startSizeZ = new ParticleSystem.MinMaxCurve(d.Size0, d.Size1);
            }
            // 2026-10-27 ink-world pass: the world grade on the system's colours (local copies: a description built once is never graded twice)
            Color gC0 = Vol2FsWorld.Grade(d.C0), gC1 = Vol2FsWorld.Grade(d.C1), gEnd = Vol2FsWorld.Grade(d.EndTint);
            main.startColor = new ParticleSystem.MinMaxGradient(gC0, gC1);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, d.RandRot * Mathf.Deg2Rad);
            main.gravityModifier = d.Gravity;
            main.simulationSpace = d.World ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.maxParticles = d.Max;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission;
            em.rateOverTime = d.Rate; em.rateOverDistance = d.RatePerMeter;
            if (d.Burst > 0) em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)d.Burst) }); else em.SetBursts(new ParticleSystem.Burst[0]);
            var sh = ps.shape;
            sh.enabled = true; sh.shapeType = d.Shape; sh.radius = d.Radius; sh.angle = d.Angle; sh.arc = d.Arc; sh.scale = d.ShapeScale; sh.rotation = d.ShapeRot;
            sh.radiusThickness = 1f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(gEnd, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, d.AlphaHoldTo), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, d.SizeStart), new Keyframe(Mathf.Clamp(d.SizePeakAt, 0.01f, 0.99f), d.SizePeak), new Keyframe(1f, d.SizeEnd)));
            if (d.Spin != 0f) { var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-d.Spin * Mathf.Deg2Rad, d.Spin * Mathf.Deg2Rad); }
            if (d.VelOverLife != Vector3.zero || d.Radial != 0f || d.Orbital != 0f)
            {
                var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
                v.x = new ParticleSystem.MinMaxCurve(d.VelOverLife.x); v.y = new ParticleSystem.MinMaxCurve(d.VelOverLife.y); v.z = new ParticleSystem.MinMaxCurve(d.VelOverLife.z);
                if (d.Radial != 0f) v.radial = new ParticleSystem.MinMaxCurve(d.Radial);
                if (d.Orbital != 0f) { v.space = ParticleSystemSimulationSpace.Local; v.orbitalY = new ParticleSystem.MinMaxCurve(d.Orbital); }
            }
            if (d.Drag > 0f) { var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.drag = d.Drag; lim.multiplyDragByParticleSize = false; lim.multiplyDragByParticleVelocity = true; }
            if (d.Noise > 0f) { var n = ps.noise; n.enabled = true; n.strength = d.Noise; n.frequency = d.NoiseFreq; n.scrollSpeed = 0.6f; n.quality = ParticleSystemNoiseQuality.Medium; }
            if (d.Inherit != 0f) { var inh = ps.inheritVelocity; inh.enabled = true; inh.mode = ParticleSystemInheritVelocityMode.Initial; inh.curve = d.Inherit; }
            if (d.Tiles > 1)
            {
                var ts = ps.textureSheetAnimation; ts.enabled = true; ts.mode = ParticleSystemAnimationMode.Grid; ts.numTilesX = d.Tiles; ts.numTilesY = d.Tiles;
                ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f); ts.startFrame = new ParticleSystem.MinMaxCurve(0f, d.Tiles * d.Tiles - 0.01f);
            }
            // 2026-10-27 common material pass: a fire / puff material that carries the flipbook plays it over each particle's life (8 x 8, birth -> burn ->
            // smoke), drawn a little bigger (the cells keep a margin; batches 0 - 4 fire read small and sparse). The material stays shared: the flipbook is
            // switched on for this renderer only (property block), so ribbons with the same material keep the plain texture.
            // 2026-10-27 per-glyph polish: a fire particle system that asked for nothing (no FlipScale, not NoFlip) plays licking tongues instead of the
            // flipbook - the glyph sheets showed the flipbook as yellow paper cut-outs on every fire glyph (노 누 너 낙 녹 넉 눅 난 넌 논 눈); tall upright
            // billboards of the plain tongue sheet, a little narrower. A glyph that wants the flipbook (나's fireball, 노's breath) sets FlipScale.
            bool fireMat = d.Mesh == null && d.Mat != null && d.Mat.HasProperty("_FsKind") && d.Mat.GetTexture("_FlipTex") != null && Mathf.RoundToInt(d.Mat.GetFloat("_FsKind")) == 1;
            if (fireMat && !d.NoFlip && d.FlipScale <= 0f && !d.Stretch)
            {
                d.NoFlip = true;
                if (Mathf.Abs(d.Aspect - 1f) < 0.01f)
                {
                    main.startSize3D = true;
                    main.startSizeX = new ParticleSystem.MinMaxCurve(d.Size0, d.Size1);
                    main.startSizeY = new ParticleSystem.MinMaxCurve(d.Size0 * AutoLickAspect, d.Size1 * AutoLickAspect);
                    main.startSizeZ = new ParticleSystem.MinMaxCurve(d.Size0, d.Size1);
                    main.startRotation = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
                    sz.sizeMultiplier = AutoLickWidth;
                }
            }
            int flipKind = !d.NoFlip && d.Mesh == null && d.Mat != null && d.Mat.HasProperty("_FsKind") && d.Mat.GetTexture("_FlipTex") != null ? Mathf.RoundToInt(d.Mat.GetFloat("_FsKind")) : 0;
            if (flipKind > 0)
            {
                var ts = ps.textureSheetAnimation; ts.enabled = true; ts.mode = ParticleSystemAnimationMode.Grid; ts.numTilesX = 8; ts.numTilesY = 8;
                ts.timeMode = ParticleSystemAnimationTimeMode.Lifetime; ts.cycleCount = 1;
                ts.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                ts.startFrame = new ParticleSystem.MinMaxCurve(0f, flipKind == 1 ? 3.99f : 1.99f);      // a slightly different birth per particle
                sz.sizeMultiplier = d.FlipScale > 0f ? d.FlipScale : flipKind == 1 ? FlipFireScale : FlipPuffScale;
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = d.Mat;
            r.renderMode = d.Stretch ? ParticleSystemRenderMode.Stretch : d.Horizontal ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
            if (d.Stretch) { r.velocityScale = d.StretchVel; r.lengthScale = d.StretchLen; }
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.sortingFudge = d.SortFudge;
            r.maxParticleSize = 3f;
            r.SetActiveVertexStreams(Streams);
            if (flipKind > 0) { var mpb = new MaterialPropertyBlock(); r.GetPropertyBlock(mpb); mpb.SetFloat("_Flip", 1f); r.SetPropertyBlock(mpb); }
            if (d.Mesh != null)
            {
                r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = d.Mesh; r.alignment = ParticleSystemRenderSpace.World;
                r.SetActiveVertexStreams(MeshStreams);
                main.startRotation3D = true;
                float rr = d.RandRot * Mathf.Deg2Rad;
                main.startRotationX = new ParticleSystem.MinMaxCurve(0f, rr); main.startRotationY = new ParticleSystem.MinMaxCurve(0f, rr); main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, rr);
                if (d.Spin != 0f)
                {
                    var rot = ps.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
                    float s = d.Spin * Mathf.Deg2Rad;
                    rot.x = new ParticleSystem.MinMaxCurve(-s, s); rot.y = new ParticleSystem.MinMaxCurve(-s, s); rot.z = new ParticleSystem.MinMaxCurve(-s, s);
                }
                r.receiveShadows = true;
            }
            return ps;
        }

        public static Light PointLight(Transform parent, string name, Color c, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = Vol2FsWorld.Grade(c); l.range = range; l.intensity = 0f; l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
            return l;
        }

        public static TrailRenderer Trail(Transform parent, string name, Material mat, float time, float width, AnimationCurve widthCurve, Gradient color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TrailRenderer>();
            t.sharedMaterial = mat; t.time = time; t.widthMultiplier = width; t.widthCurve = widthCurve; t.colorGradient = color;
            t.minVertexDistance = 0.05f; t.numCapVertices = 2; t.textureMode = LineTextureMode.Stretch; t.alignment = LineAlignment.View;
            t.shadowCastingMode = ShadowCastingMode.Off; t.receiveShadows = false; t.emitting = false;
            return t;
        }

        public static Gradient Fade(Color c, float holdTo = 0.2f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, holdTo), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        public static MeshRenderer MeshObject(Transform parent, string name, Material mat, out Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = true; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        public static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / Mathf.Max(1e-5f, b - a)); return t * t * (3f - 2f * t); }
        public static float EaseOutBack(float t) { t = Mathf.Clamp01(t); float c1 = 1.70158f, c3 = c1 + 1f; return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f); }
        public static float Hash(int a, int b) { unchecked { uint h = (uint)(a * 374761393 + b * 668265263); h = (h ^ (h >> 13)) * 1274126177u; return ((h ^ (h >> 16)) & 0xffffff) / 16777216f; } }
    }

    /// <summary>Growing tubes (branches, shoots, roots) written into one mesh. Each tube: a Catmull-Rom curve through control points, revealed up to
    /// grow x length with a sharp tip; bark uv (u around, v along).</summary>
    public sealed class Vol2FsTubes
    {
        public sealed class Tube
        {
            public Vector3[] Ctrl; public float R0, R1, Grow, Knots, Sharp = 0.25f; public int Sides = 7;
            /// <summary>Node rings (bamboo): every this many metres the radius bulges (0 = none).</summary>
            public float NodeEvery;
            public float Len;
            public readonly List<Vector3> Pts = new List<Vector3>();
            public readonly List<float> Acc = new List<float>();
        }

        public readonly List<Tube> Tubes = new List<Tube>();
        /// <summary>2026-10-27: girth of a long thin tube (vine / snare / branch) against its written radius.</summary>
        public const float VineGirth = 1.3f;
        readonly List<Vector3> _v = new List<Vector3>(4096), _n = new List<Vector3>(4096);
        readonly List<Vector2> _uv = new List<Vector2>(4096);
        readonly List<int> _i = new List<int>(16384);

        public Tube Add(Vector3[] ctrl, float r0, float r1, int samples = 14)
        {
            var t = new Tube { Ctrl = ctrl, R0 = r0, R1 = r1 };
            for (int s = 0; s <= samples; s++) t.Pts.Add(CatmullRom(ctrl, s / (float)samples));
            float acc = 0f; t.Acc.Add(0f);
            for (int s = 1; s < t.Pts.Count; s++) { acc += Vector3.Distance(t.Pts[s - 1], t.Pts[s]); t.Acc.Add(acc); }
            t.Len = acc;
            Tubes.Add(t);
            return t;
        }

        public static Vector3 CatmullRom(Vector3[] c, float u)
        {
            int n = c.Length;
            if (n == 1) return c[0];
            float f = u * (n - 1); int i = Mathf.Min(n - 2, Mathf.FloorToInt(f)); float t = f - i;
            Vector3 p0 = c[Mathf.Max(0, i - 1)], p1 = c[i], p2 = c[i + 1], p3 = c[Mathf.Min(n - 1, i + 2)];
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>The point of a tube at its grown tip (for leaves / sparks).</summary>
        public static Vector3 TipOf(Tube t) { Sample(t, Mathf.Clamp01(t.Grow) * t.Len, out var p, out _); return p; }

        static void Sample(Tube t, float at, out Vector3 p, out Vector3 dir)
        {
            int k = 1;
            while (k < t.Acc.Count - 1 && t.Acc[k] < at) k++;
            float seg = Mathf.Max(1e-5f, t.Acc[k] - t.Acc[k - 1]);
            float f = Mathf.Clamp01((at - t.Acc[k - 1]) / seg);
            p = Vector3.Lerp(t.Pts[k - 1], t.Pts[k], f);
            dir = (t.Pts[k] - t.Pts[k - 1]).normalized;
        }

        public void Write(Mesh mesh)
        {
            _v.Clear(); _n.Clear(); _uv.Clear(); _i.Clear();
            foreach (var t in Tubes)
            {
                float grown = Mathf.Clamp01(t.Grow) * t.Len;
                if (grown < 0.01f) continue;
                int rings = Mathf.Max(3, Mathf.CeilToInt(grown / 0.07f));
                int sides = t.Sides;
                int baseV = _v.Count;
                Vector3 prevN = Vector3.zero;
                // 2026-10-27 common material pass: long thin tubes (vines, snares, branches; not thorns, not the thick cage ribs / trunks) read as thorn
                // spikes from behind the caster - thicker, and they keep more of their girth toward the tip
                bool vine = t.Len > 0.6f && t.R0 < 0.12f;
                float r0 = vine ? t.R0 * VineGirth : t.R0, r1 = vine ? Mathf.Max(t.R1 * VineGirth, r0 * 0.5f) : t.R1;
                for (int r = 0; r <= rings; r++)
                {
                    float a = grown * r / rings;
                    Sample(t, a, out var p, out var dir);
                    float along = a / Mathf.Max(1e-4f, t.Len);
                    float rad = Mathf.Lerp(r0, r1, along);
                    float toTip = (grown - a) / Mathf.Max(0.01f, t.Sharp);       // the growing tip is a sharp point
                    rad *= Mathf.Clamp01(toTip);
                    if (t.Knots > 0f) rad *= 1f + t.Knots * (Mathf.PerlinNoise(a * 3.1f, t.R0 * 91f) - 0.5f);
                    if (t.NodeEvery > 0f) { float m = Mathf.Repeat(a, t.NodeEvery) / t.NodeEvery; rad *= 1f + 0.22f * Mathf.Exp(-m * m * 220f) + 0.22f * Mathf.Exp(-(1 - m) * (1 - m) * 220f); }
                    // a frame that does not twist: the normal is carried from ring to ring
                    Vector3 nrm = prevN == Vector3.zero ? Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized : Vector3.ProjectOnPlane(prevN, dir).normalized;
                    if (nrm == Vector3.zero) nrm = Vector3.Cross(dir, Vector3.right).normalized;
                    prevN = nrm;
                    Vector3 bin = Vector3.Cross(dir, nrm);
                    for (int s = 0; s <= sides; s++)
                    {
                        float ang = s / (float)sides * Mathf.PI * 2f;
                        Vector3 o = nrm * Mathf.Cos(ang) + bin * Mathf.Sin(ang);
                        _v.Add(p + o * rad); _n.Add(o); _uv.Add(new Vector2(s / (float)sides, a * 1.6f));
                    }
                }
                int stride = sides + 1;
                for (int r = 0; r < rings; r++)
                    for (int s = 0; s < sides; s++)
                    {
                        int a0 = baseV + r * stride + s, a1 = a0 + 1, b0 = a0 + stride, b1 = b0 + 1;
                        _i.Add(a0); _i.Add(b0); _i.Add(a1); _i.Add(a1); _i.Add(b0); _i.Add(b1);
                    }
            }
            mesh.Clear();
            mesh.indexFormat = _v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_v); mesh.SetNormals(_n); mesh.SetUVs(0, _uv); mesh.SetTriangles(_i, 0);
            mesh.RecalculateBounds();
        }
    }

    /// <summary>Camera-facing quads / strips with vertex colour (alpha = erosion life) for the showcase particle shader: ink strokes bursting from a point,
    /// a sweeping ink arc, a ground strip. uv: u along, v across; uv.z = a per-strip random (the shader's noise offset).</summary>
    public sealed class Vol2FsStrips
    {
        readonly List<Vector3> _v = new List<Vector3>(2048);
        readonly List<Color> _c = new List<Color>(2048);
        readonly List<Vector3> _uv = new List<Vector3>(2048);
        readonly List<int> _i = new List<int>(4096);

        public void Clear() { _v.Clear(); _c.Clear(); _uv.Clear(); _i.Clear(); }

        /// <summary>A polyline strip; <paramref name="side"/> gives the width direction per point (null = facing the camera).</summary>
        public void Strip(IList<Vector3> pts, IList<float> widths, IList<Vector3> side, Color color, IList<float> alphas, float random, Vector3 camPos, float u0 = 0f, float u1 = 1f, float v0 = 0f, float v1 = 1f)
        {
            int n = pts.Count;
            if (n < 2) return;
            // batch 6: a strip with a non-finite point is skipped whole (the lab's bodies can report non-finite renderer bounds for a frame: the mesh's AABB
            // assert then fired every frame - 12 088 "Converting invalid MinMaxAABB" in the lab run 20261027_082713)
            for (int k = 0; k < n; k++) { var q = pts[k]; float wq = widths[k]; if (float.IsNaN(q.x + q.y + q.z + wq) || float.IsInfinity(q.x + q.y + q.z + wq)) return; }
            int b = _v.Count;
            for (int k = 0; k < n; k++)
            {
                Vector3 tan = (pts[Mathf.Min(n - 1, k + 1)] - pts[Mathf.Max(0, k - 1)]).normalized;
                Vector3 s = side != null ? side[k] : Vector3.Cross(tan, (camPos - pts[k]).normalized).normalized;
                float w = widths[k] * 0.5f;
                float u = Mathf.Lerp(u0, u1, k / (float)(n - 1));
                var c = color; c.a = alphas != null ? alphas[k] : color.a;
                _v.Add(pts[k] - s * w); _c.Add(c); _uv.Add(new Vector3(u, v0, random));
                _v.Add(pts[k] + s * w); _c.Add(c); _uv.Add(new Vector3(u, v1, random));
            }
            for (int k = 0; k < n - 1; k++)
            {
                int a0 = b + k * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                _i.Add(a0); _i.Add(b0); _i.Add(a1); _i.Add(a1); _i.Add(b0); _i.Add(b1);
            }
        }

        /// <summary>A flat quad (ground decal) centred at <paramref name="c"/>, rotated by <paramref name="yaw"/>.</summary>
        public void Quad(Vector3 c, Vector3 right, Vector3 fwd, float size, Color color, float random)
        {
            int b = _v.Count; float h = size * 0.5f;
            _v.Add(c - right * h - fwd * h); _v.Add(c + right * h - fwd * h); _v.Add(c - right * h + fwd * h); _v.Add(c + right * h + fwd * h);
            for (int k = 0; k < 4; k++) _c.Add(color);
            _uv.Add(new Vector3(0, 0, random)); _uv.Add(new Vector3(1, 0, random)); _uv.Add(new Vector3(0, 1, random)); _uv.Add(new Vector3(1, 1, random));
            _i.Add(b); _i.Add(b + 2); _i.Add(b + 1); _i.Add(b + 1); _i.Add(b + 2); _i.Add(b + 3);
        }

        /// <summary>2026-10-27 ink-world pass: a ground quad that samples one cell of a 2 x 2 sheet (cell 0..3, row 0 = the bottom row of the texture).</summary>
        public void QuadCell(Vector3 c, Vector3 right, Vector3 fwd, float size, Color color, float random, int cell)
        {
            int b = _v.Count; float h = size * 0.5f;
            float cu = (cell % 2) * 0.5f, cv = (cell / 2 % 2) * 0.5f;
            _v.Add(c - right * h - fwd * h); _v.Add(c + right * h - fwd * h); _v.Add(c - right * h + fwd * h); _v.Add(c + right * h + fwd * h);
            for (int k = 0; k < 4; k++) _c.Add(color);
            _uv.Add(new Vector3(cu, cv, random)); _uv.Add(new Vector3(cu + 0.5f, cv, random)); _uv.Add(new Vector3(cu, cv + 0.5f, random)); _uv.Add(new Vector3(cu + 0.5f, cv + 0.5f, random));
            _i.Add(b); _i.Add(b + 2); _i.Add(b + 1); _i.Add(b + 1); _i.Add(b + 2); _i.Add(b + 3);
        }

        public void Write(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(_v); mesh.SetColors(_c); mesh.SetUVs(0, _uv); mesh.SetTriangles(_i, 0);
            mesh.RecalculateBounds();
        }
    }

    /// <summary>Leaf cards (alpha-clipped URP Lit): each card a quad at a point, facing a direction, scaled 0..1 by its own grow.</summary>
    public sealed class Vol2FsLeaves
    {
        public struct Leaf { public Vector3 Pos, Normal, Up; public float Size, Grow; public int Cell; }
        public readonly List<Leaf> Items = new List<Leaf>();
        readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<int> _i = new List<int>();

        /// <summary>2026-10-27 common material pass: leaf cards are drawn this much bigger, each with a second card crossed at ~70 degrees (batches 0 - 4: small,
        /// sparse cards that vanish edge-on from behind the caster).</summary>
        public const float SizeScale = 1.3f;

        public void Write(Mesh mesh)
        {
            _v.Clear(); _n.Clear(); _uv.Clear(); _i.Clear();
            foreach (var l in Items)
            {
                if (l.Grow <= 0.01f) continue;
                float s = l.Size * Mathf.Clamp01(l.Grow) * SizeScale;
                Vector3 up = l.Up.normalized, nrm = l.Normal.normalized;
                Card(l.Pos, nrm, up, s, l.Cell);
                // the crossed card: turned about the leaf's own axis, a little smaller, the next cell of the atlas
                Vector3 n2 = Quaternion.AngleAxis(70f, up) * nrm;
                Card(l.Pos + up * s * 0.08f, n2, up, s * 0.85f, l.Cell + 1);
            }
            mesh.Clear();
            mesh.SetVertices(_v); mesh.SetNormals(_n); mesh.SetUVs(0, _uv); mesh.SetTriangles(_i, 0);
            mesh.RecalculateBounds();
        }

        void Card(Vector3 pos, Vector3 normal, Vector3 up, float s, int cell)
        {
            Vector3 right = Vector3.Cross(normal, up).normalized;
            if (right.sqrMagnitude < 1e-6f) return;
            int b = _v.Count;
            Vector3 p0 = pos - right * s * 0.5f, p1 = pos + right * s * 0.5f;
            _v.Add(p0); _v.Add(p1); _v.Add(p0 + up * s); _v.Add(p1 + up * s);
            for (int k = 0; k < 4; k++) _n.Add(normal);
            float cu = (cell % 2) * 0.5f, cv = (cell / 2 % 2) * 0.5f;
            _uv.Add(new Vector2(cu, cv)); _uv.Add(new Vector2(cu + 0.5f, cv)); _uv.Add(new Vector2(cu, cv + 0.5f)); _uv.Add(new Vector2(cu + 0.5f, cv + 0.5f));
            _i.Add(b); _i.Add(b + 2); _i.Add(b + 1); _i.Add(b + 1); _i.Add(b + 2); _i.Add(b + 3);
        }
    }
}
