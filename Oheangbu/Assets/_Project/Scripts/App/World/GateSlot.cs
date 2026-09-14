using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    // [SPEC-WORLD-MAP §4 층 5] 게이트 슬롯 — Prefabs/Gate/Gate_{Kind}_<Skin>의 데이터 컴포넌트(기능 0 — Spellcraft/Combat 후속).
    // 런타임 열림 = 프리팹 상태 스왑(A3): 자식 'Closed'/'Open'을 켜고 끈다 — Terrain 편집 0·콜라이더 재빌드 0·NavMesh 갱신 0.
    // 값은 시트 gates[] 행에서 빌더가 복사(stepHeight 5 = 시트 기본, A14) — 여기 기본값은 공란/0.
    // 감사: A1(본편 경로 위 mode==MainPath 0 · 예고 슬롯 존재) · C6(일방 절벽 상단 섬).
    [DisallowMultipleComponent]
    public sealed class GateSlot : MonoBehaviour
    {
        public const string ClosedChildName = "Closed";
        public const string OpenChildName = "Open";

        [Tooltip("게이트 ID(시트 gates[].id 미러)")]
        public string id = "";
        [Tooltip("게이트 5종")]
        public GateKind kind = GateKind.Guk;
        [Tooltip("예고 / 재방문 / 본편 경로 위(청림 = 0개)")]
        public GateMode mode = GateMode.Preview;
        [Tooltip("국 1회 수직 단차 m(시트 미러 — 배치 규격, 코드 0)")]
        public float stepHeight;
        [Tooltip("보상 슬롯 ID")]
        public string rewardSlot = "";
        [Tooltip("강토 스킨 이름(프리팹 접미 — Cliff/Vine/Rock)")]
        public string skin = "";

        private bool _isOpen;

        public bool IsOpen => _isOpen;

        // 상태 스왑 — 자식이 없으면 조용히 지나간다(매싱 대체 §10-8). 지형·콜라이더·NavMesh 무접촉.
        public void SetOpen(bool open)
        {
            _isOpen = open;
            var closed = transform.Find(ClosedChildName);
            var opened = transform.Find(OpenChildName);
            if (closed != null) closed.gameObject.SetActive(!open);
            if (opened != null) opened.gameObject.SetActive(open);
        }
    }
}
