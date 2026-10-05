namespace Oheangbu.App.World.UI
{
    /// <summary>#308 what G would do right now (SPEC-HUD-LIQUID-308 §3.4). Read from the summon's public members by
    /// WorldMacroPlaytestSession.ReadVehicleHud308; nothing here changes the vehicle.</summary>
    public enum VehicleHud308
    {
        Hidden = 0,     // before the first commission, or no summon in the scene
        CanCall = 1,    // recalled, or further than the recall distance: G summons it to the front
        OutNear = 2,    // out and near: G recalls it
        Refused = 3,    // the feet or the front spot lie in a boss field: G does nothing (D308-8b)
        Busy = 4,       // call stroke or ink presentation running
        Seated = 5,     // riding
    }

    /// <summary>Atlas glyph of a mark = the shader's element code (UI/InkVessel308 TEXCOORD3.x).</summary>
    public enum HudGlyph308 { Dodge = 2, Jump = 3, Vehicle = 4, VehicleOut = 5 }

    /// <summary>#308 inputs of the three marks, gathered by the presenter from PlayerMotor (the same gate predicates the input
    /// handlers use) and the session's vehicle state. Plain data: the HUD only reads.</summary>
    public struct HudActionState308
    {
        public bool HasDodge;           // the motor has a DodgeAction
        public bool DodgeGateOpen;      // PlayerMotor.DodgeGateOpen(): what OnDodge checks before asking DodgeAction
        public bool Dodging;            // the dash itself (the gate is closed by it; the cooldown keeps showing)
        public float DodgeCooldown01;   // 1 = ready; share of the cooldown that has passed
        public bool HasJump;            // a locomotion profile with a Jump action
        public bool JumpGateOpen;       // PlayerMotor.JumpGateOpen(): what OnJump checks
        public bool Airborne;
        public bool Seated;             // riding the car: the motor is suspended
        public VehicleHud308 Vehicle;
        public bool VehicleOutGlyph;    // the car is out and near (the glyph a refused / busy state keeps)
        public float VehicleBusy01;     // call-stroke progress while Busy (0 when unknown)
    }

    /// <summary>#308 how one mark is drawn: hidden (not drawn; its place stays empty), wet = usable, dry = not now,
    /// Fill = re-wetting along the stroke (cooldown / gesture progress). Snap = the mark dries at once (the action was just used).</summary>
    public struct HudMarkView308
    {
        public bool Hidden;
        public HudGlyph308 Glyph;
        public bool Wet;
        public float Fill;
        public bool Snap;
    }

    /// <summary>#308 the one place that decides the marks (SPEC §3.6). Pure: testable as a table in Edit Mode (checks:sim).</summary>
    public static class HudActionRules308
    {
        public static void Evaluate(in HudActionState308 s, out HudMarkView308 dodge, out HudMarkView308 jump, out HudMarkView308 vehicle)
        {
            dodge = Dodge(in s); jump = Jump(in s); vehicle = Vehicle(in s);
        }

        // hidden: no DodgeAction | dry: riding | dry: gate closed (drawing, airborne, sitting, crouch without roll, input blocked)
        // | dry + re-wetting: cooling down (the dash included) | wet: ready
        public static HudMarkView308 Dodge(in HudActionState308 s)
        {
            var v = new HudMarkView308 { Glyph = HudGlyph308.Dodge };
            if (!s.HasDodge) { v.Hidden = true; return v; }
            if (s.Seated) return v;
            if (!s.DodgeGateOpen && !s.Dodging) return v;
            if (s.DodgeCooldown01 < 1f) { v.Fill = s.DodgeCooldown01 < 0f ? 0f : s.DodgeCooldown01; v.Snap = true; return v; }
            if (!s.DodgeGateOpen) return v;
            v.Wet = true; return v;
        }

        // hidden: no locomotion profile | dry: riding | dry at once: airborne | dry: gate closed (crouch, sitting, dodging,
        // drawing, pulling, input blocked) | wet: ready
        public static HudMarkView308 Jump(in HudActionState308 s)
        {
            var v = new HudMarkView308 { Glyph = HudGlyph308.Jump };
            if (!s.HasJump) { v.Hidden = true; return v; }
            if (s.Seated) return v;
            if (s.Airborne) { v.Snap = true; return v; }
            if (!s.JumpGateOpen) return v;
            v.Wet = true; return v;
        }

        // the mark shows what G would do: wheel = summon, open wheel with the return hook = recall, dry = nothing will happen
        public static HudMarkView308 Vehicle(in HudActionState308 s)
        {
            var v = new HudMarkView308 { Glyph = s.VehicleOutGlyph ? HudGlyph308.VehicleOut : HudGlyph308.Vehicle };
            switch (s.Vehicle)
            {
                case VehicleHud308.Hidden: v.Hidden = true; break;
                case VehicleHud308.CanCall: v.Glyph = HudGlyph308.Vehicle; v.Wet = true; break;
                case VehicleHud308.OutNear: v.Glyph = HudGlyph308.VehicleOut; v.Wet = true; break;
                case VehicleHud308.Refused: break;                                   // the glyph it had, dry
                case VehicleHud308.Busy: v.Fill = s.VehicleBusy01 < 0f ? 0f : s.VehicleBusy01 > 1f ? 1f : s.VehicleBusy01; break;
                case VehicleHud308.Seated: v.Glyph = HudGlyph308.VehicleOut; break;
            }
            return v;
        }
    }
}
