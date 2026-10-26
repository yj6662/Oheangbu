using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App
{
    /// <summary>D308-150 V12 cast camera treatments (LAB-ONLY prototype, TEST; the user picks one from the footage). Flags: Frame (A) eases the eye
    /// up / back / right with a pitch down, Fade (B) dithers the caster while it covers the brush-to-target line, Widen (C) a wider field of view and a nod
    /// down (nothing moves - the same shape as CameraRigController.SetRenderOffset308), Cine (D) a short three-quarter side view. None = the game as it is.</summary>
    [Flags] public enum Vol2CastCamTreatment { None = 0, Frame = 1, Fade = 2, Widen = 4, Cine = 8 }

    /// <summary>Auto = the director exists in the spell lab scene only (Vol2LabRequestFile.LabScenePath); On / Off force it. Reset to Auto when Play starts.</summary>
    public enum Vol2CastCameraMode { Auto = 0, On = 1, Off = 2 }

    public struct Vol2CastCamPose
    {
        public Vector3 Position; public Quaternion Rotation; public float Fov;
        public Vol2CastCamPose(Vector3 position, Quaternion rotation, float fov) { Position = position; Rotation = rotation; Fov = fov; }
    }

    /// <summary>The treatments as pure functions of the cast's age (seconds after the commit on the scope clock) - frame-exact in a 1/60 s capture
    /// and the same in the game; no accumulated state, so the return is exact and nothing can drift.</summary>
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

        /// <summary>The pose for this frame: base = what the camera would show without the treatment. Cine wins over Frame; Widen adds on top of either.</summary>
        public static Vol2CastCamPose Evaluate(Vol2CastCamTreatment t, Vol2CastCameraProfile p, Vol2CastCamPose b, float age, Vector3 caster, Vector3 forward, Vector3 focus, out float weight)
        {
            weight = 0f;
            var o = b;
            Vector3 f = forward; f.y = 0f; f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            float baseYaw = b.Rotation.eulerAngles.y, basePitch = SignedPitch(b.Rotation);
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
            else if ((t & Vol2CastCamTreatment.Frame) != 0)
            {
                float w = Envelope(age, p.FrameIn, p.FrameHoldUntil, p.FrameOut);
                if (w > 0f)
                {
                    Vector3 eye = b.Position + yawFrame * p.FrameOffset;
                    float pitch = Mathf.Min(basePitch + p.FramePitchAdd, Mathf.Max(basePitch, p.FramePitchMax));
                    Quaternion rot = Quaternion.Euler(pitch, baseYaw + p.FrameYawAdd, 0f);
                    o.Position = Vector3.Lerp(b.Position, eye, w);
                    o.Rotation = Quaternion.Slerp(b.Rotation, rot, w);
                    o.Fov = b.Fov + p.FrameFovAdd * w;
                    weight = Mathf.Max(weight, w);
                }
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

        public static Vol2CastCamTreatment Parse(string s)
        {
            s = (s ?? "").Trim().ToUpperInvariant();
            if (s == "" || s == "OFF" || s == "NONE" || s == "0") return Vol2CastCamTreatment.None;
            var t = Vol2CastCamTreatment.None;
            foreach (char c in s)
            {
                if (c == 'A') t |= Vol2CastCamTreatment.Frame;
                else if (c == 'B') t |= Vol2CastCamTreatment.Fade;
                else if (c == 'C') t |= Vol2CastCamTreatment.Widen;
                else if (c == 'D') t |= Vol2CastCamTreatment.Cine;
                else if (c != '+' && c != ' ') throw new ArgumentException("treatment letters A B C D (or off): " + s);
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

    /// <summary>Applies the chosen treatment to the casts of every vol.2 view of the scene. LAB-ONLY: made by the bootstrap below only when the spell
    /// lab is loaded (or Mode is forced On); the treatment is None until the editor command castcam:A|B|C|D sets it (reset to None when Play starts).
    /// Two cameras: the game camera (Camera.main) gets its pose here in LateUpdate (after every Update that aims and after the stroke adapter /
    /// gesture rig) and loses it at the start of the next frame (Vol2CastCameraRestore, order -32000) - aiming never sees the moved eye. The vol.2 capture
    /// camera (named CaptureCameraName, rendered from the editor between frames) gets it on its own begin / end camera rendering. B (the caster fade)
    /// swaps the caster's materials to a dither ghost only inside one camera's render. Never while the draw close-up is on. Not a singleton.</summary>
    [DefaultExecutionOrder(32000)]
    public sealed class Vol2CastCameraDirector : MonoBehaviour
    {
        public const string CaptureCameraName = "Vol2Vfx_Cam";
        const string GhostShader = "Oheangbu/Vol2/CasterDither";

        static Vol2CastCameraMode _mode;
        static Vol2CastCamTreatment _treatment;
        public static Vol2CastCameraMode Mode { get => _mode; set => _mode = value; }
        public static Vol2CastCamTreatment Treatment { get => _treatment; set => _treatment = value; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _mode = Vol2CastCameraMode.Auto; _treatment = Vol2CastCamTreatment.None; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap() { Ensure(); }

        /// <summary>Makes the director (with its restore partner) if the mode allows one and there is none. Returns it or null.</summary>
        public static Vol2CastCameraDirector Ensure()
        {
            if (_mode == Vol2CastCameraMode.Off) return null;
            if (_mode == Vol2CastCameraMode.Auto && !Vol2FreeShowcaseInstaller.LabLoaded()) return null;
            var d = FindFirstObjectByType<Vol2CastCameraDirector>();
            if (d != null) return d;
            var go = new GameObject("Vol2CastCameraDirector");
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
        float _nextScan;
        Action<ScriptableRenderContext, Camera> _begin, _end;
        CameraRigController _rig;

        // the game camera pose put in LateUpdate and taken off by Vol2CastCameraRestore
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
        public float LastWeight { get; private set; }
        public float LastVisibility { get; private set; } = 1f;
        public float LastCover { get; private set; }
        public float LastAge { get; private set; } = -1f;
        public string LastFocus { get; private set; } = "";
        public int Coordinators => _subscribed.Count;
        public string GhostProblem { get; private set; } = "";
        public int BodyRenderers => _active != null ? _active.Body.Count : -1;
        public string BodyNames => _active == null ? "" : string.Join(", ", _active.Body.ConvertAll(r => r != null ? r.name + "[" + (r.sharedMaterial != null && r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "-") + "]" : "null"));

        void Awake()
        {
            _profile = Vol2CastCameraProfile.LoadCopy();
            _begin = OnBeginCamera; _end = OnEndCamera;
        }

        void OnEnable() { RenderPipelineManager.beginCameraRendering += _begin; RenderPipelineManager.endCameraRendering += _end; }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= _begin; RenderPipelineManager.endCameraRendering -= _end;
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

        bool Drawing()
        {
            if (_rig == null) _rig = FindFirstObjectByType<CameraRigController>();
            return _rig != null && _rig.IsDrawingCloseup;
        }

        const Vol2CastCamTreatment PoseFlags = Vol2CastCamTreatment.Frame | Vol2CastCamTreatment.Widen | Vol2CastCamTreatment.Cine;

        bool PoseFor(Camera cam, out Vol2CastCamPose pose)
        {
            pose = default;
            var t = _treatment & PoseFlags;
            if (t == Vol2CastCamTreatment.None || _active == null || cam == null) return false;
            float age = Age(_active); LastAge = age;
            var b = new Vol2CastCamPose(cam.transform.position, cam.transform.rotation, cam.fieldOfView);
            pose = Vol2CastCameraMath.Evaluate(t, _profile, b, age, _active.Caster, _active.Forward, Focus(_active), out float w);
            LastWeight = w;
            return w > 0f;
        }

        // ---- the game camera: LateUpdate on, next frame's first Update off ----

        void LateUpdate()
        {
            if (_gameApplied) RestoreGame();       // a frame without the restore partner (it was disabled): never stack
            var cam = Camera.main;
            if (cam == null || _active == null || (_treatment & PoseFlags) == Vol2CastCamTreatment.None) return;
            if (Drawing()) { RefusedDrawing++; return; }
            if (!PoseFor(cam, out var pose)) return;
            _gameCamera = cam; _gameCam = cam.transform;
            _savedLocalPos = _gameCam.localPosition; _savedLocalRot = _gameCam.localRotation; _savedFov = cam.fieldOfView;
            _gameCam.SetPositionAndRotation(pose.Position, pose.Rotation);
            cam.fieldOfView = pose.Fov;
            _writtenLocalPos = _gameCam.localPosition; _writtenLocalRot = _gameCam.localRotation; _writtenFov = cam.fieldOfView;
            _gameApplied = true; GamePosed++;
        }

        /// <summary>Called by Vol2CastCameraRestore before every other Update of the frame: the camera goes back to what the rig left (assigned).</summary>
        public void RestoreGame()
        {
            if (!_gameApplied) return;
            _gameApplied = false;
            if (_gameCam == null || _gameCamera == null) return;
            if (_gameCam.localPosition != _writtenLocalPos || _gameCam.localRotation != _writtenLocalRot || _gameCamera.fieldOfView != _writtenFov) RestoreMismatch++;
            _gameCam.localPosition = _savedLocalPos; _gameCam.localRotation = _savedLocalRot; _gameCamera.fieldOfView = _savedFov;
        }

        // ---- inside one camera's render: the capture camera's pose, and the caster fade for either camera ----

        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null || cam.cameraType != CameraType.Game || _active == null || _treatment == Vol2CastCamTreatment.None) return;
            bool capture = cam.name == CaptureCameraName;
            if (!capture && cam != Camera.main) return;
            if (capture && PoseFor(cam, out var pose))
            {
                _posedCam = cam; _capPos = cam.transform.position; _capRot = cam.transform.rotation; _capFov = cam.fieldOfView;
                cam.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
                cam.fieldOfView = pose.Fov;
                CapturePosed++;
            }
            if ((_treatment & Vol2CastCamTreatment.Fade) != 0 && (capture || !Drawing())) FadeFor(cam);
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

    /// <summary>The partner of <see cref="Vol2CastCameraDirector"/>: before any other Update of the frame the game camera goes back to the rig's own pose.</summary>
    [DefaultExecutionOrder(-32000)]
    public sealed class Vol2CastCameraRestore : MonoBehaviour
    {
        Vol2CastCameraDirector _d;
        void Update()
        {
            if (_d == null) _d = GetComponent<Vol2CastCameraDirector>();
            if (_d != null) _d.RestoreGame();
        }
    }
}
