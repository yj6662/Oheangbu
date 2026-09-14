using UnityEngine;

namespace Oheangbu.App.World.UI
{
    public readonly struct WorldMapProjection
    {
        public readonly Vector2 BoundsMin;
        public readonly Vector2 BoundsMax;

        public WorldMapProjection(Vector2 boundsMin, Vector2 boundsMax)
        {
            BoundsMin = boundsMin;
            BoundsMax = boundsMax;
        }

        public bool IsValid => BoundsMax.x > BoundsMin.x && BoundsMax.y > BoundsMin.y;

        public Vector2 WorldToNormalized(Vector2 worldXZ)
        {
            return new Vector2(
                Mathf.InverseLerp(BoundsMin.x, BoundsMax.x, worldXZ.x),
                Mathf.InverseLerp(BoundsMin.y, BoundsMax.y, worldXZ.y));
        }

        public Vector2 NormalizedToWorld(Vector2 normalized)
        {
            return new Vector2(
                Mathf.LerpUnclamped(BoundsMin.x, BoundsMax.x, normalized.x),
                Mathf.LerpUnclamped(BoundsMin.y, BoundsMax.y, normalized.y));
        }

        public Vector2 WorldToRect(Vector2 worldXZ, Rect rect)
        {
            Vector2 n = WorldToNormalized(worldXZ);
            return new Vector2(rect.xMin + rect.width * n.x, rect.yMin + rect.height * n.y);
        }

        public Rect WorldWindow(Vector2 centerWorldXZ, float halfExtentMetres)
        {
            Vector2 n = WorldToNormalized(centerWorldXZ);
            float width = halfExtentMetres * 2f / (BoundsMax.x - BoundsMin.x);
            float height = halfExtentMetres * 2f / (BoundsMax.y - BoundsMin.y);
            return ClampUvRect(new Rect(n.x - width * .5f, n.y - height * .5f, width, height));
        }

        public static Rect ClampUvRect(Rect value)
        {
            value.width = Mathf.Clamp(value.width, .001f, 1f);
            value.height = Mathf.Clamp(value.height, .001f, 1f);
            value.x = Mathf.Clamp(value.x, 0f, 1f - value.width);
            value.y = Mathf.Clamp(value.y, 0f, 1f - value.height);
            return value;
        }

        public static bool Approximately(Vector2 a, Vector2 b, float tolerance = .001f)
            => (a - b).sqrMagnitude <= tolerance * tolerance;
    }
}
