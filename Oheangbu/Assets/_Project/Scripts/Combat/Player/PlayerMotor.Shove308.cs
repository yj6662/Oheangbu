using System;
using UnityEngine;

namespace Oheangbu.Combat
{
    // #308 D308-129 (토론 17, TEST): an outside force on the player. The first is the 적로 불승냥이 charge: the App asks it from
    // the attacker's profile after a real hit. The motor only moves the body; who may shove and when belongs to the App.
    // The path is cut before it starts, so the body never leaves walkable ground: a sample every ShoveSample308 m along the push
    // looks down for support (the ground layers and slope limit of ProbeGround, a rise or drop the slope allows between two
    // samples) and the push stops ShoveMargin308 m before the first that fails. A side collision or a dodge ends it.
    // ShoveBlocked308 (숨) is asked at the start and on every step. Constant deceleration, no rebound, horizontal only; walking
    // input is ignored while it runs (as during a dash). A new shove cannot start within ShoveLockSeconds308 of the last.
    public sealed partial class PlayerMotor
    {
        public const float ShoveSample308 = .25f, ShoveMargin308 = .6f, ShoveLockSeconds308 = .8f;
        const float ShoveFirstDrop308 = 7f;   // the support under a body in the air may be this far down (the fall rule kills at 6 m)

        Vector3 _shoveDir308;
        float _shoveDistance308, _shoveSeconds308, _shoveAge308 = float.PositiveInfinity, _shoveLock308;

        // true = the push may not move the player now (the App's 숨). Null = never blocked.
        public Func<bool> ShoveBlocked308 { get; set; }
        public bool IsShoved308 => _shoveAge308 < _shoveSeconds308;
        public int ShoveSerial308 { get; private set; }
        public float ShovePlanned308 => _shoveDistance308;   // the distance after the cut

        // false = nothing moves: no locomotion, a bad request, the lock, a dash, blocked, or no walkable ground ahead.
        public bool Shove308(Vector3 direction, float distance, float seconds)
        {
            if (!HasLocomotion || _controller == null || !_controller.enabled) return false;
            if (!float.IsFinite(distance) || !float.IsFinite(seconds) || distance <= 0f || seconds <= 0f) return false;
            Vector3 dir = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (!(dir.sqrMagnitude > 1e-6f) || _shoveLock308 > 0f || IsDodging) return false;
            if (ShoveBlocked308 != null && ShoveBlocked308()) return false;
            _shoveLock308 = ShoveLockSeconds308;   // the hit spends the lock even when the ground cuts the push to nothing
            dir.Normalize();
            float allowed = ShovePathLength308(dir, distance);
            if (allowed < .01f) { CancelShove308(); return false; }
            // the same deceleration on a shorter path: a slower start, still stopping smoothly at the cut
            _shoveDir308 = dir; _shoveDistance308 = allowed; _shoveSeconds308 = seconds * Mathf.Sqrt(allowed / distance);
            _shoveAge308 = 0f; ShoveSerial308++;
            return true;
        }

        public void CancelShove308() { _shoveAge308 = float.PositiveInfinity; _shoveDistance308 = _shoveSeconds308 = 0f; }

        // StepLocomotion's planar velocity this step: the push replaces walking (a dash replaces the push).
        Vector3 StepShove308(Vector3 planar, float dt)
        {
            _shoveLock308 = Mathf.Max(0f, _shoveLock308 - dt);
            if (!IsShoved308) return planar;
            if (IsDodging || (ShoveBlocked308 != null && ShoveBlocked308())) { CancelShove308(); return planar; }
            float from = ShoveCovered308(_shoveAge308);
            _shoveAge308 += dt;
            return _shoveDir308 * ((ShoveCovered308(_shoveAge308) - from) / dt);
        }
        float ShoveCovered308(float age) { float u = 1f - Mathf.Clamp01(age / _shoveSeconds308); return _shoveDistance308 * (1f - u * u); }

        // How far the push may go along dir, up to distance (see the class note).
        float ShovePathLength308(Vector3 dir, float distance)
        {
            Vector3 feet = Feet;
            if (!ShoveSupport308(feet, feet.y, ShoveFirstDrop308, out float previous)) return 0f;
            float rise = ShoveSample308 * Mathf.Tan(Mathf.Clamp(_controller.slopeLimit, 0f, 89f) * Mathf.Deg2Rad) + .05f;
            for (float s = ShoveSample308; s <= distance + ShoveMargin308 + 1e-4f; s += ShoveSample308)
            {
                if (!ShoveSupport308(feet + dir * s, previous, rise + 1f, out float height) || Mathf.Abs(height - previous) > rise)
                    return Mathf.Clamp(s - ShoveMargin308, 0f, distance);
                previous = height;
            }
            return distance;
        }

        // The highest walkable support under point, looked for from 1 m above reference down to drop below that.
        bool ShoveSupport308(Vector3 point, float reference, float drop, out float height)
        {
            height = reference;
            Vector3 from = new Vector3(point.x, reference + 1f, point.z);
            int count = Physics.RaycastNonAlloc(from, Vector3.down, _groundHits, 1f + drop, _locomotion.GroundLayers, QueryTriggerInteraction.Ignore);
            if (count == _groundHits.Length) return false;
            float normal = Mathf.Cos(_controller.slopeLimit * Mathf.Deg2Rad), nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var h = _groundHits[i];
                if (h.collider == null || h.transform == transform || h.transform.IsChildOf(transform) || h.distance >= nearest) continue;
                nearest = h.distance; height = h.point.y;
                if (h.normal.y < normal) height = float.NaN;   // the first surface met is too steep to stand on
            }
            return float.IsFinite(nearest) && float.IsFinite(height);
        }
    }
}
