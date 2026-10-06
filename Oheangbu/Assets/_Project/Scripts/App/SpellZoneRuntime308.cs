// PURE308
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    // The list of lingering zones of one combat scope (SPEC-SPELL-120-308 WP-06). One instance, registered by
    // SpellEffectInstaller308.WP06 and handed to every zone effect through its constructor: never a static, so nothing
    // survives a scene or a play session. Each effect works on its own zones (Owner = its handler id): it adds them, asks
    // for their due steps and sweeps the ones that ran out. Whoever holds the instance may read every zone (Covered, Zones)
    // and may strike the breakable ones (Strike). Plain C#, no Unity object: the offline runner executes it as it is.
    public sealed class SpellZoneRuntime308
    {
        readonly List<SpellZone308> _zones = new List<SpellZone308>();
        int _lastId, _lastCast;

        public IReadOnlyList<SpellZone308> Zones => _zones;
        public int Count => _zones.Count;

        // A new serial for one cast: zones that come from the same cast carry it.
        public int NextCast() => ++_lastCast;

        public int CountOf(string owner)
        {
            int count = 0;
            for (int i = 0; i < _zones.Count; i++) if (_zones[i].Owner == owner) count++;
            return count;
        }

        // Adds a zone for its owner, who may hold max zones. Zones of the owner that ran out are swept first; when the owner
        // is full its oldest zone is replaced. removed receives every zone that left. Returns the new id, or 0 when the
        // zone was refused (the owner is full of zones of this same cast).
        public int Add(SpellZone308 zone, int max, float now, List<SpellZone308> removed)
        {
            Sweep(zone.Owner, now, removed);
            int verdict = SpellZoneRule308.Admit(_zones, zone.Owner, zone.Cast, max);
            if (verdict == SpellZoneRule308.Refused) return 0;
            if (verdict >= 0) RemoveAt(verdict, SpellZoneEnd.Replaced, removed);
            zone.Id = ++_lastId; zone.Ticks = 0; zone.Broken = false; zone.End = SpellZoneEnd.None;
            _zones.Add(zone);
            return zone.Id;
        }

        // Removes the owner's zones that are broken or past their life.
        public void Sweep(string owner, float now, List<SpellZone308> removed)
        {
            for (int i = _zones.Count - 1; i >= 0; i--)
            {
                var zone = _zones[i];
                if (zone.Owner != owner || !SpellZoneRule308.Ended(zone, now)) continue;
                RemoveAt(i, zone.Broken ? SpellZoneEnd.Broken : SpellZoneEnd.Expired, removed);
            }
        }

        // Removes every zone of the owner (death, rest, scene leave).
        public void Drop(string owner, List<SpellZone308> removed)
        {
            for (int i = _zones.Count - 1; i >= 0; i--)
                if (_zones[i].Owner == owner) RemoveAt(i, SpellZoneEnd.Dropped, removed);
        }

        public void Clear() => _zones.Clear();

        // Steps of the zone that fall due up to now (0 for an unknown id). Advances the zone's step counter.
        public int Ticks(int id, float now)
        {
            for (int i = 0; i < _zones.Count; i++)
            {
                if (_zones[i].Id != id) continue;
                var zone = _zones[i];
                int due = SpellZoneRule308.DueTicks(ref zone, now);
                _zones[i] = zone;
                return due;
            }
            return 0;
        }

        // An enemy strike landing at point with this reach: every breakable zone it touches loses one hit and breaks at its
        // last one (owner = null: zones of every owner). A broken zone stops acting at once; its owner sweeps it on its next
        // tick. Returns how many zones broke.
        public int Strike(string owner, Vector3 point, float reach)
        {
            int broken = 0;
            for (int i = 0; i < _zones.Count; i++)
            {
                var zone = _zones[i];
                if ((owner != null && zone.Owner != owner) || !SpellZoneRule308.Struck(zone, point, reach)) continue;
                if (Hit(ref zone)) broken++;
                _zones[i] = zone;
            }
            return broken;
        }

        // The same for a melee strike that hit the player: the attacker stands at attackerPosition and reaches as far as each
        // zone says an attacker does (Reach; 0 = this zone is never struck that way).
        public int StrikeFrom(string owner, Vector3 attackerPosition)
        {
            int broken = 0;
            for (int i = 0; i < _zones.Count; i++)
            {
                var zone = _zones[i];
                if ((owner != null && zone.Owner != owner) || zone.Reach <= 0f || !SpellZoneRule308.Struck(zone, attackerPosition, zone.Reach)) continue;
                if (Hit(ref zone)) broken++;
                _zones[i] = zone;
            }
            return broken;
        }

        // Is this position inside an active zone (owner = null: of any owner)?
        public bool Covered(Vector3 position, float now, string owner = null)
        {
            for (int i = 0; i < _zones.Count; i++)
                if ((owner == null || _zones[i].Owner == owner) && SpellZoneRule308.Covers(_zones[i], position, now)) return true;
            return false;
        }

        // Does a zone of the owner still use this presenter handle?
        public bool HoldsFx(string owner, int fx)
        {
            for (int i = 0; i < _zones.Count; i++)
                if (_zones[i].Owner == owner && _zones[i].Fx == fx) return true;
            return false;
        }

        static bool Hit(ref SpellZone308 zone)
        {
            zone.HitsLeft--;
            if (zone.HitsLeft > 0) return false;
            zone.HitsLeft = 0; zone.Broken = true;
            return true;
        }

        void RemoveAt(int index, SpellZoneEnd end, List<SpellZone308> removed)
        {
            var zone = _zones[index];
            zone.End = end;
            _zones.RemoveAt(index);
            removed?.Add(zone);
        }
    }
}
