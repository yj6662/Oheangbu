using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Experimental.AI;
using Unity.Collections;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.Prologue;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static string Navigation290(bool bake)
  {
   var scene=SceneManager.GetActiveScene();if(scene.path!=Scene290)throw new Exception("Candidate only");
   var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<NavMeshSurface>(true)).ToArray();
   var rows=all.Select(s=>$"{s.name}: active={s.isActiveAndEnabled}; collect={s.collectObjects}; centre={s.center}; size={s.size}; agent={s.agentTypeID}; {AssetDatabase.GetAssetPath(s.navMeshData)}").ToList();
   File.WriteAllText(O290+"/navigation-inventory.txt",string.Join("\n",rows));if(!bake)return string.Join("\n",rows);
   var basis=all.Single(s=>s.name=="Rebuild_SliceNavigation");var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
   var active=all.Where(s=>s.isActiveAndEnabled).ToArray();
   var moving=scene.GetRootGameObjects().Where(g=>g.activeSelf&&g.GetComponentInChildren<WorldMacroCombatWalker>(true)!=null)
    .Concat(session.Actors.Where(a=>a!=null&&a.gameObject.activeSelf).Select(a=>a.gameObject))
    .Concat(session.DemoEscortCompanion!=null&&session.DemoEscortCompanion.gameObject.activeSelf?new[]{session.DemoEscortCompanion.gameObject}:Array.Empty<GameObject>()).Distinct().ToArray();
   GameObject node=null;bool installed=false;
   try
   {
    foreach(var g in moving)g.SetActive(false);foreach(var old in active)old.RemoveData();Physics.SyncTransforms();
    node=new GameObject("Mountain290_Navigation_Pending");var surface=node.AddComponent<NavMeshSurface>();
    surface.agentTypeID=basis.agentTypeID;surface.collectObjects=CollectObjects.Volume;surface.useGeometry=NavMeshCollectGeometry.PhysicsColliders;
    var bounds=new Bounds(basis.transform.TransformPoint(basis.center),Vector3.Scale(basis.size,basis.transform.lossyScale));
    foreach(var m in session.MountainLayout.Mountains.Where(v=>v.Actions.Length>0))foreach(var p in m.MainPath.Concat(m.TemplePath).Concat(m.ReturnPath))bounds.Encapsulate(p);
    bounds.Expand(new Vector3(60,120,60));surface.center=bounds.center;surface.size=bounds.size;
    surface.overrideVoxelSize=true;surface.voxelSize=.2f;surface.overrideTileSize=true;surface.tileSize=256;surface.buildHeightMesh=true;surface.BuildNavMesh();
    if(surface.navMeshData==null)throw new Exception("No navigation data produced");
    surface.RemoveData();surface.AddData();bool passed=true; rows.Add("NAV diagnostic revision 5, default and expanded query reported separately; voxel=.2; triangles="+(NavMesh.CalculateTriangulation().indices.Length/3));
    foreach(var m in session.MountainLayout.Mountains.Where(v=>v.Actions.Length>0))
    {
     var path=new NavMeshPath();var filter=new NavMeshQueryFilter{agentTypeID=surface.agentTypeID,areaMask=NavMesh.AllAreas};
     bool foot=NavMesh.SamplePosition(m.Foot,out var start,1,filter),summit=NavMesh.SamplePosition(m.Summit,out var end,1,filter);
     bool good=foot&&summit&&NavMesh.CalculatePath(start.position,end.position,filter,path)&&path.status==NavMeshPathStatus.PathComplete;
     rows.Add($"DETAIL {m.Id} foot={foot} summit={summit} path={path.status}; last={(path.corners.Length>0?path.corners.Last():Vector3.zero)}");
     var missing=new List<string>();for(int i=0;i<m.MainPath.Length;i+=12)if(!NavMesh.SamplePosition(m.MainPath[i],out var local,.65f,filter)||Mathf.Abs(local.position.y-m.MainPath[i].y)>.55f)missing.Add(i+":"+m.MainPath[i].ToString("F1"));
     rows.Add("DETAIL unsupported navigation samples="+missing.Count+" "+string.Join(";",missing.Take(12)));
     if(foot&&summit)using(var query=new NavMeshQuery(NavMeshWorld.GetDefaultWorld(),Allocator.TempJob,65535)){
      var aa=query.MapLocation(start.position,Vector3.one,0);var bb=query.MapLocation(end.position,Vector3.one,0);
      var state=query.BeginFindPath(aa,bb);int steps=0;while((state&PathQueryStatus.InProgress)!=0&&steps++<200)state=query.UpdateFindPath(2048,out _);
      var finish=query.EndFindPath(out int polygons);good|=state==PathQueryStatus.Success&&finish==PathQueryStatus.Success&&polygons>1;rows.Add("EXPANDED query="+state+" / "+finish+" polygons="+polygons);
     }

     for(int i=12;i<m.MainPath.Length+12;i+=12){int endIndex=Mathf.Min(i,m.MainPath.Length-1);
      if(!NavMesh.SamplePosition(m.MainPath[i-12],out var a,1,filter)||!NavMesh.SamplePosition(m.MainPath[endIndex],out var b,1,filter)){good=false;rows.Add("MISSING pair endpoints "+(i-12)+"->"+endIndex);continue;}
      var localPath=new NavMeshPath();if(!NavMesh.CalculatePath(a.position,b.position,filter,localPath)||localPath.status!=NavMeshPathStatus.PathComplete)
       {good=false;rows.Add("DISCONNECTED "+(i-12)+"->"+i+" "+a.position.ToString("F2")+" -> "+b.position.ToString("F2")+" last="+(localPath.corners.Length>0?localPath.corners.Last().ToString("F2"):"none"));}
     }

     rows.Add((good?"PASS ":"FAIL ")+m.Id+" fresh navigation foot to summit");passed&=good;
    }
    if(!passed)throw new Exception("Candidate navigation incomplete; previous navigation registration restored");
    string asset=A290+"/MountainNavigation.asset";var existing=AssetDatabase.LoadAssetAtPath<NavMeshData>(asset);var built=surface.navMeshData;
    if(existing==null)AssetDatabase.CreateAsset(built,asset);else{surface.RemoveData();EditorUtility.CopySerialized(built,existing);surface.navMeshData=existing;surface.AddData();Object.DestroyImmediate(built);EditorUtility.SetDirty(existing);}
    foreach(var old in active){old.RemoveData();old.enabled=false;EditorUtility.SetDirty(old);}
    node.name="Mountain290_Navigation";installed=true;AssetDatabase.SaveAssets();
   }
   catch(Exception e){rows.Add("FAIL "+e.Message);}
   finally
   {
    foreach(var g in moving)if(g!=null)g.SetActive(true);
    if(!installed){if(node!=null){var data=node.GetComponent<NavMeshSurface>().navMeshData;Object.DestroyImmediate(node);if(data!=null&&!AssetDatabase.Contains(data))Object.DestroyImmediate(data);}foreach(var old in active)if(old!=null&&old.isActiveAndEnabled)old.AddData();}
   }
   if(installed){EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);}
   rows.Add(installed?"Installed candidate-only navigation for authored Cheongrim slice; other mountains and manual agent gameplay remain unverified":"NOT installed");
   string result=string.Join("\n",rows);File.WriteAllText(O290+"/navigation.txt",result);return result;
  }
 }
}
