using System;
using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.EditorTools
{
    // [SPEC-WORLD-MAP §4 층 8] 원경 세트 — 구역 중심 기준 능선 링 ridgeLayerCount겹(반경·앙각·노이즈 = WorldScaleSO) 폴리라인 압출 실루엣 +
    // 신목 실루엣 앵커. 콜라이더 0 · castShadows Off · LODGroup 0 · 360° 폐합 · 압출 하단 Y=0(바닥 −100은 부모 오프셋 — 슬랩 띠 기하 불가).
    // 신목 노치: 신목 방위 ±sinmokNotchDeg에서 L2~L4 높이 × sinmokNotchScale. 겹별 1D 노이즈 시드 = RealmSheetSO.farSetSeed + 겹(§6 A11).
    // 링 상단 함수 RingTopY는 메시와 감사(RidgeStacking·HorizonClosure — 해석적, 콜라이더 0)가 같은 식을 쓴다.
    // 로컬 좌표계: 루트 = 구역(첫 타일) 중심 XZ · Y 0 = 압출 하단. 링 상단 Y = baseY(골짜기 바닥 절대 고도) + h_layer × 노이즈 × 노치.
    // 수치 출처: 형상·높이 = RealmSheetSO 원경 정책 필드(§6 A9 리터럴 0) · 이 파일의 상수는 정수 해상도(분할 수·옥타브)뿐.
    // fake-null 규율(run-20260906 §MCP ②): 시트 값 읽기 → 메시 전부 생성 → 에셋 저장 → 경로로 재로드 → 계층 생성. 재질은 AssignMaterials로 뒤에 단다.
    public static class FarSetBuilder
    {
        public const string MeshFolder = "Assets/_Project/Scenes/World/Meshes";
        public const string RootName = "FarSet";
        public const string SinmokName = "Sinmok_Silhouette";
        public const string RidgeNamePrefix = "Ridge_L";
        public const string RidgeLayerName = "WorldRidge";
        public const string AnchorLayerName = "WorldAnchor";

        // 정수 해상도 상수(형상값 아님 — 형상·높이는 RealmSheetSO).
        private const int SegmentsPerRing = 360;          // 링 둘레 분할 수
        private const int NoiseCyclesPerRing = 12;        // 링 1D 노이즈 기본 주기 수(둘레당 봉우리 수)
        private const int NoiseOctaves = 3;               // 링 1D 노이즈 옥타브
        private const int NotchFromLayer = 1;             // 노치를 적용하는 첫 겹 인덱스(0 = L1) — Spec = L2~L4
        private const int SinmokProfilePoints = 9;        // 수관 프로필 분할 수
        private const float SinmokCrownWidthRatio = 0.5f; // 수관 폭 / 높이 — 화이트리스트 값(양면 대칭 실루엣의 기본 비)

        // ---- 진입점 ----

        // 기하만 만든다(재질 0) — 호출자가 모든 에셋 생성을 마친 뒤 AssignMaterials로 재질을 단다.
        public static GameObject BuildFarSet(RealmSheetSO realm, Transform parent)
        {
            if (realm == null) throw new ArgumentNullException(nameof(realm));
            var scale = realm.WorldScale;
            if (scale == null) throw new InvalidOperationException("RealmSheetSO.worldScale 없음");

            // ① 시트 값 읽기 + 메시 전부 생성(에셋 저장 전 — 참조 사망 창 밖)
            Vector2 center = AreaCenterXZ(realm);
            float baseY = BaseY(realm);
            Vector2 sinmokLocal = realm.SinmokPosition - center;
            float[] radii = scale.RidgeRadii;
            int ringCount = radii.Length;
            var meshes = new Mesh[ringCount + 1];
            var paths = new string[ringCount + 1];
            for (int layer = 0; layer < ringCount; layer++)
            {
                meshes[layer] = BuildRingMesh(realm, scale, layer, baseY);
                paths[layer] = $"{MeshFolder}/FarSet_Ridge_L{layer + 1}.asset";
            }
            meshes[ringCount] = BuildSinmokMesh(realm);
            paths[ringCount] = $"{MeshFolder}/FarSet_Sinmok.asset";

            // ② 에셋 저장 → ③ 경로로 재로드(저장이 재임포트를 불러도 씬에 박히는 참조는 산 객체)
            DevSceneKit.EnsureFolder(MeshFolder);
            for (int i = 0; i < meshes.Length; i++)
            {
                meshes[i] = SaveMeshAsset(meshes[i], paths[i]);
            }
            for (int i = 0; i < meshes.Length; i++)
            {
                var reloaded = AssetDatabase.LoadAssetAtPath<Mesh>(paths[i]);
                if (reloaded != null) meshes[i] = reloaded;
            }

            // ④ 계층 생성
            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(center.x, 0f, center.y);
            int ridgeLayer = LayerMask.NameToLayer(RidgeLayerName);
            int anchorLayer = LayerMask.NameToLayer(AnchorLayerName);
            for (int layer = 0; layer < ringCount; layer++)
            {
                AddSilhouette(root.transform, RidgeNamePrefix + (layer + 1), meshes[layer], ridgeLayer);
            }
            var sinmok = AddSilhouette(root.transform, SinmokName, meshes[ringCount], anchorLayer);
            sinmok.transform.localPosition = new Vector3(sinmokLocal.x, baseY, sinmokLocal.y);
            return root;
        }

        // 재질 배선 — 호출자가 에셋 생성을 전부 마치고 경로로 재로드한 재질만 넘긴다. 반환 = 비어 있는 슬롯 이름(경고용).
        public static List<string> AssignMaterials(GameObject root, Material[] ridgeMaterials, Material anchorMaterial)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var missing = new List<string>();
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material material = null;
                string name = renderer.gameObject.name;
                if (name == SinmokName)
                {
                    material = anchorMaterial;
                }
                else if (name.StartsWith(RidgeNamePrefix, StringComparison.Ordinal) && ridgeMaterials != null && ridgeMaterials.Length > 0)
                {
                    int layer;
                    if (int.TryParse(name.Substring(RidgeNamePrefix.Length), out layer))
                    {
                        material = ridgeMaterials[Math.Min(Math.Max(0, layer - 1), ridgeMaterials.Length - 1)];
                    }
                }
                renderer.sharedMaterial = material;
                if (material == null) missing.Add(name + ".sharedMaterial");
            }
            return missing;
        }

        // ---- 해석 함수(감사 공용) ----

        // 구역 중심(첫 구역 첫 타일 중심) — 타일 좌표 XZ.
        public static Vector2 AreaCenterXZ(RealmSheetSO realm)
        {
            var area = FirstArea(realm);
            if (area == null || area.Tiles == null || area.Tiles.Length == 0) return Vector2.zero;
            var tile = area.Tiles[0];
            return tile.originXZ + new Vector2(tile.sizeM, tile.sizeM) * 0.5f;
        }

        // 링 기준 Y = 골짜기 바닥 절대 고도(TerrainSynthSO.valleyFloorY) — 앙각 기준점(구역 중심 눈높이)의 지면.
        public static float BaseY(RealmSheetSO realm)
        {
            var area = FirstArea(realm);
            return area != null && area.Synth != null ? area.Synth.ValleyFloorY : 0f;
        }

        // 신목 방위 °(북 0° 시계) — 구역 중심 기준.
        public static float SinmokBearingDeg(RealmSheetSO realm)
        {
            Vector2 d = realm.SinmokPosition - AreaCenterXZ(realm);
            return BearingDeg(d.x, d.y);
        }

        // 링 상단 Y(루트 로컬) — 노이즈·노치 포함. 메시 정점과 감사가 같은 식을 평가한다.
        public static float RingTopY(RealmSheetSO realm, WorldScaleSO scale, int layer, float bearingDeg, float baseY)
        {
            float[] heights = scale.RidgeHeights;
            if (layer < 0 || layer >= heights.Length) return baseY;
            float u = Wrap01(bearingDeg / (Mathf.PI * 2f * Mathf.Rad2Deg));
            float noise = Noise1D(u, realm.FarSetSeed + layer);
            float h = heights[layer] * (1f + (noise * 2f - 1f) * scale.RidgeNoiseAmp);
            h *= NotchScale(scale, layer, bearingDeg, SinmokBearingDeg(realm));
            return baseY + Math.Max(0f, h);
        }

        // 공칭 링 상단 앙각(노이즈·노치·baseY 없음) — fromPos = 루트 로컬 XZ(중심 원점) · fromPos.y = baseY(골짜기 바닥) 기준 눈높이, yaw = 북 0° 시계.
        public static float RingTopElevationDeg(WorldScaleSO scale, int layer, Vector3 fromPos, float yawDeg)
        {
            float[] radii = scale.RidgeRadii;
            float[] heights = scale.RidgeHeights;
            if (layer < 0 || layer >= radii.Length) return 0f;
            float dist;
            float bearing;
            if (!RayHitsRing(fromPos, yawDeg, radii[layer], out dist, out bearing)) return 0f;
            return ElevationDeg(heights[layer] - fromPos.y, dist);
        }

        // 실측 링 상단 앙각(노이즈·노치 포함 · baseY 포함) — RidgeStacking·HorizonClosure 입력. fromPos.y = 눈 절대 고도(루트 로컬 = 절대, Y 오프셋 0 가정).
        public static float RingTopElevationDeg(RealmSheetSO realm, WorldScaleSO scale, int layer, Vector3 fromPos, float yawDeg)
        {
            float[] radii = scale.RidgeRadii;
            if (layer < 0 || layer >= radii.Length) return 0f;
            float dist;
            float bearing;
            if (!RayHitsRing(fromPos, yawDeg, radii[layer], out dist, out bearing)) return 0f;
            float top = RingTopY(realm, scale, layer, bearing, BaseY(realm));
            return ElevationDeg(top - fromPos.y, dist);
        }

        // ---- 메시 ----

        private static Mesh BuildRingMesh(RealmSheetSO realm, WorldScaleSO scale, int layer, float baseY)
        {
            int n = SegmentsPerRing;
            float radius = scale.RidgeRadii[layer];
            var vertices = new Vector3[n * 2];
            var normals = new Vector3[n * 2];
            var triangles = new List<int>(n * 6);
            for (int i = 0; i < n; i++)
            {
                float bearing = (float)i / n * Mathf.PI * 2f * Mathf.Rad2Deg;
                Vector2 dir = Direction(bearing);
                float top = RingTopY(realm, scale, layer, bearing, baseY);
                vertices[i * 2] = new Vector3(dir.x * radius, 0f, dir.y * radius);
                vertices[i * 2 + 1] = new Vector3(dir.x * radius, top, dir.y * radius);
                var inward = new Vector3(-dir.x, 0f, -dir.y);
                normals[i * 2] = inward;
                normals[i * 2 + 1] = inward;
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                AddQuad(triangles, vertices, i * 2, i * 2 + 1, j * 2 + 1, j * 2, normals[i * 2]);
            }
            var mesh = new Mesh { name = $"FarSet_Ridge_L{layer + 1}" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        // 신목 = 대칭 프로필(줄기 → 수관 sin 곡선)을 두 수직면(0°·90°)에 양면으로 — 「전역 가지 실루엣」 자리표(예준 Blender 교체 슬롯).
        // 높이·줄기 비·수관 시작 비 = RealmSheetSO(§4 층 2 원경 정책 필드).
        private static Mesh BuildSinmokMesh(RealmSheetSO realm)
        {
            float h = realm.SinmokHeightM;
            float trunkHalf = h * realm.SinmokTrunkRatio * 0.5f;
            float crownHalf = h * SinmokCrownWidthRatio * 0.5f;
            float crownStart = h * realm.SinmokCrownStartRatio;
            int crownPoints = SinmokProfilePoints;

            // 프로필 (반폭, y)
            var profile = new List<Vector2> { new Vector2(trunkHalf, 0f), new Vector2(trunkHalf, crownStart) };
            for (int k = 0; k <= crownPoints; k++)
            {
                float t = (float)k / crownPoints;
                float y = crownStart + (h - crownStart) * t;
                float half = Math.Max(trunkHalf * (1f - t), crownHalf * (float)Math.Sin(t * Mathf.PI));
                profile.Add(new Vector2(half, y));
            }

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            Vector2[] planeDirs = { Vector2.right, Vector2.up };   // XZ 방향: 0° 면 = X축, 90° 면 = Z축
            foreach (var d in planeDirs)
            {
                var planeNormal = new Vector3(-d.y, 0f, d.x);
                // 양면 — 면마다 정점을 따로 두어 셰이딩 법선 = 그 면의 앞 방향(링과 같은 규칙).
                // 두 면이 한 법선을 공유하면 태양 반대편에서 본 면까지 역광 정면 조명을 받아 소지색으로 뜬다(2026-09-07 G1 실측: 신목이 하늘에 묻힘).
                foreach (var side in new[] { planeNormal, -planeNormal })
                {
                    int baseIndex = vertices.Count;
                    for (int i = 0; i < profile.Count; i++)
                    {
                        vertices.Add(new Vector3(-d.x * profile[i].x, profile[i].y, -d.y * profile[i].x));
                        vertices.Add(new Vector3(d.x * profile[i].x, profile[i].y, d.y * profile[i].x));
                        normals.Add(side);
                        normals.Add(side);
                    }
                    var verts = vertices.ToArray();
                    for (int i = 0; i + 1 < profile.Count; i++)
                    {
                        int a = baseIndex + i * 2;
                        AddQuad(triangles, verts, a, a + 2, a + 3, a + 1, side);
                    }
                }
            }
            var mesh = new Mesh { name = "FarSet_Sinmok" };
            mesh.vertices = vertices.ToArray();
            mesh.normals = normals.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        // 사각형 2삼각형 — 기하 법선이 facing과 같은 쪽을 보도록 감김을 고른다(앞면 = 링 안쪽).
        // Unity(왼손·시계 방향 앞면)에서 (a,b,c) 순서의 앞면 법선 = cross(b−a, c−a) — 내장 Quad(0,1,2 → (0,0,−1))로 확인.
        // cross가 facing과 반대면 뒤집는다. (2026-09-07 G1 실측: 판정이 반대라 링 앞면이 바깥을 향해 안에서 전부 컬링됐다.)
        private static void AddQuad(List<int> tris, Vector3[] v, int a, int b, int c, int d, Vector3 facing)
        {
            Vector3 geo = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            bool flip = Vector3.Dot(geo, facing) < 0f;
            if (flip)
            {
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(a); tris.Add(d); tris.Add(c);
            }
            else
            {
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(a); tris.Add(c); tris.Add(d);
            }
        }

        private static GameObject AddSilhouette(Transform parent, string name, Mesh mesh, int layer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            if (layer >= 0) go.layer = layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = null;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        // 메시 에셋 — 있으면 내용만 덮어 GUID 유지(씬 참조 안정), 없으면 생성.
        public static Mesh SaveMeshAsset(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            existing.Clear();
            existing.vertices = mesh.vertices;
            existing.normals = mesh.normals;
            existing.triangles = mesh.triangles;
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            return existing;
        }

        // ---- 보조 ----

        private static AreaSheetSO FirstArea(RealmSheetSO realm)
        {
            var areas = realm.Areas;
            if (areas == null) return null;
            foreach (var a in areas)
            {
                if (a != null) return a;
            }
            return null;
        }

        // 신목 노치 배율 — |Δ방위| ≤ notchDeg에서 smoothstep으로 notchScale까지(중심 = 완전 노치).
        private static float NotchScale(WorldScaleSO scale, int layer, float bearingDeg, float sinmokBearingDeg)
        {
            if (layer < NotchFromLayer || scale.SinmokNotchDeg <= 0f) return 1f;
            float delta = Math.Abs(DeltaAngleDeg(bearingDeg, sinmokBearingDeg));
            if (delta >= scale.SinmokNotchDeg) return 1f;
            float t = delta / scale.SinmokNotchDeg;
            float s = t * t * (3 - 2f * t);
            return scale.SinmokNotchScale + (1f - scale.SinmokNotchScale) * s;
        }

        // 둘레 파라미터 u∈[0,1) 1D 값 노이즈 [0,1] — 격자 인덱스를 주기로 감아 이음새 연속. 시드 → System.Random 순열.
        private static float Noise1D(float u, int seed)
        {
            var rng = new System.Random(seed);
            const int permSize = 256;
            var perm = new int[permSize];
            for (int i = 0; i < permSize; i++) perm[i] = i;
            for (int i = permSize - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int tmp = perm[i];
                perm[i] = perm[j];
                perm[j] = tmp;
            }
            float sum = 0f;
            float norm = 0f;
            float amp = 1f;
            for (int o = 0; o < NoiseOctaves; o++)
            {
                int period = NoiseCyclesPerRing << o;
                float x = u * period;
                int i0 = (int)Math.Floor(x);
                float f = x - i0;
                float s = f * f * (3 - 2f * f);
                int a = perm[(i0 % period + period + o) % permSize];
                int b = perm[((i0 + 1) % period + period + o) % permSize];
                sum += ((float)a / permSize + ((float)b / permSize - (float)a / permSize) * s) * amp;
                norm += amp;
                amp *= 0.5f;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        // 방위 → XZ 단위 방향(북 0° 시계: 북 = +Z, 동 = +X)
        public static Vector2 Direction(float bearingDeg)
        {
            float r = bearingDeg * Mathf.Deg2Rad;
            return new Vector2((float)Math.Sin(r), (float)Math.Cos(r));
        }

        public static float BearingDeg(float dx, float dz)
        {
            return Wrap360((float)Math.Atan2(dx, dz) * Mathf.Rad2Deg);
        }

        private static bool RayHitsRing(Vector3 fromPos, float yawDeg, float radius, out float dist, out float bearingDeg)
        {
            Vector2 p = new Vector2(fromPos.x, fromPos.z);
            Vector2 d = Direction(yawDeg);
            // |p + t d|² = r² → t² + 2(p·d)t + (|p|² − r²) = 0
            float b = Vector2.Dot(p, d);
            float c = Vector2.Dot(p, p) - radius * radius;
            float disc = b * b - c;
            dist = 0f;
            bearingDeg = 0f;
            if (disc < 0f) return false;
            float t = -b + (float)Math.Sqrt(disc);
            if (t <= 0f) return false;
            dist = t;
            Vector2 hit = p + d * t;
            bearingDeg = BearingDeg(hit.x, hit.y);
            return true;
        }

        private static float ElevationDeg(float rise, float run)
        {
            return (float)Math.Atan2(rise, Math.Max(Mathf.Epsilon, run)) * Mathf.Rad2Deg;
        }

        private static float Wrap360(float deg)
        {
            float full = Mathf.PI * 2f * Mathf.Rad2Deg;
            deg %= full;
            return deg < 0f ? deg + full : deg;
        }

        private static float Wrap01(float u)
        {
            u %= 1f;
            return u < 0f ? u + 1f : u;
        }

        private static float DeltaAngleDeg(float a, float b)
        {
            float full = Mathf.PI * 2f * Mathf.Rad2Deg;
            float d = Wrap360(b - a);
            return d > full * 0.5f ? d - full : d;
        }
    }
}
