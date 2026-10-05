using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #307 phase 0 — trustworthy baseline, measurement only (Tools/Unity/Plan307/PERF_DESIGN.md C.0). IMPLEMENTED, not validated.
    // One Play from the lobby exactly like the player (Finish297MainStart flow: isolated save suffix, lobby StartNew, wait for
    // InitializationComplete + loading done), then App/World/PerfRoute307 runs the station route of
    // Art/Performance/Perf307/routes.json and writes Art/Performance/Perf307/<utc>/ (summary.json, frames.csv, segments.csv,
    // spikes.csv, editor-checks.json). Queue-safe: every refusal is a returned "refused: …" string, never a dialog. One run at a time.
    //
    // Commands (Oheangbu.EditorTools.WorldMacro.Perf307.Run through Tools/Unity/playtest_polish.py, or Tools/Unity/perf307.py):
    //   baseline[:tier=pc|mobile][:variant=full|noart][:mode=uncapped|cap120]
    //       python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.Perf307 Run "baseline:tier=pc:variant=full:mode=uncapped"
    //       python Tools/Unity/perf307.py baseline tier=pc variant=full mode=uncapped      (starts, then polls status to the end)
    //   arrival[:tier=saved|pc|mobile][:mode=…]   12 s of per-frame top markers from the first arrival frame (no teleport, no
    //       input, no UI tidy). Default tier=saved: the level the settings service applied stays (a quality switch inside the
    //       capture would add its own hitch); vSync 0 / frame cap are still forced.
    //       python Tools/Unity/perf307.py arrival
    //   ab:<toggle>[|tier=…][|mode=…]  per pose A 5 s → toggle → 1.5 s → B 5 s → back to A, two laps; toggle = the finish297 keys
    //       feature297:<RendererFeature>:<0|1> or urp:shadow=<m>[,cascades=<n>][,ssao=<0|1>] — applied in memory to the live assets
    //       only (never saved) and restored before Play ends (forwardplus is refused: it needs a renderer rebuild)
    //       python Tools/Unity/perf307.py ab "feature297:SSAO297:0" tier=pc
    //   status | abort
    //   settings:frametiming-on        PlayerSettings.enableFrameTimingStats = true and save (user decision 2026-09-30: always on);
    //                                  reports the prior value. Edit mode only.
    // Guards: the tier override (quality level, vSync 0, frame cap) is applied after arrival and re-checked every frame by the runner;
    // the settings service is never asked to save. On exit the Editor quality level, every level's vSyncCount and targetFrameRate are
    // put back, user-settings-v1.json and ProjectSettings/QualitySettings.asset are hashed before/after (plus a git diff instruction),
    // isolated saves are copied into the run folder and removed. Keep the Unity window focused: < 90 % focused frames = INVALID.
    [InitializeOnLoad]
    public static partial class Perf307
    {
        [Serializable] sealed class State
        {
            public bool Active; public string Status = "", Command = "", Tier = "pc", Variant = "full", Mode = "uncapped", Toggle = "", Suffix = "", RunId = "", Output = "";
            public string PriorUi = "", PriorStartup = ""; public bool PriorDirect, PriorDirectPresent;
            public int PriorQuality = -1, PriorTargetFrameRate = -1; public int[] PriorVsync = Array.Empty<int>(); public string PriorQualityName = "";
            public string UserSettingsBefore = "", UserSettingsAfter = "", QualityBefore = "", QualityAfter = "", PipelineAssetsBefore = "", PipelineAssetsAfter = "";
            public double Began, At, Deadline; public int Phase; public string RunnerResult = "", RunnerProgress = "";
            public List<string> Checks = new List<string>(), Log = new List<string>();
        }

        const string StateKey = "Perf307.State", Lobby = "Assets/_Project/Art/UI/Loading270/W_Compact_Lobby.unity", Main = "W_Demo_Main", MainSlot = "world-main";
        const string Renderer297 = "Assets/_Project/Art/World/Finish297/Renderer297.asset";
        static readonly string[] PipelineAssets = { "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/Mobile_RPAsset.asset", Renderer297 };
        static State state;
        static PerfRoute307 runner;      // Play-only object; found again by type if the reference is lost
        static Toggle307 toggle;         // ab toggle (live assets, restored on exit)

        static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string PerfFolder => Path.Combine(RepoRoot, "Art", "Performance", "Perf307");
        static string RoutesPath => Path.Combine(PerfFolder, "routes.json");
        static string ProjectPath(string relative) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));
        static string UserSettingsPath => Path.Combine(Application.persistentDataPath, UserSettingsService.SettingsFileName);

        static Perf307()
        {
            var json = SessionState.GetString(StateKey, ""); if (!string.IsNullOrEmpty(json)) { try { state = JsonUtility.FromJson<State>(json); } catch { state = null; } }
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged -= Changed; EditorApplication.playModeStateChanged += Changed;
        }

        static void Persist()
        {
            SessionState.SetString(StateKey, JsonUtility.ToJson(state));
            if (state != null && !string.IsNullOrEmpty(state.Output)) { try { Directory.CreateDirectory(state.Output); File.WriteAllText(Path.Combine(state.Output, "editor-state.json"), JsonUtility.ToJson(state, true)); } catch { } }
        }
        static void Check(bool ok, string detail) => state.Checks.Add((ok ? "PASS " : "FAIL ") + detail);
        static void Info(string detail) => state.Checks.Add("INFO " + detail);

        static string Summary()
        {
            if (state == null) return "no Perf307 run in this Editor session";
            string live = runner != null ? " | " + runner.Progress : state.RunnerProgress.Length > 0 ? " | " + state.RunnerProgress : "";
            return state.Status + " " + state.Command + " tier=" + state.Tier + " variant=" + state.Variant + " mode=" + state.Mode + (state.Toggle.Length > 0 ? " toggle=" + state.Toggle : "") +
                   " phase=" + state.Phase + live + " | output " + state.Output + (state.RunnerResult.Length > 0 ? " | runner: " + state.RunnerResult : "") +
                   (state.Checks.Count > 0 ? " | checks: " + string.Join(" | ", state.Checks) : "");
        }

        public static string Run(string command)
        {
            command = (command ?? "").Trim();
            if (command == "status") return Summary();
            if (command == "abort") return Abort();
            if (command == "settings:frametiming-on") return FrameTimingOn();
            int colon = command.IndexOf(':'); string verb = colon < 0 ? command : command.Substring(0, colon), rest = colon < 0 ? "" : command.Substring(colon + 1);
            if (verb != "baseline" && verb != "arrival" && verb != "ab") return "refused: unknown command '" + command + "' (baseline[:tier=pc|mobile][:variant=full|noart][:mode=uncapped|cap120] | arrival | ab:<toggle>[|k=v] | status | abort | settings:frametiming-on)";
            string toggleSpec = "";
            string[] parts;
            if (verb == "ab")
            {
                var split = rest.Split('|'); toggleSpec = split[0].Trim(); parts = split.Skip(1).ToArray();
                if (toggleSpec.Length == 0) return "refused: ab needs a toggle, e.g. ab:feature297:SSAO297:0 or ab:urp:shadow=100";
            }
            else parts = rest.Split(new[] { ':', '|' }, StringSplitOptions.RemoveEmptyEntries);
            string tier = verb == "arrival" ? "saved" : "pc", variant = "full", mode = "uncapped";
            foreach (var p in parts)
            {
                var kv = p.Split('='); if (kv.Length != 2) return "refused: option '" + p + "' is not key=value";
                string k = kv[0].Trim(), v = kv[1].Trim();
                if (k == "tier" && (v == "pc" || v == "mobile" || v == "saved")) tier = v;
                else if (k == "variant" && (v == "full" || v == "noart")) variant = v;
                else if (k == "mode" && (v == "uncapped" || v == "cap120")) mode = v;
                else return "refused: option '" + p + "' (tier=pc|mobile|saved, variant=full|noart, mode=uncapped|cap120)";
            }
            return Start(verb, tier, variant, mode, toggleSpec);
        }

        static string Busy()
        {
            if (state != null && state.Active) return "refused: a Perf307 run is active (" + state.Status + "); status / abort";
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode required (Play is running)";
            if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) return "refused: scripts are compiling or failed to compile";
            var main = SessionState.GetString("Finish297.MainStart", "");
            if (main.Contains("\"Active\":true")) return "refused: finish297 main-start is active";
            if (SessionState.GetBool("Architecture296.Restore", false)) return "refused: an architecture296 perf probe still needs cleanup";
            if (VirtualInput303.Active) return "refused: VirtualInput303 is active (another harness)";
            return null;
        }

        static string Start(string verb, string tier, string variant, string mode, string toggleSpec)
        {
            string busy = Busy(); if (busy != null) return busy;
            for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) return "refused: save the open scene(s) first (" + SceneManager.GetSceneAt(i).path + ")";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Lobby) == null) return "refused: lobby scene missing " + Lobby;
            if (!File.Exists(RoutesPath)) return "refused: routes missing " + RoutesPath;
            PerfRoutes307 routes;
            try { routes = JsonUtility.FromJson<PerfRoutes307>(File.ReadAllText(RoutesPath)); }
            catch (Exception e) { return "refused: routes.json unreadable: " + e.Message; }
            if (routes == null || routes.stations == null || routes.stations.Length == 0) return "refused: routes.json has no stations";
            var t = tier == "saved" ? new PerfTier307 { key = "saved", quality = QualitySettings.names.FirstOrDefault() ?? "" } : routes.Tier(tier);
            if (t == null) return "refused: tier " + tier + " missing in routes.json";
            if (tier != "saved" && Array.IndexOf(QualitySettings.names, t.quality) < 0) return "refused: quality level '" + t.quality + "' not in QualitySettings (" + string.Join(",", QualitySettings.names) + ")";
            toggle = null;
            if (verb == "ab") { toggle = Toggle307.Parse(toggleSpec, out string why); if (toggle == null) return "refused: " + why; }
            string utc = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
            state = new State
            {
                Active = true, Status = "Running", Command = verb, Tier = tier, Variant = variant, Mode = mode, Toggle = toggleSpec, RunId = utc,
                Suffix = "_perf307_" + utc.TrimEnd('Z'), Output = Path.Combine(PerfFolder, utc),
                Began = EditorApplication.timeSinceStartup, At = EditorApplication.timeSinceStartup,
                PriorUi = SessionState.GetString("PlaytestUiReviewSuffix", "__missing__"), PriorStartup = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
                PriorDirect = SessionState.GetBool("Compact270.DirectPlay", false),
                PriorDirectPresent = SessionState.GetBool("Compact270.DirectPlay", false) == SessionState.GetBool("Compact270.DirectPlay", true),
                PriorQuality = QualitySettings.GetQualityLevel(), PriorTargetFrameRate = Application.targetFrameRate,
                UserSettingsBefore = ShaOrAbsent(UserSettingsPath), QualityBefore = ShaOrAbsent(ProjectPath("ProjectSettings/QualitySettings.asset")),
                PipelineAssetsBefore = string.Join(";", PipelineAssets.Select(p => Path.GetFileName(p) + "=" + ShaOrAbsent(ProjectPath(p))))
            };
            if (Directory.Exists(state.Output)) { state.Active = false; return "refused: output folder already exists " + state.Output; }
            state.PriorQualityName = state.PriorQuality >= 0 && state.PriorQuality < QualitySettings.names.Length ? QualitySettings.names[state.PriorQuality] : "?";
            state.PriorVsync = SnapshotVsync(state.PriorQuality);
            Directory.CreateDirectory(state.Output);
            File.WriteAllText(Path.Combine(state.Output, "run.txt"), "Perf307 " + verb + " tier=" + tier + " variant=" + variant + " mode=" + mode + (toggleSpec.Length > 0 ? " toggle=" + toggleSpec : "") +
                " — Editor Play from the lobby, isolated slot " + MainSlot + state.Suffix + ", started " + DateTime.UtcNow.ToString("s", CultureInfo.InvariantCulture) + "Z");
            SessionState.SetString("PlaytestUiReviewSuffix", state.Suffix); SessionState.SetBool("Compact270.DirectPlay", false);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Lobby);
            Info("editor quality before Play = " + state.PriorQualityName + " (index " + state.PriorQuality + "), per-level vSync [" + string.Join(",", state.PriorVsync) + "], targetFrameRate " + state.PriorTargetFrameRate);
            Info("user-settings-v1.json before = " + state.UserSettingsBefore + "; QualitySettings.asset before = " + state.QualityBefore);
            Persist();
            Harness303.FocusGameView();   // once at start; never forced back afterwards (298 focus discipline)
            EditorApplication.isPlaying = true;
            return "started Perf307 " + verb + " (tier=" + tier + " variant=" + variant + " mode=" + mode + (toggleSpec.Length > 0 ? " toggle=" + toggleSpec : "") + "); output " + state.Output + " — keep Unity focused; poll with status";
        }

        // Every quality level's vSyncCount (SetQualityLevel without expensive changes), then the prior level again.
        static int[] SnapshotVsync(int prior)
        {
            var values = new int[QualitySettings.names.Length];
            for (int i = 0; i < values.Length; i++) { QualitySettings.SetQualityLevel(i, false); values[i] = QualitySettings.vSyncCount; }
            if (prior >= 0) QualitySettings.SetQualityLevel(prior, false);
            return values;
        }

        static string Abort()
        {
            if (state == null || !state.Active) return "nothing to abort";
            var live = runner != null ? runner : Object.FindFirstObjectByType<PerfRoute307>();
            if (live != null && EditorApplication.isPlaying) { live.Abort("abort command"); }
            state.Status = "Stopping"; state.Log.Add("abort requested"); Persist();
            if (EditorApplication.isPlaying) { EditorApplication.isPlaying = false; return "aborting: stopping Play (cleanup runs on Edit mode)"; }
            Restore(); return "aborted in Edit mode: " + Summary();
        }

        static string FrameTimingOn()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode required";
            if (state != null && state.Active) return "refused: a Perf307 run is active (" + state.Status + ")";
            bool prior = PlayerSettings.enableFrameTimingStats;
            PlayerSettings.enableFrameTimingStats = true;
            // save only the PlayerSettings object (ProjectSettings.asset), never AssetDatabase.SaveAssets(): another session may hold
            // unrelated dirty assets in this shared Editor (including protected ones)
            var player = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault(o => o is PlayerSettings);
            string saved;
            if (player == null) saved = "PlayerSettings object not found: not saved (File > Save Project writes it)";
            else { EditorUtility.SetDirty(player); AssetDatabase.SaveAssetIfDirty(player); saved = "saved ProjectSettings.asset only"; }
            string file = ProjectPath("ProjectSettings/ProjectSettings.asset");
            string line = File.Exists(file) ? File.ReadAllLines(file).FirstOrDefault(l => l.Contains("enableFrameTimingStats")) ?? "(line not found)" : "(file missing)";
            bool onDisk = line.Trim() == "enableFrameTimingStats: 1";
            return "PlayerSettings.enableFrameTimingStats prior=" + prior + " now=" + PlayerSettings.enableFrameTimingStats + "; " + saved + "; on disk: " + line.Trim() +
                   (onDisk ? "" : " (NOT on disk yet: SaveAssetIfDirty did not write it; use File > Save Project after checking nothing else is dirty)") +
                   " — review with: git -C \"" + RepoRoot + "\" diff -- Oheangbu/ProjectSettings/ProjectSettings.asset";
        }

        static void Tick()
        {
            if (state == null || !state.Active) return;
            // Play exit hook can be missed; isPlaying stays true until Play has really ended (the runner writes on OnDestroy)
            if (state.Status == "Stopping" && !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode) { Restore(); return; }
            if (state.Status != "Running" || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            double now = EditorApplication.timeSinceStartup;
            try
            {
                var ui = PlaytestUiRoot.Instance;
                if (state.Phase == 0)
                {
                    if (now - state.Began > 180) throw new TimeoutException("lobby title did not appear within 180 s");
                    if (ui == null || !ui.IsTitle || ui.Busy || now - state.At < 2) return;
                    if (ui.ActiveSlotName != MainSlot + state.Suffix) throw new Exception("lobby slot is " + ui.ActiveSlotName + ", expected the isolated " + MainSlot + state.Suffix);
                    typeof(PlaytestUiRoot).GetMethod("StartNew", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui, null);
                    state.Log.Add("lobby StartNew at " + (now - state.Began).ToString("F1", CultureInfo.InvariantCulture) + " s");
                    state.Phase = 1; state.At = now; Persist(); return;
                }
                if (state.Phase == 1)
                {
                    if (now - state.At > 300) throw new TimeoutException("main scene did not finish loading within 300 s");
                    var scene = SceneManager.GetSceneByName(Main);
                    var s = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
                    if (!scene.isLoaded || s == null || !s.InitializationComplete || ui == null || ui.LoadingInProgress) return;
                    if (s.TestSaveSuffix != state.Suffix) throw new Exception("session save suffix '" + s.TestSaveSuffix + "' is not the isolated " + state.Suffix);
                    Info("arrived in " + s.gameObject.scene.name + " " + (now - state.At).ToString("F1", CultureInfo.InvariantCulture) + " s after StartNew; slot " + s.Content.SaveSlot + s.TestSaveSuffix);
                    if (state.Command != "arrival") { state.Log.Add("ui tidy: CloseMenu " + UiAdapter303.CloseMenu() + ", ReleaseGate " + UiAdapter303.ReleaseGate()); }
                    string why = BeginRunner(s);
                    if (why != null) throw new Exception(why);
                    state.Phase = 2; state.At = now; state.Deadline = now + runner.EstimatedSeconds + 240; Persist(); return;
                }
                if (state.Phase == 2)
                {
                    if (runner == null) runner = Object.FindFirstObjectByType<PerfRoute307>();
                    if (runner == null) throw new Exception("PerfRoute307 disappeared during the run");
                    // no per-frame bookkeeping here (EditorApplication.update runs inside the measured frames): status reads
                    // runner.Progress live, the last progress line is stored once at the end
                    if (!runner.Done)
                    {
                        if (now > state.Deadline) { runner.Abort("Editor deadline " + (state.Deadline - state.At).ToString("F0", CultureInfo.InvariantCulture) + " s"); }
                        return;
                    }
                    state.RunnerProgress = runner.Progress; state.RunnerResult = runner.Result; state.Status = "Stopping"; state.Phase = 3; Persist();
                    EditorApplication.isPlaying = false;
                }
            }
            catch (Exception e)
            {
                Check(false, "perf307: " + e.Message);
                try { if (runner != null && !runner.Done) runner.Abort(e.Message); } catch { }
                state.Status = "Stopping"; Persist(); EditorApplication.isPlaying = false;
            }
        }

        static string BeginRunner(WorldMacroPlaytestSession s)
        {
            var routes = JsonUtility.FromJson<PerfRoutes307>(File.ReadAllText(RoutesPath));
            var go = new GameObject("Perf307_Route"); SceneManager.MoveGameObjectToScene(go, s.gameObject.scene);
            runner = go.AddComponent<PerfRoute307>();
            runner.FocusProbe = () => InternalEditorUtility.isApplicationActive;
            if (state.Command != "arrival") runner.VirtualInput = new EditorInput307(s);
            if (state.Command == "ab") runner.Variant = toggle.Set;
            var o = new PerfOptions307 { Command = state.Command, Tier = state.Tier, Variant = state.Variant, Mode = state.Mode, Toggle = state.Toggle, Output = state.Output, RunId = state.RunId };
            o.Meta.Add("editor.codeOptimization=" + UnityEditor.Compilation.CompilationPipeline.codeOptimization);
            o.Meta.Add("editor.sceneViewsOpen=" + SceneView.sceneViews.Count);
            o.Meta.Add("editor.gameView=" + Screen.width + "x" + Screen.height);
            o.Meta.Add("editor.priorQuality=" + state.PriorQualityName);
            o.Meta.Add("editor.userSettingsSha.before=" + state.UserSettingsBefore);
            o.Meta.Add("player.enableFrameTimingStats=" + PlayerSettings.enableFrameTimingStats);
            o.Meta.Add("save.slot=" + s.Content.SaveSlot + s.TestSaveSuffix);
            o.Meta.Add("routes.version=" + routes.version);
            string why = runner.Begin(s, routes, o);
            if (why != null) { Object.Destroy(go); runner = null; }
            return why;
        }

        static void Changed(PlayModeStateChange mode)
        {
            if (state == null || !state.Active || mode != PlayModeStateChange.EnteredEditMode) return;
            Restore();
        }

        static void Restore()
        {
            if (state == null || !state.Active) return;
            try
            {
                if (runner != null && runner.Done && string.IsNullOrEmpty(state.RunnerResult)) state.RunnerResult = runner.Result;
                runner = null;
                if (VirtualInput303.Active) { state.Log.Add("virtual input still active on exit: " + VirtualInput303.End(out _)); }
                if (toggle != null) { Check(toggle.Restore(out string detail), "ab toggle restored in memory: " + detail); toggle = null; }
                if (state.PriorUi == "__missing__") SessionState.EraseString("PlaytestUiReviewSuffix"); else SessionState.SetString("PlaytestUiReviewSuffix", state.PriorUi);
                if (state.PriorDirectPresent) SessionState.SetBool("Compact270.DirectPlay", state.PriorDirect); else SessionState.EraseBool("Compact270.DirectPlay");
                EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(state.PriorStartup) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(state.PriorStartup);
                // quality: every level's vSync, then the prior level and frame cap
                if (state.PriorVsync != null && state.PriorVsync.Length == QualitySettings.names.Length)
                    for (int i = 0; i < state.PriorVsync.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.vSyncCount = state.PriorVsync[i]; }
                if (state.PriorQuality >= 0) QualitySettings.SetQualityLevel(state.PriorQuality, true);
                Application.targetFrameRate = state.PriorTargetFrameRate;
                var vsyncNow = SnapshotVsync(state.PriorQuality);
                Check(QualitySettings.GetQualityLevel() == state.PriorQuality && vsyncNow.SequenceEqual(state.PriorVsync ?? Array.Empty<int>()),
                      "Editor quality restored to " + state.PriorQualityName + " (index " + QualitySettings.GetQualityLevel() + "), vSync [" + string.Join(",", vsyncNow) + "]");
                // isolated saves: copy into the run folder, then remove (only files carrying this run's suffix)
                int moved = 0;
                if (Directory.Exists(Application.persistentDataPath) && !string.IsNullOrEmpty(state.Suffix))
                {
                    string keep = Path.Combine(state.Output, "isolated-saves");
                    foreach (var f in Directory.GetFiles(Application.persistentDataPath, "*" + state.Suffix + "*", SearchOption.TopDirectoryOnly))
                    { Directory.CreateDirectory(keep); File.Copy(f, Path.Combine(keep, Path.GetFileName(f)), true); File.Delete(f); moved++; }
                }
                Info("isolated saves copied to the run folder and removed: " + moved);
                state.UserSettingsAfter = ShaOrAbsent(UserSettingsPath);
                Check(state.UserSettingsAfter == state.UserSettingsBefore, "user-settings-v1.json unchanged (" + state.UserSettingsBefore + " -> " + state.UserSettingsAfter + ")");
                state.QualityAfter = ShaOrAbsent(ProjectPath("ProjectSettings/QualitySettings.asset"));
                Check(state.QualityAfter == state.QualityBefore, "ProjectSettings/QualitySettings.asset on disk unchanged; also run: git -C \"" + RepoRoot + "\" diff --exit-code -- Oheangbu/ProjectSettings/QualitySettings.asset" +
                      " (in-memory quality is restored; do not Save Project while a run is active)");
                state.PipelineAssetsAfter = string.Join(";", PipelineAssets.Select(p => Path.GetFileName(p) + "=" + ShaOrAbsent(ProjectPath(p))));
                Check(state.PipelineAssetsAfter == state.PipelineAssetsBefore, "PC/Mobile RP assets and Renderer297 unchanged on disk");
                string summary = Path.Combine(state.Output, "summary.json");
                Check(File.Exists(summary), "runner wrote " + summary);
                if (string.IsNullOrEmpty(state.RunnerResult) && File.Exists(summary))
                { try { state.RunnerResult = JsonUtility.FromJson<PerfRoute307.Summary307>(File.ReadAllText(summary)).status ?? ""; } catch (Exception e) { state.Log.Add("summary unreadable: " + e.Message); } }
                bool runnerPass = state.RunnerResult.StartsWith("PASS", StringComparison.Ordinal);
                bool invalid = state.RunnerResult.StartsWith("INVALID", StringComparison.Ordinal);
                bool checksPass = !state.Checks.Any(c => c.StartsWith("FAIL", StringComparison.Ordinal));
                state.Status = !checksPass ? "FAIL" : runnerPass ? "PASS" : invalid ? "INVALID" : "FAIL";
                if (string.IsNullOrEmpty(state.RunnerResult)) state.RunnerResult = "(runner produced no result)";
                File.WriteAllText(Path.Combine(state.Output, "editor-checks.json"), JsonUtility.ToJson(state, true));
            }
            catch (Exception e) { state.Checks.Add("FAIL restore: " + e); state.Status = "FAIL"; }
            state.Active = false; Persist();
        }

        static string ShaOrAbsent(string path)
        {
            if (!File.Exists(path)) return "absent";
            using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").Substring(0, 16);
        }

        /// <summary>VirtualInput303 adapter (real Input System path, scoped action assets, IgnoreFocus; restored by End).</summary>
        sealed class EditorInput307 : IPerfInput307
        {
            readonly WorldMacroPlaytestSession s; InputFrame303 walk, idle;
            public EditorInput307(WorldMacroPlaytestSession session) { s = session; }
            public string Begin()
            {
                var assets = new List<InputActionAsset> { Harness303.Field<InputActionAsset>(s.Walker.Motor, "_actions"), Harness303.Field<InputActionAsset>(s.Walker.Drawing, "_actions") };
                string r = VirtualInput303.Begin(assets);
                idle = InputFrame303.Neutral(new Vector2(Screen.width * .5f, Screen.height * .5f)); walk = idle.WithKeys(Key.W);
                VirtualInput303.Hold = idle; return r;
            }
            public void Hold(bool w) { VirtualInput303.Hold = w ? walk : idle; }
            public string End(out bool ok) => VirtualInput303.End(out ok);
        }
    }
}
