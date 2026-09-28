using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.Demo;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  // No Play entry, input devices, focus changes or modification of the real player.
  public static string BranchBackground259(string command)
  {
   var scene=FrontageScene249();var s=VillageSession();var ground=FinalSurface(scene);
   var plan=JsonUtility.FromJson<ProgressionPlan251>(File.ReadAllText(BranchOutput259+"/route-plan.json"));
   if(command=="facts")
   {
    File.WriteAllText(BranchOutput259+"/facts-edit.txt","");void C(bool ok,string text)=>BranchCheck259(ok,text,"facts-edit");
    var profile=s.Content.Campaign;var state=new DemoCampaignState{CampaignId=profile.CampaignId};
    C(profile.DialogueFor("herbalist",state,"prior")=="prior","undiscovered evidence cannot unlock testimony");
    C(DemoCampaignProgression.TryAdvance(profile,state,DemoEventKind.Interaction,HerbTrace259,out var next,out var reward),"evidence accepted with no prior NPC contact");
    C(reward==0&&next.Facts.Contains(HerbFact259)&&!state.Facts.Contains(HerbFact259),"proposal adds fact without mutating accepted state or currency");
    C(!DemoCampaignProgression.TryAdvance(profile,next,DemoEventKind.Interaction,HerbTrace259,out _,out _),"repeat evidence cannot complete twice");
    C(profile.DialogueFor("herbalist",next,"prior").Contains("뿌리만 검었다고"),"later testimony selects prior discovery");
    var reordered=Object.Instantiate(profile);
    try{reordered.Stages=reordered.Stages.Reverse().ToArray();C(DemoCampaignProgression.TryAdvance(reordered,state,DemoEventKind.Interaction,HerbTrace259,out var other,out _)&&other.Facts.Contains(HerbFact259),"array order does not gate evidence");}finally{Object.DestroyImmediate(reordered);}
    string folder=BranchOutput259+"/FixtureSaves/"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(folder);string path=folder+"/evidence.json";
    var progress=WorldMacroProgress.CreateNew(s.Content.TerrainRevision,s.Content.StartFeet,0);progress.campaign=state;
    var store=new AtomicJsonStore<WorldMacroProgress>(path,WorldMacroProgress.Valid);store.Save(progress);
    var proposed=JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(progress));proposed.campaign=next;
    bool rejected=false;using(var file=new FileStream(path+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None))
    {try{store.Save(proposed);}catch(IOException){rejected=true;}}
    C(rejected&&!JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(path)).campaign.Facts.Contains(HerbFact259),"real atomic file failure retains prior disk fact state");
    store.Save(proposed);var loaded=WorldMacroSaveSlot.Inspect(folder,"evidence");
    C(loaded.Status==WorldMacroSaveSlotStatus.Primary&&loaded.Progress.campaign.Facts.Contains(HerbFact259),"retry and reload retain one permanent fact");
    C(loaded.Progress.campaign.Facts.Count(f=>f==HerbFact259)==1&&loaded.Progress.ledger.currency==progress.ledger.currency,"reload has no duplicate fact or unconfigured reward");
    File.AppendAllText(BranchOutput259+"/facts-edit.txt","SCOPE production campaign and atomic file contracts in Edit; live session rest/death/UI unverified. Fixture: "+path+"\n");
    return File.ReadAllText(BranchOutput259+"/facts-edit.txt");
   }
   var route=new List<Vector3>();foreach(var r in plan.routes)foreach(var p in r.points)route.Add(ground(p.x,p.y).point);
   if(command=="walk"||command=="reverse")
   {
    if(command=="reverse")route.Reverse();var source=s.Walker.Body;var probe=new GameObject("TemporaryBranchCollision259"){hideFlags=HideFlags.HideAndDontSave};
    var cc=probe.AddComponent<CharacterController>();cc.height=source.height;cc.radius=source.radius;cc.center=source.center;cc.slopeLimit=source.slopeLimit;cc.stepOffset=source.stepOffset;cc.skinWidth=source.skinWidth;cc.minMoveDistance=0;
    try
    {
     Physics.SyncTransforms();cc.enabled=false;probe.transform.position=route[0]+Vector3.up*.1f;cc.enabled=true;
     var trail=new List<Vector3>();int node=1,steps=0;float gravity=0,distance=0;Vector3 previousProgress=probe.transform.position;
     while(node<route.Count&&steps++<25000)
     {
      var before=probe.transform.position;var delta=route[node]-before;delta.y=0;
      if(delta.magnitude<.3f){node++;continue;}
      gravity=cc.isGrounded?-1:Mathf.Max(-30,gravity-.1962f);cc.Move(Vector3.ClampMagnitude(delta,.09f)+Vector3.up*(gravity*.02f));
      distance+=Vector3.Distance(before,probe.transform.position);
      if(steps%100==0){trail.Add(probe.transform.position);if(Vector3.Distance(previousProgress,probe.transform.position)<.1f)break;previousProgress=probe.transform.position;}
     }
     var receipt=new WalkReceipt{status=node>=route.Count?"PASS":"FAIL",scope="Edit-mode collision probe matching player capsule; actors/colliders retained. No real input, human wayfinding, live animation or frame-time claim.",steps=steps,simulatedSeconds=steps*.02f,distance=distance,start=route[0],end=probe.transform.position,target=route[Math.Min(node,route.Count-1)],trail=trail.ToArray()};
     File.WriteAllText(BranchOutput259+"/collision-"+command+".json",JsonUtility.ToJson(receipt,true));
     return receipt.status+" capsule "+command+" "+distance+"m end="+receipt.end+" target="+receipt.target;
    }
    finally{Object.DestroyImmediate(probe);Physics.SyncTransforms();}
   }
   if(command=="capture")
   {
    var go=new GameObject("TemporaryBranchCamera259"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();camera.CopyFrom(s.Walker.ViewCamera);camera.enabled=false;
    EditorUtility.CopySerialized(s.Walker.ViewCamera.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());
    var art=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();var observer=art.Observer;art.Observer=camera;
    var rt=new RenderTexture(1920,1080,24);var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);var active=RenderTexture.active;
    try
    {
     camera.targetTexture=rt;camera.aspect=16f/9;camera.fieldOfView=62;
     var bag=s.Content.Points.Single(p=>p.Id==HerbTrace259).Position;
     var eyes=new[]{route[0]-new Vector3(7,-2,6),bag+new Vector3(4,2,-4),route[route.Count-2]+Vector3.up*2};
     var targets=new[]{route[1]+Vector3.up,bag+Vector3.up*.3f,route.Last()+Vector3.up};
     for(int i=0;i<eyes.Length;i++){camera.transform.SetPositionAndRotation(eyes[i],Quaternion.LookRotation(targets[i]-eyes[i]));camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();File.WriteAllBytes(BranchOutput259+"/view-"+i+".png",tex.EncodeToPNG());}
     return "Three offscreen 1080p captures; no foreground window or cursor changes";
    }
    finally{art.Observer=observer;camera.targetTexture=null;RenderTexture.active=active;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);}
   }
   throw new ArgumentException(command);
  }
 }
}
