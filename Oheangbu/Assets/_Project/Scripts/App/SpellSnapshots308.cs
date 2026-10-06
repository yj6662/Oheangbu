using System.Collections.Generic;
using Oheangbu.Combat;

namespace Oheangbu.App
{
    // EnemyVitals <-> SpellActorSnap, the Unity half every effect class needs around its pure rule
    // (SPEC-SPELL-120-308 section 4). Id = the index in ISpellCastHost.Targets at the moment of the snapshot.
    public static class SpellSnapshots308
    {
        // One snapshot per target slot, in order. A missing, disabled or dead enemy is a slot with Alive = false.
        public static SpellActorSnap[] Take(IReadOnlyList<EnemyVitals> targets)
        {
            int count = targets != null ? targets.Count : 0;
            var snaps = new SpellActorSnap[count];
            for (int i = 0; i < count; i++)
            {
                var enemy = targets[i];
                bool present = enemy != null && enemy.isActiveAndEnabled;
                snaps[i] = present
                    ? new SpellActorSnap(i, enemy.transform.position, enemy.IsAlive, enemy.LifeRevision, enemy.Hp01, enemy.IsBoss)
                    : new SpellActorSnap(i, default, false, 0, 0f, false);
            }
            return snaps;
        }

        // The same snapshot written into the caller's buffer: nothing is allocated while the number of target slots stays the
        // same (the array is exactly as long as the target list, as Take's is, because rules read its Length). A handler that
        // looks at the targets every frame, or on every hit, keeps one buffer and calls this.
        public static SpellActorSnap[] Fill(IReadOnlyList<EnemyVitals> targets, ref SpellActorSnap[] buffer)
        {
            int count = targets != null ? targets.Count : 0;
            if (buffer == null || buffer.Length != count) buffer = new SpellActorSnap[count];
            for (int i = 0; i < count; i++)
            {
                var enemy = targets[i];
                bool present = enemy != null && enemy.isActiveAndEnabled;
                buffer[i] = present
                    ? new SpellActorSnap(i, enemy.transform.position, enemy.IsAlive, enemy.LifeRevision, enemy.Hp01, enemy.IsBoss)
                    : new SpellActorSnap(i, default, false, 0, 0f, false);
            }
            return buffer;
        }

        public static int IndexOf(IReadOnlyList<EnemyVitals> targets, EnemyVitals target)
        {
            if (targets != null && target != null)
                for (int i = 0; i < targets.Count; i++) if (targets[i] == target) return i;
            return -1;
        }

        // The enemy an order was made for, or null when that slot now holds another life (died, restored, replaced).
        public static EnemyVitals Target(IReadOnlyList<EnemyVitals> targets, int id, uint life)
        {
            if (targets == null || id < 0 || id >= targets.Count) return null;
            var enemy = targets[id];
            return enemy != null && enemy.isActiveAndEnabled && enemy.IsAlive && enemy.LifeRevision == life ? enemy : null;
        }
    }
}
