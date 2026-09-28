using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.Data.World;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // 검문소 최소 보강 — 도로 위 데스크 하나뿐이던 검문 POI에 차단 울타리·등불·관 깃발·현판을 놓아
    // "실제 관영 검문 초소"로 읽히게 한다. 에셋 도감(Screenshots/AssetCatalog/index.html)에서 고른
    // HwaseongHaenggung 팩 중심(역참·객주 한옥과 룩 일관). 등불=메시 전용(발광 상한 안전). 개활·산 여백 유지.
    public static partial class WorldMacroActAuthoring
    {
        // 도감 확인 프리팹(유료 팩·gitignore, GUID 참조).
        const string P_Fence = "Assets/HwaseongHaenggung/Prefabs/SM_M_Treefence.prefab";       // 차단 울타리
        const string P_Lantern = "Assets/HwaseongHaenggung/Prefabs/SM_R_Lantern_1.prefab";      // 등불(메시 전용)
        const string P_Flag = "Assets/HwaseongForteressGate/Prefabs/SM_Flag_Wind_1.prefab";     // 관 깃발
        const string P_Sign = "Assets/HwaseongHaenggung/Prefabs/SM_R_Sign_1.prefab";            // 관 표식/방

        // 축소본 도로 위 검문 POI(데스크만 있어 바레함). 성저 본점(cargo_delivery)은 이미 관 건물이 있어 제외.
        static readonly string[] CheckpointIdsToDress = { "checkpoint_1", "checkpoint_2" };

        [Serializable] sealed class DressReport
        {
            public string status, scope = "검문 POI에 울타리·등불·깃발·현판 최소 배치. 도로·데스크·진행 불변. 실제 입력·미술 미검증.";
            public List<string> placed = new List<string>();
            public List<string> notes = new List<string>();
        }

        static string DressCheckpoints()
        {
            Require();
            var session = Session;
            var content = session.Content;
            var report = new DressReport();
            if (!AssetDatabase.GetAssetPath(content).StartsWith(WorldMacroCompactAuthoring.Folder + "/"))
                throw new InvalidOperationException("Content not isolated to compact folder");

            // 검문 드레싱은 별도 루트에 모아 grep·재실행 가능하게 한다(도로·데스크는 건드리지 않음).
            var root = GameObject.Find("Demo_CheckpointDressing")?.transform ?? new GameObject("Demo_CheckpointDressing").transform;

            foreach (var id in CheckpointIdsToDress)
            {
                var point = content.Points.FirstOrDefault(p => p != null && p.Id == id);
                if (point == null) { report.notes.Add(id + " point missing — skipped"); continue; }
                // 검문 POI가 협곡 립에 걸린 경우(checkpoint_1)가 있어, 실제 평평한 도로 위에 놓인 기존 데스크를
                // 앵커로 쓴다. 데스크가 없으면 Point로 폴백.
                var desk = GameObject.Find("Demo_EscortRoad/" + id + "/ExistingInspectionDesk")?.transform;
                Vector3 c = desk != null ? desk.position : point.Position;
                // 도로 방향 근사: 다음 검문(더 남쪽) 쪽. 없으면 -z(남).
                Vector3 forward = Vector3.back;
                var next = content.Points.FirstOrDefault(p => p != null && p.Id == "cargo_delivery");
                if (next != null) { forward = next.Position - c; forward.y = 0; forward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.back; }
                Vector3 right = Vector3.Cross(forward, Vector3.up).normalized;
                float yaw = Quaternion.LookRotation(forward, Vector3.up).eulerAngles.y;

                // 프롭을 데스크 주위 ≤1.5m로 밀착 배치하되, 각 프롭이 데스크 높이 대비 ±1.5m를 벗어나면
                // (협곡 벽에 얹힌 것) 앵커 쪽으로 당겨 재접지한다. 협곡 립 산포 방지.
                float baseY = Grounded(c).y;
                Place(root, id, "Fence", P_Fence, ClampToFlat(c + forward * 1.2f, c, baseY), yaw + 90, new Vector3(3.5f, 0, 0.4f), report);
                Place(root, id, "Lantern", P_Lantern, ClampToFlat(c + right * 1.2f, c, baseY), yaw, new Vector3(0.8f, 2.0f, 0.8f), report);
                Place(root, id, "Flag", P_Flag, ClampToFlat(c - forward * 0.6f + right * 1.3f, c, baseY), yaw, new Vector3(0, 4.0f, 0), report);
                Place(root, id, "Sign", P_Sign, ClampToFlat(c - forward * 0.4f - right * 1.3f, c, baseY), yaw + 180, new Vector3(0.8f, 1.5f, 0.8f), report);
            }

            Physics.SyncTransforms();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
            EditorSceneManager.SaveScene(session.gameObject.scene);
            report.status = report.notes.Any(n => n.Contains("missing") || n.Contains("FAILED")) ? "REVIEW" : "PASS";
            return Write("dress_checkpoints.json", report);
        }

        // 후보를 접지하되 데스크 높이(baseY) 대비 ±1.5m를 넘으면 앵커 쪽으로 당겨 재접지(협곡 벽 회피).
        static Vector3 ClampToFlat(Vector3 candidate, Vector3 anchor, float baseY)
        {
            for (float t = 0f; t <= 1f; t += 0.25f)
            {
                Vector3 p = Vector3.Lerp(candidate, anchor, t);
                Vector3 g = Grounded(p);
                if (Mathf.Abs(g.y - baseY) <= 1.5f) return g;
            }
            var a = Grounded(anchor); return a;
        }

        static void Place(Transform root, string checkpoint, string kind, string prefabPath, Vector3 feet, float yaw, Vector3 desiredSize, DressReport report)
        {
            try
            {
                string id = checkpoint + "_" + kind;
                var t = WorldMacroVisualCorridorAuthoring.PlaceSource(root, id, prefabPath, feet, desiredSize, yaw, false);
                t.position = feet; t.rotation = Quaternion.Euler(0, yaw, 0); // 멱등 재실행 시 보정
                report.placed.Add(id + " @ " + feet.ToString("F1"));
            }
            catch (Exception e) { report.notes.Add(checkpoint + "_" + kind + " FAILED: " + e.Message); }
        }
    }
}
