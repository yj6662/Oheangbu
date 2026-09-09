using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    /// <summary>
    /// Stateless, presentation-only choreography for event-dependent spell silhouettes.
    /// Every position is in the owning effect's local coordinates. This class never
    /// samples Time, transforms, physics, targets, health, or any spell/gameplay state.
    /// Cues and target points must be supplied by the adapter or explicitly by a review.
    /// An authored asset using this helper is not evidence of a gameplay connection.
    /// </summary>
    public static class Vfx120CueMotion
    {
        public enum PartRole { Body, Accent }

        public struct Context
        {
            public string Glyph;
            public float Age;
            public float Duration;
            public float ImpactTime;
            public Vector3 Origin;
            public Vector3 Target;
            public Vector3 Secondary;
            public bool HasSecondary;
            // Optional, read-only to this function. No targets are found or selected here.
            public Vector3[] SecondaryPoints;
            public Vector3 BaseScale;
            public float GroundY;
            // Relative seconds; -1 means the cue has never been issued.
            public float HitAt;
            public float TargetDefeatedAt;
            public float ReleaseAt;

            public static Context Create(string glyph)
            {
                return new Context
                {
                    Glyph = glyph, Duration = 3, ImpactTime = .6f,
                    Origin = Vector3.zero, Target = Vector3.forward * 4,
                    BaseScale = Vector3.one, GroundY = -1,
                    HitAt = -1, TargetDefeatedAt = -1, ReleaseAt = -1
                };
            }
        }

        public struct Pose
        {
            public Vector3 Position;
            public Quaternion Rotation;
            // Absolute scale in the effect frame, not a multiplier of a previous pose.
            public Vector3 Scale;
            public float Alpha;
            public bool Visible;
        }

        public static int GetRequiredAccentCount(string glyph)
        {
            switch (glyph)
            {
                case "간": return 5;
                case "감": return 6;
                case "남": return 7;
                case "맘": return 4;
                case "삼": return 4;
                case "암": return 6;
                case "녹": return 8;
                case "안": return 6;
                case "검": return 12;
                default: return 0;
            }
        }

        public static bool TrySample(in Context context, PartRole role, int index,
            int count, out Pose pose)
        {
            pose = Hidden(context.Target);
            if (index < 0 || index >= count || count < 1 || !Finite(context.Age) ||
                !Finite(context.Origin) || !Finite(context.Target) ||
                !Finite(context.BaseScale) || !Finite(context.GroundY)) return false;

            switch (context.Glyph)
            {
                case "간": pose = TransferSeed(context, role, index, count); return true;
                case "감": case "남": case "맘": case "삼": case "암":
                    pose = AttachedSeal(context, role, index, count); return true;
                case "녹": pose = HitHealingLotus(context, role, index, count); return true;
                case "안": pose = SplitDroplet(context, role, index, count); return true;
                case "검": pose = PlantedTree(context, role, index, count); return true;
                default: return false;
            }
        }

        // A seed remains visibly attached. Only a defeat or explicit seed-expiry/release
        // cue frees it. Missing secondary data never invents a new target or a second hit.
        private static Pose TransferSeed(in Context c, PartRole role, int index, int count)
        {
            Vector3 axis = Incoming(c);
            Vector3 surface = FrontSurface(c);
            float arrival = Mathf.Max(.05f, c.ImpactTime);
            float transferAt = FirstCueAfter(arrival, c.TargetDefeatedAt, c.ReleaseAt);
            float travelT = Mathf.Clamp01(c.Age / arrival);
            float appear = Smooth(0, .12f, c.Age);
            float fade = LifeFade(c);

            if (c.Age < arrival)
            {
                Vector3 center = Vector3.Lerp(c.Origin, surface, travelT);
                if (role == PartRole.Body)
                    return index == 0 ? Make(center, Face(axis), c.BaseScale, appear * fade) : Hidden(center);
                float a = index * 2.399963f;
                Vector3 offset = PlaneOffset(axis, Mathf.Cos(a) * .065f, Mathf.Sin(a) * .065f);
                return Make(center - axis * (.08f + index * .025f) + offset,
                    Face(axis + offset), Vector3.one * (.10f + (index == 0 ? .055f : 0)), appear * .85f * fade);
            }

            if (transferAt < 0 || c.Age < transferAt)
            {
                float open = Smooth(arrival, arrival + .24f, c.Age);
                if (role == PartRole.Body)
                    return index == 0 ? Make(surface + axis * .045f, Face(axis), c.BaseScale * .76f, fade) : Hidden(surface);
                float a = index * Mathf.PI * 2 / Mathf.Max(1, count);
                Vector3 petal = PlaneOffset(axis, Mathf.Cos(a), Mathf.Sin(a));
                float breath = 1 + Mathf.Sin((c.Age - arrival) * 3.1f + index * .2f) * .025f;
                return Make(surface - axis * .08f + petal * (.08f * open),
                    Face(Vector3.up * .4f + petal), new Vector3(.17f, .16f, .21f) * open * breath, fade);
            }

            float age = c.Age - transferAt;
            if (!c.HasSecondary || !Finite(c.Secondary))
            {
                // Visual release only. No alternate target and no invented impact.
                Vector3 p = surface + Vector3.up * (.20f * age) - axis * (.12f * age);
                float alpha = (1 - Smooth(.18f, .72f, age)) * fade;
                if (role == PartRole.Body) return index == 0 ? Make(p, Face(axis), c.BaseScale * .32f, alpha) : Hidden(p);
                return Make(p + PlaneOffset(axis, Mathf.Cos(index * 2.4f), Mathf.Sin(index * 2.4f)) * age * .07f,
                    Face(Vector3.up), Vector3.one * .13f, alpha);
            }

            Vector3 end = c.Secondary;
            float distance = Vector3.Distance(surface, end);
            float flight = Mathf.Clamp(.4f + distance * .08f, .55f, 1.1f);
            float t = Mathf.Clamp01(age / flight);
            Vector3 control = (surface + end) * .5f + Vector3.up * Mathf.Clamp(distance * .18f, .28f, .7f);
            Vector3 centerFlight = Quadratic(surface, control, end, t);
            Vector3 tangent = QuadraticTangent(surface, control, end, t);
            float arrivalFade = 1 - Smooth(flight + .18f, flight + .55f, age);
            if (role == PartRole.Body)
                return index == 0 ? Make(centerFlight, Face(tangent), c.BaseScale * .35f, fade * arrivalFade) : Hidden(centerFlight);
            float spread = Mathf.Sin(t * Mathf.PI) * .06f;
            Vector3 swirl = PlaneOffset(tangent, Mathf.Cos(index * 2.4f + t * 4), Mathf.Sin(index * 2.4f + t * 4));
            return Make(centerFlight - tangent.normalized * (index * .025f) + swirl * spread,
                Face(tangent + swirl * .12f), new Vector3(.12f, .10f, .16f), fade * arrivalFade);
        }

        // Five attached seals share an event contract but have different closure shapes:
        // vine knot, hot seed petals, four clay corners, four metal nails, and frost cup.
        // ImpactTime is first attachment. A later HitAt is the activating C4 attack.
        private static Pose AttachedSeal(in Context c, PartRole role, int index, int count)
        {
            Vector3 incoming = Incoming(c);
            Vector3 surface = FrontSurface(c);
            float arrival = Mathf.Max(.05f, c.ImpactTime);
            float appear = Smooth(0, .13f, c.Age);
            Vector3 center = Vector3.Lerp(c.Origin, surface, Mathf.Clamp01(c.Age / arrival));
            float closed = Smooth(arrival - .04f, arrival + .24f, c.Age);
            float burstAt = FirstCueAfter(arrival + .025f, c.HitAt, c.ReleaseAt);
            bool bursting = burstAt >= 0 && c.Age >= burstAt;
            float burstAge = bursting ? c.Age - burstAt : 0;
            float a = index * Mathf.PI * 2 / Mathf.Max(1, count);
            Vector3 radial = PlaneOffset(incoming, Mathf.Cos(a), Mathf.Sin(a));
            float radius = Mathf.Lerp(.045f, .12f, closed);
            Vector3 size = c.BaseScale;
            Quaternion facing = PatchFacing(-incoming);

            if (c.Glyph == "감")
            {
                radius = role == PartRole.Body ? .11f : .16f;
                // Two opposite turns close a knot instead of a spinning rune ring.
                a += (index % 2 == 0 ? 1 : -1) * (1 - closed) * 1.25f;
                radial = PlaneOffset(incoming, Mathf.Cos(a), Mathf.Sin(a));
                facing = Face(radial * .48f + Vector3.up * .85f);
                size = role == PartRole.Body ? Vector3.Scale(c.BaseScale, new Vector3(.64f, .60f, .72f)) : new Vector3(.27f, .23f, .37f);
            }
            else if (c.Glyph == "남")
            {
                radius = role == PartRole.Body ? .085f : .14f;
                float heat = 1 + Mathf.Sin((c.Age - arrival) * 4.7f) * .035f * closed;
                size = (role == PartRole.Body ? c.BaseScale * .64f : new Vector3(.25f, .23f, .31f)) * heat;
                facing = Face(Vector3.up * .55f - radial * .55f);
            }
            else if (c.Glyph == "맘")
            {
                // Fixed, readable square corners. The clay mark does not orbit.
                Vector2 corner = SquareCorner(index);
                radial = PlaneOffset(incoming, corner.x, corner.y).normalized;
                radius = .125f;
                size = role == PartRole.Body ? Vector3.Scale(c.BaseScale, new Vector3(.63f, .62f, .65f)) : Vector3.one * .12f;
                facing = role == PartRole.Body ? PatchFacing(-incoming) : Face(radial);
            }
            else if (c.Glyph == "삼")
            {
                a += Mathf.PI * .25f;
                radial = PlaneOffset(incoming, Mathf.Cos(a), Mathf.Sin(a));
                radius = .13f;
                facing = Face(-radial + incoming * .30f);
                size = role == PartRole.Body ? Vector3.Scale(c.BaseScale, new Vector3(.65f, .65f, .80f)) : new Vector3(.19f, .17f, .34f);
            }
            else // 암: one frost cup surrounded by distinct outward-growing icicles.
            {
                radius = role == PartRole.Body ? 0 : .17f;
                facing = role == PartRole.Body ? Face(-incoming) : Face(radial + Vector3.down * .35f);
                size = role == PartRole.Body ? c.BaseScale * .56f : new Vector3(.18f, .15f, .33f);
                if (role == PartRole.Body && index > 0) return Hidden(center);
            }

            Vector3 p = center + radial * radius * closed - incoming * .015f;
            float alpha = appear * LifeFade(c);
            if (bursting)
            {
                float travel = burstAge * (role == PartRole.Body ? .95f : 1.8f);
                p = surface + radial * (radius + travel) - incoming * (burstAge * .28f) +
                    Vector3.down * (burstAge * burstAge * 1.35f);
                // Scale remains rigid during breakup; no expanding, rubbery brush parts.
                facing = Quaternion.AngleAxis(burstAge * (index % 2 == 0 ? 155 : -130), radial) * facing;
                alpha *= 1 - Smooth(.10f, role == PartRole.Body ? .65f : .82f, burstAge);
            }
            else if (role == PartRole.Accent)
            {
                // The supporting details become readable only after a real attachment.
                alpha *= Smooth(arrival, arrival + .22f, c.Age);
                if (c.Glyph == "암") size.z *= Mathf.Lerp(.18f, 1, Smooth(arrival, arrival + .65f, c.Age));
            }
            return Make(p, facing, size, alpha);
        }

        // No hit cue means no healing lotus, even after the nominal flight has ended.
        // Body mesh: one Lotus. Accent mesh: Flame, at least eight visual flame tongues.
        private static Pose HitHealingLotus(in Context c, PartRole role, int index, int count)
        {
            bool hit = Issued(c.HitAt, c.Age);
            float hitAge = hit ? c.Age - c.HitAt : -1;
            Vector3 ground = new Vector3(c.Target.x, c.GroundY, c.Target.z);
            float fade = LifeFade(c);

            if (role == PartRole.Body)
            {
                if (!hit || index != 0) return Hidden(ground);
                float open = Smooth(.09f, .62f, hitAge);
                Vector3 scale = c.BaseScale * Mathf.Lerp(.18f, 1, open);
                // Lotus depth is along local Z. Its cup bottom stays on the supplied floor.
                Vector3 p = ground + Vector3.up * (.025f + scale.z * .245f);
                float breath = 1 + Mathf.Sin(hitAge * 1.8f) * .015f;
                scale.x *= breath; scale.y *= breath;
                return Make(p, Face(Vector3.up), scale, open * fade);
            }

            float u = (index + .5f) / count;
            float a = Mathf.Lerp(-1.02f, 1.02f, u);
            Vector3 dir = Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * Incoming(c);
            float flight = Mathf.Max(.08f, c.ImpactTime);
            if (!hit)
            {
                float t = Mathf.Clamp01(c.Age / flight);
                float vanish = 1 - Smooth(flight + .03f, flight + .34f, c.Age);
                Vector3 p = Vector3.Lerp(c.Origin, c.Target, t) + dir * (.42f * t);
                return Make(p, Face(dir), new Vector3(.55f, .40f, .78f), Smooth(0, .14f, c.Age) * vanish * fade);
            }

            // The attack flame peels outward and extinguishes before the calm lotus holds.
            Vector3 radial = new Vector3(Mathf.Cos(u * Mathf.PI * 2), 0, Mathf.Sin(u * Mathf.PI * 2));
            Vector3 released = ground + radial * (.25f + hitAge * .95f) + Vector3.up * (.14f + hitAge * .38f);
            float alpha = (1 - Smooth(.10f, .56f, hitAge)) * fade;
            return Make(released, Face(radial + Vector3.up * .7f), new Vector3(.48f, .38f, .73f), alpha);
        }

        // Body/Accent both require closed droplet geometry (Ember is the current source),
        // not a Ripple ring. All supplied child targets are read-only presentation inputs.
        private static Pose SplitDroplet(in Context c, PartRole role, int index, int count)
        {
            float flight = Mathf.Max(.08f, c.ImpactTime);
            bool split = Issued(c.HitAt, c.Age);
            float t = Mathf.Clamp01(c.Age / flight);
            Vector3 axis = Incoming(c);
            Vector3 center = Vector3.Lerp(c.Origin, c.Target, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * .16f);
            float fade = LifeFade(c);

            if (!split)
            {
                if (role == PartRole.Accent) return Hidden(center);
                float noHitFade = 1 - Smooth(flight + .10f, flight + .48f, c.Age);
                return index == 0 ? Make(center, Face(axis), c.BaseScale, Smooth(0, .15f, c.Age) * noHitFade * fade) : Hidden(center);
            }

            float age = c.Age - c.HitAt;
            if (role == PartRole.Body)
            {
                if (index > 0) return Hidden(c.Target);
                float spread = 1 + Smooth(0, .13f, age) * .35f;
                return Make(c.Target, Face(axis), c.BaseScale * spread,
                    (1 - Smooth(.03f, .23f, age)) * fade);
            }

            float u = (index + .5f) / count;
            float angle = Mathf.Lerp(-1.30f, 1.30f, u);
            Vector3 outgoing = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.up) * axis;
            bool hasGoal = c.SecondaryPoints != null && c.SecondaryPoints.Length > index && Finite(c.SecondaryPoints[index]);
            Vector3 end;
            if (hasGoal) end = c.SecondaryPoints[index];
            else if (c.HasSecondary && Finite(c.Secondary))
            {
                // One supplied secondary can only be one target; do not invent neighbors.
                end = c.Secondary;
                hasGoal = true;
            }
            else end = c.Target + outgoing * 1.25f;
            float childFlight = Mathf.Clamp(Vector3.Distance(c.Target, end) * .14f, .50f, 1.05f);
            float f = Mathf.Clamp01(age / childFlight);
            Vector3 control = c.Target + outgoing * .65f + Vector3.up * (.13f + .18f * u);
            Vector3 p = Quadratic(c.Target, control, end, f);
            Vector3 tangent = QuadraticTangent(c.Target, control, end, f);
            float tail = hasGoal ? .18f : 0;
            float alpha = Smooth(0, .07f, age) * (1 - Smooth(childFlight - .1f + tail, childFlight + .25f + tail, age)) * fade;
            return Make(p, Face(tangent), new Vector3(.43f, .40f, .37f) * (1 + (index % 3) * .06f), alpha);
        }

        // One upright Tree mesh with authored Y height=1. Unlike a heal aura, the tree
        // stays on the ground; only small leaves move. ReleaseAt means break/despawn here.
        private static Pose PlantedTree(in Context c, PartRole role, int index, int count)
        {
            float plantAt = Mathf.Max(.04f, c.ImpactTime);
            float age = c.Age - plantAt;
            Vector3 ground = new Vector3(c.Target.x, c.GroundY, c.Target.z);
            float grow = Smooth(0, .62f, age);
            float fade = LifeFade(c);
            bool broken = Issued(c.ReleaseAt, c.Age);
            float breakAge = broken ? c.Age - c.ReleaseAt : -1;
            float collapse = broken ? 1 - Smooth(.05f, .73f, breakAge) : 1;

            if (role == PartRole.Body)
            {
                if (index > 0 || age < 0) return Hidden(ground);
                Vector3 scale = c.BaseScale;
                scale.x *= Mathf.Lerp(.26f, 1, grow);
                scale.z *= Mathf.Lerp(.26f, 1, grow);
                scale.y *= Mathf.Max(.025f, grow) * Mathf.Max(.03f, collapse);
                // Keep the mesh's lower bound anchored while growing into a full crown.
                Vector3 p = ground + Vector3.up * (scale.y * .5f + .015f);
                return Make(p, Quaternion.identity, scale, grow * collapse * fade);
            }

            float u = (index + .5f) / count;
            float a = index * 2.399963f;
            if (age < .15f)
            {
                float germinate = Smooth(plantAt - .25f, plantAt + .12f, c.Age);
                Vector3 root = ground + new Vector3(Mathf.Cos(a), .035f, Mathf.Sin(a)) * (.12f + germinate * .24f);
                return Make(root, Face(new Vector3(Mathf.Cos(a), .22f, Mathf.Sin(a))),
                    new Vector3(.24f, .17f, .35f), germinate * fade);
            }
            float canopy = Smooth(.1f + u * .17f, .58f + u * .17f, age);
            float width = Mathf.Max(.25f, c.BaseScale.x * .30f);
            float level = c.BaseScale.y * (.55f + .29f * u);
            Vector3 pLeaf = ground + new Vector3(Mathf.Cos(a) * width * (.60f + u * .4f),
                level, Mathf.Sin(a) * width * (.65f + .25f * u));
            pLeaf.x += Mathf.Sin(c.Age * 1.3f + index) * .035f;
            float leafAlpha = canopy * fade;
            if (broken)
            {
                pLeaf += new Vector3(Mathf.Cos(a) * breakAge * .48f,
                    -breakAge * breakAge * 1.55f, Mathf.Sin(a) * breakAge * .48f);
                leafAlpha *= 1 - Smooth(.18f, .9f, breakAge);
            }
            return Make(pLeaf, Face(new Vector3(Mathf.Cos(a), .20f + Mathf.Sin(c.Age + index) * .08f, Mathf.Sin(a))),
                new Vector3(.33f, .25f, .40f) * canopy, leafAlpha);
        }

        private static float FirstCueAfter(float earliest, float a, float b)
        {
            bool va = Finite(a) && a >= earliest;
            bool vb = Finite(b) && b >= earliest;
            if (va && vb) return Mathf.Min(a, b);
            return va ? a : vb ? b : -1;
        }

        private static bool Issued(float cueAt, float age) => Finite(cueAt) && cueAt >= 0 && age >= cueAt;
        private static float LifeFade(in Context c) => c.Duration > 0 ? 1 - Smooth(Mathf.Max(0, c.Duration - .48f), c.Duration, c.Age) : 1;
        private static float Smooth(float from, float to, float value) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(from, Mathf.Max(from + .0001f, to), value));
        private static Vector3 Incoming(in Context c) => SafeDirection(c.Target - c.Origin);
        // A 24 cm preview attachment proxy, not a collision/skin-surface assertion.
        // The adapter may already supply a surface point; adjust target upstream for it.
        private static Vector3 FrontSurface(in Context c) => c.Target - Incoming(c) * .24f;
        private static Vector3 SafeDirection(Vector3 v) => v.sqrMagnitude > .000001f ? v.normalized : Vector3.forward;

        private static Quaternion Face(Vector3 forward)
        {
            forward = SafeDirection(forward);
            return Quaternion.LookRotation(forward, Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > .98f ? Vector3.forward : Vector3.up);
        }

        private static Quaternion PatchFacing(Vector3 normal)
        {
            normal = SafeDirection(normal);
            Vector3 length = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (length.sqrMagnitude < .0001f) length = Vector3.right;
            return Quaternion.LookRotation(length, normal);
        }

        private static Vector3 PlaneOffset(Vector3 normal, float x, float y)
        {
            normal = SafeDirection(normal);
            Vector3 right = Vector3.Cross(Vector3.up, normal);
            if (right.sqrMagnitude < .0001f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(normal, right).normalized;
            return right * x + up * y;
        }

        private static Vector2 SquareCorner(int index)
        {
            switch (index % 4)
            {
                case 0: return new Vector2(-1, -1);
                case 1: return new Vector2(-1, 1);
                case 2: return new Vector2(1, 1);
                default: return new Vector2(1, -1);
            }
        }

        private static Vector3 Quadratic(Vector3 a, Vector3 b, Vector3 c, float t) =>
            (1 - t) * (1 - t) * a + 2 * (1 - t) * t * b + t * t * c;
        private static Vector3 QuadraticTangent(Vector3 a, Vector3 b, Vector3 c, float t) =>
            SafeDirection(2 * (1 - t) * (b - a) + 2 * t * (c - b));
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);

        private static Pose Hidden(Vector3 position) => new Pose
        {
            Position = position, Rotation = Quaternion.identity, Scale = Vector3.zero,
            Alpha = 0, Visible = false
        };

        private static Pose Make(Vector3 position, Quaternion rotation, Vector3 scale, float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            bool valid = Finite(position) && Finite(scale) && Finite(alpha) &&
                Finite(rotation.x) && Finite(rotation.y) && Finite(rotation.z) && Finite(rotation.w);
            return new Pose
            {
                Position = valid ? position : Vector3.zero,
                Rotation = valid ? rotation : Quaternion.identity,
                Scale = valid ? scale : Vector3.zero,
                Alpha = valid ? alpha : 0,
                Visible = valid && alpha > .002f && scale.sqrMagnitude > .000001f
            };
        }
    }
}
