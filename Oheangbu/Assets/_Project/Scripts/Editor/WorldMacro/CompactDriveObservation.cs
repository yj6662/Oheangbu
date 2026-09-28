using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
namespace Oheangbu.EditorTools.WorldMacro
{
    [InitializeOnLoad] public static class CompactDriveObservation
    {
        const string Key="CompactRecovery.Drive",Suffix="PlaytestUiReviewSuffix";
        static double next;
        static CompactDriveObservation(){EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=Changed;}
        public static string Begin()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||SessionState.GetBool(Key,false))throw new InvalidOperationException("Edit mode without active test required");
            if(SceneManager.GetActiveScene().path!=WorldMacroCompactAuthoring.TargetScene)throw new InvalidOperationException("Compact required");
            string token="_drive_"+Guid.NewGuid().ToString("N");SessionState.SetString(Key+".prior",SessionState.GetString(Suffix,"__missing__"));
            SessionState.SetString(Suffix,token);SessionState.SetString(Key+".token",token);SessionState.SetBool(Key,true);SessionState.SetBool(Key+".background",Application.runInBackground);
            Application.runInBackground=true;EditorApplication.isPlaying=true;return token+"; isolated fresh save, no quest-state seed; explicit drive-stop required";
        }
        public static string Setup()
        {
            if(!EditorApplication.isPlaying||!SessionState.GetBool(Key,false))throw new InvalidOperationException("Isolated Play required");
            var s=CompactRecovery.Find<WorldMacroPlaytestSession>();var car=CompactRecovery.Find<WorldMacroPalanquinController>();var seat=CompactRecovery.Find<WorldMacroPalanquinSeat>();
            if(seat.Occupied)throw new InvalidOperationException("Already seated");
            var candidate=seat.ExitSockets[0].position;var hits=Physics.RaycastAll(candidate+Vector3.up*3,Vector3.down,8,~0,QueryTriggerInteraction.Ignore);
            Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));bool found=false;
            foreach(var h in hits)if(!h.collider.transform.IsChildOf(car.transform)&&h.normal.y>.8f){candidate=h.point+Vector3.up*.04f;found=true;break;}
            if(!found)throw new InvalidOperationException("No supported approach point");
            s.Teleport(candidate,Quaternion.LookRotation(car.transform.position-candidate).eulerAngles.y);
            var view=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));view.Focus();
            return "API setup only: player placed beside authored car. Native E/W input remains unperformed. "+CompactRecovery.State();
        }
        static void Tick()
        {
            if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling||EditorApplication.timeSinceStartup<next)return;
            next=EditorApplication.timeSinceStartup+.1;
            try{string json=CompactRecovery.State(false).Replace("\r","").Replace("\n","");File.AppendAllText(Path.Combine(CompactRecovery.Output,SessionState.GetString(Key+".token","")+".jsonl"),json+"\n");}catch(Exception e){Debug.LogWarning("Drive observer: "+e.Message);}
        }
        public static string Stop(){if(!SessionState.GetBool(Key,false))return "inactive";EditorApplication.isPlaying=false;return "stopping; prior suffix restored after Edit mode";}
        static void Changed(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredEditMode||!SessionState.GetBool(Key,false))return;
            string prior=SessionState.GetString(Key+".prior","__missing__");if(prior=="__missing__")SessionState.EraseString(Suffix);else SessionState.SetString(Suffix,prior);
            Application.runInBackground=SessionState.GetBool(Key+".background",false);SessionState.SetBool(Key,false);
        }
    }
}
