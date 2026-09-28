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
  [Serializable] sealed class ComplexBuildingReceipt296
  {public string Id,Realm,Source,Adaptation,SceneRoot;public Vector3 Position,NativeSize;public float Yaw;public int[] Triangles;public float ColliderTriangles;}
  [Serializable] sealed class ComplexPathReceipt296 {public string Id,Realm;public float Width;public Vector3[] Points;}
  [Serializable] sealed class ComplexLedger296 {public string Method;public ComplexBuildingReceipt296[] Buildings;public ComplexPathReceipt296[] Paths;}
  static readonly List<ComplexBuildingReceipt296> complexBuildings296=new List<ComplexBuildingReceipt296>();
  static readonly List<ComplexPathReceipt296> complexPaths296=new List<ComplexPathReceipt296>();
  static readonly Dictionary<string,Material> regionalStone296=new Dictionary<string,Material>();
  static Material RegionalStoneVenue296(Material original,string region)
  {
   if(Path.GetFileName(AssetDatabase.GetAssetPath(original)).StartsWith("Stone_"+region+"_",StringComparison.Ordinal))return original;
   string guid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original)),key=region+"_"+guid;
   if(regionalStone296.TryGetValue(key,out var found))return found;
   var copy=Asset296("Materials/Venues/Stone_"+key+".mat",()=>new Material(original));copy.CopyPropertiesFromMaterial(original);
   var tint=region=="jeokro"?new Color(.48f,.43f,.39f,1):region=="hyeongang"?new Color(.42f,.49f,.50f,1):region=="cheongrim"?new Color(.48f,.52f,.43f,1):new Color(.70f,.68f,.61f,1);
   copy.SetColor("_BaseColor",tint);copy.SetFloat("_Saturation",.18f);copy.SetFloat("_AmbientFloor",.5f);copy.name="296_"+region+"_WeatheredStone";EditorUtility.SetDirty(copy);regionalStone296[key]=copy;return copy;
  }
  static void CompoundVenue296(VenuePlan296 plan,Transform root,CompactWorldSurface field,List<CompactArchitectureSheetSO.Structure> structures,List<string> report,bool masonryOnly=false,bool keepMasonryPhysical=false)
  {
   if(plan.Interior)return;
   var prior=keepMasonryPhysical?root.Find("Compound296_SteppedMasonry"):null;
   var supports=prior!=null?prior.gameObject:new GameObject("Compound296_SteppedMasonry");if(prior==null)supports.transform.SetParent(root,false);else ClearRetainingVisuals296(prior);
   var masonry=new VenueBatch296(plan.Id+"_compound_supports",supports.transform){StoneStyle=plan.Realm};
   if(plan.Realm=="jeokro")
   {
    PatchCompound296(root,field,masonry,new Vector3(0,2.4f,58),42,31);
    if(!masonryOnly)NativeCompound296(plan,root,"BurnedMainHall",Haeng296+"SM_Naknamhyeon.prefab",new Vector3(0,2.43f,58),180,true,structures,report);
    PathCompound296(plan,root,field,masonry,"main_hall",new[]{new Vector3(0,.02f,36),new Vector3(0,2.42f,43)},5);
    PatchCompound296(root,field,masonry,new Vector3(-64,-2.4f,4),16,39);
    if(!masonryOnly)NativeCompound296(plan,root,"BurnedWestBarracks",Jeju296+"Decoration/Prop_House_02.prefab",new Vector3(-64,-2.37f,4),90,true,structures,report);
    PathCompound296(plan,root,field,masonry,"west_barracks",new[]{new Vector3(-47.8f,.02f,4),new Vector3(-49.4f,.02f,4),new Vector3(-56.2f,-2.38f,4)},3.5f);
    PatchCompound296(root,field,masonry,new Vector3(56,.8f,-10),25,24);
    if(!masonryOnly)NativeCompound296(plan,root,"BurnedEastQuarters",Jeju296+"Decoration/Prop_House_01.prefab",new Vector3(56,.83f,-10),-90,true,structures,report);
    PathCompound296(plan,root,field,masonry,"east_quarters",new[]{new Vector3(40.2f,.02f,-10),new Vector3(44,.82f,-10)},3.5f);
    FallenCompound296(plan,root,field,masonry,report);
   }
   else if(plan.Realm=="hyeongang")
   {
    PatchCompound296(root,field,masonry,new Vector3(0,2.4f,52),42,31);
    if(!masonryOnly)NativeCompound296(plan,root,"DryPrincipalHall",Haeng296+"SM_Naknamhyeon.prefab",new Vector3(0,2.43f,52),180,false,structures,report);
    PathCompound296(plan,root,field,masonry,"principal_hall",new[]{new Vector3(0,.02f,30.2f),new Vector3(0,2.42f,37)},5);
    float west=151.4f-root.position.y,east=155.4f-root.position.y;
    PatchCompound296(root,field,masonry,new Vector3(-58,west,5),16,44);
    if(!masonryOnly)CorridorCompound296(plan,root,"LowerWestGallery",new Vector3(-58,west+.03f,5),40,90,false,structures,report);
    PathCompound296(plan,root,field,masonry,"west_gallery",new[]{new Vector3(-42.8f,.02f,-7),new Vector3(-43.4f,.02f,-7),new Vector3(-50.2f,west+.02f,-7)},3.5f);
    PatchCompound296(root,field,masonry,new Vector3(54,east,-10),16,44);
    if(!masonryOnly)CorridorCompound296(plan,root,"UpperEastGallery",new Vector3(54,east+.03f,-10),40,90,false,structures,report);
    PathCompound296(plan,root,field,masonry,"east_gallery",new[]{new Vector3(36,.02f,-10),new Vector3(46,east+.02f,-10)},3.5f);
    PathCompound296(plan,root,field,masonry,"existing_kaesong_middle_court",new[]{new Vector3(-66,west+.02f,-7),new Vector3(-70,151.85f-root.position.y,-4.55f),new Vector3(-82,153.43f-root.position.y,-4.55f)},3.4f);
    // A lower ruin is visibly submerged. It is outside the dry routes and is not a hidden platform.
    float sunk=147.7f-root.position.y;PatchCompound296(root,field,masonry,new Vector3(-76,sunk,25),15,29);
    if(!keepMasonryPhysical)
    {
    var ruin=new GameObject("SubmergedWestAnnex");ruin.transform.SetParent(root,false);ruin.transform.localPosition=new Vector3(-76,sunk+.03f,25);
    var fragments=new VenueBatch296(plan.Id+"_sunken_annex",ruin.transform){StoneStyle=plan.Realm};
    for(int i=0;i<7;i++)foreach(int side in new[]{-1,1})
    {float z=-12+i*4;ColumnVenue296(fragments,new Vector3(side*4,0,z),i%3==0?1.9f:3.8f);if(i%3==1)fragments.Add(Haeng296+"SM_R_RoofCorner_1.prefab",new Vector3(side*4,3.5f,z),new Vector3(2.98f,1.68f,3.3f),side*90);}
    fragments.Save();
    }
    report.Add("Hyeongang dry main hall + unequal gallery levels 151.4/155.4m; new dry connector reaches original Kaesong middle court at153.43m. Sunken annex147.7m remains off the dry path.");
   }
   else
   {
    float hall=MaxPatchGround296(root,field,new Vector3(0,0,60),42,31)+.28f;
    PatchCompound296(root,field,masonry,new Vector3(0,hall,60),42,31);
    if(!masonryOnly)NativeCompound296(plan,root,"TemplePrincipalHall",Haeng296+"SM_Naknamhyeon.prefab",new Vector3(0,hall+.03f,60),180,false,structures,report);
    foreach(int side in new[]{-1,1})
    {
     var at=new Vector3(side*54,0,14);at.y=MaxPatchGround296(root,field,at,14,34)+.24f;PatchCompound296(root,field,masonry,at,14,34);
     if(!masonryOnly)CorridorCompound296(plan,root,side<0?"TempleWestGallery":"TempleEastGallery",at+Vector3.up*.03f,30,90,false,structures,report);
    }
    // A hillside stair reaches the main hall from outside the protected boss rectangle.
    var start=new Vector3(-49,0,60);var world=root.TransformPoint(start);start.y=GroundVenue296(field,world.x,world.z)-root.position.y+.08f;
    PathCompound296(plan,root,field,masonry,"temple_hillside",new[]{start,new Vector3(-22,hall+.02f,60)},3.4f);
    report.Add("Sanctuary main temple and flanking galleries are outside the central70x70m dome/boss ground. Terrain, actors, sacred tree and the original encounter remain unchanged.");
   }
   WeatherCompound296(plan,root,masonry);masonry.Save(keepMasonryPhysical);
  }
  static float MaxPatchGround296(Transform root,CompactWorldSurface field,Vector3 centre,float width,float depth)
  {
   float max=float.NegativeInfinity;for(float x=-width*.5f;x<=width*.5f;x+=2)for(float z=-depth*.5f;z<=depth*.5f;z+=2)
   {var world=root.TransformPoint(centre+new Vector3(x,0,z));max=Mathf.Max(max,GroundVenue296(field,world.x,world.z)-root.position.y);}return max;
  }
  static void PatchCompound296(Transform root,CompactWorldSurface field,VenueBatch296 batch,Vector3 centre,float width,float depth)
  {
   for(float x=-width*.5f;x<width*.5f-.01f;x+=3)for(float z=-depth*.5f;z<depth*.5f-.01f;z+=3)
   {float w=Mathf.Min(3,width*.5f-x),d=Mathf.Min(3,depth*.5f-z);batch.Add(Jeju296+"Floors/Floor_stone_2.prefab",centre+new Vector3(x+w*.5f,-.28f,z+d*.5f),new Vector3(w,.30f,d));}
   foreach(int side in new[]{-1,1})
   {
    for(float x=-width*.5f;x<width*.5f-.01f;x+=3.5f){float w=Mathf.Min(3.5f,width*.5f-x);Course(centre+new Vector3(x+w*.5f,0,side*(depth*.5f-.4f)),w,0);}
    for(float z=-depth*.5f;z<depth*.5f-.01f;z+=3.5f){float d=Mathf.Min(3.5f,depth*.5f-z);Course(centre+new Vector3(side*(width*.5f-.4f),0,z+d*.5f),d,90);}
   }
   void Course(Vector3 local,float span,float yaw)
   {
    RetainingColumn296(root,field,batch,local,span,yaw,centre.y,1.05f,2.8f,.9f);
   }
  }
  static void NativeCompound296(VenuePlan296 plan,Transform root,string id,string source,Vector3 local,float yaw,bool burned,List<CompactArchitectureSheetSO.Structure> structures,List<string> report)
  {
   var module=ComplexModule296(source);var adapted=burned?BurnedComplex296(module):module;var go=new GameObject(id);go.transform.SetParent(root,false);go.transform.localPosition=local;go.transform.localRotation=Quaternion.Euler(0,yaw,0);
   var batch=new VenueBatch296(plan.Id+"_"+id,go.transform){Charred=burned,StoneStyle=plan.Realm};var normalise=Matrix4x4.Translate(new Vector3(-module.Bounds.center.x,-module.Bounds.min.y,-module.Bounds.center.z));
   batch.Add(adapted,normalise,false);var collision=ComplexCollision296(source,burned,module.Bounds);batch.AddCollision(collision,normalise);batch.Save();
   string scene="Architecture296_Venues/"+plan.Id+"/"+id;var counts=Enumerable.Range(0,3).Select(l=>adapted.Parts.Sum(p=>p.Meshes[l].triangles.Length/3)).ToArray();
   structures.Add(new CompactArchitectureSheetSO.Structure{Id="venue_"+plan.Id+"_"+id,Realm=plan.Realm,PlaceId=plan.Place,Label=id,SourceId="venue_"+module.Id,SceneRoot=scene,Position=go.transform.position,Yaw=root.eulerAngles.y+yaw,Size=module.Bounds.size,RouteIds=new[]{"approach_"+plan.Id}});
   complexBuildings296.Add(new ComplexBuildingReceipt296{Id=id,Realm=plan.Realm,Source=source,SceneRoot=scene,Position=go.transform.position,Yaw=root.eulerAngles.y+yaw,NativeSize=module.Bounds.size,Triangles=counts,ColliderTriangles=collision.triangles.Length/3,Adaptation=burned?"Native unit scale; charred original texture variant; upper corner roof loss in all levels; independent structural collision.":"Native unit scale, original near mesh/normals/UV; only roof QEM; coplanar structural dissolve preserves boundaries; independent structural collision."});
   report.Add(plan.Id+" "+id+" native "+module.Bounds.size+"m, unit scale; triangles="+string.Join("/",counts)+"; independent physical="+(collision.triangles.Length/3));
  }
  static readonly Dictionary<string,VenueModule296> burnedComplex296=new Dictionary<string,VenueModule296>();
  static VenueModule296 BurnedComplex296(VenueModule296 source)
  {
   if(burnedComplex296.TryGetValue(source.Path,out var cached))return cached;var parts=new List<VenuePart296>();int pi=0;
   foreach(var part in source.Parts)
   {
    var meshes=new Mesh[3];for(int level=0;level<3;level++)
    {
     var input=part.Meshes[level];var v=input.vertices;var sourceTriangles=input.triangles;var triangles=new List<int>();
     for(int t=0;t<sourceTriangles.Length;t+=3)
     {var centre=(v[sourceTriangles[t]]+v[sourceTriangles[t+1]]+v[sourceTriangles[t+2]])/3;bool lost=centre.y>source.Bounds.min.y+source.Bounds.size.y*.57f&&centre.x>source.Bounds.center.x+source.Bounds.size.x*.1f&&centre.z<source.Bounds.center.z+source.Bounds.size.z*.12f;if(!lost)triangles.AddRange(new[]{sourceTriangles[t],sourceTriangles[t+1],sourceTriangles[t+2]});}
     var mesh=Asset296("Meshes/Complex/"+source.Id+"_burnt_"+pi+"_"+level+".asset",()=>new Mesh());EditorUtility.CopySerialized(input,mesh);mesh.triangles=triangles.ToArray();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);meshes[level]=mesh;
    }
    parts.Add(new VenuePart296{Meshes=meshes,Material=part.Material});pi++;
   }
   var result=new VenueModule296{Path=source.Path,Id=source.Id,Bounds=source.Bounds,Parts=parts.ToArray()};burnedComplex296[source.Path]=result;return result;
  }
  static void CorridorCompound296(VenuePlan296 plan,Transform root,string id,Vector3 local,float length,float yaw,bool burned,List<CompactArchitectureSheetSO.Structure> structures,List<string> report)
  {
   var go=new GameObject(id);go.transform.SetParent(root,false);go.transform.localPosition=local;go.transform.localRotation=Quaternion.Euler(0,yaw,0);
   var body=new GameObject("NativePostsAndBeams");body.transform.SetParent(go.transform,false);var batch=new VenueBatch296(plan.Id+"_"+id,body.transform){Charred=burned,StoneStyle=plan.Realm};
   int bays=Mathf.CeilToInt((length-4)/3);for(int i=0;i<=bays;i++)
   {
    float x=-length*.5f+2+i*(length-4)/bays;foreach(int side in new[]{-1,1})ColumnVenue296(batch,new Vector3(x,0,side*3.05f),3.6f);
    BeamVenue296(batch,new Vector3(x,3.65f,-3.05f),new Vector3(x,3.65f,3.05f),.32f,.34f);
   }
   foreach(int side in new[]{-1,1})BeamVenue296(batch,new Vector3(-length*.5f+2,3.8f,side*3.05f),new Vector3(length*.5f-2,3.8f,side*3.05f),.30f,.36f);
   batch.Save();var roof=new GameObject("TiledGalleryRoof");roof.transform.SetParent(go.transform,false);RoofVenue296(plan.Id+"_"+id,roof.transform,length,10,3.1f,report,2.2f);
   string scene="Architecture296_Venues/"+plan.Id+"/"+id;
   structures.Add(new CompactArchitectureSheetSO.Structure{Id="venue_"+plan.Id+"_"+id,Realm=plan.Realm,PlaceId=plan.Place,Label=id,SourceId="venue_"+ModuleVenue296(Haeng296+"SM_S_Circle_1.prefab").Id,SceneRoot=scene,Position=go.transform.position,Yaw=root.eulerAngles.y+yaw,Size=new Vector3(length,7,10),RouteIds=new[]{"approach_"+plan.Id}});
   var levels=new int[3];foreach(var group in go.GetComponentsInChildren<LODGroup>())for(int l=0;l<3;l++)foreach(var renderer in group.GetLODs()[l].renderers){var mesh=renderer.GetComponent<MeshFilter>();if(mesh!=null)levels[l]+=mesh.sharedMesh.triangles.Length/3;}
   complexBuildings296.Add(new ComplexBuildingReceipt296{Id=id,Realm=plan.Realm,Source="Native pillar/beam modules and exposed original ceramic roof patch",SceneRoot=scene,Position=go.transform.position,Yaw=root.eulerAngles.y+yaw,NativeSize=new Vector3(length,7,10),Triangles=levels,ColliderTriangles=go.GetComponentsInChildren<MeshCollider>().Sum(c=>c.sharedMesh.triangles.Length/3),Adaptation="Human-scale columns and3m bays, custom continuous low-rise gallery roof, actual visible physical underside; no full building stretching."});
  }
  static void PathCompound296(VenuePlan296 plan,Transform root,CompactWorldSurface field,VenueBatch296 batch,string id,Vector3[] controls,float width)
  {
   var points=new List<Vector3>();for(int segment=1;segment<controls.Length;segment++)
   {
    var a=controls[segment-1];var b=controls[segment];int n=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z))/.45f));
    for(int k=0;k<n;k++)points.Add(Vector3.Lerp(a,b,k/(float)n));
   }points.Add(controls[controls.Length-1]);
   for(int i=1;i<points.Count;i++)
   {
    var a=points[i-1];var b=points[i];var p=(a+b)*.5f;p.y=Mathf.Max(a.y,b.y)-.20f;float run=new Vector2(b.x-a.x,b.z-a.z).magnitude;float yaw=Mathf.Atan2(b.x-a.x,b.z-a.z)*Mathf.Rad2Deg;
    batch.Add(Jeju296+"Floors/Floor_stone_2.prefab",p,new Vector3(width,.20f,run+.08f),yaw);
    if(i%9==0){var world=root.TransformPoint(p);float g=GroundVenue296(field,world.x,world.z)-root.position.y;if(p.y>g+.5f)batch.Add(GateKit296+"SM_Bastion_001.prefab",new Vector3(p.x,g-.2f,p.z),new Vector3(width-.12f,p.y-g+.2f,.6f),yaw);}
   }
   var actual=points.ToArray();for(int i=0;i<actual.Length;i++){float y=points[i].y;if(i>0)y=Mathf.Max(y,points[i-1].y);if(i+1<points.Count)y=Mathf.Max(y,points[i+1].y);actual[i]=root.TransformPoint(new Vector3(actual[i].x,y,actual[i].z));}
   complexPaths296.Add(new ComplexPathReceipt296{Id=plan.Id+"_"+id,Realm=plan.Realm,Width=width,Points=actual});
  }
  static void FallenCompound296(VenuePlan296 plan,Transform root,CompactWorldSurface field,VenueBatch296 batch,List<string> report)
  {
   batch.Charred=true;
   for(int i=0;i<17;i++)
   {
    float x=-30+i*3.7f,z=39.2f+(i%3)*.65f;var a=new Vector3(x,.07f,z);var b=a+Quaternion.Euler(0,17+i*37,0)*Vector3.right*2.3f;BeamVenue296(batch,a,b,.35f,.35f);
    if(i%3==0)batch.Add(Haeng296+"SM_R_RoofCorner_1.prefab",new Vector3(x,.08f,z+1.3f),new Vector3(2.98f,1.68f,3.3f),i*47);
   }
   batch.Charred=false;
   for(int i=0;i<9;i++)
   {var p=new Vector3(-31+i*3.5f,.04f,-39.5f);for(int k=0;k<5;k++)batch.Add("Assets/SeyeonjeongPavilion/Prefabs/SM_Rock_L.prefab",p+new Vector3(Mathf.Sin(k*2.4f)*.7f,.12f*k/5,Mathf.Cos(k*2.4f)*.55f),new Vector3(.921f,.537f,1.115f),k*53+i*19);}
   report.Add("Camp: partially lost upper roofs, two native barracks, fallen full-size beams/tile corners and nine stone burial heaps stay outside80x70 combat rectangle.");
  }
  static void WeatherCompound296(VenuePlan296 plan,Transform root,VenueBatch296 batch)
  {
   if(plan.Organic)return;string textureRoot="Assets/_Project/Art/World/Reworld292/Highlands293/Textures/";
   var material=Asset296("Materials/Venues/"+plan.Realm+"_ExistingCC0Mud.mat",()=>new Material(Shader.Find("Oheangbu/Reworld292/KoreanArchitecture")));
   material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(textureRoot+"brown_mud_rocks_01_diff_2k.jpg"));material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(textureRoot+"brown_mud_rocks_01_nor_gl_2k.jpg"));
   material.SetFloat("_BumpScale",.5f);material.SetFloat("_Saturation",.08f);material.SetFloat("_AmbientFloor",.52f);material.SetFloat("_LightResponse",.65f);material.SetFloat("_FadeOutStart",8000);material.SetFloat("_FadeOutEnd",10000);material.SetFloat("_WindAmplitude",0);material.SetFloat("_WashStrength",0);
   material.SetColor("_BaseColor",plan.Realm=="jeokro"?new Color(.32f,.30f,.28f,1):new Color(.28f,.35f,.34f,1));EditorUtility.SetDirty(material);
   var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
   for(int side=-1;side<=1;side+=2)for(int patch=0;patch<7;patch++)
   {
    float x=side*(plan.Clear.x*.5f+4),z=-plan.Clear.y*.5f+6+patch*(plan.Clear.y-12)/6;int first=vertices.Count;vertices.Add(new Vector3(x,.061f,z));uv.Add(new Vector2(x/2,z/2));
    for(int k=0;k<13;k++){float angle=k*Mathf.PI*2/13,r=2.2f+.4f*Mathf.Sin(k*7+patch*3);var p=new Vector3(x+Mathf.Cos(angle)*r,.055f,z+Mathf.Sin(angle)*r*1.45f);vertices.Add(p);uv.Add(new Vector2(p.x/2,p.z/2));}
    for(int k=0;k<13;k++)triangles.AddRange(new[]{first,first+1+(k+1)%13,first+1+k});
   }
   var mesh=Asset296("Meshes/Venues/"+plan.Id+"_PeripheralMud.asset",()=>new Mesh());mesh.Clear();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);batch.AddRaw(mesh,material,false);
  }
  static void SaveComplexLedger296()
  {
   File.WriteAllText(O296+"/venue-complexes.json",JsonUtility.ToJson(new ComplexLedger296{Method="Native whole-building dimensions and original maps, Blender QEM levels; dry stepped courtyard connections use rendered stone floors. Central combat rectangles remain empty. Sunken annex is excluded from dry paths. Existing source assets, terrain and water unchanged.",Buildings=complexBuildings296.ToArray(),Paths=complexPaths296.ToArray()},true));
   var sheet=Sheet296();sheet.Sources=sheet.Sources.Where(s=>s.Id!="venue_cc0_peripheral_mud").Concat(new[]{new CompactArchitectureSheetSO.Source{Id="venue_cc0_peripheral_mud",AssetPath="Assets/_Project/Art/World/Reworld292/Highlands293/Textures/brown_mud_rocks_01_diff_2k.jpg",Publisher="Poly Haven",Url="https://polyhaven.com/a/brown_mud_rocks_01",License="CC0; existing Highlands293 acquisition ledger",Use="Peripheral ash/wet-mud surface traces, private materials only",MeshyCredits=0}}).ToArray();EditorUtility.SetDirty(sheet);
  }
  public static string RefreshNativeColliders296()
  {
   RequireClean292();if(EditorApplication.isPlayingOrWillChangePlaymode||Session292().gameObject.scene.path!=Scene296)throw new InvalidOperationException("Native collider refresh requires the saved296 Edit candidate.");
   var ledger=JsonUtility.FromJson<ComplexLedger296>(File.ReadAllText(O296+"/venue-complexes.json"));var report=new List<string>();
   foreach(var entry in ledger.Buildings.Where(b=>b.Source.StartsWith("Assets/",StringComparison.Ordinal)))
   {
    var root=Root296(entry.SceneRoot);var physical=root.transform.Find("Physical_SourceFaces");if(physical==null)throw new Exception("Missing native physical child: "+entry.SceneRoot);
    string guid=AssetDatabase.AssetPathToGUID(entry.Source);var data=JsonUtility.FromJson<ComplexAssetData296>(File.ReadAllText(O296+"/Complex/Collision/"+guid+".json"));
    // Export bounds are source-local; the receipt size is a check, not a substitute for its offset.
    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(entry.Source);var filter=prefab.GetComponentsInChildren<MeshFilter>(true).First();
    var sourceBounds=new Bounds((data.Min+data.Max)*.5f,data.Max-data.Min);if(sourceBounds.size.sqrMagnitude<.1f)throw new Exception("Collision source bounds missing: "+entry.Source);
    var source=ComplexCollision296(entry.Source,entry.Id.StartsWith("Burned",StringComparison.Ordinal),sourceBounds);var collider=physical.GetComponent<MeshCollider>();var target=collider.sharedMesh;int old=target.triangles.Length/3;
    collider.sharedMesh=null;target.Clear();target.indexFormat=IndexFormat.UInt32;target.CombineMeshes(new[]{new CombineInstance{mesh=source,subMeshIndex=0,transform=Matrix4x4.Translate(new Vector3(-sourceBounds.center.x,-sourceBounds.min.y,-sourceBounds.center.z))}},true,true,false);target.RecalculateBounds();EditorUtility.SetDirty(target);collider.sharedMesh=target;entry.ColliderTriangles=target.triangles.Length/3;
    report.Add(entry.Id+" physical "+old+" -> "+entry.ColliderTriangles+" triangles; visual LODs retained.");
   }
   complexBuildings296.Clear();complexBuildings296.AddRange(ledger.Buildings);complexPaths296.Clear();complexPaths296.AddRange(ledger.Paths);SaveComplexLedger296();Physics.SyncTransforms();Save292();File.WriteAllLines(O296+"/native-colliders.txt",report);return string.Join("\n",report);
  }
  public static string RefreshKaesongLink296()
  {
   RequireClean292();if(EditorApplication.isPlayingOrWillChangePlaymode||Session292().gameObject.scene.path!=Scene296)throw new InvalidOperationException("Kaesong link refresh requires the saved296 Edit candidate.");
   var sheet=Sheet296();var arena=sheet.Arenas.First(a=>a.Id=="watercourt296");var root=Root296(arena.SceneRoot).transform;
   var old=JsonUtility.FromJson<ComplexLedger296>(File.ReadAllText(O296+"/venue-complexes.json"));complexBuildings296.Clear();complexBuildings296.AddRange(old.Buildings);complexPaths296.Clear();complexPaths296.AddRange(old.Paths.Where(p=>p.Realm!="hyeongang"));
   foreach(string child in new[]{"Compound296_SteppedMasonry","SubmergedWestAnnex"}){var found=root.Find(child);if(found!=null)Object.DestroyImmediate(found.gameObject);}
   var plan=new VenuePlan296{Id=arena.Id,Realm=arena.Realm,Place=arena.PlaceId,Clear=arena.ClearSize,Yaw=arena.Yaw,Centre=arena.Centre};var report=new List<string>();
   CompoundVenue296(plan,root,new CompactWorldSurface(Session292().MountainLayout),new List<CompactArchitectureSheetSO.Structure>(),report,true);
   SaveComplexLedger296();Physics.SyncTransforms();Save292();File.WriteAllLines(O296+"/kaesong-link-refresh.txt",report);
   return "Rebuilt only Hyeongang compound masonry and path samples. The Kaesong link crosses between original column rows at old local z4.55; native halls, galleries, original295 buildings, actors, terrain and water retained.\n"+string.Join("\n",report);
  }
  public static string RefreshNativeComplex296()
  {
   RequireClean292();if(EditorApplication.isPlayingOrWillChangePlaymode||Session292().gameObject.scene.path!=Scene296)throw new InvalidOperationException("Native refresh requires the saved296 Edit candidate.");
   var old=JsonUtility.FromJson<ComplexLedger296>(File.ReadAllText(O296+"/venue-complexes.json"));var native=old.Buildings.Where(b=>b.Source.StartsWith("Assets/",StringComparison.Ordinal)).ToArray();
   var sheet=Sheet296();var originalStructures=sheet.Structures;var newStructures=new List<CompactArchitectureSheetSO.Structure>();var report=new List<string>();
   complexReduced296.Clear();burnedComplex296.Clear();foreach(string path in ComplexNativeSources296)venueModules296.Remove(path);complexBuildings296.Clear();complexPaths296.Clear();complexPaths296.AddRange(old.Paths);
   foreach(var entry in native)
   {
    var current=Root296(entry.SceneRoot);if(current==null)throw new InvalidOperationException("Missing saved native building "+entry.SceneRoot);
    var root=current.transform.parent;var arena=sheet.Arenas.First(a=>a.SceneRoot==ScenePathVenue296(root));var local=current.transform.localPosition;float yaw=current.transform.localEulerAngles.y;
    var plan=new VenuePlan296{Id=arena.Id,Realm=arena.Realm,Place=arena.PlaceId};Object.DestroyImmediate(current);
    NativeCompound296(plan,root,entry.Id,entry.Source,local,yaw,entry.Id.StartsWith("Burned",StringComparison.Ordinal),newStructures,report);
   }
   var replacements=newStructures.ToDictionary(s=>s.Id,s=>s);sheet.Structures=originalStructures.Select(s=>replacements.TryGetValue(s.Id,out var updated)?updated:s).ToArray();EditorUtility.SetDirty(sheet);
   var refreshed=complexBuildings296.ToDictionary(b=>b.Realm+"/"+b.Id,b=>b);complexBuildings296.Clear();complexBuildings296.AddRange(old.Buildings.Select(b=>refreshed.TryGetValue(b.Realm+"/"+b.Id,out var replacement)?replacement:b));
   SaveVenueSources296();SaveComplexLedger296();Physics.SyncTransforms();Save292();File.WriteAllLines(O296+"/native-refresh.txt",report);
   return "Updated only "+native.Length+" native building visual/physical LODs; supports, arenas, actors, terrain, water and other structures retained.\n"+string.Join("\n",report);
  }
 }
}
