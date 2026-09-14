using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroPalanquinShortcutReview
    {
        [Serializable] class Row {public string name;public bool passed;public string detail;}
        [Serializable] class Result
        { public string status,scope="Isolated Play save; virtual G input through production Update, existing APIs and bounded test teleports. No automatic walkthrough or OS keyboard injection.";public List<Row> checks=new List<Row>();public bool cleanup; }
        static string status="NOT_RUN";
        static Result report;
        static bool running,restoreBackground,holdG,visualOnly;
        static int phase,baseCalls,vehicleId,lastFrame;
        static double due,deadline;
        static WorldMacroPlaytestSession session;
        static WorldMacroPalanquinSummon caller;
        static PlaytestUiRoot ui;
        static Keyboard keyboard,previousKeyboard;
        static InputSettings.BackgroundBehavior previousBackground;
        static Vector3 oldFeet,oldCar;
        static Quaternion oldCarRotation;
        static float oldYaw,oldHp;
        static WorldMacroPalanquinProfileSO oldProfile;

        static string Begin()
        {
            if(!EditorApplication.isPlaying||running)throw new InvalidOperationException("Idle Play required");
            session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();ui=PlaytestUiRoot.Instance;
            if(session==null||!session.OpeningCommissionReceived||string.IsNullOrEmpty(session.TestSaveSuffix))throw new InvalidOperationException("Commission-completed isolated test save required");
            caller=session.GetComponent<WorldMacroPalanquinSummon>();
            if(caller==null||caller.Gesture==null||caller.Seat.Occupied||caller.Calling||caller.IsRecalled)throw new InvalidOperationException("Apply and use parked existing car");
            oldFeet=session.Walker.Body.transform.position;oldYaw=session.Walker.Body.transform.eulerAngles.y;
            oldCar=caller.Vehicle.transform.position;oldCarRotation=caller.Vehicle.transform.rotation;oldProfile=caller.Vehicle.Profile;
            oldHp=session.Walker.Motor.GetComponent<PlayerVitals>().Hp01;baseCalls=caller.SuccessfulCalls;vehicleId=caller.Vehicle.GetInstanceID();
            report=new Result();running=true;status="RUNNING";phase=0;deadline=EditorApplication.timeSinceStartup+100;
            previousKeyboard=Keyboard.current;keyboard=InputSystem.AddDevice<Keyboard>("VehicleCallReview");keyboard.MakeCurrent();
            previousBackground=InputSystem.settings.backgroundBehavior;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            restoreBackground=Application.runInBackground;Application.runInBackground=true;
            ui.CloseMenu();ui.Pause.Gate.ReleaseImmediately();
            due=EditorApplication.timeSinceStartup+.5;
            InputSystem.onBeforeUpdate+=FeedInput;
            EditorApplication.update+=Tick;return status;
        }
        static void FeedInput()
        {
            if(!running||keyboard==null||InputState.currentUpdateType!=InputUpdateType.Dynamic)return;
            keyboard.MakeCurrent();InputSystem.QueueStateEvent(keyboard,holdG?new KeyboardState(Key.G):new KeyboardState());
        }
        static void Next(int value,double delay=.1){phase=value;due=EditorApplication.timeSinceStartup+delay;status="RUNNING phase="+phase;}
        static bool ReadyInput=>!ui.Pause.IsPaused&&!ui.Pause.Gate.InputBlocked&&session.Walker.Motor.IsLocomotionGrounded;
        static void Check(string name,bool pass,string detail="")
        { report.checks.Add(new Row{name=name,passed=pass,detail=detail});if(!pass)throw new InvalidOperationException(name+": "+detail); }
        static void Invoke(string name,float dt){typeof(WorldMacroPalanquinSummon).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(caller,new object[]{dt});}
        static void Tick()
        {
            if(!running)return;
            if(!EditorApplication.isPlaying||caller==null){Finish("ABORTED_PLAY_EXIT");return;}
            if(EditorApplication.timeSinceStartup>deadline){Finish("FAIL_TIMEOUT "+phase);return;}
            if(EditorApplication.timeSinceStartup<due||lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
            try
            {
                switch(phase)
                {
                    case 0:
                        if(!ReadyInput)return;
                        holdG=true;Next(1,.12);break;
                    case 1:
                        Check("G starts real gesture",caller.Calling&&caller.Gesture.PendantActive,caller.LastResult);
                        Check("gesture blocks gameplay without pausing",ui.Pause.Gate.InputBlocked&&!ui.Pause.IsPaused&&Time.timeScale>0);
                        Check("cannot double-call while holding G",!caller.TryBeginShortcut(out _)&&caller.SuccessfulCalls==baseCalls);
                        holdG=false;Next(2,0);break;
                    case 2:
                        if(caller.GestureProgress<.43f)return;
                        Check("plaque visible in evaluated hands",caller.TemporaryPendant.gameObject.activeInHierarchy);
                        Capture("operate",true);if(visualOnly)Capture("operate-shoulder",false);Next(3,0);break;
                    case 3:
                        if(caller.Calling&&caller.GestureProgress<.78f)return;
                        if(caller.Calling)Capture("stow",true);
                        Next(4,.6);break;
                    case 4:
                        if(caller.Calling||!ReadyInput)return;
                        Check("tap commits once",caller.SuccessfulCalls==baseCalls+1,caller.LastResult);
                        Check("same vehicle and driving profile",caller.Vehicle.GetInstanceID()==vehicleId&&caller.Vehicle.Profile==oldProfile);
                        Check("prop stowed and gate released",!caller.TemporaryPendant.gameObject.activeSelf&&!caller.Gesture.PendantActive&&!ui.Pause.Gate.InputBlocked);
                        if(visualOnly){Finish("PASS_VISUAL_SEQUENCE");break;}
                        Invoke("TickRecall",10);
                        Check("no recall before a successful exit",!caller.IsRecalled&&!caller.RecallArmed);
                        Next(5,1);break;
                    case 5:
                        if(!ReadyInput)return;
                        Check("second action starts",caller.TryBeginShortcut(out var msg),msg);
                        ui.OpenPage("차패");
                        Check("menu cancels action and keeps pause",!caller.Calling&&!caller.TemporaryPendant.gameObject.activeSelf&&ui.Pause.IsPaused&&ui.Pause.Gate.InputBlocked);
                        Check("cancel before tap creates no call",caller.SuccessfulCalls==baseCalls+1);
                        ui.CloseMenu();Next(6,.25);break;
                    case 6:
                        if(!ReadyInput)return;
                        Check("damage test action starts",caller.TryBeginShortcut(out var damageReason),damageReason);
                        session.Walker.Motor.GetComponent<PlayerVitals>().TakeDamage(1);
                        Check("damage cancels action",!caller.Calling&&!caller.TemporaryPendant.gameObject.activeSelf);
                        Next(7,.3);break;
                    case 7:
                        if(!ReadyInput)return;
                        bool placed=false;
                        foreach(var socket in caller.Seat.ExitSockets)
                        {
                            if(socket==null||!session.TrySafeFeet(socket.position,out var feet))continue;
                            session.Teleport(feet,caller.Vehicle.transform.eulerAngles.y);Physics.SyncTransforms();
                            if(caller.Seat.CanBoard){placed=true;break;}
                        }
                        Check("safe door approach",placed);Next(8,.4);break;
                    case 8:
                        if(!ReadyInput)return;
                        Check("board existing seat",caller.Seat.TryBoard(),caller.Seat.LastInteraction);
                        Check("occupied G rejected",!caller.TryBeginShortcut(out _)&&!caller.Calling);
                        Invoke("TickRecall",100);
                        Check("occupied vehicle never recalled",!caller.IsRecalled&&caller.Vehicle.gameObject.activeSelf);
                        Next(9,.25);break;
                    case 9:
                        Check("successful exit arms recall",caller.Seat.TryExit()&&caller.RecallArmed,caller.Seat.LastInteraction);
                        Invoke("TickRecall",3);
                        Check("nearby parked car stays",!caller.IsRecalled);
                        bool moved=false;
                        for(int i=0;i<12;i++)
                        {
                            float a=i*Mathf.PI/6;var p=caller.Vehicle.transform.position+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*40;
                            if(!session.TrySafeFeet(p,out var feet))continue;
                            session.Teleport(feet,oldYaw);moved=true;break;
                        }
                        Check("safe distant diagnostic point",moved);Next(10,.3);break;
                    case 10:
                        if(!ReadyInput)return;
                        Invoke("TickRecall",3);
                        Check("distant car recalled",caller.IsRecalled&&!caller.Vehicle.gameObject.activeInHierarchy);
                        Check("hidden instance retained",Object.FindObjectsByType<WorldMacroPalanquinController>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==1);
                        Check("no active recalled colliders",caller.Vehicle.GetComponentsInChildren<Collider>(true).All(c=>!c.gameObject.activeInHierarchy));
                        session.Teleport(oldFeet,oldYaw);Next(11,.6);break;
                    case 11:
                        if(!ReadyInput)return;
                        Check("recalled car can be called again",caller.TryBeginShortcut(out var recallReason),recallReason);Next(12,0);break;
                    case 12:
                        if(caller.GestureProgress<.46f)return;
                        Capture("operate-shoulder",false);Next(13,1.5);break;
                    case 13:
                        if(caller.Calling||!ReadyInput)return;
                        Check("same car restored after recall",!caller.IsRecalled&&caller.Vehicle.gameObject.activeInHierarchy&&caller.SuccessfulCalls==baseCalls+2&&caller.Vehicle.GetInstanceID()==vehicleId);
                        Check("newly summoned car not immediately recalled",!caller.RecallArmed);
                        Capture("ready",false);
                        Finish("PASS");break;
                }
            }
            catch(Exception e){report.checks.Add(new Row{name="Exception",passed=false,detail=e.ToString()});Finish("FAIL phase="+phase);}
        }
        static void Capture(string name,bool external)
        {
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Capture commit >=85%");
            var go=new GameObject("VehicleCallReviewCamera");var camera=go.AddComponent<Camera>();camera.CopyFrom(session.Walker.ViewCamera);
            camera.enabled=false;var body=session.Walker.Body.transform;
            if(external)
            {
                var focus=body.position+Vector3.up*1.20f;
                camera.transform.position=body.position+body.forward*2.35f+body.right*1.65f+Vector3.up*1.68f;
                camera.transform.LookAt(focus);camera.fieldOfView=42;
            }
            else camera.transform.SetPositionAndRotation(session.Walker.ViewCamera.transform.position,session.Walker.ViewCamera.transform.rotation);
            var dressing=Object.FindFirstObjectByType<Oheangbu.App.World.Dressing.WorldMacroDressingRenderer>();
            bool oldDiagnostic=dressing!=null&&dressing.AllowDiagnosticCameras;
            if(dressing!=null)dressing.AllowDiagnosticCameras=true;
            var rt=RenderTexture.GetTemporary(1920,1080,24,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;Texture2D image=null;
            try
            {
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                image=new Texture2D(1920,1080,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();
                File.WriteAllBytes(Output+"/"+name+".png",image.EncodeToPNG());
                File.WriteAllText(Output+"/"+name+".txt","Play snapshot; external="+external+"; gesture="+caller.GestureProgress+"; feet="+body.position+"; calls="+caller.SuccessfulCalls+"; left="+caller.Gesture.PendantLeftWrist+"; right="+caller.Gesture.PendantRightWrist);
            }
            finally{if(dressing!=null)dressing.AllowDiagnosticCameras=oldDiagnostic;camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);if(image!=null)Object.DestroyImmediate(image);Object.DestroyImmediate(go);}
        }
        static void Finish(string outcome)
        {
            if(!running)return;running=false;holdG=false;InputSystem.onBeforeUpdate-=FeedInput;EditorApplication.update-=Tick;
            try
            {
                caller.CancelCall();if(caller.Seat.Occupied)caller.Seat.TryExit();
                caller.Vehicle.gameObject.SetActive(true);caller.Vehicle.StopDriverInputForUi();
                caller.Vehicle.Body.linearVelocity=Vector3.zero;caller.Vehicle.Body.angularVelocity=Vector3.zero;
                caller.Vehicle.Body.position=oldCar;caller.Vehicle.Body.rotation=oldCarRotation;caller.Vehicle.transform.SetPositionAndRotation(oldCar,oldCarRotation);
                session.Teleport(oldFeet,oldYaw);session.Walker.Motor.GetComponent<PlayerVitals>().Restore(oldHp);
                ui.CloseMenu();ui.Pause.Gate.ReleaseWhenNeutral();
                if(keyboard!=null){InputSystem.RemoveDevice(keyboard);keyboard=null;}previousKeyboard?.MakeCurrent();
                InputSystem.settings.backgroundBehavior=previousBackground;Application.runInBackground=restoreBackground;
                report.cleanup=true;
            }
            catch(Exception e){report.checks.Add(new Row{name="cleanup",passed=false,detail=e.ToString()});}
            status=report.status=outcome;File.WriteAllText(Output+(visualOnly?"/visual_checks.json":"/runtime_checks.json"),JsonUtility.ToJson(report,true));
        }
    }
}
