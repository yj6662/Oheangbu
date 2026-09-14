using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.Presentation
{
    /// <summary>Render-only derivative. Original meshes/pivots remain available to authored collision and attachment bindings.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerModelRenderBatch : MonoBehaviour
    {
        [SerializeField] private Renderer[] _original = Array.Empty<Renderer>();
        [SerializeField] private Renderer[] _sources = Array.Empty<Renderer>();
        [SerializeField] private Renderer[] _combined = Array.Empty<Renderer>();
        [SerializeField] private bool[] _sourceOffscreen = Array.Empty<bool>();
        private Renderer[] _active;
        private bool _useCombined = true;
        public bool UseCombined => _useCombined;
        public Renderer[] ActiveRenderers { get { if (_active == null) Rebuild(); return _active; } }
        public Renderer[] Sources => _sources;
        public Renderer[] Combined => _combined;

        public void Configure(Renderer[] original, Renderer[] sources, Renderer[] combined)
        {
            _original = original; _sources = sources; _combined = combined;
            _sourceOffscreen = new bool[sources.Length];
            for (int i = 0; i < sources.Length; i++)
                _sourceOffscreen[i] = sources[i] is SkinnedMeshRenderer s && s.updateWhenOffscreen;
            Rebuild();
        }
        private void OnEnable() => Rebuild();
        private void OnDisable() => Rebuild();
        public void SetCombined(bool value) { if (_useCombined == value && _active != null) return; _useCombined = value; Rebuild(); }
        private void Rebuild()
        {
            bool combine = isActiveAndEnabled && _useCombined;
            var suppressed = new HashSet<Renderer>(_sources);
            var active = new List<Renderer>(_original.Length + _combined.Length);
            foreach (var renderer in _original) if (renderer != null && (!combine || !suppressed.Contains(renderer))) active.Add(renderer);
            if (combine) foreach (var renderer in _combined) if (renderer != null) active.Add(renderer);
            for (int i = 0; i < _sources.Length; i++) if (_sources[i] != null)
            {
                _sources[i].enabled = !combine;
                _sources[i].forceRenderingOff = combine;
                if (_sources[i] is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = !combine && i < _sourceOffscreen.Length && _sourceOffscreen[i];
            }
            foreach (var renderer in _combined) if (renderer != null) { renderer.enabled = combine; renderer.forceRenderingOff = !combine; }
            _active = active.ToArray();
        }
    }
}
