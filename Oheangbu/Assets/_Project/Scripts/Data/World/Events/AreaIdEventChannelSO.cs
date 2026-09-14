using Oheangbu.Core.Events;
using UnityEngine;

namespace Oheangbu.Data.World
{
    // [SPEC-WORLD-MAP §4 층 2·층 11] 구역 전이 채널 — AreaTransitionVolume(App)이 발화만, 구독자 0(휴식 리셋 단위 TBD A8).
    // Core 제네릭 EventChannelSO<T>의 에셋화 통로(BoolEventChannelSO 본보기) — Core 무변경.
    [CreateAssetMenu(menuName = "Oheangbu/World/Area Id Event Channel", fileName = "EC_AreaId")]
    public sealed class AreaIdEventChannelSO : EventChannelSO<AreaId>
    {
    }
}
