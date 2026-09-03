using System;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // [사격장 하네스 — SPEC-DEV-SPELL-RANGE] 과녁 더미: EnemyVitals의 피격을 눈에 보이게 한다
    // (피격 플래시 + 머리 위 라벨: 캡션·명중 수·누적 피해). 판정에는 불개입 — 읽기만 한다.
    // 적 AI(EnemyController)는 붙이지 않는다 — 과녁은 반격하지 않는다. 씬 전용·빌드 무관.
    public sealed class SpellRangeDummy : MonoBehaviour
    {
        private const float FlashDuration = 0.15f;

        [SerializeField] private EnemyVitals _vitals;
        [SerializeField] private Renderer _renderer;
        [Tooltip("누적 피해 환산용(Hp01 × MaxHp) — 과녁 전용 설정(사실상 불사)")]
        [SerializeField] private CombatConfigSO _config;
        [Tooltip("라벨 캡션 — 디렉터가 있으면 각·거리·IN/OUT으로 덮어쓴다")]
        [SerializeField] private string _caption = "";

        private TextMesh _label;
        private Color _baseColor;
        private float _flashUntil;
        private float _lastHp01 = 1f;
        private int _hits;
        private float _damageTotal;

        public event Action<SpellRangeDummy, float> Hit; // (과녁, 이번 피해량)
        public EnemyVitals Vitals => _vitals;
        public string Caption => _caption;

        private void Awake()
        {
            if (_renderer != null) _baseColor = _renderer.material.color;
            _label = DevLabel.Create(transform, Vector3.up * 1.5f, 0.045f, TextAnchor.LowerCenter, DevLabel.Paper);
        }

        private void Start()
        {
            _lastHp01 = _vitals != null ? _vitals.Hp01 : 1f; // EnemyVitals.Awake 이후의 첫 값
            RefreshLabel();
        }

        private void OnEnable()
        {
            if (_vitals != null) _vitals.HpChanged += OnHpChanged;
        }

        private void OnDisable()
        {
            if (_vitals != null) _vitals.HpChanged -= OnHpChanged;
        }

        public void SetCaption(string caption)
        {
            _caption = caption;
            RefreshLabel();
        }

        private void OnHpChanged()
        {
            float hp01 = _vitals.Hp01;
            float maxHp = _config != null ? _config.EnemyMaxHp : 0f;
            float damage = Mathf.Max(0f, (_lastHp01 - hp01) * maxHp);
            _lastHp01 = hp01;
            _hits++;
            _damageTotal += damage;
            _flashUntil = Time.time + FlashDuration;
            RefreshLabel();
            Hit?.Invoke(this, damage);
        }

        private void Update()
        {
            if (_renderer != null)
            {
                if (Time.time < _flashUntil)
                {
                    _renderer.material.color = Color.white;
                }
                else if (_flashUntil > 0f)
                {
                    _renderer.material.color = _baseColor;
                    _flashUntil = 0f;
                }
            }
            DevLabel.FaceCamera(_label);
        }

        private void RefreshLabel()
        {
            if (_label == null) return;
            string state = _vitals != null && !_vitals.IsAlive ? " X" : "";
            _label.text = $"{_caption}{state}\n명중 {_hits} · 피해 {_damageTotal:0.#}";
        }
    }
}
