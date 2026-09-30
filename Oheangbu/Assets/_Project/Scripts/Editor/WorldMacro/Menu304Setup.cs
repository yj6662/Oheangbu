using System.Collections.Generic;
using System.IO;
using Oheangbu.App.World.UI;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#304 menu area setup (Edit Mode, idempotent, no dialogs). Queue: Oheangbu.EditorTools.WorldMacro.Menu304Setup
    /// Execute("menu304-setup"): imports Textures/menu/mouse_*.png as Sprites (same settings as their import304.json manifest,
    /// so ui304-import / ui304-setup agree) and creates / refreshes Assets/_Project/Resources/UI304/menu/MenuInputIcons304.asset,
    /// which PlaytestUiRoot loads with Resources.Load (MenuInputIcons304.ResourcePath). "menu304-report" lists what is wired.</summary>
    public static class Menu304Setup
    {
        const string TextureFolder = "Assets/_Project/Art/UI/UI304/Textures/menu";
        const string ResourceFolder = "Assets/_Project/Resources/UI304/menu";
        const string AssetPath = ResourceFolder + "/MenuInputIcons304.asset";
        static readonly string[] Icons = { "mouse_left", "mouse_right", "mouse_middle", "mouse_move" };

        public static string Execute(string argument)
        {
            string a = (argument ?? "").Trim();
            if (a == "menu304-report") return Report();
            if (a != "menu304-setup") return "Expected menu304-setup or menu304-report.";
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "menu304-setup is Edit Mode only.";
            if (EditorApplication.isCompiling) return "Scripts are compiling; call menu304-setup again when done.";
            var log = new List<string>();
            int changed = 0;
            foreach (var name in Icons)
            {
                string path = TextureFolder + "/" + name + ".png";
                if (!File.Exists(path)) { log.Add("MISSING " + path + " (run Art/UI304/gen/menu_assets.py)"); continue; }
                if (AssetImporter.GetAtPath(path) == null) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) { log.Add("NOT_IMPORTED " + path); continue; }
                var s = new TextureImporterSettings(); importer.ReadTextureSettings(s);
                bool dirty = s.textureType != TextureImporterType.Sprite || s.spriteMode != (int)SpriteImportMode.Single || !s.mipmapEnabled
                    || s.wrapMode != TextureWrapMode.Clamp || !s.alphaIsTransparency || s.spriteMeshType != SpriteMeshType.FullRect
                    || importer.textureCompression != TextureImporterCompression.Uncompressed;
                if (!dirty) continue;
                s.textureType = TextureImporterType.Sprite; s.spriteMode = (int)SpriteImportMode.Single; s.spritePixelsPerUnit = 100;
                s.spriteAlignment = (int)SpriteAlignment.Center; s.spritePivot = new Vector2(.5f, .5f); s.spriteMeshType = SpriteMeshType.FullRect;
                s.spriteBorder = Vector4.zero; s.spriteGenerateFallbackPhysicsShape = false;
                s.alphaSource = TextureImporterAlphaSource.FromInput; s.alphaIsTransparency = true;
                s.mipmapEnabled = true; s.wrapMode = TextureWrapMode.Clamp; s.filterMode = FilterMode.Bilinear; s.sRGBTexture = true;
                s.npotScale = TextureImporterNPOTScale.None; s.readable = false; s.streamingMipmaps = false;
                importer.SetTextureSettings(s);
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport(); changed++;
            }
            log.Add("TEXTURES reimported=" + changed);

            if (!AssetDatabase.IsValidFolder("Assets/_Project/Resources")) AssetDatabase.CreateFolder("Assets/_Project", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Resources/UI304")) AssetDatabase.CreateFolder("Assets/_Project/Resources", "UI304");
            if (!AssetDatabase.IsValidFolder(ResourceFolder)) AssetDatabase.CreateFolder("Assets/_Project/Resources/UI304", "menu");
            var icons = AssetDatabase.LoadAssetAtPath<MenuInputIcons304>(AssetPath);
            if (icons == null)
            {
                icons = ScriptableObject.CreateInstance<MenuInputIcons304>(); icons.name = "MenuInputIcons304";
                AssetDatabase.CreateAsset(icons, AssetPath); log.Add("ASSET created " + AssetPath);
            }
            icons.MouseLeft = Load("mouse_left"); icons.MouseRight = Load("mouse_right");
            icons.MouseMiddle = Load("mouse_middle"); icons.MouseMove = Load("mouse_move");
            EditorUtility.SetDirty(icons); AssetDatabase.SaveAssets();
            log.Add(Report());
            return string.Join("\n", log);
        }

        static Sprite Load(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(TextureFolder + "/" + name + ".png");

        static string Report()
        {
            var icons = AssetDatabase.LoadAssetAtPath<MenuInputIcons304>(AssetPath);
            if (icons == null) return "MENU304 icons=missing (" + AssetPath + ")";
            int n = 0; foreach (var s in new[] { icons.MouseLeft, icons.MouseRight, icons.MouseMiddle, icons.MouseMove }) if (s != null) n++;
            return "MENU304 icons=" + n + "/4 asset=" + AssetPath;
        }
    }
}
