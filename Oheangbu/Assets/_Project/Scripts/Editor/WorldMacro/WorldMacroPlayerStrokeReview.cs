using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.Drawing;
using Oheangbu.Presentation;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroPlayerReRigAuthoring
    {
        public static string CheckCompactStrokeRegression()=>RuntimeGestureQa(null,"../Art/World/Compact/Rebuild/Gesture236/Regression",true);
        [Serializable] sealed class StrokeFrame
        {
            public string phase;public int frame;public Vector2 pointer;
            public Vector3 wrist,elbow;public Quaternion upper,hand;
            public WorldMacroPlayerGestureRig.GestureDiagnostics pose;
        }
        [Serializable] sealed class StrokeReview
        {
            public string mode,scope="Synthetic read-only pointer sequence at fixed 60 Hz; not native input or performance measurement.";
            public StrokeFrame[] frames;public string[] images;public int rawBefore,rawAfter;
            public float maxTipError,maxGripError,maxBoneError,maxWristBend;
        }
        public static string ReviewArticulatedStrokes(bool capture)
        {
            var rig=Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            Need(UnityEditor.EditorApplication.isPlaying&&rig!=null&&rig.IsPresentationReady,"Live bound gesture rig required");
            var input=QaGet<DrawingInputController>(rig,"_drawing");var feed=QaGet<BrushStrokeFeedAdapter>(rig,"_brushFeed");
            var walker=QaGet<WorldMacroCombatWalker>(rig,"_walker");var source=walker.ViewCamera;
            Need(!input.InDrawMode&&!input.IsStroking&&Time.timeScale>0,"Neutral state required");
            var fields=new List<GestureQaField>();QaSaveDeclared(fields,rig);
            foreach(var chain in rig.GetComponentsInChildren<BrushBristleRig>(true))QaSaveDeclared(fields,chain);
            foreach(var name in new[]{"<InDrawMode>k__BackingField","<HasPointer>k__BackingField","<PointerScreenPosition>k__BackingField","_currentStroke"})QaSave(fields,input,name);
            foreach(var name in new[]{"_projectionCamera","_hasStrokeEndpoint","_strokeEndpointScreen"})QaSave(fields,feed,name);
            var poses=rig.GetComponentsInChildren<Transform>(true).Select(t=>new GestureQaTransform{Target=t,Position=t.localPosition,Rotation=t.localRotation,Scale=t.localScale}).ToArray();
            var renders=rig.GetComponentsInChildren<Renderer>(true).Select(r=>new GestureQaRenderer{Target=r,Enabled=r.enabled,Shadows=r.shadowCastingMode,Shapes=r is SkinnedMeshRenderer sk&&sk.sharedMesh!=null?Enumerable.Range(0,sk.sharedMesh.blendShapeCount).Select(sk.GetBlendShapeWeight).ToArray():null}).ToArray();
            var cameraRig=QaGet<Oheangbu.Combat.CameraRigController>(rig,"_cameraRig");
            if(cameraRig!=null)QaSave(fields,cameraRig,"_drawingPresentationActive");
            bool enabled=rig.Profile.ArticulatedStrokes;var cameraObject=new GameObject("StrokeReviewCamera"){hideFlags=HideFlags.HideAndDontSave};
            var camera=cameraObject.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;
            camera.transform.SetPositionAndRotation(walker.Body.transform.position+Vector3.up*walker.EyeHeight,source.transform.rotation);
            camera.aspect=1920f/1080;camera.pixelRect=new Rect(0,0,1920,1080);
            var camData=source.GetComponent<UniversalAdditionalCameraData>();if(camData!=null)UnityEditor.EditorUtility.CopySerialized(camData,camera.GetUniversalAdditionalCameraData());
            var evaluate=typeof(WorldMacroPlayerGestureRig).GetMethod("EvaluateGesturePose",QaPrivate);
            string output="../Art/World/Compact/Rebuild/Gesture236";Directory.CreateDirectory(output);
            var summary=new List<string>();
            try
            {
                QaSet(feed,"_projectionCamera",camera);
                foreach(bool articulated in new[]{false,true})
                {
                    rig.Profile.ArticulatedStrokes=articulated;QaInvoke(rig,"ResetTransitions");QaSet(rig,"_nearHasSolution",false);
                    var frames=new List<StrokeFrame>();var images=new List<string>();
                    int raw=input.TotalPointCount;
                    for(int frame=0;frame<222;frame++)
                    {
                        Vector2 p;bool contact;string phase;
                        if(frame<30){p=new Vector2(.42f,.58f);contact=false;phase="prepare";}
                        else if(frame<90)
                        {
                            float t=(frame-30)/59f;p=t<.5f?Vector2.Lerp(new Vector2(.42f,.58f),new Vector2(.46f,.58f),t*2):Vector2.Lerp(new Vector2(.46f,.58f),new Vector2(.46f,.54f),(t-.5f)*2);
                            contact=true;phase="small-consonant";
                        }
                        else if(frame<132){p=Vector2.Lerp(new Vector2(.46f,.54f),new Vector2(.66f,.70f),Mathf.SmoothStep(0,1,(frame-90)/41f));contact=false;phase="lift-to-vowel";}
                        else if(frame<192){p=Vector2.Lerp(new Vector2(.66f,.70f),new Vector2(.66f,.28f),(frame-132)/59f);contact=true;phase="long-downstroke";}
                        else{p=new Vector2(.66f,.28f);contact=false;phase="recovery";}
                        QaInvoke(rig,"Update");QaSetInput(input,feed,frame<192,p,camera);
                        if(!contact)QaSet(input,"_currentStroke",null);
                        if(frame==30||frame==132)QaInvoke(rig,"OnStrokeStarted");
                        evaluate.Invoke(rig,new object[]{1f/60});
                        var body=QaGet<object>(rig,"_near");Transform Bone(string name)=>(Transform)body.GetType().GetField(name,BindingFlags.Public|BindingFlags.Instance).GetValue(body);
                        var sample=new StrokeFrame{phase=phase,frame=frame,pointer=p,pose=rig.Diagnostics,wrist=camera.transform.InverseTransformPoint(Bone("Hand").position),elbow=camera.transform.InverseTransformPoint(Bone("Forearm").position),upper=Quaternion.Inverse(camera.transform.rotation)*Bone("Upper").rotation,hand=Quaternion.Inverse(camera.transform.rotation)*Bone("Hand").rotation};frames.Add(sample);
                        if(capture&&frame>=30&&frame<192&&frame%6==0)
                            images.Add(QaCaptureEvaluatedSkin(rig,camera,(articulated?"after":"before")+"_"+frame.ToString("D3"),output));
                    }
                    var drawn=frames.Where(f=>f.frame<192).ToArray();
                    var report=new StrokeReview{mode=articulated?"after":"before",frames=frames.ToArray(),images=images.ToArray(),rawBefore=raw,rawAfter=input.TotalPointCount,
                        maxTipError=drawn.Max(f=>f.pose.NearTipErrorPixels),maxGripError=frames.Max(f=>f.pose.GripErrorMeters),maxBoneError=frames.Max(f=>Mathf.Max(f.pose.UpperArmLengthErrorMeters,f.pose.ForearmLengthErrorMeters)),maxWristBend=drawn.Max(f=>f.pose.NearWristBendDegrees)};
                    File.WriteAllText(output+"/"+report.mode+".json",JsonUtility.ToJson(report,true));
                    summary.Add(report.mode+" tip="+report.maxTipError+"px grip="+report.maxGripError+"m bone="+report.maxBoneError+"m wristBend="+report.maxWristBend+" rawUnchanged="+(report.rawBefore==report.rawAfter));
                    QaInvoke(rig,"RestoreAnimatedPose");foreach(var pose in poses)pose.Restore();
                }
            }
            finally
            {
                rig.Profile.ArticulatedStrokes=enabled;QaInvoke(rig,"RestoreAnimatedPose");
                foreach(var field in fields)field.Restore();foreach(var p in poses)p.Restore();foreach(var renderer in renders)renderer.Restore();Object.DestroyImmediate(cameraObject);
            }
            return string.Join("\n",summary);
        }
    }
}
