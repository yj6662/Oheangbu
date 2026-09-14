using System;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Combat;
using Oheangbu.Drawing;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    /// <summary>Single owner of modal pause, cursor and safe input-resume ordering.</summary>
    [DisallowMultipleComponent]
    public sealed class PauseCoordinator : MonoBehaviour
    {
        public GameplayUiGate Gate;
        public DrawingInputController Drawing;
        public WorldMacroPalanquinSeat PalanquinSeat;
        public WorldMacroPalanquinController PalanquinController;

        private int _depth;
        private float _priorTimeScale = 1f;
        private CursorLockMode _priorCursorLock;
        private bool _priorCursorVisible;
        private bool _restoreCursorWhenNeutral;
        private bool _initialized;
        private GameplayUiGate _subscribedGate;

        public bool IsPaused => _depth > 0;
        public int Depth => _depth;
        public event Action<bool> PausedChanged;

        private void Awake() { Initialize(); }

        public void Initialize()
        {
            SynchronizeRuntimeState();
            if (_initialized && _subscribedGate == Gate) return;
            if (_subscribedGate != null) _subscribedGate.BlockedChanged -= OnGateChanged;
            _subscribedGate = Gate;
            if (_subscribedGate != null)
            {
                _subscribedGate.BlockedChanged -= OnGateChanged;
                _subscribedGate.BlockedChanged += OnGateChanged;
            }
            _initialized = true;
        }

        private void SynchronizeRuntimeState()
        {
            if (Gate == null || Gate.State == null) return;
            if (Drawing != null)
            {
                Drawing.RuntimeState = Gate.State;
                PlayerMotor motor = Drawing.GetComponent<PlayerMotor>();
                if (motor == null) motor = Drawing.GetComponentInParent<PlayerMotor>();
                if (motor != null) motor.RuntimeState = Gate.State;
            }
            if (PalanquinSeat != null) PalanquinSeat.RuntimeState = Gate.State;
        }

        public bool Begin()
        {
            Initialize();
            _depth++;
            if (_depth > 1) return false;

            // Drawing owns its temporary 0.35 scale. Let it restore that scale without
            // committing or misfiring before this coordinator captures and pauses the world.
            Drawing?.CancelForUi();
            Gate?.Block();
            StopVehicleInput();

            _priorTimeScale = Time.timeScale;
            _priorCursorLock = Cursor.lockState;
            _priorCursorVisible = Cursor.visible;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _restoreCursorWhenNeutral = false;
            PausedChanged?.Invoke(true);
            return true;
        }

        public bool End()
        {
            if (_depth <= 0) return false;
            _depth--;
            if (_depth > 0) return false;

            Time.timeScale = _priorTimeScale;
            StopVehicleInput();
            if (Gate != null && Gate.InputBlocked)
            {
                _restoreCursorWhenNeutral = true;
                Gate.ReleaseWhenNeutral();
            }
            else RestoreCursor();
            PausedChanged?.Invoke(false);
            return true;
        }

        public void ForceResume()
        {
            if (_depth > 0) Time.timeScale = _priorTimeScale;
            _depth = 0;
            StopVehicleInput();
            Gate?.ReleaseImmediately();
            RestoreCursor();
            PausedChanged?.Invoke(false);
        }

        private void OnGateChanged(bool blocked)
        {
            if (!blocked && _restoreCursorWhenNeutral && !IsPaused) RestoreCursor();
        }

        private void StopVehicleInput()
        {
            WorldMacroPalanquinController controller = PalanquinController != null
                ? PalanquinController : PalanquinSeat?.Vehicle;
            controller?.StopDriverInputForUi();
        }

        private void RestoreCursor()
        {
            _restoreCursorWhenNeutral = false;
            Cursor.lockState = _priorCursorLock;
            Cursor.visible = _priorCursorVisible;
        }

        private void OnDisable()
        {
            if (_subscribedGate != null) _subscribedGate.BlockedChanged -= OnGateChanged;
            _subscribedGate = null;
            if (_depth > 0)
            {
                Time.timeScale = _priorTimeScale;
                _depth = 0;
            }
            Gate?.ReleaseImmediately();
            RestoreCursor();
            _initialized = false;
        }
    }
}
