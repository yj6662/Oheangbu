// PURE308
using System.Collections.Generic;

namespace Oheangbu.App
{
    // #308 WP-05 (enemy control glyphs). A handler plans its hits through the host, remembers each planned hit's attack id
    // together with what must happen when that hit is confirmed, and takes the entry back inside its hit hook.
    // An entry whose impact time has been over for a whole frame never landed (the target died first, was hidden, or the
    // slot holds another life): it is dropped, so nothing is ever applied without a confirmed hit.
    public sealed class ControlHitWatch308<T>
    {
        struct Entry { public long AttackId; public float At; public bool Due; public T Value; }
        readonly List<Entry> _entries = new List<Entry>();

        public int Count => _entries.Count;

        public void Add(long attackId, float at, T value)
        {
            if (attackId <= 0 || float.IsNaN(at)) return;
            _entries.Add(new Entry { AttackId = attackId, At = at, Value = value });
        }

        // true = this confirmed hit is one the handler was waiting for (the entry is gone afterwards: one hit, one effect).
        public bool TryTake(long attackId, out T value)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].AttackId != attackId) continue;
                value = _entries[i].Value;
                _entries.RemoveAt(i);
                return true;
            }
            value = default;
            return false;
        }

        // Once per frame, from the handler's Tick. The wiring ticks the handlers first and lands its scheduled hits afterwards
        // in the same frame, so an entry is only marked when its time comes and removed on the next sweep if it is still here.
        // Returns how many entries were dropped.
        public int Sweep(float now)
        {
            int dropped = 0;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (entry.Due) { _entries.RemoveAt(i); dropped++; }
                else if (now >= entry.At) { entry.Due = true; _entries[i] = entry; }
            }
            return dropped;
        }

        public void Clear() => _entries.Clear();
    }
}
