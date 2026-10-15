using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.Demo;
using Oheangbu.App.SpellVFX120;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // D308-84 stage 4 (SPEC-SPELL-VFX-OVERHAUL-308 11.22), summons: the ink body (D308-83b, 11.13 - 11.17) on the body the game
    // really plays - the skinned PF_<X>_Combat with its Animator and foot placement - instead of the melted static mesh
    // (which could not follow the animation). A copy of each combat prefab with every renderer's materials replaced by the
    // InkBody material (the shader already takes the summon contract _Age / _Ground / _Height that DemoSummonPresentation
    // writes each frame). The core's light stays off (D308-83b: the user's choice). connect:attack writes the live summon
    // profiles' PresentationPrefab (Architecture296 copies) once built, recording them in beast-before.json; revert restores.
    // v1: no aura strands / pool on the skinned body - later.
    //   beast          - build the five
    public static partial class Vfx308Build
    {
        static readonly (string Folder, string Key)[] Beasts = { ("WoodDeer", "deer"), ("FireHaetae", "haetae"), ("DokkaebiClub", "dokkaebi"), ("MetalTiger", "tiger"), ("WaterTurtle", "turtle") };

        static string BeastPrefabPath(string key) => Root2 + "/Prefabs/PF_V308g_summon_" + key + "_combat.prefab";
        static string BeastRecord => Path.Combine(Path.GetDirectoryName(Record), "beast-before.json");

        [Serializable] sealed class BeastRecorded { public List<string> profiles = new List<string>(); public List<string> before = new List<string>(); }

        static string BuildBeast()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build beast: Edit mode only");
            V2 = true; Touched.Clear();
            foreach (var d in new[] { "/Prefabs", "/Materials" }) Directory.CreateDirectory(Root2 + d);
            var sb = new StringBuilder();
            foreach (var b in Beasts)
            {
                var spec = Summons.First(x => x.Key == b.Key);
                string src = "Assets/_Project/Art/Demo/Summons/" + b.Folder + "/PF_" + b.Folder + "_Combat.prefab";
                if (!File.Exists(src)) { sb.AppendLine(b.Key + ": " + src + " missing"); continue; }
                if (!File.Exists(BeastPrefabPath(b.Key)) && !AssetDatabase.CopyAsset(src, BeastPrefabPath(b.Key))) throw new Exception("copy failed " + src);
                var profile = Load<Vfx120Profile>("Assets/_Project/Art/SpellVFX120/Profiles/" + spec.Profile + ".asset");
                Color.RGBToHSV(profile.Pigment, out float hue, out float sat, out float val);
                Color core = Color.HSVToRGB(hue, Mathf.Min(1f, sat * 1.25f), Mathf.Min(.26f, val * .7f));
                var contents = PrefabUtility.LoadPrefabContents(BeastPrefabPath(b.Key));
                int swapped = 0;
                try
                {
                    var renderers = contents.GetComponentsInChildren<Renderer>(true).Where(r => r is SkinnedMeshRenderer || r is MeshRenderer).ToArray();
                    var main = renderers.OrderByDescending(r => r.bounds.size.sqrMagnitude).FirstOrDefault();
                    if (main == null) { sb.AppendLine(b.Key + ": no renderer"); continue; }
                    var lb = main is SkinnedMeshRenderer smr ? smr.localBounds : main.GetComponent<MeshFilter>().sharedMesh.bounds;
                    Vector3 chest = new Vector3(lb.center.x, lb.min.y + lb.size.y * spec.Chest.x, lb.center.z + lb.size.z * spec.Chest.y);
                    var ink = InkBody("M_V308g_summon_" + b.Key + "_body", core, spec.AccentFrom, chest, lb.size.y * spec.CoreRadius, WoodDeerVfx.ReviewLife);
                    foreach (var r in renderers)
                    {
                        r.sharedMaterials = Enumerable.Repeat(ink, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();
                        r.gameObject.layer = AfterFogLayer;   // the body is drawn after the realm fog, as its strands are (11.16)
                        swapped++;
                    }
                    PrefabUtility.SaveAsPrefabAsset(contents, BeastPrefabPath(b.Key));
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                sb.AppendLine(b.Key + " -> " + BeastPrefabPath(b.Key) + " (" + swapped + " renderers to the ink body)");
            }
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            return sb.ToString();
        }

        static List<SummonCombatProfile> LiveBeasts()
        {
            const string dir = "Assets/_Project/Art/World/Architecture296/Data";
            return AssetDatabase.FindAssets("t:SummonCombatProfile", new[] { dir }).Select(g => AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(AssetDatabase.GUIDToAssetPath(g))).Where(x => x != null).ToList();
        }

        static string ConnectBeast()
        {
            if (Beasts.Any(b => !File.Exists(BeastPrefabPath(b.Key)))) return "beast: not built (run beast) - left as it is";
            var live = LiveBeasts(); var sb = new StringBuilder("beast:");
            if (!File.Exists(BeastRecord))
            {
                var rec = new BeastRecorded();
                foreach (var p in live) { rec.profiles.Add(AssetDatabase.GetAssetPath(p)); rec.before.Add(AssetDatabase.GetAssetPath(p.PresentationPrefab)); }
                File.WriteAllText(BeastRecord, JsonUtility.ToJson(rec, true));
            }
            foreach (var p in live)
            {
                var b = Beasts.FirstOrDefault(x => AssetDatabase.GetAssetPath(p).EndsWith("_" + x.Folder + ".asset", StringComparison.Ordinal));
                if (b.Key == null) continue;
                p.PresentationPrefab = Load<GameObject>(BeastPrefabPath(b.Key));
                EditorUtility.SetDirty(p); AssetDatabase.SaveAssetIfDirty(p);
                sb.Append(' ').Append(b.Folder);
            }
            return sb.Append(" -> #308 ink bodies").ToString();
        }

        static string RevertBeast()
        {
            if (!File.Exists(BeastRecord)) return "beast: nothing recorded";
            var rec = JsonUtility.FromJson<BeastRecorded>(File.ReadAllText(BeastRecord));
            for (int i = 0; i < rec.profiles.Count; i++)
            {
                var p = AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(rec.profiles[i]);
                if (p == null) continue;
                p.PresentationPrefab = string.IsNullOrEmpty(rec.before[i]) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(rec.before[i]);
                EditorUtility.SetDirty(p); AssetDatabase.SaveAssetIfDirty(p);
            }
            File.Delete(BeastRecord);
            return "beast: restored " + rec.profiles.Count + " summon presentations";
        }
    }
}
