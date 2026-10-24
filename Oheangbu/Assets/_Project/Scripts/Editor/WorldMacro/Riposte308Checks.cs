using System;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Drawing;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#308 D308-147 step 3 (debate 32, plan C): edit-mode checks of the riposte input split. Everything runs in memory on scene
    /// objects it creates and destroys; no asset is saved, no key is pressed, no Play is needed. Queue:
    /// Oheangbu.EditorTools.WorldMacro.Riposte308Checks Run "all".
    ///   V  EnemyVitals: the window generation and the narrow window close (TryConsumeWeakPointWindow)
    ///   B  DrawingInputController: presentation events, slow-down ownership, hold start, the stroke-less release (reflection on the
    ///      private EnterMode / HandleStroke(bool, bool) / OnDrawKeyReleased, like DrawPause308Checks; the Input System is not involved)
    ///   D  regression of the volley's numbers (this step changes none): 48 shots, total 45, 67.5 inside the window
    /// The pure rules (gesture decision, candidate, window claim) run in Tools/SpellVFX120/Pure308 (Cases_Riposte308).
    /// Not covered here (Play): the real Input System frame order, Riposte301 against a live walker / lock-on, the camera and arm look.</summary>
    public static class Riposte308Checks
    {
        [Serializable] sealed class Report { public int pass, fail; public List<string> lines = new List<string>(); }

        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "all") throw new ArgumentException("riposte308-checks: all");
            var r = new Report();
            try { V(r); } catch (Exception e) { Check(r, "V", false, "EXCEPTION " + e); }
            try { B(r); } catch (Exception e) { Check(r, "B", false, "EXCEPTION " + e); }
            try { D(r); } catch (Exception e) { Check(r, "D", false, "EXCEPTION " + e); }
            return JsonUtility.ToJson(r, true);
        }

        static void Check(Report r, string id, bool ok, string detail)
        {
            if (ok) r.pass++; else r.fail++;
            r.lines.Add((ok ? "PASS " : "FAIL ") + id + " " + detail);
        }

        static void Set(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) throw new MissingFieldException(target.GetType().Name, field);
            f.SetValue(target, value);
        }

        static object Get(object target, string field)
        {
            var f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) throw new MissingFieldException(target.GetType().Name, field);
            return f.GetValue(target);
        }

        // ---------------------------------------------------------------- V: EnemyVitals window generation and the narrow close

        static void V(Report r)
        {
            var go = new GameObject("Riposte308Check_Enemy") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var v = go.AddComponent<EnemyVitals>();
                Set(v, "_hp", 40f);   // Awake does not run in edit mode: alive by hand
                bool start = v.WeakPointGeneration == 0 && !v.WeakPointActive;
                v.OpenWeakPoint();
                uint g1 = v.WeakPointGeneration, life = v.LifeRevision;
                v.OpenWeakPoint();   // already open: the same window, no new opening
                Check(r, "V1", start && g1 == 1 && v.WeakPointGeneration == 1 && v.WeakPointActive, "OpenWeakPoint counts openings: 0 -> 1, a second call while it is open is the same window");
                bool wrongGen = v.TryConsumeWeakPointWindow(life, g1 + 1), wrongLife = v.TryConsumeWeakPointWindow(life + 1, g1);
                Check(r, "V2", !wrongGen && !wrongLife && v.WeakPointActive, "a wrong generation or a wrong life closes nothing");
                bool closed = v.TryConsumeWeakPointWindow(life, g1);
                bool again = v.TryConsumeWeakPointWindow(life, g1);
                Check(r, "V3", closed && !again && !v.WeakPointActive && Mathf.Approximately(v.DamageMultiplier, 1f), "the right key closes the window once (multiplier back to 1); a second call is refused");
                v.OpenWeakPoint();
                uint g2 = v.WeakPointGeneration;
                bool late = v.TryConsumeWeakPointWindow(life, g1);   // the first volley finishes late
                Check(r, "V4", g2 == g1 + 1 && !late && v.WeakPointActive, "a late finish of window " + g1 + " does not close the next window " + g2);
                v.ResetCombatState();
                v.OpenWeakPoint();
                Check(r, "V5", v.WeakPointGeneration == g2 + 1, "a full reset then a new opening is a new generation (" + v.WeakPointGeneration + ")");
                Set(v, "_hp", 0f);
                Check(r, "V6", !v.TryConsumeWeakPointWindow(life, v.WeakPointGeneration), "a dead enemy has nothing to close");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---------------------------------------------------------------- B: the drawing input against a hand-made arbiter

        sealed class FakeArbiter : IDrawPressArbiter
        {
            public DrawPressMode Mode;
            public int Began, Chosen, Released, Cancelled;
            public bool ReleaseSawInDrawMode;
            public DrawingInputController Drawing;
            public DrawPressMode BeginCandidate() { Began++; return Mode; }
            public void OnStrokeChosen() { Chosen++; }
            public bool OnReleasedWithoutStroke() { Released++; ReleaseSawInDrawMode = Drawing != null && Drawing.InDrawMode; return true; }
            public void CancelCandidate() { Cancelled++; }
        }

        sealed class Counts
        {
            public int Mode, ModeOut, Present, PresentOut, Committed, StrokeStart, StrokeAfterPresent;
        }

        static void B(Report r)
        {
            var go = new GameObject("Riposte308Check_Drawing") { hideFlags = HideFlags.HideAndDontSave };
            float scale = Time.timeScale;
            try
            {
                Time.timeScale = 1f;
                var d = go.AddComponent<DrawingInputController>();
                Set(d, "_drawTimeScale", .5f);
                var arbiter = new FakeArbiter { Drawing = d };
                d.PressArbiter = arbiter;
                var c = new Counts();
                d.ModeEntered += () => c.Mode++;
                d.ModeExited += () => c.ModeOut++;
                d.PresentationEntered += () => c.Present++;
                d.PresentationExited += () => c.PresentOut++;
                d.Committed += _ => c.Committed++;
                d.StrokeStarted += () => { c.StrokeStart++; if (c.Present > 0) c.StrokeAfterPresent++; };

                var enter = typeof(DrawingInputController).GetMethod("EnterMode", BindingFlags.Instance | BindingFlags.NonPublic);
                var stroke = typeof(DrawingInputController).GetMethod("HandleStroke", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(bool), typeof(bool) }, null);
                var release = typeof(DrawingInputController).GetMethod("OnDrawKeyReleased", BindingFlags.Instance | BindingFlags.NonPublic);
                var pointer = typeof(DrawingInputController).GetProperty("PointerScreenPosition", BindingFlags.Instance | BindingFlags.Public);
                if (enter == null || stroke == null || release == null || pointer == null) { Check(r, "B0", false, "EnterMode / HandleStroke(bool,bool) / OnDrawKeyReleased / PointerScreenPosition not found"); return; }
                void Enter() => enter.Invoke(d, null);
                void Stroke(bool pressed, bool released = false) => stroke.Invoke(d, new object[] { pressed, released });
                void Release() => release.Invoke(d, null);
                void Point(float x, float y) => pointer.GetSetMethod(true).Invoke(d, new object[] { new Vector2(x, y) });
                void Reset()
                {
                    if (d.InDrawMode) d.CancelForUi();   // leaves any mode a step left open (its events are counted away below)
                    c.Mode = c.ModeOut = c.Present = c.PresentOut = c.Committed = c.StrokeStart = c.StrokeAfterPresent = 0;
                    arbiter.Began = arbiter.Chosen = arbiter.Released = arbiter.Cancelled = 0; arbiter.ReleaseSawInDrawMode = false;
                    Time.timeScale = 1f;
                }

                // B1: a deferred candidate is in the draw mode but has no presentation: no event, no slow-down, no hold start
                arbiter.Mode = DrawPressMode.Deferred; Reset();
                Enter();
                Check(r, "B1", d.InDrawMode && !d.InDrawPresentation && c.Mode == 1 && c.Present == 0 && arbiter.Began == 1 && Mathf.Approximately(Time.timeScale, 1f) && (float)Get(d, "_modeEnterTime") == 0f,
                    "candidate press: mode on, presentation 0, slow-down not taken, hold time not started (mode " + c.Mode + ", presentation " + c.Present + ", scale " + Time.timeScale + ")");

                // B2: the first stroke starts the presentation once, before StrokeStarted, keeps the first point, takes the slow-down, starts the hold time
                Point(10f, 10f);
                Stroke(true);
                Check(r, "B2", c.Present == 1 && c.StrokeStart == 1 && c.StrokeAfterPresent == 1 && arbiter.Chosen == 1 && d.InDrawPresentation && d.HasStartedStroke && d.TotalPointCount == 1 &&
                    Mathf.Approximately(Time.timeScale, .5f) && Mathf.Approximately((float)Get(d, "_modeEnterTime"), Time.unscaledTime),
                    "first stroke: presentation 1 before StrokeStarted, draw chosen 1, first point kept (" + d.TotalPointCount + "), scale " + Time.timeScale + ", hold time = the stroke's time");
                Point(30f, 30f); Stroke(true);
                Check(r, "B2", c.Present == 1 && arbiter.Chosen == 1 && d.TotalPointCount == 2, "more points or strokes of the gesture start no second presentation and choose nothing again");

                // B3: a hit clears the strokes, not the record of the gesture
                d.InterruptLetter();
                Check(r, "B3", d.HasStartedStroke && d.StrokeCount == 0 && d.InDrawMode, "InterruptLetter clears the strokes, keeps HasStartedStroke and the mode");

                // B4: the release of a gesture that drew is the commit, never the arbiter's
                Stroke(false, true);
                Release();
                Check(r, "B4", arbiter.Released == 0 && arbiter.Cancelled == 0 && !d.InDrawMode && c.PresentOut == 1 && c.ModeOut == 1 && Mathf.Approximately(Time.timeScale, 1f) && !d.HasStartedStroke,
                    "stroke then release: the commit path (too short = quiet), the arbiter hears nothing, presentation 1 in / 1 out, scale back to 1");

                // B5: a candidate released with no stroke: left quietly first, then the arbiter; no commit, no presentation, no slow-down
                arbiter.Mode = DrawPressMode.Deferred; Reset();
                Enter();
                Release();
                Check(r, "B5", arbiter.Released == 1 && !arbiter.ReleaseSawInDrawMode && !d.InDrawMode && c.Committed == 0 && c.Present == 0 && c.PresentOut == 0 && c.ModeOut == 1 && arbiter.Cancelled == 0 && Mathf.Approximately(Time.timeScale, 1f),
                    "release without a stroke: arbiter asked once after the mode was left, no Committed, presentation never began and never ended, no cancel");

                // B6: a candidate cancelled (a menu, a shut gate, a disable)
                Reset(); arbiter.Mode = DrawPressMode.Deferred;
                Enter(); d.CancelForUi();
                Check(r, "B6", arbiter.Cancelled == 1 && arbiter.Released == 0 && !d.InDrawMode && c.PresentOut == 0, "cancelled candidate: the arbiter is told to cancel once, nothing is released");

                // B7: not a candidate: the presentation is on the press, a stroke chooses nothing
                Reset(); arbiter.Mode = DrawPressMode.None;
                Enter();
                bool onPress = c.Present == 1 && d.InDrawPresentation && Mathf.Approximately(Time.timeScale, .5f);
                Point(10f, 10f); Stroke(true);
                bool noChoice = arbiter.Chosen == 0 && c.Present == 1;
                Stroke(false, true); Release();
                Check(r, "B7", onPress && noChoice && c.PresentOut == 1 && Mathf.Approximately(Time.timeScale, 1f), "ordinary press: presentation 1 on the press, a stroke chooses nothing, one end");

                // B8: the early-presentation switch: presentation on the press, the stroke-less release still goes to the arbiter
                Reset(); arbiter.Mode = DrawPressMode.Immediate;
                Enter();
                bool early = c.Present == 1 && Mathf.Approximately(Time.timeScale, .5f);
                Release();
                Check(r, "B8", early && arbiter.Released == 1 && c.PresentOut == 1 && Mathf.Approximately(Time.timeScale, 1f) && c.Committed == 0,
                    "profile switch off: presentation on the press, the release still reaches the arbiter, the presentation ends with the mode");

                // B9: no arbiter at all: today's behaviour
                Reset(); d.PressArbiter = null;
                Enter();
                Check(r, "B9", c.Present == 1 && c.Mode == 1 && d.InDrawPresentation, "without an arbiter every press is an ordinary draw with its presentation on the press");
                d.CancelForUi();

                // B10: an arbiter that throws is an ordinary press (recognition is never cut short)
                Reset(); d.PressArbiter = new ThrowingArbiter();
                bool logging = Debug.unityLogger.logEnabled; Debug.unityLogger.logEnabled = false;
                try { Enter(); } finally { Debug.unityLogger.logEnabled = logging; }
                Check(r, "B10", d.InDrawMode && c.Present == 1, "an arbiter that throws at the press: an ordinary draw");
                d.CancelForUi();
            }
            finally { Time.timeScale = scale; Object.DestroyImmediate(go); }
        }

        sealed class ThrowingArbiter : IDrawPressArbiter
        {
            public DrawPressMode BeginCandidate() { throw new InvalidOperationException("Riposte308Checks: the arbiter fails"); }
            public void OnStrokeChosen() { }
            public bool OnReleasedWithoutStroke() { return false; }
            public void CancelCandidate() { }
        }

        // ---------------------------------------------------------------- D: the volley's numbers did not move in this step

        static void D(Report r)
        {
            var profile = Resources.Load<Riposte301Profile>("Riposte301Profile");
            if (profile == null) { Check(r, "D1", false, "Resources/Riposte301Profile not found"); return; }
            int n = Mathf.Max(1, profile.GlyphCount);
            float each = profile.TotalDamage / n;
            Check(r, "D1", n == 48 && Mathf.Approximately(profile.TotalDamage, 45f) && Mathf.Abs(each * n - 45f) < 1e-3f, "48 shots, total 45 (each " + each.ToString("0.0000") + ", sum " + (each * n).ToString("0.###") + ")");
            float window = 1.5f;
            foreach (var guid in AssetDatabase.FindAssets("t:CombatConfigSO"))
            {
                var config = AssetDatabase.LoadAssetAtPath<CombatConfigSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (config != null) { window = config.BlossomDamageMultiplier; break; }
            }
            Check(r, "D2", Mathf.Abs(each * n * window - 67.5f) < 1e-2f, "inside the window the whole volley is " + (each * n * window).ToString("0.###") + " (x" + window + ")");
            Check(r, "D3", profile.DeferPresentationOnRiposteCandidate && profile.ConsumesWindow, "the A/B switch DeferPresentationOnRiposteCandidate defaults to on (plan C); ConsumesWindow unchanged");
        }
    }
}
