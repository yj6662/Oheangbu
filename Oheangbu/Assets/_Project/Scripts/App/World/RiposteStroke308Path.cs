// PURE308
using UnityEngine;

namespace Oheangbu.App.World
{
    // D308-148 (앞잡 = 일필휘지 한 획, step 5): the geometry of the one stroke. Plain numbers and Vector3 only - no Unity objects, no
    // Quaternion, no AnimationCurve, no Time - so it runs in the pure runner (Tools/SpellVFX120/Pure308, Cases_RiposteStroke308).
    //
    // The stroke lies on one tilted WORLD plane (origin = the target's torso point at the fire, normal = the caster->target line turned
    // by PlaneYawDegrees about the vertical, local y = world up, local x = Cross(up, normal)). The plane is fixed at the fire: a camera
    // that moves does not turn it (no billboard). Local z carries the small depth shift of a target that moved after the fire.
    //
    // Centre line, in order of the brush (u = arc length / total length, 0 = the wet start, 1 = the dry tail):
    //   lead  : a quadratic curve from a start far up-left of the torso, arriving on the slash axis (tangent = the axis),
    //   torso : 2k+1 straight samples centred EXACTLY on the torso point (entry .. mid .. exit), never smoothed - this is the pierce,
    //   tail  : a quadratic curve leaving along the axis, ending where the eye's ray through the brush tip meets the plane (the
    //           "tail dies near the brush tip" of the first-person board), or a default hook when that projection is unusable.
    // The samples are fed to RibbonMeshBuilder with NO smoothing passes and no caps, so the final mesh centre line IS Pos[] and the
    // pierce can be proved on the numbers (PathPiercing). Width and ink density are per sample (the builder writes vertex alpha = ink).
    public struct RiposteStroke308Shape
    {
        public int Samples;
        public float PlaneYawDegrees, SlashDegrees;
        public float LeadIn, LeadBulge, TorsoHalf;
        public float TailLength, TailMaxReach, TailBulge, TailFloorDrop;
        public float HeadWidth, HeadPeak, StartWidthScale, TorsoWidth, TailWidth, TailInk;

        public static RiposteStroke308Shape Default => new RiposteStroke308Shape
        {
            Samples = 48, PlaneYawDegrees = 35f, SlashDegrees = 38f,
            LeadIn = 3.4f, LeadBulge = .28f, TorsoHalf = .35f,
            TailLength = 1.8f, TailMaxReach = 7f, TailBulge = .15f, TailFloorDrop = 1.0f,
            HeadWidth = 1.5f, HeadPeak = .14f, StartWidthScale = .45f, TorsoWidth = .28f, TailWidth = .05f, TailInk = .35f,
        };
    }

    /// <summary>What the world says at the fire. Torso = the target's torso point; TorsoRadius = its body radius (the torso half length
    /// of the stroke is at least this); Eye = the camera (or the film camera) and BrushTip = the brush tip of the active view.</summary>
    public struct RiposteStroke308Aim
    {
        public Vector3 Caster, Eye, Torso, BrushTip;
        public float TorsoRadius;
        public bool HasBrushTip;
    }

    public sealed class RiposteStroke308Path
    {
        public const int MaxSamples = 64;
        public const int MinSamples = 24;
        const int TorsoK = 3;   // 2k + 1 = 7 straight samples across the torso

        // plane (world)
        public Vector3 Origin, H, V, N;
        // samples, in the plane's local frame
        public int Count { get; private set; }
        public readonly Vector3[] Pos = new Vector3[MaxSamples];
        public readonly float[] Width = new float[MaxSamples];
        public readonly float[] Ink = new float[MaxSamples];
        public readonly float[] Arc = new float[MaxSamples];
        public float Length { get; private set; }
        public int LeadCount { get; private set; }
        public int EntryIndex { get; private set; }
        public int MidIndex { get; private set; }
        public int ExitIndex { get; private set; }
        /// <summary>The tail ended on the brush tip's projection (false = the default hook).</summary>
        public bool TailOnBrush { get; private set; }
        public float EntryU => U(EntryIndex);
        public float ImpactU => U(MidIndex);
        public float ExitU => U(ExitIndex);

        readonly Vector3[] _pos0 = new Vector3[MaxSamples];
        readonly float[] _weight = new float[MaxSamples];
        readonly bool[] _frozen = new bool[MaxSamples];

        public float U(int i) => Length > 1e-5f ? Arc[Mathf.Clamp(i, 0, Mathf.Max(0, Count - 1))] / Length : 0f;

        public Vector3 ToWorld(Vector3 local) => Origin + H * local.x + V * local.y + N * local.z;
        public Vector3 SampleWorld(int i) => ToWorld(Pos[i]);
        public Vector3 MidWorld => SampleWorld(MidIndex);

