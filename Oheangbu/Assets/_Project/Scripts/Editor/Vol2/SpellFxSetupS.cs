using System;
using System.IO;
using System.Linq;
using Oheangbu.App.SpellFx;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>D308-153c: builds the sprite-wave variants of 우 (SpellFxSpriteWavesPart + Oheangbu/SpellFx/Sprite): R = Recraft-made sprites, I = GPT-image-made sprites.
    /// Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.SpellFxSetupSCommands Run "setup:s:R"      (or setup:s:I)
    ///        optional source folder: "setup:s:R:dir=../Art/SpellVFX308/Restart/sprites/R"  (relative to the Unity project folder; the queue splits on colons, so no drive letters). BleedColour takes several seconds per 2048 px sprite: a slow return is not a hang.
    /// Default folder: ../Art/SpellVFX308/Restart/sprites/&lt;V&gt; (mound.png crest.png claw.png ink.png splash.png; mound OR crest is required, the rest are optional).
    /// The sprites are copied (overwritten in place, so the GUIDs stay) to Assets/_Project/Art/SpellVFX308/Restart/U/&lt;V&gt;/ with the colour of the transparent texels bled outward (no dark fringe
    /// at the cut-out edge), imported sRGB / alpha-is-transparency / mipmaps with coverage preserved / clamp. Builds Restart/U/U_&lt;V&gt;.prefab and MERGES the catalogue entry 우/&lt;V&gt;: only that
    /// letter + variant is replaced. No dialogs. Saves only the assets it writes.</summary>
    public static class SpellFxSetupSCommands
    {
        const string Root = "Assets/_Project/Art/SpellVFX308/Restart";
        const string UDir = Root + "/U";
        static readonly string[] Names = { "mound", "crest", "claw", "ink", "splash" };

        public static string Run(string command)
        {
            try
            {
                var c = (command ?? "").Trim();
                if (!c.StartsWith("setup:s:", StringComparison.Ordinal)) return "REFUSED setup: setup:s:<R|I>[:dir=<relative dir>]";
                string rest = c.Substring("setup:s:".Length);
                int colon = rest.IndexOf(':');
                string v = (colon < 0 ? rest : rest.Substring(0, colon)).Trim().ToUpperInvariant();
                string opt = colon < 0 ? null : rest.Substring(colon + 1);
                if (v != "R" && v != "I") return "REFUSED setup:s: variant must be R or I (got '" + v + "')";
                string dir = "../Art/SpellVFX308/Restart/sprites/" + v;
                if (!string.IsNullOrEmpty(opt))
                {
                    if (!opt.StartsWith("dir=", StringComparison.OrdinalIgnoreCase)) return "REFUSED setup:s: unknown option '" + opt + "' (dir=<relative dir>)";
                    dir = opt.Substring(4);
                }
                return Setup(v, dir);
            }
            catch (Exception e) { return "EXCEPTION " + e.GetType().Name + " " + e.Message; }
        }

        static string Setup(string v, string dir)
        {
            string src = Path.IsPathRooted(dir) ? dir : Path.GetFullPath(Path.Combine(Application.dataPath, "..", dir));
            if (!Directory.Exists(src)) return "REFUSED setup:s:" + v + ": source folder missing: " + src;
            string vDir = UDir + "/" + v;
            Directory.CreateDirectory(vDir);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);           // also picks up a freshly written shader file
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/SpellFxSprite.shader");
            if (shader == null) return "REFUSED setup:s:" + v + ": " + Root + "/Shaders/SpellFxSprite.shader missing";

            var log = new System.Text.StringBuilder();
            float hookSign = 1f;
            var tex = new Texture2D[Names.Length];
            for (int i = 0; i < Names.Length; i++)
            {
                string file = Path.Combine(src, Names[i] + ".png");
                if (!File.Exists(file)) { log.Append(Names[i] + ": none; "); continue; }
                string assetPath = vDir + "/" + Names[i] + ".png";
                byte[] raw = File.ReadAllBytes(file);
                float sign;
                byte[] fixedPng = BleedColour(raw, out sign);
                if (Names[i] == "crest") hookSign = sign;
                string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
                // overwrite IN PLACE (never delete + create: a prefab already in memory would keep pointing at the destroyed asset); skip an identical file
                if (!File.Exists(full) || !File.ReadAllBytes(full).SequenceEqual(fixedPng)) File.WriteAllBytes(full, fixedPng);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                tex[i] = Import(assetPath, Names[i] == "ink" ? 0.25f : 0.5f);
                log.Append(Names[i] + ": ok" + (tex[i] != null ? " " + tex[i].width + "x" + tex[i].height : " (no texture?)") + "; ");
            }
            var mound = tex[0]; var crest = tex[1];
            if (mound == null && crest == null) return "REFUSED setup:s:" + v + ": neither mound.png nor crest.png in " + src + " (" + log + ")";

            var prefab = Prefab(UDir + "/U_" + v + ".prefab", 1.8f, go =>
            {
                var player = go.GetComponent<SpellFxPlayer>();
                var pso = new SerializedObject(player); pso.FindProperty("tailSeconds").floatValue = 1.2f; pso.ApplyModifiedPropertiesWithoutUndo();      // a hit late in the life still plays its wave
                var part = go.AddComponent<SpellFxSpriteWavesPart>();
                var so = new SerializedObject(part);
                so.FindProperty("spriteShader").objectReferenceValue = shader;
                so.FindProperty("moundTex").objectReferenceValue = tex[0];
                so.FindProperty("crestTex").objectReferenceValue = tex[1];
                so.FindProperty("clawTex").objectReferenceValue = tex[2];
                so.FindProperty("inkTex").objectReferenceValue = tex[3];
                so.FindProperty("splashTex").objectReferenceValue = tex[4];
                so.FindProperty("radius").floatValue = 2.2f;
                so.FindProperty("lifeSeconds").floatValue = 1.8f;
                so.FindProperty("crestHookSign").floatValue = hookSign;
                so.ApplyModifiedPropertiesWithoutUndo();
            });
            log.Append("prefab ok (crest hook " + (hookSign > 0 ? "right" : "left") + "); ");

            // ---- catalogue: MERGE - only 우/<V> is replaced, every other letter and variant stays ----
            var cat = AssetDatabase.LoadAssetAtPath<SpellFxCatalog>(SpellFxCatalog.AssetPath);
            if (cat == null) { cat = ScriptableObject.CreateInstance<SpellFxCatalog>(); AssetDatabase.CreateAsset(cat, SpellFxCatalog.AssetPath); }
            var list = (cat.entries ?? new SpellFxCatalog.Entry[0]).Where(e => e != null && !(e.letter == "우" && e.variant == v)).ToList();
            list.Add(new SpellFxCatalog.Entry { letter = "우", variant = v, prefab = prefab });
            cat.entries = list.ToArray();
            EditorUtility.SetDirty(cat); AssetDatabase.SaveAssetIfDirty(cat);
            log.Append("catalogue: " + string.Join(", ", cat.entries.Select(e => e.letter + "/" + e.variant)));
            return log.ToString();
        }

        /// <summary>Painted sprite: sRGB, alpha is transparency, mipmaps WITH coverage preserved at the shader's cutoff (the cut-out keeps its area at distance), clamp, trilinear, no NPOT rescale.
        /// Reimports only when a setting differs.</summary>
        static Texture2D Import(string path, float cutoff)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                bool change = imp.textureType != TextureImporterType.Default || !imp.sRGBTexture || !imp.alphaIsTransparency || !imp.mipmapEnabled || imp.wrapMode != TextureWrapMode.Clamp
                    || imp.filterMode != FilterMode.Trilinear || imp.anisoLevel != 4 || imp.maxTextureSize != 2048 || imp.textureCompression != TextureImporterCompression.CompressedHQ
                    || !imp.mipMapsPreserveCoverage || Mathf.Abs(imp.alphaTestReferenceValue - cutoff) > 1e-4f || imp.npotScale != TextureImporterNPOTScale.None || imp.isReadable
                    || imp.alphaSource != TextureImporterAlphaSource.FromInput;
                if (change)
                {
                    imp.textureType = TextureImporterType.Default; imp.sRGBTexture = true; imp.alphaSource = TextureImporterAlphaSource.FromInput; imp.alphaIsTransparency = true;
                    imp.mipmapEnabled = true; imp.mipMapsPreserveCoverage = true; imp.alphaTestReferenceValue = cutoff;
                    imp.wrapMode = TextureWrapMode.Clamp; imp.filterMode = FilterMode.Trilinear; imp.anisoLevel = 4; imp.npotScale = TextureImporterNPOTScale.None; imp.isReadable = false;
                    imp.maxTextureSize = 2048; imp.textureCompression = TextureImporterCompression.CompressedHQ;
                    imp.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Straight-alpha PNG in, straight-alpha PNG out, the colour of the (nearly) transparent texels pushed outward from the opaque ones (8 passes of 8-neighbour average):
        /// a generated sprite's transparent texels are usually black or white, and bilinear sampling / mip-mapping would draw that colour as a dark / pale fringe along the cut edge.
        /// Also measures which way the crest's hook points (the alpha centroid of the top 30 % against the whole): +1 right, -1 left.</summary>
        static byte[] BleedColour(byte[] png, out float hookSign)
        {
            hookSign = 1f;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!t.LoadImage(png)) return png;
                int w = t.width, h = t.height;
                var px = t.GetPixels32();

                double sumAll = 0, wAll = 0, sumTop = 0, wTop = 0;
                int topFrom = (int)(h * 0.7f);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        double a = px[y * w + x].a / 255.0;
                        if (a < 0.5) continue;
                        sumAll += x * a; wAll += a;
                        if (y >= topFrom) { sumTop += x * a; wTop += a; }          // Texture2D row 0 is the bottom: the top 30 % are the last rows
                    }
                if (wAll > 0 && wTop > 0)
                {
                    double d = sumTop / wTop - sumAll / wAll;
                    if (Math.Abs(d) > w * 0.02) hookSign = d > 0 ? 1f : -1f;
                }

                var filled = new bool[px.Length];
                for (int i = 0; i < px.Length; i++) filled[i] = px[i].a > 8;
                for (int pass = 0; pass < 8; pass++)
                {
                    var next = (bool[])filled.Clone();
                    bool any = false;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int i = y * w + x;
                            if (filled[i]) continue;
                            int r = 0, g = 0, b = 0, n = 0;
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                int yy = y + dy; if (yy < 0 || yy >= h) continue;
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    int xx = x + dx; if (xx < 0 || xx >= w || (dx == 0 && dy == 0)) continue;
                                    int j = yy * w + xx;
                                    if (!filled[j]) continue;
                                    r += px[j].r; g += px[j].g; b += px[j].b; n++;
                                }
                            }
                            if (n == 0) continue;
                            px[i] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), px[i].a);
                            next[i] = true; any = true;
                        }
                    filled = next;
                    if (!any) break;
                }
                t.SetPixels32(px); t.Apply(false);
                return t.EncodeToPNG() ?? png;
            }
            finally { UnityEngine.Object.DestroyImmediate(t); }
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
