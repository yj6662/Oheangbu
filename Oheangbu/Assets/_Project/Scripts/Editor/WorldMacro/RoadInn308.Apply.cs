using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // RoadInn308 writes: content (XI1 / XI2), scene (XI3), seal profile (XI4). Each function plans on the loaded object, compares
 // with what is there, and writes only a difference; the dry form runs the same code and restores the object.
 public static partial class RoadInn308
 {
  [Serializable] sealed class ServiceBox{public PrologueContentSO.PointService306[] v=Array.Empty<PrologueContentSO.PointService306>();}
  [Serializable] sealed class IdBox{public string[] v=Array.Empty<string>();}

  // what this tool must leave byte-for-byte alone in a content: every commission of the keeper (the #303 flow's four texts,
  // reward, evidence, lines) and the keeper point without its Services
  static string Guard(Cfg cfg,WorldMacroPlaytestSO c)
  {
   var sb=new StringBuilder();string cid=Str(cfg.Keeper,"commission");
   foreach(var q in c.Commissions??Array.Empty<WorldMacroPlaytestSO.CommissionSpec>())if(q!=null&&(q.Id==cid||q.GiverId==cfg.KeeperId))sb.Append(JsonUtility.ToJson(q)).Append('\n');
   var k=Point(c,cfg.KeeperId);
   if(k!=null){var copy=JsonUtility.FromJson<PrologueContentSO.Point>(JsonUtility.ToJson(k));copy.Services=Array.Empty<PrologueContentSO.PointService306>();sb.Append(JsonUtility.ToJson(copy));}
   return sb.ToString();
  }

  // ---------- content: XI1 rest point + checkpoint, XI2 keeper Services ----------

  static string Current(WorldMacroPlaytestSO c,Row r)
  {
   switch(r.kind)
   {
    case "point":{var p=Point(c,r.id);return p==null?"":JsonUtility.ToJson(p);}
    case "checkpoint":{var p=Checkpoint(c,r.id);return p==null?"":JsonUtility.ToJson(p);}
    case "services":{var p=Point(c,r.id);return p==null?"":JsonUtility.ToJson(new ServiceBox{v=p.Services??Array.Empty<PrologueContentSO.PointService306>()});}
   }
   throw new Refuse("ledger row kind '"+r.kind+"' is not a content row");
  }
  static void Put(WorldMacroPlaytestSO c,string kind,string id,string json)
  {
   switch(kind)
   {
    case "point":
    {
     var list=(c.Points??Array.Empty<PrologueContentSO.Point>()).ToList();int i=list.FindIndex(p=>p!=null&&p.Id==id);
     if(json.Length==0){if(i>=0)list.RemoveAt(i);}else{var v=JsonUtility.FromJson<PrologueContentSO.Point>(json);if(i>=0)list[i]=v;else list.Add(v);}
     c.Points=list.ToArray();return;
    }
    case "checkpoint":
    {
     var list=(c.Checkpoints??Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>()).ToList();int i=list.FindIndex(p=>p!=null&&p.Id==id);
     if(json.Length==0){if(i>=0)list.RemoveAt(i);}else{var v=JsonUtility.FromJson<WorldMacroPlaytestSO.CheckpointSpec>(json);if(i>=0)list[i]=v;else list.Add(v);}
     c.Checkpoints=list.ToArray();return;
    }
    case "services":
    {
     var p=Point(c,id);if(p==null)throw new Refuse("point "+id+" is gone: its Services cannot be put back");
     p.Services=json.Length==0?Array.Empty<PrologueContentSO.PointService306>():JsonUtility.FromJson<ServiceBox>(json).v??Array.Empty<PrologueContentSO.PointService306>();return;
    }
   }
   throw new Refuse("ledger row kind '"+kind+"' is not a content row");
  }

  // edits the loaded content; returns one row per element that changed
  static List<Row> EditContent(Cfg cfg,WorldMacroPlaytestSO c,List<string> notes)
  {
   var rows=new List<Row>();var rest=cfg.Rest;string id=cfg.RestId;var feetRow=Req(rest,"feet");
   var bad=new List<string>();var services=Services(cfg,bad);
   TextRule(cfg,"rest label",Str(rest,"label"),Mathf.RoundToInt(Num(cfg.J["text_rules"],"max_chars")),bad);
   if(bad.Count>0)throw new Refuse("text rules: "+string.Join("; ",bad)+" - revise "+DataFile+". Nothing written");
   if(services.Any(s=>s.Kind==PrologueContentSO.PointServiceKind306.Rest&&s.Target!=id))notes.Add("WARN a Rest row targets another rest than "+id);
   var keeper=Point(c,cfg.KeeperId);
   if(keeper.Kind!=PrologueInteractionKind.Conversation)throw new Refuse("keeper "+cfg.KeeperId+" is not a Conversation point");
   // XI1
   var had=Point(c,id);var hadCp=Checkpoint(c,id);
   if(had!=null&&had.Kind!=PrologueInteractionKind.Rest)throw new Refuse("point "+id+" exists and is not a Rest point");
   var at=Pinned(cfg,"rest point "+id,Num(rest,"x"),Num(rest,"z"),Num(rest,"max_slope_deg"),had?.Position,Num(rest,"y_offline"),notes);
   var feet=Pinned(cfg,"respawn feet "+id,Num(feetRow,"x"),Num(feetRow,"z"),cfg.R("feet_max_slope_deg"),hadCp?.Feet,Num(feetRow,"y_offline"),notes);
   var p=new PrologueContentSO.Point{Id=id,Kind=PrologueInteractionKind.Rest,Position=at,Prompt=Str(rest,"prompt"),Text=Str(rest,"text"),Radius=Num(rest,"radius")};
   if(had!=null){p.RequiredCompleted=had.RequiredCompleted??Array.Empty<string>();p.RequiredDefeated=had.RequiredDefeated??Array.Empty<string>();p.LockedText=had.LockedText;p.Speaker=had.Speaker??"";p.Lines=had.Lines??Array.Empty<string>();p.Services=had.Services??Array.Empty<PrologueContentSO.PointService306>();}
   var cp=new WorldMacroPlaytestSO.CheckpointSpec{Id=id,Label=Str(rest,"label"),Feet=feet,Yaw=Mathf.Repeat(Num(feetRow,"yaw"),360f),Shop=Flag(rest,"shop"),ShopRequiredStageId=hadCp?.ShopRequiredStageId??""};
   void Change(string op,string kind,string rowId,string before,string after,string detail){if(before==after)return;rows.Add(new Row{op=op,kind=kind,id=rowId,before=before,after=after,detail=detail});}
   Change("XI1","point",id,had==null?"":JsonUtility.ToJson(had),JsonUtility.ToJson(p),"point "+id+(had==null?" added at ":" updated at ")+V(at)+" radius "+F(p.Radius,"F1"));
   Change("XI1","checkpoint",id,hadCp==null?"":JsonUtility.ToJson(hadCp),JsonUtility.ToJson(cp),"checkpoint "+id+(hadCp==null?" added":" updated")+" '"+cp.Label+"' feet "+V(feet)+" yaw "+F(cp.Yaw,"F1")+(cp.Shop?" shop":""));
   Put(c,"point",id,JsonUtility.ToJson(p));Put(c,"checkpoint",id,JsonUtility.ToJson(cp));
   // XI2 (Services only)
   string rowsBefore=JsonUtility.ToJson(new ServiceBox{v=keeper.Services??Array.Empty<PrologueContentSO.PointService306>()}),rowsAfter=JsonUtility.ToJson(new ServiceBox{v=services});
   Change("XI2","services",cfg.KeeperId,rowsBefore,rowsAfter,"point "+cfg.KeeperId+".Services: "+(keeper.Services?.Length??0)+" row(s) -> "+services.Length+" ("+string.Join(", ",services.Select(s=>s.Kind+(s.Label.Length>0?" '"+s.Label+"'":"")+(s.Lines!=null&&s.Lines.Length>0?" "+s.Lines.Length+" line(s)":"")))+")");
   keeper.Services=services;
   // derived conditions the design states (reported; the numbers live in the data's rules)
   float toKeeper=Harness303.Flat(feet,keeper.Position),far=Harness303.Flat(at,keeper.Position)+Num(cfg.Keeper,"talk_radius_m");
   if(toKeeper<cfg.R("feet_keeper_min_m"))throw new Refuse("respawn feet "+F(toKeeper)+" m from the keeper's point (< "+F(cfg.R("feet_keeper_min_m"))+" m): the player would wake inside the body - revise rest.feet. Nothing written");
   if(far>p.Radius)notes.Add("WARN the keeper's talk circle reaches "+F(far)+" m from the rest point (> radius "+F(p.Radius,"F1")+"): 정비 would be dim at its far edge");
   return rows;
  }

  static string ContentRun(string sceneArg,bool dry)
  {
   var cfg=Load();var t=TargetOf(cfg,sceneArg);PreviewGuard(cfg,t,false);
   var scene=Open(t);var session=Session(scene);var content=Content(t,session);
   var sb=new StringBuilder((dry?"content-dry ":"content-apply ")+t.Scene+"\n  data "+DataFile+" sha "+Short(cfg.Sha)+"\n");
   string skipWhy=SkipReason(cfg,scene,content);
   if(skipWhy!=null){sb.AppendLine("  skipped (not in this scene): "+skipWhy+" - nothing written");return Report("roadinn308_content_"+Key(t)+(dry?"-dry":"-last")+".txt",sb);}
   if(EditorUtility.IsDirty(content))throw new Refuse(t.Content+" has unsaved in-memory changes (another session?) - save or discard them first");
   PrepareGround(cfg,scene,session);
   string key="content_"+Key(t);var notes=new List<string>();
   string memory=EditorJsonUtility.ToJson(content),guard=Guard(cfg,content);List<Row> rows;
   try
   {
    rows=EditContent(cfg,content,notes);
    if(Guard(cfg,content)!=guard)throw new Refuse("the keeper's commission or the keeper point (other than Services) would change - nothing written (bug: report it)");
   }
   catch{EditorJsonUtility.FromJsonOverwrite(memory,content);throw;}
   foreach(var n in notes)sb.AppendLine("  note "+n);
   if(dry||rows.Count==0)
   {
    EditorJsonUtility.FromJsonOverwrite(memory,content);
    foreach(var r in rows)sb.AppendLine("  would ["+r.op+"]: "+r.detail);
    sb.AppendLine(rows.Count==0?"  변경 없음 (nothing to write)":"  "+rows.Count+" row(s) would be written; commission guard sha "+Short(BuildingAudit308.ShaText(guard)));
    if(scene.isDirty)sb.AppendLine("  WARN the scene is dirty after a read-only pass (bug): reload it");
    return Report("roadinn308_"+key+(dry?"-dry":"-last")+".txt",sb);
   }
   // backup and ledger first: nothing is saved unless both can be written
   Write w;Ledger ledger;
   try
   {
    ledger=ReadLedger(key)??new Ledger{group=Opt(cfg.J,"group")??"",target=t.Content,created=DateTime.UtcNow.ToString("O")};
    string abs=Harness303.Abs(t.Content);
    w=new Write{utc=DateTime.UtcNow.ToString("O"),target=t.Content,backup=Backup(t.Content),shaBefore=Harness303.Sha(abs),dataSha=cfg.Sha,guard=BuildingAudit308.ShaText(guard),rows=rows};
    Directory.CreateDirectory(OutDir);
   }
   catch{EditorJsonUtility.FromJsonOverwrite(memory,content);throw;}
   // journal first (review 2, finding 6): the write with its rows stands in the ledger BEFORE the asset is saved; an empty shaAfter
   // means "save started, not confirmed". A crash between the two leaves a ledger that content-revert can still undo.
   ledger.writes.Add(w);
   try{WriteLedger(key,ledger);}
   catch(Exception e){EditorJsonUtility.FromJsonOverwrite(memory,content);return "FAILED the ledger "+LedgerFile(key)+" could not be written ("+e.Message+"); nothing saved (the content in memory was put back)";}
   EditorUtility.SetDirty(content);AssetDatabase.SaveAssetIfDirty(content);
   w.shaAfter=Harness303.Sha(Harness303.Abs(t.Content));
   try{WriteLedger(key,ledger);}
   catch(Exception e){sb.AppendLine("  WARN the content was saved and its rows are in the ledger, but shaAfter could not be recorded ("+e.Message+")");}
   foreach(var r in rows)sb.AppendLine("  did ["+r.op+"]: "+r.detail);
   sb.AppendLine("  saved "+t.Content+" (sha "+Short(w.shaBefore)+" -> "+Short(w.shaAfter)+"; backup "+w.backup+")");
   sb.AppendLine("  commission guard sha "+Short(w.guard)+" (unchanged by this write)");
   sb.AppendLine("  ledger "+LedgerFile(key));
   return Report("roadinn308_"+key+"-last.txt",sb);
  }

  // newest row first; a row is put back only when the element still has the row's after-form (else: refuse, or --force)
  static string ContentRevert(string sceneArg,string op,bool force)
  {
   var cfg=Load();var t=TargetOf(cfg,sceneArg);string key="content_"+Key(t);
   var ledger=ReadLedger(key);if(ledger==null)throw new Refuse("no ledger "+LedgerFile(key)+" (nothing applied)");
   var content=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(t.Content);if(content==null)throw new Refuse("missing content "+t.Content);
   if(EditorUtility.IsDirty(content))throw new Refuse(t.Content+" has unsaved in-memory changes (another session?) - save or discard them first");
   var live=Live(ledger).Where(r=>op==null||r.op==op).ToList();
   if(live.Count==0)throw new Refuse("no live row"+(op!=null?" of "+op:"")+" in "+LedgerFile(key));
   // the keeper's 쉬기 row points at the rest: the rest cannot go while that row stays
   if(op=="XI1"&&Live(ledger).Any(r=>r.op=="XI2"))throw new Refuse("XI2 (keeper Services) is still applied and its 쉬기 row targets the rest: revert XI2 first, or revert without --op");
   string memory=EditorJsonUtility.ToJson(content),guard=Guard(cfg,content);var sb=new StringBuilder("content-revert "+t.Scene+(op!=null?" --op "+op:"")+"\n");var bad=new List<string>();
   try
   {
    for(int i=live.Count-1;i>=0;i--)
    {
     var r=live[i];
     if(Current(content,r)!=r.after){bad.Add(r.kind+" "+r.id+" changed after this ledger wrote it");if(!force)continue;}
     Put(content,r.kind,r.id,r.before);sb.AppendLine("  ["+r.op+"] "+r.kind+" "+r.id+(r.before.Length==0?" removed":" put back"));
    }
    if(bad.Count>0&&!force)throw new Refuse(string.Join("; ",bad)+" - nothing reverted; append ' --force' to put the before-forms back anyway (the later change is lost)");
    if(Guard(cfg,content)!=guard)throw new Refuse("the keeper's commission or the keeper point (other than Services) would change - nothing reverted (bug: report it)");
   }
   catch{EditorJsonUtility.FromJsonOverwrite(memory,content);throw;}
   foreach(var b in bad)sb.AppendLine("  WARN forced over: "+b);
   if(EditorJsonUtility.ToJson(content)==memory)sb.AppendLine("  already at the before-forms (rows marked reverted; nothing saved)");
   else
   {
    string copy;
    try{copy=Backup(t.Content);}catch{EditorJsonUtility.FromJsonOverwrite(memory,content);throw;}
    EditorUtility.SetDirty(content);AssetDatabase.SaveAssetIfDirty(content);
    sb.AppendLine("  saved "+t.Content+" (backup of the state before the revert: "+copy+")");
   }
   foreach(var r in live)r.reverted=true;
   WriteLedger(key,ledger);
   sb.AppendLine("  ledger kept "+LedgerFile(key)+" ("+live.Count+" row(s) marked reverted; first-apply backup "+(ledger.writes.Count>0?ledger.writes[0].backup:"-")+")");
   return Report("roadinn308_"+key+"-revert.txt",sb);
  }

  // ---------- scene: XI3 lantern clone, marker, InteractionVisuals ----------

  sealed class Pass
  {
   public Cfg Cfg;public Scene Scene;public WorldMacroPlaytestSession Session;public WorldMacroPlaytestSO Content;public bool Dry;public int Warns;
   public readonly List<Row> Rows=new List<Row>();public readonly List<string> Lines=new List<string>();
   public void Say(string s)=>Lines.Add(s);
   public void Warn(string s){Warns++;Lines.Add("WARN "+s);}
   public Row Do(Row r){r.op="XI3";Rows.Add(r);Lines.Add((Dry?"would: ":"did: ")+r.detail);return r;}
   // a create row names its object by BuildingAudit308.KeyOf once the object exists (never a bare name path: two siblings may share a name)
   public void Key(Row r,Transform t){if(!Dry&&t!=null)r.key=BuildingAudit308.KeyOf(t);}
  }
  static Transform Root(Scene scene,string name)
  {
   var hit=scene.GetRootGameObjects().Where(g=>g.name==name&&!Preview(g.transform)).ToArray();
   if(hit.Length>1)throw new Refuse("the scene has "+hit.Length+" roots named "+name);
   return hit.Length==1?hit[0].transform:null;
  }
  static bool Own(Cfg cfg,Transform t){for(var p=t;p!=null;p=p.parent)if(p.parent==null&&p.name==cfg.RootName)return true;return false;}
  static Transform Source(Cfg cfg,Scene scene)
  {
   string key=Str(cfg.Lantern,"source_key");var src=BuildingAudit308.Resolve(scene,key);
   // a key is accepted only when the object it resolves to still HAS that key (a same-named sibling added since would be taken for it)
   if(src==null||BuildingAudit308.KeyOf(src)!=key)throw new Refuse("lantern source_key '"+key+"' does not resolve to exactly that object in "+scene.path+(src!=null?" (found "+BuildingAudit308.KeyOf(src)+")":"")+" - revise lantern.source_key");
   if(Own(cfg,src)||Preview(src))throw new Refuse("lantern source is under "+cfg.RootName+" or a preview");
   return src;
  }

  static void ScenePlan(Pass k)
  {
   var cfg=k.Cfg;var L=cfg.Lantern;string id=cfg.RestId,rootName=cfg.RootName,name=Str(L,"name");
   var point=Point(k.Content,id);var cp=Checkpoint(k.Content,id);
   if(point==null||cp==null)throw new Refuse("rest "+id+": point / checkpoint not in this content - run content-apply:"+k.Scene.path+" first");
   if(!Ground(point.Position.x,point.Position.z,out var g,out _))throw new Refuse("no ground under the rest point "+V(point.Position));
   float dy=Mathf.Abs(g.y-point.Position.y);
   if(dy>cfg.R("y_tol_m"))k.Warn("rest "+id+": content Y "+F(point.Position.y)+" vs ground "+F(g.y)+" (|dy| "+F(dy)+" > "+F(cfg.R("y_tol_m"))+"): the content is stale here - content-revert + content-apply, then this pass again");
   float tol=cfg.R("pose_tol_m"),yawTol=cfg.R("yaw_tol_deg");
   // holder: on the rest point, front = toward the respawn feet
   float yaw=Mathf.Repeat(Harness303.YawTo(g,cp.Feet),360f);
   var root=Root(k.Scene,rootName);
   if(root==null){var row=k.Do(new Row{kind="create",key=rootName,detail="create root "+rootName});if(!k.Dry){var go=new GameObject(rootName);SceneManager.MoveGameObjectToScene(go,k.Scene);root=go.transform;ignore.Add(root);k.Key(row,root);}}
   var holder=root!=null?root.Find(id):null;string holderKey=rootName+"/"+id;
   if(holder==null)
   {
    var row=k.Do(new Row{kind="create",key=holderKey,detail="create "+holderKey+" at "+V(g)+" yaw "+F(yaw,"F1")});
    if(!k.Dry){holder=new GameObject(id).transform;holder.SetParent(root,false);holder.SetPositionAndRotation(g,Quaternion.Euler(0,yaw,0));k.Key(row,holder);}
   }
   else if(Vector3.Distance(holder.position,g)>tol||Mathf.Abs(Mathf.DeltaAngle(holder.eulerAngles.y,yaw))>yawTol)
   {
    var kids=holder.Cast<Transform>().Select(c=>(c,c.position,c.rotation)).ToArray();
    k.Do(new Row{kind="reseat",key=BuildingAudit308.KeyOf(holder),pos=holder.localPosition,euler=holder.localEulerAngles,scale=holder.localScale,detail="re-seat "+holderKey+" "+V(holder.position)+" -> "+V(g)});
    if(!k.Dry){holder.SetPositionAndRotation(g,Quaternion.Euler(0,yaw,0));foreach(var (c,p,r) in kids)c.SetPositionAndRotation(p,r);}
   }
   // lantern: the one clone, at the rest point's ground + the data offset (world axes)
   var src=Source(cfg,k.Scene);var off=Req(L,"offset_from_point");
   var at=g+new Vector3(At(off,0),At(off,1),At(off,2));
   var rot=Quaternion.Euler(0,Num(L,"world_yaw")-Num(L,"source_front_yaw"),0)*src.rotation;
   var lantern=holder!=null?holder.Find(name):null;string lanternKey=holderKey+"/"+name;
   if(lantern==null)
   {
    var lrow=k.Do(new Row{kind="create",key=lanternKey,detail="create "+lanternKey+" at "+V(at)+" yaw "+F(rot.eulerAngles.y,"F1")+" scale x"+F(Num(L,"scale"))+" from scene "+BuildingAudit308.KeyOf(src)});
    if(!k.Dry)
    {
     var go=Object.Instantiate(src.gameObject);SceneManager.MoveGameObjectToScene(go,k.Scene);go.transform.SetParent(holder,true);
     go.name=name;go.hideFlags=HideFlags.None;go.SetActive(true);
     go.transform.localScale=src.lossyScale*Num(L,"scale");go.transform.SetPositionAndRotation(at,rot);
     foreach(var m in go.GetComponentsInChildren<WorldMacroContentPoint>(true))Object.DestroyImmediate(m);   // copies would duplicate ids
     lantern=go.transform;k.Key(lrow,lantern);
    }
   }
   else
   {
    // #308 world bundle 4 (D308-24 answer 6 "작게 · 높게"): the clone's SIZE is data too (source lossy scale x lantern.scale). It was set
    // at the create only, so a changed lantern.scale never reached a lantern that already hangs. The re-seat row records the old local
    // pose and scale for the ledger. It is NOT a way back: scene-revert undoes EVERY live row of this ledger newest first (the lantern's
    // own create row too - the lantern would be destroyed), and Undo("reseat") pins the children to their world poses. To put an old
    // size back, stage the old lantern.scale / offset and scene-apply again (this code re-sizes both ways). The far-light sleeve is a
    // child of the body mesh and is scaled with it.
    var wantScale=src.lossyScale*Num(L,"scale");var scaleTolT=L["scale_tol"];float scaleTol=scaleTolT!=null&&scaleTolT.Type!=JTokenType.Null?scaleTolT.Value<float>():.002f;
    bool moved=Vector3.Distance(lantern.position,at)>tol||Quaternion.Angle(lantern.rotation,rot)>yawTol,sized=(lantern.lossyScale-wantScale).magnitude>scaleTol;
    if(moved||sized)
    {
     k.Do(new Row{kind="reseat",key=BuildingAudit308.KeyOf(lantern),pos=lantern.localPosition,euler=lantern.localEulerAngles,scale=lantern.localScale,detail="re-seat "+lanternKey+" "+V(lantern.position)+" -> "+V(at)
      +(sized?" scale x"+F(lantern.lossyScale.x/Mathf.Max(1e-6f,src.lossyScale.x),"F3")+" -> x"+F(Num(L,"scale"),"F3")+" of the source":"")});
     if(!k.Dry)
     {
      if(sized){var ps=lantern.parent!=null?lantern.parent.lossyScale:Vector3.one;lantern.localScale=new Vector3(wantScale.x/ps.x,wantScale.y/ps.y,wantScale.z/ps.z);}
      lantern.SetPositionAndRotation(at,rot);
     }
    }
    else k.Say("ok "+lanternKey+" at "+V(lantern.position)+" scale x"+F(lantern.lossyScale.x/Mathf.Max(1e-6f,src.lossyScale.x),"F3")+" of the source");
   }
   if(lantern==null){k.Say("lantern rules (colliders off, one light, marker, InteractionVisuals) are set with the create: source has "+src.GetComponentsInChildren<Light>(true).Length+" Light, "+src.GetComponentsInChildren<Collider>(true).Length+" collider(s)");LightSource(cfg,src);return;}
   // rules on the clone: no collider (nothing to stand on or bump into), exactly light.max Point light with the data values, no shadows
   var fixes=new List<string>();
   if(Flag(L,"strip_colliders")){var cols=lantern.GetComponentsInChildren<Collider>(true);if(cols.Length>0){fixes.Add(cols.Length+" collider(s) removed");if(!k.Dry)foreach(var c in cols)Object.DestroyImmediate(c);}}
   var ld=Req(L,"light");int max=Mathf.RoundToInt(Num(ld,"max"));float range=Num(ld,"range_m"),intensity=Num(ld,"intensity");
   var lights=lantern.GetComponentsInChildren<Light>(true);
   if(lights.Length<max)throw new Refuse(lanternKey+" has "+lights.Length+" Light component(s) (< "+max+"): the source carries no light - the tool does not author one. Revise lantern.source_key");
   for(int i=0;i<lights.Length;i++)
   {
    var l=lights[i];bool keep=i<max;
    if(keep&&l.type.ToString()!=Str(ld,"type"))throw new Refuse(lanternKey+" light "+l.name+" is a "+l.type+" light, the data says "+Str(ld,"type"));
    if(l.enabled!=keep||l.gameObject.activeInHierarchy!=keep&&keep){fixes.Add(l.name+(keep?" on":" off"));if(!k.Dry){l.enabled=keep;if(keep)l.gameObject.SetActive(true);}}
    if(l.shadows!=LightShadows.None){fixes.Add(l.name+" shadows off");if(!k.Dry)l.shadows=LightShadows.None;}
    if(keep&&Mathf.Abs(l.range-range)>1e-3f){fixes.Add(l.name+" range "+F(l.range,"F1")+" -> "+F(range,"F1"));if(!k.Dry)l.range=range;}
    if(keep&&Mathf.Abs(l.intensity-intensity)>1e-3f){fixes.Add(l.name+" intensity "+F(l.intensity,"F1")+" -> "+F(intensity,"F1"));if(!k.Dry)l.intensity=intensity;}
   }
   if(fixes.Count>0)k.Do(new Row{kind="note",key=lanternKey,detail="lantern rule (no collider, "+max+" "+Str(ld,"type")+" light range "+F(range,"F1")+" intensity "+F(intensity,"F1")+", no shadows): "+string.Join(", ",fixes)});
   else k.Say("ok lantern rule: "+lights.Count(l=>l.enabled)+" light on of "+lights.Length+", colliders "+lantern.GetComponentsInChildren<Collider>(true).Length);
   var marker=lantern.GetComponent<WorldMacroContentPoint>();
   if(marker==null||marker.Id!=id||marker.Visual!=lantern)
   {
    k.Do(new Row{kind="note",key=lanternKey,id=id,detail="content point marker "+id+" on "+lanternKey});
    if(!k.Dry){if(marker==null)marker=lantern.gameObject.AddComponent<WorldMacroContentPoint>();marker.Id=id;marker.Visual=lantern;}
   }
   // focus renderers (the [F] highlight): the clone's mesh renderers. Another tool's children under the lantern (lantern.focus_exclude:
   // the far-light sleeve / card FarLight308 hangs there) never join the list - they are re-made on every re-shape, so a list
   // holding one would keep a dead reference, and a second scene-apply would rewrite the list for nothing (relayout fix 2b)
   var foreign=NameSet(L,"focus_exclude");
   var renderers=holder.GetComponentsInChildren<MeshRenderer>(true).Where(m=>m.GetComponent<MeshFilter>()!=null&&!UnderNamed(m.transform,holder,foreign)).Cast<Renderer>().ToArray();
   var all=k.Session.InteractionVisuals??Array.Empty<WorldMacroPlaytestSession.InteractionVisual>();var have=Array.Find(all,v=>v!=null&&v.Id==id);
   if(have!=null)
   {
    bool same=(have.Renderers??Array.Empty<Renderer>()).Where(x=>x!=null).ToHashSet().SetEquals(renderers);
    bool ours=(have.Renderers??Array.Empty<Renderer>()).All(x=>x==null||Own(cfg,x.transform));
    if(same)k.Say("ok InteractionVisuals["+id+"]: "+renderers.Length+" renderer(s)");
    else if(!ours)k.Warn("InteractionVisuals["+id+"] is another writer's entry ("+(have.Renderers?.Length??0)+" renderer(s)); kept");
    else{k.Do(new Row{kind="note",id=id,detail="InteractionVisuals["+id+"] renderers "+(have.Renderers?.Length??0)+" -> "+renderers.Length});if(!k.Dry)have.Renderers=renderers;}
   }
   else
   {
    if(renderers.Length==0)k.Warn("the lantern has no MeshRenderer: the rest would have no focus highlight");
    k.Do(new Row{kind="visual-add",id=id,detail="InteractionVisuals["+id+"] += "+renderers.Length+" renderer(s)"});
    if(!k.Dry)k.Session.InteractionVisuals=all.Concat(new[]{new WorldMacroPlaytestSession.InteractionVisual{Id=id,Renderers=renderers}}).ToArray();
   }
  }
  // a source that cannot give the one light is refused before anything is created
  static void LightSource(Cfg cfg,Transform src)
  {
   var ld=Req(cfg.Lantern,"light");int max=Mathf.RoundToInt(Num(ld,"max"));var lights=src.GetComponentsInChildren<Light>(true);
   if(lights.Length<max)throw new Refuse("lantern source "+BuildingAudit308.KeyOf(src)+" has "+lights.Length+" Light component(s) (< "+max+") - the tool does not author a light. Revise lantern.source_key");
   for(int i=0;i<max;i++)if(lights[i].type.ToString()!=Str(ld,"type"))throw new Refuse("lantern source light "+lights[i].name+" is a "+lights[i].type+" light, the data says "+Str(ld,"type"));
  }

  static Pass Begin(Cfg cfg,Target t,bool dry)
  {
   var scene=Open(t);var session=Session(scene);var content=Content(t,session);
   PrepareGround(cfg,scene,session);
   return new Pass{Cfg=cfg,Scene=scene,Session=session,Content=content,Dry=dry};
  }

  static string SceneRun(string sceneArg,bool dry)
  {
   var cfg=Load();var t=TargetOf(cfg,sceneArg);PreviewGuard(cfg,t,false);string key="scene_"+Key(t);
   var plan=Begin(cfg,t,true);
   var sb=new StringBuilder((dry?"scene-dry ":"scene-apply ")+t.Scene+"\n  data "+DataFile+" sha "+Short(cfg.Sha)+"\n");
   string skipWhy=SkipReason(cfg,plan.Scene,plan.Content);
   if(skipWhy!=null){sb.AppendLine("  skipped (not in this scene): "+skipWhy+" - nothing written");return Report("roadinn308_"+key+(dry?"-dry":"-last")+".txt",sb);}
   // pass 1 never changes anything: it decides whether there is something to write at all. In a dry run before content-apply
   // the rest is not in the content yet: that is reported, not refused.
   try{ScenePlan(plan);}
   catch(Refuse r)when(dry){sb.AppendLine("  scene plan not available: "+r.Message);return Report("roadinn308_"+key+"-dry.txt",sb);}
   if(dry||plan.Rows.Count==0)
   {
    foreach(var l in plan.Lines)sb.AppendLine("  "+l);
    sb.AppendLine(plan.Rows.Count==0?"  변경 없음 (nothing to write; scene not saved)":"  "+plan.Rows.Count+" row(s) would be written; WARN "+plan.Warns);
    if(plan.Scene.isDirty)sb.AppendLine("  WARN the scene is dirty after a read-only pass (bug): reload it");
    return Report("roadinn308_"+key+(dry?"-dry":"-last")+".txt",sb);
   }
   PreviewGuard(cfg,t,true);
   // pass 2: backup and ledger first, then the same plan for real
   string abs=Harness303.Abs(t.Scene);
   var write=new Write{utc=DateTime.UtcNow.ToString("O"),target=t.Scene,backup=Backup(t.Scene),shaBefore=Harness303.Sha(abs),dataSha=cfg.Sha};
   var ledger=ReadLedger(key)??new Ledger{group=Opt(cfg.J,"group")??"",target=t.Scene,created=DateTime.UtcNow.ToString("O")};
   Directory.CreateDirectory(OutDir);
   var pass=Begin(cfg,t,false);
   try{ScenePlan(pass);}
   catch(Exception e)
   {
    // a half-applied plan must not stay in memory: reload the scene from disk (nothing was saved)
    EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);
    return "FAILED scene-apply "+t.Scene+" (scene reloaded from disk, nothing saved; backup "+write.backup+")\n"+(e is Refuse?"REFUSED "+e.Message:e.ToString());
   }
   // journal first (review 2, finding 6): rows in the ledger before the scene is saved (empty shaAfter = save not confirmed)
   write.rows=pass.Rows;ledger.writes.Add(write);
   try{WriteLedger(key,ledger);}
   catch(Exception e)
   {
    EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);
    return "FAILED the ledger "+LedgerFile(key)+" could not be written ("+e.Message+"); scene reloaded from disk, nothing saved";
   }
   EditorUtility.SetDirty(pass.Session);EditorSceneManager.MarkSceneDirty(pass.Scene);
   if(!EditorSceneManager.SaveScene(pass.Scene))
   {
    EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);
    ledger.writes.Remove(write);try{WriteLedger(key,ledger);}catch(Exception){}
    return "FAILED SaveScene returned false for "+t.Scene+" (scene reloaded from disk, nothing saved, the journal row was taken out again; backup "+write.backup+")";
   }
   write.shaAfter=Harness303.Sha(abs);
   try{WriteLedger(key,ledger);}
   catch(Exception e){sb.AppendLine("  WARN the scene was saved and its rows are in the ledger, but shaAfter could not be recorded ("+e.Message+")");}
   foreach(var l in pass.Lines)sb.AppendLine("  "+l);
   sb.AppendLine("  saved: "+pass.Rows.Count+" row(s); WARN "+pass.Warns+"; scene sha "+Short(write.shaBefore)+" -> "+Short(write.shaAfter)+"; backup "+write.backup);
   sb.AppendLine("  ledger "+LedgerFile(key));
   return Report("roadinn308_"+key+"-last.txt",sb);
  }

  static string SceneRevert(string sceneArg)
  {
   var cfg=Load();var t=TargetOf(cfg,sceneArg);string key="scene_"+Key(t);
   var ledger=ReadLedger(key);if(ledger==null)throw new Refuse("no ledger "+LedgerFile(key)+" (nothing applied)");
   var live=Live(ledger).ToList();if(live.Count==0)throw new Refuse("no live row in "+LedgerFile(key));
   PreviewGuard(cfg,t,true);
   var scene=Open(t);var session=Session(scene);string abs=Harness303.Abs(t.Scene);
   var sb=new StringBuilder("scene-revert "+t.Scene+"\n");
   var last=ledger.writes.LastOrDefault();
   if(last!=null&&Harness303.Sha(abs)!=last.shaAfter)sb.AppendLine("  note the scene changed after this ledger's last write (another ledger wrote it): only this ledger's rows are undone");
   string copy=Backup(t.Scene);int undone=0,skipped=0;
   try
   {
    for(int i=live.Count-1;i>=0;i--)
    {
     string why=Undo(cfg,scene,session,live[i]);
     if(why==null)undone++;else{skipped++;sb.AppendLine("  skipped "+live[i].kind+" "+(live[i].key.Length>0?live[i].key:live[i].id)+": "+why);}
    }
   }
   catch(Exception e)
   {
    EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);
    return "FAILED scene-revert "+t.Scene+" (scene reloaded from disk, nothing saved)\n"+e;
   }
   EditorUtility.SetDirty(session);EditorSceneManager.MarkSceneDirty(scene);
   if(!EditorSceneManager.SaveScene(scene))
   {
    EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);
    return "FAILED SaveScene returned false for "+t.Scene+" (scene reloaded from disk, nothing saved, ledger kept; backup "+copy+")";
   }
   foreach(var r in live)r.reverted=true;
   WriteLedger(key,ledger);
   sb.AppendLine("  undone "+undone+" row(s), skipped "+skipped+"; scene saved (backup of the state before the revert: "+copy+")");
   sb.AppendLine("  ledger kept "+LedgerFile(key)+" (rows marked reverted; first-apply backup "+(ledger.writes.Count>0?ledger.writes[0].backup:"-")+")");
   return Report("roadinn308_"+key+"-revert.txt",sb);
  }
  // null = undone; else why not. Only objects under this tool's own root are ever destroyed or moved.
  static string Undo(Cfg cfg,Scene scene,WorldMacroPlaytestSession session,Row r)
  {
   switch(r.kind)
   {
    case "note":return null;   // set on an object this ledger created: its create row removes it
    case "visual-add":
    {
     var all=session.InteractionVisuals??Array.Empty<WorldMacroPlaytestSession.InteractionVisual>();var have=Array.Find(all,v=>v!=null&&v.Id==r.id);
     if(have==null)return "already gone";
     if(!(have.Renderers??Array.Empty<Renderer>()).All(x=>x==null||Own(cfg,x.transform)))return "the entry now names renderers outside "+cfg.RootName+" (another writer's); kept";
     session.InteractionVisuals=all.Where(v=>v!=have).ToArray();return null;
    }
    case "create":
    {
     var t=BuildingAudit308.Resolve(scene,r.key);if(t==null)return "already gone";
     if(!Own(cfg,t))return "not under "+cfg.RootName;
     // A container (the root, the holder: a bare Transform) is removed only when nothing else is left in it. Rows are undone newest
     // first, so what this ledger put inside is already gone; whatever is still there was added by hand or by another writer.
     if(t.GetComponents<Component>().Length==1&&t.childCount>0)return "kept: "+t.childCount+" child object(s) this ledger did not create are still inside";
     Object.DestroyImmediate(t.gameObject);return null;
    }
    case "reseat":
    {
     var t=BuildingAudit308.Resolve(scene,r.key);if(t==null)return "object gone";
     if(!Own(cfg,t)||BuildingAudit308.KeyOf(t)!=r.key)return "the key no longer names this tool's object";
     var kids=t.Cast<Transform>().Select(c=>(c,c.position,c.rotation)).ToArray();
     t.localPosition=r.pos;t.localEulerAngles=r.euler;t.localScale=r.scale;
     foreach(var (c,p,q) in kids)c.SetPositionAndRotation(p,q);return null;
    }
    default:return "unknown row kind";
   }
  }

  // ---------- seal profile: XI4 ----------

  static string SealRun(bool revert)
  {
   var cfg=Load();string path=Str(cfg.J["seal"],"profile"),id=cfg.RestId,key="seal";
   if(Harness303.IsProtected(path)||PostLedger308.IsProtectedPath(path))throw new Refuse("protected path "+path);
   var profile=AssetDatabase.LoadAssetAtPath<WorldSealProfileSO>(path);if(profile==null)throw new Refuse("missing seal profile "+path);
   if(EditorUtility.IsDirty(profile))throw new Refuse(path+" has unsaved in-memory changes (another session?) - save or discard them first");
   var sb=new StringBuilder((revert?"seal-revert ":"seal-apply ")+path+"\n");
   var ids=(profile.EaRestIds??Array.Empty<string>()).ToList();string before=JsonUtility.ToJson(new IdBox{v=ids.ToArray()});
   var ledger=ReadLedger(key);
   if(revert)
   {
    var live=Live(ledger).ToList();if(live.Count==0)throw new Refuse("no live row in "+LedgerFile(key)+" (the id was not added by this tool)");
    if(ids.Remove(id)){string copy=Backup(path);profile.EaRestIds=ids.ToArray();EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);sb.AppendLine("  EaRestIds -= "+id+" ("+ids.Count+" ids; backup "+copy+")");}
    else sb.AppendLine("  "+id+" already absent (rows marked reverted; nothing saved)");
    foreach(var r in live)r.reverted=true;WriteLedger(key,ledger);
    return Report("roadinn308_seal-revert.txt",sb.AppendLine("  ledger kept "+LedgerFile(key)));
   }
   if(ids.Contains(id)){sb.AppendLine("  변경 없음 (EaRestIds already has "+id+")");return Report("roadinn308_seal-last.txt",sb);}
   // the seal's fallback rest must resolve: the rest has to be in a content first
   if(!cfg.Targets.Any(t=>{var c=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(t.Content);var cp=c!=null?Checkpoint(c,id):null;return cp!=null&&cp.IsConfigured;}))
    throw new Refuse("no content has the checkpoint "+id+" yet - run content-apply first");
   bool ordered=ids.SequenceEqual(ids.OrderBy(x=>x,StringComparer.Ordinal));
   ids.Add(id);if(ordered)ids=ids.OrderBy(x=>x,StringComparer.Ordinal).ToList();
   ledger=ledger??new Ledger{group=Opt(cfg.J,"group")??"",target=path,created=DateTime.UtcNow.ToString("O")};
   string abs=Harness303.Abs(path);
   var w=new Write{utc=DateTime.UtcNow.ToString("O"),target=path,backup=Backup(path),shaBefore=Harness303.Sha(abs),dataSha=cfg.Sha};
   Directory.CreateDirectory(OutDir);
   profile.EaRestIds=ids.ToArray();EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);
   w.shaAfter=Harness303.Sha(abs);
   w.rows.Add(new Row{op="XI4",kind="seal",id=id,before=before,after=JsonUtility.ToJson(new IdBox{v=ids.ToArray()}),detail="EaRestIds += "+id});
   ledger.writes.Add(w);WriteLedger(key,ledger);
   sb.AppendLine("  did [XI4]: EaRestIds += "+id+" ("+ids.Count+" ids"+(ordered?", ordinal order kept":"")+"; backup "+w.backup+")");
   return Report("roadinn308_seal-last.txt",sb.AppendLine("  ledger "+LedgerFile(key)));
  }
 }
}
