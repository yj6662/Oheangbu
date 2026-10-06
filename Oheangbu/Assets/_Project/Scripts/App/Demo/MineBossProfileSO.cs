using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    // #306 #11 폐광 튜토리얼 보스(인간형) 데이터 [TEST, SPEC-PLAYTEST-306]. 기술 하나 = 저작 공격 프로필 하나(EnemyAttackProfileSO).
    // 판정·피해·예고·패링은 EnemyController가 그대로 갖고, 이 프로필은 순서·간격·기다림만 정한다.
    [CreateAssetMenu(menuName = "Oheangbu/Demo/Mine Boss Profile")]
    public sealed class MineBossProfileSO : ScriptableObject
    {
        [Header("기술 — M1 어깨 들이받기(무) · M2 휘두르기(무) · M3 마석 파편 던지기(목) · M4 결정 솟구침(목)")]
        public EnemyAttackProfileSO Charge;
        public EnemyAttackProfileSO Sweep;
        public EnemyAttackProfileSO Shard;
        public EnemyAttackProfileSO CrystalRing;
        [Header("간격 [TEST]")]
        [Min(0f)] public float Cooldown = 1.1f;
        [Min(0f)] public float ComboGap = .15f;
        [Tooltip("첫 그로기 뒤 M5(M1→M3) 혼합 콤보를 고를 확률")]
        [Range(0f, 1f)] public float ComboChance = .3f;
        [Header("자리 — 기술별 선호 거리(m)")]
        [Min(.5f)] public float MeleeDistance = 2.4f;
        [Min(.5f)] public float ShardDistance = 7f;
        [Min(.5f)] public float RingDistance = 5f;
        [Header("파편 기다림 — 플레이어가 작도·회피할 때까지 이 거리 앞에서 멈춘다")]
        [Min(0f)] public float ShardHoldDistance = 3f;
        [Min(0f)] public float ShardHoldMaxSeconds = 6f;

        public bool TryValidate(out string error)
        {
            foreach (var p in new[] { Charge, Sweep, Shard, CrystalRing })
                if (p == null || !p.TryValidate(out _)) { error = "모든 기술에 유효한 공격 프로필이 필요하다."; return false; }
            if (Charge.Elemental || Sweep.Elemental || !Shard.Elemental || !CrystalRing.Elemental ||
                Charge.Delivery != EnemyAttackDelivery.MeleeArc || Sweep.Delivery != EnemyAttackDelivery.MeleeArc ||
                (Shard.Delivery != EnemyAttackDelivery.HomingProjectile && Shard.Delivery != EnemyAttackDelivery.AimedProjectile) ||
                CrystalRing.Delivery != EnemyAttackDelivery.GroundEruption)
            { error = "M1·M2는 무속성 근접, M3는 속성 투사체, M4는 속성 지면 분출이어야 한다."; return false; }
            if (!float.IsFinite(Cooldown + ComboGap + ShardHoldDistance + ShardHoldMaxSeconds + MeleeDistance + ShardDistance + RingDistance))
            { error = "값은 유한해야 한다."; return false; }
            error = null; return true;
        }
    }
}
