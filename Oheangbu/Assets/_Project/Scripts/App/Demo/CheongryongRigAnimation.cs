using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Oheangbu.Combat;

namespace Oheangbu.App.Demo
{
    public readonly struct CheongryongClipPhase
    {
        public readonly float NormalizedTime, Weight;
        public CheongryongClipPhase(float time, float weight) { NormalizedTime = time; Weight = weight; }
    }

    /// <summary>Absolute combat-time mapping for prototype clips whose anticipation peak is at 50%.</summary>
    public static class CheongryongRigPhase
    {
        public static float IdleTime(float elapsed, float clipLength)
        {
            if (!float.IsFinite(elapsed) || !float.IsFinite(clipLength) || clipLength <= 0) return 0;
            return Mathf.Repeat(Mathf.Max(0, elapsed), clipLength);
        }

        public static CheongryongClipPhase Attack(float now, float start, float release, float activeEnd, float end, bool cancelled = false)
        {
            if (cancelled || !float.IsFinite(now) || !float.IsFinite(start) || !float.IsFinite(release) ||
                !float.IsFinite(activeEnd) || !float.IsFinite(end) || release <= start || activeEnd <= release || end < activeEnd || now < start || now >= end)
                return new CheongryongClipPhase(0, 0);
            if (now <= release)
            {
                float windup = Mathf.Clamp01((now - start) / (release - start));
                return new CheongryongClipPhase(.5f * windup, Mathf.SmoothStep(0, 1, windup * 5));
            }
            if (now <= activeEnd)
                return new CheongryongClipPhase(.5f + .4f * Mathf.Clamp01((now - release) / (activeEnd - release)), 1);
            float recovery = Mathf.Clamp01((now - activeEnd) / Mathf.Max(.00001f, end - activeEnd));
            return new CheongryongClipPhase(.9f + .1f * recovery, 1 - Mathf.SmoothStep(0, 1, recovery));
        }
    }

    /// <summary>
    /// Owns one manual Generic graph. Combat plans supply the same scaled sample time as damage.
    /// The imported tail clip contains Head anticipation only; it is not an implemented tail sweep.
    /// BodyFollow (order 10000) must solve the rest of the chain after this component (order 5000).
    /// </summary>
    [DefaultExecutionOrder(5000)]
    [DisallowMultipleComponent]
    public sealed class CheongryongRigAnimation : MonoBehaviour
    {
        [SerializeField] private Animator _animator;
        [SerializeField] private AnimationClip _idleClip, _headAttackClip, _tailAttackClip;
        [SerializeField] private CheongryongCombatController _controller;
        private CheongryongCombatController _subscribed;
        private CheongryongAttackPlan _presentationPlan;
        private bool _recovering;
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private AnimationClipPlayable _idle, _headAttack, _tailAttack;
        // Opt-in298 whole-body reactions. Null defaults preserve every older scene.
        [SerializeField] private AnimationClip _hitClip, _stunClip, _deathClip;
        [SerializeField] private CheongryongBodyFollow _stateFollow;
        [SerializeField] private CheongryongTailSweepPresentation _stateTail;
        private AnimationClipPlayable _hit, _stun, _death;
        private EnemyVitals _stateVitals;
        private float _lastHp, _hitAt = -100, _deathAt = -100, _stunAt = -100;
        private uint _lifeRevision;
        private Animator _neutralAnimator;
        private Transform[] _neutralBones;
        private Vector3[] _neutralPositions, _neutralScales;
        private Quaternion[] _neutralRotations;
        private bool _stateLayersOwned, _followWasEnabled, _tailWasEnabled, _wasStunned, _resetStateFollowAfterSample;
        public bool HasStateClips => Valid(_hitClip) && Valid(_stunClip) && Valid(_deathClip);
        public string CurrentPose { get; private set; } = "Idle";
        public bool WholeBodyStateActive => _stateLayersOwned;
        private RuntimeAnimatorController _savedController;
        private AnimatorCullingMode _savedCulling;
        private bool _savedRootMotion, _savedFireEvents, _ownsAnimator, _hasClock;
        private float _idleStartedAt;
        public bool IsConfigured { get; private set; }
        public bool HasGraph => _graph.IsValid();
        public float CurrentClipTime { get; private set; }
        public float CurrentAttackWeight { get; private set; }
        public int GraphEvaluationCount { get; private set; }
        public bool TailSweepRequested { get; private set; }
        public float TailSweepWindup01 { get; private set; }
        public bool TailSweepVisualImplemented => false;

