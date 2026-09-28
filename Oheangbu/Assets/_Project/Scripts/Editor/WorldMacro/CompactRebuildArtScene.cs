using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static float FlatPathDistance(Vector3 p,Vector3[] path)
  {
   float best=float.MaxValue;var q=new Vector2(p.x,p.z);
   for(int i=1;i<path.Length;i++){var a=new Vector2(path[i-1].x,path[i-1].z);var d=new Vector2(path[i].x,path[i].z)-a;float t=d.sqrMagnitude<.001f?0:Mathf.Clamp01(Vector2.Dot(q-a,d)/d.sqrMagnitude);best=Mathf.Min(best,Vector2.Distance(q,a+d*t));}return best;
  }
  static string ArtBuild()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=Scene)throw new Exception("Canonical Compact Edit required");
   var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
   var walk=JsonUtility.FromJson<WalkReceipt>(File.ReadAllText(Output+"/art_route.json"));
   var original=SceneManager.GetActiveScene();var scene=EditorSceneManager.OpenScene(receipt.scene,OpenSceneMode.Additive);SceneManager.SetActiveScene(scene);
   string folder=Path.GetDirectoryName(receipt.scene).Replace('\\','/')+"/Art";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
   try{
    var roots=scene.GetRootGameObjects();var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single();
    var previous=roots.SingleOrDefault(g=>g.name=="Rebuild_EnvironmentArt");if(previous!=null)Object.DestroyImmediate(previous);roots=scene.GetRootGameObjects();
    var root=new GameObject("Rebuild_EnvironmentArt");SceneManager.MoveGameObjectToScene(root,scene);
    var terrain=roots.Single(g=>g.name=="Compact_Rebuild_Terrain");var groundSurface=FinalSurface(scene);Physics.SyncTransforms();
    RaycastHit Ground(float x,float z)=>groundSurface(x,z);
    var geo=BakeArtGeography(folder,Ground);
    string sourcePath=File.ReadAllLines(Output+"/art_inventory.txt").First(l=>l.StartsWith("SHEET ")).Substring(6);var library=AssetDatabase.LoadAssetAtPath<Sheet>(sourcePath);
    var prototypes=new List<Sheet.Prototype>();
    string[] names={"SM_PinusDensiflora_Spring_2","SM_UlmusDavidiana_Summer_2","SM_Deparia_1","SM_Deparia_3","SM_Grass","LowGroundFill","SM_Rock_K","SM_Rock_L","SM_M_WoodLog","SM_M_WoodenBox","SM_052_Pot"};
    foreach(string name in names){var p=library.Prototypes.FirstOrDefault(p=>p.Id=="Cheongrim_"+name);if(p==null)throw new Exception("Missing owned source "+name);prototypes.Add(ArtPrototype(p,folder,geo));}
    var template=prototypes.First(p=>p.Category==Sheet.Kind.Tree).Lods[0].Parts.First(p=>p.Material.GetFloat("_AlphaClip")>.5f).Material;
    prototypes.Add(MeshyPine(folder,template,geo));
    var sheet=ScriptableObject.CreateInstance<Sheet>();sheet.name="Mine valley inn art placements";sheet.Geography=AssetDatabase.LoadAssetAtPath<WorldMacroSheetSO>(Path.GetDirectoryName(receipt.scene).Replace('\\','/')+"/Geography.asset");sheet.Seed=20260923;sheet.TreeNear=42;sheet.TreeMiddle=150;sheet.ForestDistance=1350;sheet.GrassMeshDistance=24;sheet.GrassDistance=95;sheet.ShrubDistance=155;sheet.Prototypes=prototypes.ToArray();
    var placements=new List<Sheet.FixedPlacement>();var random=new System.Random(sheet.Seed);float R(float min,float max)=>(float)(min+random.NextDouble()*(max-min));
    var inn=manifest.Layout.Places.Single(p=>p.Id=="geumpyo_inn").XZ;var mine=manifest.Layout.Places.Single(p=>p.Id=="mine").XZ;
    var path=walk.trail.Where((p,i)=>i%2==0||i==walk.trail.Length-1).ToArray();
    bool Clear(Vector3 p,float radius){
     if(FlatPathDistance(p,path)<radius+4f||FlatPathDistance(p,manifest.Content.MainPath)<radius+2.5f)return false;
     if(Vector2.Distance(new Vector2(p.x,p.z),inn)<(radius<=.3f?15:20)+radius)return false;
     if(Vector2.Distance(new Vector2(p.x,p.z),mine)<14+radius)return false;
     foreach(var q in manifest.Content.Points)if(Vector2.Distance(new Vector2(p.x,p.z),new Vector2(q.Position.x,q.Position.z))<q.Radius+radius+2)return false;
     foreach(var e in manifest.Content.Encounters)if(Vector2.Distance(new Vector2(p.x,p.z),new Vector2(e.Feet.x,e.Feet.z))<e.Leash+radius)return false;
     return true;
    }
    var caveFloor=roots.Single(g=>g.name=="mine").GetComponentsInChildren<MeshCollider>(true).Single(c=>c.name=="Natural_Cave_Floor");
    bool Place(string id,string prototype,float x,float z,float scale,float yaw,bool reserve=true,float tilt=0,string cluster="valley"){
     var p=prototypes.Single(p=>p.Id==prototype);var hit=Ground(x,z);float radius=p.Category==Sheet.Kind.Tree?.7f*scale:p.Category==Sheet.Kind.Rock?p.Radius*scale:.25f;
     if(Vector3.Angle(hit.normal,Vector3.up)>(p.Category==Sheet.Kind.Rock?58:34)||reserve&&!Clear(hit.point,radius))return false;
     if(p.Category!=Sheet.Kind.Prop&&BelowGalleryRoof(caveFloor,hit.point))return false;
     var pos=hit.point;if(p.Category==Sheet.Kind.Rock)pos.y-=p.Size.y*scale*.17f;
     placements.Add(new Sheet.FixedPlacement{Id=id,ClusterId=cluster,PrototypeId=prototype,Position=pos,Euler=new Vector3(tilt,yaw,0),Scale=scale,Preserve=true});return true;
    }
    // Unequal groves across the valley slopes; retain broad views and clear approach space.
    int serial=0;
    for(float z=1550;z<2730;z+=16)for(float x=2760;x<3860;x+=16){
     float px=x+R(-6,6),pz=z+R(-6,6);var point=new Vector3(px,0,pz);float route=FlatPathDistance(point,path);
     float clump=Mathf.PerlinNoise(px*.0107f+7,pz*.0131f+19);if(clump<.42f||route>430||R(0,1)>.76f)continue;
     string prototype=R(0,1)<.62f?"Cheongrim_SM_PinusDensiflora_Spring_2":"Cheongrim_SM_UlmusDavidiana_Summer_2";
     Place("grove_"+serial++,prototype,px,pz,prototype.Contains("Ulmus")?R(.65f,1.05f):R(1.25f,2.1f),R(0,360));
    }
    // A few distinct, high-detail pines frame the inn, cave exit and bends.
    var heroes=new[]{new Vector2(-26,-11),new Vector2(25,10),new Vector2(-24,23),new Vector2(30,-23),new Vector2(15,35)};
    for(int i=0;i<heroes.Length;i++)Place("inn_meshy_"+i,"Meshy_Pinus",inn.x+heroes[i].x,inn.y+heroes[i].y,R(.85f,1.18f),R(0,360),true,0,"inn_frame");
    for(int i=8;i<path.Length-3;i+=7){var d=(path[i+1]-path[i-1]);d.y=0;var side=Vector3.Cross(d.normalized,Vector3.up)*(i%2==0?1:-1);var p=path[i]+side*R(9,17);Place("path_meshy_"+i,"Meshy_Pinus",p.x,p.z,R(.85f,1.2f),R(0,360),true,0,"path_frame");}
    // Rock outcrops settle into the slope; smaller pieces and ferns collect around them.
    for(int i=0;i<230;i++){
     float x=R(2940,3690),z=R(1700,2520);var p=new Vector3(x,0,z);if(FlatPathDistance(p,path)>145)continue;
     float scale=R(.45f,1.9f);if(!Place("outcrop_"+i,"Cheongrim_SM_Rock_K",x,z,scale,R(0,360),true,R(-13,13)))continue;
     for(int j=0;j<3;j++)Place("outcrop_fern_"+i+"_"+j,"Cheongrim_SM_Deparia_1",x+R(-4,4),z+R(-4,4),R(.8f,1.5f),R(0,360));
    }
    // Path shoulders, never a continuous hedge. Keep the soil route visibly open.
    for(int i=2;i<path.Length-1;i++){
     var d=path[i+1]-path[i-1];d.y=0;var side=Vector3.Cross(d.normalized,Vector3.up);
     for(int j=0;j<130;j++){
      var p=path[i]+side*(j%2==0?1:-1)*R(3.5f,22)+d.normalized*R(-5,5);
      if(Mathf.PerlinNoise(p.x*.12f,p.z*.12f)<.36f)continue;
      string species=j%11==0?"Cheongrim_SM_Deparia_3":j%3==0?"Cheongrim_LowGroundFill":"Cheongrim_SM_Grass";
      Place("shoulder_"+i+"_"+j,species,p.x,p.z,R(.8f,1.7f),R(0,360));
     }
    }
    // Low planting outside the swept forecourt; the porch and all conversations stay empty.
    for(int i=0;i<3000;i++){
     float angle=R(0,Mathf.PI*2),r=R(16,65),x=inn.x+Mathf.Cos(angle)*r,z=inn.y+Mathf.Sin(angle)*r;
     Place("inn_understory_"+i,i%7==0?"Cheongrim_SM_Deparia_1":"Cheongrim_LowGroundFill",x,z,R(1.1f,2.3f),R(0,360));
    }
    // Hand-placed lived-in edges: wood drying, two supply boxes and pottery away from the stair.
    for(int i=0;i<15;i++){
     Place("inn_firewood_"+i,"Cheongrim_SM_M_WoodLog",inn.x-12+(i%5)*.23f,inn.y+10,1.1f,86,false,0,"inn_work");
     placements[placements.Count-1].Position+=Vector3.up*(i/5)*.18f;
    }
    for(int i=0;i<7;i++)Place("inn_edge_stone_"+i,"Cheongrim_SM_Rock_L",inn.x-10+i*.8f,inn.y-5,.55f,R(0,360),false,0,"inn_edge");
    Place("inn_pottery_a","Cheongrim_SM_052_Pot",inn.x+9,inn.y+13,1.1f,15,false,0,"inn_work");
    Place("inn_pottery_b","Cheongrim_SM_052_Pot",inn.x+10,inn.y+14,.8f,-15,false,0,"inn_work");
    Place("mine_supply_a","Cheongrim_SM_M_WoodenBox",mine.x-14,mine.y+13,1,15,false,0,"mine_work");
    Place("mine_supply_b","Cheongrim_SM_M_WoodenBox",mine.x-15,mine.y+14,.8f,-8,false,0,"mine_work");
    sheet.FixedPlacements=placements.ToArray();sheet=SavePrivate(sheet,folder+"/Placements.asset");manifest.Art=sheet;
    var draw=root.AddComponent<CompactRebuildArtRenderer>();draw.Sheet=sheet;draw.Observer=roots.SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).Single(c=>c.CompareTag("MainCamera"));
    int collisionCount=0;
    foreach(var placed in placements){
     var p=prototypes.Single(p=>p.Id==placed.PrototypeId);if(p.Category!=Sheet.Kind.Tree&&p.Category!=Sheet.Kind.Rock)continue;
     if(FlatPathDistance(placed.Position,path)>65)continue;
     var solid=new GameObject(placed.Id);solid.transform.SetParent(root.transform,false);solid.transform.SetPositionAndRotation(placed.Position,Quaternion.Euler(placed.Euler));solid.transform.localScale=Vector3.one*placed.Scale;
     if(p.Category==Sheet.Kind.Tree){var c=solid.AddComponent<CapsuleCollider>();c.radius=p.Radius;c.height=Mathf.Min(p.Size.y,5);c.center=Vector3.up*c.height*.5f;}
     else{var c=solid.AddComponent<BoxCollider>();c.size=p.Size*.72f;c.center=Vector3.up*p.Size.y*.32f;}
     collisionCount++;
    }
    DressArtGround(root,folder,path,Ground,geo,terrain);
    var look=roots.SelectMany(g=>g.GetComponentsInChildren<Oheangbu.App.WorldLookDriver>(true)).Single();var so=new SerializedObject(look);var sky=ScriptableObject.CreateInstance<InkSkyProfile>();sky.Horizon=new Color(.89f,.90f,.87f);sky.Zenith=new Color(.56f,.63f,.63f);sky.Cloud=new Color(.67f,.71f,.69f);sky.CloudDensity=.24f;sky.CapitalAtmosphere=0;sky=SavePrivate(sky,folder+"/SkyProfile.asset");so.FindProperty("_inkSkyProfile").objectReferenceValue=sky;so.ApplyModifiedPropertiesWithoutUndo();
    EditorUtility.SetDirty(manifest);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
    File.WriteAllText(Output+"/art_placements.json",JsonUtility.ToJson(sheet,true));
    string summary="Fixed placements="+placements.Count+"; "+string.Join(", ",placements.GroupBy(p=>prototypes.Single(t=>t.Id==p.PrototypeId).Category).Select(g=>g.Key+"="+g.Count()))+"; collision objects="+collisionCount+"; source-preserving instanced renderer; private geography/sky/materials.";
    File.WriteAllText(Output+"/art_build.txt",summary);return summary;
   }finally{EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(original);}
  }
  static Texture2D BakeArtGeography(string folder,Func<float,float,RaycastHit> ground)
  {
   const int w=256,h=384;var heights=new float[w*h];for(int z=0;z<h;z++)for(int x=0;x<w;x++)heights[z*w+x]=ground((x+.5f)/w*4000,(z+.5f)/h*6000).point.y;
   var texture=new Texture2D(w,h,TextureFormat.RGBA32,false,true);
   for(int z=0;z<h;z++)for(int x=0;x<w;x++){
    float at=heights[z*w+x],minimum=at;for(int dz=-16;dz<=16;dz+=8)for(int dx=-16;dx<=16;dx+=8)minimum=Mathf.Min(minimum,heights[Mathf.Clamp(z+dz,0,h-1)*w+Mathf.Clamp(x+dx,0,w-1)]);
    float gx=heights[z*w+Mathf.Min(w-1,x+1)]-heights[z*w+Mathf.Max(0,x-1)],gz=heights[Mathf.Min(h-1,z+1)*w+x]-heights[Mathf.Max(0,z-1)*w+x];var direction=new Vector2(gx,gz).normalized;float relief=at-minimum;
    texture.SetPixel(x,z,new Color(Mathf.SmoothStep(0,1,relief/55),direction.x*.5f+.5f,direction.y*.5f+.5f,Mathf.Clamp01(relief/256)));
   }
   texture.Apply();string path=folder+"/Geography.png";File.WriteAllBytes(path,texture.EncodeToPNG());Object.DestroyImmediate(texture);AssetDatabase.ImportAsset(path);var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.sRGBTexture=false;ti.mipmapEnabled=false;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.wrapMode=TextureWrapMode.Clamp;ti.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
  }
  static void DressArtGround(GameObject root,string folder,Vector3[] path,Func<float,float,RaycastHit> ground,Texture geo,GameObject terrain)
  {
   var source=terrain.GetComponentInChildren<MeshRenderer>().sharedMaterial;var terrainMat=ArtMaterial(source,folder,"Terrain",geo);terrainMat.SetFloat("_PaintedFormAuthored",0);terrainMat.SetFloat("_PaintedFormEnabled",1);
   foreach(var r in terrain.GetComponentsInChildren<MeshRenderer>())r.sharedMaterial=terrainMat;
   var material=new Material(Shader.Find("Oheangbu/WorldMacroVegetation"));material.SetColor("_BaseColor",new Color(.49f,.47f,.42f));material.SetFloat("_AmbientFloor",.78f);material.SetFloat("_LightResponse",.22f);material.SetFloat("_Saturation",.22f);material.SetFloat("_AlphaClip",1);material.SetFloat("_Cutoff",.5f);material.SetFloat("_Cull",0);material.SetFloat("_WindAmplitude",0);material.SetFloat("_WashStart",120);material.SetFloat("_WashEnd",900);material.SetFloat("_WashStrength",.58f);
   var texture=new Texture2D(128,128,TextureFormat.RGBA32,true);for(int y=0;y<128;y++)for(int x=0;x<128;x++){float edge=1-Mathf.Abs(x/127f*2-1);float noise=Mathf.PerlinNoise(x*.18f,y*.17f);float value=.76f+noise*.22f;texture.SetPixel(x,y,new Color(value,value,value,Mathf.Clamp01(edge*2.6f-noise*1.8f)));}texture.Apply();string texturePath=folder+"/Soil.png";File.WriteAllBytes(texturePath,texture.EncodeToPNG());Object.DestroyImmediate(texture);AssetDatabase.ImportAsset(texturePath);material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
   var pathMat=ArtMaterial(material,folder,"WornSoil",geo);pathMat.SetFloat("_WindAmplitude",0);Object.DestroyImmediate(material);var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();float distance=0;
   for(int i=1;i<path.Length;i++){
    var from=path[i-1];var to=path[i];var direction=to-from;direction.y=0;int steps=Mathf.Max(1,Mathf.CeilToInt(direction.magnitude/2));var side=Vector3.Cross(direction.normalized,Vector3.up);
    for(int j=0;j<steps;j++){
     var a=Vector3.Lerp(from,to,j/(float)steps);var b=Vector3.Lerp(from,to,(j+1)/(float)steps);if(i<5||a.y>70)continue;
     float width=1.0f+Mathf.PerlinNoise(a.x*.23f,a.z*.23f)*1.1f;int n=vertices.Count;
     foreach(var p in new[]{a-side*width,a+side*width,b-side*width,b+side*width})vertices.Add(ground(p.x,p.z).point+Vector3.up*.035f);
     float len=Vector3.Distance(a,b);uv.Add(new Vector2(0,distance*.22f));uv.Add(new Vector2(1,distance*.22f));uv.Add(new Vector2(0,(distance+len)*.22f));uv.Add(new Vector2(1,(distance+len)*.22f));triangles.AddRange(new[]{n,n+2,n+1,n+2,n+3,n+1});distance+=len;
    }
   }
   var mesh=new Mesh{name="Worn path over authored ground",indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();mesh=ArtMesh(mesh,folder+"/WornPath.asset");var go=new GameObject("Worn path");go.transform.SetParent(root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=pathMat;go.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
  }
 }
}
