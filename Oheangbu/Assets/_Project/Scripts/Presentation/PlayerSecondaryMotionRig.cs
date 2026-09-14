using System;
using System.Collections.Generic;
using Oheangbu.Data;
using UnityEngine;

namespace Oheangbu.Presentation
{
    [Serializable]
    public sealed class RigidOrnamentBinding
    {
        public string Name;
        public Transform Pivot;
        public Vector3 LocalCenterOfMass = new Vector3(0f, -.08f, 0f);
        public bool OverrideSettings;
        public SecondarySpringSettings Settings = new SecondarySpringSettings();
    }

    [Serializable]
    public sealed class SecondaryBoneChainBinding
    {
        public string Name;
        public Transform[] Bones = Array.Empty<Transform>();
        public Vector3 LastBoneTipLocal = new Vector3(0f, -.06f, 0f);
        public bool OverrideSettings;
        public SecondarySpringSettings Settings = new SecondarySpringSettings();
    }

    [Serializable]
    public sealed class PlayerClothBinding : ISerializationCallbackReceiver
    {
        [Serializable] private struct SavedCoefficient { public float MaxDistance, CollisionSphereDistance; }
        [Serializable] private struct SavedSpherePair { public SphereCollider First, Second; }
        public string Name;
        public Cloth Cloth;
        // Authored in Cloth.coefficients particle order, including fixed seam vertices (maxDistance = 0).
        // Unity native Cloth structs are not serializable fields of a managed MonoBehaviour.
        [NonSerialized] public ClothSkinningCoefficient[] Coefficients = Array.Empty<ClothSkinningCoefficient>();
        public CapsuleCollider[] Capsules = Array.Empty<CapsuleCollider>();
        [NonSerialized] public ClothSphereColliderPair[] Spheres = Array.Empty<ClothSphereColliderPair>();
        [SerializeField] private SavedCoefficient[] _savedCoefficients = Array.Empty<SavedCoefficient>();
        [SerializeField] private SavedSpherePair[] _savedSpheres = Array.Empty<SavedSpherePair>();

        public void OnBeforeSerialize()
        {
            _savedCoefficients = new SavedCoefficient[Coefficients?.Length ?? 0];
            for (int i = 0; i < _savedCoefficients.Length; i++)
                _savedCoefficients[i] = new SavedCoefficient { MaxDistance = Coefficients[i].maxDistance, CollisionSphereDistance = Coefficients[i].collisionSphereDistance };
            _savedSpheres = new SavedSpherePair[Spheres?.Length ?? 0];
            for (int i = 0; i < _savedSpheres.Length; i++) _savedSpheres[i] = new SavedSpherePair { First = Spheres[i].first, Second = Spheres[i].second };
        }
        public void OnAfterDeserialize()
        {
            Coefficients = new ClothSkinningCoefficient[_savedCoefficients?.Length ?? 0];
            for (int i = 0; i < Coefficients.Length; i++)
                Coefficients[i] = new ClothSkinningCoefficient { maxDistance = _savedCoefficients[i].MaxDistance, collisionSphereDistance = _savedCoefficients[i].CollisionSphereDistance };
            Spheres = new ClothSphereColliderPair[_savedSpheres?.Length ?? 0];
            for (int i = 0; i < Spheres.Length; i++) Spheres[i] = new ClothSphereColliderPair(_savedSpheres[i].First, _savedSpheres[i].Second);
        }
    }

    public enum SecondaryMotionResetReason { Configured, Manual, Enabled, RepresentationChanged, Teleport, ReferenceChanged }

    public struct PlayerSecondaryMotionDiagnostics
    {
        public int DrivenTransforms, ClothSurfaces, LastSubsteps, Resets;
        public float LargestSwingDegrees, DroppedSimulationSeconds;
        public bool CameraRelative, Active;
        public SecondaryMotionResetReason LastReset;
    }

