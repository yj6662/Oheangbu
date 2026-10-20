using Oheangbu.Core.Domain;

namespace Oheangbu.Combat
{
    // #308 D308-124: this enemy's attack element (IEnemyAttackElement308). The authored profile's element when that attack is
    // elemental, the legacy ranged element otherwise (a legacy melee hit carries none). A neutral profile or a legacy
    // melee-only enemy = none.
    public sealed partial class EnemyController : IEnemyAttackElement308
    {
        public bool TryAttackElement(out Element element)
        {
            element = default;
            var profile = AttackProfile;
            if (profile != null) { element = profile.Element; return profile.Elemental; }
            if (EffectiveAttackMode == AttackMode.MeleeOnly) return false;
            element = RangedElement;
            return true;
        }
    }
}