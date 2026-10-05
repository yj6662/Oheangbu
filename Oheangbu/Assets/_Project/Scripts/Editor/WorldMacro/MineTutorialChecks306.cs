using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#306 #11 mine tutorial Play check (SPEC-PLAYTEST-306 AC-11a/b/c/d; automated, not manual play). Isolated fresh save
    /// (Harness303, LoadStatus "new" = enrolled) in W_Demo_Main, real Input System path (VirtualInput303) for F / Tab:
    /// L1 lock card on sight → F closes (timeScale back to 1, no interaction) → Tab → L2 dodge card on the charge → L3 draw card after it
    /// → L4 parry card on the shard → the shard waits ~3 m out → a Metal guard is raised on the real ParryJudge (the drawing/recognition
    /// path is not exercised here) → Success, groggy ½ → second shard → Success → L5 riposte card → stun ends → L7 ward card on the ring →
    /// free fight. Captures one Game View frame per card. Run("start") | Run("status") | Run("abort"). Output Art/Playtest306/Checks/tutorial306.txt.</summary>
    public static class MineTutorialChecks306
    {
        const string MainScene = "Assets/_Project/Scenes/World/W_Demo_Main.unity", MainSlot = "world-main";
        static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Playtest306/Checks"));
        static readonly Isolation303 iso = new Isolation303();
        static string status = "idle", note = ""; static int phase, fails; static double at, began; static readonly List<string> lines = new List<string>();
        static WorldMacroPlaytestSession s; static bool inputStarted, tidied, hooked, bossHooked;
        static readonly List<string> outcomes = new List<string>(); static int guards;
        static double Now => EditorApplication.timeSinceStartup;
        static Vector2 Center => new Vector2(Screen.width * .5f, Screen.height * .5f);

        public static string Run(string command)
        {
            command = (command ?? "").Trim();
            if (command == "status") return status + " | phase " + phase + " | " + note + " | " + string.Join(" / ", lines.GetRange(Math.Max(0, lines.Count - 6), Math.Min(6, lines.Count)));
            if (command == "abort") { Finish("aborted"); return "aborted"; }
            if (command != "start") return "refused: unknown command " + command + " (start | status | abort)";
            if (status == "running") return "refused: already running";
            string why = Harness303.Prepare(iso, MainScene, MainSlot, "_c303_t306");
            if (why != null) return why;
            Harness303.Apply(iso, Object.FindFirstObjectByType<WorldMacroPlaytestSession>());
            status = "running"; phase = 0; fails = 0; guards = 0; lines.Clear(); outcomes.Clear(); at = began = Now; s = null; inputStarted = tidied = bossHooked = false;
            lines.Add("#306 #11 tutorial check " + DateTime.Now.ToString("s") + " isolated suffix " + iso.suffix + " — automated Play (VirtualInput303 for F/Tab, guard raised on the judge), not manual play");
            if (!hooked) { EditorApplication.update += Tick; hooked = true; }
            EditorApplication.isPlaying = true;
            return "started tutorial check";
        }

        static void Line(string x) => lines.Add(x);
        static void Check(bool ok, string what) { if (!ok) fails++; Line((ok ? "PASS " : "FAIL ") + what); }
        static MineTutorialCard306 Card => PlaytestUiRoot.Instance != null ? PlaytestUiRoot.Instance.TutorialCardShowing : null;
        static MineBossController Boss => s != null ? s.MineTutorialBoss : null;
        static void Heal() { var v = s.Walker.Body.GetComponent<PlayerVitals>(); if (v != null && v.Hp01 < .9f) v.Restore(1f); }
        static void Capture(string tag)
        {
            Directory.CreateDirectory(Folder);
            string file = Path.Combine(Folder, "tutorial306_" + tag + ".png"); ScreenCapture.CaptureScreenshot(file); Line("INFO capture " + file);
        }
        static void Advance(int next) { phase = next; at = Now; }
        static bool Elapsed(double seconds) => Now - at >= seconds;
        static bool TimedOut(double seconds, string what) { if (Now - at < seconds) return false; Check(false, "timeout " + seconds + " s: " + what); Finish("FAIL"); return true; }

        static void OnImpact(MineBossMove move, EnemyAttackImpact impact) { outcomes.Add(move + ":" + impact.Outcome); }

        // raise a Metal guard on the scene's real ParryJudge (the reward path — groggy, organ stroke — runs exactly as after a drawn 서)
        static void RaiseMetalGuard()
        {
            var judge = Harness303.Field<ParryJudge>(s, "parry");
            if (judge == null) { Check(false, "ParryJudge not found on the session"); return; }
            outcomes.Clear(); judge.RaiseGuard(Element.Metal, Time.time); guards++;
            Line("INFO Metal guard raised on the judge at t=" + Time.time.ToString("F2") + " (guard " + guards + ")");
        }

        static void Tick()
        {
            if (status == "finishing" && !EditorApplication.isPlayingOrWillChangePlaymode) { Cleanup(); return; }
            if (status != "running") return;
            if (Now - began > 300) { Check(false, "overall timeout at phase " + phase + " (" + note + ")"); Finish("timeout"); return; }
            if (!EditorApplication.isPlaying) return;
            if (s == null) { s = Object.FindFirstObjectByType<WorldMacroPlaytestSession>(); note = "waiting for session"; return; }
            if (!s.InitializationComplete || UiAdapter303.LoadingInProgress == true) { note = "waiting for init/loading"; return; }
            if (!tidied) { UiAdapter303.CloseMenu(); UiAdapter303.ReleaseGate(); tidied = true; return; }
            if (!inputStarted)
            {
                var assets = new List<InputActionAsset> { Harness303.Field<InputActionAsset>(s.Walker.Motor, "_actions"), Harness303.Field<InputActionAsset>(s.Walker.Drawing, "_actions") };
                Line("input: " + VirtualInput303.Begin(assets)); inputStarted = true; return;
            }
            VirtualInput303.Hold = InputFrame303.Neutral(Center);
            var dir = s.MineTutorial;
            switch (phase)
            {
                case 0:
                {
                    Check(s.LoadStatus == "new", "fresh isolated save (LoadStatus " + s.LoadStatus + ")");
                    Check(s.Progress.ledger.completed.Contains(MineTutorialProfileSO.EnrolledId), "new save enrolled (" + MineTutorialProfileSO.EnrolledId + ")");
                    Check(dir != null && Boss != null, "director bound to " + (Boss != null ? Boss.name : "no boss"));
                    if (dir == null || Boss == null) { Finish("FAIL"); return; }
                    var old = Array.Find(s.Actors, a => a != null && a.Id == WorldMacroPlaytestSession.MineTutorialReplacedId);
                    Line("INFO " + WorldMacroPlaytestSession.MineTutorialReplacedId + " present " + (old != null) + (old != null ? " enabled " + old.enabled : ""));
                    if (!bossHooked) { Boss.MoveImpact += OnImpact; bossHooked = true; }
                    var bp = Boss.transform.position; var from = bp + Vector3.left * 10f;
                    if (UnityEngine.AI.NavMesh.SamplePosition(from, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) from = hit.position;
                    s.Teleport(from, 90f); Heal(); Line("INFO teleported to " + from.ToString("F1") + " facing the boss at " + bp.ToString("F1"));
                    Advance(1); return;
                }
                case 1:
                    note = "waiting L1 card";
                    if (Card == null) { if (TimedOut(15, "L1 lock card on sight (step " + dir.Current + ")")) return; return; }
                    Check(Card.BeatId == "l1_lock", "first card = l1_lock (" + Card.BeatId + ")");
                    Check(Time.timeScale <= .0001f, "card pauses through PauseCoordinator (timeScale " + Time.timeScale + ")");
                    Advance(2); return;
                case 2:
                    if (!Elapsed(1.2)) return;
                    if (Card != null) Capture(Card.BeatId); VirtualInput303.Tap(Key.F, Center, 2); Advance(3); return;
                case 3:
                    if (!Elapsed(.8)) return;
                    Check(Card == null && UiAdapter303.Page.Length == 0, "F closed the card (page '" + UiAdapter303.Page + "')");
                    Check(Mathf.Approximately(Time.timeScale, 1f), "AC-11b timeScale back to 1 after the card (" + Time.timeScale + ")");
                    Check(dir.Current == MineTutorialDirector.Step.LockWait && Boss.Hold, "boss waits for the lock (step " + dir.Current + ", hold " + Boss.Hold + ")");
                    Check(string.IsNullOrEmpty(s.FocusedId) || UiAdapter303.Page.Length == 0, "AC-11c the closing F opened no interaction");
                    VirtualInput303.Tap(Key.Tab, Center, 2); Advance(4); return;
                case 4:
                    note = "waiting L2 card"; Heal();
                    if (Card == null) { if (TimedOut(20, "L2 dodge card on the charge (step " + dir.Current + ")")) return; return; }
                    var lockOn = s.Walker.Motor.GetComponent<LockOn>();
                    Check(lockOn != null && lockOn.Target == Boss.Vitals, "Tab locked the boss");
                    Check(Card.BeatId == "l2_dodge", "second card = l2_dodge (" + Card.BeatId + ")");
                    Advance(5); return;
                case 5:
                    if (!Elapsed(1.2)) return; if (Card != null) Capture(Card.BeatId); VirtualInput303.Tap(Key.F, Center, 2); Advance(6); return;
                case 6:
                    note = "waiting L3 card"; Heal();
                    if (Card == null) { if (TimedOut(20, "L3 draw card after the charge (step " + dir.Current + ")")) return; return; }
                    Check(Card.BeatId == "l3_draw", "third card = l3_draw with letter " + Card.Letter + " (" + Card.BeatId + ")");
                    Advance(7); return;
                case 7:
                    if (!Elapsed(1.2)) return; if (Card != null) Capture(Card.BeatId); VirtualInput303.Tap(Key.F, Center, 2); Advance(8); return;
                case 8:
                    note = "waiting L4 card (boss waits 8 s for a hit)"; Heal();
                    if (Card == null) { if (TimedOut(25, "L4 parry card on the shard (step " + dir.Current + ")")) return; return; }
                    Check(Card.BeatId == "l4_parry", "fourth card = l4_parry (" + Card.BeatId + ")");
                    Advance(9); return;
                case 9:
                    if (!Elapsed(1.2)) return; if (Card != null) Capture(Card.BeatId); VirtualInput303.Tap(Key.F, Center, 2); Advance(10); return;
                case 10:
                    note = "waiting for the shard to hold"; Heal();
                    if (!Boss.ShardHeld) { if (TimedOut(8, "shard held ~3 m out")) return; return; }
                    if (!Elapsed(1.0)) return;   // it stays held while no guard stands
                    Check(Boss.ShardHeld, "shard still waiting 1 s later (no guard)");
                    Capture("shard_held"); RaiseMetalGuard(); Advance(11); return;
                case 11:
                    note = "waiting first parry";
                    if (outcomes.Count == 0) { if (TimedOut(4, "shard impact after the guard")) return; return; }
                    Check(outcomes[0] == "Shard:Success", "AC-11d 서 (Metal) against the Wood shard = Success (" + outcomes[0] + ")");
                    Check(Mathf.Approximately(Boss.Vitals.Groggy.Value01, .5f), "groggy ½ after one parry (" + Boss.Vitals.Groggy.Value01 + ")");
                    Advance(12); return;
                case 12:
                    note = "waiting second shard to hold"; Heal();
                    if (Card != null) { Line("INFO unexpected card " + Card.BeatId + " — closing"); VirtualInput303.Tap(Key.F, Center, 2); at = Now; return; }
                    if (!Boss.ShardHeld) { if (TimedOut(12, "second shard held")) return; return; }
                    RaiseMetalGuard(); Advance(13); return;
                case 13:
                    note = "waiting L5 card";
                    if (Card == null) { if (TimedOut(5, "L5 riposte card at full groggy (outcomes " + string.Join(",", outcomes) + ")")) return; return; }
                    Check(Card.BeatId == "l5_riposte", "fifth card = l5_riposte (" + Card.BeatId + ")");
                    Check(Boss.Vitals.WeakPointActive, "two parries opened the weak point (tutorial value)");
                    Advance(14); return;
                case 14:
                    if (!Elapsed(1.2)) return; if (Card != null) Capture(Card.BeatId); VirtualInput303.Tap(Key.F, Center, 2); Advance(15); return;
                case 15:
                    note = "waiting L7 card after the stun"; Heal();
                    if (Card == null) { if (TimedOut(20, "L7 ward card on the ring (step " + dir.Current + ")")) return; return; }
                    Check(Card.BeatId == "l7_ward", "next card = l7_ward (" + Card.BeatId + ")");
                    Advance(16); return;
                case 16:
                    if (!Elapsed(1.2)) return; if (Card != null) Capture(Card.BeatId); VirtualInput303.Tap(Key.F, Center, 2); Advance(17); return;
                case 17:
                    note = "waiting free fight"; Heal();
                    if (dir.Current != MineTutorialDirector.Step.Free) { if (TimedOut(10, "free fight after the ring (step " + dir.Current + ")")) return; return; }
                    Check(Mathf.Approximately(Time.timeScale, 1f), "timeScale 1 in the free fight");
                    var done = s.Progress.ledger.completed; int seen = 0;
                    foreach (var id in new[] { "l1_lock", "l2_dodge", "l3_draw", "l4_parry", "l5_riposte", "l7_ward" }) if (done.Contains(MineTutorialProfileSO.IdPrefix + id)) seen++;
                    Check(seen == 6, "AC-11a ledger marks for the six fight cards (" + seen + "/6), cards shown " + dir.CardsShown);
                    Line("INFO outcomes " + string.Join(",", outcomes) + "; boss HP " + Boss.Vitals.Hp + "/" + Boss.Vitals.MaxHp);
                    Finish(fails == 0 ? "PASS" : "FAIL"); return;
            }
        }

        static void Finish(string how)
        {
            status = "finishing";
            if (bossHooked && Boss != null) Boss.MoveImpact -= OnImpact; bossHooked = false;
            try { VirtualInput303.End(out _); } catch (Exception) { }
            Line("RESULT " + how + " (fails " + fails + ")");
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            EditorApplication.delayCall += Cleanup;
        }

        static void Cleanup()
        {
            if (status != "finishing") return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.delayCall += Cleanup; return; }
            status = "cleaning";
            foreach (var c in Harness303.Cleanup(iso, Path.Combine(Folder, "tutorial306-run"))) Line(c.status + " " + c.id + " " + c.detail);
            Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, "tutorial306.txt"), string.Join("\n", lines), new UTF8Encoding(false));
            status = fails == 0 ? "done PASS" : "done FAIL"; s = null; inputStarted = tidied = false;
        }
    }
}
