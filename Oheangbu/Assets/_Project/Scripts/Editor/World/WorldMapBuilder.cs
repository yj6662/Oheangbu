using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.BrushRender;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // [SPEC-WORLD-MAP §7 빌더 · §8 P1] 월드 조립 진입점 — 결정론(입력 해시 = 출력)·재실행 가능. 전부 public static string(reflection-method-call).
    // P1 범위(구조 — Terrain·셰이더 무접촉, D1 게이트): BuildArea = ① EnsureManifestDeps + 레이어·에이전트 타입 확인 ② SO 로드 → 입력 해시
    // + 시트 검증(§5 POI·체크포인트·게이트·walkTargets) ④ TerrainSynth.Generate → HeightsHash ⑬ 요약. ③⑤~⑫(씬·Terrain·carve·키트·NavMesh·도장)는
    // BuildTerrainP2 게이트 뒤(SPEC-SPIKE-WORLD-LOOKDEV PASS 전 호출 = NotSupportedException).
    // BuildFarSet = 층 11 상주 씬 스켈레톤(원경 링·앵커·Sun·Volume·Driver 2종·RealmLifetimeScope·리그·귀환 포탈·AreaLoader — Terrain 0).
    // 수치는 SO에서 읽는다 — 원경 배치값(태양 Euler·재질 _AmbientLevel·포탈 거리)은 RealmSheetSO 원경 정책 필드, 이 파일의 숫자는 화이트리스트뿐(§6 A9).
    // 입력 해시(§6 A11)는 SO의 디스크 YAML 텍스트를 쓴다 — EditorJsonUtility는 참조를 instanceID로 적어 세션마다 달라진다.
    // fake-null 규율(run-20260906 §MCP ②): NewScene → 에셋 생성(재질·메시·리그·포탈) → 경로로 재로드 → SerializedObject 배선. 앞서 든 참조는 값만 읽는다.
    public static class WorldMapBuilder
    {
        public const string BuilderVersion = "WorldMapBuilder P1 2026-09-06";
        public const string AreaSheetPath = "Assets/_Project/Data/World/AreaSheet_GeumpyoRoad.asset";
        public const string RealmSheetPath = "Assets/_Project/Data/World/RealmSheet_Cheongrim.asset";
        public const string AreaChannelPath = "Assets/_Project/Data/World/Events/EC_AreaId.asset";
        public const string WorldSceneFolder = "Assets/_Project/Scenes/World";
        public const string FarSetScenePath = WorldSceneFolder + "/W_Cheongrim_FarSet.unity";
        public const string TerrainDataFolder = WorldSceneFolder + "/TerrainData";
        public const string StandinFloorName = "P1_StandinFloor";
        private const string InkWorldShader = "Oheangbu/InkWorld";
        private const string InkTerrainMaterialName = "M_InkTerrain";
        private const string GroundLayerName = "WorldGround";
        private const string AgentTypeName = "Humanoid_Oheangbu";
        private const string AmbientLevelProperty = "_AmbientLevel";

        // §5-1 필수 POI · 체크포인트(성황당 이름 3 불변 + 주막) · 게이트 슬롯(예고 3) · 길.
        private static readonly string[] RequiredPois =
        {
            "MineExit", "Shrine_Entry", "Inn_Geumpyo", "Stele_Geumpyo", "Yeokcham_Geumpyo", "Village_Logging",
            "Gaekju_Village", "Shrine_Logging", "Entry_LoggingCamp", "Shrine_Junction",
        };
        private static readonly string[] RequiredCheckpoints = { "Shrine_Entry", "Inn_Geumpyo", "Shrine_Logging", "Shrine_Junction" };
        private static readonly string[] RequiredGates = { "Guk_CliffTop", "Guk_LoggingTower", "MinePreview" };
        private static readonly string[] RequiredPaths = { "Trail_Main", "Trail_Stele", "Trail_Logging" };
        private static readonly string[] RequiredLayers = { "WorldGround", "WorldRidge", "WorldAnchor", "WorldLight", "WorldVeg", "WorldRibbon" };
        private const string InnWalkFrom = "MineExit";
        private const string InnWalkTo = "Inn_Geumpyo";
        private const string BossShrineFrom = "Shrine_Logging";
        private const string BossShrineTo = "Entry_LoggingCamp";

        // P2 게이트 — SPEC-SPIKE-WORLD-LOOKDEV G2 PASS 후 코드 편집으로만 연다(설정값 아님 · 런타임 토글 0).
        // 2026-09-20 개방: 예준 G2 육안 판정 PASS(DECISIONS #221). BuildTerrainP2·강토 터레인/능선 빌드 허용.
        private static readonly bool TerrainGateOpen = true;

        [MenuItem("Oheangbu/World/금표의 길 조립")]
        public static void Build()
        {
            Debug.Log(BuildArea(AreaSheetPath));
        }

        [MenuItem("Oheangbu/World/청림 원경 세트 조립 (W_Cheongrim_FarSet)")]
        public static void BuildFarSetMenu()
        {
            Debug.Log(BuildFarSet(RealmSheetPath));
        }

        // ---- BuildArea (P1) ----

        public static string BuildArea(string sheetPath)
        {
            var sw = Stopwatch.StartNew();
            var report = new StringBuilder();

            // ① 의존·레이어·에이전트 타입
            string manifest;
            bool manifestOk = ManifestDeps.TryEnsureManifestDeps(out manifest);
            report.Append(manifest);
            var missingLayers = new List<string>();
            foreach (var layer in RequiredLayers)
            {
                if (LayerMask.NameToLayer(layer) < 0) missingLayers.Add(layer);
            }
            report.Append(" | layers ").Append(missingLayers.Count == 0 ? "OK" : "FAIL missing " + string.Join(",", missingLayers));
            int agentTypeId;
            bool agentOk = TryFindAgentType(AgentTypeName, out agentTypeId);
            report.Append(" | agent ").Append(agentOk ? AgentTypeName + "=" + agentTypeId : "FAIL " + AgentTypeName + " 없음(Navigation 창 P0 chore)");

            // ② SO 로드(P1은 씬을 만들지 않으므로 fake-null 함정 없음 — 에셋 저장 0)
            var sheet = AssetDatabase.LoadAssetAtPath<AreaSheetSO>(sheetPath);
            if (sheet == null) return "FAIL BuildArea: AreaSheetSO 없음 " + sheetPath + " | " + report;
            var synth = sheet.Synth;
            if (synth == null) return "FAIL BuildArea: AreaSheetSO.synth 미지정 " + sheetPath + " | " + report;
            var realm = FindRealmFor(sheet);
            if (realm == null) return "FAIL BuildArea: 이 시트를 areas[]에 가진 RealmSheetSO 없음 | " + report;
            var scale = realm.WorldScale;
            if (scale == null) return "FAIL BuildArea: RealmSheetSO.worldScale 미지정 | " + report;

            string inputHash = ComputeInputHash(sheet, synth, scale);
            var problems = ValidateSheet(sheet, scale);

            // ④ 합성(순수 C#) → heightsHash(§6 A11)
            TerrainSynth.SynthResult synthResult;
            try
            {
                synthResult = TerrainSynth.GenerateDetailed(sheet, synth, scale, null, null);
            }
            catch (Exception e)
            {
                return "FAIL BuildArea: TerrainSynth " + e.GetType().Name + ": " + e.Message + " | " + report;
            }
            string heightsHash = TerrainSynth.HeightsHash(synthResult.Heights);
            int res = synthResult.Heights.GetLength(0);
            int cliffCells = CountTrue(synthResult.CliffMask);
            int maskCells = CountTrue(synthResult.ErosionMask);

            sw.Stop();
            bool ok = manifestOk && missingLayers.Count == 0 && agentOk && problems.Count == 0;
            var summary = new StringBuilder();
            summary.Append(ok ? "OK" : "FAIL(" + problems.Count + ")").Append(" BuildArea P1 — 구조만(Terrain 0 · 씬 0 · D1 게이트)");
            summary.Append(" | sheet=").Append(sheet.AreaIdString).Append(" tile=").Append(res).Append("²");
            summary.Append(" | inputHash=").Append(inputHash);
            summary.Append(" | heightsHash=").Append(heightsHash);
            summary.Append(" | synth m=[").Append(synthResult.MinHeightM.ToString("F1")).Append(", ").Append(synthResult.MaxHeightM.ToString("F1")).Append("]");
            summary.Append(" cliffCells=").Append(cliffCells).Append(" erosionMaskCells=").Append(maskCells);
            summary.Append(" | ").Append(sw.Elapsed.TotalSeconds.ToString("F1")).Append("s");
            summary.Append(" | ").Append(report);
            foreach (var p in problems)
            {
                summary.Append("\n  - ").Append(p);
            }
            return summary.ToString();
        }

        // ⑥ Terrain 인스턴스화 — P2 게이트(SPEC-SPIKE-WORLD-LOOKDEV PASS) 전에는 호출 = 예외. BuildArea(P1)는 호출하지 않는다.
        public static Terrain BuildTerrainP2(AreaSheetSO sheet, float[,] heights, Material inkTerrain, out string heightsHash)
        {
            if (!TerrainGateOpen) throw new NotSupportedException("P2 gate: SPEC-SPIKE-WORLD-LOOKDEV PASS required");
            if (sheet == null || sheet.Tiles == null || sheet.Tiles.Length == 0) throw new ArgumentException("tiles 없음", nameof(sheet));
            var tile = sheet.Tiles[0];
            DevSceneKit.EnsureFolder(TerrainDataFolder);
            string dataPath = $"{TerrainDataFolder}/T_{sheet.AreaIdString}_00.asset";
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
            if (data == null)
            {
                data = new TerrainData();
                AssetDatabase.CreateAsset(data, dataPath);
            }
            data.heightmapResolution = tile.heightmapRes;
            data.size = new Vector3(tile.sizeM, tile.heightM, tile.sizeM);
            data.SetHeights(0, 0, heights);
            heightsHash = TerrainSynth.HeightsHash(heights);
            var go = Terrain.CreateTerrainGameObject(data);
            go.name = $"T_{sheet.AreaIdString}_00";
            go.transform.position = new Vector3(tile.originXZ.x, 0f, tile.originXZ.y);
            var terrain = go.GetComponent<Terrain>();
            TerrainSealer.Seal(terrain, inkTerrain);
            int ground = LayerMask.NameToLayer(GroundLayerName);
            if (ground >= 0) go.layer = ground;
            return terrain;
        }

        // ---- BuildFarSet (P1 스켈레톤) ----

        public static string BuildFarSet(string realmSheetPath)
        {
            // ① 사전 검증 + 값 복사 — 씬·에셋 생성 전에 든 참조(probe)는 값만 읽고 배선에는 쓰지 않는다(run-20260906 §MCP ②)
            var probe = AssetDatabase.LoadAssetAtPath<RealmSheetSO>(realmSheetPath);
            if (probe == null) return "FAIL BuildFarSet: RealmSheetSO 없음 " + realmSheetPath;
            if (probe.WorldScale == null) return "FAIL BuildFarSet: RealmSheetSO.worldScale 미지정";
            if (AssetDatabase.LoadAssetAtPath<ElementPaletteSO>(DevSceneKit.PalettePath) == null) return "FAIL BuildFarSet: palette missing " + DevSceneKit.PalettePath;
            if (AssetDatabase.LoadAssetAtPath<AreaIdEventChannelSO>(AreaChannelPath) == null) return "FAIL BuildFarSet: AreaIdEventChannelSO 없음 " + AreaChannelPath;

            int ridgeCount = Math.Max(1, probe.WorldScale.RidgeLayerCount);
            float[] ridgeAmbient = probe.RidgeAmbientLevels != null && probe.RidgeAmbientLevels.Length > 0
                ? (float[])probe.RidgeAmbientLevels.Clone()
                : new[] { probe.SinmokAmbientLevel };
            float sinmokAmbient = probe.SinmokAmbientLevel;
            float standinAmbient = probe.StandinAmbientLevel;
            float returnPortalBehindM = probe.ReturnPortalBehindM;
            Vector3 sunEuler = probe.SunEuler;

            var warnings = new List<string>();
            var area = FirstArea(probe);
            Vector3 spawnGround = Vector3.zero;
            float spawnYaw = 0f;
            bool hasTile = false;
            Vector2 tileOrigin = Vector2.zero;
            float tileSize = 0f;
            if (area == null) warnings.Add("RealmSheetSO.areas 비어 있음 — 리그 스폰 = 원점");
            else
            {
                var poi = area.FindPoi(AreaLoader.SpawnPoiId);
                if (poi != null)
                {
                    spawnGround = poi.position;
                    spawnYaw = poi.yaw;
                }
                else warnings.Add($"POI '{AreaLoader.SpawnPoiId}' 없음 — 리그 스폰 = 원점");
                if (area.Tiles != null && area.Tiles.Length > 0)
                {
                    hasTile = true;
                    tileOrigin = area.Tiles[0].originXZ;
                    tileSize = area.Tiles[0].sizeM;
                }
            }

            // ② 씬 비우기 → 에셋 생성 전부(재질 → 원경 메시 → 리그 → 포탈). 재질은 경로만 기록해 두고 ③에서 재로드한다.
            var scene = DevSceneKit.NewEmptyScene();
            DevSceneKit.EnsureFolder(WorldSceneFolder);
            DevSceneKit.EnsureFolder(FarSetBuilder.MeshFolder);

            var ridgeMaterialPaths = new string[ridgeCount];
            for (int i = 0; i < ridgeCount; i++)
            {
                float ambient = ridgeAmbient[Math.Min(i, ridgeAmbient.Length - 1)];
                var created = DevSceneKit.EnsureMaterial($"M_InkWorld_Ridge_L{i + 1}", InkWorldShader, m => m.SetFloat(AmbientLevelProperty, ambient));
                ridgeMaterialPaths[i] = AssetDatabase.GetAssetPath(created);
            }
            string sinmokMaterialPath = AssetDatabase.GetAssetPath(
                DevSceneKit.EnsureMaterial("M_InkWorld_Sinmok", InkWorldShader, m => m.SetFloat(AmbientLevelProperty, sinmokAmbient)));
            string rockMaterialPath = AssetDatabase.GetAssetPath(
                DevSceneKit.EnsureMaterial("M_InkWorld_Rock", InkWorldShader, m => m.SetFloat(AmbientLevelProperty, standinAmbient)));

            // 원경 세트 기하(층 8) — 메시 에셋 저장 포함 · 재질 0(④에서 배선). 루트 = 구역 중심 XZ · Y 0 = 압출 하단(바닥 −floorY)
            var farSetRealm = AssetDatabase.LoadAssetAtPath<RealmSheetSO>(realmSheetPath);
            var farSet = FarSetBuilder.BuildFarSet(farSetRealm, null);

            // 리그 · 귀환 포탈(스폰 뒤 · 리그 방향) — 포탈은 개발 재질(EnsureMaterial)을 만들 수 있어 에셋 생성 구간 안
            Vector3 spawn = spawnGround + Vector3.up * DevSceneKit.Spawn.y;   // 캡슐 중심 오프셋 = 개발 씬 공통 스폰 y
            var rig = DevSceneKit.InstantiateRig(scene, spawn, spawnYaw);
            Vector3 forward = Quaternion.Euler(0f, spawnYaw, 0f) * Vector3.forward;
            Vector3 portalPos = spawnGround - forward * returnPortalBehindM;
            DevSceneKit.AddReturnPortal(portalPos, spawn);

            // ③ 마지막 에셋 생성 뒤 — 배선에 쓰는 참조를 전부 경로로 (재)로드
            var realm = AssetDatabase.LoadAssetAtPath<RealmSheetSO>(realmSheetPath);
            var scale = realm != null ? realm.WorldScale : null;
            var palette = AssetDatabase.LoadAssetAtPath<ElementPaletteSO>(DevSceneKit.PalettePath);
            var channel = AssetDatabase.LoadAssetAtPath<AreaIdEventChannelSO>(AreaChannelPath);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(DevSceneKit.InkLookProfilePath);
            var ridgeMaterials = new Material[ridgeCount];
            for (int i = 0; i < ridgeCount; i++)
            {
                ridgeMaterials[i] = AssetDatabase.LoadAssetAtPath<Material>(ridgeMaterialPaths[i]);
            }
            var sinmokMaterial = AssetDatabase.LoadAssetAtPath<Material>(sinmokMaterialPath);
            var rockMaterial = AssetDatabase.LoadAssetAtPath<Material>(rockMaterialPath);
            var camera = rig.GetComponentInChildren<Camera>(true);

            // ④ 배선 — 씬 객체 생성만(에셋 생성 0). 비어 있는 참조는 이름으로 모아 마지막에 경고한다.
            var nullRefs = new List<string>();
            nullRefs.AddRange(FarSetBuilder.AssignMaterials(farSet, ridgeMaterials, sinmokMaterial));

            // P1 임시 바닥 — Terrain 0인 동안 리그가 설 자리(스폰 POI 접지 고도 · 타일 크기 · 두께 1). P2 BuildTerrain이 제거한다
            if (hasTile)
            {
                float floorY = spawnGround.y;
                var center = new Vector3(tileOrigin.x + tileSize * 0.5f, floorY - 0.5f, tileOrigin.y + tileSize * 0.5f);
                DevSceneKit.AddCube(null, StandinFloorName, center, new Vector3(tileSize, 1f, tileSize), rockMaterial);
                if (rockMaterial == null) nullRefs.Add(StandinFloorName + ".sharedMaterial");
            }

            // 태양 — 백색·Intensity만(스파이크 #153). 그림자 설정은 RPAsset 무변경
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Color.white;
            sun.intensity = 1f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(sunEuler);

            // Volume — VP_InkLook(#156)
            var volumeGo = new GameObject("GlobalVolume_InkLook");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
            if (profile == null) nullRefs.Add("Volume.sharedProfile");

            // 드라이버 2종 — 합산 정확히 1(층 11)
            var lookGo = new GameObject("WorldLook");
            var lookDriver = lookGo.AddComponent<WorldLookDriver>();
            var lookSo = new SerializedObject(lookDriver);
            AssignRef(lookSo, "_palette", palette, nullRefs);
            int element = realm != null ? realm.Element : probe.Element;
            lookSo.FindProperty("_veinInitial").intValue = element;
            lookSo.FindProperty("_beaconInitial").intValue = element;   // 봉수 = 강토 오행색(층 8 GetRawVeinColor(element))
            AssignRef(lookSo, "_skyCamera", camera, nullRefs);
            lookSo.ApplyModifiedPropertiesWithoutUndo();
            var scaleDriver = lookGo.AddComponent<WorldScaleDriver>();
            var scaleSo = new SerializedObject(scaleDriver);
            AssignRef(scaleSo, "_scale", scale, nullRefs);
            AssignRef(scaleSo, "_camera", camera, nullRefs);
            scaleSo.ApplyModifiedPropertiesWithoutUndo();

            // 강토 스코프(부모) + AreaLoader
            var scopeGo = new GameObject("RealmLifetimeScope");
            var scope = scopeGo.AddComponent<RealmLifetimeScope>();
            var scopeSo = new SerializedObject(scope);
            AssignRef(scopeSo, "_worldScale", scale, nullRefs);
            AssignRef(scopeSo, "_realmSheet", realm, nullRefs);
            AssignRef(scopeSo, "_areaChannel", channel, nullRefs);
            scopeSo.ApplyModifiedPropertiesWithoutUndo();

            var loaderGo = new GameObject("AreaLoader");
            var loader = loaderGo.AddComponent<AreaLoader>();
            var loaderSo = new SerializedObject(loader);
            AssignRef(loaderSo, "_realmSheet", realm, nullRefs);
            AssignRef(loaderSo, "_playerRig", rig, nullRefs);
            loaderSo.ApplyModifiedPropertiesWithoutUndo();

            lookDriver.Apply();
            scaleDriver.Apply();

            // ⑤ 참조 점검 — 비어 있는 objectReferenceValue는 전부 이름으로 경고(무음 fileID 0 금지)
            if (nullRefs.Count > 0)
            {
                string list = string.Join(", ", nullRefs);
                Debug.LogWarning("[World] BuildFarSet: 비어 있는 참조 " + nullRefs.Count + "건 — " + list);
                warnings.Add("비어 있는 참조: " + list);
            }

            bool saved = EditorSceneManager.SaveScene(scene, FarSetScenePath);
            DevSceneKit.RegisterBuildScenes(FarSetScenePath);
            AssetDatabase.SaveAssets();

            var sb = new StringBuilder();
            sb.Append(saved ? "OK" : "FAIL").Append(": ").Append(FarSetScenePath);
            sb.Append(" rings=").Append(ridgeCount).Append(" sinmok=").Append(probe.SinmokPosition);
            sb.Append(" farSetRoot=").Append(farSet.transform.position).Append(" spawn=").Append(spawn).Append(" yaw=").Append(spawnYaw);
            sb.Append(" nullRefs=").Append(nullRefs.Count);
            sb.Append(" | ").Append(DevSceneKit.AssertSingleRig());
            foreach (var w in warnings)
            {
                sb.Append("\n  ⚠ ").Append(w);
            }
            return sb.ToString();
        }

        // ---- 검증(§5 배치표 ↔ 시트 · §6 A12 서사 침묵 · walkTargets 정합) ----

        public static List<string> ValidateSheet(AreaSheetSO sheet, WorldScaleSO scale)
        {
            var problems = new List<string>();
            if (sheet.Tiles == null || sheet.Tiles.Length == 0) problems.Add("tiles 비어 있음");
            else
            {
                var tile = sheet.Tiles[0];
                int cells = tile.heightmapRes - 1;
                if (tile.heightmapRes < 2 || (cells & (cells - 1)) != 0) problems.Add($"heightmapRes {tile.heightmapRes} ≠ 2^n+1");
                if (tile.sizeM <= 0f || tile.heightM <= 0f) problems.Add("tile sizeM/heightM ≤ 0");
            }
            if (sheet.Synth == null) problems.Add("synth 미지정");
            else if (sheet.Synth.ValleyAxisKnots == null || sheet.Synth.ValleyAxisKnots.Length < 2) problems.Add("synth.valleyAxisKnots < 2");
            if (string.IsNullOrEmpty(sheet.ScenePath)) problems.Add("scenePath 공란");

            foreach (var id in RequiredPois)
            {
                if (sheet.FindPoi(id) == null) problems.Add("POI 없음: " + id);
            }
            if (sheet.Pois != null)
            {
                foreach (var poi in sheet.Pois)
                {
                    if (poi == null) continue;
                    if (!string.IsNullOrEmpty(poi.narrativeSlot)) problems.Add($"narrativeSlot 비공란(A12 서사 침묵): {poi.id}");
                    if (!InsideTile(sheet, poi.position)) problems.Add($"POI 타일 밖: {poi.id} {poi.position}");
                }
            }

            foreach (var id in RequiredCheckpoints)
            {
                bool found = false;
                if (sheet.Checkpoints != null)
                {
                    foreach (var cp in sheet.Checkpoints)
                    {
                        if (cp != null && cp.id == id)
                        {
                            found = true;
                            if (sheet.FindPoi(cp.poiId) == null) problems.Add($"체크포인트 {cp.id} poiId 미해결: {cp.poiId}");
                        }
                    }
                }
                if (!found) problems.Add("체크포인트 없음: " + id);
            }

            foreach (var id in RequiredGates)
            {
                bool found = false;
                if (sheet.Gates != null)
                {
                    foreach (var g in sheet.Gates)
                    {
                        if (g != null && g.id == id) found = true;
                    }
                }
                if (!found) problems.Add("게이트 슬롯 없음: " + id);
            }
            if (sheet.Gates != null)
            {
                foreach (var g in sheet.Gates)
                {
                    if (g != null && g.mode == GateMode.MainPath) problems.Add($"본편 경로 위 게이트(A1 청림 0): {g.id}");
                }
            }

            foreach (var id in RequiredPaths)
            {
                var path = sheet.FindPath(id);
                if (path == null) problems.Add("길 없음: " + id);
                else if (path.knots == null || path.knots.Length < 2) problems.Add("길 knots < 2: " + id);
            }
            if (sheet.Paths != null)
            {
                foreach (var path in sheet.Paths)
                {
                    if (path == null || path.knots == null) continue;
                    foreach (var k in path.knots)
                    {
                        if (!InsideTile(sheet, k))
                        {
                            problems.Add($"길 knot 타일 밖: {path.id} {k}");
                            break;
                        }
                    }
                }
            }

            // walkTargets — POI 해결·값 정합·D4 시드·체크포인트 상한·난소 30 s·직선거리 하한(경로는 직선보다 짧을 수 없다)
            bool innTarget = false;
            if (sheet.WalkTargets != null)
            {
                foreach (var w in sheet.WalkTargets)
                {
                    if (w == null) continue;
                    var from = sheet.FindPoi(w.from);
                    var to = sheet.FindPoi(w.to);
                    if (from == null || to == null)
                    {
                        problems.Add($"walkTarget POI 미해결: {w.from}→{w.to}");
                        continue;
                    }
                    if (w.minutes <= 0f || w.tolerance < 0f) problems.Add($"walkTarget 값 이상: {w.from}→{w.to} {w.minutes}±{w.tolerance}");
                    float straightMin = Vector3.Distance(from.position, to.position) / scale.MetersPerMinute;
                    if (straightMin > w.minutes + w.tolerance) problems.Add($"walkTarget 직선거리 초과: {w.from}→{w.to} 직선 {straightMin:F2}분 > {w.minutes}+{w.tolerance}");
                    if (w.from == InnWalkFrom && w.to == InnWalkTo)
                    {
                        innTarget = true;
                        if (Math.Abs(w.minutes - scale.InnTargetWalkMinutes) > w.tolerance) problems.Add($"D4 시드 불일치: {w.minutes} vs innTargetWalkMinutes {scale.InnTargetWalkMinutes}");
                    }
                    if (w.from == BossShrineFrom && w.to == BossShrineTo && w.minutes * (scale.MetersPerMinute / scale.WalkSpeedMps) > scale.BossShrineSeconds)
                    {
                        problems.Add($"난소 직전 성황당 초과: {w.from}→{w.to} {w.minutes}분 > {scale.BossShrineSeconds}s");
                    }
                    if (w.minutes > scale.AreaWalkMinutesMax) problems.Add($"walkTarget > areaWalkMinutesMax: {w.from}→{w.to}");
                }
            }
            if (!innTarget) problems.Add($"walkTarget {InnWalkFrom}→{InnWalkTo}(D4) 없음");

            // 인력 Mid 링크 직선 ≤ MidAttractionMaxDistance(§6 D1 — POI 간 링크만 P1에서 계산 가능)
            if (sheet.Attraction != null)
            {
                foreach (var a in sheet.Attraction)
                {
                    if (a == null || a.layer != AttractionLayer.Mid) continue;
                    var from = sheet.FindPoi(a.fromPoi);
                    var to = sheet.FindPoi(a.toPoi);
                    if (from == null || to == null) continue;
                    float d = Vector3.Distance(from.position, to.position);
                    if (d > scale.MidAttractionMaxDistance) problems.Add($"Mid 인력 직선 초과: {a.fromPoi}→{a.toPoi} {d:F0} m > {scale.MidAttractionMaxDistance:F0}");
                }
            }
            return problems;
        }

        // ---- 보조 ----

        // 입력 해시(§7 ② · §6 A11) = 시트·합성·스케일 SO의 디스크 YAML 텍스트 + 빌더 버전 + 랜드마크 checkReportHash — SHA-256 hex.
        // YAML의 참조는 GUID라 세션 간 안정(EditorJsonUtility.ToJson은 {"instanceID":N}을 적어 에디터 세션마다 달라진다).
        public static string ComputeInputHash(AreaSheetSO sheet, TerrainSynthSO synth, WorldScaleSO scale)
        {
            var sb = new StringBuilder();
            sb.Append(AssetYaml(sheet)).Append('\n');
            sb.Append(AssetYaml(synth)).Append('\n');
            sb.Append(AssetYaml(scale)).Append('\n');
            sb.Append(BuilderVersion);
            if (sheet != null && sheet.Landmarks != null)
            {
                foreach (var lm in sheet.Landmarks)
                {
                    if (lm != null && lm.landmark != null) sb.Append('\n').Append(lm.landmark.CheckReportHash);
                }
            }
            using (var sha = SHA256.Create())
            {
                return TerrainSynth.ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
            }
        }

        // SO의 디스크 YAML 원문 — 에셋이 아니거나 파일이 없으면 이름만(경고 — 해시 안정성 보장 밖).
        private static string AssetYaml(Object so)
        {
            if (so == null) return "null";
            string path = AssetDatabase.GetAssetPath(so);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogWarning("[World] ComputeInputHash: 디스크 YAML 없음(미저장 SO?) — " + so.name);
                return "unsaved:" + so.name;
            }
            return File.ReadAllText(path);
        }

        public static RealmSheetSO FindRealmFor(AreaSheetSO sheet)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(RealmSheetSO)))
            {
                var realm = AssetDatabase.LoadAssetAtPath<RealmSheetSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (realm == null || realm.Areas == null) continue;
                foreach (var a in realm.Areas)
                {
                    if (a == sheet) return realm;
                }
            }
            return null;
        }

        // SerializedObject 참조 배선 — null이면 「타입.필드」로 기록(⑤에서 경고 목록).
        private static void AssignRef(SerializedObject so, string property, Object value, List<string> nullRefs)
        {
            var prop = so.FindProperty(property);
            if (prop == null) throw new InvalidOperationException(so.targetObject.GetType().Name + "." + property + " 직렬화 필드 없음");
            prop.objectReferenceValue = value;
            if (value == null) nullRefs.Add(so.targetObject.GetType().Name + "." + property);
        }

        private static AreaSheetSO FirstArea(RealmSheetSO realm)
        {
            if (realm.Areas == null) return null;
            foreach (var a in realm.Areas)
            {
                if (a != null) return a;
            }
            return null;
        }

        private static bool InsideTile(AreaSheetSO sheet, Vector3 p)
        {
            if (sheet.Tiles == null || sheet.Tiles.Length == 0) return true;
            var tile = sheet.Tiles[0];
            return p.x >= tile.originXZ.x && p.x <= tile.originXZ.x + tile.sizeM
                && p.z >= tile.originXZ.y && p.z <= tile.originXZ.y + tile.sizeM;
        }

        // NavMesh 에이전트 타입 이름 → agentTypeID(층 12 — 없으면 false).
        public static bool TryFindAgentType(string name, out int agentTypeId)
        {
            int count = NavMesh.GetSettingsCount();
            for (int i = 0; i < count; i++)
            {
                var settings = NavMesh.GetSettingsByIndex(i);
                if (NavMesh.GetSettingsNameFromID(settings.agentTypeID) == name)
                {
                    agentTypeId = settings.agentTypeID;
                    return true;
                }
            }
            agentTypeId = 0;
            return false;
        }

        private static int CountTrue(bool[,] mask)
        {
            if (mask == null) return 0;
            int n = 0;
            foreach (bool b in mask)
            {
                if (b) n++;
            }
            return n;
        }
    }
}
