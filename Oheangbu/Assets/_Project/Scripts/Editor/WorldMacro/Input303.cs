using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Drawing;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>One Dynamic input update worth of virtual keyboard + mouse state.</summary>
    internal struct InputFrame303
    {
        public Key[] Keys; public Vector2 Position; public Vector2 Delta; public bool Lmb; public string Tag;
        public static InputFrame303 Neutral(Vector2 position) => new InputFrame303 { Keys = Array.Empty<Key>(), Position = position };
        public InputFrame303 WithKeys(params Key[] keys) { var f = this; f.Keys = keys ?? Array.Empty<Key>(); return f; }
        public bool Has(Key k) => Keys != null && Array.IndexOf(Keys, k) >= 0;
        public string KeyText => Keys == null || Keys.Length == 0 ? "" : string.Join("+", Keys.Select(k => k.ToString()));
    }

    /// <summary>Virtual keyboard/mouse through the real Input System path (Vfx120PlayerInputAudit method + 298 focus rules):
    /// two added devices, the existing action assets' device filters scoped to them (raw map filters restored exactly),
    /// IgnoreFocus background behaviour, and one queued frame consumed per Dynamic input update from onBeforeUpdate.
    /// Keyboard.current / Mouse.current are made current every update, so direct Keyboard.current reads (F interact,
    /// seat E/W/A/S/D/Space, summon G, the UI gate) see the same state as the actions. Physical devices stay enabled but are
    /// filtered out of the actions. Nothing here calls DrawingInputController, recognition, Commit or any gameplay API.</summary>
    internal static class VirtualInput303
    {
        public static bool Active { get; private set; }
        public static Keyboard Kb { get; private set; }
        public static Mouse Ms { get; private set; }
        public static InputFrame303 Hold;             // repeated while the queue is empty
        public static InputFrame303 LastFed;
        public static int Consumed, HeldFrames, Updates, LastFedFrame = -1;
        public static string UpdateMode = "";
        static readonly Queue<InputFrame303> queue = new Queue<InputFrame303>();
        static Keyboard priorKb; static Mouse priorMs;
        static InputSettings.BackgroundBehavior priorBackground; static InputSettings.EditorInputBehaviorInPlayMode priorEditor; static bool priorRunInBackground;
        static bool fixedMode, captured;
        sealed class MapFilter { public InputActionMap map; public InputDevice[] raw; public bool enabled; }
        sealed class AssetFilter { public InputActionAsset asset; public InputDevice[] raw; public readonly List<MapFilter> maps = new List<MapFilter>(); }
        static readonly List<AssetFilter> filters = new List<AssetFilter>();

        public static int Pending => queue.Count;
        public static void Enqueue(InputFrame303 f) => queue.Enqueue(f);
        public static void Enqueue(IEnumerable<InputFrame303> frames) { foreach (var f in frames) queue.Enqueue(f); }
        public static void ClearQueue() => queue.Clear();

        /// <summary>Press `key` for `frames` updates, then one release update (optionally with extra held keys).</summary>
        public static void Tap(Key key, Vector2 position, int frames = 2, params Key[] with)
        {
            var keys = new[] { key }.Concat(with ?? Array.Empty<Key>()).ToArray();
            for (int i = 0; i < frames; i++) queue.Enqueue(new InputFrame303 { Keys = keys, Position = position, Tag = "tap:" + key });
            queue.Enqueue(InputFrame303.Neutral(position));
        }

        public static string Begin(IEnumerable<InputActionAsset> assets)
        {
            if (Active) End(out _);
            queue.Clear(); Consumed = HeldFrames = Updates = 0; LastFedFrame = -1;
            priorKb = Keyboard.current; priorMs = Mouse.current;
            priorBackground = InputSystem.settings.backgroundBehavior; priorEditor = InputSystem.settings.editorInputBehaviorInPlayMode;
            priorRunInBackground = Application.runInBackground;
            UpdateMode = InputSystem.settings.updateMode.ToString();
            fixedMode = InputSystem.settings.updateMode == InputSettings.UpdateMode.ProcessEventsInFixedUpdate;
            filters.Clear();
            foreach (var asset in assets.Where(a => a != null).Distinct())
            {
                var af = new AssetFilter { asset = asset, raw = Snapshot(asset.devices) };
                foreach (var map in asset.actionMaps) af.maps.Add(new MapFilter { map = map, raw = RawMapDevices(map), enabled = map.enabled });
                filters.Add(af);
            }
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Application.runInBackground = true;
            Kb = InputSystem.AddDevice<Keyboard>("C303Keyboard"); Ms = InputSystem.AddDevice<Mouse>("C303Mouse");
            var scoped = new InputDevice[] { Kb, Ms };
            foreach (var af in filters) { af.asset.devices = scoped; foreach (var mf in af.maps) mf.map.devices = scoped; }
            captured = true;
            Hold = InputFrame303.Neutral(new Vector2(Screen.width * .5f, Screen.height * .5f));
            InputSystem.onBeforeUpdate -= Feed; InputSystem.onBeforeUpdate += Feed;
            Active = true;
            return "virtual devices " + Kb.deviceId + "/" + Ms.deviceId + ", " + filters.Count + " action asset(s) scoped, update mode " + UpdateMode;
        }

        static void Feed()
        {
            if (!Active || Kb == null || !Kb.added || Ms == null || !Ms.added) return;
            var t = InputState.currentUpdateType;
            if (!(t == InputUpdateType.Dynamic || fixedMode && t == InputUpdateType.Fixed)) return;
            InputFrame303 f;
            if (queue.Count > 0) { f = queue.Dequeue(); Consumed++; } else { f = Hold; HeldFrames++; }
            InputSystem.QueueStateEvent(Kb, new KeyboardState(f.Keys ?? Array.Empty<Key>()));
            InputSystem.QueueStateEvent(Ms, new MouseState { position = f.Position, delta = f.Delta }.WithButton(MouseButton.Left, f.Lmb));
            Kb.MakeCurrent(); Ms.MakeCurrent();
            LastFed = f; LastFedFrame = Time.frameCount; Updates++;
        }

        /// <summary>Removes the devices and puts filters/settings back. Returns false in `ok` when anything differs.</summary>
        public static string End(out bool ok)
        {
            InputSystem.onBeforeUpdate -= Feed;
            queue.Clear(); ok = true;
            if (!captured) { Active = false; return "virtual input was not active"; }
            var notes = new List<string>();
            try
            {
                foreach (var af in filters)
                {
                    if (af.asset == null) continue;
                    af.asset.devices = Nullable(af.raw);
                    foreach (var mf in af.maps) if (mf.map != null) mf.map.devices = Nullable(mf.raw);
                    string before = Ids(af.raw), after = Ids(Snapshot(af.asset.devices));
                    if (before != after) { ok = false; notes.Add(af.asset.name + " asset filter " + before + " -> " + after); }
                    foreach (var mf in af.maps)
                    {
                        if (mf.map == null) continue;
                        string rb = Ids(mf.raw), ra = Ids(RawMapDevices(mf.map));
                        if (rb != ra) { ok = false; notes.Add(af.asset.name + "/" + mf.map.name + " map filter " + rb + " -> " + ra); }
                    }
                }
            }
            catch (Exception e) { ok = false; notes.Add("filter revert: " + e.Message); }
            if (Kb != null && Kb.added) InputSystem.RemoveDevice(Kb);
            if (Ms != null && Ms.added) InputSystem.RemoveDevice(Ms);
            if (priorKb != null && priorKb.added) priorKb.MakeCurrent();
            if (priorMs != null && priorMs.added) priorMs.MakeCurrent();
            InputSystem.settings.backgroundBehavior = priorBackground; InputSystem.settings.editorInputBehaviorInPlayMode = priorEditor;
            Application.runInBackground = priorRunInBackground;
            bool devicesGone = (Kb == null || !Kb.added) && (Ms == null || !Ms.added);
            bool settings = InputSystem.settings.backgroundBehavior == priorBackground && InputSystem.settings.editorInputBehaviorInPlayMode == priorEditor && Application.runInBackground == priorRunInBackground;
            if (!devicesGone) { ok = false; notes.Add("virtual device still added"); }
            if (!settings) { ok = false; notes.Add("input settings differ"); }
            Kb = null; Ms = null; filters.Clear(); captured = false; Active = false;
            return (ok ? "input reverted" : "input revert FINDINGS: " + string.Join("; ", notes)) + " (consumed " + Consumed + ", held " + HeldFrames + ", updates " + Updates + ")";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetPlaySession()
        {
            // a Play session never inherits another session's virtual devices (domain reload is disabled)
            if (Active) End(out _);
            queue.Clear();
        }

        static InputDevice[] Snapshot(ReadOnlyArray<InputDevice>? devices) => devices.HasValue ? devices.Value.ToArray() : null;
        static ReadOnlyArray<InputDevice>? Nullable(InputDevice[] devices) => devices == null ? (ReadOnlyArray<InputDevice>?)null : new ReadOnlyArray<InputDevice>(devices);
        static string Ids(InputDevice[] devices) => devices == null ? "null" : "[" + string.Join(",", devices.Select(d => d.deviceId)) + "]";
        static InputDevice[] RawMapDevices(InputActionMap map)
        {
            // map.devices falls back to asset.devices; read the map's own nullable storage so an inherited filter is not
            // turned into a persistent map-local override on revert (Vfx120PlayerInputAudit.RawMapDevices)
            var field = typeof(InputActionMap).GetField("m_Devices", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException("InputActionMap.m_Devices unavailable in this Input System version");
            var storage = field.GetValue(map);
            var getter = storage.GetType().GetMethod("Get", BindingFlags.Instance | BindingFlags.Public)
                         ?? throw new InvalidOperationException("InputActionMap device storage getter unavailable");
            var value = getter.Invoke(storage, null);
            return value == null ? null : ((ReadOnlyArray<InputDevice>)value).ToArray();
        }
    }

    /// <summary>Mouse-look steering: delta = clamp(err / degreesPerUnit x 0.35, ±30) (Presentation297 AimDelta gain).</summary>
    internal static class Steer303
    {
        public const float MeasuredPerUnit = .1214f;   // Presentation297: 10 units = 1.21°
        public const float Gain = .35f;
        public static float PerUnit = MeasuredPerUnit;
        public static float ObservedPerUnit; public static int ObservedSamples;
        static float lastYaw; static bool haveLast;

        public static void Reset(float perUnit)
        {
            PerUnit = float.IsFinite(perUnit) && perUnit > .005f && perUnit < 5f ? perUnit : MeasuredPerUnit;
            ObservedPerUnit = 0; ObservedSamples = 0; haveLast = false;
        }
        public static Vector2 Delta(float yawError, float pitchError, bool invertY)
        {
            float dx = Mathf.Clamp(yawError / PerUnit * Gain, -30f, 30f);
            float dy = Mathf.Clamp(-pitchError / PerUnit * Gain, -30f, 30f);
            return new Vector2(dx, invertY ? -dy : dy);
        }
        /// <summary>Called once per frame after the motor: yaw change against the delta actually fed this frame.</summary>
        public static void Observe(float yaw, float fedDeltaX, bool valid)
        {
            if (haveLast && valid && Mathf.Abs(fedDeltaX) >= 3f)
            {
                float observed = Mathf.DeltaAngle(lastYaw, yaw) / fedDeltaX;
                if (observed > .005f && observed < 5f) { ObservedPerUnit = ObservedSamples == 0 ? observed : Mathf.Lerp(ObservedPerUnit, observed, .2f); ObservedSamples++; }
            }
            lastYaw = yaw; haveLast = true;
        }
        public static float CameraYaw(WorldMacroPlaytestSession s)
        {
            var cam = s.Walker.ViewCamera != null ? s.Walker.ViewCamera : Camera.main;
            return cam != null ? cam.transform.eulerAngles.y : s.Walker.Body.transform.eulerAngles.y;
        }
        public static float CameraPitch(WorldMacroPlaytestSession s)
        {
            var cam = s.Walker.ViewCamera != null ? s.Walker.ViewCamera : Camera.main;
            return cam != null ? Mathf.DeltaAngle(0f, cam.transform.eulerAngles.x) : 0f;
        }
        public static bool InvertY(WorldMacroPlaytestSession s) => s.Walker.Motor.RuntimeState != null && s.Walker.Motor.RuntimeState.InvertLookY;
        /// <summary>Delta that turns the view toward `point` (yaw + pitch, + pitch = down).</summary>
        public static Vector2 Toward(WorldMacroPlaytestSession s, Vector3 point, out float yawError)
        {
            var cam = s.Walker.ViewCamera != null ? s.Walker.ViewCamera : Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : s.Walker.Body.transform.position + Vector3.up * s.Walker.EyeHeight;
            Vector3 d = point - eye;
            float desiredYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float desiredPitch = Mathf.Atan2(-d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            yawError = Mathf.DeltaAngle(CameraYaw(s), desiredYaw);
            return Delta(yawError, desiredPitch - CameraPitch(s), InvertY(s));
        }
        public static Vector2 Level(WorldMacroPlaytestSession s, float yawError, float desiredPitch = 5f)
            => Delta(yawError, desiredPitch - CameraPitch(s), InvertY(s));
    }

    /// <summary>Walking autopilot: NavMesh corners, mouse-look steering, W (+ sprint on long straights), stall recovery
    /// (Space+W, A/D, S then repath) — plan §5.4. Produces the frame to hold; the motor itself moves the player.</summary>
    internal sealed class Walker303
    {
        public string Status = "idle", Detail = "", Label = "";
        public Vector3 Goal; public float Arrive = .6f, Limit = 240f, StartedReal, PathLength, Walked;
        public int Repaths, Recoveries; public bool AllowSprint = true;
        public string LastBlock = "";
        Vector3[] path = Array.Empty<Vector3>(); int next;
        float repathAt, progressAt, recoveryAt; Vector3 progressFrom, lastFeet; int recoveryStage = -1;
        readonly List<float> failures = new List<float>();
        int sprintCooldown;

        static Vector3 Feet(WorldMacroPlaytestSession s) => s.Walker.Body.transform.position;

        public bool Begin(WorldMacroPlaytestSession s, Vector3 goal, float arrive, float limit, string label)
        {
            Goal = goal; Arrive = arrive; Limit = limit; Label = label; StartedReal = Time.unscaledTime; Status = "walking"; Detail = "";
            Walked = 0; Repaths = 0; Recoveries = 0; failures.Clear(); recoveryStage = -1;
            lastFeet = progressFrom = Feet(s); progressAt = Time.unscaledTime;
            if (!Repath(s)) return false;
            PathLength = Length(path);
            return true;
        }

        public static bool TryPath(Vector3 from, Vector3 to, out Vector3[] corners, out string reason)
        {
            corners = Array.Empty<Vector3>(); reason = "";
            if (!NavMesh.SamplePosition(from, out var a, 2.5f, NavMesh.AllAreas)) { reason = "start off NavMesh " + Harness303.V(from); return false; }
            if (!NavMesh.SamplePosition(to, out var b, 2.5f, NavMesh.AllAreas)) { reason = "goal off NavMesh " + Harness303.V(to); return false; }
            var p = new NavMeshPath();
            if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, p)) { reason = "CalculatePath failed"; return false; }
            corners = p.corners;
            if (p.status != NavMeshPathStatus.PathComplete) { reason = "path " + p.status; return false; }
            return true;
        }
        public static float Length(Vector3[] c) { float l = 0; for (int i = 1; i < c.Length; i++) l += Vector3.Distance(c[i - 1], c[i]); return l; }

        bool Repath(WorldMacroPlaytestSession s)
        {
            if (!TryPath(Feet(s), Goal, out var corners, out var reason)) { Status = "failed"; Detail = "no path: " + reason; return false; }
            path = corners; next = Math.Min(1, path.Length - 1); Repaths++; repathAt = Time.unscaledTime + 4f;
            return true;
        }

        public InputFrame303 Tick(WorldMacroPlaytestSession s, Vector2 pointer)
        {
            var frame = InputFrame303.Neutral(pointer);
            if (Status != "walking") return frame;
            float now = Time.unscaledTime; var feet = Feet(s);
            Walked += Harness303.Flat(lastFeet, feet); lastFeet = feet;
            if (now - StartedReal > Limit) { Status = "failed"; Detail = "time limit " + Harness303.F(Limit, "F0") + " s"; return frame; }
            if (Harness303.Flat(feet, Goal) <= Arrive && Mathf.Abs(feet.y - Goal.y) < 2.5f) { Status = "arrived"; return frame; }
            if (path.Length < 2) { if (!Repath(s)) return frame; }
            while (next < path.Length - 1 && Harness303.Flat(feet, path[next]) < 1.2f) next++;
            if (now >= repathAt || SegmentDistance(feet, path[Math.Max(0, next - 1)], path[next]) > 3f) { if (!Repath(s)) return frame; }
            if (recoveryStage >= 0) return Recover(s, frame, now);
            if (now - progressAt >= 3f)
            {
                if (Harness303.Flat(feet, progressFrom) < .5f)
                {
                    failures.Add(now); failures.RemoveAll(t => now - t > 20f); Recoveries++;
                    if (failures.Count >= 3) { Status = "failed"; Detail = "stalled 3x in 20 s at " + Harness303.V(feet); LastBlock = Blocker(s); return frame; }
                    recoveryStage = 0; recoveryAt = now;
                    return Recover(s, frame, now);
                }
                progressFrom = feet; progressAt = now;
            }
            Vector3 target = path[next];
            float err = Mathf.DeltaAngle(Steer303.CameraYaw(s), Harness303.YawTo(feet, target));
            frame.Delta = Steer303.Level(s, err);
            var keys = new List<Key>();
            if (Mathf.Abs(err) < 30f) keys.Add(Key.W);
            bool wantSprint = AllowSprint && Mathf.Abs(err) < 12f && Harness303.Flat(feet, target) > 6f;
            SprintKey(s, wantSprint, keys);
            frame.Keys = keys.ToArray(); frame.Tag = "walk:" + Label;
            return frame;
        }

        void SprintKey(WorldMacroPlaytestSession s, bool want, List<Key> keys)
        {
            var m = s.Walker.Motor;
            if (!m.SprintToggles) { if (want) keys.Add(Key.LeftCtrl); return; }
            if (sprintCooldown > 0) { sprintCooldown--; return; }
            if (want != m.SprintLatched) { keys.Add(Key.LeftCtrl); sprintCooldown = 10; }
        }

        InputFrame303 Recover(WorldMacroPlaytestSession s, InputFrame303 frame, float now)
        {
            float t = now - recoveryAt;
            switch (recoveryStage)
            {
                case 0: if (t < .5f) { frame.Keys = new[] { Key.Space, Key.W }; frame.Tag = "recover:jump"; return frame; } recoveryStage = 1; recoveryAt = now; return frame;
                case 1: if (t < .6f) { frame.Keys = new[] { Key.A }; frame.Tag = "recover:left"; return frame; }
                        if (t < 1.2f) { frame.Keys = new[] { Key.D }; frame.Tag = "recover:right"; return frame; }
                        recoveryStage = 2; recoveryAt = now; return frame;
                case 2: if (t < .8f) { frame.Keys = new[] { Key.S }; frame.Tag = "recover:back"; return frame; }
                        recoveryStage = -1; progressFrom = Feet(s); progressAt = now; Repath(s); return frame;
            }
            recoveryStage = -1; return frame;
        }

        static string Blocker(WorldMacroPlaytestSession s)
        {
            var body = s.Walker.Body.transform; var p = body.position;
            var list = Physics.OverlapCapsule(p + Vector3.up * .31f, p + Vector3.up * 1.47f, .4f, ~0, QueryTriggerInteraction.Ignore)
                .Where(c => !c.transform.IsChildOf(body)).Select(c => Harness303.PathOf(c.transform)).Distinct().Take(6).ToList();
            if (Physics.Raycast(p + Vector3.up * .8f, body.forward, out var hit, 1.5f, ~0, QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(body))
                list.Add("ahead " + Harness303.PathOf(hit.transform) + " @" + Harness303.F(hit.distance, "F2"));
            return string.Join(" | ", list);
        }

        static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 P = new Vector2(p.x, p.z), A = new Vector2(a.x, a.z), B = new Vector2(b.x, b.z);
            Vector2 ab = B - A; float t = ab.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(P - A, ab) / ab.sqrMagnitude);
            return Vector2.Distance(P, A + ab * t);
        }
    }

    /// <summary>A replayable glyph: which registered templates, how densely each stroke is sampled, where on screen.</summary>
    [Serializable] internal sealed class StrokeRecipe303
    {
        public string letter = "", plan = "A", initialName = "", medialName = "", initialTemplate = "", medialTemplate = "", initialPath = "", medialPath = "";
        public int kInitial, kMedial, penUpFrames = 1, points, penUps, frames;
        public Rect initialRect = new Rect(.29f, .34f, .21f, .34f), medialRect = new Rect(.55f, .31f, .18f, .40f);
        public float basePower, enemyHp = 60, minimumBrush = .7f;
        public float brush30p10, brush30p50, brush45p10, brush60p10, nominalWorst1080, nominalWorst1440;
        public int shots30p10, shots45p10, shots60p10;
        public float recognitionRate;
        public string labRunUtc = "", book = "", library = "", gate = "";
    }

    /// <summary>Registered-template glyph replay: XML → corner-preserving resample → screen layout → frame script.
    /// The same geometry feeds the Edit-mode lab (offline RecognitionPipeline) and the Play queue (virtual mouse).</summary>
    internal static class Strokes303
    {
        public static TextAsset[] Templates(JamoTemplateLibrarySO library, string field) =>
            Harness303.FieldValue(library, field) as TextAsset[] ?? Array.Empty<TextAsset>();

        public static string TemplateName(TextAsset asset)
        {
            var xml = new XmlDocument(); xml.LoadXml(asset.text);
            return xml.DocumentElement?.GetAttribute("Name") ?? "";
        }

        public static List<List<Vector2>> Read(TextAsset asset)
        {
            var xml = new XmlDocument(); xml.LoadXml(asset.text);
            var strokes = new List<List<Vector2>>();
            foreach (XmlNode stroke in xml.SelectNodes("/Gesture/Stroke"))
            {
                var pts = new List<Vector2>();
                foreach (XmlNode p in stroke.SelectNodes("Point"))
                    pts.Add(new Vector2(float.Parse(p.Attributes["X"].Value, CultureInfo.InvariantCulture), float.Parse(p.Attributes["Y"].Value, CultureInfo.InvariantCulture)));
                if (pts.Count > 1) strokes.Add(pts);
            }
            return strokes;
        }

        /// <summary>Douglas–Peucker keeps the corners (epsilon in template units), arc length fills up to at least k points.</summary>
        public static List<Vector2> Resample(List<Vector2> pts, int k, float epsilon)
        {
            var keep = new bool[pts.Count]; keep[0] = keep[pts.Count - 1] = true;
            Dp(pts, 0, pts.Count - 1, epsilon, keep);
            var corners = pts.Where((p, i) => keep[i]).ToList();
            if (corners.Count >= k) return corners;
            var seg = new float[corners.Count - 1]; float total = 0;
            for (int i = 0; i < seg.Length; i++) { seg[i] = Vector2.Distance(corners[i], corners[i + 1]); total += seg[i]; }
            int extra = k - corners.Count;
            var add = new int[seg.Length]; float[] want = seg.Select(l => total > 0 ? l / total * extra : 0).ToArray();
            int given = 0; for (int i = 0; i < seg.Length; i++) { add[i] = Mathf.FloorToInt(want[i]); given += add[i]; }
            foreach (int i in Enumerable.Range(0, seg.Length).OrderByDescending(i => want[i] - add[i]).Take(extra - given)) add[i]++;
            var result = new List<Vector2>();
            for (int i = 0; i < seg.Length; i++)
            {
                result.Add(corners[i]);
                for (int j = 1; j <= add[i]; j++) result.Add(Vector2.Lerp(corners[i], corners[i + 1], j / (add[i] + 1f)));
            }
            result.Add(corners[corners.Count - 1]);
            return result;
        }

        static void Dp(List<Vector2> pts, int a, int b, float eps, bool[] keep)
        {
            if (b <= a + 1) return;
            float best = -1; int index = -1; Vector2 A = pts[a], B = pts[b], ab = B - A;
            for (int i = a + 1; i < b; i++)
            {
                float d = ab.sqrMagnitude < 1e-6f ? Vector2.Distance(pts[i], A) : Mathf.Abs(ab.x * (pts[i].y - A.y) - ab.y * (pts[i].x - A.x)) / ab.magnitude;
                if (d > best) { best = d; index = i; }
            }
            if (best > eps) { keep[index] = true; Dp(pts, a, index, eps, keep); Dp(pts, index, b, eps, keep); }
        }

        /// <summary>Template (y-down) → screen (y-up) inside a viewport rect, Vfx120PlayerInputAudit.AddJamo transform;
        /// each stroke resampled to k points with DP epsilon = 1.5% of the rect diagonal.</summary>
        public static List<List<Vector2>> Layout(List<List<Vector2>> strokes, Rect viewport, float width, float height, int k)
        {
            var all = strokes.SelectMany(x => x).ToArray();
            var min = new Vector2(all.Min(x => x.x), all.Min(x => x.y)); var max = new Vector2(all.Max(x => x.x), all.Max(x => x.y));
            var size = max - min;
            float scale = Mathf.Min(viewport.width * width / Mathf.Max(1, size.x), viewport.height * height / Mathf.Max(1, size.y));
            var origin = new Vector2(viewport.center.x * width - size.x * scale * .5f, viewport.center.y * height + size.y * scale * .5f);
            float eps = .015f * new Vector2(viewport.width * width, viewport.height * height).magnitude / Mathf.Max(1e-3f, scale);
            var result = new List<List<Vector2>>();
            foreach (var s in strokes)
                result.Add(Resample(s, k, eps).Select(p => origin + new Vector2(p.x - min.x, -(p.y - min.y)) * scale).ToList());
            return result;
        }

        public static List<List<Vector2>> Glyph(StrokeRecipe303 r, JamoTemplateLibrarySO library, float width, float height)
        {
            var ini = Templates(library, "_initials").FirstOrDefault(t => t != null && t.name == r.initialTemplate) ?? throw new InvalidOperationException("template missing " + r.initialTemplate);
            var med = Templates(library, "_medials").FirstOrDefault(t => t != null && t.name == r.medialTemplate) ?? throw new InvalidOperationException("template missing " + r.medialTemplate);
            return Layout(Read(ini), r.initialRect, width, height, r.kInitial).Concat(Layout(Read(med), r.medialRect, width, height, r.kMedial)).ToList();
        }

        /// <summary>Plan §5.3 frame script: [Q] 1 frame, per stroke LMB down + one point per frame, pen-up frame(s) with the
        /// pointer at the next start, then Q and LMB released together on the frame after the last point (commit).</summary>
        public static List<InputFrame303> Frames(List<List<Vector2>> strokes, int penUp, out int points, out int penUps)
        {
            var frames = new List<InputFrame303>(); points = 0; penUps = 0;
            var q = new[] { Key.Q };
            frames.Add(new InputFrame303 { Keys = q, Position = strokes[0][0], Tag = "draw:q" });
            for (int s = 0; s < strokes.Count; s++)
            {
                foreach (var p in strokes[s]) { frames.Add(new InputFrame303 { Keys = q, Position = p, Lmb = true, Tag = "draw:stroke" + s }); points++; }
                if (s < strokes.Count - 1)
                    for (int i = 0; i < Math.Max(1, penUp); i++) { frames.Add(new InputFrame303 { Keys = q, Position = strokes[s + 1][0], Tag = "draw:penup" }); penUps++; }
            }
            var last = strokes[strokes.Count - 1]; frames.Add(new InputFrame303 { Keys = Array.Empty<Key>(), Position = last[last.Count - 1], Tag = "draw:commit" });
            return frames;
        }

        /// <summary>What DrawingInputController would record from that script at `fps`: 2 px sample filter, unscaled times,
        /// optional noise (σ px) and point drop (the dropped frame still passes). Returns strokes and the stroke duration.</summary>
        public static List<StrokeData> Simulate(List<List<Vector2>> strokes, int penUp, float fps, float minPixels, System.Random random, float sigma, float drop, out float duration, out int recorded)
        {
            var result = new List<StrokeData>(); int frame = 1; float first = -1, lastTime = 0; recorded = 0;
            for (int s = 0; s < strokes.Count; s++)
            {
                var data = new StrokeData(); Vector2 lastSample = new Vector2(float.MinValue, 0);
                for (int i = 0; i < strokes[s].Count; i++, frame++)
                {
                    Vector2 p = strokes[s][i];
                    if (random != null)
                    {
                        bool edge = i == 0 || i == strokes[s].Count - 1;
                        if (!edge && random.NextDouble() < drop) continue;
                        p += new Vector2(Gauss(random), Gauss(random)) * sigma;
                    }
                    if (Vector2.Distance(p, lastSample) < minPixels) continue;
                    lastSample = p; float t = frame / fps;
                    data.Add(new StrokePoint(p, t)); recorded++;
                    if (first < 0) first = t; lastTime = t;
                }
                result.Add(data);
                if (s < strokes.Count - 1) frame += Math.Max(1, penUp);
            }
            duration = first < 0 ? 0 : lastTime - first;
            return result;
        }

        static float Gauss(System.Random r)
        {
            double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2));
        }

        /// <summary>Kills needed for `hp` at `power` per hit, counting an exact tie (floating point) as one more hit.</summary>
        public static int Shots(float hp, float power) => power <= 0 ? 999 : Mathf.CeilToInt((hp + .01f) / power);
    }

    /// <summary>Runs after the real motor, animation and session updates (execution order 32000): the harness phase machine
    /// and every Play-mode sample read here, not in EditorApplication.update (298 lesson).</summary>
    [DefaultExecutionOrder(32000)]
    public sealed class Probe303 : MonoBehaviour
    {
        internal static Action Late;
        void LateUpdate() { Late?.Invoke(); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetPlaySession() { Late = null; }
    }
}
