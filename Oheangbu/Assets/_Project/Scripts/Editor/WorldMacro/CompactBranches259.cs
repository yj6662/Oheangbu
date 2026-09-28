using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.Prologue;
using Oheangbu.Data.World;
using Oheangbu.Data.Demo;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string BranchOutput259=Output+"/Branches259",BranchRoot259="CheongrimBranches259",HerbTrace259="herb_trace259",HerbFact259="evidence:herb_black_root259";
  const string HerbText259="바구니에 남은 풀은 잎이 멀쩡한데 뿌리만 검다. 밑바닥에 붙은 흙도 먹물처럼 굳었다.";
  [Serializable]class BranchProposals259{public string height_sha256;public ProgressionRoute251[] routes;}
  public static string BranchSurvey259()
  {
   var scene=FrontageScene249();var ground=FinalSurface(scene);Directory.CreateDirectory(BranchOutput259);
   var input=JsonUtility.FromJson<BranchProposals259>(File.ReadAllText(BranchOutput259+"/route-proposals.json"));
   using(var sha=SHA256.Create())
    if(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Output+"/Cartography/heights.f32"))).Replace("-","").ToLowerInvariant()!=input.height_sha256)throw new Exception("Height proposal source changed");
   var results=new List<ProgressionRoute251>();var lines=new List<string>();
   foreach(var route in input.routes.Where(r=>r.id.StartsWith("herb_",StringComparison.Ordinal)))
   {
    var points=new List<Vector3>();
    foreach(var p in route.points)
    {
     var foot=ground(p.x,p.y).point;
     if(!NavMesh.SamplePosition(foot,out var nav,1.5f,NavMesh.AllAreas)||Vector3.Distance(foot,nav.position)>1.2f)throw new Exception("No supported navigation for "+route.id+" at "+foot);
     if(points.Count==0){points.Add(nav.position);continue;}
     var path=new NavMeshPath();
     if(!NavMesh.CalculatePath(points.Last(),nav.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)throw new Exception("Disconnected branch "+route.id);
     foreach(var corner in path.corners.Skip(1))if(Vector3.Distance(corner,points.Last())>.05f)points.Add(corner);
    }
    float length=0;for(int i=1;i<points.Count;i++)length+=Vector3.Distance(points[i-1],points[i]);
    if(length>route.length*1.5f)throw new Exception("Navigation introduces excessive detour: "+route.id+" "+length);
    results.Add(new ProgressionRoute251{id=route.id,fromId=route.fromId,toId=route.toId,points=points.Select(p=>new Vector2(p.x,p.z)).ToArray(),length=length});
    lines.Add("PASS current baked navigation "+route.id+" length="+length+" corners="+points.Count);
   }
   if(results.Count!=2)throw new Exception("Expected two herb branches");
   var bag=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="Worker_Straw_Bag");
   if(bag.GetComponentsInChildren<Renderer>(true).Length==0)throw new Exception("Existing straw bag art missing");
   File.WriteAllText(BranchOutput259+"/route-plan.json",JsonUtility.ToJson(new ProgressionPlan251{routes=results.ToArray()},true));
   File.WriteAllText(BranchOutput259+"/survey.txt",string.Join("\n",lines)+"\nExisting bag art: "+bag.name+". Input walking and physical clearance still required.");
   return File.ReadAllText(BranchOutput259+"/survey.txt");
  }
  public static string BranchBuild259()
  {
   var scene=FrontageScene249();if(scene.isDirty)throw new Exception("Clean candidate required");
   BranchSurvey259();var s=VillageSession();var roots=scene.GetRootGameObjects();var ground=FinalSurface(scene);
   var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single();var layout=manifest.Layout;
   var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)).Single();
   var map=roots.SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single().MapData;
   var plan=JsonUtility.FromJson<ProgressionPlan251>(File.ReadAllText(BranchOutput259+"/route-plan.json"));
   string backup=BranchOutput259+"/Before/"+DateTime.UtcNow.ToString("yyyyMMddHHmmss");Directory.CreateDirectory(backup);
   var saved=new List<string>{scene.path};saved.AddRange(new Object[]{s.Content,s.Content.Campaign,layout,art.Sheet,map}.Select(AssetDatabase.GetAssetPath));
   for(int i=0;i<saved.Count;i++)foreach(var suffix in new[]{"",".meta"})if(File.Exists(saved[i]+suffix))File.Copy(saved[i]+suffix,backup+"/"+i+"_"+Path.GetFileName(saved[i])+suffix,true);
   File.WriteAllLines(backup+"/paths.txt",saved);
   string folder=Path.GetDirectoryName(scene.path).Replace('\\','/')+"/Branches259";Directory.CreateDirectory(folder);
   T Asset<T>(string name,Func<T> make)where T:Object{string p=folder+"/"+name+".asset";var a=AssetDatabase.LoadAssetAtPath<T>(p);if(a==null){a=make();AssetDatabase.CreateAsset(a,p);}return a;}
   var originalBag=roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="Worker_Straw_Bag");
   var old=roots.SingleOrDefault(g=>g.name==BranchRoot259);if(old!=null)Object.DestroyImmediate(old);roots=roots.Where(g=>g!=null).ToArray();
   var root=new GameObject(BranchRoot259);var paths=new Dictionary<string,Vector3[]>();
   var soil=AssetDatabase.LoadAssetAtPath<Material>(Path.GetDirectoryName(scene.path).Replace('\\','/')+"/Progression251/FootpathSoil.asset");if(soil==null)throw new Exception("Candidate path material missing");
   var pathMaterial=Asset("Footpath",()=>new Material(Shader.Find("Oheangbu/Compact/Footpath")));
   pathMaterial.SetTexture("_BaseMap",soil.GetTexture("_FineMap"));pathMaterial.SetTexture("_NormalMap",soil.GetTexture("_FineNormal"));pathMaterial.SetColor("_BaseColor",new Color(.48f,.44f,.36f,.45f));EditorUtility.SetDirty(pathMaterial);
   foreach(var route in plan.routes)
   {
    var line=new List<Vector3>();
    for(int i=1;i<route.points.Length;i++){int n=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(route.points[i-1],route.points[i])/1.5f));for(int j=0;j<n;j++){var p=Vector2.Lerp(route.points[i-1],route.points[i],j/(float)n);line.Add(ground(p.x,p.y).point);}}
    var last=route.points.Last();line.Add(ground(last.x,last.y).point);paths.Add(route.id,line.ToArray());
    var vertices=new List<Vector3>();var uv=new List<Vector2>();var colors=new List<Color>();var triangles=new List<int>();float distance=0;
    for(int i=0;i<line.Count;i++)
    {
     if(i>0)distance+=Vector3.Distance(line[i-1],line[i]);var d=line[Math.Min(i+1,line.Count-1)]-line[Math.Max(0,i-1)];d.y=0;var side=Vector3.Cross(d.normalized,Vector3.up);
     for(int j=0;j<3;j++){var p=line[i]+side*(j-1)*.85f;vertices.Add(ground(p.x,p.z).point+Vector3.up*.022f);uv.Add(new Vector2(j*.85f,distance));colors.Add(new Color(1,1,1,j==1?1:0));}
     if(i>0)for(int j=0;j<2;j++){int v=i*3+j;triangles.AddRange(new[]{v-3,v,v+1,v-3,v+1,v-2});}
    }
    var mesh=Asset(route.id,()=>new Mesh());mesh.Clear();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);UpwardRibbon258(mesh);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
    var go=new GameObject(route.id,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root.transform);go.GetComponent<MeshFilter>().sharedMesh=mesh;var rr=go.GetComponent<MeshRenderer>();rr.sharedMaterial=pathMaterial;rr.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
   }
   var junction=layout.Places.FirstOrDefault(p=>p.Id=="herb_junction259");
   if(junction==null){junction=new CompactWorldLayoutSO.Place{Id="herb_junction259",Realm="cheongrim",Label="약초길 갈림",Purpose="벌목장 우회",XZ=plan.routes[0].points[0],GroundRadius=2};layout.Places=layout.Places.Concat(new[]{junction}).ToArray();}
   junction.HasSourceBinding=true;junction.SceneRoots=new[]{BranchRoot259+"/herb_entry259"};
   var place=layout.Places.Single(p=>p.Id=="herb_path");place.XZ=plan.routes[0].points.Last();place.HasSourceBinding=true;place.SceneRoots=new[]{BranchRoot259};place.InteractionIds=new[]{HerbTrace259};place.InteractionId=HerbTrace259;
   var bag=Object.Instantiate(originalBag.gameObject,root.transform);bag.name=HerbTrace259;
   foreach(var behaviour in bag.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(behaviour);
   foreach(var collider in bag.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
   var bp=place.XZ+new Vector2(1.2f,0);var foot=ground(bp.x,bp.y).point;bag.transform.SetPositionAndRotation(foot,Quaternion.Euler(0,35,0));bag.transform.localScale=originalBag.lossyScale;
   var renderers=bag.GetComponentsInChildren<Renderer>(true);float low=renderers.Min(r=>r.bounds.min.y);bag.transform.position+=Vector3.up*(foot.y-low+.015f);
   var identity=bag.AddComponent<WorldMacroContentPoint>();identity.Id=HerbTrace259;identity.Visual=bag.transform;
   s.Content.Points=s.Content.Points.Where(p=>p.Id!=HerbTrace259).Concat(new[]{new PrologueContentSO.Point{Id=HerbTrace259,Kind=PrologueInteractionKind.Evidence,Position=foot,Radius=2.3f,Prompt="",Text=HerbText259}}).ToArray();
   s.InteractionVisuals=s.InteractionVisuals.Where(p=>p.Id!=HerbTrace259).Concat(new[]{new WorldMacroPlaytestSession.InteractionVisual{Id=HerbTrace259,Renderers=renderers}}).ToArray();
   var campaign=s.Content.Campaign;
   campaign.Stages=campaign.Stages.Where(p=>p.Id!=HerbTrace259).Concat(new[]{new DemoCampaignProfile.Stage{Id=HerbTrace259,Objective="약초꾼이 남긴 흔적",TriggerId=HerbTrace259,Dialogue=HerbText259,Event=DemoEventKind.Interaction,Implemented=true,Optional=true,GrantedFacts=new[]{HerbFact259},DestinationId="herb_path",Destination=foot}}).ToArray();
   campaign.Testimonies=campaign.Testimonies.Where(t=>!(t.RequiredFacts??Array.Empty<string>()).Contains(HerbFact259)).Prepend(new DemoCampaignProfile.Testimony{TriggerId="herbalist",RequiredFacts=new[]{HerbFact259},Text="약초꾼: 잎은 멀쩡한데 뿌리만 검었다고? 벌목꾼도 그런 뿌리를 봤다더군. 전에는 그 산허리에서 좋은 풀을 캤소."}).ToArray();
   s.Content.Points.Single(p=>p.Id=="herbalist").Text="약초꾼: 벌목장을 피하려면 산허리의 좁은 길로 돌아가시오. 내 바구니를 두고 왔는데, 그 자리의 풀잎이 이상했소.";
   if(!campaign.IsValid)throw new Exception("Branch campaign invalid");
   var replaced=new HashSet<string>(plan.routes.Select(r=>r.id).Concat(new[]{"geumpyo_inn__herb_path","relay__herb_path","herb_path__deep_forest"}));
   layout.Routes=layout.Routes.Where(r=>!replaced.Contains(r.Id)).Concat(plan.routes.Select(r=>new CompactWorldLayoutSO.Route{Id=r.id,From=r.fromId,To=r.toId,Role=r.id=="herb_return259"?CompactRouteRole.ReturnShortcut:CompactRouteRole.Exploration,Width=1.7f,Bends=r.points.Skip(1).Take(r.points.Length-2).ToArray()})).ToArray();
   map.Lines=map.Lines.Where(l=>!replaced.Contains(l.Id)).Concat(paths.Select(p=>new WorldMapLineSpec{Id=p.Key,Kind=WorldMapLineKind.Trail,Points=p.Value.Select(v=>new Vector2(v.x,v.z)).ToArray()})).ToArray();
   bool Clear(Vector3 p)=>paths.Values.Any(path=>FlatPathDistance(p,path)<1.4f)||Vector3.Distance(p,foot)<2.2f;
   var removed=new HashSet<string>(art.Sheet.FixedPlacements.Where(p=>Clear(p.Position)).Select(p=>p.Id));
   var kept=art.Sheet.FixedPlacements.Where(p=>!removed.Contains(p.Id)&&!p.Id.StartsWith("herb259_",StringComparison.Ordinal)).ToList();
   for(int i=0;i<24;i++)
   {
    float angle=i*2.39996f,radius=2.8f+(i%5)*.9f;var pos=foot+new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius);pos=ground(pos.x,pos.z).point;
    if(paths.Values.Any(path=>FlatPathDistance(pos,path)<1.25f))continue;
    kept.Add(new WorldMacroDressingSheetSO.FixedPlacement{Id="herb259_"+i,ClusterId="herb_path",PrototypeId=i%3==0?"Cheongrim_SM_Grass":"Cheongrim_SM_Deparia_1",Position=pos,Euler=new Vector3(0,i*47%360,0),Scale=.8f+(i%4)*.1f});
   }
   var sheet=Asset("Placements",()=>Object.Instantiate(art.Sheet));sheet.FixedPlacements=kept.ToArray();art.Sheet=sheet;manifest.Art=sheet;art.Invalidate();
   foreach(var t in roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>removed.Contains(t.name)).ToArray())if(t!=null)Object.DestroyImmediate(t.gameObject);
   foreach(var obj in new Object[]{s,s.Content,campaign,layout,map,art,sheet,manifest})EditorUtility.SetDirty(obj);
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   File.WriteAllText(Output+"/layout.json",JsonUtility.ToJson(layout,true));File.WriteAllText(Output+"/Cartography/map.json",JsonUtility.ToJson(map,true));
   File.WriteAllText(Output+"/art_placements.json",JsonUtility.ToJson(sheet,true));File.WriteAllText(Output+"/Cartography/placements.json",JsonUtility.ToJson(sheet,true));
   File.WriteAllText(BranchOutput259+"/build.txt","Herb bypass, evidence and late testimony connected. No new reward or mandatory gate. Input traversal/lifecycle/art review pending.");
   return File.ReadAllText(BranchOutput259+"/build.txt");
  }
 }
}