        public bool Configure(Animator animator, AnimationClip idle, AnimationClip headAttack,
            AnimationClip tailAttack, CheongryongCombatController controller)
        {
            Unsubscribe(); ReleaseGraph(); IsConfigured = false;
            if (animator == null || animator.isHuman || !Valid(idle) || !Valid(headAttack) || !Valid(tailAttack)) return false;
            _animator = animator; _idleClip = idle; _headAttackClip = headAttack; _tailAttackClip = tailAttack; _controller = controller;
            IsConfigured = true; _hasClock = false;
            if (isActiveAndEnabled) { Subscribe(); if (!Dead) CreateGraph(); }
            return true;
        }

        public bool ConfigureStateClips(AnimationClip hit, AnimationClip stun, AnimationClip death,
            CheongryongBodyFollow follow, CheongryongTailSweepPresentation tail)
        {
            if (!Valid(hit) || !Valid(stun) || !Valid(death) || follow == null || tail == null) return false;
            ReleaseGraph(); UnbindStateVitals();
            _hitClip = hit; _stunClip = stun; _deathClip = death; _stateFollow = follow; _stateTail = tail;
            CaptureNeutralStatePose(); BindStateVitals(); ResetStateClock();
            if (isActiveAndEnabled && IsConfigured) CreateGraph(); return true;
        }
        private void CaptureNeutralStatePose()
        {
            if (!HasStateClips || _animator == null || _neutralAnimator == _animator) return;
            _neutralAnimator = _animator;
            _neutralBones = Array.FindAll(_animator.GetComponentsInChildren<Transform>(true), bone => bone != _animator.transform);
            int n = _neutralBones.Length; _neutralPositions = new Vector3[n]; _neutralScales = new Vector3[n]; _neutralRotations = new Quaternion[n];
            for (int i = 0; i < n; i++) { _neutralPositions[i] = _neutralBones[i].localPosition; _neutralRotations[i] = _neutralBones[i].localRotation; _neutralScales[i] = _neutralBones[i].localScale; }
        }
        private void RestoreNeutralStatePose()
        {
            if (!HasStateClips || _neutralBones == null) return;
            for (int i = 0; i < _neutralBones.Length; i++) if (_neutralBones[i] != null)
            { _neutralBones[i].localPosition = _neutralPositions[i]; _neutralBones[i].localRotation = _neutralRotations[i]; _neutralBones[i].localScale = _neutralScales[i]; }
        }
        private void BindStateVitals()
        {
            var current = HasStateClips ? GetComponent<EnemyVitals>() : null;
            if (_stateVitals == current) return; UnbindStateVitals(); _stateVitals = current;
            if (_stateVitals != null) { _lastHp = _stateVitals.Hp; _lifeRevision = _stateVitals.LifeRevision; _stateVitals.HpChanged += StateHpChanged; _stateVitals.Died += StateDied; }
        }
        private void UnbindStateVitals()
        {
            if (_stateVitals != null) { _stateVitals.HpChanged -= StateHpChanged; _stateVitals.Died -= StateDied; } _stateVitals = null;
        }
        private void StateHpChanged() { if (_stateVitals != null) { if (_stateVitals.Hp < _lastHp) _hitAt = Time.time; _lastHp = _stateVitals.Hp; } }
        private void StateDied() { _deathAt = Time.time; ClearAttack(); }
        private void ResetStateClock()
        {
            _hitAt = _deathAt = _stunAt = -100; _wasStunned = false;
            if (_stateVitals != null) { _lastHp = _stateVitals.Hp; _lifeRevision = _stateVitals.LifeRevision; }
            SetWholeBodyState(false); RestoreNeutralStatePose();
        }
        private void SetWholeBodyState(bool active)
        {
            if (active == _stateLayersOwned) return;
            if (active)
            {
                _followWasEnabled = _stateFollow != null && _stateFollow.enabled; _tailWasEnabled = _stateTail != null && _stateTail.enabled;
                if (_stateTail != null) _stateTail.enabled = false;
                if (_stateFollow != null) _stateFollow.enabled = false;
            }
            else
            {
                if (_stateFollow != null) { _stateFollow.enabled = _followWasEnabled; _resetStateFollowAfterSample = _followWasEnabled; }
                if (_stateTail != null) _stateTail.enabled = _tailWasEnabled;
            }
            _stateLayersOwned = active;
        }
        private static bool Valid(AnimationClip clip) => clip != null && !clip.legacy && float.IsFinite(clip.length) && clip.length > 0;
        private bool Dead => _controller != null && (_controller.State == CheongryongCombatState.Dead ||
            (_controller.Vitals != null && !_controller.Vitals.IsAlive));
        private void OnEnable()
        {
            CaptureNeutralStatePose(); BindStateVitals();
            if (!IsConfigured && _animator != null) Configure(_animator, _idleClip, _headAttackClip, _tailAttackClip, _controller);
            else if (IsConfigured) { _hasClock = false; Subscribe(); if (!Dead) CreateGraph(); }
        }
        private void OnDisable() { Unsubscribe(); UnbindStateVitals(); ReleaseGraph(); }
        private void OnDestroy() { Unsubscribe(); UnbindStateVitals(); ReleaseGraph(); }
        private void LateUpdate() { EvaluateAt(Time.time); }

