namespace Oheangbu.Combat
{
    // #306 contract (SPEC-PLAYTEST-306 #8): what the HUD needs to draw an enemy health stroke under the lock-on reticle or a boss
    // bar. Track C implements it on EnemyVitals (per-enemy profile); track U reads it from the lock target. No element colour.
    public interface IHealthBarSource306
    {
        float Hp01 { get; }
        bool Alive { get; }
        string DisplayName { get; }   // boss bar label; empty for ordinary enemies
        bool IsBoss { get; }          // boss bar at the bottom while engaged, instead of the reticle stroke
    }
}
