using System;
using System.IO;
using System.Linq;
using Oheangbu.App.SpellVFX120;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.59, the rock 막 leaves where it lands (single.cover, Role Ward). The rule keeps the rock
    // cover.life seconds; the presenter puts this body down through the 11.57 floor table, scales it by cover.radius and gives
    // Vfx308ZoneBody the rule's life (dark for 3 s, faint after, dries on Expire or when a newer rock makes room). Built at 1 m
    // radius. An ink rock, not a stone: one dark pour, a faint earth stain, a hump of overlapping upright earth-heave cels held
    // still (start speed 0, rate-based so the late fade reaches it), a low earth-dust skirt. No glow, no droplets, nothing left
    // once it has dried. Break child = one ink splat.
    //   rock           - build the one prefab and point the 막 _308b profile at it
    public static partial class Vfx308Build
    {
        const string RockId = "050_B9C9";
        static string RockPath => Root2 + "/Prefabs/PF_V308z_" + RockId + ".prefab";

        static string BuildRock()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build rock: Edit mode only");
            V2 = true; Touched.Clear(); seed = 47100;
            foreach (var d in new[] { "/Prefabs", "/Materials" }) Directory.CreateDirectory(Root2 + d);
            foreach (var s in new[] { "ink_pool", "ink_splat", "earth_heave", "earth_dust" }) Sheet2(s);
            var kit = new Kit { Thorn = Model("thorn"), Splinter = Model("splinter"), Clod = Model("clod"), Chunk = Model("chunk"), Blade = Model("blade"), Stalk = Model("stalk"), Drop = Model("drop"), Tongue = Model("tongue") };
            var a = Attack.First(x => x.Effect == RockId);
            var e = Element(kit, a.Elem); kit.Ink = e.Ink;
            var prefab = RockBody(e, out float dry);
            if (!File.Exists(AttackProfilePath(a))) return a.Glyph + ": the attack copy is not built (run build first) - rock " + prefab.name + " built, not assigned";
            var profile = Load<Vfx120Profile>(AttackProfilePath(a));
            profile.Zone308Prefab = prefab;
            EditorUtility.SetDirty(profile); Touched.Add(profile);
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            return a.Glyph + " rock -> " + RockPath + " (dry " + dry.ToString("0.00") + " s) on " + AttackProfilePath(a);
        }

        static GameObject RockBody(Elem e, out float dryFade)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(RockPath));
            try
            {
                var t = root.transform;
                string m = "M_V308z_" + RockId + "_";
                Color wash = Color.Lerp(e.Pig, e.Ink, .55f), dark = Color.Lerp(e.Pig, e.Ink, .82f);
                var pool = Bleed(m + "pool", "ink_pool", C(e.Ink, .85f), 0f, .85f, 0f, 0f);
                // ink first: one dark pour where the rock lands
                Layer(t, new L { Name = "Pour", Mat = pool, World = false, Mode = 1, Burst = 1, Max = 1, Life = new Vector2(1.2f, 1.4f), Size = new Vector2(2f, 2.3f),
                    NoShape = true, ShapePos = new Vector3(0f, .02f, 0f), Blend = true, FrameFrom = 3f, FrameTo = 12f, Colour = InkFade(.5f), Grow = Curve(0f, .55f, .35f, 1f, 1f, 1.1f) });
                Stain(t, pool, wash, .2f);
                // the rock: upright earth-heave cels on the spot, one random cel each, overlapping so the hump never blinks
                Layer(t, new L { Name = "Hump", Mat = SoakVariant(Bleed(m + "hump", "earth_heave", C(e.Ink, .95f), 0f, .9f)), World = false, Mode = 2, Rate = 3.2f, Max = 10,
                    Life = new Vector2(2.4f, 2.9f), Size = new Vector2(1.3f, 1.5f), Pivot = new Vector3(0f, .42f, 0f), SpinRandom = false,
                    Shape = ParticleSystemShapeType.Circle, Radius = .18f, Thickness = 1f, ShapeRot = new Vector3(-90f, 0f, 0f), Blend = true, RandomStart = true, RandomCell = true, FrameFrom = 6f, FrameTo = 10f,
                    Colour = G((0f, C(dark, 0f)), (.2f, C(dark, .85f)), (.75f, C(dark, .8f)), (1f, C(dark, 0f))), Grow = Curve(0f, .9f, .2f, 1f, 1f, 1.02f) });
                // the skirt: earth-dust ink lying flat around the foot of the rock
                Mist(t, Bleed(m + "dust", "earth_dust", C(e.Ink, .85f), 0f, .7f, 0f, .3f), Color.Lerp(e.Pig, e.Ink, .62f), .22f, new[] { .03f });
                // a broken rock: one ink splat, once
                var brk = new GameObject("Break").transform; brk.SetParent(t, false);
                Layer(brk, new L { Name = "Splat", Mat = e.Pool, World = false, Mode = 1, Burst = 1, Max = 1, Life = new Vector2(.6f, .7f), Size = new Vector2(1.8f, 2.1f),
                    NoShape = true, ShapePos = new Vector3(0f, .035f, 0f), Blend = true, FrameFrom = 0f, FrameTo = 12f, Colour = G((0f, C(e.Ink, .85f)), (.4f, C(dark, .6f)), (1f, C(dark, 0f))), Grow = Curve(0f, .6f, .3f, 1f, 1f, 1.1f) });

                dryFade = 1.2f;
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) dryFade = Mathf.Max(dryFade, ps.main.startLifetime.constantMax);
                var body = root.AddComponent<Vfx308ZoneBody>();
                body.LateAfter = 3f; body.LateAlpha = .35f; body.DryFade = dryFade;
                foreach (var x in root.GetComponentsInChildren<Transform>(true)) x.gameObject.layer = AfterFogLayer;
                return PrefabUtility.SaveAsPrefabAsset(root, RockPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
