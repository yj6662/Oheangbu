using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#308 Play-test probe (2026-10-13, PLAYTEST_308.md). Editor only, queue safe (no dialogs, no asset or scene writes).
    /// Everything except hold-from runs inside a Play that is already up on an ISOLATED store (a suffixed save): it refuses on the real
    /// slot. Reads: where | tpnav:x,y,z[,yaw] | ui | ui:close | route:<way>[,speed[,secs]] | route | gates | path | enemies | watch | owned | walkers | lockon | boss | frames | log. Play-only acts on the
    /// isolated session: walk (virtual keyboard through the real Input System path), near, kill, hit, strike, walkers:off|on,
    /// fixture:southgate (in-memory ledger fixture, the SpellSmoke308 unlock=fixture pattern), save, save-copy.
    /// hold-from:path (Edit Mode) = Map307Capture play-hold with the isolated store seeded from a COPY of the given save file.</summary>
    public static class PlayProbe308
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F(float v, string f = "F2") => v.ToString(f, Inv);
        static string V(Vector3 v) => "(" + F(v.x, "F1") + ", " + F(v.y, "F1") + ", " + F(v.z, "F1") + ")";
        static double Now => EditorApplication.timeSinceStartup;
        static string OutFolder => Path.Combine(Harness303.RepoRoot, "Tools", "Unity", "Stage308_ops", "logs", "play", "probe");

        // log hook (domain reload is off: statics live across Plays, reset by "log:reset")
        static bool logHooked; static int exceptions, errors; static readonly List<string> firstErrors = new List<string>();
        static void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Exception) exceptions++; else if (type == LogType.Error || type == LogType.Assert) errors++; else return;
            if (firstErrors.Count < 12) firstErrors.Add(type + ": " + (condition.Length > 220 ? condition.Substring(0, 220) : condition) + " @ " + (stack ?? "").Split('\n').FirstOrDefault());
        }
        static void HookLog() { if (logHooked) return; Application.logMessageReceived += OnLog; logHooked = true; }

        public static string Run(string command)
        {
            try { return Dispatch((command ?? "").Trim()); }
            catch (Exception e) { return "probe exception: " + e.GetType().Name + " " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n').FirstOrDefault(); }
        }

        static string Dispatch(string command)
        {
            HookLog();
            if (command == "" || command == "help") return "PlayProbe308: where | gates | path:x,z>x,z | enemies[:r] | watch:<id|near>:secs | watch | owned | walkers | walkers:sample:secs | walkers:off|on | lockon | boss | frames:secs | frames | log | log:reset | walk:secs:Keys[:TapKey@interval] | walk | near:<id>[,dist[,back|front]] | kill:<id> | hit:<id>,amount | strike:<id>:off|on | fixture:southgate | save | save-copy:name | hold-from:path";
            if (command == "log:reset") { exceptions = errors = 0; firstErrors.Clear(); return "log counters reset"; }
            if (command == "log") return "log exceptions " + exceptions + " errors " + errors + (firstErrors.Count > 0 ? "\n" + string.Join("\n", firstErrors) : "");
            if (command.StartsWith("hold-from:", StringComparison.Ordinal)) return HoldFrom(command.Substring(10).Trim());
            if (!EditorApplication.isPlaying) return "refused: Play only (hold first)";
            // frames only reads frame clocks, so it also runs without a harness session (spell lab, 2026-10-19 stage 5)
            if (command == "frames") return FramesReport();
            if (command.StartsWith("frames:", StringComparison.Ordinal)) return FramesStart(float.Parse(command.Substring(7), Inv));
            var s = Harness303.Session; if (s == null || !s.InitializationComplete || s.Walker == null || s.Walker.Body == null) return "refused: no ready session";
            if (string.IsNullOrEmpty(s.TestSaveSuffix) || !s.TestSaveSuffix.StartsWith("_", StringComparison.Ordinal)) return "refused: this session is on the real slot (no suffix)";
            WatchDeaths(s);
            if (command == "deaths") return "deaths " + deathLines.Count + (deathLines.Count > 0 ? "\n" + string.Join("\n", deathLines) : "");
            if (command == "deaths:reset") { deathLines.Clear(); return "deaths reset"; }
            string head = command.Split(':')[0], rest = command.Contains(":") ? command.Substring(command.IndexOf(':') + 1) : "";
            switch (head)
            {
                case "where": return Where(s);
                case "tpnav":
                {
                    var v = rest.Split(',').Select(x => float.Parse(x.Trim(), Inv)).ToArray(); var want = new Vector3(v[0], v[1], v[2]);
                    if (!NavMesh.SamplePosition(want, out var spot, 10f, NavMesh.AllAreas)) return "tpnav: no navmesh within 10 m of " + V(want);
                    s.Teleport(spot.position, v.Length > 3 ? v[3] : 0f); return "teleported to " + V(spot.position) + " (navmesh " + F(Vector3.Distance(spot.position, want), "F1") + " m from the asked point)";
                }
                case "route": return rest == "" ? RouteReport(s) : RouteStart(s, rest);
                case "ui": if (rest == "close") { UiAdapter303.CloseMenu(); return "ui close asked | page '" + UiAdapter303.Page + "'"; } return "ui page '" + UiAdapter303.Page + "'";
                case "gates": return Gates(s);
                case "path": return PathLine(rest);
                case "enemies": return Enemies(s, rest == "" ? 60f : float.Parse(rest, Inv));
                case "watch": return rest == "" ? WatchReport() : WatchStart(s, rest);
                case "owned": return Owned(s);
                case "walkers": return Walkers(s, rest);
                case "lockon": return LockOnProbe(s);
                case "boss": return Boss();
                case "frames": return rest == "" ? FramesReport() : FramesStart(float.Parse(rest, Inv));
                case "walk": return rest == "" ? WalkReport(s) : WalkStart(s, rest);
                case "near": return Near(s, rest);
                case "kill": return Hit(s, rest, float.MaxValue);
                case "hit": { var a = rest.Split(','); return Hit(s, a[0], float.Parse(a[1], Inv)); }
                case "strike": return Strike(s, rest);
                case "fixture": return rest == "southgate" ? FixtureSouthGate(s) : "refused: fixture:southgate";
                case "save": { bool ok = s.SaveNow(out string error); return "save " + (ok ? "ok" : "FAILED " + error) + " | store " + Path.GetFileName(Harness303.StorePath(s)); }
                case "save-copy": return SaveCopy(s, rest);
            }
            return "refused: unknown command " + command;
        }

        // player deaths (cause + place), recorded from the vitals' own event
        static PlayerVitals deathVitals; static readonly List<string> deathLines = new List<string>();
        static void WatchDeaths(WorldMacroPlaytestSession s)
        {
            var v = s.Walker.Body.GetComponent<PlayerVitals>(); if (v == null || v == deathVitals) return;
            deathVitals = v; v.Died += () => { if (deathLines.Count < 40 && deathVitals != null) deathLines.Add("DEATH t " + F(Time.time, "F1") + " cause " + deathVitals.LastEnvironmentDeath + " at " + V(deathVitals.transform.position)); };
        }

        // ------------------------------------------------------------------ reads
        static float PlayerHp(WorldMacroPlaytestSession s) { var v = s.Walker.Body.GetComponent<PlayerVitals>(); return v != null ? v.Hp01 : -1f; }

        static string Where(WorldMacroPlaytestSession s)
        {
            var body = s.Walker.Body; var p = body.transform.position;
            bool nav = NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas);
            var scenes = new List<string>(); for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) scenes.Add(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name);
            return "player " + V(p) + " yaw " + F(body.transform.eulerAngles.y, "F0") + " grounded " + body.isGrounded + " hp " + F(PlayerHp(s)) + " | navmesh within 2 m " + nav + (nav ? " d " + F(Vector3.Distance(hit.position, p)) : "") +
                " | store " + Path.GetFileName(Harness303.StorePath(s)) + " isolated yes | quality " + QualitySettings.names[QualitySettings.GetQualityLevel()] + " | focused " + Harness303.Focused + " appActive " + UnityEditorInternal.InternalEditorUtility.isApplicationActive +
                " | ui page '" + UiAdapter303.Page + "' | scenes " + string.Join(",", scenes) + " | actors " + (s.Actors != null ? s.Actors.Length : 0) + " | feedback '" + s.LastFeedback + "' | exceptions " + exceptions + " errors " + errors + " | deaths " + deathLines.Count + (deathLines.Count > 0 ? " last " + deathLines[deathLines.Count - 1] : "");
        }

        static string PathOf(Transform t) { var names = new List<string>(); int n = 0; while (t != null && n++ < 3) { names.Add(t.name); t = t.parent; } names.Reverse(); return string.Join("/", names); }

        static string Gates(WorldMacroPlaytestSession s)
        {
            var lines = new List<string>();
            lines.Add("south gate open (session) " + s.DemoSouthGateOpen + " | ledger has hwanggyeong_south_gate " + s.Progress.ledger.completed.Contains(WorldMacroPlaytestSession.SouthGateOpenedId));
            foreach (var g in Object.FindObjectsByType<WorldSealGate308>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                lines.Add("SEALGATE " + PathOf(g.transform) + " at " + V(g.transform.position) + " active " + g.isActiveAndEnabled + " configured " + g.IsConfigured + " fact " + g.RequiredCompleted + " open " + g.IsOpen + " progress " + F(g.OpenProgress) +
                    " blockers on " + g.ClosedBlockers.Count(c => c != null && c.enabled) + "/" + g.ClosedBlockers.Length + " nav obstacles on " + g.ClosedNavigation.Count(o => o != null && o.enabled) + "/" + g.ClosedNavigation.Length +
                    " open colliders on " + g.OpenColliders.Count(c => c != null && c.enabled) + "/" + g.OpenColliders.Length);
            foreach (var d in Object.FindObjectsByType<SouthGateDoorPresentation>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(d); var physical = so.FindProperty("_closedBlockers"); var navs = so.FindProperty("_closedNavigation"); int on = 0, total = 0, non = 0, ntotal = 0;
                if (physical != null) for (int i = 0; i < physical.arraySize; i++) { var c = physical.GetArrayElementAtIndex(i).objectReferenceValue as Collider; total++; if (c != null && c.enabled) on++; }
                if (navs != null) for (int i = 0; i < navs.arraySize; i++) { var o = navs.GetArrayElementAtIndex(i).objectReferenceValue as NavMeshObstacle; ntotal++; if (o != null && o.enabled) non++; }
                lines.Add("DOOR " + PathOf(d.transform) + " at " + V(d.transform.position) + " active " + d.isActiveAndEnabled + " configured " + d.IsConfigured + " open requested " + d.IsOpenRequested + " open complete " + d.OpenAnimationComplete + " blockers on " + on + "/" + total + " nav obstacles on " + non + "/" + ntotal);
            }
            return string.Join("\n", lines);
        }

        static string PathLine(string rest)
        {
            var ends = rest.Split('>'); if (ends.Length != 2) return "refused: path:x,z>x,z";
            Vector3 P(string t) { var v = t.Split(',').Select(x => float.Parse(x.Trim(), Inv)).ToArray(); if (v.Length >= 3) return new Vector3(v[0], v[1], v[2]); var top = new Vector3(v[0], 2000f, v[1]); return Physics.Raycast(top, Vector3.down, out var g, 4000f, ~0, QueryTriggerInteraction.Ignore) ? g.point : new Vector3(v[0], 0, v[1]); }
            Vector3 a = P(ends[0]), b = P(ends[1]);
            bool sa = NavMesh.SamplePosition(a, out var ha, 8f, NavMesh.AllAreas), sb = NavMesh.SamplePosition(b, out var hb, 8f, NavMesh.AllAreas);
            if (!sa || !sb) return "PATH no navmesh within 8 m of " + (!sa ? "start " + V(a) : "") + (!sb ? " end " + V(b) : "");
            var path = new NavMeshPath(); bool found = NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, path); float length = 0; for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            var last = path.corners.Length > 0 ? path.corners[path.corners.Length - 1] : ha.position;
            return "PATH " + V(ha.position) + " -> " + V(hb.position) + " found " + found + " status " + path.status + " corners " + path.corners.Length + " length " + F(length, "F1") + " m | last corner " + V(last) + " to goal " + F(Vector3.Distance(last, hb.position), "F1") + " m" + (path.corners.Length <= 16 ? " | corners " + string.Join(" ", path.corners.Select(c => V(c))) : "");
        }

        static string ActorLine(WorldMacroPlaytestSession s, PrologueEncounter a)
        {
            var life = a.GetComponent<EnemyVitals>(); var rig = a.GetComponentInChildren<EnemyRigMotion298>(true); var ec = a.GetComponent<EnemyController>();
            return a.Id + " | " + V(a.transform.position) + " d " + F(Vector3.Distance(a.transform.position, s.Walker.Body.transform.position), "F1") + " | active " + a.gameObject.activeInHierarchy + " enabled " + a.enabled + " | " + a.Current + "/" + a.Phase + " brain " + a.BrainActive308 + " turn " + a.HoldsTurn308 +
                " ranged " + a.Ranged + " | hp " + (life != null ? F(life.Hp, "F0") + "/" + F(life.MaxHp, "F0") + " boss " + life.IsBoss : "none") + " | moved " + F(a.MovedSeconds308, "F1") + " s slip " + F(a.SideSlipSeconds308, "F2") + " s" + (a.MovedSeconds308 > 0 ? " (" + F(100f * a.SideSlipSeconds308 / a.MovedSeconds308, "F1") + " %)" : "") +
                " | rig " + (rig == null ? "none" : "configured " + rig.IsConfigured + " graph " + rig.HasGraph + " pose " + rig.CurrentPose + " strikeRead " + (rig.StrikeRead != null)) + (ec != null ? " | telegraphing " + ec.IsTelegraphing + " attackEnabled " + ec.AttackEnabled : "") + " | navmesh " + NavMesh.SamplePosition(a.transform.position, out _, 2f, NavMesh.AllAreas);
        }

        static string Enemies(WorldMacroPlaytestSession s, float radius)
        {
            var p = s.Walker.Body.transform.position; var lines = new List<string>();
            var pack = s.Walker.Body.GetComponent<EnemyPack308>();
            lines.Add("PACK " + (pack == null ? "none" : "members " + pack.Members + " melee turns " + pack.MeleeTurns + " ranged turns " + pack.RangedTurns + " peak melee " + pack.PeakMeleeTurns + " peak ranged " + pack.PeakRangedTurns + " grants " + pack.Grants + " calls " + pack.Calls) + " | player hp " + F(PlayerHp(s)));
            foreach (var a in (s.Actors ?? Array.Empty<PrologueEncounter>()).Where(a => a != null && Vector3.Distance(a.transform.position, p) <= radius).OrderBy(a => Vector3.Distance(a.transform.position, p)).Take(24)) lines.Add("ACTOR " + ActorLine(s, a));
            return string.Join("\n", lines);
        }

        static string Owned(WorldMacroPlaytestSession s)
        {
            var g = s.Progress; var owned = g.equipment != null ? g.equipment.Owned.Select(o => o.Id).ToArray() : Array.Empty<string>();
            return "owned " + owned.Length + " [" + string.Join(",", owned) + "] revision " + (g.equipment != null ? g.equipment.Revision : -1) + " | boss drops due " + s.BossDropsDue308 + " | evidence [" + string.Join(",", g.campaign != null && g.campaign.EncounterEvidence != null ? g.campaign.EncounterEvidence : new List<string>()) +
                "] | defeated [" + string.Join(",", g.defeated) + "] | checkpoint " + g.ledger.checkpoint + " " + V(g.ledger.checkpointPosition) + " | currency " + g.ledger.currency + " | completed " + g.ledger.completed.Count + " [" + string.Join(",", g.ledger.completed.Skip(Math.Max(0, g.ledger.completed.Count - 8))) + "] | feedback '" + s.LastFeedback + "'";
        }

        static string Boss()
        {
            var bars = Object.FindObjectsByType<HudBossBar304>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            return "boss bars " + bars.Length + ": " + string.Join("; ", bars.Select(b => b.name + " active " + b.isActiveAndEnabled + " showing " + b.Showing + " alpha " + F(b.Alpha)));
        }

        static string LockOnProbe(WorldMacroPlaytestSession s)
        {
            var lockOn = s.Walker.Motor.GetComponent<LockOn>(); if (lockOn == null) return "lockon: no LockOn on the motor";
            bool before = lockOn.IsLocked; if (before) lockOn.Toggle();
            lockOn.Toggle(); string got = lockOn.IsLocked ? lockOn.Target.name + " at " + V(lockOn.Target.transform.position) + " d " + F(Vector3.Distance(lockOn.Target.transform.position, s.Walker.Body.transform.position), "F1") : "nothing";
            if (lockOn.IsLocked) lockOn.Toggle();
            return "lockon toggle selected " + got + " (was locked " + before + ", now released)";
        }

        // ------------------------------------------------------------------ walkers
        static double sampleUntil; static readonly Dictionary<int, Vector3> sampleLast = new Dictionary<int, Vector3>(); static readonly List<float> sampleSpeeds = new List<float>(); static int samplePeak; static float sampleLastTime; static string sampleNote = "walkers sample: not asked";
        static string Walkers(WorldMacroPlaytestSession s, string rest)
        {
            var all = Object.FindObjectsByType<TownWalkers308>(FindObjectsInactive.Include, FindObjectsSortMode.None); if (all.Length == 0) return "walkers: no TownWalkers308 in the loaded scenes";
            var w = all[0];
            if (rest == "off" || rest == "on") { w.enabled = rest == "on"; return "walkers component enabled " + w.enabled + " (Play only, in memory)"; }
            if (rest.StartsWith("sample:", StringComparison.Ordinal))
            {
                sampleUntil = Now + float.Parse(rest.Substring(7), Inv); sampleLast.Clear(); sampleSpeeds.Clear(); samplePeak = 0; sampleLastTime = Time.time; sampleNote = "walkers sample: running";
                EditorApplication.update -= SampleTick; EditorApplication.update += SampleTick; return sampleNote;
            }
            var bodies = w.GetComponentsInChildren<TownWalkerBody308>(false); var p = s.Walker.Body.transform.position;
            string stride = string.Join(",", w.BodyPrefabs.Where(b => b != null).Select(b => b.name + " stride " + F(b.StrideMetres) + " max " + b.MaxActive));
            return "walkers " + all.Length + " component(s) | enabled " + w.isActiveAndEnabled + " total " + w.Total + " active " + w.Active + " inside " + w.Inside + " threatened " + w.Threatened + " | active bodies " + bodies.Length + " drawn " + bodies.Count(b => b.Drawn) +
                " nearest " + (bodies.Length > 0 ? F(bodies.Min(b => Vector3.Distance(b.transform.position, p)), "F1") : "-") + " m farthest " + (bodies.Length > 0 ? F(bodies.Max(b => Vector3.Distance(b.transform.position, p)), "F1") : "-") + " m | colliders on bodies " + bodies.Sum(b => b.GetComponentsInChildren<Collider>().Length) +
                " EnemyVitals on bodies " + bodies.Sum(b => b.GetComponentsInChildren<EnemyVitals>().Length) + " | prefabs " + stride + " | " + sampleNote;
        }
        static void SampleTick()
        {
            if (!EditorApplication.isPlaying || Now > sampleUntil)
            {
                EditorApplication.update -= SampleTick; var sp = sampleSpeeds.Where(v => v > .15f).OrderBy(v => v).ToList();
                sampleNote = "walkers sample: done | peak active bodies " + samplePeak + " | walking speed samples " + sp.Count + (sp.Count > 0 ? " median " + F(sp[sp.Count / 2]) + " p05 " + F(sp[(int)(sp.Count * .05f)]) + " p95 " + F(sp[(int)(sp.Count * .95f)]) + " max " + F(sp[sp.Count - 1]) + " m/s" : "") + " | standing samples " + sampleSpeeds.Count(v => v <= .15f);
                return;
            }
            float dt = Time.time - sampleLastTime; if (dt < .25f) return; sampleLastTime = Time.time;
            var all = Object.FindObjectsByType<TownWalkers308>(FindObjectsSortMode.None); if (all.Length == 0) return;
            var bodies = all[0].GetComponentsInChildren<TownWalkerBody308>(false); samplePeak = Mathf.Max(samplePeak, bodies.Length);
            foreach (var b in bodies)
            {
                int id = b.GetInstanceID(); var at = b.transform.position;
                if (sampleLast.TryGetValue(id, out var was)) { float d = Vector3.Distance(new Vector3(at.x, 0, at.z), new Vector3(was.x, 0, was.z)); if (d < 6f) sampleSpeeds.Add(d / dt); }
                sampleLast[id] = at;
            }
        }

        // ------------------------------------------------------------------ frames
        static double framesUntil; static readonly List<float> frameMs = new List<float>(); static int lastFrame, focusedFrames; static string framesNote = "frames: not asked";
        static string FramesStart(float seconds)
        {
            framesUntil = Now + seconds; frameMs.Clear(); lastFrame = Time.frameCount; focusedFrames = 0; framesNote = "frames: running";
            EditorApplication.update -= FramesTick; EditorApplication.update += FramesTick; return framesNote + " " + F(seconds, "F0") + " s | quality " + QualitySettings.names[QualitySettings.GetQualityLevel()] + " vsync " + QualitySettings.vSyncCount + " targetFrameRate " + Application.targetFrameRate + " screen " + Screen.width + "x" + Screen.height;
        }
        static void FramesTick()
        {
            if (!EditorApplication.isPlaying || Now > framesUntil)
            {
                EditorApplication.update -= FramesTick; var ms = frameMs.OrderBy(v => v).ToList(); int n = ms.Count;
                framesNote = n == 0 ? "frames: done, no frames" : "frames: done | n " + n + " median " + F(ms[n / 2]) + " ms p95 " + F(ms[Mathf.Min(n - 1, (int)(n * .95f))]) + " ms p99 " + F(ms[Mathf.Min(n - 1, (int)(n * .99f))]) + " ms max " + F(ms[n - 1]) + " ms mean " + F(ms.Average()) + " ms | focused frames " + F(100f * focusedFrames / n, "F0") + " % | batches " + UnityStats.batches + " drawCalls " + UnityStats.drawCalls + " setPass " + UnityStats.setPassCalls + " tris " + UnityStats.triangles +
                    " | quality " + QualitySettings.names[QualitySettings.GetQualityLevel()] + " screen " + Screen.width + "x" + Screen.height + " | managed " + F(GC.GetTotalMemory(false) / 1048576f, "F0") + " MB unity allocated " + F(UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576f, "F0") + " MB reserved " + F(UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576f, "F0") + " MB";
                return;
            }
            if (Time.frameCount == lastFrame) return; lastFrame = Time.frameCount; frameMs.Add(Time.unscaledDeltaTime * 1000f); if (UnityEditorInternal.InternalEditorUtility.isApplicationActive) focusedFrames++;
        }
        static string FramesReport() { var s = Harness303.Session; return framesNote + (s != null && s.Walker != null && s.Walker.Body != null ? " | player " + V(s.Walker.Body.transform.position) : ""); }

        // ------------------------------------------------------------------ watch
        static double watchUntil; static readonly List<PrologueEncounter> watched = new List<PrologueEncounter>(); static readonly Dictionary<int, string> watchState = new Dictionary<int, string>(); static readonly Dictionary<int, HashSet<string>> watchPoses = new Dictionary<int, HashSet<string>>();
        static readonly List<string> watchLines = new List<string>(); static float watchHp, watchT0; static bool watchOn;
        static string WatchStart(WorldMacroPlaytestSession s, string rest)
        {
            var a = rest.Split(':'); float seconds = a.Length > 1 ? float.Parse(a[1], Inv) : 20f; var p = s.Walker.Body.transform.position;
            watched.Clear(); watchState.Clear(); watchPoses.Clear(); watchLines.Clear();
            if (a[0] == "near") watched.AddRange(s.Actors.Where(x => x != null && x.gameObject.activeInHierarchy && Vector3.Distance(x.transform.position, p) < 45f).OrderBy(x => Vector3.Distance(x.transform.position, p)).Take(8));
            else watched.AddRange(s.Actors.Where(x => x != null && x.Id == a[0]));
            if (watched.Count == 0) return "watch: no actor " + a[0];
            watchUntil = Now + seconds; watchHp = PlayerHp(s); watchT0 = Time.time; watchOn = true;
            EditorApplication.update -= WatchTick; EditorApplication.update += WatchTick;
            return "watch started " + watched.Count + " actor(s) for " + F(seconds, "F0") + " s: " + string.Join(",", watched.Select(x => x.Id));
        }
        static void WatchTick()
        {
            var s = Harness303.Session;
            if (!EditorApplication.isPlaying || s == null || Now > watchUntil) { EditorApplication.update -= WatchTick; watchOn = false; return; }
            float t = Time.time - watchT0; float hp = PlayerHp(s);
            if (Mathf.Abs(hp - watchHp) > .0005f) { if (watchLines.Count < 400) watchLines.Add(F(t, "F3") + " PLAYER hp " + F(watchHp, "F3") + " -> " + F(hp, "F3")); watchHp = hp; }
            foreach (var x in watched)
            {
                if (x == null) continue; var rig = x.GetComponentInChildren<EnemyRigMotion298>(true); var ec = x.GetComponent<EnemyController>(); var life = x.GetComponent<EnemyVitals>();
                string pose = rig != null ? rig.CurrentPose : "-"; int id = x.GetInstanceID();
                if (!watchPoses.TryGetValue(id, out var set)) watchPoses[id] = set = new HashSet<string>(); if (!string.IsNullOrEmpty(pose)) set.Add(pose);
                string state = x.Current + "/" + x.Phase + " tele " + (ec != null && ec.IsTelegraphing) + " turn " + x.HoldsTurn308 + " pose " + pose + " alive " + (life != null && life.IsAlive);
                if (!watchState.TryGetValue(id, out var was) || was != state) { watchState[id] = state; if (watchLines.Count < 400) watchLines.Add(F(t, "F3") + " " + x.Id + " " + state + " d " + F(Vector3.Distance(x.transform.position, s.Walker.Body.transform.position), "F1")); }
            }
        }
        static string WatchReport()
        {
            var s = Harness303.Session; var lines = new List<string> { "watch " + (watchOn ? "running" : "done") + " | lines " + watchLines.Count };
            lines.AddRange(watchLines);
            foreach (var x in watched) if (x != null) lines.Add("SUMMARY " + x.Id + " poses {" + string.Join(",", watchPoses.TryGetValue(x.GetInstanceID(), out var set) ? set.OrderBy(v => v) : Enumerable.Empty<string>()) + "} | " + (s != null ? ActorLine(s, x) : ""));
            var pack = s != null ? s.Walker.Body.GetComponent<EnemyPack308>() : null; if (pack != null) lines.Add("PACK peak melee " + pack.PeakMeleeTurns + " peak ranged " + pack.PeakRangedTurns + " members " + pack.Members + " grants " + pack.Grants);
            return string.Join("\n", lines);
        }

        // ------------------------------------------------------------------ acts (isolated Play only)
        static string Near(WorldMacroPlaytestSession s, string rest)
        {
            var a = rest.Split(','); var actor = s.Actors.FirstOrDefault(x => x != null && x.Id == a[0]); if (actor == null) return "near: no actor " + a[0];
            float dist = a.Length > 1 ? float.Parse(a[1], Inv) : 8f; bool back = a.Length > 2 && a[2] == "back";
            var forward = Vector3.ProjectOnPlane(actor.transform.forward, Vector3.up).normalized; var want = actor.transform.position + (back ? -forward : forward) * dist;
            if (!NavMesh.SamplePosition(want, out var hit, 6f, NavMesh.AllAreas)) return "near: no navmesh within 6 m of " + V(want) + " (actor at " + V(actor.transform.position) + ")";
            float yaw = Harness303.YawTo(hit.position, actor.transform.position); s.Teleport(hit.position, yaw);
            return "teleported to " + V(hit.position) + " yaw " + F(yaw, "F0") + " | " + (back ? "behind" : "in front of") + " " + actor.Id + " at " + F(Harness303.Flat(hit.position, actor.transform.position), "F1") + " m | actor " + actor.Current + "/" + actor.Phase + " active " + actor.gameObject.activeInHierarchy;
        }

        static string Hit(WorldMacroPlaytestSession s, string id, float amount)
        {
            var actor = s.Actors.FirstOrDefault(x => x != null && x.Id == id); if (actor == null) return "hit: no actor " + id;
            var life = actor.GetComponent<EnemyVitals>(); if (life == null) return "hit: no vitals on " + id; float before = life.Hp;
            life.TakeDamage(amount, AttackProvenance.Create(s.Walker.Body.gameObject, DamageSource.PlayerDirect, Element.Metal));
            return "hit " + id + " hp " + F(before, "F0") + " -> " + F(life.Hp, "F0") + " alive " + life.IsAlive + " | actor active " + actor.gameObject.activeInHierarchy + " enabled " + actor.enabled + " " + actor.Current + "/" + actor.Phase;
        }

        static readonly Dictionary<int, Oheangbu.Data.World.EnemyStrikeRead308> strikeKept = new Dictionary<int, Oheangbu.Data.World.EnemyStrikeRead308>();
        static string Strike(WorldMacroPlaytestSession s, string rest)
        {
            var a = rest.Split(':'); var actor = s.Actors.FirstOrDefault(x => x != null && x.Id == a[0]); if (actor == null) return "strike: no actor " + a[0];
            var rig = actor.GetComponentInChildren<EnemyRigMotion298>(true); if (rig == null) return "strike: no rig on " + a[0]; int id = rig.GetInstanceID();
            if (a.Length > 1 && a[1] == "off") { if (!strikeKept.ContainsKey(id)) strikeKept[id] = rig.StrikeRead; rig.StrikeRead = null; }
            else if (strikeKept.TryGetValue(id, out var kept)) { rig.StrikeRead = kept; strikeKept.Remove(id); }
            return "strike read on " + a[0] + " = " + (rig.StrikeRead != null ? rig.StrikeRead.name : "none") + " (Play only, in memory)";
        }

        static string FixtureSouthGate(WorldMacroPlaytestSession s)
        {
            var g = s.Progress; var added = new List<string>();
            if (!g.defeated.Contains(WorldMacroPlaytestSession.SouthGateGeneralId)) { g.defeated.Add(WorldMacroPlaytestSession.SouthGateGeneralId); added.Add("defeated"); }
            if (!g.ledger.completed.Contains(WorldMacroPlaytestSession.SouthGateOpenedId)) { g.ledger.completed.Add(WorldMacroPlaytestSession.SouthGateOpenedId); added.Add("ledger"); }
            if (g.campaign != null && g.campaign.Completed != null) foreach (var id in new[] { "south_gate", "ending" }) if (!g.campaign.Completed.Contains(id)) { g.campaign.Completed.Add(id); added.Add("campaign:" + id); }
            return "FIXTURE southgate (in memory, isolated store " + Path.GetFileName(Harness303.StorePath(s)) + "): added [" + string.Join(",", added) + "] | session south gate open " + s.DemoSouthGateOpen + " | valid " + WorldMacroProgress.Valid(g);
        }

        static string SaveCopy(WorldMacroPlaytestSession s, string name)
        {
            string store = Harness303.StorePath(s); if (!File.Exists(store)) return "save-copy: no file " + store;
            Directory.CreateDirectory(OutFolder); string to = Path.Combine(OutFolder, name + ".json"); File.Copy(store, to, true);
            return "save-copy " + Path.GetFileName(store) + " -> " + to + " (" + new FileInfo(to).Length + " bytes)";
        }

        static string HoldFrom(string source)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode required";
            if (!File.Exists(source)) return "refused: no file " + source;
            string reply = Map307Capture.Run("play-hold"); if (!reply.StartsWith("started", StringComparison.Ordinal)) return "play-hold " + reply;
            var s = Object.FindFirstObjectByType<WorldMacroPlaytestSession>(); string suffix = s != null ? s.TestSaveSuffix : "";
            if (string.IsNullOrEmpty(suffix) || !suffix.StartsWith("_c303", StringComparison.Ordinal)) { Map307Capture.Run("play-release"); return "refused: no harness suffix on the session after play-hold (" + suffix + "); released"; }
            string to = Path.Combine(Harness303.SaveFolder, s.Content.SaveSlot + suffix + ".json");
            if (File.Exists(to)) return "refused: " + to + " already exists";
            File.Copy(source, to, false);
            return "hold-from: " + reply + " | isolated store " + Path.GetFileName(to) + " seeded from a copy of " + source + " (" + new FileInfo(to).Length + " bytes)";
        }

        // ------------------------------------------------------------------ route (CharacterController.Move along a walked line, the RealmStream308 glide method)
        [Serializable] sealed class WayLine { public string id = ""; public float[] walkX = new float[0], walkY = new float[0], walkZ = new float[0]; }
        [Serializable] sealed class WayFile { public WayLine[] ways = new WayLine[0]; }
        static readonly List<Vector3> routePoints = new List<Vector3>(); static int routeIndex; static float routeSpeed, routeStall, routeMaxStall, routeMoved; static double routeUntil; static bool routeOn; static Vector3 routeLast; static string routeNote = "route: not asked";
        static string RouteStart(WorldMacroPlaytestSession s, string rest)
        {
            // route:<way id>[,speed[,seconds]]  from Art/World/Compact/Rebuild/CliffWays308/layout.json
            var a = rest.Split(','); string file = Path.Combine(Harness303.RepoRoot, "Art", "World", "Compact", "Rebuild", "CliffWays308", "layout.json");
            if (!File.Exists(file)) return "route: no " + file;
            var way = JsonUtility.FromJson<WayFile>(File.ReadAllText(file)).ways.FirstOrDefault(w => w.id == a[0]); if (way == null || way.walkX.Length < 2) return "route: no way " + a[0];
            routePoints.Clear(); for (int i = 0; i < way.walkX.Length; i++) routePoints.Add(new Vector3(way.walkX[i], way.walkY[i], way.walkZ[i]));
            routeSpeed = a.Length > 1 ? float.Parse(a[1], Inv) : 3.5f; routeUntil = Now + (a.Length > 2 ? float.Parse(a[2], Inv) : 90f);
            s.Teleport(routePoints[0] + Vector3.up * .15f, Harness303.YawTo(routePoints[0], routePoints[1]));
            routeIndex = 1; routeStall = routeMaxStall = routeMoved = 0; routeLast = routePoints[0]; routeOn = true; routeNote = "route: running";
            EditorApplication.update -= RouteTick; EditorApplication.update += RouteTick;
            return "route " + a[0] + " started: " + routePoints.Count + " points from " + V(routePoints[0]) + " to " + V(routePoints[routePoints.Count - 1]) + " at " + F(routeSpeed, "F1") + " m/s";
        }
        static void RouteTick()
        {
            var s = Harness303.Session; var body = s != null && s.Walker != null ? s.Walker.Body : null;
            if (!EditorApplication.isPlaying || body == null || !body.enabled || Now > routeUntil) { RouteEnd(body, "time or Play ended"); return; }
            var p = body.transform.position; float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
            while (routeIndex < routePoints.Count && Harness303.Flat(routePoints[routeIndex], p) < .6f) routeIndex++;
            if (routeIndex >= routePoints.Count) { RouteEnd(body, "reached the last point"); return; }
            float step = Harness303.Flat(p, routeLast); routeMoved += step; routeLast = p;
            if (step < routeSpeed * dt * .2f) { routeStall += dt; routeMaxStall = Mathf.Max(routeMaxStall, routeStall); } else routeStall = 0;
            if (routeStall > 4f) { RouteEnd(body, "STUCK 4 s"); return; }
            var aim = routePoints[routeIndex]; var flat = new Vector3(aim.x - p.x, 0, aim.z - p.z);
            body.Move(flat.normalized * routeSpeed * dt + Vector3.down * .2f);
        }
        static void RouteEnd(CharacterController body, string why)
        {
            EditorApplication.update -= RouteTick; routeOn = false; var p = body != null ? body.transform.position : Vector3.zero; var top = routePoints[routePoints.Count - 1];
            int at = Mathf.Min(routeIndex, routePoints.Count - 1);
            routeNote = "route: done (" + why + ") | point " + routeIndex + "/" + routePoints.Count + " | player " + V(p) + " | line y at that point " + F(routePoints[at].y, "F1") + " (player - line " + F(p.y - routePoints[at].y) + " m) | to the top " + F(Vector3.Distance(p, top), "F1") + " m | moved " + F(routeMoved, "F1") + " m | longest stall " + F(routeMaxStall) + " s";
        }
        static string RouteReport(WorldMacroPlaytestSession s) => routeNote + (routeOn ? " | point " + routeIndex + "/" + routePoints.Count : "") + " | now " + V(s.Walker.Body.transform.position) + " grounded " + s.Walker.Body.isGrounded + " hp " + F(PlayerHp(s));

        // ------------------------------------------------------------------ walk (virtual keyboard, the real motor)
        static double walkUntil, walkNextTap; static Key[] walkKeys = Array.Empty<Key>(); static Key walkTap = Key.None; static float walkInterval; static bool walkOn;
        static Vector3 walkStart, walkForward; static float walkMaxAlong, walkMinAlong, walkMaxY, walkMinY, walkMaxFlat; static int walkAir; static string walkNote = "walk: not asked"; static readonly List<string> walkTrail = new List<string>(); static double walkNextTrail;
        static Vector2 Center => new Vector2(Screen.width * .5f, Screen.height * .5f);
        static string WalkStart(WorldMacroPlaytestSession s, string rest)
        {
            if (walkOn) return "refused: a walk is running";
            var a = rest.Split(':'); float seconds = float.Parse(a[0], Inv);
            walkKeys = a.Length > 1 && a[1] == "-" ? Array.Empty<Key>() : a.Length > 1 && a[1].Length > 0 ? a[1].Split('+').Select(k => (Key)Enum.Parse(typeof(Key), k, true)).ToArray() : new[] { Key.W };
            walkTap = Key.None; walkInterval = 0;
            if (a.Length > 2 && a[2].Length > 0) { var t = a[2].Split('@'); walkTap = (Key)Enum.Parse(typeof(Key), t[0], true); walkInterval = t.Length > 1 ? float.Parse(t[1], Inv) : .8f; }
            Harness303.FocusGameView();
            var assets = new List<InputActionAsset> { Harness303.Field<InputActionAsset>(s.Walker.Motor, "_actions"), Harness303.Field<InputActionAsset>(s.Walker.Drawing, "_actions") };
            string begun = VirtualInput303.Begin(assets);
            var body = s.Walker.Body.transform; walkStart = body.position; walkForward = Vector3.ProjectOnPlane(body.forward, Vector3.up).normalized;
            walkMaxAlong = walkMinAlong = walkMaxFlat = 0; walkMaxY = walkMinY = walkStart.y; walkAir = 0; walkTrail.Clear(); walkNextTrail = 0;
            walkUntil = Now + seconds; walkNextTap = Now + .4; walkOn = true; walkNote = "walk: running";
            VirtualInput303.Hold = new InputFrame303 { Keys = walkKeys, Position = Center, Tag = "probe-walk" };
            EditorApplication.update -= WalkTick; EditorApplication.update += WalkTick;
            return "walk started " + F(seconds, "F1") + " s keys " + string.Join("+", walkKeys) + (walkTap != Key.None ? " tap " + walkTap + " every " + F(walkInterval) + " s" : "") + " from " + V(walkStart) + " forward " + V(walkForward) + " | input " + begun + " | focused " + Harness303.Focused;
        }
        static void WalkTick()
        {
            var s = Harness303.Session;
            if (!EditorApplication.isPlaying || s == null || Now > walkUntil)
            {
                EditorApplication.update -= WalkTick; walkOn = false;
                VirtualInput303.Hold = InputFrame303.Neutral(Center); VirtualInput303.ClearQueue(); string ended = VirtualInput303.Active ? VirtualInput303.End(out _) : "inactive";
                var end = s != null ? s.Walker.Body.transform.position : walkStart;
                walkNote = "walk: done | start " + V(walkStart) + " end " + V(end) + " | along forward: end " + F(Vector3.Dot(end - walkStart, walkForward)) + " max " + F(walkMaxAlong) + " min " + F(walkMinAlong) + " m | max flat from start " + F(walkMaxFlat) + " m | y min " + F(walkMinY) + " max " + F(walkMaxY) + " (start " + F(walkStart.y) + ") | airborne samples " + walkAir +
                    " | hp " + (s != null ? F(PlayerHp(s)) : "-") + " | consumed " + VirtualInput303.Consumed + " held " + VirtualInput303.HeldFrames + " | input end " + ended;
                return;
            }
            var p = s.Walker.Body.transform.position; float along = Vector3.Dot(p - walkStart, walkForward);
            walkMaxAlong = Mathf.Max(walkMaxAlong, along); walkMinAlong = Mathf.Min(walkMinAlong, along); walkMaxY = Mathf.Max(walkMaxY, p.y); walkMinY = Mathf.Min(walkMinY, p.y);
            walkMaxFlat = Mathf.Max(walkMaxFlat, Harness303.Flat(p, walkStart)); if (!s.Walker.Body.isGrounded) walkAir++;
            if (Now > walkNextTrail) { walkNextTrail = Now + 1.0; if (walkTrail.Count < 40) walkTrail.Add(V(p)); }
            if (walkTap != Key.None && Now > walkNextTap) { walkNextTap = Now + walkInterval; VirtualInput303.Tap(walkTap, Center, 3, walkKeys); }
        }
        static string WalkReport(WorldMacroPlaytestSession s) => walkNote + " | trail " + string.Join(" ", walkTrail) + " | now " + V(s.Walker.Body.transform.position) + " grounded " + s.Walker.Body.isGrounded;
    }
}
