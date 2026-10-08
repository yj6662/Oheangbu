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
 // #308 realm dressing + taint look (D308-36; SPEC-WORLD-REALMS-DRESSING-308, PROPOSED) [TEST values throughout]. WORLD DRESSING ONLY:
 // no person, no dialogue, no text, no content row, no NavMesh, no road / terrain / vegetation-sheet change. Every pose, path, colour and
 // limit comes from Tools/Unity/Stage308_realms/Data/realms308.json (the code holds none).
 // A row = one object under <root>/<group>: plain MeshFilter / MeshRenderer parts that SHARE an existing mesh (the renderers of an existing
 // prefab, an existing Mesh asset, or the built-in Quad). No mesh is baked, no prefab script / collider comes along. A kind may carry ONE
 // BoxCollider (big lumps only). Nothing glows: a material with _EMISSION refuses the load.
 // Queue-safe: Run(string) never opens a dialog; refusals come back as "REFUSED ...". Only the target scene (SaveScene) and the materials
 // the data names under made_materials (CreateAsset / SaveAssetIfDirty) are saved - never AssetDatabase.SaveAssets.
 //   status | probe:<x>,<z>,<r> | ground:<x>,<z>[;<x>,<z>...]
 //   assets-plan | assets-apply | assets-revert          the made_materials{} (once; shared by the three scenes)
 //   plan:<scene> | scene-apply:<scene> | scene-revert:<scene> | check:<scene>
 //   eyes:<label>                                         the Presentation297 shot lines of stills[] on the physical ground of the open scene
 // scene-apply REBUILDS this tool's roots from the data (the roots hold nothing else), after a byte backup of the scene file.
 public static class Realms308
 {
  const string Usage="status | show:on|off | probe:<x>,<z>,<r> | ground:<x>,<z>[;...] | assets-plan | assets-apply | assets-revert | plan:<scene> | scene-apply:<scene> | scene-revert:<scene> | check:<scene> | eyes:<label>";
  static string StageDir=>Path.Combine(Harness303.RepoRoot,"Tools","Unity","Stage308_realms");
  static string DataFile=>Path.Combine(StageDir,"Data","realms308.json");
  sealed class Refuse:Exception{public Refuse(string m):base(m){}}
  static readonly CultureInfo Inv=CultureInfo.InvariantCulture;

  public static string Run(string command)
  {
   string c=(command??"").Trim();
   try
   {
    if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Edit mode only (Play is running)";
    if(EditorApplication.isCompiling)return "REFUSED scripts are compiling";
    if(c=="status")return Status();
    if(c=="assets-plan")return Assets("plan");
    if(c=="assets-apply")return Assets("apply");
    if(c=="assets-revert")return Assets("revert");
    string a;
    if((a=Arg(c,"probe:"))!=null)return Probe(a);
    if((a=Arg(c,"ground:"))!=null)return GroundAt(a);
    if((a=Arg(c,"plan:"))!=null)return Apply(a,true);
    if((a=Arg(c,"scene-apply:"))!=null)return Apply(a,false);
    if((a=Arg(c,"scene-revert:"))!=null)return Revert(a);
    if((a=Arg(c,"check:"))!=null)return Check(a);
    if((a=Arg(c,"eyes:"))!=null)return Eyes(a);
    if((a=Arg(c,"show:"))!=null)return Show(a);
   }
   catch(Refuse r){return "REFUSED "+r.Message;}
   catch(Exception e){return "FAILED "+e;}
   return "REFUSED unknown Realms308 command '"+c+"' ("+Usage+")";
  }
  static string Arg(string c,string head)=>c.StartsWith(head,StringComparison.Ordinal)?c.Substring(head.Length).Trim():null;

  // ---------- data ----------

  sealed class Target{public string Alias,Scene;}
  sealed class PartDef{public Mesh Mesh;public Material[] Mats;public Vector3 Pos;public Quaternion Rot=Quaternion.identity;public Vector3 Scale=Vector3.one;public int Lod=-1;}
  sealed class Kind
  {
   public string Name;public List<PartDef> Parts=new List<PartDef>();public float[] LodScreen;public bool Shadows,HasCollider;public Vector3 ColCenter,ColSize;public string Source;
   public long NearTris;public Bounds Local;public bool SeatBase;
  }
  sealed class Cfg
  {
   public JObject J;public string Sha;public Target[] Targets;
   public readonly Dictionary<string,Material> Mats=new Dictionary<string,Material>(StringComparer.Ordinal);
   public readonly Dictionary<string,Kind> Kinds=new Dictionary<string,Kind>(StringComparer.Ordinal);
   public JToken Rules=>J["rules"];public float R(string k)=>Num(Rules,k);
   public string[] Roots=>Arr(J,"groups").Select(g=>Str(g,"root")).Distinct().ToArray();
  }
  static JToken Req(JToken o,string key){var t=o?[key];if(t==null||t.Type==JTokenType.Null)throw new Refuse("realms308.json: missing '"+key+"' in "+(o==null?"(null)":string.IsNullOrEmpty(o.Path)?"(root)":o.Path));return t;}
  static float Num(JToken o,string key)=>Req(o,key).Value<float>();
  static float OptNum(JToken o,string key,float fallback){var t=o?[key];return t==null||t.Type==JTokenType.Null?fallback:t.Value<float>();}
  static string Str(JToken o,string key)=>Req(o,key).Value<string>();
  static string Opt(JToken o,string key){var t=o?[key];return t==null||t.Type==JTokenType.Null?null:t.Value<string>();}
  static bool Flag(JToken o,string key,bool fallback=false){var t=o?[key];return t!=null&&t.Type==JTokenType.Boolean?t.Value<bool>():fallback;}
  static IEnumerable<JToken> Arr(JToken o,string key){var t=o?[key] as JArray;return t??(IEnumerable<JToken>)Array.Empty<JToken>();}
  static Vector3 Vec(JToken o,string key){var a=Req(o,key) as JArray;if(a==null||a.Count<3)throw new Refuse("realms308.json: "+key+" needs 3 numbers");return new Vector3(a[0].Value<float>(),a[1].Value<float>(),a[2].Value<float>());}
  static Vector3 OptVec(JToken o,string key,Vector3 fallback){var a=o?[key] as JArray;return a==null||a.Count<3?fallback:new Vector3(a[0].Value<float>(),a[1].Value<float>(),a[2].Value<float>());}
  static string F(float v,string f="F2")=>v.ToString(f,Inv);
  static string V(Vector3 v)=>"("+F(v.x)+", "+F(v.y)+", "+F(v.z)+")";
  static string Short(string sha)=>string.IsNullOrEmpty(sha)?"-":sha.Substring(0,Math.Min(12,sha.Length)).ToLowerInvariant();
  static bool Protected(string p)=>Harness303.IsProtected(p)||PostLedger308.IsProtectedPath(p);
  static bool Glows(Material m)=>m!=null&&(m.IsKeywordEnabled("_EMISSION")||(m.HasProperty("_EmissionColor")&&m.IsKeywordEnabled("_EMISSION")));

  static Cfg Load(bool needMade=true)
  {
   string f=DataFile;if(!File.Exists(f))throw new Refuse("data missing: "+f);
   JObject j;try{j=JObject.Parse(File.ReadAllText(f));}catch(Exception e){throw new Refuse("data does not parse: "+f+" ("+e.Message+")");}
   var cfg=new Cfg{J=j,Sha=Harness303.Sha(f)};
   foreach(string key in new[]{"rules","targets","materials","made_materials","kinds","groups"})Req(j,key);
   foreach(string key in new[]{"float_tol_m","pose_tol_m","pose_tol_deg","sample_step_m"})Num(cfg.Rules,key);
   cfg.Targets=Arr(j,"targets").Select(t=>new Target{Alias=Str(t,"alias"),Scene=Str(t,"scene")}).ToArray();
   if(cfg.Targets.Length==0)throw new Refuse("realms308.json: targets[] is empty");
   foreach(var t in cfg.Targets)if(Protected(t.Scene))throw new Refuse("protected scene in data: "+t.Scene);
   foreach(string r in cfg.Roots)if(PostLedger308.IsProtectedPath("/"+r+"/"))throw new Refuse("root "+r+" is a protected tree name");
   foreach(var kv in (JObject)j["materials"])
   {
    string path=Str(kv.Value,"path");var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m==null)throw new Refuse("material '"+kv.Key+"' does not load: "+path);
    if(Glows(m))throw new Refuse("material '"+kv.Key+"' ("+path+") has _EMISSION: nothing in this stage may glow");
    cfg.Mats[kv.Key]=m;
   }
   foreach(var kv in (JObject)j["made_materials"])
   {
    string path=Str(kv.Value,"path");if(Protected(path))throw new Refuse("made material under a protected path: "+path);
    var m=AssetDatabase.LoadAssetAtPath<Material>(path);
    if(m==null){if(needMade)throw new Refuse("made material '"+kv.Key+"' is not there yet ("+path+") - run assets-apply first");continue;}
    if(Glows(m))throw new Refuse("made material '"+kv.Key+"' glows");
    cfg.Mats[kv.Key]=m;
   }
   foreach(var kv in (JObject)j["kinds"])cfg.Kinds[kv.Key]=BuildKind(cfg,kv.Key,kv.Value,needMade);
   foreach(var g in Arr(j,"groups"))
   {
    string gid=Str(g,"id");Str(g,"root");var ids=new HashSet<string>(StringComparer.Ordinal);
    if(gid.IndexOf('/')>=0)throw new Refuse("group id '"+gid+"': no '/'");
    foreach(var r in Arr(g,"rows"))
    {
     string id=Str(r,"id");if(!ids.Add(id))throw new Refuse("row id '"+id+"' is used twice in group "+gid);
     if(id.IndexOf('/')>=0||id.IndexOf('#')>=0)throw new Refuse("row id '"+id+"': no '/' or '#'");
     if(!cfg.Kinds.ContainsKey(Str(r,"kind")))throw new Refuse("row "+id+": kind '"+Str(r,"kind")+"' is not in kinds{}");
     Num(r,"x");Num(r,"z");
     string mk=Opt(r,"mat");if(mk!=null&&needMade&&!cfg.Mats.ContainsKey(mk))throw new Refuse("row "+id+": material '"+mk+"' is not in materials{} / made_materials{}");
    }
   }
   return cfg;
  }

  static Material[] MatsOf(Cfg cfg,string kind,IEnumerable<JToken> keys,bool needMade)
  {
   var l=new List<Material>();
   foreach(var k in keys){string key=k.Value<string>();if(cfg.Mats.TryGetValue(key,out var m))l.Add(m);else if(needMade)throw new Refuse("kind "+kind+": material '"+key+"' is not in materials{} / made_materials{}");else l.Add(null);}
   return l.ToArray();
  }
  static long Tris(Mesh m){long n=0;if(m==null)return 0;for(int i=0;i<m.subMeshCount;i++)n+=m.GetIndexCount(i)/3;return n;}

  // a kind = shared-mesh parts in the local space of the row object
  static Kind BuildKind(Cfg cfg,string name,JToken k,bool needMade)
  {
   var kind=new Kind{Name=name,Shadows=Flag(k,"shadows",true)};
   string prefab=Opt(k,"prefab"),builtin=Opt(k,"builtin");string over=Opt(k,"mat");Material overMat=null;
   if(over!=null&&!cfg.Mats.TryGetValue(over,out overMat)&&needMade)throw new Refuse("kind "+name+": material '"+over+"' is not in materials{} / made_materials{}");
   var partRot=Quaternion.Euler(OptVec(k,"part_euler",Vector3.zero));var partScale=OptVec(k,"part_scale",Vector3.one);var partPos=OptVec(k,"part_offset",Vector3.zero);
   if(prefab!=null)
   {
    if(Protected(prefab))throw new Refuse("kind "+name+": prefab under a protected path");
    var go=AssetDatabase.LoadAssetAtPath<GameObject>(prefab);if(go==null)throw new Refuse("kind "+name+": prefab does not load: "+prefab);
    kind.Source=prefab;var root=go.transform;var top=Matrix4x4.TRS(partPos,partRot*root.localRotation,Vector3.Scale(partScale,root.localScale))*root.worldToLocalMatrix;
    var lg=go.GetComponentInChildren<LODGroup>(false);var lodOf=new Dictionary<Renderer,int>();
    if(lg!=null){var lods=lg.GetLODs();kind.LodScreen=lods.Select(l=>l.screenRelativeTransitionHeight).ToArray();for(int i=0;i<lods.Length;i++)foreach(var r in lods[i].renderers)if(r!=null&&!lodOf.ContainsKey(r))lodOf[r]=i;}
    int maxLod=Mathf.RoundToInt(OptNum(k,"max_lods",8f));
    foreach(var mr in go.GetComponentsInChildren<MeshRenderer>(false))
    {
     var mf=mr.GetComponent<MeshFilter>();if(mf==null||mf.sharedMesh==null||!mr.enabled)continue;
     int lod=lodOf.TryGetValue(mr,out int li)?li:-1;if(lod>=maxLod)continue;
     var m=top*mr.transform.localToWorldMatrix;
     var mats=overMat!=null?Enumerable.Repeat(overMat,mr.sharedMaterials.Length).ToArray():mr.sharedMaterials;
     if(mats.Any(x=>x==null))throw new Refuse("kind "+name+": the prefab has an empty material slot ("+mr.name+")");
     foreach(var x in mats)if(Glows(x))throw new Refuse("kind "+name+": material "+x.name+" has _EMISSION (give the kind a 'mat' override or pick another prefab)");
     kind.Parts.Add(new PartDef{Mesh=mf.sharedMesh,Mats=mats,Pos=m.GetColumn(3),Rot=m.rotation,Scale=m.lossyScale,Lod=lod});
    }
    if(kind.LodScreen!=null&&kind.LodScreen.Length>maxLod)kind.LodScreen=kind.LodScreen.Take(maxLod).ToArray();
    if(kind.Parts.Count==0)throw new Refuse("kind "+name+": the prefab has no MeshRenderer");
    if(kind.Parts.All(p=>p.Lod<0))kind.LodScreen=null;
   }
   else if(builtin!=null)
   {
    var mesh=Resources.GetBuiltinResource<Mesh>(builtin+".fbx");if(mesh==null)throw new Refuse("kind "+name+": no built-in mesh '"+builtin+"'");
    kind.Source="builtin:"+builtin;kind.Parts.Add(new PartDef{Mesh=mesh,Mats=overMat!=null?new[]{overMat}:MatsOf(cfg,name,Arr(k,"mats"),needMade),Pos=partPos,Rot=partRot,Scale=partScale});
   }
   else
   {
    var lods=Arr(k,"lods").ToArray();if(lods.Length==0)throw new Refuse("kind "+name+": needs 'prefab', 'builtin' or 'lods'");
    kind.Source=Str(lods[0],"mesh");
    for(int i=0;i<lods.Length;i++)
    {
     string mp=Str(lods[i],"mesh");var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(mp);if(mesh==null)throw new Refuse("kind "+name+": mesh does not load: "+mp);
     var mats=MatsOf(cfg,name,Arr(lods[i],"mats"),needMade);if(mats.Length!=mesh.subMeshCount)throw new Refuse("kind "+name+": "+mp+" has "+mesh.subMeshCount+" submesh(es), the data names "+mats.Length+" material(s)");
     if(overMat!=null)mats=Enumerable.Repeat(overMat,mats.Length).ToArray();
     kind.Parts.Add(new PartDef{Mesh=mesh,Mats=mats,Pos=partPos,Rot=partRot,Scale=partScale,Lod=lods.Length>1?i:-1});
    }
    if(lods.Length>1){kind.LodScreen=Arr(k,"lod_screen").Select(x=>x.Value<float>()).ToArray();if(kind.LodScreen.Length!=lods.Length)throw new Refuse("kind "+name+": lod_screen needs "+lods.Length+" entries");}
   }
   foreach(var p in kind.Parts)if(p.Lod<=0)kind.NearTris+=Tris(p.Mesh);
   // the kind's box in the row object's local space (the nearest LOD's parts)
   bool first=true;
   foreach(var p in kind.Parts)
   {
    if(p.Lod>0)continue;var m=Matrix4x4.TRS(p.Pos,p.Rot,p.Scale);var b=p.Mesh.bounds;
    for(int i=0;i<8;i++){var c=m.MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));if(first){kind.Local=new Bounds(c,Vector3.zero);first=false;}else kind.Local.Encapsulate(c);}
   }
   kind.SeatBase=Opt(k,"seat")=="base";
   var col=k["collider"];
   if(col!=null&&col.Type==JTokenType.String&&col.Value<string>()=="auto"){kind.HasCollider=true;float sh=OptNum(k,"collider_shrink",1f);kind.ColCenter=kind.Local.center;kind.ColSize=Vector3.Scale(kind.Local.size,new Vector3(sh,1f,sh));}
   else if(col!=null&&col.Type!=JTokenType.Null){kind.HasCollider=true;kind.ColCenter=Vec(col,"center");kind.ColSize=Vec(col,"size");}
   return kind;
  }

  static Target TargetOf(Cfg cfg,string arg)
  {
   arg=(arg??"").Trim().Replace('\\','/');
   var t=cfg.Targets.FirstOrDefault(x=>string.Equals(x.Alias,arg,StringComparison.OrdinalIgnoreCase)||x.Scene==arg);
   if(t==null)throw new Refuse("not a realms308 target: '"+arg+"' (allowed: "+string.Join(", ",cfg.Targets.Select(x=>x.Alias))+")");
   return t;
  }
  static Scene Open(Target t)
  {
   for(int i=0;i<SceneManager.sceneCount;i++){var s=SceneManager.GetSceneAt(i);if(s.isDirty)throw new Refuse("scene "+s.path+" has unsaved changes that are not this tool's - nothing written, nothing saved");}
   var active=SceneManager.GetActiveScene();
   if(active.path==t.Scene&&SceneManager.sceneCount==1)return active;
   var opened=EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);EditorUtility.UnloadUnusedAssetsImmediate(true);GC.Collect();return opened;
  }

  // ---------- ground ----------

  static bool Under(Transform t,HashSet<string> roots){for(var p=t;p!=null;p=p.parent)if(p.parent==null&&roots.Contains(p.name))return true;return false;}
  static bool Preview(Transform t){for(var p=t;p!=null;p=p.parent)if((p.gameObject.hideFlags&HideFlags.DontSave)!=0)return true;return false;}
  static bool TerrainLike(Collider c){if(c is TerrainCollider)return true;for(var t=c.transform;t!=null;t=t.parent){var n=t.name;if(n.Contains("Terrain")||n.Contains("Surface"))return true;}return false;}
  static HashSet<string> IgnoreRoots(Cfg cfg){var h=new HashSet<string>(Arr(cfg.Rules,"ground_ignore_roots").Select(v=>v.Value<string>()));foreach(string r in cfg.Roots)h.Add(r);return h;}
  static bool Ground(HashSet<string> ignore,float x,float z,out Vector3 p,out Vector3 n)
  {
   p=default;n=Vector3.up;bool any=false;
   foreach(var h in Physics.RaycastAll(new Vector3(x,3000f,z),Vector3.down,6000f,~0,QueryTriggerInteraction.Ignore))
   {
    var c=h.collider;if(c==null||h.normal.y<=.3f||c is CharacterController||Preview(c.transform)||Under(c.transform,ignore)||!TerrainLike(c))continue;
    if(!any||h.point.y>p.y){p=h.point;n=h.normal;any=true;}
   }
   return any;
  }
  static string GroundAt(string arg)
  {
   var cfg=Load(false);var ig=IgnoreRoots(cfg);Physics.SyncTransforms();var sb=new StringBuilder("Realms308 ground ("+SceneManager.GetActiveScene().path+")\n");
   foreach(string pair in arg.Split(';'))
   {
    var a=pair.Split(',');if(a.Length<2)continue;float x=float.Parse(a[0],Inv),z=float.Parse(a[1],Inv);
    sb.AppendLine(Ground(ig,x,z,out var p,out var n)?"  "+F(x,"F1")+", "+F(z,"F1")+": y "+F(p.y)+" normal "+V(n):"  "+F(x,"F1")+", "+F(z,"F1")+": no terrain");
   }
   return sb.ToString();
  }

  static string PathOf(Transform t,int depth){var names=new List<string>();for(var p=t;p!=null;p=p.parent)names.Add(p.name);names.Reverse();return string.Join("/",names.Take(depth));}
  static string Probe(string arg)
  {
   var a=arg.Split(',');float x=float.Parse(a[0],Inv),z=float.Parse(a[1],Inv),r=float.Parse(a[2],Inv);int depth=a.Length>3?int.Parse(a[3],Inv):3;
   var cfg=Load(false);var ig=IgnoreRoots(cfg);Physics.SyncTransforms();var scene=SceneManager.GetActiveScene();
   var sb=new StringBuilder("Realms308 probe "+F(x,"F0")+", "+F(z,"F0")+" r "+F(r,"F0")+" in "+scene.path+"\n");
   sb.AppendLine(Ground(ig,x,z,out var gp,out var gn)?"  ground y "+F(gp.y)+" normal "+V(gn):"  no terrain under the centre");
   var groups=new Dictionary<string,List<Renderer>>();
   foreach(var go in scene.GetRootGameObjects())foreach(var rr in go.GetComponentsInChildren<Renderer>(false))
   {
    var b=rr.bounds;float dx=Mathf.Max(0,Mathf.Abs(b.center.x-x)-b.extents.x),dz=Mathf.Max(0,Mathf.Abs(b.center.z-z)-b.extents.z);if(dx*dx+dz*dz>r*r)continue;
    string key=PathOf(rr.transform,depth);if(!groups.TryGetValue(key,out var l))groups[key]=l=new List<Renderer>();l.Add(rr);
   }
   foreach(var kv in groups.OrderByDescending(k=>k.Value.Count).Take(60))
   {
    var b=kv.Value[0].bounds;foreach(var rr in kv.Value)b.Encapsulate(rr.bounds);var first=kv.Value[0];var mf=first.GetComponent<MeshFilter>();
    sb.AppendLine("  "+kv.Value.Count+"x "+kv.Key+" | bounds y "+F(b.min.y,"F1")+".."+F(b.max.y,"F1")+" centre "+V(b.center)+" size "+V(b.size)+" | "+first.name+" mesh "+(mf!=null&&mf.sharedMesh!=null?mf.sharedMesh.name:"-")+" mat "+string.Join(",",first.sharedMaterials.Where(m=>m!=null).Select(m=>m.name+"<"+m.shader.name+">").Take(3)));
   }
   return sb.ToString();
  }

  // ---------- made materials ----------

  static string Assets(string mode)
  {
   var cfg=Load(false);var sb=new StringBuilder("Realms308 assets-"+mode+" (data "+Short(cfg.Sha)+")\n");int n=0;
   foreach(var kv in (JObject)cfg.J["made_materials"])
   {
    string path=Str(kv.Value,"path");var have=AssetDatabase.LoadAssetAtPath<Material>(path);
    if(mode=="revert"){if(have==null)sb.AppendLine("  "+path+": already gone");else if(AssetDatabase.DeleteAsset(path)){sb.AppendLine("  deleted "+path);n++;}else sb.AppendLine("  KEPT "+path);continue;}
    var shader=Shader.Find(Str(kv.Value,"shader"));if(shader==null)throw new Refuse("made material '"+kv.Key+"': no shader '"+Str(kv.Value,"shader")+"'");
    string tp=Opt(kv.Value,"texture");Texture tex=null;if(tp!=null){tex=AssetDatabase.LoadAssetAtPath<Texture>(tp);if(tex==null)throw new Refuse("made material '"+kv.Key+"': texture does not load: "+tp);}
    if(mode=="plan"){sb.AppendLine("  "+(have==null?"create ":"update ")+path+" ("+shader.name+(tp!=null?", "+tp:"")+")");n++;continue;}
    string dir=Path.GetDirectoryName(path).Replace('\\','/');string acc="";
    foreach(string part in dir.Split('/')){string parent=acc;acc=acc.Length==0?part:acc+"/"+part;if(!AssetDatabase.IsValidFolder(acc)&&string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent,part)))throw new Refuse("could not create folder "+acc);}
    var m=have!=null?have:new Material(shader);if(m.shader!=shader)m.shader=shader;
    var col=Arr(kv.Value,"colour").Select(v=>v.Value<float>()).ToArray();var c=new Color(col[0],col[1],col[2],col.Length>3?col[3]:1f);
    if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",c);if(m.HasProperty("_Color"))m.SetColor("_Color",c);
    if(tex!=null){if(m.HasProperty("_BaseMap"))m.SetTexture("_BaseMap",tex);if(m.HasProperty("_MainTex"))m.SetTexture("_MainTex",tex);}
    if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",OptNum(kv.Value,"smoothness",0f));
    if(m.HasProperty("_Metallic"))m.SetFloat("_Metallic",0f);
    if(m.HasProperty("_SpecularHighlights")){m.SetFloat("_SpecularHighlights",0f);m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");}
    if(m.HasProperty("_EnvironmentReflections")){m.SetFloat("_EnvironmentReflections",0f);m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");}
    m.DisableKeyword("_EMISSION");m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.EmissiveIsBlack;
    if(Flag(kv.Value,"transparent"))
    {
     m.SetFloat("_Surface",1f);m.SetFloat("_Blend",0f);m.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);m.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
     if(m.HasProperty("_SrcBlendAlpha"))m.SetFloat("_SrcBlendAlpha",(float)BlendMode.One);if(m.HasProperty("_DstBlendAlpha"))m.SetFloat("_DstBlendAlpha",(float)BlendMode.OneMinusSrcAlpha);
     m.SetFloat("_ZWrite",0f);m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.SetOverrideTag("RenderType","Transparent");
     m.SetShaderPassEnabled("DepthOnly",false);m.SetShaderPassEnabled("DepthNormals",false);m.SetShaderPassEnabled("DepthNormalsOnly",false);m.SetShaderPassEnabled("ShadowCaster",false);
    }
    if(m.HasProperty("_Cull"))m.SetFloat("_Cull",OptNum(kv.Value,"cull",2f));
    m.renderQueue=Mathf.RoundToInt(OptNum(kv.Value,"queue",Flag(kv.Value,"transparent")?3000f:2000f));
    if(have==null)AssetDatabase.CreateAsset(m,path);else EditorUtility.SetDirty(m);
    AssetDatabase.SaveAssetIfDirty(m);sb.AppendLine("  "+(have==null?"created ":"updated ")+path+" (guid "+AssetDatabase.AssetPathToGUID(path)+")");n++;
   }
   return sb.Append("  "+mode+": "+n+" material(s)").ToString();
  }

  // ---------- plan ----------

  sealed class Item{public string Group,Root,Id;public JToken Row;public Kind K;public Vector3 Pos,Scale;public Quaternion Rot;public Material Over;public float GroundY;}
  static IEnumerable<JToken> Groups(Cfg cfg)=>Arr(cfg.J,"groups").Where(g=>Flag(g,"enabled",true));
  static Item Build(Cfg cfg,HashSet<string> ig,JToken g,JToken r)
  {
   var it=new Item{Group=Str(g,"id"),Root=Str(g,"root"),Id=Str(r,"id"),Row=r,K=cfg.Kinds[Str(r,"kind")]};float x=Num(r,"x"),z=Num(r,"z");
   if(!Ground(ig,x,z,out var gp,out var gn))throw new Refuse("no terrain under "+it.Group+"/"+it.Id+" ("+F(x,"F1")+", "+F(z,"F1")+") - is the scene loaded?");
   it.GroundY=gp.y;var yTok=r["y"];float y=yTok!=null&&yTok.Type!=JTokenType.Null?yTok.Value<float>():gp.y+OptNum(r,"dy",0f);
   var e=Quaternion.Euler(OptNum(r,"pitch",0f),OptNum(r,"yaw",0f),OptNum(r,"roll",0f));
   it.Rot=Flag(r,"align")?Quaternion.FromToRotation(Vector3.up,gn)*e:e;
   float s=OptNum(r,"s",1f);it.Scale=Vector3.Scale(OptVec(r,"scale",Vector3.one),new Vector3(s,s,s));
   if(it.K.SeatBase)y-=it.K.Local.min.y*it.Scale.y;
   it.Pos=new Vector3(x,y,z);
   if(Flag(r,"align"))it.Pos+=gn*OptNum(r,"lift",0f);
   string mk=Opt(r,"mat");if(mk!=null)it.Over=cfg.Mats[mk];
   return it;
  }
  static List<Item> Plan(Cfg cfg)
  {
   var ig=IgnoreRoots(cfg);Physics.SyncTransforms();var l=new List<Item>();
   foreach(var g in Groups(cfg))foreach(var r in Arr(g,"rows"))l.Add(Build(cfg,ig,g,r));
   return l;
  }
  static GameObject Make(Transform holder,Item it)
  {
   var k=it.K;var obj=new GameObject(it.Id);obj.transform.SetParent(holder,false);obj.transform.SetPositionAndRotation(it.Pos,it.Rot);obj.transform.localScale=it.Scale;
   var made=new List<(PartDef,Renderer)>();int n=0;
   foreach(var p in k.Parts)
   {
    var go=new GameObject(p.Lod>=0?"L"+p.Lod+"_"+n:"P"+n);n++;go.transform.SetParent(obj.transform,false);go.transform.localPosition=p.Pos;go.transform.localRotation=p.Rot;go.transform.localScale=p.Scale;
    go.AddComponent<MeshFilter>().sharedMesh=p.Mesh;var mr=go.AddComponent<MeshRenderer>();
    mr.sharedMaterials=it.Over!=null?Enumerable.Repeat(it.Over,p.Mats.Length).ToArray():p.Mats;
    mr.shadowCastingMode=k.Shadows&&p.Lod<=0?ShadowCastingMode.On:ShadowCastingMode.Off;mr.lightProbeUsage=LightProbeUsage.Off;mr.reflectionProbeUsage=ReflectionProbeUsage.Off;
    made.Add((p,mr));
   }
   if(k.LodScreen!=null)
   {
    var lods=new LOD[k.LodScreen.Length];
    for(int i=0;i<lods.Length;i++)lods[i]=new LOD(k.LodScreen[i],made.Where(m=>m.Item1.Lod==i).Select(m=>m.Item2).ToArray());
    var group=obj.AddComponent<LODGroup>();group.SetLODs(lods);group.RecalculateBounds();
   }
   if(k.HasCollider){var cg=new GameObject("Col");cg.transform.SetParent(obj.transform,false);cg.transform.localPosition=k.ColCenter;cg.AddComponent<BoxCollider>().size=k.ColSize;}
   return obj;
  }
  static IEnumerable<GameObject> OwnRoots(Cfg cfg,Scene s){var names=new HashSet<string>(cfg.Roots);return s.GetRootGameObjects().Where(g=>names.Contains(g.name)&&!Preview(g.transform));}
  static string Counts(List<Item> items)
  {
   long tris=0;int parts=0,cols=0;var meshes=new HashSet<Mesh>();
   foreach(var it in items){tris+=it.K.NearTris;parts+=it.K.Parts.Count;if(it.K.HasCollider)cols++;foreach(var p in it.K.Parts)meshes.Add(p.Mesh);}
   return items.Count+" row object(s), "+parts+" MeshRenderer(s), "+meshes.Count+" distinct shared mesh(es), near triangles "+tris.ToString("N0",Inv)+", BoxCollider(s) "+cols;
  }

  static string Status()
  {
   var cfg=Load(false);var sb=new StringBuilder("Realms308 status (data "+Short(cfg.Sha)+" "+(Opt(cfg.J,"version")??"?")+")\n");
   foreach(var g in Arr(cfg.J,"groups"))sb.AppendLine("  group "+Str(g,"root")+"/"+Str(g,"id")+(Flag(g,"enabled",true)?"":" (off)")+": "+Arr(g,"rows").Count()+" row(s)");
   foreach(var kv in cfg.Kinds)sb.AppendLine("  kind "+kv.Key+": "+kv.Value.Parts.Count+" part(s), near triangles "+kv.Value.NearTris+", lods "+(kv.Value.LodScreen?.Length??0)+(kv.Value.HasCollider?", box":"")+", local y "+F(kv.Value.Local.min.y)+".."+F(kv.Value.Local.max.y)+" size "+V(kv.Value.Local.size)+" centre "+V(kv.Value.Local.center)+" <- "+kv.Value.Source);
   foreach(var kv in (JObject)cfg.J["made_materials"])sb.AppendLine("  made material "+kv.Key+": "+(AssetDatabase.LoadAssetAtPath<Material>(Str(kv.Value,"path"))!=null?"in place":"absent"));
   var s=SceneManager.GetActiveScene();sb.AppendLine("  open scene "+s.path+" dirty "+s.isDirty+"; roots here: "+string.Join(", ",OwnRoots(cfg,s).Select(g=>g.name+" ("+g.transform.Cast<Transform>().Sum(h=>h.childCount)+")")));
   return sb.ToString();
  }

  // ---------- apply / revert ----------

  static string BackupScene(Target t,string tag)
  {
   string dir=Path.Combine(StageDir,"Backup",DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff",Inv)+"_"+tag+"_"+t.Alias);Directory.CreateDirectory(dir);
   string copy=Path.Combine(dir,Path.GetFileName(t.Scene));File.Copy(Harness303.Abs(t.Scene),copy,true);return copy;
  }
  static void Journal(Target t,string what,string before,string after,string dataSha,string backup)
  {
   string dir=Path.Combine(StageDir,"Ledger");Directory.CreateDirectory(dir);
   var o=new JObject{["utc"]=DateTime.UtcNow.ToString("O"),["scene"]=t.Scene,["what"]=what,["sha_before"]=before,["sha_after"]=after,["data_sha"]=dataSha,["backup"]=backup};
   File.AppendAllText(Path.Combine(dir,"ledger_realms308_"+t.Alias+".jsonl"),o.ToString(Newtonsoft.Json.Formatting.None)+"\n");
  }
  static string Apply(string arg,bool dry)
  {
   var cfg=Load(!dry);var t=TargetOf(cfg,arg);var scene=Open(t);
   var sb=new StringBuilder("Realms308 "+(dry?"plan":"scene-apply")+" "+t.Alias+" (data "+Short(cfg.Sha)+" "+(Opt(cfg.J,"version")??"?")+")\n");
   foreach(var g in OwnRoots(cfg,scene))if(PostLedger308.UnderProtectedTree(g.transform))throw new Refuse("root "+g.name+" is a protected tree name");
   HiddenGuard(cfg,scene);
   var items=Plan(cfg);
   foreach(var grp in items.GroupBy(i=>i.Root+"/"+i.Group))sb.AppendLine("  "+grp.Key+": "+Counts(grp.ToList()));
   string what=Counts(items);
   if(dry)return sb.Append("  plan only: nothing written (would rebuild "+what+")").ToString();
   string abs=Harness303.Abs(t.Scene),before=Harness303.Sha(abs),backup=BackupScene(t,"apply");
   try
   {
    foreach(var g in OwnRoots(cfg,scene).ToList())Object.DestroyImmediate(g);
    var roots=new Dictionary<string,Transform>();var holders=new Dictionary<string,Transform>();
    foreach(var it in items)
    {
     if(!roots.TryGetValue(it.Root,out var root)){var go=new GameObject(it.Root);SceneManager.MoveGameObjectToScene(go,scene);roots[it.Root]=root=go.transform;}
     string hk=it.Root+"/"+it.Group;if(!holders.TryGetValue(hk,out var holder)){holder=new GameObject(it.Group).transform;holder.SetParent(root,false);holders[hk]=holder;}
     Make(holder,it);
    }
   }
   catch(Exception e)
   {
    EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);
    return "FAILED scene-apply "+t.Scene+" (scene reloaded from disk, nothing saved; backup "+backup+")\n"+e;
   }
   EditorSceneManager.MarkSceneDirty(scene);
   if(!EditorSceneManager.SaveScene(scene)){EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);return "FAILED SaveScene returned false for "+t.Scene+" (scene reloaded from disk, nothing saved; backup "+backup+")";}
   string after=Harness303.Sha(abs);Journal(t,"apply "+what,before,after,cfg.Sha,backup);
   return sb.Append("  saved: "+what+"; scene sha "+Short(before)+" -> "+Short(after)+"; backup "+backup).ToString();
  }
  static string Revert(string arg)
  {
   var cfg=Load(false);var t=TargetOf(cfg,arg);var scene=Open(t);var own=OwnRoots(cfg,scene).ToList();
   if(own.Count==0)return "Realms308 scene-revert "+t.Alias+": 변경 없음 (no root of this tool in the scene)";
   foreach(var g in own)if(PostLedger308.UnderProtectedTree(g.transform))throw new Refuse("root "+g.name+" is a protected tree name");
   string abs=Harness303.Abs(t.Scene),before=Harness303.Sha(abs),backup=BackupScene(t,"revert");
   foreach(var g in own)Object.DestroyImmediate(g);
   EditorSceneManager.MarkSceneDirty(scene);
   if(!EditorSceneManager.SaveScene(scene)){EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);return "FAILED SaveScene returned false for "+t.Scene+" (scene reloaded from disk; backup "+backup+")";}
   Journal(t,"revert "+own.Count+" root(s)",before,Harness303.Sha(abs),cfg.Sha,backup);
   return "Realms308 scene-revert "+t.Alias+": saved; removed "+own.Count+" root(s); backup "+backup;
  }

  // ---------- check ----------

  static string Check(string arg)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);var scene=Open(t);var items=Plan(cfg);var ig=IgnoreRoots(cfg);
   var sb=new StringBuilder("Realms308 check "+t.Alias+" (data "+Short(cfg.Sha)+" "+(Opt(cfg.J,"version")??"?")+")\n");int pass=0,fail=0;
   void Line(bool ok,string id,string text){if(ok)pass++;else fail++;sb.AppendLine("  "+(ok?"PASS ":"FAIL ")+id+" "+text);}
   var own=OwnRoots(cfg,scene).ToList();var names=cfg.Roots;
   Line(own.All(g=>g.activeSelf),"AC-R0","the roots are active (not left hidden by show:off)");
   Line(names.All(n=>own.Count(g=>g.name==n)==1)&&own.Count==names.Length,"AC-R1","roots: "+string.Join(", ",names.Select(n=>n+" x"+own.Count(g=>g.name==n)))+"; none under a protected tree: "+own.All(g=>!PostLedger308.UnderProtectedTree(g.transform)));
   int rowsData=items.Count,rowsScene=0,poseBad=0,missing=0;var badNames=new List<string>();
   float pt=cfg.R("pose_tol_m"),pd=cfg.R("pose_tol_deg");
   foreach(var grp in items.GroupBy(i=>i.Root+"/"+i.Group))
   {
    var root=own.FirstOrDefault(g=>g.name==grp.First().Root);var holder=root!=null?root.transform.Find(grp.First().Group):null;int have=holder!=null?holder.childCount:0;rowsScene+=have;
    sb.AppendLine("    "+grp.Key+": data rows "+grp.Count()+", scene objects "+have);
    foreach(var it in grp)
    {
     var tr=holder!=null?holder.Find(it.Id):null;if(tr==null){missing++;if(badNames.Count<8)badNames.Add(it.Id+" missing");continue;}
     if((tr.position-it.Pos).magnitude>pt||Quaternion.Angle(tr.rotation,it.Rot)>pd||(tr.localScale-it.Scale).magnitude>1e-3f||tr.GetComponentsInChildren<MeshRenderer>(true).Length!=it.K.Parts.Count){poseBad++;if(badNames.Count<8)badNames.Add(it.Id+" pose/parts");}
    }
   }
   int holdersScene=own.Sum(g=>g.transform.childCount),holdersData=items.Select(i=>i.Root+"/"+i.Group).Distinct().Count();
   Line(rowsScene==rowsData&&missing==0&&holdersScene==holdersData,"AC-R2","row objects under the roots "+rowsScene+" = data rows "+rowsData+" (missing "+missing+", group holders "+holdersScene+"/"+holdersData+")");
   Line(poseBad==0,"AC-R3","every row at the pose the data + the physical ground give: off "+poseBad+(badNames.Count>0?" ["+string.Join("; ",badNames)+"]":""));
   var all=own.SelectMany(g=>g.GetComponentsInChildren<Component>(true)).ToList();
   int nulls=all.Count(c=>c==null),scripts=all.Count(c=>c is MonoBehaviour),texts=all.Count(c=>c is TextMesh||(c!=null&&c.GetType().FullName.StartsWith("TMPro.",StringComparison.Ordinal)));
   int rigid=all.Count(c=>c is Rigidbody),nav=all.Count(c=>c!=null&&(c.GetType().FullName.Contains("NavMesh")||c.GetType().FullName.Contains("OffMeshLink"))),anim=all.Count(c=>c is Animator||c is Animation),audio=all.Count(c=>c is AudioSource),lights=all.Count(c=>c is Light),particles=all.Count(c=>c is ParticleSystem),skinned=all.Count(c=>c is SkinnedMeshRenderer);
   Line(nulls+scripts+texts+rigid+nav+anim+audio+lights+particles+skinned==0,"AC-R4","dressing only: scripts "+scripts+" (missing "+nulls+"), text "+texts+", rigidbodies "+rigid+", NavMesh components "+nav+", animators "+anim+", audio "+audio+", lights "+lights+", particle systems "+particles+", skinned "+skinned);
   var cols=all.OfType<Collider>().ToList();int boxWant=items.Count(i=>i.K.HasCollider);
   Line(cols.Count==boxWant&&cols.All(c=>c is BoxCollider&&!c.isTrigger),"AC-R5","colliders under the roots "+cols.Count+" = BoxColliders the data gives "+boxWant+" (small props carry none)");
   var rends=all.OfType<MeshRenderer>().ToList();var glow=rends.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().Where(Glows).Select(m=>m.name).ToList();int nullMat=rends.Count(r=>r.sharedMaterials.Any(m=>m==null));
   Line(glow.Count==0&&nullMat==0,"AC-R6","no glow: materials with _EMISSION "+glow.Count+(glow.Count>0?" ["+string.Join(", ",glow.Take(5))+"]":"")+", empty material slots "+nullMat+", distinct materials "+rends.SelectMany(r=>r.sharedMaterials).Distinct().Count());
   var filters=all.OfType<MeshFilter>().ToList();var meshes=new HashSet<Mesh>(filters.Select(f=>f.sharedMesh).Where(m=>m!=null));int sceneMesh=meshes.Count(m=>!EditorUtility.IsPersistent(m)),noMesh=filters.Count(f=>f.sharedMesh==null);
   Line(sceneMesh==0&&noMesh==0,"AC-R7","shared meshes only: distinct meshes "+meshes.Count+", meshes living in the scene file "+sceneMesh+", empty filters "+noMesh);
   var lower=new HashSet<Renderer>();foreach(var lg in all.OfType<LODGroup>()){var lods=lg.GetLODs();for(int i=1;i<lods.Length;i++)foreach(var r in lods[i].renderers)if(r!=null)lower.Add(r);}
   long tris=0;foreach(var r in rends){if(lower.Contains(r))continue;var mf=r.GetComponent<MeshFilter>();tris+=Tris(mf!=null?mf.sharedMesh:null);}
   Line(true,"AC-R8","cost (noted): MeshRenderers "+rends.Count+", LODGroups "+all.OfType<LODGroup>().Count()+", near triangles "+tris.ToString("N0",Inv)+", BoxColliders "+cols.Count+" | plan says "+Counts(items));
   // AC-R9 floating / buried: the lowest point of a row against the lowest and highest terrain under its footprint
   float tol=cfg.R("float_tol_m"),step=cfg.R("sample_step_m");var floats=new List<string>();var buried=new List<string>();Physics.SyncTransforms();
   foreach(var it in items)
   {
    if(Flag(it.Row,"float_ok"))continue;
    var root=own.FirstOrDefault(g=>g.name==it.Root);var tr=root!=null?root.transform.Find(it.Group+"/"+it.Id):null;if(tr==null)continue;
    var rs=tr.GetComponentsInChildren<MeshRenderer>(true);if(rs.Length==0)continue;var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
    float lo=float.PositiveInfinity,hi=float.NegativeInfinity;int nx=Mathf.Clamp(Mathf.CeilToInt(b.size.x/step)+1,2,6),nz=Mathf.Clamp(Mathf.CeilToInt(b.size.z/step)+1,2,6);
    for(int i=0;i<nx;i++)for(int k=0;k<nz;k++)if(Ground(ig,b.min.x+b.size.x*i/(nx-1),b.min.z+b.size.z*k/(nz-1),out var gp,out _)){lo=Mathf.Min(lo,gp.y);hi=Mathf.Max(hi,gp.y);}
    if(float.IsInfinity(lo))continue;
    if(b.min.y-hi>tol)floats.Add(it.Id+" +"+F(b.min.y-hi));
    if(b.max.y<lo)buried.Add(it.Id+" "+F(b.max.y-lo));
   }
   Line(floats.Count==0&&buried.Count==0,"AC-R9","no floating / buried row (tolerance "+F(tol)+" m; rows marked float_ok skipped): floating "+floats.Count+(floats.Count>0?" ["+string.Join("; ",floats.Take(12))+"]":"")+", buried "+buried.Count+(buried.Count>0?" ["+string.Join("; ",buried.Take(12))+"]":""));
   bool outside=own.All(g=>g.transform.parent==null)&&!cfg.Roots.Any(r=>PostLedger308.IsProtectedPath("/"+r+"/"));
   Line(outside,"AC-R10","the roots are scene roots outside the protected trees; scene dirty "+scene.isDirty);
   return sb.Append(fail==0?"  no FAIL (PASS "+pass+")":"  FAIL "+fail+" (PASS "+pass+")").ToString();
  }

  // ---------- stills ----------

  // show:off hides this tool's roots for a "before" still WITHOUT dirtying the scene (nothing is saved); show:on brings them back.
  // A write command refuses while they are hidden.
  static string Show(string arg)
  {
   var cfg=Load(false);bool on=arg!="off";var s=SceneManager.GetActiveScene();bool dirty=s.isDirty;int n=0;
   foreach(var g in OwnRoots(cfg,s)){g.SetActive(on);n++;}
   return "Realms308 show:"+(on?"on":"off")+" "+n+" root(s) in "+s.path+" (scene dirty before "+dirty+", after "+s.isDirty+")";
  }
  static void HiddenGuard(Cfg cfg,Scene s){foreach(var g in OwnRoots(cfg,s))if(!g.activeSelf)throw new Refuse("root "+g.name+" is hidden (show:off) - run show:on first");}

  static string Eyes(string label)
  {
   var cfg=Load(false);var ig=IgnoreRoots(cfg);Physics.SyncTransforms();var sb=new StringBuilder();
   foreach(var s in Arr(cfg.J,"stills"))
   {
    var e=Vec(s,"eye");var tg=Vec(s,"target");
    if(!Ground(ig,e.x,e.z,out var ge,out _)||!Ground(ig,tg.x,tg.z,out var gt,out _)){sb.AppendLine("# "+Str(s,"name")+": no terrain");continue;}
    float ey=Flag(s,"eye_abs")?e.y:ge.y+e.y,ty=Flag(s,"target_abs")?tg.y:gt.y+tg.y;
    sb.AppendLine("shot:"+label+"_"+Str(s,"name")+":"+F(e.x)+","+F(ey)+","+F(e.z)+":"+F(tg.x)+","+F(ty)+","+F(tg.z)+":fov="+F(OptNum(s,"fov",60f),"F0")+":w=1600:h=900:hideplayer");
   }
   return sb.ToString();
  }
 }
}
