using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.BrushRender;
using Oheangbu.Combat;
using Oheangbu.Drawing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#308 lock-on drawing stability (SPEC-LOCKON-DRAW-STABILITY-308, D308-21).
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.LockDraw308 Run "check"
    ///       Edit Mode, changes nothing: the pull rule table (modes x cases, the real LockOnDrawPull.Step), the new config fields
    ///       of every CombatConfigSO asset (in the YAML or read from the class default), the script execution order the fix
    ///       relies on, the render-time stroke anchor against the LateUpdate one (own preview scene, HideAndDontSave objects),
    ///       and the assembly borders that keep recognition out of reach.
    ///   ... Run "measure:full" | "measure:edge" | "measure:hold" | "measure:asset"   Edit Mode: isolated save (Harness303), own Play
    ///   ... Run "run:&lt;mode&gt;"                    inside a Play that is already on an isolated store (refused on the real slot)
    ///   ... Run "status" | "report" | "abort" | "compare" | "fixtures:cleanup"
    /// Queue paths never open a dialog: a refusal comes back as "refused: ...". Nothing here saves a scene or an asset.
    /// The measurement half is LockDraw308.Measure.cs.
    /// check 수리 2026-10-05 (the first editor run: 50 checks, 6 failed - README "check 수리"): E1 - E3 read the order as
    /// saved-or-attribute, B1 - B3 read "uses" and "may use", A2 compares the scale in float steps, A3 is split by origin and
    /// measured with PreciseScreen (not Camera.WorldToScreenPoint), A5 / A6 / E6 are new. 54 checks.</summary>
    public static partial class LockDraw308
    {
        const string CheckVersion = "2 (check 수리 2026-10-05)";
        const string FixturePrefix = "LockDraw308_Fixture";
        static readonly string[] ConfigFields =
        {
            "_lockOnDrawMode", "_lockOnDrawSettleSeconds", "_lockOnDrawResumeDelay", "_lockOnDrawResumeSeconds", "_lockOnDrawEdgeMargin",
            "_lockOnDrawEdgeGain", "_lockOnDrawEdgeMaxSpeed", "_lockOnDrawEdgeAcceleration", "_lockOnDrawAimKeepDegrees",
        };

        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Playtest308/LockDraw"));
        static string F(float v, string f = "F3") => v.ToString(f, CultureInfo.InvariantCulture);

        public static string Run(string command)
        {
            command = (command ?? "").Trim();
            string[] parts = command.Split(':'); string verb = parts[0].Trim().ToLowerInvariant();
            switch (verb)
            {
                case "": case "help": return Help();
                case "check": return Check();
                case "fixtures": return parts.Length > 1 && parts[1].Trim() == "cleanup" ? CleanupFixtures() : "refused: expected fixtures:cleanup";
                case "measure": return StartMeasure(parts.Skip(1), true);
                case "run": return StartMeasure(parts.Skip(1), false);
                case "status": return MeasureStatus();
                case "report": return MeasureReport();
                case "abort": return MeasureAbort();
                case "compare": return Compare(parts.Length > 1 ? parts[1].Trim() : "");
            }
            return "refused: unknown command '" + command + "'\n" + Help();
        }

        static string Help() => "LockDraw308: check (Edit, read only) | measure:<full|edge|hold|asset>[:opts] (Edit, isolated save, own Play) | run:<mode>[:opts] (inside a Play on an isolated store) | status | report | abort | compare[:<folder>] | fixtures:cleanup\n" +
            "opts: move=strafe|none dist=3.5 offset=18 seconds=1.5 target=<actor id> ai=hold|live glyph=<letter> fog=on|off";

        // ------------------------------------------------------------------ check
        sealed class Checks
        {
            public readonly List<string> Lines = new List<string>(); public int Failed, Count;
            public void Add(string id, bool ok, string detail) { Count++; if (!ok) Failed++; Lines.Add((ok ? "PASS " : "FAIL ") + id + " | " + detail); }
            public void Info(string text) { Lines.Add("INFO " + text); }
        }

        static string Check()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: check is Edit Mode only";
            if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) return "refused: scripts are compiling or failed to compile";
            var c = new Checks();
            var scene = SceneManager.GetActiveScene(); bool dirtyBefore = scene.isDirty; int rootsBefore = scene.IsValid() ? scene.rootCount : 0;
            CombatConfigSO defaults = null;
            try
            {
                defaults = ScriptableObject.CreateInstance<CombatConfigSO>(); defaults.hideFlags = HideFlags.HideAndDontSave;
                c.Info("LockDraw308 check " + DateTime.Now.ToString("s") + " | scene " + scene.path + " | Unity " + Application.unityVersion + " | check version " + CheckVersion);
                RuleTable(c, defaults);
                ConfigFieldsCheck(c, defaults);
                ExecutionOrder(c);
                AssemblyBorders(c);
                AnchorFixture(c);
                SceneWiring(c, scene);
            }
            catch (Exception e) { c.Add("check.exception", false, e.ToString()); }
            finally { if (defaults != null) Object.DestroyImmediate(defaults); }
            // the check must leave the open scene as it found it
            c.Add("X1.scene_untouched", scene.isDirty == dirtyBefore && (!scene.IsValid() || scene.rootCount == rootsBefore) && LeftoverFixtures() == 0,
                "open scene dirty " + dirtyBefore + " -> " + scene.isDirty + ", roots " + rootsBefore + " -> " + (scene.IsValid() ? scene.rootCount : 0) + ", leftover fixture objects " + LeftoverFixtures());
            string summary = "lockdraw308 check: " + c.Count + " checks, " + c.Failed + " failed";
            c.Lines.Add(summary);
            try
            {
                Directory.CreateDirectory(Root);
                File.WriteAllText(Path.Combine(Root, "check_" + Harness303.UtcStamp() + ".txt"), string.Join("\n", c.Lines), new UTF8Encoding(false));
            }
            catch (Exception e) { c.Lines.Add("INFO report not written: " + e.Message); }
            return string.Join("\n", c.Lines);
        }

        // ---- rule table: the real LockOnDrawPull.Step with the class-default numbers (python twin: sim/lockdraw_checks.py)
        struct Row { public float T, Yaw, Pitch; public LockOnDrawPull.State State; }

        static void Viewport(float yaw, float pitch, float targetYaw, float targetPitch, float tanX, float tanY, out float vx, out float vy, out bool front)
        {
            float ex = Mathf.DeltaAngle(yaw, targetYaw) * Mathf.Deg2Rad, ey = Mathf.DeltaAngle(pitch, targetPitch) * Mathf.Deg2Rad;
            front = Mathf.Abs(ex) < Mathf.PI * .5f;
            vx = front ? .5f + Mathf.Tan(ex) / tanX * .5f : .5f; vy = front ? .5f - Mathf.Tan(ey) / tanY * .5f : .5f;
        }

        static List<Row> Table(LockOnDrawPull.Settings c, int fps, float seconds, Func<float, Vector2> target, float drawFrom = 0f, float drawTo = 1e9f,
            float startYaw = 0f, float startPitch = 0f, bool viewport = true)
        {
            float tanY = Mathf.Tan(52f * .5f * Mathf.Deg2Rad), tanX = tanY * 16f / 9f, dt = 1f / fps;
            var state = default(LockOnDrawPull.State); float yaw = startYaw, pitch = startPitch;
            int n = Mathf.RoundToInt(seconds * fps); var rows = new List<Row>(n);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)fps; Vector2 tg = target(t);
                var f = new LockOnDrawPull.Frame { Drawing = t >= drawFrom - 1e-6f && t < drawTo - 1e-6f, HasYaw = true, Yaw = yaw, Pitch = pitch, TargetYaw = tg.x, TargetPitch = tg.y, DeltaTime = dt };
                if (viewport)
                {
                    Viewport(yaw, pitch, tg.x, tg.y, tanX, tanY, out f.ViewportX, out f.ViewportY, out f.InFront);
                    f.HasViewport = true; f.TanHalfFovX = tanX; f.TanHalfFovY = tanY;
                }
                LockOnDrawPull.Step(ref state, in c, in f, out yaw, out pitch);
                rows.Add(new Row { T = (i + 1) / (float)fps, Yaw = yaw, Pitch = pitch, State = state });
            }
            return rows;
        }

        static void Legacy(ref float yaw, ref float pitch, float targetYaw, float targetPitch, float pull, float dt)
        {
            // PlayerMotor.ApplyLockOnPull before the stage, statement for statement
            float blend = 1f - Mathf.Exp(-pull * dt);
            yaw = Mathf.LerpAngle(yaw, targetYaw, blend);
            pitch = Mathf.LerpAngle(pitch, targetPitch, blend);
        }

        static float[] Speeds(List<Row> rows, int fps)
        {
            var s = new float[Math.Max(0, rows.Count - 1)];
            for (int i = 1; i < rows.Count; i++) s[i - 1] = Mathf.DeltaAngle(rows[i - 1].Yaw, rows[i].Yaw) * fps;
            return s;
        }

        static void RuleTable(Checks c, CombatConfigSO d)
        {
            LockOnDrawPull.Settings S(LockOnDrawMode mode, float pull) => d.GetLockOnDrawSettings(pull, mode);
            var modes = new[] { LockOnDrawMode.EdgeOnly, LockOnDrawMode.Hold };
            // R1 FullPull (and every mode outside the draw) is the legacy formula, value for value
            {
                var rnd = new System.Random(1); bool same = true; float worst = 0f;
                for (int trial = 0; trial < 40; trial++)
                {
                    float pull = new[] { 1f, 2.5f, .3f, 7f }[rnd.Next(4)]; int fps = new[] { 30, 60, 144 }[rnd.Next(3)]; float dt = 1f / fps;
                    float yaw = (float)rnd.NextDouble() * 360f, pitch = (float)rnd.NextDouble() * 80f - 30f, ly = yaw, lp = pitch;
                    var st = default(LockOnDrawPull.State); var cfg = S(LockOnDrawMode.FullPull, pull);
                    float tanY = Mathf.Tan(26f * Mathf.Deg2Rad), tanX = tanY * 16f / 9f;
                    for (int i = 0; i < 200; i++)
                    {
                        float ty = Mathf.Repeat(i * ((float)rnd.NextDouble() * 4f - 2f), 360f), tp = (float)rnd.NextDouble() * 40f - 20f;
                        var f = new LockOnDrawPull.Frame { Drawing = (i / 50) % 2 == 1, HasYaw = true, Yaw = yaw, Pitch = pitch, TargetYaw = ty, TargetPitch = tp, DeltaTime = dt, HasViewport = true, TanHalfFovX = tanX, TanHalfFovY = tanY };
                        Viewport(yaw, pitch, ty, tp, tanX, tanY, out f.ViewportX, out f.ViewportY, out f.InFront);
                        LockOnDrawPull.Step(ref st, in cfg, in f, out yaw, out pitch);
                        Legacy(ref ly, ref lp, ty, tp, pull, dt);
                        if (yaw != ly || pitch != lp) { same = false; worst = Mathf.Max(worst, Mathf.Abs(yaw - ly), Mathf.Abs(pitch - lp)); }
                    }
                }
                c.Add("R1.fullpull_is_legacy", same, "FullPull equals the legacy step exactly over 40 x 200 frames, drawing or not (worst difference " + F(worst, "G3") + ")");
                foreach (var mode in modes)
                {
                    var st = default(LockOnDrawPull.State); var cfg = S(mode, 2.5f); float yaw = 10f, pitch = 5f, ly = yaw, lp = pitch; bool ok = true;
                    for (int i = 0; i < 120; i++)
                    {
                        var f = new LockOnDrawPull.Frame { HasYaw = true, Yaw = yaw, Pitch = pitch, TargetYaw = 60f, TargetPitch = -4f, DeltaTime = 1f / 60f };
                        LockOnDrawPull.Step(ref st, in cfg, in f, out yaw, out pitch); Legacy(ref ly, ref lp, 60f, -4f, 2.5f, 1f / 60f);
                        ok &= yaw == ly && pitch == lp;
                    }
                    c.Add("R1." + mode + "_not_drawing_is_legacy", ok, "locked, never drew: the pull is the legacy step exactly");
                }
            }
            // R2 Hold / EdgeOnly inside the zone: still after the settle
            foreach (var mode in modes)
                foreach (float pull in new[] { 1f, 2.5f })
                {
                    var cfg = S(mode, pull); var rows = Table(cfg, 60, 2f, t => new Vector2(10f, 4f));
                    var after = rows.Where(r => r.T > cfg.SettleSeconds + 1e-4f).ToList();
                    float moved = after.Max(r => Mathf.Abs(r.Yaw - after[0].Yaw) + Mathf.Abs(r.Pitch - after[0].Pitch));
                    c.Add("R2." + mode + "_holds_pull" + F(pull, "F1"), moved == 0f, "target 10 deg inside the zone: 0 deg of motion after the settle (moved " + F(moved, "G3") + ")");
                }
            // R3 settle: no step in speed on the draw-start frame, at rest afterwards
            {
                var cfg = S(LockOnDrawMode.Hold, 2.5f); var rows = Table(cfg, 144, 1.5f, t => new Vector2(15f, 0f), .5f);
                var sp = Speeds(rows, 144); int k = rows.FindIndex(r => r.T > .5f + 1e-4f);
                float step = Mathf.Abs(sp[k - 1] - sp[k - 2]);   // sp[k - 1] = the first draw frame, sp[k - 2] = the last frame before it
                c.Add("R3.settle_continuous", step < 1f && sp[sp.Length - 1] == 0f, "speed change on the draw-start frame " + F(step, "F2") + " deg/s (< 1), speed at the end " + F(sp[sp.Length - 1], "G3"));
            }
            // R4 EdgeOnly outside the zone: toward the zone edge, capped, acceleration limited, never past the centre
            foreach (int fps in new[] { 60, 144 })
            {
                var cfg = S(LockOnDrawMode.EdgeOnly, 1f); var rows = Table(cfg, fps, 3f, t => new Vector2(35f, 0f)); var sp = Speeds(rows, fps);
                float top = sp.Max(v => Mathf.Abs(v)), acc = 0f; for (int i = Mathf.CeilToInt(cfg.SettleSeconds * fps) + 2; i < sp.Length; i++) acc = Mathf.Max(acc, Mathf.Abs(sp[i] - sp[i - 1]) * fps);
                float left = Mathf.DeltaAngle(rows[rows.Count - 1].Yaw, 35f);
                c.Add("R4.edge_follow_fps" + fps, top <= cfg.EdgeMaxSpeed + 1e-3f && acc <= cfg.EdgeAcceleration * 1.02f && left > 0f && left <= cfg.AimKeepDegrees + .5f && sp.All(v => v >= -1e-4f),
                    "max speed " + F(top, "F2") + " <= " + F(cfg.EdgeMaxSpeed, "F0") + ", max acceleration " + F(acc, "F1") + " <= " + F(cfg.EdgeAcceleration, "F0") + ", final error " + F(left, "F2") + " deg (not past the centre), never turns away");
            }
            {
                var cfg = S(LockOnDrawMode.EdgeOnly, 1f); cfg.EdgeMaxSpeed = 10f;
                var sp = Speeds(Table(cfg, 60, 2f, t => new Vector2(80f, 0f)), 60); float top = sp.Skip(60).Max();
                c.Add("R4.cap", Mathf.Abs(top - 10f) < 1e-2f, "a target 80 deg away is followed at the cap (" + F(top) + " deg/s, cap 10)");
                cfg = S(LockOnDrawMode.EdgeOnly, 1f);
                var up = Table(cfg, 60, 2f, t => new Vector2(0f, -30f)); var down = Table(cfg, 60, 2f, t => new Vector2(0f, 30f));
                c.Add("R4.pitch_sign", up[up.Count - 1].Pitch < -5f && down[down.Count - 1].Pitch > 5f && up[up.Count - 1].Yaw == 0f,
                    "a target above turns the pitch up (" + F(up[up.Count - 1].Pitch, "F1") + "), below down (" + F(down[down.Count - 1].Pitch, "F1") + "); the yaw stays");
                cfg.AimKeepDegrees = 0f;
                var right = Table(cfg, 60, 2f, t => new Vector2(35f, 0f)); var leftRows = Table(cfg, 60, 2f, t => new Vector2(-35f, 0f));
                c.Add("R4.yaw_sign_viewport", right[right.Count - 1].Yaw > 5f && leftRows[leftRows.Count - 1].Yaw < -5f,
                    "aim keep off: right of the zone turns right (" + F(right[right.Count - 1].Yaw, "F1") + "), left of it left (" + F(leftRows[leftRows.Count - 1].Yaw, "F1") + ")");
            }
            // R5 aim keep / no camera / behind the camera
            {
                var cfg = S(LockOnDrawMode.EdgeOnly, 1f); cfg.EdgeMarginX = cfg.EdgeMarginY = .02f;
                var a = Table(cfg, 60, 2f, t => new Vector2(30f, 0f)); cfg.AimKeepDegrees = 0f;
                var b = Table(cfg, 60, 2f, t => new Vector2(30f, 0f)).Where(r => r.T > cfg.SettleSeconds + 1e-4f).ToList();
                c.Add("R5.aim_keep", a[a.Count - 1].Yaw > 5f && b[b.Count - 1].Yaw == b[0].Yaw, "bearing 30 deg inside a wide zone: with the aim keep the view turns (to " + F(a[a.Count - 1].Yaw, "F1") + "); with 0 it holds");
                cfg = S(LockOnDrawMode.EdgeOnly, 1f);
                var n = Table(cfg, 60, 2f, t => new Vector2(30f, 20f), viewport: false); var n0 = n.Where(r => r.T > .3f).ToList();
                c.Add("R5.no_viewport", n[n.Count - 1].Yaw > 5f && n0[n0.Count - 1].Pitch == n0[0].Pitch, "without a camera the yaw is kept by the aim limit (" + F(n[n.Count - 1].Yaw, "F1") + ") and the pitch holds");
                var behind = Table(cfg, 60, 3f, t => new Vector2(170f, 0f)); cfg.AimKeepDegrees = 0f; var behind0 = Table(cfg, 60, 3f, t => new Vector2(170f, 0f));
                c.Add("R5.behind", behind[behind.Count - 1].Yaw > 60f && behind0[behind0.Count - 1].Yaw > 60f, "a target behind the view is followed (yaw " + F(behind[behind.Count - 1].Yaw, "F1") + "; aim keep off " + F(behind0[behind0.Count - 1].Yaw, "F1") + ")");
            }
            // R6 after the draw: a pause, an eased return, then the legacy step; a running follow glides to a stop
            foreach (var mode in modes)
            {
                var cfg = S(mode, 2.5f); var rows = Table(cfg, 60, 4f, t => new Vector2(12f, 3f), 0f, 1f); var sp = Speeds(rows, 60);
                int end = rows.FindIndex(r => r.T > 1f + 1e-4f); float pause = 0f;
                // sp[end - 1] = the first frame after the draw
                for (int i = end - 1; i < end + Mathf.FloorToInt(cfg.ResumeDelay * 60f) - 2; i++) pause = Mathf.Max(pause, Mathf.Abs(sp[i]));
                float peak = 0f; for (int i = end - 1; i < sp.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(sp[i]));
                var late = rows.First(r => r.T > 1f + cfg.ResumeDelay + cfg.ResumeSeconds + .05f);
                var st = late.State; float y = late.Yaw, p = late.Pitch, ly = y, lp = p;
                var f = new LockOnDrawPull.Frame { HasYaw = true, Yaw = y, Pitch = p, TargetYaw = 12f, TargetPitch = 3f, DeltaTime = 1f / 60f };
                LockOnDrawPull.Step(ref st, in cfg, in f, out y, out p); Legacy(ref ly, ref lp, 12f, 3f, 2.5f, 1f / 60f);
                c.Add("R6." + mode + "_resume", pause == 0f && sp[end - 1] == 0f && peak < .6f * 2.5f * 12f && late.State.Ramp == 1f && y == ly && p == lp && Mathf.DeltaAngle(rows[rows.Count - 1].Yaw, 12f) < .5f,
                    "pause " + F(cfg.ResumeDelay, "F1") + " s with 0 motion, first frame 0 deg/s, peak " + F(peak, "F1") + " deg/s (an un-eased return would start at 30), then the legacy step exactly");
            }
            {
                var cfg = S(LockOnDrawMode.Hold, 2.5f); int fps = 240; var sp = Speeds(Table(cfg, fps, 3f, t => new Vector2(12f, 0f), 0f, 1f), fps);
                int k = -1; for (int i = fps + 1; i < sp.Length; i++) if (sp[i] > 0f) { k = i; break; }
                float straight = 2.5f * 12f * (1f / fps) / Mathf.Max(1e-4f, cfg.ResumeSeconds) * .5f;
                c.Add("R6.eased", k > 0 && sp[k] < .5f * straight, "first moving frame of the return " + (k > 0 ? F(sp[k], "F4") : "none") + " deg/s (a straight-line ramp would give " + F(straight, "F4") + ")");
                cfg = S(LockOnDrawMode.EdgeOnly, 1f); fps = 120; var rows = Table(cfg, fps, 1.4f, t => new Vector2(70f, 0f), 0f, 1f); sp = Speeds(rows, fps);
                // sp[fps - 1] = the first frame after the draw, sp[fps - 2] = the last drawing frame
                float steps = 0f; for (int i = fps - 1; i < sp.Length; i++) steps = Mathf.Max(steps, Mathf.Abs(sp[i] - sp[i - 1]));
                c.Add("R6.follow_glides_to_stop", sp[fps - 2] > 5f && steps <= cfg.EdgeAcceleration / fps * 1.05f && rows[rows.Count - 1].State.YawSpeed == 0f && sp[sp.Length - 1] == 0f && sp[fps - 1] > 0f && sp[fps - 1] < sp[fps - 2],
                    "follow speed " + F(sp[fps - 2], "F1") + " deg/s at the draw end falls by at most " + F(steps, "F2") + " deg/s per frame (limit " + F(cfg.EdgeAcceleration / fps, "F2") + ") to 0");
            }
            // R7 frame rate
            foreach (var mode in modes)
            {
                var ends = new List<float>();
                foreach (int fps in new[] { 30, 60, 144, 240 }) ends.Add(Table(S(mode, 2.5f), fps, 3f, t => new Vector2(14f, 3f), .5f, 1.5f).Last(r => r.T <= 2.5f + 1e-4f).Yaw);
                c.Add("R7." + mode + "_frame_rate", ends.Max() - ends.Min() <= .01f, "yaw at t = 2.5 s over 30 / 60 / 144 / 240 fps differs by " + F(ends.Max() - ends.Min(), "G3") + " deg (<= 0.01)");
            }
            {
                float a = Table(S(LockOnDrawMode.EdgeOnly, 1f), 60, 2f, t => new Vector2(20f + 25f * t, 0f)).Last().Yaw, b = Table(S(LockOnDrawMode.EdgeOnly, 1f), 144, 2f, t => new Vector2(20f + 25f * t, 0f)).Last().Yaw;
                c.Add("R7.edge_moving_frame_rate", Mathf.Abs(a - b) <= 1f, "a target drifting 25 deg/s: yaw after 2 s at 60 vs 144 fps differs by " + F(Mathf.Abs(a - b)) + " deg (<= 1)");
            }
            // R8 odd numbers, a hitch frame, lock taken inside a draw, pull 0
            {
                bool ok = true; string why = "";
                foreach (int variant in Enumerable.Range(0, 14))
                    foreach (LockOnDrawMode mode in Enum.GetValues(typeof(LockOnDrawMode)))
                    {
                        var cfg = S(mode, 2.5f);
                        switch (variant)
                        {
                            case 0: cfg.SettleSeconds = 0f; break; case 1: cfg.ResumeSeconds = cfg.ResumeDelay = 0f; break; case 2: cfg.EdgeAcceleration = 0f; break;
                            case 3: cfg.EdgeMaxSpeed = 0f; break; case 4: cfg.EdgeGain = 0f; break; case 5: cfg.EdgeMarginX = .5f; cfg.EdgeMarginY = .9f; break;
                            case 6: cfg.EdgeMarginX = -1f; break; case 7: cfg.AimKeepDegrees = 0f; break; case 8: cfg.Pull = 0f; break; case 9: cfg.Pull = 50f; break;
                            case 10: cfg.EdgeMaxSpeed = -5f; break; case 11: cfg.EdgeGain = -3f; break; case 12: cfg.EdgeAcceleration = -10f; break;
                            case 13: cfg.SettleSeconds = cfg.ResumeSeconds = cfg.ResumeDelay = -1f; break;
                        }
                        try
                        {
                            foreach (var r in Table(cfg, 60, 2f, t => new Vector2(40f * t, 10f), .3f, 1.2f))
                                if (float.IsNaN(r.Yaw) || float.IsInfinity(r.Yaw) || float.IsNaN(r.Pitch) || float.IsInfinity(r.Pitch)) { ok = false; why = "variant " + variant + " " + mode; }
                        }
                        catch (Exception e) { ok = false; why = "variant " + variant + " " + mode + ": " + e.Message; }
                    }
                c.Add("R8.degenerate_settings", ok, "zero / out-of-range values in every mode: finite output, no exception" + (why.Length > 0 ? " | " + why : ""));
                var zero = S(LockOnDrawMode.EdgeOnly, 1f); zero.EdgeAcceleration = 0f;
                var z = Table(zero, 60, 2f, t => new Vector2(60f, 0f)).Where(r => r.T > .3f).ToList();
                c.Add("R8.zero_accel_is_hold", z[z.Count - 1].Yaw == z[0].Yaw, "acceleration 0: no follow (the safe reading of a zero field)");
                float away = 0f;
                for (int variant = 0; variant < 3; variant++)
                {
                    var neg = S(LockOnDrawMode.EdgeOnly, 1f);
                    if (variant == 0) neg.EdgeMaxSpeed = -5f; else if (variant == 1) neg.EdgeGain = -3f; else neg.EdgeAcceleration = -10f;
                    var ng = Table(neg, 60, 2f, t => new Vector2(60f, 0f)).Where(r => r.T > .3f).ToList();
                    away = Mathf.Max(away, Mathf.Abs(ng[ng.Count - 1].Yaw - ng[0].Yaw));
                }
                c.Add("R8.negative_reads_as_zero", away == 0f, "a negative cap / gain / acceleration never turns the view (moved " + F(away, "G3") + " deg)");
                var cfg2 = S(LockOnDrawMode.EdgeOnly, 1f); var st = default(LockOnDrawPull.State); float y = 0f, p;
                float tanY = Mathf.Tan(26f * Mathf.Deg2Rad), tanX = tanY * 16f / 9f;
                LockOnDrawPull.Frame Edge(float yaw, float dt) => new LockOnDrawPull.Frame { Drawing = true, HasYaw = true, Yaw = yaw, TargetYaw = 70f, DeltaTime = dt, HasViewport = true, InFront = true, ViewportX = .99f, ViewportY = .5f, TanHalfFovX = tanX, TanHalfFovY = tanY };
                for (int i = 0; i < 60; i++) { var f = Edge(y, 1f / 60f); LockOnDrawPull.Step(ref st, in cfg2, in f, out y, out p); }
                float before = y; { var f = Edge(y, 1f); LockOnDrawPull.Step(ref st, in cfg2, in f, out y, out p); }
                c.Add("R8.hitch", y - before <= cfg2.EdgeMaxSpeed * LockOnDrawPull.MaxFollowStep + 1e-3f, "a 1 s frame moves the follow " + F(y - before, "F2") + " deg (<= cap x " + F(LockOnDrawPull.MaxFollowStep, "F1") + " s)");
                foreach (var mode in modes)
                {
                    var s2 = default(LockOnDrawPull.State); var cfg = S(mode, 2.5f);
                    var f = new LockOnDrawPull.Frame { Drawing = true, HasYaw = true, TargetYaw = 8f, TargetPitch = 2f, DeltaTime = 1f / 60f, HasViewport = true, InFront = true, ViewportX = .55f, ViewportY = .5f, TanHalfFovX = tanX, TanHalfFovY = tanY };
                    LockOnDrawPull.Step(ref s2, in cfg, in f, out float y2, out float p2);
                    c.Add("R8." + mode + "_lock_during_draw", y2 == 0f && p2 == 0f && s2.Ramp == 0f, "the first locked frame inside a draw does not move the view");
                }
                var off = S(LockOnDrawMode.EdgeOnly, 0f); var s3 = new LockOnDrawPull.State { Started = true, YawSpeed = 9f };
                var f3 = new LockOnDrawPull.Frame { Drawing = true, HasYaw = true, Yaw = 3f, Pitch = 4f, TargetYaw = 90f, TargetPitch = 9f, DeltaTime = 1f / 60f, HasViewport = true, InFront = true, ViewportX = .99f, ViewportY = .5f, TanHalfFovX = tanX, TanHalfFovY = tanY };
                LockOnDrawPull.Step(ref s3, in off, in f3, out float y3, out float p3);
                c.Add("R8.pull_zero", y3 == 3f && p3 == 4f && !s3.Started && s3.YawSpeed == 0f, "LockOnCameraPull 0: no motion in any mode (as before)");
            }
            // R9 the draw starts with the target OUTSIDE the zone: the settling pull and the edge follow must not add up
            foreach (int fps in new[] { 60, 144 })
            {
                var edge = S(LockOnDrawMode.EdgeOnly, 1f); var hold = S(LockOnDrawMode.Hold, 1f);
                var re = Table(edge, fps, 3f, t => new Vector2(45f, 0f), .5f); var rh = Table(hold, fps, 3f, t => new Vector2(45f, 0f), .5f);
                var se = Speeds(re, fps); var sh = Speeds(rh, fps); int k = re.FindIndex(r => r.T > .5f + 1e-4f);
                // se[k - 1] = the first draw frame, se[k - 2] = the last frame before it
                float atStart = Mathf.DeltaAngle(re[k - 1].Yaw, 45f), stepE = se[k - 1] - se[k - 2], stepH = sh[k - 1] - sh[k - 2], before = se[k - 2], top = 0f, decE = 0f, decH = 0f;
                for (int i = k - 1; i < se.Length; i++) top = Mathf.Max(top, se[i]);
                for (int i = k; i < se.Length; i++) { decE = Mathf.Max(decE, (se[i - 1] - se[i]) * fps); decH = Mathf.Max(decH, (sh[i - 1] - sh[i]) * fps); }
                float left = Mathf.DeltaAngle(re[re.Count - 1].Yaw, 45f);
                c.Add("R9.outside_at_draw_start_fps" + fps,
                    atStart > edge.AimKeepDegrees + 5f && Mathf.Abs(stepE) < 1f && Mathf.Abs(stepE - stepH) < 1f && top <= Mathf.Max(before, edge.EdgeMaxSpeed) + 1e-2f
                    && decE <= decH + edge.EdgeAcceleration * 1.05f && left > 0f && left <= edge.AimKeepDegrees + .5f && se.All(v => v >= -1e-3f),
                    "target " + F(atStart, "F1") + " deg off when the draw starts: speed step on that frame " + F(stepE, "F2") + " deg/s (Hold " + F(stepH, "F2") + "; both < 1), never faster than before the draw or the cap (max " +
                    F(top, "F2") + ", before " + F(before, "F2") + ", cap " + F(edge.EdgeMaxSpeed, "F0") + "), hardest slow-down " + F(decE, "F0") + " deg/s2 (<= Hold's settle " + F(decH, "F0") + " + the follow limit " +
                    F(edge.EdgeAcceleration, "F0") + "), ends at the zone edge (" + F(left, "F2") + " deg left)");
            }
        }

        // ---- config: which assets carry the new keys, and what a missing key reads as
        static void ConfigFieldsCheck(Checks c, CombatConfigSO d)
        {
            var so = new SerializedObject(d);
            bool present = ConfigFields.All(f => so.FindProperty(f) != null);
            c.Add("C1.fields_declared", present, "CombatConfigSO declares " + ConfigFields.Length + " lock-on drawing fields: " + string.Join(", ", ConfigFields.Where(f => so.FindProperty(f) == null).DefaultIfEmpty("all present")));
            c.Add("C2.class_defaults", d.LockOnDrawMode == LockOnDrawMode.EdgeOnly && (int)LockOnDrawMode.EdgeOnly == 0 && d.LockOnDrawSettleSeconds > 0f && d.LockOnDrawResumeSeconds > 0f
                && d.LockOnDrawEdgeMargin.x > 0f && d.LockOnDrawEdgeMargin.x < LockOnDrawPull.MaxMargin && d.LockOnDrawEdgeMargin.y > 0f && d.LockOnDrawEdgeMargin.y < LockOnDrawPull.MaxMargin
                && d.LockOnDrawEdgeGain > 0f && d.LockOnDrawEdgeMaxSpeed > 0f && d.LockOnDrawEdgeAcceleration > 0f && d.LockOnDrawAimKeepDegrees > 0f && d.LockOnDrawAimKeepDegrees < d.AreaConeAngle,
                "class defaults: mode " + d.LockOnDrawMode + " (enum value 0 = " + (LockOnDrawMode)0 + "), settle " + F(d.LockOnDrawSettleSeconds, "F2") + " s, resume " + F(d.LockOnDrawResumeDelay, "F2") + " + " + F(d.LockOnDrawResumeSeconds, "F2") +
                " s, margin " + d.LockOnDrawEdgeMargin.ToString("F2") + ", gain " + F(d.LockOnDrawEdgeGain, "F1") + ", cap " + F(d.LockOnDrawEdgeMaxSpeed, "F0") + " deg/s, accel " + F(d.LockOnDrawEdgeAcceleration, "F0") + " deg/s2, aim keep " +
                F(d.LockOnDrawAimKeepDegrees, "F0") + " deg (< cone half angle " + F(d.AreaConeAngle, "F0") + ")");
            string[] guids = AssetDatabase.FindAssets("t:CombatConfigSO"); int assets = 0, withKeys = 0, wrong = 0; var odd = new List<string>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid); var asset = AssetDatabase.LoadAssetAtPath<CombatConfigSO>(path);
                if (asset == null) continue;
                assets++;
                string yaml = ""; try { yaml = File.ReadAllText(Harness303.Abs(path)); } catch { }
                int inYaml = ConfigFields.Count(f => yaml.Contains("\n  " + f + ":"));
                if (inYaml > 0) withKeys++;
                // a key that is not in the YAML must read as the class default (the field initialiser)
                bool same = true;
                if (!yaml.Contains("\n  _lockOnDrawMode:")) same &= asset.LockOnDrawMode == d.LockOnDrawMode;
                if (!yaml.Contains("\n  _lockOnDrawSettleSeconds:")) same &= asset.LockOnDrawSettleSeconds == d.LockOnDrawSettleSeconds;
                if (!yaml.Contains("\n  _lockOnDrawResumeDelay:")) same &= asset.LockOnDrawResumeDelay == d.LockOnDrawResumeDelay;
                if (!yaml.Contains("\n  _lockOnDrawResumeSeconds:")) same &= asset.LockOnDrawResumeSeconds == d.LockOnDrawResumeSeconds;
                if (!yaml.Contains("\n  _lockOnDrawEdgeMargin:")) same &= asset.LockOnDrawEdgeMargin == d.LockOnDrawEdgeMargin;
                if (!yaml.Contains("\n  _lockOnDrawEdgeGain:")) same &= asset.LockOnDrawEdgeGain == d.LockOnDrawEdgeGain;
                if (!yaml.Contains("\n  _lockOnDrawEdgeMaxSpeed:")) same &= asset.LockOnDrawEdgeMaxSpeed == d.LockOnDrawEdgeMaxSpeed;
                if (!yaml.Contains("\n  _lockOnDrawEdgeAcceleration:")) same &= asset.LockOnDrawEdgeAcceleration == d.LockOnDrawEdgeAcceleration;
                if (!yaml.Contains("\n  _lockOnDrawAimKeepDegrees:")) same &= asset.LockOnDrawAimKeepDegrees == d.LockOnDrawAimKeepDegrees;
                if (!same) { wrong++; odd.Add(path); }
                if (inYaml > 0 || !same) c.Info("config " + path + ": " + inYaml + "/" + ConfigFields.Length + " keys in the YAML, mode " + asset.LockOnDrawMode + ", pull " + F(asset.LockOnCameraPull, "F2"));
            }
            c.Add("C3.missing_key_reads_default", wrong == 0 && assets > 0, assets + " CombatConfigSO asset(s), " + withKeys + " carry at least one of the new keys; every key that is not in an asset's YAML reads as the class default" +
                (wrong > 0 ? " EXCEPT " + string.Join(", ", odd) : ""));
        }

        // ---- execution order the analysis relies on
        // How the order is read (check 수리 2026-10-05). MonoImporter.GetExecutionOrder returns the order SAVED for a script: the
        // executionOrder line of its .meta, written by Project Settings > Script Execution Order. It does not carry
        // [DefaultExecutionOrder]: in Unity 6000.3.9f1 it read 0 for PlayerMotor (attribute -100), BrushStrokeFeedAdapter (-1000)
        // and the two near-arm scripts (1000), and the first version of E1 - E3 compared that 0 with the attribute (3 false FAILs).
        // Unity's rule: a saved order other than 0 wins; without one the attribute is the script's order; without either, 0.
        // So the order a script runs at = saved != 0 ? saved : attribute, and E1 fails when a saved order contradicts an attribute.
        static int MetaOrder(string assetPath)
        {
            try
            {
                var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(Harness303.Abs(assetPath) + ".meta"), @"^\s*executionOrder:\s*(-?\d+)", System.Text.RegularExpressions.RegexOptions.Multiline);
                return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;   // a .meta without the line = 0
            }
            catch { return int.MinValue; }
        }

        static void ExecutionOrder(Checks c)
        {
            int Attr(Type t) { var a = t.GetCustomAttribute<DefaultExecutionOrder>(); return a != null ? a.order : 0; }
            var scripts = MonoImporter.GetAllRuntimeMonoScripts();
            MonoScript Script(Type t) { foreach (var s in scripts) if (s != null && s.GetClass() == t) return s; return null; }
            var types = new[] { typeof(PlayerMotor), typeof(CameraRigController), typeof(BrushStrokeFeedAdapter), typeof(BrushStrokeRenderer), typeof(WorldMacroPlayerGestureRig), typeof(PlayerVisualDriver), typeof(DrawingInputController) };
            var order = new Dictionary<Type, int>(); var text = new List<string>(); bool allFound = true, noContradiction = true;
            foreach (var t in types)
            {
                var script = Script(t); int attr = Attr(t), saved = script != null ? MonoImporter.GetExecutionOrder(script) : 0;
                int meta = script != null ? MetaOrder(AssetDatabase.GetAssetPath(script)) : 0;
                allFound &= script != null; noContradiction &= saved == 0 || saved == attr;
                order[t] = saved != 0 ? saved : attr;
                text.Add(t.Name + " " + order[t] + " (attribute " + attr + ", saved " + saved + (meta != saved ? ", .meta text " + (meta == int.MinValue ? "unreadable" : meta.ToString(CultureInfo.InvariantCulture)) : "") + (script == null ? ", SCRIPT NOT FOUND" : "") + ")");
            }
            c.Add("E1.orders", allFound && noContradiction, "the order each script runs at (a saved order other than 0 wins, otherwise the attribute); no saved order contradicts an attribute: " + string.Join(", ", text));
            c.Add("E2.update_chain", order[typeof(PlayerMotor)] == -100 && order[typeof(PlayerMotor)] < order[typeof(CameraRigController)],
                "Update: PlayerMotor (" + order[typeof(PlayerMotor)] + ", the pull) before CameraRigController (" + order[typeof(CameraRigController)] + ", the camera pose)");
            c.Add("E3.lateupdate_chain", order[typeof(BrushStrokeFeedAdapter)] == -1000 && order[typeof(BrushStrokeFeedAdapter)] < order[typeof(BrushStrokeRenderer)]
                && order[typeof(BrushStrokeRenderer)] < order[typeof(WorldMacroPlayerGestureRig)] && order[typeof(BrushStrokeRenderer)] < order[typeof(PlayerVisualDriver)],
                "LateUpdate: stroke adapter (" + order[typeof(BrushStrokeFeedAdapter)] + ") < ribbon (" + order[typeof(BrushStrokeRenderer)] + ") < near arm (" + order[typeof(WorldMacroPlayerGestureRig)] + " / " + order[typeof(PlayerVisualDriver)] + ")");
            c.Info("order: these numbers are read (attribute by reflection, saved order from the importer), not observed running - Edit Mode cannot watch a frame. The stroke anchor itself needs only 'every Update before every LateUpdate' (Unity's frame loop, whatever the numbers are)");
            var adapter = typeof(BrushStrokeFeedAdapter);
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            c.Add("E4.render_anchor_present", adapter.GetMethod("OnBeginCameraRendering308", any) != null && adapter.GetMethod("AnchorStrokeFrame", any) != null && adapter.GetMethod("SetRenderAnchor308", any) != null
                && adapter.GetMethod("UpdateStrokeFrame", any) != null && typeof(PlayerMotor).GetField("LockOnDrawModeOverride308") != null,
                "the adapter has the render-time anchor next to the LateUpdate one; the motor has the harness mode override");
            var statics = typeof(LockOnDrawPull).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Where(f => !f.IsLiteral && !f.IsInitOnly).Select(f => f.Name).ToList();
            c.Add("E5.no_static_state", statics.Count == 0, "LockOnDrawPull keeps no static state (domain reload is off): " + (statics.Count == 0 ? "none" : string.Join(", ", statics)));
            bool mapping = adapter.GetMethod("ScreenToWorldPrecise308", BindingFlags.Public | BindingFlags.Static) != null && adapter.GetMethod("ScreenToViewPoint308", BindingFlags.Public | BindingFlags.Static) != null;
            c.Add("E6.precise_mapping_present", mapping, mapping ? "the adapter maps screen to world without the engine's inverse view-projection matrix (ScreenToWorldPrecise308)"
                : "the loaded BrushStrokeFeedAdapter has no ScreenToWorldPrecise308: this is the adapter from before the check 수리 (the Stage308_juice recopy carries the fixed file) - A3b will fail");
        }

        // ---- recognition cannot be reached from the code this stage adds
        // Two readings (check 수리 2026-10-05). "uses" = what the compiled assembly references: the compiler drops a reference that no
        // source uses, so Oheangbu.BrushRender (asmdef: Core; its sources use nothing of Core) lists none - the first version of B3
        // asked for exactly [Core] and failed on the empty list. "may use" = what the assembly definition allows (the border the
        // compiler enforces, from CompilationPipeline). A forbidden name in either reading fails.
        static void AssemblyBorders(Checks c)
        {
            string[] Uses(Type t) => t.Assembly.GetReferencedAssemblies().Select(a => a.Name).Where(n => n.StartsWith("Oheangbu.", StringComparison.Ordinal)).OrderBy(n => n).ToArray();
            var pipeline = UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Editor);
            string[] May(Type t, out bool found)
            {
                string name = t.Assembly.GetName().Name; var definition = pipeline.FirstOrDefault(x => x.name == name); found = definition != null;
                return definition == null ? new string[0] : definition.assemblyReferences.Select(x => x.name).Where(n => n.StartsWith("Oheangbu.", StringComparison.Ordinal)).OrderBy(n => n).ToArray();
            }
            string List(string[] names) => names.Length == 0 ? "(none)" : string.Join(", ", names);
            bool Clean(string[] names, params string[] forbidden) => !names.Any(n => forbidden.Contains(n));
            string[] drawing = Uses(typeof(DrawingInputController)), combat = Uses(typeof(LockOnDrawPull)), brush = Uses(typeof(BrushStrokeRenderer));
            string[] drawingMay = May(typeof(DrawingInputController), out bool drawingFound), combatMay = May(typeof(LockOnDrawPull), out bool combatFound), brushMay = May(typeof(BrushStrokeRenderer), out bool brushFound);
            c.Add("B1.drawing_sees_no_presentation", drawingFound && Clean(drawing, "Oheangbu.Combat", "Oheangbu.App", "Oheangbu.BrushRender", "Oheangbu.Presentation") && Clean(drawingMay, "Oheangbu.Combat", "Oheangbu.App", "Oheangbu.BrushRender", "Oheangbu.Presentation"),
                "Oheangbu.Drawing (input + recogniser) uses: " + List(drawing) + "; may use (assembly definition" + (drawingFound ? "" : " NOT FOUND") + "): " + List(drawingMay) + " - it cannot read the motor, the config or the stroke adapter");
            c.Add("B2.combat_sees_no_drawing", combatFound && Clean(combat, "Oheangbu.Drawing", "Oheangbu.App", "Oheangbu.BrushRender") && Clean(combatMay, "Oheangbu.Drawing", "Oheangbu.App", "Oheangbu.BrushRender"),
                "Oheangbu.Combat (the pull rule, the motor) uses: " + List(combat) + "; may use (assembly definition" + (combatFound ? "" : " NOT FOUND") + "): " + List(combatMay) + " - it cannot write a stroke sample");
            c.Add("B3.brush_sees_core_only", brushFound && brush.All(n => n == "Oheangbu.Core") && brushMay.All(n => n == "Oheangbu.Core"),
                "Oheangbu.BrushRender uses: " + List(brush) + "; may use (assembly definition" + (brushFound ? "" : " NOT FOUND") + "): " + List(brushMay) + " - nothing of this project but Core in either reading");
        }

        // ---- the render-time anchor gives the same transform as the LateUpdate one, and a stroke point stays on its input pixel
        static int LeftoverFixtures() => Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g != null && g.name.StartsWith(FixturePrefix, StringComparison.Ordinal));

        static string CleanupFixtures()
        {
            int n = 0;
            foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g != null && g.name.StartsWith(FixturePrefix, StringComparison.Ordinal)).ToArray()) { Object.DestroyImmediate(g); n++; }
            foreach (var t in Resources.FindObjectsOfTypeAll<RenderTexture>().Where(t => t != null && t.name.StartsWith(FixturePrefix, StringComparison.Ordinal)).ToArray()) { t.Release(); Object.DestroyImmediate(t); n++; }
            return "fixtures:cleanup removed " + n + " object(s) named " + FixturePrefix + "*";
        }

        static GameObject FixtureObject(string name, Scene preview, Transform parent = null)
        {
            var go = new GameObject(FixturePrefix + "_" + name) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(go, preview);
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        // ---- where a world point is on the camera's screen, without the engine's 4x4 world matrices (check 수리 2026-10-05)
        // Camera.WorldToScreenPoint multiplies by a single-precision view-projection matrix whose translation carries the world
        // coordinates: at the main scene's coordinates (3 km from the origin) that alone reads up to about 0.7 px off, and its
        // inverse, Camera.ScreenToWorldPoint, several px (control A5: the first A3 verdict, 10.4 px, was this). The verdicts of
        // this file do not go through either. world - camera position is the difference of two nearby floats (exact); the
        // rotation into the view and the projection are done in double precision from the camera's own rotation and matrix.
        internal static void PreciseScreen(Camera cam, Vector3 world, out double sx, out double sy)
        {
            Transform t = cam.transform; Vector3 o = t.position; Quaternion q = t.rotation;
            double dx = (double)world.x - o.x, dy = (double)world.y - o.y, dz = (double)world.z - o.z;
            double qx = q.x, qy = q.y, qz = q.z, qw = q.w, n = Math.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
            if (n < 1e-12) { sx = sy = double.NaN; return; }
            qx /= n; qy /= n; qz /= n; qw /= n;
            // the camera's right / up / forward axes = the columns of its rotation matrix
            double rx = 1 - 2 * (qy * qy + qz * qz), ry = 2 * (qx * qy + qw * qz), rz = 2 * (qx * qz - qw * qy);
            double ux = 2 * (qx * qy - qw * qz), uy = 1 - 2 * (qx * qx + qz * qz), uz = 2 * (qy * qz + qw * qx);
            double fx = 2 * (qx * qz + qw * qy), fy = 2 * (qy * qz - qw * qx), fz = 1 - 2 * (qx * qx + qy * qy);
            double x = dx * rx + dy * ry + dz * rz, y = dx * ux + dy * uy + dz * uz, z = -(dx * fx + dy * fy + dz * fz);   // view space, z towards the viewer
            Matrix4x4 p = cam.projectionMatrix; Rect r = cam.pixelRect;
            double cx = p.m00 * x + p.m01 * y + p.m02 * z + p.m03, cy = p.m10 * x + p.m11 * y + p.m12 * z + p.m13, cw = p.m30 * x + p.m31 * y + p.m32 * z + p.m33;
            sx = r.x + (cx / cw + 1.0) * .5 * r.width; sy = r.y + (cy / cw + 1.0) * .5 * r.height;
        }

        internal static Vector2 PreciseScreen(Camera cam, Vector3 world) { PreciseScreen(cam, world, out double sx, out double sy); return new Vector2((float)sx, (float)sy); }

        internal static double PixelOff(Camera cam, Vector3 world, Vector2 pixel)
        {
            PreciseScreen(cam, world, out double sx, out double sy); double ex = sx - pixel.x, ey = sy - pixel.y;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        sealed class PixelStats
        {
            public double Worst, SumSq; public int Count;
            public void Add(double e) { if (e > Worst) Worst = e; SumSq += e * e; Count++; }
            public double Rms => Count > 0 ? Math.Sqrt(SumSq / Count) : 0.0;
        }

        static string D(double v, string f = "F3") => v.ToString(f, CultureInfo.InvariantCulture);

        // one unit in the last place of a float (the spacing to the next float above |v|)
        static float Ulp(float v)
        {
            v = Mathf.Abs(v); if (float.IsNaN(v) || float.IsInfinity(v)) return float.NaN;
            return BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(v), 0) + 1), 0) - v;
        }

        // A5 control only: the stroke frame the way the adapter placed it before the check 수리 - through the engine's Camera.ScreenToWorldPoint
        static void AnchorByEngineApi(Transform stroke, float unitsPerPixel, Camera camera, float depth)
        {
            float scale = 2f * depth * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad) / Mathf.Max(1, camera.pixelHeight) / Mathf.Max(1e-7f, unitsPerPixel);
            stroke.SetPositionAndRotation(camera.ScreenToWorldPoint(new Vector3(0f, 0f, depth)), camera.transform.rotation);
            stroke.localScale = new Vector3(scale, scale, scale);   // every parent in the fixture has unit scale
        }

        // limits of the anchor fixture, with where each comes from (none of them is a taste number)
        const double OriginPixelLimit = .05;     // at the origin a float is 1e-7 m: the formula itself is what is measured (it reads about 0.002 px)
        const double MainPixelMaxLimit = 1.0;    // SPEC-LOCKON-DRAW-STABILITY-308 AC-1 (max). One float step of a world coordinate at 3 km is 0.24 mm =
        const double MainPixelRmsLimit = .5;     //   0.25 px at the drawing plane and the point passes four such steps: AC-1 (rms)
        const float ScaleUlpLimit = 2f;          // the two anchors differ by one rounding (a method return): measured 1 unit in the last place
        const double EngineApiSeenLimit = 2.0;   // A5: the engine's inverse mapping must read at least this far off at the main coordinates,
        const double EngineApiOriginLimit = .2;  //   and less than this at the origin - otherwise the 2026-10-05 diagnosis is not reproduced
        const double SameMeaningLimit = .05;     // A6: within 2 m of the origin the engine function is good to about 0.003 px; a half-pixel or sign convention error is >= 0.5 px

        static void AnchorFixture(Checks c)
        {
            Scene preview = default; RenderTexture rt = null;
            try
            {
                preview = EditorSceneManager.NewPreviewScene();
                var pivot = FixtureObject("Pivot", preview);
                var camObject = FixtureObject("Camera", preview, pivot.transform);
                var cam = camObject.AddComponent<Camera>(); cam.enabled = false; cam.nearClipPlane = .08f; cam.farClipPlane = 22000f;   // the main camera's planes
                rt = new RenderTexture(1920, 1080, 0) { name = FixturePrefix + "_RT", hideFlags = HideFlags.HideAndDontSave }; cam.targetTexture = rt;
                var rig = FixtureObject("DrawingRig", preview, camObject.transform);
                var adapter = rig.AddComponent<BrushStrokeFeedAdapter>();   // edit mode: OnEnable does not run, nothing subscribes
                var a = FixtureObject("StrokeA", preview, rig.transform).transform; var b = FixtureObject("StrokeB", preview, rig.transform).transform;
                var e = FixtureObject("StrokeEngineApi", preview, rig.transform).transform;
                bool inPreview = new[] { pivot, camObject, rig, a.gameObject, b.gameObject, e.gameObject }.All(g => g.scene == preview);
                const BindingFlags inst = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(BrushStrokeFeedAdapter);
                var update = type.GetMethod("UpdateStrokeFrame", inst); var units = type.GetMethod("WorldUnitsPerPixel", inst);
                type.GetField("_eyeAnchor", inst).SetValue(adapter, pivot.transform);
                type.GetField("_projectionCamera", inst).SetValue(adapter, cam);   // the adapter's own mapping then uses the fixture camera, not Camera.main
                float surface = (float)type.GetField("_surfaceDistance", inst).GetValue(adapter);
                var rnd = new System.Random(308); float worstPos = 0f, worstScale = 0f, worstScaleUlp = 0f, worstRot = 0f; double control = 0; int poses = 0;
                var origins = new[] { Vector3.zero, new Vector3(3277f, 180f, 2252f) };   // the origin, and world coordinates like the main scene's
                var ours = new[] { new PixelStats(), new PixelStats() }; var engineMap = new[] { new PixelStats(), new PixelStats() }; var engineRead = new[] { new PixelStats(), new PixelStats() };
                for (int o = 0; o < origins.Length; o++)
                    for (int i = 0; i < 40; i++)
                    {
                        pivot.transform.SetPositionAndRotation(origins[o] + new Vector3((float)rnd.NextDouble() * 20f, 1.55f, (float)rnd.NextDouble() * 20f), Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f));
                        pivot.transform.rotation *= Quaternion.Euler((float)rnd.NextDouble() * 95f - 35f, 0f, 0f);
                        camObject.transform.localPosition = new Vector3(.35f, .25f, -.2f) * (float)(.3 + rnd.NextDouble() * .7);
                        cam.fieldOfView = 52f;
                        // a stroke begun on this frame, with the adapter's own functions: fixed units per pixel (ApplyStrokeStarted), the frame
                        // (UpdateStrokeFrame), and a point stored the way ApplyStrokePointAdded stores it (its mapping, then the frame's inverse)
                        float depth0 = adapter.EffectiveSurfaceDistance(cam);
                        float upp = (float)units.Invoke(adapter, new object[] { cam });
                        var pixel = new Vector2((float)rnd.NextDouble() * 1920f, (float)rnd.NextDouble() * 1080f);
                        update.Invoke(adapter, new object[] { a, upp, cam });
                        adapter.TryProjectScreenPoint(pixel, out Vector3 inkWorld);
                        Vector3 local = a.InverseTransformPoint(inkWorld);
                        // A5 control: the same stroke and point through the engine's inverse mapping (the adapter before the check 수리)
                        Vector3 engineWorld = cam.ScreenToWorldPoint(new Vector3(pixel.x, pixel.y, depth0));
                        AnchorByEngineApi(e, upp, cam, depth0);
                        Vector3 localEngine = e.InverseTransformPoint(engineWorld);
                        // later: the rig moved the camera, then something changed pose and field of view before the render
                        pivot.transform.Rotate(0f, (float)rnd.NextDouble() * 12f - 6f, 0f, Space.World);
                        camObject.transform.localPosition = new Vector3(.35f, .25f, -.2f) * (float)(.3 + rnd.NextDouble() * .7);
                        update.Invoke(adapter, new object[] { a, upp, cam });                                    // LateUpdate anchor
                        ours[o].Add(PixelOff(cam, a.TransformPoint(local), pixel));
                        AnchorByEngineApi(e, upp, cam, adapter.EffectiveSurfaceDistance(cam));
                        engineMap[o].Add(PixelOff(cam, e.TransformPoint(localEngine), pixel));
                        cam.fieldOfView = 52f + 1.2f; camObject.transform.position += cam.transform.right * .045f;   // render-only breath + shake
                        control = Math.Max(control, PixelOff(cam, a.TransformPoint(local), pixel));               // without the render-time anchor
                        update.Invoke(adapter, new object[] { a, upp, cam });
                        BrushStrokeFeedAdapter.AnchorStrokeFrame(b, upp, cam, surface, pivot.transform);          // render-time anchor
                        worstPos = Mathf.Max(worstPos, Vector3.Distance(a.position, b.position));
                        Quaternion qa = a.rotation, qb = b.rotation;   // compared component by component: Quaternion.Angle reads 0 for anything under 0.16 deg
                        worstRot = Mathf.Max(worstRot, Mathf.Abs(qa.x - qb.x), Mathf.Abs(qa.y - qb.y), Mathf.Abs(qa.z - qb.z), Mathf.Abs(qa.w - qb.w));
                        Vector3 sa = a.localScale, sb = b.localScale;
                        worstScale = Mathf.Max(worstScale, (sa - sb).magnitude);
                        worstScaleUlp = Mathf.Max(worstScaleUlp, Mathf.Abs(sa.x - sb.x) / Ulp(sa.x), Mathf.Abs(sa.y - sb.y) / Ulp(sa.y), Mathf.Abs(sa.z - sb.z) / Ulp(sa.z));
                        Vector3 drawn = b.TransformPoint(local);
                        ours[o].Add(PixelOff(cam, drawn, pixel));
                        engineRead[o].Add(Vector2.Distance(cam.WorldToScreenPoint(drawn), pixel));               // what the first A3 verdict read (not a verdict now)
                        AnchorByEngineApi(e, upp, cam, adapter.EffectiveSurfaceDistance(cam));
                        engineMap[o].Add(PixelOff(cam, e.TransformPoint(localEngine), pixel));
                        poses++;
                    }
                // A6: the adapter's mapping against the engine function within 2 m of the origin, where the engine function is good to about 0.003 px
                double sameMeaning = 0; var near0 = new System.Random(310);
                for (int i = 0; i < 24; i++)
                {
                    pivot.transform.SetPositionAndRotation(new Vector3((float)near0.NextDouble(), 1.55f, (float)near0.NextDouble()), Quaternion.Euler((float)near0.NextDouble() * 95f - 35f, (float)near0.NextDouble() * 360f, 0f));
                    camObject.transform.localPosition = new Vector3(.35f, .25f, -.2f) * (float)(.3 + near0.NextDouble() * .7);
                    cam.fieldOfView = 52f;
                    var pixel = new Vector2((float)near0.NextDouble() * 1920f, (float)near0.NextDouble() * 1080f);
                    float depth = adapter.EffectiveSurfaceDistance(cam), upp = (float)units.Invoke(adapter, new object[] { cam });
                    adapter.TryProjectScreenPoint(pixel, out Vector3 mine);
                    sameMeaning = Math.Max(sameMeaning, Vector3.Distance(mine, cam.ScreenToWorldPoint(new Vector3(pixel.x, pixel.y, depth))) / (double)upp);   // px at the drawing plane
                }
                c.Add("A1.fixture_in_preview_scene", inPreview && cam.pixelHeight == 1080, "fixture objects live in a preview scene (HideAndDontSave), camera target 1920 x " + cam.pixelHeight);
                c.Add("A2.render_anchor_equals_lateupdate", worstPos == 0f && worstRot == 0f && worstScaleUlp <= ScaleUlpLimit, poses + " poses: AnchorStrokeFrame and UpdateStrokeFrame give the same position (" + F(worstPos, "G3") + " m) and rotation (quaternion components differ by " + F(worstRot, "G3") +
                    "); scale within " + F(worstScaleUlp, "F1") + " unit(s) in the last place of a float (<= " + F(ScaleUlpLimit, "F0") + "; difference " + F(worstScale, "G3") + " = " + D(worstScale * 2203.0, "G2") + " px at the far corner of the screen)");
                c.Add("A3a.point_on_input_pixel_origin", ours[0].Worst <= OriginPixelLimit, "at the origin a stroke point stays on its input pixel after the camera moves and after a render-only +1.2 deg / 4.5 cm change: worst " + D(ours[0].Worst, "F4") +
                    " px, rms " + D(ours[0].Rms, "F4") + " (<= " + D(OriginPixelLimit, "F2") + ": the formula)");
                c.Add("A3b.point_on_input_pixel_main_coords", ours[1].Worst <= MainPixelMaxLimit && ours[1].Rms <= MainPixelRmsLimit, "at the main scene's coordinates " + origins[1].ToString("F0") + " the same: worst " + D(ours[1].Worst) + " px, rms " + D(ours[1].Rms) +
                    " (AC-1: <= " + D(MainPixelMaxLimit, "F1") + " and <= " + D(MainPixelRmsLimit, "F1") + "; one float step of a world coordinate there is 0.25 px)");
                c.Add("A4.control_sees_drift", control > 3.0, "control: with only the LateUpdate anchor a render-only field-of-view change moves the same point " + D(control, "F1") + " px (the camera child follows the 4.5 cm shake, not the breath)");
                c.Add("A5.control_sees_engine_inverse", engineMap[1].Worst > EngineApiSeenLimit && engineMap[0].Worst < EngineApiOriginLimit, "control: the same strokes placed through Camera.ScreenToWorldPoint (the adapter before the check 수리) sit worst " + D(engineMap[1].Worst, "F2") +
                    " px, rms " + D(engineMap[1].Rms, "F2") + " off their pixels at the main scene's coordinates (> " + D(EngineApiSeenLimit, "F0") + " expected: the glyph shake) and " + D(engineMap[0].Worst, "F3") + " px at the origin (< " + D(EngineApiOriginLimit, "F1") + ")");
                c.Add("A6.mapping_means_the_same", sameMeaning <= SameMeaningLimit, "within 2 m of the origin, where the engine function is accurate, the adapter's mapping and Camera.ScreenToWorldPoint give the same point: worst " + D(sameMeaning, "F4") + " px apart over 24 poses (<= " + D(SameMeaningLimit, "F2") + ")");
                c.Info("anchor: Camera.WorldToScreenPoint reads the drawn points " + D(engineRead[0].Worst) + " px (origin) / " + D(engineRead[1].Worst) + " px (main coordinates) off their pixels - the function the first A3 verdict used; the verdicts above use a double-precision projection instead");
                AnchorOtherProjections(c, cam, pivot.transform, camObject.transform);
            }
            finally
            {
                if (preview.IsValid())
                {
                    foreach (var root in preview.GetRootGameObjects()) Object.DestroyImmediate(root);
                    EditorSceneManager.ClosePreviewScene(preview);
                }
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            }
        }

        // INFO only (no verdict): the general solve of ScreenToViewPoint308 against the engine function for an orthographic camera and
        // for a shifted lens, near the origin where the engine function is accurate. The main camera is a plain perspective camera.
        static void AnchorOtherProjections(Checks c, Camera cam, Transform pivot, Transform camTransform)
        {
            var map = typeof(BrushStrokeFeedAdapter).GetMethod("ScreenToWorldPrecise308", BindingFlags.Public | BindingFlags.Static);
            if (map == null) { c.Info("anchor: other projections not measured (the loaded adapter has no ScreenToWorldPrecise308)"); return; }
            try
            {
                pivot.SetPositionAndRotation(new Vector3(4f, 1.55f, 7f), Quaternion.Euler(12f, 140f, 0f)); camTransform.localPosition = new Vector3(.2f, .1f, -.15f);
                double WorstMillimetres()
                {
                    double worst = 0; var rnd = new System.Random(309);
                    for (int i = 0; i < 24; i++)
                    {
                        float sx = (float)rnd.NextDouble() * 1920f, sy = (float)rnd.NextDouble() * 1080f, depth = 1f + (float)rnd.NextDouble();
                        Vector3 ours = (Vector3)map.Invoke(null, new object[] { cam, sx, sy, depth }), engine = cam.ScreenToWorldPoint(new Vector3(sx, sy, depth));
                        worst = Math.Max(worst, Vector3.Distance(ours, engine) * 1000.0);
                    }
                    return worst;
                }
                cam.orthographic = true; cam.orthographicSize = 1.2f; double ortho = WorstMillimetres();
                cam.orthographic = false; cam.fieldOfView = 52f; cam.usePhysicalProperties = true; cam.lensShift = new Vector2(.15f, -.1f); double shifted = WorstMillimetres();
                cam.usePhysicalProperties = false; cam.lensShift = Vector2.zero; cam.fieldOfView = 52f;
                c.Info("anchor: the adapter's mapping against Camera.ScreenToWorldPoint near the origin, 24 points at 1 - 2 m: orthographic " + D(ortho, "F3") + " mm apart, lens shift (0.15, -0.10) " + D(shifted, "F3") +
                    " mm apart (1 mm is about 1 px at 1 m; not a verdict - the main camera uses neither)");
            }
            catch (Exception e) { c.Info("anchor: other projections not measured (" + e.GetType().Name + ": " + e.Message + ")"); }
        }

        // ---- the open scene (read only): what the measurement and the analysis assume about the rig
        static void SceneWiring(Checks c, Scene scene)
        {
            var motors = Object.FindObjectsByType<PlayerMotor>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(m => m.gameObject.scene == scene).ToArray();
            if (motors.Length == 0) { c.Info("scene wiring: no PlayerMotor in the open scene (" + scene.path + ") - skipped"); return; }
            foreach (var motor in motors)
            {
                var config = Harness303.Field<CombatConfigSO>(motor, "_config"); var pivot = Harness303.Field<Transform>(motor, "_cameraPivot");
                var camera = pivot != null ? pivot.GetComponentInChildren<Camera>(true) : null;
                var adapter = camera != null ? camera.GetComponentInChildren<BrushStrokeFeedAdapter>(true) : null;
                if (adapter == null) adapter = Object.FindObjectsByType<BrushStrokeFeedAdapter>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(x => x.gameObject.scene == scene);
                string who = Harness303.PathOf(motor.transform);
                c.Add("W1.motor_refs:" + who, config != null && pivot != null && camera != null && Harness303.Field<LockOn>(motor, "_lockOn") != null,
                    "config " + (config != null ? AssetDatabase.GetAssetPath(config) + " (pull " + F(config.LockOnCameraPull, "F2") + ", draw mode " + config.LockOnDrawMode + ")" : "MISSING") +
                    ", pivot " + (pivot != null ? pivot.name : "MISSING") + ", camera under the pivot " + (camera != null ? camera.name : "MISSING"));
                if (adapter == null) { c.Info("scene wiring: no BrushStrokeFeedAdapter for " + who); continue; }
                var eye = Harness303.Field<Transform>(adapter, "_eyeAnchor"); var projection = Harness303.Field<Camera>(adapter, "_projectionCamera");
                bool child = camera != null && adapter.transform.IsChildOf(camera.transform);
                c.Add("W2.strokes_are_camera_children:" + who, child && eye == pivot && (projection == null || projection == camera) && (camera == null || camera.CompareTag("MainCamera")),
                    "adapter " + Harness303.PathOf(adapter.transform) + (child ? " is under the camera (live strokes follow every later camera change)" : " is NOT under the camera") + ", eye anchor " + (eye != null ? eye.name : "none") +
                    ", projection camera " + (projection != null ? projection.name : "unset = Camera.main") + ", camera tag " + (camera != null ? camera.tag : "?"));
                var style = Harness303.Field<BrushStyleSO>(adapter, "_style");
                c.Info("stroke style " + (style != null ? AssetDatabase.GetAssetPath(style) + " | stroke layer " + style.StrokeRenderLayer + " (" + (style.StrokeRenderLayer < 0 ? "unchanged" : LayerMask.LayerToName(style.StrokeRenderLayer)) + ")" : "none") +
                    " | adapter object layer " + LayerMask.LayerToName(adapter.gameObject.layer));
            }
        }
    }
}
