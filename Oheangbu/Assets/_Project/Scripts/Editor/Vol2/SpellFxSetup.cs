using System;
using System.IO;
using System.Linq;
using Oheangbu.App.SpellFx;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>D308-153: builds the 우 comparison assets of the restart - the catalogue, and one prefab per route:
    ///   A = Cycles-rendered frame sequences (far / near) as Texture2DArrays on cards (SpellFxCardsPart, pitch 0),
    ///   B = the concept u_a cut into two painted stills on cards (SpellFxCardsPart, pitch 17.4 - the concept looks down),
    ///   C = the live in-engine wave ring (SpellFxRingWavePart + Oheangbu/SpellFx/Water),
    ///   P = the pattern ring (SpellFxPatternRingPart + Oheangbu/SpellFx/Pattern, D308-153b): seigaiha wall, crest cards, ink ribbons, a burst on contact. A is record-only now.
    /// Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.SpellFxSetupCommands Run "setup:u[:a=<frames dir with far/ near/>]"   or   "setup:u:p" (only P)
    /// The catalogue is MERGED: only the variants built in the run are replaced. No dialogs. Saves only the assets it writes (AssetDatabase.SaveAssetIfDirty per asset).</summary>
    public static class SpellFxSetupCommands
    {
        const string Root = "Assets/_Project/Art/SpellVFX308/Restart";
        const string UDir = Root + "/U";

        public static string Run(string command)
        {
            try
            {
                var c = (command ?? "").Trim();
                if (!c.StartsWith("setup:u", StringComparison.Ordinal)) return "REFUSED setup: setup:u[:a=<dir>] | setup:u:p";
                // setup:u           builds B, C and P (A only with a=<dir>: A is record-only now) and MERGES them into the catalogue
                // setup:u:p         builds only P
                // setup:u:a=<dir>   the rest of the string is the path (it may hold a drive colon)
                string rest = c.Substring("setup:u".Length).TrimStart(':');
                bool onlyP = string.Equals(rest, "p", StringComparison.OrdinalIgnoreCase);
                string aDir = rest.StartsWith("a=", StringComparison.OrdinalIgnoreCase) ? rest.Substring(2) : null;
                if (rest.Length > 0 && !onlyP && aDir == null) return "REFUSED setup: unknown option '" + rest + "' (setup:u[:a=<dir>] | setup:u:p)";
                return SetupU(aDir, onlyP);
            }
            catch (Exception e) { return "EXCEPTION " + e.GetType().Name + " " + e.Message; }
        }

        static string SetupU(string aDir, bool onlyP)
        {
            Directory.CreateDirectory(UDir + "/A"); Directory.CreateDirectory(UDir + "/B"); Directory.CreateDirectory(UDir + "/C"); Directory.CreateDirectory(UDir + "/P");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var log = new System.Text.StringBuilder();
            var cardShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/SpellFxCard.shader");
            var waterShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/SpellFxWater.shader");
            var patternShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/SpellFxPatternWave.shader");      // Fable rebuild from uks_1 (2026-10-29): the wave-screen shader
            if (!onlyP && (cardShader == null || waterShader == null)) return "REFUSED setup:u: shaders missing under " + Root + "/Shaders";
            if (patternShader == null) return "REFUSED setup:u: " + Root + "/Shaders/SpellFxPatternWave.shader missing";

            // ---- B: the two stills ----
            Texture2D bFar = onlyP ? null : Still(UDir + "/B/u_b_far.png"), bNear = onlyP ? null : Still(UDir + "/B/u_b_near.png");
            GameObject pB = null;
            if (onlyP) { }
            else if (bFar != null && bNear != null)
            {
                pB = Prefab(UDir + "/U_B.prefab", 2.6f, go =>
                {
                    var part = go.AddComponent<SpellFxCardsPart>();
                    var so = new SerializedObject(part);
                    so.FindProperty("shader").objectReferenceValue = cardShader;
                    so.FindProperty("pitch").floatValue = 17.4f;
                    var cards = so.FindProperty("cards"); cards.arraySize = 2;
                    SetCard(cards.GetArrayElementAtIndex(0), "Far", bFar, null, 3.6f, 0.4f);
                    SetCard(cards.GetArrayElementAtIndex(1), "Near", bNear, null, 1.3f, 0.0f);
                    so.ApplyModifiedPropertiesWithoutUndo();
                });
                log.Append("B ok; ");
            }
            else log.Append("B: stills missing (" + UDir + "/B/u_b_far.png, u_b_near.png); ");

            // ---- A: the rendered sequences ----
            GameObject pA = null;
            // r2: one colour card + its depth (the render's view depth per pixel; the card tests it against the scene) - r1's far/near cards hid the bear
            if (onlyP) { }
            else if (!string.IsNullOrEmpty(aDir) && Directory.Exists(Path.Combine(aDir, "col")))
            {
                var col = Sequence(Path.Combine(aDir, "col"), UDir + "/A/u_a_col.asset", false);
                var dep = Sequence(Path.Combine(aDir, "dep"), UDir + "/A/u_a_dep.asset", true);
                if (col != null && dep != null)
                {
                    pA = Prefab(UDir + "/U_A.prefab", Mathf.Max(2.6f, col.depth / 30f), go =>
                    {
                        var part = go.AddComponent<SpellFxCardsPart>();
                        var so = new SerializedObject(part);
                        so.FindProperty("shader").objectReferenceValue = cardShader;
                        so.FindProperty("pitch").floatValue = 0f;
                        var cards = so.FindProperty("cards"); cards.arraySize = 1;
                        var c0 = cards.GetArrayElementAtIndex(0);
                        SetCard(c0, "All", null, col, 0.6f, 0.5f);
                        c0.FindPropertyRelative("DepthSequence").objectReferenceValue = dep;
                        c0.FindPropertyRelative("DepthRange").floatValue = 12f;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    });
                    log.Append("A ok (" + col.depth + " frames " + col.width + "x" + col.height + " + depth); ");
                }
                else log.Append("A: no frames read; ");
            }
            else log.Append("A: skipped (no a=<dir>); ");

            // ---- C: the live ring ----
            GameObject pC = null;
            if (!onlyP)
            {
                pC = Prefab(UDir + "/U_C.prefab", 2.6f, go =>
                {
                    var part = go.AddComponent<SpellFxRingWavePart>();
                    var so = new SerializedObject(part);
                    so.FindProperty("waterShader").objectReferenceValue = waterShader;
                    so.ApplyModifiedPropertiesWithoutUndo();
                });
                log.Append("C ok; ");
            }

            // ---- P: the pattern wave screen (D308-153b; Fable rebuild from uks_1, 2026-10-29: SpellFxPatternWavesPart - the silhouette is cut in the shader, only the ink stroke is a texture) ----
            var ink = InkStroke(UDir + "/P/ink_stroke.png");
            GameObject pP = null;
            if (ink != null)
            {
                pP = Prefab(UDir + "/U_P.prefab", 1.8f, go =>
                {
                    var player = go.GetComponent<SpellFxPlayer>();
                    var pso = new SerializedObject(player); pso.FindProperty("tailSeconds").floatValue = 1.2f; pso.ApplyModifiedPropertiesWithoutUndo();      // a hit late in the life still plays its wave
                    var part = go.AddComponent<SpellFxPatternWavesPart>();
                    var so = new SerializedObject(part);
                    so.FindProperty("patternShader").objectReferenceValue = patternShader;
                    so.FindProperty("inkTex").objectReferenceValue = ink;
                    so.FindProperty("radius").floatValue = 2.2f;
                    so.FindProperty("lifeSeconds").floatValue = 1.8f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                });
                log.Append("P ok; ");
            }
            else log.Append("P: texture missing under " + UDir + "/P (ink_stroke); ");

            // ---- catalogue: MERGE - only the variants built in this run are replaced, every other 우 variant (and every other letter) stays ----
            var cat = AssetDatabase.LoadAssetAtPath<SpellFxCatalog>(SpellFxCatalog.AssetPath);
            if (cat == null) { cat = ScriptableObject.CreateInstance<SpellFxCatalog>(); AssetDatabase.CreateAsset(cat, SpellFxCatalog.AssetPath); }
            var built = new System.Collections.Generic.Dictionary<string, GameObject>();
            if (pA != null) built["A"] = pA;
            if (pB != null) built["B"] = pB;
            if (pC != null) built["C"] = pC;
            if (pP != null) built["P"] = pP;
            var list = (cat.entries ?? new SpellFxCatalog.Entry[0]).Where(e => e != null && !(e.letter == "우" && built.ContainsKey(e.variant))).ToList();
            foreach (var kv in built) list.Add(new SpellFxCatalog.Entry { letter = "우", variant = kv.Key, prefab = kv.Value });
            cat.entries = list.ToArray();
            EditorUtility.SetDirty(cat); AssetDatabase.SaveAssetIfDirty(cat);
            log.Append("catalogue: " + string.Join(", ", cat.entries.Select(e => e.letter + "/" + e.variant)));
            return log.ToString();
        }

        static void SetCard(SerializedProperty card, string name, Texture2D still, Texture2DArray seq, float distance, float riseLine)
        {
            card.FindPropertyRelative("Name").stringValue = name;
            card.FindPropertyRelative("Still").objectReferenceValue = still;
            card.FindPropertyRelative("Sequence").objectReferenceValue = seq;
            card.FindPropertyRelative("Fps").floatValue = 30f;
            card.FindPropertyRelative("Distance").floatValue = distance;
            card.FindPropertyRelative("RiseLine").floatValue = riseLine;
        }

        static Texture2D Still(string path)
        {
            if (!File.Exists(path)) return null;
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                imp.textureType = TextureImporterType.Default; imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.wrapMode = TextureWrapMode.Clamp;
                imp.maxTextureSize = 2048; imp.textureCompression = TextureImporterCompression.CompressedHQ; imp.sRGBTexture = true;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Route P pattern card: RGB = three masks (R line, G fill, B rim grown outward), so linear (sRGB off), mipmaps on, clamp. Reimports only when a setting differs.</summary>
        static Texture2D PatternMask(string path) => Import(path, false, false);

        /// <summary>Route P ink stroke: white RGB + alpha brush stroke, sRGB on, alpha is transparency, clamp.</summary>
        static Texture2D InkStroke(string path) => Import(path, true, true);

        static Texture2D Import(string path, bool srgb, bool alphaIsTransparency)
        {
            if (!File.Exists(path)) return null;
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                bool change = imp.textureType != TextureImporterType.Default || imp.sRGBTexture != srgb || !imp.mipmapEnabled || imp.wrapMode != TextureWrapMode.Clamp
                    || imp.alphaIsTransparency != alphaIsTransparency || imp.filterMode != FilterMode.Trilinear || imp.anisoLevel != 4 || imp.maxTextureSize != 2048
                    || imp.textureCompression != TextureImporterCompression.CompressedHQ;
                if (change)
                {
                    imp.textureType = TextureImporterType.Default; imp.sRGBTexture = srgb; imp.mipmapEnabled = true; imp.wrapMode = TextureWrapMode.Clamp;
                    imp.alphaIsTransparency = alphaIsTransparency; imp.filterMode = FilterMode.Trilinear; imp.anisoLevel = 4; imp.maxTextureSize = 2048;
                    imp.textureCompression = TextureImporterCompression.CompressedHQ;
                    imp.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Reads f_####.png (straight alpha) from a folder outside Assets and writes them as one DXT5 Texture2DArray asset.</summary>
        static Texture2DArray Sequence(string dir, string assetPath, bool depth)
        {
            var files = Directory.GetFiles(dir, "f_*.png").OrderBy(f => f, StringComparer.Ordinal).ToArray();
            if (files.Length == 0) return null;
            Texture2DArray arr = null;
            for (int i = 0; i < files.Length; i++)
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                t.LoadImage(File.ReadAllBytes(files[i]));
                if (arr == null) arr = new Texture2DArray(t.width, t.height, files.Length, depth ? TextureFormat.R8 : TextureFormat.DXT5, false, depth)      /* r3: the depth is linear (r2 made it R8_SRGB: the sampled depth was gamma-bent) */ { wrapMode = TextureWrapMode.Clamp, filterMode = depth ? FilterMode.Point : FilterMode.Bilinear };
                if (depth)
                {
                    // the depth is kept exact: the red channel, uncompressed (R8)
                    var src = t.GetPixels32(); var r = new byte[src.Length];
                    for (int k = 0; k < src.Length; k++) r[k] = src[k].r;
                    arr.SetPixelData(r, 0, i);
                }
                else
                {
                    EditorUtility.CompressTexture(t, TextureFormat.DXT5, UnityEditor.TextureCompressionQuality.Normal);
                    Graphics.CopyTexture(t, 0, 0, arr, i, 0);
                }
                UnityEngine.Object.DestroyImmediate(t);
            }
            if (depth) arr.Apply(false, false);
            else arr.Apply(false, true);
            // r3: overwrite in place (r2 deleted and re-created the asset: a prefab already in memory kept pointing at the destroyed object and the card drew
            // the shader's white default - a pale wall)
            var old = AssetDatabase.LoadAssetAtPath<Texture2DArray>(assetPath);
            if (old != null)
            {
                EditorUtility.CopySerialized(arr, old);
                UnityEngine.Object.DestroyImmediate(arr);
                EditorUtility.SetDirty(old); AssetDatabase.SaveAssetIfDirty(old);
                return old;
            }
            AssetDatabase.CreateAsset(arr, assetPath);
            AssetDatabase.SaveAssetIfDirty(arr);
            return AssetDatabase.LoadAssetAtPath<Texture2DArray>(assetPath);
        }

        static GameObject Prefab(string path, float playSeconds, Action<GameObject> build)
        {
            var go = new GameObject(Path.GetFileNameWithoutExtension(path));
            try
            {
                var player = go.AddComponent<SpellFxPlayer>();
                var so = new SerializedObject(player); so.FindProperty("playSeconds").floatValue = playSeconds; so.ApplyModifiedPropertiesWithoutUndo();
                build(go);
                return PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
