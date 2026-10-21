using System;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // #308 D308-125 적용 5 (debate decision, TEST): an ember patch a ground eruption left where it burst. It burns the player
    // who stands in it (CombatLoopWiring.Ember308 reads Contains and the profile) and is the first real target of 온's cleansing
    // wave (ISpellPurgeable308). It never touches enemies. It outlives the enemy that laid it; the wiring drops it when its time
    // is up, when it is wiped, when its owner's life is reset (a rest) and when the player dies.
    //   - Found by 온 through a thin trigger box lying just under the ground (layer Ignore Raycast, so the default raycasts,
    //     the eruption's own ground search and lines of sight never see it).
    //   - No light, no emission (ART-INK: "pollution does not shine").
    [DisallowMultipleComponent]
    public sealed class EnemyGroundPatch308 : MonoBehaviour, ISpellPurgeable308
    {
        // How far above or below the patch's plane the player's feet still stand in it, in metres (contact, not balance:
        // a jump clears it, a slope's lip does not).
        public const float ContactHeight = .6f;
        const int IgnoreRaycastLayer = 2;

        EnemyGroundPatchSO _profile;
        bool _purged;

        public EnemyGroundPatchSO Profile => _profile;
        public EnemyVitals Owner { get; private set; }
        public uint OwnerLife { get; private set; }
        public float Born { get; private set; }
        public float Until { get; private set; }
        public Vector3 Normal { get; private set; } = Vector3.up;
        public long PurgedBy { get; private set; }

        public Vector3 Position => transform.position;
        public float Radius => _profile != null ? _profile.Radius : 0f;
        public bool Purged => _purged;
        public bool Live(float now) => !_purged && _profile != null && now < Until;

        // Read-only re-broadcast: 온 wiped this patch. No rule hangs on it (the wiring drops it on its next frame).
        public event Action<EnemyGroundPatch308> Wiped;

        public void Configure(EnemyGroundPatchSO profile, EnemyVitals owner, Vector3 point, Vector3 normal, float now)
        {
            _profile = profile; _purged = false; PurgedBy = 0;
            Owner = owner; OwnerLife = owner != null ? owner.LifeRevision : 0u;
            Normal = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            Born = now; Until = now + (profile != null ? profile.Life : 0f);
            transform.SetPositionAndRotation(point, Quaternion.FromToRotation(Vector3.up, Normal));
            gameObject.layer = IgnoreRaycastLayer;
            var box = GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            float r = Radius;
            box.isTrigger = true;
            box.size = new Vector3(r * 2f, .04f, r * 2f);
            box.center = new Vector3(0f, -.03f, 0f);
        }

        // Are these feet in the patch: within its radius along the ground and within ContactHeight of its plane.
        public bool Contains(Vector3 feet)
        {
            if (_profile == null) return false;
            Vector3 v = feet - transform.position;
            float along = Vector3.Dot(v, Normal);
            if (along > ContactHeight || along < -ContactHeight) return false;
            Vector3 flat = v - Normal * along;
            return flat.sqrMagnitude <= Radius * Radius;
        }

        public bool Purge(long owner)
        {
            if (_purged) return false;
            _purged = true; PurgedBy = owner;
            Wiped?.Invoke(this);
            return true;
        }
    }
}
