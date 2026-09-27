using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using Oheangbu.EditorTools.Prologue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  static string Opening(){
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
   var scene=SceneManager.GetActiveScene();if(scene.path!=Scene||scene.isDirty)throw new Exception("Open saved Journey first");
   if(GameObject.Find("Journey Opening")!=null)throw new Exception("Opening exists; edit without regeneration");
   var holder=new GameObject("Journey Opening");
   var source=EditorSceneManager.OpenScene("Assets/_Project/Scenes/Dev/C2_CodexWorld.unity",OpenSceneMode.Additive);
   try {var mine=source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="00_Abandoned_Mine");var copy=Object.Instantiate(mine.gameObject);SceneManager.MoveGameObjectToScene(copy,scene);copy.transform.SetParent(holder.transform,true);}
   finally{EditorSceneManager.CloseScene(source,true);SceneManager.SetActiveScene(scene);}
   var session=Object.FindFirstObjectByType<PrologueSession>();var content=session.Content;
   content.StartPosition=new Vector3(0,1.1f,-27);content.StartYaw=0;
   var points=content.Points.ToList();var evidence=points.Single(p=>p.Id=="BurntCord");evidence.Position=new Vector3(2,0,-15);evidence.Prompt="그을린 지지목 살피기";evidence.Text="돌은 안쪽으로 흩어졌다. 끊긴 줄에서 화약 냄새가 난다.";
   points.Add(new PrologueContentSO.Point{Id="MineStart",Kind=PrologueInteractionKind.Rest,Position=new Vector3(-2,0,-27),Prompt="돌무더기 곁에서 숨 고르기",Text="",Radius=2.5f});
   content.Points=points.ToArray();content.MainPath=new[]{new Vector3(0,0,-27),new Vector3(0,0,-17),new Vector3(0,0,-8),new Vector3(0,0,1),new Vector3(0,.1f,12),new Vector3(-4,.8f,24),content.Points.Single(p=>p.Id=="InnRest").Position};
   var wood=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/Materials/Timber.mat");var stone=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/Materials/PaleStone.mat");
   void Prop(string name,Vector3 p,Vector3 scale,Material mat,float yaw=0){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(holder.transform);g.transform.position=p;g.transform.localScale=scale;g.transform.rotation=Quaternion.Euler(0,yaw,0);g.GetComponent<Renderer>().sharedMaterial=mat;}
   Prop("Scorched support evidence",new Vector3(2,.25f,-15),new Vector3(.28f,.3f,2.2f),wood,31);
   Prop("Mine resting stone",new Vector3(-2,.25f,-27),new Vector3(.9f,.5f,.7f),stone);
   var reward=content.Points.Single(p=>p.Id=="WorkerSatchel");Prop("Worker satchel",reward.Position+Vector3.up*.22f,new Vector3(.5f,.4f,.35f),wood,23);
   var enemy=session.Encounters[0];enemy.transform.position=new Vector3(0,1,-6);enemy.PatrolPoints=new[]{new Vector3(0,1,-6),new Vector3(0,1,-2)};enemy.DetectionRange=10;enemy.Leash=16;
   foreach(var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))if(light.type!=LightType.Directional)light.shadows=LightShadows.None;
   session.Player.position=content.StartPosition;var driver=Object.FindFirstObjectByType<WorldLookDriver>();PrologueBuilder.Set(driver,"_skyCamera",Camera.main);driver.Apply();
   var surface=Object.FindFirstObjectByType<NavMeshSurface>();surface.RemoveData();surface.center=new Vector3(0,20,42);surface.size=new Vector3(110,80,190);surface.BuildNavMesh();
   if(surface.navMeshData==null)throw new Exception("Opening navigation bake failed");var nav=Object.Instantiate(surface.navMeshData);surface.RemoveData();AssetDatabase.CreateAsset(nav,Folder+"/OpeningNavigation.asset");surface.navMeshData=nav;surface.AddData();
   EditorUtility.SetDirty(content);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
   return "Mine, evidence, start rest and reward prop authored; first encounter moved into cave; navigation rebaked";
  }
  static string AuditStart(){if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");if(SceneManager.GetActiveScene().path!=Scene)throw new Exception("Journey only");Object.FindFirstObjectByType<PrologueSession>().TestSaveSuffix="-audit-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss");EditorApplication.isPlaying=true;return "Started isolated runtime audit";}
  static string RuntimeChecks(){
   if(!EditorApplication.isPlaying||SceneManager.GetActiveScene().path!=Scene)throw new Exception("Journey play required");var session=Object.FindFirstObjectByType<PrologueSession>();if(session.Progress==null||!session.TestSaveSuffix.StartsWith("-audit-"))throw new Exception("Isolated initialized session required");
   var report=new List<string>();void Check(bool value,string name){report.Add((value?"PASS ":"FAIL ")+name);}
   Check(session.Encounters.Where(e=>e.gameObject.activeInHierarchy).All(e=>e.GetComponent<NavMeshAgent>().isOnNavMesh),"active enemies on navigation");
   var reward=session.Content.Points.Single(p=>p.Id=="WorkerSatchel");session.Teleport(reward.Position+Vector3.up,0);session.Interact(reward.Id);int balance=session.Progress.currency;session.Interact(reward.Id);Check(balance==24&&session.Progress.currency==balance,"reward once");
   session.Player.GetComponent<PlayerVitals>().ApplyFatalFall();Check(session.Progress.currency==0&&session.Progress.dropCurrency==balance,"death drop");Check(Vector3.Distance(session.Player.position,session.Progress.checkpointPosition)<.1f,"checkpoint respawn");
   session.Teleport(session.Progress.dropPosition,0);session.Interact("CurrencyDrop");Check(session.Progress.currency==balance&&session.Progress.dropCurrency==0,"retrieve currency");
   session.Encounters[0].GetComponent<EnemyVitals>().TakeDamage(10000);Check(!session.Encounters[0].GetComponent<EnemyVitals>().IsAlive,"enemy dies");
   var rest=session.Content.Points.Single(p=>p.Id=="InnRest");session.Teleport(rest.Position+Vector3.up,0);session.Interact(rest.Id);Check(session.Progress.checkpoint==rest.Id&&session.Encounters.Where(e=>e.gameObject.activeInHierarchy).All(e=>e.GetComponent<EnemyVitals>().IsAlive),"rest checkpoint and enemy reset");
   session.Save();var path=Path.Combine(Application.persistentDataPath,session.Content.SaveSlot+session.TestSaveSuffix+".json");var restored=new PrologueProgressStore(path).Load();Check(restored!=null&&restored.checkpoint==rest.Id&&restored.completed.Contains(reward.Id),"disk state round trip");
   report.Add("Diagnostic only: direct interactions, teleport and damage. Not actual-input combat, walking or application restart.");var result=string.Join("\n",report);File.WriteAllText(Path.GetFullPath("../Art/World/PineRest/journey_runtime_checks.txt"),result);return result;
  }
 }
}
