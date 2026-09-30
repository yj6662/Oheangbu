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
        // #306: the live UI root survives scene loads (DontDestroyOnLoad) with its own state SO; a scene's session may reference a
        // different one (W_Demo_Main), which let the F that closed a dialogue reopen it the same frame. Bound here with the rest.
        public WorldMacroPlaytestSession Session;

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
        /// <summary>Time.frameCount of the last full resume (End to depth 0 / ForceResume). Interaction polling ignores the key
        /// that closed a modal on this frame and the next (read via PlaytestUiRoot.LastModalCloseFrame).</summary>
        public static int LastResumeFrame { get; private set; } = -10;
        public static bool ResumedRecently => Time.frameCount - LastResumeFrame <= 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { LastResumeFrame = -10; }   // domain reload is off

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
            if (Session != null) Session.RuntimeState = Gate.State;
        }

        /// <summary>Smoke check (#306 §2-2): gate, drawing, motor, seat and session all read the same runtime state SO.</summary>
        public bool StatesAligned(out string mismatch)
        {
            mismatch = "";
            var state = Gate != null ? Gate.State : null;
            if (state == null) { mismatch = "gate state missing"; return false; }
            if (Drawing != null && Drawing.RuntimeState != state) mismatch += " drawing";
            // no ?? on UnityEngine.Object: the editor's fake-null GetComponent result would skip the parent lookup
            PlayerMotor motor = Drawing != null ? Drawing.GetComponent<PlayerMotor>() : null;
            if (motor == null && Drawing != null) motor = Drawing.GetComponentInParent<PlayerMotor>();
            if (Drawing != null && motor == null) mismatch += " motor-missing";
            else if (motor != null && motor.RuntimeState != state) mismatch += " motor";
            if (PalanquinSeat != null && PalanquinSeat.RuntimeState != state) mismatch += " seat";
            if (Session != null && Session.RuntimeState != state) mismatch += " session";
            mismatch = mismatch.Trim();
            return mismatch.Length == 0;
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
            LastResumeFrame = Time.frameCount;
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
            LastResumeFrame = Time.frameCount;
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
