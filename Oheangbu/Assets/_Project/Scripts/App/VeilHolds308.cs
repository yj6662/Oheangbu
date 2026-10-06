using System.Collections.Generic;
using Oheangbu.Combat;

namespace Oheangbu.App
{
    // #308 WP-13: what one handler has hung on enemies' strike veils (EnemyStrikeVeil308): a blur while an enemy stands in
    // a haze, a screen while the player is behind cover as seen from an enemy. The handler refreshes an entry every frame
    // it holds and releases it the frame it no longer does; this list remembers who carries what, so that a zone or a
    // cover piece that ends, and a clear (death, rest, scene leave, disable), take exactly those entries back.
    public sealed class VeilHolds308
    {
        struct Held { public EnemyVitals Enemy; public long Owner; }
        readonly List<Held> _held = new List<Held>();

        public int Count => _held.Count;

        public void Blur(EnemyVitals enemy, long owner, float until, float share)
        {
            if (enemy != null && enemy.Modifiers.Veil.Blur(owner, until, share)) Remember(enemy, owner);
        }

        public void Screen(EnemyVitals enemy, long owner, float until)
        {
            if (enemy != null && enemy.Modifiers.Veil.Screen(owner, until)) Remember(enemy, owner);
        }

        // The entry of that owner on that enemy ends now.
        public void Release(EnemyVitals enemy, long owner)
        {
            for (int i = 0; i < _held.Count; i++)
            {
                if (_held[i].Enemy != enemy || _held[i].Owner != owner) continue;
                if (enemy != null) enemy.Modifiers.Veil.Remove(owner);
                _held.RemoveAt(i);
                return;
            }
        }

        // The owner is gone (its zone ran out, its cover fell): nobody keeps its entry.
        public void ReleaseAll(long owner)
        {
            for (int i = _held.Count - 1; i >= 0; i--)
            {
                if (_held[i].Owner != owner) continue;
                if (_held[i].Enemy != null) _held[i].Enemy.Modifiers.Veil.Remove(owner);
                _held.RemoveAt(i);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _held.Count; i++)
                if (_held[i].Enemy != null) _held[i].Enemy.Modifiers.Veil.Remove(_held[i].Owner);   // null = destroyed with its scene
            _held.Clear();
        }

        void Remember(EnemyVitals enemy, long owner)
        {
            for (int i = 0; i < _held.Count; i++)
                if (_held[i].Enemy == enemy && _held[i].Owner == owner) return;
            _held.Add(new Held { Enemy = enemy, Owner = owner });
        }
    }
}
