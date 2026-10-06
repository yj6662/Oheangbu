using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Oheangbu.App.Demo
{
    // Temporary C02 human/weapon presentation. One real existing humanoid clip graph; no damage or targeting here.
    [DefaultExecutionOrder(800)]
    public sealed class SouthGateGeneralPresentation : MonoBehaviour
    {
        [SerializeField] SouthGateGeneralController _controller;
        [SerializeField] Animator _animator;
        [SerializeField] AnimationClip _idle, _walk, _action;
        [SerializeField] Transform _weaponPivot;
        [SerializeField] GameObject _earthWarning, _earthImpact;
        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        AnimationClipPlayable _idlePlayable, _walkPlayable, _actionPlayable;
        SouthGateGeneralAttackPlan _plan;
        GameObject _warning;
        readonly List<GameObject> _transients = new List<GameObject>();
        [SerializeField] Quaternion _weaponRest = Quaternion.identity;
        Vector3 _lastPosition;
        float _clock;
        public bool HasGraph => _graph.IsValid();
        // #307 phase 1 item 4: graph evaluations skipped while the general was inactive (controller disabled by the session's Cull)
        // and none of its renderers was drawn; clip times, weights, warnings and the weapon accent still update every frame.
        public int SkippedEvaluations { get; private set; }
        Renderer[] _poseRenderers;
        bool CanSkipPose()
        {
            if (_poseRenderers == null || _controller.isActiveAndEnabled) return false;
            foreach (var r in _poseRenderers) if (r != null && r.enabled && r.gameObject.activeInHierarchy) return false;
            return true;
        }
        public bool IsConfigured => _controller != null && _animator != null && _animator.avatar != null && _animator.avatar.isHuman &&
            _idle != null && _walk != null && _action != null && _weaponPivot != null;
        public string TemporaryArtNotice => "Existing C02 humanoid, reused clip with bounded weapon-pivot accent, asset-derived spear. Dedicated two-hand polearm animations/hand contact and general costume remain unverified.";
        public void Configure(SouthGateGeneralController controller, Animator animator, Transform weapon, AnimationClip idle, AnimationClip walk,
            AnimationClip action, GameObject warning, GameObject impact)
        {
            Unbind(); Release(); _controller = controller; _animator = animator; _weaponPivot = weapon;
            _idle = idle; _walk = walk; _action = action; _earthWarning = warning; _earthImpact = impact;
            if (_weaponPivot != null) _weaponRest = _weaponPivot.localRotation;
            if (isActiveAndEnabled) { Bind(); Build(); }
        }
        void OnEnable() { Bind(); Build(); _lastPosition = transform.position; }
        void OnDisable() { Unbind(); Release(); }
        void OnDestroy() { Unbind(); Release(); }
        void Bind()
        {
            if (_controller == null) return;
            _controller.AttackStarted -= Started; _controller.AttackStarted += Started;
            _controller.AttackImpactResolved -= Impact; _controller.AttackImpactResolved += Impact;
            _controller.AttackEnded -= Ended; _controller.AttackEnded += Ended;
            _controller.StateChanged -= State; _controller.StateChanged += State;
        }
        void Unbind()
        {
            if (_controller == null) return;
            _controller.AttackStarted -= Started; _controller.AttackImpactResolved -= Impact;
            _controller.AttackEnded -= Ended; _controller.StateChanged -= State;
        }
        void Build()
        {
            if (!Application.isPlaying || !IsConfigured || _graph.IsValid() || (_controller.Vitals != null && !_controller.Vitals.IsAlive)) return;
            _animator.enabled = true; _animator.applyRootMotion = false; _animator.runtimeAnimatorController = null;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _graph = PlayableGraph.Create("SouthGate_General_OwnedClips"); _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _mixer = AnimationMixerPlayable.Create(_graph, 3);
            _idlePlayable = AnimationClipPlayable.Create(_graph, _idle); _walkPlayable = AnimationClipPlayable.Create(_graph, _walk);
            _actionPlayable = AnimationClipPlayable.Create(_graph, _action);
            _graph.Connect(_idlePlayable, 0, _mixer, 0); _graph.Connect(_walkPlayable, 0, _mixer, 1); _graph.Connect(_actionPlayable, 0, _mixer, 2);
            AnimationPlayableOutput.Create(_graph, "General body", _animator).SetSourcePlayable(_mixer); _graph.Play();
            _poseRenderers = _animator.GetComponentsInChildren<Renderer>(true);
        }
        void Started(SouthGateGeneralAttackPlan plan) { _plan = plan; }
        void Ended(SouthGateGeneralAttackPlan plan, bool cancelled) { if (_plan == plan) _plan = null; RemoveWarning(); }
        void State(SouthGateCombatState state) { if (state == SouthGateCombatState.Dead) Release(); }
        void Impact(SouthGateGeneralImpact value)
        {
            if (!value.Contact || !value.Pulse.Attack.Element.HasValue || _earthImpact == null) return;
            var effect = Instantiate(_earthImpact, value.Point, Quaternion.identity); _transients.Add(effect); Destroy(effect, .8f);
        }
        void LateUpdate()
        {
            if (_controller == null || _controller.Vitals == null || !_controller.Vitals.IsAlive) return;
            Build(); if (!_graph.IsValid()) return;
            float dt = Time.deltaTime; _clock += dt;
            float speed = dt > 0 ? Vector3.Distance(transform.position, _lastPosition) / dt : 0; _lastPosition = transform.position;
            _idlePlayable.SetTime(_clock % Mathf.Max(.1f, _idle.length)); _walkPlayable.SetTime(_clock % Mathf.Max(.1f, _walk.length));
            float actionWeight = _plan != null ? 1 : 0, walking = Mathf.Clamp01(speed / 1.7f);
            _mixer.SetInputWeight(0, (1 - actionWeight) * (1 - walking)); _mixer.SetInputWeight(1, (1 - actionWeight) * walking); _mixer.SetInputWeight(2, actionWeight);
            float weaponYaw = 0, weaponPitch = 0;
            if (_plan != null)
            {
                float time = _plan.SampleTime; var pulse = _plan.Pulses[0];
                if (_plan.Pulses.Count > 1 && time >= _plan.EmpowerStartAt) pulse = _plan.Pulses[1];
                float windupStart = pulse == _plan.Pulses[0] ? _plan.StartedAt : _plan.EmpowerStartAt;
                float phase = time < pulse.ReleaseAt ? Mathf.InverseLerp(windupStart, pulse.ReleaseAt, time) * .35f :
                    time <= pulse.ActiveEndAt ? .35f + Mathf.InverseLerp(pulse.ReleaseAt, pulse.ActiveEndAt, time) * .3f :
                    .65f + Mathf.InverseLerp(pulse.ActiveEndAt, _plan.EndAt, time) * .35f;
                _actionPlayable.SetTime(Mathf.Clamp01(phase) * _action.length);
                float accent = Mathf.Sin(Mathf.Clamp01(phase) * Mathf.PI);
                weaponPitch = pulse.Shape == SouthGatePulseShape.Wave ? -32 * accent : -18 * accent;
                if (pulse.Shape == SouthGatePulseShape.Sweep) weaponYaw = Mathf.Lerp(-65, 65, Mathf.Clamp01((phase - .2f) / .55f)) * accent;
                if (pulse.Attack.Element.HasValue && time < pulse.ReleaseAt && _warning == null && _earthWarning != null)
                {
                    _warning = Instantiate(_earthWarning, transform.position - Vector3.up * .82f, Quaternion.identity);
                    _warning.transform.localScale *= 1.15f;
                }
                if (time >= pulse.ReleaseAt) RemoveWarning();
            }
            if (CanSkipPose()) SkippedEvaluations++; else _graph.Evaluate(0);
            if (_weaponPivot != null) _weaponPivot.localRotation = _weaponRest * Quaternion.Euler(weaponPitch, weaponYaw, 0);
            _transients.RemoveAll(item => item == null);
        }
        void RemoveWarning() { if (_warning != null) Destroy(_warning); _warning = null; }
        void Release()
        {
            if (_graph.IsValid()) _graph.Destroy(); _plan = null; RemoveWarning();
            foreach (var effect in _transients) if (effect != null) Destroy(effect); _transients.Clear();
            if (_weaponPivot != null) _weaponPivot.localRotation = _weaponRest;
        }
    }
}
