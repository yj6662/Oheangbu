using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // E1..E13 (plan §10.2) as short task scripts run from Probe303.LateUpdate. Core phases fail the run; the walk-by phases
    // (E8, E9b) are load checks recorded separately. Blocks and near contacts are classified by collider hierarchy.
    public static partial class Escort303Regression
    {
        // ------------------------------------------------------------------ logs
        [Serializable] sealed class ERow
        {
            public float t; public int frame; public string phase = "", keys = "", presenter = "", issue = "", blocking = "", stage = "", mode = "";
            public Vector3 player, car, companion, blockedPoint; public float carSpeed, carRoadside = -1, companionRoadside = -1; public bool seated, cargo; public int inspections;
        }
        [Serializable] sealed class DriveReceipt
        {
            public string leg = "", mode = "", status = "", detail = ""; public float seconds, metres, maxTilt; public Vector3 end;
            public List<Vector3> trail = new List<Vector3>(); public List<string> contacts = new List<string>(); public int keyFrames;
        }
        [Serializable] sealed class ELogs
        {
            public List<ERow> timeline = new List<ERow>(); public List<DriveReceipt> drives = new List<DriveReceipt>();
            public List<InteractionRow303> interactions = new List<InteractionRow303>(); public List<string> blocks = new List<string>();
        }
        [Serializable] sealed class ListFile<T> { public List<T> rows; }
        static ELogs logs = new ELogs();
        static void ResetLogs() { logs = new ELogs(); UiAdapter303.Reset(); }
        static void LoadLogs()
        {
            try { string f = Path.Combine(RunFolder, "logs-all.json"); logs = File.Exists(f) ? JsonUtility.FromJson<ELogs>(File.ReadAllText(f)) ?? new ELogs() : new ELogs(); }
            catch { logs = new ELogs(); }
        }
        static void FlushLogs()
        {
            if (state == null || string.IsNullOrEmpty(state.runId)) return;
            try
            {
                Directory.CreateDirectory(RunFolder);
                Harness303.WriteJson(RunFolder, "escort_timeline.json", new ListFile<ERow> { rows = logs.timeline });
                Harness303.WriteJson(RunFolder, "interactions.json", new ListFile<InteractionRow303> { rows = logs.interactions });
                foreach (var g in logs.drives.GroupBy(d => d.leg)) Harness303.WriteJson(RunFolder, "drive-" + g.Key + ".json", new ListFile<DriveReceipt> { rows = g.ToList() });
                File.WriteAllText(Path.Combine(RunFolder, "logs-all.json"), JsonUtility.ToJson(logs));
                Harness303.WriteJson(RunFolder, "state.json", state);
            }
            catch (Exception e) { state.errors.Add("log flush: " + e.Message); }
        }

        // ------------------------------------------------------------------ tasks
        static Vector2 Center => new Vector2(Screen.width * .5f, Screen.height * .5f);
        static double Now => EditorApplication.timeSinceStartup;
        abstract class T303
        {
            public string Status = "running", Detail = "", Label = "";
            public virtual void Begin(WorldMacroPlaytestSession s) { }
            public abstract InputFrame303 Tick(WorldMacroPlaytestSession s);
            protected static InputFrame303 N => InputFrame303.Neutral(Center);
            protected void Done(string d = null) { Status = "done"; if (d != null) Detail = d; }
            protected void Failed(string d) { Status = "failed"; Detail = Label + ": " + d; }
        }
        sealed class DoT : T303
        {
            readonly Func<WorldMacroPlaytestSession, string> act;
            public DoT(string label, Func<WorldMacroPlaytestSession, string> a) { Label = label; act = a; }
            public override void Begin(WorldMacroPlaytestSession s) { string r = act(s); if (r == null) Done(); else Failed(r); }
            public override InputFrame303 Tick(WorldMacroPlaytestSession s) => N;
        }
        sealed class UntilT : T303
        {
            readonly Func<WorldMacroPlaytestSession, bool> ok; readonly Func<WorldMacroPlaytestSession, string> failNow, why; readonly float timeout; double at;
            public Func<WorldMacroPlaytestSession, InputFrame303> Hold;
            public UntilT(string label, Func<WorldMacroPlaytestSession, bool> ok, float timeout, Func<WorldMacroPlaytestSession, string> why = null, Func<WorldMacroPlaytestSession, string> failNow = null)
            { Label = label; this.ok = ok; this.timeout = timeout; this.why = why; this.failNow = failNow; }
            public override void Begin(WorldMacroPlaytestSession s) { at = Now; }
            public override InputFrame303 Tick(WorldMacroPlaytestSession s)
            {
                if (ok(s)) { Done(); return N; }
                string f = failNow?.Invoke(s); if (!string.IsNullOrEmpty(f)) { Failed(f); return N; }
                if (Now - at > timeout) Failed("timeout " + timeout + " s" + (why != null ? " (" + why(s) + ")" : ""));
                return Hold != null ? Hold(s) : N;
            }
        }
        sealed class WaitT : T303
        {
            readonly float seconds; double at;
            public WaitT(float seconds) { Label = "wait " + seconds; this.seconds = seconds; }
            public override void Begin(WorldMacroPlaytestSession s) { at = Now; }
            public override InputFrame303 Tick(WorldMacroPlaytestSession s) { if (Now - at >= seconds) Done(); return N; }
        }
        sealed class WalkT : T303
        {
            readonly Func<WorldMacroPlaytestSession, Vector3> goal; readonly float arrive, limit; readonly Walker303 w = new Walker303();
            public WalkT(string label, Func<WorldMacroPlaytestSession, Vector3> goal, float arrive = .5f, float limit = 60f) { Label = label; this.goal = goal; this.arrive = arrive; this.limit = limit; }
            public override void Begin(WorldMacroPlaytestSession s)
            {
                var g = goal(s);
                if (Harness303.Flat(s.Walker.Body.transform.position, g) <= arrive) { Done("already there"); return; }
                if (!w.Begin(s, g, arrive, limit, Label)) Failed(w.Detail);
            }
            public override InputFrame303 Tick(WorldMacroPlaytestSession s)
            {
                var f = w.Tick(s, Center);
                if (w.Status == "arrived") { Done("walked " + Harness303.F(w.Walked, "F1") + " m"); RecordReal("walk " + Label + " " + Harness303.F(w.Walked, "F0") + " m"); }
                else if (w.Status == "failed") Failed(w.Detail + " " + w.LastBlock);
                return f;
            }
        }
        sealed class KeysT : T303
        {
            readonly Key key; readonly int frames; readonly Key[] with;
            public KeysT(Key key, int frames = 2, params Key[] with) { Label = "key " + key; this.key = key; this.frames = frames; this.with = with; }
            public override void Begin(WorldMacroPlaytestSession s) { VirtualInput303.Tap(key, Center, frames, with); RecordReal("key " + key); }
            public override InputFrame303 Tick(WorldMacroPlaytestSession s)
            {
                if (VirtualInput303.Pending == 0 && (VirtualInput303.LastFed.Keys == null || VirtualInput303.LastFed.Keys.Length == 0)) Done();
                return N;
            }
        }
        sealed class FaceT : T303
        {
            readonly Func<WorldMacroPlaytestSession, Vector3> point; int good; double at;
            public FaceT(string label, Func<WorldMacroPlaytestSession, Vector3> point) { Label = label; this.point = point; }
            public override void Begin(WorldMacroPlaytestSession s) { at = Now; }
            public override InputFrame303 Tick(WorldMacroPlaytestSession s)
            {
                var f = N; var p = point(s);
                float err = Mathf.DeltaAngle(Steer303.CameraYaw(s), Harness303.YawTo(s.Walker.Body.transform.position, p));
                f.Delta = Steer303.Level(s, err, 5f);
                good = Mathf.Abs(err) < 3f ? good + 1 : 0;
                if (good >= 3 || Now - at > 6) Done(good >= 3 ? null : "facing within " + Harness303.F(err, "F1") + "°");
                return f;
            }
        }
        sealed class TalkT : T303
        {
            readonly string id, capture; readonly Func<WorldMacroPlaytestSession, Transform> npc; readonly Func<WorldMacroPlaytestSession, Vector3?> live;
            readonly Func<WorldMacroPlaytestSession, InteractionRow303, string> after; readonly Talk303 t = new Talk303();
            public TalkT(string id, string capture, Func<WorldMacroPlaytestSession, InteractionRow303, string> after = null, Func<WorldMacroPlaytestSession, Vector3?> live = null, Func<WorldMacroPlaytestSession, Transform> npc = null)
            { Label = "F " + id; this.id = id; this.capture = capture; this.after = after; this.live = live; this.npc = npc; }
            public override void Begin(WorldMacroPlaytestSession s) { t.WaitRestPresentation = true; t.Begin(s, state.phase, id, npc?.Invoke(s), null, RunFolder, capture, live?.Invoke(s)); }
            public override InputFrame303 Tick(WorldMacroPlaytestSession s)
            {
                var f = t.Tick(Center);
                if (t.Status == "running") return f;
                logs.interactions.Add(t.Row); state.fixtures.AddRange(t.Fixtures);
                if (t.Row.details > 1 || t.Row.resolved > 1) state.findings.Add(state.phase + " " + id + ": duplicate interaction events (details " + t.Row.details + ", resolved " + t.Row.resolved + ")");
                if (t.Status != "done") { Failed(t.Row.detail); return f; }
                if (!t.Row.focusMatched) { Failed("focus at F was '" + t.Row.focusedAtPress + "'"); return f; }
                RecordReal("F " + id + " through the real focus loop");
                string problem = after?.Invoke(s, t.Row);
                if (problem != null) Failed(problem); else Done("close " + t.Row.closeMethod);
                return f;
            }
            public void End() => t.End();
        }

        /// <summary>Keyboard driving along a leg: W duty to a target speed (Lerp 5→2 m/s by heading error, slower in the last
        /// 10 m), A/D duty = |angle|/26 over a 4-frame window, Space to brake (CompactEscortDriver252 law). On a keyboard failure
        /// the same leg runs once with CompactEscortDriver252 (API input) to separate control from a blocked road.</summary>
        sealed class DriveT : T303
        {
            readonly int index; Vector3[] path; int next, window; double started, lastReal; float stalled, metres, nextSample; Vector3 prior;
            DriveReceipt receipt; bool api; CompactEscortDriver252 driver; string apiFile; bool midCaptured;
            public string KeyboardFailure;
            public DriveT(int legIndex) { index = legIndex; Label = "drive " + LegTable[legIndex].id; }
            public override void Begin(WorldMacroPlaytestSession s)
            {
                var route = Route(s.gameObject.scene);
                if (!TryLeg(s, route, index, out var leg, out var why)) { Failed(why); return; }
                path = leg.points; next = 1; started = lastReal = Now; prior = s.DemoEscortSeat.Vehicle.transform.position;
                receipt = new DriveReceipt { leg = leg.id, mode = "keyboard" }; logs.drives.Add(receipt);
                RecordReal("keyboard driving " + leg.id + " (seat reads Keyboard.current W/A/D/Space)");
            }
            public override InputFrame303 Tick(WorldMacroPlaytestSession s)
            {
                var f = N;
                var seat = s.DemoEscortSeat; var car = seat.Vehicle; var pos = car.transform.position;
                float dt = (float)(Now - lastReal); lastReal = Now;
                if (api)
                {
                    if (driver != null) return f;
                    var r = File.Exists(apiFile) ? JsonUtility.FromJson<ApiReceipt>(File.ReadAllText(apiFile)) : null;
                    var apiRow = new DriveReceipt { leg = receipt.leg, mode = "api (CompactEscortDriver252)", status = r?.status ?? "MISSING", detail = r?.detail ?? "", seconds = r?.seconds ?? 0, metres = r?.metres ?? 0, end = r?.end ?? Vector3.zero };
                    logs.drives.Add(apiRow);
                    if (apiRow.status == "PASS") { Done("keyboard FAIL (" + KeyboardFailure + "), API driver PASS → control problem, road passable"); state.findings.Add(Label + ": keyboard control failed where the API driver passed: " + KeyboardFailure); }
                    else Failed("keyboard FAIL (" + KeyboardFailure + ") and API driver " + apiRow.status + " " + apiRow.detail + " → road blocked or vehicle issue");
                    return f;
                }
                if (!seat.Occupied) { KeyFail(s, "seat ownership lost"); return f; }
                if (s.GameplayInputBlocked) return f;
                float moved = Vector3.Distance(pos, prior); metres += moved; prior = pos;
                stalled = car.Speed < .05f ? stalled + dt : 0;
                float tilt = Vector3.Angle(car.transform.up, Vector3.up); receipt.maxTilt = Mathf.Max(receipt.maxTilt, tilt);
                Contacts(s, receipt);
                if (Now - started > 300) { KeyFail(s, "300 s leg limit"); return f; }
                if (stalled > 12f) { KeyFail(s, "stalled 12 s at " + Harness303.V(pos) + ContactText(receipt)); return f; }
                if (tilt > 35f) { KeyFail(s, "tilt " + Harness303.F(tilt, "F0") + "°"); return f; }
                while (next < path.Length - 1 && Harness303.Flat(path[next], pos) < 6f) next++;
                float end = Harness303.Flat(path[path.Length - 1], pos);
                bool arrival = end < 3.2f && next >= path.Length - 1;
                if (!midCaptured && metres > 0 && next >= path.Length / 2) { midCaptured = true; Harness303.Capture(RunFolder, index == 0 ? "E04_leg1_mid" : index == 1 ? "E09_leg2_hamlet" : "E11b_leg3_mid"); }
                if (Time.time >= nextSample) { nextSample = Time.time + .2f; receipt.trail.Add(pos); }
                if (arrival && car.Speed < .15f) { receipt.status = "PASS"; receipt.detail = "arrived"; receipt.seconds = (float)(Now - started); receipt.metres = metres; receipt.end = pos; Done("arrived in " + Harness303.F(receipt.seconds, "F1") + " s, " + Harness303.F(metres, "F1") + " m"); return f; }
                var delta = Vector3.ProjectOnPlane(path[next] - pos, Vector3.up);
                float angle = Vector3.SignedAngle(Vector3.ProjectOnPlane(car.transform.forward, Vector3.up), delta, Vector3.up);
                float target = Mathf.Lerp(5f, 2f, Mathf.Clamp01(Mathf.Abs(angle) / 65f));
                if (end < 10f && next >= path.Length - 1) target = Mathf.Min(target, Mathf.Max(.7f, end * .35f));
                var keys = new List<Key>();
                if (!arrival && car.Speed < target) keys.Add(Key.W);
                if (arrival || car.Speed > target + 1f) keys.Add(Key.Space);
                int on = Mathf.RoundToInt(Mathf.Clamp01(Mathf.Abs(angle) / 26f) * 4f);
                if (window < on) keys.Add(angle > 0 ? Key.D : Key.A);
                window = (window + 1) % 4;
                f.Keys = keys.ToArray(); f.Tag = "drive"; if (keys.Count > 0) receipt.keyFrames++;
                return f;
            }
            void KeyFail(WorldMacroPlaytestSession s, string why)
            {
                KeyboardFailure = why; receipt.status = "FAIL"; receipt.detail = why; receipt.seconds = (float)(Now - started); receipt.metres = metres; receipt.end = s.DemoEscortSeat.Vehicle.transform.position;
                if (!s.DemoEscortSeat.Occupied || Object.FindFirstObjectByType<CompactEscortDriver252>() != null) { Failed("keyboard " + why + " (API comparison not possible)"); return; }
                apiFile = Path.Combine(RunFolder, "drive-" + receipt.leg + "-api-raw.json"); if (File.Exists(apiFile)) File.Delete(apiFile);
                driver = new GameObject("TemporaryEscort303ApiDriver").AddComponent<CompactEscortDriver252>();
                var rest = path.Skip(Math.Max(0, next - 1)).ToArray();
                driver.Begin(s, rest.Length >= 2 ? rest : path, apiFile);
                state.fixtures.Add(Label + ": API driver comparison (CompactEscortDriver252.SetDriverInput) after keyboard failure: " + why);
                api = true;
            }
        }
        [Serializable] sealed class ApiReceipt { public string status, detail; public float seconds, metres; public Vector3 end; }

        static void Contacts(WorldMacroPlaytestSession s, DriveReceipt receipt)
        {
            var hull = s.DemoEscortSeat.Vehicle.Hull; if (hull == null) return;
            var centre = hull.transform.TransformPoint(hull.center);
            var half = Vector3.Scale(hull.size, hull.transform.lossyScale) * .5f + Vector3.one * .2f;
            foreach (var c in Physics.OverlapBox(centre, half, hull.transform.rotation, ~0, QueryTriggerInteraction.Ignore))
                if (UnderRoadside(c.transform)) { string p = Harness303.PathOf(c.transform); if (!receipt.contacts.Contains(p)) receipt.contacts.Add(p); }
        }
        static string ContactText(DriveReceipt r) => r.contacts.Count == 0 ? "" : "; Roadside303 within 0.2 m: " + string.Join(", ", r.contacts.Take(4));

        /// <summary>Walk to the seat's safe exit, face the seat, E (real key), wait for the save-confirmed boarding; one retry.</summary>
        sealed class BoardT : T303
        {
            int stage, tries; double at; readonly Walker303 w = new Walker303();
            public BoardT() { Label = "board (F)"; }
            public override void Begin(WorldMacroPlaytestSession s)
            {
                if (!s.DemoEscortSeat.TryGetSafeExit(out var feet)) { Failed("no safe entry ground: " + s.DemoEscortSeat.LastInteraction); return; }
                if (Harness303.Flat(s.Walker.Body.transform.position, feet) > .5f && !w.Begin(s, feet, .45f, 40f, "seat")) { Failed(w.Detail); return; }
                stage = Harness303.Flat(s.Walker.Body.transform.position, feet) > .5f ? 0 : 1; at = Now;
            }
            public override InputFrame303 Tick(WorldMacroPlaytestSession s)
            {
                var seat = s.DemoEscortSeat; var f = N;
                if (stage == 0) { f = w.Tick(s, Center); if (w.Status == "arrived") { stage = 1; at = Now; } else if (w.Status == "failed") Failed("walk to seat: " + w.Detail); return f; }
                if (stage == 1)
                {
                    float err = Mathf.DeltaAngle(Steer303.CameraYaw(s), Harness303.YawTo(s.Walker.Body.transform.position, seat.SeatSocket.position));
                    f.Delta = Steer303.Level(s, err, 10f);
                    if ((Mathf.Abs(err) < 5f && seat.CanBoard) || Now - at > 3) { VirtualInput303.Tap(Key.F, Center, 2); tries++; stage = 2; at = Now; RecordReal("F board"); }   // #308 D308-8: F is the one car key (board / exit)
                    return f;
                }
                var snap = s.EscortSnapshot;
                bool boarded = seat.Occupied && snap != null && snap.Stage >= DemoEscortStage.Escorting && snap.CompanionMode == DemoEscortCompanionMode.Riding &&
                               s.DemoEscortCompanion.IsChildOf(s.DemoEscortPassengerSocket) && s.DemoEscortCargo.IsChildOf(s.DemoEscortCargoSocket);
                if (boarded) { var disk = Harness303.ReadDisk(state.iso.savedPath); if (disk != null && disk.escort.Stage == snap.Stage) Done("stage " + snap.Stage + " on disk"); else Failed("disk stage " + (disk != null ? disk.escort.Stage.ToString() : "unreadable") + " vs " + snap.Stage); return f; }
                if (Now - at > 6)
                {
                    if (tries < 2) { if (seat.Occupied) { VirtualInput303.Tap(Key.F, Center, 2); } stage = 1; at = Now; return f; }
                    var p = Presenter(s); Failed("not boarded: seat " + seat.Occupied + " '" + seat.LastInteraction + "' presenter " + (p != null ? p.State + " '" + p.LastIssue + "'" : "none") + " stage " + (snap != null ? snap.Stage + "/" + snap.CompanionMode : "?"));
                }
                return f;
            }
        }

        sealed class ExitT : T303
        {
            int stage, tries; double at;
            public ExitT() { Label = "exit (F)"; }
            public override void Begin(WorldMacroPlaytestSession s) { at = Now; }
            public override InputFrame303 Tick(WorldMacroPlaytestSession s)
            {
                var seat = s.DemoEscortSeat; var f = N;
                if (stage == 0) { if (seat.Vehicle.Speed < .15f || Now - at > 5) { VirtualInput303.Tap(Key.F, Center, 2); tries++; stage = 1; at = Now; RecordReal("F exit"); } else f.Keys = new[] { Key.Space }; return f; }
                var snap = s.EscortSnapshot;
                bool exited = !seat.Occupied && !s.Walker.Seated && snap != null && snap.CompanionMode != DemoEscortCompanionMode.Riding && !s.DemoEscortCompanion.IsChildOf(seat.Vehicle.transform);
                if (exited)
                {
                    var pv = s.Walker.Body.GetComponent<PlayerVitals>();
                    if (pv.LastEnvironmentDeath != EnvironmentDeathCause.None) { Failed("false environment death " + pv.LastEnvironmentDeath + " after exit"); return f; }
                    Done("exit at " + Harness303.V(s.Walker.Body.transform.position)); return f;
                }
                if (Now - at > 8) { if (tries < 2) { stage = 0; at = Now; return f; } Failed("not exited: '" + seat.LastInteraction + "' mode " + (snap != null ? snap.CompanionMode.ToString() : "?")); }
                return f;
            }
        }

        /// <summary>G (real key) and wait for the call gesture to materialize the car; a refusal right after a new Play (body not
        /// settled, #252 record) is retried once after 2 s.</summary>
        sealed class SummonT : T303
        {
            int calls, stage; double at; readonly Func<WorldMacroPlaytestSession, Vector3> face;
            public SummonT(Func<WorldMacroPlaytestSession, Vector3> face) { Label = "summon (G)"; this.face = face; }
            public override void Begin(WorldMacroPlaytestSession s) { calls = s.DemoEscortSummon.SuccessfulCalls; at = Now; }
            public override InputFrame303 Tick(WorldMacroPlaytestSession s)
            {
                var call = s.DemoEscortSummon; var f = N;
                if (stage == 0)
                {
                    float err = Mathf.DeltaAngle(Steer303.CameraYaw(s), Harness303.YawTo(s.Walker.Body.transform.position, face(s)));
                    f.Delta = Steer303.Level(s, err, 5f);
                    if (Mathf.Abs(err) < 3f || Now - at > 4) { VirtualInput303.Tap(Key.G, Center, 2); stage = 1; at = Now; RecordReal("G summon"); }
                    return f;
                }
                if (call.SuccessfulCalls > calls && !call.Calling && !call.IsRecalled && call.Vehicle.gameObject.activeInHierarchy) { Done("car called: " + call.LastResult); return f; }
                if (stage == 1 && !call.Calling && Now - at > 2.5)
                {
                    state.notes.Add("summon refused once: '" + call.LastResult + "' — retry after 2 s"); stage = 2; at = Now; return f;
                }
                if (stage == 2 && Now - at > 2) { VirtualInput303.Tap(Key.G, Center, 2); stage = 3; at = Now; return f; }
                if (stage == 3 && !call.Calling && Now - at > 3) Failed("summon: " + call.LastResult + " / " + call.LastPlacementDiagnostic);
                if (Now - at > 12) Failed("summon timeout: " + call.LastResult);
                return f;
            }
        }

        /// <summary>A page a fixture left open is closed the way Talk303 closes a talk page: F (real key) up to 6 times 0.3 s apart,
        /// then Escape, then the adapter (recorded as a fixture); three unblocked neutral frames end it. No page within 1 s = nothing
        /// to close. Used after the 청룡 defeat, whose 석경 folio pauses the game.</summary>
        sealed class ClosePageT : T303
        {
            int step, quiet; double at, last; string how = "";
            public ClosePageT(string label) { Label = label; }
            public override void Begin(WorldMacroPlaytestSession s) { at = last = Now; }
            public override InputFrame303 Tick(WorldMacroPlaytestSession s)
            {
                bool idle = VirtualInput303.Pending == 0 && (VirtualInput303.LastFed.Keys == null || VirtualInput303.LastFed.Keys.Length == 0);
                if (UiAdapter303.Page.Length == 0 && !s.GameplayInputBlocked)
                {
                    if (step == 0 && Now - at < 1) return N;   // a page that opens a frame late is still met
                    quiet = idle ? quiet + 1 : 0;
                    if (quiet >= 3) Done(step == 0 ? "no page was open" : "closed with " + how);
                    return N;
                }
                quiet = 0;
                if (!idle || Now - last < .3) return N;
                last = Now;
                if (step < 6) { VirtualInput303.Tap(Key.F, Center, 1); step++; how = "F x" + step; RecordReal("F closes the reading page"); return N; }
                if (step == 6) { VirtualInput303.Tap(Key.Escape, Center, 1); step++; how += " + Escape"; return N; }
                if (step == 7)
                {
                    string page = UiAdapter303.Page, a = UiAdapter303.CloseMenu(), b = UiAdapter303.ReleaseGate();
                    state.fixtures.Add(state.phase + " " + Label + ": adapter CloseMenu / ReleaseGate (" + a + ", " + b + ") after F x6 and Escape left page '" + page + "' open");
                    step++; how += " + adapter CloseMenu"; return N;
                }
                Failed("the page did not close (" + UiAdapter303.Describe() + ")");
                return N;
            }
        }

        // ------------------------------------------------------------------ runtime
        static List<Func<T303>> script = new List<Func<T303>>(); static int scriptIndex; static T303 task;
        static readonly HashSet<string> blockSeen = new HashSet<string>();
        static float lastSample; static string phaseSoft;
        static bool LoadPhase => state.phase == "E8" || state.phase == "E9b";

        static void RuntimeReset() { script = new List<Func<T303>>(); scriptIndex = 0; task = null; blockSeen.Clear(); lastSample = -1; phaseSoft = null; }
        static void Unbind() { (task as TalkT)?.End(); }
        static void RecordReal(string what) { if (state != null && !state.realInputs.Contains(state.phase + " " + what)) state.realInputs.Add(state.phase + " " + what); }
        static DemoEscortPresentation Presenter(WorldMacroPlaytestSession s) { var r = Route(s.gameObject.scene); return r != null ? r.GetComponent<DemoEscortPresentation>() : null; }
        static DemoEscortStop StopOf(WorldMacroPlaytestSession s, string id) => Stop(Route(s.gameObject.scene), id);
        static Vector3 Front(WorldMacroPlaytestSession s, string pointId, float back = 1.5f)
        {
            var p = s.Content.Points.FirstOrDefault(x => x.Id == pointId); if (p == null) return s.Walker.Body.transform.position;
            var dir = Vector3.ProjectOnPlane(s.Walker.Body.transform.position - p.Position, Vector3.up); if (dir.sqrMagnitude < .01f) dir = Vector3.back;
            var q = p.Position + dir.normalized * Mathf.Min(back, Mathf.Max(.6f, p.Radius - .6f));
            return NavMesh.SamplePosition(q, out var hit, 1.2f, NavMesh.AllAreas) ? hit.position : q;
        }

        static void EnterPhase(string phase)
        {
            if (state.phase != phase) state.notes.Add(phase + " at " + Harness303.F((float)(Now - state.began), "F1") + " s");
            state.phase = phase; state.phaseNote = ""; state.phaseAt = Now; phaseSoft = null;
            (task as TalkT)?.End(); task = null; scriptIndex = 0; script = new List<Func<T303>>();
            FlushLogs(); Persist();
        }

        static float Limit(string phase)
        {
            switch (phase)
            {
                case "E1": return 90; case "E2": return 90; case "E3": return 60; case "E4": return 20; case "E5": return 20; case "E6": return 760;
                case "E7": return 60; case "E8": return 60; case "E11": return 60; case "E9": return 800; case "E9b": return 60; case "E9c": return 60;
                case "E10": return 800; case "E12": return 120; default: return 0;
            }
        }

        static void Late()
        {
            if (state?.active != true || state.status != "RUNNING" || !EditorApplication.isPlaying) return;
            if (state.lastFrame == Time.frameCount) return; state.lastFrame = Time.frameCount;
            VirtualInput303.Hold = InputFrame303.Neutral(Center);
            try
            {
                float limit = Limit(state.phase);
                if (limit > 0 && Now - state.phaseAt > limit) { PhaseFailed(state.phase + " time limit " + limit + " s" + (task != null ? " in " + task.Label : "")); return; }
                var s = Harness303.Session; if (s == null) return;
                if (s.TestSaveSuffix != state.iso.suffix) { PhaseFailed("isolated suffix lost ('" + s.TestSaveSuffix + "') — refusing to continue on a non-isolated save"); return; }
                if (!Boot(s)) return;
                Monitor(s);
                if (script.Count == 0 && scriptIndex == 0) BuildScript(s);
                if (task == null)
                {
                    if (scriptIndex >= script.Count) { PhaseDone(s); return; }
                    task = script[scriptIndex](); task.Begin(s);
                }
                if (task.Status == "running") VirtualInput303.Hold = task.Tick(s);
                if (task.Status == "done") { if (!string.IsNullOrEmpty(task.Detail)) state.notes.Add(state.phase + " " + task.Label + ": " + task.Detail); scriptIndex++; task = null; return; }
                if (task.Status == "failed") { if (LoadPhase) { Load(state.phase, false, task.Detail); scriptIndex = script.Count; task = null; PhaseDone(s); } else PhaseFailed(task.Detail); }
            }
            catch (Exception e) { Fail("harness exception in " + state.phase + ": " + e); StopPlay(false); }
        }

        static bool Boot(WorldMacroPlaytestSession s)
        {
            if (!s.InitializationComplete || UiAdapter303.LoadingInProgress == true || !state.focusReady) return false;
            if (!state.uiTidied)
            {
                string a = UiAdapter303.CloseMenu(), b = UiAdapter303.ReleaseGate();
                state.fixtures.Add("boot " + state.boots + ": one UI tidy before input — CloseMenu " + a + ", ReleaseGate " + b);
                state.uiTidied = true; return false;
            }
            if (!state.inputStarted)
            {
                var summary = VirtualInput303.Begin(new[] { Harness303.Field<InputActionAsset>(s.Walker.Motor, "_actions"), Harness303.Field<InputActionAsset>(s.Walker.Drawing, "_actions") });
                var cfg = Harness303.Field<CombatConfigSO>(s.Walker.Motor, "_config");
                float preference = s.Walker.Motor.RuntimeState != null ? s.Walker.Motor.RuntimeState.LookSensitivity : 1f;
                Steer303.Reset(cfg != null ? cfg.LookSensitivity * preference : Steer303.MeasuredPerUnit);
                if (state.boots == 1) state.realInputs.Add("virtual keyboard/mouse: " + summary);
                roadsideCache = RoadsideColliders(s.gameObject.scene);
                state.inputStarted = true; return false;
            }
            return true;
        }

        static void PhaseFailed(string why)
        {
            Step(state.phase, false, why); Fail(why);
            // a blocking collider seen during the failed phase decides the class
            StopPlay(false);
        }

        static void PhaseDone(WorldMacroPlaytestSession s)
        {
            string phase = state.phase;
            if (!LoadPhase) Step(phase, phaseSoft == null, phaseSoft ?? "ok");
            else if (!state.load.Any(x => x.id == phase)) Load(phase, true, "no Roadside303 block while walking by");
            string next = NextPhase(phase);
            if (next == null) { state.finished = true; StopPlay(false); return; }
            if (next == "RESTART") { StopPlay(true); return; }
            EnterPhase(next);
        }

        static string NextPhase(string phase)
        {
            switch (phase)
            {
                case "E1": return "E2"; case "E2": return "E3"; case "E3": return "E4"; case "E4": return "E5"; case "E5": return "E6"; case "E6": return "E7";
                case "E7": return "E8"; case "E8": return state.full ? "E11" : "E9"; case "E11": return "E9"; case "E9": return "E9b"; case "E9b": return "E9c";
                case "E9c": return "E10"; case "E10": return "E12pre"; case "E12pre": return "RESTART"; case "E12": return null;
                default: return null;
            }
        }

        /// <summary>Companion blocks and vehicle contacts every frame; Roadside303 = #303 regression (load phases: load finding).</summary>
        static void Monitor(WorldMacroPlaytestSession s)
        {
            var p = Presenter(s);
            if (p != null && p.State == DemoEscortPresentationState.Blocked && p.LastBlockingCollider != null)
            {
                string path = Harness303.PathOf(p.LastBlockingCollider.transform), key = state.phase + "|" + path;
                if (blockSeen.Add(key))
                {
                    string line = state.phase + ": companion blocked by " + path + " at " + Harness303.V(p.LastBlockedPoint) + " ('" + p.LastIssue + "')";
                    logs.blocks.Add(line);
                    if (UnderRoadside(p.LastBlockingCollider.transform)) { if (LoadPhase) Load(state.phase, false, line); else state.findings.Add("#303 REGRESSION: " + line); }
                    else state.findings.Add("pre-existing: " + line);
                }
            }
            if (Time.realtimeSinceStartup - lastSample < .2f) return;
            lastSample = Time.realtimeSinceStartup;
            var snap = s.EscortSnapshot; var car = s.DemoEscortSeat.Vehicle;
            var colliders = roadsideCache;
            logs.timeline.Add(new ERow
            {
                t = (float)(Now - state.began), frame = Time.frameCount, phase = state.phase, keys = VirtualInput303.LastFed.KeyText,
                player = s.Walker.Body.transform.position, car = car.transform.position, carSpeed = car.Speed, seated = s.DemoEscortSeat.Occupied,
                companion = s.DemoEscortCompanion.position, presenter = p != null ? p.State.ToString() : "", issue = p?.LastIssue ?? "",
                blocking = p != null && p.LastBlockingCollider != null ? Harness303.PathOf(p.LastBlockingCollider.transform) : "", blockedPoint = p != null ? p.LastBlockedPoint : default,
                cargo = p != null && p.CargoCarried, stage = snap?.Stage.ToString() ?? "", mode = snap?.CompanionMode.ToString() ?? "", inspections = snap?.InspectionsCleared ?? 0,
                carRoadside = Clearance(colliders, car.transform.position, 1.1f, out _), companionRoadside = Clearance(colliders, s.DemoEscortCompanion.position, .27f, out _),
            });
        }
            // ------------------------------------------------------------------ phase scripts
        sealed class IfT : T303
        {
            readonly Func<WorldMacroPlaytestSession, bool> cond; readonly Func<T303> make; T303 inner;
            public IfT(string label, Func<WorldMacroPlaytestSession, bool> cond, Func<T303> make) { Label = label; this.cond = cond; this.make = make; }
            public override void Begin(WorldMacroPlaytestSession s) { if (!cond(s)) { Done("not needed"); return; } inner = make(); inner.Begin(s); Sync(); }
            public override InputFrame303 Tick(WorldMacroPlaytestSession s) { if (inner == null) return N; var f = inner.Status == "running" ? inner.Tick(s) : N; Sync(); return f; }
            void Sync() { if (inner == null) return; Status = inner.Status; Detail = inner.Detail; }
        }

        static List<Collider> roadsideCache = new List<Collider>();
        static DriveT currentDrive;
        const string BossId = "cheongryong";

        static void BuildScript(WorldMacroPlaytestSession s)
        {
            var list = new List<Func<T303>>();
            PlayerVitals Pv() => s.Walker.Body.GetComponent<PlayerVitals>();
            bool CompanionNear(WorldMacroPlaytestSession x) => Vector3.Distance(x.DemoEscortCompanion.position, x.Walker.Body.transform.position) < 8f && Vector3.Distance(x.DemoEscortCargo.position, x.Walker.Body.transform.position) < 8f;
            string CompanionWhy(WorldMacroPlaytestSession x) { var p = Presenter(x); return "companion " + Harness303.F(Vector3.Distance(x.DemoEscortCompanion.position, x.Walker.Body.transform.position), "F1") + " m, presenter " + (p != null ? p.State + " '" + p.LastIssue + "'" : "none"); }
            bool CarNeeded(WorldMacroPlaytestSession x) { var car = x.DemoEscortSeat.Vehicle; return !car.gameObject.activeInHierarchy || x.DemoEscortSummon.IsRecalled || Harness303.Flat(car.transform.position, x.Walker.Body.transform.position) > 25f; }
            Func<T303> Capture(string name) => () => new DoT("capture " + name, x => { Harness303.Capture(RunFolder, name); return null; });
            Func<T303> Near(string id) => () => new DoT("approach " + id + " (fixture teleport)", x => TeleportNear(x, id));
            Func<T303> WaitCompanion() => () => new UntilT("companion and cargo within 8 m", CompanionNear, 30f, CompanionWhy);
            switch (state.phase)
            {
                case "E1":
                    list.Add(() => new DoT("isolated store, fresh load", x =>
                    {
                        string store = Harness303.StorePath(x);
                        return store != null && Path.GetFullPath(store) == Path.GetFullPath(state.iso.savedPath) && x.LoadStatus == "new" ? null : "store " + store + " LoadStatus " + x.LoadStatus;
                    }));
                    list.Add(() => new DoT("combat off (fixture)", x => { x.CombatActive = false; x.Cull(); state.fixtures.Add("E1 session.CombatActive = false (escort only, no ambient enemies; #252 precedent)"); return null; }));
                    list.Add(() => new WaitT(1f));
                    break;
                case "E2":
                    if (s.OpeningJourneyActive && !s.OpeningCommissionReceived)
                    {
                        list.Add(Near(WorldMacroOpeningProfileSO_CommissionId));
                        list.Add(() => new TalkT(WorldMacroOpeningProfileSO_CommissionId, null, (x, r) => x.OpeningCommissionReceived ? null : "opening commission not received"));
                    }
                    list.Add(Near("wangso_w1"));
                    list.Add(() => new TalkT("wangso_w1", null, (x, r) => { state.notes.Add("E2 Wangso greeting → escort " + x.EscortSnapshot?.Stage); return null; }));
                    list.Add(Near("jeongdam_j1"));
                    list.Add(() => new TalkT("jeongdam_j1", null));
                    list.Add(Near("wangso_w1"));
                    list.Add(() => new TalkT("wangso_w1", null, (x, r) => x.EscortSnapshot != null && x.EscortSnapshot.Stage == DemoEscortStage.Contracted ? null : "Wangso contract not recorded (" + x.EscortSnapshot?.Stage + ")"));
                    {
                        // Harness repair 2026-10-06: since D308-16c (2026-10-05) the campaign lists the metal lesson stage among 청룡's
                        // prerequisites, so the boss death alone no longer advances the campaign (TryAdvance refuses, no 국 unlock).
                        // The stage is read from the campaign asset; null = this campaign has no such gate and E2 is the script it was.
                        string lesson = LessonStage(s);
                        if (lesson != null)
                        {
                            bool Open(WorldMacroPlaytestSession x) => !x.Progress.campaign.Completed.Contains(lesson);
                            list.Add(() => new DoT("approach the metal lesson (fixture teleport)", x =>
                            {
                                if (!Open(x)) return null;
                                string r = TeleportNear(x, DemoGrowthLessonLink.LessonId);
                                if (r != null) state.notes.Add("E2 lesson approach skipped (the two hits do not need it): " + r);
                                return null;
                            }));
                            list.Add(() => new WaitT(.25f));
                            list.Add(() => new DoT("metal lesson threshold hit (fixture)", x => Open(x) ? LessonHit(x, lesson, false) : null));
                            list.Add(() => new WaitT(.25f));
                            list.Add(() => new DoT("metal lesson Metal hit (fixture)", x => Open(x) ? LessonHit(x, lesson, true) : null));
                            list.Add(() => new UntilT("campaign stage " + lesson, x => !Open(x), 8f, LessonWhy));
                        }
                    }
                    list.Add(() => new DoT("Cheongryong API defeat (fixture)", x => BossFixture(x)));
                    list.Add(() => new UntilT("HasDemoGuk", x => x.HasDemoGuk, 8f, BossWhy));   // judged line unchanged; BossWhy only words a timeout
                    // the committed defeat shows its 석경 folio (DetailRequested -> PlaytestUiRoot.OpenPage("상세") -> Pause.Begin): the game is
                    // paused under it and E3 (the companion's own walk) cannot run. Closed with F like any talk page, after the judged line.
                    list.Add(() => new ClosePageT("close the reading page of the defeat"));
                    break;
                case "E3":
                    list.Add(() => new DoT("departure spot (fixture teleport)", x =>
                    {
                        var st = StopOf(x, "escort_start"); var fwd = Vector3.ProjectOnPlane(st.Parking.forward, Vector3.up).normalized;
                        var wanted = st.Parking.position - fwd * x.DemoEscortSummon.MinimumPlayerDistance;
                        if (!x.TrySafeFeet(wanted, out var feet)) return "no safe player support at " + Harness303.V(wanted);
                        x.Teleport(feet, Quaternion.LookRotation(fwd).eulerAngles.y); Physics.SyncTransforms();
                        state.fixtures.Add("E3 teleport to the departure spot " + Harness303.V(feet) + " (parking − forward × " + x.DemoEscortSummon.MinimumPlayerDistance + " m)");
                        return null;
                    }));
                    {
                        double blockedSince = -1;
                        list.Add(() => new UntilT("companion fetches cargo and stages", x =>
                        {
                            var p = Presenter(x); var st = StopOf(x, "escort_start");
                            return p != null && p.CargoCarried && p.State == DemoEscortPresentationState.Staging && Vector3.Distance(x.DemoEscortCompanion.position, st.CompanionWait.position) < .8f;
                        }, 60f, CompanionWhy, x =>
                        {
                            var p = Presenter(x);
                            if (p == null || p.State != DemoEscortPresentationState.Blocked) { blockedSince = -1; return null; }
                            if (blockedSince < 0) blockedSince = Now;
                            if (Now - blockedSince < 3) return null;
                            var c = p.LastBlockingCollider;
                            return "Blocked 3 s: '" + p.LastIssue + "' by " + (c != null ? Harness303.PathOf(c.transform) + " [" + Cause(c) + "]" : "no collider") + " at " + Harness303.V(p.LastBlockedPoint);
                        }));
                    }
                    list.Add(Capture("E01_staging"));
                    break;
                case "E4":
                    list.Add(() => new SummonT(x => StopOf(x, "escort_start").Parking.position));
                    list.Add(Capture("E02_summon"));
                    break;
                case "E5":
                    list.Add(() => new BoardT());
                    list.Add(Capture("E03_boarded"));
                    break;
                case "E6":
                    list.Add(() => currentDrive = new DriveT(0));
                    list.Add(() => new DoT("keyboard verdict", x => { if (currentDrive?.KeyboardFailure != null) phaseSoft = "keyboard driving failed: " + currentDrive.KeyboardFailure; return null; }));
                    list.Add(Capture("E05_ck1_park"));
                    break;
                case "E7":
                    list.Add(() => new ExitT());
                    list.Add(() => new WalkT("to checkpoint_1", x => Front(x, "checkpoint_1", 2.2f), .6f, 45f));
                    list.Add(WaitCompanion());
                    list.Add(Capture("E06_ck1_follow"));
                    list.Add(() => new TalkT("checkpoint_1", "E07_ck1_page", (x, r) => x.EscortSnapshot.Stage >= DemoEscortStage.FirstInspectionCleared ? null : "inspection 1 not cleared (" + x.EscortSnapshot.Stage + ")"));
                    break;
                case "E8":
                    list.Add(() => new WalkT("walk-by past inspection_seized303", x => WalkByGoal(x, "inspection_seized303"), 1f, 25f));
                    list.Add(Capture("E08_walkby_seized"));
                    list.Add(() => new WalkT("back to checkpoint_1", x => Front(x, "checkpoint_1", 2.2f), .8f, 25f));
                    break;
                case "E11":
                    list.Add(() => new WalkT("to road_rest_1", x => Front(x, "road_rest_1"), .6f, 40f));
                    list.Add(() => new TalkT("road_rest_1", "E14a_rest", (x, r) => x.EscortSnapshot.CheckpointId == "road_rest_1" ? null : "rest did not record the party checkpoint (" + x.EscortSnapshot.CheckpointId + ")"));
                    list.Add(() => new DoT("fatal fall (fixture)", x =>
                    {
                        if (!x.SaveNow(out var err)) return "SaveNow " + err;
                        state.rememberedProgress = JsonUtility.ToJson(x.Progress);
                        state.fixtures.Add("E11 PlayerVitals.ApplyFatalFall() (environment death API, #252 precedent)");
                        Pv().ApplyFatalFall(); return null;
                    }));
                    list.Add(() => new UntilT("whole-party recovery", x =>
                    {
                        var e = x.EscortSnapshot; var p = Presenter(x);
                        return Pv().Hp01 > 0 && !x.DeathRespawnPending && e != null && Vector3.Distance(x.Walker.Body.transform.position, e.CheckpointFeet) < 2f && CompanionNear(x) && p != null && p.CargoCarried;
                    }, 25f, x => "hp " + Harness303.F(Pv().Hp01) + " death pending " + x.DeathRespawnPending + " " + CompanionWhy(x)));
                    list.Add(() => new DoT("recovery kept progress", x =>
                    {
                        var before = JsonUtility.FromJson<WorldMacroProgress>(state.rememberedProgress);
                        state.notes.Add("E11 LastEnvironmentDeath " + Pv().LastEnvironmentDeath + " (#303 death veil skipped for environment deaths)");
                        return before.escort.Stage == x.Progress.escort.Stage && before.campaign.Facts.All(f => x.Progress.campaign.Facts.Contains(f)) && JsonUtility.ToJson(before.equipment) == JsonUtility.ToJson(x.Progress.equipment) ? null : "stage/facts/equipment changed by the recovery";
                    }));
                    list.Add(Capture("E14_death_recovery"));
                    break;
                case "E9":
                    // 2026-10-06 (first run that reached E9): the summon was refused twice where the walk-by returns to ("앞에 자동차를 놓을 공간이
                    // 부족하다", wheel support spread 0.76 - uneven ground ahead). The party walks to the road in front of the parking first
                    // (the E3 spot: parking - forward x 6 m), as a player would, and calls the car there.
                    list.Add(() => new IfT("to the summon spot", CarNeeded, () => new WalkT("to the summon spot of checkpoint_1", x => StopOf(x, "checkpoint_1").Parking.position - StopOf(x, "checkpoint_1").Parking.forward * 6f, .8f, 45f)));
                    list.Add(() => new IfT("car near the party", CarNeeded, () => new SummonT(x => StopOf(x, "checkpoint_1").Parking.position + StopOf(x, "checkpoint_1").Parking.forward * 10f)));
                    list.Add(() => new BoardT());
                    list.Add(() => currentDrive = new DriveT(1));
                    list.Add(() => new DoT("keyboard verdict", x => { if (currentDrive?.KeyboardFailure != null) phaseSoft = "keyboard driving failed: " + currentDrive.KeyboardFailure; return null; }));
                    list.Add(() => new ExitT());
                    break;
                case "E9b":
                    list.Add(() => new WalkT("walk-by past inspection_burnt303", x => WalkByGoal(x, "inspection_burnt303"), 1f, 25f));
                    list.Add(Capture("E11_walkby_burnt"));
                    list.Add(() => new WalkT("back towards checkpoint_2", x => Front(x, "checkpoint_2", 2.2f), .8f, 30f));
                    break;
                case "E9c":
                    list.Add(() => new WalkT("to checkpoint_2", x => Front(x, "checkpoint_2", 2.2f), .6f, 45f));
                    list.Add(WaitCompanion());
                    list.Add(() => new TalkT("checkpoint_2", "E10_ck2_page", (x, r) => x.EscortSnapshot.Stage >= DemoEscortStage.SecondInspectionCleared ? null : "inspection 2 not cleared (" + x.EscortSnapshot.Stage + ")"));
                    break;
                case "E10":
                    list.Add(() => new IfT("to the summon spot", CarNeeded, () => new WalkT("to the summon spot of checkpoint_2", x => StopOf(x, "checkpoint_2").Parking.position - StopOf(x, "checkpoint_2").Parking.forward * 6f, .8f, 45f)));
                    list.Add(() => new IfT("car near the party", CarNeeded, () => new SummonT(x => StopOf(x, "checkpoint_2").Parking.position + StopOf(x, "checkpoint_2").Parking.forward * 10f)));
                    list.Add(() => new BoardT());
                    list.Add(() => currentDrive = new DriveT(2));
                    list.Add(() => new DoT("keyboard verdict", x => { if (currentDrive?.KeyboardFailure != null) phaseSoft = "keyboard driving failed: " + currentDrive.KeyboardFailure; return null; }));
                    list.Add(() => new ExitT());
                    list.Add(() => new WalkT("to cargo_delivery", x => Front(x, "cargo_delivery", 2.2f), .6f, 45f));
                    list.Add(WaitCompanion());
                    list.Add(() => new DoT("currency before delivery", x => { state.currencyBeforeDelivery = x.Progress.ledger.currency; return null; }));
                    list.Add(() => new TalkT("cargo_delivery", "E12_delivery_page", (x, r) =>
                    {
                        var e = x.EscortSnapshot; state.currencyAfterDelivery = x.Progress.ledger.currency;
                        return e.Stage == DemoEscortStage.Delivered && e.DeliveryRewardRecorded ? null : "delivery not recorded (" + e.Stage + ", reward " + e.DeliveryRewardRecorded + ")";
                    }));
                    list.Add(() => new TalkT("wangso_w1", "E13_wangso_followup", (x, r) =>
                        x.Progress.ledger.currency == state.currencyAfterDelivery && x.EscortSnapshot.Stage == DemoEscortStage.Delivered ? null : "follow-up changed currency " + state.currencyAfterDelivery + " → " + x.Progress.ledger.currency));
                    break;
                case "E12pre":
                    list.Add(() => new DoT("remember progress", x =>
                    {
                        if (!x.SaveNow(out var err)) return "SaveNow " + err;
                        state.rememberedProgress = JsonUtility.ToJson(x.Progress);
                        File.Copy(state.iso.savedPath, Path.Combine(RunFolder, "save-before-restart.json"), true); return null;
                    }));
                    break;
                case "E12":
                    list.Add(() => new DoT("restart keeps the escort", x =>
                    {
                        var e = JsonUtility.FromJson<WorldMacroProgress>(state.rememberedProgress);
                        File.Copy(state.iso.savedPath, Path.Combine(RunFolder, "save-after-restart.json"), true);
                        bool ok = x.LoadStatus == "primary" && x.Progress.escort.Stage == e.escort.Stage && x.Progress.escort.InspectionsCleared == e.escort.InspectionsCleared &&
                                  x.Progress.escort.DeliveryRewardRecorded == e.escort.DeliveryRewardRecorded && x.Progress.ledger.currency == e.ledger.currency &&
                                  JsonUtility.ToJson(x.Progress.equipment) == JsonUtility.ToJson(e.equipment);
                        return ok ? null : "restart: LoadStatus " + x.LoadStatus + " stage " + x.Progress.escort.Stage + " vs " + e.escort.Stage + " currency " + x.Progress.ledger.currency + " vs " + e.ledger.currency;
                    }));
                    list.Add(() => new WaitT(1f));
                    list.Add(Capture("E15_restart"));
                    break;
            }
            script = list;
            if (script.Count == 0) script.Add(() => new DoT("empty phase", x => null));
        }

        const string WorldMacroOpeningProfileSO_CommissionId = Oheangbu.Data.World.WorldMacroOpeningProfileSO.CommissionId;

        /// <summary>Fixture: stand 2 m from the point on safe ground, facing it (the F that follows is real input).</summary>
        static string TeleportNear(WorldMacroPlaytestSession s, string id)
        {
            var p = Harness303.InteractionPoint(s, id);
            if (p == null) return "no interaction point " + id;
            for (int i = 0; i < 16; i++)
            {
                float radius = i < 8 ? 2f : 1.4f;
                var c = p.Position + Quaternion.Euler(0, i * 45f + (i < 8 ? 0 : 22.5f), 0) * Vector3.forward * radius;
                if (!s.TrySafeFeet(c, out var feet)) continue;
                s.Teleport(feet, Harness303.YawTo(feet, p.Position)); Physics.SyncTransforms();
                state.fixtures.Add(state.phase + " teleport 2 m from " + id + " " + Harness303.V(feet));
                return null;
            }
            return "no safe feet around " + id + " " + Harness303.V(p.Position);
        }

        /// <summary>Fixture (#252 prepare): the Cheongryong prerequisite is recorded through its real death path, not fought.</summary>
        static string BossFixture(WorldMacroPlaytestSession s)
        {
            var boss = s.Actors.FirstOrDefault(a => a != null && a.Id == BossId);
            if (boss == null) return "no actor " + BossId;
            boss.enabled = true;
            var life = boss.GetComponent<EnemyVitals>(); if (life == null) return "boss has no EnemyVitals";
            life.enabled = true;
            var cc = boss.GetComponent<CheongryongCombatController>(); if (cc != null) { cc.enabled = true; cc.AttackEnabled = false; }
            life.TakeDamage(float.MaxValue);
            state.fixtures.Add("E2 Cheongryong defeated through EnemyVitals API (prerequisite only, #252 prepare precedent)");
            return null;
        }

        /// <summary>The campaign stage the metal lesson completes (GrowthInterrupted / metal_growth_lesson) when the campaign lists it
        /// among 청룡's prerequisites (D308-16c gate: relay + cargo_contract + deep_forest); null when it does not (the D308-2 form
        /// relay + cargo_contract, the #252 form without prerequisites, or the gate reverted).</summary>
        static string LessonStage(WorldMacroPlaytestSession s)
        {
            var campaign = s.Content != null ? s.Content.Campaign : null;
            if (campaign == null || campaign.Stages == null || !campaign.UseExplicitPrerequisites) return null;
            var boss = campaign.Stages.FirstOrDefault(x => x != null && x.Event == Oheangbu.Data.Demo.DemoEventKind.BossDefeated && x.TriggerId == BossId);
            if (boss == null || boss.PrerequisiteIds == null) return null;
            foreach (string id in boss.PrerequisiteIds)
            {
                var stage = campaign.FindStage(id);
                if (stage != null && stage.Event == Oheangbu.Data.Demo.DemoEventKind.GrowthInterrupted && stage.TriggerId == DemoGrowthLessonLink.LessonId) return id;
            }
            return null;
        }

        static Oheangbu.App.Prologue.PrologueEncounter LessonActor(WorldMacroPlaytestSession s)
        {
            var spec = s.Content.Encounters.FirstOrDefault(e => e.ContentId == DemoGrowthLessonLink.LessonId);
            return spec != null ? s.Actors.FirstOrDefault(a => a != null && a.Id == spec.Id) : null;
        }

        /// <summary>Fixture (precedents: CompactRebuildProgression251 "life", DemoChapterThreeRuntimeChecks stages 10 and 1): the metal
        /// lesson stage is completed the way the game completes it. A threshold hit on the lesson tree starts its growth wind-up; a
        /// Metal hit from the player inside the wind-up interrupts it; CheongryongGrowthController raises GrowthInterrupted,
        /// DemoGrowthLessonLink hands the proof to the session, the session advances the campaign (TryAdvance GrowthInterrupted) and
        /// saves. The API calls are the two hits (EnemyVitals.TakeDamage with the player's provenance) and, before the first, one
        /// session.Cull() - the pass the session runs itself - so the lesson's vitals are in the state the session wants after the
        /// teleport; all three are named in report.fixtures. Nothing is written into the progress by hand, and a lesson the session
        /// does not hold available is reported, not forced on.</summary>
        static string LessonHit(WorldMacroPlaytestSession s, string stage, bool metal)
        {
            var actor = LessonActor(s);
            if (actor == null) return "no encounter with ContentId " + DemoGrowthLessonLink.LessonId + " in this session";
            var life = actor.GetComponent<EnemyVitals>(); var growth = actor.GetComponent<CheongryongGrowthController>(); var link = actor.GetComponent<DemoGrowthLessonLink>();
            if (life == null || growth == null || link == null) return actor.Id + " lacks EnemyVitals / CheongryongGrowthController / DemoGrowthLessonLink";
            var player = s.Walker.Body.gameObject;
            string was = actor.Id + " hp " + Harness303.F(life.Hp, "F1") + "/" + Harness303.F(life.MaxHp, "F1") + " growth " + growth.State;
            if (!metal)
            {
                s.Cull();   // the session's own availability pass: it switches the vitals of an available lesson on
                if (!life.isActiveAndEnabled || !growth.isActiveAndEnabled) return "the session does not hold the lesson available (vitals on " + life.isActiveAndEnabled + ", growth controller on " + growth.isActiveAndEnabled + "): " + was;
                if (!life.IsAlive) return "the lesson tree is dead: " + was;
                float amount = Mathf.Max(0f, life.Hp - life.MaxHp * .49f);
                if (amount > 0f) life.TakeDamage(amount, AttackProvenance.Create(player, DamageSource.PlayerDirect, Oheangbu.Core.Domain.Element.Fire));
                if (!growth.IsWindingUp) return "the threshold hit of " + Harness303.F(amount, "F1") + " hp did not start the growth wind-up: was " + was + ", now hp " + Harness303.F(life.Hp, "F1") + " growth " + growth.State;
                state.fixtures.Add("E2 metal lesson (campaign stage " + stage + ", a 청룡 prerequisite since D308-16c): session.Cull() called once (the session's own availability pass, nothing forced on), then a threshold hit of " +
                                   Harness303.F(amount, "F1") + " hp (Fire, PlayerDirect) through the EnemyVitals API on " + actor.Id + " -> growth wind-up (precedents: #251 'life', chapter-3 runtime check)");
                return null;
            }
            if (!growth.IsWindingUp) return "the growth wind-up was over before the Metal hit: " + was;
            life.TakeDamage(1f, AttackProvenance.Create(player, DamageSource.PlayerDirect, Oheangbu.Core.Domain.Element.Metal));
            if (!growth.WasInterrupted || !link.HasProof) return "the Metal hit did not interrupt the growth: was " + was + ", now growth " + growth.State + " proof " + link.HasProof;
            state.fixtures.Add("E2 metal lesson: Metal hit of 1 hp (PlayerDirect) through the EnemyVitals API -> GrowthInterrupted -> DemoGrowthLessonLink -> session.TryCompleteGrowthLesson (the game's own campaign advance and save; nothing written by hand)");
            return null;
        }

        static string LessonWhy(WorldMacroPlaytestSession s)
        {
            var actor = LessonActor(s); var growth = actor != null ? actor.GetComponent<CheongryongGrowthController>() : null; var link = actor != null ? actor.GetComponent<DemoGrowthLessonLink>() : null;
            return "growth " + (growth != null ? growth.State.ToString() : "?") + ", proof " + (link != null && link.HasProof) + ", campaign completed [" + string.Join(",", s.Progress.campaign.Completed) + "], last notice '" + (s.LastFeedback ?? "") + "'";
        }

        /// <summary>Words a timeout of the HasDemoGuk wait (the judged condition and its 8 s are untouched): the boss, the record, and
        /// which of 청룡's campaign prerequisites are not completed - the reason DemoCampaignProgression.TryAdvance refuses the defeat.</summary>
        static string BossWhy(WorldMacroPlaytestSession s)
        {
            var campaign = s.Content != null ? s.Content.Campaign : null; var done = s.Progress.campaign.Completed;
            var stage = campaign != null && campaign.Stages != null ? campaign.Stages.FirstOrDefault(x => x != null && x.Event == Oheangbu.Data.Demo.DemoEventKind.BossDefeated && x.TriggerId == BossId) : null;
            var boss = s.Actors.FirstOrDefault(a => a != null && a.Id == BossId); var life = boss != null ? boss.GetComponent<EnemyVitals>() : null;
            string missing = stage == null ? "no BossDefeated stage" : string.Join(",", (stage.PrerequisiteIds ?? Array.Empty<string>()).Where(id => !done.Contains(id)));
            return "boss alive " + (life != null && life.IsAlive) + ", defeat recorded " + s.Progress.defeated.Contains(BossId) + ", campaign completed [" + string.Join(",", done) + "], " + BossId + " prerequisites not completed [" + missing + "]";
        }

        /// <summary>A road point 30 m past `site` (eastward, +x) on the nearest escort leg, on the NavMesh.</summary>
        static Vector3 WalkByGoal(WorldMacroPlaytestSession s, string site)
        {
            var root = Harness303.Find(s.gameObject.scene, Roadside);
            var t = root != null ? root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == site) : null;
            var feet = s.Walker.Body.transform.position;
            if (t == null) { state.notes.Add(state.phase + ": " + site + " missing; walk-by skipped"); return feet; }
            var route = Route(s.gameObject.scene); Vector3 best = t.position, dir = Vector3.right; float bestD = float.MaxValue;
            for (int i = 0; i < LegTable.Length; i++)
            {
                if (!TryLeg(s, route, i, out var leg, out _)) continue;
                for (int j = 1; j < leg.points.Length; j++)
                {
                    float d = Harness303.Flat(leg.points[j], t.position);
                    if (d < bestD) { bestD = d; best = leg.points[j]; dir = Vector3.ProjectOnPlane(leg.points[j] - leg.points[j - 1], Vector3.up).normalized; }
                }
            }
            if (dir.x < 0) dir = -dir;
            var goal = best + dir * 30f;
            if (Harness303.Ground(goal, out var hit)) goal = hit.point;
            if (NavMesh.SamplePosition(goal, out var nav, 4f, NavMesh.AllAreas)) goal = nav.position;
            state.notes.Add(state.phase + ": walk-by " + site + " road point " + Harness303.V(best) + " (" + Harness303.F(bestD, "F1") + " m from site) → " + Harness303.V(goal));
            return goal;
        }
    }
}
