using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Oheangbu.App
{
    // 프로젝트 전체 DI의 뿌리 스코프 — 모듈 등록은 앞으로 여기서만 모은다.
    // 전역 싱글턴 금지(CLAUDE.md)의 대체 구조: 수명은 VContainer가 관리한다.
    public sealed class RootLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            // 아직 등록할 모듈이 없다 — 걷는 뼈대 단계.
        }

        protected override void Awake()
        {
            base.Awake();
            // 부팅 확인용 단 한 줄 — Bootstrap 씬에 배치돼 컨테이너가 살아났는지 확인한다.
            Debug.Log("[Oheangbu] RootLifetimeScope 부팅 완료");
        }
    }
}
