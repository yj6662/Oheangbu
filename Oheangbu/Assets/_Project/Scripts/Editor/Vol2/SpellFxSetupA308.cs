using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Oheangbu.App.SpellFx;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>2026-10-30 ㅓ 부착 (PLAN-308 §2.3, lane C; the user: "'ㅜ' 술식과 같은 톤의 술식들을 계속 구현해"): the six wood ㅓ glyphs 거 걱 건 검 것 겅 (초성 ㄱ x 종성 없음 ㄱ ㄴ ㅁ ㅅ ㅇ) and the
    /// general "남긴다" of the fire body, 넉 (ㄴ + ㅓ + ㄱ: "불이 내 몸에 잔존물로 붙는다 6 s"; the template trial's 7th glyph - the 일반 잔존물 template of the ㄱ trial's missing 16),
    /// drawn as cells on the caster's body (<see cref="SpellFxHexAttachPart"/>: the brush shaft, the sleeve, the flank of the shoulder - nothing on the back, nothing on the ground). One prefab per glyph
    /// Restart/G/A_&lt;letter&gt;.prefab (SpellFxPlayer + SpellFxHexAttachPart, the body's sheet material Restart/G/M_F_Fan_&lt;body&gt;.mat - taken as it is when setup:f made it, else made the way setup:f
    /// makes it - and its end burst). The catalogue is MERGED: variant F of these glyphs is dropped and re-added pointing at the A_ prefab; variant A (the game's default, the vine) is NOT touched - the
    /// gate decides. Every other glyph's entries are untouched.
    /// Play lengths: 거 1.8 s (0.5 s hold + the break), 걱 4 s (3 s heal, the last cell climbs 0.9 s), 건 / 것 / 넉 7.5 s (6 s hold + the letting go: far cells first, 0.05 s apart, 0.3 s each), 겅 8 s (12 cells 0.08 s
    /// apart + 0.3 s), 검 9.5 s (its 8 s wait is the rule's, 7 s would cut the seed).
    /// Round 2 (2026-10-31): the part now writes nothing but these five fields (material, bite, burstBody, holdSeconds, apexMark) - every size, radius and timing it used to serialize (cellRadiusMin / Max,
    /// cameraCone, orbitRadius, seedRadius ...) is a constant inside the part, so a prefab made by an earlier run of this command carries stale values that are simply ignored; re-running setup:a is only
    /// needed to pick up the new PlaySeconds below.
    /// Needs setup:h (the ward materials; only when a sheet material is missing).
    /// Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.SpellFxSetupA308Commands Run "setup:a"
    /// No dialogs. Saves only what it writes.
    ///
    /// 2026-10-31 (D308-161, the user: "방어는 그런 육각면으로 하고, 공격은 다른 투사체를 사용해야지"): the hex membrane is DEFENCE only - the 막기 five (우 H) and the 패링 five 거 너 머 서 어
    /// (ㄱ ㄴ ㅁ ㅅ ㅇ + ㅓ, no final consonant; wood fire earth metal water). <c>setup:a:parry</c> makes those five: Restart/G/A_&lt;letter&gt;.prefab (SpellFxPlayer 1.8 s + SpellFxHexAttachPart, bite Parry, hold 0.5 s,
    /// the body's end burst) on a per-body material Restart/G/M_A_Parry_&lt;body&gt;.mat = a copy of that body's ward (막기 dome) material in Restart/U/H - the same family colours, accent and KCISA pattern atlas -
    /// with the small-standing-cell widths of <see cref="SpellFxSetupF308Commands.Tune"/> laid over it (the ones this setup's sheet materials M_F_Fan_&lt;body&gt; carry: halo / seam in render px, patterns from the first
    /// cell) and <c>_FlashWhite</c> 0 (the layer sets it on its own instance as well). The struck-cell flash is the material's <c>_Accent</c> (the part lerps the seam to _Accent x 1.6), so it comes out in the
    /// body's own colour. The catalogue: for these five glyphs variant F AND variant A both point at the A_ prefab (the parry is the hex membrane by default); no other glyph's entry is touched.
    /// <c>setup:a</c> builds 거 exactly the same way (same material, same catalogue F + A), so running either, in any order, leaves the same 거. NOTE: <c>setup:g</c> points variant A of 거 너 머 서 어 back at the vine -
    /// run <c>setup:a:parry</c> again after it.
    /// Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.SpellFxSetupA308Commands Run "setup:a:parry"</summary>
    public static class SpellFxSetupA308Commands
    {
        const string Root = "Assets/_Project/Art/SpellVFX308/Restart";
        const string GDir = Root + "/G";
        const string HDir = Root + "/U/H";

        public static string Run(string command)
        {
            try
            {
                var c = (command ?? "").Trim();
                if (string.Equals(c, "setup:a", StringComparison.OrdinalIgnoreCase)) return SetupA();
                if (string.Equals(c, "setup:a:parry", StringComparison.OrdinalIgnoreCase)) return SetupParry();
                return "REFUSED setup: setup:a | setup:a:parry";
            }
            catch (Exception e) { return "EXCEPTION " + e.GetType().Name + " " + e.Message; }
        }

        /// <summary>One glyph's recipe: the body (key of its material, 초성 index, what its end is made of), the 종성 index, the verb, how long the cells stand (the rule's own life) and how long the prefab plays.</summary>
        sealed class Glyph
        {
            public string Key; public int Cho, Jong; public SpellFxBurstBody Burst;
            public SpellFxHexAttachPart.HexAttachBite Bite; public float Hold, PlaySeconds; public bool Apex;
            public bool IsParry => Bite == SpellFxHexAttachPart.HexAttachBite.Parry;
        }

        const int Jung_EO = 4;      // ㅓ

        /// <summary>The 패링 five (ㅓ, no final consonant): 거 목 · 너 화 · 머 토 · 서 금 · 어 수; the ward (막기 dome) material each one's cell material is copied from (water = the plain M_U_H).</summary>
        sealed class ParryBody { public string Key, Ward; public int Cho; public SpellFxBurstBody Burst; }

        static readonly ParryBody[] ParryBodies =
        {
            new ParryBody { Key = "wood",  Ward = "M_U_H_wood",  Cho = 0,  Burst = SpellFxBurstBody.Wood },
            new ParryBody { Key = "fire",  Ward = "M_U_H_fire",  Cho = 2,  Burst = SpellFxBurstBody.Fire },
            new ParryBody { Key = "earth", Ward = "M_U_H_earth", Cho = 6,  Burst = SpellFxBurstBody.Earth },
            new ParryBody { Key = "metal", Ward = "M_U_H_metal", Cho = 9,  Burst = SpellFxBurstBody.Metal },
            new ParryBody { Key = "water", Ward = "M_U_H",       Cho = 11, Burst = SpellFxBurstBody.Water },
        };

        const float ParryHold = 0.5f, ParryPlaySeconds = 1.8f;

        static Glyph ParryGlyph(ParryBody b) => new Glyph { Key = b.Key, Cho = b.Cho, Jong = 0, Burst = b.Burst, Bite = SpellFxHexAttachPart.HexAttachBite.Parry, Hold = ParryHold, PlaySeconds = ParryPlaySeconds };

        static string ParryMatPath(string key) => GDir + "/M_A_Parry_" + key + ".mat";

        // 종성 없음 ㄱ ㄴ ㅁ ㅅ ㅇ (Hangul jongseong indices 0 1 4 16 19 21): 거 Parry · 걱 Heal (극치) · 건 Cling · 검 SelfSeed · 것 Blade · 겅 Orbit; + 넉 Residue (fire, 초성 ㄴ = 2, 종성 ㄱ = 1)
        static readonly Glyph[] Glyphs =
        {
            new Glyph { Key = "wood", Cho = 0, Jong = 0,  Burst = SpellFxBurstBody.Wood, Bite = SpellFxHexAttachPart.HexAttachBite.Parry,    Hold = 0.5f, PlaySeconds = 1.8f },
            new Glyph { Key = "wood", Cho = 0, Jong = 1,  Burst = SpellFxBurstBody.Wood, Bite = SpellFxHexAttachPart.HexAttachBite.Heal,     Hold = 3f,   PlaySeconds = 4f, Apex = true },
            new Glyph { Key = "wood", Cho = 0, Jong = 4,  Burst = SpellFxBurstBody.Wood, Bite = SpellFxHexAttachPart.HexAttachBite.Cling,    Hold = 6f,   PlaySeconds = 7.5f },
            new Glyph { Key = "wood", Cho = 0, Jong = 16, Burst = SpellFxBurstBody.Wood, Bite = SpellFxHexAttachPart.HexAttachBite.SelfSeed, Hold = 8f,   PlaySeconds = 9.5f },
            new Glyph { Key = "wood", Cho = 0, Jong = 19, Burst = SpellFxBurstBody.Wood, Bite = SpellFxHexAttachPart.HexAttachBite.Blade,    Hold = 6f,   PlaySeconds = 7.5f },
            new Glyph { Key = "wood", Cho = 0, Jong = 21, Burst = SpellFxBurstBody.Wood, Bite = SpellFxHexAttachPart.HexAttachBite.Orbit,    Hold = 6f,   PlaySeconds = 8f },
            new Glyph { Key = "fire", Cho = 2, Jong = 1,  Burst = SpellFxBurstBody.Fire, Bite = SpellFxHexAttachPart.HexAttachBite.Residue,  Hold = 6f,   PlaySeconds = 7.5f },
        };

        static string LetterOf(Glyph g) => ((char)(0xAC00 + (g.Cho * 21 + Jung_EO) * 28 + g.Jong)).ToString();

        static string SetupA()
        {
            var log = new StringBuilder();
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, GDir));

            // ---- the sheet materials: the ones setup:f made (reused as they are), else made the same way ----
            var mats = new Dictionary<string, Material>();
            // 거 (the parry) is a hex-membrane DEFENCE glyph: its cells are the parry material (M_A_Parry_wood), exactly what setup:a:parry makes - so the two commands leave the same 거.
            foreach (var g in Glyphs)
            {
                if (g.IsParry) continue;
                if (mats.ContainsKey(g.Key)) continue;
                var mat = SpellFxSetupF308Commands.EnsureMaterial(g.Key, false, "setup:a", out string problem);
                if (mat == null) return problem;
                mats[g.Key] = mat;
            }
            Material parryWood = null;
            if (Glyphs.Any(g => g.IsParry))
            {
                parryWood = EnsureParryMaterial(ParryBodies[0], "setup:a", out string problem);
                if (parryWood == null) return problem;
            }
            log.Append("materials ").Append(mats.Count + (parryWood != null ? 1 : 0)).Append("; ");

            // ---- prefabs: 6 wood glyphs + 넉 ----
            var built = new Dictionary<string, GameObject>();
            var both = new HashSet<string>();
            foreach (var g in Glyphs)
            {
                string letter = LetterOf(g);
                built[letter] = BuildPrefab(g, letter, g.IsParry ? parryWood : mats[g.Key]);
                if (g.IsParry) both.Add(letter);
            }
            log.Append("prefabs ").Append(built.Count).Append(" (").Append(string.Join("", built.Keys)).Append("); ");

            // ---- catalogue: MERGE - for these glyphs only, variant F is dropped and re-added pointing at the A_ prefab (A, the game's default, stays); the parry 거 gets F AND A ----
            log.Append(MergeCatalogue(built, both));
            return log.ToString();
        }

        /// <summary>2026-10-31 (D308-161): the five parry glyphs 거 너 머 서 어 as hex-membrane cells on the caster's body, one per body. See the class comment.</summary>
        static string SetupParry()
        {
            var log = new StringBuilder();
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, GDir));

            // ---- the five cell materials: Restart/G/M_A_Parry_<body>.mat, always rebuilt from the ward material (deterministic) ----
            var mats = new Dictionary<string, Material>();
            foreach (var b in ParryBodies)
            {
                var mat = EnsureParryMaterial(b, "setup:a:parry", out string problem);
                if (mat == null) return problem;
                mats[b.Key] = mat;
                Color acc = mat.GetColor("_Accent");
                log.Append("M_A_Parry_").Append(b.Key).Append(" _Accent(").Append(acc.r.ToString("0.##")).Append(',').Append(acc.g.ToString("0.##")).Append(',').Append(acc.b.ToString("0.##"))
                   .Append(") _FlashWhite ").Append(mat.GetFloat("_FlashWhite").ToString("0.##")).Append(" halo/seam px ").Append(mat.GetFloat("_HaloPx").ToString("0.##")).Append('/').Append(mat.GetFloat("_SeamPx").ToString("0.##")).Append("; ");
            }

            // ---- prefabs: 5 bodies x the parry ----
            var built = new Dictionary<string, GameObject>();
            var both = new HashSet<string>();
            foreach (var b in ParryBodies)
            {
                var g = ParryGlyph(b);
                string letter = LetterOf(g);
                var row = SpellFxHexGlyphs308.Of(letter[0]);
                if (row == null || row.Frame != HexGlyphFrame.Attach) return "REFUSED setup:a:parry: " + letter + " is not a ㅓ (attach) glyph of SpellFxHexGlyphs308";
                built[letter] = BuildPrefab(g, letter, mats[b.Key]);
                both.Add(letter);
            }
            log.Append("prefabs ").Append(built.Count).Append(" (").Append(string.Join("", built.Keys)).Append("); ");

            log.Append(MergeCatalogue(built, both));
            return log.ToString();
        }

        /// <summary>One A_ prefab: SpellFxPlayer + SpellFxHexAttachPart, written with the five fields the part reads (material, bite, burstBody, holdSeconds, apexMark).</summary>
        static GameObject BuildPrefab(Glyph g, string letter, Material material)
        {
            var go = new GameObject("A_" + letter);
            try
            {
                var player = go.AddComponent<SpellFxPlayer>();
                var pso = new SerializedObject(player);
                pso.FindProperty("playSeconds").floatValue = g.PlaySeconds; pso.FindProperty("tailSeconds").floatValue = 0.5f;
                pso.ApplyModifiedPropertiesWithoutUndo();
                var part = go.AddComponent<SpellFxHexAttachPart>();
                var so = new SerializedObject(part);
                so.FindProperty("material").objectReferenceValue = material;
                so.FindProperty("bite").enumValueIndex = (int)g.Bite;
                so.FindProperty("burstBody").enumValueIndex = (int)g.Burst;          // the END of the cells: the body's own (목 = none, the roots let go)
                so.FindProperty("holdSeconds").floatValue = g.Hold;                  // the rule's own life (거 0.5 · 걱 3 · 검 8 · the rest 6)
                so.FindProperty("apexMark").boolValue = g.Apex;                      // 걱 is a 극치 glyph: cells x1.25, the body's main pattern
                so.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(go, GDir + "/A_" + letter + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        /// <summary>Catalogue MERGE for the glyphs in <paramref name="built"/> only: their variant F is dropped and re-added pointing at the A_ prefab; for the letters in <paramref name="both"/> variant A is
        /// dropped and re-added too (the game plays A by default). Every other entry is untouched.</summary>
        static string MergeCatalogue(Dictionary<string, GameObject> built, HashSet<string> both)
        {
            var cat = AssetDatabase.LoadAssetAtPath<SpellFxCatalog>(SpellFxCatalog.AssetPath);
            if (cat == null) { cat = ScriptableObject.CreateInstance<SpellFxCatalog>(); AssetDatabase.CreateAsset(cat, SpellFxCatalog.AssetPath); }
            var before = cat.entries ?? new SpellFxCatalog.Entry[0];
            var list = before.Where(e => e != null && !(e.letter != null && built.ContainsKey(e.letter) && (e.variant == "F" || (e.variant == "A" && both.Contains(e.letter))))).ToList();
            int replacedA = before.Count(e => e != null && e.variant == "A" && e.letter != null && both.Contains(e.letter));
            foreach (var kv in built)
            {
                list.Add(new SpellFxCatalog.Entry { letter = kv.Key, variant = "F", prefab = kv.Value });
                if (both.Contains(kv.Key)) list.Add(new SpellFxCatalog.Entry { letter = kv.Key, variant = "A", prefab = kv.Value });
            }
            cat.entries = list.ToArray();
            EditorUtility.SetDirty(cat); AssetDatabase.SaveAssetIfDirty(cat);
            return "catalogue: " + built.Count + " glyphs x F, " + both.Count + " of them also x A (A replaced where it existed: " + replacedA + "; the other glyphs' A untouched); entries " + before.Length + " -> " + cat.entries.Length;
        }

        /// <summary>Restart/G/M_A_Parry_&lt;body&gt;.mat: a copy of the body's ward (막기 dome) material in Restart/U/H - colours, accent and pattern atlas as they are - with the small-cell widths of
        /// <see cref="SpellFxSetupF308Commands.Tune"/> laid over it and _FlashWhite 0 (a struck cell flashes its seams in the material's _Accent; the layer sets the same on its instance). Rebuilt on every run.
        /// null + <paramref name="problem"/> when the ward material is missing (run setup:h).</summary>
        static Material EnsureParryMaterial(ParryBody b, string command, out string problem)
        {
            problem = null;
            var ward = AssetDatabase.LoadAssetAtPath<Material>(HDir + "/" + b.Ward + ".mat");
            if (ward == null) { problem = "REFUSED " + command + ": " + HDir + "/" + b.Ward + ".mat missing (run setup:h first)"; return null; }
            string path = ParryMatPath(b.Key);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(ward.shader); AssetDatabase.CreateAsset(mat, path); }
            EditorUtility.CopySerialized(ward, mat);
            mat.name = "M_A_Parry_" + b.Key;
            SpellFxSetupF308Commands.Tune(mat, ward, b.Key == "wood");
            if (mat.HasProperty("_FlashWhite")) mat.SetFloat("_FlashWhite", 0f);
            EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat);
            return mat;
        }
    }
}
