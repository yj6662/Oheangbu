using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.Dressing;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroVisualCorridorAuthoring
    {
        // This is a scene-owned copy. The macro-wide source and hand placed instances stay untouched.
        static string InstallClearance()
        {
            var renderer=Object.FindFirstObjectByType<WorldMacroDressingRenderer>();
            var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if(renderer==null||session==null)throw new Exception("Scene dressing/session required");
            string path=Folder+"/Dressing_VisualCorridor.asset";
            var local=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(path);
            if(local==null){local=Object.Instantiate(renderer.Sheet);local.name="Dressing_VisualCorridor";AssetDatabase.CreateAsset(local,path);}
            var areas=local.PreservedAreas.Where(a=>!a.Id.StartsWith("content:visual:",StringComparison.Ordinal)).ToList();
            var main=session.Content.MainPath;
            float total=0;for(int i=1;i<main.Length;i++)total+=Vector3.Distance(main[i-1],main[i]);
            for(float s=0;s<total;s+=12){var a=Along(main,s,out _);var b=Along(main,Mathf.Min(total,s+12),out _);var v=b-a;v.y=0;
                if(v.magnitude<.1f)continue;
                areas.Add(new WorldMacroDressingSheetSO.PreserveArea{Id="content:visual:main:"+(int)s,Centre=(a+b)*.5f,HalfSize=new Vector2(2.5f,v.magnitude*.5f+.6f),Yaw=Quaternion.LookRotation(v).eulerAngles.y});
            }
            // Buildings reserve their actual footprint and doorway apron, not a broad empty city rectangle.
            var root=GameObject.Find(RootName).transform;
            foreach(var p in Sheet.Placements.Where(p=>p.Id.StartsWith("CAP_",StringComparison.Ordinal))){
                var t=root.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name==p.Id);if(t==null)continue;
                var rr=t.GetComponentsInChildren<Renderer>();if(rr.Length==0)continue;var bounds=rr[0].bounds;foreach(var r in rr.Skip(1))bounds.Encapsulate(r.bounds);
                areas.Add(new WorldMacroDressingSheetSO.PreserveArea{Id="content:visual:"+p.Id,Centre=bounds.center,HalfSize=new Vector2(bounds.extents.x+2,bounds.extents.z+2),Yaw=0});
            }
            local.PreservedAreas=areas.ToArray();renderer.Sheet=local;renderer.ResetCache();EditorUtility.SetDirty(renderer);EditorUtility.SetDirty(local);
            AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            string result="Scene-only dressing exclusion copy installed: "+areas.Count+" support/doorway/route areas; fixed placements and shared source unchanged.";
            File.WriteAllText(Output+"/clearance_install.txt",result);return result;
        }

        [Serializable] sealed class GeometryReport
        {
            public string scope="Editor static geometry and capsule stations; not a user walking completion or visual art approval.";
            public int placements,renderers,lodGroups,allLodTriangles,missingMeshes,missingMaterials,missingPlacements;
            public int capsuleStations,capsuleBlockers,routeSupportFailures;public float maximumTerrainSlope;
            public string[] failures,stations;public bool passed;
        }
        static string ValidateGeometry()
        {
            Physics.SyncTransforms();EnsureFolders();var root=GameObject.Find(RootName);if(root==null)throw new Exception("Visual root missing");
            var report=new GeometryReport();var failures=new List<string>();var stations=new List<string>();var transforms=root.GetComponentsInChildren<Transform>();
            report.placements=Sheet.Placements.Length;
            foreach(var p in Sheet.Placements){var t=transforms.FirstOrDefault(t=>t.name==p.Id);if(t==null){report.missingPlacements++;failures.Add("Missing "+p.Id);}else if(Vector3.Distance(t.position,p.Position)>.02f)failures.Add("Unrecorded placement change "+p.Id);}
            foreach(var f in root.GetComponentsInChildren<MeshFilter>(true))if(f.sharedMesh==null)report.missingMeshes++;else report.allLodTriangles+=f.sharedMesh.triangles.Length/3;
            foreach(var r in root.GetComponentsInChildren<Renderer>(true)){report.renderers++;if(r.sharedMaterials.Length==0||r.sharedMaterials.Any(m=>m==null||m.shader==null||!m.shader.isSupported))report.missingMaterials++;}
            foreach(var group in root.GetComponentsInChildren<LODGroup>(true)){report.lodGroups++;var lods=group.GetLODs();if(lods.Length==1&&lods[0].screenRelativeTransitionHeight>.03f)failures.Add("Single LOD culls early "+group.name);}
            var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            foreach(var route in CapitalRouteIds.Concat(new[]{"Trail_Inn_Logging","Trail_Logging_Deep"}).Select(FindRoute))
            for(int i=1;i<route.Points.Length;i++){
                int steps=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(route.Points[i-1],route.Points[i])/3));
                for(int n=0;n<steps;n++){
                    var p=Vector3.Lerp(route.Points[i-1],route.Points[i],n/(float)steps);report.capsuleStations++;
                    var hits=Physics.RaycastAll(p+Vector3.up*.8f,Vector3.down,3,1,QueryTriggerInteraction.Ignore).Where(h=>!h.transform.IsChildOf(session.Walker.Body.transform)).OrderBy(h=>h.distance).ToArray();
                    if(hits.Length==0){report.routeSupportFailures++;if(stations.Count<100)stations.Add(route.Id+" unsupported "+p);continue;}
                    var feet=hits[0].point+Vector3.up*.12f;report.maximumTerrainSlope=Mathf.Max(report.maximumTerrainSlope,Vector3.Angle(hits[0].normal,Vector3.up));
                    var blockers=Physics.OverlapCapsule(feet+Vector3.up*.28f,feet+Vector3.up*1.47f,.28f,1,QueryTriggerInteraction.Ignore).Where(c=>!c.transform.IsChildOf(session.Walker.Body.transform)).ToArray();
                    if(blockers.Length>0){report.capsuleBlockers++;if(stations.Count<100)stations.Add(route.Id+" blocked "+p+" "+string.Join(",",blockers.Select(c=>c.name)));}
                }
            }
            if(report.missingMeshes+report.missingMaterials+report.missingPlacements>0)failures.Add("Missing geometry/material/placements");
            failures.AddRange(ValidateCacheDrift());
            if(report.capsuleBlockers+report.routeSupportFailures>0)failures.Add("Route stations require correction (see stations)");
            report.failures=failures.ToArray();report.stations=stations.ToArray();report.passed=failures.Count==0;
            string json=JsonUtility.ToJson(report,true);File.WriteAllText(Output+"/validation.json",json);
            File.WriteAllText(Output+"/asset_manifest.json",JsonUtility.ToJson(Sheet,true));return json;
        }
    }
}
