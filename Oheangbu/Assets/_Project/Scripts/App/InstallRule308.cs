// PURE308
using System.Collections.Generic;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App
{
    // One installed harmony mark as the rule sees it (SPEC-SPELL-120-308 WP-10, COMBAT-INSTALL). The effect class keeps the
    // Unity half (the enemy, the glyph, the presentation handle) next to it, keyed by Serial.
    public struct InstallMark308
    {
        public long Serial;        // attach order: the smallest is the oldest
        public int TargetId;       // index in ISpellCastHost.Targets
        public uint Life;          // EnemyVitals.LifeRevision the mark is bound to: another life never detonates it
        public Element Element;    // element of the installing glyph (its initial)
        public float Power;        // detonation damage before the equipment scale
        public int Groggy;         // groggy steps one detonation adds
        public float ExpiresAt;    // the wiring's scaled clock
    }

    // Harmony install: attach to one enemy without damage, detonate on the next hit of an attack spell, expire unfired.
    // The five install glyphs share this rule; only the element differs.
    public static class InstallRule308
    {
        // Where a confirmed player-side hit came from, as far as a mark cares. Only an attack spell's own hit detonates.
        public enum Origin { AttackSpell, Summon, Persistent, Companion, Retaliation, Harmony, Harvest, Bonus, Other }

        public static bool Detonates(Origin origin, float appliedDamage) => origin == Origin.AttackSpell && appliedDamage > 0f;

        // The mark flies like a single-target spell: same speed law as the wiring's single judgement.
        public static float FlightSeconds(Vector3 from, Vector3 to, float projectileSpeed, float speedMul)
        {
            float speed = Mathf.Max(1f, projectileSpeed * speedMul);
            return Vector3.Distance(from, to) / speed;
        }

        // Brush law: the install row has no base power of its own (attaching deals nothing), so the detonation takes the
        // sheet value times the brush multiplier and the hold decay of the cast that installed it.
        public static float DetonationPower(float sheetPower, float brush, float hold)
        {
            float power = sheetPower * brush * hold;
            return float.IsNaN(power) || float.IsInfinity(power) || power < 0f ? 0f : power;
        }

        // The enemy the mark was thrown at is still that enemy (alive, same life).
        public static bool Holds(SpellActorSnap[] actors, int targetId, uint life)
        {
            return actors != null && targetId >= 0 && targetId < actors.Length && actors[targetId].Alive && actors[targetId].Life == life;
        }

        // Adds a mark. Over the cap the oldest marks make room (they are lost unfired) and are reported in replaced.
        public static void Attach(List<InstallMark308> marks, InstallMark308 mark, int max, List<InstallMark308> replaced)
        {
            if (max < 1) max = 1;
            while (marks.Count >= max)
            {
                int oldest = 0;
                for (int i = 1; i < marks.Count; i++) if (marks[i].Serial < marks[oldest].Serial) oldest = i;
                replaced?.Add(marks[oldest]);
                marks.RemoveAt(oldest);
            }
            marks.Add(mark);
        }

        // Takes the oldest live mark of that enemy and life out of the list (one call = one detonation). The caller loops
        // until false: a mark is removed before its detonation is executed, so a nested hit can never fire it twice.
        public static bool TakeForDetonation(List<InstallMark308> marks, int targetId, uint life, float now, out InstallMark308 mark)
        {
            int found = -1;
            for (int i = 0; i < marks.Count; i++)
            {
                if (marks[i].TargetId != targetId || marks[i].Life != life || now >= marks[i].ExpiresAt) continue;
                if (found < 0 || marks[i].Serial < marks[found].Serial) found = i;
            }
            if (found < 0) { mark = default; return false; }
            mark = marks[found]; marks.RemoveAt(found);
            return true;
        }

        // What becomes of an unfired mark this frame. held = its enemy is still alive in the life the mark is bound to.
        public enum Fate { Keep, Expired, Lost }

        public static Fate FateOf(in InstallMark308 mark, bool held, float now)
        {
            if (!held) return Fate.Lost;
            return now >= mark.ExpiresAt ? Fate.Expired : Fate.Keep;
        }

        // Unfired marks whose time ran out (expired) and marks whose enemy is gone or lives another life (lost).
        public static void Sweep(List<InstallMark308> marks, SpellActorSnap[] actors, float now, List<InstallMark308> expired, List<InstallMark308> lost)
        {
            for (int i = marks.Count - 1; i >= 0; i--)
            {
                var mark = marks[i];
                Fate fate = FateOf(mark, Holds(actors, mark.TargetId, mark.Life), now);
                if (fate == Fate.Keep) continue;
                (fate == Fate.Lost ? lost : expired)?.Add(mark);
                marks.RemoveAt(i);
            }
        }

        // Every mark on that enemy, whatever its life (the enemy was just defeated).
        public static void DropTarget(List<InstallMark308> marks, int targetId, List<InstallMark308> lost)
        {
            for (int i = marks.Count - 1; i >= 0; i--)
                if (marks[i].TargetId == targetId) { lost?.Add(marks[i]); marks.RemoveAt(i); }
        }
    }
}
