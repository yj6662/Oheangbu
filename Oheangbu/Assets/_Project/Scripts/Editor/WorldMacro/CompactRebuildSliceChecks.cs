using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
 [InitializeOnLoad]
 public static class CompactRebuildSliceChecks
 {
  const string Key="CompactSlice.CheckSuffix", Phase="CompactSlice.CheckPhase", PriorUi="CompactSlice.PriorUiSuffix";
  const string Report="../Art/World/Compact/Rebuild/slice_runtime_checks.txt";
  [Serializable] sealed class Receipt {public string scene;}
  static string PathToScene=>JsonUtility.FromJson<Receipt>(File.ReadAllText("../Art/World/Compact/Rebuild/migration_slice.json")).scene;
  static WorldMacroPlaytestSession Session()=>SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
  static CompactRebuildSliceChecks(){EditorApplication.playModeStateChanged+=Changed;}
  static void Changed(PlayModeStateChange state)
  {
   if(state!=PlayModeStateChange.EnteredEditMode||string.IsNullOrEmpty(SessionState.GetString(Key,"")))return;
   if(SceneManager.GetActiveScene().path==PathToScene)Session().TestSaveSuffix="";
   if(SessionState.GetString(Phase,"")=="restart")SessionState.SetString(Phase,"awaiting-restart");
   else if(SessionState.GetString(Phase,"")=="finish"){
    SessionState.SetString("PlaytestUiReviewSuffix",SessionState.GetString(PriorUi,""));
    SessionState.EraseString(Key);SessionState.EraseString(Phase);SessionState.EraseString(PriorUi);
    EditorSceneManager.OpenScene(CompactRebuildAuthoring.Scene);
   }
  }
  public static string Run(string command)
  {
   if(command=="slice-cleanup")
   {
    if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop diagnostic Play before cleanup");
    if(SceneManager.GetActiveScene().path==PathToScene)Session().TestSaveSuffix="";
    string prior=SessionState.GetString(PriorUi,"");
    // Recover an earlier run interrupted before its cleanup callback completed.
    if(prior.StartsWith("_runtime_")||prior.StartsWith("_compact_slice_"))prior="";
    SessionState.SetString("PlaytestUiReviewSuffix",prior);
    SessionState.EraseString(Key);SessionState.EraseString(Phase);SessionState.EraseString(PriorUi);
    SessionState.EraseString("CompactSlice.DefeatedId");SessionState.EraseInt("CompactSlice.ExpectedCurrency");
    EditorSceneManager.OpenScene(CompactRebuildAuthoring.Scene);return "Diagnostic overrides cleared; canonical Compact restored";
   }
   if(command=="slice-state")return "scene="+SceneManager.GetActiveScene().path+"; playing="+EditorApplication.isPlaying+"; phase="+SessionState.GetString(Phase,"")+"; suffix="+SessionState.GetString(Key,"");
   if(command=="slice-resume")
   {
    if(EditorApplication.isPlayingOrWillChangePlaymode||string.IsNullOrEmpty(SessionState.GetString(Key,"")))throw new Exception("Stopped diagnostic required");
    if(SceneManager.GetActiveScene().path!=PathToScene)EditorSceneManager.OpenScene(PathToScene);
    Session().TestSaveSuffix=SessionState.GetString(Key,"");SessionState.SetString(Phase,"reloaded");EditorApplication.EnterPlaymode();return "Resuming same diagnostic save";
   }
   if(command=="slice-start")
   {
    if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
    var active=SceneManager.GetActiveScene();if(active.isDirty)throw new Exception("Save or recover current scene before diagnostics");
    EditorSceneManager.OpenScene(PathToScene);
    string suffix="_compact_slice_"+Guid.NewGuid().ToString("N");SessionState.SetString(Key,suffix);SessionState.SetString(Phase,"initial");
    SessionState.SetString(PriorUi,SessionState.GetString("PlaytestUiReviewSuffix",""));SessionState.SetString("PlaytestUiReviewSuffix",suffix);
    Session().TestSaveSuffix=suffix;File.WriteAllText(Report,"Controlled runtime integration checks. Teleport used for interaction fixtures; navigation walking is a separate report.\n");
    EditorApplication.EnterPlaymode();return "Entering wired candidate with private diagnostic slot "+suffix;
   }
   if(string.IsNullOrEmpty(SessionState.GetString(Key,""))||!EditorApplication.isPlaying||SceneManager.GetActiveScene().path!=PathToScene)throw new Exception("No candidate diagnostic Play run");
   if(command=="slice-stop"){SessionState.SetString(Phase,"finish");EditorApplication.ExitPlaymode();return "Restoring canonical scene after diagnostics";}
   if(command=="slice-restart"){SessionState.SetString(Phase,"restart");EditorApplication.ExitPlaymode();return "Restarting candidate with same isolated save";}
   var s=Session();var lines=new List<string>();
   void Check(bool pass,string label){lines.Add((pass?"PASS ":"FAIL ")+label);}
   var ui=UnityEngine.Object.FindFirstObjectByType<PlaytestUiRoot>();
   if(command=="slice-view"){
    ui.CloseMenu();s.Teleport(s.Content.InnCheckpointFeet,0);return "Inn review pose set; wait one rendered frame before capture";
   }
   if(command=="slice-capture"){
    string path=System.IO.Path.GetFullPath("../Art/World/Compact/Rebuild/runtime_inn.png");ScreenCapture.CaptureScreenshot(path);return "Capture queued: "+path;
   }
   Check(s.Progress!=null&&!s.SaveBlocked&&string.IsNullOrEmpty(s.SaveError),"session loaded valid progress without save error");
   if(s.Progress==null)throw new Exception("Session not initialized");
   Check(ui!=null&&ui.Content==s.Content&&ui.ActiveSlotName==s.Content.SaveSlot+s.TestSaveSuffix,"UI and session share candidate content and diagnostic save");
   Check(ui!=null&&ui.MapData.IsUsable&&ui.MapData.BoundsMin==Vector2.zero&&ui.MapData.BoundsMax==new Vector2(4000,6000),"private map uses rebuilt world coordinates");
   if(command=="slice-health")
   {
    Check(s.Actors.Length==s.Content.Encounters.Length&&s.Actors.All(a=>a!=null&&a.Player==s.Walker.Body.transform),"all authored actors target candidate player");
    Check(s.Actors.All(a=>a.GetComponent<NavMeshAgent>().isOnNavMesh),"mine actors bound to baked candidate navigation");
    Check(Vector3.Distance(s.Walker.Body.transform.position,s.Content.StartFeet)<10,"fresh player starts at relocated mine");
    Check(s.Progress.campaign.Completed.Contains("commission")&&string.IsNullOrEmpty(s.DemoObjective),"commission active without persistent quest objective");
   }
   else if(command=="slice-defeat")
   {
    // A resumed save may be kilometres from the mine. Activate the witnessed
    // ordinary encounter before exercising its death subscription.
    ui.CloseMenu();ui.Gate.ReleaseImmediately();
    var actor=s.Actors.First(a=>s.Content.Encounters.Any(e=>e.Id==a.Id&&e.RespawnOnRest)&&a.GetComponent<Oheangbu.Combat.EnemyVitals>().IsAlive&&!s.Progress.defeated.Contains(a.Id));
    s.Teleport(s.Content.Encounters.Single(e=>e.Id==actor.Id).Feet+Vector3.right*3,0);s.CombatActive=true;s.Cull();
    var vitals=actor.GetComponent<Oheangbu.Combat.EnemyVitals>();string id=actor.Id;
    Check(actor.isActiveAndEnabled&&vitals.isActiveAndEnabled,"ordinary encounter active before death witness");
    int before=s.Progress.ledger.currency;s.EnemyDefeated(id);
    Check(vitals.IsAlive&&!s.Progress.defeated.Contains(id)&&s.Progress.ledger.currency==before,"string-only defeat without dead actor rejected");
    string path=System.IO.Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+s.TestSaveSuffix+".json.tmp");
    using(var locked=new FileStream(path,FileMode.Create,FileAccess.ReadWrite,FileShare.None))
    {
     vitals.TakeDamage(float.MaxValue);
     Check(!vitals.IsAlive,"registered enemy supplies actual death witness");
     Check(s.Progress.ledger.currency==before&&!s.Progress.campaign.Facts.Contains("defeated:"+id),"failed write publishes neither defeat reward nor fact");
     Check(!s.SaveNow(out _),"pending defeat prevents saving a partial snapshot");
    }
    Check(s.SaveNow(out var error),"failed defeat save retries after lock release: "+error);
    Check(s.Progress.ledger.currency==before+s.Content.TestRules.EnemyReward&&s.Progress.campaign.Facts.Contains("defeated:"+id),"retry commits reward and permanent defeat fact together");
    s.EnemyDefeated(id);Check(s.Progress.ledger.currency==before+s.Content.TestRules.EnemyReward,"duplicate death event cannot repay");
    SessionState.SetString("CompactSlice.DefeatedId",id);
   }
   else if(command.StartsWith("slice-point:"))
   {
    // Isolate interaction/save behavior from attacks. These fixtures do not assert combat victory.
    foreach(var actor in s.Actors)actor.gameObject.SetActive(false);
    bool InteractAt(string id)
    {
     ui.CloseMenu();var point=s.Content.Points.Single(p=>p.Id==id);bool found=false;
     for(int ring=0;ring<3&&!found;ring++)for(int i=0;i<16&&!found;i++)
     {
      float angle=i*Mathf.PI/8, distance=Mathf.Max(.3f,point.Radius*(.35f+ring*.2f));
      var probe=point.Position+new Vector3(Mathf.Cos(angle)*distance,1,Mathf.Sin(angle)*distance);
      if(!NavMesh.SamplePosition(probe,out var hit,2,NavMesh.AllAreas))continue;
      s.Teleport(hit.position,0);Physics.SyncTransforms();found=s.CanInteract(id);
     }
     Check(found,"reachable line of sight for "+id);if(!found)return false;
     bool result=s.Interact(id);ui.CloseMenu();return result;
    }
    string id=command.Substring(12);int before=s.Progress.ledger.currency;
    bool already=s.Progress.ledger.completed.Contains(id);
    Check(InteractAt(id),"actual interaction commits: "+id);
    if(already)Check(s.Progress.ledger.currency==before,"repeat interaction does not duplicate currency");
    if(id=="mine_inquiry")Check(s.Progress.campaign.Facts.Contains("evidence:mine_blast"),"permanent mine evidence fact");
    if(id=="geumpyo_inn"){
     Check(s.Progress.ledger.checkpoint==id&&s.Progress.campaign.Facts.Contains("evidence:mine_blast"),"rest changes checkpoint and preserves facts");
     string defeated=SessionState.GetString("CompactSlice.DefeatedId","");
     if(!string.IsNullOrEmpty(defeated))Check(s.Progress.campaign.Facts.Contains("defeated:"+defeated)&&!s.Progress.defeated.Contains(defeated),"rest respawns encounter while preserving its discovery fact");
     SessionState.SetInt("CompactSlice.ExpectedCurrency",s.Progress.ledger.currency);
    }
    Check(s.SaveNow(out var error),"snapshot saved: "+error);
   }
   else if(command=="slice-death")
   {
    ui.CloseMenu();foreach(var actor in s.Actors)actor.gameObject.SetActive(false);
    var facts=s.Progress.campaign.Facts.ToArray();
    s.Walker.Body.GetComponent<Oheangbu.Combat.PlayerVitals>().ApplyFatalFall();
    Check(facts.All(f=>s.Progress.campaign.Facts.Contains(f)),"player death retains every discovered fact");
    Check(s.Progress.ledger.checkpoint=="geumpyo_inn","player death retains inn checkpoint");
    Check(Vector3.Distance(s.Walker.Body.transform.position,s.Content.InnCheckpointFeet)<10,"player respawns at relocated inn");
    Check(s.Progress.ledger.dropCurrency>0,"death leaves recoverable currency at last safe position");
   }
   else if(command=="slice-reloaded")
   {
    Check(SessionState.GetString(Phase,"")=="reloaded","Editor Play actually restarted");
    Check(s.Progress.campaign.Facts.Contains("evidence:mine_blast")&&s.Progress.ledger.completed.Contains("worker_satchel"),"evidence and satchel survived disk reload");
    Check(s.Progress.ledger.checkpoint=="geumpyo_inn","inn checkpoint survived disk reload");
    Check(s.Progress.ledger.currency==SessionState.GetInt("CompactSlice.ExpectedCurrency",-1),"currency unchanged after reload");
    string defeated=SessionState.GetString("CompactSlice.DefeatedId","");if(!string.IsNullOrEmpty(defeated))Check(s.Progress.campaign.Facts.Contains("defeated:"+defeated),"permanent encounter fact survived rest and disk reload");
    Check(Vector3.Distance(s.Walker.Body.transform.position,s.Content.InnCheckpointFeet)<20,"saved player position restored near inn");
   }
   else throw new ArgumentException(command);
   string result=command+"\n"+string.Join("\n",lines);File.AppendAllText(Report,result+"\n");return result;
  }
 }
}
