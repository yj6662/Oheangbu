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
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;
using Dress308=Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 opening guide (D308-29 answer 1, group G16_open_guide) - the stage parts at the mine mouth that survived the adversarial
 // reviews of the three stage-only designs (Docs/Handoff/Results308/OpeningGuide2). Every value [TEST].
 // This tool owns three things and nothing else:
 //   rows[]    new dressing objects under ONE own root (OpenGuide308): generated Mesh assets with EXISTING materials -
 //             the stacked pit props across the mouth of the old cut road (solid: one BoxCollider each) and the ore-cart rails that
 //             leave the old tip track at the turnout and run down the hill-side shoulder of the inn road (no collider).
 //             y_mode ground = the pivot stands on the physical ground; y_mode data = a mesh baked to the height field in world
 //             axes: the pivot y is the data's and probes[] are compared with the physical ground.
 //   hides[]   existing scene objects switched off (the #306 tip-track rails and sleepers: the line onto the spoil heap). Their
 //             own tool (Mine306) cannot run - its data is lost - so they are not rebuilt, only SetActive(false), journalled.
 //   trees{}   named rows taken out of ONE vegetation sheet (shared by the three scenes), journalled with the placement json.
 //             BuildingFix308.VegRows is not used: it needs veg308.json, which is lost.
 // The beast move, the MainPath legs and the stele move are data rows of Content308 (content308_relayout.json /
 // content308_scene.json) - this tool only READS their result: expect{} is compared in check:<scene>, and plan / scene-apply
 // refuse solid rows while the content path still passes under them (run Content308 paths-regen first).
 // No light, no emission, no text, no content row, no camera, no HUD, no terrain edit. Queue-safe: Run(string) never opens a
 // dialog; refusals come back as "REFUSED ...". Ledgers (Art/Playtest308/Pacing/ledger_openguide308_assets.json,
 // ledger_openguide308_scene_<alias>.json, ledger_openguide308_trees.json) are NEW files of this tool, written BEFORE the asset /
 // scene / sheet is saved, with a byte backup; a second identical apply reports "변경 없음"; a revert takes out exactly what
 // the ledger made. Only the target scene / the created Mesh assets / the one sheet are saved (never AssetDatabase.SaveAssets).
 //   status
 //   assets-plan | assets-apply | assets-revert      the Mesh assets under asset_dir (once; shared by the three scenes)
 //   plan:<scene> | scene-apply:<scene> | scene-revert:<scene>
 //   check:<scene>                                   AC-G1..G13 on the saved scene; read-only
 //   trees-plan | trees-apply | trees-revert         the sheet rows (once; shared by the three scenes; no scene is saved)
 // <scene> = arch296 | folk298 | main. Order: RUN_ORDER_openguide.md. The two solid rows carry BoxColliders: they must be in all
 // three scenes BEFORE the one NavMesh bake.
 public static partial class OpenGuide308
 {
  const string Usage="status | assets-plan | assets-apply | assets-revert | plan:<scene> | scene-apply:<scene> | scene-revert:<scene> | check:<scene> | trees-plan | trees-apply | trees-revert";
  static string DataFile=>Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","CliffBoundary308","openguide308.json");
  static string OutDir=>Path.Combine(Harness303.RepoRoot,"Art","Playtest308","Pacing");
  sealed class Refuse:Exception{public Refuse(string m):base(m){}}

  public static string Run(string command)
  {
   string c=(command??"").Trim();
   try
   {
    if(c=="status")return Status();
    if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Edit mode only (Play is running)";
    if(EditorApplication.isCompiling||EditorUtility.scriptCompilationFailed)return "REFUSED scripts are compiling or failed to compile";
    if(c=="assets-plan")return Assets("plan");
    if(c=="assets-apply")return Assets("apply");
    if(c=="assets-revert")return Assets("revert");
    if(c=="trees-plan")return Trees("plan");
    if(c=="trees-apply")return Trees("apply");
    if(c=="trees-revert")return Trees("revert");
    string a;
    if((a=Arg(c,"plan:"))!=null)return Apply(a,true);
    if((a=Arg(c,"scene-apply:"))!=null)return Apply(a,false);
    if((a=Arg(c,"scene-revert:"))!=null)return Revert(a);
    if((a=Arg(c,"check:"))!=null)return Check(a);
   }
   catch(Refuse r){return "REFUSED "+r.Message;}
   catch(Exception e){return "FAILED "+e;}
   return "REFUSED unknown OpenGuide308 command '"+c+"' ("+Usage+")";
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
  static JToken Req(JToken o,string key){var t=o?[key];if(t==null||t.Type==JTokenType.Null)throw new Refuse("openguide308.json: missing '"+key+"' in "+(o==null?"(null)":string.IsNullOrEmpty(o.Path)?"(root)":o.Path));return t;}
  static float Num(JToken o,string key)=>Req(o,key).Value<float>();
  static float OptNum(JToken o,string key,float fallback){var t=o?[key];return t==null||t.Type==JTokenType.Null?fallback:t.Value<float>();}
  static string Str(JToken o,string key)=>Req(o,key).Value<string>();
  static string Opt(JToken o,string key){var t=o?[key];return t==null||t.Type==JTokenType.Null?null:t.Value<string>();}
  static bool Flag(JToken o,string key,bool fallback=false){var t=o?[key];return t!=null&&t.Type==JTokenType.Boolean?t.Value<bool>():fallback;}
  static IEnumerable<JToken> Arr(JToken o,string key){var t=o?[key] as JArray;return t??(IEnumerable<JToken>)Array.Empty<JToken>();}
  static Vector3 Vec(JToken o,string key){var a=Req(o,key) as JArray;if(a==null||a.Count<3)throw new Refuse("openguide308.json: "+key+" needs 3 numbers");return new Vector3(a[0].Value<float>(),a[1].Value<float>(),a[2].Value<float>());}
  static string F(float v,string f="F2")=>v.ToString(f,CultureInfo.InvariantCulture);
  static string V(Vector3 v)=>Harness303.V(v);
  static string Short(string sha)=>string.IsNullOrEmpty(sha)?"-":sha.Substring(0,Math.Min(12,sha.Length)).ToLowerInvariant();
  static bool Protected(string path)=>Harness303.IsProtected(path)||PostLedger308.IsProtectedPath(path);

  static Cfg Load()
  {
   string f=DataFile;if(!File.Exists(f))throw new Refuse("data missing: "+f+" (python Tools/Unity/Stage308_openguide/openguide_copy.py --apply)");
   JObject j;try{j=JObject.Parse(File.ReadAllText(f));}catch(Exception e){throw new Refuse("data does not parse: "+f+" ("+e.Message+")");}
   var cfg=new Cfg{J=j,Sha=Harness303.Sha(f)};
   foreach(string key in new[]{"rules","rows","hides","trees","meshes","materials","root","asset_dir","mesh_dir","expect"})Req(j,key);
   foreach(string key in new[]{"pose_tol_m","pose_tol_deg","scale_tol","y_tol_m","baked_y_tol_m","corner_gap_max_m","corridor_clear_m","path_run_gap_m","point_collider_pad_m","mesh_bounds_tol_m","wall_min_m","wall_under_m","expect_tol_m","beast_tol_m"})Num(cfg.Rules,key);   // a missing rule refuses here, not half way through a pass
   cfg.Targets=Arr(j,"targets").Select(t=>new Target{Alias=Str(t,"alias"),Scene=Str(t,"scene"),Content=Str(t,"content")}).ToArray();
   if(cfg.Targets.Length==0)throw new Refuse("openguide308.json: targets[] is empty");
   foreach(var p in cfg.Targets.SelectMany(t=>new[]{t.Scene,t.Content}).Concat(new[]{cfg.AssetDir,Str(Req(j,"trees"),"sheet")}))if(Protected(p))throw new Refuse("protected path in data: "+p);
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
   foreach(var r in Arr(j,"rows"))
   {
    string id=Str(r,"id");if(!ids.Add(id))throw new Refuse("row id '"+id+"' is used twice");
    if(Str(r,"kind")!="mesh")throw new Refuse("row "+id+": kind '"+Str(r,"kind")+"' (mesh only)");
    string ym=Str(r,"y_mode");if(ym!="ground"&&ym!="data")throw new Refuse("row "+id+": y_mode '"+ym+"' (ground | data)");
    var m=MeshRow(cfg,Str(r,"mesh"));bool solid=Flag(r,"solid");
    if(solid&&!Arr(m,"colliders").Any())throw new Refuse("row "+id+" is solid but mesh "+Str(r,"mesh")+" has no collider box");
    if(solid&&ym!="ground")throw new Refuse("row "+id+": a solid row stands on the physical ground (y_mode ground)");
    if(ym=="ground")Num(r,"max_slope_deg");else{Num(r,"y");if(!Arr(r,"probes").Any())throw new Refuse("row "+id+": y_mode data needs probes[]");}
   }
   foreach(var h in Arr(j,"hides")){if(!ids.Add(Str(h,"id")))throw new Refuse("hide id '"+Str(h,"id")+"' is used twice");Str(h,"path");Str(h,"mesh");}
   return cfg;
  }
  static float CornerMax(Cfg cfg,JToken r)=>OptNum(r,"corner_gap_max_m",cfg.R("corner_gap_max_m"));
  static JToken MeshRow(Cfg cfg,string name)=>Arr(cfg.J,"meshes").FirstOrDefault(m=>Str(m,"name")==name)??throw new Refuse("mesh '"+name+"' is not in meshes[]");
  static Target TargetOf(Cfg cfg,string arg)
  {
   arg=(arg??"").Trim().Replace('\\','/');
   var t=cfg.Targets.FirstOrDefault(x=>string.Equals(x.Alias,arg,StringComparison.OrdinalIgnoreCase)||x.Scene==arg);
   if(t==null)throw new Refuse("not an openguide308 target: '"+arg+"' (allowed: "+string.Join(", ",cfg.Targets.Select(x=>x.Alias))+")");
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

  // ---------- ledgers (new files of this tool) ----------

  [Serializable] sealed class Row{public string kind="",key="",detail="";public bool reverted;}
  [Serializable] sealed class Write{public string utc="",target="",backup="",shaBefore="",shaAfter="",dataSha="";public List<Row> rows=new List<Row>();}
  [Serializable] sealed class Ledger{public string kind="openguide308",group="",target="",created="";public List<Write> writes=new List<Write>();}
  static string LedgerFile(string key)=>Path.Combine(OutDir,"ledger_openguide308_"+key+".json");
  static Ledger ReadLedger(string key){var f=LedgerFile(key);return File.Exists(f)?JsonUtility.FromJson<Ledger>(File.ReadAllText(f)):null;}
  static void WriteLedger(string key,Ledger l){Directory.CreateDirectory(OutDir);File.WriteAllText(LedgerFile(key),JsonUtility.ToJson(l,true));}
  static IEnumerable<Row> Live(Ledger l)=>l==null?Enumerable.Empty<Row>():l.writes.Where(w=>w!=null&&w.rows!=null).SelectMany(w=>w.rows).Where(r=>r!=null&&!r.reverted);
  static string Backup(string assetPath,string tag="")
  {
   string dir=Path.Combine(OutDir,"Backups","openguide308-"+tag+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff",CultureInfo.InvariantCulture));Directory.CreateDirectory(dir);
   string copy=Path.Combine(dir,Path.GetFileName(assetPath));File.Copy(Harness303.Abs(assetPath),copy,true);return copy;
  }

  // ---------- Mesh assets (one per meshes[] row, shared by the three scenes) ----------

  [Serializable] sealed class KitSub{public string m;public int[] t;}
  [Serializable] sealed class KitMesh{public string name;public float[] v,n,uv;public KitSub[] sub;}
  static string MeshAsset(Cfg cfg,string name)=>cfg.AssetDir+"/"+name+".asset";
  static string MeshJson(Cfg cfg,JToken m)=>Path.Combine(cfg.MeshDir,Str(m,"json"));
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
  static bool MeshMatches(Cfg cfg,Mesh a,JToken m)
  {
   if(a==null||a.vertexCount!=Mathf.RoundToInt(Num(m,"verts"))||a.subMeshCount!=Arr(m,"slots").Count())return false;
   Vector3 lo=Vec(m,"lo"),hi=Vec(m,"hi");float tol=cfg.R("mesh_bounds_tol_m");return (a.bounds.min-lo).magnitude<=tol&&(a.bounds.max-hi).magnitude<=tol;
  }
  static bool AnySceneLive(Cfg cfg,out string who)
  {
   who=string.Join(", ",cfg.Targets.Where(t=>Live(ReadLedger("scene_"+t.Alias)).Any(r=>r.kind=="create")).Select(t=>t.Alias));return who.Length>0;
  }
  static string Assets(string mode)
  {
   var cfg=Load();var sb=new StringBuilder("OpenGuide308 assets-"+mode+" (data "+Short(cfg.Sha)+" "+(Opt(cfg.J,"version")??"?")+")\n");
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
    if(bad!=null)throw new Refuse("mesh "+name+": "+bad+" (openguide_copy.py copies the json files; nothing written)");
    var have=AssetDatabase.LoadAssetAtPath<Mesh>(path);
    if(have==null){sb.AppendLine("  create "+path+" ("+F(Num(m,"verts"),"F0")+" vertices, "+F(Num(m,"tris"),"F0")+" triangles, slots "+string.Join(" / ",Arr(m,"slots").Select(s=>s.Value<string>()))+")");todo.Add(m);}
    else if(MeshMatches(cfg,have,m))sb.AppendLine("  same   "+path);
    else throw new Refuse(path+" exists and is not the mesh the data describes - assets-revert first (after scene-revert). Nothing written");
   }
   if(todo.Count==0)return sb.Append("  변경 없음 (every mesh asset is in place)").ToString();
   if(mode=="plan")return sb.Append("  plan only: nothing written (would create "+todo.Count+" mesh asset(s))").ToString();
   var write=new Write{utc=DateTime.UtcNow.ToString("O"),target=cfg.AssetDir,dataSha=cfg.Sha};
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
  // the Content308 rule in short (as Places308): the highest terrain surface under the XZ; own / listed roots and previews never count
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
   public string Id;public JToken Row,MeshData;public Mesh Mesh;public Material[] Mats;public Vector3 Pos;public Quaternion Rot;public bool Solid,Baked,Shadows;
   public float Slope,CornerGap,ProbeGap,WallMin=float.PositiveInfinity,UnderMin=float.PositiveInfinity;public List<string> Notes=new List<string>();public int Warns;
   public string Path(Cfg cfg)=>cfg.Root+"/"+Id;
  }
  static JToken[] Boxes(Item it)=>it.Solid?Arr(it.MeshData,"colliders").ToArray():Array.Empty<JToken>();
  // world XZ corners + world top / bottom of a collider box of the row
  static IEnumerable<(Vector2 xz,float top,float bottom)> BoxCorners(Item it,JToken box)
  {
   Vector3 c=Vec(box,"center"),s=Vec(box,"size");var m=Matrix4x4.TRS(it.Pos,it.Rot*Quaternion.Euler(0,OptNum(box,"yaw",0f),0),Vector3.one);
   foreach(var k in new[]{new Vector3(-1,0,-1),new Vector3(1,0,-1),new Vector3(1,0,1),new Vector3(-1,0,1)})
   {var w=m.MultiplyPoint3x4(c+new Vector3(k.x*s.x*.5f,0,k.z*s.z*.5f));yield return (new Vector2(w.x,w.z),w.y+s.y*.5f,w.y-s.y*.5f);}
  }
  static Item Build(Cfg cfg,JToken r)
  {
   var it=new Item{Id=Str(r,"id"),Row=r,Solid=Flag(r,"solid"),Baked=Str(r,"y_mode")=="data",Shadows=Flag(r,"shadows",true)};float x=Num(r,"x"),z=Num(r,"z");
   if(!Ground(cfg,x,z,out var g,out float slope))throw new Refuse("no terrain under "+it.Id+" ("+F(x,"F1")+", "+F(z,"F1")+") - is the scene loaded?");
   it.Slope=slope;it.Rot=Quaternion.Euler(0,Num(r,"yaw"),0);
   it.MeshData=MeshRow(cfg,Str(r,"mesh"));it.Mesh=AssetDatabase.LoadAssetAtPath<Mesh>(MeshAsset(cfg,Str(r,"mesh")));
   if(it.Mesh==null||!MeshMatches(cfg,it.Mesh,it.MeshData))throw new Refuse(it.Id+": mesh asset "+MeshAsset(cfg,Str(r,"mesh"))+(it.Mesh==null?" is missing":" is not the data's mesh")+" - run assets-apply first. Nothing written");
   it.Mats=Arr(it.MeshData,"slots").Select(v=>cfg.Mats[v.Value<string>()]).ToArray();
   if(it.Baked)
   {
    // a mesh baked to the height field: its pivot y is the data's; the physical ground under the probes says whether that still holds
    it.Pos=new Vector3(x,Num(r,"y"),z);int n=0;
    foreach(var p in Arr(r,"probes")){var a=p as JArray;if(a==null||a.Count<3)throw new Refuse(it.Id+": a probe needs [x, z, y]");if(!Ground(cfg,a[0].Value<float>(),a[1].Value<float>(),out var gp,out _))throw new Refuse(it.Id+": no terrain under a probe");it.ProbeGap=Mathf.Max(it.ProbeGap,Mathf.Abs(gp.y-a[2].Value<float>()));n++;}
    if(it.ProbeGap>cfg.R("y_tol_m"))throw new Refuse(it.Id+": the physical ground is "+F(it.ProbeGap)+" m off the height field this mesh was baked to (> "+F(cfg.R("y_tol_m"))+") - build the stage again on the current terrain. Nothing written");
    bool warn=it.ProbeGap>cfg.R("baked_y_tol_m");if(warn)it.Warns++;
    it.Notes.Add((warn?"WARN ":"")+it.Id+" ("+Str(r,"mesh")+", baked) pivot "+V(it.Pos)+", physical ground under "+n+" probe(s) within "+F(it.ProbeGap,"F3")+" m of the height field (warn above "+F(cfg.R("baked_y_tol_m"))+")");
    return it;
   }
   if(slope>Num(r,"max_slope_deg"))throw new Refuse(it.Id+": physical slope "+F(slope,"F1")+"° > "+F(Num(r,"max_slope_deg"),"F1")+"° - revise the row. Nothing written");
   it.Pos=g+Vector3.down*OptNum(r,"sink_m",0f);
   float dy=g.y-OptNum(r,"y",g.y);
   foreach(var box in Boxes(it))foreach(var c in BoxCorners(it,box))
   {
    if(!Ground(cfg,c.xz.x,c.xz.y,out var gc,out _))throw new Refuse(it.Id+": no terrain under a collider corner");
    it.CornerGap=Mathf.Max(it.CornerGap,Mathf.Abs(gc.y-g.y));it.WallMin=Mathf.Min(it.WallMin,c.top-gc.y);it.UnderMin=Mathf.Min(it.UnderMin,gc.y-c.bottom);
   }
   bool w2=Mathf.Abs(dy)>cfg.R("y_tol_m")||it.CornerGap>CornerMax(cfg,r);if(w2)it.Warns++;
   it.Notes.Add((w2?"WARN ":"")+it.Id+" ("+Str(r,"mesh")+(it.Solid?", solid":"")+") at "+V(it.Pos)+" yaw "+F(Num(r,"yaw"),"F1")+", slope "+F(slope,"F1")+"° (offline y "+F(OptNum(r,"y",g.y))+", Δ "+F(dy)+" m - the editor value is used), ground under the collider corners within "+F(it.CornerGap)+" m (limit "+F(CornerMax(cfg,r))+")"+(it.Solid?", collider top "+F(it.WallMin)+" m or more over the ground, bottom "+F(it.UnderMin)+" m or more under it":""));
   return it;
  }
  // a solid row must be a wall, not a step (no lip a jump of 0.75 m reaches), and must not float
  static string WallFault(Cfg cfg,Item it)
  {
   if(!it.Solid)return null;
   if(it.WallMin<cfg.R("wall_min_m"))return it.Id+": a collider top is only "+F(it.WallMin)+" m over the physical ground (< "+F(cfg.R("wall_min_m"))+" = a lip that can be jumped on)";
   if(it.UnderMin<cfg.R("wall_under_m"))return it.Id+": a collider bottom is "+F(-it.UnderMin)+" m over the ground at a corner (a gap under the box)";
   return null;
  }
  static float SegDist(Vector2 p,Vector2 a,Vector2 b){var ab=b-a;float len=ab.sqrMagnitude;float t=len<1e-8f?0f:Mathf.Clamp01(Vector2.Dot(p-a,ab)/len);return Vector2.Distance(p,a+ab*t);}
  static float PathDist(Vector3[] path,Vector2 q,float gap)
  {
   float best=float.PositiveInfinity;if(path==null)return best;
   for(int i=1;i<path.Length;i++){var a=new Vector2(path[i-1].x,path[i-1].z);var b=new Vector2(path[i].x,path[i].z);if(Vector2.Distance(a,b)>gap)continue;best=Mathf.Min(best,SegDist(q,a,b));}
   return best;
  }
  // collider corners against the content path centre lines and the prompt discs of the content points
  static void Corridor(Cfg cfg,WorldMacroPlaytestSO content,Item it,out float line,out float pad,out string near)
  {
   line=float.PositiveInfinity;pad=float.PositiveInfinity;near="";float gap=cfg.R("path_run_gap_m");
   foreach(var box in Boxes(it))foreach(var c in BoxCorners(it,box))
   {
    line=Mathf.Min(line,Mathf.Min(PathDist(content.MainPath,c.xz,gap),PathDist(content.BranchPath,c.xz,gap)));
    foreach(var p in content.Points??Array.Empty<PrologueContentSO.Point>()){if(p==null)continue;float d=Vector2.Distance(c.xz,new Vector2(p.Position.x,p.Position.z))-p.Radius;if(d<pad){pad=d;near=p.Id;}}
   }
  }
  static Transform Root(Cfg cfg,Scene s)=>s.GetRootGameObjects().Where(g=>g.name==cfg.Root&&!Preview(g.transform)).Select(g=>g.transform).FirstOrDefault();
  static bool Same(Cfg cfg,Transform have,Item it)
  {
   if((have.position-it.Pos).magnitude>cfg.R("pose_tol_m")||Quaternion.Angle(have.rotation,it.Rot)>cfg.R("pose_tol_deg")||(have.localScale-Vector3.one).magnitude>cfg.R("scale_tol"))return false;
   var mf=have.GetComponent<MeshFilter>();var mr=have.GetComponent<MeshRenderer>();if(mf==null||mr==null||mf.sharedMesh!=it.Mesh||!mr.sharedMaterials.SequenceEqual(it.Mats))return false;
   return have.GetComponentsInChildren<Collider>(true).Length==Boxes(it).Length;
  }
  // an existing scene object by its plain hierarchy path; every segment must name exactly one object (a path that could mean two
  // objects is refused, not guessed: project memory "이름 경로 첫 형제 함정")
  static Transform Unique(Scene s,string path,out string why)
  {
   why=null;var seg=path.Split('/');var roots=s.GetRootGameObjects().Where(g=>g.name==seg[0]).ToArray();
   if(roots.Length!=1){why=roots.Length+" root object(s) named "+seg[0];return null;}
   var t=roots[0].transform;
   for(int i=1;i<seg.Length;i++)
   {
    var kids=t.Cast<Transform>().Where(c=>c.name==seg[i]).ToArray();
    if(kids.Length!=1){why=kids.Length+" child object(s) named "+seg[i]+" under "+Harness303.PathOf(t);return null;}
    t=kids[0];
   }
   return t;
  }
  sealed class Hide{public string Id,Path;public Transform T;public bool On,Ours;public string Note;}
  static Hide HideOf(Cfg cfg,Scene scene,JToken h,Ledger ledger)
  {
   var o=new Hide{Id=Str(h,"id"),Path=Str(h,"path")};o.T=Unique(scene,o.Path,out string why);
   if(o.T==null){o.Note=o.Id+": "+o.Path+" is not in this scene ("+why+")";return o;}
   if(PostLedger308.UnderProtectedTree(o.T))throw new Refuse(o.Id+": "+o.Path+" is under a protected tree");
   var mf=o.T.GetComponent<MeshFilter>();string mesh=mf!=null&&mf.sharedMesh!=null?AssetDatabase.GetAssetPath(mf.sharedMesh):"";
   if(mesh!=Str(h,"mesh"))throw new Refuse(o.Id+": "+o.Path+" carries mesh '"+mesh+"', the data means "+Str(h,"mesh")+" - not the object this row was written for. Nothing written");
   if(o.T.GetComponentsInChildren<Collider>(true).Length>0)throw new Refuse(o.Id+": "+o.Path+" carries a collider - hiding it would change the walkable ground. Nothing written");
   o.On=o.T.gameObject.activeSelf;o.Ours=Live(ledger).Any(r=>r.kind=="hide"&&r.key==o.Path);
   return o;
  }

  // ---------- apply / revert ----------

  static string Apply(string arg,bool dry)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,!dry);var scene=Open(t);var content=Content(t,scene);Physics.SyncTransforms();
   var sb=new StringBuilder("OpenGuide308 "+(dry?"plan":"scene-apply")+" "+t.Alias+" (data "+Short(cfg.Sha)+" "+(Opt(cfg.J,"version")??"?")+", group "+(Opt(cfg.J,"group")??"")+")\n");
   var root=Root(cfg,scene);if(root!=null&&PostLedger308.UnderProtectedTree(root))throw new Refuse("root "+cfg.Root+" is a protected tree name");
   var ledger=ReadLedger("scene_"+t.Alias)??new Ledger{group=Opt(cfg.J,"group")??"",target=t.Scene,created=DateTime.UtcNow.ToString("O")};
   var todo=new List<Item>();int warns=0,solid=0;
   foreach(var r in Arr(cfg.J,"rows"))
   {
    var it=Build(cfg,r);warns+=it.Warns;foreach(var n in it.Notes)sb.AppendLine("  "+n);
    string fault=WallFault(cfg,it);if(fault!=null)throw new Refuse(fault+" - revise the row. Nothing written");
    if(it.Solid)
    {
     Corridor(cfg,content,it,out float line,out float pad,out string near);
     if(line<cfg.R("corridor_clear_m"))throw new Refuse(it.Id+": a collider corner is "+F(line)+" m from a content path centre line (< "+F(cfg.R("corridor_clear_m"),"F1")+"): the content path still passes here - run Content308 paths-regen:"+t.Alias+" first (RUN_ORDER_openguide.md). Nothing written");
     if(pad<cfg.R("point_collider_pad_m"))throw new Refuse(it.Id+": a collider corner is "+F(pad)+" m from the prompt disc of content point "+near+" (< "+F(cfg.R("point_collider_pad_m"),"F1")+"). Nothing written");
     sb.AppendLine("    collider corners "+F(line)+" m from the content path centre lines, "+F(pad)+" m from the prompt disc of "+near);
    }
    var have=root!=null?root.Find(it.Id):null;
    if(have!=null){if(Same(cfg,have,it)){sb.AppendLine("    in the scene, same pose");continue;}throw new Refuse(it.Path(cfg)+" is in "+scene.path+" with another pose / mesh / colliders than the data gives - scene-revert:"+t.Alias+" first. Nothing written");}
    todo.Add(it);if(it.Solid)solid++;
   }
   if(root!=null)foreach(Transform c in root)if(!Arr(cfg.J,"rows").Any(r=>Str(r,"id")==c.name))throw new Refuse(cfg.Root+"/"+c.name+" is in the scene and not in the data - scene-revert:"+t.Alias+" with the data that made it first. Nothing written");
   var hides=new List<Hide>();
   foreach(var h in Arr(cfg.J,"hides"))
   {
    var o=HideOf(cfg,scene,h,ledger);
    if(o.T==null){sb.AppendLine("  note "+o.Note+"; skipped");continue;}
    if(o.On){hides.Add(o);sb.AppendLine("  hide "+o.Path+" (active -> off)");}
    else sb.AppendLine("  "+o.Path+": already off"+(o.Ours?" (this tool's ledger)":" (NOT by this tool: left as it is, a revert will not switch it on)"));
   }
   if(todo.Count==0&&hides.Count==0)return sb.Append("  변경 없음 (no change; WARN "+warns+")").ToString();
   string bake=solid>0?"; "+solid+" of them carry colliders = NEEDS THE NAVMESH BAKE ("+string.Join(", ",todo.Where(i=>i.Solid).Select(i=>i.Id))+")":"; no collider is added";
   if(dry)return sb.Append("  plan only: nothing written (would create "+todo.Count+" object(s)"+bake+", switch off "+hides.Count+" object(s); WARN "+warns+")").ToString();
   string abs=Harness303.Abs(t.Scene);var write=new Write{utc=DateTime.UtcNow.ToString("O"),target=t.Scene,backup=Backup(t.Scene),shaBefore=Harness303.Sha(abs),dataSha=cfg.Sha};
   try
   {
    if(root==null&&todo.Count>0){var go=new GameObject(cfg.Root);SceneManager.MoveGameObjectToScene(go,scene);root=go.transform;write.rows.Add(new Row{kind="create-root",key=cfg.Root,detail="root"});}
    foreach(var it in todo)
    {
     var obj=new GameObject(it.Id);obj.transform.SetParent(root,false);obj.AddComponent<MeshFilter>().sharedMesh=it.Mesh;
     var mr=obj.AddComponent<MeshRenderer>();mr.sharedMaterials=it.Mats;mr.shadowCastingMode=it.Shadows?ShadowCastingMode.On:ShadowCastingMode.Off;
     obj.transform.SetPositionAndRotation(it.Pos,it.Rot);obj.transform.localScale=Vector3.one;
     var boxes=Boxes(it);
     for(int i=0;i<boxes.Length;i++)
     {
      var cg=new GameObject("Col_"+i);cg.transform.SetParent(obj.transform,false);cg.transform.localPosition=Vec(boxes[i],"center");cg.transform.localRotation=Quaternion.Euler(0,OptNum(boxes[i],"yaw",0f),0);
      cg.AddComponent<BoxCollider>().size=Vec(boxes[i],"size");cg.isStatic=true;
     }
     obj.isStatic=it.Solid;
     write.rows.Add(new Row{kind="create",key=cfg.Root+"/"+it.Id,detail=it.Id+" at "+V(it.Pos)+(it.Solid?" solid x"+boxes.Length:"")});
    }
    foreach(var o in hides){o.T.gameObject.SetActive(false);write.rows.Add(new Row{kind="hide",key=o.Path,detail=o.Id+": active -> off"});}
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
   return sb.Append("  saved: "+todo.Count+" object(s)"+bake+", "+hides.Count+" object(s) switched off; WARN "+warns+"; scene sha "+Short(write.shaBefore)+" -> "+Short(write.shaAfter)+"; backup "+write.backup+"; ledger "+LedgerFile("scene_"+t.Alias)).ToString();
  }

  static string Revert(string arg)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,true);var ledger=ReadLedger("scene_"+t.Alias);
   var live=Live(ledger).ToList();
   if(live.Count==0)return "OpenGuide308 scene-revert "+t.Alias+": 변경 없음 (no live ledger row)";
   var scene=Open(t);var sb=new StringBuilder("OpenGuide308 scene-revert "+t.Alias+"\n");string copy=Backup(t.Scene);int kept=0;var marked=new List<Row>();
   // hidden objects first (switched on again), then the created objects, the root last (only when nothing is left inside)
   foreach(var r in live.Where(x=>x.kind=="hide"))
   {
    var tr=Unique(scene,r.key,out string why);
    if(tr==null){kept++;sb.AppendLine("  KEPT "+r.key+": "+why);continue;}
    if(tr.gameObject.activeSelf)sb.AppendLine("  "+r.key+": already on");else{tr.gameObject.SetActive(true);sb.AppendLine("  switched on "+r.key);}
    r.reverted=true;marked.Add(r);
   }
   foreach(var r in live.Where(x=>x.kind=="create").Concat(live.Where(x=>x.kind=="create-root")))
   {
    var tr=Unique(scene,r.key,out string why);
    if(tr==null){if(why.StartsWith("0 ",StringComparison.Ordinal)){r.reverted=true;marked.Add(r);sb.AppendLine("  "+r.key+": already gone");}else{kept++;sb.AppendLine("  KEPT "+r.key+": "+why);}continue;}
    if(tr.root.name!=cfg.Root){kept++;sb.AppendLine("  KEPT "+r.key+": not under "+cfg.Root);continue;}
    if(r.kind=="create-root"&&tr.childCount>0){kept++;sb.AppendLine("  KEPT "+r.key+": "+tr.childCount+" child object(s) are still inside");continue;}
    Object.DestroyImmediate(tr.gameObject);r.reverted=true;marked.Add(r);sb.AppendLine("  removed "+r.key);
   }
   WriteLedger("scene_"+t.Alias,ledger);EditorSceneManager.MarkSceneDirty(scene);
   if(!EditorSceneManager.SaveScene(scene))
   {
    foreach(var r in marked)r.reverted=false;WriteLedger("scene_"+t.Alias,ledger);EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);
    return "FAILED SaveScene returned false for "+t.Scene+" (scene reloaded from disk, nothing saved; the "+marked.Count+" ledger row(s) of this call are live again - run scene-revert again; backup "+copy+")";
   }
   return sb.Append("  saved; kept "+kept+"; scene sha "+Short(Harness303.Sha(Harness303.Abs(t.Scene)))+"; backup "+copy+" (a NavMesh baked WITH the two prop bays is stale now: bake again)").ToString();
  }

  // ---------- check (read-only): AC-G1..G13 ----------

  static string Check(string arg)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,false);var scene=Open(t);var content=Content(t,scene);Physics.SyncTransforms();int fail=0,pass=0;
   var sb=new StringBuilder("OpenGuide308 check "+t.Alias+" (data "+Short(cfg.Sha)+")\n");
   void C(bool ok,string what){if(ok)pass++;else fail++;sb.AppendLine("  ["+(ok?"PASS":"FAIL")+"] "+what);}
   var root=Root(cfg,scene);var want=Arr(cfg.J,"rows").Select(r=>Str(r,"id")).ToList();
   var have=root==null?new List<string>():root.Cast<Transform>().Select(o=>o.name).ToList();
   C(root!=null&&want.All(have.Contains)&&have.Count==want.Count,"AC-G1 "+cfg.Root+" holds exactly the data rows ("+have.Count+" of "+want.Count+")"+(root==null?" - the root is absent: run scene-apply":""));
   if(root!=null)
   {
    C(scene.GetRootGameObjects().Count(g=>g.name==cfg.Root)==1&&!PostLedger308.UnderProtectedTree(root),"AC-G1 one root named "+cfg.Root+", outside the protected trees");
    C(root.GetComponentsInChildren<Light>(true).Length==0,"AC-G5 Light components under the root: "+root.GetComponentsInChildren<Light>(true).Length+" (no new light)");
    C(root.GetComponentsInChildren<NavMeshAgent>(true).Length==0&&root.GetComponentsInChildren<NavMeshObstacle>(true).Length==0,"AC-G5 NavMeshAgent / NavMeshObstacle under the root: 0");
    var rends=root.GetComponentsInChildren<Renderer>(true);
    C(rends.All(r=>r.sharedMaterials.All(m=>m!=null&&!m.IsKeywordEnabled("_EMISSION"))),"AC-G5 no material under the root has _EMISSION ("+rends.Length+" renderer(s))");
    var solidNames=new List<string>();int colliders=0;
    foreach(var r in Arr(cfg.J,"rows"))
    {
     var obj=root.Find(Str(r,"id"));if(obj==null){C(false,"AC-G2 "+Str(r,"id")+" is in the scene");continue;}
     Item it;try{it=Build(cfg,r);}catch(Refuse e){C(false,"AC-G2 "+Str(r,"id")+": "+e.Message);continue;}
     C(Same(cfg,obj,it),"AC-G2 "+it.Id+" stands where the data"+(it.Baked?"":" and the physical ground")+" put it ("+V(obj.position)+"), with the data's mesh, materials and "+Boxes(it).Length+" collider box(es)");
     var mf=obj.GetComponent<MeshFilter>();C(mf!=null&&mf.sharedMesh!=null&&EditorUtility.IsPersistent(mf.sharedMesh)&&AssetDatabase.GetAssetPath(mf.sharedMesh)==MeshAsset(cfg,Str(r,"mesh")),"AC-G3 "+it.Id+": its mesh is the asset "+MeshAsset(cfg,Str(r,"mesh")));
     if(it.Baked)C(it.ProbeGap<=cfg.R("baked_y_tol_m"),"AC-G8 "+it.Id+": the physical ground under its probes is within "+F(it.ProbeGap,"F3")+" m of the height field it was baked to (<= "+F(cfg.R("baked_y_tol_m"))+")");
     var cols=obj.GetComponentsInChildren<BoxCollider>(true);colliders+=cols.Length;
     if(!it.Solid)continue;
     solidNames.Add(it.Id);
     C(WallFault(cfg,it)==null&&it.Slope<=Num(r,"max_slope_deg")&&it.CornerGap<=CornerMax(cfg,r),"AC-G6 "+it.Id+": slope "+F(it.Slope,"F1")+"°, collider top "+F(it.WallMin)+" m or more over the ground at every corner (>= "+F(cfg.R("wall_min_m"))+": no lip), bottom "+F(it.UnderMin)+" m or more under it (>= "+F(cfg.R("wall_under_m"))+": no gap), corners within "+F(it.CornerGap)+" m");
     Corridor(cfg,content,it,out float line,out float pad,out string near);
     C(line>=cfg.R("corridor_clear_m"),"AC-G7 "+it.Id+": collider corners "+F(line)+" m from the content path centre lines >= "+F(cfg.R("corridor_clear_m"),"F1"));
     C(pad>=cfg.R("point_collider_pad_m"),"AC-G7 "+it.Id+": nearest content point "+near+" - its prompt disc is "+F(pad)+" m from a collider corner (>= "+F(cfg.R("point_collider_pad_m"),"F1")+")");
    }
    C(root.GetComponentsInChildren<Collider>(true).Length==colliders,"AC-G4 every collider under the root is a data box ("+colliders+" BoxCollider, "+(root.GetComponentsInChildren<Collider>(true).Length-colliders)+" other)");
    sb.AppendLine("  colliders that the NavMesh bake must see: "+(solidNames.Count==0?"none":string.Join(", ",solidNames)));
   }
   foreach(var h in Arr(cfg.J,"hides"))
   {
    var tr=Unique(scene,Str(h,"path"),out string why);
    if(tr==null){sb.AppendLine("  note "+Str(h,"id")+": "+Str(h,"path")+" is not in this scene ("+why+")");continue;}
    C(!tr.gameObject.activeSelf,"AC-G9 "+Str(h,"path")+" is switched off (the old line onto the spoil heap is out of the picture)");
   }
   var ex=cfg.J["expect"];float tol=cfg.R("expect_tol_m");
   {
    var a=Req(ex,"mainpath_start") as JArray;var want0=new Vector2(a[0].Value<float>(),a[1].Value<float>());var mp=content.MainPath??Array.Empty<Vector3>();
    float d=mp.Length>0?Vector2.Distance(new Vector2(mp[0].x,mp[0].z),want0):float.PositiveInfinity;
    C(d<=tol,"AC-G10 MainPath starts "+(mp.Length>0?F(d)+" m from the T ("+F(want0.x,"F1")+", "+F(want0.y,"F1")+")":"nowhere (empty)")+" (<= "+F(tol,"F1")+": the old cut road is no leg of the content path; else run Content308 paths-regen:"+t.Alias+")");
   }
   {
    var b=Req(ex,"beast");string id=Str(b,"id");var e=Array.Find(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>(),q=>q!=null&&q.Id==id);
    if(e==null)sb.AppendLine("  note encounter "+id+" is not in this content");
    else
    {
     float d=Vector2.Distance(new Vector2(e.Feet.x,e.Feet.z),new Vector2(Num(b,"x"),Num(b,"z")));
     C(d<=cfg.R("beast_tol_m"),"AC-G11 "+id+" feet "+V(e.Feet)+" are "+F(d)+" m from the data place (<= "+F(cfg.R("beast_tol_m"))+"; Wood308's existing-place rule is the same 0.5 m)");
     C(NavMesh.SamplePosition(e.Feet,out var hit,1f,NavMesh.AllAreas),"AC-G11 "+id+": NavMesh within 1 m of its feet (the session refuses to start an actor off the NavMesh)");
     var actors=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PrologueEncounter>(true)).Where(x=>x.Id==id).ToArray();
     C(actors.Length==1&&Vector2.Distance(new Vector2(actors[0].transform.position.x,actors[0].transform.position.z),new Vector2(e.Feet.x,e.Feet.z))<=cfg.R("beast_tol_m"),"AC-G11 "+id+": one scene actor, standing over the content feet ("+actors.Length+" actor(s)"+(actors.Length==1?" at "+V(actors[0].transform.position):"")+"; else run Content308 scene-apply:"+t.Alias+")");
    }
   }
   {
    var s=Req(ex,"stele_point");string id=Str(s,"id");var p=Array.Find(content.Points??Array.Empty<PrologueContentSO.Point>(),q=>q!=null&&q.Id==id);
    if(p==null)sb.AppendLine("  note point "+id+" is not in this content");
    else
    {
     float d=Vector2.Distance(new Vector2(p.Position.x,p.Position.z),new Vector2(Num(s,"x"),Num(s,"z")));
     C(d<=tol,"AC-G12 point "+id+" "+V(p.Position)+" is "+F(d)+" m from the data place (<= "+F(tol,"F1")+")");
     var sp=Req(ex,"stele_prop");var tr=Unique(scene,Str(sp,"path"),out string why);
     C(tr!=null&&Vector2.Distance(new Vector2(tr.position.x,tr.position.z),new Vector2(Num(sp,"x"),Num(sp,"z")))<=tol&&Mathf.Abs(Mathf.DeltaAngle(tr.eulerAngles.y,Num(sp,"yaw")))<=1f,
      "AC-G12 "+Str(sp,"path")+(tr==null?" is not in the scene ("+why+")":" stands at "+V(tr.position)+" yaw "+F(tr.eulerAngles.y,"F1"))+" (data "+F(Num(sp,"x"),"F1")+", "+F(Num(sp,"z"),"F1")+" yaw "+F(Num(sp,"yaw"),"F0")+"; else run Content308 scene-apply:"+t.Alias+")");
    }
   }
   {
    int left=TreeAsks(cfg,out var sheet,out _).Count(a=>Index(sheet,a,Num(cfg.J["trees"],"tolerance_m"))>=0);
    C(left==0,"AC-G13 the "+Arr(cfg.J["trees"],"rows").Count()+" sheet tree(s) of the sight line are out of "+Path.GetFileName(Str(cfg.J["trees"],"sheet"))+" ("+left+" still there; else trees-apply)");
   }
   if(scene.isDirty)sb.AppendLine("  WARN the scene is dirty after a read-only pass (bug): reload it");
   return sb.Append("  "+(fail==0?"no FAIL":"FAIL "+fail)+" (PASS "+pass+")").ToString();
  }

  // ---------- trees: named rows of one vegetation sheet ----------

  sealed class TreeAsk{public string Id,Proto;public Vector3 At;}
  [Serializable] sealed class TreeRow{public string id="",proto="",placementJson="";public Vector3 from;public int index=-1;public bool reverted;}
  [Serializable] sealed class TreeWrite{public string utc="",sheet="",backup="",shaBefore="",shaAfter="",dataSha="",status="";public List<TreeRow> rows=new List<TreeRow>();}
  [Serializable] sealed class TreeLedger{public string kind="openguide308-trees",created="";public List<TreeWrite> writes=new List<TreeWrite>();}
  static string TreeLedgerFile=>Path.Combine(OutDir,"ledger_openguide308_trees.json");
  static TreeLedger ReadTreeLedger()=>File.Exists(TreeLedgerFile)?JsonUtility.FromJson<TreeLedger>(File.ReadAllText(TreeLedgerFile)):new TreeLedger{created=DateTime.UtcNow.ToString("O")};
  static void WriteTreeLedger(TreeLedger l){Directory.CreateDirectory(OutDir);File.WriteAllText(TreeLedgerFile,JsonUtility.ToJson(l,true));}
  static List<TreeAsk> TreeAsks(Cfg cfg,out Dress308 sheet,out string path)
  {
   var d=Req(cfg.J,"trees");path=Str(d,"sheet");
   var audit=BuildingAudit308.LoadConfig(out string err);if(audit==null)throw new Refuse("BuildingAudit308 config (the protected-asset list): "+err);
   if(audit.ProtectedAsset(path)||Protected(path))throw new Refuse("protected sheet "+path);
   sheet=AssetDatabase.LoadAssetAtPath<Dress308>(path);if(sheet==null)throw new Refuse("no sheet "+path);
   return Arr(d,"rows").Select(r=>new TreeAsk{Id=Str(r,"id"),Proto=Str(r,"proto"),At=Vec(r,"at")}).ToList();
  }
  // index of the ONE placement that is this ask (-1 none, -2 more than one)
  static int Index(Dress308 sheet,TreeAsk a,float tol)
  {
   var fps=sheet.FixedPlacements??Array.Empty<Dress308.FixedPlacement>();int at=-1;
   for(int i=0;i<fps.Length;i++){var p=fps[i];if(p==null||(p.Id??"")!=a.Id||Vector3.Distance(p.Position,a.At)>tol)continue;if(at>=0)return -2;at=i;}
   return at;
  }
  static void Redraw(Dress308 sheet){foreach(var a in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(a.Sheet==sheet)a.Invalidate();}
  static string Trees(string mode)
  {
   var cfg=Load();float tol=Num(cfg.J["trees"],"tolerance_m");var asks=TreeAsks(cfg,out var sheet,out string path);string abs=Harness303.Abs(path);
   var sb=new StringBuilder("OpenGuide308 trees-"+mode+" (data "+Short(cfg.Sha)+"; sheet "+path+", "+(sheet.FixedPlacements?.Length??0)+" placements, sha "+Short(Harness303.Sha(abs))+")\n");
   if(EditorUtility.IsDirty(sheet))throw new Refuse(path+" has unsaved changes in memory (another session?). Nothing written");
   var ledger=ReadTreeLedger();
   if(mode=="revert")
   {
    var op=ledger.writes.LastOrDefault(w=>w!=null&&w.rows.Any(r=>!r.reverted));
    if(op==null)return sb.Append("  변경 없음 (no live tree row in the ledger)").ToString();
    var fps=(sheet.FixedPlacements??Array.Empty<Dress308.FixedPlacement>()).ToList();int back=0,there=0;string copy=Backup(path,"trees-revert-");
    foreach(var r in op.rows.Where(r=>!r.reverted).OrderBy(r=>r.index))
    {
     if(fps.Any(p=>p!=null&&(p.Id??"")==r.id&&Vector3.Distance(p.Position,r.from)<=tol)){there++;r.reverted=true;sb.AppendLine("  "+r.id+": already back");continue;}
     var p0=JsonUtility.FromJson<Dress308.FixedPlacement>(r.placementJson);if(p0==null){sb.AppendLine("  KEPT "+r.id+": no placement json in the ledger");continue;}
     fps.Insert(Mathf.Clamp(r.index,0,fps.Count),p0);r.reverted=true;back++;sb.AppendLine("  re-inserted "+r.id+" ("+r.proto+") at "+V(r.from));
    }
    WriteTreeLedger(ledger);sheet.FixedPlacements=fps.ToArray();EditorUtility.SetDirty(sheet);AssetDatabase.SaveAssetIfDirty(sheet);Redraw(sheet);
    string after=Harness303.Sha(abs);
    return sb.Append("  re-inserted "+back+", already back "+there+"; sheet sha "+Short(after)+(after==op.shaBefore?" (= the sha before the apply)":" (differs from the sha before the apply "+Short(op.shaBefore)+": other edits were made since)")+"; backup "+copy+"; ledger "+TreeLedgerFile).ToString();
   }
   var drop=new List<(TreeAsk a,int i)>();int gone=0;
   foreach(var a in asks)
   {
    int i=Index(sheet,a,tol);
    if(i==-2)throw new Refuse("more than one placement matches "+a.Id+" at "+V(a.At)+" in "+path+". Nothing written");
    if(i<0){gone++;sb.AppendLine("  already "+a.Id+" ("+a.Proto+") at "+V(a.At));continue;}
    var p=sheet.FixedPlacements[i];
    if((p.PrototypeId??"")!=a.Proto)throw new Refuse(a.Id+": the sheet row's prototype is '"+p.PrototypeId+"', the data says '"+a.Proto+"' - not the tree this row was written for. Nothing written");
    drop.Add((a,i));sb.AppendLine("  remove  "+a.Id+" ("+a.Proto+") at "+V(p.Position)+" [index "+i+"]");
   }
   if(drop.Count==0)return sb.Append("  변경 없음 ("+gone+" row(s) already gone; no sheet saved, no ledger row)").ToString();
   if(mode=="plan")return sb.Append("  plan only: nothing written (would remove "+drop.Count+", already gone "+gone+"; the sheet is shared by the three scenes: ONE apply)").ToString();
   var write=new TreeWrite{utc=DateTime.UtcNow.ToString("O"),sheet=path,backup=Backup(path,"trees-"),shaBefore=Harness303.Sha(abs),dataSha=cfg.Sha,status="applying"};
   foreach(var (a,i) in drop)write.rows.Add(new TreeRow{id=a.Id,proto=a.Proto,from=sheet.FixedPlacements[i].Position,index=i,placementJson=JsonUtility.ToJson(sheet.FixedPlacements[i])});
   ledger.writes.Add(write);WriteTreeLedger(ledger);   // the journal (index + placement json of every row) is on disk before the sheet is saved
   var kill=new HashSet<int>(drop.Select(d=>d.i));var keep=new List<Dress308.FixedPlacement>();
   for(int i=0;i<sheet.FixedPlacements.Length;i++)if(!kill.Contains(i))keep.Add(sheet.FixedPlacements[i]);
   sheet.FixedPlacements=keep.ToArray();EditorUtility.SetDirty(sheet);AssetDatabase.SaveAssetIfDirty(sheet);Redraw(sheet);
   write.shaAfter=Harness303.Sha(abs);write.status="applied";WriteTreeLedger(ledger);
   return sb.Append("  saved: removed "+drop.Count+", already gone "+gone+"; sheet sha "+Short(write.shaBefore)+" -> "+Short(write.shaAfter)+"; backup "+write.backup+"; ledger "+TreeLedgerFile+" (a sheet re-bake brings the rows back: run trees-apply again)").ToString();
  }

  // ---------- status ----------

  static string Status()
  {
   var sb=new StringBuilder("OpenGuide308 status (play="+EditorApplication.isPlaying+")\n");
   Cfg cfg;try{cfg=Load();}catch(Refuse r){return sb.Append("  "+r.Message).ToString();}
   int rows=Arr(cfg.J,"rows").Count(),solid=Arr(cfg.J,"rows").Count(r=>Flag(r,"solid"));
   sb.AppendLine("  data "+DataFile+" sha "+Short(cfg.Sha)+" version "+(Opt(cfg.J,"version")??"?")+", group "+(Opt(cfg.J,"group")??"")+": "+rows+" row(s), "+solid+" with colliders (bake), "+Arr(cfg.J,"hides").Count()+" hide row(s), "+Arr(cfg.J["trees"],"rows").Count()+" tree row(s)");
   int ok=Arr(cfg.J,"meshes").Count(m=>MeshMatches(cfg,AssetDatabase.LoadAssetAtPath<Mesh>(MeshAsset(cfg,Str(m,"name"))),m));
   sb.AppendLine("  mesh assets in place "+ok+"/"+Arr(cfg.J,"meshes").Count()+" under "+cfg.AssetDir+"; mesh json "+Arr(cfg.J,"meshes").Count(m=>JsonState(cfg,m)==null)+"/"+Arr(cfg.J,"meshes").Count()+" as the data says; live asset ledger rows "+Live(ReadLedger("assets")).Count());
   foreach(var t in cfg.Targets)sb.AppendLine("  "+t.Alias+": live scene ledger rows "+Live(ReadLedger("scene_"+t.Alias)).Count());
   try{float tol=Num(cfg.J["trees"],"tolerance_m");var asks=TreeAsks(cfg,out var sheet,out string path);sb.AppendLine("  trees: "+asks.Count(a=>Index(sheet,a,tol)>=0)+" of "+asks.Count+" still in "+Path.GetFileName(path)+"; live tree ledger rows "+ReadTreeLedger().writes.Sum(w=>w.rows.Count(r=>!r.reverted)));}
   catch(Refuse r){sb.AppendLine("  trees: "+r.Message);}
   var active=SceneManager.GetActiveScene();var root=Root(cfg,active);
   return sb.Append("  active scene "+active.path+(active.isDirty?" (dirty)":"")+": "+cfg.Root+" "+(root==null?"absent":"present, "+root.childCount+" object(s)")).ToString();
  }
 }
}
