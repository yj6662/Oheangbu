// PURE308
using System.Collections.Generic;

namespace Oheangbu.App
{
    // #308 WP-17 (SPEC-SPELL-120-308, D308-121 결계): what the player's own domain does to the player standing in it. One
    // instance per CombatLoopWiring (IPlayerDomainHost308), cleared with the spells. Like EnemyDomainState308 every entry
    // belongs to an owner (the domain zone's token) and ends at its own time; the domain refreshes it every frame the player
    // is inside and ends it the frame the player leaves. The wiring asks the readers at its seams.
    //   regen   natural ink regeneration x value (above 1); the "no regen while drawing" rule stays     (움)
    //   drain   the sword form spends no ink                                                          (눔)
    //   anchor  the player cannot be pushed or pulled (read by CombatLoopWiring.Shove308, D308-129)    (숨)
    //   ward    no lasting enemy status sticks to the player (no such status exists yet: the reader waits) (운)
    // Nothing here heals, deals damage or changes HP. Plain C#, no clock of its own: the offline runner executes this file.
    public sealed class PlayerDomainState308
    {
        public enum Kind { Regen, Drain, Anchor, Ward }
        struct Entry { public long Owner; public Kind Kind; public float Until, Value; }

        readonly List<Entry> _entries = new List<Entry>();

        public bool IsEmpty => _entries.Count == 0;
        public int Count => _entries.Count;

        // ---- writers (one entry per owner and kind: a refresh replaces it; a value that would change nothing is refused) ----

        public bool Regen(long owner, float until, float scale) => scale > 1f && Set(owner, Kind.Regen, until, scale);
        public bool HoldDrain(long owner, float until) => Set(owner, Kind.Drain, until, 1f);
        public bool Anchor(long owner, float until) => Set(owner, Kind.Anchor, until, 1f);
        public bool Ward(long owner, float until) => Set(owner, Kind.Ward, until, 1f);

        public void End(long owner)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
                if (_entries[i].Owner == owner) _entries.RemoveAt(i);
        }

        public void End(long owner, Kind kind)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
                if (_entries[i].Owner == owner && _entries[i].Kind == kind) _entries.RemoveAt(i);
        }

        public void Clear() => _entries.Clear();

        // ---- readers (expired entries are dropped as they are met) ----

        // The strongest entry counts (1 = untouched).
        public float RegenScale(float now)
        {
            float best = 1f;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (entry.Until <= now) { _entries.RemoveAt(i); continue; }
                if (entry.Kind == Kind.Regen && entry.Value > best) best = entry.Value;
            }
            return best;
        }

        public bool DrainHeld(float now) => Any(Kind.Drain, now);
        public bool Anchored(float now) => Any(Kind.Anchor, now);
        public bool Warded(float now) => Any(Kind.Ward, now);

        // ---- internals ----

        bool Set(long owner, Kind kind, float until, float value)
        {
            if (owner <= 0 || !float.IsFinite(until) || !float.IsFinite(value)) return false;
            var entry = new Entry { Owner = owner, Kind = kind, Until = until, Value = value };
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Owner == owner && _entries[i].Kind == kind) { _entries[i] = entry; return true; }
            _entries.Add(entry);
            return true;
        }

        bool Any(Kind kind, float now)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (entry.Until <= now) { _entries.RemoveAt(i); continue; }
                if (entry.Kind == kind) return true;
            }
            return false;
        }
    }

    // What a domain handler may ask of the wiring beyond ISpellCastHost (CombatLoopWiring.Domain308.cs). A host without it
    // (checks, a lab) simply gets no player-side effect.
    public interface IPlayerDomainHost308
    {
        PlayerDomainState308 PlayerDomain308 { get; }
        // A parry guard stands now: shots the guard would judge are left to it (눗 · 숭).
        bool GuardStands308 { get; }
    }
}
