using System;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.SpellVFX120;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.57, the floors the lingering zones leave (곡 곳 녹 농 몽). Built at 1 m radius; the presenter
    // scales the prefab by the zone's radius and gives Vfx308ZoneBody the rule's life. Everything is flat or knee-low, local
    // (it follows the scale), no glow, no droplets, gone once it has dried:
    //   곡 - vines: flat wood-grain strokes crawling over a faint stain (the slowing field)
    //   곳 - thorns: short upright dark strokes that rise and sink at random over the stain (the damaging field)
    //   녹 - the heal zone: a warm ink pool, a scorched rim and thin heat wisps (ink, not light)
    //   농 - steam: pale ink mist in flat layers at shin height, drifting out
    //   몽 - muddy fog: earth-dust ink in flat layers, heavier and lower than 농
    // Every floor opens with one dark pour (ink first) under the steady body and has a Break child (one ink splat).
    //   zone           - build the five and point their _308b profiles at them
    public static partial class Vfx308Build
    {
        static readonly string[] ZoneIds = { "014_ACE1", "017_ACF3", "038_B179", "042_B18D", "066_BABD" };
        static string ZonePath(string id) => Root2 + "/Prefabs/PF_V308z_" + id + ".prefab";

        static string BuildZone()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build zone: Edit mode only");
            V2 = true; Touched.Clear(); seed = 46700;
            foreach (var d in new[] { "/Prefabs", "/Materials" }) Directory.CreateDirectory(Root2 + d);
            foreach (var s in new[] { "ink_pool", "ink_splat", "wood_stroke", "ink_wisp", "ink_smoke", "earth_dust" }) Sheet2(s);
            var kit = new Kit { Thorn = Model("thorn"), Splinter = Model("splinter"), Clod = Model("clod"), Chunk = Model("chunk"), Blade = Model("blade"), Stalk = Model("stalk"), Drop = Model("drop"), Tongue = Model("tongue") };
            var sb = new StringBuilder();
            foreach (string id in ZoneIds)
            {
                var a = Attack.First(x => x.Effect == id);
                var e = Element(kit, a.Elem); kit.Ink = e.Ink;
                var prefab = ZoneBody(id, a.Glyph, e, out float dry);
                if (!File.Exists(AttackProfilePath(a))) { sb.AppendLine(a.Glyph + ": the attack copy is not built (run build first) - floor " + prefab.name + " built, not assigned"); continue; }
                var profile = Load<Vfx120Profile>(AttackProfilePath(a));
                profile.Zone308Prefab = prefab;
                EditorUtility.SetDirty(profile); Touched.Add(profile);
                sb.AppendLine(a.Glyph + " zone -> " + ZonePath(id) + " (dry " + dry.ToString("0.00") + " s) on " + AttackProfilePath(a));
            }
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            return sb.ToString();
        }

        static GameObject ZoneBody(string id, string glyph, Elem e, out float dryFade)
        {
            var root = new GameObject("PF_V308z_" + id);
            try
            {
                var t = root.transform;
                string m = "M_V308z_" + id + "_";
                Color wash = Color.Lerp(e.Pig, e.Ink, .55f), dark = Color.Lerp(e.Pig, e.Ink, .8f);
                var pool = Bleed(m + "pool", "ink_pool", C(e.Ink, .85f), 0f, .85f, 0f, 0f);
                // ink first: one dark pour under the body, gone as the steady layers come in
                Layer(t, new L { Name = "Pour", Mat = pool, World = false, Mode = 1, Burst = 1, Max = 1, Life = new Vector2(1.4f, 1.6f), Size = new Vector2(2f, 2.3f),
                    NoShape = true, ShapePos = new Vector3(0f, .02f, 0f), Blend = true, FrameFrom = 3f, FrameTo = 12f, Colour = InkFade(.5f), Grow = Curve(0f, .55f, .35f, 1f, 1f, 1.1f) });
                switch (glyph)
                {
                    case "곡":   // vines crawl flat over the stain
                        Stain(t, pool, wash, .26f);
                        Layer(t, new L { Name = "Vines", Mat = SoakVariant(Bleed(m + "vine", "wood_stroke", C(e.Ink, .9f), 0f, .85f, 0f, 0f)), World = false, Mode = 1, Rate = 7f, Max = 18,
                            Life = new Vector2(1.8f, 2.4f), Size = new Vector2(.45f, .7f), Shape = ParticleSystemShapeType.Circle, Radius = .85f, Thickness = 1f, ShapeRot = new Vector3(-90f, 0f, 0f),
                            ShapePos = new Vector3(0f, .025f, 0f), Blend = true, RandomStart = true, FrameFrom = 0f, FrameTo = 8f, Spin = 12f,
                            Colour = G((0f, C(dark, 0f)), (.2f, C(dark, .6f)), (.7f, C(wash, .45f)), (1f, C(wash, 0f))), Grow = Curve(0f, .5f, .35f, 1f, 1f, 1.05f) });
                        break;
                    case "곳":   // thorns: short upright strokes rise, hold and sink, a few at a time
                        Stain(t, pool, wash, .22f);
                        Layer(t, new L { Name = "Thorns", Mat = SoakVariant(Bleed(m + "thorn", "wood_stroke", C(e.Ink, .95f), 0f, .9f)), World = false, Mode = 2, Rate = 9f, Max = 24,
                            Life = new Vector2(2f, 2.6f), Size = new Vector2(.18f, .26f), Pivot = new Vector3(0f, .45f, 0f), SpinRandom = false,
                            Shape = ParticleSystemShapeType.Circle, Radius = .85f, Thickness = 1f, ShapeRot = new Vector3(-90f, 0f, 0f), Blend = true, RandomCell = true, FrameFrom = 4f, FrameTo = 10f,
                            Colour = G((0f, C(e.Ink, 0f)), (.1f, C(e.Ink, .9f)), (.6f, C(dark, .75f)), (1f, C(dark, 0f))), Grow = Curve(0f, .2f, .12f, 1f, .8f, 1f, 1f, .55f) });
                        break;
                    case "녹":   // the heal zone: a warm pool, a scorched rim, thin heat wisps
                        Color warm = Color.Lerp(e.Pig, e.Ink, .5f);
                        Stain(t, pool, warm, .32f);
                        Layer(t, new L { Name = "Rim", Mat = SoakVariant(Bleed(m + "rim", "ink_pool", C(e.Ink, .85f), 0f, .8f, 0f, 0f)), World = false, Mode = 1, Rate = 9f, Max = 16,
                            Life = new Vector2(1.4f, 1.8f), Size = new Vector2(.5f, .65f), Shape = ParticleSystemShapeType.Circle, Radius = 1f, Thickness = 0f, ShapeRot = new Vector3(-90f, 0f, 0f),
                            ShapePos = new Vector3(0f, .03f, 0f), Blend = true, RandomStart = true, FrameFrom = 0f, FrameTo = 6f,
                            Colour = G((0f, C(dark, 0f)), (.25f, C(dark, .5f)), (.7f, C(dark, .35f)), (1f, C(dark, 0f))), Grow = Curve(0f, .85f, 1f, 1.05f) });
                        Layer(t, new L { Name = "Heat", Mat = Bleed(m + "heat", "ink_wisp", C(e.Ink, .7f), .25f, .6f), World = false, Mode = 2, Rate = 3f, Max = 6,
                            Life = new Vector2(1.2f, 1.6f), Size = new Vector2(.22f, .32f), RiseY = .25f, SpinRandom = false,
                            Shape = ParticleSystemShapeType.Circle, Radius = .6f, Thickness = 1f, ShapeRot = new Vector3(-90f, 0f, 0f), ShapePos = new Vector3(0f, .1f, 0f), Blend = true, FrameFrom = 0f, FrameTo = 10f,
                            Colour = G((0f, C(warm, 0f)), (.3f, C(warm, .3f)), (1f, C(dark, 0f))), Grow = Curve(0f, .6f, 1f, 1.2f) });
                        break;
                    case "농":   // steam: pale mist in flat layers at shin height, drifting out
                        Color steam = Color.Lerp(e.Ink, new Color(.62f, .6f, .57f), .62f);
                        Stain(t, pool, Color.Lerp(steam, e.Ink, .4f), .16f);
                        Mist(t, Bleed(m + "mist", "ink_smoke", C(Color.Lerp(e.Ink, steam, .5f), .8f), 0f, .6f, 0f, .3f), steam, .3f, new[] { .05f, .1f });
                        break;
                    default:     // 몽 - muddy fog: earth-dust ink, heavier and lower
                        Color mud = Color.Lerp(e.Pig, e.Ink, .62f);
                        Stain(t, pool, mud, .22f);
                        Mist(t, Bleed(m + "dust", "earth_dust", C(e.Ink, .85f), 0f, .7f, 0f, .3f), mud, .34f, new[] { .03f, .06f });
                        break;
                }
                // a broken zone: one ink splat, once (Break is not part of the body)
                var brk = new GameObject("Break").transform; brk.SetParent(t, false);
                Layer(brk, new L { Name = "Splat", Mat = e.Pool, World = false, Mode = 1, Burst = 1, Max = 1, Life = new Vector2(.6f, .7f), Size = new Vector2(1.8f, 2.1f),
                    NoShape = true, ShapePos = new Vector3(0f, .035f, 0f), Blend = true, FrameFrom = 0f, FrameTo = 12f, Colour = G((0f, C(e.Ink, .85f)), (.4f, C(dark, .6f)), (1f, C(dark, 0f))), Grow = Curve(0f, .6f, .3f, 1f, 1f, 1.1f) });

                dryFade = 1.2f;
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) dryFade = Mathf.Max(dryFade, ps.main.startLifetime.constantMax);
                var body = root.AddComponent<Vfx308ZoneBody>();
                body.LateAfter = 3f; body.LateAlpha = .35f; body.DryFade = dryFade;
                foreach (var x in root.GetComponentsInChildren<Transform>(true)) x.gameObject.layer = AfterFogLayer;
                return PrefabUtility.SaveAsPrefabAsset(root, ZonePath(id));
            }
            finally { Object.DestroyImmediate(root); }
        }

        // the faint steady stain: overlapping flat pool cels so it never blinks
        static void Stain(Transform t, Material pool, Color c, float alpha)
        {
            Layer(t, new L { Name = "Stain", Mat = pool, World = false, Mode = 1, Rate = 1.6f, Max = 6, Life = new Vector2(2.4f, 2.8f), Size = new Vector2(1.9f, 2.1f),
                NoShape = true, ShapePos = new Vector3(0f, .015f, 0f), Blend = true, RandomStart = true, FrameFrom = 0f, FrameTo = 3f,
                Colour = G((0f, C(c, 0f)), (.3f, C(c, alpha)), (.7f, C(c, alpha)), (1f, C(c, 0f))), Grow = Curve(0f, .95f, 1f, 1.03f) });
        }

        // low mist: flat layers (never upright - the player walks into a zone, nothing may stand in front of the eye)
        static void Mist(Transform t, Material mat, Color c, float alpha, float[] heights)
        {
            for (int i = 0; i < heights.Length; i++)
                Layer(t, new L { Name = "Mist" + i, Mat = mat, World = false, Mode = 1, Rate = 5f, Max = 14, Life = new Vector2(2.2f, 2.8f), Size = new Vector2(.7f, 1f), Speed = new Vector2(.04f, .1f),
                    Shape = ParticleSystemShapeType.Circle, Radius = .75f, Thickness = 1f, ShapeRot = new Vector3(-90f, 0f, 0f), ShapePos = new Vector3(0f, heights[i], 0f),
                    Blend = true, RandomStart = true, FrameFrom = 0f, FrameTo = 6f, Spin = 8f,
                    Colour = G((0f, C(c, 0f)), (.3f, C(c, alpha * (i == 0 ? 1f : .7f))), (.75f, C(c, alpha * .6f)), (1f, C(c, 0f))), Grow = Curve(0f, .7f, 1f, 1.2f) });
        }
    }
}