        private void Subscribe()
        {
            if (_subscribed == _controller) return;
            Unsubscribe(); _subscribed = _controller;
            if (_subscribed != null)
            {
                _subscribed.StateChanged += OnStateChanged;
                _subscribed.AttackStarted += OnAttackStarted; _subscribed.AttackEnded += OnAttackEnded;
                if (_subscribed.CurrentPlan != null) OnAttackStarted(_subscribed.CurrentPlan);
            }
        }
        private void Unsubscribe()
        {
            if (_subscribed != null)
            {
                _subscribed.StateChanged -= OnStateChanged;
                _subscribed.AttackStarted -= OnAttackStarted; _subscribed.AttackEnded -= OnAttackEnded;
            }
            _subscribed = null;
        }
        private void OnStateChanged(CheongryongCombatState state)
        {
            if (state == CheongryongCombatState.Dead) { if (HasStateClips) { ClearAttack(); if (_deathAt < 0) _deathAt = Time.time; } else ReleaseGraph(); }
            else if (state == CheongryongCombatState.Growth || state == CheongryongCombatState.Stunned) ClearAttack();
        }
        private void ClearAttack() { _presentationPlan = null; _recovering = false; }
        private void OnAttackStarted(CheongryongAttackPlan plan)
        {
            if (plan == null || plan.IsCancelled || (plan.Kind != CheongryongAttackKind.HeadBite && plan.Kind != CheongryongAttackKind.TailSweep))
            { ClearAttack(); return; }
            _presentationPlan = plan; _recovering = false;
        }
        private void OnAttackEnded(CheongryongAttackPlan plan, bool cancelled)
        {
            if (_presentationPlan != plan) return;
            if (cancelled || plan.IsCancelled) ClearAttack();
            else _recovering = true;
        }

