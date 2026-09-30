using Oheangbu.Core;
using Oheangbu.Core.Events;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Oheangbu.Combat
{
    // 1인칭 이동·시점 + Gameplay 입력 허브 (SPEC-COMBAT-CORE-LOOP 결정 3 — WASD·마우스·점프 없음).
    // 작도 중(_modeChanged=true)에는 시점 회전·회피를 멈추고 커서를 푼다 — 마우스가 붓이 되는 동안.
    // 이동은 유지한다(§10.1 [제안]). 액션맵 이름·바인딩은 데이터(.inputactions) — 코드는 이름만 안다.
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(-100)]
    public sealed partial class PlayerMotor : MonoBehaviour
    {
        private const string MapName = "Gameplay";

        [SerializeField] private InputActionAsset _actions;
        [SerializeField] private CombatConfigSO _config;
        [SerializeField] private Transform _cameraPivot;           // 피치 회전 담당(카메라 부모)
        [SerializeField] private BoolEventChannelSO _drawModeChanged;
        [SerializeField] private DodgeAction _dodge;
        [SerializeField] private HarvestAction _harvest;
        [SerializeField] private LockOn _lockOn;
        [SerializeField] private CameraRigController _cameraRig; // [실험 2026-08-27] 숄더뷰 피치 클램프 조회(없으면 기본 ±80)
        public GameplayRuntimeStateSO RuntimeState;
        public float TerrainMovementScale {get;set;}=1f;
        public bool EnvironmentalInputBlocked {get;set;}

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
        // 메뉴·상호작용·환경 막힘 — 먹 자연 회복 등 배선부가 읽는다(D306)
        public bool InputBlocked => EnvironmentalInputBlocked || RuntimeState != null && RuntimeState.InputBlocked;
        public void ResetMotion(){_verticalVelocity=0;_pitch=0;ResetLocomotion();}

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            var map = _actions != null ? _actions.FindActionMap(MapName, throwIfNotFound: true) : null;
            _move = map?.FindAction("Move", true);
            _look = map?.FindAction("Look", true);
            _dodgeAction = map?.FindAction("Dodge", true);
            _harvestAction = map?.FindAction("Harvest", true);
            _lockOnAction = map?.FindAction("LockOn", true);
            InitializeLocomotion();
        }

        private void OnEnable()
        {
            _actions?.FindActionMap(MapName)?.Enable();
            if (_dodgeAction != null) _dodgeAction.performed += OnDodge;
            if (_lockOnAction != null) _lockOnAction.performed += OnLockOn;
            if (_drawModeChanged != null) _drawModeChanged.Subscribe(OnDrawModeChanged);
            SetCursorLocked(true);
            EnableLocomotion();
        }

        private void OnDisable()
        {
            _harvest?.Cancel(HarvestCancelReason.Disabled); // 모터가 꺼지면 막힘 검사도 멈춘다 — 뽑기가 홀로 끝나지 않게
            DisableLocomotion();
            if (_dodgeAction != null) _dodgeAction.performed -= OnDodge;
            if (_lockOnAction != null) _lockOnAction.performed -= OnLockOn;
            if (_drawModeChanged != null) _drawModeChanged.Unsubscribe(OnDrawModeChanged);
            _actions?.FindActionMap(MapName)?.Disable();
            SetCursorLocked(false);
        }

        private void Update()
        {
            if (_config == null) return;
            if (InputBlocked) { _harvest?.Cancel(HarvestCancelReason.InputBlocked); SuspendLocomotionInput(); return; }
            if (HasLocomotion && Time.deltaTime <= 0f) return;

            float yawBeforeInput = transform.eulerAngles.y;
            // 시점 — 작도 중에는 마우스를 붓에게 양보한다(마우스 look만 차단)
            if (!_drawing && _look != null)
            {
                float preference = RuntimeState != null ? RuntimeState.LookSensitivity : 1f;
                Vector2 look = _look.ReadValue<Vector2>() * (_config.LookSensitivity * preference);
                transform.Rotate(0f, look.x, 0f);
                float vertical = RuntimeState != null && RuntimeState.InvertLookY ? -look.y : look.y;
                _pitch = ClampPitch(_pitch - vertical);
            }

            // 락온 소프트 당김(엘든링식): 카메라가 대상 쪽으로 은은히 끌리되 고정하지 않는다.
            // 작도 중에도 락온이면 계속 돈다(2차 카메라 검수) — 결투 계약은 붓을 들어도 유지된다.
            // 회전해도 작도는 안 깨진다: 작도면·획이 카메라-로컬이라 화면상 글자는 불변이다.
            ApplyLockOnPull();
            if (HasLocomotion && Time.deltaTime > 0) _actualYawSpeed = Mathf.DeltaAngle(yawBeforeInput, transform.eulerAngles.y) / Time.deltaTime;

            if (_cameraPivot != null) _cameraPivot.localEulerAngles = new Vector3(_pitch, 0f, 0f);

            // 갈무리 = 좌클릭 한 번 덩어리 뽑기(D306 [TEST]) — 누름 프레임에 시작, 뽑기·끊기·냉각은 HarvestAction이 소유.
            // 작도 중엔 마우스가 붓이므로 무효. 뽑는 시간은 scaled — 감속 중엔 뽑기도 함께 느려진다
            if (!_drawing && CanHarvestNow && _harvestAction != null && _harvestAction.WasPressedThisFrame())
            {
                _harvest?.TryBeginPull();
            }

            // 이동 — 감속 중에도 scaled deltaTime을 그대로 쓴다(세상이 함께 느려진다)
            Vector2 input = _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;
            if (HasLocomotion) { UpdateLocomotion(input); return; }
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
            _pitch = ClampPitch(Mathf.LerpAngle(_pitch, targetPitch, blend));
        }

        // 숄더뷰는 카메라 오프셋 궤도가 지면을 관통하므로 클램프가 좁아진다 — 한계는 리그가 소유
        private float ClampPitch(float pitch)
        {
            float min = -80f, max = 80f;
            if (_cameraRig != null) _cameraRig.GetPitchLimits(out min, out max);
            return Mathf.Clamp(pitch, min, max);
        }

        private void OnDodge(InputAction.CallbackContext _)
        {
            if (EnvironmentalInputBlocked || RuntimeState != null && RuntimeState.InputBlocked) return;
            if (_drawing) return; // 작도 중 회피 없음 — 작도는 무방비의 시간이다(감속이 그 대가)
            // 뽑는 중 회피 = 뽑기 취소(수입 0, D306) — 회피가 막히지 않는다
            if (HasLocomotion && (!ActionAllowed || !IsLocomotionGrounded || IsSitting || IsDodging
                || (IsCrouching && !_locomotion.CrouchRollEnabled))) return;
            Vector2 input = _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;
            Vector3 direction = input.sqrMagnitude > 0.01f
                ? (transform.right * input.x + transform.forward * input.y).normalized
                : transform.forward;
            bool roll = HasLocomotion && IsCrouching && _locomotion.CrouchRollEnabled;
            if (_dodge != null && _dodge.TryDodge(direction, roll ? _locomotion.CrouchRollSeconds : 0f))
            { _rolling = roll; _harvest?.Cancel(HarvestCancelReason.Dodge); }
        }

        private void OnLockOn(InputAction.CallbackContext _)
        {
            if (EnvironmentalInputBlocked || RuntimeState != null && RuntimeState.InputBlocked) return;
            _lockOn?.Toggle();
        }

        private void OnDrawModeChanged(bool drawing)
        {
            _drawing = drawing;
            if (drawing) _harvest?.Cancel(HarvestCancelReason.Drawing);
            SetCursorLocked(!drawing);
        }

        private void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
