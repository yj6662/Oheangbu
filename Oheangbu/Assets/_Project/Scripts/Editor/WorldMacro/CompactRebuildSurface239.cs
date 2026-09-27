using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Oheangbu.App.World;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro {
 public static partial class CompactRebuildAuthoring {
 static string Surface239(string command) {
  var scene=SceneManager.GetActiveScene();var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
  if(scene.path!=receipt.scene)throw new Exception("Open candidate first");
  string dir=Output+"/Surface239";Directory.CreateDirectory(dir);
  var roots=scene.GetRootGameObjects();var session=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
  var mine=roots.Single(g=>g.name=="mine");string folder=Path.GetDirectoryName(receipt.scene).Replace('\\','/')+"/Surface239";
  if(command.StartsWith("capture:")) {
   string label=command.Substring(8);var go=new GameObject("Temporary_Surface239Camera");var camera=go.AddComponent<Camera>();camera.CopyFrom(session.Walker.ViewCamera);camera.enabled=false;EditorUtility.CopySerialized(session.Walker.ViewCamera.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());
   var rt=new RenderTexture(1920,1080,24);var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);var previous=RenderTexture.active;
   var start=session.Content.StartFeet;var positions=new[]{start+new Vector3(0,1.65f,-1.2f),start+new Vector3(-1,1.25f,1),start+new Vector3(0,1.2f,3)};
   var targets=new[]{start+new Vector3(0,1.2f,7),start+new Vector3(-5,1.1f,3),start+new Vector3(0,0,5)};
   try{camera.targetTexture=rt;camera.aspect=16f/9;camera.fieldOfView=65;for(int i=0;i<positions.Length;i++){camera.transform.SetPositionAndRotation(positions[i],Quaternion.LookRotation(targets[i]-positions[i]));camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();File.WriteAllBytes(dir+"/"+label+"_"+i+".png",tex.EncodeToPNG());}}
   finally{camera.targetTexture=null;RenderTexture.active=previous;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);}return "Three same-position cave surface captures "+label;
  }
  if(command=="audit") {
   var lines=new List<string>();void Check(bool pass,string label)=>lines.Add((pass?"PASS ":"FAIL ")+label);
   var walls=mine.GetComponentsInChildren<Renderer>().Where(r=>r.name=="Natural_Cave_Interior"||r.name=="Natural_Cave_Floor").ToArray();
   Check(walls.Length==2&&walls.All(r=>r.sharedMaterial.shader.name=="Oheangbu/Compact/SurfaceRelief"),"floor and walls use height/normal/cavity material");
   Check(walls.All(r=>r.sharedMaterial.GetTexture("_Relief")?.width==2048&&r.sharedMaterial.GetTexture("_ReliefNormal")?.width==2048),"2048 relief and physical normals assigned");
   Check(walls.All(r=>!ShaderUtil.ShaderHasError(r.sharedMaterial.shader)&&r.sharedMaterial.shader.isSupported),"surface shader supported and compiled");
   var stones=mine.transform.Find("Surface239_Gravel");Check(stones!=null&&stones.GetComponentsInChildren<MeshRenderer>().Length>0,"physical gravel batches present");
   Check(stones!=null&&stones.GetComponentsInChildren<Collider>().Length==0,"decorative small stones cannot block traversal");
   Check(session.Content.SaveSlot=="world-demo-compact-cave-v4","same gameplay save slot");
   string report=string.Join("\n",lines);File.WriteAllText(dir+"/audit.txt",report);return report;
  }
  if(command!="build"||EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit build required");
  DevSceneKit.EnsureFolder(folder);DevSceneKit.EnsureFolder(folder+"/Textures");
  Texture2D Import(string name,bool normal) {
   string path=folder+"/Textures/"+name+".png";File.Copy(dir+"/Textures/"+name+".png",path,true);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
   var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;importer.sRGBTexture=false;importer.maxTextureSize=2048;importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Repeat;importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=8;importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
  }
  var gravel=Import("Gravel_Relief",false);var gravelN=Import("Gravel_Normal",true);var rock=Import("Fracture_Relief",false);var rockN=Import("Fracture_Normal",true);
  string natural="Assets/_Project/Art/World/WorldCompact/NaturalSurface/Textures/";
  foreach(var r in mine.GetComponentsInChildren<Renderer>().Where(r=>!r.name.StartsWith("Gravel_")&&r.sharedMaterial!=null&&(r.sharedMaterial.shader.name=="Oheangbu/Compact/CaveRock"||r.sharedMaterial.shader.name=="Oheangbu/Compact/SurfaceRelief")).ToArray()) {
   bool floor=r.name=="Natural_Cave_Floor";var original=r.sharedMaterial;var material=new Material(Shader.Find("Oheangbu/Compact/SurfaceRelief"));material.SetColor("_BaseColor",original.GetColor("_BaseColor"));material.SetFloat("_Ambient",original.GetFloat("_Ambient"));material.SetFloat("_Floor",floor?1:0);material.SetFloat("_Height",floor?.012f:.045f);material.SetFloat("_Scale",floor?.5f:.25f);material.SetFloat("_NormalStrength",floor?.55f:1);
   material.SetTexture("_Relief",floor?gravel:rock);material.SetTexture("_ReliefNormal",floor?gravelN:rockN);string stem=floor?"T_Dirt_1":"T_RockGround_1";material.SetTexture("_FineMap",AssetDatabase.LoadAssetAtPath<Texture2D>(natural+stem+"_BC.png"));material.SetTexture("_FineNormal",AssetDatabase.LoadAssetAtPath<Texture2D>(natural+stem+"_N.png"));r.sharedMaterial=SurveyMaterial(material,folder+(floor?"/Floor.mat":"/Rock.mat"));
  }
  var groundMaterials=roots.SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&m.shader.name=="Oheangbu/Study/InkPaintingGround"&&m.GetFloat("_RebuildDetailStrength")>0).Distinct().ToArray();
  foreach(var m in groundMaterials){m.SetTexture("_RebuildGravel",gravel);m.SetTexture("_RebuildGravelNormal",gravelN);m.SetFloat("_RebuildGravelStrength",1);EditorUtility.SetDirty(m);}
  var previousGravel=mine.transform.Find("Surface239_Gravel");if(previousGravel!=null)Object.DestroyImmediate(previousGravel.gameObject);
  var root=new GameObject("Surface239_Gravel");root.transform.SetParent(mine.transform,false);root.transform.position=Vector3.zero;
  var collider=mine.GetComponentsInChildren<MeshCollider>().Single(c=>c.name=="Natural_Cave_Floor");var mesh=collider.sharedMesh;var vertices=mesh.vertices;var tri=mesh.triangles;var areas=new float[tri.Length/3];float total=0;
  for(int i=0;i<areas.Length;i++){var a=collider.transform.TransformPoint(vertices[tri[i*3]]);var b=collider.transform.TransformPoint(vertices[tri[i*3+1]]);var c=collider.transform.TransformPoint(vertices[tri[i*3+2]]);total+=Vector3.Cross(b-a,c-a).magnitude*.5f;areas[i]=total;}
  var random=new System.Random(239);float Next()=> (float)random.NextDouble();var groups=new Dictionary<Vector2Int,List<Vector3>>();
  int count=Mathf.Clamp(Mathf.RoundToInt(total*1.8f),500,6500);
  for(int i=0;i<count;i++){int t=Array.BinarySearch(areas,Next()*total);if(t<0)t=~t;t=Math.Min(t,areas.Length-1);float u=Mathf.Sqrt(Next()),v=Next();var p=collider.transform.TransformPoint(vertices[tri[t*3]]*(1-u)+vertices[tri[t*3+1]]*(u*(1-v))+vertices[tri[t*3+2]]*(u*v));var key=new Vector2Int(Mathf.FloorToInt(p.x/16),Mathf.FloorToInt(p.z/16));if(!groups.TryGetValue(key,out var list)){list=new List<Vector3>();groups.Add(key,list);}list.Add(p);}
  var stoneMaterial=new Material(AssetDatabase.LoadAssetAtPath<Material>(folder+"/Rock.mat"));stoneMaterial.SetColor("_BaseColor",mine.GetComponentsInChildren<Renderer>().Single(r=>r.name=="Natural_Cave_Floor").sharedMaterial.GetColor("_BaseColor"));stoneMaterial.SetFloat("_Ambient",.34f);stoneMaterial.SetFloat("_NormalStrength",.4f);stoneMaterial=SurveyMaterial(stoneMaterial,folder+"/LooseStone.mat");
  foreach(var pair in groups){var vs=new List<Vector3>();var ts=new List<int>();foreach(var p in pair.Value){float radius=Mathf.Lerp(.025f,.085f,Next()),height=radius*Mathf.Lerp(.25f,.65f,Next()),turn=Next()*6.28f;int n=vs.Count;vs.Add(p+Vector3.up*height);for(int k=0;k<6;k++){float a=turn+k*Mathf.PI/3;float rr=radius*Mathf.Lerp(.65f,1.2f,Next());vs.Add(p+new Vector3(Mathf.Cos(a)*rr,height*.30f,Mathf.Sin(a)*rr));}for(int k=0;k<6;k++){ts.Add(n);ts.Add(n+1+(k+1)%6);ts.Add(n+1+k);}}
   // Split corners so each chip keeps small broken faces instead of a smooth dome.
   var flat=ts.Select(i=>vs[i]).ToArray();var chipMesh=new Mesh{name="LooseGravel239",indexFormat=IndexFormat.UInt32};chipMesh.vertices=flat;chipMesh.triangles=Enumerable.Range(0,flat.Length).ToArray();chipMesh.RecalculateNormals();chipMesh.RecalculateBounds();chipMesh=ArtMesh(chipMesh,folder+"/Gravel_"+pair.Key.x+"_"+pair.Key.y+".asset");var go=new GameObject("Gravel_"+pair.Key);go.transform.SetParent(root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=chipMesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=stoneMaterial;var lod=go.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.12f,new Renderer[]{renderer})});lod.RecalculateBounds();}
  AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);string result="Cave physical relief + "+count+" loose stones in "+groups.Count+" batches; exterior ground materials="+groundMaterials.Length+"; existing base colours and traversal meshes retained";File.WriteAllText(dir+"/build.txt",result);return result;
 }
 }
}
