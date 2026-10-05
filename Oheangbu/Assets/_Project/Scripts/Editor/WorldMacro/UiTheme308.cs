using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#308 UI theme kit import (SPEC-UI-THEME-308 section 5.5, DECISIONS D308-15). Queue:
    /// Oheangbu.EditorTools.WorldMacro.UiTheme308 Execute "theme308-setup:dry" | "theme308-setup" | "theme308-report".
    /// Edit Mode only, idempotent, never opens a dialog; a refusal is a returned string.
    ///   theme308-setup:dry  what setup would do; writes nothing.
    ///   theme308-setup      1. theme308_atlas.png: the import settings written in theme308_atlas.json (Sprite / Multiple, FullRect,
    ///                          200 px per unit = 2 texels per design px, sRGB, mips, Clamp, Trilinear, uncompressed) and one
    ///                          named sprite per cell (name = cell name, rect = rect_unity, border = border_texels L B R T).
    ///                       2. Lattice/lat308_*.png: the settings written in Lattice/import308.json (Sprite / Single, FullRect,
    ///                          Repeat, Alpha8, mips).
    ///                       Only a texture whose settings differ is reimported (TextureImporter.SaveAndReimport: its own .meta).
    ///   theme308-report     the import state and the named sprites that load.
    /// This is the IMPORT half of the Spec's setup only: UiTheme308SO (the token asset) and theme308-revert do not exist yet
    /// (Spec "남은 일" 4). The consumers read the cells by NAME: SPEC-UI-EQUIPMENT-308 binds the named sprites, and both it and
    /// SPEC-MAP-OVERHAUL-308 cut a cell from the atlas by theme308_atlas.json when a named sprite is missing.
    /// Writes nothing outside Assets/_Project/Art/UI/UI308/Theme (the .meta files of the kit textures). No scene, no other
    /// asset, no AssetDatabase.SaveAssets. Way back: delete that folder after the consumers' theme blocks are unbound.</summary>
    public static class UiTheme308
    {
        const string Folder = "Assets/_Project/Art/UI/UI308/Theme";
        const string AtlasPath = Folder + "/theme308_atlas.png", AtlasJson = Folder + "/theme308_atlas.json";
        const string TileFolder = Folder + "/Lattice", TileJson = TileFolder + "/import308.json", TilePrefix = "lat308_";
        static readonly string[] ProtectedTrees = { "Watershed295", "Reworld292", "MountainTrail285" };

        [Serializable] sealed class ImportSettings
        {
            public string spriteMode = "Multiple", wrap = "Clamp", filter = "Trilinear", format = "RGBA32";
            public float pixelsPerUnit = 200f; public bool sRGB = true, alphaIsTransparency = true, mips = true; public int maxSize = 512;
        }
        [Serializable] sealed class Cell { public string name = ""; public int[] rect_unity; public int[] border_texels; }
        [Serializable] sealed class AtlasFile { public int[] size; public string sha256 = ""; public ImportSettings import_settings = new ImportSettings(); public Cell[] cells; }
        [Serializable] sealed class TileTexture
        {
            public string file = "", wrap = "Repeat", filter = "Trilinear", format = "Alpha8";
            public float pixelsPerUnit = 200f; public bool sRGB, alphaIsTransparency = true, mips = true; public int maxSize = 512;
        }
        [Serializable] sealed class TileFile { public TileTexture[] textures; }

        public static string Execute(string argument)
        {
            string a = (argument ?? "").Trim();
            if (a == "theme308-setup:dry") return Setup(false);
            if (a == "theme308-setup") return Setup(true);
            if (a == "theme308-report") return Report();
            throw new ArgumentException("Expected theme308-setup:dry, theme308-setup or theme308-report");
        }

        static string Refusal()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED theme308 setup is Edit Mode only";
            if (EditorApplication.isCompiling) return "REFUSED scripts are compiling; call again when done";
            foreach (var tree in ProtectedTrees)
                if (Folder.IndexOf(tree, StringComparison.OrdinalIgnoreCase) >= 0) return "REFUSED protected tree in " + Folder;
            return null;
        }

        static string Setup(bool apply)
        {
            string refused = Refusal(); if (refused != null) return refused;
            if (!File.Exists(AtlasJson) || !File.Exists(AtlasPath)) return "REFUSED missing " + AtlasPath + " / " + AtlasJson + " (copy the kit stage and refresh first)";
            var atlas = JsonUtility.FromJson<AtlasFile>(File.ReadAllText(AtlasJson));
            if (atlas == null || atlas.cells == null || atlas.cells.Length == 0) return "REFUSED no cells in " + AtlasJson;
            // the picture on disk must be the one the cell table was written for
            string sha = Sha256(AtlasPath);
            if (!string.IsNullOrEmpty(atlas.sha256) && !string.Equals(sha, atlas.sha256, StringComparison.OrdinalIgnoreCase))
                return "REFUSED " + AtlasPath + " (sha256 " + Short(sha) + ") is not the atlas " + AtlasJson + " describes (" + Short(atlas.sha256) + "); nothing was written";
            foreach (var c in atlas.cells)
                if (c == null || string.IsNullOrEmpty(c.name) || c.rect_unity == null || c.rect_unity.Length != 4 || c.border_texels == null || c.border_texels.Length != 4
                    || c.rect_unity[0] < 0 || c.rect_unity[1] < 0 || c.rect_unity[2] <= 0 || c.rect_unity[3] <= 0
                    || (atlas.size != null && atlas.size.Length == 2 && (c.rect_unity[0] + c.rect_unity[2] > atlas.size[0] || c.rect_unity[1] + c.rect_unity[3] > atlas.size[1])))
                    return "REFUSED a cell of " + AtlasJson + " has no usable rect_unity / border_texels: " + (c != null ? c.name : "<null>");
            var importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
            if (importer == null) return "REFUSED " + AtlasPath + " is not imported as a texture (copy the file, then refresh)";

            var log = new List<string>(); int changes = 0;
            if (AtlasGood(importer, atlas)) log.Add("OK       " + AtlasPath + " (" + atlas.cells.Length + " cells in the sprite sheet)");
            else
            {
                changes++;
                if (!apply) log.Add("WOULD import " + AtlasPath + " as Sprite / Multiple, " + atlas.cells.Length + " named cells, " + atlas.import_settings.pixelsPerUnit + " px per unit, uncompressed, mips " + atlas.import_settings.mips);
                else
                {
                    ConfigureAtlas(importer, atlas); importer.SaveAndReimport();
                    log.Add("IMPORTED " + AtlasPath + " as Sprite / Multiple, " + atlas.cells.Length + " named cells");
                }
            }

            if (!File.Exists(TileJson)) log.Add("TILES none: " + TileJson + " not found (the lattice tiles are left as they are)");
            else
            {
                var tiles = JsonUtility.FromJson<TileFile>(File.ReadAllText(TileJson));
                foreach (var t in tiles != null && tiles.textures != null ? tiles.textures : new TileTexture[0])
                {
                    // only the kit's own tiles: a bare file name with the kit prefix
                    if (t == null || string.IsNullOrEmpty(t.file) || t.file != Path.GetFileName(t.file) || !t.file.StartsWith(TilePrefix, StringComparison.Ordinal)
                        || !t.file.EndsWith(".png", StringComparison.Ordinal))
                    { log.Add("SKIPPED a tile entry that is not a " + TilePrefix + "*.png file name: " + (t != null ? t.file : "<null>")); continue; }
                    string path = TileFolder + "/" + t.file;
                    var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (ti == null) { log.Add("MISSING  " + path + " (not imported: copy the file, then refresh)"); continue; }
                    if (TileGood(ti, t)) { log.Add("OK       " + path); continue; }
                    changes++;
                    if (!apply) { log.Add("WOULD import " + path + " as Sprite / Single, " + t.wrap + ", " + t.format); continue; }
                    ConfigureTile(ti, t); ti.SaveAndReimport();
                    log.Add("IMPORTED " + path + " as Sprite / Single, " + t.wrap + ", " + t.format);
                }
            }
            log.Add(apply ? (changes == 0 ? "APPLIED 변경 없음 (nothing to write)" : "APPLIED changes=" + changes) : "DRY changes=" + changes + " (nothing written)");
            if (apply) log.Add(Report());
            return string.Join("\n", log);
        }

        static string Report()
        {
            if (!File.Exists(AtlasJson) || !File.Exists(AtlasPath)) return "THEME308 kit not deployed (" + Folder + ")";
            var atlas = JsonUtility.FromJson<AtlasFile>(File.ReadAllText(AtlasJson));
            int cells = atlas != null && atlas.cells != null ? atlas.cells.Length : 0;
            var importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
            var named = new HashSet<string>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(AtlasPath)) { var sp = o as Sprite; if (sp != null) named.Add(sp.name); }
            int found = 0; var missing = new List<string>();
            for (int i = 0; i < cells; i++) { if (named.Contains(atlas.cells[i].name)) found++; else missing.Add(atlas.cells[i].name); }
            int tiles = 0, tileSprites = 0;
            if (Directory.Exists(TileFolder))
                foreach (var file in Directory.GetFiles(TileFolder, TilePrefix + "*.png"))
                {
                    tiles++;
                    if (AssetDatabase.LoadAssetAtPath<Sprite>(TileFolder + "/" + Path.GetFileName(file)) != null) tileSprites++;
                }
            return "THEME308 atlas=" + AtlasPath + " sha256=" + Short(Sha256(AtlasPath)) + (atlas != null && string.Equals(Sha256(AtlasPath), atlas.sha256, StringComparison.OrdinalIgnoreCase) ? "(= json)" : "(NOT the json's)")
                + " import=" + (importer != null && atlas != null && AtlasGood(importer, atlas) ? "ok" : "differs from theme308_atlas.json")
                + " named sprites=" + found + "/" + cells
                + (found < cells ? " (missing: " + string.Join(",", missing) + " - consumers cut a missing cell from the atlas by theme308_atlas.json)" : "")
                + " lattice tiles as sprites=" + tileSprites + "/" + tiles
                + " tokens=" + (File.Exists(Folder + "/theme308_tokens.json") ? "present" : "MISSING");
        }

        // ------------------------------------------------------------------ atlas
        static FilterMode Filter(string name) => name == "Point" ? FilterMode.Point : name == "Bilinear" ? FilterMode.Bilinear : FilterMode.Trilinear;
        static TextureWrapMode Wrap(string name) => name == "Repeat" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;

        static Rect CellRect(Cell c) => new Rect(c.rect_unity[0], c.rect_unity[1], c.rect_unity[2], c.rect_unity[3]);
        static Vector4 CellBorder(Cell c) => new Vector4(c.border_texels[0], c.border_texels[1], c.border_texels[2], c.border_texels[3]);

