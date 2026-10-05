using UnityEngine;

namespace Oheangbu.Combat
{
    // 적별 체력 프로필 [TEST — D306 #8, SPEC-PLAYTEST-306 AC-8a]. 비우면 EnemyVitals는 CombatConfigSO.EnemyMaxHp를 쓴다.
    // 단계(에셋은 Data/Configs/EnemyTiers): 동굴 24 · 길 36 · 들판·설화 60 · 보스=설정 사본 그대로(MaxHp 0) + 이름·IsBoss.
    // 표시 계약만 더한다 — 속성색 없음(ART-UI), 그로기 표시는 락온 원상 단독(COMBAT-GROGGY).
    [CreateAssetMenu(menuName = "Oheangbu/Combat/Enemy Vitals Profile", fileName = "EnemyVitals")]
    public sealed class EnemyVitalsProfileSO : ScriptableObject
    {
        [Tooltip("최대 체력. 0=CombatConfigSO.EnemyMaxHp(보스 설정 사본 480/400/520/180을 그대로 쓴다)")]
        [SerializeField, Min(0f)] private float _maxHp = 60f;
        [Tooltip("보스 바 이름(하단). 일반 적은 비운다")]
        [SerializeField] private string _displayName = "";
        [Tooltip("교전 중 하단 보스 바 — 락온 체력 획 대신")]
        [SerializeField] private bool _isBoss;
        [Tooltip("락온 원상 아래 체력 획(TargetHpStroke)")]
        [SerializeField] private bool _showLockOnBar = true;
        // #308 WP-07 (D308-13 Q4) [TEST]: a new stat. 0 = no armour, which is every enemy that exists today.
        [Tooltip("방어력 [TEST D308-13]. 받는 피해에서 덜어내는 비율 0~1 (0.3 = 30% 덜 받는다). 0 = 방어 없음(기존 적 전부). 방어 감소·방어 무시 술식이 이 값에 작용한다")]
        [SerializeField, Range(0f, 1f)] private float _defence;

        public float Defence => _defence;
        public float MaxHp => _maxHp;
        public string DisplayName => _displayName ?? "";
        public bool IsBoss => _isBoss;
        public bool ShowLockOnBar => _showLockOnBar;
    }
}
