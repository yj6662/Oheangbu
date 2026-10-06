using System;
using System.Collections.Generic;
using System.IO;
using Oheangbu.App.World.UI;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#308 소지품 screen setup (SPEC-UI-EQUIPMENT-308). Queue: Oheangbu.EditorTools.WorldMacro.EquipmentSetup308 Execute
    /// "dry" | "apply" | "revert" | "report". Edit Mode only, idempotent, never opens a dialog; a refusal is a returned string.
    ///   dry    = what apply would do; writes nothing.
    ///   apply  = 1. import settings of its own nine textures under Assets/_Project/Art/UI/UI308/Equipment (import308.json:
    ///               Sprite, frame Sliced 28, mips on the 64 px marks, uncompressed); only a texture whose settings differ is reimported.
    ///            2. creates Assets/_Project/Resources/UI308/EquipmentScreen308.asset and binds the sprites.
    ///            3. #308 theme (SPEC-UI-THEME-308, D308-15): when the theme kit is deployed under
    ///               Assets/_Project/Art/UI/UI308/Theme (imported by the theme's own setup: UiTheme308 theme308-setup), binds the
    ///               asset's Theme block: kit sprites by cell name (a cell the atlas has no named sprite for is cut from the atlas
    ///               by theme308_atlas.json into this asset), Lattice/lat308_bit, and the token colours read from theme308_tokens.json.
    ///               The kit is only READ. No kit = the block is left alone and the screen draws the #304 look.
    ///            Saves ONLY that asset (AssetDatabase.SaveAssetIfDirty: other sessions' dirty assets are left alone) and
    ///            writes a ledger to Art/UI308/Equipment/setup308_ledger.json.
    ///   revert = deletes what the ledger says apply created (the asset, then folders it created when they are empty). The
    ///            textures are deploy files: delete their folder by hand to remove them.
    /// It touches no scene, no theme / style asset, nothing under Watershed295 / Reworld292 / MountainTrail285.</summary>
    public static class EquipmentSetup308
    {
        const string TextureFolder = "Assets/_Project/Art/UI/UI308/Equipment";
        const string ResourcesRoot = "Assets/_Project/Resources";
        const string AssetFolder = ResourcesRoot + "/UI308";
        const string AssetPath = AssetFolder + "/EquipmentScreen308.asset";
        const string ManifestName = "import308.json";
        static readonly string[] ProtectedTrees = { "Watershed295", "Reworld292", "MountainTrail285" };
        static readonly string[] SpriteFiles = { "frame_item", "mark_effect", "mark_compare", "mark_upgrade", "mark_candidates", "mark_body", "mark_power", "mark_tier", "mark_learned" };
        // the theme kit (another Spec's files: read, never written)
        const string ThemeFolder = "Assets/_Project/Art/UI/UI308/Theme";
        const string ThemeAtlas = ThemeFolder + "/theme308_atlas.png", ThemeTokens = ThemeFolder + "/theme308_tokens.json", ThemeBit = ThemeFolder + "/Lattice/lat308_bit.png";
        [Serializable] sealed class TokenColours { public string Wood = "", Lacquer = "", InlayDark = "", Nacre = ""; }
        [Serializable] sealed class TokenFile { public TokenColours colours = new TokenColours(); }
        // the kit's cell table (theme308_atlas.json), for a cell the atlas has no named sprite for
        const string ThemeAtlasJson = ThemeFolder + "/theme308_atlas.json", CutPrefix = "equip308_";
        static readonly string[] ThemeCells = { "white", "frame_select", "plaque_porcelain", "pip_petal", "motif_chrys", "sanggam_lotus", "keycap_porcelain", "line_najeon" };
        [Serializable] sealed class KitCell { public string name = ""; public int[] rect_unity; public int[] border_texels; }
        [Serializable] sealed class KitImport { public float pixelsPerUnit = 200f; }
        [Serializable] sealed class KitAtlas { public KitImport import_settings = new KitImport(); public KitCell[] cells; }

        [Serializable] sealed class ManifestTexture { public string file, kind, wrap; public int[] border; public bool mips; public int size; }
        [Serializable] sealed class ManifestFile { public string spec, generator; public ManifestTexture[] textures; }
        [Serializable] sealed class Ledger
        {
            public string appliedUtc = ""; public bool createdAsset;
            public List<string> createdFolders = new List<string>(); public List<string> reimported = new List<string>();
        }

        static string LedgerPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/UI308/Equipment/setup308_ledger.json"));

        public static string Execute(string argument)
        {
            string a = (argument ?? "").Trim();
            if (a == "dry") return Run(false);
            if (a == "apply") return Run(true);
            if (a == "revert") return Revert();
            if (a == "report") return Report();
            throw new ArgumentException("Expected dry, apply, revert or report");
        }

        static string Refusal()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED equip308 setup is Edit Mode only";
            if (EditorApplication.isCompiling) return "REFUSED scripts are compiling; call again when done";
            foreach (var path in new[] { TextureFolder, AssetFolder, AssetPath })
                foreach (var tree in ProtectedTrees)
                    if (path.IndexOf(tree, StringComparison.OrdinalIgnoreCase) >= 0) return "REFUSED protected tree in " + path;
            return null;
        }

        static string Run(bool apply)
        {
            string refused = Refusal(); if (refused != null) return refused;
            string manifestPath = TextureFolder + "/" + ManifestName;
            if (!File.Exists(manifestPath)) return "REFUSED missing " + manifestPath + " (copy the stage textures and refresh first)";
            var manifest = JsonUtility.FromJson<ManifestFile>(File.ReadAllText(manifestPath));
            if (manifest == null || manifest.textures == null || manifest.textures.Length == 0) return "REFUSED empty manifest " + manifestPath;
            // the manifest may only name this command's own nine textures (a bare file name: no folder, no other asset)
            foreach (var t in manifest.textures)
                if (t == null || string.IsNullOrEmpty(t.file) || t.file != Path.GetFileName(t.file) || !t.file.EndsWith(".png", StringComparison.Ordinal)
                    || Array.IndexOf(SpriteFiles, Path.GetFileNameWithoutExtension(t.file)) < 0)
                    return "REFUSED manifest names a file that is not one of the nine #308 textures: " + (t != null ? t.file : "<null>");
            // never write over something that is not this command's own asset (a file of another type, or one that does not load)
            if (AssetDatabase.LoadAssetAtPath<EquipmentScreen308SO>(AssetPath) == null
                && (File.Exists(AssetPath) || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(AssetPath, AssetPathToGUIDOptions.OnlyExistingAssets))))
                return "REFUSED " + AssetPath + " exists but is not an EquipmentScreen308SO; nothing was written";

            var log = new List<string>(); int changes = 0, missing = 0;
            var ledger = apply ? (LoadLedger() ?? new Ledger()) : null;
            foreach (var t in manifest.textures)
            {
                string path = TextureFolder + "/" + t.file;
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) { log.Add("MISSING " + path + " (not imported: copy the file, then refresh)"); missing++; continue; }
                if (Good(importer, t)) { log.Add("OK       " + path); continue; }
                changes++;
                if (!apply) { log.Add("WOULD import " + path + " as Sprite border=" + Border(t) + " mips=" + t.mips); continue; }
                Configure(importer, t); importer.SaveAndReimport();
                if (!ledger.reimported.Contains(path)) ledger.reimported.Add(path);
                log.Add("IMPORTED " + path + " as Sprite border=" + Border(t) + " mips=" + t.mips);
            }
            if (missing > 0) { log.Add((apply ? "REFUSED" : "DRY") + " " + missing + " texture(s) missing; nothing else was done"); return string.Join("\n", log); }

            foreach (var folder in new[] { ResourcesRoot, AssetFolder })
            {
                if (AssetDatabase.IsValidFolder(folder)) continue;
                changes++;
                if (!apply) { log.Add("WOULD create folder " + folder); continue; }
                AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder));
                ledger.createdFolders.Add(folder); log.Add("CREATED folder " + folder);
            }
            var asset = AssetDatabase.LoadAssetAtPath<EquipmentScreen308SO>(AssetPath);
            if (asset == null)
            {
                changes++;
                if (apply)
                {
                    asset = ScriptableObject.CreateInstance<EquipmentScreen308SO>(); asset.name = "EquipmentScreen308";
                    AssetDatabase.CreateAsset(asset, AssetPath); ledger.createdAsset = true; log.Add("CREATED " + AssetPath);
                }
                else log.Add("WOULD create " + AssetPath);
            }
            int rebind = 0;
            for (int i = 0; i < SpriteFiles.Length; i++)
            {
                var sprite = SpriteAt(SpriteFiles[i]);
                if (asset != null && Get(asset, i) == sprite && sprite != null) continue;
                rebind++;
                if (apply && asset != null) Set(asset, i, sprite);
            }
            if (rebind > 0)
            {
                changes++;
                log.Add((apply ? "BOUND " : "WOULD bind ") + rebind + " sprite field(s) on " + AssetPath);
                if (apply && asset != null) { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
            }
            if (BindTheme(asset, apply, log))
            {
                changes++;
                if (apply && asset != null) { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
            }
            if (apply)
            {
                ledger.appliedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                Directory.CreateDirectory(Path.GetDirectoryName(LedgerPath));
                File.WriteAllText(LedgerPath, JsonUtility.ToJson(ledger, true));
                log.Add("LEDGER " + LedgerPath);
                log.Add(changes == 0 ? "APPLIED 변경 없음 (nothing to write)" : "APPLIED changes=" + changes);
                log.Add(Report());
            }
            else log.Add("DRY changes=" + changes + " (nothing written)");
            return string.Join("\n", log);
        }

        /// <summary>The asset's Theme block from the deployed kit: atlas sprites by cell name, the 빗살 tile, the token colours.
        /// True when the block differs from what the kit gives (apply writes it). A kit that is not there, or an atlas the
        /// theme's setup has not sliced yet, changes nothing: the screen then draws the #304 look.</summary>
        static bool BindTheme(EquipmentScreen308SO asset, bool apply, List<string> log)
        {
            if (!File.Exists(ThemeAtlas) || !File.Exists(ThemeTokens))
            { log.Add("THEME kit not deployed (" + ThemeFolder + "): theme block left as it is; unbound = the #304 look"); return false; }
            var sprites = new Dictionary<string, Sprite>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ThemeAtlas)) { var sp = o as Sprite; if (sp != null) sprites[sp.name] = sp; }
            int toCut = CutMissingCells(asset, sprites, apply, log);
            var tokens = JsonUtility.FromJson<TokenFile>(File.ReadAllText(ThemeTokens));
            var want = new EquipTheme308();
            sprites.TryGetValue("white", out want.White); sprites.TryGetValue("frame_select", out want.FrameSelect);
            sprites.TryGetValue("plaque_porcelain", out want.Plaque); sprites.TryGetValue("pip_petal", out want.Petal);
            sprites.TryGetValue("motif_chrys", out want.Chrys); sprites.TryGetValue("sanggam_lotus", out want.Lotus);
            sprites.TryGetValue("keycap_porcelain", out want.Keycap); sprites.TryGetValue("line_najeon", out want.Line);
            want.LatticeBit = AssetDatabase.LoadAssetAtPath<Sprite>(ThemeBit);
            bool colours = tokens != null && tokens.colours != null
                && ColorUtility.TryParseHtmlString(tokens.colours.Wood, out want.Wood) && ColorUtility.TryParseHtmlString(tokens.colours.Lacquer, out want.Lacquer)
                && ColorUtility.TryParseHtmlString(tokens.colours.InlayDark, out want.InlayDark) && ColorUtility.TryParseHtmlString(tokens.colours.Nacre, out want.Nacre);
            if (!apply && toCut > 0 && want.LatticeBit != null && colours)
            { log.Add("WOULD bind the theme block on " + AssetPath + " after cutting " + toCut + " kit cell(s)"); return true; }
            if (!want.Bound)
            {
                log.Add("THEME kit incomplete (atlas sprites " + sprites.Count + ", lat308_bit " + (want.LatticeBit != null) + ", token colours " + colours
                    + "): run the theme's own setup first (UiTheme308 theme308-setup); theme block left as it is");
                return false;
            }
            var have = asset != null && asset.Theme != null ? asset.Theme : new EquipTheme308();
            bool same = have.White == want.White && have.FrameSelect == want.FrameSelect && have.Plaque == want.Plaque && have.Petal == want.Petal
                && have.LatticeBit == want.LatticeBit && have.Chrys == want.Chrys && have.Lotus == want.Lotus && have.Keycap == want.Keycap && have.Line == want.Line
                && have.Wood == want.Wood && have.Lacquer == want.Lacquer && have.InlayDark == want.InlayDark && have.Nacre == want.Nacre;
            if (same) { log.Add("OK       theme block (kit sprites + token colours)"); return false; }
            if (apply && asset != null) asset.Theme = want;
            log.Add((apply ? "BOUND " : "WOULD bind ") + "the theme block on " + AssetPath + " (kit sprites by cell name + Wood / Lacquer / InlayDark / Nacre from " + ThemeTokens + ")");
            return true;
        }

        /// <summary>A kit cell the atlas has no NAMED sprite for (the theme's setup has not sliced the atlas, or this editor did not
        /// take its sprite sheet) is cut from the atlas texture by theme308_atlas.json and kept as a sub-asset of this screen's own
        /// asset: it references the kit's texture, nothing is written under Theme/ (SPEC-MAP-OVERHAUL-308's setup does the same).
        /// Fills `sprites` with what it finds or makes; returns how many cells are (apply) or would be (dry) newly cut. Cut
        /// sprites no cell needs any more are removed on apply.</summary>
        static int CutMissingCells(EquipmentScreen308SO asset, Dictionary<string, Sprite> sprites, bool apply, List<string> log)
        {
            var own = new Dictionary<string, Sprite>();
            if (asset != null) foreach (var o in AssetDatabase.LoadAllAssetsAtPath(AssetPath)) { var sp = o as Sprite; if (sp != null && sp.name.StartsWith(CutPrefix, StringComparison.Ordinal)) own[sp.name] = sp; }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ThemeAtlas);
            var kit = File.Exists(ThemeAtlasJson) ? JsonUtility.FromJson<KitAtlas>(File.ReadAllText(ThemeAtlasJson)) : null;
            float ppu = kit != null && kit.import_settings != null && kit.import_settings.pixelsPerUnit > 0f ? kit.import_settings.pixelsPerUnit : 200f;
            var used = new HashSet<Sprite>(); int cut = 0;
            foreach (string cellName in ThemeCells)
            {
                if (sprites.ContainsKey(cellName)) continue;      // the kit's own named sprite
                KitCell spec = null;
                if (kit != null && kit.cells != null) foreach (var c in kit.cells) if (c != null && c.name == cellName) spec = c;
                if (texture == null || spec == null || spec.rect_unity == null || spec.rect_unity.Length != 4 || spec.border_texels == null || spec.border_texels.Length != 4) continue;
                var rect = new Rect(spec.rect_unity[0], spec.rect_unity[1], spec.rect_unity[2], spec.rect_unity[3]);
                var border = new Vector4(spec.border_texels[0], spec.border_texels[1], spec.border_texels[2], spec.border_texels[3]);
                if (rect.width <= 0f || rect.height <= 0f || rect.xMax > texture.width + .5f || rect.yMax > texture.height + .5f) continue;   // not the atlas the table describes
                own.TryGetValue(CutPrefix + cellName, out var kept);
                if (kept != null && kept.texture == texture && kept.rect == rect && kept.border == border && Mathf.Approximately(kept.pixelsPerUnit, ppu))
                { sprites[cellName] = kept; used.Add(kept); continue; }                                   // cut by an earlier run: a second run writes nothing
                cut++;
                if (!apply || asset == null) continue;
                var made = Sprite.Create(texture, rect, new Vector2(.5f, .5f), ppu, 0, SpriteMeshType.FullRect, border);
                made.name = CutPrefix + cellName;
                AssetDatabase.AddObjectToAsset(made, asset);
                sprites[cellName] = made; used.Add(made);
            }
            if (apply && asset != null)
                foreach (var old in own.Values)
                    if (old != null && !used.Contains(old)) { AssetDatabase.RemoveObjectFromAsset(old); UnityEngine.Object.DestroyImmediate(old, true); }
            if (cut > 0)
                log.Add((apply ? "CUT " : "WOULD cut ") + cut + " kit cell(s) from " + ThemeAtlas + " by theme308_atlas.json into " + AssetPath + " (the atlas has no named sprite for them)");
            return cut;
        }

        static string Revert()
        {
            string refused = Refusal(); if (refused != null) return refused;
            var ledger = LoadLedger();
            if (ledger == null) return "NOTHING to revert (no ledger at " + LedgerPath + ")";
            var log = new List<string>();
            if (ledger.createdAsset && AssetDatabase.LoadAssetAtPath<EquipmentScreen308SO>(AssetPath) != null)
                log.Add(AssetDatabase.DeleteAsset(AssetPath) ? "DELETED " + AssetPath : "FAILED to delete " + AssetPath);
            else log.Add("KEPT " + AssetPath + " (not created by this command, or already gone)");
            for (int i = ledger.createdFolders.Count - 1; i >= 0; i--)
            {
                string folder = ledger.createdFolders[i];
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                bool empty = AssetDatabase.FindAssets("", new[] { folder }).Length == 0;
                log.Add(empty ? (AssetDatabase.DeleteAsset(folder) ? "DELETED folder " + folder : "FAILED to delete folder " + folder) : "KEPT folder " + folder + " (not empty)");
            }
            string archived = LedgerPath.Replace(".json", ".reverted-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ") + ".json");
            File.Move(LedgerPath, archived);
            log.Add("LEDGER archived " + archived);
            log.Add("TEXTURES kept (" + ledger.reimported.Count + " reimported as sprites): delete " + TextureFolder + " by hand to remove them");
            return string.Join("\n", log);
        }

        static string Report()
        {
            var asset = AssetDatabase.LoadAssetAtPath<EquipmentScreen308SO>(AssetPath);
            if (asset == null) return "EQUIP308 missing " + AssetPath + " (run apply; the screen still draws without it)";
            int bound = 0; var missing = new List<string>();
            for (int i = 0; i < SpriteFiles.Length; i++) { if (Get(asset, i) != null) bound++; else missing.Add(SpriteFiles[i]); }
            return "EQUIP308 asset=" + AssetPath + " sprites=" + bound + "/" + SpriteFiles.Length + (missing.Count > 0 ? " missing=" + string.Join(",", missing) : "")
                + " inkScale=" + asset.InkDisplayScale + " direct=" + asset.DirectEquipFromCandidate + " removeKey=" + asset.RemoveKey
                + " theme=" + (EquipmentScreen308SO.Themed(asset) != null ? "bound" : asset.ThemeOn ? "unbound(#304 look)" : "off") + " motifs=" + asset.ThemeMotifs + " phase2=" + asset.ThemePhase2
                + " resources=" + (Resources.Load<EquipmentScreen308SO>(EquipmentScreen308SO.ResourcePath) != null ? "ok" : "NOT FOUND");
        }

        static Ledger LoadLedger() => File.Exists(LedgerPath) ? JsonUtility.FromJson<Ledger>(File.ReadAllText(LedgerPath)) : null;
        static Sprite SpriteAt(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(TextureFolder + "/" + name + ".png");
        static Vector4 Border(ManifestTexture t) => t.border != null && t.border.Length == 4 ? new Vector4(t.border[0], t.border[1], t.border[2], t.border[3]) : Vector4.zero;

        static bool Good(TextureImporter i, ManifestTexture t)
            => i.textureType == TextureImporterType.Sprite && i.spriteImportMode == SpriteImportMode.Single && i.spriteBorder == Border(t)
            && i.mipmapEnabled == t.mips && i.alphaIsTransparency && i.wrapMode == TextureWrapMode.Clamp && !i.isReadable
            && i.npotScale == TextureImporterNPOTScale.None && i.textureCompression == TextureImporterCompression.Uncompressed;

        static void Configure(TextureImporter i, ManifestTexture t)
        {
            var s = new TextureImporterSettings(); i.ReadTextureSettings(s);
            s.textureType = TextureImporterType.Sprite; s.spriteMode = (int)SpriteImportMode.Single; s.spritePixelsPerUnit = 100;
            s.spriteMeshType = SpriteMeshType.FullRect; s.spriteBorder = Border(t); s.spriteGenerateFallbackPhysicsShape = false;
            s.alphaSource = TextureImporterAlphaSource.FromInput; s.alphaIsTransparency = true;
            s.mipmapEnabled = t.mips; s.wrapMode = TextureWrapMode.Clamp; s.filterMode = FilterMode.Bilinear; s.sRGBTexture = true;
            s.npotScale = TextureImporterNPOTScale.None; s.readable = false;
            i.SetTextureSettings(s);
            i.textureCompression = TextureImporterCompression.Uncompressed;
        }

        static Sprite Get(EquipmentScreen308SO a, int i)
        {
            switch (i)
            {
                case 0: return a.ItemFrame; case 1: return a.MarkEffect; case 2: return a.MarkCompare; case 3: return a.MarkUpgrade; case 4: return a.MarkCandidates;
                case 5: return a.MarkBody; case 6: return a.MarkPower; case 7: return a.MarkTier; default: return a.MarkLearned;
            }
        }

        static void Set(EquipmentScreen308SO a, int i, Sprite s)
        {
            switch (i)
            {
                case 0: a.ItemFrame = s; break; case 1: a.MarkEffect = s; break; case 2: a.MarkCompare = s; break; case 3: a.MarkUpgrade = s; break;
                case 4: a.MarkCandidates = s; break; case 5: a.MarkBody = s; break; case 6: a.MarkPower = s; break; case 7: a.MarkTier = s; break;
                default: a.MarkLearned = s; break;
            }
        }
    }
}
