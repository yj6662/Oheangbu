using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // #300 (SPEC-PLAYER-FEEL-300 D): the five basic attack flight bodies rebuilt from the free Gabriel Aguiar
    // "Free Quick Effects Vol.1" textures (already in the project; its built-in-pipeline materials are not used).
    // Ink rules: bodies and trails are alpha-blended ink/pigment; additive glow only as a momentary launch/contact flash
    // (나's flame keeps the user-approved burning glow, SPEC-SPELL-VFX120 2026-09-10). Colours follow ART-COLOR via the
    // original profiles' Pigment/Accent/Ink. Originals are copied, never edited.
    //   build    — materials, PF_Bolt300_* prefabs, profile + effect copies under Art/SpellVFX120/Bolt300
    //   connect  — the main visual set's 가·나·마·사·아 use the #300 effects (previous entries recorded)
    //   revert   — restore the recorded entries
    //   status
    public static class Bolt300Build
    {
        const string Root = "Assets/_Project/Art/SpellVFX120/Bolt300";
        const string Tex = "Assets/GabrielAguiarProductions/FreeQuickEffectsVol1/Textures/";
        const string VisualSet = "Assets/_Project/Art/World/Architecture296/Data/9f55ff4897c35bf408434d2fd4cd5dbc_278c3cdf45b3ab24fb93a8f33e2264e2_9ba170fd23c53dc418707872eb7b2056_SpellVisualSet_120.asset";
        static string Record => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/SpellVFX120/Bolt300/visualset-before.json"));

        sealed class Spell { public string Glyph, Key, Effect; public float Arc, Curve, Spin, Scale = 1f; }
        static readonly Spell[] Spells =
        {
            new Spell { Glyph = "가", Key = "ga", Effect = "001_AC00", Arc = .15f, Scale = 1.4f },
            new Spell { Glyph = "나", Key = "na", Effect = "025_B098", Arc = .3f, Scale = 1.2f },
            new Spell { Glyph = "마", Key = "ma", Effect = "049_B9C8", Arc = 1.9f, Spin = 480f },
            new Spell { Glyph = "사", Key = "sa", Effect = "073_C0AC", Scale = 1.3f },
            new Spell { Glyph = "아", Key = "a", Effect = "097_C544", Arc = .5f, Curve = 1.3f, Scale = 1.4f },
        };

        public static string Run(string command)
        {
            switch (command)
            {
                case "build": return Build();
                case "connect": return Connect();
                case "revert": return Revert();
                case "status": return Status();
                case "after-fog": return AfterFog();
                default: throw new ArgumentException("Bolt300Build: build | connect | revert | status");
            }
        }

        // ---------------------------------------------------------------- materials

        internal static Material Mat(string name, string texture, bool additive) => Mat(name, texture, additive, Tex, Root + "/Materials");
        internal static Material Mat(string name, string texture, bool additive, string texFolder, string matFolder)
        {
            Directory.CreateDirectory(matFolder);
            string path = matFolder + "/" + name + ".mat";
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) throw new Exception("URP Particles/Unlit shader not found");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texFolder + texture));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);                       // transparent
            m.SetFloat("_Blend", additive ? 2f : 0f);         // additive | alpha
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_ColorMode", 0f);                     // multiply vertex colour
            m.SetFloat("_SoftParticlesEnabled", 1f);
            m.SetFloat("_SoftParticlesNearFadeDistance", 0f);
            m.SetFloat("_SoftParticlesFarFadeDistance", .35f);
            // the inspector's own keyword/blend-state setup (URP editor assembly, reached by reflection)
            var baseGui = Type.GetType("UnityEditor.BaseShaderGUI, Unity.RenderPipelines.Universal.Editor");
            var particleGui = Type.GetType("UnityEditor.Rendering.Universal.ShaderGUI.ParticleGUI, Unity.RenderPipelines.Universal.Editor");
            var setKeywords = baseGui?.GetMethod("SetMaterialKeywords", BindingFlags.Public | BindingFlags.Static);
            var particleKeywords = particleGui?.GetMethod("SetMaterialKeywords", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Material) }, null);
            if (setKeywords == null || particleKeywords == null) throw new Exception("URP ShaderGUI helpers not found");
            setKeywords.Invoke(null, new object[] { m, null, (Action<Material>)Delegate.CreateDelegate(typeof(Action<Material>), particleKeywords) });
            EditorUtility.SetDirty(m);
            return m;
        }

        sealed class Mats { public Material Blot, Splat, Dot, Ring, Swirl, Glow, Flame, Trail, TrailGlow, GlowSplat; }
        static Mats Materials() => new Mats
        {
            Blot = Mat("M_B300_Blot", "Flame02.png", false),
            Splat = Mat("M_B300_Splat", "DistortedFlare01.png", false),
            Dot = Mat("M_B300_Dot", "Flare00.PNG", false),
            Ring = Mat("M_B300_Ring", "Circle01_v1.png", false),
            Swirl = Mat("M_B300_Swirl", "Swirl01.png", false),
            Glow = Mat("M_B300_Glow", "Flare00.PNG", true),
            Flame = Mat("M_B300_Flame", "Flame02.png", false),
            Trail = Mat("M_B300_Trail", "Flare00.PNG", false),
            TrailGlow = Mat("M_B300_TrailGlow", "Flare00.PNG", true),
            GlowSplat = Mat("M_B300_GlowSplat", "DistortedFlare01.png", true),
        };

        // ---------------------------------------------------------------- particle layers

        sealed class L
        {
            public string Name; public Material Mat; public bool World = true;
            public Vector2 Life = new Vector2(.3f, .5f), Size = new Vector2(.1f, .2f), Speed = Vector2.zero;
            public float Rate, BurstTime; public int Burst, Max = 256;
            public Gradient Colour; public AnimationCurve Grow;
            public ParticleSystemShapeType Shape = ParticleSystemShapeType.Sphere; public float Radius = .04f, Angle = 20f;
            public float Gravity, Spin, Noise, Drag, Length = 0f; public bool Stretch, SpinRandom = true;
            public Mesh Mesh; public float MaxScreen = 2f;
        }

        static Gradient G(params (float t, Color c)[] keys)
        {
            var g = new Gradient();
            g.SetKeys(keys.Select(k => new GradientColorKey(new Color(k.c.r, k.c.g, k.c.b), k.t)).ToArray(),
                keys.Select(k => new GradientAlphaKey(k.c.a, k.t)).ToArray());
            return g;
        }

        static Color C(Color c, float a) => new Color(c.r, c.g, c.b, a);
        static Color Mul(Color c, float k, float a) => new Color(c.r * k, c.g * k, c.b * k, a);
        static AnimationCurve Curve(params float[] tv) { var k = new Keyframe[tv.Length / 2]; for (int i = 0; i < k.Length; i++) k[i] = new Keyframe(tv[2 * i], tv[2 * i + 1]); return new AnimationCurve(k); }

        static int seed = 3000;
        static ParticleSystem Layer(Transform parent, L s)
        {
            var go = new GameObject(s.Name); go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false; ps.randomSeed = (uint)(seed++);
            var main = ps.main;
            main.playOnAwake = false; main.loop = s.Rate > 0f; main.duration = 2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(s.Life.x, s.Life.y);
            main.startSize = new ParticleSystem.MinMaxCurve(s.Size.x, s.Size.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(s.Speed.x, s.Speed.y);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
            main.startRotation = s.SpinRandom ? new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f) : new ParticleSystem.MinMaxCurve(0f);
            main.simulationSpace = s.World ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.gravityModifier = s.Gravity;
            main.maxParticles = s.Max;
            var em = ps.emission; em.enabled = true; em.rateOverTime = s.Rate;
            em.SetBursts(s.Burst > 0 ? new[] { new ParticleSystem.Burst(s.BurstTime, (short)s.Burst) } : new ParticleSystem.Burst[0]);
            var sh = ps.shape; sh.enabled = true; sh.shapeType = s.Shape; sh.radius = s.Radius; sh.angle = s.Angle;
            if (s.Colour != null) { var col = ps.colorOverLifetime; col.enabled = true; col.color = new ParticleSystem.MinMaxGradient(s.Colour); }
            if (s.Grow != null) { var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, s.Grow); }
            if (Mathf.Abs(s.Spin) > .001f) { var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-s.Spin * Mathf.Deg2Rad, s.Spin * Mathf.Deg2Rad); }
            if (s.Noise > 0f) { var n = ps.noise; n.enabled = true; n.strength = s.Noise; n.frequency = 1.2f; n.scrollSpeed = .6f; n.quality = ParticleSystemNoiseQuality.Medium; }
            if (s.Drag > 0f) { var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.drag = s.Drag; lv.limit = 100f; }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = s.Mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.maxParticleSize = s.MaxScreen;
            if (s.Mesh != null) { r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = s.Mesh; r.alignment = ParticleSystemRenderSpace.Local; }
            else if (s.Stretch) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = .05f; r.lengthScale = s.Length > 0 ? s.Length : 2f; }
            else r.renderMode = ParticleSystemRenderMode.Billboard;
            return ps;
        }

        static TrailRenderer Trail(Transform parent, string name, Material mat, float time, float width, Gradient colour)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var t = go.AddComponent<TrailRenderer>();
            t.sharedMaterial = mat; t.time = time; t.widthMultiplier = width;
            t.widthCurve = Curve(0f, 1f, .5f, .55f, 1f, 0f);
            t.colorGradient = colour; t.minVertexDistance = .04f; t.numCapVertices = 3; t.numCornerVertices = 2;
            t.alignment = LineAlignment.View; t.textureMode = LineTextureMode.Stretch;
            t.shadowCastingMode = ShadowCastingMode.Off; t.receiveShadows = false; t.emitting = false; t.autodestruct = false;
            return t;
        }

        // a mesh body under Travel/Spin, longest axis along +Z, scaled to a length (m)
        static void Body(Transform travel, Mesh mesh, Material material, float length, float thickness = 1f, bool flip = false)
        {
            var spin = new GameObject("Spin").transform; spin.SetParent(travel, false);
            var body = new GameObject("Body"); body.transform.SetParent(spin, false);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = body.AddComponent<MeshRenderer>(); mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            var b = mesh.bounds; Vector3 e = b.size;
            // mesh-local longest axis -> Spin +Z; the other two axes take the thickness factor
            int axis = e.x >= e.y && e.x >= e.z ? 0 : e.y >= e.z ? 1 : 2;
            Quaternion align = axis == 0 ? Quaternion.Euler(0f, 90f, 0f) : axis == 1 ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
            if (flip) align = Quaternion.Euler(0f, 180f, 0f) * align;
            float k = length / Mathf.Max(.0001f, e[axis]);
            var scale = Vector3.one * (k * thickness); scale[axis] = k;
            body.transform.localRotation = align;
            body.transform.localScale = scale;
            body.transform.localPosition = -(align * Vector3.Scale(b.center, scale));
        }

        static (Transform travel, Transform contact, GameObject root) Shell(string name)
        {
            var root = new GameObject(name);
            var travel = new GameObject("Travel").transform; travel.SetParent(root.transform, false);
            var contact = new GameObject("Contact").transform; contact.SetParent(root.transform, false);
            return (travel, contact, root);
        }

        static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new Exception("missing " + path);

        // ---------------------------------------------------------------- the five bodies

        static GameObject Ga(Mats m, Vfx120Profile p)
        {
            var (travel, contact, root) = Shell("PF_Bolt300_ga");
            Color pig = p.Pigment, acc = p.Accent, ink = new Color(.07f, .13f, .10f);
            Body(travel, Load<Mesh>("Assets/_Project/Art/SpellVFX120/BambooBolt/VFX120_BambooBolt_001.asset"),
                Load<Material>("Assets/_Project/Art/SpellVFX120/BambooBolt/M_BambooBolt_001.mat"), 1.05f);
            Trail(travel, "InkTrail", m.Trail, .42f, .26f, G((0f, C(ink, .85f)), (.6f, C(pig, .45f)), (1f, C(pig, 0f))));
            Layer(travel, new L { Name = "Flecks", Mat = m.Splat, Rate = 90, Life = new Vector2(.35f, .6f), Size = new Vector2(.06f, .15f), Gravity = .3f, Noise = .35f,
                Colour = G((0f, C(ink, .9f)), (.5f, C(pig, .6f)), (1f, C(pig, 0f))), Grow = Curve(0, .6f, 1, 1.2f) });
            Layer(travel, new L { Name = "Leaves", Mat = m.Blot, Rate = 22, Life = new Vector2(.5f, .8f), Size = new Vector2(.07f, .12f), Gravity = .6f, Noise = .6f, Spin = 240,
                Colour = G((0f, C(acc, .85f)), (1f, C(pig, 0f))) });
            Layer(travel, new L { Name = "Sap", Mat = m.Glow, Burst = 10, Life = new Vector2(.15f, .25f), Size = new Vector2(.05f, .1f), Speed = new Vector2(.4f, 1.2f),
                Colour = G((0f, Mul(acc, 1.6f, 1f)), (1f, C(acc, 0f))) });
            Layer(contact, new L { Name = "Splash", Mat = m.Splat, Burst = 1, Life = new Vector2(.55f, .55f), Size = new Vector2(1.25f, 1.25f),
                Colour = G((0f, C(ink, .92f)), (.5f, C(ink, .7f)), (1f, C(ink, 0f))), Grow = Curve(0, .45f, .2f, 1f, 1, 1.1f) });
            Layer(contact, new L { Name = "Shards", Mat = m.Splat, Burst = 18, Life = new Vector2(.4f, .7f), Size = new Vector2(.05f, .12f), Speed = new Vector2(2f, 5f), Gravity = 1f, Radius = .1f,
                Colour = G((0f, C(pig, .95f)), (1f, C(ink, 0f))) });
            Layer(contact, new L { Name = "Ring", Mat = m.Ring, Burst = 1, Life = new Vector2(.35f, .35f), Size = new Vector2(1.5f, 1.5f),
                Colour = G((0f, C(acc, .75f)), (1f, C(acc, 0f))), Grow = Curve(0, .2f, 1, 1f) });
            Layer(contact, new L { Name = "Flash", Mat = m.Glow, Burst = 1, Life = new Vector2(.12f, .12f), Size = new Vector2(.9f, .9f),
                Colour = G((0f, Mul(acc, 1.8f, 1f)), (1f, C(acc, 0f))) });
            return root;
        }

        static GameObject Na(Mats m, Vfx120Profile p)
        {
            var (travel, contact, root) = Shell("PF_Bolt300_na");
            Color pig = p.Pigment, acc = p.Accent, soot = new Color(.06f, .04f, .03f);
            Color hot = new Color(2.2f, .95f, .38f), warm = new Color(1.25f, .38f, .14f), char0 = new Color(.24f, .08f, .05f);
            Layer(travel, new L { Name = "Core", Mat = m.Dot, World = false, Rate = 70, Life = new Vector2(.1f, .14f), Size = new Vector2(.3f, .34f),
                Colour = G((0f, C(soot, .95f)), (1f, C(soot, .9f))) });
            Layer(travel, new L { Name = "Flame", Mat = m.Flame, Rate = 150, Life = new Vector2(.2f, .34f), Size = new Vector2(.42f, .58f), Speed = new Vector2(0f, .4f), Noise = .3f, Spin = 180, Radius = .08f,
                Colour = G((0f, C(hot, .92f)), (.35f, C(warm, .8f)), (1f, C(char0, 0f))), Grow = Curve(0, 1f, 1, .35f) });
            Layer(travel, new L { Name = "Smoke", Mat = m.Blot, Rate = 45, Life = new Vector2(.6f, .9f), Size = new Vector2(.32f, .46f), Speed = new Vector2(.1f, .3f), Noise = .4f, Spin = 60, Gravity = -.05f,
                Colour = G((0f, C(soot, .6f)), (1f, C(new Color(.2f, .18f, .16f), 0f))), Grow = Curve(0, .8f, 1, 2.2f) });
            Layer(travel, new L { Name = "Embers", Mat = m.Glow, Rate = 50, Life = new Vector2(.3f, .6f), Size = new Vector2(.03f, .06f), Speed = new Vector2(.5f, 1.5f), Gravity = -.2f, Noise = .8f,
                Colour = G((0f, C(new Color(1.8f, .8f, .3f), 1f)), (1f, C(pig, 0f))) });
            Layer(contact, new L { Name = "Burst", Mat = m.Flame, Burst = 14, Life = new Vector2(.3f, .5f), Size = new Vector2(.45f, .85f), Speed = new Vector2(2f, 5f), Radius = .15f, Drag = 2f, Spin = 120,
                Colour = G((0f, C(hot, .95f)), (.4f, C(warm, .8f)), (1f, C(char0, 0f))), Grow = Curve(0, .6f, 1, 1.4f) });
            Layer(contact, new L { Name = "SmokeBurst", Mat = m.Blot, Burst = 8, Life = new Vector2(.8f, 1.2f), Size = new Vector2(.6f, 1.2f), Speed = new Vector2(.5f, 1.5f), Drag = 1f, Noise = .3f, Spin = 40,
                Colour = G((0f, C(soot, .65f)), (1f, C(new Color(.22f, .2f, .18f), 0f))), Grow = Curve(0, .7f, 1, 1.8f) });
            Layer(contact, new L { Name = "Sparks", Mat = m.Glow, Burst = 30, Life = new Vector2(.2f, .45f), Size = new Vector2(.03f, .06f), Speed = new Vector2(4f, 9f), Gravity = .8f, Stretch = true, Length = 3f,
                Colour = G((0f, C(new Color(2f, .9f, .35f), 1f)), (1f, C(pig, 0f))) });
            Layer(contact, new L { Name = "Flash", Mat = m.Glow, Burst = 1, Life = new Vector2(.12f, .12f), Size = new Vector2(1.6f, 1.6f),
                Colour = G((0f, C(new Color(2.5f, 1.1f, .4f), 1f)), (1f, C(pig, 0f))) });
            return root;
        }

        static GameObject Ma(Mats m, Vfx120Profile p)
        {
            var (travel, contact, root) = Shell("PF_Bolt300_ma");
            Color pig = p.Pigment, dark = new Color(.22f, .16f, .10f), dust = new Color(.60f, .50f, .36f);
            var rock = Load<Mesh>("Assets/_Project/Art/SpellVFX120/Meshes/Rock.asset");
            var rockMat = Load<Material>("Assets/_Project/Art/SpellVFX120/Materials/M_Body_Rock.mat");
            Body(travel, rock, rockMat, .62f);
            Layer(travel, new L { Name = "Dust", Mat = m.Blot, Rate = 70, Life = new Vector2(.5f, .8f), Size = new Vector2(.25f, .35f), Speed = new Vector2(.1f, .3f), Noise = .35f, Spin = 50, Radius = .15f,
                Colour = G((0f, C(pig, .55f)), (1f, C(dust, 0f))), Grow = Curve(0, .8f, 1, 2f) });
            Layer(travel, new L { Name = "Grit", Mat = m.Splat, Rate = 40, Life = new Vector2(.4f, .7f), Size = new Vector2(.04f, .08f), Gravity = 1.2f, Radius = .2f,
                Colour = G((0f, C(dark, .9f)), (1f, C(dark, 0f))) });
            Layer(contact, new L { Name = "DustCloud", Mat = m.Blot, Burst = 16, Life = new Vector2(.8f, 1.3f), Size = new Vector2(.8f, 1.6f), Speed = new Vector2(1.5f, 4f), Drag = 2.5f, Noise = .3f, Spin = 30,
                Shape = ParticleSystemShapeType.Hemisphere, Radius = .3f, Colour = G((0f, C(pig, .75f)), (.6f, C(dust, .4f)), (1f, C(dust, 0f))), Grow = Curve(0, .6f, 1, 1.6f) });
            Layer(contact, new L { Name = "Rubble", Mat = rockMat, Mesh = rock, Burst = 10, Life = new Vector2(.7f, 1f), Size = new Vector2(.08f, .18f), Speed = new Vector2(3f, 6f), Gravity = 2f,
                Shape = ParticleSystemShapeType.Hemisphere, Radius = .2f, Spin = 360 });
            Layer(contact, new L { Name = "Ring", Mat = m.Ring, Burst = 1, Life = new Vector2(.45f, .45f), Size = new Vector2(2.6f, 2.6f),
                Colour = G((0f, C(pig, .7f)), (1f, C(pig, 0f))), Grow = Curve(0, .2f, 1, 1f) });
            Layer(contact, new L { Name = "Splat", Mat = m.Splat, Burst = 1, Life = new Vector2(.6f, .6f), Size = new Vector2(1.7f, 1.7f),
                Colour = G((0f, C(dark, .9f)), (1f, C(dark, 0f))), Grow = Curve(0, .5f, .25f, 1f, 1, 1.05f) });
            return root;
        }

        static GameObject Sa(Mats m, Vfx120Profile p)
        {
            var (travel, contact, root) = Shell("PF_Bolt300_sa");
            Color pig = p.Pigment, acc = p.Accent, silver = new Color(1.35f, 1.4f, 1.45f), ink = new Color(.06f, .06f, .07f);
            Body(travel, Load<Mesh>("Assets/_Project/Art/SpellVFX120/Meshes/Shard.asset"),
                Load<Material>("Assets/_Project/Art/SpellVFX120/Materials/M_Body_Shard.mat"), .95f, .5f);
            Trail(travel, "Streak", m.TrailGlow, .3f, .12f, G((0f, C(silver, 1f)), (.5f, C(pig, .6f)), (1f, C(acc, 0f))));
            Trail(travel, "InkLine", m.Trail, .45f, .05f, G((0f, C(ink, .9f)), (1f, C(ink, 0f))));
            Layer(travel, new L { Name = "Glint", Mat = m.GlowSplat, Rate = 220, Life = new Vector2(.12f, .22f), Size = new Vector2(.05f, .1f),
                Colour = G((0f, C(silver, 1f)), (1f, C(acc, 0f))) });
            Layer(contact, new L { Name = "Sparks", Mat = m.Glow, Burst = 26, Life = new Vector2(.15f, .35f), Size = new Vector2(.02f, .05f), Speed = new Vector2(5f, 10f), Gravity = .5f, Stretch = true, Length = 4f,
                Colour = G((0f, C(new Color(1.5f, 1.5f, 1.6f), 1f)), (1f, C(acc, 0f))) });
            Layer(contact, new L { Name = "CutMark", Mat = m.Splat, Burst = 1, Life = new Vector2(.45f, .45f), Size = new Vector2(.55f, .55f),
                Colour = G((0f, C(ink, .92f)), (1f, C(ink, 0f))) });
            Layer(contact, new L { Name = "Ring", Mat = m.Ring, Burst = 1, Life = new Vector2(.22f, .22f), Size = new Vector2(.95f, .95f),
                Colour = G((0f, C(pig, .75f)), (1f, C(pig, 0f))), Grow = Curve(0, .2f, 1, 1f) });
            Layer(contact, new L { Name = "Flash", Mat = m.Glow, Burst = 1, Life = new Vector2(.08f, .08f), Size = new Vector2(.65f, .65f),
                Colour = G((0f, C(new Color(1.6f, 1.7f, 1.8f), 1f)), (1f, C(acc, 0f))) });
            return root;
        }

        static GameObject A(Mats m, Vfx120Profile p)
        {
            var (travel, contact, root) = Shell("PF_Bolt300_a");
            Color pig = p.Pigment, acc = p.Accent, deep = new Color(.03f, .05f, .08f), skin = new Color(.10f, .16f, .24f);
            Layer(travel, new L { Name = "Drop", Mat = m.Ring, World = false, Rate = 40, Life = new Vector2(.1f, .12f), Size = new Vector2(.44f, .46f), SpinRandom = false,
                Colour = G((0f, C(skin, .85f)), (1f, C(skin, .8f))) });
            Layer(travel, new L { Name = "InkCore", Mat = m.Dot, World = false, Rate = 40, Life = new Vector2(.1f, .12f), Size = new Vector2(.26f, .28f),
                Colour = G((0f, C(deep, .95f)), (1f, C(deep, .9f))) });
            Layer(travel, new L { Name = "Swirl", Mat = m.Swirl, World = false, Rate = 12, Life = new Vector2(.3f, .3f), Size = new Vector2(.58f, .6f), Spin = 360, SpinRandom = true,
                Colour = G((0f, C(acc, 0f)), (.3f, C(pig, .6f)), (1f, C(pig, 0f))) });
            Trail(travel, "Wake", m.Trail, .7f, .3f, G((0f, C(skin, .85f)), (.6f, C(pig, .4f)), (1f, C(pig, 0f))));
            Layer(travel, new L { Name = "Drips", Mat = m.Dot, Rate = 30, Life = new Vector2(.4f, .7f), Size = new Vector2(.04f, .08f), Gravity = .8f, Radius = .12f,
                Colour = G((0f, C(skin, .9f)), (1f, C(pig, 0f))) });
            var ripple = Layer(contact, new L { Name = "Ripple", Mat = m.Ring, Burst = 1, Life = new Vector2(.6f, .6f), Size = new Vector2(2f, 2f), SpinRandom = false,
                Colour = G((0f, C(acc, .65f)), (1f, C(acc, 0f))), Grow = Curve(0, .15f, 1, 1f) });
            var em = ripple.emission; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1), new ParticleSystem.Burst(.08f, 1), new ParticleSystem.Burst(.16f, 1) });
            Layer(contact, new L { Name = "Splash", Mat = m.Splat, Burst = 1, Life = new Vector2(.6f, .6f), Size = new Vector2(1.3f, 1.3f),
                Colour = G((0f, C(deep, .92f)), (1f, C(deep, 0f))), Grow = Curve(0, .5f, .25f, 1f, 1, 1.05f) });
            Layer(contact, new L { Name = "Droplets", Mat = m.Dot, Burst = 20, Life = new Vector2(.5f, .8f), Size = new Vector2(.05f, .1f), Speed = new Vector2(2f, 4f), Gravity = 1.5f,
                Colour = G((0f, C(skin, .95f)), (1f, C(pig, 0f))) });
            return root;
        }

        // ---------------------------------------------------------------- after-fog layer
        // RealmFog297 (full-screen, BeforeRenderingPostProcessing) fogs every pixel by the depth texture. Transparent VFX and
        // bodies without a depth pass therefore take the fog of the far terrain behind them and wash out. The #300 bodies
        // live on their own layer, left out of the normal opaque/transparent passes and drawn right after the fog instead.
        public const int AfterFogLayer = 20;
        const string RendererPath = "Assets/_Project/Art/World/Finish297/Renderer297.asset";

        static string AfterFog()
        {
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tags.FindProperty("layers");
            var slot = layers.GetArrayElementAtIndex(AfterFogLayer);
            if (slot.stringValue != "VfxAfterFog")
            {
                if (!string.IsNullOrEmpty(slot.stringValue)) throw new Exception("layer " + AfterFogLayer + " is taken: " + slot.stringValue);
                slot.stringValue = "VfxAfterFog"; tags.ApplyModifiedPropertiesWithoutUndo();
            }
            var data = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(RendererPath);
            var so = new SerializedObject(data);
            int bit = 1 << AfterFogLayer;
            so.FindProperty("m_OpaqueLayerMask").intValue &= ~bit;
            so.FindProperty("m_TransparentLayerMask").intValue &= ~bit;
            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            var sb = new StringBuilder();
            foreach (var (name, queue) in new[] { ("VfxAfterFog300_Opaque", UnityEngine.Rendering.Universal.RenderQueueType.Opaque),
                                                   ("VfxAfterFog300_Transparent", UnityEngine.Rendering.Universal.RenderQueueType.Transparent) })
            {
                bool exists = false;
                for (int i = 0; i < features.arraySize; i++) if (features.GetArrayElementAtIndex(i).objectReferenceValue?.name == name) exists = true;
                if (exists) { sb.AppendLine(name + " present"); continue; }
                var ro = ScriptableObject.CreateInstance<UnityEngine.Rendering.Universal.RenderObjects>();
                ro.name = name;
                ro.settings.passTag = name;
                ro.settings.Event = UnityEngine.Rendering.Universal.RenderPassEvent.BeforeRenderingPostProcessing;
                ro.settings.filterSettings.RenderQueueType = queue;
                ro.settings.filterSettings.LayerMask = bit;
                AssetDatabase.AddObjectToAsset(ro, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ro, out string _, out long id);
                features.arraySize++; features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = ro;
                map.arraySize++; map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
                sb.AppendLine(name + " added (" + queue + ")");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        static void SetLayer(GameObject root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = AfterFogLayer;
        }

        // ---------------------------------------------------------------- build / connect

        static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Bolt300Build: Edit mode only");
            Directory.CreateDirectory(Root); Directory.CreateDirectory(Root + "/Profiles"); Directory.CreateDirectory(Root + "/Effects");
            AssetDatabase.Refresh();
            var mats = Materials();
            var sb = new StringBuilder();
            foreach (var s in Spells)
            {
                string srcProfile = "Assets/_Project/Art/SpellVFX120/Profiles/" + s.Effect + ".asset";
                string profilePath = Root + "/Profiles/" + s.Effect + "_300.asset";
                if (!File.Exists(profilePath) && !AssetDatabase.CopyAsset(srcProfile, profilePath)) throw new Exception("copy failed " + srcProfile);
                var profile = Load<Vfx120Profile>(profilePath);
                var original = Load<Vfx120Profile>(srcProfile);
                var root = s.Key switch { "ga" => Ga(mats, original), "na" => Na(mats, original), "ma" => Ma(mats, original), "sa" => Sa(mats, original), _ => A(mats, original) };
                SetLayer(root);
                string prefabPath = Root + "/PF_Bolt300_" + s.Key + ".prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Object.DestroyImmediate(root);
                profile.Bolt300Prefab = prefab; profile.Bolt300Arc = s.Arc; profile.Bolt300Curve = s.Curve; profile.Bolt300Spin = s.Spin;
                profile.Bolt300Scale = s.Scale; profile.Bolt300Ease = 1f;
                EditorUtility.SetDirty(profile);
                string srcEffect = "Assets/_Project/Art/SpellVFX120/Prefabs/" + s.Effect + ".prefab";
                string effectPath = Root + "/Effects/" + s.Effect + "_300.prefab";
                if (!File.Exists(effectPath) && !AssetDatabase.CopyAsset(srcEffect, effectPath)) throw new Exception("copy failed " + srcEffect);
                var contents = PrefabUtility.LoadPrefabContents(effectPath);
                try
                {
                    var fx = contents.GetComponentInChildren<Vfx120Effect>(true) ?? throw new Exception("no Vfx120Effect in " + effectPath);
                    fx.Profile = profile;
                    PrefabUtility.SaveAsPrefabAsset(contents, effectPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                sb.AppendLine(s.Glyph + " " + prefabPath + " -> " + effectPath + " (profile " + profilePath + ")");
            }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        [Serializable] sealed class Entry { public string glyph, before, after; }
        [Serializable] sealed class Recorded { public List<Entry> entries = new List<Entry>(); }

        static string Connect()
        {
            var set = Load<SpellVisualSetSO>(VisualSet);
            var so = new SerializedObject(set);
            var entries = so.FindProperty("_entries");
            var rec = File.Exists(Record) ? JsonUtility.FromJson<Recorded>(File.ReadAllText(Record)) : new Recorded();
            var sb = new StringBuilder();
            foreach (var s in Spells)
            {
                var effect = Load<GameObject>(Root + "/Effects/" + s.Effect + "_300.prefab");
                for (int i = 0; i < entries.arraySize; i++)
                {
                    var e = entries.GetArrayElementAtIndex(i);
                    if (e.FindPropertyRelative("Letter").stringValue != s.Glyph) continue;
                    var fx = e.FindPropertyRelative("FxPrefab");
                    string current = AssetDatabase.GetAssetPath(fx.objectReferenceValue);
                    if (current != AssetDatabase.GetAssetPath(effect) && rec.entries.All(x => x.glyph != s.Glyph))
                        rec.entries.Add(new Entry { glyph = s.Glyph, before = current, after = AssetDatabase.GetAssetPath(effect) });
                    fx.objectReferenceValue = effect;
                    sb.AppendLine(s.Glyph + ": " + current + " -> " + AssetDatabase.GetAssetPath(effect));
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Path.GetDirectoryName(Record));
            File.WriteAllText(Record, JsonUtility.ToJson(rec, true));
            return sb.ToString();
        }

        static string Revert()
        {
            if (!File.Exists(Record)) return "nothing recorded";
            var rec = JsonUtility.FromJson<Recorded>(File.ReadAllText(Record));
            var set = Load<SpellVisualSetSO>(VisualSet);
            var so = new SerializedObject(set);
            var entries = so.FindProperty("_entries");
            var sb = new StringBuilder();
            foreach (var r in rec.entries)
                for (int i = 0; i < entries.arraySize; i++)
                {
                    var e = entries.GetArrayElementAtIndex(i);
                    if (e.FindPropertyRelative("Letter").stringValue != r.glyph) continue;
                    e.FindPropertyRelative("FxPrefab").objectReferenceValue = Load<GameObject>(r.before);
                    sb.AppendLine(r.glyph + " -> " + r.before);
                }
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        static string Status()
        {
            var set = Load<SpellVisualSetSO>(VisualSet);
            var so = new SerializedObject(set);
            var entries = so.FindProperty("_entries");
            var sb = new StringBuilder();
            foreach (var s in Spells)
                for (int i = 0; i < entries.arraySize; i++)
                {
                    var e = entries.GetArrayElementAtIndex(i);
                    if (e.FindPropertyRelative("Letter").stringValue == s.Glyph)
                        sb.AppendLine(s.Glyph + ": " + AssetDatabase.GetAssetPath(e.FindPropertyRelative("FxPrefab").objectReferenceValue));
                }
            return sb.ToString();
        }
    }
}
