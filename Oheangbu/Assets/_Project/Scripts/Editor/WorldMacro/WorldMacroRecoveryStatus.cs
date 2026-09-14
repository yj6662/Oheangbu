using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.World;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Read-only restart audit. Never reloads, rebuilds or saves the user's open scene.
    public static class WorldMacroRecoveryStatus
    {
        [Serializable] sealed class Status
        {
            public string utc,scene;
            public bool playing,dirty,correctScene,positionsMatch,idsMatch,walkerConnected;
            public int contentEntries,contentObjects,missingScripts,environmentCells,prototypes,fixedProps,contentExclusions;
            public float commitRatio;
        }
        public static string Read()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var root=GameObject.Find(WorldMacroContentAuthoring.RootName);
            var preview=root==null?null:root.GetComponent<WorldMacroContentPreview>();
            var entries=preview?.Sheet?.Entries??Array.Empty<WorldMacroContentSheetSO.Entry>();
            var points=root==null?Array.Empty<WorldMacroContentPoint>():root.GetComponentsInChildren<WorldMacroContentPoint>(true);
            var dressing=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(WorldMacroDressingAuthoring.SheetPath);
            var s=new Status{utc=DateTime.UtcNow.ToString("o"),scene=scene.path,playing=EditorApplication.isPlaying,dirty=scene.isDirty,
                correctScene=scene.path==WorldMacroBuilder.ScenePath,contentEntries=entries.Length,contentObjects=points.Length,
                missingScripts=scene.GetRootGameObjects().Sum(g=>g.GetComponentsInChildren<Transform>(true).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject))),
                idsMatch=entries.Length>0&&entries.Select(e=>e.Id).OrderBy(x=>x).SequenceEqual(points.Select(p=>p.Id).OrderBy(x=>x)),
                positionsMatch=entries.Length>0&&entries.All(e=>points.Any(p=>p.Id==e.Id&&Vector3.Distance(p.transform.position,e.Position)<.001f)),
                walkerConnected=preview!=null&&preview.Walker!=null&&preview.Walker.WalkBody!=null,
                environmentCells=dressing?.Cells?.Length??0,prototypes=dressing?.Prototypes?.Length??0,fixedProps=dressing?.FixedPlacements?.Length??0,
                contentExclusions=dressing?.PreservedAreas?.Count(a=>a.Id.StartsWith("content:"))??0,
                commitRatio=Prologue.PrologueAudit.CommitRatio()};
            var path=WorldMacroBuilder.Output+"/Recovery";Directory.CreateDirectory(path);
            string json=JsonUtility.ToJson(s,true);File.WriteAllText(path+"/scene_status.json",json);return json;
        }
    }
}
