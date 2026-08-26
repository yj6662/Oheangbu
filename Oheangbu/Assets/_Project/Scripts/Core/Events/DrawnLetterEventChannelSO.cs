using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.Core.Events
{
    // 작도 커밋(글자 인식 성공) 방송 채널 — Drawing이 발행하고 Spellcraft/Combat이 구독할 경계.
    // 채널이 Core에 있는 이유: 발행자(Drawing)와 미래 구독자(Spellcraft)가 서로를 모른 채 만나는 자리이기 때문.
    [CreateAssetMenu(menuName = "Oheangbu/Events/Drawn Letter Event Channel", fileName = "DrawnLetterEventChannel")]
    public sealed class DrawnLetterEventChannelSO : EventChannelSO<DrawnLetter>
    {
    }
}
