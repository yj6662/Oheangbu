using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.App.Demo
{
    /// <summary>
    /// Small terrain correction for the approved deer derivative. Add to the combat actor root.
    /// Configure AFTER the model's initial animation evaluation, then call Sample exactly once
    /// after each fresh graph.Evaluate. This class has no Update/LateUpdate and owns no movement,
    /// time, damage or mesh edits. A false result leaves the evaluated animation unchanged.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DemoWoodDeerFootPlacement : MonoBehaviour
    {
        const float SoleClearance = .005f;
        const float MaximumBodyDrop = .12f;
        const float MaximumFootShift = .14f;
        const float SwingLift = .025f;
        const float ProbeUp = .18f, ProbeDown = .32f;
        const float SupportHalfLength = .025f, SupportHeightDifference = .03f;
        const float MaximumFootTilt = 12f;
        const float ReachMargin = .0002f, LengthTolerance = .0015f, TargetTolerance = .003f;
        static readonly string[] Names = { "Fore_L", "Fore_R", "Hind_L", "Hind_R" };

        [Serializable] public sealed class FootResult
        {
            public string name;
            public bool planted, supported;
            public float authoredLift, authoredGap, ankleError;
            public Vector3 authoredSole, surfacePoint, targetSole, targetAnkle, solvedAnkle;
            public string colliderName;
        }
        sealed class Leg
        {
            public string Name;
            public Transform Upper, Lower, Hoof;
            public float UpperLength, LowerLength, RestSoleHeight;
            public Vector3 SoleLocal, SoleUpLocal, BendPoleActorLocal;
            public Vector3 Hip, Knee, Ankle, Sole, Target, SolvedKnee;
            public Quaternion UpperRotation, LowerRotation, HoofRotation, DesiredHoofRotation;
            public Quaternion UpperLocalRotation, LowerLocalRotation, HoofLocalRotation;
            public bool Planted;
        }
        readonly Leg[] legs = new Leg[4];
        readonly FootResult[] diagnostics = new FootResult[4];
        readonly RaycastHit[] hits = new RaycastHit[64];
        Transform model, bodyRoot;
        SummonCombatProfile profile;
        PhysicsScene physics;
        bool configured;
        public bool Configured => configured;
        public bool SupportedLastSample { get; private set; }
        public string LastStatus { get; private set; } = "NOT_CONFIGURED";
        public float BodyDrop { get; private set; }
        public int PlantedFeet { get; private set; }
        public int SkippedSwingFeet { get; private set; }
        public int RayQueriesLastSample { get; private set; }
        public FootResult[] Diagnostics => diagnostics;

        public void Configure(GameObject visualModel, SummonCombatProfile value)
        {
            if (configured) throw new InvalidOperationException("Foot placement is configured once per summon.");
            if (visualModel == null || value == null || !visualModel.transform.IsChildOf(transform))
                throw new ArgumentException("Attach foot placement to the actor root and supply its instantiated model.");
            model = visualModel.transform; profile = value;
            physics = visualModel.scene.GetPhysicsScene();
            if (!visualModel.scene.IsValid() || !physics.IsValid()) throw new InvalidOperationException("Valid owned physics scene required.");
            var transforms = visualModel.GetComponentsInChildren<Transform>(true);
            bodyRoot = Unique(transforms, "Root");
            var skins = visualModel.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer skin = null;
            foreach (var candidate in skins)
                if (candidate.name == "WoodDeer_Body") { if (skin != null) throw new InvalidOperationException("Ambiguous deer body."); skin = candidate; }
            if (skin == null || skin.sharedMesh == null) throw new InvalidOperationException("Approved skinned deer body required.");
            if (!UniformPositive(model.lossyScale)) throw new InvalidOperationException("Nonuniform/reflected model scale is unsupported.");

            for (int i = 0; i < legs.Length; i++)
            {
                string name = Names[i];
                var leg = new Leg { Name = name, Upper = Unique(transforms, name + "_Upper"), Lower = Unique(transforms, name + "_Lower"), Hoof = Unique(transforms, name + "_Hoof") };
                if (leg.Lower.parent != leg.Upper || leg.Hoof.parent != leg.Lower || !leg.Upper.IsChildOf(bodyRoot))
                    throw new InvalidOperationException("Unexpected deer chain hierarchy: " + name);
                leg.UpperLength = Vector3.Distance(leg.Upper.position, leg.Lower.position);
                leg.LowerLength = Vector3.Distance(leg.Lower.position, leg.Hoof.position);
                if (leg.UpperLength < .05f || leg.LowerLength < .05f) throw new InvalidOperationException("Degenerate deer chain: " + name);
                Vector3 direction = (leg.Hoof.position - leg.Upper.position).normalized;
                Vector3 bend = Vector3.ProjectOnPlane(leg.Lower.position - leg.Upper.position, direction);
                if (bend.sqrMagnitude < .000001f) bend = Vector3.ProjectOnPlane(transform.forward, direction);
                leg.BendPoleActorLocal = transform.InverseTransformDirection(bend.normalized);
                leg.SoleUpLocal = leg.Hoof.InverseTransformDirection(Vector3.up);
                legs[i] = leg; diagnostics[i] = new FootResult { name = name };
            }

            // The real skinned surface, not the hoof pivot or renderer's animation AABB, defines contact.
            var baked = new Mesh();
            try
            {
                skin.BakeMesh(baked, false); Vector3[] vertices = baked.vertices;
                var counts = skin.sharedMesh.GetBonesPerVertex(); var weights = skin.sharedMesh.GetAllBoneWeights();
                var candidates = new List<int>[4]; for (int i = 0; i < 4; i++) candidates[i] = new List<int>();
                try
                {
                    int cursor = 0;
                    for (int vertex = 0; vertex < counts.Length; vertex++)
                        for (int j = 0; j < counts[vertex]; j++)
                        {
                            var w = weights[cursor++]; if (w.weight <= .5f || w.boneIndex >= skin.bones.Length) continue;
                            for (int i = 0; i < 4; i++) if (skin.bones[w.boneIndex] == legs[i].Hoof) candidates[i].Add(vertex);
                        }
                }
                finally { counts.Dispose(); weights.Dispose(); }
                for (int i = 0; i < 4; i++)
                {
                    if (candidates[i].Count < 3) throw new InvalidOperationException("No measured skinned hoof surface: " + Names[i]);
                    Vector3 bottom = skin.transform.TransformPoint(vertices[candidates[i][0]]);
                    foreach (int index in candidates[i])
                    { Vector3 point = skin.transform.TransformPoint(vertices[index]); if (point.y < bottom.y) bottom = point; }
                    Leg leg = legs[i]; leg.SoleLocal = leg.Hoof.InverseTransformPoint(bottom);
                    leg.RestSoleHeight = bottom.y - transform.position.y;
                    if (!Finite(bottom) || leg.RestSoleHeight < -.05f || leg.RestSoleHeight > .05f)
                        throw new InvalidOperationException("Configure in the initial supported idle pose, before terrain correction: " + Names[i]);
                }
            }
            finally { if (Application.isPlaying) Destroy(baked); else DestroyImmediate(baked); }
            configured = true; LastStatus = "READY_AFTER_ANIMATION";
        }

        public bool Sample(SummonCombatClock clock, bool moving)
        {
            SupportedLastSample = false; BodyDrop = 0; PlantedFeet = 0; SkippedSwingFeet = 0; RayQueriesLastSample = 0;
            if (!configured || model == null || bodyRoot == null || clock == null) return Fail("NOT_READY");
            if (clock.Phase == SummonPhase.Ended) return Fail("ENDED");
            if (!model.gameObject.scene.IsValid()) return Fail("INVALID_OWNED_SCENE");
            physics = model.gameObject.scene.GetPhysicsScene();
            if (!physics.IsValid() || !UniformPositive(model.lossyScale)) return Fail("INVALID_SCENE_OR_SCALE");
            foreach (var d in diagnostics) { d.supported = false; d.planted = false; d.colliderName = null; d.ankleError = 0; }
            float drop = 0;
            for (int i = 0; i < 4; i++)
            {
                Leg leg = legs[i]; FootResult d = diagnostics[i];
                leg.Hip = leg.Upper.position; leg.Knee = leg.Lower.position; leg.Ankle = leg.Hoof.position;
                leg.UpperRotation = leg.Upper.rotation; leg.LowerRotation = leg.Lower.rotation; leg.HoofRotation = leg.Hoof.rotation;
                leg.UpperLocalRotation = leg.Upper.localRotation; leg.LowerLocalRotation = leg.Lower.localRotation; leg.HoofLocalRotation = leg.Hoof.localRotation;
                leg.Sole = leg.Hoof.TransformPoint(leg.SoleLocal);
                d.authoredSole = leg.Sole; d.authoredLift = leg.Sole.y - transform.position.y - leg.RestSoleHeight;
                d.supported = false; d.colliderName = null; d.ankleError = 0; d.authoredGap = 0;
                // Measured lift also protects attack/recovery blends; never pull an authored swing into the floor.
                leg.Planted = d.authoredLift <= SwingLift; d.planted = leg.Planted;
                leg.Target = leg.Ankle; leg.DesiredHoofRotation = leg.HoofRotation;
                if (!Finite(leg.Hip) || !Finite(leg.Knee) || !Finite(leg.Ankle) || !Finite(leg.Sole)) return Fail("NONFINITE_POSE");
                if (Mathf.Abs(Vector3.Distance(leg.Hip, leg.Knee) - leg.UpperLength) > LengthTolerance ||
                    Mathf.Abs(Vector3.Distance(leg.Knee, leg.Ankle) - leg.LowerLength) > LengthTolerance) return Fail("ANIMATED_BONE_LENGTH_CHANGED");
                if (!leg.Planted) { SkippedSwingFeet++; continue; }
                PlantedFeet++;
                if (!TrySupport(leg.Sole, out var support)) return Fail("UNSUPPORTED_" + leg.Name);
                float gap = leg.Sole.y - (support.point.y + SoleClearance);
                if (Mathf.Abs(gap) > MaximumFootShift) return Fail("TERRAIN_STEP_TOO_LARGE_" + leg.Name);
                Vector3 up = leg.HoofRotation * leg.SoleUpLocal;
                Quaternion aligned = Quaternion.FromToRotation(up, support.normal) * leg.HoofRotation;
                leg.DesiredHoofRotation = Quaternion.RotateTowards(leg.HoofRotation, aligned, MaximumFootTilt);
                Vector3 soleOffset = leg.DesiredHoofRotation * Vector3.Scale(leg.Hoof.lossyScale, leg.SoleLocal);
                Vector3 targetSole = support.point + Vector3.up * SoleClearance;
                leg.Target = targetSole - soleOffset;
                if (Vector3.Distance(leg.Target, leg.Ankle) > MaximumFootShift) return Fail("ANKLE_SHIFT_TOO_LARGE_" + leg.Name);
                drop = Mathf.Max(drop, gap);
                d.supported = true; d.surfacePoint = support.point; d.targetSole = targetSole; d.targetAnkle = leg.Target;
                d.authoredGap = leg.Sole.y - support.point.y; d.colliderName = support.collider.name;
            }
            if (PlantedFeet < 2) return Fail(moving ? "INSUFFICIENT_STANCE_FEET" : "AIRBORNE_POSE");
            drop = Mathf.Clamp(drop, 0, MaximumBodyDrop);
            Vector3 bodyOffset = Vector3.down * drop;
            // Validate EVERY chain before touching any transform. Swing ankles retain their authored world pose.
            for (int i = 0; i < 4; i++)
            {
                Leg leg = legs[i]; Vector3 hip = leg.Hip + bodyOffset;
                Vector3 oldAxis = leg.Ankle - leg.Hip;
                Vector3 bend = Vector3.ProjectOnPlane(leg.Knee - leg.Hip, oldAxis.normalized);
                if (bend.sqrMagnitude < .000001f) bend = transform.TransformDirection(leg.BendPoleActorLocal);
                Vector3 newAxis = leg.Target - hip;
                if (oldAxis.sqrMagnitude > .000001f && newAxis.sqrMagnitude > .000001f) bend = Quaternion.FromToRotation(oldAxis, newAxis) * bend;
                if (!TryTwoBoneKnee(hip, leg.Target, bend, leg.UpperLength, leg.LowerLength, out leg.SolvedKnee))
                    return Fail("UNREACHABLE_" + leg.Name);
            }
            Vector3 rootLocal = bodyRoot.localPosition;
            bodyRoot.position += bodyOffset;
            for (int i = 0; i < 4; i++)
            {
                Leg leg = legs[i];
                leg.Upper.rotation = Quaternion.FromToRotation(leg.Lower.position - leg.Upper.position, leg.SolvedKnee - leg.Upper.position) * leg.Upper.rotation;
                leg.Lower.rotation = Quaternion.FromToRotation(leg.Hoof.position - leg.Lower.position, leg.Target - leg.Lower.position) * leg.Lower.rotation;
                leg.Hoof.rotation = leg.DesiredHoofRotation;
                float error = Vector3.Distance(leg.Hoof.position, leg.Target);
                diagnostics[i].solvedAnkle = leg.Hoof.position; diagnostics[i].targetAnkle = leg.Target; diagnostics[i].ankleError = error;
                if (!float.IsFinite(error) || error > TargetTolerance)
                { Restore(rootLocal); return Fail("SOLVE_ERROR_" + leg.Name); }
            }
            BodyDrop = drop; SupportedLastSample = true; LastStatus = "SMALL_TERRAIN_CORRECTION_APPLIED"; return true;
        }

        // Pure analytic solver: fixed segment lengths, preserved knee plane, no target clamping/stretching.
        public static bool TryTwoBoneKnee(Vector3 hip, Vector3 ankle, Vector3 bendHint, float upper, float lower, out Vector3 knee)
        {
            knee = default;
            if (!Finite(hip) || !Finite(ankle) || !Finite(bendHint) || !float.IsFinite(upper) || !float.IsFinite(lower) || upper <= 0 || lower <= 0) return false;
            Vector3 delta = ankle - hip; float distance = delta.magnitude;
            if (distance <= Mathf.Abs(upper - lower) + ReachMargin || distance >= upper + lower - ReachMargin) return false;
            Vector3 axis = delta / distance; Vector3 bend = Vector3.ProjectOnPlane(bendHint, axis);
            if (bend.sqrMagnitude < .0000001f) return false;
            bend.Normalize();
            float along = (upper * upper - lower * lower + distance * distance) / (2 * distance);
            float heightSquared = upper * upper - along * along;
            if (heightSquared < 0 || !float.IsFinite(heightSquared)) return false;
            knee = hip + axis * along + bend * Mathf.Sqrt(heightSquared); return Finite(knee);
        }
        bool TrySupport(Vector3 sole, out RaycastHit centre)
        {
            centre = default;
            if (!TryGround(sole, out centre)) return false;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized * SupportHalfLength;
            if (!TryGround(sole + forward, out var toe) || !TryGround(sole - forward, out var heel)) return false;
            return Mathf.Abs(toe.point.y - centre.point.y) <= SupportHeightDifference && Mathf.Abs(heel.point.y - centre.point.y) <= SupportHeightDifference;
        }
        bool TryGround(Vector3 sole, out RaycastHit selected)
        {
            selected = default; RayQueriesLastSample++;
            int count = physics.Raycast(sole + Vector3.up * ProbeUp, Vector3.down, hits, ProbeUp + ProbeDown, profile.GroundMask, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (IgnoreCharacter(hit.collider) || !Finite(hit.point) || !Finite(hit.normal) ||
                    Vector3.Angle(hit.normal, Vector3.up) > Mathf.Min(25f, profile.MaxSlope) || hit.distance >= nearest) continue;
                nearest = hit.distance; selected = hit;
            }
            return float.IsFinite(nearest);
        }
        bool IgnoreCharacter(Collider collider) => collider == null || collider is CharacterController ||
            collider.transform.IsChildOf(transform) || collider.GetComponentInParent<EnemyVitals>() != null ||
            collider.GetComponentInParent<PlayerVitals>() != null || collider.GetComponentInParent<DemoSummonPresentation>() != null;
        void Restore(Vector3 rootLocal)
        {
            bodyRoot.localPosition = rootLocal;
            for (int i = 0; i < 4; i++)
            { Leg leg = legs[i]; leg.Upper.localRotation = leg.UpperLocalRotation; leg.Lower.localRotation = leg.LowerLocalRotation; leg.Hoof.localRotation = leg.HoofLocalRotation; }
        }
        bool Fail(string reason) { LastStatus = reason; return false; }
        static Transform Unique(Transform[] all, string name)
        {
            Transform found = null;
            foreach (var t in all) if (t.name == name) { if (found != null) throw new InvalidOperationException("Ambiguous bone: " + name); found = t; }
            if (found == null) throw new InvalidOperationException("Missing deer bone: " + name); return found;
        }
        static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
        static bool UniformPositive(Vector3 scale) => Finite(scale) && scale.x > 0 && scale.y > 0 && scale.z > 0 &&
            Mathf.Abs(scale.x - scale.y) < .001f && Mathf.Abs(scale.x - scale.z) < .001f;
    }
}
