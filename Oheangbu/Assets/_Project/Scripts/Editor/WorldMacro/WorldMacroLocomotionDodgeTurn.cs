using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Oheangbu.App.World;
using Oheangbu.Combat;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class WorldMacroLocomotionAuthoring
 {
  public const string DodgeTurnFolder = "Assets/_Project/Art/Characters/PlaytestDodgeTurn";
  public static string ExecuteDodgeTurn(string command)
  {
   _assetFolderOverride=DodgeTurnFolder; _boneSourceCache=null;
   try { if(command=="prepare") return PrepareDodgeTurn(); if(command=="apply") return Apply(); throw new ArgumentException(command); }
   finally { _assetFolderOverride=null; _boneSourceCache=null; }
  }
  private static string PrepareDodgeTurn()
  {
   EditOnly(); Directory.CreateDirectory(Folder+"/Animations"); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
   var source=Need<WorldMacroPlayerAppearanceProfile>(RecoveryFolder+"/PlayerAppearance_Locomotion.asset");
   var importer=(ModelImporter)AssetImporter.GetAtPath(Actions);
   importer.animationType=ModelImporterAnimationType.Generic; importer.useFileScale=true;
   importer.globalScale=((ModelImporter)AssetImporter.GetAtPath(Model)).globalScale;
   importer.importAnimation=true; importer.animationCompression=ModelImporterAnimationCompression.Off; importer.optimizeGameObjects=false; importer.SaveAndReimport();
   var takes=importer.defaultClipAnimations;
   foreach(var take in takes) { take.lockRootRotation=true; take.lockRootHeightY=true; take.lockRootPositionXZ=true; take.keepOriginalOrientation=true; take.keepOriginalPositionXZ=true; take.keepOriginalPositionY=true; }
   importer.clipAnimations=takes; importer.SaveAndReimport();
   var clips=Directions.Select(d=>BakeExactAuthored(FindClip(Actions,"PT_Dodge"+d),"Dodge"+d,false)).ToArray();
   var rollClip=BakeExactAuthored(FindClip(Actions,"PT_CrouchRoll"),"CrouchRoll",false);
   if(AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)==null && !AssetDatabase.CopyAsset(RecoveryFolder+"/AC_PlaytestLocomotion.controller",ControllerPath)) throw new IOException("Controller clone failed");
   var controller=Need<AnimatorController>(ControllerPath); var sm=controller.layers[0].stateMachine;
   foreach(var p in new[]{"Rolling","Turning"}) if(!controller.parameters.Any(x=>x.name==p))controller.AddParameter(p,AnimatorControllerParameterType.Bool);
   if(!controller.parameters.Any(x=>x.name=="TurnProgress"))controller.AddParameter("TurnProgress",AnimatorControllerParameterType.Float);
   var dash=sm.states.First(s=>s.state.name=="Dodge").state; var tree=(BlendTree)dash.motion; var children=tree.children;
   for(int i=0;i<children.Length;i++)children[i].motion=clips[Array.FindIndex(Axes,a=>a==children[i].position)];
   tree.children=children; EditorUtility.SetDirty(tree);
   var crouch=sm.states.First(s=>s.state.name=="Crouch").state;
   var loco=sm.states.First(s=>s.state.name=="Locomotion").state;
   // The old idle contains foot repositioning. Hold its neutral pose while the
   // head-look and explicit turn steps provide idle response; preserve source clip.
   var still=UnityEngine.Object.Instantiate(source.Idle);still.name="IdleStill";
   foreach(var binding in AnimationUtility.GetCurveBindings(still))
   {
    var curve=AnimationUtility.GetEditorCurve(still,binding);float value=curve.Evaluate(0f);
    AnimationUtility.SetEditorCurve(still,binding,AnimationCurve.Constant(0f,1f,value));
   }
   var idleSettings=AnimationUtility.GetAnimationClipSettings(still);idleSettings.startTime=0;idleSettings.stopTime=1;idleSettings.loopTime=true;AnimationUtility.SetAnimationClipSettings(still,idleSettings);
   var idle=CopyClip(still,"IdleStill",true);UnityEngine.Object.DestroyImmediate(still);
   var walkTree=(BlendTree)loco.motion;var walkChildren=walkTree.children;
   for(int i=0;i<walkChildren.Length;i++)if(walkChildren[i].position==Vector2.zero)walkChildren[i].motion=idle;
   walkTree.children=walkChildren;EditorUtility.SetDirty(walkTree);
   var roll=sm.states.FirstOrDefault(s=>s.state.name=="CrouchRoll").state ?? State(sm,"CrouchRoll",rollClip);
   roll.motion=rollClip; roll.timeParameter="DodgeProgress"; roll.timeParameterActive=true;
   foreach(var t in roll.transitions)roll.RemoveTransition(t);
   Transition(roll,crouch,"Dodging",AnimatorConditionMode.IfNot,.1f);
   Transition(roll,sm.states.First(s=>s.state.name=="JumpFall").state,"Grounded",AnimatorConditionMode.IfNot,.06f);
   foreach(var s in sm.states.Select(x=>x.state).Where(s=>s!=roll&&s!=dash))
   {
    foreach(var t in s.transitions.Where(t=>t.destinationState==roll).ToArray())s.RemoveTransition(t);
    foreach(var t in s.transitions.Where(t=>t.destinationState==dash)) if(!t.conditions.Any(c=>c.parameter=="Rolling"))t.AddCondition(AnimatorConditionMode.IfNot,0,"Rolling");
    // A state change on the same input frame must still consume the latched roll.
    if(s.name=="Crouch"||s.name=="Locomotion"||s.name=="Land"||s.name.StartsWith("Turn"))
    { var t=Transition(s,roll,"Dodging",AnimatorConditionMode.If,.025f); t.AddCondition(AnimatorConditionMode.If,0,"Rolling"); }
   }
   foreach(string direction in new[]{"Left","Right"})
   {
    var turn=sm.states.First(s=>s.state.name=="Turn"+direction).state;
    turn.timeParameter="TurnProgress"; turn.timeParameterActive=true;
    foreach(var t in turn.transitions.Where(t=>t.hasExitTime).ToArray())turn.RemoveTransition(t);
    Transition(turn,loco,"Turning",AnimatorConditionMode.IfNot,.09f);
    var low=sm.states.FirstOrDefault(s=>s.state.name=="CrouchTurn"+direction).state ?? State(sm,"CrouchTurn"+direction,source.DirectionalCrouch[direction=="Left"?2:3]);
    foreach(var t in low.transitions)low.RemoveTransition(t);
    low.timeParameter="TurnProgress"; low.timeParameterActive=true;
    foreach(var t in crouch.transitions.Where(t=>t.destinationState==low).ToArray())crouch.RemoveTransition(t);
    Transition(crouch,low,"Turn"+direction,AnimatorConditionMode.If,.06f);
    Transition(low,crouch,"Turning",AnimatorConditionMode.IfNot,.08f);
    Transition(low,crouch,"PlanarSpeed",AnimatorConditionMode.Greater,.05f,.1f);
    Transition(low,loco,"Crouching",AnimatorConditionMode.IfNot,.08f);
    var toRoll=Transition(low,roll,"Dodging",AnimatorConditionMode.If,.025f);toRoll.AddCondition(AnimatorConditionMode.If,0,"Rolling");
   }
   var profile=Asset<WorldMacroPlayerAppearanceProfile>(ProfilePath); EditorUtility.CopySerialized(source,profile);
   profile.Controller=controller;profile.Idle=idle; profile.DirectionalDodge=clips; profile.CrouchRoll=rollClip;profile.IdleLookTurn=true;profile.IdleLookDegrees=90;
   var motor=Asset<PlayerLocomotionProfileSO>(MotorProfilePath);EditorUtility.CopySerialized(Need<PlayerLocomotionProfileSO>(RecoveryFolder+"/PlayerLocomotion.asset"),motor);
   motor.CrouchRollEnabled=true;motor.CrouchRollSeconds=.8f;
   EditorUtility.SetDirty(controller);EditorUtility.SetDirty(profile);EditorUtility.SetDirty(motor);AssetDatabase.SaveAssets();
   return "Five official Mixamo clips baked on C02; separate controller/profile ready. Runtime/visual checks pending.";
  }
 }
}
