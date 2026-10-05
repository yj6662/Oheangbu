using UnityEngine;

namespace Oheangbu.App.World
{
    // #308 D308-23 (SPEC-ENEMY-RIG-VERIFY-308, section "run", TEST): when an EnemyRigMotion298 actor that carries a Run clip
    // leaves the walk for the run and comes back, as data. Without this asset the Run slot keeps its pre-D308-23 rule
    // (RunThresholdMetresPerSecond, exit at 85 % of it, TransitionSeconds). Presentation only: movement speed, attack
    // telegraphs and their clocks never read it. Every value is authored in the asset; the code holds no default numbers.
    [CreateAssetMenu(menuName = "Oheangbu/World/Enemy Run Profile 308")]
    public sealed class EnemyRunProfile308 : ScriptableObject
    {
        [Tooltip("Ground speed (m/s) above which the actor runs.")]
        [Min(0)] public float EnterMetresPerSecond;
        [Tooltip("Ground speed (m/s) below which a running actor walks again. Not above the enter speed: the gap is the hysteresis.")]
        [Min(0)] public float ExitMetresPerSecond;
        [Tooltip("Seconds the ground speed has to stay above the enter speed before the actor runs (0 = at once). Keeps a short burst of speed - " +
            "a turn on a patrol leg, a step toward a target that is already in reach - from flicking the gait. Leaving the run is immediate.")]
        [Min(0)] public float EnterHoldSeconds;
        [Tooltip("On: an actor whose PrologueEncounter is on patrol keeps the walk whatever its speed and runs only while it chases or returns " +
            "(patrol legs can be shorter than the distance the run needs). Actors without a PrologueEncounter are not affected.")]
        public bool WalkOnPatrol;
        [Tooltip("Seconds of the walk <-> run pose blend (other transitions keep EnemyRigMotion298.TransitionSeconds).")]
        [Range(0, 1)] public float BlendSeconds;
        [Tooltip("Run clip phase: actor metres per clip second = the measured stance-foot speed of the run clip (like WalkMetresPerSecond).")]
        [Min(0)] public float RunMetresPerSecond;
        [Tooltip("Arm relax at run weight. 0 = the run clip's arms as authored.")]
        [Range(0, 1)] public float RunArm;
        [Range(0, 1)] public float RunElbow;
        [Tooltip("The humanoid avatar the run clip was authored for. Set: the Run slot is used only on an Animator with this avatar, so an actor " +
            "cloned from another species keeps its Run / RunProfile slots but never runs with them. None = no such guard.")]
        public Avatar Avatar;

        // An asset that was created but never filled in must not switch anything on.
        public bool Usable =>
            float.IsFinite(EnterMetresPerSecond) && EnterMetresPerSecond > 0 && float.IsFinite(ExitMetresPerSecond) && ExitMetresPerSecond > 0 &&
            ExitMetresPerSecond <= EnterMetresPerSecond && float.IsFinite(RunMetresPerSecond) && RunMetresPerSecond > 0 &&
            float.IsFinite(BlendSeconds) && BlendSeconds >= 0 && float.IsFinite(EnterHoldSeconds) && EnterHoldSeconds >= 0;
    }
}
