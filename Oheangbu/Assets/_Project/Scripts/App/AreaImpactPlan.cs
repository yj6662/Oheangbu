using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // 판정이 확정한 착탄 1건 — 대상·시각·위력. 배선의 PendingCast와 같은 값(시각은 하나뿐)
    public sealed class PlannedHit
    {
        public EnemyVitals Target;
        public float ImpactTime;
        public float LaunchTime;
        public Vector3 ImpactPoint;
        public bool HasImpactPoint;
        public float Power;
    }

    // 광역 판정 계획 [SPELL-AREA-SHAPES §3] — 배선이 만들고 연출(SpellSequenceEffect.SetAreaPlan)이 받는다.
    // 중심·방향·운동 수치는 연출의 정본(프리팹 직렬 값은 폴백), Shots는 발 단위 대상·시각(Volley)
    public sealed class PlannedSpike { public Vector3 Point; public float RiseAt; }

    public sealed class AreaImpactPlan
    {
        public AreaShape Shape;
        public Vector3 Point;      // Circle 중심 / Path 시작점 / Volley 허공 대체점
        public Vector3 Direction;  // Path 전진 방향(수평 단위) / Cone 전방
        public float Angle;        // Cone 반각(°) — CombatConfig 정본의 전달 사본(연출이 판정 부채꼴과 같은 폭을 그리는 통로, 읽기 전용 — 판정은 이 값을 읽지 않는다)
        public float Radius;       // Circle 반경 / Path 반폭
        public float Length;
        public float Speed;
        public float Delay;
        public float CreatedAt = Time.time;
        public int ShotCount;
        public int VisualSeed; // One seed per cast; presentation never changes damage or Unity random state.
        public float ShotInterval;
        public readonly List<PlannedSpike> Spikes = new List<PlannedSpike>();
        public readonly List<PlannedHit> Shots = new List<PlannedHit>();
    }

    // 시전 1건의 판정 계획 전체 — 하네스 계측용 재방송(CombatLoopWiring.CastPlanned). 규칙에는 불개입
    public sealed class CastPlan
    {
        public SpellCast Cast;
        public AreaImpactPlan Area; // 단일 판정이면 null
        public readonly List<PlannedHit> Hits = new List<PlannedHit>();
    }
}
