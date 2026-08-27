using System;
using UnityEngine;

namespace Oheangbu.Combat
{
    // 락온 = 결투 계약(COMBAT-GROGGY) — 유도 조준·갈무리·그로기 표시의 전제.
    // 프로토는 적 1체이므로 후보 탐색 없이 지정 대상 토글(다수전은 후속).
    public sealed class LockOn : MonoBehaviour
    {
        [SerializeField] private EnemyVitals _target; // 프로토 단일 적 — 씬 지정
        [SerializeField] private CombatConfigSO _config;

        private bool _locked;

        public event Action Changed;

        public EnemyVitals Target => _locked && _target != null && _target.IsAlive ? _target : null;
        public bool IsLocked => Target != null;

        public void Toggle()
        {
            _locked = !_locked && _target != null && _target.IsAlive;
            Changed?.Invoke();
        }

        private void Update()
        {
            // 대상이 죽으면 계약도 끝난다 — 판 단위 소멸(그로기 리셋은 배선부가 이 변화를 구독)
            if (_locked && (_target == null || !_target.IsAlive))
            {
                _locked = false;
                Changed?.Invoke();
            }
        }
    }
}
