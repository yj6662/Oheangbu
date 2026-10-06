using UnityEngine;

namespace Oheangbu.App
{
    // #308 WP-13, question Q5 (USER_ANSWERS, DECISIONS D308-13): "the spell rule plus a TEST target; real enemy AI is
    // separate work". The interception glyphs shoot enemy projectiles out of the air. This file holds the seam the spells
    // talk to and one TEST target that implements it. A projectile built later (its own object with a collider)
    // implements the same members; the flying projectile of today's EnemyController is reached through
    // EnemyProjectileIntercept308 (Intercepts308.cs), which answers the seam by stopping that enemy's attack.
    //   - A cast finds a projectile object through physics: it needs a collider (a trigger is fine). No registry, no static list.
    public interface ISpellInterceptable308
    {
        bool InFlight { get; }              // still in the air and able to be shot down
        Vector3 Position { get; }
        Vector3 Velocity { get; }           // where it flies (a direction is enough)
        // Frozen and dropped by a cast. owner = the identity of that cast. false = it was gone already.
        bool Intercept(long owner);
    }

    // TEST target for the interception clauses: a dummy projectile that flies in a straight line and hurts nobody. When a
    // spell intercepts it, it freezes (it stops flying forward) and drops to the ground, where it stays as a dead object.
    // It carries no AI and deals no damage. The motion is this dummy's own picture; spell rules read only the seam.
    [DisallowMultipleComponent]
    public sealed class SpellProjectileTestTarget308 : MonoBehaviour, ISpellInterceptable308
    {
        public const int QueryCapacity = 32;   // first size of a cast's overlap buffer (it grows when a query fills it)

        [Tooltip("TEST: flight velocity in metres per second (world space)")]
        [SerializeField] private Vector3 _velocity = new Vector3(0f, 0f, -6f);
        [Tooltip("TEST: how fast a frozen dummy drops, in metres per second")]
        [SerializeField, Min(0f)] private float _dropSpeed = 6f;
        [Tooltip("TEST: height of the ground the frozen dummy drops to (world y)")]
        [SerializeField] private float _groundHeight;

        private bool _frozen;

        public bool InFlight => !_frozen && isActiveAndEnabled;
        public Vector3 Position => transform.position;
        public Vector3 Velocity => _velocity;
        public bool Frozen => _frozen;
        public long InterceptedBy { get; private set; }     // the cast that shot it down (checks read this)

        public bool Intercept(long owner)
        {
            if (_frozen) return false;
            _frozen = true; InterceptedBy = owner;
            return true;
        }

        // Puts the dummy back into the air (a check's reset).
        public void Relaunch(Vector3 position, Vector3 velocity)
        {
            transform.position = position; _velocity = velocity; _frozen = false; InterceptedBy = 0;
        }

        public void Configure(Vector3 velocity, float dropSpeed, float groundHeight)
        { _velocity = velocity; _dropSpeed = Mathf.Max(0f, dropSpeed); _groundHeight = groundHeight; }

        private void Update()
        {
            Vector3 position = transform.position;
            if (!_frozen) position += _velocity * Time.deltaTime;
            else position.y = Mathf.MoveTowards(position.y, _groundHeight, _dropSpeed * Time.deltaTime);
            transform.position = position;
        }

        // A TEST projectile built from a primitive (no asset): a small trigger sphere. Callers make it in Play or in a
        // preview scene only: nothing here keeps it out of a scene that gets saved.
        public static SpellProjectileTestTarget308 CreateTest(Vector3 position, Vector3 velocity)
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Spell308_TestProjectile";
            ball.transform.position = position;
            ball.transform.localScale = Vector3.one * .35f;
            var body = ball.GetComponent<Collider>();
            if (body != null) body.isTrigger = true;
            var projectile = ball.AddComponent<SpellProjectileTestTarget308>();
            projectile.Configure(velocity, projectile._dropSpeed, position.y - 1f);
            return projectile;
        }
    }
}
