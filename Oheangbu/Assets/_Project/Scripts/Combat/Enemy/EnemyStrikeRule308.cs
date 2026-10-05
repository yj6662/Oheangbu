// PURE308
namespace Oheangbu.Combat
{
    // #308 the arithmetic of the enemy -> player doorway (EnemyStrike308.Deliver), apart from the Unity objects so the
    // offline runner executes the very lines the doorway runs:
    //   WP-07  the attacker's outgoing modifiers scale the amount (weaken: every strike while it runs; dull: the next
    //          strike, once, used up by this call),
    //   WP-13  what veils the attacker's strikes: a haze it stands in (a share of its strikes miss), cover the player
    //          stands behind (its ranged strikes are stopped).
    // An attacker nobody touched (no modifiers at all, or none of these) delivers its amount bit for bit as it came in.
    // Nothing in here gives the player anything: the amount only ever goes down, or the strike does not arrive.
    public static class EnemyStrikeRule308
    {
        // false = nothing reaches the player (the strike missed in a haze, or cover stopped a ranged strike).
        // delivered = what the player is asked to take when the answer is true.
        public static bool Resolve(EnemyModifierState308 attacker, float amount, bool ranged, float now, out float delivered)
        {
            delivered = amount;
            if (attacker == null) return true;
            if (amount > 0f) delivered = attacker.TakeStrike(amount, now);
            if (delivered > 0f && attacker.Veil.Misses(ranged, now)) return false;
            return true;
        }
    }
}
