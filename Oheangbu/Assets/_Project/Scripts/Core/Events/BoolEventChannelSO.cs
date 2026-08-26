using UnityEngine;

namespace Oheangbu.Core.Events
{
    // 켜짐/꺼짐 상태 변화를 알리는 채널(예: 작도 모드 진입·이탈).
    // 제네릭 베이스의 에셋화 통로 — 새 페이로드 형이 필요하면 이 파일을 본보기로 파생형을 추가한다.
    [CreateAssetMenu(menuName = "Oheangbu/Events/Bool Event Channel", fileName = "BoolEventChannel")]
    public sealed class BoolEventChannelSO : EventChannelSO<bool>
    {
    }
}
