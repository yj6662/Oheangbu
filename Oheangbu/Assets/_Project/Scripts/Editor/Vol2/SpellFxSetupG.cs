using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Oheangbu.App.SpellFx;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>D308-153 variant 우/G: the VFX Graph route. Builds Assets/_Project/Art/SpellVFX308/Restart/U/U_G.prefab = SpellFxPlayer + SpellFxVfxWavePart and merges it into the
    /// catalogue as 우/G (only that entry is replaced).
    /// The graphs are COPIES of V2/Graphs/VFX_V308_na_ring.vfx (the original is never touched), made with AssetDatabase.CopyAsset once (a re-run keeps the GUID) and then edited as text,
    /// idempotently (see EnsureGraph): the arc-circle emitter is moved from (0, 0.1, 6) to the cast origin and given a ring radius, the quads face the camera instead of the velocity,
    /// the over-life grey / alpha tint is made neutral, and the three texIndex blocks (random, flipbook player, brush-variant map) are disabled so one painted sprite = one quad.
    /// VFX_U_G.vfx = the 2.2 m ring; VFX_U_G_hit.vfx = a 0.3 m ring for the burst at the struck body. The sprites: Art/SpellVFX308/Restart/sprites/I/ then sprites/R/ (mound, crest,
    /// claw, ink, splash .png, outside Assets), else placeholders made here from the KCISA masks (U/P) / the ink stroke / a procedural dot.
    /// The child VisualEffects are made by the part at runtime: this editor assembly does not reference the VFX runtime.
    /// Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.SpellFxSetupGCommands Run "setup:g"
    /// No dialogs. Saves only what it writes.</summary>
    public static class SpellFxSetupGCommands
    {
        const string Root = "Assets/_Project/Art/SpellVFX308/Restart";
        const string GDir = Root + "/U/G";
        const string SpriteDir = GDir + "/sprites";
        const string SourceGraph = "Assets/_Project/Art/SpellVFX308/V2/Graphs/VFX_V308_na_ring.vfx";
        const string RingGraph = GDir + "/VFX_U_G.vfx";
        const string HitGraph = GDir + "/VFX_U_G_hit.vfx";
        const float RingRadius = 2.2f, HitRadius = 0.3f;

        // block fileIDs inside VFX_V308_na_ring.vfx (read from the YAML): FlipbookPlayer (update), random texIndex (init), AttributeFromMap texIndex (init)
        static readonly string[] TexIndexBlocks = { "8926484042661614742", "8926484042661614795", "8926484042661614963" };

        public static string Run(string command)
        {
            try
            {
                var c = (command ?? "").Trim();
                if (!string.Equals(c, "setup:g", StringComparison.OrdinalIgnoreCase)) return "REFUSED setup: setup:g";
                return SetupG();
            }
            catch (Exception e) { return "EXCEPTION " + e.GetType().Name + " " + e.Message; }
        }

        static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        static string RepoRoot => Directory.GetParent(ProjectRoot).FullName;
        static string Full(string assetPath) => Path.Combine(ProjectRoot, assetPath);

        static string SetupG()
        {
            var log = new StringBuilder();
            Directory.CreateDirectory(Full(SpriteDir));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // ---- graphs ----
            string err = EnsureGraph(RingGraph, RingRadius, log);
            if (err != null) return "REFUSED setup:g: " + err;
            err = EnsureGraph(HitGraph, HitRadius, log);
            if (err != null) return "REFUSED setup:g: " + err;
            var ringAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(RingGraph);
            var hitAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(HitGraph);
            if (ringAsset == null || hitAsset == null) return "REFUSED setup:g: the graph copies did not import (" + log + ")";
            log.Append("graph asset type ").Append(ringAsset.GetType().Name).Append("; ");

            // ---- sprites ----
            var mound = SpriteAsset("mound", log); var crest = SpriteAsset("crest", log); var claw = SpriteAsset("claw", log); var ink = SpriteAsset("ink", log); var splash = SpriteAsset("splash", log);

            // ---- prefab ----
            var prefab = Prefab(Root + "/U/U_G.prefab", 1.8f, go =>
            {
                var player = go.GetComponent<SpellFxPlayer>();
                var pso = new SerializedObject(player); pso.FindProperty("tailSeconds").floatValue = 1.0f; pso.ApplyModifiedPropertiesWithoutUndo();      // a hit late in the life still plays its burst
                var part = go.AddComponent<SpellFxVfxWavePart>();
                var so = new SerializedObject(part);
                so.FindProperty("ringGraph").objectReferenceValue = ringAsset;
                so.FindProperty("hitGraph").objectReferenceValue = hitAsset;
                so.FindProperty("moundTex").objectReferenceValue = mound;
                so.FindProperty("crestTex").objectReferenceValue = crest;
                so.FindProperty("inkTex").objectReferenceValue = ink;
                so.FindProperty("clawTex").objectReferenceValue = claw;
                so.FindProperty("splashTex").objectReferenceValue = splash;
                so.FindProperty("bakedRadius").floatValue = RingRadius;
                so.ApplyModifiedPropertiesWithoutUndo();
            });
            log.Append("G prefab ok; ");

            // ---- catalogue: MERGE - only 우/G is replaced ----
            var cat = AssetDatabase.LoadAssetAtPath<SpellFxCatalog>(SpellFxCatalog.AssetPath);
            if (cat == null) { cat = ScriptableObject.CreateInstance<SpellFxCatalog>(); AssetDatabase.CreateAsset(cat, SpellFxCatalog.AssetPath); }
            var list = (cat.entries ?? new SpellFxCatalog.Entry[0]).Where(e => e != null && !(e.letter == "우" && e.variant == "G")).ToList();
            list.Add(new SpellFxCatalog.Entry { letter = "우", variant = "G", prefab = prefab });
            cat.entries = list.ToArray();
            EditorUtility.SetDirty(cat); AssetDatabase.SaveAssetIfDirty(cat);
            log.Append("catalogue: " + string.Join(", ", cat.entries.Select(e => e.letter + "/" + e.variant)));
            return log.ToString();
        }

        // ------------------------------------------------------------------ graph copy + text edit
        /// <summary>Copies the source graph to <paramref name="dst"/> once, then applies the edits. Every edit is counted: a pattern that is not there (already edited, or the source
        /// changed) is reported in the log, never forced. Returns an error text, or null.</summary>
        static string EnsureGraph(string dst, float radius, StringBuilder log)
        {
            if (!File.Exists(Full(SourceGraph))) return SourceGraph + " missing";
            if (!File.Exists(Full(dst)))
            {
                if (!AssetDatabase.CopyAsset(SourceGraph, dst)) return "CopyAsset " + SourceGraph + " -> " + dst + " failed";
                AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceSynchronousImport);
            }
            string full = Full(dst);
            string orig = File.ReadAllText(full), text = orig;
            string name = Path.GetFileName(dst);
            int n;

            // 1. the emitter centre: arc-circle position and spherical-velocity centre were (0, 0.1, 6) = six metres ahead of the effect; now the effect's own origin
            var reCentre = new Regex("(\"y\":0\\.10000000149011612,\"z\":)6\\.0");
            n = reCentre.Matches(text).Count; text = reCentre.Replace(text, "${1}0.0");
            log.Append(name).Append(" centre z6->0: ").Append(n).Append("; ");

            // 2. the ring radius (arc-circle, 2.2999999 in the source)
            var reRadius = new Regex("\"radius\":[0-9.eE+-]+\\},\"arc\"");
            string radiusText = "\"radius\":" + radius.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "},\"arc\"";
            n = reRadius.Matches(text).Count; text = reRadius.Replace(text, radiusText);
            log.Append("radius ").Append(radius.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" x").Append(n).Append("; ");

            // 3. Orient: AlongVelocity (6) -> FaceCameraPlane (0): a painted sprite stands facing the camera
            var reOrient = new Regex("(?m)^(  mode: )6(?=\\r?$)");
            n = reOrient.Matches(text).Count; text = reOrient.Replace(text, "${1}0");
            log.Append("orient x").Append(n).Append("; ");

            // 4. the over-life tint (white -> 50 % grey, alpha 0 / 1 / 0.8 / 0) darkens a painted sprite: grey -> white, 0.8 -> 1 (the exposed Color Over Life still fades the ends)
            const string grey = "{\"color\":{\"r\":0.5,\"g\":0.5,\"b\":0.5,\"a\":1.0},\"time\":1.0}";
            const string white = "{\"color\":{\"r\":1.0,\"g\":1.0,\"b\":1.0,\"a\":1.0},\"time\":1.0}";
            n = text.Contains(grey) ? 1 : 0; text = text.Replace(grey, white);
            const string a08 = "{\"alpha\":0.800000011920929,\"time\":0.800000011920929}";
            const string a10 = "{\"alpha\":1.0,\"time\":0.800000011920929}";
            n += text.Contains(a08) ? 1 : 0; text = text.Replace(a08, a10);
            log.Append("tint x").Append(n).Append("; ");

            // 5. texIndex: the flipbook player, the random index and the brush-variant map are switched off (one sprite per quad, index 0)
            int off = 0;
            foreach (var id in TexIndexBlocks) { bool did; text = DisableBlock(text, id, out did); if (did) off++; }
            log.Append("texIndex blocks off x").Append(off).Append("; ");

            if (!string.Equals(text, orig, StringComparison.Ordinal))
            {
                File.WriteAllText(full, text, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                log.Append("(edited, reimported); ");
            }
            else log.Append("(unchanged); ");
            return null;
        }

        /// <summary>Sets the first "  m_Disabled: 0" inside the YAML document of the object <paramref name="id"/> to 1. false when the object is not found or already disabled.</summary>
        static string DisableBlock(string text, string id, out bool changed)
        {
            changed = false;
            int head = text.IndexOf("--- !u!114 &" + id + "\n", StringComparison.Ordinal);
            if (head < 0) return text;
            int next = text.IndexOf("\n--- !u!", head + 10, StringComparison.Ordinal);
            if (next < 0) next = text.Length;
            const string enabledLine = "\n  m_Disabled: 0\n", disabledLine = "\n  m_Disabled: 1\n";
            int at = text.IndexOf(enabledLine, head, StringComparison.Ordinal);
            if (at < 0 || at > next) return text;
            changed = true;
            return text.Substring(0, at) + disabledLine + text.Substring(at + enabledLine.Length);
        }

        // ------------------------------------------------------------------ sprites
        /// <summary>One element sprite as a texture asset under U/G/sprites: sprites/I, then sprites/R (repo Art folder), else a placeholder.</summary>
        static Texture2D SpriteAsset(string name, StringBuilder log)
        {
            string assetPath = SpriteDir + "/" + name + ".png";
            byte[] bytes = null; string origin = null;
            string spritesRoot = Path.Combine(RepoRoot, "Art", "SpellVFX308", "Restart", "sprites");
            foreach (var sub in new[] { "I", "R" })
            {
                string p = Path.Combine(spritesRoot, sub, name + ".png");
                if (File.Exists(p)) { bytes = File.ReadAllBytes(p); origin = "sprites/" + sub; break; }
            }
            if (bytes == null)
            {
                bytes = Placeholder(name);
                origin = "placeholder";
            }
            if (bytes == null) { log.Append(name).Append(": no source; "); return null; }
            string full = Full(assetPath);
            if (!File.Exists(full) || !File.ReadAllBytes(full).SequenceEqual(bytes))
            {
                File.WriteAllBytes(full, bytes);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }
            log.Append(name).Append(" <- ").Append(origin).Append("; ");
            return Import(assetPath);
        }

        /// <summary>No painted sprite yet: mound / crest from the KCISA masks (R line, G fill, B silhouette) painted indigo / cream / ink, claw / ink from the ink stroke, splash a soft dot.</summary>
        static byte[] Placeholder(string name)
        {
            string pDir = Root + "/U/P/";
            switch (name)
            {
                case "mound": return Painted(Full(pDir + "p_226.png"));
                case "crest": return Painted(Full(pDir + "p_232.png"));
                case "claw":
                case "ink": { string p = Full(pDir + "ink_stroke.png"); return File.Exists(p) ? File.ReadAllBytes(p) : null; }
                default: return Dot();
            }
        }

        static byte[] Painted(string maskPath)
        {
            if (!File.Exists(maskPath)) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!t.LoadImage(File.ReadAllBytes(maskPath))) return null;
                var px = t.GetPixels32();
                var ink = new Color(0.03f, 0.05f, 0.10f); var indigo = new Color(0.09f, 0.20f, 0.40f); var cream = new Color(0.93f, 0.90f, 0.78f);
                for (int i = 0; i < px.Length; i++)
                {
                    float line = px[i].r / 255f, fill = px[i].g / 255f, sil = px[i].b / 255f;
                    Color c = Color.Lerp(Color.Lerp(ink, indigo, fill), cream, line);
                    c.a = Mathf.Max(sil, Mathf.Max(fill, line));
                    px[i] = (Color32)c;
                }
                t.SetPixels32(px); t.Apply(false);
                return t.EncodeToPNG();
            }
            finally { UnityEngine.Object.DestroyImmediate(t); }
        }

        static byte[] Dot()
        {
            const int S = 128;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
            try
            {
                var px = new Color32[S * S];
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01((0.95f - r) * 6f);
                        px[y * S + x] = (Color32)new Color(0.98f, 0.96f, 0.9f, a);
                    }
                t.SetPixels32(px); t.Apply(false);
                return t.EncodeToPNG();
            }
            finally { UnityEngine.Object.DestroyImmediate(t); }
        }

        /// <summary>Particle sprite: straight alpha is transparency, sRGB, mipmaps, clamp. Reimports only when a setting differs.</summary>
        static Texture2D Import(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                bool change = imp.textureType != TextureImporterType.Default || !imp.sRGBTexture || !imp.mipmapEnabled || imp.wrapMode != TextureWrapMode.Clamp
                    || !imp.alphaIsTransparency || imp.filterMode != FilterMode.Trilinear || imp.maxTextureSize != 2048 || imp.textureCompression != TextureImporterCompression.CompressedHQ;
                if (change)
                {
                    imp.textureType = TextureImporterType.Default; imp.sRGBTexture = true; imp.mipmapEnabled = true; imp.wrapMode = TextureWrapMode.Clamp;
                    imp.alphaIsTransparency = true; imp.filterMode = FilterMode.Trilinear; imp.maxTextureSize = 2048; imp.textureCompression = TextureImporterCompression.CompressedHQ;
                    imp.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ prefab
        /// <summary>Same as SpellFxSetupCommands.Prefab (that file is not edited): saves in place, so an existing prefab keeps its GUID.</summary>
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
