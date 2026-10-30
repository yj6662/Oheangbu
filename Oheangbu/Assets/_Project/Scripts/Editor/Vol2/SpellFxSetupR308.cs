using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Oheangbu.App.SpellFx;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>2026-10-30 ㅜ 둘레 (the user: "'ㅜ' 술식과 같은 톤의 술식들을 계속 구현해"; after the ㅗ 면 trial passed its gate): the five ㅜ + final glyphs of the wood body - 국 군 굼 굿 궁 (초성 ㄱ x 종성 ㄱ ㄴ ㅁ ㅅ ㅇ) -
    /// drawn as a low ring wall of standing hexagon cells (knee to waist high, one row, gaps between, leaning out - <see cref="SpellFxHexRingPart"/>, the ㅜ ward's grammar; PLAN-308 §2.2). One prefab per glyph Restart/G/R_&lt;letter&gt;.prefab (SpellFxPlayer + SpellFxHexRingPart, the body's
    /// sheet material Restart/G/M_F_Fan_&lt;body&gt;.mat - taken as it is when setup:f made it, else made the way setup:f makes it - and its end burst), and the catalogue is MERGED: variant F of these glyphs
    /// is dropped and re-added pointing at the R prefab; variant A (the vine ring, Restart/G/G_&lt;letter&gt;) is NOT touched - point A at the R prefabs after the gate. Every other glyph's entries are untouched.
    /// The part takes any of the five bodies (material + burstBody on the prefab); only wood is built now (<see cref="Enabled"/>): add a key there to build another body's five.
    /// Needs setup:h (the ward materials; only when a sheet material is missing).
    /// Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.SpellFxSetupR308Commands Run "setup:r"
    /// No dialogs. Saves only what it writes.</summary>
    public static class SpellFxSetupR308Commands
    {
        const string Root = "Assets/_Project/Art/SpellVFX308/Restart";
        const string GDir = Root + "/G";

        public static string Run(string command)
        {
            try
            {
                var c = (command ?? "").Trim();
                if (!string.Equals(c, "setup:r", StringComparison.OrdinalIgnoreCase)) return "REFUSED setup: setup:r";
                return SetupR();
            }
            catch (Exception e) { return "EXCEPTION " + e.GetType().Name + " " + e.Message; }
        }

        sealed class Verb { public SpellFxHexRingPart.HexRingBite Bite; public int Jong; public float PlaySeconds; }

        // 종성 ㄱ ㄴ ㅁ ㅅ ㅇ (Hangul jongseong indices 1 4 16 19 21) -> the verb: 극치 남긴다 · 번진다 · 심는다 · 가른다 · 돌아온다. The play length is the part's own life (about 10.3 s / 3.7 s + the crawl marks, to ~6 s
        // for a late crossing / 8.6 s / 10.3 s / 2.7 s) + its tail.
        static readonly Verb[] Verbs =
        {
            new Verb { Jong = 1,  Bite = SpellFxHexRingPart.HexRingBite.Pillar, PlaySeconds = 11f },
            new Verb { Jong = 4,  Bite = SpellFxHexRingPart.HexRingBite.Spread, PlaySeconds = 6.5f },
            new Verb { Jong = 16, Bite = SpellFxHexRingPart.HexRingBite.Seed,   PlaySeconds = 9.5f },
            new Verb { Jong = 19, Bite = SpellFxHexRingPart.HexRingBite.Cut,    PlaySeconds = 11f },
            new Verb { Jong = 21, Bite = SpellFxHexRingPart.HexRingBite.Return, PlaySeconds = 4f },
        };

        const int Jung_U = 13;      // ㅜ

        /// <summary>One body: its 초성 index, the key of its material, and what its end burst is made of.</summary>
        sealed class Body { public string Key; public int Cho; public SpellFxBurstBody Burst; }

        static readonly Body[] Bodies =
        {
            new Body { Key = "wood",  Cho = 0,  Burst = SpellFxBurstBody.Wood },
            new Body { Key = "fire",  Cho = 2,  Burst = SpellFxBurstBody.Fire },
            new Body { Key = "earth", Cho = 6,  Burst = SpellFxBurstBody.Earth },
            new Body { Key = "metal", Cho = 9,  Burst = SpellFxBurstBody.Metal },
            new Body { Key = "water", Cho = 11, Burst = SpellFxBurstBody.Water },
        };

        /// <summary>The bodies built now. The part and this table are ready for the other four (add "fire", "earth", "metal", "water") - not built until the wood ring has passed its gate.</summary>
        static readonly string[] Enabled = { "wood" };

        static string LetterOf(int cho, int jong) => ((char)(0xAC00 + (cho * 21 + Jung_U) * 28 + jong)).ToString();

        static string SetupR()
        {
            var log = new StringBuilder();
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, GDir));

            // ---- the sheet materials: the ones setup:f made (reused as they are), else made the same way ----
            var mats = new Dictionary<string, Material>();
            foreach (var b in Bodies)
            {
                if (!Enabled.Contains(b.Key)) continue;
                var mat = SpellFxSetupF308Commands.EnsureMaterial(b.Key, false, "setup:r", out string problem);
                if (mat == null) return problem;
                mats[b.Key] = mat;
            }
            log.Append("materials ").Append(mats.Count).Append("; ");

            // ---- prefabs: the enabled bodies x 5 verbs ----
            var built = new Dictionary<string, GameObject>();
            foreach (var b in Bodies)
            {
                if (!Enabled.Contains(b.Key)) continue;
                foreach (var v in Verbs)
                {
                    string letter = LetterOf(b.Cho, v.Jong);
                    var go = new GameObject("R_" + letter);
                    try
                    {
                        var player = go.AddComponent<SpellFxPlayer>();
                        var pso = new SerializedObject(player);
                        pso.FindProperty("playSeconds").floatValue = v.PlaySeconds; pso.FindProperty("tailSeconds").floatValue = 0.5f;
                        pso.ApplyModifiedPropertiesWithoutUndo();
                        var part = go.AddComponent<SpellFxHexRingPart>();
                        var so = new SerializedObject(part);
                        so.FindProperty("material").objectReferenceValue = mats[b.Key];
                        so.FindProperty("bite").enumValueIndex = (int)v.Bite;
                        so.FindProperty("burstBody").enumValueIndex = (int)b.Burst;      // the END burst: the body's own (목 = none, the leaves are the look)
                        so.ApplyModifiedPropertiesWithoutUndo();
                        built[letter] = PrefabUtility.SaveAsPrefabAsset(go, GDir + "/R_" + letter + ".prefab");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(go); }
                }
            }
            log.Append("prefabs ").Append(built.Count).Append(" (").Append(string.Join("", built.Keys)).Append("); ");

            // ---- catalogue: MERGE - for these glyphs only, variant F is dropped and re-added pointing at the R prefab (A, the vine ring, stays) ----
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
