using UnityEngine;

namespace Oheangbu.App
{
    // [개발 하네스 공용 — SPEC-DEV-TEST-HUB] 월드 텍스트·바닥 고리·무광 재질. 게임 HUD 무접촉(헌법 화이트리스트) —
    // 하네스 안내는 전부 세계 안의 텍스트다. 발광 없음: 근접 표시는 명도 차만 쓴다(ART-INK 광원 3등급).
    public static class DevLabel
    {
        public static readonly Color Paper = new Color(0.95f, 0.93f, 0.88f);   // 제목·힌트
        public static readonly Color Ash = new Color(0.84f, 0.81f, 0.76f);     // 설명 본문
        public static readonly Color Ink = new Color(0.16f, 0.15f, 0.13f);     // 바닥 선·먼 고리
        public static readonly Color InkLift = new Color(0.55f, 0.52f, 0.48f); // 근접 고리(명도만 상승)

        public static TextMesh Create(Transform parent, Vector3 localPosition, float characterSize,
            TextAnchor anchor, Color color, string name = "Label")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var text = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                text.font = font;
                go.GetComponent<MeshRenderer>().material = font.material;
            }
            text.fontSize = 48;
            text.characterSize = characterSize;
            text.anchor = anchor;
            text.alignment = TextAlignment.Center;
            text.richText = true;
            text.color = color;
            return text;
        }

        public static void FaceCamera(TextMesh label)
        {
            var cam = Camera.main;
            if (label == null || cam == null) return;
            label.transform.rotation = Quaternion.LookRotation(label.transform.position - cam.transform.position);
        }

        public static Material CreateUnlit(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var material = new Material(shader);
            material.color = color;
            return material;
        }

        // 부모 로컬 공간의 수평 고리(부모가 지면 높이라는 전제 — height는 그 위 오프셋)
        public static LineRenderer CreateRing(Transform parent, string name, float radius, float width, Material material,
            float height = 0.03f, int segments = 24)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.widthMultiplier = width;
            line.material = material;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, height, Mathf.Sin(a) * radius));
            }
            return line;
        }
    }
}