        private void CreateGraph()
        {
            if (HasGraph || _animator == null || !IsConfigured) return;
            CaptureNeutralStatePose();
            _savedController = _animator.runtimeAnimatorController; _savedRootMotion = _animator.applyRootMotion;
            _savedCulling = _animator.cullingMode; _savedFireEvents = _animator.fireEvents; _ownsAnimator = true;
            // No AnimatorController remains to evaluate in parallel with the manual graph.
            _animator.runtimeAnimatorController = null; _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; _animator.fireEvents = false;
            _graph = PlayableGraph.Create("Cheongryong explicit scaled pose");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual); _mixer = AnimationMixerPlayable.Create(_graph, HasStateClips ? 6 : 3);
            _idle = AnimationClipPlayable.Create(_graph, _idleClip);
            _headAttack = AnimationClipPlayable.Create(_graph, _headAttackClip);
            _tailAttack = AnimationClipPlayable.Create(_graph, _tailAttackClip);
            _idle.SetSpeed(0); _headAttack.SetSpeed(0); _tailAttack.SetSpeed(0);
            _idle.SetApplyFootIK(false); _headAttack.SetApplyFootIK(false); _tailAttack.SetApplyFootIK(false);
            _graph.Connect(_idle, 0, _mixer, 0); _graph.Connect(_headAttack, 0, _mixer, 1); _graph.Connect(_tailAttack, 0, _mixer, 2);
            if (HasStateClips)
            {
                _hit = AnimationClipPlayable.Create(_graph, _hitClip); _stun = AnimationClipPlayable.Create(_graph, _stunClip); _death = AnimationClipPlayable.Create(_graph, _deathClip);
                _hit.SetSpeed(0); _stun.SetSpeed(0); _death.SetSpeed(0);
                _hit.SetApplyFootIK(false); _stun.SetApplyFootIK(false); _death.SetApplyFootIK(false);
                _graph.Connect(_hit, 0, _mixer, 3); _graph.Connect(_stun, 0, _mixer, 4); _graph.Connect(_death, 0, _mixer, 5);
            }
            var output = AnimationPlayableOutput.Create(_graph, "Cheongryong Generic rig", _animator); output.SetSourcePlayable(_mixer);
            _mixer.SetInputWeight(0, 1); _mixer.SetInputWeight(1, 0); _mixer.SetInputWeight(2, 0); _graph.Play();
        }

        public void EvaluateAt(float scaledTime)
        {
            if (!IsConfigured || !isActiveAndEnabled || _animator == null || !float.IsFinite(scaledTime)) return;
            BindStateVitals();
            if (Dead && !HasStateClips) { ReleaseGraph(); return; }
            if (_stateVitals != null && _lifeRevision != _stateVitals.LifeRevision)
            {
                _lifeRevision = _stateVitals.LifeRevision;
                if (_stateVitals.IsAlive) { ResetStateClock(); _hasClock = false; }
            }
            CreateGraph(); if (!HasGraph) return;
            // Full-body clips key fins/limbs that head-only clips do not. Reset
            // their authored local pose before every sample, then let the active
            // graph and (when appropriate) BodyFollow own this frame explicitly.
            RestoreNeutralStatePose();
            if (HasStateClips && EvaluateStatePose(scaledTime)) return;
            CurrentPose = "Idle";
            if (!_hasClock || scaledTime < _idleStartedAt) { _idleStartedAt = scaledTime; _hasClock = true; }
            float idleTime = CheongryongRigPhase.IdleTime(scaledTime - _idleStartedAt, _idleClip.length);
            _idle.SetTime(idleTime); _headAttack.SetTime(0); _tailAttack.SetTime(0);
            CurrentAttackWeight = 0; CurrentClipTime = idleTime; TailSweepRequested = false; TailSweepWindup01 = 0;
            if (_controller != null)
            {
                if (!_controller.isActiveAndEnabled) ClearAttack();
                else if (_controller.CurrentPlan != null && _controller.CurrentPlan != _presentationPlan) OnAttackStarted(_controller.CurrentPlan);
            }
            var plan = _presentationPlan;
            int attackInput = 0;
            if (plan != null && !plan.IsCancelled && (plan.Kind == CheongryongAttackKind.HeadBite || plan.Kind == CheongryongAttackKind.TailSweep))
            {
                // Never advance the combat plan or substitute a separately accumulated animation delta.
                // CurrentPlan is removed at ActiveEndAt. Retain a normally ended plan
                // until EndAt so its actual recovery blend is not replaced by an idle snap.
                float poseTime = _recovering ? Mathf.Max(plan.SampleTime, scaledTime) : plan.SampleTime;
                CheongryongClipPhase phase = CheongryongRigPhase.Attack(poseTime, plan.StartedAt, plan.ReleaseAt, plan.ActiveEndAt, plan.EndAt);
                bool tail = plan.Kind == CheongryongAttackKind.TailSweep; attackInput = tail ? 2 : 1; CurrentPose = tail ? "TailSweep" : "HeadAttack";
                CurrentAttackWeight = phase.Weight; CurrentClipTime = phase.NormalizedTime * (tail ? _tailAttackClip.length : _headAttackClip.length);
                if (tail) _tailAttack.SetTime(CurrentClipTime); else _headAttack.SetTime(CurrentClipTime);
                TailSweepRequested = tail && poseTime < plan.EndAt;
                TailSweepWindup01 = TailSweepRequested ? plan.Windup01 : 0;
                if (poseTime >= plan.EndAt) ClearAttack();
            }
            if (HasStateClips) { _mixer.SetInputWeight(3, 0); _mixer.SetInputWeight(4, 0); _mixer.SetInputWeight(5, 0); }
            _mixer.SetInputWeight(0, 1 - CurrentAttackWeight);
            _mixer.SetInputWeight(1, attackInput == 1 ? CurrentAttackWeight : 0);
            _mixer.SetInputWeight(2, attackInput == 2 ? CurrentAttackWeight : 0);
            // Even a paused frame needs the same explicit pose before BodyFollow overwrites descendants.
            Transform root = _animator.transform; Vector3 position = root.localPosition, scale = root.localScale; Quaternion rotation = root.localRotation;
            _graph.Evaluate(0); GraphEvaluationCount++;
            root.localPosition = position; root.localRotation = rotation; root.localScale = scale;
            if (_resetStateFollowAfterSample && _stateFollow != null) { _stateFollow.ResetPoseHistory(); _resetStateFollowAfterSample = false; }
        }

