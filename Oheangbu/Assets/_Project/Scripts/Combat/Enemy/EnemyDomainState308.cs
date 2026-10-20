// PURE308
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.Combat
{
    // #308 WP-17 (SPEC-SPELL-120-308, D308-121 결계): what a player's domain does to one enemy standing in it. Hung on
    // EnemyVitals.Modifiers and cleared with that enemy's life. Every entry belongs to an owner (the domain zone's token)
    // and ends at its own time; the domain refreshes it every frame the enemy is inside and removes it the frame the enemy
    // leaves. The enemy's own code asks the readers at its seams (guard entry, ally call, telegraph, turn, dash, leash,
    // attack start, aim, regen). An enemy without an entry answers every reader with the neutral value.
    //   seal       no regeneration, no revival and no new floor hazard of this enemy           (눅)
    //   dash       dash and leap speed x value, 0 = the enemy cannot dash or leap at all        (묵)
    //   move       walking speed x value                                                        (숙 on a boss)
    //   leash      the enemy cannot leave the circle (centre, radius)                           (숙)
    //   telegraph  attack telegraph x value (above 1 = longer)                                  (욱)
    //   guard      the enemy cannot enter a guard (or reflect) stance; a stance already up stays (순)
    //   call       the enemy cannot call allies                                                 (뭇)
    //   turn       turn rate x value                                                            (웃)
    //   hold       the enemy cannot START an attack before the entry's time; one already started goes on (굿)
    //   aim        a ranged attack's aim locks at this share of its telegraph (0 = at its start) (눙)
    //   exclude    the enemy cannot enter the circle (centre, radius); it walks around it          (굼)
    // Nothing here deals damage, raises groggy, opens a weak point or changes whom the enemy targets.
    // Plain C#, no clock of its own (every call takes "now"): the offline runner executes this file.
    public sealed class EnemyDomainState308
    {
        public enum Kind { Seal, Dash, Move, Leash, Telegraph, Guard, Call, Turn, Hold, Aim, Exclude }
        struct Entry { public long Owner; public Kind Kind; public float Until, Value, Extra; public Vector3 Centre; }

        readonly List<Entry> _entries = new List<Entry>();

        public bool IsEmpty => _entries.Count == 0;
        public int Count => _entries.Count;

        // ---- writers (one entry per owner and kind: a refresh replaces it; a value that would change nothing is refused) ----

        public bool Seal(long owner, float until) => Set(owner, Kind.Seal, until, 1f, default);
        public bool Guard(long owner, float until) => Set(owner, Kind.Guard, until, 1f, default);
        public bool Call(long owner, float until) => Set(owner, Kind.Call, until, 1f, default);

        // scale in 0..1 and below 1 (0 = no dash at all).
        public bool Dash(long owner, float until, float scale) => scale >= 0f && scale < 1f && Set(owner, Kind.Dash, until, scale, default);
        public bool Move(long owner, float until, float scale) => scale > 0f && scale < 1f && Set(owner, Kind.Move, until, scale, default);
        // instantRate = the turn rate (degrees per second) an enemy that turns at once counts as while slowed (0 = it stays instant).
        public bool Turn(long owner, float until, float scale, float instantRate = 0f) => scale > 0f && scale < 1f && instantRate >= 0f && Set(owner, Kind.Turn, until, scale, default, instantRate);

        // scale above 1.
        public bool Telegraph(long owner, float until, float scale) => scale > 1f && Set(owner, Kind.Telegraph, until, scale, default);

        public bool Leash(long owner, float until, Vector3 centre, float radius) => radius > 0f && Set(owner, Kind.Leash, until, radius, centre);
        public bool Exclude(long owner, float until, Vector3 centre, float radius) => radius > 0f && Set(owner, Kind.Exclude, until, radius, centre);

        // No attack may start before `until` (the entry is its own end).
        public bool Hold(long owner, float until) => Set(owner, Kind.Hold, until, 1f, default);

        // share in 0..1 of the telegraph at which the aim locks.
        // missRadius = how far from the locked point a player must stand to be missed by an attack that has no radius of its own.
        public bool Aim(long owner, float until, float share, float missRadius = 0f) => share >= 0f && share <= 1f && missRadius >= 0f && Set(owner, Kind.Aim, until, share, default, missRadius);

        public void Remove(long owner)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
                if (_entries[i].Owner == owner) _entries.RemoveAt(i);
        }

        public void Remove(long owner, Kind kind)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
                if (_entries[i].Owner == owner && _entries[i].Kind == kind) _entries.RemoveAt(i);
        }

        public void Clear() => _entries.Clear();

        // ---- readers (expired entries are dropped as they are met) ----

        public bool Sealed(float now) => Any(Kind.Seal, now);
        public bool GuardBlocked(float now) => Any(Kind.Guard, now);
        public bool CallBlocked(float now) => Any(Kind.Call, now);
        public bool AttackHeld(float now) => Any(Kind.Hold, now);

        // The slowest entry counts (1 = untouched). Dash 0 = the enemy walks instead.
        public float DashScale(float now) => Lowest(Kind.Dash, now, 1f);
        public float MoveScale(float now) => Lowest(Kind.Move, now, 1f);
        public float TurnScale(float now) => Lowest(Kind.Turn, now, 1f);

        // The turn rate to use instead of `rate` (degrees per second, 0 = turns at once). The slowest entry counts.
        public float TurnRate(float rate, float now)
        {
            int at = LowestAt(Kind.Turn, now);
            if (at < 0) return rate;
            var entry = _entries[at];
            float from = rate > 0f ? rate : entry.Extra;
            return from > 0f ? from * entry.Value : rate;
        }

        // The longest entry counts (1 = untouched).
        public float TelegraphScale(float now)
        {
            float best = 1f;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (entry.Until <= now) { _entries.RemoveAt(i); continue; }
                if (entry.Kind == Kind.Telegraph && entry.Value > best) best = entry.Value;
            }
            return best;
        }

        // Share of the telegraph at which a ranged attack's aim locks; -1 = it tracks to the end as before. The earliest
        // entry counts; missRadius is that entry's.
        public float AimLockShare(float now) => AimLockShare(now, out _);
        public float AimLockShare(float now, out float missRadius)
        {
            float best = -1f; missRadius = 0f;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (entry.Until <= now) { _entries.RemoveAt(i); continue; }
                if (entry.Kind == Kind.Aim && (best < 0f || entry.Value < best)) { best = entry.Value; missRadius = entry.Extra; }
            }
            return best;
        }

        // The circle this enemy may not leave (radius 0 = free).
        public bool Leashed(float now, out Vector3 centre, out float radius)
        {
            centre = default; radius = 0f;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (entry.Until <= now) { _entries.RemoveAt(i); continue; }
                if (entry.Kind != Kind.Leash) continue;
                centre = entry.Centre; radius = entry.Value;
                return true;
            }
            return false;
        }

        // Where a step that wants to reach `next` may end: inside the leash circle and outside every circle that shuts this
        // enemy out (on the ground plane), else unchanged.
        public Vector3 Contain(Vector3 next, float now)
        {
            if (Leashed(now, out var centre, out float radius)) next = DomainLeashRule308.Contain(centre, radius, next);
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (entry.Until <= now) { _entries.RemoveAt(i); continue; }
                if (entry.Kind == Kind.Exclude) next = DomainLeashRule308.Exclude(entry.Centre, entry.Value, next);
            }
            return next;
        }

        public bool Excluded(float now) => Any(Kind.Exclude, now);

        // ---- internals ----

        bool Set(long owner, Kind kind, float until, float value, Vector3 centre, float extra = 0f)
        {
            if (owner <= 0 || !float.IsFinite(until) || !float.IsFinite(value) || !float.IsFinite(extra)) return false;
            var entry = new Entry { Owner = owner, Kind = kind, Until = until, Value = value, Extra = extra, Centre = centre };
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Owner == owner && _entries[i].Kind == kind) { _entries[i] = entry; return true; }
            _entries.Add(entry);
            return true;
        }

        bool Any(Kind kind, float now)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (entry.Until <= now) { _entries.RemoveAt(i); continue; }
                if (entry.Kind == kind) return true;
            }
            return false;
        }

        float Lowest(Kind kind, float now, float neutral)
        {
            int at = LowestAt(kind, now);
            return at >= 0 && _entries[at].Value < neutral ? _entries[at].Value : neutral;
        }

        // Index of the live entry of this kind with the lowest value (-1 = none). Expired entries are dropped first.
        int LowestAt(Kind kind, float now)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
                if (_entries[i].Until <= now) _entries.RemoveAt(i);
            int at = -1;
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Kind == kind && (at < 0 || _entries[i].Value < _entries[at].Value)) at = i;
            return at;
        }
    }

    // The leash arithmetic on its own: a point outside the circle (ground plane, height kept) is put back on its edge.
    public static class DomainLeashRule308
    {
        public static Vector3 Contain(Vector3 centre, float radius, Vector3 point)
        {
            if (!(radius > 0f)) return point;
            float dx = point.x - centre.x, dz = point.z - centre.z;
            float sq = dx * dx + dz * dz;
            if (sq <= radius * radius) return point;
            float scale = radius / Mathf.Sqrt(sq);
            return new Vector3(centre.x + dx * scale, point.y, centre.z + dz * scale);
        }

        // The other way round (굼): a point inside the circle is put out on its edge, straight away from the centre (a point on
        // the centre itself goes along +z). A point outside or on the edge is unchanged.
        public static Vector3 Exclude(Vector3 centre, float radius, Vector3 point)
        {
            if (!(radius > 0f)) return point;
            float dx = point.x - centre.x, dz = point.z - centre.z;
            float sq = dx * dx + dz * dz;
            if (sq >= radius * radius) return point;
            if (sq < 1e-8f) return new Vector3(centre.x, point.y, centre.z + radius);
            float scale = radius / Mathf.Sqrt(sq);
            return new Vector3(centre.x + dx * scale, point.y, centre.z + dz * scale);
        }
    }
}
