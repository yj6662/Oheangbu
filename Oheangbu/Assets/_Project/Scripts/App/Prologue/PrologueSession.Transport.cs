using Oheangbu.App.World.Vehicle;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App.Prologue
{
    public sealed partial class PrologueSession
    {
        public WorldMacroPalanquinSeat JourneySeat;
        public bool JourneySeated => JourneySeat != null && JourneySeat.Occupied;

        void BindJourneyTransport()
        {
            if (JourneySeat == null) return;
            JourneySeat.RuntimeState = Player.GetComponent<PlayerMotor>().RuntimeState;
            JourneySeat.BoardingAllowed = CanBoardJourneyTransport;
            JourneySeat.Exited += JourneyTransportExited;
            BindJourneyEscortLoad();
        }

        bool CanBoardJourneyTransport() => ready && !respawning && isActiveAndEnabled &&
            Time.timeScale > 0 && vitals != null && vitals.Hp01 > 0 && HasGuk &&
            Progress.completed.Contains("wangso_w1") && (JourneySeat.RuntimeState == null || !JourneySeat.RuntimeState.InputBlocked) &&
            (JourneyField == null || !JourneyField.HasPlatform) && CanLoadJourneyEscort();

        void TrackJourneyTransportGround()
        {
            if (JourneySeated && JourneySeat.Vehicle.GroundedWheelCount >= 3 &&
                JourneySeat.TryGetSafeExit(out var exit)) lastGrounded = exit;
        }

        void JourneyTransportExited()
        {
            // Seat resolves a supported, clear capsule location before restoring player ownership.
            lastGrounded = Player.position;
            RequestJourneyEscortExit();
        }

        void ReleaseJourneyTransport()
        {
            escortRecovering=true;
            if (JourneySeated) JourneySeat.ForceExitForRecovery(Progress.checkpointPosition, 0);
            escortRecovering=false;
        }

        void UnbindJourneyTransport()
        {
            if (JourneySeat == null) return;
            JourneySeat.BoardingAllowed = null;
            JourneySeat.ConfirmBoarding = null;
            JourneySeat.Exited -= JourneyTransportExited;
        }
    }
}
