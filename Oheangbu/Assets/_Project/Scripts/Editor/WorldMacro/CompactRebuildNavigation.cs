using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static string NavSlice()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
   var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
   if(!receipt.scene.StartsWith(Folder+"/slice-",StringComparison.Ordinal))throw new Exception("Candidate scene required");
   var original=SceneManager.GetActiveScene();bool same=original.path==receipt.scene;
   var scene=same?original:EditorSceneManager.OpenScene(receipt.scene,OpenSceneMode.Additive);
   var originalRoots=same?Array.Empty<GameObject>():original.GetRootGameObjects().Where(g=>g.activeSelf).ToArray();
   try
   {
    foreach(var g in originalRoots)g.SetActive(false);Physics.SyncTransforms();
    // Exclude the live player/vehicle rigs from static baking and the separate walking probe.
    var moving=scene.GetRootGameObjects().Where(g=>g.activeSelf&&(g.name=="Macro_CombatPlayerRig"||g.name=="WorldMacro_Playtest"||g.GetComponentInChildren<WorldMacroCombatWalker>(true)!=null))
     .Concat(scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Oheangbu.App.Prologue.PrologueEncounter>(true)).Where(a=>a.gameObject.activeSelf).Select(a=>a.gameObject))
     .Concat(scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Where(s=>s.DemoEscortCompanion!=null&&s.DemoEscortCompanion.gameObject.activeSelf).Select(s=>s.DemoEscortCompanion.gameObject)).Distinct().ToArray();
    foreach(var g in moving)g.SetActive(false);Physics.SyncTransforms();
    var manifest=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single();
    var old=scene.GetRootGameObjects().SingleOrDefault(g=>g.name=="Rebuild_SliceNavigation");if(old!=null)Object.DestroyImmediate(old);
    var root=new GameObject("Rebuild_SliceNavigation");SceneManager.MoveGameObjectToScene(root,scene);
    var surface=root.AddComponent<NavMeshSurface>();surface.collectObjects=CollectObjects.Volume;surface.useGeometry=NavMeshCollectGeometry.PhysicsColliders;
    var extent=new Bounds(manifest.Content.StartFeet,Vector3.zero);extent.Encapsulate(manifest.Content.InnCheckpointFeet);
    foreach(var p in manifest.Content.Points)extent.Encapsulate(p.Position);foreach(var p in manifest.Content.MainPath)extent.Encapsulate(p);foreach(var e in manifest.Content.Encounters)extent.Encapsulate(e.Feet);
    extent.Expand(new Vector3(320,600,320));surface.center=extent.center;surface.size=extent.size;surface.overrideVoxelSize=true;surface.voxelSize=.3f;surface.overrideTileSize=true;surface.tileSize=256;
    surface.BuildNavMesh();if(surface.navMeshData==null)throw new Exception("NavMesh build returned no data");
    string path=Path.GetDirectoryName(receipt.scene).Replace('\\','/')+"/Navigation.asset";var existing=AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
    if(existing==null)AssetDatabase.CreateAsset(surface.navMeshData,path);else{var built=surface.navMeshData;surface.RemoveData();EditorUtility.CopySerialized(built,existing);surface.navMeshData=existing;surface.AddData();Object.DestroyImmediate(built);EditorUtility.SetDirty(existing);}
    Physics.SyncTransforms();var checks=new List<string>();bool all=true;
    void Check(bool value,string name){checks.Add((value?"PASS ":"FAIL ")+name);all&=value;}
    foreach(var point in manifest.Content.Points)
    {
     bool ground=Physics.RaycastAll(point.Position+Vector3.up*1.5f,Vector3.down,4,1,QueryTriggerInteraction.Ignore).Any(h=>h.collider.gameObject.scene==scene&&h.normal.y>.6f);
     Check(ground,"candidate ground under "+point.Id);
     if(!ground)checks.Add("DETAIL "+point.Id+" at "+point.Position.ToString("F2")+" hits "+string.Join("; ",Physics.RaycastAll(point.Position+Vector3.up*300,Vector3.down,600,1,QueryTriggerInteraction.Ignore).Where(h=>h.collider.gameObject.scene==scene).OrderBy(h=>h.distance).Select(h=>h.collider.name+" y="+h.point.y.ToString("F2"))));
    }
    bool a=NavMesh.SamplePosition(manifest.Content.StartFeet,out var start,3,NavMesh.AllAreas),b=NavMesh.SamplePosition(manifest.Content.InnCheckpointFeet,out var inn,3,NavMesh.AllAreas);
    Check(a,"mine start has nearby navigation");Check(b,"inn checkpoint has nearby navigation");
    var navPath=new NavMeshPath();bool connected=a&&b&&NavMesh.CalculatePath(start.position,inn.position,NavMesh.AllAreas,navPath)&&navPath.status==NavMeshPathStatus.PathComplete;
    Check(connected,"mine to inn connected navigation");
    if(connected)
    {
     int samples=0,blocked=0,unsupported=0;var blockers=new HashSet<string>();
     var approach=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshCollider>(true)).SingleOrDefault(c=>c.name=="Continuous_Approach_Soil");
     for(int i=1;i<navPath.corners.Length;i++)
     {
      var from=navPath.corners[i-1];var to=navPath.corners[i];int steps=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(from,to)));
      for(int j=0;j<=steps;j++)
      {
       var p=Vector3.Lerp(from,to,j/(float)steps);samples++;
       var ground=Physics.RaycastAll(p+Vector3.up*1.5f,Vector3.down,4,1,QueryTriggerInteraction.Ignore).Where(h=>h.collider.gameObject.scene==scene&&h.normal.y>.6f).OrderBy(h=>h.distance).ToArray();
       // The exit deck sits above the portal rock and terrain. Interpolated corner
       // heights can put the short ray below it: explicitly probe that finite deck.
       // Do not accept its underlying rock as the walking floor or the deck as a wall.
       if(approach!=null&&approach.Raycast(new Ray(p+Vector3.up*300,Vector3.down),out var deck,600)&&deck.normal.y>.6f)
           ground=Physics.RaycastAll(deck.point+Vector3.up*1.5f,Vector3.down,4,1,QueryTriggerInteraction.Ignore).Where(h=>h.collider.gameObject.scene==scene&&h.normal.y>.6f).OrderBy(h=>h.distance).ToArray();
       // Outside the gallery roof the new host bank is the actual walking terrain.
       // The old terrain still lies beneath it and must not be treated as the surface.
       // The rebuilt cave floor now continues through the mouth into this range.
       if(p.x<3423&&p.z>1854)
           ground=Physics.RaycastAll(p+Vector3.up*300,Vector3.down,600,1,QueryTriggerInteraction.Ignore).Where(h=>h.collider.gameObject.scene==scene&&(h.collider.name.StartsWith("Terrain_",StringComparison.Ordinal)||h.collider.name=="Cave_ExteriorCover237"||h.collider.name=="Natural_Cave_Floor"||h.collider==approach)&&h.normal.y>.6f).OrderBy(h=>h.distance).ToArray();
       // NavMesh corners define a straight XZ path; interpolating their Y does not
       // follow curved terrain between corners. Project exterior samples to the
       // actual terrain, then require the baked surface at that same position.
       if(ground.Length==0)ground=Physics.RaycastAll(p+Vector3.up*300,Vector3.down,600,1,QueryTriggerInteraction.Ignore).Where(h=>h.collider.gameObject.scene==scene&&h.collider.name.StartsWith("Terrain_",StringComparison.Ordinal)&&h.normal.y>.6f).OrderBy(h=>h.distance).ToArray();
       if(ground.Length==0){unsupported++;checks.Add("DETAIL no ground sample "+p.ToString("F3"));continue;}p=ground[0].point+Vector3.up*.05f;
       if(!NavMesh.SamplePosition(p,out var localNav,1,NavMesh.AllAreas)||Vector2.Distance(new Vector2(p.x,p.z),new Vector2(localNav.position.x,localNav.position.z))>.5f){unsupported++;checks.Add("DETAIL unsupported sample "+p.ToString("F3")+" ground="+ground[0].collider.name+" nav="+localNav.position.ToString("F3"));continue;}
       var overlaps=Physics.OverlapCapsule(p+Vector3.up*.31f,p+Vector3.up*1.47f,.27f,~0,QueryTriggerInteraction.Ignore).Where(c=>c.gameObject.scene==scene&&c!=ground[0].collider).ToArray();
       if(overlaps.Length>0){blocked++;foreach(var c in overlaps)blockers.Add(c.name);checks.Add("DETAIL clearance sample "+p.ToString("F3")+" ground="+ground[0].collider.name+" overlaps="+string.Join(",",overlaps.Select(c=>c.name)));}
      }
     }
     Check(unsupported==0,"sampled physical support "+samples+" positions / "+unsupported+" missing");
     Check(blocked==0,"sampled non-support standing clearance / "+blocked+" overlaps");
     if(blocked>0)checks.Add("DETAIL blocking colliders "+string.Join(", ",blockers.Take(10)));
    }
    checks.Add("DETAIL start="+start.position.ToString("F2")+" inn="+inn.position.ToString("F2")+" path="+navPath.status+" corners="+string.Join(";",navPath.corners.Select(p=>p.ToString("F2"))));
    manifest.TraversalVerified=false;checks.Add("NavMesh/ground checks only; physical walking, vehicle clearance and gameplay session not yet verified.");
    foreach(var g in moving)g.SetActive(true);
    AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);string report=string.Join("\n",checks);File.WriteAllText(Output+"/migration_navigation.txt",report);return report;
   }
   finally{if(!same)EditorSceneManager.CloseScene(scene,true);foreach(var g in originalRoots)if(g!=null)g.SetActive(true);Physics.SyncTransforms();SceneManager.SetActiveScene(original);}
  }
 }
}
