// PURE308
using Oheangbu.Core.Domain;

namespace Oheangbu.Combat
{
    // #308 D308-124: inside the weak-point window (groggy in full bloom) a spell whose element overcomes the enemy's attack
    // element hurts more. The enemy's element is the one its parry answer is judged against (IEnemyAttackElement308), fixed
    // by its attack make-up; an enemy without one (mixed, the yellow dragon, a melee-only legacy enemy) is never countered.
    public static class CounterElementRule308
    {
        // The factor one hit gets. 1 = no change: the window is shut, the source is not a spell of the player, the hit or
        // the enemy has no element, the hit's element does not overcome it, or the data multiplier is unusable.
        public static float Scale(bool windowOpen, bool spellSource, Element? hit, bool enemyHasElement, Element enemy, float multiplier)
        {
            if (!windowOpen || !spellSource || !hit.HasValue || !enemyHasElement) return 1f;
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier <= 0f) return 1f;
            return ElementRelations.Overcomes(hit.Value, enemy) ? multiplier : 1f;
        }
    }
}