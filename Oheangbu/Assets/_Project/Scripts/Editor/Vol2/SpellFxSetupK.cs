using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Oheangbu.App.SpellFx;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>D308-153b variant 우/K (2026-10-29): the Kling AI video of the concept uks_1, matted to RGBA frames (Tools/Art/SpellVFX308/restart/u_k_matte.py ->
    /// Art/SpellVFX308/Restart/u_K/frames/f_0000.png ...), played as a frame sequence on cards (SpellFxCardsPart). Builds
    ///   1. two DXT5 Texture2DArrays from the one frame folder (outside Assets): FAR = the picture above a soft horizontal split, NEAR = the bottom band, alpha weights baked at import;
    ///   2. Restart/U/U_K.prefab: SpellFxPlayer + SpellFxCardsPart with two cards (Far 3.6 m, Near 1.3 m, pitch 17.4 - the same camera as route B: the concept uks_1 has the same
    ///      horizon height as u_a, 0.20-0.22 of the picture from the top), Fps 30;
    ///   3. MERGES the catalogue entry 우/K (every other entry stays).
    /// Why two cards: the clip has no water in front of the caster (the matte already cuts his silhouette out of the waves), so the FAR card sits behind him (3.6 m; he is ~3.0 m from the
    /// eye) and in front of the bear on the ring (~5 m): the water drenches it. But the ground plane cuts a flat card below a screen line (the card bottom is under the ground at ~0.79 of the
    /// picture height), so the bottom band of the picture - the front arcs - is on a NEAR card at 1.3 m, exactly as route B does it. Both cards show the same picture from the same eye,
    /// so the join is invisible; the weights overlap (far 1 -> 0 over rows 540..600, near 0 -> 1 over rows 500..540, rows counted from the top of the 720 px picture) so no seam dims.
    /// Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.SpellFxSetupKCommands Run "setup:k"      (the default frames dir is the repo's Art/SpellVFX308/Restart/u_K/frames)
    ///   options (all optional, ':'-separated, no drive colon anywhere): dir=&lt;frames dir relative to the repo root, or to the Unity project folder (../Art/... works too)&gt;  far=3.6  near=1.3  pitch=17.4  split=540 (rows from the top where the far card starts to fade)
    /// No dialogs. Saves only the assets it writes. The arrays and the prefab are overwritten IN PLACE (never delete + create: a prefab in memory keeps pointing at the destroyed object).</summary>
    public static class SpellFxSetupKCommands
    {
        const string Root = "Assets/_Project/Art/SpellVFX308/Restart";
        const string KDir = Root + "/U/K";
        const string DefaultFrames = "Art/SpellVFX308/Restart/u_K/frames";

        public static string Run(string command)
        {
            try
            {
                var c = (command ?? "").Trim();
                if (!c.StartsWith("setup:k", StringComparison.OrdinalIgnoreCase)) return "REFUSED setup: setup:k[:dir=<frames dir relative to the repo>][:far=3.6][:near=1.3][:pitch=17.4][:split=540]";
                string dir = DefaultFrames; float far = 3.6f, near = 1.3f, pitch = 17.4f, split = 540f;
                var tokens = c.Split(':');
                if (tokens.Length < 2 || !string.Equals(tokens[1], "k", StringComparison.OrdinalIgnoreCase)) return "REFUSED setup: setup:k[:dir=...] only";
                for (int i = 2; i < tokens.Length; i++)
                {
                    var t = tokens[i].Trim(); if (t.Length == 0) continue;
                    int eq = t.IndexOf('='); if (eq <= 0) return "REFUSED setup:k: unknown option '" + t + "'";
                    string key = t.Substring(0, eq).ToLowerInvariant(), val = t.Substring(eq + 1);
                    switch (key)
                    {
                        case "dir": dir = val; break;
                        case "far": if (!F(val, out far)) return "REFUSED setup:k: far=" + val; break;
                        case "near": if (!F(val, out near)) return "REFUSED setup:k: near=" + val; break;
                        case "pitch": if (!F(val, out pitch)) return "REFUSED setup:k: pitch=" + val; break;
                        case "split": if (!F(val, out split)) return "REFUSED setup:k: split=" + val; break;
                        default: return "REFUSED setup:k: unknown option '" + key + "' (dir, far, near, pitch, split)";
                    }
                }
                return SetupK(dir, far, near, pitch, split);
            }
            catch (Exception e) { return "EXCEPTION " + e.GetType().Name + " " + e.Message; }
        }

        static bool F(string s, out float v) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        static string SetupK(string dir, float farDistance, float nearDistance, float pitch, float split)
        {
            var log = new System.Text.StringBuilder();
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            // relative paths are tried against the repo root first, then against the Unity project folder (so both "Art/SpellVFX308/..." and "../Art/SpellVFX308/..." work)
            string rel = dir.Replace('/', Path.DirectorySeparatorChar), frames = Path.IsPathRooted(rel) ? rel : Path.GetFullPath(Path.Combine(repo, rel));
            if (!Directory.Exists(frames) && !Path.IsPathRooted(rel)) frames = Path.GetFullPath(Path.Combine(repo, "Oheangbu", rel));
            if (!Directory.Exists(frames)) return "REFUSED setup:k: frames dir missing: " + frames;
            var cardShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/SpellFxCard.shader");
            if (cardShader == null) return "REFUSED setup:k: " + Root + "/Shaders/SpellFxCard.shader missing";
            Directory.CreateDirectory(KDir);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // far: 1 above `split`, fades to 0 over 60 rows. near: 0 until split-40, up to 1 at `split` (so the overlap is opaque - no dimmed seam)
            var far = Sequence(frames, KDir + "/u_k_far.asset", r => 1f - Smooth((r - split) / 60f));
            var near = Sequence(frames, KDir + "/u_k_near.asset", r => Smooth((r - (split - 40f)) / 40f));
            if (far == null || near == null) return "REFUSED setup:k: no f_*.png in " + frames;
            log.Append("arrays " + far.depth + " frames " + far.width + "x" + far.height + "; ");

            var prefab = Prefab(Root + "/U/U_K.prefab", far.depth / 30f + 0.3f, go =>
            {
                var part = go.AddComponent<SpellFxCardsPart>();
                var so = new SerializedObject(part);
                so.FindProperty("shader").objectReferenceValue = cardShader;
                so.FindProperty("pitch").floatValue = pitch;
                var cards = so.FindProperty("cards"); cards.arraySize = 2;
                SetCard(cards.GetArrayElementAtIndex(0), "Far", far, farDistance);
                SetCard(cards.GetArrayElementAtIndex(1), "Near", near, nearDistance);
                so.ApplyModifiedPropertiesWithoutUndo();
            });
            log.Append("K ok (far " + farDistance.ToString(CultureInfo.InvariantCulture) + " m, near " + nearDistance.ToString(CultureInfo.InvariantCulture) + " m, pitch " + pitch.ToString(CultureInfo.InvariantCulture) + "); ");

            // ---- catalogue: MERGE - only 우/K is replaced, every other entry stays ----
            var cat = AssetDatabase.LoadAssetAtPath<SpellFxCatalog>(SpellFxCatalog.AssetPath);
            if (cat == null) { cat = ScriptableObject.CreateInstance<SpellFxCatalog>(); AssetDatabase.CreateAsset(cat, SpellFxCatalog.AssetPath); }
            var list = (cat.entries ?? new SpellFxCatalog.Entry[0]).Where(e => e != null && !(e.letter == "우" && e.variant == "K")).ToList();
            list.Add(new SpellFxCatalog.Entry { letter = "우", variant = "K", prefab = prefab });
            cat.entries = list.ToArray();
            EditorUtility.SetDirty(cat); AssetDatabase.SaveAssetIfDirty(cat);
            log.Append("catalogue: " + string.Join(", ", cat.entries.Select(e => e.letter + "/" + e.variant)));
            return log.ToString();
        }

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        static void SetCard(SerializedProperty card, string name, Texture2DArray seq, float distance)
        {
            card.FindPropertyRelative("Name").stringValue = name;
            card.FindPropertyRelative("Still").objectReferenceValue = null;
            card.FindPropertyRelative("Sequence").objectReferenceValue = seq;
            card.FindPropertyRelative("Fps").floatValue = 30f;
            card.FindPropertyRelative("Distance").floatValue = distance;
            card.FindPropertyRelative("RiseLine").floatValue = 0f;
        }

        /// <summary>Reads f_####.png (straight alpha) from a folder outside Assets, multiplies the alpha by weight(row counted from the TOP of the picture) and writes one DXT5
        /// Texture2DArray asset (sRGB, clamp, bilinear, no mips). Overwritten in place when it exists.</summary>
        static Texture2DArray Sequence(string dir, string assetPath, Func<float, float> weightByRow)
        {
            var files = Directory.GetFiles(dir, "f_*.png").OrderBy(f => f, StringComparer.Ordinal).ToArray();
            if (files.Length == 0) return null;
            Texture2DArray arr = null; float[] wRow = null;
            for (int i = 0; i < files.Length; i++)
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                t.LoadImage(File.ReadAllBytes(files[i]));
                if (arr == null)
                {
                    arr = new Texture2DArray(t.width, t.height, files.Length, TextureFormat.DXT5, false, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                    // the picture is 720 rows tall whatever the file size: weights are in rows of the 720 px picture
                    wRow = new float[t.height];
                    for (int y = 0; y < t.height; y++) wRow[y] = weightByRow((t.height - 1 - y) * 720f / t.height);      // Unity row 0 is the bottom of the picture
                }
                if (t.width != arr.width || t.height != arr.height) { UnityEngine.Object.DestroyImmediate(t); throw new InvalidOperationException("frame size differs: " + files[i]); }
                var px = t.GetPixels32();
                for (int y = 0; y < t.height; y++)
                {
                    float w = wRow[y]; int o = y * t.width;
                    for (int x = 0; x < t.width; x++) px[o + x].a = (byte)Mathf.Clamp(Mathf.RoundToInt(px[o + x].a * w), 0, 255);
                }
                t.SetPixels32(px); t.Apply(false);
                EditorUtility.CompressTexture(t, TextureFormat.DXT5, UnityEditor.TextureCompressionQuality.Normal);
                Graphics.CopyTexture(t, 0, 0, arr, i, 0);
                UnityEngine.Object.DestroyImmediate(t);
            }
            arr.Apply(false, true);
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
