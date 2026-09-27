using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
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
  [Serializable] class WalkReceipt
  {public string status,scope,generation;public int steps;public float simulatedSeconds,distance;public Vector3 start,end,target;public Vector3[] trail;}
  static string WalkSlice(bool caveTour=false)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=Scene)throw new Exception("Compact edit required");
   var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
   if(!receipt.scene.StartsWith(Folder+"/slice-",StringComparison.Ordinal))throw new Exception("Candidate required");
   var original=SceneManager.GetActiveScene();var source=original.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single().Walker.Body;
   var scene=EditorSceneManager.OpenScene(receipt.scene,OpenSceneMode.Additive);GameObject probe=null;
   var originalRoots=original.GetRootGameObjects().Where(g=>g.activeSelf).ToArray();
   try
   {
    foreach(var g in originalRoots)g.SetActive(false);Physics.SyncTransforms();
    // Exclude the live player/vehicle rigs from static baking and the separate walking probe.
    var moving=scene.GetRootGameObjects().Where(g=>g.activeSelf&&(g.name=="Macro_CombatPlayerRig"||g.name=="WorldMacro_Playtest"||g.GetComponentInChildren<WorldMacroCombatWalker>(true)!=null)).ToArray();
    foreach(var g in moving)g.SetActive(false);Physics.SyncTransforms();
    var manifest=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single();
    if(!NavMesh.SamplePosition(manifest.Content.StartFeet,out var start,3,NavMesh.AllAreas)||!NavMesh.SamplePosition(manifest.Content.InnCheckpointFeet,out var end,3,NavMesh.AllAreas))throw new Exception("Baked endpoint missing");
    var path=new NavMeshPath();if(!NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)throw new Exception("Complete navigation required");
    var route=path.corners;
    if(caveTour){
     var data=JsonUtility.FromJson<CaveLayoutData>(File.ReadAllText(Output+"/CaveV4/geometry.json"));
     var visits=new[]{data.evidence+Vector3.left*1.6f,data.recess.Last()+Vector3.left*1.8f,data.main[5],data.satchel+Vector3.right*1.6f,data.main[6],data.main.Last(),end.position};
     var combined=new List<Vector3>{start.position};var from=start.position;
     foreach(var visit in visits){if(!NavMesh.SamplePosition(visit,out var destination,3,NavMesh.AllAreas))throw new Exception("Tour endpoint missing: "+visit);
      var leg=new NavMeshPath();if(!NavMesh.CalculatePath(from,destination.position,NavMesh.AllAreas,leg)||leg.status!=NavMeshPathStatus.PathComplete)throw new Exception("Tour leg disconnected: "+visit);
      combined.AddRange(leg.corners.Skip(1));from=destination.position;}
     route=combined.ToArray();
    }
    probe=new GameObject("Temporary_Compact_ControllerProbe");SceneManager.MoveGameObjectToScene(probe,scene);probe.transform.position=start.position+Vector3.up*.12f;
    var controller=probe.AddComponent<CharacterController>();controller.height=source.height;controller.radius=source.radius;controller.center=source.center;controller.slopeLimit=source.slopeLimit;controller.stepOffset=source.stepOffset;controller.skinWidth=source.skinWidth;controller.minMoveDistance=0;
    Physics.SyncTransforms();int corner=1,steps=0,stalled=0;float vertical=0,distance=0;var trail=new List<Vector3>{probe.transform.position};
    const float dt=.02f,speed=4.5f;
    for(;steps<20000&&corner<route.Length;steps++)
    {
     var before=probe.transform.position;var delta=route[corner]-before;delta.y=0;
     if(delta.magnitude<.3f){corner++;continue;}
     vertical=controller.isGrounded?-1:Mathf.Max(-30,vertical-9.81f*dt);
     controller.Move(Vector3.ClampMagnitude(delta,speed*dt)+Vector3.up*(vertical*dt));var after=probe.transform.position;
     float moved=Vector3.Distance(before,after);distance+=moved;stalled=moved<.0001f?stalled+1:0;
     if(steps%50==0)trail.Add(after);if(stalled>300)break;
    }
    trail.Add(probe.transform.position);bool arrived=Vector3.Distance(probe.transform.position,end.position)<1;
    var result=new WalkReceipt{status=arrived?"PASS_CONTROLLER_TRAVERSAL":"FAIL_CONTROLLER_TRAVERSAL",scope="Automated CharacterController.Move in editor physics using the canonical player capsule dimensions; no player input, combat, vehicle, performance or visual approval.",generation=receipt.generation,steps=steps,simulatedSeconds=steps*dt,distance=distance,start=start.position,end=probe.transform.position,target=end.position,trail=trail.ToArray()};
    File.WriteAllText(Output+(caveTour?"/CaveV4/tour_walk.json":"/migration_walk.json"),JsonUtility.ToJson(result,true));return result.status+" steps="+steps+" simulatedSeconds="+result.simulatedSeconds+" metres="+distance+" end="+result.end+" target="+result.target;
   }
   finally{if(probe!=null)Object.DestroyImmediate(probe);EditorSceneManager.CloseScene(scene,true);foreach(var g in originalRoots)if(g!=null)g.SetActive(true);Physics.SyncTransforms();SceneManager.SetActiveScene(original);}
  }
 }
}
