using UnityEngine;

namespace Oheangbu.Data.World
{
    // [SPEC-WORLD-MAP §4 층 2·층 4] Blender 랜드마크 메시 계약(D8) — Data/World/Landmarks/LM_<Name>.asset.
    // 메시는 GUID 문자열로 참조(FX-ASSETS §4.7 관행 — 에셋 교체·유료 팩 미커밋에 안전). 발자국·해시는
    // landmark_check.py 산출(footprint.json)을 LandmarkMeshPostprocessor(EditorTools)가 기입한다 — 사람이 손으로 쓰지 않는다.
    [CreateAssetMenu(menuName = "Oheangbu/World/Landmark Mesh", fileName = "LM_")]
    public sealed class LandmarkMeshSO : ScriptableObject
    {
        [Header("메시 참조")]
        [Tooltip("임포트된 FBX 모델 에셋 GUID(문자열 참조 — 직접 링크 0)")]
        [SerializeField] private string meshGuid = "";
        [Tooltip("발자국 XZ 폐다각형(m · 피벗 기준) — footprint.json 임포트")]
        [SerializeField] private Vector2[] footprint = new Vector2[0];
        [Tooltip("접지 오프셋 m(발자국면 = 피벗 y + 이 값)")]
        [SerializeField] private float groundOffset;

        [Header("Terrain 컨폼 (층 4)")]
        [Tooltip("Raise/Carve = 발자국 안 지면 = 접지면 − 0.05 · Hole = 발자국 안 홀 + 밖 1 m 링 · None")]
        [SerializeField] private ConformMode conformMode = ConformMode.Raise;
        [Tooltip("발자국 밖 smoothstep 블렌드 밴드 m(이음 실패 시 6 m 1회 후퇴)")]
        [SerializeField] private float blendBandM = 4f;
        [Tooltip("콜라이더 방식 — UCX_ 볼록 조각 / 단일 메시 ≤2000 tris / 없음")]
        [SerializeField] private ColliderMode collider = ColliderMode.UCX;

        [Header("계약 · 검사 기록")]
        [Tooltip("렌더 메시 트라이앵글 예산")]
        [SerializeField] private int triBudget = 8000;
        [Tooltip("원본 .blend 경로(리포 루트 기준 — Unity Assets 밖)")]
        [SerializeField] private string blenderSource = "Art/Blender/LM_.blend";
        [Tooltip("Blender 계약 버전(landmark_check.py와 일치해야 임포트 통과)")]
        [SerializeField] private int contractVersion = 1;
        [Tooltip("report.json sha256 — 빌더 입력 해시에 포함(§6 A11 결정론)")]
        [SerializeField] private string checkReportHash = "";

        public string MeshGuid => meshGuid;
        public Vector2[] Footprint => footprint;
        public float GroundOffset => groundOffset;
        public ConformMode ConformMode => conformMode;
        public float BlendBandM => blendBandM;
        public ColliderMode Collider => collider;
        public int TriBudget => triBudget;
        public string BlenderSource => blenderSource;
        public int ContractVersion => contractVersion;
        public string CheckReportHash => checkReportHash;

#if UNITY_EDITOR
        // 포스트프로세서 전용 기입 통로(에디터) — 런타임은 읽기 전용.
        public void EditorApplyCheckResult(string guid, Vector2[] polygon, float offset, int version, string reportHash)
        {
            meshGuid = guid;
            footprint = polygon ?? new Vector2[0];
            groundOffset = offset;
            contractVersion = version;
            checkReportHash = reportHash ?? "";
        }
#endif
    }
}
