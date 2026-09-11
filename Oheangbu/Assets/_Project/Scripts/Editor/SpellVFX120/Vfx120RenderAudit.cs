using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.SpellVFX120;
using Oheangbu.EditorTools.SpellVFX120;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // Pixel presence/expiry check only. Does not certify visual quality, gameplay
    // connections or event-dependent branches. Call in the saved edit review scene.
    public static class Vfx120RenderAudit
    {
        const int Width = 480, Height = 270, Tolerance = 2;
        const double MaximumSeconds = 150;
        static bool _running;

        [Serializable] public sealed class PixelDifference
        {
            public int changedPixels, maximumChannelDifference;
            public double meanAbsoluteRgbDifference;
        }
        [Serializable] public sealed class SampleResult
        {
            public float fraction, seconds;
            public PixelDifference pixels;
            public int liveParticles;
        }
        [Serializable] public sealed class EntryResult
        {
            public int index;
            public string glyph, family, status = "UNVERIFIED", reason, peakImage, lifeImage;
            public float life, peakFraction;
            public int peakChangedPixels;
            public bool appearanceAtSampledTimes, noVisibleResidueAtLife;
            public SampleResult[] samples;
        }
        [Serializable] public sealed class BaselineProbe
        {
            public string stage;
            public PixelDifference pixels;
        }
        [Serializable] public sealed class Report
        {
            public string status = "UNVERIFIED", reason, utc, unityVersion, scene, directory;
            public int width = Width, height = Height, perChannelTolerance = Tolerance;
            public int expected = 120, completed, passed, failed, unverified;
            public bool baselineStable, sceneDirtyBefore, sceneDirtyAfter, hierarchyRestored;
            public bool artQualityPass = false, gameplayPass = false, demonstrationCues = false;
            public double elapsedSeconds;
            public string target;
            public EntryResult[] entries = Array.Empty<EntryResult>();
            public List<BaselineProbe> baselineProbes = new List<BaselineProbe>();
        }

        public static string Run()
        {
            if (_running) throw new InvalidOperationException("VFX120 pixel audit is already running");
            _running = true;
            var watch = Stopwatch.StartNew();
            var report = new Report
            {
                utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                directory = Path.Combine(Vfx120Editor.Output, "RenderAudit_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff"))
            };
            Camera camera = null;
            RenderTexture target = null, previousTarget = null, previousActive = RenderTexture.active;
            Texture2D readback = null;
            Color previousBackground = Color.black;
            CameraClearFlags previousClearFlags = CameraClearFlags.SolidColor;
            GameObject instance = null;
            var ownedMeshes = new HashSet<Mesh>();
            var initialMeshIds = new HashSet<int>();
            Scene originalScene = SceneManager.GetActiveScene();
            var originalRoots = new HashSet<int>();
            foreach (var go in originalScene.GetRootGameObjects()) originalRoots.Add(go.GetInstanceID());
            report.scene = originalScene.path;
            report.sceneDirtyBefore = originalScene.isDirty;
            try
            {
                Directory.CreateDirectory(report.directory);
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                    throw new InvalidOperationException("Saved edit mode is required");
                if (report.sceneDirtyBefore || report.scene != Vfx120Editor.AssetRoot + "/Scenes/SpellVFX120_Review.unity")
                    throw new InvalidOperationException("Open the saved, clean VFX review scene first; this audit never saves or replaces a scene");
                foreach (var effect in Object.FindObjectsByType<Vfx120Effect>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (effect.gameObject.scene.IsValid())
                        throw new InvalidOperationException("An existing VFX effect is present; stop its capture before auditing");
                var catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot + "/Data/VFX120_Catalog.asset");
                if (catalog == null || catalog.Entries == null || catalog.Entries.Length != 120)
                    throw new InvalidOperationException("Exactly 120 catalog entries are required");
                var candidateCamera = Camera.main;
                if (candidateCamera == null || candidateCamera.gameObject.scene != originalScene)
                    throw new InvalidOperationException("The saved review scene must own Camera.main");
                camera = candidateCamera;
                previousTarget = camera.targetTexture;
                previousBackground = camera.backgroundColor;
                previousClearFlags = camera.clearFlags;
                if (previousClearFlags != CameraClearFlags.SolidColor && previousClearFlags != CameraClearFlags.Skybox)
                    throw new InvalidOperationException("Camera must clear its color; no implicit background change is allowed");
                foreach (var mesh in Resources.FindObjectsOfTypeAll<Mesh>()) initialMeshIds.Add(mesh.GetInstanceID());
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
                { name = "VFX120 Pixel Audit RT", hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
                if (!target.Create()) throw new InvalidOperationException("RenderTexture creation failed");
                readback = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
                { name = "VFX120 Pixel Audit Readback", hideFlags = HideFlags.HideAndDontSave };
                var baseline = new Color32[Width * Height];
                var current = new Color32[baseline.Length];
                var peak = new Color32[baseline.Length];
                var life = new Color32[baseline.Length];

                // One warm render initializes the pipeline. The next image is the
                // single immutable baseline for every effect and all stability probes.
                Capture(camera, target, readback, current);
                Capture(camera, target, readback, baseline);
                SavePng(readback, baseline, Path.Combine(report.directory, "baseline.png"));
                Capture(camera, target, readback, current);
                var firstProbe = AddProbe(report, "before_first_effect", baseline, current);
                report.baselineStable = firstProbe.changedPixels == 0;
                if (!report.baselineStable)
                {
                    SavePng(readback, current, Path.Combine(report.directory, "FAIL_baseline_unstable.png"));
                    throw new InvalidOperationException("Baseline is unstable beyond fixed per-channel tolerance 2");
                }

                Transform actor = GameObject.Find("VFX Target")?.transform;
                report.target = actor != null ? actor.name : "fallback (0,1,4)";
                report.entries = new EntryResult[catalog.Entries.Length];
                for (int i = 0; i < report.entries.Length; i++)
                    report.entries[i] = new EntryResult { index = i + 1, glyph = catalog.Entries[i].Glyph,
                        family = catalog.Entries[i].Profile != null ? catalog.Entries[i].Profile.Family : null };

                for (int i = 0; i < catalog.Entries.Length; i++)
                {
                    if (watch.Elapsed.TotalSeconds > MaximumSeconds)
                    { report.reason = "Time budget reached; remaining entries were not rendered"; break; }
                    var source = catalog.Entries[i];
                    var result = report.entries[i];
                    bool peakCaptured = false, lifeCaptured = false;
                    string id = (i + 1).ToString("000") + "_" + (string.IsNullOrEmpty(source.Glyph) ? "unknown" : ((int)source.Glyph[0]).ToString("X4"));
                    try
                    {
                        if (source.Prefab == null || source.Profile == null)
                            throw new InvalidOperationException("Missing prefab/profile");
                        instance = Object.Instantiate(source.Prefab);
                        instance.name = "__VFX120_PIXEL_AUDIT_" + id;
                        instance.hideFlags = HideFlags.HideAndDontSave;
                        var effect = instance.GetComponent<Vfx120Effect>();
                        if (effect == null) throw new InvalidOperationException("Prefab has no Vfx120Effect");
                        effect.PreviewControlled = true;
                        effect.DemonstrationCues = false;
                        effect.Begin(new Vector3(0, 1, 0), actor, new Vector3(0, 1, 4), Color.white);
                        if (!effect.Begun || !float.IsFinite(effect.Life) || effect.Life <= 0)
                            throw new InvalidOperationException("Effect did not begin with a positive finite lifetime");
                        result.life = effect.Life;
                        foreach (var child in instance.GetComponentsInChildren<Transform>(true))
                            child.gameObject.hideFlags = HideFlags.HideAndDontSave;
                        RememberOwnedMeshes(instance, initialMeshIds, ownedMeshes);
                        var particles = instance.GetComponentsInChildren<ParticleSystem>(true);
                        result.samples = new SampleResult[3];
                        float[] fractions = { .1f, .45f, 1f };
                        for (int j = 0; j < fractions.Length; j++)
                        {
                            effect.Sample(j == 2 ? effect.Life : effect.Life * fractions[j]);
                            Capture(camera, target, readback, current);
                            var sample = new SampleResult { fraction = fractions[j], seconds = effect.Age,
                                pixels = Compare(baseline, current) };
                            foreach (var ps in particles) sample.liveParticles += ps.particleCount;
                            result.samples[j] = sample;
                            if (j < 2 && (!peakCaptured || sample.pixels.changedPixels > result.peakChangedPixels))
                            {
                                result.peakChangedPixels = sample.pixels.changedPixels;
                                result.peakFraction = fractions[j];
                                Array.Copy(current, peak, current.Length); peakCaptured = true;
                            }
                            if (j == 2) { Array.Copy(current, life, current.Length); lifeCaptured = true; }
                        }
                        result.appearanceAtSampledTimes = result.peakChangedPixels > 0;
                        result.noVisibleResidueAtLife = result.samples[2].pixels.changedPixels == 0;
                        result.status = result.appearanceAtSampledTimes && result.noVisibleResidueAtLife
                            ? "PASS_PIXELS_ONLY" : "FAIL";
                        if (!result.appearanceAtSampledTimes) result.reason = "No visible pixels at 0.1 or 0.45 Life; other times/event branches not tested";
                        if (!result.noVisibleResidueAtLife) result.reason = (result.reason == null ? "" : result.reason + "; ") + "Visible pixels remain at exact Life";
                    }
                    catch (Exception e) { result.status = "FAIL_EXCEPTION"; result.reason = e.ToString(); }
                    finally
                    {
                        if (instance != null)
                        {
                            RememberOwnedMeshes(instance, initialMeshIds, ownedMeshes);
                            Object.DestroyImmediate(instance); instance = null;
                        }
                    }

                    // Detect background animation, render history drift or cleanup
                    // contamination instead of enlarging the acceptance threshold.
                    Capture(camera, target, readback, current);
                    var probe = AddProbe(report, "after_" + id + "_destroy", baseline, current);
                    bool stable = probe.changedPixels == 0;
                    bool representative = i % 24 == 0; // one preselected sample per element
                    if (representative || result.status != "PASS_PIXELS_ONLY" || !stable)
                    {
                        if (peakCaptured) { result.peakImage = id + "_peak.png"; SavePng(readback, peak, Path.Combine(report.directory, result.peakImage)); }
                        if (lifeCaptured) { result.lifeImage = id + "_life.png"; SavePng(readback, life, Path.Combine(report.directory, result.lifeImage)); }
                    }
                    report.completed++;
                    if (!stable)
                    {
                        report.baselineStable = false;
                        report.reason = "Baseline changed after " + id + "; pixel attribution is unverified";
                        SavePng(readback, current, Path.Combine(report.directory, "FAIL_baseline_after_" + id + ".png"));
                        break;
                    }
                }
            }
            catch (Exception e) { report.reason = e.ToString(); }
            finally
            {
                if (instance != null)
                {
                    RememberOwnedMeshes(instance, initialMeshIds, ownedMeshes);
                    Object.DestroyImmediate(instance);
                }
                if (camera != null)
                {
                    camera.targetTexture = previousTarget;
                    camera.backgroundColor = previousBackground;
                    camera.clearFlags = previousClearFlags;
                }
                RenderTexture.active = previousActive;
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (readback != null) Object.DestroyImmediate(readback);
                foreach (var mesh in ownedMeshes) if (mesh != null) Object.DestroyImmediate(mesh);
                report.sceneDirtyAfter = originalScene.IsValid() && originalScene.isDirty;
                var rootsAfter = new HashSet<int>();
                if (originalScene.IsValid()) foreach (var go in originalScene.GetRootGameObjects()) rootsAfter.Add(go.GetInstanceID());
                report.hierarchyRestored = SceneManager.GetActiveScene() == originalScene && rootsAfter.SetEquals(originalRoots);
                if (!report.baselineStable || !report.hierarchyRestored || report.sceneDirtyAfter != report.sceneDirtyBefore)
                    foreach (var item in report.entries)
                        if (item.status == "PASS_PIXELS_ONLY") { item.status = "UNVERIFIED"; item.reason = "Baseline or scene restoration did not pass"; }
                foreach (var item in report.entries)
                {
                    if (item.status == "PASS_PIXELS_ONLY") report.passed++;
                    else if (item.status.StartsWith("FAIL", StringComparison.Ordinal)) report.failed++;
                    else report.unverified++;
                }
                report.unverified += Mathf.Max(0, 120 - report.entries.Length);
                report.status = !report.baselineStable || !report.hierarchyRestored || report.sceneDirtyAfter != report.sceneDirtyBefore
                    ? "UNVERIFIED" : report.failed > 0 ? "FAIL" : report.unverified > 0 ? "UNVERIFIED" : "PASS_PIXEL_PRESENCE_AND_EXPIRY_ONLY";
                report.elapsedSeconds = watch.Elapsed.TotalSeconds;
                _running = false;
            }
            string json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory(report.directory);
            File.WriteAllText(Path.Combine(report.directory, "report.json"), json);
            File.WriteAllText(Path.Combine(Vfx120Editor.Output, "render_audit.json"), json);
            return json;
        }

        static void Capture(Camera camera, RenderTexture target, Texture2D readback, Color32[] destination)
        {
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                readback.GetRawTextureData<Color32>().CopyTo(destination);
            }
            finally { camera.targetTexture = oldTarget; RenderTexture.active = oldActive; }
        }

        static PixelDifference Compare(Color32[] baseline, Color32[] current)
        {
            var d = new PixelDifference(); long sum = 0;
            for (int i = 0; i < baseline.Length; i++)
            {
                int r = Math.Abs(baseline[i].r - current[i].r), g = Math.Abs(baseline[i].g - current[i].g), b = Math.Abs(baseline[i].b - current[i].b);
                int maximum = Math.Max(r, Math.Max(g, b));
                if (maximum > Tolerance) d.changedPixels++;
                d.maximumChannelDifference = Math.Max(d.maximumChannelDifference, maximum);
                sum += r + g + b;
            }
            d.meanAbsoluteRgbDifference = sum / (baseline.Length * 3.0);
            return d;
        }

        static PixelDifference AddProbe(Report report, string stage, Color32[] baseline, Color32[] current)
        {
            var difference = Compare(baseline, current);
            report.baselineProbes.Add(new BaselineProbe { stage = stage, pixels = difference });
            return difference;
        }

        static void SavePng(Texture2D readback, Color32[] pixels, string path)
        {
            readback.SetPixels32(pixels);
            File.WriteAllBytes(path, readback.EncodeToPNG());
        }

        static void RememberOwnedMeshes(GameObject root, HashSet<int> previousIds, HashSet<Mesh> owned)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null && !EditorUtility.IsPersistent(filter.sharedMesh) && !previousIds.Contains(filter.sharedMesh.GetInstanceID()))
                    owned.Add(filter.sharedMesh);
        }
    }
}
