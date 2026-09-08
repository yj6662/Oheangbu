using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Oheangbu.C02RigFaceLab
{
    [DefaultExecutionOrder(1200)]
    public sealed class LabCapture : MonoBehaviour
    {
        public LabProfileSO profile;
        public LabFaceController face;
        public LabPlaybackController playback;
        public Camera captureCamera;
        public string outputDirectory;
        public string mode = "face_sweep";
        public int requestedFrames = 180;
        public int holdFrames = 15;
        public bool stopPlayWhenComplete;
        public string status { get; private set; } = "IDLE";
        public int capturedFrames { get; private set; }
        RenderTexture target;
        Texture2D image;
        int previousCaptureRate;
        readonly List<FrameRecord> frames = new List<FrameRecord>();
        [Serializable] public class FrameRecord { public int index; public float time; public float realtime; public string state; public float normalizedState; public string channel; public float faceValue; public float cpuRenderMilliseconds; public int overwriteCount; public bool worldBoundsMeasured; public float worldMinY; public float worldMaxY; public float groundY; public float rootY; public float lightShadowBias; public float lightShadowNormalBias; }
        [Serializable] public class CaptureReport
        {
            public string status; public string mode; public int frameCount; public int fps; public int width; public int height; public float duration; public string camera; public Vector3 cameraPosition; public Vector3 cameraEuler; public float cameraFov;
            public string scope = "Actual Unity Play Mode frames, fixed game timestep and 1x animation speed. Synchronous offscreen URP capture; capture throughput is not gameplay performance.";
            public string visualQuality = "UNVERIFIED"; public string skinWeights; public FrameRecord[] frames; public string error;
        }
        public void Begin()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Capture requires actual Play Mode");
            if (status == "RUNNING") throw new InvalidOperationException("Capture already running");
            if (profile == null || captureCamera == null || face == null) throw new InvalidOperationException("Missing capture configuration");
            if (string.IsNullOrEmpty(outputDirectory)) throw new InvalidOperationException("Missing output folder");
            Directory.CreateDirectory(outputDirectory);
            if (Directory.GetFiles(outputDirectory, "frame_*.png").Length > 0) throw new InvalidOperationException("Use a new capture output directory");
            previousCaptureRate = Time.captureFramerate; Time.captureFramerate = profile.captureFps;
            target = new RenderTexture(profile.captureWidth, profile.captureHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); target.Create();
            image = new Texture2D(profile.captureWidth, profile.captureHeight, TextureFormat.RGB24, false, false);
            capturedFrames = 0; frames.Clear(); face.Neutral();
            if (mode.StartsWith("sequence", StringComparison.Ordinal)) playback.StartSequence();
            status = "RUNNING"; WriteReport();
        }
        void Update()
        {
            if (status != "RUNNING") return;
            if (mode == "face_sweep")
            {
                int channel = capturedFrames / (holdFrames * 6), stage = (capturedFrames / holdFrames) % 6;
                face.Neutral(); if (channel < face.ChannelCount) face.SetValue(channel, stage < 5 ? stage * 0.25f : 0);
            }
            else if (mode == "sequence_face")
            {
                float phase = (capturedFrames % 90) / 90f; float blink = phase < 0.16f ? Mathf.Sin(phase / 0.16f * Mathf.PI) : 0;
                for (int c = 0; c < face.ChannelCount; ++c) face.SetValue(c, face.profile.faceChannels[c].IndexOf("Blink", StringComparison.OrdinalIgnoreCase) >= 0 ? blink : 0.35f);
            }
        }
        void LateUpdate()
        {
            if (status != "RUNNING") return;
            try
            {
                face.Apply();
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(captureCamera, request)) throw new InvalidOperationException("Current URP camera does not support SingleCameraRequest");
                RenderPipeline.SubmitRenderRequest(captureCamera, request);
                var previous = RenderTexture.active; RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false); image.Apply(false, false); RenderTexture.active = previous;
                File.WriteAllBytes(Path.Combine(outputDirectory, "frame_" + capturedFrames.ToString("D6") + ".png"), image.EncodeToPNG());
                timer.Stop();
                int channel = mode == "face_sweep" ? capturedFrames / (holdFrames * 6) : -1;
                var state = playback != null && playback.bodyAnimator != null && playback.bodyAnimator.runtimeAnimatorController != null ? playback.bodyAnimator.GetCurrentAnimatorStateInfo(0) : new AnimatorStateInfo();
                bool measureBounds = capturedFrames < 2; float minimumY = float.PositiveInfinity, maximumY = float.NegativeInfinity;
                if (measureBounds)
                {
                    var baked = new Mesh();
                    foreach (var renderer in playback.bodyAnimator.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        renderer.BakeMesh(baked);
                        foreach (var vertex in baked.vertices) { float y = renderer.transform.TransformPoint(vertex).y; minimumY = Mathf.Min(minimumY, y); maximumY = Mathf.Max(maximumY, y); }
                    }
                    Destroy(baked);
                }
                var ground = GameObject.Find("Inspection ground"); var keyLight = GameObject.Find("Lab key light").GetComponent<Light>();
                frames.Add(new FrameRecord { worldBoundsMeasured = measureBounds, worldMinY = measureBounds ? minimumY : 0, worldMaxY = measureBounds ? maximumY : 0, groundY = ground.transform.position.y, rootY = playback.bodyAnimator.transform.position.y, lightShadowBias = keyLight.shadowBias, lightShadowNormalBias = keyLight.shadowNormalBias, index = capturedFrames, time = capturedFrames / (float)profile.captureFps, realtime = Time.realtimeSinceStartup, state = playback == null ? "none" : playback.currentState, normalizedState = state.normalizedTime, channel = channel >= 0 && channel < face.ChannelCount ? face.profile.faceChannels[channel] : null, faceValue = channel >= 0 && channel < face.ChannelCount ? face.GetValue(channel) : 0, cpuRenderMilliseconds = (float)timer.Elapsed.TotalMilliseconds, overwriteCount = face.overwriteCount });
                capturedFrames++;
                if (capturedFrames >= requestedFrames || mode.StartsWith("sequence", StringComparison.Ordinal) && playback.sequenceStatus == "COMPLETE") Finish("COMPLETE");
                else if (capturedFrames % profile.captureFps == 0) WriteReport();
            }
            catch (Exception exception) { Finish("FAILED", exception.ToString()); Debug.LogException(exception); }
        }
        public void Stop() { Finish("STOPPED"); }
        void Finish(string finalStatus, string error = null)
        {
            status = finalStatus; WriteReport(error); Time.captureFramerate = previousCaptureRate;
            if (target != null) { target.Release(); Destroy(target); target = null; }
            if (image != null) { Destroy(image); image = null; }
            if (face != null) face.Neutral();
#if UNITY_EDITOR
            if (stopPlayWhenComplete && finalStatus == "COMPLETE") UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
        void WriteReport(string error = null)
        {
            File.WriteAllText(Path.Combine(outputDirectory, "capture.json"), JsonUtility.ToJson(new CaptureReport { status = status, mode = mode, frameCount = capturedFrames, fps = profile.captureFps, width = profile.captureWidth, height = profile.captureHeight, duration = capturedFrames / (float)profile.captureFps, camera = captureCamera.name, cameraPosition = captureCamera.transform.position, cameraEuler = captureCamera.transform.eulerAngles, cameraFov = captureCamera.fieldOfView, skinWeights = QualitySettings.skinWeights.ToString(), frames = frames.ToArray(), error = error }, true));
        }
        void OnDisable() { if (status == "RUNNING") Finish("INTERRUPTED"); }
    }
}
