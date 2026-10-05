using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    // #308 D308-8 (SPEC-VEHICLE-UX-308 §2) [TEST]: 보스 필드 = 마석 자동차 소환이 거절되는 보스 교전 구역 목록(데이터).
    // 한 에셋을 세 씬(#296 후보 · #298 후보 · W_Demo_Main)이 함께 쓴다. EncounterId가 있으면 중심은 런타임에 그 씬 콘텐츠의
    // Encounter.Feet에서 읽는다(콘텐츠가 보스를 옮겨도 따라간다). 없으면 Centre를 쓴다. 판정은 XZ 평면만 본다(높이 무시).
    // 목록은 Vehicle308 fields-derive가 씬의 보스 조우(EnemyVitals.IsBoss + 이름 목록)에서 만든다. 손으로 고쳐도 된다.
    // 정적 가변 상태 없음.
    [CreateAssetMenu(menuName = "Oheangbu/World/Vehicle Boss Fields 308 TEST", fileName = "VehicleBossFields308")]
    public sealed class VehicleBossFieldSO : ScriptableObject
    {
        [Serializable]
        public sealed class Field
        {
            [Tooltip("Field name (the boss encounter id when derived).")]
            public string Id = "";
            public bool Enabled = true;
            [Tooltip("Optional content Encounter id. When the open scene's content has it, its Feet is the centre at run time (else Centre).")]
            public string EncounterId = "";
            [Tooltip("Centre used when EncounterId is empty or missing from the scene's content (XZ only).")]
            public Vector3 Centre;
            [Tooltip("Flat radius in metres (derived: encounter Leash + margin).")]
            [Min(1f)] public float Radius = 40f;
            [Tooltip("Optional XZ polygon in world metres; three or more points replace the circle.")]
            public Vector2[] Polygon = Array.Empty<Vector2>();
            [Tooltip("TEST false (D308-8 literal): summoning stays refused after the boss is defeated. True opens the field once the encounter id is on the defeat record.")]
            public bool OpenAfterDefeat;
            [Tooltip("Provenance written by Vehicle308 fields-derive (scene, encounter, leash, margin). Read only by people.")]
            public string Source = "";
        }

        public Field[] Fields = Array.Empty<Field>();

        /// <summary>True when <paramref name="point"/> (plus <paramref name="extra"/> metres of footprint) lies in the field
        /// whose centre is <paramref name="centre"/>. XZ only.</summary>
        public static bool Contains(Field field, Vector3 centre, Vector3 point, float extra)
        {
            if (field == null) return false;
            extra = Mathf.Max(0f, extra);
            var poly = field.Polygon;
            if (poly != null && poly.Length >= 3)
            {
                var p = new Vector2(point.x, point.z);
                if (InsidePolygon(poly, p)) return true;
                if (extra <= 0f) return false;
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                    if (DistanceToSegment(p, poly[j], poly[i]) <= extra) return true;
                return false;
            }
            float dx = point.x - centre.x, dz = point.z - centre.z, r = Mathf.Max(0f, field.Radius) + extra;
            return dx * dx + dz * dz <= r * r;
        }

        static bool InsidePolygon(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a; float len = ab.sqrMagnitude;
            float t = len < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
