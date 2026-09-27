using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.Data.Demo;
using Oheangbu.App.World;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static string Checks()
  {
   var source=AssetDatabase.LoadAssetAtPath<DemoCampaignProfile>(Folder+"/Campaign.asset");
   if(source==null)throw new Exception("Author campaign first");
   var profile=Object.Instantiate(source);var checks=new List<string>();
   void Check(bool value,string name){checks.Add((value?"PASS ":"FAIL ")+name);if(!value)throw new Exception(name);}
   DemoCampaignState Fresh()=>new DemoCampaignState{CampaignId=profile.CampaignId,Completed=new List<string>(profile.InitialCompletedIds),Facts=new List<string>{"case:mine_commissioned"}};
   bool Step(ref DemoCampaignState state,string id,out int reward)
   {
    var stage=profile.Stages.Single(s=>s.Id==id);
    bool result=DemoCampaignProgression.TryAdvance(profile,state,stage.Event,stage.TriggerId,out var next,out reward);
    if(result)state=next;return result;
   }
   try
   {
    Check(profile.IsValid,"authored graph valid");
    checks.Add("INFO Scene availability: "+string.Join(", ",profile.Stages.Where(s=>!s.Implemented).Select(s=>s.Id))+" remain disabled; following chain tests enable a detached fixture only.");
    foreach(var stage in profile.Stages)stage.Implemented=true;
    Array.Reverse(profile.Stages);Check(profile.IsValid,"stage storage order is not a dependency");
    var state=Fresh();var before=JsonUtility.ToJson(state);
    Check(!Step(ref state,"office_report",out _),"report without evidence rejected");
    Check(JsonUtility.ToJson(state)==before,"rejected event leaves source unchanged");
    Check(Step(ref state,"logging",out _)&&state.Facts.Contains("evidence:logging_growth"),"logging evidence before any NPC meeting retained");
    Check(profile.DialogueFor("jeongdam_j1",state,"fallback")!="fallback","early evidence changes later Jeongdam testimony");
    Check(!Step(ref state,"cargo_contract",out _),"contract before Jeongdam contact rejected");
    Check(Step(ref state,"cheongryong",out _)&&state.Facts.Contains("ability:guk")&&!state.Completed.Contains("relay"),"early boss event accepted without reports or lessons");
    Check(!Step(ref state,"escort",out _),"boss alone cannot start escort without contract");
    Check(Step(ref state,"relay",out _)&&Step(ref state,"cargo_contract",out _),"late NPC meeting enables contract");
    Check(!Step(ref state,"checkpoint_one",out _),"contract is not proof of departure");
    Check(Step(ref state,"escort",out _),"departure requires both contract and boss unlock");
    Check(!Step(ref state,"delivery",out _),"delivery cannot skip inspections");
    Check(Step(ref state,"checkpoint_one",out _)&&Step(ref state,"checkpoint_two",out _)&&Step(ref state,"delivery",out _),"explicit inspection and delivery chain");
    Check(Step(ref state,"south_gate",out _)&&Step(ref state,"ending",out _),"gate transaction no longer depends on adjacent array entries");
    Check(!state.Completed.Contains("office_report")&&!state.Completed.Contains("inn_rest"),"optional reports and rests do not block main gate");
    Check(Step(ref state,"mine_evidence",out _)&&!state.Completed.Contains("office_report"),"new evidence never automatically replays an old report interaction");
    Check(Step(ref state,"office_report",out int reward)&&reward>0,"late report has its own payable live event");
    Check(!Step(ref state,"office_report",out int duplicate)&&duplicate==0,"repeat report cannot repay");
    var detached=state.Copy();detached.Facts.Add("test:detached");Check(!state.Facts.Contains("test:detached"),"proposal facts detached from committed facts");
    var loaded=JsonUtility.FromJson<DemoCampaignState>(JsonUtility.ToJson(state));loaded.Normalize();Check(loaded.IsValid()&&loaded.Facts.Contains("evidence:logging_growth")&&loaded.Completed.Contains("office_report"),"facts and payment proof survive serialization");
    string diskPath=Path.GetFullPath(Output+"/checks_"+Guid.NewGuid().ToString("N")+".json");
    var saved=WorldMacroProgress.CreateNew("compact-rebuild-v2",Vector3.zero,0);saved.campaign=loaded.Copy();saved.ledger.currency=100;
    var store=new AtomicJsonStore<WorldMacroProgress>(diskPath,WorldMacroProgress.Valid);store.Save(saved);
    var candidate=JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(saved));candidate.campaign.Facts.Add("test:pending_discovery");candidate.ledger.currency+=30;
    bool failed=false;
    using(var locked=new FileStream(diskPath+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None))
        try{store.Save(candidate);}catch(IOException){failed=true;}
    var afterFailure=store.Load();
    Check(failed&&afterFailure.ledger.currency==100&&!afterFailure.campaign.Facts.Contains("test:pending_discovery"),"real filesystem write failure preserves committed facts and currency");
    Check(saved.ledger.currency==100&&!saved.campaign.Facts.Contains("test:pending_discovery"),"failed detached proposal does not mutate source");
    store.Save(candidate);var reopened=new AtomicJsonStore<WorldMacroProgress>(diskPath,WorldMacroProgress.Valid).Load();
    Check(reopened.ledger.currency==130&&reopened.campaign.Facts.Contains("test:pending_discovery"),"retry publishes facts and currency together across store reopen");
    var existingFacts=loaded.Facts.ToArray();loaded.EncounterEvidence.Clear();Check(existingFacts.SequenceEqual(loaded.Facts),"discovery facts independent of encounter list");
    var relay=profile.Stages.Single(s=>s.Id=="relay");relay.PrerequisiteIds=new[]{"relay"};Check(!profile.IsValid,"self cycle rejected");relay.PrerequisiteIds=Array.Empty<string>();
    var contract=profile.Stages.Single(s=>s.Id=="cargo_contract");relay.PrerequisiteIds=new[]{"cargo_contract"};contract.PrerequisiteIds=new[]{"relay"};Check(!profile.IsValid,"multi-node cycle rejected");relay.PrerequisiteIds=Array.Empty<string>();contract.PrerequisiteIds=Array.Empty<string>();
    relay.PrerequisiteIds=new[]{"missing"};Check(!profile.IsValid,"unknown dependency rejected");relay.PrerequisiteIds=Array.Empty<string>();
    var ambiguous=JsonUtility.FromJson<DemoCampaignProfile.Stage>(JsonUtility.ToJson(profile.Stages.Single(s=>s.Id=="logging")));ambiguous.Id="ambiguous_logging";profile.Stages=profile.Stages.Concat(new[]{ambiguous}).ToArray();state=Fresh();Check(!Step(ref state,"logging",out _),"ambiguous event cannot select a stage by array order");
    var legacy=AssetDatabase.LoadAssetAtPath<DemoCampaignProfile>("Assets/_Project/Art/World/WorldCompact/ActsTerrain/Campaign_TEST.asset");
    Check(legacy!=null&&!legacy.UseExplicitPrerequisites&&legacy.IsValid,"legacy source profile retained");
    var oldState=new DemoCampaignState{CampaignId=legacy.CampaignId};var oldEvidence=legacy.Stages.Single(s=>s.Id=="mine_evidence");Check(!DemoCampaignProgression.TryAdvance(legacy,oldState,oldEvidence.Event,oldEvidence.TriggerId,out _,out _),"legacy sequential behavior retained");
   }
   catch(Exception e){checks.Add("ERROR "+e);}
   finally{Object.DestroyImmediate(profile);}
   checks.Add("Data/event checks only. Real enemy witnesses, convoy presence, disk failures and manual exploration require separate runtime verification.");
   string report=string.Join("\n",checks);File.WriteAllText(Output+"/campaign_checks.txt",report);return report;
  }
 }
}
