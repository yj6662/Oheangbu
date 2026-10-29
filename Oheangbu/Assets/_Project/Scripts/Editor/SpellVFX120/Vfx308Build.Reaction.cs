using System;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // D308-128 21 (rx_r_a2_wildfire, rx_r_b3_steam) / D308-132 step 6, the look of the five-element reactions. Built at a radius of
    // 1 (the presenter dresses them: SpellReactionDress308 - scale, direction, life, the run -> burst -> decay beat). Ink only: the
    // InkBleed shader, alpha-blended, no emission, no glow, nothing additive (ART-INK: pollution and fire here do not shine).
    //   wildfire (목생화, 들불) - the vines the material was are left as scorched strokes (fuel); tongues run along five branches of
    //     them and only on them (the tongue sheet of the ember patch: black body, red edge under #D65A43, heat #C75436), soot
    //     smoke and ash that is DARKER than the ground (#5C5449, never the #867C6B highlight); the old perimeter turns to ash
    //     strokes where it stood (world space: the look grows x1.5 around them in .4 s, Vfx308ReactGrow). <= 45 particles.
    //   steam (수극화, 김) - a cracked wet track (steel-blue ink #3F5866) along the way the water ran, and over it low cool
    //     smoke (B >= R: foot #4A5054 darker than the ground, tips #6E767A, never white) that drifts ALONG that way at .6 m/s,
    //     stretched along it, in two strips with the middle left empty (the enemy stays >= 70 % visible), <= .9 m high at any
    //     radius (the presenter scales it by (radius, 1, radius)), gone in 2 s. 12 smoke quads + 5 track pieces.
    // Mud (진창) is recognised and has no look yet (the Look asset lists it with no prefab).
    //   reaction - build both prefabs and the Resources look (Spell308/SpellReactionLook308); idempotent
    public static partial class Vfx308Build
    {
        const string ReactLookAsset = "Assets/_Project/Resources/Spell308/SpellReactionLook308.asset";
        internal static string ReactPath(string key) => Root2 + "/Prefabs/PF_V308z_react_" + key + ".prefab";

        static string BuildReaction()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build reaction: Edit mode only");
            V2 = true; Touched.Clear(); seed = 49100;
            foreach (var d in new[] { "/Prefabs", "/Materials" }) Directory.CreateDirectory(Root2 + d);
            foreach (var s in new[] { "ink_pool", "ink_splat", "wood_stroke", "soot_wisp", "ink_drops" }) Sheet2(s);
            var fire = WildfireBody(out float fireDry);
            var steam = SteamBody(out float steamDry);
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);

            if (!AssetDatabase.IsValidFolder("Assets/_Project/Resources")) AssetDatabase.CreateFolder("Assets/_Project", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Resources/Spell308")) AssetDatabase.CreateFolder("Assets/_Project/Resources", "Spell308");
            var look = AssetDatabase.LoadAssetAtPath<SpellReactionLook308SO>(ReactLookAsset);
            if (look == null) { look = ScriptableObject.CreateInstance<SpellReactionLook308SO>(); AssetDatabase.CreateAsset(look, ReactLookAsset); }
            look.Entries = new[]
            {
                new SpellReactionLook308SO.Entry { Kind = ReactionKind308.Wildfire, Prefab = fire, KeepHeight = false },
                new SpellReactionLook308SO.Entry { Kind = ReactionKind308.Steam, Prefab = steam, KeepHeight = true },
                new SpellReactionLook308SO.Entry { Kind = ReactionKind308.Mud, Prefab = null, KeepHeight = false },   // recognised, no look yet
            };
            EditorUtility.SetDirty(look);
            AssetDatabase.SaveAssetIfDirty(look);
            var sb = new StringBuilder();
            sb.AppendLine("wildfire -> " + AssetDatabase.GetAssetPath(fire) + " (" + Particles(fire) + " particles at most, dry " + fireDry.ToString("0.00") + " s)");
            sb.AppendLine("steam -> " + AssetDatabase.GetAssetPath(steam) + " (" + Particles(steam) + " particles at most, dry " + steamDry.ToString("0.00") + " s)");
            sb.AppendLine("look -> " + ReactLookAsset + " (wildfire, steam, mud without a prefab)");
            return sb.ToString();
        }

        static int Particles(GameObject prefab) => prefab.GetComponentsInChildren<ParticleSystem>(true).Sum(ps => ps.main.maxParticles);

        // ---------------------------------------------------------------- wildfire (rx_r_a2_wildfire)

        // five branches from the middle outward (degrees from +X, ground plane) and their lengths in the look's own radius (the look is
        // built so that 1 is the product's edge, the new perimeter; at its birth the whole is shrunk to the material's radius)
        static readonly float[] BranchDeg = { 18f, 92f, 158f, 230f, 304f };
        static readonly float[] BranchLen = { .95f, .8f, .9f, .85f, .75f };

        static GameObject WildfireBody(out float dryFade)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(ReactPath("wildfire")));
            try
            {
                var t = root.transform;
                const string m = "M_V308z_react_fire_";
                Color scorch = Hex("#2B2620"), ash = Hex("#5C5449"), soot = Hex("#3E362C");
                var pool = Bleed(m + "pool", "ink_pool", C(scorch, .85f), 0f, .85f, 0f, 0f);
                var stroke = Bleed(m + "char", "wood_stroke", C(scorch, .92f), 0f, .9f, 0f, 0f);
                // the ember patch's brush sheet tongues as they are (the brightest strokes already brought under the #D65A43 ceiling)
                var tongue = Paint(m + "tongue", "brush", 2, new Vector4(8f, 8f, 16f, 0f), new Color(.83f, .38f, .33f), .08f);
                var wisp = Bleed(m + "wisp", "soot_wisp", C(soot, .6f), 0f, .6f, 0f, .3f);
                var flakes = Bleed(m + "ash", "ink_drops", C(ash, .9f), 0f, .9f, 0f, .1f);
                var ring = Bleed(m + "ring", "wood_stroke", C(ash, .8f), 0f, .8f, 0f, 0f);
                var settle = Bleed(m + "settle", "ink_pool", C(ash, .8f), 0f, .8f, 0f, 0f);

                // ink first: one dark pour under the rest, then a faint scorched stain (both widen with the circle)
                Layer(t, new L { Name = "Pour", Mat = pool, World = false, Mode = 1, Burst = 1, Max = 1, Life = new Vector2(1.4f, 1.6f), Size = new Vector2(2f, 2.3f),
                    NoShape = true, ShapePos = new Vector3(0f, .02f, 0f), Blend = true, FrameFrom = 3f, FrameTo = 12f, Colour = InkFade(.5f), Grow = Curve(0f, .55f, .35f, 1f, 1f, 1.1f) });
                Layer(t, new L { Name = "Stain", Mat = pool, World = false, Mode = 1, Rate = .8f, Max = 2, Life = new Vector2(2.4f, 2.8f), Size = new Vector2(1.9f, 2.1f),
                    NoShape = true, ShapePos = new Vector3(0f, .015f, 0f), Blend = true, RandomStart = true, FrameFrom = 0f, FrameTo = 3f,
                    Colour = G((0f, C(scorch, 0f)), (.3f, C(scorch, .45f)), (.7f, C(scorch, .45f)), (1f, C(scorch, 0f))), Grow = Curve(0f, .95f, 1f, 1.03f) });

                for (int i = 0; i < BranchDeg.Length; i++)
                {
                    float rad = BranchDeg[i] * Mathf.Deg2Rad, len = BranchLen[i], half = len * .5f;
                    var along = new Vector3(Mathf.Cos(rad) * half, .03f, Mathf.Sin(rad) * half);
                    var yaw = new Vector3(0f, -BranchDeg[i], 0f);
                    // the vine left as fuel: dark strokes crawling along the branch, paling to ash as the fire takes them
                    BurnDown(Layer(t, new L { Name = "Fuel_" + i, Mat = stroke, World = false, Mode = 1, Max = 2, Life = new Vector2(2.4f, 3f), Size = new Vector2(.5f, .7f),
                        Shape = ParticleSystemShapeType.SingleSidedEdge, Radius = half, ShapePos = along, ShapeRot = yaw, Blend = true, RandomStart = true, FrameFrom = 0f, FrameTo = 8f, Spin = 6f,
                        Colour = G((0f, C(scorch, 0f)), (.15f, C(scorch, .8f)), (.75f, C(scorch, .65f)), (1f, C(ash, 0f))), Grow = Curve(0f, .6f, .3f, 1f, 1f, 1.05f) }),
                        1f, Vector2.zero, Vector2.zero);
                    // the tongues run along the same line and nowhere else: none on the bare ground, none past the new perimeter
                    BurnDown(Tongues(Layer(t, new L { Name = "Tongue_" + i, Mat = tongue, World = false, Mode = 2, Max = 4, Life = new Vector2(.7f, 1f), Size = new Vector2(.45f, .5f),
                        Shape = ParticleSystemShapeType.SingleSidedEdge, Radius = half, ShapePos = along, ShapeRot = yaw, ShapeScaling = true, Pivot = TonguePivot, SpinRandom = false,
                        Flipbook = false, Colour = TongueLife() }), 0f), 2.5f, new Vector2(.45f, .5f), new Vector2(.2f, .24f));
                }

                // soot smoke leaning up off the fire (bent, thick, slow, <= 1.3 m at the largest radius, never red) and ash flakes
                BurnDown(Layer(t, new L { Name = "Wisps", Mat = wisp, World = false, Mode = 2, Max = 3, Life = new Vector2(2.2f, 2.8f), Size = new Vector2(.4f, .55f),
                    Shape = ParticleSystemShapeType.Circle, Radius = .8f, Thickness = 1f, ShapeRot = new Vector3(-90f, 0f, 0f), ShapePos = new Vector3(0f, .06f, 0f), ShapeScaling = true,
                    SpinRandom = false, RotDeg = new Vector2(70f, 110f), RiseY = .18f, Noise = .12f, NoiseFreq = .3f, Blend = true, RandomStart = true, FrameTo = 6f,
                    Colour = G((0f, C(soot, 0f)), (.25f, C(soot, .3f)), (.7f, C(soot, .2f)), (1f, C(soot, 0f))), Grow = Curve(0f, .7f, 1f, 1.4f) }), 1.2f, Vector2.zero, Vector2.zero);
                BurnDown(Layer(t, new L { Name = "Ash", Mat = flakes, World = true, Mode = 0, Max = 4, Life = new Vector2(2.5f, 3.2f), Size = new Vector2(.03f, .06f),
                    Shape = ParticleSystemShapeType.Circle, Radius = .8f, Thickness = 1f, ShapeRot = new Vector3(-90f, 0f, 0f), ShapePos = new Vector3(0f, .1f, 0f), ShapeScaling = true,
                    RiseY = .3f, Noise = .15f, NoiseFreq = .4f, Spin = 40f, RandomCell = true, FrameFrom = 0f, FrameTo = 16f,
                    Colour = G((0f, C(ash, 0f)), (.15f, C(ash, .8f)), (.7f, C(ash, .5f)), (1f, C(ash, 0f))) }), 1.4f, Vector2.zero, Vector2.zero);

                // the old perimeter becomes ash where it stood: world space, four strokes laid on the material's edge in the first
                // instant (the look is still at the material's radius then) that pale away while the fire grows past them
                Layer(t, new L { Name = "OldRing", Mat = ring, World = true, Mode = 1, Burst = 4, Max = 4, Life = new Vector2(1.6f, 2f), Size = new Vector2(.45f, .6f),
                    Shape = ParticleSystemShapeType.Circle, Radius = 1f, Thickness = 0f, ShapeRot = new Vector3(-90f, 0f, 0f), ShapePos = new Vector3(0f, .02f, 0f),
                    Blend = true, RandomStart = true, FrameFrom = 0f, FrameTo = 8f, Spin = 8f,
                    Colour = G((0f, C(ash, 0f)), (.25f, C(ash, .55f)), (1f, C(ash, 0f))) });

                // burnt out: the ground keeps a grey-brown ash stain, darker than the ground (End is played when the body dries)
                var end = new GameObject("End").transform; end.SetParent(t, false);
                Layer(end, new L { Name = "Settle", Mat = settle, World = false, Mode = 1, Burst = 1, Max = 1, Life = new Vector2(1.5f, 1.8f), Size = new Vector2(1.9f, 2.1f),
                    NoShape = true, ShapePos = new Vector3(0f, .012f, 0f), Blend = true, FrameFrom = 6f, FrameTo = 12f,
                    Colour = G((0f, C(ash, 0f)), (.2f, C(ash, .5f)), (1f, C(ash, 0f))), Grow = Curve(0f, .95f, 1f, 1.03f) });

                dryFade = 1.8f;
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) dryFade = Mathf.Max(dryFade, ps.main.startLifetime.constantMax);
                var body = root.AddComponent<Vfx308ZoneBody>();
                body.LateAfter = 0f; body.LateBefore = 0f; body.DryFade = dryFade;
                foreach (var x in root.GetComponentsInChildren<Transform>(true)) x.gameObject.layer = AfterFogLayer;
                return PrefabUtility.SaveAsPrefabAsset(root, ReactPath("wildfire"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ---------------------------------------------------------------- steam (rx_r_b3_steam)

        static GameObject SteamBody(out float dryFade)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(ReactPath("steam")));
            try
            {
                var t = root.transform;
                const string m = "M_V308z_react_steam_";
                Color wet = Hex("#3F5866"), crack = Hex("#2A2724"), foot = Hex("#4A5054"), top = Hex("#6E767A");
                var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                var wetMat = Bleed(m + "wet", "ink_pool", C(wet, .85f), 0f, .85f, 0f, 0f);
                var crackMat = Bleed(m + "crack", "ink_splat", C(crack, .85f), 0f, .85f, 0f, 0f);
                // cool grey smoke: the heart ink is cool too (B >= R), and a player crossing the strip is not blinded
                var mist = Bleed(m + "mist", "soot_wisp", C(Hex("#3C4448"), .8f), 0f, .6f, 0f, .4f);
                mist.SetFloat("_NearClip", .6f); mist.SetFloat("_NearFade", 1.6f); EditorUtility.SetDirty(mist);

                // 0 - .15 s: the ground cracks where the haze stood (two dark flat cracks, quick); sized by the radius in the presenter
                Layer(t, new L { Name = "Crack", Mat = crackMat, World = false, Mode = 1, Burst = 2, Max = 2, Life = new Vector2(.5f, .6f), Size = new Vector2(.9f, 1.1f), NoShape = true,
                    ShapePos = new Vector3(0f, .03f, 0f), ShapeScaling = true, Blend = true, FrameFrom = 0f, FrameTo = 12f,
                    Colour = G((0f, C(crack, .8f)), (.5f, C(crack, .5f)), (1f, C(crack, 0f))), Grow = Curve(0f, .7f, .4f, 1f, 1f, 1.05f) });
                // the wet track the water leaves: three flat strips along the way it ran (+Z of the look), steel-blue ink, held to the end
                var wetStrips = Flat(Layer(t, new L { Name = "Wet", Mat = wetMat, World = false, Burst = 3, BurstTime = .1f, Max = 3, Life = new Vector2(1.7f, 1.8f), Size = new Vector2(1f, 1f), Size3D = new Vector3(.3f, 1f, 1f),
                    Shape = ParticleSystemShapeType.Box, ShapeScale = new Vector3(.12f, .01f, .9f), ShapePos = new Vector3(0f, .02f, 0f), ShapeScaling = true, NoShape = false,
                    Mesh = quad, SpinRandom = false, Blend = true,
                    Colour = G((0f, C(wet, 0f)), (.12f, C(wet, .8f)), (.7f, C(wet, .7f)), (1f, C(wet, 0f))) }), 5f);
                var tsa = Sheet16(wetStrips); tsa.startFrame = new ParticleSystem.MinMaxCurve(5f, 9f); tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);

                // the smoke: low, cool, stretched along the way the water ran, two strips with the middle empty (the enemy stands there).
                // foot darker than the ground, tips only a little paler, nothing white; each of the 12 quads lives < 1 s
                foreach (var side in new[] { -1f, 1f })
                {
                    string tag = side < 0f ? "Near" : "Far";
                    Drift(Layer(t, new L { Name = "Foot" + tag, Mat = mist, World = false, Mode = 3, Rate = 5f, Max = 4, Life = new Vector2(.8f, 1f), Size = new Vector2(.7f, .95f),
                        Shape = ParticleSystemShapeType.Box, ShapeScale = new Vector3(.5f, .02f, .7f), ShapePos = new Vector3(0f, .06f, side * .62f), ShapeScaling = true,
                        Blend = true, RandomCell = true, FrameFrom = 0f, FrameTo = 6f,
                        Colour = G((0f, C(foot, 0f)), (.3f, C(foot, .28f)), (.7f, C(foot, .2f)), (1f, C(foot, 0f))), Grow = Curve(0f, .8f, 1f, 1.15f) }), .05f, .1f);
                    Drift(Layer(t, new L { Name = "Top" + tag, Mat = mist, World = false, Mode = 3, Rate = 2.5f, Max = 2, Life = new Vector2(.8f, 1f), Size = new Vector2(.6f, .8f),
                        Shape = ParticleSystemShapeType.Box, ShapeScale = new Vector3(.5f, .02f, .7f), ShapePos = new Vector3(0f, .3f, side * .62f), ShapeScaling = true,
                        Blend = true, RandomCell = true, FrameFrom = 0f, FrameTo = 6f,
                        Colour = G((0f, C(top, 0f)), (.3f, C(top, .12f)), (.7f, C(top, .09f)), (1f, C(top, 0f))), Grow = Curve(0f, .8f, 1f, 1.15f) }), .06f, .1f);
                }

                dryFade = 1f;
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) dryFade = Mathf.Max(dryFade, ps.main.startLifetime.constantMax);
                var body = root.AddComponent<Vfx308ZoneBody>();
                body.LateAfter = 0f; body.LateBefore = 0f; body.DryFade = dryFade;
                foreach (var x in root.GetComponentsInChildren<Transform>(true)) x.gameObject.layer = AfterFogLayer;
                return PrefabUtility.SaveAsPrefabAsset(root, ReactPath("steam"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        // drift along the way the water ran (+Z of the look): .6 m/s, sideways <= .12, up by `rise` (half the soot wisp's rise or less),
        // stretched 1.8 x along it (the quad is a streak of water, not a lump)
        static ParticleSystem Drift(ParticleSystem ps, float riseMin, float riseMax)
        {
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.Local;
            v.x = new ParticleSystem.MinMaxCurve(-.12f, .12f); v.y = new ParticleSystem.MinMaxCurve(riseMin, riseMax); v.z = new ParticleSystem.MinMaxCurve(.55f, .65f);
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.lengthScale = 1.8f;
            return ps;
        }
    }
}
