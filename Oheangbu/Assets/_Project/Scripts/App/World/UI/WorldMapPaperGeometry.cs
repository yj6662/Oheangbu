using System;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    /// <summary>
    /// A continuous sheet, folded once across each centre line. Coordinates use a
    /// one-unit sheet width, with positive Z towards the viewer. No Unity scene
    /// objects are needed to evaluate or export the paper.
    /// </summary>
    public static class WorldMapPaperGeometry
    {
        public const int Columns = 40;
        public const int Rows = 32;
        public const int VertexCount = (Columns + 1) * (Rows + 1);
        public const int TriangleCount = Columns * Rows * 2;
        public const float Perspective = 2.4f;

        public static void CreateGrid(Vector2[] uv, int[] triangles)
        {
            RequireLength(uv, VertexCount, nameof(uv));
            RequireLength(triangles, TriangleCount * 3, nameof(triangles));
            int vertex = 0;
            for (int row = 0; row <= Rows; row++)
                for (int column = 0; column <= Columns; column++)
                    uv[vertex++] = new Vector2(AxisCoordinate(column, Columns), AxisCoordinate(row, Rows));

            int index = 0;
            for (int row = 0; row < Rows; row++)
                for (int column = 0; column < Columns; column++)
                {
                    int a = row * (Columns + 1) + column;
                    int b = a + 1;
                    int c = a + Columns + 1;
                    int d = c + 1;
                    triangles[index++] = a; triangles[index++] = b; triangles[index++] = d;
                    triangles[index++] = a; triangles[index++] = d; triangles[index++] = c;
                }
        }

        public static void GetFoldAngles(float progress, out float verticalRadians, out float horizontalRadians)
        {
            verticalRadians = Mathf.PI * (1f - Ease(.14f, .54f, progress));
            horizontalRadians = Mathf.PI * (1f - Ease(.54f, .93f, progress));
        }

        public static void Evaluate(float progress, float heightOverWidth, Vector2[] uv,
            Vector3[] positions, Vector3[] normals)
        {
            RequireLength(uv, VertexCount, nameof(uv));
            RequireLength(positions, VertexCount, nameof(positions));
            RequireLength(normals, VertexCount, nameof(normals));
            var state = new FoldState(progress, heightOverWidth);
            for (int i = 0; i < VertexCount; i++)
            {
                positions[i] = EvaluatePoint(uv[i], state);
                normals[i] = Vector3.zero;
            }

            // Area-weighted normals retain the narrow curved fold and tiny wrinkles.
            for (int row = 0; row < Rows; row++)
                for (int column = 0; column < Columns; column++)
                {
                    int a = row * (Columns + 1) + column;
                    int b = a + 1;
                    int c = a + Columns + 1;
                    int d = c + 1;
                    AccumulateNormal(a, b, d, positions, normals);
                    AccumulateNormal(a, d, c, positions, normals);
                }
            for (int i = 0; i < VertexCount; i++)
            {
                // The deliberately dense crease cells have very small cross
                // products. Vector3.normalized treats lengths below 1e-5 as zero,
                // although these are valid normals in our one-unit coordinate scale.
                float squaredLength = normals[i].sqrMagnitude;
                normals[i] = squaredLength > 1e-20f
                    ? normals[i] * (1f / Mathf.Sqrt(squaredLength))
                    : Vector3.forward;
            }
        }

        public static Vector3 EvaluatePoint(Vector2 uv, float progress, float heightOverWidth)
            => EvaluatePoint(uv, new FoldState(progress, heightOverWidth));

        public static Vector2 Project(Vector3 position, float perspective = Perspective)
        {
            float scale = perspective / Mathf.Max(.2f, perspective - position.z);
            return new Vector2(position.x * scale, position.y * scale);
        }

        static Vector3 EvaluatePoint(Vector2 uv, FoldState state)
        {
            float u = Mathf.Clamp01(uv.x), v = Mathf.Clamp01(uv.y);
            float x = u - .5f, y = (v - .5f) * state.Aspect;

            // The frayed outline is deterministic and only affects the outer 1%.
            // Both folds sample this same surface, so their shared edges never split.
            float xEdge = Mathf.Exp(-Mathf.Min(u, 1f - u) * 170f);
            float yEdge = Mathf.Exp(-Mathf.Min(v, 1f - v) * 170f);
            x -= Mathf.Sign(x) * .0023f * xEdge * EdgeGrain(v, 1.7f);
            y -= Mathf.Sign(y) * .0023f * yEdge * EdgeGrain(u, 4.1f);

            float relief = RestRelief(u, v) * state.Relief;
            float horizontalZ, foldedY;
            Bend(y, relief, state.Horizontal, state.HorizontalWidth, out foldedY, out horizontalZ);
            float foldedX, foldedZ;
            // The outer vertical hinge carries the already folded horizontal half.
            // Its larger bend radius encloses both layers instead of intersecting them.
            Bend(x, horizontalZ, state.Vertical, state.VerticalWidth, out foldedX, out foldedZ);

            var point = new Vector3(foldedX + state.CenterX, foldedY + state.CenterY, foldedZ);
            point = state.Pose * point * state.Scale;
            point.y += state.EnterOffset;
            return point;
        }

        static void Bend(float coordinate, float normalOffset, float angle, float halfWidth,
            out float along, out float raised)
        {
            if (angle < .00001f)
            {
                along = coordinate;
                raised = normalOffset;
                return;
            }
            float turn;
            if (coordinate <= -halfWidth)
            {
                along = coordinate;
                raised = normalOffset;
                return;
            }

            // Integrating the rotating tangent gives a circular neutral surface,
            // not a scaled card: arc length through the crease is conserved.
            float radius = 2f * halfWidth / angle;
            if (coordinate < halfWidth)
            {
                turn = angle * (coordinate + halfWidth) / (2f * halfWidth);
                along = -halfWidth + radius * Mathf.Sin(turn);
                raised = radius * (1f - Mathf.Cos(turn));
            }
            else
            {
                turn = angle;
                float remainder = coordinate - halfWidth;
                along = -halfWidth + radius * Mathf.Sin(turn) + remainder * Mathf.Cos(turn);
                raised = radius * (1f - Mathf.Cos(turn)) + remainder * Mathf.Sin(turn);
            }
            along -= normalOffset * Mathf.Sin(turn);
            raised += normalOffset * Mathf.Cos(turn);
        }

        static float RestRelief(float u, float v)
        {
            float fibres = Mathf.Sin(u * 33f + Mathf.Sin(v * 14f) * 1.3f) *
                Mathf.Sin(v * 27f + u * 6f) * .00055f;
            float diagonalA = v - (.18f + u * .43f);
            float diagonalB = v - (.89f - u * .38f);
            float wrinkles = Mathf.Exp(-diagonalA * diagonalA * 3900f) * .0016f * Mathf.Sin(u * 9f) +
                Mathf.Exp(-diagonalB * diagonalB * 2600f) * .0011f * Mathf.Cos(u * 13f);
            float edgeDistance = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
            float edgeCurl = Mathf.Exp(-edgeDistance * 65f) *
                (.0008f + .0013f * Mathf.Sin(u * 17f + v * 21f));
            float creaseMemory = .00065f * Mathf.Exp(-(u - .5f) * (u - .5f) * 8000f) +
                .0005f * Mathf.Exp(-(v - .5f) * (v - .5f) * 8000f);
            return fibres + wrinkles + edgeCurl + creaseMemory;
        }

        static float EdgeGrain(float coordinate, float seed)
            => .53f + .23f * Mathf.Sin(coordinate * 161f + seed) +
               .15f * Mathf.Sin(coordinate * 317f + seed * 3f) +
               .09f * Mathf.Sin(coordinate * 557f + seed * 7f);

        static float AxisCoordinate(int index, int divisions)
        {
            float signed = 2f * index / divisions - 1f;
            // Extra samples near both centre creases capture their bending radii.
            return .5f + .5f * Mathf.Sign(signed) * signed * signed;
        }

        static float Ease(float from, float to, float value)
        {
            float t = Mathf.Clamp01((value - from) / (to - from));
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        static void AccumulateNormal(int a, int b, int c, Vector3[] positions, Vector3[] normals)
        {
            Vector3 normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            normals[a] += normal; normals[b] += normal; normals[c] += normal;
        }

        static void RequireLength(Array array, int length, string name)
        {
            if (array == null || array.Length < length)
                throw new ArgumentException("The paper buffer must contain at least " + length + " entries.", name);
        }

        readonly struct FoldState
        {
            public readonly float Aspect, Vertical, Horizontal, VerticalWidth, HorizontalWidth;
            public readonly float CenterX, CenterY, Relief, Scale, EnterOffset;
            public readonly Quaternion Pose;

            public FoldState(float progress, float heightOverWidth)
            {
                float p = Mathf.Clamp01(progress);
                Aspect = Mathf.Max(.2f, heightOverWidth);
                GetFoldAngles(p, out float vertical, out float horizontal);
                Vertical = vertical; Horizontal = horizontal;
                HorizontalWidth = .008f * Mathf.Min(1f, Aspect);
                VerticalWidth = .027f * Mathf.Min(1f, Aspect);
                CenterX = .125f * (1f - Mathf.Cos(vertical));
                CenterY = .125f * Aspect * (1f - Mathf.Cos(horizontal));
                Relief = .15f + .85f * Ease(.72f, 1f, p);
                Scale = .9f + .1f * Ease(0f, .2f, p);
                EnterOffset = -.15f * Aspect * (1f - Ease(0f, .14f, p));
                float settle = 1f - Ease(.87f, 1f, p);
                float flutter = Mathf.Sin(Mathf.Clamp01((p - .87f) / .13f) * Mathf.PI * 2f) * settle;
                Pose = Quaternion.Euler(-5.5f * settle + 1.2f * flutter,
                    6f * Mathf.Sin(p * Mathf.PI) * (1f - p),
                    -8f * (1f - Ease(.14f, .93f, p)));
            }
        }
    }
}
