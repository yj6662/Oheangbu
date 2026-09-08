using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.C02RigFaceLab
{
    // Animator evaluates first; this component owns only explicitly listed facial keys.
    [DefaultExecutionOrder(800)]
    public sealed class LabFaceController : MonoBehaviour
    {
        public LabProfileSO profile;
        public Transform model;
        [SerializeField] float[] values = Array.Empty<float>();
        readonly List<Binding> bindings = new List<Binding>();
        public int writeCount { get; private set; }
        public int overwriteCount { get; private set; }
        struct Binding { public SkinnedMeshRenderer renderer; public int shape; public int channel; public float last; public bool written; }

        public void Rebind()
        {
            bindings.Clear();
            values = new float[profile == null ? 0 : profile.faceChannels.Length];
            if (profile == null || model == null) return;
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null) continue;
                for (int c = 0; c < profile.faceChannels.Length; ++c)
                {
                    int index = renderer.sharedMesh.GetBlendShapeIndex(profile.faceChannels[c]);
                    if (index >= 0) bindings.Add(new Binding { renderer = renderer, shape = index, channel = c });
                }
            }
        }
        void Awake() { Rebind(); }
        public int ChannelCount => values.Length;
        public int BindingCount => bindings.Count;
        public float GetValue(int channel) => values[channel];
        public void SetValue(int channel, float value) { values[channel] = Mathf.Clamp01(value); }
        public void Neutral() { Array.Clear(values, 0, values.Length); Apply(); }
        public void ApplyPreset(int index)
        {
            Neutral();
            var preset = profile.presets[index];
            for (int i = 0; i < Math.Min(values.Length, preset.values.Length); ++i) SetValue(i, preset.values[i]);
            Apply();
        }
        public void Apply()
        {
            for (int i = 0; i < bindings.Count; ++i)
            {
                var binding = bindings[i];
                float before = binding.renderer.GetBlendShapeWeight(binding.shape);
                if (binding.written && Mathf.Abs(before - binding.last) > 0.001f) overwriteCount++;
                binding.last = values[binding.channel] * 100f;
                binding.renderer.SetBlendShapeWeight(binding.shape, binding.last);
                binding.written = true;
                bindings[i] = binding;
                writeCount++;
            }
        }
        void LateUpdate() { Apply(); }
    }
}
