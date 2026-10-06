using System.Threading;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.Combat
{
    // #308 (appended, TEST): Companion = extra shot of a companion buff, Retaliation = damage returned to a melee attacker,
    // Harmony = detonation of an installed mark. None of the three feeds the five-element completion (PlayerDirect only).
    public enum DamageSource { Unknown, PlayerDirect, Enemy, Summon, Harvest, FiveElementBonus, PersistentSpell, Companion, Retaliation, Harmony }

    // IDs identify actual scheduled attacks, not render frames or VFX instances.
    public readonly struct AttackProvenance
    {
        private static long _sequence;
        public readonly long AttackId;
        public readonly Object Instigator;
        public readonly DamageSource Source;
        public readonly Element? Element;
        public AttackProvenance(long attackId, Object instigator, DamageSource source, Element? element)
        { AttackId = attackId; Instigator = instigator; Source = source; Element = element; }
        public static AttackProvenance Create(Object instigator, DamageSource source, Element? element) =>
            new AttackProvenance(Interlocked.Increment(ref _sequence), instigator, source, element);

        // With domain reload disabled the counter would carry over between play sessions.
        // Kept in a nested class so the attribute is certain to be discovered.
        private static class PlaySession
        {
            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            private static void Reset() => _sequence = 0;
        }
    }

    public readonly struct EnemyDamageResult
    {
        public readonly EnemyVitals Target;
        public readonly AttackProvenance Attack;
        public readonly float AppliedDamage;
        public readonly float CompletionBonus;
        public readonly bool Killed;
        public EnemyDamageResult(EnemyVitals target, AttackProvenance attack, float appliedDamage, float completionBonus, bool killed)
        { Target = target; Attack = attack; AppliedDamage = appliedDamage; CompletionBonus = completionBonus; Killed = killed; }
    }

    public readonly struct ParryImpactResult
    {
        public readonly ParryOutcome Outcome;
        public readonly Element GuardElement;
        public readonly Vector3 Point;
        public readonly AttackProvenance Attack;
        public ParryImpactResult(ParryOutcome outcome, Element guardElement, Vector3 point, AttackProvenance attack)
        { Outcome = outcome; GuardElement = guardElement; Point = point; Attack = attack; }
    }
}
