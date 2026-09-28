using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.Combat
{
    public enum EnemyArchetype { NeutralMelee, FireRanged, WoodVine, WaterRanged, MetalSoldier, EarthHeavy }
    public enum EnemyAttackDelivery { MeleeArc, HomingProjectile, AimedProjectile, GroundEruption }

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
