using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.World
{
    /// <summary>
    /// Keeps the validated C02 fifth skin influences active while a bound player is present.
    /// This is a runtime lease; it never changes a project quality asset. Other skinned meshes
    /// retain their imported influence counts and explicit renderer-quality limits.
    /// </summary>
    [DefaultExecutionOrder(1100)]
    [DisallowMultipleComponent]
    public sealed class WorldMacroPlayerSkinningLease : MonoBehaviour
    {
        private static readonly HashSet<WorldMacroPlayerSkinningLease> Owners = new HashSet<WorldMacroPlayerSkinningLease>();
        private static SkinWeights _restoreWeights;
        private static bool _hasRestoreValue;
        private bool _bound, _hasLease;

        public bool HasLease => _hasLease;
        public static int ActiveLeaseCount => Owners.Count;
        public static bool EffectiveUnlimited => Owners.Count > 0 && QualitySettings.skinWeights == SkinWeights.Unlimited;

        public void SetBound(bool bound)
        {
            _bound = bound;
            if (bound && isActiveAndEnabled && Application.isPlaying) Acquire();
            else Release();
        }

        private void OnEnable()
        {
            if (_bound && Application.isPlaying) Acquire();
        }

        private void LateUpdate()
        {
            if (!_hasLease && _bound && Application.isPlaying) Acquire();
            if (_hasLease) EnsureUnlimited();
        }

        private void Acquire()
        {
            if (_hasLease) { EnsureUnlimited(); return; }
            if (Owners.Count == 0)
            {
                _restoreWeights = QualitySettings.skinWeights;
                _hasRestoreValue = true;
            }
            Owners.Add(this);
            _hasLease = true;
            EnsureUnlimited();
        }

        private static void EnsureUnlimited()
        {
            if (QualitySettings.skinWeights == SkinWeights.Unlimited) return;
            // A quality-preset change while playing is an external request. Honor that
            // requested value when the final lease ends, rather than restoring a stale preset.
            _restoreWeights = QualitySettings.skinWeights;
            _hasRestoreValue = true;
            QualitySettings.skinWeights = SkinWeights.Unlimited;
        }

        private void Release()
        {
            if (!_hasLease) return;
            Owners.Remove(this);
            _hasLease = false;
            if (Owners.Count != 0 || !_hasRestoreValue) return;
            // A caller may switch presets and disable the player in the same frame.
            if (QualitySettings.skinWeights == SkinWeights.Unlimited)
                QualitySettings.skinWeights = _restoreWeights;
            _hasRestoreValue = false;
        }

        private void OnDisable() { Release(); }
        private void OnDestroy() { Release(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPlaySession()
        {
            // Also handles entering play with domain reload disabled.
            if (_hasRestoreValue && QualitySettings.skinWeights == SkinWeights.Unlimited)
                QualitySettings.skinWeights = _restoreWeights;
            foreach (var owner in Owners) if (owner != null) owner._hasLease = false;
            Owners.Clear();
            _hasRestoreValue = false;
        }
    }
}
