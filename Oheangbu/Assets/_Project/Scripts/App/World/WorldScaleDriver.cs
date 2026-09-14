using Oheangbu.Data.World;
using UnityEngine;
using VContainer;

namespace Oheangbu.App.World
{
    // [SPEC-WORLD-MAP §4 층 1] 강토 스케일 공급 — WorldScaleSO → 카메라 far + _Oh* 페이드 전역(스파이크 §5-8 WorldLookDriver 거울).
    // 직렬화 SO 필드 = 정본(에디터 모드에서도 동작). 플레이 시 RealmLifetimeScope가 같은 SO를 [Inject]로 덮어쓸 수 있다 — 싱글턴 0.
    // 전역 5종은 SO 파생값(PaperFadeStartM/EndM = 최원경 반경 × 비율, D7)에서만 읽는다 — 코드 수치 0(§6 A9).
    // _OhScaleDriven = 1: 스파이크 P2 InkWorldPost가 >0.5면 전역을, 아니면 재질값을 쓴다(§10-4). OnDisable → 0(스파이크 씬 A10 누출 0).
    // far clip은 플레이 모드에서만 덮어쓴다(§10-3 「런타임에 farClipPlane 덮어씀」 — 리그 프리팹 far 1000 무변경 계약, 에디터 인스턴스 오버라이드 0).
    // WorldLookDriver와 합산 정확히 1(FarSet 상주 씬, 층 11).
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class WorldScaleDriver : MonoBehaviour
    {
        [Tooltip("강토 스케일 캐논 — 정본(에디터 모드 동작). 플레이 시 LifetimeScope 주입이 같은 SO로 덮어쓸 수 있다")]
        [SerializeField] private WorldScaleSO _scale;
        [Tooltip("far clip을 덮어쓸 카메라 — 비우면 Camera.main")]
        [SerializeField] private Camera _camera;

        private static readonly int PaperFadeStartId = Shader.PropertyToID("_OhPaperFadeStart");
        private static readonly int PaperFadeEndId = Shader.PropertyToID("_OhPaperFadeEnd");
        private static readonly int PaperKeepId = Shader.PropertyToID("_OhPaperKeep");
        private static readonly int PaperStrengthId = Shader.PropertyToID("_OhPaperStrength");
        private static readonly int NormalEdgeFadeId = Shader.PropertyToID("_OhNormalEdgeFade");
        private static readonly int ScaleDrivenId = Shader.PropertyToID("_OhScaleDriven");

        public WorldScaleSO Scale => _scale;

        // 플레이 모드 DI 경로 — RealmLifetimeScope.RegisterInstance(WorldScaleSO)가 같은 에셋을 넘긴다. null이면 직렬화 필드 유지.
        [Inject]
        public void Construct(WorldScaleSO scale)
        {
            if (scale != null) _scale = scale;
            if (isActiveAndEnabled) Apply();
        }

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
            // 에디터에서 SO 편집을 즉시 반영(WorldLookDriver와 같은 비용 계약). 플레이 중엔 OnEnable/Construct 1회로 충분.
            if (!Application.isPlaying) Apply();
        }

        private void OnDisable()
        {
            Shader.SetGlobalFloat(ScaleDrivenId, 0f);
        }

        public void Apply()
        {
            if (_scale == null) return;

            Shader.SetGlobalFloat(PaperFadeStartId, _scale.PaperFadeStartM);
            Shader.SetGlobalFloat(PaperFadeEndId, _scale.PaperFadeEndM);
            Shader.SetGlobalFloat(PaperKeepId, _scale.PaperKeep);
            Shader.SetGlobalFloat(PaperStrengthId, _scale.PaperStrength);
            Shader.SetGlobalFloat(NormalEdgeFadeId, _scale.NormalEdgeFadeDistance);
            Shader.SetGlobalFloat(ScaleDrivenId, 1f);

            if (!Application.isPlaying) return;
            var camera = _camera != null ? _camera : Camera.main;
            if (camera != null)
            {
                camera.farClipPlane = _scale.CameraFar;
            }
        }
    }
}
