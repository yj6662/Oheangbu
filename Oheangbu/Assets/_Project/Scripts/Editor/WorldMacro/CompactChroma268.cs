using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering.Universal;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  public static string Chroma268(string command)
  {
   if(command!="apply")throw new ArgumentException(command);
   var scene=FrontageScene249();if(scene.isDirty)throw new Exception("Saved candidate required");
   const string output=Output+"/Chroma268";Directory.CreateDirectory(output+"/Before");
   var roots=scene.GetRootGameObjects();var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();var grass=roots.SelectMany(g=>g.GetComponentsInChildren<CompactGrassRenderer266>()).Single();
   var realm=roots.SelectMany(g=>g.GetComponentsInChildren<WorldLocationArrival>()).Single().Catalog.Entries.Single(e=>e.Id=="realm_cheongrim");
   var rect=new Vector4(realm.Polygon.Min(p=>p.x),realm.Polygon.Min(p=>p.y),realm.Polygon.Max(p=>p.x),realm.Polygon.Max(p=>p.y));
   var materials=art.Sheet.Prototypes.Where(p=>p.Category==Sheet.Kind.Grass||p.Category==Sheet.Kind.Shrub).SelectMany(p=>p.Lods).SelectMany(l=>l.Parts).Select(p=>p.Material).ToList();materials.Add(grass.Field.Material);
   string folder=Path.GetDirectoryName(scene.path).Replace('\\','/');
   materials.AddRange(roots.SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>()).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&m.shader.name=="Oheangbu/Study/InkPaintingGround"&&AssetDatabase.GetAssetPath(m).StartsWith(folder+"/")));
   var targets=materials.Where(m=>m!=null).Distinct().ToArray();
   if(targets.Any(m=>!AssetDatabase.GetAssetPath(m).StartsWith(folder+"/")||!m.HasProperty("_InkChromaRetain268")))throw new Exception("A material is shared outside candidate or shader property missing");
   var paths=targets.Select(m=>AssetDatabase.GetAssetPath(m)).ToArray();for(int i=0;i<paths.Length;i++)if(!File.Exists(output+"/Before/"+i+".asset"))File.Copy(paths[i],output+"/Before/"+i+".asset");File.WriteAllLines(output+"/materials.txt",paths);
   var observer=art.Observer;var ground=FinalSurface(scene);var source=VillageSession().Walker.ViewCamera;var go=new GameObject("268 fixed colour review"){hideFlags=HideFlags.HideAndDontSave};var cam=go.AddComponent<Camera>();cam.CopyFrom(source);cam.enabled=false;EditorUtility.CopySerialized(source.GetUniversalAdditionalCameraData(),cam.GetUniversalAdditionalCameraData());go.AddComponent<Skybox>().material=AssetDatabase.LoadAssetAtPath<Material>(Folder263+"/Sky.asset");
   var rt=new RenderTexture(1920,1080,24);var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);var previous=RenderTexture.active;
   var points=new[]{new Vector2(3078,2240),new Vector2(3010,2785),new Vector2(3260,3060),new Vector2(2700,2155)};
   try{art.Observer=cam;art.ContactPreviewClock265=10;grass.WindPreviewClock267=10;cam.targetTexture=rt;cam.aspect=16f/9;cam.fieldOfView=60;
    for(int view=0;view<points.Length;view++){
     var p=points[view];var eye=ground(p.x,p.y).point+Vector3.up*1.7f;var target=ground(p.x+8,p.y+15).point+Vector3.up*.5f;cam.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));
     for(int after=0;after<2;after++){
      foreach(var m in targets){m.SetVector("_InkChromaRect268",rect);m.SetFloat("_InkChromaRetain268",after==0?1:.2f);}cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();File.WriteAllBytes(output+"/"+view+(after==0?"-before.png":"-after.png"),tex.EncodeToPNG());
     }
    }
   }finally{foreach(var m in targets){m.SetVector("_InkChromaRect268",rect);m.SetFloat("_InkChromaRetain268",.2f);EditorUtility.SetDirty(m);}grass.WindPreviewClock267=-1;art.Observer=observer;art.ContactPreviewClock265=-1;cam.targetTexture=null;RenderTexture.active=previous;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);}
   bool valid=targets.All(m=>m.shader.isSupported&&!ShaderUtil.ShaderHasError(m.shader)&&Mathf.Abs(m.GetFloat("_InkChromaRetain268")-.2f)<.0001f);
   string result=(valid?"PASS ":"FAIL ")+targets.Length+" private grass/understory/ground materials; chroma retention 0.2 inside "+rect+"; luminance-preserving before existing atmosphere. No new texture samples or draw calls. Four fixed wind/camera before-after pairs.";File.WriteAllText(output+"/result.txt",result);return result;
  }
 }
}
