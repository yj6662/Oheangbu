using UnityEngine;

namespace Oheangbu.Core.Events
{
    // 실수 하나를 나르는 범용 채널 — 첫 소비자는 먹 잔량(0..1) 방송(COMBAT-HARVEST 먹 3원).
    [CreateAssetMenu(menuName = "Oheangbu/Events/Float Event Channel", fileName = "FloatEventChannel")]
    public sealed class FloatEventChannelSO : EventChannelSO<float>
    {
    }
}
