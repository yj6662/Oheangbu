using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Combat;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // D308-84 stage 4 (SPEC-SPELL-VFX-OVERHAUL-308 11.22), the ㄱ-final attacks 각 낙 삭 악. EAGiyeokRuntime instantiates the EA
    // profile's Root / Burn / Pierce / Return prefab and drives it like a bolt (SetImpactClock(flight) + Begin): per letter a
    // #308 copy of its Vfx120 profile and a copy of its effect prefab. connect:attack writes the four prefab fields of the live
    // EAGiyeok profile (Architecture296 copy) once built, recording what was there in giyeok-before.json; revert restores them.
    // 11.45: each letter has its own ink body (PF_V308j_giyeok_<key>) instead of the element bolt with the FinalK marker (its
    // stakes and lingering pool broke the no-ground-mark rule), and the profile flags pick the runtime variation:
    //   각 - roots: ink threads rise round the held target's legs for the hold (Bolt300FollowTarget, height 0)
    //   낙 - scorch: soot breathes off the burning target's body for the burn (Bolt300FollowTarget, height 1)
    //   삭 - pierce: a thin dry needle flies its whole range through every hit, no hit body (Bolt300Pierce)
    //   악 - return: the wet orb hits, bounces off and flies back into the target (Bolt300Return)
    // No ground mark, no flying bits, no light past the 2-cel birth; ink first, the pigment soaks in (D308-109).
    //   giyeok         - build the four
    public static partial class Vfx308Build
    {
        static readonly (string Glyph, string Id, string Key, string Field)[] Giyeoks =
        {
            ("각", "002_AC01", "ga", "RootPrefab"), ("낙", "026_B099", "na", "BurnPrefab"), ("삭", "074_C0AD", "sa", "PiercePrefab"), ("악", "098_C545", "a", "ReturnPrefab"),
        };

        static string GiyeokProfilePath(string id) => Root2 + "/Profiles/" + id + "_308e.asset";
        static string GiyeokEffectPath(string id) => Root2 + "/Effects/" + id + "_308e.prefab";
        static string GiyeokRecord => Path.Combine(Path.GetDirectoryName(Record), "giyeok-before.json");

        [Serializable] sealed class GiyeokRecorded { public List<string> fields = new List<string>(); public List<string> before = new List<string>(); }

        static string BuildGiyeok()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build giyeok: Edit mode only");
            V2 = true; Touched.Clear();
            foreach (var d in new[] { "/Profiles", "/Effects" }) Directory.CreateDirectory(Root2 + d);
            var kit = new Kit { Thorn = Model("thorn"), Splinter = Model("splinter"), Clod = Model("clod"), Chunk = Model("chunk"), Blade = Model("blade"), Stalk = Model("stalk"), Drop = Model("drop"), Tongue = Model("tongue") };
            var sb = new StringBuilder(); seed = 45400;
            foreach (var g in Giyeoks)
            {
                var spell = Spells.First(x => x.Key == g.Key);
                var elem = Element(kit, g.Key); kit.Ink = elem.Ink;
                var bolt = GiyeokBody(elem);   // 11.45: its own body (no FinalK marker)
                string srcProfile = "Assets/_Project/Art/SpellVFX120/Profiles/" + g.Id + ".asset";
                if (!File.Exists(GiyeokProfilePath(g.Id)) && !AssetDatabase.CopyAsset(srcProfile, GiyeokProfilePath(g.Id))) throw new Exception("copy failed " + srcProfile);
                var profile = Load<Vfx120Profile>(GiyeokProfilePath(g.Id));
                profile.Bolt300Prefab = bolt; profile.Bolt300Arc = spell.Arc; profile.Bolt300Curve = spell.Curve; profile.Bolt300Spin = spell.Spin;
                profile.Bolt300Pierce = g.Key == "sa"; profile.Bolt300Return = g.Key == "a";
                profile.Bolt300FollowTarget = g.Key is "ga" or "na"; profile.Bolt300FollowHeight = g.Key == "na" ? 1f : 0f;
                profile.Bolt300Scale = 1f; profile.Bolt300Ease = 1f; profile.Bolt300SoakSeconds = BodySoakSeconds; profile.Bolt300NearContactMeters = NearContactMeters; profile.Bolt300TravelScale = TravelScale;
                profile.AreaRemake = Vfx120AreaRemake.None;
                profile.BodyMesh = Element(kit, g.Key).Bit; profile.NativeBodyPrefab = null;   // the old bespoke bodies stay out (as for the attack singles)
                profile.KtpContactBrightness = 1f;
                profile.BotanicalPrefab = null; profile.NativeCastPrefab = null;   // sw_z: the old root ring (각) and the cast flakes (삭) stayed
                EditorUtility.SetDirty(profile); Touched.Add(profile);

                string srcEffect = "Assets/_Project/Art/SpellVFX120/Prefabs/" + g.Id + ".prefab";
                if (!File.Exists(GiyeokEffectPath(g.Id)) && !AssetDatabase.CopyAsset(srcEffect, GiyeokEffectPath(g.Id))) throw new Exception("copy failed " + srcEffect);
                var contents = PrefabUtility.LoadPrefabContents(GiyeokEffectPath(g.Id));
                try
                {
                    var fx = contents.GetComponentInChildren<Vfx120Effect>(true) ?? throw new Exception("no Vfx120Effect in " + GiyeokEffectPath(g.Id));
                    fx.Profile = profile;
                    PrefabUtility.SaveAsPrefabAsset(contents, GiyeokEffectPath(g.Id));
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                sb.AppendLine(g.Glyph + " (" + g.Field + ") -> " + GiyeokEffectPath(g.Id) + " bolt " + bolt.name);
            }
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            return sb.ToString();
        }

        static string GiyeokBodyPath(string key) => Root2 + "/Prefabs/PF_V308j_giyeok_" + key + ".prefab";

        // 11.45: one ink body per letter. The flight is the attack singles' ink mass (no shed - its world-space drops lingered),
        // the birth is the 2-cel glow, the line of the flight the ink trail. The Contact is what the profile flag makes of it.
        static GameObject GiyeokBody(Elem e)
        {
            Directory.CreateDirectory(Root2 + "/Prefabs");
            var (travel, contact, root) = Shell("PF_V308j_giyeok_" + e.Key);
            try
            {
                switch (e.Key)
                {
                    case "ga":   // roots: upright ink threads rise round the held target's feet for the hold (contact follows at height 0)
                        InkMass(travel, "gk_ga", e.Ink, e.Wash, e.Dark, 1.0f, rate: 26f, spin: 60f, shed: 0f);
                        Birth2(travel, "gk_ga", e.Pig, 1.0f);
                        Layer(contact, new L { Name = "Roots", Mat = e.Stroke, World = false, Mode = 2, Rate = 10f, Max = 12, Life = new Vector2(.6f, .8f), Size = new Vector2(.9f, 1.3f),
                            Shape = ParticleSystemShapeType.Circle, Radius = .45f, Thickness = 0f, ShapeRot = new Vector3(-90f, 0f, 0f), Pivot = new Vector3(0f, .4f, 0f), RiseY = .6f, SpinRandom = false, Blend = true,
                            FrameFrom = 0f, FrameTo = 12f, Colour = InkFirst(e.Ink, e.Wash, e.Dark), Grow = Curve(0f, .6f, 1f, 1.1f) });
                        Wash(contact, "Splat", B("ga", "ink_splat", C(e.Ink, .9f), 0f, .85f), e.Dark, e.Dark, 3f, .9f, .35f, 1.2f, 15.5f, new Vector3(0f, .9f, 0f), true);
                        break;
                    case "na":   // scorch: the burning core in flight; soot breathes off the target's body for the burn (contact follows at height 1)
                        var naSpin = InkMass(travel, "gk_na", e.Ink, e.Wash, e.Dark, .9f, rate: 22f, spin: 50f, shed: 0f);
                        Layer(naSpin, new L { Name = "Flame", Mat = e.Flame, World = false, Rate = 10f, Max = 5, Life = new Vector2(2f * Cel, 2f * Cel), Size = new Vector2(1.1f, 1.3f), NoShape = true, SpinRandom = false, Blend = true,
                            RandomStart = true, FrameFrom = 0f, FrameTo = 8f, Pivot = new Vector3(0f, .2f, 0f), Colour = InkFirst(e.Ink, e.Wash, e.Dark) });
                        Birth2(travel, "gk_na", e.Wash, 1.0f);
                        Layer(contact, new L { Name = "Soot", Mat = e.LickSoot, World = false, Mode = 2, Rate = 8f, Max = 10, Life = new Vector2(.6f, .9f), Size = new Vector2(.8f, 1.1f),
                            Shape = ParticleSystemShapeType.Circle, Radius = .3f, Thickness = 0f, ShapeRot = new Vector3(-90f, 0f, 0f), RiseY = .5f, SpinRandom = false, Blend = true,
                            RandomStart = true, FrameFrom = 0f, FrameTo = 8f, Colour = InkFirst(e.Ink, e.Dark, e.Dark), Grow = Curve(0f, .7f, 1f, 1.15f) });
                        break;
                    case "sa":   // pierce: a thin dry needle with a metal glint at its tip; no Contact (a hit does not stop it)
                        InkMass(travel, "gk_sa", e.Ink, e.Wash, e.Dark, .75f, rate: 26f, spin: 0f, life: .16f, shed: 0f);
                        Layer(travel, new L { Name = "Glint", Mat = B("sa", "metal_streak", C(e.Ink, .8f)), World = false, Rate = 12f, Max = 4, Life = new Vector2(Cel, Cel), Size = new Vector2(2.2f, 2.6f), NoShape = true, Blend = true,
                            RandomCell = true, FrameFrom = 0f, FrameTo = 1.99f, Colour = G((0f, C(e.Wash, 1f)), (1f, C(e.Wash, .8f))) });
                        Birth2(travel, "gk_sa", e.Wash, 1.0f);
                        break;
                    default:     // return: the wet orb hits with a crown and flies back into the target (the body stays)
                        var aSpin = InkMass(travel, "gk_a", e.Ink, e.Wash, e.Dark, .95f, rate: 24f, spin: 25f, shed: 0f);
                        Layer(aSpin, new L { Name = "Orb", Mat = Bleed("M_V308b_a_water_orb_veil", "water_orb", C(Color.Lerp(e.Pig, e.Ink, .7f), .3f), 0f, .6f), World = false, Burst = 1, Max = 2, Life = new Vector2(8f, 8f), Size = new Vector2(2.2f, 2.2f), NoShape = true, SpinRandom = false, Blend = true,
                            Cycles = 6f, Colour = G((0f, C(e.Wash, 1f)), (1f, C(e.Wash, 1f))), Grow = Curve(0f, .3f, .05f, 1f, 1f, 1f) });
                        Birth2(travel, "gk_a", e.Wash, 1.0f);
                        Wash(contact, "Crown", B("a", "water_ring", C(Color.Lerp(e.Pig, e.Ink, .7f), .9f)), e.Wash, e.Dark, 3.5f, .9f, .5f, 1.12f, depth: 2);
                        break;
                }
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = AfterFogLayer;
                Bolt308Extras(root);
                InkTrail(travel, "gk_" + e.Key, e.Ink, e.Dark, e.Key == "sa" ? .2f : .3f);
                return PrefabUtility.SaveAsPrefabAsset(root, GiyeokBodyPath(e.Key));
            }
            finally { Object.DestroyImmediate(root); }
        }

        static EAGiyeokProfileSO LiveGiyeok()
        {
            const string dir = "Assets/_Project/Art/World/Architecture296/Data";
            var file = Directory.Exists(dir) ? Directory.GetFiles(dir, "*EAGiyeok_TEST.asset").FirstOrDefault() : null;
            return file == null ? null : AssetDatabase.LoadAssetAtPath<EAGiyeokProfileSO>(file.Replace('\\', '/'));
        }

        static string ConnectGiyeok()
        {
            var live = LiveGiyeok();
            if (live == null) return "giyeok: live EAGiyeok profile not found";
            if (Giyeoks.Any(g => !File.Exists(GiyeokEffectPath(g.Id)))) return "giyeok: not built (run giyeok) - left as it is";
            var so = new SerializedObject(live);
            if (!File.Exists(GiyeokRecord))
            {
                var rec = new GiyeokRecorded();
                foreach (var g in Giyeoks) { rec.fields.Add(g.Field); rec.before.Add(AssetDatabase.GetAssetPath(so.FindProperty(g.Field).objectReferenceValue)); }
                File.WriteAllText(GiyeokRecord, JsonUtility.ToJson(rec, true));
            }
            foreach (var g in Giyeoks) so.FindProperty(g.Field).objectReferenceValue = Load<GameObject>(GiyeokEffectPath(g.Id));
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(live); AssetDatabase.SaveAssetIfDirty(live);
            return "giyeok: 각 낙 삭 악 -> #308 bolts (" + AssetDatabase.GetAssetPath(live) + ")";
        }

        static string RevertGiyeok()
        {
            if (!File.Exists(GiyeokRecord)) return "giyeok: nothing recorded";
            var live = LiveGiyeok();
            if (live == null) return "giyeok: live EAGiyeok profile not found (record kept)";
            var rec = JsonUtility.FromJson<GiyeokRecorded>(File.ReadAllText(GiyeokRecord));
            var so = new SerializedObject(live);
            for (int i = 0; i < rec.fields.Count; i++)
                so.FindProperty(rec.fields[i]).objectReferenceValue = string.IsNullOrEmpty(rec.before[i]) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(rec.before[i]);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(live); AssetDatabase.SaveAssetIfDirty(live);
            File.Delete(GiyeokRecord);
            return "giyeok: restored " + rec.fields.Count + " prefab fields";
        }
    }
}
