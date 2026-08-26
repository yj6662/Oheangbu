using System.Collections.Generic;
using PDollarGestureRecognizer;
using UnityEngine;

namespace Oheangbu.Drawing
{
    // 자모 템플릿(실측 통과 XML — _Project/Data/JamoTemplates)의 참조 라이브러리.
    // 파일 경로 IO 대신 TextAsset 참조를 쓰는 이유: 빌드에서도 동작해야 하고(dataPath는 에디터 전용),
    // 정본 데이터가 무엇인지 에셋 참조로 명시되기 때문. 채우기는 에디터 도구가 폴더를 스캔해 수행한다.
    [CreateAssetMenu(menuName = "Oheangbu/Drawing/Jamo Template Library", fileName = "JamoTemplateLibrary")]
    public sealed class JamoTemplateLibrarySO : ScriptableObject
    {
        // 종성은 별도 템플릿 없이 초성에서 걸러 재사용한다(레거시 검증 방식 — 자음 형태는 동일, $P는 위치·크기 불변)
        private static readonly string[] FinalCandidates = { "ㄱ", "ㄴ", "ㅁ", "ㅅ", "ㅇ" };

        [SerializeField] private TextAsset[] _initials;
        [SerializeField] private TextAsset[] _medials;

        public int InitialCount => _initials != null ? _initials.Length : 0;
        public int MedialCount => _medials != null ? _medials.Length : 0;

        public Gesture[] BuildInitialGestures() => Build(_initials);
        public Gesture[] BuildMedialGestures() => Build(_medials);

        public Gesture[] BuildFinalGestures()
        {
            var finals = new List<Gesture>();
            foreach (var gesture in Build(_initials))
            {
                if (System.Array.IndexOf(FinalCandidates, gesture.Name) >= 0) finals.Add(gesture);
            }
            return finals.ToArray();
        }

        private static Gesture[] Build(TextAsset[] sources)
        {
            if (sources == null) return new Gesture[0];
            var list = new List<Gesture>(sources.Length);
            foreach (var asset in sources)
            {
                if (asset == null) continue;
                list.Add(GestureIO.ReadGestureFromXML(asset.text));
            }
            return list.ToArray();
        }

#if UNITY_EDITOR
        // 에디터 채우기 도구(EditorTools)가 쓰는 통로 — 런타임 코드는 호출하지 않는다
        public void EditorSetTemplates(TextAsset[] initials, TextAsset[] medials)
        {
            _initials = initials;
            _medials = medials;
        }
#endif
    }
}
