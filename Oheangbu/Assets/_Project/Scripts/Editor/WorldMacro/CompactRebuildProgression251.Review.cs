using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.App.Demo;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  public static string ProgressionWalk251()
  {
   var scene=FrontageScene249();var s=VillageSession();var source=s.Walker.Body;var ground=FinalSurface(scene);
   var plan=JsonUtility.FromJson<ProgressionPlan251>(File.ReadAllText(ProgressionOutput251+"/route-plan.json"));
   var disabled=s.Actors.Select(a=>a.gameObject).Concat(new[]{s.Walker.gameObject}).Where(g=>g.activeSelf).ToArray();
   var probe=new GameObject("Temporary251WalkProbe");var cc=probe.AddComponent<CharacterController>();cc.height=source.height;cc.radius=source.radius;cc.center=source.center;cc.slopeLimit=source.slopeLimit;cc.stepOffset=source.stepOffset;cc.skinWidth=source.skinWidth;cc.minMoveDistance=0;
   var route=new List<Vector3>();var trail=new List<Vector3>();File.WriteAllText(ProgressionOutput251+"/walk.txt","");
   try
   {
    foreach(var g in disabled)g.SetActive(false);Physics.SyncTransforms();
    foreach(var segment in plan.routes)foreach(var p in segment.points)
    {
     if(!NavMesh.SamplePosition(ground(p.x,p.y).point,out var h,3,NavMesh.AllAreas))throw new Exception("Missing walk node "+p);
     if(route.Count==0){route.Add(h.position);continue;}
     var nav=new NavMeshPath();if(!NavMesh.CalculatePath(route.Last(),h.position,NavMesh.AllAreas,nav)||nav.status!=NavMeshPathStatus.PathComplete)throw new Exception("Disconnected walk node "+p);route.AddRange(nav.corners.Skip(1));
    }
    cc.enabled=false;probe.transform.position=route[0]+Vector3.up*.1f;cc.enabled=true;Physics.SyncTransforms();int target=1,steps=0,stalled=0;float vertical=0,distance=0;
    while(target<route.Count&&steps++<90000)
    {
     var before=probe.transform.position;var d=route[target]-before;d.y=0;if(d.magnitude<.28f){target++;continue;}
     vertical=cc.isGrounded?-1:Mathf.Max(-30,vertical-.1962f);cc.Move(Vector3.ClampMagnitude(d,.09f)+Vector3.up*(vertical*.02f));
     float moved=Vector3.Distance(before,probe.transform.position);distance+=moved;stalled=moved<.0001f?stalled+1:0;if(steps%50==0)trail.Add(probe.transform.position);if(stalled>300)break;
    }
    var receipt=new WalkReceipt{status=target>=route.Count?"PASS":"FAIL",scope="Automatic collision-resolved capsule traversal; no native input, combat, wayfinding or performance claim",steps=steps,simulatedSeconds=steps*.02f,distance=distance,start=route[0],end=probe.transform.position,target=route[Math.Min(target,route.Count-1)],trail=trail.ToArray()};
    File.WriteAllText(ProgressionOutput251+"/walk.json",JsonUtility.ToJson(receipt,true));
    Check251(target>=route.Count,"village to logging, deep forest and sanctuary: "+distance+"m / simulated "+steps*.02f+"s / end="+probe.transform.position+" target="+receipt.target,"walk");
   }
   finally{Object.DestroyImmediate(probe);foreach(var g in disabled)if(g!=null)g.SetActive(true);Physics.SyncTransforms();}
   return File.ReadAllText(ProgressionOutput251+"/walk.txt");
  }
  public static string ProgressionCapture251()
  {
   var scene=FrontageScene249();var s=VillageSession();var ground=FinalSurface(scene);var site=Object.FindFirstObjectByType<DemoGukRevisitSite>();
   var eye=new[]{ground(3260,3523).point+Vector3.up*2,ground(3347,2684).point+Vector3.up*2,site.LiftPad.position+new Vector3(-6,2,-5)};
   var targets=new[]{s.Actors.Single(a=>a.Id=="cheongryong").transform.position+Vector3.up*2,s.Actors.Single(a=>a.Id=="demo_growth_lesson").transform.position,site.UpperSurface.position};
   var go=new GameObject("Temporary251ReviewCamera");var camera=go.AddComponent<Camera>();camera.CopyFrom(s.Walker.ViewCamera);camera.enabled=false;EditorUtility.CopySerialized(s.Walker.ViewCamera.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());
   var art=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();var previousObserver=art.Observer;art.Observer=camera;
   var rt=new RenderTexture(1920,1080,24);var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);var previous=RenderTexture.active;
   try{camera.targetTexture=rt;camera.aspect=16f/9;camera.fieldOfView=62;for(int i=0;i<eye.Length;i++){camera.transform.SetPositionAndRotation(eye[i],Quaternion.LookRotation(targets[i]-eye[i]));camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();File.WriteAllBytes(ProgressionOutput251+"/view_"+i+".png",tex.EncodeToPNG());}}
   finally{art.Observer=previousObserver;camera.targetTexture=null;RenderTexture.active=previous;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);}return "Three candidate 1080p views saved";
  }
 }
}
