using System;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data;
using Oheangbu.Data.Spell;
using Oheangbu.Drawing;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#308 SPEC-PACE-308 section 9: edit-mode checks P1 - P6. Everything runs in memory on copies; no asset is saved.
    /// Queue: Oheangbu.EditorTools.WorldMacro.Pace308Checks Run "all".</summary>
    public static class Pace308Checks
    {
        [Serializable] sealed class Report { public int pass, fail; public List<string> lines = new List<string>(); }

        public static string Run(string command)
        {
            if (command != "all") throw new ArgumentException("pace308-checks: all");
            var r = new Report();
            P1(r); P2(r); P3(r); P4(r); P5(r); P6(r);
            return JsonUtility.ToJson(r, true);
        }

        static void Check(Report r, string id, bool ok, string detail)
        {
            if (ok) r.pass++; else r.fail++;
            r.lines.Add((ok ? "PASS " : "FAIL ") + id + " " + detail);
        }

        // P1: the draw slow-down lifts at DrawSlowSeconds of real time, never when the setting is 0
        static void P1(Report r)
        {
            bool never = !DrawingInputController.ShouldLift(0f, 0f, 0f) && !DrawingInputController.ShouldLift(0f, 1000f, 0f);
            bool before = !DrawingInputController.ShouldLift(10f, 10.29f, .3f);
            bool at = DrawingInputController.ShouldLift(10f, 10.3001f, .3f) && DrawingInputController.ShouldLift(10f, 12f, .3f);
            Check(r, "P1", never && before && at, $"slow0 never={never} .29 false={before} .30 true={at}");
        }

        // P2: the live swing table and its shifted copy keep apex >= aim rise, lock >= apex and IsValid
        static void P2(Report r)
        {
            var live = AssetDatabase.LoadAssetAtPath<SpellSwingProfile308>(Pace308Switch.SwingPath);
            if (live == null) { Check(r, "P2", false, "missing " + Pace308Switch.SwingPath); return; }
            bool liveOk = live.IsValid & Pace308Switch.SwingAimOk(live, out string whyLive);
            var copy = Object.Instantiate(live);
            try
            {
                bool alreadyOn = System.IO.File.Exists(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../Art/Pace308/pace308-swing-before.json")));
                if (!alreadyOn) Pace308Switch.ApplySwingOn(copy);
                bool onOk = copy.IsValid & Pace308Switch.SwingAimOk(copy, out string whyOn);
                var e = Pace308Switch.Waiting(copy);
                Check(r, "P2", liveOk && onOk, $"live={liveOk}{(whyLive != null ? " (" + whyLive + ")" : "")} on={onOk}{(whyOn != null ? " (" + whyOn + ")" : "")} single={e[0].ApexSeconds}/{e[0].LockSeconds} area={e[1].ApexSeconds}/{e[1].LockSeconds} summon={e[2].ApexSeconds}/{e[2].LockSeconds} field={e[3].ApexSeconds}/{e[3].LockSeconds} install={e[4].ApexSeconds}/{e[4].LockSeconds} aim={copy.Position.AimInSeconds}{(alreadyOn ? " (live already on)" : "")}");
            }
            finally { Object.DestroyImmediate(copy); }
        }

        // P3: the recovery scale is 1 without a profile, the set value inside the range, 1 outside it
        static void P3(Report r)
        {
            var p = ScriptableObject.CreateInstance<PaceProfile308>();
            try
            {
                float none = PaceProfile308.RecoveryScaleOf(null), def = PaceProfile308.RecoveryScaleOf(p);
                p.EnemyRecoveryScale = .75f; float on = PaceProfile308.RecoveryScaleOf(p);
                p.EnemyRecoveryScale = 2f; float high = PaceProfile308.RecoveryScaleOf(p);
                p.EnemyRecoveryScale = .1f; float low = PaceProfile308.RecoveryScaleOf(p);
                p.EnemyRecoveryScale = 1f; p.SprintFovDegrees = 40f; float otherBad = PaceProfile308.RecoveryScaleOf(p);
                bool ok = none == 1f && def == 1f && Mathf.Approximately(on, .75f) && high == 1f && low == 1f && otherBad == 1f;
                Check(r, "P3", ok, $"none={none} default={def} .75={on} 2->{high} .1->{low} invalid-other->{otherBad}");
            }
            finally { Object.DestroyImmediate(p); }
        }

        // P4: the sprint FOV is added as a difference: an outside FOV change (impact breath) survives, the sum equals the target,
        // and after an absolute FOV write (applied reset to 0) the next step adds the whole target again
        static void P4(Report r)
        {
            const float baseFov = 60f, degrees = 6f, smooth = .15f, dt = 1f / 60f;
            float fov = baseFov, applied = 0f, s01 = 0f, breath = 0f; bool ok = true; string detail = "";
            for (int frame = 0; frame < 40; frame++)
            {
                float outside = frame == 5 ? 1.5f : frame == 20 ? -1.5f : 0f; fov += outside; breath += outside;
                s01 = CameraRigController.NextSprint01(s01, frame < 30, dt, smooth);
                float target = s01 * degrees; fov += target - applied; applied = target;
                if (Mathf.Abs(fov - baseFov - breath - target) > .0001f) { ok = false; detail = "frame " + frame + " drift " + (fov - baseFov - breath - target); break; }
            }
            bool rose = ok && Mathf.Abs(CameraRigController.NextSprint01(0f, true, smooth, smooth) - 1f) < .0001f;
            bool fell = ok && s01 < .0001f;
            fov = 70f; applied = 0f; s01 = 1f;
            s01 = CameraRigController.NextSprint01(s01, true, dt, smooth); fov += s01 * degrees - applied;
            bool reset = Mathf.Abs(fov - 76f) < .0001f;
            Check(r, "P4", ok && rose && fell && reset, $"compose={ok}{(detail.Length > 0 ? " (" + detail + ")" : "")} full-in-{smooth}s={rose} back-to-0={fell} after-reset={reset}");
        }

        // P5: on -> off restores every serialized byte (copies of the live assets; nothing saved)
        static void P5(Report r)
        {
            var copies = new List<Object>(); var fails = new List<string>();
            try
            {
                var swing = AssetDatabase.LoadAssetAtPath<SpellSwingProfile308>(Pace308Switch.SwingPath);
                if (swing != null)
                {
                    var c = Object.Instantiate(swing); copies.Add(c); string before = EditorJsonUtility.ToJson(c);
                    var rec = JsonUtility.FromJson<Pace308Switch.SwingRecord>(JsonUtility.ToJson(Pace308Switch.CaptureSwing(c)));
                    Pace308Switch.ApplySwingOn(c); bool moved = EditorJsonUtility.ToJson(c) != before;
                    Pace308Switch.RestoreSwing(c, rec); if (!moved || EditorJsonUtility.ToJson(c) != before) fails.Add("swing moved=" + moved);
                }
                else fails.Add("swing missing");
                if (Pace308Switch.TryRunTargets(out var loco, out var look, out string error))
                {
                    var l = Object.Instantiate(loco); var a = Object.Instantiate(look); copies.Add(l); copies.Add(a);
                    string bl = EditorJsonUtility.ToJson(l), ba = EditorJsonUtility.ToJson(a);
                    var rec = JsonUtility.FromJson<Pace308Switch.RunRecord>(JsonUtility.ToJson(Pace308Switch.CaptureRun(l, a)));
                    Pace308Switch.ApplyRunOn(l, a); Pace308Switch.RestoreRun(l, a, rec);
                    if (EditorJsonUtility.ToJson(l) != bl || EditorJsonUtility.ToJson(a) != ba) fails.Add("run");
                }
                else fails.Add("run targets: " + error);
                var pace = AssetDatabase.LoadAssetAtPath<PaceProfile308>(Pace308Switch.PacePath);
                var p = pace != null ? Object.Instantiate(pace) : ScriptableObject.CreateInstance<PaceProfile308>(); copies.Add(p);
                string bp = EditorJsonUtility.ToJson(p);
                var prec = JsonUtility.FromJson<Pace308Switch.ProfileRecord>(JsonUtility.ToJson(Pace308Switch.CaptureProfile(p)));
                Pace308Switch.ApplyProfileOn(p); bool valid = p.IsValid(out string perr); Pace308Switch.RestoreProfile(p, prec);
                if (!valid) fails.Add("profile on invalid: " + perr);
                if (EditorJsonUtility.ToJson(p) != bp) fails.Add("profile");
            }
            finally { foreach (var c in copies) if (c != null) Object.DestroyImmediate(c); }
            Check(r, "P5", fails.Count == 0, fails.Count == 0 ? "swing / run / profile round trips byte-equal" : string.Join(", ", fails));
        }

        // P6: run on = walk x 1.5 on both live profiles, nothing else in them changes, and exactly one of each is the target
        static void P6(Report r)
        {
            if (!Pace308Switch.TryRunTargets(out var loco, out var look, out string error)) { Check(r, "P6", false, error); return; }
            var l = Object.Instantiate(loco); var a = Object.Instantiate(look);
            try
            {
                float walk = l.WalkSpeed, runBefore = l.RunSpeed, lookBefore = a.RunSpeed;
                string bl = EditorJsonUtility.ToJson(l), ba = EditorJsonUtility.ToJson(a);
                Pace308Switch.ApplyRunOn(l, a);
                bool speeds = Mathf.Abs(l.RunSpeed - walk * 1.5f) < .001f && a.RunSpeed == l.RunSpeed;
                float runOn = l.RunSpeed; l.RunSpeed = runBefore; a.RunSpeed = lookBefore;
                bool onlyRun = EditorJsonUtility.ToJson(l) == bl && EditorJsonUtility.ToJson(a) == ba;
                Check(r, "P6", speeds && onlyRun, $"walk={walk} run {runBefore}->{runOn} appearance {lookBefore}->{runOn} (anim x{runOn / Mathf.Max(.01f, a.NativeRunSpeed):0.00}) only-run={onlyRun} targets={AssetDatabase.GetAssetPath(loco)} | {AssetDatabase.GetAssetPath(look)}");
            }
            finally { Object.DestroyImmediate(l); Object.DestroyImmediate(a); }
        }
    }
}
