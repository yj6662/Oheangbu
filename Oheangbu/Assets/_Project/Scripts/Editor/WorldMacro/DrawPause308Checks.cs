using System;
using System.Reflection;
using System.Text;
using Oheangbu.App.World.UI;
using Oheangbu.Drawing;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 the drawing slow-down against the pause (user report 2026-10-08: a tutorial card came up while drawing and the game went on
    // in the slow-down). Run inside a held Play (Map307Capture play-hold). Drives the real PauseCoordinator and the player's real
    // DrawingInputController; the draw mode is entered through its own private EnterMode (no input synthesis). Leaves the scale at 1.
    //   check   C1 drawing, then pause with the coordinator's Drawing reference cleared (a stale binding): paused at 0, resumes at 1
    //           C2 a draw-key press while paused: not entered, the world stays at 0
    //           C3 drawing entered during a temporary scale (a hit stop): leaving it does not restore the temporary scale
    //           C4 leaving twice in a row writes the scale once
    public static class DrawPause308Checks
    {
        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "check") return "REFUSED check";
            if (!EditorApplication.isPlaying) return "REFUSED Play only (Map307Capture play-hold first)";
            var ui = UnityEngine.Object.FindFirstObjectByType<PlaytestUiRoot>(); var pause = ui != null ? ui.Pause : null; var drawing = pause != null ? pause.Drawing : null;
            if (pause == null || drawing == null) return "REFUSED no PauseCoordinator / DrawingInputController in this Play";
            if (pause.Depth > 0 || drawing.InDrawMode) return "REFUSED something is open (pause depth " + pause.Depth + ", drawing " + drawing.InDrawMode + ")";
            var enter = typeof(DrawingInputController).GetMethod("EnterMode", BindingFlags.Instance | BindingFlags.NonPublic);
            var exit = typeof(DrawingInputController).GetMethod("ExitMode", BindingFlags.Instance | BindingFlags.NonPublic);
            if (enter == null || exit == null) return "REFUSED EnterMode / ExitMode not found";
            var sb = new StringBuilder("DrawPause308 check (draw scale " + drawing.DrawTimeScale.ToString("0.##") + ")").Append((char)10); int pass = 0, fail = 0;
            void Line(bool ok, string id, string text) { sb.Append(ok ? "  PASS " : "  FAIL ").Append(id).Append(' ').Append(text).Append((char)10); if (ok) pass++; else fail++; }
            float start = Time.timeScale;
            try
            {
                // C1
                Time.timeScale = 1f; enter.Invoke(drawing, null); float inDraw = Time.timeScale; var bound = pause.Drawing; pause.Drawing = null;
                pause.Begin(); float paused = Time.timeScale; bool stillDrawing = drawing.InDrawMode; pause.End(); float resumed = Time.timeScale; pause.Drawing = bound;
                Line(Mathf.Approximately(inDraw, drawing.DrawTimeScale) && paused == 0f && !stillDrawing && Mathf.Approximately(resumed, 1f), "C1",
                    "drawing " + inDraw.ToString("0.##") + " -> pause (binding cleared) " + paused.ToString("0.##") + ", drawing left open " + stillDrawing + " -> resume " + resumed.ToString("0.##"));
                // C2
                Time.timeScale = 1f; pause.Begin(); enter.Invoke(drawing, null); float underCard = Time.timeScale; bool entered = drawing.InDrawMode;
                if (entered) exit.Invoke(drawing, new object[] { false, true }); pause.End(); float after = Time.timeScale;
                Line(!entered && underCard == 0f && Mathf.Approximately(after, 1f), "C2", "press while paused: entered " + entered + ", scale under the card " + underCard.ToString("0.##") + ", after the card " + after.ToString("0.##"));
                // C3
                Time.timeScale = .06f; enter.Invoke(drawing, null); Time.timeScale = 1f; exit.Invoke(drawing, new object[] { false, true }); float afterHitStop = Time.timeScale;
                Time.timeScale = .06f; enter.Invoke(drawing, null); exit.Invoke(drawing, new object[] { false, true }); float direct = Time.timeScale;
                Line(Mathf.Approximately(afterHitStop, 1f) && Mathf.Approximately(direct, 1f), "C3", "entered at 0.06: left after the hit stop ended " + afterHitStop.ToString("0.##") + ", left during it " + direct.ToString("0.##"));
                // C4
                Time.timeScale = 1f; enter.Invoke(drawing, null); exit.Invoke(drawing, new object[] { false, true }); Time.timeScale = 0f; exit.Invoke(drawing, new object[] { false, true }); float twice = Time.timeScale;
                Line(twice == 0f, "C4", "second ExitMode over a pause: scale " + twice.ToString("0.##"));
            }
            catch (Exception e) { sb.Append("  EXCEPTION ").Append(e.InnerException != null ? e.InnerException.Message : e.Message).Append((char)10); fail++; }
            finally { if (drawing.InDrawMode) drawing.CancelForUi(); while (pause.Depth > 0) pause.End(); Time.timeScale = start > 0f ? start : 1f; }
            return sb.Append("  checks, ").Append(pass).Append(" pass ").Append(fail).Append(" fail").ToString();
        }
    }
}
