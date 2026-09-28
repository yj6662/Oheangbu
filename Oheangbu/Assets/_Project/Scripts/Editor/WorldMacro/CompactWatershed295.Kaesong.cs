using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] sealed class KaesongReceipt295
  {
   public string Interpretation="Fictional flooded capital informed by Manwoldae terrain, stone terraces and asymmetrical precincts; not an archaeological reconstruction.";
   public string[] References={"https://whc.unesco.org/uploads/nominations/1278rev.pdf (printed pp.26,28,30-31; Figures2-4 inspected)","https://contents.history.go.kr/mobile/kc/view.do?code=kc_age_20&levelId=kc_r200300"};
   public string SourceHall="Assets/JejumokGwana/Prefabs/Buildings/Honhwagak.prefab; previously owned Joseon-era Jeju administrative hall asset, original terms retained, not a verified Goryeo superstructure.";
   public string Stone="Existing Highlands293 Hyeongang granite material derived from its CC0 source ledger.";
   public string Timber="Existing Mountain285 Pier289_planks material; source retained in Reworld292 sources.json.";
   public string ReferencePdfSha256;
   public Vector3 Shore,UpperHall,LookFrom,LookAt;
   public float Yaw,LakeLevel,LowerCourt,MiddleCourt,UpperCourt;
   public int Halls,StonePieces,WoodPieces,MeshTriangles;
   public KaesongHallGeometry295[] HallGeometry;
   public string CameraMethod;
   public float CameraGroundBefore,CameraGroundAfter,CameraDistance;
   public float[] CameraSightClearances;
  }
  [Serializable] sealed class KaesongRendererGeometry295
  {
   public string Name,Mesh,MeshPath;public bool Active,Enabled,ForceOff,MaterialsCoverSubmeshes;public int Vertices,Submeshes,Triangles;
   public Vector3 Position,LossyScale,BoundsCenter,BoundsSize,MeshBoundsCenter,MeshBoundsSize;public string[] Materials;
  }
  [Serializable] sealed class KaesongLodGeometry295
  {public float Threshold;public string[] Renderers;}
  [Serializable] sealed class KaesongHallGeometry295
  {
   public string Name;public bool Active;public Vector3 Position,LossyScale,BoundsCenter,BoundsSize,LodReference;
   public float LodSize;public KaesongRendererGeometry295[] Renderers;public KaesongLodGeometry295[] Lods;
  }
  [Serializable] sealed class KaesongGeometryReport295
  {public KaesongHallGeometry295 SourceNear,SourceDistant;public KaesongHallGeometry295[] Candidates;}
  [Serializable] sealed class KaesongRoutePaths295 {public KaesongRoutePath295[] routes=Array.Empty<KaesongRoutePath295>();}
  [Serializable] sealed class KaesongRoutePath295 {public Vector3[] points=Array.Empty<Vector3>();}
  public static string ReframeKaesong295()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=Scene295)throw new Exception("Kaesong receipt framing requires the #295 Edit candidate.");
   var receipt=JsonUtility.FromJson<KaesongReceipt295>(File.ReadAllText(O295+"/KaesongSources.json"));
   var layout=Session292().MountainLayout;KaesongFrame295(receipt,new CompactWorldSurface(layout),layout.Hydrology);
   ArchiveKaesongReceipt295();File.WriteAllText(O295+"/KaesongSources.json",JsonUtility.ToJson(receipt,true));
   return "Kaesong camera receipt updated only: eye="+receipt.LookFrom+", target="+receipt.LookAt+", 3 sight clearances="+string.Join(", ",receipt.CameraSightClearances.Select(v=>v.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)))+". Regenerate the separate QA camera list.";
  }
  static void ArchiveKaesongReceipt295()
  {
   string path=O295+"/KaesongSources.json";if(!File.Exists(path))return;
   Directory.CreateDirectory(O295+"/History");File.Copy(path,O295+"/History/KaesongSources-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff")+".json");
  }
  static void KaesongFrame295(KaesongReceipt295 receipt,CompactWorldSurface field,CompactHydrologySO hydro)
  {
   if(hydro?.Lake?.Polygon==null||hydro.Lake.Polygon.Length<3)throw new Exception("Main-lake polygon is required for Kaesong framing.");
   var before=new CompactWorldSurface(1001,1501,4,File.ReadAllBytes(A292+"/Surface/height.bytes"));
   var rotation=Quaternion.Euler(0,receipt.Yaw,0);var origin=new Vector3(receipt.Shore.x,0,receipt.Shore.z);
   Vector3 P(float x,float y,float z)=>origin+rotation*new Vector3(x,y,z);
   var upper=receipt.HallGeometry?.FirstOrDefault(h=>h.Name=="PalaceHall_SurvivingRoof");
   var roof=upper!=null?upper.BoundsCenter:P(-9,receipt.UpperCourt+4.8f,53);
   var middle=P(0,receipt.MiddleCourt+1.5f,7);var target=Vector3.Lerp(middle,roof,.55f);
   var goals=new[]{roof+Vector3.up*(upper!=null?upper.BoundsSize.y*.3f:2.8f),P(-9,receipt.UpperCourt+1.5f,38),middle};
   float best=float.PositiveInfinity;Vector3 eye=default;float[] bestClearance=null;float bestBefore=0,bestAfter=0;
   for(float distance=110;distance<=170;distance+=10)for(float angle=-80;angle<=80;angle+=5)
   {
    float radians=angle*Mathf.Deg2Rad;var horizontal=rotation*new Vector3(Mathf.Sin(radians)*distance,0,-Mathf.Cos(radians)*distance);
    var candidate=target+horizontal;float oldGround=before.Sample(candidate.x,candidate.z),newGround=field.Sample(candidate.x,candidate.z);
    if(!KaesongInLake295(new Vector2(candidate.x,candidate.z),hydro.Lake.Polygon)||newGround>receipt.LakeLevel-.5f)continue;
    candidate.y=Mathf.Max(receipt.LakeLevel+17,Mathf.Max(oldGround,newGround)+5);
    float[] clearance=new float[goals.Length];bool clear=true;
    for(int i=0;i<goals.Length;i++)
    {
     clearance[i]=float.PositiveInfinity;int steps=Mathf.CeilToInt(Vector3.Distance(candidate,goals[i])/1.5f);
     for(int step=0;step<=steps;step++)
     {var p=Vector3.Lerp(candidate,goals[i],step/(float)steps);clearance[i]=Mathf.Min(clearance[i],p.y-field.Sample(p.x,p.z));}
     if(clearance[i]<.7f){clear=false;break;}
    }
    if(!clear)continue;
    // Prefer a lake-side three-quarter view at ~140m. Raising above old terrain is explicit and penalized.
    float score=Mathf.Abs(distance-140)*.03f+Mathf.Abs(angle+20)*.015f+Mathf.Max(0,candidate.y-receipt.LakeLevel-20)*.12f;
    if(score>=best)continue;best=score;eye=candidate;bestClearance=clearance;bestBefore=oldGround;bestAfter=newGround;
   }
   if(!float.IsFinite(best))throw new Exception("No safe lake camera has three terrain sightlines within 110–170m; retain prior receipt for review.");
   receipt.LookFrom=eye;receipt.LookAt=target;receipt.CameraGroundBefore=bestBefore;receipt.CameraGroundAfter=bestAfter;
   receipt.CameraDistance=Vector2.Distance(new Vector2(eye.x,eye.z),new Vector2(target.x,target.z));receipt.CameraSightClearances=bestClearance;
   receipt.CameraMethod="Main-lake XZ; 110–170m horizontal distance; nominal water+17m, raised when needed to clear both source/current terrain by5m; sampled 1.5m terrain sightlines to surviving roof, upper court and middle court. No scene or QA camera mutation.";
  }
  static bool KaesongInLake295(Vector2 p,Vector2[] polygon)
  {
   bool inside=false;for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)if((polygon[i].y>p.y)!=(polygon[j].y>p.y)&&p.x<(polygon[j].x-polygon[i].x)*(p.y-polygon[i].y)/(polygon[j].y-polygon[i].y)+polygon[i].x)inside=!inside;
   return inside;
  }
  public static string InspectKaesong295()
  {
   var sansa=Root295("Reworld292_Sansa");
   var source=sansa!=null?sansa.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="KoreanHall_SourceAdaptation"):null;
   var root=Root295("Watershed295_SunkenCapital");
   var report=new KaesongGeometryReport295{SourceNear=HallGeometry295(source),SourceDistant=HallGeometry295(source?.parent.Find("KoreanHall_Distant")),
    Candidates=root==null?Array.Empty<KaesongHallGeometry295>():root.GetComponentsInChildren<LODGroup>(true).Where(g=>g.name.Contains("Hall_")).Select(g=>HallGeometry295(g.transform)).ToArray()};
   string json=JsonUtility.ToJson(report,true);Directory.CreateDirectory(O295);File.WriteAllText(O295+"/KaesongGeometry.json",json);
   return "Wrote "+Path.GetFullPath(O295+"/KaesongGeometry.json")+"; candidate halls="+report.Candidates.Length+", renderers="+report.Candidates.Sum(h=>h.Renderers.Length)+", invalid material coverage="+report.Candidates.Sum(h=>h.Renderers.Count(r=>!r.MaterialsCoverSubmeshes))+".";
  }
  static KaesongHallGeometry295 HallGeometry295(Transform hall)
  {
   if(hall==null)return null;var renderers=hall.GetComponentsInChildren<Renderer>(true);var group=hall.GetComponent<LODGroup>();
   var bounds=new Bounds(hall.position,Vector3.zero);if(renderers.Length>0){bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);}
   return new KaesongHallGeometry295{Name=hall.name,Active=hall.gameObject.activeInHierarchy,Position=hall.position,LossyScale=hall.lossyScale,BoundsCenter=bounds.center,BoundsSize=bounds.size,
    LodSize=group!=null?group.size:0,LodReference=group!=null?group.localReferencePoint:Vector3.zero,
    Lods=group==null?Array.Empty<KaesongLodGeometry295>():group.GetLODs().Select(l=>new KaesongLodGeometry295{Threshold=l.screenRelativeTransitionHeight,Renderers=l.renderers.Where(r=>r!=null).Select(r=>r.name).ToArray()}).ToArray(),
    Renderers=renderers.Select(r=>{var f=r.GetComponent<MeshFilter>();var mesh=f!=null?f.sharedMesh:null;int triangles=0;
     if(mesh!=null)for(int i=0;i<mesh.subMeshCount;i++)triangles+=(int)mesh.GetIndexCount(i)/3;
     return new KaesongRendererGeometry295{Name=r.name,Mesh=mesh!=null?mesh.name:null,MeshPath=AssetDatabase.GetAssetPath(mesh),Active=r.gameObject.activeInHierarchy,Enabled=r.enabled,ForceOff=r.forceRenderingOff,
      Vertices=mesh!=null?mesh.vertexCount:0,Submeshes=mesh!=null?mesh.subMeshCount:0,Triangles=triangles,Position=r.transform.position,LossyScale=r.transform.lossyScale,
      MaterialsCoverSubmeshes=mesh!=null&&mesh.subMeshCount>0&&r.sharedMaterials.Length>=mesh.subMeshCount&&r.sharedMaterials.Take(mesh.subMeshCount).All(m=>m!=null),
      BoundsCenter=r.bounds.center,BoundsSize=r.bounds.size,MeshBoundsCenter=mesh!=null?mesh.bounds.center:Vector3.zero,MeshBoundsSize=mesh!=null?mesh.bounds.size:Vector3.zero,
      Materials=r.sharedMaterials.Select(m=>m!=null?AssetDatabase.GetAssetPath(m):"MISSING").ToArray()};}).ToArray()};
  }
  static void KaesongRuins295(CompactWorldSurface field,CompactHydrologySO hydro,List<string> report)
  {
   if(hydro?.Lake?.ShorePoints==null||hydro.Lake.ShorePoints.Length==0)throw new Exception("Kaesong precinct needs the final lake shoreline.");
   var sansa=Root295("Reworld292_Sansa");
   var source=sansa!=null?sansa.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="KoreanHall_SourceAdaptation"):null;
   if(source==null)throw new Exception("Original-proportion Korean hall source is missing.");
   var old=Root295("Watershed295_SunkenCapital");if(old!=null)Object.DestroyImmediate(old);
   var site=ChooseKaesongSite295(field,hydro);var origin=site.shore;float water=hydro.Lake.Level;
   var root=new GameObject("Watershed295_SunkenCapital");root.transform.SetPositionAndRotation(new Vector3(origin.x,0,origin.z),Quaternion.Euler(0,site.yaw,0));
   var stoneSource=AssetDatabase.LoadAssetAtPath<Material>(S293+"/Materials/Hyeongang_Granite.mat");
   var stone=Asset295("Materials/KaesongStone.mat",()=>new Material(stoneSource));
   var woodSource=AssetDatabase.LoadAssetAtPath<Material>(A285+"/Materials/Pier289_planks.mat");
   var wood=Asset295("Materials/KaesongTimber.mat",()=>new Material(woodSource));
   var upper=new KaesongBatch295("UpperPalace",root.transform,stone);var middle=new KaesongBatch295("FloodedCourts",root.transform,stone);
   var market=new KaesongBatch295("LowTown",root.transform,stone);var timber=new KaesongBatch295("TimberRemains",root.transform,wood);
   float Height(float x,float z){var p=root.transform.TransformPoint(new Vector3(x,0,z));return field.Sample(p.x,p.z);}
   float Highest(float x,float z,float width,float depth)
   {float highest=float.NegativeInfinity;for(int a=-1;a<=1;a++)for(int b=-1;b<=1;b++)highest=Mathf.Max(highest,Height(x+a*width*.5f,z+b*depth*.5f));return highest;}
   float lowerTop=Mathf.Max(water-2.3f,Highest(10,-25,52,22)+.2f);
   float middleTop=Mathf.Max(water+.5f,Highest(0,7,60,28)+.3f);
   float upperTop=Mathf.Max(middleTop+5.5f,Highest(-9,51,48,30)+.3f);
   var random=new System.Random(295073);
   float R(float min,float max)=>(float)(min+random.NextDouble()*(max-min));
   void Terrace(KaesongBatch295 batch,string label,float x,float z,float width,float depth,float top,bool fullPaving)
   {
    // Jointed retaining courses stop against the actual terrain instead of a floating slab.
    foreach(int side in new[]{-1,1})
    {
     for(float across=-width*.5f;across<width*.5f;)
     {
      float size=Mathf.Min(R(1.7f,3.4f),width*.5f-across);float xx=x+across+size*.5f,zz=z+side*(depth*.5f-.38f),baseY=Height(xx,zz)-.25f;
      for(float y=baseY;y<top;y+=.58f)batch.Box(new Vector3(xx,Mathf.Min(top,y+.56f)*.5f+y*.5f,zz),new Vector3(size-.025f,Mathf.Min(.56f,top-y),.86f),R(-.7f,.7f));
      across+=size;
     }
     for(float along=-depth*.5f+.7f;along<depth*.5f-.7f;along+=2.35f)
     {
      float xx=x+side*(width*.5f-.35f),zz=z+along,baseY=Height(xx,zz)-.25f;
      for(float y=baseY;y<top;y+=.58f)batch.Box(new Vector3(xx,y+Mathf.Min(.56f,top-y)*.5f,zz),new Vector3(.85f,Mathf.Min(.56f,top-y),2.32f));
     }
    }
    // Broad surfaces are structural stone paving, broken into restrained joints rather than a single white plane.
    float stride=fullPaving?3.1f:5.8f;
    for(float xx=x-width*.5f+1.7f;xx<x+width*.5f-1;xx+=stride)
     for(float zz=z-depth*.5f+1.7f;zz<z+depth*.5f-1;zz+=stride)
     {
      if(!fullPaving&&random.NextDouble()<.32)continue;
      batch.Box(new Vector3(xx,top-.16f,zz),new Vector3(Mathf.Min(stride-.07f,x+width*.5f-xx)*2*.5f,.3f,Mathf.Min(stride-.07f,z+depth*.5f-zz)),R(-.3f,.3f));
     }
   }
   Terrace(middle,"LowerForecourt",10,-25,52,22,lowerTop,false);
   Terrace(middle,"MiddleForecourt",0,7,60,28,middleTop,true);
   Terrace(upper,"MainHallTerrace",-9,51,48,30,upperTop,true);
   // Four wide flights recall the Manwoldae front stair group, adapted to the measured candidate rise.
   int stairCount=Mathf.CeilToInt((upperTop-middleTop)/.21f);
   for(int flight=0;flight<4;flight++)
   {
    float x=-27+flight*12;for(int k=0;k<stairCount;k++)
    {
     float a=k/(float)stairCount,b=(k+1f)/stairCount,z=Mathf.Lerp(21,36,(a+b)*.5f),top=Mathf.Lerp(middleTop,upperTop,b);
     float mass=top-middleTop+.4f;upper.Box(new Vector3(x,top-mass*.5f,z),new Vector3(4.8f,mass,15f/stairCount+.025f));
    }
   }
   // One broken, partly inundated flight is offset from the main axis.
   int lowerSteps=Mathf.CeilToInt((middleTop-lowerTop)/.2f);
   for(int k=0;k<lowerSteps;k++)
   {
    if(k==2||k==5)continue;float t=(k+1f)/lowerSteps;
    float top=Mathf.Lerp(lowerTop,middleTop,t),mass=top-lowerTop+.3f;
    middle.Box(new Vector3(8,top-mass*.5f,Mathf.Lerp(-14,-7,t)),new Vector3(7.2f,mass,7f/lowerSteps+.025f));
   }
   var nearMeshes=KaesongNearMeshes295(source);
   var hall=CopyKaesongHall295(source,nearMeshes,root.transform,new Vector3(-9,upperTop,53),0,"PalaceHall_SurvivingRoof",true);
   // A second court is lower and oblique; the precinct is not three identical temples on a grid.
   float wingTop=Mathf.Max(water-1.05f,Highest(-52,10,26,23)+.2f);
   Terrace(middle,"WesternResidentialCourt",-52,10,27,24,wingTop,false);
   var westHall=CopyKaesongHall295(source,nearMeshes,root.transform,new Vector3(-52,wingTop,12),-17,"WestHall_FloodedLowerStorey",false);
   // Foundation grids and orphaned column bases carry the lost corridor, while keeping gaps legible.
   foreach(int side in new[]{-1,1})for(int k=0;k<9;k++)
   {
    float x=side<0?-31:32,z=-29+k*6.1f,top=Mathf.Lerp(lowerTop,middleTop,Mathf.Clamp01((z+22)/25));
    foreach(float columnX in new[]{x-1.8f,x+1.8f})
    {
     middle.Cylinder(new Vector3(columnX,top+.18f,z),.58f,.36f,12);
     if((k+side+5)%3!=0){float height=R(1.4f,3.4f);timber.Cylinder(new Vector3(columnX,top+.36f+height*.5f,z),.17f,height,10);}
    }
    if(k==3||k==6)continue;middle.Box(new Vector3(x,top-.15f,z),new Vector3(4.5f,.3f,5.75f));
   }
   // The gate survives as column bases and a fractured timber lintel, not a pasted complete hall.
   for(int k=0;k<4;k++)
   {
    float x=-8+k*5.3f;middle.Cylinder(new Vector3(x,lowerTop+.28f,-37),.8f,.56f,12);
    float height=k==0?1.8f:k==3?2.7f:4.8f;
    timber.Cylinder(new Vector3(x,lowerTop+.55f+height*.5f,-37),.24f,height,12);
   }
   timber.Box(new Vector3(.2f,lowerTop+5.15f,-37),new Vector3(8.5f,.38f,.5f),-4);
   // Small irregular plots branch from a bent lane following the drowned shore below the palace.
   for(int plot=0;plot<10;plot++)
   {
    float z=-63+plot*7.2f,x=45+Mathf.Sin(plot*.62f)*7+(plot%2==0?10:-8),w=R(6.5f,9.5f),d=R(7.5f,11);
    float top=Mathf.Max(water-R(1.4f,2.2f),Highest(x,z,w,d)+.15f);
    Terrace(market,"MarketPlot",x,z,w,d,top,false);
    for(int c=0;c<4;c++)
    {
     var p=new Vector3(x+(c%2==0?-1:1)*(w*.5f-1),top+.2f,z+(c<2?-1:1)*(d*.5f-1));market.Cylinder(p,.43f,.4f,10);
     if((plot+c)%3==0)timber.Cylinder(p+Vector3.up*.9f,.12f,1.8f,8);
    }
    if(plot%3==0)market.Box(new Vector3(x,top+.65f,z+d*.5f-.4f),new Vector3(w*.7f,1.3f,.8f),R(-5,5));
   }
   for(int k=0;k<18;k++)
   {
    float z=-70+k*4.2f,x=45+Mathf.Sin(k*.36f)*7,top=Mathf.Max(water-1.8f,Height(x,z)+.15f);
    market.Box(new Vector3(x,top-.15f,z),new Vector3(4.1f,.3f,4.05f),Mathf.Cos(k*.36f)*11);
   }
   int pieces=upper.Count+middle.Count+market.Count,triangles=upper.Save()+middle.Save()+market.Save()+timber.Save();
   var receipt=new KaesongReceipt295{Shore=origin,Yaw=site.yaw,LakeLevel=water,LowerCourt=lowerTop,MiddleCourt=middleTop,UpperCourt=upperTop,
    UpperHall=hall.position,Halls=2,StonePieces=pieces,WoodPieces=timber.Count,MeshTriangles=triangles,HallGeometry=new[]{HallGeometry295(hall),HallGeometry295(westHall)},
   };
   KaesongFrame295(receipt,field,hydro);
   string reference="../tmp/pdfs/kaesong-nomination.pdf";
   if(File.Exists(reference)){using(var sha=SHA256.Create())receipt.ReferencePdfSha256=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(reference))).Replace("-","").ToLowerInvariant();}
   Directory.CreateDirectory(O295);ArchiveKaesongReceipt295();File.WriteAllText(O295+"/KaesongSources.json",JsonUtility.ToJson(receipt,true));
   report.Add("Kaesong-inspired sunken precinct: 3 stepped courts, 2 proportion-preserved hall roofs, four front stair flights, gate/corridor remnants, 10 market foundations; stone pieces="+pieces+", wood="+timber.Count+", new triangles="+triangles+"; shore="+origin+". Exact historical reconstruction not claimed.");
  }
  static (Vector3 shore,float yaw) ChooseKaesongSite295(CompactWorldSurface field,CompactHydrologySO hydro)
  {
   var paths=JsonUtility.FromJson<KaesongRoutePaths295>(File.ReadAllText(G295+"/routes.json")).routes.Select(r=>r.points).Where(p=>p!=null&&p.Length>0).ToList();
   foreach(var mountain in Session292().MountainLayout.Mountains)
   {paths.Add(mountain.MainPath??Array.Empty<Vector3>());paths.Add(mountain.TemplePath??Array.Empty<Vector3>());paths.Add(mountain.ReturnPath??Array.Empty<Vector3>());paths.Add(new[]{mountain.Foot});}
   float best=float.PositiveInfinity;Vector3 chosen=default;float chosenYaw=0,level=hydro.Lake.Level;
   foreach(var p in hydro.Lake.ShorePoints)
   {
    if(p.z<4200||p.z>5650||p.x<1400||p.x>3100)continue;
    for(int heading=0;heading<64;heading++)
    {
     float yaw=heading*5.625f;var q=Quaternion.Euler(0,yaw,0);
     float H(float x,float z){var v=p+q*new Vector3(x,0,z);return field.Sample(v.x,v.z);}
     float low=H(10,-25),mid=H(0,7),high=H(-9,51);
     if(low>level-.7f||low<level-12||mid>level+5||high<level+1||high>level+17)continue;
     float lowMax=float.NegativeInfinity,midMax=float.NegativeInfinity,hiMax=float.NegativeInfinity;
     foreach(int x in new[]{-1,0,1})foreach(int z in new[]{-1,0,1})
     {lowMax=Mathf.Max(lowMax,H(10+x*26,-25+z*11));midMax=Mathf.Max(midMax,H(x*30,7+z*14));hiMax=Mathf.Max(hiMax,H(-9+x*24,51+z*15));}
     if(lowMax>level-.4f||midMax>level+7||hiMax>level+21)continue;
     if(KaesongBlocksTravel295(p,q,paths))continue;
     float score=Vector2.Distance(new Vector2(p.x,p.z),new Vector2(2270,5050))*.015f+
      Mathf.Abs(high-level-8)*2+Mathf.Abs(mid-level)*2+(hiMax-high)*2+(midMax-mid)*3;
     if(score>=best)continue;best=score;chosen=p;chosenYaw=yaw;
    }
   }
   if(!float.IsFinite(best))throw new Exception("No grounded sloping shore fits the Kaesong precinct; review terrain before placing a palace.");
   return(chosen,chosenYaw);
  }
  static bool KaesongBlocksTravel295(Vector3 origin,Quaternion rotation,List<Vector3[]> paths)
  {
   var inverse=Quaternion.Inverse(rotation);
   // Physical courts/steps/corridors/market footprints with 2m clearance. Open gaps can retain existing paths.
   foreach(var path in paths)
   {
    if(path.Length==0)continue;var previous=inverse*(path[0]-origin);
    if(KaesongSegmentHits295(new Vector2(previous.x,previous.z),new Vector2(previous.x,previous.z)))return true;
    for(int i=1;i<path.Length;i++)
    {var next=inverse*(path[i]-origin);if(KaesongSegmentHits295(new Vector2(previous.x,previous.z),new Vector2(next.x,next.z)))return true;previous=next;}
   }
   return false;
  }
  static bool KaesongSegmentHits295(Vector2 a,Vector2 b)
  {
   bool Rect(float minX,float maxX,float minZ,float maxZ)
   {
    float lo=0,hi=1;var delta=b-a;
    bool Axis(float p,float d,float min,float max)
    {if(Mathf.Abs(d)<.000001f)return p>=min&&p<=max;float first=(min-p)/d,last=(max-p)/d;if(first>last){float swap=first;first=last;last=swap;}lo=Mathf.Max(lo,first);hi=Mathf.Min(hi,last);return lo<=hi;}
    return Axis(a.x,delta.x,minX-2,maxX+2)&&Axis(a.y,delta.y,minZ-2,maxZ+2);
   }
   if(Rect(-33,15,36,66)||Rect(-30,30,-7,21)||Rect(-16,36,-36,-14)||Rect(-65.5f,-38.5f,-2,22))return true;
   for(int i=0;i<4;i++){float x=-27+i*12;if(Rect(x-2.4f,x+2.4f,21,36))return true;}
   if(Rect(4.4f,11.6f,-14,-7)||Rect(-35.1f,-26.9f,-31.9f,22.7f)||Rect(27.9f,36.1f,-31.9f,22.7f)||Rect(-9,9.9f,-37.8f,-36.2f))return true;
   for(int plot=0;plot<10;plot++)
   {float z=-63+plot*7.2f,x=45+Mathf.Sin(plot*.62f)*7+(plot%2==0?10:-8);if(Rect(x-4.8f,x+4.8f,z-5.6f,z+5.6f))return true;}
   for(int k=0;k<18;k++){float z=-70+k*4.2f,x=45+Mathf.Sin(k*.36f)*7;if(Rect(x-2.5f,x+2.5f,z-2.5f,z+2.5f))return true;}
   return false;
  }
  sealed class KaesongHallMesh295 { public Mesh Mesh; public Material Material; }
  static KaesongHallMesh295[] KaesongNearMeshes295(Transform source)
  {
   // Preserve every source triangle, UV and material; only combine parts sharing the same material.
   var groups=new Dictionary<Material,List<CombineInstance>>();
   foreach(var f in source.GetComponentsInChildren<MeshFilter>(true))
   {
    var r=f.GetComponent<Renderer>();if(f.sharedMesh==null||r==null)continue;
    if(f.sharedMesh.subMeshCount==0||r.sharedMaterials.Length<f.sharedMesh.subMeshCount)throw new Exception("Source hall material coverage is incomplete: "+f.name);
    for(int sub=0;sub<f.sharedMesh.subMeshCount;sub++)
    {
     var material=r.sharedMaterials[sub];if(material==null)throw new Exception("Source hall material is missing: "+f.name);
     if(!groups.TryGetValue(material,out var list)){list=new List<CombineInstance>();groups[material]=list;}
     list.Add(new CombineInstance{mesh=f.sharedMesh,subMeshIndex=sub,transform=source.worldToLocalMatrix*f.transform.localToWorldMatrix});
    }
   }
   return groups.OrderBy(p=>AssetDatabase.GetAssetPath(p.Key),StringComparer.Ordinal).Select((pair,index)=>
   {
    var mesh=Asset295("Meshes/Kaesong_HallNear_"+index.ToString("D2")+".asset",()=>new Mesh());mesh.Clear();mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
    mesh.CombineMeshes(pair.Value.ToArray(),true,true,false);mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
    return new KaesongHallMesh295{Mesh=mesh,Material=pair.Key};
   }).ToArray();
  }
  static Material[] KaesongProxyMaterials295(Mesh mesh,Material[] existing)
  {
   if(mesh==null||mesh.subMeshCount==0)throw new Exception("Korean hall proxy has no submeshes.");
   if(existing.Length>=mesh.subMeshCount&&existing.Take(mesh.subMeshCount).All(m=>m!=null))return existing;
   // The inherited OBJ has 23 valid submeshes but zero renderer materials. Recover each slot from
   // unique face counts in the reviewed OBJ and its original material ledger, independent of importer ordering.
   var counts=new Dictionary<int,int>();int current=-1;
   foreach(string line in File.ReadLines(A292+"/Architecture/HallLOD.obj"))
   {
    if(line.StartsWith("usemtl m_",StringComparison.Ordinal)){current=int.Parse(line.Substring(9).Trim(),System.Globalization.CultureInfo.InvariantCulture);if(!counts.ContainsKey(current))counts[current]=0;}
    else if(line.StartsWith("f ",StringComparison.Ordinal))
    {if(current<0)throw new Exception("Proxy OBJ face has no material.");int vertices=line.Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries).Length-1;counts[current]+=vertices-2;}
   }
   if(counts.Count!=mesh.subMeshCount||counts.Values.Distinct().Count()!=counts.Count)throw new Exception("Proxy material face counts need explicit review; cannot infer slot ownership.");
   var ledger=JsonUtility.FromJson<HallMaterials292>(File.ReadAllText(O292+"/HallLOD/materials.json"));
   var materials=new Material[mesh.subMeshCount];
   for(int sub=0;sub<mesh.subMeshCount;sub++)
   {
    int triangles=(int)mesh.GetIndexCount(sub)/3;var matches=counts.Where(p=>p.Value==triangles).ToArray();
    if(matches.Length!=1||matches[0].Key<0||matches[0].Key>=ledger.paths.Length)throw new Exception("Proxy submesh cannot be mapped to its reviewed material: "+sub);
    materials[sub]=AssetDatabase.LoadAssetAtPath<Material>(ledger.paths[matches[0].Key]);
    if(materials[sub]==null)throw new Exception("Reviewed proxy material is missing: "+ledger.paths[matches[0].Key]);
   }
   return materials;
  }
  static Transform CopyKaesongHall295(Transform source,KaesongHallMesh295[] nearMeshes,Transform parent,Vector3 position,float yaw,string name,bool collision)
  {
   var container=new GameObject(name);container.transform.SetParent(parent,false);container.transform.localRotation=Quaternion.Euler(0,180+yaw,0);container.transform.localScale=source.lossyScale;
   var near=new List<Renderer>();
   for(int index=0;index<nearMeshes.Length;index++)
   {
    var part=nearMeshes[index];var go=new GameObject("Near_"+index.ToString("D2"));go.transform.SetParent(container.transform,false);
    go.AddComponent<MeshFilter>().sharedMesh=part.Mesh;var copy=go.AddComponent<MeshRenderer>();copy.sharedMaterial=part.Material;near.Add(copy);
   }
   var distantSource=source.parent.Find("KoreanHall_Distant");var distant=new List<Renderer>();
   if(distantSource!=null)foreach(var f in distantSource.GetComponentsInChildren<MeshFilter>(true))
   {
    var r=f.GetComponent<Renderer>();if(f.sharedMesh==null||r==null)continue;
    var go=new GameObject("Distant_"+f.name);go.transform.SetParent(container.transform,false);var m=source.worldToLocalMatrix*f.transform.localToWorldMatrix;
    go.transform.localPosition=m.GetColumn(3);go.transform.localRotation=m.rotation;go.transform.localScale=m.lossyScale;
    go.AddComponent<MeshFilter>().sharedMesh=f.sharedMesh;var copy=go.AddComponent<MeshRenderer>();copy.sharedMaterials=KaesongProxyMaterials295(f.sharedMesh,r.sharedMaterials);distant.Add(copy);
    if(collision)go.AddComponent<MeshCollider>().sharedMesh=f.sharedMesh;
   }
   if(near.Count==0)throw new Exception("Korean source hall has no visible mesh.");
   var bounds=near[0].bounds;foreach(var r in near)bounds.Encapsulate(r.bounds);
   var target=parent.TransformPoint(position);container.transform.position+=new Vector3(target.x-bounds.center.x,target.y-bounds.min.y,target.z-bounds.center.z);
   var group=container.AddComponent<LODGroup>();group.SetLODs(distant.Count>0?new[]{new LOD(.1f,near.ToArray()),new LOD(.003f,distant.ToArray())}:new[]{new LOD(.003f,near.ToArray())});group.RecalculateBounds();
   foreach(var lod in group.GetLODs())
   {
    if(lod.renderers.Length==0)throw new Exception("Kaesong hall has an empty active LOD: "+name);
    foreach(var r in lod.renderers)
    {var mesh=r.GetComponent<MeshFilter>().sharedMesh;if(mesh==null||mesh.subMeshCount==0||r.sharedMaterials.Length<mesh.subMeshCount||r.sharedMaterials.Take(mesh.subMeshCount).Any(m=>m==null))throw new Exception("Kaesong hall LOD has missing material coverage: "+name+"/"+r.name);}
   }
   return container.transform;
  }
  sealed class KaesongBatch295
  {
   readonly string name;readonly Transform parent;readonly Material material;
   readonly List<Vector3> vertices=new List<Vector3>();readonly List<Vector2> uv=new List<Vector2>();readonly List<int> triangles=new List<int>();
   public int Count{get;private set;}
   public KaesongBatch295(string name,Transform parent,Material material){this.name=name;this.parent=parent;this.material=material;}
   void Face(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
   {
    int n=vertices.Count;vertices.AddRange(new[]{a,b,c,d});float w=Vector3.Distance(a,b),h=Vector3.Distance(b,c);uv.AddRange(new[]{Vector2.zero,new Vector2(w,0),new Vector2(w,h),new Vector2(0,h)});triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
   }
   void Triangle(Vector3 a,Vector3 b,Vector3 c)
   {int n=vertices.Count;vertices.AddRange(new[]{a,b,c});uv.AddRange(new[]{new Vector2(a.x,a.z),new Vector2(b.x,b.z),new Vector2(c.x,c.z)});triangles.AddRange(new[]{n,n+1,n+2});}
   public void Box(Vector3 center,Vector3 size,float yaw=0)
   {
    if(size.x<=.001f||size.y<=.001f||size.z<=.001f)return;Count++;
    var m=Matrix4x4.TRS(center,Quaternion.Euler(0,yaw,0),size);
    Vector3 P(float x,float y,float z)=>m.MultiplyPoint3x4(new Vector3(x,y,z)*.5f);
    Face(P(-1,1,-1),P(-1,1,1),P(1,1,1),P(1,1,-1));Face(P(-1,-1,-1),P(1,-1,-1),P(1,-1,1),P(-1,-1,1));
    Face(P(-1,-1,-1),P(-1,-1,1),P(-1,1,1),P(-1,1,-1));Face(P(1,-1,1),P(1,-1,-1),P(1,1,-1),P(1,1,1));
    Face(P(1,-1,-1),P(-1,-1,-1),P(-1,1,-1),P(1,1,-1));Face(P(-1,-1,1),P(1,-1,1),P(1,1,1),P(-1,1,1));
   }
   public void Cylinder(Vector3 center,float radius,float height,int sides)
   {
    Count++;for(int i=0;i<sides;i++)
    {
     float a=i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;
     Vector3 pa=new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius),pb=new Vector3(Mathf.Cos(b)*radius,0,Mathf.Sin(b)*radius),up=Vector3.up*height*.5f;
     Face(center+pa-up,center+pa+up,center+pb+up,center+pb-up);Triangle(center+up,center+pb+up,center+pa+up);Triangle(center-up,center+pa-up,center+pb-up);
    }
   }
   public int Save()
   {
    var mesh=Asset295("Meshes/Kaesong_"+name+".asset",()=>new Mesh());mesh.Clear();mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
    mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
    var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;
    go.AddComponent<MeshCollider>().sharedMesh=mesh;var lod=go.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.006f,new[]{r})});lod.RecalculateBounds();return triangles.Count/3;
   }
  }
 }
}
