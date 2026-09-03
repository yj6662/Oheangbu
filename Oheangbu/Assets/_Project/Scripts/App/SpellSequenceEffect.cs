using UnityEngine;

namespace Oheangbu.App
{
    // [SPEC-SPELL-FX-ASSETS P4] 어휘별 자체 시계 연출의 공통 계약 — 소(SpikeVolleyEffect)의 Begin
    // 시그니처를 일반화한다. 결합 프리팹의 자식에 실리고, 어댑터가 커밋 프레임에 Begin으로 떼어내
    // 문양(부모) 수명과 분리해 굴린다. 피해는 배선의 단일 착탄 시계 그대로 — 연출≠실판정(§9-1).
    public abstract class SpellSequenceEffect : MonoBehaviour
    {
        // origin=커밋 문양 중심 / target=락온 대상(없으면 null) / fallbackPoint=허공 착탄점 / tint=팔레트 속성색
        public abstract void Begin(Vector3 origin, Transform target, Vector3 fallbackPoint, Color tint);

        // [SPELL-FIDELITY §4.4] 배선의 판정 착탄 시계(비행시간) 수신 훅 — Begin 직전에 호출된다.
        // 단일 유도 연출(가 랜스)이 「피해=착탄 동기화」에 참여하는 통로. 광역 자체 시계는 무시(기본 no-op)
        public virtual void SetImpactClock(float flightDuration) { }
    }
}
