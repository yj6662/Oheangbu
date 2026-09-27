using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.Prologue;
using Oheangbu.Data.World;
using Oheangbu.Data.Demo;
using Oheangbu.Combat;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string Root263="RootCaveAndSinmok263";
  static string BuildWorld263()
  {
   var scene=FrontageScene249();if(scene.isDirty)throw new Exception("Clean candidate required");var s=VillageSession();var roots=scene.GetRootGameObjects();
   var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single();var layout=manifest.Layout;
   var ui=roots.SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();var map=ui.MapData;var cat=roots.SelectMany(g=>g.GetComponentsInChildren<WorldLocationArrival>(true)).Single().Catalog;
   var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)).Single();var ground=FinalSurface(scene);
   var old=roots.FirstOrDefault(g=>g.name==Root263);if(old!=null)Object.DestroyImmediate(old);roots=roots.Where(g=>g!=null).ToArray();var root=new GameObject(Root263);
   var rock=Asset263("CaveRock",()=>new Material(roots.SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).First(r=>r.name=="Natural_Cave_Interior").sharedMaterial));
   var timber=art.Sheet.Prototypes.First(p=>p.Id.Contains("WoodLog")).Lods[0].Parts[0].Material;
   var bark=Asset263("TreeBark",()=>new Material(timber));if(bark.HasProperty("_BaseColor"))bark.SetColor("_BaseColor",new Color(.78f,.73f,.63f));if(bark.HasProperty("_AmbientFloor"))bark.SetFloat("_AmbientFloor",.62f);if(bark.HasProperty("_WindAmplitude"))bark.SetFloat("_WindAmplitude",0);EditorUtility.SetDirty(bark);
   var soil=AssetDatabase.LoadAssetAtPath<Material>(Path.GetDirectoryName(scene.path).Replace('\\','/')+"/Branches259/Footpath.asset");
   var plan=JsonUtility.FromJson<ProgressionPlan251>(File.ReadAllText(Output263+"/route-proposals.json"));var paths=new Dictionary<string,Vector3[]>();
   foreach(var route in plan.routes){var points=new List<Vector3>();for(int i=1;i<route.points.Length;i++){int n=Mathf.CeilToInt(Vector2.Distance(route.points[i-1],route.points[i])/1.5f);for(int j=0;j<n;j++){var p=Vector2.Lerp(route.points[i-1],route.points[i],j/(float)n);points.Add(ground(p.x,p.y).point);}}var end=route.points.Last();points.Add(ground(end.x,end.y).point);paths[route.id]=points.ToArray();Ribbon263(root.transform,route.id,points.ToArray(),soil,ground,2.4f);}
   Vector2[] caveKnots={new Vector2(3550,2850),new Vector2(3558,2861),new Vector2(3558,2875),new Vector2(3546,2885),new Vector2(3532,2888),new Vector2(3524,2898),new Vector2(3512,2908),new Vector2(3500,2910)};
   var cave=new List<Vector3>();for(int i=1;i<caveKnots.Length;i++){int steps=Mathf.CeilToInt(Vector2.Distance(caveKnots[i-1],caveKnots[i])/1.4f);for(int j=0;j<steps;j++){var p=Vector2.Lerp(caveKnots[i-1],caveKnots[i],j/(float)steps);cave.Add(ground(p.x,p.y).point);}}cave.Add(ground(3500,2910).point);var floor=cave.ToArray();paths["root_gallery263"]=floor;
   var caveRoot=new GameObject("RootGallery").transform;caveRoot.SetParent(root.transform,false);
   var vertices=new List<Vector3>();var triangles=new List<int>();var uv=new List<Vector2>();var left=new List<Vector2>();var right=new List<Vector2>();
   for(int i=0;i<floor.Length;i++){
    var tangent=(floor[Math.Min(i+1,floor.Length-1)]-floor[Math.Max(0,i-1)]);tangent.y=0;var side=Vector3.Cross(tangent.normalized,Vector3.up);
    float along=i/(float)(floor.Length-1),width=3.2f+3.4f*Mathf.Exp(-Mathf.Pow((along-.45f)*9,2));
    left.Add(new Vector2(floor[i].x+side.x*width,floor[i].z+side.z*width));right.Add(new Vector2(floor[i].x-side.x*width,floor[i].z-side.z*width));
    for(int layer=0;layer<2;layer++)for(int j=0;j<=16;j++){
     float a=j/16f*Mathf.PI;float irregular=.28f*Mathf.Sin(i*.59f+j*1.31f)+.15f*Mathf.Sin(i*.21f-j*2.1f);
     float radius=width+irregular+(layer==1?4.6f+1.1f*Mathf.Sin(i*.15f+j*.6f):0);var p=floor[i]+side*(Mathf.Cos(a)*radius);
     float h=4.9f+1.2f*Mathf.Sin(along*8)+.22f*Mathf.Sin(i*.6f+j*1.7f)+(layer==1?3.5f+1.1f*Mathf.Sin(i*.24f+j*.7f):0);
     p.y=ground(p.x,p.z).point.y+Mathf.Sin(a)*h-.2f;vertices.Add(p);uv.Add(new Vector2(i*.23f,j*.2f));
     if(i>0&&j>0){int n=i*34+layer*17+j;int[] tri={n-35,n-1,n,n-35,n,n-34};if(layer==1)Array.Reverse(tri);triangles.AddRange(tri);}}
   }
   // Connect inner and outer rings at both mouths; no plane closes either entrance.
   foreach(int i in new[]{0,floor.Length-1})for(int j=0;j<16;j++){int a=i*34+j;triangles.AddRange(new[]{a,a+1,a+18,a,a+18,a+17});}
   var wallMesh=Asset263("RootGalleryShell",()=>new Mesh());wallMesh.Clear();wallMesh.SetVertices(vertices);wallMesh.SetUVs(0,uv);wallMesh.SetTriangles(triangles,0);wallMesh.RecalculateNormals();wallMesh.RecalculateTangents();wallMesh.RecalculateBounds();EditorUtility.SetDirty(wallMesh);
   var shell=new GameObject("RockShell",typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider));shell.transform.SetParent(caveRoot,false);shell.GetComponent<MeshFilter>().sharedMesh=wallMesh;shell.GetComponent<MeshRenderer>().sharedMaterial=rock;shell.GetComponent<MeshCollider>().sharedMesh=wallMesh;
   Ribbon263(caveRoot,"RootCaveFloor",floor,soil,ground,4.8f);
   // Root ribs descend from the ceiling; portal timbers and weathered logs belong to the working site.
   for(int i=7;i<floor.Length-6;i+=8){var p=floor[i];var points=new[]{p+new Vector3(-2.8f,1.2f,0),p+new Vector3(-2,3.5f,.4f),p+new Vector3(0,4.5f,.2f),p+new Vector3(2.4f,3,0)};
    MeshObject263(caveRoot,"CeilingRoot"+i,Tube263("CeilingRoot"+i,points,new[]{.2f,.33f,.4f,.16f},i),bark,false);}
   var chamber=floor[(int)(floor.Length*.45f)];
   MeshObject263(caveRoot,"SplitPillar",Tube263("SplitPillar",new[]{chamber,chamber+new Vector3(.1f,2,0),chamber+new Vector3(.3f,5,.2f)},new[]{.95f,1.1f,.65f},13),rock,true);
   var bagSource=roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).First(t=>t.name=="Worker_Straw_Bag");
   var clue=Object.Instantiate(bagSource.gameObject,caveRoot);clue.name="root_evidence263";foreach(var b in clue.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(b);
   var clueFoot=ground(chamber.x-3.1f,chamber.z).point;clue.transform.position=clueFoot;clue.transform.localScale=bagSource.lossyScale;
   var clueRs=clue.GetComponentsInChildren<Renderer>();clue.transform.position+=Vector3.up*(clueFoot.y-clueRs.Min(r=>r.bounds.min.y)+.04f);clue.AddComponent<WorldMacroContentPoint>().Id=clue.name;
   for(int i=0;i<5;i++){var p=floor[7+i*(floor.Length-15)/5];MeshObject263(caveRoot,"BrokenTimber"+i,Tube263("BrokenTimber"+i,new[]{p+new Vector3(2.4f,.12f,-1),p+new Vector3(2.4f,.13f,1.2f)},new[]{.12f,.09f},i,10,8),timber,false);
    var light=new GameObject("WorkLamp"+i).AddComponent<Light>();light.transform.SetParent(caveRoot,false);light.transform.position=p+new Vector3(2,2.7f,0);light.type=LightType.Point;light.range=8;light.intensity=.65f;light.color=new Color(1,.75f,.45f);}
   foreach(var template in roots.SelectMany(g=>g.GetComponentsInChildren<CompactCaveAmbience>(true)).Take(2)){
    var ambience=Object.Instantiate(template,caveRoot);ambience.name="RootCave_"+template.name;ambience.ZoneId="root_cave";ambience.transform.position=floor[template.Intermittent?floor.Length/2:floor.Length-10]+Vector3.up*1.5f;ambience.Radius=42;}
   var polygon=left.Concat(right.AsEnumerable().Reverse()).ToArray();var min=new Vector2(polygon.Min(p=>p.x)-5,polygon.Min(p=>p.y)-5);var max=new Vector2(polygon.Max(p=>p.x)+5,polygon.Max(p=>p.y)+5);
   var illustration=CaveMap263(polygon,floor,min,max);
   map.Zones=map.Zones.Where(z=>z.Id!="root_cave").Concat(new[]{new WorldMapZoneSpec{Id="root_cave",Label="청림 · 뿌리 굴",Polygon=polygon,FloorPath=floor,MinimumY=floor.Min(p=>p.y)-1,MaximumY=floor.Max(p=>p.y)+3,FloorClearance=3,ExploreWalkedPassages=true,DiscoveryRevision="root263",Illustration=illustration,IllustrationWorldUv=new Rect(min.x/4000,min.y/6000,(max.x-min.x)/4000,(max.y-min.y)/6000),DetailPath=floor.Select(p=>new Vector2(p.x,p.z)).ToArray()}}).ToArray();
   var treeFoot=ground(3710,3255).point;var actor=Tree263(root.transform,treeFoot,s,bark,ground);
   s.Actors=s.Actors.Where(a=>a!=null&&a.Id!=actor.Id).Concat(new[]{actor}).ToArray();
   s.Content.Encounters=s.Content.Encounters.Where(e=>e.Id!=actor.Id).Concat(new[]{new WorldMacroPlaytestSO.Encounter{Id=actor.Id,ContentId=actor.Id,Feet=treeFoot,Patrol=new[]{treeFoot},Speed=0,Activation=700,Detection=23,Leash=35,RespawnOnRest=false}}).ToArray();
   var wiring=new SerializedObject(s.Walker.Wiring);var enemies=wiring.FindProperty("_enemies");enemies.arraySize=s.Actors.Length;for(int i=0;i<s.Actors.Length;i++)enemies.GetArrayElementAtIndex(i).objectReferenceValue=s.Actors[i].GetComponent<EnemyVitals>();wiring.ApplyModifiedPropertiesWithoutUndo();
   var restFoot=ground(3670,3230).point;var restMesh=Tube263("RestStone",new[]{restFoot,restFoot+Vector3.up*.6f,restFoot+new Vector3(.1f,1,0)},new[]{.55f,.5f,.1f},31,14,8);var rest=MeshObject263(root.transform,"sinmok_rest263",restMesh,rock,true);rest.AddComponent<WorldMacroContentPoint>().Id=rest.name;
   s.Content.Points=s.Content.Points.Where(p=>p.Id!="root_evidence263"&&p.Id!="sinmok_rest263").Concat(new[]{new PrologueContentSO.Point{Id="root_evidence263",Kind=PrologueInteractionKind.Evidence,Position=clueFoot,Radius=2.5f,Text="뿌리에 감긴 쇠사슬. 바깥은 새 줄기에 먹혔지만 쇠가 닿은 자리는 마른 채다. 보따리 속 장부에는 베어 낸 나무 수 대신 돌아오지 않은 사람의 이름이 적혀 있다."},new PrologueContentSO.Point{Id="sinmok_rest263",Kind=PrologueInteractionKind.Rest,Position=restFoot,Radius=2.5f,Text=""}}).ToArray();
   s.InteractionVisuals=s.InteractionVisuals.Where(v=>v.Id!="root_evidence263"&&v.Id!="sinmok_rest263").Concat(new[]{new WorldMacroPlaytestSession.InteractionVisual{Id="root_evidence263",Renderers=clueRs},new WorldMacroPlaytestSession.InteractionVisual{Id="sinmok_rest263",Renderers=rest.GetComponentsInChildren<Renderer>()}}).ToArray();
   s.Content.Checkpoints=s.Content.Checkpoints.Where(c=>c.Id!="sinmok_rest263").Concat(new[]{new WorldMacroPlaytestSO.CheckpointSpec{Id="sinmok_rest263",Label="신목 앞 돌무더기",Feet=ground(restFoot.x-2,restFoot.z).point,Yaw=60}}).ToArray();
   var campaign=s.Content.Campaign;campaign.Stages=campaign.Stages.Where(x=>x.Id!="sinmok263"&&x.Id!="root_evidence263").Concat(new[]{new DemoCampaignProfile.Stage{Id="root_evidence263",TriggerId="root_evidence263",Objective="뿌리 굴의 장부",Event=DemoEventKind.Interaction,Implemented=true,Optional=true,TongboReward=60,GrantedFacts=new[]{"evidence:root_ledger263"},Destination=clueFoot,DestinationId="root_cave"},new DemoCampaignProfile.Stage{Id="sinmok263",TriggerId="sinmok263",Objective="신목",Event=DemoEventKind.BossDefeated,Implemented=true,Optional=true,TongboReward=260,GrantedFacts=new[]{"defeated:sinmok263"},Destination=treeFoot,DestinationId="old_tree"}}).ToArray();
   if(!campaign.IsValid)throw new Exception("Invalid campaign263");
   foreach(var route in plan.routes){layout.Routes=layout.Routes.Where(r=>!(r.From==route.fromId&&r.To==route.toId)).Concat(new[]{new CompactWorldLayoutSO.Route{Id=route.id,From=route.fromId,To=route.toId,Role=CompactRouteRole.Exploration,Width=2.4f,Bends=route.points.Skip(1).Take(route.points.Length-2).ToArray()}}).ToArray();}
   var rootPlace=layout.Places.Single(p=>p.Id=="root_cave");rootPlace.HasSourceBinding=true;rootPlace.SceneRoots=new[]{Root263+"/RootGallery"};rootPlace.InteractionIds=new[]{"root_evidence263"};
   var treePlace=layout.Places.Single(p=>p.Id=="old_tree");treePlace.XZ=new Vector2(treeFoot.x,treeFoot.z);treePlace.HasSourceBinding=true;treePlace.SceneRoots=new[]{Root263+"/"+actor.Id};treePlace.EncounterIds=new[]{actor.Id};treePlace.CheckpointIds=new[]{"sinmok_rest263"};treePlace.Purpose="청룡 전에 만나는 신목 주요 조우";
   map.Lines=map.Lines.Where(l=>!paths.ContainsKey(l.Id)).Concat(paths.Select(p=>new WorldMapLineSpec{Id=p.Key,Kind=WorldMapLineKind.Trail,Points=p.Value.Select(v=>new Vector2(v.x,v.z)).ToArray()})).ToArray();
   cat.Entries=cat.Entries.Where(e=>e.Id!="root_cave"&&e.Id!="old_tree").Concat(new[]{new WorldLocationCatalog.Entry{Id="root_cave",Name="뿌리 굴",RealmId="realm_cheongrim",MarkerId="root_cave",Centre=floor[0],Polygon=polygon,FloorPath=floor,Priority=40},new WorldLocationCatalog.Entry{Id="old_tree",Name="신목",RealmId="realm_cheongrim",MarkerId="old_tree",Centre=treeFoot,Radius=32,MinimumY=treeFoot.y-15,MaximumY=treeFoot.y+20,Priority=20}}).ToArray();
   map.Markers=map.Markers.Where(m=>m.Id!="root_cave"&&m.Id!="old_tree").Concat(cat.Entries.Where(e=>e.Id=="root_cave"||e.Id=="old_tree").Select(e=>new WorldMapMarkerSpec{Id=e.Id,Label=cat.DisplayName(e),WorldXZ=new Vector2(e.Centre.x,e.Centre.z),Kind=WorldMapMarkerKind.Place,RequiresArrival=true,CompletionId=e.Id=="old_tree"?"sinmok263":null})).ToArray();
   // Preserve the last art sheet as a source; remove only vegetation crossing new corridors/arena.
   string sourcePath=Folder263+"/PlacementsSource.asset";var source=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(sourcePath);if(source==null){source=Object.Instantiate(art.Sheet);AssetDatabase.CreateAsset(source,sourcePath);}
   bool Clear(Vector3 p)=>paths.Any(path=>FlatPathDistance(p,path.Value)<(path.Key=="root_gallery263"?10:2.3f))||Vector2.Distance(new Vector2(p.x,p.z),treePlace.XZ)<28;
   var removed=new HashSet<string>(source.FixedPlacements.Where(p=>Clear(p.Position)).Select(p=>p.Id));var sheet=Asset263("Placements",()=>Object.Instantiate(source));sheet.FixedPlacements=source.FixedPlacements.Where(p=>!removed.Contains(p.Id)).ToArray();
   Dress263(root.transform,actor,art.Sheet,sheet,paths,floor,ground,bark);
   art.Sheet=sheet;manifest.Art=sheet;art.Invalidate();
   foreach(var t in roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>removed.Contains(t.name)).ToArray())if(t!=null)t.gameObject.SetActive(false);
   foreach(var o in new Object[]{s,s.Content,campaign,layout,map,cat,art,sheet,manifest})EditorUtility.SetDirty(o);
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   File.WriteAllText(Output263+"/routes.json",JsonUtility.ToJson(new Routes263{routes=paths.Select(p=>new Route263{id=p.Key,points=p.Value}).ToArray()},true));
   File.WriteAllText(Output+"/layout.json",JsonUtility.ToJson(layout,true));File.WriteAllText(Output+"/Cartography/map.json",JsonUtility.ToJson(map,true));File.WriteAllText(Output+"/art_placements.json",JsonUtility.ToJson(sheet,true));File.WriteAllText(Output+"/Cartography/placements.json",JsonUtility.ToJson(sheet,true));
   File.WriteAllText(Output263+"/build.txt","Root gallery and two open mouths; physical sloping floor retained; multipart skinned Sinmok + four timed attacks + permanent defeat + TEST260 reward; root evidence TEST60. Navigation and integration checks pending.");return File.ReadAllText(Output263+"/build.txt");
  }
  [Serializable]class Route263{public string id;public Vector3[] points;}
  [Serializable]class Routes263{public Route263[] routes;}
  static GameObject MeshObject263(Transform parent,string name,Mesh mesh,Material mat,bool collision){var g=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));g.transform.SetParent(parent,false);g.GetComponent<MeshFilter>().sharedMesh=mesh;g.GetComponent<MeshRenderer>().sharedMaterial=mat;if(collision)g.AddComponent<MeshCollider>().sharedMesh=mesh;return g;}
  static void Ribbon263(Transform parent,string name,Vector3[] path,Material material,Func<float,float,RaycastHit> ground,float width)
  {
   var v=new List<Vector3>();var uv=new List<Vector2>();var c=new List<Color>();var tri=new List<int>();float distance=0;
   for(int i=0;i<path.Length;i++){if(i>0)distance+=Vector3.Distance(path[i-1],path[i]);var d=path[Math.Min(i+1,path.Length-1)]-path[Math.Max(0,i-1)];d.y=0;var side=Vector3.Cross(d.normalized,Vector3.up);
    for(int j=0;j<3;j++){var p=path[i]+side*(j-1)*width*.5f;v.Add(ground(p.x,p.z).point+Vector3.up*.025f);uv.Add(new Vector2(j*.8f,distance));c.Add(new Color(1,1,1,j==1?1:0));if(i>0&&j<2){int n=i*3+j;tri.AddRange(new[]{n-3,n,n+1,n-3,n+1,n-2});}}}
   var mesh=Asset263(name,()=>new Mesh());mesh.Clear();mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetColors(c);mesh.SetTriangles(tri,0);UpwardRibbon258(mesh);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);MeshObject263(parent,name,mesh,material,false);
  }
  static Texture2D CaveMap263(Vector2[] polygon,Vector3[] path,Vector2 min,Vector2 max)
  {
   int w=768,h=768;var tex=new Texture2D(w,h,TextureFormat.RGB24,false);var pixels=new Color[w*h];for(int y=0;y<h;y++)for(int x=0;x<w;x++){
    var p=min+Vector2.Scale(new Vector2(x/(float)(w-1),y/(float)(h-1)),max-min);bool inside=WorldMapDiscoveryGrid.Contains(polygon,p);float n=Mathf.PerlinNoise(x*.06f,y*.06f);
    pixels[y*w+x]=inside?Color.Lerp(new Color(.33f,.31f,.25f),new Color(.58f,.54f,.43f),n):Color.Lerp(new Color(.075f,.09f,.08f),new Color(.15f,.17f,.14f),n);}
   tex.SetPixels(pixels);tex.Apply();string file=Folder263+"/RootCaveMap.png";File.WriteAllBytes(file,tex.EncodeToPNG());Object.DestroyImmediate(tex);AssetDatabase.ImportAsset(file);var importer=(TextureImporter)AssetImporter.GetAtPath(file);importer.mipmapEnabled=false;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(file);
  }
 }
}
