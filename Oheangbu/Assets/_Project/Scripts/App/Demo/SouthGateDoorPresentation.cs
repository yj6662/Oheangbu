using UnityEngine;
using UnityEngine.AI;
using Oheangbu.App.World;

namespace Oheangbu.App.Demo
{
    // Visual/physical gate only. Session owns durable victory; this class never awards or advances progress.
    public sealed class SouthGateDoorPresentation : MonoBehaviour
    {
        [SerializeField] Transform _leftLeaf, _rightLeaf;
        [SerializeField] Collider[] _closedBlockers = new Collider[0];
        [SerializeField] NavMeshObstacle[] _closedNavigation = new NavMeshObstacle[0];
        [SerializeField] Quaternion _leftClosed = Quaternion.identity, _rightClosed = Quaternion.identity;
        [SerializeField] float _duration = 2.4f, _openDegrees = 100;
        [SerializeField] WorldMacroPlaytestSession _session;
        bool _loaded;
        bool _targetOpen;
        float _progress;
        public bool IsConfigured => _leftLeaf != null && _rightLeaf != null && _closedBlockers != null && _closedBlockers.Length > 0;
        public bool OpenAnimationComplete => IsConfigured && _targetOpen && _progress >= 1f;
        public bool IsOpenRequested => _targetOpen;
        public void ConfigureNavigation(NavMeshObstacle[] obstacles)
        { _closedNavigation = obstacles ?? new NavMeshObstacle[0]; Apply(); }
        public void ConfigureSession(WorldMacroPlaytestSession session)
        { Unbind(); _session = session; _loaded = false; if (isActiveAndEnabled) Bind(); }
        void OnEnable() { Bind(); }
        void OnDisable() { Unbind(); }
        void Bind() { if (_session != null) { _session.DemoSouthGateOpened -= Victory; _session.DemoSouthGateOpened += Victory; } }
        void Unbind() { if (_session != null) _session.DemoSouthGateOpened -= Victory; }
        void Victory() { _loaded = true; SetOpened(true); }
        public void Configure(Transform leftLeaf, Transform rightLeaf, Collider[] blockers, float duration = 2.4f)
        {
            _leftLeaf = leftLeaf; _rightLeaf = rightLeaf; _closedBlockers = blockers ?? new Collider[0];
            _duration = Mathf.Max(.1f, duration);
            if (_leftLeaf != null) _leftClosed = _leftLeaf.localRotation;
            if (_rightLeaf != null) _rightClosed = _rightLeaf.localRotation;
            _targetOpen = false; _progress = 0; Apply();
        }
        public void SetOpened(bool open, bool immediate = false)
        { _targetOpen = open; if (immediate) _progress = open ? 1 : 0; Apply(); }
        void Update()
        {
            if (!IsConfigured) return;
            if (!_loaded && _session != null && _session.DemoEscortReady) { _loaded = true; SetOpened(_session.DemoSouthGateOpen, true); }
            _progress = Mathf.MoveTowards(_progress, _targetOpen ? 1 : 0, Time.deltaTime / _duration); Apply();
        }
        void Apply()
        {
            if (!IsConfigured) return;
            float value = Mathf.SmoothStep(0, 1, _progress);
            _leftLeaf.localRotation = _leftClosed * Quaternion.Euler(0, -_openDegrees * value, 0);
            _rightLeaf.localRotation = _rightClosed * Quaternion.Euler(0, _openDegrees * value, 0);
            foreach (var collider in _closedBlockers) if (collider != null) collider.enabled = !OpenAnimationComplete;
            foreach (var obstacle in _closedNavigation) if (obstacle != null) obstacle.enabled = !OpenAnimationComplete;
        }
    }
}
