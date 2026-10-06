using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 content scene pass (SPEC-CONTENT-PACING-308 §2·4·8, D308-9c Q4) [TEST]: the scene half of Content308.
 // Data-driven: Art/World/Compact/Rebuild/CliffBoundary308/content308_scene.json holds every number and every source; positions come
 // from the scene's own content asset (content-apply put them on the physical ground). Nothing here writes an asset: new objects are
 // clones of objects the scene already has (or instances of existing prefabs) under one root, "Content308".
 //   scene-dry:<scene>     the plan with measured numbers; nothing changes
 //   scene-apply:<scene>   idempotent ledger (Art/Playtest308/Pacing/ledger_scene308_<scene>.json + a byte backup of the scene);
 //                         a second apply reports "변경 없음" and does not save
 //   scene-revert:<scene>  undoes the ledger op by op (objects created are destroyed, before-values are put back)
 //   scene-check:<scene>   scene ACs (C5 site, C7, C12 scene half, 국 gate set, keep-outs, boss fields); read-only
 //   lifts:<scene>         every GukLiftSite / DemoGukRevisitSite: path, state, ground gap, referrers in the scene file; read-only
 //   guk-survey:<scene>    amneung lift columns scored on the real colliders; read-only
 //   keepout:<scene>       #308 content vs the cliff plan lines (read from plan/boundary308.json now) and built cliff roots; read-only
 // <scene> = the asset path, or arch296 | folk298 | main. Order: #296 candidate -> #298 candidate -> W_Demo_Main, after the cliff
 // ledger of that scene. No NavMesh bake here (one shared bake after every scene ledger). Refusals are strings; no dialog.
 public static partial class Content308
 {
  const string SceneRootDefault308="Content308";
  const string ScenePassUsage308="scene-dry:<scene> | scene-apply:<scene> | scene-revert:<scene> | scene-check:<scene> | lifts:<scene> | guk-survey:<scene> | keepout:<scene>";
  static string SceneDataFile308=>Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","CliffBoundary308","content308_scene.json");

  static string ScenePass308(string c)
  {
   string Arg(string head)=>c.StartsWith(head,StringComparison.Ordinal)?SceneArg308(c.Substring(head.Length)):null;
   string a;
   if((a=Arg("scene-dry:"))!=null)return SceneRun308(a,true);
   if((a=Arg("scene-apply:"))!=null)return SceneRun308(a,false);
   if((a=Arg("scene-revert:"))!=null)return argGroup308!=null?SceneRevertGroup308(a,argGroup308,argForce308):SceneRevert308(a);
   if((a=Arg("scene-check:"))!=null)return SceneCheck308(a);
   if((a=Arg("lifts:"))!=null)return Lifts308(a);
   if((a=Arg("guk-survey:"))!=null)return GukSurvey308(a);
   if((a=Arg("keepout:"))!=null)return KeepoutReport308(a);
   return null;
  }
  static string SceneArg308(string token)
  {
   token=(token??"").Trim();
   foreach(var s in Scenes308)if(string.Equals(PostLedger308.Short(s),token,StringComparison.OrdinalIgnoreCase))return s;
   return token;
  }

  // ---------- data (Newtonsoft: the plan file holds arrays of arrays) ----------

  static JObject SceneData308(out string sha)
  {
   string f=SceneDataFile308;
   if(!File.Exists(f))throw new Refuse308("scene data missing: "+f+" (copy Tools/Unity/Stage308_content/Data/content308_scene.json there)");
   sha=Harness303.Sha(f);
   try{return JObject.Parse(File.ReadAllText(f));}
   catch(Exception e){throw new Refuse308("scene data does not parse: "+f+" ("+e.Message+")");}
  }
  static JToken Req308(JToken o,string key){var t=o?[key];if(t==null||t.Type==JTokenType.Null)throw new Refuse308("scene data: missing '"+key+"' in "+(o==null?"(null)":string.IsNullOrEmpty(o.Path)?"(root)":o.Path));return t;}
  static float Num308(JToken o,string key)=>Req308(o,key).Value<float>();
  static string Str308(JToken o,string key)=>Req308(o,key).Value<string>();
  static string Opt308(JToken o,string key){var t=o?[key];return t==null||t.Type==JTokenType.Null?null:t.Value<string>();}
  static bool Flag308(JToken o,string key){var t=o?[key];return t!=null&&t.Type==JTokenType.Boolean&&t.Value<bool>();}
  static IEnumerable<JToken> Arr308(JToken o,string key){var t=o?[key] as JArray;return t??(IEnumerable<JToken>)Array.Empty<JToken>();}
  static float At308(JToken array,int i){var a=array as JArray;if(a==null||a.Count<=i)throw new Refuse308("scene data: "+(array==null?"(null)":array.Path)+" needs "+(i+1)+" numbers");return a[i].Value<float>();}

  // ---------- another session's look preview (CliffLook308 / WallLook308) ----------

  // A look preview is a DontSave root, but while it is up it also switches REAL renderers off (some under protected trees) and
  // changes the quality level until its own preview:off. A scene save in that state would write those switched-off renderers into
  // the scene file; opening another scene would orphan the preview's restore list. Root names come from the data (preview_roots).
  static List<string> PreviewsUp308(JObject data)
  {
   if(!(Req308(data,"preview_roots") is JArray list)||list.Count==0)throw new Refuse308("scene data: 'preview_roots' must be a non-empty list of preview root names (CliffLook308 / WallLook308)");
   var names=new HashSet<string>(list.Select(t=>t.Value<string>()));
   return Resources.FindObjectsOfTypeAll<GameObject>().Where(g=>g!=null&&!EditorUtility.IsPersistent(g)&&g.transform.parent==null&&names.Contains(g.name)).Select(g=>g.name).Distinct().OrderBy(n=>n,StringComparer.Ordinal).ToList();
  }
  // write = the command saves the scene. A read-only command is refused only when it would have to open another scene.
  static void PreviewGuard308(JObject data,string scenePath,bool write)
  {
   var up=PreviewsUp308(data);if(up.Count==0)return;
   var active=SceneManager.GetActiveScene();bool switches=!(active.path==scenePath&&SceneManager.sceneCount==1);
   if(write)throw new Refuse308("a look preview is up ("+string.Join(", ",up)+"): saving the scene now would write the renderers it switched off - run that tool's preview:off first, then this command again");
   if(switches)throw new Refuse308("a look preview is up ("+string.Join(", ",up)+") in "+active.path+": opening "+scenePath+" would orphan its restore list - run that tool's preview:off first");
  }

  // ---------- the pass ----------

  [Serializable] sealed class SceneOp308
  {
   // op: create | trs | patrol | actor-add | wired-add | visual-add | lift-retire | note
   public string op="",path="",id="",detail="",field="",before="";
   // T7: group = the data group that asked for the op ("" = the #308 base); key = BuildingAudit308.KeyOf of the object when the row
   // was written (same-named siblings stay apart); posAfter = its world position after the op (a group revert refuses when the
   // object is no longer there); undone = a group revert already undid this row (the whole-ledger revert skips it).
   // "note" rows with field reseat / reseat-holder carry the local TRS before, so a re-seat of an object this pass created can be
   // put back by a group revert; "remove" rows record an object taken out because its group-tagged props row was switched off.
   public string group="",key="";public Vector3 posAfter;public bool hasAfter,undone;
   public Vector3 pos,euler,scale;                 // local TRS before (trs, note/reseat)
   public Vector3[] points=Array.Empty<Vector3>(); // PatrolPoints before (patrol)
   public float f0,f1;                             // CastRadius, RiseSeconds (lift-retire)
  }
  [Serializable] sealed class SceneWrite308{public string utc="",backup="",shaBefore="",shaAfter="",dataSha="",planSha="";public List<SceneOp308> ops=new List<SceneOp308>();}
  [Serializable] sealed class SceneLedger308{public string kind="scene",scene="",created="";public List<SceneWrite308> writes=new List<SceneWrite308>();}
  static string SceneKey308(string scene)=>"scene308_"+Key308(scene);

  sealed class Pass308
  {
   public Scene Scene;public WorldMacroPlaytestSession Session;public WorldMacroPlaytestSO Content;public CompactWorldLayoutSO Layout;public JObject Data;
   public bool Dry;public int Warns,Pending;
   public readonly List<SceneOp308> Ops=new List<SceneOp308>();public readonly List<string> Lines=new List<string>();
   public readonly HashSet<string> Blocked=new HashSet<string>();   // content ids a keep-out forbids
   public readonly HashSet<string> Planned=new HashSet<string>();   // root / group paths already counted (a dry pass meets them again)
   public float PoseTol,YawTol;
   public string Group="";                                          // the data group of the row being handled (stamped on its ops)
   public void Say(string s)=>Lines.Add(s);
   public void Warn(string s){Warns++;Lines.Add("WARN "+s);}
   public void Hold(string s){Pending++;Lines.Add("PENDING "+s);}
   public void Do(SceneOp308 op){if(string.IsNullOrEmpty(op.group))op.group=Group??"";Ops.Add(op);Lines.Add((Dry?"would: ":"did: ")+op.detail+(op.group.Length>0?" ["+op.group+"]":""));}
  }

  static Pass308 Begin308(string scenePath,bool dry,out string dataSha)
  {
   var data=SceneData308(out dataSha);
   PreviewGuard308(data,Scene308(scenePath),false);
   var scene=Open308(scenePath);var session=Session308(scene);var t=Targets308[scene.path];
   var content=Load308<WorldMacroPlaytestSO>(t.content);var layout=Load308<CompactWorldLayoutSO>(t.layout);
   if(session.Content!=content)throw new Refuse308(scene.path+" session Content is "+AssetDatabase.GetAssetPath(session.Content)+", expected "+t.content);
   PrepareGround308(scene,session);
   var g=Req308(data,"ground");
   return new Pass308{Scene=scene,Session=session,Content=content,Layout=layout,Data=data,Dry=dry,PoseTol=Num308(g,"pose_tol_m"),YawTol=Num308(g,"yaw_tol_deg")};
  }

  static void ScenePlan308(Pass308 k)
  {
   k.Say("-- keep-outs");Keepout308(k,true);
   k.Say("-- rests (성황당 ×3, 사냥꾼 주막)");RestOps308(k);
   k.Say("-- npc bodies");NpcOps308(k);
   k.Say("-- encounters");ActorOps308(k);
   k.Say("-- dead 국 lift sites (D308-9c Q4)");LiftRetireOps308(k);
   k.Say("-- 금표 암릉 국 바위턱");LiftSiteOps308(k);
   k.Say("-- props");PropOps308(k);
   k.Say("-- escort_start seat (proposal)");EscortSeatOps308(k);
   // relayout (D308-16): only with content308_relayout.json present - without it the plan above is the whole pass
   k.Group="";
   if(Relayout308()!=null){k.Say("-- escort rests (relayout rests_escort, T5)");EscortRestOps308(k);k.Group="";}
  }

  static string SceneRun308(string scenePath,bool dry)
  {
   // pass 1 never changes anything: it decides whether there is something to write at all
   var plan=Begin308(scenePath,true,out string dataSha);ScenePlan308(plan);
   string planSha=PlanSha308(plan.Data);string key=SceneKey308(plan.Scene.path);
   var head=new StringBuilder((dry?"scene-dry ":"scene-apply ")+plan.Scene.path+"\n  data "+SceneDataFile308+" sha "+Short308(dataSha)+", plan sha "+Short308(planSha)+"\n");
   int real=plan.Ops.Count;
   if(dry||real==0)
   {
    foreach(var l in plan.Lines)head.AppendLine("  "+l);
    head.AppendLine(real==0?"  변경 없음 (nothing to write; scene not saved)":"  "+real+" op(s) would be written; WARN "+plan.Warns+", PENDING "+plan.Pending+", keep-out blocked "+plan.Blocked.Count);
    if(plan.Scene.isDirty)head.AppendLine("  WARN the scene is dirty after a read-only pass (bug): reload it");
    return SceneOut308(key+(dry?"-dry":"-last")+".txt",head);
   }
   // a scene save while a look preview is up would write the renderers that preview switched off (PreviewGuard308)
   PreviewGuard308(plan.Data,plan.Scene.path,true);
   // pass 2: backup first, then the same plan for real
   string abs=Harness303.Abs(plan.Scene.path);string backupDir=BackupDir308();Directory.CreateDirectory(backupDir);
   string copy=Path.Combine(backupDir,Path.GetFileName(plan.Scene.path));File.Copy(abs,copy,true);
   var write=new SceneWrite308{utc=DateTime.UtcNow.ToString("O"),backup=copy,shaBefore=Harness303.Sha(abs),dataSha=dataSha,planSha=planSha};
   // the ledger must be readable before anything is saved (a ledger that cannot be written after the save would leave ops with no revert)
   var ledger=ReadSceneLedger308(key)??new SceneLedger308{scene=plan.Scene.path,created=DateTime.UtcNow.ToString("O")};
   var pass=Begin308(scenePath,false,out _);
   try{ScenePlan308(pass);}
   catch(Exception e)
   {
    // a half-applied plan must not stay in memory: reload the scene from disk (nothing was saved)
    EditorSceneManager.OpenScene(pass.Scene.path,OpenSceneMode.Single);
    return "FAILED scene-apply "+pass.Scene.path+" (scene reloaded from disk, nothing saved; backup "+copy+")\n"+(e is Refuse308?"REFUSED "+e.Message:e.ToString());
   }
   EditorUtility.SetDirty(pass.Session);
   EditorSceneManager.MarkSceneDirty(pass.Scene);
   if(!EditorSceneManager.SaveScene(pass.Scene))
   {
    // the applied plan must not stay in memory as an unsaved scene either
    EditorSceneManager.OpenScene(pass.Scene.path,OpenSceneMode.Single);
    return "FAILED SaveScene returned false for "+pass.Scene.path+" (scene reloaded from disk, nothing saved; backup "+copy+")";
   }
   write.shaAfter=Harness303.Sha(abs);write.ops=pass.Ops;
   ledger.writes.Add(write);
   try{Directory.CreateDirectory(Out308);File.WriteAllText(LedgerFile308(key),JsonUtility.ToJson(ledger,true));}
   catch(Exception e)
   {
    return "FAILED the scene was saved ("+pass.Ops.Count+" op(s)) but the ledger "+LedgerFile308(key)+" could not be written ("+e.Message+"). Do not run scene-apply again: copy the backup "+copy+
           " back over "+abs+" (sha before "+write.shaBefore+") and refresh, then fix the ledger folder";
   }
   foreach(var l in pass.Lines)head.AppendLine("  "+l);
   head.AppendLine("  saved: "+pass.Ops.Count+" op(s); WARN "+pass.Warns+", PENDING "+pass.Pending+", keep-out blocked "+pass.Blocked.Count);
   head.AppendLine("  scene sha "+Short308(write.shaBefore)+" -> "+Short308(write.shaAfter)+"; backup "+copy);
   head.AppendLine("  ledger "+LedgerFile308(key));
   return SceneOut308(key+"-last.txt",head);
  }
  static SceneLedger308 ReadSceneLedger308(string key){var f=LedgerFile308(key);return File.Exists(f)?JsonUtility.FromJson<SceneLedger308>(File.ReadAllText(f)):null;}
  static string SceneOut308(string file,StringBuilder sb){Directory.CreateDirectory(Out308);File.WriteAllText(Path.Combine(Out308,file),sb.ToString());return sb.Append("  report "+Path.Combine(Out308,file)).ToString();}
  static string Short308(string sha)=>string.IsNullOrEmpty(sha)?"-":sha.Substring(0,Math.Min(12,sha.Length));
  static string PlanSha308(JObject data){string f=Path.Combine(Harness303.RepoRoot,Str308(data,"plan_file"));return File.Exists(f)?Harness303.Sha(f):"";}

  // ---------- revert ----------

  static string SceneRevert308(string scenePath)
  {
   scenePath=Scene308(scenePath);string key=SceneKey308(scenePath);
   var ledger=ReadSceneLedger308(key);if(ledger==null)throw new Refuse308("no ledger "+LedgerFile308(key)+" (nothing applied, or already reverted)");
   // the revert saves the scene: never while a look preview has real renderers switched off
   PreviewGuard308(SceneData308(out _),scenePath,true);
   var scene=Open308(scenePath);var session=Session308(scene);string abs=Harness303.Abs(scene.path);
   var sb=new StringBuilder("scene-revert "+scene.path+"\n");
   var last=ledger.writes.LastOrDefault();
   if(last!=null&&Harness303.Sha(abs)!=last.shaAfter)sb.AppendLine("  note the scene changed after this ledger's last write (another ledger wrote it): only this ledger's ops are undone");
   string backupDir=BackupDir308();Directory.CreateDirectory(backupDir);string copy=Path.Combine(backupDir,Path.GetFileName(scene.path));File.Copy(abs,copy,true);
   int undone=0,missing=0;
   try
   {
    for(int w=ledger.writes.Count-1;w>=0;w--)
     for(int i=ledger.writes[w].ops.Count-1;i>=0;i--)
     {
      var op=ledger.writes[w].ops[i];if(op.undone)continue;   // a group revert already undid it
      string r=Undo308(scene,session,op,false);
      if(r==null)undone++;else{missing++;sb.AppendLine("  skipped "+op.op+" "+(string.IsNullOrEmpty(op.id)?op.path:op.id)+": "+r);}
     }
   }
   catch(Exception e)
   {
    EditorSceneManager.OpenScene(scene.path,OpenSceneMode.Single);
    return "FAILED scene-revert "+scene.path+" (scene reloaded from disk, nothing saved)\n"+e;
   }
   EditorUtility.SetDirty(session);EditorSceneManager.MarkSceneDirty(scene);
   if(!EditorSceneManager.SaveScene(scene))
   {
    EditorSceneManager.OpenScene(scene.path,OpenSceneMode.Single);
    return "FAILED SaveScene returned false for "+scene.path+" (scene reloaded from disk, nothing saved, ledger kept; backup "+copy+")";
   }
   string archived=LedgerFile308(key)+".reverted-"+Harness303.UtcStamp();File.Move(LedgerFile308(key),archived);
   sb.AppendLine("  undone "+undone+" op(s), skipped "+missing+"; scene saved (backup of the state before the revert: "+copy+")");
   sb.AppendLine("  first-apply backup (bytes before any #308 scene op): "+(ledger.writes.Count>0?ledger.writes[0].backup:"-"));
   sb.AppendLine("  ledger archived "+archived);
   return SceneOut308(key+"-revert.txt",sb);
  }

  // null = undone; else why not
  // groupMode (T7): "note" rows that carry a before pose (re-seats of objects this pass created) are put back too. In the
  // whole-ledger revert they need nothing: the object's own "create" row destroys it.
  static string Undo308(Scene scene,WorldMacroPlaytestSession session,SceneOp308 op,bool groupMode)
  {
   switch(op.op)
   {
    case "note":
    {
     if(!groupMode||(op.field!="reseat"&&op.field!="reseat-holder"))return null;
     var t=TargetOf308(scene,op,out string why);if(t==null)return why;
     if(PostLedger308.UnderProtectedTree(t))return "under a protected tree";
     // a holder's children keep their own world poses (their rows put them back themselves)
     var kids=op.field=="reseat-holder"?t.Cast<Transform>().Select(c=>(c,c.position,c.rotation)).ToArray():null;
     t.localPosition=op.pos;t.localEulerAngles=op.euler;t.localScale=op.scale;
     if(kids!=null)foreach(var (c,p,r) in kids)c.SetPositionAndRotation(p,r);
     return null;
    }
    case "remove":return "the object was removed because its props row was switched off: switch the row on and run scene-apply to place it again (was at "+V308(op.pos)+")";
    case "create":
    {
     var hit=FindAll308(scene,op.path);if(hit.Count==0)return "already gone";
     // the root and its groups (path depth ≤ 1) are shared holders: what this ledger put in them is already undone (reverse
     // order), so anything still inside belongs to another writer and the holder stays
     bool shared=op.path.Count(ch=>ch=='/')<=1;
     if(shared&&hit.Any(t=>t.childCount>0))return "kept: "+hit.Sum(t=>t.childCount)+" child object(s) this ledger did not create are still inside";
     foreach(var t in hit)Object.DestroyImmediate(t.gameObject);return null;
    }
    case "trs":
    {
     Transform t=TargetOf308(scene,op,out string why);
     if(t==null)return why;
     if(PostLedger308.UnderProtectedTree(t))return "under a protected tree";
     t.localPosition=op.pos;t.localEulerAngles=op.euler;t.localScale=op.scale;return null;
    }
    case "patrol":
    {
     var a=Saved308<PrologueEncounter>(scene).FirstOrDefault(x=>x.Id==op.id);if(a==null)return "encounter not found";
     a.PatrolPoints=op.points??Array.Empty<Vector3>();EditorUtility.SetDirty(a);return null;
    }
    case "actor-add":
     session.Actors=(session.Actors??Array.Empty<PrologueEncounter>()).Where(a=>a!=null&&a.Id!=op.id).ToArray();return null;
    case "wired-add":
    {
     var a=Saved308<PrologueEncounter>(scene).FirstOrDefault(x=>x.Id==op.id);var vit=a!=null?a.GetComponent<EnemyVitals>():null;
     if(vit==null)return "enemy not found";SetWired308(session,vit,false);return null;
    }
    case "visual-add":
     session.InteractionVisuals=(session.InteractionVisuals??Array.Empty<WorldMacroPlaytestSession.InteractionVisual>()).Where(v=>v==null||v.Id!=op.id).ToArray();return null;
    case "lift-retire":
    {
     var hit=FindAll308(scene,op.path);if(hit.Count!=1)return "owner object found "+hit.Count+" times";
     var parts=(op.before??"").Split('|');if(parts.Length<3)return "ledger row has no before-values";
     if(op.field=="disable"){var s=hit[0].GetComponent<GukLiftSite>();if(s==null)return "component gone";s.enabled=parts[0]=="1";return null;}
     if(hit[0].GetComponent<GukLiftSite>()!=null)return "component already there";
     var site=hit[0].gameObject.AddComponent<GukLiftSite>();site.Id=op.id;site.CastRadius=op.f0;site.RiseSeconds=op.f1;
     site.Lower=FindAll308(scene,parts[1]).FirstOrDefault();site.Upper=FindAll308(scene,parts[2]).FirstOrDefault();site.enabled=parts[0]=="1";
     return site.Lower!=null&&site.Upper!=null?null:"re-added, but Lower / Upper were not found ("+parts[1]+", "+parts[2]+")";
    }
    default:return "unknown op";
   }
  }

  // the object a ledger row is about: by component id for actors / npc bodies, else by its ledger key (BuildingAudit308.KeyOf:
  // same-named siblings stay apart), else by the path as before (an object name may itself hold '/', which a key cannot express)
  // A key is accepted only when the object it resolves to still HAS that key (Resolve takes the first same-named child for a
  // segment without '#k': a sibling added since would be mistaken for the row's object). A key that no longer reads back falls
  // to the path, and the path must then name exactly one object; anything else is "ambiguous" and the row is left alone.
  static Transform TargetOf308(Scene scene,SceneOp308 op)=>TargetOf308(scene,op,out _);
  static Transform TargetOf308(Scene scene,SceneOp308 op,out string why)
  {
   why="object not found";
   if(op.op=="trs"&&op.field=="encounter")return Saved308<PrologueEncounter>(scene).FirstOrDefault(a=>a.Id==op.id)?.transform;
   if(op.op=="trs"&&op.field=="point")return Saved308<WorldMacroContentPoint>(scene).FirstOrDefault(a=>a.Id==op.id&&!Own308(a.transform))?.transform;
   var hit=FindAll308(scene,op.path);
   if(!string.IsNullOrEmpty(op.key))
   {
    var t=BuildingAudit308.Resolve(scene,op.key);
    if(t!=null&&!Preview308(t)&&BuildingAudit308.KeyOf(t)==op.key)return t;
    if(hit.Count>1){why="ambiguous key: "+op.key+" no longer reads back as one object and the path "+op.path+" names "+hit.Count+" (same-named siblings appeared after the row was written)";return null;}
   }
   return hit.FirstOrDefault();
  }
  // what a ledger row is about, for chaining the rows of one object (T7)
  static string TargetId308(SceneOp308 op)=>
   op.op=="trs"&&(op.field=="encounter"||op.field=="point")?op.field+"|"+op.id:!string.IsNullOrEmpty(op.key)?"key|"+op.key:"path|"+op.path;

  // scene-revert:<scene> --group <G> (T7): only that group's rows, newest first. The rows of one object form a chain (the same
  // group may have written it more than once: data refined after a probe, the re-apply after the NavMesh bake): only the NEWEST
  // row's after-place is compared with where the object stands now - an object that is no longer there (another writer moved it)
  // refuses the whole revert (" --force" overrides) - and the older rows are undone behind it in order, ending at the oldest
  // before-value (the rule of the content half, RevertGroup308). A row that could not be undone stays LIVE (only "already gone"
  // creates and "remove" rows are closed), so the whole-ledger revert still sees it. The ledger stays; the scene is backed up and
  // saved once. As in the content half, a group that is still on in the relayout data is refused (the next apply would redo it).
  static string SceneRevertGroup308(string scenePath,string group,bool force)
  {
   scenePath=Scene308(scenePath);string key=SceneKey308(scenePath);
   var ledger=ReadSceneLedger308(key);if(ledger==null)throw new Refuse308("no ledger "+LedgerFile308(key)+" (nothing applied, or already reverted)");
   var rd=Relayout308();
   if(rd!=null&&!rd.Off.Contains(group)&&GroupsInData308(rd).Contains(group))
    throw new Refuse308("group '"+group+"' is still on in "+RelayoutFile308+": add it to groups_off first (else the next scene-apply writes it again), then run this revert");
   var rows=ledger.writes.SelectMany(w=>w.ops).Where(o=>o!=null&&o.group==group&&!o.undone).ToList();
   if(rows.Count==0)throw new Refuse308("no live op of group '"+group+"' in "+LedgerFile308(key)+" (groups with live ops: "+
    string.Join(", ",ledger.writes.SelectMany(w=>w.ops).Where(o=>o!=null&&!o.undone).Select(o=>o.group.Length==0?"(base)":o.group).Distinct().OrderBy(x=>x,StringComparer.Ordinal))+")");
   var data=SceneData308(out _);PreviewGuard308(data,scenePath,true);
   var scene=Open308(scenePath);var session=Session308(scene);string abs=Harness303.Abs(scene.path);
   float tol=Num308(Req308(data,"ground"),"pose_tol_m");
   var sb=new StringBuilder("scene-revert "+scene.path+" --group "+group+"\n");
   var drift=new List<string>();
   // rows are in ledger order (oldest first): per object only its newest after-place is checked
   var chains=rows.Where(o=>o.hasAfter).GroupBy(TargetId308).Select(g=>g.ToList()).ToList();
   foreach(var chain in chains)
   {
    var op=chain[chain.Count-1];var t=TargetOf308(scene,op,out string why);string name=string.IsNullOrEmpty(op.id)?op.path:op.id;
    if(t==null)drift.Add(op.op+" "+name+": "+why);
    else if(Vector3.Distance(t.position,op.posAfter)>tol)drift.Add(op.op+" "+name+": now at "+V308(t.position)+", the ledger's newest row left it at "+V308(op.posAfter));
    if(chain.Count>1)sb.AppendLine("  chain "+name+": "+chain.Count+" rows of "+group+" on this object; only the newest after-place is compared, the oldest before-value is what comes back");
   }
   if(drift.Count>0&&!force)throw new Refuse308("group '"+group+"' no longer matches its ledger rows (another writer moved these): "+string.Join("; ",drift)+" - nothing undone; append ' --force' to put the before-values back anyway");
   foreach(var d in drift)sb.AppendLine("  WARN forced over "+d);
   string backupDir=BackupDir308();Directory.CreateDirectory(backupDir);string copy=Path.Combine(backupDir,Path.GetFileName(scene.path));File.Copy(abs,copy,true);
   int undone=0,skipped=0;var done=new List<SceneOp308>();var live=new List<SceneOp308>();
   try
   {
    for(int i=rows.Count-1;i>=0;i--)
    {
     var op=rows[i];string r=Undo308(scene,session,op,true);
     if(r==null){undone++;done.Add(op);sb.AppendLine("  undone "+op.op+" "+(string.IsNullOrEmpty(op.id)?op.path:op.id)+(op.field.Length>0?" ("+op.field+")":""));}
     else
     {
      // closed only when there is nothing left to undo; every other reason keeps the row live (its before-value is still needed)
      bool closed=op.op=="remove"||(op.op=="create"&&r=="already gone");
      skipped++;if(closed)done.Add(op);else live.Add(op);
      sb.AppendLine("  skipped "+op.op+" "+(string.IsNullOrEmpty(op.id)?op.path:op.id)+": "+r+(closed?"":" [row stays live]"));
     }
    }
   }
   catch(Exception e)
   {
    EditorSceneManager.OpenScene(scene.path,OpenSceneMode.Single);
    return "FAILED scene-revert --group "+group+" "+scene.path+" (scene reloaded from disk, nothing saved, ledger untouched)\n"+e;
   }
   EditorUtility.SetDirty(session);EditorSceneManager.MarkSceneDirty(scene);
   if(!EditorSceneManager.SaveScene(scene))
   {
    EditorSceneManager.OpenScene(scene.path,OpenSceneMode.Single);
    return "FAILED SaveScene returned false for "+scene.path+" (scene reloaded from disk, nothing saved, ledger untouched; backup "+copy+")";
   }
   foreach(var op in done)op.undone=true;
   try{File.WriteAllText(LedgerFile308(key),JsonUtility.ToJson(ledger,true));}
   catch(Exception e){return "FAILED the scene was saved (group "+group+" undone) but the ledger "+LedgerFile308(key)+" could not be written ("+e.Message+"): mark the group's rows undone by hand or copy the backup "+copy+" back";}
   sb.AppendLine("  undone "+undone+" op(s), skipped "+skipped+"; scene saved (backup of the state before this revert: "+copy+")");
   sb.AppendLine("  STILL LIVE "+live.Count+(live.Count==0?"":" row(s) of "+group+" (not undone, not closed: fix the reason above and run this revert again; the whole-ledger scene-revert also still sees them): "+
    string.Join(", ",live.Select(o=>o.op+" "+(string.IsNullOrEmpty(o.id)?o.path:o.id)))));
   sb.AppendLine("  ledger kept "+LedgerFile308(key)+" ("+done.Count+" row(s) of "+group+" marked undone). The content half is content-revert:<scene> --group "+group+"; with the group still on in the data the next scene-apply places it again");
   return SceneOut308(key+"-revert-"+group+".txt",sb);
  }

  // ---------- scene helpers ----------

  // saved objects only: another session's in-memory preview (HideFlags.DontSave) is never read
  static bool Preview308(Transform t){for(var p=t;p!=null;p=p.parent)if((p.gameObject.hideFlags&HideFlags.DontSave)!=0)return true;return false;}
  static IEnumerable<T> Saved308<T>(Scene scene)where T:Component=>scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true)).Where(c=>c!=null&&!Preview308(c.transform));
  static bool Own308(Transform t){for(var p=t;p!=null;p=p.parent)if(p.parent==null&&p.name==SceneRootDefault308)return true;return false;}

  // hierarchy path lookup; an object name may itself hold '/' (mine_beast/1), so a child may consume several path parts
  static List<Transform> FindAll308(Scene scene,string path)
  {
   var list=new List<Transform>();var parts=(path??"").Split('/');if(parts.Length==0||parts[0].Length==0)return list;
   void Walk(Transform t,int next)
   {
    if(next>=parts.Length){list.Add(t);return;}
    foreach(Transform c in t)
     for(int n=1;next+n<=parts.Length;n++)if(c.name==string.Join("/",parts,next,n))Walk(c,next+n);
   }
   foreach(var root in scene.GetRootGameObjects())
   {
    if(Preview308(root.transform))continue;
    for(int n=1;n<=parts.Length;n++)if(root.name==string.Join("/",parts,0,n))Walk(root.transform,n);
   }
   return list;
  }

  static PrologueContentSO.Point Point308(WorldMacroPlaytestSO c,string id)=>Array.Find(c.Points??Array.Empty<PrologueContentSO.Point>(),p=>p!=null&&p.Id==id);
  static WorldMacroPlaytestSO.CheckpointSpec Checkpoint308(WorldMacroPlaytestSO c,string id)=>Array.Find(c.Checkpoints??Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>(),p=>p!=null&&p.Id==id);
  static WorldMacroPlaytestSO.Encounter Encounter308(WorldMacroPlaytestSO c,string id)=>Array.Find(c.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>(),p=>p!=null&&p.Id==id);

  static float SegDist308(Vector2 p,Vector2 a,Vector2 b){var ab=b-a;float l=ab.sqrMagnitude;float t=l<1e-6f?0:Mathf.Clamp01(Vector2.Dot(p-a,ab)/l);return Vector2.Distance(p,a+ab*t);}
  // XZ distance to the content corridors (MainPath + BranchPath; a jump over 40 m starts a new run)
  static float PathDistance308(WorldMacroPlaytestSO content,Vector3 at)
  {
   float best=float.PositiveInfinity;var p=new Vector2(at.x,at.z);
   foreach(var path in new[]{content.MainPath,content.BranchPath})
   {
    if(path==null)continue;
    for(int i=0;i<path.Length;i++)
    {
     var a=new Vector2(path[i].x,path[i].z);best=Mathf.Min(best,Vector2.Distance(p,a));
     if(i==0)continue;var b=new Vector2(path[i-1].x,path[i-1].z);
     if(Vector2.Distance(a,b)<=RunGap308)best=Mathf.Min(best,SegDist308(p,b,a));
    }
   }
   return best;
  }

  // the one root and its groups; created objects are ledger rows so the revert removes exactly them
  static Transform Group308(Pass308 k,string group)
  {
   string groupPath=SceneRootDefault308+"/"+group;
   var root=FindAll308(k.Scene,SceneRootDefault308).FirstOrDefault();
   if(root==null)
   {
    if(k.Planned.Add(SceneRootDefault308))k.Do(new SceneOp308{op="create",path=SceneRootDefault308,detail="create root "+SceneRootDefault308});
    if(!k.Dry){var go=new GameObject(SceneRootDefault308);SceneManager.MoveGameObjectToScene(go,k.Scene);root=go.transform;ignore308.Add(root);}
   }
   var g=root!=null?root.Find(group):null;
   if(g==null)
   {
    if(k.Planned.Add(groupPath))k.Do(new SceneOp308{op="create",path=groupPath,detail="create group "+groupPath});
    if(!k.Dry){g=new GameObject(group).transform;g.SetParent(root,false);}
   }
   return g;   // null in a dry pass while the group does not exist yet
  }
  // an empty holder at a world pose under a group
  static Transform Holder308(Pass308 k,string group,string name,Vector3 at,float yaw)
  {
   var parent=Group308(k,group);var have=parent!=null?parent.Find(name):null;string path=SceneRootDefault308+"/"+group+"/"+name;
   if(have==null)
   {
    // a group that does not exist yet has already counted itself; the holder is one more row
    k.Do(new SceneOp308{op="create",path=path,detail="create "+path+" at "+V308(at)+" yaw "+F308(yaw,"F1")});
    if(k.Dry)return null;
    have=new GameObject(name).transform;have.SetParent(parent,false);have.SetPositionAndRotation(at,Quaternion.Euler(0,yaw,0));return have;
   }
   if(Vector3.Distance(have.position,at)>k.PoseTol||Mathf.Abs(Mathf.DeltaAngle(have.eulerAngles.y,yaw))>k.YawTol)
   {
    // moving the holder would drag its children off their own grounded poses: children are re-seated by their own rows
    var kids=have.Cast<Transform>().Select(c=>(c,c.position,c.rotation)).ToArray();
    k.Do(new SceneOp308{op="note",field="reseat-holder",path=path,key=BuildingAudit308.KeyOf(have),pos=have.localPosition,euler=have.localEulerAngles,scale=have.localScale,hasAfter=true,posAfter=at,
     detail="re-seat "+path+" "+V308(have.position)+" -> "+V308(at)});
    if(!k.Dry){have.SetPositionAndRotation(at,Quaternion.Euler(0,yaw,0));foreach(var (c,p,r) in kids)c.SetPositionAndRotation(p,r);}
   }
   return have;
  }

  // a clone of a scene object (source_scene) or an instance of a prefab (source_asset) at a world pose. The source's own world
  // scale is kept; its world yaw is turned by (yaw - source_front_yaw). WorldMacroContentPoint copies never survive (duplicate ids).
  static bool Source308(Pass308 k,JToken part,string what,out Transform sceneSource,out GameObject asset)
  {
   sceneSource=null;asset=null;string s=Opt308(part,"source_scene"),a=Opt308(part,"source_asset");
   if(!string.IsNullOrEmpty(s))
   {
    var hit=FindAll308(k.Scene,s).Where(t=>!Own308(t)).ToList();
    if(hit.Count!=1){k.Warn(what+": source "+s+" found "+hit.Count+" time(s) in this scene; skipped");return false;}
    sceneSource=hit[0];return true;
   }
   if(!string.IsNullOrEmpty(a))
   {
    if(Harness303.IsProtected(a)){k.Warn(what+": source asset under a protected path "+a+"; skipped");return false;}
    asset=AssetDatabase.LoadAssetAtPath<GameObject>(a);if(asset==null){k.Warn(what+": source asset missing "+a+"; skipped");return false;}
    return true;
   }
   k.Warn(what+": no source_scene / source_asset in data; skipped");return false;
  }
  static Quaternion SourceRotation308(JToken part,Transform sceneSource,float yaw)
  {
   // no source_front_yaw = the source's own world yaw is its front, so `yaw` is then the clone's absolute world yaw
   float front=part["source_front_yaw"]!=null?part["source_front_yaw"].Value<float>():sceneSource!=null?sceneSource.eulerAngles.y:0f;
   var turn=Quaternion.Euler(0,yaw-front,0);
   return sceneSource!=null?turn*sceneSource.rotation:turn;
  }
  static Transform Place308(Pass308 k,Transform parent,string parentPath,string name,JToken part,Vector3 at,float yaw,string what)
  {
   if(!Source308(k,part,what,out var src,out var asset))return null;
   var rot=SourceRotation308(part,src,yaw);string path=parentPath+"/"+name;
   var have=parent!=null?parent.Find(name):null;
   if(have!=null)
   {
    if(Vector3.Distance(have.position,at)>k.PoseTol||Quaternion.Angle(have.rotation,rot)>k.YawTol)
    {
     k.Do(new SceneOp308{op="note",field="reseat",path=path,key=BuildingAudit308.KeyOf(have),pos=have.localPosition,euler=have.localEulerAngles,scale=have.localScale,hasAfter=true,posAfter=at,
      detail="re-seat "+path+" "+V308(have.position)+" -> "+V308(at)});
     if(!k.Dry)have.SetPositionAndRotation(at,rot);
    }
    else k.Say("ok "+path+" at "+V308(have.position));
    return have;
   }
   k.Do(new SceneOp308{op="create",path=path,detail="create "+path+" at "+V308(at)+" yaw "+F308(yaw,"F1")+" from "+(src!=null?"scene "+Harness303.PathOf(src):"asset "+AssetDatabase.GetAssetPath(asset))});
   if(k.Dry)return null;
   GameObject go;
   if(src!=null){go=Object.Instantiate(src.gameObject);SceneManager.MoveGameObjectToScene(go,k.Scene);go.transform.SetParent(parent,true);go.transform.localScale=src.lossyScale;}
   else{go=(GameObject)PrefabUtility.InstantiatePrefab(asset,k.Scene);go.transform.SetParent(parent,true);}
   go.name=name;go.hideFlags=HideFlags.None;go.SetActive(true);
   if(part["scale"]!=null)go.transform.localScale*=part["scale"].Value<float>();
   go.transform.SetPositionAndRotation(at,rot);
   foreach(var cp in go.GetComponentsInChildren<WorldMacroContentPoint>(true))Object.DestroyImmediate(cp);
   return go.transform;
  }

  static bool SetWired308(WorldMacroPlaytestSession session,EnemyVitals vit,bool add)
  {
   var wiring=session.Walker!=null?session.Walker.Wiring:null;if(wiring==null||vit==null)throw new Refuse308("no CombatLoopWiring on the session walker");
   var so=new SerializedObject(wiring);var arr=so.FindProperty("_enemies");if(arr==null)throw new Refuse308("CombatLoopWiring._enemies not found");
   int at=-1;for(int i=0;i<arr.arraySize;i++)if(arr.GetArrayElementAtIndex(i).objectReferenceValue==vit){at=i;break;}
   if(add){if(at>=0)return false;arr.arraySize++;arr.GetArrayElementAtIndex(arr.arraySize-1).objectReferenceValue=vit;}
   else{if(at<0)return false;arr.GetArrayElementAtIndex(at).objectReferenceValue=null;arr.DeleteArrayElementAtIndex(at);}
   so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(wiring);return true;
  }
  static bool Wired308(WorldMacroPlaytestSession session,EnemyVitals vit)
  {
   var wiring=session.Walker!=null?session.Walker.Wiring:null;if(wiring==null||vit==null)return false;
   var arr=new SerializedObject(wiring).FindProperty("_enemies");if(arr==null)return false;
   for(int i=0;i<arr.arraySize;i++)if(arr.GetArrayElementAtIndex(i).objectReferenceValue==vit)return true;
   return false;
  }
 }
}
