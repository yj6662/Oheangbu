using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World.Vehicle;
using Oheangbu.App.World.UI;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactRecovery
    {
        public static string Output=>Path.Combine(WorldMacroCompactAuthoring.Output,"Recovery");
        public static string Execute(string command)
        {
            Directory.CreateDirectory(Output);
            if(command.StartsWith("ink-study:"))return CompactInkPaintingStudy.Execute(command.Substring(10));
            if(command.StartsWith("ink-broad:"))return CompactBroadBrushAuthoring.Execute(command.Substring(10));
            if(command.StartsWith("ink-paint:"))return CompactPainterlyAuthoring.Execute(command.Substring(10));
            if(command.StartsWith("ink-dark:"))return CompactDarkInkAuthoring.Execute(command.Substring(9));
            if(command.StartsWith("ink:"))return CompactInkLandscapeAuthoring.Execute(command.Substring(4));
            if(command=="preserve")return Preserve();
            if(command=="state")return State();
            if(command=="drive-begin")return CompactDriveObservation.Begin();
            if(command=="drive-setup")return CompactDriveObservation.Setup();
            if(command=="drive-stop")return CompactDriveObservation.Stop();
            if(command.StartsWith("capture:"))return Capture(command.Substring(8));
            if(command=="shadow-casters")return ShadowCasters();
            if(command=="surfaces-export")return ExportSurfaces();
            if(command=="surfaces-import")return ImportSurfaces();
            if(command=="assemblies")return AuditAssemblies();
            if(command.StartsWith("natural:"))return CompactNaturalSurface.Execute(command.Substring(8));
            if(command.StartsWith("kcisa:"))return CompactKcisaReplacement.Execute(command.Substring(6));
            if(command=="courtyard-display")
            {
                RequireEdit();var t=SceneManager.GetActiveScene().GetRootGameObjects().Single(g=>g.name=="Playtest_Village_Office").transform.Find("Courtyard");
                if(!AssetDatabase.GetAssetPath(t.GetComponent<MeshFilter>().sharedMesh).Contains("/Recovery/"))throw new InvalidOperationException("Ground conforming derivative required first");
                // Pigment display is coincident with the actual terrain; only terrain casts.
                var r=t.GetComponent<MeshRenderer>();r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                EditorUtility.SetDirty(r);EditorSceneManager.MarkSceneDirty(t.gameObject.scene);EditorSceneManager.SaveScene(t.gameObject.scene);return "Courtyard display no longer duplicates the underlying terrain shadow caster";
            }
            throw new ArgumentException(command);
        }
        static string PathOf(Transform t)=>t.parent==null?t.name:PathOf(t.parent)+"/"+t.name;
        internal static T Find<T>() where T:Component=>Object.FindObjectsByType<T>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(x=>x.gameObject.scene==SceneManager.GetActiveScene());
        static void RequireEdit()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=WorldMacroCompactAuthoring.TargetScene)throw new InvalidOperationException("Compact Edit scene required");
        }
        static string Preserve()
        {
            RequireEdit();var scene=SceneManager.GetActiveScene();bool dirty=scene.isDirty;
            string disk=Path.Combine(Output,"before_disk.unity"),live=Path.Combine(Output,"before_open_scene.unity");
            if(!File.Exists(disk))File.Copy(scene.path,disk);
            if(!File.Exists(live)&&!EditorSceneManager.SaveScene(scene,live,true))throw new IOException("Could not preserve open scene");
            return "PRESERVED disk and open scene separately; initial dirty="+dirty+"; active="+scene.path;
        }
        [Serializable] public sealed class VehicleState
        {
            public bool play,sceneDirty,occupied,driver,configured,seatEnabled,vehicleEnabled,inputBlocked,environmentBlocked,focus,braking,releasePending,focusBlock;
            public float timeScale,throttle,applied,torque,speed,hp;
            public int grounded,pauseDepth,neutralFrames;
            public string issue,scene,interaction;
            public Vector3 position;
        }
        public static string State(bool persist=true)
        {
            var seat=Find<WorldMacroPalanquinSeat>();var v=seat?.Vehicle;var gate=Find<GameplayUiGate>();var pause=Find<PauseCoordinator>();
            var s=new VehicleState{play=EditorApplication.isPlaying,scene=SceneManager.GetActiveScene().path,sceneDirty=SceneManager.GetActiveScene().isDirty,timeScale=Time.timeScale,focus=Application.isFocused};
            if(seat!=null){s.occupied=seat.Occupied;s.seatEnabled=seat.isActiveAndEnabled;s.interaction=seat.LastInteraction;s.inputBlocked=seat.RuntimeState!=null&&seat.RuntimeState.InputBlocked;s.environmentBlocked=seat.CombatWalker!=null&&seat.CombatWalker.Motor.EnvironmentalInputBlocked;s.hp=seat.CombatWalker!=null?seat.CombatWalker.Body.GetComponent<Oheangbu.Combat.PlayerVitals>().Hp01:1;}
            if(v!=null){s.driver=v.DriverPresent;s.vehicleEnabled=v.isActiveAndEnabled;s.configured=v.IsConfigured;s.issue=v.ConfigurationIssue;s.throttle=v.RequestedThrottle;s.applied=v.AppliedThrottle;s.torque=v.AppliedMotorTorque;s.braking=v.Braking;s.grounded=v.GroundedWheelCount;s.speed=v.Speed;s.position=v.transform.position;}
            if(gate!=null){s.releasePending=gate.ReleasePending;s.focusBlock=gate.FocusOwnsBlock;s.neutralFrames=gate.NeutralFrames;}
            s.pauseDepth=pause!=null?pause.Depth:0;string json=JsonUtility.ToJson(s,true);if(persist)File.WriteAllText(Path.Combine(Output,"state.json"),json);return json;
        }
    }
}
