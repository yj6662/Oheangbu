using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Oheangbu.Combat
{
    // S5-b slice 2: small lookup books of the defence side. Instances, never statics (Enter Play Mode keeps statics between runs).

    /// <summary>Interns strings as positive long ids (1, 2, 3 ...). The same string always answers the same id.</summary>
    public sealed class Vol2KeyBook
    {
        readonly Dictionary<string, long> _ids = new Dictionary<string, long>(StringComparer.Ordinal);
        public int Count => _ids.Count;
        public long Id(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (!_ids.TryGetValue(key, out long id)) { id = _ids.Count + 1; _ids.Add(key, id); }
            return id;
        }
    }

    /// <summary>Stable ids of enemies (design section 4: a TargetId is stable, never a list index). Two kinds:
    /// Id = per scope, from the object instance (posture, parry: one fight);
    /// StableId = from the scene and the hierarchy path WITH sibling indices, interned in the session book - the same enemy keeps its id
    /// when its scene is reloaded, so the 갈무리 ledger does not refill by a reload (the name-path "first sibling" trap is why indices are in).</summary>
    public sealed class Vol2TargetBook
    {
        readonly Dictionary<int, long> _byInstance = new Dictionary<int, long>();
        readonly Vol2KeyBook _stable;
        public Vol2TargetBook(Vol2KeyBook stableSessionBook = null) { _stable = stableSessionBook ?? new Vol2KeyBook(); }
        public long Id(EnemyVitals target)
        {
            if (target == null) return 0;
            int key = target.GetInstanceID();
            if (!_byInstance.TryGetValue(key, out long id)) { id = _byInstance.Count + 1; _byInstance.Add(key, id); }
            return id;
        }
        public long StableId(EnemyVitals target)
        {
            if (target == null) return 0;
            return _stable.Id(StableKey(target));
        }
        public static string StableKey(Component c)
        {
            var sb = new StringBuilder();
            for (var t = c.transform; t != null; t = t.parent) sb.Insert(0, "/" + t.name + "#" + t.GetSiblingIndex());
            var scene = c.gameObject.scene;
            return (scene.IsValid() ? scene.path : "") + sb;
        }
    }

    /// <summary>What a parry needs to know about an enemy attack and the impact cannot tell: whether it is a projectile and how long it was
    /// telegraphed. Filled from EnemyController.AttackTelegraphed (EnemyAttackCue); the key is the AttackId. An attack without an entry is
    /// unknown (only the authored controller fires the cue) - the parry service treats it as a projectile, which the rules never defend.</summary>
    public sealed class Vol2TelegraphBook
    {
        public readonly struct Entry
        {
            public readonly bool Projectile;
            public readonly long TelegraphUs;
            public readonly EnemyAttackDelivery Delivery;
            public Entry(bool projectile, long telegraphUs, EnemyAttackDelivery delivery) { Projectile = projectile; TelegraphUs = telegraphUs; Delivery = delivery; }
        }
        const int Capacity = 256;
        readonly Dictionary<long, Entry> _byAttack = new Dictionary<long, Entry>();
        readonly Queue<long> _order = new Queue<long>();
        public int Count => _byAttack.Count;
        public void Record(EnemyAttackCue cue)
        {
            long id = cue.Attack.AttackId;
            if (id <= 0) return;
            bool projectile = cue.Delivery == EnemyAttackDelivery.HomingProjectile || cue.Delivery == EnemyAttackDelivery.AimedProjectile;
            long us = Oheangbu.Vol2.Rules.Vol2Time.FromSeconds(Math.Max(0f, cue.Duration));
            if (!_byAttack.ContainsKey(id)) _order.Enqueue(id);
            _byAttack[id] = new Entry(projectile, us, cue.Delivery);
            while (_order.Count > Capacity) _byAttack.Remove(_order.Dequeue());
        }
        public bool TryGet(long attackId, out Entry entry) => _byAttack.TryGetValue(attackId, out entry);
        public void Clear() { _byAttack.Clear(); _order.Clear(); }
    }
}
