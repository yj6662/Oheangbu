using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Oheangbu.App.SpellFx;
using UnityEditor;
using UnityEngine;
using RuleBody = Oheangbu.Vol2.Rules.Body;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>2026-10-31 ㅏ 경로 as ink brush strokes (D308-161 / D308-162, PLAN-308-INK-STROKE-SPELLS 4.2; the user: "방어는 그런 육각면으로 하고, 공격은 다른 투사체를 사용해야지" -> "구현 시작, 97은 영상에서"):
    /// nine glyphs drawn by <see cref="SpellFxInkPathPart"/> - the wood ㅏ verbs 가 각 간 갓 강 (한 번 · 남긴다(극치) · 번진다 · 가른다 · 돌아온다) and the 한 번 of the other four bodies 나 마 사 아.
    /// One prefab per glyph Restart/N/N_&lt;letter&gt;.prefab (shader A) or Restart/N/O_&lt;letter&gt;.prefab (shader B): SpellFxPlayer + SpellFxInkPathPart with the body's material
    /// Restart/N/M_InkStroke&lt;A|B&gt;_&lt;body&gt;.mat (the shader's ink colours and the body's ONE accent, HDR). The catalogue is MERGED: variant N (A) or O (B) of these glyphs is dropped and re-added;
    /// every other entry (A the vine, F the hexagons, ...) is NOT touched.
    /// Commands: setup:n (shader A -> variant N) · setup:n:b (shader B -> variant O) · setup:n:rebuild / setup:n:b:rebuild (the materials rewritten; without it a material on disk is kept as it is
    /// - a hand-tuned one survives a re-run - only its shader is put right).
    /// Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.SpellFxSetupN308Commands Run "setup:n"
    /// No dialogs. Saves only what it writes. REFUSED when the shader is missing, unsupported or has compile errors.</summary>
    public static class SpellFxSetupN308Commands
    {
        const string Root = "Assets/_Project/Art/SpellVFX308/Restart";
        const string NDir = Root + "/N";
        const string ShaderA = "Oheangbu/SpellFx/InkStrokeA", ShaderB = "Oheangbu/SpellFx/InkStrokeB";

        public static string Run(string command)
        {
            try
            {
                var c = (command ?? "").Trim().ToLowerInvariant();
                switch (c)
                {
                    case "setup:n": return Setup(false, false, c);
                    case "setup:n:b": return Setup(true, false, c);
                    case "setup:n:rebuild": return Setup(false, true, c);
                    case "setup:n:b:rebuild": return Setup(true, true, c);
                }
                return "REFUSED setup: setup:n | setup:n:b | setup:n:rebuild | setup:n:b:rebuild";
            }
            catch (Exception e) { return "EXCEPTION " + e.GetType().Name + " " + e.Message; }
        }

        /// <summary>One glyph: its letter, body, verb, the 극치 mark and the prefab's play length (the part's own life + its end).</summary>
        sealed class Glyph { public string Letter; public RuleBody Body; public SpellFxInkPathPart.InkPathBite Bite; public bool Peak; public float PlaySeconds; }

        // 가 각 간 갓 강 = 초성 ㄱ + ㅏ + 종성 없음 · ㄱ · ㄴ · ㅅ · ㅇ (wood); 나 마 사 아 = 초성 ㄴ ㅁ ㅅ ㅇ + ㅏ, 한 번 (fire, earth, metal, water). 감 (심는다) waits for its design decision (judge concept_r2 5 (b)).
        // Play: 각 stands 10 s (+ the rise and the end: 11.5), the rest 4 (Tl ~0.25 + the 2 s grip + the end; 간's bleed 3 s from ~Tl + 0.2).
        static readonly Glyph[] Glyphs =
        {
            new Glyph { Letter = "가", Body = RuleBody.Wood,  Bite = SpellFxInkPathPart.InkPathBite.Once,   PlaySeconds = 4f },
            new Glyph { Letter = "각", Body = RuleBody.Wood,  Bite = SpellFxInkPathPart.InkPathBite.Cage,   PlaySeconds = 11.5f, Peak = true },
            new Glyph { Letter = "간", Body = RuleBody.Wood,  Bite = SpellFxInkPathPart.InkPathBite.Spread, PlaySeconds = 4f },
            new Glyph { Letter = "갓", Body = RuleBody.Wood,  Bite = SpellFxInkPathPart.InkPathBite.Cut,    PlaySeconds = 4f },
            new Glyph { Letter = "강", Body = RuleBody.Wood,  Bite = SpellFxInkPathPart.InkPathBite.Return, PlaySeconds = 4f },
            new Glyph { Letter = "나", Body = RuleBody.Fire,  Bite = SpellFxInkPathPart.InkPathBite.Once,   PlaySeconds = 4f },
            new Glyph { Letter = "마", Body = RuleBody.Earth, Bite = SpellFxInkPathPart.InkPathBite.Once,   PlaySeconds = 4f },
            new Glyph { Letter = "사", Body = RuleBody.Metal, Bite = SpellFxInkPathPart.InkPathBite.Once,   PlaySeconds = 4f },
            new Glyph { Letter = "아", Body = RuleBody.Water, Bite = SpellFxInkPathPart.InkPathBite.Once,   PlaySeconds = 4f },
        };

        static string KeyOf(RuleBody b)
        {
            switch (b)
            {
                case RuleBody.Fire: return "fire";
                case RuleBody.Earth: return "earth";
                case RuleBody.Metal: return "metal";
                case RuleBody.Water: return "water";
                default: return "wood";
            }
        }

        /// <summary>The body's ONE accent (PLAN 1.3; TEST): 목 옥 · 화 주홍 · 토 황토 · 금 금백 · 수 청백, HDR so the core line and the sparks may glow (D308-150 V11).</summary>
        static Color AccentOf(RuleBody b)
        {
            switch (b)
            {
                case RuleBody.Fire: return new Color(0.91f, 0.35f, 0.18f, 1f) * 2.4f;
                case RuleBody.Earth: return new Color(0.78f, 0.63f, 0.31f, 1f) * 1.8f;
                case RuleBody.Metal: return new Color(0.95f, 0.88f, 0.62f, 1f) * 2.6f;
                case RuleBody.Water: return new Color(0.62f, 0.82f, 1f, 1f) * 2.2f;
                default: return new Color(0.37f, 0.75f, 0.56f, 1f) * 2.2f;
            }
        }

        static readonly Color InkWet = new Color(0.055f, 0.051f, 0.047f, 1f);   // #0E0D0C
        static readonly Color InkDry = new Color(0.2f, 0.184f, 0.169f, 1f);     // #332F2B

        static string Setup(bool shaderB, bool rebuild, string command)
        {
            var log = new StringBuilder();
            string shaderName = shaderB ? ShaderB : ShaderA, tag = shaderB ? "B" : "A", variant = shaderB ? "O" : "N", prefix = shaderB ? "O_" : "N_";
            var shader = Shader.Find(shaderName);
            if (shader == null) return "REFUSED " + command + ": shader '" + shaderName + "' not found (not imported yet?)";
            if (!shader.isSupported) return "REFUSED " + command + ": shader '" + shaderName + "' is not supported on this platform";
            if (ShaderUtil.ShaderHasError(shader)) return "REFUSED " + command + ": shader '" + shaderName + "' has compile errors";
            if (!AssetDatabase.IsValidFolder(NDir)) AssetDatabase.CreateFolder(Root, "N");
            if (!AssetDatabase.IsValidFolder(NDir)) return "REFUSED " + command + ": could not make " + NDir;

            // ---- materials: one per body ----
            var mats = new Dictionary<RuleBody, Material>();
            int made = 0, kept = 0, rewritten = 0;
            foreach (var g in Glyphs)
            {
                if (mats.ContainsKey(g.Body)) continue;
                string path = NDir + "/M_InkStroke" + tag + "_" + KeyOf(g.Body) + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool fresh = mat == null;
                if (fresh) { mat = new Material(shader) { name = "M_InkStroke" + tag + "_" + KeyOf(g.Body) }; AssetDatabase.CreateAsset(mat, path); made++; }
                else if (mat.shader != shader) mat.shader = shader;
                if (fresh || rebuild)
                {
                    SetColorIfAny(mat, "_InkWet", InkWet);
                    SetColorIfAny(mat, "_InkDry", InkDry);
                    SetColorIfAny(mat, "_Accent", AccentOf(g.Body));
                    if (!fresh) rewritten++;
                }
                else kept++;
                EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat);
                mats[g.Body] = mat;
            }
            log.Append("materials ").Append(mats.Count).Append(" (").Append(shaderName).Append("; made ").Append(made).Append(", kept ").Append(kept).Append(", rewritten ").Append(rewritten).Append("); ");

            // ---- prefabs: one per glyph ----
            var built = new Dictionary<string, GameObject>();
            foreach (var g in Glyphs)
            {
                var go = new GameObject(prefix + g.Letter);
                try
                {
                    var player = go.AddComponent<SpellFxPlayer>();
                    var pso = new SerializedObject(player);
                    pso.FindProperty("playSeconds").floatValue = g.PlaySeconds; pso.FindProperty("tailSeconds").floatValue = 0.5f;
                    pso.ApplyModifiedPropertiesWithoutUndo();
                    var part = go.AddComponent<SpellFxInkPathPart>();
                    var so = new SerializedObject(part);
                    so.FindProperty("material").objectReferenceValue = mats[g.Body];
                    so.FindProperty("body").enumValueIndex = (int)g.Body;            // enum order = declaration order (Wood Fire Earth Metal Water)
                    so.FindProperty("bite").enumValueIndex = (int)g.Bite;            // Once Cage Spread Cut Return (append only)
                    so.FindProperty("peak").boolValue = g.Peak;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    built[g.Letter] = PrefabUtility.SaveAsPrefabAsset(go, NDir + "/" + prefix + g.Letter + ".prefab");
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
            log.Append("prefabs ").Append(built.Count).Append(" (").Append(string.Join("", built.Keys)).Append("); ");

            // ---- catalogue: MERGE - for these glyphs only, this variant is dropped and re-added (every other variant stays) ----
            var cat = AssetDatabase.LoadAssetAtPath<SpellFxCatalog>(SpellFxCatalog.AssetPath);
            if (cat == null) { cat = ScriptableObject.CreateInstance<SpellFxCatalog>(); AssetDatabase.CreateAsset(cat, SpellFxCatalog.AssetPath); }
            var before = cat.entries ?? new SpellFxCatalog.Entry[0];
            var list = before.Where(e => e != null && !(e.variant == variant && built.ContainsKey(e.letter))).ToList();
            foreach (var kv in built) list.Add(new SpellFxCatalog.Entry { letter = kv.Key, variant = variant, prefab = kv.Value });
            cat.entries = list.ToArray();
            EditorUtility.SetDirty(cat); AssetDatabase.SaveAssetIfDirty(cat);
            log.Append("catalogue: ").Append(built.Count).Append(" glyphs x ").Append(variant).Append(" (other variants untouched); entries ").Append(before.Length).Append(" -> ").Append(cat.entries.Length);
            if (!cat.Validate(out string problems)) log.Append("; catalogue problems: ").Append(problems);
            return log.ToString();
        }

        static void SetColorIfAny(Material m, string prop, Color c) { if (m.HasProperty(prop)) m.SetColor(prop, c); }
    }
}
