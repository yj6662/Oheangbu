using System;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Data.Demo;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static void BranchCheck259(bool ok,string message,string file)
  {Directory.CreateDirectory(BranchOutput259);File.AppendAllText(BranchOutput259+"/"+file+".txt",(ok?"PASS ":"FAIL ")+message+"\n");if(!ok)throw new Exception(message);}
  public static string BranchAudit259()
  {
   var scene=FrontageScene249();var s=VillageSession();var roots=scene.GetRootGameObjects();
   File.WriteAllText(BranchOutput259+"/audit.txt","");void C(bool ok,string label)=>BranchCheck259(ok,label,"audit");
   C(roots.Count(g=>g.name==BranchRoot259)==1,"one owned branch root");
   var root=roots.Single(g=>g.name==BranchRoot259);
   C(root.GetComponentsInChildren<WorldMacroContentPoint>(true).Count(p=>p.Id==HerbTrace259)==1,"one evidence visual");
   C(s.Content.Points.Count(p=>p.Id==HerbTrace259)==1&&s.InteractionVisuals.Count(p=>p.Id==HerbTrace259)==1,"one content and focus binding");
   C(root.GetComponentsInChildren<Renderer>(true).All(r=>r.sharedMaterials.All(m=>m!=null&&m.shader!=null&&m.shader.isSupported)),"supported branch materials");
   var campaign=s.Content.Campaign;var stage=campaign.Stages.Single(p=>p.Id==HerbTrace259);
   C(campaign.IsValid&&campaign.UseExplicitPrerequisites,"valid explicit campaign graph");
   C(stage.Optional&&stage.PrerequisiteIds.Length==0&&stage.RequiredFacts.Length==0&&stage.TongboReward==0,"optional evidence has no acceptance or reward gate");
   C(!campaign.Stages.Any(p=>p.PrerequisiteIds.Contains(HerbTrace259)||p.RequiredFacts.Contains(HerbFact259)),"main progression does not require branch");
   var map=roots.SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single().MapData;
   var layout=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single().Layout;
   var plan=JsonUtility.FromJson<ProgressionPlan251>(File.ReadAllText(BranchOutput259+"/route-plan.json"));
   var ground=FinalSurface(scene);
   foreach(var route in plan.routes)
   {
    C(layout.Routes.Count(r=>r.Id==route.id)==1&&map.Lines.Count(l=>l.Id==route.id)==1,"ledger and map route "+route.id);
    bool connected=true;for(int i=1;i<route.points.Length;i++)
    {
     var a=ground(route.points[i-1].x,route.points[i-1].y).point;var b=ground(route.points[i].x,route.points[i].y).point;
     var path=new NavMeshPath();connected&=NavMesh.SamplePosition(a,out var na,1.5f,NavMesh.AllAreas)&&NavMesh.SamplePosition(b,out var nb,1.5f,NavMesh.AllAreas)&&NavMesh.CalculatePath(na.position,nb.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;
    }
    C(connected,"current navigation remains connected "+route.id);
   }
   C(s.Content.SaveSlot=="world-demo-compact-cave-v4","candidate save slot unchanged");
   return File.ReadAllText(BranchOutput259+"/audit.txt");
  }
  public static string BranchRuntime259(string command)
  {
   var s=JourneySession258();var ui=PlaytestUiRoot.Instance;ui.CloseMenu();ui.Gate.ReleaseImmediately();
   void C(bool ok,string label)=>BranchCheck259(ok,label,"runtime");
   string save=Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+s.TestSaveSuffix+".json");
   if(command=="walk-setup")
   {
    var plan=JsonUtility.FromJson<ProgressionPlan251>(File.ReadAllText(BranchOutput259+"/route-plan.json"));var p=plan.routes[0].points[0];
    var foot=FinalSurface(SceneManager.GetActiveScene())(p.x,p.y).point;
    C(NavMesh.SamplePosition(foot,out var nav,1.5f,NavMesh.AllAreas),"branch input-walk fixture start on navigation");
    s.Teleport(nav.position,0);return "Fixture start positioned; subsequent walk must use normal input";
   }
   if(command=="evidence")
   {
    C(!s.Progress.campaign.Facts.Contains(HerbFact259)&&!s.Progress.ledger.completed.Contains("herbalist"),"fresh evidence before herbalist contact");
    int currency=s.Progress.ledger.currency;C(ApproachVillage(HerbTrace259),"evidence can be approached physically");
    using(var locked=new FileStream(save+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None))
    {C(!s.Interact(HerbTrace259)&&!s.Progress.campaign.Facts.Contains(HerbFact259)&&s.Progress.ledger.currency==currency,"failed save does not grant discovery or change currency");}
    ui.CloseMenu();ui.Gate.ReleaseImmediately();C(s.Interact(HerbTrace259)&&s.Progress.campaign.Facts.Contains(HerbFact259),"discovery before acceptance persists on retry");
    var disk=JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(save));
    C(disk.campaign.Facts.Contains(HerbFact259)&&disk.campaign.Completed.Contains(HerbTrace259),"fact and completed evidence share one persisted snapshot");
    ui.CloseMenu();ui.Gate.ReleaseImmediately();s.Interact(HerbTrace259);
    C(s.Progress.campaign.Facts.Count(f=>f==HerbFact259)==1&&s.Progress.ledger.currency==currency,"repeated evidence neither duplicates nor pays currency");
    C(ApproachVillage("herbalist"),"late herbalist approach");s.Interact("herbalist");
    string actual=s.Content.Campaign.DialogueFor("herbalist",s.Progress.campaign,"");
    C(actual.Contains("뿌리만 검었다고"),"late testimony uses prior evidence");
    ui.CloseMenu();ui.Gate.ReleaseImmediately();C(ApproachVillage("village_rest")&&s.Interact("village_rest"),"actual door rest after discovery");
    C(s.Progress.campaign.Facts.Contains(HerbFact259),"rest preserves permanent branch fact");
    s.Walker.Body.GetComponent<PlayerVitals>().ApplyFatalFall();return "Fatal fall queued for persistence check";
   }
   if(command=="death"||command=="reload")
   {
    C(s.Walker.Body.GetComponent<PlayerVitals>().Hp01>0,"living player after "+command);
    C(s.Progress.campaign.Facts.Contains(HerbFact259)&&s.Progress.campaign.Completed.Contains(HerbTrace259),"branch fact retained after "+command);
    C(s.Content.Campaign.DialogueFor("herbalist",s.Progress.campaign,"").Contains("뿌리만 검었다고"),"late testimony retained after "+command);
    return "Branch persistence checked";
   }
   throw new ArgumentException(command);
  }
 }
}
