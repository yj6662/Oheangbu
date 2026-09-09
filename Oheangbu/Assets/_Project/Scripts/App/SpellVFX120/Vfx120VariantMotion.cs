using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // Visual choreography only. These analytic secondary phases emit no gameplay events.
    // The current interface has no second target, damage/hit-break event or AreaPlan.Shots.
    // Consequently bounce/ricochet/freeze-release/salvo offsets are review illustrations,
    // not proof that those currently unimplemented game rules or impacts occurred.
    // Authoritative per-shot clocks, when available, must take priority in the caller.
    public static class Vfx120VariantMotion
    {
        // Call after calculating the generic part pose, BEFORE assigning its transform.
        // Pass a fresh copy of the base center for EACH part; never accumulate center edits.
        // All positions and directions are in the VFX root's local space. aim is its endpoint.
        // scale is the final part scale, not an extra multiplier. This function preserves
        // the original meshes/materials and allocates no objects or managed collections.
        public static void Apply(Vfx120Profile p, float age, float flight, float life,
            int index, int count, Vector3 aim, ref Vector3 center, ref Vector3 local,
            ref Vector3 axis, ref Vector3 scale, float size,
            float originGround = -1f, float targetGround = -1f)
        {
            if (p == null || string.IsNullOrEmpty(p.Glyph)) return;
            if (!Finite(age) || !Finite(flight) || !Finite(life) || !Finite(size) || !Finite(aim)
                || !Finite(originGround) || !Finite(targetGround) || !Finite(p.PartScale)
                || index < 0 || index >= Mathf.Max(1, count))
            {
                center = local = scale = Vector3.zero; axis = Vector3.forward;
                return;
            }
            char glyph = p.Glyph[0];
            flight = Mathf.Max(.01f, flight);
            count = Mathf.Max(1, count);
            float u = count > 1 ? index / (float)(count - 1) : .5f;
            Vector3 forward = Direction(aim);
            switch (glyph)
            {
                case '곡': GroundWeave(p, age, flight, life, index, count, aim, size, targetGround,
                    ref center, ref local, ref axis, ref scale); break;
                case '곤': RootUppercut(p, age, flight, life, index, count, aim, size, targetGround,
                    ref center, ref local, ref axis, ref scale); break;
                case '공': AdvancingThicket(p, age, flight, life, index, count, aim, size, originGround, targetGround,
                    ref center, ref local, ref axis, ref scale); break;
                case '국': GrowingFooting(p, age, flight, life, index, count, originGround,
                    ref center, ref local, ref axis, ref scale); break;
                case '넉': LowEmberRing(p, age, life, index, count, size, originGround,
                    ref center, ref local, ref axis, ref scale); break;
                case '넌': ForearmCharge(p, age, life, index, originGround,
                    ref center, ref local, ref axis, ref scale); break;
                case '넘': SteadfastPoints(p, age, life, index, originGround,
                    ref center, ref local, ref axis, ref scale); break;
                case '낫': FireSplit(p, age, flight, life, index, aim, forward, size, ref center, ref local, ref axis, ref scale); break;
                case '논': FireEndBurst(p, age, flight, life, u, aim, forward, size, ref center, ref local, ref axis, ref scale); break;
                case '망': StoneSkip(p, age, flight, life, aim, forward, size, ref center, ref local, ref axis, ref scale); break;
                case '목': DelayedStone(p, age, flight, life, index, u, aim, size, ref center, ref local, ref axis, ref scale); break;
                case '악':
                case '앙': WaterReturn(p, glyph == '악', age, flight, life, aim, forward, size, ref center, ref local, ref axis, ref scale); break;
                case '옥': Tide(p, age, flight, life, aim, ref center, ref local, ref axis, ref scale); break;
                case '앗': IceBind(p, age, flight, life, index, count, size, ref local, ref axis, ref scale); break;
                case '삭': Pierce(p, age, flight, life, aim, forward, size, ref center, ref local, ref axis, ref scale); break;
                case '손': WarmMetal(p, age, flight, life, u, size, ref local, ref axis, ref scale); break;
                case '소':
                case '속':
                case '솟':
                case '송': MetalSalvo(p, glyph, age, flight, life, index, count, u, aim, size, ref center, ref local, ref axis, ref scale); break;
            }
        }

        // These three effects have caster-relative accents too. Calling only Apply
        // would leave the generic flecks at the old head/target anchor.
        public static int GetSelfAccentCount(Vfx120Profile p)
        {
            if (p == null) return 0;
            switch (p.Glyph) { case "넉": return 12; case "넌": return 6; case "넘": return 3; default: return 0; }
        }

        public static bool TrySampleSelfAccent(Vfx120Profile p, float age, float life,
            float originGround, int index, int count, out Vfx120CueMotion.Pose pose)
        {
            pose = new Vfx120CueMotion.Pose { Rotation = Quaternion.identity };
            int wanted = GetSelfAccentCount(p);
            if (wanted == 0) return false;
            if (!Finite(age) || !Finite(life) || !Finite(originGround) || index < 0 || index >= count || index >= wanted)
                return true; // Authored hidden pose; never fall back to the obsolete anchor.
            float fade = Lifetime(age, life);
            Vector3 point, axis, dimensions;
            if (p.Glyph == "넉")
            {
                float a = index * Mathf.PI * 2 / wanted + age * .42f;
                Vector3 radial = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float rise = .5f + .5f * Mathf.Sin(age * 2.8f + index * 1.7f);
                point = radial * Mathf.Max(.55f, p.Size * .98f);
                point.y = originGround + .10f + rise * .18f;
                axis = Direction(Vector3.up + radial * .18f);
                dimensions = new Vector3(.065f, .035f, .15f);
            }
            else if (p.Glyph == "넌")
            {
                float a = index * Mathf.PI * 2 / wanted + age * 1.8f;
                // An exposed point outside the right forearm, not inside the torso.
                Vector3 charge = new Vector3(.48f, originGround + 1.04f, .40f);
                point = charge + new Vector3(Mathf.Cos(a) * .09f, Mathf.Sin(a) * .12f, -.025f + (index % 2) * .07f);
                axis = Direction(charge - point + Vector3.up * .08f);
                float breathe = 1 + .09f * Mathf.Sin(age * 4 + index);
                dimensions = new Vector3(.07f, .06f, .14f) * breathe;
            }
            else
            {
                point = SteadfastPoint(index, age, originGround) + Vector3.forward * .025f;
                axis = Vector3.up;
                dimensions = new Vector3(.035f, .035f, .075f);
            }
            pose.Position = point;
            pose.Rotation = Face(axis);
            pose.Scale = MeshSize(p.AccentMesh, dimensions, Vector3.one) * fade;
            pose.Alpha = fade;
            pose.Visible = fade > .001f;
            return true;
        }

        private static void LowEmberRing(Vfx120Profile p, float age, float life, int index,
            int count, float size, float ground, ref Vector3 center, ref Vector3 local,
            ref Vector3 axis, ref Vector3 scale)
        {
            float a = index * Mathf.PI * 2 / count + age * .22f;
            Vector3 radial = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            float breath = .88f + .12f * Mathf.Sin(age * 3.2f + index * 1.3f);
            axis = Direction(Vector3.up + radial * .24f);
            scale = WorldSize(p, new Vector3(.105f, .08f, .24f * breath)) * Lifetime(age, life);
            center = radial * Mathf.Max(.55f, size * .94f);
            center.y = ground + HalfHeight(p, axis, scale) + .035f;
            local = Vector3.zero;
        }

        private static void ForearmCharge(Vfx120Profile p, float age, float life, int index,
            float ground, ref Vector3 center, ref Vector3 local, ref Vector3 axis, ref Vector3 scale)
        {
            center = new Vector3(.48f, ground + 1.04f, .40f);
            local = Vector3.zero;
            if (index >= 2) { scale = Vector3.zero; axis = Vector3.up; return; }
            // Two short crossed ribbons form a held knot; six embers carry the charge.
            float turn = age * .65f + index * Mathf.PI;
            center += new Vector3(Mathf.Cos(turn) * .025f, Mathf.Sin(turn) * .035f, index * .035f);
            axis = Direction(Vector3.up + Vector3.right * (index == 0 ? .7f : -.7f));
            scale = WorldSize(p, new Vector3(.10f, .055f, .31f)) * Lifetime(age, life);
        }

        private static void SteadfastPoints(Vfx120Profile p, float age, float life, int index,
            float ground, ref Vector3 center, ref Vector3 local, ref Vector3 axis, ref Vector3 scale)
        {
            center = SteadfastPoint(index, age, ground);
            local = Vector3.zero;
            // Seal lies in XZ. Pointing its Z upward turns its broad face forward.
            axis = Direction(Vector3.up + Vector3.right * Mathf.Sin(age * .40f + index * 2) * .12f);
            scale = index < 3
                ? WorldSize(p, new Vector3(.145f, .032f, .19f)) * Lifetime(age, life)
                : Vector3.zero;
        }

        private static Vector3 SteadfastPoint(int index, float age, float ground)
        {
            Vector3 point = index == 0 ? new Vector3(0, ground + 1.27f, .40f)
                : new Vector3(index == 1 ? -.34f : .34f, ground + 1.43f, .26f);
            float a = age * .48f + index * Mathf.PI * 2 / 3;
            return point + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * .025f;
        }

        // 곡: interwoven horizontal roots spread OUT from the selected floor point, then
        // remain there. No orbit, vertical cage or character-height lift is used.
        private static void GroundWeave(Vfx120Profile p, float age, float flight, float life,
            int index, int count, Vector3 aim, float size, float ground,
            ref Vector3 center, ref Vector3 local, ref Vector3 axis, ref Vector3 scale)
        {
            int ring = index % 3;
            int spoke = index / 3;
            int spokes = Mathf.Max(1, (count + 2) / 3);
            float a = spoke * Mathf.PI * 2 / spokes + ring * .42f;
            Vector3 radial = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            Vector3 tangent = new Vector3(-radial.z, 0, radial.x);
            float spread = Ease(Phase(age - flight - ring * .10f, .56f));
            float reach = Mathf.Max(.3f, size) * (.20f + ring * .25f) * spread;
            axis = ring == 1 ? radial : tangent;
            float length = Mathf.Max(.35f, p.PartScale.z * (.64f + ring * .12f));
            Vector3 dimensions = new Vector3(Mathf.Max(.14f, p.PartScale.x * .28f),
                .055f + ring * .018f, length);
            scale = WorldSize(p, dimensions) * spread * Lifetime(age, life);
            center = new Vector3(aim.x, ground, aim.z) + radial * reach;
            center.y += HalfHeight(p, axis, scale) + .022f + ring * .008f;
            local = Vector3.zero;
        }

        // 곤: six independent roots push once from a compact footprint, make a short
        // upward crown, and fold down. The target/player root is never moved by this.
        private static void RootUppercut(Vfx120Profile p, float age, float flight, float life,
            int index, int count, Vector3 aim, float size, float ground,
            ref Vector3 center, ref Vector3 local, ref Vector3 axis, ref Vector3 scale)
        {
            float t = age - flight - index * .012f;
            float push = Ease(Phase(t, .18f));
            float recoil = 1 - Ease(Phase(t - .27f, .33f));
            float thrust = push * recoil;
            float a = index * Mathf.PI * 2 / count;
            Vector3 radial = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            float height = Mathf.Max(.7f, p.Lift);
            axis = Direction(Vector3.up - radial * (.10f + .20f * thrust));
            Vector3 dimensions = new Vector3(Mathf.Max(.16f, p.PartScale.x * .18f),
                Mathf.Max(.09f, p.PartScale.y * .13f), height);
            scale = WorldSize(p, dimensions) * thrust * Lifetime(age, life);
            float radius = Mathf.Max(.16f, size * .24f) * (1 - .25f * thrust);
            center = new Vector3(aim.x, ground, aim.z) + radial * radius;
            center.y += HalfHeight(p, axis, scale) + .02f;
            local = Vector3.zero;
        }

        // 공: low roots lead; a denser, taller screen follows behind. Only the caller's
        // presentation center advances. Native combat movement/plans remain authoritative.
        private static void AdvancingThicket(Vfx120Profile p, float age, float flight, float life,
            int index, int count, Vector3 aim, float size, float originGround, float targetGround,
            ref Vector3 center, ref Vector3 local, ref Vector3 axis, ref Vector3 scale)
        {
            Vector3 forward = FlatDirection(aim);
            Vector3 right = Right(forward);
            Vector3 travelCenter = center;
            float distance = new Vector2(aim.x, aim.z).magnitude;
            float progressed = Mathf.Clamp01(Vector3.Dot(travelCenter, forward) / Mathf.Max(.1f, distance));
            float ground = Mathf.Lerp(originGround, targetGround, progressed);
            int lowCount = Mathf.Clamp(count / 3, 1, Mathf.Max(1, count - 1));
            bool leading = index < lowCount;
            int rowCount = leading ? lowCount : Mathf.Max(1, count - lowCount);
            int rowIndex = leading ? index : index - lowCount;
            float u = (rowIndex + .5f) / rowCount;
            float rise = Ease(Phase(age - flight - (leading ? 0 : .10f + rowIndex * .015f), .27f));
            float width = Mathf.Max(.4f, size * 2);
            Vector3 dimensions;
            if (leading)
            {
                axis = right;
                dimensions = new Vector3(Mathf.Max(.20f, p.PartScale.x * .24f), .10f,
                    width / rowCount * 1.28f);
                center = travelCenter + right * ((u - .5f) * width) + forward * .34f;
            }
            else
            {
                // Overlapping branches and their existing broad leaves form a screen;
                // the front row stays low enough to read as the leading root wave.
                axis = Direction(Vector3.up + forward * (.07f + Mathf.Sin(age * 1.6f + rowIndex) * .022f));
                dimensions = new Vector3(Mathf.Max(width / rowCount * 1.65f, p.PartScale.x * .40f),
                    Mathf.Max(.14f, p.PartScale.y * .15f), Mathf.Max(.9f, p.Lift));
                center = travelCenter + right * ((u - .5f) * width) - forward * (.40f + (rowIndex % 2) * .13f);
            }
            scale = WorldSize(p, dimensions) * rise * Lifetime(age, life);
            center.y = ground + HalfHeight(p, axis, scale) + .025f;
            local = Vector3.zero;
        }

        // 국: a woven deck is assembled at the cast-origin floor, then raised by three
        // living struts. The horizontal footing remains distinct from the tall supports.
        private static void GrowingFooting(Vfx120Profile p, float age, float flight, float life,
            int index, int count, float ground,
            ref Vector3 center, ref Vector3 local, ref Vector3 axis, ref Vector3 scale)
        {
            int supportCount = Mathf.Clamp(count - 4, 1, Mathf.Max(1, count - 1));
            bool support = index < supportCount;
            float growth = Ease(Phase(age - flight, .90f));
            float height = Mathf.Max(.7f, p.Lift) * growth;
            float lifeFade = Lifetime(age, life);
            if (support)
            {
                float a = index * Mathf.PI * 2 / supportCount;
                Vector3 radial = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                axis = Vector3.up;
                scale = WorldSize(p, new Vector3(Mathf.Max(.19f, p.PartScale.x * .17f),
                    Mathf.Max(.12f, p.PartScale.y * .12f), Mathf.Max(.7f, p.Lift))) * growth * lifeFade;
                center = radial * .23f;
                center.y = ground + HalfHeight(p, axis, scale) + .02f;
            }
            else
            {
                int bar = index - supportCount;
                int bars = Mathf.Max(1, count - supportCount);
                bool across = (bar & 1) == 0;
                float offset = ((bar / 2) - (bars - 2) * .25f) * .36f;
                axis = across ? Vector3.right : Vector3.forward;
                float assemble = Ease(Phase(age - flight * .42f - bar * .022f, .28f));
                scale = WorldSize(p, new Vector3(Mathf.Max(.34f, p.PartScale.x * .28f), .11f,
                    Mathf.Max(.85f, p.PartScale.z * .48f))) * assemble * lifeFade;
                center = across ? new Vector3(0, 0, offset) : new Vector3(offset, 0, 0);
                center.y = ground + height * lifeFade + .07f + (bar & 1) * .028f;
            }
            local = Vector3.zero;
        }

        // The current Vine is X=.79318, Y=.24870, Z=1, but use the actual shared bounds
        // so a later remesh preserves authored widths/heights without mutating vertices.
        private static Vector3 WorldSize(Vfx120Profile p, Vector3 dimensions)
        {
            return MeshSize(p.BodyMesh, dimensions, new Vector3(.79318f, .24870f, 1));
        }

        private static Vector3 MeshSize(Mesh mesh, Vector3 dimensions, Vector3 fallback)
        {
            Vector3 extent = mesh != null ? mesh.bounds.size : fallback;
            return new Vector3(Mathf.Max(0, dimensions.x) / Mathf.Max(.001f, extent.x),
                Mathf.Max(0, dimensions.y) / Mathf.Max(.001f, extent.y),
                Mathf.Max(0, dimensions.z) / Mathf.Max(.001f, extent.z));
        }

        private static Quaternion Face(Vector3 axis)
        {
            axis = Direction(axis);
            return Quaternion.LookRotation(axis, Mathf.Abs(axis.y) > .98f ? Vector3.forward : Vector3.up);
        }

        private static float HalfHeight(Vfx120Profile p, Vector3 axis, Vector3 scale)
        {
            Vector3 e = p.BodyMesh != null ? p.BodyMesh.bounds.extents : new Vector3(.39659f, .12435f, .5f);
            axis = Direction(axis);
            Quaternion q = Quaternion.LookRotation(axis, Mathf.Abs(axis.y) > .98f ? Vector3.forward : Vector3.up);
            return Mathf.Abs((q * Vector3.right).y) * e.x * scale.x
                + Mathf.Abs((q * Vector3.up).y) * e.y * scale.y
                + Mathf.Abs((q * Vector3.forward).y) * e.z * scale.z;
        }

        private static float Lifetime(float age, float life) => Ease(Phase(age, .08f))
            * (1 - Ease(Phase(age - Mathf.Max(0, life - .7f), Mathf.Min(.7f, Mathf.Max(.01f, life)))));
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);

        private static void FireSplit(Vfx120Profile p, float age, float flight, float life,
            int index, Vector3 aim, Vector3 forward, float size,
            ref Vector3 center, ref Vector3 local, ref Vector3 axis, ref Vector3 scale)
        {
            float side = (index & 1) == 0 ? -1f : 1f;
            float post = Phase(age - flight, PostSpan(flight, life, 1.3f));
            Vector3 right = Right(forward);
            Vector3 branch = Direction(forward * .72f + right * side * .7f);
            center = age < flight ? aim * Phase(age, flight) : aim;
            local = age < flight ? right * side * .025f : branch * (size * 2.1f * post);
            axis = age < flight ? forward : branch;
            scale = Sized(p, age, life, new Vector3(Mathf.Lerp(.48f, 1f, Ease(post)), .8f, 1f));
        }

        private static void FireEndBurst(Vfx120Profile p, float age, float flight, float life,
            float u, Vector3 aim, Vector3 forward, float size,
            ref Vector3 center, ref Vector3 local, ref Vector3 axis, ref Vector3 scale)
        {
            // Stage one is a narrow outward jet; stage two expands at that same endpoint.
            float post = Phase(age - flight, PostSpan(flight, life, .85f));
            float angle = u * Mathf.PI * 2.399963f * 3f;
            Vector3 outward = Direction(new Vector3(Mathf.Cos(angle), .35f + u * .8f, Mathf.Sin(angle)));
            center = aim * Phase(age, flight);
            local = age < flight ? -forward * size * (1 - u) * .45f : outward * size * (1.25f * Ease(post));
            axis = age < flight ? forward : outward;
            scale = Sized(p, age, life, age < flight ? new Vector3(.35f, .45f, .85f) : Vector3.one * (1.25f - post * .4f));
        }

        private static void StoneSkip(Vfx120Profile p, float age, float flight, float life,
            Vector3 aim, Vector3 forward, float size,
            ref Vector3 center, ref Vector3 local, ref Vector3 axis, ref Vector3 scale)
        {
            // CSV: first impact and ONE further forward skip, i.e. two contact points total.
            float t = Phase(age, flight);
            float post = Phase(age - flight, PostSpan(flight, life, .85f));
            Vector3 groundForward = FlatDirection(forward);
            center = age <= flight
                ? aim * t + Vector3.up * (4 * t * (1 - t) * Mathf.Max(.22f, size * .75f))
                : aim + groundForward * (size * 2.6f * post) + Vector3.up * (Mathf.Sin(post * Mathf.PI) * size * .62f);
            local = Vector3.zero;
            axis = age <= flight ? forward + Vector3.up * ((1 - 2 * t) * .45f)
                : groundForward + Vector3.up * (Mathf.Cos(post * Mathf.PI) * .55f);
            // Brief flattening at each contact, without changing any mesh vertices.
            float contact = age <= flight ? 1 - Ease(Phase(flight - age, flight * .16f)) : Mathf.Abs(Mathf.Cos(post * Mathf.PI));
            scale = Sized(p, age, life, new Vector3(1 + contact * .05f, 1 - contact * .12f, 1));
        }

        private static void DelayedStone(Vfx120Profile p, float age, float flight, float life,
            int index, float u, Vector3 aim, float size,
            ref Vector3 center, ref Vector3 local, ref Vector3 axis, ref Vector3 scale)
        {
            // The front passes; fixed stone teeth rise later in sequence along the path.
            float start = flight + u * PostSpan(flight, life, 2.4f);
            float rise = Ease(Phase(age - start, Mathf.Max(.06f, flight * .5f)));
            Vector3 forward = FlatDirection(aim);
            float length = Mathf.Max(size * 2, new Vector2(aim.x, aim.z).magnitude);
            center = new Vector3(0, center.y, 0); // keep the caller's sampled ground height
            local = forward * Mathf.Lerp(.2f, length, u) + Right(forward) * ((index % 3 - 1) * size * .24f);
            local.y -= (1 - rise) * p.PartScale.z * .25f;
            axis = Vector3.up;
            scale = Sized(p, age, life, new Vector3(.72f, .72f, 1.15f)) * rise;
        }

        private static void WaterReturn(Vfx120Profile p, bool rehit, float age, float flight,
            float life, Vector3 aim, Vector3 forward, float size,
            ref Vector3 center, ref Vector3 local, ref Vector3 axis, ref Vector3 scale)
        {
            float post = Phase(age - flight, PostSpan(flight, life, rehit ? 1.25f : 1.1f));
            Vector3 right = Right(forward);
            local = Vector3.zero;
            if (age <= flight) { center = aim * Phase(age, flight); axis = forward; }
            else if (rehit)
            {
                // 악: recoil past the target, turn, and meet exactly the same point again.
                center = aim + forward * (Mathf.Sin(post * Mathf.PI) * size * 2)
                    + right * (Mathf.Sin(post * Mathf.PI * 2) * size * .25f);
                axis = forward * Mathf.Cos(post * Mathf.PI) + right * (Mathf.Cos(post * Mathf.PI * 2) * .25f);
            }
            else
            {
                // 앙: the return destination is this instance's origin, not another target.
                center = Vector3.Lerp(aim, Vector3.zero, Ease(post)) + right * (Mathf.Sin(post * Mathf.PI) * size * .45f);
                axis = -forward;
            }
            scale = Sized(p, age, life, Vector3.one);
            if (!rehit && age > flight) scale *= 1 - Ease(Phase(post - .88f, .12f));
        }

        private static void Tide(Vfx120Profile p, float age, float flight, float life,
            Vector3 aim, ref Vector3 center, ref Vector3 local, ref Vector3 axis, ref Vector3 scale)
        {
            float post = Phase(age - flight, PostSpan(flight, life, 6f));
            bool returning = post > .5f;
            float travel = Ease(returning ? (1 - post) * 2 : post * 2);
            center = new Vector3(aim.x * travel, center.y, aim.z * travel);
            if (returning) local.z = -local.z;
            axis = new Vector3(0, .35f, returning ? -1 : 1);
            scale = Sized(p, age, life, Vector3.one);
            scale *= Ease(Phase(age - flight, flight * .4f));
        }

        private static void IceBind(Vfx120Profile p, float age, float flight, float life,
            int index, int count, float size, ref Vector3 local, ref Vector3 axis, ref Vector3 scale)
        {
            float angle = index / (float)count * Mathf.PI * 2;
            Vector3 radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            float freeze = Ease(Phase(age - flight, Mathf.Max(.08f, flight * .3f)));
            // Expiry-only illustration: there is no hit-break signal in this interface.
            float release = Phase(age - Mathf.Max(flight, life - .32f), .32f);
            local = radial * size * (.6f + release * .32f);
            local.y = .15f - release * release * .25f;
            axis = Vector3.up + radial * (.12f + release * .7f);
            scale = Sized(p, age, life, new Vector3(.55f, .7f, .85f + (index % 3) * .12f)) * freeze;
        }

        private static void Pierce(Vfx120Profile p, float age, float flight, float life,
            Vector3 aim, Vector3 forward, float size,
            ref Vector3 center, ref Vector3 local, ref Vector3 axis, ref Vector3 scale)
        {
            float post = Phase(age - flight, PostSpan(flight, life, 1.5f));
            center = age <= flight ? aim * Phase(age, flight) : aim + forward * Mathf.Max(size * 5, 1) * post;
            local = Vector3.zero; axis = forward;
            scale = Sized(p, age, life, new Vector3(.85f, .85f, 1.15f));
        }

        private static void WarmMetal(Vfx120Profile p, float age, float flight, float life,
            float u, float size, ref Vector3 local, ref Vector3 axis, ref Vector3 scale)
        {
            // 손 is a self buff, not a newly invented volley or persistent gameplay stack.
            // Small rhythmic expansion reads heating; actual cast-stack values are absent.
            float heat = .5f + .5f * Mathf.Sin(age / flight * Mathf.PI * 2 - u * 3);
            float angle = u * Mathf.PI * 2 + age * .32f;
            local = new Vector3(Mathf.Cos(angle) * size * .45f, .8f + u * .3f, .35f + Mathf.Sin(angle) * size * .25f);
            axis = Vector3.forward;
            scale = Sized(p, age, life, Vector3.one * (1 + heat * .07f));
        }

        private static void MetalSalvo(Vfx120Profile p, char glyph, float age, float flight,
            float life, int index, int count, float u, Vector3 aim, float size,
            ref Vector3 center, ref Vector3 local, ref Vector3 axis, ref Vector3 scale)
        {
            // Deterministic display offsets only. This is NOT AreaPlan.Shots scheduling.
            float spreadTime = Mathf.Min(Mathf.Max(0, life - flight - .35f) * .45f, flight * .16f * (count - 1));
            float shotAge = age - spreadTime * u;
            Vector3 right = Right(Direction(aim));
            Vector3 endpoint = aim + right * ((u - .5f) * size * 1.5f);
            if (glyph == '송') endpoint.y += Mathf.Sin(u * Mathf.PI) * size * .65f;
            Vector3 forward = Direction(endpoint);
            center = endpoint * Phase(shotAge, flight); local = Vector3.zero; axis = forward;
            Vector3 shape = glyph == '솟' ? new Vector3(1 - u * .24f, 1 - u * .24f, 1 + u * .7f) : Vector3.one;
            scale = Sized(p, age, life, shape) * Ease(Phase(shotAge, Mathf.Min(.07f, flight * .4f)));
            float post = Phase(shotAge - flight, PostSpan(flight, life, glyph == '송' ? 3f : 1.7f));
            if (shotAge <= flight) return;
            if (glyph == '속')
            {
                float side = (index & 1) == 0 ? -1f : 1f;
                Vector3 deflected = Direction(forward * .5f + right * side * .85f);
                center = endpoint + deflected * (post * size * 1.8f); axis = deflected;
                scale *= 1 - Ease(Phase(post - .7f, .3f));
            }
            else if (glyph == '송')
            {
                float fall = Ease(Phase(post - .2f, .8f));
                center = endpoint + Vector3.down * (fall * fall * size * 1.4f);
                axis = Vector3.Lerp(forward, Vector3.down + right * .25f, fall);
                scale = Vector3.Scale(scale, new Vector3(1 + (1 - fall) * .8f, 1 + (1 - fall) * .8f, 1));
                scale *= 1 - Ease(Phase(post - .75f, .25f));
            }
            else scale *= 1 - Ease(post); // 소 / 솟 terminate each visual needle at its contact.
        }

        private static Vector3 Sized(Vfx120Profile p, float age, float life, Vector3 multiplier)
        {
            float end = 1 - Ease(Phase(age - Mathf.Max(0, life - .7f), Mathf.Min(.7f, Mathf.Max(.01f, life))));
            return Vector3.Scale(p.PartScale, multiplier) * Ease(Phase(age, .08f)) * end;
        }
        private static float PostSpan(float flight, float life, float ratio)
            => Mathf.Max(.001f, Mathf.Min(Mathf.Max(.06f, flight * ratio), Mathf.Max(.001f, life - flight - .2f)));
        private static float Phase(float elapsed, float span) => Mathf.Clamp01(elapsed / Mathf.Max(.001f, span));
        private static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }
        private static Vector3 Direction(Vector3 v) => v.sqrMagnitude > .00001f ? v.normalized : Vector3.forward;
        private static Vector3 FlatDirection(Vector3 v) { v.y = 0; return Direction(v); }
        private static Vector3 Right(Vector3 forward) => Direction(Vector3.Cross(Vector3.up, FlatDirection(forward)));
    }
}
