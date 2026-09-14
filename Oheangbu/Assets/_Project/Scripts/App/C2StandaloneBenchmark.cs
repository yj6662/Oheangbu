using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.Drawing;
using Oheangbu.Combat;
using Oheangbu.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.LowLevel;

namespace Oheangbu.App
{
    // Explicit command-line benchmark only; never created during ordinary play.
    public sealed class C2StandaloneBenchmark : MonoBehaviour
    {
        [Serializable] public sealed class Phase { public string name; public int frames; public double meanMs, p95Ms, p99Ms, fps, gpuMs, cpuMainMs, cpuRenderMs; public int timingSamples; public float travel; }
        [Serializable] public sealed class Report { public string status, hardware, graphicsApi, unity, output, scope; public int width, height, errors, renderedCameraFrames; public bool clothEnabled, drawingSeen, shoulderRestored, frameTimingEnabled; public List<Phase> phases = new List<Phase>(); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-c2-benchmark");
            if (index < 0 || index + 1 >= args.Length || SceneManager.GetActiveScene().name != "C2_CodexWorld") return;
            new GameObject("C2_Standalone_Benchmark").AddComponent<C2StandaloneBenchmark>().output = args[index + 1];
        }
        string output; Report report; PlayerMotor motor; DrawingInputController drawing; CameraRigController cameraRig;
        Camera renderCamera; RenderTexture renderTarget; Texture2D syncPixel; RenderPipeline.StandardRequest renderRequest; struct RenderStage { }
        Keyboard keyboard; Mouse mouse; readonly List<double> samples = new List<double>(); readonly FrameTiming[] timings = new FrameTiming[1];
        int phase = -1; double start; Vector3 origin; bool interrupted;
        Cloth[] benchmarkCloth; bool referenceCloth;
        readonly string[] names = { "idle_shoulder", "walk_forward", "drawing", "idle_return" };
        void Start()
        {
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; Application.runInBackground = true;
            motor = FindFirstObjectByType<PlayerMotor>(); drawing = FindFirstObjectByType<DrawingInputController>(); cameraRig = FindFirstObjectByType<CameraRigController>();
            report = new Report { status = "RUNNING", hardware = SystemInfo.processorType + " / " + SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(), unity = Application.unityVersion, output = output,
                scope = "Standalone C2, 1920x1080 requested, vSync0, uncapped. Four phases with 3 seconds warmup and 5 seconds measurements each. Real virtual keyboard/mouse input, original movement/drawing/physics. GPU timings0 mean unavailable. Benchmark process exits after report.",
                clothEnabled = FindObjectsByType<Cloth>(FindObjectsSortMode.None).All(c => c.enabled) };
            Directory.CreateDirectory(Path.GetDirectoryName(output)); Application.logMessageReceived += Logged;
            report.frameTimingEnabled = FrameTimingManager.IsFeatureEnabled();
            report.scope = "C2 standalone, explicit full camera-stack rendering to1920x1080 after native Cloth, one-pixel synchronous GPU readback each frame. Hidden-window skipped rendering is disallowed. CPU/GPU serialize at readback, so this is a conservative offscreen render benchmark, not normal presentation FPS. 3s warmup +5s sampling per phase.";
            // Keep the gameplay camera enabled: representation setup also discovers Camera.main.
            // Hidden windows skip automatic rendering, so the explicit request remains required.
            renderCamera = Camera.main;
            benchmarkCloth = FindObjectsByType<Cloth>(FindObjectsSortMode.None);
            referenceCloth = Array.IndexOf(Environment.GetCommandLineArgs(), "-c2-cloth-120") >= 0;
            renderTarget = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32); renderTarget.Create();
            syncPixel = new Texture2D(1, 1, TextureFormat.RGB24, false); renderRequest = new RenderPipeline.StandardRequest { destination = renderTarget };
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (!InsertRender(ref loop)) throw new InvalidOperationException("Native Cloth render stage unavailable");
            PlayerLoop.SetPlayerLoop(loop);
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard = InputSystem.AddDevice<Keyboard>("C2BenchKeyboard"); mouse = InputSystem.AddDevice<Mouse>("C2BenchMouse");
            foreach (var owner in new object[] { motor, drawing })
            {
                var field = owner.GetType().GetField("_actions", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field?.GetValue(owner) is InputActionAsset actions) actions.devices = new InputDevice[] { keyboard, mouse };
            }
            Next();
        }
        void Next()
        {
            phase++; if (phase == names.Length) { Finish(); return; }
            samples.Clear(); start = Time.realtimeSinceStartupAsDouble; origin = motor.transform.position;
            report.phases.Add(new Phase { name = names[phase] }); interrupted = false;
        }
        void Update()
        {
            if (report == null || phase >= names.Length) return;
            if (referenceCloth) foreach (var cloth in benchmarkCloth) cloth.clothSolverFrequency = 120f;
            double elapsed = Time.realtimeSinceStartupAsDouble - start;
            bool draw = phase == 2 && elapsed < 7.5;
            InputSystem.QueueStateEvent(keyboard, phase == 1 ? new KeyboardState(Key.W) : draw ? new KeyboardState(Key.Q) : new KeyboardState());
            var pointer = new Vector2(Screen.width * (.5f + .12f * Mathf.Sin((float)elapsed * 2)), Screen.height * (.5f + .09f * Mathf.Cos((float)elapsed * 2)));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer }.WithButton(MouseButton.Left, draw && elapsed > .5 && elapsed < 7.2));
            if (phase == 2 && elapsed >= 7.25 && !interrupted) { drawing.InterruptLetter(); interrupted = true; }
            report.drawingSeen |= drawing.InDrawMode;
            if (phase == 3) report.shoulderRestored |= cameraRig.IsShoulder && !cameraRig.IsDrawingCloseup;
            FrameTimingManager.CaptureFrameTimings();
            if (elapsed > 3)
            {
                var p = report.phases[phase]; p.frames++; samples.Add(Time.unscaledDeltaTime * 1000d); p.travel = Vector3.Distance(origin, motor.transform.position);
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0)
                { p.timingSamples++; p.gpuMs += timings[0].gpuFrameTime; p.cpuMainMs += timings[0].cpuMainThreadFrameTime; p.cpuRenderMs += timings[0].cpuRenderThreadFrameTime; }
            }
            if (elapsed < 8) return;
            var result = report.phases[phase]; samples.Sort(); result.meanMs = samples.Average(); result.fps = 1000 / result.meanMs;
            result.p95Ms = samples[Math.Min(samples.Count - 1, (int)(samples.Count * .95))]; result.p99Ms = samples[Math.Min(samples.Count - 1, (int)(samples.Count * .99))];
            if (result.timingSamples > 0) { result.gpuMs /= result.timingSamples; result.cpuMainMs /= result.timingSamples; result.cpuRenderMs /= result.timingSamples; }
            File.WriteAllText(output, JsonUtility.ToJson(report, true)); Next();
        }
        void Finish()
        {
            report.status = report.errors == 0 ? "COMPLETE" : "ERRORS"; report.width = Screen.width; report.height = Screen.height;
            InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse);
            File.WriteAllText(output, JsonUtility.ToJson(report, true)); Application.Quit(report.errors == 0 ? 0 : 2);
        }
        int capturedPhase = -1;
        void RenderFrame()
        {
            if (report == null || phase < 0 || phase >= names.Length) return;
            renderCamera.aspect = 1920f / 1080f;
            RenderPipeline.SubmitRenderRequest(renderCamera, renderRequest);
            var previous = RenderTexture.active; RenderTexture.active = renderTarget;
            syncPixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
            report.renderedCameraFrames++;
            if (phase != capturedPhase && Time.realtimeSinceStartupAsDouble - start >= 1)
            {
                capturedPhase = phase; var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0, false); pixels.Apply(false, false);
                File.WriteAllBytes(Path.ChangeExtension(output, null) + "-" + names[phase] + ".png", pixels.EncodeToPNG()); Destroy(pixels);
            }
            RenderTexture.active = previous;
        }
        bool InsertRender(ref PlayerLoopSystem parent)
        {
            if (parent.subSystemList == null) return false;
            for (int i = 0; i < parent.subSystemList.Length; i++)
            {
                if (parent.subSystemList[i].type == typeof(UnityEngine.PlayerLoop.PostLateUpdate.PhysicsSkinnedClothFinishUpdate))
                { var items = parent.subSystemList.ToList(); items.Insert(i + 1, new PlayerLoopSystem { type = typeof(RenderStage), updateDelegate = RenderFrame }); parent.subSystemList = items.ToArray(); return true; }
                var child = parent.subSystemList[i]; if (InsertRender(ref child)) { parent.subSystemList[i] = child; return true; }
            }
            return false;
        }
        void Logged(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors++; }
    }
}
