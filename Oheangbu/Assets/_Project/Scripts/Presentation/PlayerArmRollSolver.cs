using System;
using UnityEngine;

namespace Oheangbu.Presentation
{
    /// <summary>
    /// Candidate orientation-only postprocess for an already solved two-bone arm.
    /// Explicitly opt in per profile; bind rotations come from the mesh bindposes,
    /// never from an Animator pose. End-hand rotation and bone translations stay fixed.
    /// No scene queries, allocations or gameplay calls during Evaluate/Apply.
    /// </summary>
    public sealed class PlayerArmRollSolver
    {
        [Serializable]
        public struct Settings
        {
            public bool Enabled;
            [Min(1f)] public float MaximumRollDegreesPerSecond;
            [Range(5f, 90f)] public float TrackingWindowDegrees;
            public static Settings CandidateDefault => new Settings
            { Enabled = false, MaximumRollDegreesPerSecond = 720f, TrackingWindowDegrees = 40f };
        }

        public struct BindPose
        {
            public Quaternion UpperLocal, ForearmLocal, HandLocal;
        }

        public struct State
        {
            public bool Initialized;
            public Quaternion UpperInParent, ForearmInParent;
        }

        [Serializable]
        public struct Result
        {
            public Quaternion Upper, Forearm, Hand;
            public Vector3 BeforeJointDegrees, AfterJointDegrees;
            public float UpperAxialDegrees, ForearmAxialDegrees;
            public int CostEvaluations;
            public bool Applied;
            public bool PreferredLocalBranch, RateLimitBypassed, WristBudgetExceeded;
            public float TargetCost;
        }

        private readonly Transform _parent, _upper, _forearm, _hand;
        private readonly BindPose _bind;
        private State _state;
        public Result Diagnostics { get; private set; }

        private PlayerArmRollSolver(Transform upper, Transform forearm, Transform hand, BindPose bind)
        { _upper = upper; _forearm = forearm; _hand = hand; _parent = upper.parent; _bind = bind; }

        public void Reset() { _state = default; }

