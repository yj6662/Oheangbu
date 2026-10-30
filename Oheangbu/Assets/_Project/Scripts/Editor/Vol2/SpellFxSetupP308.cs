using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Oheangbu.App.SpellFx;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>2026-10-30 ㅏ 경로 (lane D of PLAN-308-MEMBRANE-SPELLS, section 2.4; the user: "'ㅜ' 술식과 같은 톤의 술식들을 계속 구현해"): the six ㅏ glyphs of the wood body - 가 각 간 감 갓 강
    /// (초성 ㄱ x 종성 없음 ㄱ ㄴ ㅁ ㅅ ㅇ) - and 낙 (fire, 초성 ㄴ + ㅏ + ㄱ: the plain 'leave it' verb, the glyph that shows the general residue template) drawn as a chain of hexagon cells shot from the brush tip
    /// (<see cref="SpellFxHexPathPart"/>). One prefab per glyph Restart/G/P_&lt;letter&gt;.prefab (SpellFxPlayer + SpellFxHexPathPart, the body's sheet material Restart/G/M_F_Fan_&lt;body&gt;.mat - taken as it
    /// is when setup:f made it, else made the way setup:f makes it - and its end burst). The catalogue is MERGED: variant F of these glyphs is dropped and re-added pointing at the P prefab; variant A
    /// (the vine, Restart/G/G_&lt;letter&gt;) is NOT touched. Every other glyph's entries are untouched.
    /// Needs setup:h (the ward materials; only when a sheet material is missing).
    /// Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.SpellFxSetupP308Commands Run "setup:p"
    /// No dialogs. Saves only what it writes.</summary>
    public static class SpellFxSetupP308Commands
    {
        const string Root = "Assets/_Project/Art/SpellVFX308/Restart";
        const string GDir = Root + "/G";

        public static string Run(string command)
        {
            try
            {
                var c = (command ?? "").Trim();
                if (!string.Equals(c, "setup:p", StringComparison.OrdinalIgnoreCase)) return "REFUSED setup: setup:p";
                return SetupP();
            }
            catch (Exception e) { return "EXCEPTION " + e.GetType().Name + " " + e.Message; }
        }

        const int Jung_A = 0;      // ㅏ

        /// <summary>One glyph: its body (material key), 초성 / 종성 index, the verb and the prefab's play length (the part's own life + its tail).</summary>
        sealed class Glyph { public string Body; public int Cho, Jong; public SpellFxHexPathPart.HexPathBite Bite; public float PlaySeconds; }

        // 초성 ㄱ0 ㄴ2, 종성 없음0 ㄱ1 ㄴ4 ㅁ16 ㅅ19 ㅇ21 -> 한 번 · 남긴다(극치) · 번진다 · 심는다 · 가른다 · 돌아온다 (낙 = fire's plain 남긴다). Play: 각 · 낙 stand 10 s (+ the 2 s of the chase), 감 waits 8 s.
        static readonly Glyph[] Glyphs =
        {
            new Glyph { Body = "wood", Cho = 0, Jong = 0,  Bite = SpellFxHexPathPart.HexPathBite.Once,    PlaySeconds = 4f },
            new Glyph { Body = "wood", Cho = 0, Jong = 1,  Bite = SpellFxHexPathPart.HexPathBite.Cage,    PlaySeconds = 11f },
            new Glyph { Body = "wood", Cho = 0, Jong = 4,  Bite = SpellFxHexPathPart.HexPathBite.Spread,  PlaySeconds = 4f },
            new Glyph { Body = "wood", Cho = 0, Jong = 16, Bite = SpellFxHexPathPart.HexPathBite.Seed,    PlaySeconds = 9f },
            new Glyph { Body = "wood", Cho = 0, Jong = 19, Bite = SpellFxHexPathPart.HexPathBite.Cut,     PlaySeconds = 4f },
            new Glyph { Body = "wood", Cho = 0, Jong = 21, Bite = SpellFxHexPathPart.HexPathBite.Return,  PlaySeconds = 4f },
            new Glyph { Body = "fire", Cho = 2, Jong = 1,  Bite = SpellFxHexPathPart.HexPathBite.Residue, PlaySeconds = 11f },
        };

        /// <summary>What a body's END burst is made of (cells breaking up; wood: none).</summary>
        static SpellFxBurstBody BurstOf(string body)
        {
            switch (body)
            {
                case "fire": return SpellFxBurstBody.Fire;
                case "earth": return SpellFxBurstBody.Earth;
                case "metal": return SpellFxBurstBody.Metal;
                case "water": return SpellFxBurstBody.Water;
                default: return SpellFxBurstBody.Wood;
            }
        }

        static string LetterOf(int cho, int jong) => ((char)(0xAC00 + (cho * 21 + Jung_A) * 28 + jong)).ToString();

        static string SetupP()
        {
            var log = new StringBuilder();
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, GDir));

            // ---- the sheet materials: the ones setup:f made (reused as they are), else made the same way ----
            var mats = new Dictionary<string, Material>();
            foreach (var g in Glyphs)
            {
                if (mats.ContainsKey(g.Body)) continue;
                var mat = SpellFxSetupF308Commands.EnsureMaterial(g.Body, false, "setup:p", out string problem);
                if (mat == null) return problem;
                mats[g.Body] = mat;
            }
            log.Append("materials ").Append(mats.Count).Append("; ");

            // ---- prefabs: one per glyph ----
            var built = new Dictionary<string, GameObject>();
            foreach (var g in Glyphs)
            {
                string letter = LetterOf(g.Cho, g.Jong);
                var go = new GameObject("P_" + letter);
                try
                {
                    var player = go.AddComponent<SpellFxPlayer>();
                    var pso = new SerializedObject(player);
                    pso.FindProperty("playSeconds").floatValue = g.PlaySeconds; pso.FindProperty("tailSeconds").floatValue = 0.5f;
                    pso.ApplyModifiedPropertiesWithoutUndo();
                    var part = go.AddComponent<SpellFxHexPathPart>();
                    var so = new SerializedObject(part);
                    so.FindProperty("material").objectReferenceValue = mats[g.Body];
                    so.FindProperty("bite").enumValueIndex = (int)g.Bite;
                    so.FindProperty("burstBody").enumValueIndex = (int)BurstOf(g.Body);      // the END burst: the body's own (목 = none, the leaves are the look)
                    so.ApplyModifiedPropertiesWithoutUndo();
                    built[letter] = PrefabUtility.SaveAsPrefabAsset(go, GDir + "/P_" + letter + ".prefab");
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
            log.Append("prefabs ").Append(built.Count).Append(" (").Append(string.Join("", built.Keys)).Append("); ");

            // ---- catalogue: MERGE - for these glyphs only, variant F is dropped and re-added pointing at the P prefab (A, the vine, stays) ----
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
