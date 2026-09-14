using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Explicit, isolated presentation diagnostic. Uses the real InkPool event/HUD wiring and
    // actual gesture brush tip, but never asks HarvestAction to damage or extract from an enemy.
    [InitializeOnLoad]
    public static class PlaytestInkFeedbackReview
    {
        const float InitialInk = .34f, GainAmount = .01f;
        const int GainCount = 16;
        enum Phase { Baseline, Hold, Release, WaitScreenshot }
        [Serializable] sealed class Still
        {
            public string id, path;
            public int issuedFrame, width, height, gainEvents, strands, droplets;
            public float simulatedSeconds, poolInk, hudInk, receivedAlpha, endpointErrorMeters, commitRatio;
            public Vector2 receivedSegment;
            public Vector3 source, actualTip, tipViewport;
            public bool flowVisible;
        }
        [Serializable] sealed class Report
        {
            public string status, utc, sourceKind, saveSuffix;
            public string scope = "Synthetic extraction presentation with real GestureRig.EffectTip, production HarvestInkFlowRenderer/profile, actual InkPool.Gain and existing HUD event subscription. Real 1920x1080 Game View stills. Not native input, enemy extraction, damage, economy balance, or player harvest-animation validation.";
            public int positiveGainEvents, restoreGainEvents;
            public float positiveGainTotal, expectedGain = GainCount * GainAmount, maximumTipErrorMeters;
            public bool actualSinkBinding, receivedSegmentCorrect, releaseCleared, restored;
            public float maximumGainWallGapSeconds;
            public int maximumGainFrameGap;
            public List<Still> screenshots = new List<Still>();
            public List<GainEvent> gains = new List<GainEvent>();
            public List<string> failures = new List<string>();
        }
        [Serializable] sealed class GainEvent { public int frame; public float unscaledTime, amount; public Vector2 hudSegment; }
        sealed class SavedFile { public string path; public byte[] bytes; public DateTime modified; }
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/PlaytestPolish/AudioInk/InkFeedback"));
        static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly string[] HudFields = { "_receivedFrom", "_receivedTo", "_receivedLast", "_receivedBegan", "_receivedLastFrame" };
        static readonly Dictionary<string, object> SavedHud = new Dictionary<string, object>();
        static readonly List<SavedFile> SavedFiles = new List<SavedFile>();
        static WorldMacroPlaytestSession session;
        static PlaytestUiRoot ui;
        static HudController hud;
        static InkPool ink;
        static PlayerVitals vitals;
        static WorldMacroPlayerGestureRig gesture;
        static HarvestInkStreamEffect effect;
        static HarvestInkFlowRenderer flow;
        static Camera camera;
        static GameObject owned;
        static EditorWindow priorWindow;
        static Report report;
        static Still pending;
        static string folder, progressBefore;
        static Phase phase, phaseAfterScreenshot;
        static bool running, pausedByReview, oldSessionEnabled, oldRun, finishAfterScreenshot, subscribed;
        static float oldInk, oldHp, oldTimeScale, elapsed;
        static Vector3 source, cameraPosition, feetBefore;
        static Quaternion cameraRotation;
        static float cameraFov;
        static CursorLockMode oldCursorLock;
        static bool oldCursorVisible;
        static double deadline;
        static int lastFrame, gainsIssued;

        static PlaytestInkFeedbackReview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += () => { if (running) Stop("Assembly reload interrupted the diagnostic"); };
            EditorApplication.playModeStateChanged += state => { if (running && state == PlayModeStateChange.ExitingPlayMode) Stop("Play mode exited"); };
        }
        public static string Execute(string action)
        {
            if (action == "poll") return running ? "RUNNING ink feedback " + phase : File.Exists(Output + "/ink_feedback.json") ? File.ReadAllText(Output + "/ink_feedback.json") : "NOT_RUN";
            if (action == "abort") { if (running) Stop("Aborted explicitly"); return "Ink diagnostic stopped and restored"; }
            if (action != "begin") throw new ArgumentException("Ink feedback diagnostic: begin | poll | abort");
            Need(!running && EditorApplication.isPlaying && !EditorApplication.isPaused, "Existing unpaused Play mode required");
            session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>(); ui = PlaytestUiRoot.Instance;
            hud = Object.FindFirstObjectByType<HudController>(); effect = Object.FindFirstObjectByType<HarvestInkStreamEffect>();
            gesture = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>(); camera = Camera.main;
            Need(session != null && session.Progress != null && !string.IsNullOrWhiteSpace(session.TestSaveSuffix) && session.TestSaveSuffix != "_", "Isolated non-empty TestSaveSuffix required");
            Need(ui != null && ui.Pause != null && !ui.Pause.IsPaused && ui.Page.Length == 0 && !ui.Map.Folding && !ui.Map.Expanded, "Close menus and finish other reviews first");
            Need(session.Walker != null && !session.Walker.Seated && !session.Walker.Drawing.InDrawMode, "Idle, unseated player required");
            Need(gesture != null && gesture.IsPresentationReady && gesture.EffectTip != null && effect != null && effect.SinkAnchor == gesture.EffectTip, "Actual gesture EffectTip must be the bound harvest sink");
            Need(effect.FlowProfile != null && effect.FlowProfile.Material != null && !effect.FlowVisible, "Prepared, idle ink flow required");
            var harvest = Object.FindFirstObjectByType<HarvestAction>();
            Need(harvest == null || !harvest.IsExtracting, "Release extraction before diagnostics");
            Need(camera != null && hud != null && hud.IsSkinned, "Actual camera and skinned HUD required");
            ink = Get<InkPool>(session, "ink"); vitals = session.Walker.Body.GetComponent<PlayerVitals>();
            Need(ink != null && vitals != null, "Injected actual ink pool and HP required");
            NeedResolution(); Need(Prologue.PrologueAudit.CommitRatio() < .85f, "System commit >=85%; no new capture");
            Need(Get<RectTransform>(hud, "_inkReceivedMask") != null && Get<Image>(hud, "_inkReceivedImage") != null, "Received-segment HUD is missing");
            report = new Report { status = "RUNNING", utc = DateTime.UtcNow.ToString("O"), saveSuffix = session.TestSaveSuffix, actualSinkBinding = true };
            folder = Output + "/Screenshots/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            Directory.CreateDirectory(folder); SavedHud.Clear(); SavedFiles.Clear();
            foreach (string field in HudFields) SavedHud[field] = Field(hud, field).GetValue(hud);
            oldInk = ink.Value; oldHp = vitals.Hp01; oldTimeScale = Time.timeScale;
            oldCursorLock = Cursor.lockState; oldCursorVisible = Cursor.visible;
            oldSessionEnabled = session.enabled; oldRun = Application.runInBackground; priorWindow = EditorWindow.focusedWindow;
            progressBefore = JsonUtility.ToJson(session.Progress); feetBefore = session.Walker.Body.transform.position;
            cameraPosition = camera.transform.position; cameraRotation = camera.transform.rotation; cameraFov = camera.fieldOfView;
            string slot = WorldMacroSaveSlot.GetPrimaryPath(Application.persistentDataPath, session.Content.SaveSlot + session.TestSaveSuffix);
            foreach (string path in new[] { slot, slot + ".bak", slot + ".tmp" }) SavedFiles.Add(new SavedFile { path = path, bytes = File.Exists(path) ? File.ReadAllBytes(path) : null, modified = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default });
            running = true; pausedByReview = false; subscribed = false; gainsIssued = 0; finishAfterScreenshot = false;
            try
            {
                // Prevent an unscaled autosave while the real pool temporarily receives diagnostic ink.
                session.enabled = false; ui.Pause.Begin(); pausedByReview = true; Application.runInBackground = true;
                ink.Gained += OnGain; subscribed = true; ink.Restore(InitialInk); report.restoreGainEvents = report.positiveGainEvents;
                Need(Mathf.Abs(hud.Ink01 - InitialInk) < .0001f, "Actual InkPool -> HUD Changed subscription is absent");
                owned = new GameObject("Temporary_InkFeedback_QA") { hideFlags = HideFlags.HideAndDontSave };
                flow = new HarvestInkFlowRenderer(owned.transform, effect.FlowProfile);
                source = ChooseSource();
                Type gameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                if (gameView != null) EditorWindow.GetWindow(gameView, false, "Game", true).Focus();
                phase = Phase.Baseline; elapsed = 0; lastFrame = -1; deadline = EditorApplication.timeSinceStartup + 45;
                EditorApplication.update += Tick; WriteReport();
                return "RUNNING: ink flow + actual positive-income HUD diagnostic; three sequential 1080p Game View stills; poll via PlaytestInkFeedbackReview.Execute(\"poll\")";
            }
            catch (Exception exception) { Stop(exception.ToString()); throw; }
        }
        static Vector3 ChooseSource()
        {
            Vector3 best = camera.ViewportToWorldPoint(new Vector3(.52f, .54f, 4f)); float distance = 25f;
            var harvest = Object.FindFirstObjectByType<HarvestAction>();
            var config = harvest != null ? Get<CombatConfigSO>(harvest, "_config") : null;
            float actualRange = config != null ? config.HarvestRange : 4f;
            report.sourceKind = "Explicit diagnostic viewport source; no enemy or hit event generated";
            foreach (var enemy in Object.FindObjectsByType<EnemyVitals>(FindObjectsSortMode.None))
            {
                var p = enemy.transform.position + Vector3.up * 1.0f; var view = camera.WorldToViewportPoint(p);
                if (harvest == null || Vector3.Distance(harvest.transform.position, enemy.transform.position) > actualRange) continue;
                if (view.x < .12f || view.x > .88f || view.y < .15f || view.y > .85f || view.z < 1.2f || view.z > distance) continue;
                best = p; distance = view.z; report.sourceKind = "Existing visible enemy chest position only; no target state, extraction or damage mutated: " + enemy.name;
            }
            return best;
        }
        static void OnGain(float amount)
        {
            if (report.gains.Count > 0)
            {
                var previous = report.gains[report.gains.Count - 1];
                report.maximumGainWallGapSeconds = Mathf.Max(report.maximumGainWallGapSeconds, Time.unscaledTime - previous.unscaledTime);
                report.maximumGainFrameGap = Mathf.Max(report.maximumGainFrameGap, Time.frameCount - previous.frame);
            }
            report.gains.Add(new GainEvent { frame = Time.frameCount, unscaledTime = Time.unscaledTime, amount = amount, hudSegment = hud.ReceivedInkSegment });
            report.positiveGainEvents++; report.positiveGainTotal += amount;
        }
        static void Tick()
        {
            if (!running) return;
            try
            {
                Need(EditorApplication.isPlaying && !EditorApplication.isPaused, "Play stopped or Editor paused");
                Need(EditorApplication.timeSinceStartup < deadline, "Ink diagnostic exceeded 45 seconds");
                if (lastFrame == Time.frameCount) return; lastFrame = Time.frameCount;
                Need(ui != null && ui.Pause.IsPaused && Time.timeScale == 0 && session != null && gesture != null, "Paused diagnostic state was changed externally");
                Need((session.Walker.Body.transform.position - feetBefore).sqrMagnitude < .000001f, "Player moved during the isolated review");
                float dt = Mathf.Clamp(Time.unscaledDeltaTime, .001f, .1f);
                bool hold = phase == Phase.Hold || phase == Phase.WaitScreenshot && phaseAfterScreenshot == Phase.Release && !finishAfterScreenshot;
                flow.Tick(hold, source, gesture.EffectTip.position, gesture.EffectTip.up, dt);
                if (phase == Phase.WaitScreenshot)
                {
                    if (Time.frameCount < pending.issuedFrame + 3 || !File.Exists(pending.path)) return;
                    if (finishAfterScreenshot) { Stop(null); return; }
                    phase = phaseAfterScreenshot; elapsed = 0; return;
                }
                elapsed += dt;
                switch (phase)
                {
                    case Phase.Baseline:
                        if (elapsed >= .12f) Issue("01_before_income", Phase.Hold, false);
                        break;
                    case Phase.Hold:
                        // Production harvesting pays every gameplay frame. Match that cadence instead
                        // of scheduling a second clamped clock against the HUD's unscaled clock.
                        if (elapsed >= .24f && gainsIssued < GainCount) { ink.Gain(GainAmount); gainsIssued++; }
                        CheckTip();
                        if (gainsIssued == GainCount)
                        {
                            Vector2 segment = hud.ReceivedInkSegment;
                            report.receivedSegmentCorrect = Mathf.Abs(segment.x - InitialInk) < .0002f && Mathf.Abs(segment.y - (InitialInk + GainCount * GainAmount)) < .0002f;
                            if (!report.receivedSegmentCorrect) report.failures.Add("HUD coalesced received segment differs from the real positive gain range: " + segment);
                            Issue("02_receiving_ink_at_actual_tip", Phase.Release, false);
                        }
                        break;
                    case Phase.Release:
                        if (elapsed >= .56f)
                        {
                            report.releaseCleared = !flow.Visible && flow.LiveDroplets == 0 && owned.GetComponentsInChildren<LineRenderer>().All(line => !line.enabled);
                            if (!report.releaseCleared) report.failures.Add("Ink presentation remained after release");
                            Issue("03_after_release", Phase.Release, true);
                        }
                        break;
                }
            }
            catch (Exception exception) { Stop(exception.ToString()); }
        }
        static float CheckTip()
        {
            float error = 0;
            foreach (var line in owned.GetComponentsInChildren<LineRenderer>())
                if (line.enabled && line.positionCount > 0) error = Mathf.Max(error, Vector3.Distance(line.GetPosition(line.positionCount - 1), gesture.EffectTip.position));
            // The initial reveal deliberately has not reached the tip yet.
            if (elapsed >= effect.FlowProfile.RevealSeconds + .05f)
            {
                report.maximumTipErrorMeters = Mathf.Max(report.maximumTipErrorMeters, error);
                if (error > .002f && !report.failures.Contains("Revealed thread misses actual tip by >2mm")) report.failures.Add("Revealed thread misses actual tip by >2mm");
            }
            return error;
        }
        static void Issue(string id, Phase after, bool finish)
        {
            NeedResolution(); float commit = Prologue.PrologueAudit.CommitRatio(); Need(commit < .85f, "System commit >=85%; next screenshot aborted");
            var receivedImage = Get<Image>(hud, "_inkReceivedImage");
            pending = new Still { id = id, path = folder + "/" + id + "_1920x1080.png", issuedFrame = Time.frameCount, width = Screen.width, height = Screen.height,
                simulatedSeconds = elapsed, gainEvents = report.positiveGainEvents, poolInk = ink.Value, hudInk = hud.Ink01, receivedSegment = hud.ReceivedInkSegment,
                receivedAlpha = receivedImage != null && receivedImage.gameObject.activeInHierarchy ? receivedImage.color.a : 0,
                strands = flow.StrandCount, droplets = flow.LiveDroplets, flowVisible = flow.Visible, source = source, actualTip = gesture.EffectTip.position,
                tipViewport = camera.WorldToViewportPoint(gesture.EffectTip.position), endpointErrorMeters = flow.Visible ? CheckTip() : 0, commitRatio = commit };
            Need(!File.Exists(pending.path), "Refusing to overwrite screenshot");
            if (id == "02_receiving_ink_at_actual_tip" && pending.receivedAlpha <= .05f) report.failures.Add("Received HUD highlight is not visible at capture");
            if (id == "02_receiving_ink_at_actual_tip" && (pending.tipViewport.z <= 0 || pending.tipViewport.x < 0 || pending.tipViewport.x > 1 || pending.tipViewport.y < 0 || pending.tipViewport.y > 1)) report.failures.Add("Actual brush tip is outside current camera; composition requires a separate pose/camera review");
            report.screenshots.Add(pending); ScreenCapture.CaptureScreenshot(pending.path, 1);
            phaseAfterScreenshot = after; finishAfterScreenshot = finish; phase = Phase.WaitScreenshot; WriteReport();
        }
        static void Stop(string failure)
        {
            if (!running) return;
            if (!string.IsNullOrEmpty(failure)) report.failures.Add(failure);
            EditorApplication.update -= Tick; running = false;
            void Restore(Action action) { try { action(); } catch (Exception e) { report.failures.Add("Restore: " + e.Message); } }
            Restore(() => { if (subscribed && ink != null) ink.Gained -= OnGain; subscribed = false; });
            Restore(() => flow?.Dispose()); flow = null;
            Restore(() => { if (owned != null) Object.DestroyImmediate(owned); }); owned = null;
            Restore(() => { ink?.Restore(oldInk); if (vitals != null) vitals.Restore(oldHp); });
            Restore(() => { if (session != null && session.Progress != null) JsonUtility.FromJsonOverwrite(progressBefore, session.Progress); });
            Restore(() =>
            {
                if (hud == null) return;
                foreach (var pair in SavedHud) Field(hud, pair.Key).SetValue(hud, pair.Value);
                typeof(HudController).GetMethod("UpdateInkReceived", Fields).Invoke(hud, null);
            });
            Restore(() => { if (camera != null) { camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation); camera.fieldOfView = cameraFov; } });
            Restore(() => { if (pausedByReview && ui != null) ui.Pause.End(); pausedByReview = false; Time.timeScale = oldTimeScale; Cursor.lockState = oldCursorLock; Cursor.visible = oldCursorVisible; });
            Restore(() => { if (session != null) session.enabled = oldSessionEnabled; Application.runInBackground = oldRun; });
            foreach (var file in SavedFiles) Restore(() =>
            {
                if (file.bytes == null) { if (File.Exists(file.path)) File.Delete(file.path); }
                else { File.WriteAllBytes(file.path, file.bytes); File.SetLastWriteTimeUtc(file.path, file.modified); }
            });
            Restore(() => { if (priorWindow != null) priorWindow.Focus(); });
            report.restored = ink != null && Mathf.Abs(ink.Value - oldInk) < .00001f && vitals != null && Mathf.Abs(vitals.Hp01 - oldHp) < .00001f && ui != null && ui.Page.Length == 0 && !ui.Pause.IsPaused;
            if (!report.restored) report.failures.Add("Ink/HP/menu restoration check failed");
            if (string.IsNullOrEmpty(failure))
            {
                if (report.positiveGainEvents != GainCount || Mathf.Abs(report.positiveGainTotal - report.expectedGain) > .0001f) report.failures.Add("Real positive gain count/total mismatch");
                if (report.restoreGainEvents != 0) report.failures.Add("Restore incorrectly generated gain feedback");
            }
            report.status = report.failures.Count == 0 ? "PASS_SYNTHETIC_PRESENTATION_AND_REAL_GAIN_EVENTS" : "FINDINGS";
            WriteReport();
            string cards = string.Join("", report.screenshots.Select(shot => "<article><h2>" + shot.id + "</h2><img src='" + Path.GetRelativePath(Output, shot.path).Replace('\\', '/') + "'><p>먹 " + shot.poolInk.ToString("P0") + " · 수급 이벤트 " + shot.gainEvents + " · 붓 끝 오차 " + (shot.endpointErrorMeters * 1000).ToString("F2") + "mm</p></article>"));
            File.WriteAllText(Output + "/REVIEW.html", "<!doctype html><meta charset='utf-8'><title>먹 수급 표현 진단</title><style>body{background:#eee7d8;color:#302c26;font:17px sans-serif;max-width:1180px;margin:35px auto}img{width:100%}article{margin:25px 0}</style><h1>먹 수급 표현 진단</h1><p>임시 추출 표현 신호와 실제 InkPool.Gain/HUD 이벤트를 사용했습니다. 기존 붓 끝으로 흐르는지를 검사하며 실제 입력·적 갈무리·피해·캐릭터 갈무리 동작의 통과를 의미하지 않습니다. 테스트 후 먹·체력·진행·저장·메뉴·카메라를 복원합니다.</p><p>" + report.status + "</p>" + cards);
        }
        static void WriteReport() { Directory.CreateDirectory(Output); File.WriteAllText(Output + "/ink_feedback.json", JsonUtility.ToJson(report, true)); }
        static FieldInfo Field(object target, string name) => target.GetType().GetField(name, Fields) ?? throw new MissingFieldException(target.GetType().Name, name);
        static T Get<T>(object target, string name) => (T)Field(target, name).GetValue(target);
        static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void NeedResolution() => Need(Screen.width == 1920 && Screen.height == 1080, "Set actual Game View to 1920x1080 before the ink diagnostic; no rescaling or surrogate camera capture");
    }
}
