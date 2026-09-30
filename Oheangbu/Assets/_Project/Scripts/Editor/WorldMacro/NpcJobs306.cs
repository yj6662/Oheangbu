using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #306 NPC job behaviour (SPEC-PLAYTEST-306 #5, PLAN §2-5). Queue-safe (refusals are strings, never a dialog), idempotent.
 //   import            — 15 Mixamo FBX (user-exported, Art/Characters/Npc306/mixamo) -> Clips/, Humanoid (C02 avatar when its bones
 //                       match, else from the model), root rotation / XZ (Original) and height (feet) baked into the pose, loops on work clips;
 //                       Blink Gathering -> a looping .anim copy (the vendor import is left alone). MiningLoop unused (no miners).
 //   controller        — AC_NpcJob (IK pass; Idle / Walk / Work0-2 / Talk / Glance / Sit / SitDown / StandUp) rebuilt in place
 //                       + one AnimatorOverrideController per humanoid role (asset GUIDs survive re-runs)
 //   profiles          — NpcJobProfileSO per role from the §2-5 table (+ axe / firewood tool prefabs from the Roadside303 meshes)
 //   author:<scene>    — NpcJobActor + hand tools on the NPC instances; does NOT move NPCs, prints proposed workplaces instead
 //   revert:<scene>    — actors and tools off, original controllers back
 //                       (author / revert first copy the saved scene + meta to Art/Playtest306/Backups/NpcJobs306; the oldest copy is kept)
 //   audit             — role -> point, actor, Animator, profile, clips (Play: live state too); clip root travel
 public static class NpcJobs306
 {
  const string Src="../Art/Characters/Npc306/mixamo";const string Folder="Assets/_Project/Art/Characters/Npc306";
  const string Clips=Folder+"/Clips",Ac=Folder+"/AC_NpcJob.controller",Ovr=Folder+"/Overrides",ToolDir=Folder+"/Tools",ProfileDir="Assets/_Project/Data/World/NpcJobs306";
  const string C02="Assets/_Project/Art/Characters/PlaytestReRig/Models/Player_C02_ReRig.fbx";
  const string Blink="Assets/Blink/Art/Animations/Animations_Starter_Pack/Gathering/Gathering.fbx";
  const string Natural="Assets/_Project/Art/Characters/PlaytestNaturalLocomotion/Animations/",Loco="Assets/_Project/Art/Characters/PlaytestLocomotion/Animations/";
  const string Road="Assets/_Project/Art/World/Roadside303/";const string ToolPrefix="NpcTool306_";
  static readonly string[] Scenes={"Assets/_Project/Scenes/World/W_Demo_Main.unity","Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity","Assets/_Project/Art/Characters/Folklore298/W_Demo_Compact_Folklore298.unity"};
  static readonly string[] ProtectedTrees={"Watershed295","Reworld292","MountainTrail285"};
  // file, loop (talk gestures play once and settle into Idle)
  static readonly (string file,bool loop)[] Files={("N306_Bartending",true),("N306_AxeDownward",true),("N306_WipingSweat",true),("N306_WorkingOnDevice",true),
   ("N306_LiftingObject",true),("N306_BoxIdle",true),("N306_PullHeavy",true),("N306_BoredIdle",true),("N306_AnnoyedHeadShake",false),("N306_DismissForward",false),
   ("N306_NervousLookAround",true),("N306_IdleLookAround",true),("N306_WritingSeated",true),("N306_TakeItemExamine",true),("N306_KneelingInspect",true)};
  static readonly string[] Slots={"Work0","Work1","Work2","Talk","Glance"};
  sealed class B{public string Label,Clip,Tool;public NpcJobProfileSO.Slot Slot;public float Min,Max,Weight,Length;public HumanBodyBones Bone=HumanBodyBones.RightHand;public Vector3 Pos=new Vector3(0,.04f,.02f),Euler,Offset;public float Yaw;}
  sealed class R{public string Id;public string[] Points;public NpcAttitude306 Attitude;public bool Generic;public string Talk="IdleStill",Glance="N306_IdleLookAround";public float GlanceHold=1.6f;public B[] Work=Array.Empty<B>();public string[] Workplace=Array.Empty<string>();}
  static B W(string label,NpcJobProfileSO.Slot slot,string clip,float min,float max,float weight,Vector3 offset,float yaw=0,string tool=null,float length=.5f,HumanBodyBones bone=HumanBodyBones.RightHand)
   =>new B{Label=label,Slot=slot,Clip=clip,Min=min,Max=max,Weight=weight,Offset=offset,Yaw=yaw,Tool=tool,Length=length,Bone=bone};
  const NpcJobProfileSO.Slot W0=NpcJobProfileSO.Slot.Work0,W1=NpcJobProfileSO.Slot.Work1,W2=NpcJobProfileSO.Slot.Work2,Sit=NpcJobProfileSO.Slot.Sit;
  // PLAN §2-5 role table (TEST). Tools: key -> prefab (ToolPrefab); poses are first guesses to tune in the editor.
  static readonly R[] Roles=
  {
   new R{Id="innkeeper",Points=new[]{"geumpyo_innkeeper"},Attitude=NpcAttitude306.Neutral,Workplace=new[]{"SorghumBroom","SM_Tray","Table","Bowl"},
    Work=new[]{W("상 차림",W0,"N306_Bartending",10,18,3,new Vector3(0,0,.6f),0,"bowl",.14f,HumanBodyBones.LeftHand),W("문간 서성임",W1,"N306_BoredIdle",6,10,1,new Vector3(.8f,0,0),-30)}},
   new R{Id="logger",Points=new[]{"logger"},Attitude=NpcAttitude306.Wary,Talk="N306_AnnoyedHeadShake",Workplace=new[]{"SplitFirewood","Firewood","Chopping","Stump"},
    Work=new[]{W("장작 패기",W0,"N306_AxeDownward",12,20,3,new Vector3(0,0,.5f),0,"axe",.75f),W("땀 닦기",W1,"N306_WipingSweat",4,7,1,new Vector3(.6f,0,.3f),35)}},
   new R{Id="herbalist",Points=new[]{"herbalist"},Attitude=NpcAttitude306.Wary,Talk="N306_DismissForward",Workplace=new[]{"SM_Basket","SM_Mortar","Basket"},
    Work=new[]{W("약초 살피기",W0,"N306_KneelingInspect",10,16,2,new Vector3(0,0,.5f),0,"sickle",.42f),W("약초 캐기",W1,"Gathering",8,14,2,new Vector3(.7f,0,.4f),25),W("풀 들여다보기",W2,"N306_TakeItemExamine",5,8,1,Vector3.zero)}},
   new R{Id="carter",Points=new[]{"carter303"},Attitude=NpcAttitude306.Wary,Talk="N306_AnnoyedHeadShake",Workplace=new[]{"SM_PullCar","Cart","SackOfRice"},
    Work=new[]{W("짐 들기",W0,"N306_LiftingObject",8,12,2,new Vector3(0,0,.5f)),W("바퀴 살피기",W1,"N306_KneelingInspect",8,12,1,new Vector3(.7f,0,.2f),30),W("짐 끌기",W2,"N306_PullHeavy",5,8,1,Vector3.zero)}},
   new R{Id="woodbrother",Points=new[]{"woodbrother303"},Attitude=NpcAttitude306.Wary,Glance="N306_NervousLookAround",Workplace=new[]{"Door","Gate","Threshold"},
    Work=new[]{W("숲 쪽 살핌",W0,"N306_NervousLookAround",8,14,2,Vector3.zero),W("문간에 앉음",Sit,"SitIdle",12,20,2,new Vector3(.5f,0,0))}},
   new R{Id="elder",Points=new[]{"hamlet_elder303"},Attitude=NpcAttitude306.Wary,Glance=null,GlanceHold=0,Workplace=new[]{"Threshold","Door","Step"},
    Work=new[]{W("문턱에 앉음",Sit,"SitIdle",20,40,4,Vector3.zero),W("서성임",W0,"N306_BoredIdle",5,8,1,new Vector3(0,0,.3f))}},
   new R{Id="merchant",Points=new[]{"village_shop"},Attitude=NpcAttitude306.Neutral,Workplace=new[]{"SM_Scale","Counter","Stall"},
    Work=new[]{W("물건 살피기",W0,"N306_TakeItemExamine",8,14,2,new Vector3(0,0,.3f)),W("손님 기다림",W1,"N306_BoredIdle",6,10,1,new Vector3(.5f,0,0))}},
   new R{Id="artisan",Points=new[]{"village_artisan"},Attitude=NpcAttitude306.Neutral,Workplace=new[]{"SharedWorkBench","WorkBench","Bench"},
    Work=new[]{W("망치질",W0,"N306_WorkingOnDevice",12,20,3,new Vector3(0,0,.4f),0,"hammer",.35f),W("땀 닦기",W1,"N306_WipingSweat",4,7,1,new Vector3(.5f,0,.2f))}},
   new R{Id="resident",Points=new[]{"village_resident"},Attitude=NpcAttitude306.Wary,Talk="N306_AnnoyedHeadShake",Workplace=new[]{"WoodPile","Firewood","Log"},
    Work=new[]{W("장작 나르기",W0,"N306_BoxIdle",8,12,2,new Vector3(0,0,.6f),0,"firewood",.5f),W("장작 쌓기",W1,"N306_LiftingObject",8,12,2,new Vector3(.8f,0,.3f)),W("서성임",W2,"N306_BoredIdle",5,8,1,Vector3.zero)}},
   // officials: cold stare, a partial turn (hostile: 60 deg at 90 deg/s), no glance clip; seated writing waits for a stool (weight 0, TEST)
   new R{Id="inspector",Points=new[]{"checkpoint_1","checkpoint_2"},Attitude=NpcAttitude306.Hostile,Glance=null,GlanceHold=0,Workplace=new[]{"InspectionDesk","Desk"},
    Work=new[]{W("장부 대조",W0,"N306_TakeItemExamine",10,16,2,Vector3.zero,0,"scroll",.3f,HumanBodyBones.LeftHand),W("서서 대기",W1,"N306_BoredIdle",6,10,1,Vector3.zero),W("앉아 기록",W2,"N306_WritingSeated",12,20,0,Vector3.zero)}},
   new R{Id="clerk",Points=new[]{"cargo_delivery"},Attitude=NpcAttitude306.Neutral,Workplace=new[]{"Cargo","Crate","Storage_Box"},
    Work=new[]{W("화물 셈",W0,"N306_TakeItemExamine",10,16,2,new Vector3(0,0,.4f),0,"scroll",.3f,HumanBodyBones.LeftHand),W("서서 대기",W1,"N306_BoredIdle",6,10,1,Vector3.zero),W("앉아 기록",W2,"N306_WritingSeated",12,20,0,Vector3.zero)}},
   // Principal256/257 (Generic rig, PROD-RIG exception D306): body turn only, their own controller stays
   new R{Id="wangso",Points=new[]{"wangso_w1"},Attitude=NpcAttitude306.Neutral,Generic=true,Talk=null,Glance=null,GlanceHold=0},
   new R{Id="jeongdam",Points=new[]{"jeongdam_j1"},Attitude=NpcAttitude306.Neutral,Generic=true,Talk=null,Glance=null,GlanceHold=0},
  };
  static string ToolPrefab(string key)=>key=="axe"?ToolDir+"/PF_NpcTool306_Axe.prefab":key=="firewood"?ToolDir+"/PF_NpcTool306_Firewood.prefab":key=="bowl"?"Assets/Korea_TreasureProps/Prefabs/SM_031_Bowl.prefab":
   key=="sickle"?"Assets/KoreanTraditionalFestival/Prefabs/SM_Sickle.prefab":key=="hammer"?"Assets/KoreanTraditionalFestival/Prefabs/SM_MeHammer.prefab":key=="scroll"?"Assets/Korea_TreasureProps/Prefabs/SM_029_Wooden_Scroll.prefab":null;

  public static string Run(string c)
  {
   c=(c??"").Trim();
   try
   {
    if(c=="audit")return Audit();
    if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Play mode (only audit runs in Play)";
    if(c=="import")return Import();
    if(c=="controller")return Controller();
    if(c=="profiles")return Profiles();
    if(c.StartsWith("author:",StringComparison.Ordinal))return Author(c.Substring(7).Trim());
    if(c.StartsWith("revert:",StringComparison.Ordinal))return Revert(c.Substring(7).Trim());
   }
   catch(Exception e){return "FAILED "+c+": "+e.GetType().Name+" "+e.Message;}
   return "REFUSED unknown command '"+c+"' (import | controller | profiles | author:<scene> | revert:<scene> | audit)";
  }

  // ---------- import ----------
  static string Import()
  {
   EnsureFolder(Clips);var sb=new StringBuilder();int done=0;
   var c02Importer=AssetImporter.GetAtPath(C02) as ModelImporter;var c02Avatar=AssetDatabase.LoadAllAssetsAtPath(C02).OfType<Avatar>().FirstOrDefault(a=>a.isHuman);
   var c02Bones=c02Importer!=null?c02Importer.humanDescription.human.Select(h=>h.boneName).ToArray():Array.Empty<string>();
   foreach(var (file,loop) in Files)
   {
    string src=Path.Combine(Abs(Src),file+".fbx"),dst=Clips+"/"+file+".fbx";
    if(!File.Exists(src)){sb.AppendLine("missing source "+src);continue;}
    if(!File.Exists(Abs(dst))||new FileInfo(Abs(dst)).Length!=new FileInfo(src).Length){File.Copy(src,Abs(dst),true);AssetDatabase.ImportAsset(dst,ImportAssetOptions.ForceUpdate);}
    var mi=(ModelImporter)AssetImporter.GetAtPath(dst);if(mi==null){sb.AppendLine("no importer "+dst);continue;}
    var model=AssetDatabase.LoadAssetAtPath<GameObject>(dst);var names=new HashSet<string>(model!=null?model.GetComponentsInChildren<Transform>(true).Select(t=>t.name):Enumerable.Empty<string>());
    bool copy=c02Avatar!=null&&c02Bones.Length>0&&c02Bones.All(names.Contains);
    mi.animationType=ModelImporterAnimationType.Human;
    if(copy){mi.avatarSetup=ModelImporterAvatarSetup.CopyFromOther;mi.sourceAvatar=c02Avatar;}else{mi.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;mi.sourceAvatar=null;}
    mi.materialImportMode=ModelImporterMaterialImportMode.None;mi.importAnimation=true;mi.SaveAndReimport();
    var take=mi.defaultClipAnimations.FirstOrDefault();if(take==null){sb.AppendLine("no take in "+dst);continue;}
    // in place: rotation and XZ stay in the pose (Original: Mixamo In Place keeps the hips over the start, so the feet stay planted);
    // height baked based upon feet (Player303Build / humanoid-clip-root-bake: Original Y loses the kneel and the lift); author forces
    // applyRootMotion off
    take.name=file;take.loopTime=loop;take.loopPose=loop;
    take.lockRootRotation=true;take.keepOriginalOrientation=true;
    take.lockRootHeightY=true;take.keepOriginalPositionY=false;take.heightFromFeet=true;
    take.lockRootPositionXZ=true;take.keepOriginalPositionXZ=true;
    mi.clipAnimations=new[]{take};mi.SaveAndReimport();
    var clip=FbxClip(dst);sb.AppendLine(file+" "+(clip!=null?clip.length.ToString("F2")+"s":"NO CLIP")+(loop?" loop":"")+" avatar "+(copy?"C02 copy":"from model"));done++;
   }
   // Blink Gathering (already Humanoid in the vendor import): a looping copy, so the package's own import settings stay untouched
   var gather=AssetDatabase.LoadAllAssetsAtPath(Blink).OfType<AnimationClip>().FirstOrDefault(x=>x.name=="Gathering");
   if(gather==null)sb.AppendLine("missing "+Blink+" (Gathering clip)");
   else
   {
    string path=Clips+"/N306_Gathering.anim";var fresh=Object.Instantiate(gather);fresh.name="N306_Gathering";
    var s=AnimationUtility.GetAnimationClipSettings(fresh);s.loopTime=true;s.loopBlend=true;s.loopBlendOrientation=true;s.loopBlendPositionY=true;s.loopBlendPositionXZ=true;
    s.keepOriginalOrientation=true;s.keepOriginalPositionY=false;s.keepOriginalPositionXZ=true;s.heightFromFeet=true;AnimationUtility.SetAnimationClipSettings(fresh,s);
    var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
    if(existing==null)AssetDatabase.CreateAsset(fresh,path);else{EditorUtility.CopySerialized(fresh,existing);existing.name="N306_Gathering";Object.DestroyImmediate(fresh);EditorUtility.SetDirty(existing);}
    sb.AppendLine("N306_Gathering.anim "+gather.length.ToString("F2")+"s loop (copy of Blink Gathering, humanMotion "+gather.humanMotion+")");
   }
   AssetDatabase.SaveAssets();
   return "import: "+done+"/"+Files.Length+" Mixamo clips in "+Clips+"\n"+sb;
  }

  // ---------- controller ----------
  static string Controller()
  {
   var idle=A(Natural+"IdleStill.anim");var walk=A(Natural+"WalkForward.anim");var sitIdle=A(Loco+"SitIdle.anim");var sitDown=A(Loco+"SitDown.anim");var standUp=A(Loco+"StandUp.anim");
   if(idle==null||walk==null||sitIdle==null)return "REFUSED reuse clips missing (IdleStill / WalkForward / SitIdle)";
   if(FbxClip(Clips+"/N306_BoredIdle.fbx")==null)return "REFUSED run 'import' first";
   EnsureFolder(Clips);EnsureFolder(Ovr);
   // empty placeholders: each slot needs its own key clip or one override would replace every slot at once
   var slot=new Dictionary<string,AnimationClip>();
   foreach(var s in Slots)
   {
    string p=Clips+"/NpcJob306_Slot_"+s+".anim";var clip=A(p);
    if(clip==null){clip=new AnimationClip{name="NpcJob306_Slot_"+s};AssetDatabase.CreateAsset(clip,p);}
    slot[s]=clip;
   }
   var ac=AssetDatabase.LoadAssetAtPath<AnimatorController>(Ac)??AnimatorController.CreateAnimatorControllerAtPath(Ac);
   var sm=ac.layers[0].stateMachine;
   foreach(var st in sm.states.ToArray())sm.RemoveState(st.state);
   foreach(var t in sm.anyStateTransitions.ToArray())sm.RemoveAnyStateTransition(t);
   AnimatorState Add(string n,Motion m,int row){var st=sm.AddState(n,new Vector3(300,row*60,0));st.motion=m;st.writeDefaultValues=true;return st;}
   var sIdle=Add("Idle",idle,0);Add("Walk",walk,1);int r=2;foreach(var s in Slots)Add(s,slot[s],r++);
   var sSit=Add("Sit",sitIdle,r++);
   if(sitDown!=null){var sd=Add("SitDown",sitDown,r++);var t=sd.AddTransition(sSit);t.hasExitTime=true;t.exitTime=.95f;t.duration=.2f;t.hasFixedDuration=true;}
   if(standUp!=null){var su=Add("StandUp",standUp,r++);var t=su.AddTransition(sIdle);t.hasExitTime=true;t.exitTime=.95f;t.duration=.2f;t.hasFixedDuration=true;}
   sm.defaultState=sIdle;
   var layers=ac.layers;layers[0].iKPass=true;ac.layers=layers;EditorUtility.SetDirty(ac);
   var sb=new StringBuilder();
   foreach(var role in Roles.Where(x=>!x.Generic))
   {
    string p=Ovr+"/AOC_NpcJob306_"+role.Id+".overrideController";var ovr=AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(p);
    if(ovr==null){ovr=new AnimatorOverrideController(ac){name="AOC_NpcJob306_"+role.Id};AssetDatabase.CreateAsset(ovr,p);}
    ovr.runtimeAnimatorController=ac;
    var map=new Dictionary<string,AnimationClip>();
    foreach(var b in role.Work)if(b.Slot!=Sit)map[b.Slot.ToString()]=Human(Clip(b.Clip));
    map["Talk"]=Human(Clip(role.Talk))??idle;map["Glance"]=Human(Clip(role.Glance))??Human(Clip("N306_IdleLookAround"))??idle;
    var pairs=new List<KeyValuePair<AnimationClip,AnimationClip>>();ovr.GetOverrides(pairs);
    for(int i=0;i<pairs.Count;i++){var key=pairs[i].Key;string s=Slots.FirstOrDefault(x=>slot[x]==key);if(s!=null)pairs[i]=new KeyValuePair<AnimationClip,AnimationClip>(key,map.TryGetValue(s,out var v)&&v!=null?v:idle);}
    ovr.ApplyOverrides(pairs);EditorUtility.SetDirty(ovr);
    sb.AppendLine(role.Id+": "+string.Join(", ",Slots.Select(s=>s+"="+(map.TryGetValue(s,out var v)&&v!=null?v.name:"IdleStill"))));
   }
   AssetDatabase.SaveAssets();
   return "controller "+Ac+" states "+sm.states.Length+" (IK pass on layer 0)\n"+sb;
  }

  // ---------- profiles ----------
  static string Profiles()
  {
   EnsureFolder(ProfileDir);EnsureFolder(ToolDir);var sb=new StringBuilder();
   sb.AppendLine(BuildAxe());sb.AppendLine(BuildFirewood());
   foreach(var role in Roles)
   {
    string p=ProfileDir+"/NpcJob306_"+role.Id+".asset";var so=AssetDatabase.LoadAssetAtPath<NpcJobProfileSO>(p);
    if(so==null){so=ScriptableObject.CreateInstance<NpcJobProfileSO>();AssetDatabase.CreateAsset(so,p);}
    else{var fresh=ScriptableObject.CreateInstance<NpcJobProfileSO>();EditorUtility.CopySerialized(fresh,so);Object.DestroyImmediate(fresh);}
    so.name="NpcJob306_"+role.Id;so.RoleId=role.Id;so.Attitude=role.Attitude;
    so.HeadWeight=NpcJobProfileSO.DefaultHeadWeight(role.Attitude);so.TurnDegreesPerSecond=role.Generic?120:NpcJobProfileSO.DefaultTurnRate(role.Attitude);so.MaxTurnDegrees=NpcJobProfileSO.DefaultMaxTurn(role.Attitude);
    so.GlanceHoldSeconds=role.GlanceHold;so.GlanceTurnsBody=false;
    so.Controller=role.Generic?null:AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(Ovr+"/AOC_NpcJob306_"+role.Id+".overrideController");
    so.TalkClip=Clip(role.Talk);so.GlanceClip=Clip(role.Glance);
    var missing=new List<string>();
    so.Behaviours=role.Work.Select(b=>
    {
     var tool=b.Tool!=null?AssetDatabase.LoadAssetAtPath<GameObject>(ToolPrefab(b.Tool)):null;if(b.Tool!=null&&tool==null)missing.Add("tool "+b.Tool);
     // a missing or non-humanoid clip plays IdleStill in its slot: weight 0 so the NPC never "works" standing still
     var clip=Clip(b.Clip);bool usable=clip!=null&&clip.humanMotion;if(!usable)missing.Add((clip==null?"clip ":"non-humanoid clip ")+b.Clip+" (weight 0)");
     return new NpcJobProfileSO.Behaviour{Label=b.Label,State=b.Slot,Clip=clip,MinSeconds=b.Min,MaxSeconds=b.Max,Weight=usable?b.Weight:0,HandTool=tool,HandBone=b.Bone,
      ToolLength=b.Length,ToolLocalPosition=b.Pos,ToolLocalEuler=b.Euler,Offset=b.Offset,Yaw=b.Yaw};
    }).ToArray();
    if(!role.Generic&&so.Controller==null)missing.Add("override controller (run 'controller')");
    EditorUtility.SetDirty(so);
    sb.AppendLine(role.Id+" "+role.Attitude+" behaviours "+so.Behaviours.Length+" head "+so.HeadWeight.ToString("F1")+" turn "+so.TurnDegreesPerSecond+"/s max "+so.MaxTurnDegrees+(missing.Count>0?" MISSING "+string.Join(", ",missing):""));
   }
   AssetDatabase.SaveAssets();
   return "profiles in "+ProfileDir+"\n"+sb;
  }
  // Roadside303 jige axe / firewood meshes (a Roadside303 rebuild rewrites them in place; re-run to refresh)
  static string BuildAxe()
  {
   var handle=AssetDatabase.LoadAssetAtPath<Mesh>(Road+"Meshes/changgui_jige303_axeHandle.asset");var head=AssetDatabase.LoadAssetAtPath<Mesh>(Road+"Meshes/changgui_jige303_axeHead.asset");
   var wood=AssetDatabase.LoadAssetAtPath<Material>(Road+"Materials/WeatheredWood303.mat");var iron=AssetDatabase.LoadAssetAtPath<Material>(Road+"Materials/PittedIron303.mat");
   if(handle==null||head==null||wood==null||iron==null)return "axe: Roadside303 meshes/materials missing (tool left empty)";
   var root=new GameObject("PF_NpcTool306_Axe");
   try
   {
    var h=Part(root.transform,"AxeHandle",handle,wood);var hd=Part(h,"AxeHead",head,iron);hd.localPosition=new Vector3(0,.03f,.7f);hd.localRotation=Quaternion.Euler(0,0,90);
    PrefabUtility.SaveAsPrefabAsset(root,ToolDir+"/PF_NpcTool306_Axe.prefab");
   }
   finally{Object.DestroyImmediate(root);}
   return "axe: "+ToolDir+"/PF_NpcTool306_Axe.prefab";
  }
  static string BuildFirewood()
  {
   var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Road+"Meshes/changgui_jige303_wood0.asset");var wood=AssetDatabase.LoadAssetAtPath<Material>(Road+"Materials/WeatheredWood303.mat");
   if(mesh==null||wood==null)return "firewood: Roadside303 mesh/material missing (tool left empty)";
   var root=new GameObject("PF_NpcTool306_Firewood");
   try
   {
    for(int i=0;i<3;i++){var p=Part(root.transform,"Split"+i,mesh,wood);p.localPosition=new Vector3((i-1)*.07f,i==1?.06f:0,0);p.localRotation=Quaternion.Euler(0,(i-1)*6,0);}
    PrefabUtility.SaveAsPrefabAsset(root,ToolDir+"/PF_NpcTool306_Firewood.prefab");
   }
   finally{Object.DestroyImmediate(root);}
   return "firewood: "+ToolDir+"/PF_NpcTool306_Firewood.prefab";
  }
  static Transform Part(Transform parent,string name,Mesh mesh,Material mat)
  {
   var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
   var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=mat;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;return go.transform;
  }

  // ---------- author / revert ----------
  static string SceneGuard(string path,out Scene scene)
  {
   scene=default;
   if(!Scenes.Contains(path))return "REFUSED scene not in the #306 list: "+path;
   for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)return "REFUSED dirty scene "+SceneManager.GetSceneAt(i).path;
   scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);return null;
  }
  // the scenes are untracked (~50 MB): copy the saved file (+meta) before the first write; the oldest copy wins, so a second author
  // run never overwrites the pre-author state. SceneGuard has refused dirty scenes, so the file on disk is the state being changed.
  static string BackupScene(string path)
  {
   string dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/Playtest306/Backups/NpcJobs306"));Directory.CreateDirectory(dir);
   string dst=Path.Combine(dir,Path.GetFileName(path));
   if(File.Exists(dst))return dst+" (kept older backup)";
   File.Copy(Abs(path),dst);if(File.Exists(Abs(path)+".meta"))File.Copy(Abs(path)+".meta",dst+".meta",true);
   return dst;
  }
  static string Author(string path)
  {
   var refusal=SceneGuard(path,out var scene);if(refusal!=null)return refusal;string backup=BackupScene(path);
   var session=Object.FindObjectsByType<WorldMacroPlaytestSession>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(s=>s.gameObject.scene==scene);
   var points=Object.FindObjectsByType<WorldMacroContentPoint>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(p=>p.gameObject.scene==scene).ToArray();
   var sb=new StringBuilder();var proposals=new StringBuilder();int authored=0,tools=0;
   foreach(var role in Roles)foreach(var id in role.Points)
   {
    var point=points.FirstOrDefault(p=>p.Id==id);if(point==null){sb.AppendLine(id+": no point in scene");continue;}
    if(Protected(point.transform)){sb.AppendLine(id+": under a protected tree, skipped");continue;}
    var profile=AssetDatabase.LoadAssetAtPath<NpcJobProfileSO>(ProfileDir+"/NpcJob306_"+role.Id+".asset");if(profile==null){sb.AppendLine(id+": no profile (run 'profiles')");continue;}
    var anim=point.GetComponentsInChildren<Animator>(true).FirstOrDefault(a=>a.name=="Appearance")??point.GetComponentsInChildren<Animator>(true).FirstOrDefault();
    if(anim==null){sb.AppendLine(id+": no Animator under the point");continue;}
    var actor=anim.GetComponent<NpcJobActor>()??anim.gameObject.AddComponent<NpcJobActor>();
    var current=anim.runtimeAnimatorController;
    bool ours=current!=null&&(current.name.StartsWith("AOC_NpcJob306_",StringComparison.Ordinal)||AssetDatabase.GetAssetPath(current)==Ac);
    if(actor.OriginalController==null&&!ours)actor.OriginalController=current;
    bool human=anim.avatar!=null&&anim.avatar.isHuman&&!role.Generic;
    if(human&&profile.Controller!=null)anim.runtimeAnimatorController=profile.Controller;
    if(human)anim.applyRootMotion=false;   // job clips are in place; the actor moves Body itself
    actor.Session=session;actor.Profile=profile;actor.PointId=id;actor.Animator=anim;actor.Body=anim.transform.parent!=null?anim.transform.parent:anim.transform;
    actor.Motion=actor.Body.GetComponent<OwnedActorMotion>();actor.Seed=StableHash(id);
    foreach(var t in anim.GetComponentsInChildren<Transform>(true).Where(t=>t!=null&&t.name.StartsWith(ToolPrefix,StringComparison.Ordinal)).ToArray())if(t!=null)Object.DestroyImmediate(t.gameObject);
    var list=new GameObject[profile.Behaviours.Length];var notes=new List<string>();
    if(human)for(int i=0;i<list.Length;i++)
    {
     var b=profile.Behaviours[i];if(b.HandTool==null)continue;var bone=Bone(anim,b.HandBone);
     if(bone==null){notes.Add("no bone "+b.HandBone);continue;}
     list[i]=Tool(bone,b,i,anim.gameObject.layer,out string note);if(note!=null)notes.Add(note);if(list[i]!=null)tools++;
    }
    actor.Tools=list;EditorUtility.SetDirty(actor);EditorUtility.SetDirty(anim);authored++;
    sb.AppendLine(id+" -> "+role.Id+" "+(human?"humanoid "+(profile.Controller!=null?profile.Controller.name:"(no controller)"):"generic body-turn")+" tools "+list.Count(x=>x!=null)+(notes.Count>0?" | "+string.Join("; ",notes):""));
    proposals.AppendLine(Propose(role,id,point,points));
   }
   if(session==null)sb.AppendLine("WARNING no WorldMacroPlaytestSession in scene (actors look it up at Start)");
   EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   return "author "+path+" (backup "+backup+"): "+authored+" actors, "+tools+" hand tools (NPCs not moved)\n"+sb+"\nproposed workplaces (batch 4 moves the point and WorldContent positions together; not applied):\n"+proposals;
  }
  static string Revert(string path)
  {
   var refusal=SceneGuard(path,out var scene);if(refusal!=null)return refusal;string backup=BackupScene(path);int n=0;
   foreach(var actor in Object.FindObjectsByType<NpcJobActor>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(a=>a.gameObject.scene==scene).ToArray())
   {
    var anim=actor.Animator!=null?actor.Animator:actor.GetComponent<Animator>();
    if(anim!=null)
    {
     var current=anim.runtimeAnimatorController;
     bool ours=current!=null&&(current.name.StartsWith("AOC_NpcJob306_",StringComparison.Ordinal)||AssetDatabase.GetAssetPath(current)==Ac);
     // author records nothing when the original was null: clear the job controller instead of leaving it on
     if(actor.OriginalController!=null)anim.runtimeAnimatorController=actor.OriginalController;else if(ours)anim.runtimeAnimatorController=null;
     EditorUtility.SetDirty(anim);
     foreach(var t in anim.GetComponentsInChildren<Transform>(true).Where(t=>t!=null&&t.name.StartsWith(ToolPrefix,StringComparison.Ordinal)).ToArray())if(t!=null)Object.DestroyImmediate(t.gameObject);
    }
    if(actor.Motion!=null){actor.Motion.enabled=true;EditorUtility.SetDirty(actor.Motion);}
    Object.DestroyImmediate(actor);n++;
   }
   EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   return "revert "+path+" (backup "+backup+"): "+n+" actors removed, controllers restored, tools removed (applyRootMotion is not restored: NpcJobActor records no original)";
  }
  static GameObject Tool(Transform bone,NpcJobProfileSO.Behaviour b,int i,int layer,out string note)
  {
   note=null;var copy=(GameObject)Object.Instantiate(b.HandTool);copy.name="Mesh";
   foreach(var x in copy.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(x);
   foreach(var x in copy.GetComponentsInChildren<Rigidbody>(true))Object.DestroyImmediate(x);
   foreach(var x in copy.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(x);
   foreach(var x in copy.GetComponentsInChildren<Light>(true))Object.DestroyImmediate(x);
   foreach(var x in copy.GetComponentsInChildren<ParticleSystem>(true))Object.DestroyImmediate(x.gameObject);
   var rs=copy.GetComponentsInChildren<Renderer>(true);
   // glow cap (ART-INK): a hand tool never carries emission
   if(rs.Length==0||rs.SelectMany(r=>r.sharedMaterials).Any(Emissive)){note=(rs.Length==0?"no renderer ":"emissive ")+b.HandTool.name+" refused";Object.DestroyImmediate(copy);return null;}
   foreach(var t in copy.GetComponentsInChildren<Transform>(true)){t.gameObject.layer=layer;GameObjectUtility.SetStaticEditorFlags(t.gameObject,(StaticEditorFlags)0);}
   copy.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);copy.transform.localScale=Vector3.one;
   Bounds bounds=rs[0].bounds;foreach(var r in rs)bounds.Encapsulate(r.bounds);
   float s=b.ToolLength/Mathf.Max(.001f,Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z))),lossy=Mathf.Max(.001f,bone.lossyScale.x);
   var root=new GameObject(ToolPrefix+i+"_"+b.HandTool.name).transform;root.gameObject.layer=layer;root.SetParent(bone,false);
   root.localPosition=b.ToolLocalPosition/lossy;root.localRotation=Quaternion.Euler(b.ToolLocalEuler);root.localScale=Vector3.one;
   copy.transform.SetParent(root,false);copy.transform.localScale=Vector3.one*(s/lossy);copy.transform.localPosition=-bounds.center*(s/lossy);copy.transform.localRotation=Quaternion.identity;
   root.gameObject.SetActive(false);return root.gameObject;
  }
  static bool Emissive(Material m)=>m!=null&&(m.IsKeywordEnabled("_EMISSION")&&m.HasProperty("_EmissionColor")&&m.GetColor("_EmissionColor").maxColorComponent>.001f);
  static Transform Bone(Animator anim,HumanBodyBones bone)
  {
   Transform t=null;try{t=anim.GetBoneTransform(bone);}catch(Exception){}
   if(t!=null||anim.avatar==null||!anim.avatar.isHuman)return t;
   string human=HumanTrait.BoneName[(int)bone];var hb=anim.avatar.humanDescription.human.FirstOrDefault(h=>h.humanName==human);
   return string.IsNullOrEmpty(hb.boneName)?null:anim.GetComponentsInChildren<Transform>(true).FirstOrDefault(x=>x.name==hb.boneName);
  }
  // nearest workplace-like object (name tokens) within 70 m, outside NPC points, vacant houses and protected trees
  static string Propose(R role,string id,WorldMacroContentPoint point,WorldMacroContentPoint[] points)
  {
   var content=Content(point.gameObject.scene);var cp=content?.Points?.FirstOrDefault(p=>p!=null&&p.Id==id);
   Vector3 at=cp!=null?cp.Position:point.transform.position;string head=id+" ("+role.Id+") now "+V(at)+(cp!=null?" r "+cp.Radius.ToString("F1"):"");
   if(role.Workplace.Length==0)return head+" -> keep (no workplace for this role in phase 1)";
   var npcRoots=points.Where(p=>Roles.Any(r=>r.Points.Contains(p.Id))).Select(p=>p.transform).ToArray();
   Transform best=null;float bd=70;
   foreach(var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
   {
    if(t.gameObject.scene!=point.gameObject.scene||(t.GetComponent<MeshRenderer>()==null&&t.GetComponent<LODGroup>()==null))continue;
    if(!role.Workplace.Any(w=>t.name.IndexOf(w,StringComparison.OrdinalIgnoreCase)>=0))continue;
    if(npcRoots.Any(r=>t.IsChildOf(r))||Protected(t)||PathOf(t).IndexOf("vacant",StringComparison.OrdinalIgnoreCase)>=0)continue;
    float d=Vector3.Distance(Flat(t.position),Flat(at));if(d<bd){bd=d;best=t;}
   }
   if(best==null)return head+" -> no candidate ("+string.Join("/",role.Workplace)+") within 70 m: keep";
   var dir=Flat(at)-Flat(best.position);dir=dir.sqrMagnitude>.01f?dir.normalized:Vector3.forward;var q=best.position+dir*1.1f;
   if(Physics.Raycast(q+Vector3.up*20,Vector3.down,out var hit,60,~0,QueryTriggerInteraction.Ignore))q.y=hit.point.y;
   string crowd=string.Join(",",points.Where(p=>p.Id!=id&&Roles.Any(r=>r.Points.Contains(p.Id))&&Vector3.Distance(Flat(p.transform.position),Flat(q))<2.5f).Select(p=>p.Id));
   return head+" -> "+V(q)+" beside "+PathOf(best)+" ("+bd.ToString("F1")+" m)"+(crowd.Length>0?" NEAR "+crowd:"");
  }

  // ---------- audit ----------
  static string Audit()
  {
   var sb=new StringBuilder();var scene=SceneManager.GetActiveScene();sb.AppendLine("scene "+scene.path+(EditorApplication.isPlaying?" (Play)":" (Edit)"));
   var ac=AssetDatabase.LoadAssetAtPath<AnimatorController>(Ac);
   sb.AppendLine("controller "+(ac!=null?ac.layers[0].stateMachine.states.Length+" states, IK "+ac.layers[0].iKPass:"MISSING"));
   foreach(var (file,_) in Files){var c=FbxClip(Clips+"/"+file+".fbx");sb.AppendLine("  "+file+" "+(c==null?"MISSING":c.length.ToString("F2")+"s loop "+c.isLooping+" human "+c.humanMotion+Travel(c)));}
   var g=A(Clips+"/N306_Gathering.anim");sb.AppendLine("  N306_Gathering "+(g==null?"MISSING":g.length.ToString("F2")+"s loop "+g.isLooping+Travel(g)));
   var points=Object.FindObjectsByType<WorldMacroContentPoint>(FindObjectsInactive.Include,FindObjectsSortMode.None);
   foreach(var role in Roles)
   {
    var profile=AssetDatabase.LoadAssetAtPath<NpcJobProfileSO>(ProfileDir+"/NpcJob306_"+role.Id+".asset");
    foreach(var id in role.Points)
    {
     var point=points.FirstOrDefault(p=>p.Id==id);
     var actor=point!=null?point.GetComponentsInChildren<NpcJobActor>(true).FirstOrDefault():null;var anim=actor!=null?actor.Animator:point!=null?point.GetComponentsInChildren<Animator>(true).FirstOrDefault():null;
     var line=new StringBuilder(role.Id+" "+id+": point "+(point!=null)+" actor "+(actor!=null?(actor.isActiveAndEnabled?"on":"off"):"none"));
     line.Append(" animator "+(anim==null?"none":(anim.isHuman?"human ":"generic ")+(anim.runtimeAnimatorController!=null?anim.runtimeAnimatorController.name:"no controller")));
     line.Append(" profile "+(profile!=null?profile.name+(actor!=null&&actor.Profile!=profile?" (actor has "+(actor.Profile!=null?actor.Profile.name:"none")+")":""):"MISSING"));
     if(anim!=null&&anim.runtimeAnimatorController is AnimatorOverrideController ovr)
     {var pairs=new List<KeyValuePair<AnimationClip,AnimationClip>>();ovr.GetOverrides(pairs);line.Append(" clips "+string.Join(" ",pairs.Where(p=>p.Key!=null&&p.Key.name.StartsWith("NpcJob306_Slot_")).Select(p=>p.Key.name.Substring(15)+"="+(p.Value!=null?p.Value.name:"EMPTY"))));}
     if(profile!=null)line.Append(" behaviours "+string.Join(",",profile.Behaviours.Select(b=>b.State+":"+(b.Clip!=null?b.Clip.name:"-")+(b.HandTool!=null?"+"+b.HandTool.name:""))));
     if(actor!=null)line.Append(" tools "+actor.Tools.Count(t=>t!=null)+"/"+actor.Tools.Length+" motion "+(actor.Motion!=null?(actor.Motion.enabled?"on":"off"):"none"));
     if(actor!=null&&EditorApplication.isPlaying)line.Append(" | state "+actor.State+" behaviour "+actor.Current+" playing "+actor.Playing+" look "+actor.LookWeight.ToString("F2")+" glance "+actor.Glancing+" carried "+actor.Carried+" allowed "+actor.Allowed.ToString("F2")+" m update "+(actor.Animator!=null?actor.Animator.updateMode.ToString():"-")+(actor.ListenToDialogueRequests?" listens DialogueRequested":""));
     sb.AppendLine(line.ToString());
    }
   }
   return sb.ToString();
  }
  static string Travel(AnimationClip c)
  {
   var bx=AnimationUtility.GetCurveBindings(c).FirstOrDefault(x=>x.propertyName=="RootT.x");var bz=AnimationUtility.GetCurveBindings(c).FirstOrDefault(x=>x.propertyName=="RootT.z");
   if(bx.propertyName==null||bz.propertyName==null)return "";
   var cx=AnimationUtility.GetEditorCurve(c,bx);var cz=AnimationUtility.GetEditorCurve(c,bz);
   return " rootXZ travel "+new Vector2(cx.Evaluate(c.length)-cx.Evaluate(0),cz.Evaluate(c.length)-cz.Evaluate(0)).magnitude.ToString("F2")+" m";
  }

  // ---------- helpers ----------
  static AnimationClip Clip(string key)
  {
   if(string.IsNullOrEmpty(key))return null;
   if(key=="IdleStill"||key=="WalkForward")return A(Natural+key+".anim");
   if(key=="SitIdle"||key=="SitDown"||key=="StandUp")return A(Loco+key+".anim");
   if(key=="Gathering")return A(Clips+"/N306_Gathering.anim");
   return FbxClip(Clips+"/"+key+".fbx");
  }
  static AnimationClip A(string p)=>AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
  static AnimationClip Human(AnimationClip c)=>c!=null&&c.humanMotion?c:null;
  static AnimationClip FbxClip(string p)=>AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().FirstOrDefault(c=>!c.name.StartsWith("__preview__",StringComparison.Ordinal));
  static WorldMacroPlaytestSO Content(Scene scene)=>Object.FindObjectsByType<WorldMacroPlaytestSession>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(s=>s.gameObject.scene==scene).Select(s=>s.Content).FirstOrDefault(c=>c!=null);
  static bool Protected(Transform t){var p=PathOf(t);return ProtectedTrees.Any(x=>p.IndexOf(x,StringComparison.Ordinal)>=0);}
  static string PathOf(Transform t){var s=t.name;for(var p=t.parent;p!=null;p=p.parent)s=p.name+"/"+s;return s;}
  static Vector3 Flat(Vector3 v)=>new Vector3(v.x,0,v.z);
  static string V(Vector3 v)=>"("+v.x.ToString("F1")+", "+v.y.ToString("F1")+", "+v.z.ToString("F1")+")";
  static int StableHash(string s){unchecked{int h=17;foreach(char ch in s??"")h=h*31+ch;return h;}}
  static string Abs(string p)=>Path.GetFullPath(Path.Combine(Application.dataPath,"..",p));
  static void EnsureFolder(string path)
  {
   if(AssetDatabase.IsValidFolder(path))return;
   var parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
  }
 }
}
