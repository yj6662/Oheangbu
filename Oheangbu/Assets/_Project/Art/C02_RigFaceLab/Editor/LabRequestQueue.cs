using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.C02RigFaceLab.Editor
{
    // Local recovery path for the MCP package's connection loss across domain reloads.
    // Only the experiment's named operations are accepted; no arbitrary C# evaluation.
    [InitializeOnLoad]
    public static class LabRequestQueue
    {
        [Serializable] class Command { public string id; public string method; public string request; }
        [Serializable] class Response { public string id; public string method; public string status; public string result; public string error; public bool playing; }
        static double nextCheck;
        static LabRequestQueue() { EditorApplication.update += Tick; }
        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextCheck) return;
            nextCheck = EditorApplication.timeSinceStartup + 0.25;
            string path = Path.Combine(LabEditor.OutputRoot, "command.json");
            if (!File.Exists(path)) return;
            Command command;
            try { command = JsonUtility.FromJson<Command>(File.ReadAllText(path)); }
            catch (IOException) { return; }
            if (command == null || string.IsNullOrEmpty(command.id)) return;
            string responsePath = Path.Combine(LabEditor.OutputRoot, "response_" + command.id + ".json");
            if (File.Exists(responsePath)) return;
            // Mark consumed before entering play mode or importing, both of which can reload this class.
            File.Move(path, Path.Combine(LabEditor.OutputRoot, "command_" + command.id + ".json"));
            var response = new Response { id = command.id, method = command.method };
            try
            {
                switch (command.method)
                {
                    case "RootHeightAudit": response.result = LabRootHeightAudit.Run(); break;
                    case "Probe": response.result = LabEditor.Probe(); break;
                    case "Import": response.result = LabEditor.Import(command.request); break;
                    case "BuildReview": response.result = LabEditor.BuildReview(command.request); break;
                    case "CalibrateReview": response.result = LabEditor.CalibrateReview(); break;
                    case "TestCandidateSkinQuality": response.result = LabEditor.TestCandidateSkinQuality(); break;
                    case "OpenReview": response.result = LabEditor.OpenReview(command.request); break;
                    case "TestFiveWeights": response.result = LabEditor.TestFiveWeights(); break;
                    case "StartCapture": response.result = LabEditor.StartCapture(command.request); break;
                    case "Play": EditorApplication.isPlaying = true; response.result = "PLAY_REQUESTED"; break;
                    case "Stop": EditorApplication.isPlaying = false; response.result = "STOP_REQUESTED"; break;
                    default: throw new InvalidOperationException("Unknown experiment command");
                }
                response.status = "COMPLETE";
            }
            catch (Exception exception) { response.status = "FAILED"; response.error = exception.ToString(); }
            response.playing = EditorApplication.isPlaying;
            File.WriteAllText(responsePath, JsonUtility.ToJson(response, true));
        }
    }
}
