using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#304 QA round 1 tour steps (harness): diagnostics and player-path page opens the lead needs to verify the fixes.
    ///   interact-probe[:name]      why the focused interaction / HUD prompt / world F is or is not visible (JSON in the set folder)
    ///   near:&lt;point id&gt;[~metres]    stand in front of an interaction point facing it (restored when the tour ends)
    ///   reveal-map[:on|off]        every map cell discovered on the review save's map (in memory; restored + saved when the tour ends)
    ///   focus-report[:name]        EventSystem selection and each FocusMark304's target (JSON in the set folder)
    ///   page-default:&lt;page&gt;       open a page the way the player does from gameplay (Esc / I / M / the pause list row), then judge
    ///                              the page's initial selection (IMPLEMENTATION §4.2): tour-status.checks gets PASS / FAIL
    ///   rail:&lt;page&gt;               go to a page through its rail tab (select + submit): the rail tab must stay selected
    /// #304 QA round 2:
    ///   settle[:max seconds]       hold the tour until the #304 tweens under the menu root are at rest (CanvasGroup alphas,
    ///                              InkRevealEffect reveals, 방점): the motion budget of unscaled time, then 3 still frames over .06 s;
    ///                              soft (default max 3 s, the log names what still moves). page / close / back / detail / confirm /
    ///                              dismiss / loading and the page-default / rail / title-page routes settle by themselves
    ///   alpha-report[:name[~s]]    every drawn Graphic whose effective alpha (CanvasGroup chain x colour x CanvasRenderer) is below
    ///                              .98, with its path, material / shader, the linear-alpha remap and the CanvasGroup that causes it;
    ///                              a second sample `s` seconds later (default .3, 0 = one sample) tells a page still bleeding in
    ///                              (capture too early) from one stuck translucent (JSON in the set folder)
    ///   title-page:&lt;page&gt;        on the title: Options / Controls row selected + submitted as the player does, settled, then
    ///                              PASS / FAIL: the page's CanvasGroup chain is at 1 and the TitleCard is hidden (tour-status.checks)
    /// Members that belong to other engineers are read by name (reflection); a missing one is reported, never assumed.</summary>
    public static partial class UiOverhaul304
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // wait-until condition of the page-default / rail sub-steps; reveal / near restore state. Reset with the tour statics.
        static Func<bool> waitUntil;
        static string waitUntilWhat;
        static double waitUntilDeadline;
        static Func<string> revealUndo;
        static bool revealHeld, nearHeld;
        // settle (unscaled-time wait for the #304 tweens) and the alpha-report first sample. Reset with the tour statics.
        static readonly Dictionary<long, float> settleLast = new Dictionary<long, float>(), settleNow = new Dictionary<long, float>();
        static readonly Dictionary<long, string> settleNames = new Dictionary<long, string>();
        static readonly List<long> settleChanged = new List<long>();
        static int settleStillFrames;
        static double settleStillSince;
        static bool settleSampled;
        static string lastSettle, settleMoving;
        static AlphaPending304 alphaPending;

        static void ProbeResetStatics()
        {
            waitUntil = null; waitUntilWhat = null; waitUntilDeadline = 0; revealUndo = null; revealHeld = nearHeld = false;
            settleLast.Clear(); settleNow.Clear(); settleNames.Clear(); settleChanged.Clear();
            settleStillFrames = 0; settleStillSince = 0; settleSampled = false; lastSettle = settleMoving = null; alphaPending = null;
        }

        /// <summary>True while a wait-until sub-step holds the tour (Tick returns until the condition is met or times out).</summary>
        static bool ProbeWaiting()
        {
            if (waitUntil == null) return false;
            bool done;
            try { done = waitUntil(); }
            catch (Exception e) { throw new InvalidOperationException("wait for " + waitUntilWhat + " threw " + e.GetType().Name + ": " + e.Message); }
            if (done) { waitUntil = null; waitUntilWhat = null; return false; }
            if (EditorApplication.timeSinceStartup > waitUntilDeadline) throw new TimeoutException("timed out waiting for " + waitUntilWhat);
            return true;
        }

        static void WaitUntil(string what, Func<bool> condition, double seconds = 6d)
        { waitUntil = condition; waitUntilWhat = what; waitUntilDeadline = EditorApplication.timeSinceStartup + seconds; }

        /// <summary>Queues `sub` before the remaining steps (and extends the tour deadline for them).</summary>
        static void PushFront(IList<string> sub)
        {
            steps = new Queue<string>(sub.Concat(steps));
            deadline += 4d * sub.Count;
        }

        /// <summary>The probe/verdict/sub-step verbs of this file; null = not one of them.</summary>
        static string ProbeStep(PlaytestUiRoot root, string verb, string arg)
        {
            switch (verb)
            {
                case "interact-probe": waitFrames = 1; return TourInteractProbe(root, arg);
                case "near": waitFrames = 24; return TourNear(root, arg);             // focus is recomputed in the session Update, camera settles
                case "reveal-map": waitFrames = 4; return TourRevealMap(root, arg);
                case "focus-report": waitFrames = 1; return TourFocusReport(root, arg);
                case "page-default": waitFrames = 0; return TourPageDefault(root, arg);
                case "rail": waitFrames = 0; return TourRail(root, arg);
                case "settle": waitFrames = 0; return TourSettle(root, arg, "settle");
                case "alpha-report": waitFrames = 0; return TourAlphaReport(root, arg);
                case "title-page": waitFrames = 0; return TourTitlePage(root, arg);
                // ---- sub-steps (queued by page-default / rail; usable on their own)
                case "_menu-closed":
                    Need(root != null, "no PlaytestUiRoot");
                    if (HasConfirmation(root)) Private(root, "DismissConfirmation");
                    if (root.Page.Length > 0) root.CloseMenu();
                    WaitUntil("the menu to close", () => root != null && root.Page.Length == 0 && !root.Busy && !HasConfirmation(root));
                    waitFrames = 4; return "closing from page=" + root.Page;
                case "_esc":   // the Menus/Pause action: PlaytestUiRoot.Back (gameplay -> 일시정지)
                    Need(root != null, "no PlaytestUiRoot"); root.Back(); waitFrames = 16; return "Back() -> page=" + root.Page;
                case "_open":  // the Menus/Inventory (I) / Map (M) handlers from gameplay call OpenPage(page) when no page is open
                    Need(root != null, "no PlaytestUiRoot"); Need(root.Page.Length == 0, "_open is the I / M path from gameplay; a page is open (" + root.Page + ")");
                    root.OpenPage(arg); waitFrames = 16; return "OpenPage(" + arg + ") from gameplay -> page=" + root.Page;
                case "_select":                                                        // keyboard / pad navigation moves the selection
                {
                    waitFrames = 8; string selected = TourSelect(root, arg);
                    Need(!selected.StartsWith("SKIPPED", StringComparison.Ordinal), "_select:" + arg + " " + selected + " (the next _submit would press something else)");
                    return selected;
                }
                case "_submit": waitFrames = 16; return TourSubmit(root, arg);         // Enter / A on the selected row (InputSystemUIInputModule submit)
                case "_verdict": waitFrames = 0; return TourVerdict(root, arg);
                case "_settle": waitFrames = 0; return TourSettle(root, arg, "_settle");
                case "_alpha-report2": waitFrames = 0; return TourAlphaReportSecond(root);
                case "_title-verdict": waitFrames = 0; return TourTitleVerdict(root, arg);
                default: return null;
            }
        }

        static bool HasConfirmation(PlaytestUiRoot root) => root != null && HarnessUiRules304.Member(root, "confirmationRoot") is Object o && o != null;

        static string SelectedPath(PlaytestUiRoot root)
        {
            var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return sel == null ? "<none>" : HarnessUiRules304.Path(sel.transform, root != null ? root.transform : null);
        }

        static string ReportPath(string kind, string name)
        {
            string dir = Path.Combine(Output, string.IsNullOrEmpty(state.set) ? "probe" : state.set); Directory.CreateDirectory(dir);
            string safe = string.IsNullOrEmpty(name) ? "" : "_" + new string(name.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
            return Path.Combine(dir, kind + safe + ".json");
        }

        static string F2(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        static string V3(Vector3 v) => "(" + v.x.ToString("0.00", CultureInfo.InvariantCulture) + "," + v.y.ToString("0.00", CultureInfo.InvariantCulture) + "," + v.z.ToString("0.00", CultureInfo.InvariantCulture) + ")";

        // ------------------------------------------------------------------ interact-probe
        [Serializable]
        sealed class InteractProbe304
        {
            public string utc, set, name, verdict, worldLetterVerdict;
            public string scene, page;
            public bool paused, gateBlocked, gateReleasePending, gateFocusOwnsBlock, applicationFocused, editorFocusedOnGameView;
            public float timeScale;
            public bool sessionFound, sessionReady, gameplayInputBlocked, runtimeStateInputBlocked, seated, motorEnabled, motorDrawing, inDrawMode, restPresentation, canvasHudPresenterActive;
            public float playerHp01;
            public string playerFeet, loadStatus;
            public string focusedId, focusedBundle, currentHudText, promptText, promptTextSource, lastFeedback, saveError;
            public bool focusedCanInteract, focusedPositionOk, focusedBoundsOk, focusedVisualBinding;
            public int focusedRenderers, focusedRenderersDrawn;
            public string[] nearestPoints = Array.Empty<string>();
            public string[] nearestPickups = Array.Empty<string>();
            public bool presenterFound, presenterEnabled, presenterPresenting, hudFound, hudSkinned, hudHideText, hudEditorDiagnostic, hudInteractionVisible, hudCanvasEnabled, promptObjectActive;
            public float promptAlpha;
            public string promptLabel, promptKey;
            public bool worldLetterBuilt, worldLetterActive, worldLetterLineOfSight;
            public string worldLetterViewport, worldLetterCamera;
        }

        sealed class PointProbe { public string Id, Kind; public float Distance, Radius; public bool CanInteract; public string Ray; public bool RayClear; }

        /// <summary>interact-probe[:name]: why the focused interaction's HUD prompt / world F is or is not visible. Reproduces the
        /// session's own rules (CanInteract: range = Point.Radius from the feet, eye ray to target + 1.25 m ignoring the player and
        /// the point's own WorldMacroContentPoint) for the nearest points, then walks the presenter -> HudController -> HudPrompt304
        /// chain and the world-F conditions (TryGetFocusedInteractionBounds, FocusLineOfSight, viewport).</summary>
        static string TourInteractProbe(PlaytestUiRoot root, string name)
        {
            var p = new InteractProbe304 { utc = DateTime.UtcNow.ToString("O"), set = state.set, name = name ?? "", scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path };
            p.page = root != null ? root.Page : "<no root>";
            p.paused = root != null && root.Pause != null && root.Pause.IsPaused;
            p.gateBlocked = root != null && root.Gate != null && root.Gate.InputBlocked;
            p.gateReleasePending = root != null && root.Gate != null && root.Gate.ReleasePending;
            p.gateFocusOwnsBlock = root != null && root.Gate != null && root.Gate.FocusOwnsBlock;
            p.applicationFocused = Application.isFocused;
            p.editorFocusedOnGameView = EditorWindow.focusedWindow != null && EditorWindow.focusedWindow.GetType().Name == "GameView";
            p.timeScale = Time.timeScale;

            var session = root != null && root.Session != null ? root.Session : Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            var hud = Object.FindFirstObjectByType<HudController>();
            var presenter = Object.FindFirstObjectByType<WorldMacroPlaytestHudPresenter>();
            p.sessionFound = session != null; p.hudFound = hud != null; p.presenterFound = presenter != null;
            var points = new List<PointProbe>();
            if (session != null)
            {
                var walker = session.Walker;
                p.sessionReady = session.InitializationComplete;
                p.gameplayInputBlocked = session.GameplayInputBlocked;
                p.runtimeStateInputBlocked = session.RuntimeState != null && session.RuntimeState.InputBlocked;
                p.seated = walker != null && walker.Seated;
                p.motorEnabled = walker != null && walker.Motor != null && walker.Motor.enabled;
                p.motorDrawing = walker != null && walker.Motor != null && walker.Motor.IsDrawing;
                p.inDrawMode = walker != null && walker.Drawing != null && walker.Drawing.InDrawMode;
                p.restPresentation = session.RestPresentationActive;
                p.canvasHudPresenterActive = session.CanvasHudPresenterActive;
                p.loadStatus = session.LoadStatus;
                var vitals = walker != null && walker.Body != null ? walker.Body.GetComponent<Oheangbu.Combat.PlayerVitals>() : null;
                p.playerHp01 = vitals != null ? vitals.Hp01 : -1f;
                p.playerFeet = walker != null && walker.Body != null ? V3(walker.Body.transform.position) : "<no body>";
                p.focusedId = session.FocusedId; p.focusedBundle = session.FocusedCollectionBundleId;
                p.currentHudText = session.CurrentHudText; p.lastFeedback = session.LastFeedback; p.saveError = session.SaveError;
                p.promptText = PromptTextFor(session, out p.promptTextSource);
                p.focusedPositionOk = session.TryGetFocusedInteractionPosition(out _);
                p.focusedBoundsOk = session.TryGetFocusedInteractionBounds(out var focusBounds);
                if (!string.IsNullOrEmpty(session.FocusedId)) p.focusedCanInteract = session.CanInteract(session.FocusedId);
                var renderers = session.FocusedRenderers ?? Array.Empty<Renderer>();
                p.focusedRenderers = renderers.Length;
                p.focusedRenderersDrawn = renderers.Count(r => r != null && r.enabled && r.gameObject.activeInHierarchy);
                p.focusedVisualBinding = !string.IsNullOrEmpty(session.FocusedId) && session.InteractionVisuals != null && session.InteractionVisuals.Any(v => v != null && v.Id == session.FocusedId && v.Renderers != null && v.Renderers.Length > 0);
                if (walker != null && walker.Body != null)
                {
                    points = NearestPoints(session, 4);
                    p.nearestPoints = points.Select(x => x.Id + " kind=" + x.Kind + " d=" + F2(x.Distance) + "/r=" + F2(x.Radius) + " canInteract=" + x.CanInteract + " ray=" + x.Ray).ToArray();
                    Vector3 feet = walker.Body.transform.position;
                    p.nearestPickups = Object.FindObjectsByType<WorldMacroFragmentPickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                        .OrderBy(x => Vector3.Distance(x.InteractionPosition, feet)).Take(2)
                        .Select(x => x.BundleId + " d=" + F2(Vector3.Distance(x.InteractionPosition, feet)) + "/r=" + F2(x.Radius) + " collected=" + x.IsCollected).ToArray();
                }
                // ---- world F (WorldMacroPlaytestHudPresenter.UpdateWorldInteraction)
                if (presenter != null)
                {
                    var canvas = HarnessUiRules304.Member(presenter, "_interactionCanvas") as Canvas;
                    p.worldLetterBuilt = canvas != null;
                    p.worldLetterActive = canvas != null && canvas.gameObject.activeInHierarchy && canvas.enabled;
                    var camera = walker != null ? walker.ViewCamera : null;
                    p.worldLetterCamera = camera != null ? camera.name : "<no view camera>";
                    if (p.focusedBoundsOk && camera != null && hud != null && hud.Skin != null)
                    {
                        var anchor = new Vector3(focusBounds.center.x, focusBounds.max.y + hud.Skin.InteractionLetterOffset + hud.Skin.InteractionLetterHeight * .5f, focusBounds.center.z);
                        Vector3 vp = camera.WorldToViewportPoint(anchor);
                        p.worldLetterViewport = V3(vp);
                        var los = presenter.GetType().GetMethod("FocusLineOfSight", Instance, null, new[] { typeof(Camera), typeof(Bounds) }, null);
                        p.worldLetterLineOfSight = los != null && (bool)los.Invoke(presenter, new object[] { camera, focusBounds });
                        if (los == null) p.worldLetterViewport += " (FocusLineOfSight not found by name)";
                    }
                }
            }
            if (presenter != null)
            {
                p.presenterEnabled = presenter.isActiveAndEnabled;
                p.presenterPresenting = HarnessUiRules304.Member(presenter, "_presenting") is bool presenting && presenting;
            }
            if (hud != null)
            {
                p.hudSkinned = hud.IsSkinned; p.hudHideText = hud.HideText; p.hudInteractionVisible = hud.InteractionVisible;
                p.hudEditorDiagnostic = HarnessUiRules304.Member(hud, "_editorDiagnosticState") is bool diagnostic && diagnostic;
                var hudCanvas = HarnessUiRules304.Member(hud, "Canvas") as Canvas;
                p.hudCanvasEnabled = hudCanvas != null && hudCanvas.isActiveAndEnabled;
                var prompt = HarnessUiRules304.Member(hud, "Prompt304") as Component;
                if (prompt != null)
                {
                    p.promptObjectActive = prompt.gameObject.activeInHierarchy;
                    p.promptAlpha = HarnessUiRules304.GroupAlpha(prompt);
                    var label = HarnessUiRules304.Member(prompt, "Label") as Graphic;
                    p.promptLabel = label != null ? HarnessUiRules304.TextOf(label) : null;
                    p.promptKey = HarnessUiRules304.Member(prompt, "KeyLabel") as string;
                }
            }
            p.verdict = InteractVerdict(p, points);
            p.worldLetterVerdict = WorldLetterVerdict(p);
            string path = ReportPath("interact_probe", name);
            File.WriteAllText(path, JsonUtility.ToJson(p, true));
            state.reports.Add(path);
            return "verdict=" + p.verdict + " | worldF=" + p.worldLetterVerdict + " report=" + path;
        }

        /// <summary>WorldMacroPlaytestHudPresenter.PromptText(session) by name (the HUD area owns it); CurrentHudText otherwise.</summary>
        static string PromptTextFor(WorldMacroPlaytestSession session, out string source)
        {
            var m = typeof(WorldMacroPlaytestHudPresenter).GetMethod("PromptText", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(WorldMacroPlaytestSession) }, null);
            if (m == null) { source = "session.CurrentHudText (presenter PromptText not found by name)"; return session.CurrentHudText; }
            source = "WorldMacroPlaytestHudPresenter.PromptText";
            return m.Invoke(null, new object[] { session }) as string;
        }

        /// <summary>The nearest interaction points with the session's own CanInteract, plus the eye-ray result it is based on.</summary>
        static List<PointProbe> NearestPoints(WorldMacroPlaytestSession session, int count)
        {
            var list = new List<PointProbe>();
            var all = HarnessUiRules304.Member(session, "InteractionPoints") as PrologueContentSO.Point[];
            if (all == null) return list;
            var find = typeof(WorldMacroPlaytestSession).GetMethod("FindInteractionPoint", Instance, null, new[] { typeof(string) }, null);
            var body = session.Walker.Body.transform;
            Vector3 feet = body.position;
            foreach (var authored in all)
            {
                if (authored == null || string.IsNullOrEmpty(authored.Id)) continue;
                var live = find != null ? find.Invoke(session, new object[] { authored.Id }) as PrologueContentSO.Point : null;
                var point = live ?? authored;
                list.Add(new PointProbe { Id = point.Id, Kind = point.Kind.ToString(), Distance = Vector3.Distance(feet, point.Position), Radius = point.Radius });
            }
            list = list.OrderBy(x => x.Distance).Take(count).ToList();
            foreach (var x in list)
            {
                x.CanInteract = session.CanInteract(x.Id);
                var live = find != null ? find.Invoke(session, new object[] { x.Id }) as PrologueContentSO.Point : null;
                Vector3 target = (live != null ? live.Position : Array.Find(all, a => a != null && a.Id == x.Id).Position);
                Vector3 eye = feet + Vector3.up * session.Walker.EyeHeight, to = target + Vector3.up * 1.25f - eye;
                var blockers = new List<string>();
                if (to.sqrMagnitude > 1e-6f)
                    foreach (var h in Physics.RaycastAll(eye, to.normalized, to.magnitude, ~0, QueryTriggerInteraction.Ignore).OrderBy(hit => hit.distance))
                    {
                        if (h.transform.IsChildOf(body)) continue;
                        var cp = h.transform.GetComponentInParent<WorldMacroContentPoint>();
                        if (cp != null && cp.Id == x.Id) continue;
                        blockers.Add(h.collider.name + "@" + F2(h.distance) + (cp != null ? "(point " + cp.Id + ")" : "") + " root=" + h.transform.root.name);
                    }
                x.RayClear = blockers.Count == 0;
                x.Ray = x.RayClear ? "clear" : "blocked by " + string.Join(", ", blockers.Take(3));
            }
            return list;
        }

        static string InteractVerdict(InteractProbe304 p, List<PointProbe> points)
        {
            if (!p.sessionFound) return "no WorldMacroPlaytestSession in the scene";
            if (!p.sessionReady) return "session not initialised yet";
            if (!p.hudFound) return "no HudController";
            if (!p.presenterFound) return "no WorldMacroPlaytestHudPresenter (the session draws its own OnGUI box instead)";
            if (!p.presenterEnabled || !p.presenterPresenting) return "presenter not presenting (enabled=" + p.presenterEnabled + ", presenting=" + p.presenterPresenting + ", hudSkinned=" + p.hudSkinned + ")";
            if (!string.IsNullOrEmpty(p.page) && p.page != "<no root>") return "a menu page is open (" + p.page + "): the session drops focus while input is blocked and the HUD canvases are disabled";
            if (p.gameplayInputBlocked)
                return "GameplayInputBlocked (timeScale=" + F2(p.timeScale) + ", RuntimeState.InputBlocked=" + p.runtimeStateInputBlocked + ", gate releasePending=" + p.gateReleasePending
                    + ", focusOwnsBlock=" + p.gateFocusOwnsBlock + ", applicationFocused=" + p.applicationFocused + "): the session clears FocusedId every frame while blocked"
                    + (!p.applicationFocused || p.gateReleasePending ? "; the gate releases only after two neutral frames with the application focused (focus the Game View)" : "");
            if (string.IsNullOrEmpty(p.focusedId) && string.IsNullOrEmpty(p.focusedBundle))
            {
                if (p.seated || p.inDrawMode || p.motorDrawing || !p.motorEnabled) return "no focus: seated=" + p.seated + ", drawing=" + (p.inDrawMode || p.motorDrawing) + ", motorEnabled=" + p.motorEnabled;
                var n = points.FirstOrDefault();
                if (n == null) return "no focus: no interaction point found (InteractionPoints not readable by name or empty)";
                if (n.Distance > n.Radius) return "no focus: nearest point '" + n.Id + "' is " + F2(n.Distance) + " m from the feet, beyond its radius " + F2(n.Radius) + " m (CanInteract range test)";
                if (!n.RayClear) return "no focus: '" + n.Id + "' is in range but the eye ray to it is " + n.Ray + " (CanInteract line-of-sight test)";
                return "no focus: '" + n.Id + "' is in range with a clear ray but CanInteract=" + n.CanInteract + " (pending defeats / opened shortcut door / walker state)";
            }
            if (string.IsNullOrEmpty(p.promptText))
                return string.IsNullOrEmpty(p.currentHudText) ? "focused (" + (p.focusedId ?? p.focusedBundle) + ") but the session text is empty"
                    : "focused (" + (p.focusedId ?? p.focusedBundle) + ") but " + p.promptTextSource + " filtered '" + p.currentHudText + "' (routed to the toast stack or the death wake line)";
            if (!p.hudInteractionVisible)
                return p.hudHideText ? "HudController.HideText hides the prompt" : p.hudEditorDiagnostic ? "the HUD editor diagnostic state freezes the prompt (ClearEditorDiagnosticState)"
                    : "prompt text '" + p.promptText + "' was given but HudController.InteractionVisible is false (SetInteractionText / HudPrompt304.Show)";
            if (!p.hudCanvasEnabled) return "prompt logically visible but HUD_Canvas is disabled";
            if (!p.promptObjectActive || p.promptAlpha < .01f) return "prompt logically visible but not drawn (active=" + p.promptObjectActive + ", alpha=" + F2(p.promptAlpha) + ")";
            return "HUD prompt visible: [" + (p.promptKey ?? "-") + "] " + p.promptLabel;
        }

        static string WorldLetterVerdict(InteractProbe304 p)
        {
            if (p.hudInteractionVisible) return p.worldLetterActive ? "VIOLATION world F active while the HUD prompt shows (one surface)" : "hidden (the HUD prompt shows: one surface)";
            if (!p.focusedPositionOk) return "hidden (no focused interaction)";
            if (!p.focusedBoundsOk) return "hidden: no drawable renderers for the focus (" + (p.focusedVisualBinding ? "InteractionVisuals renderers disabled/inactive" : "no InteractionVisuals binding for '" + p.focusedId + "'") + "; outline and F need them)";
            if (!p.worldLetterLineOfSight) return "hidden: FocusLineOfSight false from the camera";
            return p.worldLetterActive ? "visible at viewport " + p.worldLetterViewport : "expected visible (viewport " + p.worldLetterViewport + ") but the WorldInteractionPrompt canvas is " + (p.worldLetterBuilt ? "inactive" : "not built");
        }

        // ------------------------------------------------------------------ near
        /// <summary>near:&lt;point id&gt;[~metres]: teleports the player `metres` (default min(1.6, .6 x radius)) in front of the live point
        /// (the escort NPC follows its companion), facing it, on safe feet (session.TrySafeFeet). Restored when the tour ends.</summary>
        static string TourNear(PlaytestUiRoot root, string arg)
        {
            var session = root != null && root.Session != null ? root.Session : Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            Need(session != null && session.Walker != null && session.Walker.Body != null, "no playtest session / player");
            string[] a = (arg ?? "").Split('~');
            string id = a[0].Trim();
            var find = typeof(WorldMacroPlaytestSession).GetMethod("FindInteractionPoint", Instance, null, new[] { typeof(string) }, null);
            var all = HarnessUiRules304.Member(session, "InteractionPoints") as PrologueContentSO.Point[] ?? Array.Empty<PrologueContentSO.Point>();
            var point = find != null ? find.Invoke(session, new object[] { id }) as PrologueContentSO.Point : Array.Find(all, x => x != null && x.Id == id);
            Need(point != null, "near:<point id>[~metres]: no interaction point '" + id + "' (known: " + string.Join(",", all.Where(x => x != null).Select(x => x.Id).Take(40)) + ")");
            float metres = Mathf.Min(1.6f, point.Radius * .6f);
            if (a.Length > 1) Need(float.TryParse(a[1], NumberStyles.Float, CultureInfo.InvariantCulture, out metres) && metres > .2f && metres < 30f, "metres must be .2..30");
            var body = session.Walker.Body.transform;
            Vector3 from = body.position - point.Position; from.y = 0f;
            if (from.sqrMagnitude < .01f) from = Vector3.back;
            from.Normalize();
            Vector3 feet = default; bool found = false;
            foreach (float angle in new[] { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 135f, -135f, 180f })
            {
                Vector3 candidate = point.Position + Quaternion.Euler(0f, angle, 0f) * from * metres;
                if (session.TrySafeFeet(candidate, out feet)) { found = true; break; }
            }
            Need(found, "no safe feet within " + F2(metres) + " m of '" + id + "'");
            Vector3 look = point.Position - feet; look.y = 0f;
            float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            if (!nearHeld)
            {
                Vector3 wasFeet = body.position; float wasYaw = body.eulerAngles.y; nearHeld = true;
                tourRestore.Add(() => { if (session != null && nearHeld && session.Walker != null && session.Walker.Body != null) { session.Teleport(wasFeet, wasYaw); nearHeld = false; } });
            }
            session.Teleport(feet, yaw); Physics.SyncTransforms();
            return "near=" + id + " feet=" + V3(feet) + " distance=" + F2(Vector3.Distance(feet, point.Position)) + " radius=" + F2(point.Radius) + " yaw=" + F2(yaw)
                + (root != null && root.Page.Length > 0 ? " (page " + root.Page + " is open: close it for focus)" : "");
        }

        // ------------------------------------------------------------------ reveal-map
        /// <summary>reveal-map[:on|off]: swaps the presenter's discovery grid for an all-discovered one (WorldMapPresenter.discovery +
        /// RefreshFog, by name) so a capture shows the printed map without fog. Only on a suffixed review save. The saved
        /// discoveredCells are not written (the grid is in memory); the markers the presenter auto-discovers while revealed are put
        /// back and the review save is re-saved when the reveal ends (reveal-map:off, tour end or failure).</summary>
        static string TourRevealMap(PlaytestUiRoot root, string arg)
        {
            string mode = (arg ?? "").Trim().ToLowerInvariant(); if (mode.Length == 0) mode = "on";
            Need(mode == "on" || mode == "off", "reveal-map[:on|off]");
            if (mode == "off") return revealHeld && revealUndo != null ? revealUndo() : "reveal not active";
            Need(root != null && root.Map != null && root.Session != null && root.Session.Progress != null && root.Session.Progress.ui != null, "reveal-map needs the gameplay PlaytestUiRoot with its map and a loaded session");
            var session = root.Session;
            Need(!string.IsNullOrEmpty(session.TestSaveSuffix), "reveal-map runs only on a suffixed review save (session.TestSaveSuffix is empty = the production slot)");
            if (revealHeld) return "already revealed";
            var map = root.Map;
            var field = typeof(WorldMapPresenter).GetField("discovery", Instance);
            var data = HarnessUiRules304.Member(map, "data") as WorldMapBakedDataSO;
            var refresh = typeof(WorldMapPresenter).GetMethod("RefreshFog", Instance, null, Type.EmptyTypes, null);
            if (field == null || data == null || refresh == null) return "SKIPPED WorldMapPresenter.discovery / data / RefreshFog() not found by name";
            if (HarnessUiRules304.Member(map, "progressLoaded") is bool loaded && !loaded)
                typeof(WorldMapPresenter).GetMethod("LoadProgressIfReady", Instance, null, Type.EmptyTypes, null)?.Invoke(map, null);   // else the next Tick replaces the revealed grid
            var before = field.GetValue(map) as WorldMapDiscoveryGrid;
            Need(before != null, "the map has no discovery grid");
            int playable = 0, discovered = 0;
            for (int y = 0; y < before.Height; y++) for (int x = 0; x < before.Width; x++)
                if (before.IsPlayableCell(x, y)) { playable++; if (before.IsDiscovered(x, y)) discovered++; }
            var all = new WorldMapDiscoveryGrid(data.BoundsMin, data.BoundsMax, data.Outline, Enumerable.Repeat((byte)0xFF, before.ByteCount).ToArray());
            Need(all.Width == before.Width && all.Height == before.Height, "grid size mismatch " + all.Width + "x" + all.Height + " vs " + before.Width + "x" + before.Height);
            var ui = session.Progress.ui;
            string cellsBefore = ui.discoveredCells;
            var markersBefore = ui.discoveredMarkers != null ? new List<string>(ui.discoveredMarkers) : null;
            field.SetValue(map, all); refresh.Invoke(map, null); HarnessUiRules304.SetMember(map, "inkReady", false);
            revealHeld = true;
            revealUndo = () =>
            {
                if (!revealHeld) return "reveal not active";
                revealHeld = false;
                if (map != null) { field.SetValue(map, before); refresh.Invoke(map, null); HarnessUiRules304.SetMember(map, "inkReady", false); }
                string saved = "session gone";
                if (session != null && session.Progress != null && session.Progress.ui != null)
                {
                    var now = session.Progress.ui;   // SaveNow may have replaced Progress: write the values into the current object
                    now.discoveredCells = cellsBefore;
                    if (markersBefore != null) { if (now.discoveredMarkers == null) now.discoveredMarkers = new List<string>(); now.discoveredMarkers.Clear(); now.discoveredMarkers.AddRange(markersBefore); }
                    saved = session.SaveNow(out string error) ? "review save re-saved" : "re-save failed: " + error;
                }
                return "reveal undone (" + saved + ")";
            };
            tourRestore.Add(() => { if (revealHeld && revealUndo != null) state.log.Add("restore: " + revealUndo()); });
            var material = HarnessUiRules304.Member(map, "paperMaterial") as Material;
            string Tex(string property) => material != null && material.HasProperty(property) && material.GetTexture(property) != null ? material.GetTexture(property).name : "-";
            return "revealed; before=" + discovered + "/" + playable + " playable cells discovered (" + (playable > 0 ? (100f * discovered / playable).ToString("0.0", CultureInfo.InvariantCulture) : "0") + "%)"
                + " illustrated=" + data.HasIllustration + " paintedRelief=" + data.PaintedRelief + " display=" + (data.DisplayMap != null ? data.DisplayMap.name : "-")
                + " explored=" + (data.ExploredMap != null ? data.ExploredMap.name : "-") + " tiles=" + (data.RegionTiles != null ? data.RegionTiles.Length : 0)
                + " paper _MapTex=" + Tex("_MapTex") + " _DetailTex=" + Tex("_DetailTex") + " _HasDetail=" + (material != null && material.HasProperty("_HasDetail") ? F2(material.GetFloat("_HasDetail")) : "-")
                + " _FogTex=" + Tex("_FogTex");
        }

        // ------------------------------------------------------------------ focus-report
        [Serializable]
        sealed class FocusReport304
        {
            public string utc, set, name, page, verdict;
            public string selected, selectedName, selectedVisual, selectedDabAnchor;
            public bool selectedInRail, selectedInteractable, selectedActive;
            public int visibleDabs, activeMarks;
            public string[] marks = Array.Empty<string>();
        }

        /// <summary>focus-report[:name]: the EventSystem selection (path, rail tab or not, its IFocusVisual304 / DabAnchor) and every
        /// FocusMark304 (Current, private target, Visible, dab screen position / alpha, scope). The verdict names the mismatch.</summary>
        static string TourFocusReport(PlaytestUiRoot root, string name)
        {
            var r = new FocusReport304 { utc = DateTime.UtcNow.ToString("O"), set = state.set, name = name ?? "", page = root != null ? root.Page : "<no root>" };
            var es = EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            Transform rootT = root != null ? root.transform : null;
            IFocusVisual304 visual = sel != null ? sel.GetComponent<IFocusVisual304>() : null;
            RectTransform anchor = (visual as Object) != null ? visual.DabAnchor : null;
            r.selected = sel != null ? HarnessUiRules304.Path(sel.transform, rootT) : "<none>";
            r.selectedName = sel != null ? sel.name : null;
            r.selectedActive = sel != null && sel.activeInHierarchy;
            r.selectedInRail = sel != null && sel.GetComponentInParent<MenuRail304>() != null;
            var selectable = sel != null ? sel.GetComponent<Selectable>() : null;
            r.selectedInteractable = selectable != null && selectable.IsInteractable();
            r.selectedVisual = (visual as Object) != null ? visual.GetType().Name : "none";
            r.selectedDabAnchor = anchor != null ? HarnessUiRules304.Path(anchor, rootT) : "none";
            r.visibleDabs = FocusMark304.VisibleCount; r.activeMarks = FocusMark304.ActiveCount;
            var lines = new List<string>(); string follows = null;
            foreach (var mark in Object.FindObjectsByType<FocusMark304>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mark == null) continue;
                var target = HarnessUiRules304.Member(mark, "target") as RectTransform;
                var owner = target != null ? target.GetComponentInParent<Selectable>() : null;
                Vector2 screen = mark.Dab != null ? RectTransformUtility.WorldToScreenPoint(null, mark.Dab.rectTransform.position) : Vector2.zero;
                float alpha = HarnessUiRules304.TryFloat(mark, "dabAlpha", out float da) ? da : -1f;
                lines.Add(HarnessUiRules304.Path(mark.transform, rootT) + " enabled=" + mark.isActiveAndEnabled + " current=" + (mark.Current != null ? mark.Current.name : "<none>")
                    + " target=" + (target != null ? HarnessUiRules304.Path(target, rootT) : "<none>") + " targetOwner=" + (owner != null ? owner.name : "<none>")
                    + " visible=" + mark.Visible + " dabScreen=(" + screen.x.ToString("0") + "," + screen.y.ToString("0") + ") dabAlpha=" + F2(alpha)
                    + " scope=" + (mark.Scope != null ? HarnessUiRules304.Path(mark.Scope, rootT) : "<none>"));
                if (mark.Visible && sel != null) follows = mark.Current == sel && target == anchor ? "follows" : "MISMATCH (mark current=" + (mark.Current != null ? mark.Current.name : "<none>") + ", target owner=" + (owner != null ? owner.name : "<none>") + ")";
            }
            r.marks = lines.ToArray();
            r.verdict = sel == null ? "nothing selected" + (r.visibleDabs > 0 ? " but " + r.visibleDabs + " dab(s) visible" : "")
                : r.visibleDabs == 0 ? "selection " + sel.name + " has no visible 방점 (visual=" + r.selectedVisual + ", anchor=" + r.selectedDabAnchor + ")"
                : r.visibleDabs > 1 ? r.visibleDabs + " 방점 visible (one per open menu, §9.2)"
                : "방점 " + (follows ?? "follows") + " selection " + sel.name + (r.selectedInRail ? " (a rail tab)" : "");
            string path = ReportPath("focus_report", name);
            File.WriteAllText(path, JsonUtility.ToJson(r, true));
            state.reports.Add(path);
            return "selected=" + r.selected + (r.selectedInRail ? " [rail]" : "") + " verdict=" + r.verdict + " report=" + path;
        }

        // ------------------------------------------------------------------ page-default / rail
        static string PageId304(string raw)
        {
            string s = (raw ?? "").Trim();
            switch (s.ToLowerInvariant())
            {
                case "pause": case "일시정지": return "일시정지";
                case "inventory": case "소지품": return "소지품";
                case "codex": case "술식": case "술식 도감": return "술식 도감";
                case "chapae": case "차패": return "차패";
                case "map": case "지도": return "지도";
                case "options": case "설정": case "옵션": return "옵션";
                case "controls": case "조작": case "조작 안내": return "조작 안내";
                default: return s;
            }
        }

        /// <summary>page-default:&lt;page&gt; — from gameplay (menus closed first): 일시정지 = Esc (Back), 소지품 = I, 지도 = M (the handlers
        /// call OpenPage with no page open), the other pages = Esc then the pause list row Tab_&lt;page&gt; selected and submitted
        /// (Enter). On the title: the Options / Controls row. Then judges the initial selection (IMPLEMENTATION §4.2).</summary>
        static string TourPageDefault(PlaytestUiRoot root, string arg)
        {
            Need(root != null, "no PlaytestUiRoot");
            string page = PageId304(arg);
            bool title = root.IsTitle;
            Need(PlaytestUiRoot.IsSupportedPage(page, title) && page != "상세" && page != "정비" && page != "장비 상점" && page != "장비 강화",
                "page-default:<page> with page in 일시정지/소지품/술식 도감/차패/지도/옵션/조작 안내 (aliases pause/inventory/codex/chapae/map/options/controls)" + (title ? "; the title has 옵션/조작 안내" : ""));
            var sub = new List<string> { "_menu-closed" };
            if (title) { sub.Add("_select:" + (page == "옵션" ? "Options" : "Controls")); sub.Add("_submit"); }
            else if (page == "일시정지") sub.Add("_esc");
            else if (page == "소지품" || page == "지도") sub.Add("_open:" + page);
            else { sub.Add("_esc"); sub.Add("_select:Tab_" + page); sub.Add("_submit"); }
            sub.Add("_settle");   // #304 QA2: the bleed-in runs on unscaled time; 16 frames are ~.1 s on the light title scene
            sub.Add("_verdict:default~" + page);
            PushFront(sub);
            return "route=" + string.Join(" > ", sub);
        }

        /// <summary>rail:&lt;page&gt; — the rail tab Tab_&lt;page&gt; of the open page is selected and submitted (a click / Enter on the rail).
        /// Rail navigation keeps the rail tab selected on the new page.</summary>
        static string TourRail(PlaytestUiRoot root, string arg)
        {
            Need(root != null, "no PlaytestUiRoot");
            string page = PageId304(arg);
            Need(root.Page.Length > 0 && root.Menu304Rail != null, "rail:<page> needs an open page with a rail (open one with page-default first); page=" + root.Page);
            Need(root.Menu304Rail.TabButton(page) != null, "the rail has no tab for '" + page + "' (tabs: " + string.Join(",", root.Menu304Rail.Tabs.Select(t => t.Page)) + ")");
            var sub = new List<string> { "_select:Tab_" + page, "_submit", "_settle", "_verdict:rail~" + page };
            PushFront(sub);
            return "route=" + string.Join(" > ", sub);
        }

        /// <summary>_submit[:name]: ISubmitHandler on the selected object (or the named active Selectable), what the UI input module
        /// sends for Enter / pad A.</summary>
        static string TourSubmit(PlaytestUiRoot root, string name)
        {
            var es = EventSystem.current;
            Need(es != null, "no EventSystem");
            GameObject target = es.currentSelectedGameObject;
            if (!string.IsNullOrEmpty(name))
                target = (root != null ? root.GetComponentsInChildren<Selectable>(false) : Object.FindObjectsByType<Selectable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    .FirstOrDefault(s => s.name == name)?.gameObject;
            Need(target != null && target.activeInHierarchy, "_submit: nothing " + (string.IsNullOrEmpty(name) ? "selected" : "named '" + name + "'") + " to submit");
            string was = root != null ? root.Page : "-";
            string label = target.name;
            ExecuteEvents.Execute(target, new BaseEventData(es), ExecuteEvents.submitHandler);
            return "submitted=" + label + " page " + was + " -> " + (root != null ? root.Page : "-");
        }

        /// <summary>_verdict:default~&lt;page&gt; / _verdict:rail~&lt;page&gt;: the page is open and its selection follows §4.2 — 일시정지 =
        /// Resume; other pages = a content row (not a rail tab) when the page has any focusable content, the rail tab otherwise;
        /// rail navigation = the new page's rail tab. Adds PASS / FAIL to tour-status.checks.</summary>
        static string TourVerdict(PlaytestUiRoot root, string arg)
        {
            Need(root != null, "no PlaytestUiRoot");
            string[] a = (arg ?? "").Split('~');
            string kind = a[0].Trim().ToLowerInvariant(), page = PageId304(a.Length > 1 ? a[1] : root.Page);
            var es = EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            bool open = root.Page == page;
            bool inRail = sel != null && sel.GetComponentInParent<MenuRail304>() != null;
            bool pass; string expect;
            if (kind == "rail") { pass = open && inRail && sel.name == "Tab_" + page; expect = "the rail tab Tab_" + page + " (reached by rail navigation)"; }
            else if (page == "일시정지") { pass = open && sel != null && sel.name == "Resume"; expect = "Resume"; }
            else
            {
                Transform content = null;
                if (page == "지도") content = root.Map != null ? root.Map.FullRoot : null;
                else if (root.Menu304PageRoot != null) content = root.Menu304PageRoot.Find("PageContent") ?? root.Menu304PageRoot;
                bool hasContent = content != null && content.GetComponentsInChildren<Selectable>(false).Any(s => s != null && s.interactable && s.navigation.mode != Navigation.Mode.None);
                pass = open && sel != null && sel.activeInHierarchy && (hasContent ? !inRail : inRail);
                expect = hasContent ? "a content row, not a rail tab (§4.2 initial selection" + (page == "옵션" ? ": 설정 = 첫 행" : "") + ")" : "the rail tab (the page has no focusable content)";
            }
            string line = (pass ? "PASS " : "FAIL ") + (kind == "rail" ? "rail:" : "page-default:") + page + " page=" + root.Page + " selected=" + SelectedPath(root)
                + (inRail ? " [rail]" : "") + " expected " + expect + " dabs=" + FocusMark304.VisibleCount + (lastSettle != null ? " ui=" + lastSettle : "");
            state.checks.Add(line);
            return line;
        }

        // ------------------------------------------------------------------ settle (#304 QA round 2)
        /// <summary>settle[:max seconds] / _settle: see Settle.</summary>
        static string TourSettle(PlaytestUiRoot root, string arg, string what)
        {
            double max = 3d;
            if (!string.IsNullOrEmpty(arg)) Need(double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out max) && max > 0d && max <= 20d, what + "[:max seconds 0..20]");
            double min = HarnessUiRules304.MotionBudgetSeconds(root);
            Settle(root, what, min, max);
            return "waiting for the UI to settle (min " + min.ToString("0.00", CultureInfo.InvariantCulture) + " s, max " + max.ToString("0.#", CultureInfo.InvariantCulture) + " s)";
        }

        /// <summary>The frame-count waits after page / close / back / detail / confirm / dismiss / loading are followed by a settle.
        /// #304 QA2: `page:` waited 12 frames, ~90 ms in the light title scene, so after2_lobby/title_options.png caught the page in
        /// its bleed-in (rail α.63, content α.15, the focused paper underlay α.15^2.2 through the InkReveal remap, the title card
        /// still fading out) = the modelled tween values at t ≈ 90 ms. Gameplay pages run at a lower frame rate and looked done.</summary>
        static void SettleAfter(PlaytestUiRoot root, string what) => Settle(root, what, HarnessUiRules304.MotionBudgetSeconds(root), 3d);

        /// <summary>Holds the tour until the #304 tweens under the menu root are at rest: `minSeconds` of unscaled time since the step,
        /// then no CanvasGroup alpha / InkRevealEffect reveal / 방점 change for 3 frames spanning .06 s. Soft: after `maxSeconds` the
        /// tour goes on and the log names what still moves. The result is kept in lastSettle (verdicts and reports quote it).</summary>
        static void Settle(PlaytestUiRoot root, string what, double minSeconds, double maxSeconds)
        {
            settleLast.Clear(); settleChanged.Clear(); settleSampled = false; settleStillFrames = 0;
            double start = EditorApplication.timeSinceStartup; settleStillSince = start;
            lastSettle = settleMoving = null;
            WaitUntil("the UI to settle after " + what, () => SettleTick(root, what, start, minSeconds, maxSeconds), maxSeconds + 5d);
        }

        static bool SettleTick(PlaytestUiRoot root, string what, double start, double min, double max)
        {
            double now = EditorApplication.timeSinceStartup, elapsed = now - start;
            HarnessUiRules304.SampleMotion(root, settleNow, settleNames);
            int moved = settleSampled ? HarnessUiRules304.MotionChanged(settleLast, settleNow, settleChanged) : 1;
            if (moved > 0)
            {
                settleStillFrames = 0; settleStillSince = now;
                if (settleChanged.Count > 0) settleMoving = string.Join(", ", settleChanged.Take(4).Select(k => settleNames.TryGetValue(k, out string n) ? n : "<gone>"));
            }
            else settleStillFrames++;
            settleLast.Clear(); foreach (var kv in settleNow) settleLast[kv.Key] = kv.Value;
            settleSampled = true;
            if (elapsed >= min && settleStillFrames >= 3 && now - settleStillSince >= .06d)
            { lastSettle = "settled " + elapsed.ToString("0.00", CultureInfo.InvariantCulture) + "s"; return true; }
            if (elapsed < max) return false;
            lastSettle = "NOT settled after " + max.ToString("0.#", CultureInfo.InvariantCulture) + "s (last moving: " + (settleMoving ?? "-") + ")";
            state.log.Add("f" + Time.frameCount + " " + what + ": " + lastSettle);
            return true;
        }

        // ------------------------------------------------------------------ alpha-report (#304 QA round 2)
        [Serializable]
        sealed class AlphaEntry304
        {
            public string scope, canvas, path, type, text, material, shader, layer, groupCause, state = "single";   // scope: page / title / menu / other
            public bool compensated;
            // effective = colorAlpha x groupAlpha x crossfadeAlpha (before the texture and any shader remap); drawnAlpha = what the
            // shader composites (the InkReveal remap when compensated); mockupMatchAlpha = the linear alpha that reads like the
            // mockup's sRGB alpha `effective` (for an uncompensated graphic the gap to `effective` is the linear-blending error)
            public float colorAlpha, groupAlpha, crossfadeAlpha, inheritedAlpha, effective, drawnAlpha, mockupMatchAlpha;
            public float reveal = -1f, effectiveLater = -1f;
        }

        [Serializable]
        sealed class AlphaGroup304
        {
            public string path, note, state = "single";
            public float alpha, alphaLater = -1f;
            public int graphics;
        }

        [Serializable]
        sealed class AlphaReport304
        {
            public string utc, set, name, scene, page, colorSpace, settle, titleCard, verdict;
            public bool title;
            public float secondSampleSeconds;
            public int drawn, translucent, byGroup, byColour, settling, stuckGroups, uncompensatedOffMockup, inPage, inTitleCard;
            public List<AlphaGroup304> groups = new List<AlphaGroup304>();
            public List<AlphaEntry304> entries = new List<AlphaEntry304>();
        }

        sealed class AlphaSample304
        {
            public readonly Dictionary<int, AlphaEntry304> Entries = new Dictionary<int, AlphaEntry304>();
            public readonly Dictionary<int, AlphaGroup304> Groups = new Dictionary<int, AlphaGroup304>();
            public readonly Dictionary<int, CanvasGroup> GroupObjects = new Dictionary<int, CanvasGroup>();
            public readonly Dictionary<int, float> AllEffective = new Dictionary<int, float>();   // every visible drawn graphic
            public int Drawn;
        }

        sealed class AlphaPending304 { public string Name; public AlphaSample304 First; public float Seconds; }

        static string FullPath(Component c) => c != null ? HarnessUiRules304.Path(c.transform, null) : "<none>";

        static string ShortText(Graphic g)
        {
            string t = HarnessUiRules304.TextOf(g).Replace("\n", " ").Trim();
            return t.Length > 24 ? t.Substring(0, 24) + "…" : t;
        }

        /// <summary>Every drawn Graphic in the loaded scenes (all enabled canvases: menu, title, HUD, toasts, arrival, world F) with a
        /// visible effective alpha; the ones below .98 become entries, grouped by the lowest CanvasGroup of their chain.</summary>
        static AlphaSample304 SampleAlpha(PlaytestUiRoot root)
        {
            var sample = new AlphaSample304();
            Transform page = root != null ? root.Menu304PageRoot : null, card = FindTitleCard(root);
            Transform map = root != null && root.Map != null && root.Page == "지도" ? root.Map.FullRoot : null;
            string ScopeOf(Transform t) => page != null && t.IsChildOf(page) || map != null && t.IsChildOf(map) ? "page"
                : card != null && t.IsChildOf(card) ? "title" : root != null && t.IsChildOf(root.transform) ? "menu" : "other";
            foreach (var g in Object.FindObjectsByType<Graphic>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!HarnessUiRules304.IsDrawn(g)) continue;
                float effective = HarnessUiRules304.EffectiveAlpha(g);
                if (effective <= 1f / 255f) continue;   // not visible (hit areas, faded-out rows, a hidden card)
                sample.Drawn++;
                sample.AllEffective[g.GetInstanceID()] = effective;
                if (effective >= .98f) continue;
                var material = g.materialForRendering;
                bool compensated = HarnessUiRules304.IsGammaCompensated(material, out float gamma);
                var low = HarnessUiRules304.LowestGroup(g);
                var rootCanvas = g.canvas != null ? g.canvas.rootCanvas : null;
                g.TryGetComponent(out InkRevealEffect fx);
                sample.Entries[g.GetInstanceID()] = new AlphaEntry304
                {
                    scope = ScopeOf(g.transform),
                    canvas = rootCanvas != null ? rootCanvas.name + (rootCanvas.renderMode == RenderMode.WorldSpace ? " (world)" : "") : "<none>",
                    path = FullPath(g), type = g.GetType().Name, text = ShortText(g),
                    material = material != null ? material.name : "<none>",
                    shader = material != null && material.shader != null ? material.shader.name : "<none>",
                    compensated = compensated, layer = HarnessUiRules304.LayerOf(g.color),
                    colorAlpha = g.color.a, groupAlpha = HarnessUiRules304.GroupAlpha(g),
                    crossfadeAlpha = g.canvasRenderer.GetAlpha(), inheritedAlpha = g.canvasRenderer.GetInheritedAlpha(),
                    effective = effective,
                    drawnAlpha = compensated ? HarnessUiRules304.LinearMatchAlpha(g.color, effective, gamma) : effective,
                    mockupMatchAlpha = HarnessUiRules304.LinearMatchAlpha(g.color, effective),
                    groupCause = low != null ? FullPath(low) + " α" + F2(low.alpha) : null,
                    reveal = fx != null && fx.isActiveAndEnabled ? fx.Reveal : -1f,
                };
                if (low == null) continue;
                int id = low.GetInstanceID();
                if (!sample.Groups.TryGetValue(id, out var group))
                {
                    var gate = low.GetComponent<FlowTitleGate304>();
                    sample.Groups[id] = group = new AlphaGroup304
                    {
                        path = FullPath(low), alpha = low.alpha,
                        note = (gate != null ? "FlowTitleGate304 hidden=" + gate.Hidden + " " : "") + "interactable=" + low.interactable + " blocksRaycasts=" + low.blocksRaycasts
                    };
                    sample.GroupObjects[id] = low;
                }
                group.graphics++;
            }
            return sample;
        }

        /// <summary>alpha-report[:name[~seconds]]: see the file summary. Written when the second sample is taken (or at once for 0).</summary>
        static string TourAlphaReport(PlaytestUiRoot root, string arg)
        {
            string[] a = (arg ?? "").Split('~');
            string name = a[0].Trim();
            float seconds = .3f;
            if (a.Length > 1) Need(float.TryParse(a[1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && seconds >= 0f && seconds <= 10f, "alpha-report[:name[~seconds 0..10]]");
            var first = SampleAlpha(root);
            if (seconds <= 0f) return WriteAlphaReport(root, name, first, null, 0f);
            alphaPending = new AlphaPending304 { Name = name, First = first, Seconds = seconds };
            double until = EditorApplication.timeSinceStartup + seconds;
            WaitUntil("the alpha-report second sample", () => EditorApplication.timeSinceStartup >= until, seconds + 5d);
            PushFront(new[] { "_alpha-report2" });
            return "first sample: drawn=" + first.Drawn + " translucent=" + first.Entries.Count + " groups=" + first.Groups.Count + "; second sample in " + seconds.ToString("0.##", CultureInfo.InvariantCulture) + " s";
        }

        static string TourAlphaReportSecond(PlaytestUiRoot root)
        {
            Need(alphaPending != null, "_alpha-report2 without a pending alpha-report");
            var pending = alphaPending; alphaPending = null;
            return WriteAlphaReport(root, pending.Name, pending.First, SampleAlpha(root), pending.Seconds);
        }

        static string WriteAlphaReport(PlaytestUiRoot root, string name, AlphaSample304 first, AlphaSample304 later, float seconds)
        {
            var r = new AlphaReport304
            {
                utc = DateTime.UtcNow.ToString("O"), set = state.set, name = name ?? "", scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
                page = root != null ? root.Page : "<no root>", title = root != null && root.IsTitle, colorSpace = QualitySettings.activeColorSpace.ToString(),
                settle = lastSettle ?? "-", titleCard = TitleCardState(root), secondSampleSeconds = later != null ? seconds : 0f, drawn = first.Drawn
            };
            foreach (var e in first.Entries.OrderBy(kv => kv.Value.scope, StringComparer.Ordinal).ThenBy(kv => kv.Value.canvas, StringComparer.Ordinal).ThenBy(kv => kv.Value.path, StringComparer.Ordinal))
            {
                var entry = e.Value;
                if (later != null)
                {
                    if (later.AllEffective.TryGetValue(e.Key, out float then)) { entry.effectiveLater = then; entry.state = Mathf.Abs(then - entry.effective) > .01f ? "settling" : "static"; }
                    else entry.state = "gone";   // not drawn / invisible at the second sample
                }
                r.entries.Add(entry);
                if (entry.groupCause != null) r.byGroup++; else r.byColour++;
                if (entry.scope == "page") r.inPage++; else if (entry.scope == "title") r.inTitleCard++;
                if (entry.state == "settling" || entry.state == "gone") r.settling++;
                if (!entry.compensated && Mathf.Abs(entry.mockupMatchAlpha - entry.effective) > .05f) r.uncompensatedOffMockup++;
            }
            foreach (var kv in first.Groups.OrderByDescending(kv => kv.Value.graphics))
            {
                var g = kv.Value;
                if (later != null)
                {
                    first.GroupObjects.TryGetValue(kv.Key, out var obj);
                    if (obj != null) { g.alphaLater = obj.enabled ? obj.alpha : 1f; g.state = Mathf.Abs(g.alphaLater - g.alpha) > .01f ? "settling" : "static"; }
                    else g.state = "gone";
                }
                if (g.state == "static") r.stuckGroups++;   // unchanged over the second-sample interval
                r.groups.Add(g);
            }
            r.translucent = r.entries.Count;
            string Group(AlphaGroup304 g) => g.path.Split('/').Last() + " α" + F2(g.alpha) + (g.alphaLater >= 0f ? "->" + F2(g.alphaLater) : "") + " (" + g.graphics + ")";
            var still = r.groups.Where(g => g.state == "static" || g.state == "single").ToList();
            var moving = r.groups.Where(g => g.state == "settling" || g.state == "gone").ToList();
            r.verdict = still.Count > 0
                    ? (later != null ? "CanvasGroup STATIC below .98 (stuck unless intended): " : "CanvasGroup below .98 (one sample): ") + string.Join(", ", still.Take(5).Select(Group))
                        + (moving.Count > 0 ? "; still bleeding in: " + string.Join(", ", moving.Take(3).Select(Group)) : "")
                : moving.Count > 0 ? "still bleeding in at the first sample (captured too early): " + string.Join(", ", moving.Take(5).Select(Group))
                : "no CanvasGroup below .98; " + r.byColour + " colour-translucent graphic(s), " + r.uncompensatedOffMockup + " uncompensated off the mockup alpha by > .05";
            string path = ReportPath("alpha_report", name);
            File.WriteAllText(path, JsonUtility.ToJson(r, true));
            state.reports.Add(path);
            return "drawn=" + r.drawn + " translucent=" + r.translucent + " (page " + r.inPage + ", title card " + r.inTitleCard + "; group " + r.byGroup + " / colour " + r.byColour + ") settling=" + r.settling
                + " uncompensatedOffMockup=" + r.uncompensatedOffMockup + " titleCard=" + r.titleCard + " verdict=" + r.verdict + " report=" + path;
        }

        static Transform FindTitleCard(PlaytestUiRoot root)
            => root != null ? root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "TitleCard") : null;

        /// <summary>Brightest visible effective alpha of the drawn graphics under `t` (0 = nothing shows).</summary>
        static float MaxDrawnAlpha(Transform t)
        {
            float max = 0f;
            if (t == null || !t.gameObject.activeInHierarchy) return 0f;
            foreach (var g in t.GetComponentsInChildren<Graphic>(false))
                if (HarnessUiRules304.IsDrawn(g)) max = Mathf.Max(max, HarnessUiRules304.EffectiveAlpha(g));
            return max;
        }

        static string TitleCardState(PlaytestUiRoot root)
        {
            if (root == null || !root.IsTitle) return "-";
            var card = FindTitleCard(root);
            if (card == null) return "missing";
            if (!card.gameObject.activeInHierarchy) return "inactive";
            var gate = card.GetComponent<FlowTitleGate304>();
            return "group α" + F2(HarnessUiRules304.GroupAlpha(card)) + " brightest α" + F2(MaxDrawnAlpha(card))
                + (gate != null ? " gateHidden=" + gate.Hidden + (gate.Group != null ? " raycasts=" + gate.Group.blocksRaycasts : "") : " (no FlowTitleGate304)");
        }

        // ------------------------------------------------------------------ title-page (#304 QA round 2)
        /// <summary>title-page:&lt;page&gt; (옵션 / 조작 안내, aliases options / controls / 설정 / 조작) on the title scene: menus closed and
        /// settled, the title row Options / Controls selected and submitted (Enter, the player's path), settled, then _title-verdict.</summary>
        static string TourTitlePage(PlaytestUiRoot root, string arg)
        {
            Need(root != null, "no PlaytestUiRoot");
            Need(root.IsTitle, "title-page:<page> runs on the title scene (the lobby); this scene is " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            string page = PageId304(arg);
            Need(page == "옵션" || page == "조작 안내", "title-page:<options|controls> (옵션 / 조작 안내 are the title's pages)");
            var sub = new List<string> { "_menu-closed", "_settle", "_select:" + (page == "옵션" ? "Options" : "Controls"), "_submit", "_settle", "_title-verdict:" + page };
            PushFront(sub);
            return "route=" + string.Join(" > ", sub);
        }

        /// <summary>_title-verdict:&lt;page&gt;: PASS when the page is open, its CanvasGroup chain (Page304 root up to the canvas) and every
        /// enabled CanvasGroup inside it are at 1, and the TitleCard shows nothing (inactive or every drawn graphic under it at α 0)
        /// and no longer blocks raycasts. Writes alpha_report_title_&lt;page&gt;.json as evidence. tour-status.checks gets the line.</summary>
        static string TourTitleVerdict(PlaytestUiRoot root, string arg)
        {
            Need(root != null, "no PlaytestUiRoot");
            string page = PageId304(arg);
            var fails = new List<string>();
            if (!root.IsTitle) fails.Add("not on the title");
            if (root.Page != page) fails.Add("page=" + (root.Page.Length > 0 ? root.Page : "<none>") + ", expected " + page);
            var pageRoot = root.Menu304PageRoot;
            float chain = -1f;
            if (pageRoot == null) fails.Add("no Menu304PageRoot");
            else
            {
                chain = HarnessUiRules304.GroupAlpha(pageRoot);
                if (chain < .999f) fails.Add("page chain α" + F2(chain) + " (" + FullPath(HarnessUiRules304.LowestGroup(pageRoot, .999f)) + ")");
                var low = pageRoot.GetComponentsInChildren<CanvasGroup>(false).Where(g => g != null && g.enabled && g.alpha < .999f)
                    .Select(g => HarnessUiRules304.Path(g.transform, pageRoot) + " α" + F2(g.alpha)).ToList();
                if (low.Count > 0) fails.Add("CanvasGroups below 1 inside the page: " + string.Join(", ", low.Take(6)));
            }
            var card = FindTitleCard(root);
            var gate = card != null ? card.GetComponent<FlowTitleGate304>() : null;
            float cardShows = MaxDrawnAlpha(card);
            if (card != null && cardShows > 1f / 255f) fails.Add("TitleCard shows through (" + TitleCardState(root) + ")");
            if (card != null && card.gameObject.activeInHierarchy && gate != null && gate.Group != null && gate.Group.blocksRaycasts) fails.Add("TitleCard still blocks raycasts");
            if (card != null && card.gameObject.activeInHierarchy && gate == null) fails.Add("TitleCard has no FlowTitleGate304");
            string alias = page == "옵션" ? "options" : "controls";
            string report = WriteAlphaReport(root, "title_" + alias, SampleAlpha(root), null, 0f);
            string reportPath = report.Substring(report.LastIndexOf(" report=", StringComparison.Ordinal) + 8);
            string line = (fails.Count == 0 ? "PASS " : "FAIL ") + "title-page:" + page + " page=" + (root.Page.Length > 0 ? root.Page : "<none>")
                + " pageChain=α" + F2(chain) + " titleCard=" + TitleCardState(root) + " selected=" + SelectedPath(root)
                + (lastSettle != null ? " ui=" + lastSettle : "") + (fails.Count > 0 ? " -- " + string.Join("; ", fails) : "") + " alphaReport=" + reportPath;
            state.checks.Add(line);
            return line;
        }
    }
}
