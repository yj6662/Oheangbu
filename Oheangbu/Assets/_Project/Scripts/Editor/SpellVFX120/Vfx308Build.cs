using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // #308 spell VFX overhaul, first five prototypes (SPEC-SPELL-VFX-OVERHAUL-308, D308-52): 가·나·마·사·아 rebuilt from
    // baked material - Blender forms (Tools/Blender/SpellVFX308) stylised into ink flipbooks (Tools/Art/SpellVFX308), plus
    // Blender meshes for the solid parts. Same seam as #300: a prefab with Travel[/Spin] and Contact, set on a COPY of the
    // profile (Vfx120Profile.Bolt300Prefab); the originals, the #300 assets and the visual set are not edited by `build`.
    // Ink rules: alpha-blended LDR ink only; the one glow is the two-cel birth layer (under the D308-10c ceiling); 나 keeps a
    // warm painted heat tone in flight. Nothing is drawn nearer than .45 m (shader). Presentation only.
    // Two looks: v2 (default) = bleeding ink (D308-53 (3)), assets under SpellVFX308/V2; v1 = the first hard cutout look.
    // Append :v1 to any command for the first look (build:v1, connect:v1, pose:ga:mid:v1 ...).
    //   build                     - import settings, materials, PF_V308_* prefabs, profile + effect copies (re-runnable)
    //   build                     - the five prototypes, and (v2) the attack set: 46 of the 50 attack letters by element x form
    //   build:five | attack       - only the five | the letter / form / body table
    //   connect | connect:five    - the main visual set's five letters use the #308 effects (previous entries recorded)
    //   connect:attack            - the five and the 41 other built attack letters (v2 only)
    //   revert                    - restore the recorded entries
    //   status | budget
    //   pose:<letter>:<when>[:ox,oy,oz:tx,ty,tz]  - Edit mode: one unsaved preview posed at a moment of its flight
    //                               when = seconds | launch | mid (6 m out) | hit | hit+<seconds>
    //   pose:clear
    public static partial class Vfx308Build
    {
        const string Root = "Assets/_Project/Art/SpellVFX308";
        const string VisualSet = "Assets/_Project/Art/World/Architecture296/Data/9f55ff4897c35bf408434d2fd4cd5dbc_278c3cdf45b3ab24fb93a8f33e2264e2_9ba170fd23c53dc418707872eb7b2056_SpellVisualSet_120.asset";
        const string PoseName = "__Vfx308Pose";
        const int AfterFogLayer = 20;      // "VfxAfterFog" (#300): drawn after the full-screen fog
        const float Cel = 1f / 12f;
        static string Record => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/SpellVFX308/visualset-before.json"));

        sealed class Spell { public string Glyph, Key, Effect; public float Arc, Curve, Spin, Scale = 1f, Speed; }
        static readonly Spell[] Spells =
        {
            new Spell { Glyph = "가", Key = "ga", Effect = "001_AC00", Arc = .15f, Speed = 23.4f },
            new Spell { Glyph = "나", Key = "na", Effect = "025_B098", Arc = .3f, Speed = 18f },
            new Spell { Glyph = "마", Key = "ma", Effect = "049_B9C8", Arc = 1.9f, Spin = 420f, Speed = 18f },
            new Spell { Glyph = "사", Key = "sa", Effect = "073_C0AC", Speed = 32.4f },
            new Spell { Glyph = "아", Key = "a", Effect = "097_C544", Arc = .5f, Curve = 1.3f, Speed = 10.8f },
        };

        public static string Run(string command)
        {
            // the look: v2 (bleeding ink, D308-53 (3)) unless the command ends in :v1 (the first, hard-edged cutout look)
            V2 = !command.EndsWith(":v1", StringComparison.Ordinal);
            if (!V2) command = command.Substring(0, command.Length - 3);
            if (command.StartsWith("pose", StringComparison.Ordinal)) return Pose(command.Split(':'));
            if (command.StartsWith("summon", StringComparison.Ordinal)) return Summon(command.Split(':'));         // 11.13: the ink body of a summon
            if (command.StartsWith("swingink", StringComparison.Ordinal)) return SwingInk(command.Split(':'));     // 11.10: the ink of the five swings
            if (command.StartsWith("graph:blocks", StringComparison.Ordinal)) return GraphBlocks(command.Split(':'));   // 11.33 8th pass: real block / slot names
            if (command.StartsWith("graph:dumpfull", StringComparison.Ordinal)) return GraphDump(command.Length > 15 ? command.Substring(15) : NaSmokeGraph, true);
            switch (command)
            {
                case "build": return Build() + (V2 ? BuildAttack() : "");
                case "build:five": return Build();
                case "connect": case "connect:five": return Connect(false);
                case "connect:attack": if (!V2) throw new ArgumentException("the attack set exists in the v2 look only"); return Connect(true);
                case "attack": return AttackTable();
                case "stain": return BuildStain();   // 11.37 (e) D308-104: the range-ㄱ foot stain alone
                case "guard": return BuildGuard();   // 11.22 stage 4: the guard membranes
                case "giyeok": return BuildGiyeok(); // 11.22 stage 4: the ㄱ-final attacks as #308 bolts
                case "aura": return BuildAura();     // 11.22 stage 4: buffs and wards as #308 loop bodies
                case "cue": return BuildCue();       // 11.40: the 27 non-attack cue letters
                case "beast": return BuildBeast();   // 11.22 stage 4: the ink body on the skinned summons
                case "retire:summon": return ConnectSummonFx();         // 11.37 (f) D308-107: old summon effects off on the live profiles
                case "retire:summon:revert": return RevertSummonFx();
                case "unravel": return BuildUnravel();   // 11.25: the letter coming apart into the brush (particle prefab + Resources profile)
                case "graph:build": return GraphBuild();             // 11.32: the 노 smoke graph (concepts C + D)
                case "graph:build:ring": return GraphBuildRing();     // 11.34 fire8: tongues leaning out round the target
                case "graph:build:scorch": return GraphBuildScorch(); // 11.34 fire8: red-ink scorch laid on the ground
                case "graph:build:column": return GraphBuildColumn(); // 11.34 fire8: the thin soot column
                case "graph:build:wash": return GraphBuildWash();     // 11.34 fire16: the soot wash under the ring
                case "graph:build:pool": return GraphBuildPool();     // 11.34 fire16: the red-black ground pool
                case "graph:build:bank": return GraphBuildBank();     // 11.34 fire17: the baked fluid fire band round the target
                case "graph:build:stain": return GraphBuildStain();   // 11.34 fire17: the splash stain under it
                case "graph:build:banks": return GraphBuildBanks();   // 11.34 fire18: the bank as five arcs opened in turn
                case "graph:build:ember": return GraphBuildEmber();   // 11.34 fire18: the ember thrown from the brush tip
                case "graph:dump": return GraphDump(NaSmokeGraph);
                case "revert": return Revert();
                case "status": return Status();
                case "budget": return Budget();
                default: throw new ArgumentException("Vfx308Build: build | connect | revert | status | budget | pose:<letter>:<when> | pose:clear  (append :v1 for the first look)");
            }
        }

        // ---------------------------------------------------------------- imported art

        static readonly string[] Sheets = { "ink_splat", "ink_drops", "wood_splinter", "fire_tongue", "fire_burst", "earth_dust", "metal_streak", "water_ring", "water_orb" };
        static readonly string[] MeshNames = { "thorn", "splinter", "clod", "chunk", "blade", "stalk", "drop", "tongue" };
        static readonly string[] Sheets2 = Sheets.Concat(new[] { "wood_stroke", "fire_lick", "speed_line", "earth_heave", "water_crest", "ink_pool", "ink_throw", "ink_wisp", "ink_gather" }).ToArray();     // v2 only (4th pass, range forms)

        static Texture2D Sheet(string name)
        {
            string path = Root + "/Textures/T_V308_" + name + ".png";
            var imp = AssetImporter.GetAtPath(path) as TextureImporter ?? throw new Exception("missing " + path + " (run stylise308.py)");
            // data channels (heat / erosion field / halo / coverage): linear, alpha as data, mips, clamped
            if (imp.sRGBTexture || imp.alphaIsTransparency || imp.wrapMode != TextureWrapMode.Clamp || !imp.mipmapEnabled || imp.maxTextureSize != 1024)
            {
                imp.textureType = TextureImporterType.Default; imp.sRGBTexture = false; imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.alphaIsTransparency = false; imp.mipmapEnabled = true; imp.wrapMode = TextureWrapMode.Clamp; imp.filterMode = FilterMode.Bilinear;
                imp.maxTextureSize = 1024; imp.textureCompression = TextureImporterCompression.Compressed;
                imp.SaveAndReimport();
            }
            return Load<Texture2D>(path);
        }

        static Mesh Model(string name)
        {
            string path = Root + "/Meshes/V308_" + name + ".fbx";
            var imp = AssetImporter.GetAtPath(path) as ModelImporter ?? throw new Exception("missing " + path + " (run meshes308.py)");
            if (!imp.isReadable || imp.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                imp.isReadable = true;                                  // particle mesh rendering reads the mesh
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.importAnimation = false; imp.importCameras = false; imp.importLights = false;
                imp.SaveAndReimport();
            }
            var mesh = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault() ?? throw new Exception("no mesh in " + path);
            if (mesh.triangles.Length == 0) throw new Exception("mesh without triangles: " + path);
            return mesh;
        }

        static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new Exception("missing " + path);

        // ---------------------------------------------------------------- materials

        static readonly List<Object> Touched = new List<Object>();

        static Material Flip(string name, string sheet, Color ink, float heat = 0f, float glow = 0f, float halo = .18f, float steps = 0f, float softDepth = .25f)
        // (halo / softDepth are kept in the signature for a later blended variant; the cutout shader has no such terms)
        {
            Directory.CreateDirectory(Root + "/Materials");
            string path = Root + "/Materials/" + name + ".mat";
            var shader = Shader.Find("Oheangbu/SpellVFX308/InkFlipbook") ?? throw new Exception("InkFlipbook shader not found");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetTexture("_MainTex", Sheet(sheet));
            m.SetColor("_Ink", ink);
            m.SetFloat("_Heat", heat);
            bool thin = sheet == "wood_splinter" || sheet == "metal_streak";      // hair-thin strokes have a low field everywhere
            m.SetFloat("_CoreLo", thin ? .02f : .1f); m.SetFloat("_CoreHi", thin ? .22f : .4f);
            m.SetFloat("_Glow", Mathf.Min(glow, .35f));       // SpellDeploy308ProfileSO.MaxGlowAmount
            m.SetFloat("_ErodeSteps", steps);
            m.renderQueue = -1;
            EditorUtility.SetDirty(m); Touched.Add(m);
            return m;
        }

        static Material Solid(string name, Color pigment, Color ink, float shade = .55f, float rim = .38f)
        {
            Directory.CreateDirectory(Root + "/Materials");
            string path = Root + "/Materials/" + name + ".mat";
            var shader = Shader.Find("Oheangbu/SpellVFX308/InkSolid") ?? throw new Exception("InkSolid shader not found");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetColor("_BaseColor", pigment); m.SetColor("_Ink", ink); m.SetFloat("_Shade", Mathf.Min(shade, .5f)); m.SetFloat("_Rim", rim);
            // 5th pass: the surface of each element (grain domain round / round / along the axis + strength, bump, ink in the
            // grain, pooled underside, ink toward the tip). The element is read off the material's name (M_V308_<key>_...).
            string el = name.Split('_').Length > 2 ? name.Split('_')[2] : "";
            bool line = name.EndsWith("_line", StringComparison.Ordinal), strand = name.EndsWith("_strand", StringComparison.Ordinal);
            Vector4 grain; float bump, cavity, under = 0f, tip = 0f;
            switch (el)
            {
                case "ga": grain = new Vector4(7f, 7f, 1.6f, 1f); bump = .035f; cavity = .65f; break;                   // wood: long fibre
                case "ma": grain = new Vector4(3.2f, 3.2f, 4.5f, 1f); bump = .06f; cavity = .6f; break;                 // stone: pits and rough faces
                case "sa": grain = new Vector4(11f, 11f, .9f, .55f); bump = .012f; cavity = .35f; break;                // metal: fine brushed faces
                case "a": grain = new Vector4(1.6f, 1.6f, 1.8f, .45f); bump = .02f; cavity = 0f; under = 1f; break;     // water: smooth, sagging, darker below
                default: grain = new Vector4(4f, 4f, 3f, 1f); bump = .05f; cavity = .7f; break;                         // fire: coal
            }
            if (strand) { grain = new Vector4(5f, 5f, 2.2f, .9f); bump = .03f; cavity = .5f; tip = .95f; }
            if (line) { grain.w = 0f; bump = 0f; cavity = 0f; }
            m.SetVector("_Grain", grain); m.SetFloat("_Bump", bump); m.SetFloat("_Cavity", cavity); m.SetFloat("_Under", under); m.SetFloat("_TipInk", tip); m.SetFloat("_Visibility", 1f);
            EditorUtility.SetDirty(m); Touched.Add(m);
            return m;
        }

        // ---------------------------------------------------------------- particle layers

        sealed class L
        {
            public string Name; public Material Mat; public bool World = true;
            public Vector2 Life = new Vector2(.3f, .5f), Size = new Vector2(.1f, .2f), Speed = Vector2.zero;
            public Vector3 Size3D;                                     // mesh particles: x,y thickness and z length factors
            public float Rate, BurstTime = .012f; public int Burst, Max = 64;   // never a burst at t = 0: Vfx120Effect restarts the systems with Simulate(0) before it places Travel / Contact, and a t = 0 burst would be emitted there
            public Gradient Colour; public AnimationCurve Grow;
            public ParticleSystemShapeType Shape = ParticleSystemShapeType.Sphere; public float Radius = .04f, Angle = 20f, ArcSpeed, Thickness = 1f;
            public Vector3 ShapeRot, ShapeScale, ShapePos; public bool NoShape, ShapeScaling;      // ShapeScaling: the parent's scale widens the emitter only (path fronts)
            public float Gravity, Spin, Drag, RiseY, Roll;             // Roll: deg/s about the flight axis (mesh particles)
            public bool DragLinear;                                    // pq_fan3: Unity's drag is times size and speed by default (a 3 m sheet hardly moved); true = plain drag
            public bool SpinRandom = true, Tumble, AlongVelocity;
            public Vector2? RotDeg;                                    // billboards: a fixed start rotation range in degrees (11.23 markers)
            public Mesh Mesh; public Vector3 Pivot;
            // flipbook: frames FrameFrom..FrameTo of 16 played over the lifetime; RandomCell = one random cel held;
            // RandomStart = start anywhere and advance FrameTo - FrameFrom cels; Cycles = whole-sheet loops over the lifetime
            public int Mode;                                         // billboard: 0 camera, 1 flat on the ground, 2 upright, 3 stretched along its velocity
            public bool Flipbook = true, RandomCell, RandomStart, Blend; public float FrameFrom, FrameTo = 16f, Cycles;
            public AnimationCurve FrameCurve;                        // 11.28: overrides the linear run - the frame (0..1 of the sheet) over the lifetime, to reach a cel and hold it
        }

        static Gradient G(params (float t, Color c)[] keys)
        {
            var g = new Gradient();
            g.SetKeys(keys.Select(k => new GradientColorKey(new Color(k.c.r, k.c.g, k.c.b), k.t)).ToArray(),
                keys.Select(k => new GradientAlphaKey(k.c.a, k.t)).ToArray());
            return g;
        }

        // whole, then eroded away from `hold` of the life on (the shader thins the stroke with the alpha)
        static Gradient Melt(Color c, float hold = .45f, Color? end = null)
        {
            Color e = end ?? c;
            return G((0f, C(c, 1f)), (hold, C(Color.Lerp(c, e, .5f), 1f)), (1f, C(e, 0f)));
        }

        static Color C(Color c, float a) => new Color(c.r, c.g, c.b, a);
        static AnimationCurve Curve(params float[] tv) { var k = new Keyframe[tv.Length / 2]; for (int i = 0; i < k.Length; i++) k[i] = new Keyframe(tv[2 * i], tv[2 * i + 1]); return new AnimationCurve(k); }

        static int seed;
        static ParticleSystem Layer(Transform parent, L s)
        {
            var go = new GameObject(s.Name); go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false; ps.randomSeed = (uint)(seed++);
            var main = ps.main;
            main.playOnAwake = false; main.loop = s.Rate > 0f; main.duration = 2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(s.Life.x, s.Life.y);
            if (s.Size3D != Vector3.zero)
            {
                main.startSize3D = true;
                main.startSizeX = new ParticleSystem.MinMaxCurve(s.Size.x * s.Size3D.x, s.Size.y * s.Size3D.x);
                main.startSizeY = new ParticleSystem.MinMaxCurve(s.Size.x * s.Size3D.y, s.Size.y * s.Size3D.y);
                main.startSizeZ = new ParticleSystem.MinMaxCurve(s.Size.x * s.Size3D.z, s.Size.y * s.Size3D.z);
            }
            else main.startSize = new ParticleSystem.MinMaxCurve(s.Size.x, s.Size.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(s.Speed.x, s.Speed.y);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
            if (s.Tumble)
            {
                main.startRotation3D = true;
                main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            }
            else if (s.Mesh != null && Backward(s.Mesh)) { main.startRotation3D = true; main.startRotationX = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f); main.startRotationY = new ParticleSystem.MinMaxCurve(Mathf.PI); }
            else main.startRotation = s.SpinRandom && s.Mesh == null ? new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f) : new ParticleSystem.MinMaxCurve(0f);
            if (s.RotDeg.HasValue && s.Mesh == null && !s.Tumble) main.startRotation = new ParticleSystem.MinMaxCurve(s.RotDeg.Value.x * Mathf.Deg2Rad, s.RotDeg.Value.y * Mathf.Deg2Rad);
            main.simulationSpace = s.World ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.scalingMode = s.ShapeScaling ? ParticleSystemScalingMode.Shape : ParticleSystemScalingMode.Hierarchy;
            main.gravityModifier = s.Gravity;
            main.maxParticles = s.Max;
            var em = ps.emission; em.enabled = true; em.rateOverTime = s.Rate;
            em.SetBursts(s.Burst > 0 ? new[] { new ParticleSystem.Burst(s.BurstTime, (short)s.Burst) } : new ParticleSystem.Burst[0]);
            var sh = ps.shape; sh.enabled = !s.NoShape; sh.shapeType = s.Shape; sh.radius = s.Radius; sh.angle = s.Angle; sh.rotation = s.ShapeRot; sh.radiusThickness = s.Thickness; sh.position = s.ShapePos;
            if (s.ShapeScale != Vector3.zero) sh.scale = s.ShapeScale;
            if (s.ArcSpeed > 0f) { sh.arcMode = ParticleSystemShapeMultiModeValue.Loop; sh.arcSpeed = s.ArcSpeed; }
            if (s.Colour != null) { var col = ps.colorOverLifetime; col.enabled = true; col.color = new ParticleSystem.MinMaxGradient(s.Colour); }
            if (s.Grow != null) { var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, s.Grow); }
            if (Mathf.Abs(s.Spin) > .001f || Mathf.Abs(s.Roll) > .001f)
            {
                var rot = ps.rotationOverLifetime; rot.enabled = true;
                if (s.Tumble || Mathf.Abs(s.Roll) > .001f)
                {
                    rot.separateAxes = true;
                    float k = s.Spin * Mathf.Deg2Rad;
                    rot.x = new ParticleSystem.MinMaxCurve(-k, k); rot.y = new ParticleSystem.MinMaxCurve(-k, k);
                    rot.z = Mathf.Abs(s.Roll) > .001f ? new ParticleSystem.MinMaxCurve(s.Roll * Mathf.Deg2Rad) : new ParticleSystem.MinMaxCurve(-k, k);
                }
                else rot.z = new ParticleSystem.MinMaxCurve(-s.Spin * Mathf.Deg2Rad, s.Spin * Mathf.Deg2Rad);
            }
            if (s.Drag > 0f) { var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.drag = s.Drag; lv.limit = 100f; if (s.DragLinear) { lv.multiplyDragByParticleSize = false; lv.multiplyDragByParticleVelocity = false; } }
            if (Mathf.Abs(s.RiseY) > .001f)
            {
                var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
                v.x = new ParticleSystem.MinMaxCurve(0f, 0f); v.z = new ParticleSystem.MinMaxCurve(0f, 0f); v.y = new ParticleSystem.MinMaxCurve(s.RiseY * .7f, s.RiseY * 1.3f);
            }
            if (s.Flipbook && s.Mesh == null)
            {
                var tsa = ps.textureSheetAnimation; tsa.enabled = true; tsa.mode = ParticleSystemAnimationMode.Grid;
                tsa.numTilesX = 4; tsa.numTilesY = 4; tsa.animation = ParticleSystemAnimationType.WholeSheet; tsa.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
                if (s.FrameCurve != null) { tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, s.FrameCurve); tsa.cycleCount = 1; }
                else if (s.RandomCell) { tsa.startFrame = new ParticleSystem.MinMaxCurve(s.FrameFrom, Mathf.Min(s.FrameTo, 15.99f)); tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f); tsa.cycleCount = 1; }
                else if (s.Cycles > 0f) { tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f)); tsa.cycleCount = Mathf.Max(1, Mathf.RoundToInt(s.Cycles)); }
                else
                {
                    if (s.RandomStart) tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 15.99f);
                    float from = s.RandomStart ? 0f : s.FrameFrom / 16f, to = (s.RandomStart ? s.FrameTo - s.FrameFrom : s.FrameTo) / 16f;
                    tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, from, 1f, Mathf.Min(to, .999f))); tsa.cycleCount = 1;
                }
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = s.Mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.maxParticleSize = 3f; r.pivot = s.Pivot; r.enableGPUInstancing = false;
            if (s.Blend) r.SetActiveVertexStreams(BlendStreams);      // UV2 + AnimBlend: the bleed shader interpolates between cels
            if (s.Mesh != null)
            {
                r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = s.Mesh;
                r.alignment = s.AlongVelocity ? ParticleSystemRenderSpace.Velocity : ParticleSystemRenderSpace.Local;
            }
            else if (s.Mode == 1) r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            else if (s.Mode == 2) r.renderMode = ParticleSystemRenderMode.VerticalBillboard;
            else if (s.Mode == 3) { r.renderMode = ParticleSystemRenderMode.Stretch; r.lengthScale = 1f; r.velocityScale = 0f; r.cameraVelocityScale = 0f; }
            else r.renderMode = ParticleSystemRenderMode.Billboard;
            return ps;
        }

        // a mesh body under Travel/Spin (its renderer is hidden at the impact by Vfx120Effect)
        static Transform SpinRoot(Transform travel) { var spin = new GameObject("Spin").transform; spin.SetParent(travel, false); return spin; }

        // thorn and blade are modelled with the tip further from the pivot than the tail: if the import turned them round, turn them back
        static bool Backward(Mesh mesh) => mesh.bounds.center.z < -.01f;

        static void Body(Transform spin, Mesh mesh, Material material, float scale)
        {
            var body = new GameObject("Body"); body.transform.SetParent(spin, false);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = body.AddComponent<MeshRenderer>(); mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            body.transform.localScale = Vector3.one * scale;
            if (Backward(mesh)) body.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }

        static (Transform travel, Transform contact, GameObject root) Shell(string name)
        {
            var root = new GameObject(name);
            var travel = new GameObject("Travel").transform; travel.SetParent(root.transform, false);
            var contact = new GameObject("Contact").transform; contact.SetParent(root.transform, false);
            return (travel, contact, root);
        }

        // the birth: two cels of a faintly glowing stroke on the body as it leaves (the only glow of an ordinary spell).
        // Local, like every Contact layer: Vfx120Effect moves Travel / Contact in jumps, and a world-space system spreads what
        // it emits in that step along the jump (a burst then lands somewhere between the old place and the new one).
        static void Birth(Transform travel, Material glow, Color c, float size)
        {
            Layer(travel, new L { Name = "Birth", Mat = glow, World = false, Burst = 1, Max = 2, Life = new Vector2(2f * Cel, 2f * Cel), Size = new Vector2(size, size), NoShape = true,
                FrameFrom = 0f, FrameTo = 2f, Colour = G((0f, C(c, 1f)), (1f, C(c, .55f))) });
        }

        // the hit: the flung-ink sheet every element shares, in the element's own dark tone
        static void Splat(Transform contact, Material mat, Color c, float size)
        {
            Layer(contact, new L { Name = "Splat", World = false, Mat = mat, Burst = 1, Max = 2, Life = new Vector2(.9f, .9f), Size = new Vector2(size, size), NoShape = true,
                FrameFrom = 0f, FrameTo = 11f, Colour = Melt(c, .5f) });
        }

        sealed class Kit
        {
            public Mesh Thorn, Splinter, Clod, Chunk, Blade, Stalk, Drop, Tongue;
            public Color Ink;
            public Material Sheet(string key, string sheet, Color ink, float heat = 0f, float halo = .18f) => Flip("M_V308_" + key + "_" + sheet, sheet, ink, heat, 0f, halo);
            public Material Glow(string key, string sheet, Color ink, float heat = 0f) => Flip("M_V308_" + key + "_" + sheet + "_birth", sheet, C(ink, .35f), heat, .16f, .3f);
        }

        // ---------------------------------------------------------------- the five

        // wood: a straight living thorn that rolls, splinters peeling off in a spiral; the hit is a fan of straight shards
        static GameObject Ga(Kit k, Vfx120Profile p)
        {
            var (travel, contact, root) = Shell("PF_V308_ga");
            Color pig = p.Pigment, acc = p.Accent, ink = k.Ink, dark = Color.Lerp(pig, ink, .65f);
            var solid = Solid("M_V308_ga_solid", Color.Lerp(pig, ink, .2f), ink);
            var drops = k.Sheet("ga", "ink_drops", C(ink, .7f));
            var spin = SpinRoot(travel);
            Layer(spin, new L { Name = "Thorn", Mat = solid, Mesh = k.Thorn, World = false, Burst = 1, Max = 2, Life = new Vector2(8f, 8f), Size = new Vector2(1.7f, 1.7f), NoShape = true, Roll = 540f });
            Layer(travel, new L { Name = "Helix", Mat = drops, Rate = 110, Max = 56, Life = new Vector2(.22f, .34f), Size = new Vector2(.2f, .34f), Speed = new Vector2(.3f, .9f),
                Shape = ParticleSystemShapeType.Circle, Radius = .17f, Thickness = 0f, ArcSpeed = 2.5f, RandomCell = true, Colour = Melt(pig, .3f, dark) });
            Layer(travel, new L { Name = "Splinters", Mat = solid, Mesh = k.Splinter, Rate = 40, Max = 32, Life = new Vector2(.3f, .45f), Size = new Vector2(.3f, .55f), Speed = new Vector2(1.5f, 3.5f),
                Shape = ParticleSystemShapeType.Cone, Radius = .05f, Angle = 40f, ShapeRot = new Vector3(0f, 180f, 0f), Gravity = .6f, Tumble = true, Spin = 200f, Grow = Curve(0, 1f, 1, 0f) });
            Birth(travel, k.Glow("ga", "wood_splinter", ink), pig, .8f);
            Layer(contact, new L { Name = "Burst", World = false, Mat = k.Sheet("ga", "wood_splinter", C(ink, .8f)), Burst = 1, Max = 2, Life = new Vector2(.9f, .9f), Size = new Vector2(4.2f, 4.2f), NoShape = true, SpinRandom = false,
                FrameFrom = 0f, FrameTo = 11f, Colour = Melt(Color.Lerp(pig, ink, .3f), .55f, dark) });
            Splat(contact, k.Sheet("ga", "ink_splat", C(ink, .9f)), dark, 3.2f);
            Layer(contact, new L { Name = "Shards", World = false, Mat = solid, Mesh = k.Splinter, Burst = 16, Max = 16, Life = new Vector2(.45f, .8f), Size = new Vector2(.5f, .95f), Speed = new Vector2(3f, 7f),
                Shape = ParticleSystemShapeType.Hemisphere, Radius = .1f, Gravity = 1.4f, Tumble = true, Spin = 260f, Grow = Curve(0, 1f, .5f, .8f, 1, 0f) });
            Layer(contact, new L { Name = "Flecks", World = false, Mat = drops, Burst = 12, Max = 16, Life = new Vector2(.4f, .8f), Size = new Vector2(.35f, .6f), Speed = new Vector2(2f, 5f), Gravity = 1.2f, Radius = .1f,
                RandomCell = true, Colour = Melt(dark, .3f) });
            return root;
        }

        // fire: tongues that rise and lick round a burning core (the one spell that keeps a warm tone in flight)
        static GameObject Na(Kit k, Vfx120Profile p)
        {
            var (travel, contact, root) = Shell("PF_V308_na");
            Color pig = p.Pigment, ink = k.Ink, ember = Color.Lerp(pig, ink, .5f), soot = Color.Lerp(pig, ink, .85f);
            var flame = k.Sheet("na", "fire_tongue", C(ink, .3f), 1f);
            var drops = k.Sheet("na", "ink_drops", C(ink, .8f));
            Layer(travel, new L { Name = "Flame", Mat = flame, World = false, Rate = 12, Max = 6, Life = new Vector2(2f * Cel, 2f * Cel), Size = new Vector2(1.5f, 1.75f), NoShape = true, SpinRandom = false,
                RandomCell = true, Pivot = new Vector3(0f, .2f, 0f), Colour = G((0f, C(ember, 1f)), (.5f, C(ember, 1f)), (1f, C(ember, .55f))) });
            Layer(travel, new L { Name = "Licks", Mat = flame, Rate = 10, Max = 12, Life = new Vector2(.4f, .55f), Size = new Vector2(.85f, 1.15f), NoShape = true, SpinRandom = false,
                RandomStart = true, FrameFrom = 0f, FrameTo = 5f, RiseY = 1.1f, Pivot = new Vector3(0f, .2f, 0f), Colour = Melt(ember, .2f, soot), Grow = Curve(0, 1f, 1, .7f) });
            Layer(travel, new L { Name = "Soot", Mat = drops, Rate = 30, Max = 24, Life = new Vector2(.3f, .5f), Size = new Vector2(.3f, .5f), Radius = .15f, Gravity = -.15f, RandomCell = true, Colour = Melt(soot, .3f) });
            Birth(travel, k.Glow("na", "fire_burst", ink, 1f), ember, .8f);
            Layer(contact, new L { Name = "Burst", World = false, Mat = k.Sheet("na", "fire_burst", C(ink, .3f), 1f), Burst = 1, Max = 2, Life = new Vector2(.9f, .9f), Size = new Vector2(4.2f, 4.2f), NoShape = true, SpinRandom = false,
                FrameFrom = 0f, FrameTo = 11f, Colour = Melt(ember, .55f, soot) });
            Layer(contact, new L { Name = "Tongues", World = false, Mat = flame, Burst = 5, Max = 6, Life = new Vector2(.5f, .8f), Size = new Vector2(.9f, 1.4f), Radius = .4f, SpinRandom = false,
                RandomStart = true, FrameFrom = 0f, FrameTo = 8f, RiseY = 1.2f, Colour = Melt(ember, .3f, soot) });
            Splat(contact, k.Sheet("na", "ink_splat", C(ink, .9f)), soot, 3.0f);
            Layer(contact, new L { Name = "Embers", World = false, Mat = drops, Burst = 16, Max = 16, Life = new Vector2(.5f, .9f), Size = new Vector2(.16f, .3f), Speed = new Vector2(2f, 5f), Gravity = -.6f, Drag = 1.5f, Radius = .15f,
                RandomCell = true, Colour = Melt(ember, .3f, soot) });
            return root;
        }

        // earth: a heavy tumbling clod shedding crumbs; the hit rolls dust out low and lobs chunks
        static GameObject Ma(Kit k, Vfx120Profile p)
        {
            var (travel, contact, root) = Shell("PF_V308_ma");
            Color pig = p.Pigment, ink = k.Ink, soil = Color.Lerp(pig, ink, .3f), dark = Color.Lerp(pig, ink, .7f);
            var solid = Solid("M_V308_ma_solid", Color.Lerp(pig, ink, .15f), ink, .6f, .3f);
            var drops = k.Sheet("ma", "ink_drops", C(ink, .6f));
            Body(SpinRoot(travel), k.Clod, solid, .55f);
            Layer(travel, new L { Name = "Crumbs", Mat = solid, Mesh = k.Chunk, Rate = 22, Max = 24, Life = new Vector2(.45f, .7f), Size = new Vector2(.1f, .2f), Speed = new Vector2(.3f, 1f), Radius = .2f,
                Gravity = 2.2f, Tumble = true, Spin = 300f });
            Layer(travel, new L { Name = "Dust", Mat = drops, Rate = 40, Max = 32, Life = new Vector2(.4f, .6f), Size = new Vector2(.5f, .8f), Radius = .15f, Gravity = 1f, RandomCell = true, Colour = Melt(soil, .25f, dark) });
            Birth(travel, k.Glow("ma", "ink_splat", ink), soil, .8f);
            Layer(contact, new L { Name = "DustRoll", World = false, Mat = k.Sheet("ma", "earth_dust", C(ink, .6f)), Burst = 1, Max = 2, Life = new Vector2(.9f, .9f), Size = new Vector2(4.8f, 4.8f), NoShape = true, SpinRandom = false,
                FrameFrom = 0f, FrameTo = 12f, Pivot = new Vector3(0f, .12f, 0f), Colour = Melt(soil, .6f, dark) });
            Layer(contact, new L { Name = "Chunks", World = false, Mat = solid, Mesh = k.Chunk, Burst = 12, Max = 12, Life = new Vector2(.6f, .9f), Size = new Vector2(.16f, .32f), Speed = new Vector2(3f, 6.5f), Radius = .2f,
                Gravity = 2.4f, Tumble = true, Spin = 400f, Grow = Curve(0, 1f, .7f, 1f, 1, 0f) });
            Layer(contact, new L { Name = "Clods", World = false, Mat = solid, Mesh = k.Clod, Burst = 3, Max = 4, Life = new Vector2(.7f, .9f), Size = new Vector2(.22f, .34f), Speed = new Vector2(2f, 4f), Radius = .15f,
                Gravity = 2.6f, Tumble = true, Spin = 240f, Grow = Curve(0, 1f, .7f, 1f, 1, 0f) });
            Splat(contact, k.Sheet("ma", "ink_splat", C(ink, .8f)), dark, 3.4f);
            return root;
        }

        // metal: a thin fast blade inside hair-thin speed lines and a flicking glint; the hit is sharp streaks, gone at once
        static GameObject Sa(Kit k, Vfx120Profile p)
        {
            var (travel, contact, root) = Shell("PF_V308_sa");
            Color pig = p.Pigment, ink = k.Ink, steel = Color.Lerp(pig, ink, .5f), dark = Color.Lerp(pig, ink, .8f);
            var solid = Solid("M_V308_sa_solid", Color.Lerp(pig, ink, .1f), ink, .5f, .3f);
            var line = Solid("M_V308_sa_line", dark, ink, .5f, 0f);
            var streak = k.Sheet("sa", "metal_streak", C(ink, .7f), 0f, .08f);
            Layer(SpinRoot(travel), new L { Name = "Blade", Mat = solid, Mesh = k.Blade, World = false, Burst = 1, Max = 2, Life = new Vector2(8f, 8f), Size = new Vector2(1.25f, 1.25f), NoShape = true, Roll = 900f });
            Layer(travel, new L { Name = "Lines", Mat = line, Mesh = k.Splinter, Rate = 60, Max = 16, Life = new Vector2(.1f, .16f), Size = new Vector2(.8f, 1.2f), Size3D = new Vector3(.5f, .5f, 1.6f),
                Shape = ParticleSystemShapeType.Circle, Radius = .24f, Thickness = .7f, Grow = Curve(0, 1f, 1, .3f) });
            Layer(travel, new L { Name = "Glint", Mat = streak, World = false, Rate = 12, Max = 4, Life = new Vector2(Cel, Cel), Size = new Vector2(1.7f, 2.1f), NoShape = true,
                RandomCell = true, FrameFrom = 0f, FrameTo = 1.99f, Colour = G((0f, C(steel, 1f)), (1f, C(steel, 1f))) });
            Birth(travel, k.Glow("sa", "metal_streak", ink), steel, .9f);
            Layer(contact, new L { Name = "Streaks", World = false, Mat = streak, Burst = 1, Max = 2, Life = new Vector2(8f * Cel, 8f * Cel), Size = new Vector2(4.6f, 4.6f), NoShape = true,
                FrameFrom = 0f, FrameTo = 8f, Colour = G((0f, C(steel, 1f)), (1f, C(dark, 1f))) });
            Splat(contact, k.Sheet("sa", "ink_splat", C(ink, .9f)), dark, 2.0f);
            Layer(contact, new L { Name = "Filings", World = false, Mat = line, Mesh = k.Splinter, Burst = 16, Max = 16, Life = new Vector2(.12f, .25f), Size = new Vector2(.5f, .9f), Size3D = new Vector3(.4f, .4f, 1f), Speed = new Vector2(8f, 14f),
                Radius = .05f, AlongVelocity = true, Grow = Curve(0, 1f, 1, .2f) });
            return root;
        }

        // water: a slow heavy orb that wobbles and drips, a curl of drops behind it; the hit is a crown that falls back
        static GameObject A(Kit k, Vfx120Profile p)
        {
            var (travel, contact, root) = Shell("PF_V308_a");
            Color pig = p.Pigment, acc = p.Accent, ink = k.Ink, water = Color.Lerp(pig, acc, .15f), dark = Color.Lerp(pig, ink, .6f);
            var drops = k.Sheet("a", "ink_drops", C(ink, .6f));
            var spin = SpinRoot(travel);
            Layer(spin, new L { Name = "Orb", Mat = k.Sheet("a", "water_orb", C(Color.Lerp(pig, ink, .7f), .9f)), World = false, Burst = 1, Max = 2, Life = new Vector2(8f, 8f), Size = new Vector2(1.5f, 1.5f), NoShape = true, SpinRandom = false,
                Cycles = 6f, Colour = G((0f, C(water, 1f)), (1f, C(water, 1f))), Grow = Curve(0f, .3f, .05f, 1f, 1f, 1f) });
            Layer(travel, new L { Name = "Wake", Mat = drops, Rate = 14, Max = 16, Life = new Vector2(.5f, .8f), Size = new Vector2(.45f, .75f), Radius = .15f, Gravity = 1.3f, RandomCell = true, Colour = Melt(pig, .35f, dark) });
            Layer(travel, new L { Name = "Curl", Mat = drops, Rate = 22, Max = 20, Life = new Vector2(.5f, .7f), Size = new Vector2(.3f, .45f), Speed = new Vector2(.1f, .4f),
                Shape = ParticleSystemShapeType.Circle, Radius = .36f, Thickness = 0f, ArcSpeed = 1.2f, RandomCell = true, Colour = Melt(Color.Lerp(pig, acc, .3f), .35f, dark) });
            Birth(travel, k.Glow("a", "ink_splat", ink), pig, .8f);
            Layer(contact, new L { Name = "Crown", World = false, Mat = k.Sheet("a", "water_ring", C(Color.Lerp(pig, ink, .7f), .85f)), Burst = 1, Max = 2, Life = new Vector2(.9f, .9f), Size = new Vector2(4.6f, 4.6f), NoShape = true, SpinRandom = false,
                FrameFrom = 0f, FrameTo = 12f, Colour = Melt(water, .6f, dark) });
            Splat(contact, k.Sheet("a", "ink_splat", C(ink, .85f)), dark, 3.0f);
            Layer(contact, new L { Name = "Beads", World = false, Mat = drops, Burst = 14, Max = 16, Life = new Vector2(.6f, .9f), Size = new Vector2(.35f, .6f), Speed = new Vector2(2.5f, 5f), Gravity = 2.6f, Radius = .2f,
                RandomCell = true, Colour = Melt(pig, .4f, dark) });
            return root;
        }

        // ---------------------------------------------------------------- second look: bleeding ink (D308-53 (3))
        // "Softer, bleeding ink": the sheets come from bleed308.py (a wet heart, a wash that pales toward its edge, strands pulled
        // out by a curling flow) and are drawn alpha-blended with frame interpolation (InkBleed308). A stroke fades by paling
        // while it swells. Flung drops are fewer; the bleed is the subject. How much each element bleeds is its character:
        // metal least and shortest, water and earth most. Solid parts (thorn, clod, blade) stay solid ink.

        static readonly List<ParticleSystemVertexStream> BlendStreams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV, ParticleSystemVertexStream.UV2, ParticleSystemVertexStream.AnimBlend,
            ParticleSystemVertexStream.AgePercent,   // D308-84: TEXCOORD1.y, the ink -> pigment soak (InkBleed308 _SoakTo)
        };

        static Texture2D Sheet2(string name)
        {
            string path = Root2 + "/Textures/T_V308b_" + name + ".png";
            var imp = AssetImporter.GetAtPath(path) as TextureImporter ?? throw new Exception("missing " + path + " (run bleed308.py)");
            if (imp.sRGBTexture || imp.alphaIsTransparency || imp.wrapMode != TextureWrapMode.Clamp || !imp.mipmapEnabled || imp.maxTextureSize != 1024)
            {
                imp.textureType = TextureImporterType.Default; imp.sRGBTexture = false; imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.alphaIsTransparency = false; imp.mipmapEnabled = true; imp.wrapMode = TextureWrapMode.Clamp; imp.filterMode = FilterMode.Trilinear;
                imp.maxTextureSize = 1024; imp.textureCompression = TextureImporterCompression.Compressed;
                imp.SaveAndReimport();
            }
            return Load<Texture2D>(path);
        }

        static Material Bleed(string name, string sheet, Color ink, float heat = 0f, float opacity = 1f, float glow = 0f, float soft = -1f, Color? tint = null)
        {
            string path = Root2 + "/Materials/" + name + ".mat";
            var shader = Shader.Find("Oheangbu/SpellVFX308/InkBleed") ?? throw new Exception("InkBleed shader not found");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetTexture("_MainTex", sheet == null ? null : Sheet2(sheet));
            m.SetColor("_Ink", ink); m.SetFloat("_Heat", heat); m.SetFloat("_Opacity", opacity);
            m.SetFloat("_Glow", Mathf.Min(glow, .35f)); m.SetFloat("_Radial", glow > 0f ? 1f : 0f);
            m.SetFloat("_SoftDepth", soft >= 0f ? soft : glow > 0f ? .25f : .5f);
            m.SetColor("_Tint", tint ?? Color.white);
            EditorUtility.SetDirty(m); Touched.Add(m);
            return m;
        }

        // D308-109 (ink first, the pigment soaks in): black ink at birth, the element's colour from a quarter of the life, fading out
        static Gradient InkFirst(Color ink, Color pigment, Color end) => G((0f, C(ink, 1f)), (.25f, C(Color.Lerp(ink, pigment, .6f), 1f)), (.5f, C(pigment, 1f)), (1f, C(end, 0f)));

        // whole, then paling to nothing from `hold` of the life on
        static Gradient Fade(Color c, float hold = .35f, Color? end = null)
        {
            Color e = end ?? c;
            return G((0f, C(c, 1f)), (hold, C(Color.Lerp(c, e, .4f), 1f)), (1f, C(e, 0f)));
        }

        // the birth: a small soft round glow on the body for two cels (the only light of an ordinary spell)
        static void Birth2(Transform travel, string key, Color c, float size)
        {
            Layer(travel, new L { Name = "Birth", Mat = Bleed("M_V308b_" + key + "_birth", null, Color.clear, 0f, .5f, .12f)   /* D308-109 audit: the birth glow cap .12 (D308-103) */, World = false, Burst = 1, Max = 2, Life = new Vector2(2f * Cel, 2f * Cel),
                Size = new Vector2(size, size), NoShape = true, Flipbook = false, SpinRandom = false, Colour = G((0f, C(c, .9f)), (.5f, C(c, .6f)), (1f, C(c, 0f))) });
            // 6th pass (D308-83): the body is made of thrown ink. Where it leaves, ink comes together (ink_gather sheet, the
            // cels in which the drops are already on their way in) and is drawn in as the body goes: ink first, colour after.
            // World space: it stays where the spell left the brush. Plain ink colour, no glow.
            var gatherInk = new Color(.06f, .055f, .05f, 1f);
            Layer(travel, new L { Name = "Gather", Mat = Bleed("M_V308b_ink_gather", "ink_gather", C(gatherInk, .95f), 0f, .95f), Burst = 1, Max = 2, Life = new Vector2(3f * Cel, 3f * Cel),
                Size = new Vector2(size * 1.25f, size * 1.25f), NoShape = true, Blend = true, FrameFrom = 6f, FrameTo = 13f, Colour = G((0f, C(gatherInk, 1f)), (.7f, C(gatherInk, 1f)), (1f, C(gatherInk, 0f))), Grow = Curve(0f, 1.15f, 1f, .45f) });
        }

        // a whole sheet played once over the hit (the .9 s the effect outlives its impact), swelling as it pales
        // depth (5th pass): the wash is not one sheet of paper. With depth 2 or 3 the same sheet is laid again behind and in
        // front of the hit (Contact +Z looks back at the caster), smaller / larger, thinner, and a cel or two out of step, so
        // the layers slide against each other when the camera moves and the hit has a near side and a far side.
        static void Wash(Transform contact, string name, Material mat, Color c, Color end, float size, float life = .9f, float hold = .4f, float swell = 1.2f, float frames = 15.5f, Vector3 pivot = default, bool spin = false, int depth = 1)
        {
            for (int i = 0; i < depth; i++)
            {
                float z = i == 0 ? 0f : i == 1 ? -.7f : .65f, k = i == 0 ? 1f : i == 1 ? .82f : 1.12f, a = i == 0 ? 1f : i == 1 ? .75f : .5f, start = i == 0 ? 0f : i == 1 ? 1.5f : 3f;
                Color c0 = i == 1 ? Color.Lerp(c, end, .5f) : c;
                var g = G((0f, C(c0, a)), (hold, C(Color.Lerp(c0, end, .4f), a)), (1f, C(end, 0f)));
                Layer(contact, new L { Name = name + (i == 0 ? "" : i == 1 ? "_far" : "_near"), Mat = mat, World = false, Burst = 1, Max = 2, Life = new Vector2(life, life), Size = new Vector2(size, size) * k, Radius = .001f, ShapePos = new Vector3(i == 2 ? .35f : i == 1 ? -.3f : 0f, 0f, z),
                    SpinRandom = spin, Blend = true, FrameFrom = start, FrameTo = frames, Pivot = pivot, Colour = g, Grow = Curve(0f, 1f, 1f, swell) });
            }
        }

        // drifting blots left behind in flight (single stills from the drops sheet, each with its own bleed)
        static void Blots(Transform travel, string name, Material mat, Color c, Color end, float rate, Vector2 size, Vector2 life, float gravity = 0f, float radius = .12f, float arc = 0f, float swell = 1.6f)
        {
            Layer(travel, new L { Name = name, Mat = mat, Rate = rate, Max = Mathf.CeilToInt(rate * life.y) + 4, Life = life, Size = size, Radius = radius, Gravity = gravity, RandomCell = true, Blend = true,
                Shape = arc > 0f ? ParticleSystemShapeType.Circle : ParticleSystemShapeType.Sphere, Thickness = arc > 0f ? 0f : 1f, ArcSpeed = arc, Speed = new Vector2(.1f, .5f),
                Colour = Fade(c, .25f, end), Grow = Curve(0f, .8f, 1f, swell) });
        }

        static Material B(string key, string sheet, Color ink, float heat = 0f, float opacity = 1f) => Bleed("M_V308b_" + key + "_" + sheet, sheet, ink, heat, opacity);

        // ---- D308-84 (SPEC-SPELL-VFX-OVERHAUL-308 11.21): the body forms from the ink, then the pigment soaks in; ink stays ----
        const float BodySoakSeconds = .6f;    // solid body: ink -> pigment after leaving (TEST; sw_t .35 was not readable)
        const float ParticleSoakTo = .6f;     // bleeding particles: pigment full at this fraction of their life (TEST; .45)
        const float TravelScale = 1.35f;      // the flying body only (sw_w: a small dark dot at 0.2x)
        const float NearContactMeters = 18f;  // hits nearer the camera than this shrink, to 45 % (TEST; 12 left the earth hit wide)
        const float SingleHitScale = .4f;     // single hits: the bolt's own Contact splat is the hit - the contact seam only adds a small splat (sw_r: x1 covered a third of the view at 5 m)    // bleeding particles: pigment full at this fraction of their life (TEST)

        static Material SoakVariant(Material m)
        {
            string path = AssetDatabase.GetAssetPath(m);
            if (string.IsNullOrEmpty(path) || path.EndsWith("_soak.mat", StringComparison.Ordinal)) return m;
            string soakPath = path.Substring(0, path.Length - 4) + "_soak.mat";
            var v = AssetDatabase.LoadAssetAtPath<Material>(soakPath);
            if (v == null) { v = new Material(m); AssetDatabase.CreateAsset(v, soakPath); } else { v.shader = m.shader; v.CopyPropertiesFromMaterial(m); }
            v.SetColor("_SoakInk", SwingInkColour); v.SetFloat("_SoakFrom", 0f); v.SetFloat("_SoakTo", ParticleSoakTo);
            EditorUtility.SetDirty(v); Touched.Add(v);
            return v;
        }

        // Deploy: scattered ink drawn together at the origin (the splat sheet backwards), 0.15 s. No Residue (D308-109). Bleeding Travel particles
        // swap to a soak copy of their material (black first, pigment by ParticleSoakTo of their life). Glow cels stay as they are.
        static void Bolt308Extras(GameObject root)
        {
            var streams = new List<ParticleSystemVertexStream>();
            foreach (var r in root.transform.Find("Travel").GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                var m = r.sharedMaterial;
                if (m == null || m.shader == null || m.shader.name != "Oheangbu/SpellVFX308/InkBleed" || m.GetFloat("_Radial") > .5f) continue;
                streams.Clear(); r.GetActiveVertexStreams(streams);
                if (streams.Contains(ParticleSystemVertexStream.AgePercent)) r.sharedMaterial = SoakVariant(m);
            }
            var deploy = new GameObject("Deploy").transform; deploy.SetParent(root.transform, false);
            Layer(deploy, new L { Name = "Gather", Mat = Bleed("M_V308c_deploy_splat", "ink_splat", C(SwingInkColour, .9f), 0f, .9f), World = false, Burst = 1, Max = 2, Life = new Vector2(.15f, .15f),
                Size = new Vector2(1.1f, 1.1f), NoShape = true, Blend = true, FrameFrom = 11f, FrameTo = 0f,
                Colour = G((0f, C(SwingInkColour, 0f)), (.3f, C(SwingInkColour, 1f)), (1f, C(SwingInkColour, .7f))), Grow = Curve(0f, 1.25f, 1f, .5f) });
            // D308-109 audit (rule: no flying droplets, single hits leave KTP only): the deploy drops and the residue pool are gone.
            // Without a Residue child the runtime's DropResidue308 does nothing; FinalK's Linger finds no residue either.
            foreach (var t in deploy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = AfterFogLayer;
        }

        static GameObject Ga2(Kit k, Vfx120Profile p)
        {
            var (travel, contact, root) = Shell("PF_V308b_ga");
            Color pig = p.Pigment, ink = k.Ink, leaf = Color.Lerp(pig, ink, .45f), dark = Color.Lerp(pig, ink, .75f);
            var solid = Solid("M_V308_ga_solid", Color.Lerp(pig, ink, .2f), ink);
            var drops = B("ga", "ink_drops", C(ink, .85f));
            // D308-86: ink that keeps forming instead of the thorn mesh; wood = fine ink sprigs reaching out of it
            var gaSpin = InkMass(travel, "ga", ink, leaf, dark, 1.15f);
            Birth2(travel, "ga", pig, 1.1f);
            Wash(contact, "Burst", B("ga", "wood_splinter", C(ink, .9f)), leaf, dark, 6.2f, .9f, .4f, 1.12f, depth: 3);
            Wash(contact, "Splat", B("ga", "ink_splat", C(ink, .9f), 0f, .85f), dark, dark, 4.2f, .9f, .35f, 1.25f, 15.5f, default, true);
            // user 2026-10-15 questionnaire "술식 쪽만 빼기": the drops falling off the flight / thrown off the hit are gone (the swing's own ink stays)
            return root;
        }

        static GameObject Na2(Kit k, Vfx120Profile p)
        {
            var (travel, contact, root) = Shell("PF_V308b_na");
            Color pig = p.Pigment, ink = k.Ink, ember = Color.Lerp(pig, ink, .45f), soot = Color.Lerp(pig, ink, .85f);
            var flame = B("na", "fire_tongue", C(ink, .35f), 1f, .85f);
            var drops = B("na", "ink_drops", C(ink, .85f), 0f, .8f);
            var naSpin = InkMass(travel, "na", ink, ember, soot, .9f, rate: 22f, shed: 20f);   // D308-86: ink under the flame, not a coal
            Layer(naSpin, new L { Name = "Flame", Mat = flame, World = false, Burst = 1, Max = 2, Life = new Vector2(8f, 8f), Size = new Vector2(2.5f, 2.5f), NoShape = true, SpinRandom = false, Blend = true,
                Cycles = 6f, Pivot = new Vector3(0f, .2f, 0f), Colour = G((0f, C(ember, 1f)), (1f, C(ember, 1f))), Grow = Curve(0f, .35f, .04f, 1f, 1f, 1f) });
            Layer(travel, new L { Name = "Licks", Mat = flame, Rate = 9, Max = 10, Life = new Vector2(.45f, .6f), Size = new Vector2(1.3f, 1.8f), NoShape = true, SpinRandom = false, Blend = true,
                RandomStart = true, FrameFrom = 0f, FrameTo = 6f, RiseY = 1.2f, Pivot = new Vector3(0f, .2f, 0f), Colour = Fade(ember, .15f, soot), Grow = Curve(0, .9f, 1, 1.4f) });
            Birth2(travel, "na", new Color(.6f, .28f, .1f), 1.2f);
            // 6th pass (sheets r5): from 12 m the hit was one red mass. Its own burst material with half the heat and more ink, one
            // layer fewer, smaller; fewer tongues; the ink splat under it is the larger shape now.
            Wash(contact, "Burst", Bleed("M_V308b_na_fire_burst_hit", "fire_burst", C(ink, .7f), .5f, 1f), Color.Lerp(ember, soot, .45f), soot, 5.2f, .9f, .3f, 1.15f, depth: 2);
            Layer(contact, new L { Name = "Tongues", World = false, Mat = flame, Burst = 4, Max = 5, Life = new Vector2(.55f, .85f), Size = new Vector2(1.5f, 2.1f), Radius = .6f, SpinRandom = false, Blend = true,
                RandomStart = true, FrameFrom = 0f, FrameTo = 8f, RiseY = 1.7f, Colour = Fade(ember, .3f, soot), Grow = Curve(0, .9f, 1, 1.3f) });
            Wash(contact, "Splat", B("na", "ink_splat", C(ink, .9f), 0f, .95f), soot, soot, 5.6f, .9f, .4f, 1.25f, 15.5f, default, true);
            // user 2026-10-15 questionnaire "술식 쪽만 빼기": the drops falling off the flight / thrown off the hit are gone (the swing's own ink stays)
            return root;
        }

        static GameObject Ma2(Kit k, Vfx120Profile p)
        {
            var (travel, contact, root) = Shell("PF_V308b_ma");
            Color pig = p.Pigment, ink = k.Ink, soil = Color.Lerp(pig, ink, .3f), dark = Color.Lerp(pig, ink, .7f);
            var solid = Solid("M_V308_ma_solid", Color.Lerp(pig, ink, .15f), ink, .6f, .3f);
            var drops = B("ma", "ink_drops", C(ink, .6f));
            // D308-86: a heavy mass of earth-grained ink instead of the clod; ink drops fall off it instead of crumbs
            InkMass(travel, "ma", ink, soil, dark, 1.05f, "earth_dust", 24f, 40f, .34f);
            Birth2(travel, "ma", soil, 1.0f);
            Wash(contact, "DustRoll", B("ma", "earth_dust", C(ink, .6f)), soil, dark, 6.8f, .9f, .5f, 1.12f, 15.5f, new Vector3(0f, .12f, 0f), depth: 3);
            Wash(contact, "Splat", B("ma", "ink_splat", C(ink, .8f), 0f, .85f), dark, dark, 4.6f, .9f, .35f, 1.25f, 15.5f, default, true);
            // user 2026-10-15 questionnaire "술식 쪽만 빼기": the drops falling off the flight / thrown off the hit are gone (the swing's own ink stays)
            return root;
        }

        // metal bleeds least and is gone first: its sheets hardly wash, its hit lasts seven cels
        static GameObject Sa2(Kit k, Vfx120Profile p)
        {
            var (travel, contact, root) = Shell("PF_V308b_sa");
            Color pig = p.Pigment, ink = k.Ink, steel = Color.Lerp(pig, ink, .5f), dark = Color.Lerp(pig, ink, .8f);
            var line = Solid("M_V308_sa_line", dark, ink, .5f, 0f);
            var streak = B("sa", "metal_streak", C(ink, .8f));
            // D308-86: dry, quick ink instead of the blade (metal bleeds least: short-lived cels, little breath behind)
            InkMass(travel, "sa", ink, steel, dark, .85f, rate: 26f, spin: 0f, life: .16f, shed: 14f);
            Layer(travel, new L { Name = "Glint", Mat = streak, World = false, Rate = 12, Max = 4, Life = new Vector2(Cel, Cel), Size = new Vector2(2.8f, 3.3f), NoShape = true, Blend = true,
                RandomCell = true, FrameFrom = 0f, FrameTo = 1.99f, Colour = G((0f, C(steel, 1f)), (1f, C(steel, .8f))) });
            Birth2(travel, "sa", steel, 1.1f);
            Wash(contact, "Streaks", streak, steel, dark, 7f, 7f * Cel, .5f, 1.05f, 8f, default, true, depth: 2);
            Wash(contact, "Splat", B("sa", "ink_splat", C(ink, .9f), 0f, .8f), dark, dark, 2.6f, .5f, .3f, 1.1f, 9f, default, true);
            return root;
        }

        static GameObject A2(Kit k, Vfx120Profile p)
        {
            var (travel, contact, root) = Shell("PF_V308b_a");
            Color pig = p.Pigment, acc = p.Accent, ink = k.Ink, water = Color.Lerp(pig, acc, .15f), dark = Color.Lerp(pig, ink, .6f);
            var deep = C(Color.Lerp(pig, ink, .7f), .9f);
            var drops = B("a", "ink_drops", deep);
            var aSpin = InkMass(travel, "a", ink, water, dark, .95f, rate: 24f, spin: 25f, shed: 22f);   // D308-86: round wet ink inside the veil, not a bead
            // the wobbling wash is now a veil round the solid bead, not a black disc over it
            Layer(aSpin, new L { Name = "Orb", Mat = Bleed("M_V308b_a_water_orb_veil", "water_orb", C(Color.Lerp(pig, ink, .7f), .3f), 0f, .6f), World = false, Burst = 1, Max = 2, Life = new Vector2(8f, 8f), Size = new Vector2(2.2f, 2.2f), NoShape = true, SpinRandom = false, Blend = true,
                Cycles = 6f, Colour = G((0f, C(water, 1f)), (1f, C(water, 1f))), Grow = Curve(0f, .3f, .05f, 1f, 1f, 1f) });
            Birth2(travel, "a", water, 1.0f);
            Wash(contact, "Crown", B("a", "water_ring", deep), water, dark, 6.6f, .9f, .5f, 1.12f, depth: 3);
            Wash(contact, "Splat", B("a", "ink_splat", C(ink, .85f), 0f, .8f), dark, dark, 4.0f, .9f, .35f, 1.25f, 15.5f, default, true);
            // user 2026-10-15 questionnaire "술식 쪽만 빼기": the drops falling off the flight / thrown off the hit are gone (the swing's own ink stays)
            return root;
        }

        // ---------------------------------------------------------------- attack 50 (3rd pass): element x form
        // The 50 attack rows of the vocabulary (Docs/오행부_작도어휘_v0_1.csv, read only) are five elements x ten letters, and the
        // rules (Data/Spells/Rules308_*.csv, AreaShape) give each a FORM. A form is one assembly rule (a template below) that is
        // dressed in an element; no letter is built by hand. Seams (runtime code is not touched):
        //   Single (21 letters) - Vfx120Profile.Bolt300Prefab: the element's Travel / Contact prefab, scaled per letter
        //   Circle / Cone / Volley / Path (25) - Vfx120Profile.AreaRemake + NativeBodyPrefab (Vfx120Effect.AreaRemake.cs, the
        //       plain variants: BambooField = 17 bodies at the plan's spikes, FlameCone = one body whose particle shapes become
        //       the cone, NeedleVolley = one body per shot, SandFront = one body riding the front, scaled to its width)
        //   Giyeok (각 낙 삭 악) - NOT built: those casts never read the visual set (EAGiyeokRuntime / EAGiyeokProfileSO prefabs)
        // In this alphabet a form is tied to elements (Circle = wood, Cone = fire, Volley = metal, Path = earth, water and 공);
        // the templates take any element all the same.
        enum Form { Single, Circle, Cone, Volley, Path, Giyeok }
        sealed class Atk
        {
            public string Glyph, Effect, Elem; public Form Shape; public float Scale = 1f, Radius, Length, Speed, Delay, Angle, Interval; public int Shots;
            // 4th pass: what tells the letters of one form apart - size, density, tempo - as DATA read off the rule rows (range,
            // persistence, shot count / weight). The templates never look at the letter.
            public float Big = 1f, Dense = 1f, Slow = 1f;
            public Atk V(float big, float dense, float slow) { Big = big; Dense = dense; Slow = slow; return this; }
            public string Variant => "b" + Mathf.RoundToInt(Big * 100) + "d" + Mathf.RoundToInt(Dense * 100) + "s" + Mathf.RoundToInt(Slow * 100);
        }
        static Atk One(string g, string id, string e, float scale = 1f) => new Atk { Glyph = g, Effect = id, Elem = e, Shape = Form.Single, Scale = scale };
        static Atk Gi(string g, string id, string e) => new Atk { Glyph = g, Effect = id, Elem = e, Shape = Form.Giyeok };
        static Atk Circle(string g, string id, string e, float r) => new Atk { Glyph = g, Effect = id, Elem = e, Shape = Form.Circle, Radius = r, Length = 10f, Delay = .3f };
        static Atk Cone(string g, string id, string e) => new Atk { Glyph = g, Effect = id, Elem = e, Shape = Form.Cone, Angle = 30f, Length = 8f, Delay = .25f };   // angle / range live in CombatConfig: assumed here for the pose only
        static Atk Volley(string g, string id, string e) => new Atk { Glyph = g, Effect = id, Elem = e, Shape = Form.Volley, Shots = 24, Interval = .045f, Radius = 45f, Length = 16f, Speed = 32f, Delay = 1.45f };
        static Atk Sweep(string g, string id, string e, float half, float length, float speed, float delay) => new Atk { Glyph = g, Effect = id, Elem = e, Shape = Form.Path, Radius = half, Length = length, Speed = speed, Delay = delay };
        // numbers = Rules308 (pose only; in the game the plan carries them)
        static readonly Atk[] Attack =
        {
            One("가", "001_AC00", "ga"), Gi("각", "002_AC01", "ga"), One("간", "003_AC04", "ga"), One("갓", "005_AC13", "ga", 1.1f), One("강", "006_AC15", "ga", 1.1f),
            Circle("고", "013_ACE0", "ga", 2.5f), Circle("곡", "014_ACE1", "ga", 3.5f).V(1.3f, 1.3f, 1.35f), Circle("곤", "015_ACE4", "ga", 2.5f).V(1.25f, .8f, .75f), Circle("곳", "017_ACF3", "ga", 2.5f).V(1f, 1.5f, 1.4f), Sweep("공", "018_ACF5", "ga", 3f, 12f, 5f, .4f).V(.9f, 1.2f, 1.1f),
            One("나", "025_B098", "na"), Gi("낙", "026_B099", "na"), One("난", "027_B09C", "na", 1.25f), One("낫", "029_B0AB", "na"), One("낭", "030_B0AD", "na", 1.1f),
            Cone("노", "037_B178", "na"), Cone("녹", "038_B179", "na").V(.9f, .8f, 1.3f), Cone("논", "039_B17C", "na").V(1.25f, 1f, .9f), Cone("놋", "041_B18B", "na").V(1f, 1.35f, 1f), Cone("농", "042_B18D", "na").V(1.1f, 1f, 1.6f),
            One("마", "049_B9C8", "ma"), One("막", "050_B9C9", "ma", 1.15f), One("만", "051_B9CC", "ma", 1.2f), One("맛", "053_B9DB", "ma"), One("망", "054_B9DD", "ma"),
            Sweep("모", "061_BAA8", "ma", 6f, 12f, 7f, .4f), Sweep("목", "062_BAA9", "ma", 6f, 12f, 7f, .4f).V(1.15f, 1f, 1.4f), Sweep("몬", "063_BAAC", "ma", 6f, 12f, 7f, .4f).V(1f, 1.35f, 1f), Sweep("못", "065_BABB", "ma", 6f, 12f, 7f, .4f).V(.88f, 1f, 1.2f), Sweep("몽", "066_BABD", "ma", 6f, 12f, 7f, .4f).V(1.15f, .9f, 1.7f),
            One("사", "073_C0AC", "sa"), Gi("삭", "074_C0AD", "sa"), One("산", "075_C0B0", "sa"), One("삿", "077_C0BF", "sa", 1.1f), One("상", "078_C0C1", "sa"),
            Volley("소", "085_C18C", "sa"), Volley("속", "086_C18D", "sa").V(.85f, 1f, 1f), Volley("손", "087_C190", "sa").V(1f, 1.4f, 1f), Volley("솟", "089_C19F", "sa").V(1.3f, 1f, 1f), Volley("송", "090_C1A1", "sa").V(1.1f, .8f, 1f),
            One("아", "097_C544", "a"), Gi("악", "098_C545", "a"), One("안", "099_C548", "a"), One("앗", "101_C557", "a"), One("앙", "102_C559", "a"),
            Sweep("오", "109_C624", "a", 6f, 14f, 6f, .45f), Sweep("옥", "110_C625", "a", 6f, 14f, 6f, .45f).V(1f, 1f, 1.4f), Sweep("온", "111_C628", "a", 6f, 14f, 6f, .45f).V(1.12f, .8f, 1f), Sweep("옷", "113_C637", "a", 6f, 14f, 6f, .45f).V(1f, 1.35f, 1f), Sweep("옹", "114_C639", "a", 6f, 14f, 6f, .45f).V(1f, 1f, 1.6f),
        };

        static string AttackEffectPath(Atk a) => Root2 + "/Effects/" + a.Effect + "_308b.prefab";
        static string AttackProfilePath(Atk a) => Root2 + "/Profiles/" + a.Effect + "_308b.asset";
        static string AreaBodyPath(Atk a) => Root2 + "/Prefabs/PF_V308b_" + a.Shape.ToString().ToLowerInvariant() + "_" + a.Elem + "_" + a.Variant + ".prefab";
        static string HitPath(string elem) => Root2 + "/Prefabs/PF_V308b_hit_" + elem + ".prefab";

        // an element as the form templates see it
        sealed class Elem
        {
            public string Key; public Color Pig, Ink, Wash, Dark; public Material Solid, Line, Drops, Burst, Flame; public Mesh Spike, Bit; public float Heat;
            public string BurstSheet;
            // range forms: the upright stroke of a circle, the tongue of a cone, the head of a path front, the ground wash, the volley ribbon
            public Material Stroke, StrokeSolid, Lick, LickSoot, Strand, Head, Pool, Ring, Ribbon; public Mesh Stalk, Tongue; public Vector2 HeadSize = new Vector2(3.4f, 4.4f), HeadLife = new Vector2(.75f, .95f); public float HeadPivot = .3f, HeadRate = 12f;
        }

        static Elem Element(Kit k, string key)
        {
            var spell = Spells.First(x => x.Key == key);
            var p = Load<Vfx120Profile>("Assets/_Project/Art/SpellVFX120/Profiles/" + spell.Effect + ".asset");
            Color pig = p.Pigment, ink = p.Ink;
            var e = new Elem { Key = key, Pig = pig, Ink = ink };
            switch (key)
            {
                case "ga": e.Wash = Color.Lerp(pig, ink, .45f); e.Dark = Color.Lerp(pig, ink, .75f); e.BurstSheet = "wood_splinter"; e.Spike = k.Thorn; e.Bit = k.Splinter;
                    e.Solid = Solid("M_V308_ga_solid", Color.Lerp(pig, ink, .2f), ink); break;
                case "na": e.Wash = Color.Lerp(pig, ink, .45f); e.Dark = Color.Lerp(pig, ink, .85f); e.BurstSheet = "fire_burst"; e.Spike = k.Splinter; e.Bit = k.Splinter; e.Heat = 1f;
                    e.Solid = Solid("M_V308_na_solid", Color.Lerp(pig, ink, .6f), ink); break;
                case "ma": e.Wash = Color.Lerp(pig, ink, .3f); e.Dark = Color.Lerp(pig, ink, .7f); e.BurstSheet = "earth_dust"; e.Spike = k.Clod; e.Bit = k.Chunk;
                    e.Solid = Solid("M_V308_ma_solid", Color.Lerp(pig, ink, .15f), ink, .6f, .3f); break;
                case "sa": e.Wash = Color.Lerp(pig, ink, .5f); e.Dark = Color.Lerp(pig, ink, .8f); e.BurstSheet = "metal_streak"; e.Spike = k.Blade; e.Bit = k.Splinter;
                    e.Solid = Solid("M_V308_sa_solid", Color.Lerp(pig, ink, .1f), ink, .5f, .3f); break;
                default: e.Wash = Color.Lerp(pig, p.Accent, .15f); e.Dark = Color.Lerp(pig, ink, .6f); e.BurstSheet = "water_ring"; e.Spike = k.Splinter; e.Bit = k.Splinter;
                    e.Solid = Solid("M_V308_a_solid", Color.Lerp(pig, ink, .5f), ink); break;
            }
            e.Line = Solid("M_V308_" + key + "_line", e.Dark, ink, .5f, 0f);
            e.Drops = B(key, "ink_drops", C(ink, .85f));
            e.Burst = B(key, e.BurstSheet, C(ink, key == "na" ? .35f : .9f), e.Heat);
            e.Flame = key == "na" ? B(key, "fire_tongue", C(ink, .35f), 1f) : e.Burst;
            e.Stroke = key == "ga" ? B(key, "wood_stroke", C(ink, .92f)) : e.Burst;
            e.Stalk = key == "ga" ? k.Stalk : e.Spike;
            e.StrokeSolid = Solid("M_V308_" + key + "_stroke", Color.Lerp(pig, ink, .78f), ink, .5f, .3f);   /* 7th pass (sw_w): still green posts - more ink */      // 6th pass: ink first - the canes read as green bamboo posts
            e.Lick = key == "na" ? B(key, "fire_lick", C(ink, .3f), 1f) : e.Burst;
            e.LickSoot = key == "na" ? Bleed("M_V308b_na_fire_lick_soot", "fire_lick", C(ink, .85f), .3f, .92f) : e.Burst;       // mostly soot, a little heat at the root
            e.Strand = Solid("M_V308_" + key + "_strand", Color.Lerp(pig, ink, key == "na" ? .62f : .55f), ink, .45f, .3f);      // 6th pass: soot-dark strands, not red flakes
            e.Tongue = k.Tongue;
            e.Ring = Bleed("M_V308b_" + key + "_ring", "ink_pool", C(ink, .9f), 0f, .9f, 0f, 0f);
            e.Head = e.Burst;
            if (key == "ga") { e.Head = e.Stroke; e.HeadSize = new Vector2(2.8f, 3.6f);   /* sw_y: the path heads walled the view - all smaller */ e.HeadPivot = .36f; e.HeadRate = 14f; }
            if (key == "ma") { e.Head = B(key, "earth_heave", C(ink, .7f)); e.HeadSize = new Vector2(3f, 3.8f); e.HeadLife = new Vector2(.8f, 1f);   /* sw_y 4.6 - 5.8: an earth wall over 2/3 of the view */ e.HeadPivot = .36f; e.HeadRate = 12f; }
            if (key == "a") { e.Head = B(key, "water_crest", C(Color.Lerp(pig, ink, .7f), .95f)); e.HeadSize = new Vector2(3.4f, 4.2f); e.HeadLife = new Vector2(.6f, .8f); e.HeadPivot = .24f; e.HeadRate = 15f; }
            e.Pool = Bleed("M_V308b_" + key + "_pool", "ink_splat", C(ink, .9f), 0f, .9f, 0f, 0f);                          // lies on the ground: no depth fade
            e.Ribbon = Bleed("M_V308b_" + key + "_ribbon", "speed_line", C(ink, .95f), 0f, 1f, 0f, .3f, e.Dark);
            return e;
        }

        static GameObject Upright(Transform parent, string name, Mesh mesh, Material mat, float height)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            // the meshes fly along +Z: stand them on their tail
            bool back = Backward(mesh); var b = mesh.bounds; float len = Mathf.Max(.01f, b.size.z), k = height / len;
            go.transform.localRotation = Quaternion.Euler(back ? 90f : -90f, 0f, 0f);
            go.transform.localScale = Vector3.one * k;
            go.transform.localPosition = Vector3.up * ((back ? b.max.z : -b.min.z) * k);
            return go;
        }

        // Circle (5th pass): the GROUND is the subject. At each of the 17 planned points ink wells out flat over the ground - a
        // pool with a thin middle and a gathered rim (ink_pool sheet), large enough that the pools run together and their
        // outer rims read as the edge of one ring (the seam gives points, not a radius). Over each point ONE thick jointed
        // cane with a round section and a cut top (never a point), textured and shaded as wood; the seam's _Visibility eats
        // it away through its grain as it goes. A few blots bleed off the canes. No blade leaves, no spear silhouettes.
        static GameObject CircleBody(Elem e, Atk a)
        {
            var root = new GameObject("PF_V308b_circle_" + e.Key);
            // 6th pass (edit-mode sheets r5, 2026-10-14): the pools were too small to run together and the canes read as a fence of posts.
            // The ground ink is now the larger thing (ink colour, not the element's dark), the cane is lower and ink-dark, and
            // ink leaves its top the whole time it stands (a stroke that bleeds away, not a post).
            Upright(root.transform, "Stalk", e.Stalk, e.StrokeSolid, 1.25f * a.Big);
            // D308-109 audit: the pool is held to the AreaMark308 cap (r + 1.8 m) - its rim reaches 1.73 m past a point at any Big; the band carries the ring.
            Layer(root.transform, new L { Name = "Pool", Mat = e.Ring, World = false, Mode = 1, Rate = 3f * a.Dense, Max = Mathf.CeilToInt(3.4f * a.Dense * a.Slow) + 1, Life = new Vector2(.9f, 1.1f) * a.Slow, Size = new Vector2(2.9f, 3.4f), Radius = .01f, ShapePos = new Vector3(0f, .08f, 0f), Blend = true,
                FrameFrom = 3f, FrameTo = 12f, Colour = Fade(Color.Lerp(e.Dark, e.Ink, .75f), .75f, e.Ink), Grow = Curve(0f, .85f, 1f, 1.2f) });
            // user 2026-10-15 questionnaire "술식 쪽만 빼기": the drops falling off the flight / thrown off the hit are gone (the swing's own ink stays)
            return root;
        }

        // Cone (5th pass): strands, not a red sheet. Solid tongue meshes (round, curling, shaded; warm at the root, soot at
        // the tip) are thrown down the cone along their own velocity - each strand reads on its own and there is air between
        // them - with only a few soot washes and blots among them. A strand grows as it travels, so what is near the caster
        // is thin and low and the volume is out in front. Mostly soot and pale wash; red only near the roots.
        static readonly Vector3 FanFlat = new Vector3(1f, .3f, 1f), FanTip = new Vector3(6f, 0f, 0f);   // pq_area: the fan's cone, pressed flat and tipped down (TEST)
        static GameObject ConeBody(Elem e, Atk a)
        {
            var root = new GameObject("PF_V308b_cone_" + e.Key);
            // pq_area (2026-10-15): the tongues flew up and round the caster like red feathers and the wash was faint - every
            // emitter's cone is pressed flat into a fan over the ground (FanFlat) and tipped down a little, the tongues are smaller,
            // the ground wash and the soot denser. pq_fan (11.29 on): the fan stopped ~2 m out against a 6 m reach - faster, less drag; pq_fan3 probe: the sheets stopped at ~1 m (drag times size), the tongues at 3.4 m - plain drag.
            // user 2026-10-15 "'노'를 이 방식인데 입자만 없는 버전으로 ... 꼭 부채꼴 모양일 필요는 없어" (pq_fan4 picture): no flying bits -
            // the flame jet and the blots are gone, the scorched ground ink and the soot wash carry the spell.
            // user 2026-10-15 "여러겹의 불 입자가 겹쳐있는게 너무 부자연스럽게": a third of the sheets, each larger and longer-lived -
            //   the wash reads as one scorch from the caster to the target instead of a stack of overlapping flame cards.
            // 6th pass (sheets r5): the strands alone were a handful of red flakes with nothing between them. A flat ink wash is
            // thrown over the ground under the fan and the soot is what fills it; the strands are the dark threads in it.
            // user 2026-10-15 "판을 만들어서 퍼뜨리는 것보다 연기같은 VFX로 현실적인 느낌을 더 살릴 수는 없을까?" then, showing the
            // early soot of pq_drop, "내가 의도한 건 이런 연기였는데": the torn soot wisps are the spell. The red ground wash (the
            // stacked red cards) is gone; the soot lives longer, only its wispy cels play (0-6 of fire_lick), it slows hard (linear
            // drag: reach ~ v^2 / 2 drag, 3.5 - 7 m) and as it stops the rise turns the stretched wisps upward - smoke settling.
            // (The billow spike, smoke308.py, read as red cartoon puffs - not used.)
            // pq_smoke: fire_lick's cels end in a straight root block at their left border = a card edge in game -> soot_wisp (smoke308.py wisp: that root faded out)
            // D308-88 (11.31): the VFX Graph smoke carries the spell when the graph is there; the soot sheet stays as the fallback
            // 11.34 fire8 (user 2026-10-16 "C 그을음 섞인 불" + a ring round the target, several sheets): the soot-tipped tongues,
            // the outward-leaning ring, the scorched road and the soot column; the soot carpet (SmokeVfx) is off for 노
            // 11.33 fire15 (user 2026-10-16 "길자국 없애고"): the scorched road is off (ScorchGraph kept, tag na-fire-308-v15-scorch)
            // 11.34 fire16 (user 2026-10-16 "네가 자유롭게 만든다면 ... 그렇게 진행해줘"): the ring is painted brush tongues over a
            // soot wash and a red-black ground pool
            // 11.34 fire17 (same instruction): the baked fluid band + the splash stain replace the painted tongue ring, the soot
            // wash and the pool (RingGraph / WashGraph / PoolGraph, their graphs and sheets stay, off; tag na-fire-308-v16)
            // 11.34 fire18 (user 2026-10-16 "시전자 쪽에서 작게 시작된 불씨가 시전 지역으로 가서 번지는듯한 느낌으로. 지금 vfx는 아주
            // 좋아"): the same fire, lit by an ember thrown from the brush tip - the bank comes up as five arcs, near side first
            // (Vfx308SeamTiming on each host); the 17th single bank = BankGraph in place of EmberGraph + BankArcGraphs (tag na-fire-308-v17)
            if (ConeFireGraph(root.transform, e, a))
            {
                EmberGraph(root.transform, e, a); BankArcGraphs(root.transform, e, a); StainGraph(root.transform, e, a); ColumnGraph(root.transform, e, a);
                return root;
            }
            if (ConeSmokeGraph(root.transform, e, a)) return root;
            var soot = e.Key == "na" ? Bleed("M_V308b_na_soot_wisp", "soot_wisp", C(e.Ink, .85f), .3f, .92f) : e.LickSoot;
            Layer(root.transform, new L { Name = "Soot", Mat = soot, Mode = 3, Rate = 42f * a.Dense, Max = Mathf.CeilToInt(18 * a.Dense), Life = new Vector2(1.0f, 1.4f) * a.Slow, Size = new Vector2(1.6f, 2.3f) * a.Big,
                Speed = new Vector2(7f, 10f), Drag = 7f, DragLinear = true, SpinRandom = false, Blend = true, RiseY = .9f,
                Shape = ParticleSystemShapeType.Cone, Angle = 30f, ShapeScale = FanFlat, ShapeRot = FanTip, FrameFrom = 0f, FrameTo = 6f,
                Colour = G((0f, C(Color.Lerp(e.Wash, e.Dark, .6f), 0f)), (.05f, C(Color.Lerp(e.Wash, e.Dark, .6f), .9f)), (.4f, C(Color.Lerp(e.Wash, e.Dark, .7f), .7f)), (1f, C(e.Dark, 0f))),
                Grow = Curve(0f, .6f, .4f, 1f, 1f, 1.3f) });   // pq_smoke: 2.6 - 3.6 m swelling to ~5 m read as broad see-through fans - the wisps the user pointed at were ~2 m
            return root;
        }

        // the volley ribbon: two crossed quads behind the shot (head at the origin, tail down -Z), mapped to one cel of the
        // speed-line sheet. A mesh, because a shot's body is switched off outside its flight and cannot run particles.
        static Mesh RibbonMesh()
        {
            Directory.CreateDirectory(Root2 + "/Meshes");
            string path = Root2 + "/Meshes/V308b_ribbon.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = "V308b_ribbon" }; AssetDatabase.CreateAsset(mesh, path); }
            float u0 = .01f, u1 = .24f, v0 = .875f - .045f, v1 = .875f + .045f;       // cel 0 (top-left), only the band the stroke lies in; its head is at the right of the cel
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            foreach (var side in new[] { Vector3.right, Vector3.up })
            {
                int n = v.Count;
                v.Add(side * -.5f); v.Add(side * .5f); v.Add(side * .5f - Vector3.forward); v.Add(side * -.5f - Vector3.forward);
                uv.Add(new Vector2(u1, v0)); uv.Add(new Vector2(u1, v1)); uv.Add(new Vector2(u0, v1)); uv.Add(new Vector2(u0, v0));
                tri.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
            }
            mesh.Clear(); mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh); Touched.Add(mesh);
            return mesh;
        }

        // the volley needle (D308-109): an ink spindle along -Z, head at the origin, thickest just behind the head and tapering
        // to a point at the tail. UVs as InkSolid reads them: u = angle round the flight axis, v = 0 at the head .. 1 at the
        // tail (with _TipInk the tail goes to plain ink, the head keeps a little pigment - ink first, the element soaks in).
        static Mesh NeedleMesh()
        {
            Directory.CreateDirectory(Root2 + "/Meshes");
            string path = Root2 + "/Meshes/V308b_needle.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = "V308b_needle" }; AssetDatabase.CreateAsset(mesh, path); }
            const int around = 8, along = 12; const float length = NeedleLength308, radius = .035f;
            float R(float t) => radius * (t < .12f ? Mathf.Lerp(.6f, 1f, t / .12f) : Mathf.Pow(Mathf.Max(0f, 1f - (t - .12f) / .88f), 1.3f));
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int j = 0; j <= along; j++)
            {
                float t = j / (float)along, r = R(t), slope = (R(Mathf.Min(1f, t + .01f)) - R(Mathf.Max(0f, t - .01f))) / (.02f * length);   // dr / d(distance toward the tail)
                for (int i = 0; i <= around; i++)
                {
                    float ang = i / (float)around * Mathf.PI * 2f; var radial = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
                    v.Add(radial * r + Vector3.back * (t * length));
                    n.Add((radial + Vector3.forward * slope).normalized);
                    uv.Add(new Vector2(i / (float)around, t));
                }
            }
            for (int j = 0; j < along; j++)
                for (int i = 0; i < around; i++)
                {
                    int a0 = j * (around + 1) + i, b0 = a0 + around + 1;
                    tri.AddRange(new[] { a0, b0, a0 + 1, a0 + 1, b0, b0 + 1 });
                }
            mesh.Clear(); mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0); mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh); Touched.Add(mesh);
            return mesh;
        }

        // D308-109 (user 2026-10-17 "먹 바늘 + 끝만 금속", "짧게, 공중에서만"): lengths are absolute; the letter's size only thickens
        const float NeedleLength308 = 1.1f, NeedleTail308 = 1.25f, NeedleTipLength308 = .2f;

        // Volley (D308-109, replaces the 4th pass's pale element solid + 5.2 m speed stroke that read as a road mark): per shot
        // a dark INK needle with a small element solid only at its head, and a short (1.25 m) bleeding tail behind it. The
        // body is switched off outside [launch, hit), so the tail is only ever in the air. Where a shot lands = the hit prefab.
        static GameObject VolleyBody(Elem e, Atk a, Mesh ribbon, Mesh needle)
        {
            var root = new GameObject("PF_V308b_volley_" + e.Key);
            var body = new GameObject("Needle"); body.transform.SetParent(root.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = needle;
            var mr = body.AddComponent<MeshRenderer>(); mr.sharedMaterial = Solid("M_V308_" + e.Key + "_needle_strand", Color.Lerp(e.Pig, e.Ink, .8f), e.Ink, .45f, .3f); mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            body.transform.localScale = new Vector3(a.Big, a.Big, 1f);
            // the element only at the head: the element's solid, scaled to a 0.2 m point whose back sits just inside the needle
            var tip = new GameObject("Tip"); tip.transform.SetParent(root.transform, false);
            tip.AddComponent<MeshFilter>().sharedMesh = e.Spike;
            var tr = tip.AddComponent<MeshRenderer>(); tr.sharedMaterial = e.Solid; tr.shadowCastingMode = ShadowCastingMode.Off; tr.receiveShadows = false;
            bool back = Backward(e.Spike); var b = e.Spike.bounds; float k = NeedleTipLength308 / Mathf.Max(.01f, b.size.z);
            tip.transform.localRotation = back ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            tip.transform.localScale = new Vector3(k * a.Big, k * a.Big, k);
            var centre = new Vector3(b.center.x * k * a.Big, b.center.y * k * a.Big, 0f); if (back) centre.x = -centre.x;
            tip.transform.localPosition = new Vector3(-centre.x, -centre.y, -.04f + (back ? b.max.z * k : -b.min.z * k));
            int lines = a.Dense > 1.15f ? 2 : 1;
            for (int i = 0; i < lines; i++)
            {
                var line = new GameObject("Line" + i); line.transform.SetParent(root.transform, false);
                line.AddComponent<MeshFilter>().sharedMesh = ribbon;
                var lr = line.AddComponent<MeshRenderer>(); lr.sharedMaterial = e.Ribbon; lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false;
                line.transform.localScale = new Vector3(.6f * a.Big, .6f * a.Big, NeedleTail308); line.transform.localRotation = Quaternion.Euler(0f, 0f, 45f * i);
                line.transform.localPosition = new Vector3(0f, 0f, -.1f);
            }
            return root;
        }

        // Path (4th pass): the element's HEAD stands up along the front (earth: a tall jagged heave; water: a low smooth crest
        // with its dark rim; wood: a row of brush-stroke stalks), born as ink and soaking to the element (D308-109), and solid
        // bits are thrown up. The seam scales the body across to the path's width, so the emitters use Shape scaling.
        static GameObject PathBody(Elem e, Atk a)
        {
            var root = new GameObject("PF_V308b_path_" + e.Key);
            var box = new Vector3(1f, .1f, 1.5f);      // 5th pass: the heads stand in a band 1.5 m deep, not on one line - they slide against each other as the camera moves
            Layer(root.transform, new L { Name = "Head", Mat = e.Head, World = false, Mode = 2, Rate = e.HeadRate * a.Dense, Max = Mathf.CeilToInt(e.HeadRate * a.Dense * e.HeadLife.y * a.Slow) + 3, Life = e.HeadLife * a.Slow, Size = e.HeadSize * a.Big, SpinRandom = false, Blend = true, ShapeScaling = true,
                Shape = ParticleSystemShapeType.Box, ShapeScale = box, FrameFrom = 0f, FrameTo = 15.5f, Pivot = new Vector3(0f, e.HeadPivot, 0f), Colour = InkFirst(e.Ink, e.Key == "ma" ? Color.Lerp(e.Wash, e.Dark, .55f) : e.Wash, e.Dark), Grow = Curve(0f, 1f, 1f, 1.12f) });   // pq_area: the earth head read as a muddy brown cloud - darker toward the ink
            // D308-109 audit (D308-105 front ring only, no stain): the wet wash left behind the front is gone; the head rides with the front (local sim).
            // user 2026-10-15 (no flying bits): the beads / clumps thrown up off the front are gone - the head carries it
            return root;
        }

        // where a range letter HITS an enemy: the per-hit contact seam (Vfx120Profile.AreaContactPrefab, spawned by the game at
        // each confirmed hit and played on its own clock). Contract: a root with a non-looping ParticleSystem, particles only.
        // A short flung-ink wash and a few blots. It only plays for letters whose profile is registered in KTP_ContactProfile
        // (connect:attack does that, revert undoes it); a shot that hits nothing has no landing mark through any seam.
        static GameObject HitBody(Elem e)
        {
            var ps = Layer(null, new L { Name = "PF_V308b_hit_" + e.Key, Mat = B(e.Key, "ink_splat", C(e.Ink, .9f), 0f, .9f), World = false, Burst = 1, Max = 2, Life = new Vector2(.5f, .5f), Size = new Vector2(2.6f, 2.6f), NoShape = true, Blend = true,
                FrameFrom = 0f, FrameTo = 10f, Colour = Fade(e.Dark, .35f, e.Dark), Grow = Curve(0f, .9f, 1f, 1.25f) });
            // user 2026-10-15 questionnaire "술식 쪽만 빼기": the drops falling off the flight / thrown off the hit are gone (the swing's own ink stays)
            return ps.gameObject;
        }

        static Vfx120AreaRemake Kind(Form f) => f == Form.Circle ? Vfx120AreaRemake.BambooField : f == Form.Cone ? Vfx120AreaRemake.FlameCone : f == Form.Volley ? Vfx120AreaRemake.NeedleVolley : Vfx120AreaRemake.SandFront;

        static string BuildAttack()
        {
            var kit = new Kit { Thorn = Model("thorn"), Splinter = Model("splinter"), Clod = Model("clod"), Chunk = Model("chunk"), Blade = Model("blade"), Stalk = Model("stalk"), Drop = Model("drop"), Tongue = Model("tongue") };
            Touched.Clear(); seed = 30900;
            var elems = new Dictionary<string, Elem>(); var bodies = new Dictionary<string, GameObject>(); var hits = new Dictionary<string, GameObject>(); var finals = new Dictionary<string, GameObject>(); var stains = new Dictionary<string, GameObject>(); Mesh ribbon = null, needle = null;
            var sb = new StringBuilder(); int built = 0, skipped = 0;
            foreach (var a in Attack)
            {
                if (a.Shape == Form.Giyeok) { skipped++; sb.AppendLine(a.Glyph + " giyeok route: not built"); continue; }
                if (Spells.Any(x => x.Glyph == a.Glyph)) continue;                       // the five prototypes are built by Build()
                if (!elems.TryGetValue(a.Elem, out var e)) elems[a.Elem] = e = Element(kit, a.Elem);
                string src = "Assets/_Project/Art/SpellVFX120/Profiles/" + a.Effect + ".asset";
                if (!File.Exists(AttackProfilePath(a)) && !AssetDatabase.CopyAsset(src, AttackProfilePath(a))) throw new Exception("copy failed " + src);
                var profile = Load<Vfx120Profile>(AttackProfilePath(a));
                if (a.Shape == Form.Single)
                {
                    var spell = Spells.First(x => x.Key == a.Elem);
                    profile.Bolt300Prefab = V2 ? FinalBolt(e, spell, FinalOf(a.Glyph), finals) : Load<GameObject>(PrefabPath(spell)); profile.Bolt300Arc = spell.Arc; profile.Bolt300Curve = spell.Curve; profile.Bolt300Spin = spell.Spin;   // 11.23: the final's marker
                    profile.NativeCastPrefab = null;   // 11.26 (D308-87): the old KTP cast (glowing zigzag + sparks at the launch point) is retired
                    profile.Bolt300Scale = a.Scale; profile.Bolt300Ease = 1f; profile.Bolt300SoakSeconds = V2 ? BodySoakSeconds : 0f; profile.Bolt300NearContactMeters = V2 ? NearContactMeters : 0f; profile.Bolt300TravelScale = V2 ? TravelScale : 0f;
                    profile.AreaRemake = Vfx120AreaRemake.None;
                    // the old bespoke bodies are keyed on the letter + the name of its body mesh / native body: give the copy
                    // other ones so they are not built beside the new body (BodyMesh must stay non-null)
                    profile.BodyMesh = e.Bit; profile.NativeBodyPrefab = null;
                }
                else
                {
                    string key = AreaBodyPath(a);
                    if (!bodies.TryGetValue(key, out var body))
                    {
                        var root = a.Shape == Form.Circle ? CircleBody(e, a) : a.Shape == Form.Cone ? ConeBody(e, a) : a.Shape == Form.Volley ? VolleyBody(e, a, ribbon ?? (ribbon = RibbonMesh()), needle ?? (needle = NeedleMesh())) : PathBody(e, a);
                        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = AfterFogLayer;
                        bodies[key] = body = PrefabUtility.SaveAsPrefabAsset(root, key);
                        Object.DestroyImmediate(root);
                    }
                    var hit = FinalHit(e, FinalOf(a.Glyph), hits);   // 11.23: the final's marker where a range letter hits
                    profile.AreaRemake = Kind(a.Shape); profile.NativeBodyPrefab = body; profile.AreaKeepShape308 = a.Shape == Form.Cone;   /* 11.29: the fan seam keeps this body's shape and sizes */ profile.AreaBambooLean = a.Shape == Form.Circle ? 18f : 0f; profile.AreaBambooHeightSpread = a.Shape == Form.Circle ? .4f : 0f; profile.Bolt300Prefab = null;   // D308-84 7th pass: not a fence
                    // the plain variants of the seam only: no older revision look, no rift lines, no cast seal
                    profile.WideAreaRevision = profile.AreaFlowRevision = profile.AreaReadabilityRevision = profile.EarthRift = profile.BranchedEarthRift = profile.ProceduralEarthRift = false;
                    profile.NativeCastPrefab = null;
                    // the per-hit contact seam: the game scales the contact by KtpContactProfile.ParryScale (.22) x this
                    profile.AreaStainPrefab = V2 && FinalOf(a.Glyph) == FinalK ? StainBody(e, stains) : null;   // 11.37 (e): one stain per struck enemy in place of the per-hit pool
                    profile.AreaContactPrefab = hit; profile.KtpEmphasis = true; profile.KtpContactMultiplier = 1f / .22f; profile.KtpContactBrightness = 1f;   // ink, not light (census: 3.5 copied from the old seal)
                }
                EditorUtility.SetDirty(profile); Touched.Add(profile);
                string srcEffect = "Assets/_Project/Art/SpellVFX120/Prefabs/" + a.Effect + ".prefab";
                if (!File.Exists(AttackEffectPath(a)) && !AssetDatabase.CopyAsset(srcEffect, AttackEffectPath(a))) throw new Exception("copy failed " + srcEffect);
                var contents = PrefabUtility.LoadPrefabContents(AttackEffectPath(a));
                try
                {
                    var fx = contents.GetComponentInChildren<Vfx120Effect>(true) ?? throw new Exception("no Vfx120Effect in " + AttackEffectPath(a));
                    fx.Profile = profile;
                    PrefabUtility.SaveAsPrefabAsset(contents, AttackEffectPath(a));
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                built++;
            }
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            sb.AppendLine("attack: " + built + " letters built on " + bodies.Count + " area bodies + the five element bodies, " + skipped + " giyeok letters skipped");
            return sb.ToString();
        }

        static string AttackTable()
        {
            var sb = new StringBuilder();
            foreach (var a in Attack)
                sb.AppendLine(a.Glyph + "\t" + a.Effect + "\t" + a.Elem + "\t" + a.Shape + "\t" + (a.Shape == Form.Giyeok ? "-" : a.Shape == Form.Single ? "PF_V308b_" + a.Elem + " x" + a.Scale.ToString("0.00", CultureInfo.InvariantCulture) : Path.GetFileNameWithoutExtension(AreaBodyPath(a)))
                    + "\t" + (a.Shape != Form.Giyeok && File.Exists(Spells.Any(x => x.Glyph == a.Glyph) ? EffectPath(Spells.First(x => x.Glyph == a.Glyph)) : AttackEffectPath(a)) ? "built" : "-"));
            return sb.ToString();
        }

        static float Ground(Vector3 p) => Physics.Raycast(p + Vector3.up * 6f, Vector3.down, out var hit, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.point.y : p.y;

        // Edit-mode pose of an area letter: the plain branches of Vfx120Effect.AreaRemake (BuildAreaRemake / SampleAreaRemake)
        // replayed on unsaved copies with a plan made from the rule numbers. A pose for stills, not the game.
        //   pose:<letter>:<when>[:ox,oy,oz:fx,fy,fz]   when = seconds after the cast | launch | hit | hit+<s>;  o = caster, f = a point ahead
        static string PoseArea(Atk a, string[] arg)
        {
            Vector3 caster = arg.Length > 3 && arg[3].Contains(",") ? Vec(arg[3]) : new Vector3(3343.3f, 99.95f, 2691.75f);
            Vector3 ahead = arg.Length > 4 && arg[4].Contains(",") ? Vec(arg[4]) : new Vector3(3332f, 100.9f, 2692f);
            Vector3 forward = ahead - caster; forward.y = 0; forward.Normalize(); Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 feet = caster; feet.y = Ground(caster);
            float peak = a.Shape == Form.Circle ? a.Delay + .45f : a.Shape == Form.Cone ? a.Delay + .22f : a.Shape == Form.Volley ? a.Delay + a.Shots * a.Interval * .5f + .25f : a.Delay + a.Length / a.Speed * .6f;
            float end = a.Shape == Form.Circle ? a.Delay + 1.3f : a.Shape == Form.Cone ? a.Delay + .9f : a.Shape == Form.Volley ? a.Delay + (a.Shots - 1) * a.Interval + a.Length / a.Speed : a.Delay + a.Length / a.Speed + .5f;
            string when = arg[2]; float t = when == "fade" ? end - .2f : when == "launch" ? a.Delay + .1f : when.StartsWith("hit", StringComparison.Ordinal) ? peak + (when.Length > 3 ? float.Parse(when.Substring(3), CultureInfo.InvariantCulture) : 0f) : float.Parse(when, CultureInfo.InvariantCulture);
            var kind = Kind(a.Shape);
            var prefab = Load<GameObject>(AreaBodyPath(a));
            var host = new GameObject(PoseName) { hideFlags = HideFlags.HideAndDontSave };
            int count = kind == Vfx120AreaRemake.BambooField ? 17 : kind == Vfx120AreaRemake.NeedleVolley ? a.Shots : 1;
            Vector3 point = feet;                                                                    // cone origin | path start
            if (kind == Vfx120AreaRemake.BambooField) { point = ahead; point.y = Ground(ahead); }    // circle centre = the aim point
            var bodies = new Transform[count]; var feetAt = new Vector3[count]; var riseAt = new float[count];
            for (int i = 0; i < count; i++)
            {
                var body = Object.Instantiate(prefab, host.transform, false); bodies[i] = body.transform;
                if (kind == Vfx120AreaRemake.BambooField)
                {
                    float ang = i * 2.39996f, r = a.Radius * Mathf.Sqrt((i + .5f) / 17f);
                    var q = point + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * r; q.y = Ground(q); feetAt[i] = q; riseAt[i] = a.Delay + .6f * i / 16f;
                    body.transform.localScale = new Vector3(.8f + i % 3 * .11f, .85f + i % 5 * .1f, .8f + i % 3 * .11f); body.transform.rotation = Quaternion.Euler(i % 3 * 3, i * 71, i % 2 * 4 - 2);
                }
            }
            foreach (var x in host.GetComponentsInChildren<Transform>(true)) x.gameObject.hideFlags = HideFlags.HideAndDontSave;
            var systems = host.GetComponentsInChildren<ParticleSystem>();
            foreach (var ps in systems) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Simulate(0, false, true, false); }
            Vector3 nozzle = caster;
            if (kind == Vfx120AreaRemake.FlameCone)
            {
                bodies[0].SetPositionAndRotation(point, Quaternion.LookRotation(forward));
                foreach (var ps in systems) { var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.ConeVolume; shape.angle = a.Angle; shape.radius = .08f; shape.length = Mathf.Max(1, a.Length - 1.6f); shape.position = bodies[0].InverseTransformPoint(nozzle); }
            }
            int shown = 0;
            void Place(float now)
            {
                if (kind == Vfx120AreaRemake.BambooField)
                    for (int i = 0; i < 17; i++)
                    {
                        float rise = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(riseAt[i] - .12f, riseAt[i], now));
                        bodies[i].position = feetAt[i] - Vector3.up * ((1 - rise) * 2.1f);
                        foreach (var r in bodies[i].GetComponentsInChildren<MeshRenderer>(true)) r.enabled = rise > 0;
                    }
                else if (kind == Vfx120AreaRemake.NeedleVolley)
                {
                    shown = 0;
                    for (int i = 0; i < count; i++)
                    {
                        float launch = a.Delay + i * a.Interval, hit = launch + a.Length / Mathf.Max(1, a.Speed);
                        Vector3 end = nozzle + forward * a.Length + right * Mathf.Sin(i * 2.4f) * a.Length * .12f;
                        bool visible = now >= launch && now < hit; bodies[i].gameObject.SetActive(visible); if (!visible) continue; shown++;
                        bodies[i].position = Vector3.Lerp(nozzle, end, Mathf.Clamp01((now - launch) / Mathf.Max(.001f, hit - launch))); bodies[i].rotation = Quaternion.LookRotation(end - nozzle);
                    }
                }
                else if (kind == Vfx120AreaRemake.SandFront)
                {
                    float along = Mathf.Clamp((now - a.Delay) * a.Speed, 0, a.Length);
                    var front = point + forward * along; front.y = Ground(front) + .35f;
                    bodies[0].SetPositionAndRotation(front, Quaternion.LookRotation(forward)); bodies[0].localScale = new Vector3(a.Radius * 2, 1, 1);
                }
            }
            const float dt = 1f / 120; float sim = 0f;
            while (sim + dt <= t + .00001f)
            {
                sim += dt; Place(sim);
                bool emit = kind == Vfx120AreaRemake.FlameCone ? sim >= Mathf.Max(0, a.Delay - .12f) && sim < a.Delay + .18f : sim >= a.Delay && sim < a.Delay + a.Length / Mathf.Max(.1f, a.Speed);
                foreach (var ps in systems) { if (!ps.gameObject.activeInHierarchy) continue; var em = ps.emission; em.enabled = emit; ps.Simulate(dt, false, false, false); }
            }
            Place(t);
            SceneView.RepaintAll();
            return a.Glyph + " " + a.Shape + " posed at " + t.ToString("0.00", CultureInfo.InvariantCulture) + " s (delay " + a.Delay.ToString("0.00", CultureInfo.InvariantCulture) + ") bodies " + count
                + (kind == Vfx120AreaRemake.NeedleVolley ? " shown " + shown : "") + " particles " + systems.Sum(x => x.particleCount);
        }

        // ---------------------------------------------------------------- build

        static bool V2 = true;
        const string Root2 = Root + "/V2";
        static string PrefabPath(Spell s) => V2 ? Root2 + "/Prefabs/PF_V308b_" + s.Key + ".prefab" : Root + "/Prefabs/PF_V308_" + s.Key + ".prefab";
        static string EffectPath(Spell s) => V2 ? Root2 + "/Effects/" + s.Effect + "_308b.prefab" : Root + "/Effects/" + s.Effect + "_308.prefab";
        static string ProfilePath(Spell s) => V2 ? Root2 + "/Profiles/" + s.Effect + "_308b.asset" : Root + "/Profiles/" + s.Effect + "_308.asset";

        static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build: Edit mode only");
            foreach (var d in new[] { "/Prefabs", "/Profiles", "/Effects", "/Materials" }) Directory.CreateDirectory(Root + d);
            AssetDatabase.Refresh();
            Touched.Clear(); seed = 30800;
            // every import setting first, then load: a reimport later in the run would leave earlier mesh references dead
            foreach (var d in new[] { "/Prefabs", "/Profiles", "/Effects", "/Materials" }) Directory.CreateDirectory(Root2 + d);
            foreach (var s in V2 ? Sheets2 : Sheets) { if (V2) Sheet2(s); else Sheet(s); }
            foreach (var m in MeshNames) Model(m);
            var kit = new Kit { Thorn = Model("thorn"), Splinter = Model("splinter"), Clod = Model("clod"), Chunk = Model("chunk"), Blade = Model("blade"), Stalk = Model("stalk"), Drop = Model("drop"), Tongue = Model("tongue") };
            var sb = new StringBuilder();
            foreach (var s in Spells)
            {
                string srcProfile = "Assets/_Project/Art/SpellVFX120/Profiles/" + s.Effect + ".asset";
                if (!File.Exists(ProfilePath(s)) && !AssetDatabase.CopyAsset(srcProfile, ProfilePath(s))) throw new Exception("copy failed " + srcProfile);
                var profile = Load<Vfx120Profile>(ProfilePath(s));
                var original = Load<Vfx120Profile>(srcProfile);
                kit.Ink = original.Ink;
                var root = V2
                    ? s.Key switch { "ga" => Ga2(kit, original), "na" => Na2(kit, original), "ma" => Ma2(kit, original), "sa" => Sa2(kit, original), _ => A2(kit, original) }
                    : s.Key switch { "ga" => Ga(kit, original), "na" => Na(kit, original), "ma" => Ma(kit, original), "sa" => Sa(kit, original), _ => A(kit, original) };
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = AfterFogLayer;
                if (V2) Bolt308Extras(root);
                if (V2) InkTrail(root.transform.Find("Travel"), s.Key, original.Ink, Color.Lerp(original.Pigment, original.Ink, .75f));   // 11.23: the line of the flight
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(s));
                Object.DestroyImmediate(root);
                profile.Bolt300Prefab = prefab; profile.Bolt300Arc = s.Arc; profile.Bolt300Curve = s.Curve; profile.Bolt300Spin = s.Spin;
                profile.NativeCastPrefab = null;   // 11.26 (D308-87): no old KTP cast beside the ink body
                profile.Bolt300Scale = s.Scale; profile.Bolt300Ease = 1f; profile.Bolt300SoakSeconds = V2 ? BodySoakSeconds : 0f; profile.Bolt300NearContactMeters = V2 ? NearContactMeters : 0f; profile.Bolt300TravelScale = V2 ? TravelScale : 0f;
                EditorUtility.SetDirty(profile); Touched.Add(profile);
                string srcEffect = "Assets/_Project/Art/SpellVFX120/Prefabs/" + s.Effect + ".prefab";
                if (!File.Exists(EffectPath(s)) && !AssetDatabase.CopyAsset(srcEffect, EffectPath(s))) throw new Exception("copy failed " + srcEffect);
                var contents = PrefabUtility.LoadPrefabContents(EffectPath(s));
                try
                {
                    var fx = contents.GetComponentInChildren<Vfx120Effect>(true) ?? throw new Exception("no Vfx120Effect in " + EffectPath(s));
                    fx.Profile = profile;
                    PrefabUtility.SaveAsPrefabAsset(contents, EffectPath(s));
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                sb.AppendLine(s.Glyph + " " + PrefabPath(s) + " -> " + EffectPath(s) + " (profile " + ProfilePath(s) + ")");
            }
            // only this tool's own assets are written (the editor is shared: no AssetDatabase.SaveAssets)
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            return sb.ToString();
        }

        // ---------------------------------------------------------------- connect / revert / status

        [Serializable] sealed class Entry { public string glyph, before, after; }
        [Serializable] sealed class Recorded { public List<Entry> entries = new List<Entry>(); }

        static string Connect(bool attack)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build: Edit mode only");
            var set = Load<SpellVisualSetSO>(VisualSet);
            var so = new SerializedObject(set);
            var entries = so.FindProperty("_entries");
            var rec = File.Exists(Record) ? JsonUtility.FromJson<Recorded>(File.ReadAllText(Record)) : new Recorded();
            var sb = new StringBuilder();
            var targets = Spells.Select(x => (glyph: x.Glyph, path: EffectPath(x))).ToList();
            if (attack) targets.AddRange(Attack.Where(x => x.Shape != Form.Giyeok && Spells.All(b => b.Glyph != x.Glyph)).Select(x => (glyph: x.Glyph, path: AttackEffectPath(x))));
            if (attack) targets.AddRange(Guards.Where(g => File.Exists(GuardEffectPath(g.Id))).Select(g => (glyph: g.Glyph, path: GuardEffectPath(g.Id))));   // 11.22: once built
            if (attack) targets.AddRange(Cues.Where(g => File.Exists(CueEffectPath(g.Id))).Select(g => (glyph: g.Glyph, path: CueEffectPath(g.Id))));         // 11.40: once built
            foreach (var s in targets)
            {
                var effect = Load<GameObject>(s.path);
                for (int i = 0; i < entries.arraySize; i++)
                {
                    var e = entries.GetArrayElementAtIndex(i);
                    if (e.FindPropertyRelative("Letter").stringValue != s.glyph) continue;
                    var fx = e.FindPropertyRelative("FxPrefab");
                    string current = AssetDatabase.GetAssetPath(fx.objectReferenceValue);
                    if (current != AssetDatabase.GetAssetPath(effect) && rec.entries.All(x => x.glyph != s.glyph))
                        rec.entries.Add(new Entry { glyph = s.glyph, before = current, after = AssetDatabase.GetAssetPath(effect) });
                    fx.objectReferenceValue = effect;
                    sb.AppendLine(s.glyph + ": " + current + " -> " + AssetDatabase.GetAssetPath(effect));
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(set); AssetDatabase.SaveAssetIfDirty(set);
            Directory.CreateDirectory(Path.GetDirectoryName(Record));
            File.WriteAllText(Record, JsonUtility.ToJson(rec, true));
            if (attack) sb.AppendLine(RegisterContacts());
            if (attack) sb.AppendLine(ConnectGiyeok());
            if (attack) sb.AppendLine(ConnectAura());
            if (attack) sb.AppendLine(ConnectBeast());
            if (attack) sb.AppendLine(ConnectSummonFx());
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
            EditorUtility.SetDirty(set); AssetDatabase.SaveAssetIfDirty(set);
            File.Delete(Record);
            sb.AppendLine(UnregisterContacts());
            sb.AppendLine(RevertGiyeok());
            sb.AppendLine(RevertAura());
            sb.AppendLine(RevertBeast());
            sb.AppendLine(RevertSummonFx());
            return sb.ToString();
        }

        // The per-hit contact of a range letter is looked up in KTP_ContactProfile.SpellProfiles by letter (first match with
        // KtpEmphasis). connect:attack puts the #308 profile copies of the 25 range letters in front; the list as it was is
        // recorded beside the visual-set record and put back by revert.
        const string KtpProfile = "Assets/_Project/Art/SpellVFX120/Resources/KTP_ContactProfile.asset";
        static string KtpRecord => Path.Combine(Path.GetDirectoryName(Record), "ktp-contact-before.json");
        [Serializable] sealed class Paths { public List<string> paths = new List<string>(); }

        static string RegisterContacts()
        {
            var ktp = Load<KtpContactProfile>(KtpProfile);
            if (!File.Exists(KtpRecord)) File.WriteAllText(KtpRecord, JsonUtility.ToJson(new Paths { paths = ktp.SpellProfiles.Select(x => AssetDatabase.GetAssetPath(x)).ToList() }, true));
            var mine = Attack.Where(x => x.Shape != Form.Giyeok && x.Shape != Form.Single).Select(x => Load<Vfx120Profile>(AttackProfilePath(x))).ToList();
            // D308-84 (11.21, plan stage 3 "hit = ink splat"): singles too - Play sw_p still showed the KTP pattern seal (Fly prefab,
            // x3.5) on every single-target hit. Their #308 profile copies get the element's ink-splat hit as the contact.
            int singles = 0;
            foreach (var a in Attack.Where(x => x.Shape == Form.Single))
            {
                var spell = Spells.FirstOrDefault(x => x.Glyph == a.Glyph);
                var profile = Load<Vfx120Profile>(spell != null ? ProfilePath(spell) : AttackProfilePath(a));
                var hit = AssetDatabase.LoadAssetAtPath<GameObject>(HitPath(a.Elem));
                if (profile == null || hit == null) continue;
                profile.AreaContactPrefab = hit; profile.KtpEmphasis = true; profile.KtpContactMultiplier = SingleHitScale / .22f; profile.KtpContactBrightness = 1f;
                EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
                mine.Add(profile); singles++;
            }
            foreach (var g in Guards) { var gp = File.Exists(GuardProfilePath(g.Id)) ? Load<Vfx120Profile>(GuardProfilePath(g.Id)) : null; if (gp != null) mine.Add(gp); }   // 11.22: the guard contact
            ktp.SpellProfiles = mine.Concat(ktp.SpellProfiles.Where(x => x != null && !mine.Contains(x))).ToArray();
            EditorUtility.SetDirty(ktp); AssetDatabase.SaveAssetIfDirty(ktp);
            return "hit contacts: " + (mine.Count - singles) + " range + " + singles + " single profiles put first in KTP_ContactProfile.SpellProfiles";
        }

        static string UnregisterContacts()
        {
            if (!File.Exists(KtpRecord)) return "hit contacts: nothing recorded";
            var ktp = Load<KtpContactProfile>(KtpProfile);
            ktp.SpellProfiles = JsonUtility.FromJson<Paths>(File.ReadAllText(KtpRecord)).paths.Select(x => AssetDatabase.LoadAssetAtPath<Vfx120Profile>(x)).ToArray();
            EditorUtility.SetDirty(ktp); AssetDatabase.SaveAssetIfDirty(ktp);
            File.Delete(KtpRecord);
            return "hit contacts: KTP_ContactProfile.SpellProfiles restored";
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
                        sb.AppendLine(s.Glyph + ": " + AssetDatabase.GetAssetPath(e.FindPropertyRelative("FxPrefab").objectReferenceValue)
                            + (File.Exists(PrefabPath(s)) ? "  [#308 built]" : "  [#308 not built]"));
                }
            foreach (var g in Cues)   // 11.40
                for (int i = 0; i < entries.arraySize; i++)
                {
                    var e = entries.GetArrayElementAtIndex(i);
                    if (e.FindPropertyRelative("Letter").stringValue == g.Glyph)
                        sb.AppendLine(g.Glyph + " " + g.Kind + ": " + AssetDatabase.GetAssetPath(e.FindPropertyRelative("FxPrefab").objectReferenceValue)
                            + (File.Exists(CueEffectPath(g.Id)) ? "  [#308 built]" : "  [#308 not built]"));
                }
            sb.AppendLine("record: " + (File.Exists(Record) ? Record : "none"));
            return sb.ToString();
        }

        static string Budget()
        {
            var sb = new StringBuilder();
            long texBytes = 0;
            foreach (var s in V2 ? Sheets2 : Sheets)
            {
                var t = Load<Texture2D>(V2 ? Root2 + "/Textures/T_V308b_" + s + ".png" : Root + "/Textures/T_V308_" + s + ".png");
                long b = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t); texBytes += b;
                sb.AppendLine("sheet " + s + " " + t.width + "x" + t.height + " " + t.format + " " + (b / 1024) + " KB");
            }
            sb.AppendLine((V2 ? "v2" : "v1") + " sheets total " + (texBytes / 1024) + " KB");
            foreach (var m in MeshNames) { var mesh = AssetDatabase.LoadAllAssetsAtPath(Root + "/Meshes/V308_" + m + ".fbx").OfType<Mesh>().First(); sb.AppendLine("mesh " + m + " " + mesh.triangles.Length / 3 + " tris " + mesh.vertexCount + " verts"); }
            foreach (var s in Spells)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(s));
                if (prefab == null) { sb.AppendLine(s.Glyph + " not built"); continue; }
                foreach (string part in new[] { "Travel", "Contact" })
                {
                    var systems = prefab.transform.Find(part).GetComponentsInChildren<ParticleSystem>(true);
                    sb.AppendLine(s.Glyph + " " + part + ": " + systems.Length + " systems, max particles " + systems.Sum(x => x.main.maxParticles)
                        + ", materials " + systems.Select(x => x.GetComponent<ParticleSystemRenderer>().sharedMaterial).Distinct().Count());
                }
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- edit-mode pose (unsaved preview)

        static void ClearPose()
        {
            foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>().Where(x => x != null && x.name == PoseName && !EditorUtility.IsPersistent(x)).ToArray())
                Object.DestroyImmediate(g);
        }

        static Vector3 Vec(string s) { var v = s.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray(); return new Vector3(v[0], v[1], v[2]); }

        // The same stepping as Vfx120Effect.SampleBolt300 (fixed 60 Hz, emission off at the arrival, Contact placed at the
        // target looking back along the path), run in Edit mode on an unsaved copy. It is a pose for stills, not the game.
        static string Pose(string[] a)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build pose: Edit mode only");
            ClearPose();
            if (a.Length > 1 && a[1] == "clear") return "pose cleared";
            if (a.Length < 3) throw new ArgumentException("pose:<letter>:<when>[:ox,oy,oz:tx,ty,tz]");
            var s = Spells.FirstOrDefault(x => x.Glyph == a[1] || x.Key == a[1]);
            float bodyScale = 1f;
            if (s == null)
            {
                var atk = Attack.FirstOrDefault(x => x.Glyph == a[1] || x.Effect == a[1]) ?? throw new ArgumentException("unknown letter " + a[1]);
                if (atk.Shape == Form.Giyeok) return atk.Glyph + ": giyeok route - not drawn from the visual set, nothing built";
                if (atk.Shape != Form.Single) return PoseArea(atk, a);
                s = Spells.First(x => x.Key == atk.Elem); bodyScale = atk.Scale;
            }
            Vector3 origin = a.Length > 3 && a[3].Contains(",") ? Vec(a[3]) : new Vector3(3340.25f, 99.95f, 2688.7f);
            Vector3 target = a.Length > 4 && a[4].Contains(",") ? Vec(a[4]) : new Vector3(3340f, 100.5f, 2700f);
            float flight = Vector3.Distance(origin, target) / s.Speed;
            string when = a[2]; float at;
            if (when == "launch") at = .06f;
            else if (when == "mid") at = flight * Mathf.Clamp01(6f / Vector3.Distance(origin, target));
            else if (when.StartsWith("hit", StringComparison.Ordinal)) at = flight + (when.Length > 3 ? float.Parse(when.Substring(3), CultureInfo.InvariantCulture) : .04f);
            else at = float.Parse(when, CultureInfo.InvariantCulture);

            var prefab = Load<GameObject>(PrefabPath(s));
            var root = Object.Instantiate(prefab, origin, Quaternion.identity); root.name = PoseName;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            var travel = root.transform.Find("Travel"); var contact = root.transform.Find("Contact"); var spin = travel.Find("Spin");
            var travelSystems = travel.GetComponentsInChildren<ParticleSystem>(true); var contactSystems = contact.GetComponentsInChildren<ParticleSystem>(true);
            var bodies = spin != null ? spin.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            travel.localScale = contact.localScale = Vector3.one * s.Scale * bodyScale;
            Vector3 line = target - origin; line.y = 0; Vector3 side = Vector3.Cross(Vector3.up, line.normalized);
            Vector3 Point(float time)
            {
                float u = Mathf.Clamp01(time / Mathf.Max(.001f, flight));
                return Vector3.Lerp(origin, target, u) + Vector3.up * (s.Arc * 4f * u * (1f - u)) + side * (s.Curve * Mathf.Sin(Mathf.PI * u) * (1f - .35f * u));
            }
            Vector3 Tangent(float time)
            {
                Vector3 d = Point(Mathf.Min(time + .02f, flight)) - Point(Mathf.Max(0f, Mathf.Min(time, flight - .02f)));
                return d.sqrMagnitude > 1e-6f ? d.normalized : (target - origin).normalized;
            }
            travel.position = origin; travel.rotation = Quaternion.LookRotation(Tangent(0f), Vector3.up);
            foreach (var ps in travelSystems) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Simulate(0, false, true, false); }
            foreach (var ps in contactSystems) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Simulate(0, false, true, false); }
            const float step = 1f / 60f; float simulated = 0f; bool impacted = false;
            while (simulated + step <= at + .00001f)
            {
                float now = simulated + step; bool flying = now < flight;
                travel.position = Point(Mathf.Min(now, flight));
                travel.rotation = Quaternion.LookRotation(Tangent(Mathf.Min(now, flight)), Vector3.up);
                if (spin != null && Mathf.Abs(s.Spin) > .01f) spin.localRotation = Quaternion.Euler(now * s.Spin, now * s.Spin * .37f, 0f);
                foreach (var ps in travelSystems) { var em = ps.emission; em.enabled = flying; ps.Simulate(step, false, false, false); }
                if (now >= flight)
                {
                    if (!impacted)
                    {
                        impacted = true; contact.position = target;
                        contact.rotation = Quaternion.LookRotation(-Tangent(flight), Vector3.up);
                        foreach (var r in bodies) r.enabled = false;
                    }
                    foreach (var ps in contactSystems) ps.Simulate(Mathf.Min(step, now - flight), false, false, false);
                }
                simulated = now;
            }
            if (a.Contains("probe"))
            {
                // diagnosis only: every bleed sheet drawn as pure black ink (unsaved material copies). If the heart still reads
                // grey in a still, the paleness comes after the shader (post / fog), not from the colours or the sheet.
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.sharedMaterial == null || r.sharedMaterial.shader.name != "Oheangbu/SpellVFX308/InkBleed") continue;
                    var m = new Material(r.sharedMaterial) { hideFlags = HideFlags.HideAndDontSave };
                    m.SetColor("_Ink", new Color(0f, 0f, 0f, 1f)); m.SetFloat("_HeartLo", -1f); m.SetFloat("_HeartHi", -.5f); m.SetFloat("_Heat", 0f);
                    r.sharedMaterial = m;
                }
            }
            if (a.Contains("diag"))
            {
                var d = new StringBuilder();
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    d.AppendLine(r.name + " enabled=" + r.enabled + " active=" + r.gameObject.activeInHierarchy + " layer=" + r.gameObject.layer + " bounds=" + r.bounds.center.ToString("F1") + "/" + r.bounds.size.ToString("F2")
                        + " mat=" + (r.sharedMaterial != null ? r.sharedMaterial.name + ":" + r.sharedMaterial.shader.name + ":q" + r.sharedMaterial.renderQueue : "null") + " lossy=" + r.transform.lossyScale.ToString("F2"));
                var buf = new ParticleSystem.Particle[4];
                foreach (var ps in travelSystems.Concat(contactSystems)) { int n = ps.GetParticles(buf); if (n > 0) d.AppendLine(ps.name + " p0=" + buf[0].position.ToString("F1") + " size=" + buf[0].GetCurrentSize3D(ps).ToString("F2") + " col=" + buf[0].GetCurrentColor(ps) + " space=" + ps.main.simulationSpace); }
                return d.ToString();
            }
            SceneView.RepaintAll();
            return s.Glyph + " posed at " + at.ToString("0.000", CultureInfo.InvariantCulture) + " s (flight " + flight.ToString("0.000", CultureInfo.InvariantCulture)
                + " s, " + Vector3.Distance(origin, target).ToString("0.0", CultureInfo.InvariantCulture) + " m) body " + travel.position.ToString("F2")
                + " particles " + string.Join(" ", travelSystems.Concat(contactSystems).Select(x => x.name + "=" + x.particleCount));
        }
    }
}
