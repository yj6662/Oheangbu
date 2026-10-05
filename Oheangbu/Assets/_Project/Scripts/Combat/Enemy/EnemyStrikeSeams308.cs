// PURE308
using System.Collections.Generic;

namespace Oheangbu.Combat
{
    // #308 WP-13 (SPEC-SPELL-120-308, user answer Q5 = DECISIONS D308-13: "the spell rule plus a TEST target; real enemy AI
    // is separate work"). Two seams on one enemy, both hung on EnemyVitals.Modifiers and cleared with that enemy's life.
    // Plain C#, no clock of its own: the offline runner executes this file.

    // The enemy's own guard: a guard, ward or reflect stance that stops a share of every hit it takes. No enemy of the game
    // raises one yet; a guarding enemy built later (and the TEST target today) calls Raise and Lower. A hit registered with
    // LetThrough ignores the stance (the unblockable thrust) and uses its entry up when it lands. An enemy that never
    // raised a guard and has no entry gets every hit back untouched.
    public sealed class EnemyGuardStance308
    {
        // Bound of remembered hit ids, not a balance number: an id whose hit never landed is dropped oldest first.
        const int Capacity = 64;

        float _share;                                         // share of a hit the stance stops, 0 = not guarding
        readonly List<long> _through = new List<long>();

        public bool Raised => _share > 0f;
        public float Share => _share;
        public bool IsEmpty => !(_share > 0f) && _through.Count == 0;

        // share = how much of every hit the stance stops, 0..1 (1 = all of it). false = nothing changed.
        public bool Raise(float share)
        {
            if (!float.IsFinite(share) || !(share > 0f)) return false;
            _share = share > 1f ? 1f : share;
            return true;
        }

        public void Lower() { _share = 0f; }

        // The hit with this attack id ignores the stance. Used up when that hit is taken.
        public bool LetThrough(long attackId)
        {
            if (attackId <= 0) return false;
            Forget(attackId);
            if (_through.Count >= Capacity) _through.RemoveAt(0);
            _through.Add(attackId);
            return true;
        }

        public void Forget(long attackId)
        {
            for (int i = _through.Count - 1; i >= 0; i--)
                if (_through[i] == attackId) _through.RemoveAt(i);
        }

        public bool Passes(long attackId) => attackId > 0 && _through.Contains(attackId);

        // Damage of one hit after the stance. The hit's own entry is used up.
        public float Intake(float damage, long attackId)
        {
            if (_through.Count == 0 && !(_share > 0f)) return damage;
            bool passes = Passes(attackId);
            if (passes) Forget(attackId);
            if (passes || !(_share > 0f)) return damage;
            return damage * (1f - _share);
        }

        public void Clear() { _share = 0f; _through.Clear(); }
    }

    // What stands between this enemy's strikes and the player.
    //   blur    the enemy stands in a haze: a share of its strikes miss                     (hit chance, any kind of strike)
    //   screen  the player is behind cover as seen from this enemy: ranged strikes are stopped
    // Each entry belongs to an owner (the zone or cover piece that set it) and ends at its own time; the owner refreshes
    // it while it holds and removes it when it no longer does. Misses is asked once per strike, at the enemy -> player
    // doorway (EnemyStrike308). The hit chance is not a dice roll: every strike adds the miss share to a credit, and a
    // strike misses when the credit reaches one, so over N strikes exactly the stated share miss, in a fixed pattern.
    // Nothing here gives the player anything but the missed strike itself.
    public sealed class EnemyStrikeVeil308
    {
        struct Entry { public long Owner; public float Until, Share; public bool Screen; }

        // Slack of the credit comparison: ten strikes at a share of 0.1 must add up to one miss whatever the float says.
        const float Slack = 1e-4f;

        readonly List<Entry> _entries = new List<Entry>();
        float _credit;

        public bool IsEmpty => _entries.Count == 0;

        // Strikes of this enemy miss `share` (0..1) of the time until `until`. false = nothing changed.
        public bool Blur(long owner, float until, float share)
        {
            if (owner <= 0 || !float.IsFinite(until) || !float.IsFinite(share) || !(share > 0f)) return false;
            Set(new Entry { Owner = owner, Until = until, Share = share > 1f ? 1f : share });
            return true;
        }

        // Ranged strikes of this enemy are stopped until `until`.
        public bool Screen(long owner, float until)
        {
            if (owner <= 0 || !float.IsFinite(until)) return false;
            Set(new Entry { Owner = owner, Until = until, Screen = true });
            return true;
        }

        public void Remove(long owner)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
                if (_entries[i].Owner == owner) _entries.RemoveAt(i);
        }

        // The strongest blur still running (overlapping hazes do not add up). 0 = the enemy sees clearly.
        public float MissShare(float now)
        {
            float best = 0f;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (entry.Until <= now) { _entries.RemoveAt(i); continue; }
                if (!entry.Screen && entry.Share > best) best = entry.Share;
            }
            return best;
        }

        public bool Screened(float now)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (entry.Until <= now) { _entries.RemoveAt(i); continue; }
                if (entry.Screen) return true;
            }
            return false;
        }

        // One strike of this enemy is about to reach the player: does it miss? Advances the hit-chance credit.
        public bool Misses(bool ranged, float now)
        {
            if (_entries.Count == 0) return false;
            if (ranged && Screened(now)) return true;
            float share = MissShare(now);
            if (!(share > 0f)) return false;
            _credit += share;
            if (_credit < 1f - Slack) return false;
            _credit -= 1f;
            if (_credit < 0f) _credit = 0f;
            return true;
        }

        public void Clear() { _entries.Clear(); _credit = 0f; }

        void Set(Entry entry)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Owner == entry.Owner && _entries[i].Screen == entry.Screen) { _entries[i] = entry; return; }
            _entries.Add(entry);
        }
    }
}
