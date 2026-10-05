// PURE308
namespace Oheangbu.App
{
    // Full bind of the wood giyeok glyph: an ordinary enemy cannot act or move, a boss is only slowed
    // (canonical text: boss = slowed). SPEC-SPELL-120-308 WP-00: "boss" is the enemy's data (EnemyVitals.IsBoss).
    public static class RootBindRule308
    {
        public static void Resolve(bool isBoss, float bossSpeed, out float movementScale, out bool blocksActions)
        {
            movementScale = isBoss ? bossSpeed : 0f;
            blocksActions = !isBoss;
        }
    }
}
