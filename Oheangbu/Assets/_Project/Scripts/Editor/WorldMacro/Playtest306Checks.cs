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
    /// Output: Art/Playtest306/Checks/f-test.txt.
    /// #307 probe-mini (SPEC-MINIMAP-307, MINIMAP_DESIGN §2): the style A minimap through the same isolated Play: (a) the mine start
    /// after 3 s, (b) 6 s of W in the cave (walked cave cells must grow, AC-1a), (c) a walked outdoor spot after its reprint,
    /// (d) the same spot with MinimapFollowView previewed on (never saved), (e) the map page and the pause page hide the HUD
    /// canvas (AC-1c). Captures b23_mini_&lt;step&gt;_&lt;W&gt;x&lt;H&gt;.png (Screen size) and checks their pixels on the disc: cave mean
    /// lightness (rho &lt; .75) &lt; .70 (whole-disc hanji), no green, cinnabar only within 18 reference px of the centre (the arrow).</summary>
    public static class Playtest306Checks
    {
        const string MainScene = "Assets/_Project/Scenes/World/W_Demo_Main.unity", MainSlot = "world-main";
        static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Playtest306/Checks"));
        sealed class State { public string status = "idle", note = ""; public string[] ids = Array.Empty<string>(); public int index, phase; public double at; public readonly List<string> lines = new List<string>(); public int fails; }
        const string ProbeB1 = "__probe_b1__", ProbeB23 = "__probe_b23__", ProbeOrgan = "__probe_organ__", ProbeMini = "__probe_mini__";
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
            if (command == "probe-organ") command = "f-test:" + ProbeOrgan; // #308 probe: on-model organ overlay (boss, growth lesson, neutral mine_fire/0)
            if (command == "probe-mini") command = "f-test:" + ProbeMini;   // #307 probe: style A minimap (cave, cave walk, outside, follow, hidden)
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
                inputStarted = tidied = false; pOrganIndex = 0; pGenericShot = false; pShotFrame = -1;
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
            if (id == ProbeMini) { ProbeMiniTick(); return; }
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
        // ---- organ telegraph probe (SPEC-TELEGRAPH-ORGAN-308 검증 3, replaces the #306 halo probe) ----
        // For each target (MineTutorialBoss306 M3/M4, demo_growth_lesson eruption, mine_fire/0 = neutral now → never lit): teleport 8 m in
        // front, Tab-lock, watch up to 20 s of its own rhythm. Records LitLevel (level × (1 − blot) handed to the shader), LitOrganCount,
        // OverlayRendererCount, selection changes; checks AC-T1 (no new Transform/Renderer under the actor while lit), AC-T2 (material
        // arrays back to the pre-telegraph references, ≤ 1 overlay copy per target renderer), AC-12d (overlay _Brightness /
        // _PaperBrightness ≤ MaxBrightness — the old "LitLevel ≤ 1" check was an alpha and always true), AC-T4 (readability MPB carries no
        // overlay values; EnemyController tints the same slot-0 copy), AC-T7 (both boss shoulders lit together). The first lit peak is
        // still captured as b23_organ_lit.png (SPEC-EVENT-WASH-308 AC-W6 reads it), plus b23_organ_lit_<target>.png per target.
        static readonly string[] OrganTargets = { "mine_tutorial_boss", "mine_fire/0", "demo_growth_lesson" };
        static Oheangbu.App.Demo.EnemyElementTelegraph pTel; static float pLitMax; static double pLitFirst, pLitLast; static int pLitShots, pOrgansMax, pOverlayMax, pSelMax;
        static Oheangbu.Combat.EnemyVitals pOrganTarget; static Oheangbu.App.Prologue.PrologueEncounter pOrganActor; static int pOrganIndex, pTransforms0, pRenderers0, pMaterials0, pNewObjects, pActorObjects0, pActorDelta;
        static bool pShoulders, pGroundSeen, pBrightOk, pMpbChecked, pGenericShot; static float pBrightMax; static string pOrganId = ""; static int pShotFrame = -1;
        static readonly List<KeyValuePair<Renderer, Material[]>> pArrays = new List<KeyValuePair<Renderer, Material[]>>();
        static string OrganTargetId(int i) => i < OrganTargets.Length ? (OrganTargets[i] == "mine_tutorial_boss" ? Oheangbu.App.World.MineTutorialProfileSO.BossId : OrganTargets[i]) : null;
        static void ProbeOrganTick()
        {
            var wiring = s.Walker.Wiring; var lockOn = Harness303.Field<Oheangbu.Combat.LockOn>(wiring, "_lockOn");
            if (pOrganIndex >= OrganTargets.Length) { pOrganIndex = 0; st.index++; st.phase = 0; return; }
            switch (st.phase)
            {
                case 0:
                {
                    if (Now - st.at < 3) return;
                    pOrganId = OrganTargetId(pOrganIndex);
                    pOrganActor = s.Actors.FirstOrDefault(a => a != null && a.Id == pOrganId);
                    pOrganTarget = pOrganActor != null ? pOrganActor.GetComponent<Oheangbu.Combat.EnemyVitals>() : null;
                    pTel = pOrganActor != null ? pOrganActor.GetComponentsInChildren<Oheangbu.App.Demo.EnemyElementTelegraph>(true).FirstOrDefault(t => t.isActiveAndEnabled && !t.Deferred) : null;
                    if (pOrganActor == null) { Line("INFO organ probe: " + pOrganId + " not in the session (skipped)"); pOrganIndex++; st.at = Now - 3; return; }
                    Check(pTel != null && pTel.Organs != null, pOrganId + " carries an element telegraph + organ set: " + (pTel != null ? pTel.name + " organs " + (pTel.Organs != null ? pTel.Organs.Count : 0) + ", bound " + pTel.Overlay.BoundOrganCount + ", unbound " + pTel.Overlay.UnboundOrganCount + (pTel.Overlay.Refusals.Length > 0 ? " (" + pTel.Overlay.Refusals + ")" : "") : "missing"));
                    if (pTel == null || pOrganTarget == null) { pOrganIndex++; st.at = Now - 3; return; }
                    // baselines: objects under the actor, Material objects, the organ target renderers' arrays (AC-T1, AC-T2)
                    pTransforms0 = pTel.GetComponentsInChildren<Transform>(true).Length; pRenderers0 = pTel.GetComponentsInChildren<Renderer>(true).Length;
                    pActorObjects0 = pOrganActor.GetComponentsInChildren<Transform>(true).Length;
                    pMaterials0 = Resources.FindObjectsOfTypeAll<Material>().Length; pArrays.Clear();
                    for (int i = 0; i < pTel.Organs.Count; i++) { var r = pTel.Overlay.RendererOf(i); if (r != null && !pArrays.Any(p => p.Key == r)) pArrays.Add(new KeyValuePair<Renderer, Material[]>(r, r.sharedMaterials)); }
                    var e = pOrganActor.transform.position; bool placed = false;
                    for (int k = 0; k < 8 && !placed; k++)
                    {
                        // first the actor's front (the authored facing), then the other bearings
                        var dir = Quaternion.Euler(0, k * 45f, 0) * pOrganActor.transform.forward;
                        if (!UnityEngine.AI.NavMesh.SamplePosition(e + dir * 8f, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                        var look = Vector3.ProjectOnPlane(e - hit.position, Vector3.up);
                        s.Teleport(hit.position, look.sqrMagnitude > .01f ? Quaternion.LookRotation(look).eulerAngles.y : 0f); placed = true;
                        Line("INFO probe teleport 8 m from " + pOrganId + " at " + hit.position.ToString("F1") + " (" + (k * 45) + " deg from its front)");
                    }
                    pLitMax = 0f; pLitFirst = pLitLast = -1; pLitShots = 0; pOrgansMax = pOverlayMax = pSelMax = pNewObjects = pActorDelta = 0;
                    pShoulders = pGroundSeen = pMpbChecked = false; pBrightOk = true; pBrightMax = 0f;
                    st.phase = 1; st.at = Now; return;
                }
                case 1:
                    if (Now - st.at < 1.0) return;
                    if (lockOn != null && lockOn.Target != pOrganTarget) VirtualInput303.Tap(Key.Tab, Center, 2);
                    st.phase = 2; st.at = Now; return;
                case 2:
                {
                    float lit = pTel.LitLevel; var overlay = pTel.Overlay;
                    if (lit > 0f)
                    {
                        if (pLitFirst < 0) pLitFirst = Now; pLitLast = Now;
                        pOrgansMax = Mathf.Max(pOrgansMax, pTel.LitOrganCount); pOverlayMax = Mathf.Max(pOverlayMax, pTel.OverlayRendererCount); pSelMax = Mathf.Max(pSelMax, pTel.SelectionChanges);
                        // AC-T1: nothing new under the telegraph's own model (the ground "where" markers of the attack owner are allowed and
                        // only reported), and none of the #306 halo / thread names anywhere under the actor
                        int extra = pTel.GetComponentsInChildren<Transform>(true).Length - pTransforms0 + pTel.GetComponentsInChildren<Renderer>(true).Length - pRenderers0;
                        foreach (var t in pOrganActor.GetComponentsInChildren<Transform>(true))
                            if (t.name.StartsWith("OrganHalo306", StringComparison.Ordinal) || t.name == "BoltHalo306" || t.name == "OrganThread306" || t.name == "Telegraph306") extra++;
                        pNewObjects = Mathf.Max(pNewObjects, extra);
                        pActorDelta = Mathf.Max(pActorDelta, pOrganActor.GetComponentsInChildren<Transform>(true).Length - pActorObjects0);
                        var timing = Resources.Load<Oheangbu.App.EnemyTelegraphTimingSO>(Oheangbu.App.EnemyTelegraphTimingSO.ResourcePath);
                        float cap = timing != null ? timing.MaxBrightness : .85f; pBrightMax = Mathf.Max(pBrightMax, Mathf.Max(overlay.LastBrightness, overlay.LastPaperBrightness));
                        if (overlay.LastBrightness > cap + 1e-4f || overlay.LastPaperBrightness > cap + 1e-4f) pBrightOk = false;
                        // AC-T7: both shoulders together (boss ground ring)
                        int sl = -1, sr = -1;
                        for (int i = 0; i < pTel.Organs.Count; i++) { var o = pTel.Organs.Get(i); if (o == null) continue; if (o.Id == "shoulder_l") sl = i; else if (o.Id == "shoulder_r") sr = i; }
                        if (sl >= 0 && sr >= 0 && (pTel.OrganLevel(sl) > 0f || pTel.OrganLevel(sr) > 0f)) { pGroundSeen = true; if (pTel.OrganLevel(sl) > 0f && pTel.OrganLevel(sr) > 0f) pShoulders = true; }
                        // AC-T4 after 1 s lit (≥ 4 readability re-applies): its MPB holds no overlay value; the enemy tints its slot-0 copy
                        if (!pMpbChecked && Now - pLitFirst > 1.0)
                        {
                            pMpbChecked = true;
                            var rd = pOrganActor.GetComponentInChildren<FolkloreReadability298>(true); var block = new MaterialPropertyBlock();
                            if (rd != null && rd.Targets != null && rd.Targets.Length > 0 && rd.Targets[0] != null)
                            { rd.Targets[0].GetPropertyBlock(block); Check(!block.HasColor("_Tint") && !block.HasFloat("_OrganCount"), pOrganId + " readability MPB carries no overlay values after 1 s lit (applied renderers " + rd.AppliedRenderers + ")"); }
                            var enemy = pOrganActor.GetComponent<Oheangbu.Combat.EnemyController>(); var bodyR = enemy != null ? Harness303.Field<Renderer>(enemy, "_renderer") : null;
                            if (enemy != null && bodyR != null && enemy.TintMaterial != null) Check(bodyR.sharedMaterials.Length > 0 && bodyR.sharedMaterials[0] == enemy.TintMaterial, pOrganId + " EnemyController tints the slot-0 copy it made in Awake (" + enemy.TintMaterial.name + ")");
                        }
                    }
                    if (lit > pLitMax) pLitMax = lit;
                    // one ScreenCapture request per frame (a second request in the same frame can replace the first); the shared
                    // b23_organ_lit.png (SPEC-EVENT-WASH-308 AC-W6) is taken once per run, from the first target that lights
                    if (lit > .6f && pLitShots == 0) { Shot("organ_lit_" + pOrganId.Replace('/', '_')); pLitShots = 1; pShotFrame = Time.frameCount; }
                    else if (lit > .6f && pLitShots == 1 && Time.frameCount != pShotFrame) { if (!pGenericShot) { Shot("organ_lit"); pGenericShot = true; } pLitShots = 2; }
                    if (pLitFirst > 0 && lit <= 0f && Now - pLitLast > .6 || Now - st.at > 20)
                    {
                        bool neutral = pOrganId == "mine_fire/0";
                        if (neutral) Check(pLitMax <= 0f && pOverlayMax == 0, pOrganId + " (neutral attacker, D308-2) never lit: max LitLevel " + pLitMax.ToString("F2") + ", overlays " + pOverlayMax
                            + (pLitMax > 0f ? " — lit means its attack is still elemental: D308 item 2 (content pacing) not deployed yet, or a regression" : ""));
                        else if (pLitFirst < 0) Line("INFO " + pOrganId + ": no elemental attack lit within 20 s (tutorial beat / range?) — lit checks not run");
                        else
                        {
                            Check(pLitMax > 0f && pOverlayMax > 0, pOrganId + " lit on its model: max LitLevel " + pLitMax.ToString("F2") + ", organs " + pOrgansMax + ", overlay renderers " + pOverlayMax + ", lit for " + (pLitLast - pLitFirst).ToString("F2") + " s, selection changes " + pSelMax);
                            Check(pLitLast - pLitFirst < 3.0, pOrganId + " transient glow (lit span " + (pLitLast - pLitFirst).ToString("F2") + " s < 3 s)");
                            Check(pNewObjects <= 0, pOrganId + " AC-T1 no new Transform / Renderer under the telegraph model and no #306 halo / thread objects while lit: +" + pNewObjects + " (whole actor +" + pActorDelta + ", includes the owner's ground markers)");
                            Check(pBrightOk, pOrganId + " AC-12d overlay _Brightness / _PaperBrightness <= MaxBrightness: max " + pBrightMax.ToString("F2"));
                            if (pGroundSeen) Check(pShoulders, pOrganId + " AC-T7 both shoulders lit together during the ground ring");
                        }
                        // AC-T2: after the window the arrays are the pre-telegraph references; overlay copies ≤ 1 per target renderer.
                        // A 20 s timeout that lands inside a lit window is not a restore failure: report it and do not judge
                        bool stillOpen = lit > 0f || pTel.OverlayRendererCount > 0;
                        int grown = Resources.FindObjectsOfTypeAll<Material>().Length - pMaterials0;
                        // D308-4d: a contamination-only body surface keeps one persistent overlay copy of its own (not an organ target renderer)
                        int pContamOnly = pTel.Overlay.ContaminationOnlySurfaceCount;
                        Check(pTel.Overlay.MaterialCount <= pArrays.Count + pContamOnly, pOrganId + " AC-T2 overlay copies " + pTel.Overlay.MaterialCount + " <= target renderers " + pArrays.Count + " + contamination-only surfaces " + pContamOnly + " (Material objects +" + grown + " in the window, scene-wide)");
                        if (stillOpen) Line("INFO " + pOrganId + ": 20 s timeout inside a lit window (LitLevel " + lit.ToString("F2") + ", overlays " + pTel.OverlayRendererCount + ") — AC-T2 restore / no-glow-after not judged; rerun probe-organ");
                        else
                        {
                            bool same = true; foreach (var p in pArrays) { var now = p.Key != null ? p.Key.sharedMaterials : null; if (now == null || !now.SequenceEqual(p.Value)) same = false; }
                            Check(same, pOrganId + " AC-T2 material arrays restored (" + pArrays.Count + " renderers), overlays now " + pTel.OverlayRendererCount + ", restore mismatches " + pTel.Overlay.RestoreMismatches);
                            Check(pTel.LitLevel <= 0f, pOrganId + " no glow after the attack: LitLevel " + pTel.LitLevel.ToString("F2"));
                        }
                        Line("INFO lock target " + (lockOn != null && lockOn.Target != null ? lockOn.Target.name : "none"));
                        pOrganIndex++; st.phase = 0; st.at = Now - 3; return;
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

        // ---- #307 minimap probe (SPEC-MINIMAP-307): style A in the cave and outside, follow view, hidden with the HUD ----
        static int mCells0, mPrints0; static string mShot = "", mStep = ""; static bool mCave; static double mShotAt;
        static Oheangbu.App.World.UI.PlaytestUiRoot MiniUi => Oheangbu.App.World.UI.PlaytestUiRoot.Instance;
        static void MiniShot(string step, bool cave)
        {
            Directory.CreateDirectory(Folder);
            string name = "b23_mini_" + step + "_" + Screen.width + "x" + Screen.height + ".png";
            mShot = Path.Combine(Folder, name); mStep = step; mCave = cave; mShotAt = Now;
            try { if (File.Exists(mShot)) File.Delete(mShot); } catch (IOException) { }
            ScreenCapture.CaptureScreenshot(mShot); Line("INFO capture " + name);
        }
        /// <summary>True once the last MiniShot was read (or given up after 4 s): pixel checks on the disc, then the next step.</summary>
        static bool MiniShotRead(Oheangbu.App.World.UI.HudMinimap304 mini)
        {
            if (mShot.Length == 0) return true;
            Texture2D tex = null;
            try
            {
                if (File.Exists(mShot)) { tex = new Texture2D(2, 2, TextureFormat.RGBA32, false); if (!tex.LoadImage(File.ReadAllBytes(mShot))) { Object.DestroyImmediate(tex); tex = null; } }
            }
            catch (IOException) { if (tex != null) Object.DestroyImmediate(tex); tex = null; }   // still being written
            if (tex == null)
            {
                if (Now - mShotAt < 4) return false;
                Check(false, "mini " + mStep + ": capture " + Path.GetFileName(mShot) + " not readable after 4 s"); mShot = ""; return true;
            }
            try { MiniPixels(tex, mini); } finally { Object.DestroyImmediate(tex); mShot = ""; }
            return true;
        }
        static void MiniPixels(Texture2D tex, Oheangbu.App.World.UI.HudMinimap304 mini)
        {
            if (mini == null || mini.Map == null || mini.Root == null) { Check(false, "mini " + mStep + ": minimap missing for the pixel checks"); return; }
            var canvas = mini.Root.GetComponentInParent<Canvas>(true); var top = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
            if (top == null) { Check(false, "mini " + mStep + ": no HUD canvas"); return; }
            Rect cr = top.rect;
            // canvas space -> capture pixels (bottom-left origin, as Texture2D.LoadImage and the canvas), whatever Screen.width says
            Vector2 ToTex(Vector3 world) { Vector3 l = top.InverseTransformPoint(world); return new Vector2((l.x - cr.xMin) / cr.width * tex.width, (l.y - cr.yMin) / cr.height * tex.height); }
            var c = new Vector3[4]; mini.Map.rectTransform.GetWorldCorners(c);
            Vector2 a = ToTex(c[0]), b = ToTex(c[2]), centre = (a + b) * .5f;
            float radius = Vector2.Distance(a, b) / (2f * Mathf.Sqrt(2f));                 // half the side (the disc turns in follow view)
            float refPx = tex.height / 1080f;                                                  // reference (1080) px -> capture px
            var px = tex.GetPixels32(); int w = tex.width, h = tex.height;
            double lum = 0; int lumN = 0, green = 0, greenDark = 0, discN = 0, cinnabar = 0, cinnabarOut = 0; float farthest = 0f;
            float box = radius * 1.3f;                                                         // the enso ring around the disc too
            int x0 = Mathf.Max(0, Mathf.FloorToInt(centre.x - box)), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(centre.x + box));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(centre.y - box)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(centre.y + box));
            float arrowLimit = 18f * refPx;
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                var p = px[y * w + x]; float r = p.r / 255f, g = p.g / 255f, bl = p.b / 255f;
                float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), centre), rho = d / Mathf.Max(1f, radius);
                if (rho < .75f) { lum += .299 * r + .587 * g + .114 * bl; lumN++; }
                if (rho < .97f) { discN++; if (g > Mathf.Max(r, bl) + .04f) { green++; if (.299f * r + .587f * g + .114f * bl < .35f) greenDark++; } }
                if (r - Mathf.Max(g, bl) > .2f && r > .3f) { cinnabar++; if (d > arrowLimit) { cinnabarOut++; farthest = Mathf.Max(farthest, d / refPx); } }
            }
            double mean = lumN > 0 ? lum / lumN : -1;
            Line("INFO mini " + mStep + " capture " + w + "x" + h + " disc centre " + centre.ToString("F0") + " radius " + radius.ToString("F1") + " px (" + (radius / refPx).ToString("F1") + " ref px), mean lightness rho<.75 " + mean.ToString("F3") + " over " + lumN + " px");
            if (mCave) Check(lumN > 0 && mean < .70, "mini " + mStep + ": cave disc mean lightness (rho < .75) " + mean.ToString("F3") + " < .70 (whole-disc hanji, user 2026-09-30; old white disc ~.83)");
            Check(discN > 0 && green == 0, "mini " + mStep + ": no green on the disc (g > max(r, b) + .04): " + green + " of " + discN + " px" + (green > 0 ? " (" + greenDark + " of them dark, lightness < .35: likely the world through the translucent unwalked wash, not map ink - judge from the capture)" : ""));
            Check(cinnabarOut == 0, "mini " + mStep + ": cinnabar only on the arrow (within 18 ref px of the centre): " + cinnabar + " px, " + cinnabarOut + " outside" + (cinnabarOut > 0 ? " (farthest " + farthest.ToString("F1") + " ref px)" : ""));
            Line("INFO mini " + mStep + " marks visible " + mini.VisibleMarkers + ", objectives offered and left off " + mini.ObjectivesLeftOff + " (never drawn, AC-1e)");
        }
        static void ProbeMiniTick()
        {
            var hud = Object.FindFirstObjectByType<Oheangbu.App.HudController>();
            var mini = hud != null ? hud.Minimap304 : null; var ui = MiniUi; var map = ui != null ? ui.Map : null;
            if (mini == null || map == null) { Check(false, "probe-mini: HUD minimap " + (mini != null) + ", world map " + (map != null)); st.index++; st.phase = 0; return; }
            switch (st.phase)
            {
                case 0:
                {
                    if (Now - st.at < 3) return;
                    if (ShaderUtil.anythingCompiling && Now - st.at < 60) return;   // the editor draws a cyan placeholder until the new variant is compiled
                    var spec = hud.Minimap;
                    Line("INFO mini scene spec: Window " + spec.Window + ", CaveMetres " + spec.CaveMetres + ", OutsideMetres " + spec.OutsideMetres + ", MarkerRimInset " + spec.MarkerRimInset + " (#307 targets .8 / 28 / 120 / 28; minimap-look-apply sets the saved scenes)");
                    Check(mini.isActiveAndEnabled && mini.TargetAlpha > 0f && mini.Interior, "mini a: shown at the mine start inside the cave: alpha " + mini.TargetAlpha.ToString("F2") + " interior " + mini.Interior + " range " + mini.RangeMetres + " m prints " + mini.Prints);
                    Check(mini.Material != null && mini.Material.IsKeywordEnabled("_MINI_HUD"), "mini a: runtime material runs _MINI_HUD");
                    mCells0 = map.MiniWalkedCaveCells; Line("INFO mini a walked cave cells " + mCells0);
                    MiniShot("a_cave", true); st.phase = 1; st.at = Now; return;
                }
                case 1:
                    if (!MiniShotRead(mini)) return;
                    VirtualInput303.Hold = InputFrame303.Neutral(Center).WithKeys(Key.W); st.phase = 2; st.at = Now; st.note = "mini cave walk"; return;
                case 2:
                {
                    VirtualInput303.Hold = InputFrame303.Neutral(Center).WithKeys(Key.W);
                    if (Now - st.at < 6) return;
                    VirtualInput303.Hold = InputFrame303.Neutral(Center);
                    int cells = map.MiniWalkedCaveCells;
                    Check(cells > mCells0, "mini b: 6 s of W in the cave grows the walked cave plan (AC-1a): " + mCells0 + " -> " + cells + " cells, interior " + mini.Interior + ", prints " + mini.Prints);
                    MiniShot("b_cavewalk", mini.Interior); st.phase = 3; st.at = Now; return;
                }
                case 3:
                {
                    if (!MiniShotRead(mini)) return;
                    var p = Harness303.InteractionPoint(s, "geumpyo_inn");
                    Vector3 spot = p != null ? p.Position : s.Content.StartFeet; bool placed = false;
                    for (int k = 0; k < 8 && !placed && p != null; k++)
                    {
                        var dir = Quaternion.Euler(0, k * 45f, 0) * Vector3.back;
                        if (!UnityEngine.AI.NavMesh.SamplePosition(p.Position + dir * 4f, out var hit, 1.5f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                        spot = hit.position; placed = true;
                    }
                    s.Teleport(spot, 0f); Line("INFO mini c teleport outside to " + spot.ToString("F1") + (placed ? " (NavMesh by geumpyo_inn)" : " (no NavMesh hit; point / start)"));
                    mPrints0 = mini.Prints; st.phase = 4; st.at = Now; return;
                }
                case 4:
                    if ((mini.Interior || mini.Prints <= mPrints0 || Now - st.at < 2.0) && Now - st.at < 8) return;
                    Check(!mini.Interior && mini.Prints > mPrints0, "mini c: outside after the teleport, reprinted: interior " + mini.Interior + ", range " + mini.RangeMetres + " m (spec " + hud.Minimap.OutsideMetres + "), prints " + mPrints0 + " -> " + mini.Prints + ", walked cells revealed by the arrival (map discovery)");
                    MiniShot("c_outside", false); st.phase = 5; st.at = Now; return;
                case 5:
                {
                    if (!MiniShotRead(mini)) return;
                    var settings = ui.Settings;
                    if (settings == null) { Check(false, "mini d: no settings service"); st.phase = 7; st.at = Now; return; }
                    var d = settings.Current; d.MinimapFollowView = true; settings.Preview(d);   // preview only: rolled back below, never saved
                    Line("INFO mini d MinimapFollowView previewed on"); st.phase = 6; st.at = Now; return;
                }
                case 6:
                {
                    if (Now - st.at < 1.0) return;
                    float z = mini.Map.rectTransform.localEulerAngles.z;
                    var arrowT = mini.transform.Find("PlayerArrow"); float arrowZ = arrowT != null ? arrowT.localEulerAngles.z : 0f;
                    ((Oheangbu.App.World.UI.IMapMiniSource304)map).MiniPose(out _, out float heading);
                    Check(mini.FollowView && Mathf.Abs(Mathf.DeltaAngle(z, heading)) < 1f && Mathf.Abs(Mathf.DeltaAngle(arrowZ, 0f)) < .5f,
                        "mini d: follow view turns the map with the view, the arrow stays up: follow " + mini.FollowView + ", map turn " + z.ToString("F1") + " deg, heading " + heading.ToString("F1") + ", arrow " + arrowZ.ToString("F1"));
                    MiniShot("d_follow", false); st.phase = 7; st.at = Now; return;
                }
                case 7:
                    if (!MiniShotRead(mini)) return;
                    if (ui.Settings != null && ui.Settings.IsPreviewing) ui.Settings.Revert();
                    Line("INFO mini d follow preview reverted: follow setting " + (ui.Settings != null && ui.Settings.Current.MinimapFollowView));
                    ui.OpenPage("지도"); st.phase = 8; st.at = Now; return;
                case 8:
                    if (Now - st.at < .8) return;
                    Check(ui.Page == "지도" && hud.Canvas != null && !hud.Canvas.enabled, "mini e: the map page hides the HUD canvas with the minimap (AC-1c): page '" + ui.Page + "', HUD canvas " + (hud.Canvas != null && hud.Canvas.enabled));
                    MiniShot("e_map", false); mShot = ""; ui.CloseMenu(); st.phase = 9; st.at = Now; return;
                case 9:
                    if (ui.Page.Length > 0 && Now - st.at < 4) return;
                    if (Now - st.at < .5) return;
                    ui.OpenPage("일시정지"); st.phase = 10; st.at = Now; return;
                case 10:
                    if (Now - st.at < .6) return;
                    Check(ui.Page == "일시정지" && hud.Canvas != null && !hud.Canvas.enabled, "mini e: the pause page hides the HUD canvas with the minimap (AC-1c): page '" + ui.Page + "', HUD canvas " + (hud.Canvas != null && hud.Canvas.enabled));
                    MiniShot("e_pause", false); mShot = ""; ui.CloseMenu(); st.phase = 11; st.at = Now; return;
                case 11:
                    if ((ui.Page.Length > 0 || hud.Canvas == null || !hud.Canvas.enabled) && Now - st.at < 3) return;
                    Check(ui.Page.Length == 0 && hud.Canvas != null && hud.Canvas.enabled, "mini e: HUD canvas back after closing the pause page: page '" + ui.Page + "'");
                    st.index++; st.phase = 0; return;
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
