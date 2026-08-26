using System;
using UnityEngine;

namespace Oheangbu.Core.Events
{
    // 페이로드를 싣는 SO 이벤트 채널의 제네릭 베이스.
    // 발신자와 수신자가 서로를 모른 채 에셋 하나를 공유해 통신한다 —
    // 전역 싱글턴·God Manager 금지(CLAUDE.md) 아래에서 모듈 경계를 넘는 유일한 통로.
    // 제네릭이라 CreateAssetMenu를 붙일 수 없다 — 에셋화는 구체 파생형(Bool 등)이 담당한다.
    public abstract class EventChannelSO<T> : ScriptableObject
    {
        private event Action<T> _onRaised;

        // 방송. 구독자가 없어도 안전하게 지나간다(부팅 순서에 대한 결합 제거).
        public void Raise(T payload)
        {
            _onRaised?.Invoke(payload);
        }

        public void Subscribe(Action<T> handler)
        {
            _onRaised += handler;
        }

        // 구독 해제를 짝으로 강제한다 — SO는 씬보다 오래 살아서,
        // 해제를 잊으면 파괴된 오브젝트로의 참조가 채널에 남는다.
        public void Unsubscribe(Action<T> handler)
        {
            _onRaised -= handler;
        }
    }
}
