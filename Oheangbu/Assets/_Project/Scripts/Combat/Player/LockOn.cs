using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.Combat
{
    // 락온 = 결투 계약(COMBAT-GROGGY) — 유도 조준·갈무리·그로기 표시의 전제.
    // 대상 선정 [TEST · DECISIONS #139 2026-09-03 — 소울류 관례 차용]: 사거리 안·화면 안의 생존 후보 중
    // 화면 중앙(카메라 전방)에 가장 가까운 적. 동률은 거리 가중으로 가까운 쪽. 후보 집합은 배선부가 넘긴다
    // (씬 배선 — 전역 탐색 없음). 후보가 없으면 계약은 서지 않는다(변화 없음). 잠긴 상태의 재입력=해제.
    public sealed class LockOn : MonoBehaviour
    {
        [SerializeField] private CombatConfigSO _config;

        private readonly List<EnemyVitals> _candidates = new List<EnemyVitals>();
        private EnemyVitals _target;
        private bool _locked;

        public event Action Changed;

        public EnemyVitals Target => _locked && _target != null && _target.IsAlive ? _target : null;
        public bool IsLocked => Target != null;

        public void SetCandidates(IEnumerable<EnemyVitals> candidates)
        {
            _candidates.Clear();
            foreach (var candidate in candidates)
            {
                if (candidate != null && !_candidates.Contains(candidate)) _candidates.Add(candidate);
            }
        }

        public void Toggle()
        {
            if (_locked)
            {
                _locked = false;
                _target = null;
                Changed?.Invoke();
                return;
            }
            var best = SelectTarget();
            if (best == null) return;
            _target = best;
            _locked = true;
            Changed?.Invoke();
        }

        // 조준 기준 = 카메라(보는 곳이 곧 겨눈 곳 — 자유 조준과 같은 기준)
        private EnemyVitals SelectTarget()
        {
            var cam = Camera.main;
            if (cam == null) return null;
            float range = _config != null ? _config.LockOnRange : 20f;
            float margin = _config != null ? _config.LockOnViewportMargin : 0.05f;
            float weight = _config != null ? _config.LockOnDistanceWeight : 0.5f;

            EnemyVitals best = null;
            float bestScore = float.MaxValue;
            foreach (var candidate in _candidates)
            {
                if (candidate == null || !candidate.IsAlive) continue;
                Vector3 to = candidate.transform.position - cam.transform.position;
                float distance = to.magnitude;
                if (distance > range) continue;
                Vector3 viewport = cam.WorldToViewportPoint(candidate.transform.position);
                if (viewport.z <= 0f) continue; // 등 뒤
                if (viewport.x < -margin || viewport.x > 1f + margin || viewport.y < -margin || viewport.y > 1f + margin) continue;
                float score = Vector3.Angle(cam.transform.forward, to) + distance * weight;
                if (score < bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            return best;
        }

        private void Update()
        {
            // 대상이 죽으면 계약도 끝난다 — 판 단위 소멸(그로기 리셋은 배선부가 이 변화를 구독)
            if (_locked && (_target == null || !_target.IsAlive))
            {
                _locked = false;
                _target = null;
                Changed?.Invoke();
            }
        }
    }
}
