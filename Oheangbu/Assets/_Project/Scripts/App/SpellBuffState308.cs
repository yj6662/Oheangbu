// PURE308
using System.Collections.Generic;

namespace Oheangbu.App
{
    // Shared timers of the self buffs added by #308 (SPEC-SPELL-120-308 WP-08, Q8 default: the EA buff rule for every buff).
    // Recasting the same glyph replaces its remaining time with the new duration (never additive); different glyphs run side
    // by side. Nothing is saved: the owner clears it on death, rest, scene leave and disable. The five existing buffs keep
    // their own runtime (EABuffRuntime) and are not in here.
    public sealed class SpellBuffState308
    {
        struct Slot { public char Letter; public float Until; public int Charges; }
        readonly List<Slot> _slots = new List<Slot>();

        // charges = 0: a plain timed buff. charges > 0: also ends when the last charge is consumed.
        public void Activate(char letter, float now, float duration, int charges = 0)
        {
            if (float.IsNaN(now) || float.IsNaN(duration) || duration <= 0f) return;
            var slot = new Slot { Letter = letter, Until = now + duration, Charges = charges > 0 ? charges : 0 };
            int at = IndexOf(letter);
            if (at >= 0) _slots[at] = slot; else _slots.Add(slot);
        }

        public bool Active(char letter, float now)
        {
            int at = IndexOf(letter);
            return at >= 0 && now < _slots[at].Until;
        }

        public float Remaining(char letter, float now)
        {
            int at = IndexOf(letter);
            return at >= 0 && now < _slots[at].Until ? _slots[at].Until - now : 0f;
        }

        public int Charges(char letter, float now)
        {
            int at = IndexOf(letter);
            return at >= 0 && now < _slots[at].Until ? _slots[at].Charges : 0;
        }

        // Uses one charge of an active buff. The buff ends with its last charge. false = not active or no charges.
        public bool Consume(char letter, float now)
        {
            int at = IndexOf(letter);
            if (at < 0 || now >= _slots[at].Until || _slots[at].Charges <= 0) return false;
            var slot = _slots[at]; slot.Charges--;
            if (slot.Charges <= 0) _slots.RemoveAt(at); else _slots[at] = slot;
            return true;
        }

        public void End(char letter)
        {
            int at = IndexOf(letter);
            if (at >= 0) _slots.RemoveAt(at);
        }

        // Drops finished buffs; returns how many are still running.
        public int Expire(float now)
        {
            for (int i = _slots.Count - 1; i >= 0; i--)
                if (now >= _slots[i].Until) _slots.RemoveAt(i);
            return _slots.Count;
        }

        public void Clear() => _slots.Clear();

        int IndexOf(char letter)
        {
            for (int i = 0; i < _slots.Count; i++) if (_slots[i].Letter == letter) return i;
            return -1;
        }
    }
}
