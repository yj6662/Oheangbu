using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string Out276="../Art/World/Compact/Rebuild/Surface276";
  const string Assets276="Assets/_Project/Art/World/Surface276";
  public static string Surface276(string command)
  {
   Directory.CreateDirectory(Out276);var scene=FrontageScene249();
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
   var renderers=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
   var ground=renderers.Where(r=>r.sharedMaterials.Any(m=>m!=null&&m.shader.name=="Oheangbu/Study/InkPaintingGround"&&m.GetFloat("_GroundKind")<.5f)).ToArray();
   if(command=="inspect")return string.Join("\n",ground.Select(r=>r.name+" : "+string.Join(",",r.sharedMaterials.Select(m=>AssetDatabase.GetAssetPath(m)))).Distinct());
   if(command.StartsWith("capture:"))return CaptureSurface276(command.Substring(8));
   if(command=="check")return CheckSurface276(ground);
   if(command!="build"||scene.isDirty)throw new Exception("Clean candidate required");
   string snapshot=Out276+"/Recovery";Directory.CreateDirectory(snapshot);
   if(!File.Exists(snapshot+"/"+Path.GetFileName(scene.path)))File.Copy(scene.path,snapshot+"/"+Path.GetFileName(scene.path));
   if(!File.Exists(Out276+"/geometry-before.txt"))File.WriteAllLines(Out276+"/geometry-before.txt",SurfaceGeometry276());
   ValidateHeight276();
   string fieldPath=Assets276+"/Morphology.png";
   AssetDatabase.ImportAsset(fieldPath,ImportAssetOptions.ForceSynchronousImport);
   var importer=(TextureImporter)AssetImporter.GetAtPath(fieldPath);importer.sRGBTexture=false;importer.mipmapEnabled=false;importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=1024;importer.npotScale=TextureImporterNPOTScale.None;importer.isReadable=true;importer.SaveAndReimport();
   string relief=Path.GetDirectoryName(scene.path).Replace('\\','/')+"/Surface239/Textures/";
   var map=new Dictionary<Material,Material>();
   foreach(var r in ground)
   {
    var materials=r.sharedMaterials;
    for(int i=0;i<materials.Length;i++)
    {
     var source=materials[i];if(source==null||source.shader.name!="Oheangbu/Study/InkPaintingGround"||source.GetFloat("_GroundKind")>.5f)continue;
     if(!map.TryGetValue(source,out var target))
     {
      var sourcePath=AssetDatabase.GetAssetPath(source);string path=sourcePath.StartsWith(Assets276+"/")?sourcePath:Assets276+"/Ground_"+AssetDatabase.AssetPathToGUID(sourcePath)+".mat";
      target=new Material(source){name=source.name.EndsWith("_Rules276")?source.name:source.name+"_Rules276"};
      target.SetFloat("_StrataStrength276",1);target.SetVector("_StrataRange276",new Vector4(160,650,0,0));
      target.SetTexture("_StrataField276",AssetDatabase.LoadAssetAtPath<Texture2D>(fieldPath));
      target.SetTexture("_StrataFracture276",AssetDatabase.LoadAssetAtPath<Texture2D>(relief+"Fracture_Relief.png"));
      target.SetTexture("_StrataFractureNormal276",AssetDatabase.LoadAssetAtPath<Texture2D>(relief+"Fracture_Normal.png"));
      target=SurveyMaterial(target,path);map.Add(source,target);
     }
     materials[i]=target;
    }
    r.sharedMaterials=materials;
   }
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   string result="Rule surfaces on "+ground.Length+" renderers / "+map.Count+" private material(s). Geometry, road mask, grass field, save and navigation unchanged.";
   File.WriteAllText(Out276+"/build.txt",result);return result;
  }
  static string[] SurfaceGeometry276()
  {
   var roots=FrontageScene249().GetRootGameObjects();
   var lines=roots.SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Select(m=>"mesh "+GlobalObjectId.GetGlobalObjectIdSlow(m)+" "+AssetDatabase.GetAssetPath(m.sharedMesh)).Concat(
    roots.SelectMany(g=>g.GetComponentsInChildren<MeshCollider>(true)).Select(m=>"collider "+GlobalObjectId.GetGlobalObjectIdSlow(m)+" "+AssetDatabase.GetAssetPath(m.sharedMesh)));
   return lines.OrderBy(v=>v).ToArray();
  }
  static void ValidateHeight276()
  {
   var bytes=File.ReadAllBytes(Output+"/Cartography/heights.f32");var ground=FinalSurface(FrontageScene249());var lines=new List<string>();float max=0;int count=0;
   // Cell-centre samples across all five realms, not just the first camera.
   for(int z=123;z<2350;z+=217)for(int x=97;x<1570;x+=173)
   {
    float expected=BitConverter.ToSingle(bytes,(z*1600+x)*4);var hit=ground((x+.5f)*2.5f,(z+.5f)*2.5f);
    if(hit.collider==null)throw new Exception("Missing terrain at field sample");
    max=Mathf.Max(max,Mathf.Abs(expected-hit.point.y));count++;
   }
   File.WriteAllText(Out276+"/height-check.txt",count+" final collider samples; maximum difference "+max+" m");
   if(max>.12f)throw new Exception("Height snapshot stale: "+max);
  }
  static string CheckSurface276(MeshRenderer[] ground)
  {
   ValidateHeight276();var lines=new List<string>();
   void C(bool pass,string label){lines.Add((pass?"PASS ":"FAIL ")+label);}
   var mats=ground.SelectMany(r=>r.sharedMaterials).Distinct().Where(m=>m!=null&&m.shader.name=="Oheangbu/Study/InkPaintingGround").ToArray();
   C(ground.Length>=96,"whole split terrain bound, not one showcase mesh");
   C(mats.All(m=>AssetDatabase.GetAssetPath(m).StartsWith(Assets276+"/")&&m.GetFloat("_StrataStrength276")==1),"private candidate materials opt in");
   C(mats.All(m=>m.GetTexture("_StrataField276")!=null&&m.GetTexture("_StrataFracture276")!=null&&m.GetTexture("_StrataFractureNormal276")!=null),"morphology and matching joint relief bound");
   C(mats.All(m=>m.shader.isSupported&&!ShaderUtil.ShaderHasError(m.shader)),"terrain shader supported, no compilation errors");
   C(mats.All(m=>m.FindPass("DepthNormals")>=0&&m.FindPass("ShadowCaster")>=0),"depth normals and shadow passes retained");
   C(mats.All(m=>{
    var guid=Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(m)).Substring("Ground_".Length);
    var original=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
    return original!=null&&new[]{"_GroundPathMask","_GrassCover267","_RealmPigment"}.All(p=>m.GetTexture(p)==original.GetTexture(p))&&m.GetFloat("_GrassCoverStrength267")==original.GetFloat("_GrassCoverStrength267");
   }),"path/meadow/pigment masks and optional cover weights identical to source");
   C(mats.All(m=>m.GetFloat("_RebuildScreenFog")==1),"distance fog still camera-owned");
   C(SurfaceGeometry276().SequenceEqual(File.ReadAllLines(Out276+"/geometry-before.txt")),"all candidate mesh/collider references identical");
   C(VillageSession().Content.SaveSlot=="world-demo-compact-cave-v4","save slot unchanged");
   C(AssetDatabase.FindAssets("t:Material",new[]{"Assets/_Project"}).Select(AssetDatabase.GUIDToAssetPath).Where(p=>!p.StartsWith(Assets276+"/")).Select(AssetDatabase.LoadAssetAtPath<Material>).Where(m=>m!=null&&m.shader.name=="Oheangbu/Study/InkPaintingGround").All(m=>m.GetFloat("_StrataStrength276")==0),"other materials keep old look by default");
   var field=AssetDatabase.LoadAssetAtPath<Texture2D>(Assets276+"/Morphology.png");
   C(field.width==512&&field.height==768&&field.mipmapCount==1&&field.wrapMode==TextureWrapMode.Clamp,"linear field extent and filtering");
   var sampleGround=FinalSurface(FrontageScene249());float fieldHeightError=0;
   for(int z=700;z<5300;z+=600)for(int x=600;x<3700;x+=600)
    fieldHeightError=Mathf.Max(fieldHeightError,Mathf.Abs(field.GetPixelBilinear(x/4000f,z/6000f).b*500-sampleGround(x,z).point.y));
   C(fieldHeightError<8,"field UV orientation and height match live terrain; max coarse error="+fieldHeightError+"m");
   C(field.GetPixels().Any(c=>c.r<.3f)&&field.GetPixels().Any(c=>c.r>.7f),"field contains both accumulation hollows and convex crests");
   var shader=File.ReadAllText("Assets/_Project/Shaders/InkPaintingStudy/InkPaintingGround.shader");
   C(shader.Contains("RuleNormal276(i.world")&&shader.Contains("RuleNormal276(p,n"),"forward and depth normal paths share relief calculation");
   File.WriteAllLines(Out276+"/checks.txt",lines);return string.Join("\n",lines);
  }
  static string CaptureSurface276(string tag)
  {
   var scene=FrontageScene249();var roots=scene.GetRootGameObjects();var session=VillageSession();var ground=FinalSurface(scene);
   var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();var observer=art.Observer;
   var go=new GameObject("Surface276 review");var camera=go.AddComponent<Camera>();camera.CopyFrom(session.Walker.ViewCamera);camera.enabled=false;camera.useOcclusionCulling=false;
   EditorUtility.CopySerialized(session.Walker.ViewCamera.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());art.Observer=camera;
   // A downhill-facing camera looking into the slope, at three increasing distances.
   var hit=ground(3516,2526);var center=hit.point;var outward=new Vector3(hit.normal.x,0,hit.normal.z).normalized;
   var cliff=ground(3783.75f,1901.25f);var cliffOut=new Vector3(cliff.normal.x,0,cliff.normal.z).normalized;
   var eyes=new[]{center+outward*5+Vector3.up*2,center+outward*22+Vector3.up*9,new Vector3(3410,140,1880),new Vector3(3250,205,2260),cliff.point+cliffOut*7+Vector3.up*2};
   var targets=new[]{center+Vector3.up*.5f,center,new Vector3(3020,85,2280),new Vector3(2820,130,2850),cliff.point+Vector3.up*1.5f};
   var rt=new RenderTexture(1920,1080,24);var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);var prior=RenderTexture.active;
   try
   {
    camera.targetTexture=rt;camera.aspect=16f/9;camera.fieldOfView=62;
    for(int i=0;i<eyes.Length;i++){camera.transform.SetPositionAndRotation(eyes[i],Quaternion.LookRotation(targets[i]-eyes[i]));camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();File.WriteAllBytes(Out276+"/"+tag+"-"+i+".png",tex.EncodeToPNG());}
   }finally{art.Observer=observer;camera.targetTexture=null;RenderTexture.active=prior;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);}
   return "Five matched 1080p offscreen terrain views: "+tag;
  }
 }
}
