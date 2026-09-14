using UnityEngine;

namespace Oheangbu.Presentation
{
    /// <summary>표현 전용 계산. 관절 길이와 게임플레이 루트는 변경하지 않는다.</summary>
    public static class PlayerVisualIK
    {
        public static Vector3 ElbowPosition(Vector3 shoulder, Vector3 target, Vector3 pole,
            float upperLength, float lowerLength, float extension)
        {
            Vector3 delta = target - shoulder;
            float distance = delta.magnitude;
            Vector3 direction = distance > 0.00001f ? delta / distance : Vector3.forward;
            float min = Mathf.Abs(upperLength - lowerLength) + 0.0001f;
            float max = Mathf.Max(min, (upperLength + lowerLength) * Mathf.Clamp(extension, 0.5f, 0.9999f));
            float d = Mathf.Clamp(distance, min, max);
            float along = (upperLength * upperLength - lowerLength * lowerLength + d * d) / (2f * d);
            float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            Vector3 bend = Vector3.ProjectOnPlane(pole - shoulder, direction);
            if (bend.sqrMagnitude < 0.000001f)
                bend = Vector3.ProjectOnPlane(Mathf.Abs(direction.y) < 0.9f ? Vector3.up : Vector3.right, direction);
            return shoulder + direction * along + bend.normalized * height;
        }

        public static Vector3 ClampReach(Vector3 shoulder, Vector3 target, float upper, float lower, float extension)
        {
            Vector3 delta = target - shoulder;
            float len = delta.magnitude;
            if (len < 0.00001f) delta = Vector3.forward;
            float d = Mathf.Clamp(len, Mathf.Abs(upper - lower) + 0.0001f, (upper + lower) * extension);
            return shoulder + delta.normalized * d;
        }

        public static void Solve(Transform upper, Transform lower, Transform hand, Vector3 target,
            Vector3 pole, Quaternion rotation, float weight, float extension = 0.999f)
        {
            if (upper == null || lower == null || hand == null || weight <= 0f) return;
            float a = Vector3.Distance(upper.position, lower.position);
            float b = Vector3.Distance(lower.position, hand.position);
            if (a < 0.0001f || b < 0.0001f) return;
            target = ClampReach(upper.position, target, a, b, extension);
            Vector3 elbow = ElbowPosition(upper.position, target, pole, a, b, extension);
            Quaternion upperGoal = Quaternion.FromToRotation(lower.position - upper.position, elbow - upper.position) * upper.rotation;
            upper.rotation = Quaternion.Slerp(upper.rotation, upperGoal, weight);
            Quaternion lowerGoal = Quaternion.FromToRotation(hand.position - lower.position, target - lower.position) * lower.rotation;
            lower.rotation = Quaternion.Slerp(lower.rotation, lowerGoal, weight);
            hand.rotation = Quaternion.Slerp(hand.rotation, rotation, weight);
        }

        // 동일한 ray 위의 깊이만 고른다. 화면 XY를 제한하거나 평활화하지 않는다.
        public static bool FindTip(Ray ray, Vector3 shoulder, float armReach, float brushLength,
            float minDepth, float maxDepth, float preferredDepth, out Vector3 tip)
        {
            float closest = Vector3.Dot(shoulder - ray.origin, ray.direction);
            Vector3 near = ray.GetPoint(closest);
            float radius = Mathf.Max(0.01f, armReach + brushLength - 0.012f);
            float sideSquared = (near - shoulder).sqrMagnitude;
            if (sideSquared > radius * radius)
            {
                tip = ray.GetPoint(Mathf.Clamp(closest, minDepth, maxDepth));
                return false;
            }
            float half = Mathf.Sqrt(Mathf.Max(0f, radius * radius - sideSquared));
            float first = Mathf.Max(minDepth, closest - half);
            float last = Mathf.Min(maxDepth, closest + half);
            if (first > last)
            {
                tip = ray.GetPoint(Mathf.Clamp(closest, minDepth, maxDepth));
                return false;
            }
            tip = ray.GetPoint(Mathf.Clamp(preferredDepth, first, last));
            return true;
        }
    }
}
