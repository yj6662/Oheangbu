using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.World.Vehicle;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string EscortRoot252="CapitalEscort252";
  public static string EscortBuild252()
  {
   var scene=FrontageScene249();if(scene.isDirty)throw new Exception("Save unrelated edits before escort authoring");
   var s=VillageSession();var ground=FinalSurface(scene);Vector3 G(Vector2 p)=>ground(p.x,p.y).point;
   string baseFolder=Path.GetDirectoryName(scene.path).Replace('\\','/'),folder=baseFolder+"/Escort252";
   Directory.CreateDirectory(folder);AssetDatabase.Refresh();
   T Asset<T>(string name,Func<T> make)where T:Object{var p=folder+"/"+name+".asset";var a=AssetDatabase.LoadAssetAtPath<T>(p);if(a==null){a=make();AssetDatabase.CreateAsset(a,p);}return a;}
   var old=scene.GetRootGameObjects().SingleOrDefault(g=>g.name==EscortRoot252);if(old!=null)Object.DestroyImmediate(old);
   var root=new GameObject(EscortRoot252);var route=root.AddComponent<DemoEscortSceneRoute>();
   var roots=scene.GetRootGameObjects();var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single();var layout=manifest.Layout;
   layout.Places=layout.Places.Where(p=>p.Id!="escort_departure").Concat(new[]{new CompactWorldLayoutSO.Place{Id="escort_departure",Realm="Cheongrim",Label="상경 출발 정차소",Purpose="실제 호송 탑승",XZ=new Vector2(2700,2154),GroundRadius=12,SurfaceBlendDistance=0,HasSourceBinding=true,SceneRoots=new[]{EscortRoot252+"/escort_start"},InteractionIds=new[]{"escort_start"},CheckpointIds=new[]{"escort_start"}}}).ToArray();
   layout.Routes=layout.Routes.Where(r=>r.Id!="merchant_departure252").Concat(new[]{new CompactWorldLayoutSO.Route{Id="merchant_departure252",From="merchant",To="escort_departure",Role=CompactRouteRole.Main,Width=2.5f,Bends=new[]{new Vector2(2706,2171),new Vector2(2705,2154)}}}).ToArray();
   var ui=roots.SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();var map=ui.MapData;
   var summon=s.GetComponent<WorldMacroPalanquinSummon>();var geography=summon.WorldSheet;
   if(!AssetDatabase.GetAssetPath(geography).StartsWith(baseFolder+"/"))throw new Exception("Private candidate geography required");
   var plan=JsonUtility.FromJson<ProgressionPlan251>(File.ReadAllText(EscortOutput252+"/route-plan.json"));
   var paths=new Dictionary<string,Vector3[]>();
   var road=Asset("RoadSoil",()=>Object.Instantiate(AssetDatabase.LoadAssetAtPath<Material>(baseFolder+"/Village245/RoadSoil.asset")));
   foreach(var r in plan.routes)
   {
    var path=new List<Vector3>();for(int i=1;i<r.points.Length;i++){int n=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(r.points[i-1],r.points[i])/2));for(int j=0;j<n;j++)path.Add(G(Vector2.Lerp(r.points[i-1],r.points[i],j/(float)n)));}path.Add(G(r.points.Last()));paths[r.id]=path.ToArray();
    var vv=new List<Vector3>();var tt=new List<int>();var uv=new List<Vector2>();float distance=0;
    for(int i=0;i<path.Count;i++)
    {
     if(i>0)distance+=Vector3.Distance(path[i],path[i-1]);var d=path[Math.Min(i+1,path.Count-1)]-path[Math.Max(0,i-1)];d.y=0;var side=Vector3.Cross(d.normalized,Vector3.up);
     float width=8+Mathf.Sin(distance*.12f)*.15f;
     for(int k=0;k<9;k++){var p=path[i]+side*((k/8f-.5f)*width);vv.Add(ground(p.x,p.z).point+Vector3.up*.03f);uv.Add(new Vector2(k,distance));}
     if(i>0)for(int k=0;k<8;k++){int v=i*9+k;tt.AddRange(new[]{v-9,v,v+1,v-9,v+1,v-8});}
    }
    var mesh=Asset(r.id,()=>new Mesh());mesh.Clear();mesh.SetVertices(vv);mesh.SetUVs(0,uv);mesh.SetTriangles(tt,0);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
    var go=new GameObject(r.id,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root.transform);go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=road;
    layout.Routes=layout.Routes.Where(x=>x.Id!=r.id&&!(x.From==r.fromId&&x.To==r.toId)).Concat(new[]{new CompactWorldLayoutSO.Route{Id=r.id,From=r.fromId,To=r.toId,Role=CompactRouteRole.Main,GradeForVehicle=true,Width=8,Bends=r.points.Skip(1).Take(r.points.Length-2).ToArray()}}).ToArray();
   }
   geography.Routes=geography.Routes.Where(r=>!paths.ContainsKey(r.Id)).Concat(plan.routes.Select(r=>new WorldMacroSheetSO.RouteSpec{Id=r.id,From=r.fromId,To=r.toId,Points=paths[r.id],Width=8,Carriage=true})).ToArray();
   map.Lines=map.Lines.Where(l=>!paths.ContainsKey(l.Id)).Concat(paths.Select(p=>new WorldMapLineSpec{Id=p.Key,Kind=WorldMapLineKind.Trail,Points=p.Value.Select(v=>new Vector2(v.x,v.z)).ToArray()})).ToArray();
   string pathSource=folder+"/OriginalMainPath252.json";if(!File.Exists(pathSource))File.WriteAllText(pathSource,JsonUtility.ToJson(new VillageLedger245{main=s.Content.MainPath}));
   s.Content.MainPath=JsonUtility.FromJson<VillageLedger245>(File.ReadAllText(pathSource)).main.Concat(paths.Values.SelectMany(p=>p)).ToArray();
   var source=EditorSceneManager.OpenScene(Scene,OpenSceneMode.Additive);
   try
   {
    var original=source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
    s.DemoEscortCargo=Object.Instantiate(original.DemoEscortCargo,root.transform);s.DemoEscortCargo.name="SealedCargo252";
   }
   finally{EditorSceneManager.CloseScene(source,true);SceneManager.SetActiveScene(scene);}
   s.DemoEscortCargo.position=ground(2702,2170).point;
   var navigator=s.DemoEscortCompanion.GetComponent<NavMeshAgent>();if(navigator==null)navigator=s.DemoEscortCompanion.gameObject.AddComponent<NavMeshAgent>();navigator.enabled=false;navigator.height=1.7f;navigator.radius=.27f;navigator.baseOffset=0;
   var ids=new[]{"escort_start","checkpoint_1","checkpoint_2","cargo_delivery"};var checkpointIds=new[]{"escort_start","road_rest_1","road_rest_2","capital_escort_rest"};
   var placeIds=new[]{"merchant","inspection_one","inspection_two","capital_delivery"};
   var routeIds=new[]{"merchant__road_pass","road_pass__inspection_one","road_hamlet__inspection_two","inspection_two__capital_delivery"};
   var texts=new[]{"서쪽 고개를 넘어가면 첫 검문이오. 봉인은 내가 지키겠소.","봉인이 남아 있군. 아래 취락을 지나 다음 검문으로 가시오.","화물과 차패를 확인했소. 성저 객주에서 인수할 것이오.","봉인이 온전합니다. 짐은 이곳에 내려놓으시오."};
   var owned=new HashSet<string>(ids.Concat(checkpointIds));var points=s.Content.Points.Where(p=>!owned.Contains(p.Id)).ToList();var visuals=s.InteractionVisuals.Where(p=>!owned.Contains(p.Id)).ToList();var stops=new List<DemoEscortStop>();
   Transform Node(Transform parent,string name,Vector3 p,float yaw=0){var n=new GameObject(name).transform;n.SetParent(parent);n.SetPositionAndRotation(p,Quaternion.Euler(0,yaw,0));return n;}
   void AddPoint(string id,PrologueInteractionKind kind,Vector3 p,float radius,Renderer[] rr,string text)
   {points.Add(new PrologueContentSO.Point{Id=id,Kind=kind,Position=p,Radius=radius,Prompt="",Text=text});visuals.Add(new WorldMacroPlaytestSession.InteractionVisual{Id=id,Renderers=rr});}
   var wood=AssetDatabase.LoadAssetAtPath<Material>(baseFolder+"/Village245/WeatheredWood.asset");
   for(int i=0;i<4;i++)
   {
    var place=layout.Places.Single(p=>p.Id==placeIds[i]);var center=i==0?G(new Vector2(2700,2154)):G(place.XZ);var path=paths[routeIds[i]];
    var f=i==0?Vector3.left:Vector3.ProjectOnPlane(path.Last()-path[Math.Max(0,path.Length-5)],Vector3.up).normalized;var right=Vector3.Cross(Vector3.up,f);float yaw=Quaternion.LookRotation(f).eulerAngles.y;
    Vector3 Ground(Vector3 p)=>ground(p.x,p.z).point;
    var stopRoot=Node(root.transform,ids[i],center);var interaction=i==0?center:Ground(center+right*6);
    var wait=i==0?G(new Vector2(2705,2154)):Ground(center+right*3+f*2);var cargoWait=Ground(center+right*4+f*2);var checkpoint=i==0?G(new Vector2(2706,2154)):Ground(center+right*6-f*3);
    var stop=new DemoEscortStop{Id=ids[i],CheckpointId=checkpointIds[i],RouteId=routeIds[i],Interaction=Node(stopRoot,"Interaction",interaction),Parking=Node(stopRoot,"Parking",center,yaw),CompanionWait=Node(stopRoot,"CompanionWait",wait),CargoWait=Node(stopRoot,"CargoWait",cargoWait),Checkpoint=Node(stopRoot,"Checkpoint",checkpoint,yaw)};stops.Add(stop);
    if(i==0){stop.StagingApproach=Node(stopRoot,"StagingApproach",G(new Vector2(2706,2171)));AddPoint(ids[i],PrologueInteractionKind.Conversation,interaction,4.5f,Array.Empty<Renderer>(),texts[i]);continue;}
    var npc=Object.Instantiate(s.DemoEscortCompanion.gameObject,stopRoot);npc.name=i==3?"CargoClerk252":"InspectionOfficer252";npc.transform.SetPositionAndRotation(interaction,Quaternion.LookRotation(-right));
    foreach(var a in npc.GetComponents<NavMeshAgent>())Object.DestroyImmediate(a);
    foreach(var cp in npc.GetComponentsInChildren<WorldMacroContentPoint>(true))Object.DestroyImmediate(cp);
    var marker=npc.AddComponent<WorldMacroContentPoint>();marker.Id=ids[i];marker.Visual=npc.transform;
    AddPoint(ids[i],PrologueInteractionKind.Conversation,interaction,3.2f,npc.GetComponentsInChildren<Renderer>(true),texts[i]);
    WorldMacroVisualCorridorAuthoring.PlaceSource(stopRoot,"InspectionDesk252","Assets/HwaseongHaenggung/Prefabs/SM_M_WoodenBox.prefab",Ground(interaction+f*2),new Vector3(1,.85f,.75f),yaw,true);
    var rest=GameObject.CreatePrimitive(PrimitiveType.Cube);rest.name=checkpointIds[i];rest.transform.SetParent(stopRoot);rest.transform.position=checkpoint+right*1.4f+Vector3.up*.23f;rest.transform.localScale=new Vector3(.5f,.46f,1.3f);rest.GetComponent<Renderer>().sharedMaterial=wood;
    AddPoint(checkpointIds[i],PrologueInteractionKind.Rest,checkpoint,2,rest.GetComponentsInChildren<Renderer>(),"");
    if(i==3)WorldMacroVisualCorridorAuthoring.PlaceSource(stopRoot,"Guesthouse252","Assets/HwaseongHaenggung/Prefabs/SM_Naeposa.prefab",Ground(interaction+right*10),new Vector3(12,0,10),yaw,false);
    place.HasSourceBinding=true;place.SceneRoots=new[]{EscortRoot252+"/"+ids[i]};place.InteractionIds=new[]{ids[i],checkpointIds[i]};place.CheckpointIds=new[]{checkpointIds[i]};
   }
   route.Stops=stops.ToArray();s.Content.Points=points.ToArray();s.InteractionVisuals=visuals.ToArray();
   s.Content.Checkpoints=s.Content.Checkpoints.Where(p=>!checkpointIds.Contains(p.Id)).Concat(stops.Select(p=>new WorldMacroPlaytestSO.CheckpointSpec{Id=p.CheckpointId,Label=p.Id=="escort_start"?"청림 출발 정차소":"상경길 쉼터",Feet=p.Checkpoint.position,Yaw=p.Checkpoint.eulerAngles.y,Shop=false})).ToArray();
   if(!s.ConfigureDemoEscort(s.DemoEscortCompanion,s.DemoEscortCargo,summon.Seat,s.DemoEscortPassengerSocket,s.DemoEscortCargoSocket,summon))throw new Exception("Escort session references invalid");
   var presentation=root.AddComponent<DemoEscortPresentation>();if(!presentation.Configure(s,route))throw new Exception("Escort presentation references invalid");
   FixDeliveryFrontage253(s,scene);
   ConfigureParkingNavigation252(s);
   foreach(var stage in s.Content.Campaign.Stages){if(new[]{"escort","checkpoint_one","checkpoint_two","delivery"}.Contains(stage.Id))stage.Implemented=true;var p=points.FirstOrDefault(p=>p.Id==stage.TriggerId);if(p!=null)stage.Destination=p.Position;}
   var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)).Single();var sheet=Asset("Placements",()=>Object.Instantiate(art.Sheet));
   var plants=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(baseFolder+"/Progression251/Placements.asset");
   bool Clear(Vector3 p)=>paths.Values.Any(path=>FlatPathDistance(p,path)<5.5f)||stops.Any(x=>Vector3.Distance(p,x.Interaction.position)<12);
   sheet.FixedPlacements=plants.FixedPlacements.Where(p=>!Clear(p.Position)).ToArray();art.Sheet=sheet;manifest.Art=sheet;art.Invalidate();
   var kept=new HashSet<string>(sheet.FixedPlacements.Select(p=>p.Id));foreach(var t in roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>t.name.StartsWith("grove_")||t.name.StartsWith("outcrop_")||t.name.StartsWith("path_meshy_")||t.name.StartsWith("inn_meshy_")).ToArray())if(t!=null&&!kept.Contains(t.name)&&Clear(t.position))Object.DestroyImmediate(t.gameObject);
   foreach(var o in new Object[]{s,s.Content,s.Content.Campaign,layout,map,geography,sheet,art,manifest})EditorUtility.SetDirty(o);
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   File.WriteAllText(Output+"/layout.json",JsonUtility.ToJson(layout,true));File.WriteAllText(Output+"/Cartography/map.json",JsonUtility.ToJson(map,true));File.WriteAllText(Output+"/art_placements.json",JsonUtility.ToJson(sheet,true));File.WriteAllText(Output+"/Cartography/placements.json",JsonUtility.ToJson(sheet,true));File.WriteAllText(EscortOutput252+"/content.json",JsonUtility.ToJson(s.Content,true));
   return "Escort scene authored; four stages enabled. NavMesh and runtime verification required. South gate stages remain disabled.";
  }
  static void ConfigureParkingNavigation252(WorldMacroPlaytestSession session)
  {
   var car=session.DemoEscortSeat.Vehicle;var hull=car.Hull;
   var obstacle=car.GetComponent<NavMeshObstacle>();if(obstacle==null)obstacle=car.gameObject.AddComponent<NavMeshObstacle>();
   var bounds=new Bounds(car.transform.InverseTransformPoint(hull.transform.TransformPoint(hull.center)),Vector3.zero);
   foreach(float x in new[]{-1f,1f})foreach(float y in new[]{-1f,1f})foreach(float z in new[]{-1f,1f})
    bounds.Encapsulate(car.transform.InverseTransformPoint(hull.transform.TransformPoint(hull.center+Vector3.Scale(hull.size*.5f,new Vector3(x,y,z)))));
   obstacle.shape=NavMeshObstacleShape.Box;obstacle.center=bounds.center;obstacle.size=bounds.size+new Vector3(.4f,.1f,.4f);
   obstacle.carving=true;obstacle.carveOnlyStationary=true;obstacle.carvingMoveThreshold=.3f;obstacle.carvingTimeToStationary=.3f;
   EditorUtility.SetDirty(obstacle);
  }
  public static string EscortParkingNavigation252()
  {
   var scene=FrontageScene249();if(scene.isDirty)throw new Exception("Save unrelated scene edits first");
   ConfigureParkingNavigation252(VillageSession());EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   return "Candidate parked car carves NPC navigation; moving car uses existing physics. No static terrain bake or normal save change.";
  }
 }
}
