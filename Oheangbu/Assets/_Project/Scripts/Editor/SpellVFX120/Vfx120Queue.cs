using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.SpellVFX120
{
    [InitializeOnLoad]
    public static class Vfx120Queue
    {
        [Serializable] class Command { public string id, method, request; }
        [Serializable] class Response { public string id, status, result, error; }
        static double next;
        static Vfx120Queue() { EditorApplication.update += Tick; }
        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + .25;
            string path = Path.Combine(Vfx120Editor.Output, "command.json");
            if (!File.Exists(path)) return;
            Command command;
            try { command = JsonUtility.FromJson<Command>(File.ReadAllText(path)); } catch (IOException) { return; }
            if (command == null || string.IsNullOrEmpty(command.id)) return;
            var response = new Response { id = command.id };
            File.Move(path, Path.Combine(Vfx120Editor.Output, "request_" + command.id + ".json"));
            try
            {
                switch (command.method)
                {
                    case "Probe": response.result = "Unity=" + Application.unityVersion + "; playing=" + EditorApplication.isPlaying + "; scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path; break;
                    case "ParticleColorProbe": response.result = Vfx120ParticleColorProbe.Run(); break;
                    case "RibbonProbe": response.result = Vfx120ParticleColorProbe.RibbonProbe(); break;
                    case "Build": response.result = Vfx120Editor.Build(); break;
                    case "Audit": response.result = Vfx120Editor.AuditCatalog(); break;
                    case "Review": response.result = Vfx120Editor.Review(); break;
                    case "Calibrate": response.result = Vfx120Editor.Calibrate(); break;
                    case "ExportMeshes": response.result = Vfx120Editor.ExportMeshes(); break;
                    case "Connect": response.result = Vfx120Editor.Connect(); break;
                    case "AdapterAudit": response.result = Vfx120AdapterAudit.Run(); break;
                    case "MotionAudit": response.result = Vfx120MotionAudit.Run(); break;
                    case "NativeParticleAudit": response.result = Vfx120MotionAudit.RunNativeParticles(); break;
                    case "AreaAudit": response.result = Vfx120AreaAudit.Run(); break;
                    case "RuntimeAudit": response.result = Vfx120RuntimeAudit.Start(); break;
                    case "RuntimePoll": response.result = Vfx120RuntimeAudit.Poll(); break;
                    case "GameplayAudit": response.result = Vfx120GameplayAudit.Start(); break;
                    case "GameplayCapture": response.result = Vfx120GameplayAudit.Start(0, true); break;
                    case "GameplayDiagnosticCapture": response.result = Vfx120GameplayAudit.Start(0, true, true); break;
                    case "ContrastAudit": response.result = Vfx120ContrastAudit.Run(); break;
                    case "GameplayPoll": response.result = Vfx120GameplayAudit.Poll(); break;
                    case "GameplayCancel": response.result = Vfx120GameplayAudit.Cancel(); break;
                    case "OpenWorld": response.result = OpenSavedScene("Assets/_Project/Scenes/Dev/C2_CodexWorld.unity"); break;
                    case "OpenRange": response.result = OpenSavedScene("Assets/_Project/Scenes/Dev/C1_SpellRange.unity"); break;
                    case "RenderAudit": response.result = Vfx120RenderAudit.Run(); break;
                    case "Capture": response.result = Vfx120Capture.Start(command.request); break;
                    case "CapturePoll": response.result = Vfx120Capture.Poll(); break;
                    case "Play": EditorApplication.isPlaying = true; response.result = "PLAY_REQUESTED"; break;
                    case "Stop": EditorApplication.isPlaying = false; response.result = "STOP_REQUESTED"; break;
                    default: throw new InvalidOperationException("Unknown scoped VFX command");
                }
                response.status = "COMPLETE";
            }
            catch (Exception e) { response.status = "FAILED"; response.error = e.ToString(); }
            File.WriteAllText(Path.Combine(Vfx120Editor.Output, "response_" + command.id + ".json"), JsonUtility.ToJson(response, true));
        }

        static string OpenSavedScene(string path)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before opening a saved scene");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Unsaved scene changes; saved scene was not replaced");
            if (!File.Exists(path)) throw new FileNotFoundException("Scene is unavailable", path);
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
            return path;
        }
    }
}
