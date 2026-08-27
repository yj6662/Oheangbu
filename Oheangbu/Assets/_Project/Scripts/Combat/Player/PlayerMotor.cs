using Oheangbu.Core.Events;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Oheangbu.Combat
{
    // 1인칭 이동·시점 + Gameplay 입력 허브 (SPEC-COMBAT-CORE-LOOP 결정 3 — WASD·마우스·점프 없음).
    // 작도 중(_modeChanged=true)에는 시점 회전·회피를 멈추고 커서를 푼다 — 마우스가 붓이 되는 동안.
    // 이동은 유지한다(§10.1 [제안]). 액션맵 이름·바인딩은 데이터(.inputactions) — 코드는 이름만 안다.
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        private const string MapName = "Gameplay";

        [SerializeField] private InputActionAsset _actions;
        [SerializeField] private CombatConfigSO _config;
        [SerializeField] private Transform _cameraPivot;           // 피치 회전 담당(카메라 부모)
        [SerializeField] private BoolEventChannelSO _drawModeChanged;
        [SerializeField] private DodgeAction _dodge;
        [SerializeField] private HarvestAction _harvest;
        [SerializeField] private LockOn _lockOn;

        private CharacterController _controller;
        private InputAction _move;
        private InputAction _look;
        private InputAction _dodgeAction;
        private InputAction _harvestAction;
        private InputAction _lockOnAction;
        private float _pitch;
        private float _verticalVelocity;
        private bool _drawing;

        public bool IsDrawing => _drawing;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            var map = _actions != null ? _actions.FindActionMap(MapName, throwIfNotFound: true) : null;
            _move = map?.FindAction("Move", true);
            _look = map?.FindAction("Look", true);
            _dodgeAction = map?.FindAction("Dodge", true);
            _harvestAction = map?.FindAction("Harvest", true);
            _lockOnAction = map?.FindAction("LockOn", true);
        }

        private void OnEnable()
        {
            _actions?.FindActionMap(MapName)?.Enable();
            if (_dodgeAction != null) _dodgeAction.performed += OnDodge;
            if (_harvestAction != null) _harvestAction.performed += OnHarvest;
            if (_lockOnAction != null) _lockOnAction.performed += OnLockOn;
            if (_drawModeChanged != null) _drawModeChanged.Subscribe(OnDrawModeChanged);
            SetCursorLocked(true);
        }

        private void OnDisable()
        {
            if (_dodgeAction != null) _dodgeAction.performed -= OnDodge;
            if (_harvestAction != null) _harvestAction.performed -= OnHarvest;
            if (_lockOnAction != null) _lockOnAction.performed -= OnLockOn;
            if (_drawModeChanged != null) _drawModeChanged.Unsubscribe(OnDrawModeChanged);
            _actions?.FindActionMap(MapName)?.Disable();
            SetCursorLocked(false);
        }

        private void Update()
        {
            if (_config == null) return;

            // 시점 — 작도 중에는 마우스를 붓에게 양보한다
            if (!_drawing && _look != null)
            {
                Vector2 look = _look.ReadValue<Vector2>() * _config.LookSensitivity;
                transform.Rotate(0f, look.x, 0f);
                _pitch = Mathf.Clamp(_pitch - look.y, -80f, 80f);

                // 락온 소프트 당김(예준 검수 — 엘든링식): 카메라가 대상 쪽으로 은은히 끌리되
                // 고정하지 않는다 — 마우스 입력이 항상 위에 얹히므로 언제든 시선을 뺄 수 있다
                ApplyLockOnPull();

                if (_cameraPivot != null) _cameraPivot.localEulerAngles = new Vector3(_pitch, 0f, 0f);
            }

            // 이동 — 감속 중에도 scaled deltaTime을 그대로 쓴다(세상이 함께 느려진다)
            Vector2 input = _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;
            Vector3 planar = (transform.right * input.x + transform.forward * input.y) * _config.MoveSpeed;
            if (_dodge != null && _dodge.TryGetDashVelocity(out Vector3 dash)) planar = dash;

            _verticalVelocity = _controller.isGrounded ? -1f : _verticalVelocity + _config.Gravity * Time.deltaTime;
            Vector3 velocity = planar + Vector3.up * _verticalVelocity;
            _controller.Move(velocity * Time.deltaTime);
        }

        private void ApplyLockOnPull()
        {
            float pull = _config.LockOnCameraPull;
            if (pull <= 0f || _lockOn == null || _lockOn.Target == null || _cameraPivot == null) return;

            Vector3 to = _lockOn.Target.transform.position + Vector3.up * 1.1f - _cameraPivot.position;
            if (to.sqrMagnitude < 0.01f) return;

            // 프레임률 무관한 지수 수렴 — 목표를 향해 끌리는 「자기력」의 세기가 pull
            float blend = 1f - Mathf.Exp(-pull * Time.unscaledDeltaTime);

            Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (flat.sqrMagnitude > 0.001f)
            {
                float targetYaw = Quaternion.LookRotation(flat).eulerAngles.y;
                float yaw = Mathf.LerpAngle(transform.eulerAngles.y, targetYaw, blend);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }

            float targetPitch = -Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            _pitch = Mathf.Clamp(Mathf.LerpAngle(_pitch, targetPitch, blend), -80f, 80f);
        }

        private void OnDodge(InputAction.CallbackContext _)
        {
            if (_drawing) return; // 작도 중 회피 없음 — 작도는 무방비의 시간이다(감속이 그 대가)
            Vector2 input = _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;
            Vector3 direction = input.sqrMagnitude > 0.01f
                ? (transform.right * input.x + transform.forward * input.y).normalized
                : transform.forward;
            _dodge?.TryDodge(direction);
        }

        private void OnHarvest(InputAction.CallbackContext _)
        {
            if (_drawing) return;
            _harvest?.TryHarvest();
        }

        private void OnLockOn(InputAction.CallbackContext _)
        {
            _lockOn?.Toggle();
        }

        private void OnDrawModeChanged(bool drawing)
        {
            _drawing = drawing;
            SetCursorLocked(!drawing);
        }

        private void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
