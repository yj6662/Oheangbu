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

            // 2026-10-30 (the user: "ㄱ 끝나면 나머지 술식도 완성해서 술식 도감에 반영해놔"): the other four bodies wear the same 24 templates - the
            // body's ink / glass / seam colours and its own KCISA atlas; every glyph but the bare ㅜ (the hexagon wards, setup:h)
            int bodiesBuilt = 0;
            foreach (var b in Skins)
            {
                var bv = Mat("M_G_Vine_" + b.Key, "Oheangbu/SpellFx/Vine", log);
                var bl = Mat("M_G_Leaf_" + b.Key, "Oheangbu/SpellFx/GlassLeaf", log);
                var bf = Mat("M_G_Flash_" + b.Key, "Oheangbu/SpellFx/BiteFlash", log);
                foreach (var kv in b.Colours) { var m = kv.Key.StartsWith("V") ? bv : kv.Key.StartsWith("L") ? bl : bf; m.SetColor(kv.Key.Substring(1), kv.Value); }
                bl.SetTexture("_Patterns", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/U/H/" + b.Atlas));
                bv.SetFloat("_SeamIdle", 0.24f);          // the body's colour runs along the stalk at rest too (all four read alike as black stalks otherwise)
                Save(bv); Save(bl); Save(bf);
                for (int jung = 0; jung < 4; jung++)
                    for (int jong = 0; jong < 6; jong++)
                    {
                        if (jung == 3 && jong == 0) continue;                    // the bare ㅜ: the ward
                        string letter = ((char)(0xAC00 + (b.Cho * 21 + Jung[jung]) * 28 + Jong[jong])).ToString();
                        var bite = Bites[jung * 6 + jong];
                        var go = new GameObject("G_" + letter);
                        try
                        {
                            var player = go.AddComponent<SpellFxPlayer>();
                            var pso = new SerializedObject(player); pso.FindProperty("playSeconds").floatValue = PlayOf(bite); pso.FindProperty("tailSeconds").floatValue = 0.5f; pso.ApplyModifiedPropertiesWithoutUndo();
                            Vine(go, bv, bl, bf, so => { so.FindProperty("bite").enumValueIndex = (int)bite; so.FindProperty("burstBody").enumValueIndex = (int)b.Burst; });
                            built[letter] = PrefabUtility.SaveAsPrefabAsset(go, GDir + "/G_" + letter + ".prefab");
                            bodiesBuilt++;
                        }
                        finally { UnityEngine.Object.DestroyImmediate(go); }
                    }
            }
            log.Append("other bodies: ").Append(bodiesBuilt).Append(" prefabs; ");

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

        // the Hangul indices: 초성 ㄱ0 ㄴ2 ㅁ6 ㅅ9 ㅇ11; 중성 ㅏ0 ㅓ4 ㅗ8 ㅜ13; 종성 없음0 ㄱ1 ㄴ4 ㅁ16 ㅅ19 ㅇ21
        static readonly int[] Jung = { 0, 4, 8, 13 }, Jong = { 0, 1, 4, 16, 19, 21 };
        // frame (ㅏ ㅓ ㅗ ㅜ) x verb (한 번 · 남긴다 · 번진다 · 심는다 · 가른다 · 돌아온다) -> the template (the ㄱ glyphs' own; ㅜ 한 번 is the ward, never used here)
        static readonly SpellFxVinePart.VineBite[] Bites =
        {
            SpellFxVinePart.VineBite.Coil, SpellFxVinePart.VineBite.Cage, SpellFxVinePart.VineBite.Spread, SpellFxVinePart.VineBite.Seed, SpellFxVinePart.VineBite.Cut, SpellFxVinePart.VineBite.Return,
            SpellFxVinePart.VineBite.Parry, SpellFxVinePart.VineBite.Heal, SpellFxVinePart.VineBite.Cling, SpellFxVinePart.VineBite.SelfSeed, SpellFxVinePart.VineBite.Blade, SpellFxVinePart.VineBite.Orbit,
            SpellFxVinePart.VineBite.Sweep, SpellFxVinePart.VineBite.Thicket, SpellFxVinePart.VineBite.FanSpread, SpellFxVinePart.VineBite.FanSeed, SpellFxVinePart.VineBite.FanCut, SpellFxVinePart.VineBite.FanReturn,
            SpellFxVinePart.VineBite.Coil, SpellFxVinePart.VineBite.RingPillar, SpellFxVinePart.VineBite.RingSpread, SpellFxVinePart.VineBite.RingSeed, SpellFxVinePart.VineBite.RingCut, SpellFxVinePart.VineBite.RingReturn,
        };

        static float PlayOf(SpellFxVinePart.VineBite b)
        {
            switch (b)
            {
                case SpellFxVinePart.VineBite.Parry: return 1.8f;
                case SpellFxVinePart.VineBite.Heal: case SpellFxVinePart.VineBite.Thicket: return 4.5f;
                case SpellFxVinePart.VineBite.Cling: case SpellFxVinePart.VineBite.Blade: case SpellFxVinePart.VineBite.Orbit: return 7f;
                case SpellFxVinePart.VineBite.SelfSeed: case SpellFxVinePart.VineBite.FanSeed: case SpellFxVinePart.VineBite.RingSeed: return 9f;
                case SpellFxVinePart.VineBite.RingCut: return 10.5f;
                case SpellFxVinePart.VineBite.RingPillar: return 6f;
                default: return 4f;
            }
        }

        sealed class Skin { public string Key, Atlas; public int Cho; public SpellFxBurstBody Burst; public Dictionary<string, Color> Colours; }

        static Skin MakeSkin(string key, int cho, string atlas, SpellFxBurstBody burst, Color ink, Color inkHi, Color rim, Color seam, Color glass, Color line, Color glow, Color core, Color ray)
            => new Skin { Key = key, Cho = cho, Atlas = atlas, Burst = burst, Colours = new Dictionary<string, Color> {
                { "V_Ink", ink }, { "V_InkHi", inkHi }, { "V_Rim", rim }, { "V_Seam", seam }, { "L_Glass", glass }, { "L_Line", line }, { "L_Glow", glow }, { "F_Core", core }, { "F_Ray", ray } } };

        /// <summary>The four other bodies (prefix letter of each colour key: V vine tube, L glass leaf, F bite flash).</summary>
        static readonly Skin[] Skins =
        {
            MakeSkin("fire", 2, "h_patterns_fire.png", SpellFxBurstBody.Fire, new Color(0.035f, 0.02f, 0.015f), new Color(0.2f, 0.09f, 0.05f), new Color(1f, 0.45f, 0.15f, 0.25f), new Color(1.45f, 0.7f, 0.25f),
                new Color(0.5f, 0.2f, 0.07f, 0.3f), new Color(1f, 0.78f, 0.5f), new Color(1.3f, 0.65f, 0.22f), new Color(1.2f, 0.85f, 0.45f), new Color(1.25f, 0.55f, 0.15f)),
            MakeSkin("earth", 6, "h_patterns_earth.png", SpellFxBurstBody.Earth, new Color(0.04f, 0.03f, 0.02f), new Color(0.22f, 0.16f, 0.09f), new Color(0.85f, 0.65f, 0.35f, 0.22f), new Color(1.2f, 0.95f, 0.55f),
                new Color(0.4f, 0.3f, 0.16f, 0.3f), new Color(0.96f, 0.86f, 0.66f), new Color(1.1f, 0.86f, 0.46f), new Color(1.1f, 0.95f, 0.7f), new Color(1.1f, 0.8f, 0.4f)),
            MakeSkin("metal", 9, "h_patterns_metal.png", SpellFxBurstBody.Metal, new Color(0.03f, 0.032f, 0.036f), new Color(0.2f, 0.21f, 0.24f), new Color(0.8f, 0.85f, 0.95f, 0.25f), new Color(1.2f, 1.25f, 1.35f),
                new Color(0.36f, 0.39f, 0.45f, 0.3f), new Color(0.95f, 0.97f, 1f), new Color(1.2f, 1.25f, 1.35f), new Color(1.15f, 1.15f, 1.2f), new Color(1.1f, 1.15f, 1.3f)),
            MakeSkin("water", 11, "h_patterns.png", SpellFxBurstBody.Water, new Color(0.02f, 0.03f, 0.05f), new Color(0.1f, 0.14f, 0.22f), new Color(0.4f, 0.6f, 1f, 0.25f), new Color(0.7f, 0.95f, 1.4f),
                new Color(0.14f, 0.26f, 0.46f, 0.3f), new Color(0.85f, 0.92f, 1f), new Color(0.7f, 0.95f, 1.4f), new Color(0.9f, 1f, 1.2f), new Color(0.6f, 0.85f, 1.3f)),
        };

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
            // ㅗ 면: the vine over the wedge in front
            new Recipe { Letter = "고", PlaySeconds = 3.4f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.Sweep)) },
            new Recipe { Letter = "곡", PlaySeconds = 4.5f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.Thicket)) },
            new Recipe { Letter = "곤", PlaySeconds = 3.8f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.FanSpread)) },
            new Recipe { Letter = "곰", PlaySeconds = 9f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.FanSeed)) },
            new Recipe { Letter = "곳", PlaySeconds = 3.6f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.FanCut)) },
            new Recipe { Letter = "공", PlaySeconds = 2.8f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.FanReturn)) },
            // ㅜ 둘레 with a final (the bare 구 is the hexagon ward, setup:h)
            new Recipe { Letter = "국", PlaySeconds = 6f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.RingPillar)) },
            new Recipe { Letter = "군", PlaySeconds = 3.6f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.RingSpread)) },
            new Recipe { Letter = "굼", PlaySeconds = 9f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.RingSeed)) },
            new Recipe { Letter = "굿", PlaySeconds = 10.5f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.RingCut)) },
            new Recipe { Letter = "궁", PlaySeconds = 3.2f, Build = (go, v, l, f) => Vine(go, v, l, f, Bite(SpellFxVinePart.VineBite.RingReturn)) },
        };

        static Action<SerializedObject> Bite(SpellFxVinePart.VineBite b) => so => so.FindProperty("bite").enumValueIndex = (int)b;
    }
}
