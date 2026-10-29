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
    ///   D  the numbers of the riposte after D308-148: total 45 as ONE hit, 67.5 inside the window (the old 48-shot volley is gone)
    ///   S1 SingleHitDamage    one DamageResolved of 45 (67.5 inside the window; the window's 1.5 is EnemyVitals', applied once)
    ///   S2 WindowBTiming      the window is judged at the call (open +100 s / closing exactly now / closed before): 67.5 / 45 / 45
    ///   S3 WindowGeneration + LifeRevision   a late hit meets the NEW window; a closed-by-key old key closes nothing; a reborn / dead target takes nothing
    ///   S4 PathPiercing       the REAL mesh (RibbonMeshBuilder, no smoothing): vertex-pair midpoints = the samples, the centre line passes the torso
    ///   S5 PoolReuse + LookVolumeLifecycle   the pool is warmed once and reused; the riposte's own volume (saturation only) rises, falls and is gone
    /// The pure rules (gesture decision, candidate, window claim, stroke geometry / clock / hit latch / hit stop) run in
    /// Tools/SpellVFX120/Pure308 (Cases_Riposte308, Cases_RiposteStroke308).
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
            try { S1(r); } catch (Exception e) { Check(r, "S1", false, "EXCEPTION " + e); }
            try { S2(r); } catch (Exception e) { Check(r, "S2", false, "EXCEPTION " + e); }
            try { S3(r); } catch (Exception e) { Check(r, "S3", false, "EXCEPTION " + e); }
            try { S4(r); } catch (Exception e) { Check(r, "S4", false, "EXCEPTION " + e); }
            try { S5(r); } catch (Exception e) { Check(r, "S5", false, "EXCEPTION " + e); }
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

        // ---------------------------------------------------------------- D: the numbers of the riposte after D308-148

        static void D(Report r)
        {
            var profile = Resources.Load<Riposte301Profile>("Riposte301Profile");
            if (profile == null) { Check(r, "D1", false, "Resources/Riposte301Profile not found"); return; }
            // D308-148: ONE hit of TotalDamage; GlyphCount (48 in the asset) is LEGACY and read by nobody
            Check(r, "D1", Mathf.Approximately(profile.TotalDamage, 45f), "the one hit is " + profile.TotalDamage + " (45 expected; GlyphCount " + profile.GlyphCount + " is legacy and unused)");
            float window = 1.5f;
            foreach (var guid in AssetDatabase.FindAssets("t:CombatConfigSO"))
            {
                var config = AssetDatabase.LoadAssetAtPath<CombatConfigSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (config != null) { window = config.BlossomDamageMultiplier; break; }
            }
            Check(r, "D2", Mathf.Abs(profile.TotalDamage * window - 67.5f) < 1e-2f, "inside the window the one hit is " + (profile.TotalDamage * window).ToString("0.###") + " (x" + window + ")");
            Check(r, "D3", profile.DeferPresentationOnRiposteCandidate && profile.ConsumesWindow, "the A/B switch DeferPresentationOnRiposteCandidate defaults to on (plan C); ConsumesWindow unchanged");
            Check(r, "D4", profile.Stroke != null && profile.Stroke.Ready, "Riposte301Profile.Stroke is set and has its materials (RiposteStroke308Build ran): " + (profile.Stroke == null ? "EMPTY - the riposte is not offered" : profile.Stroke.Ready ? "ready" : "no materials"));
        }

        // ---------------------------------------------------------------- S: the one stroke (D308-148)

        static EnemyVitals NewEnemy(string name, float hp, out GameObject go)
        {
            go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            var v = go.AddComponent<EnemyVitals>();
            Set(v, "_hp", hp);   // Awake does not run in edit mode: alive by hand
            return v;
        }

        // S1 SingleHitDamage: the impact is one TakeDamage of 45; inside the window EnemyVitals' own 1.5 makes it 67.5 - once
        static void S1(Report r)
        {
            var host = new GameObject("Riposte308Check_S1_Host") { hideFlags = HideFlags.HideAndDontSave };
            var v = NewEnemy("Riposte308Check_S1_Enemy", 1000f, out var enemy);
            try
            {
                var riposte = host.AddComponent<Riposte301>();
                int hits = 0; float applied = 0f;
                v.DamageResolved += result => { hits++; applied += result.AppliedDamage; };
                bool ok = riposte.ApplyImpact308(v, v.LifeRevision, 45f, out var outside);
                Check(r, "S1a", ok && hits == 1 && Mathf.Approximately(applied, 45f) && outside.Attack.Melee, "no window: one hit of " + applied + " (45), melee provenance " + outside.Attack.Melee);
                v.OpenWeakPoint(); hits = 0; applied = 0f;
                ok = riposte.ApplyImpact308(v, v.LifeRevision, 45f, out _);
                Check(r, "S1b", ok && hits == 1 && Mathf.Abs(applied - 45f * v.DamageMultiplier) < 1e-3f && Mathf.Abs(applied - 67.5f) < 1e-3f, "inside the window: one hit of " + applied + " (67.5 = 45 x " + v.DamageMultiplier + ", never x1.5 twice)");
                Check(r, "S1c", !riposte.ApplyImpact308(v, v.LifeRevision, 0f, out _) && !riposte.ApplyImpact308(null, 0, 45f, out _) && hits == 1, "a zero amount or no target hits nothing");
            }
            finally { Object.DestroyImmediate(host); Object.DestroyImmediate(enemy); }
        }

        // S2 WindowBTiming: B is judged at the call (the window as it is at the pierce frame), never fixed at the fire
        static void S2(Report r)
        {
            var host = new GameObject("Riposte308Check_S2_Host") { hideFlags = HideFlags.HideAndDontSave };
            var v = NewEnemy("Riposte308Check_S2_Enemy", 100000f, out var enemy);
            try
            {
                var riposte = host.AddComponent<Riposte301>();
                float Hit(float until)
                {
                    v.ResetCombatState(); v.OpenWeakPoint();              // opened at "the fire": the key is taken then
                    Set(v, "_weakPointUntil", until);                     // ... and the window is what it is when the head pierces
                    float before = v.Hp; riposte.ApplyImpact308(v, v.LifeRevision, 45f, out _);
                    return before - v.Hp;
                }
                float inside = Hit(Time.time + 100f), at = Hit(Time.time), after = Hit(Time.time - 1f);
                Check(r, "S2a", Mathf.Abs(inside - 67.5f) < 1e-3f, "window open at the pierce: " + inside + " (67.5)");
                Check(r, "S2b", Mathf.Abs(at - 45f) < 1e-3f, "window closing exactly at the pierce frame: " + at + " (45: the window is Time.time < until)");
                Check(r, "S2c", Mathf.Abs(after - 45f) < 1e-3f, "window closed before the pierce though it was open at the fire: " + after + " (45: nothing is fixed at the fire)");
            }
            finally { Object.DestroyImmediate(host); Object.DestroyImmediate(enemy); }
        }

        // S3 WindowGeneration + LifeRevision: a late stroke meets the window that is open NOW; the key of the old one closes nothing; another life takes nothing
        static void S3(Report r)
        {
            var host = new GameObject("Riposte308Check_S3_Host") { hideFlags = HideFlags.HideAndDontSave };
            var v = NewEnemy("Riposte308Check_S3_Enemy", 100000f, out var enemy);
            try
            {
                var riposte = host.AddComponent<Riposte301>();
                v.OpenWeakPoint();
                uint life = v.LifeRevision, first = v.WeakPointGeneration;
                Set(v, "_weakPointUntil", Time.time - 1f);   // the first window ran out before the head arrived ...
                v.ExpireWeakPoint(Time.time);
                v.OpenWeakPoint();                             // ... and the next one opened
                uint second = v.WeakPointGeneration;
                float before = v.Hp; bool ok = riposte.ApplyImpact308(v, life, 45f, out _); float dealt = before - v.Hp;
                Check(r, "S3a", second == first + 1 && ok && Mathf.Abs(dealt - 67.5f) < 1e-3f, "a late pierce meets the new window (generation " + first + " -> " + second + "): " + dealt + " (judged at the pierce)");
                bool lateClose = v.TryConsumeWeakPointWindow(life, first);
                Check(r, "S3b", !lateClose && v.WeakPointActive && v.WeakPointGeneration == second, "closing by the OLD key changes nothing: the new window stays open");
                bool rightClose = v.TryConsumeWeakPointWindow(life, second);
                Check(r, "S3c", rightClose && !v.WeakPointActive, "the right key closes it once");
                v.Restore();   // a new life
                before = v.Hp; ok = riposte.ApplyImpact308(v, life, 45f, out _);
                Check(r, "S3d", !ok && Mathf.Approximately(before, v.Hp), "a stroke fired in the old life hits nothing after the enemy was reborn (LifeRevision " + life + " -> " + v.LifeRevision + ")");
                Set(v, "_hp", 0f);
                Check(r, "S3e", !riposte.ApplyImpact308(v, v.LifeRevision, 45f, out _), "a dead enemy takes nothing");
            }
            finally { Object.DestroyImmediate(host); Object.DestroyImmediate(enemy); }
        }

        static Material DummyMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/InternalErrorShader");
            return new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        static RiposteStroke308Profile DummyProfile(int pool)
        {
            var p = ScriptableObject.CreateInstance<RiposteStroke308Profile>();
            p.hideFlags = HideFlags.HideAndDontSave; p.PoolSize = pool;
            p.StrokeMaterial = DummyMaterial(); p.FlakeMaterial = DummyMaterial();
            return p;
        }

        static void DestroyProfile(RiposteStroke308Profile p)
        {
            if (p == null) return;
            if (p.StrokeMaterial != null) Object.DestroyImmediate(p.StrokeMaterial);
            if (p.FlakeMaterial != null) Object.DestroyImmediate(p.FlakeMaterial);
            Object.DestroyImmediate(p);
        }

        // S4 PathPiercing on the REAL mesh: no smoothing, no caps, so the vertex pairs' midpoints are the samples; the centre line passes the torso;
        // a target that moved before the pierce is pierced where it is, and the part already written stayed where it was
        static void S4(Report r)
        {
            var profile = DummyProfile(2);
            var effect = new RiposteStroke308Effect();
            var rootObject = new GameObject("Riposte308Check_S4_Target") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                Check(r, "S4a", effect.Warm(null, profile, 0), "the effect warms with a profile that has its materials");
                var aim = new RiposteStroke308Aim { Caster = Vector3.zero, Eye = new Vector3(0f, 1.6f, 0f), Torso = new Vector3(0f, 1.2f, 6f), BrushTip = new Vector3(.3f, 1.2f, .8f), HasBrushTip = true, TorsoRadius = .4f };
                rootObject.transform.position = new Vector3(0f, 0f, 6f);
                var slot = effect.Begin(aim, rootObject.transform, null);
                var mesh = slot.StrokeMesh.Mesh; var path = slot.Path;
                var vertices = mesh.vertices;
                bool pairs = vertices.Length == path.Count * 2; float worst = 0f;
                for (int i = 0; pairs && i < path.Count; i++) worst = Mathf.Max(worst, Vector3.Distance((vertices[2 * i] + vertices[2 * i + 1]) * .5f, path.Pos[i]));
                Check(r, "S4b", pairs && worst < 1e-4f, "the mesh is the samples: " + vertices.Length + " vertices for " + path.Count + " samples, vertex-pair midpoints off by at most " + worst.ToString("0.#####") + " m (no smoothing, no caps)");
                Vector3 WorldMid() { var f = mesh.vertices; int m = path.MidIndex; return slot.StrokeMesh.Transform.TransformPoint((f[2 * m] + f[2 * m + 1]) * .5f); }
                float centre = Vector3.Distance(WorldMid(), aim.Torso);
                Check(r, "S4c", centre < 1e-3f && path.DistanceToCentreline(aim.Torso) < 1e-3f, "the mesh centre line passes the torso mark (" + centre.ToString("0.#####") + " m)");
                float e = Vector3.Distance(path.SampleWorld(path.EntryIndex), aim.Torso), x = Vector3.Distance(path.SampleWorld(path.ExitIndex), aim.Torso);
                Check(r, "S4d", e >= .4f - 1e-3f && x >= .4f - 1e-3f && path.EntryU < path.ImpactU && path.ImpactU < path.ExitU, "it enters and leaves the body (" + e.ToString("0.00") + " / " + x.ToString("0.00") + " m from the torso mark, entry < mid < exit)");
                Vector3 startBefore = path.SampleWorld(0);
                effect.Tick(.02f);                                                // prep: nothing written yet
                rootObject.transform.position += new Vector3(.3f, 0f, .2f);       // the target stepped aside
                Vector3 moved = aim.Torso + new Vector3(.3f, 0f, .2f);
                effect.Tick(5f);                                                  // one huge frame: the head skips over the body
                Check(r, "S4e", slot.ImpactReady && slot.Impact.Fired && Vector3.Distance(WorldMid(), moved) < 1e-3f && path.DistanceToCentreline(moved) < 1e-3f,
                    "a target that moved before the pierce is pierced where it is (mid sample off by " + Vector3.Distance(WorldMid(), moved).ToString("0.#####") + " m)");
                Check(r, "S4f", Vector3.Distance(path.SampleWorld(0), startBefore) < 1e-4f, "the written start stayed where it was written");
            }
            finally { effect.Dispose(true); Object.DestroyImmediate(rootObject); DestroyProfile(profile); }
        }

        // S5 PoolReuse + LookVolumeLifecycle
        static void S5(Report r)
        {
            var profile = DummyProfile(2);
            var effect = new RiposteStroke308Effect();
            try
            {
                effect.Warm(null, profile, 0);
                var vol = effect.LookVolume; var volObject = vol != null ? vol.gameObject : null; var flakes = effect.Flakes;
                var colour = effect.LookColour; var smh = effect.LookHighlights;
                Check(r, "S5a", vol != null && !vol.enabled && vol.weight == 0f && vol.isGlobal && Mathf.Approximately(vol.priority, profile.VolumePriority) && (volObject.hideFlags & HideFlags.HideAndDontSave) == HideFlags.HideAndDontSave,
                    "the riposte's own volume: global, hidden, priority " + profile.VolumePriority + ", off and weight 0 until a stroke runs");
                Check(r, "S5b", colour != null && colour.saturation.overrideState && Mathf.Approximately(colour.saturation.value, -profile.Desaturate) && !colour.postExposure.overrideState && !colour.contrast.overrideState && smh != null && smh.highlights.overrideState && !smh.shadows.overrideState && !smh.midtones.overrideState,
                    "saturation (" + (colour != null ? colour.saturation.value : 0f) + ") and the highlights are overridden; exposure and contrast are not");
                Check(r, "S5c", flakes != null && !flakes.emission.enabled && flakes.main.maxParticles == profile.MaxParticles && flakes.main.simulationSpace == ParticleSystemSimulationSpace.World && flakes.GetComponent<ParticleSystemRenderer>().sharedMaterial == profile.FlakeMaterial,
                    "one flake particle system: manual emission only, world space, the flake material, " + profile.MaxParticles + " particles");
                var aim = new RiposteStroke308Aim { Caster = Vector3.zero, Eye = new Vector3(0f, 1.6f, 0f), Torso = new Vector3(0f, 1.2f, 6f), TorsoRadius = .4f };
                int children = effect.Root.childCount;
                var ids = new System.Collections.Generic.HashSet<int>();
                for (int i = 0; i < effect.Capacity; i++) ids.Add(effect.SlotAt(i).StrokeMesh.Mesh.GetInstanceID());
                var a = effect.Begin(aim, null, null); var b = effect.Begin(aim, null, null); var c = effect.Begin(aim, null, null);
                bool reused = ReferenceEquals(c, a) && effect.Root.childCount == children && effect.Capacity == 2 && effect.LiveCount == 2 && ids.Contains(c.StrokeMesh.Mesh.GetInstanceID());
                Check(r, "S5d", reused, "three fires on a pool of two: the oldest slot is reused, no object or mesh is made (children " + children + " -> " + effect.Root.childCount + ")");
                effect.CancelAll();
                var s = effect.Begin(aim, null, null);
                float t = s.PredictedImpactT, wRise = 0f;
                effect.Tick(Mathf.Max(0f, t - .02f)); wRise = effect.LookWeight;
                bool enabledRise = vol.enabled;
                effect.Tick(.021f + .02f); float wPeak = effect.LookWeight;                       // just after the pierce
                effect.Tick(.5f); float wEnd = effect.LookWeight;                                 // past the return
                Check(r, "S5e", wRise > 0f && wRise < 1f && enabledRise && wPeak > .99f && wEnd == 0f && !vol.enabled && vol.weight == 0f,
                    "the drain rises before the pierce (" + wRise.ToString("0.00") + "), peaks (" + wPeak.ToString("0.00") + ") and is gone and off after the return (" + wEnd + ")");
                var s2 = effect.Begin(aim, null, null); effect.Tick(s2.PredictedImpactT + .01f);
                bool on = vol.enabled && vol.weight > 0f;
                effect.CancelAll();
                Check(r, "S5f", on && !vol.enabled && vol.weight == 0f && effect.LiveCount == 0, "a cancel puts the volume to weight 0 and off at once");
                bool splashOk = true;
                try { effect.Begin(aim, null, null); effect.Splash(effect.SlotAt(0)); } catch (Exception e) { splashOk = false; Check(r, "S5g", false, "Splash / Emit threw " + e.Message); }
                if (splashOk) Check(r, "S5g", true, "the specks emit without error");
                effect.Dispose(true);
                Check(r, "S5h", vol == null && volObject == null && flakes == null && !effect.IsReady, "disposing the effect destroys its volume, particle system and meshes");
            }
            finally { effect.Dispose(true); DestroyProfile(profile); }
        }
    }
}
