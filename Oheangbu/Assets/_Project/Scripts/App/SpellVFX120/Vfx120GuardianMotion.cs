using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // Rigid Blender stone parts, not a summoned gameplay actor. This helper never
    // selects targets, emits hit events, advances a simulation clock or creates objects.
    public static class Vfx120GuardianMotion
    {
        public const int PartCount = 11;
        public enum Part { Pelvis, Torso, Head, LeftArm, LeftFist, RightArm, RightFist, LeftLeg, LeftFoot, RightLeg, RightFoot }

        public struct Context
        {
            public float Age, Life, FormationTime, Height, OriginGround, TargetGround;
            // -1 means absent. StrikeAt must be an explicitly supplied planned cue;
            // a late HitAt cannot retroactively create a windup before the actual hit.
            public float StrikeAt, HitAt;
            public bool Demonstration, HasTarget;
            public Vector3 Target, RightFistContact;
            // Immutable shared values from guardian_parts_manifest.json, Unity axes,
            // in the ORIGINAL normalized full-body space. Meshes are pivot-relative.
            public Vector3[] Pivots;
        }

        public static int Parent(int index)
        {
            switch ((Part)index)
            {
                case Part.Torso: case Part.LeftLeg: case Part.RightLeg: return (int)Part.Pelvis;
                case Part.Head: case Part.LeftArm: case Part.RightArm: return (int)Part.Torso;
                case Part.LeftFist: return (int)Part.LeftArm;
                case Part.RightFist: return (int)Part.RightArm;
                case Part.LeftFoot: return (int)Part.LeftLeg;
                case Part.RightFoot: return (int)Part.RightLeg;
                default: return -1;
            }
        }

        public static bool TrySampleBody(Context c, int index, out Vfx120CueMotion.Pose pose)
        {
            pose = new Vfx120CueMotion.Pose { Rotation = Quaternion.identity };
            if (index < 0 || index >= PartCount) return false;
            if (!Valid(c) || c.Age >= c.Life) return true; // Authored hidden pose; no generic fallback.
            var phase = Evaluate(c);
            Joint(c, phase, index, out Vector3 position, out Quaternion rotation);
            float delay = AssemblyDelay(index) * phase.Formation;
            float appear = Ease(Unit(c.Age - delay, Mathf.Max(.055f, phase.Formation * .42f)));
            float disappear = Ease(Unit(c.Age - Mathf.Max(0, c.Life - .55f) - ExitDelay(index), .28f));
            float lifeFade = 1 - Ease(Unit(c.Age - Mathf.Max(0, c.Life - .18f), .18f));
            float alpha = appear * (1 - disappear) * lifeFade;
            // Stone blocks keep their actual dimensions. Assembly/exit translate
            // from their seams; scaling the whole guardian would look like inflation.
            position.y += (1 - appear) * .12f - disappear * .17f;
            pose.Position = position;
            pose.Rotation = rotation;
            pose.Scale = Vector3.one * c.Height;
            pose.Alpha = alpha;
            pose.Visible = alpha > .001f;
            return true;
        }

        // Provides a matching anchor for the floor seal and low dust. Never reuse the
        // generic target-center anchor, which would place the target inside the torso.
        public static bool TrySampleAnchor(Context c, out Vector3 groundPoint, out Quaternion facing)
        {
            groundPoint = Vector3.zero; facing = Quaternion.identity;
            if (!Valid(c)) return false;
            var phase = Evaluate(c);
            groundPoint = phase.GroundPoint; facing = phase.Facing;
            return true;
        }

        private struct Phase
        {
            public Vector3 GroundPoint;
            public Quaternion Facing;
            public float Formation, Step, Windup, Punch, Recoil, Breath;
        }

        private static Phase Evaluate(Context c)
        {
            var p = new Phase();
            p.Formation = Mathf.Min(Mathf.Max(.18f, c.FormationTime), Mathf.Max(.18f, c.Life * .23f));
            Vector3 forward = Flat(c.Target);
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 start = -right * (c.Height * .58f) + forward * .70f;
            start.y = c.OriginGround;
            // Approach the target's outer flank and strike back toward it. This
            // leaves the striking shoulder outside the torso/target overlap; the
            // facing still follows the actual target, with no camera dependency.
            Vector3 stop = c.HasTarget ? c.Target + forward * (c.Height * .12f) + right * (c.Height * .28f) : start;
            stop.y = c.HasTarget ? c.TargetGround : c.OriginGround;
            float strikeAt = c.StrikeAt;
            if (strikeAt < 0 && c.Demonstration && c.HasTarget)
                strikeAt = Mathf.Max(p.Formation + .55f, c.Life * .55f);
            bool scheduled = strikeAt >= p.Formation + .12f && strikeAt < c.Life && c.HasTarget;
            float approachEnd = scheduled ? Mathf.Max(p.Formation + .05f, strikeAt - .36f) : p.Formation;
            float approach = scheduled ? Ease(Unit(c.Age - p.Formation, approachEnd - p.Formation)) : 0;
            p.GroundPoint = Vector3.Lerp(start, stop, approach);
            Vector3 direction = c.HasTarget ? c.Target - p.GroundPoint : forward;
            p.Facing = Quaternion.LookRotation(Flat(direction), Vector3.up);
            p.Breath = Mathf.Sin(c.Age * 2.2f) * .7f;
            if (scheduled)
            {
                float stride = Unit(c.Age - p.Formation, approachEnd - p.Formation);
                p.Step = Mathf.Sin(stride * Mathf.PI * 4) * Mathf.Sin(stride * Mathf.PI);
                p.Windup = Ease(Unit(c.Age - (strikeAt - .32f), .22f))
                    * (1 - Ease(Unit(c.Age - (strikeAt - .10f), .10f)));
                // Punch peak coincides exactly with the supplied/demo strike cue.
                p.Punch = Ease(Unit(c.Age - (strikeAt - .10f), .10f))
                    * (1 - Ease(Unit(c.Age - strikeAt, .23f)));
                p.Recoil = Mathf.Sin(Unit(c.Age - strikeAt, .42f) * Mathf.PI) * .65f;
            }
            else if (c.HitAt >= 0 && c.Age >= c.HitAt)
            {
                // Unannounced actual contact: immediate contact/recovery only. There
                // is deliberately no made-up earlier advance or anticipation phase.
                p.Punch = 1 - Ease(Unit(c.Age - c.HitAt, .23f));
                p.Recoil = Mathf.Sin(Unit(c.Age - c.HitAt, .42f) * Mathf.PI) * .65f;
            }
            return p;
        }

        private static void Joint(Context c, Phase p, int index, out Vector3 position, out Quaternion rotation)
        {
            int parent = Parent(index);
            if (parent < 0)
            {
                // Full source bounds are [-.5,+.5] in Y. Keep feet grounded while
                // torso/shoulder rotations provide the weight transfer above them.
                rotation = p.Facing;
                position = p.GroundPoint + Vector3.up * (c.Height * .5f)
                    + rotation * (c.Pivots[index] * c.Height);
            }
            else
            {
                Joint(c, p, parent, out Vector3 parentPosition, out Quaternion parentRotation);
                position = parentPosition + parentRotation * ((c.Pivots[index] - c.Pivots[parent]) * c.Height);
                rotation = parentRotation;
            }
            rotation *= LocalRotation((Part)index, p);
            if (c.HasTarget && p.Punch > .001f && (index == (int)Part.RightArm || index == (int)Part.RightFist))
            {
                SolveRightStrike(c, p, out Quaternion upper, out Quaternion fist);
                rotation = Quaternion.Slerp(rotation, index == (int)Part.RightArm ? upper : fist, p.Punch);
            }
        }

        private static void SolveRightStrike(Context c, Phase p, out Quaternion upper, out Quaternion fist)
        {
            Joint(c, p, (int)Part.Torso, out Vector3 torso, out Quaternion torsoRotation);
            Vector3 shoulder = torso + torsoRotation * ((c.Pivots[(int)Part.RightArm] - c.Pivots[(int)Part.Torso]) * c.Height);
            Vector3 first = (c.Pivots[(int)Part.RightFist] - c.Pivots[(int)Part.RightArm]) * c.Height;
            Vector3 second = (c.RightFistContact - c.Pivots[(int)Part.RightFist]) * c.Height;
            float a = Mathf.Max(.01f, first.magnitude), b = Mathf.Max(.01f, second.magnitude);
            Vector3 targetVector = c.Target - shoulder;
            Vector3 direction = targetVector.sqrMagnitude > .00001f ? targetVector.normalized : p.Facing * Vector3.forward;
            float distance = Mathf.Clamp(targetVector.magnitude, Mathf.Abs(a - b) + .005f, a + b - .005f);
            // Keep the elbow on the authored limb's side after FBX handedness
            // conversion; anatomical labels alone do not define the imported X.
            float side = Mathf.Sign(c.Pivots[(int)Part.RightArm].x - c.Pivots[(int)Part.Torso].x);
            Vector3 outside = torsoRotation * (Vector3.right * (side == 0 ? 1 : side));
            Vector3 bend = Vector3.ProjectOnPlane(outside, direction);
            if (bend.sqrMagnitude < .00001f) bend = Vector3.ProjectOnPlane(Vector3.up, direction);
            bend.Normalize();
            float along = (a * a - b * b + distance * distance) / (2 * distance);
            float outwards = Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            Vector3 elbow = shoulder + direction * along + bend * outwards;
            Vector3 reachableContact = shoulder + direction * distance;
            upper = Quaternion.FromToRotation(torsoRotation * first, elbow - shoulder) * torsoRotation;
            fist = Quaternion.FromToRotation(upper * second, reachableContact - elbow) * upper;
            // Reach is clamped; neither arm nor fist is stretched to conceal a bad
            // target/stance. Contact accuracy still needs the rendered rig audit.
        }

        private static Quaternion LocalRotation(Part part, Phase p)
        {
            switch (part)
            {
                case Part.Torso: return Quaternion.Euler(p.Punch * 7 - p.Windup * 4,
                    -p.Windup * 12 + p.Punch * 14 - p.Recoil * 3, p.Breath);
                case Part.Head: return Quaternion.Euler(-p.Punch * 4, p.Windup * 6 - p.Punch * 7, 0);
                case Part.LeftArm: return Quaternion.Euler(p.Step * 10 - p.Windup * 18 - p.Punch * 22, 0, -p.Punch * 8);
                case Part.LeftFist: return Quaternion.Euler(-p.Windup * 20 - p.Punch * 32, 0, 0);
                case Part.RightArm: return Quaternion.Euler(-p.Step * 10 - p.Windup * 30 - p.Punch * 82,
                    -p.Windup * 8, p.Windup * 7 - p.Punch * 8);
                case Part.RightFist: return Quaternion.Euler(-p.Windup * 88 - p.Punch * 13 + p.Recoil * 12, 0, 0);
                case Part.LeftLeg: return Quaternion.Euler(p.Step * 11, 0, 0);
                case Part.RightLeg: return Quaternion.Euler(-p.Step * 11, 0, 0);
                case Part.LeftFoot: return Quaternion.Euler(-p.Step * 8, 0, 0);
                case Part.RightFoot: return Quaternion.Euler(p.Step * 8, 0, 0);
                default: return Quaternion.identity;
            }
        }

        private static float AssemblyDelay(int index)
        {
            switch ((Part)index)
            {
                case Part.LeftFoot: case Part.RightFoot: return 0;
                case Part.LeftLeg: case Part.RightLeg: return .12f;
                case Part.Pelvis: return .25f;
                case Part.Torso: return .38f;
                case Part.LeftArm: case Part.RightArm: return .44f;
                case Part.LeftFist: case Part.RightFist: return .50f;
                default: return .56f;
            }
        }
        private static float ExitDelay(int index) => (1 - AssemblyDelay(index)) * .12f;
        private static bool Valid(Context c)
        {
            if (!Finite(c.Age) || c.Age < 0 || !Finite(c.Life) || c.Life <= 0
                || !Finite(c.FormationTime) || !Finite(c.Height) || c.Height <= 0
                || !Finite(c.OriginGround) || !Finite(c.TargetGround) || !Finite(c.Target)
                || !Finite(c.RightFistContact) || !Finite(c.StrikeAt) || !Finite(c.HitAt) || c.Pivots == null || c.Pivots.Length != PartCount) return false;
            for (int i = 0; i < c.Pivots.Length; i++) if (!Finite(c.Pivots[i])) return false;
            return true;
        }
        private static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > .00001f ? v.normalized : Vector3.forward; }
        private static float Unit(float t, float span) => Mathf.Clamp01(t / Mathf.Max(.001f, span));
        private static float Ease(float t) => t * t * (3 - 2 * t);
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
    }
}
