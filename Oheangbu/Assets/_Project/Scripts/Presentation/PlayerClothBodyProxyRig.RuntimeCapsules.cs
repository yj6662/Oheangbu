using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.Presentation
{
    public sealed partial class PlayerClothBodyProxyRig
    {
        private struct RuntimeBinding
        {
            public Transform Bone;
            public CapsuleCollider Capsule;
            public Vector3 Center;
            public Quaternion Rotation;
        }
        private bool _runtimeBoneAttached;
        private RuntimeBinding[] _runtimeBindings;
        public bool UsesBoneAttachedRuntimeCapsules => _runtimeBoneAttached;
        public void SetBoneAttachedRuntimeCapsules(bool enabled)
        {
            if (_runtimeBoneAttached == enabled) return;
            _runtimeBoneAttached = enabled; _runtimeBindings = null; _hasPose = false;
        }

        private void BuildRuntimeBoneBindings()
        {
            var bindings = new List<RuntimeBinding>();
            foreach (var source in _sources)
                foreach (var region in source.Binding.regions)
                {
                    var influence = new float[source.Bones.Length];
                    foreach (var corner in region.corners)
                    {
                        Accumulate(source.Weights[corner.a], corner.bary.x, influence);
                        Accumulate(source.Weights[corner.b], corner.bary.y, influence);
                        Accumulate(source.Weights[corner.c], corner.bary.z, influence);
                    }
                    int best = 0;
                    for (int i = 1; i < influence.Length; i++) if (influence[i] > influence[best]) best = i;
                    Transform bone = source.Bones[best], target = region.capsule.transform;
                    bindings.Add(new RuntimeBinding { Bone = bone, Capsule = region.capsule,
                        Center = bone.InverseTransformPoint(target.position), Rotation = Quaternion.Inverse(bone.rotation) * target.rotation });
                }
            _runtimeBindings = bindings.ToArray();
        }

        private static void Accumulate(BoneWeight weights, float factor, float[] result)
        {
            result[weights.boneIndex0] += weights.weight0 * factor;
            result[weights.boneIndex1] += weights.weight1 * factor;
            result[weights.boneIndex2] += weights.weight2 * factor;
            result[weights.boneIndex3] += weights.weight3 * factor;
        }

        private void FollowRuntimeBones()
        {
            foreach (var binding in _runtimeBindings)
                binding.Capsule.transform.SetPositionAndRotation(binding.Bone.TransformPoint(binding.Center), binding.Bone.rotation * binding.Rotation);
        }
    }
}
