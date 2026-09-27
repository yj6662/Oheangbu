using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Globalization;
using Oheangbu.App.World.Vehicle;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static class CompactVehicleRestoration
    {
        [Serializable] sealed class Difference{public string path;public Vector3 before,expected;public float metres;}
        [Serializable] sealed class Receipt
        {
            public string status,scene,prefab,scope="Prefab child positions and original scene cargo/passenger socket positions; root world pose, physics profile and ownership retained.";
            public int matchedTransforms,meshChecks,materialChecks;public Vector3 rootBefore,rootAfter;public string profile;
            public List<Difference> differences=new List<Difference>();public List<string> failures=new List<string>();public List<string> sceneOnly=new List<string>();
        }
        public static string Run(bool repair)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=WorldMacroCompactAuthoring.TargetScene)throw new InvalidOperationException("Compact Edit scene required");
            var car=Object.FindObjectsByType<WorldMacroPalanquinController>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(c=>c.gameObject.scene==SceneManager.GetActiveScene());
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(MagicStoneCarAuthoring.PrefabPath);
            string folder=Path.Combine(WorldMacroCompactAuthoring.Output,"VehicleRestoration");Directory.CreateDirectory(folder);
            var report=new Receipt{scene=SceneManager.GetActiveScene().path,prefab=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(car.gameObject),rootBefore=car.transform.position,profile=AssetDatabase.GetAssetPath(car.Profile)};
            if(report.prefab!=MagicStoneCarAuthoring.PrefabPath)throw new InvalidOperationException("Unexpected vehicle model, refusing to replace blindly");
            var targets=new Dictionary<Transform,Vector3>();
            foreach(var t in car.GetComponentsInChildren<Transform>(true))
            {
                if(t==car.transform)continue;string path=AnimationUtility.CalculateTransformPath(t,car.transform);var original=source.transform.Find(path);
                if(original==null)
                {
                    report.sceneOnly.Add(path);
                    if(t.name=="DemoEscortCargoSocket"||t.name=="DemoEscortPassengerSocket")
                    {
                        var expected=OriginalSocket(t.name);targets.Add(t,expected);
                        if(Vector3.Distance(t.localPosition,expected)>.0001f)report.differences.Add(new Difference{path=path,before=t.localPosition,expected=expected,metres=Vector3.Distance(t.localPosition,expected)});
                    }
                    continue;
                }
                targets.Add(t,original.localPosition);report.matchedTransforms++;
                float delta=Vector3.Distance(t.localPosition,original.localPosition);
                if(delta>.0001f)report.differences.Add(new Difference{path=path,before=t.localPosition,expected=original.localPosition,metres=delta});
                var mesh=t.GetComponent<MeshFilter>();var sourceMesh=original.GetComponent<MeshFilter>();
                if(mesh!=null&&sourceMesh!=null){report.meshChecks++;if(mesh.sharedMesh!=sourceMesh.sharedMesh)report.failures.Add("Mesh differs: "+path);}
                var renderer=t.GetComponent<Renderer>();var sourceRenderer=original.GetComponent<Renderer>();
                if(renderer!=null&&sourceRenderer!=null){report.materialChecks++;if(!renderer.sharedMaterials.SequenceEqual(sourceRenderer.sharedMaterials))report.failures.Add("Materials differ: "+path);}
            }
            if(repair)
            {
                if(report.failures.Count>0)throw new InvalidOperationException("Mesh/material mismatches require inspection: "+string.Join(";",report.failures));
                string backup=Path.Combine(folder,"W_Demo_Compact_before_vehicle.unity");if(!File.Exists(backup))File.Copy(report.scene,backup);
                if(!File.Exists(Path.Combine(folder,"before.json")))File.WriteAllText(Path.Combine(folder,"before.json"),JsonUtility.ToJson(report,true));
                foreach(var pair in targets)
                {
                    if(Vector3.Distance(pair.Key.localPosition,pair.Value)<=.0001f)continue;
                    Undo.RecordObject(pair.Key,"Restore rigid vehicle local placement");pair.Key.localPosition=pair.Value;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(pair.Key);EditorUtility.SetDirty(pair.Key);
                }
                Physics.SyncTransforms();
                if(!car.ValidateConfiguration(out var issue))throw new InvalidOperationException(issue);
                if(targets.Any(p=>Vector3.Distance(p.Key.localPosition,p.Value)>.0001f))throw new InvalidOperationException("Restoration mismatch");
                EditorSceneManager.MarkSceneDirty(car.gameObject.scene);EditorSceneManager.SaveScene(car.gameObject.scene);
            }
            report.rootAfter=car.transform.position;
            if(report.rootBefore!=report.rootAfter)report.failures.Add("Root moved");
            report.status=report.failures.Count>0?"FAIL":repair?"RESTORED":report.differences.Count>0?"LOCAL_PLACEMENT_DEFORMED":"MATCHES_SOURCE_PREFAB";
            string json=JsonUtility.ToJson(report,true);File.WriteAllText(Path.Combine(folder,repair?"repair.json":"audit.json"),json);return json;
        }
        static Vector3 OriginalSocket(string name)
        {
            var docs=File.ReadAllText(WorldMacroCompactAuthoring.SourceScene).Split(new[]{"--- !u!"},StringSplitOptions.None);
            var go=docs.Single(d=>d.StartsWith("1 &")&&Regex.IsMatch(d,@"(?m)^  m_Name: "+Regex.Escape(name)+@"\r?$"));
            string id=Regex.Match(go,@"^1 &(\d+)").Groups[1].Value;
            var transform=docs.Single(d=>d.StartsWith("4 &")&&d.Contains("m_GameObject: {fileID: "+id+"}"));
            var p=Regex.Match(transform,@"m_LocalPosition: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}");
            if(!p.Success)throw new InvalidOperationException("Original socket position unavailable: "+name);
            return new Vector3(float.Parse(p.Groups[1].Value,CultureInfo.InvariantCulture),float.Parse(p.Groups[2].Value,CultureInfo.InvariantCulture),float.Parse(p.Groups[3].Value,CultureInfo.InvariantCulture));
        }
    }
}
