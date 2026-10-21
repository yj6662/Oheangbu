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
        // #308 WP-16 (D308-114): a hit landed by the caster's body or weapon (the sword form, the riposte), not a spell shot.
        // A raised guard reduces spell shots only, so a melee hit passes it. false for every attack made before this flag.
        public readonly bool Melee;
        // #308 D308-123: damage that follows an earlier hit of the same attack (the icicle's shatter). It never counts toward
        // the five-element completion, whether or not the first hit did. false for every attack made before this flag.
        public readonly bool NoCredit;
        public AttackProvenance(long attackId, Object instigator, DamageSource source, Element? element)
            : this(attackId, instigator, source, element, false, false) { }
        public AttackProvenance(long attackId, Object instigator, DamageSource source, Element? element, bool melee)
            : this(attackId, instigator, source, element, melee, false) { }
        public AttackProvenance(long attackId, Object instigator, DamageSource source, Element? element, bool melee, bool noCredit)
        { AttackId = attackId; Instigator = instigator; Source = source; Element = element; Melee = melee; NoCredit = noCredit; }
        public static AttackProvenance Create(Object instigator, DamageSource source, Element? element) =>
            new AttackProvenance(Interlocked.Increment(ref _sequence), instigator, source, element);
        public static AttackProvenance CreateMelee(Object instigator, DamageSource source, Element? element) =>
            new AttackProvenance(Interlocked.Increment(ref _sequence), instigator, source, element, true);
        // #308 D308-123: the last id handed out. Ids only grow, so an id above a value read before some work and not above one
        // read after it was handed out by that work.
        public static long Issued => Interlocked.Read(ref _sequence);

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
