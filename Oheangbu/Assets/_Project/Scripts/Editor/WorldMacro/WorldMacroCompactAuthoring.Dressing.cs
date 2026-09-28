using System;
using System.IO;
using System.Linq;
using Oheangbu.Data.World;
using Oheangbu.App.World.Dressing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroCompactAuthoring
    {
        static string DressingReceipt()
        {
            string json=JsonUtility.ToJson(WorldMacroDressingAuthoring.CompactProgress,true);File.WriteAllText(Path.Combine(Output,"dressing_progress.json"),json);return json;
        }
        static string BeginDressing()
        {
            RequireCompact();var p=ReadProgress();
            var sheet=p.assets.Select(a=>AssetDatabase.LoadMainAssetAtPath(a.target)).OfType<WorldMacroDressingSheetSO>().Single();
            var landmarks=p.assets.Select(a=>AssetDatabase.LoadMainAssetAtPath(a.target)).OfType<WorldMacroLandmarkSheetSO>().Single();
            WorldMacroDressingAuthoring.BeginCompact(sheet,landmarks);return DressingReceipt();
        }
        static string StepDressing()
        {
            RequireCompact();double start=EditorApplication.timeSinceStartup;
            for(int i=0;i<16;i++)
            {
                if(WorldMacroDressingAuthoring.StepCompact(4))break;
                if((EditorApplication.timeSinceStartup-start)*1000>=300)break;
            }
            return DressingReceipt();
        }
        static string FinishDressing()
        {
            RequireCompact();WorldMacroDressingAuthoring.FinishCompact();
            var renderer=Object.FindFirstObjectByType<WorldMacroDressingRenderer>();EditorUtility.SetDirty(renderer.Sheet);AssetDatabase.SaveAssetIfDirty(renderer.Sheet);renderer.ResetCache();EditorUtility.SetDirty(renderer);EditorSceneManager.SaveScene(SceneManager.GetActiveScene());return DressingReceipt();
        }
    }
}