        private bool EvaluateStatePose(float now)
        {
            bool stunned = _controller != null && _controller.State == CheongryongCombatState.Stunned || _stateVitals != null && _stateVitals.WeakPointActive;
            if (stunned && !_wasStunned) _stunAt = now; _wasStunned = stunned;
            int input = -1; float time = 0;
            if (Dead) { if (_deathAt < 0) _deathAt = now; input = 5; time = Mathf.Clamp(now - _deathAt, 0, _deathClip.length); CurrentPose = "Death"; }
            else if (stunned) { input = 4; time = Mathf.Clamp(now - _stunAt, 0, _stunClip.length); CurrentPose = "Stun"; }
            else if ((_controller == null || !_controller.AttackInProgress) && now >= _hitAt && now - _hitAt < _hitClip.length)
            { input = 3; time = now - _hitAt; CurrentPose = "Hit"; }
            SetWholeBodyState(input >= 0); if (input < 0) return false;
            ClearAttack(); TailSweepRequested = false; TailSweepWindup01 = 0; CurrentAttackWeight = 0; CurrentClipTime = time;
            for (int i = 0; i < 6; i++) _mixer.SetInputWeight(i, i == input ? 1 : 0);
            if (input == 3) _hit.SetTime(time); else if (input == 4) _stun.SetTime(time); else _death.SetTime(time);
            var root = _animator.transform; var p = root.localPosition; var r = root.localRotation; var scale = root.localScale;
            _graph.Evaluate(0); GraphEvaluationCount++; root.localPosition = p; root.localRotation = r; root.localScale = scale;
            return true;
        }

        public void ReleaseGraph()
        {
            SetWholeBodyState(false);
            if (_graph.IsValid()) _graph.Destroy();
            RestoreNeutralStatePose();
            if (_ownsAnimator && _animator != null)
            {
                _animator.runtimeAnimatorController = _savedController; _animator.applyRootMotion = _savedRootMotion;
                _animator.cullingMode = _savedCulling; _animator.fireEvents = _savedFireEvents;
            }
            _ownsAnimator = false; _hasClock = false; ClearAttack(); CurrentAttackWeight = 0; CurrentClipTime = 0;
            TailSweepRequested = false; TailSweepWindup01 = 0;
        }
    }
}
