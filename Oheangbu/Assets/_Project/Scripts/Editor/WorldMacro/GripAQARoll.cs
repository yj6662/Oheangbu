using System;
using System.Globalization;
using System.IO;
using Oheangbu.App.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Explicit, one-frame A-hand framing comparison. Never starts Play or builds.</summary>
    public static class GripAQARoll
    {
        public static string Execute(string command) => WorldMacroPlayerReRigAuthoring.ExecuteGripARoll(command);
    }

    public static partial class WorldMacroPlayerReRigAuthoring
    {
        [Serializable] private sealed class GripARollResult
        {
            public string status, utc, command, profile, profileFileSha256Before, profileFileSha256After,
                directory, image, error;
            public float previousDrawingBrushRoll, requestedDrawingBrushRoll;
            public bool profileRestored, profileFilePreserved, otherProfileFieldsPreserved;
            public GestureQaReport presentation;
            public string scope = "Only DrawingBrushRoll changes. Finger poses, grip calibration, mesh, camera calibration and gameplay are unchanged. " +
                "A temporary roll override is not represented by the on-disk profile hash; requestedDrawingBrushRoll records it explicitly. " +
                "Numerical presentation checks do not certify visible fingers or skin/shaft contact.";
        }

        public static string ExecuteGripARoll(string command)
        {
            Need(!EditorApplication.isCompiling, "Wait for compilation before a roll comparison.");
            string[] parts = (command ?? "").Split(':');
            Need(parts.Length == 2 && (parts[0] == "capture" || parts[0] == "apply") &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _),
                "Expected capture:-162, capture:-192 or apply:<reviewed roll>.");
            float requested = float.Parse(parts[1], CultureInfo.InvariantCulture);
            Need(!float.IsNaN(requested) && !float.IsInfinity(requested) && requested >= -360f && requested <= 360f,
                "Roll must be finite and in [-360, 360] degrees.");
            bool capture = parts[0] == "capture";
            Need(capture ? EditorApplication.isPlaying && !EditorApplication.isPaused : !EditorApplication.isPlaying,
                capture ? "Capture requires an existing unpaused Play session." : "Persist a selected roll only in Edit mode.");
            var profile = AssetDatabase.LoadAssetAtPath<WorldMacroPlayerGestureProfile>(GripAProfile);
            Need(profile != null, "The selected A profile is missing.");
            if (capture)
            {
                var rig = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
                Need(rig != null && rig.IsPresentationReady && rig.Profile == profile && ActiveSkinSourcePath(rig) == GripAModel,
                    "Current actor must use the actual selected A mesh and profile.");
                var surface = JsonUtility.FromJson<GripASurface>(File.ReadAllText(Path.Combine(GripASource, "Validation/hand_surface_sampling.json")));
                Need(surface.sourceFbxSha256 == ActiveSkinSourceHash(rig), "The static A surface evidence is stale for this mesh.");
            }

            float previous = profile.DrawingBrushRoll;
            string beforeJson = JsonUtility.ToJson(profile);
            string token = requested.ToString("0.###", CultureInfo.InvariantCulture).Replace("-", "minus").Replace(".", "p");
            string directory = Path.Combine(GripASource, "Validation/Unity/RollComparison",
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "_roll_" + token + "_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "profile_before.json"), beforeJson);
            File.Copy(GripAProfile, Path.Combine(directory, "PlayerGesture_GripA_before.asset"));
            var report = new GripARollResult
            {
                utc = DateTime.UtcNow.ToString("o"), command = command, profile = GripAProfile,
                previousDrawingBrushRoll = previous, requestedDrawingBrushRoll = requested,
                profileFileSha256Before = Sha(GripAProfile), directory = directory
            };
            bool applied = false;
            try
            {
                if (!capture) Undo.RecordObject(profile, "Apply reviewed A-hand drawing roll");
                profile.DrawingBrushRoll = requested;
                if (capture)
                {
                    report.presentation = JsonUtility.FromJson<GestureQaReport>(RuntimeGestureQa("center", directory, true));
                    if (!string.IsNullOrEmpty(report.presentation.image) && File.Exists(report.presentation.image))
                    {
                        string target = Path.Combine(Path.GetDirectoryName(report.presentation.image), "synthetic_center_roll_" + token + ".png");
                        File.Move(report.presentation.image, target);
                        report.presentation.image = target; report.image = target;
                    }
                    report.presentation.scope += " Temporary DrawingBrushRoll=" + requested.ToString(CultureInfo.InvariantCulture) +
                        " degrees; see roll_comparison.json for the restored baseline profile and explicit override.";
                    File.WriteAllText(Path.Combine(directory, "runtime_capture_center.json"), JsonUtility.ToJson(report.presentation, true));
                    report.status = report.presentation.status == "PASS_SYNTHETIC_PRESENTATION_ONLY"
                        ? "CAPTURED_ROLL_REQUIRES_VISUAL_REVIEW" : "FINDINGS";
                }
                else
                {
                    EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
                    applied = true; report.status = "APPLIED_REVIEWED_ROLL_ONLY";
                }
            }
            catch (Exception exception) { report.error = exception.ToString(); report.status = "FINDINGS"; }
            finally
            {
                // Also verifies every other serialized field without replacing the live profile or rebinding the rig.
                profile.DrawingBrushRoll = previous;
                report.otherProfileFieldsPreserved = JsonUtility.ToJson(profile) == beforeJson;
                report.profileRestored = capture && report.otherProfileFieldsPreserved;
                if (applied) profile.DrawingBrushRoll = requested;
                else if (!capture && report.error != null)
                {
                    EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
                }
                report.profileFileSha256After = Sha(GripAProfile);
                report.profileFilePreserved = report.profileFileSha256Before == report.profileFileSha256After;
                if (!report.otherProfileFieldsPreserved || (capture && (!report.profileRestored || !report.profileFilePreserved)))
                    report.status = "FINDINGS";
            }
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(directory, "roll_comparison.json"), json);
            return json;
        }
    }
}
