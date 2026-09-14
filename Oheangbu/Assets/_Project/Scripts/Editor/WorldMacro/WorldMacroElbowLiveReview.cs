using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Drawing;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    [InitializeOnLoad] public static class WorldMacroElbowLiveReview
    {
        [Serializable] class Row
        {
            public float time;public int frame;public Vector2 pointer;
            public Vector3 elbow;public Quaternion hand;
            public WorldMacroPlayerGestureRig.GestureDiagnostics pose;
        }
        [Serializable] class Report
        {
            public string status,mode,error,scope="Virtual Q + left mouse through production Dynamic InputSystem. Fixed player, continuous loop and diagonal stroke, cancelled before release: no spell committed, no walking or video.";
            public int commits;public bool restored;public Row[] frames;
        }
        static bool running,oldStable,oldBackground;static double start;static string path,status="idle";
        static WorldMacroPlayerGestureRig rig;static DrawingInputController drawing;static PlaytestUiRoot ui;
        static Transform elbow,hand;static InputActionAsset actions;static ReadOnlyArray<InputDevice>? oldDevices;
        static Keyboard keyboard,oldKeyboard;static Mouse mouse,oldMouse;
        static InputSettings.BackgroundBehavior background;
        static Report report;static List<Row> rows=new List<Row>();static int lastFrame;
        static T Field<T>(object o,string n)=>(T)o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);
        static WorldMacroElbowLiveReview()
        {
            AssemblyReloadEvents.beforeAssemblyReload+=()=>{if(running)Finish("INTERRUPTED");};
            EditorApplication.playModeStateChanged+=s=>{if(running&&s==PlayModeStateChange.ExitingPlayMode)Finish("INTERRUPTED");};
        }
        public static string Execute(string command)
        {
            if(command=="poll")return status;
            if(command=="abort"){if(running)Finish("ABORTED");return status;}
            if(running||!Application.isPlaying)throw new InvalidOperationException("Idle Play required");
            var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if(session==null||string.IsNullOrEmpty(session.TestSaveSuffix))throw new InvalidOperationException("Isolated slot required");
            rig=Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();ui=PlaytestUiRoot.Instance;
            drawing=Field<DrawingInputController>(rig,"_drawing");
            actions=Field<InputActionAsset>(session.Walker.Motor,"_actions");
            var near=Field<Transform>(rig,"_nearRoot");
            foreach(var t in near.GetComponentsInChildren<Transform>(true)){if(t.name==rig.Profile.RightForearmName)elbow=t;if(t.name==rig.Profile.RightHandName)hand=t;}
            ui.CloseMenu();ui.Pause.Gate.ReleaseImmediately();
            oldStable=rig.Profile.StableDrawingElbow;rig.Profile.StableDrawingElbow=command=="after";
            oldDevices=actions.devices;oldKeyboard=Keyboard.current;oldMouse=Mouse.current;
            oldBackground=Application.runInBackground;background=InputSystem.settings.backgroundBehavior;
            keyboard=InputSystem.AddDevice<Keyboard>("ElbowReviewKeys");mouse=InputSystem.AddDevice<Mouse>("ElbowReviewPointer");
            actions.devices=new InputDevice[]{keyboard,mouse};Application.runInBackground=true;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            rows.Clear();report=new Report{mode=command};path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestRecovery/DrawingElbow/"+command+"/live.json"));Directory.CreateDirectory(Path.GetDirectoryName(path));
            running=true;lastFrame=-1;start=EditorApplication.timeSinceStartup;drawing.Committed+=Committed;
            InputSystem.onBeforeUpdate+=Input;EditorApplication.update+=Observe;
            return status="RUNNING";
        }
        static void Committed(bool valid){report.commits++;}
        static Vector2 Pointer(float t)
        {
            if(t<1)return new Vector2(.5f,.5f);
            float u=(t-1)/7f;return new Vector2(.5f+.40f*Mathf.Sin(u*Mathf.PI*2),.5f+.39f*Mathf.Sin(u*Mathf.PI*4));
        }
        static void Input()
        {
            if(!running||InputState.currentUpdateType!=InputUpdateType.Dynamic)return;
            float t=(float)(EditorApplication.timeSinceStartup-start);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(t>.5f?new[]{Key.Q}:Array.Empty<Key>()));
            Vector2 p=Pointer(t);var m=new MouseState{position=new Vector2(p.x*Screen.width,p.y*Screen.height)};
            m.WithButton(MouseButton.Left,t>1);InputSystem.QueueStateEvent(mouse,m);
        }
        static void Observe()
        {
            if(!running)return;
            try
            {
                float t=(float)(EditorApplication.timeSinceStartup-start);
                if(t>=8){Finish(rows.Count>20&&report.commits==0?"RECORDED":"FAILED_NO_FRAMES");return;}
                if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
                if(t>1&&rig.Diagnostics.NearVisible)rows.Add(new Row{time=t,frame=Time.frameCount,pointer=Pointer(t),elbow=Field<WorldMacroCombatWalker>(rig,"_walker").ViewCamera.transform.InverseTransformPoint(elbow.position),hand=hand.rotation,pose=rig.Diagnostics});
            }catch(Exception e){report.error=e.ToString();Finish("FAILED");}
        }
        static void Finish(string result)
        {
            running=false;InputSystem.onBeforeUpdate-=Input;EditorApplication.update-=Observe;
            try
            {
                // PauseCoordinator cancels the unfinished production stroke without committing it.
                ui.OpenPage("일시정지");drawing.Committed-=Committed;
                actions.devices=oldDevices;InputSystem.RemoveDevice(keyboard);InputSystem.RemoveDevice(mouse);
                oldKeyboard?.MakeCurrent();oldMouse?.MakeCurrent();
                InputSystem.settings.backgroundBehavior=background;Application.runInBackground=oldBackground;
                rig.Profile.StableDrawingElbow=oldStable;ui.CloseMenu();ui.Pause.Gate.ReleaseImmediately();report.restored=true;
            }catch(Exception e){report.error+=" cleanup:"+e;}
            status=report.status=result;report.frames=rows.ToArray();File.WriteAllText(path,JsonUtility.ToJson(report,true));
        }
    }
}
