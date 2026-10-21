// PURE308
using System;
using System.Collections.Generic;
using System.Globalization;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App
{
    // What a reaction leaves behind (#308 D308-132): a damage zone (wildfire), a slowing zone (mud) or a zone with no rule
    // that only holds the look and stands as the material of a second reaction (steam).
    public enum ReactionProductKind308 { Damage, Slow, None }

    // How the product's life is reckoned from the material's remaining life R: Extend = R + min(R x share, cap),
    // Cap = min(R, cap), Fixed = cap.
    public enum ReactionLifeMode308 { Extend, Cap, Fixed }

    // One row of Data/Spells/Reactions308.csv: a lingering zone of Material meets a spell of Trigger.
    public sealed class ReactionRow308
    {
        public string Id = "";
        public Element Material, Trigger, Product;
        public bool Generates;                  // true = 상생 (material feeds trigger), false = 상극 (trigger overcomes material)
        public ReactionProductKind308 Kind;
        public float RadiusMul = 1f;
        public ReactionLifeMode308 LifeMode;
        public float LifeShare, LifeCap;
        public float PerSecond;                 // Damage: share of the follow-up cast's power per second
        public float HitShare;                  // the instant blow: share of the follow-up cast's power, on every enemy in the material
        public float Tick;                      // Damage: seconds between steps
        public float Slow = 1f;                 // Slow: movement scale inside
    }

    // The ground a cast covers at the moment it is dispatched, read off its plan (#308 D308-132): flat, like every area
    // judgement (AreaGeometry). A volley is not an area; a single cast is the point it hits; an air single cast is nothing.
    public enum ReactionFootprintShape308 { None, Point, Circle, Corridor, Cone }

    public struct ReactionFootprint308
    {
        public ReactionFootprintShape308 Shape;
        public Vector3 Origin;      // Point / Circle centre / Corridor start / Cone apex
        public Vector3 Direction;   // Corridor / Cone forward
        public float Radius;        // Circle radius / Corridor half width
        public float Length;        // Corridor length / Cone range
        public float Angle;         // Cone half angle in degrees

        public static ReactionFootprint308 Point(Vector3 p) => new ReactionFootprint308 { Shape = ReactionFootprintShape308.Point, Origin = p };
        public static ReactionFootprint308 Circle(Vector3 c, float r) => new ReactionFootprint308 { Shape = ReactionFootprintShape308.Circle, Origin = c, Radius = r };
        public static ReactionFootprint308 Corridor(Vector3 start, Vector3 dir, float halfWidth, float length)
            => new ReactionFootprint308 { Shape = ReactionFootprintShape308.Corridor, Origin = start, Direction = dir, Radius = halfWidth, Length = length };
        public static ReactionFootprint308 Cone(Vector3 apex, Vector3 forward, float halfAngle, float range)
            => new ReactionFootprint308 { Shape = ReactionFootprintShape308.Cone, Origin = apex, Direction = forward, Angle = halfAngle, Length = range };
    }

    // Five-element reactions between the player's lingering zones and a later spell (SPEC-SPELL-120-308, DECISIONS D308-128 B,
    // D308-132). Pure arithmetic: which zone is material, whether the cast's ground touches it, what the product is.
    public static class ReactionRule308
    {
        public const int MaxDepth = 2;            // a product of a product may react once more, no further
        public const string ProductOwner = "zone.react";
        const string HealZone = "cone.healzone";

        // Zone owners that can be material: the lingering zones (zone.*, the products included) and 녹's heal zone. The
        // domains (domain.*) are not: there is one, under the player's feet, and it is the player's own defence (D308-132).
        public static bool MaterialOwner(string owner)
            => owner != null && (owner.StartsWith("zone.", StringComparison.Ordinal) || owner == HealZone);

        // The zone may react with a cast dispatched now by a caster standing at caster: it is material, active, not too deep, the
        // caster does not stand in it (you burn your own vines from outside), and the cast did not lay it (window = the cast's
        // attack ids; null = no window, nothing excluded).
        public static bool Eligible(in SpellZone308 zone, float now, Vector3 caster, SpellAttackWindow308 window)
        {
            if (!MaterialOwner(zone.Owner) || !SpellZoneRule308.Active(zone, now) || zone.Depth >= MaxDepth) return false;
            if (SpellZoneRule308.Inside(zone, caster)) return false;
            return window == null || zone.Token == 0 || !window.Holds(zone.Token);
        }

        public static ReactionRow308 Find(IReadOnlyList<ReactionRow308> rows, Element material, Element trigger)
        {
            for (int i = 0; rows != null && i < rows.Count; i++)
                if (rows[i].Material == material && rows[i].Trigger == trigger) return rows[i];
            return null;
        }

        // Does the cast's ground touch the zone's circle? Edge contact counts.
        public static bool Overlaps(in ReactionFootprint308 f, in SpellZone308 zone)
        {
            Vector3 c = zone.Centre;
            float r = Mathf.Max(0f, zone.Radius);
            switch (f.Shape)
            {
                case ReactionFootprintShape308.Point:
                    return AreaGeometry.InCircle(c, f.Origin, r, out _);
                case ReactionFootprintShape308.Circle:
                    return AreaGeometry.InCircle(c, f.Origin, r + Mathf.Max(0f, f.Radius), out _);
                case ReactionFootprintShape308.Corridor:
                    {
                        Vector3 dir = Unit(f.Direction);
                        return SegmentDistance(f.Origin, f.Origin + dir * Mathf.Max(0f, f.Length), c) <= r + Mathf.Max(0f, f.Radius);
                    }
                case ReactionFootprintShape308.Cone:
                    {
                        if (AreaGeometry.InCone(f.Origin, f.Direction, c, f.Angle, f.Length, out _, out _)) return true;
                        if (AreaGeometry.InCircle(f.Origin, c, r, out _)) return true;
                        Vector3 fwd = Unit(f.Direction);
                        return SegmentDistance(f.Origin, f.Origin + Turn(fwd, f.Angle) * f.Length, c) <= r
                            || SegmentDistance(f.Origin, f.Origin + Turn(fwd, -f.Angle) * f.Length, c) <= r
                            || ArcReach(f, c, r);
                    }
                default: return false;
            }
        }

        // The product's life from the material's remaining life at the moment of the reaction.
        public static float ProductLife(ReactionRow308 row, float remaining)
        {
            float rem = Mathf.Max(0f, remaining);
            switch (row.LifeMode)
            {
                case ReactionLifeMode308.Extend: return rem + Mathf.Min(rem * Mathf.Max(0f, row.LifeShare), Mathf.Max(0f, row.LifeCap));
                case ReactionLifeMode308.Cap: return Mathf.Min(rem, Mathf.Max(0f, row.LifeCap));
                default: return Mathf.Max(0f, row.LifeCap);
            }
        }

        // The product zone (owner, centre, radius, life, value, steps, depth). followPower = the follow-up cast's power before
        // the element damage scale (the host applies that); 0 for a cast without damage, and then the product hurts nobody.
        public static SpellZone308 Product(ReactionRow308 row, in SpellZone308 material, float now, float followPower, int cast)
        {
            float life = ProductLife(row, material.Until - now);
            var zone = new SpellZone308
            {
                Owner = ProductOwner, Cast = cast, Letter = material.Letter, Element = row.Product,
                Centre = material.Centre, Radius = material.Radius * row.RadiusMul,
                From = now, Until = now + life, Life = life, Depth = material.Depth + 1,
            };
            switch (row.Kind)
            {
                case ReactionProductKind308.Damage:
                    zone.TickEvery = row.Tick;
                    zone.Value = SpellZoneRule308.StepPower(followPower, row.PerSecond, row.Tick);
                    break;
                case ReactionProductKind308.Slow:
                    zone.Value = Mathf.Clamp01(row.Slow);
                    break;
            }
            return zone;
        }

        // The instant blow on each enemy inside the material (before the element damage scale).
        public static float HitPower(ReactionRow308 row, float followPower) => followPower > 0f && row.HitShare > 0f ? followPower * row.HitShare : 0f;

        static Vector3 Unit(Vector3 v) { v = AreaGeometry.Flat(v); return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward; }

        // Rotation about the vertical by degrees, without Quaternion (the offline runner cannot call native code).
        static Vector3 Turn(Vector3 v, float degrees)
        {
            float a = degrees * Mathf.Deg2Rad, cos = Mathf.Cos(a), sin = Mathf.Sin(a);
            return new Vector3(v.x * cos + v.z * sin, 0f, -v.x * sin + v.z * cos);
        }

        static float SegmentDistance(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = AreaGeometry.Flat(b - a), ap = AreaGeometry.Flat(p - a);
            float len = ab.sqrMagnitude;
            float t = len > 1e-8f ? Mathf.Clamp01(Vector3.Dot(ap, ab) / len) : 0f;
            return (ap - ab * t).magnitude;
        }

        // A circle beyond the cone's arc but within r of it, inside the cone's angle.
        static bool ArcReach(in ReactionFootprint308 f, Vector3 c, float r)
        {
            Vector3 to = AreaGeometry.Flat(c - f.Origin);
            float d = to.magnitude;
            if (d <= f.Length || d > f.Length + r) return false;
            return Vector3.Angle(Unit(f.Direction), to) <= f.Angle;
        }
    }

    // Reads Data/Spells/Reactions308.csv (# lines and blank lines are skipped). Never named Rules308_*: the table import
    // swallows those as row sheets.
    public static class ReactionTable308
    {
        public const string FileName = "Reactions308.csv";
        static readonly string[] Header = { "Id", "Material", "Trigger", "Relation", "Product", "Kind", "RadiusMul", "LifeMode", "LifeShare", "LifeCap", "PerSecond", "HitShare", "Tick", "Slow" };

        public static List<ReactionRow308> Parse(string csv, List<string> errors)
        {
            var rows = new List<ReactionRow308>();
            bool header = false;
            int line = 0;
            foreach (string raw in (csv ?? "").Replace("\r", "").Split('\n'))
            {
                line++;
                string text = raw.Trim().TrimStart('﻿');
                if (text.Length == 0 || text.StartsWith("#", StringComparison.Ordinal)) continue;
                string[] cells = text.Split(',');
                for (int i = 0; i < cells.Length; i++) cells[i] = cells[i].Trim();
                if (!header)
                {
                    header = true;
                    if (cells.Length != Header.Length) { errors?.Add("header: " + cells.Length + " columns, expected " + Header.Length); return rows; }
                    for (int i = 0; i < Header.Length; i++)
                        if (cells[i] != Header[i]) { errors?.Add("header: column " + (i + 1) + " is '" + cells[i] + "', expected '" + Header[i] + "'"); return rows; }
                    continue;
                }
                string where = "line " + line;
                if (cells.Length != Header.Length) { errors?.Add(where + ": " + cells.Length + " columns"); continue; }
                var row = new ReactionRow308 { Id = cells[0] };
                bool ok = row.Id.Length > 0;
                if (!ok) errors?.Add(where + ": empty Id");
                ok &= Enum308(cells[1], out row.Material, where, "Material", errors);
                ok &= Enum308(cells[2], out row.Trigger, where, "Trigger", errors);
                if (cells[3] == "Generates") row.Generates = true;
                else if (cells[3] != "Overcomes") { errors?.Add(where + ": Relation must be Generates or Overcomes"); ok = false; }
                ok &= Enum308(cells[4], out row.Product, where, "Product", errors);
                ok &= Enum308(cells[5], out row.Kind, where, "Kind", errors);
                ok &= Number(cells[6], out row.RadiusMul, where, "RadiusMul", errors);
                ok &= Enum308(cells[7], out row.LifeMode, where, "LifeMode", errors);
                ok &= Number(cells[8], out row.LifeShare, where, "LifeShare", errors);
                ok &= Number(cells[9], out row.LifeCap, where, "LifeCap", errors);
                ok &= Number(cells[10], out row.PerSecond, where, "PerSecond", errors);
                ok &= Number(cells[11], out row.HitShare, where, "HitShare", errors);
                ok &= Number(cells[12], out row.Tick, where, "Tick", errors);
                ok &= Number(cells[13], out row.Slow, where, "Slow", errors);
                if (!ok) continue;
                if (row.Generates ? !ElementRelations.Generates(row.Material, row.Trigger) : !ElementRelations.Overcomes(row.Trigger, row.Material))
                { errors?.Add(where + ": " + row.Material + " and " + row.Trigger + " are not in the relation " + cells[3]); continue; }
                if (row.RadiusMul <= 0f) { errors?.Add(where + ": RadiusMul must be positive"); continue; }
                if (row.Kind == ReactionProductKind308.Damage && row.Tick <= 0f) { errors?.Add(where + ": a Damage product needs a positive Tick"); continue; }
                if (row.Slow > 1f) { errors?.Add(where + ": Slow is a movement scale, at most 1"); continue; }
                if (row.LifeMode != ReactionLifeMode308.Extend && row.LifeCap <= 0f) { errors?.Add(where + ": " + row.LifeMode + " needs a positive LifeCap"); continue; }
                if (ReactionRule308.Find(rows, row.Material, row.Trigger) != null) { errors?.Add(where + ": a second row for " + row.Material + " + " + row.Trigger); continue; }
                rows.Add(row);
            }
            if (!header) errors?.Add("no header");
            return rows;
        }

        static bool Enum308<T>(string text, out T value, string where, string column, List<string> errors) where T : struct
        {
            if (Enum.TryParse(text, false, out value) && Enum.IsDefined(typeof(T), value)) return true;
            errors?.Add(where + ": unknown " + column + " '" + text + "'");
            return false;
        }

        static bool Number(string text, out float value, string where, string column, List<string> errors)
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f) return true;
            errors?.Add(where + ": " + column + " must be a number 0 or above, not '" + text + "'");
            return false;
        }
    }
}
