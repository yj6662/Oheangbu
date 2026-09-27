using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using Oheangbu.App.World;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static string GrassChecks266()
  {
   var scene=FrontageScene249();var renderers=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactGrassRenderer266>()).ToArray();var r=renderers.Single();var f=r.Field;var lines=new List<string>();void C(bool ok,string msg)=>lines.Add((ok?"PASS ":"FAIL ")+msg);
   C(renderers.Length==1,"one renderer for world field");C(r.GetComponentsInChildren<Collider>().Length==0&&r.GetComponentsInChildren<Rigidbody>().Length==0,"no per-grass objects or physics bodies");
   C(f.Count>100000&&f.Count==f.Cells.Where(c=>c!=null).Sum(c=>c.Seeds.Length),"world coverage and seed count: "+f.Count);
   C(f.Cells.Length==f.Columns*f.Rows,"complete spatial index");C(r.Art.Contacts.GrassField==f,"shared soft contact field linked");
   C(f.Material.enableInstancing&&f.Material.shader.isSupported&&!ShaderUtil.ShaderHasError(f.Material.shader),"instancing and shader compile");C(f.Material.GetFloat("_AlphaClip")==0,"opaque blades avoid alpha overdraw");
   C(f.NearMesh.triangles.Length/3==384&&f.FarMesh.triangles.Length/3==144,"bounded 384/144 triangle clusters");C(f.FarDistance<=80&&f.NearDistance<=30,"bounded detail distances");
   var ground=FinalSurface(scene);var masks=GrassMasks266();int samples=0;float gap=0;bool safe=true,normal=true,valid=true;var points=new List<Vector3>();
   for(int id=0;id<f.Cells.Length;id++){var cell=f.Cells[id];if(cell==null||cell.Seeds.Length==0)continue;foreach(var s in cell.Seeds){var coordinate=f.Coordinate(s.Position);if(f.Index(coordinate.x,coordinate.y)!=id||!cell.Bounds.Contains(s.Position))valid=false;}
    if(id%11!=0)continue;var seed=cell.Seeds[cell.Seeds.Length/2];gap=Mathf.Max(gap,Mathf.Abs(ground(seed.Position.x,seed.Position.z).point.y-seed.Position.y));if(masks.Any(m=>m.Contains(seed.Position)))safe=false;if(seed.NormalXZ.sqrMagnitude>1-.79f*.79f+.001f)normal=false;points.Add(seed.Position);samples++;}
   C(valid,"all seeds belong to their bounded cell");C(gap<.025f,"fresh surface sample grounding <=2.5cm: "+samples+" max="+gap);C(safe,"fresh roads, water, structures and service exclusions");C(normal,"slope limit respected");
   C(points.All(p=>f.HasGrass(p,.2f)),"spatial contact lookup finds sampled grass");C(points.All(p=>!f.HasGrass(p+Vector3.up*2000,1)),"contact respects vertical separation");C(!f.HasGrass(new Vector3(-1000,0,-1000),1),"out of world contact empty");
   C(VillageSession().Content.SaveSlot=="world-demo-compact-cave-v4","save slot retained");
   File.WriteAllText(GrassOut266+"/checks.txt",string.Join("\n",lines));return string.Join("\n",lines);
  }
  static string GrassCapture266()
  {
   var scene=FrontageScene249();var renderer=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactGrassRenderer266>()).Single();var art=renderer.Art;var observer=art.Observer;var ground=FinalSurface(scene);var session=VillageSession();
   var points=new[]{new Vector2(3078,2240),new Vector2(3010,2785),new Vector2(3260,3060),new Vector2(2700,2155)};
   var go=new GameObject("266 offscreen review"){hideFlags=HideFlags.HideAndDontSave};var cam=go.AddComponent<Camera>();cam.CopyFrom(session.Walker.ViewCamera);cam.enabled=false;EditorUtility.CopySerialized(session.Walker.ViewCamera.GetUniversalAdditionalCameraData(),cam.GetUniversalAdditionalCameraData());go.AddComponent<Skybox>().material=AssetDatabase.LoadAssetAtPath<Material>(Folder263+"/Sky.asset");
   var rt=new RenderTexture(1920,1080,24);var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);var previous=RenderTexture.active;bool wasEnabled=renderer.enabled;var lines=new List<string>();
   try{art.Observer=cam;art.ContactPreviewClock265=10;cam.targetTexture=rt;cam.aspect=16f/9;cam.fieldOfView=60;
    for(int view=0;view<points.Length;view++){
     var p=points[view];var eye=ground(p.x,p.y).point+Vector3.up*1.7f;var target=ground(p.x+8,p.y+15).point+Vector3.up*.5f;cam.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));
     for(int show=0;show<2;show++){
      renderer.enabled=show==1;for(int warm=0;warm<3;warm++)cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();File.WriteAllBytes(GrassOut266+"/"+view+(show==0?"-before.png":"-after.png"),tex.EncodeToPNG());
      if(show==1){if(renderer.CellsTested==0)throw new Exception("Grass renderer did not execute");var ms=new List<double>();for(int i=0;i<24;i++){cam.Render();ms.Add(renderer.LastCpuMs);}ms.Sort();lines.Add("view="+view+" testedCells="+renderer.CellsTested+" cached="+renderer.CachedCells+" submissions="+renderer.SubmittedInstances+" drawCalls="+renderer.DrawCalls+" triangles="+renderer.SubmittedTriangles+" warm renderer CPU median="+ms[12].ToString("F3")+"ms p95="+ms[22].ToString("F3")+"ms");}
     }
    }
   }finally{renderer.enabled=wasEnabled;art.Observer=observer;art.ContactPreviewClock265=-1;cam.targetTexture=null;RenderTexture.active=previous;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);}
   lines.Add("Editor offscreen rendering only. Warm CPU measures this grass culling/submission method, not total frame CPU/GPU. No input play or 120fps claim.");File.WriteAllText(GrassOut266+"/render-cost.txt",string.Join("\n",lines));return string.Join("\n",lines);
  }
 }
}
