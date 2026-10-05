// PURE308
using System.Collections.Generic;

namespace Oheangbu.App
{
    // One scheduled impact that may execute: the attack identity of the planned hit and what the execution would add.
    public struct ExecuteMark308
    {
        public long AttackId;       // identity of the planned hit (PlannedHit.AttackId)
        public float ImpactAt;      // planned impact clock
        public float CastPower;     // power of the cast, before the equipment scale
        public float Bonus;         // extra hit as a multiple of the cast power
        public float Threshold01;   // HP ratio at or below which the hit executes
        public char Letter;         // the cast's glyph (contact presentation only)
        public bool Due;            // a tick has already seen the impact clock pass
    }

    // "Cut the breath" (SPEC-SPELL-120-308 WP-02, handler single.execute): a hit that lands on a target whose HP ratio just
    // before the impact was at or below the threshold deals one extra hit under the same attack identity. Above the
    // threshold it is the plain hit and nothing else.
    public static class ExecuteRule308
    {
        // The HP ratio the target had just before the impact that was just applied (HP now + what the impact took).
        public static float Hp01Before(float hpAfter, float applied, float maxHp)
        {
            if (!(maxHp > 0f)) return 0f;
            return ((hpAfter > 0f ? hpAfter : 0f) + (applied > 0f ? applied : 0f)) / maxHp;
        }

        // At or below the threshold (the edge executes). A threshold of 0 or less never executes.
        public static bool Executes(float hp01Before, float threshold01)
        {
            return threshold01 > 0f && hp01Before <= threshold01;
        }

        // Power of the extra hit: cast power x bonus when the impact executes, 0 otherwise.
        public static float BonusPower(float hp01Before, float threshold01, float castPower, float bonus)
        {
            return Executes(hp01Before, threshold01) && castPower > 0f && bonus > 0f ? castPower * bonus : 0f;
        }
    }

    // The marks of the casts in flight. One execution per attack identity: taking a mark removes it, so the extra hit (which
    // carries the same identity and is heard as a hit again) can never execute a second time.
    public sealed class ExecuteMarks308
    {
        readonly List<ExecuteMark308> _marks = new List<ExecuteMark308>();

        public int Count => _marks.Count;

        public void Add(long attackId, float impactAt, float castPower, float bonus, float threshold01, char letter)
        {
            if (attackId <= 0) return;   // 0 = a visual-only shot: nothing will ever land under it
            _marks.Add(new ExecuteMark308 { AttackId = attackId, ImpactAt = impactAt, CastPower = castPower, Bonus = bonus, Threshold01 = threshold01, Letter = letter });
        }

        public bool TryTake(long attackId, out ExecuteMark308 mark)
        {
            for (int i = 0; i < _marks.Count; i++)
            {
                if (_marks[i].AttackId != attackId) continue;
                mark = _marks[i]; _marks.RemoveAt(i);
                return true;
            }
            mark = default; return false;
        }

        // Called once per frame, before the frame's impacts land. The first call at or after a mark's impact clock leaves it
        // (its hit lands later in that same frame); the next call drops it: the hit never landed (the target died or went
        // out of sight first), so nothing is left waiting.
        public void Expire(float now)
        {
            for (int i = _marks.Count - 1; i >= 0; i--)
            {
                var mark = _marks[i];
                if (!(now >= mark.ImpactAt)) continue;
                if (mark.Due) { _marks.RemoveAt(i); continue; }
                mark.Due = true; _marks[i] = mark;
            }
        }

        public void Clear() => _marks.Clear();
    }
}
