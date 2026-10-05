using Oheangbu.App.World.UI;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #308 HUD (SPEC-HUD-LIQUID-308 §3.4 / §5.1): what G would do right now, for the vehicle mark. Read-only: only public
    // members of the summon are asked (ShouldRecallOnToggle308, Calling, GestureProgress, RecallGesture, InkBusy308,
    // FrontSpotInBossField308) plus this session's own boss-field test. The vehicle classes and the other session files are not
    // changed. The presenter calls this a few times per second (the boss-field test walks polygons). No fields, no static state.
    public sealed partial class WorldMacroPlaytestSession
    {
        /// <summary>state = what the mark shows; outGlyph = the "out, G recalls" glyph (kept by a busy / refused state);
        /// busy01 = call-stroke progress while Busy.</summary>
        public void ReadVehicleHud308(out VehicleHud308 state, out bool outGlyph, out float busy01)
        {
            state = VehicleHud308.Hidden; outGlyph = false; busy01 = 0f;
            var summon = vehicleSummon308 != null ? vehicleSummon308 : DemoEscortSummon;
            // hidden before the first commission (VehicleAvailable) and in scenes without a summon
            if (summon == null || !summon.isActiveAndEnabled || Progress == null || Progress.ledger == null || !VehicleAvailable) return;
            if (Walker != null && Walker.Seated) { state = VehicleHud308.Seated; outGlyph = true; return; }
            bool near = summon.ShouldRecallOnToggle308();   // the car is out and within RecallToggleDistance: G recalls
            outGlyph = near;
            if (summon.Calling || summon.InkBusy308)
            {
                state = VehicleHud308.Busy;
                if (summon.Calling) { busy01 = summon.GestureProgress; outGlyph = summon.RecallGesture; }
                return;
            }
            if (near) { state = VehicleHud308.OutNear; return; }
            // G would summon: the feet or the front spot in a boss field = nothing will happen (D308-8b). The mark going dry is
            // the only advance notice; a recall is not a summon and is never refused by a field.
            if (Walker != null && Walker.Body != null)
            {
                Vector3 feet = Walker.Body.transform.position;
                if (InVehicleBossField308(feet, 0f, out _) || summon.FrontSpotInBossField308(feet, Walker.Body.transform.forward, out _))
                { state = VehicleHud308.Refused; return; }
            }
            state = VehicleHud308.CanCall;
        }
    }
}
