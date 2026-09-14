using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.Presentation
{
    /// <summary>Rest-to-grip palm corrections authored with the hand rig. No input or bone changes.</summary>
    public sealed class PlayerHandGripCorrectives
    {
        private struct Binding { public SkinnedMeshRenderer Renderer; public int Right, Left; }
        private readonly Binding[] _bindings;

        public PlayerHandGripCorrectives(IEnumerable<Renderer> renderers)
        {
            var bindings = new List<Binding>();
            foreach (var renderer in renderers)
            {
                if (!(renderer is SkinnedMeshRenderer skin) || skin.sharedMesh == null) continue;
                int right = Find(skin.sharedMesh, "GripPalmRelax_Right");
                int left = Find(skin.sharedMesh, "GripPalmRelax_Left");
                if (right >= 0 || left >= 0) bindings.Add(new Binding { Renderer = skin, Right = right, Left = left });
            }
            _bindings = bindings.ToArray();
        }

        public void Apply(float right, float left)
        {
            right = Mathf.Clamp01(right) * 100f; left = Mathf.Clamp01(left) * 100f;
            foreach (var binding in _bindings)
            {
                if (binding.Renderer == null) continue;
                if (binding.Right >= 0) binding.Renderer.SetBlendShapeWeight(binding.Right, right);
                if (binding.Left >= 0) binding.Renderer.SetBlendShapeWeight(binding.Left, left);
            }
        }

        private static int Find(Mesh mesh, string name)
        {
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string imported = mesh.GetBlendShapeName(i);
                if (imported == name || imported.EndsWith("." + name, StringComparison.Ordinal)) return i;
            }
            return -1;
        }
    }
}
