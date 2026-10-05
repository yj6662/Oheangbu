// PURE308
using System.Collections.Generic;

namespace Oheangbu.App
{
    // #308 WP-07: which scheduled hits of a modifier spell are still on their way. A modifier is put on an enemy only when
    // the hit that carries it really lands ("the target that was hit", "everyone it swept"), so the effect remembers each
    // scheduled hit by its attack id together with the numbers read from the row at cast time, and takes the entry back
    // when the wiring reports that id as a confirmed hit. Plain C#: the offline runner executes it.
    public sealed class ModifierHitLedger308
    {
        public struct Entry
        {
            public long AttackId;
            public int TargetId;      // index in ISpellCastHost.Targets at cast time
            public uint Life;         // EnemyVitals.LifeRevision at cast time
            public float ImpactAt;    // the wiring's scaled clock
            public float Value, Duration;
            public bool Due;
        }

        readonly List<Entry> _entries = new List<Entry>();

        public int Count => _entries.Count;

        public void Add(long attackId, int targetId, uint life, float impactAt, float value, float duration)
        {
            if (attackId <= 0) return;
            _entries.Add(new Entry { AttackId = attackId, TargetId = targetId, Life = life, ImpactAt = impactAt, Value = value, Duration = duration });
        }

        // The confirmed hit with this id: its entry leaves the ledger. false = not one of this effect's hits (or already taken).
        public bool Take(long attackId, out Entry entry)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].AttackId != attackId) continue;
                entry = _entries[i]; _entries.RemoveAt(i);
                return true;
            }
            entry = default; return false;
        }

        // Call once per frame, before the wiring lands that frame's hits (the order CombatLoopWiring.Update runs in).
        // A hit lands in the first frame whose clock reached its impact time, after this sweep. So an entry is dropped on the
        // second sweep at or past its impact time: by then its hit has landed (and was taken) or never will (the target died,
        // was replaced or went out of sight). Dropped entries are handed back so the caller can undo what it set up at cast.
        public void Sweep(float now, List<Entry> dropped = null)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (now < entry.ImpactAt) continue;
                if (!entry.Due) { entry.Due = true; _entries[i] = entry; continue; }
                _entries.RemoveAt(i);
                dropped?.Add(entry);
            }
        }

        public void Clear() => _entries.Clear();
    }
}
