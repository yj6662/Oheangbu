using Oheangbu.Combat;
using Oheangbu.Drawing;
using Oheangbu.Presentation;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>읽기 전용 게임 상태를 표시 골격에 전달한다. 작도 인식이나 이동 판정에 쓰지 않는다.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed class PlayerVisualDriver : MonoBehaviour
    {
        [SerializeField] private PlayerVisualRig _rig;
        [SerializeField] private CharacterController _controller;
        [SerializeField] private PlayerMotor _motor;
        [SerializeField] private DodgeAction _dodge;
        [SerializeField] private LockOn _lockOn;
        [SerializeField] private DrawingInputController _input;
        [SerializeField] private CameraRigController _cameraRig;
        [SerializeField] private BrushStrokeFeedAdapter _brushFeed;
        private PlayerVisualFrame _frame;

        public PlayerVisualDiagnostics Diagnostics => _rig != null ? _rig.Diagnostics : default;

        public void Configure(PlayerVisualRig rig, CharacterController controller, PlayerMotor motor,
            DodgeAction dodge, LockOn lockOn, DrawingInputController input, CameraRigController cameraRig,
            BrushStrokeFeedAdapter brushFeed = null)
        {
            _rig = rig; _controller = controller; _motor = motor; _dodge = dodge;
            _lockOn = lockOn; _input = input; _cameraRig = cameraRig; _brushFeed = brushFeed;
        }

        private void Update()
        {
            if (_rig == null) return;
            Transform root = _controller != null ? _controller.transform : transform;
            Vector3 velocity = _controller != null ? _controller.velocity : Vector3.zero;
            Vector3 local = root.InverseTransformDirection(velocity);
            Vector3 dodgeDirection = _dodge != null ? root.InverseTransformDirection(_dodge.Direction) : Vector3.zero;
            bool drawing = _motor != null && _motor.IsDrawing;
            _cameraRig?.SetDrawingPresentationActive(drawing);
            _frame = new PlayerVisualFrame
            {
                LocalVelocity = new Vector3(local.x, 0f, local.z),
                Grounded = _controller != null && _controller.isGrounded,
                Combat = (_lockOn != null && _lockOn.IsLocked) || drawing,
                Drawing = drawing,
                Stroking = _input != null && _input.IsStroking,
                Dodging = _dodge != null && _dodge.IsDashing,
                DodgeDirection = new Vector2(dodgeDirection.x, dodgeDirection.z),
                DodgeProgress = _dodge != null ? _dodge.Progress : 1f,
                HasPointer = _input != null && _input.HasPointer,
                PointerScreen = _input != null ? _input.PointerScreenPosition : Vector2.zero,
                WorldBodyVisible = _cameraRig != null && _cameraRig.IsBodyVisible,
                Camera = Camera.main
            };
            _rig.UpdateMotion(_frame);
        }

        private void LateUpdate()
        {
            if (_rig == null) return;
            // 먹선 어댑터(-1000)와 리본(0)이 평가한 동일 최종 카메라·획 끝을 읽는다.
            if (_brushFeed != null && _brushFeed.TryGetVisualPointer(out Camera cam, out Vector2 screen, out Vector3 ink))
            {
                _frame.Camera = cam;
                _frame.PointerScreen = screen;
                _frame.InkWorldPoint = ink;
                _frame.HasPointer = true;
            }
            _frame.WorldBodyVisible = _cameraRig != null && _cameraRig.IsBodyVisible;
            _rig.ApplyFrame(_frame);
        }

        private void OnDisable()
        {
            _cameraRig?.SetDrawingPresentationActive(false);
            _rig?.ReleasePresentation();
        }
    }
}
