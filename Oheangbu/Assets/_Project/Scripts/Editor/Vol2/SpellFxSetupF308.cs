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
    /// <summary>2026-10-30 ㅗ 면 (the user: "ㅗ 면 6자로 먼저 시험" -> "87–90점 글자도 같이 바꿈"; round 3 widens the trial to all five bodies): the 30 ㅗ glyphs -
    /// 초성 ㄱ(목) ㄴ(화) ㅁ(토) ㅅ(금) ㅇ(수) x 종성 없음 ㄱ ㄴ ㅁ ㅅ ㅇ = 고 곡 곤 곰 곳 공 and so on - drawn as the hexagon-cell membrane lying on the ground (the ㅜ ward's grammar).
    /// Builds one material per body, Restart/G/M_F_Fan_&lt;wood|fire|earth|metal|water&gt;.mat (a copy of that body's ward material in Restart/U/H - M_U_H_wood / _fire / _earth / _metal, and M_U_H
    /// for water - with the sheet tuning below laid over it, the body's colours kept), one prefab per glyph Restart/G/F_&lt;letter&gt;.prefab (SpellFxPlayer + SpellFxHexFanPart, the body's burst),
    /// and merges the catalogue: for these 30 glyphs variant F AND variant A both point at the F prefab (A is the variant the game plays by default, so the ㅗ glyphs now play the membrane;
    /// the vine prefabs Restart/G/G_&lt;letter&gt;.prefab stay on disk - point A back at them to undo). Every other glyph's entries are untouched.
    /// Needs setup:h to have run (it makes the ward materials). Re-running setup:g would point A of these 30 back at the vine.
    /// Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.SpellFxSetupF308Commands Run "setup:f"
    /// No dialogs. Saves only what it writes.</summary>
    public static class SpellFxSetupF308Commands
    {
        const string Root = "Assets/_Project/Art/SpellVFX308/Restart";
        const string GDir = Root + "/G";
        const string HDir = Root + "/U/H";

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

        sealed class Verb { public SpellFxHexFanPart.HexFanBite Bite; public int Jong; public float PlaySeconds; }

        // 종성 없음 ㄱ ㄴ ㅁ ㅅ ㅇ (Hangul jongseong indices 0 1 4 16 19 21) -> the verb: 한 번 · 남긴다 · 번진다 · 심는다 · 가른다 · 돌아온다
        static readonly Verb[] Verbs =
        {
            new Verb { Jong = 0,  Bite = SpellFxHexFanPart.HexFanBite.Sweep,     PlaySeconds = 4f },
            new Verb { Jong = 1,  Bite = SpellFxHexFanPart.HexFanBite.Thicket,   PlaySeconds = 4.5f },
            new Verb { Jong = 4,  Bite = SpellFxHexFanPart.HexFanBite.FanSpread, PlaySeconds = 4f },
            new Verb { Jong = 16, Bite = SpellFxHexFanPart.HexFanBite.FanSeed,   PlaySeconds = 9f },
            new Verb { Jong = 19, Bite = SpellFxHexFanPart.HexFanBite.FanCut,    PlaySeconds = 4f },
            new Verb { Jong = 21, Bite = SpellFxHexFanPart.HexFanBite.FanReturn, PlaySeconds = 4f },
        };

        const int Jung_O = 8;      // ㅗ

        /// <summary>One body: its 초성 index, the key of its material, the ward material it is copied from, and what its ink spits are made of.</summary>
        sealed class Body { public string Key, Ward; public int Cho; public SpellFxBurstBody Burst; }

        static readonly Body[] Bodies =
        {
            new Body { Key = "wood",  Ward = "M_U_H_wood",  Cho = 0,  Burst = SpellFxBurstBody.Wood },
            new Body { Key = "fire",  Ward = "M_U_H_fire",  Cho = 2,  Burst = SpellFxBurstBody.Fire },
            new Body { Key = "earth", Ward = "M_U_H_earth", Cho = 6,  Burst = SpellFxBurstBody.Earth },
            new Body { Key = "metal", Ward = "M_U_H_metal", Cho = 9,  Burst = SpellFxBurstBody.Metal },
            new Body { Key = "water", Ward = "M_U_H",       Cho = 11, Burst = SpellFxBurstBody.Water },
        };

        static string LetterOf(int cho, int jong) => ((char)(0xAC00 + (cho * 21 + Jung_O) * 28 + jong)).ToString();

        static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        static string Full(string assetPath) => Path.Combine(ProjectRoot, assetPath);

        static string SetupF()
        {
            var log = new StringBuilder();
            Directory.CreateDirectory(Full(GDir));

            // ---- materials: one per body, the body's ward glass with the sheet tuning (round 2, Fable f_*_r1: "wire mesh, not a membrane") laid over it ----
            // Every cell is drawn as TWO layers by the part (hash B = hash A + 1/13.7: the same bloom period half a period out), so the glass alpha below is per layer.
            // The shader's fill = _Glass.a * glassK * sil, glassK = lerp(1, 0.5, facing), sil = lerp(0.25, 1, (1 - |n.v|)^1.5). With the part's shading normal leaned 0.35 toward the
            // camera (|n.v| ~0.55 far .. 0.77 near, a 13 - 30 deg view): far cell ~ 0.8 * 0.97 * 0.47 = 0.36 per layer (0.60 for two), near ~ 0.8 * 0.65 * 0.33 = 0.17 (0.31 for two).
            // Arithmetic from the shader text only - NOT checked against a capture.
            var mats = new Dictionary<string, Material>();
            foreach (var b in Bodies)
            {
                var mat = EnsureMaterial(b.Key, true, "setup:f", out string problem);
                if (mat == null) return problem;
                mats[b.Key] = mat;
            }
            log.Append("materials ").Append(mats.Count).Append("; ");

            // ---- prefabs: 5 bodies x 6 verbs ----
            var built = new Dictionary<string, GameObject>();
            foreach (var b in Bodies)
                foreach (var v in Verbs)
                {
                    string letter = LetterOf(b.Cho, v.Jong);
                    var go = new GameObject("F_" + letter);
                    try
                    {
                        var player = go.AddComponent<SpellFxPlayer>();
                        var pso = new SerializedObject(player);
                        pso.FindProperty("playSeconds").floatValue = v.PlaySeconds; pso.FindProperty("tailSeconds").floatValue = 0.5f;
                        pso.ApplyModifiedPropertiesWithoutUndo();
                        var part = go.AddComponent<SpellFxHexFanPart>();
                        var so = new SerializedObject(part);
                        so.FindProperty("material").objectReferenceValue = mats[b.Key];
                        so.FindProperty("bite").enumValueIndex = (int)v.Bite;
                        so.FindProperty("burstBody").enumValueIndex = (int)b.Burst;      // the ink spits: the body's own (목 = none, the leaves are the look)
                        so.ApplyModifiedPropertiesWithoutUndo();
                        built[letter] = PrefabUtility.SaveAsPrefabAsset(go, GDir + "/F_" + letter + ".prefab");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(go); }
                }
            log.Append("prefabs ").Append(built.Count).Append(" (").Append(string.Join("", built.Keys)).Append("); ");

            // ---- catalogue: MERGE - for these glyphs only, variants F and A are dropped and re-added, both pointing at the F prefab ----
            var cat = AssetDatabase.LoadAssetAtPath<SpellFxCatalog>(SpellFxCatalog.AssetPath);
            if (cat == null) { cat = ScriptableObject.CreateInstance<SpellFxCatalog>(); AssetDatabase.CreateAsset(cat, SpellFxCatalog.AssetPath); }
            var before = cat.entries ?? new SpellFxCatalog.Entry[0];
            var list = before.Where(e => e != null && !((e.variant == "F" || e.variant == "A") && built.ContainsKey(e.letter))).ToList();
            int replacedA = before.Count(e => e != null && e.variant == "A" && built.ContainsKey(e.letter));
            foreach (var kv in built)
            {
                list.Add(new SpellFxCatalog.Entry { letter = kv.Key, variant = "F", prefab = kv.Value });
                list.Add(new SpellFxCatalog.Entry { letter = kv.Key, variant = "A", prefab = kv.Value });
            }
            cat.entries = list.ToArray();
            EditorUtility.SetDirty(cat); AssetDatabase.SaveAssetIfDirty(cat);
            log.Append("catalogue: ").Append(built.Count).Append(" glyphs x (F, A); A replaced where it existed: ").Append(replacedA).Append("; entries ").Append(before.Length).Append(" -> ").Append(cat.entries.Length);
            return log.ToString();
        }

        /// <summary>One body's sheet material, Restart/G/M_F_Fan_&lt;key&gt;.mat: <paramref name="rebuild"/> = false takes the one on disk as it is (the ring setup reuses it); when it is missing, or
        /// <paramref name="rebuild"/>, it is made as a copy of that body's ward material with <see cref="Tune"/> laid over it. null + <paramref name="problem"/> when the ward material is missing.</summary>
        internal static Material EnsureMaterial(string key, bool rebuild, string command, out string problem)
        {
            problem = null;
            Body b = null;
            foreach (var x in Bodies) if (x.Key == key) b = x;
            if (b == null) { problem = "REFUSED " + command + ": unknown body '" + key + "'"; return null; }
            string matPath = GDir + "/M_F_Fan_" + b.Key + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat != null && !rebuild) return mat;
            var ward = AssetDatabase.LoadAssetAtPath<Material>(HDir + "/" + b.Ward + ".mat");
            if (ward == null) { problem = "REFUSED " + command + ": " + HDir + "/" + b.Ward + ".mat missing (run setup:h first)"; return null; }
            Directory.CreateDirectory(Full(GDir));
            if (mat == null) { mat = new Material(ward.shader); AssetDatabase.CreateAsset(mat, matPath); }
            EditorUtility.CopySerialized(ward, mat);
            mat.name = "M_F_Fan_" + b.Key;
            Tune(mat, ward, b.Key == "wood");
            EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat);
            return mat;
        }

        /// <summary>The wood sheet's tuning (the r2 values) on a copy of the ward material. Widths, bleed and pattern settings are the same for every body; the colours keep the body's own:
        /// wood has the exact r2 colours (ink-green fill, cream seam and pattern, jade bleed); the others take the ward's seam, side and pattern colours as they are, a fill that is the ward's
        /// glass darkened by the factor wood's was darkened by (x0.42), and a bleed that is the ward's accent dimmed (x0.26).</summary>
        static void Tune(Material mat, Material ward, bool wood)
        {
            mat.SetFloat("_PatternFrom", 0f);          // the dome keeps its low ring plain; here uv2.y is the distance, patterns from the first cell
            mat.SetFloat("_FacingPattern", 0.85f);     // a floor seen from above is "facing" at a steep view: do not fade its patterns / seams
            mat.SetFloat("_FacingBorder", 0.9f);
            mat.SetFloat("_RiseTime", 0.06f);          // the part sets it again at run time (the cells' own growth is the sweep)
            mat.SetFloat("_HaloA", 0.22f);
            mat.SetFloat("_HaloPx", 2f);             // F4 still: halo / seam widths are in fwidth(edge) units - a floor cell seen edge-on is a few dozen px, so 6 / 3 filled a third of it
            mat.SetFloat("_EdgeW", 0.009f);             // out of the way: the px clamp below decides the seam's width (x2 the wood ward's 0.2 - 0.34)
            mat.SetFloat("_CorePxMin", 0.2f);       // F3 still: x2 on a floor seen edge-on read as a thick cream honeycomb - back to the ward's own widths
            mat.SetFloat("_CorePxMax", 0.34f);
            mat.SetFloat("_SeamPx", 1.2f);
            mat.SetFloat("_SeamA", 0.35f);
            mat.SetFloat("_CoreA", 0.8f);
            mat.SetFloat("_PatternScale", 0.92f);      // a bigger pattern (the cell's inscribed circle is 0.468 of its uv square: 0.92 -> 0.46)
            if (wood)
            {
                mat.SetColor("_Glass", new Color(0.06f, 0.15f, 0.11f, 0.8f));         // dark ink-green fill (the part breathes its alpha +- 10 %)
                mat.SetColor("_Line", new Color(1.4f, 1.3f, 1.08f, 1f));              // cream seam (was mint)
                mat.SetColor("_LineInk", new Color(0.52f, 0.47f, 0.36f, 1f));         // the seam's ink-toned sides, warm
                mat.SetColor("_Pattern", new Color(1.0f, 0.97f, 0.86f, 1f));          // cream pattern lines, full alpha
                mat.SetColor("_Halo", new Color(0.14f, 0.42f, 0.33f, 1f));            // the bleed inside the seam: jade (F2 still: 0.5 x 16 px filled the cells - a teal tiled floor)
                return;
            }
            Color g = ward.GetColor("_Glass"), acc = ward.GetColor("_Accent"), pat = ward.GetColor("_Pattern");
            mat.SetColor("_Glass", new Color(g.r * 0.42f, g.g * 0.42f, g.b * 0.42f, 0.8f));
            mat.SetColor("_Pattern", new Color(pat.r, pat.g, pat.b, 1f));
            mat.SetColor("_Halo", new Color(Mathf.Clamp01(acc.r * 0.26f), Mathf.Clamp01(acc.g * 0.26f), Mathf.Clamp01(acc.b * 0.26f), 1f));
            // _Line and _LineInk stay the ward's own
        }
    }
}
