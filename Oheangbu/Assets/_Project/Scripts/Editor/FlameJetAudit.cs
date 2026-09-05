using System.Text;
using Oheangbu.App;
using Oheangbu.Combat;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools
{
    // [SPEC-SPELL-FX-REWORK §3.1.2 — 노 검수 도구] 원격 정량 증명(script-execute 불능 수칙 → reflection-method-call 또는 메뉴).
    //   Probe(): 플레이 중 씬의 FlameJetEffect마다 「뒤 입자 0 · 최대 방위각 ≤ 반각 · 최대 거리 ≤ 사거리+여유 · 활성 tris · 활성 Light」
    //            — 각도·거리는 판정과 같은 함수(AreaGeometry.InCone)로 잰다. 불혀 출발점이 노즐 원판(반지름 r)이라 원점 기준
    //            위치각은 방출 방위각보다 최대 asin(r/d)만큼 크다(sin(초과)=r_x·cos(az)/d ≤ r/d) — 그만큼 빼서 방위각을 추정한다.
    //            밑불(노즐 링, 판 평면)은 각도 통계에서 제외.
    //   MeshGate(assetPath): FBX 반입 직후 실측 게이트 — bounds·z 범위·상하단 10% 정점 수(첨단 +Z)·tris·서브메시(메시 통짜 직렬화 회피).
    // 판정·인식 무접촉 — 읽기만 한다. 반환은 한 줄 문자열(+Debug.Log).
    public static class FlameJetAudit
    {
        private const float BehindTolerance = 0.05f;  // 「뒤」 판정 여유(m) — 노즐 원판·수치 오차
        private const float ReachTolerance = 0.5f;    // 최대 거리 허용 여유(m) — 불혀 길이·드롭
        private const float DistEpsilon = 1e-3f;      // 노즐 시차 보정의 거리 하한(m) — 0 나눗셈 방지
        private const float SlabFraction = 0.10f;     // 상·하단 z 슬랩 비율(첨단 판정: 상단 정점 수 < 하단 정점 수)
        private const float WidthMin = 0.30f;         // 정규화 폭 기대 범위(높이 1.0 대비)
        private const float WidthMax = 0.45f;
        private const float FatWidth = 0.50f;         // 「뚱뚱」 — 2차 preview 사유
        private const float HeightTolerance = 0.05f;  // 높이 1.0 허용 ±
        private const float PivotTolerance = 0.05f;   // 바닥 피벗 허용(높이 대비)
        private const int MaxTris = 500;              // 불혀 폴리 상한(Meshy 목표 400·≤450 권장)

        // [§6-8 보조] 프레임의 드로우콜·배치·삼각형을 읽는다(UnityStats — 에디터 전용, 게임 뷰 기준).
        //   고 마디는 마디마다 MaterialPropertyBlock을 쓰는데 **MPB는 SRP Batcher 호환을 깬다** —
        //   즉 폴리보다 **드로우콜**이 비용의 주역일 수 있다. SoloExtra와 짝지어 A·B로 재면 한계 드로우콜이 나온다.
        public static string RenderStats()
        {
            return Log($"[RenderStats] drawCalls={UnityStats.drawCalls} batches={UnityStats.batches} "
                     + $"dynamicBatched={UnityStats.dynamicBatchedDrawCalls} staticBatched={UnityStats.staticBatchedDrawCalls} "
                     + $"instanced={UnityStats.instancedBatchedDrawCalls} tris={UnityStats.triangles} setPass={UnityStats.setPassCalls}");
        }

        // [§6-8 프레임 격리 실측용] 감사 씬 `AuditSpawner._extraPrefabs`를 이름에 `contains`가 든 항목만 남긴다.
        //   빈 문자열이면 전부 비운다(=글자 0 기준선). 씬을 **저장하지 않는다** — 되돌리려면 씬을 다시 연다.
        //   목적: 「고만 있는 프레임」 − 「글자 0인 프레임」 = 고의 한계 비용. 전체 씬 ms는 20개 문양판에 묻혀 비교 불가다.
        public static string SoloExtra(string contains)
        {
            var sp = Object.FindFirstObjectByType<Oheangbu.App.PatternAuditSpawner>(FindObjectsInactive.Include);
            if (sp == null) return Log("SoloExtra: PatternAuditSpawner 없음(감사 씬을 열 것)");
            var so = new SerializedObject(sp);
            var arr = so.FindProperty("_extraPrefabs");
            var tint = so.FindProperty("_extraTints");
            var keptNames = new StringBuilder();
            int write = 0;
            for (int i = 0; i < arr.arraySize; i++)
            {
                var go = arr.GetArrayElementAtIndex(i).objectReferenceValue;
                if (go == null) continue;
                if (string.IsNullOrEmpty(contains) || go.name.IndexOf(contains, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (keptNames.Length > 0) keptNames.Append(", ");
                keptNames.Append(go.name);
                var c = i < tint.arraySize ? tint.GetArrayElementAtIndex(i).colorValue : default;
                arr.GetArrayElementAtIndex(write).objectReferenceValue = go;
                if (write < tint.arraySize) tint.GetArrayElementAtIndex(write).colorValue = c;
                write++;
            }
            arr.arraySize = write;
            if (tint.arraySize > write) tint.arraySize = write;
            so.ApplyModifiedPropertiesWithoutUndo();
            return Log($"SoloExtra(\"{contains}\"): _extraPrefabs {write}개 남김 [{keptNames}] — 씬 미저장(되돌리기=씬 재오픈)");
        }

        // [§3.4.4 — 고 마디 체인 검수] 플레이 중 호출. 생성된 본·마디·실 tris·바운즈를 잰다.
        //   본 수는 _count가 아니라 **실제 생성분**이다 — _maxSegments가 먼저 소진되면 뒤쪽 본은 아예 만들어지지 않는다.
        //   tris는 활성 MeshFilter만 합산(잎 포함) — 폴리 상한(글자별 12k) 판정의 유일 근거.
        public static string ThornProbe()
        {
            var fx = Object.FindObjectsByType<ThornRiseEffect>(FindObjectsSortMode.None);
            if (fx.Length == 0) return Log("FlameJetAudit.ThornProbe: 씬에 ThornRiseEffect 인스턴스 없음(플레이 중 호출)");
            var sb = new StringBuilder();
            for (int i = 0; i < fx.Length; i++)
            {
                if (i > 0) sb.AppendLine();
                int segs = 0, leaves = 0, tris = 0;
                bool any = false;
                Bounds b = default;
                // 연출 반경 = 판정 중심에서 **수평** 최대 도달. 바운즈 대각(size/2)은 중심이 어긋나면 반경이 아니다
                Vector3 c0 = fx[i].AuditCenter;
                float reachXZ = 0f;
                var mfs = fx[i].GetComponentsInChildren<MeshFilter>(false);
                for (int k = 0; k < mfs.Length; k++)
                {
                    var m = mfs[k].sharedMesh;
                    if (m == null) continue;
                    tris += m.triangles.Length / 3;
                    if (mfs[k].gameObject.name == "Leaves") leaves++; else segs++;
                    var r = mfs[k].GetComponent<MeshRenderer>();
                    if (r == null) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                    // 렌더러 바운즈 네 수평 모서리 중 가장 먼 것 — 셰이더 오프셋은 바운즈에 안 들어가므로 하한이다
                    Vector3 mn = r.bounds.min, mx = r.bounds.max;
                    for (int cx = 0; cx < 2; cx++)
                    for (int cz = 0; cz < 2; cz++)
                    {
                        float dx = (cx == 0 ? mn.x : mx.x) - c0.x;
                        float dz = (cz == 0 ? mn.z : mx.z) - c0.z;
                        float d = Mathf.Sqrt(dx * dx + dz * dz);
                        if (d > reachXZ) reachXZ = d;
                    }
                }
                int runs = 0;
                var tr = fx[i].transform;
                for (int k = 0; k < tr.childCount; k++)
                {
                    if (tr.GetChild(k).name == "Seg0") runs++;
                }
                sb.Append($"[ThornProbe] {fx[i].name}: runs={runs} segs={segs} leaves={leaves} tris={tris} "
                        + $"reachXZ={reachXZ:F2} center({F(c0)}) bounds c({F(b.center)}) s({F(b.size)})");
                // 같은 프레임의 렌더 통계 — 따로 부르면 이펙트 주기 재생 탓에 짝이 어긋난다
                sb.Append($" | frame drawCalls={UnityStats.drawCalls} setPass={UnityStats.setPassCalls} "
                        + $"batches={UnityStats.batches} sceneTris={UnityStats.triangles} ms={(Time.smoothDeltaTime * 1000f):F2}");
            }
            return Log(sb.ToString());
        }

        // 플레이 중 호출(점화 후 0.3s·0.6s·1.0s 권장) — 인스턴스별 한 줄
        public static string Probe()
        {
            var jets = Object.FindObjectsByType<FlameJetEffect>(FindObjectsSortMode.None);
            if (jets.Length == 0) return Log("FlameJetAudit.Probe: 씬에 FlameJetEffect 인스턴스 없음(플레이 중·점화 후 호출)");
            var sb = new StringBuilder();
            for (int j = 0; j < jets.Length; j++)
            {
                if (j > 0) sb.Append('\n');
                sb.Append(Describe(jets[j]));
            }
            return Log(sb.ToString());
        }

        private static string Describe(FlameJetEffect jet)
        {
            Vector3 origin = jet.Origin;
            Vector3 axis = jet.Axis;

            // 본류 = 파티클(FX-REWORK §3.1.2 — 불혀 메시 풀 폐기). World 시뮬레이션이라 particle.position이 곧 월드 좌표다
            int jetCount = 0, behind = 0;
            float maxAngle = 0f, maxDist = 0f, minAlong = 0f;
            var ps = jet.JetSystem;
            if (ps != null)
            {
                var buffer = new ParticleSystem.Particle[ps.particleCount];
                jetCount = ps.GetParticles(buffer);
                for (int i = 0; i < jetCount; i++)
                {
                    Vector3 p = buffer[i].position;
                    float along = Vector3.Dot(p - origin, axis);
                    if (along < -BehindTolerance) behind++;
                    if (along < minAlong) minAlong = along;
                    AreaGeometry.InCone(origin, axis, p, 180f, float.MaxValue, out float angle, out float dist);
                    // 노즐 시차 보정: 출발점이 노즐 원판(반지름 r)이라 원점 기준 위치각은 방출각보다 최대 asin(r/d) 크다
                    float nozzleTol = Mathf.Asin(Mathf.Clamp01(jet.NozzleRadius / Mathf.Max(dist, DistEpsilon))) * Mathf.Rad2Deg;
                    float azEst = angle - nozzleTol;
                    if (azEst > maxAngle) maxAngle = azEst;
                    if (dist > maxDist) maxDist = dist;
                }
            }

            // 밑불(문양 테두리 링·불혀 메시)은 각도 통계에서 제외 — 개수·폴리만 센다
            int embers = 0, tris = 0;
            foreach (var r in jet.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!r.enabled) continue;
                if (!r.name.StartsWith(FlameJetEffect.EmberName, System.StringComparison.Ordinal)) continue;
                embers++;
                var filter = r.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) tris += TriangleCount(filter.sharedMesh);
            }

            int lights = 0;
            foreach (var l in jet.GetComponentsInChildren<Light>(false))
            {
                if (l.enabled) lights++;
            }

            bool pass = behind == 0 && maxAngle <= jet.HalfAngleUsed && maxDist <= jet.ReachUsed + ReachTolerance;
            return $"[FlameJetAudit] {jet.name} t={jet.Elapsed:F2}"
                 + $" plan(반각 {jet.HalfAngleUsed:F1}° 사거리 {jet.ReachUsed:F1} 판정 {jet.DelayUsed:F2} 점화 {jet.IgniteAt:F2})"
                 + $" | 본류 {jetCount} 밑불 {embers} | 뒤 {behind} minAlong {minAlong:F2}"
                 + $" | 최대 방위각(노즐 보정) {maxAngle:F1}° 최대거리 {maxDist:F2} | 밑불 tris {tris} | Light {lights}"
                 + $" | {(pass ? "PASS" : "FAIL")}(뒤 0 · 각 ≤반각 · 거리 ≤사거리+{ReachTolerance})";
        }

        // 반입 검수 — assetPath 예: "Assets/_Project/Art/Generated/No/FlameTongue_No.fbx" (첫 Mesh 서브에셋)
        public static string MeshGate(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return Log("FlameJetAudit.MeshGate: 경로 없음");
            Mesh mesh = null;
            var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    if (asset is Mesh m) { mesh = m; break; }
                }
            }
            if (mesh == null) return Log($"FlameJetAudit.MeshGate: Mesh 없음 — {assetPath}");

            Bounds b = mesh.bounds;
            Vector3 size = b.size;
            Vector3[] verts = mesh.vertices;
            int n = verts.Length;
            int tris = TriangleCount(mesh);
            float h = Mathf.Max(size.z, 1e-4f);
            float topZ = b.max.z - h * SlabFraction;
            float botZ = b.min.z + h * SlabFraction;
            int top = 0, bottom = 0;
            for (int i = 0; i < n; i++)
            {
                float z = verts[i].z;
                if (z >= topZ) top++;
                if (z <= botZ) bottom++;
            }

            bool widthOk = size.x >= WidthMin && size.x <= WidthMax && size.y >= WidthMin && size.y <= WidthMax;
            bool fat = size.x > FatWidth || size.y > FatWidth;
            bool heightOk = Mathf.Abs(size.z - 1f) <= HeightTolerance;
            bool pivotOk = Mathf.Abs(b.min.z) <= PivotTolerance * h;
            bool tipUp = top < bottom;
            bool trisOk = tris <= MaxTris;
            bool subOk = mesh.subMeshCount == 1;
            bool pass = widthOk && heightOk && pivotOk && tipUp && trisOk && subOk;

            string widthVerdict = widthOk ? "폭 OK" : fat ? "뚱뚱(>0.5 — 2차 preview 사유)" : "폭 범위 밖(0.30~0.45)";
            return Log($"[FlameJetAudit.MeshGate] {assetPath}:{mesh.name}"
                 + $" | bounds min({F(b.min)}) max({F(b.max)}) size({F(size)})"
                 + $" | verts {n} tris {tris} sub {mesh.subMeshCount} | 상단10% {top} 하단10% {bottom}"
                 + $" | {widthVerdict} · {(heightOk ? "높이 1.0 OK" : "높이≠1.0(정규화)")}"
                 + $" · {(pivotOk ? "바닥 피벗 OK" : "피벗≠바닥(z 재계산)")} · {(tipUp ? "첨단 +Z OK" : "첨단 −Z(180°X 재익스포트)")}"
                 + $" · {(trisOk ? "tris OK" : "tris>500(Decimate)")} · {(subOk ? "서브메시 1 OK" : "서브메시≠1")}"
                 + $" | {(pass ? "PASS" : "FAIL")}");
        }

        [MenuItem("Oheangbu/Dev/FlameJet 프로브 (플레이 중)")]
        private static void ProbeMenu()
        {
            Probe();
        }

        [MenuItem("Oheangbu/Dev/불혀 메시 반입 게이트 (선택 에셋)")]
        private static void MeshGateSelection()
        {
            var objects = Selection.objects;
            if (objects == null || objects.Length == 0)
            {
                Debug.Log("FlameJetAudit: 프로젝트 창에서 FBX/메시 에셋을 선택하세요.");
                return;
            }
            foreach (var obj in objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path)) MeshGate(path);
            }
        }

        private static int TriangleCount(Mesh mesh)
        {
            int count = 0;
            for (int s = 0; s < mesh.subMeshCount; s++) count += (int)mesh.GetIndexCount(s) / 3;
            return count;
        }

        private static string Log(string message)
        {
            Debug.Log(message);
            return message;
        }

        private static string F(Vector3 v) => $"{v.x:F3},{v.y:F3},{v.z:F3}";
    }
}
