using System;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  static string ForestBindings(){
   if(EditorApplication.isPlaying||SceneManager.GetActiveScene().path!=Scene)throw new Exception("Journey edit required");
   var session=Object.FindFirstObjectByType<PrologueSession>();var boss=session.Encounters.Single(a=>a.Id=="cheongryong");
   var so=new SerializedObject(boss.GetComponent<EnemyController>());so.FindProperty("_player").objectReferenceValue=session.Player;so.FindProperty("_playerVitals").objectReferenceValue=session.Player.GetComponent<PlayerVitals>();so.ApplyModifiedPropertiesWithoutUndo();
   EditorSceneManager.SaveScene(SceneManager.GetActiveScene());return "Boss sight and player damage references rebound to Journey";
  }
  static string Forest(){
   var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty)throw new Exception("Saved Journey required");
   if(GameObject.Find("Journey Deep Forest")!=null)throw new Exception("Already authored; preserve manual scene edits");
   var session=Object.FindFirstObjectByType<PrologueSession>();var ground=Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Single(c=>c.name=="Valley");
   Vector3 Ground(float x,float z){if(!ground.Raycast(new Ray(new Vector3(x,300,z),Vector3.down),out var hit,600))throw new Exception("Unsupported forest point");return hit.point;}
   var root=new GameObject("Journey Deep Forest").transform;PrologueEncounter boss=null;
   var source=EditorSceneManager.OpenScene("Assets/_Project/Scenes/World/W_Demo_Compact.unity",OpenSceneMode.Additive);
   try{
    var original=source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CheongryongCombatController>(true)).Single();
    var copy=Object.Instantiate(original.gameObject);SceneManager.MoveGameObjectToScene(copy,scene);copy.transform.SetParent(root,true);boss=copy.GetComponent<PrologueEncounter>();
   }finally{EditorSceneManager.CloseScene(source,true);SceneManager.SetActiveScene(scene);}
   boss.gameObject.SetActive(false);boss.name="cheongryong";boss.Id="cheongryong";boss.Player=session.Player;boss.Session=session;
   boss.transform.position=Ground(0,188)+Vector3.up*.875f;boss.transform.rotation=Quaternion.Euler(0,180,0);boss.PatrolPoints=new[]{boss.transform.position};boss.DetectionRange=23;boss.Leash=32;boss.GetComponent<NavMeshAgent>().enabled=true;
   var actors=session.Encounters.Concat(new[]{boss}).ToArray();session.Encounters=actors;
   var wiring=new SerializedObject(session.Wiring);var enemies=wiring.FindProperty("_enemies");enemies.arraySize=actors.Length;for(int i=0;i<actors.Length;i++)enemies.GetArrayElementAtIndex(i).objectReferenceValue=actors[i].GetComponent<EnemyVitals>();wiring.ApplyModifiedPropertiesWithoutUndo();
   var rules=(session.Content.EncounterRules??Array.Empty<PrologueContentSO.EncounterRule>()).ToList();rules.Add(new PrologueContentSO.EncounterRule{Id=boss.Id,RequiredCompleted=new[]{"j1:reported"},PersistentDefeat=true});session.Content.EncounterRules=rules.ToArray();
   var rest=Ground(-5,150);session.Content.Points=session.Content.Points.Concat(new[]{new PrologueContentSO.Point{Id="ForestRest",Kind=PrologueInteractionKind.Rest,Position=rest,Radius=2.5f,Prompt="숲길에서 숨 고르기",Text=""}}).ToArray();
   var stone=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/Materials/PaleStone.mat");
   var seat=GameObject.CreatePrimitive(PrimitiveType.Cube);seat.name="Forest resting stone";seat.transform.SetParent(root);seat.transform.position=rest+Vector3.up*.22f;seat.transform.localScale=new Vector3(1.5f,.44f,.65f);seat.GetComponent<Renderer>().sharedMaterial=stone;
   var tree=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/World/PineRestTrees/PhotorealPine/Tree.prefab");
   for(int i=0;i<10;i++){
    float x=(i%2==0?-1:1)*(16+i%3*2),z=142+i/2*14;
    var pine=(GameObject)PrefabUtility.InstantiatePrefab(tree,root);pine.name="Forest edge pine "+i;var b=pine.GetComponentsInChildren<Renderer>().First().bounds;pine.transform.localScale*=9/Mathf.Max(.1f,b.size.y);b=pine.GetComponentsInChildren<Renderer>().First().bounds;pine.transform.position=Ground(x,z)-Vector3.up*b.min.y;pine.transform.Rotate(0,i*137,0);
   }
   Physics.SyncTransforms();var surface=Object.FindFirstObjectByType<NavMeshSurface>();var previous=surface.navMeshData;surface.center=new Vector3(0,25,100);surface.size=new Vector3(145,100,260);surface.BuildNavMesh();var next=surface.navMeshData;if(next==null)throw new Exception("Forest nav bake failed");surface.RemoveData();EditorUtility.CopySerialized(next,previous);surface.navMeshData=previous;surface.AddData();EditorUtility.SetDirty(previous);Object.DestroyImmediate(next);
   if(!NavMesh.SamplePosition(boss.transform.position,out _,3,NavMesh.AllAreas))throw new Exception("Boss outside navigation");
   EditorUtility.SetDirty(session.Content);EditorUtility.SetDirty(session);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);ForestBindings();return "Cheongryong clone, J1 gate, persistent defeat, approach rest and forest edge authored";
  }
  static string ForestSurvey(){
   var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty)throw new Exception("Saved Journey required");
   var log=new StringBuilder();var ground=Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Single(c=>c.name=="Valley");log.AppendLine("Ground "+ground.bounds);
   for(int z=135;z<=350;z+=25){if(ground.Raycast(new Ray(new Vector3(0,300,z),Vector3.down),out var hit,600))log.AppendLine("sample "+hit.point+" slope="+Vector3.Angle(hit.normal,Vector3.up));}
   var source=EditorSceneManager.OpenScene("Assets/_Project/Scenes/World/W_Demo_Compact.unity",OpenSceneMode.Additive);
   try{foreach(var boss in source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CheongryongCombatController>(true))){log.AppendLine("BOSS "+boss.name+" "+boss.transform.position);foreach(var component in boss.GetComponents<Component>())log.AppendLine(" component "+component.GetType().FullName);foreach(var renderer in boss.GetComponentsInChildren<Renderer>(true).Take(3))log.AppendLine(" renderer "+renderer.name+" "+renderer.bounds);}}
   finally{EditorSceneManager.CloseScene(source,true);SceneManager.SetActiveScene(scene);}
   File.WriteAllText("../Art/World/PineRest/forest_survey.txt",log.ToString());return log.ToString();
  }
 }
}
