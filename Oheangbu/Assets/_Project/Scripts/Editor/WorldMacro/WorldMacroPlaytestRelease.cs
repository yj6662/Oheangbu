using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.Drawing;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Minimal, scene-explicit Windows build candidate surface for the dated external playtest.</summary>
    public static class WorldMacroPlaytestRelease
    {
        public const string Label = "BUILD CANDIDATE";
        public const string ExpectedUnityVersion = "6000.3.9f1";
        public const string ExecutableName = "Oheangbu_Playtest.exe";

        private static readonly char[] SummonLetters = { '곰', '놈', '몸', '솜', '옴' };

        [Serializable]
        public sealed class Check
        {
            public string name;
            public bool pass;
            public string detail;
        }

        [Serializable]
        public sealed class ReleaseReport
        {
            public string label = Label;
            public bool qaComplete;
            public string status;
            public string startedUtc;
            public string finishedUtc;
            public string unityVersion;
            public string target = "StandaloneWindows64";
            public string scriptingBackend;
            public string scene = PlaytestMenuAuthoring.TitlePath;
            public string executable;
            public string outputFolder;
            public string readme;
            public string log;
            public string buildResult;
            public ulong totalSizeBytes;
            public double totalSeconds;
            public int totalErrors;
            public int totalWarnings;
            public float commitRatio;
            public bool buildSettingsOverrideApplied;
            public bool buildSettingsRestored;
            public bool buildSettingsFileRestored;
            public string buildSettingsFile;
            public string buildSettingsHashBefore;
            public string buildSettingsHashDuring;
            public string buildSettingsHashAfter;
            public string[] buildSettingsBefore = Array.Empty<string>();
            public string[] buildSettingsDuring = Array.Empty<string>();
            public string[] buildSettingsAfter = Array.Empty<string>();
            public Check[] checks = Array.Empty<Check>();
            public string[] buildMessages = Array.Empty<string>();
            public string[] limitations =
            {
                "This is a BUILD CANDIDATE and is not QA complete.",
                "Five summons are timed static presentations; they have no movement, animation, AI, attack, damage, aggro, collider, or rigidbody gameplay.",
                "Final route walking, drawing recognition, combat feel, camera comfort, graphics compatibility, and packaging still require external playtest review."
            };
        }

        private static string RepositoryRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        private static string BuildRoot => Path.Combine(RepositoryRoot, "Builds", "Playtest-20260915");
        private static string LatestPreflightPath => Path.Combine(BuildRoot, "preflight_latest.json");

        public static string Execute(string argument)
        {
            switch ((argument ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "preflight": return Preflight();
                case "build": return Build();
                case "status": return Status();
                default: throw new ArgumentException("Expected preflight, build, or status.");
            }
        }

        private static string Preflight()
        {
            ReleaseReport report = RunPreflight();
            Directory.CreateDirectory(BuildRoot);
            File.WriteAllText(LatestPreflightPath, JsonUtility.ToJson(report, true), Encoding.UTF8);
            return JsonUtility.ToJson(report, true);
        }

        private static ReleaseReport RunPreflight()
        {
            var checks = new List<Check>();
            var report = new ReleaseReport
            {
                startedUtc = DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion,
                scriptingBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone).ToString()
            };

            Add(checks, "editor is idle", !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling,
                "playingOrChanging=" + EditorApplication.isPlayingOrWillChangePlaymode + "; compiling=" + EditorApplication.isCompiling);
            Add(checks, "scripts compiled without errors", !EditorUtility.scriptCompilationFailed,
                "EditorUtility.scriptCompilationFailed=" + EditorUtility.scriptCompilationFailed);
            Add(checks, "pinned Unity editor", Application.unityVersion == ExpectedUnityVersion,
                "expected=" + ExpectedUnityVersion + "; actual=" + Application.unityVersion);
            Add(checks, "Windows x64 support", BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64),
                "StandaloneWindows64 build support must be installed.");
            Add(checks, "Standalone Mono backend", PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone) == ScriptingImplementation.Mono2x,
                "backend=" + report.scriptingBackend + "; project settings are read only.");

            report.commitRatio = Prologue.PrologueAudit.CommitRatio();
            Add(checks, "commit capacity below stop line", report.commitRatio < .85f,
                "commitRatio=" + report.commitRatio.ToString("P1") + "; required <85%. Build does not start at or above the stop line.");
            Add(checks, "saved playtest scene exists", AssetDatabase.LoadAssetAtPath<SceneAsset>(WorldMacroPlaytestAuthoring.ScenePath) != null,
                WorldMacroPlaytestAuthoring.ScenePath);
            Add(checks, "saved title scene exists", AssetDatabase.LoadAssetAtPath<SceneAsset>(PlaytestMenuAuthoring.TitlePath) != null,
                PlaytestMenuAuthoring.TitlePath);
            Scene active = SceneManager.GetActiveScene();
            Add(checks, "active playtest scene is saved", active.path != WorldMacroPlaytestAuthoring.ScenePath || !active.isDirty,
                active.path == WorldMacroPlaytestAuthoring.ScenePath ? "isDirty=" + active.isDirty : "Build reads the saved explicit scene asset.");

            SpellBookSO book = AssetDatabase.LoadAssetAtPath<SpellBookSO>(WorldMacroSummonCastAuthoring.TestBookPath);
            bool fivePresentationOnly = book != null && SummonLetters.All(letter => book.TryGet(letter, out SpellBookSO.Entry entry)
                && entry.Kind == SpellKind.Summon && entry.BasePower == 0f && entry.AreaShape == AreaShape.None);
            Add(checks, "TEST spell book has five non-attack summons", fivePresentationOnly,
                book == null ? "Missing " + WorldMacroSummonCastAuthoring.TestBookPath
                    : "Expected 곰/놈/몸/솜/옴 as Summon with zero power and no attack area.");

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(WorldMacroPlaytestAuthoring.ScenePath) != null)
                InspectSavedScene(checks, book);

            report.checks = checks.ToArray();
            report.status = checks.All(check => check.pass) ? "PREFLIGHT_PASS_BUILD_CANDIDATE" : "PREFLIGHT_REJECTED";
            report.finishedUtc = DateTime.UtcNow.ToString("o");
            return report;
        }

        private static void InspectSavedScene(List<Check> checks, SpellBookSO book)
        {
            Scene preview = default;
            try
            {
                preview = EditorSceneManager.OpenPreviewScene(WorldMacroPlaytestAuthoring.ScenePath);
                WorldMacroPlaytestSession[] sessions = Components<WorldMacroPlaytestSession>(preview);
                WorldMacroCombatWalker[] walkers = Components<WorldMacroCombatWalker>(preview);
                WorldMacroPlayerAppearance[] appearances = Components<WorldMacroPlayerAppearance>(preview);
                Add(checks, "saved scene playtest session", sessions.Length == 1 && sessions[0].Content != null,
                    "sessions=" + sessions.Length);
                Add(checks, "no diagnostic save suffix in release", sessions.Length == 1 && string.IsNullOrEmpty(sessions[0].TestSaveSuffix),
                    "Release uses the normal player slot, never an automated diagnostic fixture.");
                Add(checks, "saved scene combat walker", walkers.Length == 1 && walkers[0].Body != null && walkers[0].ViewCamera != null,
                    "walkers=" + walkers.Length);

                HudController[] huds = Components<HudController>(preview);
                WorldMacroPlaytestHudPresenter[] presenters = Components<WorldMacroPlaytestHudPresenter>(preview);
                bool hudReady = huds.Length == 1 && huds[0].Skin != null && presenters.Length == 1
                    && AssetDatabase.GetAssetPath(huds[0].Skin) == PlaytestMenuAuthoring.Folder + "/ResourceBars.asset"
                    && huds[0].Skin.UseInkBar && huds[0].Skin.KoreanFont != null
                    && huds[0].Skin.InkBarSize.x > 0f && huds[0].Skin.InkBarSize.y > 0f;
                Add(checks, "saved scene Recraft HUD", hudReady,
                    "Expected one isolated Recraft resource-bar skin with ink bar and Korean font, plus one session presenter.");
                WorldMacroPlaytestAudio[] feedbackAudio = Components<WorldMacroPlaytestAudio>(preview);
                bool audioReady = feedbackAudio.Length == 1;
                if (audioReady)
                {
                    var serializedAudio = new SerializedObject(feedbackAudio[0]);
                    var profile = serializedAudio.FindProperty("_profile").objectReferenceValue as WorldMacroPlaytestAudioProfileSO;
                    audioReady = profile != null && profile.GetType().GetFields()
                        .Where(field => field.FieldType == typeof(WorldMacroPlaytestAudioProfileSO.Cue))
                        .Select(field => field.GetValue(profile) as WorldMacroPlaytestAudioProfileSO.Cue)
                        .All(cue => cue != null && cue.Clip != null && cue.Clip.channels == 1);
                }
                Add(checks, "saved scene ElevenLabs SFX", audioReady,
                    "Expected one bounded audio component with all fourteen mono cue references.");
                AudioListener[] listeners = Components<AudioListener>(preview)
                    .Where(listener => listener.enabled && listener.gameObject.activeInHierarchy).ToArray();
                Add(checks, "one active play camera audio listener", listeners.Length == 1 && walkers.Length == 1
                    && listeners[0].gameObject == walkers[0].ViewCamera.gameObject,
                    "Active listeners=" + listeners.Length + "; listener must follow the same walking and driving camera.");

                bool playerReady = appearances.Length == 1 && appearances[0].Profile != null && appearances[0].Animator != null
                    && appearances[0].Animator.avatar != null && appearances[0].Animator.avatar.isValid
                    && appearances[0].Animator.avatar.isHuman && !appearances[0].Animator.applyRootMotion
                    && appearances[0].WorldRenderers != null && appearances[0].WorldRenderers.Length > 0;
                Add(checks, "saved scene player appearance", playerReady,
                    "appearances=" + appearances.Length + "; expected installed C02 Humanoid with renderer ownership and root motion off.");

                bool usesReRig = playerReady
                    && AssetDatabase.GetAssetPath(appearances[0].Profile) == WorldMacroPlayerReRigAuthoring.ProfilePath;
                var activeGesture = playerReady ? appearances[0].GetComponent<WorldMacroPlayerGestureRig>() : null;
                bool usesGripALocomotion = playerReady && activeGesture != null && activeGesture.Profile != null
                    && AssetDatabase.GetAssetPath(appearances[0].Profile) == WorldMacroLocomotionAuthoring.ProfilePath
                    && AssetDatabase.GetAssetPath(activeGesture.Profile) == WorldMacroPlayerReRigAuthoring.GripAProfile;
                bool usesRecovery = playerReady && activeGesture != null && activeGesture.Profile != null
                    && AssetDatabase.GetAssetPath(appearances[0].Profile) == WorldMacroLocomotionAuthoring.RecoveryFolder+"/PlayerAppearance_Locomotion.asset"
                    && AssetDatabase.GetAssetPath(activeGesture.Profile) == "Assets/_Project/Art/Characters/PlaytestRecoveryGripA/PlayerGesture_GripA.asset";
                bool profileBound = playerReady
                    && (usesReRig || usesGripALocomotion || usesRecovery || AssetDatabase.GetAssetPath(appearances[0].Profile) == WorldMacroPlayerAppearanceAuthoring.ProfilePath)
                    && MonoScript.FromScriptableObject(appearances[0].Profile) != null
                    && AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(appearances[0].Profile))
                        == WorldMacroPlayerAppearanceAuthoring.ProfileScriptPath;
                Add(checks, "player profile script binding", profileBound,
                    "Expected profile asset and reload-safe MonoScript " + WorldMacroPlayerAppearanceAuthoring.ProfileScriptPath);

                string[] usedMaterialPaths = playerReady ? appearances[0].WorldRenderers.Where(renderer => renderer != null)
                    .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null)
                    .Select(AssetDatabase.GetAssetPath).Distinct().OrderBy(path => path).ToArray() : Array.Empty<string>();
                string[] expectedMaterials = usesReRig || usesGripALocomotion || usesRecovery ? WorldMacroPlayerReRigAuthoring.OwnedMaterialPaths
                    : WorldMacroPlayerAppearanceAuthoring.OwnedMaterialPaths;
                bool materialsReady = usedMaterialPaths.Length == expectedMaterials.Length
                    && expectedMaterials.All(path => usedMaterialPaths.Contains(path));
                Add(checks, "owned readable player materials", materialsReady,
                    usedMaterialPaths.Length == 0 ? "No player materials found." : string.Join(",", usedMaterialPaths));
                if (usesGripALocomotion || usesRecovery)
                {
                    string detail;bool gate = usesRecovery ? WorldMacroPlayerReRigAuthoring.HasRecoveryReleaseGate(appearances[0], out detail) : WorldMacroPlayerReRigAuthoring.HasGripAReleaseGate(appearances[0], out detail);
                    Add(checks, "exact active A hand and directional locomotion acceptance", gate, detail);
                    var prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(appearances[0].gameObject);
                    bool sourceBound = prefabSource == AssetDatabase.LoadAssetAtPath<GameObject>(WorldMacroPlayerReRigAuthoring.PrefabPath)
                        || prefabSource == AssetDatabase.LoadAssetAtPath<GameObject>(WorldMacroPlayerReRigAuthoring.GripAPrefab);
                    Add(checks, "combined player visual prefab binding", sourceBound,
                        "Accept the preserved ReRig instance with verified A overrides or the synchronized A visual prefab; neither path replaces the current actor.");
                }
                else if (usesReRig)
                {
                    bool gateCurrent = WorldMacroPlayerReRigAuthoring.HasCurrentRigGate(out string gateDetail);
                    var gesture = appearances[0].GetComponent<WorldMacroPlayerGestureRig>();
                    bool gestureReady = gesture != null && gesture.Profile != null
                        && AssetDatabase.GetAssetPath(gesture.Profile) == WorldMacroPlayerReRigAuthoring.GestureProfilePath
                        && gesture.Profile.Fingers != null && gesture.Profile.Fingers.Length == 30
                        && gesture.Profile.Fingers.All(finger => finger != null && finger.UseExplicitRestPose
                            && WorldMacroPlayerGestureProfile.IsUsableRotation(finger.RestLocalRotation)
                            && WorldMacroPlayerGestureProfile.IsUsableRotation(finger.CarryOffset));
                    bool prefabBound = PrefabUtility.GetCorrespondingObjectFromSource(appearances[0].gameObject)
                        == AssetDatabase.LoadAssetAtPath<GameObject>(WorldMacroPlayerReRigAuthoring.PrefabPath);
                    Add(checks, "C02 rerig source and scoped acceptance", gateCurrent && prefabBound,
                        gateDetail + "; expected derivative prefab=" + prefabBound);
                    Add(checks, "C02 calibrated hand presentation", gestureReady,
                        "Expected derivative gesture profile and thirty explicit calibrated finger poses; runtime contact still requires its separate QA report.");
                }

                CombatLifetimeScope[] scopes = Components<CombatLifetimeScope>(preview);
                BrushStrokeFeedAdapter[] adapters = Components<BrushStrokeFeedAdapter>(preview);
                bool consumersReady = book != null && scopes.Length > 0 && adapters.Length > 0
                    && scopes.All(scope => UsesBook(scope, book)) && adapters.All(adapter => UsesBook(adapter, book));
                Add(checks, "saved scene uses TEST spell book", consumersReady,
                    "combatScopes=" + scopes.Length + "; brushAdapters=" + adapters.Length + "; expected="
                    + WorldMacroSummonCastAuthoring.TestBookPath);
            }
            catch (Exception exception)
            {
                Add(checks, "saved scene preview inspection", false, exception.GetType().Name + ": " + exception.Message);
            }
            finally
            {
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static string Build()
        {
            ReleaseReport report = RunPreflight();
            if (report.checks.Any(check => !check.pass))
                throw new InvalidOperationException("Release preflight rejected the build:\n"
                    + string.Join("\n", report.checks.Where(check => !check.pass).Select(check => check.name + ": " + check.detail)));

            string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
            string directory = Path.Combine(BuildRoot, stamp);
            string executable = Path.Combine(directory, ExecutableName);
            if (Directory.Exists(directory) || File.Exists(executable))
                throw new IOException("Refusing to overwrite an existing release path: " + directory);
            Directory.CreateDirectory(directory);
            report.outputFolder = directory;
            report.executable = executable;
            report.readme = Path.Combine(directory, "README_플레이테스트.txt");
            report.log = Path.Combine(directory, "build.log");
            report.status = "BUILDING_BUILD_CANDIDATE";

            BuildReport unityReport = null;
            Exception failure = null;
            string[] selectedScenes = { PlaytestMenuAuthoring.TitlePath, WorldMacroPlaytestAuthoring.ScenePath };
            EditorBuildSettingsScene[] originalBuildSettings = EditorBuildSettings.scenes;
            string buildSettingsFile = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../ProjectSettings/EditorBuildSettings.asset"));
            if (!File.Exists(buildSettingsFile))
                throw new FileNotFoundException("EditorBuildSettings asset is missing.", buildSettingsFile);
            byte[] originalBuildSettingsBytes = File.ReadAllBytes(buildSettingsFile);
            report.buildSettingsFile = buildSettingsFile;
            report.buildSettingsHashBefore = Sha256(originalBuildSettingsBytes);
            EditorBuildSettingsScene[] exposedBuildSettings = selectedScenes
                .Select(path => new EditorBuildSettingsScene(path, true)).ToArray();
            report.buildSettingsBefore = DescribeBuildSettings(originalBuildSettings);
            try
            {
                // com.unity.pipeline 0.5.0-exp.1 scans EditorBuildSettings instead of the explicit
                // BuildPlayerOptions scene array. Expose this build's identical title/play selection
                // only for the synchronous build call; the finally block restores the full list.
                EditorBuildSettings.scenes = exposedBuildSettings;
                EditorBuildSettingsScene[] activeBuildSettings = EditorBuildSettings.scenes;
                report.buildSettingsDuring = DescribeBuildSettings(activeBuildSettings);
                report.buildSettingsHashDuring = File.Exists(buildSettingsFile)
                    ? Sha256(File.ReadAllBytes(buildSettingsFile)) : "MISSING";
                report.buildSettingsOverrideApplied = SameBuildSettings(exposedBuildSettings, activeBuildSettings);
                if (!report.buildSettingsOverrideApplied)
                    throw new InvalidOperationException("Could not expose the explicit build scene to legacy preprocessors.");

                unityReport = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = selectedScenes,
                    locationPathName = executable,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                });
                ReadBuildReport(unityReport, report);
                if (unityReport == null || unityReport.summary.result != BuildResult.Succeeded)
                    failure = new InvalidOperationException("Unity build result: " + report.buildResult);
                else if (!File.Exists(executable))
                    failure = new FileNotFoundException("Unity reported success but the executable is missing.", executable);
            }
            catch (Exception exception)
            {
                failure = exception;
                report.buildMessages = report.buildMessages.Concat(new[] { exception.ToString() }).ToArray();
            }
            finally
            {
                var restoreFailures = new List<Exception>();
                try
                {
                    EditorBuildSettings.scenes = originalBuildSettings;
                    EditorBuildSettingsScene[] restoredBuildSettings = EditorBuildSettings.scenes;
                    report.buildSettingsAfter = DescribeBuildSettings(restoredBuildSettings);
                    report.buildSettingsRestored = SameBuildSettings(originalBuildSettings, restoredBuildSettings);
                    if (!report.buildSettingsRestored)
                        restoreFailures.Add(new InvalidOperationException(
                            "EditorBuildSettings scene list did not restore exactly after the explicit build."));
                }
                catch (Exception exception)
                {
                    restoreFailures.Add(exception);
                }

                try
                {
                    byte[] currentBuildSettingsBytes = File.Exists(buildSettingsFile)
                        ? File.ReadAllBytes(buildSettingsFile) : null;
                    if (currentBuildSettingsBytes == null
                        || !currentBuildSettingsBytes.SequenceEqual(originalBuildSettingsBytes))
                        File.WriteAllBytes(buildSettingsFile, originalBuildSettingsBytes);

                    byte[] restoredBuildSettingsBytes = File.ReadAllBytes(buildSettingsFile);
                    report.buildSettingsHashAfter = Sha256(restoredBuildSettingsBytes);
                    report.buildSettingsFileRestored = restoredBuildSettingsBytes.SequenceEqual(originalBuildSettingsBytes)
                        && string.Equals(report.buildSettingsHashBefore, report.buildSettingsHashAfter,
                            StringComparison.Ordinal);
                    if (!report.buildSettingsFileRestored)
                        restoreFailures.Add(new InvalidOperationException(
                            "EditorBuildSettings asset bytes did not restore exactly after the explicit build."));
                }
                catch (Exception exception)
                {
                    restoreFailures.Add(exception);
                }

                report.checks = report.checks.Concat(new[]
                {
                    new Check
                    {
                        name = "explicit scene exposed to legacy build preprocessors",
                        pass = report.buildSettingsOverrideApplied,
                        detail = string.Join(",", report.buildSettingsDuring)
                    },
                    new Check
                    {
                        name = "EditorBuildSettings scene list restored",
                        pass = report.buildSettingsRestored,
                        detail = report.buildSettingsRestored ? "Restored exact path/enabled/GUID order after BuildPlayer."
                            : "before=" + string.Join(",", report.buildSettingsBefore) + "; after=" + string.Join(",", report.buildSettingsAfter)
                    },
                    new Check
                    {
                        name = "EditorBuildSettings asset bytes restored",
                        pass = report.buildSettingsFileRestored,
                        detail = "before=" + report.buildSettingsHashBefore + "; during=" + report.buildSettingsHashDuring
                            + "; after=" + report.buildSettingsHashAfter + "; file=" + report.buildSettingsFile
                    }
                }).ToArray();

                if (restoreFailures.Count > 0)
                {
                    Exception restoreFailure = restoreFailures.Count == 1
                        ? restoreFailures[0] : new AggregateException(restoreFailures);
                    failure = failure == null ? restoreFailure : new AggregateException(failure, restoreFailure);
                    report.buildMessages = report.buildMessages.Concat(new[] { restoreFailure.ToString() }).ToArray();
                }
            }

            report.finishedUtc = DateTime.UtcNow.ToString("o");
            report.status = failure == null ? "BUILD_CANDIDATE_CREATED_NOT_QA_COMPLETE" : "BUILD_FAILED";
            WriteBuildLog(report);
            if (failure == null) File.WriteAllText(report.readme, KoreanReadme(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "release_build_report.json"), JsonUtility.ToJson(report, true), Encoding.UTF8);
            if (failure != null) throw new InvalidOperationException("Build failed; report: "
                + Path.Combine(directory, "release_build_report.json"), failure);
            return JsonUtility.ToJson(report, true);
        }

        private static string Status()
        {
            if (!Directory.Exists(BuildRoot)) return "NOT_BUILT; expectedRoot=" + BuildRoot;
            string latest = Directory.EnumerateFiles(BuildRoot, "release_build_report.json", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (latest == null) return File.Exists(LatestPreflightPath)
                ? "NO_BUILD; latestPreflight=" + LatestPreflightPath : "NOT_BUILT; expectedRoot=" + BuildRoot;
            ReleaseReport report = JsonUtility.FromJson<ReleaseReport>(File.ReadAllText(latest));
            return report.status + "; result=" + report.buildResult + "; errors=" + report.totalErrors
                + "; warnings=" + report.totalWarnings + "; executable=" + report.executable + "; report=" + latest;
        }

        private static void ReadBuildReport(BuildReport unityReport, ReleaseReport report)
        {
            if (unityReport == null)
            {
                report.buildResult = "NO_REPORT";
                return;
            }
            BuildSummary summary = unityReport.summary;
            report.buildResult = summary.result.ToString();
            report.totalSizeBytes = summary.totalSize;
            report.totalSeconds = summary.totalTime.TotalSeconds;
            report.totalErrors = summary.totalErrors;
            report.totalWarnings = summary.totalWarnings;
            report.buildMessages = unityReport.steps.SelectMany(step => step.messages.Select(message =>
                message.type + " | " + step.name + " | " + message.content)).ToArray();
        }

        private static void WriteBuildLog(ReleaseReport report)
        {
            var lines = new List<string>
            {
                Label + " / QA COMPLETE: NO",
                "Status: " + report.status,
                "Started UTC: " + report.startedUtc,
                "Finished UTC: " + report.finishedUtc,
                "Unity: " + report.unityVersion,
                "Target: " + report.target + " / " + report.scriptingBackend,
                "Scene[0]: " + report.scene,
                "Build Settings override applied: " + report.buildSettingsOverrideApplied,
                "Build Settings restored: " + report.buildSettingsRestored,
                "Build Settings file: " + report.buildSettingsFile,
                "Build Settings file restored: " + report.buildSettingsFileRestored,
                "Build Settings SHA-256 before: " + report.buildSettingsHashBefore,
                "Build Settings SHA-256 during: " + report.buildSettingsHashDuring,
                "Build Settings SHA-256 after: " + report.buildSettingsHashAfter,
                "Executable: " + report.executable,
                "Result: " + report.buildResult,
                "Size bytes: " + report.totalSizeBytes,
                "Duration seconds: " + report.totalSeconds.ToString("F2"),
                "Errors: " + report.totalErrors + " / Warnings: " + report.totalWarnings,
                string.Empty,
                "Preflight:"
            };
            lines.AddRange(report.checks.Select(check => (check.pass ? "PASS" : "FAIL") + " | " + check.name + " | " + check.detail));
            lines.Add(string.Empty);
            lines.Add("Unity build messages:");
            lines.AddRange(report.buildMessages);
            File.WriteAllLines(report.log, lines, Encoding.UTF8);
        }

        private static string KoreanReadme()
        {
            return "오행부 외부 플레이테스트 — BUILD CANDIDATE\r\n"
                + "QA 완료본이 아닙니다. 동봉된 폴더 전체를 같은 위치 관계로 유지하여 실행하세요. EXE만 따로 복사하면 실행되지 않습니다.\r\n\r\n"
                + "실행: Oheangbu_Playtest.exe\r\n"
                + "걷기: WASD (2.2m/s) / 달리기: Ctrl+WASD (5.5m/s) / 시점: 마우스\r\n"
                + "점프: 서서 지상에서 Space / 웅크리기 전환: C (1.2m/s) / 바닥 착석·기립: X / 이동 입력으로도 기립\r\n"
                + "회피: 서 있을 때 왼쪽 Shift / 락온: Tab / 착석 자체에는 회복·저장 효과 없음\r\n"
                + "웅크린 상태에서는 이동·작도·갈무리를 허용하며 점프·달리기·회피는 서 있을 때 시작합니다. 작도·갈무리 중 C는 무시합니다. 공중·착석 중에는 전투 입력을 받지 않습니다.\r\n"
                + "먹 갈무리: 비작도 상태에서 마우스 왼쪽 버튼 유지\r\n"
                + "작도: Q를 누른 채 마우스 왼쪽 버튼으로 획을 그리고 Q를 놓아 확정\r\n"
                + "상호작용: F\r\n"
                + "메뉴: Esc / 소지품: I / 지도: M (메뉴가 열린 동안 플레이 정지)\r\n"
                + "지도: 드래그 이동 / 휠 확대·축소 / 현재 위치·전체 보기 / 우클릭 표식\r\n"
                + "종료: 메뉴의 종료·로비 복귀에서 저장 확인 / Alt+F4\r\n"
                + "가마: E 탑승·하차 / WASD 주행 / Space 제동 / V 시점 전환 / 오른쪽 마우스 버튼 시점\r\n\r\n"
                + "소환 글자: 곰·놈·몸·솜·옴\r\n"
                + "현재 5종 소환은 제한 시간 동안 보이는 정적 연출입니다. 이동·애니메이션·AI·공격·피해·어그로·충돌·물리는 없습니다.\r\n"
                + "석경 파편: 폐광 시작·출구·금표 주막·산길 쉼터·황경 앞에서 F로 획득합니다.\r\n"
                + "파편은 도감 설명을 공개하며, 기존 시전 가능 여부는 바꾸지 않습니다.\r\n"
                + "로비의 새 게임은 기존 저장을 별도로 보관합니다. 옵션은 진행과 따로 저장됩니다.\r\n"
                + "알려진 제한: 차량 주행 중 종료 후 이어하기는 마지막 안전 보행 위치로 복원됩니다.\r\n"
                + "고밀도 식생은 120fps 목표를 모든 구간에서 보장하지 않습니다. 실제 보행·경사·계단·작도·장치 소리는 이번 플레이에서 확인해 주세요.\r\n"
                + "화면 설정 변경은 15초 안에 확인하지 않으면 이전 상태로 복원됩니다.\r\n"
                + "한국어 글꼴: Noto Sans CJK KR / SIL Open Font License (동봉 라이선스 참조).\r\n"
                + "문제 제보 시 재현 위치, 입력, 화면, build.log와 release_build_report.json을 함께 전달해 주세요.\r\n";
        }

        private static T[] Components<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
        }

        private static bool UsesBook(Object consumer, SpellBookSO book)
        {
            var serialized = new SerializedObject(consumer);
            SerializedProperty property = serialized.FindProperty("_spellBook");
            return property != null && ReferenceEquals(property.objectReferenceValue, book);
        }

        private static string[] DescribeBuildSettings(EditorBuildSettingsScene[] scenes)
        {
            return (scenes ?? Array.Empty<EditorBuildSettingsScene>()).Select(scene =>
                scene.path + "|enabled=" + scene.enabled + "|guid=" + scene.guid).ToArray();
        }

        private static bool SameBuildSettings(EditorBuildSettingsScene[] expected, EditorBuildSettingsScene[] actual)
        {
            expected = expected ?? Array.Empty<EditorBuildSettingsScene>();
            actual = actual ?? Array.Empty<EditorBuildSettingsScene>();
            if (expected.Length != actual.Length) return false;
            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i].enabled != actual[i].enabled
                    || !string.Equals(expected[i].path, actual[i].path, StringComparison.Ordinal)
                    || !string.Equals(expected[i].guid.ToString(), actual[i].guid.ToString(), StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var result = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) result.Append(value.ToString("X2"));
                return result.ToString();
            }
        }

        private static void Add(List<Check> checks, string name, bool pass, string detail)
        {
            checks.Add(new Check { name = name, pass = pass, detail = detail });
        }
    }
}
