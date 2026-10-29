using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.SpellFx;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>2026-10-30 초성 ㄱ(목) 24자 (the user: "이제 'ㄱ' 술식부터 전체 진행하자"; ledger Docs/Handoff/HANDOFF-308-SPELL-G-20261030.md):
    /// builds the shared materials under Restart/G (vine tube, glass leaf, bite flash) and one prefab per glyph that has a recipe here, Restart/G/G_&lt;letter&gt;.prefab,
    /// then merges &lt;letter&gt;/A into the catalogue (A = the variant the game plays by default; only those entries are replaced).
    /// The leaf patterns are the ward's wood atlas (Restart/U/H/h_patterns_wood.png, setup:h).
    /// Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.SpellFxSetupG308Commands Run "setup:g"
    /// No dialogs. Saves only what it writes.</summary>
    public static class SpellFxSetupG308Commands
    {
        const string Root = "Assets/_Project/Art/SpellVFX308/Restart";
        const string GDir = Root + "/G";
        const string Variant = "A";

        public static string Run(string command)
        {
            try
            {
                var c = (command ?? "").Trim();
                if (!string.Equals(c, "setup:g", StringComparison.OrdinalIgnoreCase)) return "REFUSED setup: setup:g";
                return Setup();
            }
            catch (Exception e) { return "EXCEPTION " + e.GetType().Name + " " + e.Message; }
        }

        static Material Mat(string name, string shaderName, StringBuilder log)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) { log.Append("shader missing ").Append(shaderName).Append("; "); return null; }
            string path = GDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            var fresh = new Material(shader);
            try { EditorUtility.CopySerialized(fresh, mat); } finally { UnityEngine.Object.DestroyImmediate(fresh); }
            mat.name = name;
            return mat;
        }

        static void Save(UnityEngine.Object o) { EditorUtility.SetDirty(o); AssetDatabase.SaveAssetIfDirty(o); }

        static string Setup()
        {
            var log = new StringBuilder();
            Directory.CreateDirectory(Path.Combine(Directory.GetParent(Application.dataPath).FullName, GDir));
            var vine = Mat("M_G_Vine", "Oheangbu/SpellFx/Vine", log);
            var leaf = Mat("M_G_Leaf", "Oheangbu/SpellFx/GlassLeaf", log);
            var flash = Mat("M_G_Flash", "Oheangbu/SpellFx/BiteFlash", log);
            if (vine == null || leaf == null || flash == null) return "REFUSED setup:g: " + log;
            var wood = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/U/H/h_patterns_wood.png");
            if (wood == null) log.Append("no wood atlas (setup:h) - leaves without patterns; ");
            leaf.SetTexture("_Patterns", wood);
            Save(vine); Save(leaf); Save(flash);

            var built = new Dictionary<string, GameObject>();
            foreach (var r in Recipes)
            {
                var go = new GameObject("G_" + r.Letter);
                try
                {
                    var player = go.AddComponent<SpellFxPlayer>();
                    var pso = new SerializedObject(player); pso.FindProperty("playSeconds").floatValue = r.PlaySeconds; pso.FindProperty("tailSeconds").floatValue = 0.5f; pso.ApplyModifiedPropertiesWithoutUndo();
                    r.Build(go, vine, leaf, flash);
                    built[r.Letter] = PrefabUtility.SaveAsPrefabAsset(go, GDir + "/G_" + r.Letter + ".prefab");
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
                log.Append(r.Letter).Append(" prefab ok; ");
            }

            var cat = AssetDatabase.LoadAssetAtPath<SpellFxCatalog>(SpellFxCatalog.AssetPath);
            if (cat == null) { cat = ScriptableObject.CreateInstance<SpellFxCatalog>(); AssetDatabase.CreateAsset(cat, SpellFxCatalog.AssetPath); }
            var list = (cat.entries ?? new SpellFxCatalog.Entry[0]).Where(e => e != null && !(e.variant == Variant && built.ContainsKey(e.letter))).ToList();
            foreach (var kv in built) list.Add(new SpellFxCatalog.Entry { letter = kv.Key, variant = Variant, prefab = kv.Value });
            cat.entries = list.ToArray();
            Save(cat);
            log.Append("catalogue ").Append(Variant).Append(": ").Append(string.Join(", ", built.Keys));
            return log.ToString();
        }

        sealed class Recipe { public string Letter; public float PlaySeconds; public Action<GameObject, Material, Material, Material> Build; }

        static void Vine(GameObject go, Material vine, Material leaf, Material flash, Action<SerializedObject> tune = null)
        {
            var part = go.AddComponent<SpellFxVinePart>();
            var so = new SerializedObject(part);
            so.FindProperty("tubeMaterial").objectReferenceValue = vine;
            so.FindProperty("leafMaterial").objectReferenceValue = leaf;
            so.FindProperty("flashMaterial").objectReferenceValue = flash;
            tune?.Invoke(so);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>One recipe per glyph done so far (the ledger's order). A glyph not listed keeps whatever the catalogue has.</summary>
        static readonly Recipe[] Recipes =
        {
            new Recipe { Letter = "가", PlaySeconds = 3.4f, Build = (go, v, l, f) => Vine(go, v, l, f) },
            new Recipe { Letter = "각", PlaySeconds = 4f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.Cage)) },
            new Recipe { Letter = "간", PlaySeconds = 4.2f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.Spread)) },
            new Recipe { Letter = "감", PlaySeconds = 4f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.Seed)) },
            new Recipe { Letter = "갓", PlaySeconds = 3.6f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.Cut)) },
            new Recipe { Letter = "강", PlaySeconds = 3.4f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.Return)) },
            // ㅓ 부착: the vine on the caster (the same part, the self verbs)
            new Recipe { Letter = "거", PlaySeconds = 1.8f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.Parry)) },
            new Recipe { Letter = "걱", PlaySeconds = 4.2f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.Heal)) },
            new Recipe { Letter = "건", PlaySeconds = 7f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.Cling)) },
            new Recipe { Letter = "검", PlaySeconds = 9f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.SelfSeed)) },
            new Recipe { Letter = "것", PlaySeconds = 7f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.Blade)) },
            new Recipe { Letter = "겅", PlaySeconds = 7f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.Orbit)) },
        };

        static Action<SerializedObject> Bite(SpellFxVinePart.VineBite b) => so => so.FindProperty("bite").enumValueIndex = (int)b;
    }
}
