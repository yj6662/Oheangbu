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
    /// <summary>#307 unfolded-map regression still (SPEC-MINIMAP-307: the minimap shader variant must leave the sheet unchanged).
    /// map-capture:&lt;name&gt; — isolated save (Harness303), Play W_Demo_Main, open the map with a real M press (VirtualInput303),
    /// settle, ScreenCapture to Art/Playtest306/Checks/&lt;name&gt;_&lt;W&gt;x&lt;H&gt;.png, Escape, stop Play, clean up. map-status | map-abort.
    /// Compare two captures with the same Game View size (before/after a shader change).</summary>
    public static class Map307Capture
    {
        const string MainScene = "Assets/_Project/Scenes/World/W_Demo_Main.unity", MainSlot = "world-main";
        static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Playtest306/Checks"));
        static readonly Isolation303 iso = new Isolation303();
        static string status = "idle", note = "", name = "", shot = ""; static int phase; static double at; static bool hooked, inputStarted, hold;
        static readonly List<string> lines = new List<string>();
        static WorldMacroPlaytestSession s;
        static double Now => EditorApplication.timeSinceStartup;
        static Vector2 Center => new Vector2(Screen.width * .5f, Screen.height * .5f);

        /// <summary>the held Play session (play-hold after arrival), else null — PerfSweep307 drives stations through it</summary>
        internal static WorldMacroPlaytestSession HeldSession => hold && status == "running" && phase == 1 && EditorApplication.isPlaying ? s : null;

        public static string Run(string command)
        {
            command = (command ?? "").Trim();
            if (command == "map-status") return status + " | " + note + " | " + shot + " | " + string.Join(" / ", lines.Skip(Math.Max(0, lines.Count - 4)));
            if (command == "map-abort" || command == "play-release") { Finish(hold ? "released" : "aborted"); return hold ? "released" : "aborted"; }
            // held Play helpers (#307 black-speck and perf look checks): hold-teleport:x,z[,yaw] (NavMesh within 30 m), hold-shot:name,
            // hold-quality:<index|name> (QualitySettings level, in memory only; the settings service may re-apply on scene change)
            if (command.StartsWith("hold-", StringComparison.Ordinal))
            {
                if (!hold || status != "running" || phase != 1 || s == null) return "refused: no held Play (play-hold first)";
                string rest = command.Substring(command.IndexOf(':') + 1);
                if (command.StartsWith("hold-teleport:", StringComparison.Ordinal))
                {
                    var v = rest.Split(',').Select(x => float.Parse(x.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                    // x,z[,yaw]: onto the top surface (raycast from above); x,y,z,yaw: exact feet (caves under terrain)
                    if (v.Length >= 4) { var feet = new Vector3(v[0], v[1], v[2]); s.Teleport(feet, v[3]); return "teleported to " + feet.ToString("F2") + " (exact)"; }
                    var probe = new Vector3(v[0], 2000f, v[1]);
                    if (!Physics.Raycast(probe, Vector3.down, out var ground, 4000f, ~0, QueryTriggerInteraction.Ignore)) return "refused: no ground under " + v[0] + "," + v[1];
                    var spot = ground.point;
                    if (UnityEngine.AI.NavMesh.SamplePosition(spot, out var nav, 30f, UnityEngine.AI.NavMesh.AllAreas)) spot = nav.position;
                    s.Teleport(spot, v.Length > 2 ? v[2] : 0f); return "teleported to " + spot.ToString("F1");
                }
                if (command.StartsWith("hold-shot:", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(Folder); string file = Path.Combine(Folder, rest + "_" + Screen.width + "x" + Screen.height + ".png");
                    ScreenCapture.CaptureScreenshot(file); return "capture requested " + file + " | quality " + QualitySettings.names[QualitySettings.GetQualityLevel()];
                }
                if (command.StartsWith("hold-quality:", StringComparison.Ordinal))
                {
                    int q = int.TryParse(rest, out int qi) ? qi : Array.IndexOf(QualitySettings.names, rest);
                    if (q < 0 || q >= QualitySettings.names.Length) return "refused: quality " + rest + " (" + string.Join(",", QualitySettings.names) + ")";
                    QualitySettings.SetQualityLevel(q, true); return "quality " + QualitySettings.names[q];
                }
                if (command.StartsWith("hold-lights", StringComparison.Ordinal))
                {
                    var cam = s.Walker != null ? s.Walker.ViewCamera : null; var eye = cam != null ? cam.transform.position : Vector3.zero;
                    var ls = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(l => l.type != LightType.Directional).ToList();
                    return "lights " + ls.Count + " active " + ls.Count(l => l.isActiveAndEnabled) + " shadowed " + ls.Count(l => l.shadows != LightShadows.None) +
                        " | nearest: " +
                        string.Join("; ", ls.Where(l => l.isActiveAndEnabled).OrderBy(l => Vector3.Distance(eye, l.transform.position)).Take(6)
                            .Select(l => l.name + " " + Vector3.Distance(eye, l.transform.position).ToString("0") + "m r" + l.range.ToString("0.#") + " " + l.shadows));
                }
                if (command.StartsWith("hold-sight", StringComparison.Ordinal))
                {
                    var cam = s.Walker != null ? s.Walker.ViewCamera : null; var sight = s.InteriorSight;
                    return "camera " + (cam != null ? cam.transform.position.ToString("F2") : "none") + " | interior sight " + (sight == null ? "none" : (sight.enabled ? "on" : "off") + " sealed " + sight.Sealed + " zone " + (sight.Current != null ? sight.Current.ZoneId : "-"));
                }
                if (command.StartsWith("hold-feet", StringComparison.Ordinal))
                {
                    var feet = Object.FindObjectsByType<WorldMacroPlayerFootPlacement>(FindObjectsSortMode.None);
                    return string.Join("; ", feet.Select(f => f.name + " clearance duplicates dropped " + f.ClearanceDuplicates307));
                }
                return "refused: hold-teleport | hold-shot | hold-quality | hold-lights | hold-sight | hold-feet";
            }
            // play-hold: the same isolated Play with no capture, held after arrival for Play-only checks (ArtCull307 artpixel etc.)
            if (command == "play-hold") { hold = true; command = "map-capture:__hold__"; }
            else if (command.StartsWith("map-capture:", StringComparison.Ordinal)) hold = false;
            if (!command.StartsWith("map-capture:", StringComparison.Ordinal)) return "refused: unknown command " + command;
            if (status == "running") return "refused: already running";
            // a finished run can leave its suffix on the edit-mode session (Play exit reloads the scene backup after cleanup)
            var stale = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if (stale != null && (stale.TestSaveSuffix ?? "").StartsWith("_c303", StringComparison.Ordinal)) { stale.TestSaveSuffix = ""; EditorUtility.ClearDirty(stale); }
            string why = Harness303.Prepare(iso, MainScene, MainSlot, "_c303_m307");
            if (why != null) return why;
            Harness303.Apply(iso, Object.FindFirstObjectByType<WorldMacroPlaytestSession>());
            name = command.Substring(12).Trim(); if (name.Length == 0) name = "map307";
            lines.Clear(); shot = ""; status = "running"; note = "starting"; phase = 0; at = Now; inputStarted = false; s = null;
            if (!hooked) { EditorApplication.update += Tick; hooked = true; }
            EditorApplication.isPlaying = true;
            return "started map-capture " + name;
        }

        static void Tick()
        {
            if (status == "finishing" && !EditorApplication.isPlayingOrWillChangePlaymode) { Cleanup(); return; }
            if (status != "running") return;
            if (hold && phase == 1 && Now - at > 1800) { lines.Add("FAIL hold over 30 min"); Finish("timeout"); return; }
            if (Now - at > 240 && !(hold && phase == 1)) { lines.Add("FAIL timeout phase " + phase + " (" + note + ")"); Finish("timeout"); return; }
            if (!EditorApplication.isPlaying) return;
            if (s == null) { s = Object.FindFirstObjectByType<WorldMacroPlaytestSession>(); if (s == null) { note = "waiting for session"; return; } }
            if (!s.InitializationComplete || UiAdapter303.LoadingInProgress == true) { note = "waiting for init/loading"; return; }
            switch (phase)
            {
                case 0: UiAdapter303.CloseMenu(); UiAdapter303.ReleaseGate(); phase = 1; at = Now; return;
                case 1 when hold: at = Now; note = "holding (play-release to stop)"; return;
                case 1:
                    if (Now - at < 1.5) return;
                    var assets = new List<InputActionAsset> { Harness303.Field<InputActionAsset>(s.Walker.Motor, "_actions"), Harness303.Field<InputActionAsset>(s.Walker.Drawing, "_actions") };
                    lines.Add("input: " + VirtualInput303.Begin(assets)); inputStarted = true; phase = 2; at = Now; return;
                case 2:
                    if (Now - at < 1.0) return;
                    VirtualInput303.Tap(Key.M, Center, 2); phase = 3; at = Now; note = "opening map"; return;
                case 3:
                    if (UiAdapter303.Page.Length == 0 && Now - at < 4) return;
                    if (Now - at < 3.0) return;   // fold-open motion settles
                    Directory.CreateDirectory(Folder);
                    shot = Path.Combine(Folder, name + "_" + Screen.width + "x" + Screen.height + ".png");
                    ScreenCapture.CaptureScreenshot(shot); lines.Add("capture " + shot + " page '" + UiAdapter303.Page + "'"); phase = 4; at = Now; return;
                case 4:
                    if (Now - at < 1.0) return;
                    VirtualInput303.Tap(Key.Escape, Center, 2); phase = 5; at = Now; return;
                case 5:
                    if (Now - at < 1.0) return;
                    Finish(File.Exists(shot) ? "PASS" : "FAIL no file"); return;
            }
        }

        static void Finish(string how)
        {
            status = "finishing"; lines.Add("RESULT " + how);
            if (inputStarted) { try { VirtualInput303.End(out _); } catch (Exception) { } inputStarted = false; }
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            EditorApplication.delayCall += Cleanup;
        }

        static void Cleanup()
        {
            if (status != "finishing") return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.delayCall += Cleanup; return; }
            status = "cleaning";
            foreach (var c in Harness303.Cleanup(iso, Path.Combine(Folder, "map307-run"))) lines.Add(c.status + " " + c.id + " " + c.detail);
            File.WriteAllText(Path.Combine(Folder, name + ".txt"), string.Join("\n", lines), new UTF8Encoding(false));
            status = lines.Any(l => l.StartsWith("FAIL", StringComparison.Ordinal)) ? "done FAIL" : "done PASS"; s = null;
        }
    }
}
