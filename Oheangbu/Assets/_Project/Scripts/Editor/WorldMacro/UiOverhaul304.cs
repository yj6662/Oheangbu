using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#304 UI overhaul review: fixed 1920x1080 Game View and scripted page/overlay tours captured with ScreenCapture
    /// (UI included). Queue: Oheangbu.EditorTools.WorldMacro.UiOverhaul304 Execute. Tours run over several frames; poll tour-status.</summary>
    [InitializeOnLoad]
    public static partial class UiOverhaul304
    {
        const string SizeKey = "UiOverhaul304.PreviousSize";
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/UI304"));

        [Serializable] sealed class TourState
        {
            public string status = "idle", set = "", error = "";
            public List<string> log = new List<string>(); public List<string> shots = new List<string>(); public List<string> skipped = new List<string>();
            public List<string> checks = new List<string>();    // #304 QA: PASS / FAIL lines of page-default / rail verdicts
            public List<string> reports = new List<string>();   // #304 QA: interact-probe / focus-report JSON files
        }
        static TourState state = new TourState();
        static Queue<string> steps = new Queue<string>();
        static int waitFrames, lastFrame = -1; static string pendingShot; static double deadline;
        static readonly List<Canvas> hidden = new List<Canvas>();
        // #304 capture steps change HUD display values / the reticle preview only; they are put back when the tour ends
        static readonly List<Action> tourRestore = new List<Action>();
        static bool tourHpSaved, tourInkSaved, tourLockHeld;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void TourResetStatics() { tourRestore.Clear(); tourHpSaved = tourInkSaved = tourLockHeld = false; ProbeResetStatics(); }

        static UiOverhaul304()
        {
            AssemblyReloadEvents.beforeAssemblyReload += () => { if (state.status == "running") Fail("assembly reload"); };
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.ExitingPlayMode && state.status == "running") Fail("play mode exit");
                if (s == PlayModeStateChange.ExitingPlayMode || s == PlayModeStateChange.EnteredEditMode) TourResetStatics();
            };
        }

        public static string Execute(string argument)
        {
            string a = (argument ?? "").Trim();
            if (a == "size") return SetSize(1920, 1080);
            if (a == "size-restore") return RestoreSize();
            if (a == "tour-status") return JsonUtility.ToJson(state, true);
            if (a == "status") return "playing=" + EditorApplication.isPlaying + " screen=" + Screen.width + "x" + Screen.height
                + " page=" + (PlaytestUiRoot.Instance != null ? PlaytestUiRoot.Instance.Page : "<no root>") + " tour=" + state.status;
            if (a.StartsWith("open:", StringComparison.Ordinal))
            {
                Need(!EditorApplication.isPlayingOrWillChangePlaymode, "open is Edit Mode only.");
                var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                Need(!active.isDirty, "Active scene " + active.path + " has unsaved changes; refusing to switch.");
                var opened = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(a.Substring(5), UnityEditor.SceneManagement.OpenSceneMode.Single);
                return "OPENED " + opened.path;
            }
            if (a == "play") { Need(!EditorApplication.isPlaying, "already playing"); EditorApplication.isPlaying = true; return "ENTERING_PLAY"; }
            if (a == "stop") { EditorApplication.isPlaying = false; return "STOPPING"; }
            if (a.StartsWith("tour:", StringComparison.Ordinal))
            {
                // tour:<set>|step|step... steps: page:<name> close back notice:<title>[\n<source>][~<seconds>] detail:<title>~<body>
                //   confirm:<title>~<message> dismiss loading[:<label>] wait:<frames> shot:<name> canvases:<off|on>
                //   #304: toast:<kind>~<title>~<source>[~<glyphs>] arrival[:<region>~<place>] hp:<0..1> ink:<0..1>
                //         lock:<on|off>[~<groggy 0..1>] select:<GameObject name> (empty = clear the selection)
                //   #304 QA (UiOverhaul304.Probe304.cs): interact-probe[:name] near:<point id>[~metres] reveal-map[:on|off]
                //         focus-report[:name] page-default:<page> rail:<page>  (tour-status: checks / reports)
                //   #304 QA2: settle[:max s] alpha-report[:name[~s]] title-page:<options|controls>
                string[] parts = a.Substring(5).Split('|');
                Need(EditorApplication.isPlaying && !EditorApplication.isPaused, "Needs an unpaused Play session.");
                Need(state.status != "running", "A tour is already running.");
                state = new TourState { status = "running", set = parts[0] };
                steps = new Queue<string>(parts.Skip(1).Where(p => p.Length > 0));
                waitFrames = 2; pendingShot = null; lastFrame = -1; deadline = EditorApplication.timeSinceStartup + 60 + steps.Count * 4;
                TourResetStatics();
                FocusGameView();
                EditorApplication.update -= Tick; EditorApplication.update += Tick;
                return "TOUR_STARTED set=" + state.set + " steps=" + steps.Count + " out=" + Path.Combine(Output, state.set);
            }
            string more = ExecuteMore(a); if (more != null) return more;
            throw new ArgumentException("Expected size, size-restore, status, open:<scene>, play, stop, tour:<set>|steps..., tour-status, tmp-setup, tmp-fonts");
        }

        static void Tick()
        {
            if (state.status != "running") { EditorApplication.update -= Tick; return; }
            if (EditorApplication.timeSinceStartup > deadline) { Fail("timeout"); return; }
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            try
            {
                if (pendingShot != null)
                {
                    if (!File.Exists(pendingShot)) return;
                    state.shots.Add(pendingShot); pendingShot = null; waitFrames = 1;
                }
                if (waitFrames-- > 0) return;
                if (ProbeWaiting()) return;                           // page-default / rail sub-steps wait for the menu to settle
                if (steps.Count == 0) { TourRestore(); state.status = "done"; EditorApplication.update -= Tick; return; }
                string step = steps.Dequeue(); int colon = step.IndexOf(':');
                string verb = colon < 0 ? step : step.Substring(0, colon), arg = colon < 0 ? "" : step.Substring(colon + 1);
                var root = PlaytestUiRoot.Instance;
                string note = null;
                switch (verb)
                {
                    // #304 QA2: page / close / back / detail / confirm / dismiss / loading also settle (unscaled-time tweens, see SettleAfter)
                    case "page": Need(root != null, "no PlaytestUiRoot"); root.OpenPage(arg); waitFrames = 12; SettleAfter(root, step); break;
                    case "close": if (root != null) { root.CloseMenu(); SettleAfter(root, step); } waitFrames = 12; break;
                    case "notice": note = TourNotice(root, arg); waitFrames = 8; break;
                    case "wait": waitFrames = int.Parse(arg); break;
                    case "detail":   // detail:<title>~<body>  (private PlaytestUiRoot.ShowDetail, the dialogue/story surface)
                    {
                        Need(root != null, "no PlaytestUiRoot"); string[] tb = arg.Split('~');
                        Private(root, "ShowDetail", tb[0], tb.Length > 1 ? tb[1].Replace("\\n", "\n") : ""); waitFrames = 14; SettleAfter(root, step); break;
                    }
                    case "confirm":  // confirm:<title>~<message>
                    {
                        Need(root != null, "no PlaytestUiRoot"); string[] tm = arg.Split('~');
                        Private(root, "Confirm", tm[0], tm.Length > 1 ? tm[1] : "", (Action)(() => { })); waitFrames = 10; SettleAfter(root, step); break;
                    }
                    case "dismiss": if (root != null) { Private(root, "DismissConfirmation"); SettleAfter(root, step); } waitFrames = 8; break;
                    case "loading":  // loading[:<label>][~<progress 0..1>]: no label = the return-to-lobby overlay of ReturnTitle ("여정 기록됨")
                        note = TourLoading(root, arg); waitFrames = 10; SettleAfter(root, step); break;
                    case "back": if (root != null) { root.Back(); SettleAfter(root, step); } waitFrames = 10; break;
                    case "canvases":
                        if (arg == "off")
                        {
                            hidden.Clear();
                            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                                if (c.enabled && c.isRootCanvas) { c.enabled = false; hidden.Add(c); }
                        }
                        else { foreach (var c in hidden) if (c != null) c.enabled = true; hidden.Clear(); }
                        waitFrames = 3; break;
                    case "shot":
                        string dir = Path.Combine(Output, state.set); Directory.CreateDirectory(dir);
                        pendingShot = Path.Combine(dir, arg + ".png"); if (File.Exists(pendingShot)) File.Delete(pendingShot);
                        ScreenCapture.CaptureScreenshot(pendingShot, 1);
                        if (lastSettle != null) note = "lastSettle=" + lastSettle;
                        break;
                    // ---------------------------------------------------------------- #304 capture steps
                    case "toast": note = TourToast(root, arg); waitFrames = 16; break;          // ToastInMs 240
                    case "arrival": note = TourArrival(arg); waitFrames = 20; break;            // ArrivalInMs 280
                    case "hp": note = TourMeter(true, arg); waitFrames = 6; break;               // lag shows MeterLagHoldMs 450: add wait:N
                    case "ink": note = TourMeter(false, arg); waitFrames = 6; break;
                    case "lock": note = TourLock(arg); waitFrames = 12; break;                  // EnsoPopMs 180
                    case "select": note = TourSelect(root, arg); waitFrames = 12; break;        // DabSlideMs 160 + StrokeMs 240
                    default:
                        note = ProbeStep(root, verb, arg);
                        if (note == null) throw new ArgumentException("unknown step " + step);
                        break;
                }
                if (note != null && note.StartsWith("SKIPPED", StringComparison.Ordinal)) state.skipped.Add(step + " -> " + note);
                state.log.Add("f" + Time.frameCount + " " + step + " page=" + (root != null ? root.Page : "-") + " sel=" + SelectedName() + (note != null ? " " + note : ""));
            }
            catch (Exception e) { Fail(e.GetType().Name + ": " + e.Message); }
        }

        // ------------------------------------------------------------------ #304 capture steps
        /// <summary>toast:&lt;kind&gt;~&lt;title&gt;~&lt;source&gt;[~&lt;glyphs&gt;] through the UiNoticeChannelSO of the theme's Style304
        /// (the same channel ShowNotice / OnCollected / Session notices use). kind = UiNoticeKind304 name.</summary>
        static string TourToast(PlaytestUiRoot root, string arg)
        {
            Need(root != null, "no PlaytestUiRoot");
            string[] p = arg.Split('~');
            bool parsed = Enum.TryParse(p[0].Trim(), true, out UiNoticeKind304 kind);
            Need(p.Length >= 2 && parsed && Enum.IsDefined(typeof(UiNoticeKind304), kind),
                "toast:<kind>~<title>~<source>[~<glyphs>] with kind in " + string.Join("/", Enum.GetNames(typeof(UiNoticeKind304))));
            var style = root.Theme != null ? root.Theme.Style304 : null;
            var channel = style != null ? style.Notices : null;
            if (channel == null) return "SKIPPED theme Style304" + (style == null ? "" : ".Notices") + " is not assigned (run ui304-setup)";
            channel.Raise(kind, p[1], p.Length > 2 ? p[2] : null, p.Length > 3 ? p[3] : null);
            return "raised=" + channel.RaisedCount + " listeners=" + channel.HasListeners + " pending=" + channel.PendingCount;
        }

        /// <summary>arrival[:&lt;region&gt;~&lt;place&gt;]: force-shows the arrival card through a public preview method when the HUD code
        /// exposes one (WorldLocationArrival first, then HudController, then any *Arrival* component): Preview304 /
        /// PreviewArrival304 / PreviewArrival with (), (string) or (string, string).</summary>
        static string TourArrival(string arg)
        {
            string[] p = string.IsNullOrEmpty(arg) ? Array.Empty<string>() : arg.Split('~');
            string region = p.Length > 0 && p[0].Length > 0 ? p[0] : null, place = p.Length > 1 && p[1].Length > 0 ? p[1] : null;
            string[] names = { "Preview304", "PreviewArrival304", "PreviewArrival", "ShowPreview304" };
            var candidates = new List<Component>();
            var arrival = Object.FindFirstObjectByType<WorldLocationArrival>(); if (arrival != null) candidates.Add(arrival);
            var hud = Object.FindFirstObjectByType<HudController>(); if (hud != null) candidates.Add(hud);
            candidates.AddRange(Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(m => m != null && m.GetType().Name.IndexOf("Arrival", StringComparison.OrdinalIgnoreCase) >= 0 && !candidates.Contains(m)));
            foreach (var target in candidates)
            {
                var two = HarnessUiRules304.FindMethod(target.GetType(), names, new[] { typeof(string), typeof(string) });
                if (two != null) { two.Invoke(target, new object[] { region, place }); return "preview=" + target.GetType().Name + "." + two.Name + "(region, place)"; }
                var one = HarnessUiRules304.FindMethod(target.GetType(), names, new[] { typeof(string) });
                if (one != null) { one.Invoke(target, new object[] { region ?? place }); return "preview=" + target.GetType().Name + "." + one.Name + "(name)"; }
                var none = HarnessUiRules304.FindMethod(target.GetType(), names, Type.EmptyTypes);
                if (none != null) { none.Invoke(target, null); return "preview=" + target.GetType().Name + "." + none.Name + "()" + (region != null ? " (names ignored)" : ""); }
            }
            return "SKIPPED no public arrival preview (" + string.Join("/", names) + ") on " + (candidates.Count == 0 ? "any component" : string.Join(",", candidates.Select(c => c.GetType().Name).Distinct()));
        }

        /// <summary>hp:&lt;0..1&gt; / ink:&lt;0..1&gt;: HudController.SetHp01 / SetInk01 display values for captures. PlayerVitals and the ink
        /// economy are untouched; the previous display values are restored when the tour ends.</summary>
        static string TourMeter(bool hp, string arg)
        {
            var hud = Object.FindFirstObjectByType<HudController>();
            Need(hud != null, "no HudController");
            Need(float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && value >= 0f && value <= 1f, (hp ? "hp" : "ink") + ":<0..1> expected");
            if (hp && !tourHpSaved) { float was = hud.Hp01; tourHpSaved = true; tourRestore.Add(() => { if (hud != null) hud.SetHp01(was); }); }
            if (!hp && !tourInkSaved) { float was = hud.Ink01; tourInkSaved = true; tourRestore.Add(() => { if (hud != null) hud.SetInk01(was); }); }
            if (hp) hud.SetHp01(value); else hud.SetInk01(value);
            return hp ? "Hp01=" + hud.Hp01.ToString("0.00", CultureInfo.InvariantCulture) : "Ink01=" + hud.Ink01.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>lock:on|off[~groggy]: the lock-on 일원상 at screen centre-ish. Prefers a public HUD preview (PreviewLockOn304 /
        /// PreviewLockOn / PreviewReticle304 with (bool) or (bool, float)); falls back to the editor diagnostic state that
        /// HudController already exposes (SetEditorDiagnosticState / ClearEditorDiagnosticState: it also freezes the prompt text
        /// while held). SKIPPED when neither exists.</summary>
        static string TourLock(string arg)
        {
            var hud = Object.FindFirstObjectByType<HudController>();
            Need(hud != null, "no HudController");
            string[] p = arg.Split('~');
            string mode = p[0].Trim().ToLowerInvariant();
            Need(mode == "on" || mode == "off", "lock:on|off[~groggy]");
            bool on = mode == "on";
            float groggy = hud.Groggy01;
            if (p.Length > 1) Need(float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out groggy) && groggy >= 0f && groggy <= 1f, "groggy must be 0..1");
            string[] names = { "PreviewLockOn304", "PreviewLockOn", "PreviewReticle304", "PreviewReticle" };
            var withGroggy = HarnessUiRules304.FindMethod(hud.GetType(), names, new[] { typeof(bool), typeof(float) });
            if (withGroggy != null) { withGroggy.Invoke(hud, new object[] { on, groggy }); TrackLockPreview(hud, withGroggy, on, groggy); return "preview=" + withGroggy.Name + "(" + on + ", " + groggy.ToString("0.00", CultureInfo.InvariantCulture) + ")"; }
            var plain = HarnessUiRules304.FindMethod(hud.GetType(), names, new[] { typeof(bool) });
            if (plain != null)
            {
                if (on && p.Length > 1) hud.SetGroggy01(groggy);
                plain.Invoke(hud, new object[] { on }); TrackLockPreview(hud, plain, on, groggy); return "preview=" + plain.Name + "(" + on + ")";
            }
            var set = HarnessUiRules304.FindMethod(hud.GetType(), "SetEditorDiagnosticState", 5);
            var clear = HarnessUiRules304.FindMethod(hud.GetType(), "ClearEditorDiagnosticState", 6);
            if (set == null || clear == null) return "SKIPPED no public reticle preview (" + string.Join("/", names) + ") and no editor diagnostic state";
            var session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            string text = session != null ? session.CurrentHudText : null;
            if (on)
            {
                if (!tourLockHeld)
                {
                    float was = hud.Groggy01; tourLockHeld = true;
                    tourRestore.Add(() => { if (hud != null && tourLockHeld) { clear.Invoke(hud, new object[] { hud.Hp01, hud.Ink01, was, session != null ? session.CurrentHudText : null, false, Vector3.zero }); tourLockHeld = false; } });
                }
                set.Invoke(hud, new object[] { hud.Hp01, hud.Ink01, groggy, text, new Vector2(Screen.width * .5f, Screen.height * .58f) });
                return "fallback=SetEditorDiagnosticState (prompt text frozen while held)";
            }
            if (tourLockHeld) { clear.Invoke(hud, new object[] { hud.Hp01, hud.Ink01, hud.Groggy01, text, false, Vector3.zero }); tourLockHeld = false; }
            return "fallback=ClearEditorDiagnosticState";
        }

        static void TrackLockPreview(HudController hud, MethodInfo preview, bool on, float groggy)
        {
            if (!on) { tourLockHeld = false; return; }
            if (tourLockHeld) return;
            tourLockHeld = true;
            var args = preview.GetParameters().Length == 2 ? new object[] { false, groggy } : new object[] { false };
            tourRestore.Add(() => { if (hud != null && tourLockHeld) { preview.Invoke(hud, args); tourLockHeld = false; } });
        }

        /// <summary>select:&lt;GameObject name&gt;: EventSystem selection (FocusMark304.Select) so captures show the focus state (underlay,
        /// 방점). The menu root is searched first, then every active Selectable, then any active GameObject. Empty = deselect.</summary>
        static string TourSelect(PlaytestUiRoot root, string name)
        {
            var es = EventSystem.current;
            if (es == null) return "SKIPPED no EventSystem";
            if (string.IsNullOrEmpty(name)) { es.SetSelectedGameObject(null); return "selection cleared"; }
            GameObject target = null;
            if (root != null) target = root.GetComponentsInChildren<Selectable>(false).FirstOrDefault(s => s.name == name)?.gameObject;
            if (target == null) target = Object.FindObjectsByType<Selectable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault(s => s.name == name)?.gameObject;
            if (target == null && root != null) target = root.GetComponentsInChildren<Transform>(false).FirstOrDefault(t => t.name == name)?.gameObject;
            if (target == null) return "SKIPPED no active GameObject named '" + name + "'";
            FocusMark304.Select(target);
            var selectable = target.GetComponent<Selectable>();
            return "selected=" + HarnessUiRules304.Path(target.transform, root != null ? root.transform : null)
                + (selectable == null ? " (not a Selectable: no focus visual)" : selectable.IsInteractable() ? "" : " (not interactable)")
                + " visual=" + (target.GetComponent<IFocusVisual304>() != null ? target.GetComponent<IFocusVisual304>().GetType().Name : "none");
        }

        static void TourRestore()
        {
            for (int i = tourRestore.Count - 1; i >= 0; i--)
            {
                try { tourRestore[i](); }
                catch (Exception e) { state.log.Add("restore failed: " + e.GetType().Name + ": " + e.Message); }
            }
            TourResetStatics();
        }

        /// <summary>The label PlaytestUiRoot.ReturnTitle passes to ShowLoading (the recorded return-to-lobby overlay: label + progress
        /// line; no seal since UiStyle304SO.ShowSeals = false, 2026-09-30).</summary>
        const string ReturnTitleLoadingLabel = "여정 기록됨";

        /// <summary>loading[:&lt;label&gt;][~&lt;progress 0..1&gt;]: private ShowLoading(label) (empty label = ReturnTitleLoadingLabel). #304 QA2: the
        /// overlay's progress line is driven by the scene load (Flow304LoadingProgress(load.progress / .9)); a still of the bare step
        /// showed an empty track (after2/loading.png). `~p` sets that progress by name so the capture shows the line mid-load.</summary>
        static string TourLoading(PlaytestUiRoot root, string arg)
        {
            Need(root != null, "no PlaytestUiRoot");
            string label = arg ?? ""; float progress = -1f;
            int cut = label.LastIndexOf('~');
            if (cut >= 0)
            {
                Need(float.TryParse(label.Substring(cut + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out progress) && progress >= 0f && progress <= 1f, "loading[:<label>][~<progress 0..1>]");
                label = label.Substring(0, cut);
            }
            if (label.Length == 0) label = ReturnTitleLoadingLabel;
            Private(root, "ShowLoading", label);
            if (progress < 0f) return "label=" + label;
            var set = root.GetType().GetMethod("Flow304LoadingProgress", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(float) }, null);
            if (set == null) return "SKIPPED progress: PlaytestUiRoot.Flow304LoadingProgress(float) not found by name (label=" + label + " shown)";
            set.Invoke(root, new object[] { progress });
            return "label=" + label + " progress=" + progress.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>notice:&lt;title&gt;[\n&lt;source&gt;][~&lt;seconds&gt;]: PlaytestUiRoot.ShowNotice with its default hold (4 s) unless seconds are
        /// given. #304 QA: the step used to pass 8 s = ToastHoldErrorMs, so ShowNotice raised every tour notice as an Error toast
        /// (after/notice.png). An error toast on purpose: toast:error~title~source.</summary>
        static string TourNotice(PlaytestUiRoot root, string arg)
        {
            Need(root != null, "no PlaytestUiRoot");
            string text = arg ?? ""; float seconds = 4f;
            int cut = text.LastIndexOf('~');
            if (cut >= 0 && float.TryParse(text.Substring(cut + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float s) && s > 0f) { seconds = s; text = text.Substring(0, cut); }
            text = text.Replace("\\n", "\n");
            root.ShowNotice(text, seconds);
            var channel = root.Theme != null && root.Theme.Style304 != null ? root.Theme.Style304.Notices : null;
            return "seconds=" + seconds.ToString("0.#", CultureInfo.InvariantCulture) + (channel != null && channel.RaisedCount > 0 ? " kind=" + channel.Last.Kind : " (no notice channel)");
        }

        static string SelectedName()
        {
            var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return sel != null ? sel.name : "-";
        }

        /// <summary>Instance method by name whose parameters accept `args` (an overload added by another area does not steal the call).</summary>
        static object Private(object target, string method, params object[] args)
        {
            var candidates = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(x => x.Name == method && x.GetParameters().Length == args.Length).ToArray();
            var m = candidates.FirstOrDefault(x => x.GetParameters().Select((p, i) => args[i] == null ? !p.ParameterType.IsValueType : p.ParameterType.IsInstanceOfType(args[i])).All(ok => ok))
                ?? candidates.FirstOrDefault();
            Need(m != null, target.GetType().Name + "." + method + "/" + args.Length + " not found");
            return m.Invoke(target, args);
        }

        static void Fail(string why)
        {
            state.status = "failed"; state.error = why; EditorApplication.update -= Tick;
            foreach (var c in hidden) if (c != null) c.enabled = true; hidden.Clear();
            waitUntil = null;
            if (EditorApplication.isPlaying) TourRestore(); else TourResetStatics();
        }

        static void FocusGameView()
        {
            Type type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            var window = EditorWindow.GetWindow(type, false, "Game", true); if (window != null) window.Focus();
        }

        static string SetSize(int w, int h)
        {
            Type gv = Type.GetType("UnityEditor.GameView,UnityEditor", true);
            Object[] views = Resources.FindObjectsOfTypeAll(gv); Need(views.Length > 0, "No Game view is open.");
            Type sizesType = Type.GetType("UnityEditor.GameViewSizes,UnityEditor", true);
            object sizes = Prop(sizesType, "instance").GetValue(null);
            object group = Prop(sizesType, "currentGroup").GetValue(sizes);
            Type gt = group.GetType(); int count = (int)Meth(gt, "GetTotalCount").Invoke(group, null); int index = -1;
            for (int i = 0; i < count && index < 0; i++)
            {
                object size = Meth(gt, "GetGameViewSize").Invoke(group, new object[] { i }); Type st = size.GetType();
                if ((int)Prop(st, "width").GetValue(size) == w && (int)Prop(st, "height").GetValue(size) == h && Prop(st, "sizeType").GetValue(size).ToString() == "FixedResolution") index = i;
            }
            Need(index >= 0, "No FixedResolution " + w + "x" + h + " entry in the current Game view size group.");
            var sel = Prop(gv, "selectedSizeIndex"); var notes = new List<string>();
            foreach (Object view in views)
            {
                int prev = (int)sel.GetValue(view);
                if (string.IsNullOrEmpty(SessionState.GetString(SizeKey, ""))) SessionState.SetString(SizeKey, prev.ToString());
                Meth(gv, "SizeSelectionCallback").Invoke(view, new object[] { index, null }); notes.Add(prev + "->" + index);
            }
            return "SIZE_SET " + w + "x" + h + " " + string.Join(",", notes);
        }

        static string RestoreSize()
        {
            Need(int.TryParse(SessionState.GetString(SizeKey, ""), out int prev), "No saved size.");
            Type gv = Type.GetType("UnityEditor.GameView,UnityEditor", true);
            foreach (Object view in Resources.FindObjectsOfTypeAll(gv)) Meth(gv, "SizeSelectionCallback").Invoke(view, new object[] { prev, null });
            SessionState.EraseString(SizeKey); return "SIZE_RESTORED index=" + prev;
        }

        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
        static PropertyInfo Prop(Type t, string n) => t.GetProperty(n, All) ?? throw new MissingMemberException(t.FullName, n);
        static MethodInfo Meth(Type t, string n) => t.GetMethod(n, All) ?? throw new MissingMethodException(t.FullName, n);
        static void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
    }
}
