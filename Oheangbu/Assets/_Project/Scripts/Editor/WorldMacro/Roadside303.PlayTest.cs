using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    //   play-test         — (Play mode, isolated review save) walk every commission through the live session, frame by frame:
    //                       locked evidence → accept → wait → evidence / defeat → report + pay → completed line, then read the
    //                       save file back. The F press is session.Interact on the id the real focus loop picked; with the #306
    //                       dialogue view bound, "first talk" answers the offer's 맡는다 through the raised request's Chosen.
    //   play-test-status  — progress and results (JSON)
    public static partial class Roadside303
    {
        [Serializable] class TestStep { public string commission, action, target, expect, focused, text, result; public bool pass; public int currency; }
        [Serializable] class TestReport { public string status = "idle", error, saveFile; public List<TestStep> steps = new List<TestStep>(); public int passed, failed; public string[] saveKeys; }
        static TestReport report;
        static Queue<Func<bool>> plan;       // each returns true when its step is finished
        static float waitUntil;
        static string lastTitle, lastBody;

        static WorldMacroPlaytestSession LiveSession => Object.FindFirstObjectByType<WorldMacroPlaytestSession>();

        static string PlayTestStart()
        {
            if (!EditorApplication.isPlaying) throw new Exception("play-test: Play mode only (use the isolated review Play)");
            var s = LiveSession ?? throw new Exception("no live session");
            if (string.IsNullOrEmpty(s.TestSaveSuffix)) throw new Exception("play-test: refusing to run on an unsuffixed (real) save — start the review Play first");
            var data = LoadData();
            report = new TestReport { status = "running" };
            plan = new Queue<Func<bool>>();
            s.DetailRequested -= OnDetail; s.DetailRequested += OnDetail;
            foreach (var c in data.commissions)
            {
                var q = s.Content.Commissions.First(x => x.Id == c.id);
                if (!string.IsNullOrEmpty(q.EvidenceId))
                    Talk(c.id, "evidence before accepting", q.EvidenceId, () => !s.Progress.ledger.completed.Contains(q.EvidenceId) && !s.CommissionAccepted(q.Id), "locked");
                Talk(c.id, "first talk", q.GiverId, () => s.CommissionAccepted(q.Id), "accepted");
                Talk(c.id, "talk before done", q.GiverId, () => !s.CommissionReported(q.Id), "waiting");
                if (!string.IsNullOrEmpty(q.EvidenceId)) Talk(c.id, "take evidence", q.EvidenceId, () => s.Progress.ledger.completed.Contains(q.EvidenceId), "evidence");
                if (!string.IsNullOrEmpty(q.TargetEncounterId)) Defeat(c.id, q.TargetEncounterId);
                int before = 0;
                plan.Enqueue(() => { before = s.Progress.ledger.currency; return true; });
                Talk(c.id, "report", q.GiverId, () => s.CommissionReported(q.Id) && s.Progress.ledger.currency == before + q.Reward, "reported +" + q.Reward);
                Talk(c.id, "talk after", q.GiverId, () => s.CommissionReported(q.Id), "completed");
            }
            plan.Enqueue(() =>
            {
                string file = Path.Combine(Application.persistentDataPath, s.Content.SaveSlot + s.TestSaveSuffix + ".json");
                report.saveFile = file;
                if (!File.Exists(file)) { Add("save", "read save file", file, "exists", "", "missing", false); return true; }
                string json = File.ReadAllText(file);
                var keys = data.commissions.SelectMany(c => new[] { c.id + ":accepted", c.id + ":reported" }).ToArray();
                report.saveKeys = keys.Where(k => json.Contains("\"" + k + "\"")).ToArray();
                Add("save", "read save file", file, "all accepted/reported keys", "", report.saveKeys.Length + "/" + keys.Length, report.saveKeys.Length == keys.Length);
                return true;
            });
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            return "play-test started: " + plan.Count + " steps";
        }

        static void OnDetail(string title, string body) { lastTitle = title; lastBody = body; }

        static void Add(string commission, string action, string target, string expect, string focused, string result, bool pass, string text = null)
        {
            var s = LiveSession;
            report.steps.Add(new TestStep { commission = commission, action = action, target = target, expect = expect, focused = focused, result = result, pass = pass, text = text, currency = s != null && s.Progress != null ? s.Progress.ledger.currency : -1 });
            if (pass) report.passed++; else report.failed++;
        }

        // stand in front of the point, let the real focus loop pick it, press (Interact), record the text, close any page
        static void Talk(string commission, string action, string id, Func<bool> check, string expect)
        {
            var s = LiveSession; int phase = 0; string focused = null;
            // #306: with the dialogue view bound the giver's talk is one DialogueRequest306 (the offer ends on [맡는다 / 거절한다]);
            // the request raised during the direct Interact is kept so "first talk" can answer 맡는다 through its Chosen, as the view would
            DialogueRequest306 raised = null;
            void Grab(DialogueRequest306 r) { if (raised == null) raised = r; }
            plan.Enqueue(() =>
            {
                var ui = PlaytestUiRoot.Instance;
                switch (phase)
                {
                    case 0:
                        if (ui != null && ui.Page.Length > 0) { ui.Back(); return false; }   // earlier page still open
                        var p = s.Content.Points.FirstOrDefault(x => x.Id == id);
                        if (p == null) { Add(commission, action, id, expect, "", "no point", false); return true; }
                        var npc = Find(Root303 + "/Givers/" + id);
                        Vector3 front = npc != null ? p.Position + npc.forward * 1.5f : p.Position + ApproachOffset(p.Position);
                        float yaw = Mathf.Atan2(p.Position.x - front.x, p.Position.z - front.z) * Mathf.Rad2Deg;
                        if (!s.TrySafeFeet(front, out var feet)) feet = front;
                        s.Teleport(feet, yaw); lastTitle = lastBody = null; waitUntil = Time.realtimeSinceStartup + .6f; phase = 1; return false;
                    case 1:
                        if (Time.realtimeSinceStartup < waitUntil) return false;
                        focused = s.FocusedId;
                        raised = null;
                        s.DialogueRequested += Grab;
                        try { s.Interact(focused == id ? focused : id); }
                        finally { s.DialogueRequested -= Grab; }
                        if (raised != null && raised.Lines != null && raised.Lines.Length > 0) { lastTitle = raised.Speaker; lastBody = string.Join("\n", raised.Lines); }
                        if (action == "first talk" && raised != null && raised.Services != null)
                        {
                            var accept = Array.Find(raised.Services, x => x != null && x.Kind == DialogueServiceKind306.CommissionAccept && x.Enabled);
                            // the session writes <id>:accepted and shows its follow-up in place; phase 3's Back closes it
                            if (accept != null) { raised.Chosen?.Invoke(accept); lastBody = (lastBody ?? "") + "\n[" + accept.Label + "]"; }
                        }
                        waitUntil = Time.realtimeSinceStartup + .5f; phase = 2; return false;
                    case 2:
                        if (Time.realtimeSinceStartup < waitUntil) return false;
                        string text = lastBody ?? s.LastFeedback;
                        Capture(commission + "_" + action.Replace(' ', '_'));   // written at the end of this frame, page still open
                        bool ok = check() && focused == id;
                        Add(commission, action, id, expect, focused, ok ? "ok" : "focus=" + focused + " check=" + check(), ok, text);
                        waitUntil = Time.realtimeSinceStartup + .3f; phase = 3; return false;
                    default:
                        if (Time.realtimeSinceStartup < waitUntil) return false;
                        if (ui != null && ui.Page.Length > 0) ui.Back();
                        return true;
                }
            });
        }

        static Vector3 ApproachOffset(Vector3 at)
        {
            // evidence points sit at the front of their site; approach from the site's road side
            var root = Find(Root303); if (root == null) return Vector3.back * 1.3f;
            Transform best = null; float d = float.MaxValue;
            foreach (Transform site in root) { if (site.name == "Givers") continue; float dd = Vector3.Distance(site.position, at); if (dd < d) { d = dd; best = site; } }
            return best != null ? best.forward * 1.3f : Vector3.back * 1.3f;
        }

        static void Defeat(string commission, string encounterId)
        {
            var s = LiveSession; int phase = 0;
            plan.Enqueue(() =>
            {
                var actor = s.Actors.FirstOrDefault(a => a != null && a.Id == encounterId);
                if (actor == null) { Add(commission, "defeat target", encounterId, "defeated", "", "no actor", false); return true; }
                switch (phase)
                {
                    case 0:
                        var near = actor.transform.position - actor.transform.forward * 4f;
                        if (!s.TrySafeFeet(near, out var feet)) feet = near;
                        s.Teleport(feet, actor.transform.eulerAngles.y); waitUntil = Time.realtimeSinceStartup + 1.2f; phase = 1; return false;
                    case 1:
                        if (Time.realtimeSinceStartup < waitUntil) return false;
                        // the real death path (vitals → Defeated → session record); only the fight itself is skipped
                        actor.GetComponent<EnemyVitals>().TakeDamage(float.MaxValue);
                        waitUntil = Time.realtimeSinceStartup + 2.0f; phase = 2; return false;
                    default:
                        if (Time.realtimeSinceStartup < waitUntil) return false;
                        bool ok = s.Progress.campaign.EncounterEvidence.Contains(encounterId);
                        Add(commission, "defeat target (API)", encounterId, "EncounterEvidence", "", ok ? "ok" : "not recorded", ok);
                        // walk away so the corpse's area does not hold the next teleport
                        return true;
                }
            });
        }

        static void Capture(string name)
        {
            string folder = Path.Combine(AbsOutput, "PlayTest"); Directory.CreateDirectory(folder);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png"));
        }

        static void Tick()
        {
            if (report == null || report.status != "running") { EditorApplication.update -= Tick; return; }
            if (!EditorApplication.isPlaying) { report.status = "aborted"; report.error = "Play stopped"; EditorApplication.update -= Tick; return; }
            try
            {
                if (plan.Count == 0) { report.status = report.failed == 0 ? "passed" : "failed"; EditorApplication.update -= Tick; Finish(); return; }
                if (plan.Peek()()) plan.Dequeue();
            }
            catch (Exception e) { report.status = "error"; report.error = e.ToString(); EditorApplication.update -= Tick; Finish(); }
        }

        static void Finish()
        {
            var s = LiveSession; if (s != null) s.DetailRequested -= OnDetail;
            File.WriteAllText(Path.Combine(AbsOutput, "playtest303.json"), JsonUtility.ToJson(report, true));
        }

        static string PlayTestStatus() => report == null ? "{\"status\":\"idle\"}" : JsonUtility.ToJson(report, true);
    }
}
