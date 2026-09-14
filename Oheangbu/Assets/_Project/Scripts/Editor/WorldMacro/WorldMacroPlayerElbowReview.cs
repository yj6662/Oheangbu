using System;
using System.IO;
using Oheangbu.App.World;
using UnityEngine;
using UnityEditor;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroPlayerReRigAuthoring
    {
        public static string ExecuteElbowReview(string command)
        {
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestRecovery/DrawingElbow"));
            Directory.CreateDirectory(root);
            if(command=="apply")
            {
                Need(!EditorApplication.isPlaying,"Edit only");
                var profile=AssetDatabase.LoadAssetAtPath<WorldMacroPlayerGestureProfile>("Assets/_Project/Art/Characters/PlaytestRecoveryGripA/PlayerGesture_GripA.asset");
                profile.StableDrawingElbow=true;EditorUtility.SetDirty(profile);AssetDatabase.SaveAssets();return "Applied stable drawing elbow";
            }
            var parts=command.Split(':');
            Need(parts.Length>=2&&(parts[0]=="before"||parts[0]=="after"),"before/after:check or before/after:capture:state");
            var rig=Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            Need(rig!=null&&EditorApplication.isPlaying,"Play rig required");
            bool old=rig.Profile.StableDrawingElbow;
            try
            {
                rig.Profile.StableDrawingElbow=parts[0]=="after";
                return RuntimeGestureQa(parts[1]=="check"?null:parts[2],Path.Combine(root,parts[0]),true);
            }
            finally {rig.Profile.StableDrawingElbow=old;}
        }
    }
}
