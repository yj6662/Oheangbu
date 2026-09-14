using System;
using UnityEngine;

namespace Oheangbu.App.World
{
    // [SPEC-WORLD-MAP §4 층 11·§7 ⑫] 빌드 도장 — 구역 씬 루트에 빌더가 기록하는 결정론 증빙(기능 0).
    // inputHash = 시트·스케일·합성 SO JSON + 빌더 버전 + checkReportHash · heightsHash = SetHeights 직후 float[,] SHA-256 · holesHash 동일.
    // cuts[] = §6 컷 V1~V3의 결정적 포즈 — 빌드 시 고정, 이후 이동 금지(측정 후 이동 금지 규율).
    // 감사 A11: 감사 시점 TerrainData.GetHeights 재해시 ≠ heightsHash → 「브러시/외부 편집 감지」 경고.
    [DisallowMultipleComponent]
    public sealed class WorldBuildStamp : MonoBehaviour
    {
        [Serializable]
        public sealed class CutPose
        {
            [Tooltip("컷 ID(V1·V2·V3)")]
            public string id = "";
            [Tooltip("눈 위치(월드 m — 눈높이 포함)")]
            public Vector3 position;
            [Tooltip("yaw °(북 0° 시계)")]
            public float yaw;
            [Tooltip("pitch °(위 +)")]
            public float pitch;
        }

        [Tooltip("입력 해시(SO JSON + 빌더 버전 + checkReportHash)")]
        public string inputHash = "";
        [Tooltip("SetHeights 직후 float[,] SHA-256")]
        public string heightsHash = "";
        [Tooltip("SetHoles 직후 bool[,] SHA-256")]
        public string holesHash = "";
        [Tooltip("빌더 버전 문자열")]
        public string builderVersion = "";
        [Tooltip("빌드 UTC 시각(ISO 8601)")]
        public string utc = "";
        [Tooltip("컷 포즈(빌드 시 고정 — 이동 금지)")]
        public CutPose[] cuts = new CutPose[0];
    }
}
