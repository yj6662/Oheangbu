using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.74 (user answer "겉만 먹으로"): the 국 lift and the 뭄 bridge keep their meshes, colliders,
    // sizes and clocks; only the material of what is seen becomes an InkSolid308 ink body (as 11.47 did for the deer roots).
    //   국  Vfx120Profile.BodyMaterial (trunk + deck) and InkMaterial (the three leaves); the PatternMaterial foot mark stays
    //   뭄  MumBridgeProfileSO.BlockMaterial (the rough stone blocks - the only path drawn while BlockLength > 0)
    // The profiles changed are the ones the main world points at (W_Demo_Main's DemoWoodLiftProfile / MumBridgeProfile,
    // read from the scene text by guid - the scene is never opened or saved): today the Architecture296 copies. The
    // SpellVFX120 / Watershed295 originals are not touched.
    //   fieldskin          - build the three materials
    //   fieldskin:connect  - record what is there (fieldskin-before.json) and put the ink materials on
    //   fieldskin:revert   - put back what was recorded (also run by revert)
    public static partial class Vfx308Build
    {
        public const string FieldSkinScene = "Assets/_Project/Scenes/World/W_Demo_Main.unity";
        public static string LiftBodyInkPath => Root + "/Materials/M_V308_ga_lift_body.mat";
        public static string LiftLeafInkPath => Root + "/Materials/M_V308_ga_lift_leaf.mat";
        public static string BankBlockInkPath => Root + "/Materials/M_V308_ma_bank_block.mat";
        static string FieldSkinRecord => Path.Combine(Path.GetDirectoryName(Record), "fieldskin-before.json");

        [Serializable] sealed class FieldSkinRecorded { public string lift, body, leaf, pattern, bridge, block; }

        static string BuildFieldSkin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build fieldskin: Edit mode only");
            V2 = true; Touched.Clear();
            var ga = Load<Vfx120Profile>("Assets/_Project/Art/SpellVFX120/Profiles/" + Spells.First(x => x.Key == "ga").Effect + ".asset");
            var ma = Load<Vfx120Profile>("Assets/_Project/Art/SpellVFX120/Profiles/" + Spells.First(x => x.Key == "ma").Effect + ".asset");
            var body = Solid("M_V308_ga_lift_body", Color.Lerp(ga.Pigment, ga.Ink, .3f), ga.Ink);
            var leaf = Solid("M_V308_ga_lift_leaf", Color.Lerp(ga.Pigment, ga.Ink, .55f), ga.Ink, .5f, .3f);
            var block = Solid("M_V308_ma_bank_block", Color.Lerp(ma.Pigment, ma.Ink, .2f), ma.Ink, .6f, .3f);
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            return "fieldskin -> " + string.Join(", ", new[] { body, leaf, block }.Select(AssetDatabase.GetAssetPath)) + " (InkSolid308)";
        }

        // The two profiles the main world points at, by the guid in the scene text. Null when the field is not found.
        public static (Vfx120Profile lift, MumBridgeProfileSO bridge) FieldSkinTargets()
        {
            string lift = null, bridge = null;
            var field = new Regex(@"^\s*(DemoWoodLiftProfile|MumBridgeProfile): \{fileID: \d+, guid: ([0-9a-f]{32})", RegexOptions.CultureInvariant);
            foreach (var line in File.ReadLines(FieldSkinScene))
            {
                if (line.IndexOf("Profile: {", StringComparison.Ordinal) < 0) continue;
                var m = field.Match(line);
                if (!m.Success) continue;
                if (m.Groups[1].Value == "DemoWoodLiftProfile") lift ??= m.Groups[2].Value; else bridge ??= m.Groups[2].Value;
                if (lift != null && bridge != null) break;
            }
            return (lift == null ? null : AssetDatabase.LoadAssetAtPath<Vfx120Profile>(AssetDatabase.GUIDToAssetPath(lift)),
                    bridge == null ? null : AssetDatabase.LoadAssetAtPath<MumBridgeProfileSO>(AssetDatabase.GUIDToAssetPath(bridge)));
        }

        static string ConnectFieldSkin()
        {
            var body = AssetDatabase.LoadAssetAtPath<Material>(LiftBodyInkPath);
            var leaf = AssetDatabase.LoadAssetAtPath<Material>(LiftLeafInkPath);
            var block = AssetDatabase.LoadAssetAtPath<Material>(BankBlockInkPath);
            if (body == null || leaf == null || block == null) return "fieldskin: not built (run fieldskin) - left as it is";
            var (lift, bridge) = FieldSkinTargets();
            if (lift == null || bridge == null) return "fieldskin: " + FieldSkinScene + " lift " + (lift != null) + " bridge " + (bridge != null) + " - left as it is";
            if (!Vfx120Effect.IsWoodLift(lift)) return "fieldskin: " + AssetDatabase.GetAssetPath(lift) + " is not a WoodLift profile - left as it is";
            if (!File.Exists(FieldSkinRecord))
            {
                var rec = new FieldSkinRecorded
                {
                    lift = AssetDatabase.GetAssetPath(lift), body = AssetDatabase.GetAssetPath(lift.BodyMaterial), leaf = AssetDatabase.GetAssetPath(lift.InkMaterial),
                    pattern = AssetDatabase.GetAssetPath(lift.PatternMaterial), bridge = AssetDatabase.GetAssetPath(bridge), block = AssetDatabase.GetAssetPath(bridge.BlockMaterial),
                };
                File.WriteAllText(FieldSkinRecord, JsonUtility.ToJson(rec, true));
            }
            lift.BodyMaterial = body; lift.InkMaterial = leaf;
            bridge.BlockMaterial = block;
            EditorUtility.SetDirty(lift); AssetDatabase.SaveAssetIfDirty(lift);
            EditorUtility.SetDirty(bridge); AssetDatabase.SaveAssetIfDirty(bridge);
            return "fieldskin: 국 " + AssetDatabase.GetAssetPath(lift) + " body/leaf -> ink, 뭄 " + AssetDatabase.GetAssetPath(bridge) + " block -> ink";
        }

        static string RevertFieldSkin()
        {
            if (!File.Exists(FieldSkinRecord)) return "fieldskin: nothing recorded";
            var rec = JsonUtility.FromJson<FieldSkinRecorded>(File.ReadAllText(FieldSkinRecord));
            Material M(string path) => string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Material>(path);
            var lift = AssetDatabase.LoadAssetAtPath<Vfx120Profile>(rec.lift);
            var bridge = AssetDatabase.LoadAssetAtPath<MumBridgeProfileSO>(rec.bridge);
            var done = new List<string>();
            if (lift != null) { lift.BodyMaterial = M(rec.body); lift.InkMaterial = M(rec.leaf); EditorUtility.SetDirty(lift); AssetDatabase.SaveAssetIfDirty(lift); done.Add("국"); }
            if (bridge != null) { bridge.BlockMaterial = M(rec.block); EditorUtility.SetDirty(bridge); AssetDatabase.SaveAssetIfDirty(bridge); done.Add("뭄"); }
            File.Delete(FieldSkinRecord);
            return "fieldskin: restored " + (done.Count == 0 ? "nothing (profiles gone)" : string.Join(" ", done));
        }

        // Read only, for deploy308-unit D59: what the recorded state says the lift's untouched parts were.
        public static string FieldSkinRecordedPattern()
        {
            if (!File.Exists(FieldSkinRecord)) return null;
            return JsonUtility.FromJson<FieldSkinRecorded>(File.ReadAllText(FieldSkinRecord)).pattern;
        }
    }
}
