using System;
using UnityEngine;

namespace Oheangbu.Core.Events
{
    // 페이로드 없는 신호 전용 채널("일어났다"만 전한다).
    // C#의 void는 형식 인자가 될 수 없어 제네릭 베이스와 별도 구현으로 둔다.
    [CreateAssetMenu(menuName = "Oheangbu/Events/Void Event Channel", fileName = "VoidEventChannel")]
    public sealed class VoidEventChannelSO : ScriptableObject
    {
        private event Action _onRaised;

        public void Raise()
        {
            _onRaised?.Invoke();
        }

        public void Subscribe(Action handler)
        {
            _onRaised += handler;
        }

        public void Unsubscribe(Action handler)
        {
            _onRaised -= handler;
        }
    }
}
