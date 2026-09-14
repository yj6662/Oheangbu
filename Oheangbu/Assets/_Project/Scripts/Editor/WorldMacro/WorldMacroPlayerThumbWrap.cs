using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Oheangbu.App.World;

namespace Oheangbu.EditorTools.WorldMacro {
public static partial class WorldMacroPlayerReRigAuthoring {
 public static string ExecuteThumbWrap(string command) {
  _recoveryHand=true;
  var p=AssetDatabase.LoadAssetAtPath<WorldMacroPlayerGestureProfile>(GripAProfile);
  var original=AssetDatabase.LoadAssetAtPath<WorldMacroPlayerGestureProfile>("Assets/_Project/Art/Characters/PlaytestPolish/GripA/PlayerGesture_GripA.asset");
  var poses=p.Fingers.Where(x=>x.BoneName.StartsWith("RightHandThumb")).ToArray();
  var saved=poses.Select(x=>new[]{x.CarryOffset,x.DrawingOffset,x.HarvestOffset}).ToArray();
  bool applied=false;
  try {
   var parts=command.Split(':');
   Need(parts.Length==4 || parts.Length==5,"probe/apply:base:middle:tip[:opposition] degrees required");
   var source=original.Fingers.First(x=>x.BoneName=="RightHandThumb2");
   source.CarryOffset.ToAngleAxis(out float ignored,out Vector3 flexAxis);
   for(int i=0;i<poses.Length;i++) {
    float degrees=float.Parse(parts[i+1],System.Globalization.CultureInfo.InvariantCulture);
    var rest=original.Fingers.First(x=>x.BoneName==poses[i].BoneName);
    Quaternion offset=rest.CarryOffset*Quaternion.AngleAxis(degrees,flexAxis);
    if(i==0 && parts.Length==5) {
     float oppose=float.Parse(parts[4],System.Globalization.CultureInfo.InvariantCulture);
     var basis=WorldMacroPlayerGestureProfile.SafeRotation(poses[i].RestLocalRotation);
     offset=Quaternion.Inverse(basis)*Quaternion.AngleAxis(oppose,p.HandDorsalLocal)*basis*offset;
    }
    poses[i].CarryOffset=poses[i].DrawingOffset=poses[i].HarvestOffset=offset;
   }
   if(parts[0]=="apply") {
    Need(!EditorApplication.isPlaying,"Apply in Edit mode");
    EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();applied=true;return "THUMB_WRAP_APPLIED "+command;
   }
   Need(EditorApplication.isPlaying,"Probe requires neutral Play");
   var rig=UnityEngine.Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
   string dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestRecovery/ThumbWrap",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));
   Directory.CreateDirectory(dir);
   File.WriteAllText(Path.Combine(dir,"pose_override.json"),JsonUtility.ToJson(p,true));
   QaInvoke(rig,"RestoreAnimatedPose");QaInvoke(rig,"LateUpdate");
   string contact=ValidateCurrentSkinContact(dir);
   string capture=RuntimeGestureQa("center",dir,true);
   return capture;
  } finally {
   if(!applied)for(int i=0;i<poses.Length;i++) {poses[i].CarryOffset=saved[i][0];poses[i].DrawingOffset=saved[i][1];poses[i].HarvestOffset=saved[i][2];}
   _recoveryHand=false;
  }
 }
}}
