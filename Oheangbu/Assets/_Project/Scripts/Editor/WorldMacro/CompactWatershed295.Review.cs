using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static class CompactWatershedQa295
 {public static string Run(string command)=>CompactRebuildAuthoring.Review295(command);}

 public static partial class CompactRebuildAuthoring
 {
  [Serializable] sealed class View295 {public string Id,Label,Scale;public Vector3 Eye,Target;}
  [Serializable] sealed class Views295 {public View295[] Views;}
  [Serializable] sealed class CaptureReceipt295 {public string Stage,Scene,CapturedUtc,HeightSHA256,WaterSHA256;public View295 View;}
  public static string Review295(string command)
  {
   if(command=="status")return "play="+EditorApplication.isPlaying+" scene="+SceneManager.GetActiveScene().path+" probe="+(probe295!=null?probe295.Result??"running":"none");
   if(command.StartsWith("capture:"))return Capture295(command.Substring(8));
   if(SceneManager.GetActiveScene().path!=Scene295||EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Clean Watershed295 Edit scene required");
   RequireClean292();Directory.CreateDirectory(O295);
   if(command=="check")return Check295();
   if(command=="reframe:entry")return ReframeEntry295();
   if(command=="reframe:kaesong")return ReframeKaesongViews295();
   if(command=="walk")return Walk295();
   if(command=="nav")return Navigation295();
   if(command=="nav:0.2")return Navigation295(.2f);
   if(command=="nav-diagnose")return NavigationDiagnostics295();
   if(command=="bridge-aprons")
   {
    var report=new List<string>();Bridges295(new CompactWorldSurface(Session292().MountainLayout),report);Physics.SyncTransforms();Save292();
    File.WriteAllLines(O295+"/bridge-aprons.txt",report);return string.Join("\n",report);
   }
   if(command.StartsWith("perf:"))return Perf295(command.Substring(5));
   throw new ArgumentException(command);
  }
  static T[] Components295<T>() where T:Component=>SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true)).ToArray();
  static bool Finite295(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
  static CompactHydrologySO GeneratedHydro295()
  {var h=ScriptableObject.CreateInstance<CompactHydrologySO>();JsonUtility.FromJsonOverwrite(File.ReadAllText(G295+"/hydro.json"),h);return h;}
  static string Check295()
  {
   var scene=SceneManager.GetActiveScene();bool dirty=scene.isDirty;var session=Session292();var layout=session.MountainLayout;var h=layout.Hydrology;
   var lines=new List<string>{"Watershed295 independent candidate checks "+DateTime.Now.ToString("s")};
   void C(bool pass,string label)=>lines.Add((pass?"PASS ":"FAIL ")+label);
   C(session.Content.SaveSlot=="world-compact-watershed-295","isolated save slot");
   C(AssetDatabase.GetAssetPath(layout).StartsWith(A295+"/"),"private layout");
   C(layout.FinalSurface!=null&&AssetDatabase.GetAssetPath(layout.FinalSurface).StartsWith(A295+"/"),"private final terrain");
   C(h!=null&&h.Reaches.Length>0&&h.WaterLevels!=null,"independent hydrology connected");
   C(Components295<Transform>().All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),"no missing scripts");
   var root=Root295("Watershed295_Water");if(root==null)throw new Exception("295 water has not been built");
   var filters=root.GetComponentsInChildren<MeshFilter>();var rendered=new List<WorldTerrainQuery.WaterTriangle>();int invalid=0,degenerate=0,downward=0;
   foreach(var filter in filters)
   {
    var mesh=filter.sharedMesh;if(mesh==null){invalid++;continue;}var v=mesh.vertices;var t=mesh.triangles;
    if(!v.All(Finite295)||t.Length%3!=0||t.Any(i=>i<0||i>=v.Length)){invalid++;continue;}
    for(int i=0;i<t.Length;i+=3)
    {var a=filter.transform.TransformPoint(v[t[i]]);var b=filter.transform.TransformPoint(v[t[i+1]]);var c=filter.transform.TransformPoint(v[t[i+2]]);float area=Vector3.Cross(b-a,c-a).y;if(Mathf.Abs(area)<.00001f)degenerate++;if(area<-.00001f)downward++;rendered.Add(new WorldTerrainQuery.WaterTriangle{A=a,B=b,C=c});}
   }
   C(filters.Length>0&&invalid==0,"finite indexed water chunks="+filters.Length+" invalid="+invalid);
   C(degenerate==0&&downward==0,"water face winding; degenerate="+degenerate+" downward="+downward);
   C(root.GetComponentsInChildren<Collider>().Length==0,"water has no solid colliders");
   RenderGeometry295(C,root,filters);
   var queries=Components295<WorldTerrainQuery>();C(queries.Length>0,"water query exists");
   foreach(var query in queries)
   {
    C(query.Water.Length==rendered.Count&&query.Water.Zip(rendered,(a,b)=>a.A==b.A&&a.B==b.B&&a.C==b.C).All(x=>x),"query/render triangles exactly equal: "+query.Water.Length);
    int misses=0;for(int i=0;i<rendered.Count;i+=Mathf.Max(1,rendered.Count/4000)){var triangle=rendered[i];var p=(triangle.A+triangle.B+triangle.C)/3;if(!query.TryWaterHeight(p,out float y)||Mathf.Abs(y-p.y)>.015f)misses++;}
    C(misses==0,"water centroid query mismatch="+misses);
   }
   if(h!=null)
   {
    foreach(var reach in h.Reaches)
    {
     int uphill=0,badWidth=0,badBed=0;for(int i=0;i<reach.Rows.Length;i++){var r=reach.Rows[i];if(i>0&&r.Position.y>reach.Rows[i-1].Position.y+.001f)uphill++;if(r.LeftWidth<=0||r.RightWidth<=0)badWidth++;if(r.BedY>=r.Position.y)badBed++;}
     C(uphill==0&&badWidth==0&&badBed==0,"reach "+reach.Id+" uphill="+uphill+" invalid widths="+badWidth+" dry center="+badBed);
    }
    if(h.ProtectedMask!=null)
    {
     var mask=h.ProtectedMask.bytes;var before=File.ReadAllBytes(O295+"/Baseline/height.bytes");var after=layout.FinalSurface.bytes;int changed=0,protectedCount=0;
     if(mask.Length*4!=after.Length||before.Length!=after.Length)changed=-1;
     else for(int i=0;i<mask.Length;i++)if(mask[i]!=0){protectedCount++;for(int k=0;k<4;k++)if(before[i*4+k]!=after[i*4+k]){changed++;break;}}
     C(changed==0&&protectedCount>0,"protected terrain byte comparison samples="+protectedCount+" changed="+changed);
    }
    else C(false,"protected terrain mask missing");
   }
   var arts=Components295<CompactRebuildArtRenderer>().Where(a=>a.isActiveAndEnabled&&a.Sheet!=null).ToArray();
   var materials=Components295<Renderer>().Where(r=>r.enabled&&r.gameObject.activeInHierarchy).SelectMany(r=>r.sharedMaterials)
    .Concat(arts.SelectMany(a=>a.Sheet.Prototypes).SelectMany(p=>p.Lods).SelectMany(l=>l.Parts).Select(p=>p.Material)).Distinct().ToArray();
   C(materials.All(m=>m!=null&&m.shader!=null&&m.shader.isSupported&&!ShaderUtil.ShaderHasError(m.shader)),"active shaders and instanced materials compile");
   foreach(var art in arts)C(art.Sheet.FixedPlacements.Select(p=>p.Id).Distinct().Count()==art.Sheet.FixedPlacements.Length,"unique placements "+AssetDatabase.GetAssetPath(art.Sheet));
   var probe=new GameObject("Watershed295_culling_oracle"){hideFlags=HideFlags.HideAndDontSave};var camera=probe.AddComponent<Camera>();camera.enabled=false;camera.aspect=16f/9;camera.farClipPlane=4000;
   int poses=0,mismatch=0,accounting=0;long matrices=0;var target=new RenderTexture(640,360,24);var observers=arts.Select(a=>a.Observer).ToArray();
   try
   {
    camera.targetTexture=target;foreach(var a in arts)a.Observer=camera;
    var views=CameraViews295().Views;
    void ComparePose(Vector3 eye,Vector3 look)
    {
     camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(look-eye));camera.fieldOfView=60;camera.Render();
     foreach(var art in arts){var scan=art.CompareWithFullScan(camera);mismatch+=scan.Mismatches;matrices+=scan.Matrices;if(scan.CulledVisible!=scan.FullVisible||art.VisibleInstances!=scan.FullVisible||art.DrawCalls!=scan.ExpectedDrawCalls||art.SubmittedTriangles!=scan.ExpectedTriangles)accounting++;}poses++;
    }
    foreach(var view in views)foreach(float offset in new[]{-.01f,0,.01f,64f,80f,250f})ComparePose(view.Eye+Vector3.right*offset,view.Target+Vector3.right*offset);
    // Exact cell borders and the 80m always-retained ring around real bank
    // placements exercise thresholds in addition to the authored review views.
    foreach(var art in arts)
    {
     var selected=art.Sheet.FixedPlacements.OrderBy(p=>(new Vector2(p.Position.x,p.Position.z)-new Vector2(views[1].Eye.x,views[1].Eye.z)).sqrMagnitude).Take(2);
     foreach(var placement in selected)
     {
      var p=placement.Position;float borderX=Mathf.Round(p.x/64)*64,borderZ=Mathf.Round(p.z/64)*64;
      foreach(float epsilon in new[]{-.01f,0,.01f})
      {
       var x=new Vector3(borderX+epsilon,p.y+1.65f,p.z);ComparePose(x,x+Vector3.forward*50);
       var z=new Vector3(p.x,p.y+1.65f,borderZ+epsilon);ComparePose(z,z+Vector3.right*50);
       var ring=p+new Vector3(80+epsilon,1.65f,0);ComparePose(ring,ring+Vector3.right*50);
      }
     }
    }
   }
   finally{for(int i=0;i<arts.Length;i++)arts[i].Observer=observers[i];camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(probe);}
   C(mismatch==0&&accounting==0,"all active sheets culling oracle sheets="+arts.Length+" poses="+poses+" matrices="+matrices+" mismatches="+mismatch+" render counters="+accounting);
   var routeLines=RouteSupport295();lines.AddRange(routeLines);
   C(scene.isDirty==dirty,"check preserves scene dirty state "+dirty+"->"+scene.isDirty);
   lines.Add("These checks do not establish manual play, art acceptance, boss progression or 120fps.");
   File.WriteAllLines(O295+"/checks.txt",lines);return string.Join("\n",lines);
  }
  static void RenderGeometry295(Action<bool,string> check,GameObject waterRoot,MeshFilter[] waterFilters)
  {
   int badLocal=0,badWorld=0,badRenderer=0;var wetTiles=new HashSet<Vector2Int>();
   foreach(var filter in waterFilters)
   {
    var mesh=filter.sharedMesh;var renderer=filter.GetComponent<Renderer>();if(mesh==null||renderer==null){badRenderer++;continue;}
    if(!renderer.enabled||renderer.forceRenderingOff||!renderer.gameObject.activeInHierarchy)badRenderer++;
    var local=mesh.bounds;local.Expand(.02f);var world=renderer.bounds;world.Expand(.05f);
    foreach(var p in mesh.vertices)
    {var v=filter.transform.TransformPoint(p);if(!local.Contains(p))badLocal++;if(!world.Contains(v))badWorld++;}
    var vertices=mesh.vertices;var triangles=mesh.triangles;
    for(int i=0;i<triangles.Length;i+=3){var v=filter.transform.TransformPoint((vertices[triangles[i]]+vertices[triangles[i+1]]+vertices[triangles[i+2]])/3);wetTiles.Add(new Vector2Int(Mathf.Clamp(Mathf.FloorToInt(v.x/500),0,7),Mathf.Clamp(Mathf.FloorToInt(v.z/500),0,11)));}
   }
   check(badLocal+badWorld+badRenderer==0,"water renderer bounds cover all vertices; local="+badLocal+" world="+badWorld+" hidden/invalid="+badRenderer);
   var terrain=Root295("Reworld292_Terrain");int shores=0,badLod=0,badCollider=0;var details=new List<string>();
   foreach(Transform tile in terrain.transform)
   {
    var key=new Vector2Int(Mathf.RoundToInt(tile.position.x/500),Mathf.RoundToInt(tile.position.z/500));if(!wetTiles.Contains(key))continue;shores++;
    var full=tile.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f=>f.name=="LOD0");var group=tile.GetComponent<LODGroup>();var collider=tile.GetComponent<MeshCollider>();
    if(full==null||collider==null||!collider.enabled||collider.isTrigger||collider.sharedMesh!=full.sharedMesh){badCollider++;if(details.Count<4)details.Add(tile.name+" collider");}
    var levels=group!=null?group.GetLODs():Array.Empty<LOD>();
    if(full==null||levels.Length==0||levels.Any(l=>l.renderers.Length==0||l.renderers.Any(r=>r==null||!r.gameObject.activeInHierarchy||r.GetComponent<MeshFilter>()?.sharedMesh!=full.sharedMesh)))
    {badLod++;if(details.Count<4)details.Add(tile.name+" LOD");}
   }
   check(shores>0&&badLod+badCollider==0,"water-intersecting terrain tiles retain full geometry and matching collision; tiles="+shores+" LOD="+badLod+" collider="+badCollider+" "+string.Join(",",details));
   var hiddenWater=Components295<Collider>().Where(c=>c.enabled&&c.gameObject.activeInHierarchy&&!c.isTrigger&&
    (c.transform.root==waterRoot.transform||c.transform.root.name=="Reworld292_Water"||c.transform.root.name=="River294"||c.name=="Water_NotWalkable_BakeOnly"||c.name.StartsWith("RailBoundary_BakeOnly_",StringComparison.Ordinal)||c.name.StartsWith("FenceBoundary_BakeOnly_",StringComparison.Ordinal))).ToArray();
   check(hiddenWater.Length==0,"active water roots and temporary water/rail/fence bake proxies have no retained hidden collision="+hiddenWater.Length);
   var crossings=JsonUtility.FromJson<Crossings295>(File.ReadAllText(G295+"/crossings.json")).Crossings.Where(c=>c.Kind!="Mum"&&c.Kind!="AbilityGate").ToArray();
   var bridgeRoot=Root295("Watershed295_Crossings");int badBridge=0,aprons=0,apronSamples=0,missingApron=0;
   foreach(var crossing in crossings)
   {
    var bridge=bridgeRoot!=null?bridgeRoot.transform.Find(crossing.Id):null;var filter=bridge!=null?bridge.GetComponent<MeshFilter>():null;var collider=bridge!=null?bridge.GetComponent<MeshCollider>():null;
    var renderer=bridge!=null?bridge.GetComponent<MeshRenderer>():null;
    if(filter==null||filter.sharedMesh==null||collider==null||collider.sharedMesh!=filter.sharedMesh||!collider.enabled||collider.isTrigger||renderer==null||!renderer.enabled||renderer.forceRenderingOff){badBridge++;continue;}
    var bounds=renderer.bounds;bounds.Expand(.03f);if(filter.sharedMesh.vertices.Any(p=>!bounds.Contains(bridge.TransformPoint(p))))badBridge++;
    var points=crossing.Points!=null&&crossing.Points.Length>1?crossing.Points:new[]{crossing.Start,crossing.End};float half=Mathf.Clamp(crossing.RouteWidth,3.2f,8)*.4f;
    foreach(bool start in new[]{true,false})
    {
     aprons++;var end=points[start?0:points.Length-1];var inward=points[start?1:points.Length-2]-end;inward.y=0;inward.Normalize();var side=Vector3.Cross(Vector3.up,inward);
     foreach(float distance in new[]{.5f,3f,5f})foreach(float lateral in new[]{-half,0,half})
     {
      apronSamples++;var p=end-inward*distance+side*lateral;
      if(!collider.Raycast(new Ray(p+Vector3.up*30,Vector3.down),out var hit,60)||hit.normal.y<=.5f)missingApron++;
     }
    }
   }
   check(crossings.Length==5&&badBridge==0,"all permanent bridge render bounds and shared render/collision meshes; count="+crossings.Length+" invalid="+badBridge);
   check(aprons==crossings.Length*2&&missingApron==0,"visible bank aprons have collision across entry width; aprons="+aprons+" physical samples="+apronSamples+" missing="+missingApron);
   var capital=Root295("Watershed295_SunkenCapital");int groups=0,badGroups=0,parts=0,badParts=0;
   if(capital!=null)foreach(var group in capital.GetComponentsInChildren<LODGroup>(true))
   {
    groups++;var levels=group.GetLODs();if(!group.enabled||!group.gameObject.activeInHierarchy||levels.Length==0||group.size<=0)badGroups++;
    foreach(var level in levels)
    {
     if(level.renderers.Length==0)badGroups++;
     foreach(var r in level.renderers)
     {
      parts++;if(r==null){badParts++;continue;}var mesh=r.GetComponent<MeshFilter>()?.sharedMesh;
      if(mesh==null||mesh.vertexCount==0||mesh.subMeshCount==0||Enumerable.Range(0,mesh.subMeshCount).Sum(i=>(long)mesh.GetIndexCount(i))<3||!r.enabled||r.forceRenderingOff||!r.gameObject.activeInHierarchy||r.sharedMaterials.Length<mesh.subMeshCount||r.sharedMaterials.Take(mesh.subMeshCount).Any(m=>m==null))badParts++;
     }
    }
   }
   check(groups>0&&badGroups+badParts==0,"Kaesong LOD levels have visible nonempty meshes and material coverage; groups="+groups+" levels invalid="+badGroups+" parts="+parts+" invalid="+badParts);
  }
  static Views295 CameraViews295()
  {
   string file=O295+"/cameras.json";if(File.Exists(file))return JsonUtility.FromJson<Views295>(File.ReadAllText(file));
   var hydro=GeneratedHydro295();var field=new CompactWorldSurface(1001,1501,4,File.ReadAllBytes(G295+"/height.bytes"));var original=new CompactWorldSurface(1001,1501,4,File.ReadAllBytes(O295+"/Baseline/height.bytes"));
   Vector3 DryEye(Vector3 wanted)
   {
    Vector3 best=Vector3.zero;float cost=float.PositiveInfinity;
    for(int z=-300;z<=300;z+=4)for(int x=-300;x<=300;x+=4)
    {float px=wanted.x+x,pz=wanted.z+z;if(px<4||px>3996||pz<4||pz>5996)continue;float y=field.Sample(px,pz);if(y<hydro.Lake.Level+.25f&&pz>4400||field.Normal(px,pz).y<.82f||Mathf.Abs(y-original.Sample(px,pz))>.15f)continue;float score=x*x+z*z+Mathf.Abs(y-wanted.y)*4;if(score<cost){cost=score;best=new Vector3(px,y+1.65f,pz);}}
    if(float.IsPositiveInfinity(cost))throw new Exception("No unchanged dry eye-level support near "+wanted);return best;
   }
   try
   {
    var shore=hydro.Lake.ShorePoints;var centre=shore.Length>0?shore.Aggregate(Vector3.zero,(a,b)=>a+b)/shore.Length:new Vector3(2200,hydro.Lake.Level,5100);centre.y=hydro.Lake.Level;
    var outlet=hydro.Reaches.OrderBy(r=>r.Id.Contains("main")?0:1).First();var rows=outlet.Rows;var gorge=rows[Mathf.Min(rows.Length-1,Mathf.RoundToInt(rows.Length*.23f))].Position;
    var views=new Views295{Views=new[]{
     new View295{Id="basin",Label="현강 침수 분지",Scale="overview",Eye=centre+new Vector3(-650,650,-950),Target=centre},
     new View295{Id="entry",Label="현강 진입 물가",Scale="eye",Eye=DryEye(new Vector3(1930,hydro.Lake.Level+2,4820)),Target=centre+Vector3.up*3},
     new View295{Id="bay",Label="동쪽 만과 산줄기",Scale="eye",Eye=DryEye(new Vector3(2670,hydro.Lake.Level+2,5230)),Target=centre+Vector3.up*2},
     new View295{Id="gorge",Label="유출 협곡",Scale="eye",Eye=DryEye(gorge+new Vector3(40,5,-15)),Target=gorge+new Vector3(0,1,-30)},
     new View295{Id="gorge-oblique",Label="협곡과 양안",Scale="medium",Eye=gorge+new Vector3(95,65,-95),Target=gorge},
     new View295{Id="watershed",Label="전체 수계",Scale="overview",Eye=new Vector3(2000,5500,300),Target=new Vector3(2000,80,3050)}}};
    File.WriteAllText(file,JsonUtility.ToJson(views,true));return views;
   }
   finally{Object.DestroyImmediate(hydro);}
  }
  static string Capture295(string argument)
  {
   RequireClean292();var args=argument.Split(':');string stage=args[0];int index=args[1]=="kaesong"?6:args[1]=="kaesong-close"?7:int.Parse(args[1]);bool raw=args.Length>2&&args[2]=="raw";
   if(stage!="before"&&stage!="after")throw new ArgumentException("capture before|after:index[:raw]");
   var view=index<6?CameraViews295().Views[index]:KaesongViews295().Views[index-6];string original=SceneManager.GetActiveScene().path;string scenePath=stage=="before"?Scene292:Scene295;
   if(original!=scenePath)EditorSceneManager.OpenScene(scenePath);
   string folder=O295+"/Captures/"+stage;Directory.CreateDirectory(folder);string output=folder+"/"+index+"-"+view.Id+(raw?"-raw":"")+".png";
   var saved=new Dictionary<Renderer,Material[]>();var disabled=new List<Behaviour>();var hidden=new List<Renderer>();Material clay=null,water=null;
   try
   {
    if(raw)
    {
     clay=new Material(Shader.Find("Universal Render Pipeline/Lit"));clay.SetColor("_BaseColor",new Color(.5f,.5f,.5f));clay.SetFloat("_Smoothness",.12f);
     water=new Material(clay);water.SetColor("_BaseColor",new Color(.2f,.32f,.34f));
     foreach(var a in Components295<CompactRebuildArtRenderer>().Where(a=>a.isActiveAndEnabled)){a.enabled=false;disabled.Add(a);}
     foreach(var a in Components295<CompactGrassRenderer266>().Where(a=>a.isActiveAndEnabled)){a.enabled=false;disabled.Add(a);}
     foreach(var r in Components295<Renderer>().Where(r=>r.enabled&&r.gameObject.activeInHierarchy))
     {
      string name=r.transform.root.name;bool liquid=name=="Watershed295_Water"||name=="Reworld292_Water"||name=="River294";
      bool structure=name=="Reworld292_Terrain"||liquid||name.Contains("Highlands293")||name=="Watershed295_Crossings"||index>=6&&name=="Watershed295_SunkenCapital";
      if(!structure){r.enabled=false;hidden.Add(r);continue;}saved[r]=r.sharedMaterials;r.sharedMaterials=r.sharedMaterials.Select(_=>liquid?water:clay).ToArray();
     }
    }
    eye293=view.Eye;target293=view.Target;output293=output;raw293=raw;Capture292(6);
    string Hash(byte[] bytes){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    var layout=Session292().MountainLayout;
    File.WriteAllText(output+".json",JsonUtility.ToJson(new CaptureReceipt295{Stage=stage,Scene=scenePath,CapturedUtc=DateTime.UtcNow.ToString("o"),View=view,
     HeightSHA256=layout.FinalSurface!=null?Hash(layout.FinalSurface.bytes):"none",WaterSHA256=layout.Hydrology!=null&&layout.Hydrology.WaterLevels!=null?Hash(layout.Hydrology.WaterLevels.bytes):"none"},true));return output;
   }
   finally
   {
    eye293=null;target293=null;output293=null;raw293=false;
    foreach(var pair in saved)if(pair.Key!=null)pair.Key.sharedMaterials=pair.Value;foreach(var r in hidden)if(r!=null)r.enabled=true;foreach(var b in disabled)if(b!=null)b.enabled=true;
    if(clay!=null)Object.DestroyImmediate(clay);if(water!=null)Object.DestroyImmediate(water);
    if(original!=scenePath)EditorSceneManager.OpenScene(original);
   }
  }
  static Views295 KaesongViews295()
  {
   string file=O295+"/cameras-kaesong.json";if(File.Exists(file))return JsonUtility.FromJson<Views295>(File.ReadAllText(file));
   var source=JsonUtility.FromJson<KaesongReceipt295>(File.ReadAllText(O295+"/KaesongSources.json"));
   var field=new CompactWorldSurface(1001,1501,4,File.ReadAllBytes(G295+"/height.bytes"));var before=new CompactWorldSurface(1001,1501,4,File.ReadAllBytes(O295+"/Baseline/height.bytes"));
   var close=Vector3.Lerp(source.LookAt,source.LookFrom,.5f)+Vector3.up*5;close.y=Mathf.Max(close.y,Mathf.Max(field.Sample(close.x,close.z),before.Sample(close.x,close.z))+5);
   var views=new Views295{Views=new[]{
    new View295{Id="kaesong",Label="옛 도성 석단과 침수 마당",Scale="medium",Eye=source.LookFrom,Target=source.LookAt},
    new View295{Id="kaesong-close",Label="옛 도성 기단과 계단",Scale="close",Eye=close,Target=source.LookAt+Vector3.up*3}}};
   File.WriteAllText(file,JsonUtility.ToJson(views,true));return views;
  }
  static string ReframeKaesongViews295()
  {
   string status=ReframeKaesong295();string rejected=O295+"/Captures/rejected/kaesong-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff");Directory.CreateDirectory(rejected);
   string cameras=O295+"/cameras-kaesong.json";if(File.Exists(cameras))File.Move(cameras,rejected+"/cameras-kaesong.json");
   foreach(string stage in new[]{"before","after"})foreach(string id in new[]{"6-kaesong","7-kaesong-close"})foreach(string suffix in new[]{"","-raw"})
   {string path=O295+"/Captures/"+stage+"/"+id+suffix+".png",archived=rejected+"/"+stage+"-"+id+suffix+".png";if(File.Exists(path))File.Move(path,archived);if(File.Exists(path+".json"))File.Move(path+".json",archived+".json");}
   KaesongViews295();return status+"; both QA camera poses regenerated, previous captures="+rejected;
  }
  static string ReframeEntry295()
  {
   var views=CameraViews295();var field=new CompactWorldSurface(Session292().MountainLayout);var old=new CompactWorldSurface(1001,1501,4,File.ReadAllBytes(O295+"/Baseline/height.bytes"));
   var query=Components295<WorldTerrainQuery>().First();var h=Session292().MountainLayout.Hydrology;var target=views.Views[0].Target;
   var waterTargets=new List<Vector3>();var candidates=new List<Vector3>();var wanted=new Vector2(1930,4820);
   for(float z=4440;z<=5560;z+=40)for(float x=1650;x<=2880;x+=40)
   {var p=new Vector3(x,h.Lake.Level+2,z);if(field.Sample(x,z)<h.Lake.Level-2&&query.TryWaterHeight(p,out float water)&&Mathf.Abs(water-h.Lake.Level)<.01f)waterTargets.Add(p);}
   for(float z=wanted.y-700;z<=wanted.y+700;z+=8)for(float x=wanted.x-700;x<=wanted.x+700;x+=8)
   {
    if(x<8||x>3992||z<8||z>5992)continue;float y=field.Sample(x,z);if(y<h.Lake.Level+.3f||y>h.Lake.Level+28||Mathf.Abs(y-old.Sample(x,z))>.15f||field.Normal(x,z).y<.85f)continue;
    var eye=new Vector3(x,y+1.65f,z);if(query.Immersion(eye-Vector3.up*1.65f)<=0)candidates.Add(eye);
   }
   var props=Components295<CompactRebuildArtRenderer>().Where(a=>a.isActiveAndEnabled&&a.Sheet!=null).SelectMany(a=>a.Sheet.FixedPlacements.Select(p=>a.transform.TransformPoint(p.Position))).ToArray();
   Vector3 selected=Vector3.zero;bool found=false;
   foreach(var eye in candidates.OrderBy(p=>(new Vector2(p.x,p.z)-wanted).sqrMagnitude))
   {
    if(props.Any(p=>new Vector2(p.x-eye.x,p.z-eye.z).sqrMagnitude<64&&p.y<eye.y+12&&p.y>eye.y-10))continue;
    foreach(var aim in waterTargets.OrderBy(p=>Mathf.Abs(Vector2.Distance(new Vector2(p.x,p.z),new Vector2(eye.x,eye.z))-220)).Take(50))
    {
     var direction=aim-eye;direction.y=0;var right=Vector3.Cross(Vector3.up,direction.normalized)*20;var targets=new[]{aim,aim+right,aim-right};bool clear=true;
     foreach(var end in targets)
     {
      if(field.Sample(end.x,end.z)>h.Lake.Level-1||!query.TryWaterHeight(end,out float y)||Mathf.Abs(y-h.Lake.Level)>.01f){clear=false;break;}
      int n=Mathf.CeilToInt(Vector3.Distance(eye,end)/4);for(int i=1;i<n;i++){var p=Vector3.Lerp(eye,end,i/(float)n);if(field.Sample(p.x,p.z)>p.y-.3f){clear=false;break;}}
      if(!clear||Physics.Linecast(eye,end,~0,QueryTriggerInteraction.Ignore)){clear=false;break;}
     }
     if(!clear)continue;selected=eye;target=aim;found=true;break;
    }
    if(found)break;
   }
   if(!found)throw new Exception("No unchanged low shore viewpoint has three clear terrain/physics sightlines and an 8m prop gap");
   string rejected=O295+"/Captures/rejected/entry-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff");Directory.CreateDirectory(rejected);
   File.Copy(O295+"/cameras.json",rejected+"/cameras.json");
   foreach(string stage in new[]{"before","after"})foreach(string suffix in new[]{"","-raw"})
   {string path=O295+"/Captures/"+stage+"/1-entry"+suffix+".png";string archived=rejected+"/"+stage+"-1-entry"+suffix+".png";if(File.Exists(path))File.Move(path,archived);if(File.Exists(path+".json"))File.Move(path+".json",archived+".json");}
   views.Views[1].Eye=selected;views.Views[1].Target=target;File.WriteAllText(O295+"/cameras.json",JsonUtility.ToJson(views,true));
   return "Entry viewpoint="+selected+" target="+target+"; unchanged ground, three clear terrain/physics sightlines, 8m prop gap. Rejected pair="+rejected+". Recapture both stages.";
  }
 }
}
