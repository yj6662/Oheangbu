using System;
using System.Collections.Generic;
using Oheangbu.App.Demo;
#if !SUMMON_CLOCK_ONLY
using Oheangbu.Core.Domain;
using UnityEngine;
#endif

namespace Oheangbu.EditorTools
{
    public static class DemoSummonFoundationChecks
    {
        [Serializable] public sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public string[] unverified = { "Actual summon manager and ink transaction", "Navigation, target detection and physics line of sight",
                "Damage attribution through CombatLoopWiring", "Animation, presentation and runtime cleanup", "Five summon visuals and user acceptance" };
        }

        // The same suite can run under plain .NET with SUMMON_CLOCK_ONLY. No scene, Unity clock,
        // physics or engine lifecycle is needed to exercise lifetime/ownership/strike rules.
        public static Report RunPure()
        {
            var r = new Report();
            void Check(bool pass, string label) => (pass ? r.passed : r.failed).Add(label);
            try
            {
                var c = new SummonCombatClock(1.25f, 20, .75f);
                var phases = new List<SummonPhase>(); c.PhaseChanged += phases.Add;
                Check(c.Phase == SummonPhase.Forming && Near(c.ActivityRemaining, 20) && !c.CanAttack, "Formation reserves full 20-second activity and cannot attack");
                Check(!c.TryBeginAttack(1, 10, 1, .25f, 1) && !c.SetActivityPhase(SummonPhase.Following), "Manager cannot skip formation with navigation or attack");
                c.Advance(0); Check(c.Elapsed == 0 && phases.Count == 0, "Paused formation makes no time or phase progress");
                c.Advance(1); Check(c.Phase == SummonPhase.Forming && Near(c.FormationProgress01, .8f), "Formation progress uses supplied scaled time");
                c.Advance(.25f); Check(c.Phase == SummonPhase.Following && Near(c.PhaseElapsed, 0) && Near(c.ActivityRemaining, 20), "Formation boundary begins activity exactly once");
                c.Advance(2); c.SetActivityPhase(SummonPhase.Approaching); c.Advance(.5f);
                Check(Near(c.ActivityElapsed, 2.5f) && Near(c.PhaseElapsed, .5f), "Navigation phase changes do not extend shared activity budget");
                c.SetActivityPhase(SummonPhase.Approaching);
                Check(Near(c.PhaseElapsed, .5f), "Repeated navigation state does not reset phase clock");
                Check(!c.SetActivityPhase(SummonPhase.Attacking) && !c.SetActivityPhase(SummonPhase.Forming), "Navigation cannot forge attack or rewind lifecycle");
                c.SetActivityPhase(SummonPhase.Returning); c.Advance(17.5f);
                Check(c.Phase == SummonPhase.Dissolving && Near(c.ActivityRemaining, 0) && Near(c.DissolveProgress01, 0), "20-second activity expiration dissolves even while returning");
                c.Advance(0); Check(c.Phase == SummonPhase.Dissolving && Near(c.DissolveProgress01, 0), "Pause freezes dissolution");
                c.Advance(.375f); Check(Near(c.DissolveProgress01, .5f) && !c.CanAttack, "Dissolve progress is separate from activity and disables combat");
                c.Advance(.375f); Check(c.Phase == SummonPhase.Ended && Near(c.Elapsed, 22), "Formation plus activity plus dissolve lifetime is exact");
                int transitions = phases.Count; c.Advance(100); c.BeginDissolve(); c.EndImmediately();
                Check(Near(c.Elapsed, 22) && phases.Count == transitions, "Ended state is terminal and repeated cleanup emits no extra transition");

                c = new SummonCombatClock(1.25f, 20, .75f); phases.Clear(); c.PhaseChanged += phases.Add; c.Advance(100);
                Check(c.Phase == SummonPhase.Ended && Near(c.Elapsed, 22) && phases.Count == 3 && phases[0] == SummonPhase.Following && phases[1] == SummonPhase.Dissolving && phases[2] == SummonPhase.Ended,
                    "One coarse frame crosses all boundaries in chronological order without extra lifetime");
                foreach (int fps in new[] { 30, 60, 120 })
                {
                    c = new SummonCombatClock(1.2f, 20, .8f);
                    for (int i = 0; i < fps * 23; i++) c.Advance(1f / fps);
                    Check(c.Phase == SummonPhase.Ended && Near(c.Elapsed, 22), fps + " fps reaches the same authored lifetime without residue");
                }
                var normal = new SummonCombatClock(1.25f, 20, .75f); var slow = new SummonCombatClock(1.25f, 20, .75f);
                for (int i = 0; i < 100; i++) normal.Advance(.125f);
                for (int i = 0; i < 400; i++) slow.Advance(.03125f);
                Check(normal.Phase == slow.Phase && Near(normal.Elapsed, slow.Elapsed) && Near(normal.ActivityRemaining, slow.ActivityRemaining), "Quarter speed changes wall duration but not accumulated scaled-time state");

                c = new SummonCombatClock(0, 20, .75f);
                Check(c.CanAttack && c.TryBeginAttack(1, -123, 8, .5f, 1), "Zero formation starts active and negative Unity instance IDs are valid targets");
                Check(!c.TryBeginAttack(2, 456, 1, 0, 1) && !c.SetActivityPhase(SummonPhase.Returning), "Only one pending attack; navigation must explicitly cancel windup");
                Check(!c.TryConsumeHit(1, -123, 8, true, true, true), "Hit cannot be consumed before windup completes");
                c.Advance(0); Check(c.PendingAttackId == 1 && Near(c.PhaseElapsed, 0), "Pause preserves pending windup without advancing attack");
                c.Advance(.5f);
                Check(!c.TryConsumeHit(999, -123, 8, true, true, true) && c.PendingAttackId == 1, "Wrong attack ID does not steal pending strike");
                Check(c.TryConsumeHit(1, -123, 8, true, true, true) && !c.TryConsumeHit(1, -123, 8, true, true, true), "Due valid strike is delivered exactly once");
                Check(!c.CanAttack && !c.TryBeginAttack(2, -123, 8, 0, 1), "Post-impact cooldown prevents repeated attack");
                c.Advance(1);
                Check(!c.TryBeginAttack(1, -123, 8, 0, 1) && c.TryBeginAttack(2, -123, 8, .5f, 1), "Cooldown boundary accepts next ID but rejects already used attack ID");
                c.CancelAttack(); c.Advance(1);
                Check(c.PendingAttackId == 0 && !c.TryConsumeHit(2, -123, 8, true, true, true) && c.CanAttack, "Cancellation clears windup and prevents delayed hit while respecting cooldown");

                for (int reason = 0; reason < 5; reason++)
                {
                    c = new SummonCombatClock(0, 20, .75f); c.TryBeginAttack(1, 10, 2, .25f, 1); c.Advance(.25f);
                    bool applied = c.TryConsumeHit(1, reason == 0 ? 11 : 10, reason == 1 ? 3u : 2u, reason != 2, reason != 3, reason != 4);
                    Check(!applied && c.PendingAttackId == 0 && !c.TryConsumeHit(1, 10, 2, true, true, true),
                        new[] { "Different enemy", "Restored enemy life revision", "Dead target", "Out-of-range target", "Wall-occluded target" }[reason] + " consumes failed strike and cannot later revive it");
                }
                c = new SummonCombatClock(0, 1, .75f);
                Check(!c.TryBeginAttack(1, 10, 1, 1, 1), "Strike exactly at expiration is rejected at scheduling");
                Check(c.TryBeginAttack(1, 10, 1, .25f, 1), "Strike before expiration can be scheduled"); c.Advance(1);
                Check(c.Phase == SummonPhase.Dissolving && c.PendingAttackId == 0 && !c.TryConsumeHit(1, 10, 1, true, true, true), "Frame crossing expiry clears pending strike and cannot apply late damage");
                c = new SummonCombatClock(0, 20, .75f); c.TryBeginAttack(1, 10, 1, 1, 1); c.Advance(.25f); c.BeginDissolve(); c.BeginDissolve(); c.Advance(.75f);
                Check(c.Phase == SummonPhase.Ended && Near(c.Elapsed, 1) && !c.TryConsumeHit(1, 10, 1, true, true, true), "Early dissolve is idempotent and cancels telegraph rather than allowing late hit");
                c = new SummonCombatClock(2, 20, 0); c.BeginDissolve(); Check(c.Phase == SummonPhase.Ended, "Immediate dissolve while forming has no hidden active frame");
                c = new SummonCombatClock(0, 20, .75f); c.TryBeginAttack(1, 10, 1, 0, 1); c.EndImmediately();
                Check(!c.TryConsumeHit(1, 10, 1, true, true, true) && c.PendingAttackId == 0, "Death/rest/scene-style immediate cleanup invalidates a due hit");

                var slot = new SummonCombatSlot(); var first = new SummonCombatClock(0, 20, .8f); var next = new SummonCombatClock(1.2f, 20, .8f);
                Check(slot.TryReplace(first, true, true), "Valid committed summon acquires empty slot");
                first.TryBeginAttack(1, 10, 1, .5f, 1); first.Advance(.25f);
                Check(!slot.TryReplace(next, false, true) && ReferenceEquals(slot.Current, first) && first.PendingAttackId == 1, "Bad placement preserves old summon and its pending attack");
                Check(!slot.TryReplace(next, true, false) && Near(first.Elapsed, .25f), "Uncommitted cost does not replace or advance old summon");
                Check(!slot.TryReplace(first, true, true) && !slot.TryReplace(null, true, true), "Duplicate same instance and null requests cannot dismiss active summon");
                Check(slot.TryReplace(next, true, true) && ReferenceEquals(slot.Current, next) && first.Phase == SummonPhase.Ended && first.PendingAttackId == 0,
                    "Successful replacement terminates old attacker before new formation occupies slot");
                first.Advance(1); Check(!first.TryConsumeHit(1, 10, 1, true, true, true), "Replaced summon cannot strike even if old manager callback arrives");
                var ended = new SummonCombatClock(0, 1, 0); ended.EndImmediately(); var dissolving = new SummonCombatClock(0, 20, 1); dissolving.BeginDissolve();
                Check(!slot.TryReplace(ended, true, true) && !slot.TryReplace(dissolving, true, true) && ReferenceEquals(slot.Current, next), "Ended/dissolving candidates cannot replace healthy current summon");
                slot.Advance(100); Check(slot.Current == null && next.Phase == SummonPhase.Ended, "Natural expiry clears the single active slot");
                next = new SummonCombatClock(0, 20, .8f); slot.TryReplace(next, true, true); slot.Clear(); slot.Clear();
                Check(slot.Current == null && next.Phase == SummonPhase.Ended, "Repeated slot cleanup is safe and leaves no active clock");
                var other = new SummonCombatSlot(); next = new SummonCombatClock(0, 20, 1); other.TryReplace(next, true, true); slot.Clear();
                Check(ReferenceEquals(other.Current, next) && next.CanAttack, "Independent owners have no shared static summon state");

                Check(Throws(() => new SummonCombatClock(-1, 20, 1)) && Throws(() => new SummonCombatClock(1, 0, 1)) && Throws(() => new SummonCombatClock(1, 20, float.NaN)), "Invalid lifetime arguments are rejected");
                c = new SummonCombatClock(0, 20, 1);
                Check(Throws(() => c.Advance(float.NaN)) && Throws(() => c.Advance(-1)) && Throws(() => c.Advance(float.PositiveInfinity)) && Near(c.Elapsed, 0), "Invalid delta cannot corrupt clock state");
                Check(Throws(() => c.TryBeginAttack(1, 10, 1, -1, 1)) && Throws(() => c.TryBeginAttack(1, 10, 1, 0, float.NaN)) && c.PendingAttackId == 0, "Invalid attack timing rejected without creating pending attack");
                Check(!c.TryBeginAttack(0, 10, 1, 0, 1) && !c.TryBeginAttack(1, 0, 1, 0, 1), "Unowned attack and zero target IDs are rejected");
            }
            catch (Exception e) { r.failed.Add(e.ToString()); }
            r.status = r.failed.Count == 0 ? "PASS" : "FAIL"; return r;
        }

#if !SUMMON_CLOCK_ONLY
        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Edit-mode isolated checks only.");
            var r = RunPure(); var p = ScriptableObject.CreateInstance<SummonCombatProfile>();
            void Check(bool pass, string label) => (pass ? r.passed : r.failed).Add(label);
            try
            {
                Check(p.TryValidate(out _) && p.Letter == "곰" && p.Element == Element.Wood && Near(p.ActivitySeconds, 20), "Default wood-deer profile validates with 20-second activity");
                Check(!p.TryValidate(out _, true), "Runtime spawning requires an authored presentation prefab");
                string[] letters = { "곰", "놈", "솜", "몸", "옴" }; Element[] elements = { Element.Wood, Element.Fire, Element.Metal, Element.Earth, Element.Water };
                for (int i = 0; i < letters.Length; i++) { p.Letter = letters[i]; p.Element = elements[i]; Check(p.TryValidate(out _), letters[i] + " correct element mapping is accepted without claiming its runtime implementation"); }
                p.Letter = "곰"; Check(!p.TryValidate(out _), "Mismatched letter and elemental provenance rejected"); p.Element = Element.Wood;
                p.AttackRange = 13; Check(!p.TryValidate(out _), "Attack range cannot exceed detection"); p.AttackRange = 2.4f;
                p.LeashRange = 10; Check(!p.TryValidate(out _), "Detection cannot exceed return leash"); p.LeashRange = 18;
                p.FollowDistance = 18; Check(!p.TryValidate(out _), "Following distance cannot defeat leash"); p.FollowDistance = 2.5f;
                p.MaxSlope = 90; Check(!p.TryValidate(out _), "Vertical placement is invalid"); p.MaxSlope = 32;
                p.GroundMask = 0; Check(!p.TryValidate(out _), "Empty ground mask rejected"); p.GroundMask = ~0;
                p.NavMeshAreaMask = 0; Check(!p.TryValidate(out _), "Empty navigation area mask rejected"); p.NavMeshAreaMask = -1;
                for (int axis = 0; axis < 3; axis++)
                {
                    var offset = Vector3.zero; offset[axis] = float.NaN; p.FootprintOffset = offset;
                    Check(!p.TryValidate(out _), "Footprint offset axis " + axis + " rejects non-finite coordinates");
                }
                p.FootprintOffset = Vector3.zero;
                foreach (var field in typeof(SummonCombatProfile).GetFields())
                {
                    if (field.FieldType != typeof(float)) continue;
                    float original = (float)field.GetValue(p); field.SetValue(p, float.NaN); bool nanRejected = !p.TryValidate(out _);
                    field.SetValue(p, float.PositiveInfinity); bool infinityRejected = !p.TryValidate(out _); field.SetValue(p, original);
                    Check(nanRejected && infinityRejected, field.Name + " rejects NaN and infinity");
                }
                Check(p.TryValidate(out _), "Isolated validation restores coherent profile after invalid-value tests");
            }
            catch (Exception e) { r.failed.Add(e.ToString()); }
            finally { UnityEngine.Object.DestroyImmediate(p); }
            r.status = r.failed.Count == 0 ? "PASS" : "FAIL"; return JsonUtility.ToJson(r, true);
        }
#endif
        private static bool Near(float a, float b) => Math.Abs(a - b) < .001f;
        private static bool Throws(Action action) { try { action(); return false; } catch (ArgumentOutOfRangeException) { return true; } }
    }
}
