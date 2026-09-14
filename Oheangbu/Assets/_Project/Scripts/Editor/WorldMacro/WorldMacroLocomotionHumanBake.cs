using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class WorldMacroLocomotionAuthoring
 {
  [Serializable] private sealed class AuthoredBoneClip { public string name; public float duration; public float[] poses; }
  [Serializable] private sealed class AuthoredBoneSource { public string fbxSha256; public string[] boneNames; public float[] restPose; public AuthoredBoneClip[] clips; }
  [Serializable] private sealed class HumanBakeSample { public float time,leftSole,rightSole; public Vector3 leftShoulder,leftWrist,hips,bodyPosition; }
  [Serializable] private sealed class HumanBindSample { public string name; public Vector3 authoredRest,alignedRest,approvedBind; public float error; }
  [Serializable] private sealed class HumanBakeReport
  {
   public string name,status,method="Explicit Blender rest and sampled deformation matrices, verified against unchanged C02 skin binds; the original approved avatar HumanPoseHandler produces copied Humanoid curves. No inferred second T pose.";
   public int samples,transferredBones; public float maximumSourceSoleDifference,maximumAlignedBindDifference;
   public List<HumanBakeSample> poses=new List<HumanBakeSample>(); public List<HumanBindSample> binds=new List<HumanBindSample>();
  }
  private static AuthoredBoneSource _boneSourceCache; private static DateTime _boneSourceWrite;
  private static AuthoredBoneSource LoadAuthoredBones()
  {
   string path=Path.GetFullPath(Folder+"/AuthoredBoneMotion.json"); DateTime write=File.GetLastWriteTimeUtc(path);
   if(_boneSourceCache==null || write!=_boneSourceWrite) { _boneSourceCache=JsonUtility.FromJson<AuthoredBoneSource>(File.ReadAllText(path)); _boneSourceWrite=write; }
   using(var stream=File.OpenRead(Actions)) using(var sha=SHA256.Create())
   {
    string actual=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
    if(actual!=_boneSourceCache.fbxSha256) throw new InvalidDataException("Authored bone metadata belongs to a different FBX; rerun export_authored_bones.py.");
   }
   if(_boneSourceCache.boneNames.Length!=54 || _boneSourceCache.restPose.Length!=54*7) throw new InvalidDataException("Expected unchanged 54-bone skeleton.");
   return _boneSourceCache;
  }
  private static Matrix4x4 AuthoredMatrix(float[] v,int i)
  {
   Vector3 p=new Vector3(v[i],v[i+1],v[i+2]); Quaternion q=new Quaternion(v[i+3],v[i+4],v[i+5],v[i+6]);
   if(!float.IsFinite(p.x+p.y+p.z+q.x+q.y+q.z+q.w)) throw new InvalidDataException("Nonfinite authored transform.");
   return Matrix4x4.TRS(p,q.normalized,Vector3.one);
  }
  private static Matrix4x4 SkeletonFrame(Func<string,Vector3> point)
  {
   Vector3 hips=point("Hips"),up=(point("Head")-hips).normalized;
   Vector3 right=Vector3.ProjectOnPlane(point("RightArm")-point("LeftArm"),up).normalized;
   Vector3 toes=(point("LeftToeBase")-point("LeftFoot")+point("RightToeBase")-point("RightFoot"))*.5f;
   Vector3 forward=Vector3.Cross(right,up).normalized;
   if(Mathf.Abs(Vector3.Dot(forward,toes.normalized))<.2f) throw new InvalidDataException("Bind toes cannot disambiguate forward axis.");
   if(Vector3.Dot(forward,toes)<0) forward=-forward;
   Matrix4x4 frame=Matrix4x4.identity;
   frame.SetColumn(0,new Vector4(right.x,right.y,right.z,0)); frame.SetColumn(1,new Vector4(up.x,up.y,up.z,0));
   frame.SetColumn(2,new Vector4(forward.x,forward.y,forward.z,0)); frame.SetColumn(3,new Vector4(hips.x,hips.y,hips.z,1)); return frame;
  }
  private static AnimationClip BakeExactAuthored(AnimationClip generic,string name,bool loop)
  {
   var source=LoadAuthoredBones(); var authored=source.clips.First(c=>c.name==name); var support=AuthoredSupportFor(name);
   var baseline=Need<Oheangbu.App.World.WorldMacroPlayerAppearanceProfile>(SourceProfile);
   if(authored.poses.Length!=121*54*7 || Mathf.Abs(authored.duration-generic.length)>.002f) throw new InvalidDataException("Authored sample count/duration mismatch: "+name);
   // Keep a failed source/bind gate from clearing a currently installed .anim asset.
   var clip=Object.Instantiate(baseline.Idle); clip.name=name; AnimationClip saved=null;
   foreach(var binding in AnimationUtility.GetCurveBindings(clip)) AnimationUtility.SetEditorCurve(clip,binding,null);
   var curves=new AnimationCurve[HumanTrait.MuscleCount+7]; for(int i=0;i<curves.Length;i++) curves[i]=new AnimationCurve();
   var preview=EditorSceneManager.NewPreviewScene(); GameObject actor=null; HumanPoseHandler handler=null;
   var report=new HumanBakeReport { name=name,samples=121 };
   try
   {
    actor=Object.Instantiate(Need<GameObject>(Model)); SceneManager.MoveGameObjectToScene(actor,preview);
    actor.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity); actor.transform.localScale=Vector3.one;
    var animator=actor.GetComponent<Animator>(); animator.runtimeAnimatorController=null; animator.applyRootMotion=false; animator.Rebind(); animator.enabled=false;
    var targetBind=new Dictionary<Transform,Matrix4x4>();
    foreach(var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
     var bones=skin.bones; var bind=skin.sharedMesh.bindposes;
     for(int b=0;b<bones.Length;b++) if(bones[b]!=null && !targetBind.ContainsKey(bones[b])) targetBind.Add(bones[b],skin.transform.localToWorldMatrix*bind[b].inverse);
    }
    var targetByName=targetBind.Keys.ToDictionary(t=>t.name);
    var sourceIndex=source.boneNames.Select((n,i)=>new {n,i}).ToDictionary(p=>p.n,p=>p.i);
    var sourceRest=source.boneNames.ToDictionary(n=>n,n=>AuthoredMatrix(source.restPose,sourceIndex[n]*7));
    Matrix4x4 sourceFrame=SkeletonFrame(n=>sourceRest[n].GetColumn(3));
    Matrix4x4 targetFrame=SkeletonFrame(n=>targetBind[targetByName[n]].GetColumn(3));
    Matrix4x4 conversion=targetFrame*sourceFrame.inverse,inverse=conversion.inverse;
    var targetBones=targetBind.Keys.OrderBy(b=>AnimationUtility.CalculateTransformPath(b,actor.transform).Count(c=>c=='/')).ToArray(); report.transferredBones=targetBones.Length;
    foreach(var bone in targetBones)
    {
     Vector3 original=sourceRest[bone.name].GetColumn(3),aligned=conversion.MultiplyPoint3x4(original),approved=targetBind[bone].GetColumn(3);
     float error=Vector3.Distance(aligned,approved); report.maximumAlignedBindDifference=Mathf.Max(report.maximumAlignedBindDifference,error);
     report.binds.Add(new HumanBindSample {name=bone.name,authoredRest=original,alignedRest=aligned,approvedBind=approved,error=error});
    }
    if(report.maximumAlignedBindDifference>.001f)
    {
     report.status="FAILED_BIND_FRAME"; WriteHumanBake(report); throw new InvalidDataException("Explicit Blender/C02 bind geography differ: "+report.maximumAlignedBindDifference);
    }
    var left=CalibrationSole(actor,animator.GetBoneTransform(HumanBodyBones.LeftFoot)); var right=CalibrationSole(actor,animator.GetBoneTransform(HumanBodyBones.RightFoot));
    handler=new HumanPoseHandler(animator.avatar,actor.transform); var pose=new HumanPose {muscles=new float[HumanTrait.MuscleCount]}; Quaternion previous=Quaternion.identity;
    for(int i=0;i<121;i++)
    {
     float time=generic.length*i/120f;
     foreach(var bone in targetBones)
     {
      Matrix4x4 sampled=AuthoredMatrix(authored.poses,(i*54+sourceIndex[bone.name])*7);
      Matrix4x4 deformation=sampled*sourceRest[bone.name].inverse;
      Matrix4x4 target=conversion*deformation*inverse*targetBind[bone]; bone.SetPositionAndRotation(target.GetColumn(3),target.rotation);
     }
     SampleSole(left,out float leftY); SampleSole(right,out float rightY);
     float expected=Mathf.Min(support.samples[i].left,support.samples[i].right);
     report.maximumSourceSoleDifference=Mathf.Max(report.maximumSourceSoleDifference,Mathf.Abs(Mathf.Min(leftY,rightY)-expected));
     handler.GetHumanPose(ref pose); Quaternion q=pose.bodyRotation;
     if(i>0 && Quaternion.Dot(previous,q)<0) q=new Quaternion(-q.x,-q.y,-q.z,-q.w); previous=q;
     for(int m=0;m<HumanTrait.MuscleCount;m++) curves[m].AddKey(time,pose.muscles[m]); int offset=HumanTrait.MuscleCount;
     curves[offset].AddKey(time,pose.bodyPosition.x); curves[offset+1].AddKey(time,pose.bodyPosition.y); curves[offset+2].AddKey(time,pose.bodyPosition.z);
     curves[offset+3].AddKey(time,q.x); curves[offset+4].AddKey(time,q.y); curves[offset+5].AddKey(time,q.z); curves[offset+6].AddKey(time,q.w);
     if(i==0 || i==60 || i==120) report.poses.Add(new HumanBakeSample {time=time,leftSole=leftY,rightSole=rightY,leftShoulder=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position,leftWrist=animator.GetBoneTransform(HumanBodyBones.LeftHand).position,hips=animator.GetBoneTransform(HumanBodyBones.Hips).position,bodyPosition=pose.bodyPosition});
    }
    report.status=report.maximumSourceSoleDifference<.015f ? "EXACT_SOURCE_POSE_PASS":"FAILED_SOURCE_POSE_MATCH"; WriteHumanBake(report);
    if(report.status!="EXACT_SOURCE_POSE_PASS") throw new InvalidDataException("Explicit authored pose differs from C02 sole trajectory: "+name+" "+report.maximumSourceSoleDifference);
    for(int m=0;m<HumanTrait.MuscleCount;m++) AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Animator),HumanTrait.MuscleName[m]),curves[m]);
    string[] names={"RootT.x","RootT.y","RootT.z","RootQ.x","RootQ.y","RootQ.z","RootQ.w"};
    for(int i=0;i<names.Length;i++) AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Animator),names[i]),curves[HumanTrait.MuscleCount+i]);
    var settings=AnimationUtility.GetAnimationClipSettings(clip); settings.startTime=0; settings.stopTime=generic.length; settings.loopTime=loop; settings.loopBlend=loop;
    settings.loopBlendPositionY=true; settings.loopBlendPositionXZ=true; settings.loopBlendOrientation=true; AnimationUtility.SetAnimationClipSettings(clip,settings); clip.frameRate=30;
    saved=CopyClip(clip,name,loop);
   }
   finally { handler?.Dispose(); if(actor!=null) Object.DestroyImmediate(actor); Object.DestroyImmediate(clip); EditorSceneManager.ClosePreviewScene(preview); }
   return saved;
  }
  private static void WriteHumanBake(HumanBakeReport report)
  {
   Directory.CreateDirectory(ReportFolder); File.WriteAllText(Path.Combine(ReportFolder,"human_bake_"+report.name+".json"),JsonUtility.ToJson(report,true));
  }
 }
}
