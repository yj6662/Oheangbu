using System;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.EditorTools
{
    // [SPEC-WORLD-MAP §13-13] 봉인 상수 표식 — 이 어트리뷰트가 붙은 클래스 안의 float 리터럴은 Spec 상수(층 3)로 간주, A9 StatusWords가 명시 출력하며 건너뛴다.
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class SealingConstantsAttribute : Attribute
    {
    }

    // [SPEC-WORLD-MAP §4 층 3 봉인 목록] Terrain 텍스처 기능 봉인(D2) — 레이어·디테일·나무·베이스맵 0, InkTerrain 재질·인스턴싱·픽셀 법선.
    // Seal() = 빌더 강제(§7 ⑥) · Verify() = §6 A7 감사(PASS/FAIL 상세). 봉인 값은 Policy 하나에 이름 붙여 모은다(흩어진 리터럴 0).
    // MCP 봉인(terrain-add-layer/paint-layer/remove-layer/place-trees/set-tree-prototypes/set-detail-prototypes)은 도구 측 규칙 — 여기서는 Verify가 결과를 잡는다.
    // 봉인 상수는 Spec 상수(층 3)이지 튜닝값이 아니다(§13-13) — [SealingConstants] 클래스 안의 리터럴로 두고, A9 StatusWords는 이 클래스를 명시 출력하며 건너뛴다.
    public static class TerrainSealer
    {
        // 봉인 데이터(층 3 표 그대로) — 값 변경 = Spec 개정. A9 리터럴 스캔 제외 대상(무음 제외 금지 — 감사가 "skipped: TerrainSealPolicy (SealingConstants)"를 출력).
        [Serializable]
        [SealingConstants]
        public sealed class TerrainSealPolicy
        {
            public int alphamapResolution = 16;
            public int baseMapResolution = 16;
            public float basemapDistance = 20000f;
            public bool drawInstanced = true;
            public float heightmapPixelError = 5f;
            public bool drawTreesAndFoliage = false;
            public float treeDistance = 0f;
            public bool allowAutoConnect = false;
            public ShadowCastingMode shadowCastingMode = ShadowCastingMode.On;
            public ReflectionProbeUsage reflectionProbeUsage = ReflectionProbeUsage.Off;
            public string perPixelNormalKeyword = "_TERRAIN_INSTANCED_PERPIXEL_NORMAL";
            public string inkTerrainShaderName = "Oheangbu/InkTerrain";
        }

        // 호출마다 새 인스턴스 — 공유 가변 정적 상태 0(CLAUDE.md 아키텍처 규칙).
        public static TerrainSealPolicy Policy => new TerrainSealPolicy();

        // 봉인 강제 — 재실행 안전(멱등). inkTerrain = M_InkTerrain(Oheangbu/InkTerrain). 홀은 여기서 건드리지 않는다(빌더 SetHoles만).
        public static void Seal(Terrain terrain, Material inkTerrain)
        {
            if (terrain == null) throw new ArgumentNullException(nameof(terrain));
            if (inkTerrain == null) throw new ArgumentNullException(nameof(inkTerrain));
            var data = terrain.terrainData;
            if (data == null) throw new InvalidOperationException("Terrain.terrainData 없음");
            var p = Policy;

            data.terrainLayers = new TerrainLayer[0];
            data.detailPrototypes = new DetailPrototype[0];
            data.treePrototypes = new TreePrototype[0];
            data.treeInstances = new TreeInstance[0];
            data.alphamapResolution = p.alphamapResolution;
            data.baseMapResolution = p.baseMapResolution;

            terrain.basemapDistance = p.basemapDistance;
            terrain.materialTemplate = inkTerrain;
            terrain.drawInstanced = p.drawInstanced;
            terrain.heightmapPixelError = p.heightmapPixelError;
            terrain.drawTreesAndFoliage = p.drawTreesAndFoliage;
            terrain.treeDistance = p.treeDistance;
            terrain.allowAutoConnect = p.allowAutoConnect;
            terrain.shadowCastingMode = p.shadowCastingMode;
            terrain.reflectionProbeUsage = p.reflectionProbeUsage;
            terrain.SetNeighbors(null, null, null, null);   // null 안전 명시(예비 타일 미생성 — C11)

            // CustomEditor 부재 → TerrainLitShaderGUI 역할 대행: 인스턴싱일 때만 픽셀 법선 키워드(§10-7 폴백 시 해제)
            if (p.drawInstanced) inkTerrain.EnableKeyword(p.perPixelNormalKeyword);
            else inkTerrain.DisableKeyword(p.perPixelNormalKeyword);
        }

        // §6 A7 — 봉인 전 항 대조. 반환 첫 토큰 PASS/FAIL, 이어서 항목별 상세.
        public static string Verify(Terrain terrain)
        {
            if (terrain == null) return "FAIL: Terrain null";
            var data = terrain.terrainData;
            if (data == null) return "FAIL: terrainData null";
            var p = Policy;
            var sb = new StringBuilder();
            int fails = 0;

            Check(sb, ref fails, "terrainLayers==0", data.terrainLayers == null || data.terrainLayers.Length == 0, data.terrainLayers?.Length.ToString());
            Check(sb, ref fails, "detailPrototypes==0", data.detailPrototypes == null || data.detailPrototypes.Length == 0, data.detailPrototypes?.Length.ToString());
            Check(sb, ref fails, "treePrototypes==0", data.treePrototypes == null || data.treePrototypes.Length == 0, data.treePrototypes?.Length.ToString());
            Check(sb, ref fails, "alphamapResolution", data.alphamapResolution == p.alphamapResolution, data.alphamapResolution.ToString());
            Check(sb, ref fails, "baseMapResolution", data.baseMapResolution == p.baseMapResolution, data.baseMapResolution.ToString());
            Check(sb, ref fails, "basemapDistance", Mathf.Approximately(terrain.basemapDistance, p.basemapDistance), terrain.basemapDistance.ToString());

            var mat = terrain.materialTemplate;
            bool matOk = mat != null && mat.shader != null && mat.shader.name == p.inkTerrainShaderName;
            Check(sb, ref fails, "materialTemplate=" + p.inkTerrainShaderName, matOk, mat != null ? (mat.name + "/" + (mat.shader != null ? mat.shader.name : "null")) : "null");
            Check(sb, ref fails, "drawInstanced", terrain.drawInstanced == p.drawInstanced, terrain.drawInstanced.ToString());
            bool keywordOk = mat != null && (mat.IsKeywordEnabled(p.perPixelNormalKeyword) == terrain.drawInstanced);
            Check(sb, ref fails, p.perPixelNormalKeyword + "==drawInstanced", keywordOk, mat != null ? mat.IsKeywordEnabled(p.perPixelNormalKeyword).ToString() : "null");
            Check(sb, ref fails, "heightmapPixelError", Mathf.Approximately(terrain.heightmapPixelError, p.heightmapPixelError), terrain.heightmapPixelError.ToString());
            Check(sb, ref fails, "drawTreesAndFoliage", terrain.drawTreesAndFoliage == p.drawTreesAndFoliage, terrain.drawTreesAndFoliage.ToString());
            Check(sb, ref fails, "treeDistance", Mathf.Approximately(terrain.treeDistance, p.treeDistance), terrain.treeDistance.ToString());
            Check(sb, ref fails, "allowAutoConnect", terrain.allowAutoConnect == p.allowAutoConnect, terrain.allowAutoConnect.ToString());
            Check(sb, ref fails, "shadowCastingMode", terrain.shadowCastingMode == p.shadowCastingMode, terrain.shadowCastingMode.ToString());
            Check(sb, ref fails, "reflectionProbeUsage", terrain.reflectionProbeUsage == p.reflectionProbeUsage, terrain.reflectionProbeUsage.ToString());
            // 텍스처 참조 0(엔진 내부 3종은 재질 프로퍼티가 아니라 Terrain이 주입 — 재질 슬롯에는 아무것도 없어야 한다)
            Check(sb, ref fails, "material textures==0", mat == null || CountTextures(mat) == 0, mat != null ? CountTextures(mat).ToString() : "null");
            // §10-5 가드 — 품질 오버라이드가 켜지면 pixelError 봉인이 죽은 값이 된다
            Check(sb, ref fails, "QualitySettings.terrainQualityOverrides==None", QualitySettings.terrainQualityOverrides == TerrainQualityOverrides.None, QualitySettings.terrainQualityOverrides.ToString());

            return (fails == 0 ? "PASS" : "FAIL(" + fails + ")") + " TerrainSealer.Verify " + terrain.name + sb;
        }

        private static void Check(StringBuilder sb, ref int fails, string label, bool ok, string actual)
        {
            if (!ok) fails++;
            sb.Append(" · ").Append(ok ? "ok " : "FAIL ").Append(label);
            if (!ok) sb.Append("=").Append(actual ?? "null");
        }

        private static int CountTextures(Material mat)
        {
            int n = 0;
            var shader = mat.shader;
            if (shader == null) return 0;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                if (mat.GetTexture(shader.GetPropertyNameId(i)) != null) n++;
            }
            return n;
        }
    }
}
