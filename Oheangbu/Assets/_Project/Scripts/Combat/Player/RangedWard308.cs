namespace Oheangbu.Combat
{
    // #308 D308-115 (SPEC-DEMO-SUMMON-COMBAT "D308-115 개별 행동", 옴): one owner may stand between the player and an enemy
    // ranged strike. EnemyStrike308.Deliver asks it after the strike rule and before the player takes the hit, and only for a
    // ranged strike that would really land (PlayerVitals.WouldLand308). true = the strike is stopped: no damage, no hit event.
    // A block gives the player nothing (no ink, no groggy, no reward) — the owner only spends its own charge.
    public interface IRangedWard308
    {
        bool TryBlockRanged308(EnemyVitals attacker, float amount, IncomingDamageKind kind);
    }
}
