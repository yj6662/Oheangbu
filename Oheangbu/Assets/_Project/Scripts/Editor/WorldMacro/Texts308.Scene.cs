using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // Texts308 scene writes (root "Texts308") and the read-only check.
 //   boards[]  an empty holder on the content point with the point marker; InteractionVisuals[id] = the board's own renderers
 //   people[]  ONE clone of an existing body on its content point: marker, NpcJobActor (existing profile, no hand tool), props
 // Nothing outside the root is edited; the session's InteractionVisuals array gains one entry per id (ledgered, taken out again
 // by the revert). No Light, no NavMeshAgent, no collider on a prop may remain under the root.
 public static partial class Texts308
 {
  sealed class Pass
  {
   public Cfg Cfg;public Scene Scene;public WorldMacroPlaytestSession Session;public WorldMacroPlaytestSO Content;public bool Dry;public int Warns;public Transform Root;public string LedgerKey="";
   public readonly List<Row> Rows=new List<Row>();public readonly List<string> Lines=new List<string>();
   public void Say(string s)=>Lines.Add(s);
   public void Warn(string s){Warns++;Lines.Add("WARN "+s);}
   public Row Do(string op,Row r){r.op=op;Rows.Add(r);Lines.Add((Dry?"would ["+op+"]: ":"did ["+op+"]: ")+r.detail);return r;}
   // a create row names its object by BuildingAudit308.KeyOf once the object exists (never a bare name path)
   public void Key(Row r,Transform t){if(!Dry&&t!=null)r.key=BuildingAudit308.KeyOf(t);}
  }
  static Transform FindRoot(Scene scene,string name)
  {
   var hit=scene.GetRootGameObjects().Where(g=>g.name==name&&!Preview(g.transform)).ToArray();
   if(hit.Length>1)throw new Refuse("the scene has "+hit.Length+" roots named "+name);
   return hit.Length==1?hit[0].transform:null;
  }
  static bool Own(Cfg cfg,Transform t){for(var p=t;p!=null;p=p.parent)if(p.parent==null&&p.name==cfg.RootName)return true;return false;}
  static void EnsureRoot(Pass k,string op)
  {
   if(k.Root!=null)return;k.Root=FindRoot(k.Scene,k.Cfg.RootName);if(k.Root!=null)return;
   var row=k.Do(op,new Row{kind="create",key=k.Cfg.RootName,detail="create root "+k.Cfg.RootName});
   if(!k.Dry){var go=new GameObject(k.Cfg.RootName);SceneManager.MoveGameObjectToScene(go,k.Scene);k.Root=go.transform;ignore.Add(k.Root);k.Key(row,k.Root);}
  }
  static void Visuals(Pass k,string op,string id,Renderer[] renderers)
  {
   var all=k.Session.InteractionVisuals??Array.Empty<WorldMacroPlaytestSession.InteractionVisual>();var have=Array.Find(all,v=>v!=null&&v.Id==id);
   if(have!=null)
   {
    bool same=(have.Renderers??Array.Empty<Renderer>()).Where(x=>x!=null).ToHashSet().SetEquals(renderers);
    if(same)k.Say("ok InteractionVisuals["+id+"]: "+renderers.Length+" renderer(s)");
    else
    {
     // an entry of this id may be rewritten only when a visual-add row of this ledger made it (its revert takes the whole
     // entry out). An entry somebody else made is never overwritten: no row here could put its renderer list back.
     if(!Live(ReadLedger(k.LedgerKey)).Any(r=>r.kind=="visual-add"&&r.id==id))throw new Refuse("InteractionVisuals["+id+"] exists and was not added by this tool (no live visual-add row in "+LedgerFile(k.LedgerKey)+"): its renderer list is not overwritten");
     k.Do(op,new Row{kind="note",id=id,detail="InteractionVisuals["+id+"] renderers "+(have.Renderers?.Length??0)+" -> "+renderers.Length});if(!k.Dry)have.Renderers=renderers;
    }
    return;
   }
   if(renderers.Length==0)k.Warn(id+": no renderer for the focus highlight");
   k.Do(op,new Row{kind="visual-add",id=id,detail="InteractionVisuals["+id+"] += "+renderers.Length+" renderer(s)"});
   if(!k.Dry)k.Session.InteractionVisuals=all.Concat(new[]{new WorldMacroPlaytestSession.InteractionVisual{Id=id,Renderers=renderers}}).ToArray();
  }
  static void Marker(Pass k,string op,Transform t,string id,string what)
  {
   var marker=t.GetComponent<WorldMacroContentPoint>();var extra=t.GetComponentsInChildren<WorldMacroContentPoint>(true).Where(m=>m.transform!=t).ToArray();
   if(marker!=null&&marker.Id==id&&marker.Visual==t&&extra.Length==0){k.Say("ok "+what+": content point marker "+id);return;}
   k.Do(op,new Row{kind="note",id=id,detail=what+": content point marker "+id+(extra.Length>0?" ("+extra.Length+" copied marker(s) below it removed)":"")});
   if(k.Dry)return;
   foreach(var m in extra)Object.DestroyImmediate(m);   // copies would duplicate ids
   if(marker==null)marker=t.gameObject.AddComponent<WorldMacroContentPoint>();marker.Id=id;marker.Visual=t;
  }
  static string Rel(Transform root,Transform t){var parts=new List<string>();for(var p=t;p!=null&&p!=root;p=p.parent)parts.Insert(0,p.name);return string.Join("/",parts);}

  // #308 fix 4 rev 2 - a prop on a slope (texts308.json people[].props[]): hang_m / sink_m move it off the ground under it, tilt
  // lays it on the plane through four ground probes round its centre (rules.tilt_probe_m). A prop without these keys is placed
  // exactly as before.
  static float PropLift(JToken pr)=>OptNum(pr,"hang_m",0f)-OptNum(pr,"sink_m",0f);
  static Quaternion PropTilt(Cfg cfg,JToken pr,Vector3 at)
  {
   var flag=pr["tilt"];if(flag==null||flag.Type!=JTokenType.Boolean||!flag.Value<bool>())return Quaternion.identity;
   float d=cfg.R("tilt_probe_m");
   if(!Ground(at.x+d,at.z,out var a,out _,out _,out _)||!Ground(at.x-d,at.z,out var b,out _,out _,out _)||!Ground(at.x,at.z+d,out var c,out _,out _,out _)||!Ground(at.x,at.z-d,out var e,out _,out _,out _))return Quaternion.identity;
   var n=Vector3.Cross(c-e,a-b).normalized;if(n.y<0f)n=-n;
   return Quaternion.FromToRotation(Vector3.up,n);
  }

  static void ScenePlan(Pass k)
  {
   var cfg=k.Cfg;float tol=cfg.R("pose_tol_m"),yawTol=cfg.R("yaw_tol_deg");
   foreach(var b in Arr(cfg.J,"boards"))
   {
    string id=Str(b,"id"),op=Str(b,"op"),key=Str(b,"object_key");var obj=Resolved(k.Scene,key);
    if(obj==null){k.Say("skipped board "+id+" (not in this scene): no "+key);continue;}
    var point=Point(k.Content,id);if(point==null){k.Say("board "+id+": its point is not in this content yet - run content-apply:"+k.Scene.path+" first");continue;}
    EnsureRoot(k,op);var holder=k.Root!=null?k.Root.Find(id):null;string holderKey=cfg.RootName+"/"+id;float yaw=obj.eulerAngles.y;
    if(holder==null)
    {
     var row=k.Do(op,new Row{kind="create",key=holderKey,id=id,detail="create "+holderKey+" at "+V(point.Position)+" (the board "+key+" stands at "+V(obj.position)+")"});
     if(!k.Dry){holder=new GameObject(id).transform;holder.SetParent(k.Root,false);holder.SetPositionAndRotation(point.Position,Quaternion.Euler(0,yaw,0));k.Key(row,holder);}
    }
    else if(Vector3.Distance(holder.position,point.Position)>tol)
    {
     k.Do(op,new Row{kind="reseat",key=BuildingAudit308.KeyOf(holder),id=id,pos=holder.localPosition,euler=holder.localEulerAngles,scale=holder.localScale,detail="re-seat "+holderKey+" "+V(holder.position)+" -> "+V(point.Position)});
     if(!k.Dry)holder.SetPositionAndRotation(point.Position,Quaternion.Euler(0,yaw,0));
    }
    else k.Say("ok "+holderKey+" at "+V(holder.position));
    if(holder==null){k.Say("board "+id+": marker and InteractionVisuals are set with the create");continue;}
    Marker(k,op,holder,id,holderKey);
    Visuals(k,op,id,obj.GetComponentsInChildren<MeshRenderer>(true).Where(m=>m.GetComponent<MeshFilter>()!=null).Cast<Renderer>().ToArray());
   }
   foreach(var n in Arr(cfg.J,"people"))
   {
    string id=Str(n,"id"),op=Str(n,"op"),key=Str(n,"source_key");var src=Resolved(k.Scene,key);
    if(src==null){k.Say("skipped "+id+" (not in this scene): no "+key);continue;}
    if(Own(cfg,src)||Preview(src))throw new Refuse(id+": source "+key+" is under "+cfg.RootName+" or a preview");
    var point=Point(k.Content,id);if(point==null){k.Say(id+": its point is not in this content yet - run content-apply:"+k.Scene.path+" first");continue;}
    if(!Ground(point.Position.x,point.Position.z,out var g,out _,out _,out _)){k.Warn(id+": no ground under "+V(point.Position)+"; skipped");continue;}
    float dy=Mathf.Abs(g.y-point.Position.y);
    if(dy>cfg.R("y_tol_m"))k.Warn(id+": content Y "+F(point.Position.y)+" vs ground "+F(g.y)+" (|dy| "+F(dy)+" > "+F(cfg.R("y_tol_m"))+"): the content is stale here - content-revert + content-apply, then this pass again");
    var face=n["face"];float yaw=face!=null&&face.Type==JTokenType.Object?Mathf.Repeat(Harness303.YawTo(g,new Vector3(Num(face,"x"),g.y,Num(face,"z"))),360f):Mathf.Repeat(Num(n,"yaw"),360f);
    var profile=AssetDatabase.LoadAssetAtPath<NpcJobProfileSO>(Str(n,"job_profile"));if(profile==null)throw new Refuse(id+": job_profile "+Str(n,"job_profile")+" is missing (this tool makes no profile)");
    // everything the second pass would need is looked up in the first one (a missing prefab must not cost a backup and a clone)
    foreach(var pr in Arr(n,"props"))if(AssetDatabase.LoadAssetAtPath<GameObject>(Str(pr,"prefab"))==null)throw new Refuse(id+": prop prefab "+Str(pr,"prefab")+" is missing (existing props only)");
    foreach(string name in Ids(n,"reseat_children").Concat(Ids(n,"remove_children")))if(src.Find(name)==null)throw new Refuse(id+": the source "+key+" has no child '"+name+"' (reseat_children / remove_children)");
    EnsureRoot(k,op);var body=k.Root!=null?k.Root.Find(id):null;string bodyKey=cfg.RootName+"/"+id;
    if(body==null)
    {
     var row=k.Do(op,new Row{kind="create",key=bodyKey,id=id,detail="create "+bodyKey+" at "+V(g)+" yaw "+F(yaw,"F1")+" = a clone of "+key+" (profile "+profile.name+", "+Arr(n,"props").Count()+" prop(s))"});
     if(!k.Dry)
     {
      var go=Object.Instantiate(src.gameObject);SceneManager.MoveGameObjectToScene(go,k.Scene);go.transform.SetParent(k.Root,true);
      go.name=id;go.hideFlags=HideFlags.None;go.SetActive(true);go.transform.localScale=src.lossyScale;go.transform.SetPositionAndRotation(g,Quaternion.Euler(0,yaw,0));
      body=go.transform;k.Key(row,body);
     }
    }
    else if(Harness303.Flat(body.position,g)>tol||Mathf.Abs(body.position.y-g.y)>cfg.R("y_tol_m")||Mathf.Abs(Mathf.DeltaAngle(body.eulerAngles.y,yaw))>yawTol)
    {
     k.Do(op,new Row{kind="reseat",key=BuildingAudit308.KeyOf(body),id=id,pos=body.localPosition,euler=body.localEulerAngles,scale=body.localScale,detail="re-seat "+bodyKey+" "+V(body.position)+" -> "+V(g)+" yaw "+F(yaw,"F1")});
     if(!k.Dry)body.SetPositionAndRotation(g,Quaternion.Euler(0,yaw,0));
    }
    else k.Say("ok "+bodyKey+" at "+V(body.position)+" yaw "+F(body.eulerAngles.y,"F1"));
    if(body==null){k.Say(id+": marker, job, props and InteractionVisuals are set with the create (source: "+src.GetComponentsInChildren<NpcJobActor>(true).Length+" NpcJobActor, "+src.GetComponentsInChildren<Light>(true).Length+" Light, "+src.GetComponentsInChildren<NavMeshAgent>(true).Length+" NavMeshAgent)");continue;}
    Marker(k,op,body,id,bodyKey);
    // rules on the body: listed children out, no Light, the job actor on the data's profile with no hand tool
    var fixes=new List<string>();
    foreach(string name in Ids(n,"remove_children")){var child=body.Find(name);if(child!=null){fixes.Add("child "+name+" removed");if(!k.Dry)Object.DestroyImmediate(child.gameObject);}}
    var lights=body.GetComponentsInChildren<Light>(true);if(lights.Length>0){fixes.Add(lights.Length+" Light removed");if(!k.Dry)foreach(var l in lights)Object.DestroyImmediate(l);}
    // ground children the clone brought along were posed for the SOURCE's ground: each goes onto the physical ground under
    // it, keeping the height it had over the source's feet. lift[name] = how far that moved it from its copied pose.
    var lift=new Dictionary<string,float>(StringComparer.Ordinal);
    foreach(string name in Ids(n,"reseat_children"))
    {
     var child=body.Find(name);var from=src.Find(name);
     if(child==null){k.Warn(id+": re-seated child "+name+" is gone from the clone; skipped");continue;}
     var at=child.position;if(!Ground(at.x,at.z,out var cg,out _,out _,out _)){k.Warn(id+": no ground under child "+name+" at "+V(at)+"; left as copied");continue;}
     float wantY=cg.y+from.localPosition.y*body.lossyScale.y,copiedY=body.TransformPoint(from.localPosition).y;lift[name]=wantY-copiedY;
     if(Mathf.Abs(at.y-wantY)<=tol)continue;
     fixes.Add("child "+name+" re-seated y "+F(at.y)+" -> "+F(wantY)+" (the ground under it; "+F(wantY-copiedY)+" m from its copied pose)");
     if(!k.Dry)child.position=new Vector3(at.x,wantY,at.z);
    }
    var actors=body.GetComponentsInChildren<NpcJobActor>(true);
    if(actors.Length==0)k.Warn(id+": the source body carries no NpcJobActor (NpcJobs306 author:<scene> was not run on it): the clone stands idle");
    foreach(var a in actors)
    {
     var anim=a.Animator!=null?a.Animator:a.GetComponent<Animator>();int seed=Animator.StringToHash(id);
     bool human=anim!=null&&anim.avatar!=null&&anim.avatar.isHuman;
     var tools=anim!=null?anim.GetComponentsInChildren<Transform>(true).Where(t=>t!=null&&t.name.StartsWith("NpcTool306_",StringComparison.Ordinal)).ToArray():Array.Empty<Transform>();
     bool toolsOff=(a.Tools??Array.Empty<GameObject>()).Length==profile.Behaviours.Length&&(a.Tools??Array.Empty<GameObject>()).All(x=>x==null)&&tools.Length==0;
     bool controller=!human||profile.Controller==null||anim.runtimeAnimatorController==profile.Controller;
     if(a.PointId==id&&a.Seed==seed&&a.Profile==profile&&a.Session==k.Session&&toolsOff&&controller)continue;
     fixes.Add("NpcJobActor -> "+profile.name+(human&&profile.Controller!=null?" / "+profile.Controller.name:" (controller kept)")+", point "+id+", hand tools "+tools.Length+" -> 0");
     if(k.Dry)continue;
     foreach(var t in tools)if(t!=null)Object.DestroyImmediate(t.gameObject);
     a.PointId=id;a.Seed=seed;a.Profile=profile;a.Session=k.Session;a.Tools=new GameObject[profile.Behaviours.Length];
     if(human&&profile.Controller!=null){anim.runtimeAnimatorController=profile.Controller;anim.applyRootMotion=false;EditorUtility.SetDirty(anim);}
     EditorUtility.SetDirty(a);
    }
    // props: children of the body root in its frame; local y 0 = seated on the physical ground under the prop
    foreach(var pr in Arr(n,"props"))
    {
     string name=Str(pr,"name"),path=Str(pr,"prefab");var local=Req(pr,"local");float scale=Num(pr,"scale"),pyaw=Num(pr,"yaw");
     var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(prefab==null)throw new Refuse(id+": prop prefab "+path+" is missing (existing props only)");
     var want=body.TransformPoint(new Vector3(At(local,0),At(local,1),At(local,2)));
     if(Mathf.Abs(At(local,1))<1e-4f&&Ground(want.x,want.z,out var pg,out _,out _,out _))want.y=pg.y+PropLift(pr);
     string on=Opt(pr,"on");if(on!=null&&lift.TryGetValue(on,out float up))want.y+=up;   // a prop standing on a re-seated child follows it
     var rot=PropTilt(cfg,pr,want)*(body.rotation*Quaternion.Euler(0,pyaw,0));var size=prefab.transform.localScale*scale;
     var child=body.Find(name);
     // #308 fix 4: the data may name another prefab for a prop that is already there (the painted jar -> a plain one). The
     // instance of the old prefab goes and the new one is made in its place; a pose change alone still only re-seats.
     var was=child!=null?PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject):null;bool swap=child!=null&&was!=prefab;
     if(swap)
     {
      fixes.Add("prop "+name+" replaced: "+(was!=null?was.name:"(no prefab link)")+" -> "+System.IO.Path.GetFileNameWithoutExtension(path)+" x"+F(scale)+" at "+V(want));
      if(k.Dry)continue;
      Object.DestroyImmediate(child.gameObject);child=null;
     }
     if(child==null)
     {
      if(!swap)fixes.Add("prop "+name+" ("+System.IO.Path.GetFileNameWithoutExtension(path)+" x"+F(scale)+") at "+V(want));
      if(k.Dry)continue;
      var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,k.Scene);go.name=name;go.transform.SetParent(body,true);child=go.transform;
     }
     else if(Vector3.Distance(child.position,want)<=tol&&Quaternion.Angle(child.rotation,rot)<=yawTol&&(child.localScale-size).sqrMagnitude<1e-8f&&child.GetComponentsInChildren<Collider>(true).Length==0&&child.GetComponentsInChildren<Light>(true).Length==0)continue;
     else fixes.Add("prop "+name+" re-seated "+V(child.position)+" -> "+V(want));
     if(k.Dry)continue;
     child.localScale=size;child.SetPositionAndRotation(want,rot);
     foreach(var c in child.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
     foreach(var l in child.GetComponentsInChildren<Light>(true))Object.DestroyImmediate(l);
    }
    if(fixes.Count>0)k.Do(op,new Row{kind="note",key=bodyKey,id=id,detail=bodyKey+": "+string.Join("; ",fixes)});else k.Say("ok "+bodyKey+": job "+profile.name+", "+Arr(n,"props").Count()+" prop(s), 0 Light");
    // focus renderers: the same relative ones the source's entry names, else every renderer of the body
    Renderer[] renderers=null;string sourcePoint=Opt(n,"source_point");
    var entry=sourcePoint!=null?Array.Find(k.Session.InteractionVisuals??Array.Empty<WorldMacroPlaytestSession.InteractionVisual>(),v=>v!=null&&v.Id==sourcePoint):null;
    if(entry?.Renderers!=null)renderers=entry.Renderers.Where(x=>x!=null&&x.transform.IsChildOf(src)).Select(x=>body.Find(Rel(src,x.transform))).Where(t=>t!=null).Select(t=>t.GetComponent<Renderer>()).Where(x=>x!=null).ToArray();
    if(renderers==null||renderers.Length==0)renderers=body.GetComponentsInChildren<Renderer>(true);
    Visuals(k,op,id,renderers);
   }
  }

  static Pass Begin(Cfg cfg,Target t,bool dry)
  {
   var scene=Open(t);var session=Session(scene);var content=Content(t,session);
   PrepareGround(cfg,scene,session);
   return new Pass{Cfg=cfg,Scene=scene,Session=session,Content=content,Dry=dry,LedgerKey="scene_"+t.Alias};
  }
  static string SceneRun(string sceneArg,bool dry)
  {
   var cfg=Load();var t=TargetOf(cfg,sceneArg);PreviewGuard(cfg,t,false);string key="scene_"+t.Alias;
   var plan=Begin(cfg,t,true);
   var sb=new StringBuilder((dry?"scene-dry ":"scene-apply ")+t.Scene+"\n  data "+DataFile+" sha "+Short(cfg.Sha)+"\n");
   // pass 1 never changes anything: it decides whether there is something to write at all
   try{ScenePlan(plan);}
   catch(Refuse r)when(dry){sb.AppendLine("  scene plan not available: "+r.Message);return Report("texts308_"+key+"-dry.txt",sb);}
   if(dry||plan.Rows.Count==0)
   {
    foreach(var l in plan.Lines)sb.AppendLine("  "+l);
    sb.AppendLine(plan.Rows.Count==0?"  변경 없음 (nothing to write; scene not saved)":"  "+plan.Rows.Count+" row(s) would be written; WARN "+plan.Warns);
    if(plan.Scene.isDirty)sb.AppendLine("  WARN the scene is dirty after a read-only pass (bug): reload it");
    return Report("texts308_"+key+(dry?"-dry":"-last")+".txt",sb);
   }
   PreviewGuard(cfg,t,true);
   // pass 2: backup and journal first, then the same plan for real
   string abs=Harness303.Abs(t.Scene);
   var write=new Write{utc=DateTime.UtcNow.ToString("O"),target=t.Scene,backup=Backup(t.Scene),shaBefore=Harness303.Sha(abs),dataSha=cfg.Sha};
   var ledger=ReadLedger(key)??new Ledger{group=cfg.Group,target=t.Scene,created=DateTime.UtcNow.ToString("O")};
   var pass=Begin(cfg,t,false);
   try{ScenePlan(pass);}
   catch(Exception e)
   {
    EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);   // a half-applied plan must not stay in memory (nothing was saved)
    return "FAILED scene-apply "+t.Scene+" (scene reloaded from disk, nothing saved; backup "+write.backup+")\n"+(e is Refuse?"REFUSED "+e.Message:e.ToString());
   }
   write.rows=pass.Rows;ledger.writes.Add(write);
   try{WriteLedger(key,ledger);}
   catch(Exception e){EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);return "FAILED the ledger "+LedgerFile(key)+" could not be written ("+e.Message+"); scene reloaded from disk, nothing saved";}
   EditorUtility.SetDirty(pass.Session);EditorSceneManager.MarkSceneDirty(pass.Scene);
   if(!EditorSceneManager.SaveScene(pass.Scene))
   {
    EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);
    ledger.writes.Remove(write);try{WriteLedger(key,ledger);}catch(Exception){}
    return "FAILED SaveScene returned false for "+t.Scene+" (scene reloaded from disk, nothing saved, the journal row was taken out again; backup "+write.backup+")";
   }
   write.shaAfter=Harness303.Sha(abs);
   try{WriteLedger(key,ledger);}catch(Exception e){sb.AppendLine("  WARN the scene was saved and journalled, but shaAfter could not be recorded ("+e.Message+")");}
   foreach(var l in pass.Lines)sb.AppendLine("  "+l);
   sb.AppendLine("  saved: "+pass.Rows.Count+" row(s); WARN "+pass.Warns+"; scene sha "+Short(write.shaBefore)+" -> "+Short(write.shaAfter)+"; backup "+write.backup);
   sb.AppendLine("  ledger "+LedgerFile(key)+" group "+cfg.Group);
   return Report("texts308_"+key+"-last.txt",sb);
  }
  static string SceneRevert(string sceneArg,string op)
  {
   var cfg=Load();var t=TargetOf(cfg,sceneArg);string key="scene_"+t.Alias;
   var ledger=ReadLedger(key);if(ledger==null)throw new Refuse("no ledger "+LedgerFile(key)+" (nothing applied)");
   var live=Live(ledger).Where(r=>op==null||r.op==op).ToList();if(live.Count==0)throw new Refuse("no live row"+(op!=null?" of "+op:"")+" in "+LedgerFile(key));
   PreviewGuard(cfg,t,true);
   var scene=Open(t);var session=Session(scene);
   var sb=new StringBuilder("scene-revert "+t.Scene+(op!=null?" --op "+op:"")+"\n");
   string copy=Backup(t.Scene);int undone=0,skipped=0;
   try
   {
    for(int i=live.Count-1;i>=0;i--)
    {
     string why=Undo(cfg,scene,session,live[i]);
     if(why==null)undone++;else{skipped++;sb.AppendLine("  skipped "+live[i].kind+" "+(live[i].key.Length>0?live[i].key:live[i].id)+": "+why);}
    }
   }
   catch(Exception e){EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);return "FAILED scene-revert "+t.Scene+" (scene reloaded from disk, nothing saved)\n"+e;}
   EditorUtility.SetDirty(session);EditorSceneManager.MarkSceneDirty(scene);
   if(!EditorSceneManager.SaveScene(scene)){EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);return "FAILED SaveScene returned false for "+t.Scene+" (scene reloaded from disk, nothing saved, ledger kept; backup "+copy+")";}
   foreach(var r in live)r.reverted=true;WriteLedger(key,ledger);
   sb.AppendLine("  undone "+undone+" row(s), skipped "+skipped+"; scene saved (backup of the state before the revert: "+copy+")");
   sb.AppendLine("  ledger kept "+LedgerFile(key)+" (rows marked reverted)");
   return Report("texts308_"+key+"-revert.txt",sb);
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
     session.InteractionVisuals=all.Where(v=>v!=have).ToArray();return null;
    }
    case "create":
    {
     var t=BuildingAudit308.Resolve(scene,r.key);if(t==null)return "already gone";
     if(!Own(cfg,t))return "not under "+cfg.RootName;
     // a name path takes the FIRST sibling of that name: destroy only the object whose own key is still the row's key
     if(BuildingAudit308.KeyOf(t)!=r.key)return "the key no longer names this tool's object (a same-named sibling stands beside it): nothing destroyed";
     // the root is removed only when nothing else is left in it (rows are undone newest first)
     if(t.parent==null&&t.childCount>0)return "kept: "+t.childCount+" child object(s) are still inside (another op of this tool, or added by hand)";
     Object.DestroyImmediate(t.gameObject);
     // the last object of the root gone (an --op revert may already have marked the root's own create row): no empty root stays
     var left=FindRoot(scene,cfg.RootName);if(left!=null&&left.childCount==0)Object.DestroyImmediate(left.gameObject);
     return null;
    }
    case "reseat":
    {
     var t=BuildingAudit308.Resolve(scene,r.key);if(t==null)return "object gone";
     if(!Own(cfg,t)||BuildingAudit308.KeyOf(t)!=r.key)return "the key no longer names this tool's object";
     t.localPosition=r.pos;t.localEulerAngles=r.euler;t.localScale=r.scale;return null;
    }
    default:return "unknown row kind";
   }
  }

  // ---------- check (read-only): AC-T1..T9 ----------

  static string Check(string sceneArg)
  {
   var cfg=Load();var t=TargetOf(cfg,sceneArg);PreviewGuard(cfg,t,false);
   var scene=Open(t);var session=Session(scene);var c=Content(t,session);var camp=Campaign(cfg);PrepareGround(cfg,scene,session);
   var sb=new StringBuilder("Texts308 check "+t.Scene+"\n  data "+DataFile+" sha "+Short(cfg.Sha)+"\n");int pass=0,fail=0;
   void C(bool ok,string line){if(ok)pass++;else fail++;sb.AppendLine((ok?"  PASS ":"  FAIL ")+line);}
   C(true,"AC-T1 text rules: "+cfg.Text.Count+" lines within the limits, no banned word (checked at load)");
   // campaign
   var cr=cfg.J["campaign_rows"];var st=Stage(camp,Str(cr,"stage"));var v=Req(cr,"variants")[Str(cr,"variant")];
   C(st!=null&&st.Objective==cfg.T(Str(v,"Objective"))&&st.DestinationLabel==cfg.T(Str(v,"DestinationLabel")),"AC-T2 "+Str(cr,"stage")+": Objective '"+(st?.Objective??"")+"' / DestinationLabel '"+(st?.DestinationLabel??"")+"' = variant "+Str(cr,"variant"));
   foreach(var ts in Arr(cfg.J,"testimonies"))
   {
    int i=FindTestimony(camp,ts);string want=Joined(cfg,ts,"pages",Opt(ts,"join")=="evidence");
    C(i>=0&&camp.Testimonies[i].Text==want,"AC-T3 testimony "+Str(ts,"id")+" ("+Str(ts,"trigger")+"): "+(i<0?"absent":"present, text "+(camp.Testimonies[i].Text==want?"equal":"DIFFERS")));
    int first=Array.FindIndex(camp.Testimonies,x=>x!=null&&x.TriggerId==Str(ts,"trigger"));
    if(i>=0&&Opt(ts,"place")=="first")C(first==i,"AC-T3 testimony "+Str(ts,"id")+" is the first row of its trigger (DialogueFor returns the first eligible row)");
    string until=Opt(ts,"until_stage_open")??"";
    if(until.Length>0){var gate=Stage(camp,until);C(gate!=null&&gate.Implemented&&(gate.PrerequisiteIds??Array.Empty<string>()).Length>0,"AC-T3 "+Str(ts,"id")+": shown while "+until+" is closed = until ["+string.Join(", ",gate?.PrerequisiteIds??Array.Empty<string>())+"] are completed (the gate's own row, read at run time)");}
    C(Point(c,Str(ts,"trigger"))!=null,"AC-T3 testimony "+Str(ts,"id")+": trigger point "+Str(ts,"trigger")+" is in this content");
   }
   // content: keepers
   foreach(var kp in Arr(cfg.J,"keepers"))
   {
    var p=Point(c,Str(kp,"point"));if(p==null){sb.AppendLine("  note keeper "+Str(kp,"point")+" is not in this content (skipped)");continue;}
    foreach(var row in TalkRows(cfg,kp))
    {
     var have=(p.Services??Array.Empty<PrologueContentSO.PointService306>()).Where(s=>s!=null&&s.Kind==PrologueContentSO.PointServiceKind306.Talk&&s.Label==row.Label).ToArray();
     C(have.Length==1&&(have[0].Lines??Array.Empty<string>()).SequenceEqual(row.Lines),"AC-T4 "+p.Id+" Talk row '"+row.Label+"': "+(have.Length==1?(have[0].Lines??Array.Empty<string>()).Length+" line(s) "+((have[0].Lines??Array.Empty<string>()).SequenceEqual(row.Lines)?"equal":"DIFFER"):have.Length+" row(s) of that name (a Content308 content-apply rewrites this point's Services: run Texts308 content-apply again)"));
    }
    C((p.Services??Array.Empty<PrologueContentSO.PointService306>()).Any(s=>s!=null&&s.Kind==PrologueContentSO.PointServiceKind306.Rest),"AC-T4 "+p.Id+" keeps its 쉬기 row ("+RowsText(p.Services)+")");
    // the rows that are not ours, against what the ledger saw before its first write to this point
    var first=Live(ReadLedger("content_"+t.Alias)).FirstOrDefault(r=>r.kind=="services"&&r.id==p.Id);
    if(first==null)sb.AppendLine("  note "+p.Id+": no live services row in the content ledger (not applied here yet): the rows that are not ours are not compared");
    else
    {
     var mine=new HashSet<string>(TalkRows(cfg,kp).Select(r=>r.Label??""));var was=first.before.Length>0?JsonUtility.FromJson<ServiceBox>(first.before)?.v:null;
     C(Foreign(was,mine)==Foreign(p.Services,mine),"AC-T4 "+p.Id+": every row that is not one of this tool's Talk rows is as the ledger found it ("+RowsText(was)+")");
    }
   }
   // content: points, places
   var root=FindRoot(scene,cfg.RootName);
   foreach(var spec in Arr(cfg.J,"points"))
   {
    string id=Str(spec,"id");string why=SkipPoint(cfg,scene,id);if(why!=null){sb.AppendLine("  note point "+id+" skipped in this scene: "+why);continue;}
    var p=Point(c,id);if(p==null){C(false,"AC-T5 point "+id+" is in the content");continue;}
    var want=Built(cfg,spec,p.Position,p);
    C(JsonUtility.ToJson(want)==JsonUtility.ToJson(p),"AC-T5 point "+id+": kind, prompt '"+p.Prompt+"', text, speaker '"+p.Speaker+"', rows ("+RowsText(p.Services)+"), radius "+F(p.Radius,"F1")+" as the data says");
    C(Arr(spec,"places").Any(pl=>Mathf.Abs(Num(pl,"x")-p.Position.x)<=cfg.R("pin_keep_m")&&Mathf.Abs(Num(pl,"z")-p.Position.z)<=cfg.R("pin_keep_m")),"AC-T6 point "+id+" stands on one of its candidate places "+V(p.Position));
    if(Ground(p.Position.x,p.Position.z,out var g,out float slope,out float cover,out string coverName))
    {
     C(Mathf.Abs(g.y-p.Position.y)<=cfg.R("y_tol_m"),"AC-T6 point "+id+": |content y - ground| "+F(Mathf.Abs(g.y-p.Position.y))+" <= "+F(cfg.R("y_tol_m")));
     C(slope<=Num(spec,"max_slope_deg"),"AC-T6 point "+id+": physical slope "+F(slope,"F1")+"° <= "+F(Num(spec,"max_slope_deg"),"F0")+"°");
     C(cover>cfg.R("cover_probe_up_m"),"AC-T6 point "+id+": nothing walkable within "+F(cfg.R("cover_probe_up_m"),"F0")+" m above it"+(cover<=cfg.R("cover_probe_up_m")?" ("+coverName+" at +"+F(cover)+" m)":""));
    }
    else C(false,"AC-T6 point "+id+": ground under "+V(p.Position));
    var lines=new List<string>();Clear(cfg,spec,c,p.Position,lines);
    foreach(var l in lines)if(l.EndsWith("[info]",StringComparison.Ordinal))sb.AppendLine("  note "+id+": "+l);else C(!l.EndsWith("[FAIL]",StringComparison.Ordinal),"AC-T6 point "+id+": "+l);
    // scene side
    var obj=root!=null?root.Find(id):null;
    C(obj!=null&&Vector3.Distance(obj.position,p.Position)<=cfg.R("y_tol_m"),"AC-T7 "+cfg.RootName+"/"+id+" stands on its content point"+(obj!=null?" ("+F(Vector3.Distance(obj.position,p.Position))+" m off)":" (absent: run scene-apply)"));
    int markers=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<WorldMacroContentPoint>(true)).Count(m=>m.Id==id);
    C(markers==1,"AC-T7 content point markers named "+id+" in the scene: "+markers+" (exactly 1)");
    var vis=Array.Find(session.InteractionVisuals??Array.Empty<WorldMacroPlaytestSession.InteractionVisual>(),x=>x!=null&&x.Id==id);
    C(vis!=null&&(vis.Renderers??Array.Empty<Renderer>()).Count(x=>x!=null)>0,"AC-T7 InteractionVisuals["+id+"]: "+(vis==null?"absent":(vis.Renderers??Array.Empty<Renderer>()).Count(x=>x!=null)+" renderer(s)"));
   }
   foreach(var n in Arr(cfg.J,"people"))
   {
    string id=Str(n,"id");var body=root!=null?root.Find(id):null;if(body==null)continue;
    var profile=AssetDatabase.LoadAssetAtPath<NpcJobProfileSO>(Str(n,"job_profile"));var actors=body.GetComponentsInChildren<NpcJobActor>(true);
    C(actors.Length==1&&actors[0].PointId==id&&actors[0].Profile==profile&&(actors[0].Tools??Array.Empty<GameObject>()).All(x=>x==null),"AC-T8 "+id+": NpcJobActor x"+actors.Length+" on "+(profile!=null?profile.name:"(missing profile)")+", point id "+(actors.Length>0?actors[0].PointId:"-")+", no hand tool");
    int missing=Arr(n,"props").Count(pr=>body.Find(Str(pr,"name"))==null);
    C(missing==0,"AC-T8 "+id+": props "+(Arr(n,"props").Count()-missing)+"/"+Arr(n,"props").Count());
    C(Arr(n,"props").All(pr=>{var ch=body.Find(Str(pr,"name"));return ch==null||ch.GetComponentsInChildren<Collider>(true).Length==0;}),"AC-T8 "+id+": props carry no collider");
    C(Arr(n,"props").All(pr=>{var ch=body.Find(Str(pr,"name"));return ch==null||PrefabUtility.GetCorrespondingObjectFromSource(ch.gameObject)==AssetDatabase.LoadAssetAtPath<GameObject>(Str(pr,"prefab"));}),"AC-T8 "+id+": every prop is an instance of the prefab the data names (#308 fix 4)");
    C(Arr(n,"props").All(pr=>{var ch=body.Find(Str(pr,"name"));if(ch==null)return true;
      bool lies=Quaternion.Angle(ch.rotation,PropTilt(cfg,pr,ch.position)*(body.rotation*Quaternion.Euler(0,Num(pr,"yaw"),0)))<=cfg.R("yaw_tol_deg");
      bool seated=Mathf.Abs(At(Req(pr,"local"),1))>=1e-4f||Opt(pr,"on")!=null||(Ground(ch.position.x,ch.position.z,out var cg,out _,out _,out _)&&Mathf.Abs(ch.position.y-(cg.y+PropLift(pr)))<=cfg.R("y_tol_m"));
      return lies&&seated;}),"AC-T8 "+id+": every prop lies as the data says (tilt = on the ground plane under it, hang_m / sink_m over the ground under it; #308 fix 4 rev 2)");
    var source=Resolved(scene,Str(n,"source_key"));
    foreach(string name in Ids(n,"remove_children"))C(body.Find(name)==null,"AC-T8 "+id+": child "+name+" of the source body is not kept");
    foreach(string name in Ids(n,"reseat_children"))
    {
     var child=body.Find(name);var from=source!=null?source.Find(name):null;
     if(child==null||from==null){C(false,"AC-T8 "+id+": re-seated child "+name+" present (clone "+(child!=null)+", source "+(from!=null)+")");continue;}
     bool grounded=Ground(child.position.x,child.position.z,out var cg,out _,out _,out _);float off=grounded?Mathf.Abs(child.position.y-(cg.y+from.localPosition.y*body.lossyScale.y)):float.NaN;
     C(grounded&&off<=cfg.R("y_tol_m"),"AC-T8 "+id+": child "+name+" stands on the ground under it ("+(grounded?F(off)+" m off":"no ground")+" <= "+F(cfg.R("y_tol_m"))+")");
    }
   }
   if(root!=null)
   {
    C(root.GetComponentsInChildren<Light>(true).Length==0,"AC-T9 Light components under "+cfg.RootName+": "+root.GetComponentsInChildren<Light>(true).Length+" (no new light)");
    C(root.GetComponentsInChildren<NavMeshAgent>(true).Length==0&&root.GetComponentsInChildren<NavMeshObstacle>(true).Length==0,"AC-T9 NavMeshAgent / NavMeshObstacle under "+cfg.RootName+": 0 (the bodies carry a plain CapsuleCollider: a PhysicsColliders NavMesh bake carves them - move people BEFORE a bake)");
   }
   sb.AppendLine("  "+(fail==0?"no FAIL":"FAIL "+fail)+" (PASS "+pass+")");
   if(scene.isDirty)sb.AppendLine("  WARN the scene is dirty after a read-only pass (bug): reload it");
   return Report("texts308_check_"+t.Alias+".txt",sb);
  }
 }
}
