using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.World.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#307 (SPEC-MINIMAP-307, MINIMAP_DESIGN §2): HudController.Minimap (MinimapSpec304) is serialized in the play scenes, so the
    /// three values #307 changed in code (Window .88 -> .80, CaveMetres 40 -> 28, MarkerRimInset 10 -> 28) do not reach scenes saved
    /// before it (the new #307 fields do: they take the code defaults on load). This writes the code defaults of those three fields to
    /// every HudController in W_Demo_Main, W_Demo_Compact_Architecture296 and W_Demo_Compact_Folklore298 through SerializedObject.
    ///   minimap-look-apply:dry   open each scene, report before / would-be values (nothing written, no backup)
    ///   minimap-look-apply       back the three scene files up first (Art/Playtest306/Backups/Minimap307/&lt;utc&gt;/), then set and save
    /// Queue-safe: refusals are strings, never a dialog. Edit mode only; refuses while any open scene has unsaved changes; never touches
    /// the protected W_Demo_Compact.unity or the Watershed295 / Reworld292 / MountainTrail285 folders. The open scene set is restored.</summary>
    public static class Minimap307
    {
        const string Command = "minimap-look-apply";
        static string[] Scenes => new[] { Harness303.MainScene, Harness303.CandidateScene, Harness303.FolkloreScene };
        static string BackupRoot => Path.Combine(Harness303.RepoRoot, "Art", "Playtest306", "Backups", "Minimap307");

        public static string Run(string command)
        {
            string raw = (command ?? "").Trim();
            bool dry;
            if (raw == Command) dry = false;
            else if (raw == Command + ":dry") dry = true;
            else return "REFUSED unknown command '" + raw + "' (" + Command + " | " + Command + ":dry)";
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit mode only (Play is running)";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED the editor is compiling or importing";
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var open = SceneManager.GetSceneAt(i);
                if (open.isDirty) return "REFUSED unsaved changes in the open scene '" + (open.path.Length > 0 ? open.path : open.name) + "' - save or discard them first";
            }
            foreach (var path in Scenes)
            {
                if (Harness303.IsProtected(path) || path.EndsWith("/W_Demo_Compact.unity", StringComparison.OrdinalIgnoreCase)) return "REFUSED protected scene " + path;
                if (!File.Exists(Harness303.Abs(path))) return "REFUSED missing scene " + path;
            }

            var target = new MinimapSpec304();
            var sb = new StringBuilder();
            sb.AppendLine((dry ? "DRY (nothing written)" : "APPLY") + " #307 minimap look: Window " + target.Window + ", CaveMetres " + target.CaveMetres + ", MarkerRimInset " + target.MarkerRimInset + " (MinimapSpec304 code defaults)");
            string backups = null;
            if (!dry)
            {
                try
                {
                    backups = Path.Combine(BackupRoot, Harness303.UtcStamp());
                    Directory.CreateDirectory(backups);
                    foreach (var path in Scenes) File.Copy(Harness303.Abs(path), Path.Combine(backups, Path.GetFileName(path)), false);
                    File.WriteAllLines(Path.Combine(backups, "manifest.txt"), Scenes.Select(p => p + " | sha256 " + Harness303.Sha(Harness303.Abs(p))));
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                { return "REFUSED backup failed (nothing written): " + e.Message; }
                sb.AppendLine("backups: " + backups);
            }

            var setup = EditorSceneManager.GetSceneManagerSetup();
            int changedHuds = 0, savedScenes = 0; var failures = new List<string>();
            try
            {
                foreach (var path in Scenes)
                {
                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    if (!scene.IsValid() || !scene.isLoaded) { failures.Add(path + ": did not open"); continue; }
                    var huds = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<HudController>(true)).ToArray();
                    if (huds.Length == 0) { sb.AppendLine(path + " | no HudController"); continue; }
                    bool sceneChanged = false;
                    foreach (var hud in huds)
                    {
                        var so = new SerializedObject(hud);
                        var window = so.FindProperty("Minimap.Window"); var cave = so.FindProperty("Minimap.CaveMetres"); var inset = so.FindProperty("Minimap.MarkerRimInset");
                        string where = path + " | " + Harness303.PathOf(hud.transform);
                        if (window == null || cave == null || inset == null) { failures.Add(where + ": Minimap fields not found"); continue; }
                        string before = Values(window.floatValue, cave.floatValue, inset.floatValue);
                        bool needs = true || !Mathf.Approximately(window.floatValue, target.Window) || !Mathf.Approximately(cave.floatValue, target.CaveMetres) || !Mathf.Approximately(inset.floatValue, target.MarkerRimInset);
                        if (!needs) { sb.AppendLine(where + " | before " + before + " | unchanged"); continue; }
                        if (dry) { sb.AppendLine(where + " | before " + before + " | would be " + Values(target.Window, target.CaveMetres, target.MarkerRimInset)); continue; }
                        window.floatValue = target.Window; cave.floatValue = target.CaveMetres; inset.floatValue = target.MarkerRimInset;
                        // user 2026-09-30 rectangle: width, aspect and edge fade follow the code defaults too
                        var dia = so.FindProperty("Minimap.Diameter"); if (dia != null) dia.floatValue = target.Diameter;
                        var asp = so.FindProperty("Minimap.Aspect"); if (asp != null) asp.floatValue = target.Aspect;
                        var fade = so.FindProperty("Minimap.Fade"); if (fade != null) fade.vector2Value = target.Fade;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        so.Update();
                        sb.AppendLine(where + " | before " + before + " | after " + Values(so.FindProperty("Minimap.Window").floatValue, so.FindProperty("Minimap.CaveMetres").floatValue, so.FindProperty("Minimap.MarkerRimInset").floatValue));
                        sceneChanged = true; changedHuds++;
                    }
                    if (!sceneChanged) continue;
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (EditorSceneManager.SaveScene(scene)) savedScenes++; else failures.Add(path + ": SaveScene failed");
                }
            }
            catch (Exception e) { failures.Add("exception: " + e.GetType().Name + " " + e.Message); }
            finally
            {
                try { if (setup != null && setup.Length > 0 && setup.Any(x => x.isLoaded)) EditorSceneManager.RestoreSceneManagerSetup(setup); }
                catch (Exception e) { failures.Add("restoring the open scenes: " + e.Message); }
            }
            foreach (var f in failures) sb.AppendLine("FAIL " + f);
            sb.Append(dry ? "DRY done" : "APPLIED " + changedHuds + " HudController(s), saved " + savedScenes + " scene(s)").Append(failures.Count > 0 ? " with " + failures.Count + " failure(s)" : "");
            string report = sb.ToString();
            if (backups != null) { try { File.WriteAllText(Path.Combine(backups, "report.txt"), report, new UTF8Encoding(false)); } catch (IOException) { } }
            return report;
        }

        static string Values(float window, float cave, float inset) => "Window " + window.ToString("0.###") + ", CaveMetres " + cave.ToString("0.###") + ", MarkerRimInset " + inset.ToString("0.###");
    }
}
