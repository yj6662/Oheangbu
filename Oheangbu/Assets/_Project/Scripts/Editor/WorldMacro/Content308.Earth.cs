using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // D308-16b 답 2 / D308-16c - 가도의 토 잡몹 3체 (relayout308_ext.json XE1-XE6, group G9_earth_road) [TEST].
 // A pass of its own beside Content308.Run: its own entry (Earth), its own data file (earth308.json) and its own ledgers, so the
 // group can be applied and reverted alone and the BASE relayout files are not edited.
 //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.Content308 Earth "<command>"
 //   status                                   data sha, profiles, content rows, ledgers (read only; works in Play)
 //   profiles-dry | profiles | profiles-revert XE1: EarthRoad308_Slow / _Gate = copies of EarthHeavy with telegraph / damage from data.
 //                                            Saves only its own two assets (SaveAssetIfDirty). The source stays byte-identical.
 //   content-dry:<scene> | content-apply:<scene> | content-revert:<scene> [--force]
 //                                            XE2-XE4 content half: Encounters[earth_road308/n] on the physical ground. Ledger
 //                                            Art/Playtest308/Pacing/ledger_earth308_content_<scene>.json (rows before / after, byte
 //                                            backup). The revert removes exactly the rows this ledger wrote.
 //   scene-dry:<scene> | scene-apply:<scene> | scene-revert:<scene>      (Content308.EarthScene.cs)
 //                                            XE2-XE4 scene half + XE5 profile wiring + XE6 crystal shape, one save per scene.
 //   check | scene-check:<scene>              AC-E1 .. AC-E6 (data / scene half; read only)
 // <scene> = arch296 | folk298 | main or the asset path. Order: profiles -> content-apply x3 -> scene-apply x3 (#296 -> #298 -> Main).
 // Refusals are strings (no dialog). Play, a dirty scene, protected targets and a look preview refuse. Numbers and asset paths come
 // from the data file; nothing here is a tuning constant.
 //
 // D308-18 답 5 (목 속성 짐승 셋, group G12_wood_beasts): the same pass serves a second family.
 //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.Content308 Wood "<command>"     (wood308.json)
 // A family = entry name + data file + ledger prefix (ledger_<family>308_..., <family>308_content_<scene>, <family>308_scene_<scene>).
 // The op names in the scene ledgers (earth-enc / earth-ref / earth-mesh / earth-flag / earth-radius) are the vocabulary of this pass
 // for every family: the ledgers the earth group already wrote stay readable. What the wood data adds (all optional, the earth file
 // has none of it, so the earth family behaves as before):
 //   encounters[].existing = true   the row names an actor that already stands in the scene: nothing is created, moved or re-linked and
 //                                  its content row is not written; detection / leash / speed / ... are the values the design READ
 //                                  (check judges the content against them = "behaviour unchanged")
 //   profiles.set_element           the copies carry this element (Elemental = true) instead of the source's
 //   vitals / runtime absent        EnemyVitals._profile and the session switch are left as they are
 //   organ.grow_radius = true       the organ sphere grows to cover a larger crystal shape (never shrinks)
 public static partial class Content308
 {
  sealed class ElementFamily308{public string Name="",Entry="",FileName="",StageCopy="";}
  static readonly ElementFamily308 EarthFamily308=new ElementFamily308{Name="earth",Entry="Earth",FileName="earth308.json",StageCopy="Tools/Unity/Stage308_relayout_ext/Data/earth308.json"};
  static readonly ElementFamily308 WoodFamily308=new ElementFamily308{Name="wood",Entry="Wood",FileName="wood308.json",StageCopy="Tools/Unity/Stage308_world18/wood/Data/wood308.json"};
  // editor only; set by the entry of each call and put back before it returns (no state survives a call)
  static ElementFamily308 family308=EarthFamily308;

  const string EarthUsage308="status | profiles-dry | profiles | profiles-revert | content-dry:<scene> | content-apply:<scene> | content-revert:<scene> [--force] | scene-dry:<scene> | scene-apply:<scene> | scene-revert:<scene> | check | scene-check:<scene>";
  static string EarthFile308=>Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","CliffBoundary308",family308.FileName);
  static string EarthContentKey308(string scene)=>family308.Name+"308_content_"+Key308(scene);
  static string EarthSceneKey308(string scene)=>family308.Name+"308_scene_"+Key308(scene);
  static string EarthProfileLedger308=>Path.Combine(Out308,"ledger_"+family308.Name+"308_profiles.json");

  public static string Earth(string command)=>Element308(EarthFamily308,command);
  public static string Wood(string command)=>Element308(WoodFamily308,command);
  static string Element308(ElementFamily308 family,string command)
  {
   family308=family;
   try{return ElementRun308(command);}
   finally{family308=EarthFamily308;}
  }

  static string ElementRun308(string command)
  {
   string c=(command??"").Trim();
   // domain reload is off: nothing parsed in an earlier call survives (the same reset Run does)
   relayoutCache308=null;relayoutRead308=false;argGroup308=null;argForce308=false;
   try
   {
    if(c=="status")return EarthStatus308();
    if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Edit mode only (Play is running)";
    bool force=c.EndsWith(" --force",StringComparison.Ordinal);if(force)c=c.Substring(0,c.Length-8).TrimEnd();
    string Arg(string head)=>c.StartsWith(head,StringComparison.Ordinal)?SceneArg308(c.Substring(head.Length)):null;
    string a;
    if(c=="profiles-dry")return EarthProfiles308(true);
    if(c=="profiles")return EarthProfiles308(false);
    if(c=="profiles-revert")return EarthProfilesRevert308();
    if(c=="check")return EarthCheck308();
    if((a=Arg("content-dry:"))!=null)return EarthContent308(a,true);
    if((a=Arg("content-apply:"))!=null)return EarthContent308(a,false);
    if((a=Arg("content-revert:"))!=null){var d=EarthData308();return RevertGroup308(EarthContentKey308(Scene308(a)),d.Group,force);}
    if((a=Arg("scene-dry:"))!=null)return EarthScene308(a,true);
    if((a=Arg("scene-apply:"))!=null)return EarthScene308(a,false);
    if((a=Arg("scene-revert:"))!=null)return EarthSceneRevert308(a);
    if((a=Arg("scene-check:"))!=null)return EarthSceneCheck308(a);
   }
   catch(Refuse308 r){return "REFUSED "+r.Message;}
   catch(Exception e){return "FAILED "+e;}
   return "REFUSED unknown Content308."+family308.Entry+" command '"+c+"' ("+EarthUsage308+")";
  }

  // ---------- data ----------

  sealed class EarthRow308
  {
   public string Id="",Op="",Template="",ContentId="",Attack="";
   public float X,Z,Yaw,P2X,P2Z,Speed,Detection,Leash,Activation,PreferredDistance,Scale=1f,RoadMin,YOffline=float.NaN;
   public bool Ranged,Respawn,Existing;public HashSet<string> Scenes=new HashSet<string>();
  }
  sealed class EarthSet308
  {
   public JObject Data;public string Sha="",Group="",ProfileDir="",ProfileSource="",Vitals="",OrganBody="",OrganElement="",ElementSet="",Element="";
   public float FeetMaxSlope,Keep,NavRadius,NavXzTol,GapMax,ScaleTol,OrganK,OrganKTol,PlaceTol;
   public bool Enabled,GrowRadius;
   // "enabled": false switches the whole group off: nothing is applied any more (dry runs, checks and the reverts still work)
   public void NeedOn(){if(!Enabled)throw new Refuse308(family308.Name+" data: 'enabled' is false in "+EarthFile308+" (the group is switched off) - nothing applied");}
   public readonly List<EarthRow308> Rows=new List<EarthRow308>();
   public IEnumerable<EarthRow308> RowsOf(string scene)=>Rows.Where(r=>r.Scenes.Contains(PostLedger308.Short(scene)));
   public string AttackPath(EarthRow308 r)=>ProfileDir+"/"+r.Attack+".asset";
  }

  static EarthSet308 EarthData308()
  {
   string f=EarthFile308;
   if(!File.Exists(f))throw new Refuse308(family308.Name+" data missing: "+f+" (copy "+family308.StageCopy+" there)");
   JObject j;
   try{j=JObject.Parse(File.ReadAllText(f));}
   catch(Exception e){throw new Refuse308(family308.Name+" data does not parse: "+f+" ("+e.Message+")");}
   var rules=Req308(j,"rules");var prof=Req308(j,"profiles");var organ=Req308(j,"organ");
   var d=new EarthSet308{Data=j,Enabled=Flag308(j,"enabled"),Sha=Harness303.Sha(f),Group=Str308(j,"group"),ProfileDir=Str308(prof,"dir").TrimEnd('/'),ProfileSource=Str308(prof,"source"),Vitals=Opt308(j,"vitals")??"",
    OrganBody=Str308(organ,"body"),OrganElement=Str308(organ,"element"),OrganK=Num308(Req308(organ,"fit"),"k"),OrganKTol=Num308(organ,"k_tol"),
    FeetMaxSlope=Num308(rules,"feet_max_slope_deg"),Keep=Num308(rules,"keep_m"),NavRadius=Num308(rules,"nav_radius_m"),NavXzTol=Num308(rules,"nav_xz_tol_m"),GapMax=Num308(rules,"ground_gap_max_m"),ScaleTol=Num308(rules,"scale_tol")};
   if(d.Group.Length==0)throw new Refuse308(family308.Name+" data: 'group' is empty");
   // review 2, finding 2: the crystal shape and the attack profile name ONE element
   d.PlaceTol=Has308(rules,"existing_place_tol_m")?Num308(rules,"existing_place_tol_m"):d.Keep;
   d.GrowRadius=Flag308(organ,"grow_radius");
   d.ElementSet=Opt308(prof,"set_element")??"";
   if(d.ElementSet.Length>0&&(!Enum.TryParse(d.ElementSet,out Element setTo)||!Enum.IsDefined(typeof(Element),setTo)||setTo.ToString()!=d.ElementSet))
    throw new Refuse308(family308.Name+" data: profiles.set_element '"+d.ElementSet+"' is not one of "+string.Join(", ",Enum.GetNames(typeof(Element))));
   {string pe=d.ElementSet.Length>0?d.ElementSet:Flag308(Req308(prof,"expect"),"elemental")?Str308(Req308(prof,"expect"),"element"):"";
    d.Element=pe.Length>0?pe:OrganSurface308.ShardElements[0];
    if(pe!=d.OrganElement)throw new Refuse308(family308.Name+" data: organ.element '"+d.OrganElement+"' is not the element of the profiles (profiles.set_element / profiles.expect.element '"+pe+"') - crystal shape = actor element (SPEC-TELEGRAPH-ORGAN-308)");}
   foreach(var p in new[]{d.ProfileDir,d.ProfileSource,d.Vitals})if(p.Length>0&&Harness303.IsProtected(p))throw new Refuse308(family308.Name+" data names a protected path "+p);
   if(!d.ProfileDir.StartsWith("Assets/",StringComparison.Ordinal))throw new Refuse308(family308.Name+" data: profiles.dir must be an Assets/ folder ("+d.ProfileDir+")");
   var names=new HashSet<string>(Arr308(prof,"rows").Select(r=>Str308(r,"name")));
   foreach(var r in Arr308(j,"encounters"))
   {
    if(!Flag308(r,"enabled"))continue;
    // existing = an actor that already stands in the scene (wood beasts): no template, no patrol2, no yaw, no scale; preferred_distance
    // only when the row changes it (absent = the actor keeps its own)
    bool existing=Flag308(r,"existing");var p2=existing?null:Req308(r,"patrol2");
    // the mine tutorial's actors (SPEC-PLAYTEST-306) are never a row of this pass: refused here, not only by the offline dry run
    if(existing&&(Str308(r,"id")==MineTutorialProfileSO.BossId||Str308(r,"id")==WorldMacroPlaytestSession.MineTutorialReplacedId))
     throw new Refuse308(family308.Name+" data: row '"+Str308(r,"id")+"' is an actor of the mine tutorial ("+MineTutorialProfileSO.BossId+" / "+WorldMacroPlaytestSession.MineTutorialReplacedId+") - this pass never touches it");
    var row=new EarthRow308{Id=Str308(r,"id"),Op=Opt308(r,"op")??"",Existing=existing,Template=existing?Opt308(r,"template")??"":Str308(r,"template"),ContentId=Str308(r,"content_id"),Attack=Str308(r,"attack"),
     X=Num308(r,"x"),Z=Num308(r,"z"),Yaw=existing?0f:Mathf.Repeat(Num308(r,"yaw"),360f),P2X=existing?0f:Num308(p2,"x"),P2Z=existing?0f:Num308(p2,"z"),Speed=Num308(r,"speed"),Detection=Num308(r,"detection"),Leash=Num308(r,"leash"),
     Activation=Num308(r,"activation"),PreferredDistance=existing&&!Has308(r,"preferred_distance")?float.NaN:Num308(r,"preferred_distance"),Scale=existing?1f:Num308(r,"scale"),
     RoadMin=existing&&!Has308(r,"road_min_m")?0f:Num308(r,"road_min_m"),Ranged=Flag308(r,"ranged"),Respawn=Flag308(r,"respawn")};
    if(Has308(r,"y_offline"))row.YOffline=Num308(r,"y_offline");
    foreach(var s in Arr308(r,"scenes"))row.Scenes.Add(s.Value<string>());
    if(!names.Contains(row.Attack))throw new Refuse308(family308.Name+" data: encounters["+row.Id+"].attack '"+row.Attack+"' is not a profiles.rows name");
    if(d.Rows.Any(x=>x.Id==row.Id))throw new Refuse308(family308.Name+" data: duplicate encounter id "+row.Id);
    if(!(row.Scale>0f)||!(row.Speed>0f)||!(row.Detection>0f)||!(row.Leash>0f))throw new Refuse308(family308.Name+" data: encounters["+row.Id+"] needs positive scale / speed / detection / leash");
    d.Rows.Add(row);
   }
   return d;
  }

  // ---------- status ----------

  static string EarthStatus308()
  {
   var sb=new StringBuilder("Content308.Earth status (play="+EditorApplication.isPlaying+")\n");
   EarthSet308 d;
   try{d=EarthData308();}catch(Refuse308 r){return sb.Append("  "+r.Message).ToString();}
   sb.AppendLine("  data "+EarthFile308+" sha "+Short308(d.Sha)+", group "+d.Group+", rows "+d.Rows.Count);
   foreach(var p in Arr308(Req308(d.Data,"profiles"),"rows"))
   {
    string path=d.ProfileDir+"/"+Str308(p,"name")+".asset";var a=AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(path);
    sb.AppendLine("  profile "+path+": "+(a==null?"MISSING (run profiles)":"Telegraph "+F308(a.Telegraph)+" · Damage "+F308(a.Damage,"F0")+" · "+(a.Elemental?a.Element.ToString():"Neutral")));
   }
   sb.AppendLine("  ledger profiles: "+(File.Exists(EarthProfileLedger308)?"present":"none"));
   foreach(var s in Scenes308)
   {
    var c=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(Targets308[s].content);
    int have=c==null?0:d.RowsOf(s).Count(r=>Encounter308(c,r.Id)!=null);
    sb.AppendLine("  "+Key308(s)+": content rows "+have+"/"+d.RowsOf(s).Count()+", ledger content "+(File.Exists(LedgerFile308(EarthContentKey308(s)))?"present":"none")+", ledger scene "+(File.Exists(LedgerFile308(EarthSceneKey308(s)))?"present":"none"));
   }
   return sb.ToString();
  }

  // ---------- XE1 profiles ----------

  [Serializable] sealed class EarthProfileRow308{public string path="",utc="",before="",after="";public bool created;}
  [Serializable] sealed class EarthProfileLedgerData308{public string source="",sourceSha="";public List<EarthProfileRow308> rows=new List<EarthProfileRow308>();}

  static string EarthProfiles308(bool dry)
  {
   var d=EarthData308();if(!dry)d.NeedOn();var prof=Req308(d.Data,"profiles");var expect=Req308(prof,"expect");
   var source=AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(d.ProfileSource);
   if(source==null)throw new Refuse308("missing source profile "+d.ProfileSource);
   string srcAbs=Harness303.Abs(d.ProfileSource),srcSha=Harness303.Sha(srcAbs);
   // the copy must be a copy of what the design read (profiles.expect names the SOURCE; profiles.set_element, when present, is what
   // the copies carry instead of the source's element): never silently another thing
   bool wantElemental=Flag308(expect,"elemental");string wantElement=Str308(expect,"element"),wantMode=Str308(expect,"mode"),wantDelivery=Str308(expect,"delivery");
   if(source.Elemental!=wantElemental||source.Element.ToString()!=wantElement||source.Mode.ToString()!=wantMode||source.Delivery.ToString()!=wantDelivery)
    throw new Refuse308(d.ProfileSource+" is "+(source.Elemental?source.Element.ToString():"Neutral")+" · "+source.Mode+" · "+source.Delivery+", the data expects "+wantElement+" · "+wantMode+" · "+wantDelivery+" - nothing written");
   var sb=new StringBuilder(family308.Name+" profiles"+(dry?" (dry: nothing is written)":"")+"\n");
   string noted=Opt308(prof,"source_sha256");
   sb.AppendLine("  source "+d.ProfileSource+" sha "+Short308(srcSha)+(noted!=null&&!string.Equals(noted,srcSha,StringComparison.OrdinalIgnoreCase)?" (WARN the data file noted "+Short308(noted)+": the source changed since the design read it)":"")+
    ": Telegraph "+F308(source.Telegraph)+" · Damage "+F308(source.Damage,"F0")+" · Recovery "+F308(source.Recovery)+" · Range "+F308(source.Range)+" · Arc "+F308(source.ArcDegrees,"F0")+" · PreferredDistanceHint "+F308(source.PreferredDistanceHint));
   var ledger=File.Exists(EarthProfileLedger308)?JsonUtility.FromJson<EarthProfileLedgerData308>(File.ReadAllText(EarthProfileLedger308)):new EarthProfileLedgerData308();
   ledger.source=d.ProfileSource;ledger.sourceSha=srcSha;bool journaled=false;
   foreach(var row in Arr308(prof,"rows"))
   {
    string name=Str308(row,"name"),path=d.ProfileDir+"/"+name+".asset";float telegraph=Num308(row,"telegraph"),damage=Num308(row,"damage");
    var asset=AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(path);
    if(asset==null&&AssetDatabase.LoadMainAssetAtPath(path)!=null)throw new Refuse308(path+" exists and is not an EnemyAttackProfileSO");
    bool created=asset==null;
    if(created)
    {
     if(dry){sb.AppendLine("  would create "+path+" (copy of the source): Telegraph "+F308(source.Telegraph)+" -> "+F308(telegraph)+", Damage "+F308(source.Damage,"F0")+" -> "+F308(damage,"F0"));continue;}
     EnsureFolder308(d.ProfileDir);
     // journal first: the ledger names the asset before it exists, so a refusal further down (this row or a later one) can never leave
     // an asset that profiles-revert does not know
     ledger.rows.Add(new EarthProfileRow308{path=path,utc=DateTime.UtcNow.ToString("O"),before="",after="",created=true});EarthProfileJournal308(ledger);journaled=true;
     asset=Object.Instantiate(source);asset.name=name;AssetDatabase.CreateAsset(asset,path);
    }
    else if(EditorUtility.IsDirty(asset))throw new Refuse308(path+" has unsaved in-memory changes (another session?) - save or discard them first");
    string before=created?"":EditorJsonUtility.ToJson(asset);
    // source copy + the two numbers of the row: every other field follows the source on each run
    asset.Archetype=source.Archetype;asset.Mode=source.Mode;asset.Delivery=source.Delivery;asset.Elemental=source.Elemental;asset.Element=source.Element;
    asset.Recovery=source.Recovery;asset.Range=source.Range;asset.ProjectileSpeed=source.ProjectileSpeed;asset.ImpactRadius=source.ImpactRadius;asset.ArcDegrees=source.ArcDegrees;
    asset.CooldownRange=source.CooldownRange;asset.MovementSpeedHint=source.MovementSpeedHint;asset.PreferredDistanceHint=source.PreferredDistanceHint;
    asset.Telegraph=telegraph;asset.Damage=damage;
    if(d.ElementSet.Length>0){asset.Elemental=true;asset.Element=(Element)Enum.Parse(typeof(Element),d.ElementSet);}
    string after=EditorJsonUtility.ToJson(asset);
    if(!asset.TryValidate(out string error))
    {
     if(!created)EditorJsonUtility.FromJsonOverwrite(before,asset);
     throw new Refuse308(path+": "+error+(created?" (the new asset was created but not saved with these values: fix the data row and run profiles again)":""));
    }
    if(!created&&after==before){sb.AppendLine("  "+path+" 변경 없음 (Telegraph "+F308(asset.Telegraph)+" · Damage "+F308(asset.Damage,"F0")+")");continue;}
    if(dry){EditorJsonUtility.FromJsonOverwrite(before,asset);sb.AppendLine("  would update "+path+": Telegraph -> "+F308(telegraph)+", Damage -> "+F308(damage,"F0"));continue;}
    if(created)ledger.rows.Last(x=>x.path==path&&x.created).after=after;
    else ledger.rows.Add(new EarthProfileRow308{path=path,utc=DateTime.UtcNow.ToString("O"),before=before,after=after,created=false});
    EarthProfileJournal308(ledger);journaled=true;   // the ledger before the save
    EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset);
    sb.AppendLine("  "+(created?"created ":"updated ")+path+": Telegraph "+F308(telegraph)+" · Damage "+F308(damage,"F0")+" · "+OrganSurface308.ProfileElement(asset)+" · "+asset.Mode+" (TEST)");
   }
   if(journaled)sb.AppendLine("  ledger "+EarthProfileLedger308+" (written before each asset write)");
   string shaNow=Harness303.Sha(srcAbs);
   sb.Append("  source bytes "+(shaNow==srcSha?"unchanged":"CHANGED (FAIL: "+Short308(srcSha)+" -> "+Short308(shaNow)+")"));
   return sb.ToString();
  }

  static void EarthProfileJournal308(EarthProfileLedgerData308 ledger){Directory.CreateDirectory(Out308);File.WriteAllText(EarthProfileLedger308,JsonUtility.ToJson(ledger,true));}

  // an asset this ledger created goes to the OS trash (recoverable); an asset that was there before gets its first before-form back.
  // Refused while a scene still wires the profiles (an earth scene ledger is present): scene-revert first.
  static string EarthProfilesRevert308()
  {
   if(!File.Exists(EarthProfileLedger308))throw new Refuse308("no ledger "+EarthProfileLedger308+" (nothing applied, or already reverted)");
   var live=Scenes308.Where(s=>File.Exists(LedgerFile308(EarthSceneKey308(s)))).Select(Key308).ToArray();
   if(live.Length>0)throw new Refuse308("the profiles are still wired in "+string.Join(", ",live)+" (earth scene ledger present): run "+family308.Entry+" scene-revert:<scene> there first");
   var ledger=JsonUtility.FromJson<EarthProfileLedgerData308>(File.ReadAllText(EarthProfileLedger308));
   var sb=new StringBuilder(family308.Name+" profiles (revert)\n");
   foreach(var g in ledger.rows.GroupBy(r=>r.path))
   {
    var first=g.First();string path=g.Key;
    if(Harness303.IsProtected(path))throw new Refuse308("protected path in ledger "+path);
    var asset=AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(path);
    if(asset==null){sb.AppendLine("  "+path+": already gone");continue;}
    if(first.created){bool ok=AssetDatabase.MoveAssetToTrash(path);sb.AppendLine("  "+path+(ok?" moved to the OS trash (this ledger created it)":" WARN MoveAssetToTrash returned false"));}
    else{EditorJsonUtility.FromJsonOverwrite(first.before,asset);EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset);sb.AppendLine("  "+path+": first before-form put back");}
   }
   string archived=EarthProfileLedger308+".reverted-"+Harness303.UtcStamp();File.Move(EarthProfileLedger308,archived);
   return sb.Append("  ledger archived "+archived).ToString();
  }

  // ---------- XE2-XE4 content half ----------

  // pinned: the row's XZ on the physical ground; a slope over the limit refuses (the row is revised, the tool never moves it)
  static Vector3 EarthGround308(string what,float x,float z,EarthSet308 d,float yOffline,List<string> notes)
  {
   if(!Ground308(x,z,out var g,out float slope))throw new Refuse308("no ground under "+what+" ("+F308(x,"F1")+", "+F308(z,"F1")+") - is the scene loaded?");
   if(slope>d.FeetMaxSlope)throw new Refuse308(what+": the physical slope at ("+F308(x,"F1")+", "+F308(z,"F1")+") is "+F308(slope,"F1")+"° > rules.feet_max_slope_deg "+F308(d.FeetMaxSlope,"F0")+"° - revise the row in "+EarthFile308+". Nothing written");
   Vector3 at=g;
   if(NavMesh.SamplePosition(g,out var hit,d.NavRadius,NavMesh.AllAreas))
   {
    float off=Harness303.Flat(hit.position,g);
    if(off<=d.NavXzTol)at=hit.position;
    else notes.Add("WARN "+what+": the nearest NavMesh point is "+F308(off)+" m away in XZ (> "+F308(d.NavXzTol)+"); the place stays on the row (pinned) - look at it after the bake (QE1)");
   }
   else notes.Add("WARN "+what+": no NavMesh within "+F308(d.NavRadius,"F0")+" m of "+V308(g)+" (the session throws at start without it: bake, then scene-check)");
   notes.Add(what+" at "+V308(at)+" (slope "+F308(slope,"F1")+"°)");
   OfflineY308(what,at,yOffline,notes);
   return at;
  }

  static List<WorldMacroPlaytestSO.Encounter> EarthPlanContent308(EarthSet308 d,string scenePath,WorldMacroPlaytestSO content,List<string> notes)
  {
   var plan=new List<WorldMacroPlaytestSO.Encounter>();
   foreach(var r in d.RowsOf(scenePath))
   {
    if(r.Existing){notes.Add(r.Id+": an existing encounter of this content ("+(Encounter308(content,r.Id)!=null?"present":"MISSING")+") - this pass does not write its content row");continue;}
    var had=Encounter308(content,r.Id);
    // written once near the row = kept as it is (a second apply writes nothing; the bake does not shift it)
    bool keep=had!=null&&Vector2.Distance(new Vector2(had.Feet.x,had.Feet.z),new Vector2(r.X,r.Z))<=d.Keep;
    var feet=keep?had.Feet:EarthGround308(r.Id,r.X,r.Z,d,r.YOffline,notes);
    bool keepPatrol=keep&&had.Patrol!=null&&had.Patrol.Length==2&&Vector2.Distance(new Vector2(had.Patrol[1].x,had.Patrol[1].z),new Vector2(r.P2X,r.P2Z))<=d.Keep&&(had.Patrol[0]-feet).sqrMagnitude<1e-6f;
    var patrol=keepPatrol?had.Patrol.ToArray():new[]{feet,EarthGround308(r.Id+" patrol2",r.P2X,r.P2Z,d,float.NaN,notes)};
    if(!Array.Exists(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>(),e=>e!=null&&e.Id==r.Template))notes.Add("WARN "+r.Id+": the template encounter "+r.Template+" is not in this content (the scene pass will have no template actor either)");
    plan.Add(new WorldMacroPlaytestSO.Encounter{Id=r.Id,ContentId=r.ContentId,Feet=feet,Patrol=patrol,Ranged=r.Ranged,RespawnOnRest=r.Respawn,Detection=r.Detection,Leash=r.Leash,Speed=r.Speed,Activation=r.Activation});
   }
   return plan;
  }
  static List<string> EarthEditContent308(WorldMacroPlaytestSO content,List<WorldMacroPlaytestSO.Encounter> plan)
  {
   var ch=new List<string>();var list=(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>()).ToList();
   foreach(var spec in plan)
   {
    int i=list.FindIndex(x=>x!=null&&x.Id==spec.Id);
    string what="feet "+V308(spec.Feet)+", patrol2 "+V308(spec.Patrol[spec.Patrol.Length-1])+", Detection "+F308(spec.Detection,"F0")+" · Leash "+F308(spec.Leash,"F0")+" · Speed "+F308(spec.Speed,"F1")+" · RespawnOnRest "+(spec.RespawnOnRest?1:0)+" · Ranged "+(spec.Ranged?1:0)+" · Activation "+F308(spec.Activation,"F0");
    if(i<0){list.Add(spec);ch.Add("encounter "+spec.Id+" added: "+what);}
    else if(JsonUtility.ToJson(list[i])!=JsonUtility.ToJson(spec)){ch.Add("encounter "+spec.Id+" updated: "+JsonUtility.ToJson(list[i])+" -> "+what);list[i]=spec;}
   }
   content.Encounters=list.ToArray();
   return ch;
  }

  static string EarthContent308(string scenePath,bool dry)
  {
   var d=EarthData308();if(!dry)d.NeedOn();
   var scene=Open308(scenePath);var session=Session308(scene);var t=Targets308[scene.path];
   var content=Load308<WorldMacroPlaytestSO>(t.content);
   if(session.Content!=content)throw new Refuse308(scene.path+" session Content is "+AssetDatabase.GetAssetPath(session.Content)+", expected "+t.content);
   PrepareGround308(scene,session);
   var notes=new List<string>{family308.Name+" data "+EarthFile308+" sha "+Short308(d.Sha)+", group "+d.Group};
   string sceneNote=Opt308(d.Data["scene_notes"],PostLedger308.Short(scene.path));if(sceneNote!=null)notes.Add(sceneNote);
   var plan=EarthPlanContent308(d,scene.path,content,notes);
   string key=EarthContentKey308(scene.path);
   if(dry)
   {
    if(EditorUtility.IsDirty(content))throw new Refuse308(t.content+" has unsaved in-memory changes (another session?) - save or discard them first");
    string before=EditorJsonUtility.ToJson(content);List<string> ch;
    try{ch=EarthEditContent308(content,plan);}
    finally{EditorJsonUtility.FromJsonOverwrite(before,content);}
    var sb=new StringBuilder(family308.Name+" content-dry "+scene.path+" (nothing is written)\n");
    if(ch.Count==0)sb.AppendLine("  변경 없음 (content-apply would write nothing)");
    foreach(var x in ch)sb.AppendLine("  would: "+x+" ["+d.Group+"]");
    foreach(var n in notes)sb.AppendLine("  note "+n);
    Directory.CreateDirectory(Out308);string file=Path.Combine(Out308,key+"-dry.txt");File.WriteAllText(file,sb.ToString());
    return sb.Append("  report "+file).ToString();
   }
   var ledger=ReadLedger308(key)??new Ledger308{kind=family308.Name+"-content",scene=scene.path,created=DateTime.UtcNow.ToString("O")};
   Write308 w=null;
   try
   {
    w=Edit308(content,BackupDir308(),ledger,()=>EarthEditContent308(content,plan));
    // every row of this ledger belongs to the one group (the group revert reads it)
    if(w!=null)foreach(var row in w.rows)row.group=d.Group;
   }
   finally{if(w!=null)WriteLedger308(key,ledger);}
   return Report308(family308.Name+" content-apply "+scene.path+" ["+d.Group+"]",new List<Write308>{w},notes,key);
  }

  // ---------- check (data half, the three contents; nothing opened, nothing saved) ----------

  static void EarthContentChecks308(Check308 k,string tag,EarthSet308 d,string scene,WorldMacroPlaytestSO c)
  {
   var ck=Req308(d.Data,"checks");float rule=Num308(ck,"rule54_m"),gap=Num308(ck,"enemy_gap_m");
   var enc=c.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>();
   var mine=new List<(EarthRow308 row,WorldMacroPlaytestSO.Encounter e)>();
   foreach(var r in d.RowsOf(scene))
   {
    var e=Array.Find(enc,x=>x!=null&&x.Id==r.Id);
    bool ok=e!=null&&Mathf.Approximately(e.Detection,r.Detection)&&Mathf.Approximately(e.Leash,r.Leash)&&Mathf.Approximately(e.Speed,r.Speed)&&e.RespawnOnRest==r.Respawn&&e.Ranged==r.Ranged&&e.ContentId==r.ContentId;
    k.C(ok,tag+"AC-E1 "+r.Id+": "+(e==null?"missing (run "+family308.Entry+" content-apply)":"Detection "+F308(e.Detection,"F0")+" · Leash "+F308(e.Leash,"F0")+" · Speed "+F308(e.Speed,"F1")+" · RespawnOnRest "+(e.RespawnOnRest?1:0)+" · Ranged "+(e.Ranged?1:0)+" · ContentId "+e.ContentId));
    if(e==null)continue;mine.Add((r,e));
    float off=Vector2.Distance(new Vector2(e.Feet.x,e.Feet.z),new Vector2(r.X,r.Z));
    float placeTol=r.Existing?d.PlaceTol:d.Keep;
    k.C(off<=placeTol,tag+r.Id+": feet "+V308(e.Feet)+" "+F308(off)+" m from its row (≤ "+F308(placeTol)+")"+(float.IsNaN(r.YOffline)?"":", y vs offline "+F308(e.Feet.y-r.YOffline)+" m"));
    if(!r.Existing)k.C(e.Patrol!=null&&e.Patrol.Length==2&&Vector2.Distance(new Vector2(e.Patrol[1].x,e.Patrol[1].z),new Vector2(r.P2X,r.P2Z))<=d.Keep,tag+r.Id+": patrol = [feet, patrol2] as the row says");
    // AC-E3 (1): every rest place of this content (checkpoint feet, Rest points)
    float need=Mathf.Max(rule,e.Detection+e.Leash+10f);
    var rests=(c.Checkpoints??Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>()).Where(x=>x!=null&&x.IsConfigured).Select(x=>(name:"feet "+x.Id,at:x.Feet))
     .Concat((c.Points??Array.Empty<PrologueContentSO.Point>()).Where(x=>x!=null&&x.Kind==PrologueInteractionKind.Rest).Select(x=>(name:"rest "+x.Id,at:x.Position))).ToArray();
    if(rests.Length>0){var near=rests.OrderBy(x=>Harness303.Flat(x.at,e.Feet)).First();float nd=Harness303.Flat(near.at,e.Feet);k.C(nd>=need,tag+"AC-E3 "+r.Id+" <-> nearest rest place ("+near.name+") "+F308(nd,"F1")+" m ≥ "+F308(need,"F0"));}
    // AC-E3 (2): mandatory stops / report NPCs of the base relayout (offline XZ from the data)
    foreach(var s in Arr308(ck,"stops")){float sd=Vector2.Distance(new Vector2(e.Feet.x,e.Feet.z),new Vector2(Num308(s,"x"),Num308(s,"z")));if(sd<need+40f)k.C(sd>=need,tag+"AC-E3 "+r.Id+" <-> "+Str308(s,"name")+" "+F308(sd,"F1")+" m ≥ "+F308(need,"F0"));}
    // review 2: places the player stands at without resting (an investigation point): its ring must stay outside the detection range
    foreach(var s in (ck["watch"] as Newtonsoft.Json.Linq.JArray)??new Newtonsoft.Json.Linq.JArray())
    {
     string pid=Opt308(s,"point")??"";var pt=Array.Find(c.Points??Array.Empty<PrologueContentSO.Point>(),x=>x!=null&&x.Id==pid);
     var at=pt!=null?new Vector2(pt.Position.x,pt.Position.z):new Vector2(Num308(s,"x"),Num308(s,"z"));
     float wd=Vector2.Distance(new Vector2(e.Feet.x,e.Feet.z),at)-Num308(s,"radius_m");
     if(wd<e.Detection+40f)k.C(wd>=e.Detection,tag+"AC-E3 "+r.Id+" <-> "+Str308(s,"name")+(pt!=null?"":" (not in this content: data XZ)")+": ring edge "+F308(wd,"F1")+" m ≥ detection "+F308(e.Detection,"F0"));
    }
    // AC-E4: the encounters named in checks.others
    foreach(var o in Arr308(ck,"others"))
    {
     var other=Array.Find(enc,x=>x!=null&&x.Id==o.Value<string>());
     if(other==null){k.I(tag+"AC-E4 "+o.Value<string>()+" not in this content (scene without it)");continue;}
     float od=Harness303.Flat(other.Feet,e.Feet);if(od<gap+200f)k.C(od>=gap,tag+"AC-E4 "+r.Id+" <-> "+other.Id+" "+F308(od,"F1")+" m ≥ "+F308(gap,"F0"));
    }
   }
   for(int i=0;i<mine.Count;i++)for(int j=i+1;j<mine.Count;j++)
   {
    float dd=Harness303.Flat(mine[i].e.Feet,mine[j].e.Feet);
    if(gap>0f&&dd<gap+100f)k.C(dd>=gap,tag+"AC-E4 "+mine[i].row.Id+" <-> "+mine[j].row.Id+" "+F308(dd,"F1")+" m ≥ "+F308(gap,"F0"));
   }
  }
  static void EarthRoadChecks308(Check308 k,string tag,EarthSet308 d,string scene,WorldMacroPlaytestSO c,CompactWorldLayoutSO layout)
  {
   if(layout==null)return;
   foreach(var r in d.RowsOf(scene))
   {
    var e=Encounter308(c,r.Id);if(e==null||e.Patrol==null||e.Patrol.Length==0)continue;
    foreach(var (name,p) in new[]{("feet",e.Feet),("patrol2",e.Patrol[e.Patrol.Length-1])})
    {
     float rd=RoadDistance308(layout,new Vector2(p.x,p.z),out string road,out float width);
     if(r.RoadMin>0f)k.C(rd>=r.RoadMin,tag+"AC-E5 "+r.Id+" "+name+": "+F308(rd)+" m from the centre of "+road+" (width "+F308(width,"F1")+") ≥ "+F308(r.RoadMin,"F1"));
     else k.I(tag+"AC-E5 "+r.Id+" "+name+": "+F308(rd,"F1")+" m from the centre of "+road+" (no road limit on this row)");
    }
   }
  }
  static void EarthProfileChecks308(Check308 k,EarthSet308 d)
  {
   var prof=Req308(d.Data,"profiles");var source=AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(d.ProfileSource);
   foreach(var row in Arr308(prof,"rows"))
   {
    string path=d.ProfileDir+"/"+Str308(row,"name")+".asset";var a=AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(path);
    bool ok=a!=null&&a.TryValidate(out _)&&OrganSurface308.ProfileElement(a)==d.Element&&Mathf.Approximately(a.Telegraph,Num308(row,"telegraph"))&&Mathf.Approximately(a.Damage,Num308(row,"damage"))
     &&source!=null&&a.Mode==source.Mode&&a.Delivery==source.Delivery&&Mathf.Approximately(a.Range,source.Range)&&Mathf.Approximately(a.Recovery,source.Recovery)&&Mathf.Approximately(a.PreferredDistanceHint,source.PreferredDistanceHint);
    k.C(ok,"AC-E2 "+path+": "+(a==null?"missing (run "+family308.Entry+" profiles)":"valid · "+d.Element+" · Telegraph "+F308(a.Telegraph)+" · Damage "+F308(a.Damage,"F0")+" · the rest equal to the source"));
   }
   string noted=Opt308(prof,"source_sha256");string now=File.Exists(Harness303.Abs(d.ProfileSource))?Harness303.Sha(Harness303.Abs(d.ProfileSource)):"";
   if(noted!=null)k.C(string.Equals(noted,now,StringComparison.OrdinalIgnoreCase),"AC-E2 "+d.ProfileSource+" bytes unchanged (sha "+Short308(now)+" vs the data's "+Short308(noted)+")");
   if(d.Vitals.Length==0){k.I("AC-E2 vitals: not wired by this pass (each actor keeps its own EnemyVitals profile)");return;}
   var vit=AssetDatabase.LoadAssetAtPath<EnemyVitalsProfileSO>(d.Vitals);
   k.C(vit!=null&&!vit.IsBoss&&vit.MaxHp>0f,"AC-E2 "+d.Vitals+": "+(vit==null?"missing":"MaxHp "+F308(vit.MaxHp,"F0")+", not a boss"));
  }

  static string EarthCheck308()
  {
   var d=EarthData308();var k=new Check308();
   k.I(family308.Name+" data sha "+Short308(d.Sha)+", group "+d.Group+", rows "+d.Rows.Count);
   EarthProfileChecks308(k,d);
   foreach(var scene in Scenes308)
   {
    var t=Targets308[scene];var c=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(t.content);var layout=AssetDatabase.LoadAssetAtPath<CompactWorldLayoutSO>(t.layout);
    string tag=Key308(scene)+" ";
    if(c==null){k.C(false,tag+"content missing "+t.content);continue;}
    string sceneNote=Opt308(d.Data["scene_notes"],PostLedger308.Short(scene));if(sceneNote!=null)k.I(tag+sceneNote);
    EarthContentChecks308(k,tag,d,scene,c);
    EarthRoadChecks308(k,tag,d,scene,c,layout);
    var l=ReadLedger308(EarthContentKey308(scene));
    k.I(tag+"content ledger: "+(l==null?"none":l.writes.Count+" write(s), live rows "+l.writes.Where(w=>w!=null&&w.rows!=null).SelectMany(w=>w.rows).Count(r=>r!=null&&!r.reverted)));
   }
   k.I("AC-E9: no reward field is authored here - the three use TestRules.EnemyReward like every ordinary enemy; no HUD element, no instruction text is added by this pass");
   return k.Done("checks-"+family308.Name+"308.txt");
  }
 }
}
