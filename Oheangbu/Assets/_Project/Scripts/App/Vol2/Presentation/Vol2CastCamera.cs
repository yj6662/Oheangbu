using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Drawing;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App
{
    /// <summary>D308-150 cast camera treatments. V12 (lab prototype): Frame (A) eases the eye up / back / right with a pitch down, Fade (B) dithers the caster
    /// while it covers the brush-to-target line, Widen (C) a wider field of view and a nod down, Cine (D) a short three-quarter side view.
    /// V13 (2026-10-26, the user: "추천대로 진행" = A softened as the default): A at about half the V12 distances on one spring weight that holds across
    /// rapid casts, with the rig's boom, ON for every vol.2 scope (Legacy308 never makes a director). B / C / D stay switches. None = the game as it was.</summary>
    [Flags] public enum Vol2CastCamTreatment { None = 0, Frame = 1, Fade = 2, Widen = 4, Cine = 8 }

    /// <summary>Auto = a director exists wherever a vol.2 scope runs (Vol2ShapeRuntime.CreateForScope makes it); On / Off force it. Reset to Auto when Play starts.</summary>
    public enum Vol2CastCameraMode { Auto = 0, On = 1, Off = 2 }

    public struct Vol2CastCamPose
    {
        public Vector3 Position; public Quaternion Rotation; public float Fov;
        public Vol2CastCamPose(Vector3 position, Quaternion rotation, float fov) { Position = position; Rotation = rotation; Fov = fov; }
    }

    /// <summary>The treatments as pure functions: B / C / D of the cast's age (seconds after the commit on the scope clock), A of its weight (0 - 1, the
    /// director's spring). No physics here (the boom is the director's).</summary>
    public static class Vol2CastCameraMath
    {
        public static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>0 -> 1 over [0, in], 1 until holdUntil, 1 -> 0 over [holdUntil, holdUntil + out] (smoothstep both ways: no velocity step at either end).</summary>
        public static float Envelope(float age, float inSeconds, float holdUntil, float outSeconds)
        {
            if (age < 0f) return 0f;
            float up = inSeconds > 1e-4f ? Smooth(age / inSeconds) : 1f;
            float down = age <= holdUntil ? 1f : outSeconds > 1e-4f ? 1f - Smooth((age - holdUntil) / outSeconds) : 0f;
            return Mathf.Min(up, down);
        }

        public static float SignedPitch(Quaternion r) { float x = r.eulerAngles.x; return x > 180f ? x - 360f : x; }

        /// <summary>A at weight w, ADDED to the base pose: the eye moves by the offset (camera yaw frame) x strength x w, the view turns by the yaw about the
        /// world up and pitches down in its own frame (both x w) - a nod or roll the base already carries is kept. Linear in w: the eye's speed is the
        /// weight's rate x |offset|.</summary>
        public static Vol2CastCamPose Frame(Vol2CastCameraProfile p, Vol2CastCamPose b, float w)
        {
            if (w <= 0f) return b;
            float s = Mathf.Max(0f, p.FrameStrength);
            float baseYaw = b.Rotation.eulerAngles.y, basePitch = SignedPitch(b.Rotation);
            float pitchTarget = Mathf.Min(basePitch + p.FramePitchAdd * s, Mathf.Max(basePitch, p.FramePitchMax));
            float pitchAdd = pitchTarget - basePitch;
            var o = b;
            o.Position = b.Position + Quaternion.Euler(0f, baseYaw, 0f) * (p.FrameOffset * s) * w;
            o.Rotation = Quaternion.AngleAxis(p.FrameYawAdd * s * w, Vector3.up) * b.Rotation * Quaternion.Euler(pitchAdd * w, 0f, 0f);
            o.Fov = b.Fov + p.FrameFovAdd * s * w;
            return o;
        }

        /// <summary>The pose for this frame: base = what the camera would show without the treatment. Cine wins over Frame; Widen adds on top of either.
        /// frameWeight = A's weight (the director's spring or, AgeEnvelope, the V12 envelope).</summary>
        public static Vol2CastCamPose Evaluate(Vol2CastCamTreatment t, Vol2CastCameraProfile p, Vol2CastCamPose b, float age, float frameWeight, Vector3 caster, Vector3 forward, Vector3 focus, out float weight)
        {
            weight = 0f;
            var o = b;
            Vector3 f = forward; f.y = 0f; f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            float baseYaw = b.Rotation.eulerAngles.y;
            Quaternion yawFrame = Quaternion.Euler(0f, baseYaw, 0f);
            if ((t & Vol2CastCamTreatment.Cine) != 0)
            {
                float w = Envelope(age, p.CineIn, p.CineHoldUntil, p.CineOut);
                if (w > 0f)
                {
                    Vector3 side = yawFrame * Vector3.right;
                    Vector3 eye = caster + f * p.CineAlong + side * p.CineSide + Vector3.up * p.CineHeight;
                    Vector3 groundFocus = new Vector3(focus.x, caster.y, focus.z);
                    Vector3 look = Vector3.Lerp(caster, groundFocus, p.CineLookAlong) + Vector3.up * p.CineLookHeight;
                    Quaternion rot = Quaternion.LookRotation(look - eye, Vector3.up);
                    o.Position = Vector3.Lerp(b.Position, eye, w);
                    o.Rotation = Quaternion.Slerp(b.Rotation, rot, w);
                    o.Fov = Mathf.Lerp(b.Fov, p.CineFov, w);
                    weight = Mathf.Max(weight, w);
                }
            }
            else if ((t & Vol2CastCamTreatment.Frame) != 0 && frameWeight > 0f)
            {
                o = Frame(p, b, Mathf.Clamp01(frameWeight));
                weight = Mathf.Max(weight, frameWeight);
            }
            if ((t & Vol2CastCamTreatment.Widen) != 0)
            {
                float w = Envelope(age, p.WidenIn, p.WidenHoldUntil, p.WidenOut);
                if (w > 0f)
                {
                    o.Rotation = o.Rotation * Quaternion.Euler(p.WidenPitchAdd * w, 0f, 0f);    // a nod in the camera's own frame (the render offset's shape)
                    o.Fov += p.WidenFovAdd * w;
                    o.Position -= yawFrame * Vector3.forward * (p.WidenBack * w);
                    weight = Mathf.Max(weight, w);
                }
            }
            return o;
        }

        /// <summary>A's boom: the largest share f of the offset d (from the base eye b) whose eye the pivot can see with a sphere of radius r - the same
        /// SphereCast the rig's own boom makes (pivot -> eye, default layers, triggers ignored), so the framed eye is never in or behind a wall. 1 = clear.</summary>
        public static float BoomFraction(Vector3 pivot, Vector3 b, Vector3 d, float r)
        {
            if (d.sqrMagnitude < 1e-8f) return 1f;
            if (Clear(pivot, b + d, r)) return 1f;
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 6; i++) { float mid = (lo + hi) * .5f; if (Clear(pivot, b + d * mid, r)) lo = mid; else hi = mid; }
            return lo;
        }

        static bool Clear(Vector3 pivot, Vector3 eye, float r)
        {
            Vector3 dir = eye - pivot; float len = dir.magnitude;
            if (len < 1e-3f) return true;
            return !Physics.SphereCast(pivot, r, dir / len, out _, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>B: how much of the brush-to-target line (viewport points) lies inside the caster's screen box; samples evenly spaced, both ends included.</summary>
        public static float Cover(Rect casterBox, IList<Vector3> lineViewport)
        {
            if (lineViewport == null || lineViewport.Count == 0) return 0f;
            int inside = 0, valid = 0;
            foreach (var v in lineViewport)
            {
                if (v.z <= 0f) continue;
                valid++;
                if (casterBox.Contains(new Vector2(v.x, v.y))) inside++;
            }
            return valid > 0 ? inside / (float)valid : 0f;
        }

        /// <summary>B: the caster's visibility for this frame (1 = whole).</summary>
        public static float Visibility(Vol2CastCameraProfile p, float age, float cover)
        {
            float w = Envelope(age, p.FadeIn, p.FadeHoldUntil, p.FadeOut);
            float strength = p.FadeFullCover > 1e-4f ? Mathf.Clamp01(cover / p.FadeFullCover) : (cover > 0f ? 1f : 0f);
            return Mathf.Lerp(1f, p.FadeMinVisibility, w * strength);
        }

        /// <summary>Letters A B C D (any order, '+' allowed); "off" / "none" / "0" / "A0" = None (A0 = A switched off, for comparison).</summary>
        public static Vol2CastCamTreatment Parse(string s)
        {
            s = (s ?? "").Trim().ToUpperInvariant();
            if (s == "" || s == "OFF" || s == "NONE" || s == "0" || s == "A0") return Vol2CastCamTreatment.None;
            var t = Vol2CastCamTreatment.None;
            foreach (char c in s)
            {
                if (c == 'A') t |= Vol2CastCamTreatment.Frame;
                else if (c == 'B') t |= Vol2CastCamTreatment.Fade;
                else if (c == 'C') t |= Vol2CastCamTreatment.Widen;
                else if (c == 'D') t |= Vol2CastCamTreatment.Cine;
                else if (c != '+' && c != ' ') throw new ArgumentException("treatment letters A B C D (or off / A0): " + s);
            }
            return t;
        }

        public static string Letters(Vol2CastCamTreatment t)
        {
            if (t == Vol2CastCamTreatment.None) return "off";
            string s = "";
            if ((t & Vol2CastCamTreatment.Frame) != 0) s += "A";
            if ((t & Vol2CastCamTreatment.Fade) != 0) s += "B";
            if ((t & Vol2CastCamTreatment.Widen) != 0) s += "C";
            if ((t & Vol2CastCamTreatment.Cine) != 0) s += "D";
            return s;
        }
    }

    /// <summary>One aim probe sample (castcam:aim writes them): what the aim function saw at one phase of the frame right after a framed render.</summary>
    public struct Vol2CastCamAimSample
    {
        public int Frame; public int Site; public Vector3 RayOrigin, RayDir, PosedOrigin, BaseOrigin; public bool SawPosed, AtRigPose; public float Weight;
    }

    /// <summary>Applies the chosen treatment to the casts of every vol.2 view of the scene. Made by Vol2ShapeRuntime.CreateForScope for every vol.2
    /// scope (V13; Mode Auto), under the scope's host; the treatment is A (Frame) unless the editor command castcam:A0|off|B|C|D|AB changes it (back to A
    /// when Play starts). Two cameras: the game camera (Camera.main) gets its pose here in LateUpdate (after every Update and LateUpdate that aims, places
    /// the arm or the HUD) and loses it at the start of the next frame (PlayerLoopTiming.Initialization, before input, FixedUpdate and every Update; the
    /// Vol2CastCameraRestore partner at order -32000 is the second lock) - aiming, lock-on and the drawing input never see the moved eye. The vol.2
    /// capture camera (named CaptureCameraName, rendered from the editor between frames) gets it on its own begin / end camera rendering. B (the caster
    /// fade) swaps the caster's materials to a dither ghost only inside one camera's render. Never while drawing (the same refusals as the rig's render
    /// offset and the juice: draw presentation, draw input mode, a live stroke, the get-up camera). Not a singleton.</summary>
    [DefaultExecutionOrder(32000)]
    public sealed class Vol2CastCameraDirector : MonoBehaviour
    {
        public const string CaptureCameraName = "Vol2Vfx_Cam";
        const string GhostShader = "Oheangbu/Vol2/CasterDither";
        const int AimRing = 512;

        static Vol2CastCameraMode _mode;
        static Vol2CastCamTreatment _treatment;
        public static Vol2CastCameraMode Mode { get => _mode; set => _mode = value; }
        public static Vol2CastCamTreatment Treatment { get => _treatment; set => _treatment = value; }
        /// <summary>V13: what Play starts with.</summary>
        public const Vol2CastCamTreatment DefaultTreatment = Vol2CastCamTreatment.Frame;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _mode = Vol2CastCameraMode.Auto; _treatment = DefaultTreatment; }

        /// <summary>A scene that already holds a vol.2 view when it finishes loading (the scope hook below is the usual way in).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap() { Ensure(); }

        /// <summary>Makes the director (with its restore partner) if the mode allows one and there is none. scopeHost != null = called by a vol.2 scope
        /// (Vol2ShapeRuntime.CreateForScope): the director lives under that host and dies with the scope. Returns it or null. Play only.</summary>
        public static Vol2CastCameraDirector Ensure(Transform scopeHost = null)
        {
            if (!Application.isPlaying || _mode == Vol2CastCameraMode.Off) return null;
            if (_mode == Vol2CastCameraMode.Auto && scopeHost == null && FindFirstObjectByType<Vol2VfxPresenter>() == null) return null;
            var d = FindFirstObjectByType<Vol2CastCameraDirector>();
            if (d != null) return d;
            var go = new GameObject("Vol2CastCameraDirector");
            if (scopeHost != null) go.transform.SetParent(scopeHost, false);
            go.AddComponent<Vol2CastCameraRestore>();
            return go.AddComponent<Vol2CastCameraDirector>();
        }

        sealed class Active
        {
            public Vol2CastLog Log; public Vol2CastCoordinator Coordinator; public IVol2Target Target; public Vector3 Caster, Forward, FallbackFocus;
            public readonly List<Renderer> Body = new List<Renderer>();
            public float End;
        }

        Vol2CastCameraProfile _profile;
        readonly HashSet<Vol2CastCoordinator> _subscribed = new HashSet<Vol2CastCoordinator>();
        readonly List<Vol2CastCoordinator> _coordinators = new List<Vol2CastCoordinator>();
        Action<Vol2CastLog>[] _handlers = new Action<Vol2CastLog>[0];
        Active _active;
        float _nextScan, _nextFind;
        Action<ScriptableRenderContext, Camera> _begin, _end;
        CancellationTokenSource _cts;
        // the drawing refusals (found lazily, searched again every 2 s while missing)
        CameraRigController _rig; DrawingInputController _drawInput; PlayerMotor _motor; CombatLoopWiring _wiring; WorldMacroPlayerGetUpCamera303 _getUp;

        // A: one weight for every camera, stepped once per frame (LateUpdate) on the rig's clock; the hold is on the scope clock of the latest cast
        float _w, _wVel;
        bool _hasCommit; long _lastCommitUs; Vol2CastCoordinator _commitClock; Vector3 _lastCaster;
        float _boomGame = 1f;

        // the game camera pose put in LateUpdate and taken off at the next frame's start
        bool _gameApplied; Transform _gameCam; Camera _gameCamera;
        Vector3 _savedLocalPos; Quaternion _savedLocalRot; float _savedFov;
        Vector3 _writtenLocalPos; Quaternion _writtenLocalRot; float _writtenFov;
        // inside one camera render
        Camera _posedCam; Vector3 _capPos; Quaternion _capRot; float _capFov;
        readonly List<(Renderer r, Material[] mats)> _swapped = new List<(Renderer, Material[])>();
        readonly Dictionary<Material, Material> _ghosts = new Dictionary<Material, Material>();
        readonly List<Vector3> _line = new List<Vector3>();
        Shader _ghostShader;

        // diagnostics (castcam:status)
        public Vol2CastCameraProfile Profile => _profile;
        public int CastsSeen { get; private set; }
        public int GamePosed { get; private set; }
        public int CapturePosed { get; private set; }
        public int Faded { get; private set; }
        public int RestoreMismatch { get; private set; }
        public int RefusedDrawing { get; private set; }
        public string LastRefusal { get; private set; } = "";
        public float LastWeight { get; private set; }
        public float LastVisibility { get; private set; } = 1f;
        public float LastCover { get; private set; }
        public float LastAge { get; private set; } = -1f;
        public string LastFocus { get; private set; } = "";
        public int Coordinators => _subscribed.Count;
        public string GhostProblem { get; private set; } = "";
        public int BodyRenderers => _active != null ? _active.Body.Count : -1;
        public string BodyNames => _active == null ? "" : string.Join(", ", _active.Body.ConvertAll(r => r != null ? r.name + "[" + (r.sharedMaterial != null && r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "-") + "]" : "null"));
        // V13 A
        public float FrameWeightNow => FrameWeight();
        public float FrameRate => _wVel;
        /// <summary>Casts that came while A was already up (> 0.05): the framing was extended, no new swing started.</summary>
        public int ChainedCasts { get; private set; }
        /// <summary>Casts that started A from (near) rest.</summary>
        public int FreshSwings { get; private set; }
        /// <summary>Frames where A's return turned back up (a cast during the return) - smooth, counted to see it.</summary>
        public int Reversals { get; private set; }
        /// <summary>V12 shape only: casts that restarted the envelope while it was up (the ping-pong the spring removes).</summary>
        public int EnvelopeRestarts { get; private set; }
        public float PeakRate { get; private set; }
        /// <summary>The A-made motion of the game camera relative to the rig's pose (m/s, deg/s), frame to frame.</summary>
        public float PeakEyeSpeed { get; private set; }
        public float PeakTurnRate { get; private set; }
        public float PeakWeight { get; private set; }
        public int BoomClamped { get; private set; }
        public float MinBoom { get; private set; } = 1f;
        public float LastBoom { get; private set; } = 1f;
        public int EyeInside { get; private set; }
        public int RestoredAtFrameStart { get; private set; }
        public int RestoredInUpdate { get; private set; }
        readonly int[] _aimProbes = new int[3], _aimSawPosed = new int[3], _aimAtRig = new int[3];
        readonly float[] _aimMaxSep = new float[3], _aimMaxDev = new float[3];
        readonly Vol2CastCamAimSample[] _aim = new Vol2CastCamAimSample[AimRing];
        int _aimCount;
        int _lastPosedFrame = -10; Vector3 _lastPosedOrigin, _lastBaseOrigin; float _lastPosedWeight;
        Vector3 _prevOffset; Quaternion _prevRel = Quaternion.identity; int _prevOffsetFrame = -10;

        public string AimSummary()
        {
            string[] names = { "FixedUpdate", "Update", "LateUpdate" };
            var parts = new List<string>();
            for (int i = 0; i < 3; i++)
                parts.Add(names[i] + " probes=" + _aimProbes[i] + " sawPosed=" + _aimSawPosed[i] + (i < 2 ? " atRigPose=" + _aimAtRig[i] : "") + " maxSepFromPosed=" + _aimMaxSep[i].ToString("0.000") + " m"
                    + (i < 2 ? " maxDevFromRig=" + _aimMaxDev[i].ToString("0.0000") + " m" : ""));
            return string.Join(" | ", parts);
        }

        public Vol2CastCamAimSample[] AimSamples()
        {
            int n = Mathf.Min(_aimCount, AimRing);
            var outp = new Vol2CastCamAimSample[n];
            for (int i = 0; i < n; i++) outp[i] = _aim[(_aimCount - n + i) % AimRing];
            return outp;
        }

        public void ResetDiagnostics()
        {
            CastsSeen = GamePosed = CapturePosed = Faded = RestoreMismatch = RefusedDrawing = 0;
            ChainedCasts = FreshSwings = Reversals = EnvelopeRestarts = BoomClamped = EyeInside = RestoredAtFrameStart = RestoredInUpdate = 0;
            PeakRate = PeakEyeSpeed = PeakTurnRate = PeakWeight = 0f; MinBoom = LastBoom = 1f; LastRefusal = "";
            Array.Clear(_aimProbes, 0, 3); Array.Clear(_aimSawPosed, 0, 3); Array.Clear(_aimAtRig, 0, 3); Array.Clear(_aimMaxSep, 0, 3); Array.Clear(_aimMaxDev, 0, 3); _aimCount = 0;
        }

        void Awake()
        {
            _profile = Vol2CastCameraProfile.LoadCopy();
            _begin = OnBeginCamera; _end = OnEndCamera;
        }

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += _begin; RenderPipelineManager.endCameraRendering += _end;
            _cts = new CancellationTokenSource();
            RestoreAtFrameStart(_cts.Token).Forget();
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= _begin; RenderPipelineManager.endCameraRendering -= _end;
            if (_cts != null) { _cts.Cancel(); _cts.Dispose(); _cts = null; }
            RestoreGame();
            RestoreMaterials();
        }

        void OnDestroy()
        {
            for (int i = 0; i < _coordinators.Count; i++) if (_coordinators[i] != null) _coordinators[i].CastBegan -= _handlers[i];
            _coordinators.Clear(); _subscribed.Clear();
            foreach (var g in _ghosts.Values) if (g != null) Destroy(g);
            _ghosts.Clear();
            if (_profile != null) Destroy(_profile);
        }

        /// <summary>The first thing of every frame (before input events, FixedUpdate and every Update): the game camera goes back to what the rig left.</summary>
        async UniTaskVoid RestoreAtFrameStart(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await UniTask.Yield(PlayerLoopTiming.Initialization, ct);
                    if (_gameApplied) { RestoreGame(); RestoredAtFrameStart++; }
                }
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>The profile copy is replaced (castcam:reload) - the asset is read again.</summary>
        public void Reload() { if (_profile != null) Destroy(_profile); _profile = Vol2CastCameraProfile.LoadCopy(); }

        void Update()
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + .25f;
                foreach (var p in FindObjectsByType<Vol2VfxPresenter>(FindObjectsSortMode.None))
                {
                    var c = p != null ? p.Coordinator : null;
                    if (c == null || _subscribed.Contains(c)) continue;
                    _subscribed.Add(c);
                    var coord = c;
                    Action<Vol2CastLog> h = log => OnCastBegan(coord, log);
                    c.CastBegan += h;
                    _coordinators.Add(c);
                    Array.Resize(ref _handlers, _coordinators.Count); _handlers[_handlers.Length - 1] = h;
                }
            }
            if (_active != null && Age(_active) > _active.End) _active = null;
        }

        void OnCastBegan(Vol2CastCoordinator coord, Vol2CastLog log)
        {
            if (log == null || log.Spec == null) return;
            CastsSeen++;
            float before = FrameWeight();
            if ((_treatment & Vol2CastCamTreatment.Frame) != 0)
            {
                if (_profile.FrameShape == Vol2FrameShape.Spring) { if (before > .05f) ChainedCasts++; else FreshSwings++; }
                else if (before > .05f) EnvelopeRestarts++;
            }
            _hasCommit = true; _lastCommitUs = log.StartUs; _commitClock = coord; _lastCaster = log.Spec.Origin;
            var a = new Active { Log = log, Coordinator = coord, Caster = log.Spec.Origin, Forward = log.Spec.Forward, End = _profile.EndOf((Vol2CastCamTreatment)15) + .1f };
            a.FallbackFocus = a.Caster + a.Forward * _profile.FocusNoTargetDistance;
            float best = float.MaxValue;
            var targets = coord.Targets;
            if (targets != null)
                foreach (var t in targets)
                {
                    if (t == null || !t.Alive) continue;
                    Vector3 d = t.Position - a.Caster; d.y = 0f;
                    float dist = d.magnitude;
                    if (dist < .3f || dist > _profile.FocusMaxDistance) continue;
                    if (Vector3.Angle(a.Forward, d) > _profile.FocusMaxAngle) continue;
                    if (dist < best) { best = dist; a.Target = t; }
                }
            var anchor = coord.Caster != null ? coord.Caster.Anchor : null;
            if (anchor != null)
                foreach (var r in anchor.GetComponentsInChildren<Renderer>(false))
                {
                    if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
                    if (!r.enabled) continue;
                    if (!_profile.FadeBrush && UnderBrush(r.transform, anchor)) continue;
                    a.Body.Add(r);
                }
            _active = a;
            LastFocus = a.Target != null ? a.Target.Name + " at " + best.ToString("0.0") + " m" : "none (point ahead)";
        }

        static bool UnderBrush(Transform t, Transform root)
        {
            for (var x = t; x != null && x != root; x = x.parent) if (x.name.IndexOf("Brush", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        float Age(Active a)
        {
            var clock = a.Coordinator != null && a.Coordinator.Services != null ? a.Coordinator.Services.Clock : null;
            if (clock == null) return float.MaxValue;
            return (clock.NowUs - a.Log.StartUs) / 1000000f;
        }

        Vector3 Focus(Active a)
        {
            Vector3 p = a.Target != null && a.Target.Alive ? a.Target.Position : a.FallbackFocus;
            p.y = (a.Target != null ? p.y : a.Caster.y) + _profile.FocusHeight;
            return p;
        }

        // ---- the drawing refusals: the same set as the rig's render offset (draw presentation, draw input) and the juice (a live stroke, the get-up camera) ----

        /// <summary>Why the camera must not move this frame ("" = it may).</summary>
        string Refusal()
        {
            if (Time.unscaledTime >= _nextFind && (_rig == null || _drawInput == null || _motor == null || _wiring == null || _getUp == null))
            {
                _nextFind = Time.unscaledTime + 2f;
                if (_rig == null) _rig = FindFirstObjectByType<CameraRigController>();
                if (_drawInput == null) _drawInput = FindFirstObjectByType<DrawingInputController>();
                if (_motor == null) _motor = FindFirstObjectByType<PlayerMotor>();
                if (_wiring == null) _wiring = FindFirstObjectByType<CombatLoopWiring>();
                if (_getUp == null) _getUp = FindFirstObjectByType<WorldMacroPlayerGetUpCamera303>();
            }
            if (_rig != null && _rig.IsDrawingCloseup) return "draw close-up";
            if (_drawInput != null && _drawInput.InDrawMode) return "draw input";
            if (_motor != null && _motor.IsDrawing) return "motor drawing";
            var feed = _wiring != null ? _wiring.BrushAdapter308 : null;
            if (feed != null && feed.LiveStrokeCount308 > 0) return "live stroke";
            if (_getUp != null && _getUp.Active) return "get-up camera";
            return "";
        }

        // ---- A's weight ----

        float FrameWeight()
        {
            if ((_treatment & Vol2CastCamTreatment.Frame) == 0) return 0f;
            if (_profile.FrameShape == Vol2FrameShape.Spring) return _w;
            return _active != null ? Vol2CastCameraMath.Envelope(Age(_active), _profile.FrameIn, _profile.FrameHoldUntil, _profile.FrameOut) : 0f;
        }

        bool FrameHeld()
        {
            if (!_hasCommit || _commitClock == null || _commitClock.Services == null || _commitClock.Services.Clock == null) return false;
            float since = (_commitClock.Services.Clock.NowUs - _lastCommitUs) / 1000000f;
            return since >= 0f && since < _profile.FrameHoldUntil;
        }

        void StepFrame(float dt, bool refused)
        {
            if ((_treatment & Vol2CastCamTreatment.Frame) == 0 || _profile.FrameShape != Vol2FrameShape.Spring || refused) { _w = 0f; _wVel = 0f; return; }
            float target = FrameHeld() ? 1f : 0f;
            if (target == 0f && _w <= 0f) { _wVel = 0f; return; }
            float prev = _wVel;
            float smooth = Mathf.Max(.01f, target > _w ? _profile.FrameRiseSmooth : _profile.FrameReturnSmooth);
            _w = Mathf.SmoothDamp(_w, target, ref _wVel, smooth, Mathf.Max(.01f, _profile.FrameMaxRate), dt);
            if (_w > 1f) { _w = 1f; _wVel = Mathf.Min(_wVel, 0f); }
            if (_w < 0f) { _w = 0f; _wVel = Mathf.Max(_wVel, 0f); }
            if (target == 0f && _w < 1e-3f && Mathf.Abs(_wVel) < 1e-2f) { _w = 0f; _wVel = 0f; }
            if (prev < -1e-3f && _wVel > 1e-3f) Reversals++;
            PeakRate = Mathf.Max(PeakRate, Mathf.Abs(_wVel));
            PeakWeight = Mathf.Max(PeakWeight, _w);
        }

        const Vol2CastCamTreatment PoseFlags = Vol2CastCamTreatment.Frame | Vol2CastCamTreatment.Widen | Vol2CastCamTreatment.Cine;

        bool PoseFor(Camera cam, bool game, float dt, out Vol2CastCamPose pose)
        {
            pose = default;
            var t = _treatment & PoseFlags;
            if (t == Vol2CastCamTreatment.None || cam == null) return false;
            float frameW = FrameWeight();
            if (_active == null && frameW <= 0f) return false;
            float age = _active != null ? Age(_active) : float.MaxValue; LastAge = _active != null ? age : -1f;
            var b = new Vol2CastCamPose(cam.transform.position, cam.transform.rotation, cam.fieldOfView);
            Vector3 caster = _active != null ? _active.Caster : _lastCaster, fwd = _active != null ? _active.Forward : cam.transform.forward;
            Vector3 focus = _active != null ? Focus(_active) : caster + fwd * _profile.FocusNoTargetDistance;
            pose = Vol2CastCameraMath.Evaluate(t, _profile, b, age, frameW, caster, fwd, focus, out float w);
            LastWeight = w;
            if (w <= 0f) return false;
            if (_profile.FrameBoom && frameW > 0f && (t & Vol2CastCamTreatment.Frame) != 0 && (t & Vol2CastCamTreatment.Cine) == 0)
            {
                // the rig's boom from its pivot (the camera's parent) - or, for a camera without one, the capture rig's pivot above the caster
                Transform parent = cam.transform.parent;
                Vector3 pivot = game && parent != null ? parent.position : caster + Vector3.up * _profile.BoomPivotHeight;
                Vector3 d = pose.Position - b.Position;
                float f = Vol2CastCameraMath.BoomFraction(pivot, b.Position, d, _profile.BoomRadius);
                if (game)
                {
                    _boomGame = f < _boomGame ? f : Mathf.Lerp(_boomGame, f, 1f - Mathf.Exp(-_profile.BoomReleaseRate * dt));
                    f = Mathf.Min(_boomGame, f);
                    LastBoom = f; MinBoom = Mathf.Min(MinBoom, f);
                    if (f < .999f) BoomClamped++;
                }
                pose.Position = b.Position + d * f;
                if (game && Physics.CheckSphere(pose.Position, _profile.BoomRadius * .9f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) EyeInside++;
            }
            return true;
        }

        // ---- the game camera: LateUpdate on, the next frame's start off ----

        void LateUpdate()
        {
            if (_gameApplied) RestoreGame();       // never stack
            float dt = Time.captureDeltaTime > 0f ? Time.captureDeltaTime : Time.unscaledDeltaTime;
            dt = Mathf.Clamp(dt, 0f, .1f);
            string refusal = Refusal();
            Probe(2);
            StepFrame(dt, refusal != "");
            var cam = Camera.main;
            if (cam == null || (_treatment & PoseFlags) == Vol2CastCamTreatment.None) { _boomGame = 1f; return; }
            if (refusal != "") { if (_active != null) { RefusedDrawing++; LastRefusal = refusal; } _boomGame = 1f; return; }
            if (!PoseFor(cam, true, dt, out var pose)) { _boomGame = 1f; _prevOffsetFrame = -10; return; }
            _gameCamera = cam; _gameCam = cam.transform;
            _savedLocalPos = _gameCam.localPosition; _savedLocalRot = _gameCam.localRotation; _savedFov = cam.fieldOfView;
            Vector3 basePos = _gameCam.position; Quaternion baseRot = _gameCam.rotation;
            Vector2 center = cam.pixelRect.center;
            _lastBaseOrigin = BrushStrokeFeedAdapter.ScreenPointToRayPrecise308(cam, center).origin;
            _gameCam.SetPositionAndRotation(pose.Position, pose.Rotation);
            cam.fieldOfView = pose.Fov;
            _writtenLocalPos = _gameCam.localPosition; _writtenLocalRot = _gameCam.localRotation; _writtenFov = cam.fieldOfView;
            _lastPosedOrigin = BrushStrokeFeedAdapter.ScreenPointToRayPrecise308(cam, center).origin;
            _lastPosedFrame = Time.frameCount; _lastPosedWeight = LastWeight;
            _gameApplied = true; GamePosed++;
            // the A-made motion (pose relative to the rig's pose), frame to frame
            Vector3 off = Quaternion.Inverse(baseRot) * (pose.Position - basePos);
            Quaternion rel = Quaternion.Inverse(baseRot) * pose.Rotation;
            if (_prevOffsetFrame == Time.frameCount - 1 && dt > 1e-5f)
            {
                PeakEyeSpeed = Mathf.Max(PeakEyeSpeed, (off - _prevOffset).magnitude / dt);
                PeakTurnRate = Mathf.Max(PeakTurnRate, Quaternion.Angle(_prevRel, rel) / dt);
            }
            _prevOffset = off; _prevRel = rel; _prevOffsetFrame = Time.frameCount;
        }

        /// <summary>The game camera goes back to what the rig left (assigned). Called at the frame's start, by the partner's Update and before a new pose.</summary>
        public void RestoreGame()
        {
            if (!_gameApplied) return;
            _gameApplied = false;
            if (_gameCam == null || _gameCamera == null) return;
            if (_gameCam.localPosition != _writtenLocalPos || _gameCam.localRotation != _writtenLocalRot || _gameCamera.fieldOfView != _writtenFov) RestoreMismatch++;
            _gameCam.localPosition = _savedLocalPos; _gameCam.localRotation = _savedLocalRot; _gameCamera.fieldOfView = _savedFov;
        }

        /// <summary>The partner's Update: the second lock (the frame-start restore normally ran already).</summary>
        public void RestoreInUpdate() { if (_gameApplied) { RestoreGame(); RestoredInUpdate++; } }

        /// <summary>Aim probe (site 0 FixedUpdate at order -32000, 1 Update at order -32000, 2 LateUpdate at order 32000 before the new pose): on the frame
        /// after a framed render, the aim function the gesture rig uses (BrushStrokeFeedAdapter.ScreenPointToRayPrecise308, screen centre) on Camera.main.
        /// sawPosed = the camera was still at the framed eye (must stay 0); atRigPose (sites 0 - 1, before the rig's Update) = exactly at the rig's pose.</summary>
        public void Probe(int site)
        {
            if (_lastPosedFrame != Time.frameCount - 1 || _gameCamera == null || _gameCam == null) return;
            var cam = _gameCamera;
            Ray r = BrushStrokeFeedAdapter.ScreenPointToRayPrecise308(cam, cam.pixelRect.center);
            bool sawPosed = _gameCam.localPosition == _writtenLocalPos && _gameCam.localRotation == _writtenLocalRot && (_writtenLocalPos - _savedLocalPos).sqrMagnitude > 1e-8f;
            bool atRig = _gameCam.localPosition == _savedLocalPos && _gameCam.localRotation == _savedLocalRot;
            _aimProbes[site]++;
            if (sawPosed) _aimSawPosed[site]++;
            if (atRig) _aimAtRig[site]++;
            _aimMaxSep[site] = Mathf.Max(_aimMaxSep[site], (r.origin - _lastPosedOrigin).magnitude);
            if (site < 2) _aimMaxDev[site] = Mathf.Max(_aimMaxDev[site], (r.origin - _lastBaseOrigin).magnitude);
            _aim[_aimCount % AimRing] = new Vol2CastCamAimSample { Frame = Time.frameCount, Site = site, RayOrigin = r.origin, RayDir = r.direction, PosedOrigin = _lastPosedOrigin, BaseOrigin = _lastBaseOrigin,
                SawPosed = sawPosed, AtRigPose = atRig, Weight = _lastPosedWeight };
            _aimCount++;
        }

        // ---- inside one camera's render: the capture camera's pose, and the caster fade for either camera ----

        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null || cam.cameraType != CameraType.Game || _treatment == Vol2CastCamTreatment.None) return;
            bool capture = cam.name == CaptureCameraName;
            if (!capture && cam != Camera.main) return;
            if (capture && PoseFor(cam, false, 0f, out var pose))
            {
                _posedCam = cam; _capPos = cam.transform.position; _capRot = cam.transform.rotation; _capFov = cam.fieldOfView;
                cam.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
                cam.fieldOfView = pose.Fov;
                CapturePosed++;
            }
            if (_active != null && (_treatment & Vol2CastCamTreatment.Fade) != 0 && (capture || Refusal() == "")) FadeFor(cam);
        }

        void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            RestoreMaterials();
            if (_posedCam != null && cam == _posedCam)
            {
                cam.transform.SetPositionAndRotation(_capPos, _capRot);
                cam.fieldOfView = _capFov;
                _posedCam = null;
            }
        }

        void FadeFor(Camera cam)
        {
            var a = _active;
            if (a.Body.Count == 0) return;
            float age = Age(a);
            // the caster's screen box (its renderers' world bounds)
            Bounds bb = default; bool any = false;
            foreach (var r in a.Body) { if (r == null) continue; if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds); }
            if (!any) return;
            float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f; bool front = false;
            Vector3 c = bb.center, e = bb.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 w = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                Vector3 v = cam.WorldToViewportPoint(w);
                if (v.z <= 0f) continue;
                front = true;
                x0 = Mathf.Min(x0, v.x); y0 = Mathf.Min(y0, v.y); x1 = Mathf.Max(x1, v.x); y1 = Mathf.Max(y1, v.y);
            }
            if (!front) return;
            float m = _profile.FadeRectMargin;
            var box = Rect.MinMaxRect(x0 - m, y0 - m, x1 + m, y1 + m);
            Vector3 from = a.Caster + a.Forward * _profile.LineStartAhead + Vector3.up * _profile.LineStartHeight, to = Focus(a);
            _line.Clear();
            for (int i = 0; i <= 12; i++) _line.Add(cam.WorldToViewportPoint(Vector3.Lerp(from, to, i / 12f)));
            float cover = Vol2CastCameraMath.Cover(box, _line);
            float vis = Vol2CastCameraMath.Visibility(_profile, age, cover);
            LastCover = cover; LastVisibility = vis; LastAge = age;
            if (vis >= .999f) return;
            if (_ghostShader == null) { _ghostShader = Shader.Find(GhostShader); if (_ghostShader == null) { GhostProblem = "shader " + GhostShader + " not found"; return; } }
            foreach (var r in a.Body)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                var ghost = new Material[mats.Length];
                for (int k = 0; k < mats.Length; k++) { ghost[k] = Ghost(mats[k]); if (ghost[k] != null) ghost[k].SetFloat("_CastCamVisibility", vis); }
                _swapped.Add((r, mats));
                r.sharedMaterials = ghost;
            }
            Faded++;
        }

        Material Ghost(Material src)
        {
            if (src == null) return null;
            if (_ghosts.TryGetValue(src, out var g) && g != null) return g;
            g = new Material(_ghostShader) { name = src.name + " (cast fade)", hideFlags = HideFlags.DontSave };
            Texture tex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
            if (tex != null) g.SetTexture("_BaseMap", tex);
            if (src.HasProperty("_BaseMap")) { g.SetTextureScale("_BaseMap", src.GetTextureScale("_BaseMap")); g.SetTextureOffset("_BaseMap", src.GetTextureOffset("_BaseMap")); }
            Color col = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
            g.SetColor("_BaseColor", col);
            bool clipOn = src.IsKeywordEnabled("_ALPHATEST_ON") || src.renderQueue >= 2450 && src.renderQueue < 3000;
            g.SetFloat("_Cutoff", clipOn && src.HasProperty("_Cutoff") ? src.GetFloat("_Cutoff") : 0f);
            g.SetFloat("_Cull", src.HasProperty("_Cull") ? src.GetFloat("_Cull") : 2f);
            _ghosts[src] = g;
            return g;
        }

        void RestoreMaterials()
        {
            if (_swapped.Count == 0) return;
            foreach (var (r, mats) in _swapped) if (r != null) r.sharedMaterials = mats;
            _swapped.Clear();
        }
    }

    /// <summary>The partner of <see cref="Vol2CastCameraDirector"/>: the aim probe at the earliest FixedUpdate and Update of the frame, and the second lock
    /// (the game camera back to the rig's own pose before any other Update, should the frame-start restore not have run).</summary>
    [DefaultExecutionOrder(-32000)]
    public sealed class Vol2CastCameraRestore : MonoBehaviour
    {
        Vol2CastCameraDirector _d;
        void FixedUpdate()
        {
            if (_d == null) _d = GetComponent<Vol2CastCameraDirector>();
            if (_d != null) _d.Probe(0);
        }
        void Update()
        {
            if (_d == null) _d = GetComponent<Vol2CastCameraDirector>();
            if (_d == null) return;
            _d.Probe(1);
            _d.RestoreInUpdate();
        }
    }
}
