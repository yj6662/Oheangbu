using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Stationary play-mode lifecycle checks and explicitly requested 1080p player stills.</summary>
    [InitializeOnLoad]
    public static partial class WorldMacroPlayerAppearanceAuthoring
    {
        [Serializable]
        public sealed class RuntimeCheck
        {
            public string name;
            public string status;
            public string detail;
        }

        [Serializable]
        public sealed class RuntimeClipWeight
        {
            public string clip;
            public float weight;
        }

        [Serializable]
        public sealed class RuntimeSample
        {
            public int frame;
            public float gameTime;
            public float controllerPlanarSpeed;
            public float appearancePlanarSpeed;
            public float animatorPlanarSpeed;
            public float stateNormalizedTime;
            public string state;
            public RuntimeClipWeight[] clips;
            public bool bodyVisible;
            public string[] rendererModes;
        }

        [Serializable]
        public sealed class RuntimeCapture
        {
            public string view;
            public string file;
            public int width = 1920;
            public int height = 1080;
            public Vector3 position;
            public Vector3 target;
            public float fieldOfView;
            public bool imageWritten;
        }

        [Serializable]
        public sealed class RuntimeReviewReport
        {
            public string utc;
            public string status;
            public string request;
            public string detail;
            public string scope = "Actual W_WorldMacro_Playtest play-mode lifecycle and stationary 1920x1080 stills. No input synthesis, CharacterController.Move, automatic walking, video, source-camera move, physics change, or visual approval.";
            public RuntimeCheck[] checks = Array.Empty<RuntimeCheck>();
            public RuntimeSample[] samples = Array.Empty<RuntimeSample>();
            public RuntimeCapture[] captures = Array.Empty<RuntimeCapture>();
            public int shaderPassesChecked;
            public int shaderPassesCompiled;
            public float walkThreshold;
            public float runThreshold;
            public float runTimeScale;
            public float maximumPlanarDisplacement;
            public float verticalDisplacement;
            public bool sourceCameraPreserved;
            public bool cleanupComplete;
        }

        private static readonly List<RuntimeCheck> ReviewChecks = new List<RuntimeCheck>();
        private static readonly List<RuntimeSample> ReviewSamples = new List<RuntimeSample>();
        private static readonly List<RuntimeCapture> ReviewCaptures = new List<RuntimeCapture>();
        private static bool ReviewRunning;
        private static bool ReviewCaptureBehind;
        private static bool ReviewCaptureFront;
        private static int ReviewStartFrame;
        private static float ReviewStartGameTime;
        private static double ReviewDeadline;
        private static Vector3 ReviewStartFeet;
        private static Vector3 ReviewCameraPosition;
        private static Quaternion ReviewCameraRotation;
        private static WorldMacroCombatWalker ReviewWalker;
        private static WorldMacroPlayerAppearance ReviewAppearance;
        private static Animator ReviewAnimator;
        private static Camera ReviewSourceCamera;
        private static RuntimeReviewReport ReviewReport;

        private static string RuntimeReviewOutput => WorldMacroPlaytestAuthoring.Output + "/PlayerAppearance/RuntimeReview";
        public static bool RuntimeReviewIsRunning => ReviewRunning;

        static WorldMacroPlayerAppearanceAuthoring()
        {
            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                if (ReviewRunning) FinishRuntimeReview("ABORTED", "Assembly reload interrupted the player review.");
            };
            EditorApplication.playModeStateChanged += state =>
            {
                if (ReviewRunning && state == PlayModeStateChange.ExitingPlayMode)
                    FinishRuntimeReview("ABORTED", "Play mode exited before review completion.");
            };
        }

        public static string BeginRuntimeReview() => BeginRuntimeReview("checks");

        public static string BeginRuntimeReview(string request)
        {
            if (ReviewRunning) throw new InvalidOperationException("Player runtime review is already running; Poll or EndRuntimeReview first.");
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || Time.timeScale <= 0f
                || SceneManager.GetActiveScene().path != WorldMacroPlaytestAuthoring.ScenePath)
                throw new InvalidOperationException("W_WorldMacro_Playtest must already be running and unpaused. This helper does not start Play.");

            string normalized = (request ?? "checks").Trim().ToLowerInvariant();
            ReviewCaptureBehind = normalized == "capture:behind" || normalized == "capture:both";
            ReviewCaptureFront = normalized == "capture:front" || normalized == "capture:both";
            if (normalized != "checks" && !ReviewCaptureBehind && !ReviewCaptureFront)
                throw new ArgumentException("Expected checks, capture:behind, capture:front, or capture:both.");

            ReviewWalker = Object.FindFirstObjectByType<WorldMacroCombatWalker>();
            ReviewAppearance = ReviewWalker != null && ReviewWalker.Body != null
                ? ReviewWalker.Body.transform.Find(SceneRootName)?.GetComponent<WorldMacroPlayerAppearance>() : null;
            ReviewAnimator = ReviewAppearance != null ? ReviewAppearance.Animator : null;
            ReviewSourceCamera = ReviewWalker != null ? ReviewWalker.ViewCamera : null;
            if (ReviewWalker == null || ReviewWalker.Body == null || ReviewAppearance == null || ReviewAnimator == null
                || ReviewSourceCamera == null || ReviewWalker.CameraRig == null)
                throw new InvalidOperationException("Installed macro player appearance, Animator, walker, camera, or camera rig is missing.");
            if (ReviewWalker.Seated || !ReviewWalker.Body.enabled || !ReviewWalker.Motor.enabled || ReviewAppearance.DrawingActive)
                throw new InvalidOperationException("Unseated, enabled, non-drawing player state required. The helper does not take input ownership.");
            if (ReviewAnimator.runtimeAnimatorController == null || ReviewAnimator.avatar == null
                || !ReviewAnimator.avatar.isValid || !ReviewAnimator.avatar.isHuman)
                throw new InvalidOperationException("Live C02 Animator needs its valid Humanoid Avatar and owned controller.");

            ReviewChecks.Clear();
            ReviewSamples.Clear();
            ReviewCaptures.Clear();
            ReviewReport = new RuntimeReviewReport
            {
                utc = DateTime.UtcNow.ToString("o"),
                status = "RUNNING",
                request = normalized
            };
            ReadBlendContract(ReviewReport, ReviewChecks);
            if (ReviewCaptureBehind || ReviewCaptureFront)
            {
                var variants = MagicStoneCarReviewTools.PrepareCaptureVariants(ReviewAppearance.gameObject);
                ReviewReport.shaderPassesChecked = variants.checkedPasses;
                ReviewReport.shaderPassesCompiled = variants.compiledNow;
            }

            ReviewStartFrame = Time.frameCount;
            ReviewStartGameTime = Time.time;
            ReviewStartFeet = ReviewWalker.Body.transform.position;
            ReviewCameraPosition = ReviewSourceCamera.transform.position;
            ReviewCameraRotation = ReviewSourceCamera.transform.rotation;
            ReviewDeadline = EditorApplication.timeSinceStartup + 20d;
            ReviewRunning = true;
            EditorApplication.update += RuntimeReviewTick;
            return "RUNNING: stationary player lifecycle" + (ReviewCaptureBehind || ReviewCaptureFront
                ? " and requested 1920x1080 still capture." : ".") + " No automatic walking or video.";
        }

        public static string Poll()
        {
            if (ReviewRunning)
                return "RUNNING frames=" + (Time.frameCount - ReviewStartFrame) + "; samples=" + ReviewSamples.Count
                    + "; request=" + ReviewReport.request;
            string path = RuntimeReviewOutput + "/runtime_review.json";
            if (!File.Exists(path)) return "NOT_RUN";
            var report = JsonUtility.FromJson<RuntimeReviewReport>(File.ReadAllText(path));
            return report.status + "; checks=" + report.checks.Count(c => c.status == "PASS") + "/" + report.checks.Length
                + "; captures=" + report.captures.Count(c => c.imageWritten) + "/" + report.captures.Length
                + "; raw=" + path;
        }

        public static string EndRuntimeReview()
        {
            if (ReviewRunning) FinishRuntimeReview("ABORTED", "Stopped explicitly.");
            return Poll();
        }

        private static void RuntimeReviewTick()
        {
            if (!ReviewRunning) return;
            try
            {
                if (!EditorApplication.isPlaying || ReviewWalker == null || ReviewAppearance == null
                    || ReviewAnimator == null || ReviewSourceCamera == null)
                {
                    FinishRuntimeReview("ABORTED", "Live player review targets became unavailable.");
                    return;
                }
                if (EditorApplication.isPaused || Time.timeScale <= 0f)
                {
                    FinishRuntimeReview("ABORTED", "Pause interrupted the lifecycle review.");
                    return;
                }
                if (EditorApplication.timeSinceStartup > ReviewDeadline)
                {
                    FinishRuntimeReview("ABORTED", "Twenty-second wall-clock deadline reached.");
                    return;
                }
                if (ReviewWalker.Seated || ReviewAppearance.DrawingActive)
                {
                    FinishRuntimeReview("ABORTED", "Player ownership changed to seating or drawing during the stationary review.");
                    return;
                }

                int elapsedFrames = Time.frameCount - ReviewStartFrame;
                if (elapsedFrames < 2 || ReviewSamples.Count > 0 && ReviewSamples[ReviewSamples.Count - 1].frame == Time.frameCount) return;
                ReviewSamples.Add(ReadRuntimeSample());
                if (elapsedFrames < 8) return;

                // Snapshot immediately before the synchronous review work. Normal camera follow may
                // settle during the eight warm-up frames; this check is only about mutations by us.
                ReviewCameraPosition = ReviewSourceCamera.transform.position;
                ReviewCameraRotation = ReviewSourceCamera.transform.rotation;
                EvaluateRuntimeChecks();
                if (ReviewCaptureBehind) ReviewCaptures.Add(CapturePlayerStill("shoulder_behind"));
                if (ReviewCaptureFront) ReviewCaptures.Add(CapturePlayerStill("shoulder_front"));
                string status = ReviewChecks.All(c => c.status == "PASS")
                    && ReviewCaptures.All(c => c.imageWritten) ? "PASS_BOUNDED_RUNTIME" : "FINDINGS";
                FinishRuntimeReview(status, "Stationary lifecycle and requested stills completed.");
            }
            catch (Exception exception)
            {
                FinishRuntimeReview("ERROR", exception.ToString());
            }
        }

        private static RuntimeSample ReadRuntimeSample()
        {
            Vector3 velocity = ReviewWalker.Body.velocity;
            velocity.y = 0f;
            var state = ReviewAnimator.GetCurrentAnimatorStateInfo(0);
            var clips = ReviewAnimator.GetCurrentAnimatorClipInfo(0)
                .Select(info => new RuntimeClipWeight { clip = info.clip != null ? info.clip.name : "<NULL>", weight = info.weight }).ToArray();
            return new RuntimeSample
            {
                frame = Time.frameCount,
                gameTime = Time.time - ReviewStartGameTime,
                controllerPlanarSpeed = velocity.magnitude,
                appearancePlanarSpeed = ReviewAppearance.PlanarSpeed,
                animatorPlanarSpeed = ReviewAnimator.GetFloat(WorldMacroPlayerAppearance.PlanarSpeedParameter),
                stateNormalizedTime = state.normalizedTime,
                state = StateName(state),
                clips = clips,
                bodyVisible = ReviewWalker.CameraRig.IsBodyVisible,
                rendererModes = ReviewAppearance.WorldRenderers.Where(renderer => renderer != null)
                    .Select(renderer => renderer.name + ":enabled=" + renderer.enabled + ":shadow=" + renderer.shadowCastingMode).ToArray()
            };
        }

        private static string StateName(AnimatorStateInfo state)
        {
            if (state.IsName("B2 Idle Walk Run")) return "B2 Idle Walk Run";
            return "hash:" + state.shortNameHash;
        }

        private static void EvaluateRuntimeChecks()
        {
            RuntimeSample sample = ReviewSamples[ReviewSamples.Count - 1];
            AddReviewCheck("live Humanoid Animator", ReviewAnimator.isActiveAndEnabled && ReviewAnimator.isHuman
                && ReviewAnimator.avatar != null && ReviewAnimator.avatar.isValid,
                "avatar=" + ReviewAnimator.avatar.name + "; controller=" + ReviewAnimator.runtimeAnimatorController.name);
            AddReviewCheck("root motion remains off", !ReviewAnimator.applyRootMotion,
                "Animator.applyRootMotion=" + ReviewAnimator.applyRootMotion);
            AddReviewCheck("B2 locomotion state is live", sample.state == "B2 Idle Walk Run" && sample.clips.Length > 0,
                "state=" + sample.state + "; clips=" + string.Join(",", sample.clips.Select(c => c.clip + "@" + c.weight.ToString("F3"))));
            bool idle = sample.controllerPlanarSpeed <= .05f;
            bool idleClip = sample.clips.Any(c => c.clip == "Integrated_B2_C3_Idle" && c.weight >= .95f);
            AddReviewCheck("stationary sample resolves B2 Idle", !idle || idleClip,
                "controllerSpeed=" + sample.controllerPlanarSpeed.ToString("F4") + "; idleWeight="
                + sample.clips.Where(c => c.clip == "Integrated_B2_C3_Idle").Select(c => c.weight).DefaultIfEmpty(0f).Max().ToString("F3"));
            AddReviewCheck("actual speed drives Animator parameter", float.IsFinite(sample.animatorPlanarSpeed)
                && Mathf.Abs(sample.animatorPlanarSpeed - sample.appearancePlanarSpeed) <= .001f,
                "controller=" + sample.controllerPlanarSpeed.ToString("F3") + "; adapter=" + sample.appearancePlanarSpeed.ToString("F3")
                + "; animator=" + sample.animatorPlanarSpeed.ToString("F3"));
            AddReviewCheck("walk/run blend contract", Mathf.Abs(ReviewReport.walkThreshold - 1.5537528f) < .001f
                && Mathf.Abs(ReviewReport.runThreshold - 4.5f) < .001f
                && Mathf.Abs(ReviewReport.runTimeScale - 4.5f / 5.362285f) < .001f,
                "walk threshold=" + ReviewReport.walkThreshold.ToString("F3") + "m/s; run threshold="
                + ReviewReport.runThreshold.ToString("F3") + "m/s; run timeScale=" + ReviewReport.runTimeScale.ToString("F3"));

            var renderers = ReviewAppearance.WorldRenderers.Where(renderer => renderer != null).ToArray();
            bool rendererContract = SameRenderers(ReviewWalker.Visuals, renderers)
                && CameraRenderers(ReviewWalker.CameraRig, renderers);
            AddReviewCheck("camera and walker own C02 renderers", rendererContract,
                "appearance=" + renderers.Length + "; walker=" + (ReviewWalker.Visuals == null ? 0 : ReviewWalker.Visuals.Length));
            bool visibility = renderers.All(renderer => renderer.enabled && renderer.shadowCastingMode
                == (ReviewWalker.CameraRig.IsBodyVisible ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly));
            AddReviewCheck("live body visibility follows camera contract", visibility,
                "bodyVisible=" + ReviewWalker.CameraRig.IsBodyVisible + "; " + string.Join(",", sample.rendererModes));
            Transform primitive = ReviewWalker.Body.transform.Find("Body");
            var primitiveRenderer = primitive != null ? primitive.GetComponent<Renderer>() : null;
            AddReviewCheck("primitive body stays hidden", primitiveRenderer == null || !primitiveRenderer.enabled,
                primitiveRenderer == null ? "No primitive renderer." : "enabled=" + primitiveRenderer.enabled);

            Vector3 displacement = ReviewWalker.Body.transform.position - ReviewStartFeet;
            ReviewReport.maximumPlanarDisplacement = Vector3.ProjectOnPlane(displacement, Vector3.up).magnitude;
            ReviewReport.verticalDisplacement = displacement.y;
            AddReviewCheck("review does not move player", ReviewReport.maximumPlanarDisplacement <= .01f,
                "planar=" + ReviewReport.maximumPlanarDisplacement.ToString("F5") + "m; vertical physics settle="
                + ReviewReport.verticalDisplacement.ToString("F5") + "m");
        }

        private static void ReadBlendContract(RuntimeReviewReport report, List<RuntimeCheck> checks)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var profile = AssetDatabase.LoadAssetAtPath<WorldMacroPlayerAppearanceProfile>(ProfilePath);
            var tree = controller != null ? controller.layers.SelectMany(layer => layer.stateMachine.states)
                .Select(state => state.state.motion).OfType<BlendTree>().FirstOrDefault(candidate =>
                    candidate.blendParameter == WorldMacroPlayerAppearance.PlanarSpeedParameter) : null;
            if (controller == null || profile == null || tree == null)
            {
                checks.Add(new RuntimeCheck { name = "owned blend assets", status = "FAIL", detail = "Controller, profile, or PlanarSpeed BlendTree missing." });
                return;
            }
            var walk = tree.children.FirstOrDefault(child => child.motion == profile.Walk);
            var run = tree.children.FirstOrDefault(child => child.motion == profile.Run);
            report.walkThreshold = walk.threshold;
            report.runThreshold = run.threshold;
            report.runTimeScale = run.timeScale;
            checks.Add(new RuntimeCheck
            {
                name = "owned blend assets",
                status = tree.children.Length == 3 && walk.motion != null && run.motion != null ? "PASS" : "FAIL",
                detail = "children=" + tree.children.Length + "; controller=" + controller.name + "; profile=" + profile.name
            });
        }

        private static RuntimeCapture CapturePlayerStill(string view)
        {
            if (Prologue.PrologueAudit.CommitRatio() >= .85f)
                throw new InvalidOperationException("Stopped before player still: system commit >=85%.");
            Transform player = ReviewWalker.Body.transform;
            Bounds bounds = RuntimeBounds(ReviewAppearance.WorldRenderers);
            Vector3 up = Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(player.forward, up).normalized;
            Vector3 right = Vector3.Cross(up, forward).normalized;
            Vector3 target = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * .52f, bounds.center.z);
            Vector3 localOffset = view == "shoulder_behind"
                ? -forward * 3.15f + right * .62f + up * .62f
                : forward * 3.15f - right * .62f + up * .62f;
            Vector3 position = ResolveStillPosition(target, target + localOffset, player);
            var capture = new RuntimeCapture
            {
                view = view,
                file = RuntimeReviewOutput + "/player_" + view + ".png",
                position = position,
                target = target,
                fieldOfView = 42f
            };

            GameObject cameraObject = null;
            Camera camera = null;
            RenderTexture targetTexture = null;
            Texture2D pixels = null;
            RenderTexture previousActive = RenderTexture.active;
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                cameraObject = new GameObject("Temporary_C02PlayerStill_" + view) { hideFlags = HideFlags.HideAndDontSave };
                camera = cameraObject.AddComponent<Camera>();
                camera.CopyFrom(ReviewSourceCamera);
                camera.enabled = false;
                camera.aspect = 1920f / 1080f;
                camera.fieldOfView = capture.fieldOfView;
                camera.useOcclusionCulling = false;
                camera.layerCullDistances = new float[32];
                camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, up));
                var sourceData = ReviewSourceCamera.GetComponent<UniversalAdditionalCameraData>();
                if (sourceData != null) EditorUtility.CopySerialized(sourceData, camera.GetUniversalAdditionalCameraData());
                targetTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false, false);
                camera.targetTexture = targetTexture;
                camera.Render();
                RenderTexture.active = targetTexture;
                pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0, false);
                pixels.Apply(false, false);
                Directory.CreateDirectory(RuntimeReviewOutput);
                File.WriteAllBytes(capture.file, pixels.EncodeToPNG());
                capture.imageWritten = File.Exists(capture.file) && new FileInfo(capture.file).Length > 1024;
                return capture;
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = previousAsync;
                if (camera != null) camera.targetTexture = null;
                RenderTexture.active = previousActive;
                if (targetTexture != null) { targetTexture.Release(); Object.DestroyImmediate(targetTexture); }
                if (pixels != null) Object.DestroyImmediate(pixels);
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
            }
        }

        private static Vector3 ResolveStillPosition(Vector3 target, Vector3 desired, Transform player)
        {
            Vector3 delta = desired - target;
            float distance = delta.magnitude;
            var hits = Physics.RaycastAll(target, delta.normalized, distance, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => hit.collider != null && !hit.transform.IsChildOf(player))
                .OrderBy(hit => hit.distance).ToArray();
            if (hits.Length == 0) return desired;
            float available = hits[0].distance - .18f;
            if (available < 1.35f)
                throw new InvalidOperationException("No clear " + distance.ToString("F2") + "m player still line; nearest world surface leaves "
                    + available.ToString("F2") + "m. Player was not relocated.");
            return target + delta.normalized * available;
        }

        private static Bounds RuntimeBounds(Renderer[] renderers)
        {
            var valid = renderers != null ? renderers.Where(renderer => renderer != null).ToArray() : Array.Empty<Renderer>();
            if (valid.Length == 0) throw new InvalidOperationException("Live player has no render bounds.");
            Bounds bounds = valid[0].bounds;
            for (int i = 1; i < valid.Length; i++) bounds.Encapsulate(valid[i].bounds);
            return bounds;
        }

        private static void AddReviewCheck(string name, bool pass, string detail)
        {
            ReviewChecks.Add(new RuntimeCheck { name = name, status = pass ? "PASS" : "FAIL", detail = detail });
        }

        private static void FinishRuntimeReview(string status, string detail)
        {
            if (!ReviewRunning) return;
            ReviewRunning = false;
            EditorApplication.update -= RuntimeReviewTick;
            try
            {
                if (ReviewSourceCamera != null)
                {
                    ReviewReport.sourceCameraPreserved = Vector3.Distance(ReviewSourceCamera.transform.position, ReviewCameraPosition) <= .0001f
                        && Quaternion.Angle(ReviewSourceCamera.transform.rotation, ReviewCameraRotation) <= .001f;
                }
                else ReviewReport.sourceCameraPreserved = false;
                AddReviewCheck("source camera transform preserved", ReviewReport.sourceCameraPreserved,
                    "Temporary offscreen cameras only; actual camera transform was not assigned.");
                ReviewReport.detail = detail;
                ReviewReport.samples = ReviewSamples.ToArray();
                ReviewReport.captures = ReviewCaptures.ToArray();
                ReviewReport.cleanupComplete = !Resources.FindObjectsOfTypeAll<GameObject>()
                    .Any(candidate => candidate != null && candidate.name.StartsWith("Temporary_C02PlayerStill_", StringComparison.Ordinal));
                AddReviewCheck("temporary capture objects cleaned", ReviewReport.cleanupComplete,
                    ReviewReport.cleanupComplete ? "No temporary player still camera remains." : "A temporary player still camera remains.");
                if (status == "PASS_BOUNDED_RUNTIME" && ReviewChecks.Any(check => check.status != "PASS"))
                    status = "FINDINGS";
                ReviewReport.status = status;
                ReviewReport.checks = ReviewChecks.ToArray();
                Directory.CreateDirectory(RuntimeReviewOutput);
                File.WriteAllText(RuntimeReviewOutput + "/runtime_review.json", JsonUtility.ToJson(ReviewReport, true));
            }
            finally
            {
                ReviewWalker = null;
                ReviewAppearance = null;
                ReviewAnimator = null;
                ReviewSourceCamera = null;
            }
        }
    }
}
