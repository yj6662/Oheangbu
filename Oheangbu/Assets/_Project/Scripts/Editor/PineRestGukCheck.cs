using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  static int gukStep,gukBalance;static double gukNext,gukDeadline,gukLast;static float gukInk;
  static readonly List<string> gukChecks=new List<string>();const string GukReport="../Art/World/PineRest/guk_runtime_checks.txt";
  static string GukCheck(){
   var s=Object.FindFirstObjectByType<PrologueSession>();if(!EditorApplication.isPlaying||s?.Progress==null||!s.TestSaveSuffix.StartsWith("-audit-")||s.HasGuk)throw new Exception("Fresh isolated Play required");
   gukChecks.Clear();gukStep=0;gukNext=EditorApplication.timeSinceStartup;gukDeadline=gukNext+45;EditorApplication.update+=GukTick;File.WriteAllText(GukReport,"RUNNING");return "Guk unlock/lift/ledge transaction check running";
  }
  static void GukTick(){
   try{
    if(!EditorApplication.isPlaying||EditorApplication.timeSinceStartup>gukDeadline)throw new Exception("Stopped or timeout");
    if(EditorApplication.timeSinceStartup<gukNext)return;
    var s=Object.FindFirstObjectByType<PrologueSession>();var field=s.JourneyField;var cc=s.Player.GetComponent<CharacterController>();var site=s.RevisitSite;
    var ink=(InkPool)typeof(PrologueSession).GetField("ink",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(s);
    void Check(bool v,string label){gukChecks.Add((v?"PASS ":"FAIL ")+label);}
    void Position(Vector3 feet){s.Teleport(feet+Vector3.up*(cc.height*.5f-cc.center.y+.03f),0);}
    switch(gukStep){
     case 0: Check(field!=null&&!field.IsUnlocked,"Guk locked before boss record");CommissionCheck();gukStep++;gukNext=EditorApplication.timeSinceStartup+.5;break;
     case 1: s.Encounters.Single(a=>a.Id=="cheongryong").GetComponent<EnemyVitals>().TakeDamage(100000);Check(field.IsUnlocked,"persisted boss death unlocks Guk");Position(site.UpperSurface.position);gukBalance=s.Progress.currency;s.Interact(DemoGukRevisitSite.Id);Check(s.Progress.currency==gukBalance&&!s.Progress.completed.Contains(DemoGukRevisitSite.Id),"upper teleport alone cannot collect");Position(site.LiftPad.position);gukStep++;gukNext=EditorApplication.timeSinceStartup+.6;break;
     case 2: gukInk=ink.Value;typeof(Oheangbu.App.CombatLoopWiring).GetMethod("OnLetterDrawn",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(s.Wiring,new object[]{new DrawnLetter('국',default,default,null,.8f,.7f,2,2,4)});Check(field.HasPlatform&&ink.Value<gukInk,"post-recognition pipeline creates lift and pays ink");if(!field.HasPlatform)throw new Exception("Lift failure: "+field.LastFailure);gukStep++;break;
     case 3: if(field.State!=FieldLiftState.Holding)return;Check(field.PassengerSupported&&field.CurrentHeight>2.1f,"live service carries player to holding height");ScreenCapture.CaptureScreenshot(Path.GetFullPath("../Art/World/PineRest/guk_lift.png"));gukLast=EditorApplication.timeSinceStartup;gukStep++;break;
     case 4: float dt=(float)Math.Min(.04,EditorApplication.timeSinceStartup-gukLast);gukLast=EditorApplication.timeSinceStartup;var delta=site.UpperSurface.position-s.Player.position;delta.y=0;if(delta.magnitude>.15f){cc.Move(delta.normalized*dt*1.4f);return;}gukStep++;gukNext=EditorApplication.timeSinceStartup+.35;break;
     case 5: var feet=s.Player.TransformPoint(cc.center)-Vector3.up*cc.height*.5f;Check(Mathf.Abs(feet.y-site.UpperSurface.position.y)<.25f,"CharacterController reaches authored upper shelf");s.Interact(DemoGukRevisitSite.Id);Check(s.Progress.completed.Contains(DemoGukRevisitSite.Id)&&s.Progress.currency==gukBalance+40,"lift proof and upper arrival collect reward");s.Interact(DemoGukRevisitSite.Id);Check(s.Progress.currency==gukBalance+40,"revisit reward is one-time");var disk=new PrologueProgressStore(Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+s.TestSaveSuffix+".json")).Load();Check(disk.defeated.Contains("cheongryong")&&disk.completed.Contains(DemoGukRevisitSite.Id),"unlock and revisit persist together");ScreenCapture.CaptureScreenshot(Path.GetFullPath("../Art/World/PineRest/guk_ledge.png"));FinishGukCheck();break;
    }
   }catch(Exception e){gukChecks.Add("FAIL "+e);FinishGukCheck();}
  }
  static void FinishGukCheck(){EditorApplication.update-=GukTick;gukChecks.Add("Synthetic recognized letter, setup teleport/boss damage and scripted CharacterController movement. Not hand drawing, virtual mouse recognition, or manual traversal.");File.WriteAllText(GukReport,string.Join("\n",gukChecks));}
 }
}
