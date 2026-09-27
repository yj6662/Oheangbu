using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  // Old bastion faces remain the exact physical foundation. The facade is a closed
  // continuous wall assembled from the owned CW face at fixed .65 source scale.
  // It has no new collider or horizontal walking ledge.
  static void RetainingColumn296(Transform root,CompactWorldSurface field,VenueBatch296 batch,Vector3 local,float span,float yaw,float top,float course,float maxDepth,float baseDepth)
  {
   var world=root.TransformPoint(local);float bottom=GroundVenue296(field,world.x,world.z)-root.position.y-.25f;
   bool collisionOnly=batch.CollisionOnly;
   batch.CollisionOnly=true;
   for(float y=bottom;y<top-.16f;y+=course)
   {
    float h=Mathf.Min(course,top-.16f-y);
    batch.Add(GateKit296+"SM_Bastion_001.prefab",new Vector3(local.x,y,local.z),new Vector3(span-.02f,h,Mathf.Min(maxDepth,baseDepth+(top-y)*.045f)),yaw);
   }
   batch.CollisionOnly=collisionOnly;
   if(collisionOnly||bottom>=top-.16f)return;
   var source=CrossingSource(Rampart296);var part=source.Parts[0];batch.Sources.Add(Rampart296);
   var normalise=Matrix4x4.Scale(Vector3.one*.65f)*Matrix4x4.Translate(-new Vector3(source.Bounds.center.x,source.Bounds.min.y,source.Bounds.center.z));
   var material=RegionalStoneVenue296(VenueMaterial296(part.material),batch.StoneStyle);
   bool tiered=batch.StoneStyle=="hyeongang"&&top-.16f-bottom>2.2f;
   float upper=top-.16f,total=upper-bottom;
   float Depth(float y)
   {
    float inset=.025f;
    if(tiered)
    {
     // Two short, steep bevels read as masonry tiers, not unsupported steps.
     inset+=.12f*Mathf.SmoothStep(0,1,Mathf.InverseLerp(bottom+total*.30f,bottom+total*.30f+.16f,y));
     inset+=.12f*Mathf.SmoothStep(0,1,Mathf.InverseLerp(bottom+total*.66f,bottom+total*.66f+.16f,y));
    }
    return Mathf.Max(.16f,Mathf.Min(maxDepth,baseDepth+(top-y)*.045f)*.5f-inset);
   }
   var boundaries=new SortedSet<float>{bottom,upper};
   // Align the stone courses from the court level so neighboring modules share joints.
   for(float y=upper-course;y>bottom;y-=course)boundaries.Add(y);
   if(tiered)foreach(float ratio in new[]{.30f,.66f}){boundaries.Add(bottom+total*ratio);boundaries.Add(Mathf.Min(upper,bottom+total*ratio+.08f));boundaries.Add(Mathf.Min(upper,bottom+total*ratio+.16f));}
   var levels=boundaries.ToArray();var rotation=Quaternion.Euler(0,yaw,0);
   for(int k=1;k<levels.Length;k++)
   {
    float y=levels[k-1],height=levels[k]-y;if(height<.0001f)continue;
    foreach(int face in new[]{0,1,2,3})
    {
     float width=face<2?span:Mathf.Max(Depth(y),Depth(y+height))*2;
     var clipped=CropWall296(part.mesh,part.submesh,normalise*part.matrix,width,height);
     var mesh=CrossingMasonryFacing296(clipped);Object.DestroyImmediate(clipped);
     if(mesh.vertexCount==0){Object.DestroyImmediate(mesh);throw new InvalidOperationException("CW lower face is empty");}
     var vertices=mesh.vertices;
     for(int i=0;i<vertices.Length;i++)
     {
      var p=vertices[i];float yy=y+p.y,d=Depth(yy);Vector3 q;
      if(face==0)q=new Vector3(p.x,yy,d);
      else if(face==1)q=new Vector3(-p.x,yy,-d);
      else if(face==2)q=new Vector3(span*.5f,yy,-p.x/width*(2*d));
      else q=new Vector3(-span*.5f,yy,p.x/width*(2*d));
      vertices[i]=new Vector3(local.x,0,local.z)+rotation*q;
     }
     mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
     batch.AddTemporaryVisual(mesh,material);
    }
   }
  }
  static void ClearRetainingVisuals296(Transform root)
  {
   var group=root.GetComponent<LODGroup>();if(group!=null)Object.DestroyImmediate(group);
   foreach(var t in root.Cast<Transform>().Where(t=>t.name.StartsWith("L0_",StringComparison.Ordinal)||t.name.StartsWith("L1_",StringComparison.Ordinal)||t.name.StartsWith("L2_",StringComparison.Ordinal)).ToArray())Object.DestroyImmediate(t.gameObject);
  }
  static string RetainingSceneCollision296()
  {
   return string.Join("\n",Session292().gameObject.scene.GetRootGameObjects().OrderBy(g=>g.name,StringComparer.Ordinal).Select(g=>InteriorCollisionStamp296(g.transform)));
  }
  static string RetainingTextHash296(string text)
  {using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
  [Serializable] sealed class RetainingRefreshReceipt296
  {
   public string Utc,Scene,BeforeColliderSha256,AfterColliderSha256,BeforeSupportSha256,AfterSupportSha256,HeightSha256,WaterSha256;
   public int ColliderCount,SupportSamples;public bool CollidersUnchanged,SupportUnchanged,MetadataUnchanged;
  }
  static string RetainingSupportStamp296(out int count)
  {
   var points=new List<Vector3>();foreach(var arena in Sheet296().Arenas)
   {
    points.AddRange(arena.Approach??Array.Empty<Vector3>());
    foreach(float x in new[]{-.4f,0,.4f})foreach(float z in new[]{-.4f,0,.4f})points.Add(arena.Centre+Quaternion.Euler(0,arena.Yaw,0)*new Vector3(arena.ClearSize.x*x,.03f,arena.ClearSize.y*z));
   }
   var paths=JsonUtility.FromJson<ComplexLedger296>(File.ReadAllText(O296+"/venue-complexes.json"));foreach(var path in paths.Paths)points.AddRange(path.Points);
   count=points.Count;var lines=new List<string>();
   foreach(var p in points)
   {
    var hits=Physics.RaycastAll(p+Vector3.up*1.5f,Vector3.down,4,~0,QueryTriggerInteraction.Ignore).Where(h=>h.collider.gameObject.isStatic).OrderBy(h=>Mathf.Abs(h.point.y-p.y)).ThenBy(h=>ScenePathVenue296(h.collider.transform),StringComparer.Ordinal).ToArray();
    lines.Add(p.ToString("R")+"|"+(hits.Length==0?"NO_STATIC_HIT":ScenePathVenue296(hits[0].collider.transform)+"|"+hits[0].point.ToString("R")));
   }
   return RetainingTextHash296(string.Join("\n",lines));
  }
  public static string RefreshRetainingMasonry296()
  {
   RequireClean292();if(EditorApplication.isPlayingOrWillChangePlaymode||Session292().gameObject.scene.path!=Scene296)throw new InvalidOperationException("Retaining repair requires saved296 Edit candidate");
   Physics.SyncTransforms();string before=RetainingSceneCollision296(),supportBefore=RetainingSupportStamp296(out int supportCount);
   string paths=File.ReadAllText(O296+"/venue-complexes.json"),placements=File.ReadAllText(O296+"/venue-placements.json");
   var layout=Session292().MountainLayout;var field=new CompactWorldSurface(layout);
   string height=HashVenue296(AssetDatabase.GetAssetPath(layout.FinalSurface));
   string water=HashVenue296(O296+"/../Watershed295/Generated/waterlevel.bytes");
   var sheet=Sheet296();string approaches=JsonUtility.ToJson(sheet);
   var old=JsonUtility.FromJson<ComplexLedger296>(paths);var report=new List<string>();
   venueModules296.Clear();regionalStone296.Clear();
   foreach(var arena in sheet.Arenas)
   {
    var root=Root296(arena.SceneRoot).transform;bool organic=arena.Id=="sanctuary296";
    var plan=new VenuePlan296{Id=arena.Id,Realm=arena.Realm,Place=arena.PlaceId,Clear=arena.ClearSize,Yaw=arena.Yaw,Centre=arena.Centre,Height=arena.ClearHeight,Interior=arena.Interior,Organic=organic};
    if(!organic)
    {
     ClearRetainingVisuals296(root);
     var batch=new VenueBatch296(plan.Id,root){StoneStyle=plan.Realm};TerraceVenue296(plan,root,field,batch);
     if(arena.Interior)
     {
      var eaves=root.Find("SecondEave_OutsideClearFloor");if(eaves!=null)Object.DestroyImmediate(eaves.gameObject);
      InteriorVenue296(plan,root,batch,report,true);
     }
     else CourtVenue296(plan,root,batch);
     batch.Save(true);
    }
    if(!arena.Interior)CompoundVenue296(plan,root,field,new List<CompactArchitectureSheetSO.Structure>(),report,true,true);
    report.Add("PASS "+arena.Id+" continuous original-UV CW facade; old foundations retained; new facade has zero colliders and no horizontal ledges.");
   }
   complexBuildings296.Clear();complexBuildings296.AddRange(old.Buildings);complexPaths296.Clear();complexPaths296.AddRange(old.Paths);
   Physics.SyncTransforms();
   string after=RetainingSceneCollision296(),supportAfter=RetainingSupportStamp296(out int afterCount);
   if(before!=after)throw new InvalidOperationException("Retaining visual repair changed a scene collider/transform/physical mesh SHA");
   if(supportBefore!=supportAfter||supportCount!=afterCount)throw new InvalidOperationException("Retaining visual repair changed an actual floor/stair ray sample");
   if(approaches!=JsonUtility.ToJson(sheet)||paths!=File.ReadAllText(O296+"/venue-complexes.json")||placements!=File.ReadAllText(O296+"/venue-placements.json"))throw new InvalidOperationException("Retaining visual repair changed layout/approach/floor metadata");
   if(height!=HashVenue296(AssetDatabase.GetAssetPath(layout.FinalSurface))||water!=HashVenue296(O296+"/../Watershed295/Generated/waterlevel.bytes"))throw new InvalidOperationException("Retaining visual repair changed terrain/water");
   report.Add("PASS all venue collider components/transforms/physical mesh SHA256 unchanged; all floors/stairs therefore unchanged. Layout, approaches, compound path metadata, terrain and water hashes unchanged.");
   report.Add("PASS unchanged static floor/stair support ray samples="+supportCount);
   Save292();File.WriteAllLines(O296+"/retaining-visual-refresh.txt",report);
   var receipt=new RetainingRefreshReceipt296{Utc=DateTime.UtcNow.ToString("O"),Scene=Scene296,BeforeColliderSha256=RetainingTextHash296(before),AfterColliderSha256=RetainingTextHash296(after),BeforeSupportSha256=supportBefore,AfterSupportSha256=supportAfter,HeightSha256=height,WaterSha256=water,SupportSamples=supportCount,ColliderCount=Session292().gameObject.scene.GetRootGameObjects().Sum(g=>g.GetComponentsInChildren<Collider>(true).Length),CollidersUnchanged=true,SupportUnchanged=true,MetadataUnchanged=true};
   File.WriteAllText(O296+"/retaining-visual-refresh.json",JsonUtility.ToJson(receipt,true));return string.Join("\n",report);
  }
 }
}
