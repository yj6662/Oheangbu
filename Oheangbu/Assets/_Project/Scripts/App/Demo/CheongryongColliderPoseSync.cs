using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    /// <summary>
    /// Flushes changed dragon damage colliders after animation (5000), follow (10000), and tail (11000).
    /// Unity exposes only a global SyncTransforms call; the dirty trigger is scoped to this actor's
    /// explicitly tracked colliders. Does not enable global automatic synchronization or simulate physics.
    /// </summary>
    [DefaultExecutionOrder(12000)]
    [DisallowMultipleComponent]
    public sealed class CheongryongColliderPoseSync : MonoBehaviour
    {
        [SerializeField] private Collider[] _damageColliders;
        private Matrix4x4[] _lastMatrices;
        private bool[] _lastActive;
        private bool _hasSnapshot;
        private static int _lastGlobalSyncFrame = int.MinValue;
        // With domain reload disabled a stale frame could coincide with a new session's frame and skip a sync.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPlaySession() => _lastGlobalSyncFrame = int.MinValue;
        public bool IsConfigured { get; private set; }
        public int TrackedColliderCount => _damageColliders != null ? _damageColliders.Length : 0;
        public int SynchronizationCount { get; private set; }
        public int LastSynchronizedFrame { get; private set; } = -1;

        private void Awake() { if (!IsConfigured) TryAutoConfigure(); }
        private void OnEnable() { _hasSnapshot = false; }
        private void OnDisable() { _hasSnapshot = false; }
        private void LateUpdate() { SynchronizeIfChanged(); }

        public bool TryAutoConfigure()
        {
            if (_damageColliders != null && _damageColliders.Length > 0) return Configure(_damageColliders);
            var owned = new List<Collider>();
            foreach (Collider collider in GetComponentsInChildren<Collider>(true))
                if (collider.name.StartsWith("Damage_", StringComparison.Ordinal)) owned.Add(collider);
            return Configure(owned.ToArray());
        }

        public bool Configure(Collider[] damageColliders)
        {
            IsConfigured = false; _hasSnapshot = false;
            if (damageColliders == null || damageColliders.Length == 0 || damageColliders.Length > 64) return false;
            var unique = new HashSet<Collider>();
            foreach (Collider collider in damageColliders)
                if (collider == null || !collider.transform.IsChildOf(transform) || !unique.Add(collider)) return false;
            _damageColliders = (Collider[])damageColliders.Clone();
            _lastMatrices = new Matrix4x4[_damageColliders.Length]; _lastActive = new bool[_damageColliders.Length];
            IsConfigured = true; return true;
        }

        public bool SynchronizeIfChanged()
        {
            if (!IsConfigured || !isActiveAndEnabled) return false;
            bool anyActive = false, changed = !_hasSnapshot;
            for (int i = 0; i < _damageColliders.Length; i++)
            {
                Collider collider = _damageColliders[i];
                bool active = collider != null && collider.enabled && collider.gameObject.activeInHierarchy;
                anyActive |= active;
                if (active && (!_lastActive[i] || MatrixChanged(collider.transform.localToWorldMatrix, _lastMatrices[i]))) changed = true;
            }
            if (!anyActive) { _hasSnapshot = false; return false; }
            if (!changed) return false;
            int frame = Time.frameCount;
            if (_lastGlobalSyncFrame == frame)
            {
                // Do not acknowledge a transform changed after another caller's flush.
                // It remains dirty for next frame rather than silently staying stale forever.
                return false;
            }
            using (Perf307Markers.ColliderSync.Auto()) { Physics.SyncTransforms(); }
            _lastGlobalSyncFrame = frame; LastSynchronizedFrame = frame; SynchronizationCount++;
            for (int i = 0; i < _damageColliders.Length; i++)
            {
                Collider collider = _damageColliders[i];
                _lastActive[i] = collider != null && collider.enabled && collider.gameObject.activeInHierarchy;
                if (_lastActive[i]) _lastMatrices[i] = collider.transform.localToWorldMatrix;
            }
            _hasSnapshot = true; return true;
        }

        private static bool MatrixChanged(Matrix4x4 current, Matrix4x4 previous)
        {
            // Ignore sub-micrometer/roundoff jitter from reapplying an identical solved pose.
            for (int i = 0; i < 16; i++) if (Mathf.Abs(current[i] - previous[i]) > .000001f) return true;
            return false;
        }
    }
}
