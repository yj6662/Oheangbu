using UnityEngine;

namespace Oheangbu.App.World.Vehicle
{
    // #308 player juice (SPEC-ANIM-JUICE-308 C-4 / D-2b) [TEST]: the car's arrival has weight.
    //   · The ink presentation (VehicleInkPresentation308) is not changed. This reads its phase once a frame and, when a phase
    //     boundary passes, tells the two presenters that already exist: the car controller (visual body settle / lift, no physics)
    //     and the player's gesture rig (the set-down thump of the camera reaction, render-time only).
    //       summon: Recede -> Idle with one more materialization = the car is fully revealed  -> settle + thump
    //       recall: entering Cover = the ink starts to take the car                            -> lift (no camera reaction)
    //   · Numbers come from the rig's PlayerJuice308ProfileSO; without a profile nothing here does anything.
    //   · This component runs before the ink presentation (order -200), so a boundary is seen one frame after it happened.
    // Presentation only. No static field.
    public sealed partial class WorldMacroPalanquinSummon
    {
        VehicleInkPresentation308.Phase inkPhase308;
        int inkMaterialized308;
        /// <summary>#308 juice: arrival settles / recall lifts handed to the car controller (diagnostic).</summary>
        public int ArrivalSettles308 { get; private set; }
        public int RecallLifts308 { get; private set; }

        void TickJuice308()
        {
            if (ink308 == null) return;
            var phase = ink308.Current; int materialized = ink308.Materializations;
            if (phase == inkPhase308 && materialized == inkMaterialized308) return;
            var previous = inkPhase308; int before = inkMaterialized308;
            inkPhase308 = phase; inkMaterialized308 = materialized;
            if (Gesture == null || Vehicle == null) return;
            var profile = Gesture.JuiceProfile308;
            if (profile == null) return;
            float scale = Gesture.JuiceCallScale308;
            if (materialized > before && phase == VehicleInkPresentation308.Phase.Idle)
            {
                var a = profile.Arrival;
                if (a.Enabled && scale > 0f && Vehicle.isActiveAndEnabled)
                {
                    Vehicle.BeginVisualSettle308(a.DropMeters * Mathf.Min(1f, scale), a.FallSeconds, a.Hz, a.Damping, a.SettleSeconds);
                    ArrivalSettles308++;
                }
                Gesture.NotifyVehicleSetDown308(FlatDistanceToCar308());
            }
            else if (phase == VehicleInkPresentation308.Phase.Cover && previous != VehicleInkPresentation308.Phase.Cover)
            {
                var a = profile.Arrival;
                if (a.Enabled && scale > 0f && Vehicle.isActiveAndEnabled)
                {
                    Vehicle.BeginVisualLift308(a.RecallLiftMeters * Mathf.Min(1f, scale), a.RecallLiftSeconds);
                    RecallLifts308++;
                }
            }
        }

        float FlatDistanceToCar308()
        {
            if (Walker == null || Walker.Body == null || Vehicle == null) return float.PositiveInfinity;
            Vector3 delta = Walker.Body.transform.position - Vehicle.transform.position; delta.y = 0f;
            return delta.magnitude;
        }
    }
}
