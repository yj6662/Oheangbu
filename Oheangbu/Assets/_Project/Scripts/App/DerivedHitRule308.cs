// PURE308
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // Shared bookkeeping of the effects that derive something from a confirmed hit (SPEC-SPELL-120-308 WP-04).
    // Pure: attack ids, clocks and numbers only. The effect classes own the Unity half (who the enemy is, the damage call).

    // One scheduled impact an effect waits for. Nothing is derived until the wiring reports that very attack id as a
    // confirmed hit (applied damage above zero): a shot into the air, a shot whose target died first and a shot a wall
    // stopped derive nothing.
    public struct DerivedHitWatch308
    {
        public long AttackId;     // PlannedHit.AttackId of the scheduled impact
        public float ImpactAt;    // its scheduled time (the wiring's scaled clock)
        public float Power;       // its scheduled power: the cast power with the equipment scale already inside
        public Vector3 Origin;    // where the shot left; its flat travel direction is impact point - Origin
        public Element Element;
        public char Letter;       // carried from the cast for the damage call, never compared
        public SpellRow Row;      // the numbers of the cast that made it
        public float Cost;        // nominal ink cost of that cast (base capacity units)
        public int Shot;          // ordinal of this shot inside its cast (0 for a single shot)
        public int Fx;            // presenter handle id of the cast (0 = nothing shown)
        public bool Due;          // Sweep has seen the impact time pass
    }

    public sealed class DerivedHitWatchList308
    {
        readonly List<DerivedHitWatch308> _items = new List<DerivedHitWatch308>();

        public int Count => _items.Count;

        // A scheduled impact always has a positive attack id; anything else (a visual-only shot) is not watched.
        public void Add(DerivedHitWatch308 watch)
        {
            if (watch.AttackId > 0) _items.Add(watch);
        }

        // The watch of a confirmed hit, exactly once: the second report of the same attack id finds nothing.
        public bool TryTake(long attackId, out DerivedHitWatch308 watch)
        {
            if (attackId > 0)
                for (int i = 0; i < _items.Count; i++)
                    if (_items[i].AttackId == attackId) { watch = _items[i]; _items.RemoveAt(i); return true; }
            watch = default;
            return false;
        }

        // Forgets the watches whose impact never came. The wiring applies a scheduled hit right after the effects tick, so a
        // watch survives the tick in which its time passes and is dropped on the next one. Returns how many were dropped.
        public int Sweep(float now)
        {
            int dropped = 0;
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var watch = _items[i];
                if (watch.Due) { _items.RemoveAt(i); dropped++; }
                else if (now >= watch.ImpactAt) { watch.Due = true; _items[i] = watch; }
            }
            return dropped;
        }

        public void Clear() => _items.Clear();
    }

    // Things that happen at a later time of the wiring's clock (a derived shot landing, a returning shot arriving).
    // The owner keeps the clock itself because only ScheduleHit has one, and ScheduleHit always judges line of sight from
    // the player; a derived shot leaves from where the first one landed.
    public sealed class DerivedTimedQueue308<T>
    {
        readonly List<float> _at = new List<float>();
        readonly List<T> _items = new List<T>();

        public int Count => _items.Count;

        public void Add(float at, T item)
        {
            if (float.IsNaN(at)) return;
            _at.Add(at); _items.Add(item);
        }

        // Moves everything due at now into the list, earliest first (ties keep the order they were added in).
        public int TakeDue(float now, List<T> into)
        {
            int taken = 0;
            while (true)
            {
                int best = -1;
                for (int i = 0; i < _at.Count; i++)
                    if (_at[i] <= now && (best < 0 || _at[i] < _at[best])) best = i;
                if (best < 0) return taken;
                into.Add(_items[best]);
                _at.RemoveAt(best); _items.RemoveAt(best);
                taken++;
            }
        }

        public void Clear() { _at.Clear(); _items.Clear(); }
    }

    public static class DerivedHitRule308
    {
        const int StackActors = 256;      // above this many target slots Near falls back to a heap array (not a balance number)

        // Living enemies inside radius of the point, nearest first (ties by id), without the ids in skip.
        public static void Near(SpellActorSnap[] actors, Vector3 point, float radius, IReadOnlyList<int> skip, List<int> ids)
        {
            ids.Clear();
            if (actors == null || float.IsNaN(radius) || radius <= 0f) return;
            // distances of the ids found so far, in the same order: on the stack (a hit asks this; no list per call)
            System.Span<float> distances = actors.Length <= StackActors ? stackalloc float[actors.Length] : new float[actors.Length];
            foreach (var actor in actors)
            {
                if (!actor.Alive || Skipped(skip, actor.Id)) continue;
                if (!AreaGeometry.InCircle(point, actor.Position, radius, out float distance)) continue;
                int at = ids.Count;
                while (at > 0 && distances[at - 1] > distance) at--;
                for (int k = ids.Count; k > at; k--) distances[k] = distances[k - 1];
                ids.Insert(at, actor.Id); distances[at] = distance;
            }
        }

        // Seconds a derived shot needs from one point to another (flat distance, as every area judgement measures).
        public static float Flight(Vector3 from, Vector3 to, float speed)
        {
            if (float.IsNaN(speed) || speed <= 0f) return 0f;
            return AreaGeometry.Flat(to - from).magnitude / speed;
        }

        // The snapshot with this id (normally the array slot of the same number).
        public static bool TryFind(SpellActorSnap[] actors, int id, out SpellActorSnap actor)
        {
            if (actors != null)
            {
                if (id >= 0 && id < actors.Length && actors[id].Id == id) { actor = actors[id]; return true; }
                for (int i = 0; i < actors.Length; i++) if (actors[i].Id == id) { actor = actors[i]; return true; }
            }
            actor = default;
            return false;
        }

        static bool Skipped(IReadOnlyList<int> skip, int id)
        {
            if (skip != null)
                for (int i = 0; i < skip.Count; i++) if (skip[i] == id) return true;
            return false;
        }
    }
}
