using Oheangbu.Combat;

namespace Oheangbu.App
{
    // #308 SPEC-UI-EQUIPMENT-308: read-only view of the combat config for the 소지품 status column (먹 회복). No rule lives here.
    public sealed partial class CombatLoopWiring
    {
        /// <summary>The shared combat config this wiring was built with (read only; the asset is never changed here).</summary>
        public CombatConfigSO Config308 => _config;
    }
}
