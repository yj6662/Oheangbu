using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Core;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Oheangbu.EditorTools
{
    /// <summary>Cross-component pause timing and failed lobby load. All world IO is in a UUID temp directory.</summary>
    public static class DemoSouthGateIntegrationChecks
    {
        [Serializable] sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public string[] unverified = { "Native keyboard I/M/Escape dispatch and controller navigation", "Successful scene load and actual ending door animation", "Native frame ordering in Play mode; tests explicitly order real component callbacks" };
        }
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        static void Field(object instance, string name, object value) => instance.GetType().GetField(name, Private).SetValue(instance, value);
        static void Property(object instance, string name, object value) => instance.GetType().GetProperty(name).SetValue(instance, value);
        static MethodInfo Method(object instance, string name) => instance.GetType().GetMethod(name, Private) ?? throw new MissingMethodException(name);
        static GameObject Create(Scene scene, string name, bool active = true)
        { var go = new GameObject(name); go.SetActive(active); SceneManager.MoveGameObjectToScene(go, scene); return go; }
        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run isolated integration checks in Edit mode.");
            var report = new Report(); void Check(bool ok, string name) => (ok ? report.passed : report.failed).Add(name);
            float oldTime = Time.timeScale; var oldLock = Cursor.lockState; bool oldCursor = Cursor.visible;
            Scene scene = default; PauseCoordinator pause = null; WorldMacroPlaytestSession session = null;
            GameplayRuntimeStateSO state = null; SouthGateGeneralProfile profile = null; CombatConfigSO config = null; WorldMacroPlaytestSO content = null;
            string folder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OheangbuSouthGateIntegration_" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(folder);
            try
            {
                scene = EditorSceneManager.NewPreviewScene(); state = ScriptableObject.CreateInstance<GameplayRuntimeStateSO>();
                var pauseHost = Create(scene, "Isolated pause owner"); var gate = pauseHost.AddComponent<GameplayUiGate>(); gate.State = state; gate.Initialize();
                pause = pauseHost.AddComponent<PauseCoordinator>(); pause.Gate = gate; pause.Initialize(); Time.timeScale = 1;
                config = ScriptableObject.CreateInstance<CombatConfigSO>(); profile = ScriptableObject.CreateInstance<SouthGateGeneralProfile>();
                var enemy = Create(scene, "Actual general life").AddComponent<EnemyVitals>(); Field(enemy, "_config", config); enemy.Restore();
                var player = Create(scene, "Actual player life").AddComponent<PlayerVitals>(); Field(player, "_config", config); player.Restore(); player.transform.position = Vector3.forward * 2;
                var controller = enemy.gameObject.AddComponent<SouthGateGeneralController>(); float now = Time.time + 100;
                Field(controller, "_clock", (Func<float>)(() => now)); controller.ConfigureProfile(profile, config); controller.Configure(player.transform, player, new ParryJudge(config));
                now += profile.InitialDelay + profile.CancelRecovery + 1;
                Check(controller.TryBeginAttack(SouthGateAttackKind.Thrust), "Real general starts its attack before the menu pause");
                var plan = controller.CurrentPlan; int hits = 0; controller.AttackImpactResolved += _ => hits++;
                if (plan == null) throw new InvalidOperationException("General attack fixture failed to begin");
                now = plan.Pulses[0].ReleaseAt; float hp = player.Hp01;
                pause.Begin(); Check(pause.IsPaused && gate.InputBlocked && Time.timeScale == 0, "PauseCoordinator blocks input and freezes shared gameplay time");
                Method(controller, "Update").Invoke(controller, null);
                Check(hits == 0 && player.Hp01 == hp && !plan.Pulses[0].Consumed,
                    "Pause begins before a release-frame controller update: no pulse or damage may resolve while paused");
                Method(controller, "Update").Invoke(controller, null);
                Check(hits == 0 && !plan.Pulses[0].Consumed, "Repeated paused updates do not consume a pending strike");
                pause.End(); gate.ReleaseImmediately(); Method(controller, "Update").Invoke(controller, null);
                Check(hits == 1 && plan.Pulses[0].Consumed && player.Hp01 < hp, "The same pending strike resolves exactly once after actual pause release");
                Method(controller, "Update").Invoke(controller, null); Check(hits == 1, "Resume does not duplicate a resolved strike");
                controller.AttackEnabled = false;

                // Inactive diagnostic UI avoids Awake, singleton ownership, settings IO and production scene binding.
                var uiHost = Create(scene, "Isolated ending UI", false); var ui = uiHost.AddComponent<PlaytestUiRoot>();
                var settings = uiHost.AddComponent<UserSettingsService>(); Field(settings, "_initialized", true); Field(settings, "_previewing", false);
                Property(ui, "Settings", settings); Property(ui, "Pause", pause); Property(ui, "Gate", gate); Property(ui, "Page", "여정의 끝");
                var modal = PlaytestUiView.Stretch("Retained ending modal", uiHost.transform); var marker = PlaytestUiView.Rect("Retained ending content", modal, 0, 0, 10, 10);
                var notice = PlaytestUiView.Rect("Diagnostic notice", uiHost.transform, 0, 0, 10, 10); var text = notice.gameObject.AddComponent<Text>();
                Field(ui, "modalLayer", modal); Field(ui, "noticeRoot", notice); Field(ui, "noticeText", text);
                var map = Create(scene, "Retained map", false).AddComponent<WorldMapPresenter>(); Property(ui, "Map", map);
                var sessionHost = Create(scene, "Isolated saved completion", false); session = sessionHost.AddComponent<WorldMacroPlaytestSession>(); session.Actors = Array.Empty<PrologueEncounter>();
                content = ScriptableObject.CreateInstance<WorldMacroPlaytestSO>(); session.Content = content;
                var progress = WorldMacroProgress.CreateNew("fixture", Vector3.zero, 0); progress.ledger.currency = 123;
                Property(session, "Progress", progress); Field(session, "ready", true);
                string path = Path.Combine(folder, "completion.json"); Field(session, "store", new AtomicJsonStore<WorldMacroProgress>(path, WorldMacroProgress.Valid));
                Property(ui, "Session", session);
                var detail = (Action<string, string>)Delegate.CreateDelegate(typeof(Action<string, string>), ui, Method(ui, "ShowDetail")); session.DetailRequested += detail;
                pause.Begin(); int depth = pause.Depth;
                ui.OpenPage("소지품"); ui.OpenPage("지도"); ui.CloseMenu(); ui.Back();
                Check(ui.ShowingDemoEnding && pause.Depth == depth && Time.timeScale == 0 && gate.InputBlocked && ReferenceEquals(ui.Map, map) && !map.gameObject.activeSelf,
                    "Ending page rejects inventory/map/back/close routes while retaining pause and map state");
                // A UUID scene name is guaranteed not to be a registered scene. Edit mode may reject loading
                // earlier; either exception/null path must preserve the existing UI and active scene.
                ui.TitleSceneName = "__Oheangbu_Missing_Title_" + Guid.NewGuid().ToString("N");
                int activeScene = SceneManager.GetActiveScene().handle; int sceneCount = SceneManager.sceneCount;
                var routine = (IEnumerator)Method(ui, "ReturnTitle").Invoke(ui, new object[] { false });
                Check(!routine.MoveNext(), "Rejected native lobby load terminates the coroutine without awaiting a scene");
                Check(!ui.Busy && ui.ShowingDemoEnding && pause.Depth == depth && Time.timeScale == 0 && gate.InputBlocked,
                    "Load failure retains the ending screen, pause ownership and blocked gameplay input");
                Check(marker.gameObject.activeSelf && ReferenceEquals(ui.Map, map) && ReferenceEquals(ui.Session, session),
                    "Load failure preserves existing ending content, map instance and session binding");
                var subscriptions = (Action<string, string>)typeof(WorldMacroPlaytestSession).GetField("DetailRequested", Private).GetValue(session);
                Check(subscriptions != null && Array.Exists(subscriptions.GetInvocationList(), callback => callback.Equals(detail)),
                    "Load failure does not unhook session UI subscriptions");
                Check(!string.IsNullOrEmpty(ui.LastError) && text.text == ui.LastError && notice.gameObject.activeSelf,
                    "Failed lobby load leaves a visible error and an available retry");
                Check(SceneManager.GetActiveScene().handle == activeScene && SceneManager.sceneCount == sceneCount,
                    "Diagnostic failed load changes neither active nor loaded production scenes");
                var stored = new AtomicJsonStore<WorldMacroProgress>(path, WorldMacroProgress.Valid).Load();
                Check(stored != null && stored.ledger.currency == 123, "Accepted pre-exit save remains intact when subsequent lobby loading fails");
                routine = (IEnumerator)Method(ui, "ReturnTitle").Invoke(ui, new object[] { false }); routine.MoveNext();
                Check(!ui.Busy && ui.ShowingDemoEnding && pause.Depth == depth && Time.timeScale == 0, "Repeated rejected lobby load does not accumulate or release pause ownership");
                session.DetailRequested -= detail;
            }
            catch (Exception e) { report.failed.Add(e.ToString()); }
            finally
            {
                if (session != null) Field(session, "ready", false);
                if (pause != null) pause.ForceResume();
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                if (state != null) UnityEngine.Object.DestroyImmediate(state); if (profile != null) UnityEngine.Object.DestroyImmediate(profile);
                if (config != null) UnityEngine.Object.DestroyImmediate(config); if (content != null) UnityEngine.Object.DestroyImmediate(content);
                Time.timeScale = oldTime; Cursor.lockState = oldLock; Cursor.visible = oldCursor;
                string parent = Path.GetDirectoryName(folder).TrimEnd(Path.DirectorySeparatorChar);
                if (string.Equals(parent, Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(folder).StartsWith("OheangbuSouthGateIntegration_", StringComparison.Ordinal)) Directory.Delete(folder, true);
            }
            report.status = report.failed.Count == 0 ? "PASS" : "FAIL"; return JsonUtility.ToJson(report, true);
        }
    }
}