        /// <summary>Lays the stroke out for this fire. Always succeeds (degenerate geometry falls back to the world forward).</summary>
        public void Build(in RiposteStroke308Shape shape, in RiposteStroke308Aim aim)
        {
            int n = Mathf.Clamp(shape.Samples, MinSamples, MaxSamples);
            Vector3 f = aim.Torso - aim.Caster; f.y = 0f;
            if (f.sqrMagnitude < 1e-6f) { f = aim.Torso - aim.Eye; f.y = 0f; }
            if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
            f.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, f);
            float yaw = shape.PlaneYawDegrees * Mathf.Deg2Rad;
            N = (f * Mathf.Cos(yaw) - right * Mathf.Sin(yaw)).normalized;
            V = Vector3.up;
            H = Vector3.Cross(V, N);
            Origin = aim.Torso;

            float a = shape.SlashDegrees * Mathf.Deg2Rad;
            var d = new Vector3(Mathf.Cos(a), -Mathf.Sin(a), 0f);     // the slash: right and down
            var p = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f);      // its normal: up and right
            float half = Mathf.Max(.05f, Mathf.Max(shape.TorsoHalf, aim.TorsoRadius));
            float leadIn = Mathf.Max(half + .5f, shape.LeadIn);
            Vector3 entry = -d * half, exit = d * half;

            // the tail's end: the eye's ray through the brush tip, met with the plane
            Vector3 end = exit + d * Mathf.Max(.5f, shape.TailLength) - p * (Mathf.Max(0f, shape.TailBulge) * Mathf.Max(.5f, shape.TailLength));
            TailOnBrush = false;
            if (aim.HasBrushTip && ProjectToPlane(aim.Eye, aim.BrushTip, out Vector3 onPlane))
            {
                Vector3 rel = onPlane - exit; rel.z = 0f;
                // a brush near the eye projects far below the floor on a plane several metres away: the tail stops at floor level
                // (TailFloorDrop below the torso mark) on the same line, so it never dives under the ground
                float floor = -Mathf.Max(.3f, shape.TailFloorDrop);
                if (exit.y + rel.y < floor && rel.y < -1e-3f) rel *= (floor - exit.y) / rel.y;
                float along = Vector3.Dot(rel, d);
                if (along > .6f && rel.magnitude > .8f)
                {
                    float reach = Mathf.Max(1f, shape.TailMaxReach);
                    if (rel.magnitude > reach) rel = rel.normalized * reach;
                    end = exit + rel; TailOnBrush = true;
                }
            }

            int tail = Mathf.Max(6, (int)(n * .42f));
            int lead = n - (2 * TorsoK + 1) - tail;
            if (lead < 8) { lead = 8; tail = n - (2 * TorsoK + 1) - lead; }
            Count = n; LeadCount = lead;
            EntryIndex = lead; MidIndex = lead + TorsoK; ExitIndex = lead + 2 * TorsoK;

