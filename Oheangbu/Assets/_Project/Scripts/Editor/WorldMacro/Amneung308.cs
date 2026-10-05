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
using Oheangbu.App.Demo;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // D308-16c (2026-10-05) 금표 암릉: a jointed-granite rock band (visible rocks, no collider) + invisible core BoxColliders + the dressed
 // 국 stone of the logging-site lift, on the ridge crest the cut herb_amneung_cut308 crosses. A landmark of the return loop after 청룡,
 // NOT a wall (both ends are walked around). 국 itself is unlocked by the 청룡 fact as everywhere (HasDemoGuk): no gate data here.
 // Every number and name comes from Art/World/Compact/Rebuild/CliffBoundary308/amneung308.json (stage copy
 // Tools/Unity/Stage308_relayout_ext/Data/amneung308.json, derived from ext_amneung_v2.json by amneung308_derive.py) [TEST values].
 // Queue-safe: Run(string) never opens a dialog; refusals come back as "REFUSED ...". Edit mode only, no dirty scene, the three #308
 // target scenes only (#296 candidate -> #298 candidate -> W_Demo_Main; protected trees, W_Demo_Compact and 03_Content are refused).
 //   status
 //   plan:<scene>            dry: what apply would build, measured on the opened scene's physical ground; nothing is changed
 //   apply:<scene>           byte backup of the scene file -> root "Amneung308" (Rocks / Core / Stone) -> ledger -> only that scene saved
 //   verify:<scene>          read only: rock bottoms buried, core chain without a gap, core tops over the ground, stone and pad tops,
 //                           lift rise, 국 place count, no Light / emission / text, core-fit against the real rock mesh, trees, NavMesh note
 //   revert:<scene>[ --force] removes the root (only objects this ledger wrote; an object added by hand under it refuses without --force)
 //   veg-plan | veg-apply | veg-revert   the trees under the band, ONCE for the three scenes (shared sheet), through the
 //                           BuildingFix308.Veg ledger (BuildingFix308.VegRows.cs); protected sheets are never written
 // Ledger rows carry group (data "group", G10_amneung) so this item is reverted alone: Art/Playtest308/Pacing/ledger_amneung308_<scene>.json.
 // <scene> = arch296 | folk298 | main | the asset path.
 public static partial class Amneung308
 {
  static readonly CultureInfo IC=CultureInfo.InvariantCulture;
  static string DataFile=>Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","CliffBoundary308","amneung308.json");
  static string OutDir=>Path.Combine(Harness303.RepoRoot,"Art","Playtest308","Pacing");
  static string LedgerFile(string alias)=>Path.Combine(OutDir,"ledger_amneung308_"+alias+".json");
  sealed class Refuse:Exception{public Refuse(string m):base(m){}}

  // ---------- data (JsonUtility shapes; keys not listed here are ignored) ----------
  [Serializable] sealed class AssetsD{public string rock_lod0="",rock_lod1="",rock_material="";public float[] lod_screen=Array.Empty<float>();}
  [Serializable] sealed class RockD{public string id="";public float x,y,z,yaw,ground_y;public float[] scale=Array.Empty<float>();}
  [Serializable] sealed class CoreD{public string id="";public float x,y,z,yaw,top_y;public float[] size=Array.Empty<float>();}
  [Serializable] sealed class PartD{public string name="",source="";public float yaw,top_y;public bool collider=true;}
  [Serializable] sealed class PadD{public string id="",name="",source="";public float x,z,yaw,top_y;public float[] size=Array.Empty<float>();}
  [Serializable] sealed class SiteD{public bool enabled;public string name="",reward_id="",lift_pad="",descent_exit="";public float upper_toward_pad_m,node_lift_m;}
  [Serializable] sealed class StoneD{public float x,z,top_y,half_m,yaw_box,rise_m;public string source_root="";public PartD[] parts=Array.Empty<PartD>();public PadD[] pads=Array.Empty<PadD>();public SiteD site=new SiteD();}
  [Serializable] sealed class VegSheetD{public string path="",role="",note="";}
  [Serializable] sealed class VegRowD{public string id="",proto="";public float x,y,z;}
  [Serializable] sealed class VegD{public string tag="";public float tolerance_m,keep_clear_core_m,keep_clear_stone_m;public VegSheetD[] sheets=Array.Empty<VegSheetD>();public VegRowD[] rows=Array.Empty<VegRowD>();}
  [Serializable] sealed class ChecksD
  {
   public float[] rise_m=Array.Empty<float>(),stone_size_m=Array.Empty<float>();
   public float joint_min_m,core_ground_ring_m,core_margin_m,core_over_ground_min_m,core_bury_min_m,rock_bury_min_m,fit_low_rock_m,fit_face_h_m,trs_tol_m,yaw_tol_deg,stone_top_tol_m,pad_top_tol_m,
    support_probe_step_m,raster_cell_m,pad_ground_over_top_max_m,nav_probe_m;
   public int fit_low_cells_max,fit_bare_points_max,gates_expected,gates_cap,gates_reserved;
   public string gates_realm="";
  }
  [Serializable] sealed class DataD
  {
   public string version="",status="",root="",group="";
   public AssetsD assets=new AssetsD();public RockD[] rocks=Array.Empty<RockD>();public CoreD[] cores=Array.Empty<CoreD>();
   public StoneD stone=new StoneD();public VegD vegetation=new VegD();public ChecksD checks=new ChecksD();
   [NonSerialized] public string Sha="";
  }

  // ---------- ledger ----------
  [Serializable] sealed class Row{public string group="",kind="",key="",before="",after="";}
  [Serializable] sealed class Ledger
  {
   public string kind="amneung308",scene="",state="",utc="",dataSha="",dataVersion="",root="",sceneBackup="",sceneShaBefore="",sceneShaAfter="",revertedUtc="";
   public List<Row> rows=new List<Row>();public List<string> notes=new List<string>();
  }
  static Ledger ReadLedger(string alias){string f=LedgerFile(alias);return File.Exists(f)?JsonUtility.FromJson<Ledger>(File.ReadAllText(f)):null;}
  static void WriteLedger(string alias,Ledger l){Directory.CreateDirectory(OutDir);File.WriteAllText(LedgerFile(alias),JsonUtility.ToJson(l,true),new UTF8Encoding(false));}

  public static string Run(string command)
  {
   string c=(command??"").Trim();
   try
   {
    if(c=="status")return Status();
    bool force=c.EndsWith(" --force",StringComparison.Ordinal);if(force)c=c.Substring(0,c.Length-8).TrimEnd();
    if(c=="veg-plan")return Veg("plan");
    if(c=="veg-apply")return Veg("apply");
    if(c=="veg-revert")return Veg("revert");
    {var look=LookRun(c,force);if(look!=null)return look;}       // #308 fix 2 L1: rocks-status | rocks-plan | rocks-apply | rocks-revert (Amneung308.Look.cs)
    if(c.StartsWith("plan:",StringComparison.Ordinal))return Plan(ScenePath(c.Substring(5)));
    if(c.StartsWith("apply:",StringComparison.Ordinal))return Apply(ScenePath(c.Substring(6)));
    if(c.StartsWith("verify:",StringComparison.Ordinal))return Verify(ScenePath(c.Substring(7)));
    if(c.StartsWith("revert:",StringComparison.Ordinal))return Revert(ScenePath(c.Substring(7)),force);
   }
   catch(Refuse r){return "REFUSED "+r.Message;}
   catch(PostLedger308.Refused r){return "REFUSED "+r.Message;}
   catch(Exception e){return "FAILED "+e;}
   return "REFUSED unknown Amneung308 command '"+c+"' (status | plan:<scene> | apply:<scene> | verify:<scene> | revert:<scene> [--force] | veg-plan | veg-apply | veg-revert | rocks-status | rocks-plan:<scene> | rocks-apply:<scene> | rocks-revert:<scene> [--force]; <scene> = arch296 | folk298 | main)";
  }

  static string ScenePath(string token)
  {
   token=(token??"").Trim().Replace('\\','/');
   var hit=PostLedger308.Scenes.FirstOrDefault(s=>s==token||string.Equals(PostLedger308.Short(s),token,StringComparison.OrdinalIgnoreCase));
   if(hit==null)throw new Refuse("scene '"+token+"' is not a #308 target (arch296 | folk298 | main)");
   if(PostLedger308.IsProtectedPath(hit)||Harness303.IsProtected(hit))throw new Refuse("protected path "+hit);
   return hit;
  }

  static DataD Load()
  {
   string f=DataFile;if(!File.Exists(f))throw new Refuse("build data missing: "+f+" (deploy Tools/Unity/Stage308_relayout_ext/Data/amneung308.json there)");
   DataD d;
   try{d=JsonUtility.FromJson<DataD>(File.ReadAllText(f).TrimStart('\uFEFF'));}
   catch(Exception e){throw new Refuse("build data does not parse: "+f+" ("+e.Message+")");}
   if(d==null||string.IsNullOrEmpty(d.root)||string.IsNullOrEmpty(d.group))throw new Refuse("build data: root / group missing in "+f);
   if(d.rocks.Length==0||d.cores.Length==0)throw new Refuse("build data: rocks / cores are empty in "+f);
   foreach(var r in d.rocks)if(r.scale==null||r.scale.Length!=3||r.scale.Any(v=>v<=0))throw new Refuse("build data: rock "+r.id+" needs scale [x, y, z] > 0");
   foreach(var c in d.cores)if(c.size==null||c.size.Length!=3||c.size.Any(v=>v<=0))throw new Refuse("build data: core "+c.id+" needs size [length, height, thickness] > 0");
   foreach(var p in d.stone.pads)if(p.size==null||p.size.Length!=3)throw new Refuse("build data: pad "+p.id+" needs size [x, y, z]");
   if(d.rocks.Select(r=>r.id).Concat(d.cores.Select(c=>c.id)).Distinct().Count()!=d.rocks.Length+d.cores.Length)throw new Refuse("build data: rock / core ids are not unique");
   var k=d.checks;
   if(k.rise_m.Length!=2||k.stone_size_m.Length!=2||k.raster_cell_m<=0||k.support_probe_step_m<=0||k.gates_cap<=0||string.IsNullOrEmpty(k.gates_realm)||k.core_over_ground_min_m<=0)
    throw new Refuse("build data: checks{} is incomplete (rise_m, stone_size_m, raster_cell_m, support_probe_step_m, gates_*, core_over_ground_min_m)");
   if(d.assets.lod_screen.Length!=2)throw new Refuse("build data: assets.lod_screen needs [lod0, lod1]");
   d.Sha=Harness303.Sha(f);return d;
  }

  static string F(float v,string f="F2")=>v.ToString(f,IC);
  static string Trs(Transform t)=>Harness303.V(t.position)+"|"+Harness303.V(t.rotation.eulerAngles)+"|"+Harness303.V(t.lossyScale);
  static string Short(string sha)=>string.IsNullOrEmpty(sha)?"-":sha.Substring(0,Math.Min(12,sha.Length));
  static GameObject RootOf(Scene s,string name,out int count){var all=s.GetRootGameObjects().Where(g=>g.name==name).ToArray();count=all.Length;return all.Length==1?all[0]:null;}
  static List<Transform> FindAll(Scene scene,string path)
  {
   var parts=path.Split('/');var level=scene.GetRootGameObjects().Where(g=>g.name==parts[0]).Select(g=>g.transform).ToList();
   for(int i=1;i<parts.Length;i++){var next=new List<Transform>();foreach(var t in level)foreach(Transform c in t)if(c.name==parts[i])next.Add(c);level=next;}
   return level;
  }

  // physical ground of the opened scene under (x, z): terrain first (TerrainCollider or a *Terrain* / *Surface* object, the Content308
  // rule), else the highest walkable hit; colliders under `skip` (this tool's root), triggers, controllers and preview objects never count
  static bool Ground(float x,float z,Transform skip,out float y)
  {
   y=0f;float terrain=float.NegativeInfinity,any=float.NegativeInfinity;
   foreach(var h in Physics.RaycastAll(new Vector3(x,2000f,z),Vector3.down,4000f,~0,QueryTriggerInteraction.Ignore))
   {
    var c=h.collider;if(c==null||c is CharacterController||h.normal.y<=.5f)continue;
    if(skip!=null&&c.transform.IsChildOf(skip))continue;
    bool preview=false,isTerrain=c is TerrainCollider;
    for(var t=c.transform;t!=null;t=t.parent){if((t.gameObject.hideFlags&HideFlags.DontSave)!=0)preview=true;if(t.name.Contains("Terrain")||t.name.Contains("Surface"))isTerrain=true;}
    if(preview)continue;
    if(isTerrain&&h.point.y>terrain)terrain=h.point.y;
    if(h.point.y>any)any=h.point.y;
   }
   if(!float.IsNegativeInfinity(terrain)){y=terrain;return true;}
   if(!float.IsNegativeInfinity(any)){y=any;return true;}
   return false;
  }

  // box frame of a footprint: Unity yaw about +y (local +x -> (cos, -sin), local +z -> (sin, cos))
  static Vector2 ToLocal(float px,float pz,float cx,float cz,float yaw){float a=yaw*Mathf.Deg2Rad,ca=Mathf.Cos(a),sa=Mathf.Sin(a);float dx=px-cx,dz=pz-cz;return new Vector2(dx*ca-dz*sa,dx*sa+dz*ca);}
  static Vector2 ToWorld(float u,float v,float cx,float cz,float yaw){float a=yaw*Mathf.Deg2Rad,ca=Mathf.Cos(a),sa=Mathf.Sin(a);return new Vector2(cx+u*ca+v*sa,cz-u*sa+v*ca);}

  // ---------- status ----------
  static string Status()
  {
   var sb=new StringBuilder("Amneung308 status (play="+EditorApplication.isPlaying+")\n");
   try{var d=Load();sb.AppendLine("  data "+DataFile+" sha "+Short(d.Sha)+" version "+d.version+": rocks "+d.rocks.Length+", cores "+d.cores.Length+", stone parts "+d.stone.parts.Length+", pads "+d.stone.pads.Length+", site "+(d.stone.site.enabled?d.stone.site.reward_id:"off")+", trees "+d.vegetation.rows.Length+", group "+d.group);}
   catch(Refuse r){sb.AppendLine("  data: "+r.Message);}
   foreach(var s in PostLedger308.Scenes)
   {
    string alias=PostLedger308.Short(s);var l=ReadLedger(alias);
    sb.AppendLine("  "+alias+": ledger "+(l==null?"none":l.state+" "+l.utc+" data "+Short(l.dataSha)+" rows "+l.rows.Count+(l.notes.Count>0?" notes "+l.notes.Count:"")));
   }
   var active=SceneManager.GetActiveScene();sb.Append("  active scene "+active.path+(active.isDirty?" (dirty)":""));
   return sb.ToString();
  }

  // ---------- plan (dry) ----------
  sealed class PlanInfo{public readonly List<string> Lines=new List<string>(),Blocks=new List<string>(),Notes=new List<string>();public Mesh Lod0,Lod1;public Material Mat;public readonly Dictionary<string,Transform> Sources=new Dictionary<string,Transform>();}

  static PlanInfo Survey(Scene scene,DataD d)
  {
   var p=new PlanInfo();
   p.Lod0=AssetDatabase.LoadAssetAtPath<Mesh>(d.assets.rock_lod0);p.Lod1=AssetDatabase.LoadAssetAtPath<Mesh>(d.assets.rock_lod1);p.Mat=AssetDatabase.LoadAssetAtPath<Material>(d.assets.rock_material);
   if(p.Lod0==null)p.Blocks.Add("rock LOD0 mesh missing: "+d.assets.rock_lod0);
   if(p.Lod1==null)p.Blocks.Add("rock LOD1 mesh missing: "+d.assets.rock_lod1);
   if(p.Mat==null)p.Blocks.Add("rock material missing: "+d.assets.rock_material);
   else if(Emissive(p.Mat))p.Blocks.Add("rock material "+d.assets.rock_material+" is emissive (ART-INK: rocks carry no glow)");
   // the stone set is cloned from the scene's own logging-site lift; a scene without it gets the band without the stone (note)
   foreach(var name in d.stone.parts.Select(x=>x.source).Concat(d.stone.pads.Select(x=>x.source)).Distinct())
   {
    string path=d.stone.source_root+"/"+name;var hits=FindAll(scene,path);
    if(hits.Count==1)
    {
     p.Sources[name]=hits[0];
     var rs=hits[0].GetComponentsInChildren<Renderer>(true);
     if(rs.Length==0)p.Notes.Add("stone source "+path+" has no renderer");
     if(rs.SelectMany(r=>r.sharedMaterials).Any(m=>m!=null&&Emissive(m)))p.Blocks.Add("stone source "+path+" carries an emissive material (ART-INK: the stone carries no glow)");
    }
    else p.Notes.Add("SKIP stone source "+path+": found "+hits.Count+" (need exactly 1) - the parts cloned from it are not built in this scene");
   }
   Physics.SyncTransforms();
   int rootCount;var root=RootOf(scene,d.root,out rootCount);var skip=root!=null?root.transform:null;
   p.Lines.Add("root "+d.root+": "+(rootCount==0?"absent":rootCount==1?"PRESENT ("+root.GetComponentsInChildren<Transform>(true).Length+" transforms)":"AMBIGUOUS x"+rootCount));
   if(rootCount>1)p.Blocks.Add(rootCount+" root objects named "+d.root);
   float worstBury=float.MaxValue;string worstRock="";int noGround=0;
   foreach(var r in d.rocks)
   {
    float lo=float.MaxValue;
    for(int i=-1;i<=1;i++)for(int j=-1;j<=1;j++){var w=ToWorld(i*r.scale[0]*.5f,j*r.scale[2]*.5f,r.x,r.z,r.yaw);if(Ground(w.x,w.y,skip,out float gy))lo=Mathf.Min(lo,gy);else noGround++;}
    if(lo<float.MaxValue&&lo-r.y<worstBury){worstBury=lo-r.y;worstRock=r.id;}
   }
   p.Lines.Add("rocks "+d.rocks.Length+": LOD0 "+d.assets.rock_lod0+" ("+(p.Lod0!=null?p.Lod0.triangles.Length/3:0)+" tris each), LOD1 "+d.assets.rock_lod1+", material "+d.assets.rock_material+", no collider; lowest burial "+F(worstBury)+" m at "+worstRock+" (physical ground, 9 points per rock; need "+F(d.checks.rock_bury_min_m)+")");
   if(noGround>0)p.Blocks.Add(noGround+" rock footprint point(s) have no ground under them (is the scene's terrain loaded?)");
   float worstTop=float.MaxValue;string worstCore="";
   foreach(var c in d.cores){if(!Ground(c.x,c.z,skip,out float gy)){p.Blocks.Add("no ground under core "+c.id);continue;}if(c.top_y-gy<worstTop){worstTop=c.top_y-gy;worstCore=c.id;}}
   p.Lines.Add("cores "+d.cores.Length+": invisible BoxColliders, length "+F(d.cores.Min(c=>c.size[0]))+"-"+F(d.cores.Max(c=>c.size[0]))+" m, thickness "+F(d.cores.Min(c=>c.size[2]))+"-"+F(d.cores.Max(c=>c.size[2]))+" m; lowest top over the ground at its centre "+F(worstTop)+" m at "+worstCore);
   if(Ground(d.stone.x,d.stone.z,skip,out float sy))p.Lines.Add("stone at ("+F(d.stone.x)+", "+F(d.stone.z)+"): physical ground "+F(sy)+", top "+F(d.stone.top_y)+" (+"+F(d.stone.top_y-sy)+" m), pads top "+string.Join(" / ",d.stone.pads.Select(x=>F(x.top_y)))+", rise "+F(d.stone.top_y-(d.stone.pads.Length>0?d.stone.pads[0].top_y:float.NaN))+" m");
   else p.Blocks.Add("no ground under the stone");
   try
   {
    var names=new List<string>();int gates=Content308.GukGates308(scene,d.checks.gates_realm,names);bool mine=names.Contains("revisit:"+d.stone.site.reward_id);
    int after=gates+(d.stone.site.enabled&&!mine&&p.Sources.Count>0?1:0);
    p.Lines.Add("국 places in "+d.checks.gates_realm+" now "+gates+" ["+string.Join(", ",names)+"] -> "+after+" after apply (data expects "+d.checks.gates_expected+"; cap "+d.checks.gates_cap+", "+d.checks.gates_reserved+" kept free for the 신목 upper place, so at most "+(d.checks.gates_cap-d.checks.gates_reserved)+")");
    if(after>d.checks.gates_cap-d.checks.gates_reserved)p.Blocks.Add("국 places would be "+after+" > cap "+d.checks.gates_cap+" - reserved "+d.checks.gates_reserved+" (LDB:55)");
   }
   catch(Exception e){p.Notes.Add("국 place count unavailable: "+e.Message);}
   return p;
  }

  static bool Emissive(Material m)
  {
   if(m==null)return false;
   if(m.IsKeywordEnabled("_EMISSION"))return true;
   foreach(var n in new[]{"_EmissionColor","_EmissiveColor"})if(m.HasProperty(n)&&m.GetColor(n).maxColorComponent>1e-4f&&(m.globalIlluminationFlags&MaterialGlobalIlluminationFlags.EmissiveIsBlack)==0)return true;
   return false;
  }

  static string Plan(string scenePath)
  {
   PostLedger308.RequireEditable();var d=Load();var scene=PostLedger308.Open(scenePath);string alias=PostLedger308.Short(scenePath);
   var p=Survey(scene,d);var l=ReadLedger(alias);
   var sb=new StringBuilder("Amneung308 plan "+scenePath+" (nothing is changed)\n  data sha "+Short(d.Sha)+" version "+d.version+", group "+d.group+"\n");
   sb.AppendLine("  ledger: "+(l==null?"none":l.state+" "+l.utc+" data "+Short(l.dataSha)+(l.dataSha==d.Sha?" (= this data)":" (OTHER data: revert first)")));
   foreach(var x in p.Lines)sb.AppendLine("  "+x);
   foreach(var x in p.Notes)sb.AppendLine("  note "+x);
   foreach(var x in p.Blocks)sb.AppendLine("  BLOCK "+x);
   sb.AppendLine("  would create: 1 root, "+d.rocks.Length+" rocks (x2 LOD children), "+d.cores.Length+" cores, "+d.stone.parts.Count(x=>p.Sources.ContainsKey(x.source))+" stone parts, "+d.stone.pads.Count(x=>p.Sources.ContainsKey(x.source))+" pads, "+(d.stone.site.enabled?"1 DemoGukRevisitSite ("+d.stone.site.reward_id+", 3 nodes)":"no site")+"; 0 Light, 0 text");
   sb.AppendLine("  vegetation: "+d.vegetation.rows.Length+" tree row(s) are cleared by 'veg-apply' (once, shared sheet) - not by apply:<scene>");
   sb.AppendLine(p.Blocks.Count==0?"  -> apply:"+alias+" would run":"  -> apply:"+alias+" would REFUSE ("+p.Blocks.Count+" block(s))");
   Directory.CreateDirectory(OutDir);File.WriteAllText(Path.Combine(OutDir,"amneung308-"+alias+"-plan.txt"),sb.ToString(),new UTF8Encoding(false));
   return sb.Append("  report "+Path.Combine(OutDir,"amneung308-"+alias+"-plan.txt")).ToString();
  }

  // ---------- apply ----------
  static readonly Type[] KeepTypes={typeof(Transform),typeof(MeshFilter),typeof(MeshRenderer),typeof(LODGroup)};

  static string Apply(string scenePath)
  {
   PostLedger308.RequireEditable();var d=Load();var scene=PostLedger308.Open(scenePath);string alias=PostLedger308.Short(scenePath);
   var old=ReadLedger(alias);int rootCount;var existing=RootOf(scene,d.root,out rootCount);
   if(old!=null&&old.state=="applied")
   {
    if(old.dataSha==d.Sha&&existing!=null)return "Amneung308 apply "+scenePath+"\n  변경 없음 (root "+d.root+" already built from this data, ledger "+old.utc+"); run verify:"+alias;
    throw new Refuse("ledger "+LedgerFile(alias)+" says applied ("+old.utc+", data "+Short(old.dataSha)+") but "+(existing==null?"the root is gone":"the data changed to "+Short(d.Sha))+": run revert:"+alias+" first (nothing written)");
   }
   if(rootCount>0)throw new Refuse("a root object named "+d.root+" already exists in "+scenePath+" without an applied ledger: it is not this tool's - remove or rename it by hand (nothing written)");
   var p=Survey(scene,d);
   if(p.Blocks.Count>0)throw new Refuse("plan blocks ("+p.Blocks.Count+"): "+string.Join("; ",p.Blocks)+" (nothing written)");
   string utc=DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff",IC);
   var l=new Ledger{scene=scenePath,state="applying",utc=utc,dataSha=d.Sha,dataVersion=d.version,root=d.root,sceneShaBefore=Harness303.Sha(Harness303.Abs(scenePath))};
   l.sceneBackup=PostLedger308.Backup(Path.Combine(OutDir,"Backups"),utc,scenePath);
   l.notes.AddRange(p.Notes);
   GameObject root=null;
   try
   {
    root=new GameObject(d.root);SceneManager.MoveGameObjectToScene(root,scene);
    void Log(string kind,Transform t)=>l.rows.Add(new Row{group=d.group,kind=kind,key=BuildingAudit308.KeyOf(t),before="",after=Trs(t)});
    Transform Group(string name){var g=new GameObject(name);g.transform.SetParent(root.transform,false);return g.transform;}
    var rocks=Group("Rocks");var cores=Group("Core");var stone=Group("Stone");
    foreach(var r in d.rocks)
    {
     var go=new GameObject(r.id);go.transform.SetParent(rocks,false);
     go.transform.SetPositionAndRotation(new Vector3(r.x,r.y,r.z),Quaternion.Euler(0f,r.yaw,0f));go.transform.localScale=new Vector3(r.scale[0],r.scale[1],r.scale[2]);
     var renderers=new Renderer[2];
     for(int lod=0;lod<2;lod++)
     {
      var child=new GameObject("LOD"+lod);child.transform.SetParent(go.transform,false);
      child.AddComponent<MeshFilter>().sharedMesh=lod==0?p.Lod0:p.Lod1;
      var mr=child.AddComponent<MeshRenderer>();mr.sharedMaterial=p.Mat;mr.shadowCastingMode=lod==0?UnityEngine.Rendering.ShadowCastingMode.On:UnityEngine.Rendering.ShadowCastingMode.Off;renderers[lod]=mr;
     }
     var group=go.AddComponent<LODGroup>();group.SetLODs(new[]{new LOD(d.assets.lod_screen[0],new[]{renderers[0]}),new LOD(d.assets.lod_screen[1],new[]{renderers[1]})});group.RecalculateBounds();
     Log("rock",go.transform);
    }
    foreach(var c in d.cores)
    {
     var go=new GameObject(c.id);go.transform.SetParent(cores,false);
     go.transform.SetPositionAndRotation(new Vector3(c.x,c.y,c.z),Quaternion.Euler(0f,c.yaw,0f));
     var box=go.AddComponent<BoxCollider>();box.center=Vector3.zero;box.size=new Vector3(c.size[0],c.size[1],c.size[2]);
     Log("core",go.transform);
    }
    GameObject Clone(Transform src,string name,bool keepCollider)
    {
     var go=Object.Instantiate(src.gameObject,stone,true);go.name=name;go.SetActive(true);
     foreach(var t in go.GetComponentsInChildren<Transform>(true))t.gameObject.SetActive(true);
     // only the look and (when asked) the collision of the stone are taken over: no interaction, no light, no script
     foreach(var comp in go.GetComponentsInChildren<Component>(true).Reverse())
     {
      if(comp==null||KeepTypes.Contains(comp.GetType()))continue;
      if(comp is Collider col){if(keepCollider){col.isTrigger=false;col.enabled=true;continue;}}
      Object.DestroyImmediate(comp);
     }
     return go;
    }
    Bounds LookBounds(GameObject go)
    {
     var rs=go.GetComponentsInChildren<Renderer>(true);if(rs.Length==0)throw new Refuse("stone clone "+go.name+" has no renderer (nothing written)");
     var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;
    }
    foreach(var part in d.stone.parts)
    {
     if(!p.Sources.TryGetValue(part.source,out var src)){l.notes.Add("SKIP stone part "+part.name+": source "+part.source+" absent in this scene");continue;}
     var go=Clone(src,part.name,part.collider);
     go.transform.rotation=Quaternion.Euler(0f,part.yaw-src.rotation.eulerAngles.y,0f)*src.rotation;
     var b=LookBounds(go);
     go.transform.position+=new Vector3(d.stone.x-b.center.x,part.top_y-b.max.y,d.stone.z-b.center.z);
     Log("stone",go.transform);
    }
    var padTop=new Dictionary<string,Vector3>();
    foreach(var pad in d.stone.pads)
    {
     if(!p.Sources.TryGetValue(pad.source,out var src)){l.notes.Add("SKIP pad "+pad.name+": source "+pad.source+" absent in this scene");continue;}
     var go=Clone(src,pad.name,true);
     go.transform.rotation=Quaternion.Euler(0f,pad.yaw,0f);go.transform.localScale=new Vector3(pad.size[0],pad.size[1],pad.size[2]);
     var b=LookBounds(go);
     go.transform.position+=new Vector3(pad.x-b.center.x,pad.top_y-b.max.y,pad.z-b.center.z);
     padTop[pad.id]=new Vector3(pad.x,pad.top_y,pad.z);
     Log("pad",go.transform);
    }
    var sd=d.stone.site;
    if(sd.enabled)
    {
     if(!padTop.TryGetValue(sd.lift_pad,out var liftAt)||!padTop.TryGetValue(sd.descent_exit,out var exitAt)||!d.stone.parts.All(x=>p.Sources.ContainsKey(x.source)))
      l.notes.Add("SKIP site "+sd.name+": the stone or its pads are not built in this scene");
     else
     {
      var go=new GameObject(sd.name);go.transform.SetParent(stone,false);go.transform.position=new Vector3(d.stone.x,d.stone.top_y,d.stone.z);
      var site=go.AddComponent<DemoGukRevisitSite>();site.RewardId=sd.reward_id;
      Transform Node(string name,Vector3 at){var n=new GameObject(name);n.transform.SetParent(go.transform,false);n.transform.position=at;return n.transform;}
      var toward=new Vector3(liftAt.x-d.stone.x,0f,liftAt.z-d.stone.z).normalized;
      site.LiftPad=Node("LiftPad",liftAt+Vector3.up*sd.node_lift_m);
      site.UpperSurface=Node("UpperSurface",new Vector3(d.stone.x,d.stone.top_y+sd.node_lift_m,d.stone.z)+toward*sd.upper_toward_pad_m);
      site.DescentExit=Node("DescentExit",exitAt+Vector3.up*sd.node_lift_m);
      Log("site",go.transform);
     }
    }
    Log("root",root.transform);
    // canon guard before anything is saved: no Light, nothing emissive, no text component under the root
    var lights=root.GetComponentsInChildren<Light>(true).Length;
    var glow=root.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&Emissive(m)).Select(m=>m.name).Distinct().ToArray();
    var texts=root.GetComponentsInChildren<Component>(true).Where(c=>c!=null&&TextType(c.GetType())).Select(c=>c.GetType().Name).Distinct().ToArray();
    if(lights>0||glow.Length>0||texts.Length>0)throw new Refuse("the built root would carry "+lights+" Light(s), emissive material(s) ["+string.Join(", ",glow)+"], text component(s) ["+string.Join(", ",texts)+"] (nothing written)");
    WriteLedger(alias,l);                       // journal first: a crash between here and the save leaves "applying" + the backup
    PostLedger308.SaveScene(scene);
    l.state="applied";l.sceneShaAfter=Harness303.Sha(Harness303.Abs(scenePath));WriteLedger(alias,l);
   }
   catch
   {
    if(root!=null)Object.DestroyImmediate(root);
    if(File.Exists(LedgerFile(alias))&&ReadLedger(alias)?.state=="applying"){l.state="failed";WriteLedger(alias,l);}
    throw;
   }
   var sb=new StringBuilder("Amneung308 apply "+scenePath+"\n  built root "+d.root+": "+l.rows.Count(r=>r.kind=="rock")+" rocks, "+l.rows.Count(r=>r.kind=="core")+" cores, "+l.rows.Count(r=>r.kind=="stone")+" stone parts, "+l.rows.Count(r=>r.kind=="pad")+" pads, "+l.rows.Count(r=>r.kind=="site")+" site (group "+d.group+")\n");
   foreach(var n in l.notes)sb.AppendLine("  note "+n);
   sb.AppendLine("  scene backup "+l.sceneBackup+" (sha "+Short(l.sceneShaBefore)+" -> "+Short(l.sceneShaAfter)+")");
   sb.AppendLine("  ledger "+LedgerFile(alias));
   sb.AppendLine("  next: verify:"+alias+"; the NavMesh bake of the relayout must run AFTER the three applies (the cores are static colliders)");
   File.WriteAllText(Path.Combine(OutDir,"amneung308-"+alias+"-apply.txt"),sb.ToString(),new UTF8Encoding(false));
   return sb.ToString();
  }

  static bool TextType(Type t)
  {
   for(var x=t;x!=null;x=x.BaseType){string n=x.Name;if(n=="TMP_Text"||n=="TextMesh"||n=="Text"||n=="Canvas"||n=="TextMeshPro"||n=="TextMeshProUGUI")return true;}
   return false;
  }

  // ---------- revert ----------
  static string Revert(string scenePath,bool force)
  {
   PostLedger308.RequireEditable();string alias=PostLedger308.Short(scenePath);
   var l=ReadLedger(alias);if(l==null)throw new Refuse("no ledger "+LedgerFile(alias)+" (nothing applied, or already reverted)");
   if(l.state!="applied"&&l.state!="applying"&&l.state!="failed")throw new Refuse("ledger "+LedgerFile(alias)+" is in state '"+l.state+"'");
   var scene=PostLedger308.Open(scenePath);int rootCount;var root=RootOf(scene,l.root,out rootCount);
   var sb=new StringBuilder("Amneung308 revert "+scenePath+"\n");
   if(rootCount>1)throw new Refuse(rootCount+" root objects named "+l.root+": remove the one that is not this tool's by hand (nothing written)");
   if(root!=null)
   {
    // only what the ledger wrote: an object under the root that the ledger does not know was added by hand
    var known=new HashSet<string>(l.rows.Select(r=>r.key));var extra=new List<string>();
    // review 2, finding 7: depth 1 as well - a child of the root that is neither a ledger row nor one of the group holders the rows sit in
    var holders=new HashSet<string>(l.rows.Select(r=>{var s=(r.key??"").Split('/');if(s.Length<3)return "";int h=s[1].LastIndexOf('#');return h>0?s[1].Substring(0,h):s[1];}));
    foreach(Transform groupT in root.transform)
    {
     bool holder=holders.Contains(groupT.name);
     if(!holder&&!known.Contains(BuildingAudit308.KeyOf(groupT))){extra.Add(Harness303.PathOf(groupT));continue;}
     if(holder)foreach(Transform t in groupT)if(!known.Contains(BuildingAudit308.KeyOf(t)))extra.Add(Harness303.PathOf(t));
    }
    if(extra.Count>0&&!force)throw new Refuse(extra.Count+" object(s) under "+l.root+" are not in the ledger ("+string.Join(", ",extra.Take(5))+"): move them out, or append ' --force' to remove them with the root (nothing written)");
    foreach(var e in extra)sb.AppendLine("  WARN forced: removed with the root: "+e);
    string utc=DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff",IC);
    string backup=PostLedger308.Backup(Path.Combine(OutDir,"Backups"),utc,scenePath);
    Object.DestroyImmediate(root);PostLedger308.SaveScene(scene);
    sb.AppendLine("  removed root "+l.root+" ("+l.rows.Count+" ledger row(s) of group "+string.Join(", ",l.rows.Select(r=>r.group).Distinct())+"); backup of the state before this revert "+backup);
   }
   else sb.AppendLine("  root "+l.root+" is not in the scene (nothing to remove)");
   l.state="reverted";l.revertedUtc=PostLedger308.Utc();WriteLedger(alias,l);
   string archived=LedgerFile(alias)+".reverted-"+Harness303.UtcStamp();File.Move(LedgerFile(alias),archived);
   return sb.Append("  ledger archived "+archived+" (the pre-apply scene bytes stay at "+l.sceneBackup+")").ToString();
  }

  // ---------- vegetation (shared sheet, once) ----------
  static string Veg(string mode)
  {
   var d=Load();var v=d.vegetation;
   if(string.IsNullOrEmpty(v.tag)||v.tolerance_m<=0)throw new Refuse("build data: vegetation.tag / tolerance_m missing");
   var sheets=v.sheets.Where(s=>s.role=="rendered").ToArray();
   if(sheets.Length==0)throw new Refuse("build data: vegetation.sheets lists no sheet with role 'rendered'");
   var asks=new List<BuildingFix308.VegRowAsk308>();
   foreach(var s in sheets)
   {
    if(PostLedger308.IsProtectedPath(s.path)||Harness303.IsProtected(s.path))throw new Refuse("protected sheet "+s.path+" (never written)");
    foreach(var r in v.rows)asks.Add(new BuildingFix308.VegRowAsk308{sheet=s.path,id=r.id,proto=r.proto,at=new Vector3(r.x,r.y,r.z)});
   }
   string head="Amneung308 veg-"+mode+" (tag "+v.tag+", "+v.rows.Length+" row(s) x "+sheets.Length+" rendered sheet(s); shared by the three scenes)\n";
   string body=mode=="plan"?BuildingFix308.VegRowsPlan(v.tag,asks,v.tolerance_m):mode=="apply"?BuildingFix308.VegRowsApply(v.tag,asks,v.tolerance_m):BuildingFix308.VegRowsRevert(v.tag,v.tolerance_m);
   foreach(var s in v.sheets.Where(s=>s.role!="rendered"))head+="  not written ("+s.role+"): "+s.path+" - "+s.note+"\n";
   Directory.CreateDirectory(OutDir);File.WriteAllText(Path.Combine(OutDir,"amneung308-veg-"+mode+".txt"),head+body,new UTF8Encoding(false));
   return head+body;
  }
 }
}