        public static bool TryBind(Transform upper, Transform forearm, Transform hand,
            SkinnedMeshRenderer[] renderers, out PlayerArmRollSolver solver)
        {
            solver = null;
            if (upper == null || upper.parent == null || forearm == null || hand == null
                || forearm.parent != upper || hand.parent != forearm || renderers == null) return false;
            // Rotation-only postprocessing assumes the authored hierarchy has unit local scale.
            if (!UnitScale(upper) || !UnitScale(forearm) || !UnitScale(hand)) return false;
            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer.sharedMesh == null) continue;
                var bones = renderer.bones; var bindposes = renderer.sharedMesh.bindposes;
                if (bones.Length != bindposes.Length) continue;
                int p = Array.IndexOf(bones, upper.parent), u = Array.IndexOf(bones, upper);
                int f = Array.IndexOf(bones, forearm), h = Array.IndexOf(bones, hand);
                if (p < 0 || u < 0 || f < 0 || h < 0) continue;
                // bindpose = inverse(boneRestWorld) * rendererRestWorld.
                // Parent bindpose * inverse(child bindpose) is child relative to parent.
                var bind = new BindPose
                {
                    UpperLocal = (bindposes[p] * bindposes[u].inverse).rotation,
                    ForearmLocal = (bindposes[u] * bindposes[f].inverse).rotation,
                    HandLocal = (bindposes[f] * bindposes[h].inverse).rotation
                };
                solver = new PlayerArmRollSolver(upper, forearm, hand, bind); return true;
            }
            return false;
        }

        private static bool UnitScale(Transform value) =>
            (value.localScale - Vector3.one).sqrMagnitude < 0.00000001f;

        /// <summary>Call after PlayerVisualIK.Solve, before PlaceBrushOnHand and secondary motion.</summary>
        public Result Apply(Settings settings, float weight, float deltaTime)
        {
            if (!settings.Enabled || weight <= 0f) { Reset(); return Diagnostics = default; }
            Quaternion handRotation = _hand.rotation;
            Result result = Evaluate(_parent.rotation, _upper.rotation, _forearm.rotation, handRotation,
                _forearm.position - _upper.position, _hand.position - _forearm.position,
                _bind, ref _state, settings, weight, deltaTime);
            if (result.Applied)
            {
                // Axial rotations preserve the two child positions because their axes
                // pass through the joint endpoints. Do not repair errors by translating bones.
                _upper.rotation = result.Upper;
                _forearm.rotation = result.Forearm;
                _hand.rotation = handRotation;
            }
            return Diagnostics = result;
        }

        /// <summary>Pure orientation search; axes and all four input rotations are world-space.</summary>
        public static Result Evaluate(Quaternion parent, Quaternion upper, Quaternion forearm,
            Quaternion hand, Vector3 upperAxis, Vector3 forearmAxis, BindPose bind,
            ref State state, Settings settings, float weight, float deltaTime)
        {
            var result = new Result { Upper = upper, Forearm = forearm, Hand = hand };
            if (!settings.Enabled || weight <= 0f || upperAxis.sqrMagnitude < 1e-8f || forearmAxis.sqrMagnitude < 1e-8f)
            { state = default; return result; }
            upperAxis.Normalize(); forearmAxis.Normalize();
            result.BeforeJointDegrees = JointDegrees(parent, upper, forearm, hand, bind);
            // A periodic global candidate is a feasibility reference, not an
            // instruction to change to an equally good opposite roll branch.
            const float branchCostHysteresis = 10f;
            const float wristRotationBudget = 120f;
            const float cappedCostSlack = .25f;
            float centerU = 0f, centerF = 0f;
            if (state.Initialized)
            {
                Quaternion previousU = parent * state.UpperInParent;
                Quaternion previousF = parent * state.ForearmInParent;
                Vector3 localU = Quaternion.Inverse(upper) * upperAxis;
                Vector3 localF = Quaternion.Inverse(forearm) * forearmAxis;
                previousU = Quaternion.FromToRotation(previousU * localU, upperAxis) * previousU;
                previousF = Quaternion.FromToRotation(previousF * localF, forearmAxis) * previousF;
                centerU = SignedTwist(previousU * Quaternion.Inverse(upper), upperAxis);
                centerF = SignedTwist(previousF * Quaternion.Inverse(forearm), forearmAxis);
            }
            var search = new Search(parent, upper, forearm, hand, upperAxis, forearmAxis, bind);
            search.Set(centerU, centerF);
            search.Grid(centerU, centerF, 30f, 6);
            search.Refine(5f, 4); search.Refine(1f, 5); search.Refine(.2f, 5);
            int otherEvaluations = 0;
            if (state.Initialized)
            {
                var local = new Search(parent, upper, forearm, hand, upperAxis, forearmAxis, bind);
                local.Set(centerU, centerF);
                local.Grid(centerU, centerF, Mathf.Clamp(settings.TrackingWindowDegrees, 5f, 90f) / 4f, 4);
                local.Refine(5f, 4); local.Refine(1f, 5); local.Refine(.2f, 5);
                local.CostAt(local.BestU, local.BestF, out Vector3 localAngles);
                if (local.BestCost <= search.BestCost + branchCostHysteresis && localAngles.z <= wristRotationBudget)
                { otherEvaluations = search.Evaluations; search = local; result.PreferredLocalBranch = true; }
                else otherEvaluations = local.Evaluations;
            }
            float chosenU = search.BestU, chosenF = search.BestF;
            float w = Mathf.Clamp01(weight);
            result.TargetCost = search.BestCost;
            if (state.Initialized)
            {
                float maxDelta = Mathf.Max(1f, settings.MaximumRollDegreesPerSecond) * Mathf.Max(0f, deltaTime);
                float limitedU = centerU + Mathf.Clamp(Mathf.DeltaAngle(centerU, chosenU), -maxDelta, maxDelta);
                float limitedF = centerF + Mathf.Clamp(Mathf.DeltaAngle(centerF, chosenF), -maxDelta, maxDelta);
                float cappedCost = search.CostAt(limitedU, limitedF, out Vector3 cappedAngles);
                // A hard rate cap caused an observed 157.5deg wrist collapse
                // despite a reachable same-tip solution. Keep the cap when it
                // remains near the chosen branch; otherwise re-acquire it.
                bool retainCap = deltaTime <= 0f || (cappedCost <= search.BestCost + cappedCostSlack && cappedAngles.z <= wristRotationBudget);
                result.RateLimitBypassed = !retainCap;
                if (retainCap) { chosenU = limitedU; chosenF = limitedF; }
                chosenU = centerU + Mathf.DeltaAngle(centerU, chosenU) * w;
                chosenF = centerF + Mathf.DeltaAngle(centerF, chosenF) * w;
            }
            else { chosenU = Mathf.DeltaAngle(0f, chosenU) * w; chosenF = Mathf.DeltaAngle(0f, chosenF) * w; }
            result.Upper = Quaternion.AngleAxis(chosenU, upperAxis) * upper;
            result.Forearm = Quaternion.AngleAxis(chosenF, forearmAxis) * forearm;
            result.UpperAxialDegrees = chosenU;
            result.ForearmAxialDegrees = chosenF;
            result.AfterJointDegrees = JointDegrees(parent, result.Upper, result.Forearm, hand, bind);
            result.CostEvaluations = search.Evaluations + otherEvaluations; result.Applied = true;
            result.WristBudgetExceeded = result.AfterJointDegrees.z > wristRotationBudget;
            state.Initialized = true;
            state.UpperInParent = Quaternion.Inverse(parent) * result.Upper;
            state.ForearmInParent = Quaternion.Inverse(parent) * result.Forearm;
            return result;
        }

        private static float SignedTwist(Quaternion q, Vector3 axis)
        {
            if (q.w < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            float projection = q.x * axis.x + q.y * axis.y + q.z * axis.z;
            return Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(projection, q.w) * Mathf.Rad2Deg);
        }

        private static Vector3 JointDegrees(Quaternion p, Quaternion u, Quaternion f, Quaternion h, BindPose bind) =>
            new Vector3(Quaternion.Angle(Quaternion.Inverse(p) * u, bind.UpperLocal),
                Quaternion.Angle(Quaternion.Inverse(u) * f, bind.ForearmLocal),
                Quaternion.Angle(Quaternion.Inverse(f) * h, bind.HandLocal));

        private struct Search
        {
            private readonly Quaternion _parent, _upper, _forearm, _hand;
            private readonly Vector3 _upperAxis, _forearmAxis;
            private readonly BindPose _bind;
            private float _best;
            public float BestU, BestF;
            public float BestCost => _best;
            public int Evaluations;
            public Search(Quaternion p, Quaternion u, Quaternion f, Quaternion h, Vector3 ua, Vector3 fa, BindPose bind)
            { _parent = p; _upper = u; _forearm = f; _hand = h; _upperAxis = ua; _forearmAxis = fa; _bind = bind;
              _best = float.PositiveInfinity; BestU = BestF = 0f; Evaluations = 0; }
            public float CostAt(float u, float f, out Vector3 a)
            {
                a = JointDegrees(_parent, Quaternion.AngleAxis(u, _upperAxis) * _upper,
                    Quaternion.AngleAxis(f, _forearmAxis) * _forearm, _hand, _bind);
                Evaluations++;
                return Mathf.Max(a.x, Mathf.Max(a.y, a.z)) + .0001f * a.sqrMagnitude;
            }
            public void Set(float u, float f)
            {
                float cost = CostAt(u, f, out _);
                if (cost < _best - .00001f) { _best = cost; BestU = u; BestF = f; }
            }
            public void Grid(float u, float f, float step, int half)
            { for (int i = -half; i <= half; i++) for (int j = -half; j <= half; j++) Set(u + i * step, f + j * step); }
            public void Refine(float step, int half) { float u = BestU, f = BestF; Grid(u, f, step, half); }
        }
    }
}
