using UnityEngine;

namespace Oheangbu.App
{
    // #308 WP-17 (D308-121 결계): the player side of the domains. The state is read at the wiring's own seams (ink regen
    // here, the sword form's drain in SwordFormEffect308); the domain handlers write it through IPlayerDomainHost308.
    public sealed partial class CombatLoopWiring : IPlayerDomainHost308
    {
        readonly PlayerDomainState308 _playerDomain308 = new PlayerDomainState308();

        public PlayerDomainState308 PlayerDomain308 => _playerDomain308;
        bool IPlayerDomainHost308.GuardStands308 => _judge != null && _judge.GuardStands(Time.time);
    }
}
