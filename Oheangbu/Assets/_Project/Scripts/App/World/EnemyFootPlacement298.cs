using System;
using System.Collections.Generic;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Oheangbu.App.World
{
    /// <summary>Opt-in four-leg visual correction. Restore before graph.Evaluate, then ApplyPose once.
    /// No autonomous update, root movement, navigation, collision or mesh mutation.</summary>
    [DisallowMultipleComponent]
    public sealed class EnemyFootPlacement298 : MonoBehaviour
    {
        [Serializable] public sealed class LegBinding
        {
            public string Name;
            public Transform Upper, Lower, Foot;
            public bool HasSoleOverride;
            public Vector3 SoleLocalOverride;
            public Vector3 SoleNormalLocalOverride = Vector3.up;
            [HideInInspector] public Vector3 SoleLocal, SoleUpLocal, BendActorLocal;
            [HideInInspector] public float RestSoleActorY, UpperModelLength, LowerModelLength;
            [HideInInspector] public int WeightedSurfaceVertices;
            [HideInInspector] public string CalibrationSource;
        }
        [Serializable] public sealed class FootDiagnostic
        {
            public string Name, Collider;
            public bool Planted, Supported, Corrected;
            public float AuthoredLift, GroundGap, AnkleError;
            public Vector3 AuthoredSole, TargetSole, SolvedSole;
        }
        public Transform ModelRoot, BodyRoot;
        public EnemyVitals Vitals;
        public SkinnedMeshRenderer[] Skins = Array.Empty<SkinnedMeshRenderer>();
        public LegBinding[] Legs = Array.Empty<LegBinding>();
        public LayerMask GroundMask = ~0;
        [Min(0)] public float SoleClearance = .005f;
        [Min(.01f)] public float MaximumFootShift = .14f;
        [Min(0)] public float MaximumBodyDrop = .12f;
        [Min(0)] public float SwingLift = .025f;
        [Range(0, 25)] public float MaximumGroundSlope = 25f;
        [Range(0, 12)] public float MaximumFootTilt = 12f;
        [Min(.01f)] public float ProbeUp = .18f, ProbeDown = .32f;
        [Min(.005f)] public float SupportHalfLength = .025f;
        [Min(.001f)] public float SupportHeightDifference = .03f;
        [SerializeField] bool calibrated;
        public bool Configured => calibrated;
        public string LastStatus { get; private set; } = "NOT_CONFIGURED";
        public float BodyDrop { get; private set; }
        public int RayQueriesLastSample { get; private set; }
        public int PlantedFeet { get; private set; }
        public int AppliedSamples { get; private set; }
        public FootDiagnostic[] Diagnostics { get; private set; } = Array.Empty<FootDiagnostic>();
        readonly RaycastHit[] hits = new RaycastHit[64];
        sealed class Sample
        {
            public Vector3 Hip, Knee, Ankle, Sole, Target, SolvedKnee;
            public Quaternion UpperLocal, LowerLocal, FootLocal, DesiredRotation;
            public bool Planted;
        }
        Sample[] samples;
        Vector3 bodyLocalBefore;
        bool applied;
        bool runtimeBound;
        NavMeshAgent navigation;
        const float LengthTolerance = .0015f, TargetTolerance = .003f;

        public void Configure(Transform actorRoot, Transform modelRoot, Transform bodyRoot,
            SkinnedMeshRenderer[] skins, LegBinding[] chains, LayerMask groundMask)
        {
            RestoreAuthoredPose(); calibrated = false;
            if (actorRoot != transform) throw new ArgumentException("Attach EnemyFootPlacement298 to its existing combat actor root.");
            ModelRoot = modelRoot; BodyRoot = bodyRoot; Skins = skins ?? Array.Empty<SkinnedMeshRenderer>();
            Legs = chains ?? Array.Empty<LegBinding>(); GroundMask = groundMask; Vitals = GetComponent<EnemyVitals>();
            if (!ValidBinding()) throw new ArgumentException(LastStatus);
            var unique = new HashSet<Transform>();
            foreach (var leg in Legs)
            {
                if (!unique.Add(leg.Upper) || !unique.Add(leg.Lower) || !unique.Add(leg.Foot)) throw new ArgumentException("Four distinct leg chains required.");
                leg.UpperModelLength = ModelRoot.InverseTransformVector(leg.Lower.position - leg.Upper.position).magnitude;
                leg.LowerModelLength = ModelRoot.InverseTransformVector(leg.Foot.position - leg.Lower.position).magnitude;
                if (Mathf.Min(leg.UpperModelLength, leg.LowerModelLength) < .005f) throw new ArgumentException("Degenerate leg: " + leg.Name);
                Vector3 axis = leg.Foot.position - leg.Upper.position;
                Vector3 bend = Vector3.ProjectOnPlane(leg.Lower.position - leg.Upper.position, axis);
                if (bend.sqrMagnitude < .000001f) bend = Vector3.ProjectOnPlane(transform.forward, axis);
                if (bend.sqrMagnitude < .000001f) throw new ArgumentException("A measurable knee plane is required: " + leg.Name);
                leg.BendActorLocal = transform.InverseTransformDirection(bend.normalized);
                if (leg.HasSoleOverride)
                {
                    if (!Finite(leg.SoleLocalOverride) || !Finite(leg.SoleNormalLocalOverride) || leg.SoleNormalLocalOverride.sqrMagnitude < .5f)
                        throw new ArgumentException("Explicit sole override must be finite and measured.");
                    leg.SoleLocal = leg.SoleLocalOverride; leg.SoleUpLocal = leg.SoleNormalLocalOverride.normalized;
                    leg.CalibrationSource = "EXPLICIT_REVIEWED_LOCAL_SOLE"; leg.WeightedSurfaceVertices = 0;
                }
                else CalibrateSole(leg);
                leg.RestSoleActorY = transform.InverseTransformPoint(leg.Foot.TransformPoint(leg.SoleLocal)).y;
            }
            Allocate(); calibrated = true; runtimeBound = true; navigation = GetComponent<NavMeshAgent>(); LastStatus = "CALIBRATED_FROM_INITIAL_IDLE";
        }

        bool ValidBinding()
        {
            if (ModelRoot == null || BodyRoot == null || Vitals == null || !ModelRoot.IsChildOf(transform) ||
                BodyRoot == transform || !(BodyRoot == ModelRoot || BodyRoot.IsChildOf(ModelRoot)) || Legs == null || Legs.Length != 4)
                return Fail("FOUR_LEG_VISUAL_BINDING_REQUIRED");
            if (!UniformPositive(ModelRoot.lossyScale) || !UniformPositive(transform.lossyScale)) return Fail("NONUNIFORM_OR_REFLECTED_SCALE");
            // Visual bones may never carry physical support or combat collision shapes.
            if (BodyRoot.GetComponentsInChildren<Collider>(true).Length != 0) return Fail("BODY_ROOT_CONTAINS_COLLIDERS");
            foreach (var leg in Legs)
                if (leg == null || leg.Upper == null || leg.Lower == null || leg.Foot == null ||
                    leg.Lower.parent != leg.Upper || leg.Foot.parent != leg.Lower || !leg.Upper.IsChildOf(BodyRoot) ||
                    !UniformPositive(leg.Upper.lossyScale) || !UniformPositive(leg.Lower.lossyScale) || !UniformPositive(leg.Foot.lossyScale))
                    return Fail("INVALID_TWO_BONE_CHAIN");
            return true;
        }
        void Allocate()
        {
            if (samples != null && samples.Length == Legs.Length)
            { for (int i = 0; i < Legs.Length; i++) Diagnostics[i].Name = Legs[i].Name; return; }
            samples = new Sample[Legs.Length]; Diagnostics = new FootDiagnostic[Legs.Length];
            for (int i = 0; i < Legs.Length; i++) { samples[i] = new Sample(); Diagnostics[i] = new FootDiagnostic { Name = Legs[i].Name }; }
        }
        void CalibrateSole(LegBinding leg)
        {
            var points = new List<Vector3>();
            foreach (var skin in Skins)
            {
                if (skin == null || skin.sharedMesh == null || !skin.transform.IsChildOf(ModelRoot)) continue;
                if (!skin.sharedMesh.isReadable) throw new ArgumentException("Readable candidate skin required for sole calibration.");
                var vertices = WorldVertices(skin); var bones = skin.bones;
                var counts = skin.sharedMesh.GetBonesPerVertex(); var weights = skin.sharedMesh.GetAllBoneWeights();
                try
                {
                    int cursor = 0;
                    for (int vertex = 0; vertex < counts.Length; vertex++)
                    {
                        float amount = 0;
                        for (int w = 0; w < counts[vertex]; w++)
                        {
                            var value = weights[cursor++];
                            if (bones[value.boneIndex] == leg.Foot || bones[value.boneIndex].IsChildOf(leg.Foot)) amount += value.weight;
                        }
                        if (amount >= .5f) points.Add(vertices[vertex]);
                    }
                }
                finally { counts.Dispose(); weights.Dispose(); }
            }
            if (points.Count < 3) throw new ArgumentException("Missing measured weighted foot surface: " + leg.Name);
            float bottom = float.PositiveInfinity; foreach (var point in points) { if (!Finite(point)) throw new ArgumentException("Nonfinite sole."); bottom = Mathf.Min(bottom, point.y); }
            Vector3 mean = Vector3.zero; int count = 0;
            foreach (var point in points) if (point.y <= bottom + .015f) { mean += point; count++; }
            mean /= count; mean.y = bottom;
            leg.SoleLocal = leg.Foot.InverseTransformPoint(mean); leg.SoleUpLocal = leg.Foot.InverseTransformDirection(Vector3.up);
            leg.WeightedSurfaceVertices = points.Count; leg.CalibrationSource = "ACTUAL_BONE_BINDPOSE_WEIGHTED_WORLD_SURFACE";
        }

        // Exact current linear skinning in world space. Renderer scale is never applied a second
        // time after bone.localToWorld * bindpose. Candidate rigs have no active blend shapes.
        public static Vector3[] WorldVertices(SkinnedMeshRenderer skin)
        {
            if (skin == null || skin.sharedMesh == null || !skin.sharedMesh.isReadable)
                throw new ArgumentException("Readable skinned mesh required.");
            var mesh = skin.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
                if (Mathf.Abs(skin.GetBlendShapeWeight(i)) > .0001f)
                    throw new ArgumentException("Active blend shapes require separate sole calibration support.");
            var source = mesh.vertices; var bones = skin.bones; var bindposes = mesh.bindposes;
            if (bindposes.Length != bones.Length) throw new ArgumentException("Skin bone/bindpose count mismatch.");
            var matrices = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) throw new ArgumentException("Missing skin bone.");
                matrices[i] = bones[i].localToWorldMatrix * bindposes[i];
            }
            var counts = mesh.GetBonesPerVertex(); var weights = mesh.GetAllBoneWeights();
            try
            {
                if (counts.Length != source.Length) throw new ArgumentException("Skin vertex/weight count mismatch.");
                var result = new Vector3[source.Length]; int cursor = 0;
                for (int i = 0; i < source.Length; i++)
                {
                    float total = 0;
                    for (int w = 0; w < counts[i]; w++)
                    {
                        if (cursor >= weights.Length) throw new ArgumentException("Truncated skin weights.");
                        var value = weights[cursor++];
                        if (value.boneIndex < 0 || value.boneIndex >= matrices.Length || !float.IsFinite(value.weight) || value.weight < 0)
                            throw new ArgumentException("Invalid skin weight.");
                        result[i] += matrices[value.boneIndex].MultiplyPoint3x4(source[i]) * value.weight;
                        total += value.weight;
                    }
                    if (Mathf.Abs(total - 1) > .005f || !Finite(result[i])) throw new ArgumentException("Unnormalized or nonfinite skinned vertex.");
                }
                if (cursor != weights.Length) throw new ArgumentException("Unused skin weights.");
                return result;
            }
            finally { counts.Dispose(); weights.Dispose(); }
        }

        // Call before the next fresh graph evaluation, not after it. This also restores unkeyed bones.
        public void RestoreAuthoredPose()
        {
            if (!applied) return;
            if (BodyRoot != null) BodyRoot.localPosition = bodyLocalBefore;
            for (int i = 0; samples != null && Legs != null && i < Mathf.Min(samples.Length, Legs.Length); i++)
            {
                var leg = Legs[i]; var sample = samples[i];
                if (leg?.Upper != null) leg.Upper.localRotation = sample.UpperLocal;
                if (leg?.Lower != null) leg.Lower.localRotation = sample.LowerLocal;
                if (leg?.Foot != null) leg.Foot.localRotation = sample.FootLocal;
            }
            applied = false;
        }
        void OnDisable() => RestoreAuthoredPose();
        void OnEnable() { runtimeBound = false; }

        public bool ApplyPose(float dt)
        {
            BodyDrop = 0; PlantedFeet = 0; RayQueriesLastSample = 0;
            if (!float.IsFinite(dt) || dt < 0) return Fail("INVALID_STEP");
            if (dt == 0 || Time.timeScale <= 0) return Fail("PAUSED");
            if (!float.IsFinite(SoleClearance) || SoleClearance < 0 || SoleClearance > .02f ||
                !float.IsFinite(MaximumFootShift) || MaximumFootShift <= 0 || MaximumFootShift > .14f ||
                !float.IsFinite(MaximumBodyDrop) || MaximumBodyDrop < 0 || MaximumBodyDrop > .12f ||
                !float.IsFinite(SwingLift) || SwingLift < 0 || !float.IsFinite(MaximumFootTilt) || MaximumFootTilt < 0 || MaximumFootTilt > 12 ||
                !float.IsFinite(MaximumGroundSlope) || MaximumGroundSlope < 0 || MaximumGroundSlope > 25 ||
                !float.IsFinite(ProbeUp) || ProbeUp <= 0 || !float.IsFinite(ProbeDown) || ProbeDown <= 0 ||
                !float.IsFinite(SupportHalfLength) || SupportHalfLength <= 0 || !float.IsFinite(SupportHeightDifference) || SupportHeightDifference <= 0)
                return Fail("INVALID_BOUNDED_SETTINGS");
            if (applied) return Fail("RESTORE_REQUIRED_BEFORE_GRAPH_EVALUATION");
            if (!isActiveAndEnabled || !calibrated) return Fail("NOT_READY");
            if (!runtimeBound)
            {
                if (!ValidBinding()) return false;
                navigation = GetComponent<NavMeshAgent>(); runtimeBound = true;
            }
            if (!UniformPositive(ModelRoot.lossyScale) || !UniformPositive(transform.lossyScale)) return Fail("NONUNIFORM_OR_REFLECTED_SCALE");
            if (!Vitals.isActiveAndEnabled || !Vitals.IsAlive) return Fail("DEAD_OR_DISABLED");
            if (navigation != null && (!navigation.enabled || !navigation.isOnNavMesh)) return Fail("OFF_NAV_SURFACE");
            var physics = gameObject.scene.GetPhysicsScene(); if (!physics.IsValid()) return Fail("INVALID_OWNED_PHYSICS_SCENE");
            Allocate(); float drop = 0, scale = ModelRoot.lossyScale.x;
            for (int i = 0; i < Legs.Length; i++)
            {
                var leg = Legs[i]; var sample = samples[i]; var d = Diagnostics[i];
                sample.Hip = leg.Upper.position; sample.Knee = leg.Lower.position; sample.Ankle = leg.Foot.position; sample.Sole = leg.Foot.TransformPoint(leg.SoleLocal);
                sample.UpperLocal = leg.Upper.localRotation; sample.LowerLocal = leg.Lower.localRotation; sample.FootLocal = leg.Foot.localRotation;
                sample.Target = sample.Ankle; sample.DesiredRotation = leg.Foot.rotation;
                d.AuthoredSole = d.SolvedSole = sample.Sole; d.Supported = d.Corrected = false; d.Collider = null; d.GroundGap = d.AnkleError = 0;
                d.AuthoredLift = (transform.InverseTransformPoint(sample.Sole).y - leg.RestSoleActorY) * transform.lossyScale.y;
                d.Planted = sample.Planted = d.AuthoredLift <= SwingLift;
                if (!Finite(sample.Hip) || !Finite(sample.Knee) || !Finite(sample.Ankle) || !Finite(sample.Sole)) return Fail("NONFINITE_POSE");
                if (Mathf.Abs(Vector3.Distance(sample.Hip, sample.Knee) - leg.UpperModelLength * scale) > LengthTolerance ||
                    Mathf.Abs(Vector3.Distance(sample.Knee, sample.Ankle) - leg.LowerModelLength * scale) > LengthTolerance) return Fail("ANIMATED_BONE_LENGTH_CHANGED");
                if (!sample.Planted) continue;
                PlantedFeet++;
                if (!Support(physics, sample.Sole, out var support)) return Fail("UNSUPPORTED_" + leg.Name);
                d.GroundGap = sample.Sole.y - support.point.y;
                if (Mathf.Abs(d.GroundGap - SoleClearance) > MaximumFootShift) return Fail("GROUND_STEP_TOO_LARGE_" + leg.Name);
                var aligned = Quaternion.FromToRotation(leg.Foot.rotation * leg.SoleUpLocal, support.normal) * leg.Foot.rotation;
                sample.DesiredRotation = Quaternion.RotateTowards(leg.Foot.rotation, aligned, MaximumFootTilt);
                d.TargetSole = support.point + Vector3.up * SoleClearance;
                sample.Target = d.TargetSole - sample.DesiredRotation * Vector3.Scale(leg.Foot.lossyScale, leg.SoleLocal);
                if (Vector3.Distance(sample.Ankle, sample.Target) > MaximumFootShift) return Fail("ANKLE_SHIFT_TOO_LARGE_" + leg.Name);
                d.Supported = true; d.Collider = support.collider.name; drop = Mathf.Max(drop, d.GroundGap - SoleClearance);
            }
            if (PlantedFeet < 2) return Fail("AIRBORNE_OR_INSUFFICIENT_STANCE");
            // Never move an airborne leg through its parent's correction. Body drop is all-feet stance only.
            drop = PlantedFeet == 4 ? Mathf.Clamp(drop, 0, MaximumBodyDrop) : 0;
            Vector3 bodyOffset = Vector3.down * drop;
            for (int i = 0; i < Legs.Length; i++)
            {
                var leg = Legs[i]; var sample = samples[i]; if (!sample.Planted) continue;
                Vector3 oldAxis = sample.Ankle - sample.Hip;
                Vector3 bend = Vector3.ProjectOnPlane(sample.Knee - sample.Hip, oldAxis);
                if (bend.sqrMagnitude < .000001f) bend = transform.TransformDirection(leg.BendActorLocal);
                Vector3 newAxis = sample.Target - (sample.Hip + bodyOffset);
                if (newAxis.sqrMagnitude > .000001f) bend = Quaternion.FromToRotation(oldAxis, newAxis) * bend;
                if (!DemoWoodDeerFootPlacement.TryTwoBoneKnee(sample.Hip + bodyOffset, sample.Target, bend,
                    leg.UpperModelLength * scale, leg.LowerModelLength * scale, out sample.SolvedKnee)) return Fail("UNREACHABLE_" + leg.Name);
            }
            bodyLocalBefore = BodyRoot.localPosition; applied = true;
            BodyRoot.position += bodyOffset;
            for (int i = 0; i < Legs.Length; i++)
            {
                var leg = Legs[i]; var sample = samples[i]; if (!sample.Planted) continue;
                leg.Upper.rotation = Quaternion.FromToRotation(leg.Lower.position - leg.Upper.position, sample.SolvedKnee - leg.Upper.position) * leg.Upper.rotation;
                leg.Lower.rotation = Quaternion.FromToRotation(leg.Foot.position - leg.Lower.position, sample.Target - leg.Lower.position) * leg.Lower.rotation;
                leg.Foot.rotation = sample.DesiredRotation;
                var d = Diagnostics[i]; d.AnkleError = Vector3.Distance(leg.Foot.position, sample.Target); d.SolvedSole = leg.Foot.TransformPoint(leg.SoleLocal);
                if (!Finite(d.SolvedSole) || !float.IsFinite(d.AnkleError) || d.AnkleError > TargetTolerance)
                { RestoreAuthoredPose(); foreach (var diagnostic in Diagnostics) diagnostic.Corrected = false; return Fail("SOLVE_ERROR_" + leg.Name); }
                d.Corrected = true;
            }
            BodyDrop = drop; AppliedSamples++; LastStatus = "SMALL_TERRAIN_CORRECTION_APPLIED"; return true;
        }
        bool Support(PhysicsScene physics, Vector3 sole, out RaycastHit centre)
        {
            centre = default;
            if (!Ground(physics, sole, out centre)) return false;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized * SupportHalfLength;
            return Ground(physics, sole + forward, out var toe) && Ground(physics, sole - forward, out var heel) &&
                Mathf.Abs(toe.point.y - centre.point.y) <= SupportHeightDifference && Mathf.Abs(heel.point.y - centre.point.y) <= SupportHeightDifference;
        }
        bool Ground(PhysicsScene physics, Vector3 sole, out RaycastHit best)
        {
            best = default; RayQueriesLastSample++;
            int count = physics.Raycast(sole + Vector3.up * ProbeUp, Vector3.down, hits, ProbeUp + ProbeDown, GroundMask, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var h = hits[i]; var c = h.collider;
                if (c == null || c is CharacterController || c.transform.IsChildOf(transform) ||
                    c.GetComponentInParent<EnemyVitals>() != null || c.GetComponentInParent<PlayerVitals>() != null || c.GetComponentInParent<DemoSummonPresentation>() != null ||
                    !Finite(h.point) || !Finite(h.normal) || Vector3.Angle(h.normal, Vector3.up) > MaximumGroundSlope || h.distance >= nearest) continue;
                nearest = h.distance; best = h;
            }
            return float.IsFinite(nearest);
        }
        bool Fail(string reason) { LastStatus = reason; return false; }
        static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        static bool UniformPositive(Vector3 v) => Finite(v) && v.x > 0 && v.y > 0 && v.z > 0 && Mathf.Abs(v.x - v.y) < .001f && Mathf.Abs(v.x - v.z) < .001f;
    }
}
