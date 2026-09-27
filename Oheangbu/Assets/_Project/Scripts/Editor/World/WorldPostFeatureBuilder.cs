using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // [SPEC-SPIKE-WORLD-LOOKDEV §5-3·§8-P2·§10-2] InkWorldPost 전체화면 패스를 PC_Renderer에 전역 등록/해제.
    // FSPRF(URP 내장 FullScreenPassRendererFeature) — 커스텀 RenderFeature 코드 0줄. 주입점 450 BeforeRenderingTransparents.
    // 전역 게이트: _OhWorldPost < 0.5 이면 셰이더가 원본 반환(A5 NaN 항등) — 드라이버 없는 씬 픽셀 불변.
    // reflection-method-call 진입점(전부 public static string). ScriptableRendererData의 subasset 규약(feature list + map + AddObjectToAsset)을
    // SerializedObject로 다룬다 — URP 인스펙터 「Add Renderer Feature」와 같은 경로. 재실행 = 이름으로 기존 피처 찾아 덮어씀(중복 0).
    public static class WorldPostFeatureBuilder
    {
        public const string RendererPath = "Assets/Settings/PC_Renderer.asset";
        public const string PostShader = "Oheangbu/InkWorldPost";
        public const string PostMaterialName = "M_InkWorldPost";
        public const string FeatureName = "InkWorldPost";

        // §5-3 초기 수치 [TEST] — 재질 생성 시 1회. 이후 튜닝은 재질이 들고 코드가 덮지 않는다.
        // 거리 규약(§5-11): 페이드 90/420·strength 0.9·keep 0.55·법선 엣지 감쇠 140m.
        private static void ConfigureMaterial(Material m)
        {
            m.SetFloat("_PaperFadeStart", 90f);
            m.SetFloat("_PaperFadeEnd", 420f);
            m.SetFloat("_WashStrength", 0.9f);
            m.SetFloat("_WashKeep", 0.55f);
            m.SetFloat("_NormalEdgeFadeDistance", 140f);
        }

        public static string Register()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
            if (rendererData == null) return "FAIL: renderer 없음 " + RendererPath;

            var material = DevSceneKit.EnsureMaterial(PostMaterialName, PostShader, ConfigureMaterial);
            if (material == null) return "FAIL: 재질 생성 실패 " + PostMaterialName;

            var so = new SerializedObject(rendererData);
            var listProp = so.FindProperty("m_RendererFeatures");
            var mapProp = so.FindProperty("m_RendererFeatureMap");

            // 이미 있으면 그 인스턴스 재사용(재실행 안전)
            FullScreenPassRendererFeature feature = FindExisting(rendererData);
            bool created = false;
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                feature.name = FeatureName;
                AssetDatabase.AddObjectToAsset(feature, rendererData);
                created = true;

                so.Update();
                int idx = listProp.arraySize;
                listProp.InsertArrayElementAtIndex(idx);
                listProp.GetArrayElementAtIndex(idx).objectReferenceValue = feature;
                // m_RendererFeatureMap = 각 피처의 localFileId를 이어붙인 키(URP 규약). 피처 추가 시 localId를 map에 추가.
                mapProp.arraySize = listProp.arraySize;
                var localIdProp = mapProp.GetArrayElementAtIndex(idx);
                localIdProp.longValue = GetLocalId(feature);
                so.ApplyModifiedProperties();
            }

            // 설정(§5-3) — 재실행마다 계약값을 보증(재질 파라미터가 아니라 피처 배선이라 코드가 든다)
            feature.name = FeatureName;
            feature.passMaterial = material;
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingTransparents; // 450
            feature.fetchColorBuffer = true;                                   // §5-3 fetchColorBuffer 1
            feature.requirements = ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal; // §5-3
            feature.passIndex = 0;
            feature.SetActive(true);
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();

            return $"OK: {FeatureName} {(created ? "생성" : "갱신")} · mat={PostMaterialName} · 주입점=BeforeRenderingTransparents(450) · fetchColor=1 · req=Depth|Normal · features={listProp.arraySize}";
        }

        public static string Unregister()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
            if (rendererData == null) return "FAIL: renderer 없음 " + RendererPath;
            var feature = FindExisting(rendererData);
            if (feature == null) return "OK: 이미 없음";

            var so = new SerializedObject(rendererData);
            var listProp = so.FindProperty("m_RendererFeatures");
            var mapProp = so.FindProperty("m_RendererFeatureMap");
            for (int i = listProp.arraySize - 1; i >= 0; i--)
            {
                if (listProp.GetArrayElementAtIndex(i).objectReferenceValue == feature)
                {
                    listProp.GetArrayElementAtIndex(i).objectReferenceValue = null;
                    listProp.DeleteArrayElementAtIndex(i);
                    if (i < mapProp.arraySize) mapProp.DeleteArrayElementAtIndex(i);
                }
            }
            so.ApplyModifiedProperties();
            AssetDatabase.RemoveObjectFromAsset(feature);
            Object.DestroyImmediate(feature, true);
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
            return "OK: " + FeatureName + " 제거 · features=" + listProp.arraySize;
        }

        public static string Status()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
            if (rendererData == null) return "FAIL: renderer 없음";
            var feature = FindExisting(rendererData);
            if (feature == null) return "ABSENT: InkWorldPost 미등록 · features=" + rendererData.rendererFeatures.Count;
            return $"PRESENT: active={feature.isActive} · mat={(feature.passMaterial != null ? feature.passMaterial.name : "null")} · 주입점={feature.injectionPoint} · fetchColor={feature.fetchColorBuffer} · req={feature.requirements}";
        }

        private static FullScreenPassRendererFeature FindExisting(ScriptableRendererData data)
        {
            foreach (var f in data.rendererFeatures)
                if (f is FullScreenPassRendererFeature fs && f.name == FeatureName) return fs;
            return null;
        }

        private static long GetLocalId(Object obj)
        {
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out _, out long localId)) return localId;
            return 0;
        }
    }
}
