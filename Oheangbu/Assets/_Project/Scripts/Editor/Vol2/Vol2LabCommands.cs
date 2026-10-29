using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Oheangbu.App;
using Oheangbu.EditorTools.WorldMacro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>S5-b slice 1 (D308-149): the editor command that starts the 120-row vol.2 lab run. Queue (editor idle, no Play yet):
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2LabCommands Run "vol2lab:play"      leave a one-shot request, open SpellLab308, enter Play
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2LabCommands Run "vol2lab:status"    request / latest run summary
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2LabCommands Run "vol2lab:cancel"    remove a pending request
    /// vol2lab:play does NOT run unless asked: nothing else (no build, no refresh, no fixture) enters Play. It refuses, as text and without a
    /// dialog (a modal would stop the queue), when the editor is busy / playing, a scene is unsaved, the lab scene or its unlock stand-in
    /// is missing, or the vol.2 data does not load. The scope that builds in the lab reads the request once (Vol2LabRequestFile) and starts
    /// the Vol2LabRunner; the run writes Art/SpellVFX308/Vol2Lab/run_&lt;runId&gt;.csv and .summary.json. Leave Play with
    /// python Tools/Unity/playtest_polish.py editor stop   (or Oheangbu.EditorTools.WorldMacro.SpellLab308 Run "stop").</summary>
    public static class Vol2LabCommands
    {
        [Serializable] sealed class Started { public string status, runId, csv, summary, request, commit, scene, rulebook; }

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            if (c.StartsWith("vol2lab:", StringComparison.Ordinal)) c = c.Substring(8);
            try
            {
                switch (c)
                {
                    case "play": return Play();
                    case "status": return Status();
                    case "cancel": return Cancel();
                    default: return "REFUSED vol2lab: commands are vol2lab:play | vol2lab:status | vol2lab:cancel";
                }
            }
            catch (Exception e) { return "REFUSED vol2lab:" + c + ": " + e.GetType().Name + " " + e.Message; }
        }

        static string Play()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED vol2lab:play: Play is running or starting";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2lab:play: editor busy (compile / import)";
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) return "REFUSED vol2lab:play: unsaved changes in " + SceneManager.GetSceneAt(i).path + " (opening the lab would ask about them)";
            string scenePath = Vol2LabRequestFile.LabScenePath, sceneFull = Path.GetFullPath(scenePath);
            if (!File.Exists(sceneFull)) return "REFUSED vol2lab:play: no lab scene " + scenePath + " (SpellLab308 build)";
            if (!LabUnlockAssigned(sceneFull)) return "REFUSED vol2lab:play: the lab scene's CombatLifetimeScope has no _labUnlocks (the request would never be read)";
            var book = Vol2RulebookLoader.Load(null, null);
            if (!book.Ok) return "REFUSED vol2lab:play: vol.2 data unavailable: " + book.Error;

            string runId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string commit = Commit();
            Directory.CreateDirectory(Vol2LabRequestFile.Folder);
            var request = new Vol2LabRequest { mode = "Vol2", run = true, runId = runId, commit = commit, issuedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), scene = scenePath };
            File.WriteAllText(Vol2LabRequestFile.RequestPath, JsonUtility.ToJson(request, true), new UTF8Encoding(false));
            string started = SpellLab308.Run("play");
            if (!started.StartsWith("play:", StringComparison.Ordinal))
            {
                try { File.Delete(Vol2LabRequestFile.RequestPath); } catch (Exception) { }
                return "REFUSED vol2lab:play: " + started;
            }
            return JsonUtility.ToJson(new Started
            {
                status = "PLAY-REQUESTED", runId = runId, commit = commit, scene = scenePath, rulebook = "ok " + book.Spells.Count + " spells dto " + book.DtoHash.Substring(0, 12),
                request = Vol2LabRequestFile.RequestPath, csv = Path.Combine(Vol2LabRequestFile.Folder, "run_" + runId + ".csv"), summary = Path.Combine(Vol2LabRequestFile.Folder, "run_" + runId + ".summary.json")
            }, true);
        }

        static string Status()
        {
            var sb = new StringBuilder();
            sb.Append("playing=").Append(EditorApplication.isPlaying).Append(" scene=").Append(SceneManager.GetActiveScene().path).Append('\n');
            sb.Append("request=").Append(File.Exists(Vol2LabRequestFile.RequestPath) ? "PENDING " + Vol2LabRequestFile.RequestPath : "none").Append('\n');
            string folder = Vol2LabRequestFile.Folder;
            if (!Directory.Exists(folder)) { sb.Append("no run folder yet: ").Append(folder); return sb.ToString(); }
            var summaries = new DirectoryInfo(folder).GetFiles("run_*.summary.json").OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
            sb.Append("runs=").Append(new DirectoryInfo(folder).GetFiles("run_*.csv").Length).Append('\n');
            if (summaries.Length > 0) sb.Append("latest summary ").Append(summaries[0].FullName).Append('\n').Append(File.ReadAllText(summaries[0].FullName));
            return sb.ToString();
        }

        static string Cancel()
        {
            if (!File.Exists(Vol2LabRequestFile.RequestPath)) return "vol2lab:cancel: no pending request";
            File.Delete(Vol2LabRequestFile.RequestPath);
            return "vol2lab:cancel: pending request removed";
        }

        // _labUnlocks: {fileID: N} with N != 0 anywhere in the scene file = the scope has the lab stand-in assigned
        static bool LabUnlockAssigned(string sceneFull)
        {
            foreach (string line in File.ReadLines(sceneFull))
            {
                var m = Regex.Match(line, @"^\s*_labUnlocks:\s*\{fileID:\s*(-?\d+)");
                if (m.Success) return m.Groups[1].Value != "0";
            }
            return false;
        }

        static string Commit()
        {
            try
            {
                string git = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.git"));
                string head = File.ReadAllText(Path.Combine(git, "HEAD")).Trim();
                if (!head.StartsWith("ref:", StringComparison.Ordinal)) return head.Length >= 8 ? head.Substring(0, 8) : head;
                string reference = head.Substring(4).Trim(), file = Path.Combine(git, reference.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(file)) return File.ReadAllText(file).Trim().Substring(0, 8);
                string packed = Path.Combine(git, "packed-refs");
                if (File.Exists(packed))
                    foreach (string line in File.ReadLines(packed))
                        if (line.EndsWith(" " + reference, StringComparison.Ordinal) && line.Length > 8) return line.Substring(0, 8);
            }
            catch (Exception) { }
            return "unknown";
        }
    }
}
