// PURE308
using System.Collections.Generic;

namespace Oheangbu.Combat
{
    // #308 WP-07 (SPEC-SPELL-120-308, user answer Q4 = DECISIONS D308-13): the timed modifiers a spell can hang on one enemy.
    //   brand      the enemy takes more damage for a while                    (damage taken x scale, scale > 1)
    //   shred      the enemy's defence is lowered for a while                 (defence - amount, never below 0)
    //   weaken     the enemy's own attacks are weaker for a while             (outgoing x scale, scale < 1)
    //   dull       the enemy's next attack, once, is weaker                   (outgoing x scale, one charge with a time limit)
    //   pierce     one identified hit ignores a share of the enemy's defence  (registered at cast, used up by that hit)
    // Defence itself is the enemy's data (EnemyVitalsProfileSO, default 0): the share of incoming damage its armour removes.
    // Plain C#: no Unity object and no clock of its own (every call takes "now"), so the offline runner executes it.
    // Like EnemyControlState it is independent of HP, groggy and the weak point: nothing in here raises groggy, opens a weak
    // point or feeds the five-element completion. Same kind does not stack: the strongest running entry counts.
    // An untouched enemy (no entry, defence 0) answers every query with the neutral value and Intake returns its input as is.
    public sealed class EnemyModifierState308
    {
        enum Kind { Brand, Shred, Weaken, Dull }
        struct Entry { public Kind Kind; public float Until, Value; }
        struct Pierce { public long AttackId; public float Share; }

        // Bound of remembered hit ids, not a balance number: an id whose hit never landed is dropped oldest first.
        const int PierceCapacity = 64;

        readonly List<Entry> _timed = new List<Entry>();
        readonly List<Pierce> _pierce = new List<Pierce>();

        // #308 WP-13 (added at integration): the enemy's own guard stance and what veils its strikes (EnemyStrikeSeams308.cs).
        // Both are empty unless a WP-13 spell or a guarding enemy used them, and are cleared with everything else here.
        public EnemyGuardStance308 Stance { get; } = new EnemyGuardStance308();
        public EnemyStrikeVeil308 Veil { get; } = new EnemyStrikeVeil308();

        public bool IsEmpty => _timed.Count == 0 && _pierce.Count == 0 && Stance.IsEmpty && Veil.IsEmpty;

        // ---- writers (a value that would change nothing is refused: false) ----

        // Damage taken x scale until 'until'. scale must be above 1.
        public bool Brand(float until, float scale)
            => float.IsFinite(until) && float.IsFinite(scale) && scale > 1f && Add(Kind.Brand, until, scale, true);

        // Defence lowered by amount (in defence points, 0..1) until 'until'.
        public bool ShredDefence(float until, float amount)
            => float.IsFinite(until) && float.IsFinite(amount) && amount > 0f && Add(Kind.Shred, until, amount > 1f ? 1f : amount, true);

        // Every strike of this enemy x scale until 'until'. scale must be in 0..1 and below 1.
        public bool Weaken(float until, float scale)
            => float.IsFinite(until) && float.IsFinite(scale) && scale >= 0f && scale < 1f && Add(Kind.Weaken, until, scale, false);

        // The next strike of this enemy x scale, once, if it comes before 'until'. Casting again refreshes the one charge.
        public bool Dull(float until, float scale)
            => float.IsFinite(until) && float.IsFinite(scale) && scale >= 0f && scale < 1f && Add(Kind.Dull, until, scale, false);

        // The hit with this attack id ignores 'share' (0..1) of the defence. Used up when that hit is taken.
        public bool IgnoreDefence(long attackId, float share)
        {
            if (attackId <= 0 || !float.IsFinite(share) || share <= 0f) return false;
            ForgetIgnore(attackId);
            if (_pierce.Count >= PierceCapacity) _pierce.RemoveAt(0);
            _pierce.Add(new Pierce { AttackId = attackId, Share = share > 1f ? 1f : share });
            return true;
        }

        public void ForgetIgnore(long attackId)
        {
            for (int i = _pierce.Count - 1; i >= 0; i--)
                if (_pierce[i].AttackId == attackId) _pierce.RemoveAt(i);
        }

        public void Clear() { _timed.Clear(); _pierce.Clear(); Stance.Clear(); Veil.Clear(); }

        // The caster died, rested or left: the handler that hung a modifier on this enemy takes it back (every running
        // entry of that kind ends now, like the controls and the veils the other spell handlers take back). The guard
        // stance, the veil and the other kinds are not touched.
        public void EndBrand() => End(Kind.Brand);
        public void EndShred() => End(Kind.Shred);
        public void EndWeaken() => End(Kind.Weaken);
        public void EndDull() => End(Kind.Dull);

        void End(Kind kind)
        {
            for (int i = _timed.Count - 1; i >= 0; i--)
                if (_timed[i].Kind == kind) _timed.RemoveAt(i);
        }

