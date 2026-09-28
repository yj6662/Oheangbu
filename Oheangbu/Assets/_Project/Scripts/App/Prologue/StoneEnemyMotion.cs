using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace Oheangbu.App.Prologue
{
    /// <summary>Rigid stone-body motion only; the actor collider and attack clock remain authoritative.</summary>
    public sealed class StoneEnemyMotion : MonoBehaviour
    {
        public EnemyController Enemy;
        public Transform Visual;
        [Range(0, 40)] public float WindupDegrees = 22;
        [Range(0, 60)] public float StrikeDegrees = 32;
        [Range(0, .2f)] public float StepHeight = .055f;
        [Min(.1f)] public float StepLength = 1.1f;
        Vector3 restPosition;
        Quaternion restRotation;
        NavMeshAgent agent;
        float stride;
        bool captured;

        void Awake()
        {
            if (Visual == null || Enemy == null) { enabled = false; return; }
            restPosition = Visual.localPosition;
            restRotation = Visual.localRotation;
            agent = Enemy.GetComponent<NavMeshAgent>();
            captured = true;
        }
        void LateUpdate()
        {
            if (!captured || Time.deltaTime <= 0) return;
            float pitch = 0, roll = 0, lift = 0;
            if (Enemy.IsStunned) pitch = StrikeDegrees * .65f;
            else if (Enemy.IsTelegraphing)
                pitch = -WindupDegrees * Mathf.SmoothStep(0, 1, Enemy.TelegraphProgress);
            else if (Enemy.IsRecovering)
                pitch = StrikeDegrees * (1 - Mathf.SmoothStep(0, 1, Enemy.RecoveryProgress));
            else if (Enemy.IsProjectileFlying) pitch = StrikeDegrees * .35f;
            else if (agent != null && agent.enabled && agent.isOnNavMesh && !agent.isStopped)
            {
                float speed = Vector3.ProjectOnPlane(agent.velocity, Vector3.up).magnitude;
                stride += speed * Time.deltaTime / Mathf.Max(.1f, StepLength) * Mathf.PI * 2;
                float weight = Mathf.Clamp01(speed);
                lift = Mathf.Abs(Mathf.Sin(stride)) * StepHeight * weight;
                roll = Mathf.Sin(stride) * 4 * weight;
            }
            Visual.localPosition = restPosition + Vector3.up * lift;
            Visual.localRotation = restRotation * Quaternion.Euler(pitch, 0, roll);
        }
        void OnDisable()
        {
            if (!captured || Visual == null) return;
            Visual.localPosition = restPosition;
            Visual.localRotation = restRotation;
        }
    }
}
