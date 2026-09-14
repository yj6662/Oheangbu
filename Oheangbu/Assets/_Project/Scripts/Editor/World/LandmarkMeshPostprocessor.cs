using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools
{
    // [SPEC-WORLD-MAP §4 층 4] Blender 랜드마크 임포트 컨폼(D8) — Assets/_Project/Art/Models/Landmarks/ 한정 AssetPostprocessor.
    // landmark_check.py가 PASS 판정한 FBX만 이 폴더에 온다(Invoke-LandmarkCheck.ps1). 여기서는 임포터 설정을 계약값으로 고정하고,
    // 재질은 이름(InkWorld_Rock/InkWorld_Wood)으로 기존 M_InkWorld_* 에셋을 돌려주며(재질 에셋 생성 0), 콜라이더 자식은 MeshCollider로 바꾼다:
    // UCX_* = 볼록 조각(convex) / <Name>_col = 단일 비볼록 메시(ColliderMode.Mesh, ≤ ColliderTriBudget tris — 초과 시 FAIL 로그).
    // 루트 scale (1,1,1)·rot 0 아니면 FAIL 로그(레거시 Meshy 100/270° 함정). 형제 <name>.footprint.json → Data/World/Landmarks/<name>.asset(LandmarkMeshSO) 갱신.
    // SO 갱신은 임포트 프레임 밖(delayCall)에서 한다 — 임포트 중 다른 에셋 생성·저장은 중첩 임포트 함정.
    public sealed class LandmarkMeshPostprocessor : AssetPostprocessor
    {
        public const string Folder = "Assets/_Project/Art/Models/Landmarks/";
        public const string LandmarkSoFolder = "Assets/_Project/Data/World/Landmarks";
        public const string RockMaterialPath = "Assets/_Project/Art/Materials/M_InkWorld_Rock.mat";
        public const string WoodMaterialPath = "Assets/_Project/Art/Materials/M_InkWorld_Wood.mat";
        public const string RockSlotName = "InkWorld_Rock";
        public const string WoodSlotName = "InkWorld_Wood";
        public const string ColliderSuffix = "_col";
        public const string ConvexPrefix = "UCX_";
        public const string FootprintSuffix = ".footprint.json";
        public const string ReportSuffix = ".report.json";
        // [SPEC-WORLD-MAP §4 층 4] 단일 <Name>_col 콜라이더 트라이앵글 상한(계약값 2000 — landmark_check.py COLLIDER_TRI_MAX와 동일).
        // LandmarkMeshSO에는 콜라이더 예산 필드가 없다(triBudget = 렌더 8000) → 이 상수가 유일한 계약값.
        public const int ColliderTriBudget = 2000;

        private static readonly Regex PairRegex = new Regex(@"\[\s*(-?[0-9.eE+-]+)\s*,\s*(-?[0-9.eE+-]+)\s*\]", RegexOptions.Compiled);

        private bool InScope => assetPath.StartsWith(Folder, StringComparison.Ordinal);

        private void OnPreprocessModel()
        {
            if (!InScope) return;
            var importer = (ModelImporter)assetImporter;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.isReadable = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.addCollider = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
        }

        private Material OnAssignMaterialModel(Material material, Renderer renderer)
        {
            if (!InScope) return null;
            // 콜라이더 오브젝트(_col·UCX_*)는 재질이 없다(Blender에서 무재질 → Unity 'No Name') — OnPostprocessModel이 렌더러를 제거하므로 계약 검사 대상 아님
            if (renderer != null && IsColliderName(renderer.gameObject.name)) return null;
            string path;
            switch (material.name)
            {
                case RockSlotName: path = RockMaterialPath; break;
                case WoodSlotName: path = WoodMaterialPath; break;
                default:
                    Debug.LogError("FAIL LandmarkMeshPostprocessor: 재질 슬롯 이름 '" + material.name + "'은 계약 밖(" + RockSlotName + "/" + WoodSlotName + ") — " + assetPath);
                    return null;
            }
            var shared = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (shared == null) Debug.LogError("FAIL LandmarkMeshPostprocessor: " + path + " 없음 — " + assetPath);
            return shared;
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!InScope) return;

            bool scaleOk = Approximately(root.transform.localScale, Vector3.one);
            bool rotOk = Quaternion.Angle(root.transform.localRotation, Quaternion.identity) <= 1e-4f;
            if (!scaleOk || !rotOk)
                Debug.LogError("FAIL LandmarkMeshPostprocessor: 루트 scale=" + root.transform.localScale + " rot=" + root.transform.localRotation.eulerAngles
                               + " (계약 = scale 1 · rot 0, FBX_SCALE_UNITS·bake_space_transform) — " + assetPath);

            int colliders = 0;
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child == root.transform) continue;
                string n = child.name;
                if (!IsColliderName(n)) continue;
                var filter = child.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                bool convex = n.StartsWith(ConvexPrefix, StringComparison.Ordinal);
                if (!convex)
                {
                    int tris = TriangleCount(filter.sharedMesh);
                    if (tris > ColliderTriBudget)
                        Debug.LogError("FAIL LandmarkMeshPostprocessor: " + n + " 비볼록 콜라이더 " + tris + " tris > 계약 " + ColliderTriBudget + " — " + assetPath);
                }
                var collider = child.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = convex;
                var renderer = child.GetComponent<MeshRenderer>();
                if (renderer != null) UnityEngine.Object.DestroyImmediate(renderer);
                UnityEngine.Object.DestroyImmediate(filter);
                colliders++;
            }

            string name = Path.GetFileNameWithoutExtension(assetPath);
            string footprintPath = Path.Combine(Path.GetDirectoryName(assetPath) ?? Folder, name + FootprintSuffix).Replace('\\', '/');
            string reportPath = Path.Combine(Path.GetDirectoryName(assetPath) ?? Folder, name + ReportSuffix).Replace('\\', '/');
            if (!File.Exists(footprintPath))
            {
                Debug.Log("LandmarkMeshPostprocessor: " + name + " 임포트(콜라이더 " + colliders + ") — footprint.json 없음, LandmarkMeshSO 갱신 생략");
                return;
            }

            Vector2[] polygon;
            float groundOffset;
            int contractVersion;
            if (!TryParseFootprint(File.ReadAllText(footprintPath), out polygon, out groundOffset, out contractVersion))
            {
                Debug.LogError("FAIL LandmarkMeshPostprocessor: footprint.json 파싱 실패 — " + footprintPath);
                return;
            }
            string reportHash = File.Exists(reportPath) ? WorldMapAudit.Sha256File(reportPath) : "";
            string modelPath = assetPath;
            string soPath = LandmarkSoFolder + "/" + name + ".asset";
            string blenderSource = "Art/Blender/" + name + ".blend";

            Debug.Log("LandmarkMeshPostprocessor: " + name + " 임포트(콜라이더 " + colliders + ", 발자국 " + polygon.Length + "점) → " + soPath + " 은 OnPostprocessAllAssets에서 갱신");
        }

        // 임포트 배치가 끝난 뒤(도메인 리로드 뒤에도 같은 배치로 호출된다) 랜드마크 SO를 만든다 — delayCall은 임포트 직후 리로드에 유실된다(2026-09-07 실측).
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (var imported in importedAssets)
            {
                if (!imported.StartsWith(Folder, StringComparison.Ordinal)) continue;
                if (!imported.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;
                string name = Path.GetFileNameWithoutExtension(imported);
                string footprintPath = Folder + name + FootprintSuffix;
                string reportPath = Folder + name + ReportSuffix;
                if (!File.Exists(footprintPath)) continue;
                Vector2[] polygon;
                float groundOffset;
                int contractVersion;
                if (!TryParseFootprint(File.ReadAllText(footprintPath), out polygon, out groundOffset, out contractVersion))
                {
                    Debug.LogError("FAIL LandmarkMeshPostprocessor: footprint.json 파싱 실패 — " + footprintPath);
                    continue;
                }
                string reportHash = File.Exists(reportPath) ? WorldMapAudit.Sha256File(reportPath) : "";
                ApplyToLandmarkSo(imported, LandmarkSoFolder + "/" + name + ".asset", "Art/Blender/" + name + ".blend", polygon, groundOffset, contractVersion, reportHash);
            }
        }

        private static bool IsColliderName(string n)
        {
            return n.EndsWith(ColliderSuffix, StringComparison.Ordinal) || n.StartsWith(ConvexPrefix, StringComparison.Ordinal);
        }

        // footprint.json = {polygon:[[x,y]..], polygonFlat:[x0,y0,..], groundOffset, contractVersion, tris, sha256}. polygon(스펙 형식)을 정규식으로 읽는다.
        public static bool TryParseFootprint(string json, out Vector2[] polygon, out float groundOffset, out int contractVersion)
        {
            polygon = null;
            groundOffset = 0f;
            contractVersion = 0;
            if (string.IsNullOrEmpty(json)) return false;

            var polyMatch = Regex.Match(json, "\"polygon\"\\s*:\\s*\\[(.*?)\\]\\s*,\\s*\"", RegexOptions.Singleline);
            if (!polyMatch.Success) polyMatch = Regex.Match(json, "\"polygon\"\\s*:\\s*\\[(.*)\\]", RegexOptions.Singleline);
            if (!polyMatch.Success) return false;
            var points = new List<Vector2>();
            foreach (Match m in PairRegex.Matches(polyMatch.Groups[1].Value))
            {
                float x, y;
                if (!float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out x)) return false;
                if (!float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out y)) return false;
                points.Add(new Vector2(x, y));
            }
            if (points.Count == 0) return false;
            polygon = points.ToArray();

            var offset = Regex.Match(json, "\"groundOffset\"\\s*:\\s*(-?[0-9.eE+-]+)");
            if (offset.Success) float.TryParse(offset.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out groundOffset);
            var version = Regex.Match(json, "\"contractVersion\"\\s*:\\s*(\\d+)");
            if (version.Success) int.TryParse(version.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out contractVersion);
            return true;
        }

        private static void ApplyToLandmarkSo(string modelPath, string soPath, string blenderSource, Vector2[] polygon, float groundOffset, int contractVersion, string reportHash)
        {
            string guid = AssetDatabase.AssetPathToGUID(modelPath);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogError("FAIL LandmarkMeshPostprocessor: GUID 조회 실패 — " + modelPath);
                return;
            }
            var so = AssetDatabase.LoadAssetAtPath<LandmarkMeshSO>(soPath);
            bool created = false;
            if (so == null)
            {
                DevSceneKit.EnsureFolder(LandmarkSoFolder);
                so = ScriptableObject.CreateInstance<LandmarkMeshSO>();
                AssetDatabase.CreateAsset(so, soPath);
                created = true;
            }

            var serialized = new SerializedObject(so);
            serialized.FindProperty("meshGuid").stringValue = guid;
            var footprint = serialized.FindProperty("footprint");
            footprint.arraySize = polygon.Length;
            for (int i = 0; i < polygon.Length; i++) footprint.GetArrayElementAtIndex(i).vector2Value = polygon[i];
            serialized.FindProperty("groundOffset").floatValue = groundOffset;
            serialized.FindProperty("contractVersion").intValue = contractVersion;
            serialized.FindProperty("checkReportHash").stringValue = reportHash;
            if (created) serialized.FindProperty("blenderSource").stringValue = blenderSource;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(so);
            AssetDatabase.SaveAssets();
            Debug.Log("LandmarkMeshPostprocessor: " + soPath + (created ? " 생성" : " 갱신") + " meshGuid=" + guid + " footprint=" + polygon.Length + " reportHash=" + reportHash);
        }

        // 서브메시 디스크립터(indexCount)로 센다 — isReadable=false 임포트에서도 메타데이터는 읽힌다. 삼각형 토폴로지만 집계.
        private static int TriangleCount(Mesh mesh)
        {
            int total = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                var sub = mesh.GetSubMesh(i);
                if (sub.topology == MeshTopology.Triangles) total += sub.indexCount / 3;
            }
            return total;
        }

        private static bool Approximately(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.x - b.x) <= 1e-4f && Mathf.Abs(a.y - b.y) <= 1e-4f && Mathf.Abs(a.z - b.z) <= 1e-4f;
        }
    }
}
