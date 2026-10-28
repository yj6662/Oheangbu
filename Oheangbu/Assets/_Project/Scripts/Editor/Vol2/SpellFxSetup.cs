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
    ///   C = the live in-engine wave ring (SpellFxRingWavePart + Oheangbu/SpellFx/Water).
    /// Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.SpellFxSetupCommands Run "setup:u[:a=<frames dir with far/ near/>]"
    /// No dialogs. Saves only the assets it writes (AssetDatabase.SaveAssetIfDirty per asset).</summary>
    public static class SpellFxSetupCommands
    {
        const string Root = "Assets/_Project/Art/SpellVFX308/Restart";
        const string UDir = Root + "/U";

        public static string Run(string command)
        {
            try
            {
                var c = (command ?? "").Trim();
                if (!c.StartsWith("setup:u", StringComparison.Ordinal)) return "REFUSED setup: setup:u[:a=<dir>]";
                string aDir = null;
                foreach (var part in c.Split(':').Skip(2)) if (part.StartsWith("a=", StringComparison.Ordinal)) aDir = part.Substring(2);
                return SetupU(aDir);
            }
            catch (Exception e) { return "EXCEPTION " + e.GetType().Name + " " + e.Message; }
        }

        static string SetupU(string aDir)
        {
            Directory.CreateDirectory(UDir + "/A"); Directory.CreateDirectory(UDir + "/B"); Directory.CreateDirectory(UDir + "/C");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var log = new System.Text.StringBuilder();
            var cardShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/SpellFxCard.shader");
            var waterShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/SpellFxWater.shader");
            if (cardShader == null || waterShader == null) return "REFUSED setup:u: shaders missing under " + Root + "/Shaders";

            // ---- B: the two stills ----
            Texture2D bFar = Still(UDir + "/B/u_b_far.png"), bNear = Still(UDir + "/B/u_b_near.png");
            GameObject pB = null;
            if (bFar != null && bNear != null)
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
            if (!string.IsNullOrEmpty(aDir) && Directory.Exists(Path.Combine(aDir, "col")))
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
            var pC = Prefab(UDir + "/U_C.prefab", 2.6f, go =>
            {
                var part = go.AddComponent<SpellFxRingWavePart>();
                var so = new SerializedObject(part);
                so.FindProperty("waterShader").objectReferenceValue = waterShader;
                so.ApplyModifiedPropertiesWithoutUndo();
            });
            log.Append("C ok; ");

            // ---- catalogue ----
            var cat = AssetDatabase.LoadAssetAtPath<SpellFxCatalog>(SpellFxCatalog.AssetPath);
            if (cat == null) { cat = ScriptableObject.CreateInstance<SpellFxCatalog>(); AssetDatabase.CreateAsset(cat, SpellFxCatalog.AssetPath); }
            var list = (cat.entries ?? new SpellFxCatalog.Entry[0]).Where(e => e != null && e.letter != "우").ToList();
            if (pA != null) list.Add(new SpellFxCatalog.Entry { letter = "우", variant = "A", prefab = pA });
            if (pB != null) list.Add(new SpellFxCatalog.Entry { letter = "우", variant = "B", prefab = pB });
            if (pC != null) list.Add(new SpellFxCatalog.Entry { letter = "우", variant = "C", prefab = pC });
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
