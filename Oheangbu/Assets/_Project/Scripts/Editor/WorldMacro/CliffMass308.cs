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

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 cliff mass (group G18_cliffmass, Stage308_cliffmass) - the N2 rock mass on a slice of the boundary (user decision 2026-10-06).
 // Every value [TEST]. The data (Art/World/Compact/Rebuild/CliffBoundary308/cliffmass308.json) and its mesh json are made offline by
 // Tools/Unity/Stage308_cliffmass/Offline/cm_build.py. This tool owns ONE root (CliffMass308) and nothing else. Per panel:
 //   <panel>            LODGroup, at the data pivot, identity rotation (the meshes are baked to the height field in world axes)
 //   <panel>/LOD0|LOD1  MeshRenderer with the ONE existing granite material (world triplanar: position + normal only)
 //   <panel>/Col        ONE MeshCollider (never convex, never a trigger). Its mesh is a SUBSET OF THE LOD0 TRIANGLES:
 //                      hard rule "no invisible wall" - a collider never stands outside drawn rock (check AC-K6 compares the triangles).
 // The protected terrain is not touched; nothing is hidden or removed; no light, no emission, no text, no camera, no HUD, no content row.
 // Queue-safe: Run(string) never opens a dialog; refusals come back as "REFUSED ...". Ledgers are NEW files of this tool
 // (Art/World/Compact/Rebuild/CliffBoundary308/Out/CliffMass308/ledger_cliffmass308_*.json), written BEFORE the asset / scene is saved, with a byte
 // backup of the scene; a second identical apply reports "변경 없음"; a revert takes out exactly what the ledger made. Only the target
 // scene / the created Mesh assets are saved (never AssetDatabase.SaveAssets).
 //   status
 //   assets-plan | assets-apply | assets-revert      the Mesh assets under asset_dir (once for the three scenes)
 //   plan:<scene> | scene-apply:<scene> | scene-revert:<scene>
 //   check:<scene>                                   AC-K1..K9 on the saved scene; read-only
 // <scene> = arch296 | folk298 | main. Order: RUN_ORDER_cliffmass.md. The panels carry colliders: they must be in all three scenes
 // BEFORE the one NavMesh bake.
 public static class CliffMass308
 {
  const string Usage="status | assets-plan | assets-apply | assets-revert | plan:<scene> | scene-apply:<scene> | scene-revert:<scene> | check:<scene>";
  static string DataFile=>Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","CliffBoundary308","cliffmass308.json");
  static string OutDir=>Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","CliffBoundary308","Out","CliffMass308");
  static readonly string[] Lods={"lod0","lod1"};
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
    string a;
    if((a=Arg(c,"plan:"))!=null)return Apply(a,true);
    if((a=Arg(c,"scene-apply:"))!=null)return Apply(a,false);
    if((a=Arg(c,"scene-revert:"))!=null)return Revert(a);
    if((a=Arg(c,"check:"))!=null)return Check(a);
   }
   catch(Refuse r){return "REFUSED "+r.Message;}
   catch(Exception e){return "FAILED "+e;}
   return "REFUSED unknown CliffMass308 command '"+c+"' ("+Usage+")";
  }
  static string Arg(string c,string head)=>c.StartsWith(head,StringComparison.Ordinal)?c.Substring(head.Length).Trim():null;

  // ---------- data ----------

  sealed class Target{public string Alias,Scene,Content;}
  sealed class Cfg
  {
   public JObject J;public string Sha;public Target[] Targets;public Material Mat;
   public JToken Rules=>J["rules"];public float R(string k)=>Num(Rules,k);public string Root=>Str(J,"root");public string AssetDir=>Str(J,"asset_dir").TrimEnd('/');
   public string MeshDir=>Path.Combine(Harness303.RepoRoot,Str(J,"mesh_dir").Replace('/',Path.DirectorySeparatorChar));
   public IEnumerable<JToken> Panels=>Arr(J,"panels");
  }
  static JToken Req(JToken o,string key){var t=o?[key];if(t==null||t.Type==JTokenType.Null)throw new Refuse("cliffmass308.json: missing '"+key+"' in "+(o==null?"(null)":string.IsNullOrEmpty(o.Path)?"(root)":o.Path));return t;}
  static float Num(JToken o,string key)=>Req(o,key).Value<float>();
  static string Str(JToken o,string key)=>Req(o,key).Value<string>();
  static string Opt(JToken o,string key){var t=o?[key];return t==null||t.Type==JTokenType.Null?null:t.Value<string>();}
  static IEnumerable<JToken> Arr(JToken o,string key){var t=o?[key] as JArray;return t??(IEnumerable<JToken>)Array.Empty<JToken>();}
  static Vector3 Vec(JToken o,string key){var a=Req(o,key) as JArray;if(a==null||a.Count<3)throw new Refuse("cliffmass308.json: "+key+" needs 3 numbers");return new Vector3(a[0].Value<float>(),a[1].Value<float>(),a[2].Value<float>());}
  static string F(float v,string f="F2")=>v.ToString(f,CultureInfo.InvariantCulture);
  static string V(Vector3 v)=>Harness303.V(v);
  static string Short(string sha)=>string.IsNullOrEmpty(sha)?"-":sha.Substring(0,Math.Min(12,sha.Length)).ToLowerInvariant();
  static bool Protected(string path)=>Harness303.IsProtected(path)||PostLedger308.IsProtectedPath(path);

  static Cfg Load()
  {
   string f=DataFile;if(!File.Exists(f))throw new Refuse("data missing: "+f+" (python Tools/Unity/Stage308_cliffmass/cliffmass_copy.py --apply)");
   JObject j;try{j=JObject.Parse(File.ReadAllText(f));}catch(Exception e){throw new Refuse("data does not parse: "+f+" ("+e.Message+")");}
   var cfg=new Cfg{J=j,Sha=Harness303.Sha(f)};
   foreach(string key in new[]{"rules","panels","material","root","asset_dir","mesh_dir","slot","lod_screen","shadows","targets"})Req(j,key);
   foreach(string key in new[]{"pose_tol_m","mesh_bounds_tol_m","ground_tol_m","ray_tol_m","ray_len_m","content_clear_m"})Num(cfg.Rules,key);   // a missing rule refuses here, not half way through a pass
   cfg.Targets=Arr(j,"targets").Select(t=>new Target{Alias=Str(t,"alias"),Scene=Str(t,"scene"),Content=Str(t,"content")}).ToArray();
   if(cfg.Targets.Length==0)throw new Refuse("cliffmass308.json: targets[] is empty");
   foreach(var p in cfg.Targets.SelectMany(t=>new[]{t.Scene,t.Content}).Concat(new[]{cfg.AssetDir+"/"}))if(Protected(p))throw new Refuse("protected path in data: "+p);
   foreach(string tree in new[]{"Reworld292","Watershed295","MountainTrail285"})if(cfg.Root.StartsWith(tree,StringComparison.Ordinal))throw new Refuse("root name "+cfg.Root+" is a protected tree name");
   if(!cfg.AssetDir.StartsWith("Assets/_Project/",StringComparison.Ordinal))throw new Refuse("asset_dir must lie under Assets/_Project/: "+cfg.AssetDir);
   var ls=Req(j,"lod_screen") as JArray;if(ls==null||ls.Count!=Lods.Length||!(ls[0].Value<float>()>ls[1].Value<float>())||ls[1].Value<float>()<0f)throw new Refuse("cliffmass308.json: lod_screen needs "+Lods.Length+" falling numbers");
   // the one existing material: the guid must resolve to the path the data names, and it may not glow ("오염은 빛나지 않는다")
   string path=Str(j["material"],"path"),guid=Str(j["material"],"guid");
   if(Protected(path))throw new Refuse("the material lives under a protected tree: "+path);
   if(AssetDatabase.GUIDToAssetPath(guid)!=path)throw new Refuse("material guid "+guid+" resolves to '"+AssetDatabase.GUIDToAssetPath(guid)+"', the data says "+path);
   cfg.Mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(cfg.Mat==null)throw new Refuse("the material does not load: "+path);
   if(cfg.Mat.IsKeywordEnabled("_EMISSION"))throw new Refuse("material "+path+" has _EMISSION: this stage adds no glow");
   var ids=new HashSet<string>(StringComparer.Ordinal);
   foreach(var p in cfg.Panels)
   {
    string id=Str(p,"id");if(!ids.Add(id))throw new Refuse("panel id '"+id+"' is used twice");
    if(id.IndexOfAny(new[]{'/','\\',' '})>=0)throw new Refuse("bad panel id '"+id+"'");
    if(p["light"]!=null||p["text"]!=null||p["prompt"]!=null)throw new Refuse("panel "+id+" carries a light / text / prompt key: this tool makes none");
    Vec(p,"pos");foreach(string m in new[]{"lod0","lod1","col"}){var d=Req(p,m);Str(d,"json");Str(d,"sha256");Num(d,"verts");Num(d,"tris");Vec(d,"lo");Vec(d,"hi");}
    if(!Arr(p,"ground").Any())throw new Refuse("panel "+id+": ground[] is empty (the mesh is baked to the height field: it needs probes)");
   }
   if(ids.Count==0)throw new Refuse("cliffmass308.json: panels[] is empty");
   return cfg;
  }
  static Target TargetOf(Cfg cfg,string arg)
  {
   arg=(arg??"").Trim().Replace('\\','/');
   var t=cfg.Targets.FirstOrDefault(x=>string.Equals(x.Alias,arg,StringComparison.OrdinalIgnoreCase)||x.Scene==arg);
   if(t==null)throw new Refuse("not a cliffmass308 target: '"+arg+"' (allowed: "+string.Join(", ",cfg.Targets.Select(x=>x.Alias))+")");
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
  [Serializable] sealed class Ledger{public string kind="cliffmass308",group="",target="",created="";public List<Write> writes=new List<Write>();}
  static string LedgerFile(string key)=>Path.Combine(OutDir,"ledger_cliffmass308_"+key+".json");
  static Ledger ReadLedger(string key){var f=LedgerFile(key);return File.Exists(f)?JsonUtility.FromJson<Ledger>(File.ReadAllText(f)):null;}
  static void WriteLedger(string key,Ledger l){Directory.CreateDirectory(OutDir);File.WriteAllText(LedgerFile(key),JsonUtility.ToJson(l,true));}
  static IEnumerable<Row> Live(Ledger l)=>l==null?Enumerable.Empty<Row>():l.writes.Where(w=>w!=null&&w.rows!=null).SelectMany(w=>w.rows).Where(r=>r!=null&&!r.reverted);
  static string Backup(string assetPath)
  {
   string dir=Path.Combine(OutDir,"Backups","cliffmass308-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff",CultureInfo.InvariantCulture));Directory.CreateDirectory(dir);
   string copy=Path.Combine(dir,Path.GetFileName(assetPath));File.Copy(Harness303.Abs(assetPath),copy,true);return copy;
  }

  // ---------- Mesh assets (shared by the three scenes) ----------

  [Serializable] sealed class KitSub{public string m;public int[] t;}
  [Serializable] sealed class KitMesh{public string name;public float[] v,n;public KitSub[] sub;}
  static string MeshName(JToken d)=>Path.GetFileNameWithoutExtension(Str(d,"json"));
  static string MeshAsset(Cfg cfg,JToken d)=>cfg.AssetDir+"/"+MeshName(d)+".asset";
  static string MeshJson(Cfg cfg,JToken d)=>Path.Combine(cfg.MeshDir,Str(d,"json"));
  static IEnumerable<JToken> MeshRows(Cfg cfg)=>cfg.Panels.SelectMany(p=>new[]{p["lod0"],p["lod1"],p["col"]});
  static string JsonState(Cfg cfg,JToken d)
  {
   string f=MeshJson(cfg,d);if(!File.Exists(f))return "json missing: "+f;
   return string.Equals(Harness303.Sha(f),Str(d,"sha256"),StringComparison.OrdinalIgnoreCase)?null:"json sha "+Short(Harness303.Sha(f))+" is not the data's "+Short(Str(d,"sha256"));
  }
  static KitMesh ReadKit(Cfg cfg,JToken d)
  {
   var k=JsonUtility.FromJson<KitMesh>(File.ReadAllText(MeshJson(cfg,d)));int count=k==null||k.v==null?0:k.v.Length/3;
   if(count<3||count!=Mathf.RoundToInt(Num(d,"verts"))||k.sub==null||k.sub.Length!=1||k.sub[0].t==null||k.sub[0].t.Length!=3*Mathf.RoundToInt(Num(d,"tris")))
    throw new Refuse("mesh json "+Str(d,"json")+" does not hold "+F(Num(d,"verts"),"F0")+" vertices / "+F(Num(d,"tris"),"F0")+" triangles in one submesh");
   return k;
  }
  // position + normal only (the granite shader is a world triplanar: no UV, no tangent). A collider mesh carries positions only
  static Mesh BuildMesh(Cfg cfg,JToken d)
  {
   var k=ReadKit(cfg,d);int count=k.v.Length/3;var v=new Vector3[count];
   for(int i=0;i<count;i++)v[i]=new Vector3(k.v[3*i],k.v[3*i+1],k.v[3*i+2]);
   var mesh=new Mesh{name=MeshName(d),indexFormat=count>65535?IndexFormat.UInt32:IndexFormat.UInt16};mesh.SetVertices(v);
   if(k.n!=null&&k.n.Length==k.v.Length){var n=new Vector3[count];for(int i=0;i<count;i++)n[i]=new Vector3(k.n[3*i],k.n[3*i+1],k.n[3*i+2]);mesh.SetNormals(n);}
   mesh.subMeshCount=1;mesh.SetTriangles(k.sub[0].t,0,false);mesh.RecalculateBounds();
   return mesh;
  }
  static bool MeshMatches(Cfg cfg,Mesh a,JToken d)
  {
   if(a==null||a.vertexCount!=Mathf.RoundToInt(Num(d,"verts"))||a.subMeshCount!=1||a.GetIndexCount(0)!=3u*(uint)Mathf.RoundToInt(Num(d,"tris")))return false;
   Vector3 lo=Vec(d,"lo"),hi=Vec(d,"hi");float tol=cfg.R("mesh_bounds_tol_m");return (a.bounds.min-lo).magnitude<=tol&&(a.bounds.max-hi).magnitude<=tol;
  }
  static bool AnySceneLive(Cfg cfg,out string who)
  {
   who=string.Join(", ",cfg.Targets.Where(t=>Live(ReadLedger("scene_"+t.Alias)).Any(r=>r.kind=="create")).Select(t=>t.Alias));return who.Length>0;
  }
  static string Assets(string mode)
  {
   var cfg=Load();var sb=new StringBuilder("CliffMass308 assets-"+mode+" (data "+Short(cfg.Sha)+" "+(Opt(cfg.J,"version")??"?")+")\n");
   var ledger=ReadLedger("assets")??new Ledger{group=Opt(cfg.J,"group")??"",target=cfg.AssetDir,created=DateTime.UtcNow.ToString("O")};
   if(mode=="revert")
   {
    if(AnySceneLive(cfg,out string who))throw new Refuse("scene ledger rows are still live ("+who+"): scene-revert those scenes first - their objects use these assets");
    var live=Live(ledger).ToList();if(live.Count==0)return sb.Append("  변경 없음 (no live asset row)").ToString();
    int gone=0,kept=0;
    foreach(var r in Enumerable.Reverse(live))
    {
     if(!r.key.StartsWith(cfg.AssetDir,StringComparison.Ordinal)||Protected(r.key+"/")){kept++;sb.AppendLine("  KEPT "+r.key+": outside "+cfg.AssetDir);continue;}
     if(r.kind=="mesh"){if(AssetDatabase.LoadMainAssetAtPath(r.key)==null)gone++;else if(AssetDatabase.DeleteAsset(r.key))gone++;else{kept++;sb.AppendLine("  KEPT "+r.key+": DeleteAsset returned false");continue;}}
     else if(r.kind=="folder"){if(!AssetDatabase.IsValidFolder(r.key))sb.AppendLine("  "+r.key+": already gone");else if(Directory.EnumerateFileSystemEntries(Harness303.Abs(r.key)).Any(e=>!e.EndsWith(".meta",StringComparison.Ordinal))){kept++;sb.AppendLine("  KEPT folder "+r.key+": it still holds assets");continue;}else if(AssetDatabase.DeleteAsset(r.key))sb.AppendLine("  deleted folder "+r.key);else{kept++;sb.AppendLine("  KEPT folder "+r.key);continue;}}
     r.reverted=true;
    }
    WriteLedger("assets",ledger);return sb.Append("  deleted "+gone+" asset(s), kept "+kept+"; ledger "+LedgerFile("assets")).ToString();
   }
   var todo=new List<JToken>();int same=0;
   foreach(var d in MeshRows(cfg))
   {
    string path=MeshAsset(cfg,d),bad=JsonState(cfg,d);
    if(bad!=null)throw new Refuse("mesh "+MeshName(d)+": "+bad+" (cliffmass_copy.py copies the json files; nothing written)");
    var have=AssetDatabase.LoadAssetAtPath<Mesh>(path);
    if(have==null)todo.Add(d);
    else if(MeshMatches(cfg,have,d))same++;
    else throw new Refuse(path+" exists and is not the mesh the data describes - assets-revert first (after scene-revert). Nothing written");
   }
   int tris=todo.Sum(d=>Mathf.RoundToInt(Num(d,"tris")));
   sb.AppendLine("  create "+todo.Count+" mesh asset(s) ("+tris+" triangles) under "+cfg.AssetDir+"; already in place "+same);
   if(todo.Count==0)return sb.Append("  변경 없음 (every asset is in place)").ToString();
   if(mode=="plan")return sb.Append("  plan only: nothing written").ToString();
   var write=new Write{utc=DateTime.UtcNow.ToString("O"),target=cfg.AssetDir,dataSha=cfg.Sha};
   string acc="";foreach(string part in cfg.AssetDir.Split('/'))
   {
    string parent=acc;acc=acc.Length==0?part:acc+"/"+part;if(AssetDatabase.IsValidFolder(acc))continue;
    write.rows.Add(new Row{kind="folder",key=acc,detail="folder"});if(!ledger.writes.Contains(write))ledger.writes.Add(write);WriteLedger("assets",ledger);
    if(string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent,part)))throw new Refuse("could not create folder "+acc);
   }
   // every row is journalled before its file exists (one ledger write for the whole batch, then the assets one by one)
   foreach(var d in todo)write.rows.Add(new Row{kind="mesh",key=MeshAsset(cfg,d),detail=MeshName(d)+" json "+Short(Str(d,"sha256"))});
   if(!ledger.writes.Contains(write))ledger.writes.Add(write);WriteLedger("assets",ledger);
   foreach(var d in todo){var mesh=BuildMesh(cfg,d);AssetDatabase.CreateAsset(mesh,MeshAsset(cfg,d));AssetDatabase.SaveAssetIfDirty(mesh);}
   return sb.Append("  created "+todo.Count+" mesh asset(s); ledger "+LedgerFile("assets")).ToString();
  }

  // ---------- ground / content ----------

  static bool Under(Transform t,HashSet<string> roots){for(var p=t;p!=null;p=p.parent)if(p.parent==null&&roots.Contains(p.name))return true;return false;}
  static bool Preview(Transform t){for(var p=t;p!=null;p=p.parent)if((p.gameObject.hideFlags&HideFlags.DontSave)!=0)return true;return false;}
  static bool TerrainLike(Collider c){if(c is TerrainCollider)return true;for(var t=c.transform;t!=null;t=t.parent){var n=t.name;if(n.Contains("Terrain")||n.Contains("Surface"))return true;}return false;}
  // the Content308 rule in short (as Guide2_308): the highest terrain surface under the XZ; own / listed roots and previews never count
  static HashSet<string> _ignore;
  static bool Ground(Cfg cfg,float x,float z,out Vector3 p)
  {
   p=default;if(_ignore==null)_ignore=new HashSet<string>(Arr(cfg.Rules,"ground_ignore_roots").Select(v=>v.Value<string>()));bool any=false;
   foreach(var h in Physics.RaycastAll(new Vector3(x,2000f,z),Vector3.down,4000f,~0,QueryTriggerInteraction.Ignore))
   {
    var c=h.collider;if(c==null||h.normal.y<=.5f||c is CharacterController||Preview(c.transform)||Under(c.transform,_ignore)||!TerrainLike(c))continue;
    if(!any||h.point.y>p.y){p=h.point;any=true;}
   }
   return any;
  }
  // the physical ground under the probes of a panel against the height field its meshes were baked to
  static float GroundGap(Cfg cfg,JToken p)
  {
   float gap=0f;
   foreach(var g in Arr(p,"ground"))
   {
    var a=g as JArray;if(a==null||a.Count<3)throw new Refuse(Str(p,"id")+": a ground probe needs [x, z, y]");
    if(!Ground(cfg,a[0].Value<float>(),a[1].Value<float>(),out var gp))throw new Refuse(Str(p,"id")+": no terrain under a ground probe ("+F(a[0].Value<float>(),"F1")+", "+F(a[1].Value<float>(),"F1")+") - is the scene loaded?");
    gap=Mathf.Max(gap,Mathf.Abs(gp.y-a[2].Value<float>()));
   }
   return gap;
  }
  // distance from the content (path points, prompt discs) to the panel's box on the ground plane
  static float ContentClear(WorldMacroPlaytestSO content,JToken p,out string near)
  {
   Vector3 pos=Vec(p,"pos"),lo=pos+Vec(p["lod0"],"lo"),hi=pos+Vec(p["lod0"],"hi");float best=float.PositiveInfinity;near="";
   float D(Vector3 q){float dx=Mathf.Max(Mathf.Max(lo.x-q.x,0f),q.x-hi.x),dz=Mathf.Max(Mathf.Max(lo.z-q.z,0f),q.z-hi.z);return Mathf.Sqrt(dx*dx+dz*dz);}
   foreach(var path in new[]{content.MainPath,content.BranchPath}){if(path==null)continue;foreach(var q in path){float d=D(q);if(d<best){best=d;near="path";}}}
   foreach(var pt in content.Points??Array.Empty<PrologueContentSO.Point>()){if(pt==null)continue;float d=D(pt.Position)-pt.Radius;if(d<best){best=d;near=pt.Id;}}
   return best;
  }

  // ---------- scene objects ----------

  static Transform Root(Cfg cfg,Scene s)=>s.GetRootGameObjects().Where(g=>g.name==cfg.Root&&!Preview(g.transform)).Select(g=>g.transform).FirstOrDefault();
  static bool Identity(Transform t)=>t.position.sqrMagnitude<1e-10f&&Quaternion.Angle(t.rotation,Quaternion.identity)<.01f&&(t.lossyScale-Vector3.one).sqrMagnitude<1e-10f;
  static Mesh LoadMesh(Cfg cfg,JToken d,bool must=true)
  {
   var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(MeshAsset(cfg,d));
   if(mesh==null||!MeshMatches(cfg,mesh,d)){if(!must)return null;throw new Refuse("mesh asset "+MeshAsset(cfg,d)+(mesh==null?" is missing":" is not the data's mesh")+" - run assets-apply first. Nothing written");}
   return mesh;
  }
  static ShadowCastingMode Shadow(Cfg cfg)=>Req(cfg.J,"shadows").Value<bool>()?ShadowCastingMode.On:ShadowCastingMode.Off;
  // is the object in the scene exactly what the data describes?
  static string Differs(Cfg cfg,Transform have,JToken p)
  {
   if((have.position-Vec(p,"pos")).magnitude>cfg.R("pose_tol_m")||Quaternion.Angle(have.rotation,Quaternion.identity)>.05f||(have.lossyScale-Vector3.one).magnitude>1e-4f)return "pose";
   var group=have.GetComponent<LODGroup>();if(group==null)return "no LODGroup";
   var lods=group.GetLODs();if(lods.Length!=Lods.Length)return lods.Length+" LOD levels";
   var screen=(JArray)cfg.J["lod_screen"];
   for(int i=0;i<Lods.Length;i++)
   {
    var c=have.Find(Lods[i].ToUpperInvariant());if(c==null)return "no child "+Lods[i].ToUpperInvariant();
    var mf=c.GetComponent<MeshFilter>();var mr=c.GetComponent<MeshRenderer>();
    if(mf==null||mr==null||mf.sharedMesh==null||AssetDatabase.GetAssetPath(mf.sharedMesh)!=MeshAsset(cfg,p[Lods[i]])||!MeshMatches(cfg,mf.sharedMesh,p[Lods[i]]))return Lods[i]+" mesh";
    if(mr.sharedMaterials.Length!=1||mr.sharedMaterials[0]!=cfg.Mat)return Lods[i]+" material";
    if(mr.shadowCastingMode!=Shadow(cfg))return Lods[i]+" shadow mode";
    if(lods[i].renderers==null||lods[i].renderers.Length!=1||lods[i].renderers[0]!=mr)return Lods[i]+" is not level "+i+" of the LODGroup";
    if(Mathf.Abs(lods[i].screenRelativeTransitionHeight-screen[i].Value<float>())>1e-4f)return Lods[i]+" screen height";
   }
   var cols=have.GetComponentsInChildren<Collider>(true);
   if(cols.Length!=1)return cols.Length+" colliders";
   var mc=cols[0] as MeshCollider;
   if(mc==null||mc.transform.parent!=have||mc.name!="Col"||mc.convex||mc.isTrigger||!mc.enabled||mc.sharedMesh==null||AssetDatabase.GetAssetPath(mc.sharedMesh)!=MeshAsset(cfg,p["col"])||!MeshMatches(cfg,mc.sharedMesh,p["col"]))return "collider";
   if(have.childCount!=Lods.Length+1)return have.childCount+" children";
   return null;
  }
  static void Make(Cfg cfg,Transform root,JToken p,Mesh[] lodMesh,Mesh col)
  {
   var obj=new GameObject(Str(p,"id"));obj.transform.SetParent(root,false);obj.transform.SetPositionAndRotation(Vec(p,"pos"),Quaternion.identity);obj.transform.localScale=Vector3.one;
   var screen=(JArray)cfg.J["lod_screen"];var levels=new LOD[Lods.Length];
   for(int i=0;i<Lods.Length;i++)
   {
    var c=new GameObject(Lods[i].ToUpperInvariant());c.transform.SetParent(obj.transform,false);
    c.AddComponent<MeshFilter>().sharedMesh=lodMesh[i];var mr=c.AddComponent<MeshRenderer>();mr.sharedMaterials=new[]{cfg.Mat};mr.shadowCastingMode=Shadow(cfg);mr.receiveShadows=true;c.isStatic=true;
    levels[i]=new LOD(screen[i].Value<float>(),new Renderer[]{mr});
   }
   var cg=new GameObject("Col");cg.transform.SetParent(obj.transform,false);var mc=cg.AddComponent<MeshCollider>();mc.convex=false;mc.isTrigger=false;mc.sharedMesh=col;cg.isStatic=true;
   var group=obj.AddComponent<LODGroup>();group.fadeMode=LODFadeMode.None;group.SetLODs(levels);group.RecalculateBounds();obj.isStatic=true;
  }
  // an existing scene object by its plain hierarchy path; every segment must name exactly one object (project memory "이름 경로 첫 형제 함정")
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

  // ---------- apply / revert ----------

  static string Apply(string arg,bool dry)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,!dry);var scene=Open(t);var content=Content(t,scene);Physics.SyncTransforms();_ignore=null;
   var sb=new StringBuilder("CliffMass308 "+(dry?"plan":"scene-apply")+" "+t.Alias+" (data "+Short(cfg.Sha)+" "+(Opt(cfg.J,"version")??"?")+", group "+(Opt(cfg.J,"group")??"")+")\n");
   if(scene.GetRootGameObjects().Count(g=>g.name==cfg.Root&&!Preview(g.transform))>1)throw new Refuse("more than one root named "+cfg.Root+" in "+scene.path);
   var root=Root(cfg,scene);
   if(root!=null&&(PostLedger308.UnderProtectedTree(root)||!Identity(root)))throw new Refuse("root "+cfg.Root+" is a protected tree name or not at the identity transform");
   var ledger=ReadLedger("scene_"+t.Alias)??new Ledger{group=Opt(cfg.J,"group")??"",target=t.Scene,created=DateTime.UtcNow.ToString("O")};
   var todo=new List<JToken>();int same=0,tris=0,colTris=0;float gapMax=0f,clearMin=float.PositiveInfinity;string clearNear="";
   foreach(var p in cfg.Panels)
   {
    string id=Str(p,"id");float gap=GroundGap(cfg,p);gapMax=Mathf.Max(gapMax,gap);
    if(gap>cfg.R("ground_tol_m"))throw new Refuse(id+": the physical ground is "+F(gap,"F3")+" m off the height field this mesh was baked to (> "+F(cfg.R("ground_tol_m"))+") - build the stage again on the current terrain. Nothing written");
    float clear=ContentClear(content,p,out string near);if(clear<clearMin){clearMin=clear;clearNear=near;}
    if(clear<cfg.R("content_clear_m"))throw new Refuse(id+": the panel box is "+F(clear)+" m from the content ("+near+") (< "+F(cfg.R("content_clear_m"),"F1")+") - a rock mass may not stand on the content path. Nothing written");
    foreach(string l in Lods)LoadMesh(cfg,p[l]);LoadMesh(cfg,p["col"]);
    var have=root!=null?root.Find(id):null;
    if(have!=null){string why=Differs(cfg,have,p);if(why==null){same++;continue;}throw new Refuse(cfg.Root+"/"+id+" is in "+scene.path+" and differs from the data ("+why+") - scene-revert:"+t.Alias+" first. Nothing written");}
    todo.Add(p);tris+=Mathf.RoundToInt(Num(p["lod0"],"tris"));colTris+=Mathf.RoundToInt(Num(p["col"],"tris"));
   }
   if(root!=null)foreach(Transform c in root)if(!cfg.Panels.Any(p=>Str(p,"id")==c.name))throw new Refuse(cfg.Root+"/"+c.name+" is in the scene and not in the data - scene-revert:"+t.Alias+" with the data that made it first. Nothing written");
   sb.AppendLine("  panels "+cfg.Panels.Count()+": to create "+todo.Count+" ("+tris+" LOD0 triangles, "+colTris+" collider triangles), in the scene as the data says "+same);
   sb.AppendLine("  physical ground under the probes within "+F(gapMax,"F3")+" m of the height field (<= "+F(cfg.R("ground_tol_m"))+"); nearest content "+F(clearMin,"F1")+" m ("+clearNear+", >= "+F(cfg.R("content_clear_m"),"F1")+")");
   if(todo.Count==0)return sb.Append("  변경 없음 (no change)").ToString();
   string bake="; "+todo.Count+" MeshCollider(s) = NEEDS THE NAVMESH BAKE";
   if(dry)return sb.Append("  plan only: nothing written (would create "+todo.Count+" panel(s)"+bake+")").ToString();
   string abs=Harness303.Abs(t.Scene);var write=new Write{utc=DateTime.UtcNow.ToString("O"),target=t.Scene,backup=Backup(t.Scene),shaBefore=Harness303.Sha(abs),dataSha=cfg.Sha};
   try
   {
    if(root==null){var go=new GameObject(cfg.Root);SceneManager.MoveGameObjectToScene(go,scene);root=go.transform;root.SetPositionAndRotation(Vector3.zero,Quaternion.identity);go.isStatic=true;write.rows.Add(new Row{kind="create-root",key=cfg.Root,detail="root"});}
    foreach(var p in todo)
    {
     Make(cfg,root,p,Lods.Select(l=>LoadMesh(cfg,p[l])).ToArray(),LoadMesh(cfg,p["col"]));
     write.rows.Add(new Row{kind="create",key=cfg.Root+"/"+Str(p,"id"),detail=Str(p,"id")+" at "+V(Vec(p,"pos"))+" lod0 "+Short(Str(p["lod0"],"sha256"))+" col "+Short(Str(p["col"],"sha256"))});
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
   return sb.Append("  saved: "+todo.Count+" panel(s)"+bake+"; scene sha "+Short(write.shaBefore)+" -> "+Short(write.shaAfter)+"; backup "+write.backup+"; ledger "+LedgerFile("scene_"+t.Alias)).ToString();
  }

  static string Revert(string arg)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,true);var ledger=ReadLedger("scene_"+t.Alias);
   var live=Live(ledger).ToList();
   if(live.Count==0)return "CliffMass308 scene-revert "+t.Alias+": 변경 없음 (no live ledger row)";
   var scene=Open(t);var sb=new StringBuilder("CliffMass308 scene-revert "+t.Alias+"\n");string copy=Backup(t.Scene);int kept=0,removed=0,gone=0;var marked=new List<Row>();
   // the created panels, the root last (only when nothing is left inside)
   foreach(var r in live.Where(x=>x.kind=="create").Concat(live.Where(x=>x.kind=="create-root")))
   {
    var tr=Unique(scene,r.key,out string why);
    if(tr==null){if(why.StartsWith("0 ",StringComparison.Ordinal)){r.reverted=true;marked.Add(r);gone++;}else{kept++;sb.AppendLine("  KEPT "+r.key+": "+why);}continue;}
    if(tr.root.name!=cfg.Root||PostLedger308.UnderProtectedTree(tr)){kept++;sb.AppendLine("  KEPT "+r.key+": not under "+cfg.Root);continue;}
    if(r.kind=="create-root"&&tr.childCount>0){kept++;sb.AppendLine("  KEPT "+r.key+": "+tr.childCount+" child object(s) are still inside");continue;}
    Object.DestroyImmediate(tr.gameObject);r.reverted=true;marked.Add(r);removed++;
   }
   WriteLedger("scene_"+t.Alias,ledger);EditorSceneManager.MarkSceneDirty(scene);
   if(!EditorSceneManager.SaveScene(scene))
   {
    foreach(var r in marked)r.reverted=false;WriteLedger("scene_"+t.Alias,ledger);EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);
    return "FAILED SaveScene returned false for "+t.Scene+" (scene reloaded from disk, nothing saved; the "+marked.Count+" ledger row(s) of this call are live again - run scene-revert again; backup "+copy+")";
   }
   return sb.Append("  saved; removed "+removed+", already gone "+gone+", kept "+kept+"; scene sha "+Short(Harness303.Sha(Harness303.Abs(t.Scene)))+"; backup "+copy+" (a NavMesh baked WITH these colliders is stale now: bake again)").ToString();
  }

  // ---------- check (read-only): AC-K1..K9 ----------

  static long Key(float x,float y,float z)=>((long)(Mathf.RoundToInt(x*1000f)+(1<<20))<<42)|((long)(Mathf.RoundToInt(y*1000f)+(1<<20))<<21)|(long)(Mathf.RoundToInt(z*1000f)+(1<<20));
  static HashSet<(long,long,long)> Triangles(KitMesh k,out int count)
  {
   var keys=new long[k.v.Length/3];for(int i=0;i<keys.Length;i++)keys[i]=Key(k.v[3*i],k.v[3*i+1],k.v[3*i+2]);
   var set=new HashSet<(long,long,long)>();var t=k.sub[0].t;count=t.Length/3;var a=new long[3];
   for(int i=0;i<t.Length;i+=3){a[0]=keys[t[i]];a[1]=keys[t[i+1]];a[2]=keys[t[i+2]];Array.Sort(a);set.Add((a[0],a[1],a[2]));}
   return set;
  }
  // hard rule: every collider triangle IS a drawn LOD0 triangle (same three corners to the millimetre) - never a volume outside the rock
  static int OutsideDrawn(Cfg cfg,JToken p,out int colTris)
  {
   var drawn=Triangles(ReadKit(cfg,p["lod0"]),out _);var col=ReadKit(cfg,p["col"]);var keys=new long[col.v.Length/3];
   for(int i=0;i<keys.Length;i++)keys[i]=Key(col.v[3*i],col.v[3*i+1],col.v[3*i+2]);
   var t=col.sub[0].t;colTris=t.Length/3;int outside=0;var a=new long[3];
   for(int i=0;i<t.Length;i+=3){a[0]=keys[t[i]];a[1]=keys[t[i+1]];a[2]=keys[t[i+2]];Array.Sort(a);if(!drawn.Contains((a[0],a[1],a[2])))outside++;}
   return outside;
  }

  static string Check(string arg)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,false);var scene=Open(t);var content=Content(t,scene);Physics.SyncTransforms();_ignore=null;int fail=0,pass=0;
   var sb=new StringBuilder("CliffMass308 check "+t.Alias+" (data "+Short(cfg.Sha)+")\n");
   void C(bool ok,string what){if(ok)pass++;else fail++;sb.AppendLine("  ["+(ok?"PASS":"FAIL")+"] "+what);}
   var root=Root(cfg,scene);var want=cfg.Panels.Select(p=>Str(p,"id")).ToList();
   var have=root==null?new List<string>():root.Cast<Transform>().Select(o=>o.name).ToList();
   C(root!=null&&want.All(have.Contains)&&have.Count==want.Count,"AC-K1 "+cfg.Root+" holds exactly the data panels ("+have.Count+" of "+want.Count+")"+(root==null?" - the root is absent: run scene-apply":""));
   if(root!=null)
   {
    C(scene.GetRootGameObjects().Count(g=>g.name==cfg.Root)==1&&!PostLedger308.UnderProtectedTree(root)&&Identity(root),"AC-K1 one root named "+cfg.Root+", at the identity transform, outside the protected trees");
    var rends=root.GetComponentsInChildren<Renderer>(true);
    C(root.GetComponentsInChildren<Light>(true).Length==0&&rends.All(r=>r.sharedMaterials.All(m=>m!=null&&!m.IsKeywordEnabled("_EMISSION"))),"AC-K5 no Light and no _EMISSION material under the root ("+rends.Length+" renderer(s), "+rends.SelectMany(r=>r.sharedMaterials).Distinct().Count()+" material)");
    C(root.GetComponentsInChildren<Canvas>(true).Length==0&&root.GetComponentsInChildren<Camera>(true).Length==0&&root.GetComponentsInChildren<NavMeshAgent>(true).Length==0&&root.GetComponentsInChildren<NavMeshObstacle>(true).Length==0&&root.GetComponentsInChildren<MonoBehaviour>(true).Length==0,
      "AC-K5 no Canvas (text / marker), Camera, NavMeshAgent / NavMeshObstacle or script under the root");
    int differ=0,jsonBad=0,outside=0,colTris=0,groundBad=0,clearBad=0,rayOk=0,rayBad=0,rayOther=0,rays=0;float gapMax=0f,clearMin=float.PositiveInfinity,rayMax=0f;string clearNear="";var bad=new List<string>();var others=new SortedDictionary<string,int>(StringComparer.Ordinal);
    foreach(var p in cfg.Panels)
    {
     string id=Str(p,"id");var obj=root.Find(id);if(obj==null){differ++;bad.Add(id+" is not in the scene");continue;}
     string why=Differs(cfg,obj,p);if(why!=null){differ++;bad.Add(id+" differs from the data ("+why+")");}
     bool files=true;foreach(string m in new[]{"lod0","lod1","col"}){string js=JsonState(cfg,p[m]);if(js!=null){files=false;jsonBad++;bad.Add(id+" "+m+": "+js);}}
     if(files){int o=OutsideDrawn(cfg,p,out int n);outside+=o;colTris+=n;if(o>0)bad.Add(id+": "+o+" collider triangle(s) are not drawn LOD0 triangles");}
     try{float gap=GroundGap(cfg,p);gapMax=Mathf.Max(gapMax,gap);if(gap>cfg.R("ground_tol_m")){groundBad++;bad.Add(id+": physical ground "+F(gap,"F3")+" m off the height field");}}catch(Refuse e){groundBad++;bad.Add(e.Message);}
     float clear=ContentClear(content,p,out string near);if(clear<clearMin){clearMin=clear;clearNear=near;}if(clear<cfg.R("content_clear_m")){clearBad++;bad.Add(id+": "+F(clear)+" m from the content ("+near+")");}
     // body rays: a walker's chest meets the drawn rock (or the terrain) where the offline build says - no wall before it, no way into it
     foreach(var q in Arr(p,"rays"))
     {
      var a=q as JArray;if(a==null||a.Count<7)continue;rays++;
      var origin=new Vector3(a[0].Value<float>(),a[1].Value<float>(),a[2].Value<float>());var dir=new Vector3(a[3].Value<float>(),0f,a[4].Value<float>()).normalized;float exp=a[5].Value<float>();string what=a[6].Value<string>();
      float best=float.PositiveInfinity;string hitWhat="nothing";
      foreach(var h in Physics.RaycastAll(origin,dir,cfg.R("ray_len_m"),~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
      {
       var c=h.collider;if(c==null||c is CharacterController||Preview(c.transform))continue;
       bool own=c.transform.root==root,terrain=!own&&TerrainLike(c);
       if(!own&&!terrain){if(h.distance<exp-cfg.R("ray_tol_m")){string n=c.transform.root.name;others[n]=others.TryGetValue(n,out int k)?k+1:1;}continue;}
       best=h.distance;hitWhat=own?"rock":"terrain";break;
      }
      float d=Mathf.Abs(best-exp);
      if(float.IsInfinity(best)||d>cfg.R("ray_tol_m")||hitWhat!=what){rayBad++;if(rayBad<=6)bad.Add(id+": body ray from "+V(origin)+" meets "+hitWhat+" at "+(float.IsInfinity(best)?"-":F(best))+" m, the build says "+what+" at "+F(exp)+" m");}
      else{rayOk++;rayMax=Mathf.Max(rayMax,d);}
     }
    }
    rayOther=others.Values.Sum();
    C(differ==0,"AC-K2 "+want.Count+" panel(s): pose, LODGroup of "+Lods.Length+" levels, the data's Mesh assets, the one granite material, shadow mode "+Shadow(cfg)+" ("+differ+" differ)");
    C(jsonBad==0,"AC-K3 the mesh json files on disk are the data's ("+jsonBad+" are not)");
    var all=root.GetComponentsInChildren<Collider>(true);
    C(all.Length==want.Count&&all.All(c=>c is MeshCollider m&&!m.convex&&!m.isTrigger),"AC-K4 colliders under the root: "+all.Length+" = one MeshCollider per panel, none convex, none a trigger, no box / fill volume");
    C(outside==0&&jsonBad==0,"AC-K6 hard rule: every collider triangle is a drawn LOD0 triangle ("+colTris+" triangles, "+outside+" outside drawn rock; tolerance 0 mm)");
    C(groundBad==0,"AC-K7 the physical ground under the probes is within "+F(gapMax,"F3")+" m of the height field the meshes were baked to (<= "+F(cfg.R("ground_tol_m"))+") ("+groundBad+" fail)");
    C(clearBad==0,"AC-K8 nearest content (path points, prompt discs) "+F(clearMin,"F1")+" m ("+clearNear+", >= "+F(cfg.R("content_clear_m"),"F1")+") ("+clearBad+" fail)");
    C(rayBad==0&&rays>0,"AC-K9 body rays: "+rayOk+" of "+rays+" meet the rock / terrain where the build says (largest difference "+F(rayMax,"F3")+" m, limit "+F(cfg.R("ray_tol_m"))+") ("+rayBad+" fail)");
    if(rayOther>0)sb.AppendLine("  WARN "+rayOther+" body ray(s) pass through a collider of another root before the rock: "+string.Join(", ",others.Select(kv=>kv.Key+" x"+kv.Value))+" - look at these places (a tree / prop standing in the new rock)");
    foreach(var b in bad.Take(14))sb.AppendLine("    - "+b);if(bad.Count>14)sb.AppendLine("    - ... "+(bad.Count-14)+" more");
    sb.AppendLine("  colliders that the NavMesh bake must see: "+all.Length+" MeshCollider(s), "+colTris+" triangles");
   }
   if(scene.isDirty)sb.AppendLine("  WARN the scene is dirty after a read-only pass (bug): reload it");
   return sb.Append("  "+(fail==0?"no FAIL":"FAIL "+fail)+" (PASS "+pass+")").ToString();
  }

  // ---------- status ----------

  static string Status()
  {
   var sb=new StringBuilder("CliffMass308 status (play="+EditorApplication.isPlaying+")\n");
   Cfg cfg;try{cfg=Load();}catch(Refuse r){return sb.Append("  "+r.Message).ToString();}
   var rows=MeshRows(cfg).ToList();var tot=cfg.J["totals"];
   sb.AppendLine("  data "+DataFile+" sha "+Short(cfg.Sha)+" version "+(Opt(cfg.J,"version")??"?")+", group "+(Opt(cfg.J,"group")??"")+": "+cfg.Panels.Count()+" panel(s), LOD0 "+(Opt(tot,"lod0_tris")??"?")+" / LOD1 "+(Opt(tot,"lod1_tris")??"?")+" / collider "+(Opt(tot,"col_tris")??"?")+" triangles");
   sb.AppendLine("  mesh assets in place "+rows.Count(d=>LoadMesh(cfg,d,false)!=null)+"/"+rows.Count+" under "+cfg.AssetDir+"; mesh json "+rows.Count(d=>JsonState(cfg,d)==null)+"/"+rows.Count+" as the data says; live asset ledger rows "+Live(ReadLedger("assets")).Count());
   foreach(var t in cfg.Targets)sb.AppendLine("  "+t.Alias+": live scene ledger rows "+Live(ReadLedger("scene_"+t.Alias)).Count());
   var active=SceneManager.GetActiveScene();var root=Root(cfg,active);
   return sb.Append("  active scene "+active.path+(active.isDirty?" (dirty)":"")+": "+cfg.Root+" "+(root==null?"absent":"present, "+root.childCount+" panel(s)")).ToString();
  }
 }
}
