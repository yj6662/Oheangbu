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
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 D308-19 새 글 (SPEC-CONTENT-PACING-308 "글 T0-T4", group G14_texts) [TEST text throughout].
 //   XT0  campaign: Stages[deep_forest].Objective / .DestinationLabel - exactly these two fields, variant A | B of the data
 //   XT1  campaign: Testimonies += the 긁힌 돌바닥 text shown while 청룡 is still closed (Testimony.UntilStageOpen)
 //        content:  Points[hunter_innkeeper308].Services += two Talk rows
 //   XT2  content:  Points += mine_notice308 (Evidence, four pages); scene: holder + marker + InteractionVisuals on the board
 //   XT3  content:  Points += sinmok_rope_keeper308 (Conversation, two Talk rows); campaign: Testimonies += the after-kill line;
 //        scene: one clone of an existing body + ground props
 //   XT4  content:  Points += south_gate_potter308 (Conversation, one Talk row); scene: one clone + jars
 // Every word, id, path and number comes from Art/World/Compact/Rebuild/CliffBoundary308/texts308.json (stage copy:
 // Tools/Unity/Stage308_texts19/Data/texts308.json); a line that breaks text_rules refuses every command at load. Heights are
 // the opened scene's physical ground. Queue-safe: Run(string) never opens a dialog, refusals come back as "REFUSED ...".
 // Ledgers (Art/Playtest308/Pacing/ledger_texts308_*.json, group G14_texts): one row per element with its op, before- and
 // after-form and a byte backup of the file as it was; the journal row is written BEFORE the save; a second identical apply
 // reports "변경 없음" and writes nothing; a revert puts the before-forms back newest first (" --op XT2" = that set alone).
 // Only the edited asset / the target scene is saved (SaveAssetIfDirty / SaveScene; never AssetDatabase.SaveAssets).
 //   status
 //   dry:<scene>                    what campaign-apply, content-apply and scene-apply would write; nothing is saved
 //   campaign-apply | campaign-revert [--op XT0|XT1|XT3] [--force]
 //   content-apply:<scene> | content-revert:<scene> [--op XT1..XT4] [--force]
 //   scene-apply:<scene>   | scene-revert:<scene> [--op XT2..XT4]
 //   check:<scene>                  AC-T1..T9 (data, campaign, content, scene); read-only
 //   probe:<scene>                  read-only: renderer roots within rules.probe_radius_m of the T0 stage Destination (variant
 //                                  A / B evidence). campaign-apply writes the two stage fields only after probe:<rules.probe_required>.
 // <scene> = an alias of the data's targets[] (arch296 | folk298 | main) or the scene's asset path. Order: campaign-apply,
 // content-apply of the three scenes, scene-apply #296 -> #298 -> Main, check x3. Group revert = scene-revert x3 (Main first),
 // content-revert x3, campaign-revert. No NavMesh bake: nothing here is an agent or carves the mesh (see DEPLOY_PLAN_texts.md).
 public static partial class Texts308
 {
  const string Usage="status | dry:<scene> | campaign-apply | campaign-revert [--op XT0|XT1|XT3] [--force] | content-apply:<scene> | content-revert:<scene> [--op XTn] [--force] | scene-apply:<scene> | scene-revert:<scene> [--op XTn] | check:<scene> | probe:<scene>";
  static string DataFile=>Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","CliffBoundary308","texts308.json");
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
    if(oi>=0){op=c.Substring(oi+6).Trim();c=c.Substring(0,oi).TrimEnd();if(!Regex.IsMatch(op,"^XT[0-4]$"))throw new Refuse("--op takes XT0 .. XT4");}
    bool revert=c=="campaign-revert"||c.StartsWith("content-revert:",StringComparison.Ordinal)||c.StartsWith("scene-revert:",StringComparison.Ordinal);
    if((op!=null||force)&&!revert)throw new Refuse("--op / --force are read by the revert commands only");
    string a;
    if((a=Arg(c,"dry:"))!=null)return CampaignRun(true)+"\n"+ContentRun(a,true)+"\n"+SceneRun(a,true);
    if(c=="campaign-dry")return CampaignRun(true);
    if(c=="campaign-apply")return CampaignRun(false);
    if(c=="campaign-revert")return CampaignRevert(op,force);
    if((a=Arg(c,"content-apply:"))!=null)return ContentRun(a,false);
    if((a=Arg(c,"content-revert:"))!=null)return ContentRevert(a,op,force);
    if((a=Arg(c,"scene-apply:"))!=null)return SceneRun(a,false);
    if((a=Arg(c,"scene-revert:"))!=null)return SceneRevert(a,op);
    if((a=Arg(c,"check:"))!=null)return Check(a);
    if((a=Arg(c,"probe:"))!=null)return Probe(a);
   }
   catch(Refuse r){return "REFUSED "+r.Message;}
   catch(Exception e){return "FAILED "+e;}
   finally{skip.Clear();ignore.Clear();}   // the ground scratch holds no Collider / Transform between calls
   return "REFUSED unknown Texts308 command '"+c+"' ("+Usage+")";
  }
  static string Arg(string c,string head)=>c.StartsWith(head,StringComparison.Ordinal)?c.Substring(head.Length).Trim():null;

  // ---------- data ----------

  sealed class Target{public string Alias,Scene,Content;}
  sealed class Cfg
  {
   public JObject J;public string Sha;public Target[] Targets;public readonly Dictionary<string,string> Text=new Dictionary<string,string>(StringComparer.Ordinal);
   public JToken Rules=>J["rules"];public string RootName=>Str(J,"root");public string Group=>Str(J,"group");public string Join=>Str(J,"evidence_page_join");
   public float R(string key)=>Num(Rules,key);
   public string T(string id){if(id==null||!Text.TryGetValue(id,out var t))throw new Refuse("texts308.json: line '"+id+"' is not in lines{}");return t;}
  }
  static JToken Req(JToken o,string key){var t=o?[key];if(t==null||t.Type==JTokenType.Null)throw new Refuse("texts308.json: missing '"+key+"' in "+(o==null?"(null)":string.IsNullOrEmpty(o.Path)?"(root)":o.Path));return t;}
  static float Num(JToken o,string key)=>Req(o,key).Value<float>();
  static string Str(JToken o,string key)=>Req(o,key).Value<string>();
  static string Opt(JToken o,string key){var t=o?[key];return t==null||t.Type==JTokenType.Null?null:t.Value<string>();}
  static float OptNum(JToken o,string key,float fallback){var t=o?[key];return t==null||t.Type==JTokenType.Null?fallback:t.Value<float>();}
  static IEnumerable<JToken> Arr(JToken o,string key){var t=o?[key] as JArray;return t??(IEnumerable<JToken>)Array.Empty<JToken>();}
  static string[] Ids(JToken o,string key)=>Arr(o,key).Select(x=>x.Value<string>()).ToArray();
  static float At(JToken array,int i){var a=array as JArray;if(a==null||a.Count<=i)throw new Refuse("texts308.json: "+(array==null?"(null)":array.Path)+" needs "+(i+1)+" numbers");return a[i].Value<float>();}

  // parsed on every call (domain reload is off: nothing is kept between calls). Text rules run here: one bad line refuses all.
  static Cfg Load()
  {
   string f=DataFile;if(!File.Exists(f))throw new Refuse("data missing: "+f+" (copy Tools/Unity/Stage308_texts19/Data/texts308.json there)");
   JObject j;try{j=JObject.Parse(File.ReadAllText(f));}catch(Exception e){throw new Refuse("data does not parse: "+f+" ("+e.Message+")");}
   var cfg=new Cfg{J=j,Sha=Harness303.Sha(f)};
   cfg.Targets=Arr(j,"targets").Select(t=>new Target{Alias=Str(t,"alias"),Scene=Str(t,"scene"),Content=Str(t,"content")}).ToArray();
   if(cfg.Targets.Length==0)throw new Refuse("texts308.json: targets[] is empty");
   string camp=Str(j,"campaign");
   foreach(var p in cfg.Targets.SelectMany(t=>new[]{t.Scene,t.Content}).Concat(new[]{camp}))if(Harness303.IsProtected(p)||PostLedger308.IsProtectedPath(p))throw new Refuse("protected target in data: "+p);
   Req(j,"rules");Req(j,"text_rules");Req(j,"campaign_rows");Req(j,"root");Req(j,"group");Req(j,"evidence_page_join");
   var tr=j["text_rules"];var bad=new List<string>();
   var lines=Req(j,"lines") as JObject;if(lines==null)throw new Refuse("texts308.json: lines is not an object");
   foreach(var kv in lines)
   {
    var e=kv.Value;if(e==null||e.Type!=JTokenType.Object){bad.Add("line "+kv.Key+" is not an object");continue;}
    string text=Opt(e,"text")??"",kind=Opt(e,"kind")??"line";
    if(Opt(e,"status")!="TEST")bad.Add("line "+kv.Key+" status is '"+Opt(e,"status")+"' (only TEST lines are written)");
    string maxKey=kind=="line"?"max_chars":kind=="row"?"label_max_chars":kind+"_max_chars";
    TextRule(tr,kind+" "+kv.Key,text,Mathf.RoundToInt(Num(tr,maxKey)),bad);cfg.Text[kv.Key]=text;
   }
   if(bad.Count>0)throw new Refuse("text rules: "+string.Join("; ",bad)+" - revise "+f+". Nothing read, nothing written");
   Structure(cfg);
   return cfg;
  }
  // which owner (point / keeper / stage) may carry the lines of a decision bullet (owners{}), and the decision's order inside
  // one row / page list: a line under another speaker, swapped pages or a row on another NPC refuses every command.
  static void Structure(Cfg cfg)
  {
   var j=cfg.J;var owners=Req(j,"owners") as JObject;if(owners==null)throw new Refuse("texts308.json: owners is not an object");
   var lines=(JObject)j["lines"];var bad=new List<string>();
   void Group(string owner,string what,IEnumerable<string> ids)
   {
    string tag=null;int last=-1;
    foreach(string id in ids)
    {
     var src=id!=null?lines[id]?["src"] as JArray:null;
     if(src==null||src.Count!=2){bad.Add(what+" of "+owner+": line '"+id+"' carries no src (a derived or unknown line cannot stand here)");return;}
     string t=src[0].Value<string>();int n=src[1].Value<int>();
     if(tag==null){tag=t;if(!Ids(owners,t).Contains(owner))bad.Add(what+" of "+owner+": owners["+t+"] does not name it");}
     else if(t!=tag)bad.Add(what+" of "+owner+": lines of two decision bullets ("+tag+", "+t+")");
     if(n<=last)bad.Add(what+" of "+owner+": line '"+id+"' is out of the decision's order");
     last=n;
    }
   }
   var cr=j["campaign_rows"];
   foreach(var v in (Req(cr,"variants") as JObject)?.Properties()??Enumerable.Empty<JProperty>())Group(Str(cr,"stage"),"variant "+v.Name,new[]{Opt(v.Value,"Objective"),Opt(v.Value,"DestinationLabel")}.Where(x=>x!=null));
   foreach(var t in Arr(j,"testimonies"))Group(Str(t,"trigger"),"testimony "+Str(t,"id"),Ids(t,"pages"));
   foreach(var k in Arr(j,"keepers"))foreach(var r in Arr(k,"rows"))Group(Str(k,"point"),"keeper row",new[]{Str(r,"label")}.Concat(Ids(r,"lines")));
   foreach(var p in Arr(j,"points"))
   {
    string id=Str(p,"id");
    if(Str(p,"kind")=="Evidence")Group(id,"prompt + pages",new[]{Str(p,"prompt")}.Concat(Ids(p,"pages")));
    else Group(id,"speaker + first words",new[]{Str(p,"speaker")}.Concat(Ids(p,"first")));
    foreach(var r in Arr(p,"rows"))Group(id,"row",new[]{Str(r,"label")}.Concat(Ids(r,"lines")));
    var labels=Arr(p,"rows").Select(r=>Str(r,"label")).ToArray();if(labels.Length>1)Group(id,"row names",labels);
   }
   foreach(var n in Arr(j,"people"))
   {
    var seat=Ids(n,"reseat_children");var gone=Ids(n,"remove_children");
    foreach(string c in seat)if(gone.Contains(c))bad.Add(Str(n,"id")+": child "+c+" is both re-seated and removed");
    foreach(var pr in Arr(n,"props")){string on=Opt(pr,"on");if(on!=null&&!seat.Contains(on))bad.Add(Str(n,"id")+": prop "+Str(pr,"name")+" rides '"+on+"', which is not a re-seated child");}
   }
   if(bad.Count>0)throw new Refuse("structure: "+string.Join("; ",bad)+" - revise "+DataFile+". Nothing read, nothing written");
  }
  static void TextRule(JToken tr,string what,string text,int max,List<string> bad)
  {
   if(text.Length==0)bad.Add(what+" is empty");
   if(text.Length>max)bad.Add(what+" has "+text.Length+" characters (> "+max+")");
   foreach(var w in Arr(tr,"banned")){string s=w.Value<string>();if(s.Length>0&&text.Contains(s))bad.Add(what+" holds the banned word '"+s+"'");}
   foreach(var w in Arr(tr,"banned_endings")){string s=w.Value<string>();if(s.Length>0&&text.Contains(s))bad.Add(what+" holds the banned ending '"+s+"'");}
   string rx=Opt(tr,"banned_regex");if(!string.IsNullOrEmpty(rx)&&Regex.IsMatch(text,rx))bad.Add(what+" matches banned_regex "+rx);
   if(text!=text.Trim()||text.Contains("\n")||text.Contains("\r"))bad.Add(what+" has a line break or outer blanks");
  }
  static Target TargetOf(Cfg cfg,string arg)
  {
   arg=(arg??"").Trim().Replace('\\','/');
   var t=cfg.Targets.FirstOrDefault(x=>string.Equals(x.Alias,arg,StringComparison.OrdinalIgnoreCase)||x.Scene==arg);
   if(t==null)throw new Refuse("not a texts308 target: '"+arg+"' (allowed: "+string.Join(", ",cfg.Targets.Select(x=>x.Alias))+")");
   return t;
  }
  // the pages of an examine text / a first word as ONE string (Point.Text, Testimony.Text)
  static string Joined(Cfg cfg,JToken o,string key,bool evidence)=>string.Join(evidence?cfg.Join:"\n\n",Ids(o,key).Select(cfg.T));
  static PrologueContentSO.PointService306[] TalkRows(Cfg cfg,JToken o)=>Arr(o,"rows").Select(r=>new PrologueContentSO.PointService306
   {Kind=PrologueContentSO.PointServiceKind306.Talk,Label=cfg.T(Str(r,"label")),Target="",Lines=Ids(r,"lines").Select(cfg.T).ToArray()}).ToArray();

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
  static DemoCampaignProfile Campaign(Cfg cfg)
  {
   string path=Str(cfg.J,"campaign");var p=AssetDatabase.LoadAssetAtPath<DemoCampaignProfile>(path);if(p==null)throw new Refuse("missing campaign "+path);
   foreach(var t in cfg.Targets){var c=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(t.Content);if(c!=null&&c.Campaign!=null&&c.Campaign!=p)throw new Refuse(t.Content+" plays another campaign ("+AssetDatabase.GetAssetPath(c.Campaign)+") than the data's "+path);}
   return p;
  }
  // a look preview (DontSave root) switches real renderers off while it is up: a scene save would write them switched off
  static void PreviewGuard(Cfg cfg,Target t,bool write)
  {
   var names=new HashSet<string>(Ids(cfg.Rules,"preview_roots"));
   var up=Resources.FindObjectsOfTypeAll<GameObject>().Where(g=>g!=null&&!EditorUtility.IsPersistent(g)&&g.transform.parent==null&&names.Contains(g.name)).Select(g=>g.name).Distinct().ToList();
   if(up.Count==0)return;
   var active=SceneManager.GetActiveScene();bool switches=!(active.path==t.Scene&&SceneManager.sceneCount==1);
   if(write)throw new Refuse("a look preview is up ("+string.Join(", ",up)+"): saving the scene now would write the renderers it switched off - run that tool's preview:off first");
   if(switches)throw new Refuse("a look preview is up ("+string.Join(", ",up)+") in "+active.path+": opening "+t.Scene+" would orphan its restore list - run that tool's preview:off first");
  }
  static PrologueContentSO.Point Point(WorldMacroPlaytestSO c,string id)=>Array.Find(c.Points??Array.Empty<PrologueContentSO.Point>(),p=>p!=null&&p.Id==id);
  static string F(float v,string f="F2")=>v.ToString(f,CultureInfo.InvariantCulture);
  static string V(Vector3 v)=>Harness303.V(v);
  static string Short(string sha)=>string.IsNullOrEmpty(sha)?"-":sha.Substring(0,Math.Min(12,sha.Length));

  // ---------- ledger / backup ----------

  // kind = stage-field | testimony (campaign), point | services (content), create | reseat | visual-add | note (scene: key =
  // (a note row changes only an object or an InteractionVisuals entry that a create / visual-add row of this ledger made)
  // BuildingAudit308.KeyOf of the object, pos / euler / scale = local TRS before). before / after = the element's form, "" = absent.
  [Serializable] sealed class Row{public string op="",kind="",id="",key="",before="",after="",detail="";public Vector3 pos,euler,scale;public bool reverted;}
  [Serializable] sealed class Write{public string utc="",target="",backup="",shaBefore="",shaAfter="",dataSha="",guard="";public List<Row> rows=new List<Row>();}
  [Serializable] sealed class Ledger{public string kind="texts308",group="",target="",created="";public List<Write> writes=new List<Write>();}
  static string LedgerFile(string key)=>Path.Combine(OutDir,"ledger_texts308_"+key+".json");
  static Ledger ReadLedger(string key){var f=LedgerFile(key);return File.Exists(f)?JsonUtility.FromJson<Ledger>(File.ReadAllText(f)):null;}
  static void WriteLedger(string key,Ledger l){Directory.CreateDirectory(OutDir);File.WriteAllText(LedgerFile(key),JsonUtility.ToJson(l,true));}
  static string Backup(string assetPath)
  {
   string dir=Path.Combine(OutDir,"Backups","texts308-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff",CultureInfo.InvariantCulture));Directory.CreateDirectory(dir);
   string copy=Path.Combine(dir,Path.GetFileName(assetPath));File.Copy(Harness303.Abs(assetPath),copy,true);return copy;
  }
  static IEnumerable<Row> Live(Ledger l)=>l==null?Enumerable.Empty<Row>():l.writes.Where(w=>w!=null&&w.rows!=null).SelectMany(w=>w.rows).Where(r=>r!=null&&!r.reverted);
  static string Report(string file,StringBuilder sb){Directory.CreateDirectory(OutDir);string f=Path.Combine(OutDir,file);File.WriteAllText(f,sb.ToString());return sb.Append("  report "+f).ToString();}
  // one asset write: journal row first (an empty shaAfter = "save started, not confirmed"), then SaveAssetIfDirty of that asset only
  static string SaveAsset(Cfg cfg,Object asset,string path,string key,List<Row> rows,string guard,string memory,StringBuilder sb)
  {
   Write w;Ledger ledger;
   try
   {
    ledger=ReadLedger(key)??new Ledger{group=cfg.Group,target=path,created=DateTime.UtcNow.ToString("O")};
    w=new Write{utc=DateTime.UtcNow.ToString("O"),target=path,backup=Backup(path),shaBefore=Harness303.Sha(Harness303.Abs(path)),dataSha=cfg.Sha,guard=BuildingAudit308.ShaText(guard),rows=rows};
    ledger.writes.Add(w);WriteLedger(key,ledger);
   }
   catch(Exception e){EditorJsonUtility.FromJsonOverwrite(memory,asset);return "FAILED the backup / ledger "+LedgerFile(key)+" could not be written ("+e.Message+"); nothing saved (the asset in memory was put back)";}
   EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset);
   w.shaAfter=Harness303.Sha(Harness303.Abs(path));
   if(w.shaAfter==w.shaBefore)
   {
    // rows were planned, yet the file on disk is byte for byte what it was: the save did not happen (locked / read-only file).
    // Nothing may stay half done: the object in memory goes back (and is no longer dirty, so no later SaveAssets flush writes
    // it), the journal row comes out.
    EditorJsonUtility.FromJsonOverwrite(memory,asset);EditorUtility.ClearDirty(asset);ledger.writes.Remove(w);string led="";
    try{WriteLedger(key,ledger);}catch(Exception e){led=" WARN the journal row could not be taken out of "+LedgerFile(key)+" ("+e.Message+"): it stands with an empty shaAfter.";}
    return "FAILED "+path+" was not written: sha "+Short(w.shaBefore)+" before and after SaveAssetIfDirty. The asset in memory was put back, the journal row taken out."+led+" Backup "+w.backup;
   }
   try{WriteLedger(key,ledger);}catch(Exception e){sb.AppendLine("  WARN saved and journalled, but shaAfter could not be recorded ("+e.Message+")");}
   foreach(var r in rows)sb.AppendLine("  did ["+r.op+"]: "+r.detail);
   sb.AppendLine("  saved "+path+" (sha "+Short(w.shaBefore)+" -> "+Short(w.shaAfter)+"; backup "+w.backup+")");
   sb.AppendLine("  guard sha "+Short(w.guard)+" (unchanged by this write); ledger "+LedgerFile(key)+" group "+cfg.Group);
   return null;
  }

  // ---------- ground (the opened scene's physics; actors, the player body, NPC bodies, own / listed roots and previews never count) ----------

  // editor-only scratch of ONE call: filled by PrepareGround, emptied when Run returns (never read across calls)
  static readonly HashSet<Collider> skip=new HashSet<Collider>();
  static readonly List<Transform> ignore=new List<Transform>();
  static void PrepareGround(Cfg cfg,Scene s,WorldMacroPlaytestSession session)
  {
   skip.Clear();ignore.Clear();   // refilled on every call (static scratch only; domain reload is off)
   foreach(var a in session.Actors??Array.Empty<PrologueEncounter>())if(a!=null)foreach(var c in a.GetComponentsInChildren<Collider>(true))skip.Add(c);
   if(session.Walker!=null&&session.Walker.Body!=null)foreach(var c in session.Walker.Body.GetComponentsInChildren<Collider>(true))skip.Add(c);
   foreach(var m in s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroContentPoint>(true)))foreach(var c in m.GetComponentsInChildren<Collider>(true))skip.Add(c);
   var names=new HashSet<string>(Ids(cfg.Rules,"ground_ignore_roots"));
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
  // the Content308 rule, with its own figures (ray from y 2000 down 4000, support window -0.05 .. +1.5 m over the terrain,
  // walkable = normal.y > 0.5): the highest walkable support within 1.5 m over the terrain, else the highest walkable hit.
  // These are the figures of Content308.Ground kept equal on purpose - a point seated by either tool reads the same ground.
  // cover = metres to the lowest walkable hit ABOVE that support (a rock shell over the terrain), +inf = open sky.
  static bool Ground(float x,float z,out Vector3 p,out float slope,out float cover,out string coverName)
  {
   p=default;slope=90f;cover=float.PositiveInfinity;coverName="";float baseY=float.NegativeInfinity;
   var hits=Physics.RaycastAll(new Vector3(x,2000f,z),Vector3.down,4000f,~0,QueryTriggerInteraction.Ignore);
   foreach(var h in hits)if(Usable(h)&&Terrain(h.collider)&&h.point.y>baseY)baseY=h.point.y;
   if(float.IsNegativeInfinity(baseY))foreach(var h in hits)if(Usable(h)&&h.point.y>baseY)baseY=h.point.y;
   if(float.IsNegativeInfinity(baseY))return false;
   bool any=false;RaycastHit best=default;
   foreach(var h in hits){if(!Usable(h)||h.point.y<baseY-.05f||h.point.y>baseY+1.5f)continue;if(!any||h.point.y>best.point.y){best=h;any=true;}}
   p=best.point;slope=Vector3.Angle(best.normal,Vector3.up);
   foreach(var h in hits){if(!Usable(h)||h.point.y<=p.y+.05f)continue;float d=h.point.y-p.y;if(d<cover){cover=d;coverName=Harness303.PathOf(h.collider.transform);}}
   return true;
  }
  static float PathDistance(Vector3[] path,Vector3 at,float gap)
  {
   float best=float.PositiveInfinity;if(path==null)return best;var q=new Vector2(at.x,at.z);
   for(int i=1;i<path.Length;i++)
   {
    var a=new Vector2(path[i-1].x,path[i-1].z);var b=new Vector2(path[i].x,path[i].z);var ab=b-a;float len=ab.magnitude;if(len>gap||len<1e-4f)continue;
    float t=Mathf.Clamp01(Vector2.Dot(q-a,ab)/(len*len));best=Mathf.Min(best,Vector2.Distance(q,a+ab*t));
   }
   return best;
  }
  // every clearance of a point row against the content, as "name value [ok|FAIL]" lines; returns false when one fails
  static bool Clear(Cfg cfg,JToken spec,WorldMacroPlaytestSO c,Vector3 at,List<string> lines)
  {
   var k=spec["clear"];bool ok=true;string id=Str(spec,"id");if(k==null)return true;
   void Say(string name,float value,bool pass,string rule){lines.Add(name+" "+F(value,"F1")+" m "+rule+(pass?" [ok]":" [FAIL]"));if(!pass)ok=false;}
   WorldMacroPlaytestSO.Encounter E(string eid)=>Array.Find(c.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>(),e=>e!=null&&e.Id==eid);
   string boss=Opt(k,"boss");
   if(boss!=null){var e=E(boss);if(e==null)lines.Add("boss "+boss+" is not in this content [info]");else Say("boss "+boss,Harness303.Flat(at,e.Feet),Harness303.Flat(at,e.Feet)>=Num(k,"boss_min_m"),">= "+F(Num(k,"boss_min_m"),"F0"));}
   foreach(string eid in Ids(k,"enemies"))
   {
    var e=E(eid);if(e==null){lines.Add("enemy "+eid+" is not in this content [info]");continue;}
    float d=Harness303.Flat(at,e.Feet);
    if(k["enemies_min_m"]!=null)Say("enemy "+eid+" (detection "+F(e.Detection,"F0")+", leash "+F(e.Leash,"F0")+")",d,d>=Num(k,"enemies_min_m")&&d>e.Detection,">= "+F(Num(k,"enemies_min_m"),"F0")+" and outside its detection");
    if(k["enemy_stand_min_m"]!=null)
    {
     float stand=d;foreach(var pp in e.Patrol??Array.Empty<Vector3>())stand=Mathf.Min(stand,Harness303.Flat(at,pp));
     Say("enemy "+eid+" stand / patrol (detection "+F(e.Detection,"F0")+": the point is "+(d<=e.Detection?"INSIDE":"outside")+" it)",stand,stand>=Num(k,"enemy_stand_min_m"),">= "+F(Num(k,"enemy_stand_min_m"),"F0"));
    }
   }
   if(k["rests_min_m"]!=null)
   {
    float best=float.PositiveInfinity;string who="";
    foreach(var cp in c.Checkpoints??Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>()){if(cp==null||!cp.IsConfigured)continue;float d=Harness303.Flat(at,cp.Feet);if(d<best){best=d;who="feet "+cp.Id;}}
    foreach(var p in c.Points??Array.Empty<PrologueContentSO.Point>()){if(p==null||p.Kind!=PrologueInteractionKind.Rest)continue;float d=Harness303.Flat(at,p.Position);if(d<best){best=d;who="rest "+p.Id;}}
    Say("nearest rest ("+who+")",best,best>=Num(k,"rests_min_m"),">= "+F(Num(k,"rests_min_m"),"F0"));
   }
   if(k["points_min_m"]!=null)
   {
    float best=float.PositiveInfinity;string who="";
    foreach(var p in c.Points??Array.Empty<PrologueContentSO.Point>()){if(p==null||p.Id==id)continue;float d=Harness303.Flat(at,p.Position);if(d<best){best=d;who=p.Id;}}
    Say("nearest other point ("+who+")",best,best>=Num(k,"points_min_m"),">= "+F(Num(k,"points_min_m"),"F0")+" (two prompts never share a spot)");
   }
   if(k["road_min_m"]!=null)
   {
    float gap=cfg.R("road_gap_m");float d=Mathf.Min(PathDistance(c.MainPath,at,gap),PathDistance(c.BranchPath,at,gap));
    Say("road centre line (content MainPath / BranchPath)",d,d>=Num(k,"road_min_m")&&d<=OptNum(k,"road_max_m",float.PositiveInfinity),"in ["+F(Num(k,"road_min_m"),"F1")+", "+F(OptNum(k,"road_max_m",999f),"F1")+"]");
   }
   return ok;
  }
  // the first candidate place that stands: physical slope within the row's limit and every clearance kept. A point already
  // written within pin_keep_m of a candidate is returned as it is (second apply = no change). None = refused.
  static Vector3 Pick(Cfg cfg,JToken spec,WorldMacroPlaytestSO c,Vector3? had,List<string> notes)
  {
   string id=Str(spec,"id");float maxSlope=Num(spec,"max_slope_deg");var why=new List<string>();
   foreach(var place in Arr(spec,"places"))
   {
    float x=Num(place,"x"),z=Num(place,"z");string at="("+F(x,"F1")+", "+F(z,"F1")+")";
    if(!Ground(x,z,out var p,out float slope,out float cover,out string coverName)){why.Add(at+": no ground");continue;}
    if(slope>maxSlope){why.Add(at+": physical slope "+F(slope,"F1")+"° > "+F(maxSlope,"F1")+"°");continue;}
    var lines=new List<string>();
    if(!Clear(cfg,spec,c,p,lines)){why.Add(at+": "+string.Join("; ",lines.Where(l=>l.EndsWith("[FAIL]",StringComparison.Ordinal))));continue;}
    float dy=p.y-OptNum(place,"y_offline",p.y);
    notes.Add((Mathf.Abs(dy)>cfg.R("y_tol_m")?"WARN ":"")+"point "+id+" at "+V(p)+" (slope "+F(slope,"F1")+"°; offline y "+F(OptNum(place,"y_offline",p.y))+", Δ "+F(dy)+" m - the editor value is used)");
    if(cover<=cfg.R("cover_probe_up_m"))notes.Add("WARN point "+id+": a walkable collider "+F(cover)+" m above the chosen ground ("+coverName+") - the place may be under a shell: look at the still before keeping it");
    foreach(var l in lines)notes.Add("point "+id+": "+l);
    if(why.Count>0)notes.Add("point "+id+": earlier candidate(s) passed over - "+string.Join(" | ",why));
    return had!=null&&Harness303.Flat(had.Value,p)<=cfg.R("pin_keep_m")?had.Value:p;
   }
   throw new Refuse("point "+id+": no candidate place stands - "+string.Join(" | ",why)+". Revise places[] in "+DataFile+". Nothing written");
  }

  // ---------- status / probe ----------

  static string Status()
  {
   var sb=new StringBuilder("Texts308 status (play="+EditorApplication.isPlaying+")\n");
   Cfg cfg;try{cfg=Load();}catch(Refuse r){return sb.Append("  "+r.Message).ToString();}
   sb.AppendLine("  data "+DataFile+" sha "+Short(cfg.Sha)+" version "+(Opt(cfg.J,"version")??"?")+", group "+cfg.Group+", "+cfg.Text.Count+" lines (text rules ok)");
   var camp=AssetDatabase.LoadAssetAtPath<DemoCampaignProfile>(Str(cfg.J,"campaign"));var cr=cfg.J["campaign_rows"];
   if(camp==null)sb.AppendLine("  campaign MISSING");
   else
   {
    var st=camp.Stages!=null?Array.Find(camp.Stages,s=>s!=null&&s.Id==Str(cr,"stage")):null;
    sb.AppendLine("  campaign: "+Str(cr,"stage")+" Objective '"+(st?.Objective??"(no stage)")+"' / DestinationLabel '"+(st?.DestinationLabel??"")+"' (data variant "+Str(cr,"variant")+"); testimonies of ours "+
     Arr(cfg.J,"testimonies").Count(t=>FindTestimony(camp,t)>=0)+"/"+Arr(cfg.J,"testimonies").Count()+"; live ledger rows "+Live(ReadLedger("campaign")).Count());
    int lost=Live(ReadLedger("campaign")).Count(r=>!CampaignHolds(camp,r));
    if(lost>0)sb.AppendLine("  WARN campaign: "+lost+" live ledger row(s) are NOT in the asset any more (another tool put the file back, e.g. Content308 campaign-revert --force): close the ledger with campaign-revert --force, then campaign-apply again");
    string alias=Opt(cfg.Rules,"probe_required");
    if(alias!=null){string pf=Path.Combine(OutDir,"texts308_probe_"+alias+".txt");sb.AppendLine("  T0 probe report "+(File.Exists(pf)?"present ("+File.GetLastWriteTimeUtc(pf).ToString("O")+")":"ABSENT: campaign-apply refuses the two stage fields until probe:"+alias+" has been run"));}
   }
   foreach(var t in cfg.Targets)
   {
    var c=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(t.Content);
    string content=c==null?"content MISSING":"points "+string.Join(", ",Arr(cfg.J,"points").Select(p=>Str(p,"id")+"="+(Point(c,Str(p,"id"))!=null?"yes":"no")))+"; "+
     string.Join(", ",Arr(cfg.J,"keepers").Select(k=>Str(k,"point")+" rows "+(Point(c,Str(k,"point"))?.Services?.Length.ToString()??"(no point)")));
    sb.AppendLine("  "+t.Alias+": "+content+"; live ledger rows content "+Live(ReadLedger("content_"+t.Alias)).Count()+", scene "+Live(ReadLedger("scene_"+t.Alias)).Count());
    int lost=c==null?0:Live(ReadLedger("content_"+t.Alias)).Count(r=>(r.kind=="point"||r.kind=="services")&&Current(c,r)!=r.after);
    if(lost>0)sb.AppendLine("  WARN "+t.Alias+": "+lost+" live content ledger row(s) are NOT in the asset any more (Content308 content-apply rewrote the keeper's rows, or a content-revert --force put the file back): content-apply:"+t.Alias+" again (a rewritten keeper), or content-revert:"+t.Alias+" --force then content-apply");
   }
   var active=SceneManager.GetActiveScene();return sb.Append("  active scene "+active.path+(active.isDirty?" (dirty)":"")).ToString();
  }

  // T0 evidence, read-only: what stands within rules.probe_radius_m of the lesson stage's Destination
  static string Probe(string sceneArg)
  {
   var cfg=Load();var t=TargetOf(cfg,sceneArg);PreviewGuard(cfg,t,false);var scene=Open(t);var camp=Campaign(cfg);var cr=cfg.J["campaign_rows"];
   var st=Array.Find(camp.Stages,s=>s!=null&&s.Id==Str(cr,"stage"));if(st==null)throw new Refuse("stage "+Str(cr,"stage")+" is not in the campaign");
   float radius=cfg.R("probe_radius_m");
   var sb=new StringBuilder("probe "+t.Scene+": renderers within "+F(radius,"F0")+" m of "+st.Id+".Destination "+V(st.Destination)+"\n");
   var seen=new SortedDictionary<string,(float d,int n)>(StringComparer.Ordinal);
   foreach(var r in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(false)))
   {
    float d=Harness303.Flat(r.bounds.center,st.Destination);if(d>radius)continue;
    var top=r.transform;while(top.parent!=null&&top.parent.parent!=null&&top.parent.parent.parent!=null)top=top.parent;   // root / group / object
    string key=Harness303.PathOf(top);seen[key]=seen.TryGetValue(key,out var had)?(Mathf.Min(had.d,d),had.n+1):(d,1);
   }
   foreach(var kv in seen.OrderBy(k=>k.Value.d))sb.AppendLine("  "+F(kv.Value.d,"F1")+" m  "+kv.Key+"  ("+kv.Value.n+" renderer(s))");
   sb.AppendLine("  rule: "+(Opt(cr,"confirm")??""));
   sb.AppendLine("  data variant now: "+Str(cr,"variant")+" (baked vegetation sheets are not scene objects: see the still)");
   if(scene.isDirty)sb.AppendLine("  WARN the scene is dirty after a read-only pass (bug): reload it");
   return Report("texts308_probe_"+t.Alias+".txt",sb);
  }
 }
}
