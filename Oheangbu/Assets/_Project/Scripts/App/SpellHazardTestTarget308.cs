using System;
using UnityEngine;

namespace Oheangbu.App
{
    // #308 WP-13, question Q5 (USER_ANSWERS, DECISIONS D308-13): "the spell rule plus a TEST target; real enemy AI and world
    // placement are separate work". The cleansing wave wipes enemy hazards and remnants on its path. No enemy of the game
    // leaves one on the ground yet, so this file holds the seam the spell talks to and one TEST target that implements it.
    // A hazard built later (a poison pool, a burning patch, a remnant) implements the same members.
    //   - A cast finds hazards through physics: the hazard needs a collider (a trigger is fine) on itself or on a child.
    //     No registry and no static list.
    public interface ISpellPurgeable308
    {
        Vector3 Position { get; }
        float Radius { get; }               // how far it reaches from its position, in metres
        bool Purged { get; }
        // Wiped by a cast. owner = the identity of that cast's wave. false = it was gone already.
        bool Purge(long owner);
    }

    // TEST target for the cleansing clause: a patch on the ground that does nothing to anyone and can be wiped once.
    // The optional body is only this dummy's own picture (a child that is switched off when wiped); spell rules never read it.
    // No light and no emission is driven from here.
    [DisallowMultipleComponent]
    public sealed class SpellHazardTestTarget308 : MonoBehaviour, ISpellPurgeable308
    {
        public const int QueryCapacity = 32;   // first size of a cast's overlap buffer (it grows when a query fills it)

        [Tooltip("TEST: how far the patch reaches from its pivot, in metres")]
        [SerializeField, Min(0f)] private float _radius = 1.5f;
        [Tooltip("Optional child that is shown while the patch lies there (picture only)")]
        [SerializeField] private GameObject _body;
        [SerializeField] private bool _purged;

        public Vector3 Position => transform.position;
        public float Radius => _radius;
        public bool Purged => _purged;
        public long PurgedBy { get; private set; }      // the wave that wiped it (checks read this)

        // Read-only re-broadcast: the patch was wiped by a cast. No rule hangs on it.
        public event Action<SpellHazardTestTarget308> Wiped;

        public bool Purge(long owner)
        {
            if (_purged) return false;
            _purged = true; PurgedBy = owner; Apply();
            Wiped?.Invoke(this);
            return true;
        }

        // Puts the patch back (a check's reset).
        public void Restore() { _purged = false; PurgedBy = 0; Apply(); }

        public void Configure(float radius, GameObject body) { _radius = Mathf.Max(0f, radius); _body = body; Apply(); }

        private void OnEnable() { Apply(); }

        private void Apply()
        {
            if (_body != null && _body != gameObject) _body.SetActive(!_purged);
        }

        // A TEST patch built from a primitive (no asset): a flat trigger disc of the given radius. Callers make it in Play or
        // in a preview scene only: nothing here keeps it out of a scene that gets saved.
        public static SpellHazardTestTarget308 CreateTest(Vector3 position, float radius)
        {
            var root = new GameObject("Spell308_TestHazard");
            root.transform.position = position;
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Patch";
            disc.transform.SetParent(root.transform, false);
            disc.transform.localScale = new Vector3(radius * 2f, .05f, radius * 2f);
            var body = disc.GetComponent<Collider>();
            if (body != null) body.isTrigger = true;
            var hazard = root.AddComponent<SpellHazardTestTarget308>();
            hazard.Configure(radius, disc);
            return hazard;
        }
    }
}
