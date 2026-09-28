using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  static string Commission(){
   var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty)throw new Exception("Saved Journey edit required");
   if(GameObject.Find("Journey Relay and Logging")!=null)throw new Exception("Commission already authored");
   var session=Object.FindFirstObjectByType<PrologueSession>();var root=new GameObject("Journey Relay and Logging");
   var ground=Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Single(c=>c.name=="Valley");
   Vector3 Ground(float x,float z,float lift=0){if(!ground.Raycast(new Ray(new Vector3(x,200,z),Vector3.down),out var hit,400))throw new Exception("Ground missing");return hit.point+Vector3.up*lift;}
   var wood=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/Materials/Timber.mat");var stone=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/Materials/PaleStone.mat");
   void Prop(string name,PrimitiveType shape,Vector3 pos,Vector3 scale,Material mat){var go=GameObject.CreatePrimitive(shape);go.name=name;go.transform.SetParent(root.transform);go.transform.position=pos;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=mat;}
   var giver=Ground(5,55);Prop("Jeongdam temporary actor",PrimitiveType.Capsule,giver+Vector3.up,new Vector3(.65f,1,.65f),stone);
   Prop("Relay desk",PrimitiveType.Cube,giver+new Vector3(-1,.65f,0),new Vector3(1.2f,.12f,.6f),wood);
   foreach(float x in new[]{3.5f,4.5f})Prop("Desk support",PrimitiveType.Cube,Ground(x,55,.3f),new Vector3(.12f,.6f,.5f),wood);
   var evidence=Ground(12,119);for(int i=0;i<7;i++)Prop("Cut timber "+i,PrimitiveType.Cylinder,Ground(17+i%3*1.8f,113+i/3*3,.5f),new Vector3(1.1f,.5f,1.1f),wood);
   Prop("Logging ledger",PrimitiveType.Cube,evidence+Vector3.up*.2f,new Vector3(.7f,.4f,.5f),wood);
   var actors=session.Encounters.ToList();for(int i=0;i<2;i++){
    var actor=Object.Instantiate(session.Encounters[1].gameObject,root.transform).GetComponent<PrologueEncounter>();actor.Id="LoggingBeast"+i;actor.name=actor.Id;actor.transform.position=Ground(i==0?8:17,i==0?110:123,1);actor.PatrolPoints=new[]{actor.transform.position,actor.transform.position+Vector3.forward*2};actor.Session=session;actor.Player=session.Player;actors.Add(actor);
   }
   session.Encounters=actors.ToArray();DevSceneKit.WireRigSeam(actors[0].GetComponent<EnemyController>(),actors[0].GetComponent<EnemyVitals>(),actors.Select(a=>a.GetComponent<EnemyVitals>()).ToArray());
   var points=session.Content.Points.ToList();points.Add(new PrologueContentSO.Point{Id="jeongdam_j1",Kind=PrologueInteractionKind.Conversation,Position=giver,Prompt="정담과 이야기",Text="",Radius=2.5f});
   points.Add(new PrologueContentSO.Point{Id="LoggingEvidence",Kind=PrologueInteractionKind.Evidence,Position=evidence,Prompt="벌목 장부 살피기",Text="마지막 줄이 같은 말로 덧씌워졌다. 베어도 다시 자란다.",RequiredCompleted=new[]{"j1:accepted"},RequiredDefeated=new[]{"JourneyBeast1","LoggingBeast0","LoggingBeast1"},LockedText="장부 주변에 물든 짐승들이 남아 있다. 먼저 정담에게 길 사정을 물어야겠다."});
   session.Content.Points=points.ToArray();session.Content.Commissions=new[]{new PrologueContentSO.Commission{Id="j1",GiverId="jeongdam_j1",EvidenceId="LoggingEvidence",Reward=60,OfferText="정담: 오랜만이군. 벌목장 인부들이 모두 내려왔네. 숲길을 확인해 주겠나?",WaitingText="정담: 벌목장 장부부터 찾아보게. 돌아온 인부들은 말을 아끼더군.",ReportText="정담: 베어도 자란다… 심부에서부터 번져 온 모양이군. 수고했네.",CompletedText="정담: 더 깊은 숲에 들기 전에 주막에서 쉬어 가게."}};
   EditorUtility.SetDirty(session.Content);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);return "Relay giver, logging props, three-target evidence condition and report reward connected; four encounters total";
  }
  static string CommissionCheck(){
   var s=Object.FindFirstObjectByType<PrologueSession>();if(!EditorApplication.isPlaying||s==null||s.Progress==null||!s.TestSaveSuffix.StartsWith("-audit-"))throw new Exception("Isolated Journey play required");
   var checks=new List<string>();void Check(bool v,string n){checks.Add((v?"PASS ":"FAIL ")+n);}void Interact(string id){var p=s.Content.Points.Single(p=>p.Id==id);s.Teleport(p.Position+Vector3.up,0);s.Interact(id);}
   var evidenceView=Object.FindFirstObjectByType<JourneyEvidenceView>();
   void EvidenceVisible(bool site,bool recipient,string label){if(evidenceView==null){Check(false,"evidence component present");return;}evidenceView.RefreshFromProgress();Check(evidenceView.AtSite.activeSelf==site&&evidenceView.AtRecipient.activeSelf==recipient,label);}
   EvidenceVisible(true,false,"ledger begins at logging site");
   Interact("LoggingEvidence");Check(!s.Progress.completed.Contains("LoggingEvidence"),"evidence blocked before acceptance");Interact("jeongdam_j1");Check(s.Progress.completed.Contains("j1:accepted"),"accepted");Interact("jeongdam_j1");Check(!s.Progress.completed.Contains("j1:reported"),"premature report blocked");
   Interact("LoggingEvidence");Check(!s.Progress.completed.Contains("LoggingEvidence"),"live enemies block evidence");foreach(var e in s.Encounters.Where(e=>e.Id=="JourneyBeast1"||e.Id=="LoggingBeast0"||e.Id=="LoggingBeast1"))e.GetComponent<EnemyVitals>().TakeDamage(10000);Interact("LoggingEvidence");Check(s.Progress.completed.Contains("LoggingEvidence"),"defeated enemies permit investigation");
   EvidenceVisible(false,false,"investigated ledger carried away from site");
   int before=s.Progress.currency;Interact("jeongdam_j1");Check(s.Progress.completed.Contains("j1:reported")&&s.Progress.currency==before+60,"report pays once");Interact("jeongdam_j1");Check(s.Progress.currency==before+60,"repeated report no reward");s.Save();var loaded=new PrologueProgressStore(Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+s.TestSaveSuffix+".json")).Load();Check(loaded.completed.Contains("j1:reported")&&loaded.defeated.Count>=3,"quest and kills persist");
   EvidenceVisible(false,true,"reported ledger delivered to relay desk");
   checks.Add("Diagnostic direct calls and synthetic damage, not an input combat playthrough.");var result=string.Join("\n",checks);File.WriteAllText(Path.GetFullPath("../Art/World/PineRest/commission_checks.txt"),result);return result;
  }
 }
}
