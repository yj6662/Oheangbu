using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Oheangbu.Combat;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;

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
        [Tooltip("#306 #11: an EnemyController that switches authored profiles (tutorial boss) plays MoveClips[i] while its current " +
            "profile is MoveProfiles[i] (contact phase MovePeaks01[i]); any other profile plays Attack. Not used with General.")]
        public EnemyAttackProfileSO[] MoveProfiles = Array.Empty<EnemyAttackProfileSO>();
        public AnimationClip[] MoveClips = Array.Empty<AnimationClip>();
        public float[] MovePeaks01 = Array.Empty<float>();
        [Range(.05f, .95f)] public float SweepPeak01 = .5f, WavePeak01 = .5f;
        [Range(.05f, .95f)] public float AttackPeak01 = .5f;
        [Min(.1f)] public float WalkMetresPerSecond = 1.5f;
        [Tooltip("#308 (SPEC-ANIM-QUADRUPED-308 design 4, TEST): optional fast gait. Plays Run instead of Walk while the actor's ground speed " +
            "exceeds RunThresholdMetresPerSecond (leaves below 85% of it). 0 = off: no Run node, Walk only (pre-#308 behaviour).")]
        public AnimationClip Run;
        [Min(0)] public float RunThresholdMetresPerSecond;
        [Tooltip("Run clip phase: actor metres per clip second (measured stance-foot speed, like WalkMetresPerSecond).")]
        [Min(.1f)] public float RunMetresPerSecond = 3f;
        [Tooltip("#308 D308-23 (SPEC-ENEMY-RIG-VERIFY-308 run section, TEST): the walk <-> run switch as data - enter / exit speed, how long the " +
            "enter speed has to hold, walk on patrol, blend time, the run clip's metres per second, arm relax at run weight, the avatar the clip is for. " +
            "None, or an asset that is not filled in = the three fields above with the fixed 85 % exit and TransitionSeconds (pre-D308-23 behaviour, same arithmetic).")]
        public EnemyRunProfile308 RunProfile;
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
        int runIndex = -1, moveNodes;   // #308: Run node sits after Sweep/Wave and MoveClips; -1 = no Run node in this graph
        bool running;
        float blendStarted;
        bool gaitBlend;      // #308 D308-23: the blend in progress is walk <-> run of a RunProfile actor (its own blend time, both gaits keep stepping)
        float gaitPhase;     // #308 D308-23: normalised step phase shared by Walk and Run of a RunProfile actor
        float runHeld;       // #308 D308-23: seconds the ground speed has stayed above the profile's enter speed (EnterHoldSeconds)
        bool runProfiled;    // #308 D308-23: latched with the graph - this actor's Run node is switched by its RunProfile
        PrologueEncounter encounter;   // #308 D308-23: read only (Current), for RunProfile.WalkOnPatrol; none = the speed rule alone
        [Range(0, .2f)] public float TransitionSeconds = .1f;
        // #302 retarget fix: the Meshy basic walking clip (walking_man) lands on the humanoid avatar with the arms held
        // out and the elbows locked bent. In idle/walk only, pull the upper arms toward the body and open the elbows.
        // -1 = automatic (on for walking_man, off otherwise). Values are TEST.
        [Range(-1, 1)] public float ArmRelax = -1f, ElbowRelax = -1f;
        [Tooltip("#308 (SPEC-ENEMY-RIG-VERIFY-308, TEST): idle and walk relax amounts as data. None = the single ArmRelax / ElbowRelax " +
            "pair above for both poses (pre-#308 behaviour, same arithmetic).")]
        public EnemyArmRelaxProfile308 RelaxProfile;
        bool wasStunned, resetSerpentHistory;
        Animator serpentPoseAnimator;
        Transform[] serpentPoseBones;
        Vector3[] serpentPosePositions, serpentPoseScales;
        Quaternion[] serpentPoseRotations;
        public string CurrentPose { get; private set; }
        public float PoseTime { get; private set; }
        // #307 phase 1 item 4: frames whose pose application was skipped because the actor was inactive and none of its renderers
        // was drawn (bookkeeping - clock, travel, blend weights, events - still ran, so the first drawn frame is the same pose).
        public int SkippedEvaluations { get; private set; }
        Renderer[] poseRenderers;
        bool liveEvaluate;   // only the component's own LateUpdate may skip; an explicit Evaluate call (checks) always samples
        public bool HasGraph => graph.IsValid();
        public bool IsConfigured => Animator != null && Vitals != null && (Enemy != null || General != null) &&
            Valid(Idle) && Valid(Walk) && Valid(Attack) && Valid(Hit) && Valid(Stun) && Valid(Death) &&
            float.IsFinite(AttackPeak01) && AttackPeak01 > 0 && AttackPeak01 < 1 &&
            (General == null || Valid(Sweep) && Valid(Wave));
        static bool Valid(AnimationClip c) => c != null && !c.legacy && c.length > 0;
        // #308: the Run slot is data-gated; a missing clip or a zero threshold keeps the Walk-only graph.
        // #308 D308-23: a usable RunProfile carries the gate and says whom the clip is for - the Run node is built only on an Animator of the
        // clip's kind (humanoid clip on a humanoid) and, when the profile names an avatar, of that avatar: a clone of another species keeps the
        // slots but never runs. An asset that is not filled in leaves the pre-D308-23 gate as it was.
        bool RunByProfile => RunProfile != null && RunProfile.Usable;
        bool RunFitsActor => Animator != null && Run.isHumanMotion == Animator.isHuman && (RunProfile.Avatar == null || Animator.avatar == RunProfile.Avatar);
        public bool HasRun => Valid(Run) && (RunByProfile ? RunFitsActor : float.IsFinite(RunThresholdMetresPerSecond) && RunThresholdMetresPerSecond > 0);
        public bool Running => running;
        public float GaitPhase => gaitPhase;
        void OnEnable() { CaptureSerpentPose(); Bind(); ResetPose(); }
        void Bind()
        {
            Unbind(); subscribedVitals = Vitals; subscribedGeneral = General; encounter = GetComponent<PrologueEncounter>();
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
            previous = transform.position; distance = 0; hitAt = deathAt = stunAt = -100; wasStunned = false; plan = null; priorInput = -1; running = false; gaitBlend = false; gaitPhase = 0; runHeld = 0;
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
            moveNodes = 0;
            if (General == null && MoveCount > 0) { var all = new List<AnimationClip>(clips); for (int i = 0; i < MoveCount; i++) all.Add(MoveClips[i]); clips = all.ToArray(); moveNodes = MoveCount; }
            runIndex = -1;
            if (HasRun) { var all = new List<AnimationClip>(clips) { Run }; runIndex = all.Count - 1; clips = all.ToArray(); }
            runProfiled = runIndex >= 0 && RunByProfile;   // #308 D308-23
            mixer = AnimationMixerPlayable.Create(graph, clips.Length); nodes = new AnimationClipPlayable[clips.Length]; blendFrom = new float[clips.Length]; priorInput = -1;
            for (int i = 0; i < clips.Length; i++) { nodes[i] = AnimationClipPlayable.Create(graph, clips[i]); nodes[i].SetSpeed(0); graph.Connect(nodes[i], 0, mixer, i); }
            AnimationPlayableOutput.Create(graph, "Actor bones", Animator).SetSourcePlayable(mixer); graph.Play();
            poseRenderers = Animator.GetComponentsInChildren<Renderer>(true);
        }
        // Not active (the session's Cull disabled its controller) and not drawn this frame: no enabled renderer on an active object.
        // A serpent (history-driven BodyFollow) always evaluates: its trail would diverge from the continuous one.
        bool CanSkipPose()
        {
            if (!liveEvaluate || SerpentFollow != null || poseRenderers == null) return false;
            if (Enemy != null && Enemy.isActiveAndEnabled || General != null && General.isActiveAndEnabled) return false;
            foreach (var r in poseRenderers) if (r != null && r.enabled && r.gameObject.activeInHierarchy) return false;
            return true;
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
            bool profiled = runProfiled && RunProfile != null && Run != null;   // #308 D308-23: this actor switches by the profile
            if (profiled)
            {
                // WalkOnPatrol: on patrol the actor keeps the walk whatever its speed (a short patrol leg would pump the gait every turn); it runs
                // while it chases or returns. EnterHoldSeconds: the enter speed has to hold that long first (0 = at once). Leaving is immediate.
                bool allowed = !RunProfile.WalkOnPatrol || encounter == null || encounter.Current != PrologueEncounter.Behaviour.Patrol;
                bool above = allowed && speed > RunProfile.EnterMetresPerSecond;
                runHeld = above && !running ? runHeld + dt : 0f;
                running = running ? allowed && speed > RunProfile.ExitMetresPerSecond : above && runHeld >= RunProfile.EnterHoldSeconds;
                // one step phase for both gaits: the distance over the stride of the gait that is on (same clip rate as the distance formula below)
                float stride = running ? Mathf.Max(.1f, RunProfile.RunMetresPerSecond) * Run.length : Mathf.Max(.1f, WalkMetresPerSecond) * Walk.length;
                gaitPhase = Mathf.Repeat(gaitPhase + travel / stride, 1f);
            }
            else if (runIndex >= 0) running = running ? speed > RunThresholdMetresPerSecond * .85f : speed > RunThresholdMetresPerSecond;
            bool stunned = General != null ? General.State == SouthGateCombatState.Stunned : Enemy != null && Enemy.IsStunned;
            if (stunned && !wasStunned) stunAt = now; wasStunned = stunned;
            int index = 0; float t = Mathf.Repeat(now, Idle.length); CurrentPose = "Idle";
            if (!Vitals.IsAlive) { if (deathAt < 0) deathAt = now; index = 5; t = Mathf.Min(Mathf.Max(0, now - deathAt), Death.length); CurrentPose = "Death"; }
            else if (stunned) { index = 4; t = LoopStun ? Mathf.Repeat(Mathf.Max(0, now - stunAt), Stun.length) : Mathf.Min(Mathf.Max(0, now - stunAt), Stun.length); CurrentPose = "Stun"; }
            else if (General != null && plan != null) { float phase = GeneralPhase(out index); t = phase * (index == 6 ? Sweep.length : index == 7 ? Wave.length : Attack.length); CurrentPose = index == 6 ? "Sweep" : index == 7 ? "Wave" : "Thrust"; }
            else if (General == null && Enemy != null && Enemy.AttackInProgress)
            {
                int move = MoveIndex(Enemy.AttackProfile);
                var clip = move >= 0 ? MoveClips[move] : Attack; float peak = move >= 0 && move < (MovePeaks01?.Length ?? 0) ? MovePeaks01[move] : AttackPeak01;
                index = move >= 0 ? 6 + move : 2; t = AttackPhase(peak, Enemy.IsTelegraphing, Enemy.TelegraphProgress, Enemy.IsRecovering, Enemy.RecoveryProgress) * clip.length;
                CurrentPose = move >= 0 ? clip.name : "Attack";
            }
            else if (now >= hitAt && now - hitAt < Hit.length) { index = 3; t = now - hitAt; CurrentPose = "Hit"; }
            else if (running && Run != null) { index = runIndex; t = profiled ? gaitPhase * Run.length : Mathf.Repeat(distance / Mathf.Max(.1f, RunMetresPerSecond), Run.length); CurrentPose = "Run"; }
            else if (speed > .08f) { index = 1; t = profiled ? gaitPhase * Walk.length : Mathf.Repeat(distance / Mathf.Max(.1f, WalkMetresPerSecond), Walk.length); CurrentPose = "Walk"; }
            bool firstPose = priorInput < 0;
            if (priorInput != index)
            {
                for (int i = 0; i < nodes.Length; i++) blendFrom[i] = firstPose ? (i == index ? 1 : 0) : mixer.GetInputWeight(i);
                gaitBlend = profiled && !firstPose && (priorInput == 1 && index == runIndex || priorInput == runIndex && index == 1);
                priorInput = index; blendStarted = now;
            }
            // Blend poses only. Both attack clip time and the impact clock stay
            // absolute; the anticipation reaches the same authored contact phase.
            float blendSeconds = gaitBlend && profiled ? RunProfile.BlendSeconds : TransitionSeconds;
            float blend = blendSeconds <= 0 ? 1 : Mathf.Clamp01((now - blendStarted) / blendSeconds);
            for (int i = 0; i < nodes.Length; i++) mixer.SetInputWeight(i, Mathf.Lerp(blendFrom[i], i == index ? 1 : 0, blend));
            if (SerpentFollow != null)
            {
                bool follow = Vitals.IsAlive && index != 3 && index != 4 && index != 5;
                if (follow && !SerpentFollow.enabled) resetSerpentHistory = true;
                SerpentFollow.enabled = follow;
            }
            nodes[index].SetTime(t); PoseTime = t;
            // #308 D308-23: during a walk <-> run blend the outgoing gait keeps stepping on the shared phase (frozen, its feet would cross the incoming ones)
            if (gaitBlend && profiled && blend < 1f) { bool toRun = index == runIndex; nodes[toRun ? 1 : runIndex].SetTime(gaitPhase * (toRun ? Walk.length : Run.length)); }
            if (CanSkipPose()) { SkippedEvaluations++; effects.RemoveAll(e => e == null); return; }
            FootPlacement?.RestoreAuthoredPose(); RestoreSerpentPose(); graph.Evaluate(0);
            ApplyArmRelax(mixer.GetInputWeight(0), mixer.GetInputWeight(1), runIndex >= 0 ? mixer.GetInputWeight(runIndex) : 0f);
            FootPlacement?.ApplyPose(dt);
            if (resetSerpentHistory && SerpentFollow != null && SerpentFollow.enabled) { SerpentFollow.ResetPoseHistory(); resetSerpentHistory = false; }
            effects.RemoveAll(e => e == null);
        }
        // #306 #11: profile -> move clip (only valid pairs count; a missing clip falls back to Attack)
        int MoveCount
        {
            get
            {
                int n = Mathf.Min(MoveProfiles?.Length ?? 0, MoveClips?.Length ?? 0);
                for (int i = 0; i < n; i++) if (!Valid(MoveClips[i])) return i;
                return n;
            }
        }
        int MoveIndex(EnemyAttackProfileSO profile)
        {
            if (profile == null || nodes == null) return -1;
            int n = Mathf.Min(MoveCount, moveNodes);   // #308: the optional Run node follows the move nodes
            for (int i = 0; i < n; i++) if (MoveProfiles[i] == profile) return i;
            return -1;
        }
        // #307 item 12: Walk.name allocates; the answer is kept per clip object (a destroyed clip still reads as false)
        AnimationClip meshyWalkClip; bool meshyWalkCached;
        bool MeshyWalk
        {
            get
            {
                if (Walk == null) return false;
                if (!ReferenceEquals(Walk, meshyWalkClip)) { meshyWalkClip = Walk; meshyWalkCached = Walk.name.Contains("walking_man"); }
                return meshyWalkCached;
            }
        }
        // #307 item 4: humanoid arm bones per Animator/avatar (GetBoneTransform x6 per frame before)
        Animator boneAnimator; Avatar boneAvatar; readonly Transform[] armBones = new Transform[6];
        public float ArmRelaxAmount => ArmRelax >= 0 ? ArmRelax : MeshyWalk ? .55f : 0f;
        public float ElbowRelaxAmount => ElbowRelax >= 0 ? ElbowRelax : MeshyWalk ? .5f : 0f;
        // #308: what each pose gets. Without a profile both poses read the #302 pair.
        public float IdleArmRelaxAmount => RelaxProfile != null ? RelaxProfile.IdleArm : ArmRelaxAmount;
        public float IdleElbowRelaxAmount => RelaxProfile != null ? RelaxProfile.IdleElbow : ElbowRelaxAmount;
        public float WalkArmRelaxAmount => RelaxProfile != null ? RelaxProfile.WalkArm : ArmRelaxAmount;
        public float WalkElbowRelaxAmount => RelaxProfile != null ? RelaxProfile.WalkElbow : ElbowRelaxAmount;
        // #308 D308-23: the run pose is relaxed only by a RunProfile (none = the clip's arms as authored, as before).
        public float RunArmRelaxAmount => RunProfile != null ? RunProfile.RunArm : 0f;
        public float RunElbowRelaxAmount => RunProfile != null ? RunProfile.RunElbow : 0f;

        // #308 (SPEC-ENEMY-RIG-VERIFY-308): idle and walk weights apart so a profile can relax them by different amounts.
        // Public for the edit-mode verifier (EnemyRigVerify308 samples a clip, then calls this: the code Play runs after graph.Evaluate).
        public void ApplyArmRelax(float idleWeight, float walkWeight) => ApplyArmRelax(idleWeight, walkWeight, 0f);

        // #308 D308-23: the same entry with the run weight. A run weight of 0, or no RunProfile, is the two-weight arithmetic unchanged.
        public void ApplyArmRelax(float idleWeight, float walkWeight, float runWeight)
        {
            float arm, elbow;
            if (RelaxProfile == null) { float weight = idleWeight + walkWeight; arm = ArmRelaxAmount * weight; elbow = ElbowRelaxAmount * weight; }   // pre-#308 arithmetic
            else { arm = RelaxProfile.IdleArm * idleWeight + RelaxProfile.WalkArm * walkWeight; elbow = RelaxProfile.IdleElbow * idleWeight + RelaxProfile.WalkElbow * walkWeight; }
            if (RunProfile != null && runWeight > 0) { arm += RunProfile.RunArm * runWeight; elbow += RunProfile.RunElbow * runWeight; }
            if (arm <= 0 && elbow <= 0 || Animator == null || !Animator.isHuman) return;
            // arms hang, slightly forward (#302 direction when there is no profile)
            Vector3 hang = RelaxProfile == null ? (-transform.up * .95f + transform.forward * .12f).normalized
                : (-transform.up * RelaxProfile.HangDown + transform.forward * RelaxProfile.HangForward).normalized;
            if (hang.sqrMagnitude < .5f) return;
            var avatar = Animator.avatar;
            if (!ReferenceEquals(boneAnimator, Animator) || !ReferenceEquals(boneAvatar, avatar))
            {
                boneAnimator = Animator; boneAvatar = avatar;
                armBones[0] = Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm); armBones[1] = Animator.GetBoneTransform(HumanBodyBones.LeftLowerArm); armBones[2] = Animator.GetBoneTransform(HumanBodyBones.LeftHand);
                armBones[3] = Animator.GetBoneTransform(HumanBodyBones.RightUpperArm); armBones[4] = Animator.GetBoneTransform(HumanBodyBones.RightLowerArm); armBones[5] = Animator.GetBoneTransform(HumanBodyBones.RightHand);
            }
            Relax(armBones[0], armBones[1], armBones[2], arm, elbow, hang);
            Relax(armBones[3], armBones[4], armBones[5], arm, elbow, hang);
        }

        static void Relax(Transform upper, Transform lower, Transform hand, float arm, float elbow, Vector3 hang)
        {
            if (upper == null || lower == null || hand == null) return;
            Vector3 dir = (lower.position - upper.position).normalized;
            upper.rotation = Quaternion.FromToRotation(dir, Vector3.Slerp(dir, hang, arm)) * upper.rotation;
            Vector3 along = (lower.position - upper.position).normalized, fore = (hand.position - lower.position).normalized;
            lower.rotation = Quaternion.FromToRotation(fore, Vector3.Slerp(fore, along, elbow)) * lower.rotation;
        }

        void LateUpdate() { using (Perf307Markers.RigEvaluate.Auto()) { if (Time.deltaTime > 0) { liveEvaluate = true; try { Evaluate(Time.time, Time.deltaTime); } finally { liveEvaluate = false; } } } }
        void RemoveWarning() { if (warning != null) Destroy(warning); warning = null; }
        void OnDisable()
        {
            Unbind(); if (graph.IsValid()) graph.Destroy(); RestoreSerpentPose(); resetSerpentHistory = true; RemoveWarning();
            foreach (var effect in effects) if (effect != null) Destroy(effect); effects.Clear(); plan = null;
        }
    }
}
