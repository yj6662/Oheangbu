using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static void EaveBeltVenue296(string id,Transform parent,float halfX,float halfZ,float height,VenueBatch296 batch,List<string> report)
  {
   var data=JsonUtility.FromJson<RoofPatchLevels296>(File.ReadAllText(O296+"/Roof/tile-lods.json"));var material=AssetDatabase.LoadAssetAtPath<Material>(A296+"/Materials/Venues/RoofCeramic296.mat");
   var root=new GameObject("SecondEave_OutsideClearFloor");root.transform.SetParent(parent,false);var renderers=new Renderer[3];var counts=new int[3];float ix=halfX-.20f,iz=halfZ-.20f,ox=halfX+4,oz=halfZ+4;
   var strips=new[]{new Rect(-ox,-oz,2*ox,oz-iz),new Rect(-ox,iz,2*ox,oz-iz),new Rect(-ox,-iz,ox-ix,2*iz),new Rect(ix,-iz,ox-ix,2*iz)};
   float Height(float x,float z){float outward=Mathf.Max(Mathf.Abs(x)-halfX,Mathf.Abs(z)-halfZ),u=Mathf.Clamp01((outward+.2f)/4.2f);return height+2.2f*Mathf.Pow(1-u,1.3f)+.3f*Mathf.Pow(u,8);}
   for(int level=0;level<3;level++)
   {
    var patch=data.Levels[level];var vertices=new List<Vector3>();var texture=new List<Vector2>();var triangles=new List<int>();
    foreach(var strip in strips)
    {
     int nx=Mathf.CeilToInt(strip.width/data.TileSize.x),nz=Mathf.CeilToInt(strip.height/data.TileSize.y);
     for(int x=0;x<nx;x++)for(int z=0;z<nz;z++)
     {
      float cx=strip.xMin+(x+.5f)*data.TileSize.x,cz=strip.yMin+(z+.5f)*data.TileSize.y;
      for(int i=0;i<patch.Triangles.Length;i+=3)
      {
       var poly=new List<RoofVertex296>(3);for(int k=0;k<3;k++){int index=patch.Triangles[i+k];var p=patch.Vertices[index]+new Vector3(cx,0,cz);poly.Add(new RoofVertex296(p,patch.UV[index]));}
       poly=ClipRoof296(poly,0,strip.xMax,false);poly=ClipRoof296(poly,2,strip.yMax,false);if(poly.Count<3)continue;
       int first=vertices.Count;foreach(var v in poly){var p=v.P;p.y+=Height(p.x,p.z);vertices.Add(p);texture.Add(v.UV);}for(int k=1;k<poly.Count-1;k++){triangles.Add(first);triangles.Add(first+k);triangles.Add(first+k+1);}
      }
     }
    }
    var mesh=Asset296("Meshes/Venues/"+id+"_SecondEave"+level+".asset",()=>new Mesh());mesh.Clear();mesh.indexFormat=IndexFormat.UInt32;mesh.SetVertices(vertices);mesh.SetUVs(0,texture);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
    var go=new GameObject("HumanScaleEaveTiles_L"+level);go.transform.SetParent(root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderers[level]=renderer;counts[level]=triangles.Count/3;
   }
   var lod=root.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.65f,new[]{renderers[0]}),new LOD(.17f,new[]{renderers[1]}),new LOD(.003f,new[]{renderers[2]})});lod.RecalculateBounds();
   var sv=new List<Vector3>();var su=new List<Vector2>();var st=new List<int>();
   foreach(var strip in strips)
   {
    int nx=Mathf.CeilToInt(strip.width/1.5f),nz=Mathf.CeilToInt(strip.height/1.5f),first=sv.Count,plane=(nx+1)*(nz+1);
    for(int side=0;side<2;side++)for(int x=0;x<=nx;x++)for(int z=0;z<=nz;z++){float xx=strip.xMin+x*strip.width/nx,zz=strip.yMin+z*strip.height/nz;sv.Add(new Vector3(xx,Height(xx,zz)-.32f-side*.16f,zz));su.Add(new Vector2(xx/3,zz/3));}
    for(int x=0;x<nx;x++)for(int z=0;z<nz;z++){int a=first+x*(nz+1)+z,b=a+nz+1;st.AddRange(new[]{a,a+1,b,b,a+1,b+1,a+plane,b+plane,a+1+plane,b+plane,b+1+plane,a+1+plane});}
    void Edge(int a,int b){a+=first;b+=first;st.AddRange(new[]{a,b,a+plane,b,b+plane,a+plane});}
    for(int x=0;x<nx;x++){Edge(x*(nz+1),(x+1)*(nz+1));Edge((x+1)*(nz+1)+nz,x*(nz+1)+nz);}for(int z=0;z<nz;z++){Edge(z+1,z);Edge(nx*(nz+1)+z,nx*(nz+1)+z+1);}
   }
   var shell=Asset296("Meshes/Venues/"+id+"_SecondEaveShell.asset",()=>new Mesh());shell.Clear();shell.indexFormat=IndexFormat.UInt32;shell.SetVertices(sv);shell.SetUVs(0,su);shell.SetTriangles(st,0);shell.RecalculateNormals();shell.RecalculateTangents();shell.RecalculateBounds();EditorUtility.SetDirty(shell);batch.AddRaw(shell,ModuleVenue296(Jeju296+"Floors/Floor_Wood.prefab").Parts[0].Material,true);
   foreach(int side in new[]{-1,1})
   {
    for(float x=-halfX+1;x<halfX;x+=3)Bracket(new Vector3(x,height-.6f,side*(halfZ+1.6f)),new Vector3(0,0,side*.45f),0);
    for(float z=-halfZ+1;z<halfZ;z+=3)Bracket(new Vector3(side*(halfX+1.6f),height-.6f,z),new Vector3(side*.45f,0,0),90);
    BeamVenue296(batch,new Vector3(-ox,height,side*oz),new Vector3(ox,height,side*oz),.25f,.25f);BeamVenue296(batch,new Vector3(side*ox,height,-oz),new Vector3(side*ox,height,oz),.25f,.25f);
   }
   void Bracket(Vector3 position,Vector3 outward,float yaw)
   {
    // Preserve the approved sealed shell collision; keep the rendered exterior bracket
    // wholly outside the wall. Its original3.6m span reached0.2m into the interior.
    bool collisionOnly=batch.CollisionOnly,visualsOnly=batch.VisualsOnly;
    batch.CollisionOnly=true;batch.Add(Haeng296+"SM_R_Dancheong_4.prefab",position,new Vector3(.30f,.75f,3.6f),yaw);batch.CollisionOnly=collisionOnly;
    batch.VisualsOnly=true;batch.Add(Haeng296+"SM_R_Dancheong_4.prefab",position+outward,new Vector3(.30f,.75f,3.6f),yaw,false);batch.VisualsOnly=visualsOnly;
   }
   report.Add(id+" second eave outside clear rectangle: inner="+ix+","+iz+" outer="+ox+","+oz+"; height="+height+"; LOD triangles="+string.Join("/",counts)+"; source-size brackets and segmented beams.");
  }
 }
}
