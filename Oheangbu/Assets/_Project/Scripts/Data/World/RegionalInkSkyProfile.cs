using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    /// <summary>Continuous XZ climate blend. Cloud geometry and its clock stay in the shared sky profile.</summary>
    /// <remarks>#308 (SPEC-REGION-SKY-308 1b, TEST): snap distance, capital atmosphere anchor and sub-region accents. Every new field
    /// defaults to "current behaviour" (SnapDistance 0 = off, CapitalAtmosphere 0 = the shared sky profile's own anchor, no accents),
    /// so an asset without them (RegionalSky297) evaluates exactly as before (AC-S14 b).
    /// #308 step 2 (RealmInkSky308, TEST): per-realm cloud character appended to Entry. All of it defaults to 0 and an entry with
    /// Stretch 0 carries no step-2 data: such a state has Shape 0, the driver writes no step-2 property and the far fog stays the
    /// horizon, so assets without the fields (RegionalSky297, a 1b-only RegionalSky308) render exactly as before.</remarks>
    [CreateAssetMenu(menuName = "Oheangbu/World/Regional Ink Sky Profile")]
    public sealed class RegionalInkSkyProfile : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public RealmId Realm;
            public Color Horizon = new Color(.90f, .88f, .83f);
            public Color Zenith = new Color(.73f, .75f, .72f);
            public Color Cloud = new Color(.55f, .58f, .55f);
            [Range(0f, .5f)] public float CloudDensity = .14f;

            // ---- #308 step 2 (appended; serialization-safe: a missing field reads 0 = "no step-2 data") ----
            [Header("#308 step 2 — RealmInkSky308 (TEST). Stretch 0 = no step-2 data")]
            [Tooltip("구름 먹: cloud ink core (sRGB, 담채 — never pure black)")]
            public Color CloudInk;
            [Tooltip("덮임 (0..1). CloudDensity keeps its .5 clamp for the step-1 sky; Coverage is the step-2 value")]
            [Range(0f, 1f)] public float Coverage;
            [Range(0f, .4f)] public float Softness;
            [Tooltip("Band stretch (> 0). 0 marks the entry as having no step-2 data")]
            [Min(0f)] public float Stretch;
            [Tooltip("Band direction, degrees of world yaw (blended between realms as a unit vector)")]
            public float BandYaw;
            [Tooltip("Storm side, degrees of world yaw (blended between realms as a unit vector)")]
            public float StormYaw;
            [Range(0f, 1f)] public float StormGain;
            [Range(0f, 1f)] public float WetEdge;
            [Range(0f, 1f)] public float CoreInk;
            [Tooltip("지평 안개 height")]
            [Range(0f, .5f)] public float MistHeight;
            [Range(0f, 1f)] public float ZenithFade;
            [Tooltip("Far fog tint (sRGB). a = 0 uses the horizon; a in (0, 1] mixes the horizon toward rgb")]
            public Color FogTint;

            /// <summary>True when the entry carries step-2 data (Stretch > 0 and every step-2 value finite).</summary>
            public bool HasShape => Stretch > 0f && Finite(Stretch) && Finite(CloudInk) && Finite(Coverage) && Finite(Softness) &&
                Finite(BandYaw) && Finite(StormYaw) && Finite(StormGain) && Finite(WetEdge) && Finite(CoreInk) && Finite(MistHeight) &&
                Finite(ZenithFade);
        }

        [Serializable]
        public struct SkyState
        {
            public Color Horizon;
            public Color Zenith;
            public Color Cloud;
            public float CloudDensity;
            // #308: far fog colour the screen passes follow (the blended horizon unless step-2 FogTint data mixes it), horizon mist
            // the step-2 sky reads (InkCloudSky has no such property), and the capital atmosphere strength (0 = leave the shared
            // profile's value).
            public Color Fog;
            public float Mist;
            public float Capital;
            // #308 step 2: cloud character, blended over the entries that carry step-2 data (renormalized). Shape = their weight
            // share (0 = none: the driver writes no step-2 property). Band / Storm are blended unit vectors (sin, cos) of the yaws.
            public Color CloudInk;
            public float Coverage, Softness, Stretch, StormGain, WetEdge, CoreInk, ZenithFade;
            public Vector2 Band, Storm;
            public float Shape;

            /// <summary>World yaw in degrees of the blended band direction (InkWashSky300 convention: axis = (sin, cos)).</summary>
            public float BandYaw => YawDegrees(Band);
            /// <summary>World yaw in degrees of the blended storm side.</summary>
            public float StormYaw => YawDegrees(Storm);
        }

        public enum AccentKind { Gradient = 0, Override = 1 }

        /// <summary>#308 sub-region accent (TEST). Points are world XZ baked from WorldLocationCatalog centres by the editor command
        /// (Data cannot read the App catalog); PointIds keep the catalog ids for the check.</summary>
        [Serializable]
        public sealed class Accent
        {
            public string Id = "";
            public AccentKind Kind;
            [Tooltip("Gradient: realm whose weight scales the effect. Override: realm the state is pulled toward is TowardRealm.")]
            public RealmId Realm;
            public bool ScaleByRealmWeight = true;
            public string[] PointIds = Array.Empty<string>();
            public Vector2[] Points = Array.Empty<Vector2>();
            [Header("Gradient (values per point, interpolated by projection along the polyline)")]
            public float[] Brightness = Array.Empty<float>();   // zenith + cloud (wash and step-2 ink) multiplier
            public float[] CoverAdd = Array.Empty<float>();     // cloud density add (clamped to [0, .5]); step 2: Coverage add ([0, 1])
            public float[] MistAdd = Array.Empty<float>();      // horizon mist add (step-2 sky only)
            public float[] HorizonPull = Array.Empty<float>();  // horizon pulled toward PullColor
            public Color PullColor = new Color(.80f, .78f, .70f);
            [Min(0f)] public float SideWidth = 250f;
            [Min(0f)] public float SideFalloff = 120f;
            [Header("Override (circle around Points[0])")]
            public RealmId TowardRealm = RealmId.Hwanggyeong;
            [Range(0f, 1f)] public float Pull = .5f;
            [Min(0f)] public float Radius = 220f;
            [Min(0f)] public float Falloff = 120f;
        }

        [Min(1f)] public float BlendDistance = 1200f;
        [Min(0f)] public float ResponseSeconds = 2f;
        public RealmId DefaultRealm = RealmId.Hwanggyeong;
        public Entry[] Regions = Array.Empty<Entry>();

        [Header("#308 (TEST) — defaults keep the #297 behaviour")]
        [Tooltip("One-frame camera move above this (m) snaps to the target state. <= 0 disables the snap (first frame only).")]
        [Min(0f)] public float SnapDistance;
        [Tooltip("LDB-ATTRACTION capital anchor strength (decision range .02–.03). 0 keeps the shared sky profile's anchor untouched.")]
        [Range(0f, .08f)] public float CapitalAtmosphere;
        public RealmId CapitalRealm = RealmId.Hwanggyeong;
        public Accent[] Accents = Array.Empty<Accent>();

        /// <summary>True when every entry carries step-2 data (data:apply:step=2 ran).</summary>
        public bool HasShapeData
        {
            get
            {
                if (Regions == null || Regions.Length == 0) return false;
                for (int i = 0; i < Regions.Length; i++) if (Regions[i] == null || !Regions[i].HasShape) return false;
                return true;
            }
        }

        public SkyState Evaluate(WorldMacroSheetSO sheet, Vector3 position) => Evaluate(sheet, position, null);

        /// <summary>Allocation-free. <paramref name="weights"/> (length >= sheet.Regions.Length) receives the normalized region weights
        /// after the override accents; with a null or short buffer accents and the capital strength are skipped.</summary>
        public SkyState Evaluate(WorldMacroSheetSO sheet, Vector3 position, float[] weights)
        {
            SkyState fallback = FallbackState();
            if (weights != null) Array.Clear(weights, 0, weights.Length);
            if (sheet == null || sheet.Regions == null || !Finite(position.x) || !Finite(position.z)) return fallback;
            float maximum = MaximumDistance(sheet, position, sheet.Regions.Length);
            if (!Finite(maximum)) return fallback;

            bool useWeights = weights != null && weights.Length >= sheet.Regions.Length;
            float inverseScale = InverseScale(), total = 0f;
            SkyState sum = default;
            ShapeSum shape = default;
            // same arithmetic and order as #297 so a profile without #308 data renders identically (step-2 sums are separate)
            for (int i = 0; i < sheet.Regions.Length; i++)
            {
                var region = sheet.Regions[i];
                float d = Distance(region, position);
                if (!Finite(d)) continue;
                float weight = Mathf.Exp((d - maximum) * inverseScale);
                SkyState state = StateFor(region.Realm, fallback);
                sum.Horizon += state.Horizon * weight;
                sum.Zenith += state.Zenith * weight;
                sum.Cloud += state.Cloud * weight;
                sum.CloudDensity += state.CloudDensity * weight;
                shape.Add(ref state, weight);
                total += weight;
                if (useWeights) weights[i] = weight;
            }
            if (total <= 0f) return fallback;
            float inverseTotal = 1f / total;
            sum.Horizon *= inverseTotal;
            sum.Zenith *= inverseTotal;
            sum.Cloud *= inverseTotal;
            sum.CloudDensity *= inverseTotal;
            shape.Write(ref sum);
            if (!useWeights) return WithFog(sum, ref shape);
            for (int i = 0; i < sheet.Regions.Length; i++) weights[i] *= inverseTotal;
            if (Accents != null && Accents.Length > 0) sum = ApplyAccents(sheet, position, weights, sum, fallback, ref shape);
            sum = WithFog(sum, ref shape);
            if (CapitalAtmosphere > 0f) sum.Capital = CapitalAtmosphere * (1f - Mathf.Clamp01(RealmWeight(sheet, weights, CapitalRealm)));
            return sum;
        }

        /// <summary>Far fog = the (accented) horizon plus the weighted step-2 FogTint offsets. Without FogTint data the offset is an
        /// exact zero, so Fog == Horizon as in step 1.</summary>
        static SkyState WithFog(SkyState s, ref ShapeSum shape)
        {
            s.Fog = s.Horizon;
            if (shape.HasFog && shape.Total > 0f)
            {
                float k = 1f / shape.Total;
                s.Fog = new Color(s.Horizon.r + shape.FogDelta.r * k, s.Horizon.g + shape.FogDelta.g * k, s.Horizon.b + shape.FogDelta.b * k, s.Horizon.a);
            }
            return s;
        }

        /// <summary>Step-2 accumulator (stack only). Shape values are averaged over the entries that carry them; the fog offset over
        /// every weighted entry.</summary>
        struct ShapeSum
        {
            public Color CloudInk, FogDelta;
            public Vector2 Band, Storm;
            public float Coverage, Softness, Stretch, StormGain, WetEdge, CoreInk, Mist, ZenithFade, Weight, Total;
            public bool HasFog;

            public void Add(ref SkyState s, float w)
            {
                Total += w;
                // exact compare (Color == is approximate): FogOf returns the horizon itself when there is no tint
                if (s.Fog.r != s.Horizon.r || s.Fog.g != s.Horizon.g || s.Fog.b != s.Horizon.b)
                {
                    HasFog = true;
                    FogDelta.r += (s.Fog.r - s.Horizon.r) * w; FogDelta.g += (s.Fog.g - s.Horizon.g) * w; FogDelta.b += (s.Fog.b - s.Horizon.b) * w;
                }
                if (!(s.Shape > 0f)) return;
                float k = w * s.Shape;
                CloudInk += s.CloudInk * k;
                Coverage += s.Coverage * k; Softness += s.Softness * k; Stretch += s.Stretch * k;
                Band += s.Band * k; Storm += s.Storm * k;
                StormGain += s.StormGain * k; WetEdge += s.WetEdge * k; CoreInk += s.CoreInk * k;
                Mist += s.Mist * k; ZenithFade += s.ZenithFade * k;
                Weight += k;
            }

            /// <summary>Writes the averaged step-2 values (Mist is added: the accents add to it later). No-op without step-2 data.</summary>
            public void Write(ref SkyState s)
            {
                if (!(Weight > 0f)) return;
                float k = 1f / Weight;
                s.CloudInk = CloudInk * k;
                s.Coverage = Coverage * k; s.Softness = Softness * k; s.Stretch = Stretch * k;
                s.Band = Band * k; s.Storm = Storm * k;
                s.StormGain = StormGain * k; s.WetEdge = WetEdge * k; s.CoreInk = CoreInk * k;
                s.Mist += Mist * k; s.ZenithFade = ZenithFade * k;
                s.Shape = Total > 0f ? Mathf.Clamp01(Weight / Total) : 0f;
            }
        }

        SkyState ApplyAccents(WorldMacroSheetSO sheet, Vector3 position, float[] weights, SkyState sum, SkyState fallback, ref ShapeSum shape)
        {
            var p = new Vector2(position.x, position.z);
            // 1. overrides pull the realm weights (S08 고개 검문: toward 황경), then the state is re-summed from the weights
            bool changed = false;
            for (int a = 0; a < Accents.Length; a++)
            {
                var accent = Accents[a];
                if (accent == null || accent.Kind != AccentKind.Override || accent.Points == null || accent.Points.Length == 0) continue;
                float d = Vector2.Distance(p, accent.Points[0]);
                float spatial = 1f - Smooth(accent.Radius, accent.Radius + Mathf.Max(.001f, accent.Falloff), d);
                float pull = Mathf.Clamp01(accent.Pull) * spatial;
                if (accent.ScaleByRealmWeight) pull *= Mathf.Clamp01(RealmWeight(sheet, weights, accent.Realm));
                if (pull <= 0f) continue;
                float toward = RealmWeight(sheet, weights, accent.TowardRealm);
                int towardCount = 0;
                for (int i = 0; i < sheet.Regions.Length; i++) if (sheet.Regions[i] != null && sheet.Regions[i].Realm == accent.TowardRealm) towardCount++;
                if (towardCount == 0) continue;
                for (int i = 0; i < sheet.Regions.Length; i++)
                {
                    bool target = sheet.Regions[i] != null && sheet.Regions[i].Realm == accent.TowardRealm;
                    float share = !target ? 0f : toward > 1e-6f ? weights[i] / toward : 1f / towardCount;
                    weights[i] = weights[i] * (1f - pull) + pull * share;
                }
                changed = true;
            }
            if (changed)
            {
                SkyState resum = default;
                ShapeSum reshape = default;
                for (int i = 0; i < sheet.Regions.Length; i++)
                {
                    float w = weights[i];
                    if (w <= 0f || sheet.Regions[i] == null) continue;
                    SkyState state = StateFor(sheet.Regions[i].Realm, fallback);
                    resum.Horizon += state.Horizon * w;
                    resum.Zenith += state.Zenith * w;
                    resum.Cloud += state.Cloud * w;
                    resum.CloudDensity += state.CloudDensity * w;
                    reshape.Add(ref state, w);
                }
                sum.Horizon = resum.Horizon; sum.Zenith = resum.Zenith; sum.Cloud = resum.Cloud; sum.CloudDensity = resum.CloudDensity;
                // step-2 values follow the pulled weights too (the accent mist below is added afterwards)
                ClearShape(ref sum);
                reshape.Write(ref sum);
                shape = reshape;
            }
            // 2. gradients: value along the polyline (projection t), lateral falloff, scaled by their realm weight
            for (int a = 0; a < Accents.Length; a++)
            {
                var accent = Accents[a];
                if (accent == null || accent.Kind != AccentKind.Gradient || accent.Points == null || accent.Points.Length < 2) continue;
                float g = accent.ScaleByRealmWeight ? Mathf.Clamp01(RealmWeight(sheet, weights, accent.Realm)) : 1f;
                if (g <= 0f) continue;
                Project(accent.Points, p, out int segment, out float u, out float lateral);
                g *= 1f - Smooth(accent.SideWidth, accent.SideWidth + Mathf.Max(.001f, accent.SideFalloff), lateral);
                if (g <= 0f) continue;
                float brightness = Mathf.LerpUnclamped(1f, Sample(accent.Brightness, segment, u, 1f), g);
                sum.Zenith = Scale(sum.Zenith, brightness);
                sum.Cloud = Scale(sum.Cloud, brightness);
                float cover = Sample(accent.CoverAdd, segment, u, 0f) * g;
                sum.CloudDensity = Mathf.Clamp(sum.CloudDensity + cover, 0f, .5f);
                if (sum.Shape > 0f)
                {
                    sum.CloudInk = Scale(sum.CloudInk, brightness);
                    sum.Coverage = Mathf.Clamp01(sum.Coverage + cover);
                }
                sum.Mist += Sample(accent.MistAdd, segment, u, 0f) * g;
                float pullHorizon = Mathf.Clamp01(Sample(accent.HorizonPull, segment, u, 0f) * g);
                if (pullHorizon > 0f && Finite(accent.PullColor))
                {
                    Color h = Color.LerpUnclamped(sum.Horizon, accent.PullColor, pullHorizon);
                    sum.Horizon = new Color(h.r, h.g, h.b, sum.Horizon.a);
                }
            }
            return sum;
        }

        static void ClearShape(ref SkyState s)
        {
            s.CloudInk = default; s.Coverage = 0f; s.Softness = 0f; s.Stretch = 0f; s.Band = default; s.Storm = default;
            s.StormGain = 0f; s.WetEdge = 0f; s.CoreInk = 0f; s.Mist = 0f; s.ZenithFade = 0f; s.Shape = 0f;
        }

        static Color Scale(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / Mathf.Max(1e-4f, b - a));
            return t * t * (3f - 2f * t);
        }
        static float Sample(float[] values, int segment, float u, float fallback)
        {
            if (values == null || values.Length == 0) return fallback;
            int i = Mathf.Clamp(segment, 0, values.Length - 1), j = Mathf.Clamp(segment + 1, 0, values.Length - 1);
            float a = Finite(values[i]) ? values[i] : fallback, b = Finite(values[j]) ? values[j] : fallback;
            return Mathf.LerpUnclamped(a, b, Mathf.Clamp01(u));
        }

        /// <summary>Nearest point of the polyline: its segment, the parameter on that segment and the distance (lateral, or past an end).</summary>
        public static void Project(Vector2[] points, Vector2 p, out int segment, out float u, out float distance)
        {
            segment = 0; u = 0f; distance = float.PositiveInfinity;
            if (points == null || points.Length == 0) return;
            if (points.Length == 1) { distance = Vector2.Distance(p, points[0]); return; }
            for (int i = 0; i + 1 < points.Length; i++)
            {
                Vector2 a = points[i], d = points[i + 1] - a;
                float lengthSquared = d.sqrMagnitude;
                float t = lengthSquared > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / lengthSquared) : 0f;
                float e = (p - a - d * t).magnitude;
                if (e < distance) { distance = e; segment = i; u = t; }
            }
        }

        /// <summary>Summed weight of every region of one realm (weights in RegionSpec order).</summary>
        public static float RealmWeight(WorldMacroSheetSO sheet, float[] weights, RealmId realm)
        {
            if (sheet?.Regions == null || weights == null) return 0f;
            float w = 0f;
            int count = Mathf.Min(sheet.Regions.Length, weights.Length);
            for (int i = 0; i < count; i++) if (sheet.Regions[i] != null && sheet.Regions[i].Realm == realm) w += weights[i];
            return w;
        }

        /// <summary>Area centroid (XZ) of every polygon of one realm; false when the realm has no valid polygon.</summary>
        public static bool Centroid(WorldMacroSheetSO sheet, RealmId realm, out Vector2 centroid)
        {
            centroid = Vector2.zero;
            if (sheet?.Regions == null) return false;
            double area = 0d, cx = 0d, cz = 0d;
            foreach (var region in sheet.Regions)
            {
                var polygon = region?.Polygon;
                if (region == null || region.Realm != realm || polygon == null || polygon.Length < 3) continue;
                for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                {
                    double cross = (double)polygon[j].x * polygon[i].y - (double)polygon[i].x * polygon[j].y;
                    area += cross; cx += (polygon[j].x + polygon[i].x) * cross; cz += (polygon[j].y + polygon[i].y) * cross;
                }
            }
            if (Math.Abs(area) < 1e-6d) return false;
            centroid = new Vector2((float)(cx / (3d * area)), (float)(cz / (3d * area)));
            return true;
        }

        /// <summary>Azimuth in radians from +Z toward +X (InkCloudSky _CapitalAzimuth convention: dir.xz · (sin, cos)).</summary>
        public static float AzimuthTo(Vector3 from, Vector2 target)
        {
            float dx = target.x - from.x, dz = target.y - from.z;
            return dx * dx + dz * dz < 1e-6f ? 0f : Mathf.Atan2(dx, dz);
        }

        /// <summary>Unit vector (sin, cos) of a world yaw in degrees (InkWashSky300 band axis convention).</summary>
        public static Vector2 YawVector(float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
        }

        /// <summary>World yaw in degrees of a (sin, cos) vector, in (-180, 180]; 0 for a zero vector.</summary>
        public static float YawDegrees(Vector2 v) => v.x * v.x + v.y * v.y < 1e-12f ? 0f : Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;

        /// <summary>RegionSpec order. Allocate destination once with at least sheet.Regions.Length entries.</summary>
        public void GetWeights(WorldMacroSheetSO sheet, Vector3 position, float[] destination)
        {
            if (destination == null) return;
            Array.Clear(destination, 0, destination.Length);
            if (sheet == null || sheet.Regions == null || destination.Length == 0) return;
            // A truncated diagnostic buffer still receives normalized weights for its available entries.
            int count = Mathf.Min(sheet.Regions.Length, destination.Length);
            if (count == 0) return;
            float maximum = Finite(position.x) && Finite(position.z)
                ? MaximumDistance(sheet, position, count) : float.NegativeInfinity;
            if (!Finite(maximum))
            {
                int index = -1;
                for (int i = 0; i < count; i++)
                {
                    if (sheet.Regions[i] == null) continue;
                    if (index < 0) index = i;
                    if (sheet.Regions[i].Realm == DefaultRealm) { index = i; break; }
                }
                if (index >= 0) destination[index] = 1f;
                return;
            }
            float total = 0f, inverseScale = InverseScale();
            for (int i = 0; i < count; i++)
            {
                float d = Distance(sheet.Regions[i], position);
                if (!Finite(d)) continue;
                destination[i] = Mathf.Exp((d - maximum) * inverseScale);
                total += destination[i];
            }
            if (total > 0f) for (int i = 0; i < count; i++) destination[i] /= total;
        }

        /// <summary>Time blend. Colours and scalars lerp; the step-2 yaws lerp as unit vectors (Band, Storm).</summary>
        public static SkyState Blend(SkyState a, SkyState b, float t)
        {
            t = Finite(t) ? Mathf.Clamp01(t) : 0f;
            return new SkyState
            {
                Horizon = Color.LerpUnclamped(a.Horizon, b.Horizon, t),
                Zenith = Color.LerpUnclamped(a.Zenith, b.Zenith, t),
                Cloud = Color.LerpUnclamped(a.Cloud, b.Cloud, t),
                CloudDensity = Mathf.LerpUnclamped(a.CloudDensity, b.CloudDensity, t),
                Fog = Color.LerpUnclamped(a.Fog, b.Fog, t),
                Mist = Mathf.LerpUnclamped(a.Mist, b.Mist, t),
                Capital = Mathf.LerpUnclamped(a.Capital, b.Capital, t),
                CloudInk = Color.LerpUnclamped(a.CloudInk, b.CloudInk, t),
                Coverage = Mathf.LerpUnclamped(a.Coverage, b.Coverage, t),
                Softness = Mathf.LerpUnclamped(a.Softness, b.Softness, t),
                Stretch = Mathf.LerpUnclamped(a.Stretch, b.Stretch, t),
                StormGain = Mathf.LerpUnclamped(a.StormGain, b.StormGain, t),
                WetEdge = Mathf.LerpUnclamped(a.WetEdge, b.WetEdge, t),
                CoreInk = Mathf.LerpUnclamped(a.CoreInk, b.CoreInk, t),
                ZenithFade = Mathf.LerpUnclamped(a.ZenithFade, b.ZenithFade, t),
                Band = Vector2.LerpUnclamped(a.Band, b.Band, t),
                Storm = Vector2.LerpUnclamped(a.Storm, b.Storm, t),
                Shape = Mathf.LerpUnclamped(a.Shape, b.Shape, t)
            };
        }

        float InverseScale() => 2f / (Finite(BlendDistance) ? Mathf.Max(1f, BlendDistance) : 1200f);

        static float MaximumDistance(WorldMacroSheetSO sheet, Vector3 position, int count)
        {
            float maximum = float.NegativeInfinity;
            for (int i = 0; i < count; i++) maximum = Mathf.Max(maximum, Distance(sheet.Regions[i], position));
            return maximum;
        }

        static float Distance(WorldMacroSheetSO.RegionSpec region, Vector3 p)
        {
            var polygon = region == null ? null : region.Polygon;
            if (polygon == null || polygon.Length < 3) return float.NegativeInfinity;
            bool inside = false;
            float nearestSquared = float.PositiveInfinity;
            double signedArea = 0d;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[j], b = polygon[i];
                if (!Finite(a.x) || !Finite(a.y) || !Finite(b.x) || !Finite(b.y)) return float.NegativeInfinity;
                signedArea += (double)a.x * b.y - (double)b.x * a.y;
                if ((a.y > p.z) != (b.y > p.z) && p.x < (b.x - a.x) * (p.z - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
                float dx = b.x - a.x, dz = b.y - a.y;
                float lengthSquared = dx * dx + dz * dz;
                float t = lengthSquared > .000001f
                    ? Mathf.Clamp01(((p.x - a.x) * dx + (p.z - a.y) * dz) / lengthSquared) : 0f;
                float ex = p.x - a.x - t * dx, ez = p.z - a.y - t * dz;
                nearestSquared = Mathf.Min(nearestSquared, ex * ex + ez * ez);
            }
            if (Math.Abs(signedArea) < .000001d) return float.NegativeInfinity;
            float distance = Mathf.Sqrt(nearestSquared);
            return inside ? distance : -distance;
        }

        SkyState FallbackState()
        {
            SkyState basis = new SkyState
            {
                Horizon = new Color(.90f, .88f, .83f), Zenith = new Color(.73f, .75f, .72f),
                Cloud = new Color(.55f, .58f, .55f), CloudDensity = .14f
            };
            basis.Fog = basis.Horizon;
            if (Regions == null) return basis;
            Entry first = null;
            for (int i = 0; i < Regions.Length; i++)
            {
                if (Regions[i] == null) continue;
                if (first == null) first = Regions[i];
                if (Regions[i].Realm == DefaultRealm) return ReadState(Regions[i], basis);
            }
            return first == null ? basis : ReadState(first, basis);
        }

        SkyState StateFor(RealmId realm, SkyState fallback)
        {
            if (Regions != null)
                for (int i = 0; i < Regions.Length; i++)
                    if (Regions[i] != null && Regions[i].Realm == realm) return ReadState(Regions[i], fallback);
            return fallback;
        }

        static SkyState ReadState(Entry entry, SkyState fallback)
        {
            var s = new SkyState
            {
                Horizon = Finite(entry.Horizon) ? entry.Horizon : fallback.Horizon,
                Zenith = Finite(entry.Zenith) ? entry.Zenith : fallback.Zenith,
                Cloud = Finite(entry.Cloud) ? entry.Cloud : fallback.Cloud,
                CloudDensity = Finite(entry.CloudDensity) ? Mathf.Clamp(entry.CloudDensity, 0f, .5f) : fallback.CloudDensity
            };
            s.Fog = FogOf(entry.FogTint, s.Horizon);
            if (!entry.HasShape) return s;
            s.Shape = 1f;
            s.CloudInk = entry.CloudInk;
            s.Coverage = Mathf.Clamp01(entry.Coverage);
            s.Softness = Mathf.Clamp(entry.Softness, 0f, .4f);
            s.Stretch = entry.Stretch;
            s.Band = YawVector(entry.BandYaw);
            s.Storm = YawVector(entry.StormYaw);
            s.StormGain = Mathf.Clamp01(entry.StormGain);
            s.WetEdge = Mathf.Clamp01(entry.WetEdge);
            s.CoreInk = Mathf.Clamp01(entry.CoreInk);
            s.Mist = Mathf.Clamp(entry.MistHeight, 0f, .5f);
            s.ZenithFade = Mathf.Clamp01(entry.ZenithFade);
            return s;
        }

        /// <summary>FogTint.a = 0 (default) returns the horizon itself (exact, so step 1 is unchanged); otherwise the horizon mixed
        /// toward the tint by a (clamped to 1). Alpha stays the horizon's.</summary>
        static Color FogOf(Color tint, Color horizon)
        {
            if (!(tint.a > 0f) || !Finite(tint)) return horizon;
            float a = Mathf.Min(1f, tint.a);
            return new Color(horizon.r + (tint.r - horizon.r) * a, horizon.g + (tint.g - horizon.g) * a, horizon.b + (tint.b - horizon.b) * a, horizon.a);
        }

        static bool Finite(Color c) => Finite(c.r) && Finite(c.g) && Finite(c.b) && Finite(c.a);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
