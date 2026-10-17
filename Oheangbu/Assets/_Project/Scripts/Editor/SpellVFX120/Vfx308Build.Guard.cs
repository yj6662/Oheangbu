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

        const float GuardHeight = 2.3f;          // 11.28 the membrane's height (m), its width ~1.3x (TEST)
        const float GuardClockPreview = 4f, GuardWindowPreview = .5f;   // CombatConfig defaults; Play sets the received clocks
        const float GuardPigment = .22f;         // pq_guard v2: how much of the element's pigment tints the black membrane (TEST)
        const float GuardDryAlpha = .45f;        // after the parry window: eroded, see-through (the old seal's *= .45)
        const float GuardContactScale = .6f;     // the struck splat, times the seam's .22 (TEST)
        const float GuardStrokeLead = .1f;       // 11.51: the strokes stand this far in front of the body (was .25 - .45 m from the origin)
        const float GuardFootLife = 1.2f;        // 11.51: the foot stain's whole life (s), out of the guard clock
        const float GuardHoldSoakTo = .06f;      // 11.51: guard-clock layers coloured by this fraction of their life (~.24 s at 4 s)

        static Material GuardSoak(Material m)
        {
            var v = SoakVariant(m);
            v.SetFloat("_SoakTo", GuardHoldSoakTo); EditorUtility.SetDirty(v);
            return v;
        }

        static string BuildGuard()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build guard: Edit mode only");
            V2 = true; Touched.Clear(); seed = 31300;
            foreach (var d in new[] { "/Prefabs", "/Materials", "/Profiles", "/Effects" }) Directory.CreateDirectory(Root2 + d);
            AssetDatabase.Refresh();
            Sheet2("ink_throw"); Sheet2("ink_drops"); Sheet2("ink_pool");
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
                profile.NativeBodyPrefab = null;                        // census 10-15: 너 kept the old fire guard body (PF_FireGuard_031, additive) beside the membrane
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

        // 11.28 the standing membrane, its root = its foot on the ground (Vfx120Effect.Guard308 stands it there): a wet ink blot
        // stood up, three upright brush strokes drawn left to right inside 0.1 s, a short stain at its foot (11.51; the drops
        // and the splash went with the 10-15 questionnaire). "Wet*" lives the parry window (full, dark), "Hold*" the guard clock (eroded to ~.45 by
        // the bleed shader's alpha, so the attack behind it stays readable) - the runtime sets those lifetimes from the clocks
        // it received; the values here are the preview's (4 s / 0.5 s, CombatConfig defaults). No glow.
        static GameObject GuardInk(Elem e)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(GuardPrefabPath(e.Key)));
            // pq_guard v1: the soaked pool read as a flat green / red sticker - black ink with only a hint of the element (v2)
            Color c = Color.Lerp(e.Ink, e.Pig, GuardPigment);
            // 11.51 (D308-109 ink first): the parry-window body soaks over the usual fraction of its life; the guard-clock
            // layers (4 s preview, longer in play) get their own soak copies that are coloured by .06 of their life.
            var blot = SoakVariant(Bleed("M_V308d_guard_" + e.Key + "_blot", "ink_pool", C(e.Ink, .9f), 0f, .92f));
            var holdBlot = GuardSoak(Bleed("M_V308d_guard_" + e.Key + "_holdblot", "ink_pool", C(e.Ink, .9f), 0f, .92f));
            var stroke = GuardSoak(Bleed("M_V308d_guard_" + e.Key + "_sheet", "ink_throw", C(e.Ink, .9f), 0f, .95f));
            var foot = Bleed("M_V308d_guard_" + e.Key + "_foot", "ink_pool", C(e.Ink, .9f), 0f, .92f);
            Transform Host(string name, Vector3 at) { var h = new GameObject(name).transform; h.SetParent(root.transform, false); h.localPosition = at; return h; }

            var body = Host("BodyHost", new Vector3(0f, GuardHeight * .5f, 0f));
            Layer(body, new L { Name = "WetBody", Mat = blot, World = false, Burst = 1, Max = 2, Life = new Vector2(GuardWindowPreview, GuardWindowPreview), Size = new Vector2(GuardHeight, GuardHeight), Size3D = new Vector3(1.3f, 1f, 1f),
                NoShape = true, Blend = true, SpinRandom = false, FrameCurve = Curve(0f, 1f / 16f, .45f, 4.5f / 16f, 1f, 5.5f / 16f),
                Colour = G((0f, C(c, 0f)), (.05f, C(c, .85f)), (.7f, C(c, .8f)), (1f, C(c, 0f))), Grow = Curve(0f, .7f, .4f, 1f, 1f, 1.02f) });
            Layer(body, new L { Name = "HoldBody", Mat = holdBlot, World = false, Burst = 1, Max = 2, Life = new Vector2(GuardClockPreview, GuardClockPreview), Size = new Vector2(GuardHeight, GuardHeight), Size3D = new Vector3(1.3f, 1f, 1f),
                NoShape = true, Blend = true, SpinRandom = false, FrameCurve = Curve(0f, 1f / 16f, .06f, 4.5f / 16f, 1f, 6f / 16f),
                Colour = G((0f, C(c, 0f)), (.03f, C(c, GuardDryAlpha)), (.88f, C(c, GuardDryAlpha * .92f)), (1f, C(c, 0f))), Grow = Curve(0f, .7f, .06f, 1f, 1f, 1.03f) });

            for (int i = 0; i < 3; i++)
            {
                // 11.51: z -.25 put the strokes at .45 m from the effect origin (the 0.45 m camera rule's edge) - now .6 m
                var h = Host("StrokeHost" + i, new Vector3((i - 1) * GuardHeight * .42f, GuardHeight * .52f, -GuardStrokeLead));
                Layer(h, new L { Name = "HoldStroke" + i, Mat = stroke, World = false, Burst = 1, Max = 2, BurstTime = .012f + i * .04f, Life = new Vector2(GuardClockPreview, GuardClockPreview),
                    Size = new Vector2(GuardHeight * 1.1f, GuardHeight * 1.1f), NoShape = true, Blend = true, SpinRandom = false, RotDeg = new Vector2(84f + i * 4f, 88f + i * 4f),
                    FrameCurve = Curve(0f, 2f / 16f, .03f, 6f / 16f, 1f, 7.5f / 16f),
                    Colour = G((0f, C(e.Ink, 0f)), (.015f, C(e.Ink, 1f)), (.12f, C(e.Ink, 1f)), (.22f, C(e.Ink, .5f)), (.88f, C(e.Ink, .45f)), (1f, C(e.Ink, 0f))), Grow = Curve(0f, .8f, .03f, 1f, 1f, 1.02f) });
            }

            // 11.51 (D308-109 no lasting ground mark): the foot stain was "HoldFoot" - dark (.9 -> .7) for the whole guard clock.
            // Now "FootStain" (no Hold/Wet prefix, so the runtime keeps its own life): dark only around the landing, down to
            // the ward stain's .045 by 0.95 s, gone at GuardFootLife.
            float fl = GuardFootLife, peak = .1f / fl, low = .95f / fl;
            Layer(Host("FootHost", new Vector3(0f, .03f, 0f)), new L { Name = "FootStain", Mat = foot, World = false, Burst = 1, Max = 2, BurstTime = .03f, Life = new Vector2(fl, fl),
                Size = new Vector2(GuardHeight * 1.3f, GuardHeight * 1.3f), Size3D = new Vector3(1.15f, .45f, 1f), Mode = 1, NoShape = true, Blend = true, SpinRandom = false, FrameCurve = Curve(0f, 1f / 16f, low, 10f / 16f, 1f, 11f / 16f),
                Colour = G((0f, C(e.Ink, 0f)), (peak, C(e.Ink, .7f)), (low, C(e.Ink, .045f)), (1f, C(e.Ink, 0f))), Grow = Curve(0f, .6f, peak * 2f, 1f, 1f, 1.05f) });

            // user 2026-10-15 questionnaire "술식 쪽만 빼기": no drops running down the membrane, no splash
            return root;
        }
    }
}
