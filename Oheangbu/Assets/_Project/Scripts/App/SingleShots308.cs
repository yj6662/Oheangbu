using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // Unity half shared by the WP-11 single shots whose hit is decided in flight (single.ballistic, single.homing).
    // The wiring's single judgement schedules a hit that is certain to land (the lock-on guidance of before #308). These
    // two handlers decide the hit when the shot arrives, so they hold the hit themselves and land it through the host's
    // direct hit. The stroke adapter keeps presenting the shot exactly as it did (the same target, the same flight clock):
    // it is fed the wiring's own single plan with a power of zero, which schedules nothing that can hurt
    // (EnemyVitals.TakeDamage ignores an amount of zero).
    public static class SingleShots308
    {
        // Seconds a watch for a guided shot waits beyond the shot's flight limit. The shot's age is summed frame by frame
        // and the watch reads the wall clock: this keeps the watch alive in the frame the two disagree by a rounding step.
        // A tolerance of bookkeeping, not a balance number.
        public const float WatchSlack = .5f;

        // The projectile speed the wiring's single judgement uses.
        public static float Speed(ISpellCastHost host, in SpellCast cast)
        {
            float speed = host.Config != null ? host.Config.SpellProjectileSpeed : 1f;
            return Mathf.Max(1f, speed * cast.SpeedMul);
        }

        // Where a shot leaves the caster and where sight is judged from (the wiring's own origin for scheduled hits).
        public static Vector3 Muzzle(ISpellCastHost host) => host.PlayerPosition + Vector3.up * .4f;

        // The flight plan of this cast without any damage of its own: the wiring's single plan with a power of zero. The
        // stroke adapter flies its pattern along it when feedAdapter is true (false = the caster presents the shot through
        // ISpellPresenter); either way the plan is broadcast, so listeners still see one planned shot.
        public static CastPlan Present(ISpellCastHost host, in SpellCast cast, bool feedAdapter = true)
        {
            var shown = new SpellCast(cast.Letter, cast.Kind, cast.Element, 0f, cast.Area, cast.SpeedMul, cast.HoldScale, cast.Brush, cast.Brush01);
            return host.PlanSingle(shown, feedAdapter);
        }

        // The held hit as the wiring would record a scheduled one: who, when it is expected, the power it carries (the
        // equipment scale is taken at cast time, as a scheduled hit takes it) and the identity it lands under.
        public static PlannedHit HeldHit(ISpellCastHost host, in SpellCast cast, EnemyVitals target, float expectedAt, in AttackProvenance attack)
        {
            return new PlannedHit { Target = target, ImpactTime = expectedAt, Power = cast.Power * host.DamageScale(cast.Element), AttackId = attack.AttackId };
        }

        // One attack identity for the held hit: a direct player hit of the cast's element, like a scheduled one.
        public static AttackProvenance Identity(ISpellCastHost host, Element element)
        {
            Object caster = host.PlayerVitals != null ? (Object)host.PlayerVitals : host.Player;
            return AttackProvenance.Create(caster, DamageSource.PlayerDirect, element);
        }

        // Lands the held hit now. The host's direct hit checks the life and the line of sight. power = PlannedHit.Power of
        // HeldHit: the equipment scale is already inside (the host's direct hit applies none).
        public static EnemyDamageResult Land(ISpellCastHost host, EnemyVitals target, uint life, float power,
            Vector3 sightOrigin, AttackProvenance attack, char letter)
        {
            return host.ApplyDirectHit(target, life, power, sightOrigin, attack, letter);
        }
    }
}
