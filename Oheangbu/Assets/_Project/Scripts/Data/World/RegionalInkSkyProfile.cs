using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    /// <summary>Continuous XZ climate blend. Cloud geometry and its clock stay in the shared sky profile.</summary>
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
        }

        [Serializable]
        public struct SkyState
        {
            public Color Horizon;
            public Color Zenith;
            public Color Cloud;
            public float CloudDensity;
        }

        [Min(1f)] public float BlendDistance = 1200f;
        [Min(0f)] public float ResponseSeconds = 2f;
        public RealmId DefaultRealm = RealmId.Hwanggyeong;
        public Entry[] Regions = Array.Empty<Entry>();

        public SkyState Evaluate(WorldMacroSheetSO sheet, Vector3 position)
        {
            SkyState fallback = FallbackState();
            if (sheet == null || sheet.Regions == null || !Finite(position.x) || !Finite(position.z)) return fallback;
            float maximum = MaximumDistance(sheet, position, sheet.Regions.Length);
            if (!Finite(maximum)) return fallback;

            float inverseScale = InverseScale(), total = 0f;
            SkyState sum = default;
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
                total += weight;
            }
            if (total <= 0f) return fallback;
            float inverseTotal = 1f / total;
            sum.Horizon *= inverseTotal;
            sum.Zenith *= inverseTotal;
            sum.Cloud *= inverseTotal;
            sum.CloudDensity *= inverseTotal;
            return sum;
        }

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

        public static SkyState Blend(SkyState a, SkyState b, float t)
        {
            t = Finite(t) ? Mathf.Clamp01(t) : 0f;
            return new SkyState
            {
                Horizon = Color.LerpUnclamped(a.Horizon, b.Horizon, t),
                Zenith = Color.LerpUnclamped(a.Zenith, b.Zenith, t),
                Cloud = Color.LerpUnclamped(a.Cloud, b.Cloud, t),
                CloudDensity = Mathf.LerpUnclamped(a.CloudDensity, b.CloudDensity, t)
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

        static SkyState ReadState(Entry entry, SkyState fallback) => new SkyState
        {
            Horizon = Finite(entry.Horizon) ? entry.Horizon : fallback.Horizon,
            Zenith = Finite(entry.Zenith) ? entry.Zenith : fallback.Zenith,
            Cloud = Finite(entry.Cloud) ? entry.Cloud : fallback.Cloud,
            CloudDensity = Finite(entry.CloudDensity) ? Mathf.Clamp(entry.CloudDensity, 0f, .5f) : fallback.CloudDensity
        };

        static bool Finite(Color c) => Finite(c.r) && Finite(c.g) && Finite(c.b) && Finite(c.a);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
