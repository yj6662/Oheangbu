using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.Demo;
using Oheangbu.App.SpellVFX120;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.47: the summon attacks that still drew the old way, as ink bodies. The wood deer's roots
    // keep their botanical mesh and ground plan but take an InkSolid308 material (wood fibre grain, ink first, the pigment
    // soaking in); the water turtle's jet keeps its tube and clock but takes an InkSolid308 material (water grain flowing
    // forward) and drops the foam. Nothing glows, nothing flies off, nothing stays on the ground.
    //   beast:ink          - build the two materials
    //   beast:ink:connect  - put them on the live WoodDeer / WaterTurtle profiles (recorded; also run by connect:attack)
    //   beast:ink:revert   - put back what was there (also run by revert)
    public static partial class Vfx308Build
    {
        static string BeastInkRecord => Path.Combine(Path.GetDirectoryName(Record), "beast-ink-before.json");
        static string DeerRootInkPath => Root + "/Materials/M_V308_ga_deer_root.mat";
        static string TurtleJetInkPath => Root + "/Materials/M_V308_a_turtle_jet.mat";

        [Serializable] sealed class BeastInkRecorded { public List<string> profiles = new List<string>(); public List<string> root = new List<string>(); public List<string> jet = new List<string>(); }

        static string BuildBeastInk()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build beast:ink: Edit mode only");
            V2 = true; Touched.Clear(); seed = 44700;
            var ga = Load<Vfx120Profile>("Assets/_Project/Art/SpellVFX120/Profiles/" + Spells.First(x => x.Key == "ga").Effect + ".asset");
            var a = Load<Vfx120Profile>("Assets/_Project/Art/SpellVFX120/Profiles/" + Spells.First(x => x.Key == "a").Effect + ".asset");
            var root = Solid("M_V308_ga_deer_root", ga.Pigment, ga.Ink);
            var jet = Solid("M_V308_a_turtle_jet", a.Pigment, a.Ink, .45f, .3f);
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            return "beast:ink -> " + AssetDatabase.GetAssetPath(root) + ", " + AssetDatabase.GetAssetPath(jet) + " (InkSolid308)";
        }

        static bool IsDeer(SummonCombatProfile p) => AssetDatabase.GetAssetPath(p).EndsWith("_WoodDeer.asset", StringComparison.Ordinal);
        static bool IsTurtle(SummonCombatProfile p) => AssetDatabase.GetAssetPath(p).EndsWith("_WaterTurtle.asset", StringComparison.Ordinal);

        static string ConnectBeastInk()
        {
            var root = AssetDatabase.LoadAssetAtPath<Material>(DeerRootInkPath);
            var jet = AssetDatabase.LoadAssetAtPath<Material>(TurtleJetInkPath);
            if (root == null || jet == null) return "beast:ink: not built (run beast:ink) - left as it is";
            var live = LiveBeasts().Where(x => IsDeer(x) || IsTurtle(x)).ToList();
            if (live.Count == 0) return "beast:ink: no live WoodDeer / WaterTurtle profile";
            if (!File.Exists(BeastInkRecord))
            {
                var rec = new BeastInkRecorded();
                foreach (var p in live)
                {
                    rec.profiles.Add(AssetDatabase.GetAssetPath(p));
                    rec.root.Add(AssetDatabase.GetAssetPath(p.RootInk308Material)); rec.jet.Add(AssetDatabase.GetAssetPath(p.WaterJetInk308Material));
                }
                File.WriteAllText(BeastInkRecord, JsonUtility.ToJson(rec, true));
            }
            int deer = 0, turtle = 0;
            foreach (var p in live)
            {
                if (IsDeer(p)) { p.RootInk308Material = root; deer++; }
                if (IsTurtle(p)) { p.WaterJetInk308Material = jet; turtle++; }
                EditorUtility.SetDirty(p); AssetDatabase.SaveAssetIfDirty(p);
            }
            return "beast:ink: " + deer + " live WoodDeer -> " + DeerRootInkPath + ", " + turtle + " live WaterTurtle -> " + TurtleJetInkPath;
        }

        static string RevertBeastInk()
        {
            if (!File.Exists(BeastInkRecord)) return "beast:ink: nothing recorded";
            var rec = JsonUtility.FromJson<BeastInkRecorded>(File.ReadAllText(BeastInkRecord));
            for (int i = 0; i < rec.profiles.Count; i++)
            {
                var p = AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(rec.profiles[i]);
                if (p == null) continue;
                p.RootInk308Material = string.IsNullOrEmpty(rec.root[i]) ? null : AssetDatabase.LoadAssetAtPath<Material>(rec.root[i]);
                p.WaterJetInk308Material = string.IsNullOrEmpty(rec.jet[i]) ? null : AssetDatabase.LoadAssetAtPath<Material>(rec.jet[i]);
                EditorUtility.SetDirty(p); AssetDatabase.SaveAssetIfDirty(p);
            }
            File.Delete(BeastInkRecord);
            return "beast:ink: restored " + rec.profiles.Count + " profiles";
        }
    }
}
