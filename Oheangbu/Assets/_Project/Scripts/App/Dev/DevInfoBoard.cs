using UnityEngine;

namespace Oheangbu.App
{
    // [SPEC-DEV-TEST-HUB §5] 고정 월드 텍스트 판 — 안내판·정답표·판정판·어휘판의 본체. 빌더가 판 루트를
    // 플레이어(시작점) 쪽으로 돌려 세우고, 글은 앞면(+Z)에서 읽히도록 뒤집어 붙인다
    public sealed class DevInfoBoard : MonoBehaviour
    {
        [SerializeField, TextArea(3, 20)] private string _text = "";
        [SerializeField] private float _characterSize = 0.04f;
        [SerializeField] private TextAnchor _anchor = TextAnchor.MiddleCenter;
        [Tooltip("글 위치(로컬) — 판 두께(0.15) 앞")]
        [SerializeField] private Vector3 _localOffset = new Vector3(0f, 0.9f, 0.09f);

        private TextMesh _label;

        private void Awake()
        {
            _label = DevLabel.Create(transform, _localOffset, _characterSize, _anchor, DevLabel.Paper, "Text");
            _label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // +Z 쪽(플레이어)에서 읽힘
            _label.text = _text;
        }

        public void SetText(string text)
        {
            _text = text;
            if (_label != null) _label.text = text;
        }
    }
}
