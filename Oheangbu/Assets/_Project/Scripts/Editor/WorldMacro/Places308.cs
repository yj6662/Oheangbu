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
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 relayout fix 4 - three places that did not read as their texts say (run-2 verification; SPEC-CONTENT-PACING-308
 // "#308 수리 4차 - 세 자리", group G15_places_fix4) [TEST values throughout]:
 //   A_yard    the gold-lesson place 적재장: log stacks on bearers, a skid way, a tripod with a slung log, a chopping block, stumps
 //   B_rope    the fresh straw rope beside the Sinmok approach path where the rope elder works
 // The two people (the rope elder, the potter) and everything they carry are moved by Texts308 (texts308.json 308.texts19.3:
 // places / props); this tool only dresses. rev 2 (review): a row may carry its own corner_gap_max_m (a rope gate across a 20 deg
 // slope), eyes prints the note of a still on its own line, the path gap and the mesh tolerance come from the data.
 // A dressing object = one generated Mesh asset (the #306 mine-yard JSON shape, built offline by the stage's build_meshes.py)
 // with EXISTING materials, or one instance of an existing prefab with its colliders stripped. No light, no emission, no text,
 // no content row. Every pose, limit, path and GUID comes from Art/World/Compact/Rebuild/CliffBoundary308/places308.json.
 // Queue-safe: Run(string) never opens a dialog; refusals come back as "REFUSED ...". Ledgers (Art/Playtest308/Pacing/
 // ledger_places308_assets.json, ledger_places308_scene_<alias>.json) are written BEFORE the asset / scene is saved, with a
 // byte backup of the scene; a second identical apply reports "변경 없음"; a revert takes out exactly what the ledger made.
 // Only the target scene / the created Mesh assets are saved (SaveScene, CreateAsset; never AssetDatabase.SaveAssets).
 //   status
 //   assets-plan | assets-apply | assets-revert     the Mesh assets under asset_dir (once; shared by the three scenes)
 //   plan:<scene>                                   what scene-apply would create, with the physical ground of every row
 //   scene-apply:<scene> | scene-revert:<scene> [--place A_yard|B_rope]
 //   check:<scene>                                  AC-P1..P8 on the saved scene; read-only
 //   eyes:<scene>[:<label>]                         the Presentation297 shot lines of stills[] on the PHYSICAL ground (names
 //                                                  fix4_<label>_<id>: label = before | after); read-only
 // <scene> = arch296 | folk298 | main. Order: assets-apply, then scene-apply arch296 -> folk298 -> main, check x3.
 // Rows with solid true carry BoxColliders: they must be in all three scenes BEFORE the NavMesh bake (RUN_ORDER_fix4.md).
 public static class Places308
 {
  const string Usage="status | assets-plan | assets-apply | assets-revert | plan:<scene> | scene-apply:<scene> | scene-revert:<scene> [--place <id>] | check:<scene> | eyes:<scene>[:<label>]";
  static string DataFile=>Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","CliffBoundary308","places308.json");
  static string OutDir=>Path.Combine(Harness303.RepoRoot,"Art","Playtest308","Pacing");
  sealed class Refuse:Exception{public Refuse(string m):base(m){}}

  public static string Run(string command)
  {
   string c=(command??"").Trim();
   try
   {
    if(c=="status")return Status();
    if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Edit mode only (Play is running)";
    if(EditorApplication.isCompiling)return "REFUSED scripts are compiling";
    string place=null;int pi=c.IndexOf(" --place ",StringComparison.Ordinal);
    if(pi>=0){place=c.Substring(pi+9).Trim();c=c.Substring(0,pi).TrimEnd();}
    if(place!=null&&!c.StartsWith("scene-revert:",StringComparison.Ordinal))throw new Refuse("--place is read by scene-revert only");
    if(c=="assets-plan")return Assets("plan");
    if(c=="assets-apply")return Assets("apply");
    if(c=="assets-revert")return Assets("revert");
    string a;
    if((a=Arg(c,"plan:"))!=null)return Apply(a,true);
    if((a=Arg(c,"scene-apply:"))!=null)return Apply(a,false);
    if((a=Arg(c,"scene-revert:"))!=null)return Revert(a,place);
    if((a=Arg(c,"check:"))!=null)return Check(a);
    if((a=Arg(c,"eyes:"))!=null)return Eyes(a);
   }
   catch(Refuse r){return "REFUSED "+r.Message;}
   catch(Exception e){return "FAILED "+e;}
   return "REFUSED unknown Places308 command '"+c+"' ("+Usage+")";
  }
  static string Arg(string c,string head)=>c.StartsWith(head,StringComparison.Ordinal)?c.Substring(head.Length).Trim():null;

  // ---------- data ----------

  sealed class Target{public string Alias,Scene,Content;}
  sealed class Cfg
  {
   public JObject J;public string Sha;public Target[] Targets;public readonly Dictionary<string,Material> Mats=new Dictionary<string,Material>(StringComparer.Ordinal);
   public JToken Rules=>J["rules"];public float R(string k)=>Num(Rules,k);public string Root=>Str(J,"root");public string AssetDir=>Str(J,"asset_dir").TrimEnd('/');
   public string MeshDir=>Path.Combine(Harness303.RepoRoot,Str(J,"mesh_dir").Replace('/',Path.DirectorySeparatorChar));
  }
  static JToken Req(JToken o,string key){var t=o?[key];if(t==null||t.Type==JTokenType.Null)throw new Refuse("places308.json: missing '"+key+"' in "+(o==null?"(null)":string.IsNullOrEmpty(o.Path)?"(root)":o.Path));return t;}
  static float Num(JToken o,string key)=>Req(o,key).Value<float>();
  static float OptNum(JToken o,string key,float fallback){var t=o?[key];return t==null||t.Type==JTokenType.Null?fallback:t.Value<float>();}
  static string Str(JToken o,string key)=>Req(o,key).Value<string>();
  static string Opt(JToken o,string key){var t=o?[key];return t==null||t.Type==JTokenType.Null?null:t.Value<string>();}
  static bool Flag(JToken o,string key,bool fallback=false){var t=o?[key];return t!=null&&t.Type==JTokenType.Boolean?t.Value<bool>():fallback;}
  static IEnumerable<JToken> Arr(JToken o,string key){var t=o?[key] as JArray;return t??(IEnumerable<JToken>)Array.Empty<JToken>();}
  static Vector3 Vec(JToken o,string key){var a=Req(o,key) as JArray;if(a==null||a.Count<3)throw new Refuse("places308.json: "+key+" needs 3 numbers");return new Vector3(a[0].Value<float>(),a[1].Value<float>(),a[2].Value<float>());}
  static string F(float v,string f="F2")=>v.ToString(f,CultureInfo.InvariantCulture);
  static string V(Vector3 v)=>Harness303.V(v);
  static string Short(string sha)=>string.IsNullOrEmpty(sha)?"-":sha.Substring(0,Math.Min(12,sha.Length)).ToLowerInvariant();

  static Cfg Load()
  {
   string f=DataFile;if(!File.Exists(f))throw new Refuse("data missing: "+f+" (python Tools/Unity/Stage308_relayout_fix4/fix4_copy.py --apply)");
   JObject j;try{j=JObject.Parse(File.ReadAllText(f));}catch(Exception e){throw new Refuse("data does not parse: "+f+" ("+e.Message+")");}
   var cfg=new Cfg{J=j,Sha=Harness303.Sha(f)};
   Req(j,"rules");Req(j,"places");Req(j,"meshes");Req(j,"materials");Req(j,"root");Req(j,"asset_dir");Req(j,"mesh_dir");
   foreach(string key in new[]{"pose_tol_m","pose_tol_deg","scale_tol","y_tol_m","corner_gap_max_m","corridor_clear_m","point_collider_pad_m","prefab_scale_max","mesh_bounds_tol_m","path_run_gap_m"})Num(cfg.Rules,key);   // a missing rule refuses here, not half way through a pass
   cfg.Targets=Arr(j,"targets").Select(t=>new Target{Alias=Str(t,"alias"),Scene=Str(t,"scene"),Content=Str(t,"content")}).ToArray();
   if(cfg.Targets.Length==0)throw new Refuse("places308.json: targets[] is empty");
   foreach(var p in cfg.Targets.SelectMany(t=>new[]{t.Scene,t.Content}).Concat(new[]{cfg.AssetDir}))if(Harness303.IsProtected(p)||PostLedger308.IsProtectedPath(p))throw new Refuse("protected path in data: "+p);
   // existing materials only: the guid must resolve to the path the data names, and nothing may glow ("오염은 빛나지 않는다")
   foreach(var kv in (JObject)j["materials"])
   {
    string path=Str(kv.Value,"path"),guid=Str(kv.Value,"guid");
    if(AssetDatabase.GUIDToAssetPath(guid)!=path)throw new Refuse("material '"+kv.Key+"': guid "+guid+" resolves to '"+AssetDatabase.GUIDToAssetPath(guid)+"', the data says "+path);
    var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m==null)throw new Refuse("material '"+kv.Key+"' does not load: "+path);
    if(m.IsKeywordEnabled("_EMISSION"))throw new Refuse("material '"+kv.Key+"' ("+path+") has _EMISSION: this stage adds no glow");
    cfg.Mats[kv.Key]=m;
   }
   foreach(var m in Arr(j,"meshes"))foreach(var s in Arr(m,"slots"))if(!cfg.Mats.ContainsKey(s.Value<string>()))throw new Refuse("mesh "+Str(m,"name")+": slot '"+s.Value<string>()+"' has no material in materials{}");
   var ids=new HashSet<string>(StringComparer.Ordinal);
   foreach(var pl in Arr(j,"places"))foreach(var r in Arr(pl,"rows"))
   {
    string id=Str(r,"id"),kind=Str(r,"kind");if(!ids.Add(id))throw new Refuse("row id '"+id+"' is used twice");
    if(kind=="mesh"){var m=MeshRow(cfg,Str(r,"mesh"));if(Flag(r,"solid")&&!Arr(m,"colliders").Any())throw new Refuse("row "+id+" is solid but mesh "+Str(r,"mesh")+" has no collider box");}
    else if(kind=="prefab"){if(Flag(r,"solid"))throw new Refuse("row "+id+": a prefab row is never solid (its colliders are stripped)");if(OptNum(r,"scale",1f)>cfg.R("prefab_scale_max"))throw new Refuse("row "+id+": scale above rules.prefab_scale_max");}
    else throw new Refuse("row "+id+": kind '"+kind+"' (mesh | prefab)");
   }
   return cfg;
  }
  // the ground under the footprint corners of a row may differ from the ground under its pivot by this much: the row's own value
  // (a mesh made for a slope: poles that reach far under the pivot ground) or the rule for everything else
  static float CornerMax(Cfg cfg,JToken r)=>OptNum(r,"corner_gap_max_m",cfg.R("corner_gap_max_m"));
  static JToken MeshRow(Cfg cfg,string name)=>Arr(cfg.J,"meshes").FirstOrDefault(m=>Str(m,"name")==name)??throw new Refuse("mesh '"+name+"' is not in meshes[]");
  static Target TargetOf(Cfg cfg,string arg)
  {
   arg=(arg??"").Trim().Replace('\\','/');
   var t=cfg.Targets.FirstOrDefault(x=>string.Equals(x.Alias,arg,StringComparison.OrdinalIgnoreCase)||x.Scene==arg);
   if(t==null)throw new Refuse("not a places308 target: '"+arg+"' (allowed: "+string.Join(", ",cfg.Targets.Select(x=>x.Alias))+")");
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
  static WorldMacroPlaytestSO Content(Target t,Scene s)
  {
   var all=s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).ToArray();
   if(all.Length!=1)throw new Refuse(s.path+" has "+all.Length+" WorldMacroPlaytestSession components (expected 1)");
   var c=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(t.Content);if(c==null)throw new Refuse("missing content "+t.Content);
   if(all[0].Content!=c)throw new Refuse(t.Scene+" session Content is "+AssetDatabase.GetAssetPath(all[0].Content)+", expected "+t.Content);
   return c;
  }

  // ---------- ledgers ----------

  [Serializable] sealed class Row{public string kind="",key="",detail="";public bool reverted;}
  [Serializable] sealed class Write{public string utc="",target="",backup="",shaBefore="",shaAfter="",dataSha="";public List<Row> rows=new List<Row>();}
  [Serializable] sealed class Ledger{public string kind="places308",group="",target="",created="";public List<Write> writes=new List<Write>();}
  static string LedgerFile(string key)=>Path.Combine(OutDir,"ledger_places308_"+key+".json");
  static Ledger ReadLedger(string key){var f=LedgerFile(key);return File.Exists(f)?JsonUtility.FromJson<Ledger>(File.ReadAllText(f)):null;}
  static void WriteLedger(string key,Ledger l){Directory.CreateDirectory(OutDir);File.WriteAllText(LedgerFile(key),JsonUtility.ToJson(l,true));}
  static IEnumerable<Row> Live(Ledger l)=>l==null?Enumerable.Empty<Row>():l.writes.Where(w=>w!=null&&w.rows!=null).SelectMany(w=>w.rows).Where(r=>r!=null&&!r.reverted);
  static string Backup(string assetPath)
  {
   string dir=Path.Combine(OutDir,"Backups","places308-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff",CultureInfo.InvariantCulture));Directory.CreateDirectory(dir);
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
   mesh.vertices=v;mesh.normals=n;mesh.uv=uv;mesh.subMeshCount=slots.Length;
   for(int i=0;i<slots.Length;i++){var sub=Array.Find(d.sub,s=>s.m==slots[i]);if(sub==null)throw new Refuse("mesh "+Str(m,"name")+": the json has no submesh '"+slots[i]+"'");mesh.SetTriangles(sub.t,i,false);}
   mesh.RecalculateBounds();mesh.RecalculateTangents();
   return mesh;
  }
  // does the asset on disk carry what the data row describes? (vertex count, submeshes, bounds within 1 mm)
  static bool MeshMatches(Cfg cfg,Mesh a,JToken m)
  {
   if(a==null||a.vertexCount!=Mathf.RoundToInt(Num(m,"verts"))||a.subMeshCount!=Arr(m,"slots").Count())return false;
   Vector3 lo=Vec(m,"lo"),hi=Vec(m,"hi");float tol=cfg.R("mesh_bounds_tol_m");return (a.bounds.min-lo).magnitude<=tol&&(a.bounds.max-hi).magnitude<=tol;
  }
  static bool AnySceneLive(Cfg cfg,out string who)
  {
   who=string.Join(", ",cfg.Targets.Where(t=>Live(ReadLedger("scene_"+t.Alias)).Any()).Select(t=>t.Alias));return who.Length>0;
  }
  static string Assets(string mode)
  {
   var cfg=Load();var sb=new StringBuilder("Places308 assets-"+mode+" (data "+Short(cfg.Sha)+" "+(Opt(cfg.J,"version")??"?")+")\n");
   var ledger=ReadLedger("assets")??new Ledger{group=Opt(cfg.J,"group")??"",target=cfg.AssetDir,created=DateTime.UtcNow.ToString("O")};
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
    WriteLedger("assets",ledger);return sb.Append("  ledger "+LedgerFile("assets")).ToString();
   }
   var todo=new List<JToken>();
   foreach(var m in Arr(cfg.J,"meshes"))
   {
    string name=Str(m,"name"),path=MeshAsset(cfg,name),bad=JsonState(cfg,m);
    if(bad!=null)throw new Refuse("mesh "+name+": "+bad+" (fix4_copy.py copies the json files; nothing written)");
    var have=AssetDatabase.LoadAssetAtPath<Mesh>(path);
    if(have==null){sb.AppendLine("  create "+path+" ("+F(Num(m,"verts"),"F0")+" vertices, "+F(Num(m,"tris"),"F0")+" triangles, slots "+string.Join(" / ",Arr(m,"slots").Select(s=>s.Value<string>()))+")");todo.Add(m);}
    else if(MeshMatches(cfg,have,m))sb.AppendLine("  same   "+path);
    else throw new Refuse(path+" exists and is not the mesh the data describes - assets-revert first (after scene-revert). Nothing written");
   }
   if(todo.Count==0)return sb.Append("  변경 없음 (every mesh asset is in place)").ToString();
   if(mode=="plan")return sb.Append("  plan only: nothing written (would create "+todo.Count+" mesh asset(s))").ToString();
   var write=new Write{utc=DateTime.UtcNow.ToString("O"),target=cfg.AssetDir,dataSha=cfg.Sha};
   // folders first (each one that is made is journalled so the revert can take it out again)
   string acc="";foreach(string part in cfg.AssetDir.Split('/'))
   {
    string parent=acc;acc=acc.Length==0?part:acc+"/"+part;if(AssetDatabase.IsValidFolder(acc))continue;
    write.rows.Add(new Row{kind="folder",key=acc,detail="folder"});if(!ledger.writes.Contains(write))ledger.writes.Add(write);WriteLedger("assets",ledger);
    if(string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent,part)))throw new Refuse("could not create folder "+acc);
   }
   foreach(var m in todo)
   {
    string path=MeshAsset(cfg,Str(m,"name"));var mesh=BuildMesh(cfg,m);
    write.rows.Add(new Row{kind="mesh",key=path,detail=Str(m,"name")+" json "+Short(Str(m,"sha256"))});if(!ledger.writes.Contains(write))ledger.writes.Add(write);WriteLedger("assets",ledger);   // journal before the file
    AssetDatabase.CreateAsset(mesh,path);AssetDatabase.SaveAssetIfDirty(mesh);
    sb.AppendLine("  created "+path+" (guid "+AssetDatabase.AssetPathToGUID(path)+")");
   }
   return sb.Append("  created "+todo.Count+" mesh asset(s); ledger "+LedgerFile("assets")).ToString();
  }

  // ---------- ground ----------

  static bool Under(Transform t,HashSet<string> roots){for(var p=t;p!=null;p=p.parent)if(p.parent==null&&roots.Contains(p.name))return true;return false;}
  static bool Preview(Transform t){for(var p=t;p!=null;p=p.parent)if((p.gameObject.hideFlags&HideFlags.DontSave)!=0)return true;return false;}
  static bool TerrainLike(Collider c){if(c is TerrainCollider)return true;for(var t=c.transform;t!=null;t=t.parent){var n=t.name;if(n.Contains("Terrain")||n.Contains("Surface"))return true;}return false;}
  // the Content308 rule in short (as OreOutcrop308): the highest terrain surface under the XZ; own / listed roots and previews never count
  static bool Ground(Cfg cfg,float x,float z,out Vector3 p,out float slope)
  {
   p=default;slope=90f;var roots=new HashSet<string>(Arr(cfg.Rules,"ground_ignore_roots").Select(v=>v.Value<string>()));bool any=false;
   foreach(var h in Physics.RaycastAll(new Vector3(x,2000f,z),Vector3.down,4000f,~0,QueryTriggerInteraction.Ignore))
   {
    var c=h.collider;if(c==null||h.normal.y<=.5f||c is CharacterController||Preview(c.transform)||Under(c.transform,roots)||!TerrainLike(c))continue;
    if(!any||h.point.y>p.y){p=h.point;slope=Vector3.Angle(h.normal,Vector3.up);any=true;}
   }
   return any;
  }

  // ---------- plan ----------

  sealed class Item
  {
   public string Place,Id,Kind;public JToken Row,MeshData;public Mesh Mesh;public Material[] Mats;public GameObject Prefab;public Vector3 Pos,Scale;public Quaternion Rot;public bool Solid;
   public float Slope,CornerGap;public List<string> Notes=new List<string>();public int Warns;
   public string Path(Cfg cfg)=>cfg.Root+"/"+Place+"/"+Id;
  }
  static Vector2[] Footprint(Item it)
  {
   Vector3 lo,hi;
   if(it.Kind=="mesh"){lo=Vec(it.MeshData,"lo");hi=Vec(it.MeshData,"hi");}
   else{var rs=it.Prefab.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh!=null).ToArray();if(rs.Length==0)return Array.Empty<Vector2>();var b=rs[0].sharedMesh.bounds;lo=b.min;hi=b.max;}
   var m=Matrix4x4.TRS(it.Pos,it.Rot,it.Scale);
   return new[]{new Vector3(lo.x,0,lo.z),new Vector3(hi.x,0,lo.z),new Vector3(hi.x,0,hi.z),new Vector3(lo.x,0,hi.z)}.Select(c=>{var w=m.MultiplyPoint3x4(c);return new Vector2(w.x,w.z);}).ToArray();
  }
  static Item Build(Cfg cfg,JToken place,JToken r)
  {
   var it=new Item{Place=Str(place,"id"),Id=Str(r,"id"),Kind=Str(r,"kind"),Row=r,Solid=Flag(r,"solid")};float x=Num(r,"x"),z=Num(r,"z"),s=OptNum(r,"scale",1f);
   if(!Ground(cfg,x,z,out var g,out float slope))throw new Refuse("no terrain under "+it.Id+" ("+F(x,"F1")+", "+F(z,"F1")+") - is the scene loaded?");
   if(slope>Num(r,"max_slope_deg"))throw new Refuse(it.Id+": physical slope "+F(slope,"F1")+"° > "+F(Num(r,"max_slope_deg"),"F1")+"° - revise the row. Nothing written");
   it.Slope=slope;it.Pos=g+Vector3.down*OptNum(r,"sink_m",0f);it.Rot=Quaternion.Euler(0,Num(r,"yaw"),0);
   if(it.Kind=="mesh")
   {
    it.MeshData=MeshRow(cfg,Str(r,"mesh"));it.Mesh=AssetDatabase.LoadAssetAtPath<Mesh>(MeshAsset(cfg,Str(r,"mesh")));
    if(it.Mesh==null||!MeshMatches(cfg,it.Mesh,it.MeshData))throw new Refuse(it.Id+": mesh asset "+MeshAsset(cfg,Str(r,"mesh"))+(it.Mesh==null?" is missing":" is not the data's mesh")+" - run assets-apply first. Nothing written");
    it.Mats=Arr(it.MeshData,"slots").Select(v=>cfg.Mats[v.Value<string>()]).ToArray();it.Scale=Vector3.one*s;
   }
   else
   {
    it.Prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Str(r,"prefab"));if(it.Prefab==null)throw new Refuse(it.Id+": prefab "+Str(r,"prefab")+" is missing (existing props only)");
    if(Harness303.IsProtected(Str(r,"prefab"))||PostLedger308.IsProtectedPath(Str(r,"prefab")))throw new Refuse(it.Id+": prefab in a protected tree");
    if(Quaternion.Angle(it.Prefab.transform.localRotation,Quaternion.identity)>.1f)throw new Refuse(it.Id+": the prefab root is rotated - it would not stand upright where this tool puts it");
    it.Scale=it.Prefab.transform.localScale*s;
   }
   float dy=g.y-OptNum(r,"y_offline",g.y);
   foreach(var c in Footprint(it))if(Ground(cfg,c.x,c.y,out var gc,out _))it.CornerGap=Mathf.Max(it.CornerGap,Mathf.Abs(gc.y-g.y));
   bool warn=Mathf.Abs(dy)>cfg.R("y_tol_m")||it.CornerGap>CornerMax(cfg,r);if(warn)it.Warns++;
   it.Notes.Add((warn?"WARN ":"")+it.Id+" ("+(it.Kind=="mesh"?Str(r,"mesh"):Path.GetFileNameWithoutExtension(Str(r,"prefab")))+(it.Solid?", solid":"")+") at "+V(it.Pos)+" yaw "+F(Num(r,"yaw"),"F0")+", slope "+F(slope,"F1")+"° (offline y "+F(OptNum(r,"y_offline",g.y))+", Δ "+F(dy)+" m - the editor value is used), ground under the footprint corners within "+F(it.CornerGap)+" m (limit "+F(CornerMax(cfg,r))+(r["corner_gap_max_m"]!=null?", this row's own":"")+")");
   return it;
  }
  static IEnumerable<JToken> EnabledPlaces(Cfg cfg)=>Arr(cfg.J,"places").Where(p=>Flag(p,"enabled",true));
  static Transform Root(Cfg cfg,Scene s)=>s.GetRootGameObjects().Where(g=>g.name==cfg.Root&&!Preview(g.transform)).Select(g=>g.transform).FirstOrDefault();
  static JToken[] Boxes(Item it)=>it.Solid&&it.Kind=="mesh"?Arr(it.MeshData,"colliders").ToArray():Array.Empty<JToken>();
  static bool Same(Cfg cfg,Transform have,Item it)
  {
   if((have.position-it.Pos).magnitude>cfg.R("pose_tol_m")||Quaternion.Angle(have.rotation,it.Rot)>cfg.R("pose_tol_deg")||(have.localScale-it.Scale).magnitude>cfg.R("scale_tol"))return false;
   if(it.Kind=="mesh"){var mf=have.GetComponent<MeshFilter>();var mr=have.GetComponent<MeshRenderer>();if(mf==null||mr==null||mf.sharedMesh!=it.Mesh||!mr.sharedMaterials.SequenceEqual(it.Mats))return false;}
   else if(PrefabUtility.GetCorrespondingObjectFromSource(have.gameObject)!=it.Prefab)return false;
   return have.GetComponentsInChildren<Collider>(true).Length==Boxes(it).Length;
  }

  // ---------- apply / revert ----------

  static string Apply(string arg,bool dry)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,!dry);var scene=Open(t);Physics.SyncTransforms();
   var sb=new StringBuilder("Places308 "+(dry?"plan":"scene-apply")+" "+t.Alias+" (data "+Short(cfg.Sha)+" "+(Opt(cfg.J,"version")??"?")+", group "+(Opt(cfg.J,"group")??"")+")\n");
   var root=Root(cfg,scene);if(root!=null&&PostLedger308.UnderProtectedTree(root))throw new Refuse("root "+cfg.Root+" is a protected tree name");
   var todo=new List<Item>();int warns=0,solid=0;
   foreach(var place in EnabledPlaces(cfg))foreach(var r in Arr(place,"rows"))
   {
    var it=Build(cfg,place,r);warns+=it.Warns;foreach(var n in it.Notes)sb.AppendLine("  "+n);
    var have=root!=null?root.Find(it.Place+"/"+it.Id):null;
    if(have!=null){if(Same(cfg,have,it)){sb.AppendLine("    in the scene, same pose");continue;}throw new Refuse(it.Path(cfg)+" is in "+scene.path+" with another pose / mesh / colliders than the data gives - scene-revert:"+t.Alias+" first. Nothing written");}
    todo.Add(it);if(it.Solid)solid++;
   }
   foreach(var place in Arr(cfg.J,"places").Where(p=>!Flag(p,"enabled",true)))if(root!=null&&root.Find(Str(place,"id"))!=null)throw new Refuse("place "+Str(place,"id")+" is off in the data but in the scene - scene-revert:"+t.Alias+" --place "+Str(place,"id")+" first");
   if(todo.Count==0)return sb.Append("  변경 없음 (no change; WARN "+warns+")").ToString();
   string bake=solid>0?"; "+solid+" of them carry colliders = NEEDS THE NAVMESH BAKE ("+string.Join(", ",todo.Where(i=>i.Solid).Select(i=>i.Id))+")":"; no collider is added";
   if(dry)return sb.Append("  plan only: nothing written (would create "+todo.Count+" object(s)"+bake+"; WARN "+warns+")").ToString();
   string abs=Harness303.Abs(t.Scene);var write=new Write{utc=DateTime.UtcNow.ToString("O"),target=t.Scene,backup=Backup(t.Scene),shaBefore=Harness303.Sha(abs),dataSha=cfg.Sha};
   var ledger=ReadLedger("scene_"+t.Alias)??new Ledger{group=Opt(cfg.J,"group")??"",target=t.Scene,created=DateTime.UtcNow.ToString("O")};
   try
   {
    if(root==null){var go=new GameObject(cfg.Root);SceneManager.MoveGameObjectToScene(go,scene);root=go.transform;write.rows.Add(new Row{kind="create-root",key=cfg.Root,detail="root"});}
    foreach(var it in todo)
    {
     var holder=root.Find(it.Place);
     if(holder==null){holder=new GameObject(it.Place).transform;holder.SetParent(root,false);write.rows.Add(new Row{kind="create-place",key=BuildingAudit308.KeyOf(holder),detail="place "+it.Place});}
     GameObject obj;
     if(it.Kind=="mesh")
     {
      obj=new GameObject(it.Id);obj.transform.SetParent(holder,false);obj.AddComponent<MeshFilter>().sharedMesh=it.Mesh;
      var mr=obj.AddComponent<MeshRenderer>();mr.sharedMaterials=it.Mats;mr.shadowCastingMode=ShadowCastingMode.On;
     }
     else{obj=(GameObject)PrefabUtility.InstantiatePrefab(it.Prefab,scene);obj.name=it.Id;obj.transform.SetParent(holder,true);}
     obj.transform.SetPositionAndRotation(it.Pos,it.Rot);obj.transform.localScale=it.Scale;
     foreach(var c in obj.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
     foreach(var l in obj.GetComponentsInChildren<Light>(true))Object.DestroyImmediate(l);
     var boxes=Boxes(it);
     for(int i=0;i<boxes.Length;i++)
     {
      var cg=new GameObject("Col_"+i);cg.transform.SetParent(obj.transform,false);cg.transform.localPosition=Vec(boxes[i],"center");cg.transform.localRotation=Quaternion.Euler(0,OptNum(boxes[i],"yaw",0f),0);
      cg.AddComponent<BoxCollider>().size=Vec(boxes[i],"size");cg.isStatic=true;
     }
     obj.isStatic=it.Solid;
     write.rows.Add(new Row{kind="create",key=BuildingAudit308.KeyOf(obj.transform),detail=it.Id+" at "+V(it.Pos)+(it.Solid?" solid x"+boxes.Length:"")});
    }
    ledger.writes.Add(write);WriteLedger("scene_"+t.Alias,ledger);   // the ledger is on disk before the scene is saved
   }
   catch(Exception e)
   {
    EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);   // a half-built plan must not stay in memory (nothing was saved)
    return "FAILED scene-apply "+t.Scene+" (scene reloaded from disk, nothing saved; backup "+write.backup+")\n"+e;
   }
   EditorSceneManager.MarkSceneDirty(scene);
   if(!EditorSceneManager.SaveScene(scene))
   {
    ledger.writes.Remove(write);WriteLedger("scene_"+t.Alias,ledger);EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);
    return "FAILED SaveScene returned false for "+t.Scene+" (scene reloaded from disk, nothing saved, the ledger row was taken out again; backup "+write.backup+")";
   }
   write.shaAfter=Harness303.Sha(abs);WriteLedger("scene_"+t.Alias,ledger);
   return sb.Append("  saved: "+todo.Count+" object(s)"+bake+"; WARN "+warns+"; scene sha "+Short(write.shaBefore)+" -> "+Short(write.shaAfter)+"; backup "+write.backup+"; ledger "+LedgerFile("scene_"+t.Alias)).ToString();
  }

  static string Revert(string arg,string place)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,true);var ledger=ReadLedger("scene_"+t.Alias);
   string prefix=place!=null?cfg.Root+"/"+place:null;
   var live=Live(ledger).Where(r=>prefix==null||r.key==prefix||r.key.StartsWith(prefix+"/",StringComparison.Ordinal)).ToList();
   if(live.Count==0)return "Places308 scene-revert "+t.Alias+": 변경 없음 (no live ledger row"+(place!=null?" of "+place:"")+")";
   var scene=Open(t);var sb=new StringBuilder("Places308 scene-revert "+t.Alias+(place!=null?" --place "+place:"")+"\n");string copy=Backup(t.Scene);int kept=0;var marked=new List<Row>();
   // objects first, then their place holders, the root last: a holder / the root goes only when nothing is left inside
   foreach(var r in live.Where(x=>x.kind=="create").Concat(live.Where(x=>x.kind=="create-place")).Concat(Live(ledger).Where(x=>x.kind=="create-root")))
   {
    var tr=BuildingAudit308.Resolve(scene,r.key);
    if(tr==null){r.reverted=true;marked.Add(r);sb.AppendLine("  "+r.key+": already gone");continue;}
    // a name path takes the FIRST sibling of that name: destroy only the object whose own key is still the row's key
    if(BuildingAudit308.KeyOf(tr)!=r.key||tr.root.name!=cfg.Root){kept++;sb.AppendLine("  KEPT "+r.key+": the key no longer names this tool's object");continue;}
    if(r.kind!="create"&&tr.childCount>0){if(r.kind=="create-place"||prefix==null){kept++;sb.AppendLine("  KEPT "+r.key+": "+tr.childCount+" child object(s) are still inside");}continue;}
    Object.DestroyImmediate(tr.gameObject);r.reverted=true;marked.Add(r);sb.AppendLine("  removed "+r.key);
   }
   WriteLedger("scene_"+t.Alias,ledger);EditorSceneManager.MarkSceneDirty(scene);
   if(!EditorSceneManager.SaveScene(scene))
   {
    foreach(var r in marked)r.reverted=false;WriteLedger("scene_"+t.Alias,ledger);EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);
    return "FAILED SaveScene returned false for "+t.Scene+" (scene reloaded from disk, nothing saved; the "+marked.Count+" ledger row(s) of this call are live again - run scene-revert again; backup "+copy+")";
   }
   return sb.Append("  saved; kept "+kept+"; scene sha "+Short(Harness303.Sha(Harness303.Abs(t.Scene)))+"; backup "+copy+" (a NavMesh baked WITH these colliders is stale now: see RUN_ORDER_fix4.md)").ToString();
  }

  // ---------- check (read-only): AC-P1..P8 ----------

  static float SegDist(Vector2 p,Vector2 a,Vector2 b){var ab=b-a;float len=ab.sqrMagnitude;float t=len<1e-8f?0f:Mathf.Clamp01(Vector2.Dot(p-a,ab)/len);return Vector2.Distance(p,a+ab*t);}
  static float PathDist(Vector3[] path,Vector2 q,float gap)
  {
   float best=float.PositiveInfinity;if(path==null)return best;
   for(int i=1;i<path.Length;i++){var a=new Vector2(path[i-1].x,path[i-1].z);var b=new Vector2(path[i].x,path[i].z);if(Vector2.Distance(a,b)>gap)continue;best=Mathf.Min(best,SegDist(q,a,b));}
   return best;
  }
  static string Check(string arg)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,false);var scene=Open(t);var content=Content(t,scene);Physics.SyncTransforms();int fail=0,pass=0;
   var sb=new StringBuilder("Places308 check "+t.Alias+" (data "+Short(cfg.Sha)+")\n");
   void C(bool ok,string what){if(ok)pass++;else fail++;sb.AppendLine("  ["+(ok?"PASS":"FAIL")+"] "+what);}
   var root=Root(cfg,scene);var want=EnabledPlaces(cfg).SelectMany(p=>Arr(p,"rows").Select(r=>Str(p,"id")+"/"+Str(r,"id"))).ToList();
   var have=root==null?new List<string>():root.Cast<Transform>().SelectMany(h=>h.Cast<Transform>().Select(o=>h.name+"/"+o.name)).ToList();
   C(root!=null&&want.All(have.Contains)&&have.Count==want.Count,"AC-P1 "+cfg.Root+" holds exactly the enabled rows ("+have.Count+" of "+want.Count+")"+(root==null?" - the root is absent: run scene-apply":""));
   if(root==null)return sb.Append("  FAIL "+fail).ToString();
   C(scene.GetRootGameObjects().Count(g=>g.name==cfg.Root)==1&&!PostLedger308.UnderProtectedTree(root),"AC-P1 one root named "+cfg.Root+", outside the protected trees");
   C(root.GetComponentsInChildren<Light>(true).Length==0,"AC-P5 Light components under the root: "+root.GetComponentsInChildren<Light>(true).Length+" (no new light)");
   C(root.GetComponentsInChildren<NavMeshAgent>(true).Length==0&&root.GetComponentsInChildren<NavMeshObstacle>(true).Length==0,"AC-P5 NavMeshAgent / NavMeshObstacle under the root: 0");
   var rends=root.GetComponentsInChildren<Renderer>(true);
   C(rends.All(r=>r.sharedMaterials.All(m=>m!=null&&!m.IsKeywordEnabled("_EMISSION"))),"AC-P5 no material under the root has _EMISSION ("+rends.Length+" renderer(s))");
   float gap=cfg.R("path_run_gap_m");var solidNames=new List<string>();int colliders=0;
   foreach(var place in EnabledPlaces(cfg))
   {
    var lesson=Opt(place,"lesson")!=null?Array.Find(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>(),e=>e!=null&&e.Id==Opt(place,"lesson")):null;
    if(Opt(place,"lesson")!=null&&lesson==null)sb.AppendLine("  note "+Str(place,"id")+": encounter "+Opt(place,"lesson")+" is not in this content (the lesson rules are not measured in this scene)");
    foreach(var r in Arr(place,"rows"))
    {
     var obj=root.Find(Str(place,"id")+"/"+Str(r,"id"));if(obj==null){C(false,"AC-P2 "+Str(r,"id")+" is in the scene");continue;}
     Item it;try{it=Build(cfg,place,r);}catch(Refuse e){C(false,"AC-P2 "+Str(r,"id")+": "+e.Message);continue;}
     C(Same(cfg,obj,it),"AC-P2 "+it.Id+" stands where the data and the physical ground put it ("+V(obj.position)+"), with the data's mesh / prefab, materials and "+Boxes(it).Length+" collider box(es)");
     if(it.Kind=="mesh"){var mf=obj.GetComponent<MeshFilter>();C(mf!=null&&mf.sharedMesh!=null&&EditorUtility.IsPersistent(mf.sharedMesh)&&AssetDatabase.GetAssetPath(mf.sharedMesh)==MeshAsset(cfg,Str(r,"mesh")),"AC-P3 "+it.Id+": its mesh is the asset "+MeshAsset(cfg,Str(r,"mesh")));}
     C(it.Slope<=Num(r,"max_slope_deg")&&it.CornerGap<=CornerMax(cfg,r),"AC-P6 "+it.Id+": slope "+F(it.Slope,"F1")+"° <= "+F(Num(r,"max_slope_deg"),"F0")+", ground under the footprint corners within "+F(it.CornerGap)+" m (<= "+F(CornerMax(cfg,r))+")");
     var cols=obj.GetComponentsInChildren<BoxCollider>(true);colliders+=cols.Length;
     if(cols.Length==0)continue;
     solidNames.Add(it.Id);float line=float.PositiveInfinity,les=float.PositiveInfinity;string near="";float nearGap=float.PositiveInfinity;
     foreach(var bc in cols)
     {
      var h=bc.size*.5f;
      foreach(var corner in new[]{new Vector3(-h.x,0,-h.z),new Vector3(h.x,0,-h.z),new Vector3(h.x,0,h.z),new Vector3(-h.x,0,h.z)})
      {
       var w=bc.transform.TransformPoint(bc.center+corner);var q=new Vector2(w.x,w.z);
       line=Mathf.Min(line,Mathf.Min(PathDist(content.MainPath,q,gap),PathDist(content.BranchPath,q,gap)));
       if(lesson!=null)les=Mathf.Min(les,Vector2.Distance(q,new Vector2(lesson.Feet.x,lesson.Feet.z)));
       foreach(var p in content.Points??Array.Empty<PrologueContentSO.Point>()){if(p==null)continue;float d=Vector2.Distance(q,new Vector2(p.Position.x,p.Position.z))-p.Radius;if(d<nearGap){nearGap=d;near=p.Id;}}
      }
     }
     C(line>=cfg.R("corridor_clear_m"),"AC-P7 "+it.Id+": collider corners "+F(line)+" m from the content path centre lines >= "+F(cfg.R("corridor_clear_m"),"F1")+" (layout routes are measured offline: relayout_fix4_dry.py A1)");
     if(lesson!=null)C(les>lesson.Detection,"AC-P7 "+it.Id+": "+F(les)+" m from the lesson feet > its Detection "+F(lesson.Detection,"F0")+" (the lesson's ground is not changed)");
     C(nearGap>=cfg.R("point_collider_pad_m"),"AC-P8 "+it.Id+": nearest content point "+near+" - its prompt disc is "+F(nearGap)+" m from a collider corner (>= "+F(cfg.R("point_collider_pad_m"),"F1")+": no box cuts the eye -> point ray)");
    }
   }
   C(root.GetComponentsInChildren<Collider>(true).Length==colliders,"AC-P4 every collider under the root is a data box ("+colliders+" BoxCollider, "+(root.GetComponentsInChildren<Collider>(true).Length-colliders)+" other)");
   sb.AppendLine("  colliders that the NavMesh bake must see: "+(solidNames.Count==0?"none":string.Join(", ",solidNames)));
   if(scene.isDirty)sb.AppendLine("  WARN the scene is dirty after a read-only pass (bug): reload it");
   return sb.Append("  "+(fail==0?"no FAIL":"FAIL "+fail)+" (PASS "+pass+")").ToString();
  }

  // ---------- eyes: the still lines on the physical ground ----------

  static string Eyes(string arg)
  {
   string label="";int ci=arg.IndexOf(':');if(ci>=0){label=arg.Substring(ci+1).Trim();arg=arg.Substring(0,ci);}
   if(label.Length>0&&!label.All(ch=>char.IsLetterOrDigit(ch)||ch=='_'))throw new Refuse("eyes label: letters, digits and _ only");
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,false);Open(t);Physics.SyncTransforms();float eyeM=Num(Req(cfg.J,"read"),"eye_m");
   var sb=new StringBuilder("Places308 eyes "+t.Alias+" (eye = physical ground + "+F(eyeM,"F1")+" m; a line that starts with 'shot:' is one Presentation297 Run argument, a line that starts with '#' is its note)\n");
   foreach(var s in Arr(cfg.J,"stills"))
   {
    var e=Req(s,"eye") as JArray;var l=Req(s,"look") as JArray;
    if(!Ground(cfg,e[0].Value<float>(),e[1].Value<float>(),out var ge,out _)||!Ground(cfg,l[0].Value<float>(),l[1].Value<float>(),out var gl,out _)){sb.AppendLine("  "+Str(s,"id")+": no ground under the eye or the look-at point");continue;}
    Vector3 eye=ge+Vector3.up*eyeM,look=gl+Vector3.up*Num(s,"look_h_m");
    // two lines per still: the note first (never part of the command), then the shot line with nothing after hideplayer
    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,"  # {0}: {1:F1} m - {2}",Str(s,"id"),Vector3.Distance(eye,look),Opt(s,"what")??""));
    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,"shot:fix4_{0}:{1:F2},{2:F2},{3:F2}:{4:F2},{5:F2},{6:F2}:fov={7:F0}:w={8:F0}:h={9:F0}:hideplayer",(label.Length>0?label+"_":"")+Str(s,"id"),eye.x,eye.y,eye.z,look.x,look.y,look.z,Num(Req(cfg.J,"read"),"fov"),Num(Req(cfg.J,"read"),"still_w"),Num(Req(cfg.J,"read"),"still_h")));
   }
   return sb.ToString();
  }

  // ---------- status ----------

  static string Status()
  {
   var sb=new StringBuilder("Places308 status (play="+EditorApplication.isPlaying+")\n");
   Cfg cfg;try{cfg=Load();}catch(Refuse r){return sb.Append("  "+r.Message).ToString();}
   int rows=EnabledPlaces(cfg).Sum(p=>Arr(p,"rows").Count()),solid=EnabledPlaces(cfg).Sum(p=>Arr(p,"rows").Count(r=>Flag(r,"solid")));
   sb.AppendLine("  data "+DataFile+" sha "+Short(cfg.Sha)+" version "+(Opt(cfg.J,"version")??"?")+", group "+(Opt(cfg.J,"group")??"")+": "+EnabledPlaces(cfg).Count()+" place(s), "+rows+" row(s), "+solid+" with colliders (bake)");
   int ok=Arr(cfg.J,"meshes").Count(m=>MeshMatches(cfg,AssetDatabase.LoadAssetAtPath<Mesh>(MeshAsset(cfg,Str(m,"name"))),m));
   sb.AppendLine("  mesh assets in place "+ok+"/"+Arr(cfg.J,"meshes").Count()+" under "+cfg.AssetDir+"; mesh json "+Arr(cfg.J,"meshes").Count(m=>JsonState(cfg,m)==null)+"/"+Arr(cfg.J,"meshes").Count()+" as the data says; live asset ledger rows "+Live(ReadLedger("assets")).Count());
   foreach(var t in cfg.Targets)sb.AppendLine("  "+t.Alias+": live scene ledger rows "+Live(ReadLedger("scene_"+t.Alias)).Count());
   var active=SceneManager.GetActiveScene();var root=Root(cfg,active);
   return sb.Append("  active scene "+active.path+(active.isDirty?" (dirty)":"")+": "+cfg.Root+" "+(root==null?"absent":"present, "+root.Cast<Transform>().Sum(h=>h.childCount)+" object(s)")).ToString();
  }
 }
}
