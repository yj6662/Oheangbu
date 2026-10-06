using UnityEngine;

namespace Oheangbu.Combat
{
    // #308 WP-07: the outgoing half of EnemyModifierState308. The amount of the strike an enemy delivers now, after its
    // weaken / dull modifiers; the one-strike dull charge is used up by the call.
    // This is the modifier SPEC-SPELL-120-308 section 10 reserves inside EnemyStrike308.Deliver. The package could not
    // edit that frozen file; the phase B integration put the call into the doorway (one line), so the dull and weaken
    // glyphs do change what the player takes from a marked enemy.
    // It only ever lowers what the player takes from that enemy; it gives the player nothing (no heal, no ink).
    public static class EnemyStrikeModifiers308
    {
        public static float Apply(EnemyVitals attacker, float amount)
        {
            return attacker != null && amount > 0f ? attacker.Modifiers.TakeStrike(amount, Time.time) : amount;
        }
    }
}
