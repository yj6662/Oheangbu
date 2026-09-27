using System;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    public readonly struct CheongryongTailPhase
    {
        public readonly float AngleDegrees, Weight;
        public CheongryongTailPhase(float angle, float weight) { AngleDegrees = angle; Weight = weight; }
    }

    public static class CheongryongTailPoseMath
    {
        public static CheongryongTailPhase Phase(float now, float start, float release, float activeEnd, float end, float halfAngle, bool cancelled = false)
        {
            if (cancelled || !float.IsFinite(now) || !float.IsFinite(start) || !float.IsFinite(release) || !float.IsFinite(activeEnd) ||
                !float.IsFinite(end) || !float.IsFinite(halfAngle) || release <= start || activeEnd <= release || end < activeEnd || now < start || now >= end)
                return new CheongryongTailPhase(0, 0);
            halfAngle = Mathf.Clamp(halfAngle, 0, 180);
            if (now < release) return new CheongryongTailPhase(-halfAngle, Mathf.SmoothStep(0, 1, (now - start) / (release - start)));
            if (now <= activeEnd) return new CheongryongTailPhase(Mathf.Lerp(-halfAngle, halfAngle, (now - release) / (activeEnd - release)), 1);
            return new CheongryongTailPhase(halfAngle, 1 - Mathf.SmoothStep(0, 1, (now - activeEnd) / Mathf.Max(.00001f, end - activeEnd)));
        }

        /// <summary>Builds a curved yaw sweep; every segment keeps its baseline length and vertical delta.</summary>
        public static bool Solve(Vector3[] baseline, Vector3 lockedDirection, float angle, float weight, Vector3[] output, Vector3[] directions)
        {
            if (baseline == null || baseline.Length < 3 || baseline.Length > 129 || output == null || output.Length < baseline.Length ||
                directions == null || directions.Length < baseline.Length - 1 || !float.IsFinite(angle) || !float.IsFinite(weight) ||
                !CheongryongArcLengthHistory.Finite(lockedDirection)) return false;
            for (int i = 0; i < baseline.Length; i++) if (!CheongryongArcLengthHistory.Finite(baseline[i])) return false;
            Vector3 lockedFlat = Vector3.ProjectOnPlane(lockedDirection, Vector3.up);
            if (lockedFlat.sqrMagnitude < 1e-8f) return false;
            Vector3 desired = Quaternion.AngleAxis(Mathf.Clamp(angle, -180, 180), Vector3.up) * lockedFlat.normalized;
            Vector3 chord = Vector3.ProjectOnPlane(baseline[baseline.Length - 1] - baseline[0], Vector3.up);
            if (chord.sqrMagnitude < 1e-8f) chord = Vector3.ProjectOnPlane(baseline[1] - baseline[0], Vector3.up);
            if (chord.sqrMagnitude < 1e-8f) return false;
            float yaw = Vector3.SignedAngle(chord, desired, Vector3.up); Vector3 total = Vector3.zero;
            int count = baseline.Length - 1;
            for (int i = 0; i < count; i++)
            {
                Vector3 delta = baseline[i + 1] - baseline[i];
                if (!float.IsFinite(delta.sqrMagnitude) || delta.sqrMagnitude < 1e-10f) return false;
                // A mild distributed bend changes the chain shape rather than swivelling one rigid tail.
                float bend = 12 * Mathf.Sin(((i + .5f) / count) * Mathf.PI * 2);
                directions[i] = Quaternion.AngleAxis(yaw + bend, Vector3.up) * delta; total += directions[i];
            }
            float correction = Vector3.SignedAngle(Vector3.ProjectOnPlane(total, Vector3.up), desired, Vector3.up);
            output[0] = baseline[0]; weight = Mathf.Clamp01(weight);
            for (int i = 0; i < count; i++)
            {
                Vector3 delta = baseline[i + 1] - baseline[i];
                Vector3 target = Quaternion.AngleAxis(correction, Vector3.up) * directions[i];
                float segmentYaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(delta, Vector3.up), Vector3.ProjectOnPlane(target, Vector3.up), Vector3.up);
                Vector3 final = Quaternion.AngleAxis(segmentYaw * weight, Vector3.up) * delta;
                // Explicit projection protects lengths against numerical quaternion error.
                final = CheongryongArcLengthHistory.SafeDirection(final, delta) * delta.magnitude;
                output[i + 1] = output[i] + final;
            }
            return true;
        }
    }

    /// <summary>
    /// Visual deformation only, evaluated after BodyFollow. Expects Body_12..Body_24 plus TailTip.
    /// Keeps the tail base fixed, reads locked plan geometry, and never creates hits or moves the actor.
    /// Ground contact, body self-collision and bend limits at the tail-base joint are not solved.
    /// </summary>
    [DefaultExecutionOrder(11000)]
    [DisallowMultipleComponent]
    public sealed class CheongryongTailSweepPresentation : MonoBehaviour
    {
        [SerializeField] private CheongryongCombatController _controller;
        [SerializeField] private Transform[] _tailBones;
        [SerializeField] private Transform _tailTip;
        private CheongryongCombatController _subscribed;
        private CheongryongAttackPlan _plan;
        private Vector3[] _baseline, _solved, _directions, _lastOutput;
        private Quaternion[] _baselineRotations, _lastRotations;
        private bool _hasOwnedPose, _recovering;
        public bool IsConfigured { get; private set; }
        public bool SweepImplemented => IsConfigured;
        public float MaximumSegmentLengthError { get; private set; }
        public float CurrentSweepAngle { get; private set; }
        public float CurrentWeight { get; private set; }
        public float TailRestLength { get; private set; }

        public bool Configure(CheongryongCombatController controller, Transform[] tailBones, Transform tailTip)
        {
            ClearPose(); Unsubscribe(); IsConfigured = false;
            if (tailBones == null || tailBones.Length < 2 || tailBones.Length > 128 || tailTip == null) return false;
            for (int i = 0; i < tailBones.Length; i++)
                if (tailBones[i] == null || (i > 0 && tailBones[i].parent != tailBones[i - 1])) return false;
            if (tailTip.parent != tailBones[tailBones.Length - 1]) return false;
            _controller = controller; _tailBones = (Transform[])tailBones.Clone(); _tailTip = tailTip;
            int count = tailBones.Length;
            _baseline = new Vector3[count + 1]; _solved = new Vector3[count + 1]; _lastOutput = new Vector3[count + 1]; _directions = new Vector3[count];
            _baselineRotations = new Quaternion[count]; _lastRotations = new Quaternion[count];
            CaptureBaseline(); TailRestLength = 0;
            for (int i = 0; i < count; i++)
            {
                float length = Vector3.Distance(_baseline[i], _baseline[i + 1]);
                if (!float.IsFinite(length) || length < 1e-5f) return false;
                TailRestLength += length;
            }
            IsConfigured = true; if (isActiveAndEnabled) Subscribe(); return true;
        }

        private void OnEnable()
        {
            if (!IsConfigured && _tailBones != null && _tailBones.Length >= 2) Configure(_controller, _tailBones, _tailTip);
            else if (IsConfigured) Subscribe();
        }
        private void OnDisable() { ClearPose(); Unsubscribe(); }
        private void OnDestroy() { ClearPose(); Unsubscribe(); }
        private void LateUpdate() { EvaluateAt(Time.time); }
        private void Subscribe()
        {
            if (_subscribed == _controller) return;
            Unsubscribe(); _subscribed = _controller;
            if (_subscribed == null) return;
            _subscribed.AttackStarted += OnStarted; _subscribed.AttackEnded += OnEnded; _subscribed.StateChanged += OnStateChanged;
            if (_subscribed.CurrentPlan != null) OnStarted(_subscribed.CurrentPlan);
        }
        private void Unsubscribe()
        {
            if (_subscribed != null)
            { _subscribed.AttackStarted -= OnStarted; _subscribed.AttackEnded -= OnEnded; _subscribed.StateChanged -= OnStateChanged; }
            _subscribed = null;
        }
        private void OnStarted(CheongryongAttackPlan plan)
        {
            if (plan == null) return;
            if (plan.Kind != CheongryongAttackKind.TailSweep || plan.IsCancelled) { ClearPose(); return; }
            _plan = plan; _recovering = false;
        }
        private void OnEnded(CheongryongAttackPlan plan, bool cancelled)
        {
            if (_plan != plan) return;
            if (cancelled) { ClearPose(); return; }
            _recovering = true; // Controller removes CurrentPlan at ActiveEndAt; keep this bounded recovery.
        }
        private void OnStateChanged(CheongryongCombatState state)
        {
            if (state == CheongryongCombatState.Dead || state == CheongryongCombatState.Growth || state == CheongryongCombatState.Stunned) ClearPose();
        }

        public void EvaluateAt(float scaledTime)
        {
            if (!IsConfigured || !isActiveAndEnabled || !float.IsFinite(scaledTime)) return;
            if (_controller != null && (!_controller.isActiveAndEnabled || _controller.State == CheongryongCombatState.Dead ||
                (_controller.Vitals != null && !_controller.Vitals.IsAlive))) { ClearPose(); return; }
            if (_controller != null && _controller.CurrentPlan != null && _controller.CurrentPlan != _plan) OnStarted(_controller.CurrentPlan);
            if (_plan == null || _plan.IsCancelled) { ClearPose(); return; }
            float now = _recovering ? Mathf.Max(_plan.SampleTime, scaledTime) : _plan.SampleTime;
            CheongryongTailPhase phase = CheongryongTailPoseMath.Phase(now, _plan.StartedAt, _plan.ReleaseAt, _plan.ActiveEndAt, _plan.EndAt, _plan.HalfAngleDegrees);
            if (now >= _plan.EndAt) { ClearPose(); return; }
            ApplyLockedPose(_plan.Direction, phase.AngleDegrees, phase.Weight);
        }

        // Public deterministic presentation seam for isolated pose checks. No gameplay state is changed.
        public bool ApplyLockedPose(Vector3 lockedDirection, float angle, float weight)
        {
            if (!IsConfigured) return false;
            for (int i = 0; i < _tailBones.Length; i++) if (_tailBones[i] == null) return false;
            if (_tailTip == null) return false;
            // If called twice without a fresh BodyFollow solve, reuse the previous baseline.
            // Otherwise the current post-follow pose becomes this frame's recovery target.
            if (!MatchesOwnedOutput()) CaptureBaseline();
            if (!CheongryongTailPoseMath.Solve(_baseline, lockedDirection, angle, weight, _solved, _directions)) return false;
            CurrentSweepAngle = angle; CurrentWeight = Mathf.Clamp01(weight); MaximumSegmentLengthError = 0;
            for (int i = 0; i < _tailBones.Length; i++)
            {
                Quaternion rotation = Quaternion.FromToRotation(_baseline[i + 1] - _baseline[i], _solved[i + 1] - _solved[i]) * _baselineRotations[i];
                _tailBones[i].SetPositionAndRotation(_solved[i], rotation);
            }
            _tailTip.position = _solved[_tailBones.Length];
            for (int i = 0; i < _tailBones.Length; i++)
            {
                _lastOutput[i] = _tailBones[i].position; _lastRotations[i] = _tailBones[i].rotation;
                Vector3 end = i + 1 < _tailBones.Length ? _tailBones[i + 1].position : _tailTip.position;
                MaximumSegmentLengthError = Mathf.Max(MaximumSegmentLengthError,
                    Mathf.Abs(Vector3.Distance(_tailBones[i].position, end) - Vector3.Distance(_baseline[i], _baseline[i + 1])));
            }
            _lastOutput[_tailBones.Length] = _tailTip.position; _hasOwnedPose = true; return true;
        }

        public void ClearPose()
        {
            // Restore only transforms still owned by this layer; never overwrite a newer BodyFollow pose.
            if (MatchesOwnedOutput())
            {
                for (int i = 0; i < _tailBones.Length; i++) _tailBones[i].SetPositionAndRotation(_baseline[i], _baselineRotations[i]);
                _tailTip.position = _baseline[_tailBones.Length];
            }
            _hasOwnedPose = false; _plan = null; _recovering = false; CurrentWeight = 0; CurrentSweepAngle = 0; MaximumSegmentLengthError = 0;
        }
        private bool MatchesOwnedOutput()
        {
            if (!_hasOwnedPose || _tailBones == null || _tailTip == null) return false;
            for (int i = 0; i < _tailBones.Length; i++)
                if (_tailBones[i] == null || Vector3.Distance(_tailBones[i].position, _lastOutput[i]) > .00001f || Mathf.Abs(Quaternion.Dot(_tailBones[i].rotation, _lastRotations[i])) < .999999f) return false;
            return Vector3.Distance(_tailTip.position, _lastOutput[_tailBones.Length]) <= .00001f;
        }
        private void CaptureBaseline()
        {
            for (int i = 0; i < _tailBones.Length; i++) { _baseline[i] = _tailBones[i].position; _baselineRotations[i] = _tailBones[i].rotation; }
            _baseline[_tailBones.Length] = _tailTip.position;
        }
    }
}
