using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] sealed class VenuePlacementReceipt296
  {
   public string Id,PlaceId,Realm,Method;public Vector3 Requested,Actual;public Vector2 ClearSize;public float Floor,GroundMin,GroundMax,Yaw;
   public int SourcePieces,NearTriangles;public Vector3[] Approach;public string[] Sources;
  }
  [Serializable] sealed class VenuePlacementLedger296 {public string SourceHeightSha256,Protection;public VenuePlacementReceipt296[] Venues;}
  sealed class VenuePlan296
  {
   public string Id,Realm,Label,Place;public Vector3 Requested,Centre;public Vector2 Clear;public float Yaw,Height;public bool Interior,Organic;
  }
  static void Venues296(List<string> report)
  {
   if(Session292().gameObject.scene.path!=Scene296)throw new Exception("Venue authoring is candidate296 only.");
   // Each invocation rebuilds from current immutable source/export inputs, even in one Editor domain.
   complexReduced296.Clear();burnedComplex296.Clear();venueModules296.Clear();regionalStone296.Clear();
   var old=Root296("Architecture296_Venues");if(old!=null)Object.DestroyImmediate(old);
   var root=new GameObject("Architecture296_Venues");var layout=Session292().MountainLayout;var field=new CompactWorldSurface(layout);
   var plans=new[]{
    new VenuePlan296{Id="sanctuary296",Realm="cheongrim",Label="성역 폐사찰",Place="sanctuary",Requested=new Vector3(3210,0,3550),Centre=new Vector3(3210,0,3550),Clear=new Vector2(70,70),Organic=true},
    new VenuePlan296{Id="camp296",Realm="jeokro",Label="불탄 군영·위령 마당",Place="arena_jeokro296",Requested=new Vector3(2260,0,930),Centre=new Vector3(2380,0,950),Clear=new Vector2(80,70)},
    new VenuePlan296{Id="arsenal296",Realm="cheolong",Label="철옹 군기전",Place="arena_cheolong296",Requested=new Vector3(510,0,3340),Centre=new Vector3(670,0,3440),Clear=new Vector2(60,45),Interior=true,Height=18,Yaw=180},
    new VenuePlan296{Id="watercourt296",Realm="hyeongang",Label="침수 궁성 수변 마당",Place="arena_hyeongang296",Requested=new Vector3(2112,0,4632),Centre=new Vector3(2163.85364f,0,4534.98866f),Clear=new Vector2(70,60),Yaw=61.875f},
    new VenuePlan296{Id="palace296",Realm="hwanggyeong",Label="황경 대전",Place="palace",Requested=new Vector3(2000,0,3380),Centre=new Vector3(1860,0,3460),Clear=new Vector2(60,50),Interior=true,Height=20}
   };
   complexBuildings296.Clear();complexPaths296.Clear();var receipts=new List<VenuePlacementReceipt296>();var arenas=new List<CompactArchitectureSheetSO.Arena>();var structures=new List<CompactArchitectureSheetSO.Structure>();
   foreach(var plan in plans)
   {
    var go=new GameObject(plan.Id);go.transform.SetParent(root.transform,false);go.transform.SetPositionAndRotation(plan.Centre,Quaternion.Euler(0,plan.Yaw,0));
    float min=float.PositiveInfinity,max=float.NegativeInfinity;
    for(float x=-plan.Clear.x*.5f-8;x<=plan.Clear.x*.5f+8;x+=2)for(float z=-plan.Clear.y*.5f-8;z<=plan.Clear.y*.5f+8;z+=2)
    {var p=go.transform.TransformPoint(new Vector3(x,0,z));float h=GroundVenue296(field,p.x,p.z);min=Mathf.Min(min,h);max=Mathf.Max(max,h);}
    float floor=plan.Organic?GroundVenue296(field,plan.Centre.x,plan.Centre.z):max+.32f;if(plan.Realm=="hyeongang")floor=Mathf.Max(153.8f,floor);
    go.transform.position=new Vector3(plan.Centre.x,floor,plan.Centre.z);var batch=new VenueBatch296(plan.Id,go.transform){StoneStyle=plan.Realm};
    if(plan.Organic)OrganicSanctuary296(plan,go.transform,field,batch);
    else
    {
     TerraceVenue296(plan,go.transform,field,batch);
     if(plan.Interior)InteriorVenue296(plan,go.transform,batch,report);else CourtVenue296(plan,go.transform,batch);
    }
    CompoundVenue296(plan,go.transform,field,structures,report);
    batch.Save();
    Vector3 entrance=go.transform.TransformPoint(new Vector3(0,.03f,-plan.Clear.y*.5f-8));
    var approach=ApproachVenue296(plan,go.transform,field,entrance,report);
    var arena=new CompactArchitectureSheetSO.Arena{Id=plan.Id,Realm=plan.Realm,PlaceId=plan.Place,Label=plan.Label,SceneRoot="Architecture296_Venues/"+plan.Id,
     Centre=go.transform.position,ClearSize=plan.Clear,ClearHeight=plan.Interior?plan.Height:40,Interior=plan.Interior,Yaw=plan.Yaw,Entrance=entrance,
     Exit=go.transform.TransformPoint(new Vector3(0,.03f,plan.Clear.y*.5f+8)),SafePoint=plan.Organic?approach[0]:entrance+go.transform.forward*3,Approach=approach,
     EncounterId=plan.Organic?"cheongryong":"",CheckpointId=plan.Organic?"sanctuary_rest251":"",InteractionIds=plan.Organic?new[]{"sanctuary_rest251"}:Array.Empty<string>(),TestOnly=!plan.Organic};
    arenas.Add(arena);var rs=go.GetComponentsInChildren<Renderer>(true);var bounds=rs[0].bounds;foreach(var r in rs)bounds.Encapsulate(r.bounds);
    structures.Add(new CompactArchitectureSheetSO.Structure{Id="venue_"+plan.Id,Realm=plan.Realm,PlaceId=plan.Place,Label=plan.Label,SourceId="venue_"+ModuleVenue296(Jeju296+"Floors/Floor_stone_2.prefab").Id,SceneRoot=arena.SceneRoot,Position=go.transform.position,Yaw=plan.Yaw,Size=bounds.size,RouteIds=new[]{"approach_"+plan.Id}});
    UpdateVenuePlace296(plan,arena,layout);
    receipts.Add(new VenuePlacementReceipt296{Id=plan.Id,PlaceId=plan.Place,Realm=plan.Realm,Method=plan.Organic?"Existing boss/dome and central ground preserved; grounded perimeter ruins only.":"Owned modular roof/wall/rafter/stone components; stepped retaining courses reach unchanged terrain. New test venue anchor is separate from protected mountain trail.",Requested=plan.Requested,Actual=go.transform.position,ClearSize=plan.Clear,Floor=floor,GroundMin=min,GroundMax=max,Yaw=plan.Yaw,SourcePieces=batch.Pieces,NearTriangles=batch.NearTriangles,Approach=approach,Sources=batch.Sources.OrderBy(s=>s).ToArray()});
    report.Add(plan.Id+" centre="+go.transform.position+" yaw="+plan.Yaw+" clear="+plan.Clear+" source pieces="+batch.Pieces+" body triangles="+batch.NearTriangles+" terrain="+min.ToString("F2")+".."+max.ToString("F2")+"; approach samples="+approach.Length);
   }
   var sheet=Sheet296();sheet.Arenas=arenas.ToArray();sheet.Structures=sheet.Structures.Where(s=>!s.Id.StartsWith("venue_",StringComparison.Ordinal)).Concat(structures).ToArray();EditorUtility.SetDirty(sheet);
   EditorUtility.SetDirty(layout);SaveVenueSources296();SaveComplexLedger296();File.WriteAllText(O296+"/venue-placements.json",JsonUtility.ToJson(new VenuePlacementLedger296{SourceHeightSha256=HashVenue296(AssetDatabase.GetAssetPath(layout.FinalSurface)),Protection="Original terrain/water/293 paths/295 assets and functional actor transforms unchanged. Later encounters are TestOnly without reward or unlock.",Venues=receipts.ToArray()},true));
   CanonicalizeVenueMaterials296(report);
  }
  static void TerraceVenue296(VenuePlan296 plan,Transform root,CompactWorldSurface field,VenueBatch296 batch)
  {
   float width=plan.Clear.x+16,depth=plan.Clear.y+16;
   for(float x=-width*.5f;x<width*.5f-.01f;x+=3)for(float z=-depth*.5f;z<depth*.5f-.01f;z+=3)
   {float w=Mathf.Min(3,width*.5f-x),d=Mathf.Min(3,depth*.5f-z);batch.Add(Jeju296+"Floors/Floor_stone_2.prefab",new Vector3(x+w*.5f,-.28f,z+d*.5f),new Vector3(w,.3f,d));}
   // Masonry breaks into short courses stepped back against the hillside. No giant monolithic slab.
   foreach(int side in new[]{-1,1})
   {
    for(float x=-width*.5f;x<width*.5f-.01f;x+=4)
    {float w=Mathf.Min(4,width*.5f-x);RetainVenue296(root,field,batch,new Vector3(x+w*.5f,0,side*(depth*.5f-.4f)),w,0);}
    for(float z=-depth*.5f;z<depth*.5f-.01f;z+=4)
    {float d=Mathf.Min(4,depth*.5f-z);RetainVenue296(root,field,batch,new Vector3(side*(width*.5f-.4f),0,z+d*.5f),d,90);}
   }
  }
  static void RetainVenue296(Transform root,CompactWorldSurface field,VenueBatch296 batch,Vector3 position,float width,float yaw)
  {
   RetainingColumn296(root,field,batch,position,width,yaw,0,1.15f,3,.95f);
  }
  static void InteriorVenue296(VenuePlan296 plan,Transform root,VenueBatch296 batch,List<string> report,bool retainMainRoof=false)
  {
   batch.InteriorRealm=plan.Realm;
   float halfX=plan.Clear.x*.5f+5,halfZ=plan.Clear.y*.5f+5,h=plan.Height;
   // Four axial openings preserve entry, retreat and perimeter circulation. Columns remain outside clear rectangle.
   foreach(int side in new[]{-1,1})
   {
    int nx=Mathf.CeilToInt(halfX*2/3);for(int i=0;i<nx;i++)
    {float x=-halfX+(i+.5f)*halfX*2/nx;if(Mathf.Abs(x)<5.8f)continue;WallBayVenue296(batch,new Vector3(x,0,side*halfZ),halfX*2/nx,h,0);}
    int nz=Mathf.CeilToInt(halfZ*2/3);for(int i=0;i<nz;i++)
    {float z=-halfZ+(i+.5f)*halfZ*2/nz;if(Mathf.Abs(z)<5.8f)continue;WallBayVenue296(batch,new Vector3(side*halfX,0,z),halfZ*2/nz,h,90);}
   }
   for(float x=-halfX;x<=halfX+.1f;x+=5)
   {
    if(Mathf.Abs(x)>=5.8f)foreach(int side in new[]{-1,1})ColumnVenue296(batch,new Vector3(x,0,side*halfZ),h);
    // Trusses are all above the specified clear height; the central combat area has no posts.
    BeamVenue296(batch,new Vector3(x,h+.50f,-halfZ),new Vector3(x,h+.50f,halfZ),.70f,.95f);
    float crest=h+Mathf.Max(1.0f,1.2f+12*Mathf.Pow(Mathf.Clamp01((plan.Clear.x*.5f+11-Mathf.Abs(x))/(plan.Clear.y*.5f+11)),1.2f)-1.0f);
    BeamVenue296(batch,new Vector3(x,h+.9f,-halfZ),new Vector3(x,crest,0),.8f,.7f);
    BeamVenue296(batch,new Vector3(x,crest,0),new Vector3(x,h+.9f,halfZ),.8f,.7f);
    batch.Add(Jeju296+"Building_Pillars/RedPillarThick.prefab",new Vector3(x,h+.9f,0),new Vector3(.7f,Mathf.Max(.10f,crest-h-.9f),.7f));
   }
   foreach(int side in new[]{-1,1})for(float z=-halfZ+5;z<halfZ-1;z+=5)if(Mathf.Abs(z)>=5.8f)ColumnVenue296(batch,new Vector3(side*halfX,0,z),h);
   foreach(int side in new[]{-1,1})foreach(int jamb in new[]{-1,1}){ColumnVenue296(batch,new Vector3(jamb*6.6f,0,side*halfZ),h);ColumnVenue296(batch,new Vector3(side*halfX,0,jamb*6.6f),h);}
   foreach(int side in new[]{-1,1})for(float x=-halfX;x<halfX-.1f;x+=3)
   {float w=Mathf.Min(3,halfX-x);batch.Add(Haeng296+"SM_R_Dancheong_4.prefab",new Vector3(x+w*.5f,h-.8f,side*halfZ),new Vector3(.32f,.75f,w),90);}
   foreach(int side in new[]{-1,1})for(float z=-halfZ;z<halfZ-.1f;z+=3)
   {float d=Mathf.Min(3,halfZ-z);batch.Add(Haeng296+"SM_R_Dancheong_4.prefab",new Vector3(side*halfX,h-.8f,z+d*.5f),new Vector3(.32f,.75f,d));}
   foreach(int side in new[]{-1,1})
   {PortalVenue296(batch,new Vector3(0,0,side*halfZ),h,0);PortalVenue296(batch,new Vector3(side*halfX,0,0),h,90);}
   if(!retainMainRoof){var roof=new GameObject("TiledHippedRoof");roof.transform.SetParent(root,false);RoofVenue296(plan.Id,roof.transform,plan.Clear.x+22,plan.Clear.y+22,h,report);}
   EaveBeltVenue296(plan.Id,root,halfX,halfZ,h*.5f,batch,report);
   // Native roof ridge ornaments retain metre-scale dimensions.
   for(float x=-Mathf.Max(0,(plan.Clear.x-plan.Clear.y)*.5f);x<=Mathf.Max(0,(plan.Clear.x-plan.Clear.y)*.5f);x+=2.8f)
    batch.Add(Haeng296+"SM_R_MiddleRoof_1.prefab",new Vector3(x,h+13.05f,0),new Vector3(2.83f,1.35f,.47f));
  }
  static void WallBayVenue296(VenueBatch296 batch,Vector3 position,float width,float height,float yaw)
  {
   batch.Add(Jeju296+"Building_Walls/WallSetD.prefab",position,new Vector3(width,3,.23f),yaw);
   batch.Add(Jeju296+"Building_Walls/WallSetC.prefab",position+Vector3.up*3,new Vector3(width,1.26f,.23f),yaw);
   UpperWallVenue296(batch,position,width,4.26f,height,yaw);
  }
  static void UpperWallVenue296(VenueBatch296 batch,Vector3 position,float width,float start,float height,float yaw)
  {
   // The sealed structural shell is unchanged. New render-only infill has no door hardware.
   bool oldCollision=batch.CollisionOnly,oldVisual=batch.VisualsOnly;
   batch.CollisionOnly=true;LegacyUpperShell296(batch,position,width,start,height,yaw);batch.CollisionOnly=oldCollision;
   batch.VisualsOnly=true;UpperInfill296(batch,position,width,start,height,yaw);batch.VisualsOnly=oldVisual;
  }
  static void LegacyUpperShell296(VenueBatch296 batch,Vector3 position,float width,float start,float height,float yaw)
  {
   float cursor=start;
   foreach(float band in new[]{6.2f,height*.67f,height-2.0f})
   {
    if(band<start-.01f)continue;
    Plaster(cursor,band);batch.Add(Jeju296+"Building_Walls/WallSetC.prefab",position+Vector3.up*band,new Vector3(width,1.26f,.30f),yaw);
    batch.Add(Haeng296+"SM_R_Beam_19.prefab",position+Vector3.up*(band+1.26f),new Vector3(width,.22f,.34f),yaw);cursor=band+1.48f;
   }
   Plaster(cursor,height-.25f);
   void Plaster(float from,float to)
   {
    for(float y=from;y<to-.01f;y+=2.8f)
    {float size=Mathf.Min(2.8f,to-y);batch.Add(Haeng296+"SM_W_White.prefab",position+Vector3.up*y,new Vector3(.23f,size,width),yaw+90);batch.Add(Haeng296+"SM_R_Beam_19.prefab",position+Vector3.up*y,new Vector3(width,.19f,.29f),yaw);}
   }
  }

  static void PortalVenue296(VenueBatch296 batch,Vector3 position,float height,float yaw)
  {
   var across=Quaternion.Euler(0,yaw,0)*Vector3.right;
   BeamVenue296(batch,position-across*6.6f+Vector3.up*5.075f,position+across*6.6f+Vector3.up*5.075f,.85f,.7f);
   for(int i=0;i<4;i++)UpperWallVenue296(batch,position+across*(-4.35f+i*2.9f),2.9f,5.5f,height,yaw);
  }
  static void BeamVenue296(VenueBatch296 batch,Vector3 a,Vector3 b,float height,float thickness)
  {
   var module=ModuleVenue296(Haeng296+"SM_R_Beam_19.prefab");var size=module.Bounds.size;int count=Mathf.CeilToInt(Vector3.Distance(a,b)/2.45f);
   for(int i=0;i<count;i++)
   {var first=Vector3.Lerp(a,b,i/(float)count);var last=Vector3.Lerp(a,b,(i+1)/(float)count);var matrix=Matrix4x4.TRS((first+last)*.5f,Quaternion.FromToRotation(Vector3.right,(last-first).normalized),new Vector3((Vector3.Distance(first,last)+.025f)/size.x,height/size.y,thickness/size.z))*Matrix4x4.Translate(-module.Bounds.center);batch.Add(module,matrix,true);}
  }

  static void ColumnVenue296(VenueBatch296 batch,Vector3 p,float height)
  {
   float diameter=height>12?1.15f:.72f,baseWidth=height>12?1.6f:1.2f;
   batch.Add(Haeng296+"SM_S_Circle_1.prefab",p,new Vector3(baseWidth,.4f,baseWidth));
   batch.Add(Jeju296+"Building_Pillars/RedPillarThick.prefab",p+Vector3.up*.4f,new Vector3(diameter,height-.4f,diameter));
   batch.Add(Haeng296+"SM_R_Beam_1.prefab",p+Vector3.up*(height-.65f),new Vector3(1.96f,1.01f,.47f));
  }
  static void CourtVenue296(VenuePlan296 plan,Transform root,VenueBatch296 batch)
  {
   float x=plan.Clear.x*.5f+5,z=plan.Clear.y*.5f+5;
   foreach(int side in new[]{-1,1})
   {
    for(float a=-x;a<x-.1f;a+=3)
    {float w=Mathf.Min(3,x-a);if(Mathf.Abs(a+w*.5f)<5.5f)continue;batch.Add(Jeju296+"Building_Walls/OuterWall_Middle.prefab",new Vector3(a+w*.5f,.02f,side*z),new Vector3(w,1.75f,.5f));}
    for(float a=-z;a<z-.1f;a+=3)
    {float d=Mathf.Min(3,z-a);if(Mathf.Abs(a+d*.5f)<13f)continue;batch.Add(Jeju296+"Building_Walls/OuterWall_Middle.prefab",new Vector3(side*x,.02f,a+d*.5f),new Vector3(d,1.75f,.5f),90);}
   }
   if(plan.Realm=="jeokro")
   {
    // Broken frames use a private charred variant; original maps and source materials stay unchanged.
    batch.Charred=true;
    foreach(int side in new[]{-1,1})for(int bay=0;bay<7;bay++)
    {
     float zz=-z+5+bay*8;if(Mathf.Abs(zz-4)<4||Mathf.Abs(zz+10)<4)continue;ColumnVenue296(batch,new Vector3(side*(x-1.1f),.02f,zz),bay%3==0?2.2f:4.2f);
     batch.Add(Haeng296+"SM_R_Beam_10.prefab",new Vector3(side*(x-1.1f),bay%3==0?1.0f:3.9f,zz+2),new Vector3(4.45f,.82f,.33f),bay%3==0?side*24:90);
     if(bay%3==1)batch.Add(Haeng296+"SM_R_RoofCorner_1.prefab",new Vector3(side*(x-1),4.0f,zz+1),new Vector3(2.98f,1.68f,3.3f),side*90);
    }
   }
   else
   {
    batch.Charred=false;foreach(int side in new[]{-1,1})
    {batch.Add(Haeng296+"SM_Naeposa.prefab",new Vector3(side*(x-1),.02f,z-2),new Vector3(6.23f,6.67f,8.74f),180);batch.Add(Haeng296+"SM_Hongsalmun.prefab",new Vector3(0,.02f,side*z),new Vector3(8.79f,7.44f,.94f));}
   }
  }
  static void OrganicSanctuary296(VenuePlan296 plan,Transform root,CompactWorldSurface field,VenueBatch296 batch)
  {
   float half=plan.Clear.x*.5f+7;
   foreach(int side in new[]{-1,1})for(int bay=0;bay<10;bay++)
   {
    float z=-half+5+bay*8;if(Mathf.Abs(z)<7)continue;var p=root.TransformPoint(new Vector3(side*half,0,z));float y=GroundVenue296(field,p.x,p.z)-root.position.y;
    batch.Add(Jeju296+"Building_Walls/OuterWall_Middle.prefab",new Vector3(side*half,y-.1f,z),new Vector3(2.85f,bay%3==0?.85f:1.7f,.55f),90);
    if(bay%3==1){ColumnVenue296(batch,new Vector3(side*(half+2.3f),y,z),3.4f);batch.Add(Haeng296+"SM_R_RoofCorner_1.prefab",new Vector3(side*(half+2.3f),y+3.2f,z),new Vector3(2.98f,1.68f,3.3f),side*90);}
   }
   foreach(int side in new[]{-1,1})
   {var p=root.TransformPoint(new Vector3(side*25,0,half+8));float y=GroundVenue296(field,p.x,p.z)-root.position.y;batch.Add(Haeng296+"SM_Naeposa.prefab",new Vector3(side*25,y,half+8),new Vector3(6.23f,6.67f,8.74f),180);}
  }
  static void UpdateVenuePlace296(VenuePlan296 plan,CompactArchitectureSheetSO.Arena arena,CompactWorldLayoutSO layout)
  {
   if(plan.Organic)return;var places=layout.Places.ToList();var place=places.FirstOrDefault(p=>p.Id==plan.Place);
   if(place==null){place=new CompactWorldLayoutSO.Place{Id=plan.Place};places.Add(place);}
   place.Realm=plan.Realm;place.Label=plan.Label;place.Purpose="Architecture296 test venue; no new encounter, progression or reward.";place.XZ=new Vector2(arena.Centre.x,arena.Centre.z);place.GroundRadius=Mathf.Max(arena.ClearSize.x,arena.ClearSize.y)*.5f;
   place.SourceAnchor=arena.Centre;place.HasSourceBinding=true;place.SceneRoots=new[]{arena.SceneRoot};layout.Places=places.ToArray();
   var routes=layout.Routes.Where(r=>r.Id!="approach_"+plan.Id).ToList();routes.Add(new CompactWorldLayoutSO.Route{Id="approach_"+plan.Id,From=plan.Realm=="cheolong"?"mountain_cheolong_summit":plan.Realm=="jeokro"?"mountain_jeokro_summit":plan.Realm=="hyeongang"?"mountain_hyeongang_foot":"capital_center",To=plan.Place,Role=CompactRouteRole.Exploration,Width=4,Traversal=CompactTraversal.FootOnly,Bends=arena.Approach.Select(p=>new Vector2(p.x,p.z)).ToArray()});layout.Routes=routes.ToArray();
  }
  static Vector3[] ApproachVenue296(VenuePlan296 plan,Transform root,CompactWorldSurface field,Vector3 entrance,List<string> report)
  {
   if(plan.Organic){var p=root.position+Vector3.back*43;p.y=GroundVenue296(field,p.x,p.z)+.06f;return new[]{p,root.position+Vector3.back*30};}
   List<Vector3> controls;
   if(plan.Realm=="cheolong")
   {controls=ContourVenue296(field,new Vector3(510,0,3340),new Vector3(670,0,3512),new Rect(610,3382,120,116));controls.Add(new Vector3(670,0,3490));controls.Add(entrance);controls=SmoothContourVenue296(controls);}
   else if(plan.Realm=="jeokro")controls=new List<Vector3>{new Vector3(2260,0,930),new Vector3(2300,0,910),new Vector3(2340,0,880),new Vector3(2380,0,880),entrance};
   else if(plan.Realm=="hyeongang")controls=new List<Vector3>{new Vector3(2112,155.6006f,4484),new Vector3(2120,0,4500),entrance};
   else controls=new List<Vector3>{new Vector3(2000,0,3380),new Vector3(1940,0,3360),new Vector3(1900,0,3380),new Vector3(1860,0,3400),new Vector3(1860,0,3410),entrance};
   if(plan.Realm!="cheolong")controls=SmoothContourVenue296(controls);
   var points=new List<Vector3>();
   for(int i=1;i<controls.Count;i++)
   {
    var a=controls[i-1];var b=controls[i];float length=Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));int count=Mathf.Max(1,Mathf.CeilToInt(length/.55f));
    for(int k=0;k<count;k++){var p=Vector3.Lerp(a,b,k/(float)count);p.y=GroundVenue296(field,p.x,p.z)+.10f;if(plan.Realm=="hyeongang")p.y=Mathf.Max(p.y,Mathf.Lerp(155.6006f,entrance.y,(i-1+k/(float)count)/(controls.Count-1)));points.Add(p);}
   }points.Add(entrance);
   // Grade a visible stone stair profile above the unchanged terrain, with the entry level fixed.
   float start=points[0].y;
   for(int i=points.Count-2;i>=0;i--){float d=Vector2.Distance(new Vector2(points[i].x,points[i].z),new Vector2(points[i+1].x,points[i+1].z));var p=points[i];p.y=Mathf.Max(p.y,points[i+1].y-.4f*d);points[i]=p;}
   for(int i=1;i<points.Count;i++){float d=Vector2.Distance(new Vector2(points[i].x,points[i].z),new Vector2(points[i-1].x,points[i-1].z));var p=points[i];p.y=Mathf.Max(p.y,points[i-1].y-.4f*d);points[i]=p;}
   if(points[0].y-start>.21f||Mathf.Abs(points[points.Count-1].y-entrance.y)>.21f)throw new Exception(plan.Id+" approach grade cannot meet endpoints without changing ground: start delta="+(points[0].y-start)+", end="+(points[points.Count-1].y-entrance.y));
   var go=new GameObject("Approach_"+plan.Id);go.transform.SetParent(root.parent,false);var batch=new VenueBatch296("Approach_"+plan.Id,go.transform){StoneStyle=plan.Realm};var temporaryTreads=new List<Mesh>();
   try
   {
   for(int i=1;i<points.Count;i++)
   {
    var a=points[i-1];var b=points[i];float length=Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));var p=(a+b)*.5f;p.y=Mathf.Max(a.y,b.y)-.20f;float yaw=Mathf.Atan2(b.x-a.x,b.z-a.z)*Mathf.Rad2Deg;
    AddJoinedApproachTread296(batch,points,i,plan.Realm,temporaryTreads);
    if(i%12==0){float ground=GroundVenue296(field,p.x,p.z);if(p.y-ground>.7f)batch.Add(GateKit296+"SM_Bastion_001.prefab",new Vector3(p.x,ground-.2f,p.z),new Vector3(3.9f,p.y-ground+.2f,.65f),yaw);}
   }
   batch.Save();
   }
   finally{foreach(var tread in temporaryTreads)Object.DestroyImmediate(tread);}
   var physical=points.ToArray();
   // The graded points describe riser boundaries. The actual overlapping stone tread at each
   // boundary is the higher adjacent step, which must also be the navigation/reference height.
   for(int i=0;i<physical.Length;i++){float y=points[i].y;if(i>0)y=Mathf.Max(y,points[i-1].y);if(i+1<points.Count)y=Mathf.Max(y,points[i+1].y);physical[i].y=y;}
   report.Add(plan.Id+" grounded visible stair approach: "+points.Count+" samples, max designed grade0.4, original endpoint delta="+(points[0].y-start).ToString("F3")+"; metadata uses actual adjacent tread tops.");return physical;
  }
  static List<Vector3> SmoothContourVenue296(List<Vector3> controls)
  {
   // The old4m grid hairpins overlapped their own4m-wide stone treads. Smooth arc length,
   // then approach the gate from its north side so the final approach never doubles back.
   const float spacing=.4f,sigma=3;const int radius=22;var arc=new List<float>{0};
   for(int i=1;i<controls.Count;i++)arc.Add(arc[i-1]+Vector2.Distance(new Vector2(controls[i-1].x,controls[i-1].z),new Vector2(controls[i].x,controls[i].z)));
   var sample=new List<Vector3>();var distances=new List<float>();int segment=1;
   for(float d=0;d<arc[arc.Count-1];d+=spacing){while(segment<arc.Count-1&&arc[segment]<d)segment++;sample.Add(Vector3.Lerp(controls[segment-1],controls[segment],Mathf.InverseLerp(arc[segment-1],arc[segment],d)));distances.Add(d);}
   sample.Add(controls[controls.Count-1]);distances.Add(arc[arc.Count-1]);var result=new List<Vector3>();
   for(int i=0;i<sample.Count;i++)
   {
    Vector3 point=Vector3.zero;float total=0;for(int k=-radius;k<=radius;k++){float weight=Mathf.Exp(-.5f*k*k*spacing*spacing/(sigma*sigma));point+=sample[Mathf.Clamp(i+k,0,sample.Count-1)]*weight;total+=weight;}
    float blend=Mathf.Clamp01(Mathf.Min(distances[i]/8,(distances[distances.Count-1]-distances[i])/8));result.Add(Vector3.Lerp(sample[i],point/total,blend));
   }
   for(int i=1;i<result.Count;i++)if(result[i-1].z>3490&&result[i].z<3490&&Mathf.Abs(result[i].x-670)<.1f){result.Insert(i,new Vector3(670,0,3490));break;}
   for(int i=1;i<result.Count;i++)if(result[i-1].z<3410&&result[i].z>3410&&Mathf.Abs(result[i].x-1860)<.1f){result.Insert(i,new Vector3(1860,0,3410));break;}
   result[0]=controls[0];result[result.Count-1]=controls[controls.Count-1];return result;
  }
  static List<Vector3> ContourVenue296(CompactWorldSurface field,Vector3 start,Vector3 goal,Rect forbidden)
  {
   const int minX=60,maxX=240,minZ=780,maxZ=930;var s=(x:Mathf.RoundToInt(start.x/4),z:Mathf.RoundToInt(start.z/4));var end=(x:Mathf.RoundToInt(goal.x/4),z:Mathf.RoundToInt(goal.z/4));
   var cost=new Dictionary<(int x,int z),float>{{s,0}};var prior=new Dictionary<(int x,int z),(int x,int z)>();var queue=new SortedSet<(float score,long serial,int x,int z)>();long serial=0;queue.Add((0,serial++,s.x,s.z));var closed=new HashSet<(int,int)>();bool found=false;
   while(queue.Count>0)
   {
    var first=queue.Min;queue.Remove(first);var p=(x:first.x,z:first.z);if(!closed.Add(p))continue;if(p==end){found=true;break;}
    float oldY=GroundVenue296(field,p.x*4,p.z*4);
    for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)
    {
     if(dx==0&&dz==0)continue;var n=(x:p.x+dx,z:p.z+dz);if(n.x<minX||n.x>maxX||n.z<minZ||n.z>maxZ||forbidden.Contains(new Vector2(n.x*4,n.z*4)))continue;
     float distance=4*Mathf.Sqrt(dx*dx+dz*dz),newY=GroundVenue296(field,n.x*4,n.z*4),midY=GroundVenue296(field,(p.x+n.x)*2,(p.z+n.z)*2),dy=Mathf.Abs(newY-oldY);
     if(dy>distance*.36f||Mathf.Abs(midY-oldY)>distance*.18f||Mathf.Abs(newY-midY)>distance*.18f)continue;
     float next=cost[p]+distance+dy*.2f;if(cost.TryGetValue(n,out var known)&&next>=known)continue;cost[n]=next;prior[n]=p;queue.Add((next+4*Vector2.Distance(new Vector2(n.x,n.z),new Vector2(end.x,end.z)),serial++,n.x,n.z));
    }
   }
   if(!found)throw new Exception("No grade-limited contour approach to the mountain arsenal.");
   var route=new List<Vector3>();var at=end;route.Add(new Vector3(at.x*4,0,at.z*4));while(at!=s){at=prior[at];route.Add(new Vector3(at.x*4,0,at.z*4));}route.Reverse();route[0]=start;route.Add(goal);return route;
  }
 }
}
