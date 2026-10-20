// PURE308
using System.Collections.Generic;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // Pure rules of the nine self buffs of SPEC-SPELL-120-308 WP-08 (timers come from SpellBuffState308; here: consumption,
    // discount, heal share, ink reserve, retaliation answers). No Unity object, no clock of its own: the effect classes hand
    // in the time, the numbers of their row and plain snapshots, and execute what comes back. The offline runner
    // (Tools/SpellVFX120/Pure308/Cases_WP08.cs) runs exactly these bodies.
    //
    // Canon kept here: a buff may answer a hit (retaliation) but nothing in an answer belongs to the player, a retaliation
    // never touches groggy or the five-element completion (the effect sends it with the Retaliation source), and no rule
    // looks at recognition or presentation.
    public static class BuffRule308
    {
        // "Attack" for the buffs that change the next attack: the two attack kinds, nothing else (parry, summon, ward, field,
        // buff and install casts are not attacks).
        public static bool IsAttack(SpellKind kind) => kind == SpellKind.AttackSingle || kind == SpellKind.AttackArea;

        // Swift: an attack cast flies faster while the buff runs. The power is handed in so the rule owns the whole answer:
        // it leaves it alone.
        public static void Swift(bool active, SpellKind kind, float mul, ref float power, ref float speedMul)
        {
            if (active && IsAttack(kind) && mul > 0f) speedMul *= mul;
        }

        // Steadfast: a hit taken while the buff runs does not ask the drawing layer to drop the letter. The hit itself is
        // never changed: the same amount comes back.
        public static float Steadfast(bool active, float damage, ref bool interruptsDrawing)
        {
            if (active) interruptsDrawing = false;
            return damage;
        }

        // Reflect: one retaliation order for the enemy that struck in melee, at the moment of the hit. Ranged hits, hits
        // without an attacker (environment), a dead attacker and an inactive buff give nothing.
        public static void Reflect(bool active, bool melee, SpellActorSnap[] actors, int attackerId, float now, float power, Element element, List<SpellHitOrder> orders)
        {
            if (!Answers(active, melee, actors, attackerId) || !(power > 0f) || orders == null) return;
            orders.Add(new SpellHitOrder(attackerId, now, power, element));
        }

        // Slowback: the enemy that struck in melee moves at scale (below 1) until the returned time. false = no slow.
        public static bool Slowback(bool active, bool melee, SpellActorSnap[] actors, int attackerId, float now, float duration, float speed, out float until, out float scale)
        {
            until = now; scale = 1f;
            if (!Answers(active, melee, actors, attackerId) || !(duration > 0f) || float.IsNaN(now) || float.IsNaN(speed)) return false;
            until = now + duration; scale = Mathf.Clamp01(speed);
            return true;
        }

        // Movement scale of an enemy slowed until a time: the slow ends by itself (the same reading as
        // EnemyControlState.MovementScale, which drops an entry once its time is reached).
        public static float SlowAt(float now, float until, float scale) => now < until ? scale : 1f;

        static bool Answers(bool active, bool melee, SpellActorSnap[] actors, int attackerId)
            => active && melee && actors != null && attackerId >= 0 && attackerId < actors.Length && actors[attackerId].Alive;
    }

    // Burst heal: a share of max HP paid out evenly over a short window. Recasting first pays what the old window owed up to
    // that moment and then replaces the rest (never additive). A dead player ends it: nothing is healed, then or later.
    public sealed class BurstHealState308
    {
        float _rate01, _last, _end;
        public bool Running { get; private set; }

        // Returns the share of max HP the old window still owed up to now (0 when none was running).
        public float Begin(float now, float duration, float total01, bool alive)
        {
            float owed = Advance(now, alive);
            Running = false;
            if (!alive || float.IsNaN(now) || !(duration > 0f) || !(total01 > 0f)) return owed;
            _rate01 = total01 / duration; _last = now; _end = now + duration; Running = true;
            return owed;
        }

        // Share of max HP to heal for the time since the last call (0 after the window, 0 for a dead player).
        public float Advance(float now, bool alive)
        {
            if (!Running) return 0f;
            if (!alive) { Running = false; return 0f; }
            float to = Mathf.Min(now, _end);
            float seconds = to - _last;
            if (seconds > 0f) _last = to; else seconds = 0f;
            if (now >= _end) Running = false;
            return _rate01 * seconds;
        }

        public void Clear() { Running = false; }
    }

    // Empower: the next attack cast is amplified once. The multiplier is applied before dispatch (Modify). D308-123 (TEST): the
    // charge is used when that cast lands, not when it is accepted. Accept keeps the attack ids its dispatch handed out
    // (SpellAttackWindow308) and the first confirmed hit with one of them uses the charge (Hit). A cast refused for ink, and one
    // that lands nothing (an air cast, every shot missed), keep the buff. While an amplified cast is in flight the charge is
    // still there, so a second attack cast made then is amplified too and whichever lands first uses it (TEST).
    // A cast accepted without a window (outside a dispatch) uses the charge at its accept, as before.
    public sealed class EmpowerState308
    {
        const int SeenCap = 32, FlyingCap = 16;
        bool _applied; char _appliedTo;
        // hits between the multiplier and the accept: a handler's Commit may land a hit inside the dispatch
        readonly List<long> _seen = new List<long>();
        readonly List<SpellAttackWindow308> _flying = new List<SpellAttackWindow308>();

        // The speed is handed in so the rule owns the whole answer: it leaves it alone.
        public void Modify(SpellBuffState308 buffs, char buff, char castLetter, SpellKind kind, float mul, float now, ref float power, ref float speedMul)
        {
            _applied = false; _seen.Clear();
            if (buffs == null || buffs.Charges(buff, now) <= 0 || !BuffRule308.IsAttack(kind) || !(mul > 0f)) return;
            power *= mul; _applied = true; _appliedTo = castLetter;
        }

        // true = the buff is spent now (no window, or the cast already landed inside its own dispatch). The cast is matched by
        // its glyph, not by the moment: a swing dispatches at its apex, later than the multiplier.
        public bool Accept(SpellBuffState308 buffs, char buff, char castLetter, SpellKind kind, SpellAttackWindow308 attacks, float now)
        {
            if (!_applied || castLetter != _appliedTo || !BuffRule308.IsAttack(kind)) return false;
            _applied = false;
            bool landed = attacks == null;
            for (int i = 0; !landed && i < _seen.Count; i++) landed = attacks.Holds(_seen[i]);
            _seen.Clear();
            if (landed) return Spend(buffs, buff, now);
            for (int i = _flying.Count - 1; i >= 0; i--)
                if (_flying[i].Empty) _flying.RemoveAt(i);
            if (_flying.Count >= FlyingCap) _flying.RemoveAt(0);
            _flying.Add(attacks);
            return false;
        }

        // true = a hit of an amplified cast landed and the buff is spent. Its other hits find nothing left to use.
        public bool Hit(SpellBuffState308 buffs, char buff, long attackId, float now)
        {
            if (attackId <= 0) return false;
            if (_applied && _seen.Count < SeenCap) _seen.Add(attackId);
            for (int i = 0; i < _flying.Count; i++)
                if (_flying[i].Holds(attackId)) return Spend(buffs, buff, now);
            return false;
        }

        bool Spend(SpellBuffState308 buffs, char buff, float now)
        {
            _flying.Clear();
            return buffs != null && buffs.Consume(buff, now);
        }

        public void Clear() { _applied = false; _seen.Clear(); _flying.Clear(); }
    }

    // Chain discount: while the buff runs, a cast whose element differs from the element of the cast accepted just before it
    // costs less. The last accepted element is remembered whether or not the buff runs; without one there is no discount.
    public sealed class ChainState308
    {
        bool _has; Element _last;

        public void Record(Element element) { _has = true; _last = element; }

        public float CostScale(bool active, Element current, float discount)
            => active && _has && current != _last && discount > 0f ? Mathf.Clamp01(1f - discount) : 1f;

        public void Clear() { _has = false; }
    }

    // Ink reserve: while the buff runs the pool carries a temporary capacity coefficient above 1. When it ends, the ink that
    // stands above the normal maximum is not lost at once: it stays as a reserve (the coefficient shrinks to 1 + reserve) that
    // dwindles at a fixed rate and is spent first. Units: 1 = the normal maximum (equipment included).
    public sealed class InkReserveState308
    {
        public float Capacity { get; private set; } = 1f;      // the temporary coefficient the pool should carry now
        public bool Expanded { get; private set; }             // the buff itself is running
        public float Reserve => Expanded ? 0f : Capacity - 1f; // ink above the normal maximum after the buff
        public bool Idle => !Expanded && Capacity <= 1f;

        public void Begin(float capacity) { Capacity = capacity > 1f ? capacity : 1f; Expanded = true; }

        // The buff ended with the pool filled to fill01 of the widened vessel: the excess becomes the reserve.
        public void Expire(float fill01)
        {
            if (!Expanded) return;
            Expanded = false;
            Capacity = 1f + Excess(fill01, Capacity);
        }

        // The reserve dwindles; ink spent meanwhile came out of the reserve first.
        public void Decay(float fill01, float decayPerSecond, float seconds)
        {
            if (Expanded || Capacity <= 1f) return;
            float spent = decayPerSecond > 0f && seconds > 0f ? decayPerSecond * seconds : 0f;
            float left = Mathf.Min(Capacity - 1f - spent, Excess(fill01, Capacity));
            Capacity = left > 0f ? 1f + left : 1f;
        }

        public void Clear() { Capacity = 1f; Expanded = false; }

        // Ink above the normal maximum when the pool reads fill01 under a coefficient.
        public static float Excess(float fill01, float capacity)
        {
            float above = Mathf.Clamp01(fill01) * capacity - 1f;
            return above > 0f ? above : 0f;
        }

        // What the pool's fill reads after its coefficient changes: the amount of ink is kept, what no longer fits is lost
        // (the same arithmetic as InkPool.SetTemporaryCapacity).
        public static float FillAfter(float fill01, float from, float to) => to > 0f ? Mathf.Clamp01(fill01 * from / to) : fill01;
    }
}
