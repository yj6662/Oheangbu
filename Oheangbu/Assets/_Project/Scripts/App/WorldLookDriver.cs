using Oheangbu.BrushRender;
using Unity.Profiling;
using UnityEngine;

namespace Oheangbu.App
{
    // [SPEC-SPIKE-WORLD-LOOKDEV §5-8] 월드 룩 색 공급 — 팔레트 SO → _Oh* 셰이더 전역, 화면의 모든 환경 색이 지나는 1경로(#153).
    // 머티리얼·씬 에셋에 색 값 0: InkWorld/InkLightSource는 프로퍼티에 색이 없고 이 전역만 읽는다.
    // 전역 기본값이 검정이라 이 컴포넌트가 없는 씬에서 InkWorld는 검정이다(#155 결합 명시 — Stage 1 씬도 필수).
    // 하늘 = 카메라 SolidColor 소지(#150·#154). 광맥·봉수 색은 초성(의미)만 들고 팔레트 원석 탁화로 파생한다(#152).
    // [ExecuteAlways]: 에디터 씬 뷰·인스펙터 튜닝에서도 같은 색. OnDisable → _OhWorldPost 0(포스트는 분기로 원본 반환).
    // #308 (SPEC-REGION-SKY-308 1b, TEST): the regional sky's time behaviour lives in one RegionalSkyStepper instance — snap on a
    // one-frame move above the profile's SnapDistance, capital atmosphere anchor (position bearing toward the capital realm
    // centroid, strength CapitalAtmosphere x (1 - capital weight), written only when the profile sets it), sub-region accents
    // (profile data), interior hold (_interiorMap zone with walked passages: state and ambient frozen), sealed cave cells skip every
    // sky write (_sealedCells), and the blended horizon/fog as globals _OhSkyHorizon/_OhSkyFog (linear) for the screen passes.
    // A profile without #308 data (RegionalSky297) renders exactly as before. No singleton, no new static state (immutable ids and
    // the profiler marker only); nothing writes a material or SO asset at runtime (the sky is a HideAndDontSave copy).
    // #308 step 2 (RealmInkSky308, TEST): the step-2 cloud character (_CloudInk, _Coverage, _Softness, _Stretch, _BandYaw, _StormYaw,
    // _StormGain, _WetEdge, _CoreInk, _MistHeight, _ZenithFade, _FogTint) is written only to a sky copy whose shader declares the
    // property and only when the profile carries step-2 data (state.Shape > 0); the far fog global follows FogTint only on such a
    // sky. InkCloudSky declares none of them, so the step-1 sky is unchanged (AC-S14). SetSkyMaterialOverride swaps the sky source
    // in memory only ([NonSerialized], never saved: Perf307 sky308 A/B, RegionSky308Capture render-ref sky=...).
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class WorldLookDriver : MonoBehaviour
    {
        [Tooltip("색 단일 출처 — ElementPalette_Test")]
        [SerializeField] private ElementPaletteSO _palette;
        [Tooltip("광맥 오행 초성 — 프롤로그 광맥 = 목(ㄱ, #152). 색은 팔레트 GetRawVeinColor로 파생(값 저장 금지)")]
        [SerializeField] private char _veinInitial = 'ㄱ';
        [Tooltip("봉수 오행 초성 — 강토 오행색 슬롯(LDB:100). 스파이크는 화(ㄴ) [TEST]")]
        [SerializeField] private char _beaconInitial = 'ㄴ';
        [Tooltip("하늘을 소지로 칠할 카메라 — 비우면 Camera.main")]
        [SerializeField] private Camera _skyCamera;
        [SerializeField] private bool _useSkybox;
        [SerializeField] private Material _skyboxMaterial;
        [SerializeField] private Oheangbu.Data.World.InkSkyProfile _inkSkyProfile;
        [SerializeField] private Oheangbu.Data.World.RegionalInkSkyProfile _regionalSkyProfile;
        [SerializeField] private Oheangbu.Data.World.WorldMacroSheetSO _skyGeography;
        [Header("#308 interior (scene ledger L3)")]
        [Tooltip("Same WorldMapBakedDataSO as the session map: a zone with walked passages (mine, root cave) holds the sky and ambient")]
        [SerializeField] private Oheangbu.App.World.UI.WorldMapBakedDataSO _interiorMap;
        [Tooltip("Baked sealed cave cells (Resources/Perf307): no exterior pixel is visible there, so sky writes are skipped")]
        [SerializeField] private Oheangbu.Data.World.InteriorSight307SO[] _sealedCells;

        private Material _regionalSkyInstance;
        private Material _regionalSkySource;
        // step-2 properties the current sky copy declares (bit per property, recomputed when the copy is made) and the source
        // material's own values of them (a state with Shape < 1 — some realm without step-2 data — mixes toward these)
        private int _shapeProperties;
        private Oheangbu.Data.World.RegionalInkSkyProfile.SkyState _shapeBase;
        // in-memory sky source override (tools only); never serialized, so it cannot leak into a saved scene
        [System.NonSerialized] private Material _skyOverride;
        private readonly Oheangbu.Data.World.RegionalSkyStepper _sky = new Oheangbu.Data.World.RegionalSkyStepper();
        private Material SkySource => _skyOverride != null ? _skyOverride : _skyboxMaterial;
        private bool HasRegionalSky => _useSkybox && SkySource != null &&
            _regionalSkyProfile != null && _skyGeography != null && _inkSkyProfile != null;

        private static readonly int InkId = Shader.PropertyToID("_OhInkColor");
        private static readonly int PaperId = Shader.PropertyToID("_OhPaperColor");
        private static readonly int WoodId = Shader.PropertyToID("_OhWoodColor");
        private static readonly int VeinId = Shader.PropertyToID("_OhVeinColor");
        private static readonly int BeaconId = Shader.PropertyToID("_OhBeaconColor");
        private static readonly int WorldPostId = Shader.PropertyToID("_OhWorldPost");
        private static readonly int SkyHorizonId = Shader.PropertyToID("_OhSkyHorizon");
        private static readonly int SkyFogId = Shader.PropertyToID("_OhSkyFog");
        private static readonly int HorizonId = Shader.PropertyToID("_Horizon");
        private static readonly int ZenithId = Shader.PropertyToID("_Zenith");
        private static readonly int CloudId = Shader.PropertyToID("_Cloud");
        private static readonly int CloudDensityId = Shader.PropertyToID("_CloudDensity");
        private static readonly int CapitalAzimuthId = Shader.PropertyToID("_CapitalAzimuth");
        private static readonly int CapitalAtmosphereId = Shader.PropertyToID("_CapitalAtmosphere");
        private static readonly int MistHeightId = Shader.PropertyToID("_MistHeight");
        // #308 step 2 (RealmInkSky308)
        private static readonly int CloudInkId = Shader.PropertyToID("_CloudInk");
        private static readonly int CoverageId = Shader.PropertyToID("_Coverage");
        private static readonly int SoftnessId = Shader.PropertyToID("_Softness");
        private static readonly int StretchId = Shader.PropertyToID("_Stretch");
        private static readonly int BandYawId = Shader.PropertyToID("_BandYaw");
        private static readonly int StormYawId = Shader.PropertyToID("_StormYaw");
        private static readonly int StormGainId = Shader.PropertyToID("_StormGain");
        private static readonly int WetEdgeId = Shader.PropertyToID("_WetEdge");
        private static readonly int CoreInkId = Shader.PropertyToID("_CoreInk");
        private static readonly int ZenithFadeId = Shader.PropertyToID("_ZenithFade");
        private static readonly int FogTintId = Shader.PropertyToID("_FogTint");
        private const int CloudInkBit = 1, CoverageBit = 2, SoftnessBit = 4, StretchBit = 8, BandYawBit = 16, StormYawBit = 32,
            StormGainBit = 64, WetEdgeBit = 128, CoreInkBit = 256, MistHeightBit = 512, ZenithFadeBit = 1024, FogTintBit = 2048;
        // a step-2 sky declares all twelve (RealmInkSky308). A shader with only some of them — Teaser300/InkWashSky300 has ten with
        // other meanings (e.g. _BandYaw, _MistHeight) — gets none: values are never written into a sky whose semantics differ.
        private const int AllShapeBits = 4095;
        // measurement only (Perf307 reads it by name; routes.json customMarkers lists it once the #307 track adds it). Immutable.
        private static readonly ProfilerMarker WorldLookMarker = new ProfilerMarker(ProfilerCategory.Scripts, "Oh.WorldLook");

        public ElementPaletteSO Palette => _palette;
        /// <summary>#308 checks (AC-S10): regional sky material writes by this instance (not static, not persisted).</summary>
        public int SkyWriteCount { get; private set; }
        /// <summary>#308 checks: the live regional sky state, whether it is held (interior) and the capital bearing in radians.</summary>
        public Oheangbu.Data.World.RegionalInkSkyProfile.SkyState RegionalSkyState => _sky.State;
        public bool RegionalSkyHolding => _sky.Holding;
        public bool RegionalSkySealed { get; private set; }
        public float CapitalAzimuthRadians => _sky.CapitalAzimuth;
        /// <summary>#308: the sky material the regional copy is made from (the in-memory override, else _skyboxMaterial).</summary>
        public Material SkyMaterial => SkySource;
        /// <summary>#308: the in-memory override (null = the serialized _skyboxMaterial).</summary>
        public Material SkyMaterialOverride => _skyOverride;
        /// <summary>#308: the transient (HideAndDontSave) regional sky copy, null before the first write.</summary>
        public Material RegionalSkyInstance => _regionalSkyInstance;
        /// <summary>#308 step 2: true when the current sky copy declares step-2 properties (RealmInkSky308).</summary>
        public bool StepTwoSky => _shapeProperties == AllShapeBits;

        /// <summary>#308 tools: swaps the sky source in memory only (null clears). Not serialized, nothing is dirtied or saved; the
        /// transient copy is rebuilt from the new source on the next write (Perf307 sky308 A/B, render-ref sky=...).</summary>
        public void SetSkyMaterialOverride(Material material)
        {
            if (_skyOverride == material) return;
            _skyOverride = material;
            if (isActiveAndEnabled) Apply();
        }

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled) { _sky.InvalidateGeometry(); Apply(); }
        }

        private void Update()
        {
            // 에디터에서 팔레트 에셋 편집을 즉시 반영(SetGlobal 6회 — 스파이크 범위의 비용). 플레이 중엔 OnEnable 1회로 충분
            if (!Application.isPlaying && !HasRegionalSky) Apply();
        }

        private void LateUpdate()
        {
            // Sample the final camera position after the movement controller's Update.
            if (!HasRegionalSky) return;
            using (WorldLookMarker.Auto())
            {
                if (!Application.isPlaying) Apply();
                else
                {
                    var camera = _skyCamera != null ? _skyCamera : Camera.main;
                    if (camera != null) ApplyRegionalSky(camera.transform.position, Time.deltaTime, false);
                }
            }
        }

        private void OnDisable()
        {
            Shader.SetGlobalFloat(WorldPostId, 0f);
            ReleaseRegionalSky();
        }

        private void ReleaseRegionalSky()
        {
            if (_regionalSkyInstance != null)
            {
                if (RenderSettings.skybox == _regionalSkyInstance) RenderSettings.skybox = SkySource;
                if (Application.isPlaying) Destroy(_regionalSkyInstance); else DestroyImmediate(_regionalSkyInstance);
            }
            _regionalSkyInstance = null; _regionalSkySource = null; _shapeProperties = 0; _shapeBase = default; _sky.Reset();
            // no regional sky: the screen passes' _SkyFogFollow sees alpha 0 and keeps the material fog (never a stale/black fog)
            Shader.SetGlobalColor(SkyFogId, Color.clear);
            Shader.SetGlobalColor(SkyHorizonId, Color.clear);
        }

        private void EnsureRegionalSky()
        {
            var source = SkySource;
            if (_regionalSkyInstance != null && _regionalSkySource == source) return;
            ReleaseRegionalSky();
            _regionalSkySource = source;
            _regionalSkyInstance = new Material(source)
            { name = "Regional ink sky (transient)", hideFlags = HideFlags.HideAndDontSave };
            _shapeProperties = ShapeProperties(_regionalSkyInstance);
            _shapeBase = ShapeBase(_regionalSkyInstance, _shapeProperties);
        }

        private static Oheangbu.Data.World.RegionalInkSkyProfile.SkyState ShapeBase(Material m, int p)
        {
            var b = new Oheangbu.Data.World.RegionalInkSkyProfile.SkyState { Shape = 1f };
            if ((p & CloudInkBit) != 0) b.CloudInk = m.GetColor(CloudInkId);
            if ((p & CoverageBit) != 0) b.Coverage = m.GetFloat(CoverageId);
            if ((p & SoftnessBit) != 0) b.Softness = m.GetFloat(SoftnessId);
            if ((p & StretchBit) != 0) b.Stretch = m.GetFloat(StretchId);
            if ((p & BandYawBit) != 0) b.Band = Oheangbu.Data.World.RegionalInkSkyProfile.YawVector(m.GetFloat(BandYawId));
            if ((p & StormYawBit) != 0) b.Storm = Oheangbu.Data.World.RegionalInkSkyProfile.YawVector(m.GetFloat(StormYawId));
            if ((p & StormGainBit) != 0) b.StormGain = m.GetFloat(StormGainId);
            if ((p & WetEdgeBit) != 0) b.WetEdge = m.GetFloat(WetEdgeId);
            if ((p & CoreInkBit) != 0) b.CoreInk = m.GetFloat(CoreInkId);
            if ((p & MistHeightBit) != 0) b.Mist = m.GetFloat(MistHeightId);
            if ((p & ZenithFadeBit) != 0) b.ZenithFade = m.GetFloat(ZenithFadeId);
            return b;
        }

        private static int ShapeProperties(Material m)
        {
            int bits = 0;
            if (m.HasProperty(CloudInkId)) bits |= CloudInkBit;
            if (m.HasProperty(CoverageId)) bits |= CoverageBit;
            if (m.HasProperty(SoftnessId)) bits |= SoftnessBit;
            if (m.HasProperty(StretchId)) bits |= StretchBit;
            if (m.HasProperty(BandYawId)) bits |= BandYawBit;
            if (m.HasProperty(StormYawId)) bits |= StormYawBit;
            if (m.HasProperty(StormGainId)) bits |= StormGainBit;
            if (m.HasProperty(WetEdgeId)) bits |= WetEdgeBit;
            if (m.HasProperty(CoreInkId)) bits |= CoreInkBit;
            if (m.HasProperty(MistHeightId)) bits |= MistHeightBit;
            if (m.HasProperty(ZenithFadeId)) bits |= ZenithFadeBit;
            if (m.HasProperty(FogTintId)) bits |= FogTintBit;
            return bits;
        }

        private bool Interior(Vector3 eye)
        {
            if (_interiorMap == null) return false;
            var zone = _interiorMap.ZoneAt(eye);
            return zone != null && zone.ExploreWalkedPassages;
        }

        private bool Sealed(Vector3 eye)
        {
            if (_sealedCells == null) return false;
            for (int i = 0; i < _sealedCells.Length; i++)
                if (_sealedCells[i] != null && _sealedCells[i].IsSealed(eye)) return true;
            return false;
        }

        private void ApplyRegionalSky(Vector3 position, float dt, bool snap)
        {
            EnsureRegionalSky();
            // interior hold and sealed skip are Play behaviour; edit mode keeps evaluating the camera spot (#297 preview)
            bool playing = Application.isPlaying;
            bool changed = _sky.Step(_regionalSkyProfile, _skyGeography, position, dt, playing && Interior(position), snap);
            // inside a sealed cave cell no exterior pixel is drawn: no material write (the state above still advances). Exceptions:
            // nothing written yet (a start inside the mine must still get its sky + ambient once) and a snap (teleport/respawn
            // into the cell: the ambient takes the evaluated value of that spot once, then holds — Spec 실내 고정 판정 1).
            RegionalSkySealed = playing && Sealed(position);
            if (RegionalSkySealed && !_sky.Snapped && RenderSettings.skybox == _regionalSkyInstance) return;
            if (!changed && _sky.Holding && RenderSettings.skybox == _regionalSkyInstance) return;
            WriteRegionalSky(_sky.State, _sky.CapitalAzimuth, _sky.HasCapitalBearing);
        }

        private void WriteRegionalSky(Oheangbu.Data.World.RegionalInkSkyProfile.SkyState state, float capitalAzimuth, bool hasBearing)
        {
            // A single shared cloud speed/scale preserves the existing _Time phase across all regions.
            _inkSkyProfile.Apply(_regionalSkyInstance);
            _regionalSkyInstance.SetColor(HorizonId, state.Horizon);
            _regionalSkyInstance.SetColor(ZenithId, state.Zenith);
            _regionalSkyInstance.SetColor(CloudId, state.Cloud);
            _regionalSkyInstance.SetFloat(CloudDensityId, state.CloudDensity);
            // #308 capital anchor: only when the regional profile owns it (0 keeps the shared sky profile's fixed bearing and value)
            if (_regionalSkyProfile.CapitalAtmosphere > 0f && hasBearing)
            {
                _regionalSkyInstance.SetFloat(CapitalAzimuthId, capitalAzimuth);
                _regionalSkyInstance.SetFloat(CapitalAtmosphereId, state.Capital);
            }
            // step-2 sky properties: only when the copy declares all of them AND the profile carries step-2 data (InkCloudSky has
            // none; a profile without step-2 data leaves the step-2 material's own values)
            bool shaped = _shapeProperties == AllShapeBits && state.Shape > 0f;
            if (shaped) WriteShape(_regionalSkyInstance, state);
            RenderSettings.skybox = _regionalSkyInstance;
            // screen passes (RealmFog297 _SkyFogFollow, later phases): globals have no sRGB conversion, so linear values.
            // The far fog follows the step-2 FogTint only on a step-2 sky; the step-1 sky keeps fog = blended horizon.
            Shader.SetGlobalColor(SkyHorizonId, state.Horizon.linear);
            Shader.SetGlobalColor(SkyFogId, (shaped ? state.Fog : state.Horizon).linear);
            SkyWriteCount++;
            // Gradient ambient follows the same blended realm sky; a flat-ink profile keeps its constant ambient.
            if (_inkSkyProfile.GradientAmbient && !SameSky(state, _ambientState))
            {
                _inkSkyProfile.ApplyEnvironment(state.Zenith, state.Horizon);
                _ambientState = state;
            }
        }

        private void WriteShape(Material m, Oheangbu.Data.World.RegionalInkSkyProfile.SkyState state)
        {
            int p = _shapeProperties;
            // every realm with step-2 data: Shape is exactly 1 and the state is written as is; otherwise the share without data
            // keeps the material's own values (continuous, never a jump to a neighbour's character)
            var s = state.Shape >= 1f ? state : Oheangbu.Data.World.RegionalInkSkyProfile.Blend(_shapeBase, state, state.Shape);
            if ((p & CloudInkBit) != 0) m.SetColor(CloudInkId, s.CloudInk);
            if ((p & CoverageBit) != 0) m.SetFloat(CoverageId, s.Coverage);
            if ((p & SoftnessBit) != 0) m.SetFloat(SoftnessId, s.Softness);
            if ((p & StretchBit) != 0) m.SetFloat(StretchId, s.Stretch);
            if ((p & BandYawBit) != 0) m.SetFloat(BandYawId, s.BandYaw);
            if ((p & StormYawBit) != 0) m.SetFloat(StormYawId, s.StormYaw);
            if ((p & StormGainBit) != 0) m.SetFloat(StormGainId, s.StormGain);
            if ((p & WetEdgeBit) != 0) m.SetFloat(WetEdgeId, s.WetEdge);
            if ((p & CoreInkBit) != 0) m.SetFloat(CoreInkId, s.CoreInk);
            if ((p & MistHeightBit) != 0) m.SetFloat(MistHeightId, s.Mist);
            if ((p & ZenithFadeBit) != 0) m.SetFloat(ZenithFadeId, s.ZenithFade);
            if ((p & FogTintBit) != 0) m.SetColor(FogTintId, new Color(state.Fog.r, state.Fog.g, state.Fog.b, 1f));
        }

        private Oheangbu.Data.World.RegionalInkSkyProfile.SkyState _ambientState;
        private static bool SameSky(Oheangbu.Data.World.RegionalInkSkyProfile.SkyState a, Oheangbu.Data.World.RegionalInkSkyProfile.SkyState b)
        {
            // 1/512 per channel is below an 8-bit step of the ambient colours.
            return Mathf.Abs(a.Zenith.r - b.Zenith.r) + Mathf.Abs(a.Zenith.g - b.Zenith.g) + Mathf.Abs(a.Zenith.b - b.Zenith.b)
                 + Mathf.Abs(a.Horizon.r - b.Horizon.r) + Mathf.Abs(a.Horizon.g - b.Horizon.g) + Mathf.Abs(a.Horizon.b - b.Horizon.b) < 1f / 512f;
        }

        /// <summary>Explicit editor/capture viewpoint. Does not change the live transition state.</summary>
        public void PreviewRegionalSky(Vector3 position)
        {
            if (!HasRegionalSky) return;
            EnsureRegionalSky();
            var state = _sky.Evaluate(_regionalSkyProfile, _skyGeography, position);
            bool bearing = Oheangbu.Data.World.RegionalInkSkyProfile.Centroid(_skyGeography, _regionalSkyProfile.CapitalRealm, out var c);
            WriteRegionalSky(state, bearing ? Oheangbu.Data.World.RegionalInkSkyProfile.AzimuthTo(position, c) : 0f, bearing);
        }

        // 실측(2026-09-06, cut1_raw.png): Shader.SetGlobalColor는 Properties에 선언되지 않은 전역에 sRGB→리니어 변환을 하지 않는다 —
        // 먹 #2A2622(0.165)가 리니어 0.165로 들어가 화면에 (113,108,103) 회색으로 나왔다. 팔레트 값은 sRGB 표기이므로 .linear로 넘긴다.
        // Camera.backgroundColor는 엔진이 변환한다(같은 실측에서 배경 = 247,241,228 정확) — 그대로 준다.
        public void Apply()
        {
            if (_palette == null) return;
            Shader.SetGlobalColor(InkId, _palette.InkColor.linear);
            Shader.SetGlobalColor(PaperId, _palette.PaperColor.linear);
            Shader.SetGlobalColor(WoodId, _palette.WoodColor.linear);
            Shader.SetGlobalColor(VeinId, _palette.GetRawVeinColor(_veinInitial).linear);
            Shader.SetGlobalColor(BeaconId, _palette.GetRawVeinColor(_beaconInitial).linear);
            Shader.SetGlobalFloat(WorldPostId, 1f);

            var camera = _skyCamera != null ? _skyCamera : Camera.main;
            if (camera != null)
            {
                camera.clearFlags = _useSkybox && SkySource != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                camera.backgroundColor = _palette.PaperColor;
                if (_useSkybox && SkySource != null)
                {
                    if(HasRegionalSky)
                    {
                        // A gradient profile takes its ambient from the regional sky written below.
                        if(!_inkSkyProfile.GradientAmbient)_inkSkyProfile.ApplyEnvironment();
                        else _ambientState=default;
                        ApplyRegionalSky(camera.transform.position, 0, !Application.isPlaying);
                    }
                    else
                    {
                        ReleaseRegionalSky();
                        if(_inkSkyProfile!=null){_inkSkyProfile.Apply(SkySource);_inkSkyProfile.ApplyEnvironment();}
                        RenderSettings.skybox = SkySource;
                    }
                }
                else ReleaseRegionalSky();
            }
        }
    }
}
