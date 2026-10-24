// PURE308
using System;
using System.Collections.Generic;

namespace Oheangbu.App.World
{
    // D308-147 (debate 32, plan C): one weak-point window answers to one choice - the riposte (앞잡) or a drawing, never both.
    // The window is named by the enemy, its life and the opening of its weak point (EnemyVitals.LifeRevision and
    // WeakPointGeneration). Plain numbers here, no engine objects: the combat side builds the key from the enemy it holds.

    /// <summary>One window: this enemy, this life, this opening. Enemy 0 = none.</summary>
    public readonly struct RiposteWindowKey308 : IEquatable<RiposteWindowKey308>
    {
        public readonly int Enemy;
        public readonly uint Life, Generation;

        public RiposteWindowKey308(int enemy, uint life, uint generation) { Enemy = enemy; Life = life; Generation = generation; }

        public bool IsNone => Enemy == 0;
        public bool Equals(RiposteWindowKey308 other) => Enemy == other.Enemy && Life == other.Life && Generation == other.Generation;
        public override bool Equals(object obj) => obj is RiposteWindowKey308 other && Equals(other);
        public override int GetHashCode() => unchecked((Enemy * 397) ^ (int)(Life * 31u) ^ (int)(Generation * 17u));
        public static bool operator ==(RiposteWindowKey308 a, RiposteWindowKey308 b) => a.Equals(b);
        public static bool operator !=(RiposteWindowKey308 a, RiposteWindowKey308 b) => !a.Equals(b);
        public override string ToString() => "enemy " + Enemy + " life " + Life + " window " + Generation;
    }

    public enum RiposteWindowChoice308 { None = 0, Draw = 1, Riposte = 2 }

    /// <summary>Who took which window. Per enemy the newest window only: an older window of the same enemy never comes back,
    /// and another enemy's window is never touched by this one's. A small bounded list, no allocation per query.</summary>
    public sealed class RiposteWindowClaim308
    {
        private const int Capacity = 64;

        private struct Entry { public RiposteWindowKey308 Key; public RiposteWindowChoice308 Choice; }

        private readonly List<Entry> _entries = new List<Entry>(8);

        public int Count => _entries.Count;

        public RiposteWindowChoice308 ChoiceOf(RiposteWindowKey308 key)
        {
            int at = Find(key.Enemy);
            return at >= 0 && _entries[at].Key.Equals(key) ? _entries[at].Choice : RiposteWindowChoice308.None;
        }

        /// <summary>The window may still be answered with a riposte: a real window nobody took yet.</summary>
        public bool CanOfferRiposte(RiposteWindowKey308 key) => !key.IsNone && ChoiceOf(key) == RiposteWindowChoice308.None;

        /// <summary>A riposte has taken this window: no new drawing may start in it (the gate asks this).</summary>
        public bool BlocksNewDraw(RiposteWindowKey308 key) => !key.IsNone && ChoiceOf(key) == RiposteWindowChoice308.Riposte;

        /// <summary>The riposte takes the window. false = it was taken already (a drawing or a riposte), or the key is stale.</summary>
        public bool TryChooseRiposte(RiposteWindowKey308 key) => Take(key, RiposteWindowChoice308.Riposte);

        /// <summary>A drawing takes the window (its first stroke). true also when a drawing had it already; false when a riposte has it
        /// or the key is stale.</summary>
        public bool TryChooseDraw(RiposteWindowKey308 key) => Take(key, RiposteWindowChoice308.Draw);

        public void Clear() { _entries.Clear(); }

        /// <summary>The volley that ran on window <paramref name="mine"/> may close the window that is open now only if it is that
        /// same window (a late finish must not wipe the next one).</summary>
        public static bool OwnsWindow(RiposteWindowKey308 mine, RiposteWindowKey308 current) => !mine.IsNone && mine.Equals(current);

        private bool Take(RiposteWindowKey308 key, RiposteWindowChoice308 choice)
        {
            if (key.IsNone) return false;
            int at = Find(key.Enemy);
            if (at >= 0)
            {
                var entry = _entries[at];
                if (entry.Key.Equals(key))
                {
                    if (entry.Choice == choice) return choice == RiposteWindowChoice308.Draw;   // a second riposte is refused, a second stroke is not news
                    return false;
                }
                if (Older(key, entry.Key)) return false;
                _entries[at] = new Entry { Key = key, Choice = choice };
                return true;
            }
            if (_entries.Count >= Capacity) _entries.RemoveAt(0);
            _entries.Add(new Entry { Key = key, Choice = choice });
            return true;
        }

        private static bool Older(RiposteWindowKey308 a, RiposteWindowKey308 b) => a.Life < b.Life || (a.Life == b.Life && a.Generation < b.Generation);

        private int Find(int enemy)
        {
            for (int i = 0; i < _entries.Count; i++) if (_entries[i].Key.Enemy == enemy) return i;
            return -1;
        }
    }
}
