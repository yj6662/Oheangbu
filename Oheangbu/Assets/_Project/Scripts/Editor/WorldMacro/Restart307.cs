using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #307 user report (2026-10-01): "타이틀로 돌아갔다가 다시 새 게임 시작해도 이어하기로 들어가고, 새게임 시작 세번정도 반복하니까
    // 성능이 gpu 11fps까지 떨어지더라". Repro/validation harness, Play from the lobby like the player with an isolated save suffix
    // (the Finish297MainStart pattern; real saves are never opened, the isolated files are deleted on exit):
    //   lobby -> 새 게임 (StartNew) -> arrive -> measure -> walk the player away (teleport, so progress differs from a new journey)
    //   -> ReturnTitle (saves) -> 새 게임 again ... `cycles` times.
    // Per arrival: distance from Content.StartFeet (a new journey must start there), cameras / render textures / art / grass /
    // InteriorSight counts, DontDestroyOnLoad roots, RenderPipelineManager / sceneLoaded subscriber counts, GPU/CPU median over
    // 4 s at the arrival pose on the PC quality level. Output: Art/Performance/Perf307/Restart/<utc>.txt
    //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.Restart307 Run "start[:cycles=3]" | "status"
    public static class Restart307
    {
        [Serializable] sealed class State
        {
            public bool Active; public string Status = "", Suffix = "", PriorUi = "", PriorStartup = "", Report = ""; public bool PriorDirect, PriorDirectPresent;
            public double Began, At; public int Phase, Cycle, Cycles = 3, Exceptions, Errors;
            // userpath: the player's condition (UI suffix key empty) with a trap suffix on the open scene's session and a trap save
            // that resumes in the mine yard; the real slot is never used (the content asset's SaveSlot is renamed in memory, not saved)
            public bool UserPath; public string PriorSlot = "", PriorSessionSuffix = "", TrapPath = ""; public long TrapSize; public string TrapStamp = "";
            public List<string> Lines = new List<string>(), Log = new List<string>();
        }
        const string Key = "Restart307.State", Lobby = "Assets/_Project/Art/UI/Loading270/W_Compact_Lobby.unity", Main = "W_Demo_Main";
        const string ContentPath = "Assets/_Project/Scenes/World/Main/WorldContent_Main.asset", Trap = "_trap307";   // not a guarded harness prefix
        static Oheangbu.Data.World.WorldMacroPlaytestSO MainContent => AssetDatabase.LoadAssetAtPath<Oheangbu.Data.World.WorldMacroPlaytestSO>(ContentPath);
        static readonly Vector2 AwayXZ = new Vector2(3368.6f, 2009f);   // the mine yard, ~170 m from the new-journey start
        static State state;
        static readonly List<float> gpu = new List<float>(), cpu = new List<float>();
        static readonly FrameTiming[] timing = new FrameTiming[1];
        static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Performance/Perf307/Restart"));

        static Restart307()
        {
            var json = SessionState.GetString(Key, ""); if (!string.IsNullOrEmpty(json)) state = JsonUtility.FromJson<State>(json);
            EditorApplication.update += Tick; EditorApplication.update += MeasureTick; EditorApplication.playModeStateChanged += Changed; Application.logMessageReceived += OnLog;
            // #307: Play exit restores the open scene from its pre-Play backup AFTER a harness cleaned up, which brings the harness save
            // suffix back onto the edit-mode session (seen 2026-10-01: a player's lobby run then saved into the harness slot). Clear any
            // stale harness suffix 1 s and 4 s after entering Edit mode (the object is not dirtied; the runtime guard covers the rest).
            EditorApplication.playModeStateChanged += mode => { if (mode == PlayModeStateChange.EnteredEditMode) { staleChecks[0] = EditorApplication.timeSinceStartup + 1; staleChecks[1] = EditorApplication.timeSinceStartup + 4; } };
            EditorApplication.update += StaleSweep;
            // after a recompile / domain reload too (the scheduled Play-exit checks do not survive it)
            staleChecks[0] = EditorApplication.timeSinceStartup + 2;
        }
        static readonly double[] staleChecks = new double[2];
        static double trapCleanupAt;
        static void StaleSweep()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (trapCleanupAt > 0 && EditorApplication.timeSinceStartup > trapCleanupAt)
            {
                trapCleanupAt = 0;
                foreach (var s in Object.FindObjectsByType<WorldMacroPlaytestSession>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (s.TestSaveSuffix == Trap) { bool d = EditorUtility.IsDirty(s); s.TestSaveSuffix = ""; if (!d) EditorUtility.ClearDirty(s); Debug.Log("[Restart307] trap suffix cleared after the scene reload"); }
            }
            for (int k = 0; k < staleChecks.Length; k++)
            {
                if (staleChecks[k] <= 0 || EditorApplication.timeSinceStartup < staleChecks[k]) continue;
                staleChecks[k] = 0;
                foreach (var s in Object.FindObjectsByType<WorldMacroPlaytestSession>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (WorldMacroPlaytestSession.StaleHarnessSuffix307(s.TestSaveSuffix))
                    { bool wasDirty = EditorUtility.IsDirty(s); Debug.Log("[Restart307] cleared stale harness save suffix '" + s.TestSaveSuffix + "' on " + s.gameObject.scene.name); s.TestSaveSuffix = ""; if (!wasDirty) EditorUtility.ClearDirty(s); }
            }
        }

        static bool measuring; static int measureIndex, measurePhase; static double measureStart, measureAt0;
        static Vector3 measureAt; static float measureYaw; static List<string> measureVariants = new List<string>(); static readonly List<string> measureLines = new List<string>();
        static void MeasureTick()
        {
            if (!measuring) return;
            var s = Map307Capture.HeldSession;
            if (s == null || !EditorApplication.isPlaying) { measureLines.Add("FAIL held Play ended"); measuring = false; return; }
            double now = EditorApplication.timeSinceStartup;
            if (now - measureStart > 60 + 12 * measureVariants.Count) { measureLines.Add("FAIL timeout"); measuring = false; return; }
            switch (measurePhase)
            {
                case 0:
                    if (Physics.Raycast(new Vector3(measureAt.x, 2000f, measureAt.z), Vector3.down, out var hit, 4000f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        var spot = hit.point; if (UnityEngine.AI.NavMesh.SamplePosition(spot, out var nav, 30f, UnityEngine.AI.NavMesh.AllAreas)) spot = nav.position;
                        s.Teleport(spot, measureYaw);
                    }
                    string v = measureVariants[measureIndex]; if (v != "A") PerfSweep307.Run("apply:" + v);
                    measurePhase = 1; measureAt0 = now; return;
                case 1:
                    if (now - measureAt0 < 2) return;
                    gpu.Clear(); cpu.Clear(); measurePhase = 2; measureAt0 = now; return;
                case 2:
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, timing) > 0) { if (timing[0].gpuFrameTime > 0) gpu.Add((float)timing[0].gpuFrameTime); if (timing[0].cpuMainThreadFrameTime > 0) cpu.Add((float)timing[0].cpuMainThreadFrameTime); }
                    if (now - measureAt0 < 3) return;
                    var cam = s.Walker.ViewCamera; var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                    int points = 0, spots = 0;
                    foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    {
                        if (!l.isActiveAndEnabled || l.shadows == LightShadows.None || (l.type != LightType.Point && l.type != LightType.Spot)) continue;
                        if (!GeometryUtility.TestPlanesAABB(planes, new Bounds(l.transform.position, Vector3.one * l.range * 2))) continue;
                        if (l.type == LightType.Point) points++; else spots++;
                    }
                    measureLines.Add(measureVariants[measureIndex] + ": gpu " + Median(gpu).ToString("F2") + " ms, cpu " + Median(cpu).ToString("F2") + " ms (n " + gpu.Count + ") | shadowed lights touching the view: point " + points + " spot " + spots + " (~" + (points * 6 + spots) + " maps) | quality " + QualitySettings.names[QualitySettings.GetQualityLevel()] + " " + Screen.width + "x" + Screen.height);
                    if (measureVariants[measureIndex] != "A") PerfSweep307.Run("revert");
                    measureIndex++; measurePhase = 0;
                    if (measureIndex >= measureVariants.Count) measuring = false;
                    return;
            }
        }
        static void Persist() => SessionState.SetString(Key, JsonUtility.ToJson(state));
        static string Summary() => state == null ? "no restart307 run" : state.Status + " phase=" + state.Phase + " cycle=" + state.Cycle + "/" + state.Cycles + " exceptions=" + state.Exceptions + " errors=" + state.Errors + (state.Report.Length > 0 ? " report " + state.Report : "") + "\n" + string.Join("\n", state.Lines);

        public static string Run(string command)
        {
            command = (command ?? "").Trim();
            if (command == "status") return Summary();
            if (command == "abort")
            {
                if (state?.Active != true) return "nothing to abort";
                if (EditorApplication.isPlaying) { state.Lines.Add("FAIL aborted"); state.Status = "Stopping"; Persist(); EditorApplication.isPlaying = false; return "stopping Play; cleanup runs on Edit mode"; }
                state.Lines.Add("FAIL aborted before Play"); Changed(PlayModeStateChange.EnteredEditMode); return "aborted: " + Summary();
            }
            // stale[:clear] — the open scenes' sessions: TestSaveSuffix, the UI suffix key, and the Play-time verdict of
            // WorldMacroPlaytestSession.StaleHarnessSuffix307; :clear resets a stale harness suffix on the edit-mode object (not saved)
            if (command == "stale" || command == "stale:clear")
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode only";
                string ui = SessionState.GetString("PlaytestUiReviewSuffix", "");
                var sb = new StringBuilder("ui suffix key '" + ui + "'");
                foreach (var s in Object.FindObjectsByType<WorldMacroPlaytestSession>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    bool stale = WorldMacroPlaytestSession.StaleHarnessSuffix307(s.TestSaveSuffix);
                    sb.Append("\n").Append(s.gameObject.scene.name).Append(" session suffix '").Append(s.TestSaveSuffix).Append("' stale ").Append(stale).Append(" dirty ").Append(EditorUtility.IsDirty(s));
                    if (stale && command == "stale:clear") { bool wasDirty = EditorUtility.IsDirty(s); s.TestSaveSuffix = ""; if (!wasDirty) EditorUtility.ClearDirty(s); sb.Append(" -> cleared"); }
                }
                // the guard's rule on fixed inputs (SessionState untouched)
                sb.Append("\nrule: '_c303_x' ").Append(WorldMacroPlaytestSession.StaleHarnessSuffix307("_c303_x")).Append(", '' ").Append(WorldMacroPlaytestSession.StaleHarnessSuffix307(""))
                  .Append(", '_mum295_test' ").Append(WorldMacroPlaytestSession.StaleHarnessSuffix307("_mum295_test")).Append(", ui key itself ").Append(WorldMacroPlaytestSession.StaleHarnessSuffix307(ui));
                return sb.ToString();
            }
            // measure:x,z,yaw[|variant...] — held Play (Map307Capture play-hold): stand there, 2 s settle, 3 s GPU/CPU median, the count of
            // shadowed punctual lights in range of the camera, then the same for each PerfSweep307 variant (apply/revert)
            if (command.StartsWith("measure:", StringComparison.Ordinal))
            {
                if (Map307Capture.HeldSession == null) return "refused: no held Play";
                var parts = command.Substring(8).Split('|'); var p = parts[0].Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                measureAt = new Vector3(p[0], 0, p[1]); measureYaw = p.Length > 2 ? p[2] : 0; measureVariants = new List<string> { "A" }; measureVariants.AddRange(parts.Skip(1));
                measureIndex = 0; measurePhase = 0; measureStart = EditorApplication.timeSinceStartup; measureLines.Clear(); measuring = true;
                return "measuring " + measureVariants.Count + " variant(s) at " + command.Substring(8);
            }
            if (command == "measure-status") return (measuring ? "running " : "done ") + string.Join("\n", measureLines);
            if (!command.StartsWith("start", StringComparison.Ordinal)) return "refused: start[:cycles=N] | status | stale[:clear] | measure:x,z,yaw[|v..] | measure-status";
            if (state?.Active == true || EditorApplication.isPlayingOrWillChangePlaymode) return "refused: already running / in Play";
            if (SceneManager.GetActiveScene().isDirty) return "refused: save the open scene first";
            int cycles = 3; var m = System.Text.RegularExpressions.Regex.Match(command, @"cycles=(\d+)"); if (m.Success) cycles = Mathf.Clamp(int.Parse(m.Groups[1].Value), 1, 8);
            state = new State
            {
                Active = true, Status = "Running", Began = EditorApplication.timeSinceStartup, At = EditorApplication.timeSinceStartup, Cycles = cycles,
                Suffix = "_r307_" + DateTime.UtcNow.ToString("yyyyMMddTHHmmss", CultureInfo.InvariantCulture),
                PriorUi = SessionState.GetString("PlaytestUiReviewSuffix", "__missing__"), PriorStartup = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
                PriorDirect = SessionState.GetBool("Compact270.DirectPlay", false),
                PriorDirectPresent = SessionState.GetBool("Compact270.DirectPlay", false) == SessionState.GetBool("Compact270.DirectPlay", true),
            };
            SessionState.SetBool("Compact270.DirectPlay", false);
            if (command.Contains("userpath"))
            {
                var content = MainContent; var session = Object.FindObjectsByType<WorldMacroPlaytestSession>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(x => x.gameObject.scene.name == Main);
                if (content == null || session == null) { state.Active = false; Persist(); return "refused: userpath needs " + ContentPath + " and the open " + Main + " session"; }
                // an existing valid save is the template for the trap; it resumes in the mine yard, far from the new-journey start
                var template = Directory.GetFiles(Application.persistentDataPath, "world-main_c303_m307*.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                var progress = template != null ? new AtomicJsonStore<WorldMacroProgress>(template, WorldMacroProgress.Valid).Load() : null;
                if (progress == null) { state.Active = false; Persist(); return "refused: no valid template save for the trap"; }
                state.UserPath = true; state.Suffix = "_r307u_" + state.Suffix.Substring(6);
                PlaytestUiRoot.SlotInvariantOff307 = command.Contains("legacy");   // userpath:legacy = the pre-fix rule (expected FAIL)
                if (PlaytestUiRoot.SlotInvariantOff307) state.Lines.Add("LEGACY: the #307 slot invariant is OFF for this run (expected to FAIL)");
                state.PriorSlot = content.SaveSlot; state.PriorSessionSuffix = session.TestSaveSuffix;
                bool contentDirty = EditorUtility.IsDirty(content), sessionDirty = EditorUtility.IsDirty(session);
                content.SaveSlot = "world-main" + state.Suffix; if (!contentDirty) EditorUtility.ClearDirty(content);
                session.TestSaveSuffix = Trap; if (!sessionDirty) EditorUtility.ClearDirty(session);
                progress.ledger.position = new Vector3(AwayXZ.x, 171.4f, AwayXZ.y); progress.ledger.hasPosition = true;
                state.TrapPath = Path.Combine(Application.persistentDataPath, content.SaveSlot + Trap + ".json");
                new AtomicJsonStore<WorldMacroProgress>(state.TrapPath, WorldMacroProgress.Valid).Save(progress);
                state.TrapSize = new FileInfo(state.TrapPath).Length; state.TrapStamp = File.GetLastWriteTimeUtc(state.TrapPath).ToString("o");
                SessionState.EraseString("PlaytestUiReviewSuffix");   // the player's condition
                state.Lines.Add("userpath: slot " + content.SaveSlot + ", UI suffix key erased, session suffix trap '" + Trap + "', trap save " + state.TrapPath + " (resumes at the mine yard)");
            }
            else SessionState.SetString("PlaytestUiReviewSuffix", state.Suffix);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Lobby);
            Persist(); EditorApplication.isPlaying = true;
            return "restart307 started: " + cycles + " cycles, " + (state.UserPath ? "USER PATH (trap) slot world-main" : "isolated suffix ") + state.Suffix;
        }

        static void OnLog(string message, string stack, LogType type)
        {
            if (state?.Active != true || !EditorApplication.isPlaying) return;
            if (type == LogType.Exception) { state.Exceptions++; if (state.Log.Count < 40) state.Log.Add("EXC " + message); }
            else if (type == LogType.Error || type == LogType.Assert) { state.Errors++; if (state.Log.Count < 40) state.Log.Add("ERR " + message); }
        }

        static int Subscribers(Type type, string eventName)
        {
            foreach (var name in new[] { eventName, "m_" + eventName, "s_" + eventName, "<" + eventName + ">k__BackingField" })
            {
                var f = type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null && typeof(Delegate).IsAssignableFrom(f.FieldType)) { var d = f.GetValue(null) as Delegate; return d == null ? 0 : d.GetInvocationList().Length; }
            }
            return -1;
        }
        static string DontDestroyRoots()
        {
            var probe = new GameObject("Restart307_probe"); Object.DontDestroyOnLoad(probe);
            var roots = probe.scene.GetRootGameObjects().Where(g => g != probe).Select(g => g.name).ToArray();
            Object.DestroyImmediate(probe);
            return roots.Length + " [" + string.Join(", ", roots.GroupBy(n => n).Select(g => g.Count() > 1 ? g.Key + " x" + g.Count() : g.Key)) + "]";
        }
        static int Descendants(Transform t) { int n = 0; foreach (Transform c in t) n += 1 + Descendants(c); return n; }
        static float Median(List<float> v) { if (v.Count == 0) return float.NaN; var a = v.OrderBy(x => x).ToList(); return a[a.Count / 2]; }

        static string Snapshot(WorldMacroPlaytestSession s, string label)
        {
            var sb = new StringBuilder("[" + label + "]");
            var feet = s.Walker.Body.transform.position; var start = s.Content.StartFeet;
            float fromStart = Vector2.Distance(new Vector2(feet.x, feet.z), new Vector2(start.x, start.z));
            sb.Append(" feet ").Append(feet.ToString("F1")).Append(" fromStart ").Append(fromStart.ToString("F1")).Append(" m")
              .Append(" ledger.hasPosition ").Append(s.Progress?.ledger?.hasPosition).Append(" checkpoint '").Append(s.Progress?.ledger?.checkpoint).Append("'")
              .Append(" progressJson ").Append(s.Progress != null ? JsonUtility.ToJson(s.Progress).Length : -1);
            // #307 invariant: the session saves where the title inspected and archived (a mismatch = 새 게임 resumes another file)
            var uiRoot = PlaytestUiRoot.Instance; string sessionSlot = s.Content.SaveSlot + s.TestSaveSuffix, titleSlot = uiRoot != null ? uiRoot.ActiveSlotName : "?";
            sb.Append("\n  ").Append(sessionSlot == titleSlot ? "PASS" : "FAIL").Append(" slot session '").Append(sessionSlot).Append("' title '").Append(titleSlot).Append("'");
            sb.Append("\n  ").Append(fromStart < 2.5f ? "PASS" : "FAIL").Append(" new journey at the start (").Append(fromStart.ToString("F1")).Append(" m)");
            if (state != null && state.UserPath)
            {
                bool untouched = File.Exists(state.TrapPath) && new FileInfo(state.TrapPath).Length == state.TrapSize && File.GetLastWriteTimeUtc(state.TrapPath).ToString("o") == state.TrapStamp;
                sb.Append("\n  ").Append(untouched ? "PASS" : "FAIL").Append(" trap save untouched (the session never opened the stale slot)");
            }
            var cams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            sb.Append("\n  cameras enabled ").Append(Camera.allCamerasCount).Append(" / all ").Append(cams.Length)
              .Append(" [").Append(string.Join(", ", cams.Where(c => c.isActiveAndEnabled).Select(c => c.name + (c.targetTexture != null ? "->RT" : "")).GroupBy(n => n).Select(g => g.Count() > 1 ? g.Key + " x" + g.Count() : g.Key))).Append("]");
            var rts = Resources.FindObjectsOfTypeAll<RenderTexture>();
            long bytes = 0; foreach (var rt in rts) bytes += (long)rt.width * rt.height * 4 * Math.Max(1, rt.volumeDepth);
            sb.Append("\n  renderTextures ").Append(rts.Length).Append(" (~").Append((bytes / 1048576f).ToString("F0")).Append(" MB at 4 B/px)");
            sb.Append("\n  art ").Append(Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length)
              .Append(" grass ").Append(Object.FindObjectsByType<CompactGrassRenderer266>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length)
              .Append(" interiorSight ").Append(Object.FindObjectsByType<InteriorSight307>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length)
              .Append(" sessions ").Append(Object.FindObjectsByType<WorldMacroPlaytestSession>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length)
              .Append(" lights ").Append(Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Count(l => l.isActiveAndEnabled))
              .Append(" gameObjects ").Append(Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g.scene.IsValid()))
              .Append(" materials ").Append(Resources.FindObjectsOfTypeAll<Material>().Length)
              .Append(" meshes ").Append(Resources.FindObjectsOfTypeAll<Mesh>().Length);
            sb.Append("\n  scenes ").Append(string.Join(", ", Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i).name)));
            sb.Append("\n  dontDestroyRoots ").Append(DontDestroyRoots());
            var ui = PlaytestUiRoot.Instance; sb.Append(" uiDescendants ").Append(ui != null ? Descendants(ui.transform) : -1);
            sb.Append("\n  subscribers beginCameraRendering ").Append(Subscribers(typeof(RenderPipelineManager), "beginCameraRendering"))
              .Append(" endCameraRendering ").Append(Subscribers(typeof(RenderPipelineManager), "endCameraRendering"))
              .Append(" beginContextRendering ").Append(Subscribers(typeof(RenderPipelineManager), "beginContextRendering"))
              .Append(" sceneLoaded ").Append(Subscribers(typeof(SceneManager), "sceneLoaded"))
              .Append(" activeSceneChanged ").Append(Subscribers(typeof(SceneManager), "activeSceneChanged"))
              .Append(" onBeforeRender ").Append(Subscribers(typeof(Application), "onBeforeRender"));
            return sb.ToString();
        }

        static void Tick()
        {
            if (state?.Active != true || state.Status != "Running" || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            double now = EditorApplication.timeSinceStartup;
            try
            {
                if (now - state.Began > 240 * state.Cycles + 120) throw new TimeoutException("deadline (phase " + state.Phase + ")");
                var ui = PlaytestUiRoot.Instance;
                var s = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
                switch (state.Phase)
                {
                    case 0:   // title: press 새 게임 (StartNew archives an existing save first, exactly like the confirm's accept)
                        if (ui == null || !ui.IsTitle || ui.Busy || now - state.At < 2) return;
                        state.Lines.Add("cycle " + state.Cycle + ": title slot " + ui.ActiveSlotName + " save " + WorldMacroSaveSlot.Inspect(Application.persistentDataPath, ui.ActiveSlotName).Status);
                        typeof(PlaytestUiRoot).GetMethod("StartNew", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui, null);
                        state.Phase = 1; state.At = now; Persist(); return;
                    case 1:   // arrival
                        if (s == null || !s.InitializationComplete || ui == null || ui.LoadingInProgress || ui.Busy || s.gameObject.scene.name != Main) { if (now - state.At > 200) throw new Exception("main scene did not finish loading"); return; }
                        if (now - state.At < 3) return;
                        int pc = Array.IndexOf(QualitySettings.names, "PC"); if (pc >= 0 && QualitySettings.GetQualityLevel() != pc) QualitySettings.SetQualityLevel(pc, true);
                        gpu.Clear(); cpu.Clear(); state.Phase = 2; state.At = now; Persist(); return;
                    case 2:   // 4 s of frame timings at the arrival pose
                        FrameTimingManager.CaptureFrameTimings();
                        if (FrameTimingManager.GetLatestTimings(1, timing) > 0) { if (timing[0].gpuFrameTime > 0) gpu.Add((float)timing[0].gpuFrameTime); if (timing[0].cpuMainThreadFrameTime > 0) cpu.Add((float)timing[0].cpuMainThreadFrameTime); }
                        if (now - state.At < 4) return;
                        state.Lines.Add(Snapshot(s, "cycle " + state.Cycle + " arrival") + "\n  gpu median " + Median(gpu).ToString("F2") + " ms, cpu main " + Median(cpu).ToString("F2") + " ms (n " + gpu.Count + ", quality " + QualitySettings.names[QualitySettings.GetQualityLevel()] + ", " + Screen.width + "x" + Screen.height + ")");
                        if (state.Cycle + 1 >= state.Cycles) { state.Phase = 9; state.Status = "Stopping"; Persist(); EditorApplication.isPlaying = false; return; }
                        // make progress differ from a new journey: stand in the mine yard, then return to the title (ReturnTitle saves)
                        if (Physics.Raycast(new Vector3(AwayXZ.x, 2000f, AwayXZ.y), Vector3.down, out var hit, 4000f, ~0, QueryTriggerInteraction.Ignore))
                        {
                            var spot = hit.point;
                            if (UnityEngine.AI.NavMesh.SamplePosition(spot, out var nav, 30f, UnityEngine.AI.NavMesh.AllAreas)) spot = nav.position;
                            s.Teleport(spot, 0f);
                        }
                        state.Phase = 3; state.At = now; Persist(); return;
                    case 3:
                        if (now - state.At < 2.5) return;
                        var feet = s.Walker.Body.transform.position; state.Lines.Add("cycle " + state.Cycle + ": moved to " + feet.ToString("F1") + ", returning to the title");
                        var routine = typeof(PlaytestUiRoot).GetMethod("ReturnTitle", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui, new object[] { false }) as IEnumerator;
                        ui.StartCoroutine(routine);
                        state.Phase = 4; state.At = now; Persist(); return;
                    case 4:
                        if (ui == null || !ui.IsTitle || ui.Busy) { if (now - state.At > 120) throw new Exception("title did not load"); return; }
                        state.Cycle++; state.Phase = 0; state.At = now; Persist(); return;
                }
            }
            catch (Exception e) { state.Lines.Add("FAIL " + e.Message); state.Status = "Stopping"; Persist(); EditorApplication.isPlaying = false; }
        }

        static void Changed(PlayModeStateChange mode)
        {
            if (state?.Active != true || mode != PlayModeStateChange.EnteredEditMode) return;
            if (state.PriorUi == "__missing__") SessionState.EraseString("PlaytestUiReviewSuffix"); else SessionState.SetString("PlaytestUiReviewSuffix", state.PriorUi);
            if (state.PriorDirectPresent) SessionState.SetBool("Compact270.DirectPlay", state.PriorDirect); else SessionState.EraseBool("Compact270.DirectPlay");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(state.PriorStartup) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(state.PriorStartup);
            if (state.UserPath)
            {
                PlaytestUiRoot.SlotInvariantOff307 = false; var content = MainContent; if (content != null) { bool d = EditorUtility.IsDirty(content); content.SaveSlot = state.PriorSlot; if (!d) EditorUtility.ClearDirty(content); }
                foreach (var s in Object.FindObjectsByType<WorldMacroPlaytestSession>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (s.TestSaveSuffix == Trap) { bool d = EditorUtility.IsDirty(s); s.TestSaveSuffix = state.PriorSessionSuffix; if (!d) EditorUtility.ClearDirty(s); }
                // Play exit restores the scene backup later (with the trap suffix): clear it again after the reload
                EditorApplication.delayCall += () => EditorApplication.delayCall += () =>
                {
                    foreach (var s in Object.FindObjectsByType<WorldMacroPlaytestSession>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                        if (s.TestSaveSuffix == Trap) { bool d = EditorUtility.IsDirty(s); s.TestSaveSuffix = ""; if (!d) EditorUtility.ClearDirty(s); }
                };
                trapCleanupAt = EditorApplication.timeSinceStartup + 3;
            }
            int deleted = 0; foreach (var f in Directory.GetFiles(Application.persistentDataPath, "*" + state.Suffix + "*", SearchOption.TopDirectoryOnly)) { File.Delete(f); deleted++; }
            foreach (var dir in Directory.GetDirectories(Application.persistentDataPath, "*" + state.Suffix + "*", SearchOption.TopDirectoryOnly)) { Directory.Delete(dir, true); deleted++; }
            state.Lines.Add("isolated save files deleted " + deleted + "; exceptions " + state.Exceptions + " errors " + state.Errors + (state.Log.Count > 0 ? "\n  " + string.Join("\n  ", state.Log.Take(12)) : ""));
            Directory.CreateDirectory(Folder); state.Report = Path.Combine(Folder, DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + ".txt");
            File.WriteAllText(state.Report, "#307 Restart307 " + state.Cycles + " cycles, suffix " + state.Suffix + "\n" + string.Join("\n", state.Lines) + "\n", new UTF8Encoding(false));
            state.Active = false; state.Status = state.Lines.Any(l => l.StartsWith("FAIL", StringComparison.Ordinal) || l.Contains("\n  FAIL")) ? "FAIL" : "DONE"; Persist();
        }
    }
}
