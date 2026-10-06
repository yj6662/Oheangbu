using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEditor;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactRebuildAuthoring
    {
        const string UiArtAssets246 = "Assets/_Project/Art/UI/Equipment246";
        const string UiArtOutput246 = Output + "/UIArt246";
        public static string UiArtImport246()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || !scene.path.Contains("slice-5e82ecd76d2a/"))
                throw new InvalidOperationException("Candidate Edit required");
            string[] names = { "portrait", "brush", "head", "body", "hands", "feet", "accessory" };
            var sprites = new List<Sprite>();
            foreach (string name in names)
            {
                string path = UiArtAssets246 + "/" + name + "-v1.png";
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.isReadable = false;
                importer.sRGBTexture = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = name == "portrait" ? 2048 : 256;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
                sprites.Add(AssetDatabase.LoadAssetAtPath<Sprite>(path));
            }
            if (sprites.Any(x => x == null)) throw new Exception("Missing sprite");
            string artPath = UiArtAssets246 + "/EquipmentArt.asset";
            var art = AssetDatabase.LoadAssetAtPath<EquipmentUiArtSO>(artPath);
            if (art == null)
            {
                art = ScriptableObject.CreateInstance<EquipmentUiArtSO>();
                AssetDatabase.CreateAsset(art, artPath);
            }
            art.Portrait = sprites[0]; art.Slots = sprites.Skip(1).ToArray();
            var session = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
            if (session.Content.EquipmentCatalog == null) throw new Exception("Candidate equipment catalog required");
            session.Content.EquipmentUiArt = art;
            EditorUtility.SetDirty(art); EditorUtility.SetDirty(session.Content);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(UiArtOutput246);
            string result = "Imported 7 alpha sprites; candidate Content bound to EquipmentArt; scene geometry and save untouched.";
            File.WriteAllText(UiArtOutput246 + "/import.txt", result);
            return result;
        }

        public static string UiArtCheck246()
        {
            var session = VillageSession();
            if (!EditorApplication.isPlaying || !session.TestSaveSuffix.StartsWith("_compact_slice_"))
                throw new Exception("Private diagnostic Play required");
            var lines = new List<string>();
            void Check(bool ok, string label)
            {
                lines.Add((ok ? "PASS " : "FAIL ") + label);
                File.WriteAllText(UiArtOutput246 + "/runtime-art.txt", string.Join("\n", lines));
                if (!ok) throw new Exception(label);
            }
            var ui = PlaytestUiRoot.Instance;
            ui.OpenPage("소지품"); Canvas.ForceUpdateCanvases();
            string before = JsonUtility.ToJson(session.Progress);
            var art = session.Content.EquipmentUiArt;
            Check(art != null && art.Portrait != null && art.Slots.Length == 6 && art.Slots.All(x => x != null), "seven candidate art bindings");
            var graphics = ui.GetComponentsInChildren<EquipmentInkGraphic>();
            Check(graphics.All(g => g.Artwork != null && g.mainTexture == g.Artwork.texture && g.canvasRenderer != null), "inventory uses generated texture with CanvasRenderer");
            // #308 (SPEC-UI-EQUIPMENT-308, question 2): the 3-column 소지품 has no paper doll; the portrait asset stays bound in the art SO
            Check(graphics.All(g => g.Symbol >= 0), "3-column inventory draws no paper doll (#308)");
            Check(ui.GetComponentsInChildren<EquipmentDropSlot>().Length == 6, "all six equipment drop targets remain");
            var drag = ui.GetComponentsInChildren<EquipmentDragItem>().First();
            var source = drag.GetComponentInChildren<EquipmentInkGraphic>();
            var ev = new PointerEventData(EventSystem.current) { pointerDrag = drag.gameObject, position = new Vector2(600, 500) };
            drag.OnBeginDrag(ev);
            var ghost = GameObject.Find("DraggedEquipment").GetComponent<EquipmentInkGraphic>();
            Check(ghost.Artwork == source.Artwork && ghost.color.r == 1 && !ghost.raycastTarget, "drag ghost keeps the generated sprite and never captures pointer");
            drag.OnEndDrag(ev);
            // #308: a Gear_ row now equips at once; selecting is the slot's click (the focus moves into 바꿔 낄 것, nothing is saved)
            ui.GetComponentsInChildren<Button>().First(b => b.name.StartsWith("Equipped_")).onClick.Invoke();
            Canvas.ForceUpdateCanvases();
            Check(ui.GetComponentsInChildren<Image>().Any(x => x.name == "SelectedGearPaper"), "selected icon keeps readable paper backing");
            Check(before == JsonUtility.ToJson(session.Progress), "opening, selecting and dragging art never changes progress");
            return string.Join("\n", lines);
        }
    }
}
