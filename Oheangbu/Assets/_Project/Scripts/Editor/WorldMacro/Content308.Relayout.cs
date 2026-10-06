using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // D308-16 relayout (EA only; LOCKED order kept, places and routes only) - the data-driven half of Content308 [TEST].
 // Data: Art/World/Compact/Rebuild/CliffBoundary308/content308_relayout.json. With NO such file every list below returns the #308
 // constants of Content308.Content.cs / Content308.Campaign.cs, so content-apply / campaign-apply / the scene pass behave exactly
 // as before. With the file:
 //   rests[]            override a #308 rest by id or add one: x, z, max_slope, radius, shop, pin, force, feet{x,z,yaw}, y_offline
 //                      (a new rest takes label + prompt_from = an existing rest's prompt; no text is authored in data)
 //   points[]           move a #308 point: x, z, force (the 12 m keep rule does not hold a force row)
 //   point_moves[]      move any existing content point by id: x, z, pin, max_slope, force, yaw (the scene body's facing, T4)
 //   encounter_moves[]  move any encounter by id: x, z, respawn (-1 keep / 0 / 1), yaw (the scene actor's facing, T4), force
 //                      (feet already within 3 m of the target are snapped to the NavMesh again - for the re-run after a bake)
 //   rests_escort[]     T5: an escort rest (road_rest_n) as one bundle - content point + checkpoint feet / yaw here, the scene
 //                      Checkpoint node, the bench node and the altar in the scene pass; prompt / text / radius / label untouched
 //   layout_places[]    T9: a layout place that follows a moved encounter (XZ and the realm the layout gives that XZ)
 //   paths{}            T2: MainPath / BranchPath legs (Content308.Paths.cs)
 //   campaign{}         T3: stage -> content point (or encounter) destinations added to the #308 list; identity_guard
 //   groups_off[]       T7: groups whose rows are ignored (switch a group off here, then revert it with --group)
 // Every row may carry "group" (ledger group, T7) and "enabled". Heights are never data: y is the opened scene's physical ground;
 // y_offline is the offline height and is only reported next to the editor value.
 // Commands (queue-safe, refusals are strings):
 //   relayout-status                          data sha, groups, live ledger rows per group
 //   content-dry:<scene> | campaign-dry       what content-apply / campaign-apply would write; nothing is saved
 //   campaign-sig                             the gate signature (order, prerequisites, Optional, rewards …) and its sha
 //   paths-dry:<scene> | paths-regen:<scene> | paths-check:<scene>      (Content308.Paths.cs)
 //   campaign-revert | content-revert:<scene> | scene-revert:<scene>  + " --group <G>" [--force]    one group, newest row first
 public static partial class Content308
 {
  const string RelayoutUsage308="relayout-status | probe:<scene>:<x>,<z>[;<x>,<z>...] | content-dry:<scene> | campaign-dry | campaign-sig | paths-dry:<scene> | paths-regen:<scene> | paths-check:<scene> | <revert> --group <G> [--force]";
  static string RelayoutFile308=>Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","CliffBoundary308","content308_relayout.json");

  // per-call state (domain reload is off: Run resets these first)
  static RelayoutData308 relayoutCache308;static bool relayoutRead308;static string relayoutStamp308="";
  static string argGroup308;static bool argForce308;

  static string RelayoutPass308(string c)
  {
   string Arg(string head)=>c.StartsWith(head,StringComparison.Ordinal)?SceneArg308(c.Substring(head.Length)):null;
   string a;
   if(c=="relayout-status")return RelayoutStatus308();
   if(c.StartsWith("probe:",StringComparison.Ordinal))return Probe308(c.Substring(6));   // Content308.Probe.cs (read only)
   if(c=="campaign-sig")return CampaignSigReport308();
   if(c=="campaign-dry")return CampaignDry308();
   if((a=Arg("content-dry:"))!=null)return ContentDry308(a);
   if((a=Arg("paths-dry:"))!=null)return PathsRegen308(a,true);
   if((a=Arg("paths-regen:"))!=null)return PathsRegen308(a,false);
   if((a=Arg("paths-check:"))!=null)return PathsCheck308(a);
   return null;
  }

  // ---------- data ----------

  sealed class RelayoutData308
  {
   public JObject Data;public string Sha="";
   public readonly HashSet<string> Off=new HashSet<string>();
   public readonly Dictionary<string,string> GroupOf=new Dictionary<string,string>();   // content id -> group (rows that are on)
   public readonly Dictionary<string,float> YawOf=new Dictionary<string,float>();        // content id -> facing (point_moves / encounter_moves)
   public float ForceTol,PinKeep,FeetMaxSlope,AgreeTol,NodeTol;public bool CampaignGuard=true;
   // rules{}: yaw_tol_deg (content yaw vs the row), resnap_tol_m (a force encounter row is written again only when the new
   // NavMesh snap differs by more than this), end_match_m (a road end counts as "in the path" within this), field_centre_tol_m
   // (a boss field belongs to an encounter whose feet are this near its centre), point_move_keep_m / encounter_keep_m (how far
   // an unpinned moved point / a snapped encounter may stand from its row and still pass the check: the #308 keep distances)
   public float YawTol,ResnapTol,EndMatch,FieldCentreTol,PointMoveKeep,EncounterKeep;
   public bool On(JToken row)
   {
    var e=row?["enabled"];if(e!=null&&e.Type==JTokenType.Boolean&&!e.Value<bool>())return false;
    return !Off.Contains(GroupKey308(row));
   }
   public IEnumerable<JToken> Rows(string key)=>Arr308(Data,key).Where(On);
   public JToken Campaign=>Data["campaign"];
   public IEnumerable<JToken> Destinations=>Campaign==null?Enumerable.Empty<JToken>():Arr308(Campaign,"destinations").Where(On);
  }
  static string GroupKey308(JToken row)=>Opt308(row,"group")??"";
  static bool Has308(JToken o,string key){var t=o?[key];return t!=null&&t.Type!=JTokenType.Null;}

  static readonly string[] RowLists308={"rests","points","point_moves","encounter_moves","rests_escort","layout_places"};

  // null = no relayout data file (the constants are the whole behaviour). Parsed once per file state.
  static RelayoutData308 Relayout308()
  {
   string f=RelayoutFile308;
   if(!File.Exists(f)){relayoutRead308=true;relayoutCache308=null;relayoutStamp308="";return null;}
   var info=new FileInfo(f);string stamp=info.LastWriteTimeUtc.Ticks+":"+info.Length;
   if(relayoutRead308&&relayoutCache308!=null&&relayoutStamp308==stamp)return relayoutCache308;
   JObject j;
   try{j=JObject.Parse(File.ReadAllText(f));}
   catch(Exception e){throw new Refuse308("relayout data does not parse: "+f+" ("+e.Message+")");}
   var rules=Req308(j,"rules");
   var d=new RelayoutData308{Data=j,Sha=Harness303.Sha(f),ForceTol=Num308(rules,"force_tol_m"),PinKeep=Num308(rules,"pin_keep_m"),FeetMaxSlope=Num308(rules,"feet_max_slope_deg"),
    AgreeTol=Num308(rules,"agree_tol_m"),NodeTol=Num308(rules,"node_tol_m"),
    YawTol=Num308(rules,"yaw_tol_deg"),ResnapTol=Num308(rules,"resnap_tol_m"),EndMatch=Num308(rules,"end_match_m"),FieldCentreTol=Num308(rules,"field_centre_tol_m"),
    PointMoveKeep=Num308(rules,"point_move_keep_m"),EncounterKeep=Num308(rules,"encounter_keep_m")};
   // T1 does not read encounter_adds from data (the #308 constant list stands): rows there would be silently ignored
   if(Arr308(j,"encounter_adds").Any())throw new Refuse308("relayout data: encounter_adds[] is not implemented (new encounters are the #308 constants of Content308.Content.cs) - remove the rows from "+f);
   foreach(var g in Arr308(j,"groups_off"))d.Off.Add(g.Value<string>());
   var camp=j["campaign"];if(camp!=null&&camp["identity_guard"]!=null&&camp["identity_guard"].Type==JTokenType.Boolean)d.CampaignGuard=camp["identity_guard"].Value<bool>();
   foreach(var list in RowLists308)
    foreach(var r in d.Rows(list))
    {
     string id=Str308(r,"id");string g=GroupKey308(r);
     if(g.Length>0)d.GroupOf[id]=g;
     if((list=="point_moves"||list=="encounter_moves")&&Has308(r,"yaw"))d.YawOf[id]=Mathf.Repeat(Num308(r,"yaw"),360f);
    }
   relayoutCache308=d;relayoutRead308=true;relayoutStamp308=stamp;return d;
  }
  static float ForceTol308(){var d=Relayout308();return d!=null?d.ForceTol:float.PositiveInfinity;}
  static float EndMatch308(){var d=Relayout308()??throw new Refuse308("no relayout data: "+RelayoutFile308);return d.EndMatch;}
  static float ResnapTol308(){var d=Relayout308();return d!=null?d.ResnapTol:float.PositiveInfinity;}

  // every group key the data names (rows on or off, paths, campaign)
  static HashSet<string> GroupsInData308(RelayoutData308 d)
  {
   var set=new HashSet<string>();
   foreach(var list in RowLists308)foreach(var r in Arr308(d.Data,list)){string g=GroupKey308(r);if(g.Length>0)set.Add(g);}
   var p=d.Data["paths"];if(p!=null&&GroupKey308(p).Length>0)set.Add(GroupKey308(p));
   if(d.Campaign!=null)foreach(var r in Arr308(d.Campaign,"destinations")){string g=GroupKey308(r);if(g.Length>0)set.Add(g);}
   GateGroupsInData308(set);   // D308-16c: the gate delta group, while it is on
   return set;
  }

  // ---------- the lists the content pass reads (constants, overridden / extended by data rows) ----------

  static RestSpec308[] RestsNow308()
  {
   var d=Relayout308();if(d==null)return Rests308;
   var list=Rests308.Select(r=>r.Copy()).ToList();
   foreach(var r in d.Rows("rests"))
   {
    string id=Str308(r,"id");var s=list.FirstOrDefault(x=>x.Id==id);
    if(s==null)
    {
     // a new rest reuses the prompt of an existing #308 rest; its label is a place name from the design record
     string from=Str308(r,"prompt_from");var src=Rests308.FirstOrDefault(x=>x.Id==from);
     if(src==null)throw new Refuse308("relayout rests["+id+"]: prompt_from '"+from+"' is not a #308 rest (a new rest reuses an existing prompt; text is not authored in data)");
     s=new RestSpec308{Id=id,Label=Str308(r,"label"),Prompt=src.Prompt,X=Num308(r,"x"),Z=Num308(r,"z")};list.Add(s);
    }
    else if(Has308(r,"label"))s.Label=Str308(r,"label");
    s.Group=GroupKey308(r);
    if(Has308(r,"x"))s.X=Num308(r,"x");if(Has308(r,"z"))s.Z=Num308(r,"z");
    if(Has308(r,"max_slope"))s.MaxSlope=Num308(r,"max_slope");if(Has308(r,"radius"))s.Radius=Num308(r,"radius");
    if(Has308(r,"shop"))s.Shop=Flag308(r,"shop");
    s.Pin=Flag308(r,"pin");s.Force=Flag308(r,"force");
    var feet=r["feet"];
    if(feet!=null&&feet.Type==JTokenType.Object)
    {
     s.HasFeet=true;s.FeetX=Num308(feet,"x");s.FeetZ=Num308(feet,"z");
     if(Has308(feet,"yaw")){s.HasYaw=true;s.Yaw=Num308(feet,"yaw");}
    }
    if(Has308(r,"y_offline"))s.YOffline=Num308(r,"y_offline");
   }
   return list.ToArray();
  }

  static PointSpec308[] PointsNow308()
  {
   var list=Points308();var d=Relayout308();if(d==null)return list;
   foreach(var r in d.Rows("points"))
   {
    string id=Str308(r,"id");var s=Array.Find(list,x=>x.Id==id);
    if(s==null)throw new Refuse308("relayout points["+id+"]: not a #308 point (points[] moves the #308 points; another point moves through point_moves[]; a new point needs its text in code and a design record)");
    if(s.Anchor!=null)throw new Refuse308("relayout points["+id+"]: this point is placed relative to the rest "+s.Anchor+"; move that rest instead");
    s.X=Num308(r,"x");s.Z=Num308(r,"z");s.Force=Flag308(r,"force");s.Group=GroupKey308(r);
    if(Has308(r,"y_offline"))s.YOffline=Num308(r,"y_offline");
   }
   return list;
  }

  sealed class MoveSpec308{public string Id,Group="";public float X,Z,MaxSlope=12f,Yaw,YOffline=float.NaN;public bool Pin,Force,HasYaw;public int Respawn=-1;}

  static List<MoveSpec308> PointMovesNow308()
  {
   var list=PointMoves308.Select(m=>new MoveSpec308{Id=m.id,X=m.x,Z=m.z}).ToList();
   var d=Relayout308();if(d==null)return list;
   foreach(var r in d.Rows("point_moves"))
   {
    string id=Str308(r,"id");var s=list.FirstOrDefault(x=>x.Id==id);if(s==null){s=new MoveSpec308{Id=id};list.Add(s);}
    s.X=Num308(r,"x");s.Z=Num308(r,"z");s.Pin=Flag308(r,"pin");s.Force=Flag308(r,"force");s.Group=GroupKey308(r);
    if(Has308(r,"max_slope"))s.MaxSlope=Num308(r,"max_slope");
    if(Has308(r,"yaw")){s.HasYaw=true;s.Yaw=Num308(r,"yaw");}
    if(Has308(r,"y_offline"))s.YOffline=Num308(r,"y_offline");
   }
   return list;
  }

  static List<MoveSpec308> EncounterMovesNow308()
  {
   var list=EncounterMoves308.Select(m=>new MoveSpec308{Id=m.id,X=m.x,Z=m.z,Respawn=m.respawn}).ToList();
   var d=Relayout308();if(d==null)return list;
   foreach(var r in d.Rows("encounter_moves"))
   {
    string id=Str308(r,"id");var s=list.FirstOrDefault(x=>x.Id==id);if(s==null){s=new MoveSpec308{Id=id};list.Add(s);}
    s.X=Num308(r,"x");s.Z=Num308(r,"z");s.Group=GroupKey308(r);s.Force=Flag308(r,"force");
    if(Has308(r,"respawn"))s.Respawn=Mathf.RoundToInt(Num308(r,"respawn"));
    if(Has308(r,"yaw")){s.HasYaw=true;s.Yaw=Num308(r,"yaw");}
    if(Has308(r,"y_offline"))s.YOffline=Num308(r,"y_offline");
   }
   return list;
  }

  // agwi's place: the encounter_moves row when the data moves it, else the #308 constant (the 여막 터 default sits beside it)
  static Vector2 AgwiNow308()
  {
   var d=Relayout308();
   if(d!=null)foreach(var r in d.Rows("encounter_moves"))if(Str308(r,"id")=="folklore298/agwi")return new Vector2(Num308(r,"x"),Num308(r,"z"));
   return new Vector2(AgwiX308,AgwiZ308);
  }

  static List<(string id,Vector2 xz)> PlaceMovesNow308()
  {
   var list=new List<(string,Vector2)>();var d=Relayout308();if(d==null)return list;
   foreach(var r in d.Rows("layout_places"))list.Add((Str308(r,"id"),new Vector2(Num308(r,"x"),Num308(r,"z"))));
   return list;
  }

  // T3: the #308 destination list plus the data rows (stage -> point | encounter); a data row replaces a constant row of its stage
  static List<(string stage,string point,string encounter)> DestinationsNow308()
  {
   var list=Destinations308.ToList();var d=Relayout308();if(d==null)return list;
   foreach(var r in d.Destinations)
   {
    string stage=Str308(r,"stage"),point=Opt308(r,"point"),encounter=Opt308(r,"encounter");
    if((point==null)==(encounter==null))throw new Refuse308("relayout campaign.destinations["+stage+"]: exactly one of point / encounter is needed");
    int i=list.FindIndex(x=>x.stage==stage);
    if(i>=0)list[i]=(stage,point,encounter);else list.Add((stage,point,encounter));
   }
   return list;
  }

  // ---------- grounded places that the tool never moves ----------

  // pin: the row's XZ on the physical ground. A slope over the row's limit refuses (the row is revised, the tool does not look for
  // another place). A place already written there (within pin_keep_m) is returned as it is, so a re-run writes nothing.
  static Vector3 Pinned308(string id,float x,float z,float maxSlope,Vector3? had,float yOffline,List<string> notes)
  {
   var d=Relayout308();float keep=d!=null?d.PinKeep:0f;
   if(!Ground308(x,z,out var p,out float slope))throw new Refuse308("no ground under "+id+" ("+F308(x,"F1")+", "+F308(z,"F1")+") - is the scene loaded?");
   if(slope>maxSlope)throw new Refuse308(id+": the pinned place ("+F308(x,"F1")+", "+F308(z,"F1")+") has a physical slope of "+F308(slope,"F1")+"° > max_slope "+F308(maxSlope,"F1")+
    "° - revise the row's x / z (or max_slope) in "+RelayoutFile308+"; a pinned row is never moved by the tool. Nothing written");
   // Once written at the row's XZ the place is NOT measured again (the #308 keep rule did the same): a walkable collider another
   // tool puts there later (an NPC body, a seated prop) must not lift the point onto itself and break "second apply = no change".
   if(had!=null&&Harness303.Flat(had.Value,p)<=keep)
   {
    if(Mathf.Abs(had.Value.y-p.y)>keep)notes.Add(id+": kept at y "+F308(had.Value.y)+" (written earlier); the ground probe now reads "+F308(p.y)+" (Δ "+F308(p.y-had.Value.y)+" m) - a pinned place is not re-measured; if the terrain itself changed, revert this row's group and apply again");
    return had.Value;
   }
   notes.Add(id+" pinned at "+V308(p)+" (slope "+F308(slope,"F1")+"° ≤ "+F308(maxSlope,"F1")+"°)");
   OfflineY308(id,p,yOffline,notes);
   return p;
  }
  static Vector3 PinnedFeet308(string id,float x,float z,Vector3? had,List<string> notes)
  {
   var d=Relayout308()??throw new Refuse308("the respawn feet of "+id+" come from the relayout data, and there is none");float keep=d.PinKeep,maxSlope=d.FeetMaxSlope;
   if(!Ground308(x,z,out var p,out float slope))throw new Refuse308("no ground under the respawn feet of "+id+" ("+F308(x,"F1")+", "+F308(z,"F1")+")");
   if(slope>maxSlope)throw new Refuse308(id+": the respawn feet ("+F308(x,"F1")+", "+F308(z,"F1")+") stand on "+F308(slope,"F1")+"° > "+F308(maxSlope,"F0")+"° - revise feet in "+RelayoutFile308+". Nothing written");
   if(had!=null&&Harness303.Flat(had.Value,p)<=keep)return had.Value;   // as Pinned308: written once, not measured again
   notes.Add(id+" feet at "+V308(p)+" (slope "+F308(slope,"F1")+"°)");
   return p;
  }
  // "the editor value wins and the difference is recorded" (relayout308.json rules.y)
  static void OfflineY308(string id,Vector3 at,float yOffline,List<string> notes)
  {
   if(float.IsNaN(yOffline))return;
   notes.Add(id+": ground y "+F308(at.y)+" vs the offline height "+F308(yOffline)+" (Δ "+F308(at.y-yOffline)+" m; the editor value is used)");
  }

  // T5 content half: only rows whose point + checkpoint this content has
  static void EscortRestPlan308(Plan308 plan,PrologueContentSO.Point[] points,WorldMacroPlaytestSO.CheckpointSpec[] checkpoints,List<string> notes)
  {
   var d=Relayout308();if(d==null)return;
   foreach(var r in d.Rows("rests_escort"))
   {
    string id=Str308(r,"id");var p=Array.Find(points,q=>q!=null&&q.Id==id);var cp=Array.Find(checkpoints,c=>c!=null&&c.Id==id);
    if(p==null||cp==null){notes.Add("escort rest "+id+" not in this content; skipped");continue;}
    if(p.Kind!=PrologueInteractionKind.Rest)throw new Refuse308("relayout rests_escort["+id+"]: the content point is not a Rest point");
    var feetRow=Req308(r,"feet");
    var at=Pinned308(id,Num308(r,"x"),Num308(r,"z"),Num308(r,"max_slope"),p.Position,Has308(r,"y_offline")?Num308(r,"y_offline"):float.NaN,notes);
    var feet=PinnedFeet308(id,Num308(feetRow,"x"),Num308(feetRow,"z"),cp.Feet,notes);
    plan.EscortRests.Add((id,at,feet,Mathf.Repeat(Num308(feetRow,"yaw"),360f)));
   }
  }

  // the escort rest benches (a cube with a collider under the stop root) stand ON their content point after the move: they must
  // never count as ground, or a second apply would seat the point on the bench top
  static void RelayoutGroundSkips308(UnityEngine.SceneManagement.Scene s)
  {
   var d=Relayout308();if(d==null)return;
   var ids=new HashSet<string>(Arr308(d.Data,"rests_escort").Select(r=>Opt308(r,"id")).Where(x=>x!=null));if(ids.Count==0)return;
   foreach(var route in Saved308<DemoEscortSceneRoute>(s))
    foreach(var stop in route.Stops??Array.Empty<DemoEscortStop>())
    {
     if(stop==null||stop.Checkpoint==null||stop.Checkpoint.parent==null||!ids.Contains(stop.CheckpointId??""))continue;
     foreach(Transform c in stop.Checkpoint.parent)if(c.name==stop.CheckpointId)foreach(var col in c.GetComponentsInChildren<Collider>(true))skip308.Add(col);
    }
  }

  // ---------- dry runs (nothing saved) ----------

  static string ContentDry308(string scenePath)
  {
   var scene=Open308(scenePath);var session=Session308(scene);var t=Targets308[scene.path];
   var content=Load308<WorldMacroPlaytestSO>(t.content);var layout=Load308<CompactWorldLayoutSO>(t.layout);
   if(session.Content!=content)throw new Refuse308(scene.path+" session Content is "+AssetDatabase.GetAssetPath(session.Content)+", expected "+t.content);
   if(EditorUtility.IsDirty(content)||EditorUtility.IsDirty(layout))throw new Refuse308("the content or layout asset has unsaved in-memory changes (another session?) - save or discard them first");
   PrepareGround308(scene,session);
   var notes=new List<string>();var plan=MakePlan308(content,notes);
   string beforeC=EditorJsonUtility.ToJson(content),beforeL=EditorJsonUtility.ToJson(layout);
   var snapC=Snapshot308(content);var snapL=Snapshot308(layout);List<string> chC,chL;List<Row308> rows;
   try{chC=ApplyContent308(content,plan,notes);chL=ApplyLayout308(layout,plan);rows=DiffRows308(snapC,Snapshot308(content));rows.AddRange(DiffRows308(snapL,Snapshot308(layout)));}
   finally{EditorJsonUtility.FromJsonOverwrite(beforeC,content);EditorJsonUtility.FromJsonOverwrite(beforeL,layout);}
   var sb=new StringBuilder("content-dry "+scene.path+" (nothing is written)\n");
   var d=Relayout308();sb.AppendLine("  relayout data: "+(d==null?"none (the #308 constants)":RelayoutFile308+" sha "+Short308(d.Sha)+(d.Off.Count>0?", groups off: "+string.Join(", ",d.Off.OrderBy(x=>x,StringComparer.Ordinal)):"")));
   if(chC.Count+chL.Count==0)sb.AppendLine("  변경 없음 (content-apply would write nothing)");
   foreach(var ch in chC)sb.AppendLine("  would (content): "+ch);
   foreach(var ch in chL)sb.AppendLine("  would (layout): "+ch);
   foreach(var g in rows.GroupBy(r=>r.group).OrderBy(g=>g.Key,StringComparer.Ordinal))
    sb.AppendLine("  ledger rows of group "+(g.Key.Length==0?"(base)":g.Key)+": "+string.Join(", ",g.Select(r=>r.kind+" "+r.id)));
   foreach(var n in notes)sb.AppendLine("  note "+n);
   Directory.CreateDirectory(Out308);string file=Path.Combine(Out308,"content308_"+Key308(scene.path)+"-dry.txt");File.WriteAllText(file,sb.ToString());
   return sb.Append("  report "+file).ToString();
  }

  static string CampaignDry308()
  {
   var profile=Campaign308();var main=Load308<WorldMacroPlaytestSO>(Targets308[MainScene].content);
   if(EditorUtility.IsDirty(profile))throw new Refuse308(AssetDatabase.GetAssetPath(profile)+" has unsaved in-memory changes (another session?) - save or discard them first");
   var notes=new List<string>();string before=EditorJsonUtility.ToJson(profile);List<string> ch;
   try{ch=EditCampaign308(profile,main,notes);}
   finally{EditorJsonUtility.FromJsonOverwrite(before,profile);}
   var sb=new StringBuilder("campaign-dry "+AssetDatabase.GetAssetPath(profile)+" (nothing is written)\n");
   if(ch.Count==0)sb.AppendLine("  변경 없음 (campaign-apply would write nothing)");
   foreach(var c in ch)sb.AppendLine("  would: "+c);
   foreach(var n in notes)sb.AppendLine("  note "+n);
   Directory.CreateDirectory(Out308);string file=Path.Combine(Out308,"campaign308-dry.txt");File.WriteAllText(file,sb.ToString());
   return sb.Append("  report "+file).ToString();
  }

  // ---------- T3: the gate signature (what the relayout must leave byte-identical) ----------

  static string CampaignSig308(DemoCampaignProfile p)
  {
   string J(string[] a)=>"["+string.Join(",",a??Array.Empty<string>())+"]";
   var sb=new StringBuilder("explicit="+(p.UseExplicitPrerequisites?1:0)+";initial="+J(p.InitialCompletedIds)+"\n");
   int i=0;
   foreach(var s in p.Stages??Array.Empty<DemoCampaignProfile.Stage>())
   {
    if(s==null){sb.Append(i++).Append(":null\n");continue;}
    sb.Append(i++).Append(':').Append(s.Id).Append("|pre=").Append(J(s.PrerequisiteIds)).Append("|optional=").Append(s.Optional?1:0).Append("|tongbo=").Append(s.TongboReward)
      .Append("|implemented=").Append(s.Implemented?1:0).Append("|event=").Append((int)s.Event).Append("|trigger=").Append(s.TriggerId??"")
      .Append("|defeated=").Append(J(s.RequiredDefeatedIds)).Append("|facts=").Append(J(s.RequiredFacts)).Append("|grants=").Append(J(s.GrantedFacts)).Append("|act=").Append(s.ActId??"").Append('\n');
   }
   return sb.ToString();
  }
  static string SigDiff308(string a,string b)
  {
   var la=a.Split('\n');var lb=b.Split('\n');var diff=new List<string>();
   for(int i=0;i<Math.Max(la.Length,lb.Length)&&diff.Count<3;i++)
   {
    string x=i<la.Length?la[i]:"(none)",y=i<lb.Length?lb[i]:"(none)";
    if(x!=y)diff.Add("'"+x+"' -> '"+y+"'");
   }
   return string.Join("; ",diff);
  }
  static string CampaignSigReport308()
  {
   var p=Campaign308();string sig=CampaignSig308(p);string sha=BuildingAudit308.ShaText(sig);
   var sb=new StringBuilder("campaign-sig "+AssetDatabase.GetAssetPath(p)+"\n  sha "+sha+" ("+(p.Stages?.Length??0)+" stages)\n");
   foreach(var line in sig.Split('\n'))if(line.Length>0)sb.AppendLine("  "+line);
   Directory.CreateDirectory(Out308);string file=Path.Combine(Out308,"campaign-sig-"+Harness303.UtcStamp()+".txt");File.WriteAllText(file,sb.ToString());
   return sb.Append("  report "+file+" (run before and after the relayout: the sha must be equal)").ToString();
  }

  // a number from the scene data's checks{} (NaN = not there, or no scene data)
  static float CheckNum308(string key)
  {
   try{var t=SceneData308(out _)["checks"]?[key];return t!=null&&(t.Type==JTokenType.Float||t.Type==JTokenType.Integer)?t.Value<float>():float.NaN;}
   catch(Refuse308){return float.NaN;}
  }

  // ---------- T7: ledger rows per element, group revert ----------

  [Serializable] sealed class PathBox308{public Vector3[] v=Array.Empty<Vector3>();}

  // serialized form of every element a relayout row can change, keyed "kind|id"
  static Dictionary<string,string> Snapshot308(Object asset)
  {
   var m=new Dictionary<string,string>();
   void Add<T>(string kind,T[] arr,Func<T,string> idOf)where T:class
   {
    if(arr==null)return;
    foreach(var x in arr){if(x==null)continue;string key=kind+"|"+(idOf(x)??"");if(!m.ContainsKey(key))m[key]=JsonUtility.ToJson(x);}
   }
   if(asset is WorldMacroPlaytestSO c)
   {
    Add("point",c.Points,x=>x.Id);Add("checkpoint",c.Checkpoints,x=>x.Id);Add("encounter",c.Encounters,x=>x.Id);
    m["mainpath|"]=JsonUtility.ToJson(new PathBox308{v=c.MainPath??Array.Empty<Vector3>()});
    m["branchpath|"]=JsonUtility.ToJson(new PathBox308{v=c.BranchPath??Array.Empty<Vector3>()});
   }
   else if(asset is CompactWorldLayoutSO l){Add("place",l.Places,x=>x.Id);Add("route",l.Routes,x=>x.Id);}
   else if(asset is DemoCampaignProfile p)Add("stage",p.Stages,x=>x.Id);
   return m;
  }
  static List<Row308> DiffRows308(Dictionary<string,string> before,Dictionary<string,string> after)
  {
   var rows=new List<Row308>();
   foreach(var key in after.Keys.Concat(before.Keys.Where(k=>!after.ContainsKey(k))))
   {
    before.TryGetValue(key,out string b);after.TryGetValue(key,out string a);b=b??"";a=a??"";
    if(b==a)continue;
    int bar=key.IndexOf('|');string kind=key.Substring(0,bar),id=key.Substring(bar+1);
    rows.Add(new Row308{group=GroupOfRow308(kind,id,b,a),kind=kind,id=id,before=b,after=a});
   }
   return rows;
  }
  static string GroupOfRow308(string kind,string id,string before,string after)
  {
   var d=Relayout308();if(d==null)return "";
   if(kind=="mainpath"||kind=="branchpath"){var p=d.Data["paths"];return p!=null&&d.On(p)?GroupKey308(p):"";}
   if(kind=="stage"){foreach(var r in d.Destinations)if(Str308(r,"stage")==id)return GroupKey308(r);return GateGroupOfStage308(id,before,after);}   // D308-16c: a row that changes ONLY the declared gate fields carries the gate group
   return d.GroupOf.TryGetValue(id,out string g)?g:"";
  }

  static string Current308(Object asset,string kind,string id)
  {
   string Cur<T>(T[] a,Func<T,string> idOf)where T:class{var x=a==null?null:Array.Find(a,e=>e!=null&&idOf(e)==id);return x==null?"":JsonUtility.ToJson(x);}
   if(asset is WorldMacroPlaytestSO c)
    switch(kind)
    {
     case "point":return Cur(c.Points,x=>x.Id);
     case "checkpoint":return Cur(c.Checkpoints,x=>x.Id);
     case "encounter":return Cur(c.Encounters,x=>x.Id);
     case "mainpath":return JsonUtility.ToJson(new PathBox308{v=c.MainPath??Array.Empty<Vector3>()});
     case "branchpath":return JsonUtility.ToJson(new PathBox308{v=c.BranchPath??Array.Empty<Vector3>()});
    }
   if(asset is CompactWorldLayoutSO l)
    switch(kind)
    {
     case "place":return Cur(l.Places,x=>x.Id);
     case "route":return Cur(l.Routes,x=>x.Id);
    }
   if(asset is DemoCampaignProfile p&&kind=="stage")return Cur(p.Stages,x=>x.Id);
   throw new Refuse308("ledger row kind '"+kind+"' does not belong to "+AssetDatabase.GetAssetPath(asset));
  }
  // json "" = the element was absent before: it is removed; else it is put back in place (appended when it is gone)
  static void Put308(Object asset,string kind,string id,string json)
  {
   T[] Put<T>(T[] a,Func<T,string> idOf)where T:class
   {
    var list=(a??Array.Empty<T>()).ToList();int i=list.FindIndex(e=>e!=null&&idOf(e)==id);
    if(json.Length==0){if(i>=0)list.RemoveAt(i);}
    else{var v=JsonUtility.FromJson<T>(json);if(i>=0)list[i]=v;else list.Add(v);}
    return list.ToArray();
   }
   if(asset is WorldMacroPlaytestSO c)
    switch(kind)
    {
     case "point":c.Points=Put(c.Points,x=>x.Id);return;
     case "checkpoint":c.Checkpoints=Put(c.Checkpoints,x=>x.Id);return;
     case "encounter":c.Encounters=Put(c.Encounters,x=>x.Id);return;
     case "mainpath":c.MainPath=JsonUtility.FromJson<PathBox308>(json).v??Array.Empty<Vector3>();return;
     case "branchpath":c.BranchPath=JsonUtility.FromJson<PathBox308>(json).v??Array.Empty<Vector3>();return;
    }
   if(asset is CompactWorldLayoutSO l)
    switch(kind)
    {
     case "place":l.Places=Put(l.Places,x=>x.Id);return;
     case "route":l.Routes=Put(l.Routes,x=>x.Id);return;
    }
   if(asset is DemoCampaignProfile p&&kind=="stage"){p.Stages=Put(p.Stages,x=>x.Id);return;}
   throw new Refuse308("ledger row kind '"+kind+"' does not belong to "+AssetDatabase.GetAssetPath(asset));
  }

  // <revert> --group <G>: per element the group's rows form a chain (oldest before … newest after). The element must still be
  // the newest after-value (else another writer changed it: refuse, or --force) and the chain must be unbroken; the oldest
  // before-value is put back. The rows are marked reverted; the ledger and the pristine backups stay for the whole-ledger revert.
  static string RevertGroup308(string key,string group,bool force)
  {
   var l=ReadLedger308(key);if(l==null)throw new Refuse308("no ledger "+LedgerFile308(key)+" (nothing applied, or already reverted)");
   var d=Relayout308();
   if(d!=null&&!d.Off.Contains(group)&&GroupsInData308(d).Contains(group))
    throw new Refuse308("group '"+group+"' is still on in "+RelayoutFile308+": add it to groups_off first (else the next apply writes it again), then run this revert");
   var live=new List<(string asset,Row308 row)>();
   foreach(var w in l.writes)if(w!=null&&w.rows!=null)foreach(var r in w.rows)if(r!=null&&r.group==group&&!r.reverted)live.Add((w.asset,r));
   if(live.Count==0)
    throw new Refuse308("no live row of group '"+group+"' in "+LedgerFile308(key)+" (groups with live rows: "+
     string.Join(", ",l.writes.Where(w=>w!=null&&w.rows!=null).SelectMany(w=>w.rows).Where(r=>r!=null&&!r.reverted).Select(r=>r.group.Length==0?"(base)":r.group).Distinct().OrderBy(x=>x,StringComparer.Ordinal))+")");
   // verify everything before the first write
   var chains=live.GroupBy(x=>x.asset+"\n"+x.row.kind+"|"+x.row.id).Select(g=>g.ToList()).ToList();
   var assets=new Dictionary<string,Object>();var bad=new List<string>();
   foreach(var chain in chains)
   {
    string path=chain[0].asset;
    if(Harness303.IsProtected(path))throw new Refuse308("protected path in ledger "+path);
    if(!assets.TryGetValue(path,out var asset)){asset=AssetDatabase.LoadMainAssetAtPath(path);if(asset==null)throw new Refuse308("missing asset "+path);assets[path]=asset;}
    var last=chain[chain.Count-1].row;string name=last.kind+" "+last.id;
    if(!RowStands308(asset,last))bad.Add(name+" changed after this ledger's last write of it");
    for(int i=0;i+1<chain.Count;i++)if(!RowsLink308(chain[i].row,chain[i+1].row))bad.Add(name+" was written by another group between two rows of "+group);
   }
   if(bad.Count>0&&!force)throw new Refuse308("group '"+group+"' no longer matches its ledger rows: "+string.Join("; ",bad)+" - nothing reverted; append ' --force' to put the before-values back anyway (the later change is lost)");
   var sb=new StringBuilder(key+" (revert --group "+group+")\n");
   foreach(var b in bad)sb.AppendLine("  WARN forced over: "+b);
   string backup=BackupDir308();
   try
   {
    foreach(var kv in assets)
    {
     var mine=chains.Where(ch=>ch[0].asset==kv.Key).ToList();var asset=kv.Value;
     var w=Edit308(asset,backup,l,()=>
     {
      var ch=new List<string>();
      for(int i=mine.Count-1;i>=0;i--){var first=mine[i][0].row;if(!GatePut308(asset,first))Put308(asset,first.kind,first.id,first.before);ch.Add("group "+group+": "+first.kind+" "+first.id+(first.before.Length==0?" removed":" put back"));}
      if(asset is DemoCampaignProfile p&&!p.IsValid)throw new Refuse308("the campaign would be invalid after this group revert; nothing written");
      return ch;
     },false);
     foreach(var chain in mine)foreach(var x in chain)x.row.reverted=true;
     if(w==null)sb.AppendLine("  "+kv.Key+": already at the before-values (rows marked reverted)");
     else{sb.AppendLine("  wrote "+kv.Key+" (backup "+w.backup+")");foreach(var c in w.changes)sb.AppendLine("    "+c);}
    }
   }
   finally{WriteLedger308(key,l);}
   sb.AppendLine("  ledger kept "+LedgerFile308(key)+" ("+live.Count+" row(s) of "+group+" marked reverted)");
   Directory.CreateDirectory(Out308);File.WriteAllText(Path.Combine(Out308,key+"-revert-"+group+".txt"),sb.ToString());
   return sb.ToString();
  }

  // ---------- status ----------

  static string RelayoutStatus308()
  {
   var d=Relayout308();var sb=new StringBuilder("relayout-status\n");
   if(d==null)sb.AppendLine("  no "+RelayoutFile308+": Content308 runs on its #308 constants");
   else
   {
    sb.AppendLine("  data "+RelayoutFile308+" sha "+Short308(d.Sha)+" version "+(Opt308(d.Data,"version")??"?"));
    foreach(var g in GroupsInData308(d).OrderBy(x=>x,StringComparer.Ordinal))
    {
     var ids=new List<string>();
     foreach(var list in RowLists308)foreach(var r in Arr308(d.Data,list))if(GroupKey308(r)==g)ids.Add(list+":"+Str308(r,"id"));
     var p=d.Data["paths"];if(p!=null&&GroupKey308(p)==g)ids.Add("paths");
     if(d.Campaign!=null)foreach(var r in Arr308(d.Campaign,"destinations"))if(GroupKey308(r)==g)ids.Add("destination:"+Str308(r,"stage"));
     sb.AppendLine("  group "+g+(d.Off.Contains(g)?" OFF":" on")+": "+string.Join(", ",ids));
    }
   }
   var keys=new List<string>{"campaign308"};keys.AddRange(Scenes308.Select(s=>"content308_"+Key308(s)));
   foreach(var key in keys)
   {
    var l=ReadLedger308(key);if(l==null){sb.AppendLine("  ledger "+key+": none");continue;}
    var rows=l.writes.Where(w=>w!=null&&w.rows!=null).SelectMany(w=>w.rows).Where(r=>r!=null).ToList();
    sb.AppendLine("  ledger "+key+": "+l.writes.Count+" write(s); live rows "+(rows.Count(r=>!r.reverted)==0?"none (written before T7, or all reverted)":
     string.Join(", ",rows.Where(r=>!r.reverted).GroupBy(r=>r.group).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>(g.Key.Length==0?"(base)":g.Key)+" "+g.Count()))));
   }
   foreach(var s in Scenes308)
   {
    var l=ReadSceneLedger308(SceneKey308(s));if(l==null){sb.AppendLine("  ledger "+SceneKey308(s)+": none");continue;}
    var ops=l.writes.SelectMany(w=>w.ops).Where(o=>o!=null).ToList();
    sb.AppendLine("  ledger "+SceneKey308(s)+": "+l.writes.Count+" write(s); live ops "+string.Join(", ",ops.Where(o=>!o.undone).GroupBy(o=>o.group).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>(g.Key.Length==0?"(base)":g.Key)+" "+g.Count())));
   }
   return sb.ToString();
  }

  // ---------- checks:content, relayout half (data only) ----------

  static void RelayoutChecks308(Check308 k,string tag,WorldMacroPlaytestSO c,CompactWorldLayoutSO layout)
  {
   var d=Relayout308();if(d==null)return;
   k.I(tag+"relayout data sha "+Short308(d.Sha)+(d.Off.Count>0?", groups off: "+string.Join(", ",d.Off.OrderBy(x=>x,StringComparer.Ordinal)):""));
   var points=c.Points??Array.Empty<PrologueContentSO.Point>();var cps=c.Checkpoints??Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>();
   var enc=c.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>();
   float XZ(Vector3 p,float x,float z)=>Vector2.Distance(new Vector2(p.x,p.z),new Vector2(x,z));
   // D01 / T2
   var pd=d.Data["paths"];
   if(pd!=null&&d.On(pd)&&layout!=null)
   {
    float maxOff=Num308(pd,"max_off_road_m");int off=OffRoad308(c.MainPath,layout,maxOff,out float worst);
    k.C(off==0,tag+"D01 MainPath points farther than "+F308(maxOff,"F0")+" m from a layout road: "+off+" of "+(c.MainPath?.Length??0)+" (worst "+F308(worst,"F1")+" m)");
    foreach(var leg in Arr308(pd,"branch_chain"))
    {
     string id=Str308(leg,"route");var route=Array.Find(layout.Routes??Array.Empty<CompactWorldLayoutSO.Route>(),r=>r!=null&&r.Id==id);
     bool has=route!=null&&route.Bends!=null&&route.Bends.Length>=2&&new[]{route.Bends[0],route.Bends[route.Bends.Length-1]}.All(b=>(c.BranchPath??Array.Empty<Vector3>()).Any(q=>Vector2.Distance(new Vector2(q.x,q.z),b)<d.EndMatch));
     k.C(has,tag+"D01 BranchPath holds the road "+id+" (both ends)");
    }
   }
   // pinned / moved rows sit where the data says
   foreach(var r in RestsNow308().Where(x=>x.Pin))
   {
    var p=Array.Find(points,x=>x!=null&&x.Id==r.Id);var cp=Array.Find(cps,x=>x!=null&&x.Id==r.Id);
    k.C(p!=null&&XZ(p.Position,r.X,r.Z)<=d.NodeTol,tag+"relayout rest "+r.Id+" on its pinned place ("+(p!=null?F308(XZ(p.Position,r.X,r.Z))+" m off":"missing")+")");
    if(r.HasFeet)k.C(cp!=null&&XZ(cp.Feet,r.FeetX,r.FeetZ)<=d.NodeTol&&(!r.HasYaw||Mathf.Abs(Mathf.DeltaAngle(cp.Yaw,r.Yaw))<=d.YawTol),tag+"relayout rest "+r.Id+" respawn feet / yaw as the row says");
   }
   foreach(var s in PointsNow308().Where(x=>x.Force))
   {
    var p=Array.Find(points,x=>x!=null&&x.Id==s.Id);
    k.C(p!=null&&XZ(p.Position,s.X,s.Z)<=d.ForceTol,tag+"relayout point "+s.Id+" within "+F308(d.ForceTol,"F1")+" m of its target ("+(p!=null?F308(XZ(p.Position,s.X,s.Z))+" m":"missing")+")");
   }
   foreach(var r in d.Rows("points").Where(row=>!Flag308(row,"force")))
   {
    var p=Array.Find(points,x=>x!=null&&x.Id==Str308(r,"id"));
    k.C(p!=null&&XZ(p.Position,Num308(r,"x"),Num308(r,"z"))<=KeepNear308,tag+"relayout point "+Str308(r,"id")+" within the keep distance of its target ("+(p!=null?F308(XZ(p.Position,Num308(r,"x"),Num308(r,"z")))+" m":"missing")+")");
   }
   foreach(var m in d.Rows("point_moves"))
   {
    var p=Array.Find(points,x=>x!=null&&x.Id==Str308(m,"id"));if(p==null){k.I(tag+"point "+Str308(m,"id")+" not in this content");continue;}
    float off=XZ(p.Position,Num308(m,"x"),Num308(m,"z"));
    k.C(off<=(Flag308(m,"pin")?d.NodeTol:d.PointMoveKeep),tag+"relayout point move "+p.Id+": "+F308(off)+" m from its target");
   }
   foreach(var m in d.Rows("encounter_moves"))
   {
    var e=Array.Find(enc,x=>x!=null&&x.Id==Str308(m,"id"));if(e==null){k.I(tag+"encounter "+Str308(m,"id")+" not in this content (scene without it)");continue;}
    float off=XZ(e.Feet,Num308(m,"x"),Num308(m,"z"));
    k.C(off<=d.EncounterKeep,tag+"relayout encounter move "+e.Id+": "+F308(off)+" m from its target (≤ "+F308(d.EncounterKeep,"F0")+" m: NavMesh snap)");
    if(Has308(m,"respawn")&&Mathf.RoundToInt(Num308(m,"respawn"))>=0)k.C(e.RespawnOnRest==(Mathf.RoundToInt(Num308(m,"respawn"))==1),tag+"relayout encounter "+e.Id+" RespawnOnRest "+(e.RespawnOnRest?1:0)+" as the row says");
   }
   // T5: escort rests moved as a bundle, their words untouched
   foreach(var r in d.Rows("rests_escort"))
   {
    string id=Str308(r,"id");var p=Array.Find(points,x=>x!=null&&x.Id==id);var cp=Array.Find(cps,x=>x!=null&&x.Id==id);
    if(p==null||cp==null){k.I(tag+"escort rest "+id+" not in this content");continue;}
    var feet=Req308(r,"feet");
    k.C(XZ(p.Position,Num308(r,"x"),Num308(r,"z"))<=d.NodeTol&&XZ(cp.Feet,Num308(feet,"x"),Num308(feet,"z"))<=d.NodeTol&&Mathf.Abs(Mathf.DeltaAngle(cp.Yaw,Num308(feet,"yaw")))<=d.YawTol,
     tag+"T5 escort rest "+id+": point and checkpoint where the row says (point "+V308(p.Position)+", feet "+V308(cp.Feet)+", yaw "+F308(cp.Yaw,"F1")+")");
    var keep=Req308(r,"keep");
    bool same=(p.Prompt??"")==(Opt308(keep,"prompt")??"")&&(p.Text??"")==(Opt308(keep,"text")??"")&&Mathf.Abs(p.Radius-Num308(keep,"radius"))<1e-4f&&(cp.Label??"")==(Opt308(keep,"label")??"")&&cp.Shop==Flag308(keep,"shop")&&p.Kind==PrologueInteractionKind.Rest;
    k.C(same,tag+"T5 escort rest "+id+": prompt / text / radius / label / shop unchanged (prompt '"+Clip308(p.Prompt)+"', text '"+Clip308(p.Text)+"', radius "+F308(p.Radius,"F1")+", label '"+cp.Label+"')");
   }
   // T9: the layout place follows
   foreach(var (id,xz) in PlaceMovesNow308())
   {
    var place=layout!=null?Array.Find(layout.Places??Array.Empty<CompactWorldLayoutSO.Place>(),x=>x!=null&&x.Id==id):null;
    if(place==null){k.I(tag+"layout place "+id+" not in this layout");continue;}
    k.C((place.XZ-xz).magnitude<=d.AgreeTol,tag+"T9 layout place "+id+" at ("+F308(place.XZ.x,"F1")+", "+F308(place.XZ.y,"F1")+"), realm "+place.Realm);
   }
  }

  // ---------- scene pass, relayout half ----------

  // the ledger group of a scene-data row: its own "group", else the group of the relayout row that moves its content id
  static string SceneGroup308(JToken row,string id)
  {
   string g=Opt308(row,"group");if(!string.IsNullOrEmpty(g))return g;
   var d=Relayout308();return d!=null&&id!=null&&d.GroupOf.TryGetValue(id,out string x)?x:"";
  }
  static bool SceneGroupOff308(JToken row)
  {
   string g=Opt308(row,"group");if(string.IsNullOrEmpty(g))return false;
   var d=Relayout308();return d!=null&&d.Off.Contains(g);
  }
  // a scene-data row that NAMES a relayout group ("group"): null = handle it; else why it is left alone. Without the relayout
  // data file such a row does not exist for the pass (no file = the #308 behaviour, scene data included); with its group in
  // groups_off it is not placed again (rests, actor moves) - what was placed goes out with scene-revert:<scene> --group <G>.
  static string SceneRowHeld308(JToken row)
  {
   string g=Opt308(row,"group");if(string.IsNullOrEmpty(g))return null;
   var d=Relayout308();
   if(d==null)return "row of relayout group "+g+" and no relayout data file; left as it is";
   return d.Off.Contains(g)?"group "+g+" is off (groups_off); left as it is":null;
  }
  static bool SceneRowNoData308(JToken row)=>!string.IsNullOrEmpty(Opt308(row,"group"))&&Relayout308()==null;
  static IEnumerable<JToken> SceneLive308(IEnumerable<JToken> rows)=>rows.Where(r=>SceneRowHeld308(r)==null);
  // T5: an id that has a rests_escort row gets its altar only while that row is on and the content point / checkpoint feet stand
  // where the row says (node_tol_m); null = fine (or not an escort rest)
  static string EscortRestAway308(string id,PrologueContentSO.Point point,WorldMacroPlaytestSO.CheckpointSpec cp)
  {
   var d=Relayout308();if(d==null)return null;
   var row=Arr308(d.Data,"rests_escort").FirstOrDefault(r=>Opt308(r,"id")==id);if(row==null)return null;
   if(!d.On(row))return "its rests_escort row is off in "+RelayoutFile308;
   var feet=Req308(row,"feet");
   float off=Vector2.Distance(new Vector2(point.Position.x,point.Position.z),new Vector2(Num308(row,"x"),Num308(row,"z")));
   float offFeet=Vector2.Distance(new Vector2(cp.Feet.x,cp.Feet.z),new Vector2(Num308(feet,"x"),Num308(feet,"z")));
   return off>d.NodeTol||offFeet>d.NodeTol?"the content point / checkpoint are "+F308(off)+" / "+F308(offFeet)+" m from its rests_escort row (content-apply has not written this row yet)":null;
  }
  // T4: the facing of a moved body / actor - the scene row's "yaw", else the relayout row's; null = position only
  static float? SceneYaw308(JToken row,string id)
  {
   if(Has308(row,"yaw"))return Mathf.Repeat(Num308(row,"yaw"),360f);
   var d=Relayout308();return d!=null&&d.YawOf.TryGetValue(id,out float y)?y:(float?)null;
  }

  // a props row that is off. Rows WITHOUT a group keep the old behaviour (reported, nothing touched: the seat fix switches the
  // firewood rows off and owns those objects afterwards). A row WITH a group that is off (row or group) takes its placed object
  // out (T7); the ledger keeps where it stood.
  static void PropOff308(Pass308 k,JToken p,string id)
  {
   string g=Opt308(p,"group");
   var holder=string.IsNullOrEmpty(g)?null:FindAll308(k.Scene,SceneRootDefault308+"/Props").FirstOrDefault();
   var have=holder!=null?holder.Find(id):null;
   if(have==null){k.Say("prop "+id+": off in data (proposal)");return;}
   string path=SceneRootDefault308+"/Props/"+id;
   k.Do(new SceneOp308{op="remove",path=path,key=BuildingAudit308.KeyOf(have),id=id,pos=have.position,euler=have.eulerAngles,scale=have.localScale,
    detail="remove "+path+" (its row is off in data) from "+V308(have.position)});
   if(!k.Dry)Object.DestroyImmediate(have.gameObject);
  }

  // Light components / emissive materials on an object (null = none). 발광 상한: the only new sustained lights are shrine candles.
  static string Glow308(GameObject go)
  {
   if(go==null)return null;int lights=go.GetComponentsInChildren<Light>(true).Length;
   var emissive=go.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&m.IsKeywordEnabled("_EMISSION")&&m.HasProperty("_EmissionColor")&&m.GetColor("_EmissionColor").maxColorComponent>1e-3f)
    .Select(m=>m.name).Distinct().ToArray();
   if(lights==0&&emissive.Length==0)return null;
   return lights+" Light component(s)"+(emissive.Length>0?" and emissive material(s) "+string.Join(", ",emissive):"");
  }

  static DemoEscortStop EscortStop308(Pass308 k,string checkpointId,out int count)
  {
   var stops=Saved308<DemoEscortSceneRoute>(k.Scene).SelectMany(x=>x.Stops??Array.Empty<DemoEscortStop>()).Where(s=>s!=null&&s.CheckpointId==checkpointId).ToArray();
   count=stops.Length;return stops.Length==1?stops[0]:null;
  }
  // where the bench node of an escort rest belongs: the content point on the ground, moved by the row's node.offset in the rest
  // frame (+Z toward the respawn feet) and lifted by node.lift_m. D308-16 fix F1: node.yaw (optional) = the bench's world yaw
  // RELATIVE to the rest frame (0 = its long side along the altar's side); without it the node keeps the rotation it has.
  static bool EscortNodeWant308(JToken r,PrologueContentSO.Point point,WorldMacroPlaytestSO.CheckpointSpec cp,out Vector3 want)=>EscortNodeWant308(r,point,cp,out want,out _);
  static bool EscortNodeWant308(JToken r,PrologueContentSO.Point point,WorldMacroPlaytestSO.CheckpointSpec cp,out Vector3 want,out float? wantYaw)
  {
   want=default;wantYaw=null;var no=Req308(r,"node");var off=Req308(no,"offset");
   if(!Ground308(point.Position.x,point.Position.z,out var g,out _))return false;
   float yaw=Harness303.YawTo(g,cp.Feet);
   want=g+Quaternion.Euler(0,yaw,0)*new Vector3(At308(off,0),0,At308(off,1));
   if((At308(off,0)!=0f||At308(off,1)!=0f)&&Ground308(want.x,want.z,out var ng,out _))want.y=ng.y;
   want.y+=Num308(no,"lift_m");
   if(Has308(no,"yaw"))wantYaw=Mathf.Repeat(yaw+Num308(no,"yaw"),360f);
   return true;
  }
  // F1: XZ rectangle (x0, x1, z0, z1) of a set of local boxes in the frame of `frame` (the rest holder: +Z toward the feet)
  static bool FrameRect308(Transform frame,IEnumerable<(Transform t,Bounds local)> boxes,out Vector4 rect)
  {
   float x0=float.PositiveInfinity,x1=float.NegativeInfinity,z0=float.PositiveInfinity,z1=float.NegativeInfinity;bool any=false;
   foreach(var (t,b) in boxes)
    for(int i=0;i<8;i++)
    {
     var c=b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
     var q=frame.InverseTransformPoint(t.TransformPoint(c));any=true;
     x0=Mathf.Min(x0,q.x);x1=Mathf.Max(x1,q.x);z0=Mathf.Min(z0,q.z);z1=Mathf.Max(z1,q.z);
    }
   rect=new Vector4(x0,x1,z0,z1);return any;
  }
  // free XZ distance between two rectangles of one frame (negative = they overlap by that much on the nearer axis)
  static float RectGap308(Vector4 a,Vector4 b)
  {
   float gx=Mathf.Max(b.x-a.y,a.x-b.y),gz=Mathf.Max(b.z-a.w,a.z-b.w);
   if(gx>0f&&gz>0f)return Mathf.Sqrt(gx*gx+gz*gz);
   return Mathf.Max(gx,gz);
  }
  static IEnumerable<(Transform,Bounds)> LocalBoxes308(Transform root)
  {
   foreach(var mf in root.GetComponentsInChildren<MeshFilter>(true))
   {
    var mr=mf.GetComponent<Renderer>();if(mf.sharedMesh==null||mr==null||!mr.enabled||!mf.gameObject.activeInHierarchy)continue;
    yield return (mf.transform,mf.sharedMesh.bounds);
   }
  }

  // T5 scene half: the stop's Checkpoint node onto the content checkpoint (feet + yaw) and the bench node onto the content point.
  // The altar (RestAltar clone + two candles) is an ordinary rests[] row of the scene data with the same id (RestOps308).
  // The stop is found through the scene's DemoEscortSceneRoute (instance references), never by a name path.
  static void EscortRestOps308(Pass308 k)
  {
   var d=Relayout308();if(d==null)return;
   foreach(var r in d.Rows("rests_escort"))
   {
    string id=Str308(r,"id");k.Group=GroupKey308(r);
    var point=Point308(k.Content,id);var cp=Checkpoint308(k.Content,id);
    if(point==null||cp==null){k.Say("escort rest "+id+": not in this content; skipped");continue;}
    var feetRow=Req308(r,"feet");
    float off=Vector2.Distance(new Vector2(point.Position.x,point.Position.z),new Vector2(Num308(r,"x"),Num308(r,"z")));
    float offFeet=Vector2.Distance(new Vector2(cp.Feet.x,cp.Feet.z),new Vector2(Num308(feetRow,"x"),Num308(feetRow,"z")));
    if(off>d.NodeTol||offFeet>d.NodeTol){k.Warn("escort rest "+id+": the content point / checkpoint are "+F308(off)+" / "+F308(offFeet)+" m from the row (content-apply has not written this row yet); the scene nodes are left alone");continue;}
    if(k.Blocked.Contains(id)){k.Say("BLOCKED escort rest "+id+": inside a keep-out; not moved");continue;}
    var stop=EscortStop308(k,id,out int count);
    if(stop==null||stop.Checkpoint==null){k.Warn("escort rest "+id+": "+count+" escort stop(s) use this checkpoint id in this scene; skipped");continue;}
    var cn=stop.Checkpoint;
    if(PostLedger308.UnderProtectedTree(cn)){k.Warn("escort rest "+id+": the stop is under a protected tree ("+Harness303.PathOf(cn)+"); left alone");continue;}
    if(!Arr308(k.Data,"rests").Any(x=>Opt308(x,"id")==id&&Flag308(x,"enabled")))k.Warn("escort rest "+id+": the scene data has no enabled rests[] row with this id, so no altar is placed");
    if(Vector3.Distance(cn.position,cp.Feet)>d.AgreeTol||Mathf.Abs(Mathf.DeltaAngle(cn.eulerAngles.y,cp.Yaw))>k.YawTol)
    {
     k.Do(new SceneOp308{op="trs",id=id,path=Harness303.PathOf(cn),key=BuildingAudit308.KeyOf(cn),pos=cn.localPosition,euler=cn.localEulerAngles,scale=cn.localScale,hasAfter=true,posAfter=cp.Feet,
      detail="move escort checkpoint node "+Harness303.PathOf(cn)+" "+V308(cn.position)+" -> "+V308(cp.Feet)+", yaw "+F308(cn.eulerAngles.y,"F1")+" -> "+F308(cp.Yaw,"F1")});
     if(!k.Dry)cn.SetPositionAndRotation(cp.Feet,Quaternion.Euler(0,cp.Yaw,0));
    }
    else k.Say("ok escort checkpoint node "+id+" at "+V308(cn.position));
    var nodes=cn.parent!=null?cn.parent.Cast<Transform>().Where(c=>c.name==id).ToArray():Array.Empty<Transform>();
    if(nodes.Length!=1){k.Warn("escort rest "+id+": "+nodes.Length+" bench node(s) named "+id+" under the stop; the bench is left alone");continue;}
    var node=nodes[0];
    if(!EscortNodeWant308(r,point,cp,out var want,out float? wantYaw)){k.Warn("escort rest "+id+": no ground under the content point; the bench is left alone");continue;}
    bool turn=wantYaw!=null&&Quaternion.Angle(node.rotation,Quaternion.Euler(0,wantYaw.Value,0))>k.YawTol;
    if(Vector3.Distance(node.position,want)>k.PoseTol||turn)
    {
     k.Do(new SceneOp308{op="trs",id=id,path=Harness303.PathOf(node),key=BuildingAudit308.KeyOf(node),pos=node.localPosition,euler=node.localEulerAngles,scale=node.localScale,hasAfter=true,posAfter=want,
      detail="move escort rest bench "+Harness303.PathOf(node)+" "+V308(node.position)+" -> "+V308(want)+(wantYaw!=null?", yaw "+F308(node.eulerAngles.y,"F1")+" -> "+F308(wantYaw.Value,"F1"):"")});
     if(!k.Dry){node.position=want;if(wantYaw!=null)node.rotation=Quaternion.Euler(0,wantYaw.Value,0);}
    }
    else k.Say("ok escort rest bench "+id+" at "+V308(node.position));
   }
  }

  // scene-check, relayout half
  static void RelayoutSceneChecks308(Pass308 k,Check308 c,Transform root)
  {
   var d=Relayout308();if(d==null)return;
   c.I("relayout data sha "+Short308(d.Sha)+(d.Off.Count>0?", groups off: "+string.Join(", ",d.Off.OrderBy(x=>x,StringComparer.Ordinal)):""));
   var g=Req308(k.Data,"ground");var ck=Req308(k.Data,"checks");
   // T5: Interaction <-> content point <-> Checkpoint agree; the bench stands on the point
   foreach(var r in d.Rows("rests_escort"))
   {
    string id=Str308(r,"id");var point=Point308(k.Content,id);var cp=Checkpoint308(k.Content,id);
    if(point==null||cp==null){c.I("escort rest "+id+" not in this content");continue;}
    var stop=EscortStop308(k,id,out int count);
    c.C(stop!=null&&stop.Checkpoint!=null,"T5 "+id+": one escort stop uses this checkpoint ("+count+")");
    if(stop==null||stop.Checkpoint==null)continue;
    float dc=Vector3.Distance(stop.Checkpoint.position,cp.Feet);
    c.C(dc<=d.AgreeTol&&Mathf.Abs(Mathf.DeltaAngle(stop.Checkpoint.eulerAngles.y,cp.Yaw))<=k.YawTol,"T5 "+id+": Checkpoint node on the content checkpoint (Δ "+F308(dc,"F3")+" m ≤ "+F308(d.AgreeTol,"F3")+", yaw "+F308(stop.Checkpoint.eulerAngles.y,"F1")+" vs "+F308(cp.Yaw,"F1")+")");
    var stopPoint=Point308(k.Content,stop.Id);float di=stopPoint!=null&&stop.Interaction!=null?Vector3.Distance(stop.Interaction.position,stopPoint.Position):float.PositiveInfinity;
    c.C(di<=d.AgreeTol,"T5 "+id+": the stop's Interaction ("+stop.Id+") on its content point (Δ "+(float.IsPositiveInfinity(di)?"-":F308(di,"F3"))+" m; Escort303 audit A1)");
    var nodes=stop.Checkpoint.parent!=null?stop.Checkpoint.parent.Cast<Transform>().Where(x=>x.name==id).ToArray():Array.Empty<Transform>();
    if(nodes.Length==1&&EscortNodeWant308(r,point,cp,out var want,out float? wantYaw))
    {
     c.C(Vector3.Distance(nodes[0].position,want)<=d.NodeTol,"T5 "+id+": bench node where the row puts it (Δ "+F308(Vector3.Distance(nodes[0].position,want),"F3")+" m ≤ "+F308(d.NodeTol,"F2")+"; content point "+V308(point.Position)+")");
     if(wantYaw!=null)c.C(Quaternion.Angle(nodes[0].rotation,Quaternion.Euler(0,wantYaw.Value,0))<=k.YawTol,"T5 "+id+": bench yaw "+F308(nodes[0].eulerAngles.y,"F1")+" vs the row's "+F308(wantYaw.Value,"F1")+" (rest frame + node.yaw)");
     // F1: the bench stands beside the altar - clear of the altar's bounds (the stone pile is part of that mesh) and never in front of the candles
     var no=Req308(r,"node");var holder=root!=null?root.Find("Rests/"+id):null;
     if(Has308(no,"clear_min_m")&&holder!=null)
     {
      float clearMin=Num308(no,"clear_min_m");
      bool okA=FrameRect308(holder,LocalBoxes308(holder),out var altar);
      var boxes=nodes[0].GetComponentsInChildren<BoxCollider>(true).Select(b=>(b.transform,new Bounds(b.center,b.size))).ToArray();
      bool okB=FrameRect308(holder,boxes.Length>0?boxes:LocalBoxes308(nodes[0]),out var bench);
      if(!okA||!okB)c.C(false,"F1 "+id+": no altar / bench bounds to measure");
      else
      {
       float benchGap=RectGap308(altar,bench);
       c.C(benchGap>=clearMin,"F1 "+id+": bench "+F308(benchGap)+" m clear of the altar bounds (≥ "+F308(clearMin)+"; rest frame: altar x "+F308(altar.x)+" .. "+F308(altar.y)+" z "+F308(altar.z)+" .. "+F308(altar.w)+", bench x "+F308(bench.x)+" .. "+F308(bench.y)+" z "+F308(bench.z)+" .. "+F308(bench.w)+")");
       var candles=holder.GetComponentsInChildren<Light>(true).Where(l=>l.enabled&&l.gameObject.activeInHierarchy).Select(l=>holder.InverseTransformPoint(l.transform.position).z).ToArray();
       if(candles.Length>0)c.C(bench.w<=candles.Min(),"F1 "+id+": bench front edge z "+F308(bench.w)+" is not in front of the candles (candle line z "+F308(candles.Min())+"; +Z = toward the feet)");
       float feetGap=Vector2.Distance(new Vector2(nodes[0].position.x,nodes[0].position.z),new Vector2(cp.Feet.x,cp.Feet.z));
       c.I("F1 "+id+": bench centre "+F308(feetGap)+" m from the respawn feet, "+F308(Harness303.Flat(nodes[0].position,point.Position))+" m from the content point (interaction radius "+F308(point.Radius,"F1")+")");
      }
     }
    }
    else c.C(false,"T5 "+id+": "+nodes.Length+" bench node(s) named "+id+" under the stop");
    var keep=Req308(r,"keep");
    c.C((point.Prompt??"")==(Opt308(keep,"prompt")??"")&&(point.Text??"")==(Opt308(keep,"text")??"")&&Mathf.Abs(point.Radius-Num308(keep,"radius"))<1e-4f&&(cp.Label??"")==(Opt308(keep,"label")??""),
     "T5 "+id+": prompt / text / radius / label unchanged");
   }
   // T4: facing of moved bodies and actors
   foreach(var m in d.Rows("point_moves").Where(row=>Has308(row,"yaw")))
   {
    string id=Str308(m,"id");var bodies=Saved308<WorldMacroContentPoint>(k.Scene).Where(x=>x.Id==id&&!Own308(x.transform)&&x.gameObject.activeInHierarchy).ToArray();
    if(bodies.Length!=1){c.I("T4 "+id+": "+bodies.Length+" body marker(s) in this scene");continue;}
    float dy=Mathf.Abs(Mathf.DeltaAngle(bodies[0].transform.eulerAngles.y,Num308(m,"yaw")));
    c.C(dy<=k.YawTol,"T4 "+id+": body yaw "+F308(bodies[0].transform.eulerAngles.y,"F1")+" vs the row's "+F308(Num308(m,"yaw"),"F1"));
   }
   var actors=Saved308<PrologueEncounter>(k.Scene).ToArray();
   foreach(var m in d.Rows("encounter_moves").Where(row=>Has308(row,"yaw")))
   {
    string id=Str308(m,"id");var a=actors.Where(x=>x.Id==id).ToArray();
    if(a.Length!=1){c.I("T4 "+id+": "+a.Length+" actor(s) in this scene");continue;}
    c.C(Mathf.Abs(Mathf.DeltaAngle(a[0].transform.eulerAngles.y,Num308(m,"yaw")))<=k.YawTol,"T4 "+id+": actor yaw "+F308(a[0].transform.eulerAngles.y,"F1")+" vs the row's "+F308(Num308(m,"yaw"),"F1"));
   }
   // 발광 상한: no_light props carry nothing that glows; candles are the only new lights
   var propsRoot=root!=null?root.Find("Props"):null;
   foreach(var p in Arr308(k.Data,"props").Where(row=>Flag308(row,"no_light")&&Flag308(row,"enabled")&&!SceneGroupOff308(row)))
   {
    string id=Str308(p,"id");var have=propsRoot!=null?propsRoot.Find(id):null;
    if(have==null){c.C(false,"prop "+id+": placed (Content308/Props/"+id+")");continue;}
    string glow=Glow308(have.gameObject);c.C(glow==null,"발광 상한 prop "+id+": no Light / no emissive material"+(glow!=null?" (has "+glow+")":""));
   }
   // A02 / A07 post-conditions: a relayout prop (a props row that names a group) sits on the ground - its pivot within
   // checks.prop_pivot_gap_max_m of ground + y_offset, and, for a row with corner_gap_m [lowest, highest], the bottom of its
   // renderer bounds against the ground at the four bounds corners (a hut placed upright on a slope floats on the downhill side)
   float pivotMax=Num308(ck,"prop_pivot_gap_max_m");
   foreach(var p in Arr308(k.Data,"props").Where(row=>!string.IsNullOrEmpty(Opt308(row,"group"))&&Flag308(row,"enabled")&&Flag308(row,"ground")&&!SceneGroupOff308(row)))
   {
    string id=Str308(p,"id");var have=propsRoot!=null?propsRoot.Find(id):null;if(have==null)continue;   // "placed" is judged above (no_light rows) / by scene-dry
    if(!Ground308(have.position.x,have.position.z,out var pg,out float pslope)){c.C(false,"prop "+id+": no ground under "+V308(have.position));continue;}
    float gapPivot=have.position.y-(pg.y+Num308(p,"y_offset"));
    c.C(Mathf.Abs(gapPivot)<=pivotMax,"prop "+id+": pivot "+F308(gapPivot)+" m off ground + y_offset (|gap| ≤ "+F308(pivotMax)+"; slope "+F308(pslope,"F1")+"°)");
    // D308-16 fix F2: a row with top_max_m is a LOW object (a foundation stone): the top of its renderer bounds stands at most that
    // far over the LOWEST ground under the bounds (four corners + centre); footprint_m [lo, hi] = both horizontal sides of its mesh
    // box in its own frame (so a turned stone is not judged by its inflated world box)
    if(Has308(p,"top_max_m")||p["footprint_m"] is JArray)
    {
     var prs=have.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
     if(prs.Length==0)c.C(false,"prop "+id+": no renderer to measure (top_max_m / footprint_m)");
     else
     {
      var pb=prs[0].bounds;foreach(var r in prs)pb.Encapsulate(r.bounds);
      if(Has308(p,"top_max_m"))
      {
       float low=float.PositiveInfinity;bool allG=true;
       foreach(var q in new[]{new Vector2(pb.min.x,pb.min.z),new Vector2(pb.min.x,pb.max.z),new Vector2(pb.max.x,pb.min.z),new Vector2(pb.max.x,pb.max.z),new Vector2(pb.center.x,pb.center.z)})
       {if(Ground308(q.x,q.y,out var tg,out _))low=Mathf.Min(low,tg.y);else allG=false;}
       float top=pb.max.y-low;
       c.C(allG&&top<=Num308(p,"top_max_m"),"F2 prop "+id+": top "+(allG?F308(top):"-")+" m over the lowest ground under it (≤ "+F308(Num308(p,"top_max_m"))+"; bounds height "+F308(pb.size.y)+", y_offset "+F308(Num308(p,"y_offset"))+")");
      }
      if(p["footprint_m"] is JArray fp&&FrameRect308(have,LocalBoxes308(have),out var own))
      {
       float sx=(own.y-own.x)*Mathf.Abs(have.lossyScale.x),sz=(own.w-own.z)*Mathf.Abs(have.lossyScale.z);
       c.C(Mathf.Min(sx,sz)>=At308(fp,0)&&Mathf.Max(sx,sz)<=At308(fp,1),"F2 prop "+id+": footprint "+F308(sx)+" × "+F308(sz)+" m (each side "+F308(At308(fp,0))+" .. "+F308(At308(fp,1))+")");
      }
     }
    }
    // D308-16 fix F3 (review S1): a row with enemy_clear {encounter} is a place the player reads BEFORE the fight (it has no
    // collider, so the player walks into it): the nearest point of its renderer bounds (XZ) lies outside that encounter's
    // Detection radius. A content without that encounter reports INFO (the row is judged in the scenes that have the enemy).
    if(p["enemy_clear"] is JObject ec)
    {
     string eid=Str308(ec,"encounter");var foe=Encounter308(k.Content,eid);
     var ers=have.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
     if(foe==null)c.I("F3 prop "+id+": encounter "+eid+" is not in this content (enemy_clear not judged)");
     else if(ers.Length==0)c.C(false,"F3 prop "+id+": no renderer to measure (enemy_clear)");
     else
     {
      var eb=ers[0].bounds;foreach(var r in ers)eb.Encapsulate(r.bounds);
      float ex=Mathf.Max(eb.min.x-foe.Feet.x,0f,foe.Feet.x-eb.max.x),ez=Mathf.Max(eb.min.z-foe.Feet.z,0f,foe.Feet.z-eb.max.z);float foeNear=Mathf.Sqrt(ex*ex+ez*ez);
      c.C(foeNear>=foe.Detection,"F3 prop "+id+": renderer bounds "+F308(foeNear)+" m from "+eid+" (≥ its detection "+F308(foe.Detection,"F1")+"; the ruin is read before the fight)");
     }
    }
    if(!(p["corner_gap_m"] is JArray lim))continue;
    var rs=have.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
    if(rs.Length==0){c.C(false,"prop "+id+": no renderer to measure (corner_gap_m)");continue;}
    var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
    float lo=float.PositiveInfinity,hi=float.NegativeInfinity;bool all=true;
    foreach(float cx in new[]{b.min.x,b.max.x})foreach(float cz in new[]{b.min.z,b.max.z})
    {
     if(!Ground308(cx,cz,out var cg,out _)){all=false;continue;}
     float cornerGap=b.min.y-cg.y;lo=Mathf.Min(lo,cornerGap);hi=Mathf.Max(hi,cornerGap);
    }
    c.C(all&&lo>=At308(lim,0)&&hi<=At308(lim,1),"prop "+id+": renderer-bounds bottom − ground at the four bounds corners "+(all?F308(lo)+" .. "+F308(hi):"(no ground under a corner)")+" m (allowed "+F308(At308(lim,0))+" .. "+F308(At308(lim,1))+
     "; bounds "+F308(b.size.x,"F1")+" × "+F308(b.size.z,"F1")+" m; a float over the limit = lower the row's y_offset in the scene data)");
   }
   foreach(var p in Arr308(k.Data,"props").Where(row=>!string.IsNullOrEmpty(Opt308(row,"group"))&&!Flag308(row,"enabled")))
   {
    string id=Str308(p,"id");c.C(propsRoot==null||propsRoot.Find(id)==null,"F2 prop "+id+": its row is off, so it is not in the scene (a replaced prop is taken out by scene-apply)");
   }
   // 발광 상한, counted: every relayout rest carries exactly lights_max enabled Lights (two candles), so the new sustained lights
   // are rows × 2 and nothing else
   int newLights=0,wantLights=0;var restsRoot=root!=null?root.Find("Rests"):null;
   foreach(var token in Arr308(ck,"relayout_rest_light_ids"))
   {
    string id=token.Value<string>();var row=Arr308(k.Data,"rests").FirstOrDefault(x=>Opt308(x,"id")==id);
    if(row==null){c.C(false,"발광 상한 "+id+": no rests[] row in the scene data");continue;}
    if(!Flag308(row,"enabled")||SceneRowHeld308(row)!=null){c.I("발광 상한 "+id+": row off / its group off (not counted)");continue;}
    if(Point308(k.Content,id)==null){c.I("발광 상한 "+id+": not in this content (not counted)");continue;}
    var holder=restsRoot!=null?restsRoot.Find(id):null;int max=Mathf.RoundToInt(Num308(row,"lights_max"));wantLights+=max;
    int on=holder!=null?holder.GetComponentsInChildren<Light>(true).Count(l=>l.enabled&&l.gameObject.activeInHierarchy):0;newLights+=on;
    c.C(holder!=null&&on==max,"발광 상한 "+id+": enabled Lights "+on+" == "+max+" (candles)"+(holder==null?" - compound missing":""));
   }
   c.C(newLights==wantLights,"발광 상한 new sustained lights of the relayout: "+newLights+" (expected "+wantLights+" = candles of the relayout rests that are on in this scene)");
   if(root!=null)
   {
    var lit=root.GetComponentsInChildren<Light>(true).Where(l=>l.enabled&&l.gameObject.activeInHierarchy).ToArray();
    var outside=lit.Where(l=>root.Find("Rests")==null||!l.transform.IsChildOf(root.Find("Rests"))).Select(l=>Harness303.PathOf(l.transform)).ToArray();
    c.C(outside.Length==0,"발광 상한 enabled Lights under "+SceneRootDefault308+": "+lit.Length+", all under Rests (candles / the inn lantern)"+(outside.Length>0?" - outside: "+string.Join(", ",outside.Take(4)):""));
   }
   // rest feet: off the road, clear of colliders
   float roadMin=Num308(ck,"feet_road_min_m"),roadMinWide=Num308(ck,"feet_road_min_wide_m"),wide=Num308(ck,"wide_road_m"),gap=Num308(ck,"feet_gap_min_m");
   float pr=Num308(g,"feet_probe_radius_m"),ph=Num308(g,"feet_probe_height_m");
   foreach(var token in Arr308(ck,"rest_feet_ids"))
   {
    string id=token.Value<string>();var cp=Checkpoint308(k.Content,id);if(cp==null){c.I("rest feet "+id+": not in this content");continue;}
    float rd=RoadDistance308(k.Layout,new Vector2(cp.Feet.x,cp.Feet.z),out string road,out float width);float need=width>=wide?roadMinWide:roadMin;
    c.C(rd>=need,"rest feet "+id+": "+F308(rd,"F1")+" m from the centre of "+road+" (width "+F308(width,"F1")+") ≥ "+F308(need,"F1"));
    float r=pr+gap;
    var near=Physics.OverlapCapsule(cp.Feet+Vector3.up*(r+.1f),cp.Feet+Vector3.up*Mathf.Max(r+.1f,ph-pr),r,~0,QueryTriggerInteraction.Ignore)
     .Where(col=>!Terrain308(col)&&!skip308.Contains(col)&&!Preview308(col.transform)&&!(col is CharacterController)).Select(col=>Harness303.PathOf(col.transform)).Distinct().ToArray();
    c.C(near.Length==0,"rest feet "+id+": capsule gap ≥ "+F308(gap,"F1")+" m to every collider"+(near.Length>0?" (within it: "+string.Join(", ",near.Take(4))+")":""));
   }
   // the moved field boss: its vehicle field stays off the road
   var fields=k.Session.VehicleBossFields308;
   foreach(var m in d.Rows("encounter_moves"))
   {
    var e=Encounter308(k.Content,Str308(m,"id"));if(e==null||fields==null||fields.Fields==null)continue;
    foreach(var f in fields.Fields)
    {
     if(f==null||!f.Enabled)continue;var centre=k.Session.BossFieldCentre308(f);if(Harness303.Flat(centre,e.Feet)>d.FieldCentreTol)continue;
     var path=k.Content.MainPath??Array.Empty<Vector3>();float near=path.Length==0?float.PositiveInfinity:path.Min(p=>Harness303.Flat(p,centre));
     c.C(near>f.Radius,"boss field "+f.Id+" (radius "+F308(f.Radius,"F0")+" m, centre "+V308(centre)+") does not reach MainPath (nearest point "+F308(near,"F1")+" m)");
    }
   }
   // T3: destinations stand on the physical ground (the play scene's content is the campaign's source)
   if(k.Scene.path==MainScene)
   {
    float tol=Num308(ck,"dest_ground_tol_m");var profile=Campaign308();
    foreach(var r in d.Destinations)
    {
     var s=profile.FindStage(Str308(r,"stage"));if(s==null){c.C(false,"T3 stage "+Str308(r,"stage")+" missing");continue;}
     // D308-16 fix F4: a row with support "built" names a point PROVEN to stand on a built floor (a cave gallery, a deck). It is
     // judged against the walkable collider surface under it: that floor lies at most checks.built_floor_tol_m below the point
     // (and the point is not under it). Every other row stays terrain-judged, exactly as before.
     if(Opt308(r,"support")=="built")
     {
      float tolB=Num308(ck,"built_floor_tol_m");bool floor=BuiltFloor308(s.Destination,tolB,out float fy,out string floorOn);
      c.C(floor,"T3 "+s.Id+".Destination "+V308(s.Destination)+" [support built]: collider floor "+(floor?F308(s.Destination.y-fy)+" m below the point ("+floorOn+")":"none")+" within "+F308(tolB)+" m below it | column: "+Column308(s.Destination));
      continue;
     }
     if(!Ground308(s.Destination.x,s.Destination.z,out var gp,out _)){c.C(false,"T3 "+s.Id+".Destination "+V308(s.Destination)+": no ground under it");continue;}
     bool onGround=Mathf.Abs(s.Destination.y-gp.y)<=tol;
     c.C(onGround,"T3 "+s.Id+".Destination "+V308(s.Destination)+": |y - ground| "+F308(Mathf.Abs(s.Destination.y-gp.y))+" ≤ "+F308(tol)+(onGround?"":" | column: "+Column308(s.Destination)+" (a row may carry support \"built\" only when a floor lies within "+F308(Num308(ck,"built_floor_tol_m"))+" m under the point)"));
    }
   }
  }

  // F4: the walkable collider surface at most `tol` below p (and at most agree_tol_m above it). Actors, the player body, look
  // previews and this pass's own root never count (Usable308).
  static bool BuiltFloor308(Vector3 p,float tol,out float y,out string on)
  {
   y=float.NegativeInfinity;on="";var d=Relayout308();float up=d!=null?d.AgreeTol:0f;
   foreach(var h in Physics.RaycastAll(new Vector3(p.x,p.y+1f,p.z),Vector3.down,1f+tol+.01f,~0,QueryTriggerInteraction.Ignore))
   {
    if(!Usable308(h)||h.point.y>p.y+up||h.point.y<p.y-tol||h.point.y<=y)continue;
    y=h.point.y;on=Harness303.PathOf(h.collider.transform);
   }
   return !float.IsNegativeInfinity(y);
  }
  // F4: every walkable surface of the column through p, as "y (Δ to the point) object" - the measured facts behind a T3 line
  static string Column308(Vector3 p)
  {
   var hits=Physics.RaycastAll(new Vector3(p.x,2000f,p.z),Vector3.down,4000f,~0,QueryTriggerInteraction.Ignore).Where(Usable308).OrderByDescending(h=>h.point.y).ToArray();
   if(hits.Length==0)return "no walkable surface";
   return string.Join("; ",hits.Take(6).Select(h=>F308(h.point.y)+" ("+(h.point.y-p.y>=0?"+":"")+F308(h.point.y-p.y)+") "+h.collider.transform.name+(Terrain308(h.collider)?" [terrain rule]":"")))+(hits.Length>6?"; …":"");
  }

  // XZ distance to the nearest layout road centre line (its id and width)
  static float RoadDistance308(CompactWorldLayoutSO layout,Vector2 p,out string road,out float width)
  {
   float best=float.PositiveInfinity;road="-";width=0f;
   foreach(var r in layout.Routes??Array.Empty<CompactWorldLayoutSO.Route>())
   {
    if(r==null||r.Bends==null||r.Bends.Length<2)continue;
    for(int i=1;i<r.Bends.Length;i++){float dd=SegDist308(p,r.Bends[i-1],r.Bends[i]);if(dd<best){best=dd;road=r.Id;width=r.Width;}}
   }
   return best;
  }
 }
}
