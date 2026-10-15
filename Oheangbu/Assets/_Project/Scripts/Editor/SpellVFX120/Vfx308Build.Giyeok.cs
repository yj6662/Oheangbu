using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Combat;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // D308-84 stage 4 (SPEC-SPELL-VFX-OVERHAUL-308 11.22), the ㄱ-final attacks 각 낙 삭 악. EAGiyeokRuntime instantiates the EA
    // profile's Root / Burn / Pierce / Return prefab and drives it like a bolt (SetImpactClock(flight) + Begin), so the #308
    // single bolt fits as it is: per letter a #308 copy of its Vfx120 profile with the element's bolt (PF_V308b_<key>) and a
    // copy of its effect prefab. v1 = the plain element bolt for all four (the root / scorch / pierce / return variations of
    // the plan are later). connect:attack writes the four prefab fields of the live EAGiyeok profile (Architecture296 copy)
    // once built, recording what was there in giyeok-before.json; revert restores them.
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
            var sb = new StringBuilder();
            foreach (var g in Giyeoks)
            {
                var spell = Spells.First(x => x.Key == g.Key);
                var bolt = Load<GameObject>(PrefabPath(spell));
                if (bolt == null) { sb.AppendLine(g.Glyph + ": the element bolt is not built (run build first)"); continue; }
                string srcProfile = "Assets/_Project/Art/SpellVFX120/Profiles/" + g.Id + ".asset";
                if (!File.Exists(GiyeokProfilePath(g.Id)) && !AssetDatabase.CopyAsset(srcProfile, GiyeokProfilePath(g.Id))) throw new Exception("copy failed " + srcProfile);
                var profile = Load<Vfx120Profile>(GiyeokProfilePath(g.Id));
                profile.Bolt300Prefab = bolt; profile.Bolt300Arc = spell.Arc; profile.Bolt300Curve = spell.Curve; profile.Bolt300Spin = spell.Spin;
                profile.Bolt300Scale = 1f; profile.Bolt300Ease = 1f; profile.Bolt300SoakSeconds = BodySoakSeconds; profile.Bolt300NearContactMeters = NearContactMeters; profile.Bolt300TravelScale = TravelScale;
                profile.AreaRemake = Vfx120AreaRemake.None;
                profile.BodyMesh = Element(kit, g.Key).Bit; profile.NativeBodyPrefab = null;   // the old bespoke bodies stay out (as for the attack singles)
                profile.KtpContactBrightness = 1f;
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
