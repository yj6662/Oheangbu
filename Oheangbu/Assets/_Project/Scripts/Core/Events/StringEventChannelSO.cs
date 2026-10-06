using UnityEngine;

namespace Oheangbu.Core.Events
{
    // 글자열 하나(예: 시퀀스 id)를 싣는 채널. #308 그림 시네마틱(SPEC-CINEMATIC-STILLS-308 7.4)의 Requested / Started / Finished 가 쓴다.
    // 도메인 리로드가 꺼져 있어 SO 는 Play 를 넘어 산다 — 구독은 반드시 짝으로 푼다.
    [CreateAssetMenu(menuName = "Oheangbu/Events/String Event Channel", fileName = "StringEventChannel")]
    public sealed class StringEventChannelSO : EventChannelSO<string>
    {
    }
}
