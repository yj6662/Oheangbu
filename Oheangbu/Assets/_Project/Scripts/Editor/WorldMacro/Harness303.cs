using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Oheangbu.App.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #303 follow-up test harnesses (CHANGGUI_COMBAT_TEST_PLAN.md): shared isolated-save lifecycle, hashing, disk reads and
    // small scene helpers used by Commission303Combat and Escort303Regression. Read/verify only: nothing here saves a scene
    // or an asset, and every save file it touches carries a harness suffix (_c303fight_ / _c303escort_).
    // Editor statics live across Play sessions (domain reload is disabled): callers reset their own state per run.
    [Serializable] internal sealed class Stamp303 { public string path, sha; }
    [Serializable] internal sealed class Check303 { public string id, status, detail; }   // PASS | FAIL | CONDITION | SKIPPED | INFO

    [Serializable] internal sealed class Isolation303
    {
        public string scenePath = "", slot = "", suffix = "", savedPath = "", runFolder = "";
        public string priorSuffix = "", priorUi = "__missing__", priorStartup = "";
        public bool priorDirect, priorDirectPresent, applied, cleaned;
        public string sceneSha = "", contentPath = "", contentSha = "";
        public List<Stamp303> saves = new List<Stamp303>(), guarded = new List<Stamp303>();
        public string priorFocusedWindowType = ""; public int priorFocusedWindowId;
    }

    internal static class Harness303
    {
        public const string MainScene = "Assets/_Project/Scenes/World/W_Demo_Main.unity";
        public const string CandidateScene = "Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity";
        public const string MainSlot = "world-main", FolkloreSlot = "world-folklore-298";
        public const string DirectPlayKey = "Compact270.DirectPlay", UiSuffixKey = "PlaytestUiReviewSuffix";
        public static string FolkloreScene => CompactFolklore298.ScenePath;
        // protected: read (hashed) only, never written by any #303 test command
        public static readonly string[] ProtectedAssets =
        {
            "Assets/_Project/Scenes/World/W_Demo_Compact.unity",
            "Assets/_Project/Art/World/WorldCompact/Data/03_Content.asset",
        };
        public static readonly string[] ProtectedFolders =
        {
            "Assets/_Project/Art/World/Watershed295", "Assets/_Project/Art/World/Reworld292", "Assets/_Project/Art/World/MountainTrail285",
        };

        public static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        public static string Roadside303Folder => Path.Combine(RepoRoot, "Art", "World", "Compact", "Rebuild", "Roadside303");
        public static string Abs(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        public static string UtcStamp() => DateTime.UtcNow.ToString("yyyyMMddTHHmmss", CultureInfo.InvariantCulture);

        public static bool TryTarget(string name, out string scene, out string slot)
        {
            switch (string.IsNullOrEmpty(name) ? "main" : name)
            {
                case "main": scene = MainScene; slot = MainSlot; return true;
                case "folklore298": scene = FolkloreScene; slot = FolkloreSlot; return true;
                default: scene = slot = null; return false;
            }
        }

        public static bool IsProtected(string assetPath)
        {
            string p = (assetPath ?? "").Replace('\\', '/');
            return ProtectedAssets.Any(a => string.Equals(a, p, StringComparison.OrdinalIgnoreCase)) ||
                   ProtectedFolders.Any(f => p.StartsWith(f + "/", StringComparison.OrdinalIgnoreCase));
        }

        public static WorldMacroPlaytestSession Session
        {
            get
            {
                var all = Object.FindObjectsByType<WorldMacroPlaytestSession>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                return all.Length == 1 ? all[0] : null;
            }
        }

        public static int SessionCount => Object.FindObjectsByType<WorldMacroPlaytestSession>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

        public static string Sha(string path)
        {
            using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }

        public static string SaveFolder => Application.persistentDataPath;

        public static List<Stamp303> StampSaves()
        {
            var list = new List<Stamp303>();
            if (!Directory.Exists(SaveFolder)) return list;
            foreach (var f in Directory.GetFiles(SaveFolder, "*.json*", SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.Ordinal))
                list.Add(new Stamp303 { path = f, sha = Sha(f) });
            return list;
        }

        /// <summary>Edit-mode preconditions and fingerprints for an isolated Play. Returns null when ready, else "refused: …".
        /// Nothing is changed by this call.</summary>
        public static string Prepare(Isolation303 iso, string scenePath, string slot, string suffixTag)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode required (Play is running)";
            if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) return "refused: scripts are compiling or failed to compile";
            var scene = SceneManager.GetActiveScene();
            if (scene.path != scenePath) return "refused: open " + scenePath + " first (active: " + scene.path + ")";
            if (scene.isDirty) return "refused: the open scene has unsaved changes";
            if (SceneManager.sceneCount != 1) return "refused: exactly one open scene required";
            if (SessionCount != 1) return "refused: exactly one WorldMacroPlaytestSession required, found " + SessionCount;
            var s = Session;
            if (s == null || s.Content == null) return "refused: no playtest session/content";
            if (s.Content.SaveSlot != slot) return "refused: content save slot is " + s.Content.SaveSlot + ", expected " + slot;
            if (!string.IsNullOrEmpty(s.TestSaveSuffix) && s.TestSaveSuffix.StartsWith("_c303", StringComparison.Ordinal))
                return "refused: the session still carries an earlier #303 harness suffix (" + s.TestSaveSuffix + "); run abort first";
            Directory.CreateDirectory(SaveFolder);
            iso.scenePath = scenePath; iso.slot = slot;
            iso.suffix = suffixTag + UtcStamp();
            iso.savedPath = Path.Combine(SaveFolder, slot + iso.suffix + ".json");
            if (new[] { "", ".bak", ".tmp" }.Any(end => File.Exists(iso.savedPath + end))) return "refused: isolated save name collision (" + iso.savedPath + "); nothing deleted";
            // the suffix is new for every run, so an ordinary (real) save can never be opened by the harness
            if (!iso.suffix.StartsWith("_c303", StringComparison.Ordinal)) return "refused: harness suffix must start with _c303";
            iso.priorSuffix = s.TestSaveSuffix ?? "";
            iso.priorUi = SessionState.GetString(UiSuffixKey, "__missing__");
            iso.priorStartup = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene);
            iso.priorDirect = SessionState.GetBool(DirectPlayKey, false);
            iso.priorDirectPresent = SessionState.GetBool(DirectPlayKey, false) == SessionState.GetBool(DirectPlayKey, true);
            var focused = EditorWindow.focusedWindow;
            iso.priorFocusedWindowId = focused != null ? focused.GetInstanceID() : 0;
            iso.priorFocusedWindowType = focused != null ? focused.GetType().FullName : "";
            iso.saves = StampSaves();
            iso.sceneSha = Sha(Abs(scenePath));
            iso.contentPath = AssetDatabase.GetAssetPath(s.Content);
            iso.contentSha = string.IsNullOrEmpty(iso.contentPath) ? "" : Sha(Abs(iso.contentPath));
            iso.guarded = new List<Stamp303>();
            foreach (var p in ProtectedAssets) if (File.Exists(Abs(p))) iso.guarded.Add(new Stamp303 { path = p, sha = Sha(Abs(p)) });
            return null;
        }

        /// <summary>Points the live session at the isolated store and plays the open scene directly (the two lines of
        /// CompactLoadingStartup270.UseCurrent, done here without calling it). Reverted by Cleanup.</summary>
        public static void Apply(Isolation303 iso, WorldMacroPlaytestSession s)
        {
            s.TestSaveSuffix = iso.suffix;
            SessionState.SetString(UiSuffixKey, iso.suffix);
            SessionState.SetBool(DirectPlayKey, true);
            EditorSceneManager.playModeStartScene = null;
            iso.applied = true;
        }

        /// <summary>Edit mode after the last Play: copy the isolated files into the run folder, delete them, put the suffix,
        /// SessionState and start scene back, then prove every earlier save and guarded file is byte-identical.</summary>
        public static List<Check303> Cleanup(Isolation303 iso, string runFolder)
        {
            var checks = new List<Check303>();
            void C(bool ok, string id, string detail) => checks.Add(new Check303 { id = id, status = ok ? "PASS" : "FAIL", detail = detail });
            try
            {
                Directory.CreateDirectory(runFolder);
                int moved = 0;
                foreach (var end in new[] { "", ".bak", ".tmp" })
                {
                    string f = iso.savedPath + end;
                    if (!File.Exists(f)) continue;
                    File.Copy(f, Path.Combine(runFolder, "isolated-save.json" + end), true); File.Delete(f); moved++;
                }
                checks.Add(new Check303 { id = "cleanup.isolated", status = "INFO", detail = moved + " isolated file(s) copied to the run folder and deleted" });
                var s = Session;
                if (s != null) { s.TestSaveSuffix = iso.priorSuffix; EditorUtility.ClearDirty(s); }
                if (iso.priorUi == "__missing__") SessionState.EraseString(UiSuffixKey); else SessionState.SetString(UiSuffixKey, iso.priorUi);
                RevertStartup(iso);
                EditorApplication.delayCall += () => RevertStartup(iso);
                C(s != null && (s.TestSaveSuffix ?? "") == iso.priorSuffix, "cleanup.suffix", "session suffix back to '" + iso.priorSuffix + "'");
                C(AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == (iso.priorStartup ?? "") &&
                  SessionState.GetBool(DirectPlayKey, false) == iso.priorDirect, "cleanup.startup", "start scene / direct-play flag reverted");
                var missing = iso.saves.Where(x => !File.Exists(x.path) || Sha(x.path) != x.sha).Select(x => x.path).ToList();
                C(missing.Count == 0, "cleanup.saves", missing.Count == 0 ? iso.saves.Count + " earlier save file(s) byte-identical" : "changed or missing: " + string.Join(", ", missing));
                var extra = Directory.GetFiles(SaveFolder, "*.json*").Where(p => iso.saves.All(x => x.path != p)).ToList();
                C(extra.Count == 0, "cleanup.no-new-saves", extra.Count == 0 ? "no new save files remain" : "left behind: " + string.Join(", ", extra));
                C(File.Exists(Abs(iso.scenePath)) && Sha(Abs(iso.scenePath)) == iso.sceneSha, "cleanup.scene", iso.scenePath + " byte-identical");
                C(string.IsNullOrEmpty(iso.contentPath) || Sha(Abs(iso.contentPath)) == iso.contentSha, "cleanup.content", iso.contentPath + " byte-identical");
                var guarded = iso.guarded.Where(g => !File.Exists(Abs(g.path)) || Sha(Abs(g.path)) != g.sha).Select(g => g.path).ToList();
                C(guarded.Count == 0, "cleanup.protected", guarded.Count == 0 ? "protected files byte-identical" : "changed: " + string.Join(", ", guarded));
            }
            catch (Exception e) { C(false, "cleanup.error", e.ToString()); }
            iso.cleaned = true;
            return checks;
        }

        static void RevertStartup(Isolation303 iso)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (iso.priorDirectPresent) SessionState.SetBool(DirectPlayKey, iso.priorDirect); else SessionState.EraseBool(DirectPlayKey);
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(iso.priorStartup) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(iso.priorStartup);
        }

        // ------------------------------------------------------------------ focus (298 discipline: never forced back)
        public static bool GameViewFocused => EditorWindow.focusedWindow != null && EditorWindow.focusedWindow.GetType().FullName == "UnityEditor.GameView";
        public static bool Focused => Application.isFocused && GameViewFocused;
        public static void FocusGameView()
        {
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (type != null) EditorWindow.GetWindow(type).Focus();
        }
        public static void FocusPrior(Isolation303 iso)
        {
            if (iso == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var previous = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetInstanceID() == iso.priorFocusedWindowId && w.GetType().FullName == iso.priorFocusedWindowType);
            if (previous != null) previous.Focus();
        }

        // ------------------------------------------------------------------ live session reads (reflection = read only)
        public const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        public static T Field<T>(object owner, string name) where T : class
        {
            if (owner == null) return null;
            for (var t = owner.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) return f.GetValue(owner) as T;
            }
            return null;
        }
        /// <summary>Unity-null aware fallback (GetComponent can return a fake null object in the Editor).</summary>
        public static T Or<T>(T first, T second) where T : Object => first != null ? first : second;
        public static object FieldValue(object owner, string name)
        {
            if (owner == null) return null;
            for (var t = owner.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) return f.GetValue(owner);
            }
            return null;
        }
        public static string StorePath(WorldMacroPlaytestSession s)
        {
            var store = FieldValue(s, "store");
            return store == null ? null : FieldValue(store, "path") as string;
        }
        /// <summary>The session's own interaction lookup (content, opening and live escort NPC points), read through
        /// reflection; falls back to Content.Points.</summary>
        public static Oheangbu.Data.World.PrologueContentSO.Point InteractionPoint(WorldMacroPlaytestSession s, string id)
        {
            if (s == null || string.IsNullOrEmpty(id)) return null;
            if (Application.isPlaying && s.Progress != null)
            {
                try
                {
                    var m = typeof(WorldMacroPlaytestSession).GetMethod("FindInteractionPoint", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
                    if (m != null && m.Invoke(s, new object[] { id }) is Oheangbu.Data.World.PrologueContentSO.Point p) return p;
                }
                catch { }
            }
            return s.Content != null ? s.Content.Points.FirstOrDefault(x => x.Id == id) : null;
        }
        public static WorldMacroProgress ReadDisk(string path)
        {
            try { return File.Exists(path) ? JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(path)) : null; }
            catch { return null; }
        }

        // ------------------------------------------------------------------ scene helpers
        public static Transform Find(Scene scene, string path)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == path) return root.transform;
                if (path.StartsWith(root.name + "/", StringComparison.Ordinal)) { var t = root.transform.Find(path.Substring(root.name.Length + 1)); if (t != null) return t; }
            }
            return null;
        }
        public static string PathOf(Transform t)
        {
            if (t == null) return "";
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
        public static bool IsTerrain(Collider c)
        {
            if (c is TerrainCollider) return true;
            for (var t = c.transform; t != null; t = t.parent) if (t.name.Contains("Terrain")) return true;
            return false;
        }
        /// <summary>Terrain surface under xz (props and buildings above it are skipped).</summary>
        public static bool Ground(Vector3 xz, out RaycastHit hit)
        {
            foreach (var h in Physics.RaycastAll(new Vector3(xz.x, 1200f, xz.z), Vector3.down, 2400f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                if (IsTerrain(h.collider)) { hit = h; return true; }
            hit = default; return false;
        }
        public static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "({0:F2}, {1:F2}, {2:F2})", v.x, v.y, v.z);
        public static string F(float v, string f = "F3") => v.ToString(f, CultureInfo.InvariantCulture);
        public static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        public static float YawTo(Vector3 from, Vector3 to) => Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;

        public static void WriteJson(string folder, string name, object data)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, name), JsonUtility.ToJson(data, true));
        }

        /// <summary>Game-view capture (UI overlay included) written at the end of this frame.</summary>
        public static string Capture(string folder, string name)
        {
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, name + ".png");
            ScreenCapture.CaptureScreenshot(file);
            return file;
        }
    }

    [Serializable] internal sealed class InteractionRow303
    {
        public string phase = "", expectedId = "", focusedBefore = "", focusedAtPress = "", title = "", body = "", feedback = "", expectedBody = "";
        public string closeMethod = "", status = "", detail = "", capture = "";
        public string followUp = "", menu = "";   // #306: first in-place follow-up (pages joined), the menu rows picked ("Leave", "CommissionAccept", …)
        public bool bodyMatches, focusMatched;
        public int details, resolved, presses, closePresses, approachTries, currencyBefore, currencyAfter, frame;
        public int followUps, menuSteps;          // #306: follow-ups answered in place inside Chosen (not interactions), arrow taps on the talk menu
        public float seconds;
    }

    /// <summary>Plan §5.5 conversation: walk in front of the point, let the real focus loop pick it (3 frames), press F
    /// (2 frames + release), take the text from DetailRequested / LastFeedback, capture two frames later, close the page
    /// (#304 rule: release, F again ≤6 × 0.3 s → Escape → adapter Back → adapter CloseMenu), then wait for three neutral
    /// unblocked frames. Adapter steps are recorded as fixture interventions.
    /// #306 talk menu (DialogueView304 menu showing): the arrow keys move the focus to the wanted row, then F picks it. The row is
    /// 떠나기 (Leave, always the last Choice_) unless the caller set MenuPick before Begin (picked once, then Leave again), so a close
    /// never accepts a commission, opens a shop or rests. Arrows that do not reach the row within MaxMenuSteps fall back to an
    /// EventSystem selection, recorded as a fixture and as "adapter select" in closeMethod.</summary>
    internal sealed class Talk303
    {
        const int MaxMenuSteps = 12;
        public string Status = "idle"; public InteractionRow303 Row = new InteractionRow303();
        public readonly List<string> Fixtures = new List<string>();
        public bool WaitRestPresentation;
        public Oheangbu.App.World.UI.DialogueServiceKind306? MenuPick;   // null = 떠나기; set before Begin, kept across runs until the caller changes it
        WorldMacroPlaytestSession s; string id, captureFolder, captureName; Transform npc;
        int stage, focusFrames, neutralFrames, textFrame = -1; double at, begun, lastClose;
        int closeStep, menuMoves; string lastFocus = "", source = "", closeExtra = ""; bool menuPicked, menuAdapter, conversationEnded;
        Vector3 point, front; float radius; bool fixedPoint;
        readonly Walker303 walker = new Walker303();
        string feedbackBefore; bool subscribed;
        string approachNote = "";   // why the first walk did not start (text for a failure line only)
        // " | <distance to the point against its radius> | <why the first walk did not start>" for the two failures that can happen before F
        string ApproachNote(Vector3 feet) => " | " + Harness303.F(Vector3.Distance(feet, point), "F2") + " m from the point (radius " + Harness303.F(radius, "F1") + " m)" + (approachNote.Length > 0 ? " | " + approachNote : "");

        public void Begin(WorldMacroPlaytestSession session, string phase, string pointId, Transform npcTransform, string expectedBody, string folder, string capture, Vector3? livePoint = null)
        {
            End();
            s = session; id = pointId; npc = npcTransform; captureFolder = folder; captureName = capture;
            Row = new InteractionRow303 { phase = phase, expectedId = pointId, expectedBody = expectedBody ?? "" };
            Fixtures.Clear(); Status = "running"; stage = 0; focusFrames = 0; neutralFrames = 0; textFrame = -1; closeStep = 0;
            menuMoves = 0; menuPicked = menuAdapter = conversationEnded = false; source = closeExtra = ""; approachNote = "";
            begun = at = EditorApplication.timeSinceStartup;
            var p = Harness303.InteractionPoint(s, pointId);
            if (p == null) { Status = "failed"; Row.detail = "no interaction point " + pointId; return; }
            fixedPoint = livePoint.HasValue; point = livePoint ?? p.Position; radius = p.Radius;
            s.DetailRequested += OnDetail; s.InteractionResolved += OnResolved; s.DialogueRequested += OnDialogue; s.DialogueEnded += OnDialogueEnded; subscribed = true;
        }

        void OnDetail(string title, string body) { Row.details++; if (Row.details == 1) { Row.title = title ?? ""; Row.body = body ?? ""; } }
        // #306: NPC speech arrives as one dialogue request (speaker + pages) instead of DetailRequested. A request from the same
        // source while the close runs (stage 6) and before that conversation ended (DialogueEnded) is a menu pick answered in place
        // inside Chosen (accept -> waiting pages, a Talk row): the same conversation, counted in followUps, not as a second
        // interaction. A request after the close (the closing F re-opening the talk) still counts in details (duplicate finding).
        void OnDialogue(Oheangbu.App.World.UI.DialogueRequest306 r)
        {
            if (r == null) return;
            string text = r.Lines != null ? string.Join("\n", r.Lines) : "";
            if (Row.details > 0 && stage == 6 && !conversationEnded && (r.SourceId ?? "") == source) { Row.followUps++; if (Row.followUps == 1) Row.followUp = text; return; }
            if (Row.details == 0) source = r.SourceId ?? "";
            OnDetail(r.Speaker, text);
        }
        void OnDialogueEnded(string sourceId) { if (Row.details > 0) conversationEnded = true; }
        void OnResolved(Oheangbu.Data.World.PrologueInteractionKind kind, Vector3 where) { Row.resolved++; }

        public void End()
        {
            if (subscribed && s != null) { s.DetailRequested -= OnDetail; s.InteractionResolved -= OnResolved; s.DialogueRequested -= OnDialogue; s.DialogueEnded -= OnDialogueEnded; }
            subscribed = false;
        }

        bool PageOpen => UiAdapter303.Page.Length > 0 || s.GameplayInputBlocked;

        // #306 talk menu, before a closing F: true = this tick moved the focus (one arrow tap, or the fixture selection once the
        // arrows ran out) and the F waits; false = no menu shows, or the wanted row is focused and the F picks it
        bool MenuStep(Vector2 pointer)
        {
            var ui = Oheangbu.App.World.UI.PlaytestUiRoot.Instance;
            var view = ui != null ? ui.Dialogue304 : null;
            if (view == null || !view.MenuShown || view.Root == null) return false;
            bool usePick = MenuPick.HasValue && !menuPicked;
            int want = MenuRow(view.Current, usePick ? MenuPick : null, out bool found);
            if (usePick && !found) { menuPicked = true; usePick = false; Row.menu += (Row.menu.Length > 0 ? ", " : "") + MenuPick.Value + " not offered or disabled -> Leave"; }
            var es = UnityEngine.EventSystems.EventSystem.current;
            int current = ChoiceIndex(es != null ? es.currentSelectedGameObject : null, view.Root);
            if (current == want)
            {
                if (usePick) menuPicked = true;
                Row.menu += (Row.menu.Length > 0 ? ", " : "") + (usePick ? MenuPick.Value.ToString() : "Leave");
                return false;
            }
            if (menuMoves < MaxMenuSteps)
            {
                // rows stack top (Choice_0) to bottom (Leave): Down = the next row
                VirtualInput303.Tap(current >= 0 && current > want ? Key.UpArrow : Key.DownArrow, pointer, 1);
                menuMoves++; Row.menuSteps++; CloseExtra(); return true;
            }
            // the arrows did not reach the row: select it directly (fixture; "adapter" keeps it out of an F-only close claim)
            UnityEngine.UI.Button target = null;
            foreach (var b in view.Root.GetComponentsInChildren<UnityEngine.UI.Button>(false))
                if (ChoiceIndex(b.gameObject, view.Root) == want) { target = b; break; }
            if (target == null || es == null) { Status = "failed"; Row.detail = "talk menu row Choice_" + want + " not found (" + UiAdapter303.Describe() + ")"; return true; }
            es.SetSelectedGameObject(target.gameObject);
            Fixtures.Add(id + ": talk menu focus set through EventSystem to Choice_" + want + " (arrows did not reach it after " + MaxMenuSteps + " taps)");
            menuAdapter = true; CloseExtra(); return true;
        }
        void CloseExtra()
        {
            closeExtra = (Row.menuSteps > 0 ? " + arrows x" + Row.menuSteps : "") + (menuAdapter ? " + adapter select" : "");
            Row.closeMethod = "F x" + Row.closePresses + closeExtra;
        }
        // the Choice_ index to pick: the first enabled row of `pick`, else Leave (the view appends it after every non-Leave row)
        static int MenuRow(Oheangbu.App.World.UI.DialogueRequest306 r, Oheangbu.App.World.UI.DialogueServiceKind306? pick, out bool found)
        {
            int rows = 0, want = -1;
            if (r != null && r.Services != null)
                foreach (var x in r.Services)
                {
                    if (x == null || x.Kind == Oheangbu.App.World.UI.DialogueServiceKind306.Leave) continue;
                    if (want < 0 && pick.HasValue && x.Kind == pick.Value && x.Enabled) want = rows;
                    rows++;
                }
            found = want >= 0; return found ? want : rows;
        }
        static int ChoiceIndex(GameObject selected, Transform root)
        {
            if (selected == null || root == null || !selected.transform.IsChildOf(root)) return -1;
            string n = selected.name;
            if (!n.StartsWith("Choice_", StringComparison.Ordinal)) return -1;
            return int.TryParse(n.Substring(7), NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : -1;
        }

        public InputFrame303 Tick(Vector2 pointer)
        {
            var hold = InputFrame303.Neutral(pointer);
            if (Status != "running") return hold;
            double now = EditorApplication.timeSinceStartup;
            var feet = s.Walker.Body.transform.position;
            switch (stage)
            {
                case 0:
                {
                    Vector3 dir = Vector3.ProjectOnPlane(feet - point, Vector3.up); if (dir.sqrMagnitude < .01f) dir = Vector3.back; dir.Normalize();
                    front = npc != null ? npc.position + npc.forward * 1.5f : point + dir * Mathf.Min(1.5f, Mathf.Max(.6f, radius - .6f));
                    if (NavMesh.SamplePosition(front, out var hit, 1.2f, NavMesh.AllAreas)) front = hit.position;
                    if (Harness303.Flat(feet, front) < .5f) { stage = 2; at = now; return hold; }
                    // harness repair 2026-10-06: a first walk that cannot start (no NavMesh path) went on to the focus stage without a word;
                    // the reason is kept and added to a later approach failure (the flow itself is unchanged)
                    if (!walker.Begin(s, front, .35f, 40f, id)) { approachNote = "walk to the front spot " + Harness303.V(front) + " did not start: " + walker.Detail; stage = 2; at = now; return hold; }
                    stage = 1; return walker.Tick(s, pointer);
                }
                case 1:
                {
                    var f = walker.Tick(s, pointer);
                    if (walker.Status == "walking") return f;
                    if (walker.Status == "failed" && Vector3.Distance(feet, point) > radius) { Status = "failed"; Row.detail = "approach failed: " + walker.Detail + " " + walker.LastBlock + ApproachNote(feet); return hold; }
                    stage = 2; at = now; focusFrames = 0; return hold;
                }
                case 2:
                {
                    if (!fixedPoint) { var live = Harness303.InteractionPoint(s, id); if (live != null) point = live.Position; }
                    hold.Delta = Steer303.Toward(s, point + Vector3.up * 1.2f, out float err);
                    lastFocus = s.FocusedId ?? "";
                    if (Mathf.Abs(err) < 4f) focusFrames = lastFocus == id ? focusFrames + 1 : 0;
                    if (focusFrames >= 3) { stage = 3; return hold; }
                    if (now - at > 1.2)
                    {
                        if (Row.approachTries >= 4) { Status = "failed"; Row.detail = "focus stayed on '" + lastFocus + "' after 4 approaches" + ApproachNote(feet); return hold; }
                        Row.approachTries++;
                        var step = Vector3.ProjectOnPlane(point - feet, Vector3.up).normalized * .3f;
                        if (!walker.Begin(s, feet + step, .12f, 3f, id + "+0.3m")) { stage = 2; at = now; return hold; }
                        stage = 1; return hold;
                    }
                    return hold;
                }
                case 3:
                    Row.focusedBefore = s.FocusedId ?? ""; feedbackBefore = s.LastFeedback; Row.currencyBefore = s.Progress.ledger.currency;
                    VirtualInput303.Tap(Key.F, pointer, 2); Row.presses++; Row.frame = Time.frameCount;
                    stage = 4; at = now; return hold;
                case 4:
                    if (VirtualInput303.LastFedFrame == Time.frameCount && VirtualInput303.LastFed.Has(Key.F) && string.IsNullOrEmpty(Row.focusedAtPress))
                        Row.focusedAtPress = s.FocusedId ?? "(null)";
                    if (Row.details > 0 || Row.resolved > 0 || s.LastFeedback != feedbackBefore)
                    {
                        if (Row.details == 0) Row.body = s.LastFeedback ?? "";
                        Row.feedback = s.LastFeedback ?? ""; textFrame = Time.frameCount; stage = 5; return hold;
                    }
                    if (now - at > 1.2) { Status = "failed"; Row.detail = "no text within 1 s of F (focus at press '" + Row.focusedAtPress + "')"; }
                    return hold;
                case 5:
                    if (Time.frameCount < textFrame + 2) return hold;
                    if (!string.IsNullOrEmpty(captureName)) Row.capture = Harness303.Capture(captureFolder, captureName);
                    stage = 6; at = lastClose = now; return hold;
                case 6:
                {
                    if (WaitRestPresentation && s.RestPresentationActive && UiAdapter303.Page.Length == 0 && now - at < 15) return hold;
                    if (!PageOpen) { if (string.IsNullOrEmpty(Row.closeMethod)) Row.closeMethod = Row.closePresses > 0 ? "F x" + Row.closePresses : "none (HUD prompt or no page)"; stage = 7; neutralFrames = 0; return hold; }
                    if (VirtualInput303.Pending > 0 || VirtualInput303.LastFed.Keys != null && VirtualInput303.LastFed.Keys.Length > 0) return hold;
                    if (now - lastClose < .3) return hold;
                    lastClose = now;
                    if (closeStep < 6 && MenuStep(pointer)) return hold;
                    if (closeStep < 6) { VirtualInput303.Tap(Key.F, pointer, 1); Row.closePresses++; closeStep++; Row.closeMethod = "F x" + Row.closePresses + closeExtra; return hold; }
                    if (closeStep == 6) { if (now - at < .6 + 6 * .3) return hold; VirtualInput303.Tap(Key.Escape, pointer, 1); closeStep++; Row.closeMethod += " + Escape"; return hold; }
                    if (closeStep == 7) { string r = UiAdapter303.Back(); Fixtures.Add(id + ": page closed through adapter Back() (" + r + ")"); closeStep++; Row.closeMethod += " + adapter Back"; return hold; }
                    if (closeStep == 8) { string a = UiAdapter303.CloseMenu(), b = UiAdapter303.ReleaseGate(); Fixtures.Add(id + ": adapter CloseMenu/ReleaseGate (" + a + ", " + b + ")"); closeStep++; Row.closeMethod += " + adapter CloseMenu"; return hold; }
                    Status = "failed"; Row.detail = "page did not close (" + UiAdapter303.Describe() + ")"; return hold;
                }
                case 7:
                {
                    bool quiet = !s.GameplayInputBlocked && VirtualInput303.Pending == 0 && (VirtualInput303.LastFed.Keys == null || VirtualInput303.LastFed.Keys.Length == 0) && !VirtualInput303.LastFed.Lmb;
                    neutralFrames = quiet ? neutralFrames + 1 : 0;
                    if (neutralFrames < 3) return hold;
                    Row.currencyAfter = s.Progress.ledger.currency; Row.seconds = (float)(now - begun);
                    Row.focusMatched = Row.focusedAtPress == id || Row.focusedBefore == id;
                    Row.bodyMatches = string.IsNullOrEmpty(Row.expectedBody) || Row.body == Row.expectedBody;
                    Row.status = "done"; Status = "done"; End(); return hold;
                }
            }
            return hold;
        }
    }
}
