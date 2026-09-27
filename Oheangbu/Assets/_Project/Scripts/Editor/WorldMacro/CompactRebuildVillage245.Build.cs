using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.Prologue;
using Oheangbu.Data.World;
using Oheangbu.Data.Demo;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro {
 public static partial class CompactRebuildAuthoring {
  [Serializable] public sealed class VillageLedger245 {public string revision="village245";public Vector3[] main,branch,forest;public VillageBuilding245[] buildings;public float length;public string scene,content,catalog;}
  [Serializable] public sealed class VillageBuilding245 {public string id;public Vector2 xz;public Vector3 size;public float yaw;}
  public static string VillageBuild(){
   var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlayingOrWillChangePlaymode||!scene.path.Contains("slice-5e82ecd76d2a/"))throw new Exception("Candidate Edit required");
   var roots=scene.GetRootGameObjects();var s=roots.SelectMany(x=>x.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
   var manifest=roots.SelectMany(x=>x.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single();var layout=manifest.Layout;
   string folder=System.IO.Path.GetDirectoryName(scene.path).Replace('\\','/')+"/Village245";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
   T Asset<T>(string name,Func<T> create)where T:Object{string path=folder+"/"+name+".asset";var value=AssetDatabase.LoadAssetAtPath<T>(path);if(value==null){value=create();AssetDatabase.CreateAsset(value,path);}return value;}
   var catalog=Asset("EquipmentTEST",()=>ScriptableObject.CreateInstance<EquipmentCatalogSO>());s.Content.EquipmentCatalog=catalog;
   // Only the opt-in candidate profile gains new optional stages.
   var campaign=Asset("Campaign",()=>Object.Instantiate(s.Content.Campaign));s.Content.Campaign=campaign;
   var stages=campaign.Stages.Where(x=>!x.Id.StartsWith("village_")).ToList();
   void Stage(string id,string trigger,string text,string[] facts,string[] grants,int reward=0){stages.Add(new DemoCampaignProfile.Stage{Id=id,TriggerId=trigger,Event=DemoEventKind.Interaction,Implemented=true,Optional=true,Objective=text,Dialogue=text,Prompt="",RequiredFacts=facts,GrantedFacts=grants,TongboReward=reward});}
   Stage("village_toolbox","village_toolbox","대패와 끌이 든 공구함이다. 손잡이에 장인의 매듭이 남아 있다.",Array.Empty<string>(),new[]{"evidence:village_toolbox"});
   Stage("village_tool_report","village_tool_report","이 매듭은 내 것이지. 덕분에 작업을 다시 시작하겠네.",new[]{"evidence:village_toolbox"},new[]{"delivered:village_toolbox"},80);
   Stage("village_cut_trace","village_cut_trace","톱이 지나간 자리에 가느다란 새 나이테가 돋았다. 진액은 아직 차갑다.",Array.Empty<string>(),new[]{"evidence:village_cut_trace"});
   Stage("village_cut_report","village_resident","그루터기를 베어도 같은 자국이 돌아온다니… 숲 안쪽 벌목은 멈춰야겠군.",new[]{"evidence:village_cut_trace"},new[]{"reported:village_cut_trace"},80);
   campaign.Testimonies=campaign.Testimonies.Where(t=>t.TriggerId!="village_resident").Concat(new[]{new DemoCampaignProfile.Testimony{TriggerId="village_resident",RequiredFacts=new[]{"reported:village_cut_trace"},Text="당분간 안쪽 나무는 베지 않겠소. 알려 줘서 고맙소."}}).ToArray();
   campaign.Stages=stages.ToArray();campaign.WorkInProgressText="청림 벌목마을에 도착했다. 상경 동행은 아직 준비 중이다.";
   if(!campaign.IsValid)throw new Exception("Campaign invalid");
   var old=roots.SingleOrDefault(x=>x.name=="Village245");if(old!=null)Object.DestroyImmediate(old);roots=roots.Where(x=>x!=null).ToArray();
   var root=new GameObject("Village245");var ground=FinalSurface(scene);Vector3 G(float x,float z)=>ground(x,z).point;
   Vector3[] Path(params Vector2[] points){var a=new List<Vector3>();for(int i=0;i<points.Length-1;i++){int steps=Mathf.CeilToInt(Vector2.Distance(points[i],points[i+1])/2);for(int j=0;j<steps;j++){var q=Vector2.Lerp(points[i],points[i+1],j/(float)steps);a.Add(G(q.x,q.y));}}a.Add(G(points.Last().x,points.Last().y));return a.ToArray();}
   var main=Path(new Vector2(3110,2250),new Vector2(3095,2242),new Vector2(3078,2268),new Vector2(3055,2305),new Vector2(3038,2448),new Vector2(2940,2480),new Vector2(2868,2438),new Vector2(2840,2390),new Vector2(2820,2310),new Vector2(2798,2260),new Vector2(2750,2216),new Vector2(2700,2180));
   var branch=Path(new Vector2(3038,2448),new Vector2(3009,2408),new Vector2(2975,2400),new Vector2(2930,2435),new Vector2(2940,2480));
   var forest=Path(new Vector2(2820,2310),new Vector2(2790,2340),new Vector2(2695,2328),new Vector2(2660,2272),new Vector2(2660,2210),new Vector2(2680,2217),new Vector2(2700,2204),new Vector2(2700,2180));
   var buildings=new[]{
    new VillageBuilding245{id="gaekju",xz=new Vector2(2695,2163),size=new Vector3(17,5.6f,12),yaw=180},
    new VillageBuilding245{id="shop",xz=new Vector2(2720,2181),size=new Vector3(12,4.7f,9),yaw=90},
    new VillageBuilding245{id="workshop",xz=new Vector2(2677,2190),size=new Vector3(15,4.5f,10),yaw=-90},
    new VillageBuilding245{id="resident",xz=new Vector2(2724,2214),size=new Vector3(12,4.4f,9),yaw=0},
    new VillageBuilding245{id="vacant_east",xz=new Vector2(2755,2152),size=new Vector3(10,4.1f,8),yaw=-30},
    new VillageBuilding245{id="home_west",xz=new Vector2(2648,2160),size=new Vector3(11,4.3f,9),yaw=20},
    new VillageBuilding245{id="vacant_north",xz=new Vector2(2688,2240),size=new Vector3(11,4.0f,8),yaw=168},
    new VillageBuilding245{id="relay_shelter",xz=new Vector2(2855,2380),size=new Vector3(12,4.4f,9),yaw=90}};
   var ledger=new VillageLedger245{main=main,branch=branch,forest=forest,buildings=buildings,scene=scene.path,content=AssetDatabase.GetAssetPath(s.Content),catalog=AssetDatabase.GetAssetPath(catalog)};
   for(int i=1;i<main.Length;i++)ledger.length+=Vector3.Distance(main[i-1],main[i]);
   if(ledger.length<700||ledger.length>850)throw new Exception("Route length outside agreed range: "+ledger.length);
   var soilSource=roots.SelectMany(x=>x.GetComponentsInChildren<Renderer>(true)).FirstOrDefault(x=>x.name=="Continuous_Approach_Soil")?.sharedMaterial??roots.SelectMany(x=>x.GetComponentsInChildren<Renderer>(true)).First(x=>x.name=="Natural_Cave_Floor").sharedMaterial;
   var soil=Asset("RoadSoil",()=>Object.Instantiate(soilSource));
   var wood=Asset("WeatheredWood",()=>new Material(Shader.Find("Universal Render Pipeline/Lit")));wood.SetColor("_BaseColor",new Color(.24f,.20f,.15f));wood.SetFloat("_Smoothness",.05f);
   var stone=Asset("FoundationStone",()=>Object.Instantiate(soilSource));
   GameObject Box(string name,Transform parent,Vector3 pos,Vector3 size,Material mat,bool collide=true){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent);go.transform.position=pos;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=mat;if(!collide)Object.DestroyImmediate(go.GetComponent<Collider>());return go;}
   void Ribbon(string name,Vector3[] path,float width){var verts=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();float distance=0;
    for(int i=0;i<path.Length;i++){var d=path[Math.Min(i+1,path.Length-1)]-path[Math.Max(0,i-1)];d.y=0;var side=Vector3.Cross(d.normalized,Vector3.up);float w=width*(.94f+.06f*Mathf.Sin(i*.73f));
     if(i>0)distance+=Vector3.Distance(path[i-1],path[i]);for(int k=0;k<5;k++){var p=path[i]+side*w*(k/4f-.5f);p.y=ground(p.x,p.z).point.y+.035f;verts.Add(p);uv.Add(new Vector2(k*width/4,distance));}
     if(i>0)for(int k=0;k<4;k++){int v=i*5+k;tris.AddRange(new[]{v-5,v,v+1,v-5,v+1,v-4});}}
    var mesh=Asset(name,()=>new Mesh());mesh.Clear();mesh.SetVertices(verts);mesh.SetUVs(0,uv);mesh.SetTriangles(tris,0);UpwardRibbon258(mesh);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
    var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root.transform);go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=soil;go.GetComponent<MeshRenderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
   }
   Ribbon("Village_MainTrack",main,5.4f);Ribbon("Village_EvidenceLoop",branch,2.6f);Ribbon("Village_ForestEntrance",forest,2.7f);
   // Swept yard lanes join services. Ground stays on the existing mountain surface.
   foreach(var q in new[]{new Vector2(2701,2172),new Vector2(2710,2181),new Vector2(2687,2190),new Vector2(2724,2207)})Ribbon("Yard_"+q.x+"_"+q.y,Path(new Vector2(2700,2180),q),3.8f);
   var innPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/CodexWorld/ThatchedInn/ThatchedInn.prefab");
   var doors=new Dictionary<string,Renderer>();
   foreach(var b in buildings){
    var holder=new GameObject(b.id);holder.transform.SetParent(root.transform);holder.transform.position=G(b.xz.x,b.xz.y);holder.transform.rotation=Quaternion.Euler(0,b.yaw,0);
    var house=(GameObject)PrefabUtility.InstantiatePrefab(innPrefab);house.transform.SetParent(holder.transform,false);house.transform.localPosition=Vector3.zero;house.transform.localRotation=Quaternion.identity;
    var rr=house.GetComponentsInChildren<Renderer>(true);var bounds=rr[0].bounds;foreach(var x in rr)bounds.Encapsulate(x.bounds);
    // Normalize in unrotated prefab space before rotating the complete building.
    holder.transform.rotation=Quaternion.identity;Physics.SyncTransforms();bounds=rr[0].bounds;foreach(var x in rr)bounds.Encapsulate(x.bounds);
    house.transform.localScale=Vector3.Scale(house.transform.localScale,new Vector3(b.size.x/bounds.size.x,b.size.y/bounds.size.y,b.size.z/bounds.size.z));Physics.SyncTransforms();bounds=rr[0].bounds;foreach(var x in rr)bounds.Encapsulate(x.bounds);
    house.transform.position+=new Vector3(holder.transform.position.x-bounds.center.x,holder.transform.position.y-bounds.min.y+.12f,holder.transform.position.z-bounds.center.z);holder.transform.rotation=Quaternion.Euler(0,b.yaw,0);
    // Shallow stone plinth only under the building, never a town-wide flattening slab.
    var baseGo=Box("StoneFooting",holder.transform,holder.transform.position+Vector3.down*.28f,new Vector3(b.size.x*.78f,.7f,b.size.z*.70f),stone);baseGo.transform.rotation=holder.transform.rotation;
    var originalDoor=house.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name=="house_Re_Door");
    if(b.id=="gaekju"){
     var extracted=SurveyDoor(originalDoor,folder,G(2700,2180));doors[b.id]=extracted.GetComponent<Renderer>();
    }
    if(b.id.StartsWith("vacant")){
     foreach(var light in house.GetComponentsInChildren<Light>())light.enabled=false;
     var window=originalDoor.GetComponent<Renderer>();var wb=window.bounds;
     var board=Box("ClosedShutters",holder.transform,wb.center,new Vector3(1.3f,.13f,.10f),wood,false);board.transform.rotation=holder.transform.rotation*Quaternion.Euler(0,0,12);
    }
    else {var smokeGo=new GameObject("HearthSmoke");smokeGo.transform.SetParent(holder.transform,false);smokeGo.transform.localPosition=new Vector3(-b.size.x*.24f,b.size.y*.68f,0);var ps=smokeGo.AddComponent<ParticleSystem>();var m=ps.main;m.startLifetime=7;m.startSpeed=.36f;m.startSize=1.1f;m.startColor=new Color(.36f,.36f,.32f,.10f);m.maxParticles=25;var emission=ps.emission;emission.rateOverTime=2;var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.radius=.2f;shape.angle=7;var pr=ps.GetComponent<ParticleSystemRenderer>();var sm=Asset("Smoke",()=>new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")));sm.SetFloat("_Surface",1);sm.SetFloat("_Blend",0);sm.SetFloat("_ZWrite",0);sm.SetInt("_SrcBlend",5);sm.SetInt("_DstBlend",10);sm.renderQueue=3000;sm.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");pr.sharedMaterial=sm;}
   }
   // Reuse accepted original character geometry, never the rejected generated rigs.
   var npcTemplate=roots.SelectMany(x=>x.GetComponentsInChildren<Transform>(true)).FirstOrDefault(x=>x.name=="geumpyo_innkeeper");
   var points=s.Content.Points.Where(p=>!p.Id.StartsWith("village_")&&p.Id!="jeongdam_j1"&&p.Id!="wangso_w1").ToList();
   var visuals=s.InteractionVisuals.Where(v=>!v.Id.StartsWith("village_")&&v.Id!="jeongdam_j1"&&v.Id!="wangso_w1").ToList();
   GameObject Point(string id,float x,float z,string text,bool npc){var pos=G(x,z);var go=npc&&npcTemplate!=null?Object.Instantiate(npcTemplate.gameObject):new GameObject(id);go.name=id;go.transform.SetParent(root.transform);go.transform.position=pos;go.transform.rotation=Quaternion.Euler(0,180,0);
    foreach(var oldPoint in go.GetComponentsInChildren<WorldMacroContentPoint>(true))Object.DestroyImmediate(oldPoint);
    var cp=go.AddComponent<WorldMacroContentPoint>();cp.Id=id;cp.Visual=go.transform;
    if(!npc){var prop=Box("EvidenceBox",go.transform,pos+Vector3.up*.25f,new Vector3(.8f,.5f,.5f),wood);cp.Visual=prop.transform;}
    var renderers=go.GetComponentsInChildren<Renderer>(true);visuals.Add(new WorldMacroPlaytestSession.InteractionVisual{Id=id,Renderers=renderers});
    points.Add(new PrologueContentSO.Point{Id=id,Kind=npc?PrologueInteractionKind.Conversation:PrologueInteractionKind.Evidence,Position=pos,Radius=3.2f,Prompt="",Text=text});return go;
   }
   Point("jeongdam_j1",2844,2390,"정담이다. 아래 마을은 아직 사람이 남았네. 연기가 나는 객주에서 왕소를 찾게.",true);
   s.DemoEscortCompanion=Point("wangso_w1",2701,2172,"나는 왕소요. 마을은 쓸쓸해도 객주 문은 열려 있소. 역참의 정담을 만나 보시오.",true).transform;
   Point("village_shop",2710,2181,"쓰임이 남은 물건만 골라 두었소.",true);
   Point("village_artisan",2687,2190,"계곡 옆 부서진 수레에 공구함을 놓고 왔네.",true);
   Point("village_resident",2724,2207,"잘라 둔 나무가 이상하더군. 역참 위쪽 수레길 곁 그루터기를 살펴 주겠소?",true);
   Point("village_toolbox",2975,2400,"대패와 끌이 든 공구함이다.",false);
   Point("village_cut_trace",2955,2515,"자른 나무에 새 나이테가 돋았다.",false);
   var gd=doors["gaekju"];var restToward=G(2700,2180)-gd.bounds.center;restToward.y=0;restToward.Normalize();var restPos=new Vector3(gd.bounds.center.x,gd.bounds.min.y,gd.bounds.center.z)+restToward*.7f;
   // A narrow threshold apron joins the existing porch to the yard; no whole-village terrain flattening.
   var apronTop=restPos;apronTop.y=gd.bounds.min.y+.035f;var apronEnd=apronTop+restToward*5;apronEnd.y=G(apronEnd.x,apronEnd.z).y+.025f;
   var apronSide=Vector3.Cross(Vector3.up,restToward)*1.25f;
   var apronMesh=Asset("GuesthouseThreshold",()=>new Mesh());apronMesh.Clear();apronMesh.vertices=new[]{apronTop-apronSide,apronTop+apronSide,apronEnd-apronSide,apronEnd+apronSide};apronMesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};apronMesh.triangles=new[]{0,2,1,1,2,3};apronMesh.RecalculateNormals();apronMesh.RecalculateBounds();EditorUtility.SetDirty(apronMesh);
   var apron=new GameObject("GuesthouseThreshold",typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider));apron.transform.SetParent(root.transform);apron.GetComponent<MeshFilter>().sharedMesh=apronMesh;apron.GetComponent<MeshRenderer>().sharedMaterial=stone;apron.GetComponent<MeshCollider>().sharedMesh=apronMesh;
   var restIdentity=gd.gameObject.AddComponent<WorldMacroContentPoint>();restIdentity.Id="village_rest";restIdentity.Visual=gd.transform;
   points.Add(new PrologueContentSO.Point{Id="village_rest",Kind=PrologueInteractionKind.Rest,Position=restPos,Radius=3.5f,Prompt="",Text=""});visuals.Add(new WorldMacroPlaytestSession.InteractionVisual{Id="village_rest",Renderers=new[]{gd}});
   var yard=restPos+restToward*6.5f;var returnFeet=G(yard.x,yard.z);s.Content.Checkpoints=s.Content.Checkpoints.Where(c=>c.Id!="village_rest").Concat(new[]{new WorldMacroPlaytestSO.CheckpointSpec{Id="village_rest",Label="청림 객주",Feet=returnFeet,Yaw=0,Shop=false}}).ToArray();
   var rest=gd.gameObject.AddComponent<WorldMacroInnRestPresentation>();rest.Session=s;rest.Door=gd.transform;rest.PointId="village_rest";rest.HingeWorld=gd.bounds.center-Vector3.right*.6f;rest.ReturnYaw=0;s.VillageRestPresentation=rest;
   var logger=points.Find(p=>p.Id=="logger");if(logger!=null)logger.Text="마을에 빈집이 늘었어도 대패 소리는 끊기지 않았소. 수레길을 따라 역참을 지나면 객주가 나오지.";
   var keeper=points.Find(p=>p.Id=="geumpyo_innkeeper");if(keeper!=null)keeper.Text=KeeperGuidance258;
   s.Content.Points=points.ToArray();s.InteractionVisuals=visuals.ToArray();
   string originalPath=folder+"/OriginalMainPath.json";if(!File.Exists(originalPath))File.WriteAllText(originalPath,JsonUtility.ToJson(new VillageLedger245{main=s.Content.MainPath}));
   s.Content.MainPath=JsonUtility.FromJson<VillageLedger245>(File.ReadAllText(originalPath)).main.Concat(main).ToArray();
   var actors=s.Actors.Where(a=>a!=null&&!a.Id.StartsWith("village_")).ToList();var encounters=s.Content.Encounters.Where(a=>!a.Id.StartsWith("village_")).ToList();
   for(int i=0;i<2;i++){string id="village_road_raider_"+i;var pos=G(2951+i*7,2518+i*2);var go=Object.Instantiate(actors[0].gameObject,root.transform);go.name=id;go.transform.position=pos;
    var a=go.GetComponent<PrologueEncounter>();a.Id=id;a.PatrolPoints=new[]{pos,G(pos.x+3,pos.z+4)};a.DetectionRange=13;a.Leash=19;a.Player=s.Walker.Body.transform;actors.Add(a);
    encounters.Add(new WorldMacroPlaytestSO.Encounter{Id=id,ContentId="village_cut_trace",Feet=pos,Patrol=a.PatrolPoints,Detection=13,Leash=19,Speed=2.6f});}
   s.Actors=actors.ToArray();s.Content.Encounters=encounters.ToArray();
   // Lived-in work edges stay out of the arrival court and walking lanes.
   for(int i=0;i<18;i++){var p=G(2665+(i%6)*.4f,2176);Box("DryingPlank_"+i,root.transform,p+Vector3.up*(.20f+i/6*.15f),new Vector3(.3f,.12f,3.6f),wood);}
   for(int i=0;i<8;i++){var p=G(2738,2167+i*.3f);Box("SplitFirewood_"+i,root.transform,p+Vector3.up*.18f,new Vector3(1.8f,.32f,.24f),wood);}
   foreach(var p in new[]{new Vector2(3061,2308),new Vector2(2850,2387),new Vector2(2975,2404)}){var pos=G(p.x,p.y);Box("TimberCartBed",root.transform,pos+Vector3.up*.55f,new Vector3(1.5f,.18f,2.5f),wood);for(int i=-1;i<=1;i+=2){var wheel=GameObject.CreatePrimitive(PrimitiveType.Cylinder);wheel.name="CartWheel";wheel.transform.SetParent(root.transform);wheel.transform.position=pos+new Vector3(i*.85f,.44f,0);wheel.transform.rotation=Quaternion.Euler(0,0,90);wheel.transform.localScale=new Vector3(.8f,.10f,.8f);wheel.GetComponent<Renderer>().sharedMaterial=wood;}}
   foreach(var item in new[]{(id:"ShopStock",x:2715f,z:2186f),(id:"WorkshopTools",x:2685f,z:2195f),(id:"GaekjuCargo",x:2691f,z:2175f)}){
    for(int i=0;i<3;i++){var crate=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HwaseongHaenggung/Prefabs/SM_M_WoodenBox.prefab"));crate.name=item.id+"_"+i;crate.transform.SetParent(root.transform);crate.transform.position=G(item.x+i*.9f,item.z);}
   }
   // Single authoritative ledger additions include the full village boundary and service identity.
   var places=layout.Places.Where(p=>!p.Id.StartsWith("village")).ToList();
   places.Add(new CompactWorldLayoutSO.Place{Id="village",Realm="Cheongrim",Label="청림 벌목마을",Purpose="주민이 남은 생활 거점",XZ=new Vector2(2700,2180),GroundRadius=80,SurfaceBlendDistance=0,HasSourceBinding=true,SceneRoots=new[]{"Village245"},InteractionIds=new[]{"village_shop","village_artisan","village_resident","village_rest","wangso_w1"},CheckpointIds=new[]{"village_rest"}});
   foreach(var point in points.Where(x=>x.Id.StartsWith("village_")&&x.Id!="village_rest"))places.Add(new CompactWorldLayoutSO.Place{Id=point.Id,Realm="Cheongrim",Label=point.Id,Purpose="service/evidence",XZ=new Vector2(point.Position.x,point.Position.z),GroundRadius=3,SurfaceBlendDistance=0,HasSourceBinding=true,InteractionId=point.Id,InteractionIds=new[]{point.Id},SceneRoots=new[]{"Village245/"+point.Id}});
   var relay=places.Single(p=>p.Id=="relay");relay.HasSourceBinding=true;relay.InteractionIds=new[]{"jeongdam_j1"};relay.SceneRoots=new[]{"Village245/jeongdam_j1"};
   var merchant=places.Single(p=>p.Id=="merchant");merchant.XZ=new Vector2(2701,2172);merchant.HasSourceBinding=true;merchant.InteractionIds=new[]{"wangso_w1"};merchant.SceneRoots=new[]{"Village245/wangso_w1"};layout.Places=places.ToArray();
   var routes=layout.Routes.Where(r=>r.Id!="village_forest"&&!(r.From=="geumpyo_inn"&&r.To=="relay")&&!(r.From=="relay"&&r.To=="merchant")).ToList();
   routes.Add(new CompactWorldLayoutSO.Route{Id="inn_relay245",From="geumpyo_inn",To="relay",Role=CompactRouteRole.Main,Width=5.4f,Bends=new[]{new Vector2(3095,2242),new Vector2(3078,2268),new Vector2(3055,2305),new Vector2(3038,2448),new Vector2(2940,2480),new Vector2(2868,2438)}});
   routes.Add(new CompactWorldLayoutSO.Route{Id="relay_village245",From="relay",To="village",Role=CompactRouteRole.Main,Width=5.4f,Bends=new[]{new Vector2(2820,2310),new Vector2(2798,2260),new Vector2(2750,2216)}});
   routes.Add(new CompactWorldLayoutSO.Route{Id="village_merchant245",From="village",To="merchant",Role=CompactRouteRole.Main,Width=3.8f});
   routes.Add(new CompactWorldLayoutSO.Route{Id="village_forest",From="relay",To="village",Role=CompactRouteRole.Exploration,Width=2.7f,Bends=forest.Where((p,i)=>i%10==0).Select(p=>new Vector2(p.x,p.z)).ToArray()});foreach(var service in places.Where(p=>p.Id.StartsWith("village_"))){routes.RemoveAll(r=>r.Id=="access_"+service.Id);routes.Add(new CompactWorldLayoutSO.Route{Id="access_"+service.Id,From=service.Id=="village_toolbox"||service.Id=="village_cut_trace"?"relay":"village",To=service.Id,Role=CompactRouteRole.Exploration,Width=2.6f});}
   layout.Routes=routes.GroupBy(x=>x.Id).Select(x=>x.Last()).ToArray();
   var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)).Single();var sheet=Asset("Placements",()=>Object.Instantiate(art.Sheet));
   bool Clear(Vector3 p){if(FlatPathDistance(p,main)<5||FlatPathDistance(p,branch)<3||FlatPathDistance(p,forest)<3)return false;if(Vector2.Distance(new Vector2(p.x,p.z),new Vector2(2700,2180))<13)return false;foreach(var b in buildings)if(Vector2.Distance(new Vector2(p.x,p.z),b.xz)<13)return false;foreach(var q in points)if(Vector3.Distance(q.Position,p)<q.Radius+2)return false;return true;}
   var fixedPlants=sheet.FixedPlacements.Where(p=>!p.Id.StartsWith("v245_")&&Clear(p.Position)).ToList();var random=new System.Random(245);string[] trees={"Cheongrim_SM_PinusDensiflora_Spring_2","Cheongrim_SM_UlmusDavidiana_Summer_2"};
   int n=0;for(float x=2580;x<3100;x+=12)for(float z=2070;z<2530;z+=12){float px=x+(float)random.NextDouble()*5,pz=z+(float)random.NextDouble()*5;var hit=ground(px,pz);if(!Clear(hit.point)||hit.normal.y<.8f||random.NextDouble()>.43)continue;fixedPlants.Add(new WorldMacroDressingSheetSO.FixedPlacement{Id="v245_tree_"+n++,ClusterId="village_woods",PrototypeId=trees[n%2],Position=hit.point,Scale=n%2==0?1.45f:.8f,Euler=new Vector3(0,n*137.5f,0)});}
   // Low undergrowth and interrupted boundary fences make the seven houses read as one lived-in settlement.
   for(int i=0;i<3400;i++){float x=2600+(float)random.NextDouble()*215,z=2090+(float)random.NextDouble()*205;var p=ground(x,z).point;if(!Clear(p)||ground(x,z).normal.y<.8f)continue;
    fixedPlants.Add(new WorldMacroDressingSheetSO.FixedPlacement{Id="v245_grass_"+i,ClusterId="village_understory",PrototypeId=i%7==0?"Cheongrim_SM_Deparia_1":"Cheongrim_LowGroundFill",Position=p,Scale=1.2f+(float)random.NextDouble(),Euler=new Vector3(0,i*137,0)});}
   foreach(var b in buildings){
    if(b.id=="gaekju")continue;
    for(int i=0;i<6;i++){var pos=G(b.xz.x-10+i*3,b.xz.y-9);Box("YardFencePost",root.transform,pos+Vector3.up*.48f,new Vector3(.13f,.96f,.13f),wood);if(i<5)Box("YardFenceRail",root.transform,pos+new Vector3(1.5f,.68f,0),new Vector3(3,.09f,.10f),wood);}
    for(int i=0;i<4;i++){var pos=G(b.xz.x+8,b.xz.y+i*.7f);Box("HouseholdTimber",root.transform,pos+Vector3.up*.25f,new Vector3(2.2f,.4f,.5f),wood);}
   }
   var bench=G(2698,2197);Box("SharedWorkBench",root.transform,bench+Vector3.up*.74f,new Vector3(2.8f,.16f,1.2f),wood);for(int i=-1;i<=1;i+=2)Box("BenchLeg",root.transform,bench+new Vector3(i,.36f,0),new Vector3(.14f,.72f,.8f),wood);
   sheet.FixedPlacements=fixedPlants.ToArray();art.Sheet=sheet;manifest.Art=sheet;art.Invalidate();
   // Remove only the matching old physical vegetation colliders, not shared assets.
   var kept=new HashSet<string>(fixedPlants.Select(p=>p.Id));foreach(var t in roots.SelectMany(x=>x.GetComponentsInChildren<Transform>(true)).Where(t=>t.name.StartsWith("grove_")||t.name.StartsWith("outcrop_")||t.name.StartsWith("path_meshy_")||t.name.StartsWith("inn_meshy_")).ToArray())if(!kept.Contains(t.name)&&t!=null)Object.DestroyImmediate(t.gameObject);
   var ui=roots.SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();var map=ui.MapData;
   var lines=map.Lines.Where(l=>!l.Id.StartsWith("village245")).ToList();lines.Add(new WorldMapLineSpec{Id="village245_main",Kind=WorldMapLineKind.Trail,Points=main.Select(p=>new Vector2(p.x,p.z)).ToArray()});lines.Add(new WorldMapLineSpec{Id="village245_branch",Kind=WorldMapLineKind.Trail,Points=branch.Select(p=>new Vector2(p.x,p.z)).ToArray()});lines.Add(new WorldMapLineSpec{Id="village245_forest",Kind=WorldMapLineKind.Trail,Points=forest.Select(p=>new Vector2(p.x,p.z)).ToArray()});map.Lines=lines.ToArray();
   map.Markers=map.Markers.Where(m=>m.Id!="village"&&m.Id!="relay").Concat(new[]{new WorldMapMarkerSpec{Id="relay",Label="길목 역참",Kind=WorldMapMarkerKind.Place,WorldXZ=new Vector2(2840,2390)},new WorldMapMarkerSpec{Id="village",Label="청림 벌목마을",Kind=WorldMapMarkerKind.Rest,WorldXZ=new Vector2(2700,2180),CompletionId="village_rest"}}).ToArray();
   foreach(var stage in campaign.Stages){var point=points.FirstOrDefault(p=>p.Id==stage.TriggerId);if(point!=null)stage.Destination=point.Position;}
   foreach(var obj in new Object[]{s,s.Content,catalog,campaign,layout,manifest,art,sheet,map,rest,wood,stone,soil})EditorUtility.SetDirty(obj);
   ConfigurePace258(s);
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   File.WriteAllText(VillageOutput+"/ledger.json",JsonUtility.ToJson(ledger,true));File.WriteAllText(Output+"/layout.json",JsonUtility.ToJson(layout,true));
   File.WriteAllText(Output+"/art_placements.json",JsonUtility.ToJson(sheet,true));File.Copy(Output+"/art_placements.json",Output+"/Cartography/placements.json",true);
   File.WriteAllText(VillageOutput+"/build.txt","Route "+ledger.length+" m / "+(ledger.length/4.5f)+" seconds; seven buildings, two approaches; equipment catalog enabled; NavMesh and Play pending");
   return File.ReadAllText(VillageOutput+"/build.txt");
  }
 }
}
