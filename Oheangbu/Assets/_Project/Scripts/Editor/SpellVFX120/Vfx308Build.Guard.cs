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
    // D308-84 stage 4 (SPEC-SPELL-VFX-OVERHAUL-308 11.22), guard first: "one stroke of ink stands up as a membrane, and ink
    // splashes when it is struck". Per element a membrane prefab (PF_V308d_guard_<key>), a #308 copy of the guard letter's
    // profile (Guard308Prefab set, the pattern shield's NativeFieldPrefab cleared, the guard contact = the element's #308 ink
    // splat) and a copy of its effect prefab. connect:attack also connects these once built; revert restores them with the rest.
    //   guard          - build the five
    public static partial class Vfx308Build
    {
        static readonly (string Glyph, string Id, string Key)[] Guards =
        {
            ("거", "007_AC70", "ga"), ("너", "031_B108", "na"), ("머", "055_BA38", "ma"), ("서", "079_C11C", "sa"), ("어", "103_C5B4", "a"),
        };

        static string GuardPrefabPath(string key) => Root2 + "/Prefabs/PF_V308d_guard_" + key + ".prefab";
        static string GuardProfilePath(string id) => Root2 + "/Profiles/" + id + "_308d.asset";
        static string GuardEffectPath(string id) => Root2 + "/Effects/" + id + "_308d.prefab";

        const float GuardSheetLife = .8f;        // the membrane's own life; the guard clock ends it sooner when shorter (TEST)
        const float GuardContactScale = .6f;     // the struck splat, times the seam's .22 (TEST)

        static string BuildGuard()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build guard: Edit mode only");
            V2 = true; Touched.Clear(); seed = 31300;
            foreach (var d in new[] { "/Prefabs", "/Materials", "/Profiles", "/Effects" }) Directory.CreateDirectory(Root2 + d);
            AssetDatabase.Refresh();
            Sheet2("ink_throw"); Sheet2("ink_drops");
            var kit = new Kit { Thorn = Model("thorn"), Splinter = Model("splinter"), Clod = Model("clod"), Chunk = Model("chunk"), Blade = Model("blade"), Stalk = Model("stalk"), Drop = Model("drop"), Tongue = Model("tongue") };
            var sb = new StringBuilder();
            foreach (var g in Guards)
            {
                var e = Element(kit, g.Key);
                var root = GuardInk(e);
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = AfterFogLayer;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, GuardPrefabPath(g.Key));
                Object.DestroyImmediate(root);

                string srcProfile = "Assets/_Project/Art/SpellVFX120/Profiles/" + g.Id + ".asset";
                if (!File.Exists(GuardProfilePath(g.Id)) && !AssetDatabase.CopyAsset(srcProfile, GuardProfilePath(g.Id))) throw new Exception("copy failed " + srcProfile);
                var profile = Load<Vfx120Profile>(GuardProfilePath(g.Id));
                profile.Guard308Prefab = prefab;
                profile.NativeFieldPrefab = null;                       // no pattern shield (KtpPatternShield stays: no bamboo guard either)
                var hit = AssetDatabase.LoadAssetAtPath<GameObject>(HitPath(g.Key));
                if (hit != null) profile.GuardContactPrefab = hit;
                profile.KtpContactMultiplier = GuardContactScale / .22f; profile.KtpContactBrightness = 1f;   // ink, not light
                EditorUtility.SetDirty(profile); Touched.Add(profile);

                string srcEffect = "Assets/_Project/Art/SpellVFX120/Prefabs/" + g.Id + ".prefab";
                if (!File.Exists(GuardEffectPath(g.Id)) && !AssetDatabase.CopyAsset(srcEffect, GuardEffectPath(g.Id))) throw new Exception("copy failed " + srcEffect);
                var contents = PrefabUtility.LoadPrefabContents(GuardEffectPath(g.Id));
                try
                {
                    var fx = contents.GetComponentInChildren<Vfx120Effect>(true) ?? throw new Exception("no Vfx120Effect in " + GuardEffectPath(g.Id));
                    fx.Profile = profile;
                    PrefabUtility.SaveAsPrefabAsset(contents, GuardEffectPath(g.Id));
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                sb.AppendLine(g.Glyph + " -> " + GuardPrefabPath(g.Key) + " | profile " + GuardProfilePath(g.Id) + " | contact " + (hit != null ? hit.name : "(attack hit not built - kept the old)"));
            }
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            return sb.ToString();
        }

        // the membrane: a wide brush stroke drawn up in 0.1 s (the thrown-stroke sheet, black first, the pigment soaking in),
        // ink running off its lower edge. Local to the effect, which faces the camera.
        static GameObject GuardInk(Elem e)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(GuardPrefabPath(e.Key)));
            Color c = Color.Lerp(e.Pig, e.Ink, .5f);
            var sheet = SoakVariant(Bleed("M_V308d_guard_" + e.Key + "_sheet", "ink_throw", C(e.Ink, .9f), 0f, .95f));
            Layer(root.transform, new L { Name = "Sheet", Mat = sheet, World = false, Burst = 1, Max = 2, Life = new Vector2(GuardSheetLife, GuardSheetLife), Size = new Vector2(1.6f, 1.6f), Size3D = new Vector3(1.6f, 1f, 1f),
                NoShape = true, Blend = true, SpinRandom = false, FrameFrom = 0f, FrameTo = 15.5f,
                Colour = G((0f, C(c, 0f)), (.08f, C(c, 1f)), (.7f, C(c, .9f)), (1f, C(c, 0f))), Grow = Curve(0f, .85f, .12f, 1f, 1f, 1.04f) });
            var edgeHost = new GameObject("EdgeHost").transform; edgeHost.SetParent(root.transform, false); edgeHost.localPosition = new Vector3(0f, -.45f, 0f);
            Layer(edgeHost, new L { Name = "Edge", Mat = sheet, World = false, Burst = 1, Max = 2, Life = new Vector2(GuardSheetLife * .85f, GuardSheetLife * .85f), Size = new Vector2(1.2f, 1.2f), Size3D = new Vector3(1.9f, .45f, 1f),
                NoShape = true, Blend = true, SpinRandom = false, FrameFrom = 2f, FrameTo = 15.5f, BurstTime = .04f,
                Colour = G((0f, C(e.Ink, 0f)), (.1f, C(e.Ink, .95f)), (.7f, C(e.Ink, .8f)), (1f, C(e.Ink, 0f))), Grow = Curve(0f, .9f, .15f, 1f, 1f, 1.05f) });
            Layer(root.transform, new L { Name = "Drip", Mat = Bleed("M_V308d_guard_drops", "ink_drops", C(SwingInkColour, .9f), 0f, .9f), World = false, Burst = 14, BurstTime = .1f, Max = 16, Life = new Vector2(.45f, .75f),
                Size = new Vector2(.08f, .18f), Gravity = 1.6f, Shape = ParticleSystemShapeType.Box, ShapeScale = new Vector3(2.2f, .05f, .1f), ShapePos = new Vector3(0f, -.7f, 0f),
                RandomCell = true, Blend = true, Colour = InkFade(.5f), Grow = Curve(0f, .6f, 1f, 1.3f) });
            return root;
        }
    }
}
