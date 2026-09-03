using System;
using Oheangbu.Core.Events;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Oheangbu.App
{
    // [SPEC-DEV-TEST-HUB §4] 씬당 1개 — 플레이어와 가장 가까운 상호작용 대상(반경 안)을 고르고 F/R을 읽는다.
    // Keyboard.current 직결은 개발 씬 예외(§9-1)의 유일한 지점. 작도 중(Q 홀드) F는 무효 — 마우스가 붓인 동안.
    public sealed class DevInteractor : MonoBehaviour
    {
        [SerializeField] private Transform _player;
        [SerializeField] private DevInteractable[] _interactables = Array.Empty<DevInteractable>();
        [SerializeField] private BoolEventChannelSO _drawModeChanged;
        [Tooltip("이탈 판정 여유(m) — 반경 경계에서 설명이 깜빡이지 않게")]
        [SerializeField, Min(0f)] private float _exitHysteresis = 0.3f;

        private DevInteractable _focused;
        private bool _drawing;

        private void OnEnable()
        {
            if (_drawModeChanged != null) _drawModeChanged.Subscribe(OnDrawModeChanged);
        }

        private void OnDisable()
        {
            if (_drawModeChanged != null) _drawModeChanged.Unsubscribe(OnDrawModeChanged);
        }

        private void Start()
        {
            Debug.Log($"[Hub] {SceneManager.GetActiveScene().name} — F: 상호작용 · R: 재시작 · 상호작용 대상 {_interactables.Length}개");
        }

        private void Update()
        {
            UpdateFocus();

            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.rKey.wasPressedThisFrame)
            {
                DevSceneFlow.Restart();
                return;
            }
            if (kb.fKey.wasPressedThisFrame && !_drawing && _focused != null) _focused.Interact();
        }

        private void UpdateFocus()
        {
            if (_player == null) return;
            DevInteractable best = null;
            float bestDistance = float.MaxValue;
            foreach (var interactable in _interactables)
            {
                if (interactable == null) continue;
                Vector3 to = interactable.transform.position - _player.position;
                to.y = 0f;
                float distance = to.magnitude;
                float radius = interactable.Radius + (interactable == _focused ? _exitHysteresis : 0f);
                if (distance <= radius && distance < bestDistance)
                {
                    best = interactable;
                    bestDistance = distance;
                }
            }
            if (best == _focused) return;
            if (_focused != null) _focused.SetNear(false);
            _focused = best;
            if (_focused != null) _focused.SetNear(true);
        }

        private void OnDrawModeChanged(bool drawing)
        {
            _drawing = drawing;
        }
    }
}
