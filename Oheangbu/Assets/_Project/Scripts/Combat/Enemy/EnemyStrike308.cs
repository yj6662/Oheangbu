namespace Oheangbu.Combat
{
    // #308 (SPEC-SPELL-120-308 section 10): the single doorway of enemy -> player damage. Every place an enemy hurts the
    // player (EnemyController x2, EnemyController.Authored, CheongryongCombatController, SouthGateGeneralController) calls
    // this instead of PlayerVitals.TakeAttackDamage, so the hit carries its attacker (retaliation effects need it).
    // The two modifiers section 10 reserved for this method, and nowhere else:
    //   WP-07  the attacker's outgoing modifiers (attack lowered for a while, next attack dulled once) scale the amount;
    //   WP-13  what veils the attacker's strikes: a haze it stands in (a share of its strikes miss), cover the player
    //          stands behind (its ranged strikes are stopped).
    // An attacker without any modifier (every enemy until a WP-07 or WP-13 spell touched it) is delivered exactly as before.
    // Both only ever take away from what the player receives; neither gives the player anything.
    public static class EnemyStrike308
    {
        // true = the player really took damage (false: no player, dodge invulnerability, already dead, non-positive amount,
        // or the strike missed).
        public static bool Deliver(PlayerVitals player, EnemyVitals attacker, float amount, IncomingDamageKind kind)
        {
            if (player == null) return false;
            bool ranged = kind == IncomingDamageKind.Ranged || kind == IncomingDamageKind.ElementalRanged;
            // WP-07 (weaken, dull: the dull charge is used up here) and WP-13 (haze miss, cover): the pure rule, line for line
            // what stood here before, so the offline runner checks what the player really takes (EnemyStrikeRule308).
            if (!EnemyStrikeRule308.Resolve(attacker != null ? attacker.Modifiers : null, amount, ranged, UnityEngine.Time.time, out float delivered)) return false;
            return player.TakeAttackDamage(delivered, kind, attacker);
        }
    }
}
