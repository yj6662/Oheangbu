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
    /// Play lengths: 거 1.8 s (0.5 s hold + the break), 걱 4 s (3 s heal, the last cell climbs 0.9 s), the rest 7 s (6 s hold + the letting go); 검 9.5 s (its 8 s wait is the rule's, 7 s would cut the seed).
    /// Needs setup:h (the ward materials; only when a sheet material is missing).
    /// Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.SpellFxSetupA308Commands Run "setup:a"
    /// No dialogs. Saves only what it writes.</summary>
    public static class SpellFxSetupA308Commands
    {
        const string Root = "Assets/_Project/Art/SpellVFX308/Restart";
        const string GDir = Root + "/G";

        public static string Run(string command)
        {
            try
            {
                var c = (command ?? "").Trim();
                if (!string.Equals(c, "setup:a", StringComparison.OrdinalIgnoreCase)) return "REFUSED setup: setup:a";
                return SetupA();
            }
            catch (Exception e) { return "EXCEPTION " + e.GetType().Name + " " + e.Message; }
        }

        /// <summary>One glyph's recipe: the body (key of its material, 초성 index, what its end is made of), the 종성 index, the verb, how long the cells stand (the rule's own life) and how long the prefab plays.</summary>
        sealed class Glyph
        {
            public string Key; public int Cho, Jong; public SpellFxBurstBody Burst;
            public SpellFxHexAttachPart.HexAttachBite Bite; public float Hold, PlaySeconds; public bool Apex;
        }

        const int Jung_EO = 4;      // ㅓ

        // 종성 없음 ㄱ ㄴ ㅁ ㅅ ㅇ (Hangul jongseong indices 0 1 4 16 19 21): 거 Parry · 걱 Heal (극치) · 건 Cling · 검 SelfSeed · 것 Blade · 겅 Orbit; + 넉 Residue (fire, 초성 ㄴ = 2, 종성 ㄱ = 1)
        static readonly Glyph[] Glyphs =
        {
            new Glyph { Key = "wood", Cho = 0, Jong = 0,  Burst = SpellFxBurstBody.Wood, Bite = SpellFxHexAttachPart.HexAttachBite.Parry,    Hold = 0.5f, PlaySeconds = 1.8f },
            new Glyph { Key = "wood", Cho = 0, Jong = 1,  Burst = SpellFxBurstBody.Wood, Bite = SpellFxHexAttachPart.HexAttachBite.Heal,     Hold = 3f,   PlaySeconds = 4f, Apex = true },
            new Glyph { Key = "wood", Cho = 0, Jong = 4,  Burst = SpellFxBurstBody.Wood, Bite = SpellFxHexAttachPart.HexAttachBite.Cling,    Hold = 6f,   PlaySeconds = 7f },
            new Glyph { Key = "wood", Cho = 0, Jong = 16, Burst = SpellFxBurstBody.Wood, Bite = SpellFxHexAttachPart.HexAttachBite.SelfSeed, Hold = 8f,   PlaySeconds = 9.5f },
            new Glyph { Key = "wood", Cho = 0, Jong = 19, Burst = SpellFxBurstBody.Wood, Bite = SpellFxHexAttachPart.HexAttachBite.Blade,    Hold = 6f,   PlaySeconds = 7f },
            new Glyph { Key = "wood", Cho = 0, Jong = 21, Burst = SpellFxBurstBody.Wood, Bite = SpellFxHexAttachPart.HexAttachBite.Orbit,    Hold = 6f,   PlaySeconds = 7f },
            new Glyph { Key = "fire", Cho = 2, Jong = 1,  Burst = SpellFxBurstBody.Fire, Bite = SpellFxHexAttachPart.HexAttachBite.Residue,  Hold = 6f,   PlaySeconds = 7f },
        };

        static string LetterOf(Glyph g) => ((char)(0xAC00 + (g.Cho * 21 + Jung_EO) * 28 + g.Jong)).ToString();

        static string SetupA()
        {
            var log = new StringBuilder();
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, GDir));

            // ---- the sheet materials: the ones setup:f made (reused as they are), else made the same way ----
            var mats = new Dictionary<string, Material>();
            foreach (var g in Glyphs)
            {
                if (mats.ContainsKey(g.Key)) continue;
                var mat = SpellFxSetupF308Commands.EnsureMaterial(g.Key, false, "setup:a", out string problem);
                if (mat == null) return problem;
                mats[g.Key] = mat;
            }
            log.Append("materials ").Append(mats.Count).Append("; ");

            // ---- prefabs: 6 wood glyphs + 넉 ----
            var built = new Dictionary<string, GameObject>();
            foreach (var g in Glyphs)
            {
                string letter = LetterOf(g);
                var go = new GameObject("A_" + letter);
                try
                {
                    var player = go.AddComponent<SpellFxPlayer>();
                    var pso = new SerializedObject(player);
                    pso.FindProperty("playSeconds").floatValue = g.PlaySeconds; pso.FindProperty("tailSeconds").floatValue = 0.5f;
                    pso.ApplyModifiedPropertiesWithoutUndo();
                    var part = go.AddComponent<SpellFxHexAttachPart>();
                    var so = new SerializedObject(part);
                    so.FindProperty("material").objectReferenceValue = mats[g.Key];
                    so.FindProperty("bite").enumValueIndex = (int)g.Bite;
                    so.FindProperty("burstBody").enumValueIndex = (int)g.Burst;          // the END of the cells: the body's own (목 = none, the roots let go)
                    so.FindProperty("holdSeconds").floatValue = g.Hold;                  // the rule's own life (거 0.5 · 걱 3 · 검 8 · the rest 6)
                    so.FindProperty("apexMark").boolValue = g.Apex;                      // 걱 is a 극치 glyph: cells x1.25, the body's main pattern
                    so.ApplyModifiedPropertiesWithoutUndo();
                    built[letter] = PrefabUtility.SaveAsPrefabAsset(go, GDir + "/A_" + letter + ".prefab");
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
            log.Append("prefabs ").Append(built.Count).Append(" (").Append(string.Join("", built.Keys)).Append("); ");

            // ---- catalogue: MERGE - for these glyphs only, variant F is dropped and re-added pointing at the A_ prefab (A, the game's default, stays) ----
            var cat = AssetDatabase.LoadAssetAtPath<SpellFxCatalog>(SpellFxCatalog.AssetPath);
            if (cat == null) { cat = ScriptableObject.CreateInstance<SpellFxCatalog>(); AssetDatabase.CreateAsset(cat, SpellFxCatalog.AssetPath); }
            var before = cat.entries ?? new SpellFxCatalog.Entry[0];
            var list = before.Where(e => e != null && !(e.variant == "F" && built.ContainsKey(e.letter))).ToList();
            foreach (var kv in built) list.Add(new SpellFxCatalog.Entry { letter = kv.Key, variant = "F", prefab = kv.Value });
            cat.entries = list.ToArray();
            EditorUtility.SetDirty(cat); AssetDatabase.SaveAssetIfDirty(cat);
            log.Append("catalogue: ").Append(built.Count).Append(" glyphs x F (A untouched); entries ").Append(before.Length).Append(" -> ").Append(cat.entries.Length);
            return log.ToString();
        }
    }
}
