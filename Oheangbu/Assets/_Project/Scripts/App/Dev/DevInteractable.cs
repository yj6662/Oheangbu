using System.Text;
using UnityEngine;

namespace Oheangbu.App
{
    // [SPEC-DEV-TEST-HUB §5] 상호작용 대상의 공통 골격: 제목(상시)·설명+힌트(근접 시)·바닥 고리(근접=명도 상승).
    // 근접 판정은 DevInteractor가 한다 — 여기는 표시와 Interact()의 계약만. 발광 없음(명도 차만).
    public abstract class DevInteractable : MonoBehaviour
    {
        [SerializeField] private string _title = "";
        [SerializeField, TextArea(1, 4)] private string[] _description = System.Array.Empty<string>();
        [SerializeField, Min(0.5f)] private float _radius = 2f;

        [Header("라벨 — 제목은 7m(허브 포탈 호)에서, 설명은 2m 근접에서 읽히는 크기")]
        [SerializeField] private float _titleHeight = 2.3f;
        [SerializeField] private float _titleSize = 0.06f;
        [SerializeField] private float _descriptionHeight = 1.9f;
        [SerializeField] private float _descriptionSize = 0.03f;
        [Tooltip("라벨 기준점(로컬) — 앞면(+Z=플레이어 쪽) 가장자리")]
        [SerializeField] private Vector3 _labelOffset = new Vector3(0f, 0f, 0.4f);

        private TextMesh _titleLabel;
        private TextMesh _descriptionLabel;
        private Material _ringMaterial;

        public float Radius => _radius;
        public bool IsNear { get; private set; }
        public string Title => _title;

        protected virtual void Awake()
        {
            _titleLabel = DevLabel.Create(transform, _labelOffset + Vector3.up * _titleHeight, _titleSize,
                TextAnchor.LowerCenter, DevLabel.Paper, "Title");
            _descriptionLabel = DevLabel.Create(transform, _labelOffset + Vector3.up * _descriptionHeight, _descriptionSize,
                TextAnchor.UpperCenter, DevLabel.Ash, "Description");
            _descriptionLabel.gameObject.SetActive(false);
            _ringMaterial = DevLabel.CreateUnlit(DevLabel.Ink);
            DevLabel.CreateRing(transform, "Ring", _radius, 0.05f, _ringMaterial);
            RefreshLabels();
        }

        protected virtual void Start()
        {
            RefreshLabels();
        }

        protected virtual void Update()
        {
            DevLabel.FaceCamera(_titleLabel);
            if (IsNear) DevLabel.FaceCamera(_descriptionLabel);
        }

        protected virtual void OnDestroy()
        {
            if (_ringMaterial != null) Destroy(_ringMaterial);
        }

        public void SetNear(bool near)
        {
            IsNear = near;
            if (_descriptionLabel != null) _descriptionLabel.gameObject.SetActive(near);
            if (_ringMaterial != null) _ringMaterial.color = near ? DevLabel.InkLift : DevLabel.Ink;
            RefreshLabels();
        }

        protected void RefreshLabels()
        {
            if (_titleLabel != null) _titleLabel.text = _title;
            if (_descriptionLabel != null) _descriptionLabel.text = BuildDescription();
        }

        private string BuildDescription()
        {
            var sb = new StringBuilder();
            foreach (var line in _description)
            {
                if (!string.IsNullOrEmpty(line)) sb.Append(line).Append('\n');
            }
            string status = StatusLine;
            if (!string.IsNullOrEmpty(status)) sb.Append(status).Append('\n');
            sb.Append("<color=#F2EDE0>").Append(HintLine).Append("</color>");
            return sb.ToString();
        }

        protected virtual string StatusLine => "";
        protected abstract string HintLine { get; }
        public abstract void Interact();
    }
}
