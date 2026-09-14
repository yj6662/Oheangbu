using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using Oheangbu.App.World;
using Oheangbu.App.World.Dressing;
using Oheangbu.Combat;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 [InitializeOnLoad] public static class PlaytestNaturalMotionReview
 {
  [Serializable] public class Row { public string phase,state; public float time,yaw,body,look,height,progress,timeScale;public bool rolling,dodging,crouching,grounded,turning,blocked;public int turnSerial;public Vector3 position,leftFoot,rightFoot,leftKnee,rightKnee;public float speed,vertical,ankleWidth,normalizedTime,legLengthError;public bool leftPlanted,rightPlanted; }
  [Serializable] public class Result {public string status,error,profile,scope="Virtual keyboard/mouse injected through real InputSystem and live scene frames. Enemy GameObjects temporarily suspended and restored for isolated locomotion QA; no combat or native-user/full-route evidence.";public bool restored;public Row[] frames;public string[] screenshots;}
  static WorldMacroCombatWalker walker;static WorldMacroPlayerAppearance appearance;static InputActionAsset actions;
  static ReadOnlyArray<InputDevice>? oldDevices;static Keyboard keyboard,oldKeyboard;static Mouse mouse,oldMouse;
  static bool running,oldBackground;static double start;static Vector3 position;static Quaternion rotation;static float sensitivity;static int previous=-1;
  static readonly List<Row> rows=new List<Row>();static readonly List<string> shots=new List<string>();static readonly HashSet<string> captured=new HashSet<string>();
  static string directory;static Result result;
  static GameObject[] enemies;static bool[] enemyActive;
  static readonly string[] phases={"settle","walk_forward","walk_back","walk_left","walk_right","run_forward","run_back","run_left","run_right","diagonal","reverse_diagonal","stop","jump","landed","walk_jump","land_walk","crouch","crouch_walk","roll","stand","look89","look101","turn_settle"};
  const float Seconds=2f;
  static PlaytestNaturalMotionReview(){AssemblyReloadEvents.beforeAssemblyReload+=()=>{if(running)Finish("INTERRUPTED");};EditorApplication.playModeStateChanged+=s=>{if(running&&s==PlayModeStateChange.ExitingPlayMode)Finish("INTERRUPTED");};}
  public static string Execute(string command)
  {
   if(command=="abort"){if(running)Finish("ABORTED");return "Stopped";}
   if(command=="status")return running?"RUNNING":directory??"IDLE";
   if(running||!Application.isPlaying||Time.timeScale<.99f)throw new InvalidOperationException("Unpaused Play required");
   walker=Object.FindFirstObjectByType<WorldMacroCombatWalker>();appearance=Object.FindFirstObjectByType<WorldMacroPlayerAppearance>();var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
   if(walker==null||appearance==null||session==null||string.IsNullOrEmpty(session.TestSaveSuffix)||walker.Motor.RuntimeState.InputBlocked)throw new InvalidOperationException("Active isolated test slot required");
   actions=(InputActionAsset)typeof(PlayerMotor).GetField("_actions",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(walker.Motor);
   var config=(CombatConfigSO)typeof(PlayerMotor).GetField("_config",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(walker.Motor);
   sensitivity=config.LookSensitivity*walker.Motor.RuntimeState.LookSensitivity;
   oldDevices=actions.devices;oldKeyboard=Keyboard.current;oldMouse=Mouse.current;oldBackground=Application.runInBackground;
   position=walker.Body.transform.position;rotation=walker.Body.transform.rotation;
   directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestRecovery/NaturalLocomotion/Live",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));Directory.CreateDirectory(directory);
   rows.Clear();shots.Clear();captured.Clear();previous=-1;result=new Result{status="RUNNING",profile=AssetDatabase.GetAssetPath(appearance.Profile)};
   enemies=Object.FindObjectsByType<EnemyController>(FindObjectsInactive.Include,FindObjectsSortMode.None).Select(e=>e.gameObject).Distinct().ToArray();enemyActive=enemies.Select(e=>e.activeSelf).ToArray();
   foreach(var e in enemies)e.SetActive(false);
   keyboard=InputSystem.AddDevice<Keyboard>("NaturalMotionVirtualKeyboard");mouse=InputSystem.AddDevice<Mouse>("NaturalMotionVirtualMouse");actions.devices=new InputDevice[]{keyboard,mouse};
   running=true;Application.runInBackground=true;start=EditorApplication.timeSinceStartup;InputSystem.onBeforeUpdate+=Input;EditorApplication.update+=Observe;return "RUNNING local input exercise";
  }
  static void Input()
  {
   if(!running||InputState.currentUpdateType!=InputUpdateType.Dynamic)return;
   int p=(int)((EditorApplication.timeSinceStartup-start)/Seconds);if(p>=phases.Length)return;
   bool edge=p!=previous;previous=p;Key[] keys=Array.Empty<Key>();float dx=0;
   if(p>=1&&p<=4)keys=new[]{new[]{Key.W,Key.S,Key.A,Key.D}[p-1]};
   if(p>=5&&p<=8)keys=new[]{new[]{Key.W,Key.S,Key.A,Key.D}[p-5],Key.LeftCtrl};
   if(p==9)keys=new[]{Key.W,Key.D};if(p==10)keys=new[]{Key.S,Key.A};
   if(p==14)keys=edge?new[]{Key.W,Key.Space}:new[]{Key.W};
   if(p==15)keys=new[]{Key.S};if(p==17)keys=new[]{Key.W};
   if(edge){if(p==12)keys=new[]{Key.Space};if(p==16||p==19)keys=new[]{Key.C};if(p==18)keys=new[]{Key.S,Key.LeftShift};if(p==20)dx=89;if(p==21)dx=12;}
   InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(Screen.width*.5f,Screen.height*.5f),delta=new Vector2(dx/Mathf.Max(.001f,sensitivity),0)});
  }
  static void Observe()
  {
   if(!running)return;try{
    double t=EditorApplication.timeSinceStartup-start;int p=(int)(t/Seconds);if(p>=phases.Length){Finish("RECORDED_REQUIRES_ASSESSMENT");return;}
    var m=walker.Motor;var a=appearance.Animator;var info=a.GetCurrentAnimatorStateInfo(0);
    string state=new[]{"Locomotion","TurnLeft","TurnRight","Crouch","CrouchTurnLeft","CrouchTurnRight","Dodge","CrouchRoll","StartForward","JumpRise","JumpFall","Land"}.FirstOrDefault(s=>info.IsName(s))??info.shortNameHash.ToString();
    rows.Add(new Row{phase=phases[p],state=state,time=(float)t,timeScale=Time.timeScale,blocked=m.RuntimeState.InputBlocked,yaw=walker.Body.transform.eulerAngles.y,body=appearance.BodyYaw,look=appearance.LookYaw,turning=appearance.TurningInPlace,turnSerial=appearance.TurnSerial,height=walker.Body.height,rolling=m.IsRolling,dodging=m.IsDodging,crouching=m.IsCrouching,grounded=m.IsLocomotionGrounded,progress=m.DodgeProgress,position=walker.Body.transform.position,leftFoot=a.GetBoneTransform(HumanBodyBones.LeftFoot).position,rightFoot=a.GetBoneTransform(HumanBodyBones.RightFoot).position,leftKnee=a.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position,rightKnee=a.GetBoneTransform(HumanBodyBones.RightLowerLeg).position,speed=m.ActualLocalVelocity.magnitude,vertical=m.VerticalVelocity,normalizedTime=info.normalizedTime,ankleWidth=Mathf.Abs(walker.Body.transform.InverseTransformVector(a.GetBoneTransform(HumanBodyBones.LeftFoot).position-a.GetBoneTransform(HumanBodyBones.RightFoot).position).x),legLengthError=appearance.GetComponent<WorldMacroPlayerFootPlacement>().Diagnostics.MaximumLocalLengthError,leftPlanted=appearance.GetComponent<WorldMacroPlayerFootPlacement>().Diagnostics.LeftPlanted,rightPlanted=appearance.GetComponent<WorldMacroPlayerFootPlacement>().Diagnostics.RightPlanted});
    string shot=null;
    if((p>=1&&p<=10||p==17)&&t%Seconds>.9)shot=phases[p];
    if((p==12||p==14)&&!m.IsLocomotionGrounded)shot=m.VerticalVelocity>1?phases[p]+"_rise":m.VerticalVelocity>-.7?phases[p]+"_apex":phases[p]+"_fall";
    if((p==12||p==14)&&state=="Land")shot=phases[p]+"_contact";
    if(p==0&&t%Seconds>.8)shot="idle";
    if(m.IsRolling&&m.DodgeProgress>.3&&m.DodgeProgress<.6)shot="roll";
    if(shot!=null&&!captured.Contains(shot)){captured.Add(shot);Capture(shot);}
   }catch(Exception e){result.error=e.ToString();Finish("FAILED");}
  }
  static void Capture(string name)
  {
   if(Prologue.PrologueAudit.CommitRatio()>=.85f){shots.Add("NOT_CAPTURED_MEMORY_85:"+name);return;}
   var go=new GameObject("NaturalMotionCapture"){hideFlags=HideFlags.HideAndDontSave};var c=go.AddComponent<Camera>();c.CopyFrom(walker.ViewCamera);c.enabled=false;c.aspect=16f/9;
   var target=walker.Body.transform.position+Vector3.up*.9f;
   c.transform.position=target+rotation*new Vector3(.9f,.2f,2.8f);c.transform.LookAt(target);
   var dressing=Object.FindFirstObjectByType<WorldMacroDressingRenderer>();bool previous=dressing!=null&&dressing.AllowDiagnosticCameras;RenderTexture rt=null;Texture2D image=null;var active=RenderTexture.active;
   // Gameplay may switch the world model to shadows-only when the camera is
   // pulled close to a wall. Show that same live pose for this external still only.
   var renderers=appearance.WorldRenderers.Where(r=>r!=null).ToArray();var shadows=renderers.Select(r=>r.shadowCastingMode).ToArray();
   try{foreach(var r in renderers)r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;if(dressing!=null)dressing.AllowDiagnosticCameras=true;rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);image=new Texture2D(1920,1080,TextureFormat.RGB24,false);c.targetTexture=rt;c.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();string path=directory+"/"+name+".png";File.WriteAllBytes(path,image.EncodeToPNG());shots.Add(path);}
   finally{for(int i=0;i<renderers.Length;i++)if(renderers[i]!=null)renderers[i].shadowCastingMode=shadows[i];if(dressing!=null)dressing.AllowDiagnosticCameras=previous;RenderTexture.active=active;c.targetTexture=null;if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(image!=null)Object.DestroyImmediate(image);Object.DestroyImmediate(go);}
  }
  static void Finish(string status)
  {
   running=false;InputSystem.onBeforeUpdate-=Input;EditorApplication.update-=Observe;
   try{if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);if(actions!=null)actions.devices=oldDevices;if(oldKeyboard!=null)oldKeyboard.MakeCurrent();if(oldMouse!=null)oldMouse.MakeCurrent();
    if(walker!=null){walker.Body.enabled=false;walker.Body.transform.SetPositionAndRotation(position,rotation);walker.Body.enabled=true;walker.Motor.ResetMotion();}for(int i=0;enemies!=null&&i<enemies.Length;i++)if(enemies[i]!=null)enemies[i].SetActive(enemyActive[i]);Application.runInBackground=oldBackground;result.restored=true;
   }catch(Exception e){result.error+=" RESTORE: "+e;}
   result.status=status;result.frames=rows.ToArray();result.screenshots=shots.ToArray();File.WriteAllText(directory+"/live_input.json",JsonUtility.ToJson(result,true));
  }
 }
}
