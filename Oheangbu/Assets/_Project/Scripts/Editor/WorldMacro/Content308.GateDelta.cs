using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.Demo;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // D308-16c (2026-10-05): the ONE intended change of the campaign gate, declared in data.
 //   Art/World/Compact/Rebuild/CliffBoundary308/content308_gate_delta.json   (stage copy: Tools/Unity/Stage308_relayout_ext/Data/)
 // The relayout rule stays as it was: campaign-apply refuses when the gate signature (order, prerequisites, Optional, rewards, facts,
 // triggers) comes out different from what is expected. Without the file "expected" = the signature before the edit (D308-16). With
 // the file "expected" = that signature with exactly the declared rows applied - nothing else may move. A row names the stage, the
 // field (PrerequisiteIds | Optional), the value it must have before (from) and the value it gets (to); a stage that holds neither
 // value refuses (the delta was declared against another campaign).
 // Hooks in the base partials (each one line; see Tools/Unity/Stage308_relayout_ext/amneung308_merge.py):
 //   Content308.Campaign.cs    sigExpected / CheongryongPrereqs308() / GateDeltaEdit308 / the guard compares with sigExpected
 //   Content308.Checks.cs      AC-C1 reads CheongryongPrereqs308(); GateChecks308 before the "accepted" test
 //   Content308.Relayout.cs    GroupOfRow308 (a stage row that changes only the declared fields carries the gate group; revert by field: RowStands308 / RowsLink308 / GatePut308), GroupsInData308 (a group that is on cannot be reverted)
 //   Content308.SceneChecks.cs AC-C5 = AmneungGate308 (the 국 stone of Amneung308 instead of a 12-22 m GukLiftSite)
 // Turning the gate back: set "enabled": false in the file, then Content308 "campaign-revert --group <group>".
 public static partial class Content308
 {
  static string GateFile308=>Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","CliffBoundary308","content308_gate_delta.json");
  static string AmneungFile308=>Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","CliffBoundary308","amneung308.json");
  static readonly string[] CheongryongBase308={"relay","cargo_contract"};   // SPEC-CONTENT-PACING-308 §1 C1 (D308-2)

  sealed class GateRow308{public string Stage="",Field="";public string[] FromList,ToList;public bool FromBool,ToBool;}
  sealed class GateDelta308
  {
   public string Sha="",Decision="",Group="",LessonStage="",LessonTrigger="",LessonEvent="",LessonEncounter="";
   public readonly List<GateRow308> Rows=new List<GateRow308>();
  }

  // null = no file, "enabled": false, or the group is listed in groups_off of the relayout data. Read on every call (domain reload is off).
  static GateDelta308 GateDeltaNow308()
  {
   string f=GateFile308;if(!File.Exists(f))return null;
   JObject j;
   try{j=JObject.Parse(File.ReadAllText(f));}
   catch(Exception e){throw new Refuse308("gate delta data does not parse: "+f+" ("+e.Message+")");}
   if(!Flag308(j,"enabled"))return null;
   var d=new GateDelta308{Sha=Harness303.Sha(f),Decision=Str308(j,"decision"),Group=Str308(j,"group")};
   if(d.Group.Length==0)throw new Refuse308("gate delta: 'group' is empty in "+f);
   var relayout=Relayout308();if(relayout!=null&&relayout.Off.Contains(d.Group))return null;
   var lesson=Req308(j,"lesson");
   d.LessonStage=Str308(lesson,"stage");d.LessonTrigger=Str308(lesson,"trigger");d.LessonEvent=Str308(lesson,"event");d.LessonEncounter=Opt308(lesson,"encounter")??"";
   foreach(var r in Arr308(j,"rows"))
   {
    var row=new GateRow308{Stage=Str308(r,"stage"),Field=Str308(r,"field")};
    if(row.Field=="PrerequisiteIds")
    {
     row.FromList=Arr308(r,"from").Select(t=>t.Value<string>()).ToArray();row.ToList=Arr308(r,"to").Select(t=>t.Value<string>()).ToArray();
     if(row.ToList.Length==0)throw new Refuse308("gate delta row "+row.Stage+".PrerequisiteIds: 'to' is empty");
    }
    else if(row.Field=="Optional"){row.FromBool=Req308(r,"from").Value<bool>();row.ToBool=Req308(r,"to").Value<bool>();}
    else throw new Refuse308("gate delta row "+row.Stage+": field '"+row.Field+"' is not one of PrerequisiteIds | Optional");
    if(d.Rows.Any(x=>x.Stage==row.Stage&&x.Field==row.Field))throw new Refuse308("gate delta: two rows for "+row.Stage+"."+row.Field);
    d.Rows.Add(row);
   }
   if(d.Rows.Count==0)throw new Refuse308("gate delta: no rows in "+f+" (set enabled false instead)");
   return d;
  }

  // the prerequisites campaign-apply writes for 청룡: the #308 constant, or the declared row
  static string[] CheongryongPrereqs308()
  {
   var d=GateDeltaNow308();var row=d?.Rows.FirstOrDefault(r=>r.Stage=="cheongryong"&&r.Field=="PrerequisiteIds");
   return row!=null?row.ToList:CheongryongBase308;
  }

  // applies the rows to `p`. write=false only answers whether anything would change. A value that is neither from nor to refuses.
  static bool GateRows308(GateDelta308 d,DemoCampaignProfile p,List<string> ch,bool write)
  {
   bool any=false;
   var lesson=p.FindStage(d.LessonStage);
   if(lesson==null)throw new Refuse308("gate delta ("+d.Decision+"): lesson stage '"+d.LessonStage+"' is not in the campaign");
   if(lesson.TriggerId!=d.LessonTrigger||lesson.Event.ToString()!=d.LessonEvent||!lesson.Implemented)
    throw new Refuse308("gate delta ("+d.Decision+"): stage '"+d.LessonStage+"' is not the metal lesson the data names (trigger '"+lesson.TriggerId+"', event "+lesson.Event+", implemented "+lesson.Implemented+
     "; expected trigger '"+d.LessonTrigger+"', event "+d.LessonEvent+", implemented)");
   foreach(var r in d.Rows)
   {
    var s=p.FindStage(r.Stage);if(s==null)throw new Refuse308("gate delta ("+d.Decision+"): stage '"+r.Stage+"' is not in the campaign");
    if(r.Field=="PrerequisiteIds")
    {
     var now=s.PrerequisiteIds??Array.Empty<string>();
     if(now.SequenceEqual(r.ToList))continue;
     if(!now.SequenceEqual(r.FromList))throw new Refuse308("gate delta ("+d.Decision+"): "+r.Stage+".PrerequisiteIds is ["+string.Join(",",now)+"], neither the declared 'from' ["+string.Join(",",r.FromList)+"] nor 'to' ["+string.Join(",",r.ToList)+"]");
     any=true;if(write)Set308(ch,r.Stage+".PrerequisiteIds ("+d.Decision+")",ref s.PrerequisiteIds,r.ToList);
    }
    else
    {
     if(s.Optional==r.ToBool)continue;
     if(s.Optional!=r.FromBool)throw new Refuse308("gate delta ("+d.Decision+"): "+r.Stage+".Optional is "+s.Optional+", not the declared 'from' "+r.FromBool);
     any=true;if(write)Set308(ch,r.Stage+".Optional ("+d.Decision+")",ref s.Optional,r.ToBool);
    }
   }
   return any;
  }

  // the gate signature campaign-apply must end with: the profile as it stands, with exactly the declared rows applied (a clone is edited)
  static string GateExpectedSig308(DemoCampaignProfile profile)
  {
   var d=GateDeltaNow308();if(d==null)return CampaignSig308(profile);
   var clone=Object.Instantiate(profile);
   try{GateRows308(d,clone,new List<string>(),true);return CampaignSig308(clone);}
   finally{Object.DestroyImmediate(clone);}
  }

  // last step of EditCampaign308. The gate rows are written by a campaign-apply of their own: with other changes pending in the same
  // edit the stage rows of the ledger would mix two groups, and "campaign-revert --group" could no longer undo the gate alone.
  static void GateDeltaEdit308(List<string> ch,DemoCampaignProfile profile,List<string> notes)
  {
   var d=GateDeltaNow308();
   if(d==null){notes.Add("gate delta: none ("+(File.Exists(GateFile308)?"off in "+GateFile308:"no "+GateFile308)+") - 청룡 prerequisites are the #308 constant ["+string.Join(",",CheongryongBase308)+"]");return;}
   // the 청룡 row is written by the base line (CheongryongPrereqs308); what it did is found in `ch` under the same key
   bool cheongChanged=ch.Any(c=>c.StartsWith("cheongryong.PrerequisiteIds",StringComparison.Ordinal));
   int others=ch.Count(c=>!c.StartsWith("cheongryong.PrerequisiteIds",StringComparison.Ordinal));
   bool would=GateRows308(d,profile,null,false)||cheongChanged;
   if(would&&others>0)
    throw new Refuse308("gate delta ("+d.Decision+", group "+d.Group+") would be written together with "+others+" other campaign change(s) ("+string.Join("; ",ch.Where(c=>!c.StartsWith("cheongryong.PrerequisiteIds",StringComparison.Ordinal)).Take(3))+
     "): set \"enabled\": false in "+GateFile308+", run campaign-apply for those, set it true again and run campaign-apply once more (the gate rows then stand alone in the ledger). Nothing written.");
   GateRows308(d,profile,ch,true);
   notes.Add("gate delta "+d.Decision+" (group "+d.Group+", data sha "+Short308(d.Sha)+"): "+string.Join("; ",d.Rows.Select(r=>r.Stage+"."+r.Field+" -> "+(r.Field=="Optional"?r.ToBool.ToString():"["+string.Join(",",r.ToList)+"]")))+
    (would?" - WRITTEN by this edit":" - already in place"));
  }

  // ---------- ledger rows of the gate (review 2, finding 1) ----------
  // A stage row belongs to the gate group only when the declared fields are the ONLY thing that differs between its before and
  // after form. A later campaign-apply that writes another field of cheongryong / deep_forest (a Destination that follows a moved
  // encounter) gets the base group "" and is not pulled into "campaign-revert --group <gate group>".
  static readonly string[] GateFields308={"PrerequisiteIds","Optional"};
  static bool GateOnly308(string before,string after)
  {
   if(string.IsNullOrEmpty(before)||string.IsNullOrEmpty(after))return false;
   JObject b,a;try{b=JObject.Parse(before);a=JObject.Parse(after);}catch(Exception){return false;}
   bool gate=false;
   foreach(var f in GateFields308){if(!JToken.DeepEquals(b[f],a[f]))gate=true;b.Remove(f);a.Remove(f);}
   return gate&&JToken.DeepEquals(b,a);
  }
  static bool GateFieldsEqual308(string x,string y)
  {
   if(string.IsNullOrEmpty(x)||string.IsNullOrEmpty(y))return false;
   JObject a,b;try{a=JObject.Parse(x);b=JObject.Parse(y);}catch(Exception){return false;}
   return GateFields308.All(f=>JToken.DeepEquals(a[f],b[f]));
  }
  static string GateGroupOfStage308(string stageId,string before,string after)
  {
   GateDelta308 d;try{d=GateDeltaNow308();}catch(Refuse308){return "";}
   return d!=null&&d.Rows.Any(r=>r.Stage==stageId)&&GateOnly308(before,after)?d.Group:"";
  }
  // The gate rows are reverted by FIELD: the two declared fields go back to their before-values and every other field of the stage
  // keeps what a later write of another group gave it. So the checks of the group revert look at those fields only for such a row.
  static bool IsGateLedgerRow308(Row308 r)=>r!=null&&r.kind=="stage"&&GateOnly308(r.before,r.after);
  static bool RowStands308(Object asset,Row308 last)
  {
   string cur=Current308(asset,last.kind,last.id);
   return IsGateLedgerRow308(last)?GateFieldsEqual308(cur,last.after):cur==last.after;
  }
  static bool RowsLink308(Row308 x,Row308 y)=>IsGateLedgerRow308(x)&&IsGateLedgerRow308(y)?GateFieldsEqual308(x.after,y.before):x.after==y.before;
  static bool GatePut308(Object asset,Row308 first)
  {
   if(!IsGateLedgerRow308(first)||!(asset is DemoCampaignProfile p))return false;
   var s=p.FindStage(first.id);if(s==null)return false;   // the stage is gone: the whole-form Put308 appends it again
   var old=JsonUtility.FromJson<DemoCampaignProfile.Stage>(first.before);
   s.PrerequisiteIds=old.PrerequisiteIds??Array.Empty<string>();s.Optional=old.Optional;
   return true;
  }
  static void GateGroupsInData308(HashSet<string> set)
  {
   GateDelta308 d;try{d=GateDeltaNow308();}catch(Refuse308){return;}
   if(d!=null)set.Add(d.Group);
  }

  // checks:campaign, between "refused with J1 only" and "accepted": with the delta on, J1 + W1 alone must be refused and the lesson
  // stage must be required; the lesson is then completed in `ready` so the base line tests the full prerequisite set.
  static void GateChecks308(Check308 k,DemoCampaignProfile profile,DemoCampaignState ready,List<string> killed)
  {
   GateDelta308 d;try{d=GateDeltaNow308();}catch(Refuse308 r){k.C(false,"gate delta data: "+r.Message);return;}
   if(d==null){k.I("gate delta: none - 청룡 needs J1 + W1 only (D308-2)");return;}
   var lesson=profile.FindStage(d.LessonStage);
   k.C(lesson!=null&&!lesson.Optional&&lesson.Implemented&&lesson.TriggerId==d.LessonTrigger,d.Decision+" stage "+d.LessonStage+" (trigger "+d.LessonTrigger+") is required: Optional "+(lesson==null?"-":lesson.Optional.ToString())+", Implemented "+(lesson==null?"-":lesson.Implemented.ToString()));
   k.C(!DemoCampaignProgression.TryAdvance(profile,ready,DemoEventKind.BossDefeated,"cheongryong",out _,out _,killed),d.Decision+" AC-C1 TryAdvance(BossDefeated, cheongryong) refused with J1 + W1 but without the metal lesson ("+d.LessonStage+")");
   // the stage must sit before 청룡 in the list: CurrentRequired walks the stages in order
   int li=Array.FindIndex(profile.Stages,s=>s!=null&&s.Id==d.LessonStage),ci=Array.FindIndex(profile.Stages,s=>s!=null&&s.Id=="cheongryong");
   k.C(li>=0&&li<ci,d.Decision+" "+d.LessonStage+" (index "+li+") comes before cheongryong (index "+ci+") in the stage list");
   // the lesson itself asks for nothing the player cannot have by then
   var fresh=new DemoCampaignState{CampaignId=profile.CampaignId};
   foreach(var id in profile.InitialCompletedIds??Array.Empty<string>())Complete308(profile,fresh,id);
   k.C(lesson!=null&&DemoCampaignProgression.PrerequisitesMet(lesson,fresh),d.Decision+" "+d.LessonStage+" has no prerequisite of its own on a fresh state (prerequisites ["+string.Join(",",lesson?.PrerequisiteIds??Array.Empty<string>())+"], facts ["+string.Join(",",lesson?.RequiredFacts??Array.Empty<string>())+"])");
   if(d.LessonEncounter.Length>0)
   {
    var main=Load308<WorldMacroPlaytestSO>(Targets308[MainScene].content);
    // review 2, finding 4: with the gate on the lesson is the only way to 청룡. Its actor must exist, carry the lesson's content id
    // and come back on rest (an actor killed before the lesson was learned must not lock the campaign).
    var le=Array.Find(main.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>(),e=>e!=null&&e.Id==d.LessonEncounter);
    k.C(le!=null,d.Decision+" lesson encounter "+d.LessonEncounter+" is in WorldContent_Main");
    k.C(le!=null&&le.ContentId==d.LessonTrigger,d.Decision+" lesson encounter ContentId '"+(le?.ContentId??"-")+"' = the lesson trigger "+d.LessonTrigger+" (the session completes the stage through this id)");
    k.C(le!=null&&le.RespawnOnRest,d.Decision+" lesson encounter RespawnOnRest "+(le==null?"-":le.RespawnOnRest.ToString())+" (killed without the lesson -> back after a rest: no soft lock)");
    k.I(d.Decision+" NOT judged here (Play): the player can land a confirmed Metal hit inside the growth window on a fresh save; NavMesh under the lesson actor "+(le!=null?V308(le.Feet):"-"));
   }
   k.I(d.Decision+" gate signature expected after campaign-apply: sha "+Short308(BuildingAudit308.ShaText(GateExpectedSig308(profile)))+" (now "+Short308(BuildingAudit308.ShaText(CampaignSig308(profile)))+"); equal = the delta is in place and nothing else moved");
   Complete308(profile,ready,d.LessonStage);
  }

  // ---------- 국 places (LDB:55 cap) and AC-C5 ----------

  // active + valid GukLiftSite (Lower in the realm) + active DemoGukRevisitSite (LiftPad in the realm): the count scene-check uses
  static int GukGateCount308(Scene scene,CompactWorldLayoutSO layout,string realm,List<string> names)
  {
   int n=0;
   foreach(var s in Saved308<GukLiftSite>(scene))
    if(s.isActiveAndEnabled&&s.Valid&&s.Lower!=null&&layout.RealmAt(new Vector2(s.Lower.position.x,s.Lower.position.z))?.Id==realm){n++;names?.Add("lift:"+s.Id);}
   foreach(var r in Saved308<DemoGukRevisitSite>(scene))
    if(r.isActiveAndEnabled&&r.LiftPad!=null&&layout.RealmAt(new Vector2(r.LiftPad.position.x,r.LiftPad.position.z))?.Id==realm){n++;names?.Add("revisit:"+r.RewardId);}
   return n;
  }
  // for Amneung308 (another class): the same count on an opened #308 target scene
  internal static int GukGates308(Scene scene,string realm,List<string> names)
  {
   if(!Targets308.TryGetValue(scene.path,out var t))throw new InvalidOperationException("not a #308 target scene: "+scene.path);
   var layout=UnityEditor.AssetDatabase.LoadAssetAtPath<CompactWorldLayoutSO>(t.layout);
   if(layout==null)throw new InvalidOperationException("layout missing: "+t.layout);
   return GukGateCount308(scene,layout,realm,names);
  }

  // AC-C5. With the Amneung308 build data deployed the 국 place of the 금표 암릉 is the dressed stone (a DemoGukRevisitSite whose
  // RewardId is lift.id, rise inside checks.rise_m of that data); without it the #308 check stands as it was (a Valid GukLiftSite).
  static void AmneungGate308(Pass308 k,Check308 c,JToken ld,GukLiftSite amneung)
  {
   string id=Str308(ld,"id");
   if(!File.Exists(AmneungFile308))
   {
    c.C(amneung!=null&&amneung.isActiveAndEnabled&&amneung.Valid,"AC-C5 "+id+" in the scene, active and Valid"+(amneung==null?" (PENDING: no column / no 바위 띠 yet)":" (height "+F308(amneung.Height)+" m)"));
    return;
   }
   JObject a;
   try{a=JObject.Parse(File.ReadAllText(AmneungFile308));}
   catch(Exception e){c.C(false,"AC-C5 "+AmneungFile308+" does not parse ("+e.Message+")");return;}
   var rise=Arr308(Req308(a,"checks"),"rise_m").Select(t=>t.Value<float>()).ToArray();
   if(rise.Length!=2){c.C(false,"AC-C5 amneung308.json checks.rise_m needs [min, max]");return;}
   var sites=Saved308<DemoGukRevisitSite>(k.Scene).Where(s=>s.RewardId==id).ToArray();
   if(sites.Length!=1){c.C(false,"AC-C5 (D308-16c) one DemoGukRevisitSite with RewardId "+id+" in the scene (found "+sites.Length+": Amneung308 apply:<scene> not run?)");return;}
   var site=sites[0];bool nodes=site.LiftPad!=null&&site.UpperSurface!=null&&site.DescentExit!=null;
   float up=nodes?site.UpperSurface.position.y-site.LiftPad.position.y:float.NaN;
   c.C(site.isActiveAndEnabled&&nodes&&up>=rise[0]&&up<=rise[1],"AC-C5 (D308-16c) "+id+" = the 국 stone at "+Harness303.PathOf(site.transform)+": active "+site.isActiveAndEnabled+", UpperSurface - LiftPad "+(nodes?F308(up):"-")+" m in ["+F308(rise[0],"F1")+", "+F308(rise[1],"F1")+"]");
   c.C(amneung==null,"AC-C5 (D308-16c) no GukLiftSite "+id+" beside the stone (the 12-22 m lift of the first #308 design is not built)");
  }
 }
}
