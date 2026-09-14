using Oheangbu.Data.World;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Oheangbu.App.World
{
    // [SPEC-WORLD-MAP §4 층 11] 구역 스코프(자식) — Additive 구역 씬 루트. Awake에서 base보다 먼저 RealmLifetimeScope를 찾아
    // LifetimeScope.EnqueueParent로 부모 큐에 넣는다(직렬화 parentReference는 씬 경계를 못 넘는다).
    // 주의(caveat): FindFirstObjectByType은 「로드된 모든 씬」을 뒤진다 — FarSet이 먼저 로드돼 있어야 하며(AreaLoader Additive 순서가 보장),
    // 구역 씬을 단독으로 열어 플레이하면 부모 없이 자기 등록만으로 빌드된다(강토 인스턴스 해석 실패 = 주입 시 타입명으로 드러남).
    // 강토 스코프가 2개면 첫 것을 잡는다 — FarSet 상주 1개 계약(층 11 「Driver 합산 정확히 1」과 같은 규율).
    public sealed class AreaLifetimeScope : LifetimeScope
    {
        [Tooltip("이 구역의 배치 시트(층 2)")]
        [SerializeField] private AreaSheetSO _areaSheet;

        protected override void Awake()
        {
            var parent = FindFirstObjectByType<RealmLifetimeScope>(FindObjectsInactive.Exclude);
            if (parent == null)
            {
                Debug.LogWarning("[World] AreaLifetimeScope: RealmLifetimeScope 없음 — 부모 없이 빌드(구역 씬 단독 플레이?)", this);
                base.Awake();
                return;
            }
            using (EnqueueParent(parent))
            {
                base.Awake();
            }
        }

        protected override void Configure(IContainerBuilder builder)
        {
            if (_areaSheet == null)
            {
                Debug.LogError("[World] AreaLifetimeScope._areaSheet 미지정 — AreaSheetSO 미등록", this);
            }
            else
            {
                builder.RegisterInstance(_areaSheet);
            }
        }
    }
}
