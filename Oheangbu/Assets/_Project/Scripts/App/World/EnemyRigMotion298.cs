using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Oheangbu.Combat;
using Oheangbu.App.Demo;

namespace Oheangbu.App.World
{
    // Presentation only. Existing controllers retain navigation, windup, release,
    // damage, parry and recovery ownership. Clip events never deal damage.
    [DefaultExecutionOrder(800)]
    [DisallowMultipleComponent]
    public sealed class EnemyRigMotion298 : MonoBehaviour
    {
        public Animator Animator;
        public EnemyController Enemy;
        public EnemyVitals Vitals;
        public SouthGateGeneralController General;
        public CheongryongBodyFollow SerpentFollow;
        public EnemyFootPlacement298 FootPlacement;
        public AnimationClip Idle, Walk, Attack, Hit, Stun, Death;
        public AnimationClip Sweep, Wave;
        [Range(.05f, .95f)] public float SweepPeak01 = .5f, WavePeak01 = .5f;
        [Range(.05f, .95f)] public float AttackPeak01 = .5f;
        [Min(.1f)] public float WalkMetresPerSecond = 1.5f;
        public bool LoopStun;
        public GameObject EarthWarning, EarthImpact;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable[] nodes;
        EnemyVitals subscribedVitals;
        SouthGateGeneralController subscribedGeneral;
        SouthGateGeneralAttackPlan plan;
        readonly List<GameObject> effects = new List<GameObject>();
        GameObject warning;
        Vector3 previous;
        float distance, hitAt = -100, deathAt = -100, stunAt = -100;
        uint revision;
        float[] blendFrom;
        int priorInput = -1;
        float blendStarted;
        [Range(0, .2f)] public float TransitionSeconds = .1f;
        // #302 retarget fix: the Meshy basic walking clip (walking_man) lands on the humanoid avatar with the arms held
        // out and the elbows locked bent. In idle/walk only, pull the upper arms toward the body and open the elbows.
        // -1 = automatic (on for walking_man, off otherwise). Values are TEST.
        [Range(-1, 1)] public float ArmRelax = -1f, ElbowRelax = -1f;
        bool wasStunned, resetSerpentHistory;
        Animator serpentPoseAnimator;
        Transform[] serpentPoseBones;
        Vector3[] serpentPosePositions, serpentPoseScales;
        Quaternion[] serpentPoseRotations;
        public string CurrentPose { get; private set; }
        public float PoseTime { get; private set; }
        public bool HasGraph => graph.IsValid();
        public bool IsConfigured => Animator != null && Vitals != null && (Enemy != null || General != null) &&
            Valid(Idle) && Valid(Walk) && Valid(Attack) && Valid(Hit) && Valid(Stun) && Valid(Death) &&
            float.IsFinite(AttackPeak01) && AttackPeak01 > 0 && AttackPeak01 < 1 &&
            (General == null || Valid(Sweep) && Valid(Wave));
        static bool Valid(AnimationClip c) => c != null && !c.legacy && c.length > 0;
        void OnEnable() { CaptureSerpentPose(); Bind(); ResetPose(); }
        void Bind()
        {
            Unbind(); subscribedVitals = Vitals; subscribedGeneral = General;
            if (subscribedVitals != null) { subscribedVitals.DamageResolved += Damaged; subscribedVitals.Died += Died; }
            if (subscribedGeneral != null) { subscribedGeneral.AttackStarted += Started; subscribedGeneral.AttackEnded += Ended; subscribedGeneral.AttackImpactResolved += Impact; }
        }
        void Unbind()
        {
            if (subscribedVitals != null) { subscribedVitals.DamageResolved -= Damaged; subscribedVitals.Died -= Died; }
            if (subscribedGeneral != null) { subscribedGeneral.AttackStarted -= Started; subscribedGeneral.AttackEnded -= Ended; subscribedGeneral.AttackImpactResolved -= Impact; }
            subscribedVitals = null; subscribedGeneral = null;
        }
        void ResetPose()
        {
            previous = transform.position; distance = 0; hitAt = deathAt = stunAt = -100; wasStunned = false; plan = null; priorInput = -1;
            if (Vitals != null) revision = Vitals.LifeRevision;
            if (SerpentFollow != null) { RestoreSerpentPose(); resetSerpentHistory = true; }
            RemoveWarning();
        }
        void CaptureSerpentPose()
        {
            if (SerpentFollow == null || Animator == null || serpentPoseAnimator == Animator) return;
            serpentPoseAnimator = Animator;
            // Capture once from the authored idle prefab, before any graph/follow
            // evaluation. Head-only clips cannot reset unkeyed fins or limbs.
            var all = Animator.GetComponentsInChildren<Transform>(true);
            serpentPoseBones = Array.FindAll(all, bone => bone != Animator.transform);
            int n = serpentPoseBones.Length; serpentPosePositions = new Vector3[n]; serpentPoseScales = new Vector3[n]; serpentPoseRotations = new Quaternion[n];
            for (int i = 0; i < n; i++) { serpentPosePositions[i] = serpentPoseBones[i].localPosition; serpentPoseRotations[i] = serpentPoseBones[i].localRotation; serpentPoseScales[i] = serpentPoseBones[i].localScale; }
        }
        void RestoreSerpentPose()
        {
            if (serpentPoseBones == null) return;
            for (int i = 0; i < serpentPoseBones.Length; i++) if (serpentPoseBones[i] != null)
            { serpentPoseBones[i].localPosition = serpentPosePositions[i]; serpentPoseBones[i].localRotation = serpentPoseRotations[i]; serpentPoseBones[i].localScale = serpentPoseScales[i]; }
        }
        // #306: one hit pose per resolved damage event with provenance. A harvest chunk reacts only when
        // CombatConfigSO.HarvestChunkReaction allows it (EnemyVitals.ReactsTo); HP changes alone never restart the pose.
        void Damaged(EnemyDamageResult result)
        { if (Vitals != null && result.Target == Vitals && result.AppliedDamage > 0 && Vitals.ReactsTo(result.Attack)) hitAt = Time.time; }
        void Died() { deathAt = Time.time; plan = null; RemoveWarning(); }
        void Started(SouthGateGeneralAttackPlan value) { plan = value; }
        void Ended(SouthGateGeneralAttackPlan value, bool cancelled) { if (plan == value) plan = null; RemoveWarning(); }
        void Impact(SouthGateGeneralImpact value)
        {
            if (!value.Contact || !value.Pulse.Attack.Element.HasValue || EarthImpact == null) return;
            var effect = Instantiate(EarthImpact, value.Point, Quaternion.identity); effects.Add(effect); Destroy(effect, .8f);
        }
        void Create()
        {
            if (graph.IsValid() || !IsConfigured || !Application.isPlaying) return;
            CaptureSerpentPose();
            Animator.enabled = true; Animator.runtimeAnimatorController = null; Animator.applyRootMotion = false; Animator.fireEvents = false;
            Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("Folklore298 combat presentation"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var clips = General == null ? new[] { Idle, Walk, Attack, Hit, Stun, Death } : new[] { Idle, Walk, Attack, Hit, Stun, Death, Sweep, Wave };
            mixer = AnimationMixerPlayable.Create(graph, clips.Length); nodes = new AnimationClipPlayable[clips.Length]; blendFrom = new float[clips.Length]; priorInput = -1;
            for (int i = 0; i < clips.Length; i++) { nodes[i] = AnimationClipPlayable.Create(graph, clips[i]); nodes[i].SetSpeed(0); graph.Connect(nodes[i], 0, mixer, i); }
            AnimationPlayableOutput.Create(graph, "Actor bones", Animator).SetSourcePlayable(mixer); graph.Play();
        }
        public static float AttackPhase(float peak, bool windup, float anticipation, bool recovering, float recovery)
        {
            peak = Mathf.Clamp(peak, .05f, .95f);
            return windup ? peak * Mathf.Clamp01(anticipation) : recovering ? peak + (1 - peak) * Mathf.Clamp01(recovery) : peak;
        }
        float GeneralPhase(out int index)
        {
            float time = plan.SampleTime; var pulse = plan.Pulses[0];
            if (plan.Pulses.Count > 1 && time >= plan.EmpowerStartAt) pulse = plan.Pulses[1];
            index = pulse.Shape == SouthGatePulseShape.Sweep ? 6 : pulse.Shape == SouthGatePulseShape.Wave ? 7 : 2;
            float peak = index == 6 ? SweepPeak01 : index == 7 ? WavePeak01 : AttackPeak01;
            float start = pulse == plan.Pulses[0] ? plan.StartedAt : plan.EmpowerStartAt;
            float end = plan.Pulses.Count > 1 && pulse == plan.Pulses[0] ? plan.EmpowerStartAt : plan.EndAt;
            float phase = time < pulse.ReleaseAt ? peak * Mathf.InverseLerp(start, pulse.ReleaseAt, time) :
                peak + (1 - peak) * Mathf.InverseLerp(pulse.ReleaseAt, end, time);
            if (pulse.Attack.Element.HasValue && time < pulse.ReleaseAt && warning == null && EarthWarning != null)
            { warning = Instantiate(EarthWarning, transform.position - Vector3.up * .82f, Quaternion.identity); warning.transform.localScale *= 1.15f; }
            if (time >= pulse.ReleaseAt) RemoveWarning();
            return Mathf.Clamp01(phase);
        }
        public void Evaluate(float now, float dt)
        {
            if (!IsConfigured) return;
            if (subscribedVitals != Vitals || subscribedGeneral != General) { Bind(); ResetPose(); }
            Create(); if (!graph.IsValid()) return;
            if (revision != Vitals.LifeRevision) ResetPose();
            float travel = Vector3.ProjectOnPlane(transform.position - previous, Vector3.up).magnitude; previous = transform.position;
            float speed = dt > 0 ? travel / dt : 0; if (speed > 12) { speed = travel = distance = 0; if (SerpentFollow != null) resetSerpentHistory = true; } distance += travel;
            bool stunned = General != null ? General.State == SouthGateCombatState.Stunned : Enemy != null && Enemy.IsStunned;
            if (stunned && !wasStunned) stunAt = now; wasStunned = stunned;
            int index = 0; float t = Mathf.Repeat(now, Idle.length); CurrentPose = "Idle";
            if (!Vitals.IsAlive) { if (deathAt < 0) deathAt = now; index = 5; t = Mathf.Min(Mathf.Max(0, now - deathAt), Death.length); CurrentPose = "Death"; }
            else if (stunned) { index = 4; t = LoopStun ? Mathf.Repeat(Mathf.Max(0, now - stunAt), Stun.length) : Mathf.Min(Mathf.Max(0, now - stunAt), Stun.length); CurrentPose = "Stun"; }
            else if (General != null && plan != null) { float phase = GeneralPhase(out index); t = phase * (index == 6 ? Sweep.length : index == 7 ? Wave.length : Attack.length); CurrentPose = index == 6 ? "Sweep" : index == 7 ? "Wave" : "Thrust"; }
            else if (General == null && Enemy != null && Enemy.AttackInProgress) { index = 2; t = AttackPhase(AttackPeak01, Enemy.IsTelegraphing, Enemy.TelegraphProgress, Enemy.IsRecovering, Enemy.RecoveryProgress) * Attack.length; CurrentPose = "Attack"; }
            else if (now >= hitAt && now - hitAt < Hit.length) { index = 3; t = now - hitAt; CurrentPose = "Hit"; }
            else if (speed > .08f) { index = 1; t = Mathf.Repeat(distance / Mathf.Max(.1f, WalkMetresPerSecond), Walk.length); CurrentPose = "Walk"; }
            bool firstPose = priorInput < 0;
            if (priorInput != index)
            {
                for (int i = 0; i < nodes.Length; i++) blendFrom[i] = firstPose ? (i == index ? 1 : 0) : mixer.GetInputWeight(i);
                priorInput = index; blendStarted = now;
            }
            // Blend poses only. Both attack clip time and the impact clock stay
            // absolute; the anticipation reaches the same authored contact phase.
            float blend = TransitionSeconds <= 0 ? 1 : Mathf.Clamp01((now - blendStarted) / TransitionSeconds);
            for (int i = 0; i < nodes.Length; i++) mixer.SetInputWeight(i, Mathf.Lerp(blendFrom[i], i == index ? 1 : 0, blend));
            if (SerpentFollow != null)
            {
                bool follow = Vitals.IsAlive && index != 3 && index != 4 && index != 5;
                if (follow && !SerpentFollow.enabled) resetSerpentHistory = true;
                SerpentFollow.enabled = follow;
            }
            nodes[index].SetTime(t); PoseTime = t;
            FootPlacement?.RestoreAuthoredPose(); RestoreSerpentPose(); graph.Evaluate(0);
            RelaxArms(mixer.GetInputWeight(0) + mixer.GetInputWeight(1));
            FootPlacement?.ApplyPose(dt);
            if (resetSerpentHistory && SerpentFollow != null && SerpentFollow.enabled) { SerpentFollow.ResetPoseHistory(); resetSerpentHistory = false; }
            effects.RemoveAll(e => e == null);
        }
        bool MeshyWalk => Walk != null && Walk.name.Contains("walking_man");
        public float ArmRelaxAmount => ArmRelax >= 0 ? ArmRelax : MeshyWalk ? .55f : 0f;
        public float ElbowRelaxAmount => ElbowRelax >= 0 ? ElbowRelax : MeshyWalk ? .5f : 0f;

        void RelaxArms(float weight)
        {
            float arm = ArmRelaxAmount * weight, elbow = ElbowRelaxAmount * weight;
            if (arm <= 0 && elbow <= 0 || Animator == null || !Animator.isHuman) return;
            Relax(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, arm, elbow);
            Relax(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, arm, elbow);
        }

        void Relax(HumanBodyBones upperBone, HumanBodyBones lowerBone, HumanBodyBones handBone, float arm, float elbow)
        {
            Transform upper = Animator.GetBoneTransform(upperBone), lower = Animator.GetBoneTransform(lowerBone), hand = Animator.GetBoneTransform(handBone);
            if (upper == null || lower == null || hand == null) return;
            Vector3 hang = (-transform.up * .95f + transform.forward * .12f).normalized;          // arms hang, slightly forward
            Vector3 dir = (lower.position - upper.position).normalized;
            upper.rotation = Quaternion.FromToRotation(dir, Vector3.Slerp(dir, hang, arm)) * upper.rotation;
            Vector3 along = (lower.position - upper.position).normalized, fore = (hand.position - lower.position).normalized;
            lower.rotation = Quaternion.FromToRotation(fore, Vector3.Slerp(fore, along, elbow)) * lower.rotation;
        }

        void LateUpdate() { if (Time.deltaTime > 0) Evaluate(Time.time, Time.deltaTime); }
        void RemoveWarning() { if (warning != null) Destroy(warning); warning = null; }
        void OnDisable()
        {
            Unbind(); if (graph.IsValid()) graph.Destroy(); RestoreSerpentPose(); resetSerpentHistory = true; RemoveWarning();
            foreach (var effect in effects) if (effect != null) Destroy(effect); effects.Clear(); plan = null;
        }
    }
}
