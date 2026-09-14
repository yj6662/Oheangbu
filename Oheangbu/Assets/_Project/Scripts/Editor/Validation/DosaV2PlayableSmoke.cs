using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.BrushRender;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Drawing;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // Small actual-input smoke only. No animation authoring, physics suite or rig approval.
    public sealed class DosaV2PlayableSmoke
    {
        private const int Width = 1920, Height = 1080;
        private const float Duration = 9f;
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private struct InputStage { }
        private struct CaptureStage { }
        private static DosaV2PlayableSmoke _active;
        private static string _last = "NOT_STARTED";

        [Serializable] private sealed class Report
        {
            public string status = "STARTING", scene, directory, error;
            public string scope = "Nine-second actual input smoke; not RIG_PASS, animation or cloth validation.";
            public bool restored, v2World, v2Near, avatarsValid, rootMotionOff, moved, stopped, initialShoulder, shoulderHeldAfterV, drawingCloseup, shoulderRestoredAfterDrawing, drawingEntered, drawingExited;
            public int capturedPairs, rawPoints, rawMutationChecks, rawMutations, consoleErrors, nonDrawingFirstPersonFrames;
            public float elapsedSeconds, maxTravelMeters, maxActualCurvedTipErrorPixels, maxEffectTipDifferenceMeters;
            public string[] modelPaths;
            public List<string> errors = new List<string>();
            public List<Capture> captures = new List<Capture>();
        }
        [Serializable] private sealed class Capture
        {
            public string phase, play, observer;
            public float seconds, bendDegrees, bristleArcLengthMeters, actualTipErrorPixels = -1;
            public bool drawing, flexibleBristles;
            public Vector3 player, actualCurvedTip;
        }
        private sealed class DeviceScope
        {
            public InputActionAsset asset;
            public InputDevice[] devices;
            public bool dirty;
            public readonly List<(InputActionMap map, InputDevice[] devices)> maps = new List<(InputActionMap, InputDevice[])>();
        }
        private sealed class CameraState
        {
            public Camera camera; public RenderTexture target; public Rect rect; public float aspect, fov;
            public Vector3 localPosition; public Quaternion localRotation;
        }
        private struct RawSnapshot { public StrokeData stroke; public int index; public StrokePoint point; }
        private readonly Report _report = new Report();
        private readonly List<DeviceScope> _scopes = new List<DeviceScope>();
        private readonly List<InputDevice> _disabled = new List<InputDevice>();
        private readonly List<CameraState> _cameras = new List<CameraState>();
        private readonly List<RawSnapshot> _raw = new List<RawSnapshot>();
        private PlayerMotor _motor; private CharacterController _controller; private CameraRigController _cameraRig;
        private CombatConfigSO _cameraConfig;
        private DrawingInputController _drawing; private PlayerVisualRig _rig;
        private Camera _main, _observer; private RenderTexture _target; private Texture2D _pixels;
        private Keyboard _keyboard, _previousKeyboard; private Mouse _mouse, _previousMouse;
        private InputSettings.UpdateMode _updateMode; private InputSettings.BackgroundBehavior _background;
        private InputSettings.EditorInputBehaviorInPlayMode _editorInput;
        private Vector3 _position, _cameraBlend; private Quaternion _rotation, _pivotRotation;
        private Transform _pivot; private float _pitch, _verticalVelocity, _boom, _timeScale, _captureDelta, _start;
        private bool _runBackground, _shoulder, _cursorVisible, _stateCaptured, _finished, _toggleSent, _interrupted;
        private CursorLockMode _cursorLock;
        private int _inputFrame = -1, _captureFrame = -1, _nextCapture;

        public static string Begin()
        {
            if (_active != null) return Status();
            if (!Application.isPlaying || EditorApplication.isPaused) return "NOT_RUN: unpaused Play Mode required";
            if (SceneManager.GetActiveScene().name != "C2_CodexWorld") return "NOT_RUN: C2_CodexWorld required";
            if (DosaContextValidation.IsRunning || DosaInputPlayback.Status().Contains("\"status\":\"RUNNING\""))
                return "NOT_RUN: another Dosa playback is active";
            var run = new DosaV2PlayableSmoke(); _active = run;
            try { run.Initialise(); run._report.status = "RUNNING"; }
            catch (Exception e) { run.Finish("ERROR", e.GetBaseException().Message); }
            return Status();
        }
        public static string Status() => _active != null ? JsonUtility.ToJson(_active._report) : _last;
        public static string Stop() { _active?.Finish("STOPPED", null); return Status(); }

        private void Initialise()
        {
            _motor = Object.FindFirstObjectByType<PlayerMotor>(); _drawing = Object.FindFirstObjectByType<DrawingInputController>();
            _rig = Object.FindFirstObjectByType<PlayerVisualRig>(); _cameraRig = Object.FindFirstObjectByType<CameraRigController>(); _main = Camera.main;
            if (_motor == null || _drawing == null || _rig == null || _cameraRig == null || _main == null)
                throw new InvalidOperationException("Player, drawing, visual rig or camera missing");
            if (_drawing.InDrawMode || !_drawing.enabled || !_motor.enabled) throw new InvalidOperationException("Begin from idle with input enabled");
            _controller = _motor.GetComponent<CharacterController>();
            _report.scene = SceneManager.GetActiveScene().path;
            _report.modelPaths = _rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => AssetDatabase.GetAssetPath(r.sharedMesh)).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray();
            _report.v2World = _report.modelPaths.Contains(DosaV2PlayerBuilder.WorldModel); _report.v2Near = _report.modelPaths.Contains(DosaV2PlayerBuilder.ArmsModel);
            var animators = _rig.GetComponentsInChildren<Animator>(true);
            _report.avatarsValid = animators.Length >= 2 && animators.All(a => a.avatar != null && a.avatar.isValid && a.avatar.isHuman);
            _report.rootMotionOff = animators.All(a => !a.applyRootMotion);
            if (!_report.v2World || !_report.v2Near || !_report.avatarsValid || !_report.rootMotionOff)
                throw new InvalidOperationException("V2 world/near model, Humanoid avatar or root-motion-off check failed");

            _position = _motor.transform.position; _rotation = _motor.transform.rotation;
            _pitch = Get<float>(_motor, "_pitch"); _verticalVelocity = Get<float>(_motor, "_verticalVelocity");
            _pivot = Get<Transform>(_motor, "_cameraPivot"); _pivotRotation = _pivot.localRotation;
            _shoulder = _cameraRig.IsShoulder; _cameraBlend = Get<Vector3>(_cameraRig, "_localPose"); _boom = Get<float>(_cameraRig, "_boom01");
            _cameraConfig = Get<CombatConfigSO>(_cameraRig, "_config");
            if (_cameraConfig == null) throw new InvalidOperationException("Camera configuration missing");
            _report.initialShoulder = _cameraRig.IsShoulder && !_cameraRig.IsDrawingCloseup;
            _updateMode = InputSystem.settings.updateMode; _background = InputSystem.settings.backgroundBehavior;
            _editorInput = InputSystem.settings.editorInputBehaviorInPlayMode; _runBackground = Application.runInBackground;
            _timeScale = Time.timeScale; _captureDelta = Time.captureDeltaTime; _cursorLock = Cursor.lockState; _cursorVisible = Cursor.visible;
            _previousKeyboard = Keyboard.current; _previousMouse = Mouse.current; _stateCaptured = true;
            _report.directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2", "playable-smoke-" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")));
            Directory.CreateDirectory(Path.Combine(_report.directory, "play")); Directory.CreateDirectory(Path.Combine(_report.directory, "observer"));
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Application.runInBackground = true;
            foreach (var d in InputSystem.devices.ToArray()) if (d.enabled && (d is Keyboard || d is Mouse || d is Gamepad)) { _disabled.Add(d); InputSystem.DisableDevice(d); }
            _keyboard = InputSystem.AddDevice<Keyboard>("DosaV2SmokeKeyboard"); _mouse = InputSystem.AddDevice<Mouse>("DosaV2SmokeMouse");
            Scope(Get<InputActionAsset>(_motor, "_actions")); Scope(Get<InputActionAsset>(_drawing, "_actions"));
            _target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "DosaV2SmokeCapture" }; _target.Create();
            _pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false); HoldCamera(_main);
            foreach (var camera in _main.GetUniversalAdditionalCameraData().cameraStack) if (camera != null) HoldCamera(camera);
            _observer = new GameObject("DosaV2SmokeObserver", typeof(Camera)) { hideFlags = HideFlags.DontSave }.GetComponent<Camera>();
            _observer.CopyFrom(_main); _observer.enabled = false; _observer.tag = "Untagged"; _observer.cullingMask &= ~(1 << 31); _observer.fieldOfView = 38; _observer.nearClipPlane = .05f;
            var observerData = _observer.GetUniversalAdditionalCameraData(); observerData.renderType = CameraRenderType.Base; observerData.cameraStack.Clear(); observerData.renderPostProcessing = _main.GetUniversalAdditionalCameraData().renderPostProcessing;
            _drawing.StrokePointAdded += PointAdded; Application.logMessageReceived += Logged;
            EditorApplication.playModeStateChanged += PlayChanged; AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (!Insert(ref loop, typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate), new PlayerLoopSystem { type = typeof(InputStage), updateDelegate = Tick }, false) ||
                !Insert(ref loop, typeof(UnityEngine.PlayerLoop.PostLateUpdate.PhysicsSkinnedClothFinishUpdate), new PlayerLoopSystem { type = typeof(CaptureStage), updateDelegate = AfterSkin }, true))
                throw new InvalidOperationException("Actual Update/PhysicsSkinnedClothFinishUpdate PlayerLoop stages unavailable; no earlier capture fallback is permitted");
            _start = Time.unscaledTime; PlayerLoop.SetPlayerLoop(loop);
        }

        private void Tick()
        {
            if (_finished || _inputFrame == Time.frameCount) return; _inputFrame = Time.frameCount;
            try
            {
                float t = Time.unscaledTime - _start; _report.elapsedSeconds = t;
                if (t >= Duration) { Finish("COMPLETE", null); return; }
                var keys = new List<Key>(3); if (t >= 1 && t < 2.2f) keys.Add(Key.W);
                // V is a rejection probe: non-drawing view must remain shoulder.
                if (t >= 3.2f && !_toggleSent) { keys.Add(Key.V); _toggleSent = true; }
                if (t >= 4 && t < 7.2f) keys.Add(Key.Q);
                float stroke = Mathf.Clamp01((t - 4.8f) / 2f);
                var point = new Vector2(Width * Mathf.Lerp(.43f, .57f, stroke), Height * (.53f + .04f * Mathf.Sin(stroke * Mathf.PI)));
                InputSystem.QueueStateEvent(_mouse, new MouseState { position = point }.WithButton(MouseButton.Left, t >= 4.8f && t < 6.8f));
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys.ToArray())); InputSystem.Update(); _keyboard.MakeCurrent(); _mouse.MakeCurrent();
                // Cancel the test letter through the existing gameplay API before Q release;
                // this smoke must not spend ink or create a spell while restoring the scene.
                if (t >= 6.9f && !_interrupted) { _drawing.InterruptLetter(); _interrupted = true; }
            }
            catch (Exception e) { Finish("ERROR", e.GetBaseException().Message); }
        }

        private void AfterSkin()
        {
            if (_finished || _inputFrame != Time.frameCount || _captureFrame == Time.frameCount) return; _captureFrame = Time.frameCount;
            try
            {
                float t = _report.elapsedSeconds;
                foreach (var s in _raw)
                {
                    _report.rawMutationChecks++;
                    if (s.index >= s.stroke.Points.Count) { _report.rawMutations++; continue; }
                    var p = s.stroke.Points[s.index]; if (p.Position.x != s.point.Position.x || p.Position.y != s.point.Position.y || p.Time != s.point.Time) _report.rawMutations++;
                }
                _raw.Clear();
                float travel = Vector3.Distance(_position, _motor.transform.position); _report.maxTravelMeters = Mathf.Max(_report.maxTravelMeters, travel);
                _report.moved |= t > 1 && travel > .05f; _report.stopped |= t > 2.5f && t < 3.2f && new Vector2(_controller.velocity.x, _controller.velocity.z).magnitude < .05f;
                _report.drawingEntered |= _drawing.InDrawMode; _report.drawingExited |= t > 7.3f && _report.drawingEntered && !_drawing.InDrawMode;
                if (!_drawing.InDrawMode && (!_cameraRig.IsShoulder || _cameraRig.IsDrawingCloseup)) _report.nonDrawingFirstPersonFrames++;
                Vector3 cameraPose = Get<Vector3>(_cameraRig, "_localPose");
                bool shoulderPose = _cameraRig.IsShoulder && !_cameraRig.IsDrawingCloseup && Vector3.Distance(cameraPose, _cameraConfig.ShoulderOffset) < .01f;
                float baseFov = Get<float>(_cameraRig, "_baseFov");
                _report.shoulderHeldAfterV |= _toggleSent && t > 3.35f && t < 4f && shoulderPose;
                _report.drawingCloseup |= _drawing.InDrawMode && _cameraRig.IsDrawingCloseup && !_cameraRig.IsBodyVisible
                    && Vector3.Distance(cameraPose, _cameraConfig.ShoulderDrawOffset) < .01f
                    && Mathf.Abs(_main.fieldOfView - (_cameraConfig.DrawCloseupFov > 0 ? _cameraConfig.DrawCloseupFov : baseFov)) < .01f;
                _report.shoulderRestoredAfterDrawing |= t > 8.5f && _report.drawingExited && shoulderPose && Mathf.Abs(_main.fieldOfView - baseFov) < .01f;
                if (_nextCapture < 3 && t >= new[] { .8f, 2f, 6f }[_nextCapture]) { CapturePair(new[] { "idle", "move", "drawing" }[_nextCapture]); _nextCapture++; }
            }
            catch (Exception e) { Finish("ERROR", e.GetBaseException().Message); }
        }

        private void CapturePair(string phase)
        {
            var r = new Capture { phase = phase, seconds = _report.elapsedSeconds, player = _motor.transform.position, drawing = _drawing.InDrawMode };
            var bristles = _rig.GetComponentsInChildren<BrushBristleRig>(true).Where(b => b.IsBound).OrderBy(b => _rig.EffectTip != null ? Vector3.SqrMagnitude(b.ActualTipWorld - _rig.EffectTip.position) : float.MaxValue).FirstOrDefault();
            if (bristles != null)
            {
                r.flexibleBristles = true; r.actualCurvedTip = bristles.ActualTipWorld; r.bendDegrees = bristles.Pose.BendDegrees; r.bristleArcLengthMeters = bristles.Pose.BristleArcLength;
                if (_rig.EffectTip != null) _report.maxEffectTipDifferenceMeters = Mathf.Max(_report.maxEffectTipDifferenceMeters, Vector3.Distance(r.actualCurvedTip, _rig.EffectTip.position));
                if (r.drawing && _drawing.HasPointer) { Vector3 screen = _main.WorldToScreenPoint(r.actualCurvedTip); r.actualTipErrorPixels = Vector2.Distance(screen, _drawing.PointerScreenPosition); _report.maxActualCurvedTipErrorPixels = Mathf.Max(_report.maxActualCurvedTipErrorPixels, r.actualTipErrorPixels); }
            }
            r.play = Path.Combine(_report.directory, "play", phase + ".png"); r.observer = Path.Combine(_report.directory, "observer", phase + ".png");
            var renderers = _rig.GetComponentsInChildren<Renderer>(true).Select(x => (r: x, enabled: x.enabled, shadows: x.shadowCastingMode)).ToArray();
            // Native Cloth owns its final rendered surface. Only ordinary skinned
            // meshes need the manual-camera matrix refresh used by the old replay.
            var skins = _rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(x => x.GetComponent<Cloth>() == null).Select(x => (r: x, force: x.forceMatrixRecalculationPerRender)).ToArray();
            var ink = Object.FindObjectsByType<BrushStrokeRenderer>(FindObjectsSortMode.None).SelectMany(x => x.GetComponentsInChildren<Renderer>()).Distinct().Select(x => (r: x, enabled: x.enabled)).ToArray();
            try
            {
                foreach (var s in skins) s.r.forceMatrixRecalculationPerRender = true; Render(_main, r.play);
                foreach (var s in renderers) { s.r.enabled = s.r.gameObject.layer != 31; if (s.r.enabled) s.r.shadowCastingMode = ShadowCastingMode.On; }
                foreach (var s in ink) s.r.enabled = false;
                Vector3 focus = _motor.transform.position + Vector3.up * .05f;
                _observer.transform.position = focus + _motor.transform.forward * 3f + _motor.transform.right * 2.4f + Vector3.up * .6f; _observer.transform.LookAt(focus); Render(_observer, r.observer);
            }
            finally
            {
                foreach (var s in renderers) if (s.r != null) { s.r.enabled = s.enabled; s.r.shadowCastingMode = s.shadows; }
                foreach (var s in skins) if (s.r != null) s.r.forceMatrixRecalculationPerRender = s.force;
                foreach (var s in ink) if (s.r != null) s.r.enabled = s.enabled;
            }
            _report.captures.Add(r); _report.capturedPairs++;
        }

        private void Render(Camera camera, string path)
        {
            var active = RenderTexture.active;
            try { if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = _target }); else camera.Render(); RenderTexture.active = _target; _pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false); _pixels.Apply(false, false); File.WriteAllBytes(path, _pixels.EncodeToPNG()); }
            finally { RenderTexture.active = active; }
        }
        private void PointAdded(Vector2 point) { var s = Get<StrokeData>(_drawing, "_currentStroke"); if (s == null || s.Points.Count == 0) return; _raw.Add(new RawSnapshot { stroke = s, index = s.Points.Count - 1, point = s.Points[s.Points.Count - 1] }); _report.rawPoints++; }
        private void Logged(string message, string stack, LogType type) { if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return; _report.consoleErrors++; if (_report.errors.Count < 8) _report.errors.Add(message); }
        private void PlayChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) Finish("PLAY_EXIT", null); }
        private void BeforeReload() => Finish("DOMAIN_RELOAD", null);
        private void Scope(InputActionAsset asset)
        {
            if (asset == null || _scopes.Any(s => s.asset == asset)) return;
            var s = new DeviceScope { asset = asset, devices = asset.devices?.ToArray(), dirty = EditorUtility.IsDirty(asset) }; _scopes.Add(s); var devices = new InputDevice[] { _keyboard, _mouse }; asset.devices = devices;
            foreach (var map in asset.actionMaps) { s.maps.Add((map, map.devices?.ToArray())); map.devices = devices; }
        }
        private void HoldCamera(Camera camera) { if (_cameras.Any(c => c.camera == camera)) return; _cameras.Add(new CameraState { camera = camera, target = camera.targetTexture, rect = camera.rect, aspect = camera.aspect, fov = camera.fieldOfView, localPosition = camera.transform.localPosition, localRotation = camera.transform.localRotation }); camera.targetTexture = _target; camera.rect = new Rect(0, 0, 1, 1); camera.aspect = Width / (float)Height; }

        private void Finish(string status, string error)
        {
            if (_finished) return; _finished = true; _report.status = status; _report.error = error;
            Application.logMessageReceived -= Logged; EditorApplication.playModeStateChanged -= PlayChanged; AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            var loop = PlayerLoop.GetCurrentPlayerLoop(); if (Remove(ref loop)) PlayerLoop.SetPlayerLoop(loop);
            if (_drawing != null) _drawing.StrokePointAdded -= PointAdded;
            var failures = new List<string>();
            void Restore(Action action) { try { action(); } catch (Exception e) { failures.Add(e.GetBaseException().Message); } }
            if (_stateCaptured)
            {
                Restore(() => { if (_drawing != null && _drawing.InDrawMode) { _drawing.enabled = false; _drawing.enabled = true; } });
                foreach (var s in _scopes) Restore(() => { if (s.asset == null) return; s.asset.devices = s.devices; foreach (var m in s.maps) m.map.devices = m.devices; if (!s.dirty) EditorUtility.ClearDirty(s.asset); });
                foreach (var d in new InputDevice[] { _keyboard, _mouse }) Restore(() => { if (d != null && d.added) InputSystem.RemoveDevice(d); });
                foreach (var d in _disabled) Restore(() => { if (d != null && d.added) InputSystem.EnableDevice(d); });
                Restore(() => { if (_previousKeyboard != null && _previousKeyboard.added) _previousKeyboard.MakeCurrent(); if (_previousMouse != null && _previousMouse.added) _previousMouse.MakeCurrent(); });
                Restore(() => { InputSystem.settings.updateMode = _updateMode; InputSystem.settings.backgroundBehavior = _background; InputSystem.settings.editorInputBehaviorInPlayMode = _editorInput; Application.runInBackground = _runBackground; Time.timeScale = _timeScale; Time.captureDeltaTime = _captureDelta; });
                Restore(() => { bool enabled = _controller.enabled; _controller.enabled = false; _motor.transform.SetPositionAndRotation(_position, _rotation); _controller.enabled = enabled; Set(_motor, "_pitch", _pitch); Set(_motor, "_verticalVelocity", _verticalVelocity); _pivot.localRotation = _pivotRotation; Set(_cameraRig, "_shoulder", _shoulder); Set(_cameraRig, "_localPose", _cameraBlend); Set(_cameraRig, "_boom01", _boom); });
                foreach (var c in _cameras) Restore(() => { if (c.camera == null) return; c.camera.targetTexture = c.target; c.camera.rect = c.rect; c.camera.aspect = c.aspect; c.camera.fieldOfView = c.fov; c.camera.transform.SetLocalPositionAndRotation(c.localPosition, c.localRotation); });
                Cursor.lockState = _cursorLock; Cursor.visible = _cursorVisible;
            }
            Restore(() => { if (_observer != null) Object.Destroy(_observer.gameObject); if (_target != null) { _target.Release(); Object.Destroy(_target); } if (_pixels != null) Object.Destroy(_pixels); });
            _report.restored = failures.Count == 0; if (failures.Count != 0) _report.error += " RESTORE: " + string.Join("; ", failures);
            if (status == "COMPLETE" && (!_report.moved || !_report.stopped || !_report.initialShoulder || !_report.shoulderHeldAfterV || !_report.drawingCloseup || !_report.shoulderRestoredAfterDrawing || _report.nonDrawingFirstPersonFrames != 0 || !_report.drawingEntered || !_report.drawingExited || _report.rawPoints == 0 || _report.rawMutations != 0 || _report.consoleErrors != 0 || _report.capturedPairs != 3 || !_report.captures.Any(c => c.drawing && c.flexibleBristles) || !_report.restored)) _report.status = "INCOMPLETE_SMOKE";
            _last = JsonUtility.ToJson(_report); _active = null;
            if (!string.IsNullOrEmpty(_report.directory)) File.WriteAllText(Path.Combine(_report.directory, "summary.json"), _last);
        }
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Fields).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
        private static bool Insert(ref PlayerLoopSystem parent, Type anchor, PlayerLoopSystem stage, bool after)
        {
            if (parent.subSystemList == null) return false;
            for (int i = 0; i < parent.subSystemList.Length; i++) { var child = parent.subSystemList[i]; if (child.type == anchor) { var list = parent.subSystemList.ToList(); list.Insert(i + (after ? 1 : 0), stage); parent.subSystemList = list.ToArray(); return true; } if (Insert(ref child, anchor, stage, after)) { var children = (PlayerLoopSystem[])parent.subSystemList.Clone(); children[i] = child; parent.subSystemList = children; return true; } }
            return false;
        }
        private static bool Remove(ref PlayerLoopSystem parent)
        {
            if (parent.subSystemList == null) return false; bool changed = false; var children = new List<PlayerLoopSystem>();
            foreach (var entry in parent.subSystemList) { if (entry.type == typeof(InputStage) || entry.type == typeof(CaptureStage)) { changed = true; continue; } var child = entry; changed |= Remove(ref child); children.Add(child); }
            if (changed) parent.subSystemList = children.ToArray(); return changed;
        }
    }
}
