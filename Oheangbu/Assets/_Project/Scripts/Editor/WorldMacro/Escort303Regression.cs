using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #303 follow-up (CHANGGUI_COMBAT_TEST_PLAN.md §10): does the #252 capital escort still run in the main scene with the
    // #303 roadside wrecks and the commission giver colliders on its road? Queue entry Run(string):
    //   audit[:main|folklore298|architecture296] — Edit: E0 static audit A1..A6 → EscortTest/escort-audit*.json (GE0 = A1 + A2)
    //   start[:full]                            — Edit, W_Demo_Main clean: isolated Play (_c303escort_<UTC>) runs E1..E13 (full = E11 death recovery)
    //   status | report | abort
    // Fixed fixture steps (listed in the report): combat off, teleport approaches before the prerequisite talks, the
    // Cheongryong API defeat, the departure teleport, E11 fatal fall. Everything else is virtual keyboard/mouse input.
    [InitializeOnLoad]
    public static partial class Escort303Regression
    {
        const string StateKey = "Escort303Regression.State";
        internal const string RouteRoot = "CapitalEscort252", Roadside = "Roadside303";
        internal static readonly string[] StopIds = { "escort_start", "checkpoint_1", "checkpoint_2", "cargo_delivery" };
        internal static readonly Vector3 VillageShift = new Vector3(60f, 173.766338f, -40f);   // #292 rigid village move
        static string Folder => Path.Combine(Harness303.Roadside303Folder, "EscortTest");
        static string RunFolder => Path.Combine(Folder, state != null ? state.runId : "none");

        // ------------------------------------------------------------------ route legs (layout routes chained per stop)
        internal sealed class Leg { public string id, from, to; public Vector3[] points = Array.Empty<Vector3>(); public string[] routes; }
        internal static readonly (string id, string from, string to, string[] routes)[] LegTable =
        {
            ("leg1", "escort_start", "checkpoint_1", new[] { "merchant__road_pass", "road_pass__inspection_one" }),
            ("leg2", "checkpoint_1", "checkpoint_2", new[] { "inspection_one__road_hamlet", "road_hamlet__inspection_two" }),
            ("leg3", "checkpoint_2", "cargo_delivery", new[] { "inspection_two__capital_delivery" }),
        };

        internal static DemoEscortSceneRoute Route(Scene scene)
        {
            var root = Harness303.Find(scene, RouteRoot);
            return root != null ? root.GetComponent<DemoEscortSceneRoute>() : Object.FindObjectsByType<DemoEscortSceneRoute>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(r => r.gameObject.scene == scene);
        }
        internal static DemoEscortStop Stop(DemoEscortSceneRoute route, string id) => route?.Stops?.FirstOrDefault(s => s != null && s.Id == id);

        /// <summary>Grounded polyline from the `from` stop's parking to the `to` stop's parking along the layout routes.</summary>
        internal static bool TryLeg(WorldMacroPlaytestSession s, DemoEscortSceneRoute route, int index, out Leg leg, out string why)
        {
            var row = LegTable[index]; leg = new Leg { id = row.id, from = row.from, to = row.to, routes = row.routes }; why = "";
            var layout = s.MountainLayout;
            if (layout == null) { why = "session has no MountainLayout"; return false; }
            var places = layout.Places.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First().XZ);
            var chain = new List<Vector2>();
            foreach (var id in row.routes)
            {
                var r = layout.Routes.FirstOrDefault(x => x.Id == id);
                if (r == null || !places.ContainsKey(r.From) || !places.ContainsKey(r.To)) { why = "layout route " + id + " missing"; return false; }
                var pts = new[] { places[r.From] }.Concat(r.Bends ?? Array.Empty<Vector2>()).Concat(new[] { places[r.To] }).ToList();
                if (chain.Count > 0 && Vector2.Distance(chain[chain.Count - 1], pts[pts.Count - 1]) < Vector2.Distance(chain[chain.Count - 1], pts[0])) pts.Reverse();
                chain.AddRange(chain.Count > 0 && Vector2.Distance(chain[chain.Count - 1], pts[0]) < .5f ? pts.Skip(1) : pts);
            }
            var a = Stop(route, row.from); var b = Stop(route, row.to);
            if (a?.Parking == null || b?.Parking == null) { why = "stop parking missing (" + row.from + " / " + row.to + ")"; return false; }
            Vector2 A = new Vector2(a.Parking.position.x, a.Parking.position.z), B = new Vector2(b.Parking.position.x, b.Parking.position.z);
            int ia = Nearest(chain, A), ib = Nearest(chain, B);
            if (ib < ia) { chain.Reverse(); ia = Nearest(chain, A); ib = Nearest(chain, B); }
            var mid = chain.Skip(ia + 1).Take(Math.Max(0, ib - ia - 1)).ToList();
            var flat = new List<Vector2> { A }; flat.AddRange(mid); flat.Add(B);
            var dense = new List<Vector2>();
            for (int i = 0; i < flat.Count; i++)
            {
                if (i == 0) { dense.Add(flat[0]); continue; }
                float d = Vector2.Distance(flat[i - 1], flat[i]); int n = Mathf.Max(1, Mathf.CeilToInt(d / 8f));
                for (int j = 1; j <= n; j++) dense.Add(Vector2.Lerp(flat[i - 1], flat[i], j / (float)n));
            }
            var grounded = new List<Vector3>();
            foreach (var p in dense)
            {
                if (!Harness303.Ground(new Vector3(p.x, 0, p.y), out var hit)) { why = "no terrain under " + p; return false; }
                grounded.Add(hit.point);
            }
            grounded[0] = a.Parking.position; grounded[grounded.Count - 1] = b.Parking.position;
            leg.points = grounded.ToArray();
            return true;
        }
        static int Nearest(List<Vector2> pts, Vector2 p) { int best = 0; float d = float.MaxValue; for (int i = 0; i < pts.Count; i++) { float x = Vector2.Distance(pts[i], p); if (x < d) { d = x; best = i; } } return best; }

        internal static bool UnderRoadside(Transform t)
        {
            for (; t != null; t = t.parent) if (t.parent == null && t.name == Roadside) return true;
            return false;
        }
        internal static string Cause(Collider c) => c == null ? "" : UnderRoadside(c.transform) ? "#303" : "pre-existing";

        internal static List<Collider> RoadsideColliders(Scene scene)
        {
            var root = Harness303.Find(scene, Roadside);
            return root == null ? new List<Collider>() : root.GetComponentsInChildren<Collider>(false).Where(c => c.enabled && !c.isTrigger).ToList();
        }
        internal static Vector3 Closest(Collider c, Vector3 p)
        {
            bool exact = c is BoxCollider || c is SphereCollider || c is CapsuleCollider || c is MeshCollider m && m.convex;
            return exact ? c.ClosestPoint(p) : c.bounds.ClosestPoint(p);
        }
        internal static float Clearance(List<Collider> colliders, Vector3 p, float radius, out Collider nearest)
        {
            nearest = null; float best = float.MaxValue;
            foreach (var c in colliders)
            {
                if (c == null || Vector3.Distance(c.bounds.center, p) > c.bounds.extents.magnitude + 25f) continue;
                var q = Closest(c, p); float d = Harness303.Flat(q, p) - radius;
                if (Mathf.Abs(q.y - p.y) > 3f) continue;
                if (d < best) { best = d; nearest = c; }
            }
            return best;
        }

        /// <summary>DemoEscortPresentation.LateUpdate sweep, read-only: capsule r 0.27 from +0.3 to +1.4 along a→b; a hit with
        /// normal.y &lt; 0.65 blocks. Companion, cargo, vehicle and player colliders are ignored like the presenter does.</summary>
        internal static Collider Sweep(Vector3 a, Vector3 b, Func<Collider, bool> ignore, out Vector3 point)
        {
            point = default; var delta = b - a;
            if (delta.sqrMagnitude < 1e-6f) return null;
            foreach (var hit in Physics.CapsuleCastAll(a + Vector3.up * .3f, a + Vector3.up * 1.4f, .27f, delta.normalized, delta.magnitude + .02f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider != null && !ignore(hit.collider) && hit.normal.y < .65f) { point = hit.point; return hit.collider; }
            return null;
        }
        internal static Func<Collider, bool> Ignorer(WorldMacroPlaytestSession s)
        {
            var skip = new List<Transform>();
            if (s.DemoEscortCompanion != null) skip.Add(s.DemoEscortCompanion);
            if (s.DemoEscortCargo != null) skip.Add(s.DemoEscortCargo);
            if (s.DemoEscortSeat != null && s.DemoEscortSeat.Vehicle != null) skip.Add(s.DemoEscortSeat.Vehicle.transform);
            if (s.Walker != null && s.Walker.Body != null) skip.Add(s.Walker.Body.transform);
            var scene = s.gameObject.scene;
            return c => c == null || c.gameObject.scene != scene || skip.Any(t => c.transform.IsChildOf(t));
        }

        // ------------------------------------------------------------------ state / lifecycle
        [Serializable] sealed class EState
        {
            public bool active, finished, restartRequested, interrupted, focusReady, uiTidied, inputStarted, full;
            public string runId = "", status = "IDLE", phase = "", phaseNote = "", finalStatus = "", interruptReason = "", rememberedProgress = "";
            public int boots, lastFrame = -1, logErrors, currencyBeforeDelivery = -1, currencyAfterDelivery = -1;
            public double began, phaseAt, enteredAt, focusLossAt = -1;
            public Isolation303 iso = new Isolation303();
            public List<Check303> steps = new List<Check303>(), load = new List<Check303>(), cleanup = new List<Check303>();
            public List<string> fixtures = new List<string>(), realInputs = new List<string>(), findings = new List<string>(), errors = new List<string>(), notes = new List<string>();
        }
        static EState state;
        static GameObject probeObject;

        static Escort303Regression()
        {
            var json = SessionState.GetString(StateKey, "");
            if (json.Length > 0) { try { state = JsonUtility.FromJson<EState>(json); } catch { state = null; } }
            if (state?.active == true) LoadLogs();
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += Changed;
            Application.logMessageReceived += Logged;
        }

        public static string Run(string command)
        {
            var a = (command ?? "").Split(':');
            switch (a[0])
            {
                case "audit": return Audit(a.Length > 1 ? a[1] : "main");
                case "start": return StartRun(a.Length > 1 && a[1] == "full");
                case "status": return Status();
                case "report": return state == null ? "none" : Path.Combine(RunFolder, "report.json");
                case "abort": return Abort();
                default: return "refused: Escort303Regression audit[:main|folklore298|architecture296] | start[:full] | status | report | abort";
            }
        }

        static void Persist() { if (state != null) SessionState.SetString(StateKey, JsonUtility.ToJson(state)); }
        static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");
        static string Status()
        {
            if (state == null) return "{\"status\":\"IDLE\"}";
            return "{\"runId\":\"" + state.runId + "\",\"status\":\"" + state.status + "\",\"phase\":\"" + state.phase + "\",\"note\":\"" + Esc(state.phaseNote) + "\",\"active\":" + (state.active ? "true" : "false") +
                   ",\"final\":\"" + state.finalStatus + "\",\"pass\":" + state.steps.Count(x => x.status == "PASS") + ",\"fail\":" + state.steps.Count(x => x.status == "FAIL") + ",\"folder\":\"" + Esc(RunFolder) + "\"}";
        }

        static void Step(string id, bool ok, string detail) { state.steps.RemoveAll(x => x.id == id); state.steps.Add(new Check303 { id = id, status = ok ? "PASS" : "FAIL", detail = detail }); }
        static void Load(string id, bool ok, string detail) { state.load.Add(new Check303 { id = id, status = ok ? "PASS" : "FAIL", detail = detail }); }
        static void Fail(string why) { if (state == null) return; state.errors.Add("[" + state.phase + "] " + why); state.phaseNote = why; }

        static string StartRun(bool full)
        {
            if (state?.active == true) return "refused: run " + state.runId + " still active (" + state.status + ")";
            if (Commission303CombatActive()) return "refused: a Commission303Combat run is active";
            string auditFile = Path.Combine(Folder, "escort-audit.json");
            if (!File.Exists(auditFile)) return "refused: run Escort303Regression audit first (GE0)";
            AuditReport audit; try { audit = JsonUtility.FromJson<AuditReport>(File.ReadAllText(auditFile)); } catch (Exception e) { return "refused: unreadable audit: " + e.Message; }
            if (audit == null || !audit.gateGE0) return "refused: GE0 not met (A1/A2 failed in " + auditFile + "); apply Escort303Fix first, then audit again";
            var iso = new Isolation303();
            string refusal = Harness303.Prepare(iso, Harness303.MainScene, Harness303.MainSlot, "_c303escort_");
            if (refusal != null) return refusal;
            if (audit.sceneSha != iso.sceneSha) return "refused: the main scene changed since the audit; audit again";
            var s = Harness303.Session;
            if (Route(SceneManager.GetActiveScene()) == null || s.DemoEscortSeat == null || s.DemoEscortSummon == null || s.DemoEscortCompanion == null) return "refused: escort wiring missing in the main scene";
            state = new EState { active = true, full = full, runId = Harness303.UtcStamp(), status = "STARTING", iso = iso, began = EditorApplication.timeSinceStartup };
            ResetLogs();
            Directory.CreateDirectory(RunFolder); iso.runFolder = RunFolder;
            Harness303.Apply(iso, s);
            state.fixtures.Add("isolated save " + iso.suffix + " (session.TestSaveSuffix, SessionState PlaytestUiReviewSuffix, direct Play)");
            Harness303.FocusGameView();
            state.status = "ENTERING"; state.phaseAt = EditorApplication.timeSinceStartup; Persist();
            EditorApplication.EnterPlaymode();
            return "started " + state.runId + (full ? " (full)" : "") + " suffix=" + iso.suffix + " folder=" + RunFolder;
        }

        static bool Commission303CombatActive()
        {
            var json = SessionState.GetString("Commission303Combat.State", "");
            return json.Contains("\"active\":true");
        }

        static void Tick()
        {
            if (state?.active != true || EditorApplication.isCompiling) return;
            double now = EditorApplication.timeSinceStartup;
            if (state.status == "RESTART_PENDING")
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isUpdating || now - state.phaseAt < .75) return;
                var s = Harness303.Session; if (s == null) { Fail("restart: no session"); CleanupAndReport(); return; }
                Harness303.Apply(state.iso, s); Harness303.FocusGameView();
                state.status = "ENTERING"; state.phaseAt = now; Persist(); EditorApplication.EnterPlaymode(); return;
            }
            if (state.status == "CLEANUP_PENDING" && !EditorApplication.isPlayingOrWillChangePlaymode) { CleanupAndReport(); return; }
            if (!EditorApplication.isPlaying || state.status != "RUNNING") return;
            if (now - state.began > (state.full ? 1800 : 1500)) { Fail("overall deadline exceeded in " + state.phase); StopPlay(false); return; }
            bool focused = Harness303.Focused;
            if (!state.focusReady)
            {
                if (focused) { state.focusReady = true; Persist(); return; }
                if (now - state.enteredAt > 10) { state.interrupted = true; state.interruptReason = "focus not acquired in 10 s"; Fail(state.interruptReason); StopPlay(false); }
                return;
            }
            if (!focused)
            {
                if (state.focusLossAt < 0) state.focusLossAt = now;
                if (now - state.focusLossAt >= 2) { state.interrupted = true; state.interruptReason = "focus lost for 2 s in " + state.phase; Fail(state.interruptReason); StopPlay(false); }
                return;
            }
            state.focusLossAt = -1;
        }

        static void Changed(PlayModeStateChange mode)
        {
            if (state?.active != true) return;
            if (mode == PlayModeStateChange.EnteredPlayMode)
            {
                state.boots++; state.status = "RUNNING"; state.enteredAt = EditorApplication.timeSinceStartup; state.focusReady = false; state.focusLossAt = -1;
                state.uiTidied = false; state.inputStarted = false; state.lastFrame = -1;
                EnterPhase(state.boots == 1 ? "E1" : "E12");
                RuntimeReset();
                probeObject = new GameObject("Escort303 probe") { hideFlags = HideFlags.HideAndDontSave };
                probeObject.AddComponent<Probe303>();
                Probe303.Late = Late;
                Harness303.FocusGameView(); Persist(); return;
            }
            if (mode == PlayModeStateChange.ExitingPlayMode) { Unbind(); EndInput(); if (Probe303.Late == (Action)Late) Probe303.Late = null; if (probeObject != null) Object.DestroyImmediate(probeObject); probeObject = null; return; }
            if (mode != PlayModeStateChange.EnteredEditMode) return;
            if (state.restartRequested && !state.interrupted) { state.restartRequested = false; state.status = "RESTART_PENDING"; state.phaseAt = EditorApplication.timeSinceStartup; Persist(); return; }
            state.status = "CLEANUP_PENDING"; Persist(); CleanupAndReport();
        }

        static void StopPlay(bool restart)
        {
            Unbind(); EndInput();
            if (Probe303.Late == (Action)Late) Probe303.Late = null;
            // Stop can run inside the probe's own LateUpdate: destroy deferred while playing
            if (probeObject != null) { if (EditorApplication.isPlaying) Object.Destroy(probeObject); else Object.DestroyImmediate(probeObject); }
            probeObject = null;
            state.restartRequested = restart; state.status = restart ? "RESTARTING" : "STOPPING";
            FlushLogs(); Persist();
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); else { state.status = "CLEANUP_PENDING"; Persist(); }
        }

        static string Abort()
        {
            if (state?.active != true) return "no active run";
            Fail("explicit abort in " + state.phase);
            if (EditorApplication.isPlaying) { StopPlay(false); return "aborting " + state.runId; }
            CleanupAndReport(); return "aborted " + state.runId;
        }

        static void EndInput()
        {
            if (!VirtualInput303.Active) return;
            string r = VirtualInput303.End(out bool ok);
            state.notes.Add(r); if (!ok) state.findings.Add(r);
        }

        static void Logged(string message, string stack, LogType type)
        {
            if (state?.active != true || !EditorApplication.isPlaying || state.status != "RUNNING") return;
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            var s = Harness303.Session; if (s == null || !s.InitializationComplete) return;
            state.logErrors++; if (state.errors.Count < 40) state.errors.Add("[log " + type + " in " + state.phase + "] " + message);
        }

        static void CleanupAndReport()
        {
            if (state == null) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { state.status = "CLEANUP_PENDING"; Persist(); return; }
            FlushLogs();
            state.cleanup = Harness303.Cleanup(state.iso, RunFolder);
            Step("E13", state.cleanup.All(c => c.status != "FAIL"), string.Join("; ", state.cleanup.Select(c => c.status + " " + c.id + ": " + c.detail)));
            Step("E-log", state.logErrors == 0, state.logErrors + " Exception/Error/Assert log(s) during Play");
            var required = new[] { "E1", "E2", "E3", "E4", "E5", "E6", "E7", "E9", "E10", "E12", "E13" }.ToList();
            if (state.full) required.Add("E11");
            foreach (var id in required) if (!state.steps.Any(x => x.id == id)) state.steps.Add(new Check303 { id = id, status = "FAIL", detail = "not reached" });
            bool regression = state.findings.Any(f => f.StartsWith("#303 REGRESSION", StringComparison.Ordinal));
            if (state.interrupted) state.finalStatus = "INTERRUPTED_FOCUS";
            else if (regression) state.finalStatus = "FAIL (#303 regression)";
            else if (state.steps.Any(x => x.status == "FAIL")) state.finalStatus = "FAIL";
            else if (state.load.Any(x => x.status == "FAIL") || state.findings.Count > 0) state.finalStatus = "PASS WITH CONDITIONS";
            else state.finalStatus = "PASS (automated real input)";
            state.active = false; state.status = "DONE";
            WriteReport(); Persist();
            EditorApplication.delayCall += () => Harness303.FocusPrior(state?.iso);
        }

        [Serializable] sealed class Report
        {
            public string runId, finalStatus, disclaimer, scene, suffix, savedPath, scope; public bool full; public int boots, logErrors;
            public List<Check303> steps, loadChecks, cleanup; public List<string> realInputs, fixtures, findings, errors, notes, uiAdapter;
        }
        static void WriteReport()
        {
            Harness303.WriteJson(RunFolder, "report.json", new Report
            {
                runId = state.runId, finalStatus = state.finalStatus, disclaimer = Commission303Combat.Disclaimer, scene = state.iso.scenePath, suffix = state.iso.suffix, savedPath = state.iso.savedPath,
                full = state.full, boots = state.boots, logErrors = state.logErrors, steps = state.steps.OrderBy(x => x.id, StringComparer.Ordinal).ToList(), loadChecks = state.load, cleanup = state.cleanup,
                realInputs = state.realInputs, fixtures = state.fixtures, findings = state.findings, errors = state.errors, notes = state.notes, uiAdapter = UiAdapter303.Log.ToList(),
                scope = "Driving = virtual W/A/D/Space through the seat's own Keyboard.current reads; walking = WASD + mouse look; G/E/F = virtual keys. Blocks are classified by the blocking collider's hierarchy: Roadside303/… = #303 regression, anything else = pre-existing finding. Walk-by corridors (E8, walk-by 2) are load checks reported separately. The API driver comparison (CompactEscortDriver252) only runs when keyboard driving fails.",
            });
        }
    }
}
