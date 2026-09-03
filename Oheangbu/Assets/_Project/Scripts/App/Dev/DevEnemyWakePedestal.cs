using System;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // [SPEC-DEV-TEST-HUB §2] 선택대 — 실적 AI를 깨우고 재운다(EnemyController.enabled 토글 — 잠든 적은 결투 밖).
    // 시작 상태는 이 컴포넌트가 정한다(_awakeAtStart). 디렉터는 Toggled를 구독해 「한 번에 하나」를 유지한다
    public sealed class DevEnemyWakePedestal : DevInteractable
    {
        [SerializeField] private EnemyController _enemy;
        [SerializeField] private bool _awakeAtStart;

        public event Action<DevEnemyWakePedestal, bool> Toggled; // (선택대, 깨어남)
        public EnemyController Enemy => _enemy;
        public bool IsAwake => _enemy != null && _enemy.enabled;

        protected override void Start()
        {
            if (_enemy != null) _enemy.enabled = _awakeAtStart;
            base.Start();
        }

        public void Wake()
        {
            Set(true);
        }

        public void Sleep()
        {
            Set(false);
        }

        public override void Interact()
        {
            if (IsAwake) Sleep();
            else Wake();
        }

        private void Set(bool awake)
        {
            if (_enemy == null) return;
            if (_enemy.enabled == awake)
            {
                RefreshLabels();
                return;
            }
            _enemy.enabled = awake;
            RefreshLabels();
            Debug.Log($"[Hub] {name} — 실적 {(awake ? "활동" : "잠듦")}");
            Toggled?.Invoke(this, awake);
        }

        protected override string StatusLine => IsAwake ? "활동 중" : "잠듦";
        protected override string HintLine => IsAwake ? "F: 재우기" : "F: 깨우기";
    }
}
