using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.Combat
{
    public enum EnemyArchetype { NeutralMelee, FireRanged, WoodVine, WaterRanged, MetalSoldier, EarthHeavy }
    public enum EnemyAttackDelivery { MeleeArc, HomingProjectile, AimedProjectile, GroundEruption, Charge }

    [CreateAssetMenu(menuName = "Oheangbu/Combat/Enemy Attack Profile", fileName = "EnemyAttackProfile")]
    public sealed class EnemyAttackProfileSO : ScriptableObject
    {
        public EnemyArchetype Archetype;
        public EnemyController.AttackMode Mode = EnemyController.AttackMode.MeleeOnly;
        public EnemyAttackDelivery Delivery;
        public bool Elemental;
        public Element Element = Element.Wood;
        [Min(.05f)] public float Telegraph = .8f;
        [Min(.05f)] public float Recovery = .4f;
        [Min(0f)] public float Damage = 15f;
        [Min(.1f)] public float Range = 2.4f;
        [Min(.1f)] public float ProjectileSpeed = 10f;
        [Min(.1f)] public float ImpactRadius = .8f;
        [Range(1, 360)] public float ArcDegrees = 110f;
        public Vector2 CooldownRange = new Vector2(1.2f, 2.2f);
        [Min(.1f)] public float MovementSpeedHint = 2.6f;
        [Min(.1f)] public float PreferredDistanceHint = 1.7f;
        // #308 D308-125 적용 3 (Charge only, TEST): the body runs ProjectileSpeed m/s straight along the line it locked ChargeLockLead s
        // before the telegraph ends, for the player's distance then + ChargeOvershoot, cut to ChargeMinTravel..Range; the swept line
        // hits once within ImpactRadius. No ChargeMinTravel of straight path ahead = no charge; stopped by a wall = ChargeWallRecovery.
        [Min(0f)] public float ChargeLockLead = .25f;
        [Min(0f)] public float ChargeOvershoot = 2.5f;
        [Min(.1f)] public float ChargeMinTravel = 4f;
        [Min(.05f)] public float ChargeWallRecovery = 2f;
        // #308 D308-125 적용 4 (Charge only, TEST): what a charger does while it cannot start its run (묵's circle, no straight path):
        // it closes in and lands this melee arc once, then goes back to the charge. null = it only waits.
        public EnemyAttackProfileSO Fallback;
        // #308 D308-125 적용 5 (GroundEruption only, TEST): the ember patch each burst leaves where it landed, hit or miss
        // (a parried burst leaves none). null = nothing stays on the ground.
        public EnemyGroundPatchSO Patch;
        // #308 D308-129 (토론 17, TEST): a hit of this profile that lands (past a dodge's invulnerability) pushes the player
        // ShoveDistance m over ShoveSeconds s along the attacker's facing (PlayerMotor.Shove308; 숨 keeps the player in place).
        // 0 = no push. The App reads the attacker's current profile, so a charge's Fallback bite pushes only with its own values.
        [Min(0f)] public float ShoveDistance;
        [Min(0f)] public float ShoveSeconds;

        // TEST starting data only. Each asset is independent; shared CombatConfig is never edited.
        public void ApplyDefaults(EnemyArchetype archetype)
        {
            Archetype = archetype; Mode = EnemyController.AttackMode.MeleeOnly; Delivery = EnemyAttackDelivery.MeleeArc;
            Elemental = archetype != EnemyArchetype.NeutralMelee; Element = Element.Wood;
            Telegraph = .8f; Recovery = .5f; Damage = 15; Range = 2.4f; ProjectileSpeed = 10; ImpactRadius = .8f;
            ArcDegrees = 110; CooldownRange = new Vector2(1.2f, 2.2f); MovementSpeedHint = 2.6f; PreferredDistanceHint = 1.7f;
            switch (archetype)
            {
                case EnemyArchetype.FireRanged:
                    Element = Element.Fire; Mode = EnemyController.AttackMode.RangedOnly; Delivery = EnemyAttackDelivery.HomingProjectile;
                    Telegraph = 1.2f; Damage = 12; Range = 12; ProjectileSpeed = 12; PreferredDistanceHint = 9; break;
                case EnemyArchetype.WoodVine:
                    Element = Element.Wood; Mode = EnemyController.AttackMode.RangedOnly; Delivery = EnemyAttackDelivery.GroundEruption;
                    Telegraph = 1.35f; Recovery = .85f; Damage = 17; Range = 10; ImpactRadius = 1.35f;
                    MovementSpeedHint = 1.8f; PreferredDistanceHint = 7; CooldownRange = new Vector2(1.8f, 2.6f); break;
                case EnemyArchetype.WaterRanged:
                    Element = Element.Water; Mode = EnemyController.AttackMode.RangedOnly; Delivery = EnemyAttackDelivery.AimedProjectile;
                    Telegraph = .95f; Recovery = .65f; Damage = 11; Range = 14; ProjectileSpeed = 9; ImpactRadius = 1;
                    PreferredDistanceHint = 10; CooldownRange = new Vector2(1.1f, 1.8f); break;
                case EnemyArchetype.MetalSoldier:
                    Element = Element.Metal; Telegraph = .65f; Recovery = .8f; Range = 3.4f; ArcDegrees = 36; Damage = 18;
                    MovementSpeedHint = 2.3f; PreferredDistanceHint = 2.4f; break;
                case EnemyArchetype.EarthHeavy:
                    Element = Element.Earth; Telegraph = 1.5f; Recovery = 1.2f; Range = 3.1f; ArcDegrees = 150; Damage = 24;
                    MovementSpeedHint = 1.5f; PreferredDistanceHint = 2.2f; CooldownRange = new Vector2(2f, 2.8f); break;
            }
        }

        public bool TryValidate(out string error)
        {
            if (!System.Enum.IsDefined(typeof(EnemyArchetype), Archetype) || !System.Enum.IsDefined(typeof(EnemyAttackDelivery), Delivery) ||
                !System.Enum.IsDefined(typeof(Element), Element) || !System.Enum.IsDefined(typeof(EnemyController.AttackMode), Mode))
            { error = "Unknown enum value"; return false; }
            if (Mode == EnemyController.AttackMode.LegacyDistance ||
                (Mode == EnemyController.AttackMode.MeleeOnly) != (Delivery == EnemyAttackDelivery.MeleeArc))
            { error = "Authored mode must match delivery"; return false; }
            float[] positive = { Telegraph, Recovery, Range, ProjectileSpeed, ImpactRadius, ArcDegrees, MovementSpeedHint, PreferredDistanceHint, CooldownRange.x, CooldownRange.y };
            foreach (float v in positive) if (!float.IsFinite(v) || v <= 0) { error = "Attack values must be finite and positive"; return false; }
            if (!float.IsFinite(Damage) || Damage < 0 || ArcDegrees > 360 || CooldownRange.y < CooldownRange.x || PreferredDistanceHint > Range)
            { error = "Invalid damage, arc, cooldown or preferred distance"; return false; }
            if (Delivery == EnemyAttackDelivery.Charge && (!float.IsFinite(ChargeLockLead) || ChargeLockLead < 0 || !float.IsFinite(ChargeOvershoot) ||
                ChargeOvershoot < 0 || !float.IsFinite(ChargeMinTravel) || ChargeMinTravel <= 0 || ChargeMinTravel > Range ||
                !float.IsFinite(ChargeWallRecovery) || ChargeWallRecovery <= 0))
            { error = "Invalid charge lock, overshoot, minimum travel or wall recovery"; return false; }
            if (Fallback != null && (Delivery != EnemyAttackDelivery.Charge || Fallback == this || Fallback.Delivery != EnemyAttackDelivery.MeleeArc ||
                Fallback.Fallback != null || !(Fallback.Range <= Range) || !Fallback.TryValidate(out _)))
            { error = "A fallback belongs to a charge and must be a valid melee arc within its range, without a fallback of its own"; return false; }
            if (Patch != null && Delivery != EnemyAttackDelivery.GroundEruption) { error = "A ground patch belongs to a ground eruption"; return false; }
            if (Patch != null && !Patch.TryValidate(out string patchError)) { error = patchError; return false; }
            if (!float.IsFinite(ShoveDistance) || ShoveDistance < 0 || !float.IsFinite(ShoveSeconds) || ShoveSeconds < 0 || (ShoveDistance > 0) != (ShoveSeconds > 0))
            { error = "A shove needs both a distance and a time (or neither)"; return false; }
            error = null; return true;
        }
    }

    public readonly struct EnemyAttackCue
    {
        public readonly AttackProvenance Attack;
        public readonly EnemyAttackDelivery Delivery;
        public readonly Vector3 Point;
        public readonly float Duration;
        public readonly float Radius;
        public EnemyAttackCue(AttackProvenance attack, EnemyAttackDelivery delivery, Vector3 point, float duration, float radius)
        { Attack = attack; Delivery = delivery; Point = point; Duration = duration; Radius = radius; }
    }

    public readonly struct EnemyAttackImpact
    {
        public readonly AttackProvenance Attack;
        public readonly Vector3 Point;
        public readonly ParryOutcome Outcome;
        public readonly float AppliedDamage;
        public readonly bool InShape;
        public EnemyAttackImpact(AttackProvenance attack, Vector3 point, ParryOutcome outcome, float damage, bool inShape)
        { Attack = attack; Point = point; Outcome = outcome; AppliedDamage = damage; InShape = inShape; }
    }
}
