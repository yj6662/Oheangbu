using System;
using System.IO;
using Oheangbu.Data;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools
{
    public static class DosaV2ClosedGripBuilder
    {
        [Serializable] private sealed class Grasp
        {
            public string candidate;
            public bool OverrideHandGrip, KeepGripClosedOnPenLift;
            public Vector3 HandGripLocalPosition;
            public Quaternion HandGripLocalRotation;
            public Quaternion[] CalibratedRightFingerOffsets;
        }

        public static string ApplySelected()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Apply the grasp in Edit Mode.");
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            string source = Path.Combine(root, "Art/PlayerV2/Inspect/ClosedGripDraft/A.json");
            var grasp = JsonUtility.FromJson<Grasp>(File.ReadAllText(source));
            if (grasp.CalibratedRightFingerOffsets == null || grasp.CalibratedRightFingerOffsets.Length != 15)
                throw new InvalidDataException("The grasp must provide all fifteen right finger rotations.");
            const string asset = "Assets/_Project/Data/PlayerV2/DrawingPose_DosaV2.asset";
            var profile = AssetDatabase.LoadAssetAtPath<DrawingPoseProfileSO>(asset);
            if (!profile) throw new FileNotFoundException(asset);
            string backup = Path.Combine(root, "Art/PlayerV2/Backups/ClosedGrip_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(backup);
            File.Copy(Path.GetFullPath(asset), Path.Combine(backup, Path.GetFileName(asset)));
            Undo.RecordObject(profile, "Use closed brush grasp");
            profile.OverrideHandGrip = grasp.OverrideHandGrip;
            profile.HandGripLocalPosition = grasp.HandGripLocalPosition;
            profile.HandGripLocalRotation = grasp.HandGripLocalRotation.normalized;
            profile.KeepGripClosedOnPenLift = grasp.KeepGripClosedOnPenLift;
            profile.CalibratedRightFingerOffsets = grasp.CalibratedRightFingerOffsets;
            // Turn the back of the gripping hand toward the play camera.
            profile.BrushRollDegrees = -45f;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return JsonUtility.ToJson(new Result { status = "APPLIED", candidate = grasp.candidate, profile = asset, source = source, backup = backup });
        }

        [Serializable] private sealed class Result
        {
            public string status, candidate, profile, source, backup;
        }
    }
}
