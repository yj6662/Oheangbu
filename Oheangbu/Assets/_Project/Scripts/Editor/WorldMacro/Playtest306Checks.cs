using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#306 Play checks through the real input path (VirtualInput303 + Talk303 + Harness303 save isolation; automated,
    /// not manual play). f-test:&lt;pointId&gt;[,&lt;pointId&gt;…] — for each NPC: talk with F and page to the end with F (Talk303), then
    /// hold F for 45 frames and tap F inside the 0.3 s cooldown: no new DetailRequested / InteractionResolved may appear and
    /// the page stays closed; then a fresh F after 0.6 s must open it again (closed with Escape). f-status | f-abort.
    /// Output: Art/Playtest306/Checks/f-test.txt.</summary>
    public static class Playtest306Checks
    {
        const string MainScene = "Assets/_Project/Scenes/World/W_Demo_Main.unity", MainSlot = "world-main";
        static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Playtest306/Checks"));
        sealed class State { public string status = "idle", note = ""; public string[] ids = Array.Empty<string>(); public int index, phase; public double at; public readonly List<string> lines = new List<string>(); public int fails; }
        const string ProbeB1 = "__probe_b1__", ProbeB23 = "__probe_b23__", ProbeOrgan = "__probe_organ__";
        static State st = new State(); static readonly Isolation303 iso = new Isolation303(); static readonly Talk303 talk = new Talk303();
        static WorldMacroPlaytestSession s; static bool inputStarted, tidied, hooked; static int details, resolved; static bool subscribed;
        static Vector2 Center => new Vector2(Screen.width * .5f, Screen.height * .5f);
        static double Now => EditorApplication.timeSinceStartup;

        public static string Run(string command)
        {
            command = (command ?? "").Trim();
            if (command == "f-status") return st.status + " | " + st.note + " | " + string.Join(" / ", st.lines.Skip(Math.Max(0, st.lines.Count - 6)));
            if (command == "f-abort") { Finish("aborted"); return "aborted"; }
            if (command == "probe-b1") command = "f-test:" + ProbeB1;   // batch-1 probe: ink regen + natural solids (no NPC talk)
            if (command == "probe-b23") command = "f-test:" + ProbeB23; // batch-2/3 probe: HUD minimap + enemy HP stroke + one chunk pull per click
            if (command == "probe-organ") command = "f-test:" + ProbeOrgan; // #12 probe: mine_fire/0 organ halo lights for its fire attack
            if (command.StartsWith("f-test:", StringComparison.Ordinal))
            {
                if (st.status == "running") return "refused: already running";
                // a finished run can leave its suffix on the edit-mode session: Play exit reloads the scene backup taken at Play entry
                // after our cleanup ran (the scene file and saves are verified unchanged); nothing is running, so drop the stale suffix
                var stale = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
                if (stale != null && (stale.TestSaveSuffix ?? "").StartsWith("_c303", StringComparison.Ordinal)) { stale.TestSaveSuffix = ""; EditorUtility.ClearDirty(stale); }
                string why = Harness303.Prepare(iso, MainScene, MainSlot, "_c303_f306");
                if (why != null) return why;
                var session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
                Harness303.Apply(iso, session);
                st = new State { status = "running", ids = command.Substring(7).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray(), at = Now };
                st.lines.Add("#306 f-test " + DateTime.Now.ToString("s") + " scene " + MainScene + " isolated suffix " + iso.suffix + " — automated Play through VirtualInput303 (real Input System path), not manual play");
                inputStarted = tidied = false;
                if (!hooked) { EditorApplication.update += Tick; hooked = true; }
                EditorApplication.isPlaying = true;
                return "started f-test for " + string.Join(",", st.ids);
            }
            return "refused: unknown command " + command;
        }

        static void OnDetail(string t, string b) => details++;
        static void OnResolved(Oheangbu.Data.World.PrologueInteractionKind k, Vector3 w) => resolved++;
        static void OnDialogue(Oheangbu.App.World.UI.DialogueRequest306 r) => details++;   // #306: NPC speech = one dialogue request
        static void Subscribe() { if (subscribed || s == null) return; s.DetailRequested += OnDetail; s.InteractionResolved += OnResolved; s.DialogueRequested += OnDialogue; subscribed = true; }
        static void Unsubscribe() { if (subscribed && s != null) { s.DetailRequested -= OnDetail; s.InteractionResolved -= OnResolved; s.DialogueRequested -= OnDialogue; } subscribed = false; }
        static bool PageOpen => UiAdapter303.Page.Length > 0 || (s != null && s.GameplayInputBlocked);
        static void Line(string x) { st.lines.Add(x); }
        static void Check(bool ok, string what) { if (!ok) st.fails++; Line((ok ? "PASS " : "FAIL ") + what); }

        static void Tick()
        {
            if (st.status == "finishing" && !EditorApplication.isPlayingOrWillChangePlaymode) { Cleanup(); return; }   // delayCall can be lost across the Play exit
            if (st.status != "running") return;
            if (Now - st.at > 240) { Line("FAIL timeout at id " + (st.index < st.ids.Length ? st.ids[st.index] : "-") + " phase " + st.phase + " (" + st.note + ")"); st.fails++; Finish("timeout"); return; }
            if (!EditorApplication.isPlaying) return;
            if (s == null) { s = Object.FindFirstObjectByType<WorldMacroPlaytestSession>(); if (s == null) { st.note = "waiting for session"; return; } }
            if (!s.InitializationComplete || UiAdapter303.LoadingInProgress == true) { st.note = "waiting for init/loading"; return; }
            if (!tidied) { UiAdapter303.CloseMenu(); UiAdapter303.ReleaseGate(); tidied = true; return; }
            if (!inputStarted)
            {
                var assets = new List<InputActionAsset> { Harness303.Field<InputActionAsset>(s.Walker.Motor, "_actions"), Harness303.Field<InputActionAsset>(s.Walker.Drawing, "_actions") };
                Line("input: " + VirtualInput303.Begin(assets)); inputStarted = true; Subscribe(); return;
            }
            if (st.index >= st.ids.Length) { Finish(st.fails == 0 ? "PASS" : "FAIL"); return; }
            string id = st.ids[st.index];
            if (id == ProbeB1) { ProbeTick(); return; }
            if (id == ProbeB23) { ProbeB23Tick(); return; }
            if (id == ProbeOrgan) { ProbeOrganTick(); return; }
            switch (st.phase)
            {
                case 0:
                {
                    // the new journey starts in the mine, far from the village NPCs: teleport 4 m in front (first NavMesh hit of 8 bearings)
                    var p = Harness303.InteractionPoint(s, id);
                    if (p == null) { Check(false, id + " interaction point missing"); st.index++; return; }
                    bool placed = false;
                    for (int k = 0; k < 8 && !placed; k++)
                    {
                        var dir = Quaternion.Euler(0, k * 45f, 0) * Vector3.back;
                        if (!UnityEngine.AI.NavMesh.SamplePosition(p.Position + dir * 4f, out var hit, 1.5f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                        var look = Vector3.ProjectOnPlane(p.Position - hit.position, Vector3.up);
                        s.Teleport(hit.position, look.sqrMagnitude > .01f ? Quaternion.LookRotation(look).eulerAngles.y : 0f); placed = true;
                        Line("INFO " + id + " teleported to " + hit.position.ToString("F1") + " (" + (k * 45) + " deg bearing), point " + p.Position.ToString("F1"));
                    }
                    if (!placed) Line("INFO " + id + " no NavMesh within 4 m bearings; walking from the current position");
                    st.phase = 10; st.at = Now; return;
                }
                case 10:
                    if (Now - st.at < 1.0) return;
                    talk.WaitRestPresentation = false; talk.Begin(s, "F306", id, null, null, Folder, "f306_" + id); st.phase = 1; st.note = id + " talk"; return;
                case 1:
                    VirtualInput303.Hold = talk.Tick(Center);
                    if (talk.Status == "running") return;
                    Check(talk.Status == "done", id + " talk with F: " + talk.Status + " details=" + talk.Row.details + " resolved=" + talk.Row.resolved + " close=" + talk.Row.closeMethod + " " + talk.Row.detail);
                    Check(!talk.Row.closeMethod.Contains("Escape") && !talk.Row.closeMethod.Contains("adapter"), id + " closed with F alone (no Escape/adapter): " + talk.Row.closeMethod + " | capture " + talk.Row.capture);
                    if (talk.Status != "done") { st.index++; st.phase = 0; return; }
                    details = resolved = 0;
                    var held = new List<InputFrame303>(); for (int i = 0; i < 45; i++) held.Add(InputFrame303.Neutral(Center).WithKeys(Key.F));
                    VirtualInput303.Enqueue(held); st.phase = 2; st.at = Now; return;
                case 2:
                    VirtualInput303.Hold = InputFrame303.Neutral(Center);
                    if (VirtualInput303.Pending > 0) return;
                    if (Now - st.at < 1.0) return;
                    Check(details == 0 && resolved == 0 && !PageOpen, id + " holding F 45 frames after close: no reopen (details " + details + ", resolved " + resolved + ", page '" + UiAdapter303.Page + "')");
                    details = resolved = 0; VirtualInput303.Tap(Key.F, Center, 2); st.phase = 3; st.at = Now; return;
                case 3:
                    if (Now - st.at < .5) return;
                    // the release above ended < 0.3 s before this tap's press only if the cooldown is measured from the close;
                    // record, do not fail: the arming rule (fresh press after release) may legitimately open here
                    Line("INFO " + id + " fresh tap right after the held F: details " + details + ", resolved " + resolved + ", page '" + UiAdapter303.Page + "'");
                    if (PageOpen) { VirtualInput303.Tap(Key.Escape, Center, 1); st.phase = 4; st.at = Now; return; }
                    details = resolved = 0; st.phase = 5; st.at = Now; return;
                case 4:
                    if (PageOpen && Now - st.at < 3) { if (Now - st.at > 1.2 && VirtualInput303.Pending == 0) { VirtualInput303.Tap(Key.Escape, Center, 1); st.at = Now - .2; } return; }
                    details = resolved = 0; st.phase = 5; st.at = Now; return;
                case 5:
                    if (Now - st.at < .8) return;
                    VirtualInput303.Tap(Key.F, Center, 2); st.phase = 6; st.at = Now; return;
                case 6:
                    if (details + resolved == 0 && !PageOpen && Now - st.at < 1.5) return;
                    Check(details + resolved > 0 || PageOpen, id + " fresh F after 0.8 s opens again (details " + details + ", resolved " + resolved + ", page '" + UiAdapter303.Page + "')");
                    VirtualInput303.Tap(Key.Escape, Center, 1); st.phase = 7; st.at = Now; return;
                case 7:
                    if (PageOpen && Now - st.at < 4) { if (Now - st.at > 1.2 && VirtualInput303.Pending == 0) { VirtualInput303.Tap(Key.Escape, Center, 1); st.at = Now - .2; } return; }
                    Check(!PageOpen, id + " Escape closes the reopened page");
                    st.index++; st.phase = 0; return;
            }
        }

        static float probeInk1; static int probeActive0;
        static void ProbeTick()
        {
            var pool = Object.FindFirstObjectByType<CompactNaturalSolids>();
            var ink = Harness303.Field<Oheangbu.Combat.InkPool>(s.Walker.Wiring, "_ink");
            switch (st.phase)
            {
                case 0:
                    if (Now - st.at < 3) return;
                    Check(pool != null && pool.Ready, "natural solids ready: sheets " + (pool != null ? pool.SheetCount : 0) + ", candidates " + (pool != null ? pool.Candidates : 0) + ", cells " + (pool != null ? pool.Cells : 0) + ", prepare " + (pool != null ? pool.PrepareMs.ToString("F1") : "-") + " ms, layer " + (pool != null ? pool.Layer : -1) + (pool != null && pool.LayerMissing ? " MISSING" : ""));
                    Line("INFO natural solids at the mine start: active " + (pool != null ? pool.Active : 0) + ", peak " + (pool != null ? pool.PeakActive : 0) + ", last " + (pool != null ? pool.LastMs.ToString("F3") : "-") + " ms, peak " + (pool != null ? pool.PeakMs.ToString("F3") : "-") + " ms");
                    // forest band close-up spot of #305 (E305_001): trunks within the activate radius must get colliders
                    var spot = new Vector3(3625.9f, 600f, 2584.1f);
                    if (UnityEngine.AI.NavMesh.SamplePosition(new Vector3(spot.x, 150f, spot.z), out var nh, 60f, UnityEngine.AI.NavMesh.AllAreas)) spot = nh.position;
                    else if (Physics.Raycast(spot, Vector3.down, out var gh, 900f, ~0, QueryTriggerInteraction.Ignore)) spot = gh.point;
                    s.Teleport(spot, 180f); Line("INFO probe teleport " + spot.ToString("F1")); probeActive0 = pool != null ? pool.Active : 0; st.phase = 1; st.at = Now; return;
                case 1:
                    if (Now - st.at < 2.5) return;
                    Check(pool != null && pool.Active > 0, "natural solids near the #305 forest band: active " + (pool != null ? pool.Active : 0) + " (was " + probeActive0 + " at the mine), pool " + (pool != null ? pool.PoolSize : 0) + ", queries " + (pool != null ? pool.Queries : 0) + ", peak " + (pool != null ? pool.PeakMs.ToString("F3") : "-") + " ms, backlog " + (pool != null && pool.Backlog));
                    if (ink == null) { Check(false, "ink pool not found on the walker wiring"); st.index++; return; }
                    ink.Restore(1f); ink.SpendClamped(.3f); probeInk1 = ink.Value; st.phase = 2; st.at = Now; return;
                case 2:
                    if (Now - st.at < 4) return;
                    float gained = ink.Value - probeInk1;
                    Check(gained > .08f && ink.Value <= 1.0001f, "ink regen after spending 0.3: " + probeInk1.ToString("F3") + " -> " + ink.Value.ToString("F3") + " in 4 s (gain " + gained.ToString("F3") + "; TEST 0.05/s after a 1 s delay ~= 0.15)");
                    st.index++; st.phase = 0; return;
            }
        }

        // ---- batch 2/3 probe (SPEC-PLAYTEST-306 AC-1a/1b, AC-8a/8c, AC-9/10): real input (Tab lock, left click), isolated save ----
        static Oheangbu.Combat.EnemyVitals pv; static Oheangbu.Combat.HarvestAction ph; static Oheangbu.Combat.InkPool pInk; static readonly List<Behaviour> pHeld = new List<Behaviour>();
        static int pStarted, pChunks, pCanceled, pDamage; static float pHp0, pInk0; static string pCancel = "";
        static void PStarted(Oheangbu.Combat.EnemyVitals v, Vector3 p) => pStarted++;
        static void PChunk(Oheangbu.Combat.EnemyVitals v, Vector3 p, float ink) => pChunks++;
        static void PCancel(Oheangbu.Combat.HarvestCancelReason r) { pCanceled++; pCancel += r + " "; }
        static void PDamage(Oheangbu.Combat.EnemyDamageResult r) => pDamage++;
        static Oheangbu.Combat.PlayerVitals pVit;
        static void PPlayerHit(float d)
        {
            var frames = Environment.StackTrace.Split('\n').Select(x => x.Trim()).Where(x => x.StartsWith("at Oheangbu", StringComparison.Ordinal) && x.IndexOf("PPlayerHit", StringComparison.Ordinal) < 0).Take(5);
            Line("INFO player damaged " + d.ToString("F2") + " at " + Now.ToString("F2") + " phase " + st.phase + " <- " + string.Join(" | ", frames));
        }
        static void Shot(string name) { Directory.CreateDirectory(Folder); ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "b23_" + name + ".png")); Line("INFO capture b23_" + name + ".png"); }
        static void Click() { VirtualInput303.Enqueue(new[] { new InputFrame303 { Keys = Array.Empty<Key>(), Position = Center, Lmb = true, Tag = "lmb" }, new InputFrame303 { Keys = Array.Empty<Key>(), Position = Center, Lmb = true, Tag = "lmb" }, InputFrame303.Neutral(Center) }); }
        static void PUnhook() { if (ph != null) { ph.PullStarted -= PStarted; ph.ChunkExtracted -= PChunk; ph.PullCanceled -= PCancel; } if (pv != null) pv.DamageResolved -= PDamage; if (pVit != null) pVit.Damaged -= PPlayerHit; pVit = null; ph = null; pv = null; pInk = null; foreach (var h in pHeld) if (h != null) h.enabled = true; pHeld.Clear(); }
        // what is drawing near the target at a capture: playing particle systems and visible renderers with non-ink materials (diagnostic)
        static void LogEffectsNear(Vector3 at, string tag)
        {
            string Path(Transform t) { var sb = new StringBuilder(t.name); for (var q = t.parent; q != null && sb.Length < 160; q = q.parent) sb.Insert(0, q.name + "/"); return sb.ToString(); }
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                if (ps != null && ps.isPlaying && ps.particleCount > 0 && Vector3.Distance(ps.transform.position, at) < 8f)
                { var r = ps.GetComponent<ParticleSystemRenderer>(); Line("INFO " + tag + " particles " + ps.particleCount + " " + Path(ps.transform) + " mat " + (r != null && r.sharedMaterial != null ? r.sharedMaterial.name + "/" + r.sharedMaterial.shader.name : "-")); }
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r != null && r.isVisible && !(r is ParticleSystemRenderer) && r.sharedMaterial != null && Vector3.Distance(r.bounds.center, at) < 6f && r.bounds.size.magnitude < 6f)
                { string m = r.sharedMaterial.name + "/" + r.sharedMaterial.shader.name; if (m.IndexOf("Ink", StringComparison.OrdinalIgnoreCase) >= 0 && m.IndexOf("Wash", StringComparison.OrdinalIgnoreCase) < 0) continue; if (r is SkinnedMeshRenderer) continue; Line("INFO " + tag + " renderer " + Path(r.transform) + " mat " + m); }
        }
        // ---- organ telegraph probe (SPEC-PLAYTEST-306 #12): mine_fire/0 ranged fire attack lights its organ halo, LDR-capped ----
        static Oheangbu.App.Demo.EnemyElementTelegraph pTel; static float pLitMax; static double pLitFirst, pLitLast; static int pLitShots, pHalos;
        static Oheangbu.Combat.EnemyVitals pOrganTarget;
        static void ProbeOrganTick()
        {
            var wiring = s.Walker.Wiring; var lockOn = Harness303.Field<Oheangbu.Combat.LockOn>(wiring, "_lockOn");
            switch (st.phase)
            {
                case 0:
                {
                    if (Now - st.at < 3) return;
                    var actor = s.Actors.FirstOrDefault(a => a != null && a.Id == "mine_fire/0");
                    pOrganTarget = actor != null ? actor.GetComponent<Oheangbu.Combat.EnemyVitals>() : null;
                    pTel = actor != null ? actor.GetComponentsInChildren<Oheangbu.App.Demo.EnemyElementTelegraph>(true).FirstOrDefault(t => t.isActiveAndEnabled) : null;
                    Check(pTel != null && pTel.Organs != null, "mine_fire/0 carries an element telegraph + organ set: " + (pTel != null ? pTel.name + " organs " + (pTel.Organs != null) : "missing"));
                    if (pTel == null || pOrganTarget == null) { st.index++; return; }
                    var e = actor.transform.position; bool placed = false;
                    for (int k = 0; k < 8 && !placed; k++)
                    {
                        var dir = Quaternion.Euler(0, k * 45f, 0) * Vector3.back;
                        if (!UnityEngine.AI.NavMesh.SamplePosition(e + dir * 8f, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                        var look = Vector3.ProjectOnPlane(e - hit.position, Vector3.up);
                        s.Teleport(hit.position, look.sqrMagnitude > .01f ? Quaternion.LookRotation(look).eulerAngles.y : 0f); placed = true;
                        Line("INFO probe teleport 8 m from mine_fire/0 at " + hit.position.ToString("F1"));
                    }
                    pLitMax = 0f; pLitFirst = pLitLast = -1; pLitShots = 0; pHalos = 0;
                    st.phase = 1; st.at = Now; return;
                }
                case 1:
                    if (Now - st.at < 1.0) return;
                    if (lockOn != null && lockOn.Target != pOrganTarget) VirtualInput303.Tap(Key.Tab, Center, 2);
                    st.phase = 2; st.at = Now; return;
                case 2:
                {
                    // watch up to 14 s of its own attack rhythm (telegraph 1.2 + rest); capture the first lit peak
                    float lit = pTel.LitLevel;
                    if (lit > 0f) { if (pLitFirst < 0) pLitFirst = Now; pLitLast = Now; pHalos = Mathf.Max(pHalos, pTel.VisibleHaloCount); }
                    if (lit > pLitMax) pLitMax = lit;
                    if (lit > .6f && pLitShots == 0) { Shot("organ_lit"); pLitShots++; }
                    if (pLitFirst > 0 && lit <= 0f && Now - pLitLast > .6 || Now - st.at > 14)
                    {
                        Check(pLitMax > 0f, "organ halo lit during the fire attack: max LitLevel " + pLitMax.ToString("F2") + ", visible halos " + pHalos + (pLitFirst > 0 ? ", lit for " + (pLitLast - pLitFirst).ToString("F2") + " s" : ""));
                        Check(pLitMax <= 1.0001f, "organ halo LDR cap (LitLevel <= 1): " + pLitMax.ToString("F2"));
                        if (pLitFirst > 0) Check(pLitLast - pLitFirst < 3.0, "transient glow (lit span " + (pLitLast - pLitFirst).ToString("F2") + " s < 3 s; TEST plan 0.6-1.3 s + flight)");
                        Check(pTel.LitLevel <= 0f, "no glow after the attack: LitLevel " + pTel.LitLevel.ToString("F2"));
                        Line("INFO lock target " + (lockOn != null && lockOn.Target != null ? lockOn.Target.name : "none"));
                        st.index++; st.phase = 0; return;
                    }
                    return;
                }
            }
        }
        static void ProbeB23Tick()
        {
            var hud = Object.FindFirstObjectByType<Oheangbu.App.HudController>();
            var wiring = s.Walker.Wiring; var lockOn = Harness303.Field<Oheangbu.Combat.LockOn>(wiring, "_lockOn");
            switch (st.phase)
            {
                case 0:
                {
                    if (Now - st.at < 3) return;
                    var mini = hud != null ? hud.Minimap304 : null; var bear = hud != null ? hud.Bearing304 : null;
                    Check(mini != null && mini.isActiveAndEnabled && mini.TargetAlpha > 0f, "minimap shown at the mine start: " + (mini != null ? "alpha " + mini.TargetAlpha.ToString("F2") + " interior " + mini.Interior + " range " + mini.RangeMetres + " m prints " + mini.Prints : "missing"));
                    Check(bear != null && bear.TargetAlpha > 0f, "bearing line shown with no objective: alpha " + (bear != null ? bear.TargetAlpha.ToString("F2") : "missing"));
                    Shot("hud_mine");
                    var actor = s.Actors.FirstOrDefault(a => a != null && a.Id == "mine_beast/0");
                    pv = actor != null ? actor.GetComponent<Oheangbu.Combat.EnemyVitals>() : null;
                    if (pv == null) { Check(false, "mine_beast/0 not found"); st.index++; return; }
                    Check(Mathf.Approximately(pv.MaxHp, 24f) && Mathf.Approximately(pv.Hp, 24f), "mine_beast/0 HP tier: " + pv.Hp + "/" + pv.MaxHp + " (TEST 24, profile " + (pv.Profile != null ? pv.Profile.name : "-") + ")");
                    var e = actor.transform.position; bool placed = false;
                    for (int k = 0; k < 8 && !placed; k++)
                    {
                        var dir = Quaternion.Euler(0, k * 45f, 0) * Vector3.back;
                        if (!UnityEngine.AI.NavMesh.SamplePosition(e + dir * 4.5f, out var hit, 1.5f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                        var look = Vector3.ProjectOnPlane(e - hit.position, Vector3.up);
                        s.Teleport(hit.position, look.sqrMagnitude > .01f ? Quaternion.LookRotation(look).eulerAngles.y : 0f); placed = true;
                        Line("INFO probe teleport 4.5 m from mine_beast/0 at " + hit.position.ToString("F1"));
                    }
                    ph = wiring.Harvest; pInk = Harness303.Field<Oheangbu.Combat.InkPool>(wiring, "_ink");
                    if (ph != null) { ph.PullStarted += PStarted; ph.ChunkExtracted += PChunk; ph.PullCanceled += PCancel; }
                    pv.DamageResolved += PDamage;
                    pVit = s.Walker.Body.GetComponent<Oheangbu.Combat.PlayerVitals>(); if (pVit != null) pVit.Damaged += PPlayerHit;
                    // any hit (melee beast, the mine_fire/0 bolt) cancels a pull by design; hold the AI and movement of every enemy within 40 m
                    // for the chunk checks only (restored in PUnhook)
                    foreach (var a in s.Actors) if (a != null && Vector3.Distance(a.transform.position, e) < 40f)
                    { var ai = a.GetComponent<Oheangbu.Combat.EnemyController>(); if (ai != null && ai.enabled) { ai.enabled = false; pHeld.Add(ai); } if (a.enabled) { a.enabled = false; pHeld.Add(a); } }
                    Line("INFO enemy AI held within 40 m for the pull checks: " + pHeld.Count + " components");
                    st.phase = 1; st.at = Now; return;
                }
                case 1:
                    if (Now - st.at < 1.0) return;
                    if (lockOn != null && lockOn.Target != pv) VirtualInput303.Tap(Key.Tab, Center, 2);
                    st.phase = 2; st.at = Now; return;
                case 2:
                {
                    if (Now - st.at < .6) return;
                    Check(lockOn != null && lockOn.Target == pv, "Tab locks mine_beast/0: target " + (lockOn != null && lockOn.Target != null ? lockOn.Target.name : "none"));
                    var stroke = hud != null ? hud.TargetHpStroke304 : null;
                    Check(stroke != null && stroke.isActiveAndEnabled, "lock-on HP stroke (TargetHpStroke) present under the enso: " + (stroke != null ? stroke.name + " active " + stroke.isActiveAndEnabled : "missing"));
                    Shot("lock");
                    if (pInk != null) pInk.Restore(.3f);
                    pHp0 = pv.Hp; pInk0 = pInk != null ? pInk.Value : 0f; pStarted = pChunks = pCanceled = pDamage = 0; pCancel = "";
                    Click(); st.phase = 3; st.at = Now; return;
                }
                case 3:
                    // captures follow the pull itself: tear at >= 50 % progress, then the fly just after the snap
                    if (ph != null && ph.State == Oheangbu.Combat.HarvestState.Pulling && ph.PullProgress01 < .5f && Now - st.at < 3) return;
                    if (ph != null && ph.State != Oheangbu.Combat.HarvestState.Pulling && pChunks == 0 && pCanceled == 0 && Now - st.at < 3) return;
                    Line("INFO mid-pull: state " + (ph != null ? ph.State.ToString() : "-") + " progress " + (ph != null ? ph.PullProgress01.ToString("F2") : "-") + " after " + (Now - st.at).ToString("F2") + " s");
                    Shot("pull"); st.phase = 30; st.at = Now; return;
                case 30:
                    if (pChunks == 0 && pCanceled == 0 && Now - st.at < 3) return;
                    if (Now - st.at < .08) return;
                    Shot("fly"); LogEffectsNear(pv != null ? pv.transform.position : s.Walker.Body.transform.position, "fly"); st.phase = 4; st.at = Now; return;
                case 4:
                {
                    if (Now - st.at < 1.0) return;
                    var cfg = Harness303.Field<Oheangbu.Combat.CombatConfigSO>(wiring, "_config");
                    float dmg = cfg != null ? cfg.HarvestChunkDamage : 2f, chunk = cfg != null ? cfg.HarvestChunkInk : .3f;
                    float dealt = pHp0 - pv.Hp, gained = pInk != null ? pInk.Value - pInk0 : 0f;
                    Check(pStarted == 1 && pChunks == 1 && pCanceled == 0, "one click = one pull = one chunk: started " + pStarted + ", chunks " + pChunks + ", canceled " + pCanceled + " " + pCancel);
                    Check(pDamage == 1 && Mathf.Abs(dealt - dmg) < .01f, "one damage event of " + dmg + ": events " + pDamage + ", HP " + pHp0 + " -> " + pv.Hp);
                    Check(gained >= chunk - .01f && gained <= chunk + .12f, "ink +" + chunk + " at the snap (regen adds a little): " + pInk0.ToString("F3") + " -> " + (pInk != null ? pInk.Value.ToString("F3") : "-") + " (gain " + gained.ToString("F3") + ")");
                    Shot("after_chunk");
                    pStarted = 0; Click(); st.phase = 5; st.at = Now; return;
                }
                case 5:
                    if (Now - st.at < .4) return;
                    Check(pStarted == 0, "click inside the cooldown (" + (ph != null ? ph.CooldownRemaining.ToString("F2") : "-") + " s left) starts no pull: started " + pStarted);
                    st.phase = 6; st.at = Now; return;
                case 6:
                    if (ph != null && ph.CooldownRemaining > 0f && Now - st.at < 3) return;
                    if (Now - st.at < .2) return;
                    pStarted = pChunks = pDamage = 0; pCancel = ""; pHp0 = pv.Hp; Click(); st.phase = 7; st.at = Now; return;
                case 7:
                    if (Now - st.at < 1.0) return;
                    Check(pStarted == 1 && pChunks == 1 && pDamage == 1 && pv.Hp >= 1f, "second pull after the cooldown: started " + pStarted + ", chunks " + pChunks + ", damage events " + pDamage + ", HP " + pHp0 + " -> " + pv.Hp + (pCancel.Length > 0 ? " (cancels: " + pCancel + ")" : ""));
                    PUnhook(); st.index++; st.phase = 0; return;
            }
        }

        static void Finish(string how)
        {
            st.status = "finishing"; talk.End(); Unsubscribe(); PUnhook();
            try { VirtualInput303.End(out _); } catch (Exception) { }
            Line("RESULT " + how + " (fails " + st.fails + ")");
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            EditorApplication.delayCall += Cleanup;
        }

        static void Cleanup()
        {
            if (st.status != "finishing") return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.delayCall += Cleanup; return; }
            st.status = "cleaning";
            foreach (var c in Harness303.Cleanup(iso, Path.Combine(Folder, "f-test-run"))) Line(c.status + " " + c.id + " " + c.detail);
            Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, "f-test.txt"), string.Join("\n", st.lines), new UTF8Encoding(false));
            st.status = st.fails == 0 ? "done PASS" : "done FAIL"; s = null; inputStarted = tidied = false;
        }
    }
}