    /// <summary>
    /// Additive visual-only secondary rig. Call Evaluate once after final body/hand IK.
    /// Driven transforms must be dedicated, unanimated ornament/cloth bones, never Humanoid or brush bones.
    /// No Rigidbody, Collider, animation clip, input, camera, or gameplay root is created or modified.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSecondaryMotionRig : MonoBehaviour
    {
        [SerializeField] private PlayerSecondaryMotionProfileSO _profile;
        [SerializeField] private Transform _motionRoot;
        [SerializeField] private Camera _cameraReference;
        [SerializeField] private RigidOrnamentBinding[] _ornaments = Array.Empty<RigidOrnamentBinding>();
        [SerializeField] private SecondaryBoneChainBinding[] _chains = Array.Empty<SecondaryBoneChainBinding>();
        [SerializeField] private PlayerClothBinding[] _clothBindings = Array.Empty<PlayerClothBinding>();
        [SerializeField] private PlayerSecondaryCollisionRig _secondaryCollision;
        [SerializeField] private PlayerSkinnedSurfaceAttachmentRig _surfaceAttachments;

        private sealed class Node
        {
            public Transform Bone;
            public Quaternion RestRotation;
            public Vector3 Lever;
            public SecondarySpringSettings Settings;
            public Vector3 Angle, Velocity, LastPosition, LastLinearVelocity, LastAngularVelocity;
            public Vector3 Acceleration, AngularAcceleration;
            public Quaternion LastRotation;
            public bool History;
            public int Depth;
        }

        private sealed class ClothState
        {
            public PlayerClothBinding Binding;
            public ClothSkinningCoefficient[] OriginalCoefficients;
            public CapsuleCollider[] OriginalCapsules;
            public ClothSphereColliderPair[] OriginalSpheres;
            public bool OriginalEnabled, Gravity, Tethers, Continuous;
            public float Solver, Bending, Stretching, Damping, Friction, Velocity, Acceleration;
            public Vector3 ExternalAcceleration;
        }

        private Node[] _nodes = Array.Empty<Node>();
        private ClothState[] _cloth = Array.Empty<ClothState>();
        private bool _bound, _representationActive = true, _rootHistory;
        private Vector3 _lastRootPosition;
        private Quaternion _lastRootRotation;
        private float _accumulator;
        private PlayerSecondaryMotionDiagnostics _diagnostics;
        public PlayerSecondaryMotionDiagnostics Diagnostics => _diagnostics;
        public string LastBindingError { get; private set; }
        public bool IsConfigured => _bound;
        public bool TryBindSerialized() => _bound || Bind();
        public PlayerSecondaryCollisionRig SecondaryCollision => _secondaryCollision;
        public PlayerSkinnedSurfaceAttachmentRig SurfaceAttachments => _surfaceAttachments;

        public bool ConfigureSurfaceAttachments(PlayerSkinnedSurfaceAttachmentRig attachments)
        {
            if (attachments != null && (!attachments.Owns(_motionRoot) || !attachments.TryBindSerialized()))
                return Fail("Surface attachments must be authored for this exact motion root: " + attachments.LastError);
            _surfaceAttachments = attachments;
            ResetMotion(SecondaryMotionResetReason.Configured);
            return true;
        }

        public bool ConfigureCollision(PlayerSecondaryCollisionRig collision)
        {
            if (collision != null && (!collision.IsConfigured || !collision.Owns(_motionRoot)))
                return Fail("Secondary collision must be authored for this exact motion root.");
            if (collision != null) foreach (var node in _nodes)
                if (!collision.HasNode(node.Bone)) return Fail("Secondary collision has no complete source geometry for " + node.Bone.name);
            _secondaryCollision = collision; _secondaryCollision?.ResetHistory(); return true;
        }

        public bool Configure(PlayerSecondaryMotionProfileSO profile, Transform motionRoot,
            RigidOrnamentBinding[] ornaments, SecondaryBoneChainBinding[] chains,
            PlayerClothBinding[] clothBindings, Camera cameraReference = null)
        {
            ReleaseBindings();
            _profile = profile; _motionRoot = motionRoot; _cameraReference = cameraReference;
            _ornaments = ornaments ?? Array.Empty<RigidOrnamentBinding>();
            _chains = chains ?? Array.Empty<SecondaryBoneChainBinding>();
            _clothBindings = clothBindings ?? Array.Empty<PlayerClothBinding>();
            return Bind();
        }

        private void OnEnable()
        {
            if (!_bound && _profile != null) Bind();
            if (!_bound) return;
            ApplyClothSettings();
            ResetMotion(SecondaryMotionResetReason.Enabled);
        }
        private void Start() { if (!_bound && _profile != null) TryBindSerialized(); }

        private void OnDisable()
        {
            _secondaryCollision?.ResetHistory();
            RestoreNodeRotations();
            foreach (var state in _cloth) if (state.Binding.Cloth != null)
            { state.Binding.Cloth.ClearTransformMotion(); state.Binding.Cloth.enabled = false; }
            _rootHistory = false; _accumulator = 0f;
            _diagnostics.Active = false; _diagnostics.LastSubsteps = 0;
        }

        private void OnDestroy() { ReleaseBindings(); }

        public void SetRepresentationActive(bool active)
        {
            if (_representationActive == active) return;
            _representationActive = active;
            ResetMotion(SecondaryMotionResetReason.RepresentationChanged);
            _diagnostics.Active = active && _bound && isActiveAndEnabled;
            foreach (var state in _cloth) if (state.Binding.Cloth != null)
                state.Binding.Cloth.enabled = active && isActiveAndEnabled;
        }

        public void SetCameraReference(Camera camera)
        {
            if (_cameraReference == camera) return;
            _cameraReference = camera;
            if (_bound) ApplyClothSettings();
            ResetMotion(SecondaryMotionResetReason.ReferenceChanged);
        }

        public void ResetMotion(SecondaryMotionResetReason reason = SecondaryMotionResetReason.Manual)
        {
            _secondaryCollision?.ResetHistory();
            foreach (var node in _nodes)
            {
                node.Angle = node.Velocity = node.Acceleration = node.AngularAcceleration = Vector3.zero;
                node.LastLinearVelocity = node.LastAngularVelocity = Vector3.zero;
                node.History = false;
            }
            RestoreNodeRotations();
            foreach (var state in _cloth)
            {
                var cloth = state.Binding.Cloth;
                if (cloth == null) continue;
                // ClearTransformMotion only clears transform-induced history. Re-enter
                // native simulation to discard free-particle momentum/shape after a
                // teleport or representation reset, while preserving inactive LODs.
                bool wasEnabled = cloth.enabled;
                cloth.enabled = false;
                cloth.ClearTransformMotion();
                if (wasEnabled) cloth.enabled = true;
            }
            _rootHistory = false; _accumulator = 0f;
            _diagnostics.Resets++; _diagnostics.LastReset = reason;
            _diagnostics.LastSubsteps = 0; _diagnostics.LargestSwingDegrees = 0f;
        }

        public void Evaluate(float scaledDeltaTime, Vector3 windWorld = default)
        {
            if (!_bound || !isActiveAndEnabled || !_representationActive || _motionRoot == null) return;
            if (!PlayerSecondaryMotionMath.Finite(scaledDeltaTime) || scaledDeltaTime < 0f || !PlayerSecondaryMotionMath.Finite(windWorld)) return;
            // Final body pose is already available. Move authored belt anchors before
            // reading their acceleration or solving their independent child springs.
            if (_surfaceAttachments != null && !_surfaceAttachments.RefreshNow())
            {
                LastBindingError = "Surface attachment refresh failed: " + _surfaceAttachments.LastError;
                RestoreNodeRotations();
                foreach (var node in _nodes)
                {
                    node.Angle = node.Velocity = node.Acceleration = node.AngularAcceleration = Vector3.zero;
                    node.LastLinearVelocity = node.LastAngularVelocity = Vector3.zero; node.History = false;
                }
                _secondaryCollision?.ResetHistory(); _rootHistory = false; _accumulator = 0f;
                _diagnostics.Active = false; _diagnostics.LastSubsteps = 0;
                return;
            }
            Vector3 rootPosition = InReference(_motionRoot.position);
            Quaternion rootRotation = InReference(_motionRoot.rotation);
            if (_rootHistory && (Vector3.Distance(rootPosition, _lastRootPosition) > Mathf.Max(.01f, _profile.TeleportDistance)
                || Quaternion.Angle(rootRotation, _lastRootRotation) > Mathf.Clamp(_profile.TeleportAngleDegrees, 15f, 180f)))
                ResetMotion(SecondaryMotionResetReason.Teleport);
            _lastRootPosition = rootPosition; _lastRootRotation = rootRotation; _rootHistory = true;

            // Dedicated secondary bones have a fixed authored local pose; restore it before measuring animation-driven anchors.
            RestoreNodeRotations();
            foreach (var node in _nodes) SampleAnchor(node, scaledDeltaTime);
            // Optional authored V2 body contact. Explicit refresh is independent
            // of native Cloth/LOD state; no profile binding preserves old behavior.
            _secondaryCollision?.SetRuntimeCapsules(_profile.UseCapsuleOrnamentCollision, _profile.CapsuleCollisionIterations);
            bool collide = _secondaryCollision != null && _secondaryCollision.BeginFrame();
            Vector3 gravity = _cameraReference != null ? _profile.NearGravityInCameraSpace : Physics.gravity;
            Vector3 wind = InReferenceDirection(windWorld);
            float elapsed = Mathf.Min(scaledDeltaTime, Mathf.Clamp(_profile.MaximumCatchUpSeconds, .02f, .25f));
            _diagnostics.DroppedSimulationSeconds += Mathf.Max(0f, scaledDeltaTime - elapsed);
            _accumulator += elapsed;
            float h = 1f / Mathf.Clamp(_profile.SimulationHz, 60, 240);
            int steps = 0, limit = Mathf.Clamp(_profile.MaximumSubsteps, 1, 32);
            while (_accumulator + .0000001f >= h && steps < limit)
            {
                foreach (var node in _nodes)
                {
                    if (node.Bone == null) continue;
                    Quaternion basis = InReference(node.Bone.parent != null
                        ? node.Bone.parent.rotation * node.RestRotation : node.RestRotation);
                    Quaternion inverse = Quaternion.Inverse(basis);
                    var settings = node.Settings;
                    Vector3 acceleration = gravity * Mathf.Clamp(settings.GravityScale, 0f, 2f)
                        + wind * Mathf.Clamp(settings.WindScale, 0f, 2f)
                        - node.Acceleration * Mathf.Clamp(settings.TranslationInertia, 0f, 2f);
                    Vector3 lever = PlayerSecondaryMotionMath.Rotation(node.Angle) * node.Lever;
                    float length = Mathf.Max(Mathf.Max(.005f, _profile.MinimumLeverLength), lever.magnitude);
                    Vector3 torque = Vector3.Cross(lever / Mathf.Max(.00001f, lever.magnitude), inverse * acceleration) / length
                        - inverse * node.AngularAcceleration * Mathf.Clamp(settings.RotationInertia, 0f, 2f);
                    PlayerSecondaryMotionMath.Step(ref node.Angle, ref node.Velocity, torque,
                        Mathf.Clamp(settings.FrequencyHz, .1f, 12f), Mathf.Clamp(settings.DampingRatio, .05f, 2f),
                        Mathf.Clamp(settings.MaximumSwingDegrees, 0f, 80f) * Mathf.Deg2Rad, h);
                    if (collide && !_profile.UseCapsuleOrnamentCollision) _secondaryCollision.Constrain(node.Bone, node.RestRotation,
                        Mathf.Clamp(settings.MaximumSwingDegrees, 0f, 80f) * Mathf.Deg2Rad, ref node.Angle, ref node.Velocity);
                    node.Bone.localRotation = node.RestRotation * PlayerSecondaryMotionMath.Rotation(node.Angle);
                }
                _accumulator = Mathf.Max(0f, _accumulator - h); steps++;
            }
            if (_accumulator >= h)
            { _diagnostics.DroppedSimulationSeconds += _accumulator; _accumulator = 0f; }
            float largest = 0f;
            // A moved body can enter a stationary ornament even when no fixed
            // spring substep elapsed (including Evaluate(0) static diagnostics).
            // Parent-before-child order keeps exact local spring state in sync.
            if (collide) foreach (var node in _nodes)
                if (node.Bone != null) _secondaryCollision.Constrain(node.Bone, node.RestRotation,
                    Mathf.Clamp(node.Settings.MaximumSwingDegrees, 0f, 80f) * Mathf.Deg2Rad, ref node.Angle, ref node.Velocity);
            foreach (var node in _nodes)
            {
                if (node.Bone != null) node.Bone.localRotation = node.RestRotation * PlayerSecondaryMotionMath.Rotation(node.Angle);
                largest = Mathf.Max(largest, node.Angle.magnitude * Mathf.Rad2Deg);
            }
            UpdateClothWind(windWorld);
            _diagnostics.DrivenTransforms = _nodes.Length; _diagnostics.ClothSurfaces = _cloth.Length;
            _diagnostics.LastSubsteps = steps; _diagnostics.LargestSwingDegrees = largest;
            _diagnostics.CameraRelative = _cameraReference != null; _diagnostics.Active = true;
            if (_secondaryCollision != null) _secondaryCollision.EndFrame();
        }

        private void SampleAnchor(Node node, float dt)
        {
            if (node.Bone == null) return;
            Vector3 position = InReference(node.Bone.position);
            Quaternion rotation = InReference(node.Bone.rotation);
            if (node.History && dt > .000001f)
            {
                Vector3 velocity = (position - node.LastPosition) / dt;
                Vector3 angularVelocity = PlayerSecondaryMotionMath.RotationDelta(node.LastRotation, rotation) / dt;
                node.Acceleration = Vector3.ClampMagnitude((velocity - node.LastLinearVelocity) / dt, Mathf.Max(.1f, _profile.MaximumAnchorAcceleration));
                node.AngularAcceleration = Vector3.ClampMagnitude((angularVelocity - node.LastAngularVelocity) / dt, Mathf.Max(.1f, _profile.MaximumAngularAcceleration));
                node.LastLinearVelocity = velocity; node.LastAngularVelocity = angularVelocity;
            }
            else
            {
                node.Acceleration = node.AngularAcceleration = Vector3.zero;
                node.LastLinearVelocity = node.LastAngularVelocity = Vector3.zero;
            }
            node.LastPosition = position; node.LastRotation = rotation; node.History = true;
        }

        private Vector3 InReference(Vector3 world) => _cameraReference != null ? _cameraReference.transform.InverseTransformPoint(world) : world;
        private Quaternion InReference(Quaternion world) => _cameraReference != null ? Quaternion.Inverse(_cameraReference.transform.rotation) * world : world;
        private Vector3 InReferenceDirection(Vector3 world) => _cameraReference != null ? _cameraReference.transform.InverseTransformDirection(world) : world;

        private bool Bind()
        {
            LastBindingError = null;
            if (_profile == null || _motionRoot == null) return Fail("Profile and motion root are required.");
            if (_surfaceAttachments != null && (!_surfaceAttachments.Owns(_motionRoot) || !_surfaceAttachments.TryBindSerialized()))
                return Fail("Invalid authored surface attachments: " + _surfaceAttachments.LastError);
            if (!ValidProfile()) return Fail("Secondary motion profile contains a nonfinite setting.");
            _ornaments = _ornaments ?? Array.Empty<RigidOrnamentBinding>();
            _chains = _chains ?? Array.Empty<SecondaryBoneChainBinding>();
            _clothBindings = _clothBindings ?? Array.Empty<PlayerClothBinding>();
            var nodes = new List<Node>(); var used = new HashSet<Transform>(); var humanoid = new HashSet<Transform>();
            foreach (var animator in _motionRoot.GetComponentsInChildren<Animator>(true))
                if (animator.isHuman && animator.avatar != null && animator.avatar.isValid)
                    for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                    { var bone = animator.GetBoneTransform((HumanBodyBones)i); if (bone != null) humanoid.Add(bone); }
            bool Add(Transform bone, Vector3 lever, SecondarySpringSettings settings, string name)
            {
                if (bone == null || bone == _motionRoot || !bone.IsChildOf(_motionRoot)) return Fail(name + ": pivot must be a descendant of the motion root.");
                if (humanoid.Contains(bone)) return Fail(name + ": Humanoid bones belong to animation/IK and cannot be secondary pivots.");
                if (!used.Add(bone)) return Fail(name + ": transform is bound more than once.");
                if (!PlayerSecondaryMotionMath.Finite(lever) || lever.sqrMagnitude < .000001f) return Fail(name + ": a finite nonzero lever is required.");
                if (!ValidSpring(settings)) return Fail(name + ": finite spring settings are required.");
                Vector3 scale = bone.lossyScale;
                if (!PlayerSecondaryMotionMath.Finite(scale) || scale.x <= 0f || Mathf.Abs(scale.x - scale.y) > .0001f || Mathf.Abs(scale.x - scale.z) > .0001f)
                    return Fail(name + ": secondary pivots require positive uniform world scale; apply nonuniform scale during asset authoring.");
                int depth = 0; for (Transform t = bone.parent; t != null; t = t.parent) depth++;
                nodes.Add(new Node { Bone = bone, RestRotation = bone.localRotation, Lever = lever * scale.x, Settings = settings, Depth = depth });
                return true;
            }
            foreach (var ornament in _ornaments)
            {
                if (ornament == null) return Fail("An ornament binding is null.");
                if (!Add(ornament.Pivot, ornament.LocalCenterOfMass,
                    ornament.OverrideSettings ? ornament.Settings : _profile.Ornament, ornament.Name ?? "Ornament")) return false;
            }
            foreach (var chain in _chains)
            {
                if (chain == null || chain.Bones == null || chain.Bones.Length == 0) return Fail("A bone chain must contain at least one dedicated bone.");
                for (int i = 0; i < chain.Bones.Length; i++)
                {
                    Transform bone = chain.Bones[i];
                    if (i + 1 < chain.Bones.Length && (chain.Bones[i + 1] == null || chain.Bones[i + 1].parent != bone))
                        return Fail((chain.Name ?? "Chain") + ": bones must be a direct parent-child chain.");
                    Vector3 lever = i + 1 < chain.Bones.Length ? chain.Bones[i + 1].localPosition : chain.LastBoneTipLocal;
                    if (!Add(bone, lever, chain.OverrideSettings ? chain.Settings : _profile.BoneChain, chain.Name ?? "Chain")) return false;
                }
            }
            if (nodes.Count > Mathf.Clamp(_profile.MaximumDrivenTransforms, 1, 128)) return Fail("Dedicated secondary transform budget exceeded.");
            var cloth = new List<ClothState>(); var usedCloth = new HashSet<Cloth>();
            if (_clothBindings.Length > 0 && _profile.Cloth == null) return Fail("Cloth settings are required for cloth bindings.");
            foreach (var binding in _clothBindings)
            {
                if (binding == null || binding.Cloth == null || !usedCloth.Add(binding.Cloth)) return Fail("Each cloth binding must reference one unique authored Cloth component.");
                if (!binding.Cloth.transform.IsChildOf(_motionRoot) && binding.Cloth.transform != _motionRoot) return Fail("Cloth must belong to the motion root.");
                var original = binding.Cloth.coefficients;
                if (binding.Coefficients == null || binding.Coefficients.Length == 0 || binding.Coefficients.Length != original.Length)
                    return Fail((binding.Name ?? "Cloth") + ": authored coefficients " + (binding.Coefficients?.Length ?? 0)
                        + " must match native Cloth particle count " + original.Length + " exactly.");
                bool pinned = false;
                foreach (var coefficient in binding.Coefficients)
                {
                    if (!PlayerSecondaryMotionMath.Finite(coefficient.maxDistance) || coefficient.maxDistance < 0f
                        || !PlayerSecondaryMotionMath.Finite(coefficient.collisionSphereDistance) || coefficient.collisionSphereDistance < 0f)
                        return Fail((binding.Name ?? "Cloth") + ": coefficients must be finite and nonnegative.");
                    pinned |= coefficient.maxDistance == 0f;
                }
                if (!pinned) return Fail((binding.Name ?? "Cloth") + ": wearable cloth requires authored fixed seam vertices.");
                foreach (var capsule in binding.Capsules ?? Array.Empty<CapsuleCollider>()) if (!OwnedProxy(capsule, out string error)) return Fail(error);
                foreach (var pair in binding.Spheres ?? Array.Empty<ClothSphereColliderPair>())
                {
                    if (!OwnedProxy(pair.first, out string error)) return Fail(error);
                    if (pair.second != null && !OwnedProxy(pair.second, out error)) return Fail(error);
                }
                // Empirically measured in Unity 6000.3.9f1: only 32 sphere slots
                // (16 distinct capsules) respond, although the getter retains more.
                var collisionSpheres = new HashSet<SphereCollider>();
                foreach (var pair in binding.Spheres ?? Array.Empty<ClothSphereColliderPair>())
                {
                    if (pair.first != null) collisionSpheres.Add(pair.first);
                    if (pair.second != null) collisionSpheres.Add(pair.second);
                }
                if ((binding.Capsules?.Length ?? 0) * 2 + collisionSpheres.Count > 32)
                {
                    binding.Cloth.enabled = false;
                    return Fail((binding.Name ?? "Cloth") + ": native 32-sphere / 16-capsule collision budget exceeded; use the verified regional collision binding.");
                }
                var c = binding.Cloth;
                var snapshot = new PlayerClothBinding { Name = binding.Name, Cloth = c,
                    Coefficients = (ClothSkinningCoefficient[])binding.Coefficients.Clone(),
                    Capsules = (CapsuleCollider[])(binding.Capsules ?? Array.Empty<CapsuleCollider>()).Clone(),
                    Spheres = (ClothSphereColliderPair[])(binding.Spheres ?? Array.Empty<ClothSphereColliderPair>()).Clone() };
                cloth.Add(new ClothState { Binding = snapshot, OriginalCoefficients = original, OriginalCapsules = c.capsuleColliders,
                    OriginalSpheres = c.sphereColliders, OriginalEnabled = c.enabled, Gravity = c.useGravity,
                    Tethers = c.useTethers, Continuous = c.enableContinuousCollision, Solver = c.clothSolverFrequency,
                    Bending = c.bendingStiffness, Stretching = c.stretchingStiffness, Damping = c.damping, Friction = c.friction,
                    Velocity = c.worldVelocityScale, Acceleration = c.worldAccelerationScale, ExternalAcceleration = c.externalAcceleration });
            }
            nodes.Sort((a, b) => a.Depth.CompareTo(b.Depth));
            _nodes = nodes.ToArray(); _cloth = cloth.ToArray(); _bound = true;
            _diagnostics.DrivenTransforms = _nodes.Length; _diagnostics.ClothSurfaces = _cloth.Length;
            _diagnostics.CameraRelative = _cameraReference != null; _diagnostics.Active = isActiveAndEnabled && _representationActive;
            if (isActiveAndEnabled) ApplyClothSettings();
            ResetMotion(SecondaryMotionResetReason.Configured);
            return true;
        }

        public static bool ValidateProxy(Collider collider, out string error)
        {
            // The owner creates these explicitly. No collision-layer flags or Physics settings are changed here.
            // Queries that deliberately include IgnoreRaycast AND triggers must also exclude these proxies at integration.
            bool valid = collider != null && collider.attachedRigidbody == null && collider.isTrigger && collider.gameObject.layer == 2
                && collider.excludeLayers.value == -1 && collider.includeLayers.value == 0;
            error = valid ? null : "Cloth collision proxies must be explicit IgnoreRaycast-layer triggers without a Rigidbody, with excludeLayers=Everything and includeLayers=Nothing.";
            return valid;
        }

        private bool OwnedProxy(Collider collider, out string error)
        {
            if (!ValidateProxy(collider, out error)) return false;
            if (collider.transform != _motionRoot && collider.transform.IsChildOf(_motionRoot)) return true;
            error = "Cloth collision proxies must be dedicated descendants of this visual motion root.";
            return false;
        }

        private bool ValidProfile()
        {
            bool valid = PlayerSecondaryMotionMath.Finite(_profile.MaximumCatchUpSeconds)
                && PlayerSecondaryMotionMath.Finite(_profile.TeleportDistance) && PlayerSecondaryMotionMath.Finite(_profile.TeleportAngleDegrees)
                && PlayerSecondaryMotionMath.Finite(_profile.MaximumAnchorAcceleration) && PlayerSecondaryMotionMath.Finite(_profile.MaximumAngularAcceleration)
                && PlayerSecondaryMotionMath.Finite(_profile.MinimumLeverLength) && PlayerSecondaryMotionMath.Finite(_profile.NearGravityInCameraSpace);
            var c = _profile.Cloth;
            return valid && (c == null || (PlayerSecondaryMotionMath.Finite(c.SolverFrequency) && PlayerSecondaryMotionMath.Finite(c.BendingStiffness)
                && PlayerSecondaryMotionMath.Finite(c.StretchingStiffness) && PlayerSecondaryMotionMath.Finite(c.Damping)
                && PlayerSecondaryMotionMath.Finite(c.Friction) && PlayerSecondaryMotionMath.Finite(c.WorldVelocityScale)
                && PlayerSecondaryMotionMath.Finite(c.WorldAccelerationScale) && PlayerSecondaryMotionMath.Finite(c.WindScale)));
        }

        private static bool ValidSpring(SecondarySpringSettings s) => s != null
            && PlayerSecondaryMotionMath.Finite(s.FrequencyHz) && PlayerSecondaryMotionMath.Finite(s.DampingRatio)
            && PlayerSecondaryMotionMath.Finite(s.MaximumSwingDegrees) && PlayerSecondaryMotionMath.Finite(s.GravityScale)
            && PlayerSecondaryMotionMath.Finite(s.TranslationInertia) && PlayerSecondaryMotionMath.Finite(s.RotationInertia)
            && PlayerSecondaryMotionMath.Finite(s.WindScale);

        private bool Fail(string error) { LastBindingError = error; return false; }

        private void ApplyClothSettings()
        {
            if (_profile == null || _profile.Cloth == null) return;
            var p = _profile.Cloth;
            foreach (var state in _cloth)
            {
                var c = state.Binding.Cloth; if (c == null) continue;
                c.coefficients = state.Binding.Coefficients;
                c.capsuleColliders = state.Binding.Capsules ?? Array.Empty<CapsuleCollider>();
                c.sphereColliders = state.Binding.Spheres ?? Array.Empty<ClothSphereColliderPair>();
                c.clothSolverFrequency = Mathf.Clamp(p.SolverFrequency, 30f, 480f);
                c.bendingStiffness = Mathf.Clamp01(p.BendingStiffness); c.stretchingStiffness = Mathf.Clamp01(p.StretchingStiffness);
                c.damping = Mathf.Clamp01(p.Damping); c.friction = Mathf.Clamp01(p.Friction);
                c.useGravity = p.UseGravity && _cameraReference == null; c.useTethers = p.UseTethers; c.enableContinuousCollision = p.ContinuousCollision;
                c.worldVelocityScale = _cameraReference == null ? Mathf.Clamp(p.WorldVelocityScale, 0f, 2f) : 0f;
                c.worldAccelerationScale = _cameraReference == null ? Mathf.Clamp(p.WorldAccelerationScale, 0f, 2f) : 0f;
                c.enabled = isActiveAndEnabled && _representationActive;
                c.ClearTransformMotion();
            }
            GetComponent<PlayerClothCollisionBudgetRig>()?.ReapplyAssignments();
        }

        private void UpdateClothWind(Vector3 windWorld)
        {
            if (_profile.Cloth == null) return;
            Vector3 acceleration = windWorld * Mathf.Clamp(_profile.Cloth.WindScale, 0f, 2f);
            if (_cameraReference != null && _profile.Cloth.UseGravity)
                acceleration += _cameraReference.transform.TransformDirection(_profile.NearGravityInCameraSpace);
            foreach (var state in _cloth) if (state.Binding.Cloth != null) state.Binding.Cloth.externalAcceleration = acceleration;
        }

        private void RestoreNodeRotations()
        {
            foreach (var node in _nodes) if (node.Bone != null) node.Bone.localRotation = node.RestRotation;
        }

        private void ReleaseBindings()
        {
            RestoreNodeRotations();
            foreach (var state in _cloth)
            {
                var c = state.Binding.Cloth; if (c == null) continue;
                c.coefficients = state.OriginalCoefficients; c.capsuleColliders = state.OriginalCapsules; c.sphereColliders = state.OriginalSpheres;
                c.useGravity = state.Gravity; c.useTethers = state.Tethers; c.enableContinuousCollision = state.Continuous;
                c.clothSolverFrequency = state.Solver; c.bendingStiffness = state.Bending; c.stretchingStiffness = state.Stretching;
                c.damping = state.Damping; c.friction = state.Friction; c.worldVelocityScale = state.Velocity; c.worldAccelerationScale = state.Acceleration;
                c.externalAcceleration = state.ExternalAcceleration; c.ClearTransformMotion(); c.enabled = state.OriginalEnabled;
            }
            _nodes = Array.Empty<Node>(); _cloth = Array.Empty<ClothState>(); _bound = false;
            _diagnostics.Active = false; _diagnostics.DrivenTransforms = 0; _diagnostics.ClothSurfaces = 0;
        }
    }
}
