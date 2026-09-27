using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Drawing;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static double transferDeadline251,lastMove251;
  static Vector3 Feet251(CharacterController body)=>body.transform.TransformPoint(body.center)-Vector3.up*(body.height*.5f);
  static void Check251(bool ok,string label,string file="runtime")
  {Directory.CreateDirectory(ProgressionOutput251);File.AppendAllText(ProgressionOutput251+"/"+file+".txt",(ok?"PASS ":"FAIL ")+label+"\n");if(!ok)throw new Exception(label);}
  public static string ProgressionAudit251()
  {
   var scene=FrontageScene249();var s=VillageSession();File.WriteAllText(ProgressionOutput251+"/audit.txt","");
   void C(bool ok,string label)=>Check251(ok,label,"audit");
   C(s.Actors.Length==s.Content.Encounters.Length&&s.Actors.Select(a=>a.Id).Distinct().Count()==s.Actors.Length,"unique actor/content binding");
   C(s.Content.Campaign.IsValid&&s.Content.Campaign.UseExplicitPrerequisites,"explicit campaign conditions valid");
   foreach(var id in new[]{"cheongryong","demo_growth_lesson"})
   {
    var a=s.Actors.Single(x=>x.Id==id);var e=s.Content.Encounters.Single(x=>x.Id==id);
    C(a.Player==s.Walker.Body.transform,"candidate player binding "+id);
    C(Vector3.Distance(a.transform.position-Vector3.up*a.GetComponent<NavMeshAgent>().baseOffset,e.Feet)<.1f,"actor and ledger feet "+id);
    C(NavMesh.SamplePosition(e.Feet,out _,2,NavMesh.AllAreas),"navigation under "+id);
    C(a.GetComponentsInChildren<Renderer>(true).All(r=>r.sharedMaterials.All(m=>m!=null&&m.shader!=null&&m.shader.isSupported)),"render materials supported "+id);
   }
   var site=Object.FindObjectsByType<DemoGukRevisitSite>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(g=>g.gameObject.scene==scene);
   C(s.DemoWoodLiftProfile!=null&&site.LiftPad!=null&&site.UpperSurface!=null,"field service and site references");
   C(site.transform.Find("ExistingStone_Descent")==null,"old walk-up ramp absent from gated reward");
   C(site.UpperSurface.position.y-site.LiftPad.position.y>2.1f,"reward is above ordinary step height");
   C(s.Content.Points.Where(p=>p.Id==DemoGukRevisitSite.Id).All(p=>Vector3.Distance(p.Position,site.UpperSurface.position)<.1f),"reward interaction follows actual upper surface");
   C(s.Content.SaveSlot=="world-demo-compact-cave-v4","existing candidate slot retained");
   var plan=JsonUtility.FromJson<ProgressionPlan251>(File.ReadAllText(ProgressionOutput251+"/route-plan.json"));var ground=FinalSurface(scene);
   foreach(var r in plan.routes)
   {
    bool available=true;float length=0;var path=new NavMeshPath();
    for(int i=1;i<r.points.Length;i++)
    {
     var pa=ground(r.points[i-1].x,r.points[i-1].y).point;var pb=ground(r.points[i].x,r.points[i].y).point;
     bool a=NavMesh.SamplePosition(pa,out var na,3,NavMesh.AllAreas),b=NavMesh.SamplePosition(pb,out var nb,3,NavMesh.AllAreas);
     bool connected=a&&b&&NavMesh.CalculatePath(na.position,nb.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;
     if(!connected)File.AppendAllText(ProgressionOutput251+"/audit.txt","DETAIL "+r.id+" "+i+" "+pa+" to "+pb+"\n");
     available&=connected;length+=Vector3.Distance(pa,pb);
    }
    C(available,"continuous baked route "+r.id+" length="+length);
   }
   return File.ReadAllText(ProgressionOutput251+"/audit.txt");
  }
  public static string ProgressionRuntime251(string command)
  {
   var s=VillageSession();if(!EditorApplication.isPlaying||!s.TestSaveSuffix.StartsWith("_compact_slice_"))throw new Exception("Private diagnostic Play required");
   var ui=PlaytestUiRoot.Instance;var boss=s.Actors.Single(a=>a.Id=="cheongryong");var site=Object.FindFirstObjectByType<DemoGukRevisitSite>();
   string path=Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+s.TestSaveSuffix+".json");
   void C(bool ok,string label)=>Check251(ok,label);
   if(command=="boss-save")
   {
    File.WriteAllText(ProgressionOutput251+"/runtime.txt","");ui.CloseMenu();ui.Gate.ReleaseImmediately();
    C(!s.Progress.campaign.Completed.Contains("relay")&&!s.Progress.campaign.Completed.Contains("deep_forest")&&!s.HasDemoGuk,"fresh state before NPC contact, lesson and boss");
    C(s.DemoField!=null&&!s.DemoField.IsUnlocked,"field service connected but locked before boss");
    var actor=boss;var life=actor.GetComponent<EnemyVitals>();s.CombatActive=false;s.Cull();actor.enabled=true;var controller=actor.GetComponent<CheongryongCombatController>();controller.enabled=true;controller.AttackEnabled=false;
    C(life.enabled&&life.IsAlive,"boss available without report or lesson");
    int before=s.Progress.ledger.currency;SessionState.SetInt("Progression251.before",before);
    s.EnemyDefeated(actor.Id);C(!s.HasDemoGuk&&s.Progress.ledger.currency==before,"string-only defeat cannot unlock");
    using(var locked=new FileStream(path+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None))
    {
     life.TakeDamage(float.MaxValue,AttackProvenance.Create(s.Walker.Body.gameObject,DamageSource.PlayerDirect,Element.Metal));
     C(!life.IsAlive&&!s.HasDemoGuk&&s.Progress.ledger.currency==before,"failed boss write grants neither reward nor ability");
     C(!s.SaveNow(out _),"pending boss prevents partial ordinary save");
     C(!s.TryEquipment(Oheangbu.Data.Demo.EquipmentAction.Unequip,null,Oheangbu.Data.Demo.EquipmentSlot.Brush,s.Progress.equipment.Revision,"pending-boss",out _),"pending boss blocks equipment transaction");
     var rest=s.Content.Points.Single(p=>p.Id=="sanctuary_rest251");
     C(!(bool)typeof(WorldMacroPlaytestSession).GetMethod("Rest",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(s,new object[]{rest}),"pending boss blocks checkpoint rest even through internal entry");
    }
    C(s.SaveNow(out _),"boss save retries synchronously after lock release");
    int reward=s.Content.Campaign.Stages.Single(p=>p.Id=="cheongryong").TongboReward;
    C(s.HasDemoGuk&&s.Progress.ledger.currency==before+reward&&s.Progress.ui.knownVirtues.Contains("仁"),"actual death commits reward, Guk and Ren together");
    s.EnemyDefeated(actor.Id);C(s.Progress.ledger.currency==before+reward,"duplicate boss notification cannot repay");
    var disk=JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(path));C(disk.defeated.Contains("cheongryong")&&disk.campaign.Facts.Contains("defeated:cheongryong"),"boss death and permanent fact stored on disk");
    ui.CloseMenu();ui.Gate.ReleaseImmediately();
    s.Teleport(site.LiftPad.position,Quaternion.LookRotation(Vector3.ProjectOnPlane(site.UpperSurface.position-site.LiftPad.position,Vector3.up)).eulerAngles.y);
    return "Boss transaction checks complete; lower lift pad prepared";
   }
   if(command=="cast")
   {
    ui.CloseMenu();ui.Gate.ReleaseImmediately();s.CombatActive=false;
    C(!s.Progress.ledger.completed.Contains(DemoGukRevisitSite.Id),"standing at pad cannot auto-claim reward");
    typeof(Oheangbu.App.CombatLoopWiring).GetMethod("OnLetterDrawn",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(s.Walker.Wiring,new object[]{new DrawnLetter('국',default,default,null,.8f,.7f,2f,2f,4)});
    C(s.DemoField.State==FieldLiftState.Rising,"central cast resolver starts actual Guk lift: "+s.DemoField.LastFailure);return "Wait for actual lift updates";
   }
   if(command=="transfer")
   {
    C(s.DemoField.State==FieldLiftState.Holding&&s.DemoField.PassengerSupported&&s.DemoField.CurrentHeight>2.3f,"real controller rides live lift to supported 2.4m");
    transferDeadline251=EditorApplication.timeSinceStartup+10;lastMove251=EditorApplication.timeSinceStartup;EditorApplication.update-=Transfer251;EditorApplication.update+=Transfer251;return "Actual collision-resolved transfer running";
   }
   if(command=="status")return File.ReadAllText(ProgressionOutput251+"/runtime.txt");
   if(command=="life")
   {
    ui.CloseMenu();ui.Gate.ReleaseImmediately();s.CombatActive=false;
    var lesson=s.Actors.Single(a=>a.Id=="demo_growth_lesson");
    lesson.gameObject.SetActive(true);lesson.enabled=true;
    var life=lesson.GetComponent<EnemyVitals>();life.enabled=true;
    var growth=lesson.GetComponent<CheongryongGrowthController>();
    var proof=lesson.GetComponent<DemoGrowthLessonLink>();
    life.TakeDamage(life.MaxHp*.51f,AttackProvenance.Create(s.Walker.Body.gameObject,DamageSource.PlayerDirect,Element.Wood));
    C(growth.IsWindingUp,"living lesson begins growth at half health");
    using(var locked=new FileStream(path+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None))
    {
     life.TakeDamage(1,AttackProvenance.Create(s.Walker.Body.gameObject,DamageSource.PlayerDirect,Element.Metal));
     C(proof.HasProof&&!s.Progress.ledger.completed.Contains(DemoGrowthLessonLink.LessonId),"confirmed Metal interruption waits for successful save");
    }
    C(proof.TryCommit()&&s.Progress.ledger.completed.Contains(DemoGrowthLessonLink.LessonId),"late lesson after boss commits on retry");
    var rest=s.Content.Points.Single(p=>p.Id=="sanctuary_rest251");
    s.Teleport(s.Content.Checkpoints.Single(p=>p.Id==rest.Id).Feet,0);
    C((bool)typeof(WorldMacroPlaytestSession).GetMethod("Rest",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(s,new object[]{rest}),"new sanctuary checkpoint accepts committed rest");
    var hp=s.Walker.Body.GetComponent<PlayerVitals>();
    C(s.HasDemoGuk&&s.Progress.ledger.completed.Contains(DemoGukRevisitSite.Id)&&!boss.GetComponent<EnemyVitals>().IsAlive,"rest retains boss and claimed field reward");
    C(hp.TakeDamage(float.MaxValue)&&hp.Hp01>0&&s.Progress.renUsed,"earned Ren survives lethal hit and saves consumption");
    C((bool)typeof(WorldMacroPlaytestSession).GetMethod("Rest",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(s,new object[]{rest})&&s.RenAvailable,"rest recharges Ren");
    hp.ApplyFatalFall();return "Environment death queued; check after recovery update";
   }
   if(command=="death-check")
   {
    var expected=s.Content.Checkpoints.Single(p=>p.Id=="sanctuary_rest251");
    C(s.Walker.Body.GetComponent<PlayerVitals>().Hp01>0&&Vector3.Distance(s.Walker.Body.transform.position,expected.Feet)<2,"death recovers at new sanctuary checkpoint");
    C(s.HasDemoGuk&&s.Progress.ledger.completed.Contains(DemoGukRevisitSite.Id)&&s.Progress.ledger.completed.Contains(DemoGrowthLessonLink.LessonId),"death retains ability, reward and late lesson");
    C(s.Progress.campaign.Facts.Contains("defeated:cheongryong")&&!boss.GetComponent<EnemyVitals>().IsAlive,"death cannot respawn defeated boss");
    C(s.SaveNow(out _),"post-recovery snapshot saves");return "Death persistence verified";
   }
   if(command=="reload")
   {
    C(s.HasDemoGuk&&s.Progress.ledger.completed.Contains(DemoGukRevisitSite.Id),"restart retains ability and claimed field reward");
    C(s.Progress.campaign.Facts.Contains("defeated:cheongryong")&&!boss.GetComponent<EnemyVitals>().IsAlive,"restart retains permanent boss death");
    C(s.Progress.equipment.Owned.Count>=2,"equipment survives shared progression save");return "Reload verified";
   }
   throw new Exception("Unknown progression check");
  }
  static void Transfer251()
  {
   try
   {
    var s=VillageSession();var site=Object.FindFirstObjectByType<DemoGukRevisitSite>();var now=EditorApplication.timeSinceStartup;
    if(!EditorApplication.isPlaying||now>transferDeadline251)throw new Exception("Lift transfer timeout");
    float dt=Mathf.Clamp((float)(now-lastMove251),0,.05f);lastMove251=now;
    var body=s.Walker.Body;var feet=Feet251(body);var horizontal=Vector3.ProjectOnPlane(site.UpperSurface.position-feet,Vector3.up);
    body.Move(Vector3.ClampMagnitude(horizontal,2.2f*dt)+Vector3.down*dt*1.2f);
    feet=Feet251(body);
    if(feet.y<site.UpperSurface.position.y-.35f)throw new Exception("Transfer fell below upper stone: "+feet);
    if(Vector3.ProjectOnPlane(site.UpperSurface.position-feet,Vector3.up).magnitude>.15f||Mathf.Abs(feet.y-site.UpperSurface.position.y)>.23f)return;
    EditorApplication.update-=Transfer251;int before=s.Progress.ledger.currency;
    Check251(s.CanInteract(DemoGukRevisitSite.Id)&&s.Interact(DemoGukRevisitSite.Id),"actual lift and controller transfer permit reward interaction");
    int reward=s.Content.Campaign.Stages.Single(p=>p.Id=="guk_return").TongboReward;
    Check251(s.Progress.ledger.completed.Contains(DemoGukRevisitSite.Id)&&s.Progress.ledger.currency==before+reward,"field proof pays configured reward once");
    var ui=PlaytestUiRoot.Instance;ui.CloseMenu();ui.Gate.ReleaseImmediately();s.Interact(DemoGukRevisitSite.Id);
    Check251(s.Progress.ledger.currency==before+reward,"repeated field interaction does not repay");s.SaveNow(out _);
   }
   catch(Exception e){EditorApplication.update-=Transfer251;File.AppendAllText(ProgressionOutput251+"/runtime.txt","FAIL "+e+"\n");Debug.LogException(e);}
  }
 }
}
