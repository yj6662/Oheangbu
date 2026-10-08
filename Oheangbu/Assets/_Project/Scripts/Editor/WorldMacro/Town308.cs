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
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 capital town (user play note 2026-10-06 "황경 성벽 안의 월드는 민가도 좀 많고 번화가의 형태가 있어야 해"; SPEC-WORLD-CAPITAL-TOWN-308,
 // PROPOSED - option A "한 줄 저잣거리", first slice A1) [TEST values throughout]. WORLD DRESSING ONLY: the inside of the capital is beyond the
 // early-access end gate in canon. This tool makes no person, no dialogue, no text, no content row, no NavMesh agent / obstacle.
 // A building = one generated Mesh asset per LOD (the #306 mine-yard JSON shape, built offline on the #297 hanok kit by the stage's
 // town_meshes.py) with EXISTING materials, ONE BoxCollider = its whole plinth rectangle, and step stones without colliders.
 // Every building stands on its own level plinth: the tool measures the PHYSICAL ground under the plinth rectangle of the opened scene
 // and puts the plinth top on its highest point + rules.reveal_min_m; the stone skirt of the mesh covers the slope below. A row whose
 // physical relief is above rules.podium_relief_max_m (or whose door sill is above rules.door_reveal_max_m) REFUSES the whole apply.
 // Every pose, limit, path and GUID comes from Art/World/Compact/Rebuild/Town308/town308.json (built by town_plan.py; the code holds none).
 // Queue-safe: Run(string) never opens a dialog; refusals come back as "REFUSED ...". Ledgers (<ledger_dir>/ledger_town308_assets.json,
 // ledger_town308_scene_<alias>.json) are written BEFORE the asset / scene is saved, with a byte backup of the scene; a second identical
 // apply reports "변경 없음"; a revert takes out exactly what the ledger made. Only the target scene / the created Mesh assets are saved
 // (SaveScene, CreateAsset; never AssetDatabase.SaveAssets).
 //   status
 //   assets-plan | assets-apply | assets-revert     the Mesh assets under asset_dir (once; shared by the three scenes)
 //   plan:<scene>                                   what scene-apply would create, with the physical ground of every row
 //   scene-apply:<scene> | scene-revert:<scene> [--slice <id>]
 //   check:<scene>                                  AC-T1..T11 on the saved scene; read-only
 //   eyes:<scene>[:<label>]                         the Presentation297 shot lines of the slices' stills[] on the physical ground
 // <scene> = arch296 | folk298 | main. Order: assets-apply, then scene-apply arch296 -> folk298 -> main, check x3 (RUN_ORDER_town.md).
 public static partial class Town308
 {
  const string Usage="status | assets-plan | assets-apply | assets-revert | plan:<scene> | scene-apply:<scene> | scene-revert:<scene> [--slice <id>] | check:<scene> | eyes:<scene>[:<label>]";
  static string DataFile=>Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","Town308","town308.json");
  sealed class Refuse:Exception{public Refuse(string m):base(m){}}

  public static string Run(string command)
  {
   string c=(command??"").Trim();
   try
   {
    if(c=="status")return Status();
    if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Edit mode only (Play is running)";
    if(EditorApplication.isCompiling)return "REFUSED scripts are compiling";
    string slice=null;int si=c.IndexOf(" --slice ",StringComparison.Ordinal);
    if(si>=0){slice=c.Substring(si+9).Trim();c=c.Substring(0,si).TrimEnd();}
    if(slice!=null&&!c.StartsWith("scene-revert:",StringComparison.Ordinal))throw new Refuse("--slice is read by scene-revert only");
    if(c=="assets-plan")return Assets("plan");
    if(c=="assets-apply")return Assets("apply");
    if(c=="assets-revert")return Assets("revert");
    string a;
    if((a=Arg(c,"plan:"))!=null)return Apply(a,true);
    if((a=Arg(c,"scene-apply:"))!=null)return Apply(a,false);
    if((a=Arg(c,"scene-revert:"))!=null)return Revert(a,slice);
    if((a=Arg(c,"check:"))!=null)return Check(a);
    if((a=Arg(c,"eyes:"))!=null)return Eyes(a);
   }
   catch(Refuse r){return "REFUSED "+r.Message;}
   catch(Exception e){return "FAILED "+e;}
   return "REFUSED unknown Town308 command '"+c+"' ("+Usage+")";
  }
  static string Arg(string c,string head)=>c.StartsWith(head,StringComparison.Ordinal)?c.Substring(head.Length).Trim():null;

  // ---------- data ----------

  sealed class Target{public string Alias,Scene;}
  sealed class Kind
  {
   public string Name,Class;public float W,D,Top,Skirt;public string[] Lods;public JToken[] Colliders,Doors;public bool Solid;public Vector3 LightLocal;public bool HasLight;
   public bool Building=>Class=="shop"||Class=="house"||Class=="shed";
  }
  sealed class Cfg
  {
   public JObject J;public string Sha;public Target[] Targets;
   public readonly Dictionary<string,Material> Mats=new Dictionary<string,Material>(StringComparer.Ordinal);
   public readonly Dictionary<string,Kind> Kinds=new Dictionary<string,Kind>(StringComparer.Ordinal);
   public Material Glow;
   public JToken Rules=>J["rules"];public float R(string k)=>Num(Rules,k);public string Root=>Str(J,"root");public string AssetDir=>Str(J,"asset_dir").TrimEnd('/');
   public string MeshDir=>Path.Combine(Harness303.RepoRoot,Str(J,"mesh_dir").Replace('/',Path.DirectorySeparatorChar));
   public string LedgerDir=>Path.Combine(Harness303.RepoRoot,Str(J,"ledger_dir").Replace('/',Path.DirectorySeparatorChar));
  }
  static JToken Req(JToken o,string key){var t=o?[key];if(t==null||t.Type==JTokenType.Null)throw new Refuse("town308.json: missing '"+key+"' in "+(o==null?"(null)":string.IsNullOrEmpty(o.Path)?"(root)":o.Path));return t;}
  static float Num(JToken o,string key)=>Req(o,key).Value<float>();
  static float OptNum(JToken o,string key,float fallback){var t=o?[key];return t==null||t.Type==JTokenType.Null?fallback:t.Value<float>();}
  static string Str(JToken o,string key)=>Req(o,key).Value<string>();
  static string Opt(JToken o,string key){var t=o?[key];return t==null||t.Type==JTokenType.Null?null:t.Value<string>();}
  static bool Flag(JToken o,string key,bool fallback=false){var t=o?[key];return t!=null&&t.Type==JTokenType.Boolean?t.Value<bool>():fallback;}
  static IEnumerable<JToken> Arr(JToken o,string key){var t=o?[key] as JArray;return t??(IEnumerable<JToken>)Array.Empty<JToken>();}
  static Vector3 Vec(JToken o,string key){var a=Req(o,key) as JArray;if(a==null||a.Count<3)throw new Refuse("town308.json: "+key+" needs 3 numbers");return new Vector3(a[0].Value<float>(),a[1].Value<float>(),a[2].Value<float>());}
  static string F(float v,string f="F2")=>v.ToString(f,CultureInfo.InvariantCulture);
  static string V(Vector3 v)=>Harness303.V(v);
  static string Short(string sha)=>string.IsNullOrEmpty(sha)?"-":sha.Substring(0,Math.Min(12,sha.Length)).ToLowerInvariant();
  static bool Protected(string p)=>Harness303.IsProtected(p)||PostLedger308.IsProtectedPath(p);

  static readonly string[] RuleKeys={"flat_tol_m","podium_relief_max_m","reveal_min_m","bury_min_m","door_reveal_max_m","riser_max_m","entry_step_limit_m","step_tread_m","step_gap_m","ground_sample_step_m","door_sample_step_m","stall_leg_margin_m","shoulder_min_m","stall_shoulder_min_m","lane_min_m","touch_max_m","snag_gap_lo_m","snag_gap_hi_m","lip_lo_m","lip_hi_m","collider_top_min_m","object_clear_m","y_tol_m","relief_tol_m","pose_tol_m","pose_tol_deg","mesh_bounds_tol_m"};

  static Cfg Load()
  {
   string f=DataFile;if(!File.Exists(f))throw new Refuse("data missing: "+f+" (python Tools/Unity/Stage308_town/town_copy.py --apply)");
   JObject j;try{j=JObject.Parse(File.ReadAllText(f));}catch(Exception e){throw new Refuse("data does not parse: "+f+" ("+e.Message+")");}
   var cfg=new Cfg{J=j,Sha=Harness303.Sha(f)};
   foreach(string key in new[]{"rules","slices","meshes","materials","kinds","root","asset_dir","mesh_dir","ledger_dir","light","read","roads","targets"})Req(j,key);
   foreach(string key in RuleKeys)Num(cfg.Rules,key);   // a missing rule refuses here, not half way through a pass
   if(Arr(cfg.Rules,"lod_screen").Count()<2||Arr(cfg.Rules,"lod_shadows").Count()<2)throw new Refuse("town308.json: rules.lod_screen / lod_shadows need two entries");
   if(cfg.R("riser_max_m")>cfg.R("entry_step_limit_m"))throw new Refuse("town308.json: riser_max_m is above entry_step_limit_m");
   cfg.Targets=Arr(j,"targets").Select(t=>new Target{Alias=Str(t,"alias"),Scene=Str(t,"scene")}).ToArray();
   if(cfg.Targets.Length==0)throw new Refuse("town308.json: targets[] is empty");
   foreach(var p in cfg.Targets.Select(t=>t.Scene).Concat(new[]{cfg.AssetDir,Str(j,"mesh_dir"),Str(j,"ledger_dir")}))if(Protected(p))throw new Refuse("protected path in data: "+p);
   if(PostLedger308.IsProtectedPath("/"+cfg.Root+"/"))throw new Refuse("root "+cfg.Root+" is a protected tree name");
   // existing materials only: the guid must resolve to the path the data names; nothing glows but the one slot the data marks (the lantern)
   foreach(var kv in (JObject)j["materials"])
   {
    string path=Str(kv.Value,"path"),guid=Str(kv.Value,"guid");
    if(AssetDatabase.GUIDToAssetPath(guid)!=path)throw new Refuse("material '"+kv.Key+"': guid "+guid+" resolves to '"+AssetDatabase.GUIDToAssetPath(guid)+"', the data says "+path);
    var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m==null)throw new Refuse("material '"+kv.Key+"' does not load: "+path);
    if(Flag(kv.Value,"glow")){if(cfg.Glow!=null)throw new Refuse("more than one glow material in the data");cfg.Glow=m;}
    else if(m.IsKeywordEnabled("_EMISSION"))throw new Refuse("material '"+kv.Key+"' ("+path+") has _EMISSION: only the lantern slot may glow");
    cfg.Mats[kv.Key]=m;
   }
   foreach(var m in Arr(j,"meshes"))foreach(var s in Arr(m,"slots"))if(!cfg.Mats.ContainsKey(s.Value<string>()))throw new Refuse("mesh "+Str(m,"name")+": slot '"+s.Value<string>()+"' has no material in materials{}");
   foreach(var kv in (JObject)j["kinds"])
   {
    var pl=Req(kv.Value,"plinth") as JArray;if(pl==null||pl.Count<2)throw new Refuse("kind "+kv.Key+": plinth needs 2 numbers");
    var k=new Kind{Name=kv.Key,Class=Str(kv.Value,"class"),W=pl[0].Value<float>(),D=pl[1].Value<float>(),Top=Num(kv.Value,"top_m"),Skirt=Num(kv.Value,"skirt"),
     Lods=Arr(kv.Value,"lods").Select(x=>x.Value<string>()).ToArray(),Colliders=Arr(kv.Value,"colliders").ToArray(),Doors=Arr(kv.Value,"door_points").ToArray(),Solid=Flag(kv.Value,"solid")};
    if(k.Lods.Length<1||k.Lods.Length>2)throw new Refuse("kind "+kv.Key+": 1 or 2 lods");
    foreach(string l in k.Lods)MeshRow(cfg,l);
    if(k.Solid!=(k.Colliders.Length>0))throw new Refuse("kind "+kv.Key+": solid and colliders[] disagree");
    if(kv.Value["light_local"]!=null){k.LightLocal=Vec(kv.Value,"light_local");k.HasLight=true;}
    cfg.Kinds[kv.Key]=k;
   }
   if(!cfg.Kinds.ContainsKey("Step"))throw new Refuse("town308.json: kinds{} has no 'Step' (the entry step stone)");
   var ld=Req(j,"light");if(Str(ld,"type")!="Point"||Str(ld,"shadows")!="None")throw new Refuse("town308.json: light must be a Point light with shadows None");
   Num(ld,"range_m");Num(ld,"intensity");Num(ld,"max_count");if(Arr(ld,"colour").Count()<3)throw new Refuse("town308.json: light.colour needs 3 numbers");
   foreach(string side in new[]{"west","east"}){Num(Req(Req(j,"roads"),side),"x");Num(Req(Req(j,"roads"),side),"half_m");}
   var ids=new HashSet<string>(StringComparer.Ordinal);int lights=0;
   foreach(var sl in Arr(j,"slices"))foreach(var r in Arr(sl,"rows"))
   {
    string id=Str(r,"id");if(!ids.Add(id))throw new Refuse("row id '"+id+"' is used twice");
    if(id.IndexOf('/')>=0||id.IndexOf('#')>=0)throw new Refuse("row id '"+id+"': no '/' or '#'");
    if(!cfg.Kinds.TryGetValue(Str(r,"kind"),out var k))throw new Refuse("row "+id+": kind '"+Str(r,"kind")+"' is not in kinds{}");
    Num(r,"x");Num(r,"z");Num(r,"yaw");if(k.HasLight&&Flag(sl,"enabled",true))lights++;
   }
   if(lights>Mathf.RoundToInt(Num(ld,"max_count")))throw new Refuse("town308.json: "+lights+" light rows, light.max_count is "+F(Num(ld,"max_count"),"F0"));
   return cfg;
  }
  static JToken MeshRow(Cfg cfg,string name)=>Arr(cfg.J,"meshes").FirstOrDefault(m=>Str(m,"name")==name)??throw new Refuse("mesh '"+name+"' is not in meshes[]");
  static Target TargetOf(Cfg cfg,string arg)
  {
   arg=(arg??"").Trim().Replace('\\','/');
   var t=cfg.Targets.FirstOrDefault(x=>string.Equals(x.Alias,arg,StringComparison.OrdinalIgnoreCase)||x.Scene==arg);
   if(t==null)throw new Refuse("not a town308 target: '"+arg+"' (allowed: "+string.Join(", ",cfg.Targets.Select(x=>x.Alias))+")");
   return t;
  }
  static Scene Open(Target t)
  {
   for(int i=0;i<SceneManager.sceneCount;i++){var s=SceneManager.GetSceneAt(i);if(s.isDirty)throw new Refuse("scene "+s.path+" has unsaved changes - save or discard them first");}
   var active=SceneManager.GetActiveScene();
   return active.path==t.Scene&&SceneManager.sceneCount==1?active:EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);
  }
  // a look preview (DontSave root) switches real renderers off while it is up: a save would write them off, a scene switch would orphan it
  static void PreviewGuard(Cfg cfg,Target t,bool write)
  {
   var names=new HashSet<string>(Arr(cfg.Rules,"preview_roots").Select(x=>x.Value<string>()));
   var up=Resources.FindObjectsOfTypeAll<GameObject>().Where(g=>g!=null&&!EditorUtility.IsPersistent(g)&&g.transform.parent==null&&names.Contains(g.name)).Select(g=>g.name).Distinct().ToList();
   if(up.Count==0)return;
   var active=SceneManager.GetActiveScene();bool switches=!(active.path==t.Scene&&SceneManager.sceneCount==1);
   if(write||switches)throw new Refuse("a look preview is up ("+string.Join(", ",up)+") - run that tool's preview:off first");
  }

  // ---------- ledgers ----------

  [Serializable] sealed class Row{public string kind="",key="",detail="";public bool reverted;}
  [Serializable] sealed class Write{public string utc="",target="",backup="",shaBefore="",shaAfter="",dataSha="";public List<Row> rows=new List<Row>();}
  [Serializable] sealed class Ledger{public string kind="town308",target="",created="";public List<Write> writes=new List<Write>();}
  static string LedgerFile(Cfg cfg,string key)=>Path.Combine(cfg.LedgerDir,"ledger_town308_"+key+".json");
  static Ledger ReadLedger(Cfg cfg,string key){var f=LedgerFile(cfg,key);return File.Exists(f)?JsonUtility.FromJson<Ledger>(File.ReadAllText(f)):null;}
  static void WriteLedger(Cfg cfg,string key,Ledger l){Directory.CreateDirectory(cfg.LedgerDir);File.WriteAllText(LedgerFile(cfg,key),JsonUtility.ToJson(l,true));}
  static IEnumerable<Row> Live(Ledger l)=>l==null?Enumerable.Empty<Row>():l.writes.Where(w=>w!=null&&w.rows!=null).SelectMany(w=>w.rows).Where(r=>r!=null&&!r.reverted);
  static string Backup(Cfg cfg,string assetPath)
  {
   string dir=Path.Combine(cfg.LedgerDir,"Backups","town308-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff",CultureInfo.InvariantCulture));Directory.CreateDirectory(dir);
   string copy=Path.Combine(dir,Path.GetFileName(assetPath));File.Copy(Harness303.Abs(assetPath),copy,true);return copy;
  }

  // ---------- Mesh assets (one per meshes[] row, shared by the three scenes) ----------

  [Serializable] sealed class KitSub{public string m;public int[] t;}
  [Serializable] sealed class KitMesh{public string name;public float[] v,n,uv;public KitSub[] sub;}
  static string MeshAsset(Cfg cfg,string name)=>cfg.AssetDir+"/"+name+".asset";
  static string MeshJson(Cfg cfg,JToken m)=>Path.Combine(cfg.MeshDir,Str(m,"json"));
  // null = the json is the one the data names; else why not
  static string JsonState(Cfg cfg,JToken m)
  {
   string f=MeshJson(cfg,m);if(!File.Exists(f))return "json missing: "+f;
   return string.Equals(Harness303.Sha(f),Str(m,"sha256"),StringComparison.OrdinalIgnoreCase)?null:"json sha "+Short(Harness303.Sha(f))+" is not the data's "+Short(Str(m,"sha256"));
  }
  static Mesh BuildMesh(Cfg cfg,JToken m)
  {
   var d=JsonUtility.FromJson<KitMesh>(File.ReadAllText(MeshJson(cfg,m)));int count=d.v.Length/3;
   if(count!=Mathf.RoundToInt(Num(m,"verts")))throw new Refuse("mesh "+Str(m,"name")+": "+count+" vertices in the json, the data row says "+F(Num(m,"verts"),"F0"));
   var v=new Vector3[count];var n=new Vector3[count];var uv=new Vector2[count];
   for(int i=0;i<count;i++){v[i]=new Vector3(d.v[3*i],d.v[3*i+1],d.v[3*i+2]);n[i]=new Vector3(d.n[3*i],d.n[3*i+1],d.n[3*i+2]);uv[i]=new Vector2(d.uv[2*i],d.uv[2*i+1]);}
   var slots=Arr(m,"slots").Select(s=>s.Value<string>()).ToArray();
   var mesh=new Mesh{name=Str(m,"name"),indexFormat=count>65000?IndexFormat.UInt32:IndexFormat.UInt16};
   mesh.vertices=v;mesh.normals=n;mesh.uv=uv;mesh.subMeshCount=slots.Length;int tris=0;
   for(int i=0;i<slots.Length;i++){var sub=Array.Find(d.sub,s=>s.m==slots[i]);if(sub==null)throw new Refuse("mesh "+Str(m,"name")+": the json has no submesh '"+slots[i]+"'");mesh.SetTriangles(sub.t,i,false);tris+=sub.t.Length/3;}
   if(tris!=Mathf.RoundToInt(Num(m,"tris")))throw new Refuse("mesh "+Str(m,"name")+": "+tris+" triangles in the json, the data row says "+F(Num(m,"tris"),"F0"));
   mesh.RecalculateBounds();mesh.RecalculateTangents();
   return mesh;
  }
  // does the asset on disk carry what the data row describes? (vertex count, submeshes, bounds within the tolerance)
  static bool MeshMatches(Cfg cfg,Mesh a,JToken m)
  {
   if(a==null||a.vertexCount!=Mathf.RoundToInt(Num(m,"verts"))||a.subMeshCount!=Arr(m,"slots").Count())return false;
   Vector3 lo=Vec(m,"lo"),hi=Vec(m,"hi");float tol=cfg.R("mesh_bounds_tol_m");return (a.bounds.min-lo).magnitude<=tol&&(a.bounds.max-hi).magnitude<=tol;
  }
  static bool AnySceneLive(Cfg cfg,out string who)
  {
   who=string.Join(", ",cfg.Targets.Where(t=>Live(ReadLedger(cfg,"scene_"+t.Alias)).Any()).Select(t=>t.Alias));return who.Length>0;
  }
  static string Assets(string mode)
  {
   var cfg=Load();var sb=new StringBuilder("Town308 assets-"+mode+" (data "+Short(cfg.Sha)+" "+(Opt(cfg.J,"version")??"?")+")\n");
   var ledger=ReadLedger(cfg,"assets")??new Ledger{target=cfg.AssetDir,created=DateTime.UtcNow.ToString("O")};
   if(mode=="revert")
   {
    if(AnySceneLive(cfg,out string who))throw new Refuse("scene ledger rows are still live ("+who+"): scene-revert those scenes first - their objects use these meshes");
    var live=Live(ledger).ToList();if(live.Count==0)return sb.Append("  변경 없음 (no live asset row)").ToString();
    foreach(var r in Enumerable.Reverse(live))
    {
     if(r.kind=="mesh"){if(AssetDatabase.LoadAssetAtPath<Mesh>(r.key)==null)sb.AppendLine("  "+r.key+": already gone");else if(AssetDatabase.DeleteAsset(r.key))sb.AppendLine("  deleted "+r.key);else{sb.AppendLine("  KEPT "+r.key+": DeleteAsset returned false");continue;}}
     else if(r.kind=="folder"){if(!AssetDatabase.IsValidFolder(r.key))sb.AppendLine("  "+r.key+": already gone");else if(Directory.EnumerateFileSystemEntries(Harness303.Abs(r.key)).Any(e=>!e.EndsWith(".meta",StringComparison.Ordinal))){sb.AppendLine("  KEPT folder "+r.key+": it still holds assets");continue;}else if(AssetDatabase.DeleteAsset(r.key))sb.AppendLine("  deleted folder "+r.key);else{sb.AppendLine("  KEPT folder "+r.key);continue;}}
     r.reverted=true;
    }
    WriteLedger(cfg,"assets",ledger);return sb.Append("  ledger "+LedgerFile(cfg,"assets")).ToString();
   }
   var todo=new List<JToken>();long tris=0;
   foreach(var m in Arr(cfg.J,"meshes"))
   {
    string name=Str(m,"name"),path=MeshAsset(cfg,name),bad=JsonState(cfg,m);
    if(bad!=null)throw new Refuse("mesh "+name+": "+bad+" (town_copy.py copies the json files; nothing written)");
    var have=AssetDatabase.LoadAssetAtPath<Mesh>(path);tris+=Mathf.RoundToInt(Num(m,"tris"));
    if(have==null){sb.AppendLine("  create "+path+" ("+F(Num(m,"verts"),"F0")+" vertices, "+F(Num(m,"tris"),"F0")+" triangles, slots "+string.Join(" / ",Arr(m,"slots").Select(s=>s.Value<string>()))+")");todo.Add(m);}
    else if(MeshMatches(cfg,have,m))sb.AppendLine("  same   "+path);
    else throw new Refuse(path+" exists and is not the mesh the data describes - assets-revert first (after scene-revert). Nothing written");
   }
   if(todo.Count==0)return sb.Append("  변경 없음 (every mesh asset is in place: "+Arr(cfg.J,"meshes").Count()+", "+tris+" triangles in all)").ToString();
   if(mode=="plan")return sb.Append("  plan only: nothing written (would create "+todo.Count+" mesh asset(s))").ToString();
   var write=new Write{utc=DateTime.UtcNow.ToString("O"),target=cfg.AssetDir,dataSha=cfg.Sha};
   // folders first (each one that is made is journalled so the revert can take it out again)
   string acc="";foreach(string part in cfg.AssetDir.Split('/'))
   {
    string parent=acc;acc=acc.Length==0?part:acc+"/"+part;if(AssetDatabase.IsValidFolder(acc))continue;
    write.rows.Add(new Row{kind="folder",key=acc,detail="folder"});if(!ledger.writes.Contains(write))ledger.writes.Add(write);WriteLedger(cfg,"assets",ledger);
    if(string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent,part)))throw new Refuse("could not create folder "+acc);
   }
   foreach(var m in todo)
   {
    string path=MeshAsset(cfg,Str(m,"name"));var mesh=BuildMesh(cfg,m);
    write.rows.Add(new Row{kind="mesh",key=path,detail=Str(m,"name")+" json "+Short(Str(m,"sha256"))});if(!ledger.writes.Contains(write))ledger.writes.Add(write);WriteLedger(cfg,"assets",ledger);   // journal before the file
    AssetDatabase.CreateAsset(mesh,path);AssetDatabase.SaveAssetIfDirty(mesh);
    sb.AppendLine("  created "+path+" (guid "+AssetDatabase.AssetPathToGUID(path)+")");
   }
   return sb.Append("  created "+todo.Count+" mesh asset(s); ledger "+LedgerFile(cfg,"assets")).ToString();
  }

  // ---------- ground ----------

  static bool Under(Transform t,HashSet<string> roots){for(var p=t;p!=null;p=p.parent)if(p.parent==null&&roots.Contains(p.name))return true;return false;}
  static bool Preview(Transform t){for(var p=t;p!=null;p=p.parent)if((p.gameObject.hideFlags&HideFlags.DontSave)!=0)return true;return false;}
  static bool TerrainLike(Collider c){if(c is TerrainCollider)return true;for(var t=c.transform;t!=null;t=t.parent){var n=t.name;if(n.Contains("Terrain")||n.Contains("Surface"))return true;}return false;}
  // the Content308 rule in short (as Places308): the highest terrain surface under the XZ; own / listed roots and previews never count
  static bool Ground(Cfg cfg,float x,float z,out Vector3 p)
  {
   p=default;var roots=new HashSet<string>(Arr(cfg.Rules,"ground_ignore_roots").Select(v=>v.Value<string>()));bool any=false;
   foreach(var h in Physics.RaycastAll(new Vector3(x,2000f,z),Vector3.down,4000f,~0,QueryTriggerInteraction.Ignore))
   {
    var c=h.collider;if(c==null||h.normal.y<=.5f||c is CharacterController||Preview(c.transform)||Under(c.transform,roots)||!TerrainLike(c))continue;
    if(!any||h.point.y>p.y){p=h.point;any=true;}
   }
   return any;
  }
  // the same lattice as town_lib.rect_samples: the corners and the edges of the local rectangle are always in it
  static bool Span(Cfg cfg,Vector3 pos,Quaternion rot,float w,float d,float cx,float cz,float step,out float lo,out float hi)
  {
   lo=float.PositiveInfinity;hi=float.NegativeInfinity;int nx=Mathf.Max(2,Mathf.CeilToInt(w/step-1e-4f)+1),nz=Mathf.Max(2,Mathf.CeilToInt(d/step-1e-4f)+1);
   for(int i=0;i<nx;i++)for(int k=0;k<nz;k++)
   {
    var l=new Vector3(cx-w/2f+w*i/(nx-1),0f,cz-d/2f+d*k/(nz-1));var q=pos+rot*l;
    if(!Ground(cfg,q.x,q.z,out var g))return false;
    lo=Mathf.Min(lo,g.y);hi=Mathf.Max(hi,g.y);
   }
   return true;
  }

  // ---------- plan ----------

  sealed class StepPose{public string Name;public Vector3 Local;}
  sealed class Item
  {
   public string Slice,Id;public JToken Row;public Kind K;public Mesh[] Meshes;public Material[][] Mats;public Vector3 Pos;public Quaternion Rot;
   public float GMin,GMax,Relief,SillMax,RiserMax;public string Seat="";public List<StepPose> Steps=new List<StepPose>();public List<string> Notes=new List<string>();public int Warns;
   public string Path(Cfg cfg)=>cfg.Root+"/"+Slice+"/"+Id;
  }
  static Item Build(Cfg cfg,JToken slice,JToken r)
  {
   var it=new Item{Slice=Str(slice,"id"),Id=Str(r,"id"),Row=r,K=cfg.Kinds[Str(r,"kind")]};var k=it.K;float x=Num(r,"x"),z=Num(r,"z");
   it.Rot=Quaternion.Euler(0,Num(r,"yaw"),0);var flat=new Vector3(x,0f,z);
   if(!Span(cfg,flat,it.Rot,k.W,k.D,0f,0f,cfg.R("ground_sample_step_m"),out it.GMin,out it.GMax))throw new Refuse("no terrain under "+it.Id+" ("+F(x,"F1")+", "+F(z,"F1")+") - is the scene loaded?");
   it.Relief=it.GMax-it.GMin;float y;
   if(k.Building)
   {
    y=it.GMax+cfg.R("reveal_min_m");it.Seat=it.Relief<=cfg.R("flat_tol_m")?"flat":"podium";
    if(it.Relief>cfg.R("podium_relief_max_m"))throw new Refuse(it.Id+": physical ground relief "+F(it.Relief)+" m under its plinth > podium_relief_max_m "+F(cfg.R("podium_relief_max_m"))+" - this site is not built on (revise the data). Nothing written");
    if(k.Skirt-(y-it.GMin)<cfg.R("bury_min_m"))throw new Refuse(it.Id+": its stone skirt ("+F(k.Skirt)+" m) does not reach "+F(cfg.R("bury_min_m"))+" m below the lowest ground. Nothing written");
    float riserMax=cfg.R("riser_max_m"),tread=cfg.R("step_tread_m"),gap=cfg.R("step_gap_m");var step=cfg.Kinds["Step"];
    int nmax=Mathf.CeilToInt(cfg.R("door_reveal_max_m")/riserMax-1e-4f)-1;float depth=Mathf.Max(1,nmax)*tread+gap;
    foreach(var dp in k.Doors)
    {
     float dx=Num(dp,"x"),dz=Num(dp,"z"),o=Num(dp,"out");
     if(!Span(cfg,flat,it.Rot,step.W,depth,dx,dz+o*depth/2f,cfg.R("door_sample_step_m"),out float g0,out _))throw new Refuse("no terrain in front of the "+Str(dp,"side")+" door of "+it.Id);
     float rev=y-g0;it.SillMax=Mathf.Max(it.SillMax,rev);
     if(rev>cfg.R("door_reveal_max_m"))throw new Refuse(it.Id+": its "+Str(dp,"side")+" door sill is "+F(rev)+" m above the physical ground in front > door_reveal_max_m "+F(cfg.R("door_reveal_max_m"))+". Nothing written");
     int n=Mathf.Max(1,Mathf.CeilToInt(rev/riserMax-1e-4f));float riser=rev/n;it.RiserMax=Mathf.Max(it.RiserMax,riser);
     for(int jn=1;jn<n;jn++)it.Steps.Add(new StepPose{Name="Step_"+Str(dp,"side")+"_"+jn,Local=new Vector3(dx,-jn*riser,dz+o*(gap+(jn-.5f)*tread))});
    }
   }
   else
   {
    y=it.GMax;it.Seat="ground";
    if(it.Relief>k.Skirt-cfg.R("stall_leg_margin_m"))throw new Refuse(it.Id+": ground relief "+F(it.Relief)+" m under a "+k.Name+" whose legs reach "+F(k.Skirt)+" m. Nothing written");
   }
   it.Pos=new Vector3(x,y,z);
   it.Meshes=new Mesh[k.Lods.Length];it.Mats=new Material[k.Lods.Length][];
   for(int i=0;i<k.Lods.Length;i++)
   {
    var md=MeshRow(cfg,k.Lods[i]);it.Meshes[i]=AssetDatabase.LoadAssetAtPath<Mesh>(MeshAsset(cfg,k.Lods[i]));
    if(it.Meshes[i]==null||!MeshMatches(cfg,it.Meshes[i],md))throw new Refuse(it.Id+": mesh asset "+MeshAsset(cfg,k.Lods[i])+(it.Meshes[i]==null?" is missing":" is not the data's mesh")+" - run assets-apply first. Nothing written");
    it.Mats[i]=Arr(md,"slots").Select(v=>cfg.Mats[v.Value<string>()]).ToArray();
   }
   if(it.Steps.Count>0){var sm=AssetDatabase.LoadAssetAtPath<Mesh>(MeshAsset(cfg,"Step"));if(sm==null||!MeshMatches(cfg,sm,MeshRow(cfg,"Step")))throw new Refuse(it.Id+": the Step mesh asset is missing - run assets-apply first. Nothing written");}
   float dy=y-OptNum(r,"y_offline",y),dr=it.Relief-OptNum(r,"relief_offline",it.Relief);int stepsOff=Arr(r,"steps_offline").Count();
   bool warn=Mathf.Abs(dy)>cfg.R("y_tol_m")||Mathf.Abs(dr)>cfg.R("relief_tol_m")||stepsOff!=it.Steps.Count;if(warn)it.Warns++;
   it.Notes.Add((warn?"WARN ":"")+it.Id+" ("+k.Name+(k.Solid?", solid":"")+") at "+V(it.Pos)+" yaw "+F(Num(r,"yaw"),"F0")+": ground "+F(it.GMin)+".."+F(it.GMax)+", relief "+F(it.Relief)+" m = "+it.Seat
    +(k.Building?", sill "+F(it.SillMax)+" m, step stones "+it.Steps.Count+" (riser "+F(it.RiserMax)+")":"")+" [offline y "+F(OptNum(r,"y_offline",y))+" Δ "+F(dy)+", relief Δ "+F(dr)+", step stones "+stepsOff+" - the editor value is used]");
   return it;
  }
  static IEnumerable<JToken> EnabledSlices(Cfg cfg)=>Arr(cfg.J,"slices").Where(p=>Flag(p,"enabled",true));
  static Transform Root(Cfg cfg,Scene s)=>s.GetRootGameObjects().Where(g=>g.name==cfg.Root&&!Preview(g.transform)).Select(g=>g.transform).FirstOrDefault();
  static bool SameMesh(Transform t,Mesh mesh,Material[] mats)
  {
   if(t==null)return false;var mf=t.GetComponent<MeshFilter>();var mr=t.GetComponent<MeshRenderer>();
   return mf!=null&&mr!=null&&mf.sharedMesh==mesh&&mr.sharedMaterials.SequenceEqual(mats);
  }
  static bool Same(Cfg cfg,Transform have,Item it)
  {
   if((have.position-it.Pos).magnitude>cfg.R("pose_tol_m")||Quaternion.Angle(have.rotation,it.Rot)>cfg.R("pose_tol_deg")||(have.localScale-Vector3.one).magnitude>1e-4f)return false;
   if(it.K.Lods.Length==1){if(!SameMesh(have,it.Meshes[0],it.Mats[0])||have.GetComponent<LODGroup>()!=null)return false;}
   else{for(int i=0;i<it.K.Lods.Length;i++)if(!SameMesh(have.Find("L"+i),it.Meshes[i],it.Mats[i]))return false;if(have.GetComponent<LODGroup>()==null)return false;}
   if(have.GetComponentsInChildren<Collider>(true).Length!=it.K.Colliders.Length)return false;
   if(have.GetComponentsInChildren<Light>(true).Length!=(it.K.HasLight?1:0))return false;
   var steps=have.Cast<Transform>().Where(c=>c.name.StartsWith("Step_",StringComparison.Ordinal)).ToList();
   if(steps.Count!=it.Steps.Count)return false;
   foreach(var s in it.Steps){var c=have.Find(s.Name);if(c==null||(c.localPosition-s.Local).magnitude>cfg.R("pose_tol_m"))return false;}
   return true;
  }
  static GameObject Part(Transform parent,string name,Mesh mesh,Material[] mats,bool shadows)
  {
   var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
   var mr=go.AddComponent<MeshRenderer>();mr.sharedMaterials=mats;mr.shadowCastingMode=shadows?ShadowCastingMode.On:ShadowCastingMode.Off;
   GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);return go;
  }
  static GameObject Make(Cfg cfg,Transform holder,Item it)
  {
   var k=it.K;var shadow=Arr(cfg.Rules,"lod_shadows").Select(v=>v.Value<bool>()).ToArray();var cut=Arr(cfg.Rules,"lod_screen").Select(v=>v.Value<float>()).ToArray();
   var obj=new GameObject(it.Id);obj.transform.SetParent(holder,false);obj.transform.SetPositionAndRotation(it.Pos,it.Rot);obj.transform.localScale=Vector3.one;
   if(k.Lods.Length==1)
   {
    obj.AddComponent<MeshFilter>().sharedMesh=it.Meshes[0];var mr=obj.AddComponent<MeshRenderer>();mr.sharedMaterials=it.Mats[0];mr.shadowCastingMode=shadow[0]?ShadowCastingMode.On:ShadowCastingMode.Off;
    GameObjectUtility.SetStaticEditorFlags(obj,StaticEditorFlags.BatchingStatic);
   }
   else
   {
    var lods=new LOD[k.Lods.Length];
    for(int i=0;i<k.Lods.Length;i++){var part=Part(obj.transform,"L"+i,it.Meshes[i],it.Mats[i],shadow[Mathf.Min(i,shadow.Length-1)]);lods[i]=new LOD(cut[Mathf.Min(i,cut.Length-1)],new Renderer[]{part.GetComponent<MeshRenderer>()});}
    var group=obj.AddComponent<LODGroup>();group.SetLODs(lods);group.RecalculateBounds();
   }
   for(int i=0;i<k.Colliders.Length;i++)
   {
    var cg=new GameObject("Col_"+i);cg.transform.SetParent(obj.transform,false);cg.transform.localPosition=Vec(k.Colliders[i],"center");cg.transform.localRotation=Quaternion.Euler(0,OptNum(k.Colliders[i],"yaw",0f),0);
    cg.AddComponent<BoxCollider>().size=Vec(k.Colliders[i],"size");
   }
   if(it.Steps.Count>0)
   {
    var sm=AssetDatabase.LoadAssetAtPath<Mesh>(MeshAsset(cfg,"Step"));var mats=Arr(MeshRow(cfg,"Step"),"slots").Select(v=>cfg.Mats[v.Value<string>()]).ToArray();
    foreach(var s in it.Steps){var sg=Part(obj.transform,s.Name,sm,mats,false);sg.transform.localPosition=s.Local;}
   }
   if(k.HasLight)
   {
    var ld=cfg.J["light"];var lg=new GameObject("Light");lg.transform.SetParent(obj.transform,false);lg.transform.localPosition=k.LightLocal;
    var light=lg.AddComponent<Light>();light.type=LightType.Point;light.range=Num(ld,"range_m");light.intensity=Num(ld,"intensity");light.shadows=LightShadows.None;
    var col=Arr(ld,"colour").Select(v=>v.Value<float>()).ToArray();light.color=new Color(col[0],col[1],col[2]);
   }
   return obj;
  }

  // ---------- apply / revert ----------

  static string Apply(string arg,bool dry)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,!dry);var scene=Open(t);Physics.SyncTransforms();
   var sb=new StringBuilder("Town308 "+(dry?"plan":"scene-apply")+" "+t.Alias+" (data "+Short(cfg.Sha)+" "+(Opt(cfg.J,"version")??"?")+")\n");
   var root=Root(cfg,scene);if(root!=null&&PostLedger308.UnderProtectedTree(root))throw new Refuse("root "+cfg.Root+" is a protected tree name");
   var todo=new List<Item>();int warns=0,solid=0,steps=0,flat=0,podium=0;float worst=0f;
   foreach(var slice in EnabledSlices(cfg))foreach(var r in Arr(slice,"rows"))
   {
    var it=Build(cfg,slice,r);warns+=it.Warns;foreach(var n in it.Notes)sb.AppendLine("  "+n);
    if(it.K.Building){worst=Mathf.Max(worst,it.Relief);if(it.Seat=="flat")flat++;else podium++;}
    var have=root!=null?root.Find(it.Slice+"/"+it.Id):null;
    if(have!=null){if(Same(cfg,have,it)){sb.AppendLine("    in the scene, same pose");continue;}throw new Refuse(it.Path(cfg)+" is in "+scene.path+" with another pose / mesh / colliders / step stones than the data and the ground give - scene-revert:"+t.Alias+" first. Nothing written");}
    todo.Add(it);if(it.K.Solid)solid++;steps+=it.Steps.Count;
   }
   foreach(var slice in Arr(cfg.J,"slices").Where(p=>!Flag(p,"enabled",true)))if(root!=null&&root.Find(Str(slice,"id"))!=null)throw new Refuse("slice "+Str(slice,"id")+" is off in the data but in the scene - scene-revert:"+t.Alias+" --slice "+Str(slice,"id")+" first");
   string ground="buildings on a level plinth: flat "+flat+", podium "+podium+", physical relief max "+F(worst)+" m (limit "+F(cfg.R("podium_relief_max_m"))+")";
   if(todo.Count==0)return sb.Append("  변경 없음 (no change; "+ground+"; WARN "+warns+")").ToString();
   string what=todo.Count+" object(s) + "+steps+" step stone(s), "+solid+" BoxCollider(s); "+ground;
   if(dry)return sb.Append("  plan only: nothing written (would create "+what+"; WARN "+warns+")").ToString();
   string abs=Harness303.Abs(t.Scene);var write=new Write{utc=DateTime.UtcNow.ToString("O"),target=t.Scene,backup=Backup(cfg,t.Scene),shaBefore=Harness303.Sha(abs),dataSha=cfg.Sha};
   var ledger=ReadLedger(cfg,"scene_"+t.Alias)??new Ledger{target=t.Scene,created=DateTime.UtcNow.ToString("O")};
   try
   {
    if(root==null){var go=new GameObject(cfg.Root);SceneManager.MoveGameObjectToScene(go,scene);root=go.transform;write.rows.Add(new Row{kind="create-root",key=cfg.Root,detail="root"});}
    foreach(var it in todo)
    {
     var holder=root.Find(it.Slice);
     if(holder==null){holder=new GameObject(it.Slice).transform;holder.SetParent(root,false);write.rows.Add(new Row{kind="create-slice",key=BuildingAudit308.KeyOf(holder),detail="slice "+it.Slice});}
     var obj=Make(cfg,holder,it);
     write.rows.Add(new Row{kind="create",key=BuildingAudit308.KeyOf(obj.transform),detail=it.Id+" "+it.K.Name+" at "+V(it.Pos)+(it.K.Solid?" solid":"")+" steps "+it.Steps.Count});
    }
    ledger.writes.Add(write);WriteLedger(cfg,"scene_"+t.Alias,ledger);   // the ledger is on disk before the scene is saved
   }
   catch(Exception e)
   {
    EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);   // a half-built plan must not stay in memory (nothing was saved)
    return "FAILED scene-apply "+t.Scene+" (scene reloaded from disk, nothing saved; backup "+write.backup+")\n"+e;
   }
   EditorSceneManager.MarkSceneDirty(scene);
   if(!EditorSceneManager.SaveScene(scene))
   {
    ledger.writes.Remove(write);WriteLedger(cfg,"scene_"+t.Alias,ledger);EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);
    return "FAILED SaveScene returned false for "+t.Scene+" (scene reloaded from disk, nothing saved, the ledger row was taken out again; backup "+write.backup+")";
   }
   write.shaAfter=Harness303.Sha(abs);WriteLedger(cfg,"scene_"+t.Alias,ledger);
   return sb.Append("  saved: "+what+"; WARN "+warns+"; scene sha "+Short(write.shaBefore)+" -> "+Short(write.shaAfter)+"; backup "+write.backup+"; ledger "+LedgerFile(cfg,"scene_"+t.Alias)).ToString();
  }

  static string Revert(string arg,string slice)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,true);var ledger=ReadLedger(cfg,"scene_"+t.Alias);
   string prefix=slice!=null?cfg.Root+"/"+slice:null;
   var live=Live(ledger).Where(r=>prefix==null||r.key==prefix||r.key.StartsWith(prefix+"/",StringComparison.Ordinal)).ToList();
   if(live.Count==0)return "Town308 scene-revert "+t.Alias+": 변경 없음 (no live ledger row"+(slice!=null?" of "+slice:"")+")";
   var scene=Open(t);var sb=new StringBuilder("Town308 scene-revert "+t.Alias+(slice!=null?" --slice "+slice:"")+"\n");string copy=Backup(cfg,t.Scene);int kept=0,removed=0;var marked=new List<Row>();
   // objects first, then their slice holders, the root last: a holder / the root goes only when nothing is left inside
   foreach(var r in live.Where(x=>x.kind=="create").Concat(live.Where(x=>x.kind=="create-slice")).Concat(Live(ledger).Where(x=>x.kind=="create-root")))
   {
    var tr=BuildingAudit308.Resolve(scene,r.key);
    if(tr==null){r.reverted=true;marked.Add(r);sb.AppendLine("  "+r.key+": already gone");continue;}
    // a name path takes the FIRST sibling of that name: destroy only the object whose own key is still the row's key
    if(BuildingAudit308.KeyOf(tr)!=r.key||tr.root.name!=cfg.Root){kept++;sb.AppendLine("  KEPT "+r.key+": the key no longer names this tool's object");continue;}
    if(r.kind!="create"&&tr.childCount>0){if(r.kind=="create-slice"||prefix==null){kept++;sb.AppendLine("  KEPT "+r.key+": "+tr.childCount+" child object(s) are still inside");}continue;}
    Object.DestroyImmediate(tr.gameObject);r.reverted=true;marked.Add(r);removed++;
   }
   WriteLedger(cfg,"scene_"+t.Alias,ledger);EditorSceneManager.MarkSceneDirty(scene);
   if(!EditorSceneManager.SaveScene(scene))
   {
    foreach(var r in marked)r.reverted=false;WriteLedger(cfg,"scene_"+t.Alias,ledger);EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);
    return "FAILED SaveScene returned false for "+t.Scene+" (scene reloaded from disk, nothing saved; the "+marked.Count+" ledger row(s) of this call are live again - run scene-revert again; backup "+copy+")";
   }
   return sb.Append("  saved; removed "+removed+" object(s), kept "+kept+"; scene sha "+Short(Harness303.Sha(Harness303.Abs(t.Scene)))+"; backup "+copy).ToString();
  }
 }
}
