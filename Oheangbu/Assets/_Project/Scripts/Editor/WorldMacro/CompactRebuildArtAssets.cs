using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static Material ArtMaterial(Material source,string folder,string name,Texture geography)
  {
   string path=folder+"/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
   if(mat==null){mat=new Material(source);AssetDatabase.CreateAsset(mat,path);}else mat.CopyPropertiesFromMaterial(source);
   mat.name=name;mat.enableInstancing=true;
   void F(string key,float v){if(mat.HasProperty(key))mat.SetFloat(key,v);}
   F("_PaintedFormAuthored",0);F("_PaintedRoadBank",0);F("_WindAmplitude",.025f);F("_LeafFlutter",0);F("_WindExternalClock",1);
   F("_FadeInStart",-1);F("_FadeInEnd",0);F("_FadeOutStart",99990);F("_FadeOutEnd",99999);
   if(mat.HasProperty("_PaintedGeoField")){mat.SetTexture("_PaintedGeoField",geography);mat.SetVector("_PaintedGeoRect",new Vector4(0,0,4000,6000));F("_PaintedGeoEnabled",1);}
   EditorUtility.SetDirty(mat);return mat;
  }
  static Mesh ArtMesh(Mesh value,string path)
  {
   var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing==null){AssetDatabase.CreateAsset(value,path);return value;}
   existing.Clear();existing.indexFormat=value.indexFormat;existing.vertices=value.vertices;existing.normals=value.normals;existing.tangents=value.tangents;existing.uv=value.uv;existing.colors=value.colors;existing.subMeshCount=value.subMeshCount;
   for(int i=0;i<value.subMeshCount;i++)existing.SetTriangles(value.GetTriangles(i),i);existing.bounds=value.bounds;existing.UploadMeshData(false);Object.DestroyImmediate(value);EditorUtility.SetDirty(existing);return existing;
  }
  static Sheet.Prototype ArtPrototype(Sheet.Prototype source,string folder,Texture geo)
  {
   var copy=new Sheet.Prototype{Id=source.Id,SourcePath=source.SourcePath,SourceHash=source.SourceHash,Realm=source.Realm,Category=source.Category,Size=source.Size,Radius=source.Radius,Scale=source.Scale,BillboardViews=source.BillboardViews};
   int levels=source.Category==Sheet.Kind.Tree?3:Math.Min(2,source.Lods.Length);copy.Lods=new Sheet.Level[levels];
   for(int l=0;l<levels;l++){
    var parts=new List<Sheet.Part>();int index=0;
    foreach(var part in source.Lods[l].Parts){parts.Add(new Sheet.Part{Mesh=part.Mesh,Submesh=part.Submesh,Local=part.Local,Material=ArtMaterial(part.Material,folder,source.Id+"_L"+l+"_P"+index++,geo)});}
    foreach(var part in parts){
     var m=part.Material;
     if(source.Category==Sheet.Kind.Tree)m.SetVector("_PaintedCanopyTones",new Vector4(.016f,.033f,.058f,.1f));
     if(source.Category==Sheet.Kind.Grass||source.Category==Sheet.Kind.Shrub)m.SetVector("_PaintedCanopyTones",new Vector4(.035f,.065f,.12f,.19f));
     // Preserve leaf coverage in distant atlas mips without changing shared source imports.
     if(m.GetFloat("_Billboard")>.5f){
      // Bake card dimensions into vertices; explicit 3D bounds also cover its camera-facing rotation.
      var mesh=Object.Instantiate(part.Mesh);mesh.vertices=mesh.vertices.Select(v=>part.Local.MultiplyPoint3x4(v)).ToArray();mesh.RecalculateBounds();var bounds=mesh.bounds;bounds.size=new Vector3(bounds.size.x,bounds.size.y,Mathf.Max(bounds.size.z,bounds.size.x));mesh.bounds=bounds;part.Mesh=ArtMesh(mesh,folder+"/"+source.Id+"_Card.asset");part.Local=Matrix4x4.identity;
      string original=AssetDatabase.GetAssetPath(m.GetTexture("_BaseMap"));string target=folder+"/"+source.Id+"_Atlas"+Path.GetExtension(original);
      if(!File.Exists(target)&&!AssetDatabase.CopyAsset(original,target))throw new Exception("Atlas copy failed "+original);
      var ti=AssetImporter.GetAtPath(target) as TextureImporter;if(ti!=null){ti.mipmapEnabled=true;ti.mipMapsPreserveCoverage=true;ti.alphaTestReferenceValue=m.GetFloat("_Cutoff");ti.textureCompression=TextureImporterCompression.Uncompressed;ti.SaveAndReimport();}m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(target));
     }
    }
    copy.Lods[l]=new Sheet.Level{Parts=parts.ToArray()};
   }
   return copy;
  }
  static Mesh ClusterPine(Mesh source,float cell)
  {
   var positions=source.vertices;var normals=source.normals;var uv=source.uv;
   var lookup=new Dictionary<(int,int,int,int,int),int>();var map=new int[positions.Length];var vertices=new List<Vector3>();var n=new List<Vector3>();var tex=new List<Vector2>();var counts=new List<int>();
   for(int i=0;i<positions.Length;i++){
    var p=positions[i];var t=uv.Length==positions.Length?uv[i]:Vector2.zero;
    var key=(Mathf.RoundToInt(p.x/cell),Mathf.RoundToInt(p.y/cell),Mathf.RoundToInt(p.z/cell),Mathf.FloorToInt(t.x*16),Mathf.FloorToInt(t.y*16));
    if(!lookup.TryGetValue(key,out int at)){at=vertices.Count;lookup.Add(key,at);vertices.Add(Vector3.zero);n.Add(Vector3.zero);tex.Add(Vector2.zero);counts.Add(0);}
    map[i]=at;vertices[at]+=p;n[at]+=normals.Length==positions.Length?normals[i]:Vector3.up;tex[at]+=t;counts[at]++;
   }
   for(int i=0;i<vertices.Count;i++){vertices[i]/=counts[i];n[i]=n[i].normalized;tex[i]/=counts[i];}
   var triangles=new List<int>();var input=source.triangles;var seen=new HashSet<(int,int,int)>();
   for(int i=0;i<input.Length;i+=3){int a=map[input[i]],b=map[input[i+1]],c=map[input[i+2]];if(a==b||b==c||c==a||!seen.Add((a,b,c)))continue;triangles.Add(a);triangles.Add(b);triangles.Add(c);}
   var mesh=new Mesh{name="Meshy pine middle",indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetNormals(n);mesh.SetUVs(0,tex);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();return mesh;
  }
  static Sheet.Prototype MeshyPine(string folder,Material template,Texture geography)
  {
   const string sourcePath="Assets/_Project/Art/World/PineRestTrees/PhotorealPine/Tree.prefab";
   var source=AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);var filters=source.GetComponentsInChildren<MeshFilter>(true);var trunk=filters.First(f=>f.name=="Tree");
   var sourceBounds=trunk.GetComponent<Renderer>().bounds;float ratio=10/sourceBounds.size.y;
   var toGround=Matrix4x4.Scale(Vector3.one*ratio)*Matrix4x4.Translate(new Vector3(-sourceBounds.center.x,-sourceBounds.min.y,-sourceBounds.center.z));
   Mesh Bake(MeshFilter f){var mesh=new Mesh{name=f.name,indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(new[]{new CombineInstance{mesh=f.sharedMesh,transform=toGround*f.transform.localToWorldMatrix}},true,true);return mesh;}
   var body=ArtMesh(Bake(trunk),folder+"/MeshyPine_Near.asset");var middle=ArtMesh(ClusterPine(body,.17f),folder+"/MeshyPine_Middle.asset");
   var fullNeedles=Bake(filters.First(f=>f.name!="Tree"));var v=fullNeedles.vertices;var n=fullNeedles.normals;var tris=fullNeedles.triangles;var vv=new List<Vector3>();var nn=new List<Vector3>();var tt=new List<int>();
   // Each authored needle is one independent triangle. Retain one in eight deterministically.
   for(int i=0;i<tris.Length;i+=24)for(int j=0;j<3;j++){tt.Add(vv.Count);vv.Add(v[tris[i+j]]);nn.Add(n[tris[i+j]]);}
   var sparse=new Mesh{name="Sparse near pine needles",indexFormat=IndexFormat.UInt32};sparse.SetVertices(vv);sparse.SetNormals(nn);sparse.SetTriangles(tt,0);sparse.RecalculateBounds();Object.DestroyImmediate(fullNeedles);sparse=ArtMesh(sparse,folder+"/MeshyPine_Needles.asset");
   var bodyMat=ArtMaterial(template,folder,"MeshyPine_Near",geography);var original=trunk.GetComponent<Renderer>().sharedMaterial;
   bodyMat.SetTexture("_BaseMap",original.GetTexture("_BaseMap"));bodyMat.SetColor("_BaseColor",new Color(.72f,.76f,.65f));bodyMat.SetFloat("_AlphaClip",0);bodyMat.SetFloat("_Billboard",0);bodyMat.SetFloat("_Cull",2);bodyMat.SetFloat("_WindAmplitude",0);bodyMat.SetFloat("_Height",10);
   var needleMat=ArtMaterial(bodyMat,folder,"MeshyPine_Needles",geography);needleMat.SetTexture("_BaseMap",Texture2D.whiteTexture);needleMat.SetColor("_BaseColor",new Color(.23f,.29f,.20f));needleMat.SetFloat("_Cull",0);needleMat.SetFloat("_WindAmplitude",.02f);
   var middleMat=ArtMaterial(bodyMat,folder,"MeshyPine_Middle",geography);
   var near=new Sheet.Level{Parts=new[]{new Sheet.Part{Mesh=body,Material=bodyMat},new Sheet.Part{Mesh=sparse,Material=needleMat}}};
   var atlas=PineAtlas(folder,near);
   var card=ArtMaterial(template,folder,"MeshyPine_Far",geography);card.SetTexture("_BaseMap",atlas);card.SetColor("_BaseColor",Color.white);card.SetFloat("_AlphaClip",1);card.SetFloat("_Cutoff",.28f);card.SetFloat("_Billboard",1);card.SetFloat("_BillboardViews",4);card.SetFloat("_Cull",0);card.SetFloat("_SimpleLighting",1);card.SetFloat("_WindAmplitude",0);
   var quad=new Mesh{name="Pine far card"};quad.vertices=new[]{new Vector3(-.5f,0,0),new Vector3(.5f,0,0),new Vector3(-.5f,1,0),new Vector3(.5f,1,0)};quad.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};quad.triangles=new[]{0,2,1,2,3,1};quad.vertices=quad.vertices.Select(v=>v*12).ToArray();quad.RecalculateNormals();quad.RecalculateBounds();quad.bounds=new Bounds(Vector3.up*6,new Vector3(12,12,12));quad=ArtMesh(quad,folder+"/PineCard.asset");
   var size=body.bounds.size;size=Vector3.Max(size,sparse.bounds.size);
   return new Sheet.Prototype{Id="Meshy_Pinus",SourcePath=sourcePath,SourceHash=AssetDatabase.GetAssetDependencyHash(sourcePath).ToString(),Realm=Oheangbu.Data.World.RealmId.Cheongrim,Category=Sheet.Kind.Tree,Size=size,Radius=.3f,BillboardViews=4,
    Lods=new[]{near,new Sheet.Level{Parts=new[]{new Sheet.Part{Mesh=middle,Material=middleMat}}},new Sheet.Level{Parts=new[]{new Sheet.Part{Mesh=quad,Material=card,Local=Matrix4x4.identity}}}}};
  }
  static Texture2D PineAtlas(string folder,Sheet.Level near)
  {
   string path=folder+"/MeshyPine_Atlas.png";var root=new GameObject("Temporary_PineAtlas"){layer=31};var go=new GameObject("Temporary_AtlasCamera");var camera=go.AddComponent<Camera>();var materials=new List<Material>();var rt=new RenderTexture(384,384,24,RenderTextureFormat.ARGB32);var atlas=new Texture2D(1536,384,TextureFormat.RGBA32,false);var old=RenderTexture.active;
   try{
    foreach(var part in near.Parts){var node=new GameObject("PinePart"){layer=31};node.transform.SetParent(root.transform,false);node.AddComponent<MeshFilter>().sharedMesh=part.Mesh;var r=node.AddComponent<MeshRenderer>();var m=new Material(Shader.Find("Universal Render Pipeline/Unlit"));m.SetTexture("_BaseMap",part.Material.GetTexture("_BaseMap"));m.SetColor("_BaseColor",part.Material.GetColor("_BaseColor"));m.SetFloat("_Cull",0);r.sharedMaterial=m;r.shadowCastingMode=ShadowCastingMode.Off;materials.Add(m);}
    camera.enabled=false;camera.orthographic=true;camera.orthographicSize=6;camera.aspect=1;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.cullingMask=1<<31;camera.nearClipPlane=.1f;camera.farClipPlane=50;camera.targetTexture=rt;camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
    for(int view=0;view<4;view++){var center=Vector3.up*6;camera.transform.position=center+Quaternion.Euler(0,-view*90,0)*Vector3.back*25;camera.transform.LookAt(center);camera.Render();RenderTexture.active=rt;atlas.ReadPixels(new Rect(0,0,384,384),384*view,0);}
    atlas.Apply();File.WriteAllBytes(path,atlas.EncodeToPNG());
   }finally{camera.targetTexture=null;RenderTexture.active=old;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(atlas);Object.DestroyImmediate(go);Object.DestroyImmediate(root);foreach(var m in materials)Object.DestroyImmediate(m);}
   AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.alphaIsTransparency=true;importer.mipmapEnabled=true;importer.mipMapsPreserveCoverage=true;importer.alphaTestReferenceValue=.28f;importer.wrapMode=TextureWrapMode.Clamp;importer.maxTextureSize=2048;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
  }
 }
}
