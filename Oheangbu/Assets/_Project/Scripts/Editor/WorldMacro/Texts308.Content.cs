using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // Texts308 asset writes: the campaign (XT0 two stage fields, XT1 / XT3 testimonies) and the contents (XT1 keeper rows,
 // XT2-XT4 new points). Each function plans on the loaded object, compares with what is there and writes only a difference;
 // the dry form runs the same code and restores the object. A guard string (everything this tool must leave alone) is taken
 // before and after the edit: a difference refuses the write.
 public static partial class Texts308
 {
  [Serializable] sealed class ServiceBox{public PrologueContentSO.PointService306[] v=Array.Empty<PrologueContentSO.PointService306>();}

  // ---------- campaign ----------

  static bool SameSet(string[] a,string[] b)=>(a??Array.Empty<string>()).OrderBy(x=>x,StringComparer.Ordinal).SequenceEqual((b??Array.Empty<string>()).OrderBy(x=>x,StringComparer.Ordinal));
  // ours = the row of this trigger with this condition (required facts + until_stage_open); its text may differ
  static int FindTestimony(DemoCampaignProfile p,JToken t)
  {
   string trigger=Str(t,"trigger"),until=Opt(t,"until_stage_open")??"";var facts=Ids(t,"required_facts");
   return Array.FindIndex(p.Testimonies??Array.Empty<DemoCampaignProfile.Testimony>(),x=>x!=null&&x.TriggerId==trigger&&(x.UntilStageOpen??"")==until&&SameSet(x.RequiredFacts,facts));
  }
  static DemoCampaignProfile.Stage Stage(DemoCampaignProfile p,string id)=>Array.Find(p.Stages??Array.Empty<DemoCampaignProfile.Stage>(),s=>s!=null&&s.Id==id);
  // does the asset still hold what this live ledger row wrote? (status: a file put back by another tool leaves the ledger behind)
  static bool CampaignHolds(DemoCampaignProfile p,Row r)
  {
   if(r.kind=="testimony")return (p.Testimonies??Array.Empty<DemoCampaignProfile.Testimony>()).Any(x=>x!=null&&JsonUtility.ToJson(x)==r.after);
   if(r.kind!="stage-field")return true;
   int dot=r.id.LastIndexOf('.');var st=dot>0?Stage(p,r.id.Substring(0,dot)):null;if(st==null)return false;
   return ((r.id.Substring(dot+1)=="Objective"?st.Objective:st.DestinationLabel)??"")==r.after;
  }
  // where a testimony's trigger point stands: the content that has it, else the first candidate place of the data's new point
  static Vector3? TriggerPlace(Cfg cfg,string id)
  {
   foreach(var t in cfg.Targets){var c=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(t.Content);var pt=c!=null?Point(c,id):null;if(pt!=null)return pt.Position;}
   var spec=Arr(cfg.J,"points").FirstOrDefault(x=>Str(x,"id")==id);var place=spec!=null?Arr(spec,"places").FirstOrDefault():null;
   return place!=null?new Vector3(Num(place,"x"),0,Num(place,"z")):(Vector3?)null;
  }
  // the campaign without what this tool may write: the two stage fields blanked, our testimony rows taken out
  static string CampaignGuard(Cfg cfg,DemoCampaignProfile p)
  {
   var clone=Object.Instantiate(p);
   try
   {
    var st=Stage(clone,Str(cfg.J["campaign_rows"],"stage"));if(st!=null){st.Objective="";st.DestinationLabel="";}
    var mine=new HashSet<int>(Arr(cfg.J,"testimonies").Select(t=>FindTestimony(clone,t)).Where(i=>i>=0));
    clone.Testimonies=(clone.Testimonies??Array.Empty<DemoCampaignProfile.Testimony>()).Where((x,i)=>!mine.Contains(i)).ToArray();
    return EditorJsonUtility.ToJson(clone);
   }
   finally{Object.DestroyImmediate(clone);}
  }
  static List<Row> EditCampaign(Cfg cfg,DemoCampaignProfile p,List<string> notes)
  {
   var rows=new List<Row>();var cr=cfg.J["campaign_rows"];string sid=Str(cr,"stage"),variant=Str(cr,"variant"),op=Str(cr,"op");
   // exactly two fields of exactly one stage: anything else in the data is a refusal, not a silent skip
   var allowed=Ids(cr,"allowed_fields");
   if(!SameSet(allowed,new[]{"Objective","DestinationLabel"}))throw new Refuse("campaign_rows.allowed_fields must be exactly [Objective, DestinationLabel]: this tool writes no other stage field");
   var v=Req(Req(cr,"variants"),variant) as JObject;if(v==null)throw new Refuse("campaign_rows.variants."+variant+" is not an object");
   foreach(var prop in v.Properties())if(!allowed.Contains(prop.Name))throw new Refuse("campaign_rows.variants."+variant+" names the field '"+prop.Name+"': only "+string.Join(" / ",allowed)+" may be written");
   var st=Stage(p,sid)??throw new Refuse("campaign stage "+sid+" missing");
   // the two fields belong to the stage of the lesson and to no other: the data names the stage AND its trigger, both must agree
   string lesson=Str(cr,"stage_trigger");
   if(st.TriggerId!=lesson)throw new Refuse("campaign_rows.stage "+sid+" has TriggerId '"+st.TriggerId+"', not '"+lesson+"' (campaign_rows.stage_trigger): the two fields are written to the lesson's stage only");
   void Field(string name,string before,string after,Action set)
   {
    if(string.Equals(before??"",after,StringComparison.Ordinal))return;
    rows.Add(new Row{op=op,kind="stage-field",id=sid+"."+name,before=before??"",after=after,detail=sid+"."+name+": \""+before+"\" -> \""+after+"\" (variant "+variant+")"});set();
   }
   string obj=cfg.T(Str(v,"Objective")),label=cfg.T(Str(v,"DestinationLabel"));
   Field("Objective",st.Objective,obj,()=>st.Objective=obj);Field("DestinationLabel",st.DestinationLabel,label,()=>st.DestinationLabel=label);
   foreach(var other in p.Stages)if(other!=null&&other!=st&&!string.IsNullOrEmpty(other.DestinationLabel)&&other.DestinationLabel==label)notes.Add("WARN stage "+other.Id+" has the same DestinationLabel '"+label+"': the map would show it twice");
   // testimonies
   var granted=new HashSet<string>(p.Stages.Where(s=>s!=null).SelectMany(s=>s.GrantedFacts??Array.Empty<string>()));
   foreach(var t in Arr(cfg.J,"testimonies"))
   {
    string id=Str(t,"id"),trigger=Str(t,"trigger"),until=Opt(t,"until_stage_open")??"";var facts=Ids(t,"required_facts");
    if(until.Length>0&&Stage(p,until)==null)throw new Refuse("testimony "+id+": until_stage_open '"+until+"' is not a stage of the campaign");
    foreach(var f in facts)if(!granted.Contains(f))throw new Refuse("testimony "+id+": required fact '"+f+"' is granted by no stage of the campaign");
    if(until.Length==0&&facts.Length==0)throw new Refuse("testimony "+id+" has no condition: it would replace the point's own text for good");
    if(until.Length>0)
    {
     // the gate must be the stage of the boss beside the trigger point, not just any stage that exists
     var at=TriggerPlace(cfg,trigger);float near=cfg.R("gate_near_m");
     if(at==null)throw new Refuse("testimony "+id+": trigger point "+trigger+" is in no content and not a point of the data: the gate cannot be checked");
     float gd=Harness303.Flat(at.Value,Stage(p,until).Destination);
     if(gd>near)throw new Refuse("testimony "+id+": the Destination of gate stage '"+until+"' is "+F(gd,"F1")+" m from trigger point "+trigger+" (> gate_near_m "+F(near,"F0")+"): that is not the boss this text is about");
    }
    var own=Arr(cfg.J,"points").FirstOrDefault(x=>Str(x,"id")==trigger);
    if(facts.Length>0&&own!=null)
    {
     // a fact condition on one of our points = the kill of that point's boss (clear.boss): the fact must come from that boss's stage
     string boss=Opt(own["clear"],"boss");var bs=boss!=null?Stage(p,boss):null;
     foreach(var f in facts)if(bs==null||!(bs.GrantedFacts??Array.Empty<string>()).Contains(f))throw new Refuse("testimony "+id+": fact '"+f+"' is not granted by the stage of "+trigger+"'s boss ("+(boss??"no clear.boss")+")");
    }
    var want=new DemoCampaignProfile.Testimony{TriggerId=trigger,RequiredFacts=facts,Text=Joined(cfg,t,"pages",Opt(t,"join")=="evidence"),UntilStageOpen=until};
    var list=(p.Testimonies??Array.Empty<DemoCampaignProfile.Testimony>()).ToList();int i=FindTestimony(p,t);
    string before=i<0?"":JsonUtility.ToJson(list[i]),after=JsonUtility.ToJson(want);
    if(before==after)continue;
    if(i>=0)list[i]=want;
    else{int first=list.FindIndex(x=>x!=null&&x.TriggerId==trigger);if(Opt(t,"place")=="first"&&first>=0)list.Insert(first,want);else list.Add(want);}
    p.Testimonies=list.ToArray();
    rows.Add(new Row{op=Str(t,"op"),kind="testimony",id=id,before=before,after=after,detail="testimony "+id+(i<0?" added":" updated")+": trigger "+trigger+", "+(until.Length>0?"while "+until+" is closed":"facts ["+string.Join(",",facts)+"]")+", "+Ids(t,"pages").Length+" page(s)"});
   }
   if(!p.IsValid)throw new Refuse("the edited campaign would be invalid (DemoCampaignProfile.IsValid false); nothing written");
   return rows;
  }
  static string CampaignRun(bool dry)
  {
   var cfg=Load();var p=Campaign(cfg);string path=AssetDatabase.GetAssetPath(p);
   if(EditorUtility.IsDirty(p))throw new Refuse(path+" has unsaved in-memory changes (another session?) - save or discard them first");
   var sb=new StringBuilder((dry?"campaign-dry ":"campaign-apply ")+path+"\n  data "+DataFile+" sha "+Short(cfg.Sha)+"\n");var notes=new List<string>();
   string memory=EditorJsonUtility.ToJson(p),guard=CampaignGuard(cfg,p);List<Row> rows;
   try
   {
    rows=EditCampaign(cfg,p,notes);
    if(CampaignGuard(cfg,p)!=guard)throw new Refuse("something other than "+Str(cfg.J["campaign_rows"],"stage")+".Objective / .DestinationLabel and this tool's testimony rows would change - nothing written (bug: report it)");
   }
   catch{EditorJsonUtility.FromJsonOverwrite(memory,p);throw;}
   foreach(var n in notes)sb.AppendLine("  note "+n);
   if(dry||rows.Count==0)
   {
    EditorJsonUtility.FromJsonOverwrite(memory,p);
    foreach(var r in rows)sb.AppendLine("  would ["+r.op+"]: "+r.detail);
    sb.AppendLine(rows.Count==0?"  변경 없음 (nothing to write)":"  "+rows.Count+" row(s) would be written; guard sha "+Short(BuildingAudit308.ShaText(guard)));
    return Report("texts308_campaign"+(dry?"-dry":"-last")+".txt",sb);
   }
   if(rows.Any(r=>r.kind=="stage-field"))
   {
    // D308-19: "메인 에이전트가 씬을 보고 맞는 쪽을 쓴다" - the variant is written only after the scene was looked at
    string alias=Opt(cfg.Rules,"probe_required");string probe=alias!=null?System.IO.Path.Combine(OutDir,"texts308_probe_"+alias+".txt"):null;
    if(probe!=null&&!System.IO.File.Exists(probe))
    {
     EditorJsonUtility.FromJsonOverwrite(memory,p);
     throw new Refuse("the T0 variant ("+Str(cfg.J["campaign_rows"],"variant")+") has not been checked against the scene: run probe:"+alias+" (and look at still TX0) first - "+probe+" is absent. Nothing written");
    }
    if(probe!=null)sb.AppendLine("  T0 variant "+Str(cfg.J["campaign_rows"],"variant")+" is written after the probe report "+probe+" ("+System.IO.File.GetLastWriteTimeUtc(probe).ToString("O")+")");
   }
   string failed=SaveAsset(cfg,p,path,"campaign",rows,guard,memory,sb);if(failed!=null)return failed;
   return Report("texts308_campaign-last.txt",sb);
  }
  static string CampaignRevert(string op,bool force)
  {
   var cfg=Load();var p=Campaign(cfg);string path=AssetDatabase.GetAssetPath(p),key="campaign";
   var ledger=ReadLedger(key);if(ledger==null)throw new Refuse("no ledger "+LedgerFile(key)+" (nothing applied)");
   if(EditorUtility.IsDirty(p))throw new Refuse(path+" has unsaved in-memory changes (another session?) - save or discard them first");
   var live=Live(ledger).Where(r=>op==null||r.op==op).ToList();if(live.Count==0)throw new Refuse("no live row"+(op!=null?" of "+op:"")+" in "+LedgerFile(key));
   string memory=EditorJsonUtility.ToJson(p);var sb=new StringBuilder("campaign-revert "+path+(op!=null?" --op "+op:"")+"\n");var bad=new List<string>();
   try
   {
    for(int i=live.Count-1;i>=0;i--)
    {
     var r=live[i];
     if(r.kind=="stage-field")
     {
      int dot=r.id.LastIndexOf('.');var st=Stage(p,r.id.Substring(0,dot));string name=r.id.Substring(dot+1);
      if(st==null||name!="Objective"&&name!="DestinationLabel"){bad.Add(r.id+" cannot be resolved");continue;}
      string now=name=="Objective"?st.Objective:st.DestinationLabel;
      if((now??"")!=r.after){bad.Add(r.id+" changed after this ledger wrote it");if(!force)continue;}
      if(name=="Objective")st.Objective=r.before;else st.DestinationLabel=r.before;sb.AppendLine("  ["+r.op+"] "+r.id+" put back");
     }
     else if(r.kind=="testimony")
     {
      var list=(p.Testimonies??Array.Empty<DemoCampaignProfile.Testimony>()).ToList();int at=list.FindIndex(x=>x!=null&&JsonUtility.ToJson(x)==r.after);
      if(at<0){bad.Add("testimony "+r.id+" changed after this ledger wrote it (or is gone)");continue;}   // nothing to put back onto: never forced
      if(r.before.Length==0)list.RemoveAt(at);else list[at]=JsonUtility.FromJson<DemoCampaignProfile.Testimony>(r.before);
      p.Testimonies=list.ToArray();sb.AppendLine("  ["+r.op+"] testimony "+r.id+(r.before.Length==0?" removed":" put back"));
     }
     else bad.Add("row kind '"+r.kind+"' is not a campaign row");
    }
    if(bad.Count>0&&!force)throw new Refuse(string.Join("; ",bad)+" - nothing reverted; append ' --force' to put the stage fields back anyway (the later change is lost)");
    if(!p.IsValid)throw new Refuse("the reverted campaign would be invalid; nothing reverted");
   }
   catch{EditorJsonUtility.FromJsonOverwrite(memory,p);throw;}
   foreach(var b in bad)sb.AppendLine("  WARN "+b);
   if(EditorJsonUtility.ToJson(p)==memory)sb.AppendLine("  already at the before-forms (rows marked reverted; nothing saved)");
   else
   {
    string copy;try{copy=Backup(path);}catch{EditorJsonUtility.FromJsonOverwrite(memory,p);throw;}
    EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);sb.AppendLine("  saved "+path+" (backup of the state before the revert: "+copy+")");
   }
   foreach(var r in live)r.reverted=true;WriteLedger(key,ledger);
   sb.AppendLine("  ledger kept "+LedgerFile(key)+" ("+live.Count+" row(s) marked reverted)");
   return Report("texts308_campaign-revert.txt",sb);
  }

  // ---------- content ----------

  // null = the row belongs in this scene; else why it is skipped (absent rows are skipped with a note, never an error)
  static string SkipPoint(Cfg cfg,Scene scene,string id)
  {
   foreach(var b in Arr(cfg.J,"boards"))if(Str(b,"id")==id&&Resolved(scene,Str(b,"object_key"))==null)return "the scene has no "+Str(b,"object_key");
   foreach(var n in Arr(cfg.J,"people"))if(Str(n,"id")==id&&Resolved(scene,Str(n,"source_key"))==null)return "the scene has no "+Str(n,"source_key");
   return null;
  }
  // a key is accepted only when the object it resolves to still HAS that key (a same-named sibling added since would be taken for it)
  static Transform Resolved(Scene scene,string key){var t=BuildingAudit308.Resolve(scene,key);return t!=null&&BuildingAudit308.KeyOf(t)==key?t:null;}
  // the content without what this tool may write: our points taken out, the keepers' Services emptied
  static string ContentGuard(Cfg cfg,WorldMacroPlaytestSO c)
  {
   var clone=Object.Instantiate(c);
   try
   {
    var ours=new HashSet<string>(Arr(cfg.J,"points").Select(p=>Str(p,"id")));var keepers=new HashSet<string>(Arr(cfg.J,"keepers").Select(k=>Str(k,"point")));
    clone.Points=(clone.Points??Array.Empty<PrologueContentSO.Point>()).Where(p=>p==null||!ours.Contains(p.Id)).ToArray();
    foreach(var p in clone.Points)if(p!=null&&keepers.Contains(p.Id))p.Services=Array.Empty<PrologueContentSO.PointService306>();
    return EditorJsonUtility.ToJson(clone);
   }
   finally{Object.DestroyImmediate(clone);}
  }
  static string Current(WorldMacroPlaytestSO c,Row r)
  {
   var p=Point(c,r.id);
   if(r.kind=="point")return p==null?"":JsonUtility.ToJson(p);
   if(r.kind=="services")return p==null?"":JsonUtility.ToJson(new ServiceBox{v=p.Services??Array.Empty<PrologueContentSO.PointService306>()});
   throw new Refuse("ledger row kind '"+r.kind+"' is not a content row");
  }
  static void Put(WorldMacroPlaytestSO c,string kind,string id,string json)
  {
   if(kind=="point")
   {
    var list=(c.Points??Array.Empty<PrologueContentSO.Point>()).ToList();int i=list.FindIndex(p=>p!=null&&p.Id==id);
    if(json.Length==0){if(i>=0)list.RemoveAt(i);}else{var v=JsonUtility.FromJson<PrologueContentSO.Point>(json);if(i>=0)list[i]=v;else list.Add(v);}
    c.Points=list.ToArray();return;
   }
   if(kind=="services")
   {
    var p=Point(c,id);if(p==null)throw new Refuse("point "+id+" is gone: its Services cannot be put back");
    p.Services=json.Length==0?Array.Empty<PrologueContentSO.PointService306>():JsonUtility.FromJson<ServiceBox>(json).v??Array.Empty<PrologueContentSO.PointService306>();return;
   }
   throw new Refuse("ledger row kind '"+kind+"' is not a content row");
  }
  static string RowsText(PrologueContentSO.PointService306[] rows)=>string.Join(", ",(rows??Array.Empty<PrologueContentSO.PointService306>()).Select(s=>s.Kind+(string.IsNullOrEmpty(s.Label)?"":" '"+s.Label+"'")+(s.Lines!=null&&s.Lines.Length>0?" "+s.Lines.Length+" line(s)":"")));
  // the keeper's rows with ours merged in: other rows stay where they are, a Talk row of the same name is replaced, new ones go last
  static PrologueContentSO.PointService306[] Merged(PrologueContentSO.PointService306[] had,PrologueContentSO.PointService306[] ours)
  {
   var list=(had??Array.Empty<PrologueContentSO.PointService306>()).Where(s=>s!=null).ToList();
   foreach(var row in ours)
   {
    int i=list.FindIndex(s=>s.Kind==PrologueContentSO.PointServiceKind306.Talk&&(s.Label??"")==row.Label);
    if(i>=0)list[i]=row;else list.Add(row);
   }
   return list.ToArray();
  }
  // every row that is not one of our Talk rows, in order: what a keeper edit must bring through byte for byte
  static string Foreign(PrologueContentSO.PointService306[] rows,HashSet<string> mine)=>JsonUtility.ToJson(new ServiceBox{v=(rows??Array.Empty<PrologueContentSO.PointService306>())
   .Where(s=>s!=null&&!(s.Kind==PrologueContentSO.PointServiceKind306.Talk&&mine.Contains(s.Label??""))).ToArray()});
  // the content point a data row describes, at `at`
  static PrologueContentSO.Point Built(Cfg cfg,JToken spec,Vector3 at,PrologueContentSO.Point had)
  {
   string id=Str(spec,"id"),kind=Str(spec,"kind");bool evidence=kind=="Evidence";
   if(!evidence&&kind!="Conversation")throw new Refuse("point "+id+": kind '"+kind+"' (Evidence | Conversation)");
   // a short one-line body stays in the HUD prompt (Present: <= 42 characters and no line break) where the page markup would show raw
   if(evidence&&Ids(spec,"pages").Length>1&&!cfg.Join.Contains("\n"))throw new Refuse("evidence_page_join must hold a line break: several pages without one could be shown as one prompt line");
   var p=new PrologueContentSO.Point{Id=id,Kind=evidence?PrologueInteractionKind.Evidence:PrologueInteractionKind.Conversation,Position=at,Prompt=cfg.T(Str(spec,"prompt")),
    Text=evidence?Joined(cfg,spec,"pages",true):Joined(cfg,spec,"first",false),Radius=Num(spec,"radius"),LockedText="",
    Speaker=evidence?"":cfg.T(Str(spec,"speaker")),Lines=Array.Empty<string>(),Services=evidence?Array.Empty<PrologueContentSO.PointService306>():TalkRows(cfg,spec)};
   if(had!=null){p.RequiredCompleted=had.RequiredCompleted??Array.Empty<string>();p.RequiredDefeated=had.RequiredDefeated??Array.Empty<string>();p.Currency=had.Currency;}
   return p;
  }
  // edits the loaded content; returns one row per element that changed
  static List<Row> EditContent(Cfg cfg,Scene scene,WorldMacroPlaytestSO c,List<string> notes)
  {
   var rows=new List<Row>();
   void Change(string op,string kind,string id,string before,string after,string detail){if(before==after)return;rows.Add(new Row{op=op,kind=kind,id=id,before=before,after=after,detail=detail});}
   foreach(var k in Arr(cfg.J,"keepers"))
   {
    string id=Str(k,"point");var keeper=Point(c,id);
    if(keeper==null){notes.Add("skipped keeper "+id+": not in this content");continue;}
    if(keeper.Kind!=PrologueInteractionKind.Conversation)throw new Refuse("keeper "+id+" is not a Conversation point");
    var merged=Merged(keeper.Services,TalkRows(cfg,k));
    // ContentGuard blanks the keeper's whole Services, so the rows that are not ours are compared here, before and after
    var mine=new HashSet<string>(TalkRows(cfg,k).Select(r=>r.Label??""));
    if(Foreign(keeper.Services,mine)!=Foreign(merged,mine))throw new Refuse("keeper "+id+": a row that is not one of this tool's Talk rows would change ("+RowsText(keeper.Services)+" -> "+RowsText(merged)+") - nothing written (bug: report it)");
    Change(Str(k,"op"),"services",id,JsonUtility.ToJson(new ServiceBox{v=keeper.Services??Array.Empty<PrologueContentSO.PointService306>()}),JsonUtility.ToJson(new ServiceBox{v=merged}),
     "point "+id+".Services: "+(keeper.Services?.Length??0)+" row(s) -> "+merged.Length+" ("+RowsText(merged)+")");
    keeper.Services=merged;
   }
   foreach(var spec in Arr(cfg.J,"points"))
   {
    string id=Str(spec,"id");string why=SkipPoint(cfg,scene,id);
    if(why!=null){notes.Add("skipped point "+id+" (not in this scene): "+why);continue;}
    var had=Point(c,id);var at=Pick(cfg,spec,c,had?.Position,notes);var p=Built(cfg,spec,at,had);
    Change(Str(spec,"op"),"point",id,had==null?"":JsonUtility.ToJson(had),JsonUtility.ToJson(p),"point "+id+(had==null?" added at ":" updated at ")+V(at)+" ("+p.Kind+", prompt '"+p.Prompt+"'"+
     (p.Kind==PrologueInteractionKind.Evidence?", "+Ids(spec,"pages").Length+" page(s)":", speaker '"+p.Speaker+"', rows "+RowsText(p.Services))+")");
    Put(c,"point",id,JsonUtility.ToJson(p));
   }
   // a testimony of ours whose trigger is in no content would never be heard
   foreach(var t in Arr(cfg.J,"testimonies"))if(Point(c,Str(t,"trigger"))==null)notes.Add("WARN testimony "+Str(t,"id")+": its trigger point "+Str(t,"trigger")+" is not in this content");
   return rows;
  }
  static string ContentRun(string sceneArg,bool dry)
  {
   var cfg=Load();var t=TargetOf(cfg,sceneArg);PreviewGuard(cfg,t,false);
   var scene=Open(t);var session=Session(scene);var content=Content(t,session);
   var sb=new StringBuilder((dry?"content-dry ":"content-apply ")+t.Scene+"\n  data "+DataFile+" sha "+Short(cfg.Sha)+"\n");
   if(EditorUtility.IsDirty(content))throw new Refuse(t.Content+" has unsaved in-memory changes (another session?) - save or discard them first");
   PrepareGround(cfg,scene,session);
   string key="content_"+t.Alias;var notes=new List<string>();
   string memory=EditorJsonUtility.ToJson(content),guard=ContentGuard(cfg,content);List<Row> rows;
   try
   {
    rows=EditContent(cfg,scene,content,notes);
    if(ContentGuard(cfg,content)!=guard)throw new Refuse("something other than this tool's points and the keepers' Services would change - nothing written (bug: report it)");
   }
   catch{EditorJsonUtility.FromJsonOverwrite(memory,content);throw;}
   foreach(var n in notes)sb.AppendLine("  note "+n);
   if(dry||rows.Count==0)
   {
    EditorJsonUtility.FromJsonOverwrite(memory,content);
    foreach(var r in rows)sb.AppendLine("  would ["+r.op+"]: "+r.detail);
    sb.AppendLine(rows.Count==0?"  변경 없음 (nothing to write)":"  "+rows.Count+" row(s) would be written; guard sha "+Short(BuildingAudit308.ShaText(guard)));
    if(scene.isDirty)sb.AppendLine("  WARN the scene is dirty after a read-only pass (bug): reload it");
    return Report("texts308_"+key+(dry?"-dry":"-last")+".txt",sb);
   }
   string failed=SaveAsset(cfg,content,t.Content,key,rows,guard,memory,sb);if(failed!=null)return failed;
   return Report("texts308_"+key+"-last.txt",sb);
  }
  // newest row first; a row is put back only when the element still has the row's after-form (else: refuse, or --force)
  static string ContentRevert(string sceneArg,string op,bool force)
  {
   var cfg=Load();var t=TargetOf(cfg,sceneArg);string key="content_"+t.Alias;
   var ledger=ReadLedger(key);if(ledger==null)throw new Refuse("no ledger "+LedgerFile(key)+" (nothing applied)");
   var content=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(t.Content);if(content==null)throw new Refuse("missing content "+t.Content);
   if(EditorUtility.IsDirty(content))throw new Refuse(t.Content+" has unsaved in-memory changes (another session?) - save or discard them first");
   var live=Live(ledger).Where(r=>op==null||r.op==op).ToList();if(live.Count==0)throw new Refuse("no live row"+(op!=null?" of "+op:"")+" in "+LedgerFile(key));
   string memory=EditorJsonUtility.ToJson(content),guard=ContentGuard(cfg,content);var sb=new StringBuilder("content-revert "+t.Scene+(op!=null?" --op "+op:"")+"\n");var bad=new List<string>();
   try
   {
    for(int i=live.Count-1;i>=0;i--)
    {
     var r=live[i];
     if(Current(content,r)!=r.after){bad.Add(r.kind+" "+r.id+" changed after this ledger wrote it");if(!force)continue;}
     Put(content,r.kind,r.id,r.before);sb.AppendLine("  ["+r.op+"] "+r.kind+" "+r.id+(r.before.Length==0?" removed":" put back"));
    }
    if(bad.Count>0&&!force)throw new Refuse(string.Join("; ",bad)+" - nothing reverted; append ' --force' to put the before-forms back anyway (the later change is lost)");
    if(ContentGuard(cfg,content)!=guard)throw new Refuse("something other than this tool's points and the keepers' Services would change - nothing reverted (bug: report it)");
   }
   catch{EditorJsonUtility.FromJsonOverwrite(memory,content);throw;}
   foreach(var b in bad)sb.AppendLine("  WARN forced over: "+b);
   if(EditorJsonUtility.ToJson(content)==memory)sb.AppendLine("  already at the before-forms (rows marked reverted; nothing saved)");
   else
   {
    string copy;try{copy=Backup(t.Content);}catch{EditorJsonUtility.FromJsonOverwrite(memory,content);throw;}
    EditorUtility.SetDirty(content);AssetDatabase.SaveAssetIfDirty(content);sb.AppendLine("  saved "+t.Content+" (backup of the state before the revert: "+copy+")");
   }
   foreach(var r in live)r.reverted=true;WriteLedger(key,ledger);
   sb.AppendLine("  ledger kept "+LedgerFile(key)+" ("+live.Count+" row(s) marked reverted)");
   if(Live(ReadLedger("scene_"+t.Alias)).Any())sb.AppendLine("  note scene rows of this tool are still applied in "+t.Scene+": run scene-revert:"+t.Alias+" too (a body without its content point is only scenery)");
   return Report("texts308_"+key+"-revert.txt",sb);
  }
 }
}
