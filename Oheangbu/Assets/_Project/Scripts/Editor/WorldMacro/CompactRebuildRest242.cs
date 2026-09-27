using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class CompactRebuildRest242
    {
        const string Output = "../Art/World/Compact/Rebuild/Rest242";
        static WorldMacroPlaytestSession Session => SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(g => g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
        static readonly List<string> results = new List<string>();
        static Vector3[] corners;
        static int corner, phase, events;
        static float distance, stalled;
        static double lastTick, started;
        static Quaternion doorRotation;
        static Vector3 doorPosition, previous;
        static bool priorMotor;
        static int expectedCurrency;
        static string factSnapshot;
        static void Check(bool value, string name)
        {
            results.Add((value ? "PASS " : "FAIL ") + name);
            File.WriteAllText(Output + "/runtime.txt", string.Join("\n", results));
            if (!value) throw new Exception(name);
        }

        public static string Run(string command)
        {
            Directory.CreateDirectory(Output);
            if (command == "poll") return File.Exists(Output + "/runtime.txt") ? File.ReadAllText(Output + "/runtime.txt") : "No run";
            var s = Session;
            if (!SceneManager.GetActiveScene().path.Contains("slice-5e82ecd76d2a/")) throw new Exception("Candidate required");
            if (command == "build")
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Edit required");
                var visual = s.InteractionVisuals.Single(v => v.Id == "geumpyo_inn").Renderers.Single();
                var component = s.GetComponent<WorldMacroInnRestPresentation>() ?? s.gameObject.AddComponent<WorldMacroInnRestPresentation>();
                component.Session = s; component.Door = visual.transform;
                var b = visual.bounds;
                var toward = s.Content.InnCheckpointFeet - b.center; toward.y = 0; toward.Normalize();
                var side = Vector3.Cross(Vector3.up, toward);
                float halfWidth = Mathf.Abs(side.x) * b.extents.x + Mathf.Abs(side.z) * b.extents.z;
                component.HingeWorld = b.center - side * halfWidth;
                component.OpenDegrees = -24;
                component.ReturnYaw = Quaternion.LookRotation(toward).eulerAngles.y;
                BuildRecess(visual.transform, b, toward);
                s.InnRestPresentation = component;
                EditorUtility.SetDirty(s); EditorUtility.SetDirty(component);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                return "Doorway rest wired only to candidate; Content/Map/NavMesh/save slot untouched";
            }
            if (!EditorApplication.isPlaying || !s.TestSaveSuffix.StartsWith("_compact_slice_")) throw new Exception("Isolated slice-start Play required");
            var ui = PlaytestUiRoot.Instance;
            if (command == "reload-check")
            {
                var saved = JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(SavePath(s)));
                bool pass = s.LoadStatus == "primary" && s.Progress.ledger.checkpoint == "geumpyo_inn"
                    && Mathf.Abs(s.Progress.ledger.hp - 1) < .001f && Mathf.Abs(s.Progress.ledger.ink - 1) < .001f
                    && s.Progress.ledger.currency == saved.ledger.currency && !s.RestPresentationActive
                    && Vector3.Distance(s.Walker.Body.transform.position, saved.ledger.position) < .3f;
                File.WriteAllText(Output + "/reload.txt", (pass ? "PASS" : "FAIL") + " same isolated save reload: checkpoint/resources/currency/position; no transition replay");
                if (!pass) throw new Exception("Reload mismatch");
                return File.ReadAllText(Output + "/reload.txt");
            }
            if (command != "start") throw new ArgumentException(command);
            if (phase != 0) throw new Exception("Existing rest check running");
            results.Clear(); events = 0; distance = 0; stalled = 0;
            ui.CloseMenu(); ui.Gate.ReleaseImmediately();
            foreach (var actor in s.Actors) actor.gameObject.SetActive(false);
            s.InteractionResolved += Event;
            var door = s.InnRestPresentation;
            doorPosition = door.Door.position; doorRotation = door.Door.rotation;
            var point = s.Content.Points.Single(p => p.Id == "geumpyo_inn");
            var outward = s.Content.InnCheckpointFeet - point.Position; outward.y = 0; outward.Normalize();
            var start = s.Content.InnCheckpointFeet + outward * 14;
            if (!NavMesh.SamplePosition(start, out var startHit, 5, NavMesh.AllAreas)) throw new Exception("No approach start");
            if (!NavMesh.SamplePosition(point.Position + outward * 1.35f, out var target, 3, NavMesh.AllAreas)) throw new Exception("No doorway target");
            var path = new NavMeshPath();
            Check(NavMesh.CalculatePath(startHit.position, target.position, NavMesh.AllAreas, path)
                && path.status == NavMeshPathStatus.PathComplete, "complete approach NavMesh path");
            s.Teleport(startHit.position + Vector3.up * .06f, Quaternion.LookRotation(-outward).eulerAngles.y);
            // Start pose is a fixture. All subsequent approach displacement uses the live collision controller.
            corners = path.corners; corner = 1; previous = s.Walker.Body.transform.position;
            priorMotor = s.Walker.Motor.enabled; s.Walker.Motor.enabled = false;
            phase = 1; lastTick = started = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            Capture("approach");
            return "Continuous controller approach -> failed write -> rest transition -> input return; private save only";
        }
        static string SavePath(WorldMacroPlaytestSession s) => Path.Combine(Application.persistentDataPath, s.Content.SaveSlot + s.TestSaveSuffix + ".json");
        static void Event(Oheangbu.Data.World.PrologueInteractionKind kind, Vector3 p)
        { if (kind == Oheangbu.Data.World.PrologueInteractionKind.Rest) events++; }
        static void Capture(string name) => ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + "/" + name + ".png"));

        static void Tick()
        {
            if (!EditorApplication.isPlaying) { Stop(); return; }
            try
            {
                var s = Session; var ui = PlaytestUiRoot.Instance; var presentation = s.InnRestPresentation;
                double now = EditorApplication.timeSinceStartup;
                float dt = Mathf.Clamp((float)(now - lastTick), 0, .05f); lastTick = now;
                if (now - started > 90) throw new Exception("Rest sequence timeout; phase=" + phase + " focus=" + Application.isFocused);
                if (phase == 1)
                {
                    var pos = s.Walker.Body.transform.position;
                    if (corner < corners.Length)
                    {
                        var delta = corners[corner] - pos; delta.y = 0;
                        if (delta.magnitude < .2f) corner++;
                        else
                        {
                            s.Walker.Body.transform.rotation = Quaternion.LookRotation(delta);
                            s.Walker.Body.Move((delta.normalized * 3 + Vector3.down * 3) * dt);
                            float moved = Vector3.Distance(previous, s.Walker.Body.transform.position); distance += moved;
                            stalled = moved < .001f ? stalled + dt : 0;
                            if (stalled > 3) throw new Exception("Approach blocked at " + pos);
                            previous = s.Walker.Body.transform.position;
                        }
                        return;
                    }
                    s.Walker.Motor.enabled = priorMotor; ui.Gate.ReleaseImmediately();
                    Check(s.CanInteract("geumpyo_inn"), "live door interaction reachable after continuous approach " + distance.ToString("F2") + " m");
                    Capture("door"); phase = 2; return;
                }
                if (phase == 2)
                {
                    var vitals = s.Walker.Body.GetComponent<PlayerVitals>(); vitals.Restore(.37f);
                    var ink = typeof(WorldMacroPlaytestSession).GetField("ink", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(s) as InkPool;
                    ink.Restore(.29f);
                    string before = JsonUtility.ToJson(s.Progress);
                    var bytes = File.ReadAllBytes(SavePath(s));
                    using (var locked = new FileStream(SavePath(s) + ".tmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                    {
                        Check(!s.Interact("geumpyo_inn"), "locked save rejects rest");
                        Check(JsonUtility.ToJson(s.Progress) == before && Mathf.Abs(vitals.Hp01 - .37f) < .001f && Mathf.Abs(ink.Value - .29f) < .001f,
                            "failed rest preserves checkpoint/progress and live HP/ink");
                        Check(events == 0 && !presentation.IsActive && presentation.FadeAlpha == 0 && presentation.Door.position == doorPosition,
                            "failed rest emits no success event, door motion or fade");
                        Check(File.ReadAllBytes(SavePath(s)).SequenceEqual(bytes), "failed rest preserves primary save bytes");
                    }
                    factSnapshot = string.Join("|", s.Progress.campaign.Facts.OrderBy(x => x));
                    Check(s.Interact("geumpyo_inn"), "rest retries after write lock releases");
                    expectedCurrency = s.Progress.ledger.currency;
                    Check(Mathf.Abs(vitals.Hp01-1)<.001f&&Mathf.Abs(ink.Value-1)<.001f, "successful commit restores live HP and ink");
                    Check(presentation.IsActive && ui.Gate.InputBlocked && !s.CanInteract("geumpyo_inn"), "transition owns input and prevents repeat interaction");
                    ui.OpenPage("지도"); Check(ui.Page == "", "map cannot interrupt doorway transition");
                    Check(s.SaveNow(out _) && Vector3.Distance(s.Progress.ledger.position, s.Progress.ledger.checkpointPosition) < .001f,
                        "autosave during entry preserves committed return position");
                    phase = 3; return;
                }
                if (phase == 3 && presentation.Elapsed >= .75f) { Capture("opening"); phase = 4; }
                else if (phase == 4 && presentation.Elapsed >= 2.1f) { Capture("returning"); phase = 5; }
                else if (phase == 5 && !presentation.IsActive && !ui.Gate.InputBlocked)
                {
                    Check(events == 1 && presentation.CompletedCount == 1 && presentation.FadeAlpha == 0, "one completed transition, veil fully cleared");
                    Check(Vector3.Distance(presentation.Door.position, doorPosition) < .0001f && Quaternion.Angle(presentation.Door.rotation, doorRotation) < .01f, "door returns exactly to closed pose");
                    Check(Vector3.Distance(s.Walker.Body.transform.position, s.Progress.ledger.checkpointPosition) < .2f, "return uses safe yard checkpoint");
                    Check(factSnapshot.Split('|').Where(x=>x.Length>0).All(x=>s.Progress.campaign.Facts.Contains(x)), "discovery facts retained by rest");
                    Check(s.Walker.Motor.enabled && !s.Walker.Motor.IsSitting && !s.GameplayInputBlocked, "standing gameplay input restored without ground-sitting pose");
                    Capture("restored");
                    ui.OpenPage("지도"); Check(ui.Page == "지도", "map opens normally after rest"); ui.CloseMenu();
                    phase = 6; return;
                }
                else if (phase == 6 && !ui.Gate.InputBlocked)
                {
                    // A second approach verifies that returning to the yard does not trap or permanently consume the door.
                    var point = s.Content.Points.Single(p => p.Id == "geumpyo_inn");
                    var outward = s.Content.InnCheckpointFeet - point.Position; outward.y = 0; outward.Normalize();
                    NavMesh.SamplePosition(point.Position + outward * 1.35f, out var target, 3, NavMesh.AllAreas);
                    var delta = target.position - s.Walker.Body.transform.position; delta.y = 0;
                    if (!s.CanInteract("geumpyo_inn"))
                    {
                        s.Walker.Body.Move((delta.normalized * 3 + Vector3.down * 3) * dt); return;
                    }
                    Check(s.Interact("geumpyo_inn"), "repeat door rest accepted");
                    Check(s.Progress.ledger.currency == expectedCurrency, "repeat rest cannot repay campaign currency");
                    phase = 7; return;
                }
                else if (phase == 7 && !presentation.IsActive && !ui.Gate.InputBlocked)
                {
                    Check(presentation.CompletedCount == 2 && events == 2 && s.SaveNow(out _), "repeat transition and final snapshot succeed");
                    File.WriteAllText(Output + "/walk.txt", "Automatic Play controller approach: " + distance.ToString("F3") + " m. Start teleported; no teleport during approach. Rest intentionally returns under full fade. Not manual exploration or performance measurement.");
                    Stop();
                }
            }
            catch (Exception e) { results.Add("FAIL " + e); File.WriteAllText(Output + "/runtime.txt", string.Join("\n", results)); Stop(); }
        }
        static void Stop()
        {
            EditorApplication.update -= Tick; phase = 0;
            if (!EditorApplication.isPlaying) return;
            var s = Session; s.InteractionResolved -= Event; s.Walker.Motor.enabled = priorMotor;
        }

        static void BuildRecess(Transform door, Bounds bounds, Vector3 outward)
        {
            // The inn asset has no closed room behind this panel. A fixed shallow alcove prevents an open door
            // revealing the far landscape. These faces are behind the existing solid door collider, outside traversal.
            var parent = door.parent;
            var root = parent.Find("Inn_Door_Recess242");
            if (root == null) { root = new GameObject("Inn_Door_Recess242").transform; root.SetParent(parent, false); }
            root.SetPositionAndRotation(bounds.center, Quaternion.LookRotation(-outward));
            var scale = parent.lossyScale;
            root.localScale = new Vector3(1/scale.x,1/scale.y,1/scale.z);
            string folder = Path.GetDirectoryName(SceneManager.GetActiveScene().path).Replace('\\','/') + "/Rest242";
            DevSceneKit.EnsureFolder(folder);
            var material = AssetDatabase.LoadAssetAtPath<Material>(folder + "/Recess.mat");
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Inn_Dark_Recess242" };
                material.SetColor("_BaseColor", new Color(.055f,.043f,.032f));
                AssetDatabase.CreateAsset(material, folder + "/Recess.mat");
            }
            var tangent = Vector3.Cross(Vector3.up, outward);
            float w = Mathf.Abs(tangent.x)*bounds.size.x + Mathf.Abs(tangent.z)*bounds.size.z + .24f, h = bounds.size.y + .16f;
            void Panel(string name, Vector3 position, Vector3 size)
            {
                var child = root.Find(name);
                if (child == null)
                {
                    child = GameObject.CreatePrimitive(PrimitiveType.Cube).transform; child.name = name;
                    child.SetParent(root, false); UnityEngine.Object.DestroyImmediate(child.GetComponent<Collider>());
                }
                child.localPosition = position; child.localRotation = Quaternion.identity; child.localScale = size;
                child.GetComponent<Renderer>().sharedMaterial = material;
            }
            Panel("Back", new Vector3(0,0,.82f), new Vector3(w,h,.12f));
            Panel("Left", new Vector3(-w*.5f,0,.42f), new Vector3(.12f,h,.88f));
            Panel("Right", new Vector3(w*.5f,0,.42f), new Vector3(.12f,h,.88f));
            Panel("Ceiling", new Vector3(0,h*.5f,.42f), new Vector3(w,.12f,.88f));
            Panel("Floor", new Vector3(0,-h*.5f,.42f), new Vector3(w,.12f,.88f));
            AssetDatabase.SaveAssets();
        }
    }
}
