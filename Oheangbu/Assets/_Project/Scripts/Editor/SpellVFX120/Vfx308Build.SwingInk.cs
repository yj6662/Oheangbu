using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.SpellVFX120;
using Oheangbu.App.World;
using Oheangbu.Data.Spell;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.10 (D308-83a): the ink of the five swings (SwingSweep / SwingArc / SwingSlam /
    // SwingPlant / SwingDraw). Built from the v2 bleeding-ink sheets that already exist (ink_splat, ink_drops, ink_pool) -
    // no new texture. One prefab per swing state with the parts SwingInk308 moves: "Tip" follows the brush tip, "Apex" is
    // placed at the tip when the swing peaks, "Ground" on the ground under it. Ink colour only: the element's pigment
    // belongs to the spell that comes out of this ink, not to the ink - until the apex: from there D308-90 lets the
    // spell's pigment soak into the swung arc (SwingInk308Profile.Soaks, written here for all 120 letters from each one's
    // own profile).
    //
    //   swingink                    build the five prefabs, their materials and Resources/SwingInk308/SwingInk308Profile (= on)
    //   swingink:off                delete the Resources profile (= off; the prefabs stay)
    //   swingink:pose:<state>:<s>   edit-mode pose at <s> seconds after the release. The tip path is a STAND-IN arc drawn
    //                               here, not the animation clip - it shows the ink, not how well it sits on the motion.
    //                               optional :<px,py,pz>:<fx,fy,fz> = where the caster stands and faces
    //   swingink:pose:clear
    public static partial class Vfx308Build
    {
        const string SwingResources = "Assets/_Project/Resources/SwingInk308";
        const string SwingProfilePath = SwingResources + "/SwingInk308Profile.asset";
        const string SwingPoseName = "__Vfx308SwingPose";
        static readonly Color SwingInkColour = new Color(.05f, .047f, .045f, 1f);

        sealed class Swing
        {
            public string State; public float Width, Seconds, VisualApex, Follow, Apex;     // Apex: the rule's apex (pose only)
            public float Streaks = 14f, Dry = .85f;
            public Func<Swing, GameObject> Body;
        }

        static readonly Swing[] Swings =
        {
            new Swing { State = "SwingSweep", Width = .17f, Seconds = .60f, Follow = .10f, Apex = .40f, Body = SweepInk },
            new Swing { State = "SwingArc", Width = .24f, Seconds = .75f, Follow = .12f, Apex = .65f, Streaks = 11f, Body = ArcInk },
            new Swing { State = "SwingSlam", Width = .14f, Seconds = .40f,   /* sw_w: .2 / .6 - the far strike arc crossed the view */ Follow = .05f, Apex = .65f, Body = SlamInk },
            new Swing { State = "SwingPlant", Width = .18f, Seconds = .42f, VisualApex = .12f, Follow = .06f, Dry = .7f, Body = PlantInk },
            new Swing { State = "SwingDraw", Width = .1f, Seconds = .90f, VisualApex = .22f, Follow = .30f, Streaks = 18f, Dry = 1f, Body = DrawInk },
            // D308-84: the far single throw. Play sw_m: the sweep's thrown sheets, flung straight ahead from head height, stretched
            // along the camera's own line and covered the middle of the view - its own smaller ink; the stem bridge carries the rest
            new Swing { State = "SwingThrow", Width = .09f, Seconds = .32f,   /* sw_w: .15 / .55 - the throw arc read before the spell */ Follow = .08f, Apex = .40f, Body = ThrowInk },
        };

        static string SwingPrefabPath(Swing s) => Root2 + "/Prefabs/PF_V308c_swing_" + s.State.Substring(5).ToLowerInvariant() + ".prefab";

        static Material SwingStroke(Swing s)
        {
            string path = Root2 + "/Materials/M_V308c_swing_stroke_" + s.State.Substring(5).ToLowerInvariant() + ".mat";
            var shader = Shader.Find("Oheangbu/SwingInk308/InkArc") ?? throw new Exception("InkArc shader not found");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetColor("_InkColor", SwingInkColour); m.SetFloat("_Opacity", .95f);
            m.SetFloat("_StreakDensity", s.Streaks); m.SetFloat("_DryStrength", s.Dry); m.SetFloat("_EdgeSoftness", .12f);
            EditorUtility.SetDirty(m); Touched.Add(m);
            return m;
        }

        static Material SwingSheet(string sheet, float opacity = .92f, float soft = -1f) =>
            Bleed("M_V308c_swing_" + sheet + (soft == 0f ? "_flat" : ""), sheet, C(SwingInkColour, opacity), 0f, opacity, 0f, soft);

        // particle colour is the ink itself in the bleed shader (white would be white paint): dark ink, whole, then gone
        static readonly Color SwingDark = new Color(.06f, .055f, .05f, 1f);
        static Gradient InkFade(float hold = .4f) => G((0f, C(SwingDark, 1f)), (hold, C(SwingDark, 1f)), (1f, C(SwingDark, 0f)));

        // drops that leave the tip as it moves: one per so many centimetres of travel, carrying part of the tip's speed
        static void Thrown(Transform tip, float perMetre, Vector2 size, float inherit = .55f, float gravity = 1.6f, int max = 40, Vector2? life = null)
        {
            var ps = Layer(tip, new L { Name = "Drops", Mat = SwingSheet("ink_drops", .9f), Rate = .001f, Max = max, Life = life ?? new Vector2(.35f, .6f), Size = size, Speed = new Vector2(.2f, 1.2f), Gravity = gravity, Radius = .05f, Drag = .6f,
                RandomCell = true, Blend = true, Colour = InkFade(.5f), Grow = Curve(0f, .7f, 1f, 1.5f) });
            var em = ps.emission; em.rateOverTime = 0f; em.rateOverDistance = perMetre;
            var iv = ps.inheritVelocity; iv.enabled = true; iv.mode = ParticleSystemInheritVelocityMode.Initial; iv.curve = new ParticleSystem.MinMaxCurve(inherit * .6f, inherit);
        }

        static (GameObject root, Transform tip, Transform apex, Transform ground) SwingShell(Swing s)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(SwingPrefabPath(s)));
            Transform Child(string name) { var t = new GameObject(name).transform; t.SetParent(root.transform, false); return t; }
            return (root, Child(SwingInk308.TipName), Child(SwingInk308.ApexName), Child(SwingInk308.GroundName));
        }

        // sweep (single attack): one thick line across the body; at the apex the ink is thrown off the tip the way it was going
        static GameObject SweepInk(Swing s)
        {
            var (root, tip, apex, _) = SwingShell(s);
            Thrown(tip, 10f, new Vector2(.12f, .30f));
            // the thrown ink itself: bristle lines that run the way of the throw (ink_throw sheet, drawn along its velocity)
            Layer(apex, new L { Name = "Throw", Mat = SwingSheet("ink_throw", .95f), Mode = 3, Burst = 3, Max = 4, Life = new Vector2(.5f, .62f), Size = new Vector2(1.6f, 2.2f), Speed = new Vector2(6f, 9f), Drag = 2.6f, SpinRandom = false, Blend = true,
                Shape = ParticleSystemShapeType.Cone, Angle = 7f, Radius = .08f, FrameFrom = 0f, FrameTo = 15.5f, Colour = InkFade(.55f), Grow = Curve(0f, .7f, .25f, 1f, 1f, 1.1f) });
            Layer(apex, new L { Name = "Fling", Mat = SwingSheet("ink_splat"), Burst = 2, Max = 3, Life = new Vector2(.42f, .42f), Size = new Vector2(1.4f, 1.8f), Speed = new Vector2(2.5f, 4f), Drag = 3f, NoShape = true, Blend = true,
                FrameFrom = 0f, FrameTo = 12f, Colour = InkFade(.35f), Grow = Curve(0f, .55f, .3f, 1f, 1f, 1.3f) });
            Layer(apex, new L { Name = "Spray", Mat = SwingSheet("ink_drops", .9f), Burst = 12, Max = 12, Life = new Vector2(.3f, .55f), Size = new Vector2(.14f, .38f), Speed = new Vector2(5f, 11f), Drag = 1.6f, Gravity = 1.8f,
                Shape = ParticleSystemShapeType.Cone, Angle = 16f, Radius = .06f, RandomCell = true, Blend = true, Colour = InkFade(.45f), Grow = Curve(0f, .8f, 1f, 1.5f) });
            return root;
        }

        // throw (single attack, far target): the overhead stroke; at the apex a small burst of ink leaves the tip forward and down
        static GameObject ThrowInk(Swing s)
        {
            var (root, tip, apex, _) = SwingShell(s);
            Thrown(tip, 9f, new Vector2(.10f, .26f));
            Layer(apex, new L { Name = "Fling", Mat = SwingSheet("ink_splat"), Burst = 1, Max = 2, Life = new Vector2(.35f, .35f), Size = new Vector2(.8f, 1.0f), Speed = new Vector2(1.5f, 2.5f), Drag = 3f, NoShape = true, Blend = true,
                FrameFrom = 0f, FrameTo = 12f, Colour = InkFade(.35f), Grow = Curve(0f, .55f, .3f, 1f, 1f, 1.2f) });
            Layer(apex, new L { Name = "Spray", Mat = SwingSheet("ink_drops", .9f), Burst = 10, Max = 10, Life = new Vector2(.3f, .5f), Size = new Vector2(.12f, .3f), Speed = new Vector2(4f, 9f), Drag = 1.6f, Gravity = 2.4f,
                Shape = ParticleSystemShapeType.Cone, Angle = 14f, Radius = .05f, RandomCell = true, Blend = true, Colour = InkFade(.45f), Grow = Curve(0f, .8f, 1f, 1.4f) });
            return root;
        }

        // arc (area attack): the widest, longest stroke; the ink is cast wide over the ground ahead
        static GameObject ArcInk(Swing s)
        {
            var (root, tip, apex, _) = SwingShell(s);
            Thrown(tip, 13f, new Vector2(.16f, .42f), .6f, 1.4f, 56);
            Layer(apex, new L { Name = "Throw", Mat = SwingSheet("ink_throw", .95f), Mode = 3, Burst = 4, Max = 5, Life = new Vector2(.5f, .65f), Size = new Vector2(1.0f, 1.5f), Speed = new Vector2(6f, 10f), Drag = 2.2f, SpinRandom = false, Blend = true,
                Shape = ParticleSystemShapeType.Cone, Angle = 30f, Radius = .15f, FrameFrom = 0f, FrameTo = 15.5f, Colour = InkFade(.55f), Grow = Curve(0f, .7f, .25f, 1f, 1f, 1.1f) });
            Layer(apex, new L { Name = "Cast", Mat = SwingSheet("ink_splat"), Burst = 2, Max = 3, Life = new Vector2(.45f, .55f), Size = new Vector2(1.0f, 1.4f), Speed = new Vector2(2.5f, 5f), Drag = 2.4f, Blend = true,
                Shape = ParticleSystemShapeType.Cone, Angle = 28f, Radius = .2f, FrameFrom = 0f, FrameTo = 13f, Colour = InkFade(.35f), Grow = Curve(0f, .5f, .3f, 1f, 1f, 1.35f) });
            Layer(apex, new L { Name = "Spray", Mat = SwingSheet("ink_drops", .9f), Burst = 22, Max = 22, Life = new Vector2(.35f, .7f), Size = new Vector2(.16f, .46f), Speed = new Vector2(5f, 13f), Drag = 1.4f, Gravity = 2.2f,
                Shape = ParticleSystemShapeType.Cone, Angle = 38f, Radius = .1f, RandomCell = true, Blend = true, Colour = InkFade(.45f), Grow = Curve(0f, .8f, 1f, 1.5f) });
            return root;
        }

        // slam (summon / field / install): the stroke comes down; the ink lands - a pool, a crown of drops, a standing splash
        static GameObject SlamInk(Swing s)
        {
            var (root, tip, _, ground) = SwingShell(s);
            Thrown(tip, 8f, new Vector2(.14f, .34f), .4f, 2.2f);
            Layer(ground, new L { Name = "Pool", Mat = Bleed("M_V308c_swing_ink_pool", "ink_pool", C(SwingInkColour, .92f), 0f, .92f, 0f, 0f), World = false, Mode = 1, Burst = 1, Max = 2, Life = new Vector2(1.2f, 1.2f), Size = new Vector2(3.8f, 3.8f), NoShape = true, Blend = true,
                FrameFrom = 0f, FrameTo = 15.5f, Colour = InkFade(.6f), Grow = Curve(0f, .45f, .25f, 1f, 1f, 1.15f) });
            Layer(ground, new L { Name = "Splash", Mat = SwingSheet("ink_splat"), World = false, Mode = 2, Burst = 1, Max = 2, Life = new Vector2(.5f, .5f), Size = new Vector2(2.4f, 2.4f), NoShape = true, Blend = true, SpinRandom = false, Pivot = new Vector3(0f, .32f, 0f),
                FrameFrom = 0f, FrameTo = 13f, Colour = InkFade(.35f), Grow = Curve(0f, .6f, .3f, 1f, 1f, 1.2f) });
            Layer(ground, new L { Name = "Crown", Mat = SwingSheet("ink_drops", .9f), Burst = 16, Max = 16, Life = new Vector2(.4f, .75f), Size = new Vector2(.16f, .44f), Speed = new Vector2(3f, 6.5f), Gravity = 3f,
                Shape = ParticleSystemShapeType.Cone, Angle = 38f, Radius = .5f, ShapeRot = new Vector3(-90f, 0f, 0f), RandomCell = true, Blend = true, Colour = InkFade(.5f), Grow = Curve(0f, .8f, 1f, 1.4f) });
            // D308-84 circle bridge: the ink running over the ground to the centre, as soaked blots (SwingInk308 emits them along
            // the path; no rate, no burst). The pool sheet spreads then dries over the life, flat on the ground.
            Layer(root.transform, new L { Name = SwingInk308.RunName, Mat = Bleed("M_V308c_swing_ink_pool", "ink_pool", C(SwingInkColour, .92f), 0f, .92f, 0f, 0f), World = true, Mode = 1, Max = 128,
                Life = new Vector2(1.5f, 1.9f), Size = new Vector2(.8f, 1f), NoShape = true, Blend = true, FrameFrom = 4f, FrameTo = 12f, Colour = InkFade(.7f),   // sw_o: 2 - 15.5 / .55 read faint
                Grow = Curve(0f, .55f, .2f, 1f, 1f, 1.08f) });
            return root;
        }

        // plant (parry / ward): a short heavy stroke down; a sheet of ink stands up in front of the feet
        static GameObject PlantInk(Swing s)
        {
            var (root, tip, _, ground) = SwingShell(s);
            Thrown(tip, 6f, new Vector2(.10f, .24f), .3f, 2.4f, 16);
            Layer(ground, new L { Name = "Sheet", Mat = SwingSheet("ink_splat"), World = false, Mode = 2, Burst = 1, Max = 2, Life = new Vector2(.7f, .7f), Size = new Vector2(2.4f, 2.4f), NoShape = true, Blend = true, SpinRandom = false, Pivot = new Vector3(0f, .36f, 0f),
                FrameFrom = 0f, FrameTo = 10f, Colour = InkFade(.6f), Grow = Curve(0f, .7f, .25f, 1f, 1f, 1.1f) });
            Layer(ground, new L { Name = "Pool", Mat = Bleed("M_V308c_swing_ink_pool", "ink_pool", C(SwingInkColour, .92f), 0f, .92f, 0f, 0f), World = false, Mode = 1, Burst = 1, Max = 2, Life = new Vector2(.8f, .8f), Size = new Vector2(2.1f, 2.1f), NoShape = true, Blend = true,
                FrameFrom = 0f, FrameTo = 15.5f, Colour = InkFade(.5f), Grow = Curve(0f, .5f, .25f, 1f, 1f, 1.1f) });
            return root;
        }

        // draw (buff): a thin long stroke round the body; the ink lifts off it and climbs
        static GameObject DrawInk(Swing s)
        {
            var (root, tip, apex, _) = SwingShell(s);
            Thrown(tip, 7f, new Vector2(.10f, .22f), .15f, -.35f, 24, new Vector2(.5f, .85f));     // negative gravity: the drops drift up
            Layer(apex, new L { Name = "Rise", Mat = SwingSheet("ink_drops", .8f), Burst = 7, Max = 7, Life = new Vector2(.6f, .95f), Size = new Vector2(.22f, .42f), Speed = new Vector2(.1f, .5f), RiseY = 1.3f, Radius = .4f,
                RandomCell = true, Blend = true, Colour = InkFade(.55f), Grow = Curve(0f, .6f, 1f, 1.8f) });
            return root;
        }

        static string SwingInk(string[] a)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build swingink: Edit mode only");
            if (a.Length > 1 && a[1] == "pose") return SwingPose(a);
            if (a.Length > 1 && a[1] == "off")
            {
                ClearSwingPose();
                return AssetDatabase.DeleteAsset(SwingProfilePath) ? "swing ink off: " + SwingProfilePath + " deleted (prefabs kept)" : "swing ink was already off";
            }
            if (a.Length > 1) throw new ArgumentException("swingink | swingink:off | swingink:pose:<state>:<seconds> | swingink:pose:clear");
            V2 = true; Touched.Clear(); seed = 30900;
            foreach (var d in new[] { "/Prefabs", "/Materials" }) Directory.CreateDirectory(Root2 + d);
            Directory.CreateDirectory(SwingResources);
            AssetDatabase.Refresh();
            foreach (var sheet in new[] { "ink_splat", "ink_drops", "ink_pool", "ink_throw" }) Sheet2(sheet);
            var sb = new StringBuilder();
            var entries = new List<SwingInk308Profile.Entry>();
            foreach (var s in Swings)
            {
                var root = s.Body(s);
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = AfterFogLayer;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, SwingPrefabPath(s));
                Object.DestroyImmediate(root);
                entries.Add(new SwingInk308Profile.Entry { State = s.State, Prefab = prefab, Stroke = SwingStroke(s), StrokeWidth = s.Width, StrokeSeconds = s.Seconds, VisualApexSeconds = s.VisualApex, FollowAfterApex = s.Follow });
                sb.AppendLine(s.State + " -> " + SwingPrefabPath(s) + "  stroke " + s.Width.ToString("0.00", CultureInfo.InvariantCulture) + " m / " + s.Seconds.ToString("0.00", CultureInfo.InvariantCulture) + " s, particles <= " +
                    prefab.GetComponentsInChildren<ParticleSystem>(true).Sum(p => p.main.maxParticles));
            }
            var profile = AssetDatabase.LoadAssetAtPath<SwingInk308Profile>(SwingProfilePath);
            if (profile == null) { profile = ScriptableObject.CreateInstance<SwingInk308Profile>(); AssetDatabase.CreateAsset(profile, SwingProfilePath); }
            profile.States = entries.ToArray();
            SwingSoaks(profile, sb);
            EditorUtility.SetDirty(profile); Touched.Add(profile);
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);     // only this tool's own assets (the editor is shared)
            sb.AppendLine("profile " + SwingProfilePath + " valid=" + profile.IsValid + " (swing ink is ON from the next Play)");
            return sb.ToString();
        }

        // D308-90 (11.34 20th / 21st pass) [every number TEST]: from the apex on, the swung arc takes the spell's pigment, tip
        // first, and stays a little longer. All 120 letters at once (user "전 술식 한 번에"), every row with 노's numbers.
        // The pigment comes from each letter's own original profile (SpellVFX120/Profiles, NNN_HHHH.asset): per element the
        // most common pigment is the element row (Letters empty), every other pigment is a letter row listing its letters.
        // SoakFor takes the first match, so letter rows go before the element rows. No row at all leaves the rows already
        // there (and says so).
        const string SoakProfiles = "Assets/_Project/Art/SpellVFX120/Profiles";
        const string SoakProfilesV2 = "Assets/_Project/Art/SpellVFX308/V2/Profiles";
        const float SoakMax = .75f, SoakSeconds = .28f, SoakReach = .35f, SoakSoft = .08f, SoakAging = .55f;

        static bool SoakElement(char glyph, out Oheangbu.Core.Domain.Element element)
        {
            element = default;
            int o = glyph - 0xAC00;
            if (o < 0 || o >= 11172) return false;
            switch (o / 588)     // the initial consonant: 0 ㄱ, 2 ㄴ, 6 ㅁ, 9 ㅅ, 11 ㅇ (= the vocabulary CSV's 속성 for all 120 rows)
            {
                case 0: element = Oheangbu.Core.Domain.Element.Wood; return true;
                case 2: element = Oheangbu.Core.Domain.Element.Fire; return true;
                case 6: element = Oheangbu.Core.Domain.Element.Earth; return true;
                case 9: element = Oheangbu.Core.Domain.Element.Metal; return true;
                case 11: element = Oheangbu.Core.Domain.Element.Water; return true;
                default: return false;
            }
        }

        static void SwingSoaks(SwingInk308Profile profile, StringBuilder sb)
        {
            var found = new List<(string Name, char Glyph, Oheangbu.Core.Domain.Element Element, Color Pigment)>();
            string[] files = Directory.Exists(SoakProfiles) ? Directory.GetFiles(SoakProfiles, "*.asset").OrderBy(x => x, StringComparer.Ordinal).ToArray() : new string[0];
            int warnings = 0;
            void Warn(string s) { warnings++; Debug.LogWarning("Vfx308Build swingink: " + s); sb.AppendLine("soak warning: " + s); }
            foreach (var file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                string[] parts = name.Split('_');
                if (parts.Length < 2 || !int.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code)) { Warn("no glyph in the name " + name + " - skipped"); continue; }
                char glyph = (char)code;
                if (!SoakElement(glyph, out var element)) { Warn(name + " (" + glyph + ") has no five-element initial - skipped"); continue; }
                string path = SoakProfiles + "/" + name + ".asset";
                var source = AssetDatabase.LoadAssetAtPath<Vfx120Profile>(path);
                if (source == null) { Warn("cannot read " + path + " - " + glyph + " falls to its element row"); continue; }
                found.Add((name, glyph, element, source.Pigment));
                var v2 = AssetDatabase.LoadAssetAtPath<Vfx120Profile>(SoakProfilesV2 + "/" + name + "_308b.asset");
                if (v2 != null && ColorUtility.ToHtmlStringRGB(v2.Pigment) != ColorUtility.ToHtmlStringRGB(source.Pigment))
                    Warn(glyph + ": the V2 copy " + name + "_308b has #" + ColorUtility.ToHtmlStringRGB(v2.Pigment) + ", the original #" + ColorUtility.ToHtmlStringRGB(source.Pigment) + " - the original is used");
            }
            if (files.Length != 120) Warn(files.Length + " profiles in " + SoakProfiles + " (expected 120)");
            if (found.Count == 0) { Warn("no pigment read - soak rows left as they were"); return; }

            var letterRows = new List<SwingInk308Profile.Soak>();
            var elementRows = new List<SwingInk308Profile.Soak>();
            var lines = new List<string>();
            foreach (var byElement in found.GroupBy(x => x.Element).OrderBy(g => (int)g.Key))
            {
                // pigments as 8-bit colours, the most common first (ties: the one that turns up first in file order)
                var pigments = byElement.GroupBy(x => ColorUtility.ToHtmlStringRGBA(x.Pigment))
                    .Select((g, i) => (Hex: g.Key, Members: g.ToList(), First: i))
                    .OrderByDescending(g => g.Members.Count).ThenBy(g => g.First).ToList();
                for (int k = 0; k < pigments.Count; k++)
                {
                    var p = pigments[k];
                    string letters = k == 0 ? "" : new string(p.Members.Select(x => x.Glyph).ToArray());
                    var row = new SwingInk308Profile.Soak { Element = byElement.Key, Letters = letters, Pigment = p.Members[0].Pigment, Max = SoakMax, Seconds = SoakSeconds, Reach = SoakReach, Soft = SoakSoft, AgingAfterApex = SoakAging };
                    (k == 0 ? elementRows : letterRows).Add(row);
                    lines.Add((k == 0 ? "element " : "letters ") + byElement.Key + " " + (k == 0 ? "*" : letters) + " #" + ColorUtility.ToHtmlStringRGB(row.Pigment) + " x" + p.Members.Count
                        + (row.Usable ? "" : " (NOT usable - skipped at runtime)"));
                }
            }
            profile.Soaks = letterRows.Concat(elementRows).ToArray();
            foreach (var s in lines.Where(x => x.StartsWith("letters ", StringComparison.Ordinal)).Concat(lines.Where(x => x.StartsWith("element ", StringComparison.Ordinal))))
                sb.AppendLine("soak " + s);
            sb.AppendLine("soak rows " + profile.Soaks.Length + " (" + letterRows.Count + " letter + " + elementRows.Count + " element) from " + found.Count + "/" + files.Length + " profiles, max "
                + SoakMax.ToString("0.00", CultureInfo.InvariantCulture) + " over " + SoakSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s, reach " + SoakReach.ToString("0.00", CultureInfo.InvariantCulture)
                + ", aging x" + SoakAging.ToString("0.00", CultureInfo.InvariantCulture) + ", warnings " + warnings);
        }

        static void ClearSwingPose()
        {
            foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>().Where(x => x != null && x.name == SwingPoseName && !EditorUtility.IsPersistent(x)).ToArray())
                Object.DestroyImmediate(g);
        }

        // a stand-in for the brush tip of each swing, in the caster's frame (x right, y up from the feet, z forward); u = 0..1
        // over the time to the apex, and it keeps going a little after it
        static Vector3 StandIn(string state, float u)
        {
            float e = Mathf.Min(Mathf.Pow(Mathf.Max(0f, u), 1.7f), 1.12f);     // a swing gathers speed into its apex and runs on a little
            switch (state)
            {
                case "SwingSweep": { float th = Mathf.Lerp(-80f, 75f, e) * Mathf.Deg2Rad; return new Vector3(-1.45f * Mathf.Sin(th), 1.45f - .3f * e, .25f + 1.35f * Mathf.Cos(th)); }
                case "SwingArc": { float th = Mathf.Lerp(-125f, 60f, e) * Mathf.Deg2Rad; return new Vector3(.7f - 1.3f * e, 1.2f + 1.7f * Mathf.Cos(th * .62f) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(e * .92f + .08f)), .2f + 1.6f * Mathf.Sin(th) * .5f + 1.1f * e); }
                case "SwingSlam": return Vector3.Lerp(new Vector3(.25f, 2.7f, .2f), new Vector3(0f, .08f, 1.55f), e * e);
                case "SwingPlant": return Vector3.Lerp(new Vector3(.3f, 1.35f, .45f), new Vector3(.1f, .08f, .75f), e);
                default: { float th = Mathf.Lerp(-40f, 320f, e) * Mathf.Deg2Rad; return new Vector3(.95f * Mathf.Sin(th), 1.1f + .55f * e, .95f * Mathf.Cos(th)); }
            }
        }

        static string SwingPose(string[] a)
        {
            ClearSwingPose();
            if (a.Length > 2 && a[2] == "clear") return "swing pose cleared";
            if (a.Length < 4) throw new ArgumentException("swingink:pose:<state>:<seconds>[:px,py,pz:fx,fy,fz]");
            var s = Swings.FirstOrDefault(x => x.State == a[2] || x.State.Substring(5).Equals(a[2], StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("unknown swing state " + a[2]);
            float at = float.Parse(a[3], CultureInfo.InvariantCulture);
            Vector3 feet = a.Length > 4 && a[4].Contains(",") ? Vec(a[4]) : new Vector3(3340f, 100f, 2690f);
            feet.y = Ground(feet);
            Vector3 forward = a.Length > 5 && a[5].Contains(",") ? Vec(a[5]) : Vector3.forward;
            forward.y = 0f; forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            var frame = Quaternion.LookRotation(forward, Vector3.up);
            var profile = AssetDatabase.LoadAssetAtPath<SwingInk308Profile>(SwingProfilePath) ?? throw new Exception("no swing ink profile: run swingink first");
            int index = profile.IndexOf(s.State); if (index < 0) throw new Exception("the profile has no entry for " + s.State);
            var entry = profile.States[index];
            float apex = s.Apex > 0f ? s.Apex : entry.VisualApexSeconds, follow = apex + entry.FollowAfterApex;

            var holder = new GameObject(SwingPoseName) { hideFlags = HideFlags.HideAndDontSave };
            var root = Object.Instantiate(entry.Prefab, holder.transform); root.transform.SetPositionAndRotation(feet, Quaternion.identity);
            var strokeObject = new GameObject("Stroke"); strokeObject.transform.SetParent(holder.transform, false); strokeObject.layer = AfterFogLayer;
            var mesh = new Mesh { name = "swing pose stroke", hideFlags = HideFlags.HideAndDontSave };
            strokeObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = strokeObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = entry.Stroke; renderer.shadowCastingMode = ShadowCastingMode.Off;
            foreach (var t in holder.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            Transform tip = root.transform.Find(SwingInk308.TipName), apexPart = root.transform.Find(SwingInk308.ApexName), ground = root.transform.Find(SwingInk308.GroundName);
            var tipSystems = tip.GetComponentsInChildren<ParticleSystem>(true); var apexSystems = apexPart.GetComponentsInChildren<ParticleSystem>(true); var groundSystems = ground.GetComponentsInChildren<ParticleSystem>(true);

            Vector3 Tip(float time) => feet + frame * StandIn(s.State, time / Mathf.Max(.01f, apex));
            var samples = new List<SwingInk308.Sample>();
            tip.position = Tip(0f);
            foreach (var ps in tipSystems.Concat(apexSystems).Concat(groundSystems)) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Simulate(0, false, true, false); }
            const float step = 1f / 60f; float simulated = 0f; bool peaked = false; Vector3 previous = tip.position, velocity = Vector3.zero;
            while (simulated + step <= at + .00001f)
            {
                float now = simulated + step; bool following = now <= follow;
                if (following)
                {
                    Vector3 p = Tip(now); velocity = (p - previous) / step; previous = p; tip.position = p;
                    float width = entry.StrokeWidth * Mathf.SmoothStep(.35f, 1f, Mathf.Clamp01(now / .1f));
                    if (samples.Count == 0 || (samples[samples.Count - 1].Point - p).sqrMagnitude > profile.MinVertexDistance * profile.MinVertexDistance)
                        samples.Add(new SwingInk308.Sample { Point = p, Time = now, Width = width });
                }
                foreach (var ps in tipSystems) { var em = ps.emission; em.enabled = following; ps.Simulate(step, false, false, false); }
                if (!peaked && now >= apex)
                {
                    peaked = true;
                    Vector3 way = velocity.sqrMagnitude > .25f ? velocity.normalized : forward;
                    if (Vector3.Dot(way, forward) < 0f) way = Vector3.Reflect(way, forward);
                    apexPart.SetPositionAndRotation(previous, Quaternion.LookRotation(way, Mathf.Abs(way.y) > .98f ? forward : Vector3.up));
                    Vector3 land = previous; land.y = Ground(previous) + .03f;
                    ground.SetPositionAndRotation(land, frame);
                }
                if (peaked) foreach (var ps in apexSystems.Concat(groundSystems)) ps.Simulate(step, false, false, false);
                simulated = now;
            }
            samples.RemoveAll(x => at - x.Time > entry.StrokeSeconds);
            var view = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
            Vector3 eye = a.Length > 6 && a[6].Contains(",") ? Vec(a[6]) : view != null ? view.transform.position : feet - forward * 4f + Vector3.up * 1.7f;
            SwingInk308.BuildRibbon(mesh, samples, at, entry.StrokeSeconds, eye, new List<Vector3>(), new List<Vector2>(), new List<Color>(), new List<int>());
            SceneView.RepaintAll();
            return "swing pose " + s.State + " at " + at.ToString("0.00", CultureInfo.InvariantCulture) + " s (apex " + apex.ToString("0.00", CultureInfo.InvariantCulture) + "), feet " + feet + ", stroke points " + samples.Count +
                ", particles " + tipSystems.Concat(apexSystems).Concat(groundSystems).Sum(p => p.particleCount) + "  [stand-in tip path, not the clip]";
        }
    }
}
