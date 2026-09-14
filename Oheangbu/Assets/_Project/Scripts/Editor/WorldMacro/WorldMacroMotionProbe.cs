using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Short actual inspection-camera movement check. No video, gameplay or vehicle simulation.</summary>
    [InitializeOnLoad]
    public static class WorldMacroMotionProbe
    {
        [Serializable] public sealed class Segment
        {
            public string routeId, status;
            public bool carriage;
            public float expectedSpeedMps, measuredSpeedMps, durationGameSeconds, measuredTravelM, maxClearanceErrorM;
            public int frames, missingGround;
        }
        [Serializable] public sealed class Report
        {
            public string status, detail;
            public string scope = "Actual WorldMacroReviewController camera motion and terrain/bridge colliders in Play Mode. Inspection only; no player or vehicle physics. No video.";
            public Segment[] segments = Array.Empty<Segment>();
        }
        static readonly FieldInfo DistanceField = typeof(WorldMacroReviewController).GetField("distance", BindingFlags.Instance | BindingFlags.NonPublic);
        static WorldMacroReviewController controller;
        static readonly List<Segment> Results = new List<Segment>();
        static int savedMode, savedRoute, currentIndex, previousFrame;
        static float savedDistance, previousGameTime, accumulatedTime, accumulatedDistance, maxClearanceError;
        static Vector3 savedPosition, previousPosition;
        static Quaternion savedRotation;
        static int frames, groundMisses;
        static int[] routes;
        static double deadline;
        static bool running, warming;
        const float StartOffset = 60f;
        static string reportPath => Path.Combine(WorldMacroBuilder.Output, "motion_probe.json");

        static WorldMacroMotionProbe()
        {
            EditorApplication.playModeStateChanged += state => { if (running && state == PlayModeStateChange.ExitingPlayMode) Finish("INCOMPLETE", "Play Mode exited before the sample completed."); };
            AssemblyReloadEvents.beforeAssemblyReload += () => { if (running) Finish("INCOMPLETE", "Assembly reload interrupted the sample."); };
        }

        public static string Begin()
        {
            if (running) return "RUNNING";
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) throw new InvalidOperationException("Start unpaused Play Mode in the macro scene first.");
            controller = Object.FindFirstObjectByType<WorldMacroReviewController>();
            if (controller == null || controller.Sheet == null || DistanceField == null) throw new InvalidOperationException("Macro inspection controller unavailable.");
            int carriage = FindRoute("Road_NorthPass_Bridge", true), walk = FindRoute("Trail_Inn_Logging", false);
            if (carriage < 0 || walk < 0) throw new InvalidOperationException("Both carriage and footpath inspection routes are required.");
            savedMode = controller.Mode; savedRoute = controller.RouteIndex;
            savedDistance = (float)DistanceField.GetValue(controller); savedPosition = controller.transform.position; savedRotation = controller.transform.rotation;
            Results.Clear(); routes = new[] { carriage, walk }; currentIndex = 0; running = true;
            deadline = EditorApplication.timeSinceStartup + 24;
            EditorApplication.update += Tick;
            StartSegment();
            Write(new Report { status = "RUNNING", detail = "Sampling two 3-second inspection-camera segments." });
            return "RUNNING: carriage 14m/s and footpath 4.5m/s; actual camera motion, no recording.";
        }

        static int FindRoute(string id, bool carriage)
        {
            var list = controller.Sheet.Routes;
            for (int i = 0; i < list.Length; i++) if (list[i].Id == id) return i;
            for (int i = 0; i < list.Length; i++) if (list[i].Carriage == carriage) return i;
            return -1;
        }
        static void StartSegment()
        {
            controller.Mode = 2; controller.RouteIndex = routes[currentIndex]; DistanceField.SetValue(controller, StartOffset);
            accumulatedTime = 0; accumulatedDistance = 0; maxClearanceError = 0; frames = 0; groundMisses = 0;
            previousFrame = -1; warming = true;
        }
        static void Tick()
        {
            if (!running) return;
            try
            {
                if (controller == null || !EditorApplication.isPlaying) { Finish("INCOMPLETE", "Controller or Play Mode became unavailable."); return; }
                if (EditorApplication.timeSinceStartup >= deadline) { Finish("INCOMPLETE", "24-second wall-time limit; no forced time-scale or frame-rate changes."); return; }
                if (Time.frameCount == previousFrame) return;
                previousFrame = Time.frameCount;
                var position = controller.transform.position;
                float now = Time.time;
                if (warming)
                {
                    var start = WorldMacroReviewController.Along(controller.Sheet.Routes[routes[currentIndex]].Points, StartOffset, out _);
                    if ((float)DistanceField.GetValue(controller) <= StartOffset || Vector2.Distance(new Vector2(position.x, position.z), new Vector2(start.x, start.z)) > 50f) return;
                    warming = false; previousGameTime = now; previousPosition = position; return;
                }
                float dt = now - previousGameTime;
                if (dt <= 0) return;
                accumulatedTime += dt; accumulatedDistance += Vector3.Distance(previousPosition, position); frames++;
                if (Physics.Raycast(new Vector3(position.x, 2000, position.z), Vector3.down, out var hit, 4000, 1, QueryTriggerInteraction.Ignore))
                    maxClearanceError = Mathf.Max(maxClearanceError, Mathf.Abs(position.y - hit.point.y - controller.EyeHeight));
                else groundMisses++;
                previousGameTime = now; previousPosition = position;
                if (accumulatedTime < 3f) return;
                var route = controller.Sheet.Routes[routes[currentIndex]];
                float expected = route.Carriage ? controller.Sheet.CarriageSpeed : controller.Sheet.WalkSpeed;
                float measured = accumulatedDistance / accumulatedTime;
                bool pass = Mathf.Abs(measured - expected) <= expected * .05f && maxClearanceError <= .035f && groundMisses == 0 && frames >= 15;
                Results.Add(new Segment { routeId = route.Id, carriage = route.Carriage, expectedSpeedMps = expected, measuredSpeedMps = measured,
                    durationGameSeconds = accumulatedTime, measuredTravelM = accumulatedDistance, frames = frames, missingGround = groundMisses,
                    maxClearanceErrorM = maxClearanceError, status = pass ? "PASS" : "FAIL" });
                currentIndex++;
                if (currentIndex < routes.Length) { StartSegment(); return; }
                bool allPass = Results.TrueForAll(s => s.status == "PASS");
                Finish(allPass ? "PASS" : "COMPLETED_WITH_FINDINGS", "Each route starts 60m after its POI. Speed tolerance ±5%; ground-clearance error ≤3.5cm. No time-scale, FPS, input-device or scene asset changes.");
            }
            catch (Exception exception) { Finish("ERROR", exception.ToString()); }
        }
        public static string Poll() => running ? "RUNNING " + Results.Count + "/2" : File.Exists(reportPath) ? File.ReadAllText(reportPath) : "NOT_RUN";
        static void Finish(string status, string detail)
        {
            running = false; EditorApplication.update -= Tick;
            if (controller != null)
            {
                controller.Mode = savedMode; controller.RouteIndex = savedRoute; DistanceField.SetValue(controller, savedDistance);
                controller.transform.SetPositionAndRotation(savedPosition, savedRotation);
            }
            Write(new Report { status = status, detail = detail, segments = Results.ToArray() });
            controller = null;
        }
        static void Write(Report report)
        {
            Directory.CreateDirectory(WorldMacroBuilder.Output);
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
        }
    }
}