        // ---- readers ----

        // 1 = not branded.
        public float DamageTakenScale(float now) => Strongest(Kind.Brand, now, true, 1f);

        // 0 = defence not lowered.
        public float DefenceShred(float now) => Strongest(Kind.Shred, now, true, 0f);

        // What is left of a base defence after the running shred (never below 0).
        public float DefenceNow(float baseDefence, float now) => EnemyIntakeRule308.Defence(baseDefence, DefenceShred(now), 0f);

        public bool Dulled(float now) => Strongest(Kind.Dull, now, false, 1f) < 1f;

        // Scale of a strike delivered now, without using anything up.
        public float OutgoingScale(float now) => Strongest(Kind.Weaken, now, false, 1f) * Strongest(Kind.Dull, now, false, 1f);

        public float IgnoredShare(long attackId)
        {
            for (int i = 0; i < _pierce.Count; i++)
                if (_pierce[i].AttackId == attackId) return _pierce[i].Share;
            return 0f;
        }

        // ---- the two places the modifiers act ----

        // Damage this enemy takes from one hit (already multiplied by the weak-point multiplier). The hit's pierce entry is
        // used up. No entry and no defence: the input comes back untouched (bit for bit what it was before WP-07).
        public float Intake(float damage, float baseDefence, long attackId, float now)
        {
            damage = Stance.Intake(damage, attackId);   // WP-13: a raised guard, and the hit that ignores it (untouched when there is neither)
            if (_timed.Count == 0 && _pierce.Count == 0 && !(baseDefence > 0f)) return damage;
            float ignored = attackId > 0 ? IgnoredShare(attackId) : 0f;
            if (ignored > 0f) ForgetIgnore(attackId);
            return EnemyIntakeRule308.Apply(damage, EnemyIntakeRule308.Defence(baseDefence, DefenceShred(now), ignored), DamageTakenScale(now));
        }

        // Amount of the strike this enemy delivers now. The dull charge is used up by it (whatever the strike then does).
        public float TakeStrike(float amount, float now)
        {
            if (_timed.Count == 0 || !(amount > 0f)) return amount;
            float scale = OutgoingScale(now);
            for (int i = _timed.Count - 1; i >= 0; i--)
                if (_timed[i].Kind == Kind.Dull) _timed.RemoveAt(i);
            return scale < 1f ? amount * scale : amount;
        }

        // ---- internals ----

        // Keeps only entries that still matter: one that is no stronger and ends no later than another is dropped.
        bool Add(Kind kind, float until, float value, bool higherIsStronger)
        {
            for (int i = _timed.Count - 1; i >= 0; i--)
            {
                var other = _timed[i];
                if (other.Kind != kind) continue;
                bool otherAsStrong = higherIsStronger ? other.Value >= value : other.Value <= value;
                bool otherNoStronger = higherIsStronger ? other.Value <= value : other.Value >= value;
                if (otherAsStrong && other.Until >= until) return false;
                if (otherNoStronger && other.Until <= until) _timed.RemoveAt(i);
            }
            _timed.Add(new Entry { Kind = kind, Until = until, Value = value });
            return true;
        }

        // The strongest entry of a kind still running at 'now' (an entry ends at its Until, like EnemyControlState).
        float Strongest(Kind kind, float now, bool higherIsStronger, float neutral)
        {
            float best = neutral;
            for (int i = _timed.Count - 1; i >= 0; i--)
            {
                var entry = _timed[i];
                if (entry.Until <= now) { _timed.RemoveAt(i); continue; }
                if (entry.Kind != kind) continue;
                if (higherIsStronger ? entry.Value > best : entry.Value < best) best = entry.Value;
            }
            return best;
        }
    }

    // The arithmetic of the intake, apart from the state so it can be read and checked on its own.
    public static class EnemyIntakeRule308
    {
        // Defence that acts on one hit: base defence (0..1) minus the shred, never below 0, then the share the hit ignores.
        public static float Defence(float baseDefence, float shred, float ignoredShare)
        {
            if (!(baseDefence > 0f)) return 0f;
            float defence = (baseDefence > 1f ? 1f : baseDefence) - (shred > 0f ? shred : 0f);
            if (!(defence > 0f)) return 0f;
            if (ignoredShare > 0f) defence *= 1f - (ignoredShare > 1f ? 1f : ignoredShare);
            return defence;
        }

        // damage x (1 - defence) x damage-taken scale. Each factor is applied only when it changes something, so a hit on an
        // enemy without defence and without a brand is returned exactly as it came in.
        public static float Apply(float damage, float defence, float takenScale)
        {
            if (defence > 0f) damage *= 1f - (defence > 1f ? 1f : defence);
            if (takenScale > 1f) damage *= takenScale;
            return damage;
        }
    }
}
