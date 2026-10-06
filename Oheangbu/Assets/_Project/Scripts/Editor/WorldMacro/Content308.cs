using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 컨텐츠 순서·타이밍·위치 — data authoring (SPEC-CONTENT-PACING-308 §1·3·4·5·6·7·8·9, D308-2) [TEST values throughout].
 // Queue-safe: Run(string) never opens a dialog; refusals come back as "REFUSED ...". Every write goes through one idempotent
 // ledger per target (Art/Playtest308/Pacing/ledger_campaign308.json, ledger_content308_<scene>.json) with a byte backup of the
 // file as it was on disk (Art/Playtest308/Pacing/Backups/<UTC>/). A second identical apply reports "변경 없음" and writes nothing;
 // revert copies the first (pristine) backup back byte for byte. Only the edited asset is saved (SaveAssetIfDirty, never SaveAssets).
 // Scene objects (compounds, 국 바위턱 site, actor moves / clones, dead lift sites, keep-outs) are the scene pass in
 // Content308.Scene*.cs (scene-dry | scene-apply | scene-revert | scene-check, lifts, guk-survey, keepout). Tree clearing and the
 // NavMesh bake are not in this class (one shared bake: CompactArchitecture296 nav, after every scene ledger).
 //   status
 //   campaign-apply | campaign-revert              shared campaign: cheongryong prerequisites, 정담 testimonies, office_report off,
 //                                                 Objective / Destination / Act 1 cleanup. Destinations read WorldContent_Main; a stage
 //                                                 whose point is not there yet (logging_inquiry) stays "pending": re-run campaign-apply
 //                                                 after content-apply:<W_Demo_Main> (it then changes only those)
 //   content-apply:<scene> | content-revert:<scene> content (points, 4 rests + checkpoints, encounters, BranchPath run, EscortVoice,
 //                                                 VehicleRequiredFact) + layout (Routes herb_amneung_cut308). Opens the scene for the
 //                                                 physical ground only; the scene itself is never modified or saved
 //   profiles                                      MineRangedNeutral308 (FireRanged306 copy, Elemental false) + EnemyVitals_FieldBoss308
 //   wire-profiles:<scene> | unwire-profiles:<scene> OPTIONAL (only if the scene pass does not): mine_fire/0 _attackProfile, agwi _profile
 //   checks:campaign | checks:content              data ACs (C1, C3 text, C4 fact, C7, C9, C10, C12 data, C14, C18 run count)
 // Scenes: the #296 candidate -> the #298 candidate -> W_Demo_Main (protected trees, W_Demo_Compact and 03_Content are refused).
 // D308-16 relayout (Content308.Relayout.cs, Content308.Paths.cs): with Art/World/Compact/Rebuild/CliffBoundary308/content308_relayout.json
 // present, rows of that file override / extend the constants (T1), move escort rests as a bundle (T5), regenerate the paths (T2),
 // add campaign destinations (T3) and group the ledger rows (T7: "<revert> --group <G>"). Without the file nothing here changes.
 //   relayout-status | content-dry:<scene> | campaign-dry | campaign-sig | paths-dry:<scene> | paths-regen:<scene> | paths-check:<scene>
 public static partial class Content308
 {
  const string MainScene="Assets/_Project/Scenes/World/W_Demo_Main.unity";
  const string Scene296="Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity";
  const string Scene298="Assets/_Project/Art/Characters/Folklore298/W_Demo_Compact_Folklore298.unity";
  static readonly string[] Scenes308={Scene296,Scene298,MainScene};
  // DemoCampaignProfile (Architecture296/Data/85ebc15b…_Campaign.asset) shared by the three contents
  const string CampaignGuid308="a78aec7dcfb0ffb42bf8ebbc4ec9bdde";
  // scene -> (its session Content, its layout); checked against the opened scene's session before any write
  static readonly Dictionary<string,(string content,string layout)> Targets308=new Dictionary<string,(string,string)>
  {
   {Scene296,("Assets/_Project/Art/World/Architecture296/Data/a3cb428dc79f23e45a4caf7729b52193_7a0b9698e5728bd4ea84bd5116570b9f_2b30a7723e5289c47a4fd3cde0d788cf_Content.asset",
              "Assets/_Project/Art/World/Architecture296/Data/211377a1cf4052f47905e2d4c976f7c4_566363b0e71d6e5499a8f6599e450127_782f53e1c87540e449a7951bb733ff7a_WorldLayout.asset")},
   {Scene298,("Assets/_Project/Art/Characters/Folklore298/Data/WorldContent298.asset","Assets/_Project/Art/Characters/Folklore298/Data/WorldLayout298.asset")},
   {MainScene,("Assets/_Project/Scenes/World/Main/WorldContent_Main.asset","Assets/_Project/Scenes/World/Main/WorldLayout_Main.asset")},
  };
  static string Out308=>Path.Combine(Harness303.RepoRoot,"Art","Playtest308","Pacing");
  static string Key308(string scene)=>Path.GetFileNameWithoutExtension(scene);
  static string F308(float v,string f="F2")=>v.ToString(f,CultureInfo.InvariantCulture);
  static string V308(Vector3 v)=>Harness303.V(v);
  sealed class Refuse308:Exception{public Refuse308(string m):base(m){}}

  public static string Run(string command)
  {
   string c=(command??"").Trim();
   // domain reload is off: nothing parsed from the relayout data survives from one call to the next
   relayoutCache308=null;relayoutRead308=false;argGroup308=null;argForce308=false;
   try
   {
    if(c=="status")return Status308();
    if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Edit mode only (Play is running)";
    if(c=="campaign-apply")return CampaignApply308();
    // revert refuses when an asset changed after this ledger's last write (another track / the scene pass wrote it later);
    // " --force" restores the pristine bytes anyway (that later change is then lost: revert the later writer first instead)
    bool force=c.EndsWith(" --force",StringComparison.Ordinal);if(force)c=c.Substring(0,c.Length-8).TrimEnd();
    // " --group <G>" (T7): a revert undoes only that group's rows, newest first (campaign-revert, content-revert, scene-revert)
    string group=null;int gi=c.IndexOf(" --group ",StringComparison.Ordinal);
    if(gi>=0){group=c.Substring(gi+9).Trim();c=c.Substring(0,gi).TrimEnd();if(group.Length==0)throw new Refuse308("--group needs a group key (relayout-status lists them)");}
    argGroup308=group;argForce308=force;
    if(group!=null&&c!="campaign-revert"&&!c.StartsWith("content-revert:",StringComparison.Ordinal)&&!c.StartsWith("scene-revert:",StringComparison.Ordinal))
     throw new Refuse308("--group is read by campaign-revert, content-revert:<scene> and scene-revert:<scene> only");
    if(c=="campaign-revert")return group!=null?RevertGroup308("campaign308",group,force):Revert308("campaign308",force);
    if(c.StartsWith("content-apply:",StringComparison.Ordinal))return ContentApply308(SceneArg308(c.Substring(14)));   // scene alias (arch296 | folk298 | main) or the full path, as content-dry / content-revert take it
    if(c.StartsWith("content-revert:",StringComparison.Ordinal)){string s=Scene308(SceneArg308(c.Substring(15)));return group!=null?RevertGroup308("content308_"+Key308(s),group,force):Revert308("content308_"+Key308(s),force);}
    // relayout (D308-16; Content308.Relayout.cs / Content308.Paths.cs); null = not one of those commands
    string relayout=RelayoutPass308(c);if(relayout!=null)return relayout;
    if(c=="profiles")return Profiles308();
    if(c.StartsWith("wire-profiles:",StringComparison.Ordinal))return WireProfiles308(c.Substring(14).Trim(),false);
    if(c.StartsWith("unwire-profiles:",StringComparison.Ordinal))return WireProfiles308(c.Substring(16).Trim(),true);
    if(c=="checks:campaign")return ChecksCampaign308();
    if(c=="checks:content")return ChecksContent308();
    // scene pass (Content308.Scene*.cs); null = not a scene-pass command
    string scenePass=ScenePass308(c);if(scenePass!=null)return scenePass;
   }
   catch(Refuse308 r){return "REFUSED "+r.Message;}
   catch(Exception e){return "FAILED "+e;}
   return "REFUSED unknown Content308 command '"+c+"' (status | campaign-apply | campaign-revert [--force] | content-apply:<scene> | content-revert:<scene> [--force] | profiles | wire-profiles:<scene> | unwire-profiles:<scene> | checks:campaign | checks:content | "+RelayoutUsage308+" | "+ScenePassUsage308+")";
  }

  // ---------- targets ----------

  static string Scene308(string path)
  {
   path=(path??"").Trim().Replace('\\','/');
   if(Harness303.IsProtected(path))throw new Refuse308("protected path "+path);
   if(!Scenes308.Contains(path))throw new Refuse308("not a #308 target: "+path+" (allowed: "+string.Join(", ",Scenes308)+")");
   return path;
  }
  static Scene Open308(string path)
  {
   path=Scene308(path);
   for(int i=0;i<SceneManager.sceneCount;i++){var s=SceneManager.GetSceneAt(i);if(s.isDirty)throw new Refuse308("scene "+s.path+" has unsaved changes - save or discard them first");}
   var active=SceneManager.GetActiveScene();
   return active.path==path&&SceneManager.sceneCount==1?active:EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
  }
  static WorldMacroPlaytestSession Session308(Scene s)
  {
   var all=s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).ToArray();
   if(all.Length!=1)throw new Refuse308(s.path+" has "+all.Length+" WorldMacroPlaytestSession components (expected 1)");
   return all[0];
  }
  static DemoCampaignProfile Campaign308()
  {
   string path=AssetDatabase.GUIDToAssetPath(CampaignGuid308);
   var p=string.IsNullOrEmpty(path)?null:AssetDatabase.LoadAssetAtPath<DemoCampaignProfile>(path);
   if(p==null)throw new Refuse308("campaign guid "+CampaignGuid308+" does not resolve to a DemoCampaignProfile");
   if(Harness303.IsProtected(path))throw new Refuse308("campaign under a protected path "+path);
   return p;
  }
  static T Load308<T>(string path)where T:Object
  {
   if(Harness303.IsProtected(path))throw new Refuse308("protected path "+path);
   var a=AssetDatabase.LoadAssetAtPath<T>(path);if(a==null)throw new Refuse308("missing "+typeof(T).Name+" "+path);return a;
  }

  // ---------- ledger / backup ----------

  // rows (T7): one row per element this write changed (point / checkpoint / encounter / place / route / stage / mainpath / branchpath)
  // with its serialized form before and after ("" = absent) and the group of the data row that asked for it ("" = the #308 base).
  // A group revert puts the before-forms back, newest first, and marks the rows reverted; the whole-ledger revert still copies the
  // pristine bytes back. Ledgers written before this field read as "no rows" (only the whole-ledger revert works on those writes).
  [Serializable] sealed class Row308{public string group="",kind="",id="",before="",after="";public bool reverted;}
  [Serializable] sealed class Write308{public string utc="",asset="",backup="",shaBefore="",shaAfter="";public List<string> changes=new List<string>();public List<Row308> rows=new List<Row308>();}
  [Serializable] sealed class Ledger308
  {
   public string kind="",scene="",created="";
   public List<string> assets=new List<string>(),pristine=new List<string>(),pristineSha=new List<string>();
   public List<Write308> writes=new List<Write308>();
  }
  static string LedgerFile308(string key)=>Path.Combine(Out308,"ledger_"+key+".json");
  static Ledger308 ReadLedger308(string key){var f=LedgerFile308(key);return File.Exists(f)?JsonUtility.FromJson<Ledger308>(File.ReadAllText(f)):null;}
  static void WriteLedger308(string key,Ledger308 l){Directory.CreateDirectory(Out308);File.WriteAllText(LedgerFile308(key),JsonUtility.ToJson(l,true));}
  static string BackupDir308()=>Path.Combine(Out308,"Backups",DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff",CultureInfo.InvariantCulture));

  // Runs `edit` on the loaded asset. An unchanged serialized form writes nothing (null). A change first copies the file as it is on
  // disk into backupDir (the asset's first copy in this ledger is its pristine one), then saves only this asset. An exception
  // inside `edit` restores the in-memory object. An asset that is already dirty (another session's unsaved edit) is refused.
  static Write308 Edit308(Object asset,string backupDir,Ledger308 ledger,Func<List<string>> edit,bool recordRows=true)
  {
   string path=AssetDatabase.GetAssetPath(asset);
   if(EditorUtility.IsDirty(asset))throw new Refuse308(path+" has unsaved in-memory changes (another session?) - save or discard them first");
   string before=EditorJsonUtility.ToJson(asset);List<string> changes;var snapshot=recordRows?Snapshot308(asset):null;
   try{changes=edit();}
   catch{EditorJsonUtility.FromJsonOverwrite(before,asset);throw;}
   if(EditorJsonUtility.ToJson(asset)==before)return null;
   string abs=Harness303.Abs(path);string copy;Write308 w;
   // everything up to the save can still fail (backup copy, row grouping of a bad data row): the in-memory object then goes back
   // to what is on disk, so no edited-but-unsaved, unledgered asset is left behind
   try
   {
    Directory.CreateDirectory(backupDir);
    copy=Path.Combine(backupDir,Path.GetFileName(path));File.Copy(abs,copy,true);
    w=new Write308{utc=DateTime.UtcNow.ToString("O"),asset=path,backup=copy,shaBefore=Harness303.Sha(abs),changes=changes??new List<string>()};
    if(snapshot!=null)w.rows=DiffRows308(snapshot,Snapshot308(asset));
   }
   catch{EditorJsonUtility.FromJsonOverwrite(before,asset);throw;}
   if(!ledger.assets.Contains(path)){ledger.assets.Add(path);ledger.pristine.Add(copy);ledger.pristineSha.Add(w.shaBefore);}
   EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset);
   w.shaAfter=Harness303.Sha(abs);ledger.writes.Add(w);return w;
  }

  static string Revert308(string key,bool force)
  {
   var l=ReadLedger308(key);if(l==null)throw new Refuse308("no ledger "+LedgerFile308(key)+" (nothing applied, or already reverted)");
   var drift=new List<string>();
   for(int i=0;i<l.assets.Count;i++)
   {
    if(Harness303.IsProtected(l.assets[i]))throw new Refuse308("protected path in ledger "+l.assets[i]);
    if(!File.Exists(l.pristine[i]))throw new Refuse308("pristine backup missing "+l.pristine[i]);
    var loaded=AssetDatabase.LoadMainAssetAtPath(l.assets[i]);if(loaded!=null&&EditorUtility.IsDirty(loaded))throw new Refuse308(l.assets[i]+" has unsaved in-memory changes");
    var last=l.writes.LastOrDefault(w=>w!=null&&w.asset==l.assets[i]);string now=Harness303.Sha(Harness303.Abs(l.assets[i]));
    if(last!=null&&!string.IsNullOrEmpty(last.shaAfter)&&now!=last.shaAfter)drift.Add(l.assets[i]);
   }
   if(drift.Count>0&&!force)throw new Refuse308("changed after this ledger's last write (another writer, e.g. the scene pass): "+string.Join(", ",drift)+
    " - revert that writer first, or append ' --force' to restore the pristine bytes anyway");
   var sb=new StringBuilder(key+" (revert)\n");
   foreach(var d in drift)sb.AppendLine("  WARN forced over a later change to "+d);
   for(int i=0;i<l.assets.Count;i++)
   {
    string abs=Harness303.Abs(l.assets[i]);File.Copy(l.pristine[i],abs,true);AssetDatabase.ImportAsset(l.assets[i],ImportAssetOptions.ForceUpdate);
    bool same=Harness303.Sha(abs)==l.pristineSha[i];
    sb.AppendLine("  "+l.assets[i]+" <- "+l.pristine[i]+(same?" (bytes identical to the pristine backup)":" (WARN sha differs after import)"));
   }
   string archived=LedgerFile308(key)+".reverted-"+Harness303.UtcStamp();File.Move(LedgerFile308(key),archived);
   return sb.Append("  ledger archived "+archived).ToString();
  }

  static string Report308(string title,List<Write308> writes,List<string> notes,string ledgerKey)
  {
   var sb=new StringBuilder(title+"\n");
   var real=writes.Where(w=>w!=null).ToList();
   if(real.Count==0)sb.AppendLine("  변경 없음 (nothing written)");
   foreach(var w in real){sb.AppendLine("  wrote "+w.asset+" (backup "+w.backup+")");foreach(var ch in w.changes)sb.AppendLine("    "+ch);}
   foreach(var n in notes)sb.AppendLine("  note "+n);
   sb.AppendLine("  ledger "+LedgerFile308(ledgerKey)+(File.Exists(LedgerFile308(ledgerKey))?"":" (none)"));
   Directory.CreateDirectory(Out308);File.WriteAllText(Path.Combine(Out308,ledgerKey+"-last.txt"),sb.ToString());
   return sb.ToString();
  }

  // small setters that log what they change
  static void Set308(List<string> ch,string what,ref string field,string value){if(string.Equals(field??"",value??"",StringComparison.Ordinal))return;ch.Add(what+": \""+Clip308(field)+"\" -> \""+Clip308(value)+"\"");field=value;}
  static void Set308(List<string> ch,string what,ref float field,float value){if(Mathf.Abs(field-value)<1e-4f)return;ch.Add(what+": "+F308(field)+" -> "+F308(value));field=value;}
  static void Set308(List<string> ch,string what,ref bool field,bool value){if(field==value)return;ch.Add(what+": "+field+" -> "+value);field=value;}
  static void Set308(List<string> ch,string what,ref Vector3 field,Vector3 value){if((field-value).sqrMagnitude<1e-6f)return;ch.Add(what+": "+V308(field)+" -> "+V308(value));field=value;}
  static void Set308(List<string> ch,string what,ref string[] field,string[] value){if((field??Array.Empty<string>()).SequenceEqual(value))return;ch.Add(what+": ["+string.Join(",",field??Array.Empty<string>())+"] -> ["+string.Join(",",value)+"]");field=value;}
  static string Clip308(string s){s=(s??"").Replace("\n","\\n");return s.Length>48?s.Substring(0,48)+"…":s;}

  // ---------- status ----------

  static string Status308()
  {
   var sb=new StringBuilder("Content308 status (play="+EditorApplication.isPlaying+")\n");
   try{var c=Campaign308();var cr=c.FindStage("cheongryong");sb.AppendLine("  campaign "+AssetDatabase.GetAssetPath(c)+" valid "+c.IsValid+", cheongryong prerequisites ["+string.Join(",",cr?.PrerequisiteIds??Array.Empty<string>())+"]");}
   catch(Exception e){sb.AppendLine("  campaign: "+e.Message);}
   sb.AppendLine("  ledger campaign308: "+(File.Exists(LedgerFile308("campaign308"))?"present":"none"));
   foreach(var s in Scenes308)
   {
    var t=Targets308[s];var content=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(t.content);
    sb.AppendLine("  "+Key308(s)+": content "+(content==null?"MISSING":"VehicleRequiredFact '"+content.VehicleRequiredFact+"', rests308 "+RestsNow308().Count(r=>content.Checkpoints.Any(cp=>cp!=null&&cp.Id==r.Id))+"/"+RestsNow308().Length)+
                  ", ledger "+(File.Exists(LedgerFile308("content308_"+Key308(s)))?"present":"none"));
   }
   var active=SceneManager.GetActiveScene();sb.Append("  active scene "+active.path+(active.isDirty?" (dirty)":""));
   return sb.ToString();
  }

  // ---------- ground (the opened scene's physics; actors, the player body and the enclosure / seal roots never count) ----------

  static readonly HashSet<Collider> skip308=new HashSet<Collider>();
  static readonly List<Transform> ignore308=new List<Transform>();
  static void PrepareGround308(Scene s,WorldMacroPlaytestSession session)
  {
   skip308.Clear();ignore308.Clear();
   foreach(var a in session.Actors??Array.Empty<Oheangbu.App.Prologue.PrologueEncounter>())if(a!=null)foreach(var c in a.GetComponentsInChildren<Collider>(true))skip308.Add(c);
   if(session.Walker!=null&&session.Walker.Body!=null)foreach(var c in session.Walker.Body.GetComponentsInChildren<Collider>(true))skip308.Add(c);
   // the scene pass's own root never counts as ground (a re-run would seat things on the altar it placed last time)
   foreach(var g in s.GetRootGameObjects())if(g.name=="Enclosure305"||g.name=="Seal308"||g.name==SceneRootDefault308)ignore308.Add(g.transform);
   // relayout (only with content308_relayout.json): the escort rest benches stand on their content point and never count as ground
   RelayoutGroundSkips308(s);
   Physics.SyncTransforms();
  }
  static bool Usable308(RaycastHit h)
  {
   var c=h.collider;if(c==null||h.normal.y<=.5f||c is CharacterController||skip308.Contains(c))return false;
   // another session's in-memory preview (CliffLook308 / WallLook308: DontSave objects) is not the saved world
   for(var t=c.transform;t!=null;t=t.parent)if((t.gameObject.hideFlags&HideFlags.DontSave)!=0)return false;
   foreach(var r in ignore308)if(r!=null&&c.transform.IsChildOf(r))return false;
   return true;
  }
  // terrain = TerrainCollider or a *Terrain* / *Surface* object (Enclosure305 rule); the result is the highest walkable support
  // within 1.5 m over that terrain (decks, flat stones: Support297 rule), else the highest walkable hit when no terrain answers
  static bool Terrain308(Collider c)
  {
   if(c is TerrainCollider)return true;
   for(var t=c.transform;t!=null;t=t.parent){var n=t.name;if(n.Contains("Terrain")||n.Contains("Surface"))return true;}
   return false;
  }
  static bool Ground308(float x,float z,out Vector3 p,out float slope)
  {
   p=default;slope=90f;float baseY=float.NegativeInfinity;
   var hits=Physics.RaycastAll(new Vector3(x,2000f,z),Vector3.down,4000f,~0,QueryTriggerInteraction.Ignore);
   foreach(var h in hits)if(Usable308(h)&&Terrain308(h.collider)&&h.point.y>baseY)baseY=h.point.y;
   if(float.IsNegativeInfinity(baseY))foreach(var h in hits)if(Usable308(h)&&h.point.y>baseY)baseY=h.point.y;
   if(float.IsNegativeInfinity(baseY))return false;
   bool any=false;RaycastHit best=default;
   foreach(var h in hits){if(!Usable308(h)||h.point.y<baseY-.05f||h.point.y>baseY+1.5f)continue;if(!any||h.point.y>best.point.y){best=h;any=true;}}
   p=best.point;slope=Vector3.Angle(best.normal,Vector3.up);return true;
  }
  // grounded candidate; above maxSlope the flattest ground within `reach` (2 m rings, 12 bearings, nearest ring first) is used
  static Vector3 Flat308(string id,float x,float z,float maxSlope,float reach,List<string> notes)
  {
   if(!Ground308(x,z,out var p,out float slope))throw new Refuse308("no ground under "+id+" ("+F308(x,"F0")+", "+F308(z,"F0")+") - is the scene loaded?");
   if(slope<=maxSlope)return p;
   for(float r=2f;r<=reach+.01f;r+=2f)
   {
    Vector3 best=default;float bestSlope=float.MaxValue;
    for(int k=0;k<12;k++){float a=k*30f*Mathf.Deg2Rad;if(Ground308(x+Mathf.Cos(a)*r,z+Mathf.Sin(a)*r,out var q,out float s)&&s<=maxSlope&&s<bestSlope){best=q;bestSlope=s;}}
    if(bestSlope<float.MaxValue){notes.Add(id+": candidate slope "+F308(slope,"F1")+"°, moved "+F308(r,"F0")+" m to "+V308(best)+" ("+F308(bestSlope,"F1")+"°)");return best;}
   }
   notes.Add("WARN "+id+": nothing ≤ "+F308(maxSlope,"F0")+"° within "+F308(reach,"F0")+" m; candidate kept (slope "+F308(slope,"F1")+"°)");
   return p;
  }
 }
}
