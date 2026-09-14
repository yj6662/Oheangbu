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
  public const string NaturalFolder="Assets/_Project/Art/Characters/PlaytestNaturalLocomotion";
  public static string ExecuteNatural(string command)
  {
   _assetFolderOverride=NaturalFolder;_boneSourceCache=null;
   try { switch(command) {case "prepare":return PrepareNatural();case "apply":return Apply();case "validate":return Validate();case "footcheck":return RunFootChecks();default:throw new ArgumentException(command);} }
   finally{_assetFolderOverride=null;_boneSourceCache=null;}
  }
  static string PrepareNatural()
  {
   EditOnly();Directory.CreateDirectory(Folder+"/Animations");AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
   var source=Need<WorldMacroPlayerAppearanceProfile>(DodgeTurnFolder+"/PlayerAppearance_Locomotion.asset");
   var importer=(ModelImporter)AssetImporter.GetAtPath(Actions);
   importer.animationType=ModelImporterAnimationType.Generic;importer.useFileScale=true;importer.globalScale=((ModelImporter)AssetImporter.GetAtPath(Model)).globalScale;
   importer.importAnimation=true;importer.animationCompression=ModelImporterAnimationCompression.Off;importer.optimizeGameObjects=false;importer.SaveAndReimport();
   var takes=importer.defaultClipAnimations;
   foreach(var t in takes){t.lockRootRotation=true;t.lockRootHeightY=true;t.lockRootPositionXZ=true;t.keepOriginalOrientation=true;t.keepOriginalPositionY=true;t.keepOriginalPositionXZ=true;}
   importer.clipAnimations=takes;importer.SaveAndReimport();
   var report=new Report{command="natural_prepare"};var model=Need<GameObject>(Model);var clips=new Dictionary<string,AnimationClip>();
   var names=Directions.Select(d=>"Walk"+d).Concat(Directions.Select(d=>"Run"+d)).Concat(new[]{"IdleStill","StartForward","JumpRise","JumpFall","Land"});
   foreach(var n in names)
   {
    bool loop=n.StartsWith("Walk")||n.StartsWith("Run")||n=="IdleStill";
    var clip=BakeExactAuthored(FindClip(Actions,"PT_"+n),n,loop);
    // Flight height is owned by PlayerMotor. Never plant tucked airborne feet.
    if(!n.StartsWith("Jump"))CorrectAuthoredSoleHeight(model,source.UniformScale,clip);
    clips[n]=clip;
   }
   if(AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)==null && !AssetDatabase.CopyAsset(DodgeTurnFolder+"/AC_PlaytestLocomotion.controller",ControllerPath))throw new IOException("Controller clone failed");
   var controller=Need<AnimatorController>(ControllerPath);var sm=controller.layers[0].stateMachine;
   var tree=(BlendTree)sm.states.First(s=>s.state.name=="Locomotion").state.motion;var children=tree.children;
   for(int i=0;i<children.Length;i++)
   {
    if(children[i].position==Vector2.zero){children[i].motion=clips["IdleStill"];continue;}
    for(int d=0;d<4;d++)foreach(var pair in new[]{("Walk",2.2f),("Run",5.5f)})
     if(Vector2.Distance(children[i].position,Axes[d]*pair.Item2)<.001f)
     {var c=clips[pair.Item1+Directions[d]];children[i].motion=c;children[i].cycleOffset=0;children[i].timeScale=Calibrate(model,source.UniformScale,c,Axes[d],pair.Item2,report);}
   }
   tree.children=children;EditorUtility.SetDirty(tree);
   foreach(string n in new[]{"StartForward","JumpRise","JumpFall","Land"})
   {var state=sm.states.First(s=>s.state.name==n).state;state.motion=clips[n];}
   foreach(string stage in new[]{"Rise","Fall"})
   {
    string parameter="Jump"+stage+"Progress";
    if(!controller.parameters.Any(p=>p.name==parameter))controller.AddParameter(parameter,AnimatorControllerParameterType.Float);
    var state=sm.states.First(s=>s.state.name=="Jump"+stage).state;state.timeParameter=parameter;state.timeParameterActive=true;
   }
   var land=sm.states.First(s=>s.state.name=="Land").state;land.speed=1.6f;
   var loco=sm.states.First(s=>s.state.name=="Locomotion").state;
   // Recover from impact while remaining responsive to continued travel.
   if(!land.transitions.Any(t=>t.destinationState==loco&&t.conditions.Any(c=>c.parameter=="PlanarSpeed")))
   {var transition=Transition(land,loco,"PlanarSpeed",AnimatorConditionMode.Greater,.12f,.2f);transition.hasExitTime=true;transition.exitTime=.3f;}
   var profile=Asset<WorldMacroPlayerAppearanceProfile>(ProfilePath);EditorUtility.CopySerialized(source,profile);
   profile.Controller=controller;profile.NaturalLocomotion=true;profile.Idle=clips["IdleStill"];
   profile.DirectionalWalk=Directions.Select(d=>clips["Walk"+d]).ToArray();profile.DirectionalRun=Directions.Select(d=>clips["Run"+d]).ToArray();
   profile.Walk=profile.DirectionalWalk[0];profile.Run=profile.DirectionalRun[0];profile.StartForward=clips["StartForward"];
   profile.JumpRise=clips["JumpRise"];profile.JumpFall=clips["JumpFall"];profile.Land=clips["Land"];
   var motor=Asset<PlayerLocomotionProfileSO>(MotorProfilePath);EditorUtility.CopySerialized(Need<PlayerLocomotionProfileSO>(DodgeTurnFolder+"/PlayerLocomotion.asset"),motor);
   motor.PreserveAuthoredFootRoll=true;
   EditorUtility.SetDirty(controller);EditorUtility.SetDirty(profile);EditorUtility.SetDirty(motor);AssetDatabase.SaveAssets();
   report.status="PREPARED_LIVE_QA_PENDING";report.pass.Add("Official Mixamo locomotion source directions replace inherited C02 leg abduction. Prior dodge, crouch roll, look-turn and seated clips retained.");return Save(report);
  }
 }
}