            // lead: start up-left (bulged off the axis), control on the axis so the tangent at the entry is the slash direction
            Vector3 s0 = -d * leadIn + p * (Mathf.Max(0f, shape.LeadBulge) * leadIn);
            Vector3 c0 = entry - d * ((leadIn - half) * .55f);
            for (int i = 0; i < lead; i++)
            {
                float q = i / (float)lead;
                Pos[i] = Bezier(s0, c0, entry, q);
                float swell = Mathf.SmoothStep(0f, 1f, q / Mathf.Max(.01f, shape.HeadPeak));
                float ease = Mathf.SmoothStep(0f, 1f, (q - shape.HeadPeak) / Mathf.Max(.01f, 1f - shape.HeadPeak));
                float w = Mathf.Lerp(shape.HeadWidth * shape.StartWidthScale, shape.HeadWidth, swell);
                Width[i] = Mathf.Lerp(w, shape.TorsoWidth * 1.15f, ease);
                Ink[i] = 1f;
                _weight[i] = Mathf.SmoothStep(0f, 1f, q);
            }
            // torso: straight, centred on the torso point
            for (int j = -TorsoK; j <= TorsoK; j++)
            {
                int i = lead + TorsoK + j;
                Pos[i] = j == 0 ? Vector3.zero : d * (half * j / TorsoK);
                Width[i] = shape.TorsoWidth; Ink[i] = 1f; _weight[i] = 1f;
            }
            // tail: leaves along the axis, dries out
            Vector3 c1 = exit + d * (Vector3.Dot(end - exit, d) * .5f);
            for (int k = 0; k < tail; k++)
            {
                int i = ExitIndex + 1 + k;
                float q = (k + 1) / (float)tail;
                Pos[i] = Bezier(exit, c1, end, q);
                Width[i] = Mathf.Lerp(shape.TorsoWidth, shape.TailWidth, Mathf.SmoothStep(0f, 1f, Mathf.Pow(q, .8f)));
                Ink[i] = Mathf.Lerp(1f, Mathf.Clamp01(shape.TailInk), Mathf.Pow(q, 1.2f));
                _weight[i] = 1f - Mathf.SmoothStep(0f, 1f, q);
            }
            for (int i = 0; i < n; i++) { _pos0[i] = Pos[i]; _frozen[i] = false; }
            Measure();
        }

        /// <summary>The target moved: the samples the head has not reached yet follow it (weighted: 0 at the wet start, 1 across the torso,
        /// 0 again at the brush end); the ones it has reached stay where they were written in the world.</summary>
        public void Retarget(Vector3 torsoWorld, float revealedU)
        {
            Vector3 d = torsoWorld - Origin;
            var delta = new Vector3(Vector3.Dot(d, H), Vector3.Dot(d, V), Vector3.Dot(d, N));
            for (int i = 0; i < Count; i++)
            {
                if (_frozen[i]) continue;
                if (U(i) <= revealedU) { _frozen[i] = true; continue; }
                Pos[i] = _pos0[i] + delta * _weight[i];
            }
            Measure();
        }

        /// <summary>From the pierce on nothing moves any more.</summary>
        public void Freeze() { for (int i = 0; i < Count; i++) _frozen[i] = true; }

        public bool IsFrozen(int i) => i >= 0 && i < Count && _frozen[i];

        /// <summary>Local position at arc fraction u (linear between samples).</summary>
        public Vector3 LocalAt(float u)
        {
            if (Count < 2) return Count == 1 ? Pos[0] : Vector3.zero;
            float s = Mathf.Clamp01(u) * Length;
            for (int i = 1; i < Count; i++)
            {
                if (Arc[i] >= s || i == Count - 1)
                {
                    float seg = Mathf.Max(1e-6f, Arc[i] - Arc[i - 1]);
                    return Vector3.Lerp(Pos[i - 1], Pos[i], Mathf.Clamp01((s - Arc[i - 1]) / seg));
                }
            }
            return Pos[Count - 1];
        }

        public Vector3 WorldAt(float u) => ToWorld(LocalAt(u));

        /// <summary>Stroke width at arc fraction u (linear between samples).</summary>
        public float WidthAt(float u)
        {
            if (Count < 2) return Count == 1 ? Width[0] : 0f;
            float s = Mathf.Clamp01(u) * Length;
            for (int i = 1; i < Count; i++)
            {
                if (Arc[i] >= s || i == Count - 1)
                {
                    float seg = Mathf.Max(1e-6f, Arc[i] - Arc[i - 1]);
                    return Mathf.Lerp(Width[i - 1], Width[i], Mathf.Clamp01((s - Arc[i - 1]) / seg));
                }
            }
            return Width[Count - 1];
        }

        /// <summary>Local tangent at arc fraction u (unit, zero for a degenerate path).</summary>
        public Vector3 TangentAt(float u)
        {
            if (Count < 2) return Vector3.zero;
            float s = Mathf.Clamp01(u) * Length;
            for (int i = 1; i < Count; i++)
                if (Arc[i] >= s || i == Count - 1) return (Pos[i] - Pos[i - 1]).normalized;
            return Vector3.zero;
        }

        /// <summary>Smallest distance from a world point to the centre line (the polyline of the samples).</summary>
        public float DistanceToCentreline(Vector3 world)
        {
            float best = float.MaxValue;
            for (int i = 1; i < Count; i++)
            {
                Vector3 a = SampleWorld(i - 1), b = SampleWorld(i), ab = b - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 1e-10f ? Mathf.Clamp01(Vector3.Dot(world - a, ab) / len2) : 0f;
                best = Mathf.Min(best, Vector3.Distance(world, a + ab * t));
            }
            return best;
        }

        void Measure()
        {
            Arc[0] = 0f;
            for (int i = 1; i < Count; i++) Arc[i] = Arc[i - 1] + Vector3.Distance(Pos[i], Pos[i - 1]);
            Length = Count > 0 ? Arc[Count - 1] : 0f;
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float m = 1f - t;
            return a * (m * m) + b * (2f * m * t) + c * (t * t);
        }

        // the eye's ray through the brush tip, met with the plane; false when the plane is edge-on, behind, or nearer than the brush
        bool ProjectToPlane(Vector3 eye, Vector3 tip, out Vector3 local)
        {
            local = Vector3.zero;
            Vector3 ray = tip - eye; float dist = ray.magnitude;
            if (dist < 1e-3f) return false;
            ray /= dist;
            float denom = Vector3.Dot(ray, N);
            if (Mathf.Abs(denom) < .08f) return false;
            float t = Vector3.Dot(Origin - eye, N) / denom;
            if (t <= dist) return false;
            Vector3 hit = eye + ray * t - Origin;
            local = new Vector3(Vector3.Dot(hit, H), Vector3.Dot(hit, V), 0f);
            return true;
        }
    }
}
