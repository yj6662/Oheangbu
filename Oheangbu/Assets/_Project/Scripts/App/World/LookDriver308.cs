using System;
using Oheangbu.Data.World;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Oheangbu.App.World
{
    // #308 look "PBZ-Ink" (PLAN_look308.md, D308-78, TEST): applies a LookProfile308 to the picture.
    //  * shader globals _OhLook308* (read by InkWash297 v5 and RealmFog297; unset = 0 = both shaders run their old math only);
    //  * scene state, only in Play or while an editor tool previews (never in plain edit mode, so a scene saved by another tool
    //    cannot capture a darkened sun): sun intensity / colour / shadow strength and the ambient (probe + colours) as MULTIPLIERS
    //    of whatever the owner wrote (WorldLookDriver + InkSkyProfile keep owning them: when the value differs from what this
    //    driver last wrote, the owner has written a new base and it is re-read), and a transient global post volume
    //    (HideAndDontSave object + profile, weight = Strength) when the profile asks for one.
    // Realm blend = the same source as the regional sky: RegionalInkSkyProfile.GetWeights over the sky geography sheet at the
    // rendering camera's position, summed per RealmId, eased over ResponseSeconds in Play (edit mode and the first step snap).
    // The step runs in RenderPipelineManager.beginCameraRendering, i.e. after every LateUpdate and also inside the capture tools'
    // Camera.Render() (they move a temporary camera between stations without a frame in between).
    // The sun direction is never touched (ShadowSunMinElevation307 >= 25 deg and the baked shadow casters stay valid).
    // No singleton, no coroutine, no static state (immutable property ids and the profiler marker only), nothing is written to an
    // asset or serialized into the scene. OnDisable puts everything back and sets _OhLook308 to 0.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class LookDriver308 : MonoBehaviour
    {
        [SerializeField] private LookProfile308 _profile;
        [Tooltip("The regional sky profile and geography of this scene's WorldLookDriver (realm weights come from them)")]
        [SerializeField] private RegionalInkSkyProfile _regional;
        [SerializeField] private WorldMacroSheetSO _geography;
        [Tooltip("Empty = RenderSettings.sun")]
        [SerializeField] private Light _sun;
        [Tooltip("Also write sun / ambient / post volume in edit mode (off: shader globals only, tools preview explicitly)")]
        [SerializeField] private bool _sceneStateInEditMode;

        private static readonly int StrengthId = Shader.PropertyToID("_OhLook308");
        private static readonly int InkId = Shader.PropertyToID("_OhLook308Ink");
        private static readonly int LineId = Shader.PropertyToID("_OhLook308Line");
        private static readonly int GradeId = Shader.PropertyToID("_OhLook308Grade");
        private static readonly int ShadowId = Shader.PropertyToID("_OhLook308Shadow");
        private static readonly int HighId = Shader.PropertyToID("_OhLook308High");
        private static readonly int WashId = Shader.PropertyToID("_OhLook308Wash");
        private static readonly int AccentId = Shader.PropertyToID("_OhLook308Accent");
        private static readonly int AccentAId = Shader.PropertyToID("_OhLook308AccentA");
        private static readonly int AccentBId = Shader.PropertyToID("_OhLook308AccentB");
        private static readonly int SkyId = Shader.PropertyToID("_OhLook308Sky");
        private static readonly int PoolId = Shader.PropertyToID("_OhLook308Pool");
        private static readonly int BandId = Shader.PropertyToID("_OhLook308Band");
        private static readonly int AirId = Shader.PropertyToID("_OhLook308Air");
        private static readonly int PaperId = Shader.PropertyToID("_OhLook308Paper");
        private static readonly int DryId = Shader.PropertyToID("_OhLook308Dry");
        private static readonly int RimId = Shader.PropertyToID("_OhLook308Rim");
        private static readonly int WetId = Shader.PropertyToID("_OhLook308Wet");
        private static readonly int FogId = Shader.PropertyToID("_OhLook308Fog");
        private static readonly int FogTintId = Shader.PropertyToID("_OhLook308FogTint");
        private static readonly int ScatterId = Shader.PropertyToID("_OhLook308Scatter");
        private static readonly int MoteId = Shader.PropertyToID("_OhLook308Mote");
        private static readonly int MoteBId = Shader.PropertyToID("_OhLook308MoteB");
        private static readonly int MoteColourId = Shader.PropertyToID("_OhLook308MoteColour");
        // measurement only (Perf307 reads markers by name). Immutable.
        private static readonly ProfilerMarker LookMarker = new ProfilerMarker(ProfilerCategory.Scripts, "Oh.Look308");

        private const int RealmCount = 5;
        private readonly float[] _realm = new float[RealmCount];
        private float[] _region;
        private LookProfile308.Look _look;
        private bool _hasLook;
        private int _steppedFrame = -1;
        [NonSerialized] private bool _editPreview;

        // sun: base = what the owner wrote, wrote = what this driver wrote on top
        private Light _sunHeld;
        private bool _sunHas;
        private float _sunBaseIntensity, _sunBaseShadow, _sunWroteIntensity, _sunWroteShadow;
        private Color _sunBaseColor, _sunWroteColor;
        // ambient
        private bool _ambHas;
        private AmbientMode _ambMode;
        private SphericalHarmonicsL2 _ambBaseProbe, _ambWroteProbe;
        private Color _ambBaseSky, _ambBaseEquator, _ambBaseGround, _ambBaseFlat, _ambWroteSky, _ambWroteEquator, _ambWroteGround, _ambWroteFlat;
        // post volume (transient)
        private GameObject _volumeObject;
        private Volume _volume;
        private VolumeProfile _volumeProfile;
        private ColorAdjustments _volColour;
        private Vignette _volVignette;
        private FilmGrain _volGrain;
        private Tonemapping _volTone;

        public LookProfile308 Profile { get => _profile; set { _profile = value; _hasLook = false; } }
        /// <summary>True when the last step wrote a usable look (profile assigned, Usable).</summary>
        public bool Live { get; private set; }
        /// <summary>True while the sun / ambient / post volume carry this driver's values.</summary>
        public bool SceneStateApplied => _sunHas || _ambHas || _volume != null;
        /// <summary>The blended look of the last step (Identity before the first one).</summary>
        public LookProfile308.Look Current => _hasLook ? _look : LookProfile308.Look.Identity;
        /// <summary>Realm weight of the last step (index = (int)RealmId).</summary>
        public float RealmWeight(RealmId id) { int i = (int)id; return i >= 0 && i < RealmCount ? _realm[i] : 0f; }

        /// <summary>Editor tools (stills): sun / ambient / post volume also in edit mode while true. In memory only — never serialized,
        /// so it cannot be saved into a scene; switching it off restores the owner's values at once.</summary>
        public void SetEditPreview(bool on)
        {
            _editPreview = on;
            if (!on && !Application.isPlaying && !_sceneStateInEditMode) RestoreScene();
        }

        /// <summary>Snap-evaluates at a position now (editor tools that want the state without rendering a camera).</summary>
        public void Refresh(Vector3 eye) { _hasLook = false; Step(eye, false); }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            Release();
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null) return;
            var type = camera.cameraType;
            if (type != CameraType.Game && type != CameraType.SceneView) return;
            bool playing = Application.isPlaying;
            if (playing)
            {
                // one eased step per frame, at the player's camera (not a map / portrait camera)
                if (type != CameraType.Game) return;
                var main = Camera.main;
                if (main != null && camera != main) return;
                if (_hasLook && Time.frameCount == _steppedFrame) return;
            }
            using (LookMarker.Auto()) Step(camera.transform.position, playing);
        }

        private void Step(Vector3 eye, bool ease)
        {
            var p = _profile;
            if (p == null || !p.Usable) { Release(); return; }
            Weights(eye);
            var target = p.Evaluate(_realm, eye);
            if (!ease || !_hasLook) _look = target;
            else
            {
                float k = 1f - Mathf.Exp(-Mathf.Max(0f, Time.deltaTime) / Mathf.Max(.05f, p.ResponseSeconds));
                _look = LookProfile308.Look.Lerp(_look, target, k);
            }
            _hasLook = true; _steppedFrame = Time.frameCount;
            WriteGlobals(p, _look);
            if (Application.isPlaying || _sceneStateInEditMode || _editPreview) WriteScene(p, _look);
            else RestoreScene();
            Live = true;
        }

        private void Weights(Vector3 eye)
        {
            Array.Clear(_realm, 0, RealmCount);
            var regions = _geography != null ? _geography.Regions : null;
            if (_regional == null || regions == null || regions.Length == 0)
            {
                int d = _regional != null ? (int)_regional.DefaultRealm : (int)RealmId.Hwanggyeong;
                _realm[d >= 0 && d < RealmCount ? d : (int)RealmId.Hwanggyeong] = 1f;
                return;
            }
            if (_region == null || _region.Length < regions.Length) _region = new float[Mathf.Max(8, regions.Length)];   // once per sheet size
            _regional.GetWeights(_geography, eye, _region);
            float total = 0f;
            for (int i = 0; i < regions.Length; i++)
            {
                if (regions[i] == null) continue;
                int r = (int)regions[i].Realm;
                if (r < 0 || r >= RealmCount) continue;
                _realm[r] += _region[i]; total += _region[i];
            }
            if (total <= 1e-5f) _realm[(int)_regional.DefaultRealm >= 0 && (int)_regional.DefaultRealm < RealmCount ? (int)_regional.DefaultRealm : (int)RealmId.Hwanggyeong] = 1f;
            else if (Mathf.Abs(total - 1f) > 1e-4f) for (int r = 0; r < RealmCount; r++) _realm[r] /= total;
        }

        // ---------------------------------------------------------------- shader globals

        private static float Luma(Color linear) => .2126f * linear.r + .7152f * linear.g + .0722f * linear.b;
        // hue at unit luminance (linear): multiplying it by a pixel's luminance moves the hue and keeps the value
        private static Vector4 Hue(Color srgb, float w)
        {
            Color c = srgb.linear; float l = Mathf.Max(.02f, Luma(c));
            return new Vector4(c.r / l, c.g / l, c.b / l, w);
        }
        private static Vector4 Linear(Color srgb, float scale, float w)
        {
            Color c = srgb.linear;
            return new Vector4(c.r * scale, c.g * scale, c.b * scale, w);
        }
        // unit chroma direction of an accent hue (zero for a grey: never matches)
        private static Vector4 ChromaDirection(Color srgb)
        {
            Color c = srgb.linear; float l = Luma(c);
            var v = new Vector3(c.r - l, c.g - l, c.b - l);
            float m = v.magnitude;
            return m < 1e-4f ? Vector4.zero : new Vector4(v.x / m, v.y / m, v.z / m, 0f);
        }

        private void WriteGlobals(LookProfile308 p, in LookProfile308.Look l)
        {
            Shader.SetGlobalVector(InkId, new Vector4(Mathf.Max(0f, l.InkTone), Mathf.Max(0f, l.PaperWash), Mathf.Max(0f, l.HighlightPaper), Mathf.Max(0f, l.LineInk)));
            Shader.SetGlobalVector(LineId, new Vector4(Mathf.Clamp(l.LineWidth, 1f, 4f), Mathf.Max(1f, p.LineWidthFade), Mathf.Clamp(l.LineLitSide, 0f, 2f), Mathf.Clamp(l.NearHillInk, 0f, 2f)));
            Shader.SetGlobalVector(GradeId, new Vector4(Mathf.Pow(2f, Mathf.Clamp(l.Exposure, -4f, 4f)), Mathf.Clamp(l.Contrast, .5f, 2.5f), Mathf.Clamp(l.Saturation, 0f, 2f), Mathf.Max(.02f, p.Pivot)));
            Shader.SetGlobalVector(ShadowId, Hue(l.ShadowTint, Mathf.Clamp01(l.ShadowTintAmount)));
            Shader.SetGlobalVector(HighId, Hue(l.HighlightTint, Mathf.Clamp01(l.HighlightTintAmount)));
            Shader.SetGlobalVector(WashId, Hue(l.Wash, Mathf.Clamp01(l.WashAmount)));
            Shader.SetGlobalVector(AccentId, new Vector4(Mathf.Clamp01(p.AccentProtect), p.AccentChroma.x, Mathf.Max(p.AccentChroma.y, p.AccentChroma.x + .01f), 0f));
            Shader.SetGlobalVector(AccentAId, ChromaDirection(p.AccentA));
            Shader.SetGlobalVector(AccentBId, ChromaDirection(p.AccentB));
            Shader.SetGlobalVector(SkyId, new Vector4(Mathf.Max(0f, l.SkyValue), Mathf.Clamp(l.SkySaturation, 0f, 2f), 0f, 0f));
            Shader.SetGlobalVector(PoolId, new Vector4(l.PoolLow, Mathf.Max(l.PoolHigh, l.PoolLow + .01f), Mathf.Clamp01(l.PoolAmount), Mathf.Clamp01(l.WetEdge)));
            Shader.SetGlobalVector(BandId, new Vector4(Mathf.Max(0f, l.BandStart), Mathf.Max(l.BandEnd, l.BandStart + 1f), Mathf.Clamp(p.BandSteps, 1, 6), Mathf.Clamp01(l.BandAmount)));
            Shader.SetGlobalVector(AirId, Linear(l.Air, 1f, Mathf.Clamp(p.BandSoftness, .02f, .5f)));
            Shader.SetGlobalVector(PaperId, new Vector4(Mathf.Max(0f, l.PaperGrain), p.PaperValue.x, Mathf.Max(p.PaperValue.y, p.PaperValue.x + .01f), Mathf.Clamp01(l.DryBrush)));
            Shader.SetGlobalVector(DryId, new Vector4(Mathf.Max(1f, p.DryBrushShape.x), Mathf.Max(.001f, p.DryBrushShape.y), Mathf.Clamp01(p.DryBrushShape.z), Mathf.Max(0f, p.DryBrushNear)));   // w: 2026-10-26 갈필 distance gate (0 = off)
            Shader.SetGlobalVector(RimId, Linear(l.RimColour, Mathf.Max(0f, l.RimAmount), Mathf.Clamp(l.RimPower, .5f, 16f)));
            Shader.SetGlobalVector(WetId, new Vector4(Mathf.Clamp01(l.Wetness), Mathf.Clamp01(l.WetDarken), Mathf.Max(0f, l.WetSpecular), Mathf.Clamp(l.WetGloss, 1f, 512f)));
            Shader.SetGlobalVector(FogId, new Vector4(Mathf.Max(0f, l.FogValue), Mathf.Max(0f, l.FogDensity), Mathf.Max(0f, l.FogStart), Mathf.Max(0f, l.MistDensity)));
            Shader.SetGlobalVector(FogTintId, Linear(l.FogTint, 1f, 1f));
            Shader.SetGlobalVector(ScatterId, Linear(l.Scatter, Mathf.Max(0f, l.ScatterAmount), Mathf.Clamp(l.ScatterPower, 1f, 64f)));
            Shader.SetGlobalVector(MoteId, new Vector4(Mathf.Clamp01(l.MoteDensity), Mathf.Clamp(l.MoteSize, .5f, 12f), l.MoteFall, l.MoteWind));
            Shader.SetGlobalVector(MoteBId, new Vector4(Mathf.Clamp(l.MoteStreak, 1f, 40f), Mathf.Max(.5f, p.MoteShell.x), Mathf.Max(p.MoteShell.y, p.MoteShell.x + .5f), 0f));
            Shader.SetGlobalVector(MoteColourId, Linear(l.MoteColour, 1f, Mathf.Clamp01(l.MoteColour.a)));
            Shader.SetGlobalFloat(StrengthId, Mathf.Clamp01(p.Strength));
        }

        // ---------------------------------------------------------------- scene state (Play / preview only)

        private void WriteScene(LookProfile308 p, in LookProfile308.Look l)
        {
            float k = Mathf.Clamp01(p.Strength);
            // sun
            var sun = _sun != null ? _sun : RenderSettings.sun;
            if (sun != _sunHeld) { RestoreSun(); _sunHeld = sun; }
            if (sun != null)
            {
                if (!_sunHas || sun.intensity != _sunWroteIntensity || sun.color != _sunWroteColor || sun.shadowStrength != _sunWroteShadow)
                {
                    _sunBaseIntensity = sun.intensity; _sunBaseColor = sun.color; _sunBaseShadow = sun.shadowStrength; _sunHas = true;
                }
                Color tint = Color.Lerp(Color.white, l.SunTint, k);
                _sunWroteIntensity = _sunBaseIntensity * Mathf.Lerp(1f, Mathf.Max(0f, l.SunIntensity), k);
                _sunWroteColor = new Color(_sunBaseColor.r * tint.r, _sunBaseColor.g * tint.g, _sunBaseColor.b * tint.b, _sunBaseColor.a);
                _sunWroteShadow = Mathf.Lerp(_sunBaseShadow, 1f, Mathf.Clamp01(l.ShadowDeepen) * k);
                sun.intensity = _sunWroteIntensity; sun.color = _sunWroteColor; sun.shadowStrength = _sunWroteShadow;
                // read back what the engine stored, so the next comparison is exact
                _sunWroteIntensity = sun.intensity; _sunWroteColor = sun.color; _sunWroteShadow = sun.shadowStrength;
            }
            // ambient: a multiplier over whatever InkSkyProfile wrote for the regional sky
            var mode = RenderSettings.ambientMode;
            bool custom = mode == AmbientMode.Custom;
            if (!_ambHas || mode != _ambMode || RenderSettings.ambientSkyColor != _ambWroteSky || RenderSettings.ambientEquatorColor != _ambWroteEquator
                || RenderSettings.ambientGroundColor != _ambWroteGround || RenderSettings.ambientLight != _ambWroteFlat
                || custom && RenderSettings.ambientProbe != _ambWroteProbe)
            {
                _ambMode = mode; _ambBaseProbe = RenderSettings.ambientProbe;
                _ambBaseSky = RenderSettings.ambientSkyColor; _ambBaseEquator = RenderSettings.ambientEquatorColor;
                _ambBaseGround = RenderSettings.ambientGroundColor; _ambBaseFlat = RenderSettings.ambientLight; _ambHas = true;
            }
            Color at = l.AmbientTint.linear; float av = Mathf.Max(0f, l.AmbientValue);
            var mLinear = new Vector3(Mathf.Lerp(1f, at.r * av, k), Mathf.Lerp(1f, at.g * av, k), Mathf.Lerp(1f, at.b * av, k));
            var mGamma = new Vector3(Mathf.LinearToGammaSpace(mLinear.x), Mathf.LinearToGammaSpace(mLinear.y), Mathf.LinearToGammaSpace(mLinear.z));
            // the flat colour is read in trilight/flat modes; in Custom mode the shaders sample the probe
            RenderSettings.ambientSkyColor = Scale(_ambBaseSky, mGamma);
            RenderSettings.ambientEquatorColor = Scale(_ambBaseEquator, mGamma);
            RenderSettings.ambientGroundColor = Scale(_ambBaseGround, mGamma);
            if (mode == AmbientMode.Flat) RenderSettings.ambientLight = Scale(_ambBaseFlat, mGamma);
            if (custom)
            {
                var sh = _ambBaseProbe;
                for (int c = 0; c < 3; c++) for (int i = 0; i < 9; i++) sh[c, i] = _ambBaseProbe[c, i] * mLinear[c];
                RenderSettings.ambientProbe = sh;
            }
            _ambWroteSky = RenderSettings.ambientSkyColor; _ambWroteEquator = RenderSettings.ambientEquatorColor;
            _ambWroteGround = RenderSettings.ambientGroundColor; _ambWroteFlat = RenderSettings.ambientLight; _ambWroteProbe = RenderSettings.ambientProbe;
            // post volume
            if (p.Post.Override) WriteVolume(p.Post, k); else DestroyVolume();
        }

        private static Color Scale(Color c, Vector3 m) => new Color(c.r * m.x, c.g * m.y, c.b * m.z, c.a);

        private void RestoreSun()
        {
            if (_sunHas && _sunHeld != null && _sunHeld.intensity == _sunWroteIntensity && _sunHeld.color == _sunWroteColor && _sunHeld.shadowStrength == _sunWroteShadow)
            {
                // still this driver's values: give the owner's back (a value someone else wrote meanwhile is left alone)
                _sunHeld.intensity = _sunBaseIntensity; _sunHeld.color = _sunBaseColor; _sunHeld.shadowStrength = _sunBaseShadow;
            }
            _sunHas = false; _sunHeld = null;
        }

        private void RestoreScene()
        {
            RestoreSun();
            if (_ambHas)
            {
                bool mine = RenderSettings.ambientMode == _ambMode && RenderSettings.ambientSkyColor == _ambWroteSky && RenderSettings.ambientEquatorColor == _ambWroteEquator
                    && RenderSettings.ambientGroundColor == _ambWroteGround && RenderSettings.ambientLight == _ambWroteFlat
                    && (_ambMode != AmbientMode.Custom || RenderSettings.ambientProbe == _ambWroteProbe);
                if (mine)
                {
                    RenderSettings.ambientSkyColor = _ambBaseSky; RenderSettings.ambientEquatorColor = _ambBaseEquator;
                    RenderSettings.ambientGroundColor = _ambBaseGround; RenderSettings.ambientLight = _ambBaseFlat;
                    if (_ambMode == AmbientMode.Custom) RenderSettings.ambientProbe = _ambBaseProbe;
                }
                _ambHas = false;
            }
            DestroyVolume();
        }

        private void WriteVolume(in LookProfile308.PostGrade g, float weight)
        {
            if (_volume == null)
            {
                _volumeObject = new GameObject("Look308 post (transient)") { hideFlags = HideFlags.HideAndDontSave, layer = gameObject.layer };
                _volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                _volumeProfile.hideFlags = HideFlags.HideAndDontSave;
                _volColour = _volumeProfile.Add<ColorAdjustments>(false);
                _volVignette = _volumeProfile.Add<Vignette>(false);
                _volGrain = _volumeProfile.Add<FilmGrain>(false);
                _volTone = _volumeProfile.Add<Tonemapping>(false);
                _volume = _volumeObject.AddComponent<Volume>();
                _volume.isGlobal = true; _volume.priority = 1000f; _volume.sharedProfile = _volumeProfile;
            }
            _volColour.postExposure.Override(g.Exposure);
            _volColour.contrast.Override(Mathf.Clamp(g.Contrast, -100f, 100f));
            _volColour.saturation.Override(Mathf.Clamp(g.Saturation, -100f, 100f));
            _volVignette.intensity.Override(Mathf.Clamp01(g.Vignette));
            _volGrain.intensity.Override(Mathf.Clamp01(g.FilmGrain));
            _volGrain.active = g.FilmGrain > 0f;
            _volTone.active = g.Tonemap != 0;
            if (g.Tonemap != 0) _volTone.mode.Override(g.Tonemap == 2 ? TonemappingMode.ACES : TonemappingMode.Neutral);
            _volume.weight = Mathf.Clamp01(weight);
        }

        private void DestroyVolume()
        {
            if (_volumeObject != null) { if (Application.isPlaying) Destroy(_volumeObject); else DestroyImmediate(_volumeObject); }
            if (_volumeProfile != null)
            {
                // the components are sub-objects of the transient profile
                foreach (var c in _volumeProfile.components) if (c != null) { if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }
                if (Application.isPlaying) Destroy(_volumeProfile); else DestroyImmediate(_volumeProfile);
            }
            _volumeObject = null; _volume = null; _volumeProfile = null; _volColour = null; _volVignette = null; _volGrain = null; _volTone = null;
        }

        private void Release()
        {
            Shader.SetGlobalFloat(StrengthId, 0f);
            RestoreScene();
            _hasLook = false; Live = false;
        }
    }
}
