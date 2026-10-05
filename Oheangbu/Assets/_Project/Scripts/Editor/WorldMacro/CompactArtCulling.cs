using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  // SPEC-ART-RENDERER-CULLING (`artcull`): for every sheet a scene renderer uses, the culled path must hand DrawMeshInstanced
  // exactly the matrices, order and counts of the former full scan. A hidden, unsaved renderer and camera do the work; no
  // scene or asset is changed. Timings are Editor list-building/Draw() costs, not frame times or a 120fps result.
  static readonly string[] CullSheets={
   "Assets/_Project/Art/World/Reworld292/Data/41ed54300d9ef754a8e74defc2ab189a_MountainVegetation.asset", // W_Demo_Compact_Reworld
   "Assets/_Project/Art/World/Rock275/Placements.asset",                                                   // W_Demo_Compact_MigrationCheck
   "Assets/_Project/Art/World/MountainIntegration290/Data/MountainVegetation.asset"};                      // W_Demo_Compact_Mountains
  struct CullPose {public string Kind;public Vector3 Position;public Quaternion Rotation;public float Fov,Near,Far,Size;public bool Ortho;}
  static string ArtCulling()
  {
   var scene=SceneManager.GetActiveScene();bool dirtyBefore=scene.isDirty;var template=Camera.main;
   var lines=new List<string>{"SPEC-ART-RENDERER-CULLING equivalence "+DateTime.Now.ToString("s")+" | Unity "+Application.unityVersion+" | "+SystemInfo.processorType+" | "+SystemInfo.graphicsDeviceName,
    "Oracle: CompareWithFullScan = same eye/planes through the culled path and the unchanged full scan; matrices compared bit for bit and in order."};
   long compared=0;int mismatches=0,visibleMismatches=0,renderFailures=0,poseCount=0;
   var root=new GameObject("ArtCulling_check"){hideFlags=HideFlags.HideAndDontSave};root.SetActive(false);
   try
   {
    var camera=root.AddComponent<Camera>();camera.enabled=false;
    var art=root.AddComponent<CompactRebuildArtRenderer>();  // inactive: nothing subscribes while lists are compared
    art.CullPath307=0;  // #307: this oracle and its render accounting are the S3 (output-identical) contract; S8 has artcull2.
    // #307 2a: a match also needs every packet matrix's mesh bounds inside the packet worldBounds (BoundsViolations, counted as mismatches).
    for(int s=0;s<CullSheets.Length;s++)
    {
     var sheet=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(CullSheets[s]);if(sheet==null)throw new FileNotFoundException(CullSheets[s]);
     root.SetActive(false);art.Sheet=sheet;art.Observer=null;art.Invalidate();
     var poses=CullPoses(sheet,template,s==0);poseCount+=poses.Count;
     var culled=new List<double>();var full=new List<double>();var share=new List<double>();
     int sheetMismatches=0,sheetVisible=0;long sheetCompared=0;double prepare=0;string first=null;var info=default(CompactRebuildArtRenderer.ScanComparison);
     for(int p=0;p<poses.Count;p++)
     {
      Apply(camera,poses[p]);
      var a=art.CompareWithFullScan(camera);var b=art.CompareWithFullScan(camera);  // b: warm timings
      if(p==0){prepare=a.PrepareMs;info=a;}
      int bad=a.Mismatches+b.Mismatches+a.BoundsViolations;bool visible=a.CulledVisible!=a.FullVisible||b.CulledVisible!=b.FullVisible;
      if((bad>0||visible)&&first==null)first=Describe(poses[p])+" mismatches="+bad+" visible culled/full="+a.CulledVisible+"/"+a.FullVisible;
      sheetMismatches+=bad;if(visible)sheetVisible++;sheetCompared+=a.Matrices+b.Matrices;
      culled.Add(b.CulledMs);full.Add(b.FullMs);share.Add(100.0*b.Candidates/Math.Max(1,b.Instances));
     }
     mismatches+=sheetMismatches;visibleMismatches+=sheetVisible;compared+=sheetCompared;
     lines.Add("");lines.Add("sheet "+CullSheets[s]);
     lines.Add(" instances="+info.Instances+" cells="+info.Cells+" batches="+info.Batches+" prepare(after Invalidate)="+prepare.ToString("F1")+"ms poses="+poses.Count+" ("+string.Join(",",poses.GroupBy(x=>x.Kind).Select(g=>g.Key+" "+g.Count()))+")");
     lines.Add(" matrices compared="+sheetCompared+" mismatches="+sheetMismatches+" poses with VisibleInstances mismatch="+sheetVisible+(first!=null?" FIRST: "+first:""));
     lines.Add(" candidate instances (in accepted cells) % of sheet: "+Stats(share,"F1"));
     lines.Add(" list building ms  culled: "+Stats(culled,"F3")+" | full scan: "+Stats(full,"F3"));
     // Real Draw() through Camera.Render: submissions must match what the reference lists imply.
     var target=new RenderTexture(1920,1080,24);var draw=new List<double>();int renders=0,sheetRenderFailures=0;string renderFirst=null;
     try
     {
      camera.targetTexture=target;art.Observer=camera;root.SetActive(true);
      var chosen=poses.Where(x=>x.Kind==(s==0?"walk":"ground")).ToList();
      for(int k=0;k<chosen.Count&&renders<16;k+=Math.Max(1,chosen.Count/16))
      {
       Apply(camera,chosen[k]);camera.Render();
       var times=new List<double>();for(int t=0;t<5;t++){camera.Render();times.Add(art.LastCpuMs);}
       int drawCalls=art.DrawCalls,visible=art.VisibleInstances;long triangles=art.SubmittedTriangles;var r=art.CompareWithFullScan(camera);
       if(drawCalls!=r.ExpectedDrawCalls||triangles!=r.ExpectedTriangles||visible!=r.FullVisible||r.Mismatches>0)
       {sheetRenderFailures++;if(renderFirst==null)renderFirst=Describe(chosen[k])+" drawCalls "+drawCalls+"/"+r.ExpectedDrawCalls+" triangles "+triangles+"/"+r.ExpectedTriangles+" visible "+visible+"/"+r.FullVisible;}
       times.Sort();draw.Add(times[2]);renders++;
      }
     }
     finally{root.SetActive(false);art.Observer=null;camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);}
     renderFailures+=sheetRenderFailures;
     lines.Add(" Camera.Render accounting poses="+renders+" failures="+sheetRenderFailures+(renderFirst!=null?" FIRST: "+renderFirst:"")+" | Draw() LastCpuMs (median of 5 per pose): "+Stats(draw,"F3"));
    }
   }
   finally{Object.DestroyImmediate(root);}
   bool dirtyAfter=SceneManager.GetActiveScene().isDirty;
   bool pass=mismatches==0&&visibleMismatches==0&&renderFailures==0&&dirtyAfter==dirtyBefore;
   lines.Insert(2,(pass?"PASS":"FAIL")+" sheets="+CullSheets.Length+" poses="+poseCount+" matrices compared="+compared+" mismatches="+mismatches+" visible mismatches="+visibleMismatches+" render failures="+renderFailures+" scene dirty "+dirtyBefore+"->"+dirtyAfter);
   string folder=O293+"/Perf/Culling";Directory.CreateDirectory(folder);File.WriteAllLines(folder+"/equivalence.txt",lines);
   return lines[2]+" (report: Highlands293/Perf/Culling/equivalence.txt)";
  }
  static void Apply(Camera camera,CullPose pose)
  {
   camera.transform.SetPositionAndRotation(pose.Position,pose.Rotation);camera.orthographic=pose.Ortho;if(pose.Ortho)camera.orthographicSize=pose.Size;
   camera.fieldOfView=pose.Fov;camera.nearClipPlane=pose.Near;camera.farClipPlane=pose.Far;camera.aspect=16f/9;
  }
  static string Describe(CullPose p)=>p.Kind+" at "+p.Position.ToString("F1")+" euler "+p.Rotation.eulerAngles.ToString("F1")+(p.Ortho?" ortho "+p.Size.ToString("F0"):" fov "+p.Fov.ToString("F0"))+" near "+p.Near.ToString("F2")+" far "+p.Far.ToString("F0");
  static string Stats(List<double> values,string format)
  {
   if(values.Count==0)return "n/a";var v=values.OrderBy(x=>x).ToArray();
   return "median="+v[v.Length/2].ToString(format)+" p95="+v[(int)((v.Length-1)*.95)].ToString(format)+" max="+v[v.Length-1].ToString(format)+" n="+v.Length;
  }
  // Deterministic poses: the perf293 walk (candidate only), ground/elevated/anywhere/orthographic samples and edge cases.
  static List<CullPose> CullPoses(WorldMacroDressingSheetSO sheet,Camera template,bool walk)
  {
   var random=new System.Random(20260927);var placed=sheet.FixedPlacements;var poses=new List<CullPose>();if(placed.Length==0)return poses;
   float fov=template!=null?template.fieldOfView:60,near=template!=null?template.nearClipPlane:.3f,far=Mathf.Max(template!=null?template.farClipPlane:1000,3000);
   float F(float a,float b)=>a+(float)random.NextDouble()*(b-a);
   Vector3 Any()=>placed[random.Next(placed.Length)].Position;
   CullPose Pose(string kind,Vector3 at,Quaternion look,float f,float n,float r,bool ortho=false,float size=0)=>new CullPose{Kind=kind,Position=at,Rotation=look,Fov=f,Near=n,Far=r,Ortho=ortho,Size=size};
   var lo=new Vector3(placed.Min(p=>p.Position.x),placed.Min(p=>p.Position.y),placed.Min(p=>p.Position.z));
   var hi=new Vector3(placed.Max(p=>p.Position.x),placed.Max(p=>p.Position.y),placed.Max(p=>p.Position.z));var mid=(lo+hi)*.5f;
   if(walk)
   {
    // MountainTrailProbe285 view: eye 1.65 m over the path, looking 28 points ahead, both directions
    var points=SectionProfile293(SectionIds293()[1]).points;
    for(int i=1;i<points.Length-2;i+=4)foreach(int dir in new[]{1,-1})
    {
     var eye=points[i]+Vector3.up*1.65f;var look=points[Mathf.Clamp(i+dir*28,0,points.Length-1)]+Vector3.up*1.65f-eye;
     if(look.sqrMagnitude>.001f)poses.Add(Pose("walk",eye,Quaternion.LookRotation(look),fov,near,far));
    }
   }
   float[] fars={60,150,400,1000,1500,3000,6000};
   for(int i=0;i<160;i++)poses.Add(Pose("ground",Any()+new Vector3(F(-40,40),F(1.6f,6),F(-40,40)),Quaternion.Euler(F(-25,20),F(0,360),0),F(35,100),F(.05f,1),fars[random.Next(fars.Length)]));
   for(int i=0;i<100;i++)poses.Add(Pose("elevated",Any()+new Vector3(F(-300,300),F(30,600),F(-300,300)),Quaternion.Euler(F(5,85),F(0,360),0),F(30,90),F(.1f,2),fars[random.Next(fars.Length)]));
   for(int i=0;i<80;i++)poses.Add(Pose("anywhere",new Vector3(F(lo.x-1500,hi.x+1500),F(lo.y-100,hi.y+1500),F(lo.z-1500,hi.z+1500)),Quaternion.Euler(F(-90,90),F(0,360),F(-180,180)),F(20,110),F(.05f,5),fars[random.Next(fars.Length)]));
   for(int i=0;i<30;i++)poses.Add(Pose("ortho",Any()+new Vector3(0,F(50,1500),0),Quaternion.Euler(F(30,90),F(0,360),0),60,F(.1f,5),F(2000,6000),true,F(20,1500)));
   poses.Add(Pose("edge-down",mid+Vector3.up*2500,Quaternion.Euler(90,0,0),60,.3f,6000));
   foreach(var corner in new[]{new Vector3(lo.x-500,0,lo.z-500),new Vector3(hi.x+500,0,lo.z-500),new Vector3(lo.x-500,0,hi.z+500),new Vector3(hi.x+500,0,hi.z+500)})
   {var at=corner+Vector3.up*(hi.y+50);poses.Add(Pose("edge-corner",at,Quaternion.LookRotation(mid-at),60,.3f,far));}
   poses.Add(Pose("edge-below",new Vector3(mid.x,lo.y-200,mid.z),Quaternion.Euler(-90,0,0),60,.3f,far));
   var spot=placed[placed.Length/2].Position;
   poses.Add(Pose("edge-tiny-far",spot,Quaternion.Euler(0,45,0),60,.01f,5));
   poses.Add(Pose("edge-far100",spot+Vector3.up*1.6f,Quaternion.Euler(5,120,0),60,.3f,100));
   poses.Add(Pose("edge-ortho-all",mid+Vector3.up*3000,Quaternion.Euler(90,0,0),60,.3f,8000,true,Mathf.Max(hi.x-lo.x,hi.z-lo.z)));
   poses.Add(Pose("edge-outside",new Vector3(hi.x+10000,hi.y+200,mid.z),Quaternion.LookRotation(Vector3.left),60,.3f,20000));
   return poses;
  }
 }
}
