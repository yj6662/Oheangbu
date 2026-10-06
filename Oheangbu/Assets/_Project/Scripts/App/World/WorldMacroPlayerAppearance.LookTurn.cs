using UnityEngine;

namespace Oheangbu.App.World
{
    public sealed partial class WorldMacroPlayerAppearance
    {
        private bool _lookInitialized, _turning, _lookApplied;
        private float _bodyYaw, _turnFrom, _turnTo, _turnElapsed, _turnDuration, _headYaw;
        private Transform _lookHead, _lookNeck;
        private Quaternion _headAnimated, _neckAnimated;
        public float LookYaw => _headYaw;
        public float BodyYaw => _bodyYaw;
        public bool TurningInPlace => _turning;
        public int TurnSerial { get; private set; }

        private void UpdateLookTurn(bool moving)
        {
            if (!_profile.IdleLookTurn || _animator.transform == _motor.transform) return;
            float yaw = _motor.transform.eulerAngles.y;
            if (!_lookInitialized) { _bodyYaw = yaw; _lookInitialized = true; _turning = false; }
            if (Time.deltaTime <= 0f) return;
            bool idle = !moving && _motor.IsLocomotionGrounded && !_motor.IsDrawing && !_motor.IsHarvesting && !_motor.IsSitting && !_motor.IsDodging;
            // #308 D308-8c (SPEC-VEHICLE-UX-308 §3b): the car call stroke is drawn in front of the controller (the call direction), so
            // while it runs the body turns to face it at the stroke's data speed instead of keeping a standing look-around yaw. The
            // head keeps looking along the controller while the body turns under it (no head snap). Presentation only.
            float strokeTurn = _gestureRig != null ? _gestureRig.AirStrokeBodyTurnSpeed308 : 0f;
            if (strokeTurn > 0f && !_motor.IsDodging && !_motor.IsDrawing)
            {
                _turning = false;
                _bodyYaw = Mathf.MoveTowardsAngle(_bodyYaw, yaw, strokeTurn * Time.deltaTime);
                _headYaw = Mathf.Clamp(Mathf.DeltaAngle(_bodyYaw, yaw), -90f, 90f);
            }
            else if (!idle)
            {
                _turning = false;
                float destination = yaw;
                if (_motor.IsRolling)
                {
                    var d = _motor.transform.TransformDirection(_motor.DodgeLocalDirection);
                    destination = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                }
                _bodyYaw = _motor.IsDodging || _motor.IsDrawing ? destination
                    : Mathf.MoveTowardsAngle(_bodyYaw, destination, 360f * Time.deltaTime);
                _headYaw = 0f;
            }
            else
            {
                float difference = Mathf.DeltaAngle(_bodyYaw, yaw);
                // Hysteresis comes from finishing a latched 90-degree step; no yaw-speed retrigger.
                if (!_turning && Mathf.Abs(difference) > _profile.IdleLookDegrees)
                {
                    _turnFrom = _bodyYaw; _turnTo = _bodyYaw + Mathf.Sign(difference) * 90f;
                    var clip = difference > 0 ? _profile.TurnRight : _profile.TurnLeft;
                    _turnDuration = _motor.IsCrouching ? .7f : Mathf.Max(.25f, clip != null ? clip.length : .7f);
                    _turnElapsed = 0f; _turning = true; TurnSerial++;
                    _animator.ResetTrigger("TurnLeft"); _animator.ResetTrigger("TurnRight");
                    _animator.SetTrigger(difference > 0 ? "TurnRight" : "TurnLeft");
                }
                if (_turning)
                {
                    _turnElapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(_turnElapsed / _turnDuration);
                    _bodyYaw = Mathf.Lerp(_turnFrom, _turnTo, Mathf.SmoothStep(0f, 1f, t));
                    if (t >= 1f) _turning = false;
                }
                _headYaw = Mathf.Clamp(Mathf.DeltaAngle(_bodyYaw, yaw), -90f, 90f);
            }
            // Camera and motor stay responsive; only the visual child compensates root yaw.
            _animator.transform.rotation = Quaternion.Euler(0f, _bodyYaw + _profile.FacingYaw, 0f);
            _animator.SetFloat("TurnProgress", _turning ? Mathf.Clamp01(_turnElapsed / _turnDuration) : 1f);
            _animator.SetBool("Turning", _turning);
        }

        private void RestoreLookBones()
        {
            if (!_lookApplied) return;
            if (_lookHead != null) _lookHead.localRotation = _headAnimated;
            if (_lookNeck != null) _lookNeck.localRotation = _neckAnimated;
            _lookApplied = false;
        }

        private void ApplyLookBones()
        {
            if (_profile == null || !_profile.IdleLookTurn || _animator == null) return;
            if (_lookHead == null || _lookNeck == null)
                foreach (var t in _animator.GetComponentsInChildren<Transform>(true))
                { if (t.name == "Head") _lookHead = t; if (t.name == "neck" || t.name == "Neck") _lookNeck = t; }
            if (_lookHead == null || _lookNeck == null) return;
            _headAnimated = _lookHead.localRotation; _neckAnimated = _lookNeck.localRotation;
            _lookNeck.rotation = Quaternion.AngleAxis(_headYaw * .35f, Vector3.up) * _lookNeck.rotation;
            _lookHead.rotation = Quaternion.AngleAxis(_headYaw * .65f, Vector3.up) * _lookHead.rotation;
            _lookApplied = true;
        }
    }
}
