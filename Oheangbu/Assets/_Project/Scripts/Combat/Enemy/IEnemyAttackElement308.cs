using Oheangbu.Core.Domain;

namespace Oheangbu.Combat
{
    // #308 D308-124: the one element an enemy's attacks carry, the element its parry answer is judged against. Fixed by the
    // attack make-up, never by the attack in progress. The counter-element sword strike (SwordFormEffect308) and the
    // weak-point counter scale (CounterElementRule308) both read it through EnemyVitals.TryAttackElement.
    // false = none: mixed elements, the yellow dragon, an enemy whose attacks carry no element.
    public interface IEnemyAttackElement308
    {
        bool TryAttackElement(out Element element);
    }
}