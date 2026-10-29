using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>A body the shapes can touch. A stable id plus the life revision (never a list index, design section 4). Position is the feet,
    /// Previous is the position one tick ago (the ring judges the body's path, not its feet at one instant).</summary>
    public interface IVol2Target
    {
        long Id { get; }
        long LifeRevision { get; }
        Vector3 Position { get; }
        Vector3 Previous { get; }
        float Radius { get; }
        bool Alive { get; }
        string Name { get; }
    }

    /// <summary>The enemy as a target. Id = a positive number the registry gave this EnemyVitals when it first saw it (stable while the object lives; pooled
    /// objects change LifeRevision). NOT the Unity instance id: runtime-created objects have NEGATIVE instance ids and the rules refuse a TargetId <= 0.</summary>
    public sealed class Vol2EnemyTarget : IVol2Target
    {
        public EnemyVitals Vitals { get; }
        public long Id { get; }
        public long LifeRevision => Vitals != null ? Vitals.LifeRevision : 0;
        public Vector3 Position { get; private set; }
        public Vector3 Previous { get; private set; }
        public float Radius => Vitals != null ? Vitals.BodyRadius : 0.4f;
        public bool Alive => Vitals != null && Vitals.isActiveAndEnabled && Vitals.IsAlive;
        public string Name => Vitals != null ? Vitals.name : "(destroyed)";
        bool _seen;
        public Vol2EnemyTarget(EnemyVitals vitals, long id) { Vitals = vitals; Id = id; }
        /// <summary>Called once per tick: Previous becomes the last Position (the first time, the current one).</summary>
        public void Refresh()
        {
            if (Vitals == null) return;
            Vector3 now = Vitals.transform.position;
            Previous = _seen ? Position : now; Position = now; _seen = true;
        }
    }

    /// <summary>Who the shapes can touch in this scope. The scene's enemies come from <see cref="SceneSource"/> (the wiring's target list); the lab
    /// adds its runtime dummies to <see cref="Extras"/> (the wiring's own list is private and never sees them). While
    /// <see cref="ExtrasOnly"/> is on, only the extras are candidates (the lab scene's own dummies must not join a scenario).
    /// One instance per scope; nothing static.</summary>
    public sealed class Vol2TargetRegistry
    {
        readonly Dictionary<int, Vol2EnemyTarget> _cache = new Dictionary<int, Vol2EnemyTarget>();
        readonly List<EnemyVitals> _extras = new List<EnemyVitals>();
        readonly List<IVol2Target> _snapshot = new List<IVol2Target>();
        long _nextId;
        public Func<IReadOnlyList<EnemyVitals>> SceneSource { get; set; }
        public bool ExtrasOnly { get; set; }
        /// <summary>Targets that are not enemies (an edit-mode fixture feeds fakes here). Always included; null in the game.</summary>
        public Func<IEnumerable<IVol2Target>> Synthetic { get; set; }
        public IReadOnlyList<EnemyVitals> Extras => _extras;

        public void AddExtra(EnemyVitals v) { if (v != null && !_extras.Contains(v)) _extras.Add(v); }
        public void RemoveExtra(EnemyVitals v) { _extras.Remove(v); }
        public void ClearExtras() { _extras.Clear(); }

        /// <summary>Rebuilds the candidate list (alive, enabled) and advances every target's Previous. Call once per tick.</summary>
        public IReadOnlyList<IVol2Target> Refresh()
        {
            _snapshot.Clear();
            if (!ExtrasOnly && SceneSource != null)
            {
                var scene = SceneSource();
                if (scene != null) for (int i = 0; i < scene.Count; i++) Add(scene[i]);
            }
            for (int i = 0; i < _extras.Count; i++) Add(_extras[i]);
            if (Synthetic != null) foreach (var t in Synthetic()) if (t != null && t.Alive) _snapshot.Add(t);
            return _snapshot;
        }

        void Add(EnemyVitals v)
        {
            if (v == null) return;
            int id = v.GetInstanceID();
            if (!_cache.TryGetValue(id, out var t)) { t = new Vol2EnemyTarget(v, checked(++_nextId)); _cache.Add(id, t); }
            t.Refresh();
            if (t.Alive && !ContainsId(id)) _snapshot.Add(t);
        }

        bool ContainsId(int instanceId) { return _cache.TryGetValue(instanceId, out var c) && _snapshot.Contains(c); }

        /// <summary>The TargetId this enemy has (0 = never seen).</summary>
        public long IdOf(EnemyVitals v) => v != null && _cache.TryGetValue(v.GetInstanceID(), out var t) ? t.Id : 0;

        public IVol2Target Find(long id)
        {
            for (int i = 0; i < _snapshot.Count; i++) if (_snapshot[i].Id == id) return _snapshot[i];
            return null;
        }

        /// <summary>Drops cache entries of destroyed enemies.</summary>
        public void Prune()
        {
            List<int> dead = null;
            foreach (var p in _cache) if (p.Value.Vitals == null) (dead ?? (dead = new List<int>())).Add(p.Key);
            if (dead != null) foreach (int k in dead) _cache.Remove(k);
            _extras.RemoveAll(e => e == null);
        }
    }

    /// <summary>The caster's pose when a cast is accepted. Production wraps the combat wiring; a fixture fakes it.</summary>
    public interface IVol2Caster
    {
        Vector3 Position { get; }
        /// <summary>Flat unit aim direction: toward the lock-on target if any, else the camera's flat forward, else the body's.</summary>
        Vector3 AimDirection { get; }
        /// <summary>The object whose colliders the wall check ignores, and the attacker object handed to the damage sink.</summary>
        Transform Anchor { get; }
    }

    public sealed class Vol2WiringCaster : IVol2Caster
    {
        readonly CombatLoopWiring _wiring;
        public Vol2WiringCaster(CombatLoopWiring wiring) { _wiring = wiring; }
        public Transform Anchor => _wiring != null ? _wiring.SummonPlayer : null;
        public Vector3 Position => Anchor != null ? Anchor.position : Vector3.zero;
        public Vector3 AimDirection
        {
            get
            {
                var lockOn = _wiring != null ? _wiring.SummonLockTarget : null;
                if (lockOn != null && lockOn.IsAlive)
                {
                    Vector3 to = lockOn.transform.position - Position; to.y = 0f;
                    if (to.sqrMagnitude > 1e-4f) return to.normalized;
                }
                var cam = Camera.main;
                if (cam != null) return Vol2ShapeGeometry.FlatUnit(cam.transform.forward);
                return Anchor != null ? Vol2ShapeGeometry.FlatUnit(Anchor.forward) : Vector3.forward;
            }
        }
    }

    /// <summary>The wall check of the path: how far from <c>from</c> toward <c>to</c> before something solid that is not an enemy or the caster.</summary>
    public interface IVol2Obstruction
    {
        bool TryBlock(Vector3 from, Vector3 to, out float distance);
    }

    public sealed class Vol2PhysicsObstruction : IVol2Obstruction
    {
        readonly Transform _ignoreRoot;
        readonly int _mask;
        readonly RaycastHit[] _hits = new RaycastHit[32];
        /// <param name="mask">default 1 = the Default layer, the same layer the old line-of-sight check uses (TargetVisible).</param>
        public Vol2PhysicsObstruction(Transform ignoreRoot, int mask = 1) { _ignoreRoot = ignoreRoot; _mask = mask; }
        public bool TryBlock(Vector3 from, Vector3 to, out float distance)
        {
            distance = 0f;
            Vector3 delta = to - from; float len = delta.magnitude;
            if (len < 1e-4f) return false;
            int n = Physics.RaycastNonAlloc(from, delta / len, _hits, len, _mask, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                var col = _hits[i].collider;
                if (col == null || col.GetComponentInParent<EnemyVitals>() != null) continue;     // an enemy is a target, not a wall
                if (_ignoreRoot != null && col.transform.IsChildOf(_ignoreRoot)) continue;
                if (_hits[i].distance < best) best = _hits[i].distance;
            }
            if (float.IsPositiveInfinity(best)) return false;
            distance = best; return true;
        }
    }
}
