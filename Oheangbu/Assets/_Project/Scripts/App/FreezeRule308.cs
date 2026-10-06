// PURE308
using System.Collections.Generic;
using Oheangbu.Core.Domain;

namespace Oheangbu.App
{
    // #308 WP-05, handler single.freeze (SPEC-SPELL-120-308 section 13): the confirmed hit freezes its target; the next
    // confirmed hit on that target breaks the ice (extra damage once, control released). A freeze nobody breaks simply ends.
    // Boss rule = the giyeok precedent (Q8 default): a boss is slowed instead of blocked; its ice breaks the same way.
    public static class FreezeRule308
    {
        // What one cast carries from its row to the moment its hit is confirmed. Bonus = the extra damage of the break
        // (cast power x the row's factor), fixed at cast time.
        public readonly struct Spec
        {
            public readonly float Duration, BossSpeed, Bonus;
            public readonly Element Element;
            public readonly char Letter;
            public Spec(float duration, float bossSpeed, float bonus, Element element, char letter)
            { Duration = duration; BossSpeed = bossSpeed; Bonus = bonus; Element = element; Letter = letter; }
        }

        // One frozen enemy. Owner = the attack id of the freezing hit (also the owner id of its control).
        public readonly struct Frozen
        {
            public readonly int TargetId;
            public readonly uint Life;
            public readonly long Owner;
            public readonly float Until, Bonus;
            public readonly Element Element;
            public readonly char Letter;
            public Frozen(int targetId, uint life, long owner, float until, float bonus, Element element, char letter)
            { TargetId = targetId; Life = life; Owner = owner; Until = until; Bonus = bonus; Element = element; Letter = letter; }
        }

        public static float ShatterPower(float castPower, float scale)
        {
            return castPower > 0f && scale > 0f ? castPower * scale : 0f;
        }

        // The frozen enemies of one handler instance. No Unity object: the handler maps target ids to enemies.
        public sealed class State
        {
            readonly List<Frozen> _frozen = new List<Frozen>();

            public int Count => _frozen.Count;
            public Frozen this[int index] => _frozen[index];

            // The freezing hit was confirmed. Ordinary enemy: no action and no movement until now + duration. Boss: movement
            // scaled, actions untouched. false = nothing is frozen (the hit killed, or the row gives no duration).
            public bool Freeze(int targetId, uint life, long owner, bool isBoss, bool killed, float now, in Spec spec,
                out float until, out float speed, out bool blocksActions)
            {
                until = now + spec.Duration;
                RootBindRule308.Resolve(isBoss, spec.BossSpeed, out speed, out blocksActions);
                if (killed || targetId < 0 || owner <= 0 || !(spec.Duration > 0f)) return false;
                _frozen.Add(new Frozen(targetId, life, owner, until, spec.Bonus, spec.Element, spec.Letter));
                return true;
            }

            // A confirmed hit on targetId. true = it broke a freeze: that entry has left the list and is handed back (the
            // caller releases its control and applies Bonus once). The freezing hit itself never breaks its own ice. A hit that
            // killed, a hit on another life and a hit after the freeze ran out only drop the stale entry.
            public bool TryShatter(int targetId, uint life, long hitAttack, bool killed, float now, out Frozen broken)
            {
                for (int i = 0; i < _frozen.Count; i++)
                {
                    var entry = _frozen[i];
                    if (entry.TargetId != targetId || entry.Owner == hitAttack) continue;
                    _frozen.RemoveAt(i);
                    if (killed || entry.Life != life || now >= entry.Until) { i--; continue; }
                    broken = entry;
                    return true;
                }
                broken = default;
                return false;
            }

            public bool IsFrozen(int targetId, uint life, float now)
            {
                for (int i = 0; i < _frozen.Count; i++)
                    if (_frozen[i].TargetId == targetId && _frozen[i].Life == life && now < _frozen[i].Until) return true;
                return false;
            }

            // Freezes that ran out unbroken: no bonus. Returns how many ended.
            public int Expire(float now)
            {
                int ended = 0;
                for (int i = _frozen.Count - 1; i >= 0; i--)
                    if (now >= _frozen[i].Until) { _frozen.RemoveAt(i); ended++; }
                return ended;
            }

            public void Clear() => _frozen.Clear();
        }
    }
}
