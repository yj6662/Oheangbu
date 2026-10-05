using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Drawing;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Oheangbu.App
{
    // What the sword form (SwordFormEffect308) needs from the player's input side (#308 WP-14, Q7: built now, TEST).
    // The form stays a rule object: it asks through this seam and never touches an input action, the draw mode or the
    // harvest click itself. A host without the seam (an old scene, a bare check) still carries the form; only the click,
    // the shut draw mode and the release key are missing there.
    public interface ISwordFormSeam308
    {
        // Called by the form when it begins and then every frame while it lasts: the draw mode stays shut and the click
        // belongs to the sword. Re-applied each call, so somebody replacing the draw gate in between cannot open it.
        void HoldSwordForm308();
        // The form ended (ink gone, death, rest, scene leave, another sword cast, or put away by the player).
        void DropSwordForm308(bool byPlayer);
        // This frame's click, while the player may act with the brush hand (not in a menu, a dodge, a seat or the air).
        bool SwordStrikePressed308 { get; }
        // This frame's "put the sword away" press: the key that raises the brush.
        bool SwordReleasePressed308 { get; }
    }

    // The seam on the wiring. No rule lives here: the strike, its numbers, the counter strike and the end of the form are
    // SwordFormEffect308 / SwordFormRule308. This partial only answers "was the click pressed" and keeps two existing
    // doors shut while the form is held:
    //   draw mode   DrawingInputController.EntryAllowed (the gate the walker owns). The seam puts its own gate in front of
    //               the owner's and hands the owner's gate back when the form ends. Recognition is not touched: a shut
    //               gate means no stroke is ever sampled, so nothing is recognised, spent or misfired.
    //   harvest     the same click pulls ink outside the form. HarvestAction is switched off while the form is held and
    //               switched on again when it ends: a strike never starts a pull (no chunk damage, no ink income).
    // Input bindings stay data (.inputactions); the code knows the action names only, like PlayerMotor does.
    public sealed partial class CombatLoopWiring : ISwordFormSeam308
    {
        // TEST: the click of the Gameplay map that pulls ink outside the form. A dedicated action can replace it by name.
        private const string SwordClickMap308 = "Gameplay", SwordClickAction308 = "Harvest";

        private Func<bool> _swordGate308;        // this seam's gate (one delegate for the wiring's whole life)
        private Func<bool> _swordInnerGate308;   // the gate that was in place, handed back when the form ends
        private bool _swordHeld308, _swordReleaseHeld308, _swordHarvestOff308;
        private InputAction _swordClick308;
        private readonly List<InputAction> _swordActions308 = new List<InputAction>();

        // Read-only for presentation and checks: the brush is a sword right now.
        public bool SwordFormHeld308 => _swordHeld308;

        void ISwordFormSeam308.HoldSwordForm308()
        {
            _swordHeld308 = true; _swordReleaseHeld308 = false;
            if (_drawingInput != null)
            {
                if (_swordGate308 == null) _swordGate308 = SwordDrawGate308;
                var current = _drawingInput.EntryAllowed;
                if (!ReferenceEquals(current, _swordGate308)) { _swordInnerGate308 = current; _drawingInput.EntryAllowed = _swordGate308; }
            }
            if (_harvest != null && _harvest.enabled) { _harvest.enabled = false; _swordHarvestOff308 = true; }
        }

        void ISwordFormSeam308.DropSwordForm308(bool byPlayer)
        {
            bool held = _swordHeld308;
            _swordHeld308 = false;
            if (_swordHarvestOff308) { _swordHarvestOff308 = false; if (_harvest != null) _harvest.enabled = true; }
            // Put away by the player: the gate stays until that key is up again, so the press that ends the sword does not
            // raise the brush in the same breath. It hands itself back from SwordDrawGate308.
            _swordReleaseHeld308 = held && byPlayer && _drawingInput != null && ReferenceEquals(_drawingInput.EntryAllowed, _swordGate308);
            if (!_swordReleaseHeld308) SwordGiveBackGate308();
        }

        bool ISwordFormSeam308.SwordStrikePressed308
        {
            get
            {
                if (!_swordHeld308 || !SwordInputOpen308() || (_motor != null && !_motor.CanBeginDrawing)) return false;
                var click = SwordClick308();
                return click != null && click.WasPressedThisFrame();
            }
        }

        bool ISwordFormSeam308.SwordReleasePressed308
        {
            get
            {
                if (!_swordHeld308 || !SwordInputOpen308()) return false;
                var key = _drawingInput != null ? _drawingInput.DrawModeAction : null;
                return key != null && key.WasPressedThisFrame();
            }
        }

        // The same conditions that stop every other player action: a disabled wiring or motor, a menu, a stopped clock, death.
        private bool SwordInputOpen308()
        {
            return isActiveAndEnabled && Time.timeScale > 0f && (_motor == null || (_motor.isActiveAndEnabled && !_motor.InputBlocked)) &&
                (_playerVitals == null || _playerVitals.Hp01 > 0f);
        }

        // The draw gate while the seam is in front. false = the brush cannot be raised.
        private bool SwordDrawGate308()
        {
            if (_swordHeld308) return false;
            if (_swordReleaseHeld308)
            {
                var key = _drawingInput != null ? _drawingInput.DrawModeAction : null;
                if (key != null && key.IsPressed()) return false;
                _swordReleaseHeld308 = false;
            }
            var inner = SwordGiveBackGate308();
            return inner == null || inner();
        }

        // The owner's gate goes back where it was. An owner that switched itself off while the sword was out would have
        // taken its gate away (the walker clears its own gate in OnDisable): then nothing is put back.
        private Func<bool> SwordGiveBackGate308()
        {
            var inner = _swordInnerGate308; _swordInnerGate308 = null;
            if (inner != null && inner.Target is Behaviour owner && (owner == null || !owner.isActiveAndEnabled)) inner = null;
            if (_drawingInput != null && _swordGate308 != null && ReferenceEquals(_drawingInput.EntryAllowed, _swordGate308)) _drawingInput.EntryAllowed = inner;
            return inner;
        }

        // The enabled click action, found by name among the enabled actions (the motor enables its own map). Looked up when
        // the form needs it and kept while it stays enabled; no input asset is referenced from here.
        private InputAction SwordClick308()
        {
            if (_swordClick308 != null && _swordClick308.enabled) return _swordClick308;
            _swordClick308 = null;
            _swordActions308.Clear();
            InputSystem.ListEnabledActions(_swordActions308);
            for (int i = 0; i < _swordActions308.Count; i++)
            {
                var action = _swordActions308[i];
                if (action == null || action.name != SwordClickAction308 || action.actionMap == null || action.actionMap.name != SwordClickMap308) continue;
                _swordClick308 = action;
                break;
            }
            _swordActions308.Clear();
            return _swordClick308;
        }
    }
}
