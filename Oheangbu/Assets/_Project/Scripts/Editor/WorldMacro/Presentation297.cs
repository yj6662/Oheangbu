using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Drawing;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #297 presentation captures (editor only). Renders a copy of the scene's gameplay camera (same URP renderer, ink
    // post, realm fog, beacon halos) at shipping quality. Stills work in Edit or Play mode; films run in Play mode with
    // Time.captureFramerate so animation advances exactly one frame per saved image regardless of render cost.
    //   shot:<name>:<eye x,y,z>:<target x,y,z>[:fov=60][:w=1920][:h=1080][:hideplayer]
    //   film:<spec.json>   — {name, fps, width, height, fov, warmup, hidePlayer, keys:[{time, eye[3], target[3], fov?}]}
    //   film-status | film-stop
    //   player:<x,y,z>[:yaw] — Play mode: teleport the player (keeps enemies/LOD awake near the shot)
    // Output: <repo>/Art/Presentation297/{Stills,Films}
    public static partial class Presentation297
    {
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Presentation297"));

        public static string Run(string command)
        {
            string[] a = command.Split(':');
            switch (a[0])
            {
                case "shot": return Shot(a);
                case "film": return StartFilm(string.Join(":", a.Skip(1)));
                case "film-status": return FilmStatus();
                case "film-stop": StopFilm("stopped"); return FilmStatus();
                case "player": return Player(a);
                case "find": return Find(a);
                case "actors": return Actors();
                case "mum-status": return MumStatus();
                case "mum-search": return MumSearch(a);
                case "teaser-stage": return TeaserStage(string.Join(":", a.Skip(1)));
                case "teaser-pose": return TeaserPose(a);
                case "teaser-clear": return TeaserClear();
                case "teaser-shot": return TeaserShot(a);
                case "teaser-info": return TeaserInfo();
                case "so-get": return SoGet(a);
                case "so-set": return SoSet(a);
                case "renderers": return Renderers(a);
                case "cameras": return Cameras();
                case "maincap": return MainCapture(a);
                case "weakpoint": return WeakPoint(a);
                case "riposte-status": return RiposteStatus();
                case "floaters": return Floaters(a);
                case "guardian-lock": return GuardianLock();
                case "guardian-info": return Object.FindFirstObjectByType<Oheangbu.App.World.Guardian302Actor>()?.Info() ?? "none";
                case "guardian-weak": return GuardianWeak();
                case "guardian": return Guardian(a);
                case "guardian-clear": { foreach (var g in Object.FindObjectsByType<Oheangbu.App.World.Guardian302Actor>(FindObjectsSortMode.None)) Object.Destroy(g.gameObject); return "cleared"; }
                case "play-stop": EditorApplication.isPlaying = false; return "stopping play (reshoots start from a fresh scene)";
                case "mum-unlock": { var s = Object.FindFirstObjectByType<WorldMacroPlaytestSession>(); s.SetMum295TestUnlock(a.Length < 2 || a[1] != "off"); return MumStatus(); }
                default: throw new ArgumentException("Unknown Presentation297 command " + command);
            }
        }

        static Vector3 Vec(string text)
        {
            var v = text.Split(',').Select(t => float.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            return new Vector3(v[0], v[1], v[2]);
        }

        static float Opt(string[] a, string key, float fallback)
        {
            string s = a.FirstOrDefault(x => x.StartsWith(key + "=", StringComparison.Ordinal));
            return s != null ? float.Parse(s.Substring(key.Length + 1), CultureInfo.InvariantCulture) : fallback;
        }

        // ---------------------------------------------------------------- capture camera

        sealed class Rig
        {
            public Camera Camera;
            public RenderTexture Target;
            public Texture2D Image;
            public CompactRebuildArtRenderer[] Arts;
            public Camera[] Observers;
            public int PriorQuality = -1;
            public List<Renderer> Hidden = new List<Renderer>();
            public bool PriorAsync;
            public Oheangbu.App.World.Dressing.WorldMacroDressingRenderer[] Dressing;
            public bool[] DressingDiagnostic;
        }

        static Rig Open(int width, int height, float fov, bool hidePlayer)
        {
            var scene = SceneManager.GetActiveScene();
            var source = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(c => c.gameObject.scene == scene && c.cameraType == CameraType.Game)
                .OrderByDescending(c => c.CompareTag("MainCamera")).ThenByDescending(c => c.isActiveAndEnabled).FirstOrDefault();
            if (source == null) throw new Exception("No gameplay camera in " + scene.path);
            var rig = new Rig();
            var go = new GameObject("Presentation297_Capture") { hideFlags = HideFlags.DontSave };
            rig.Camera = go.AddComponent<Camera>();
            rig.Camera.CopyFrom(source);
            rig.Camera.enabled = false;
            EditorUtility.CopySerialized(source.GetUniversalAdditionalCameraData(), rig.Camera.GetUniversalAdditionalCameraData());
            var data = rig.Camera.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.cameraStack?.Clear();
            rig.Camera.targetTexture = null;
            rig.Camera.fieldOfView = fov;
            rig.Camera.farClipPlane = Mathf.Max(rig.Camera.farClipPlane, 10000f);
            rig.Camera.aspect = width / (float)height;
            rig.Target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            rig.Image = new Texture2D(width, height, TextureFormat.RGB24, false);
            rig.Camera.targetTexture = rig.Target;
            rig.Arts = Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None);
            rig.Observers = rig.Arts.Select(x => x.Observer).ToArray();
            foreach (var art in rig.Arts) art.Observer = rig.Camera;
            rig.PriorQuality = QualitySettings.GetQualityLevel();
            int pc = Array.IndexOf(QualitySettings.names, "PC");
            if (pc >= 0 && pc != rig.PriorQuality) QualitySettings.SetQualityLevel(pc, true);
            rig.Dressing = Object.FindObjectsByType<Oheangbu.App.World.Dressing.WorldMacroDressingRenderer>(FindObjectsSortMode.None);
            rig.DressingDiagnostic = rig.Dressing.Select(d => d.AllowDiagnosticCameras).ToArray();
            foreach (var d in rig.Dressing) d.AllowDiagnosticCameras = true;
            rig.PriorAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            if (hidePlayer)
            {
                var gesture = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
                Transform player = gesture != null ? gesture.transform : null;
                while (player != null && player.parent != null && !player.name.StartsWith("Macro_CombatPlayerRig", StringComparison.Ordinal)) player = player.parent;
                if (player != null)
                    foreach (var r in player.GetComponentsInChildren<Renderer>(false))
                        if (r.enabled) { r.enabled = false; rig.Hidden.Add(r); }
            }
            return rig;
        }

        static void Close(Rig rig)
        {
            if (rig == null) return;
            foreach (var r in rig.Hidden) if (r != null) r.enabled = true;
            if (rig.Dressing != null) for (int i = 0; i < rig.Dressing.Length; i++) if (rig.Dressing[i] != null) rig.Dressing[i].AllowDiagnosticCameras = rig.DressingDiagnostic[i];
            for (int i = 0; i < rig.Arts.Length; i++) if (rig.Arts[i] != null) rig.Arts[i].Observer = rig.Observers[i];
            if (rig.PriorQuality >= 0 && QualitySettings.GetQualityLevel() != rig.PriorQuality) QualitySettings.SetQualityLevel(rig.PriorQuality, true);
            ShaderUtil.allowAsyncCompilation = rig.PriorAsync;
            if (rig.Camera != null) { rig.Camera.targetTexture = null; Object.DestroyImmediate(rig.Camera.gameObject); }
            if (rig.Target != null) { rig.Target.Release(); Object.DestroyImmediate(rig.Target); }
            if (rig.Image != null) Object.DestroyImmediate(rig.Image);
        }

        static void Render(Rig rig, Vector3 eye, Vector3 target, float fov, string file, int passes)
        {
            rig.Camera.fieldOfView = fov;
            rig.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
            for (int i = 0; i < Mathf.Max(1, passes); i++) rig.Camera.Render();
            var prior = RenderTexture.active;
            RenderTexture.active = rig.Target;
            rig.Image.ReadPixels(new Rect(0, 0, rig.Target.width, rig.Target.height), 0, 0);
            rig.Image.Apply(false);
            RenderTexture.active = prior;
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllBytes(file, file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? rig.Image.EncodeToJPG(95) : rig.Image.EncodeToPNG());
        }

        static string Shot(string[] a)
        {
            string name = a[1];
            Vector3 eye = Vec(a[2]), target = Vec(a[3]);
            float fov = Opt(a, "fov", 60f);
            int w = Mathf.RoundToInt(Opt(a, "w", 1920f)), h = Mathf.RoundToInt(Opt(a, "h", 1080f));
            string file = Path.Combine(Root, "Stills", name + ".png");
            var rig = Open(w, h, fov, a.Contains("hideplayer"));
            try { Render(rig, eye, target, fov, file, 3); }
            finally { Close(rig); }
            return file;
        }

        static string Player(string[] a)
        {
            if (!EditorApplication.isPlaying) throw new Exception("player: Play mode only");
            var s = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if (s == null) throw new Exception("No playtest session");
            Vector3 feet = Vec(a[1]);
            float yaw = a.Length > 2 ? float.Parse(a[2], CultureInfo.InvariantCulture) : 0f;
            s.Teleport(feet, yaw);
            return "player at " + feet.ToString("F1") + " yaw " + yaw.ToString("F0", CultureInfo.InvariantCulture);
        }

        // find:<name substring>[:max] — active objects whose name contains the text (case-insensitive), with world bounds
        static string Find(string[] a)
        {
            string needle = a[1].ToLowerInvariant();
            int max = a.Length > 2 ? int.Parse(a[2], CultureInfo.InvariantCulture) : 40;
            var scene = SceneManager.GetActiveScene();
            var hits = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(false))
                .Where(x => x.name.ToLowerInvariant().Contains(needle)).Take(max).ToArray();
            var sb = new System.Text.StringBuilder();
            foreach (var x in hits)
            {
                var rs = x.GetComponentsInChildren<Renderer>(false);
                string bounds = "";
                if (rs.Length > 0) { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); bounds = " bounds " + b.center.ToString("F1") + " size " + b.size.ToString("F1"); }
                var names = new List<string>(); for (var p = x; p != null; p = p.parent) names.Add(p.name); names.Reverse();
                sb.AppendLine(string.Join("/", names) + " @ " + x.position.ToString("F1") + bounds);
            }
            return sb.Length > 0 ? sb.ToString() : "no match";
        }

        // actors — Play mode: every session actor id with its position (enemies, bosses)
        static string Actors()
        {
            var s = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if (s == null) return "no session";
            return string.Join("\n", s.Actors.Where(x => x != null).Select(x => x.Id + " @ " + x.transform.position.ToString("F1") + (x.gameObject.activeInHierarchy ? "" : " (inactive)")));
        }

        static string MumStatus()
        {
            var s = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            var m = s != null ? s.MumBridges : null;
            if (m == null) return "no mum service";
            return "unlocked=" + m.IsUnlocked + " active=" + m.ActiveCount + " prepared=" + m.HasPrepared + " lastFailure=" + m.LastFailure
                + " player=" + (s.Walker != null ? s.Walker.Body.transform.position.ToString("F1") : "-");
        }

        // mum-search:<cx>,<cz>,<radius>,<step>[:max] — Play mode, test unlock on: real MumBridgeService.TryPlan over feet
        // candidates x 12 headings x 9 spans; the player is restored afterwards. Returns valid feet/yaw/target/span.
        static string MumSearch(string[] a)
        {
            var s = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            var m = s != null ? s.MumBridges : null;
            if (m == null) throw new Exception("no mum service");
            var v = a[1].Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            int max = a.Length > 2 ? int.Parse(a[2], CultureInfo.InvariantCulture) : 6;
            var plan = typeof(Oheangbu.App.Demo.MumBridgeService).GetMethod("TryPlan", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (plan == null) throw new Exception("TryPlan not found");
            var body = s.Walker.Body;
            Vector3 home = body.transform.position; Quaternion homeRot = body.transform.rotation;
            bool enabledPrior = body.enabled;
            var found = new List<string>();
            int tried = 0;
            try
            {
                body.enabled = false;
                for (float x = v[0] - v[2]; x <= v[0] + v[2] && found.Count < max; x += v[3])
                    for (float z = v[1] - v[2]; z <= v[1] + v[2] && found.Count < max; z += v[3])
                    {
                        if (!Physics.Raycast(new Vector3(x, 2000f, z), Vector3.down, out RaycastHit g, 4000f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        Vector3 feet = g.point;
                        body.transform.position = feet; Physics.SyncTransforms();
                        for (int k = 0; k < 24 && found.Count < max; k++)
                        {
                            float yaw = k * 15f;
                            Vector3 dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                            for (float d = 12f; d <= 36f; d += 3f)
                            {
                                Vector3 tp = feet + dir * d;
                                if (!Physics.Raycast(tp + Vector3.up * 60f, Vector3.down, out RaycastHit th, 200f, ~0, QueryTriggerInteraction.Ignore)) continue;
                                var args = new object[] { th.point, null, null };
                                tried++;
                                if ((bool)plan.Invoke(m, args))
                                {
                                    var pl = (Oheangbu.App.Demo.MumBridgePlan)args[1];
                                    // robust to aim error: nearby spans and headings must also plan
                                    int ok = 0;
                                    foreach (var (dd, dy) in new[] { (-1.5f, 0f), (1.5f, 0f), (0f, -2f), (0f, 2f) })
                                    {
                                        Vector3 dir2 = Quaternion.Euler(0, yaw + dy, 0) * Vector3.forward;
                                        Vector3 tp2 = feet + dir2 * (d + dd);
                                        if (!Physics.Raycast(tp2 + Vector3.up * 60f, Vector3.down, out RaycastHit th2, 200f, ~0, QueryTriggerInteraction.Ignore)) continue;
                                        var args2 = new object[] { th2.point, null, null };
                                        if ((bool)plan.Invoke(m, args2)) ok++;
                                    }
                                    if (ok < 4) continue;
                                    found.Add("feet=" + feet.ToString("F2") + " yaw=" + yaw.ToString("F0") + " target=" + th.point.ToString("F2") + " span=" + pl.Span.ToString("F1") + " start=" + pl.Start.ToString("F1") + " end=" + pl.End.ToString("F1"));
                                    break;
                                }
                            }
                        }
                    }
            }
            finally
            {
                body.transform.SetPositionAndRotation(home, homeRot); body.enabled = enabledPrior; Physics.SyncTransforms();
            }
            return "tried " + tried + " valid " + found.Count + "\n" + string.Join("\n", found);
        }

        // ---------------------------------------------------------------- films

        [Serializable] sealed class Key { public float time; public float[] eye; public float[] target; public float fov = -1f; }
        // q: Q held for duration. stroke: left button held for duration while the pointer runs the polyline
        // (viewport x,y pairs, y up) at constant speed. Between strokes the pointer glides to the next start.
        [Serializable] sealed class InputEvent { public float time; public string type; public float duration; public float[] points; public string key = ""; }
        [Serializable] sealed class FilmSpec
        {
            public string name = "film";
            public int fps = 30, width = 1920, height = 1080, warmup = 12;
            public float fov = 55f;
            public bool hidePlayer = true;
            public Key[] keys = new Key[0];
            public bool followMain;            // render the gameplay camera's own view (near arm, strokes, VFX; no UI)
            public float duration;             // followMain films: seconds to record
            public float[] playerFeet;         // optional teleport before recording
            public float playerYaw;
            public InputEvent[] inputs = new InputEvent[0];
            public string trackActor = "";     // session actor id: key eye/target are offsets from its (smoothed) position
            public bool trackEye = true, trackTarget = true;
            public float trackSmoothing = .35f;
            public bool board;                 // teleport beside the magic-stone car seat and board it after warm-up
            public bool mumUnlock;             // editor-only isolated Mum test unlock (review Play with suffix _mum295_test)
            public float pauseAt = -1f;        // #300: pause the editor at this take time and end the film (stills are then shot on the frozen frame)
        }
        [Serializable] sealed class FilmState { public string name, status, folder, error; public int frames, total, fps, width, height; public List<string> commits = new List<string>(); }

        static FilmSpec spec;
        static Rig filmRig;
        static FilmState state;
        static int lastFrame = -1, index;

        static string StateFile => Path.Combine(Root, "Films", "film-state.json");

        static string StartFilm(string specPath)
        {
            if (!EditorApplication.isPlaying) throw new Exception("film: Play mode only (captureFramerate)");
            if (filmRig != null) throw new Exception("A film is already running");
            spec = JsonUtility.FromJson<FilmSpec>(File.ReadAllText(specPath));
            if (!spec.followMain && (spec.keys == null || spec.keys.Length < 2)) throw new Exception("film needs at least two keys");
            float duration = spec.followMain ? spec.duration : spec.keys.Last().time;
            if (spec.playerFeet != null && spec.playerFeet.Length == 3)
            {
                var session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
                if (session != null) session.Teleport(new Vector3(spec.playerFeet[0], spec.playerFeet[1], spec.playerFeet[2]), spec.playerYaw);
            }
            string folder = Path.Combine(Root, "Films", spec.name);
            if (Directory.Exists(folder)) foreach (var f in Directory.GetFiles(folder, "f*.png")) File.Delete(f);
            Directory.CreateDirectory(folder);
            state = new FilmState { name = spec.name, status = "running", folder = folder, total = Mathf.CeilToInt(duration * spec.fps) + 1, fps = spec.fps, width = spec.width, height = spec.height };
            var sessionForFilm = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if (spec.board)
            {
                boardSeat = Object.FindFirstObjectByType<Oheangbu.App.World.Vehicle.WorldMacroPalanquinSeat>();
                if (boardSeat == null || boardSeat.SeatSocket == null) throw new Exception("No magic-stone car seat in the scene");
                Vector3 socket = boardSeat.SeatSocket.position, side = boardSeat.transform.right;
                Vector3 feet = socket + side * 1.4f;
                if (Physics.Raycast(feet + Vector3.up * 6f, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore)) feet = hit.point + Vector3.up * .05f;
                sessionForFilm?.Teleport(feet, Quaternion.LookRotation(Vector3.ProjectOnPlane(socket - feet, Vector3.up)).eulerAngles.y);
            }
            if (spec.mumUnlock && sessionForFilm != null) { sessionForFilm.SetMum295TestUnlock(true); mumUnlocked = true; }
            filmRig = Open(spec.width, spec.height, spec.fov, spec.hidePlayer && !spec.followMain);
            if (!spec.followMain) Oheangbu.App.World.Riposte301.BillboardView = filmRig.Camera.transform;
            if (spec.inputs != null && spec.inputs.Length > 0) OpenInput();
            Time.captureFramerate = spec.fps;
            lastFrame = Time.frameCount;
            index = -Mathf.Max(0, spec.warmup);
            trackValid = false; trackCached = null;
            EditorApplication.update -= FilmTick;
            EditorApplication.update += FilmTick;
            EditorApplication.playModeStateChanged -= FilmPlayMode;
            EditorApplication.playModeStateChanged += FilmPlayMode;
            Save();
            return "film " + spec.name + " started: " + state.total + " frames";
        }

        static void FilmPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode && filmRig != null) StopFilm("interrupted: Play mode exited");
        }

        static void FilmTick()
        {
            if (filmRig == null || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            try
            {
                float t = Mathf.Max(0f, index) / (float)spec.fps;
                Vector3 eye, target; float fov;
                if (spec.followMain)
                {
                    var main = MainCamera();
                    eye = main.transform.position; target = eye + main.transform.forward; fov = main.fieldOfView;
                    // #308 juice (SPEC-ANIM-JUICE-308 section 4b): the camera reaction is on the game camera only while that camera is
                    // drawn, and this film camera is drawn from here - so the nod and the field-of-view kick are added to the copy
                    // (read only). A roll is not carried (the film camera is aimed with a look-at; D-3 is off in the main profile).
                    var juiceRig308 = main.GetComponentInParent<Oheangbu.Combat.CameraRigController>();
                    if (juiceRig308 != null)
                    {
                        Vector3 offset308 = juiceRig308.RenderOffset308;
                        if (offset308.x != 0f) target = eye + main.transform.rotation * Quaternion.Euler(offset308.x, 0f, 0f) * Vector3.forward;
                        fov += offset308.z;
                    }
                }
                else
                {
                    Sample(t, out eye, out target, out fov);
                    if (!string.IsNullOrEmpty(spec.trackActor))
                    {
                        var actor = TrackedActor();
                        if (actor != null)
                        {
                            Vector3 now = actor.position;
                            trackBase = trackValid ? Vector3.Lerp(trackBase, now, 1f - Mathf.Exp(-(1f / spec.fps) / Mathf.Max(.01f, spec.trackSmoothing))) : now;
                            trackValid = true;
                            if (spec.trackEye) eye += trackBase;
                            if (spec.trackTarget) target += trackBase;
                        }
                    }
                }
                if (index == 0 && boardSeat != null)
                {
                    bool boarded = boardSeat.TryBoard();
                    state.commits.Add("board " + boarded + " " + boardSeat.LastInteraction);
                }
                if (inputOpen) DriveInput(index < 0 ? -1f : t + 1f / spec.fps);
                if (inputOpen && index >= 0 && index % 10 == 0) state.commits.Add("dbg f" + index + " " + InputDebug());
                string file = Path.Combine(state.folder, "f" + Mathf.Max(0, index).ToString("00000") + ".png");
                if (index < 0) { filmRig.Camera.fieldOfView = fov; filmRig.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye)); filmRig.Camera.Render(); }
                else { Render(filmRig, eye, target, fov, file, 1); state.frames = index + 1; }
                index++;
                if (spec.pauseAt >= 0f && index > 0 && t >= spec.pauseAt) { EditorApplication.isPaused = true; StopFilm("paused"); }
                else if (index >= state.total) StopFilm("done");
                else if (index % 15 == 0) Save();
            }
            catch (Exception e) { state.error = e.Message; StopFilm("failed"); Debug.LogException(e); }
        }

        static void StopFilm(string status)
        {
            EditorApplication.update -= FilmTick;
            CloseInput();
            boardSeat = null;
            if (mumUnlocked) { mumUnlocked = false; try { Object.FindFirstObjectByType<WorldMacroPlaytestSession>()?.SetMum295TestUnlock(false); } catch (Exception e) { Debug.LogException(e); } }
            Time.captureFramerate = 0;
            Oheangbu.App.World.Riposte301.BillboardView = null;
            var rig = filmRig; filmRig = null;
            Close(rig);
            if (state != null) { state.status = status; Save(); }
        }

        static void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile));
            File.WriteAllText(StateFile, JsonUtility.ToJson(state, true));
        }

        static string FilmStatus() => state == null ? (File.Exists(StateFile) ? File.ReadAllText(StateFile) : "no film") : JsonUtility.ToJson(state, true);

        // Catmull-Rom through the keys (eye and target), with ease-in on the first and ease-out on the last segment.
        static void Sample(float t, out Vector3 eye, out Vector3 target, out float fov)
        {
            var k = spec.keys;
            int i = 0;
            while (i < k.Length - 2 && t > k[i + 1].time) i++;
            float span = Mathf.Max(1e-4f, k[i + 1].time - k[i].time);
            float u = Mathf.Clamp01((t - k[i].time) / span);
            if (i == 0 && k.Length > 2) u = EaseIn(u);
            else if (i == k.Length - 2 && k.Length > 2) u = EaseOut(u);
            else if (k.Length == 2) u = Mathf.SmoothStep(0f, 1f, u);
            Vector3 P(Key key, bool isEye) => isEye ? new Vector3(key.eye[0], key.eye[1], key.eye[2]) : new Vector3(key.target[0], key.target[1], key.target[2]);
            Key k0 = k[Mathf.Max(0, i - 1)], k1 = k[i], k2 = k[i + 1], k3 = k[Mathf.Min(k.Length - 1, i + 2)];
            eye = CatmullRom(P(k0, true), P(k1, true), P(k2, true), P(k3, true), u);
            target = CatmullRom(P(k0, false), P(k1, false), P(k2, false), P(k3, false), u);
            float f1 = k1.fov > 0f ? k1.fov : spec.fov, f2 = k2.fov > 0f ? k2.fov : spec.fov;
            fov = Mathf.Lerp(f1, f2, u);
        }

        static Oheangbu.App.World.Vehicle.WorldMacroPalanquinSeat boardSeat;
        static bool mumUnlocked;
        static Vector3 trackBase;
        static bool trackValid;
        static Transform trackCached;
        static Transform TrackedActor()
        {
            if (trackCached != null) return trackCached;
            var s = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            // "player": the walker body (third-person takes of the player's own motion)
            if (spec.trackActor == "player") { trackCached = s != null && s.Walker != null ? s.Walker.Body.transform : null; return trackCached; }
            var a = s != null ? s.Actors.FirstOrDefault(x => x != null && x.Id == spec.trackActor) : null;
            trackCached = a != null ? a.transform : null;
            return trackCached;
        }

        static Camera MainCamera()
        {
            var main = Camera.main;
            if (main != null) return main;
            return Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).First(c => c.cameraType == CameraType.Game && c.isActiveAndEnabled);
        }

        // ---------------------------------------------------------------- scripted input (real Input System path)

        static Mouse vMouse;
        static Keyboard vKeyboard;
        static bool inputOpen;
        static InputSettings.EditorInputBehaviorInPlayMode priorBehavior;
        static DrawingInputController[] drawing = new DrawingInputController[0];
        static List<InputDevice> muted = new List<InputDevice>();
        static bool ownsMouse;

        static void OpenInput()
        {
            priorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            // value actions pick the most actuated control, and a disabled mouse keeps its last (off-view) position,
            // so the take drives the existing mouse device itself; other mice are disabled for the take
            var real = InputSystem.devices.OfType<Mouse>().Where(dv => dv.added && !dv.name.StartsWith("Presentation297", StringComparison.Ordinal)).ToList();
            foreach (var stale in InputSystem.devices.Where(dv => dv.name.StartsWith("Presentation297", StringComparison.Ordinal)).ToList()) InputSystem.RemoveDevice(stale);
            ownsMouse = real.Count == 0;
            vMouse = ownsMouse ? InputSystem.AddDevice<Mouse>("Presentation297Mouse") : real[0];
            if (!vMouse.enabled) InputSystem.EnableDevice(vMouse);
            muted = real.Skip(1).Where(dv => dv.enabled).Cast<InputDevice>().ToList();
            foreach (var dv in muted) InputSystem.DisableDevice(dv);
            vKeyboard = InputSystem.AddDevice<Keyboard>("Presentation297Keyboard");
            drawing = Object.FindObjectsByType<DrawingInputController>(FindObjectsSortMode.None);
            foreach (var d in drawing) d.CommitDiagnosed += OnCommit;
            inputOpen = true;
        }

        static void CloseInput()
        {
            if (!inputOpen) return;
            inputOpen = false;
            foreach (var d in drawing) if (d != null) d.CommitDiagnosed -= OnCommit;
            drawing = new DrawingInputController[0];
            if (vMouse != null)
            {
                InputSystem.QueueStateEvent(vMouse, new MouseState { position = vMouse.position.ReadValue() });
                if (ownsMouse) InputSystem.RemoveDevice(vMouse);
                vMouse = null;
            }
            if (vKeyboard != null) { InputSystem.QueueStateEvent(vKeyboard, new KeyboardState()); InputSystem.RemoveDevice(vKeyboard); vKeyboard = null; }
            foreach (var dv in muted) if (dv != null && dv.added) InputSystem.EnableDevice(dv);
            muted.Clear();
            InputSystem.settings.editorInputBehaviorInPlayMode = priorBehavior;
        }

        static void OnCommit(DrawingInputController.CommitDiagnostics d)
        {
            state?.commits.Add((d.Success ? "OK " + d.Letter : "MISS") + " " + d.InitialName + "+" + d.MedialName + "+" + d.FinalName
                + " strokes=" + d.StrokeCount + " points=" + d.PointCount + " worst=" + d.WorstDistance.ToString("F3", CultureInfo.InvariantCulture) + " frame=" + index);
        }

        static void DriveInput(float t)
        {
            bool q = false, press = false;
            var held = new List<UnityEngine.InputSystem.Key>();
            Vector2 look = Vector2.zero;
            Vector2 view = new Vector2(.5f, .5f);
            if (t >= 0f)
            {
                InputEvent previous = null, next = null;
                foreach (var e in spec.inputs)
                {
                    if (e.type == "q") { if (t >= e.time && t < e.time + e.duration) q = true; continue; }
                    if (e.type == "aim")
                    {
                        // points = world target x,y,z[,eyeHeight]: steer the view so the draw-mode eye ray meets it
                        if (t >= e.time && t < e.time + e.duration && e.points != null && e.points.Length >= 3) look += AimDelta(e.points);
                        continue;
                    }
                    if (e.type == "look")
                    {
                        // points = total mouse delta (x, y) spread evenly over the duration
                        if (t >= e.time && t < e.time + e.duration && e.points != null && e.points.Length >= 2)
                            look += new Vector2(e.points[0], e.points[1]) / Mathf.Max(1f, e.duration * spec.fps);
                        continue;
                    }
                    if (e.type == "key")
                    {
                        if (t >= e.time && t < e.time + e.duration && Enum.TryParse(e.key, true, out UnityEngine.InputSystem.Key k)) held.Add(k);
                        continue;
                    }
                    if (e.type != "stroke" || e.points == null || e.points.Length < 4) continue;
                    if (t >= e.time && t < e.time + e.duration) { press = true; view = Along(e.points, (t - e.time) / Mathf.Max(1e-3f, e.duration)); previous = null; next = null; break; }
                    if (e.time + e.duration <= t && (previous == null || e.time > previous.time)) previous = e;
                    if (e.time > t && (next == null || e.time < next.time)) next = e;
                }
                if (!press)
                {
                    if (previous != null && next != null)
                    {
                        float gap = next.time - (previous.time + previous.duration);
                        float u = gap > 1e-3f ? Mathf.SmoothStep(0f, 1f, (t - previous.time - previous.duration) / gap) : 1f;
                        view = Vector2.Lerp(Along(previous.points, 1f), Along(next.points, 0f), u);
                    }
                    else if (previous != null) view = Along(previous.points, 1f);
                    else if (next != null) view = Along(next.points, 0f);
                }
            }
            var screen = new Vector2(view.x * Screen.width, view.y * Screen.height);
            var ms = new MouseState { position = screen, delta = look }.WithButton(MouseButton.Left, press);
            InputSystem.QueueStateEvent(vMouse, ms);
            if (q) held.Add(UnityEngine.InputSystem.Key.Q);
            InputSystem.QueueStateEvent(vKeyboard, held.Count > 0 ? new KeyboardState(held.ToArray()) : new KeyboardState());
        }

        static string InputDebug()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("q=" + (vKeyboard != null && vKeyboard.qKey.isPressed) + " lmb=" + (vMouse != null && vMouse.leftButton.isPressed) + " pos=" + (vMouse != null ? vMouse.position.ReadValue().ToString("F0") : "-"));
            sb.Append(" kb.current=" + (Keyboard.current != null ? Keyboard.current.name : "null"));
            var cam = MainCamera();
            if (cam != null)
            {
                var ray = cam.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
                string aim = Physics.Raycast(ray, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance.ToString("F1") + "m " + hit.collider.name : "none";
                sb.Append(" pitch=" + Mathf.DeltaAngle(0f, cam.transform.eulerAngles.x).ToString("F1") + " aim=" + aim);
            }
            foreach (var d in drawing)
            {
                if (d == null) continue;
                var type = d.GetType();
                var f = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var drawMode = type.GetField("_drawMode", f)?.GetValue(d) as InputAction;
                bool allowed = d.EntryAllowed == null || d.EntryAllowed();
                sb.Append(" | " + d.gameObject.name + " en=" + d.isActiveAndEnabled + " draw=" + d.InDrawMode + " strokes=" + d.StrokeCount + " pts=" + d.TotalPointCount + " ptr=" + d.PointerScreenPosition.ToString("F0") + " screen=" + Screen.width + "x" + Screen.height
                    + " allowed=" + allowed + " blocked=" + (d.RuntimeState != null && d.RuntimeState.InputBlocked)
                    + " actQ=" + (drawMode != null && drawMode.IsPressed()) + " actEnabled=" + (drawMode != null && drawMode.enabled));
            }
            return sb.ToString();
        }

        static Vector2 AimDelta(float[] p)
        {
            var cam = MainCamera();
            var s = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if (cam == null || s == null || s.Walker == null) return Vector2.zero;
            float eyeHeight = p.Length > 3 ? p[3] : 1.85f;
            Vector3 eye = s.Walker.Body.transform.position + Vector3.up * eyeHeight;
            Vector3 target = new Vector3(p[0], p[1], p[2]);
            Vector3 d = target - eye;
            float desiredPitch = Mathf.Atan2(-d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;   // + = down
            float desiredYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = Mathf.DeltaAngle(0f, cam.transform.eulerAngles.x), yaw = cam.transform.eulerAngles.y;
            const float perUnit = .1214f;                                                              // measured: 10 units = 1.21 deg
            float ep = desiredPitch - pitch, ey = Mathf.DeltaAngle(yaw, desiredYaw);
            return new Vector2(Mathf.Clamp(ey / perUnit * .35f, -30f, 30f), Mathf.Clamp(-ep / perUnit * .35f, -30f, 30f));
        }

        static Vector2 Along(float[] p, float u)
        {
            int n = p.Length / 2;
            if (n == 1) return new Vector2(p[0], p[1]);
            float total = 0f;
            for (int i = 1; i < n; i++) total += Vector2.Distance(new Vector2(p[2 * i - 2], p[2 * i - 1]), new Vector2(p[2 * i], p[2 * i + 1]));
            float want = Mathf.Clamp01(u) * total;
            for (int i = 1; i < n; i++)
            {
                var a = new Vector2(p[2 * i - 2], p[2 * i - 1]); var b = new Vector2(p[2 * i], p[2 * i + 1]);
                float d = Vector2.Distance(a, b);
                if (want <= d || i == n - 1) return Vector2.Lerp(a, b, d > 1e-6f ? Mathf.Clamp01(want / d) : 1f);
                want -= d;
            }
            return new Vector2(p[2 * n - 2], p[2 * n - 1]);
        }

        static float EaseIn(float u) => u * u * (2f - u);          // starts slow, reaches the next segment at unit speed
        static float EaseOut(float u) => u * (1f + u - u * u);     // arrives slowly

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            float u2 = u * u, u3 = u2 * u;
            return .5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
        }
    }
}
