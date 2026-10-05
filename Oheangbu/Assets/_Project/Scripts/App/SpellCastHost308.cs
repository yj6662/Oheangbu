using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // What CombatLoopWiring lends to an effect handler (SPEC-SPELL-120-308 section 5). The implementation is the partial
    // CombatLoopWiring.Spell308.cs; the Plan* bodies are today's judgement code, unchanged.
    public interface ISpellCastHost
    {
        Transform Player { get; }
        Vector3 PlayerPosition { get; }
        Vector3 PlayerForward { get; }
        PlayerVitals PlayerVitals { get; }
        InkPool Ink { get; }
        CombatConfigSO Config { get; }
        IReadOnlyList<EnemyVitals> Targets { get; }
        SpellBuffState308 Buffs { get; }                    // shared self-buff timers (cleared with the effects)

        EnemyVitals AimedTarget();
        Vector3 AimPoint(float range);
        bool TargetVisible(EnemyVitals target, Vector3 origin);
        float DamageScale(Element element);

        // Today's judgement bodies. feedAdapter = false when the effect presents through ISpellPresenter.
        CastPlan PlanSingle(in SpellCast cast, bool feedAdapter);
        CastPlan PlanCone(in SpellCast cast, bool feedAdapter);
        CastPlan PlanCircle(in SpellCast cast, bool feedAdapter);
        CastPlan PlanPath(in SpellCast cast, bool feedAdapter);
        // shotWeights = each shot's share of the cast power (any positive numbers, normalised by their sum); null = equal shares.
        CastPlan PlanVolley(in SpellCast cast, bool feedAdapter, IReadOnlyList<float> shotWeights = null);

        // One more scheduled hit on a plan (damage clock + plan record). PlannedHit.AttackId identifies it in OnEnemyHit.
        PlannedHit ScheduleHit(CastPlan plan, EnemyVitals target, float impactTime, float power);
        // Immediate player-side hit (PlayerDirect, Companion, Retaliation or Harmony source) with the usual life and sight checks.
        EnemyDamageResult ApplyDirectHit(EnemyVitals target, uint life, float power, Vector3 origin, AttackProvenance attack, char letter, bool piercesActors = false);
        // Damage over time (PersistentSpell source): never feeds the five-element completion or groggy.
        EnemyDamageResult ApplyPersistentHit(EnemyVitals target, float power, Vector3 origin, AttackProvenance attack, char letter);
        // The stroke adapter skips its default pattern for this cast.
        void PresentationOwned();
    }

    // Who knows which final consonants the player owns. The rule (ledger id, evidence) is data: SpellBookSO._unlocks.
    public interface ISpellUnlocks
    {
        bool FinalUnlocked(SpellUnlockRule rule);
    }

    // Scenes without a campaign session: nothing behind a final consonant opens.
    public sealed class LockedSpellUnlocks308 : ISpellUnlocks
    {
        public bool FinalUnlocked(SpellUnlockRule rule) => false;
    }
}
