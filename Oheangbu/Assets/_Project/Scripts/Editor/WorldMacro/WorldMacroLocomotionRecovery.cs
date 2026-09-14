using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Oheangbu.App.World;
using Oheangbu.Combat;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class WorldMacroLocomotionAuthoring
 {
  public const string RecoveryFolder="Assets/_Project/Art/Characters/PlaytestRecoveryMotion";
  static string ApplyRecovery(){_assetFolderOverride=RecoveryFolder;try{return Apply();}finally{_assetFolderOverride=null;_boneSourceCache=null;}}
  static string PrepareRecovery()
  {
   EditOnly();_assetFolderOverride=RecoveryFolder;_boneSourceCache=null;
   try
   {
    Directory.CreateDirectory(Folder+"/Animations");AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    var source=Need<WorldMacroPlayerAppearanceProfile>(OriginalFolder+"/PlayerAppearance_Locomotion.asset");
    var importer=(ModelImporter)AssetImporter.GetAtPath(Actions);
    if(importer==null)throw new FileNotFoundException(Actions);
    importer.animationType=ModelImporterAnimationType.Generic;importer.useFileScale=true;importer.globalScale=((ModelImporter)AssetImporter.GetAtPath(Model)).globalScale;
    importer.importAnimation=true;importer.animationCompression=ModelImporterAnimationCompression.Off;importer.optimizeGameObjects=false;importer.SaveAndReimport();
    var takes=importer.defaultClipAnimations;
    foreach(var t in takes){t.lockRootRotation=true;t.lockRootHeightY=true;t.lockRootPositionXZ=true;t.keepOriginalOrientation=true;t.keepOriginalPositionXZ=true;t.keepOriginalPositionY=true;}
    importer.clipAnimations=takes;importer.SaveAndReimport();
    var names=new[]{"WalkForward","RunForward","StartForward","CrouchForward","CrouchBack","CrouchLeft","CrouchRight","CrouchIdle","TurnLeft","TurnRight"};
    var clips=new Dictionary<string,AnimationClip>();var report=new Report{command="recovery-prepare"};var model=Need<GameObject>(Model);
    foreach(string n in names)
    {
     var c=BakeExactAuthored(FindClip(Actions,"PT_"+n),n,n.StartsWith("Crouch")||n=="WalkForward"||n=="RunForward");
     CorrectAuthoredSoleHeight(model,source.UniformScale,c);clips[n]=c;
    }
    // Clone the approved controller, including its standing side/backward, dodge, jump and seated states.
    if(AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)==null)
     if(!AssetDatabase.CopyAsset(OriginalFolder+"/AC_PlaytestLocomotion.controller",ControllerPath))throw new IOException("Controller copy failed");
    var controller=Need<AnimatorController>(ControllerPath);var sm=controller.layers[0].stateMachine;
    var locomotion=sm.states.First(s=>s.state.name=="Locomotion").state;
    float walkRate=Calibrate(model,source.UniformScale,clips["WalkForward"],Vector2.up,2.2f,report);
    float runRate=Calibrate(model,source.UniformScale,clips["RunForward"],Vector2.up,5.5f,report);
    var main=(BlendTree)locomotion.motion;var children=main.children;
    for(int i=0;i<children.Length;i++)
    {
     if(children[i].position==Vector2.up*2.2f){children[i].motion=clips["WalkForward"];children[i].timeScale=walkRate;}
     if(children[i].position==Vector2.up*5.5f){children[i].motion=clips["RunForward"];children[i].timeScale=runRate;}
    }
    main.children=children;EditorUtility.SetDirty(main);
    foreach(string n in new[]{"CrouchAmount","TurnSpeed"})if(!controller.parameters.Any(p=>p.name==n))controller.AddParameter(n,AnimatorControllerParameterType.Float);
    foreach(string n in new[]{"StartWalk","TurnLeft","TurnRight"})if(!controller.parameters.Any(p=>p.name==n))controller.AddParameter(n,AnimatorControllerParameterType.Trigger);
    if(!controller.parameters.Any(p=>p.name=="Crouching"))controller.AddParameter("Crouching",AnimatorControllerParameterType.Bool);
    AnimatorState Existing(string n,Motion motion){var s=sm.states.FirstOrDefault(x=>x.state.name==n).state;if(s==null)s=State(sm,n,motion);else{s.motion=motion;foreach(var t in s.transitions)s.RemoveTransition(t);}return s;}
    foreach(var t in locomotion.transitions.Where(t=>t.destinationState!=null&&new[]{"Crouch","StartForward","TurnLeft","TurnRight"}.Contains(t.destinationState.name)).ToArray())locomotion.RemoveTransition(t);
    var crouchTree=Tree(controller,"Crouched actual velocity","MoveX","MoveZ");
    var oldCrouch=sm.states.FirstOrDefault(s=>s.state.name=="Crouch").state;
    if(oldCrouch!=null&&oldCrouch.motion is BlendTree obsolete)UnityEngine.Object.DestroyImmediate(obsolete,true);
    var cs=new List<ChildMotion>{new ChildMotion{motion=clips["CrouchIdle"],position=Vector2.zero,timeScale=1}};
    for(int i=0;i<4;i++)cs.Add(new ChildMotion{motion=clips["Crouch"+Directions[i]],position=Axes[i]*1.2f,timeScale=Calibrate(model,source.UniformScale,clips["Crouch"+Directions[i]],Axes[i],1.2f,report)});
    crouchTree.children=cs.ToArray();var crouch=Existing("Crouch",crouchTree);
    Transition(locomotion,crouch,"Crouching",AnimatorConditionMode.If,.18f);Transition(crouch,locomotion,"Crouching",AnimatorConditionMode.IfNot,.18f);
    Transition(crouch,sm.states.First(s=>s.state.name=="JumpFall").state,"Grounded",AnimatorConditionMode.IfNot,.08f);
    var start=Existing("StartForward",clips["StartForward"]);Transition(locomotion,start,"StartWalk",AnimatorConditionMode.If,.05f);
    Exit(start,locomotion,.8f,.08f);Transition(start,locomotion,"MoveZ",AnimatorConditionMode.Less,.05f,.1f);
    Transition(start,locomotion,"MoveX",AnimatorConditionMode.Greater,.05f,.3f);Transition(start,locomotion,"MoveX",AnimatorConditionMode.Less,.05f,-.3f);
    Transition(start,crouch,"Crouching",AnimatorConditionMode.If,.1f);
    foreach(string n in new[]{"TurnLeft","TurnRight"})
    {
     var turn=Existing(n,clips[n]);Transition(locomotion,turn,n,AnimatorConditionMode.If,.09f);Exit(turn,locomotion,.85f,.1f);
     Transition(turn,locomotion,"PlanarSpeed",AnimatorConditionMode.Greater,.05f,.1f);Transition(turn,crouch,"Crouching",AnimatorConditionMode.If,.1f);
    }
    foreach(var transient in sm.states.Select(s=>s.state).Where(s=>s.name=="StartForward"||s.name=="TurnLeft"||s.name=="TurnRight"))
    {
     Transition(transient,sm.states.First(s=>s.state.name=="JumpRise").state,"Jump",AnimatorConditionMode.If,.035f);
     Transition(transient,sm.states.First(s=>s.state.name=="JumpFall").state,"Grounded",AnimatorConditionMode.IfNot,.06f);
     Transition(transient,sm.states.First(s=>s.state.name=="Dodge").state,"Dodge",AnimatorConditionMode.If,.025f);
     Transition(transient,sm.states.First(s=>s.state.name=="SitDown").state,"SitRequested",AnimatorConditionMode.If,.06f);
    }
    var profile=Asset<WorldMacroPlayerAppearanceProfile>(ProfilePath);EditorUtility.CopySerialized(source,profile);profile.Controller=controller;profile.RecoveryMotions=true;
    profile.DirectionalWalk=(AnimationClip[])source.DirectionalWalk.Clone();profile.DirectionalRun=(AnimationClip[])source.DirectionalRun.Clone();
    profile.Walk=profile.DirectionalWalk[0]=clips["WalkForward"];profile.Run=profile.DirectionalRun[0]=clips["RunForward"];
    profile.DirectionalCrouch=Directions.Select(d=>clips["Crouch"+d]).ToArray();profile.CrouchIdle=clips["CrouchIdle"];profile.StartForward=clips["StartForward"];profile.TurnLeft=clips["TurnLeft"];profile.TurnRight=clips["TurnRight"];
    var motor=Asset<PlayerLocomotionProfileSO>(MotorProfilePath);EditorUtility.CopySerialized(Need<PlayerLocomotionProfileSO>(OriginalFolder+"/PlayerLocomotion.asset"),motor);
    motor.CrouchSpeed=1.2f;motor.CrouchingHeight=1.25f;motor.CrouchingEyeHeight=1.1f;
    EditorUtility.SetDirty(controller);EditorUtility.SetDirty(profile);EditorUtility.SetDirty(motor);AssetDatabase.SaveAssets();
    report.status="DERIVED_REQUIRES_PLAY_AND_VISUAL_QA";return Save(report);
   }
   finally{_assetFolderOverride=null;_boneSourceCache=null;}
  }
 }
}
