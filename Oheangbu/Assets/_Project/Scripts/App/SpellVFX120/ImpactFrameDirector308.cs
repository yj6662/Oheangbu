using System;
using System.Collections.Generic;
using Oheangbu.Data.Spell;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Oheangbu.App.SpellVFX120
{
    /// <summary>The render pass seen from the director (the pass itself needs the Core RP assembly, see ImpactFramePass308.cs).</summary>
    public interface IImpactPass308
    {
        void Setup(Material material, int fullPass, int quadPass);
        void ClearFlicker();
        void AddFlicker(Renderer renderer, Material flat, int subMeshes);
        bool HasWork { get; }
        int RecordedFull { get; }
        int RecordedDraw { get; }
        bool Enqueue(Camera camera);
    }

    public enum ImpactForm308 { None, Full, Local }

    /// <summary>Edit-mode preview hook (DeployLook308): one DontSave object that enqueues the impact pass with a fixed frame for every
    /// camera that renders while it is enabled. The play-mode path never uses it.</summary>
    [ExecuteAlways]
    public sealed class ImpactPreviewHook308 : MonoBehaviour
    {
        public ImpactFrameDirector308 Director;
        public int ShaderPass = -1;
        public float FloodCover, FloodClear;
        public Camera Only;   // null = every camera
        public int Enqueued { get; private set; }
        private void OnEnable() { RenderPipelineManager.beginCameraRendering += OnCamera; }
        private void OnDisable() { RenderPipelineManager.beginCameraRendering -= OnCamera; }
        private void OnCamera(ScriptableRenderContext context, Camera camera)
        {
            // edit-mode tool only: a preview left up must never put a fixed full-screen frame into a Play session
            if (Application.isPlaying || Director == null || ShaderPass < 0 || (Only != null && camera != Only)) return;
            if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) return;
            if (Director.PreviewRender(camera, ShaderPass, FloodCover, FloodClear)) Enqueued++;
        }
    }

    // SPEC-SPELL-DEPLOY-308 sections 8 / 9 / 11 / 12 (D308-10, D308-10b): the impact frame timetable.
    //   - 1-3 frames of 42 ms (unscaled) on the burst's first cel of an attack: invert -> silhouette -> return
    //   - photosensitivity: one impact = one value flip; ImpactLimiter308 keeps full-screen flips at or below the data rate
    //     (hard ceiling 3 per second); the user setting is Full / Reduced / Off
    //   - judgement gate: a guard inside its parry window, or an enemy whose predicted impact is near, turns the full-screen
    //     frame into the local form (the tell's colour must stay readable)
    //   - globals _OhImpact308 / _OhImpactHud308 for the same frames: the HUD reads them (ImpactHud308.hlsl, HudImpactReact308)
    //   - the pass is enqueued on the base game camera only, and only on frames that need it
    //   - field-of-view breath on the render only (the camera rig never sees it); never Time.timeScale
    // It is value, not light: nothing here writes an HDR colour and the pass runs after tone mapping.
    public sealed partial class ImpactFrameDirector308 : MonoBehaviour
    {
        public const int PassInvert = 0, PassSilhouette = 1, PassReturn = 2, PassLocal = 3, PassFlood = 4, PassLocalInvert = 5;
        private static readonly int ImpactId = Shader.PropertyToID("_OhImpact308");
        private static readonly int HudId = Shader.PropertyToID("_OhImpactHud308");
        private static readonly int HudPointId = Shader.PropertyToID("_OhImpactHudPoint308");
        private static readonly int ValuesId = Shader.PropertyToID("_ImpactValues");
        private static readonly int NeedlesId = Shader.PropertyToID("_ImpactNeedles");
        private static readonly int FloodId = Shader.PropertyToID("_ImpactFlood");
        private static readonly int SeedId = Shader.PropertyToID("_ImpactSeed");
        private static readonly int SrgbId = Shader.PropertyToID("_SrgbTarget");
        private static readonly int FloodMaskId = Shader.PropertyToID("_FloodMask");

        private SpellDeploy308ProfileSO _profile;
        private SpellDeploy308ProfileSO.TierSet _tier;
        private readonly ImpactLimiter308 _limiter = new ImpactLimiter308();
        private IImpactPass308 _pass;
        private Material _material;
        private Func<float, float, bool> _judgementNear;
        private Func<Camera> _cameraSource;
        private HitFlicker308 _flicker;
        private Camera _camera;
        private List<Camera> _stack;
        private DeployFlash308 _flash = DeployFlash308.Full;
        private bool _hudReact = true, _reducedMotion;

        private bool _active, _flood, _forced;
        private ImpactForm308 _form;
        private double _start, _floodStart, _breathStart = double.NegativeInfinity;
        private int _frames, _frame = -1;
        private Vector3 _worldPoint;
        private float _strength, _floodEdge;
        private Vector4 _impact, _hud;
        private int _seed;
        private readonly Dictionary<Camera, float> _fov = new Dictionary<Camera, float>();

        public ImpactLimiter308 Limiter => _limiter;
        public IImpactPass308 Pass => _pass;
        public bool PassAvailable => _pass != null && _material != null;
        public DeployFlash308 Flash => _flash;
        public bool HudReact => _hudReact;
        public bool Active => _active;
        public ImpactForm308 Form => _active ? _form : ImpactForm308.None;
        /// <summary>1 invert, 2 silhouette, 3 return; 0 = no impact frame right now.</summary>
        public int FrameKind => (int)_impact.w;
        public Vector4 ImpactGlobal => _impact;
        public Vector4 HudGlobal => _hud;
        /// <summary>Rises whenever the globals change (the HUD binder rebuilds its meshes only then).</summary>
        public int StateSerial { get; private set; }
        public int Requested { get; private set; }
        public int FullGranted { get; private set; }
        public int LocalGranted { get; private set; }
        public int SuppressedByGate { get; private set; }
        public int SuppressedByLimiter { get; private set; }
        public int SuppressedBySetting { get; private set; }
        public int MissingPass { get; private set; }
        public int FloodGranted { get; private set; }
        public int FloodRefused { get; private set; }
        public int EnqueuedFrames { get; private set; }
        public float LastFullSeconds { get; private set; }

        partial void CreatePass(ref IImpactPass308 pass);

        public void Configure(SpellDeploy308ProfileSO profile, DeployTier308 tier, Func<float, float, bool> judgementNear, Func<Camera> cameraSource, HitFlicker308 flicker)
        {
            _profile = profile; _judgementNear = judgementNear; _cameraSource = cameraSource; _flicker = flicker;
            if (profile == null) return;
            _tier = profile.Tier(tier);
            _limiter.Configure(profile.Impact.TokenCapacity, profile.FlipsPerSecond, profile.Impact.MinInterval);
            _limiter.Reset();
            if (_pass == null) CreatePass(ref _pass);
            if (_material == null && profile.ImpactShader != null) _material = new Material(profile.ImpactShader) { name = "ImpactFrame308 (runtime)", hideFlags = HideFlags.DontSave };
            if (_material != null && profile.FloodMask != null) _material.SetTexture(FloodMaskId, profile.FloodMask);
            _hudReact = _tier.HudReactDefault;
        }

        /// <summary>User settings (UserSettingsData.ImpactFlash / HudImpactReact / ReducedMotion), pushed when they change.</summary>
        public void SetUserSettings(DeployFlash308 flash, bool hudReact, bool reducedMotion)
        {
            // the Mobile tier keeps the HUD reaction off whatever the user switch says (TEST)
            _flash = flash; _hudReact = hudReact && (_tier == null || _tier.HudReactDefault); _reducedMotion = reducedMotion;
        }

        /// <summary>Presentation-clock pause a burst may take on its first cel (section 11): none under reduced motion / flash off.</summary>
        public float CutPause => _profile == null || _reducedMotion || _flash == DeployFlash308.Off ? 0f : _profile.Beats.CutPause;

        /// <summary>True when a full-screen frame would hide a judgement window right now.</summary>
        public bool JudgementNear => _profile != null && _judgementNear != null && _judgementNear(_profile.Impact.GateSeconds, _profile.Impact.GateRadius);

        /// <summary>A big-target flicker and the flood spend the same flip budget as a full-screen frame.</summary>
        public bool SpendFlipToken() => _limiter.TryTake(Time.unscaledTimeAsDouble);

        public ImpactForm308 Request(in ImpactRequest308 request) => Request(request, Time.unscaledTimeAsDouble);

        public ImpactForm308 Request(in ImpactRequest308 request, double now)
        {
            Requested++;
            if (_profile == null || !_profile.Impact.Enabled || _flash == DeployFlash308.Off) { SuppressedBySetting++; return ImpactForm308.None; }
            // no pass (the asmdef part is not deployed, or the shader is missing): no frame at all, so the HUD never reacts to nothing
            if (!PassAvailable) { MissingPass++; return ImpactForm308.None; }
            var form = ImpactForm308.Full;
            if (request.LocalOnly || _flash == DeployFlash308.Reduced || _tier == null || !_tier.FullScreen) form = ImpactForm308.Local;
            else if (JudgementNear) { SuppressedByGate++; form = ImpactForm308.Local; }
            else if (!_limiter.TryTake(now)) { SuppressedByLimiter++; form = ImpactForm308.Local; }
            // a running full frame is never cut short by a local one
            if (_active && _form == ImpactForm308.Full && form == ImpactForm308.Local) { LocalGranted++; return ImpactForm308.Local; }

            _active = true; _forced = false; _form = form; _start = now; _frame = -1;
            _frames = form == ImpactForm308.Full ? Mathf.Clamp(request.Frames, 1, 3) : 1;
            _worldPoint = request.WorldPoint; _strength = Mathf.Clamp01(request.Strength <= 0f ? 1f : request.Strength);
            _seed = (_seed * 31 + 7) & 1023;
            if (form == ImpactForm308.Full)
            {
                FullGranted++; LastFullSeconds = _frames * _profile.ImpactFrameSeconds;
                if (_tier.FovBreath && !_reducedMotion) _breathStart = now;
            }
            else LocalGranted++;
            return form;
        }

        /// <summary>Screen ink flood (section 12): only events on the profile's list, never near a judgement window, one flip token.</summary>
        public bool RequestFlood(string eventName)
        {
            if (_profile == null || !PassAvailable || _flash == DeployFlash308.Off || _tier == null || !_tier.Flood || !_profile.FloodAllowed(eventName)
                || JudgementNear || !_limiter.TryTake(Time.unscaledTimeAsDouble)) { FloodRefused++; return false; }
            _flood = true; _floodStart = Time.unscaledTimeAsDouble;
            _floodEdge = _flash == DeployFlash308.Reduced ? _profile.Flood.ReducedEdgeShare : 0f;
            FloodGranted++;
            return true;
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            foreach (var pair in _fov) if (pair.Key != null) pair.Key.fieldOfView = pair.Value;
            _fov.Clear();
            _active = _flood = false;
            Write(Vector4.zero, Vector4.zero);
        }

        private void OnDestroy() { ReleaseResources(); }

        /// <summary>Destroys the runtime material (also called by edit-mode tools, where OnDestroy does not run).</summary>
        public void ReleaseResources()
        {
            if (_material == null) return;
            if (Application.isPlaying) Destroy(_material); else DestroyImmediate(_material);
            _material = null;
        }

        // domain reload is off: the globals must not carry an impact over into the next Play session
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetGlobals()
        {
            Shader.SetGlobalVector(ImpactId, Vector4.zero);
            Shader.SetGlobalVector(HudId, Vector4.zero);
            Shader.SetGlobalVector(HudPointId, Vector4.zero);
        }

        private void Write(Vector4 impact, Vector4 hud)
        {
            if (impact == _impact && hud == _hud) return;
            _impact = impact; _hud = hud; StateSerial++;
            Shader.SetGlobalVector(ImpactId, impact);
            Shader.SetGlobalVector(HudId, hud);
            // Overlay canvases are drawn straight onto the final target after the last camera, and its pixel rows start at the top
            // on D3D / Vulkan / Metal. The UI shader compares SV_POSITION with this point in that same frame; it reads no
            // projection flag (by then that flag is whatever the last camera left behind).
            bool top = SystemInfo.graphicsUVStartsAtTop != (_profile != null && _profile.Hud.FlipV);
            Shader.SetGlobalVector(HudPointId, impact.z <= 0f ? Vector4.zero : new Vector4(impact.x, top ? 1f - impact.y : impact.y, 0f, 0f));
        }

        /// <summary>Which shader pass a frame of an n-frame impact uses: 1 frame = silhouette; 2 = invert, silhouette; 3 = + return.</summary>
        public static int KindOf(int frames, int index) => frames <= 1 ? 2 : index == 0 ? 1 : index == 1 ? 2 : 3;

        private void Update() { Tick(Time.unscaledTimeAsDouble); }

        /// <summary>Advances the timetable (the unit check drives it with its own clock).</summary>
        public void Tick(double now)
        {
            if (_flood && _profile != null && now - _floodStart > _profile.Flood.Cover + _profile.Flood.Clear) _flood = false;
            if (_forced) return;
            if (!_active) { if (_impact.z != 0f) Write(Vector4.zero, Vector4.zero); return; }
            int index = (int)Math.Floor((now - _start) / _profile.ImpactFrameSeconds);   // clamped: 3 frames <= 126 ms
            if (index >= _frames) { _active = false; _frame = -1; Write(Vector4.zero, Vector4.zero); return; }
            if (index == _frame) return;
            _frame = index;
            int kind = _form == ImpactForm308.Full ? KindOf(_frames, index) : 2;
            var hud = Vector4.zero;
            if (_hudReact && _profile.Hud.Enabled)
            {
                bool reduced = _form != ImpactForm308.Full;
                float scale = reduced ? _profile.Hud.ReducedScale : 1f;
                hud = new Vector4(_profile.Hud.Rim * scale, reduced ? 0f : _profile.HudFill, reduced ? 0f : _profile.Hud.ShadowPixels, !reduced && kind == 1 ? 1f : 0f);
            }
            Write(new Vector4(_impact.x, _impact.y, _strength, kind), hud);
        }

        private Camera BaseCamera()
        {
            var camera = _cameraSource != null ? _cameraSource() : null;
            if (camera == null) camera = Camera.main;
            if (camera != _camera)
            {
                _camera = camera; _stack = null;
                if (camera != null && camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) && data.renderType == CameraRenderType.Base) _stack = data.cameraStack;
            }
            return _camera;
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (_profile == null || camera.cameraType != CameraType.Game) return;
            Camera main = BaseCamera();
            double now = Time.unscaledTimeAsDouble;
            bool inStack = camera == main || (_stack != null && _stack.Contains(camera));
            if (!inStack) return;
            Breathe(camera, now);
            if (camera != main) return;

            bool flicker = _flicker != null && _flicker.AnyActive(now);
            if (!_active && !_flood && !flicker) return;
            if (_active && !_forced)
            {
                // the impact point on screen: the direction the HUD's "light" comes from
                Vector3 view = camera.WorldToViewportPoint(_worldPoint);
                Vector2 uv = view.z > 0f ? new Vector2(view.x, view.y) : new Vector2(.5f, .5f);
                uv.x = Mathf.Clamp(uv.x, -.25f, 1.25f); uv.y = Mathf.Clamp(uv.y, -.25f, 1.25f);
                if (uv.x != _impact.x || uv.y != _impact.y) { var g = _impact; g.x = uv.x; g.y = uv.y; Write(g, _hud); }
            }
            if (_pass == null) return;
            int full = -1, quad = -1;
            if (_material != null)
            {
                var imp = _profile.Impact;
                _material.SetVector(ValuesId, new Vector4(imp.Threshold, imp.InkValue, Mathf.Min(imp.PaperValue, SpellDeploy308ProfileSO.PaperCeiling), imp.RadialReach));
                _material.SetVector(NeedlesId, new Vector4(imp.NeedleCount, imp.NeedleWidth, imp.OutlineStrength, imp.LocalRadius));
                _material.SetFloat(SeedId, _seed);
                _material.SetFloat(SrgbId, imp.TargetIsSrgbEncoded ? 1f : 0f);
                if (_active && FrameKind > 0)
                {
                    if (_form == ImpactForm308.Full) full = FrameKind == 1 ? PassInvert : FrameKind == 2 ? PassSilhouette : PassReturn;
                    else if (_tier.FullScreen) full = PassLocal;
                    else quad = PassLocalInvert;
                }
                else if (_flood)
                {
                    float t = (float)(now - _floodStart);
                    var f = _profile.Flood;
                    _material.SetVector(FloodId, new Vector4(Mathf.Clamp01(t / Mathf.Max(.01f, f.Cover)), Mathf.Clamp01((t - f.Cover) / Mathf.Max(.01f, f.Clear)), _floodEdge, 0f));
                    full = PassFlood;
                }
            }
            _pass.Setup(_material, full, quad);
            _pass.ClearFlicker();
            if (flicker) _flicker.Fill(_pass, now, this);
            if (_pass.HasWork && _pass.Enqueue(camera)) EnqueuedFrames++;
        }

        /// <summary>Edit-mode preview: enqueue the pass for `camera` with a fixed shader pass (ImpactPreviewHook308 calls it).</summary>
        public bool PreviewRender(Camera camera, int shaderPass, float floodCover, float floodClear)
        {
            if (_profile == null || _pass == null || _material == null || camera == null) return false;
            var imp = _profile.Impact;
            _material.SetVector(ValuesId, new Vector4(imp.Threshold, imp.InkValue, Mathf.Min(imp.PaperValue, SpellDeploy308ProfileSO.PaperCeiling), imp.RadialReach));
            _material.SetVector(NeedlesId, new Vector4(imp.NeedleCount, imp.NeedleWidth, imp.OutlineStrength, imp.LocalRadius));
            _material.SetVector(FloodId, new Vector4(floodCover, floodClear, 0f, 0f));
            _material.SetFloat(SeedId, _seed);
            _material.SetFloat(SrgbId, imp.TargetIsSrgbEncoded ? 1f : 0f);
            _pass.Setup(_material, shaderPass == PassLocalInvert ? -1 : shaderPass, shaderPass == PassLocalInvert ? PassLocalInvert : -1);
            _pass.ClearFlicker();
            if (!_pass.Enqueue(camera)) return false;
            EnqueuedFrames++;
            return true;
        }

        private void OnEndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (_fov.Count == 0) return;
            if (_fov.TryGetValue(camera, out float fov)) { camera.fieldOfView = fov; _fov.Remove(camera); }
        }

        // a breath of the field of view on the render only; the rig's own value is put back when the camera finishes
        private void Breathe(Camera camera, double now)
        {
            if (_profile == null || camera.orthographic) return;
            float duration = _profile.Camera.FovBreathSeconds;
            double t = now - _breathStart;
            if (t < 0.0 || t >= duration || _profile.Camera.FovBreathDeg <= 0f) return;
            if (_fov.ContainsKey(camera)) return;
            _fov[camera] = camera.fieldOfView;
            camera.fieldOfView += _profile.Camera.FovBreathDeg * Mathf.Sin((float)(t / duration) * Mathf.PI);
        }

        /// <summary>Preview / check hook: force one frame kind (0 clears). The editor tool uses it with a fixed frame.</summary>
        public void ForceFrame(int kind, Vector2 screenUv, float strength, bool hud)
        {
            if (kind <= 0) { _active = _forced = false; Write(Vector4.zero, Vector4.zero); return; }
            _active = _forced = true; _form = kind >= 4 ? ImpactForm308.Local : ImpactForm308.Full; _frames = 3; _frame = 0; _start = Time.unscaledTimeAsDouble;
            _strength = Mathf.Clamp01(strength);
            Write(new Vector4(screenUv.x, screenUv.y, _strength, Mathf.Clamp(kind, 1, 3)),
                hud && _profile != null ? new Vector4(_profile.Hud.Rim, _profile.HudFill, _profile.Hud.ShadowPixels, kind == 1 ? 1f : 0f) : Vector4.zero);
        }
    }
}
