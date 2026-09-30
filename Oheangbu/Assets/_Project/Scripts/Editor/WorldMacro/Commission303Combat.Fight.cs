using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Drawing;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Plan §5.6 fight policy (plan A = 마): Tab lock-on, a new glyph only when drawing is allowed, ink ≥ one cast, no
    // unlanded power already covering the remaining HP, a clear Default-layer line (the wiring's TargetVisible rule) and
    // either distance > 3.5 m without a wind-up or the 창귀 recovering; Shift+A/D dodge on a late wind-up; LMB harvest when
    // ink is short (plan B / REST_SKIPPED); retreat at ≤ 35 % HP. Everything goes through VirtualInput303 — the fight never
    // touches enemy or player state directly.
    public static partial class Commission303Combat
    {
        sealed class FightState
        {
            public float startedReal, lockedAtReal = -1, nextLockReal, dodgeReady, harvestSeconds, noResourceSince = -1, retreatUntil, strafeUntil, minHp = 1, damageTaken;
            public int lockTaps, dodges, misfiresInRow, strafes; public bool lockConfirmed, dodgeLeft, retreating, harvesting, strafeLeft, firstImpactPending, firstImpactCaptured, firstDamageCaptured;
            public Vector3 retreatFrom; public float firstDamageAt = -1;
        }
        static FightState fight;
        static bool letterActive; static CommitRow currentCommit; static int letterScriptEndFrame;
        static readonly List<PlannedHit> pendingHits = new List<PlannedHit>();

        // ------------------------------------------------------------------ glyph injection (shared with P2)
        static void BeginLetter(WorldMacroPlaytestSession s, string phase)
        {
            var glyph = Strokes303.Glyph(state.recipe, library, Screen.width, Screen.height);
            var frames = Strokes303.Frames(glyph, state.recipe.penUpFrames, out int points, out int penUps);
            VirtualInput303.ClearQueue(); VirtualInput303.Enqueue(frames);
            currentCommit = new CommitRow
            {
                index = logs.commits.Count, phase = phase, startFrame = Time.frameCount + 1, startReal = Time.realtimeSinceStartup, points = points, penUps = penUps,
                scriptFrames = frames.Count, inkBefore = Ink(s), distance = actor != null ? Harness303.Flat(s.Walker.Body.transform.position, actor.transform.position) : -1,
                enemyState = enemy == null ? "" : enemy.IsTelegraphing ? "telegraph" : enemy.IsRecovering ? "recover" : actor.Current.ToString(),
            };
            logs.commits.Add(currentCommit);
            letterActive = true; letterScriptEndFrame = Time.frameCount + frames.Count;
            if (logs.commits.Count == 1) state.realInputs.Add("glyph " + state.recipe.letter + ": Q held, LMB strokes replayed from " + state.recipe.initialTemplate + " + " + state.recipe.medialTemplate + " (one point per frame), Q released = commit");
        }

        static void LetterTick(WorldMacroPlaytestSession s)
        {
            if (currentCommit == null) { letterActive = false; return; }
            bool scriptDone = VirtualInput303.Pending == 0 && Time.frameCount > letterScriptEndFrame + 1;
            if (currentCommit.diagnosed && !drawing.InDrawMode) { FinishLetter(s, currentCommit.success ? "success" : "misfire"); return; }
            if (scriptDone && !drawing.InDrawMode && !currentCommit.diagnosed && Time.frameCount > letterScriptEndFrame + 3)
            { FinishLetter(s, currentCommit.interrupted ? "interrupted (silent cancel)" : "cancelled without CommitDiagnosed"); return; }
            if (Time.realtimeSinceStartup - currentCommit.startReal > 10f) { FinishLetter(s, "stuck"); state.findings.Add("glyph " + currentCommit.index + " did not finish in 10 s"); }
        }

        static void FinishLetter(WorldMacroPlaytestSession s, string outcome)
        {
            currentCommit.outcome = outcome; currentCommit.inkAfter = Ink(s); if (currentCommit.endFrame == 0) currentCommit.endFrame = Time.frameCount;
            letterActive = false;
            if (!currentCommit.interrupted && currentCommit.diagnosed) { if (currentCommit.success) fightMisfireReset(); else if (fight != null) fight.misfiresInRow++; }
            currentCommit = null;
        }
        static void fightMisfireReset() { if (fight != null) fight.misfiresInRow = 0; }

        static void OnLetter(DrawnLetter l)
        {
            if (currentCommit == null) return;
            currentCommit.strokeDuration = l.StrokeDuration; currentCommit.holdDuration = l.HoldDuration;
            currentCommit.evaluatedBrush = book != null ? book.EvaluateBrushPower(l.WorstJamoDistance, l.StrokeDuration) : -1;
            currentCommit.holdPower = wiring != null && wiring.EABuffs != null ? wiring.EABuffs.HoldPower(l.HoldDuration, Time.time) : 1f;
        }

        static void OnPlanned(CastPlan plan)
        {
            if (plan == null) return;
            foreach (var h in plan.Hits) if (h != null && h.Target == vitals) pendingHits.Add(h);
            if (currentCommit == null || currentCommit.planned) return;
            currentCommit.planned = true; currentCommit.kind = plan.Cast.Kind.ToString(); currentCommit.power = plan.Cast.Power;
            currentCommit.damageScale = wiring != null && wiring.PlayerDamageScale != null ? wiring.PlayerDamageScale(plan.Cast.Element) : 1f;
            foreach (var h in plan.Hits) currentCommit.hits.Add(new HitRow { impactTime = h.ImpactTime, power = h.Power, target = h.Target != null ? h.Target.name : "" });
        }

        static void OnDiagnosed(DrawingInputController.CommitDiagnostics d)
        {
            if (currentCommit == null) { state.findings.Add("CommitDiagnosed without an injected glyph (frame " + Time.frameCount + ")"); return; }
            var c = currentCommit;
            c.diagnosed = true; c.success = d.Success; c.letter = d.Success ? d.Letter.ToString() : "MISFIRE"; c.initial = d.InitialName ?? ""; c.medial = d.MedialName ?? "";
            c.split = d.SplitGroupCount; c.strokes = d.StrokeCount; c.recordedPoints = d.PointCount; c.worst = d.WorstDistance; c.avg = d.AverageDistance; c.elapsedMs = (float)d.ElapsedMilliseconds;
            c.endFrame = Time.frameCount; c.endReal = Time.realtimeSinceStartup;
            int intervals = Math.Max(1, c.points + c.penUps - 1);
            c.fps = c.strokeDuration > 1e-4f ? intervals / c.strokeDuration : (c.endReal > c.startReal ? (c.scriptFrames - 1) / (c.endReal - c.startReal) : 0);
            c.predictedBrush = book != null && offlineWorst >= 0 ? book.EvaluateBrushPower(offlineWorst, c.strokeDuration > 0 ? c.strokeDuration : intervals / Mathf.Max(1, c.fps)) : -1;
            float basePower = book != null && d.Success && book.TryGet(d.Letter, out var e) ? e.BasePower : 0;
            c.measuredBrush = c.planned && basePower > 0 ? c.power / (basePower * Mathf.Max(.01f, c.holdPower)) : c.evaluatedBrush;
        }

        static void OnInterrupted()
        {
            if (!letterActive || currentCommit == null) return;
            currentCommit.interrupted = true;
            VirtualInput303.ClearQueue();
            VirtualInput303.Enqueue(InputFrame303.Neutral(Center));   // Q and LMB up: an empty letter closes silently
            letterScriptEndFrame = Time.frameCount;
        }

        static void OnDamage(EnemyDamageResult r)
        {
            var row = new DamageRow { t = Time.time, applied = r.AppliedDamage, hpAfter = vitals != null ? vitals.Hp : -1, source = r.Attack.Source.ToString(), element = r.Attack.Element.HasValue ? r.Attack.Element.Value.ToString() : "", attackId = r.Attack.AttackId, killed = r.Killed, phase = state.phase };
            logs.damage.Add(row);
            if (r.Killed) { var s = Harness303.Session; if (s != null) state.currencyBeforeKill = s.Progress.ledger.currency; }   // DamageResolved precedes Died: pre-reward value
            if (fight != null && r.Attack.Source == DamageSource.PlayerDirect && !fight.firstImpactCaptured) fight.firstImpactPending = true;
        }

        static void OnWiringDamage(EnemyDamageResult r)
        {
            if (r.Target != vitals) return;
            logs.wiringDamage.Add(new DamageRow { t = Time.time, applied = r.AppliedDamage, source = r.Attack.Source.ToString(), element = r.Attack.Element.HasValue ? r.Attack.Element.Value.ToString() : "", attackId = r.Attack.AttackId, killed = r.Killed, phase = state.phase });
            var match = logs.damage.LastOrDefault(x => x.attackId == r.Attack.AttackId && !x.wiringPaired);
            if (match != null) match.wiringPaired = true;
        }

        static void OnPlayerDamaged(float amount)
        {
            logs.playerHits.Add(new PlayerHitRow { t = Time.time, amount = amount, hp01 = playerVitals != null ? playerVitals.Hp01 : -1, kind = "damage", phase = state.phase });
            if (fight != null) fight.damageTaken += amount;
        }
        static void OnTelegraph(EnemyAttackCue cue) => logs.playerHits.Add(new PlayerHitRow { t = Time.time, kind = "telegraph", detail = cue.Delivery + " " + Harness303.F(cue.Duration, "F2") + " s", phase = state.phase });
        static void OnImpact(EnemyAttackImpact i) => logs.playerHits.Add(new PlayerHitRow { t = Time.time, kind = "impact", amount = i.AppliedDamage, detail = i.Outcome + " inShape " + i.InShape, phase = state.phase });

        /// <summary>CombatLoopWiring.TargetVisible, read-only: Default layer between player+0.4 and target+0.4 (only when the
        /// wiring's environment occlusion is on).</summary>
        static bool Visible(WorldMacroPlaytestSession s)
        {
            if (actor == null || wiring == null) return false;
            if (!(Harness303.FieldValue(wiring, "_environmentOcclusion") is bool occlusion) || !occlusion) return true;
            var player = Harness303.Or(Harness303.Field<Transform>(wiring, "_playerTransform"), s.Walker.Body.transform);
            Vector3 origin = player.position + Vector3.up * .4f, delta = vitals.transform.position + Vector3.up * .4f - origin;
            foreach (var hit in Physics.RaycastAll(origin, delta.normalized, delta.magnitude, 1, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(vitals.transform) && !hit.transform.IsChildOf(player)) return false;
            return true;
        }

        static float PendingPower() => pendingHits.Where(h => h != null && h.ImpactTime > Time.time).Sum(h => h.Power);

        // ------------------------------------------------------------------ P7
        static void FightTick(WorldMacroPlaytestSession s)
        {
            float now = Time.realtimeSinceStartup;
            if (fight == null)
            {
                if (lockOn == null || enemy == null || vitals == null || drawing == null || library == null) { FailPhase("C8", "combat references missing (lockOn/enemy/vitals/drawing/templates)"); return; }
                fight = new FightState { startedReal = now }; pendingHits.Clear();
            }
            var feet = s.Walker.Body.transform.position;
            fight.minHp = Mathf.Min(fight.minHp, playerVitals.Hp01);
            if (fight.firstImpactPending && !drawing.InDrawMode) { fight.firstImpactPending = false; fight.firstImpactCaptured = true; fight.firstDamageAt = now; Harness303.Capture(RunFolder, "08_first_impact"); }
            if (fight.firstDamageAt > 0 && !fight.firstDamageCaptured && now - fight.firstDamageAt > .3f && !drawing.InDrawMode) { fight.firstDamageCaptured = true; Harness303.Capture(RunFolder, "09_first_damage"); }
            if (vitals == null || !vitals.IsAlive)
            {
                if (deathAt < 0) deathAt = Now;
                if (letterActive) { LetterTick(s); return; }   // a glyph already in the air finishes before the verdict
                Killed(s); return;
            }
            if (playerVitals.Hp01 <= 0) { FailC("C9", "player died in the fight (min HP " + Harness303.F(fight.minHp) + ")"); FailPhase("C8", "player died before the kill"); return; }
            int injected = logs.commits.Count(c => c.phase == "P7");
            if (fight.lockedAtReal > 0 && now - fight.lockedAtReal > 120) { FailPhase("C8", "fight longer than 120 s after lock-on"); return; }
            if (injected > 12) { FailPhase("C8", "more than 12 glyphs injected"); return; }
            if (fight.misfiresInRow >= 3) { FailPhase("C8", "3 misfires in a row"); return; }
            if (fight.noResourceSince > 0 && now - fight.noResourceSince > 20) { FailPhase("C8", "no ink and no harvest for 20 s"); return; }
            if (letterActive) { LetterTick(s); return; }
            if (VirtualInput303.Pending > 0) return;   // a tap is still playing

            float dist = Harness303.Flat(feet, actor.transform.position);
            var chest = vitals.transform.position + Vector3.up * 1.1f;
            var home = s.Content.Encounters.FirstOrDefault(e => e.Id == TargetId)?.Feet ?? actor.transform.position;
            // 1. lock-on (Tab)
            if (lockOn.Target != vitals)
            {
                if (lockOn.Target != null) { VirtualInput303.Tap(Key.Tab, Center, 1); state.notes.Add("P7 Tab released a lock on " + lockOn.Target.name); return; }
                VirtualInput303.Hold.Delta = Steer303.Toward(s, chest, out float err);
                float range = config != null ? config.LockOnRange : 20f;
                if (dist > range - 1f) { VirtualInput303.Hold.Keys = Mathf.Abs(err) < 30 ? new[] { Key.W } : Array.Empty<Key>(); return; }
                if (fight.lockConfirmed) { fight.lockConfirmed = false; state.notes.Add("P7 lock lost; re-locking"); }
                if (Mathf.Abs(err) < 6f && now >= fight.nextLockReal)
                {
                    if (fight.lockTaps >= 5) { FailPhase("C7", "Tab did not lock the 창귀 after 5 presses (target " + (lockOn.Target != null ? lockOn.Target.name : "none") + ")"); return; }
                    fight.lockTaps++; fight.nextLockReal = now + .5f; VirtualInput303.Tap(Key.Tab, Center, 1);
                }
                return;
            }
            if (!fight.lockConfirmed)
            {
                fight.lockConfirmed = true; if (fight.lockedAtReal < 0) fight.lockedAtReal = now;
                if (!Failed("C7")) Pass("C7", "Tab locked " + vitals.name + " after " + fight.lockTaps + " press(es) at " + Harness303.F(dist, "F1") + " m");
                if (!File.Exists(Path.Combine(RunFolder, "07_engage_lockon.png"))) Harness303.Capture(RunFolder, "07_engage_lockon");
                if (fight.lockTaps > 0 && !state.realInputs.Any(x => x.StartsWith("P7 Tab"))) state.realInputs.Add("P7 Tab lock-on");
            }
            // 2. dodge a late wind-up
            if (enemy.IsTelegraphing && enemy.TelegraphProgress >= .6f && dist <= enemy.AttackRange + 1f && Time.time >= fight.dodgeReady)
            {
                VirtualInput303.Tap(Key.LeftShift, Center, 2, fight.dodgeLeft ? Key.A : Key.D);
                fight.dodgeLeft = !fight.dodgeLeft; fight.dodges++; fight.dodgeReady = Time.time + (config != null ? config.DodgeCooldown : .6f);
                if (fight.dodges == 1) state.realInputs.Add("P7 Shift+A/D dodge");
                return;
            }
            // 3. low health: back off 6 m
            if (playerVitals.Hp01 <= .35f && !fight.retreating && now > fight.retreatUntil + 5f) { fight.retreating = true; fight.retreatFrom = feet; fight.retreatUntil = now + 3f; state.notes.Add("P7 retreat at HP " + Harness303.F(playerVitals.Hp01)); }
            if (fight.retreating) { if (Harness303.Flat(feet, fight.retreatFrom) >= 6f || now > fight.retreatUntil) fight.retreating = false; else { VirtualInput303.Hold.Keys = new[] { Key.S }; return; } }
            // 4. strafe in progress (line of fire)
            if (now < fight.strafeUntil) { VirtualInput303.Hold.Keys = new[] { fight.strafeLeft ? Key.A : Key.D }; return; }
            float ink = Ink(s);
            bool entry = !drawing.InDrawMode && (drawing.EntryAllowed == null || drawing.EntryAllowed()) && !s.GameplayInputBlocked && s.Walker.Motor.CanBeginDrawing;
            // 5. harvest top-up when ink is short
            if (ink < SpellCost)
            {
                float harvestRange = config != null ? config.HarvestRange : 12f;
                bool can = dist <= harvestRange && !enemy.IsTelegraphing && fight.harvestSeconds < 20f && vitals.Hp > 1.1f;
                if (can)
                {
                    fight.noResourceSince = -1; fight.harvesting = true; fight.harvestSeconds += Time.unscaledDeltaTime; VirtualInput303.Hold.Lmb = true;
                    if (!state.realInputs.Any(x => x.StartsWith("P7 LMB"))) state.realInputs.Add("P7 LMB harvest (ink " + Harness303.F(ink) + ")");
                    if (fight.harvestSeconds >= 20f) state.findings.Add("harvest total reached 20 s");
                    return;
                }
                if (fight.noResourceSince < 0) fight.noResourceSince = now;
                if (dist > harvestRange) VirtualInput303.Hold.Keys = new[] { Key.W };
                return;
            }
            if (fight.harvesting) { fight.harvesting = false; return; }   // one frame with LMB up before a glyph
            fight.noResourceSince = -1;
            // 6. over-fire guard: wait for power already in flight
            if (PendingPower() >= vitals.Hp) { state.phaseNote = "waiting for " + Harness303.F(PendingPower(), "F1") + " in flight"; return; }
            // 7. line of fire
            if (!Visible(s))
            {
                fight.strafeLeft = !fight.strafeLeft; fight.strafeUntil = now + .35f; fight.strafes++;
                VirtualInput303.Hold.Keys = new[] { fight.strafeLeft ? Key.A : Key.D }; return;
            }
            // 8. distance / enemy state rule
            if (dist > 3.5f && !enemy.IsTelegraphing) { if (entry) BeginLetter(s, "P7"); return; }
            if (dist <= 3.5f && enemy.IsRecovering) { if (entry) BeginLetter(s, "P7"); return; }
            if (dist <= 3.5f)
            {
                bool nearLeash = Harness303.Flat(feet, home) > 22f;
                VirtualInput303.Hold.Keys = nearLeash ? new[] { fight.strafeLeft ? Key.A : Key.D } : new[] { Key.S };
                return;
            }
            // telegraphing further out: hold still (the dodge rule covers late wind-ups in range)
        }

        static void Killed(WorldMacroPlaytestSession s)
        {
            if (deathAt < 0) deathAt = Now;
            var rows = logs.damage.ToList();
            var bad = rows.Where(r => !(r.source == DamageSource.Harvest.ToString() || r.source == DamageSource.PlayerDirect.ToString() && r.wiringPaired)).ToList();
            var kill = rows.LastOrDefault(r => r.killed);
            string element = book != null && book.TryGet(state.recipe.letter[0], out var e) ? e.Element.ToString() : "";
            bool killOk = kill != null && kill.source == DamageSource.PlayerDirect.ToString() && (state.plan != "A" || kill.element == element);
            var p7 = logs.commits.Where(c => c.phase == "P7").ToList();
            int injected = p7.Count(c => !c.interrupted), diagnosed = p7.Count(c => c.diagnosed);
            bool castsOk = injected == diagnosed && p7.Where(c => c.success).All(c => c.planned);
            var tokens = ForbiddenTokenHits();
            string detail = "damage rows " + rows.Count + " (bad provenance " + bad.Count + "), kill " + (kill != null ? kill.source + "/" + kill.element : "none") + " expected PlayerDirect/" + element +
                            "; glyphs injected " + injected + " diagnosed " + diagnosed + " interrupted " + p7.Count(c => c.interrupted) + " successes " + p7.Count(c => c.success) +
                            " planned " + p7.Count(c => c.planned) + "; forbidden tokens " + (tokens.Count == 0 ? "none" : string.Join(", ", tokens));
            if (bad.Count == 0 && killOk && castsOk && tokens.Count == 0) Pass("C8", detail); else FailC("C8", detail);
            string c9 = "min HP " + Harness303.F(fight?.minHp ?? 1) + ", damage taken " + Harness303.F(fight?.damageTaken ?? 0, "F1") + ", dodges " + (fight?.dodges ?? 0) + ", strafes " + (fight?.strafes ?? 0) + ", harvest " + Harness303.F(fight?.harvestSeconds ?? 0, "F1") + " s";
            if (playerVitals.Hp01 > 0) Pass("C9", c9); else FailC("C9", c9);
            if (fight != null && fight.harvestSeconds > 0 && state.plan == "A") state.notes.Add("plan A fight used " + Harness303.F(fight.harvestSeconds, "F1") + " s of harvest top-up");
            if (fight != null && fight.harvestSeconds > 0) state.planBUsed = state.planBUsed || state.plan == "B";
            state.notes.Add("P7 kill after " + Harness303.F(Time.realtimeSinceStartup - (fight?.startedReal ?? 0), "F1") + " s: " + detail);
            FlushLogs();
            EnterPhase("P8");
        }

        /// <summary>C8 ④: the harness source must not contain the direct-state tokens. The token strings are assembled so this
        /// file does not match itself.</summary>
        static List<string> ForbiddenTokenHits()
        {
            var tokens = new[] { "Take" + "Damage", "Restore" + "(", "Open" + "WeakPoint", "Reset" + "Encounter", "Set" + "Hp" };
            string folder = Path.Combine(Application.dataPath, "_Project", "Scripts", "Editor", "WorldMacro");
            var hits = new List<string>();
            if (!Directory.Exists(folder)) { hits.Add("source folder missing"); return hits; }
            var files = Directory.GetFiles(folder, "Commission303Combat*.cs").Concat(new[] { "Input303.cs", "UiAdapter303.cs", "Harness303.cs" }.Select(f => Path.Combine(folder, f)).Where(File.Exists)).ToList();
            if (files.Count < 4) hits.Add("harness sources not found in the project (" + files.Count + ")");
            foreach (var f in files)
            {
                string text = File.ReadAllText(f);
                foreach (var t in tokens) if (text.Contains(t)) hits.Add(Path.GetFileName(f) + ":" + t);
            }
            return hits;
        }
    }
}
