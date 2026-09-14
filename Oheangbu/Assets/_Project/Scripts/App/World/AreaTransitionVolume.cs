using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    // [SPEC-WORLD-MAP §4 층 11] 구역 전이 볼륨 — 심부 방향 트리거. 플레이어(CharacterController 계층) 진입 시
    // AreaIdEventChannelSO에 대상 areaId를 발화만 한다. 구독자 0 · 리셋 훅 0(휴식 리셋 단위 TBD, A8) · 씬 전환은 후속 소비자 몫.
    // 감지 = CharacterController(리그 Player) — 태그 의존 0(TagManager에 Player 태그 없음).
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class AreaTransitionVolume : MonoBehaviour
    {
        [Tooltip("전이 채널(발화만)")]
        public AreaIdEventChannelSO channel;
        [Tooltip("대상 구역 ID(AreaSheetSO.areaId)")]
        public string targetAreaId = "";

        private void Reset()
        {
            EnsureTrigger();
        }

        private void OnValidate()
        {
            EnsureTrigger();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<CharacterController>() == null) return;
            if (channel == null)
            {
                Debug.LogWarning($"[World] AreaTransitionVolume '{name}': 채널 미지정 — 발화 생략", this);
                return;
            }
            channel.Raise(new AreaId(targetAreaId));
        }

        private void EnsureTrigger()
        {
            var collider = GetComponent<Collider>();
            if (collider != null) collider.isTrigger = true;
        }
    }
}
