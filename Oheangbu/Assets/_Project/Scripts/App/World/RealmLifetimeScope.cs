using Oheangbu.Data.World;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Oheangbu.App.World
{
    // [SPEC-WORLD-MAP §4 층 11] 강토 스코프(부모) — FarSet 상주 씬. 강토 단위 데이터(스케일·시트·전이 채널)를 인스턴스로 등록하고
    // 같은 씬의 WorldScaleDriver·AreaLoader에 주입한다. 구역 씬의 AreaLifetimeScope가 EnqueueParent로 이 스코프를 부모로 삼는다.
    // 싱글턴 0: 수명은 VContainer가 관리(CombatLifetimeScope 최소형 계약 승계).
    public sealed class RealmLifetimeScope : LifetimeScope
    {
        [Tooltip("강토 스케일 캐논(층 1)")]
        [SerializeField] private WorldScaleSO _worldScale;
        [Tooltip("강토 시트(층 2) — AreaLoader가 areaId로 구역을 찾는다")]
        [SerializeField] private RealmSheetSO _realmSheet;
        [Tooltip("구역 전이 채널 — AreaTransitionVolume 발화만, 구독자 0(A8)")]
        [SerializeField] private AreaIdEventChannelSO _areaChannel;

        protected override void Configure(IContainerBuilder builder)
        {
            RegisterOrReport(builder, _worldScale, nameof(_worldScale));
            RegisterOrReport(builder, _realmSheet, nameof(_realmSheet));
            RegisterOrReport(builder, _areaChannel, nameof(_areaChannel));
            builder.RegisterComponentInHierarchy<WorldScaleDriver>();
            builder.RegisterComponentInHierarchy<AreaLoader>();
        }

        // 비어 있는 참조는 RegisterInstance(null)의 불명확한 예외 대신 필드 이름으로 보고하고 건너뛴다 — 해석 실패는 소비자 주입 시 타입명으로 드러난다.
        private void RegisterOrReport<T>(IContainerBuilder builder, T instance, string fieldName) where T : Object
        {
            if (instance == null)
            {
                Debug.LogError($"[World] RealmLifetimeScope.{fieldName} 미지정 — {typeof(T).Name} 미등록", this);
                return;
            }
            builder.RegisterInstance(instance);
        }
    }
}
