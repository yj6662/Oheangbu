using UnityEngine;

namespace Oheangbu.Combat
{
    // 광역 실판정 기하 [SPELL-AREA-SHAPES §3] — 순수 함수·수평 판정(높이 차는 묻지 않는다 [TEST]).
    // 배선(판정)과 하네스(기대 집합)가 같은 함수를 쓴다 — 기하가 두 군데서 어긋날 수 없게.
    public static class AreaGeometry
    {
        public static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        // 전방 부채꼴 — origin에서 forward(수평) 기준 반각 halfAngle·사거리 range
        public static bool InCone(Vector3 origin, Vector3 forward, Vector3 point, float halfAngle, float range,
            out float angle, out float distance)
        {
            Vector3 to = Flat(point - origin);
            Vector3 fwd = Flat(forward);
            distance = to.magnitude;
            angle = fwd.sqrMagnitude > 0.0001f && distance > 0.0001f ? Vector3.Angle(fwd, to) : 0f;
            return distance <= range && angle <= halfAngle;
        }

        // 원형 — center 기준 반경 radius
        public static bool InCircle(Vector3 center, Vector3 point, float radius, out float distance)
        {
            distance = Flat(point - center).magnitude;
            return distance <= radius;
        }

        // 복도 — start에서 direction(수평 단위)으로 길이 length, 반폭 halfWidth. along=경로 위 거리(전선 도달 시각의 근거)
        public static bool InCorridor(Vector3 start, Vector3 direction, Vector3 point, float halfWidth, float length,
            out float along, out float lateral)
        {
            Vector3 dir = Flat(direction);
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
            Vector3 rel = Flat(point - start);
            along = Vector3.Dot(rel, dir);
            lateral = (rel - dir * along).magnitude;
            return along >= 0f && along <= length && lateral <= halfWidth;
        }
    }
}
