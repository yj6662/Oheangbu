using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // #308 WP-05 shared runtime of the enemy control glyphs (interrupt, freeze, undertow). Control is written to
    // EnemyControlState only (owner id = the attack id of the confirmed hit): no weak point is opened, no damage is
    // amplified, no groggy is added. The ledger remembers what its handler applied so that a clear (player death, rest,
    // scene leave, disable) takes exactly those controls back, as the giyeok runtime does for its root.
    public sealed class SpellControlLedger308
    {
        struct Held { public EnemyVitals Target; public long Owner; public float Until; public ISpellAirborne308 Pulled; }
        readonly List<Held> _held = new List<Held>();

        public int Count => _held.Count;

        // speed = movement scale 0..1 while the control lasts. blocksActions = the enemy cannot act: its attack in progress
        // (telegraph included) is cancelled now. pullDown = an airborne target is also grounded until the same time.
        public bool Apply(EnemyVitals target, long owner, float until, float speed, bool blocksActions, bool pullDown = false)
        {
            if (target == null || !target.IsAlive || owner <= 0 || !float.IsFinite(until) || !float.IsFinite(speed)) return false;
            target.Control.Apply(owner, until, Mathf.Clamp01(speed), blocksActions);
            if (blocksActions)
            {
                var controller = target.GetComponent<EnemyController>();
                if (controller != null) controller.StopAttack();
            }
            ISpellAirborne308 pulled = null;
            if (pullDown)
            {
                pulled = target.GetComponent<ISpellAirborne308>();
                if (pulled != null && !pulled.PullDown(owner, until)) pulled = null;
            }
            _held.Add(new Held { Target = target, Owner = owner, Until = until, Pulled = pulled });
            return true;
        }

        // The control of that owner ends now (a freeze that was broken). false = this ledger holds no such control.
        public bool Release(long owner)
        {
            for (int i = 0; i < _held.Count; i++)
            {
                if (_held[i].Owner != owner) continue;
                Drop(_held[i]);
                _held.RemoveAt(i);
                return true;
            }
            return false;
        }

        // Forgets controls that ran out (EnemyControlState lets them go by itself at the same time).
        public void Expire(float now)
        {
            for (int i = _held.Count - 1; i >= 0; i--)
                if (now >= _held[i].Until) _held.RemoveAt(i);
        }

        // Takes back every control this ledger applied.
        public void Clear()
        {
            for (int i = 0; i < _held.Count; i++) Drop(_held[i]);
            _held.Clear();
        }

        static void Drop(in Held held)
        {
            if (held.Target == null) return;        // destroyed with its scene
            held.Target.Control.Remove(held.Owner);
            if (held.Pulled != null) held.Pulled.Release(held.Owner);
        }
    }
}
