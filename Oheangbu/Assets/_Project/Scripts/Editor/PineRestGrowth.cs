using System;
using System.Linq;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  static string GrowthView(){
   var s=Object.FindFirstObjectByType<PrologueSession>();if(!EditorApplication.isPlaying||!s.TestSaveSuffix.StartsWith("-audit-"))throw new Exception("Isolated Play required");
   var actor=s.Encounters.Single(a=>a.Id=="journey_growth_lesson");s.Teleport(actor.transform.position+Vector3.back*6,0);return "At growth lesson for visual inspection";
  }
  static string GrowthLesson(){
   var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty)throw new Exception("Saved Journey required");
   var s=Object.FindFirstObjectByType<PrologueSession>();if(s.Encounters.Any(a=>a.Id=="journey_growth_lesson"))return "Existing lesson retained";
   var ground=Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Single(c=>c.name=="Valley");if(!ground.Raycast(new Ray(new Vector3(8,200,162),Vector3.down),out var hit,400))throw new Exception("Lesson ground absent");
   PrologueEncounter actor=null;var source=EditorSceneManager.OpenScene("Assets/_Project/Scenes/World/W_Demo_Compact.unity",OpenSceneMode.Additive);
   try{var original=source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<DemoGrowthLessonLink>(true)).Single();var copy=Object.Instantiate(original.gameObject);SceneManager.MoveGameObjectToScene(copy,scene);Object.DestroyImmediate(copy.GetComponent<DemoGrowthLessonLink>());actor=copy.GetComponent<PrologueEncounter>();}
   finally{EditorSceneManager.CloseScene(source,true);SceneManager.SetActiveScene(scene);}
   actor.gameObject.SetActive(false);actor.transform.SetParent(GameObject.Find("Journey Deep Forest").transform);actor.transform.position=hit.point+Vector3.up*.875f;actor.name=actor.Id="journey_growth_lesson";actor.Session=s;actor.Player=s.Player;actor.PatrolPoints=new[]{actor.transform.position};actor.DetectionRange=12;actor.Leash=18;actor.GetComponent<NavMeshAgent>().enabled=true;
   var link=actor.gameObject.AddComponent<JourneyGrowthLesson>();link.Session=s;
   var ec=new SerializedObject(actor.GetComponent<EnemyController>());ec.FindProperty("_player").objectReferenceValue=s.Player;ec.FindProperty("_playerVitals").objectReferenceValue=s.Player.GetComponent<PlayerVitals>();ec.ApplyModifiedPropertiesWithoutUndo();
   s.Encounters=s.Encounters.Concat(new[]{actor}).ToArray();s.Content.EncounterRules=s.Content.EncounterRules.Concat(new[]{new PrologueContentSO.EncounterRule{Id=actor.Id,RequiredCompleted=new[]{"j1:reported"}}}).ToArray();
   var wiring=new SerializedObject(s.Wiring);var enemies=wiring.FindProperty("_enemies");enemies.arraySize=s.Encounters.Length;for(int i=0;i<s.Encounters.Length;i++)enemies.GetArrayElementAtIndex(i).objectReferenceValue=s.Encounters[i].GetComponent<EnemyVitals>();wiring.ApplyModifiedPropertiesWithoutUndo();
   if(!NavMesh.SamplePosition(actor.transform.position,out _,3,NavMesh.AllAreas))throw new Exception("Lesson outside navigation");
   EditorUtility.SetDirty(s);EditorUtility.SetDirty(s.Content);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);return "Growth lesson actor and confirmed-Metal interruption proof connected";
  }
 }
}
