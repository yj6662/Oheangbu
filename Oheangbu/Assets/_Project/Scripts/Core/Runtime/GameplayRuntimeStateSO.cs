using System;
using UnityEngine;

namespace Oheangbu.Core
{
    /// <summary>
    /// Cross-assembly runtime hints shared by input consumers. The asset carries no saved
    /// progression; the App layer owns mutations and persistence.
    /// </summary>
    [CreateAssetMenu(menuName = "Oheangbu/Runtime/Gameplay UI State")]
    public sealed class GameplayRuntimeStateSO : ScriptableObject
    {
        [NonSerialized] private bool _inputBlocked;
        [NonSerialized] private float _lookSensitivity = 1f;
        [NonSerialized] private bool _invertLookY;
        [NonSerialized] private bool _reducedMotion;

        public bool InputBlocked => _inputBlocked;
        public float LookSensitivity => Mathf.Clamp(_lookSensitivity, .1f, 4f);
        public bool InvertLookY => _invertLookY;
        public bool ReducedMotion => _reducedMotion;

        public event Action Changed;

        private void OnEnable()
        {
            _inputBlocked = false;
            _lookSensitivity = 1f;
            _invertLookY = false;
            _reducedMotion = false;
        }

        public void SetInputBlocked(bool blocked)
        {
            if (_inputBlocked == blocked) return;
            _inputBlocked = blocked;
            Changed?.Invoke();
        }

        public void SetPreferences(float lookSensitivity, bool invertLookY, bool reducedMotion)
        {
            float sensitivity = Mathf.Clamp(lookSensitivity, .1f, 4f);
            if (Mathf.Approximately(_lookSensitivity, sensitivity)
                && _invertLookY == invertLookY && _reducedMotion == reducedMotion) return;
            _lookSensitivity = sensitivity;
            _invertLookY = invertLookY;
            _reducedMotion = reducedMotion;
            Changed?.Invoke();
        }
    }
}
