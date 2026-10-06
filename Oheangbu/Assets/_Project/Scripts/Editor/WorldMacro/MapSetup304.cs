using System;
using System.Collections.Generic;
using System.IO;
using Oheangbu.App.World.UI;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #304 map area setup. Queue: Oheangbu.EditorTools.WorldMacro.MapSetup304 Execute "map304-setup" | "map304-report".
    //   map304-setup: imports Textures/map (UiOverhaul304 "ui304-import" reads Textures/map/import304.json), then creates or
    //   refreshes Resources/UI304/map/MapStyle304.asset (MapStyle304SO.ResourcePath) and wires its sprites. Tunables and
    //   text already edited in the asset are kept (":reset" restores the code defaults). Idempotent, Edit Mode only, no dialog.
    //   Run after "ui304-setup" (the foundation sprites the map also uses: sheet_map, arrow, enso, ring_dashed, disc).
    //   D308-25 (map 5): also wires MapStyle304SO.SheetWide = Textures/map/sheet_map_wide.png (the 1556 x 820 sheet picture,
    //   Tools/Art/map5_sheet_wide.py). Only the style asset is saved (SaveAssetIfDirty: other sessions' dirty assets stay).
    public static class MapSetup304
    {
        const string TextureFolder = "Assets/_Project/Art/UI/UI304/Textures/map";
        const string ResourceFolder = "Assets/_Project/Resources/UI304/map";
        const string AssetPath = ResourceFolder + "/MapStyle304.asset";

        public static string Execute(string argument)
        {
            string a = (argument ?? "").Trim();
            if (a == "map304-report") return Report();
            if (a == "map304-setup" || a == "map304-setup:reset") return Setup(a.EndsWith(":reset", StringComparison.Ordinal));
            throw new ArgumentException("Expected map304-setup[:reset] or map304-report.");
        }

        static string Setup(bool reset)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("map304-setup is Edit Mode only.");
            if (EditorApplication.isCompiling) throw new InvalidOperationException("Scripts are compiling; call map304-setup again when done.");
            var log = new List<string>();
            AssetDatabase.Refresh(ImportAssetOptions.Default);
            log.Add(UiOverhaul304.Execute("ui304-import"));
            Folder("Assets/_Project/Resources/UI304"); Folder(ResourceFolder);
            var style = AssetDatabase.LoadAssetAtPath<MapStyle304SO>(AssetPath);
            if (style == null)
            {
                style = ScriptableObject.CreateInstance<MapStyle304SO>(); style.name = "MapStyle304";
                AssetDatabase.CreateAsset(style, AssetPath); log.Add("MAPSTYLE created " + AssetPath);
            }
            else if (reset)
            {
                var fresh = ScriptableObject.CreateInstance<MapStyle304SO>();
                EditorUtility.CopySerialized(fresh, style); UnityEngine.Object.DestroyImmediate(fresh); style.name = "MapStyle304";
                log.Add("MAPSTYLE reset to the code defaults");
            }
            else log.Add("MAPSTYLE kept tunables / text");
            int missing = 0;
            Sprite S(string name)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TextureFolder + "/" + name + ".png");
                if (sprite == null) { missing++; log.Add("SPRITE missing " + name + " (run Art/UI304/gen/map_assets.py, then map304-setup again)"); }
                return sprite;
            }
            style.PictRest = S("pict_rest"); style.PictCave = S("pict_cave"); style.PictMountain = S("pict_mountain");
            style.PictGate = S("pict_gate"); style.PictVillage = S("pict_village"); style.Coin = S("coin_tongbo");
            style.VariantRing = S("ring_variant"); style.SlipFrame = S("slip_frame");
            style.SheetWide = AssetDatabase.LoadAssetAtPath<Sprite>(TextureFolder + "/sheet_map_wide.png");
            if (style.SheetWide == null) { missing++; log.Add("SPRITE missing sheet_map_wide (Tools/Art/map5_sheet_wide.py builds it from sheet_map.png; until then the map draws the 800x820 sheet stretched)"); }
            EditorUtility.SetDirty(style); AssetDatabase.SaveAssetIfDirty(style);
            log.Add("SPRITES missing=" + missing);
            log.Add(Report());
            return string.Join("\n", log);
        }

        static string Report()
        {
            var style = AssetDatabase.LoadAssetAtPath<MapStyle304SO>(AssetPath);
            if (style == null) return "MAPSTYLE missing " + AssetPath + " (the presenter runs on code defaults and the UIIcons pictures)";
            var loaded = Resources.Load<MapStyle304SO>(MapStyle304SO.ResourcePath);
            return "MAPSTYLE " + AssetPath + " resources=" + (loaded == style ? "ok" : "MISMATCH") +
                   " rest=" + Name(style.PictRest) + " cave=" + Name(style.PictCave) + " mountain=" + Name(style.PictMountain) +
                   " gate=" + Name(style.PictGate) + " village=" + Name(style.PictVillage) + " coin=" + Name(style.Coin) +
                   " ring=" + Name(style.VariantRing) + " frame=" + Name(style.SlipFrame) + (style.SlipFrame != null ? " border=" + style.SlipFrame.border : "") +
                   " objective=" + style.Objective + " daedong=" + style.Daedong + " sheet=" + Name(style.SheetWide) + " " + style.SheetRect + " slipRealm=" + style.SlipRealm;
        }

        static string Name(UnityEngine.Object o) => o != null ? o.name : "MISSING";

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'), leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) Folder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
