using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static Material RuinMaterial265(string id,bool wood)
  {
   const int n=512;var pixels=new Color[n*n];var normal=new Color[n*n];var heights=new float[n*n];
   for(int y=0;y<n;y++)for(int x=0;x<n;x++){
    float u=x/(float)n,v=y/(float)n;float broad=Mathf.PerlinNoise(u*8+31,v*8+12),fine=Mathf.PerlinNoise(u*128+7,v*128+23);
    float h;
    if(wood){float grain=Mathf.Sin((u*78+Mathf.PerlinNoise(u*5,v*9)*1.3f)*6.28f);float split=Mathf.Pow(Mathf.Max(0,grain),18)*Mathf.SmoothStep(.3f,.7f,Mathf.PerlinNoise(u*55,v*3+19));h=.5f+broad*.22f+grain*.035f-split*.32f+fine*.055f;pixels[y*n+x]=new Color(.29f,.26f,.205f)*(.68f+h*.7f);}
    else{float crack=Mathf.Pow(Mathf.Clamp01(1-Mathf.Abs(Mathf.PerlinNoise(u*19+4,v*17+8)-.5f)*30),3);h=.28f+broad*.32f+fine*.13f-crack*.22f;float stain=Mathf.PerlinNoise(u*6,v*12+11);pixels[y*n+x]=new Color(.43f,.42f,.37f)*(.65f+h*.65f)-new Color(.035f,.025f,.025f)*stain;}
    heights[y*n+x]=h;pixels[y*n+x].a=1;
   }
   for(int y=0;y<n;y++)for(int x=0;x<n;x++){float dx=heights[y*n+Mathf.Min(n-1,x+1)]-heights[y*n+Mathf.Max(0,x-1)],dy=heights[Mathf.Min(n-1,y+1)*n+x]-heights[Mathf.Max(0,y-1)*n+x];var v=new Vector3(-dx*3,-dy*3,1).normalized;normal[y*n+x]=new Color(v.x*.5f+.5f,v.y*.5f+.5f,v.z*.5f+.5f,1);}
   Texture2D Write(string suffix,Color[] colors,bool bump){string file=ContactFolder265+"/"+id+suffix+".png";var t=new Texture2D(n,n,TextureFormat.RGBA32,false);t.SetPixels(colors);t.Apply();File.WriteAllBytes(file,t.EncodeToPNG());Object.DestroyImmediate(t);AssetDatabase.ImportAsset(file,ImportAssetOptions.ForceSynchronousImport);var importer=(TextureImporter)AssetImporter.GetAtPath(file);importer.textureType=bump?TextureImporterType.NormalMap:TextureImporterType.Default;importer.sRGBTexture=!bump;importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Repeat;importer.anisoLevel=8;importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(file);}
   var albedo=Write("_Albedo",pixels,false);var bumpMap=Write("_Normal",normal,true);var m=ContactAsset265(id,()=>new Material(Shader.Find("Oheangbu/CompactNaturalVegetation")));m.shader=Shader.Find("Oheangbu/CompactNaturalVegetation");m.SetTexture("_BaseMap",albedo);m.SetTexture("_BumpMap",bumpMap);m.SetFloat("_BumpScale",.7f);m.SetFloat("_AmbientFloor",.65f);m.SetFloat("_LightResponse",.55f);m.SetFloat("_Saturation",.75f);m.SetFloat("_WindAmplitude",0);m.SetFloat("_Height",3);m.SetFloat("_WashStrength",.2f);m.SetColor("_BaseColor",Color.white);EditorUtility.SetDirty(m);return m;
  }
  static string Ruins265()
  {
   var scene=FrontageScene249();if(scene.isDirty)throw new Exception("Clean candidate required");Directory.CreateDirectory(ContactFolder265);
   var ground=FinalSurface(scene);var wood=RuinMaterial265("SplitTimber",true);var stone=RuinMaterial265("ErodedMasonry",false);
   var root=scene.GetRootGameObjects().Single(g=>g.name==PropsRoot264).transform;
   foreach(string name in new[]{"AbandonedHome264","CollapsedShelter264"}){
    var site=root.Find(name);var old=site.Find("RuinFabric265");if(old!=null)Object.DestroyImmediate(old.gameObject);
    foreach(var t in site.Cast<Transform>().Where(t=>t.name.StartsWith("BrokenWallCourse")||t.name.StartsWith("CollapsedRafter")||t.name=="SurvivingRoofFragment").ToArray())Object.DestroyImmediate(t.gameObject);
    var fabric=new GameObject("RuinFabric265").transform;fabric.SetParent(site,false);
    GameObject MeshPart(string id,List<Vector3> v,List<int> tri,Material mat,bool collider){var mesh=ContactAsset265(name+"_"+id,()=>new Mesh());mesh.Clear();mesh.SetVertices(v);mesh.SetTriangles(tri,0);mesh.SetUVs(0,v.Select(p=>new Vector2((p.x+p.z)*.45f,p.y*.45f)).ToList());mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.RecalculateTangents();EditorUtility.SetDirty(mesh);var g=new GameObject(id,typeof(MeshFilter),typeof(MeshRenderer));g.transform.SetParent(fabric,false);g.GetComponent<MeshFilter>().sharedMesh=mesh;g.GetComponent<Renderer>().sharedMaterial=mat;if(collider)g.AddComponent<MeshCollider>().sharedMesh=mesh;return g;}
    Vector3 Ground(Vector3 p,float offset){var w=site.TransformPoint(p);w.y=ground(w.x,w.z).point.y+offset;return site.InverseTransformPoint(w);}
    void Wall(string id,float x0,float x1,float z,float height,int seed){var v=new List<Vector3>();var tr=new List<int>();const int count=12;
     float[] fracture={.82f,.80f,.86f,.78f,.74f,.42f,.46f,.39f,.35f,.25f,.28f,.13f};
     for(int i=0;i<count;i++){float x=Mathf.Lerp(x0,x1,i/(float)(count-1));float h=height*fracture[seed%2==0?count-1-i:i]+.045f*Mathf.PerlinNoise(i*1.1f+seed,4);
      foreach(float side in new[]{-.19f,.19f}){v.Add(Ground(new Vector3(x,0,z+side),-.32f));v.Add(Ground(new Vector3(x,0,z+side),h));}
      if(i>0){int a=(i-1)*4,b=i*4;tr.AddRange(new[]{a,a+1,b+1,a,b+1,b,a+2,b+3,a+3,a+2,b+2,b+3,a+1,a+3,b+3,a+1,b+3,b+1,a,b+2,a+2,a,b,b+2});}}
     tr.AddRange(new[]{0,2,3,0,3,1});int last=v.Count-4;tr.AddRange(new[]{last,last+1,last+3,last,last+3,last+2});MeshPart(id,v,tr,stone,true);}
    Wall("FracturedBackWallLeft",-2.8f,-.15f,-2.25f,1.30f,3);Wall("FracturedBackWallRight",.45f,2.65f,-2.25f,.95f,8);Wall("LowCollapsedReturn",-3.5f,-2.6f,-.9f,.60f,5);
    // Fewer, irregular fallen members replace the repeated straight row.
    for(int i=0;i<7;i++){float angle=i*2.37f;var a=Ground(new Vector3(Mathf.Sin(angle)*2.3f,0,Mathf.Cos(angle)*1.9f),.018f);var b=Ground(new Vector3(a.x+.7f+Mathf.Cos(angle)*1.1f,0,a.z+1.2f+Mathf.Sin(angle)*.9f),.015f);var d=(b-a).normalized;var right=Vector3.Cross(d,Vector3.up).normalized*.075f;var up=Vector3.Cross(right,d).normalized*.06f;var v=new List<Vector3>();
     for(int j=0;j<4;j++){var p=Vector3.Lerp(a,b,j/3f);for(int k=0;k<4;k++){float sx=k%2==0?-1:1,sy=k<2?-1:1;v.Add(p+right*sx+up*sy+(j==3?d*((k%3)*.045f):Vector3.zero));}}
     var tr=new List<int>();for(int j=0;j<3;j++){int n=j*4;foreach(var edge in new[]{(0,1),(1,3),(3,2),(2,0)})tr.AddRange(new[]{n+edge.Item1,n+edge.Item2,n+edge.Item2+4,n+edge.Item1,n+edge.Item2+4,n+edge.Item1+4});}tr.AddRange(new[]{0,2,1,1,2,3,12,13,14,13,15,14});MeshPart("SplinteredFallenBeam"+i,v,tr,wood,false);
    }
    foreach(var r in site.GetComponentsInChildren<Renderer>()){if(r.transform.IsChildOf(fabric)||r.transform.IsChildOf(site.Find("BuriedRubble264")))continue;if(r.name.Contains("Timber")||r.name.Contains("Lintel")||r.transform.parent.name=="FallenLattice")r.sharedMaterials=r.sharedMaterials.Select(_=>wood).ToArray();}
   }
   var ledger=JsonUtility.FromJson<PropLedger264>(File.ReadAllText(PropsOut264+"/placements.json"));foreach(var e in ledger.sites){var t=root.Find(e.id);var rs=t.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);e.size=b.size;}File.WriteAllText(PropsOut264+"/placements.json",JsonUtility.ToJson(ledger,true));
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);return "Two ruins: irregular breached masonry, seven splintered fallen beams each, private 512px albedo + normal textures. Existing burial and scatter preserved.";
  }
 }
}
