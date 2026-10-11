using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Cinemachine;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Splines;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 trailer camera rig (SPEC-TRAILER-308): one Cinemachine shot per cut, kept in a prefab that is never part of a build.
    //   Shot_<name>            Animator + PlayableDirector (Timeline: one animation track)
    //     Path                 SplineContainer, knots in the shot's own space
    //     Aim                  what the camera looks at (its position is keyed)
    //     VCam                 CinemachineCamera + Spline Dolly (normalized position keyed) + Rotation Composer; field of view keyed
    // The timeline keys the dolly position, the field of view and the aim, so speed is a curve the user can reshape by hand.
    // Films read the shot through Presentation297 (film spec "rigShot"): each recorded frame evaluates the timeline at that
    // time, updates the Cinemachine camera and copies its pose to the capture camera. The gameplay camera is never touched.
    //   build[:<name>]   edit mode: (re)make shots from Tools/Unity/Stage308_trailer/shots308.json (a shot marked manual is left alone)
    //   list             the shots in the prefab with their lengths
    //   pose:<name>:<t>  edit mode: the camera pose of a shot at a time (for stills: eye and look point)
    //   ground:<x>,<z>[;...]   the highest collider under each point (for placing knots by eye height)
    //   sheet:<name>[:n]       stills along a shot (n evenly spaced, default 4) to Art/Presentation297/Stills/rig_<name>_<i>.png
    public static class TrailerRig308
    {
        const string Folder = "Assets/_Project/Art/Trailer308", PrefabPath = Folder + "/TrailerRig308.prefab";
        static string SpecPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Tools/Unity/Stage308_trailer/shots308.json"));

        [Serializable] sealed class Shot
        {
            public string name = ""; public float seconds = 4f, fov = 55f, damping = 0f;
            public float[] knots = new float[0];      // x,y,z ...        the dolly path
            public float[] pos = new float[0];        // t,ratio ...      where on the path (0..1) at which time; empty = even
            public float[] aim = new float[0];        // t,x,y,z ...      the look point
            public float[] fovKeys = new float[0];    // t,fov ...
            public string track = "";                 // "player" | "car" | a session actor id: the shot rides on it (knots and aim are offsets)
            public bool trackYaw;                     // offsets turn with the tracked thing's heading
            public float trackSmoothing = .25f;
            public bool manual;                       // hand-edited in the prefab: build leaves it alone
        }
        [Serializable] sealed class Specs { public Shot[] shots = new Shot[0]; }

        public static string Run(string command)
        {
            var a = (command ?? "").Split(':');
            switch (a[0])
            {
                case "build": return Build(a.Length > 1 ? a[1] : null);
                case "list": return List();
                case "pose": return Pose(a[1], float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture));
                case "ground": return Ground(a[1]);
                case "sheet": return Sheet(a[1], a.Length > 2 ? int.Parse(a[2]) : 4);
                default: return "REFUSED build[:<name>] | list | pose:<name>:<t> | ground:<x>,<z>[;...] | sheet:<name>[:n]";
            }
        }

        static Specs Load() => JsonUtility.FromJson<Specs>(File.ReadAllText(SpecPath));

        static string Build(string only)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED edit mode only";
            var specs = Load();
            Directory.CreateDirectory(Folder); Directory.CreateDirectory(Folder + "/Timelines");
            AssetDatabase.Refresh();
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var root = existing != null ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject("TrailerRig308");
            var report = new List<string>();
            try
            {
                foreach (var s in specs.shots)
                {
                    if (only != null && s.name != only) continue;
                    var old = root.transform.Find("Shot_" + s.name);
                    if (s.manual && old != null) { report.Add(s.name + " kept (manual)"); continue; }
                    if (old != null) Object.DestroyImmediate(old.gameObject);
                    report.Add(s.name + " " + MakeShot(root.transform, s));
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { if (existing != null) PrefabUtility.UnloadPrefabContents(root); else Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            return "TrailerRig308 build -> " + PrefabPath + (char)10 + string.Join(((char)10).ToString(), report);
        }

        static string MakeShot(Transform root, Shot s)
        {
            if (s.knots.Length < 6 || s.knots.Length % 3 != 0) throw new Exception(s.name + ": at least two knots (x,y,z each)");
            if (s.aim.Length < 4 || s.aim.Length % 4 != 0) throw new Exception(s.name + ": aim needs t,x,y,z groups");
            var shot = new GameObject("Shot_" + s.name); shot.transform.SetParent(root, false); shot.SetActive(false);
            var path = new GameObject("Path"); path.transform.SetParent(shot.transform, false);
            var container = path.AddComponent<SplineContainer>(); var spline = container.Spline; spline.Clear();
            for (int i = 0; i < s.knots.Length; i += 3) spline.Add(new BezierKnot(new float3(s.knots[i], s.knots[i + 1], s.knots[i + 2])), TangentMode.AutoSmooth);
            var aim = new GameObject("Aim"); aim.transform.SetParent(shot.transform, false); aim.transform.localPosition = new Vector3(s.aim[1], s.aim[2], s.aim[3]);
            var cam = new GameObject("VCam"); cam.transform.SetParent(shot.transform, false);
            var vcam = cam.AddComponent<CinemachineCamera>();
            var lens = LensSettings.Default; lens.FieldOfView = s.fov; lens.NearClipPlane = .1f; lens.FarClipPlane = 10000f; vcam.Lens = lens;
            vcam.Target = new CameraTarget { LookAtTarget = aim.transform, CustomLookAtTarget = true };
            var dolly = cam.AddComponent<CinemachineSplineDolly>(); dolly.Spline = container; dolly.PositionUnits = PathIndexUnit.Normalized; dolly.CameraPosition = 0f;
            var composer = cam.AddComponent<CinemachineRotationComposer>(); composer.Damping = new Vector2(s.damping, s.damping);

            var clip = new AnimationClip { name = "Shot_" + s.name, frameRate = 60 };
            float[] pos = s.pos.Length >= 4 ? s.pos : new[] { 0f, 0f, s.seconds, 1f };
            Curve(clip, "VCam", typeof(CinemachineSplineDolly), "m_SplineSettings.Position", Pairs(pos, 2, 1), true);
            if (s.fovKeys.Length >= 2) Curve(clip, "VCam", typeof(CinemachineCamera), "Lens.FieldOfView", Pairs(s.fovKeys, 2, 1), s.fovKeys.Length > 2);
            Curve(clip, "Aim", typeof(Transform), "m_LocalPosition.x", Pairs(s.aim, 4, 1), true);
            Curve(clip, "Aim", typeof(Transform), "m_LocalPosition.y", Pairs(s.aim, 4, 2), true);
            Curve(clip, "Aim", typeof(Transform), "m_LocalPosition.z", Pairs(s.aim, 4, 3), true);

            string timelinePath = Folder + "/Timelines/Shot_" + s.name + ".playable";
            AssetDatabase.DeleteAsset(timelinePath);
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>(); AssetDatabase.CreateAsset(timeline, timelinePath);
            timeline.editorSettings.frameRate = 60;
            var track = timeline.CreateTrack<AnimationTrack>(null, "Camera");
            AssetDatabase.AddObjectToAsset(clip, timeline);
            var tc = track.CreateClip(clip); tc.start = 0; tc.duration = s.seconds; tc.displayName = s.name;
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength; timeline.fixedDuration = s.seconds;
            EditorUtility.SetDirty(timeline); AssetDatabase.SaveAssetIfDirty(timeline);

            var animator = shot.AddComponent<Animator>();
            var director = shot.AddComponent<PlayableDirector>(); director.playableAsset = timeline; director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.Manual; director.extrapolationMode = DirectorWrapMode.Hold;
            director.SetGenericBinding(track, animator);
            return s.seconds.ToString("0.##") + " s, " + spline.Count + " knots, path " + spline.GetLength().ToString("0.0") + " m" + (s.track != "" ? ", rides " + s.track : "");
        }

        static (float t, float v)[] Pairs(float[] flat, int stride, int column)
        {
            var list = new List<(float, float)>();
            for (int i = 0; i + stride <= flat.Length; i += stride) list.Add((flat[i], flat[i + column]));
            return list.ToArray();
        }

        static void Curve(AnimationClip clip, string path, Type type, string property, (float t, float v)[] keys, bool smooth)
        {
            var curve = new AnimationCurve(keys.Select(k => new Keyframe(k.t, k.v)).ToArray());
            for (int i = 0; i < curve.length; i++)
            {
                var mode = smooth ? AnimationUtility.TangentMode.ClampedAuto : AnimationUtility.TangentMode.Linear;   // clamped: no overshoot past a key (the gate rush must not run back)
                AnimationUtility.SetKeyLeftTangentMode(curve, i, mode); AnimationUtility.SetKeyRightTangentMode(curve, i, mode);
            }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), curve);
        }

        static string List()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) return "no rig prefab (run build)";
            var specs = Load();
            return string.Join(((char)10).ToString(), prefab.GetComponentsInChildren<PlayableDirector>(true).Select(d =>
            {
                var s = specs.shots.FirstOrDefault(x => "Shot_" + x.name == d.name);
                return d.name + " " + d.duration.ToString("0.##") + " s" + (s != null && s.track != "" ? " rides " + s.track : "") + (s == null ? " (not in the spec file)" : "");
            }));
        }

        // ---------------------------------------------------------------- evaluation (films in Play, poses in edit mode)

        static GameObject live; static Shot liveShot; static float liveSeconds; static PlayableDirector liveDirector; static CinemachineCamera liveCam; static AnimationClip liveClip;
        static Transform ride; static Vector3 rideBase; static float rideYaw; static bool rideValid, first;

        public static float Begin(string name, Func<string, Transform> findRide)
        {
            End();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) ?? throw new Exception("no rig prefab (TrailerRig308 build)");
            var source = prefab.transform.Find("Shot_" + name) ?? throw new Exception("no shot " + name + " in the rig");
            live = Object.Instantiate(source.gameObject); live.name = "TrailerRig308_Live_" + name; live.hideFlags = HideFlags.DontSave;
            live.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); live.SetActive(true);
            // what a shot rides on is read from the spec file (the prefab holds only Cinemachine, spline and timeline objects)
            liveShot = Load().shots.FirstOrDefault(x => x.name == name) ?? new Shot { name = name };
            liveDirector = live.GetComponent<PlayableDirector>(); liveCam = live.GetComponentInChildren<CinemachineCamera>(true);
            liveSeconds = (float)liveDirector.duration;
            // The timeline's camera clip is sampled directly: in Play a director that is evaluated by hand does not move the dolly
            // (every frame of the first Play takes was the first frame, 2026-10-08), while sampling the clip works in both modes.
            var timeline = liveDirector.playableAsset as TimelineAsset;
            var track = timeline != null ? timeline.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault() : null;
            liveClip = track != null ? track.GetClips().Select(c => c.animationClip).FirstOrDefault(c => c != null) ?? track.infiniteClip : null;
            if (liveClip == null) throw new Exception("shot " + name + ": its timeline has no camera animation clip");
            liveDirector.enabled = false; var animator = live.GetComponent<Animator>(); if (animator != null) animator.enabled = false;
            ride = !string.IsNullOrEmpty(liveShot.track) && findRide != null ? findRide(liveShot.track) : null;
            if (!string.IsNullOrEmpty(liveShot.track) && ride == null) throw new Exception("shot " + name + " rides '" + liveShot.track + "', which is not in this Play");
            rideValid = false; first = true;
            return liveSeconds;
        }

        public static void Sample(float t, float dt, out Vector3 eye, out Quaternion rotation, out float fov)
        {
            if (ride != null)
            {
                Vector3 now = ride.position; float yaw = ride.eulerAngles.y;
                float k = rideValid ? 1f - Mathf.Exp(-dt / Mathf.Max(.01f, liveShot.trackSmoothing)) : 1f;
                rideBase = rideValid ? Vector3.Lerp(rideBase, now, k) : now; rideYaw = rideValid ? Mathf.LerpAngle(rideYaw, yaw, k) : yaw; rideValid = true;
                live.transform.SetPositionAndRotation(rideBase, liveShot.trackYaw ? Quaternion.Euler(0f, rideYaw, 0f) : Quaternion.identity);
            }
            liveClip.SampleAnimation(live, Mathf.Clamp(t, 0f, liveSeconds));
            // Called directly: UpdateCameraState is guarded to once per Cinemachine frame, and that frame number only advances under a
            // CinemachineBrain. This rig has none (the capture camera is posed by hand), so in Play every frame after the first was skipped.
            liveCam.InternalUpdateCameraState(Vector3.up, first ? -1f : dt); first = false;     // -1: no damping history on the first frame
            var state = liveCam.State;
            eye = state.GetFinalPosition(); rotation = state.GetFinalOrientation(); fov = state.Lens.FieldOfView;
        }

        public static void End()
        {
            if (live != null) Object.DestroyImmediate(live);
            live = null; liveShot = null; liveDirector = null; liveCam = null; liveClip = null; ride = null;
        }

        static string Ground(string list)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture; var sb = new System.Text.StringBuilder("ground");
            foreach (var item in list.Split(';'))
            {
                var c = item.Split(','); float x = float.Parse(c[0], inv), z = float.Parse(c[1], inv);
                var hits = Physics.RaycastAll(new Vector3(x, 2000f, z), Vector3.down, 4000f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).ToArray();
                sb.Append((char)10).Append("  ").Append(x.ToString("0.#", inv)).Append(',').Append(z.ToString("0.#", inv)).Append(" -> ");
                if (hits.Length == 0) { sb.Append("nothing"); continue; }
                sb.Append(string.Join(" | ", hits.Take(3).Select(h => h.point.y.ToString("0.00", inv) + " " + h.collider.name)));
            }
            return sb.ToString();
        }

        static string Sheet(string name, int n)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture; var lines = new List<string>();
            try
            {
                float seconds = Begin(name, _ => null);
                for (int i = 0; i < n; i++)
                {
                    float t = n == 1 ? 0f : seconds * i / (n - 1);
                    first = true; Sample(t, 1f / 30f, out var eye, out var rotation, out float fov);
                    var look = eye + rotation * Vector3.forward * 20f;
                    string F(Vector3 v) => v.x.ToString("0.00", inv) + "," + v.y.ToString("0.00", inv) + "," + v.z.ToString("0.00", inv);
                    lines.Add(Presentation297.Run("shot:rig_" + name + "_" + i + ":" + F(eye) + ":" + F(look) + ":fov=" + fov.ToString("0.#", inv) + ":w=960:h=540:hideplayer"));
                }
            }
            finally { End(); }
            return "sheet " + name + " " + n + " stills";
        }

        static string Pose(string name, float t)
        {
            try
            {
                Begin(name, _ => null);
                Sample(t, 1f / 30f, out var eye, out var rotation, out float fov);
                var look = eye + rotation * Vector3.forward * 20f;
                string F(Vector3 v) => v.x.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "," + v.y.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "," + v.z.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                return "pose " + name + " t=" + t + " eye=" + F(eye) + " look=" + F(look) + " fov=" + fov.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception e) when (e.Message.Contains("rides")) { return "pose " + name + ": a riding shot has no pose outside Play (" + e.Message + ")"; }
            finally { End(); }
        }
    }
}
