using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Drawing;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroPlayerReRigAuthoring
    {
        private const BindingFlags QaPrivate = BindingFlags.Instance | BindingFlags.NonPublic;
        [Serializable] private sealed class GestureQaCase
        {
            public string name, status, detail;
            public float observed, limit;
            public WorldMacroPlayerGestureRig.GestureDiagnostics pose;
        }
        [Serializable] private sealed class GestureQaReport
        {
            public string sourceFbxSha256, gestureProfileSha256, gestureCodeSha256;
            public string sourceModelPath, gestureProfilePath, sampledSitClip;
            public BoundSkinSource[] boundSkins;
            public string status, utc, scene, captureState, image, error, scope =
                "Synthetic presentation-state evaluation on the installed runtime rig. No native input, mode channel, recognizer, cast, damage or reward dispatch. A temporary disabled 1920x1080 camera supplies projection; the actual gameplay camera is not moved.";
            public int unityFrame, passed, failed, unverified, strokeEvents, commitEvents, rawPointsBefore, rawPointsAfter;
            public bool restorationComplete, gameplayCameraPreserved, timeScalePreserved, rawStrokeStatePreserved,
                leasesPreserved, noGameplayDrawingEvents;
            public float timeScaleBefore, timeScaleAfter;
            public GestureQaCase[] checks;
            public string[] limitations =
            {
                "Repeated evaluations occur synchronously inside one Editor command, not distinct rendered gameplay frames.",
                "30/60/120 Hz checks cover the scalar unscaled response equation only. Real frame-rate, slow-motion, input-to-camera transition and walking tests remain unverified.",
                "Socket/endpoint/bone measurements do not prove visible skin/shaft contact or sleeve penetration. The separate contact report and visual review are required.",
                "Screenshots show a labelled-in-report synthetic pose using the current C2 camera composition. Native draw-mode camera entry, hit interruption and actual harvesting are not claimed."
            };
        }
        private sealed class GestureQaTransform
        {
            public Transform Target; public Vector3 Position, Scale; public Quaternion Rotation;
            public void Restore() { if (Target == null) return; Target.localPosition = Position; Target.localRotation = Rotation; Target.localScale = Scale; }
        }
        private sealed class GestureQaRenderer
        {
            public Renderer Target; public bool Enabled; public ShadowCastingMode Shadows;
            public float[] Shapes;
            public void Restore()
            {
                if (Target == null) return; Target.enabled = Enabled; Target.shadowCastingMode = Shadows;
                if (Target is SkinnedMeshRenderer skin && Shapes != null)
                    for (int i = 0; i < Shapes.Length; i++) skin.SetBlendShapeWeight(i, Shapes[i]);
            }
        }
        private sealed class GestureQaField
        {
            public object Owner, Value; public FieldInfo Field;
            public void Restore()
            {
                if (Field.IsInitOnly && Value is Array saved && Field.GetValue(Owner) is Array target)
                    Array.Copy(saved, target, saved.Length);
                else Field.SetValue(Owner, Value);
            }
        }

        private static string RuntimeGestureQa(string captureState, string auditOutput = null, bool firstPersonFixture = false)
        {
            Need(EditorApplication.isPlaying && !EditorApplication.isPaused,
                "Start the existing playtest manually in an unpaused Play session. This command never starts Play or builds a player.");
            var rig = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            Need(rig != null && rig.IsPresentationReady, "The installed rerig must be active and fully bound.");
            var input = QaGet<DrawingInputController>(rig, "_drawing");
            var feed = QaGet<BrushStrokeFeedAdapter>(rig, "_brushFeed");
            var harvest = QaGet<HarvestAction>(rig, "_harvest");
            var cameraRig = QaGet<CameraRigController>(rig, "_cameraRig");
            var walker = QaGet<WorldMacroCombatWalker>(rig, "_walker");
            Camera source = walker != null ? walker.ViewCamera : Camera.main;
            Need(input != null && feed != null && source != null, "Current drawing/feed/play camera bindings are required.");
            Need(!input.InDrawMode && !input.IsStroking && input.StrokeCount == 0 && (harvest == null || !harvest.IsExtracting)
                && (walker == null || !walker.Seated) && Time.timeScale > 0f,
                "Wait for neutral walking/carry state without an unfinished stroke, harvesting, seating or pause.");
            Need(rig.Diagnostics.DrawingWeight < .01f && rig.Diagnostics.HarvestWeight < .01f,
                "Wait for the previous gesture recovery to finish before the diagnostic.");
            string[] captureNames = { "center", "top-left", "top-right", "bottom-left", "bottom-right", "mid-left", "mid-right", "mid-top", "mid-bottom", "harvest", "carry", "world-drawing", "world-harvest", "sit-carry", "grip-close", "grip-palm" };
            Need(captureState == null || captureNames.Contains(captureState), "Unknown synthetic capture state.");

            var report = new GestureQaReport { utc = DateTime.UtcNow.ToString("o"), unityFrame = Time.frameCount,
                sourceFbxSha256 = ActiveSkinSourceHash(rig), gestureProfileSha256 = Sha(AssetDatabase.GetAssetPath(rig.Profile)),
                sourceModelPath = ActiveSkinSourcePath(rig), gestureProfilePath = AssetDatabase.GetAssetPath(rig.Profile), boundSkins = ReadBoundSkinSources(rig),
                gestureCodeSha256 = Sha("Assets/_Project/Scripts/App/World/WorldMacroPlayerGestureRig.cs"),
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path, captureState = captureState,
                rawPointsBefore = input.TotalPointCount, timeScaleBefore = Time.timeScale };
            var checks = new List<GestureQaCase>(80);
            var fields = new List<GestureQaField>();
            QaSaveDeclared(fields, rig);
            var motor = QaGet<Oheangbu.Combat.PlayerMotor>(rig, "_motor");
            if (captureState == "sit-carry" && motor != null) QaSave(fields, motor, "_posture");
            foreach (var chain in rig.GetComponentsInChildren<BrushBristleRig>(true)) QaSaveDeclared(fields, chain);
            QaSave(fields, input, "<InDrawMode>k__BackingField");
            QaSave(fields, input, "<HasPointer>k__BackingField");
            QaSave(fields, input, "<PointerScreenPosition>k__BackingField");
            QaSave(fields, input, "_currentStroke");
            QaSave(fields, feed, "_projectionCamera"); QaSave(fields, feed, "_hasStrokeEndpoint"); QaSave(fields, feed, "_strokeEndpointScreen");
            if (harvest != null) { QaSave(fields, harvest, "_lastExtractFrame"); QaSave(fields, harvest, "<ExtractSourcePosition>k__BackingField"); }
            if (cameraRig != null) QaSave(fields, cameraRig, "_drawingPresentationActive");
            var transforms = rig.GetComponentsInChildren<Transform>(true).Select(t => new GestureQaTransform
                { Target = t, Position = t.localPosition, Rotation = t.localRotation, Scale = t.localScale }).ToArray();
            var renderers = rig.GetComponentsInChildren<Renderer>(true).Select(r => new GestureQaRenderer
            {
                Target = r, Enabled = r.enabled, Shadows = r.shadowCastingMode,
                Shapes = r is SkinnedMeshRenderer skin && skin.sharedMesh != null
                    ? Enumerable.Range(0, skin.sharedMesh.blendShapeCount).Select(skin.GetBlendShapeWeight).ToArray() : null
            }).ToArray();
            Vector3 cameraPosition = source.transform.position; Quaternion cameraRotation = source.transform.rotation;
            int leaseCount = WorldMacroPlayerSkinningLease.ActiveLeaseCount;
            SkinWeights skinWeights = QualitySettings.skinWeights;
            bool enabled = rig.enabled;
            int rawStrokes = input.StrokeCount;
            Action start = () => report.strokeEvents++;
            Action<Vector2> point = _ => report.strokeEvents++;
            Action<bool> commit = _ => report.commitEvents++;
            input.StrokeStarted += start; input.StrokePointAdded += point; input.Committed += commit;
            GameObject cameraObject = null;
            try
            {
                cameraObject = new GameObject("Temporary_C02_GestureRuntimeQa_Camera") { hideFlags = HideFlags.HideAndDontSave };
                var camera = cameraObject.AddComponent<Camera>(); camera.CopyFrom(source); camera.enabled = false;
                camera.aspect = 1920f / 1080f; camera.pixelRect = new Rect(0f, 0f, 1920f, 1080f);
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                if (firstPersonFixture && walker != null && walker.Body != null)
                {
                    camera.transform.position = walker.Body.transform.position + Vector3.up * walker.EyeHeight;
                    report.scope += " Controlled A first-person eye-height fixture; only the disabled diagnostic camera moves. This is not native draw-mode input.";
                }
                var sourceData = source.GetComponent<UniversalAdditionalCameraData>();
                if (sourceData != null) EditorUtility.CopySerialized(sourceData, camera.GetUniversalAdditionalCameraData());
                QaSet(feed, "_projectionCamera", camera);
                Time.timeScale = 1f;
                var nearBrushRoot = QaGet<Transform>(rig, "_nearBrushRoot");
                var bristles = nearBrushRoot != null ? nearBrushRoot.GetComponent<BrushBristleRig>() : null;

                if (captureState != null)
                {
                    bool extracting = captureState == "harvest" || captureState == "world-harvest";
                    bool draw = !extracting && captureState != "carry" && captureState != "sit-carry";
                    if (captureState == "sit-carry")
                    {
                        var appearance = rig.GetComponent<WorldMacroPlayerAppearance>();
                        var movement = appearance != null ? QaGet<WorldMacroPlayerAppearanceProfile>(appearance, "_profile") : null;
                        Need(motor != null && movement != null && movement.SitIdle != null, "A real seated idle clip is required; standing carry cannot substitute for sitting QA.");
                        QaInvoke(rig, "RestoreAnimatedPose");
                        movement.SitIdle.SampleAnimation(rig.gameObject, movement.SitIdle.length * .5f);
                        QaSet(motor, "_posture", 1f);
                        report.sampledSitClip = AssetDatabase.GetAssetPath(movement.SitIdle);
                        report.scope += " Existing SitIdle clip sampled on the actual actor, seated carry predicate supplied, collider/root/controller untouched and transforms restored. Native sitting transition/terrain contact remains unverified.";
                    }
                    QaPose(rig, input, feed, harvest, camera, draw, extracting, QaViewport(captureState));
                    QaEndpointChecks(checks, captureState, rig.Diagnostics, draw);
                    if (firstPersonFixture && draw && !captureState.StartsWith("world-", StringComparison.Ordinal))
                    {
                        var nearRoot = QaGet<Transform>(rig, "_nearRoot");
                        foreach (var renderer in rig.GetComponentsInChildren<Renderer>(true))
                            if ((nearRoot == null || !renderer.transform.IsChildOf(nearRoot)) &&
                                (nearBrushRoot == null || !renderer.transform.IsChildOf(nearBrushRoot))) renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                    }
                    if (captureState.StartsWith("world-", StringComparison.Ordinal) || captureState == "sit-carry")
                    {
                        // Pose/projection was evaluated with the unchanged gameplay composition.
                        // Only this disabled review camera moves after the final pose is fixed.
                        var nearRoot = QaGet<Transform>(rig, "_nearRoot");
                        foreach (var renderer in rig.GetComponentsInChildren<Renderer>(true))
                        {
                            bool isNear = (nearRoot != null && renderer.transform.IsChildOf(nearRoot))
                                || (nearBrushRoot != null && renderer.transform.IsChildOf(nearBrushRoot));
                            if (isNear) renderer.enabled = false;
                            else renderer.shadowCastingMode = ShadowCastingMode.On;
                        }
                        Vector3 target = rig.transform.position + Vector3.up * .95f;
                        camera.transform.position = target + rig.transform.forward * 3.4f + rig.transform.right * 1.2f + Vector3.up * .25f;
                        camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);
                        camera.fieldOfView = 40f;
                        report.scope += " External world-body still: near renderers hidden and world shadow-only renderers shown solely for capture, then restored.";
                    }
                    if (captureState == "grip-close" || captureState == "grip-palm")
                    {
                        var nearRoot = QaGet<Transform>(rig, "_nearRoot");
                        var hand = nearRoot.GetComponentsInChildren<Transform>(true).First(t => t.name == "RightHand");
                        Vector3 target = hand.TransformPoint(rig.Profile.RightHandGripPosition);
                        camera.transform.position = target - camera.transform.forward * .30f + camera.transform.up * .13f;
                        if (captureState == "grip-palm") camera.transform.position = target - (hand.rotation * rig.Profile.HandDorsalLocal) * .30f + (hand.rotation * rig.Profile.HandThumbSideLocal) * .08f;
                        camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, camera.transform.up);
                        camera.fieldOfView = 48f; camera.nearClipPlane = .015f;
                        report.scope += " Grip detail: disabled review camera moved only after the center pose was solved. Metrics refer to the original 1080p central projection, not this close-up camera.";
                    }
                    report.image = QaCaptureEvaluatedSkin(rig, camera, captureState, auditOutput);
                    report.scope += " Same-command pose capture uses temporary BakeMesh(true) skin snapshots to avoid the renderer's previous-frame skinning cache. Temporary meshes are destroyed after capture; gameplay renderers are restored.";
                }
                else
                {
                    foreach (string name in new[] { "center", "top-left", "top-right", "bottom-left", "bottom-right", "left", "right", "top", "bottom" })
                    {
                        QaPose(rig, input, feed, harvest, camera, true, false, QaViewport(name));
                        QaEndpointChecks(checks, name, rig.Diagnostics, true);
                    }

                    if (bristles != null && bristles.IsBound)
                    {
                        bristles.EvaluateLocal(Vector3.right * 5f, true, .10f); bristles.ApplyPose();
                        float bent = bristles.Pose.BendDegrees;
                        QaAdd(checks, "near_bristle_fixture_bends", bent > .01f, bent, .01f, "Direct bristle diagnostic stimulus, not a native stroke.");
                        QaPose(rig, input, feed, harvest, camera, false, false, Vector2.one * .5f);
                        QaAdd(checks, "near_bristle_exit_resets", Mathf.Abs(bristles.Pose.BendDegrees) < .001f,
                            bristles.Pose.BendDegrees, .001f, "Actual visibility boundary resets the hidden chain.");
                        QaPose(rig, input, feed, harvest, camera, true, false, Vector2.one * .5f);
                        QaAdd(checks, "near_bristle_reentry_neutral", Mathf.Abs(bristles.Pose.BendDegrees) < .001f,
                            bristles.Pose.BendDegrees, .001f, "Re-entry begins without the prior stroke curvature.");
                    }
                    else checks.Add(new GestureQaCase { name = "near_bristle_exit_reentry", status = "UNVERIFIED", detail = "No valid six-bone near bristle binding." });

                    QaPose(rig, input, feed, harvest, camera, true, false, Vector2.one * .5f);
                    Quaternion[] pausedRotations = transforms.Select(t => t.Target.localRotation).ToArray();
                    Vector3 pausedTip = rig.EffectTip.position;
                    Time.timeScale = 0f;
                    QaInvoke(rig, "Update"); QaInvoke(rig, "LateUpdate");
                    float pauseDelta = 0f;
                    for (int i = 0; i < transforms.Length; i++) pauseDelta = Mathf.Max(pauseDelta,
                        Quaternion.Angle(pausedRotations[i], transforms[i].Target.localRotation));
                    QaAdd(checks, "pause_pose_stays_sampled", pauseDelta < .05f && Vector3.Distance(pausedTip, rig.EffectTip.position) < .00001f,
                        pauseDelta, .05f, "Time scale zero with unchanged synthetic drawing state; no native menu input.");
                    QaSetInput(input, feed, false, Vector2.one * .5f, camera);
                    QaInvoke(rig, "OnModeExited"); QaInvoke(rig, "LateUpdate");
                    QaAdd(checks, "paused_cancel_hides_near", !rig.Diagnostics.NearVisible, rig.Diagnostics.NearVisible ? 1f : 0f,
                        0f, "Synthetic cancellation state; the actual drawing controller's input and mode events are not dispatched.");
                    Time.timeScale = 1f;
                    QaPose(rig, input, feed, harvest, camera, false, false, Vector2.one * .5f);

                    rig.enabled = false;
                    QaAdd(checks, "disable_releases_skinning", WorldMacroPlayerSkinningLease.ActiveLeaseCount == leaseCount - 1,
                        WorldMacroPlayerSkinningLease.ActiveLeaseCount, leaseCount - 1, "Actual component OnDisable, no scene unload.");
                    rig.enabled = true;
                    QaAdd(checks, "reenable_rebinds_once", rig.IsPresentationReady && WorldMacroPlayerSkinningLease.ActiveLeaseCount == leaseCount,
                        WorldMacroPlayerSkinningLease.ActiveLeaseCount, leaseCount, rig.BindingError ?? "Binding restored without a duplicate lease.");
                    QaPose(rig, input, feed, harvest, camera, true, false, Vector2.one * .5f);
                    var fingerTransforms = rig.Profile.Fingers.Select(p => Find(rig.transform, p.BoneName)).Where(t => t != null).ToArray();
                    Quaternion[] initialFingers = fingerTransforms.Select(t => t.localRotation).ToArray();
                    for (int cycle = 0; cycle < 40; cycle++)
                    {
                        QaPose(rig, input, feed, harvest, camera, false, false, Vector2.one * .5f);
                        QaPose(rig, input, feed, harvest, camera, true, false, Vector2.one * .5f);
                    }
                    float drift = 0f;
                    for (int i = 0; i < fingerTransforms.Length; i++) drift = Mathf.Max(drift, Quaternion.Angle(initialFingers[i], fingerTransforms[i].localRotation));
                    QaAdd(checks, "forty_gesture_cycles_finger_drift", drift < .05f, drift, .05f,
                        "Real installed finger transforms after 40 synchronous carry/draw evaluations, not 40 rendered input cycles.");

                    if (harvest != null)
                    {
                        QaPose(rig, input, feed, harvest, camera, false, true, Vector2.one * .5f);
                        var grip = QaGet<Transform>(rig, "_worldGripSocket");
                        var tip = QaGet<Transform>(rig, "_worldTipSocket");
                        float alignment = Vector3.Dot((tip.position - grip.position).normalized,
                            (harvest.ExtractSourcePosition - grip.position).normalized);
                        QaAdd(checks, "harvest_predicate_aims_brush", rig.Diagnostics.State == WorldMacroPlayerGestureRig.GestureState.Harvest && alignment > .95f,
                            alignment, .95f, "Read-only predicate backing fields supplied by the diagnostic; TickHarvest, damage and ink gain are never called.");
                    }
                    else checks.Add(new GestureQaCase { name = "harvest_predicate_aims_brush", status = "UNVERIFIED", detail = "No existing HarvestAction binding." });
                    QaScalarTiming(checks);
                }
            }
            catch (Exception exception) { report.error = exception.ToString(); }
            finally
            {
                bool restoredAll = QaRestore(() => rig.enabled = enabled, "component enabled state", report);
                restoredAll &= QaRestore(() => QaInvoke(rig, "RestoreAnimatedPose"), "animated pose", report);
                foreach (var field in fields) restoredAll &= QaRestore(field.Restore, field.Field.Name, report);
                foreach (var pose in transforms) restoredAll &= QaRestore(pose.Restore, "transform", report);
                foreach (var renderer in renderers) restoredAll &= QaRestore(renderer.Restore, "renderer", report);
                report.restorationComplete = restoredAll;
                Time.timeScale = report.timeScaleBefore;
                input.StrokeStarted -= start; input.StrokePointAdded -= point; input.Committed -= commit;
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                report.timeScaleAfter = Time.timeScale; report.rawPointsAfter = input.TotalPointCount;
                report.timeScalePreserved = Mathf.Approximately(report.timeScaleBefore, report.timeScaleAfter);
                report.rawStrokeStatePreserved = input.StrokeCount == rawStrokes && input.TotalPointCount == report.rawPointsBefore && !input.InDrawMode;
                report.noGameplayDrawingEvents = report.strokeEvents == 0 && report.commitEvents == 0;
                report.gameplayCameraPreserved = Vector3.Distance(cameraPosition, source.transform.position) < .000001f
                    && Quaternion.Angle(cameraRotation, source.transform.rotation) < .001f;
                report.leasesPreserved = WorldMacroPlayerSkinningLease.ActiveLeaseCount == leaseCount && QualitySettings.skinWeights == skinWeights;
            }
            report.checks = checks.ToArray(); report.passed = checks.Count(c => c.status == "PASS");
            report.failed = checks.Count(c => c.status == "FAIL"); report.unverified = checks.Count(c => c.status == "UNVERIFIED");
            bool restored = report.restorationComplete && report.timeScalePreserved && report.rawStrokeStatePreserved
                && report.noGameplayDrawingEvents && report.gameplayCameraPreserved && report.leasesPreserved;
            report.status = report.error == null && report.failed == 0 && restored ? "PASS_SYNTHETIC_PRESENTATION_ONLY" : "FINDINGS";
            string directory = auditOutput ?? Output; Directory.CreateDirectory(directory); string json = JsonUtility.ToJson(report, true);
            string path = captureState == null ? "runtime_gesture_qa.json" : "runtime_capture_" + captureState + ".json";
            File.WriteAllText(Path.Combine(directory, path), json); return json;
        }

        private static void QaPose(WorldMacroPlayerGestureRig rig, DrawingInputController input, BrushStrokeFeedAdapter feed,
            HarvestAction harvest, Camera camera, bool drawing, bool extracting, Vector2 viewport)
        {
            QaInvoke(rig, "Update");
            QaSetInput(input, feed, drawing, viewport, camera);
            if (!drawing) QaInvoke(rig, "OnModeExited");
            QaSet(rig, "_drawWeight", drawing ? 1f : 0f); QaSet(rig, "_harvestWeight", extracting ? 1f : 0f);
            QaSet(rig, "_nearRaise", drawing ? 1f : 0f); // settled pose: the close-up arm has finished rising
            QaSet(rig, "_recoveryRemaining", 0f); QaSet(rig, "_bodyPoint", (viewport - Vector2.one * .5f) * 2f);
            if (harvest != null)
            {
                QaSet(harvest, "_lastExtractFrame", extracting ? Time.frameCount : Time.frameCount - 100);
                if (extracting) QaSet(harvest, "<ExtractSourcePosition>k__BackingField",
                    rig.transform.position + rig.transform.forward * 4f + Vector3.up * 1.2f);
            }
            QaInvoke(rig, "LateUpdate");
        }

        private static void QaSetInput(DrawingInputController input, BrushStrokeFeedAdapter feed, bool drawing, Vector2 viewport, Camera camera)
        {
            Vector3 projected = camera.ViewportToScreenPoint(new Vector3(viewport.x, viewport.y, 0f));
            var screen = new Vector2(projected.x, projected.y);
            QaSet(input, "<InDrawMode>k__BackingField", drawing);
            QaSet(input, "<HasPointer>k__BackingField", drawing);
            QaSet(input, "<PointerScreenPosition>k__BackingField", screen);
            QaSet(input, "_currentStroke", drawing ? new StrokeData() : null);
            QaSet(feed, "_hasStrokeEndpoint", drawing); QaSet(feed, "_strokeEndpointScreen", screen);
        }

        private static void QaEndpointChecks(List<GestureQaCase> checks, string name,
            WorldMacroPlayerGestureRig.GestureDiagnostics pose, bool near)
        {
            if (near) QaAdd(checks, name + "_near_tip_pixels", pose.NearVisible && pose.TipReachable
                && pose.NearTipErrorPixels >= 0f && pose.NearTipErrorPixels <= 2f, pose.NearTipErrorPixels, 2f,
                "Measured by the actual runtime solver against a 1920x1080 synthetic camera endpoint.", pose);
            if(near && pose.OverhandActive) {
                QaAdd(checks,name+"_bristles_thumb_side",pose.NearBrushThumbSideDot>.5f,pose.NearBrushThumbSideDot,.5f,"Bristles point toward index/thumb side, not pinky; independent of wrist roll.",pose);
                QaAdd(checks,name+"_wrist_bend",pose.NearWristBendDegrees<=45,pose.NearWristBendDegrees,45,"Forearm axis versus wrist-to-knuckles, not socket proximity.",pose);
                QaAdd(checks,name+"_wrist_twist",Mathf.Abs(pose.NearWristTwistDegrees)<=10,Mathf.Abs(pose.NearWristTwistDegrees),10,"Axial roll belongs to forearm; wrist seam residual.",pose);
                QaAdd(checks,name+"_dorsal_up",pose.NearDorsalUp>=.3f,pose.NearDorsalUp,.3f,"Calibrated dorsal normal dotted with camera up.",pose);
            }
            if(near && pose.VerticalActive) {
                // Vertical double-hook grip (쌍구법, profile ShuanggouGrip): bristles leave the little-finger side by design.
                QaAdd(checks,name+"_vertical_bristles_ulnar",pose.NearBrushThumbSideDot<=-.5f,pose.NearBrushThumbSideDot,-.5f,"Vertical grip: thumb/index above, bristles below the little finger (dot <= limit).",pose);
                QaAdd(checks,name+"_vertical_dorsal_out",pose.NearDorsalRight>=.3f,pose.NearDorsalRight,.3f,"Vertical grip: calibrated dorsal normal dotted with camera right.",pose);
                QaAdd(checks,name+"_vertical_axis_cone",pose.NearVerticalAxisErrorDegrees<=pose.NearVerticalConeDegrees+2f,pose.NearVerticalAxisErrorDegrees,pose.NearVerticalConeDegrees+2f,"Vertical grip: brush axis versus the tilted drawing-plane normal, within the stroke pivot cone.",pose);
            }
            QaAdd(checks, name + "_grip_socket", pose.GripErrorMeters <= .0001f, pose.GripErrorMeters, .0001f,
                "Hand grip socket to brush socket; visible skin contact is checked separately.");
            float limb = Mathf.Max(pose.UpperArmLengthErrorMeters, pose.ForearmLengthErrorMeters);
            QaAdd(checks, name + "_fixed_arm_segments", limb <= .0002f, limb, .0002f,
                "Current transform-hierarchy segment displacement, including scale, before world translation. The same 0.2 mm limit is retained. Rounded world-coordinate delta for comparison: "
                + Mathf.Max(pose.UpperArmWorldCoordinateDeltaMeters, pose.ForearmWorldCoordinateDeltaMeters).ToString("G6") + "m.");
            if (pose.ShaftMeasurementAvailable)
                QaAdd(checks, name + "_rigid_grip_ferrule_span", pose.ShaftLengthErrorMeters >= 0f && pose.ShaftLengthErrorMeters <= .0001f,
                    pose.ShaftLengthErrorMeters, .0001f,
                    "GripSocket to fixed first bristle bone/ferrule anchor. Nonzero measured span: " + pose.ShaftSpanMeters.ToString("F5") + "m.");
            else checks.Add(new GestureQaCase { name = name + "_rigid_grip_ferrule_span", status = "UNVERIFIED",
                detail = "No distinct rigid shaft/ferrule endpoint; a zero root-to-grip span is not counted as a pass." });
            QaAdd(checks, name + "_brush_unit_world_scale", pose.BrushWorldScaleError <= .0001f,
                pose.BrushWorldScaleError, .0001f, "Brush root world scale deviation from (1,1,1).");
        }

        private static void QaScalarTiming(List<GestureQaCase> checks)
        {
            var method = typeof(WorldMacroPlayerGestureRig).GetMethod("Follow", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (int fps in new[] { 30, 60, 120 }) foreach (float gameScale in new[] { 1f, .35f })
            {
                float value = 0f;
                for (int i = 0; i < fps; i++) value = (float)method.Invoke(null, new object[] { value, 1f, 14f, 1f / fps });
                float error = Mathf.Abs(value - 1f);
                QaAdd(checks, "scalar_response_" + fps + "hz_scale_" + gameScale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                    error <= .0001f, error, .0001f,
                    "Pure response equation over one unscaled second. Game scale is intentionally absent from that equation; no real frame-rate or slowed rendering test.");
            }
        }

        private static Vector2 QaViewport(string name)
        {
            switch (name)
            {
                // mid-screen points: the hand stays in frame, so the arm/elbow response to stroke position is visible
                case "mid-left": return new Vector2(.30f, .50f);
                case "mid-right": return new Vector2(.70f, .50f);
                case "mid-top": return new Vector2(.50f, .72f);
                case "mid-bottom": return new Vector2(.50f, .28f);
                case "top-left": return new Vector2(.015f, .985f);
                case "top-right": return new Vector2(.985f, .985f);
                case "bottom-left": return new Vector2(.015f, .015f);
                case "bottom-right": return new Vector2(.985f, .015f);
                case "left": return new Vector2(.015f, .5f);
                case "right": return new Vector2(.985f, .5f);
                case "top": return new Vector2(.5f, .985f);
                case "bottom": return new Vector2(.5f, .015f);
                default: return Vector2.one * .5f;
            }
        }

        private static string QaCaptureEvaluatedSkin(WorldMacroPlayerGestureRig rig, Camera camera, string name, string auditOutput = null)
        {
            Need(Prologue.PrologueAudit.CommitRatio() < .85f, "System commit is at or above 85%; no pose snapshot allocated.");
            var originals = new List<SkinnedMeshRenderer>();
            var meshes = new List<Mesh>();
            var objects = new List<GameObject>();
            try
            {
                foreach (var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!skin.enabled || !skin.gameObject.activeInHierarchy || skin.sharedMesh == null) continue;
                    var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave }; meshes.Add(mesh);
                    skin.BakeMesh(mesh, true);
                    var snapshot = new GameObject("Temporary_C02_PoseSkin_" + skin.name) { hideFlags = HideFlags.HideAndDontSave, layer = skin.gameObject.layer };
                    objects.Add(snapshot); snapshot.transform.SetParent(skin.transform, false);
                    snapshot.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = snapshot.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = skin.sharedMaterials;
                    renderer.shadowCastingMode = skin.shadowCastingMode; renderer.receiveShadows = skin.receiveShadows;
                    originals.Add(skin); skin.enabled = false;
                }
                return QaCapture(camera, name, auditOutput);
            }
            finally
            {
                foreach (var skin in originals) if (skin != null) skin.enabled = true;
                foreach (var obj in objects) if (obj != null) Object.DestroyImmediate(obj);
                foreach (var mesh in meshes) if (mesh != null) Object.DestroyImmediate(mesh);
            }
        }

        private static string QaCapture(Camera camera, string name, string auditOutput = null)
        {
            Need(Prologue.PrologueAudit.CommitRatio() < .85f, "System commit is at or above 85%; no screenshot allocated.");
            RenderTexture rt = null; Texture2D pixels = null; var active = RenderTexture.active;
            bool async = ShaderUtil.allowAsyncCompilation;
            var dressing = Object.FindFirstObjectByType<Oheangbu.App.World.Dressing.WorldMacroDressingRenderer>();
            bool diagnostic = dressing != null && dressing.AllowDiagnosticCameras;
            string directory = Path.Combine(auditOutput ?? Output, "Screenshots"); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "_synthetic_" + name + ".png");
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                if (dressing != null) dressing.AllowDiagnosticCameras = true;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false, false);
                camera.targetTexture = rt; camera.pixelRect = new Rect(0, 0, 1920, 1080); camera.Render();
                RenderTexture.active = rt; pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0, false); pixels.Apply(false, false);
                File.WriteAllBytes(path, pixels.EncodeToPNG()); return path;
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = active; ShaderUtil.allowAsyncCompilation = async;
                if (dressing != null) dressing.AllowDiagnosticCameras = diagnostic;
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                if (pixels != null) Object.DestroyImmediate(pixels);
            }
        }

        private static void QaAdd(List<GestureQaCase> checks, string name, bool pass, float observed, float limit,
            string detail, WorldMacroPlayerGestureRig.GestureDiagnostics pose = default)
        {
            bool finite = !float.IsNaN(observed) && !float.IsInfinity(observed);
            if (float.IsNaN(pose.NearTipErrorPixels) || float.IsInfinity(pose.NearTipErrorPixels)) pose.NearTipErrorPixels = -1f;
            checks.Add(new GestureQaCase { name = name, status = pass && finite ? "PASS" : "FAIL",
                observed = finite ? observed : -1f, limit = limit, detail = detail, pose = pose });
        }

        private static FieldInfo QaField(object owner, string name)
        {
            var field = owner.GetType().GetField(name, QaPrivate);
            if (field == null) throw new MissingFieldException(owner.GetType().Name, name);
            return field;
        }
        private static void QaSave(List<GestureQaField> fields, object owner, string name)
        { var field = QaField(owner, name); fields.Add(new GestureQaField { Owner = owner, Field = field, Value = field.GetValue(owner) }); }
        private static void QaSaveDeclared(List<GestureQaField> fields, object owner)
        {
            foreach (var field in owner.GetType().GetFields(QaPrivate | BindingFlags.DeclaredOnly))
            {
                object value = field.GetValue(owner);
                fields.Add(new GestureQaField { Owner = owner, Field = field, Value = value is Array array ? array.Clone() : value });
            }
        }
        private static bool QaRestore(Action restore, string detail, GestureQaReport report)
        {
            try { restore(); return true; }
            catch (Exception exception) { report.error = (report.error ?? "") + "\nRESTORE " + detail + ": " + exception; return false; }
        }
        private static T QaGet<T>(object owner, string name) { return (T)QaField(owner, name).GetValue(owner); }
        private static void QaSet(object owner, string name, object value) { QaField(owner, name).SetValue(owner, value); }
        private static void QaInvoke(object owner, string name)
        {
            var method = owner.GetType().GetMethod(name, QaPrivate);
            if (method == null) throw new MissingMethodException(owner.GetType().Name, name);
            method.Invoke(owner, null);
        }
    }
}
