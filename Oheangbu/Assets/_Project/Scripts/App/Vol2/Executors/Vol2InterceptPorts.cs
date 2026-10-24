using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // S5-b slice 7: the enemy-shot seam of the 돌아온다 executor. The game's seam already exists (SPEC-SPELL-120-308 WP-13): ISpellInterceptable308 (position,
    // velocity, "is it still in the air", Intercept) and EnemyProjectileIntercept308, which answers it for the flying shot of today's EnemyController by stopping
    // that enemy's attack. What 엉 needs besides that is the shot's ORIGINAL power and its shooter (a reflected shot goes back to whoever fired it), so this file adds
    // IVol2Projectile on top of the old seam and a source that finds the shots in a vol.2 scope. Nothing here is static; nothing here changes the old seam or the enemy.

    /// <summary>An enemy shot in the air, for the vol.2 interception / reflection. <see cref="Identity"/> is the object the shot really is (the controller, the test
    /// target): the same shot always answers with the same identity, so a shot is taken once.</summary>
    public interface IVol2Projectile : ISpellInterceptable308
    {
        object Identity { get; }
        /// <summary>The shot's own power before any multiplier, in HP (the shooter's AttackDamage). 0 = unknown: such a shot is shot down but cannot be reflected.</summary>
        float PowerHp { get; }
        /// <summary>The enemy that fired it; null = nobody known.</summary>
        EnemyVitals Shooter { get; }
    }

    /// <summary>Where a vol.2 scope finds enemy shots. Gather ADDS to <paramref name="found"/> every shot in the air within <paramref name="reach"/> metres (on the
    /// plane) of <paramref name="centre"/>.</summary>
    public interface IVol2ProjectileSource
    {
        void Gather(Vol2CastCoordinator host, Vector3 centre, float reach, List<IVol2Projectile> found);
    }

    /// <summary>The flying shot of an EnemyController as an IVol2Projectile (power = the controller's AttackDamage, shooter = its own EnemyVitals).</summary>
    public sealed class Vol2EnemyShot : IVol2Projectile
    {
        readonly EnemyProjectileIntercept308 _inner;
        readonly EnemyController _controller;
        public Vol2EnemyShot(EnemyController controller) { _controller = controller; _inner = new EnemyProjectileIntercept308(controller); }
        public object Identity => _controller;
        public bool InFlight => _inner.InFlight;
        public Vector3 Position => _inner.Position;
        public Vector3 Velocity => _inner.Velocity;
        public bool Intercept(long owner) => _inner.Intercept(owner);
        public float PowerHp => _controller != null ? _controller.AttackDamage : 0f;
        public EnemyVitals Shooter => _controller != null ? _controller.GetComponent<EnemyVitals>() : null;
    }

    /// <summary>Any other ISpellInterceptable308 (the WP-13 test target): shot down like any other, but with no known power or shooter.</summary>
    public sealed class Vol2PlainShot : IVol2Projectile
    {
        readonly ISpellInterceptable308 _inner;
        public Vol2PlainShot(ISpellInterceptable308 inner) { _inner = inner; }
        public object Identity => _inner;
        public bool InFlight => _inner != null && !(_inner is UnityEngine.Object o && o == null) && _inner.InFlight;
        public Vector3 Position => _inner.Position;
        public Vector3 Velocity => _inner.Velocity;
        public bool Intercept(long owner) => _inner.Intercept(owner);
        public float PowerHp => 0f;
        public EnemyVitals Shooter => null;
    }

    /// <summary>The default source of a scope: shots a caller registered (the lab's, a fixture's), the flying shots of the scope's enemies (EnemyController.IsProjectileFlying
    /// through the target list) and the ISpellInterceptable308 colliders around the caster in the caster's own physics scene. A fixture / the lab switches the scans off
    /// and registers only its own shots. One instance per executor; nothing static.</summary>
    public sealed class Vol2ProjectileHub : IVol2ProjectileSource
    {
        readonly List<IVol2Projectile> _registered = new List<IVol2Projectile>();
        readonly Dictionary<EnemyController, Vol2EnemyShot> _enemyShots = new Dictionary<EnemyController, Vol2EnemyShot>();
        readonly Dictionary<ISpellInterceptable308, Vol2PlainShot> _plainShots = new Dictionary<ISpellInterceptable308, Vol2PlainShot>();
        Collider[] _buffer = new Collider[64];
        public bool ScanScene = true;
        public bool ScanPhysics = true;
        public int RegisteredCount => _registered.Count;

        public void Register(IVol2Projectile shot) { if (shot != null && !_registered.Contains(shot)) _registered.Add(shot); }
        public void Unregister(IVol2Projectile shot) { _registered.Remove(shot); }
        public void ClearRegistered() { _registered.Clear(); }

        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return (a - b).magnitude; }

        public void Gather(Vol2CastCoordinator host, Vector3 centre, float reach, List<IVol2Projectile> found)
        {
            for (int i = _registered.Count - 1; i >= 0; i--)
            {
                var s = _registered[i];
                if (s is UnityEngine.Object o && o == null) { _registered.RemoveAt(i); continue; }
                if (s.InFlight && Flat(centre, s.Position) <= reach) Add(found, s);
            }
            if (ScanScene && host != null)
            {
                var targets = host.Targets;
                for (int i = 0; targets != null && i < targets.Count; i++)
                {
                    var enemy = targets[i] as Vol2EnemyTarget;
                    if (enemy == null || enemy.Vitals == null) continue;
                    var controller = enemy.Vitals.GetComponent<EnemyController>();
                    if (controller == null || !controller.isActiveAndEnabled || !controller.IsProjectileFlying) continue;
                    if (!_enemyShots.TryGetValue(controller, out var shot)) { shot = new Vol2EnemyShot(controller); _enemyShots[controller] = shot; }
                    if (shot.InFlight && Flat(centre, shot.Position) <= reach) Add(found, shot);
                }
            }
            if (ScanPhysics) GatherPhysics(host, centre, reach, found);
        }

        void GatherPhysics(Vol2CastCoordinator host, Vector3 centre, float reach, List<IVol2Projectile> found)
        {
            var anchor = host != null && host.Caster != null ? host.Caster.Anchor : null;
            PhysicsScene physics = anchor != null && anchor.gameObject.scene.IsValid() ? anchor.gameObject.scene.GetPhysicsScene() : Physics.defaultPhysicsScene;
            if (!physics.IsValid()) return;
            int count = physics.OverlapSphere(centre, reach, _buffer, ~0, QueryTriggerInteraction.Collide);
            while (count >= _buffer.Length)
            {
                Array.Resize(ref _buffer, _buffer.Length * 2);
                count = physics.OverlapSphere(centre, reach, _buffer, ~0, QueryTriggerInteraction.Collide);
            }
            for (int i = 0; i < count; i++)
            {
                var col = _buffer[i];
                var seam = col != null ? col.GetComponentInParent<ISpellInterceptable308>() : null;
                if (seam == null || seam is IVol2Projectile || !seam.InFlight) continue;
                if (!_plainShots.TryGetValue(seam, out var plain)) { plain = new Vol2PlainShot(seam); _plainShots[seam] = plain; }
                Add(found, plain);
            }
            Array.Clear(_buffer, 0, count);
            if (_plainShots.Count > 128) Prune();
        }

        static void Add(List<IVol2Projectile> found, IVol2Projectile shot)
        {
            for (int i = 0; i < found.Count; i++) if (ReferenceEquals(found[i].Identity, shot.Identity)) return;
            found.Add(shot);
        }

        void Prune()
        {
            var gone = new List<ISpellInterceptable308>();
            foreach (var kv in _plainShots) if (kv.Key is UnityEngine.Object o && o == null) gone.Add(kv.Key);
            foreach (var k in gone) _plainShots.Remove(k);
        }
    }
}
