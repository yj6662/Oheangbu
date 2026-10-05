using UnityEngine;

namespace Oheangbu.App.World
{
    // #308 (SPEC-ENEMY-RIG-VERIFY-308, TEST): arm relax amounts of one monster, split by pose. EnemyRigMotion298 without a profile
    // keeps the #302 behaviour (one ArmRelax / ElbowRelax pair for idle and walk). Presentation only: nothing in combat, AI or
    // recognition reads this asset.
    [CreateAssetMenu(menuName = "Oheangbu/World/Enemy Arm Relax 308")]
    public sealed class EnemyArmRelaxProfile308 : ScriptableObject
    {
        [Tooltip("Idle pose. 0 = the clip as authored, 1 = upper arm on the hang direction / forearm on the upper-arm line.")]
        [Range(0, 1)] public float IdleArm;
        [Range(0, 1)] public float IdleElbow;
        [Tooltip("Walk pose. 0 for a walk clip whose arms are already repaired (walk_fix308).")]
        [Range(0, 1)] public float WalkArm;
        [Range(0, 1)] public float WalkElbow;
        [Tooltip("Hang direction in the actor's frame: down part and forward part (normalised at use). The #302 values are .95 / .12.")]
        public float HangDown = .95f;
        public float HangForward = .12f;
    }
}
