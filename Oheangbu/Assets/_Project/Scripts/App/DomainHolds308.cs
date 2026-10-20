using System.Collections.Generic;
using Oheangbu.Combat;

namespace Oheangbu.App
{
    // #308 WP-17: what one domain handler has hung on enemies (EnemyDomainState308), like VeilHolds308 for the veils. The
    // handler refreshes an entry every frame the enemy is inside and releases it the frame it leaves; this list remembers
    // who carries what, so a domain that ends and a clear (death, rest, scene leave, disable) take exactly those back.
    public sealed class DomainHolds308
    {
        struct Held { public EnemyVitals Enemy; public long Owner; }
        readonly List<Held> _held = new List<Held>();

        public int Count => _held.Count;

        // The writer's answer decides: an entry that changed nothing is not remembered.
        public void Hold(EnemyVitals enemy, long owner, bool written)
        {
            if (enemy == null || !written) return;
            for (int i = 0; i < _held.Count; i++)
                if (_held[i].Enemy == enemy && _held[i].Owner == owner) return;
            _held.Add(new Held { Enemy = enemy, Owner = owner });
        }

        public bool Holds(EnemyVitals enemy, long owner)
        {
            for (int i = 0; i < _held.Count; i++)
                if (_held[i].Enemy == enemy && _held[i].Owner == owner) return true;
            return false;
        }

        // The entries of that owner on that enemy end now.
        public void Release(EnemyVitals enemy, long owner)
        {
            for (int i = 0; i < _held.Count; i++)
            {
                if (_held[i].Enemy != enemy || _held[i].Owner != owner) continue;
                if (enemy != null) enemy.Modifiers.Domain.Remove(owner);
                _held.RemoveAt(i);
                return;
            }
        }

        // The domain is gone: nobody keeps its entries.
        public void ReleaseAll(long owner)
        {
            for (int i = _held.Count - 1; i >= 0; i--)
            {
                if (_held[i].Owner != owner) continue;
                if (_held[i].Enemy != null) _held[i].Enemy.Modifiers.Domain.Remove(owner);
                _held.RemoveAt(i);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _held.Count; i++)
                if (_held[i].Enemy != null) _held[i].Enemy.Modifiers.Domain.Remove(_held[i].Owner);   // null = destroyed with its scene
            _held.Clear();
        }
    }
}
