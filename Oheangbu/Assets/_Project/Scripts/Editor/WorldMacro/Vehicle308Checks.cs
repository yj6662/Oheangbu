using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using Oheangbu.Drawing;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#308 마석 자동차 UX Play checks (SPEC-VEHICLE-UX-308 AC-V1..V7) through the real input path (VirtualInput303 keyboard
    /// made current every update, Harness303 save isolation on W_Demo_Main). Automated Play, not manual play. Placement teleports are
    /// test setup only (the summon itself never moves the player — measured). Queue-safe, no dialogs.
    ///   play            start (Edit mode, W_Demo_Main open and saved, assets + fields-derive done)
    ///   play-status     one line; play-abort stops and cleans up
    /// Sequence: bind → G inside a boss field (refused, no car move; D308-8b: no gesture, no action gate, one silent refusal) → G just
    /// outside a field facing it (front spot inside: the same silent refusal, AC-V1d) → G then a 1 hp hit before the call moment
    /// (D308-8c: the call stroke is cancelled, no car, no ink flow, gate free) → G far from the car (brush call stroke in the air, its ink
    /// flies to the spot, ink summon; AC-V9 + capture call_stroke / call_flow) → F at the door
    /// (held 45 frames: boards once, never exits) → seated pose bones / visibility / RuntimeState contract / V head hide (+2 captures)
    /// → fresh F exits (held F never re-boards) → G near the car (reversed call stroke, ink recall, no teleport) → G again (summon after recall).
    /// Output: Art/Playtest308/Vehicle/vehicle-checks.txt (+ ride_external.png, ride_seat.png, summon_*.png, call_stroke.png,
    /// call_flow.png, recall_stroke.png — each stroke capture is followed by the camera boom it was taken at; the summon spot is
    /// chosen with a free shoulder-camera boom so the captures show the normal view).</summary>
    public static class Vehicle308Checks
    {
        const string MainScene = Harness303.MainScene, MainSlot = Harness303.MainSlot;
        static string Folder => Vehicle308.Out;
        sealed class State { public string status = "idle", note = ""; public int phase, sub, fails; public double at, phaseAt, started; public readonly List<string> lines = new List<string>(); }
        static State st = new State(); static readonly Isolation303 iso = new Isolation303();
        static WorldMacroPlaytestSession s; static WorldMacroPalanquinSummon call; static WorldMacroPalanquinSeat seat; static VehicleRider308 rider; static VehicleUx308ProfileSO profile;
        static bool hooked, inputStarted, tidied;
        static int calls0, declined0, silent0, boards0, exits0, recalls0, materialize0, dissolve0, enabledCar0, maxDraws;
        static Vector3 feet0, car0; static bool carActive0, sawHidden, sawRecallGesture, sawGestureInField, sawGateInField;
        static readonly HashSet<string> phases = new HashSet<string>();
        // D308-8c call stroke watch (AC-V9)
        static int strokes0, flights0, arrivals0, awaits0, drawEvents, tipIn, tipAll; static Vector3 spotFeet; static float spotYaw; static bool spotSet; static bool sawStroke, sawPendant, sawDrawMode, gateBlocked, shotStroke, shotFlow;
        static double tapAt, gateFreeAt; static float hp0, gateBlockedGame, gateFreeGame, bodyOff; static DrawingInputController drawHooked;
        static WorldMacroPlayerAppearance strokeApp; static bool bodyKnown;
        // D308-8c capture readability: summon-spot search state (free camera boom preferred), head-silhouette samples, recall stroke capture
        const float HeadDiscRadius = .16f, BoomFreeShare = .85f;
        static int spotCursor, spotPass, spotTries, tipBehindHead; static bool shotRecall; static Transform strokeHead;
        static Transform appearanceRoot, appearanceParent0; static WorldMacroPlayerAppearance appearance0; static Vector3 rootLocal0; static Quaternion rootRot0;
        static Vector2 Center => new Vector2(Screen.width * .5f, Screen.height * .5f);
        static double Now => EditorApplication.timeSinceStartup;

        public static string Run(string command)
        {
            command = (command ?? "").Trim();
            if (command == "play-status") return st.status + " | phase " + st.phase + " | " + st.note + " | " + string.Join(" / ", st.lines.Skip(Math.Max(0, st.lines.Count - 5)));
            if (command == "play-abort") { if (st.status == "running") Finish("aborted"); return "aborted"; }
            if (command != "play") return "refused: unknown command " + command + " (play | play-status | play-abort)";
            if (st.status == "running" || st.status == "finishing") return "refused: already running";
            var stale = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if (stale != null && (stale.TestSaveSuffix ?? "").StartsWith("_c303", StringComparison.Ordinal)) { stale.TestSaveSuffix = ""; EditorUtility.ClearDirty(stale); }
            string why = Harness303.Prepare(iso, MainScene, MainSlot, "_c303_v308");
            if (why != null) return why;
            if (AssetDatabase.LoadAssetAtPath<VehicleUx308ProfileSO>(Vehicle308.ProfilePath) == null) return "refused: run Vehicle308 assets first (" + Vehicle308.ProfilePath + ")";
            Harness303.Apply(iso, Object.FindFirstObjectByType<WorldMacroPlaytestSession>());
            st = new State { status = "running", at = Now, phaseAt = Now, started = Now };
            st.lines.Add("#308 vehicle checks " + DateTime.Now.ToString("s") + " scene " + MainScene + " isolated suffix " + iso.suffix + " — automated Play through VirtualInput303 (real Input System path), not manual play");
            s = null; call = null; seat = null; rider = null; profile = null; inputStarted = tidied = false; phases.Clear(); spotSet = false; sawGestureInField = sawGateInField = false; tapAt = 0; UnhookDrawing();
            spotCursor = spotPass = spotTries = 0; strokeHead = null;
            if (!hooked) { EditorApplication.update += Tick; hooked = true; }
            EditorApplication.isPlaying = true;
            return "started vehicle checks";
        }

        static void Line(string x) => st.lines.Add(x);
        static void Check(bool ok, string what) { if (!ok) st.fails++; Line((ok ? "PASS " : "FAIL ") + what); }
        static void Next(int phase, string note) { st.phase = phase; st.sub = 0; st.phaseAt = st.at = Now; st.note = note; }
        static float Since => (float)(Now - st.at);
        // gameplay input open (no menu, dialogue, card or pause); a tap is held back until then (bounded by the phase timeout)
        static bool InputOpen => !s.GameplayInputBlocked && (PlaytestUiRoot.Instance == null || PlaytestUiRoot.Instance.Gate == null || !PlaytestUiRoot.Instance.Gate.InputBlocked);
        static string Prompt => WorldMacroPlaytestHudPresenter.PromptText(s) ?? "";
        static Vector3 Feet => s.Walker.Body.transform.position;
        static float Flat(Vector3 a, Vector3 b) => Harness303.Flat(a, b);
        static void Shot(string name) { Directory.CreateDirectory(Folder); ScreenCapture.CaptureScreenshot(Path.Combine(Folder, name + ".png")); Line("INFO capture " + name + ".png"); }
        static int EnabledCarRenderers() => call == null || call.Vehicle == null ? 0 : call.Vehicle.GetComponentsInChildren<MeshRenderer>(true).Count(r => r.enabled);
        static void Watch()
        {
            // D308-8c: first — the call stroke (and the cancel test) runs before the first summon creates the ink component
            WatchStroke();
            var ink = call != null ? call.Ink308 : null; if (ink == null) return;
            if (ink.Busy) phases.Add(ink.Current.ToString());
            if (ink.CarHidden) sawHidden = true;
            maxDraws = Mathf.Max(maxDraws, ink.ShellDrawsLastFrame);
            if (call.RecallGesture) sawRecallGesture = true;
        }

        static void Tick()
        {
            if (st.status == "finishing" && !EditorApplication.isPlayingOrWillChangePlaymode) { Cleanup(); return; }
            if (st.status != "running") return;
            if (PlaytestUiRoot.Instance != null && PlaytestUiRoot.Instance.SkipTutorial306ForHarness()) Line("INFO skipped the #306 tutorial cards (harness)");
            if (Now - st.phaseAt > 60 || Now - st.started > 360) { Check(false, "timeout in phase " + st.phase + " (" + st.note + ") inputOpen=" + InputOpen + " sessionBlocked=" + s.GameplayInputBlocked + " gate=" + (PlaytestUiRoot.Instance != null && PlaytestUiRoot.Instance.Gate != null && PlaytestUiRoot.Instance.Gate.InputBlocked) + " page=" + (PlaytestUiRoot.Instance != null ? PlaytestUiRoot.Instance.Page : "") ); Finish("timeout"); return; }
            if (!EditorApplication.isPlaying) return;
            if (s == null) { s = Object.FindFirstObjectByType<WorldMacroPlaytestSession>(); st.note = "waiting for session"; return; }
            if (!s.InitializationComplete || UiAdapter303.LoadingInProgress == true) { st.note = "waiting for init/loading"; return; }
            if (!tidied) { UiAdapter303.CloseMenu(); UiAdapter303.ReleaseGate(); tidied = true; st.at = Now; return; }
            if (!inputStarted)
            {
                var assets = new List<InputActionAsset> { Harness303.Field<InputActionAsset>(s.Walker.Motor, "_actions"), Harness303.Field<InputActionAsset>(s.Walker.Drawing, "_actions") };
                Line("input: " + VirtualInput303.Begin(assets)); inputStarted = true; return;
            }
            Watch();
            try { Step(); }
            catch (Exception e) { Check(false, "exception in phase " + st.phase + ": " + e.Message); Finish("exception"); }
        }

        static void Step()
        {
            switch (st.phase)
            {
                case 0: // bind
                {
                    if (Since < 1.5) return;
                    call = s.DemoEscortSummon != null ? s.DemoEscortSummon : Object.FindFirstObjectByType<WorldMacroPalanquinSummon>();
                    seat = s.VehicleSeat308; profile = s.VehicleUxProfile308;
                    rider = seat != null ? seat.GetComponent<VehicleRider308>() : null;
                    Check(profile != null, "AC-V0 profile resolved (" + (profile != null ? "Resources/serialized" : "none") + ")");
                    Check(s.VehicleBossFields308 != null && s.VehicleBossFields308.Fields.Length > 0, "AC-V0 boss fields resolved (" + (s.VehicleBossFields308 != null ? s.VehicleBossFields308.Fields.Length : 0) + ")");
                    Check(call != null && seat != null && call.Seat == seat, "AC-V0 one summon + its seat bound");
                    if (call == null || seat == null || profile == null) { Finish("FAIL"); return; }
                    Check(seat.InteractKeyExternal, "AC-V4 seat E path off (InteractKeyExternal) — F routed by the session");
                    Check(rider != null, "AC-V6 VehicleRider308 added at run time on the seat");
                    Check(s.VehicleAvailable, "AC-V2 VehicleAvailable = opening commission only (no cheongryong gate): " + s.VehicleAvailable + ", cheongryong defeated " + (s.Progress?.defeated?.Contains("cheongryong") == true));
                    Line("INFO NaturalPresentation " + call.NaturalPresentation + ", car " + (call.IsRecalled ? "recalled" : Harness303.V(call.Vehicle.transform.position)) + ", fields " + string.Join(",", s.VehicleBossFields308.Fields.Select(f => f.Id)));
                    s.CombatActive = false; s.Cull();
                    // #308 review: the shell shader compiles synchronously (no cyan placeholder). Compile it here, before the timed
                    // phases — otherwise its first draw (~.8 s into the summon stroke) could hitch one frame up to maximumDeltaTime
                    // and push the stroke-length / gate-length checks over their one-frame tolerance on the first run after an import.
                    if (profile.ShellMaterial != null)
                    {
                        try { ShaderUtil.CompilePass(profile.ShellMaterial, 0, true); Line("INFO shell shader pass compiled before the timed phases (" + profile.ShellMaterial.shader.name + ")"); }
                        catch (Exception e) { Line("NOTE shell shader precompile failed: " + e.Message); }
                    }
                    Next(1, "boss field G"); return;
                }
                case 1: // G inside a boss field: refused with nothing visible (D308-8b), car untouched; then the front-spot case
                {
                    if (st.sub == 0)
                    {
                        var field = s.VehicleBossFields308.Fields.FirstOrDefault(f => f != null && f.Enabled && f.Id == "cheongryong") ?? s.VehicleBossFields308.Fields.FirstOrDefault(f => f != null && f.Enabled);
                        if (field == null) { Check(false, "AC-V1 no enabled boss field"); Next(2, "summon spot"); return; }
                        Vector3 centre = s.BossFieldCentre308(field); bool placed = false;
                        for (int k = 0; k < 8 && !placed; k++)
                        {
                            var probe = centre + Quaternion.Euler(0, k * 45f, 0) * Vector3.forward * Mathf.Min(field.Radius * .4f, 10f);
                            if (!NavMesh.SamplePosition(probe, out var hit, 8f, NavMesh.AllAreas) || !s.InVehicleBossField308(hit.position, 0f, out _)) continue;
                            s.Teleport(hit.position, k * 45f); placed = true; Line("INFO setup teleport into boss field " + field.Id + " at " + Harness303.V(hit.position));
                        }
                        if (!placed) { Check(false, "AC-V1 no NavMesh point inside boss field " + field.Id); Next(2, "summon spot"); return; }
                        st.sub = 1; st.at = Now; return;
                    }
                    if (st.sub == 1)
                    {
                        if (Since < 1.5 || !InputOpen) return;
                        Check(!s.VehicleBoardingAllowed308(seat), "AC-V1 boarding hook refuses while the player stands in a boss field");
                        // G next to the car is a recall (allowed in fields, D308-8), not the refused summon under test
                        if (call.ShouldRecallOnToggle308()) { Line("SKIP AC-V1b the car is within " + profile.RecallToggleDistance + " m, so G would recall - not measured"); st.sub = 3; st.at = Now; return; }
                        Arm();
                        VirtualInput303.Tap(Key.G, Center, 2); st.sub = 2; st.at = Now; return;
                    }
                    if (st.sub == 2)
                    {
                        WatchSilent();
                        if (Since < 3.0) return;
                        CheckSilentRefusal("player in the field");
                        st.sub = 3; st.at = Now; return;
                    }
                    // AC-V1d (D308-8b): the player just outside the field, facing it, so only the front spot (hull footprint) is inside
                    if (st.sub == 3)
                    {
                        var field = s.VehicleBossFields308.Fields.FirstOrDefault(f => f != null && f.Enabled && f.Id == "cheongryong") ?? s.VehicleBossFields308.Fields.FirstOrDefault(f => f != null && f.Enabled);
                        Vector3 centre = s.BossFieldCentre308(field); bool placed = false;
                        for (int k = 0; k < 8 && !placed; k++)
                            foreach (float extra in new[] { 1.5f, 3f, 4.5f })
                            {
                                var outward = Quaternion.Euler(0, k * 45f, 0) * Vector3.forward;
                                if (!NavMesh.SamplePosition(centre + outward * (field.Radius + extra), out var hit, 3f, NavMesh.AllAreas)) continue;
                                if (s.InVehicleBossField308(hit.position, 0f, out _) || !call.FrontSpotInBossField308(hit.position, -outward, out _)) continue;
                                s.Teleport(hit.position, k * 45f + 180f); placed = true;
                                Line("INFO setup teleport just outside boss field " + field.Id + " at " + Harness303.V(hit.position) + " facing it");
                                break;
                            }
                        if (!placed) { Line("SKIP AC-V1d no NavMesh point just outside " + field.Id + " whose front spot lies inside (not measured)"); Next(2, "summon spot"); return; }
                        st.sub = 4; st.at = Now; return;
                    }
                    if (st.sub == 4)
                    {
                        if (Since < 1.5 || !InputOpen) return;
                        var forward = s.Walker.Body.transform.forward;
                        if (s.InVehicleBossField308(Feet, 0f, out _) || !call.FrontSpotInBossField308(Feet, forward, out _))
                        { Line("SKIP AC-V1d the settled pose does not face the field (body forward " + Harness303.V(forward) + ") - not measured"); Next(2, "summon spot"); return; }
                        if (call.ShouldRecallOnToggle308()) { Line("SKIP AC-V1d the car is within " + profile.RecallToggleDistance + " m, so G would recall - not measured"); Next(2, "summon spot"); return; }
                        Arm();
                        VirtualInput303.Tap(Key.G, Center, 2); st.sub = 5; st.at = Now; return;
                    }
                    WatchSilent();
                    if (Since < 3.0) return;
                    CheckSilentRefusal("front spot in the field (AC-V1d)");
                    Next(2, "summon spot"); return;
                }
                case 2: // a spot outside every field, far from the car, where the front placement is free (read-only probe)
                {
                    if (st.sub == 0)
                    {
                        var start = Harness303.InteractionPoint(s, "escort_start");
                        Vector3 origin = start != null ? start.Position : s.LastSafeFeet;
                        float near = profile.RecallToggleDistance + 2f; bool placed = false;
                        // rings 2..7 (14–49 m) first, away from the station's own interaction points; 0..1 last.
                        // D308-8c: pass 0 takes only spots whose shoulder-camera boom is free (the 2026-10-03 spot had a wall behind it:
                        // the camera sat ~1 m behind the head and the head covered the stroke, so the captures judged nothing); the
                        // real boom is measured after the teleport settles (sub 1) and an obstructed spot is skipped. Pass 1 = any spot.
                        int[] rings = { 2, 3, 4, 5, 6, 7, 1, 0 };
                        int total = rings.Length * 12;
                        while (!placed && spotPass < 2)
                        {
                            for (int i = spotCursor; i < total; i++)
                            {
                                int ring = rings[i / 12], k = i % 12;
                                var dir = Quaternion.Euler(0, k * 30f, 0) * Vector3.forward;
                                if (!NavMesh.SamplePosition(origin + dir * (ring * 7f), out var hit, 3f, NavMesh.AllAreas)) continue;
                                if (s.InVehicleBossField308(hit.position, 6f, out _)) continue;
                                if (!call.IsRecalled && call.Vehicle.gameObject.activeInHierarchy && Flat(hit.position, call.Vehicle.transform.position) < near) continue;
                                if (spotPass == 0 && !BoomProbeFree(hit.position, k * 30f)) continue;
                                if (!call.TryFindPlacement(hit.position, dir, out _, out _)) continue;
                                s.Teleport(hit.position, k * 30f); placed = true; spotCursor = i + 1; spotFeet = hit.position; spotYaw = k * 30f; spotSet = true;
                                Line("INFO setup teleport to summon spot " + Harness303.V(hit.position) + " facing " + (k * 30) + " deg" + (spotPass == 0 ? " (camera boom probe free)" : " (no spot with a free camera boom: any free spot)"));
                                break;
                            }
                            if (!placed) { spotPass++; spotCursor = 0; }
                        }
                        if (!placed) { Check(false, "AC-V3 no free summon spot near escort_start"); Finish("FAIL"); return; }
                        st.sub = 1; st.at = Now; return;
                    }
                    if (Since < 1.5 || !InputOpen) return;
                    // the probe cannot see colliders that only wake near the player (#306 natural solids): the settled camera is the judge
                    if (BoomNow(out float boom, out float fullBoom))
                    {
                        bool pulled = boom < fullBoom * BoomFreeShare;
                        if (pulled && spotPass == 0 && spotTries < 6)
                        { spotTries++; Line("INFO summon spot rejected: camera boom " + Harness303.F(boom) + " of " + Harness303.F(fullBoom) + " m (pulled in) — next spot"); st.sub = 0; st.at = Now; return; }
                        Line((pulled ? "NOTE" : "INFO") + " summon spot camera boom " + Harness303.F(boom) + " of " + Harness303.F(fullBoom) + " m" + (pulled ? " — pulled in by an obstacle: the stroke captures are not the normal shoulder view" : " (normal shoulder view)"));
                    }
                    else Line("INFO summon spot camera boom not measured (no CameraRigController / config)");
                    Next(9, "G stroke cancelled by a hit"); return;
                }
                case 9: // D308-8c AC-V9: a hit before the call moment cancels the call stroke — no car, no ink flow, the action gate is free
                {
                    var vitals = s.Walker.Motor != null ? s.Walker.Motor.GetComponent<PlayerVitals>() : null;
                    if (st.sub == 0)
                    {
                        if (Since < 1.0 || !InputOpen) return;
                        if (vitals == null || !profile.CallStroke) { Line("SKIP AC-V9 hit cancel: " + (vitals == null ? "no PlayerVitals" : "CallStroke off") + " (not measured)"); Next(3, "G summon"); return; }
                        if (call.ShouldRecallOnToggle308()) { Line("SKIP AC-V9 hit cancel: the car is within " + profile.RecallToggleDistance + " m, so G would recall (not measured)"); Next(3, "G summon"); return; }
                        calls0 = call.SuccessfulCalls; declined0 = call.DeclinedGestures308; ArmStroke();
                        VirtualInput303.Tap(Key.G, Center, 2); st.sub = 1; st.at = Now; return;
                    }
                    if (st.sub == 1)
                    {
                        if (!call.StrokeGesture308) { if (Since > 3) { Check(false, "AC-V9 G did not start the call stroke: " + call.LastResult); Next(3, "G summon"); } return; }
                        if (call.GestureProgress < Mathf.Clamp(profile.StrokeRaiseEnd, .05f, .9f) * .6f) return;   // well before the call moment
                        hp0 = vitals.Hp01;
                        bool hit = vitals.TakeDamage(1f);
                        Line("INFO 1 hp test hit at gesture progress " + Harness303.F(call.GestureProgress) + " (call moment " + Harness303.F(profile.StrokeContactEnd) + "): " + (hit ? "landed" : "absorbed"));
                        st.sub = hit ? 2 : 3; st.at = Now; return;
                    }
                    if (st.sub == 2)
                    {
                        if (Since < Mathf.Max(1.2f, profile.TrailMeltSeconds + .5f)) return;
                        var cs = call.CallStroke308;
                        Check(!call.Calling && !call.StrokeGesture308 && call.SuccessfulCalls == calls0 && call.DeclinedGestures308 == declined0, "AC-V9 a hit before the call moment cancels the summon stroke: no car (calls " + calls0 + "->" + call.SuccessfulCalls + "), a real summon not a declined stroke (declined " + declined0 + "->" + call.DeclinedGestures308 + "), gesture ended (" + call.LastResult + ")");
                        Check(call.Gesture != null && !call.Gesture.AirStrokeActive308, "AC-V9 the rig left the stroke pose after the hit");
                        Check(gateFreeAt > 0, "AC-V9 action gate released after the cancelled stroke (blocked " + gateBlocked + ")");
                        Check(cs != null && cs.Flights == flights0 && !cs.Visible, "AC-V9 cancelled stroke: no ink flow (flights " + flights0 + "->" + (cs != null ? cs.Flights : -1) + "), trail dried in place (visible " + (cs != null && cs.Visible) + ")");
                        Check(drawEvents == 0 && !sawDrawMode, "AC-V9 recognition untouched during the cancelled stroke (drawing events " + drawEvents + ", draw mode " + sawDrawMode + ")");
                        st.sub = 4; st.at = Now; return;
                    }
                    if (st.sub == 3) { Line("SKIP AC-V9 hit cancel: the 1 hp hit was absorbed (invulnerable) — not measured"); st.sub = 4; st.at = Now; return; }
                    if (vitals.Hp01 < hp0) vitals.Restore(hp0);
                    // the hit reaction may hold the walker for a moment: the real summon waits for open input and a settled walker
                    if (Since < 2.5 || !InputOpen || call.Calling) return;
                    Next(3, "G summon"); return;
                }
                case 3: // G far / recalled -> ink summon in front; no teleport of the player
                {
                    if (st.sub == 0)
                    {
                        calls0 = call.SuccessfulCalls; feet0 = Feet; materialize0 = call.Ink308 != null ? call.Ink308.Materializations : 0;
                        phases.Clear(); sawHidden = false; maxDraws = 0; ArmStroke();
                        Check(!call.ShouldRecallOnToggle308(), "AC-V3 toggle reads 'summon' (car recalled or farther than " + profile.RecallToggleDistance + " m)");
                        VirtualInput303.Tap(Key.G, Center, 2); st.sub = 1; st.at = Now; return;
                    }
                    if (st.sub == 1 && call.Ink308 != null && call.Ink308.Busy && call.Ink308.Current == VehicleInkPresentation308.Phase.Gather)
                    {
                        Check(!s.VehicleBoardingAllowed308(seat), "AC-V5 boarding refused while the car forms in ink"); st.sub = 2;
                    }
                    // the capture waits until the ink covers over half the car: the first Gather frame has coverage 0 and showed
                    // nothing (2026-10-03 17:38 capture). Hold, or an early Recede frame after a slow frame, also qualifies.
                    if (st.sub == 2 && call.Ink308 != null && call.Ink308.Busy && call.Ink308.Coverage >= .55f
                        && call.Ink308.Current != VehicleInkPresentation308.Phase.Cover && call.Ink308.Current != VehicleInkPresentation308.Phase.Scatter)
                    {
                        Shot("summon_gather"); Line("INFO summon_gather.png at " + call.Ink308.Current + " coverage " + Harness303.F(call.Ink308.Coverage, "F2")); st.sub = 3;
                    }
                    bool doneCall = call.SuccessfulCalls > calls0 && !call.Calling && (call.Ink308 == null || !call.Ink308.Busy);
                    if (!doneCall) { if (Since > 10) { Check(false, "AC-V3 G summon did not complete: " + call.LastResult + " / " + call.LastPlacementDiagnostic); Finish("FAIL"); } return; }
                    Check(!call.IsRecalled && call.Vehicle.gameObject.activeInHierarchy, "AC-V3 G summons the car to the front (" + call.LastRoute + ")");
                    Check(Flat(Feet, feet0) < .3f, "AC-V3 the player is not moved by the summon (" + Harness303.F(Flat(Feet, feet0)) + " m)");
                    var ink = call.Ink308;
                    Check(ink != null && ink.Materializations == materialize0 + 1, "AC-V5 one ink materialize ran (" + (ink != null ? ink.Materializations.ToString() : "no presenter") + ")");
                    Check(phases.Contains("Gather") && phases.Contains("Recede") && sawHidden, "AC-V5 phases Gather/Hold/Recede seen [" + string.Join(",", phases) + "], car hidden under the ink " + sawHidden);
                    Check(maxDraws > 0 || profile.ShellMaterial == null, "AC-V5 ink shell drawn (max " + maxDraws + " draws/frame)");
                    Check(ink != null && !ink.CarHidden && EnabledCarRenderers() > 0, "AC-V5 car renderers back after the ink (" + EnabledCarRenderers() + " enabled)");
                    CheckStroke("summon", true);
                    enabledCar0 = EnabledCarRenderers();
                    Next(4, "F board"); return;
                }
                case 4: // F at the door: prompt, held F boards once and never exits
                {
                    if (st.sub == 0)
                    {
                        if (!seat.TryGetSafeExit(out var door)) { Check(false, "AC-V4 no safe ground at the car door: " + seat.LastInteraction); Finish("FAIL"); return; }
                        var look = Vector3.ProjectOnPlane(seat.SeatSocket.position - door, Vector3.up);
                        s.Teleport(door, look.sqrMagnitude > .01f ? Quaternion.LookRotation(look).eulerAngles.y : 0f);
                        Line("INFO setup teleport to the door " + Harness303.V(door));
                        // #308 review: the rider moves the appearance's Animator transform; measure that same transform and its own parent
                        appearance0 = s.Walker.Body.GetComponentInChildren<WorldMacroPlayerAppearance>(true);
                        appearanceRoot = appearance0 == null ? null : appearance0.Animator != null ? appearance0.Animator.transform : appearance0.transform;
                        appearanceParent0 = appearanceRoot != null ? appearanceRoot.parent : null;
                        st.sub = 1; st.at = Now; return;
                    }
                    if (st.sub == 1)
                    {
                        if (Since < 1.0 || !InputOpen) return;
                        if (appearanceRoot != null) { rootLocal0 = appearanceRoot.localPosition; rootRot0 = appearanceRoot.localRotation; }
                        string expect = "[F] " + profile.BoardPrompt;
                        Check(s.VehicleFocus308 == 1 && Prompt == expect, "AC-V4 HUD interaction prompt at the door '" + Prompt + "' (expected '" + expect + "', focus " + s.VehicleFocus308 + ", canBoard " + seat.CanBoard + ")");
                        // never press F while something else holds the focus (it would open that point's dialogue instead)
                        if (s.VehicleFocus308 != 1) { Check(false, "AC-V4 the car does not hold the F focus at the door (focused '" + s.FocusedId + "')"); Finish("FAIL"); return; }
                        boards0 = s.VehicleBoards308; exits0 = s.VehicleExits308;
                        var held = new List<InputFrame303>(); for (int i = 0; i < 45; i++) held.Add(InputFrame303.Neutral(Center).WithKeys(Key.F));
                        VirtualInput303.Enqueue(held); VirtualInput303.Enqueue(InputFrame303.Neutral(Center));
                        st.sub = 2; st.at = Now; return;
                    }
                    if (VirtualInput303.Pending > 0 || Since < 1.0) return;
                    Check(seat.Occupied && s.Walker.Seated && s.VehicleBoards308 == boards0 + 1, "AC-V4 F boards (boards " + boards0 + "->" + s.VehicleBoards308 + ", occupied " + seat.Occupied + ")");
                    Check(s.VehicleExits308 == exits0 && seat.Occupied, "AC-V4 holding the boarding F for 45 frames never exits (#306 fresh-press lock)");
                    if (!seat.Occupied) { Finish("FAIL"); return; }
                    Next(5, "seated pose"); return;
                }
                case 5: // visible seated rider + RuntimeState contract + V head hide
                {
                    if (st.sub == 0)
                    {
                        if (Since < .8) return;
                        Check(rider != null && rider.Riding, "AC-V6 rider active (" + (rider != null ? rider.LastIssue : "no rider") + ")");
                        if (rider != null && rider.Riding)
                        {
                            Line("INFO pose source " + (rider.UsingClip ? "Ride308 layer (" + profile.SeatedClip?.name + ")" : "procedural (no Ride308 layer)"));
                            Check(rider.VisibleRenderers > 0 && rider.ShadowOnRenderers > 0, "AC-V6 body visible while seated (" + rider.VisibleRenderers + " renderers, " + rider.ShadowOnRenderers + " shadow On)");
                            Check(rider.HipError <= .05f, "AC-V6 hips on the seat anchor (" + Harness303.F(rider.HipError) + " m)");
                            Check(InRange(rider.LeftKneeBend, 45, 135) && InRange(rider.RightKneeBend, 45, 135), "AC-V6 knees bent like sitting (L " + Harness303.F(rider.LeftKneeBend, "F0") + "°, R " + Harness303.F(rider.RightKneeBend, "F0") + "°; 45–135)");
                            Check(Mathf.Abs(rider.LeftThighPitch) <= 45 && Mathf.Abs(rider.RightThighPitch) <= 45, "AC-V6 thighs near level (L " + Harness303.F(rider.LeftThighPitch, "F0") + "°, R " + Harness303.F(rider.RightThighPitch, "F0") + "°; |·| <= 45)");
                            if (profile.FeetToFloor) Check(rider.LeftFootError >= 0 && rider.LeftFootError <= .10f && rider.RightFootError >= 0 && rider.RightFootError <= .10f, "AC-V6 feet on their floor targets (L " + Harness303.F(rider.LeftFootError) + ", R " + Harness303.F(rider.RightFootError) + " m)");
                            if (profile.HandsToGrip) Check(rider.LeftHandError >= 0 && rider.LeftHandError <= .12f && rider.RightHandError >= 0 && rider.RightHandError <= .12f, "AC-V6 hands on the grip targets (L " + Harness303.F(rider.LeftHandError) + ", R " + Harness303.F(rider.RightHandError) + " m; torso lean to reach " + Harness303.F(rider.GripLean, "F1") + "° of " + Harness303.F(profile.GripReachLean, "F0") + "°)");
                            if (profile.HandsToGrip) Line((Mathf.Max(rider.LeftHandError, rider.RightHandError) <= .05f ? "INFO" : "NOTE") + " AC-V6 hand error target .05 m (spec limit .12): max " + Harness303.F(Mathf.Max(rider.LeftHandError, rider.RightHandError)) + " m" + (rider.GripLean >= profile.GripReachLean - .05f ? " — lean cap reached: move HandOffsetFromHip nearer the shoulders" : ""));
                            Check(rider.AppearanceRoot != null && rider.AppearanceRoot == appearanceRoot && rider.AppearanceRoot.parent == appearanceParent0, "AC-V6 appearance root keeps its parent (attached by the seat anchor, not reparented): " + (appearanceParent0 != null ? appearanceParent0.name : "none"));
                        }
                        var ui = PlaytestUiRoot.Instance;
                        Check(ui != null && ui.Pause != null && ui.Pause.Gate != null && seat.RuntimeState == ui.Pause.Gate.State, "AC-V7 seat RuntimeState is the PauseCoordinator gate state (contract kept)");
                        string expect = "[F] " + profile.ExitPrompt;
                        Check(s.VehicleFocus308 == 2 && Prompt == expect, "AC-V4 stopped car shows '" + Prompt + "' (expected '" + expect + "')");
                        Shot("ride_external");
                        VirtualInput303.Tap(Key.V, Center, 2); st.sub = 1; st.at = Now; return;
                    }
                    if (st.sub == 1)
                    {
                        if (Since < .8) return;
                        if (rider != null && seat.SeatedView) Check(rider.HeadHidden == profile.HideHeadInSeatView, "AC-V6 seated view hides only the head (" + rider.HeadHidden + "), body still visible (" + rider.VisibleRenderers + ")");
                        Shot("ride_seat");
                        VirtualInput303.Tap(Key.V, Center, 2); st.sub = 2; st.at = Now; return;
                    }
                    if (Since < .8) return;
                    Next(6, "F exit"); return;
                }
                case 6: // a fresh F exits; the same F held for 45 frames never re-boards (#306: the closing key never re-triggers)
                {
                    if (st.sub == 0)
                    {
                        if (Since < .7 || !InputOpen) return;
                        exits0 = s.VehicleExits308; boards0 = s.VehicleBoards308;
                        var held = new List<InputFrame303>(); for (int i = 0; i < 45; i++) held.Add(InputFrame303.Neutral(Center).WithKeys(Key.F));
                        VirtualInput303.Enqueue(held); VirtualInput303.Enqueue(InputFrame303.Neutral(Center)); st.sub = 1; st.at = Now; return;
                    }
                    if (VirtualInput303.Pending > 0 || Since < 1.2) return;
                    Check(!seat.Occupied && !s.Walker.Seated && s.VehicleExits308 == exits0 + 1, "AC-V4 fresh F exits the stopped car (exits " + exits0 + "->" + s.VehicleExits308 + ")");
                    Check(s.VehicleBoards308 == boards0 && !seat.Occupied, "AC-V4 holding the exiting F for 45 frames never re-boards (boards " + boards0 + "->" + s.VehicleBoards308 + ")");
                    Check(rider == null || !rider.Riding, "AC-V6 rider released on exit");
                    if (appearanceRoot != null) Check(Vector3.Distance(appearanceRoot.localPosition, rootLocal0) < .01f && (HasLookTurn() || Quaternion.Angle(appearanceRoot.localRotation, rootRot0) < 1f), "AC-V6 appearance root local pose restored");
                    if (seat.Occupied) seat.TryExit();
                    Next(7, "G recall"); return;
                }
                case 7: // G near the car -> ink recall, the player stays where he is
                {
                    if (st.sub == 0)
                    {
                        if (Since < 1.0 || !InputOpen) return;
                        Check(call.ShouldRecallOnToggle308(), "AC-V3 toggle reads 'recall' next to the car (" + Harness303.F(Flat(Feet, call.Vehicle.transform.position), "F1") + " m <= " + profile.RecallToggleDistance + ")");
                        recalls0 = call.ToggleRecalls; feet0 = Feet; dissolve0 = call.Ink308 != null ? call.Ink308.Dissolves : 0; phases.Clear(); sawHidden = false; sawRecallGesture = false;
                        ArmStroke();
                        VirtualInput303.Tap(Key.G, Center, 2); st.sub = 1; st.at = Now; return;
                    }
                    if (st.sub == 1 && call.Ink308 != null && call.Ink308.Current == VehicleInkPresentation308.Phase.Scatter) { Shot("recall_scatter"); st.sub = 2; }
                    if (!call.IsRecalled) { if (Since > 10) { Check(false, "AC-V3 G recall did not complete: " + call.LastResult); Next(8, "G after recall"); } return; }
                    Check(sawRecallGesture && call.ToggleRecalls == recalls0 + 1 && !call.Vehicle.gameObject.activeInHierarchy, "AC-V3 G recalls the near car through the gesture (recalls " + recalls0 + "->" + call.ToggleRecalls + ")");
                    Check(Flat(Feet, feet0) < .3f, "AC-V3 recall does not move the player (" + Harness303.F(Flat(Feet, feet0)) + " m, CONST-ANTIVISION 2)");
                    Check(phases.Contains("Cover") && phases.Contains("Scatter") && call.Ink308.Dissolves == dissolve0 + 1, "AC-V5 ink dissolve Cover/Scatter seen [" + string.Join(",", phases) + "]");
                    Check(EnabledCarRenderers() == enabledCar0, "AC-V5 recalled car keeps its renderer enabled flags for the next summon (" + EnabledCarRenderers() + "/" + enabledCar0 + ")");
                    CheckStroke("recall", false);
                    Next(8, "G after recall"); return;
                }
                case 8: // G again: the recalled car comes back
                {
                    if (st.sub == 0)
                    {
                        if (Since < 2.0 || !InputOpen) return;
                        // the player left the car on its exit side: from there the front can be blocked (a house). Refusing is the right
                        // product answer, but this step tests the summon — go back to the summon spot and facing found in phase 2.
                        if (spotSet && !call.TryFindPlacement(Feet, s.Walker.Body.transform.forward, out _, out _))
                        { s.Teleport(spotFeet, spotYaw); Line("INFO G after recall: front blocked at the exit pose — back to the summon spot " + Harness303.V(spotFeet) + " facing " + spotYaw + " deg"); st.sub = 3; st.at = Now; return; }
                        calls0 = call.SuccessfulCalls; VirtualInput303.Tap(Key.G, Center, 2); st.sub = 1; st.at = Now; return;
                    }
                    if (st.sub == 3) { if (Since < 1.5 || !InputOpen) return; calls0 = call.SuccessfulCalls; VirtualInput303.Tap(Key.G, Center, 2); st.sub = 1; st.at = Now; return; }
                    bool doneCall = call.SuccessfulCalls > calls0 && !call.Calling && (call.Ink308 == null || !call.Ink308.Busy);
                    if (!doneCall) { if (Since > 10) { Check(false, "AC-V3 G after recall did not summon: " + call.LastResult + " / " + call.LastPlacementDiagnostic); Finish("FAIL"); } return; }
                    Check(!call.IsRecalled && call.Vehicle.gameObject.activeInHierarchy, "AC-V3 G after a recall summons the car again");
                    Finish(st.fails == 0 ? "PASS" : "FAIL"); return;
                }
            }
        }

        // ---------- AC-V1 D308-8b: a boss-field G shows nothing (no gesture, no action gate, no text) ----------

        static void Arm()
        {
            calls0 = call.SuccessfulCalls; declined0 = call.DeclinedGestures308; silent0 = call.SilentRefusals308;
            carActive0 = call.Vehicle.gameObject.activeInHierarchy; car0 = call.Vehicle.transform.position;
            sawGestureInField = sawGateInField = false;
        }
        // every frame of the 3 s window after the tap: a started gesture (Calling / pendant) or a blocked action gate is a FAIL
        static void WatchSilent()
        {
            if (call.Calling || (call.Gesture != null && (call.Gesture.PendantActive || call.Gesture.AirStrokeActive308))) sawGestureInField = true;
            var ui = PlaytestUiRoot.Instance;   // the gate the pendant gesture blocks (actionGate = ui.Gate / ui.Pause.Gate)
            if (VirtualInput303.Pending == 0 && ui != null && ui.Gate != null && ui.Gate.InputBlocked) sawGateInField = true;
        }
        static void CheckSilentRefusal(string where)
        {
            bool moved = call.Vehicle.gameObject.activeInHierarchy != carActive0 || (carActive0 && Vector3.Distance(call.Vehicle.transform.position, car0) > .05f);
            Check(call.SuccessfulCalls == calls0 && !moved, "AC-V1 G with the " + where + " places no car (calls " + calls0 + "->" + call.SuccessfulCalls + ", moved " + moved + ")");
            Check((call.LastResult ?? "").StartsWith("vehicle refused: boss field", StringComparison.Ordinal) && call.LastRefusalBossField308, "AC-V1 refusal reason is the boss field: '" + call.LastResult + "'");
            Check(!sawGestureInField && call.DeclinedGestures308 == declined0, "AC-V1 D308-8b no gesture with the " + where + " (declined " + declined0 + "->" + call.DeclinedGestures308 + ", calling/pendant/stroke seen " + sawGestureInField + ")");
            Check(!sawGateInField, "AC-V1 D308-8b no action-gate lock with the " + where);
            Check(call.SilentRefusals308 == silent0 + 1, "AC-V1 D308-8b one silent refusal counted (" + silent0 + "->" + call.SilentRefusals308 + "); text: none by construction (no Notify on this path, code review)");
        }

        // ---------- D308-8c capture readability: the shoulder camera's boom ----------
        // CameraRigController pulls the camera toward the pivot when something stands between them (SphereCast pivot -> shoulder
        // pose, radius .25 m, default raycast layers, triggers ignored). Read-only here: the rig's serialized pivot and config.

        static bool ShoulderBoom(out Transform pivot, out Vector3 offset)
        {
            pivot = null; offset = Vector3.zero;
            var rig = Object.FindFirstObjectByType<CameraRigController>();
            if (rig == null) return false;
            CombatConfigSO config;
            using (var so = new SerializedObject(rig))   // disposed: this runs per candidate spot and per capture
            {
                var pivotProperty = so.FindProperty("_cameraPivot"); var configProperty = so.FindProperty("_config");
                pivot = pivotProperty != null ? pivotProperty.objectReferenceValue as Transform : null;
                config = configProperty != null ? configProperty.objectReferenceValue as CombatConfigSO : null;
            }
            if (pivot == null || config == null) return false;
            offset = config.ShoulderOffset;
            return offset.sqrMagnitude > .01f;
        }
        // the same cast the rig makes, at a candidate spot and yaw (pitch 0), before the player stands there; unknown rig = no filter
        static bool BoomProbeFree(Vector3 feet, float yaw)
        {
            if (!ShoulderBoom(out var pivot, out var offset)) return true;
            Vector3 origin = feet + Vector3.up * Mathf.Max(.5f, pivot.position.y - Feet.y), dir = Quaternion.Euler(0f, yaw, 0f) * offset;
            float length = dir.magnitude;
            return !Physics.SphereCast(origin, .25f, dir / length, out _, length + .3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }
        // the camera's distance from its pivot now, and the full shoulder distance
        static bool BoomNow(out float now, out float full)
        {
            now = full = 0f; var cam = Camera.main;
            if (cam == null || !ShoulderBoom(out var pivot, out var offset)) return false;
            now = Vector3.Distance(cam.transform.position, pivot.position); full = offset.magnitude;
            return true;
        }
        static void ShotWithBoom(string name)
        {
            Shot(name);
            if (BoomNow(out float boom, out float full))
                Line((boom < full * BoomFreeShare ? "NOTE " : "INFO ") + name + ".png camera boom " + Harness303.F(boom) + " of " + Harness303.F(full) + " m" + (boom < full * BoomFreeShare ? " — pulled in by an obstacle: not the normal shoulder view" : ""));
        }

        // ---------- AC-V9 D308-8c: the call stroke in the air ----------

        static void ArmStroke()
        {
            strokes0 = call.Gesture != null ? call.Gesture.AirStrokes308 : 0;
            flights0 = call.CallStroke308 != null ? call.CallStroke308.Flights : 0; arrivals0 = call.CallStroke308 != null ? call.CallStroke308.Arrivals : 0; awaits0 = call.Ink308 != null ? call.Ink308.Awaits : 0;
            drawEvents = tipIn = tipAll = tipBehindHead = 0; sawStroke = sawPendant = sawDrawMode = gateBlocked = shotStroke = shotFlow = shotRecall = false;
            tapAt = Now; gateFreeAt = 0; gateBlockedGame = gateFreeGame = -1f; HookDrawing();
            strokeApp = s.Walker.Body.GetComponentInChildren<WorldMacroPlayerAppearance>(true); bodyOff = 0f; bodyKnown = false;
            var animator = strokeApp != null ? strokeApp.GetComponent<Animator>() : null;
            if (animator == null) animator = s.Walker.Body.GetComponentInChildren<Animator>(true);
            strokeHead = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        }
        // recognition boundary: any drawing-input event while a call stroke runs would mean the stroke reached the recognizer path
        static void OnDrawStroke() => drawEvents++;
        static void OnDrawPoint(Vector2 _) => drawEvents++;
        static void OnDrawCommit(bool _) => drawEvents++;
        static void HookDrawing()
        {
            var d = s != null && s.Walker != null ? s.Walker.Drawing : null;
            if (d == drawHooked) return;
            UnhookDrawing();
            if (d == null) return;
            d.StrokeStarted += OnDrawStroke; d.StrokePointAdded += OnDrawPoint; d.Committed += OnDrawCommit; drawHooked = d;
        }
        static void UnhookDrawing()
        {
            if (drawHooked == null) return;
            drawHooked.StrokeStarted -= OnDrawStroke; drawHooked.StrokePointAdded -= OnDrawPoint; drawHooked.Committed -= OnDrawCommit; drawHooked = null;
        }
        // every editor update while armed: stroke / pendant / draw mode seen, when the action gate frees, tip inside the view, captures
        static void WatchStroke()
        {
            if (call == null || s == null || s.Walker == null || tapAt <= 0) return;
            if (call.StrokeGesture308) sawStroke = true;
            if (call.Gesture != null && call.Gesture.PendantActive) sawPendant = true;
            if (s.Walker.Drawing != null && s.Walker.Drawing.InDrawMode) sawDrawMode = true;
            var ui = PlaytestUiRoot.Instance; bool blocked = ui != null && ui.Gate != null && ui.Gate.InputBlocked;
            // wall clock (from the tap) and game time (blocked -> free; a first-use hitch frame is capped by maximumDeltaTime in game time
            // exactly as the gesture's own clock is, so the game-time span is the one compared with CallStrokeSeconds)
            if (blocked) { if (!gateBlocked) gateBlockedGame = Time.time; gateBlocked = true; }
            else if (gateBlocked && gateFreeAt <= 0) { gateFreeAt = Now; gateFreeGame = Time.time; }
            var g = call.Gesture; var cam = Camera.main; var cs = call.CallStroke308;
            if (g != null && g.AirStrokeContact308 && g.WorldTip != null && cam != null)
            {
                tipAll++;
                // the visual body faces the call direction while the brush is in the air (look-turn lag turned away during the raise)
                if (strokeApp != null && strokeApp.Profile != null && strokeApp.Profile.IdleLookTurn && s.Walker.Motor != null)
                { bodyKnown = true; bodyOff = Mathf.Max(bodyOff, Mathf.Abs(Mathf.DeltaAngle(strokeApp.BodyYaw, s.Walker.Motor.transform.eulerAngles.y))); }
                var v = cam.WorldToViewportPoint(g.WorldTip.position);
                if (v.z > cam.nearClipPlane && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f) tipIn++;
                // readability: the tip is drawn in front of the body, so from the shoulder camera the head can cover it. Screen-space
                // test against a head disc (head bone + .09 m, radius HeadDiscRadius incl. hair). Reported, not judged (capture decides).
                if (strokeHead != null)
                {
                    Vector3 headCentre = strokeHead.position + Vector3.up * .09f;
                    var hv = cam.WorldToViewportPoint(headCentre);
                    if (hv.z > cam.nearClipPlane && v.z > hv.z)
                    {
                        float radius = Mathf.Abs(cam.WorldToViewportPoint(headCentre + cam.transform.right * HeadDiscRadius).x - hv.x);
                        float dx = v.x - hv.x, dy = (v.y - hv.y) / Mathf.Max(.01f, cam.aspect);   // viewport y in viewport-x units
                        if (dx * dx + dy * dy < radius * radius) tipBehindHead++;
                    }
                }
                float mid = (Mathf.Clamp(profile.StrokeRaiseEnd, .05f, .9f) + profile.StrokeContactEnd) * .5f;
                if (!shotStroke && call.GestureProgress >= mid && !call.RecallGesture) { shotStroke = true; ShotWithBoom("call_stroke"); }
                if (!shotRecall && call.GestureProgress >= mid && call.RecallGesture) { shotRecall = true; ShotWithBoom("recall_stroke"); }
            }
            if (!shotFlow && cs != null && cs.Flying && cs.Flights > flights0 && !call.RecallGesture && call.StrokeGesture308) { shotFlow = true; ShotWithBoom("call_flow"); }
        }
        static void CheckStroke(string what, bool summon)
        {
            var cs = call.CallStroke308; var g = call.Gesture;
            if (!profile.CallStroke) { Line("SKIP AC-V9 " + what + ": CallStroke off (legacy pendant)"); return; }
            Check(sawStroke && !sawPendant && g != null && g.AirStrokes308 == strokes0 + 1, "AC-V9 G " + what + " plays one brush call stroke, no pendant (stroke " + sawStroke + ", pendant " + sawPendant + ", strokes " + strokes0 + "->" + (g != null ? g.AirStrokes308 : -1) + ")");
            // the gesture ends on the first frame at or past CallStrokeSeconds: the overshoot is one game frame (slow editor frames included)
            float overshoot = call.LastGestureSeconds308 - profile.CallStrokeSeconds;
            Check(overshoot >= -.001f && overshoot <= Mathf.Max(.06f, Mathf.Min(.15f, Time.maximumDeltaTime)), "AC-V9 " + what + " gesture " + Harness303.F(call.LastGestureSeconds308) + " s = CallStrokeSeconds " + Harness303.F(profile.CallStrokeSeconds) + " + one frame (TEST)");
            double held = gateFreeAt > 0 ? gateFreeAt - tapAt : -1;
            float heldGame = gateBlockedGame >= 0f && gateFreeGame >= 0f ? gateFreeGame - gateBlockedGame : -1f;
            float frame = Mathf.Max(.06f, Mathf.Min(.15f, Time.maximumDeltaTime));   // sampling is per editor update: one frame each end
            Check(gateBlocked && heldGame >= profile.CallStrokeSeconds - frame && heldGame <= profile.CallStrokeSeconds + 2f * frame, "AC-V9 " + what + " action gate held only for the stroke (" + Harness303.F(heldGame) + " s game time blocked -> free, stroke " + Harness303.F(profile.CallStrokeSeconds) + " s ± frames; wall clock from the tap " + Harness303.F((float)held) + " s)");
            Check(cs != null && cs.MaxTrailPoints >= 3 && cs.LastTrailLength >= .12f, "AC-V9 " + what + " ink trail from the real brush tip (" + (cs != null ? cs.MaxTrailPoints + " points, " + Harness303.F(cs.LastTrailLength) + " m" : "no trail component") + ")");
            Check(cs != null && cs.Flights == flights0 + 1 && cs.Arrivals == arrivals0 + 1, "AC-V9 " + what + " stroke ink flew to the car and landed (flights " + flights0 + "->" + (cs != null ? cs.Flights : -1) + ", " + (cs != null ? Harness303.F(cs.LastFlightSeconds) : "-") + " s)");
            float lead = profile.FlowSeconds * profile.GatherAfterFlow;
            // counted by the ink component: one long frame (a capture, a first-use hitch) can pass the whole lead, so sampling Current misses it
            int awaits = call.Ink308 != null ? call.Ink308.Awaits - awaits0 : 0;
            Check(lead <= 0f || awaits == 1, "AC-V9 " + what + " the car's ink waited for the flow (Await entered " + awaits + "x, sampled " + phases.Contains("Await") + ", lead " + Harness303.F(lead) + " s) then " + (summon ? "gathered" : "covered"));
            Check(cs != null && cs.MaxInkChannel <= .85f, "AC-V9 " + what + " stroke ink LDR (max channel " + (cs != null ? Harness303.F(cs.MaxInkChannel, "F3") : "-") + " <= .85, flash add 0 by construction)");
            Check(tipAll > 0 && tipIn >= .8f * tipAll, "AC-V9 " + what + " brush tip inside the camera view while it draws (" + tipIn + "/" + tipAll + " samples; occlusion by the body: capture)");
            if (strokeHead == null) Line("INFO AC-V9 " + what + " head silhouette not measured (no humanoid head bone)");
            else Line((tipBehindHead * 4 > tipAll ? "NOTE" : "INFO") + " AC-V9 " + what + " brush tip behind the head silhouette in " + tipBehindHead + "/" + tipAll + " samples (head disc r " + Harness303.F(HeadDiscRadius, "F2") + " m; reported, not judged — a pulled-in camera or a path near the head raises it: StrokeFrom.x to the right)");
            Check(drawEvents == 0 && !sawDrawMode, "AC-V9 " + what + " recognition untouched: no draw mode, no drawing stroke/point/commit events (" + drawEvents + ")");
            if (!bodyKnown) Line("SKIP AC-V9 " + what + " body facing: no idle look-turn on the appearance (the body follows the controller)");
            else Check(bodyOff <= 5f, "AC-V9 " + what + " the body faces the call direction while the brush is in the air (max " + Harness303.F(bodyOff, "F1") + "° off the controller yaw <= 5; turn " + Harness303.F(profile.StrokeBodyTurnSpeed, "F0") + " °/s)");
            tapAt = 0;
        }

        static bool InRange(float v, float lo, float hi) => v >= lo && v <= hi;
        // the look-turn presentation rewrites the root's world yaw every Update, so its local rotation is compared only without it
        static bool HasLookTurn() { var app = appearance0; return app != null && app.Profile != null && app.Profile.IdleLookTurn; }

        static void Finish(string how)
        {
            st.status = "finishing";
            UnhookDrawing(); tapAt = 0;
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
            foreach (var c in Harness303.Cleanup(iso, Path.Combine(Folder, "vehicle-checks-run"))) Line(c.status + " " + c.id + " " + c.detail);
            Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, "vehicle-checks.txt"), string.Join("\n", st.lines), new UTF8Encoding(false));
            st.status = st.fails == 0 ? "done PASS" : "done FAIL"; s = null; call = null; seat = null; rider = null; inputStarted = tidied = false;
        }
    }
}
