using System.Collections.Generic;
using Oheangbu.Drawing;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools
{
    // 자모 템플릿 라이브러리 채우기 — _Project/Data/JamoTemplates의 정본 XML을 스캔해
    // JamoTemplateLibrarySO의 TextAsset 배열에 배선한다. 62개 수동 드래그를 없애는 도구.
    // (SpellDiagramCsvImporter 계보 — 데이터 정본은 폴더의 XML, SO는 참조 다발일 뿐)
    public static class JamoTemplateLibraryFiller
    {
        private const string InitialsFolder = "Assets/_Project/Data/JamoTemplates/Initials";
        private const string MedialsFolder = "Assets/_Project/Data/JamoTemplates/Medials";

        [MenuItem("Oheangbu/Drawing/자모 템플릿 라이브러리 채우기")]
        public static void FillAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:JamoTemplateLibrarySO");
            if (guids.Length == 0)
            {
                Debug.LogWarning("[Drawing] JamoTemplateLibrarySO 에셋이 없음 — 먼저 생성하세요");
                return;
            }
            foreach (string guid in guids)
            {
                var library = AssetDatabase.LoadAssetAtPath<JamoTemplateLibrarySO>(AssetDatabase.GUIDToAssetPath(guid));
                library.EditorSetTemplates(LoadXmlAssets(InitialsFolder), LoadXmlAssets(MedialsFolder));
                EditorUtility.SetDirty(library);
                Debug.Log($"[Drawing] {library.name} 채움 — 초성 {library.InitialCount} · 중성 {library.MedialCount}");
            }
            AssetDatabase.SaveAssets();
        }

        private static TextAsset[] LoadXmlAssets(string folder)
        {
            var list = new List<TextAsset>();
            foreach (string guid in AssetDatabase.FindAssets("t:TextAsset", new[] { folder }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) list.Add(asset);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list.ToArray();
        }
    }
}
