using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  sealed class MountainField290
  {
   public CompactWorldLayoutSO.Mountain Mountain;public Rect Bounds;public Quaternion Rotation;
   public float[,] Original;public TerrainData HeightData;public Vector3[] Knots;
   public Vector3 Scale=Vector3.one;
   public Vector3 World(Vector3 p)=>Mountain.Foot+Rotation*Vector3.Scale(p,Scale);
   public Vector3 Local(Vector3 p){var q=Quaternion.Inverse(Rotation)*(p-Mountain.Foot);return new Vector3(q.x/Scale.x,q.y/Scale.y,q.z/Scale.z);}
   public float Base(float x,float z)
   {
    float u=Mathf.Clamp01((x-Bounds.xMin)/Bounds.width)*256,v=Mathf.Clamp01((z-Bounds.yMin)/Bounds.height)*256;
    int a=Mathf.Min(255,(int)u),b=Mathf.Min(255,(int)v);
    return Mathf.Lerp(Mathf.Lerp(Original[b,a],Original[b,a+1],u-a),Mathf.Lerp(Original[b+1,a],Original[b+1,a+1],u-a),v-b);
   }
   public float Sample(float x,float z)=>HeightData.GetInterpolatedHeight((x-Bounds.xMin)/Bounds.width,(z-Bounds.yMin)/Bounds.height)-200;
  }
  static T Asset290<T>(string name,Func<T> create) where T:Object
  {
   string path=A290+"/"+name;DevSceneKit.EnsureFolder(Path.GetDirectoryName(path).Replace('\\','/'));
   var value=AssetDatabase.LoadAssetAtPath<T>(path);if(value==null){value=create();AssetDatabase.CreateAsset(value,path);}return value;
  }
  static Vector3[] Resample290(Vector3[] knots,float spacing)
  {
   var points=new List<Vector3>{knots[0]};
   for(int k=1;k<knots.Length;k++){int n=Mathf.CeilToInt(Vector3.Distance(knots[k-1],knots[k])/spacing);for(int i=1;i<=n;i++)points.Add(Vector3.Lerp(knots[k-1],knots[k],i/(float)n));}
   return points.ToArray();
  }
  static float Distance290(Vector2 p,Vector3[] path,out float y)=>Distance290(p,path,out y,out _);
  static float Distance290(Vector2 p,Vector3[] path,out float y,out Vector2 closest)
  {
   float best=float.MaxValue;y=0;closest=Vector2.zero;
   for(int i=1;i<path.Length;i++)
   {
    Vector2 a=new Vector2(path[i-1].x,path[i-1].z),d=new Vector2(path[i].x,path[i].z)-a;
    float t=Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(.00001f,d.sqrMagnitude)),dist=(p-a-d*t).sqrMagnitude;
    if(dist<best){best=dist;y=Mathf.Lerp(path[i-1].y,path[i].y,t);closest=a+d*t;}
   }
   return Mathf.Sqrt(best);
  }
  static string BuildGeometry290()
  {
   var scene=SceneManager.GetActiveScene();if(scene.path!=Scene290)throw new Exception("Open the independent mountain candidate");
   var roots=scene.GetRootGameObjects();var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single();
   var session=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();var layout=manifest.Layout;
   // Always sample the preserved old meshes, so reassembly never adds relief twice.
   var oldTerrain=roots.Single(g=>g.name=="Compact_Rebuild_Terrain");
   foreach(var collider in oldTerrain.GetComponentsInChildren<MeshCollider>(true)){collider.enabled=true;collider.gameObject.SetActive(true);}
   var oldArt=GameObject.Find("Cheongrim_AssetPass_290");if(oldArt!=null)Object.DestroyImmediate(oldArt);
   var previous=GameObject.Find("Compact_Mountains_290");if(previous!=null)Object.DestroyImmediate(previous);
   Physics.SyncTransforms();var ground=FinalSurface(scene);
   var root=new GameObject("Compact_Mountains_290");var access=root.AddComponent<CompactMountainAccess>();access.Layout=layout;session.MountainLayout=layout;
   var entries=new[]{"herb_path","jeokro","cheolong","hyeongang","capital_center"};
   var names=new[]{"금표 암릉","재의 봉우리","서벽 능선","운무 고개","인왕산"};
   var realms=new[]{"cheongrim","jeokro","cheolong","hyeongang","hwanggyeong"};
   var feet=new[]{new Vector2(3030,2800),new Vector2(1780,900),new Vector2(760,2920),new Vector2(1930,4820),new Vector2(1600,3450)};
   var bounds=new[]{new Rect(2500,2500,1000,1000),new Rect(1000,500,1000,1000),new Rect(0,2500,1000,1500),new Rect(1000,4500,1500,1500),new Rect(1000,3000,1000,1500)};
   var fields=new List<MountainField290>();
   for(int k=0;k<5;k++)
   {
    var m=new CompactWorldLayoutSO.Mountain{Id="mountain_"+realms[k],Realm=realms[k],Label=names[k],EntryPlaceId=entries[k],MainStory=k==1||k==2};
    var field=new MountainField290{Mountain=m,Bounds=bounds[k],Rotation=Quaternion.Euler(0,k==1?90:0,0),Original=new float[257,257],Scale=new[]{new Vector3(.94f,1,.94f),new Vector3(.88f,1.05f,.88f),new Vector3(.93f,.98f,.93f),new Vector3(.84f,.90f,.84f),new Vector3(.92f,1.02f,.92f)}[k]};
    for(int z=0;z<=256;z++)for(int x=0;x<=256;x++)field.Original[z,x]=ground(bounds[k].xMin+bounds[k].width*x/256f,bounds[k].yMin+bounds[k].height*z/256f).point.y;
    m.Foot=ground(feet[k].x,feet[k].y).point;
    var local=new[]{new Vector3(0,0,0),new Vector3(25,18,70),new Vector3(-30,35,140),new Vector3(-120,48,190),
     new Vector3(-215,55,230),new Vector3(-245,61,310),new Vector3(-160,68,340),new Vector3(-105,70,194),
     new Vector3(-45,100,350),new Vector3(-95,128,445),new Vector3(-195,145,490),new Vector3(-235,168,580)};
    field.Knots=local.Select(field.World).ToArray();m.MainPath=Resample290(field.Knots,.75f);m.Summit=m.MainPath.Last();
    m.Temple=field.World(new Vector3(-370,67,345));
    m.TemplePath=Resample290(new[]{field.Knots[5],field.World(new Vector3(-290,65,335)),m.Temple},.75f);
    m.ReturnPath=Resample290(new[]{m.Temple,field.World(new Vector3(-350,65,280)),field.Knots[4]},.75f);
    void Terrace(Vector3[] path,Vector3 centre,float radius){for(int i=0;i<path.Length;i++){float distance=Vector2.Distance(new Vector2(path[i].x,path[i].z),new Vector2(centre.x,centre.z));float blend=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(radius,radius+(radius<40?55:24),distance));path[i].y=Mathf.Lerp(path[i].y,centre.y,blend);}}
    Terrace(m.MainPath,m.Summit,29);Terrace(m.TemplePath,m.Temple,58);Terrace(m.ReturnPath,m.Temple,58);
    m.LiftBase=field.World(new Vector3(-113,48,194));m.LiftLanding=field.World(new Vector3(-111.95f,70,194));
    SmoothTurn290(m);
    m.SummitRewardId=m.Id+"_summit";m.TempleRewardId=m.Id+"_temple";
    var volumes=new List<CompactWorldLayoutSO.VehicleExclusion>();
    foreach(var path in new[]{m.MainPath,m.TemplePath,m.ReturnPath})for(int i=12;i<path.Length;i+=12)
     volumes.Add(new CompactWorldLayoutSO.VehicleExclusion{Centre=path[i]+Vector3.up*10,Size=new Vector3(22,26,22)});
    volumes.Add(new CompactWorldLayoutSO.VehicleExclusion{Centre=m.Temple+Vector3.up*25,Size=new Vector3(130,60,130)});
    volumes.Add(new CompactWorldLayoutSO.VehicleExclusion{Centre=m.Summit+Vector3.up*25,Size=new Vector3(90,60,90)});
    m.VehicleExclusions=volumes.ToArray();fields.Add(field);
   }
   layout.Mountains=fields.Select(f=>f.Mountain).ToArray();
   foreach(var ridge in layout.Ridges)ridge.Kind=CompactMountainKind.Rounded;
   var summaries=new List<string>();
   foreach(var field in fields)
   {
    var m=field.Mountain;var mountainRoot=new GameObject(m.Id);mountainRoot.transform.SetParent(root.transform,false);
    BuildHeight290(field,layout);BuildTerrainMesh290(field,mountainRoot.transform);
    var rock=MountainMaterial290(m.Realm,"Granite");var pathMaterial=MountainMaterial290(m.Realm,"Worn_steps");
    PathMesh290(m.Id+"_main",m.MainPath,2.6f,pathMaterial,mountainRoot.transform);
    PathMesh290(m.Id+"_hidden",m.TemplePath,2.0f,pathMaterial,mountainRoot.transform);
    PathMesh290(m.Id+"_return",m.ReturnPath,2.0f,pathMaterial,mountainRoot.transform);
    PathMesh290(m.Id+"_lift_approach",Resample290(new[]{field.Knots[3],m.LiftBase},.4f),2,rock,mountainRoot.transform);
    BuildCliffBands290(field,mountainRoot.transform,rock);
    BuildLift290(field,mountainRoot.transform,rock);
    summaries.Add(m.Id+" main="+Length290(m.MainPath).ToString("F1")+"m rise="+(m.Summit.y-m.Foot.y).ToString("F1")+"m; temple branch="+Length290(m.TemplePath).ToString("F1")+"m; lift="+(m.LiftLanding.y-m.LiftBase.y).ToString("F1")+"m");
   }
   foreach(var collider in oldTerrain.GetComponentsInChildren<MeshCollider>(true))
   {
    var c=collider.bounds.center;
    if(fields.Any(f=>f.Bounds.Contains(new Vector2(c.x,c.z))))collider.gameObject.SetActive(false);
   }
   // Authoritative POI and route records feed downstream maps and navigation.
   var places=layout.Places.Where(p=>!p.Id.StartsWith("mountain_")).ToList();var routes=layout.Routes.Where(p=>!p.Id.StartsWith("mountain_")).ToList();
   foreach(var m in layout.Mountains)
   {
    var junction=m.TemplePath[0];var reunion=m.ReturnPath.Last();
    foreach(var item in new[]{(m.Id+"_foot",m.Foot,"산길 입구"),(m.Id+"_summit",m.Summit,m.Label),(m.Id+"_temple",m.Temple,"숨은 절"),(m.Id+"_junction",junction,"절 갈림길"),(m.Id+"_reunion",reunion,"복귀길 합류")})
     places.Add(new CompactWorldLayoutSO.Place{Id=item.Item1,Realm=m.Realm,Label=item.Item3,XZ=new Vector2(item.Item2.x,item.Item2.z),Purpose=m.MainStory?"본선 산행":"선택 산행",GroundRadius=6});
    void Route(string id,string from,string to,Vector3[] path,CompactRouteRole role)=>routes.Add(new CompactWorldLayoutSO.Route{Id=id,From=from,To=to,Role=role,Traversal=CompactTraversal.FootOnly,Width=2.6f,Bends=path.Where((_,i)=>i%16==0||i==path.Length-1).Select(p=>new Vector2(p.x,p.z)).ToArray()});
    Route(m.Id+"_approach",m.EntryPlaceId,m.Id+"_foot",new[]{m.Foot},CompactRouteRole.Exploration);
    int ji=Array.FindIndex(m.MainPath,p=>Vector3.Distance(p,junction)<.01f),ri=Array.FindIndex(m.MainPath,p=>Vector3.Distance(p,reunion)<.01f);
    Route(m.Id+"_lower",m.Id+"_foot",m.Id+"_reunion",m.MainPath.Take(ri+1).ToArray(),m.MainStory?CompactRouteRole.Main:CompactRouteRole.Exploration);
    Route(m.Id+"_middle",m.Id+"_reunion",m.Id+"_junction",m.MainPath.Skip(ri).Take(ji-ri+1).ToArray(),m.MainStory?CompactRouteRole.Main:CompactRouteRole.Exploration);
    Route(m.Id+"_upper",m.Id+"_junction",m.Id+"_summit",m.MainPath.Skip(ji).ToArray(),m.MainStory?CompactRouteRole.Main:CompactRouteRole.Exploration);
    Route(m.Id+"_branch",m.Id+"_junction",m.Id+"_temple",m.TemplePath,CompactRouteRole.Exploration);
    Route(m.Id+"_loop",m.Id+"_temple",m.Id+"_reunion",m.ReturnPath,CompactRouteRole.ReturnShortcut);
   }
   layout.Places=places.ToArray();layout.Routes=routes.ToArray();EditorUtility.SetDirty(layout);EditorUtility.SetDirty(session);
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   File.WriteAllText(O290+"/layout.json",JsonUtility.ToJson(layout,true));File.WriteAllText(O290+"/geometry.txt",string.Join("\n",summaries)+"\nGeometry stage only; gameplay/navigation/art review pending.");
   return File.ReadAllText(O290+"/geometry.txt");
  }

  static void SmoothTurn290(CompactWorldLayoutSO.Mountain mountain)
  {
   if(mountain.RouteRevision>=1)return;
   var path=mountain.MainPath;int corner=Enumerable.Range(1,path.Length-2).OrderBy(i=>Vector2.Distance(new Vector2(path[i].x,path[i].z),new Vector2(mountain.LiftLanding.x,mountain.LiftLanding.z))).First();
   int start=Mathf.Max(1,corner-14),end=Mathf.Min(path.Length-2,corner+14);var a=path[start];var c=path[corner];var b=path[end];
   for(int i=start;i<=end;i++){float t=(i-start)/(float)(end-start);path[i]=(1-t)*(1-t)*a+2*(1-t)*t*c+t*t*b;}
   mountain.RouteRevision=1;
  }
  static string RepairTurns290()
  {
   var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();if(s.gameObject.scene.path!=Scene290)throw new Exception("Candidate only");
   foreach(var m in s.MountainLayout.Mountains){SmoothTurn290(m);var root=GameObject.Find(m.Id).transform;var old=root.Find(m.Id+"_main");if(old!=null)Object.DestroyImmediate(old.gameObject);
    PathMesh290(m.Id+"_main",m.MainPath,2.6f,MountainMaterial290(m.Realm,"Worn_steps"),root);
    var data=AssetDatabase.LoadAssetAtPath<TerrainData>(A290+"/"+m.Id+"/Height.asset");var terrain=root.GetComponentsInChildren<MeshCollider>().Single(c=>c.name.StartsWith("Terrain_"));var bounds=terrain.bounds;
    var f=new MountainField290{Mountain=m,HeightData=data,Bounds=new Rect(bounds.min.x,bounds.min.z,bounds.size.x,bounds.size.z),Rotation=Quaternion.Euler(0,m.Realm=="jeokro"?90:0,0)};
    foreach(string suffix in new[]{"_inner_rock","_outer_apron"}){var part=root.Find(m.Id+suffix);if(part!=null)Object.DestroyImmediate(part.gameObject);}
    BuildCliffBands290(f,root,MountainMaterial290(m.Realm,"Granite"));
    var shelf=root.GetComponentsInChildren<Transform>().First(t=>t.name==m.Id+"_upper_shelf");var parent=shelf.parent;Object.DestroyImmediate(shelf.gameObject);
    if(parent.Find("PermanentRootBed")==null)Box290("PermanentRootBed",parent,m.LiftBase+Vector3.up*(.035f-.12f),new Vector3(1.6f,.24f,1.6f),MountainMaterial290(m.Realm,"Granite"));
    var join=m.MainPath.Where(p=>Mathf.Abs(p.y-m.LiftLanding.y)<2).OrderBy(p=>(new Vector2(p.x,p.z)-new Vector2(m.LiftLanding.x,m.LiftLanding.z)).sqrMagnitude).First();
    PathMesh290(m.Id+"_upper_shelf",Resample290(new[]{m.LiftLanding,join},.35f),2,MountainMaterial290(m.Realm,"Granite"),parent);

    var route=Array.Find(s.MountainLayout.Routes,r=>r.Id==m.Id+"_upper");int junction=Array.FindIndex(m.MainPath,p=>Vector3.Distance(p,m.TemplePath[0])<.1f);
    route.Bends=m.MainPath.Skip(junction).Where((_,i)=>i%16==0).Append(m.MainPath.Last()).Select(p=>new Vector2(p.x,p.z)).ToArray();
   }
   EditorUtility.SetDirty(s.MountainLayout);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(s.gameObject.scene);EditorSceneManager.SaveScene(s.gameObject.scene);
   File.WriteAllText(O290+"/layout.json",JsonUtility.ToJson(s.MountainLayout,true));return "Rounded five narrow hairpins; full physics/navigation recheck required";
  }
  static float Length290(Vector3[] path){float n=0;for(int i=1;i<path.Length;i++)n+=Vector3.Distance(path[i-1],path[i]);return n;}
  static void BuildHeight290(MountainField290 field,CompactWorldLayoutSO layout)
  {
   var m=field.Mountain;const int res=1025;var heights=new float[res,res];var b=field.Bounds;
   var protectedPlaces=layout.Places.Where(p=>!p.Id.StartsWith("mountain_")&&Vector2.Distance(p.XZ,new Vector2(m.Foot.x,m.Foot.z))>20).ToArray();
   var oldRoads=layout.Routes.Where(r=>!r.Id.StartsWith("mountain_")).Select(r=>{
    var a=layout.Places.FirstOrDefault(p=>p.Id==r.From);var c=layout.Places.FirstOrDefault(p=>p.Id==r.To);
    return (r.Width,Path:(a!=null?new[]{a.XZ}:Array.Empty<Vector2>()).Concat(r.Bends).Concat(c!=null?new[]{c.XZ}:Array.Empty<Vector2>()).Select(p=>new Vector3(p.x,0,p.y)).ToArray());
   }).Where(r=>r.Path.Length>1&&r.Path.Any(p=>b.Contains(new Vector2(p.x,p.z)))).ToArray();
   var paths=new[]{m.MainPath.Where((p,i)=>i%24==0||i%3==0&&Vector3.Distance(p,m.Summit)<100||field.Knots.Any(k=>Mathf.Abs(k.x-p.x)<.01f&&Mathf.Abs(k.z-p.z)<.01f)).Append(m.MainPath.Last()).ToArray(),m.TemplePath.Where((_,i)=>i%4==0).Append(m.TemplePath.Last()).ToArray(),m.ReturnPath.Where((_,i)=>i%4==0).Append(m.ReturnPath.Last()).ToArray(),new[]{field.Knots[3],m.LiftBase}};
   for(int z=0;z<res;z++)for(int x=0;x<res;x++)
   {
    float wx=b.xMin+b.width*x/(res-1),wz=b.yMin+b.height*z/(res-1),original=field.Base(wx,wz);
    var p=field.Local(new Vector3(wx,0,wz));float nx=(p.x+150)/300,nz=(p.z-330)/385;
    float mass=Mathf.Clamp01(1-nx*nx-nz*nz),weight=Mathf.SmoothStep(0,1,mass*3);
    float fract=5*Mathf.Sin(p.x*.061f+p.z*.014f)+3*Mathf.Sin(p.z*.083f)+1.8f*Mathf.Sin(p.x*.16f-p.z*.033f);
    float high=m.Foot.y+185*field.Scale.y*Mathf.Pow(mass,.42f)+fract*mass;
    float h=Mathf.Lerp(original,high,weight);
    float flankDistance=Distance290(new Vector2(wx,wz),field.Knots,out float flankY,out var nearestFlank);
    var summitCentre=field.World(new Vector3(-150,160,330));var inland=(new Vector2(summitCentre.x,summitCentre.z)-nearestFlank).normalized;
    float signed=Vector2.Dot(new Vector2(wx,wz)-nearestFlank,inland);
    float apron=flankY+signed*(signed<0?1.35f:1.8f);
    if(flankDistance<90)h=Mathf.Lerp(apron,h,Mathf.SmoothStep(0,1,Mathf.InverseLerp(30,90,flankDistance)));
    float nearest=float.MaxValue,target=0;
    foreach(var path in paths){float d=Distance290(new Vector2(wx,wz),path,out float y);if(d<nearest){nearest=d;target=y;}}
    if(nearest<12)h=Mathf.Lerp(target-.12f,h,Mathf.SmoothStep(0,1,Mathf.InverseLerp(5f,12,nearest)));
    foreach(var platform in new[]{(m.Summit,29f),(m.Temple,58f)})
    {
     float d=Vector2.Distance(new Vector2(wx,wz),new Vector2(platform.Item1.x,platform.Item1.z));
     h=Mathf.Lerp(platform.Item1.y-.08f,h,Mathf.SmoothStep(0,1,Mathf.InverseLerp(platform.Item2,platform.Item2+12,d)));
    }
    // The authored route already includes terrace easing. Do not apply that easing twice beneath its tread.
    if(nearest<5)h=target-.12f;
    // Keep existing villages, encounters and their immediate surroundings at their actual authored elevation.
    foreach(var place in protectedPlaces){float d=Vector2.Distance(place.XZ,new Vector2(wx,wz));if(d<place.GroundRadius+30)h=Mathf.Lerp(original,h,Mathf.SmoothStep(0,1,Mathf.InverseLerp(place.GroundRadius+10,place.GroundRadius+30,d)));}
    foreach(var road in oldRoads){float d=Distance290(new Vector2(wx,wz),road.Path,out _);if(d<road.Width+12)h=Mathf.Lerp(original,h,Mathf.SmoothStep(0,1,Mathf.InverseLerp(road.Width+2,road.Width+12,d)));}
    float edge=Mathf.Min(Mathf.Min(wx-b.xMin,b.xMax-wx),Mathf.Min(wz-b.yMin,b.yMax-wz));h=Mathf.Lerp(original,h,Mathf.SmoothStep(0,1,edge/35));
    heights[z,x]=Mathf.Clamp01((h+200)/1200);
   }
   var data=Asset290(m.Id+"/Height.asset",()=>new TerrainData());data.heightmapResolution=res;data.size=new Vector3(b.width,1200,b.height);data.SetHeights(0,0,heights);EditorUtility.SetDirty(data);field.HeightData=data;
  }
  static void BuildTerrainMesh290(MountainField290 field,Transform parent)
  {
   var root=new GameObject("Terrain_"+field.Mountain.Id);root.transform.SetParent(parent,false);var lods=new List<LOD>();
   for(int level=0;level<3;level++)
   {
    int n=256>>level,side=n+1;var v=new Vector3[side*side];var uv=new Vector2[v.Length];var tri=new int[n*n*6];int at=0;
    for(int z=0;z<=n;z++)for(int x=0;x<=n;x++){float wx=field.Bounds.xMin+field.Bounds.width*x/n,wz=field.Bounds.yMin+field.Bounds.height*z/n;v[z*side+x]=new Vector3(wx,field.Sample(wx,wz),wz);uv[z*side+x]=new Vector2(wx/4,wz/4);}
    for(int z=0;z<n;z++)for(int x=0;x<n;x++){int a=z*side+x;foreach(int i in new[]{a,a+side,a+1,a+1,a+side,a+side+1})tri[at++]=i;}
    var mesh=Mesh290(field.Mountain.Id+"_terrain_"+level,v,tri,uv);var go=MeshObject278("LOD"+level,mesh,MountainMaterial290(field.Mountain.Realm,"Granite"),root.transform,false);
    lods.Add(new LOD(level==0?.22f:level==1?.06f:.001f,new[]{go.GetComponent<Renderer>()}));
    if(level==0)root.AddComponent<MeshCollider>().sharedMesh=mesh;
   }
   var group=root.AddComponent<LODGroup>();group.SetLODs(lods.ToArray());group.RecalculateBounds();
  }
  static Material MountainMaterial290(string realm,string name)
  {
   var source=AssetDatabase.LoadAssetAtPath<Material>(A285+"/Materials/"+name+".mat");
   return Asset290("Materials/"+realm+"_"+name+".mat",()=>{var material=new Material(source);material.name=realm+"_"+name;material.enableInstancing=true;return material;});
  }
  static Mesh Mesh290(string id,Vector3[] v,int[] tri,Vector2[] uv)
  {
   var sceneLayout=Object.FindFirstObjectByType<CompactMountainAccess>()?.Layout;
   if(sceneLayout!=null){var kept=new List<int>();for(int i=0;i<tri.Length;i+=3){bool blocked=false;
    if(id.Contains("_cliff_")){var mountain=sceneLayout.Mountains.First(vv=>id.StartsWith(vv.Id));var aa=v[tri[i]];var bb=v[tri[i+1]];var cc=v[tri[i+2]];var lo=Vector3.Min(aa,Vector3.Min(bb,cc));var hi=Vector3.Max(aa,Vector3.Max(bb,cc));
     foreach(var corridor in new[]{mountain.MainPath,mountain.TemplePath,mountain.ReturnPath}){for(int k=0;k<corridor.Length;k+=2){var pp=corridor[k];if(pp.x+1.1f<lo.x||pp.x-1.1f>hi.x||pp.z+1.1f<lo.z||pp.z-1.1f>hi.z||pp.y+2.2f<lo.y||pp.y+.15f>hi.y)continue;blocked=true;break;}if(blocked)break;}}
    foreach(var site in sceneLayout.Mountains){if(!id.StartsWith(site.Id))continue;
     var a=v[tri[i]];var b=v[tri[i+1]];var c=v[tri[i+2]];
     if(Mathf.Max(a.y,Mathf.Max(b.y,c.y))<site.LiftBase.y+.22f||Mathf.Min(a.y,Mathf.Min(b.y,c.y))>site.LiftLanding.y+2.2f)continue;
     var min=Vector3.Min(a,Vector3.Min(b,c));var max=Vector3.Max(a,Vector3.Max(b,c));var p=site.LiftBase;
     if(p.x+.55f>=min.x&&p.x-.55f<=max.x&&p.z+.55f>=min.z&&p.z-.55f<=max.z){blocked=true;break;}
    }if(!blocked){kept.Add(tri[i]);kept.Add(tri[i+1]);kept.Add(tri[i+2]);}}
    tri=kept.ToArray();}
   var mesh=Asset290("Meshes/"+id+".asset",()=>new Mesh());mesh.Clear();mesh.indexFormat=IndexFormat.UInt32;mesh.vertices=v;mesh.triangles=tri;mesh.uv=uv;mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);return mesh;
  }
  static void PathMesh290(string id,Vector3[] path,float width,Material material,Transform parent)
  {
   if(id.EndsWith("_main")||id.EndsWith("_hidden")||id.EndsWith("_return")){
    var steps=new List<Vector3>{path[0]};
    for(int i=1;i<path.Length;i++){float rise=path[i].y-path[i-1].y;
     if(Mathf.Abs(rise)>.075f&&Mathf.Abs(rise)<.255f){var lip=Vector3.Lerp(path[i-1],path[i],.94f);lip.y=path[i-1].y;steps.Add(lip);}
     steps.Add(path[i]);}path=steps.ToArray();}
   var v=new List<Vector3>();var uv=new List<Vector2>();var tri=new List<int>();float distance=0;
   for(int i=0;i<path.Length;i++)
   {
    if(i>0)distance+=Vector3.Distance(path[i-1],path[i]);var tangent=path[Math.Min(i+1,path.Length-1)]-path[Math.Max(0,i-1)];tangent.y=0;var side=Vector3.Cross(Vector3.up,tangent.normalized);
    float spread=width*.5f*(1+.12f*Mathf.Sin(distance*.17f)+.06f*Mathf.Sin(distance*.71f));
    for(int j=0;j<5;j++){float across=(j-2)*.5f;var p=path[i]+side*spread*across+Vector3.up*.035f;p.y+=Mathf.Abs(across)>.9f?.045f*Mathf.Sin(distance*2.1f+j):0;v.Add(p);uv.Add(new Vector2(across*spread,distance));}
    if(i==0)continue;for(int j=0;j<4;j++){int a=i*5+j;tri.AddRange(new[]{a-5,a,a+1,a-5,a+1,a-4});}
   }
   var mesh=Mesh290(id,v.ToArray(),tri.ToArray(),uv.ToArray());MeshObject278(id,mesh,material,parent,true);
  }
  static void BuildCliffBands290(MountainField290 field,Transform parent,Material material)
  {
   var m=field.Mountain;var path=m.MainPath;var centre=field.World(new Vector3(-150,160,330));
   for(int band=0;band<2;band++)
   {
    var v=new List<Vector3>();var uv=new List<Vector2>();var tri=new List<int>();
    for(int i=0;i<path.Length;i+=2)
    {
     var p=path[i];var tangent=path[Math.Min(path.Length-1,i+1)]-path[Math.Max(0,i-1)];tangent.y=0;var side=Vector3.Cross(Vector3.up,tangent.normalized);if(Vector3.Dot(side,centre-p)<0)side=-side;
     int row=v.Count/9;float fade=Mathf.SmoothStep(0,1,Mathf.Min(i,path.Length-i)/45f);
     for(int j=0;j<9;j++)
     {
      float t=j/8f;var pos=p+side*(band==0?3.4f+t*5:-3.4f-t*13);
      float end=field.Sample(pos.x,pos.z),y=band==0?Mathf.Lerp(p.y-1,Mathf.Max(p.y,end)+fade*4,t):Mathf.Lerp(p.y-.4f,Mathf.Min(p.y-18*fade,end),t);
      pos.y=y+fade*Mathf.Sin(i*.23f+j*.95f)*.32f*Mathf.Sin(t*Mathf.PI);v.Add(pos);uv.Add(new Vector2(i*.75f,t*12));
      if(row>0&&j>0){int a=row*9+j;tri.AddRange(band==0?new[]{a-10,a-1,a,a-10,a,a-9}:new[]{a-10,a,a-1,a-10,a-9,a});}
     }
    }
    MeshObject278(m.Id+(band==0?"_inner_rock":"_outer_apron"),Mesh290(m.Id+"_cliff_"+band,v.ToArray(),tri.ToArray(),uv.ToArray()),material,parent,true);
   }
  }
  static void BuildLift290(MountainField290 field,Transform parent,Material rock)
  {
   var m=field.Mountain;var root=new GameObject("Guk_"+m.Id);root.transform.SetParent(parent,false);
   var node=root.AddComponent<Oheangbu.App.Demo.GukLiftSite>();node.Id=m.Id+"_guk";
   node.Lower=new GameObject("Lower").transform;node.Lower.SetParent(root.transform);node.Lower.position=m.LiftBase+Vector3.up*.035f;
   node.Upper=new GameObject("Upper").transform;node.Upper.SetParent(root.transform);node.Upper.position=m.LiftLanding+Vector3.up*.035f;
   var upperJoin=m.MainPath.Where(p=>Mathf.Abs(p.y-m.LiftLanding.y)<2).OrderBy(p=>(new Vector2(p.x,p.z)-new Vector2(m.LiftLanding.x,m.LiftLanding.z)).sqrMagnitude).First();
   var shelf=Resample290(new[]{m.LiftLanding,upperJoin},.35f);PathMesh290(m.Id+"_upper_shelf",shelf,2.0f,rock,root.transform);
   // Grow at the exposed edge: the shaft is beside the upper shelf, never through it.
   node.CastRadius=.25f;
   Box290("PermanentRootBed",root.transform,m.LiftBase+Vector3.up*(.035f-.12f),new Vector3(1.6f,.24f,1.6f),rock);
  }
 }
}
