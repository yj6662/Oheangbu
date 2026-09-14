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
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 [InitializeOnLoad] public static class PlaytestRecoveryLiveInput
 {
  [Serializable] public class Row {public string phase,state;public float seconds,speed,crouch,height;public bool airborne,sitting,drawing,grounded;public Vector3 position;}
  [Serializable] public class Report {public string status,error,scope="Injected virtual keyboard/mouse through actual InputSystem Dynamic updates and normal scene PlayerMotor/Animator frames. Short local exercise, no route completion, native human input or video.";public bool restored;public Row[] frames;public string[] screenshots;}
  static WorldMacroCombatWalker walker;static Animator animator;static InputActionAsset actions;
  static ReadOnlyArray<InputDevice>? oldDevices;static Keyboard keyboard,oldKeyboard;static Mouse mouse,oldMouse;
  static bool running,oldBackground;static double start;static Vector3 position;static Quaternion rotation;static int lastPhase=-1;
  static readonly List<Row> rows=new List<Row>(4000);static readonly List<string> shots=new List<string>();static readonly HashSet<int> captured=new HashSet<int>();
  static string directory;static Report report;
  static readonly string[] phases={"settle","walk_forward","walk_back","walk_left","walk_right","diagonal","run","stop","crouch_toggle","crouch_forward","crouch_draw","crouch_harvest","crouch_stand","jump","land","sit","sit_hold","rise"};
  static PlaytestRecoveryLiveInput(){AssemblyReloadEvents.beforeAssemblyReload+=()=>{if(running)Finish("INTERRUPTED");};EditorApplication.playModeStateChanged+=s=>{if(running&&s==PlayModeStateChange.ExitingPlayMode)Finish("INTERRUPTED");};}
  public static string Execute(string command)
  {
   if(command=="abort"){if(running)Finish("ABORTED");return "Stopped";}
   if(running||!Application.isPlaying||Time.timeScale<.99f)throw new InvalidOperationException("Idle unpaused Play required");
   walker=Object.FindFirstObjectByType<WorldMacroCombatWalker>();var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
   if(walker==null||session==null||string.IsNullOrEmpty(session.TestSaveSuffix)||walker.Motor.RuntimeState.InputBlocked)throw new InvalidOperationException("Isolated test slot and active gameplay input required");
   actions=(InputActionAsset)typeof(Oheangbu.Combat.PlayerMotor).GetField("_actions",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(walker.Motor);
   animator=Object.FindFirstObjectByType<WorldMacroPlayerAppearance>().Animator;
   oldDevices=actions.devices;oldKeyboard=Keyboard.current;oldMouse=Mouse.current;oldBackground=Application.runInBackground;
   position=walker.Body.transform.position;rotation=walker.Body.transform.rotation;
   directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestRecovery/LiveInput",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));Directory.CreateDirectory(directory);
   report=new Report{status="RUNNING"};rows.Clear();shots.Clear();captured.Clear();lastPhase=-1;
   try{keyboard=InputSystem.AddDevice<Keyboard>("RecoveryVirtualKeyboard");mouse=InputSystem.AddDevice<Mouse>("RecoveryVirtualMouse");actions.devices=new InputDevice[]{keyboard,mouse};
    running=true;Application.runInBackground=true;start=EditorApplication.timeSinceStartup;InputSystem.onBeforeUpdate+=Input;EditorApplication.update+=Observe;return "RUNNING 27-second local real-Play input exercise";
   }catch(Exception e){report.error=e.ToString();Finish("FAILED");throw;}
  }
  static void Input()
  {
   if(!running||InputState.currentUpdateType!=InputUpdateType.Dynamic)return;
   double t=EditorApplication.timeSinceStartup-start;int phase=(int)(t/1.5);if(phase>=phases.Length)return;
   bool edge=phase!=lastPhase;lastPhase=phase;Key[] keys=Array.Empty<Key>();bool harvest=false;
   switch(phase){case 1:keys=new[]{Key.W};break;case 2:keys=new[]{Key.S};break;case 3:keys=new[]{Key.A};break;case 4:keys=new[]{Key.D};break;case 5:keys=new[]{Key.W,Key.D};break;case 6:keys=new[]{Key.W,Key.LeftCtrl};break;case 8:if(edge)keys=new[]{Key.C};break;case 12:if(t%1.5>.25&&t%1.5<.4)keys=new[]{Key.C};break;case 9:keys=new[]{Key.W};break;case 10:keys=new[]{Key.Q};break;case 11:harvest=true;break;case 13:if(edge)keys=new[]{Key.Space};break;case 15:case 17:if(edge)keys=new[]{Key.X};break;}
   InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));var state=new MouseState{position=new Vector2(Screen.width*.5f,Screen.height*.5f)};state.WithButton(MouseButton.Left,harvest);InputSystem.QueueStateEvent(mouse,state);
  }
  static void Observe()
  {
   if(!running)return;try{
    double t=EditorApplication.timeSinceStartup-start;int p=(int)(t/1.5);if(p>=phases.Length){Finish("RECORDED_REQUIRES_ASSESSMENT");return;}
    var m=walker.Motor;var s=animator.GetCurrentAnimatorStateInfo(0);
    rows.Add(new Row{phase=phases[p],seconds=(float)t,speed=new Vector2(m.ActualLocalVelocity.x,m.ActualLocalVelocity.z).magnitude,crouch=m.Crouch01,height=walker.Body.height,airborne=m.IsAirborne,grounded=m.IsLocomotionGrounded,sitting=m.IsSitting,drawing=m.IsDrawing,position=walker.Body.transform.position,state=s.shortNameHash.ToString()});
    if(t%1.5>.9&&!captured.Contains(p)&&(p==1||p==6||p==9||p==10||p==16)){captured.Add(p);Capture(phases[p],p!=10);}
   }catch(Exception e){report.error=e.ToString();Finish("FAILED");}
  }
  static void Capture(string name,bool external)
  {
   if(Prologue.PrologueAudit.CommitRatio()>=.85f){shots.Add("NOT_CAPTURED_MEMORY_85:"+name);return;}
   var go=new GameObject("RecoveryPoseCapture"){hideFlags=HideFlags.HideAndDontSave};var c=go.AddComponent<Camera>();c.CopyFrom(walker.ViewCamera);c.enabled=false;c.aspect=16f/9;
   c.transform.SetPositionAndRotation(walker.ViewCamera.transform.position,walker.ViewCamera.transform.rotation);
   if(external){var target=walker.Body.transform.position+Vector3.up*.9f;c.transform.position=target+walker.Body.transform.forward*3.5f+walker.Body.transform.right*1.8f+Vector3.up*.35f;c.transform.LookAt(target);}
   var dressing=Object.FindFirstObjectByType<WorldMacroDressingRenderer>();bool previous=dressing!=null&&dressing.AllowDiagnosticCameras;RenderTexture rt=null;Texture2D image=null;var active=RenderTexture.active;
   try{if(dressing!=null)dressing.AllowDiagnosticCameras=true;rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);image=new Texture2D(1920,1080,TextureFormat.RGB24,false);c.targetTexture=rt;c.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();string path=directory+"/"+name+".png";File.WriteAllBytes(path,image.EncodeToPNG());shots.Add(path);}
   finally{if(dressing!=null)dressing.AllowDiagnosticCameras=previous;RenderTexture.active=active;c.targetTexture=null;if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(image!=null)Object.DestroyImmediate(image);Object.DestroyImmediate(go);}
  }
  static void Finish(string status)
  {
   running=false;InputSystem.onBeforeUpdate-=Input;EditorApplication.update-=Observe;
   try{if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);if(actions!=null)actions.devices=oldDevices;if(oldKeyboard!=null)oldKeyboard.MakeCurrent();if(oldMouse!=null)oldMouse.MakeCurrent();
    if(walker!=null){walker.Body.enabled=false;walker.Body.transform.SetPositionAndRotation(position,rotation);walker.Body.enabled=true;walker.Motor.ResetMotion();}Application.runInBackground=oldBackground;if(report!=null)report.restored=true;
   }catch(Exception e){if(report!=null)report.error+=" RESTORE: "+e;}
   if(report==null)return;report.status=status;report.frames=rows.ToArray();report.screenshots=shots.ToArray();File.WriteAllText(directory+"/live_input.json",JsonUtility.ToJson(report,true));
  }
 }
}
