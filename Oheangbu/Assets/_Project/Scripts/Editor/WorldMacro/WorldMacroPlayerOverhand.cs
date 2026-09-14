using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.World;
namespace Oheangbu.EditorTools.WorldMacro {
public static partial class WorldMacroPlayerReRigAuthoring {
 public static string ExecuteOverhand(string command) {
  _recoveryHand=true;
  try {
   var p=AssetDatabase.LoadAssetAtPath<WorldMacroPlayerGestureProfile>(GripAProfile);
   if(command=="configure") {
    Need(!EditorApplication.isPlaying,"Edit required");
    var model=AssetDatabase.LoadAssetAtPath<GameObject>(GripAModel);
    var h=Find(model.transform,"RightHand");var f=Find(model.transform,"RightForeArm");
    var i=Find(model.transform,"RightHandIndex1");var k=Find(model.transform,"RightHandPinky1");
    Need(h!=null&&f!=null&&i!=null&&k!=null,"Hand reference bones missing");
    Vector3 forward=((i.position+k.position)*.5f-h.position).normalized;
    Vector3 dorsal=Vector3.Cross(i.position-h.position,k.position-h.position).normalized;
    if(Vector3.Dot(dorsal,Vector3.up)<0)dorsal=-dorsal;
    p.HandForwardLocal=h.InverseTransformDirection(forward);p.HandDorsalLocal=h.InverseTransformDirection(dorsal);
    p.HandRestInForearm=Quaternion.Inverse(f.rotation)*h.rotation;p.OverhandGrip=true;
    var original=AssetDatabase.LoadAssetAtPath<WorldMacroPlayerGestureProfile>("Assets/_Project/Art/Characters/PlaytestPolish/GripA/PlayerGesture_GripA.asset");
    // Bristles exit the thumb/index side; the terminal ring exits the pinky side.
    p.RightHandGripRotation=original.RightHandGripRotation;
    p.RightHandGripPosition=original.RightHandGripPosition;
    p.HandThumbSideLocal=h.InverseTransformDirection((i.position-k.position).normalized);
    foreach(var pose in p.Fingers) {
     var source=original.Fingers.First(x=>x.BoneName==pose.BoneName);
     pose.CarryOffset=source.CarryOffset;pose.DrawingOffset=source.DrawingOffset;pose.HarvestOffset=source.HarvestOffset;
    }
    p.DrawingBrushRoll=58;
    EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();return JsonUtility.ToJson(p);
   }
   if(command=="before")return CaptureBeforeOverhand(p);
   if(command=="fit")return FitOverhandFingers(p);
   if(command=="fine")return FitOverhandPosition(p);
   var parts=command.Split(':');
   float roll=float.Parse(parts[1],System.Globalization.CultureInfo.InvariantCulture);
   if(parts[0]=="apply") {Need(!EditorApplication.isPlaying,"Edit required"); p.DrawingBrushRoll=roll;EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();return "APPLIED "+roll;}
   Need(EditorApplication.isPlaying,"Play required");
   float old=p.DrawingBrushRoll;Quaternion oldGrip=p.RightHandGripRotation;
   try {p.DrawingBrushRoll=roll;Need(parts.Length<=3,"Brush reversal is rejected: bristles must face the thumb side.");string dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestRecovery/Overhand",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"_"+roll));Directory.CreateDirectory(dir);return RuntimeGestureQa(parts.Length>2?parts[2]:"center",dir,true);}
   finally {p.DrawingBrushRoll=old;p.RightHandGripRotation=oldGrip;}
  } finally {_recoveryHand=false;}
 }

 private static string FitOverhandFingers(WorldMacroPlayerGestureProfile p) {
  Need(EditorApplication.isPlaying,"Fit requires neutral Play");
  var rig=UnityEngine.Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
  Need(rig!=null && !rig.Diagnostics.NearVisible,"Neutral carry required");
  string dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestRecovery/Overhand/FingerFit"));Directory.CreateDirectory(dir);
  File.WriteAllText(Path.Combine(dir,"profile_before.json"),JsonUtility.ToJson(p,true));
  var history=new List<string>();
  try {
   foreach(string digit in new[]{"Thumb","Index","Middle","Ring","Pinky"}) {
    for(int pass=0;pass<2;pass++) {
     QaInvoke(rig,"RestoreAnimatedPose");QaInvoke(rig,"LateUpdate");
     var baseline=JsonUtility.FromJson<ContactReport>(ValidateCurrentSkinContact(dir));
     var dc=baseline.fingers.First(x=>x.digit==digit);
     float best=ContactScore(dc),degrees=0;int selected=0;
     var poses=p.Fingers.Where(x=>x.BoneName=="RightHand"+digit+"1"||x.BoneName=="RightHand"+digit+"2").ToArray();
     Quaternion[] originals=poses.Select(x=>x.CarryOffset).ToArray();
     for(int j=0;j<poses.Length;j++)for(int a=-4;a<=4;a++) {
      if(a==0)continue;
      originals[j].ToAngleAxis(out float angle,out Vector3 axis);
      Quaternion offset=Quaternion.AngleAxis(a*4f,axis)*originals[j];
      poses[j].CarryOffset=poses[j].DrawingOffset=poses[j].HarvestOffset=offset;
      QaInvoke(rig,"RestoreAnimatedPose");QaInvoke(rig,"LateUpdate");
      var result=JsonUtility.FromJson<ContactReport>(ValidateCurrentSkinContact(dir));
      float score=ContactScore(result.fingers.First(x=>x.digit==digit));
      if(score<best){best=score;degrees=a*4f;selected=j;}
      poses[j].CarryOffset=poses[j].DrawingOffset=poses[j].HarvestOffset=originals[j];
     }
     originals[selected].ToAngleAxis(out float ignored,out Vector3 chosenAxis);
     var final=Quaternion.AngleAxis(degrees,chosenAxis)*originals[selected];
     poses[selected].CarryOffset=poses[selected].DrawingOffset=poses[selected].HarvestOffset=final;
     history.Add(digit+" pass="+pass+" joint="+(selected+1)+" adjustment="+degrees+" score="+best);
     if(best<.1f)break;
    }
   }
   EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();
   QaInvoke(rig,"RestoreAnimatedPose");QaInvoke(rig,"LateUpdate");
   File.WriteAllLines(Path.Combine(dir,"fit_history.txt"),history);
   File.WriteAllText(Path.Combine(dir,"profile_after.json"),JsonUtility.ToJson(p,true));
   return ValidateCurrentSkinContact(dir);
  } finally {QaInvoke(rig,"RestoreAnimatedPose");}
 }
 private static float ContactScore(ContactDigit d) => Mathf.Max(0,d.maximumPenetrationMeters-.0003f)*3000f+Mathf.Max(0,d.closestSkinGapMeters-.001f)*1000f;

 private static float WholeContactScore(ContactReport r) => Mathf.Max(0,Mathf.Max(r.maximumPenetrationMeters,r.manualLbsMaximumPenetrationMeters)-.0002f)*3000f+r.fingers.Sum(d=>Mathf.Max(0,Mathf.Max(d.closestSkinGapMeters,d.manualLbsClosestSkinGapMeters)-.0012f)*1000f);
 private static string FitOverhandPosition(WorldMacroPlayerGestureProfile p) {
  Need(EditorApplication.isPlaying,"Play required");var rig=UnityEngine.Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
  string dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestRecovery/Overhand/PositionFit"));Directory.CreateDirectory(dir);
  File.WriteAllText(Path.Combine(dir,"profile_before.json"),JsonUtility.ToJson(p,true));
  foreach(float step in new[]{.0015f,.0005f}) {
   Vector3 original=p.RightHandGripPosition,bestPoint=original;
   QaInvoke(rig,"RestoreAnimatedPose");QaInvoke(rig,"LateUpdate");
   float best=WholeContactScore(JsonUtility.FromJson<ContactReport>(ValidateCurrentSkinContact(dir)));
   foreach(Vector3 direction in new[]{Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back}) {
    p.RightHandGripPosition=original+direction*step;QaInvoke(rig,"RestoreAnimatedPose");QaInvoke(rig,"LateUpdate");
    float score=WholeContactScore(JsonUtility.FromJson<ContactReport>(ValidateCurrentSkinContact(dir)));
    if(score<best){best=score;bestPoint=p.RightHandGripPosition;}
   }
   p.RightHandGripPosition=bestPoint;
  }
  EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();QaInvoke(rig,"RestoreAnimatedPose");QaInvoke(rig,"LateUpdate");
  File.WriteAllText(Path.Combine(dir,"profile_after.json"),JsonUtility.ToJson(p,true));return ValidateCurrentSkinContact(dir);
 }

 private static string CaptureBeforeOverhand(WorldMacroPlayerGestureProfile p) {
  Need(EditorApplication.isPlaying,"Play required");
  var original=AssetDatabase.LoadAssetAtPath<WorldMacroPlayerGestureProfile>("Assets/_Project/Art/Characters/PlaytestPolish/GripA/PlayerGesture_GripA.asset");
  var oldRotation=p.RightHandGripRotation;var oldPosition=p.RightHandGripPosition;float oldRoll=p.DrawingBrushRoll;bool oldFlag=p.OverhandGrip;
  var poses=p.Fingers.Select(f=>new[]{f.CarryOffset,f.DrawingOffset,f.HarvestOffset}).ToArray();
  try {
   p.OverhandGrip=false;p.RightHandGripRotation=original.RightHandGripRotation;p.RightHandGripPosition=original.RightHandGripPosition;p.DrawingBrushRoll=-162;
   for(int i=0;i<p.Fingers.Length;i++){var f=original.Fingers.First(x=>x.BoneName==p.Fingers[i].BoneName);p.Fingers[i].CarryOffset=f.CarryOffset;p.Fingers[i].DrawingOffset=f.DrawingOffset;p.Fingers[i].HarvestOffset=f.HarvestOffset;}
   string dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestRecovery/Overhand/Before"));Directory.CreateDirectory(dir);
   string json=RuntimeGestureQa("center",dir,true);
   var report=JsonUtility.FromJson<GestureQaReport>(json);report.scope+=" BEFORE fixture: temporary old grip rotation/position/finger offsets and roll -162, OverhandGrip false; disk profile hash is the current profile, not these temporary overrides. Restored afterwards.";
   json=JsonUtility.ToJson(report,true);File.WriteAllText(Path.Combine(dir,"runtime_capture_center.json"),json);return json;
  } finally {
   p.RightHandGripRotation=oldRotation;p.RightHandGripPosition=oldPosition;p.DrawingBrushRoll=oldRoll;p.OverhandGrip=oldFlag;
   for(int i=0;i<p.Fingers.Length;i++){p.Fingers[i].CarryOffset=poses[i][0];p.Fingers[i].DrawingOffset=poses[i][1];p.Fingers[i].HarvestOffset=poses[i][2];}
  }
 }
}}
