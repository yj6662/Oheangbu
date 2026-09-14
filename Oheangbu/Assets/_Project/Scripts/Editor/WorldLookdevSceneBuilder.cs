using System;
using Oheangbu.App;
using Oheangbu.BrushRender;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // [SPEC-SPIKE-WORLD-LOOKDEV §5-11] 월드 룩 실증 씬 빌더 — 배치표 단일 출처(Spec 표와 같이 움직인다). 형상 = 전부 코드 조립(크레딧 0).
    // P1 범위: 갱도 큐브 키트(Mine_Straight6m×6 · Timber×12 · Rubble · Portal) + 광맥 VeinBand×3 + 태양 + 리그 + WorldLookDriver.
    // P2에서 골짜기 heightfield·능선 3겹·앵커·봉수가 붙는다 — 그 자리는 지금 평판 스탠드인(Valley_Standin).
    // 재실행 가능: 씬은 새로 만들고, 재질·프리팹은 없을 때만 만든다(Ensure — 튜닝값은 재질·프리팹이 들고 코드가 덮지 않는다).
    // 씬에 색 값 저장 0: 카메라 배경만 팔레트에서 읽어 넣는다(드라이버가 매 활성화마다 다시 쓴다).
    public static class WorldLookdevSceneBuilder
    {
        public const string ScenePath = DevSceneKit.WorldLookdevScenePath;
        private const string KitFolder = "Assets/_Project/Prefabs/Kit";
        private const string LookdevFolder = "Assets/_Project/Prefabs/Lookdev";
        private const string InkWorldShader = "Oheangbu/InkWorld";
        private const string InkLightSourceShader = "Oheangbu/InkLightSource";

        // ---- 배치표 (Spec §5-11) ----
        private const float TunnelWidth = 3f;
        private const float TunnelHeight = 3f;
        private const float SegmentLength = 6f;
        private const float WallThickness = 0.3f;
        private const int SegmentCount = 6;            // z −36 ~ 0
        private const float TunnelEndZ = -36f;
        private const float TimberSpacing = 3f;        // 12개: z −34.5 ~ −1.5
        private static readonly Vector3 SunEuler = new Vector3(18f, 160f, 0f);   // 저각 아침 역광 [TEST]
        private static readonly Vector3 Spawn = new Vector3(0f, 1.1f, -34f);       // 막장, 전방 +Z(입구)
        private static readonly Vector3 ReturnPortalPosition = new Vector3(6f, 0f, 20f);
        private static readonly float[] VeinZ = { -30f, -20f, -11f };
        private static readonly float[] VeinRange = { 6f, 7f, 8f };                // [TEST]
        private static readonly float[] VeinIntensity = { 0.6f, 0.8f, 1.0f };      // 막장→입구 약→강(백색 Point Light)
        private const float VeinHeight = 1.5f;
        private static readonly Vector2 VeinSize = new Vector2(2.4f, 1.2f);
        private const float VeinLightOffset = 0.35f;   // 벽에서 갱도 안쪽으로
        private const char VeinInitial = 'ㄱ';          // 프롤로그 광맥 = 목(#152)
        private const char BeaconInitial = 'ㄴ';        // 봉수 = 화 [TEST]

        // 재질 초기값(Spec §9 [TEST]) — 생성 시 1회. 이후 튜닝은 재질이 들고 코드가 덮지 않는다
        private const float TunnelAmbient = 0.03f;
        private const float RockAmbient = 0.35f;
        private const float WoodTintRetain = 0.12f;

        [MenuItem("Oheangbu/Dev/월드 룩 씬 조립 (C1_WorldLookdev)")]
        public static void Build()
        {
            Debug.Log(BuildScene());
        }

        // reflection-method-call 진입점 — 요약 문자열 반환
        public static string BuildScene()
        {
            if (AssetDatabase.LoadAssetAtPath<ElementPaletteSO>(DevSceneKit.PalettePath) == null) return "FAIL: palette missing " + DevSceneKit.PalettePath;

            // 씬을 먼저 비운다 — 에셋 생성·저장이 재임포트를 부르면 먼저 들어 둔 참조가 「죽은 객체」(fake null)가 되어
            // 씬에 fileID 0으로 박힌다(DevSceneKit.EnsureParryConfig 교훈). 에셋 참조는 전부 NewScene 뒤·에셋 조작 뒤에 든다
            var scene = DevSceneKit.NewEmptyScene();

            var tunnelMat = TunnelMaterial();
            var rockMat = RockMaterial();
            var woodMat = WoodMaterial();
            var veinMat = VeinMaterial();

            var straight = EnsurePrefab(KitFolder, "Mine_Straight6m", () => BuildStraight(tunnelMat));
            var timber = EnsurePrefab(KitFolder, "Mine_Timber", () => BuildTimber(woodMat));
            var rubble = EnsurePrefab(KitFolder, "Mine_Rubble", () => BuildRubble(tunnelMat));
            var portal = EnsurePrefab(KitFolder, "Mine_Portal", () => BuildPortal(woodMat, tunnelMat));
            var veinBand = EnsurePrefab(LookdevFolder, "VeinBand", () => BuildVeinBand(veinMat));
            var palette = AssetDatabase.LoadAssetAtPath<ElementPaletteSO>(DevSceneKit.PalettePath);   // 모든 에셋 조작 뒤

            // 태양 — 백색·Intensity만(#153). 그림자 설정은 RPAsset 무변경
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Color.white;
            sun.intensity = 1f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.SetPositionAndRotation(new Vector3(0f, 20f, 0f), Quaternion.Euler(SunEuler));

            // Volume — #156 집행 후 전역 = VP_InkLook. 씬 Global Volume도 같은 프로필(Bloom만)
            var volumeGo = new GameObject("GlobalVolume_InkLook");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(DevSceneKit.InkLookProfilePath);

            // 갱도 — 6마디 + 막장 캡 + 갱목 12 + 낙석 + 입구 프레임
            var mine = new GameObject("Mine").transform;
            for (int i = 0; i < SegmentCount; i++)
            {
                float z = TunnelEndZ + SegmentLength * (i + 0.5f);
                Place(straight, mine, $"Straight_{i}", new Vector3(0f, 0f, z), Quaternion.identity, scene);
            }
            DevSceneKit.AddCube(mine, "EndCap", new Vector3(0f, TunnelHeight * 0.5f, TunnelEndZ - WallThickness * 0.5f),
                new Vector3(TunnelWidth + WallThickness * 2f, TunnelHeight + WallThickness * 2f, WallThickness), tunnelMat);
            int timberCount = Mathf.RoundToInt(-TunnelEndZ / TimberSpacing);
            for (int k = 0; k < timberCount; k++)
            {
                float z = TunnelEndZ + TimberSpacing * (k + 0.5f);
                Place(timber, mine, $"Timber_{k}", new Vector3(0f, 0f, z), Quaternion.identity, scene);
            }
            Place(rubble, mine, "Rubble", new Vector3(0f, 0f, TunnelEndZ + 0.55f), Quaternion.identity, scene);   // 스폰 캡슐(z −34.5 이후)과 겹치지 않게
            Place(portal, mine, "Portal", Vector3.zero, Quaternion.identity, scene);

            // 광맥 — 우벽(x +1.5) 안쪽 면, 막장→입구 약→강. 범위·세기는 인스턴스 오버라이드(Spec 표)
            var veins = new GameObject("Veins").transform;
            for (int v = 0; v < VeinZ.Length; v++)
            {
                var band = Place(veinBand, veins, $"VeinBand_{v + 1}",
                    new Vector3(TunnelWidth * 0.5f - 0.01f, VeinHeight, VeinZ[v]), Quaternion.LookRotation(Vector3.right), scene);
                var light = band.GetComponentInChildren<Light>();
                light.range = VeinRange[v];
                light.intensity = VeinIntensity[v];
            }

            // 골짜기 스탠드인(P2에서 heightfield로 교체) — z 0~160 · x ±100 평판
            DevSceneKit.AddCube(null, "Valley_Standin", new Vector3(0f, -WallThickness * 0.5f, 80f), new Vector3(200f, WallThickness, 160f), rockMat);

            // 리그 · 귀환 포탈 · 드라이버
            var rig = DevSceneKit.InstantiateRig(scene, Spawn, 0f);
            var camera = rig.GetComponentInChildren<Camera>(true);
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;   // 하늘 = 소지 여백(#150·#154) — 드라이버가 매 활성화마다 다시 쓴다
                camera.backgroundColor = palette.PaperColor;
            }
            DevSceneKit.AddReturnPortal(ReturnPortalPosition, Spawn);

            var driverGo = new GameObject("WorldLook");
            var driver = driverGo.AddComponent<WorldLookDriver>();
            var so = new SerializedObject(driver);
            so.FindProperty("_palette").objectReferenceValue = palette;
            so.FindProperty("_veinInitial").intValue = VeinInitial;
            so.FindProperty("_beaconInitial").intValue = BeaconInitial;
            so.FindProperty("_skyCamera").objectReferenceValue = camera;
            so.ApplyModifiedPropertiesWithoutUndo();
            driver.Apply();

            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            return $"{(saved ? "OK" : "FAIL")}: {ScenePath} segments={SegmentCount} timbers={timberCount} veins={VeinZ.Length} spawn={Spawn}";
        }

        // ---- 재질(무색 수치만 — 색은 _Oh* 전역) ----

        private static Material TunnelMaterial() => DevSceneKit.EnsureMaterial("M_InkWorld_Tunnel", InkWorldShader,
            m => m.SetFloat("_AmbientLevel", TunnelAmbient));

        private static Material RockMaterial() => DevSceneKit.EnsureMaterial("M_InkWorld_Rock", InkWorldShader,
            m => m.SetFloat("_AmbientLevel", RockAmbient));

        private static Material WoodMaterial() => DevSceneKit.EnsureMaterial("M_InkWorld_Wood", InkWorldShader, m =>
        {
            m.SetFloat("_AmbientLevel", TunnelAmbient);
            m.SetFloat("_UseWoodColor", 1f);
            m.SetFloat("_TintRetain", WoodTintRetain);
        });

        private static Material VeinMaterial() => DevSceneKit.EnsureMaterial("M_InkLightSource_Vein", InkLightSourceShader, m =>
        {
            m.SetFloat("_UseVeinColor", 1f);
            m.SetFloat("_Cull", 0f);
        });

        // ---- 프리팹 키트 (LDB:44 키트 원형 — 없을 때만 생성, GUID 유지) ----

        private static GameObject EnsurePrefab(string folder, string name, Func<GameObject> build)
        {
            string path = $"{folder}/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            DevSceneKit.EnsureFolder(folder);
            var temp = build();
            temp.name = name;
            var prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
            Object.DestroyImmediate(temp);
            return prefab;
        }

        private static GameObject Place(GameObject prefab, Transform parent, string name, Vector3 position, Quaternion rotation, Scene scene)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            return go;
        }

        // 6m 마디 — 바닥/천장/좌우 벽 분리 큐브(안쪽 면이 앞면). 피벗 = 마디 중심 z, 바닥 윗면 y 0
        private static GameObject BuildStraight(Material tunnel)
        {
            var root = new GameObject("Mine_Straight6m");
            float hw = TunnelWidth * 0.5f;
            float ht = WallThickness * 0.5f;
            var slab = new Vector3(TunnelWidth + WallThickness * 2f, WallThickness, SegmentLength);
            var wall = new Vector3(WallThickness, TunnelHeight, SegmentLength);
            DevSceneKit.AddCube(root.transform, "Floor", new Vector3(0f, -ht, 0f), slab, tunnel);
            DevSceneKit.AddCube(root.transform, "Ceiling", new Vector3(0f, TunnelHeight + ht, 0f), slab, tunnel);
            DevSceneKit.AddCube(root.transform, "WallL", new Vector3(-(hw + ht), TunnelHeight * 0.5f, 0f), wall, tunnel);
            DevSceneKit.AddCube(root.transform, "WallR", new Vector3(hw + ht, TunnelHeight * 0.5f, 0f), wall, tunnel);
            return root;
        }

        // 갱목 ㄷ자 — 기둥 2 + 들보(갈색 슬롯 재질)
        private static GameObject BuildTimber(Material wood)
        {
            var root = new GameObject("Mine_Timber");
            const float post = 0.25f;
            float x = TunnelWidth * 0.5f - post * 0.5f - 0.05f;
            float postH = TunnelHeight - post;
            DevSceneKit.AddCube(root.transform, "PostL", new Vector3(-x, postH * 0.5f, 0f), new Vector3(post, postH, post), wood);
            DevSceneKit.AddCube(root.transform, "PostR", new Vector3(x, postH * 0.5f, 0f), new Vector3(post, postH, post), wood);
            DevSceneKit.AddCube(root.transform, "Lintel", new Vector3(0f, TunnelHeight - post * 0.5f, 0f), new Vector3(TunnelWidth, post, post), wood);
            return root;
        }

        // 낙석 — 막장 바닥의 덩어리 5개(결정적 배치)
        private static GameObject BuildRubble(Material tunnel)
        {
            var root = new GameObject("Mine_Rubble");
            Vector3[] pos = { new Vector3(-0.9f, 0.3f, 0f), new Vector3(0.6f, 0.25f, -0.3f), new Vector3(0.1f, 0.2f, 0.5f), new Vector3(-0.3f, 0.7f, -0.2f), new Vector3(1.1f, 0.18f, 0.6f) };
            Vector3[] size = { new Vector3(0.9f, 0.6f, 0.8f), new Vector3(0.6f, 0.5f, 0.7f), new Vector3(0.5f, 0.4f, 0.5f), new Vector3(0.5f, 0.35f, 0.45f), new Vector3(0.4f, 0.36f, 0.4f) };
            float[] yaw = { 17f, -32f, 48f, 9f, -21f };
            for (int i = 0; i < pos.Length; i++)
            {
                var cube = DevSceneKit.AddCube(root.transform, $"Rock_{i}", pos[i], size[i], tunnel);
                cube.transform.localRotation = Quaternion.Euler(i * 7f, yaw[i], i * 5f);
            }
            return root;
        }

        // 입구 프레임 — 굵은 기둥 2 + 들보(갈색) + 바위 처마(갱도 재질). z 0
        private static GameObject BuildPortal(Material wood, Material tunnel)
        {
            var root = new GameObject("Mine_Portal");
            const float post = 0.4f;
            float x = TunnelWidth * 0.5f + post * 0.5f;
            float postH = TunnelHeight + post * 0.5f;
            DevSceneKit.AddCube(root.transform, "PostL", new Vector3(-x, postH * 0.5f, 0f), new Vector3(post, postH, post), wood);
            DevSceneKit.AddCube(root.transform, "PostR", new Vector3(x, postH * 0.5f, 0f), new Vector3(post, postH, post), wood);
            DevSceneKit.AddCube(root.transform, "Lintel", new Vector3(0f, postH + post * 0.5f, 0f), new Vector3(TunnelWidth + post * 2f, post, post), wood);
            DevSceneKit.AddCube(root.transform, "Brow", new Vector3(0f, postH + post + 0.6f, -0.3f), new Vector3(TunnelWidth + post * 2f + 1.2f, 1.2f, 1.4f), tunnel);
            return root;
        }

        // 광맥 띠 = 판(Quad, 앞면 = 로컬 −Z) + 백색 Point Light(벽에서 안쪽으로). 루트 회전으로 앞면을 갱도 중심으로 돌린다
        private static GameObject BuildVeinBand(Material vein)
        {
            var root = new GameObject("VeinBand");
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Band";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(root.transform, false);
            quad.transform.localScale = new Vector3(VeinSize.x, VeinSize.y, 1f);
            quad.GetComponent<Renderer>().sharedMaterial = vein;
            quad.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var lightGo = new GameObject("VeinLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0f, -VeinLightOffset);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Color.white;   // 색은 InkWorld가 곱하지 않는다(#153) — 백색 스칼라
            light.intensity = VeinIntensity[VeinIntensity.Length - 1];
            light.range = VeinRange[VeinRange.Length - 1];
            light.shadows = LightShadows.None;
            return root;
        }
    }
}
