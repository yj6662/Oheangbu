using System;
using System.IO;
using System.Linq;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroCompactAuthoring
    {
        static string PrepareTitle()
        {
            RequireCompact();
            const string source="Assets/_Project/Scenes/World/W_Demo_Title.unity";
            const string target="Assets/_Project/Scenes/World/W_Demo_Compact_Title.unity";
            string originalHash=Hash(source);var active=SceneManager.GetActiveScene();
            if(!File.Exists(target)&&!AssetDatabase.CopyAsset(source,target))throw new InvalidOperationException("Cannot preserve-copy title scene.");
            var title=EditorSceneManager.OpenScene(target,OpenSceneMode.Additive);
            try
            {
                var ui=title.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();
                ui.TitleSceneName="W_Demo_Compact_Title";ui.PlaySceneName="W_Demo_Compact";
                ui.WorldSheet=AssetDatabase.LoadAssetAtPath<WorldMacroSheetSO>(Folder+"/Data/01_WorldMacroSheet.asset");
                ui.MapData=AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>(Folder+"/Data/04_Map.asset");
                ui.Content=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(Folder+"/Data/03_Content.asset");
                if(!ui.WorldSheet||!ui.MapData||!ui.Content)throw new InvalidOperationException("Missing compact title binding.");
                EditorUtility.SetDirty(ui);EditorSceneManager.MarkSceneDirty(title);EditorSceneManager.SaveScene(title);
            }
            finally{EditorSceneManager.CloseScene(title,true);SceneManager.SetActiveScene(active);}
            if(Hash(source)!=originalHash||Hash(SourceScene)!=ReadProgress().sourceHash)throw new InvalidOperationException("Source scene preservation check failed.");
            // Append only. The existing startup scene and all original indices remain intact.
            var scenes=EditorBuildSettings.scenes.ToList();
            foreach(string path in new[]{target,TargetScene})if(!scenes.Any(s=>s.path==path))scenes.Add(new EditorBuildSettingsScene(path,true));
            EditorBuildSettings.scenes=scenes.ToArray();
            string report="Compact title saved with isolated content/save/map references. Both compact scenes appended for SceneManager loading; original startup and scene order preserved. No build produced. Lobby round-trip remains unverified.";
            File.WriteAllText(Path.Combine(Output,"title_report.txt"),report);return report;
        }
    }
}
