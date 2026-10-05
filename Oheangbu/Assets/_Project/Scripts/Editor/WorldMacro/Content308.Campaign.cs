using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
 // campaign-apply (SPEC-CONTENT-PACING-308 §1 순서 강제, §6 정담 증언 순서, §7 낡은 데이터 정리) [TEST text, 서사 통독 대상]
 public static partial class Content308
 {
  const string JeongdamCombined308="그 도끼 자국을 봤다지. 숲 소리도 끊겼더군. 돌아오지 않은 벌목꾼들이 아직 마음에 걸리네.";
  // Objective rewrites: "[F]" / "[G]" / "관청" leave the text (the explicit mode shows none of it today; the guidance pass will)
  static readonly (string stage,string objective)[] Objectives308=
  {
   ("commission","폐광 폭파 조사 의뢰를 받았다"),
   ("mine_evidence","폐광의 폭파 흔적을 조사한다"),
   ("office_report","폐광 조사 결과를 보고한다"),
   ("inn_rest","금표 주막에서 쉰다"),
   ("relay","금표 주막 남쪽 역참에서 정담을 만난다"),
   ("cargo_contract","마을 객주 분소에서 왕소의 운송 의뢰를 확인한다"),
   ("logging","주막 북쪽 숲길을 따라 벌목장으로 가서 위협을 정리하고 조사한다"),
   ("guk_return","벌목장 쉼터 옆 높은 석대에 국으로 올라 보따리를 살핀다"),
  };
  // stage -> the content point (or encounter) its Destination sits on (AC-C10: XZ <= 5 m, |dy| <= 2 m). ending: the gate, no point:
  // its own XZ is kept and only y follows the general's feet.
  static readonly (string stage,string point,string encounter)[] Destinations308=
  {
   ("commission","mine_inquiry",null),("mine_evidence","mine_inquiry",null),("inn_rest","geumpyo_inn",null),("relay","jeongdam_j1",null),
   ("cargo_contract","wangso_w1",null),("logging","logging_inquiry",null),("deep_forest","metal_growth_lesson",null),("cheongryong",null,"cheongryong"),
   ("guk_return","guk_high_reward",null),("escort","escort_start",null),("checkpoint_one","checkpoint_1",null),("checkpoint_two","checkpoint_2",null),
   ("delivery","cargo_delivery",null),("south_gate",null,"south_gate_general"),("ending",null,"south_gate_general"),
  };

  static string CampaignApply308()
  {
   var profile=Campaign308();
   var main=Load308<WorldMacroPlaytestSO>(Targets308[MainScene].content);
   var notes=new List<string>();
   var ledger=ReadLedger308("campaign308")??new Ledger308{kind="campaign",created=DateTime.UtcNow.ToString("O")};
   var w=Edit308(profile,BackupDir308(),ledger,()=>EditCampaign308(profile,main,notes));
   if(w!=null)WriteLedger308("campaign308",ledger);
   return Report308("campaign-apply "+UnityEditor.AssetDatabase.GetAssetPath(profile),new List<Write308>{w},notes,"campaign308");
  }

  static List<string> EditCampaign308(DemoCampaignProfile profile,WorldMacroPlaytestSO main,List<string> notes)
  {
   var ch=new List<string>();
   // D308-16: the relayout moves places and routes only. With relayout data present the stage order, prerequisites, Optional
   // flags, rewards and the other gate fields must come out of this edit exactly as they went in (CampaignSig308).
   var relayout=Relayout308();string sigBefore=CampaignSig308(profile);
   // D308-16c: the signature this edit must end with = sigBefore, or sigBefore with exactly the declared gate rows (Content308.GateDelta.cs)
   string sigExpected=GateExpectedSig308(profile);
   DemoCampaignProfile.Stage S(string id)=>profile.FindStage(id)??throw new Refuse308("campaign stage "+id+" missing");
   if(!profile.UseExplicitPrerequisites)throw new Refuse308("campaign is not in explicit-prerequisite mode; the #308 order gate assumes it");
   // §1 C1: 청룡 only after 정담 J1 (relay) and 왕소 W1 (cargo_contract)
   Set308(ch,"cheongryong.PrerequisiteIds",ref S("cheongryong").PrerequisiteIds,CheongryongPrereqs308());
   // §7 office_report: unreachable without the office opening; the row stays for save compatibility
   Set308(ch,"office_report.Implemented",ref S("office_report").Implemented,false);
   foreach(var (id,objective) in Objectives308)Set308(ch,id+".Objective",ref S(id).Objective,objective);
   Set308(ch,"commission.DestinationLabel",ref S("commission").DestinationLabel,"폐광");
   // §7 Destinations onto their points (WorldContent_Main: the promoted play target)
   foreach(var (id,pointId,encounterId) in DestinationsNow308())
   {
    var stage=S(id);Vector3 at;
    if(pointId!=null)
    {
     var p=Array.Find(main.Points??Array.Empty<PrologueContentSO.Point>(),x=>x!=null&&x.Id==pointId);
     if(p==null){notes.Add("pending "+id+".Destination: point "+pointId+" not in WorldContent_Main yet (run content-apply:"+MainScene+", then campaign-apply again)");continue;}
     at=p.Position;
    }
    else
    {
     var e=Array.Find(main.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>(),x=>x!=null&&x.Id==encounterId);
     if(e==null){notes.Add("pending "+id+".Destination: encounter "+encounterId+" not in WorldContent_Main");continue;}
     at=id=="ending"?new Vector3(stage.Destination.x,e.Feet.y,stage.Destination.z):e.Feet;
    }
    Set308(ch,id+".Destination",ref stage.Destination,at);
   }
   // §6 정담 증언 순서: [logging + cheongryong] combined, then [cheongryong], then [logging] (DialogueFor returns the first eligible)
   var list=(profile.Testimonies??Array.Empty<DemoCampaignProfile.Testimony>()).ToList();
   int at0=list.FindIndex(t=>t!=null&&t.TriggerId=="jeongdam_j1");
   var jd=list.Where(t=>t!=null&&t.TriggerId=="jeongdam_j1").ToList();
   bool Only(DemoCampaignProfile.Testimony t,params string[] facts)=>(t.RequiredFacts??Array.Empty<string>()).OrderBy(x=>x,StringComparer.Ordinal).SequenceEqual(facts.OrderBy(x=>x,StringComparer.Ordinal));
   var logging=jd.FirstOrDefault(t=>Only(t,"evidence:logging_growth"));
   var cheong=jd.FirstOrDefault(t=>Only(t,"defeated:cheongryong"));
   var both=jd.FirstOrDefault(t=>Only(t,"evidence:logging_growth","defeated:cheongryong"))??
            new DemoCampaignProfile.Testimony{TriggerId="jeongdam_j1",RequiredFacts=new[]{"evidence:logging_growth","defeated:cheongryong"},Text=JeongdamCombined308};
   if(!string.Equals(both.Text,JeongdamCombined308,StringComparison.Ordinal)){ch.Add("jeongdam_j1 combined testimony text set");both.Text=JeongdamCombined308;}
   if(logging==null||cheong==null)notes.Add("WARN jeongdam_j1 testimony "+(logging==null?"[evidence:logging_growth] ":"")+(cheong==null?"[defeated:cheongryong] ":"")+"missing; kept the rest");
   var ordered=new List<DemoCampaignProfile.Testimony>{both};
   if(cheong!=null)ordered.Add(cheong);if(logging!=null)ordered.Add(logging);
   ordered.AddRange(jd.Where(t=>t!=both&&t!=cheong&&t!=logging));
   var next=list.Where(t=>t==null||t.TriggerId!="jeongdam_j1").ToList();
   next.InsertRange(at0<0?next.Count:Mathf.Min(at0,next.Count),ordered);
   string Sig(IEnumerable<DemoCampaignProfile.Testimony> ts)=>string.Join("|",ts.Select(t=>t==null?"null":t.TriggerId+":"+string.Join(",",t.RequiredFacts??Array.Empty<string>())));
   if(Sig(next)!=Sig(list)){ch.Add("testimonies: "+Sig(list)+" -> "+Sig(next));}
   profile.Testimonies=next.ToArray();
   // §7 Act 1 journey without the office
   var act1=Array.Find(profile.Acts??Array.Empty<DemoCampaignProfile.Act>(),a=>a!=null&&a.Id=="act_1");
   if(act1==null)notes.Add("WARN act_1 missing");
   else{Set308(ch,"act_1.Journey",ref act1.Journey,"폐광 조사 → 금표 주막");Set308(ch,"act_1.RewardSummary",ref act1.RewardSummary,"첫 정비 선택");}
   GateDeltaEdit308(ch,profile,notes);   // D308-16c: the declared gate rows, last and alone
   if(!profile.IsValid)throw new Refuse308("the edited campaign would be invalid (DemoCampaignProfile.IsValid false); nothing written");
   string sigAfter=CampaignSig308(profile);
   // the note is relayout-only: without the data file the report is the #308 one, line for line
   if(relayout!=null)notes.Add("gate signature (order, prerequisites, Optional, rewards, facts, triggers of "+(profile.Stages?.Length??0)+" stages) sha "+Short308(BuildingAudit308.ShaText(sigAfter))+(sigAfter==sigBefore?" - unchanged by this edit":sigAfter==sigExpected?" - changed by the declared gate delta only (D308-16c)":" - CHANGED by this edit"));
   if(relayout!=null&&relayout.CampaignGuard&&sigAfter!=sigExpected)
    throw new Refuse308("the campaign gate signature would differ from the expected one ("+SigDiff308(sigExpected,sigAfter)+"): the relayout never touches order / prerequisites / Optional / rewards (D308-16) beyond the rows declared in content308_gate_delta.json (D308-16c). "+
     "Nothing written. A first #308 base apply on a pristine campaign needs campaign.identity_guard false in "+RelayoutFile308);
   return ch;
  }
 }
}
