using System;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Track C — 정담(역참)과 왕소(객주 분소)를 벌린다. 축소본이 왕소를 역참 17m 옆으로 당겨
    // 순차 만남 서사(NARR:187)를 뭉갰다(#169 "정본 뭉개기"). 왕소 무리를 도로를 따라 남쪽으로
    // 옮겨 "정담 먼저 → 길 따라 왕소" 순서를 복원한다. 정담·검문·인도는 불변. 정본=03_Content.asset.
    public static partial class WorldMacroActAuthoring
    {
        // 사용자 확정 2026-09-19: z0 부근(첫 검문 직전, ~150m 남쪽). NavMesh 실측으로 z1.7는 메시 밖이라
        // 남쪽 최말단 navmesh 점 z14로 조정(TrySafeFeet=True 확인). 위성은 북쪽(+z)으로 작게 오프셋해 메시 유지.
        static readonly Vector3 WangsoAnchor = new Vector3(756.51f, 236.28f, 14.34f);

        [Serializable] sealed class SeparateReport
        {
            public string status, scope = "왕소·화물·데스크·escort_start를 도로 남쪽으로 이동. 정담 불변. 실제 입력·호송 주행·미술 미검증.";
            public string contentPath;
            public Vector3 jeongdam, wangsoBefore, wangsoAfter, cargoAfter, deskAfter, escortStartAfter;
            public float distanceBefore, distanceAfter;
            public bool wangsoGrounded, cargoGrounded, deskGrounded, escortGrounded;
            public bool jeongdamUnchanged, chainPreserved;
            public System.Collections.Generic.List<string> notes = new System.Collections.Generic.List<string>();
        }

        static PrologueContentSO.Point FindPoint(WorldMacroPlaytestSO content, string id)
            => content.Points.FirstOrDefault(p => p != null && p.Id == id);

        // 지형(Terrain_) 표면에 안정적으로 접지 — y=2200에서 내리쏴 Terrain만 받는다. 프롭·NPC 콜라이더를
        // 무시하므로 재실행해도 y가 드리프트하지 않는다. navmesh 여부는 별도(NavOk)로 report에만 기록.
        static Vector3 Grounded(Vector3 p) => WorldMacroPlaytestAuthoring.Ground(new Vector3(p.x, 2200, p.z), true);
        // 보행 도달 가능(navmesh 위) 확인 — 위치는 바꾸지 않고 참/거짓만.
        static bool NavOk(Vector3 feet) => Session.TrySafeFeet(feet + Vector3.up * 0.5f, out _);

        static Transform ChapterChild(string relative)
        {
            var root = GameObject.Find(DemoChapterTwoAuthoring.RootName);
            return root == null ? null : root.transform.Find(relative);
        }

        // acts:separate-npc — Track C 적용. 왕소 무리를 남쪽 도로로 이동한다.
        static string SeparateNpc()
        {
            Require();
            var session = Session;
            var content = session.Content;
            var report = new SeparateReport { contentPath = AssetDatabase.GetAssetPath(content) };
            if (!report.contentPath.StartsWith(WorldMacroCompactAuthoring.Folder + "/"))
                throw new InvalidOperationException("Content not isolated to compact folder: " + report.contentPath);

            var jeongdam = FindPoint(content, "jeongdam_j1");
            var wangso = FindPoint(content, "wangso_w1");
            var escortPoint = FindPoint(content, "escort_start");
            if (jeongdam == null || wangso == null || escortPoint == null)
                throw new InvalidOperationException("jeongdam_j1 / wangso_w1 / escort_start point missing");

            report.jeongdam = jeongdam.Position;
            report.wangsoBefore = wangso.Position;
            report.distanceBefore = Vector3.Distance(jeongdam.Position, wangso.Position);
            var jeongdamBefore = jeongdam.Position;

            // 왕소 = 도로 앵커. 위성은 앵커 둘레 작은 오프셋(+z 북쪽 바이어스로 navmesh 유지)으로 지형 접지.
            Vector3 wangsoFeet = Grounded(WangsoAnchor);
            Vector3 cargoFeet = Grounded(WangsoAnchor + new Vector3(3f, 0, 3f));
            Vector3 deskFeet = Grounded(WangsoAnchor + new Vector3(-2.5f, 0, 2f));
            Vector3 escortFeet = Grounded(WangsoAnchor + new Vector3(-3.5f, 0, 4.5f));
            // 보행 도달 확인(위치 불변): 왕소·출발점은 navmesh 위여야 한다.
            report.wangsoGrounded = NavOk(wangsoFeet); report.escortGrounded = NavOk(escortFeet);
            report.cargoGrounded = true; report.deskGrounded = true; // 프롭은 지형 접지만 요구

            // 1) 콘텐츠 SO의 Point·Checkpoint 이동.
            wangso.Position = wangsoFeet;
            escortPoint.Position = escortFeet;
            var startCheckpoint = content.Checkpoints.FirstOrDefault(c => c != null && c.Id == "escort_start");
            if (startCheckpoint != null) startCheckpoint.Feet = escortFeet;
            else report.notes.Add("escort_start CheckpointSpec not found in content.Checkpoints");
            EditorUtility.SetDirty(content);

            // 2) 씬 트랜스폼 이동(호송이 런타임에 reparent하는 원본 위치).
            var wangsoT = ChapterChild("NPCs/wangso_w1");
            var cargoT = ChapterChild("OwnedStructures/Demo_SealedCargo");
            var deskT = ChapterChild("OwnedStructures/Demo_Merchant_Desk");
            if (wangsoT == null || cargoT == null) throw new InvalidOperationException("wangso_w1 or Demo_SealedCargo transform not found under " + DemoChapterTwoAuthoring.RootName);
            wangsoT.position = wangsoFeet;
            cargoT.position = cargoFeet;
            if (deskT != null) deskT.position = deskFeet; else report.notes.Add("Demo_Merchant_Desk transform not found");
            // 왕소가 도로(북쪽 정담 방향)를 바라보게: 정담 쪽을 향한 yaw.
            Vector3 toRoad = jeongdam.Position - wangsoFeet; toRoad.y = 0;
            if (toRoad.sqrMagnitude > 0.01f) wangsoT.rotation = Quaternion.LookRotation(toRoad.normalized, Vector3.up);

            report.wangsoAfter = wangso.Position;
            report.cargoAfter = cargoFeet;
            report.deskAfter = deskT != null ? deskFeet : Vector3.zero;
            report.escortStartAfter = escortFeet;
            report.distanceAfter = Vector3.Distance(jeongdam.Position, wangso.Position);
            report.jeongdamUnchanged = jeongdam.Position == jeongdamBefore;

            // 3) 객주 분소 건물 보강 — 왕소가 허허벌판 데스크에 서지 않도록 뒤(도로 반대·남쪽)에 한옥과 보급을 놓는다.
            //    역참과 같은 SM_Naeposa 한옥을 재사용(같은 세계의 관영 건물 문법). 멱등: PlaceSource가 기존이면 재사용.
            var props = ChapterChild("OwnedStructures");
            if (props != null)
            {
                Vector3 roadDir = (jeongdam.Position - wangsoFeet); roadDir.y = 0; roadDir = roadDir.sqrMagnitude > 0.01f ? roadDir.normalized : Vector3.forward;
                Vector3 behind = -roadDir; // 도로 반대편(남쪽) = 건물 자리
                Vector3 buildingFeet = Grounded(wangsoFeet + behind * 8f + Vector3.Cross(behind, Vector3.up) * 2f);
                float buildingYaw = Quaternion.LookRotation(roadDir, Vector3.up).eulerAngles.y; // 정면이 도로를 향하게
                var branch = WorldMacroVisualCorridorAuthoring.PlaceSource(props, "Demo_Merchant_Branch", "Assets/HwaseongHaenggung/Prefabs/SM_Naeposa.prefab", buildingFeet, new Vector3(11, 0, 10), buildingYaw, false);
                branch.position = buildingFeet; branch.rotation = Quaternion.Euler(0, buildingYaw, 0); // 멱등 재실행 시 위치 보정
                // 지붕·기둥은 메시 콜라이더로(건물 전체 솔리드 박스 금지 — 역참과 동일 규약).
                foreach (var filter in branch.GetComponentsInChildren<MeshFilter>())
                    if (filter.GetComponent<MeshCollider>() == null) { var mc = filter.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = filter.sharedMesh; }
                Vector3 suppliesFeet = Grounded(wangsoFeet + behind * 4f + Vector3.Cross(behind, Vector3.up) * -2.5f);
                var supplies = WorldMacroVisualCorridorAuthoring.PlaceSource(props, "Demo_Merchant_Supplies", "Assets/KoreanTraditionalFestival/Prefabs/SM_SackOfRice.prefab", suppliesFeet, new Vector3(1.1f, 1.2f, 1f), 20f, true);
                supplies.position = suppliesFeet;
                report.notes.Add("merchant branch building + supplies placed on terrain at " + buildingFeet.ToString("F1"));
            }
            else report.notes.Add("OwnedStructures parent not found — building not placed");

            // 호송 체인 보존 확인: 왕소 트랜스폼 경로명이 그대로여야 DemoEscortSceneAuthoring 바인딩이 유효.
            report.chainPreserved = ChapterChild("NPCs/wangso_w1") != null && ChapterChild("OwnedStructures/Demo_SealedCargo") != null;

            // 라이브 캠페인 TravelHint·DestinationLabel의 co-location 표현을 새 지리로 교체(Prepare 재실행 없이 정합).
            var campaign = content.Campaign;
            if (campaign != null)
            {
                int patched = 0;
                foreach (var s in campaign.Stages)
                {
                    if (s == null) continue;
                    if (!string.IsNullOrEmpty(s.TravelHint) && s.TravelHint.Contains("역참 옆 객주 접수처"))
                    { s.TravelHint = "상경 화물 의뢰가 남았다. 역참에서 큰길을 따라 남쪽으로 내려가면 객주 분소의 왕소가 봉인 화물과 숲길 사정을 안다."; patched++; }
                    else if (!string.IsNullOrEmpty(s.TravelHint) && s.TravelHint.Contains("객주 접수처의 왕소에게 돌아가"))
                    { s.TravelHint = "이제 상경할 수 있다. 큰길 남쪽 객주 분소의 왕소에게 돌아가 화물과 함께 자동차에 탑승해 출발한다. 높은 석대는 나중에 돌아와도 된다."; patched++; }
                    if (s.DestinationLabel == "객주 접수처") { s.DestinationLabel = "객주 분소"; patched++; }
                }
                report.notes.Add("campaign hints/labels patched: " + patched);
                EditorUtility.SetDirty(campaign);
            }

            Physics.SyncTransforms();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
            EditorSceneManager.SaveScene(session.gameObject.scene);

            report.status = (report.wangsoGrounded && report.escortGrounded && report.jeongdamUnchanged && report.chainPreserved && report.distanceAfter > 100f) ? "PASS" : "REVIEW";
            return Write("separate_npc.json", report);
        }
    }
}
