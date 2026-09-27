using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Oheangbu.App.Prologue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  static string CollectibleViews(){
   var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty)throw new Exception("Saved Journey Edit Mode required");
   var session=UnityEngine.Object.FindFirstObjectByType<PrologueSession>();
   string[] names={"Worker belongings","High trail belongings"},ids={"WorkerSatchel","guk_high_reward"};
   var props=names.Select(n=>GameObject.Find(n)??throw new Exception("Missing "+n)).ToArray();
   for(int i=0;i<props.Length;i++){var view=props[i].GetComponent<JourneyCollectibleView>()??props[i].AddComponent<JourneyCollectibleView>();view.Session=session;view.PointId=ids[i];EditorUtility.SetDirty(view);}
   EditorSceneManager.SaveScene(scene);return "Two collectible visuals bound to committed progress";
  }
  static string CollectibleViewCheck(){
   var s=RoadRestSession();if(s.Progress.completed.Contains("WorkerSatchel"))throw new Exception("Fresh isolated save required");
   var view=UnityEngine.Object.FindObjectsByType<JourneyCollectibleView>(FindObjectsSortMode.None).Single(v=>v.PointId=="WorkerSatchel");
   var report=new List<string>();void Check(bool value,string detail){report.Add((value?"PASS ":"FAIL ")+detail);}
   bool Visible()=>view.GetComponentsInChildren<Renderer>(true).Any(r=>r.enabled);
   var field=typeof(PrologueSession).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic);var original=(PrologueProgressStore)field.GetValue(s);
   string blocker=Path.GetFullPath("../Art/World/PineRest/collectible-block-"+Guid.NewGuid().ToString("N"));File.WriteAllText(blocker,"Owned failure fixture");
   try{
    view.RefreshFromProgress();Check(Visible(),"uncollected prop visible");
    var point=s.Content.Points.Single(p=>p.Id==view.PointId);s.Teleport(point.Position+Vector3.up,0);int before=s.Progress.currency;
    field.SetValue(s,new PrologueProgressStore(Path.Combine(blocker,"state.json")));s.Interact(point.Id);view.RefreshFromProgress();
    Check(Visible()&&!s.Progress.completed.Contains(point.Id)&&s.Progress.currency==before,"failed save preserves prop and reward");
    field.SetValue(s,original);s.Interact(point.Id);view.RefreshFromProgress();
    Check(!Visible()&&view.GetComponentsInChildren<Collider>(true).All(c=>!c.enabled),"committed collection hides renderers and colliders");
    Check(original.Load().completed.Contains(point.Id)&&s.Progress.currency==before+point.Currency,"collection and reward persisted");
    s.Interact(point.Id);Check(s.Progress.currency==before+point.Currency,"repeat interaction cannot pay twice");
    var rest=s.Content.Points.Single(p=>p.Id=="InnRest");s.Teleport(rest.Position+Vector3.up,0);s.Interact(rest.Id);view.RefreshFromProgress();Check(!Visible(),"rest does not respawn collectible");
   }finally{field.SetValue(s,original);File.Delete(blocker);}
   report.Add("Direct interaction/failure fixture; real F input checked separately. Fresh process visual restore not covered.");string text=string.Join("\n",report);File.WriteAllText("../Art/World/PineRest/collectible_checks.txt",text);return text;
  }
 }
}
