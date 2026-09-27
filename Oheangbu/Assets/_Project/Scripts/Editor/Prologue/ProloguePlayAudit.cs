using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.Prologue
{
 [InitializeOnLoad] public static class ProloguePlayAudit
 {
  static AsyncOperation cleanup;
  static ProloguePlayAudit(){EditorApplication.update+=Tick;}
  public static string Begin(){if(EditorApplication.isPlaying)throw new Exception("Exit Play first");var s=Object.FindFirstObjectByType<PrologueSession>();s.TestSaveSuffix="-audit-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss");SessionState.SetBool("PrologueWalkPending",true);EditorApplication.isPlaying=true;return "Starting isolated save/input walk; gameplay controls and enemy AI remain active.";}
  public static string Combat(){SessionState.SetBool("PrologueCombat",true);return Begin();}
  static void Tick(){if(!SessionState.GetBool("PrologueWalkPending",false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;var s=Object.FindFirstObjectByType<PrologueSession>();if(s==null||s.Progress==null)return;if(cleanup==null){cleanup=Resources.UnloadUnusedAssets();return;}if(!cleanup.isDone)return;GC.Collect();cleanup=null;SessionState.SetBool("PrologueWalkPending",false);bool combat=SessionState.GetBool("PrologueCombat",false);SessionState.SetBool("PrologueCombat",false);var driver=new GameObject("PrologueInputAudit").AddComponent<PrologueWalkDriver>();driver.Begin(s,combat);}
  public static string Poll()=>File.Exists(PrologueBuilder.Output+"/walk_status.json")?File.ReadAllText(PrologueBuilder.Output+"/walk_status.json"):"No run";
  public static string State()
  {
   var s=Object.FindFirstObjectByType<PrologueSession>();if(s==null)return "No session";
   return "player="+s.Player.position+" progress="+JsonUtility.ToJson(s.Progress)+" agents="+string.Join(";",s.Encounters.Select(e=>e.Id+" "+e.Current+" "+e.transform.position+" nav="+e.GetComponent<NavMeshAgent>().isOnNavMesh));
  }
  // Diagnostic calls are deliberately separate from actual input walk evidence.
  public static string StateTests()
  {
   if(!EditorApplication.isPlaying)throw new Exception("Play required");var s=Object.FindFirstObjectByType<PrologueSession>();if(!s.TestSaveSuffix.StartsWith("-audit"))throw new Exception("Isolated test save required");
   var list=new List<string>();Action<bool,string> check=(b,m)=>list.Add((b?"PASS ":"FAIL ")+m);
   s.Teleport(s.Content.Points.First(x=>x.Id=="WorkerSatchel").Position+Vector3.up,0);s.Interact("WorkerSatchel");int balance=s.Progress.currency;s.Interact("WorkerSatchel");check(balance>=24&&s.Progress.currency==balance,"runtime reward once");
   s.Player.GetComponent<PlayerVitals>().ApplyFatalFall();check(s.Progress.currency==0&&s.Progress.dropCurrency==balance,"runtime death drop");check(Vector3.Distance(s.Player.position,s.Progress.checkpointPosition)<.01f&&s.Player.GetComponent<PlayerVitals>().Hp01==1,"respawn position and HP");
   s.Teleport(s.Progress.dropPosition,0);s.Interact("CurrencyDrop");check(s.Progress.currency==balance&&s.Progress.dropCurrency==0,"runtime retrieve");
   s.Player.GetComponent<PlayerVitals>().ApplyFatalFall();s.Player.GetComponent<PlayerVitals>().ApplyFatalFall();check(s.Progress.dropCurrency==0,"runtime second death invalidates old drop");
   s.Encounters[0].GetComponent<EnemyVitals>().TakeDamage(10000);check(!s.Encounters[0].GetComponent<Collider>().enabled,"dead enemy collision removed");
   s.Teleport(s.Content.Points.First(x=>x.Id=="InnRest").Position+Vector3.up,0);s.Interact("InnRest");
   check(s.Progress.checkpoint=="InnRest"&&s.Progress.completed.Contains("WorkerSatchel"),"rest checkpoint retains one-offs");check(s.Encounters.All(e=>e.GetComponent<EnemyVitals>().IsAlive&&e.GetComponent<Collider>().enabled),"rest ordinary enemies reset");
   s.Save();var reload=new PrologueProgressStore(Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+s.TestSaveSuffix+".json")).Load();check(reload.checkpoint=="InnRest"&&reload.completed.Contains("WorkerSatchel"),"runtime disk reload");
   list.Add("DIAGNOSTIC: direct damage/interaction/teleport. Not an input combat or application restart test.");string result=string.Join("\n",list);File.WriteAllText(PrologueBuilder.Output+"/technical_runtime.txt",result);return result;
  }
 }
 [DefaultExecutionOrder(-30000)] public sealed class PrologueWalkDriver:MonoBehaviour
 {
  [Serializable]class Report{public string status="RUNNING",scope="Virtual WASD/mouse/F -> unchanged PlayerMotor/CharacterController, live enemy AI; no transform movement or synthetic damage",issue="",saveSlot,video="NOT_CAPTURED";public int waypoint,total,damageEvents,deaths,frames;public float seconds,meanFrameMs,p95FrameMs;public Vector3 position;public List<string> interactions=new List<string>();}
  struct Waypoint{public Vector3 p;public string id;public Waypoint(Vector3 p,string id=null){this.p=p;this.id=id;}}
  PrologueSession session;PlayerMotor motor;InputActionAsset actions;Keyboard keyboard,oldKeyboard;Mouse mouse,oldMouse;
  InputDevice[] assetDevices;readonly Dictionary<InputActionMap,InputDevice[]> filters=new Dictionary<InputActionMap,InputDevice[]>();
  InputSettings.BackgroundBehavior background;InputSettings.EditorInputBehaviorInPlayMode editorInput;bool oldBackground,ready;float started,advanced,pauseUntil;int index;bool tapped;
  readonly List<Waypoint> route=new List<Waypoint>();readonly List<float> times=new List<float>();Report report;
  Camera capture;RenderTexture rt;Texture2D texture;byte[] frameBuffer;System.Diagnostics.Process encoder;float nextCapture;int videoFrames;
  static T Read<T>(object o,string name)=>(T)o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);
  bool combatMode;
  bool journeyMode;
  string WalkOutput=>journeyMode?Path.GetFullPath("../Art/World/PineRest/journey_walk.json"):PrologueBuilder.Output+(combatMode?"/combat_approach.json":"/walk_status.json");
  public void Begin(PrologueSession s,bool combat=false)
  {
   combatMode=combat;journeyMode=s.gameObject.scene.path==PineRestGameBuilder.Scene;
   session=s;motor=s.Player.GetComponent<PlayerMotor>();actions=Read<InputActionAsset>(motor,"_actions");report=new Report{saveSlot=s.Content.SaveSlot+s.TestSaveSuffix};
   oldKeyboard=Keyboard.current;oldMouse=Mouse.current;assetDevices=actions.devices?.ToArray();
   foreach(var m in actions.actionMaps){var raw=typeof(InputActionMap).GetField("m_Devices",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(m);var value=raw.GetType().GetMethod("Get").Invoke(raw,null);filters[m]=value==null?null:((ReadOnlyArray<InputDevice>)value).ToArray();}
   background=InputSystem.settings.backgroundBehavior;editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;oldBackground=Application.runInBackground;
   InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;Application.runInBackground=true;
   keyboard=InputSystem.AddDevice<Keyboard>("PrologueAuditKeyboard");mouse=InputSystem.AddDevice<Mouse>("PrologueAuditMouse");var devices=new InputDevice[]{keyboard,mouse};actions.devices=devices;foreach(var m in actions.actionMaps)m.devices=devices;
   if(journeyMode){
    route.Add(new Waypoint(s.Content.Points.First(p=>p.Id=="MineStart").Position,"MineStart"));
    route.Add(new Waypoint(s.Content.Points.First(p=>p.Id=="BurntCord").Position,"BurntCord"));
    foreach(var p in s.Content.MainPath.Skip(2))route.Add(new Waypoint(p));
    route.Add(new Waypoint(s.Content.Points.First(p=>p.Id=="InnRest").Position,"InnRest"));
   }else{
   route.Add(new Waypoint(new Vector3(-1,0,-27),"MineStart"));route.Add(new Waypoint(new Vector3(1,0,-14),"BlastEvidence"));route.Add(new Waypoint(new Vector3(1.4f,0,-8)));route.Add(new Waypoint(new Vector3(0,0,-1)));
   // Main trail plus the reward loop, then rejoin. Target points only steer input.
   var main=s.Content.MainPath;int branchStart=Array.FindIndex(main,p=>p.z>84&&p.x>90),branchEnd=Array.FindIndex(main,p=>p.z>154&&p.x>75);
   for(int i=0;i<main.Length;i+=3){if(i>=branchStart&&i<branchEnd){route.AddRange(s.Content.BranchPath.Where((p,k)=>k%3==0).Select(p=>new Waypoint(p)));route.Add(new Waypoint(s.Content.Points.First(p=>p.Id=="WorkerSatchel").Position,"WorkerSatchel"));i=branchEnd;}if(i<main.Length)route.Add(new Waypoint(main[i]));}
   route.Add(new Waypoint(new Vector3(-71,0,47),"InnRest"));route.Add(new Waypoint(new Vector3(-77,0,47),"Logger"));route.Add(new Waypoint(new Vector3(-65,0,46),"Herbalist"));
   }
   if(combatMode){route.Clear();route.Add(new Waypoint(new Vector3(0,0,-18)));}
   report.total=route.Count;started=advanced=Time.unscaledTime;ready=true;s.Feedback+=Feedback;s.Player.GetComponent<PlayerVitals>().Damaged+=Damaged;s.Player.GetComponent<PlayerVitals>().Died+=Died;
   if(!journeyMode)StartCapture();Write();
  }
  void Feedback(string text){report.interactions.Add(text);}
  void Damaged(float n){report.damageEvents++;}void Died(){report.deaths++;}
  void Update()
  {
   if(!ready)return;
   try{
    report.position=session.Player.position;report.seconds=Time.unscaledTime-started;report.waypoint=index;times.Add(Time.unscaledDeltaTime*1000);
    if(index>=route.Count){Finish("COMPLETE","");return;}if(Time.unscaledTime-advanced>18){Finish("FAILED","Stalled at "+index+" target="+route[index].p+" actual="+session.Player.position);return;}
    var target=route[index];Vector3 delta=target.p-session.Player.position;delta.y=0;float angle=Mathf.DeltaAngle(session.Player.eulerAngles.y,Quaternion.LookRotation(delta.sqrMagnitude>.01f?delta:Vector3.forward).eulerAngles.y);
    var keys=new List<Key>();Vector2 look=new Vector2(Mathf.Clamp(angle,-6,6)/Read<CombatConfigSO>(motor,"_config").LookSensitivity,0);
    if(delta.magnitude<1.15f){look=Vector2.zero;if(target.id!=null&&!tapped){keys.Add(Key.F);tapped=true;pauseUntil=Time.unscaledTime+.25f;}else if(target.id==null||Time.unscaledTime>pauseUntil){index++;advanced=Time.unscaledTime;tapped=false;}}
    else if(Mathf.Abs(angle)<30)keys.Add(Key.W);
    InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys.ToArray()));InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(Screen.width*.5f,Screen.height*.5f),delta=look});InputSystem.Update();keyboard.MakeCurrent();mouse.MakeCurrent();
    if(Time.frameCount%60==0)Write();
   }catch(Exception e){Finish("FAILED",e.ToString());}
  }
  void LateUpdate()
  {
   if(!ready||encoder==null||Time.unscaledTime<nextCapture)return;nextCapture=Time.unscaledTime+1/12f;
   if(PrologueAudit.CommitRatio()>=.85f){report.video="STOPPED at commit >=85%; partial clip";StopCapture();return;}
   var target=capture.targetTexture;var prior=RenderTexture.active;
   try{capture.targetTexture=rt;capture.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1920,1080),0,0);texture.GetRawTextureData<byte>().CopyTo(frameBuffer);encoder.StandardInput.BaseStream.Write(frameBuffer,0,frameBuffer.Length);videoFrames++;}
   catch(Exception e){report.video="FAILED: "+e.Message;StopCapture();}
   finally{if(capture!=null)capture.targetTexture=target;RenderTexture.active=prior;}
  }
  void StartCapture()
  {
   if(PrologueAudit.CommitRatio()>=.85f){report.video="NOT_CAPTURED: commit >=85%";return;}
   string ff="C:/Users/yj666/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/Lib/site-packages/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe";if(!File.Exists(ff))return;
   capture=Camera.main;rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);texture=new Texture2D(1920,1080,TextureFormat.RGB24,false);frameBuffer=new byte[1920*1080*3];
   encoder=new System.Diagnostics.Process{StartInfo=new System.Diagnostics.ProcessStartInfo(ff,"-y -loglevel error -f rawvideo -pixel_format rgb24 -video_size 1920x1080 -framerate 12 -i pipe:0 -vf vflip -c:v libx264 -preset ultrafast -crf 23 -pix_fmt yuv420p \""+PrologueBuilder.Output+(combatMode?"/combat_approach.mp4":"/input_walk.mp4")+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true}};encoder.Start();report.video="1080p actual main camera rendering, 12fps sample; UI excluded; timing may accelerate if render stalls";
  }
  void StopCapture(){if(encoder!=null){encoder.StandardInput.Close();if(!encoder.WaitForExit(5000))encoder.Kill();encoder.Dispose();encoder=null;}if(rt!=null){rt.Release();Object.Destroy(rt);}if(texture!=null)Object.Destroy(texture);frameBuffer=null;}
  void Finish(string status,string issue){report.status=status;report.issue=issue;report.frames=videoFrames;if(times.Count>0){report.meanFrameMs=times.Average();times.Sort();report.p95FrameMs=times[(int)((times.Count-1)*.95f)];}Write();Cleanup();if(combatMode&&status=="COMPLETE")File.WriteAllText(PrologueBuilder.Output+"/combat_input_start.txt",Vfx120PlayerInputAudit.StartPrologue());}
  void Write()=>File.WriteAllText(WalkOutput,JsonUtility.ToJson(report,true));
  void OnDestroy(){if(ready)Finish("INTERRUPTED","Play stopped");}
  void Cleanup(){ready=false;StopCapture();session.Feedback-=Feedback;session.Player.GetComponent<PlayerVitals>().Damaged-=Damaged;session.Player.GetComponent<PlayerVitals>().Died-=Died;actions.devices=assetDevices==null?(ReadOnlyArray<InputDevice>?)null:new ReadOnlyArray<InputDevice>(assetDevices);foreach(var pair in filters)pair.Key.devices=pair.Value==null?(ReadOnlyArray<InputDevice>?)null:new ReadOnlyArray<InputDevice>(pair.Value);if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);oldKeyboard?.MakeCurrent();oldMouse?.MakeCurrent();InputSystem.settings.backgroundBehavior=background;InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;Application.runInBackground=oldBackground;}
 }
}
