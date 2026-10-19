using System;
using System.Collections.Generic;
using System.IO;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data;
using Oheangbu.Data.Spell;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#308 SPEC-PACE-308 (D308-116): turns the pace pass on in the live assets, one part at a time, recording every
    /// previous value so `&lt;part&gt;:off` restores it exactly. A part with a record refuses `on` (off first).
    /// Queue: Oheangbu.EditorTools.WorldMacro.Pace308Switch Execute "&lt;swing|run|profile|all&gt;:&lt;on|off&gt;" or "status".</summary>
    public static class Pace308Switch
    {
        internal const string SwingPath = "Assets/_Project/Resources/Swing308/SpellSwingProfile308.asset";
        internal const string LiveData = "Assets/_Project/Art/World/Architecture296/Data";
        internal const string PaceFolder = "Assets/_Project/Resources/Pace308";
        internal const string PacePath = PaceFolder + "/PaceProfile308.asset";
        internal const float ApexShift = .12f, RunOverWalk = 1.5f;
        internal const float OnDrawSlow = .3f, OnRecovery = .75f, OnFov = 6f, OnWindGain = .25f;
        internal static readonly string[] Parts = { "swing", "run", "profile" };
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Pace308"));
        static string RecordPath(string part) => Path.Combine(Output, "pace308-" + part + "-before.json");

        [Serializable] internal sealed class SwingRecord { public float[] apex = new float[5], lockSec = new float[5]; }
        [Serializable] internal sealed class RunRecord { public string locomotion, appearance; public float locomotionRun, appearanceRun; }
        [Serializable] internal sealed class ProfileRecord { public bool created; public float drawSlow, recovery, fov, smooth, gain, max; }
        [Serializable] sealed class Result { public string status; public List<string> changed = new List<string>(), skipped = new List<string>(); }

        public static string Execute(string command)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode required");
            if (command == "status") return Status();
            string[] parts = (command ?? "").Split(':');
            if (parts.Length != 2 || parts[1] != "on" && parts[1] != "off") throw new ArgumentException("pace308: <swing|run|profile|all>:<on|off> | status");
            var result = new Result();
            foreach (string part in parts[0] == "all" ? Parts : new[] { parts[0] })
            {
                if (Array.IndexOf(Parts, part) < 0) throw new ArgumentException("pace308: unknown part " + part);
                if (parts[1] == "on") On(part, result); else Off(part, result);
            }
            result.status = result.skipped.Count == 0 ? parts[1].ToUpperInvariant() : "PARTIAL";
            return JsonUtility.ToJson(result, true);
        }

        static string Status()
        {
            var lines = new List<string>();
            foreach (string part in Parts) lines.Add(part + "=" + (File.Exists(RecordPath(part)) ? "on" : "off"));
            var swing = AssetDatabase.LoadAssetAtPath<SpellSwingProfile308>(SwingPath);
            if (swing != null) lines.Add("swing single apex/lock=" + swing.AttackSingle.ApexSeconds + "/" + swing.AttackSingle.LockSeconds);
            if (TryRunTargets(out var loco, out var look, out _)) lines.Add("run walk/run/appearance=" + loco.WalkSpeed + "/" + loco.RunSpeed + "/" + look.RunSpeed);
            var pace = AssetDatabase.LoadAssetAtPath<PaceProfile308>(PacePath);
            lines.Add(pace == null ? "profile asset=missing" : "profile slow/recovery/fov/gain=" + pace.DrawSlowSeconds + "/" + pace.EnemyRecoveryScale + "/" + pace.SprintFovDegrees + "/" + pace.WindSprintGain);
            return string.Join("\n", lines);
        }

        static void On(string part, Result result)
        {
            if (File.Exists(RecordPath(part))) { result.skipped.Add(part + " (already on: " + RecordPath(part) + ")"); return; }
            Directory.CreateDirectory(Output);
            switch (part)
            {
                case "swing":
                {
                    var p = AssetDatabase.LoadAssetAtPath<SpellSwingProfile308>(SwingPath);
                    if (p == null) { result.skipped.Add("swing (missing " + SwingPath + ")"); return; }
                    var record = CaptureSwing(p); ApplySwingOn(p);
                    string why = null;
                    if (!p.IsValid || !SwingAimOk(p, out why)) { RestoreSwing(p, record); result.skipped.Add("swing (invalid after shift: " + (why ?? "IsValid") + ")"); return; }
                    File.WriteAllText(RecordPath(part), JsonUtility.ToJson(record, true)); Save(p); result.changed.Add(SwingPath);
                    return;
                }
                case "run":
                {
                    if (!TryRunTargets(out var loco, out var look, out string error)) { result.skipped.Add("run (" + error + ")"); return; }
                    var record = CaptureRun(loco, look); ApplyRunOn(loco, look);
                    File.WriteAllText(RecordPath(part), JsonUtility.ToJson(record, true)); Save(loco); Save(look);
                    result.changed.Add(record.locomotion + " RunSpeed " + record.locomotionRun + " -> " + loco.RunSpeed);
                    result.changed.Add(record.appearance + " RunSpeed " + record.appearanceRun + " -> " + look.RunSpeed);
                    return;
                }
                default:
                {
                    var p = AssetDatabase.LoadAssetAtPath<PaceProfile308>(PacePath); bool created = false;
                    if (p == null)
                    {
                        if (!AssetDatabase.IsValidFolder(PaceFolder)) AssetDatabase.CreateFolder("Assets/_Project/Resources", "Pace308");
                        p = ScriptableObject.CreateInstance<PaceProfile308>(); AssetDatabase.CreateAsset(p, PacePath); created = true;
                    }
                    var record = CaptureProfile(p); record.created = created; ApplyProfileOn(p);
                    if (!p.IsValid(out string error)) { RestoreProfile(p, record); Save(p); result.skipped.Add("profile (invalid: " + error + ")"); return; }
                    File.WriteAllText(RecordPath(part), JsonUtility.ToJson(record, true)); Save(p); result.changed.Add(PacePath + (created ? " (created)" : ""));
                    return;
                }
            }
        }

        static void Off(string part, Result result)
        {
            string path = RecordPath(part);
            if (!File.Exists(path)) { result.skipped.Add(part + " (no record)"); return; }
            string json = File.ReadAllText(path);
            switch (part)
            {
                case "swing":
                {
                    var p = AssetDatabase.LoadAssetAtPath<SpellSwingProfile308>(SwingPath);
                    if (p == null) { result.skipped.Add("swing (missing " + SwingPath + ")"); return; }
                    RestoreSwing(p, JsonUtility.FromJson<SwingRecord>(json)); Save(p); result.changed.Add(SwingPath);
                    break;
                }
                case "run":
                {
                    var record = JsonUtility.FromJson<RunRecord>(json);
                    var loco = AssetDatabase.LoadAssetAtPath<PlayerLocomotionProfileSO>(record.locomotion);
                    var look = AssetDatabase.LoadAssetAtPath<WorldMacroPlayerAppearanceProfile>(record.appearance);
                    if (loco == null || look == null) { result.skipped.Add("run (missing " + (loco == null ? record.locomotion : record.appearance) + ")"); return; }
                    RestoreRun(loco, look, record); Save(loco); Save(look); result.changed.Add(record.locomotion); result.changed.Add(record.appearance);
                    break;
                }
                default:
                {
                    var p = AssetDatabase.LoadAssetAtPath<PaceProfile308>(PacePath);
                    if (p == null) { result.skipped.Add("profile (missing " + PacePath + ")"); return; }
                    // a profile this switch created stays as a default (= off) asset; nothing is deleted
                    RestoreProfile(p, JsonUtility.FromJson<ProfileRecord>(json)); Save(p); result.changed.Add(PacePath);
                    break;
                }
            }
            File.Delete(path);
        }

        static void Save(UnityEngine.Object o) { EditorUtility.SetDirty(o); AssetDatabase.SaveAssetIfDirty(o); }

        // ---- object-level apply / restore (Pace308Checks runs these on copies) ----

        internal static SpellSwingProfile308.Entry[] Waiting(SpellSwingProfile308 p) => new[] { p.AttackSingle, p.AttackArea, p.Summon, p.Field, p.Install };

        static void SetWaiting(SpellSwingProfile308 p, SpellSwingProfile308.Entry[] e)
        {
            p.AttackSingle = e[0]; p.AttackArea = e[1]; p.Summon = e[2]; p.Field = e[3]; p.Install = e[4];
        }

        internal static SwingRecord CaptureSwing(SpellSwingProfile308 p)
        {
            var record = new SwingRecord(); var e = Waiting(p);
            for (int i = 0; i < e.Length; i++) { record.apex[i] = e[i].ApexSeconds; record.lockSec[i] = e[i].LockSeconds; }
            return record;
        }

        internal static void ApplySwingOn(SpellSwingProfile308 p)
        {
            var e = Waiting(p);
            for (int i = 0; i < e.Length; i++) { e[i].ApexSeconds = Shift(e[i].ApexSeconds); e[i].LockSeconds = Shift(e[i].LockSeconds); }
            SetWaiting(p, e);
        }

        internal static void RestoreSwing(SpellSwingProfile308 p, SwingRecord record)
        {
            var e = Waiting(p);
            for (int i = 0; i < e.Length; i++) { e[i].ApexSeconds = record.apex[i]; e[i].LockSeconds = record.lockSec[i]; }
            SetWaiting(p, e);
        }

        static float Shift(float seconds) => Mathf.Round(Mathf.Max(0f, seconds - ApexShift) * 1000f) / 1000f;

        // SPEC-PACE-308 section 3-2: an apex shorter than the aim rise would launch before the aim has settled
        internal static bool SwingAimOk(SpellSwingProfile308 p, out string why)
        {
            string[] names = { "AttackSingle", "AttackArea", "Summon", "Field", "Install" }; var e = Waiting(p);
            for (int i = 0; i < e.Length; i++)
            {
                if (e[i].ApexSeconds < p.Position.AimInSeconds - .0001f) { why = names[i] + " apex " + e[i].ApexSeconds + " < aim " + p.Position.AimInSeconds; return false; }
                if (e[i].LockSeconds < e[i].ApexSeconds) { why = names[i] + " lock " + e[i].LockSeconds + " < apex " + e[i].ApexSeconds; return false; }
            }
            why = null; return true;
        }

        internal static bool TryRunTargets(out PlayerLocomotionProfileSO loco, out WorldMacroPlayerAppearanceProfile look, out string error)
        {
            loco = One<PlayerLocomotionProfileSO>("t:PlayerLocomotionProfileSO", out string a);
            look = One<WorldMacroPlayerAppearanceProfile>("t:WorldMacroPlayerAppearanceProfile", out string b);
            error = a ?? b; return error == null;
        }

        static T One<T>(string filter, out string error) where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets(filter, new[] { LiveData });
            if (guids.Length != 1) { error = guids.Length + " " + typeof(T).Name + " in " + LiveData + " (want exactly 1)"; return null; }
            var o = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
            error = o == null ? "cannot load " + typeof(T).Name : null; return o;
        }

        internal static RunRecord CaptureRun(PlayerLocomotionProfileSO loco, WorldMacroPlayerAppearanceProfile look) => new RunRecord
        {
            locomotion = AssetDatabase.GetAssetPath(loco), appearance = AssetDatabase.GetAssetPath(look), locomotionRun = loco.RunSpeed, appearanceRun = look.RunSpeed,
        };

        internal static float RunOn(float walk) => Mathf.Round(walk * RunOverWalk * 1000f) / 1000f;

        internal static void ApplyRunOn(PlayerLocomotionProfileSO loco, WorldMacroPlayerAppearanceProfile look)
        {
            loco.RunSpeed = RunOn(loco.WalkSpeed); look.RunSpeed = loco.RunSpeed;
        }

        internal static void RestoreRun(PlayerLocomotionProfileSO loco, WorldMacroPlayerAppearanceProfile look, RunRecord record)
        {
            loco.RunSpeed = record.locomotionRun; look.RunSpeed = record.appearanceRun;
        }

        internal static ProfileRecord CaptureProfile(PaceProfile308 p) => new ProfileRecord
        {
            drawSlow = p.DrawSlowSeconds, recovery = p.EnemyRecoveryScale, fov = p.SprintFovDegrees, smooth = p.SprintFovSmoothSeconds, gain = p.WindSprintGain, max = p.WindMax,
        };

        internal static void ApplyProfileOn(PaceProfile308 p)
        {
            p.DrawSlowSeconds = OnDrawSlow; p.EnemyRecoveryScale = OnRecovery; p.SprintFovDegrees = OnFov; p.WindSprintGain = OnWindGain;
        }

        internal static void RestoreProfile(PaceProfile308 p, ProfileRecord r)
        {
            p.DrawSlowSeconds = r.drawSlow; p.EnemyRecoveryScale = r.recovery; p.SprintFovDegrees = r.fov; p.SprintFovSmoothSeconds = r.smooth; p.WindSprintGain = r.gain; p.WindMax = r.max;
        }
    }
}
