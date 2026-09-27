using System;
using System.Linq;
using Oheangbu.App.Prologue;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  static string RelayPaths(){
   var session=Object.FindFirstObjectByType<PrologueSession>();var ids=new[]{"InnRest","jeongdam_j1","LoggingEvidence"};var report="";
   for(int i=0;i<ids.Length-1;i++){
    var a=session.Content.Points.Single(p=>p.Id==ids[i]).Position;var b=session.Content.Points.Single(p=>p.Id==ids[i+1]).Position;var path=new UnityEngine.AI.NavMeshPath();
    bool ok=UnityEngine.AI.NavMesh.SamplePosition(a,out var from,3,UnityEngine.AI.NavMesh.AllAreas)&&UnityEngine.AI.NavMesh.SamplePosition(b,out var to,3,UnityEngine.AI.NavMesh.AllAreas)&&UnityEngine.AI.NavMesh.CalculatePath(from.position,to.position,UnityEngine.AI.NavMesh.AllAreas,path)&&path.status==UnityEngine.AI.NavMeshPathStatus.PathComplete;
    report+=(ok?"PASS ":"FAIL ")+ids[i]+" -> "+ids[i+1]+"\n";
   }
   System.IO.File.WriteAllText("../Art/World/PineRest/relay_paths.txt",report);return report+"Navigation query only, not controller walking.";
  }
  static string RelayShelter(){
   var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty)throw new Exception("Saved Journey edit required");
   if(GameObject.Find("Journey relay shelter")!=null)return "Existing shelter retained";
   var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HwaseongHaenggung/Prefabs/SM_Naeposa.prefab");if(source==null)throw new Exception("Shelter source missing");
   var ground=Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Single(c=>c.name=="Valley");
   if(!ground.Raycast(new Ray(new Vector3(9,200,62),Vector3.down),out var hit,400))throw new Exception("Shelter ground missing");
   var shelter=(GameObject)PrefabUtility.InstantiatePrefab(source,scene);shelter.name="Journey relay shelter";shelter.transform.SetParent(GameObject.Find("Journey Relay and Logging").transform);shelter.transform.rotation=Quaternion.Euler(0,180,0);
   var rs=shelter.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);shelter.transform.localScale*=4.5f/b.size.y;b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);shelter.transform.position+=hit.point-new Vector3(b.center.x,b.min.y,b.center.z);
   foreach(var c in shelter.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
   foreach(var filter in shelter.GetComponentsInChildren<MeshFilter>()){if(filter.sharedMesh==null)continue;filter.gameObject.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;}
   var surface=Object.FindFirstObjectByType<Unity.AI.Navigation.NavMeshSurface>();var previous=surface.navMeshData;surface.BuildNavMesh();
   var next=surface.navMeshData;if(next==null)throw new Exception("Navigation rebake failed");
   // Persist the newly baked data in the existing scene-owned asset so its GUID remains stable.
   surface.RemoveData();EditorUtility.CopySerialized(next,previous);surface.navMeshData=previous;surface.AddData();EditorUtility.SetDirty(previous);Object.DestroyImmediate(next);
   AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);return "Relay shelter grounded with mesh collision and updated owned navigation";
  }
  static string RelayActor(){
   var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty)throw new Exception("Saved Journey edit required");
   if(GameObject.Find("Jeongdam appearance")!=null)return "Existing Jeongdam retained";
   var old=GameObject.Find("Jeongdam temporary actor");if(old==null)throw new Exception("Actor anchor missing");
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Characters/PlaytestReRig/PF_Player_C02_ReRig.prefab");
   var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Art/Characters/PlaytestNaturalLocomotion/Animations/IdleStill.anim");
   if(prefab==null||idle==null)throw new Exception("Existing actor sources missing");
   var point=Object.FindFirstObjectByType<PrologueSession>().Content.Points.Single(p=>p.Id=="jeongdam_j1").Position;
   var holder=new GameObject("Jeongdam appearance").transform;holder.SetParent(old.transform.parent);holder.position=point;holder.rotation=Quaternion.Euler(0,180,0);
   var model=Object.Instantiate(prefab,holder,false);model.name="Relay officer visual";
   foreach(var script in model.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(script);
   foreach(var camera in model.GetComponentsInChildren<Camera>(true))Object.DestroyImmediate(camera.gameObject);
   foreach(var c in model.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
   foreach(var b in model.GetComponentsInChildren<Rigidbody>(true))Object.DestroyImmediate(b);
   foreach(var t in model.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="C02_NearArm"||t.name=="C02_NearBrush"||t.name=="C02_WorldBrush").ToArray())if(t!=null)Object.DestroyImmediate(t.gameObject);
   string path=Folder+"/JeongdamIdle.controller";var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
   if(controller==null){controller=AnimatorController.CreateAnimatorControllerAtPath(path);var state=controller.layers[0].stateMachine.AddState("Idle");state.motion=idle;controller.layers[0].stateMachine.defaultState=state;}
   var animator=model.GetComponent<Animator>()??model.AddComponent<Animator>();animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;
   idle.SampleAnimation(model,0);
   var bounds=NpcBounds(model);model.transform.localScale*=1.75f/Mathf.Max(.01f,bounds.size.y);bounds=NpcBounds(model);
   model.transform.position+=point-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
   // Keep the original interaction volume and navigation obstacle, retire only its primitive surface.
   Object.DestroyImmediate(old.GetComponent<MeshRenderer>());Object.DestroyImmediate(old.GetComponent<MeshFilter>());old.name="Jeongdam interaction body";
   Place("Relay supplies","Assets/HwaseongHaenggung/Prefabs/SM_M_WoodenBox.prefab",holder.parent,point+new Vector3(1.4f,0,1.1f),.65f);
   AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);return "Jeongdam rig with owned idle controller connected; interaction and quest data preserved";
  }
  static Bounds NpcBounds(GameObject model){
   var bounds=new Bounds();bool first=true;
   foreach(var renderer in model.GetComponentsInChildren<Renderer>()){
    Mesh mesh=null;bool temporary=false;if(renderer is SkinnedMeshRenderer skinned){mesh=new Mesh();skinned.BakeMesh(mesh);temporary=true;}else mesh=renderer.GetComponent<MeshFilter>()?.sharedMesh;
    if(mesh==null)continue;var b=mesh.bounds;
    for(int i=0;i<8;i++){var p=renderer.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);}
    if(temporary)Object.DestroyImmediate(mesh);
   }
   if(first)throw new Exception("NPC has no visible mesh");return bounds;
  }
  static string RelayView(){
   var session=Object.FindFirstObjectByType<PrologueSession>();if(!EditorApplication.isPlaying||!session.TestSaveSuffix.StartsWith("-audit-"))throw new Exception("Isolated Play required");
   var point=session.Content.Points.Single(p=>p.Id=="jeongdam_j1").Position;session.Teleport(point+new Vector3(0,1,-2),0);return "At relay, diagnostic camera position";
  }
 }
}
