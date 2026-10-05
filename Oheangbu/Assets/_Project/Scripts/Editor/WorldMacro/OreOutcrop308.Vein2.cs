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
 // #308 심부 광맥 노두 - SECOND try (D308-22 answer 2; SPEC-ORE-OUTCROP-308 "2회차") [TEST values throughout].
 // An outcrops[] row with kind "split_vein" is not put together from old meshes: it is BUILT. The meshes come from JSON mesh
 // packs (Art/World/Compact/Rebuild/CliffBoundary308/ore308_mesh, made by Tools/Unity/Stage308_world18/ore2/ore2_build.py in
 // headless Blender - the Rock275 precedent: Blender -> JSON -> Mesh asset). This file
 //   bake            turns the packs into Mesh assets under the row's asset_dir (each pack must hash to the sha the data holds);
 //                   an asset ledger remembers what was created. bake-plan = the same, nothing written. No AssetDatabase.SaveAssets.
 //   bake-revert     copies those assets to Art/Playtest308/Pacing/Backups and takes them out (refused while a scene still holds
 //                   the outcrop). The asset ledger is a file on disk: a path it names is taken out ONLY when it is a mesh asset
 //                   this tool could have baked (Ore308_*.asset under the asset_dir of a split_vein row of the data, under
 //                   Assets/_Project, not protected) - any other row is reported KEPT and left live (review O3)
 //   plan / scene-apply / check / shot   the first try's cycle for the built outcrop: a holder on the physical ground, one child
 //                   per part and LOD (identity pose: the packs are in the outcrop frame), a LODGroup of two levels, at most one
 //                   Point light inside rules.light_max. No collider (no NavMesh bake). scene-revert is the first try's own.
 // The FORM is measured, not assumed (Measure): rays across the fissure mouth name what one sees there - rock, selvage, vein,
 // nodule - and give the recess of the vein behind the two lips, the dark width on both sides, the opening, the nodules' height
 // over the vein face, the burial of every broken piece on the PHYSICAL ground. ore2_fit.py does the same on the packs offline.
 // Every number, id and path comes from ore308.json. Queue-safe: no dialog; refusals come back as "REFUSED ...".
 public static partial class OreOutcrop308
 {
  const string KindSplit="split_vein";
  static readonly string[] ClassName={"rockW","rockE","selvage","gouge","vein","nodule"};
  static bool IsSplit(JToken o)=>Opt(o,"kind")==KindSplit;
  static bool V2(Cfg cfg)
  {
   var en=Enabled(cfg).ToList();int s=en.Count(IsSplit);
   if(s>0&&s<en.Count)throw new Refuse("ore308.json: first-try rows and split_vein rows are enabled together - the test install is ONE outcrop (enable one kind)");
   if(s>1)throw new Refuse("ore308.json: "+s+" split_vein rows are enabled - the test install is ONE outcrop");
   return s>0;
  }
  static JToken SplitRow(Cfg cfg)=>Enabled(cfg).First(IsSplit);
  static JToken Form(Cfg cfg)=>Req(cfg.Rules,"form");

  // ---------- mesh packs -> Mesh assets ----------

  [Serializable] sealed class Pack{public float[] vertices,normals,uv;public int[] triangles,submeshStarts;public string meta;}
  [Serializable] sealed class AssetRow{public string path="",pack="",sha="",utc="";public bool reverted;}
  [Serializable] sealed class AssetLedger{public string kind="ore308-assets";public List<AssetRow> rows=new List<AssetRow>();}
  static string AssetLedgerFile=>Path.Combine(OutDir,"ledger_ore308_assets.json");
  static AssetLedger ReadAssetLedger(){var f=AssetLedgerFile;return File.Exists(f)?JsonUtility.FromJson<AssetLedger>(File.ReadAllText(f)):new AssetLedger();}
  static void WriteAssetLedger(AssetLedger l){Directory.CreateDirectory(OutDir);File.WriteAllText(AssetLedgerFile,JsonUtility.ToJson(l,true));}
  static string PackAbs(JToken o,string file)=>Path.Combine(Harness303.RepoRoot,Str(o,"mesh_dir").Replace('/',Path.DirectorySeparatorChar),file);
  static string AssetOf(JToken o,string file)=>Str(o,"asset_dir").TrimEnd('/')+"/Ore308_"+Path.GetFileNameWithoutExtension(file)+".asset";
  sealed class PackRef{public string File,Sha;public JToken Expect,Subs;}
  static List<PackRef> Packs(JToken o)
  {
   var list=new List<PackRef>();
   foreach(var p in Arr(o,"parts"))foreach(var lod in new[]{"LOD0","LOD1"})
   {
    string f=Str(Req(p,"lods"),lod);if(list.Any(x=>x.File==f))continue;
    list.Add(new PackRef{File=f,Sha=Str(Req(p,"sha256"),lod),Expect=Req(Req(p,"expect"),lod),Subs=Req(Req(p,"expect_submeshes"),lod)});
   }
   return list;
  }
  static Mesh BuildMesh(Pack p,string name)
  {
   if(p==null||p.vertices==null||p.normals==null||p.uv==null||p.triangles==null)throw new Refuse("mesh pack "+name+" does not hold vertices / normals / uv / triangles");
   int n=p.vertices.Length/3;if(n==0||p.normals.Length!=3*n||p.uv.Length!=2*n||p.triangles.Length%3!=0)throw new Refuse("mesh pack "+name+": array lengths do not agree");
   var v=new Vector3[n];var nr=new Vector3[n];var uv=new Vector2[n];
   for(int i=0;i<n;i++){v[i]=new Vector3(p.vertices[3*i],p.vertices[3*i+1],p.vertices[3*i+2]);nr[i]=new Vector3(p.normals[3*i],p.normals[3*i+1],p.normals[3*i+2]);uv[i]=new Vector2(p.uv[2*i],p.uv[2*i+1]);}
   if(p.triangles.Any(t=>t<0||t>=n))throw new Refuse("mesh pack "+name+": a triangle index is outside the vertices");
   var m=new Mesh{name=name,indexFormat=n>65000?IndexFormat.UInt32:IndexFormat.UInt16};m.vertices=v;m.normals=nr;m.uv=uv;
   var st=p.submeshStarts!=null&&p.submeshStarts.Length>0?p.submeshStarts:new[]{0};m.subMeshCount=st.Length;
   for(int s=0;s<st.Length;s++){int a=st[s],b=s+1<st.Length?st[s+1]:p.triangles.Length;var t=new int[Mathf.Max(0,b-a)];Array.Copy(p.triangles,a,t,0,t.Length);m.SetTriangles(t,s);}
   // no tangents on purpose: the neighbour rock's mesh has none, and WorldMacroVegetation then shades with the geometric normal - the same on both
   m.RecalculateBounds();return m;
  }
  static int TriCount(Mesh m){int n=0;for(int s=0;s<m.subMeshCount;s++)n+=(int)m.GetIndexCount(s)/3;return n;}
  static string Mismatch(Cfg cfg,Mesh m,PackRef r)
  {
   if(m.vertexCount!=Mathf.RoundToInt(Num(r.Expect,"vertices")))return "vertices "+m.vertexCount+" != "+Num(r.Expect,"vertices").ToString("F0",CultureInfo.InvariantCulture);
   if(TriCount(m)!=Mathf.RoundToInt(Num(r.Expect,"triangles")))return "triangles "+TriCount(m)+" != "+Num(r.Expect,"triangles").ToString("F0",CultureInfo.InvariantCulture);
   if(m.subMeshCount!=((JArray)r.Subs).Count)return "submeshes "+m.subMeshCount+" != "+((JArray)r.Subs).Count;
   float tol=Num(Req(cfg.Rules,"mesh"),"bounds_tol_m");
   if((m.bounds.min-Vec(r.Expect,"min")).magnitude>tol||(m.bounds.max-Vec(r.Expect,"max")).magnitude>tol)return "bounds "+V(m.bounds.min)+" .. "+V(m.bounds.max)+" are not the pack's";
   return null;
  }
  static void EnsureFolder(string folder)
  {
   if(AssetDatabase.IsValidFolder(folder))return;string parent=Path.GetDirectoryName(folder).Replace('\\','/');
   if(string.IsNullOrEmpty(parent))throw new Refuse("asset_dir '"+folder+"' has no parent folder");EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(folder));
  }
  static int LiveSceneRows(Cfg cfg)=>cfg.Targets.Sum(t=>Live(ReadLedger(t)).Count());
  // review O3: is this ledger path one that Bake() writes? (the same folder rule as Bake, the file name AssetOf() gives)
  static bool BakedHere(Cfg cfg,string path)
  {
   if(string.IsNullOrEmpty(path)||path.IndexOf("..",StringComparison.Ordinal)>=0||path.IndexOf('\\')>=0)return false;
   if(!path.StartsWith("Assets/_Project/",StringComparison.Ordinal)||!path.EndsWith(".asset",StringComparison.Ordinal)||!Path.GetFileName(path).StartsWith("Ore308_",StringComparison.Ordinal))return false;
   if(Harness303.IsProtected(path)||PostLedger308.IsProtectedPath(path))return false;
   return Arr(cfg.J,"outcrops").Where(IsSplit).Any(o=>path.StartsWith(Str(o,"asset_dir").TrimEnd('/')+"/",StringComparison.Ordinal)&&path.IndexOf('/',Str(o,"asset_dir").TrimEnd('/').Length+1)<0);
  }

  static string Bake(bool planOnly)
  {
   var cfg=Load();if(!V2(cfg))return "REFUSED bake: no enabled split_vein row in ore308.json (the first try bakes nothing)";
   var o=SplitRow(cfg);string dir=Str(o,"asset_dir").TrimEnd('/');
   if(!dir.StartsWith("Assets/_Project/",StringComparison.Ordinal)||Harness303.IsProtected(dir)||PostLedger308.IsProtectedPath(dir))throw new Refuse("asset_dir '"+dir+"' is not a free folder under Assets/_Project");
   var sb=new StringBuilder("OreOutcrop308 "+(planOnly?"bake-plan":"bake")+" (data "+Short(cfg.Sha)+" "+(Opt(cfg.J,"version")??"?")+") -> "+dir+"\n");
   var todo=new List<(PackRef r,Mesh mesh,string path,bool replace)>();int kept=0,live=LiveSceneRows(cfg);
   foreach(var r in Packs(o))
   {
    string abs=PackAbs(o,r.File);if(!File.Exists(abs))throw new Refuse("mesh pack missing: "+abs+" (copy Tools/Unity/Stage308_world18/ore2/Data/ore308_mesh there)");
    string sha=Harness303.Sha(abs);if(!string.Equals(sha,r.Sha,StringComparison.OrdinalIgnoreCase))throw new Refuse("mesh pack "+r.File+" is not the one the data was derived from (file "+Short(sha)+", data "+Short(r.Sha)+"). Nothing written");
    Pack p;try{p=JsonUtility.FromJson<Pack>(File.ReadAllText(abs));}catch(Exception e){throw new Refuse("mesh pack "+r.File+" does not parse ("+e.Message+")");}
    string path=AssetOf(o,r.File);var mesh=BuildMesh(p,Path.GetFileNameWithoutExtension(path));string bad=Mismatch(cfg,mesh,r);
    if(bad!=null){Object.DestroyImmediate(mesh);throw new Refuse("mesh pack "+r.File+" does not give the mesh the data expects ("+bad+"). Nothing written");}
    var have=AssetDatabase.LoadAssetAtPath<Mesh>(path);
    if(have!=null&&Mismatch(cfg,have,r)==null){kept++;sb.AppendLine("  kept    "+path+" ("+have.vertexCount+" vertices, "+TriCount(have)+" triangles, "+have.subMeshCount+" submesh(es))");Object.DestroyImmediate(mesh);continue;}
    if(have!=null&&live>0){Object.DestroyImmediate(mesh);throw new Refuse(path+" exists with another mesh and "+live+" scene ledger row(s) are live - scene-revert the three scenes first. Nothing written");}
    if(have==null&&File.Exists(Harness303.Abs(path))){Object.DestroyImmediate(mesh);throw new Refuse(path+" exists but does not load as a Mesh. Nothing written");}
    sb.AppendLine("  "+(have!=null?"rebake ":"create ")+path+" ("+mesh.vertexCount+" vertices, "+TriCount(mesh)+" triangles, "+mesh.subMeshCount+" submesh(es), pack "+Short(sha)+")");todo.Add((r,mesh,path,have!=null));
   }
   if(planOnly){foreach(var t in todo)Object.DestroyImmediate(t.mesh);return sb.Append("  plan only: nothing written (would write "+todo.Count+" mesh asset(s), keep "+kept+")").ToString();}
   if(todo.Count==0)return sb.Append("  변경 없음 (no change: "+kept+" mesh asset(s) already equal the packs)").ToString();
   var ledger=ReadAssetLedger();EnsureFolder(dir);
   foreach(var t in todo)
   {
    ledger.rows.Add(new AssetRow{path=t.path,pack=t.r.File,sha=t.r.Sha,utc=DateTime.UtcNow.ToString("O")});WriteAssetLedger(ledger);   // the ledger first
    if(t.replace){var have=AssetDatabase.LoadAssetAtPath<Mesh>(t.path);EditorUtility.CopySerialized(t.mesh,have);have.name=Path.GetFileNameWithoutExtension(t.path);EditorUtility.SetDirty(have);AssetDatabase.SaveAssetIfDirty(have);Object.DestroyImmediate(t.mesh);}
    else AssetDatabase.CreateAsset(t.mesh,t.path);
   }
   return sb.Append("  wrote "+todo.Count+" mesh asset(s), kept "+kept+"; asset ledger "+AssetLedgerFile+" (AssetDatabase.SaveAssets was not called)").ToString();
  }

  static string BakeRevert()
  {
   var cfg=Load();int live=LiveSceneRows(cfg);if(live>0)return "REFUSED bake-revert: "+live+" scene ledger row(s) are live - scene-revert main, folk298, arch296 first";
   var ledger=ReadAssetLedger();var rows=ledger.rows.Where(r=>r!=null&&!r.reverted).ToList();if(rows.Count==0)return "OreOutcrop308 bake-revert: 변경 없음 (no live asset ledger row)";
   string dir=Path.Combine(OutDir,"Backups","ore308-assets-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff",CultureInfo.InvariantCulture));Directory.CreateDirectory(dir);
   var sb=new StringBuilder("OreOutcrop308 bake-revert\n");int gone=0,kept=0;
   foreach(var r in rows.GroupBy(x=>x.path).Select(g=>g.Last()))
   {
    if(!BakedHere(cfg,r.path)){kept++;sb.AppendLine("  KEPT "+r.path+": not an Ore308_*.asset under an asset_dir of the data (the ledger row stays live, nothing deleted)");continue;}
    string abs=Harness303.Abs(r.path);
    if(File.Exists(abs))
    {
     File.Copy(abs,Path.Combine(dir,Path.GetFileName(abs)),true);if(File.Exists(abs+".meta"))File.Copy(abs+".meta",Path.Combine(dir,Path.GetFileName(abs)+".meta"),true);
     if(!AssetDatabase.DeleteAsset(r.path)){sb.AppendLine("  KEPT "+r.path+": DeleteAsset returned false");continue;}
     gone++;sb.AppendLine("  took out "+r.path);
    }
    else sb.AppendLine("  "+r.path+": already gone");
    foreach(var same in rows.Where(y=>y.path==r.path))same.reverted=true;
   }
   WriteAssetLedger(ledger);return sb.Append("  "+gone+" mesh asset(s) taken out"+(kept>0?", "+kept+" ledger row(s) KEPT (see above)":"")+"; copies in "+dir).ToString();
  }

  // ---------- the plan of a built outcrop ----------

  sealed class Part2{public string Name,Role;public Mesh L0,L1;public Material[] Mats;public bool Shadows,Emissive;}
  sealed class Plan2{public string Id;public Vector3 Holder;public List<Part2> Parts=new List<Part2>();public JToken Light;public Vector3 LightPos;public List<string> Notes=new List<string>();public List<(string id,bool ok,string text)> Lines=new List<(string,bool,string)>();public int Warns;}
  static bool Glows(Material m)=>m!=null&&(m.shader.name=="Oheangbu/InkLightSource"||m.shader.name.Contains("InkBeacon")||m.shader.name.Contains("FarGlow")||m.shader.name.Contains("BeaconHalo")||m.IsKeywordEnabled("_EMISSION"));

  static Plan2 Build2(Cfg cfg,Scene scene,JToken o,Transform inScene)
  {
   var plan=new Plan2{Id=Str(o,"id")};float x=Num(o,"x"),z=Num(o,"z");Physics.SyncTransforms();
   if(plan.Id.IndexOf("Ore_Vein",StringComparison.Ordinal)<0)throw new Refuse("outcrop id '"+plan.Id+"' must hold \"Ore_Vein\" (SPEC-EVENT-WASH-308 allow list)");
   if(!Ground(cfg,x,z,out var g,out float slope))throw new Refuse("no terrain under "+plan.Id+" ("+F(x,"F1")+", "+F(z,"F1")+") - is the scene loaded?");
   if(slope>cfg.R("max_slope_deg"))throw new Refuse(plan.Id+": physical slope "+F(slope,"F1")+"° > "+F(cfg.R("max_slope_deg"),"F1")+"° - revise the row. Nothing written");
   float dy=g.y-Num(o,"y_offline");plan.Holder=g;bool far=Mathf.Abs(dy)>cfg.R("y_tol_m");if(far)plan.Warns++;
   plan.Notes.Add((far?"WARN ":"")+"ground "+V(g)+" slope "+F(slope,"F1")+"° (offline y "+F(Num(o,"y_offline"))+", Δ "+F(dy)+" m - the packs were seated on the offline height field; the whole outcrop moves with the editor value)");
   var mats=Req(o,"materials");var refs=Packs(o);
   foreach(var p in Arr(o,"parts"))
   {
    var part=new Part2{Name=Str(p,"name"),Role=Str(p,"role"),Shadows=Flag(p,"shadows"),Emissive=Flag(p,"emissive")};
    if(part.Name.IndexOf(plan.Id,StringComparison.Ordinal)!=0)throw new Refuse("part '"+part.Name+"' must start with the outcrop id "+plan.Id);
    Mesh Load2(string lod)
    {
     string f=Str(Req(p,"lods"),lod),path=AssetOf(o,f);var m=AssetDatabase.LoadAssetAtPath<Mesh>(path);var r=refs.First(q=>q.File==f);
     if(m==null)throw new Refuse("mesh asset missing: "+path+" - run OreOutcrop308 \"bake\" first. Nothing written");
     string bad=Mismatch(cfg,m,r);if(bad!=null)throw new Refuse(path+" is not the mesh of pack "+f+" ("+bad+") - run OreOutcrop308 \"bake\". Nothing written");return m;
    }
    part.L0=Load2("LOD0");part.L1=Load2("LOD1");
    part.Mats=Arr(p,"materials").Select(s=>
    {
     var row=Req(mats,Str(s,"slot"));if(Str(row,"guid")!=Str(s,"guid"))throw new Refuse("part "+part.Name+": slot "+Str(s,"slot")+" guid is not materials."+Str(s,"slot")+".guid");
     var m=Asset<Material>(Str(s,"guid"),"material "+Str(s,"slot"));if(m.shader==null||m.shader.name!=Str(row,"shader"))throw new Refuse("material "+m.name+" shader is "+(m.shader!=null?m.shader.name:"null")+", expected "+Str(row,"shader"));return m;
    }).ToArray();
    if(part.Mats.Length!=part.L0.subMeshCount||part.L1.subMeshCount<1||part.L1.subMeshCount>part.Mats.Length)throw new Refuse("part "+part.Name+": "+part.Mats.Length+" material slot(s) for "+part.L0.subMeshCount+" / "+part.L1.subMeshCount+" submesh(es)");
    if(part.Mats.Any(Glows)!=part.Emissive||part.Mats.Any(Glows)!=part.Mats.All(Glows))throw new Refuse("part "+part.Name+": emissive flag "+part.Emissive+" does not agree with its materials (only the Ore_Vein material may glow, and an emissive part holds nothing else)");
    plan.Parts.Add(part);
   }
   foreach(var role in new[]{"hostW","hostE","selvage","lode","spall","orechunk","floatore"})if(plan.Parts.Count(q=>q.Role==role)!=1)throw new Refuse(plan.Id+": exactly one part with role '"+role+"' is needed");
   var li=o["light"];
   if(li!=null&&Flag(li,"enabled"))
   {
    var lm=Req(cfg.Rules,"light_max");if(Num(li,"intensity")>Num(lm,"intensity")||Num(li,"range_m")>Num(lm,"range_m")||Flag(li,"shadows")||Str(li,"type")!="Point")throw new Refuse("light row is outside rules.light_max (Point, no shadows)");
    plan.Light=li;plan.LightPos=g+Vec(li,"local");
   }
   Measure(cfg,o,plan,inScene);
   return plan;
  }

  // ---------- the form, measured ----------

  sealed class Tris{public List<Vector3> A=new List<Vector3>(),E1=new List<Vector3>(),E2=new List<Vector3>();public List<int> Cls=new List<int>();
   public void Add(Vector3[] w,int[] t,int cls){for(int i=0;i+2<t.Length;i+=3){var p=w[t[i]];A.Add(p);E1.Add(w[t[i+1]]-p);E2.Add(w[t[i+2]]-p);Cls.Add(cls);}}
   public bool Hit(Vector3 o,Vector3 d,out float best,out int cls)
   {
    best=float.MaxValue;cls=-1;
    for(int i=0;i<A.Count;i++)
    {
     Vector3 e1=E1[i],e2=E2[i],pv=Vector3.Cross(d,e2);float det=Vector3.Dot(e1,pv);if(det>-1e-10f&&det<1e-10f)continue;
     float inv=1f/det;Vector3 tv=o-A[i];float u=Vector3.Dot(tv,pv)*inv;if(u<0f||u>1f)continue;
     Vector3 qv=Vector3.Cross(tv,e1);float v=Vector3.Dot(d,qv)*inv;if(v<0f||u+v>1f)continue;
     float t=Vector3.Dot(e2,qv)*inv;if(t>1e-5f&&t<best){best=t;cls=Cls[i];}
    }
    return cls>=0;
   }}
  // vertices of a part in the OUTCROP FRAME (holder = origin): from the scene object when the outcrop stands there (check), else the pack pose
  static Vector3[] FrameVerts(Plan2 plan,Part2 part,Mesh mesh,string child,Transform inScene)
  {
   var v=mesh.vertices;var t=inScene!=null?inScene.Find(child):null;
   if(t==null)return v;var h=inScene.position;return v.Select(p=>t.TransformPoint(p)-h).ToArray();
  }
  static string ChildName(Part2 p,int lod)=>p.L0==p.L1?p.Name:p.Name+"_LOD"+lod;
  static float Median(List<float> a){if(a.Count==0)return 0f;var s=a.OrderBy(x=>x).ToList();return s.Count%2==1?s[s.Count/2]:(s[s.Count/2-1]+s[s.Count/2])*.5f;}

  static void Measure(Cfg cfg,JToken o,Plan2 plan,Transform inScene)
  {
   var Fm=Form(cfg);var E=Req(cfg.Rules,"emission");var B=Req(cfg.Rules,"budget");
   Part2 P(string role)=>plan.Parts.First(q=>q.Role==role);
   var tris=new Tris();
   void AddPart(string role,int[] cls){var p=P(role);var w=FrameVerts(plan,p,p.L0,ChildName(p,0),inScene);for(int s=0;s<p.L0.subMeshCount&&s<cls.Length;s++)tris.Add(w,p.L0.GetTriangles(s),cls[s]);}
   AddPart("hostW",new[]{0});AddPart("hostE",new[]{1});AddPart("selvage",new[]{2,3});AddPart("lode",new[]{4,5});
   float back=Num(Fm,"scan_back_m"),step=Num(Fm,"scan_step_m"),pad=Num(Fm,"scan_pad_m");
   // scan lines across the mouth
   var rec=new List<float>();var sw=new List<float>();var se=new List<float>();var open=new List<float>();var stp=new List<float>();int veinLines=0,both=0,lips=0,spans=0,lines=0;
   foreach(var s in Arr(o,"scan"))
   {
    lines++;Vector3 w=Vec(s,"w_lip"),e=Vec(s,"e_lip"),outv=Vec(s,"out").normalized,gdir=e-w;float Lm=gdir.magnitude;gdir/=Lm;var mid=(w+e)*.5f;
    int n=Mathf.RoundToInt((Lm+2*pad)/step);var cls=new int[n+1];var dep=new float[n+1];
    for(int i=0;i<=n;i++){float u=-Lm*.5f-pad+i*step;if(tris.Hit(mid+gdir*u+outv*back,-outv,out float t,out int c)){cls[i]=c;dep[i]=t-back;}else cls[i]=-1;}
    int first=-1,last=-1;for(int i=0;i<=n;i++)if(cls[i]>=2){if(first<0)first=i;last=i;}
    float span=first<0?0f:(last-first+1)*step;if(Mathf.Abs(span-Num(s,"mouth_m"))<=Num(Fm,"mouth_scan_tol_m"))spans++;
    bool lw=tris.Hit(w-gdir*.03f+outv*back,-outv,out float tw,out int cw)&&cw==0&&Mathf.Abs(tw-back)<=Num(Fm,"lip_tol_m");
    bool le=tris.Hit(e+gdir*.03f+outv*back,-outv,out float te,out int ce)&&ce==1&&Mathf.Abs(te-back)<=Num(Fm,"lip_tol_m");if(lw&&le)lips++;
    open.Add(Num(s,"open_m"));stp.Add(Num(s,"step_m"));
    if(Num(s,"vein_m")<=0f)continue;
    veinLines++;int v0=-1,v1=-1;float near=float.MaxValue;
    for(int i=0;i<=n;i++)if(cls[i]==4||cls[i]==5){if(v0<0)v0=i;v1=i;if(cls[i]==4&&dep[i]<near)near=dep[i];}
    if(v0<0||near==float.MaxValue)continue;
    rec.Add(near);int a=v0-1,dw=0;while(a>=0&&(cls[a]==2||cls[a]==3)){dw++;a--;}int b=v1+1,de=0;while(b<=n&&(cls[b]==2||cls[b]==3)){de++;b++;}
    sw.Add(dw*step);se.Add(de*step);if(dw*step>=Num(Fm,"selvage_min_m")-1e-4f&&de*step>=Num(Fm,"selvage_min_m")-1e-4f)both++;
   }
   bool ok=rec.Count>=Mathf.RoundToInt(Num(Fm,"vein_lines_min"))&&rec.All(v=>v>=Num(Fm,"recess_min_m")&&v<=Num(Fm,"recess_max_m"))&&Median(rec)>=Num(Fm,"recess_median_min_m");
   plan.Lines.Add(("AC-O11",ok,"recess: the vein's nearest point stands "+F(rec.Count>0?rec.Min():0f,"F3")+" - "+F(rec.Count>0?rec.Max():0f,"F3")+" m behind the mouth line on "+rec.Count+" of "+veinLines+" vein scan lines (median "+F(Median(rec),"F3")+"; allowed "+F(Num(Fm,"recess_min_m"))+" - "+F(Num(Fm,"recess_max_m"))+", median >= "+F(Num(Fm,"recess_median_min_m"),"F3")+", lines >= "+F(Num(Fm,"vein_lines_min"),"F0")+")"));
   float share=veinLines>0?both/(float)veinLines:0f;
   plan.Lines.Add(("AC-O12",share>=Num(Fm,"selvage_share_min"),"selvage: dark on BOTH sides of the vein (>= "+F(Num(Fm,"selvage_min_m"))+" m each) on "+both+" of "+veinLines+" vein scan lines = "+F(share*100,"F0")+" % (>= "+F(Num(Fm,"selvage_share_min")*100,"F0")+" %); west side "+F(sw.Count>0?sw.Min():0f)+" - "+F(sw.Count>0?sw.Max():0f)+" m, east side "+F(se.Count>0?se.Min():0f)+" - "+F(se.Count>0?se.Max():0f)+" m"));
   float need=lines*Num(Fm,"lip_share_min");ok=lines>0&&open.Min()>=Num(Fm,"mouth_min_m")&&open.Max()<=Num(Fm,"mouth_max_m")&&lips>=need&&spans>=need;
   plan.Lines.Add(("AC-O13",ok,"fissure: opening "+F(lines>0?open.Min():0f)+" - "+F(lines>0?open.Max():0f)+" m across the cut (allowed "+F(Num(Fm,"mouth_min_m"))+" - "+F(Num(Fm,"mouth_max_m"))+"); both lips found on their own half at the mouth line on "+lips+" of "+lines+" scan lines, the scanned mouth span equals the data on "+spans+" (>= "+F(Num(Fm,"lip_share_min")*100,"F0")+" % each)"));
   // nodules
   int nod=0,good=0;var proud=new List<float>();
   foreach(var nd in Arr(o,"nodules"))
   {
    nod++;Vector3 tip=Vec(nd,"tip"),bas=Vec(nd,"base"),ax=(tip-bas).normalized;bool found=tris.Hit(tip+ax*back,-ax,out float tt,out int tc)&&tc==5&&Mathf.Abs(tt-back)<=.02f;
    var a1=Vector3.Cross(ax,Vector3.up);a1=a1.sqrMagnitude>1e-12f?a1.normalized:Vector3.right;var a2=Vector3.Cross(ax,a1);float near=float.MaxValue,rad=Num(nd,"radius_m")*1.7f;
    for(int k=0;k<8;k++){var d=(a1*Mathf.Cos(k*Mathf.PI/4f)+a2*Mathf.Sin(k*Mathf.PI/4f))*rad;if(tris.Hit(tip+d+ax*back,-ax,out float tf,out int cf)&&cf==4&&tf-back<near)near=tf-back;}
    if(near==float.MaxValue)continue;proud.Add(near);if(found&&near>=Num(Fm,"nodule_proud_min_m")&&near<=Num(Fm,"nodule_proud_max_m"))good++;
   }
   ok=nod>=Mathf.RoundToInt(Num(Fm,"nodules_min"))&&nod<=Mathf.RoundToInt(Num(Fm,"nodules_max"))&&good>=nod*Num(Fm,"nodule_share_min");
   plan.Lines.Add(("AC-O14",ok,"nodules: "+nod+" (allowed "+F(Num(Fm,"nodules_min"),"F0")+" - "+F(Num(Fm,"nodules_max"),"F0")+"); "+good+" stand "+F(Num(Fm,"nodule_proud_min_m"))+" - "+F(Num(Fm,"nodule_proud_max_m"))+" m proud of the vein face beside them (>= "+F(Num(Fm,"nodule_share_min")*100,"F0")+" % of them); measured "+F(proud.Count>0?proud.Min():0f,"F3")+" - "+F(proud.Count>0?proud.Max():0f,"F3")+" m"));
   // debris on the PHYSICAL ground
   var dv=new List<Vector3>();foreach(var role in new[]{"spall","orechunk"}){var p=P(role);dv.AddRange(FrameVerts(plan,p,p.L0,ChildName(p,0),inScene));}
   var inside=new bool[dv.Count];int pieces=0,ore=0,buried=0,noGround=0;float big=0f,bmin=float.MaxValue,bmax=float.MinValue,btol=Num(Req(cfg.Rules,"mesh"),"bounds_tol_m")+.005f;
   foreach(var d in Arr(o,"debris"))
   {
    pieces++;Vector3 c=Vec(d,"centre"),half=Vec(d,"size")*.5f+Vector3.one*btol;float lo=float.MaxValue,hi=float.MinValue;
    for(int i=0;i<dv.Count;i++){var q=dv[i]-c;if(Mathf.Abs(q.x)<=half.x&&Mathf.Abs(q.y)<=half.y&&Mathf.Abs(q.z)<=half.z){inside[i]=true;lo=Mathf.Min(lo,dv[i].y);hi=Mathf.Max(hi,dv[i].y);}}
    big=Mathf.Max(big,Num(d,"largest_m"));if(Str(d,"kind")=="ore"&&Num(d,"vein_faces")>=1f)ore++;
    if(lo==float.MaxValue||!Ground(cfg,plan.Holder.x+c.x,plan.Holder.z+c.z,out var gp,out _)){noGround++;continue;}
    float bury=(gp.y-plan.Holder.y-lo)/Mathf.Max(hi-lo,1e-5f);bmin=Mathf.Min(bmin,bury);bmax=Mathf.Max(bmax,bury);if(bury>=Num(Fm,"debris_bury_min")&&bury<=Num(Fm,"debris_bury_max"))buried++;
   }
   int outside=inside.Count(q=>!q);
   ok=pieces>=Mathf.RoundToInt(Num(Fm,"debris_min"))&&pieces<=Mathf.RoundToInt(Num(Fm,"debris_max"))&&big>=Num(Fm,"debris_largest_min_m")&&big<=Num(Fm,"debris_largest_max_m")&&noGround==0&&buried==pieces&&ore>=Mathf.RoundToInt(Num(Fm,"debris_ore_min"))&&outside==0;
   plan.Lines.Add(("AC-O15",ok,"debris: "+pieces+" pieces (allowed "+F(Num(Fm,"debris_min"),"F0")+" - "+F(Num(Fm,"debris_max"),"F0")+"), the largest "+F(big)+" m (allowed "+F(Num(Fm,"debris_largest_min_m"))+" - "+F(Num(Fm,"debris_largest_max_m"))+"), buried "+F((bmin==float.MaxValue?0f:bmin)*100,"F0")+" - "+F((bmax==float.MinValue?0f:bmax)*100,"F0")+" % of their height on the physical ground (allowed "+F(Num(Fm,"debris_bury_min")*100,"F0")+" - "+F(Num(Fm,"debris_bury_max")*100,"F0")+"; "+noGround+" without a ground reading), "+ore+" carry the vein colour (>= "+F(Num(Fm,"debris_ore_min"),"F0")+"); mesh vertices outside the data boxes "+outside));
   int stepped=stp.Count(v=>v>=Num(Fm,"step_min_m"));
   plan.Lines.Add(("AC-O16",lines>0&&stepped>=lines*Num(Fm,"step_share_min")&&lips>=need,"step: the east half sits "+F(lines>0?stp.Min():0f)+" - "+F(lines>0?stp.Max():0f)+" m down the cut from the west half; >= "+F(Num(Fm,"step_min_m"))+" m on "+stepped+" of "+lines+" scan lines (>= "+F(Num(Fm,"step_share_min")*100,"F0")+" %; the lips are where the data says: "+lips+" of "+lines+")"));
   // emission cap + triangles
   float Area(Mesh m){var v=m.vertices;float a=0f;for(int s=0;s<m.subMeshCount;s++){var t=m.GetTriangles(s);for(int i=0;i+2<t.Length;i+=3)a+=Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]).magnitude*.5f;}return a;}
   float e0=plan.Parts.Where(q=>q.Emissive).Sum(q=>Area(q.L0)),e1=plan.Parts.Where(q=>q.Emissive).Sum(q=>Area(q.L1));int er=plan.Parts.Count(q=>q.Emissive);
   plan.Lines.Add(("AC-O17",Mathf.Max(e0,e1)<=Num(E,"emissive_area_max_m2")&&er<=Mathf.RoundToInt(Num(E,"emissive_renderers_max_per_lod")),"emission cap: emissive mesh area LOD0 "+F(e0)+" / LOD1 "+F(e1)+" m2 (<= "+F(Num(E,"emissive_area_max_m2"))+" = the first-try cap in area); emissive renderers per LOD "+er+" (<= "+F(Num(E,"emissive_renderers_max_per_lod"),"F0")+")"));
   int t0=plan.Parts.Sum(q=>TriCount(q.L0)),t1=plan.Parts.Sum(q=>TriCount(q.L1));
   plan.Lines.Add(("AC-O18",t0<=Mathf.RoundToInt(Num(B,"triangles_lod0_max"))&&t1<=Mathf.RoundToInt(Num(B,"triangles_lod1_max")),"triangles: LOD0 "+t0+" (<= "+F(Num(B,"triangles_lod0_max"),"F0")+"), LOD1 "+t1+" (<= "+F(Num(B,"triangles_lod1_max"),"F0")+")"));
   plan.Warns+=plan.Lines.Count(l=>!l.ok);
  }

  // ---------- scene: same / create ----------

  static bool Same2(Cfg cfg,Transform holder,Plan2 plan)
  {
   float tol=cfg.R("pose_tol_m");if((holder.position-plan.Holder).magnitude>tol)return false;int kids=0;
   foreach(var p in plan.Parts)for(int lod=0;lod<(p.L0==p.L1?1:2);lod++)
   {
    kids++;var t=holder.Find(ChildName(p,lod));var mf=t!=null?t.GetComponent<MeshFilter>():null;
    if(mf==null||mf.sharedMesh!=(lod==0?p.L0:p.L1)||t.localPosition.magnitude>tol||Quaternion.Angle(t.localRotation,Quaternion.identity)>cfg.R("pose_tol_deg")||(t.localScale-Vector3.one).magnitude>1e-3f)return false;
   }
   var l=plan.Light!=null?holder.Find(Str(plan.Light,"name")):null;if((plan.Light!=null)!=(l!=null))return false;
   if(l!=null)
   {
    kids++;if((l.position-plan.LightPos).magnitude>tol)return false;
    var lc=l.GetComponent<Light>();var c=Vec(plan.Light,"color");float vt=cfg.R("light_value_tol");   // intensity / range / colour are the data's too: a changed light is not "no change"
    if(lc==null||lc.type!=LightType.Point||lc.shadows!=LightShadows.None||Mathf.Abs(lc.intensity-Num(plan.Light,"intensity"))>vt||Mathf.Abs(lc.range-Num(plan.Light,"range_m"))>vt
       ||Mathf.Abs(lc.color.r-c.x)>vt||Mathf.Abs(lc.color.g-c.y)>vt||Mathf.Abs(lc.color.b-c.z)>vt)return false;
   }
   return holder.childCount==kids&&holder.GetComponentsInChildren<Light>(true).Length==(plan.Light!=null?1:0)&&holder.GetComponent<LODGroup>()!=null;
  }
  static float LodSwitchM(JToken o,float size,float fov)=>size*QualitySettings.lodBias/(2f*Mathf.Tan(fov*.5f*Mathf.Deg2Rad)*Mathf.Max(Num(Req(o,"lod"),"lod1_relative_height"),1e-4f));

  static string ApplyV2(string arg,bool dry)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,!dry);var scene=Open(t);var o=SplitRow(cfg);
   var sb=new StringBuilder("OreOutcrop308 "+(dry?"plan":"scene-apply")+" "+t.Alias+" (data "+Short(cfg.Sha)+" "+(Opt(cfg.J,"version")??"?")+", group "+(Opt(cfg.J,"group")??"")+", second try: "+Str(o,"id")+")\n");
   var root=Root(cfg,scene);if(root!=null&&PostLedger308.UnderProtectedTree(root))throw new Refuse("root "+cfg.Root+" is a protected tree name");
   foreach(var off in Arr(cfg.J,"outcrops").Where(q=>!Flag(q,"enabled")))if(root!=null&&root.Find(Str(off,"id"))!=null)throw new Refuse(Str(off,"id")+" is off in the data but in the scene - scene-revert:"+t.Alias+" first");
   var have=root!=null?root.Find(Str(o,"id")):null;var plan=Build2(cfg,scene,o,null);
   foreach(var n in plan.Notes)sb.AppendLine("  "+n);foreach(var l in plan.Lines)sb.AppendLine("  ["+(l.ok?"ok":"WARN")+"] "+l.id+" "+l.text);
   if(plan.Light!=null)sb.AppendLine("  light "+Str(plan.Light,"name")+" at "+V(plan.LightPos)+": Point intensity "+F(Num(plan.Light,"intensity"),"F2")+", range "+F(Num(plan.Light,"range_m"),"F1")+", no shadows (inside the fissure mouth)");
   int rends=plan.Parts.Sum(p=>p.L0==p.L1?1:2);
   if(have!=null){if(Same2(cfg,have,plan))return sb.Append("  "+plan.Id+": in the scene, same pose\n  변경 없음 (no change; WARN "+plan.Warns+")").ToString();throw new Refuse(plan.Id+" is in "+scene.path+" with another pose / parts than the data gives - scene-revert:"+t.Alias+" first. Nothing written");}
   sb.AppendLine("  create "+cfg.Root+"/"+plan.Id+" at "+V(plan.Holder)+": "+rends+" renderer(s) in a LODGroup of 2 levels ("+plan.Parts.Count(p=>p.Emissive)+" emissive per level), "+(plan.Light!=null?1:0)+" Point light, 0 colliders");
   if(dry)return sb.Append("  plan only: nothing written (would create 1 outcrop(s); WARN "+plan.Warns+")").ToString();
   if(plan.Warns>0)throw new Refuse(plan.Id+": "+plan.Warns+" acceptance line(s) of the form are not met on this scene's ground (see the plan). Nothing written");
   string abs=Harness303.Abs(t.Scene);var write=new Write{utc=DateTime.UtcNow.ToString("O"),target=t.Scene,backup=Backup(t.Scene),shaBefore=Harness303.Sha(abs),dataSha=cfg.Sha};
   var ledger=ReadLedger(t)??new Ledger{group=Opt(cfg.J,"group")??"",target=t.Scene,created=DateTime.UtcNow.ToString("O")};
   if(root==null){var go=new GameObject(cfg.Root);SceneManager.MoveGameObjectToScene(go,scene);root=go.transform;write.rows.Add(new Row{kind="create-root",key=cfg.Root,detail="root"});}
   var holder=new GameObject(plan.Id).transform;holder.SetParent(root,false);holder.position=plan.Holder;var lod0=new List<Renderer>();var lod1=new List<Renderer>();
   foreach(var p in plan.Parts)for(int lod=0;lod<(p.L0==p.L1?1:2);lod++)
   {
    var go=new GameObject(ChildName(p,lod));go.transform.SetParent(holder,false);go.AddComponent<MeshFilter>().sharedMesh=lod==0?p.L0:p.L1;var mr=go.AddComponent<MeshRenderer>();mr.sharedMaterials=lod==0?p.Mats:p.Mats.Take(p.L1.subMeshCount).ToArray();
    mr.shadowCastingMode=p.Shadows?ShadowCastingMode.On:ShadowCastingMode.Off;if(p.Emissive)mr.receiveShadows=false;
    if(p.L0==p.L1){lod0.Add(mr);lod1.Add(mr);}else(lod==0?lod0:lod1).Add(mr);
   }
   var group=holder.gameObject.AddComponent<LODGroup>();group.fadeMode=LODFadeMode.None;
   group.SetLODs(new[]{new LOD(Num(Req(o,"lod"),"lod1_relative_height"),lod0.ToArray()),new LOD(0f,lod1.ToArray())});group.RecalculateBounds();
   if(plan.Light!=null)
   {
    var lg=new GameObject(Str(plan.Light,"name"));lg.transform.SetParent(holder,false);lg.transform.position=plan.LightPos;var c=Vec(plan.Light,"color");
    var l=lg.AddComponent<Light>();l.type=LightType.Point;l.color=new Color(c.x,c.y,c.z);l.intensity=Num(plan.Light,"intensity");l.range=Num(plan.Light,"range_m");l.shadows=LightShadows.None;
   }
   write.rows.Add(new Row{kind="create",key=BuildingAudit308.KeyOf(holder),detail=plan.Id+" at "+V(plan.Holder)});
   ledger.writes.Add(write);WriteLedger(t,ledger);   // the ledger is on disk before the scene is saved
   EditorSceneManager.MarkSceneDirty(scene);
   if(!EditorSceneManager.SaveScene(scene))
   {
    ledger.writes.Remove(write);WriteLedger(t,ledger);EditorSceneManager.OpenScene(t.Scene,OpenSceneMode.Single);
    return "FAILED SaveScene returned false for "+t.Scene+" (scene reloaded from disk, nothing saved, the ledger row was taken out again; backup "+write.backup+")";
   }
   write.shaAfter=Harness303.Sha(abs);WriteLedger(t,ledger);
   return sb.Append("  saved: 1 outcrop(s); WARN 0; LOD1 from about "+F(LodSwitchM(o,group.size,Num(Req(cfg.J,"stills"),"fov")),"F0")+" m at quality level "+QualitySettings.GetQualityLevel()+" (lodBias "+F(QualitySettings.lodBias)+"); scene sha "+Short(write.shaBefore)+" -> "+Short(write.shaAfter)+"; backup "+write.backup+"; ledger "+LedgerFile(t)).ToString();
  }

  // ---------- check (read-only) ----------

  static string CheckV2(string arg)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,false);var scene=Open(t);var o=SplitRow(cfg);int fail=0;
   var sb=new StringBuilder("OreOutcrop308 check "+t.Alias+" (data "+Short(cfg.Sha)+", second try: "+Str(o,"id")+")\n");
   void C(bool ok,string what){if(!ok)fail++;sb.AppendLine("  ["+(ok?"PASS":"FAIL")+"] "+what);}
   var root=Root(cfg,scene);string id=Str(o,"id");
   C(root!=null&&root.childCount==1&&root.Find(id)!=null,"AC-O1 "+cfg.Root+" holds exactly the enabled outcrop ("+id+")");
   if(root==null||root.Find(id)==null)return sb.Append("  FAIL "+fail).ToString();
   var h=root.Find(id);var plan=Build2(cfg,scene,o,h);string dir=Str(o,"asset_dir").TrimEnd('/')+"/";
   var rends=root.GetComponentsInChildren<Renderer>(true);
   C(!PostLedger308.UnderProtectedTree(root)&&rends.All(r=>{var mf=r.GetComponent<MeshFilter>();return mf!=null&&mf.sharedMesh!=null&&EditorUtility.IsPersistent(mf.sharedMesh)&&AssetDatabase.GetAssetPath(mf.sharedMesh).StartsWith(dir,StringComparison.Ordinal);}),
    "AC-O2 every mesh under the root is a baked asset of "+dir+" ("+rends.Length+" renderer(s)); nothing under a protected tree");
   C(Same2(cfg,h,plan),"AC-O3 "+id+" stands on the physical ground at the data XZ ("+V(h.position)+"), every part at the identity pose, the light where the data puts it with the data's intensity / range / colour");
   var lights=root.GetComponentsInChildren<Light>(true);var lm=Req(cfg.Rules,"light_max");
   C(lights.Length<=Mathf.RoundToInt(cfg.R("lights_added_per_scene"))&&lights.All(l=>l.type==LightType.Point&&l.shadows==LightShadows.None&&l.intensity<=Num(lm,"intensity")+1e-4f&&l.range<=Num(lm,"range_m")+1e-4f),
    "AC-O4 lights under the root: "+lights.Length+" (<= "+F(cfg.R("lights_added_per_scene"),"F0")+"), Point, no shadows, intensity "+(lights.Length>0?F(lights.Max(l=>l.intensity),"F2"):"-")+" <= "+F(Num(lm,"intensity"),"F1")+", range "+(lights.Length>0?F(lights.Max(l=>l.range),"F1"):"-")+" <= "+F(Num(lm,"range_m"),"F1"));
   string vein=Str(Req(Req(o,"materials"),"vein"),"guid");var glow=rends.Where(r=>r.sharedMaterials.Any(Glows)).ToList();var group=h.GetComponent<LODGroup>();var lods=group!=null?group.GetLODs():new LOD[0];
   int perLod=lods.Length==0?glow.Count:lods.Max(l=>l.renderers.Count(r=>r!=null&&glow.Contains(r)));
   C(glow.All(r=>r.name.Contains("Ore_Vein")&&r.sharedMaterials.All(m=>m!=null&&AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m))==vein))&&perLod<=Mathf.RoundToInt(Num(Req(cfg.Rules,"emission"),"emissive_renderers_max_per_lod")),
    "AC-O5 sustained emission under the root = the vein meshes only (existing Ore_Vein material, names hold \"Ore_Vein\"); "+perLod+" emissive renderer(s) per LOD level (<= "+F(Num(Req(cfg.Rules,"emission"),"emissive_renderers_max_per_lod"),"F0")+"); glow cards 0");
   C(root.GetComponentsInChildren<Collider>(true).Length==0,"AC-O6 colliders under the root: 0 (no NavMesh bake)");
   C(lods.Length==2&&Mathf.Abs(lods[0].screenRelativeTransitionHeight-Num(Req(o,"lod"),"lod1_relative_height"))<1e-4f&&lods.All(l=>l.renderers.All(r=>r!=null)),
    "AC-O18b LODGroup: "+lods.Length+" levels (2), LOD1 under screen height "+(lods.Length>0?F(lods[0].screenRelativeTransitionHeight):"-")+", never culled; LOD1 from about "+(group!=null?F(LodSwitchM(o,group.size,Num(Req(cfg.J,"stills"),"fov")),"F0"):"-")+" m at lodBias "+F(QualitySettings.lodBias));
   foreach(var n in plan.Notes)sb.AppendLine("    "+n);
   foreach(var l in plan.Lines)C(l.ok,l.id+" "+l.text);
   return sb.Append("  "+(fail==0?"PASS":"FAIL "+fail)).ToString();
  }

  // ---------- stills ----------

  static string ShotV2(string arg)
  {
   var cfg=Load();string label="";int li=arg.IndexOf(":label=",StringComparison.Ordinal);if(li>=0){label=arg.Substring(li+7).Trim();arg=arg.Substring(0,li);}
   var parts=arg.Split(':');if(parts.Length!=3)throw new Refuse("shot:<scene>:<eye>:<PC|Mobile>[:label=<text>]");
   var t=TargetOf(cfg,parts[0]);PreviewGuard(cfg,t,false);Open(t);var st=Req(cfg.J,"stills");var o=SplitRow(cfg);
   if(!Arr(st,"tiers").Any(x=>x.Value<string>()==parts[2]))throw new Refuse("tier '"+parts[2]+"' is not in stills.tiers");
   if(!Ground(cfg,Num(o,"x"),Num(o,"z"),out var holder,out _))throw new Refuse("no terrain under "+Str(o,"id"));
   float eh=Num(st,"eye_height_m");var tg=Req(o,"targets");Vector3 eye,target;
   var close=Arr(st,"closes").FirstOrDefault(x=>Str(x,"name")==parts[1]);var row=Arr(st,"eyes").FirstOrDefault(x=>Str(x,"name")==parts[1]);
   if(close!=null)
   {
    target=holder+Vec(tg,Str(close,"target"));float yaw=Num(close,"from_yaw_deg")*Mathf.Deg2Rad;var e=target+new Vector3(Mathf.Sin(yaw),0,Mathf.Cos(yaw))*Num(close,"distance_m");
    if(!Ground(cfg,e.x,e.z,out var g,out _))throw new Refuse("no ground under the eye of "+parts[1]);eye=g+Vector3.up*eh;
   }
   else if(row!=null)
   {
    if(!Ground(cfg,Num(row,"x"),Num(row,"z"),out var g,out _))throw new Refuse("no ground under eye "+parts[1]);eye=g+Vector3.up*eh;target=holder+Vec(tg,Str(row,"target"));
   }
   else throw new Refuse("eye '"+parts[1]+"' is not in stills.eyes / stills.closes ("+string.Join(", ",Arr(st,"eyes").Concat(Arr(st,"closes")).Select(x=>Str(x,"name")))+")");
   string name=Str(st,"prefix")+"_"+(label.Length>0?label+"_":"")+t.Alias+"_"+parts[1]+"_"+parts[2];
   string file=Presentation297.ShotTier308(name,eye,target,Num(st,"fov"),Mathf.RoundToInt(Num(st,"w")),Mathf.RoundToInt(Num(st,"h")),parts[2],true);
   return "OreOutcrop308 shot "+name+": eye "+V(eye)+" -> "+V(target)+" ("+F((target-eye).magnitude,"F1")+" m), "+parts[2]+", quality level now "+QualitySettings.GetQualityLevel()+" -> "+file;
  }

  // ---------- status ----------

  static void StatusV2(Cfg cfg,StringBuilder sb)
  {
   bool v2;try{v2=V2(cfg);}catch(Refuse r){sb.AppendLine("  "+r.Message);return;}
   if(!v2){sb.AppendLine("  second try: no split_vein row is enabled (the first-try row is the active one)");return;}
   var o=SplitRow(cfg);int ok=0,stale=0,none=0;
   foreach(var r in Packs(o)){var m=AssetDatabase.LoadAssetAtPath<Mesh>(AssetOf(o,r.File));if(m==null)none++;else if(Mismatch(cfg,m,r)==null)ok++;else stale++;}
   sb.AppendLine("  second try "+Str(o,"id")+": mesh assets baked "+ok+", stale "+stale+", missing "+none+" (of "+Packs(o).Count+") in "+Str(o,"asset_dir")+"; asset ledger rows live "+ReadAssetLedger().rows.Count(r=>r!=null&&!r.reverted));
  }
 }
}
