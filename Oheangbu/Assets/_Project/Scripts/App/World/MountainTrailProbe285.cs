using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Oheangbu.Data.World;
namespace Oheangbu.App.World {
 public sealed class MountainTrailProbe285:MonoBehaviour {
  public MountainTrailProfile Profile;public Camera View;public string Output,Result;public int Target=1,Moves;int direction=1;float stalled,seconds;CharacterController body;RenderTexture target;
  readonly List<double> cpu=new(),gpu=new(),interval=new();readonly FrameTiming[] timing=new FrameTiming[1];readonly HashSet<string> contacts=new();
  void Start(){body=GetComponent<CharacterController>();target=new RenderTexture(1920,1080,24);View.targetTexture=target;View.aspect=16f/9;}
  void OnControllerColliderHit(ControllerColliderHit hit){contacts.Add(hit.collider.name);}
  void FixedUpdate(){
   if(Result!=null||Profile==null)return;seconds+=Time.fixedDeltaTime;if(seconds>180){Finish("FAIL timeout");return;}
   Vector3 destination=Profile.points[Target]+Profile.Right(Profile.distances[Target])*(direction*.38f);var before=transform.position;var d=destination-before;d.y=0;
   if(d.magnitude<.12f){Target+=direction;if(Target>=Profile.points.Length-2){direction=-1;Target=Profile.points.Length-3;}if(Target<1){Finish("PASS");return;}return;}
   body.Move(Vector3.ClampMagnitude(d,4.5f*Time.fixedDeltaTime)+Vector3.down*3*Time.fixedDeltaTime);Moves++;
   var step=transform.position-before;step.y=0;stalled=step.magnitude<.001f?stalled+Time.fixedDeltaTime:0;
   if(stalled>3)Finish("FAIL stuck target="+Target+" position="+transform.position);
  }
  void LateUpdate(){if(Result!=null||Profile==null)return;View.transform.position=transform.position+Vector3.up*1.65f;var look=Profile.points[Mathf.Clamp(Target+direction*28,0,Profile.points.Length-1)]+Vector3.up*1.65f-View.transform.position;if(look.sqrMagnitude>.001f)View.transform.rotation=Quaternion.Slerp(View.transform.rotation,Quaternion.LookRotation(look),Time.deltaTime*6);
   FrameTimingManager.CaptureFrameTimings();if(seconds<3)return;interval.Add(Time.unscaledDeltaTime*1000);if(FrameTimingManager.GetLatestTimings(1,timing)>0){if(timing[0].cpuFrameTime>0)cpu.Add(timing[0].cpuFrameTime);if(timing[0].gpuFrameTime>0)gpu.Add(timing[0].gpuFrameTime);}
  }
  string Stats(List<double> samples){if(samples.Count==0)return "unavailable (not measured)";var a=samples.OrderBy(x=>x).ToArray();return "samples="+a.Length+" median_ms="+a[a.Length/2].ToString("F2")+" p95_ms="+a[(int)((a.Length-1)*.95)].ToString("F2");}
  void Finish(string status){Result=status+" two-way Play CharacterController; lateral offsets +0.38m/-0.38m; target="+Target+" moves="+Moves+" contacts="+string.Join(",",contacts)+". Automated input, not manual play or art approval.";Directory.CreateDirectory(Output);File.WriteAllText(Output+"/frame-times.txt","Unity Editor Play, 1920x1080 camera render target; includes editor overhead.\nCPU FrameTiming: "+Stats(cpu)+"\nGPU FrameTiming: "+Stats(gpu)+"\nEditor frame interval: "+Stats(interval)+"\n120fps target = 8.33ms. No standalone-player performance approval.\nDevice: "+SystemInfo.graphicsDeviceName);}
  void OnDestroy(){if(View!=null)View.targetTexture=null;if(target!=null){target.Release();Destroy(target);}}
 }
}
