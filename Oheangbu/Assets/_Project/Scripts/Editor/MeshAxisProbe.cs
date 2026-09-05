using System.Text;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools
{
    // [SPEC-SPELL-FX-REWORK §3.3·§3.4 — 반입 실측 게이트] 생성 메시(FBX)의 축·피벗·립 방향을 Unity 측에서 판정한다.
    // 「반입 직후 bounds 실측이 유일 판정」(FX-ASSETS §4.2-6)을 립 부호·높이 부호까지 확장 — 전진 비대칭 메시(오 로브)는
    // Blender 단계에서 립 부호를 확정할 수 없다(180°X 단독 보상이 립을 −Y로 뒤집는다 — 반입 등가 180°Y 반전과 합성 시 (−x,−y,+z)).
    // 규약: 높이 +Z · 전진(립/첨단) +Y · 폭 +X · 바닥 피벗. 재익스포트 규칙: 립 반전=180°Z / 높이 반전=180°X + 피벗 재계산.
    // 호출: reflection-method-call(script-execute 불능 수칙) 또는 메뉴. 반환은 메시당 한 줄 문자열(메시 통짜 직렬화 회피).
    public static class MeshAxisProbe
    {
        private const float SliceFraction = 0.10f;   // 상·하단 정점 슬라이스 비율(무게중심 계산)
        private const float PivotTolerance = 0.05f;  // 바닥 피벗 허용(높이 대비) — min.z > −5%
        private const float FlipThreshold = 0.50f;   // 이 비율 이상 아래로 내려가면 높이 반전
        private const float CenterTolerance = 0.05f; // 수평 중심 피벗 허용(폭·깊이 대비)

        // assetPath 예: "Assets/_Project/Art/Generated/O/WaveLobe_O.fbx" — FBX 안의 모든 Mesh를 한 줄씩 보고
        public static string Report(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return "MeshAxisProbe: 경로 없음";
            var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (assets == null || assets.Length == 0) return $"MeshAxisProbe: 에셋 없음 — {assetPath}";

            var sb = new StringBuilder();
            int found = 0;
            foreach (var asset in assets)
            {
                if (!(asset is Mesh mesh)) continue;
                if (found++ > 0) sb.Append('\n');
                sb.Append(Describe(mesh, assetPath));
            }
            return found == 0 ? $"MeshAxisProbe: Mesh 없음 — {assetPath}" : sb.ToString();
        }

        private static string Describe(Mesh mesh, string assetPath)
        {
            Bounds b = mesh.bounds;
            Vector3 size = b.size;
            Vector3[] verts = mesh.vertices;
            int n = verts.Length;
            int tris = mesh.triangles.Length / 3;
            if (n == 0) return $"{assetPath}:{mesh.name} | 정점 0";

            // z(높이) 오름차순 정렬 → 하단 10% / 상단 10% 정점 무게중심
            var z = new float[n];
            var idx = new int[n];
            for (int i = 0; i < n; i++) { z[i] = verts[i].z; idx[i] = i; }
            System.Array.Sort(z, idx);
            int slice = Mathf.Max(1, Mathf.RoundToInt(n * SliceFraction));

            Vector3 bottom = Vector3.zero;
            Vector3 top = Vector3.zero;
            for (int i = 0; i < slice; i++)
            {
                bottom += verts[idx[i]];
                top += verts[idx[n - 1 - i]];
            }
            bottom /= slice;
            top /= slice;
            Vector3 diff = top - bottom;

            float h = Mathf.Max(size.z, 1e-4f);
            string heightVerdict = b.min.z > -PivotTolerance * h ? "높이 OK(바닥 피벗·+Z)"
                : b.min.z < -FlipThreshold * h ? "높이 반전(−Z — 180°X + 피벗 재계산)"
                : "피벗 중간(바닥 피벗 재계산)";
            string lipVerdict = diff.y > 0f ? "립 +Y OK" : "립 반전(−Y — 180°Z 재익스포트)";
            string widthVerdict = size.x >= size.y ? "폭축 X OK" : "폭축 Y(90°Z 회전 필요)";
            bool centered = Mathf.Abs(b.center.x) <= CenterTolerance * Mathf.Max(size.x, 1e-4f)
                         && Mathf.Abs(b.center.y) <= CenterTolerance * Mathf.Max(size.y, 1e-4f);
            string centerVerdict = centered ? "수평 중심 OK" : "수평 중심 어긋남(피벗 재계산)";

            return $"{assetPath}:{mesh.name}"
                 + $" | bounds min({F(b.min)}) max({F(b.max)}) size({F(size)})"
                 + $" | tris {tris} | verts {n}"
                 + $" | top10% c=({F(top)}) bottom10% c=({F(bottom)}) diff=({F(diff)})"
                 + $" | {heightVerdict} · {lipVerdict} · {widthVerdict} · {centerVerdict}";
        }

        private static string F(Vector3 v) => $"{v.x:F3},{v.y:F3},{v.z:F3}";

        [MenuItem("Oheangbu/Dev/메시 축 실측 (선택 에셋)")]
        private static void ReportSelection()
        {
            var objects = Selection.objects;
            if (objects == null || objects.Length == 0)
            {
                Debug.Log("MeshAxisProbe: 프로젝트 창에서 FBX/메시 에셋을 선택하세요.");
                return;
            }
            foreach (var obj in objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;
                Debug.Log(Report(path), obj);
            }
        }
    }
}
