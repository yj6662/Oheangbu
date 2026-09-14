using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Drawing;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>Real DevSceneFlow loads/restart while drawing. Does not save Play Mode state.</summary>
    public static class DosaSceneTransitionValidation
    {
        [Serializable] private sealed class Report
        {
            public string status = "RUNNING", note = "Real scene loading and restart. Drawing entry uses the existing mode method to isolate teardown; actual keyboard entry is covered by DosaInputPlayback.";
            public int transitions;
            public List<string> checks = new List<string>();
            public List<string> failures = new List<string>();
        }
        private static Report _report;
        private static string[] _paths;
        private static int _stage, _readyFrame;
        private static bool _running;
        private static bool _waitingSource;
        private static double _started;
        private static List<Object> _oldObjects;
        private static string _last = "NOT_RUN";
        public static string Begin()
        {
            if (_running) return Status();
            if (!Application.isPlaying) return "NOT_RUN: Play Mode required";
            if (DosaContextValidation.IsRunning || typeof(DosaInputPlayback).GetField("_active", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) != null)
                return "NOT_RUN: another input validation is running";
            if (Object.FindFirstObjectByType<PlayerVisualRig>() == null) return "NOT_RUN: installed Dosa rig required";
            _report = new Report(); _running = true; _stage = 0; _started = EditorApplication.timeSinceStartup;
            string original = SceneManager.GetActiveScene().path;
            _paths = new[] { DevSceneKit.HubScenePath, DevSceneKit.EffectLabScenePath, original, original };
            EditorApplication.update += Tick; AssemblyReloadEvents.beforeAssemblyReload += Abort;
            EditorApplication.playModeStateChanged += PlayChanged;
            try { StartTransition(); } catch (Exception e) { Fail(e.GetBaseException().Message); Finish(); }
            return Status();
        }
        public static string Status() => _running ? JsonUtility.ToJson(_report) : _last;
        private static void StartTransition()
        {
            var input = Object.FindFirstObjectByType<DrawingInputController>();
            if (input == null) throw new InvalidOperationException("Missing drawing controller before load");
            if (!input.InDrawMode)
                typeof(DrawingInputController).GetMethod("EnterMode", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(input, null);
            Check(input.InDrawMode, "Entry mode before transition " + _stage);
            _waitingSource = true;
            _readyFrame = Time.frameCount + 3;
        }
        private static void LoadTransition()
        {
            var input = Object.FindFirstObjectByType<DrawingInputController>();
            var visual = Object.FindFirstObjectByType<PlayerVisualRig>();
            Check(input != null && input.InDrawMode && visual != null && visual.Diagnostics.NearVisible,
                "Source drawing presentation evaluated before transition " + _stage);
            _oldObjects = new List<Object>();
            _oldObjects.AddRange(Object.FindObjectsByType<PlayerVisualRig>(FindObjectsSortMode.None).Select(r => (Object)r.gameObject));
            _oldObjects.AddRange(Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c => c.name == "DosaArmsCamera").Select(c => (Object)c.gameObject));
            if (_stage == 3) DevSceneFlow.Restart(); else DevSceneFlow.Load(_paths[_stage]);
            _waitingSource = false;
            _readyFrame = Time.frameCount + 4;
        }
        private static void Tick()
        {
            if (!_running || !Application.isPlaying || EditorApplication.isPaused) return;
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 90) throw new TimeoutException("Scene test timed out");
                if (Time.frameCount < _readyFrame) return;
                if (_waitingSource) { LoadTransition(); return; }
                if (SceneManager.GetActiveScene().path != _paths[_stage]) return;
                var rigs = Object.FindObjectsByType<PlayerVisualRig>(FindObjectsSortMode.None);
                var drivers = Object.FindObjectsByType<PlayerVisualDriver>(FindObjectsSortMode.None);
                var eyes = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c => c.name == "DosaArmsCamera").ToArray();
                string label = Path.GetFileNameWithoutExtension(_paths[_stage]) + (_stage == 3 ? " restart" : " load");
                Check(rigs.Length == 1 && drivers.Length == 1 && eyes.Length == 1, label + " rig/driver/near camera = 1/1/1");
                Check(_oldObjects.All(o => o == null), label + " previous body and overlay destroyed");
                Check(Mathf.Approximately(Time.timeScale, 1f), label + " timeScale restored to 1");
                var input = Object.FindFirstObjectByType<DrawingInputController>();
                Check(input != null && input.enabled && !input.InDrawMode && input.TotalPointCount == 0, label + " fresh empty input state");
                var main = Camera.main;
                Check(main != null && eyes.Length == 1 && main.GetUniversalAdditionalCameraData().cameraStack.Count(c => c != null) == 1
                    && main.GetUniversalAdditionalCameraData().cameraStack.Contains(eyes[0]), label + " correct single overlay stack");
                if (rigs.Length == 1 && drivers.Length == 1)
                {
                    Check(new SerializedObject(drivers[0]).FindProperty("_rig").objectReferenceValue == rigs[0], label + " driver points to new visual");
                    Check(eyes.Length == 1 && new SerializedObject(rigs[0]).FindProperty("_viewCamera").objectReferenceValue == eyes[0], label + " rig drives stacked camera");
                    var effects = Object.FindObjectsByType<HarvestInkStreamEffect>(FindObjectsSortMode.None);
                    Check(effects.Length == 1 && rigs[0].EffectTip != null, label + " harvest effect and final socket exist");
                    foreach (var effect in effects)
                        Check(new SerializedObject(effect).FindProperty("_sinkAnchor").objectReferenceValue == rigs[0].EffectTip, label + " harvest uses new tip");
                }
                _report.transitions++; _stage++;
                if (_stage >= _paths.Length) Finish(); else StartTransition();
            }
            catch (Exception e) { Fail(e.GetBaseException().Message); Finish(); }
        }
        private static void Check(bool condition, string message) { if (condition) _report.checks.Add(message); else Fail(message); }
        private static void Fail(string message) { _report.failures.Add(message); }
        private static void PlayChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) Abort(); }
        private static void Abort() { if (_running) { Fail("Interrupted"); Finish(); } }
        private static void Finish()
        {
            _running = false; EditorApplication.update -= Tick; AssemblyReloadEvents.beforeAssemblyReload -= Abort;
            EditorApplication.playModeStateChanged -= PlayChanged;
            if (Application.isPlaying)
            {
                var input = Object.FindFirstObjectByType<DrawingInputController>();
                if (input != null && input.InDrawMode)
                    typeof(DrawingInputController).GetMethod("ExitMode", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(input, new object[] { false, false });
                Time.timeScale = 1f;
                bool hasPlayer = Object.FindFirstObjectByType<PlayerMotor>() != null;
                Cursor.lockState = hasPlayer ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !hasPlayer;
            }
            _report.status = _report.failures.Count == 0 && _report.transitions == 4 ? "PASS" : "FAIL";
            _last = JsonUtility.ToJson(_report, true);
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosa"));
            Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "scene-transition-validation.json"), _last);
        }
    }
}
