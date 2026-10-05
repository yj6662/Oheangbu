using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 가도 주막 (D308-16b 답 1, D308-16c; relayout308_ext.json ops XI1-XI4, group G8c_road_inn) [TEST values throughout].
 // The hamlet's lived-in thatched house becomes the road inn and the #303 tablet commission's old man doubles as its host:
 //   XI1  content: Points[rest.id] (Rest) + Checkpoints[rest.id] (label, respawn feet + yaw, shop = 정비)
 //   XI2  content: Points[keeper.point].Services (쉬기 -> rest.id, 정비, the two Talk rows with the host lines) - nothing else of
 //        that point is written, and the keeper's commission must read back identical or the write is refused
 //   XI3  scene: ONE clone of an existing lantern (its one Point light = the inn's only new sustained light) under the root
 //        "RoadInn308", the content point marker on it, the session's InteractionVisuals entry
 //   XI4  seal profile: EaRestIds += rest.id
 // Every number, id, path and word comes from Art/World/Compact/Rebuild/CliffBoundary308/roadinn308.json; heights are the opened
 // scene's physical ground. Queue-safe: Run(string) never opens a dialog, refusals come back as "REFUSED ...".
 // Ledgers (Art/Playtest308/Pacing/ledger_roadinn308_*.json): one row per element with its op, its before- and after-form and a
 // byte backup of the file as it was on disk; a second identical apply reports "변경 없음" and writes nothing; a revert puts the
 // before-forms back newest first (" --op XI2" = that op alone). Only the edited asset / the target scene is saved
 // (SaveAssetIfDirty / SaveScene; never AssetDatabase.SaveAssets).
 //   status
 //   dry:<scene>                                    what content-apply and scene-apply would write; nothing is saved
 //   content-apply:<scene> | content-revert:<scene> [--op XI1|XI2] [--force]
 //   scene-apply:<scene>   | scene-revert:<scene>
 //   seal-apply            | seal-revert
 //   check:<scene>                                  AC-I1..I5, I8 (data + scene); read-only
 //   probe:<scene>                                  QI1 ground / capsule, QI2 hang gap, QI3 sight, QI5 resume ring; read-only
 // <scene> = an alias of the data's targets[] (arch296 | folk298 | main) or the scene's asset path. Order: content-apply of the
 // three scenes, scene-apply #296 -> #298 -> Main, seal-apply. A scene or content without the keeper point / a scene_requires
 // key is skipped with a note. No NavMesh bake here (the shared bake follows every scene ledger).
 public static partial class RoadInn308
 {
  const string Usage="status | dry:<scene> | content-apply:<scene> | content-revert:<scene> [--op XI1|XI2] [--force] | scene-apply:<scene> | scene-revert:<scene> | seal-apply | seal-revert | check:<scene> | probe:<scene>";
  static string DataFile=>Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","CliffBoundary308","roadinn308.json");
  static string OutDir=>Path.Combine(Harness303.RepoRoot,"Art","Playtest308","Pacing");
  sealed class Refuse:Exception{public Refuse(string m):base(m){}}

  public static string Run(string command)
  {
   string c=(command??"").Trim();
   try
   {
    if(c=="status")return Status();
    if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Edit mode only (Play is running)";
    bool force=c.EndsWith(" --force",StringComparison.Ordinal);if(force)c=c.Substring(0,c.Length-8).TrimEnd();
    string op=null;int oi=c.IndexOf(" --op ",StringComparison.Ordinal);
    if(oi>=0){op=c.Substring(oi+6).Trim();c=c.Substring(0,oi).TrimEnd();if(op!="XI1"&&op!="XI2")throw new Refuse("--op takes XI1 or XI2 (content-revert only)");}
    if((op!=null||force)&&!c.StartsWith("content-revert:",StringComparison.Ordinal))throw new Refuse("--op / --force are read by content-revert:<scene> only");
    string a;
    if((a=Arg(c,"dry:"))!=null)return ContentRun(a,true)+"\n"+SceneRun(a,true);
    if((a=Arg(c,"content-apply:"))!=null)return ContentRun(a,false);
    if((a=Arg(c,"content-revert:"))!=null)return ContentRevert(a,op,force);
    if((a=Arg(c,"scene-apply:"))!=null)return SceneRun(a,false);
    if((a=Arg(c,"scene-revert:"))!=null)return SceneRevert(a);
    if(c=="seal-apply")return SealRun(false);
    if(c=="seal-revert")return SealRun(true);
    if((a=Arg(c,"check:"))!=null)return Check(a);
    if((a=Arg(c,"probe:"))!=null)return Probe(a);
   }
   catch(Refuse r){return "REFUSED "+r.Message;}
   catch(Exception e){return "FAILED "+e;}
   return "REFUSED unknown RoadInn308 command '"+c+"' ("+Usage+")";
  }
  static string Arg(string c,string head)=>c.StartsWith(head,StringComparison.Ordinal)?c.Substring(head.Length).Trim():null;

  // ---------- data ----------

  sealed class Target{public string Alias,Scene,Content;}
  sealed class Cfg
  {
   public JObject J;public string Sha;public Target[] Targets;
   public JToken Rules=>J["rules"];public JToken Rest=>J["rest"];public JToken Keeper=>J["keeper"];public JToken Lantern=>J["lantern"];
   public string RestId=>Str(Rest,"id");public string KeeperId=>Str(Keeper,"point");public string RootName=>Str(Lantern,"root");
   public float R(string key)=>Num(Rules,key);
  }
  static JToken Req(JToken o,string key){var t=o?[key];if(t==null||t.Type==JTokenType.Null)throw new Refuse("roadinn308.json: missing '"+key+"' in "+(o==null?"(null)":string.IsNullOrEmpty(o.Path)?"(root)":o.Path));return t;}
  static float Num(JToken o,string key)=>Req(o,key).Value<float>();
  static string Str(JToken o,string key)=>Req(o,key).Value<string>();
  static string Opt(JToken o,string key){var t=o?[key];return t==null||t.Type==JTokenType.Null?null:t.Value<string>();}
  static bool Flag(JToken o,string key){var t=o?[key];return t!=null&&t.Type==JTokenType.Boolean&&t.Value<bool>();}
  static IEnumerable<JToken> Arr(JToken o,string key){var t=o?[key] as JArray;return t??(IEnumerable<JToken>)Array.Empty<JToken>();}
  static float At(JToken array,int i){var a=array as JArray;if(a==null||a.Count<=i)throw new Refuse("roadinn308.json: "+(array==null?"(null)":array.Path)+" needs "+(i+1)+" numbers");return a[i].Value<float>();}

  // parsed on every call (domain reload is off: nothing is kept between calls)
  static Cfg Load()
  {
   string f=DataFile;if(!File.Exists(f))throw new Refuse("data missing: "+f+" (copy Tools/Unity/Stage308_relayout_ext/Data/roadinn308.json there)");
   JObject j;try{j=JObject.Parse(File.ReadAllText(f));}catch(Exception e){throw new Refuse("data does not parse: "+f+" ("+e.Message+")");}
   var cfg=new Cfg{J=j,Sha=Harness303.Sha(f)};
   cfg.Targets=Arr(j,"targets").Select(t=>new Target{Alias=Str(t,"alias"),Scene=Str(t,"scene"),Content=Str(t,"content")}).ToArray();
   if(cfg.Targets.Length==0)throw new Refuse("roadinn308.json: targets[] is empty");
   foreach(var t in cfg.Targets)if(Harness303.IsProtected(t.Scene)||Harness303.IsProtected(t.Content)||PostLedger308.IsProtectedPath(t.Scene)||PostLedger308.IsProtectedPath(t.Content))throw new Refuse("protected target in data: "+t.Scene+" / "+t.Content);
   Req(j,"rules");Req(j,"rest");Req(j,"keeper");Req(j,"lantern");Req(j,"lines");Req(j,"text_rules");Req(j,"seal");
   return cfg;
  }
  static Target TargetOf(Cfg cfg,string arg)
  {
   arg=(arg??"").Trim().Replace('\\','/');
   var t=cfg.Targets.FirstOrDefault(x=>string.Equals(x.Alias,arg,StringComparison.OrdinalIgnoreCase)||x.Scene==arg);
   if(t==null)throw new Refuse("not a road inn target: '"+arg+"' (allowed: "+string.Join(", ",cfg.Targets.Select(x=>x.Alias))+")");
   return t;
  }

  // ---------- targets ----------

  static Scene Open(Target t)
  {
   for(int i=0;i<SceneManager.sceneCount;i++){var s=SceneManager.GetSceneAt(i);if(s.isDirty)throw new Refuse("scene "+s.path+" has unsaved changes - save or discard them first");}
   var active=SceneManager.GetActiveScene();
   return active.path==t.Scene&&SceneManager.sceneCount==1?active:EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);
  }
  static WorldMacroPlaytestSession Session(Scene s)
  {
   var all=s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).ToArray();
   if(all.Length!=1)throw new Refuse(s.path+" has "+all.Length+" WorldMacroPlaytestSession components (expected 1)");
   return all[0];
  }
  static WorldMacroPlaytestSO Content(Target t,WorldMacroPlaytestSession session)
  {
   var c=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(t.Content);if(c==null)throw new Refuse("missing content "+t.Content);
   if(session!=null&&session.Content!=c)throw new Refuse(t.Scene+" session Content is "+AssetDatabase.GetAssetPath(session.Content)+", expected "+t.Content);
   return c;
  }
  // a look preview (DontSave root) switches real renderers off while it is up: a scene save would write them switched off,
  // and opening another scene would orphan its restore list
  static void PreviewGuard(Cfg cfg,Target t,bool write)
  {
   var names=new HashSet<string>(Arr(cfg.Rules,"preview_roots").Select(x=>x.Value<string>()));
   var up=Resources.FindObjectsOfTypeAll<GameObject>().Where(g=>g!=null&&!EditorUtility.IsPersistent(g)&&g.transform.parent==null&&names.Contains(g.name)).Select(g=>g.name).Distinct().ToList();
   if(up.Count==0)return;
   var active=SceneManager.GetActiveScene();bool switches=!(active.path==t.Scene&&SceneManager.sceneCount==1);
   if(write)throw new Refuse("a look preview is up ("+string.Join(", ",up)+"): saving the scene now would write the renderers it switched off - run that tool's preview:off first");
   if(switches)throw new Refuse("a look preview is up ("+string.Join(", ",up)+") in "+active.path+": opening "+t.Scene+" would orphan its restore list - run that tool's preview:off first");
  }
  // null = the inn belongs in this scene; else why it is skipped (rows absent in a scene are skipped with a note, never an error)
  static string SkipReason(Cfg cfg,Scene scene,WorldMacroPlaytestSO content)
  {
   if(Point(content,cfg.KeeperId)==null)return "the content has no point "+cfg.KeeperId;
   foreach(var k in Arr(cfg.J,"scene_requires")){string key=k.Value<string>();var t=BuildingAudit308.Resolve(scene,key);if(t==null)return "the scene has no "+key;}
   return null;
  }
  static PrologueContentSO.Point Point(WorldMacroPlaytestSO c,string id)=>Array.Find(c.Points??Array.Empty<PrologueContentSO.Point>(),p=>p!=null&&p.Id==id);
  static WorldMacroPlaytestSO.CheckpointSpec Checkpoint(WorldMacroPlaytestSO c,string id)=>Array.Find(c.Checkpoints??Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>(),p=>p!=null&&p.Id==id);
  static string Key(Target t)=>t.Alias;
  static string F(float v,string f="F2")=>v.ToString(f,CultureInfo.InvariantCulture);
  static string V(Vector3 v)=>Harness303.V(v);
  static string Short(string sha)=>string.IsNullOrEmpty(sha)?"-":sha.Substring(0,Math.Min(12,sha.Length));

  // ---------- ledger / backup ----------

  // op = XI1..XI4; kind = point | checkpoint | services | seal (asset rows: before / after = the element's JSON, "" = absent) or
  // create | reseat | visual-add | note (scene rows: key = BuildingAudit308.KeyOf of the object, pos / euler / scale = local TRS before)
  [Serializable] sealed class Row{public string op="",kind="",id="",key="",before="",after="",detail="";public Vector3 pos,euler,scale;public bool reverted;}
  [Serializable] sealed class Write{public string utc="",target="",backup="",shaBefore="",shaAfter="",dataSha="",guard="";public List<Row> rows=new List<Row>();}
  [Serializable] sealed class Ledger{public string kind="roadinn308",group="",target="",created="";public List<Write> writes=new List<Write>();}
  static string LedgerFile(string key)=>Path.Combine(OutDir,"ledger_roadinn308_"+key+".json");
  static Ledger ReadLedger(string key){var f=LedgerFile(key);return File.Exists(f)?JsonUtility.FromJson<Ledger>(File.ReadAllText(f)):null;}
  static void WriteLedger(string key,Ledger l){Directory.CreateDirectory(OutDir);File.WriteAllText(LedgerFile(key),JsonUtility.ToJson(l,true));}
  static string Backup(string assetPath)
  {
   string dir=Path.Combine(OutDir,"Backups","roadinn308-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff",CultureInfo.InvariantCulture));Directory.CreateDirectory(dir);
   string copy=Path.Combine(dir,Path.GetFileName(assetPath));File.Copy(Harness303.Abs(assetPath),copy,true);return copy;
  }
  static IEnumerable<Row> Live(Ledger l)=>l==null?Enumerable.Empty<Row>():l.writes.Where(w=>w!=null&&w.rows!=null).SelectMany(w=>w.rows).Where(r=>r!=null&&!r.reverted);
  static string Report(string file,StringBuilder sb){Directory.CreateDirectory(OutDir);string f=Path.Combine(OutDir,file);File.WriteAllText(f,sb.ToString());return sb.Append("  report "+f).ToString();}

  // ---------- text rules (lines and row names: length limit, no instruction / key / UI words) ----------

  static string LineText(Cfg cfg,string id,List<string> bad)
  {
   var e=cfg.J["lines"]?[id];
   if(e==null||e.Type!=JTokenType.Object){bad.Add("line "+id+" is not in lines{}");return "";}
   if(Opt(e,"status")!="TEST")bad.Add("line "+id+" status is '"+Opt(e,"status")+"' (only TEST lines are written)");
   string text=Opt(e,"text")??"";TextRule(cfg,"line "+id,text,Mathf.RoundToInt(Num(cfg.J["text_rules"],"max_chars")),bad);return text;
  }
  static void TextRule(Cfg cfg,string what,string text,int max,List<string> bad)
  {
   var tr=cfg.J["text_rules"];
   if(text.Length==0)bad.Add(what+" is empty");
   if(text.Length>max)bad.Add(what+" has "+text.Length+" characters (> "+max+")");
   foreach(var w in Arr(tr,"banned")){string s=w.Value<string>();if(s.Length>0&&text.Contains(s))bad.Add(what+" holds the banned word '"+s+"'");}
   string rx=Opt(tr,"banned_regex");if(!string.IsNullOrEmpty(rx)&&Regex.IsMatch(text,rx))bad.Add(what+" matches banned_regex "+rx);
  }
  // the keeper's menu rows as the data says (texts checked; any violation refuses the whole apply)
  static PrologueContentSO.PointService306[] Services(Cfg cfg,List<string> bad)
  {
   var list=new List<PrologueContentSO.PointService306>();bool greet=Flag(cfg.Keeper,"rest_greeting");int labelMax=Mathf.RoundToInt(Num(cfg.J["text_rules"],"label_max_chars"));
   foreach(var s in Arr(cfg.Keeper,"services"))
   {
    string kind=Str(s,"kind"),label=Opt(s,"label")??"";if(label.Length>0)TextRule(cfg,"row name '"+label+"'",label,labelMax,bad);
    var row=new PrologueContentSO.PointService306{Label=label,Target=Opt(s,"target")??""};
    switch(kind)
    {
     case "Rest":row.Kind=PrologueContentSO.PointServiceKind306.Rest;row.Lines=greet?Arr(s,"greeting").Select(x=>LineText(cfg,x.Value<string>(),bad)).ToArray():Array.Empty<string>();break;
     case "Maintain":row.Kind=PrologueContentSO.PointServiceKind306.Maintain;break;
     case "Talk":row.Kind=PrologueContentSO.PointServiceKind306.Talk;row.Lines=Arr(s,"lines").Select(x=>LineText(cfg,x.Value<string>(),bad)).ToArray();
      if(row.Lines.Length==0)bad.Add("Talk row '"+label+"' has no lines");if(label.Length==0)bad.Add("a Talk row needs its row name");break;
     default:bad.Add("service kind '"+kind+"' (Rest | Maintain | Talk)");break;
    }
    list.Add(row);
   }
   return list.ToArray();
  }

  // ---------- ground (the opened scene's physics; actors, the player body, own / listed roots and previews never count) ----------

  static readonly HashSet<Collider> skip=new HashSet<Collider>();
  static readonly List<Transform> ignore=new List<Transform>();
  static void PrepareGround(Cfg cfg,Scene s,WorldMacroPlaytestSession session)
  {
   skip.Clear();ignore.Clear();   // refilled on every call (static scratch only; domain reload is off)
   foreach(var a in session.Actors??Array.Empty<PrologueEncounter>())if(a!=null)foreach(var c in a.GetComponentsInChildren<Collider>(true))skip.Add(c);
   if(session.Walker!=null&&session.Walker.Body!=null)foreach(var c in session.Walker.Body.GetComponentsInChildren<Collider>(true))skip.Add(c);
   // an NPC body is not ground either (the keeper stands 1.65 m from the feet)
   foreach(var m in s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroContentPoint>(true)))foreach(var c in m.GetComponentsInChildren<Collider>(true))skip.Add(c);
   var names=new HashSet<string>(Arr(cfg.Rules,"ground_ignore_roots").Select(x=>x.Value<string>()));
   foreach(var g in s.GetRootGameObjects())if(names.Contains(g.name))ignore.Add(g.transform);
   Physics.SyncTransforms();
  }
  static bool Preview(Transform t){for(var p=t;p!=null;p=p.parent)if((p.gameObject.hideFlags&HideFlags.DontSave)!=0)return true;return false;}
  static bool Usable(RaycastHit h)
  {
   var c=h.collider;if(c==null||h.normal.y<=.5f||c is CharacterController||skip.Contains(c)||Preview(c.transform))return false;
   foreach(var r in ignore)if(r!=null&&c.transform.IsChildOf(r))return false;
   return true;
  }
  static bool Terrain(Collider c)
  {
   if(c is TerrainCollider)return true;
   for(var t=c.transform;t!=null;t=t.parent){var n=t.name;if(n.Contains("Terrain")||n.Contains("Surface"))return true;}
   return false;
  }
  // the Content308 rule: the highest walkable support within 1.5 m over the terrain, else the highest walkable hit
  static bool Ground(float x,float z,out Vector3 p,out float slope)
  {
   p=default;slope=90f;float baseY=float.NegativeInfinity;
   var hits=Physics.RaycastAll(new Vector3(x,2000f,z),Vector3.down,4000f,~0,QueryTriggerInteraction.Ignore);
   foreach(var h in hits)if(Usable(h)&&Terrain(h.collider)&&h.point.y>baseY)baseY=h.point.y;
   if(float.IsNegativeInfinity(baseY))foreach(var h in hits)if(Usable(h)&&h.point.y>baseY)baseY=h.point.y;
   if(float.IsNegativeInfinity(baseY))return false;
   bool any=false;RaycastHit best=default;
   foreach(var h in hits){if(!Usable(h)||h.point.y<baseY-.05f||h.point.y>baseY+1.5f)continue;if(!any||h.point.y>best.point.y){best=h;any=true;}}
   p=best.point;slope=Vector3.Angle(best.normal,Vector3.up);return true;
  }
  // a pinned place: the row's XZ on the physical ground; over the slope limit refuses (the row is revised, the tool never looks
  // for another place); a place already written there is returned as it is (second apply = no change)
  static Vector3 Pinned(Cfg cfg,string what,float x,float z,float maxSlope,Vector3? had,float yOffline,List<string> notes)
  {
   if(!Ground(x,z,out var p,out float slope))throw new Refuse("no ground under "+what+" ("+F(x,"F1")+", "+F(z,"F1")+") - is the scene loaded?");
   if(slope>maxSlope)throw new Refuse(what+": ("+F(x,"F1")+", "+F(z,"F1")+") has a physical slope of "+F(slope,"F1")+"° > "+F(maxSlope,"F1")+"° - revise the row in "+DataFile+". Nothing written");
   if(had!=null&&Harness303.Flat(had.Value,p)<=cfg.R("pin_keep_m"))return had.Value;
   float dy=p.y-yOffline;
   notes.Add((Mathf.Abs(dy)>cfg.R("y_tol_m")?"WARN ":"")+what+" at "+V(p)+" (slope "+F(slope,"F1")+"°; offline y "+F(yOffline)+", Δ "+F(dy)+" m - the editor value is used)");
   return p;
  }

  // ---------- status ----------

  static string Status()
  {
   var sb=new StringBuilder("RoadInn308 status (play="+EditorApplication.isPlaying+")\n");
   Cfg cfg;try{cfg=Load();}catch(Refuse r){return sb.Append("  "+r.Message).ToString();}
   sb.AppendLine("  data "+DataFile+" sha "+Short(cfg.Sha)+" version "+(Opt(cfg.J,"version")??"?")+", group "+(Opt(cfg.J,"group")??""));
   foreach(var t in cfg.Targets)
   {
    var c=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(t.Content);
    string content=c==null?"content MISSING":"rest point "+(Point(c,cfg.RestId)!=null?"yes":"no")+", checkpoint "+(Checkpoint(c,cfg.RestId)!=null?"yes":"no")+", keeper rows "+(Point(c,cfg.KeeperId)?.Services?.Length.ToString()??"(no keeper)");
    sb.AppendLine("  "+t.Alias+": "+content+"; live ledger rows content "+Live(ReadLedger("content_"+Key(t))).Count()+", scene "+Live(ReadLedger("scene_"+Key(t))).Count());
   }
   var seal=AssetDatabase.LoadAssetAtPath<WorldSealProfileSO>(Str(cfg.J["seal"],"profile"));
   sb.AppendLine("  seal profile: "+(seal==null?"MISSING":"EaRestIds has "+cfg.RestId+": "+(seal.EaRestIds??Array.Empty<string>()).Contains(cfg.RestId))+"; live ledger rows "+Live(ReadLedger("seal")).Count());
   var active=SceneManager.GetActiveScene();return sb.Append("  active scene "+active.path+(active.isDirty?" (dirty)":"")).ToString();
  }
 }
}
