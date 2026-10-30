using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Oheangbu.App.SpellFx;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>2026-10-30 ㅜ 둘레 (the user: "'ㅜ' 술식과 같은 톤의 술식들을 계속 구현해"; after the ㅗ 면 trial passed its gate): the ring trial set of <see cref="SpellFxHexGlyphs308.TrialSet"/> - the wood glyphs 국 군 굼 굿 궁
    /// and the ordinary 남긴다 of fire, 눅 - drawn as a low ring wall of standing hexagon cells (knee to waist high, one row, gaps between, leaning out - <see cref="SpellFxHexRingPart"/>, the ㅜ ward's grammar;
    /// PLAN-308 §2.2). The letters, bodies and templates come from the glyph table (the template id ring.pillar / ring.spread / ring.seed / ring.cut / ring.return / ring.leave picks the part's verb); the
    /// peak mark (1.25 x cells, the main pattern everywhere, an idle accent seam) is NOT applied here - it belongs to the shared setup. One prefab per glyph Restart/G/R_&lt;letter&gt;.prefab (SpellFxPlayer +
    /// SpellFxHexRingPart, the body's sheet material Restart/G/M_F_Fan_&lt;body&gt;.mat - taken as it is when setup:f made it, else made the way setup:f makes it - and its end), and the catalogue is MERGED:
    /// variant F of these glyphs is dropped and re-added pointing at the R prefab; variant A (the vine ring, Restart/G/G_&lt;letter&gt;) is NOT touched - point A at the R prefabs after the gate. Every other
    /// glyph's entries are untouched. Needs setup:h (the ward materials; only when a sheet material is missing).
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

        static string KeyOf(SpellFxBurstBody b)
        {
            switch (b) { case SpellFxBurstBody.Wood: return "wood"; case SpellFxBurstBody.Fire: return "fire"; case SpellFxBurstBody.Earth: return "earth"; case SpellFxBurstBody.Metal: return "metal"; default: return "water"; }
        }

        /// <summary>The part's verb for a glyph's template id, and the length the prefab plays: the part's own life (about 10.3 s / 3.7 s + the road's 3 s marks, to ~6 s for a late crossing / 8.6 s / 10.3 s / 3.4 s) + its tail.</summary>
        static bool BiteOf(string templateId, out SpellFxHexRingPart.HexRingBite bite, out float play)
        {
            switch (templateId)
            {
                case "ring.pillar": bite = SpellFxHexRingPart.HexRingBite.Pillar; play = 11f; return true;
                case "ring.leave": bite = SpellFxHexRingPart.HexRingBite.Leave; play = 11f; return true;
                case "ring.spread": bite = SpellFxHexRingPart.HexRingBite.Spread; play = 6.5f; return true;
                case "ring.seed": bite = SpellFxHexRingPart.HexRingBite.Seed; play = 9.5f; return true;
                case "ring.cut": bite = SpellFxHexRingPart.HexRingBite.Cut; play = 11f; return true;
                case "ring.return": bite = SpellFxHexRingPart.HexRingBite.Return; play = 4.5f; return true;
                default: bite = SpellFxHexRingPart.HexRingBite.Pillar; play = 0f; return false;
            }
        }

        static string SetupR()
        {
            var log = new StringBuilder();
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, GDir));
            var glyphs = SpellFxHexGlyphs308.TrialSet(HexGlyphFrame.Ring).ToList();

            // ---- the sheet materials of the bodies the set uses: the ones setup:f made (reused as they are), else made the same way ----
            var mats = new Dictionary<string, Material>();
            foreach (var g in glyphs)
            {
                string key = KeyOf(g.Body);
                if (mats.ContainsKey(key)) continue;
                var mat = SpellFxSetupF308Commands.EnsureMaterial(key, false, "setup:r", out string problem);
                if (mat == null) return problem;
                mats[key] = mat;
            }
            log.Append("materials ").Append(mats.Count).Append(" (").Append(string.Join(" ", mats.Keys)).Append("); ");

            // ---- prefabs: one per glyph of the set ----
            var built = new Dictionary<string, GameObject>();
            foreach (var g in glyphs)
            {
                if (!BiteOf(g.TemplateId, out var bite, out float play)) { log.Append("SKIPPED ").Append(g.Letter).Append(" (template ").Append(g.TemplateId).Append("); "); continue; }
                string letter = g.Letter.ToString();
                var go = new GameObject("R_" + letter);
                try
                {
                    var player = go.AddComponent<SpellFxPlayer>();
                    var pso = new SerializedObject(player);
                    pso.FindProperty("playSeconds").floatValue = play; pso.FindProperty("tailSeconds").floatValue = 0.5f;
                    pso.ApplyModifiedPropertiesWithoutUndo();
                    var part = go.AddComponent<SpellFxHexRingPart>();
                    var so = new SerializedObject(part);
                    so.FindProperty("material").objectReferenceValue = mats[KeyOf(g.Body)];
                    so.FindProperty("bite").enumValueIndex = (int)bite;
                    so.FindProperty("burstBody").enumValueIndex = (int)g.Body;      // the END: the body's own (HexBodyEnd; 목 = the cell narrows, short ink strands, small drops)
                    so.ApplyModifiedPropertiesWithoutUndo();
                    built[letter] = PrefabUtility.SaveAsPrefabAsset(go, GDir + "/R_" + letter + ".prefab");
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
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
