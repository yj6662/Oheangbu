using UnityEngine.InputSystem;

namespace Oheangbu.Combat
{
    // #308 HUD (SPEC-HUD-LIQUID-308 §3 / §5.1): read-only state for the dodge and jump marks. Nothing here changes movement,
    // input or judgement: the marks ask the same gate predicates the input handlers use (DodgeGateOpen / JumpGateOpen), so
    // "the mark is wet" and "the key works" cannot drift apart. No fields, no static state.
    public sealed partial class PlayerMotor
    {
        /// <summary>The motor has a DodgeAction (no dodge mark without one).</summary>
        public bool HasDodge => _dodge != null;
        /// <summary>A locomotion profile and a Jump action exist (no jump mark without them).</summary>
        public bool HasJump => HasLocomotion && _jumpAction != null;
        /// <summary>A dodge pressed now would start: gate open, not dashing, cooldown over.</summary>
        public bool CanDodgeNow => _dodge != null && DodgeGateOpen() && !_dodge.IsDashing && _dodge.CooldownRemaining <= 0f;
        /// <summary>A jump pressed now would start.</summary>
        public bool CanJumpNow => HasJump && JumpGateOpen();
        /// <summary>1 = ready; share of the dodge cooldown that has passed.</summary>
        public float DodgeCooldown01 => _dodge != null ? _dodge.Cooldown01 : 1f;
        /// <summary>The combat numbers this motor runs on (the HUD reads SpellInkCost for the low-ink look).</summary>
        public CombatConfigSO Config => _config;
        // D308-11b key glyphs (SPEC §3.5): the very actions OnDodge / OnJump are subscribed to, so the HUD shows the key that is
        // really bound (overrides included). Read-only handles: the HUD only reads their bindings.
        /// <summary>The Input System action that triggers the dodge (null without an action asset).</summary>
        public InputAction DodgeInput => _dodgeAction;
        /// <summary>The Input System action that triggers the jump (null without a locomotion profile's action).</summary>
        public InputAction JumpInput => _jumpAction;
        /// <summary>The action asset this motor reads (the HUD looks a vehicle-call action up in its Gameplay map).</summary>
        public InputActionAsset InputActions => _actions;
        /// <summary>Name of the action map the motor's actions live in.</summary>
        public string InputMapName => MapName;
    }
}
