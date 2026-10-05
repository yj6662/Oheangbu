using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // Scene pass, read-only half: keep-outs against the cliff plan of the stage the scene carries (the file comes from the data file:
 // keepout.plan_files by the stage of the scene's cliff ledger, else plan_file; read at run time, never hard-coded),
 // the 국 lift site list, the amneung survey and the scene ACs. Nothing here changes the scene or an asset.
 public static partial class Content308
 {
  // ---------- keep-outs ----------

  sealed class PlanLine308{public string Id="",Name="",Stage="",Kind="cliff";public Vector2[] Pts;public float LowReach,UpReach;public bool UpperRight,Closed;}

  static float Par308(JToken p,string key){var t=p?[key];return t!=null&&(t.Type==JTokenType.Integer||t.Type==JTokenType.Float)?t.Value<float>():0f;}
  static Vector2[] Poly308(JToken t)=>(t as JArray)?.Where(p=>p is JArray a&&a.Count>=2).Select(p=>new Vector2(p[0].Value<float>(),p[1].Value<float>())).ToArray();

  // the plan file of the height stage THIS scene carries (1b cliff ledger): keepout.terrain_ledgers {scene path: cliff ledger file} names the
  // ledger, its state / stage name the stage, keepout.plan_files {stage: plan file} names the file - all three in the data file, none in code.
  // A data file without the two tables, a scene without an applied ledger ('base') or a stage without an entry reads plan_file, as before
  // (plan_file = the stage-1a plan, so a scene at 1a gets exactly the lines it got before this change).
  // Falling back is silent only when it is the expected state (the ledger exists and was never applied / was reverted). It is a WARN - printed
  // by scene-check too, and counted - when the tables cannot name the stage: the scene is not listed, the listed ledger file is missing or
  // does not parse, the ledger is 'applying' (a tiles write that did not finish: the scene may hold meshes of that stage), or an applied stage
  // has no plan file.
  static string PlanFile308(JObject data,string scenePath,List<string> notes)
  {
   string fallback=Str308(data,"plan_file");var kd=data["keepout"];
   var files=kd?["plan_files"] as JObject;var ledgers=kd?["terrain_ledgers"] as JObject;
   if(files==null||ledgers==null)return fallback;
   string stage="base",why="no terrain ledger is listed for this scene in keepout.terrain_ledgers";bool applied=false,warn=true;
   string ledger=Opt308(ledgers,scenePath);
   if(ledger!=null)
   {
    string abs=Path.Combine(Harness303.RepoRoot,ledger);
    if(!File.Exists(abs))why="terrain ledger "+ledger+" does not exist";
    else try
    {
     var j=JObject.Parse(File.ReadAllText(abs));string state=Opt308(j,"state"),st=Opt308(j,"stage");
     if(state=="applied"&&!string.IsNullOrEmpty(st)){stage=st;applied=true;warn=false;why="terrain ledger "+Path.GetFileName(abs)+" applied";}
     else if(state=="applying"){why="terrain ledger "+Path.GetFileName(abs)+" state 'applying' (a tiles write did not finish: the scene may hold meshes of stage '"+(st??"?")+"')";}
     else{warn=false;why="terrain ledger "+Path.GetFileName(abs)+" state '"+(state??"?")+"'";}
    }
    catch(Exception e){why="terrain ledger "+ledger+" does not parse ("+e.Message+")";}
   }
   string pick=Opt308(files,stage);
   if(pick!=null)notes?.Add((warn?"WARN ":"")+"plan file of terrain stage '"+stage+"' ("+why+"): "+pick);
   else notes?.Add((applied||warn?"WARN ":"note ")+"terrain stage '"+stage+"' ("+why+") has no entry in keepout.plan_files: reading plan_file "+fallback);
   return pick??fallback;
  }
  static string PlanShaStage308(Pass308 k){string f=Path.Combine(Harness303.RepoRoot,PlanFile308(k.Data,k.Scene.path,null));return File.Exists(f)?Harness303.Sha(f):"";}

  // cliff / wall lines of the plan as it is on disk now: stage filter + ids + name fragments from the data file
  static List<PlanLine308> PlanLines308(Pass308 k,List<string> notes)
  {
   var kd=Req308(k.Data,"keepout");var lines=new List<PlanLine308>();
   string file=Path.Combine(Harness303.RepoRoot,PlanFile308(k.Data,k.Scene.path,notes));
   if(!File.Exists(file)){notes.Add("WARN plan file missing: "+file+" (no plan-line keep-out was checked)");return lines;}
   JObject plan;try{plan=JObject.Parse(File.ReadAllText(file));}catch(Exception e){notes.Add("WARN plan file does not parse ("+e.Message+"): no plan-line keep-out was checked");return lines;}
   notes.Add("plan "+(Opt308(plan,"version")??"?")+" ("+(Opt308(plan,"date")??"?")+") sha "+Short308(Harness303.Sha(file)));
   var stages=Arr308(kd,"stages").Select(t=>t.Value<string>()).ToArray();var ids=new HashSet<string>(Arr308(kd,"extra_segment_ids").Select(t=>t.Value<string>()));
   var names=Arr308(kd,"extra_name_contains").Select(t=>t.Value<string>()).ToArray();var seenIds=new HashSet<string>();var seenNames=new HashSet<string>();
   foreach(var s in Arr308(plan,"segments"))
   {
    string id=Opt308(s,"id")??"",name=Opt308(s,"name_ko")??"",longName=Opt308(s,"name_long")??"",border=Opt308(s,"border")??"",stage=Opt308(s,"stage")??"";
    bool byStage=stages.Any(x=>stage.StartsWith(x,StringComparison.Ordinal)),byId=ids.Contains(id);
    string byName=names.FirstOrDefault(n=>name.Contains(n)||longName.Contains(n)||border.Contains(n));
    if(!byStage&&!byId&&byName==null)continue;
    if(byId)seenIds.Add(id);if(byName!=null)seenNames.Add(byName);
    var poly=Poly308(s["polyline"]);if(poly==null||poly.Length<2){notes.Add("WARN segment "+id+" has no polyline; not checked");continue;}
    var tech=s["technique"];var prm=tech?["params"];
    lines.Add(new PlanLine308{Id=id,Name=name,Stage=stage,Pts=poly,Closed=Flag308(tech,"closed_ring"),UpperRight=(Opt308(prm,"upper")??"right")=="right",
     LowReach=Par308(prm,"low_ref"),UpReach=Mathf.Max(Par308(prm,"top_w")+Par308(prm,"back"),Mathf.Max(Par308(prm,"up_ref"),Par308(prm,"cut_w")))});
   }
   foreach(var id in ids.Where(x=>!seenIds.Contains(x)))notes.Add("WARN keep-out id "+id+" is not in the plan any more (line renamed or removed): update keepout.extra_segment_ids");
   foreach(var n in names.Where(x=>!seenNames.Contains(x)))notes.Add("note no plan segment name / border holds '"+n+"' (the 금표 서릉 line has no segment of its own yet)");
   float wall=Num308(kd,"wall_reach_m");var gw=plan["gate_wall"];
   void Wall(string id,JToken line){var p=Poly308(line);if(p!=null&&p.Length>=2)lines.Add(new PlanLine308{Id=id,Name="장성",Stage="1b",Kind="wall",Pts=p,LowReach=wall,UpReach=wall});}
   Wall("wall_west",gw?["west"]?["line"]);Wall("wall_east",gw?["east"]?["line"]);
   var ga=gw?["gate"]?["A"] as JArray;var gb=gw?["gate"]?["B"] as JArray;
   if(ga!=null&&gb!=null)lines.Add(new PlanLine308{Id="gate",Name="관문",Stage="1b",Kind="wall",Pts=new[]{new Vector2(ga[0].Value<float>(),ga[1].Value<float>()),new Vector2(gb[0].Value<float>(),gb[1].Value<float>())},LowReach=wall,UpReach=wall});
   notes.Add("lines checked: "+(lines.Count==0?"none":string.Join(", ",lines.Select(l=>l.Id+"["+l.Stage+"]"))));
   return lines;
  }
  // metres of free ground between p and the op footprint of a line (negative = inside it)
  static float LineClear308(PlanLine308 l,Vector2 p,float tolerance,out bool onRaised)
  {
   float best=float.PositiveInfinity;int at=1;
   for(int i=1;i<l.Pts.Length;i++){float d=SegDist308(p,l.Pts[i-1],l.Pts[i]);if(d<best){best=d;at=i;}}
   var a=l.Pts[at-1];var b=l.Pts[at];float cross=(b.x-a.x)*(p.y-a.y)-(b.y-a.y)*(p.x-a.x);   // > 0 = left of a -> b (x east, z north)
   bool upperSide=l.Kind!="wall"&&(cross<0)==l.UpperRight;
   onRaised=upperSide&&best<=l.UpReach;
   return best-(upperSide?l.UpReach:l.LowReach)-tolerance;
  }

  sealed class Keep308{public string Kind,Id;public Vector3 At;public float Radius;}
  static List<Keep308> KeepItems308(Pass308 k,List<string> notes)
  {
   var kd=Req308(k.Data,"keepout");var items=new List<Keep308>();
   string file=Path.Combine(Harness303.RepoRoot,Str308(k.Data,"keepout_file"));
   if(!File.Exists(file))throw new Refuse308("keep-out file missing: "+file);
   var skip=new HashSet<string>(Arr308(kd,"skip_kinds").Select(t=>t.Value<string>()));var over=kd["overrides_m"] as JObject;float pad=Num308(kd,"encounter_pad_m");
   foreach(var it in Arr308(JObject.Parse(File.ReadAllText(file)),"items"))
   {
    string kind=Opt308(it,"kind")??"",id=Opt308(it,"id")??"";if(skip.Contains(kind))continue;
    float radius=it["keepout_m"]!=null?it["keepout_m"].Value<float>():0f;Vector3? at=null;
    if(kind=="enc"){var e=Encounter308(k.Content,id);if(e!=null){at=e.Feet;radius=Mathf.Max(radius,e.Detection+e.Leash+pad);}}
    else{var p=Point308(k.Content,id);if(p!=null)at=p.Position;}
    if(over!=null&&over[id]!=null)radius=Mathf.Max(radius,over[id].Value<float>());
    if(at==null){notes.Add("note "+kind+" "+id+" is not in this scene's content; not checked");continue;}
    items.Add(new Keep308{Kind=kind,Id=id,At=at.Value,Radius=radius});
   }
   // every item the scene pass places must have a radius in the keep-out file
   var known=new HashSet<string>(items.Select(i=>i.Id));
   foreach(var id in SceneLive308(Arr308(k.Data,"rests")).Select(r=>Opt308(r,"id")).Concat(Arr308(k.Data["npcs"],"clones").Select(r=>Opt308(r,"id"))).Concat(Arr308(k.Data["actors"],"clones").Select(r=>Opt308(r,"id"))).Concat(SceneLive308(Arr308(k.Data["actors"],"moves")).Select(r=>Opt308(r,"id"))))
    if(id!=null&&!known.Contains(id)&&(Point308(k.Content,id)!=null||Encounter308(k.Content,id)!=null))notes.Add("WARN "+id+" is placed by the scene pass but has no row in the keep-out file");
   return items;
  }

  // fills k.Blocked; returns the number of violations
  static int Keepout308(Pass308 k,bool say)
  {
   var notes=new List<string>();var kd=Req308(k.Data,"keepout");float tolerance=Num308(kd,"line_tolerance_m");
   var lines=PlanLines308(k,notes);var items=KeepItems308(k,notes);
   var roots=new HashSet<string>(Arr308(kd,"cliff_roots").Select(t=>t.Value<string>()));
   var built=k.Scene.GetRootGameObjects().Where(g=>roots.Contains(g.name)&&!Preview308(g.transform)).Select(g=>g.name).ToArray();
   notes.Add("built cliff / wall roots in this scene: "+(built.Length==0?"none of ["+string.Join(", ",roots)+"] (plan lines only)":string.Join(", ",built)));
   int violations=0;
   foreach(var it in items)
   {
    var p=new Vector2(it.At.x,it.At.z);string worst="-";float clear=float.PositiveInfinity;bool raised=false;
    foreach(var l in lines){float c=LineClear308(l,p,tolerance,out bool on);if(on)raised=true;if(c<clear){clear=c;worst=l.Id;}}
    string hitBuilt=null;
    if(built.Length>0)foreach(var col in Physics.OverlapSphere(it.At,it.Radius,~0,QueryTriggerInteraction.Ignore))
     if(!Preview308(col.transform)&&roots.Contains(col.transform.root.name)){hitBuilt=Harness303.PathOf(col.transform);break;}
    bool bad=raised||clear<it.Radius||hitBuilt!=null;
    if(bad){violations++;k.Blocked.Add(it.Id);}
    if(say||bad)k.Say((bad?"VIOLATION ":"ok ")+it.Kind+" "+it.Id+" (keep-out "+F308(it.Radius,"F0")+" m): "+(float.IsPositiveInfinity(clear)?"no plan line":F308(clear,"F0")+" m clear of "+worst)+
     (raised?", ON the raised side of a cliff op":"")+(hitBuilt!=null?", built geometry inside the radius: "+hitBuilt:""));
   }
   // rests: never inside a vehicle boss field, never inside an encounter's detection + leash + pad
   float minMargin=Num308(kd,"rest_margin_min_m"),pad=Num308(kd,"encounter_pad_m");
   var mine=new HashSet<string>(SceneLive308(Arr308(k.Data,"rests")).Select(r=>Opt308(r,"id")));var fields=k.Session.VehicleBossFields308;
   foreach(var cp in k.Content.Checkpoints??Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>())
   {
    if(cp==null||!cp.IsConfigured)continue;var point=Point308(k.Content,cp.Id);
    foreach(var f in fields!=null&&fields.Fields!=null?fields.Fields:Array.Empty<VehicleBossFieldSO.Field>())
    {
     if(f==null||!f.Enabled)continue;var centre=k.Session.BossFieldCentre308(f);
     bool inside=VehicleBossFieldSO.Contains(f,centre,cp.Feet,0f)||point!=null&&VehicleBossFieldSO.Contains(f,centre,point.Position,0f);
     if(!inside)continue;
     violations++;if(mine.Contains(cp.Id))k.Blocked.Add(cp.Id);
     k.Say("VIOLATION rest "+cp.Id+" sits inside the boss field "+f.Id+" (centre "+V308(centre)+", radius "+F308(f.Radius,"F0")+" m, "+F308(Harness303.Flat(centre,cp.Feet),"F0")+" m away)");
    }
    if(!mine.Contains(cp.Id))continue;
    var worst=(k.Content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>()).Where(e=>e!=null).Select(e=>(e,d:Vector3.Distance(e.Feet,cp.Feet))).OrderBy(x=>x.d-(x.e.Detection+x.e.Leash+pad)).FirstOrDefault();
    if(worst.e==null)continue;float margin=worst.d-(worst.e.Detection+worst.e.Leash+pad);bool close=margin<minMargin;
    if(close){violations++;k.Blocked.Add(cp.Id);}
    if(say||close)k.Say((close?"VIOLATION ":"ok ")+"rest "+cp.Id+" to "+worst.e.Id+": "+F308(worst.d,"F1")+" m apart, "+F308(margin,"F1")+" m beyond detection "+F308(worst.e.Detection,"F0")+" + leash "+F308(worst.e.Leash,"F0")+" + "+F308(pad,"F0")+" (min "+F308(minMargin,"F0")+")");
   }
   float road=float.PositiveInfinity;string roadLine="-";
   foreach(var q in k.Content.MainPath??Array.Empty<Vector3>())foreach(var l in lines){float c=LineClear308(l,new Vector2(q.x,q.z),0f,out _);if(c<road){road=c;roadLine=l.Id;}}
   if(say)k.Say("info MainPath to the nearest op footprint: "+(float.IsPositiveInfinity(road)?"-":F308(road,"F0")+" m ("+roadLine+")")+"; the cliff ledger owns the road-clear rule");
   if(say)foreach(var n in notes)k.Say(n);else foreach(var n in notes.Where(x=>x.StartsWith("WARN",StringComparison.Ordinal)))k.Say(n);
   k.Warns+=notes.Count(x=>x.StartsWith("WARN",StringComparison.Ordinal));
   k.Say("keep-out violations: "+violations+(violations>0?" (those items are not placed / moved)":""));
   return violations;
  }

  static string KeepoutReport308(string scenePath)
  {
   var k=Begin308(scenePath,true,out string sha);int v=Keepout308(k,true);
   var sb=new StringBuilder("keepout "+k.Scene.path+" (data sha "+Short308(sha)+")\n");foreach(var l in k.Lines)sb.AppendLine("  "+l);
   sb.AppendLine(v==0?"  RESULT 0 violations":"  RESULT "+v+" violation(s)");
   return SceneOut308(SceneKey308(k.Scene.path)+"-keepout.txt",sb);
  }

  // ---------- lifts: every 국 lift / revisit site, its state and who refers to it ----------

  static ulong LocalId308(Object o)=>GlobalObjectId.GetGlobalObjectIdSlow(o).targetObjectId;
  // lines of the saved scene file that name a file id, outside the object's own structural rows
  static Dictionary<ulong,List<string>> Referrers308(string sceneAbs,Dictionary<ulong,bool> ids)
  {
   var hits=ids.Keys.ToDictionary(x=>x,x=>new List<string>());string block="";
   foreach(var raw in File.ReadLines(sceneAbs))
   {
    if(raw.StartsWith("--- !u!",StringComparison.Ordinal)){block=raw.Substring(4);continue;}
    int at=raw.IndexOf("{fileID: ",StringComparison.Ordinal);if(at<0)continue;
    string line=raw.TrimStart();
    foreach(var kv in ids)
    {
     if(raw.IndexOf("fileID: "+kv.Key+"}",StringComparison.Ordinal)<0)continue;
     // structural rows: the owner's component list (component id) / every component's back pointer (object id)
     if(kv.Value?line.StartsWith("- component:",StringComparison.Ordinal):line.StartsWith("m_GameObject:",StringComparison.Ordinal))continue;
     hits[kv.Key].Add(block+" :: "+line);
    }
   }
   return hits;
  }

  static string Lifts308(string scenePath)
  {
   var k=Begin308(scenePath,true,out _);var sb=new StringBuilder("lifts "+k.Scene.path+"\n");
   var sites=Saved308<GukLiftSite>(k.Scene).ToArray();var revisits=Saved308<DemoGukRevisitSite>(k.Scene).ToArray();
   var ids=new Dictionary<ulong,bool>();foreach(var s in sites){ids[LocalId308(s)]=true;ids[LocalId308(s.gameObject)]=false;}
   var refs=Referrers308(Harness303.Abs(k.Scene.path),ids);
   string Realm(Vector3 p)=>k.Layout.RealmAt(new Vector2(p.x,p.z))?.Id??"-";
   var gd=Req308(k.Data,"gates");string realm=Str308(gd,"realm");var live=new List<string>();var dead=new List<string>();
   foreach(var s in sites.OrderBy(x=>x.Id,StringComparer.Ordinal))
   {
    var lower=s.Lower!=null?s.Lower.position:s.transform.position;string gap="no ground";
    if(Ground308(lower.x,lower.z,out var g,out _))gap="ground "+F308(g.y,"F1")+", Lower is "+F308(lower.y-g.y,"F1")+" m "+(lower.y>=g.y?"above":"below")+" it";
    bool alive=s.isActiveAndEnabled&&s.Valid;string where=Realm(lower);
    (alive?live:dead).Add(s.Id+(where==realm?"":" ("+where+")"));
    sb.AppendLine("  "+s.Id+" | "+Harness303.PathOf(s.transform)+" | activeSelf "+s.gameObject.activeSelf+" inHierarchy "+s.gameObject.activeInHierarchy+" enabled "+s.enabled+" Valid "+s.Valid+" | realm "+where);
    sb.AppendLine("      Lower "+V308(lower)+" Upper "+(s.Upper!=null?V308(s.Upper.position):"-")+" height "+F308(s.Height,"F1")+" m | "+gap);
    var rc=refs[LocalId308(s)];var ro=refs[LocalId308(s.gameObject)];
    sb.AppendLine("      scene referrers: component "+rc.Count+", object "+ro.Count+(rc.Count+ro.Count>0?" -> "+string.Join("; ",rc.Concat(ro).Take(6)):""));
   }
   foreach(var r in revisits)
   {
    var pad=r.LiftPad!=null?r.LiftPad.position:r.transform.position;string where=Realm(pad);bool alive=r.isActiveAndEnabled;
    (alive?live:dead).Add("revisit:"+r.RewardId+(where==realm?"":" ("+where+")"));
    sb.AppendLine("  revisit "+r.RewardId+" | "+Harness303.PathOf(r.transform)+" | active "+alive+" | realm "+where+" | LiftPad "+V308(pad)+" UpperSurface "+(r.UpperSurface!=null?V308(r.UpperSurface.position):"-"));
   }
   sb.AppendLine("  live gates (active + valid): "+(live.Count==0?"none":string.Join(", ",live)));
   sb.AppendLine("  dead sites (inactive, disabled or invalid): "+(dead.Count==0?"none":string.Join(", ",dead)));
   sb.AppendLine("  "+realm+" gate set: "+live.Count(x=>!x.Contains("("))+" now; data expects "+F308(Num308(gd,"expected_after_pass"),"F0")+" after this pass, "+F308(Num308(gd,"expected_with_cliff_top"),"F0")+" with the cliff-top site, cap "+F308(Num308(gd,"cap"),"F0"));
   sb.AppendLine("  code that counts inactive sites too (so 'disable' does not take a dead site out): Enclosure305 check-scene (reserved 'guk-lift'), Seal308 AC-S5, CompactArchitecture296 gate-mobility lift rows");
   return SceneOut308(SceneKey308(k.Scene.path)+"-lifts.txt",sb);
  }

  static string GukSurvey308(string scenePath)
  {
   var k=Begin308(scenePath,true,out _);var d=Req308(k.Data,"lift");var sb=new StringBuilder("guk-survey "+k.Scene.path+" ("+Str308(d,"id")+")\n");
   var rows=new List<string>();var c=SurveyLift308(k,d,rows);foreach(var r in rows)sb.AppendLine("  "+r);
   var have=Saved308<GukLiftSite>(k.Scene).FirstOrDefault(s=>s.Id==Str308(d,"id"));
   sb.AppendLine("  site in scene: "+(have!=null?Harness303.PathOf(have.transform)+" Valid "+have.Valid+" height "+F308(have.Height)+" m":"none"));
   sb.AppendLine(c!=null?"  RESULT a column exists: scene-apply authors the site there":"  RESULT no column: the site stays PENDING until the 바위 띠 is built (or a column is pinned in data)");
   return SceneOut308(SceneKey308(k.Scene.path)+"-guk-survey.txt",sb);
  }

  // ---------- scene-check ----------

  static string SceneCheck308(string scenePath)
  {
   var k=Begin308(scenePath,true,out string dataSha);var c=new Check308();var g=Req308(k.Data,"ground");
   float maxDy=Num308(g,"max_dy_vs_content_m"),dyTol=Num308(g,"actor_max_dy_m"),navR=Num308(g,"nav_radius_m");
   c.I("scene "+k.Scene.path+", data sha "+Short308(dataSha)+", plan sha "+Short308(PlanShaStage308(k))+", ledger "+(File.Exists(LedgerFile308(SceneKey308(k.Scene.path)))?"present":"none"));
   var root=FindAll308(k.Scene,SceneRootDefault308).FirstOrDefault();
   // rests (AC-C7 scene half, ART-INK light rule)
   foreach(var r in Arr308(k.Data,"rests"))
   {
    string id=Str308(r,"id");if(!Flag308(r,"enabled"))continue;
    // a relayout row whose group is off (or with no relayout data) is not part of this scene's wanted state
    string held=SceneRowHeld308(r);if(held!=null){c.I("rest "+id+": "+held+" (not checked)");continue;}
    var point=Point308(k.Content,id);if(point==null){c.C(false,"AC-C7 "+id+": point missing from the content");continue;}
    var holder=root!=null?root.Find("Rests/"+id):null;c.C(holder!=null,"AC-C7 "+id+": compound in the scene ("+SceneRootDefault308+"/Rests/"+id+")");
    if(holder==null)continue;
    bool ground=Ground308(point.Position.x,point.Position.z,out var gp,out _);
    c.C(ground&&Mathf.Abs(holder.position.y-gp.y)<=maxDy&&Harness303.Flat(holder.position,point.Position)<=k.PoseTol,"AC-C7 "+id+": on the content point and the ground (|dy| "+(ground?F308(Mathf.Abs(holder.position.y-gp.y)):"-")+" ≤ "+F308(maxDy)+")");
    c.C(ground&&Mathf.Abs(point.Position.y-gp.y)<=maxDy,"AC-C7 "+id+": content Y matches the ground (|dy| "+(ground?F308(Mathf.Abs(point.Position.y-gp.y)):"-")+")");
    var marker=holder.GetComponentsInChildren<WorldMacroContentPoint>(true);c.C(marker.Length==1&&marker[0].Id==id,"AC-C7 "+id+": one content point marker");
    var lights=holder.GetComponentsInChildren<Light>(true).Where(l=>l.enabled&&l.gameObject.activeInHierarchy).ToArray();int max=Mathf.RoundToInt(Num308(r,"lights_max"));
    c.C(lights.Length<=max&&lights.All(l=>l.shadows==LightShadows.None),"AC-C7 "+id+": lights "+lights.Length+" ≤ "+max+", no shadows");
    // the respawn feet must be free of this compound's own colliders (a clone that covers the feet traps the player on rest)
    var cpFeet=Checkpoint308(k.Content,id);
    if(cpFeet!=null)
    {
     float pr=Num308(g,"feet_probe_radius_m"),ph=Num308(g,"feet_probe_height_m");
     var inside=Physics.OverlapCapsule(cpFeet.Feet+Vector3.up*(pr+.1f),cpFeet.Feet+Vector3.up*(ph-pr),pr,~0,QueryTriggerInteraction.Ignore).Where(col=>Own308(col.transform)).Select(col=>col.name).Distinct().ToArray();
     c.C(inside.Length==0,"AC-C7 "+id+": respawn feet "+V308(cpFeet.Feet)+" clear of the compound's colliders"+(inside.Length>0?" (inside: "+string.Join(", ",inside)+")":""));
    }
    var vis=Array.Find(k.Session.InteractionVisuals??Array.Empty<WorldMacroPlaytestSession.InteractionVisual>(),v=>v!=null&&v.Id==id);
    c.C(vis!=null&&(vis.Renderers??Array.Empty<Renderer>()).Any(x=>x!=null),id+": InteractionVisuals entry with a renderer");
   }
   // npc bodies
   foreach(var n in Arr308(k.Data["npcs"],"clones"))
   {
    string id=Str308(n,"id");if(!Flag308(n,"enabled"))continue;var body=root!=null?root.Find("Npcs/"+id):null;var point=Point308(k.Content,id);
    c.C(body!=null&&point!=null&&Harness303.Flat(body.position,point.Position)<=k.PoseTol,"AC-C8 "+id+": body on its content point");
    if(body!=null){var marker=body.GetComponent<WorldMacroContentPoint>();c.C(body.GetComponentsInChildren<NpcJobActor>(true).All(j=>j.PointId==id)&&marker!=null&&marker.Id==id,"AC-C8 "+id+": marker and NpcJobActor.PointId");}
   }
   foreach(var n in Arr308(k.Data["npcs"],"moves"))
   {
    string id=Str308(n,"id");if(!Flag308(n,"enabled"))continue;var point=Point308(k.Content,id);
    var bodies=Saved308<WorldMacroContentPoint>(k.Scene).Where(x=>x.Id==id&&!Own308(x.transform)&&x.gameObject.activeInHierarchy).ToArray();
    c.C(point!=null&&bodies.Length==1&&Harness303.Flat(bodies[0].transform.position,point.Position)<=k.PoseTol,id+": body on its content point ("+(point!=null&&bodies.Length==1?F308(Harness303.Flat(bodies[0].transform.position,point.Position),"F1")+" m":"-")+")");
   }
   // encounters (AC-C12 scene half): one actor per content encounter; this pass's actors stand on feet + baseOffset, are in the
   // session and wired. Other actors are only reported (their pivots are not this pass's business).
   var actors=Saved308<PrologueEncounter>(k.Scene).ToArray();var sessionActors=k.Session.Actors??Array.Empty<PrologueEncounter>();
   c.C(sessionActors.All(a=>a!=null),"session.Actors holds no missing reference ("+sessionActors.Length+")");
   var passIds=new HashSet<string>(SceneLive308(Arr308(k.Data["actors"],"moves")).Concat(Arr308(k.Data["actors"],"clones")).Where(t=>Flag308(t,"enabled")).Select(t=>Opt308(t,"id")));
   foreach(var e in k.Content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>())
   {
    if(e==null)continue;var a=actors.Where(x=>x.Id==e.Id).ToArray();bool mine=passIds.Contains(e.Id);
    if(a.Length!=1){c.C(false,"AC-C12 "+e.Id+": "+a.Length+" actor(s) in the scene for this content encounter"+(mine?" (scene-apply)":""));continue;}
    var agent=a[0].GetComponent<NavMeshAgent>();var want=e.Feet+Vector3.up*(agent!=null?agent.baseOffset:0f);var at=a[0].transform.position;
    bool posed=Harness303.Flat(at,want)<=k.PoseTol&&Mathf.Abs(at.y-want.y)<=dyTol;
    if(mine)
    {
     c.C(posed,"AC-C12 "+e.Id+": actor on the content feet + baseOffset ("+V308(at)+" vs "+V308(want)+")");
     c.C(sessionActors.Contains(a[0])&&Wired308(k.Session,a[0].GetComponent<EnemyVitals>()),"AC-C12 "+e.Id+": in session.Actors and CombatLoopWiring._enemies");
    }
    else if(!posed)c.I(e.Id+" (not this pass's): actor "+V308(at)+" vs content feet + baseOffset "+V308(want)+" ("+F308(Vector3.Distance(at,want),"F1")+" m)");
   }
   // the session start rule (BindActorsToLoadedNavigation): NavMesh within 2 m under every session actor, else Play throws
   var offMesh=new List<string>();
   foreach(var a in sessionActors)
   {
    if(a==null)continue;var agent=a.GetComponent<NavMeshAgent>();if(agent==null){offMesh.Add(a.Id+" (no agent)");continue;}
    if(!NavMesh.SamplePosition(a.transform.position-Vector3.up*agent.baseOffset,out _,navR,agent.areaMask))offMesh.Add(a.Id);
   }
   c.C(offMesh.Count==0,"session start rule: NavMesh within "+F308(navR,"F0")+" m under every session actor ("+(offMesh.Count==0?"all "+sessionActors.Length:string.Join(", ",offMesh)+" - expected until the shared bake has run")+")");
   var lesson=Point308(k.Content,"metal_growth_lesson");var dok=actors.FirstOrDefault(a=>a.Id=="folklore298/dokkaebi");
   float lessonMin=Num308(Req308(k.Data,"checks"),"dokkaebi_lesson_min_m");
   if(lesson!=null&&dok!=null)c.C(Harness303.Flat(dok.transform.position,lesson.Position)>=lessonMin,"AC-C12 dokkaebi actor to the metal lesson "+F308(Harness303.Flat(dok.transform.position,lesson.Position),"F0")+" m ≥ "+F308(lessonMin,"F0"));
   else c.I("dokkaebi or the metal lesson is absent in this scene");
   // profile wiring (Content308 wire-profiles)
   var fire=actors.FirstOrDefault(a=>a.Id=="mine_fire/0");var agwi=actors.FirstOrDefault(a=>a.Id=="folklore298/agwi");
   if(fire!=null){var ctl=fire.GetComponent<EnemyController>();var p=ctl!=null?new SerializedObject(ctl).FindProperty("_attackProfile"):null;c.C(p!=null&&AssetDatabase.GetAssetPath(p.objectReferenceValue)==MineRanged308,"AC-C11 mine_fire/0 _attackProfile = MineRangedNeutral308 (wire-profiles)");}
   if(agwi!=null){var vit=agwi.GetComponent<EnemyVitals>();var p=vit!=null?new SerializedObject(vit).FindProperty("_profile"):null;c.C(p!=null&&AssetDatabase.GetAssetPath(p.objectReferenceValue)==FieldBoss308,"AC-C12 agwi _profile = EnemyVitals_FieldBoss308 (wire-profiles)");}
   // 국 gate set (D308-9c Q4)
   var ld=Req308(k.Data,"lift");var gd=Req308(k.Data,"gates");string realm=Str308(gd,"realm");var sites=Saved308<GukLiftSite>(k.Scene).ToArray();
   var amneung=sites.FirstOrDefault(s=>s.Id==Str308(ld,"id"));
   AmneungGate308(k,c,ld,amneung);   // D308-16c: the 국 stone of Amneung308 when amneung308.json is deployed, else the #308 check (Content308.GateDelta.cs)
   // D308-9c Q4 takes the dead 청림 산 리프트 out of the 국 gate count: the ids listed in lift_retire.ids must be gone.
   // Dead sites that are not listed (other realms: lift_retire.proposed_ids, a user decision) are reported, not failed.
   var retire=k.Data["lift_retire"];var listed=new HashSet<string>(retire!=null&&Flag308(retire,"enabled")?Arr308(retire,"ids").Select(t=>t.Value<string>()):Enumerable.Empty<string>());
   var deadSites=sites.Where(s=>!s.isActiveAndEnabled||!s.Valid).Select(s=>s.Id).ToArray();
   // mode remove-component: the component is gone; mode disable: it stays, switched off
   bool removeMode=retire!=null&&Opt308(retire,"mode")=="remove-component";
   var deadListed=sites.Where(s=>listed.Contains(s.Id)&&(removeMode||s.enabled)).Select(s=>s.Id).ToArray();
   c.C(listed.Count>0&&deadListed.Length==0,"D308-9c Q4 the listed dead 국 lift sites are retired ("+(listed.Count==0?"lift_retire is off or lists nothing":deadListed.Length==0?string.Join(", ",listed)+(removeMode?": component removed":": component disabled"):"not retired yet: "+string.Join(", ",deadListed))+")");
   var deadOther=deadSites.Where(id=>!listed.Contains(id)).ToArray();
   if(deadOther.Length>0)c.I("dead GukLiftSite(s) not listed for retirement (other realms; lift_retire.proposed_ids is a user decision): "+string.Join(", ",deadOther));
   int gates=sites.Count(s=>s.isActiveAndEnabled&&s.Valid&&s.Lower!=null&&k.Layout.RealmAt(new Vector2(s.Lower.position.x,s.Lower.position.z))?.Id==realm)+
             Saved308<DemoGukRevisitSite>(k.Scene).Count(r=>r.isActiveAndEnabled&&r.LiftPad!=null&&k.Layout.RealmAt(new Vector2(r.LiftPad.position.x,r.LiftPad.position.z))?.Id==realm);
   float expect=Num308(gd,"expected_after_pass"),withTop=Num308(gd,"expected_with_cliff_top"),cap=Num308(gd,"cap");
   c.C(gates<=cap&&(Mathf.Approximately(gates,expect)||Mathf.Approximately(gates,withTop)),"LDB:55 "+realm+" 국 gates "+gates+" (expected "+F308(expect,"F0")+" after this pass, "+F308(withTop,"F0")+" with the cliff-top site; cap "+F308(cap,"F0")+")");
   // keep-outs, boss fields, rest margins
   int before=k.Lines.Count;int violations=Keepout308(k,false);
   c.C(violations==0,"keep-outs: "+violations+" violation(s) (cliff plan lines, built cliff roots, boss fields, rest margins)");
   for(int i=before;i<k.Lines.Count;i++)if(k.Lines[i].StartsWith("VIOLATION",StringComparison.Ordinal)||k.Lines[i].StartsWith("WARN",StringComparison.Ordinal))c.I(k.Lines[i]);
   // escort start (Escort303Fix)
   var es=k.Data["escort_seat"];var stop=es!=null?FindAll308(k.Scene,Str308(es,"stop")).FirstOrDefault():null;var startPoint=es!=null?Point308(k.Content,Str308(es,"point")):null;
   if(stop!=null&&startPoint!=null){var it=stop.Find("Interaction");float miss=it!=null?Vector3.Distance(it.position,startPoint.Position):float.PositiveInfinity;c.C(miss<=k.PoseTol,"escort_start Interaction on its content point ("+(float.IsPositiveInfinity(miss)?"-":F308(miss,"F1")+" m")+"; Escort303Fix move-start)");}
   // relayout (D308-16) scene checks: only with content308_relayout.json present
   RelayoutSceneChecks308(k,c,root);
   // nothing of this pass lives under a protected tree
   c.C(root==null||!PostLedger308.UnderProtectedTree(root),"AC-C17 the scene pass root is outside the protected trees");
   return c.Done("checks-"+SceneKey308(k.Scene.path)+".txt");
  }
 }
}
