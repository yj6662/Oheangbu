using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // #308 WP-13, question Q5 (USER_ANSWERS, DECISIONS D308-13): "the spell rule plus a TEST target; real enemy AI is
    // separate work". The unblockable thrust ignores an enemy's guard, ward or reflect stance. No enemy of the game guards
    // yet, so this is the TEST target: a dummy that keeps the guard seam of its own EnemyVitals raised
    // (EnemyVitals.Modifiers.Stance). A guarding enemy built later raises and lowers the same seam from its own AI.
    // It carries no AI, takes no decision and deals no damage.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyVitals))]
    public sealed class SpellGuardTestTarget308 : MonoBehaviour
    {
        [Tooltip("TEST: whether this dummy is guarding")]
        [SerializeField] private bool _guarding = true;
        [Tooltip("TEST: share of every hit the guard stops, 0..1 (1 = all of it)")]
        [SerializeField, Range(0f, 1f)] private float _share = 1f;

        private EnemyVitals _vitals;

        public bool Guarding => _guarding;
        public float Share => _share;

        public void Configure(bool guarding, float share) { _guarding = guarding; _share = Mathf.Clamp01(share); Apply(); }

        private void OnEnable() { Apply(); }
        // The stance is cleared with the enemy's life (death, restore): a guarding dummy raises it again.
        private void Update() { Apply(); }
        private void OnDisable() { if (_vitals != null) _vitals.Modifiers.Stance.Lower(); }

        // Writes the dummy's state to the seam (checks call it directly: Edit Mode has no frame loop).
        public void Apply()
        {
            if (_vitals == null) _vitals = GetComponent<EnemyVitals>();
            if (_vitals == null) return;
            if (_guarding && _share > 0f) _vitals.Modifiers.Stance.Raise(_share);
            else _vitals.Modifiers.Stance.Lower();
        }
    }
}
