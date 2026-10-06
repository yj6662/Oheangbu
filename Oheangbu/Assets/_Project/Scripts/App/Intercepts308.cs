using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // The flying projectile of today's EnemyController as an interceptable (SPEC-SPELL-120-308 WP-13). That projectile
    // has no object of its own to hang a component on (its hit is judged by the impact time, not by a collision), so the
    // seam is answered by the controller: intercepting it stops that enemy's attack, which takes the projectile out of
    // the air (EnemyController.StopAttack, the same call the interrupt glyph uses). Nothing in the enemy's AI is changed.
    public sealed class EnemyProjectileIntercept308 : ISpellInterceptable308
    {
        readonly EnemyController _controller;

        public EnemyProjectileIntercept308(EnemyController controller) { _controller = controller; }

        public bool InFlight => _controller != null && _controller.isActiveAndEnabled && _controller.IsProjectileFlying;

        public Vector3 Position
        {
            get
            {
                if (_controller == null) return default;
                var shot = _controller.ProjectileTransform;
                return shot != null ? shot.position : _controller.transform.position;
            }
        }

        // It flies at the player: the direction is all the rule needs.
        public Vector3 Velocity
        {
            get
            {
                var player = _controller != null ? _controller.PlayerTarget : null;
                return player != null ? player.position - Position : Vector3.zero;
            }
        }

        public bool Intercept(long owner)
        {
            if (!InFlight) return false;
            _controller.StopAttack();
            return true;
        }
    }

    // Unity half shared by the two interception handlers: what is in the air around the caster, as snapshots for the rule.
    public static class Intercepts308
    {
        // Everything that can be shot down within reach of the caster: the flying projectiles of the wiring's own enemies,
        // and every ISpellInterceptable308 found through its collider in the caster's own physics scene.
        public static void Gather(ISpellCastHost host, float reach, List<ISpellInterceptable308> found, ref Collider[] buffer)
        {
            found.Clear();
            if (host == null) return;
            var targets = host.Targets;
            for (int i = 0; targets != null && i < targets.Count; i++)
            {
                var enemy = targets[i];
                if (enemy == null || !enemy.isActiveAndEnabled) continue;
                var controller = enemy.GetComponent<EnemyController>();
                if (controller != null && controller.isActiveAndEnabled && controller.IsProjectileFlying) found.Add(new EnemyProjectileIntercept308(controller));
            }
            if (host.Player == null || buffer == null || buffer.Length == 0) return;
            var scene = host.Player.gameObject.scene;
            PhysicsScene physics = scene.IsValid() ? scene.GetPhysicsScene() : Physics.defaultPhysicsScene;
            if (!physics.IsValid()) return;
            Vector3 centre = host.PlayerPosition;
            int count = physics.OverlapSphere(centre, reach, buffer, ~0, QueryTriggerInteraction.Collide);
            while (count >= buffer.Length)
            {
                Array.Resize(ref buffer, buffer.Length * 2);
                count = physics.OverlapSphere(centre, reach, buffer, ~0, QueryTriggerInteraction.Collide);
            }
            for (int i = 0; i < count; i++)
            {
                var shot = buffer[i] != null ? buffer[i].GetComponentInParent<ISpellInterceptable308>() : null;
                if (shot != null && shot.InFlight && !found.Contains(shot)) found.Add(shot);
            }
            Array.Clear(buffer, 0, count);
        }

        public static void Snap(List<ISpellInterceptable308> found, List<SpellProjectileSnap> snaps)
        {
            snaps.Clear();
            for (int i = 0; i < found.Count; i++) snaps.Add(new SpellProjectileSnap(i, found[i].Position, found[i].Velocity, found[i].InFlight));
        }
    }

    // Interceptions that are on their way: each is carried out when its time comes, if the projectile is still in the air.
    public sealed class InterceptQueue308
    {
        struct Held { public ISpellInterceptable308 Shot; public float At; public long Owner; public int Fx; }
        readonly List<Held> _held = new List<Held>();

        public int Count => _held.Count;
        public int Downed { get; private set; }            // projectiles shot down so far (checks read this)

        public void Add(ISpellInterceptable308 shot, float at, long owner, int fx)
        {
            if (shot != null) _held.Add(new Held { Shot = shot, At = at, Owner = owner, Fx = fx });
        }

        // Carries out everything that is due. A projectile that already landed, or was shot down by another cast, is a miss.
        public void Fire(float now, ISpellPresenter fx)
        {
            for (int i = _held.Count - 1; i >= 0; i--)
            {
                var held = _held[i];
                if (now < held.At) continue;
                _held.RemoveAt(i);
                if (held.Shot is UnityEngine.Object gone && gone == null) continue;   // destroyed with its scene
                Vector3 point = held.Shot.Position;
                bool downed = held.Shot.InFlight && held.Shot.Intercept(held.Owner);
                if (downed) Downed++;
                if (held.Fx == 0 || fx == null) continue;
                fx.Cue(new SpellFxHandle(held.Fx), downed ? SpellFxCue.Intercept : SpellFxCue.Miss, new SpellFxCueArgs { Point = point, At = now });
            }
        }

        public void Clear() { _held.Clear(); }
    }
}
