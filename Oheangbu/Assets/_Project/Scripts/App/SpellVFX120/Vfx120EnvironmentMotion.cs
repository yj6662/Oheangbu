using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // Surface-bound visual responses only. This helper never removes vegetation,
    // cuts a collider, changes pollution, writes a terrain material or emits events.
    public static class Vfx120EnvironmentMotion
    {
        public struct Context
        {
            public float Age, Life;
            public Vector3 Origin, SurfacePoint, SurfaceNormal;
            public bool HasSurface;
            public Vector3 CutStart, CutEnd;
            public bool HasCut;
            // Read-only, caller-owned coordinates in the effect's LOCAL frame.
            // A polyline uses adjacent points. Pairs allow independent vine branches.
            public Vector3[] PathPoints;
            public bool PathIsSegmentPairs;
            public float HitAt, ReleaseAt;
            public bool PreviewControlled, DemonstrationCues;

            public static Context Create() => new Context
            {
                Life = 4, SurfaceNormal = Vector3.up, HitAt = -1, ReleaseAt = -1
            };
        }

        public static bool Supports(Vfx120Profile p) => p != null &&
            (p.Glyph == "눈" || p.Glyph == "숫" || p.Glyph == "웅");
        public static int GetBodyCount(Vfx120Profile p) => !Supports(p) ? 0 : p.Glyph == "눈" ? 10 : p.Glyph == "숫" ? 1 : 7;
        public static int GetAccentCount(Vfx120Profile p) => !Supports(p) ? 0 : p.Glyph == "눈" ? 10 : p.Glyph == "숫" ? 8 : 12;
        public static Color GetAccentColor(Vfx120Profile p, int index)
        {
            if (p == null) return Color.white;
            if (p.Glyph == "숫") return Color.Lerp(p.Ink, p.Accent, .38f);
            if (p.Glyph == "웅" && index < 4) return p.Accent;
            return p.Ink;
        }

        public static bool TrySampleBody(Vfx120Profile p, in Context c, int index, int count,
            out Vfx120CueMotion.Pose pose)
        {
            pose = Hidden();
            if (!Supports(p)) return false;
            if (!Valid(c) || index < 0 || index >= count || index >= GetBodyCount(p)) return true;
            float hit = GetHitTime(c);
            if (hit < 0 || c.Age < hit) return true;
            float release = GetReleaseTime(c);
            float released = release < 0 ? 0 : Smooth(c.Age - release, p.Glyph == "숫" ? .10f : .16f);
            float alpha = (1 - released) * Life(c);
            if (alpha <= .001f) return true;
            Vector3 normal = c.SurfaceNormal.normalized;
            if (p.Glyph == "숫")
            {
                if (!c.HasCut || index != 0) return true;
                Vector3 direction = c.CutEnd - c.CutStart;
                float length = direction.magnitude;
                if (length < .01f) return true;
                float stroke = Smooth(c.Age - hit, .48f);
                direction /= length;
                pose = Make(p.BodyMesh, c.CutStart + direction * length * stroke * .5f + normal * .026f,
                    Face(direction, normal), new Vector3(.052f, .025f, length * stroke), alpha);
                return true;
            }
            if (!Segment(c, index, out Vector3 a, out Vector3 b)) return true;
            Vector3 tangent = b - a;
            float distance = tangent.magnitude;
            if (distance < .01f) return true;
            tangent /= distance;
            if (p.Glyph == "눈")
            {
                // Each supplied branch catches from its own connection. No generic
                // helix grows around an actor standing in for vegetation.
                float progress = Smooth(c.Age - hit - index * .032f, .34f);
                Vector3 point = a + tangent * distance * progress * .5f + normal * .030f;
                pose = Make(p.BodyMesh, point, Face(tangent, normal),
                    new Vector3(.085f, .050f, distance * progress), alpha);
            }
            else
            {
                // Ripple is an XY surface. Its local X follows the supplied boundary,
                // making a narrow wet edge rather than seven large unrelated rings.
                float progress = Smooth(c.Age - hit - index * .045f, .33f);
                Vector3 point = (a + b) * .5f + normal * .025f;
                Vector3 up = Vector3.Cross(normal, tangent);
                pose = Make(p.BodyMesh, point, Face(normal, up),
                    new Vector3(distance * progress, .105f, .018f), alpha * .80f);
            }
            return true;
        }

        public static bool TrySampleAccent(Vfx120Profile p, in Context c, int index, int count,
            out Vfx120CueMotion.Pose pose)
        {
            pose = Hidden();
            if (!Supports(p)) return false;
            if (!Valid(c) || index < 0 || index >= count || index >= GetAccentCount(p)) return true;
            float release = GetReleaseTime(c);
            if (release < 0 || c.Age < release) return true;
            float t = c.Age - release;
            float alpha = Life(c);
            Vector3 normal = c.SurfaceNormal.normalized;
            Basis(normal, out Vector3 right, out Vector3 up);
            Vector3 point, dimensions;
            Quaternion rotation;
            if (p.Glyph == "눈")
            {
                if (!Segment(c, index, out Vector3 a, out Vector3 b)) return true;
                float side = (index & 1) == 0 ? -1 : 1;
                point = (a + b) * .5f + right * side * t * (.25f + index * .018f)
                    - up * t * t * .14f + normal * (.045f + t * .10f);
                rotation = Quaternion.Euler(index * 39 + t * 55, index * 71, t * 34);
                dimensions = new Vector3(.095f, .055f, .14f) * (1 - Smooth(t, 1.05f));
                alpha *= 1 - Smooth(t - .26f, .72f);
            }
            else if (p.Glyph == "숫")
            {
                if (!c.HasCut) return true;
                float u = (index + .5f) / GetAccentCount(p);
                float side = (index & 1) == 0 ? -1 : 1;
                point = Vector3.Lerp(c.CutStart, c.CutEnd, u) + normal * .035f
                    + right * side * t * .18f + Vector3.down * t * t * .85f;
                rotation = Quaternion.Euler(index * 27 + t * 40, index * 41, index * 13);
                dimensions = new Vector3(.085f, .055f, .11f) * (1 - Smooth(t - .20f, .72f));
                alpha *= 1 - Smooth(t - .25f, .68f);
            }
            else
            {
                int segments = SegmentCount(c);
                if (segments == 0 || !Segment(c, index % segments, out Vector3 a, out Vector3 b)) return true;
                Vector3 start = (a + b) * .5f;
                float phase = Mathf.Clamp01((t - index * .018f) / .80f);
                Vector3 inward = c.SurfacePoint - start;
                float turn = phase * Mathf.PI * 1.35f + index * .6f;
                Vector3 curl = right * Mathf.Sin(turn) + up * Mathf.Cos(turn);
                point = Vector3.Lerp(start, c.SurfacePoint, phase) + normal * (.035f + Mathf.Sin(phase * Mathf.PI) * .14f)
                    + curl * Mathf.Sin(phase * Mathf.PI) * .13f;
                rotation = Face(inward + normal * .20f + curl * .08f, normal);
                dimensions = new Vector3(.052f, .022f, .24f) * (1 - phase * .82f);
                alpha *= Mathf.Sin(phase * Mathf.PI);
            }
            pose = Make(p.AccentMesh, point, rotation, dimensions, alpha);
            return true;
        }

        // Public for the review fixture: one clock definition, no duplicated timings.
        // A cast or ordinary Hit does NOT imply environment removal/cleansing success.
        public static float GetHitTime(in Context c)
        {
            if (Finite(c.HitAt) && c.HitAt >= 0) return c.HitAt;
            return c.PreviewControlled && c.DemonstrationCues && Finite(c.Life) ? c.Life * .18f : -1;
        }
        public static float GetReleaseTime(in Context c)
        {
            if (Finite(c.ReleaseAt) && c.ReleaseAt >= 0) return c.ReleaseAt;
            return c.PreviewControlled && c.DemonstrationCues && Finite(c.Life) ? c.Life * .52f : -1;
        }
        public static void Basis(Vector3 normal, out Vector3 right, out Vector3 up)
        {
            normal = normal.sqrMagnitude > .000001f ? normal.normalized : Vector3.up;
            right = Vector3.Cross(Vector3.up, normal);
            if (right.sqrMagnitude < .000001f) right = Vector3.right;
            right.Normalize(); up = Vector3.Cross(normal, right).normalized;
        }
        private static int SegmentCount(in Context c) => c.PathPoints == null ? 0 : c.PathIsSegmentPairs
            ? c.PathPoints.Length / 2 : Mathf.Max(0, c.PathPoints.Length - 1);
        private static bool Segment(in Context c, int index, out Vector3 a, out Vector3 b)
        {
            a = b = c.SurfacePoint;
            if (index < 0 || index >= SegmentCount(c)) return false;
            int start = c.PathIsSegmentPairs ? index * 2 : index;
            a = c.PathPoints[start]; b = c.PathPoints[start + 1];
            return Finite(a) && Finite(b);
        }
        private static Vfx120CueMotion.Pose Make(Mesh mesh, Vector3 point, Quaternion rotation,
            Vector3 dimensions, float alpha)
        {
            Vector3 bounds = mesh != null ? mesh.bounds.size : Vector3.one;
            Vector3 scale = new Vector3(Mathf.Max(0, dimensions.x) / Mathf.Max(.001f, bounds.x),
                Mathf.Max(0, dimensions.y) / Mathf.Max(.001f, bounds.y), Mathf.Max(0, dimensions.z) / Mathf.Max(.001f, bounds.z));
            return new Vfx120CueMotion.Pose { Position = point, Rotation = rotation, Scale = scale,
                Alpha = Mathf.Clamp01(alpha), Visible = alpha > .001f && scale.sqrMagnitude > .000001f };
        }
        private static Vfx120CueMotion.Pose Hidden() => new Vfx120CueMotion.Pose { Rotation = Quaternion.identity };
        private static Quaternion Face(Vector3 axis, Vector3 up)
        {
            if (axis.sqrMagnitude < .000001f) axis = Vector3.forward;
            axis.Normalize();
            if (up.sqrMagnitude < .000001f || Mathf.Abs(Vector3.Dot(axis, up.normalized)) > .99f)
                up = Mathf.Abs(axis.y) > .98f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(axis, up);
        }
        private static float Life(in Context c) => 1 - Smooth(c.Age - Mathf.Max(0, c.Life - .35f), .35f);
        private static float Smooth(float age, float span) { float t = Mathf.Clamp01(age / Mathf.Max(.001f, span)); return t * t * (3 - 2 * t); }
        private static bool Valid(in Context c) => c.HasSurface && Finite(c.Age) && Finite(c.Life) && c.Life > 0
            && Finite(c.SurfacePoint) && Finite(c.SurfaceNormal) && c.SurfaceNormal.sqrMagnitude > .000001f
            && (!c.HasCut || (Finite(c.CutStart) && Finite(c.CutEnd)));
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
    }
}
