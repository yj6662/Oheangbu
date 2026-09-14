using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static class PlaytestGapSnapshot
    {
        [Serializable] class Snapshot
        {
            public string utc,scene,scope="Read-only saved scene reference audit. Does not execute combat, navigation, saving or UI.";
            public int missingScripts,actors,previewPoints,vehicles,gestureRigs;
            public bool contentConnected,walkerConnected,vehicleValid,gestureProfileConnected;
            public string vehicleIssue,openingObjective,profile;
            public string[] missingReferences,emptyMeshFilters,duplicatePointIds,characterControllers,animationClips;
        }
        public static string Read()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Edit mode required");
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            var transforms=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
            var refs=new List<string>();
            foreach(var t in transforms)foreach(var component in t.GetComponents<MonoBehaviour>())
            {
                if(component==null)continue;
                using(var so=new SerializedObject(component))
                {
                    var p=so.GetIterator();
                    while(p.NextVisible(true))if(p.propertyType==SerializedPropertyType.ObjectReference&&p.objectReferenceValue==null&&p.objectReferenceInstanceIDValue!=0)
                        refs.Add(t.name+" / "+component.GetType().Name+" / "+p.propertyPath);
                }
            }
            var car=Object.FindFirstObjectByType<WorldMacroPalanquinController>(FindObjectsInactive.Include);
            string issue="missing vehicle";bool valid=car!=null&&car.ValidateConfiguration(out issue);
            var rigs=Object.FindObjectsByType<WorldMacroPlayerGestureRig>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            var animators=transforms.SelectMany(t=>t.GetComponents<Animator>()).Where(a=>a.runtimeAnimatorController!=null).ToArray();
            var points=session?.PreviewPoints??Array.Empty<WorldMacroContentPoint>();
            var s=new Snapshot{utc=DateTime.UtcNow.ToString("o"),scene=scene.path,
                missingScripts=transforms.Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)),
                missingReferences=refs.ToArray(),emptyMeshFilters=transforms.SelectMany(t=>t.GetComponents<MeshFilter>()).Where(f=>f.sharedMesh==null).Select(f=>f.name).ToArray(),
                actors=session?.Actors?.Length??0,previewPoints=points.Length,
                duplicatePointIds=points.Where(p=>p!=null).GroupBy(p=>p.Id).Where(g=>g.Count()>1).Select(g=>g.Key).ToArray(),
                contentConnected=session!=null&&session.Content!=null,walkerConnected=session!=null&&session.Walker!=null&&session.Walker.Motor!=null&&session.Walker.ViewCamera!=null,
                vehicles=Object.FindObjectsByType<WorldMacroPalanquinController>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length,vehicleValid=valid,vehicleIssue=issue,
                gestureRigs=rigs.Length,gestureProfileConnected=rigs.Length==1&&rigs[0].Profile!=null,
                profile=rigs.Length>0?AssetDatabase.GetAssetPath(rigs[0].Profile):"",
                characterControllers=animators.Select(a=>a.name+": "+AssetDatabase.GetAssetPath(a.runtimeAnimatorController)).ToArray(),
                animationClips=animators.SelectMany(a=>a.runtimeAnimatorController.animationClips).Distinct().Select(AssetDatabase.GetAssetPath).ToArray()};
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestRecovery/DrawingElbow"));Directory.CreateDirectory(root);
            string json=JsonUtility.ToJson(s,true);File.WriteAllText(root+"/current_scene_audit.json",json);return json;
        }
    }
}
