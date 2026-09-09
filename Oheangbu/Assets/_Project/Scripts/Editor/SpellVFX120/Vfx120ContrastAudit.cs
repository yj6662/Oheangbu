using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Oheangbu.App.SpellVFX120;
using Oheangbu.EditorTools.SpellVFX120;
using Oheangbu.Spellcraft;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // Isolated, uniform-background comparison. This deliberately does not judge
    // silhouette readability from a pixel count or imply runtime/gameplay approval.
    public static class Vfx120ContrastAudit
    {
        const int Width = 1280, Height = 720, Tolerance = 2;
        const float SampleFraction = .42f;
        const double MaximumSeconds = 180;
        static readonly Color LightBackground = new Color(.84f, .84f, .84f, 1);
        static readonly Color DarkBackground = new Color(.055f, .055f, .055f, 1);
        static bool _running;

        [Serializable] public sealed class PixelMetrics
        {
            public int changedPixels, maximumChannelDifference;
            public int weakDifferencePixels, strongDifferencePixels, frameEdgePixels;
            public int minX = -1, minY = -1, maxX = -1, maxY = -1;
            public double meanAbsoluteRgbDifference, meanChangedPixelDifference;
            public double coverageFraction;
        }
        [Serializable] public sealed class BackgroundEvidence
        {
            public string name, baselineImage;
            public Color requestedColor;
            public int observedR, observedG, observedB;
            public bool uniform, neutral, stable;
        }
        [Serializable] public sealed class EntryEvidence
        {
            public int index;
            public string glyph, family, behavior, status = "UNVERIFIED", reason;
            public string pixelStatus = "UNVERIFIED", samplingPolicy, samplingReason, impactClockSource;
            public string intendedPhase, phaseEvidence;
            public string lightImage, darkImage, artReadability = "UNVERIFIED_REQUIRES_VISUAL_REVIEW";
            public float life, requestedSampleSeconds, sampledSeconds, sampledFraction, impactClock;
            public int nativeParticles;
            public bool sameSampleForPair, baselineStableAfterPair, sampleWasClamped, afterArrivalAtSample;
            public PixelMetrics light, dark;
        }
        [Serializable] public sealed class Report
        {
            public string status = "UNVERIFIED", reason, capturedUtc, directory, scene;
            public string unityVersion, loadedRuntimeAssemblyMvid, loadedRuntimeAssemblyLastWriteUtc;
            public string sceneSha256Before, sceneSha256After;
            public int width = Width, height = Height, perChannelTolerance = Tolerance;
            public int expected = 120, completed, pixelPresenceBoth, pixelPresenceOne, noPixelsBoth, missingAtSample, unverified;
            public bool demonstrationCues = true, gameplayPass = false, artQualityPass = false;
            public bool sceneDirtyBefore, sceneDirtyAfter, hierarchyPreserved, sourceCameraPreserved;
            public bool previewClosed, renderTargetRestored, environmentPreserved, assetDirtyFlagsPreserved;
            public bool backgroundsSeparatedBy128RgbLevels;
            public bool cleanupSucceeded = true;
            public double elapsedSeconds;
            public Vector3 cameraPosition, cameraEuler;
            public float cameraFieldOfView;
            public string scope = "One identical behavior-selected sample per effect on uniform light/dark backgrounds. " +
                "Projectiles use 70% of the effective impact clock; other behaviors use 42% of Life unless an " +
                "explicit attachment/opening/planted cue window is selected and recorded. " +
                "No scene/assets are saved; no colliders or gameplay actors are created. Preview anchors and " +
                "demonstration events are presentation-only. Pixel presence is not silhouette/art approval. " +
                "Absent pixels at this one sample do not prove absence throughout the lifetime.";
            public string metricDefinition = "RGB byte difference against a separately checked uniform baseline. " +
                "Changed: maximum channel difference >2; weak: 3..11; strong: >=24. These are descriptive counts, " +
                "not perceptual contrast thresholds. Frame-edge pixels may flag framing that needs human review.";
            public string environment = "A private preview-scene culling mask, copied review-camera framing, " +
                "solid neutral background, no scenery/targets rendered, post-processing/shadows/occlusion off. " +
                "RenderSettings and the saved review camera are never modified. Existing review physics ground is read only.";
            public BackgroundEvidence[] backgrounds = Array.Empty<BackgroundEvidence>();
            public EntryEvidence[] entries = Array.Empty<EntryEvidence>();
        }

        public static string Run()
        {
            if (_running) throw new InvalidOperationException("VFX contrast audit is already running");
            _running = true;
            var watch = Stopwatch.StartNew();
            var original = SceneManager.GetActiveScene();
            var report = new Report
            {
                capturedUtc = DateTime.UtcNow.ToString("O"), scene = original.path,
                sceneDirtyBefore = original.isDirty, unityVersion = Application.unityVersion,
                directory = Path.Combine(Vfx120Editor.Output, "ContrastAudit_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff"))
            };
            Scene preview = default;
            Camera sourceCamera = null, camera = null;
            GameObject instance = null;
            RenderTexture target = null;
            Texture2D readback = null;
            var previousActive = RenderTexture.active;
            var originalRandom = UnityEngine.Random.state;
            string originalCameraJson = null, originalEnvironment = EnvironmentStamp();
            var originalRoots = Roots(original);
            int originalSceneCount = SceneManager.sceneCount;
            var initialMeshIds = new HashSet<int>();
            var ownedMeshes = new HashSet<Mesh>();
            var assetDirtyStates = new Dictionary<Object, bool>();
            bool previewWasCreated = false;
            string sceneDiskPath = string.IsNullOrEmpty(original.path) ? null
                : Path.GetFullPath(Path.Combine(Application.dataPath, "..", original.path));
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                    throw new InvalidOperationException("Wait for saved edit mode and completed compilation/import");
                if (SceneManager.sceneCount != 1 || original.isDirty || original.path != Vfx120Editor.AssetRoot + "/Scenes/SpellVFX120_Review.unity")
                    throw new InvalidOperationException("Open only the saved, clean VFX120 review scene first");
                foreach (var effect in Resources.FindObjectsOfTypeAll<Vfx120Effect>())
                    if (!EditorUtility.IsPersistent(effect) && effect.gameObject.scene.IsValid())
                        throw new InvalidOperationException("An effect/capture is already active; finish it before contrast capture");
                sourceCamera = Camera.main;
                if (sourceCamera == null || sourceCamera.gameObject.scene != original)
                    throw new InvalidOperationException("Review scene must own Camera.main");
                originalCameraJson = EditorJsonUtility.ToJson(sourceCamera);
                report.sceneSha256Before = FileHash(sceneDiskPath);
                string assembly = typeof(Vfx120Effect).Assembly.Location;
                DateTime compiled = File.GetLastWriteTimeUtc(assembly);
                foreach (string source in Directory.GetFiles("Assets/_Project/Scripts/App/SpellVFX120", "*.cs", SearchOption.AllDirectories))
                    if (File.GetLastWriteTimeUtc(source) > compiled)
                        throw new InvalidOperationException("Refresh/compile changed VFX runtime code before capture: " + source);
                report.loadedRuntimeAssemblyMvid = typeof(Vfx120Effect).Assembly.ManifestModule.ModuleVersionId.ToString();
                report.loadedRuntimeAssemblyLastWriteUtc = compiled.ToString("O");
                var catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot + "/Data/VFX120_Catalog.asset");
                if (catalog == null || catalog.Entries == null || catalog.Entries.Length != 120)
                    throw new InvalidOperationException("Exactly 120 catalog entries are required");
                RememberAsset(catalog, assetDirtyStates);
                report.entries = new EntryEvidence[120];
                for (int i = 0; i < 120; i++)
                {
                    var e = catalog.Entries[i];
                    report.entries[i] = new EntryEvidence { index = i + 1, glyph = e.Glyph, family = e.Profile != null ? e.Profile.Family : null };
                    RememberAsset(e.Prefab, assetDirtyStates); RememberAsset(e.Profile, assetDirtyStates);
                    if (e.Profile == null) continue;
                    RememberAsset(e.Profile.BodyMesh, assetDirtyStates); RememberAsset(e.Profile.AccentMesh, assetDirtyStates);
                    RememberAsset(e.Profile.BodyMaterial, assetDirtyStates); RememberAsset(e.Profile.InkMaterial, assetDirtyStates);
                    RememberAsset(e.Profile.PatternMaterial, assetDirtyStates); RememberAsset(e.Profile.MistMaterial, assetDirtyStates);
                }
                foreach (var mesh in Resources.FindObjectsOfTypeAll<Mesh>()) initialMeshIds.Add(mesh.GetInstanceID());
                Directory.CreateDirectory(report.directory);
                preview = EditorSceneManager.NewPreviewScene(); previewWasCreated = true;
                var cameraRoot = Make(preview, "VFX120 Contrast Camera");
                camera = cameraRoot.AddComponent<Camera>(); camera.CopyFrom(sourceCamera);
                camera.transform.SetPositionAndRotation(sourceCamera.transform.position, sourceCamera.transform.rotation);
                camera.enabled = false; camera.cameraType = CameraType.Preview;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.cullingMask = ~0;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(preview);
                if (camera.overrideSceneCullingMask == 0) throw new InvalidOperationException("Private preview culling mask unavailable");
                camera.targetTexture = null; camera.rect = new Rect(0, 0, 1, 1); camera.aspect = Width / (float)Height;
                camera.allowHDR = false; camera.allowMSAA = false; camera.allowDynamicResolution = false;
                camera.useOcclusionCulling = false;
                var additional = camera.GetUniversalAdditionalCameraData();
                additional.renderPostProcessing = false; additional.renderShadows = false;
                additional.volumeLayerMask = 0; additional.antialiasing = AntialiasingMode.None;
                report.cameraPosition = camera.transform.position; report.cameraEuler = camera.transform.eulerAngles;
                report.cameraFieldOfView = camera.fieldOfView;

                // Transform-only fixtures: no primitives, renderers, colliders or game rules.
                var primary = Make(preview, "Contrast Primary Anchor").transform; primary.position = new Vector3(0, 0, 4);
                var secondary = Make(preview, "Contrast Secondary Anchor").transform; secondary.position = new Vector3(2.3f, 0, 5.1f);
                var split = new Transform[6]; split[0] = secondary;
                float[] xs = { 0, -2.4f, -1.45f, -.5f, .5f, 1.45f };
                for (int i = 1; i < split.Length; i++)
                {
                    split[i] = Make(preview, "Contrast Split Anchor " + i).transform;
                    split[i].position = new Vector3(xs[i], 0, 5.55f + (i % 2) * .65f);
                }
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
                { name = "VFX120 Contrast RT", hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
                if (!target.Create()) throw new InvalidOperationException("RenderTexture creation failed");
                readback = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
                { name = "VFX120 Contrast Readback", hideFlags = HideFlags.HideAndDontSave };
                var lightBaseline = new Color32[Width * Height]; var darkBaseline = new Color32[Width * Height];
                var lightPixels = new Color32[Width * Height]; var darkPixels = new Color32[Width * Height];
                report.backgrounds = new[]
                {
                    Baseline(camera, target, readback, LightBackground, "light", report.directory, lightBaseline, lightPixels),
                    Baseline(camera, target, readback, DarkBackground, "dark", report.directory, darkBaseline, darkPixels)
                };
                foreach (var b in report.backgrounds)
                    if (!b.uniform || !b.neutral || !b.stable) throw new InvalidOperationException("Neutral baseline is not uniform/neutral/stable: " + b.name);
                report.backgroundsSeparatedBy128RgbLevels = report.backgrounds[0].observedR - report.backgrounds[1].observedR >= 128
                    && report.backgrounds[0].observedG - report.backgrounds[1].observedG >= 128
                    && report.backgrounds[0].observedB - report.backgrounds[1].observedB >= 128;
                if (!report.backgroundsSeparatedBy128RgbLevels)
                    throw new InvalidOperationException("Rendered light/dark backgrounds are not sufficiently distinct; check camera/pipeline");

                for (int i = 0; i < 120; i++)
                {
                    if (watch.Elapsed.TotalSeconds > MaximumSeconds)
                    { report.reason = "Time budget reached; remaining entries are unverified"; break; }
                    var e = catalog.Entries[i]; var row = report.entries[i];
                    string id = (i + 1).ToString("000") + "_" + (string.IsNullOrEmpty(e.Glyph) ? "unknown" : ((int)e.Glyph[0]).ToString("X4"));
                    try
                    {
                        if (e.Prefab == null || e.Profile == null) throw new InvalidOperationException("Missing prefab/profile");
                        instance = (GameObject)PrefabUtility.InstantiatePrefab(e.Prefab, preview);
                        instance.hideFlags = HideFlags.HideAndDontSave;
                        var effect = instance.GetComponent<Vfx120Effect>();
                        if (effect == null) throw new InvalidOperationException("Prefab has no Vfx120Effect");
                        effect.PreviewControlled = true; effect.DemonstrationCues = true;
                        effect.SetSecondaryTargets(secondary, e.Glyph == "안" ? split : null);
                        effect.SetAreaPlan(Vfx120Review.CreateDemonstrationAreaPlan(e.Profile));
                        effect.Begin(new Vector3(0, 1, 0), primary, new Vector3(0, 1, 4), Color.white);
                        if (!effect.Begun || !float.IsFinite(effect.Life) || effect.Life <= 0)
                            throw new InvalidOperationException("Effect did not begin with a valid lifetime");
                        // Begin builds children; all newly created objects are now in the private preview scene.
                        foreach (var t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
                        if (instance.GetComponentsInChildren<Collider>(true).Length != 0)
                            throw new InvalidOperationException("Unexpected collider in VFX prefab; contrast fixture is render-only");
                        RememberMeshes(instance, initialMeshIds, ownedMeshes);
                        ChooseSample(effect, row);
                        effect.Sample(row.sampledSeconds);
                        foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true)) row.nativeParticles += ps.particleCount;
                        Capture(camera, target, readback, LightBackground, lightPixels);
                        Capture(camera, target, readback, DarkBackground, darkPixels);
                        row.sameSampleForPair = effect.Age == row.sampledSeconds;
                        row.light = Compare(lightBaseline, lightPixels); row.dark = Compare(darkBaseline, darkPixels);
                        row.lightImage = id + "_light.png"; row.darkImage = id + "_dark.png";
                        Save(readback, lightPixels, Path.Combine(report.directory, row.lightImage));
                        Save(readback, darkPixels, Path.Combine(report.directory, row.darkImage));
                        row.status = "CAPTURED_PAIR";
                        bool visibleLight = row.light.changedPixels > 0, visibleDark = row.dark.changedPixels > 0;
                        row.pixelStatus = visibleLight && visibleDark ? "PIXELS_BOTH_BACKGROUNDS"
                            : visibleLight || visibleDark ? "PIXELS_ONE_BACKGROUND" : "NOT_VISIBLE_AT_SELECTED_SAMPLE";
                        if (!visibleLight && !visibleDark) row.artReadability = "NOT_ASSESSABLE_AT_SELECTED_SAMPLE";
                        if (!row.sameSampleForPair) throw new InvalidOperationException("Effect age changed during the background pair");
                        report.completed++;
                    }
                    catch (Exception error) { row.status = "UNVERIFIED_EXCEPTION"; row.reason = error.ToString(); }
                    finally
                    {
                        if (instance != null)
                        {
                            RememberMeshes(instance, initialMeshIds, ownedMeshes);
                            Object.DestroyImmediate(instance); instance = null;
                        }
                    }
                    Capture(camera, target, readback, LightBackground, lightPixels);
                    Capture(camera, target, readback, DarkBackground, darkPixels);
                    bool lightStable = Compare(lightBaseline, lightPixels).changedPixels == 0;
                    bool darkStable = Compare(darkBaseline, darkPixels).changedPixels == 0;
                    report.backgrounds[0].stable &= lightStable; report.backgrounds[1].stable &= darkStable;
                    row.baselineStableAfterPair = lightStable && darkStable;
                    if (!row.baselineStableAfterPair)
                    { report.reason = "Baseline changed after " + id + "; attribution of changed pixels is unverified"; break; }
                }
            }
            catch (Exception error) { report.reason = error.ToString(); }
            finally
            {
                Cleanup(() => { if (instance != null) RememberMeshes(instance, initialMeshIds, ownedMeshes); }, report);
                Cleanup(() => { if (instance != null) Object.DestroyImmediate(instance); }, report);
                Cleanup(() => { if (camera != null) camera.targetTexture = null; }, report);
                Cleanup(() => { if (previewWasCreated && preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview); }, report);
                report.previewClosed = !previewWasCreated || !preview.IsValid();
                foreach (var mesh in ownedMeshes) Cleanup(() => { if (mesh != null) Object.DestroyImmediate(mesh); }, report);
                Cleanup(() => { if (target != null) target.Release(); }, report);
                Cleanup(() => { if (target != null) Object.DestroyImmediate(target); }, report);
                Cleanup(() => { if (readback != null) Object.DestroyImmediate(readback); }, report);
                RenderTexture.active = previousActive;
                UnityEngine.Random.state = originalRandom;
                _running = false;
                report.renderTargetRestored = RenderTexture.active == previousActive;
                report.sceneDirtyAfter = original.IsValid() && original.isDirty;
                report.hierarchyPreserved = SceneManager.GetActiveScene() == original
                    && SceneManager.sceneCount == originalSceneCount && Roots(original).SetEquals(originalRoots);
                report.sourceCameraPreserved = sourceCamera != null && originalCameraJson == EditorJsonUtility.ToJson(sourceCamera);
                report.environmentPreserved = EnvironmentStamp() == originalEnvironment;
                report.assetDirtyFlagsPreserved = true;
                foreach (var pair in assetDirtyStates)
                    if (pair.Key == null || EditorUtility.IsDirty(pair.Key) != pair.Value) report.assetDirtyFlagsPreserved = false;
                try { if (sceneDiskPath != null) report.sceneSha256After = FileHash(sceneDiskPath); }
                catch (Exception error) { report.reason = (report.reason ?? "") + "\nScene hash: " + error.Message; }
                bool baselines = report.backgrounds.Length == 2 && report.backgroundsSeparatedBy128RgbLevels;
                foreach (var b in report.backgrounds) baselines &= b.uniform && b.neutral && b.stable;
                bool preserved = report.cleanupSucceeded && report.previewClosed && report.hierarchyPreserved && report.sourceCameraPreserved
                    && report.renderTargetRestored && report.environmentPreserved && report.assetDirtyFlagsPreserved
                    && report.sceneDirtyBefore == report.sceneDirtyAfter && report.sceneSha256Before != null
                    && report.sceneSha256Before == report.sceneSha256After;
                foreach (var row in report.entries)
                {
                    if (row == null) { report.unverified++; continue; }
                    if ((!preserved || !baselines) && !row.status.StartsWith("UNVERIFIED", StringComparison.Ordinal))
                    { row.status = "UNVERIFIED"; row.reason = "Baseline or restoration checks did not pass"; }
                    if (row.status != "CAPTURED_PAIR") { report.unverified++; continue; }
                    if (row.pixelStatus == "PIXELS_BOTH_BACKGROUNDS") report.pixelPresenceBoth++;
                    else if (row.pixelStatus == "PIXELS_ONE_BACKGROUND") { report.pixelPresenceOne++; report.missingAtSample++; }
                    else if (row.pixelStatus == "NOT_VISIBLE_AT_SELECTED_SAMPLE") { report.noPixelsBoth++; report.missingAtSample++; }
                    else report.unverified++;
                }
                report.unverified += Math.Max(0, 120 - report.entries.Length);
                report.status = !preserved || !baselines || report.unverified > 0 ? "UNVERIFIED_OR_INCOMPLETE"
                    : "CAPTURED_120_PAIRS_ART_UNVERIFIED";
                report.elapsedSeconds = watch.Elapsed.TotalSeconds;
            }
            string json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory(report.directory);
            File.WriteAllText(Path.Combine(report.directory, "report.json"), json);
            File.WriteAllText(Path.Combine(Vfx120Editor.Output, "contrast_audit.json"), json);
            return json;
        }

        static void ChooseSample(Vfx120Effect effect, EntryEvidence row)
        {
            var profile = effect.Profile;
            row.life = effect.Life; row.behavior = profile.Behavior.ToString();
            row.impactClock = effect.ReceivedImpactClock > 0 ? effect.ReceivedImpactClock : profile.Flight;
            row.impactClockSource = effect.ReceivedImpactClock > 0 ? "ReceivedImpactClock" : "Profile.Flight";
            var plan = effect.ReceivedAreaPlan;
            // Keep this precedence aligned with Vfx120Effect.Begin/HasSpatialPlan.
            if (plan != null && (plan.Shape == AreaShape.Circle || plan.Shape == AreaShape.Path || plan.Shape == AreaShape.Cone))
            {
                row.impactClock = Mathf.Max(.01f, plan.Delay);
                row.impactClockSource = "ReceivedAreaPlan.Delay (" + plan.Shape + ")";
            }
            if (!float.IsFinite(row.impactClock) || row.impactClock <= 0)
                throw new InvalidOperationException("A finite positive impact clock is required to select the capture phase");

            float lower = 0, upper = effect.Life;
            row.requestedSampleSeconds = effect.Life * SampleFraction;
            row.samplingPolicy = "LIFE_42_PERCENT";
            switch (profile.Behavior)
            {
                case Vfx120Behavior.Projectile:
                    row.samplingPolicy = "EFFECTIVE_IMPACT_CLOCK_70_PERCENT";
                    row.requestedSampleSeconds = row.impactClock * .70f;
                    row.intendedPhase = "IN_FLIGHT_BEFORE_ARRIVAL";
                    row.samplingReason = "Capture the travelling projectile before its impact/disappearance, regardless of the cleanup lifetime.";
                    if (profile.Glyph == "간" || profile.Glyph == "안" || profile.Glyph == "상")
                        row.samplingReason += " Later illustrative transfer/split/hit cues are outside this single-sample check.";
                    upper = Mathf.Min(upper, row.impactClock);
                    break;
                case Vfx120Behavior.Bind: row.intendedPhase = "BIND_MID_LIFETIME"; break;
                case Vfx120Behavior.Heal: row.intendedPhase = "HEAL_MID_LIFETIME"; break;
                case Vfx120Behavior.Shield: row.intendedPhase = "SHIELD_MID_LIFETIME"; break;
                case Vfx120Behavior.Zone: row.intendedPhase = "ZONE_MID_LIFETIME"; break;
                case Vfx120Behavior.Summon: row.intendedPhase = "SUMMON_MID_LIFETIME"; break;
                case Vfx120Behavior.Weapon: row.intendedPhase = "WEAPON_MID_LIFETIME"; break;
                case Vfx120Behavior.Buff: row.intendedPhase = "BUFF_MID_LIFETIME"; break;
                case Vfx120Behavior.Burst: row.intendedPhase = "BURST_MID_LIFETIME"; break;
                case Vfx120Behavior.Wave: row.intendedPhase = "WAVE_MID_LIFETIME"; break;
                case Vfx120Behavior.Reserve: row.intendedPhase = "RESERVE_MID_LIFETIME"; break;
                default: throw new InvalidOperationException("No capture policy for behavior " + profile.Behavior);
            }
            if (profile.Behavior != Vfx120Behavior.Projectile)
            {
                row.samplingReason = "Sample at 42% of the effective lifetime; this does not certify every event or motion phase.";
                bool attached = profile.Glyph == "감" || profile.Glyph == "남" || profile.Glyph == "맘"
                    || profile.Glyph == "삼" || profile.Glyph == "암";
                if (attached)
                {
                    row.samplingPolicy = "ATTACHED_BEFORE_ILLUSTRATIVE_LATER_HIT";
                    row.intendedPhase = "ATTACHED_SEAL_AFTER_ARRIVAL";
                    lower = row.impactClock + .24f;
                    upper = Mathf.Min(effect.Life, Mathf.Max(row.impactClock + .3f, effect.Life * .48f) - .04f);
                    row.samplingReason = "Show the settled attachment after impact +0.24s and before the demonstration later-hit cue; no gameplay hit is generated.";
                }
                else if (profile.Glyph == "녹")
                {
                    row.samplingPolicy = "ILLUSTRATIVE_HIT_OPENING";
                    row.intendedPhase = "LOTUS_OPENING_AFTER_ILLUSTRATIVE_HIT";
                    lower = row.impactClock + .45f;
                    row.samplingReason = "The lotus is hidden before its hit cue; sample at least 0.45s after the illustrative arrival hit during/after opening.";
                }
                else if (profile.Glyph == "검")
                {
                    row.samplingPolicy = "PLANTED_BEFORE_ILLUSTRATIVE_RELEASE";
                    row.intendedPhase = "PLANTED_TREE_AFTER_ARRIVAL";
                    lower = row.impactClock + .28f;
                    upper = Mathf.Min(effect.Life, effect.Life * .77f - .05f);
                    row.samplingReason = "Show the planted tree after arrival, before the demonstration release at 77% Life; the release branch is not judged here.";
                }
                if (lower < upper)
                    row.requestedSampleSeconds = Mathf.Clamp(row.requestedSampleSeconds, lower, upper);
            }
            float endExclusive = effect.Life * (1 - .0001f);
            row.sampledSeconds = Mathf.Clamp(row.requestedSampleSeconds, 0, endExclusive);
            row.sampleWasClamped = !Mathf.Approximately(row.sampledSeconds, row.requestedSampleSeconds);
            row.sampledFraction = row.sampledSeconds / effect.Life;
            row.afterArrivalAtSample = row.sampledSeconds >= row.impactClock;
            bool withinPhase = row.sampledSeconds >= lower && (profile.Behavior == Vfx120Behavior.Projectile
                ? row.sampledSeconds < upper : row.sampledSeconds <= upper);
            row.phaseEvidence = lower < upper && withinPhase
                ? "SELECTED_BY_CLOCK_NOT_VISUAL_PASS" : "UNVERIFIED_REQUESTED_PHASE_OUTSIDE_LIFETIME";
        }

        static GameObject Make(Scene scene, string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }
        static void Cleanup(Action action, Report report)
        {
            try { action(); }
            catch (Exception error) { report.cleanupSucceeded = false; report.reason = (report.reason ?? "") + "\nCleanup error: " + error; }
        }
        static HashSet<int> Roots(Scene scene)
        {
            var ids = new HashSet<int>();
            if (scene.IsValid()) foreach (var root in scene.GetRootGameObjects()) ids.Add(root.GetInstanceID());
            return ids;
        }
        static void RememberAsset(Object asset, Dictionary<Object, bool> states)
        {
            if (asset != null && !states.ContainsKey(asset)) states.Add(asset, EditorUtility.IsDirty(asset));
        }
        static void RememberMeshes(GameObject root, HashSet<int> originalIds, HashSet<Mesh> owned)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null && !EditorUtility.IsPersistent(filter.sharedMesh)
                    && !originalIds.Contains(filter.sharedMesh.GetInstanceID())) owned.Add(filter.sharedMesh);
        }
        static string FileHash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        static string EnvironmentStamp()
        {
            // Read-only stamp; this tool never changes RenderSettings or shared lighting assets.
            return RenderSettings.fog + "|" + RenderSettings.fogColor + "|" + RenderSettings.fogDensity + "|"
                + RenderSettings.fogMode + "|" + RenderSettings.fogStartDistance + "|" + RenderSettings.fogEndDistance + "|"
                + RenderSettings.ambientMode + "|" + RenderSettings.ambientIntensity + "|" + RenderSettings.ambientLight + "|"
                + RenderSettings.ambientSkyColor + "|" + RenderSettings.ambientEquatorColor + "|" + RenderSettings.ambientGroundColor + "|"
                + RenderSettings.reflectionIntensity + "|" + (RenderSettings.skybox != null ? RenderSettings.skybox.GetInstanceID() : 0);
        }
        static BackgroundEvidence Baseline(Camera camera, RenderTexture target, Texture2D readback,
            Color color, string name, string directory, Color32[] baseline, Color32[] scratch)
        {
            Capture(camera, target, readback, color, scratch);
            Capture(camera, target, readback, color, baseline);
            Capture(camera, target, readback, color, scratch);
            bool uniform = true; var first = baseline[0];
            foreach (var pixel in baseline)
                if (Math.Abs(pixel.r - first.r) > Tolerance || Math.Abs(pixel.g - first.g) > Tolerance
                    || Math.Abs(pixel.b - first.b) > Tolerance) { uniform = false; break; }
            string path = "baseline_" + name + ".png";
            Save(readback, baseline, Path.Combine(directory, path));
            return new BackgroundEvidence { name = name, requestedColor = color, baselineImage = path,
                observedR = first.r, observedG = first.g, observedB = first.b,
                neutral = Math.Abs(first.r - first.g) <= Tolerance && Math.Abs(first.g - first.b) <= Tolerance,
                uniform = uniform, stable = Compare(baseline, scratch).changedPixels == 0 };
        }
        static void Capture(Camera camera, RenderTexture target, Texture2D readback, Color color, Color32[] pixels)
        {
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
            try
            {
                camera.backgroundColor = color; camera.targetTexture = target;
                camera.Render(); RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                readback.GetRawTextureData<Color32>().CopyTo(pixels);
            }
            finally { camera.targetTexture = oldTarget; RenderTexture.active = oldActive; }
        }
        static void Save(Texture2D readback, Color32[] pixels, string path)
        {
            readback.SetPixels32(pixels); File.WriteAllBytes(path, readback.EncodeToPNG());
        }
        static PixelMetrics Compare(Color32[] baseline, Color32[] pixels)
        {
            var result = new PixelMetrics(); long total = 0, changedTotal = 0;
            int minX = Width, minY = Height, maxX = -1, maxY = -1;
            for (int i = 0; i < baseline.Length; i++)
            {
                int r = Math.Abs(baseline[i].r - pixels[i].r), g = Math.Abs(baseline[i].g - pixels[i].g), b = Math.Abs(baseline[i].b - pixels[i].b);
                int maximum = Math.Max(r, Math.Max(g, b)); total += r + g + b;
                result.maximumChannelDifference = Math.Max(result.maximumChannelDifference, maximum);
                if (maximum <= Tolerance) continue;
                result.changedPixels++; changedTotal += r + g + b;
                if (maximum < 12) result.weakDifferencePixels++;
                if (maximum >= 24) result.strongDifferencePixels++;
                int x = i % Width, y = i / Width;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                if (x == 0 || y == 0 || x == Width - 1 || y == Height - 1) result.frameEdgePixels++;
            }
            result.coverageFraction = result.changedPixels / (double)baseline.Length;
            result.meanAbsoluteRgbDifference = total / (baseline.Length * 3.0);
            if (result.changedPixels > 0)
            {
                result.meanChangedPixelDifference = changedTotal / (result.changedPixels * 3.0);
                result.minX = minX; result.minY = minY; result.maxX = maxX; result.maxY = maxY;
            }
            return result;
        }
    }
}
