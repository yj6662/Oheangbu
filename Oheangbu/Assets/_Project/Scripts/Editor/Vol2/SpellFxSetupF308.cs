using System;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.SpellFx;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>2026-10-30 ㅗ 면 trial (the user: "ㅗ 면 6자로 먼저 시험"): the six ㄱ ㅗ glyphs 고 곡 곤 곰 곳 공 drawn as the hexagon-cell membrane lying on the ground (the ㅜ ward's grammar).
    /// Builds Restart/G/M_F_Fan_wood.mat (a copy of the 구 ward's material M_U_H_wood, tuned for a floor sheet), one prefab per glyph Restart/G/F_&lt;letter&gt;.prefab
    /// (SpellFxPlayer + SpellFxHexFanPart) and merges &lt;letter&gt;/F into the catalogue (only those entries are replaced; variant A is untouched).
    /// Needs setup:h to have run (it makes M_U_H_wood and M_U_H_ground).
    /// Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.SpellFxSetupF308Commands Run "setup:f"
    /// No dialogs. Saves only what it writes.</summary>
    public static class SpellFxSetupF308Commands
    {
        const string Root = "Assets/_Project/Art/SpellVFX308/Restart";
        const string GDir = Root + "/G";
        const string HDir = Root + "/U/H";
        const string Variant = "F";

        public static string Run(string command)
        {
            try
            {
                var c = (command ?? "").Trim();
                if (!string.Equals(c, "setup:f", StringComparison.OrdinalIgnoreCase)) return "REFUSED setup: setup:f";
                return SetupF();
            }
            catch (Exception e) { return "EXCEPTION " + e.GetType().Name + " " + e.Message; }
        }

        sealed class Recipe { public string Letter; public float PlaySeconds; public SpellFxHexFanPart.HexFanBite Bite; }

        static readonly Recipe[] Recipes =
        {
            new Recipe { Letter = "고", PlaySeconds = 4f,   Bite = SpellFxHexFanPart.HexFanBite.Sweep },
            new Recipe { Letter = "곡", PlaySeconds = 4.5f, Bite = SpellFxHexFanPart.HexFanBite.Thicket },
            new Recipe { Letter = "곤", PlaySeconds = 4f,   Bite = SpellFxHexFanPart.HexFanBite.FanSpread },
            new Recipe { Letter = "곰", PlaySeconds = 9f,   Bite = SpellFxHexFanPart.HexFanBite.FanSeed },
            new Recipe { Letter = "곳", PlaySeconds = 4f,   Bite = SpellFxHexFanPart.HexFanBite.FanCut },
            new Recipe { Letter = "공", PlaySeconds = 4f,   Bite = SpellFxHexFanPart.HexFanBite.FanReturn },
        };

        static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        static string Full(string assetPath) => Path.Combine(ProjectRoot, assetPath);

        static string SetupF()
        {
            var log = new StringBuilder();
            var wood = AssetDatabase.LoadAssetAtPath<Material>(HDir + "/M_U_H_wood.mat");
            if (wood == null) return "REFUSED setup:f: " + HDir + "/M_U_H_wood.mat missing (run setup:h first)";
            var ground = AssetDatabase.LoadAssetAtPath<Material>(HDir + "/M_U_H_ground.mat");
            if (ground == null) log.Append("no M_U_H_ground (setup:h) - no outline; ");
            Directory.CreateDirectory(Full(GDir));

            // ---- material: the wood ward's glass, tuned for a sheet seen at a low angle ----
            string matPath = GDir + "/M_F_Fan_wood.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(wood.shader); AssetDatabase.CreateAsset(mat, matPath); }
            EditorUtility.CopySerialized(wood, mat);
            mat.name = "M_F_Fan_wood";
            mat.SetFloat("_PatternFrom", 0f);          // the dome keeps its low ring plain; here uv2.y is the distance, patterns from the first cell
            mat.SetFloat("_FacingPattern", 0.85f);     // a floor seen from above is "facing" at a steep view: do not fade its patterns / seams
            mat.SetFloat("_FacingBorder", 0.9f);
            mat.SetFloat("_RiseTime", 0.35f);          // the part sets it again at run time
            var glass = mat.GetColor("_Glass"); glass.a = 0.2f; mat.SetColor("_Glass", glass);      // a lying sheet shows less of the glass than a wall
            EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat);
            log.Append("material ok; ");

            // ---- prefabs ----
            var built = new System.Collections.Generic.Dictionary<string, GameObject>();
            foreach (var r in Recipes)
            {
                var go = new GameObject("F_" + r.Letter);
                try
                {
                    var player = go.AddComponent<SpellFxPlayer>();
                    var pso = new SerializedObject(player);
                    pso.FindProperty("playSeconds").floatValue = r.PlaySeconds; pso.FindProperty("tailSeconds").floatValue = 0.5f;
                    pso.ApplyModifiedPropertiesWithoutUndo();
                    var part = go.AddComponent<SpellFxHexFanPart>();
                    var so = new SerializedObject(part);
                    so.FindProperty("material").objectReferenceValue = mat;
                    so.FindProperty("groundMaterial").objectReferenceValue = ground;
                    so.FindProperty("bite").enumValueIndex = (int)r.Bite;
                    so.FindProperty("burstBody").enumValueIndex = (int)SpellFxBurstBody.Wood;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    built[r.Letter] = PrefabUtility.SaveAsPrefabAsset(go, GDir + "/F_" + r.Letter + ".prefab");
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
                log.Append(r.Letter).Append(" prefab ok; ");
            }

            // ---- catalogue: MERGE - only <letter>/F of the six is replaced ----
            var cat = AssetDatabase.LoadAssetAtPath<SpellFxCatalog>(SpellFxCatalog.AssetPath);
            if (cat == null) { cat = ScriptableObject.CreateInstance<SpellFxCatalog>(); AssetDatabase.CreateAsset(cat, SpellFxCatalog.AssetPath); }
            var list = (cat.entries ?? new SpellFxCatalog.Entry[0]).Where(e => e != null && !(e.variant == Variant && built.ContainsKey(e.letter))).ToList();
            foreach (var kv in built) list.Add(new SpellFxCatalog.Entry { letter = kv.Key, variant = Variant, prefab = kv.Value });
            cat.entries = list.ToArray();
            EditorUtility.SetDirty(cat); AssetDatabase.SaveAssetIfDirty(cat);
            log.Append("catalogue ").Append(Variant).Append(": ").Append(string.Join(", ", built.Keys));
            return log.ToString();
        }
    }
}
