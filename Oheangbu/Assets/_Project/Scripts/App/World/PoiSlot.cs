using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    // [SPEC-WORLD-MAP §4 층 5·§5-1] POI 슬롯 — 빌더가 AreaSheetSO.pois[] 행을 씬에 복사하는 데이터 홀더(기능 0).
    // 감사가 읽는다: C7 갈래길 끝(차수 1 노드 ↔ 슬롯 반경) · A12 서사 침묵(narrativeSlot 공란 census) · 시야선 시점.
    // 값은 시트에서 복사(단일 출처) — 여기 기본값은 공란/0.
    [DisallowMultipleComponent]
    public sealed class PoiSlot : MonoBehaviour
    {
        [Tooltip("POI ID(시트 pois[].id 미러)")]
        public string id = "";
        [Tooltip("종류")]
        public PoiKind kind = PoiKind.Vista;
        [Tooltip("패드 반경 m(시트 미러 — 식생 제외·기즈모 디스크)")]
        public float padRadius;
        [Tooltip("서사 슬롯 — 기본 공란(LDB 서사 침묵 · §6 A12)")]
        public string narrativeSlot = "";

        private void OnDrawGizmos()
        {
            // 작은 디스크 + 정면(yaw) 선 — 수치는 padRadius(데이터) · 화이트리스트 {1, 2}만.
            float radius = padRadius > 0f ? padRadius : 1f;
            Gizmos.DrawRay(transform.position, transform.forward * 2f);
#if UNITY_EDITOR
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, radius);
#else
            Gizmos.DrawWireSphere(transform.position, radius);
#endif
        }
    }
}