#pragma warning disable 0618   // TextureImporter.spritesheet / SpriteMetaData: marked obsolete in favour of the 2D Sprite package's data provider, which this assembly does not reference
        static bool AtlasGood(TextureImporter i, AtlasFile atlas)
        {
            var s = atlas.import_settings;
            if (i.textureType != TextureImporterType.Sprite || i.spriteImportMode != SpriteImportMode.Multiple || !Mathf.Approximately(i.spritePixelsPerUnit, s.pixelsPerUnit)
                || i.sRGBTexture != s.sRGB || i.mipmapEnabled != s.mips || i.alphaIsTransparency != s.alphaIsTransparency || i.wrapMode != Wrap(s.wrap) || i.filterMode != Filter(s.filter)
                || i.isReadable || i.npotScale != TextureImporterNPOTScale.None || i.textureCompression != TextureImporterCompression.Uncompressed || i.maxTextureSize != s.maxSize)
                return false;
            var sheet = i.spritesheet;
            if (sheet == null || sheet.Length != atlas.cells.Length) return false;
            var byName = new Dictionary<string, SpriteMetaData>();
            foreach (var m in sheet) byName[m.name] = m;
            foreach (var c in atlas.cells)
                if (!byName.TryGetValue(c.name, out var m) || m.rect != CellRect(c) || m.border != CellBorder(c)) return false;
            return true;
        }

        static void ConfigureAtlas(TextureImporter i, AtlasFile atlas)
        {
            var set = atlas.import_settings;
            var s = new TextureImporterSettings(); i.ReadTextureSettings(s);
            s.textureType = TextureImporterType.Sprite; s.spriteMode = (int)SpriteImportMode.Multiple; s.spritePixelsPerUnit = set.pixelsPerUnit;
            s.spriteMeshType = SpriteMeshType.FullRect; s.spriteExtrude = 0; s.spriteGenerateFallbackPhysicsShape = false;
            s.alphaSource = TextureImporterAlphaSource.FromInput; s.alphaIsTransparency = set.alphaIsTransparency;
            s.mipmapEnabled = set.mips; s.wrapMode = Wrap(set.wrap); s.filterMode = Filter(set.filter); s.sRGBTexture = set.sRGB;
            s.npotScale = TextureImporterNPOTScale.None; s.readable = false;
            i.SetTextureSettings(s);
            i.textureCompression = TextureImporterCompression.Uncompressed; i.maxTextureSize = set.maxSize;
            var sheet = new SpriteMetaData[atlas.cells.Length];
            for (int k = 0; k < sheet.Length; k++)
            {
                var c = atlas.cells[k];
                sheet[k] = new SpriteMetaData { name = c.name, rect = CellRect(c), border = CellBorder(c), alignment = (int)SpriteAlignment.Center, pivot = new Vector2(.5f, .5f) };
            }
            i.spritesheet = sheet;
        }
