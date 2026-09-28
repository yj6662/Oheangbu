using Oheangbu.BrushRender;
using UnityEngine;

namespace Oheangbu.App
{
    // [SPEC-SPIKE-WORLD-LOOKDEV §5-8] 월드 룩 색 공급 — 팔레트 SO → _Oh* 셰이더 전역, 화면의 모든 환경 색이 지나는 1경로(#153).
    // 머티리얼·씬 에셋에 색 값 0: InkWorld/InkLightSource는 프로퍼티에 색이 없고 이 전역만 읽는다.
    // 전역 기본값이 검정이라 이 컴포넌트가 없는 씬에서 InkWorld는 검정이다(#155 결합 명시 — Stage 1 씬도 필수).
    // 하늘 = 카메라 SolidColor 소지(#150·#154). 광맥·봉수 색은 초성(의미)만 들고 팔레트 원석 탁화로 파생한다(#152).
    // [ExecuteAlways]: 에디터 씬 뷰·인스펙터 튜닝에서도 같은 색. OnDisable → _OhWorldPost 0(포스트는 분기로 원본 반환).
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

        private Material _regionalSkyInstance;
        private Material _regionalSkySource;
        private Oheangbu.Data.World.RegionalInkSkyProfile.SkyState _skyState;
        private bool _hasSkyState;
        private bool HasRegionalSky => _useSkybox && _skyboxMaterial != null &&
            _regionalSkyProfile != null && _skyGeography != null && _inkSkyProfile != null;

        private static readonly int InkId = Shader.PropertyToID("_OhInkColor");
        private static readonly int PaperId = Shader.PropertyToID("_OhPaperColor");
        private static readonly int WoodId = Shader.PropertyToID("_OhWoodColor");
        private static readonly int VeinId = Shader.PropertyToID("_OhVeinColor");
        private static readonly int BeaconId = Shader.PropertyToID("_OhBeaconColor");
        private static readonly int WorldPostId = Shader.PropertyToID("_OhWorldPost");

        public ElementPaletteSO Palette => _palette;

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled) Apply();
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
            if (!Application.isPlaying) Apply();
            else
            {
                var camera = _skyCamera != null ? _skyCamera : Camera.main;
                if (camera != null) ApplyRegionalSky(camera.transform.position, Time.deltaTime, false);
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
                if (RenderSettings.skybox == _regionalSkyInstance) RenderSettings.skybox = _skyboxMaterial;
                if (Application.isPlaying) Destroy(_regionalSkyInstance); else DestroyImmediate(_regionalSkyInstance);
            }
            _regionalSkyInstance = null; _regionalSkySource = null; _hasSkyState = false;
        }

        private void EnsureRegionalSky()
        {
            if (_regionalSkyInstance != null && _regionalSkySource == _skyboxMaterial) return;
            ReleaseRegionalSky();
            _regionalSkySource = _skyboxMaterial;
            _regionalSkyInstance = new Material(_skyboxMaterial)
            { name = "Regional ink sky (transient)", hideFlags = HideFlags.HideAndDontSave };
        }

        private void ApplyRegionalSky(Vector3 position, float dt, bool snap)
        {
            EnsureRegionalSky();
            var target = _regionalSkyProfile.Evaluate(_skyGeography, position);
            float weight = 1f - Mathf.Exp(-Mathf.Max(0, dt) / Mathf.Max(.01f, _regionalSkyProfile.ResponseSeconds));
            _skyState = snap || !_hasSkyState ? target :
                Oheangbu.Data.World.RegionalInkSkyProfile.Blend(_skyState, target, weight);
            _hasSkyState = true;
            WriteRegionalSky(_skyState);
        }

        private void WriteRegionalSky(Oheangbu.Data.World.RegionalInkSkyProfile.SkyState state)
        {
            // A single shared cloud speed/scale preserves the existing _Time phase across all regions.
            _inkSkyProfile.Apply(_regionalSkyInstance);
            _regionalSkyInstance.SetColor("_Horizon", state.Horizon);
            _regionalSkyInstance.SetColor("_Zenith", state.Zenith);
            _regionalSkyInstance.SetColor("_Cloud", state.Cloud);
            _regionalSkyInstance.SetFloat("_CloudDensity", state.CloudDensity);
            RenderSettings.skybox = _regionalSkyInstance;
            // Gradient ambient follows the same blended realm sky; a flat-ink profile keeps its constant ambient.
            if (_inkSkyProfile.GradientAmbient && !SameSky(state, _ambientState))
            {
                _inkSkyProfile.ApplyEnvironment(state.Zenith, state.Horizon);
                _ambientState = state;
            }
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
            WriteRegionalSky(_regionalSkyProfile.Evaluate(_skyGeography, position));
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
                camera.clearFlags = _useSkybox && _skyboxMaterial != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                camera.backgroundColor = _palette.PaperColor;
                if (_useSkybox && _skyboxMaterial != null)
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
                        if(_inkSkyProfile!=null){_inkSkyProfile.Apply(_skyboxMaterial);_inkSkyProfile.ApplyEnvironment();}
                        RenderSettings.skybox = _skyboxMaterial;
                    }
                }
                else ReleaseRegionalSky();
            }
        }
    }
}
