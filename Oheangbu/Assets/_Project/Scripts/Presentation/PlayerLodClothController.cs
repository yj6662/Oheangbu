using System;
using System.Linq;
using UnityEngine;

namespace Oheangbu.Presentation
{
    /// <summary>Owns only a character's LOD selection and LOD0 native Cloth activity; never changes the camera or game state.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(1600)]
    public sealed class PlayerLodClothController : MonoBehaviour
    {
        [SerializeField] private LODGroup _group;
        [SerializeField] private Camera _viewCamera;
        [SerializeField] private PlayerSecondaryMotionRig _secondary;
        [SerializeField] private Cloth[] _cloth = Array.Empty<Cloth>();
        [SerializeField] private bool[] _initialClothEnabled = Array.Empty<bool>();
        private float[] _thresholds = Array.Empty<float>();
        private readonly Plane[] _planes = new Plane[6];
        private int _lastLevel = -1;
        private bool _lastCloth, _suspended;
        public int SelectedLod { get; private set; } = -1;
        public float RelativeScreenHeight { get; private set; }
        public bool VisibleToReferenceCamera { get; private set; }
        public bool ClothActive { get; private set; }
        public int LodTransitions { get; private set; }
        public int ClothResets { get; private set; }
        public string LastError { get; private set; }
        public Camera ViewCamera => _viewCamera;
        public LODGroup Group => _group;

        public void Configure(LODGroup group, Camera viewCamera, PlayerSecondaryMotionRig secondary, Cloth[] lodZeroCloth)
        {
            if (group == null || group.transform != transform || viewCamera == null || secondary == null || lodZeroCloth == null || lodZeroCloth.Length == 0)
                throw new ArgumentException("A fresh owned LODGroup, explicit camera, world secondary rig and LOD0 Cloth are required.");
            _group = group; _viewCamera = viewCamera; _secondary = secondary; _cloth = lodZeroCloth;
            _initialClothEnabled = _cloth.Select(c => c != null && c.enabled).ToArray(); Cache();
        }
        public void SetViewCamera(Camera camera)
        { _viewCamera = camera; _lastLevel = -1; if (Application.isPlaying && !_suspended) RefreshNow(); }
        private void Cache()
        {
            if (_group == null) { LastError = "Missing owned LODGroup."; return; }
            _thresholds = _group.GetLODs().Select(l => l.screenRelativeTransitionHeight).ToArray();
            if (!ValidThresholds(_thresholds))
                LastError = "Expected three descending LOD thresholds ending at zero; camera frustum handles culling.";
            else LastError = null;
        }
        private void OnEnable() { _suspended = false; Cache(); _lastLevel = -1; }
        private void LateUpdate() { if (!_suspended) RefreshNow(); }
        private void OnDisable()
        {
            if (_group != null) _group.ForceLOD(-1);
            for (int i = 0; i < _cloth.Length; i++) if (_cloth[i] != null)
            { _cloth[i].ClearTransformMotion(); _cloth[i].enabled = i < _initialClothEnabled.Length && _initialClothEnabled[i]; }
            _lastLevel = -1;
        }

        /// <summary>Only for a disposable static physics fixture, which must test LOD0 independently of its scene camera.</summary>
        public void SuspendForStaticFixture()
        {
            _suspended = true; enabled = false;
            if (_group != null) _group.ForceLOD(0);
        }
        public bool RefreshNow()
        {
            if (_suspended) return false;
            if (_group == null || _viewCamera == null || _secondary == null || _cloth.Any(c => c == null))
            { LastError = "LOD/Cloth camera or serialized references are missing; inject the actual gameplay camera after instantiation."; ApplyCloth(false); return false; }
            if (_thresholds.Length != 3) Cache();
            if (!ValidThresholds(_thresholds)) { ApplyCloth(false); return false; }
            Vector3 scale = _group.transform.lossyScale;
            float size = _group.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Vector3 center = _group.transform.TransformPoint(_group.localReferencePoint);
            float distance = Vector3.Distance(_viewCamera.transform.position, center);
            RelativeScreenHeight = CalculateRelativeHeight(size, distance, _viewCamera.fieldOfView, _viewCamera.orthographic, _viewCamera.orthographicSize, QualitySettings.lodBias);
            if (float.IsNaN(RelativeScreenHeight) || float.IsInfinity(RelativeScreenHeight)) { LastError = "Camera/group LOD dimensions are invalid."; ApplyCloth(false); return false; }
            int selected = SelectLod(RelativeScreenHeight, _thresholds);
            selected = Mathf.Max(selected, Mathf.Clamp(QualitySettings.maximumLODLevel, 0, 2));
            GeometryUtility.CalculateFrustumPlanes(_viewCamera, _planes);
            VisibleToReferenceCamera = GeometryUtility.TestPlanesAABB(_planes, new Bounds(center, Vector3.one * size));
            if (_lastLevel != selected) { LodTransitions++; _lastLevel = selected; }
            SelectedLod = selected; _group.ForceLOD(selected);
            bool active = selected == 0 && VisibleToReferenceCamera && _group.enabled && _group.gameObject.activeInHierarchy
                && _secondary.isActiveAndEnabled && _secondary.IsConfigured && _secondary.Diagnostics.Active;
            ApplyCloth(active); LastError = null; return true;
        }
        private void ApplyCloth(bool active)
        {
            bool transition = _lastCloth != active;
            if (transition) { ClothResets++; _lastCloth = active; }
            foreach (var cloth in _cloth) if (cloth != null && (transition || cloth.enabled != active))
            {
                // Request native reinitialization and clear transform history on a visibility/LOD
                // transition. Return-to-skin alignment is measured by the actual Play diagnostic.
                cloth.enabled = false; cloth.ClearTransformMotion(); if (active) cloth.enabled = true;
            }
            ClothActive = active;
        }
        public static int SelectLod(float relativeHeight, float[] thresholds)
        {
            if (!ValidThresholds(thresholds) || relativeHeight < 0f || float.IsNaN(relativeHeight) || float.IsInfinity(relativeHeight)) throw new ArgumentException("Finite nonnegative screen height and valid three LOD thresholds required.");
            for (int i = 0; i < thresholds.Length - 1; i++) if (relativeHeight >= thresholds[i]) return i; return thresholds.Length - 1;
        }
        public static float CalculateRelativeHeight(float size, float distance, float fieldOfView, bool orthographic, float orthographicSize, float lodBias)
        {
            if (!(size > 0f) || !(lodBias > 0f) || float.IsInfinity(size) || float.IsInfinity(lodBias)) return float.NaN;
            if (orthographic) return orthographicSize > 0f && !float.IsInfinity(orthographicSize) ? size * .5f / orthographicSize * lodBias : float.NaN;
            if (float.IsNaN(distance) || float.IsInfinity(distance) || fieldOfView <= 0f || fieldOfView >= 180f) return float.NaN;
            return size * .5f / (Mathf.Max(distance, .0001f) * (float)Math.Tan(fieldOfView * Math.PI / 360d)) * lodBias;
        }
        public static bool ValidThresholds(float[] thresholds)
        {
            return thresholds != null && thresholds.Length == 3 && thresholds.All(v => !float.IsNaN(v) && !float.IsInfinity(v))
                && thresholds[0] <= 1f && thresholds[0] > thresholds[1] && thresholds[1] > thresholds[2] && thresholds[2] == 0f;
        }
    }
}
