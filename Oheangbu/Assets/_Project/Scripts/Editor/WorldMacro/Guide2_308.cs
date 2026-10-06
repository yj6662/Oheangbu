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
 // #308 guide 2 (D308-32, group G17_guide2) - the guide grammar between the mine mouth and the first inn. Every value [TEST].
 // Built ON TOP of OpenGuide308 (Stage308_openguide): that tool's rows must be in the scene first; nothing of it is repeated.
 // This tool owns three kinds of thing under ONE own root (Guide2_308) and nothing else:
 //   funnel  rows part=funnel: rock bands that close the wrong sides of the road. Generated Mesh assets with EXISTING
 //           materials, baked to the height field in world axes (y_mode data: the pivot y is the data's, probes[] are compared
 //           with the physical ground) and SOLID: a chain of BoxColliders inside the wall blocks, checked corner by corner
 //           against the physical ground (no lip a jump reaches, no float). The protected terrain is not touched.
 //   traces  rows part=trace: the chain of human traces (cairn, 장승, 솟대, collapsed log landing, hand cart) - generated meshes,
 //           y_mode ground (the pivot stands on the physical ground), one collider box or none.
 //   shrine  rows kind=rig: a 금줄 behind a rest altar - an empty object with parts[] (static frame, paper strips, smoke puffs)
 //           and the runtime component ShrineLean308 configured from lean{} (the strips and the candle smoke lean toward the
 //           next place). A smoke part stands at its anchor (a candle of that rest); a missing anchor skips the part.
 // No light, no emission, no text, no content row, no camera, no HUD, no terrain edit, no vegetation sheet, no hide.
 // Queue-safe: Run(string) never opens a dialog; refusals come back as "REFUSED ...". Ledgers
 // (Art/Playtest308/Pacing/ledger_guide2_308_assets.json, ledger_guide2_308_scene_<alias>.json) are NEW files of this tool,
 // written BEFORE the asset / scene is saved, with a byte backup; a second identical apply reports "변경 없음"; a revert takes
 // out exactly what the ledger made. Only the target scene / the created assets are saved (never AssetDatabase.SaveAssets).
 //   status
 //   assets-plan | assets-apply | assets-revert      the Mesh assets and the one new Material under asset_dir (once)
 //   plan:<scene> | scene-apply:<scene> | scene-revert:<scene>
 //   check:<scene>                                   AC-U1..U12 on the saved scene; read-only
 // <scene> = arch296 | folk298 | main. Order: RUN_ORDER_guide2.md. The solid rows carry BoxColliders: they must be in all three
 // scenes BEFORE the one NavMesh bake.
 public static class Guide2_308
 {
  const string Usage="status | assets-plan | assets-apply | assets-revert | plan:<scene> | scene-apply:<scene> | scene-revert:<scene> | check:<scene>";
  static string DataFile=>Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","CliffBoundary308","guide2_308.json");
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
    string a;
    if((a=Arg(c,"plan:"))!=null)return Apply(a,true);
    if((a=Arg(c,"scene-apply:"))!=null)return Apply(a,false);
    if((a=Arg(c,"scene-revert:"))!=null)return Revert(a);
    if((a=Arg(c,"check:"))!=null)return Check(a);
   }
   catch(Refuse r){return "REFUSED "+r.Message;}
   catch(Exception e){return "FAILED "+e;}
   return "REFUSED unknown Guide2_308 command '"+c+"' ("+Usage+")";
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
  static JToken Req(JToken o,string key){var t=o?[key];if(t==null||t.Type==JTokenType.Null)throw new Refuse("guide2_308.json: missing '"+key+"' in "+(o==null?"(null)":string.IsNullOrEmpty(o.Path)?"(root)":o.Path));return t;}
  static float Num(JToken o,string key)=>Req(o,key).Value<float>();
  static float OptNum(JToken o,string key,float fallback){var t=o?[key];return t==null||t.Type==JTokenType.Null?fallback:t.Value<float>();}
  static string Str(JToken o,string key)=>Req(o,key).Value<string>();
  static string Opt(JToken o,string key){var t=o?[key];return t==null||t.Type==JTokenType.Null?null:t.Value<string>();}
  static bool Flag(JToken o,string key,bool fallback=false){var t=o?[key];return t!=null&&t.Type==JTokenType.Boolean?t.Value<bool>():fallback;}
  static IEnumerable<JToken> Arr(JToken o,string key){var t=o?[key] as JArray;return t??(IEnumerable<JToken>)Array.Empty<JToken>();}
  static Vector3 Vec(JToken o,string key){var a=Req(o,key) as JArray;if(a==null||a.Count<3)throw new Refuse("guide2_308.json: "+key+" needs 3 numbers");return new Vector3(a[0].Value<float>(),a[1].Value<float>(),a[2].Value<float>());}
  static string F(float v,string f="F2")=>v.ToString(f,CultureInfo.InvariantCulture);
  static string V(Vector3 v)=>Harness303.V(v);
  static string Short(string sha)=>string.IsNullOrEmpty(sha)?"-":sha.Substring(0,Math.Min(12,sha.Length)).ToLowerInvariant();
  static bool Protected(string path)=>Harness303.IsProtected(path)||PostLedger308.IsProtectedPath(path);
  static string MatAsset(Cfg cfg,JToken nm)=>cfg.AssetDir+"/"+Str(nm,"name")+".mat";

  static Cfg Load(bool needMaterials=true)
  {
   string f=DataFile;if(!File.Exists(f))throw new Refuse("data missing: "+f+" (python Tools/Unity/Stage308_guide2/guide2_copy.py --apply)");
   JObject j;try{j=JObject.Parse(File.ReadAllText(f));}catch(Exception e){throw new Refuse("data does not parse: "+f+" ("+e.Message+")");}
   var cfg=new Cfg{J=j,Sha=Harness303.Sha(f)};
   foreach(string key in new[]{"rules","rows","meshes","materials","new_materials","requires","root","asset_dir","mesh_dir"})Req(j,key);
   foreach(string key in new[]{"pose_tol_m","pose_tol_deg","scale_tol","y_tol_m","baked_y_tol_m","corner_gap_max_m","corridor_clear_m","path_run_gap_m","point_collider_pad_m","mesh_bounds_tol_m","wall_min_m","wall_under_m"})Num(cfg.Rules,key);   // a missing rule refuses here, not half way through a pass
   cfg.Targets=Arr(j,"targets").Select(t=>new Target{Alias=Str(t,"alias"),Scene=Str(t,"scene"),Content=Str(t,"content")}).ToArray();
   if(cfg.Targets.Length==0)throw new Refuse("guide2_308.json: targets[] is empty");
   foreach(var p in cfg.Targets.SelectMany(t=>new[]{t.Scene,t.Content}).Concat(new[]{cfg.AssetDir}))if(Protected(p))throw new Refuse("protected path in data: "+p);
   // existing materials only: the guid must resolve to the path the data names, and nothing may glow ("오염은 빛나지 않는다")
   foreach(var kv in (JObject)j["materials"])
   {
    string path=Str(kv.Value,"path"),guid=Str(kv.Value,"guid");
    if(Protected(path))throw new Refuse("material '"+kv.Key+"' lives under a protected tree: "+path);
    if(AssetDatabase.GUIDToAssetPath(guid)!=path)throw new Refuse("material '"+kv.Key+"': guid "+guid+" resolves to '"+AssetDatabase.GUIDToAssetPath(guid)+"', the data says "+path);
    var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m==null)throw new Refuse("material '"+kv.Key+"' does not load: "+path);
    if(m.IsKeywordEnabled("_EMISSION"))throw new Refuse("material '"+kv.Key+"' ("+path+") has _EMISSION: this stage adds no glow");
    cfg.Mats[kv.Key]=m;
   }
   // the materials this tool makes itself (assets-apply): lit, no emission; loaded when they are there
   foreach(var nm in Arr(j,"new_materials"))
   {
    string slot=Str(nm,"slot");if(cfg.Mats.ContainsKey(slot))throw new Refuse("new material slot '"+slot+"' is also an existing material");
    if(Str(nm,"shader").IndexOf("Unlit",StringComparison.OrdinalIgnoreCase)>=0)throw new Refuse("new material "+Str(nm,"name")+": an unlit shader would not darken at night - lit only");
    var m=AssetDatabase.LoadAssetAtPath<Material>(MatAsset(cfg,nm));
    if(m!=null){if(m.IsKeywordEnabled("_EMISSION"))throw new Refuse(MatAsset(cfg,nm)+" has _EMISSION");cfg.Mats[slot]=m;}
    else if(needMaterials)throw new Refuse("material asset "+MatAsset(cfg,nm)+" is missing - run assets-apply first. Nothing written");
   }
   var slots=new HashSet<string>(((JObject)j["materials"]).Properties().Select(p=>p.Name).Concat(Arr(j,"new_materials").Select(n=>Str(n,"slot"))));
   foreach(var m in Arr(j,"meshes"))foreach(var s in Arr(m,"slots"))if(!slots.Contains(s.Value<string>()))throw new Refuse("mesh "+Str(m,"name")+": slot '"+s.Value<string>()+"' has no material in materials{} / new_materials[]");
   var ids=new HashSet<string>(StringComparer.Ordinal);
   foreach(var r in Arr(j,"rows"))
   {
    string id=Str(r,"id");if(!ids.Add(id))throw new Refuse("row id '"+id+"' is used twice");
    string kind=Str(r,"kind");if(kind!="mesh"&&kind!="rig")throw new Refuse("row "+id+": kind '"+kind+"' (mesh | rig)");
    string ym=Str(r,"y_mode");if(ym!="ground"&&ym!="data")throw new Refuse("row "+id+": y_mode '"+ym+"' (ground | data)");
    if(r["light"]!=null||r["text"]!=null||r["prompt"]!=null)throw new Refuse("row "+id+" carries a light / text / prompt key: this tool makes none");
    if(ym=="ground")Num(r,"max_slope_deg");else{Num(r,"y");if(!Arr(r,"probes").Any())throw new Refuse("row "+id+": y_mode data needs probes[]");}
    if(kind=="mesh")
    {
     var m=MeshRow(cfg,Str(r,"mesh"));
     if(Flag(r,"solid")&&!Arr(m,"colliders").Any())throw new Refuse("row "+id+" is solid but mesh "+Str(r,"mesh")+" has no collider box");
    }
    else
    {
     if(ym!="ground"||Flag(r,"solid"))throw new Refuse("row "+id+": a rig stands on the ground and has no collider");
     if(!Arr(r,"parts").Any())throw new Refuse("row "+id+": a rig needs parts[]");
     var names=new HashSet<string>(StringComparer.Ordinal);
     foreach(var p in Arr(r,"parts"))
     {
      if(!names.Add(Str(p,"name")))throw new Refuse("row "+id+": part name '"+Str(p,"name")+"' is used twice");
      MeshRow(cfg,Str(p,"mesh"));Vec(p,"pos");string role=Str(p,"role");
      if(role!="static"&&role!="strip"&&role!="smoke")throw new Refuse("row "+id+" part "+Str(p,"name")+": role '"+role+"' (static | strip | smoke)");
      if(role=="smoke")Num(p,"rise");
     }
     var ln=Req(r,"lean");Vec(ln,"next");foreach(string key in new[]{"lean_deg","flutter_deg","flutter_hz","gust_hz","gust_share","smoke_lean_deg","smoke_sway_m"})Num(ln,key);
    }
   }
   return cfg;
  }
  static float CornerMax(Cfg cfg,JToken r)=>OptNum(r,"corner_gap_max_m",cfg.R("corner_gap_max_m"));
  static JToken MeshRow(Cfg cfg,string name)=>Arr(cfg.J,"meshes").FirstOrDefault(m=>Str(m,"name")==name)??throw new Refuse("mesh '"+name+"' is not in meshes[]");
  static Target TargetOf(Cfg cfg,string arg)
  {
   arg=(arg??"").Trim().Replace('\\','/');
   var t=cfg.Targets.FirstOrDefault(x=>string.Equals(x.Alias,arg,StringComparison.OrdinalIgnoreCase)||x.Scene==arg);
   if(t==null)throw new Refuse("not a guide2_308 target: '"+arg+"' (allowed: "+string.Join(", ",cfg.Targets.Select(x=>x.Alias))+")");
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
  [Serializable] sealed class Ledger{public string kind="guide2_308",group="",target="",created="";public List<Write> writes=new List<Write>();}
  static string LedgerFile(string key)=>Path.Combine(OutDir,"ledger_guide2_308_"+key+".json");
  static Ledger ReadLedger(string key){var f=LedgerFile(key);return File.Exists(f)?JsonUtility.FromJson<Ledger>(File.ReadAllText(f)):null;}
  static void WriteLedger(string key,Ledger l){Directory.CreateDirectory(OutDir);File.WriteAllText(LedgerFile(key),JsonUtility.ToJson(l,true));}
  static IEnumerable<Row> Live(Ledger l)=>l==null?Enumerable.Empty<Row>():l.writes.Where(w=>w!=null&&w.rows!=null).SelectMany(w=>w.rows).Where(r=>r!=null&&!r.reverted);
  static string Backup(string assetPath,string tag="")
  {
   string dir=Path.Combine(OutDir,"Backups","guide2_308-"+tag+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff",CultureInfo.InvariantCulture));Directory.CreateDirectory(dir);
   string copy=Path.Combine(dir,Path.GetFileName(assetPath));File.Copy(Harness303.Abs(assetPath),copy,true);return copy;
  }

  // ---------- Mesh / Material assets (shared by the three scenes) ----------

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
  // a lit, transparent, non-emissive material (candle smoke). Lit on purpose: it darkens with the scene, it is not a light
  static Material BuildMaterial(JToken nm)
  {
   var sh=Shader.Find(Str(nm,"shader"));if(sh==null)throw new Refuse("shader '"+Str(nm,"shader")+"' not found for "+Str(nm,"name"));
   var a=Req(nm,"color") as JArray;if(a==null||a.Count<4)throw new Refuse("new material "+Str(nm,"name")+": color needs 4 numbers");
   var m=new Material(sh){name=Str(nm,"name")};
   m.SetColor("_BaseColor",new Color(a[0].Value<float>(),a[1].Value<float>(),a[2].Value<float>(),a[3].Value<float>()));
   m.SetFloat("_Smoothness",OptNum(nm,"smoothness",0f));
   m.SetFloat("_Surface",1f);m.SetFloat("_Blend",0f);m.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);m.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);m.SetFloat("_ZWrite",0f);m.SetFloat("_Cull",(float)CullMode.Off);
   m.SetOverrideTag("RenderType","Transparent");m.renderQueue=(int)RenderQueue.Transparent;m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.DisableKeyword("_EMISSION");
   m.SetShaderPassEnabled("ShadowCaster",false);m.SetShaderPassEnabled("DepthOnly",false);m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.EmissiveIsBlack;
   return m;
  }
  static bool MaterialMatches(Material m,JToken nm)=>m!=null&&m.shader!=null&&m.shader.name==Str(nm,"shader")&&!m.IsKeywordEnabled("_EMISSION")&&m.renderQueue>=(int)RenderQueue.Transparent;
  static bool AnySceneLive(Cfg cfg,out string who)
  {
   who=string.Join(", ",cfg.Targets.Where(t=>Live(ReadLedger("scene_"+t.Alias)).Any(r=>r.kind=="create")).Select(t=>t.Alias));return who.Length>0;
  }
  static string Assets(string mode)
  {
   var cfg=Load(false);var sb=new StringBuilder("Guide2_308 assets-"+mode+" (data "+Short(cfg.Sha)+" "+(Opt(cfg.J,"version")??"?")+")\n");
   var ledger=ReadLedger("assets")??new Ledger{group=Opt(cfg.J,"group")??"",target=cfg.AssetDir,created=DateTime.UtcNow.ToString("O")};
   if(mode=="revert")
   {
    if(AnySceneLive(cfg,out string who))throw new Refuse("scene ledger rows are still live ("+who+"): scene-revert those scenes first - their objects use these assets");
    var live=Live(ledger).ToList();if(live.Count==0)return sb.Append("  변경 없음 (no live asset row)").ToString();
    int gone=0,kept=0;
    foreach(var r in Enumerable.Reverse(live))
    {
     if(r.kind=="mesh"||r.kind=="material"){if(AssetDatabase.LoadMainAssetAtPath(r.key)==null)gone++;else if(AssetDatabase.DeleteAsset(r.key))gone++;else{kept++;sb.AppendLine("  KEPT "+r.key+": DeleteAsset returned false");continue;}}
     else if(r.kind=="folder"){if(!AssetDatabase.IsValidFolder(r.key))sb.AppendLine("  "+r.key+": already gone");else if(Directory.EnumerateFileSystemEntries(Harness303.Abs(r.key)).Any(e=>!e.EndsWith(".meta",StringComparison.Ordinal))){kept++;sb.AppendLine("  KEPT folder "+r.key+": it still holds assets");continue;}else if(AssetDatabase.DeleteAsset(r.key))sb.AppendLine("  deleted folder "+r.key);else{kept++;sb.AppendLine("  KEPT folder "+r.key);continue;}}
     r.reverted=true;
    }
    WriteLedger("assets",ledger);return sb.Append("  deleted "+gone+" asset(s), kept "+kept+"; ledger "+LedgerFile("assets")).ToString();
   }
   var todo=new List<JToken>();var mats=new List<JToken>();int same=0;
   foreach(var m in Arr(cfg.J,"meshes"))
   {
    string name=Str(m,"name"),path=MeshAsset(cfg,name),bad=JsonState(cfg,m);
    if(bad!=null)throw new Refuse("mesh "+name+": "+bad+" (guide2_copy.py copies the json files; nothing written)");
    var have=AssetDatabase.LoadAssetAtPath<Mesh>(path);
    if(have==null)todo.Add(m);
    else if(MeshMatches(cfg,have,m))same++;
    else throw new Refuse(path+" exists and is not the mesh the data describes - assets-revert first (after scene-revert). Nothing written");
   }
   foreach(var nm in Arr(cfg.J,"new_materials"))
   {
    var have=AssetDatabase.LoadAssetAtPath<Material>(MatAsset(cfg,nm));
    if(have==null)mats.Add(nm);
    else if(MaterialMatches(have,nm))same++;
    else throw new Refuse(MatAsset(cfg,nm)+" exists and is not the material the data describes - assets-revert first. Nothing written");
   }
   int tris=todo.Sum(m=>Mathf.RoundToInt(Num(m,"tris")));
   sb.AppendLine("  create "+todo.Count+" mesh asset(s) ("+tris+" triangles) and "+mats.Count+" material(s) under "+cfg.AssetDir+"; already in place "+same);
   if(todo.Count==0&&mats.Count==0)return sb.Append("  변경 없음 (every asset is in place)").ToString();
   if(mode=="plan")return sb.Append("  plan only: nothing written").ToString();
   var write=new Write{utc=DateTime.UtcNow.ToString("O"),target=cfg.AssetDir,dataSha=cfg.Sha};
   string acc="";foreach(string part in cfg.AssetDir.Split('/'))
   {
    string parent=acc;acc=acc.Length==0?part:acc+"/"+part;if(AssetDatabase.IsValidFolder(acc))continue;
    write.rows.Add(new Row{kind="folder",key=acc,detail="folder"});if(!ledger.writes.Contains(write))ledger.writes.Add(write);WriteLedger("assets",ledger);
    if(string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent,part)))throw new Refuse("could not create folder "+acc);
   }
   // every row is journalled before its file exists (one ledger write for the whole batch, then the assets one by one)
   foreach(var m in todo)write.rows.Add(new Row{kind="mesh",key=MeshAsset(cfg,Str(m,"name")),detail=Str(m,"name")+" json "+Short(Str(m,"sha256"))});
   foreach(var nm in mats)write.rows.Add(new Row{kind="material",key=MatAsset(cfg,nm),detail=Str(nm,"name")+" "+Str(nm,"shader")});
   if(!ledger.writes.Contains(write))ledger.writes.Add(write);WriteLedger("assets",ledger);
   foreach(var m in todo){var mesh=BuildMesh(cfg,m);AssetDatabase.CreateAsset(mesh,MeshAsset(cfg,Str(m,"name")));AssetDatabase.SaveAssetIfDirty(mesh);}
   foreach(var nm in mats){var mat=BuildMaterial(nm);AssetDatabase.CreateAsset(mat,MatAsset(cfg,nm));AssetDatabase.SaveAssetIfDirty(mat);sb.AppendLine("  created "+MatAsset(cfg,nm)+" (lit, transparent, no emission)");}
   return sb.Append("  created "+todo.Count+" mesh asset(s) and "+mats.Count+" material(s); ledger "+LedgerFile("assets")).ToString();
  }

  // ---------- ground ----------

  static bool Under(Transform t,HashSet<string> roots){for(var p=t;p!=null;p=p.parent)if(p.parent==null&&roots.Contains(p.name))return true;return false;}
  static bool Preview(Transform t){for(var p=t;p!=null;p=p.parent)if((p.gameObject.hideFlags&HideFlags.DontSave)!=0)return true;return false;}
  static bool TerrainLike(Collider c){if(c is TerrainCollider)return true;for(var t=c.transform;t!=null;t=t.parent){var n=t.name;if(n.Contains("Terrain")||n.Contains("Surface"))return true;}return false;}
  // the Content308 rule in short (as Places308 / OpenGuide308): the highest terrain surface under the XZ; own / listed roots and previews never count
  static HashSet<string> _ignore;
  static bool Ground(Cfg cfg,float x,float z,out Vector3 p,out float slope)
  {
   p=default;slope=90f;if(_ignore==null)_ignore=new HashSet<string>(Arr(cfg.Rules,"ground_ignore_roots").Select(v=>v.Value<string>()));bool any=false;
   foreach(var h in Physics.RaycastAll(new Vector3(x,2000f,z),Vector3.down,4000f,~0,QueryTriggerInteraction.Ignore))
   {
    var c=h.collider;if(c==null||h.normal.y<=.5f||c is CharacterController||Preview(c.transform)||Under(c.transform,_ignore)||!TerrainLike(c))continue;
    if(!any||h.point.y>p.y){p=h.point;slope=Vector3.Angle(h.normal,Vector3.up);any=true;}
   }
   return any;
  }

  // ---------- plan ----------

  sealed class Part{public string Name,Role,Anchor;public Mesh Mesh;public Material[] Mats;public Vector3 Pos;public float Scale=1f,Rise;public bool Shadows,Skip;public string Why;}
  sealed class Item
  {
   public string Id,Kind;public JToken Row,MeshData;public Mesh Mesh;public Material[] Mats;public Vector3 Pos;public Quaternion Rot;public bool Solid,Baked,Shadows;
   public float Slope,CornerGap,ProbeGap,WallMin=float.PositiveInfinity,UnderMin=float.PositiveInfinity;public List<string> Notes=new List<string>();public int Warns;public List<Part> Parts=new List<Part>();
   public string Path(Cfg cfg)=>cfg.Root+"/"+Id;
  }
  static JToken[] Boxes(Item it)=>it.Solid?Arr(it.MeshData,"colliders").ToArray():Array.Empty<JToken>();
  // world XZ corners + world top / bottom of a collider box of the row
  static IEnumerable<(Vector2 xz,float top,float bottom)> BoxCorners(Item it,JToken box)
  {
   Vector3 c=Vec(box,"center"),s=Vec(box,"size");   // the centre turns with the row, the box with the row and its own yaw
   var mc=Matrix4x4.TRS(it.Pos,it.Rot,Vector3.one).MultiplyPoint3x4(c);var rot=it.Rot*Quaternion.Euler(0,OptNum(box,"yaw",0f),0);
   foreach(var k in new[]{new Vector3(-1,0,-1),new Vector3(1,0,-1),new Vector3(1,0,1),new Vector3(-1,0,1)})
   {var w=mc+rot*new Vector3(k.x*s.x*.5f,0,k.z*s.z*.5f);yield return (new Vector2(w.x,w.z),w.y+s.y*.5f,w.y-s.y*.5f);}
  }
  static Mesh LoadMesh(Cfg cfg,string id,string name,out JToken data)
  {
   data=MeshRow(cfg,name);var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(MeshAsset(cfg,name));
   if(mesh==null||!MeshMatches(cfg,mesh,data))throw new Refuse(id+": mesh asset "+MeshAsset(cfg,name)+(mesh==null?" is missing":" is not the data's mesh")+" - run assets-apply first. Nothing written");
   return mesh;
  }
  static Material[] MatsOf(Cfg cfg,JToken meshData)=>Arr(meshData,"slots").Select(v=>cfg.Mats[v.Value<string>()]).ToArray();
  static Item Build(Cfg cfg,Scene scene,JToken r)
  {
   var it=new Item{Id=Str(r,"id"),Kind=Str(r,"kind"),Row=r,Solid=Flag(r,"solid"),Baked=Str(r,"y_mode")=="data",Shadows=Flag(r,"shadows",true)};float x=Num(r,"x"),z=Num(r,"z");
   if(!Ground(cfg,x,z,out var g,out float slope))throw new Refuse("no terrain under "+it.Id+" ("+F(x,"F1")+", "+F(z,"F1")+") - is the scene loaded?");
   it.Slope=slope;it.Rot=Quaternion.Euler(0,Num(r,"yaw"),0);
   if(it.Kind=="mesh"){it.Mesh=LoadMesh(cfg,it.Id,Str(r,"mesh"),out it.MeshData);it.Mats=MatsOf(cfg,it.MeshData);}
   if(it.Baked)
   {
    // a mesh baked to the height field: its pivot y is the data's; the physical ground under the probes says whether that still holds
    it.Pos=new Vector3(x,Num(r,"y"),z);int n=0;
    foreach(var p in Arr(r,"probes")){var a=p as JArray;if(a==null||a.Count<3)throw new Refuse(it.Id+": a probe needs [x, z, y]");if(!Ground(cfg,a[0].Value<float>(),a[1].Value<float>(),out var gp,out _))throw new Refuse(it.Id+": no terrain under a probe");it.ProbeGap=Mathf.Max(it.ProbeGap,Mathf.Abs(gp.y-a[2].Value<float>()));n++;}
    if(it.ProbeGap>cfg.R("y_tol_m"))throw new Refuse(it.Id+": the physical ground is "+F(it.ProbeGap)+" m off the height field this mesh was baked to (> "+F(cfg.R("y_tol_m"))+") - build the stage again on the current terrain. Nothing written");
    bool warn=it.ProbeGap>cfg.R("baked_y_tol_m");if(warn)it.Warns++;
    // a baked row may be solid: every collider corner is measured on the physical ground (the wall must be a wall there)
    foreach(var box in Boxes(it))foreach(var c in BoxCorners(it,box))
    {
     if(!Ground(cfg,c.xz.x,c.xz.y,out var gc,out _))throw new Refuse(it.Id+": no terrain under a collider corner");
     it.WallMin=Mathf.Min(it.WallMin,c.top-gc.y);it.UnderMin=Mathf.Min(it.UnderMin,gc.y-c.bottom);
    }
    if(warn)it.Notes.Add("WARN "+it.Id+" (baked) the physical ground under "+n+" probe(s) is "+F(it.ProbeGap,"F3")+" m off the height field (warn above "+F(cfg.R("baked_y_tol_m"))+")");
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
   bool w2=Mathf.Abs(dy)>cfg.R("y_tol_m")||it.CornerGap>CornerMax(cfg,r);if(w2){it.Warns++;it.Notes.Add("WARN "+it.Id+" at "+V(it.Pos)+": offline y "+F(OptNum(r,"y",g.y))+" (Δ "+F(dy)+" m, the editor value is used), ground under the collider corners within "+F(it.CornerGap)+" m (limit "+F(CornerMax(cfg,r))+")");}
   if(it.Kind=="rig")
   {
    var world=Matrix4x4.TRS(it.Pos,it.Rot,Vector3.one);
    foreach(var p in Arr(r,"parts"))
    {
     var part=new Part{Name=Str(p,"name"),Role=Str(p,"role"),Anchor=Opt(p,"anchor"),Scale=OptNum(p,"scale",1f),Rise=OptNum(p,"rise",0f),Shadows=Flag(p,"shadows",true)};
     part.Mesh=LoadMesh(cfg,it.Id,Str(p,"mesh"),out var md);part.Mats=MatsOf(cfg,md);Vector3 local=Vec(p,"pos");
     if(part.Anchor==null)part.Pos=world.MultiplyPoint3x4(local);
     else
     {
      var a=Unique(scene,part.Anchor,out string why);
      if(a==null){part.Skip=true;part.Why=part.Anchor+" is not in this scene ("+why+")";}
      else part.Pos=a.position+local;                                   // a smoke puff stands over its candle, in world axes
     }
     it.Parts.Add(part);
    }
    int skipped=it.Parts.Count(p=>p.Skip);
    if(skipped>0){it.Warns++;it.Notes.Add("WARN "+it.Id+": "+skipped+" part(s) skipped - "+it.Parts.First(p=>p.Skip).Why);}
   }
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
  // the runs of a content path near the stage (bounds + margin): the corridor test of 400 boxes does not walk 3,000 points each
  sealed class Lines{public readonly List<(Vector2 a,Vector2 b)> Seg=new List<(Vector2,Vector2)>();}
  static Lines Near(Cfg cfg,WorldMacroPlaytestSO content,float gap)
  {
   var o=new Lines();float xmin=float.PositiveInfinity,xmax=float.NegativeInfinity,zmin=float.PositiveInfinity,zmax=float.NegativeInfinity;
   foreach(var r in Arr(cfg.J,"rows")){float x=Num(r,"x"),z=Num(r,"z");xmin=Mathf.Min(xmin,x);xmax=Mathf.Max(xmax,x);zmin=Mathf.Min(zmin,z);zmax=Mathf.Max(zmax,z);}
   xmin-=60f;xmax+=60f;zmin-=60f;zmax+=60f;
   foreach(var path in new[]{content.MainPath,content.BranchPath})
   {
    if(path==null)continue;
    for(int i=1;i<path.Length;i++)
    {
     var a=new Vector2(path[i-1].x,path[i-1].z);var b=new Vector2(path[i].x,path[i].z);if(Vector2.Distance(a,b)>gap)continue;
     if(Mathf.Max(a.x,b.x)<xmin||Mathf.Min(a.x,b.x)>xmax||Mathf.Max(a.y,b.y)<zmin||Mathf.Min(a.y,b.y)>zmax)continue;
     o.Seg.Add((a,b));
    }
   }
   return o;
  }
  // collider corners against the content path centre lines and the prompt discs of the content points
  static void Corridor(Cfg cfg,WorldMacroPlaytestSO content,Lines lines,Item it,out float line,out float pad,out string near)
  {
   line=float.PositiveInfinity;pad=float.PositiveInfinity;near="";
   foreach(var box in Boxes(it))foreach(var c in BoxCorners(it,box))
   {
    foreach(var s in lines.Seg)line=Mathf.Min(line,SegDist(c.xz,s.a,s.b));
    foreach(var p in content.Points??Array.Empty<PrologueContentSO.Point>()){if(p==null)continue;float d=Vector2.Distance(c.xz,new Vector2(p.Position.x,p.Position.z))-p.Radius;if(d<pad){pad=d;near=p.Id;}}
   }
  }
  static Transform Root(Cfg cfg,Scene s)=>s.GetRootGameObjects().Where(g=>g.name==cfg.Root&&!Preview(g.transform)).Select(g=>g.transform).FirstOrDefault();
  static bool Near3(Vector3 a,Vector3 b,float tol)=>(a-b).magnitude<=tol;
  static bool Same(Cfg cfg,Transform have,Item it)
  {
   if(!Near3(have.position,it.Pos,cfg.R("pose_tol_m"))||Quaternion.Angle(have.rotation,it.Rot)>cfg.R("pose_tol_deg")||(have.localScale-Vector3.one).magnitude>cfg.R("scale_tol"))return false;
   if(it.Kind=="mesh")
   {
    var mf=have.GetComponent<MeshFilter>();var mr=have.GetComponent<MeshRenderer>();if(mf==null||mr==null||mf.sharedMesh!=it.Mesh||!mr.sharedMaterials.SequenceEqual(it.Mats))return false;
    return have.GetComponentsInChildren<Collider>(true).Length==Boxes(it).Length;
   }
   var live=it.Parts.Where(p=>!p.Skip).ToList();if(have.childCount!=live.Count||have.GetComponentsInChildren<Collider>(true).Length!=0)return false;
   foreach(var p in live)
   {
    var c=have.Find(p.Name);if(c==null)return false;var mf=c.GetComponent<MeshFilter>();var mr=c.GetComponent<MeshRenderer>();
    if(mf==null||mr==null||mf.sharedMesh!=p.Mesh||!mr.sharedMaterials.SequenceEqual(p.Mats)||!Near3(c.position,p.Pos,cfg.R("pose_tol_m"))||Mathf.Abs(c.localScale.x-p.Scale)>cfg.R("scale_tol"))return false;
   }
   return LeanSame(have.GetComponent<ShrineLean308>(),it,live);
  }
  static bool LeanSame(ShrineLean308 l,Item it,List<Part> live)
  {
   if(l==null)return false;var ln=it.Row["lean"];const float e=1e-3f;
   return l.HasNext&&Near3(l.Next,Vec(ln,"next"),.01f)&&Mathf.Abs(l.LeanDeg-Num(ln,"lean_deg"))<e&&Mathf.Abs(l.FlutterDeg-Num(ln,"flutter_deg"))<e&&Mathf.Abs(l.FlutterHz-Num(ln,"flutter_hz"))<e&&Mathf.Abs(l.GustHz-Num(ln,"gust_hz"))<e
    &&Mathf.Abs(l.GustShare-Num(ln,"gust_share"))<e&&Mathf.Abs(l.SmokeLeanDeg-Num(ln,"smoke_lean_deg"))<e&&Mathf.Abs(l.SmokeSwayM-Num(ln,"smoke_sway_m"))<e
    &&l.StripCount==live.Count(p=>p.Role=="strip")&&l.PuffCount==live.Count(p=>p.Role=="smoke");
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
  // the rows of Stage308_openguide this stage stands on (bands YE and R end on its two prop bays)
  static string Requires(Cfg cfg,Scene scene)
  {
   var rq=Req(cfg.J,"requires");string rootName=Str(rq,"openguide_root");var miss=new List<string>();
   foreach(var id in Arr(rq,"openguide_rows")){var t=Unique(scene,rootName+"/"+id.Value<string>(),out _);if(t==null||t.GetComponentsInChildren<Collider>(true).Length==0)miss.Add(id.Value<string>());}
   return miss.Count==0?null:rootName+" row(s) "+string.Join(", ",miss)+" are not in this scene (with their collider)";
  }
  static MeshRenderer AddMesh(GameObject obj,Mesh mesh,Material[] mats,bool shadows)
  {
   obj.AddComponent<MeshFilter>().sharedMesh=mesh;var mr=obj.AddComponent<MeshRenderer>();mr.sharedMaterials=mats;mr.shadowCastingMode=shadows?ShadowCastingMode.On:ShadowCastingMode.Off;return mr;
  }

  // ---------- apply / revert ----------

  static string Apply(string arg,bool dry)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,!dry);var scene=Open(t);var content=Content(t,scene);Physics.SyncTransforms();_ignore=null;
   var sb=new StringBuilder("Guide2_308 "+(dry?"plan":"scene-apply")+" "+t.Alias+" (data "+Short(cfg.Sha)+" "+(Opt(cfg.J,"version")??"?")+", group "+(Opt(cfg.J,"group")??"")+")\n");
   string need=Requires(cfg,scene);if(need!=null)throw new Refuse(need+" - apply Stage308_openguide first (RUN_ORDER_openguide.md): the closure would have a hole at the old cut road. Nothing written");
   var root=Root(cfg,scene);if(root!=null&&PostLedger308.UnderProtectedTree(root))throw new Refuse("root "+cfg.Root+" is a protected tree name");
   var ledger=ReadLedger("scene_"+t.Alias)??new Ledger{group=Opt(cfg.J,"group")??"",target=t.Scene,created=DateTime.UtcNow.ToString("O")};
   var lines=Near(cfg,content,cfg.R("path_run_gap_m"));
   var todo=new List<Item>();int warns=0,solid=0,same=0,boxes=0;float wallMin=float.PositiveInfinity,underMin=float.PositiveInfinity,lineMin=float.PositiveInfinity,padMin=float.PositiveInfinity,probeMax=0f;string padNear="";
   foreach(var r in Arr(cfg.J,"rows"))
   {
    var it=Build(cfg,scene,r);warns+=it.Warns;foreach(var n in it.Notes)sb.AppendLine("  "+n);
    string fault=WallFault(cfg,it);if(fault!=null)throw new Refuse(fault+" - revise the row. Nothing written");
    probeMax=Mathf.Max(probeMax,it.ProbeGap);
    if(it.Solid)
    {
     Corridor(cfg,content,lines,it,out float line,out float pad,out string near);
     if(line<cfg.R("corridor_clear_m"))throw new Refuse(it.Id+": a collider corner is "+F(line)+" m from a content path centre line (< "+F(cfg.R("corridor_clear_m"),"F1")+") - if this is the old cut road, run Content308 paths-regen:"+t.Alias+" of Stage308_openguide first. Nothing written");
     if(pad<cfg.R("point_collider_pad_m"))throw new Refuse(it.Id+": a collider corner is "+F(pad)+" m from the prompt disc of content point "+near+" (< "+F(cfg.R("point_collider_pad_m"),"F1")+"). Nothing written");
     wallMin=Mathf.Min(wallMin,it.WallMin);underMin=Mathf.Min(underMin,it.UnderMin);lineMin=Mathf.Min(lineMin,line);if(pad<padMin){padMin=pad;padNear=near;}boxes+=Boxes(it).Length;
    }
    var have=root!=null?root.Find(it.Id):null;
    if(have!=null){if(Same(cfg,have,it)){same++;continue;}throw new Refuse(it.Path(cfg)+" is in "+scene.path+" with another pose / mesh / colliders / parts than the data gives - scene-revert:"+t.Alias+" first. Nothing written");}
    todo.Add(it);if(it.Solid)solid++;
   }
   if(root!=null)foreach(Transform c in root)if(!Arr(cfg.J,"rows").Any(r=>Str(r,"id")==c.name))throw new Refuse(cfg.Root+"/"+c.name+" is in the scene and not in the data - scene-revert:"+t.Alias+" with the data that made it first. Nothing written");
   sb.AppendLine("  rows "+Arr(cfg.J,"rows").Count()+": to create "+todo.Count+" ("+todo.Count(i=>Str(i.Row,"part")=="funnel")+" funnel pieces, "+todo.Count(i=>Str(i.Row,"part")=="trace")+" traces, "+todo.Count(i=>i.Kind=="rig")+" rig), in the scene with the same pose "+same);
   sb.AppendLine("  physical ground: "+boxes+" collider boxes, lowest top "+F(wallMin)+" m over the ground (>= "+F(cfg.R("wall_min_m"))+": no lip), shallowest bottom "+F(underMin)+" m under it (>= "+F(cfg.R("wall_under_m"))+"), baked pieces within "+F(probeMax,"F3")+" m of the height field; nearest content path line "+F(lineMin)+" m, nearest prompt disc "+F(padMin)+" m ("+padNear+")");
   if(todo.Count==0)return sb.Append("  변경 없음 (no change; WARN "+warns+")").ToString();
   string bake=solid>0?"; "+solid+" of them carry colliders = NEEDS THE NAVMESH BAKE":"; no collider is added";
   if(dry)return sb.Append("  plan only: nothing written (would create "+todo.Count+" object(s)"+bake+"; WARN "+warns+")").ToString();
   string abs=Harness303.Abs(t.Scene);var write=new Write{utc=DateTime.UtcNow.ToString("O"),target=t.Scene,backup=Backup(t.Scene),shaBefore=Harness303.Sha(abs),dataSha=cfg.Sha};
   try
   {
    if(root==null){var go=new GameObject(cfg.Root);SceneManager.MoveGameObjectToScene(go,scene);root=go.transform;write.rows.Add(new Row{kind="create-root",key=cfg.Root,detail="root"});}
    foreach(var it in todo)
    {
     var obj=new GameObject(it.Id);obj.transform.SetParent(root,false);obj.transform.SetPositionAndRotation(it.Pos,it.Rot);obj.transform.localScale=Vector3.one;
     if(it.Kind=="mesh")
     {
      AddMesh(obj,it.Mesh,it.Mats,it.Shadows);
      var bx=Boxes(it);
      for(int i=0;i<bx.Length;i++)
      {
       var cg=new GameObject("Col_"+i);cg.transform.SetParent(obj.transform,false);cg.transform.localPosition=Vec(bx[i],"center");cg.transform.localRotation=Quaternion.Euler(0,OptNum(bx[i],"yaw",0f),0);
       cg.AddComponent<BoxCollider>().size=Vec(bx[i],"size");cg.isStatic=true;
      }
      obj.isStatic=true;
     }
     else
     {
      var strips=new List<Transform>();var puffs=new List<Transform>();var rise=new List<float>();
      foreach(var p in it.Parts.Where(p=>!p.Skip))
      {
       var c=new GameObject(p.Name);c.transform.SetParent(obj.transform,false);c.transform.position=p.Pos;c.transform.rotation=it.Rot;c.transform.localScale=Vector3.one*p.Scale;
       AddMesh(c,p.Mesh,p.Mats,p.Shadows&&p.Role!="smoke");
       if(p.Role=="strip")strips.Add(c.transform);else if(p.Role=="smoke"){puffs.Add(c.transform);rise.Add(p.Rise);}else c.isStatic=true;
      }
      var ln=it.Row["lean"];
      obj.AddComponent<ShrineLean308>().Configure(true,Vec(ln,"next"),Num(ln,"lean_deg"),Num(ln,"flutter_deg"),Num(ln,"flutter_hz"),Num(ln,"gust_hz"),Num(ln,"gust_share"),Num(ln,"smoke_lean_deg"),Num(ln,"smoke_sway_m"),strips.ToArray(),puffs.ToArray(),rise.ToArray());
     }
     write.rows.Add(new Row{kind="create",key=cfg.Root+"/"+it.Id,detail=it.Id+" at "+V(it.Pos)+(it.Solid?" solid x"+Boxes(it).Length:"")});
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

  static string Revert(string arg)
  {
   var cfg=Load(false);var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,true);var ledger=ReadLedger("scene_"+t.Alias);
   var live=Live(ledger).ToList();
   if(live.Count==0)return "Guide2_308 scene-revert "+t.Alias+": 변경 없음 (no live ledger row)";
   var scene=Open(t);var sb=new StringBuilder("Guide2_308 scene-revert "+t.Alias+"\n");string copy=Backup(t.Scene);int kept=0,removed=0,gone=0;var marked=new List<Row>();
   // the created objects, the root last (only when nothing is left inside)
   foreach(var r in live.Where(x=>x.kind=="create").Concat(live.Where(x=>x.kind=="create-root")))
   {
    var tr=Unique(scene,r.key,out string why);
    if(tr==null){if(why.StartsWith("0 ",StringComparison.Ordinal)){r.reverted=true;marked.Add(r);gone++;}else{kept++;sb.AppendLine("  KEPT "+r.key+": "+why);}continue;}
    if(tr.root.name!=cfg.Root){kept++;sb.AppendLine("  KEPT "+r.key+": not under "+cfg.Root);continue;}
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

  // ---------- check (read-only): AC-U1..U12 ----------

  static string Check(string arg)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,false);var scene=Open(t);var content=Content(t,scene);Physics.SyncTransforms();_ignore=null;int fail=0,pass=0;
   var sb=new StringBuilder("Guide2_308 check "+t.Alias+" (data "+Short(cfg.Sha)+")\n");
   void C(bool ok,string what){if(ok)pass++;else fail++;sb.AppendLine("  ["+(ok?"PASS":"FAIL")+"] "+what);}
   var root=Root(cfg,scene);var want=Arr(cfg.J,"rows").Select(r=>Str(r,"id")).ToList();
   var have=root==null?new List<string>():root.Cast<Transform>().Select(o=>o.name).ToList();
   C(root!=null&&want.All(have.Contains)&&have.Count==want.Count,"AC-U1 "+cfg.Root+" holds exactly the data rows ("+have.Count+" of "+want.Count+")"+(root==null?" - the root is absent: run scene-apply":""));
   string need=Requires(cfg,scene);C(need==null,"AC-U9 the two prop bays of OpenGuide308 that bands YE and R end on are in the scene"+(need==null?"":" - "+need));
   if(root!=null)
   {
    C(scene.GetRootGameObjects().Count(g=>g.name==cfg.Root)==1&&!PostLedger308.UnderProtectedTree(root),"AC-U1 one root named "+cfg.Root+", outside the protected trees");
    C(root.GetComponentsInChildren<Light>(true).Length==0,"AC-U5 Light components under the root: "+root.GetComponentsInChildren<Light>(true).Length+" (no new light)");
    C(root.GetComponentsInChildren<NavMeshAgent>(true).Length==0&&root.GetComponentsInChildren<NavMeshObstacle>(true).Length==0,"AC-U5 NavMeshAgent / NavMeshObstacle under the root: 0");
    var rends=root.GetComponentsInChildren<Renderer>(true);
    C(rends.All(r=>r.sharedMaterials.All(m=>m!=null&&!m.IsKeywordEnabled("_EMISSION"))),"AC-U5 no material under the root has _EMISSION ("+rends.Length+" renderer(s))");
    C(root.GetComponentsInChildren<Canvas>(true).Length==0&&root.GetComponentsInChildren<Camera>(true).Length==0,"AC-U5 no Canvas (text / marker) and no Camera under the root");
    var lines=Near(cfg,content,cfg.R("path_run_gap_m"));
    int rows=0,poseBad=0,meshBad=0,wallBad=0,corrBad=0,probeBad=0,colliders=0,solids=0;float wallMin=float.PositiveInfinity,underMin=float.PositiveInfinity,lineMin=float.PositiveInfinity,padMin=float.PositiveInfinity,probeMax=0f;string padNear="";var bad=new List<string>();
    foreach(var r in Arr(cfg.J,"rows"))
    {
     var obj=root.Find(Str(r,"id"));if(obj==null){poseBad++;bad.Add(Str(r,"id")+" is not in the scene");continue;}
     Item it;try{it=Build(cfg,scene,r);}catch(Refuse e){poseBad++;bad.Add(e.Message);continue;}
     rows++;
     if(!Same(cfg,obj,it)){poseBad++;bad.Add(it.Id+" differs from the data (pose / mesh / colliders / parts / lean)");}
     if(it.Kind=="mesh"){var mf=obj.GetComponent<MeshFilter>();if(mf==null||mf.sharedMesh==null||!EditorUtility.IsPersistent(mf.sharedMesh)||AssetDatabase.GetAssetPath(mf.sharedMesh)!=MeshAsset(cfg,Str(r,"mesh"))){meshBad++;bad.Add(it.Id+": its mesh is not the asset "+MeshAsset(cfg,Str(r,"mesh")));}}
     if(it.Baked){probeMax=Mathf.Max(probeMax,it.ProbeGap);if(it.ProbeGap>cfg.R("baked_y_tol_m")){probeBad++;bad.Add(it.Id+": physical ground "+F(it.ProbeGap,"F3")+" m off the height field");}}
     colliders+=obj.GetComponentsInChildren<BoxCollider>(true).Length;
     if(!it.Solid)continue;
     solids++;wallMin=Mathf.Min(wallMin,it.WallMin);underMin=Mathf.Min(underMin,it.UnderMin);
     string wf=WallFault(cfg,it);if(wf!=null||(!it.Baked&&(it.Slope>Num(r,"max_slope_deg")||it.CornerGap>CornerMax(cfg,r)))){wallBad++;bad.Add(wf??it.Id+": slope "+F(it.Slope,"F1")+"°, corners within "+F(it.CornerGap)+" m");}
     Corridor(cfg,content,lines,it,out float line,out float pad,out string near);lineMin=Mathf.Min(lineMin,line);if(pad<padMin){padMin=pad;padNear=near;}
     if(line<cfg.R("corridor_clear_m")||pad<cfg.R("point_collider_pad_m")){corrBad++;bad.Add(it.Id+": "+F(line)+" m from a content path line, "+F(pad)+" m from the prompt disc of "+near);}
    }
    C(poseBad==0,"AC-U2 "+rows+" row(s) stand where the data"+" and the physical ground put them, with the data's mesh, materials, collider boxes and parts ("+poseBad+" differ)");
    C(meshBad==0,"AC-U3 every mesh is a Mesh asset under "+cfg.AssetDir+" ("+meshBad+" are not)");
    C(root.GetComponentsInChildren<Collider>(true).Length==colliders,"AC-U4 every collider under the root is a data box ("+colliders+" BoxCollider, "+(root.GetComponentsInChildren<Collider>(true).Length-colliders)+" other)");
    C(wallBad==0,"AC-U6 "+solids+" solid row(s): the lowest collider top stands "+F(wallMin)+" m over the physical ground at a corner (>= "+F(cfg.R("wall_min_m"))+": no lip), the shallowest bottom "+F(underMin)+" m under it (>= "+F(cfg.R("wall_under_m"))+": no gap) ("+wallBad+" fail)");
    C(corrBad==0,"AC-U7 collider corners: nearest content path centre line "+F(lineMin)+" m (>= "+F(cfg.R("corridor_clear_m"),"F1")+"), nearest prompt disc "+F(padMin)+" m ("+padNear+", >= "+F(cfg.R("point_collider_pad_m"),"F1")+") ("+corrBad+" fail)");
    C(probeBad==0,"AC-U8 baked pieces: the physical ground under their probes is within "+F(probeMax,"F3")+" m of the height field they were baked to (<= "+F(cfg.R("baked_y_tol_m"))+") ("+probeBad+" fail)");
    foreach(var r in Arr(cfg.J,"rows").Where(r=>Str(r,"kind")=="rig"))
    {
     var obj=root.Find(Str(r,"id"));var l=obj!=null?obj.GetComponent<ShrineLean308>():null;var ln=r["lean"];
     C(l!=null&&l.HasNext&&Near3(l.Next,Vec(ln,"next"),.01f)&&l.StripCount>0,"AC-U10 "+Str(r,"id")+": ShrineLean308 is configured ("+(l==null?"no component":l.StripCount+" strip(s), "+l.PuffCount+" smoke puff(s), lean "+F(l.LeanDeg,"F0")+"° ± "+F(l.FlutterDeg,"F0")+"° toward "+V(l.Next))+")");
     if(obj!=null)
     {
      var to=ShrineLean308.Toward(obj.position,Vec(ln,"next"));var hang=ShrineLean308.Hang(to,Num(ln,"lean_deg"),0f);
      C(to!=Vector3.zero&&Vector3.Dot(hang,to)>0f&&hang.y<0f,"AC-U10 "+Str(r,"id")+": a strip at rest leans "+F(Vector3.Angle(Vector3.down,hang),"F0")+"° toward bearing "+F(Mathf.Repeat(Mathf.Atan2(to.x,to.z)*Mathf.Rad2Deg,360f),"F0")+" (the next place "+(Opt(ln,"next_id")??"")+")");
      var smoke=obj.GetComponentsInChildren<MeshRenderer>(true).Where(m=>m.name.StartsWith("Smoke_",StringComparison.Ordinal)).ToArray();
      C(smoke.All(m=>m.shadowCastingMode==ShadowCastingMode.Off),"AC-U11 "+Str(r,"id")+": "+smoke.Length+" smoke puff(s), none casts a shadow");
      int planned=Arr(r,"parts").Count(p=>Str(p,"role")=="smoke");
      C(smoke.Length==planned,"AC-U11 "+Str(r,"id")+": every smoke part found its candle ("+smoke.Length+" of "+planned+")");
     }
    }
    C(scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<ShrineLean308>(true)).All(l=>l.transform.root==root),"AC-U12 ShrineLean308 exists only under "+cfg.Root+" (off everywhere else)");
    foreach(var b in bad.Take(12))sb.AppendLine("    - "+b);if(bad.Count>12)sb.AppendLine("    - ... "+(bad.Count-12)+" more");
    sb.AppendLine("  colliders that the NavMesh bake must see: "+solids+" solid row(s), "+colliders+" boxes");
   }
   if(scene.isDirty)sb.AppendLine("  WARN the scene is dirty after a read-only pass (bug): reload it");
   return sb.Append("  "+(fail==0?"no FAIL":"FAIL "+fail)+" (PASS "+pass+")").ToString();
  }

  // ---------- status ----------

  static string Status()
  {
   var sb=new StringBuilder("Guide2_308 status (play="+EditorApplication.isPlaying+")\n");
   Cfg cfg;try{cfg=Load(false);}catch(Refuse r){return sb.Append("  "+r.Message).ToString();}
   var rows=Arr(cfg.J,"rows").ToList();
   sb.AppendLine("  data "+DataFile+" sha "+Short(cfg.Sha)+" version "+(Opt(cfg.J,"version")??"?")+", group "+(Opt(cfg.J,"group")??"")+": "+rows.Count+" row(s) = "+rows.Count(r=>Opt(r,"part")=="funnel")+" funnel pieces + "+rows.Count(r=>Opt(r,"part")=="trace")+" traces + "+rows.Count(r=>Str(r,"kind")=="rig")+" rig; "+rows.Count(r=>Flag(r,"solid"))+" with colliders (bake)");
   int ok=Arr(cfg.J,"meshes").Count(m=>MeshMatches(cfg,AssetDatabase.LoadAssetAtPath<Mesh>(MeshAsset(cfg,Str(m,"name"))),m));
   sb.AppendLine("  mesh assets in place "+ok+"/"+Arr(cfg.J,"meshes").Count()+" under "+cfg.AssetDir+"; mesh json "+Arr(cfg.J,"meshes").Count(m=>JsonState(cfg,m)==null)+"/"+Arr(cfg.J,"meshes").Count()+" as the data says; new materials in place "+Arr(cfg.J,"new_materials").Count(n=>AssetDatabase.LoadAssetAtPath<Material>(MatAsset(cfg,n))!=null)+"/"+Arr(cfg.J,"new_materials").Count()+"; live asset ledger rows "+Live(ReadLedger("assets")).Count());
   foreach(var t in cfg.Targets)sb.AppendLine("  "+t.Alias+": live scene ledger rows "+Live(ReadLedger("scene_"+t.Alias)).Count());
   var active=SceneManager.GetActiveScene();var root=Root(cfg,active);
   return sb.Append("  active scene "+active.path+(active.isDirty?" (dirty)":"")+": "+cfg.Root+" "+(root==null?"absent":"present, "+root.childCount+" object(s)")+"; requires "+(Requires(cfg,active)??"OpenGuide308 prop bays present")).ToString();
  }
 }
}
