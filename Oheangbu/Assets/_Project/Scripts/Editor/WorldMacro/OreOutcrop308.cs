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
 // #308 심부 광맥 노두 - ONE test install (D308-18 answer 4; relayout308.json deferred_build A03 / tool gap T10; group
 // G13_ore_outcrop_test) [TEST values throughout]. SPEC-ORE-OUTCROP-308.
 // An outcrop is put together from things the project already has - nothing is generated:
 //   rock   = a vegetation-sheet rock prototype (its LOD0 mesh + material, by GUID) or a scene object named by key
 //   bands  = #297 seam meshes (wall strips whose pivot is thousands of metres from the mesh: the tool places the mesh CENTRE on
 //            the rock face it finds with a ray against the rock's own triangles) + the existing Ore_Vein material
 //   light  = at most one Point light of the prologue seam class (values in the data)
 // No glow card, no Bloom / post value, no collider (no NavMesh bake), no content row, no text. Every number, id and path comes
 // from Art/World/Compact/Rebuild/CliffBoundary308/ore308.json. Queue-safe: Run(string) never opens a dialog; refusals come back
 // as "REFUSED ...". Ledger (Art/Playtest308/Pacing/ledger_ore308_scene_<alias>.json) is written BEFORE the scene is saved, with a
 // byte backup of the scene file; a second identical apply reports "변경 없음" and writes nothing; scene-revert takes out exactly the
 // objects the ledger created. Only the target scene is saved (SaveScene; never AssetDatabase.SaveAssets).
 //   status
 //   plan:<scene>            what scene-apply would create + the measured fit of every band on the rock; nothing is written
 //   scene-apply:<scene>  |  scene-revert:<scene>
 //   check:<scene>           AC-O1..O8 on the saved scene; read-only
 //   shot:<scene>:<eye>:<PC|Mobile>[:label=<text>]   one still (stills.eyes[] of the data, or "close"); before and after the apply
 // <scene> = arch296 | folk298 | main (data targets[]). Order: arch296 -> folk298 -> main.
 // 2회차(D308-22): rows with kind "split_vein" are handled by OreOutcrop308.Vein2.cs (bake | bake-plan | bake-revert + the same plan / scene-apply / check / shot;
 // scene-revert below serves both tries - it removes exactly what the scene ledger created).
 public static partial class OreOutcrop308
 {
  const string Usage="status | bake-plan | bake | bake-revert | plan:<scene> | scene-apply:<scene> | scene-revert:<scene> | check:<scene> | shot:<scene>:<eye>:<PC|Mobile>[:label=<text>]";
  static string DataFile=>Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","CliffBoundary308","ore308.json");
  static string OutDir=>Path.Combine(Harness303.RepoRoot,"Art","Playtest308","Pacing");
  sealed class Refuse:Exception{public Refuse(string m):base(m){}}

  public static string Run(string command)
  {
   string c=(command??"").Trim();
   try
   {
    if(c=="status")return Status();
    if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Edit mode only (Play is running)";
    if(c=="bake-plan")return Bake(true);
    if(c=="bake")return Bake(false);
    if(c=="bake-revert")return BakeRevert();
    string a;
    if((a=Arg(c,"plan:"))!=null)return V2(Load())?ApplyV2(a,true):Apply(a,true);
    if((a=Arg(c,"scene-apply:"))!=null)return V2(Load())?ApplyV2(a,false):Apply(a,false);
    if((a=Arg(c,"scene-revert:"))!=null)return Revert(a);
    if((a=Arg(c,"check:"))!=null)return V2(Load())?CheckV2(a):Check(a);
    if((a=Arg(c,"shot:"))!=null)return V2(Load())?ShotV2(a):Shot(a);
   }
   catch(Refuse r){return "REFUSED "+r.Message;}
   catch(Exception e){return "FAILED "+e;}
   return "REFUSED unknown OreOutcrop308 command '"+c+"' ("+Usage+")";
  }
  static string Arg(string c,string head)=>c.StartsWith(head,StringComparison.Ordinal)?c.Substring(head.Length).Trim():null;

  // ---------- data ----------

  sealed class Target{public string Alias,Scene;}
  sealed class Cfg{public JObject J;public string Sha;public Target[] Targets;public JToken Rules=>J["rules"];public float R(string k)=>Num(Rules,k);public string Root=>Str(Rules,"root");}
  static JToken Req(JToken o,string key){var t=o?[key];if(t==null||t.Type==JTokenType.Null)throw new Refuse("ore308.json: missing '"+key+"' in "+(o==null?"(null)":string.IsNullOrEmpty(o.Path)?"(root)":o.Path));return t;}
  static float Num(JToken o,string key)=>Req(o,key).Value<float>();
  static string Str(JToken o,string key)=>Req(o,key).Value<string>();
  static string Opt(JToken o,string key){var t=o?[key];return t==null||t.Type==JTokenType.Null?null:t.Value<string>();}
  static bool Flag(JToken o,string key){var t=o?[key];return t!=null&&t.Type==JTokenType.Boolean&&t.Value<bool>();}
  static IEnumerable<JToken> Arr(JToken o,string key){var t=o?[key] as JArray;return t??(IEnumerable<JToken>)Array.Empty<JToken>();}
  static Vector3 Vec(JToken o,string key){var a=Req(o,key) as JArray;if(a==null||a.Count<3)throw new Refuse("ore308.json: "+key+" needs 3 numbers");return new Vector3(a[0].Value<float>(),a[1].Value<float>(),a[2].Value<float>());}
  static string F(float v,string f="F2")=>v.ToString(f,CultureInfo.InvariantCulture);
  static string V(Vector3 v)=>Harness303.V(v);
  static string Short(string sha)=>string.IsNullOrEmpty(sha)?"-":sha.Substring(0,Math.Min(12,sha.Length)).ToLowerInvariant();

  static Cfg Load()
  {
   string f=DataFile;if(!File.Exists(f))throw new Refuse("data missing: "+f+" (copy Tools/Unity/Stage308_world18/ore/Data/ore308.json there)");
   JObject j;try{j=JObject.Parse(File.ReadAllText(f));}catch(Exception e){throw new Refuse("data does not parse: "+f+" ("+e.Message+")");}
   var cfg=new Cfg{J=j,Sha=Harness303.Sha(f)};
   cfg.Targets=Arr(j,"targets").Select(t=>new Target{Alias=Str(t,"alias"),Scene=Str(t,"scene")}).ToArray();
   if(cfg.Targets.Length==0)throw new Refuse("ore308.json: targets[] is empty");
   foreach(var t in cfg.Targets)if(Harness303.IsProtected(t.Scene)||PostLedger308.IsProtectedPath(t.Scene))throw new Refuse("protected target in data: "+t.Scene);
   Req(j,"rules");Req(j,"outcrops");Req(j,"stills");
   return cfg;
  }
  static Target TargetOf(Cfg cfg,string arg)
  {
   arg=(arg??"").Trim().Replace('\\','/');
   var t=cfg.Targets.FirstOrDefault(x=>string.Equals(x.Alias,arg,StringComparison.OrdinalIgnoreCase)||x.Scene==arg);
   if(t==null)throw new Refuse("not an ore outcrop target: '"+arg+"' (allowed: "+string.Join(", ",cfg.Targets.Select(x=>x.Alias))+")");
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

  // ---------- ledger ----------

  [Serializable] sealed class Row{public string kind="",key="",detail="";public bool reverted;}
  [Serializable] sealed class Write{public string utc="",target="",backup="",shaBefore="",shaAfter="",dataSha="";public List<Row> rows=new List<Row>();}
  [Serializable] sealed class Ledger{public string kind="ore308",group="",target="",created="";public List<Write> writes=new List<Write>();}
  static string LedgerFile(Target t)=>Path.Combine(OutDir,"ledger_ore308_scene_"+t.Alias+".json");
  static Ledger ReadLedger(Target t){var f=LedgerFile(t);return File.Exists(f)?JsonUtility.FromJson<Ledger>(File.ReadAllText(f)):null;}
  static void WriteLedger(Target t,Ledger l){Directory.CreateDirectory(OutDir);File.WriteAllText(LedgerFile(t),JsonUtility.ToJson(l,true));}
  static IEnumerable<Row> Live(Ledger l)=>l==null?Enumerable.Empty<Row>():l.writes.Where(w=>w!=null&&w.rows!=null).SelectMany(w=>w.rows).Where(r=>r!=null&&!r.reverted);
  static string Backup(string assetPath)
  {
   string dir=Path.Combine(OutDir,"Backups","ore308-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff",CultureInfo.InvariantCulture));Directory.CreateDirectory(dir);
   string copy=Path.Combine(dir,Path.GetFileName(assetPath));File.Copy(Harness303.Abs(assetPath),copy,true);return copy;
  }

  // ---------- ground + rock rays ----------

  static bool Under(Transform t,HashSet<string> roots){for(var p=t;p!=null;p=p.parent)if(p.parent==null&&roots.Contains(p.name))return true;return false;}
  static bool Preview(Transform t){for(var p=t;p!=null;p=p.parent)if((p.gameObject.hideFlags&HideFlags.DontSave)!=0)return true;return false;}
  static bool TerrainLike(Collider c){if(c is TerrainCollider)return true;for(var t=c.transform;t!=null;t=t.parent){var n=t.name;if(n.Contains("Terrain")||n.Contains("Surface"))return true;}return false;}
  // the Content308 rule in short: the highest terrain surface under the XZ (own / listed roots and previews never count)
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
  static bool RayTri(Vector3 o,Vector3 d,Vector3 a,Vector3 b,Vector3 c,out float t)
  {
   t=0;Vector3 e1=b-a,e2=c-a,pv=Vector3.Cross(d,e2);float det=Vector3.Dot(e1,pv);if(Mathf.Abs(det)<1e-9f)return false;
   float inv=1f/det;Vector3 tv=o-a;float u=Vector3.Dot(tv,pv)*inv;if(u<0||u>1)return false;
   Vector3 qv=Vector3.Cross(tv,e1);float v=Vector3.Dot(d,qv)*inv;if(v<0||u+v>1)return false;
   t=Vector3.Dot(e2,qv)*inv;return t>1e-5f;
  }
  sealed class Hull{public Vector3[] W;public int[] T;
   public bool Ray(Vector3 o,Vector3 d,out float best,out Vector3 n)
   {
    best=float.MaxValue;n=Vector3.up;bool hit=false;
    for(int i=0;i+2<T.Length;i+=3){var a=W[T[i]];var b=W[T[i+1]];var c=W[T[i+2]];if(RayTri(o,d,a,b,c,out float t)&&t<best){best=t;n=Vector3.Cross(b-a,c-a).normalized;hit=true;}}
    return hit;
   }}

  // The face a band lies on = the mean rock normal over the band's own footprint: a (2k+1) x (2k+1) grid of rays parallel to the centre
  // ray (rules.face_samples_half = k, rules.face_sample_share = the share of the band's half size the grid covers). One triangle of a
  // jointed boulder says little: neighbouring triangles differ by tens of degrees, so a 0.1 m change of the data swung the band.
  static Vector3 FaceNormal(Cfg cfg,Hull hull,Vector3 start,Vector3 f,Vector3 centreNormal,float halfLong,float halfShort,out int hits)
  {
   int k=Mathf.Max(0,Mathf.RoundToInt(cfg.R("face_samples_half")));float share=cfg.R("face_sample_share");var side=Vector3.Cross(Vector3.up,f).normalized;
   var sum=Vector3.zero;hits=0;
   for(int i=-k;i<=k;i++)for(int j=-k;j<=k;j++)
   {
    var o=start+side*(k==0?0f:halfLong*share*i/k)+Vector3.up*(k==0?0f:halfShort*share*j/k);
    if(!hull.Ray(o,-f,out _,out var n))continue;
    if(Vector3.Dot(n,f)<0)n=-n;sum+=n;hits++;
   }
   return sum.sqrMagnitude>1e-6f?sum.normalized:centreNormal;
  }

  // ---------- plan ----------

  sealed class Part{public string Name;public Mesh Mesh;public Material[] Mats;public Vector3 Pos,Scale;public Quaternion Rot;public bool Shadows,Band;public string Fit="";public Vector3 Face,Normal;}
  sealed class Plan{public string Id;public Vector3 Holder;public List<Part> Parts=new List<Part>();public JToken Light;public Vector3 LightPos;public List<string> Notes=new List<string>();public int Warns;}
  static T Asset<T>(string guid,string what) where T:Object
  {
   string p=AssetDatabase.GUIDToAssetPath(guid);var a=string.IsNullOrEmpty(p)?null:AssetDatabase.LoadAssetAtPath<T>(p);
   if(a==null)throw new Refuse(what+": GUID "+guid+" does not load as "+typeof(T).Name+(string.IsNullOrEmpty(p)?"":" ("+p+")"));return a;
  }
  static Plan Build(Cfg cfg,Scene scene,JToken o)
  {
   var plan=new Plan{Id=Str(o,"id")};float x=Num(o,"x"),z=Num(o,"z");Physics.SyncTransforms();
   if(plan.Id.IndexOf("Ore_Vein",StringComparison.Ordinal)<0)throw new Refuse("outcrop id '"+plan.Id+"' must hold \"Ore_Vein\" (SPEC-EVENT-WASH-308 allow list)");
   if(!Ground(cfg,x,z,out var g,out float slope))throw new Refuse("no terrain under "+plan.Id+" ("+F(x,"F1")+", "+F(z,"F1")+") - is the scene loaded?");
   if(slope>cfg.R("max_slope_deg"))throw new Refuse(plan.Id+": physical slope "+F(slope,"F1")+"° > "+F(cfg.R("max_slope_deg"),"F1")+"° - revise the row. Nothing written");
   float dy=g.y-Num(o,"y_offline");plan.Holder=g;
   plan.Notes.Add((Mathf.Abs(dy)>cfg.R("y_tol_m")?"WARN ":"")+"ground "+V(g)+" slope "+F(slope,"F1")+"° (offline y "+F(Num(o,"y_offline"))+", Δ "+F(dy)+" m - the editor value is used)");
   if(Mathf.Abs(dy)>cfg.R("y_tol_m"))plan.Warns++;
   // rock
   var rk=Req(o,"rock");Mesh rockMesh;Material[] rockMats;string key=Opt(rk,"source_key");
   if(!string.IsNullOrEmpty(key))
   {
    var src=BuildingAudit308.Resolve(scene,key);if(src==null||BuildingAudit308.KeyOf(src)!=key)throw new Refuse("rock.source_key '"+key+"' does not resolve to exactly that object in "+scene.path);
    var mf=src.GetComponentInChildren<MeshFilter>(true);var mr=mf!=null?mf.GetComponent<MeshRenderer>():null;if(mf==null||mf.sharedMesh==null||mr==null)throw new Refuse("rock.source_key '"+key+"' has no mesh renderer");
    rockMesh=mf.sharedMesh;rockMats=mr.sharedMaterials;
   }
   else{rockMesh=Asset<Mesh>(Str(rk,"mesh_guid"),"rock mesh");rockMats=new[]{Asset<Material>(Str(rk,"material_guid"),"rock material")};}
   float rs=Num(rk,"scale");var rockPos=g+Vector3.down*Num(rk,"sink_m");var rockRot=Quaternion.Euler(0,Num(rk,"yaw_deg"),0);
   plan.Parts.Add(new Part{Name=Str(rk,"name"),Mesh=rockMesh,Mats=rockMats,Pos=rockPos,Rot=rockRot,Scale=Vector3.one*rs,Shadows=Flag(rk,"shadows")});
   var m=Matrix4x4.TRS(rockPos,rockRot,Vector3.one*rs);var hull=new Hull{W=rockMesh.vertices.Select(v=>m.MultiplyPoint3x4(v)).ToArray(),T=rockMesh.triangles};
   var wb=new Bounds(hull.W[0],Vector3.zero);foreach(var w in hull.W)wb.Encapsulate(w);
   float low=float.MinValue;foreach(var c in new[]{new Vector2(wb.min.x,wb.min.z),new Vector2(wb.max.x,wb.min.z),new Vector2(wb.min.x,wb.max.z),new Vector2(wb.max.x,wb.max.z)})if(Ground(cfg,c.x,c.y,out var gc,out _))low=Mathf.Max(low,wb.min.y-gc.y);
   plan.Notes.Add("rock "+rockMesh.name+" ("+rockMesh.triangles.Length/3+" tris, shader "+(rockMats.Length>0&&rockMats[0]!=null?rockMats[0].shader.name:"MISSING")+"): world box "+V(wb.size)+", top "+F(wb.max.y-g.y)+" m over the ground, lowest point "+F(wb.min.y-g.y)+" m; over the lowest footprint-corner ground "+F(low)+" m");
   if(low>cfg.R("ground_gap_max_m")){plan.Warns++;plan.Notes.Add("WARN rock base stands "+F(low)+" m over the ground at a footprint corner (> "+F(cfg.R("ground_gap_max_m"))+") - raise rock.sink_m");}
   // bands
   var vein=Asset<Material>(Str(Req(o,"vein_material"),"guid"),"vein material");
   if(vein.shader==null||vein.shader.name!=Str(o["vein_material"],"shader"))throw new Refuse("vein material "+vein.name+" shader is "+(vein.shader!=null?vein.shader.name:"null")+", expected "+Str(o["vein_material"],"shader"));
   // the emission cap in numbers ("빛은 전구가 아니라 먹이다"): how many sustained-emission strips one outcrop may carry, and how large
   int bandCount=Arr(o,"bands").Count();
   if(bandCount>Mathf.RoundToInt(cfg.R("bands_max_per_outcrop")))throw new Refuse(plan.Id+": "+bandCount+" bands > rules.bands_max_per_outcrop "+F(cfg.R("bands_max_per_outcrop"),"F0")+". Nothing written");
   foreach(var b in Arr(o,"bands"))
   {
    if(Num(b,"scale")>cfg.R("band_scale_max")||Num(b,"scale")<=0f)throw new Refuse(plan.Id+" band "+Str(b,"id")+": scale "+F(Num(b,"scale"))+" outside (0, rules.band_scale_max "+F(cfg.R("band_scale_max"))+"]. Nothing written");
    var mesh=Asset<Mesh>(Str(b,"mesh_guid"),"band mesh");var fr=Req(b,"frame");Vector3 centre=Vec(fr,"centre"),lng=Vec(fr,"long").normalized,nrm=Vec(fr,"normal").normalized;
    if((mesh.bounds.center-centre).magnitude>cfg.R("frame_centre_tol_m"))throw new Refuse("band "+Str(b,"id")+": mesh centre "+V(mesh.bounds.center)+" is not the data frame centre "+V(centre)+" - run ore308_dry.py and revise the frame");
    float yaw=Num(b,"face_yaw_deg")*Mathf.Deg2Rad,s=Num(b,"scale");var f=new Vector3(Mathf.Sin(yaw),0,Mathf.Cos(yaw));
    var start=new Vector3(x,g.y+Num(b,"height_m"),z)+f*cfg.R("ray_back_m");
    if(!hull.Ray(start,-f,out float t,out var n))throw new Refuse("band "+Str(b,"id")+": no rock face at height "+F(Num(b,"height_m"))+" m, yaw "+F(Num(b,"face_yaw_deg"),"F0")+" - revise height_m / face_yaw_deg");
    if(Vector3.Dot(n,f)<0)n=-n;var p=start-f*t;
    var fsz=Vec(fr,"size_m");n=FaceNormal(cfg,hull,start,f,n,fsz.x*s*.5f,fsz.y*s*.5f,out int faceHits);
    float elev=Mathf.Asin(Mathf.Clamp(n.y,-1f,1f))*Mathf.Rad2Deg;var nh=new Vector3(n.x,0,n.z);float offYaw=nh.sqrMagnitude>1e-6f?Vector3.Angle(nh,f):180f;
    var tan=Vector3.Cross(Vector3.up,n);if(tan.sqrMagnitude<1e-4f)tan=Vector3.Cross(f,Vector3.up);tan=Quaternion.AngleAxis(Num(b,"tilt_deg"),n)*tan.normalized;var across=Vector3.Cross(n,tan);
    var rot=Quaternion.LookRotation(n,across)*Quaternion.Inverse(Quaternion.LookRotation(nrm,Vector3.Cross(nrm,lng)));
    var pos=p+n*Num(b,"proud_m")-rot*(centre*s);
    // fit: every band vertex against the rock along the face normal (+ = in front of the rock, - = buried)
    int fl=0,off=0;float maxF=0,maxB=0;var vs=mesh.vertices;
    foreach(var v in vs){var w=pos+rot*(v*s);if(!hull.Ray(w+n,-n,out float d,out _)){off++;continue;}float gap=d-1f;if(gap>cfg.R("band_float_max_m"))fl++;maxF=Mathf.Max(maxF,gap);maxB=Mathf.Max(maxB,-gap);}
    float flS=fl/(float)vs.Length,offS=off/(float)vs.Length;bool bad=flS>cfg.R("band_float_share_max")||offS>cfg.R("band_offrock_share_max")||maxB>cfg.R("band_bury_max_m")||maxF>cfg.R("band_float_abs_max_m")||Mathf.Abs(elev)>cfg.R("face_elevation_max_deg")||offYaw>cfg.R("face_off_yaw_max_deg");if(bad)plan.Warns++;
    string name=plan.Id+"_"+Str(b,"id");
    plan.Parts.Add(new Part{Name=name,Mesh=mesh,Mats=new[]{vein},Pos=pos,Rot=rot,Scale=Vector3.one*s,Band=true,Face=p,Normal=n,
     Fit=(bad?"WARN ":"")+name+" on the face at "+V(p)+" (mean normal of "+faceHits+" ray(s) "+V(n)+": elevation "+F(elev,"F0")+"° (limit "+F(cfg.R("face_elevation_max_deg"),"F0")+"), "+F(offYaw,"F0")+"° off the asked yaw (limit "+F(cfg.R("face_off_yaw_max_deg"),"F0")+")): floating > "+F(cfg.R("band_float_max_m"))+" m "+F(flS*100,"F0")+" % (farthest "+F(maxF)+" m, limit "+F(cfg.R("band_float_abs_max_m"))+"), off the rock "+F(offS*100,"F0")+" %, deepest buried "+F(maxB)+" m"});
   }
   var li=o["light"];
   if(li!=null&&Flag(li,"enabled"))
   {
    var lm=Req(cfg.Rules,"light_max");if(Num(li,"intensity")>Num(lm,"intensity")||Num(li,"range_m")>Num(lm,"range_m")||Flag(li,"shadows")||Str(li,"type")!="Point")throw new Refuse("light row is outside rules.light_max (Point, no shadows)");
    var at=plan.Parts.FirstOrDefault(q=>q.Band&&q.Name==plan.Id+"_"+Str(li,"band"));if(at==null)throw new Refuse("light.band '"+Str(li,"band")+"' is not a band of "+plan.Id);
    plan.Light=li;plan.LightPos=at.Face+at.Normal*Num(li,"off_face_m");
   }
   return plan;
  }
  static IEnumerable<JToken> Enabled(Cfg cfg)=>Arr(cfg.J,"outcrops").Where(o=>Flag(o,"enabled"));
  static Transform Root(Cfg cfg,Scene s)=>s.GetRootGameObjects().Where(g=>g.name==cfg.Root).Select(g=>g.transform).FirstOrDefault();
  static bool Same(Cfg cfg,Transform holder,Plan plan)
  {
   float tol=cfg.R("pose_tol_m");if((holder.position-plan.Holder).magnitude>tol)return false;
   foreach(var p in plan.Parts){var t=holder.Find(p.Name);var mf=t!=null?t.GetComponent<MeshFilter>():null;if(mf==null||mf.sharedMesh!=p.Mesh||(t.position-p.Pos).magnitude>tol||Quaternion.Angle(t.rotation,p.Rot)>cfg.R("pose_tol_deg")||Mathf.Abs(t.localScale.x-p.Scale.x)>1e-3f)return false;}
   var l=plan.Light!=null?holder.Find(Str(plan.Light,"name")):null;
   if((plan.Light!=null)!=(l!=null))return false;
   return holder.GetComponentsInChildren<Light>(true).Length==(plan.Light!=null?1:0);
  }

  // ---------- apply / revert ----------

  static string Apply(string arg,bool dry)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,!dry);var scene=Open(t);
   var sb=new StringBuilder("OreOutcrop308 "+(dry?"plan":"scene-apply")+" "+t.Alias+" (data "+Short(cfg.Sha)+" "+(Opt(cfg.J,"version")??"?")+", group "+(Opt(cfg.J,"group")??"")+")\n");
   var todo=new List<Plan>();int warns=0;var root=Root(cfg,scene);
   if(root!=null&&PostLedger308.UnderProtectedTree(root))throw new Refuse("root "+cfg.Root+" is a protected tree name");
   foreach(var o in Enabled(cfg))
   {
    var plan=Build(cfg,scene,o);warns+=plan.Warns;var have=root!=null?root.Find(plan.Id):null;
    foreach(var n in plan.Notes)sb.AppendLine("  "+n);foreach(var p in plan.Parts.Where(q=>q.Band))sb.AppendLine("  "+p.Fit);
    if(plan.Light!=null)sb.AppendLine("  light "+Str(plan.Light,"name")+" at "+V(plan.LightPos)+": Point intensity "+F(Num(plan.Light,"intensity"),"F1")+", range "+F(Num(plan.Light,"range_m"),"F1")+", no shadows");
    if(have!=null){if(Same(cfg,have,plan)){sb.AppendLine("  "+plan.Id+": in the scene, same pose");continue;}throw new Refuse(plan.Id+" is in "+scene.path+" with another pose / parts than the data gives - scene-revert:"+t.Alias+" first. Nothing written");}
    sb.AppendLine("  create "+cfg.Root+"/"+plan.Id+" at "+V(plan.Holder)+": "+plan.Parts.Count+" renderer(s) ("+plan.Parts.Count(q=>q.Band)+" vein band(s)), "+(plan.Light!=null?1:0)+" Point light, 0 colliders");
    todo.Add(plan);
   }
   foreach(var o in Arr(cfg.J,"outcrops").Where(o=>!Flag(o,"enabled")))if(root!=null&&root.Find(Str(o,"id"))!=null)throw new Refuse(Str(o,"id")+" is off in the data but in the scene - scene-revert:"+t.Alias+" first");
   if(todo.Count==0)return sb.Append("  변경 없음 (no change; WARN "+warns+")").ToString();
   if(dry)return sb.Append("  plan only: nothing written (would create "+todo.Count+" outcrop(s); WARN "+warns+")").ToString();
   string abs=Harness303.Abs(t.Scene);var write=new Write{utc=DateTime.UtcNow.ToString("O"),target=t.Scene,backup=Backup(t.Scene),shaBefore=Harness303.Sha(abs),dataSha=cfg.Sha};
   var ledger=ReadLedger(t)??new Ledger{group=Opt(cfg.J,"group")??"",target=t.Scene,created=DateTime.UtcNow.ToString("O")};
   if(root==null){var go=new GameObject(cfg.Root);SceneManager.MoveGameObjectToScene(go,scene);root=go.transform;write.rows.Add(new Row{kind="create-root",key=cfg.Root,detail="root"});}
   foreach(var plan in todo)
   {
    var holder=new GameObject(plan.Id).transform;holder.SetParent(root,false);holder.position=plan.Holder;
    foreach(var p in plan.Parts)
    {
     var go=new GameObject(p.Name);go.transform.SetParent(holder,false);go.transform.SetPositionAndRotation(p.Pos,p.Rot);go.transform.localScale=p.Scale;
     go.AddComponent<MeshFilter>().sharedMesh=p.Mesh;var mr=go.AddComponent<MeshRenderer>();mr.sharedMaterials=p.Mats;
     mr.shadowCastingMode=p.Shadows?ShadowCastingMode.On:ShadowCastingMode.Off;if(p.Band)mr.receiveShadows=false;
    }
    if(plan.Light!=null)
    {
     var lg=new GameObject(Str(plan.Light,"name"));lg.transform.SetParent(holder,false);lg.transform.position=plan.LightPos;var c=Vec(plan.Light,"color");
     var l=lg.AddComponent<Light>();l.type=LightType.Point;l.color=new Color(c.x,c.y,c.z);l.intensity=Num(plan.Light,"intensity");l.range=Num(plan.Light,"range_m");l.shadows=LightShadows.None;
    }
    write.rows.Add(new Row{kind="create",key=BuildingAudit308.KeyOf(holder),detail=plan.Id+" at "+V(plan.Holder)});
   }
   ledger.writes.Add(write);WriteLedger(t,ledger);   // the ledger is on disk before the scene is saved
   EditorSceneManager.MarkSceneDirty(scene);
   if(!EditorSceneManager.SaveScene(scene))
   {
    ledger.writes.Remove(write);WriteLedger(t,ledger);EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);
    return "FAILED SaveScene returned false for "+t.Scene+" (scene reloaded from disk, nothing saved, the ledger row was taken out again; backup "+write.backup+")";
   }
   write.shaAfter=Harness303.Sha(abs);WriteLedger(t,ledger);
   return sb.Append("  saved: "+todo.Count+" outcrop(s); WARN "+warns+"; scene sha "+Short(write.shaBefore)+" -> "+Short(write.shaAfter)+"; backup "+write.backup+"; ledger "+LedgerFile(t)).ToString();
  }

  static string Revert(string arg)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,true);var ledger=ReadLedger(t);var live=Live(ledger).ToList();
   if(live.Count==0)return "OreOutcrop308 scene-revert "+t.Alias+": 변경 없음 (no live ledger row)";
   var scene=Open(t);var sb=new StringBuilder("OreOutcrop308 scene-revert "+t.Alias+"\n");string copy=Backup(t.Scene);int kept=0;var marked=new List<Row>();
   foreach(var r in live.Where(x=>x.kind=="create").Concat(live.Where(x=>x.kind=="create-root")))
   {
    var tr=BuildingAudit308.Resolve(scene,r.key);
    if(tr==null){r.reverted=true;marked.Add(r);sb.AppendLine("  "+r.key+": already gone");continue;}
    if(BuildingAudit308.KeyOf(tr)!=r.key||tr.root.name!=cfg.Root){kept++;sb.AppendLine("  KEPT "+r.key+": the key no longer names this tool's object");continue;}
    if(r.kind=="create-root"&&tr.childCount>0){kept++;sb.AppendLine("  KEPT "+r.key+": "+tr.childCount+" child object(s) are still inside");continue;}
    Object.DestroyImmediate(tr.gameObject);r.reverted=true;marked.Add(r);sb.AppendLine("  removed "+r.key);
   }
   WriteLedger(t,ledger);EditorSceneManager.MarkSceneDirty(scene);
   if(!EditorSceneManager.SaveScene(scene))
   {
    // the objects are back after the reload: the rows this call marked are live again, so a second scene-revert finds them
    foreach(var r in marked)r.reverted=false;WriteLedger(t,ledger);EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);
    return "FAILED SaveScene returned false for "+t.Scene+" (scene reloaded from disk, nothing saved; the "+marked.Count+" ledger row(s) of this call are live again - run scene-revert again; backup "+copy+")";
   }
   return sb.Append("  saved; kept "+kept+"; scene sha "+Short(Harness303.Sha(Harness303.Abs(t.Scene)))+"; backup "+copy).ToString();
  }

  // ---------- check (read-only) ----------

  static string Check(string arg)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,false);var scene=Open(t);int fail=0;
   var sb=new StringBuilder("OreOutcrop308 check "+t.Alias+" (data "+Short(cfg.Sha)+")\n");
   void C(bool ok,string what){if(!ok)fail++;sb.AppendLine("  ["+(ok?"PASS":"FAIL")+"] "+what);}
   var root=Root(cfg,scene);var ids=Enabled(cfg).Select(o=>Str(o,"id")).ToList();
   C(root!=null&&ids.All(id=>root.Find(id)!=null)&&root.childCount==ids.Count,"AC-O1 "+cfg.Root+" holds exactly the enabled outcrops ("+string.Join(", ",ids)+")");
   if(root==null)return sb.Append("  FAIL "+fail).ToString();
   var lights=root.GetComponentsInChildren<Light>(true);var lm=Req(cfg.Rules,"light_max");
   C(lights.Length<=Mathf.RoundToInt(cfg.R("lights_added_per_scene"))&&lights.All(l=>l.type==LightType.Point&&l.shadows==LightShadows.None&&l.intensity<=Num(lm,"intensity")+1e-4f&&l.range<=Num(lm,"range_m")+1e-4f),
    "AC-O4 lights under the root: "+lights.Length+" (<= "+F(cfg.R("lights_added_per_scene"),"F0")+"), Point, no shadows, intensity <= "+F(Num(lm,"intensity"),"F1")+", range <= "+F(Num(lm,"range_m"),"F1"));
   var rends=root.GetComponentsInChildren<Renderer>(true);var veinGuids=new HashSet<string>(Enabled(cfg).Select(o=>Str(o["vein_material"],"guid")));
   bool Glows(Material m)=>m!=null&&(m.shader.name=="Oheangbu/InkLightSource"||m.shader.name.Contains("InkBeacon")||m.shader.name.Contains("FarGlow")||m.shader.name.Contains("BeaconHalo")||m.IsKeywordEnabled("_EMISSION"));
   var glow=rends.Where(r=>r.sharedMaterials.Any(Glows)).ToList();
   int glowMax=Enabled(cfg).Count()*Mathf.RoundToInt(cfg.R("bands_max_per_outcrop"));
   C(glow.Count<=glowMax&&glow.All(r=>r.name.Contains("Ore_Vein")&&r.transform.lossyScale.x<=cfg.R("band_scale_max")+1e-3f&&r.sharedMaterials.All(m=>m!=null&&veinGuids.Contains(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m))))),
    "AC-O5 sustained emission under the root = the vein bands only ("+glow.Count+" renderer(s) <= "+glowMax+", scale <= "+F(cfg.R("band_scale_max"))+", existing Ore_Vein material, names hold \"Ore_Vein\"); glow cards 0");
   C(root.GetComponentsInChildren<Collider>(true).Length==0,"AC-O6 colliders under the root: 0 (no NavMesh bake)");
   C(!PostLedger308.UnderProtectedTree(root)&&rends.All(r=>r.GetComponent<MeshFilter>()!=null&&EditorUtility.IsPersistent(r.GetComponent<MeshFilter>().sharedMesh)),"AC-O2 every mesh is an existing asset (nothing generated), outside the protected trees");
   foreach(var o in Enabled(cfg))
   {
    var h=root.Find(Str(o,"id"));if(h==null)continue;var plan=Build(cfg,scene,o);
    C(Same(cfg,h,plan),"AC-O3 "+plan.Id+" stands where the data and the physical ground put it ("+V(h.position)+")");
    foreach(var p in plan.Parts.Where(q=>q.Band))sb.AppendLine("    "+p.Fit);foreach(var n in plan.Notes)sb.AppendLine("    "+n);
    C(plan.Warns==0,"AC-O7 "+plan.Id+": ground / band-fit warnings "+plan.Warns+" (the still judges the look; a WARN here is reported with the stills)");
   }
   return sb.Append("  "+(fail==0?"PASS":"FAIL "+fail)).ToString();
  }

  // ---------- stills ----------

  static string Shot(string arg)
  {
   var cfg=Load();string label="";int li=arg.IndexOf(":label=",StringComparison.Ordinal);if(li>=0){label=arg.Substring(li+7).Trim();arg=arg.Substring(0,li);}
   var parts=arg.Split(':');if(parts.Length!=3)throw new Refuse("shot:<scene>:<eye>:<PC|Mobile>[:label=<text>]");
   var t=TargetOf(cfg,parts[0]);PreviewGuard(cfg,t,false);var scene=Open(t);var st=Req(cfg.J,"stills");
   if(!Arr(st,"tiers").Any(x=>x.Value<string>()==parts[2]))throw new Refuse("tier '"+parts[2]+"' is not in stills.tiers");
   var o=Enabled(cfg).FirstOrDefault();if(o==null)throw new Refuse("no enabled outcrop");var plan=Build(cfg,scene,o);
   Part Band(string id)=>plan.Parts.FirstOrDefault(p=>p.Band&&p.Name==plan.Id+"_"+id)??throw new Refuse("stills band '"+id+"' is not a band of "+plan.Id);
   Vector3 eye,target;float eh=Num(st,"eye_height_m");
   if(parts[1]=="close"){var b=Band(Str(st,"target_band"));var e=b.Face+new Vector3(b.Normal.x,0,b.Normal.z).normalized*Num(st,"close_distance_m");if(!Ground(cfg,e.x,e.z,out var g,out _))throw new Refuse("no ground under the close eye");eye=g+Vector3.up*eh;target=b.Face;}
   else
   {
    var row=Arr(st,"eyes").FirstOrDefault(x=>Str(x,"name")==parts[1]);if(row==null)throw new Refuse("eye '"+parts[1]+"' is not in stills.eyes ("+string.Join(", ",Arr(st,"eyes").Select(x=>Str(x,"name")))+", close)");
    if(!Ground(cfg,Num(row,"x"),Num(row,"z"),out var g,out _))throw new Refuse("no ground under eye "+parts[1]);
    eye=g+Vector3.up*eh;target=Band(parts[1]=="sanctuary_side"?Str(st,"target_band_return"):Str(st,"target_band")).Face;
   }
   string name=Str(st,"prefix")+"_"+(label.Length>0?label+"_":"")+t.Alias+"_"+parts[1]+"_"+parts[2];
   string file=Presentation297.ShotTier308(name,eye,target,Num(st,"fov"),Mathf.RoundToInt(Num(st,"w")),Mathf.RoundToInt(Num(st,"h")),parts[2],true);
   return "OreOutcrop308 shot "+name+": eye "+V(eye)+" -> "+V(target)+" ("+F((target-eye).magnitude,"F1")+" m), "+parts[2]+", quality level now "+QualitySettings.GetQualityLevel()+" -> "+file;
  }

  // ---------- status ----------

  static string Status()
  {
   var sb=new StringBuilder("OreOutcrop308 status (play="+EditorApplication.isPlaying+")\n");
   Cfg cfg;try{cfg=Load();}catch(Refuse r){return sb.Append("  "+r.Message).ToString();}
   sb.AppendLine("  data "+DataFile+" sha "+Short(cfg.Sha)+" version "+(Opt(cfg.J,"version")??"?")+", group "+(Opt(cfg.J,"group")??"")+", enabled outcrops "+string.Join(", ",Enabled(cfg).Select(o=>Str(o,"id"))));
   foreach(var t in cfg.Targets)sb.AppendLine("  "+t.Alias+": live ledger rows "+Live(ReadLedger(t)).Count());
   StatusV2(cfg,sb);
   var active=SceneManager.GetActiveScene();var root=Root(cfg,active);
   return sb.Append("  active scene "+active.path+(active.isDirty?" (dirty)":"")+": "+cfg.Root+" "+(root==null?"absent":"present, "+root.childCount+" outcrop(s)")).ToString();
  }
 }
}
