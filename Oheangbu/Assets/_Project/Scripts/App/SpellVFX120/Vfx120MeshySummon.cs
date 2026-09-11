using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    /// <summary>
    /// One static, authored Meshy model. Only material alpha changes over its life.
    /// No generated walk, bone motion, stretch, physics, damage or gameplay clocks.
    /// The owner supplies the fixed host transform and owns the host's final destruction.
    /// </summary>
    public sealed class Vfx120MeshySummon : MonoBehaviour
    {
        private sealed class Slot
        {
            public MeshRenderer Renderer;
            public int Index;
            public Color Original;
            public MaterialPropertyBlock Block;
        }
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Surface = Shader.PropertyToID("_Surface");
        private GameObject _instance;
        private Slot[] _slots;
        private MeshRenderer[] _renderers;
        private bool[] _rendererEnabled;
        private bool _configured;
        private float _startedAt;

        public bool PreviewControlled { get; private set; }
        public bool IsComplete { get; private set; }
        public float Age { get; private set; }
        public float Life { get; private set; }
        public float Alpha { get; private set; }
        public string Diagnostic { get; private set; } = "UNASSIGNED";
        public string AnimationStatus => SourceAnimatorCount == 0 ? "STATIC_MODEL_NO_RIG_OR_ANIMATION" : "STATIC_ADAPTER_ANIMATOR_DISABLED";
        public int SourceAnimatorCount { get; private set; }
        public int RendererCount => _renderers == null ? 0 : _renderers.Length;
        public int MaterialCloneCount => 0;
        public long TriangleCount { get; private set; }
        public int InstanceCount => _instance == null ? 0 : 1;
        public Transform ContentTransform => _instance == null ? null : _instance.transform;

        public bool Configure(GameObject source, float life, bool previewControlled)
        {
            Clear(); TriangleCount = 0; SourceAnimatorCount = 0;
            if (source == null || !Finite(life) || life <= 0) return Reject("SOURCE_OR_LIFE_INVALID");
            if (source.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
                return Reject("SOURCE_SCRIPT_UNSUPPORTED");
            if (source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0
                || source.GetComponentsInChildren<ParticleSystem>(true).Length != 0)
                return Reject("STATIC_MESH_SOURCE_REQUIRED");
            var sourceRenderers = source.GetComponentsInChildren<MeshRenderer>(true);
            if (sourceRenderers.Length == 0) return Reject("SOURCE_HAS_NO_MESH_RENDERER");
            foreach (var t in source.GetComponentsInChildren<Transform>(true))
                if (!Finite(t.localPosition) || !Finite(t.localScale) || !Finite(t.localRotation))
                    return Reject("SOURCE_TRANSFORM_NONFINITE");
            foreach (var renderer in sourceRenderers)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || filter.sharedMesh.vertexCount == 0)
                    return Reject("SOURCE_MESH_MISSING");
                var mesh = filter.sharedMesh;
                if (!Finite(mesh.bounds.center) || !Finite(mesh.bounds.size)) return Reject("SOURCE_BOUNDS_NONFINITE");
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) return Reject("NON_TRIANGLE_SOURCE_UNSUPPORTED");
                    TriangleCount += mesh.GetIndexCount(sub) / 3;
                }
                var materials = renderer.sharedMaterials;
                if (materials.Length == 0) return Reject("SOURCE_MATERIAL_MISSING");
                foreach (var material in materials)
                    if (material == null || material.shader == null || !material.shader.isSupported
                        || !material.HasProperty(BaseColor) || !material.HasProperty(Surface)
                        || material.GetFloat(Surface) < .5f || !Finite(material.GetColor(BaseColor)))
                        return Reject("TRANSPARENT_BASE_COLOR_MATERIAL_REQUIRED");
            }
            Life = life; Age = Alpha = 0; IsComplete = false; PreviewControlled = previewControlled;
            _startedAt = Time.time;
            var staging = new GameObject("MeshyInactiveStaging");
            staging.SetActive(false); staging.transform.SetParent(transform, false);
            try
            {
                _instance = Instantiate(source, staging.transform, false);
                _instance.name = "Meshy_" + source.name;
                _instance.SetActive(false); _instance.transform.SetParent(transform, false);
                // Preserve every source transform and all shared mesh/material assets.
                foreach (var collider in _instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (var light in _instance.GetComponentsInChildren<Light>(true)) light.enabled = false;
                foreach (var audio in _instance.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
                foreach (var body in _instance.GetComponentsInChildren<Rigidbody>(true))
                { body.isKinematic = true; body.detectCollisions = false; }
                foreach (var animator in _instance.GetComponentsInChildren<Animator>(true))
                {
                    // A later optional controller cannot silently turn this static model
                    // into root motion or callbacks. Authored animation needs separate review.
                    SourceAnimatorCount++; animator.fireEvents = false;
                    animator.applyRootMotion = false; animator.enabled = false;
                }
                foreach (var animation in _instance.GetComponentsInChildren<Animation>(true)) animation.enabled = false;
                _renderers = _instance.GetComponentsInChildren<MeshRenderer>(true);
                _rendererEnabled = new bool[_renderers.Length];
                for (int i = 0; i < _renderers.Length; i++) _rendererEnabled[i] = _renderers[i].enabled;
                var slots = new List<Slot>();
                foreach (var renderer in _renderers)
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, i);
                        slots.Add(new Slot { Renderer = renderer, Index = i, Block = block,
                            Original = materials[i].GetColor(BaseColor) });
                    }
                }
                _slots = slots.ToArray(); _configured = true;
                _instance.SetActive(true); Apply(0);
                Diagnostic = "CONFIGURED_STATIC_MODEL_RUNTIME_ART_UNVERIFIED";
                return true;
            }
            catch (Exception error)
            { Clear(); return Reject("CONFIGURE_FAILED: " + error.GetType().Name + ": " + error.Message); }
            finally { Dispose(staging); }
        }

        // Pure lifetime envelope. Full geometric size is retained even while alpha is zero.
        public static float Opacity(float age, float life)
        {
            if (!Finite(age) || !Finite(life) || life <= 0 || age <= 0 || age >= life) return 0;
            float appear = Math.Min(.3f, life * .25f), decay = Math.Min(.7f, life * .35f);
            float enter = Smooth(age / appear), leave = Smooth((life - age) / decay);
            return enter * leave;
        }
        private static float Smooth(float t)
        { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }

        public void Sample(float age)
        {
            if (!_configured || !PreviewControlled || !Finite(age) || _instance == null) return;
            Apply(age);
        }
        private void Update()
        {
            if (!_configured || PreviewControlled || IsComplete) return;
            Apply(Mathf.Max(0, Time.time - _startedAt));
            if (IsComplete) { DestroyInstance(); Diagnostic = "RUNTIME_ENDED"; }
        }
        private void Apply(float age)
        {
            Age = Mathf.Max(0, age); Alpha = Opacity(age, Life); IsComplete = age >= Life;
            foreach (var slot in _slots)
            {
                var color = slot.Original; color.a *= Alpha;
                slot.Block.SetColor(BaseColor, color);
                slot.Renderer.SetPropertyBlock(slot.Block, slot.Index);
            }
            // Avoid depth/shadow remnants at zero opacity; no model transform is changed.
            for (int i = 0; i < _renderers.Length; i++) _renderers[i].enabled = _rendererEnabled[i] && Alpha > 0;
        }
        public void Clear()
        {
            DestroyInstance(); _configured = false; Age = Alpha = 0; IsComplete = true;
            Diagnostic = "CLEARED";
        }
        private void DestroyInstance()
        {
            if (_instance != null) { _instance.SetActive(false); Dispose(_instance); }
            _instance = null; _slots = null; _renderers = null; _rendererEnabled = null;
        }
        private void OnDisable() { if (_configured) Clear(); }
        private void OnDestroy() { DestroyInstance(); }
        private bool Reject(string reason) { Diagnostic = reason; return false; }
        private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static bool Finite(Quaternion q) => Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w);
        private static bool Finite(Color c) => Finite(c.r) && Finite(c.g) && Finite(c.b) && Finite(c.a);
        private static void Dispose(UnityEngine.Object obj)
        { if (obj != null) { if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); } }
    }
}
