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
 [InitializeOnLoad] public static class PlaytestDodgeTurnReview
 {
  [Serializable] public class Row { public string phase,state; public float time,yaw,body,look,height,progress,timeScale;public bool rolling,dodging,crouching,grounded,turning,blocked;public int turnSerial;public Vector3 position,leftFoot,rightFoot; }
  [Serializable] public class Result {public string status,error,scope="Virtual keyboard/mouse injected through real InputSystem and live scene frames; local tests, no native-user or full-route evidence.";public bool restored;public Row[] frames;public string[] screenshots;}
  static WorldMacroCombatWalker walker;static WorldMacroPlayerAppearance appearance;static InputActionAsset actions;
  static ReadOnlyArray<InputDevice>? oldDevices;static Keyboard keyboard,oldKeyboard;static Mouse mouse,oldMouse;
  static bool running,oldBackground;static double start;static Vector3 position;static Quaternion rotation;static float sensitivity;static int previous=-1;
  static readonly List<Row> rows=new List<Row>();static readonly List<string> shots=new List<string>();static readonly HashSet<string> captured=new HashSet<string>();
  static string directory;static Result result;
  static readonly string[] phases={"settle","look45","hold45","look89","hold89","look100","turn_right","hold100","look_left","turn_left","idle","dodge_forward","dodge_back","dodge_left","dodge_right","crouch","roll_forward","crouch_hold","roll_right","crouch_return","crouch_look","crouch_turn","stand","pause_open","pause_inputs","pause_close","after_menu"};
  const float Seconds=1.6f;
  static PlaytestDodgeTurnReview(){AssemblyReloadEvents.beforeAssemblyReload+=()=>{if(running)Finish("INTERRUPTED");};EditorApplication.playModeStateChanged+=s=>{if(running&&s==PlayModeStateChange.ExitingPlayMode)Finish("INTERRUPTED");};}
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
   directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestRecovery/DodgeTurn/Live",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));Directory.CreateDirectory(directory);
   rows.Clear();shots.Clear();captured.Clear();previous=-1;result=new Result{status="RUNNING"};
   keyboard=InputSystem.AddDevice<Keyboard>("DodgeTurnVirtualKeyboard");mouse=InputSystem.AddDevice<Mouse>("DodgeTurnVirtualMouse");actions.devices=new InputDevice[]{keyboard,mouse};
   running=true;Application.runInBackground=true;start=EditorApplication.timeSinceStartup;InputSystem.onBeforeUpdate+=Input;EditorApplication.update+=Observe;return "RUNNING local input exercise";
  }
  static void Input()
  {
   if(!running||InputState.currentUpdateType!=InputUpdateType.Dynamic)return;
   int p=(int)((EditorApplication.timeSinceStartup-start)/Seconds);if(p>=phases.Length)return;
   bool edge=p!=previous;previous=p;Key[] keys=Array.Empty<Key>();float dx=0;
   if(edge)
   {
    if(p==1)dx=45;else if(p==3)dx=44;else if(p==5)dx=11;else if(p==8)dx=-120;else if(p==20)dx=200;
    if(p==11)keys=new[]{Key.W,Key.LeftShift};if(p==12)keys=new[]{Key.S,Key.LeftShift};if(p==13)keys=new[]{Key.A,Key.LeftShift};if(p==14)keys=new[]{Key.D,Key.LeftShift};
    if(p==15||p==22)keys=new[]{Key.C};if(p==16)keys=new[]{Key.W,Key.LeftShift};if(p==18)keys=new[]{Key.D,Key.LeftShift};
    if(p==23||p==25)keys=new[]{Key.Escape};
   }
   if(p==24)keys=new[]{Key.W,Key.LeftShift,Key.C};
   InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(Screen.width*.5f,Screen.height*.5f),delta=new Vector2(dx/Mathf.Max(.001f,sensitivity),0)});
  }
  static void Observe()
  {
   if(!running)return;try{
    double t=EditorApplication.timeSinceStartup-start;int p=(int)(t/Seconds);if(p>=phases.Length){Finish("RECORDED_REQUIRES_ASSESSMENT");return;}
    var m=walker.Motor;var a=appearance.Animator;var info=a.GetCurrentAnimatorStateInfo(0);
    string state=new[]{"Locomotion","TurnLeft","TurnRight","Crouch","CrouchTurnLeft","CrouchTurnRight","Dodge","CrouchRoll"}.FirstOrDefault(s=>info.IsName(s))??info.shortNameHash.ToString();
    rows.Add(new Row{phase=phases[p],state=state,time=(float)t,timeScale=Time.timeScale,blocked=m.RuntimeState.InputBlocked,yaw=walker.Body.transform.eulerAngles.y,body=appearance.BodyYaw,look=appearance.LookYaw,turning=appearance.TurningInPlace,turnSerial=appearance.TurnSerial,height=walker.Body.height,rolling=m.IsRolling,dodging=m.IsDodging,crouching=m.IsCrouching,grounded=m.IsLocomotionGrounded,progress=m.DodgeProgress,position=walker.Body.transform.position,leftFoot=a.GetBoneTransform(HumanBodyBones.LeftFoot).position,rightFoot=a.GetBoneTransform(HumanBodyBones.RightFoot).position});
    bool pose=m.IsDodging&&m.DodgeProgress>.28f&&m.DodgeProgress<.7f;
    bool look=(p==2||p==4||p==5||p==8||p==20)&&t%Seconds>.25;
    if((pose||look)&&!captured.Contains(phases[p])){captured.Add(phases[p]);Capture(phases[p]);}
   }catch(Exception e){result.error=e.ToString();Finish("FAILED");}
  }
  static void Capture(string name)
  {
   if(Prologue.PrologueAudit.CommitRatio()>=.85f){shots.Add("NOT_CAPTURED_MEMORY_85:"+name);return;}
   var go=new GameObject("DodgeTurnCapture"){hideFlags=HideFlags.HideAndDontSave};var c=go.AddComponent<Camera>();c.CopyFrom(walker.ViewCamera);c.enabled=false;c.aspect=16f/9;
   var target=walker.Body.transform.position+Vector3.up*.9f;
   c.transform.position=target+rotation*new Vector3(2.8f,.35f,3.4f);c.transform.LookAt(target);
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
    if(walker!=null){walker.Body.enabled=false;walker.Body.transform.SetPositionAndRotation(position,rotation);walker.Body.enabled=true;walker.Motor.ResetMotion();}Application.runInBackground=oldBackground;result.restored=true;
   }catch(Exception e){result.error+=" RESTORE: "+e;}
   result.status=status;result.frames=rows.ToArray();result.screenshots=shots.ToArray();File.WriteAllText(directory+"/live_input.json",JsonUtility.ToJson(result,true));
  }
 }
}