#pragma warning restore 0618

        // ------------------------------------------------------------------ lattice tiles
        static TextureImporterFormat TileFormat(TileTexture t) => t.format == "Alpha8" ? TextureImporterFormat.Alpha8 : TextureImporterFormat.RGBA32;

        static bool TileGood(TextureImporter i, TileTexture t)
            => i.textureType == TextureImporterType.Sprite && i.spriteImportMode == SpriteImportMode.Single && Mathf.Approximately(i.spritePixelsPerUnit, t.pixelsPerUnit)
            && i.spriteBorder == Vector4.zero && i.sRGBTexture == t.sRGB && i.mipmapEnabled == t.mips && i.wrapMode == Wrap(t.wrap) && i.filterMode == Filter(t.filter)
            && !i.isReadable && i.npotScale == TextureImporterNPOTScale.None && i.maxTextureSize == t.maxSize
            && i.GetDefaultPlatformTextureSettings().format == TileFormat(t);

        static void ConfigureTile(TextureImporter i, TileTexture t)
        {
            var s = new TextureImporterSettings(); i.ReadTextureSettings(s);
            s.textureType = TextureImporterType.Sprite; s.spriteMode = (int)SpriteImportMode.Single; s.spritePixelsPerUnit = t.pixelsPerUnit;
            s.spriteMeshType = SpriteMeshType.FullRect; s.spriteBorder = Vector4.zero; s.spriteExtrude = 0; s.spriteGenerateFallbackPhysicsShape = false;
            s.alphaSource = TextureImporterAlphaSource.FromInput; s.alphaIsTransparency = t.alphaIsTransparency;
            s.mipmapEnabled = t.mips; s.wrapMode = Wrap(t.wrap); s.filterMode = Filter(t.filter); s.sRGBTexture = t.sRGB;
            s.npotScale = TextureImporterNPOTScale.None; s.readable = false;
            i.SetTextureSettings(s);
            i.maxTextureSize = t.maxSize;
            var platform = i.GetDefaultPlatformTextureSettings();
            platform.format = TileFormat(t); platform.textureCompression = TextureImporterCompression.Uncompressed; platform.maxTextureSize = t.maxSize;
            i.SetPlatformTextureSettings(platform);
        }

        static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        static string Short(string sha) => string.IsNullOrEmpty(sha) ? "?" : sha.Substring(0, Math.Min(16, sha.Length));
    }
}
