using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] sealed class RoofPatchData296 {public Vector3[] Vertices,Normals;public Vector2[] UV;public int[] Triangles;}
  [Serializable] sealed class RoofPatchSource296 {public string Source,SourceSha256,Material;public RoofPatchData296 Mesh;public Vector3 Min,Max;}
  [Serializable] sealed class RoofPatchLevels296 {public string SourceSha256,Method,Material,ExportSha256;public Vector2 TileSize;public RoofPatchData296[] Levels;}
  static void ExportRoofTiles296(List<string> report)
  {
   var mesh=RoofTilePatch296(out var material);string source=GateKit296+"SM_B_GateHouse_Roof_001.prefab";
   System.IO.Directory.CreateDirectory(O296+"/Roof");System.IO.File.WriteAllText(O296+"/Roof/source-patch.json",JsonUtility.ToJson(new RoofPatchSource296{Source=source,SourceSha256=HashVenue296(source),Material=AssetDatabase.GetAssetPath(material),Mesh=new RoofPatchData296{Vertices=mesh.vertices,Normals=mesh.normals,UV=mesh.uv,Triangles=mesh.triangles},Min=mesh.bounds.min,Max=mesh.bounds.max},true));
   report.Add("Roof source2.5m patch exported: vertices="+mesh.vertexCount+" triangles="+mesh.triangles.Length/3+" localY="+mesh.bounds.min.y+".."+mesh.bounds.max.y+" material="+AssetDatabase.GetAssetPath(material));Object.DestroyImmediate(mesh);ExportComplexSources296(report);
  }
  struct RoofVertex296 {public Vector3 P;public Vector2 UV;public RoofVertex296(Vector3 p,Vector2 uv){P=p;UV=uv;}}
  static List<RoofVertex296> ClipRoof296(List<RoofVertex296> polygon,int axis,float value,bool greater)
  {
   var result=new List<RoofVertex296>();if(polygon.Count==0)return result;
   float Coord(RoofVertex296 v)=>axis==0?v.P.x:v.P.z;
   var prior=polygon[polygon.Count-1];bool was=greater?Coord(prior)>=value:Coord(prior)<=value;
   foreach(var next in polygon)
   {
    bool inside=greater?Coord(next)>=value:Coord(next)<=value;
    if(inside!=was){float t=(value-Coord(prior))/(Coord(next)-Coord(prior));result.Add(new RoofVertex296(Vector3.Lerp(prior.P,next.P,t),Vector2.Lerp(prior.UV,next.UV,t)));}
    if(inside)result.Add(next);prior=next;was=inside;
   }return result;
  }
  static Mesh RoofTilePatch296(out Material material)
  {
   var module=ModuleVenue296(GateKit296+"SM_B_GateHouse_Roof_001.prefab");
   // This reviewed prefab is one actual tiled hipped roof slope, not a whole building. The
   // middle 2.5m square excludes the authored ridge/eave ornaments. Preserve physical tile pitch.
   var chosen=module.Parts.OrderByDescending(p=>p.Meshes[0].triangles.Length).First();material=chosen.Material;
   var mesh=chosen.Meshes[0];var vertices=mesh.vertices;var uv=mesh.uv;var triangles=mesh.triangles;
   var centre=module.Bounds.center;const float half=1.25f;var output=new List<Vector3>();var texture=new List<Vector2>();var indices=new List<int>();
   for(int t=0;t<triangles.Length;t+=3)
   {
    var poly=new List<RoofVertex296>(3);for(int k=0;k<3;k++){int v=triangles[t+k];poly.Add(new RoofVertex296(vertices[v]-centre,uv.Length==vertices.Length?uv[v]:Vector2.zero));}
    poly=ClipRoof296(poly,0,-half,true);poly=ClipRoof296(poly,0,half,false);poly=ClipRoof296(poly,2,-half,true);poly=ClipRoof296(poly,2,half,false);
    if(poly.Count<3)continue;int first=output.Count;foreach(var v in poly){output.Add(v.P);texture.Add(v.UV);}for(int k=1;k<poly.Count-1;k++){indices.Add(first);indices.Add(first+k);indices.Add(first+k+1);}
   }
   if(indices.Count<30)throw new Exception("Tiled roof source extraction is empty.");
   double sx=0,sz=0,sy=0;foreach(var p in output){sx+=p.x;sz+=p.z;sy+=p.y;}double mx=sx/output.Count,mz=sz/output.Count,my=sy/output.Count;
   double xx=0,zz=0,xz=0,xy=0,zy=0;foreach(var p in output){double x=p.x-mx,z=p.z-mz,y=p.y-my;xx+=x*x;zz+=z*z;xz+=x*z;xy+=x*y;zy+=z*y;}
   double det=xx*zz-xz*xz;if(Math.Abs(det)<1e-8)throw new Exception("Roof tile plane cannot be fitted.");float a=(float)((xy*zz-zy*xz)/det),b=(float)((zy*xx-xy*xz)/det),c=(float)(my-a*mx-b*mz);
   for(int i=0;i<output.Count;i++){var p=output[i];p.y-=a*p.x+b*p.z+c;output[i]=p;}
   var patch=new Mesh{indexFormat=IndexFormat.UInt32,name="KCISA_2_5m_TilePatch296"};patch.SetVertices(output);patch.SetUVs(0,texture);patch.SetTriangles(indices,0);patch.RecalculateNormals();patch.RecalculateTangents();patch.RecalculateBounds();
   return patch;
  }
  static Material RoofMaterial296(RoofPatchLevels296 data)
  {
   string folder=A296+"/Textures/Venues";System.IO.Directory.CreateDirectory(folder);
   Texture2D Import(string file,bool normal)
   {
    string path=folder+"/"+file;System.IO.File.Copy(O296+"/Roof/"+file,path,true);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
    var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
    importer.sRGBTexture=!normal;importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Repeat;importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=4;importer.maxTextureSize=1024;importer.SaveAndReimport();
    return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
   }
   ModuleVenue296(GateKit296+"SM_B_GateHouse_Roof_001.prefab");
   var original=AssetDatabase.LoadAssetAtPath<Material>(data.Material);if(original==null)throw new Exception("Roof export material missing: "+data.Material);
   var copy=Asset296("Materials/Venues/RoofCeramic296.mat",()=>new Material(original));copy.CopyPropertiesFromMaterial(original);copy.shader=Shader.Find("Oheangbu/Reworld292/KoreanArchitecture");
   copy.SetTexture("_BaseMap",Import("tile-base296.png",false));copy.SetTexture("_BumpMap",Import("tile-normal296.png",true));copy.SetTextureScale("_BaseMap",Vector2.one);copy.SetTextureOffset("_BaseMap",Vector2.zero);
   copy.SetFloat("_Saturation",.15f);copy.SetFloat("_AmbientFloor",.52f);copy.SetFloat("_BumpScale",.55f);copy.SetFloat("_LightResponse",.65f);EditorUtility.SetDirty(copy);return copy;
  }
  static void RoofVenue296(string id,Transform parent,float width,float depth,float clearHeight,List<string> report,float roofRise=12)
  {
   string input=O296+"/Roof/tile-lods.json";if(!System.IO.File.Exists(input))throw new Exception("Run roof-export then Blender build_roof296.py before venues.");
   var data=JsonUtility.FromJson<RoofPatchLevels296>(System.IO.File.ReadAllText(input));
   if(data.SourceSha256!=HashVenue296(GateKit296+"SM_B_GateHouse_Roof_001.prefab")||data.ExportSha256!=HashVenue296(O296+"/Roof/source-patch.json"))throw new Exception("Roof source changed since offline reduction.");
   if(data.Levels==null||data.Levels.Length!=3||data.TileSize.x<=0||data.TileSize.y<=0)throw new Exception("Invalid reduced roof schema.");
   var material=RoofMaterial296(data);var renderers=new Renderer[3];int[] counts=new int[3];
   float Height(float x,float z)
   {
    float inset=Mathf.Min(width*.5f-Mathf.Abs(x),depth*.5f-Mathf.Abs(z)),u=Mathf.Clamp01(inset/(depth*.5f));
    return clearHeight+1.2f+roofRise*Mathf.Pow(u,1.2f)+.5f*Mathf.Exp(-inset*inset/3);
   }
   float minTile=data.Levels.SelectMany(l=>l.Vertices).Min(v=>v.y),underOffset=Mathf.Min(-.35f,minTile-.20f);
   for(int level=0;level<3;level++)
   {
    var patch=data.Levels[level];var p=patch.Vertices;var uv=patch.UV;var tri=patch.Triangles;
    var vertices=new List<Vector3>();var texture=new List<Vector2>();var triangles=new List<int>();
    int nx=Mathf.CeilToInt(width/data.TileSize.x),nz=Mathf.CeilToInt(depth/data.TileSize.y);
    for(int x=0;x<nx;x++)for(int z=0;z<nz;z++)
    {
     float cx=-width*.5f+(x+.5f)*data.TileSize.x,cz=-depth*.5f+(z+.5f)*data.TileSize.y;
     for(int i=0;i<tri.Length;i+=3)
     {
      var poly=new List<RoofVertex296>(3);for(int k=0;k<3;k++){int v=tri[i+k];poly.Add(new RoofVertex296(new Vector3(p[v].x+cx,p[v].y,p[v].z+cz),uv[v]));}
      poly=ClipRoof296(poly,0,width*.5f,false);poly=ClipRoof296(poly,2,depth*.5f,false);if(poly.Count<3)continue;
      int first=vertices.Count;foreach(var v in poly){var point=v.P;point.y+=Height(point.x,point.z);vertices.Add(point);texture.Add(v.UV);}for(int k=1;k<poly.Count-1;k++){triangles.Add(first);triangles.Add(first+k);triangles.Add(first+k+1);}
     }
    }
    var mesh=Asset296("Meshes/Venues/"+id+"_Tiles"+level+".asset",()=>new Mesh());mesh.Clear();mesh.indexFormat=IndexFormat.UInt32;mesh.SetVertices(vertices);mesh.SetUVs(0,texture);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
    counts[level]=triangles.Count/3;var go=new GameObject("HumanScaleTiles_L"+level);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;renderers[level]=r;
   }
   var lod=parent.gameObject.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.65f,new[]{renderers[0]}),new LOD(.17f,new[]{renderers[1]}),new LOD(.003f,new[]{renderers[2]})});lod.RecalculateBounds();
   // A closed, renderable curved shell supports the tile skin. Bottom stays below every tile
   // valley at every LOD; top and bottom are separate vertices/normals, without coplanar faces.
   var shellV=new List<Vector3>();var shellUV=new List<Vector2>();var shellT=new List<int>();int sx=Mathf.CeilToInt(width/2),sz=Mathf.CeilToInt(depth/2),plane=(sx+1)*(sz+1);
   for(int side=0;side<2;side++)for(int x=0;x<=sx;x++)for(int z=0;z<=sz;z++)
   {float xx=-width*.5f+x*width/sx,zz=-depth*.5f+z*depth/sz;shellV.Add(new Vector3(xx,Height(xx,zz)+underOffset-(side==1?.22f:0),zz));shellUV.Add(new Vector2(xx/3,zz/3));}
   for(int x=0;x<sx;x++)for(int z=0;z<sz;z++)
   {int a=x*(sz+1)+z,b=a+sz+1;shellT.AddRange(new[]{a,a+1,b,b,a+1,b+1,a+plane,b+plane,a+1+plane,b+plane,b+1+plane,a+1+plane});}
   void Edge(int a,int b){shellT.AddRange(new[]{a,b,a+plane,b,b+plane,a+plane});}
   for(int x=0;x<sx;x++){Edge(x*(sz+1),(x+1)*(sz+1));Edge((x+1)*(sz+1)+sz,x*(sz+1)+sz);}
   for(int z=0;z<sz;z++){Edge(z+1,z);Edge(sx*(sz+1)+z,sx*(sz+1)+z+1);}
   var shell=Asset296("Meshes/Venues/"+id+"_RoofUnderside.asset",()=>new Mesh());shell.Clear();shell.indexFormat=IndexFormat.UInt32;shell.SetVertices(shellV);shell.SetUVs(0,shellUV);shell.SetTriangles(shellT,0);shell.RecalculateNormals();shell.RecalculateTangents();shell.RecalculateBounds();EditorUtility.SetDirty(shell);
   var body=new GameObject("VisibleRoofUnderside_Physical");body.transform.SetParent(parent,false);body.AddComponent<MeshFilter>().sharedMesh=shell;body.AddComponent<MeshCollider>().sharedMesh=shell;body.AddComponent<MeshRenderer>().sharedMaterial=ModuleVenue296(Jeju296+"Floors/Floor_Wood.prefab").Parts[0].Material;
   report.Add(id+" tiled roof: original off-ridge ceramic baked/QEM, tile pitch retained; LOD triangles="+string.Join("/",counts)+"; shell triangles="+shellT.Count/3+"; tile-to-shell minimum="+(minTile-underOffset).ToString("F3")+"m; clear roof bottom >="+(clearHeight+1.2f+underOffset-.22f).ToString("F2")+"m.");
  }
 }
}
