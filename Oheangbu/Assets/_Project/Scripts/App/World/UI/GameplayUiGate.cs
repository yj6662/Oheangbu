using System;
using Oheangbu.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Oheangbu.App.World.UI
{
    /// <summary>Owns modal input blocking and waits for held controls to return neutral.</summary>
    [DisallowMultipleComponent]
    public sealed class GameplayUiGate : MonoBehaviour
    {
        public GameplayRuntimeStateSO State;

        private bool _releasePending;
        private bool _focusOwnsBlock;
        private int _neutralFrames;

        public bool InputBlocked => State != null && State.InputBlocked;
        public bool ReleasePending => _releasePending;
        public bool FocusOwnsBlock => _focusOwnsBlock;
        public int NeutralFrames => _neutralFrames;
        public event Action<bool> BlockedChanged;

        private void Awake() { Initialize(); }

        public void Initialize()
        {
            if (State == null) return;
            _releasePending = false;
            _focusOwnsBlock = false;
            _neutralFrames = 0;
            State.SetInputBlocked(false);
            if (!Application.isFocused)
            {
                _focusOwnsBlock = true;
                SetBlocked(true);
            }
        }

        public void Block()
        {
            // A modal owner supersedes a transient focus-loss block.
            _focusOwnsBlock = false;
            _releasePending = false;
            _neutralFrames = 0;
            SetBlocked(true);
        }

        public void ReleaseWhenNeutral()
        {
            if (!InputBlocked) return;
            _releasePending = true;
            _neutralFrames = 0;
        }

        public void ReleaseImmediately()
        {
            _releasePending = false;
            _neutralFrames = 0;
            SetBlocked(false);
        }

        private void Update()
        {
            if (!_releasePending) return;
            if (!Application.isFocused) { _neutralFrames = 0; return; }
            if (HasHeldControl()) { _neutralFrames = 0; return; }
            // Require a complete neutral frame so the key/button that closed the menu cannot
            // become gameplay input in the same frame.
            if (++_neutralFrames < 2) return;
            ReleaseImmediately();
        }

        private static bool HasHeldControl()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.wKey.isPressed || keyboard.aKey.isPressed
                || keyboard.sKey.isPressed || keyboard.dKey.isPressed || keyboard.qKey.isPressed
                || keyboard.leftShiftKey.isPressed || keyboard.tabKey.isPressed || keyboard.fKey.isPressed
                || keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed || keyboard.cKey.isPressed || keyboard.xKey.isPressed
                || keyboard.gKey.isPressed || keyboard.eKey.isPressed || keyboard.vKey.isPressed || keyboard.spaceKey.isPressed
                || keyboard.iKey.isPressed || keyboard.mKey.isPressed || keyboard.escapeKey.isPressed)) return true;
            Mouse mouse = Mouse.current;
            return mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed
                || mouse.middleButton.isPressed);
        }

        private void OnDisable()
        {
            _focusOwnsBlock = false;
            if (State != null && State.InputBlocked) ReleaseImmediately();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                if (!InputBlocked)
                {
                    _focusOwnsBlock = true;
                    _releasePending = false;
                    _neutralFrames = 0;
                    SetBlocked(true);
                }
                return;
            }
            if (!_focusOwnsBlock) return;
            _focusOwnsBlock = false;
            ReleaseWhenNeutral();
        }

        private void SetBlocked(bool blocked)
        {
            if (State == null) return;
            bool changed = State.InputBlocked != blocked;
            State.SetInputBlocked(blocked);
            if (changed) BlockedChanged?.Invoke(blocked);
        }
    }
}
