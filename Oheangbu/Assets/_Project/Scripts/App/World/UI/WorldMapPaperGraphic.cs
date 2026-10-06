using System;
using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    /// <summary>
    /// Perspective projection of a real, tessellated folded sheet into a uGUI rect.
    /// UV0 addresses the whole paper. UV1.x is 0 on the printed face and 1 on the
    /// reverse; UV1.y is the fold amount and UV1.z is the pre-projection depth.
    /// A single reusable mesh supports both faces without a camera or render target.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WorldMapPaperGraphic : MaskableGraphic
    {
        [SerializeField] Texture paperTexture;
        [SerializeField, Range(0f, 1f)] float progress = 1f;

        Vector2[] paperUv;
        Vector3[] positions;
        Vector3[] normals;
        Vector2[] projected;
        int[] indices;
        PaperTriangle[] triangleOrder;
        float[] quadrantDepth;
        float evaluatedProgress = -1f;
        float evaluatedAspect = -1f;
        float frontVisibility = 1f;

        public Texture PaperTexture
        {
            get => paperTexture;
            set
            {
                if (paperTexture == value) return;
                paperTexture = value;
                SetMaterialDirty();
            }
        }

        /// <summary>D308-25: falloff multipliers of the fold crease shade along x / y. The shade was tuned as a share of an
        /// 800 x 820 sheet (7.3 px); a sheet of another size passes (its width / 800, its height / 820) so the crease keeps that
        /// width in px. (1, 1) = the shade as it always was.</summary>
        public Vector2 CreaseScale
        {
            get => creaseScale;
            set { if (creaseScale == value) return; creaseScale = value; SetVerticesDirty(); }
        }
        Vector2 creaseScale = Vector2.one;

        public float Progress => progress;
        public int GeometryVertexCount => WorldMapPaperGeometry.VertexCount;
        public int GeometryTriangleCount => WorldMapPaperGeometry.TriangleCount;

        /// <summary>Projected fraction facing the viewer before layer occlusion.</summary>
        public float FrontVisibility { get { EnsureGeometry(); return frontVisibility; } }

        public override Texture mainTexture => paperTexture != null ? paperTexture : s_WhiteTexture;

        public void Configure(Texture paper, Color tint)
        {
            PaperTexture = paper;
            color = tint;
            raycastTarget = false;
            EnableVertexChannels();
            SetVerticesDirty();
        }

        public void SetProgress(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(progress, value)) return;
            progress = value;
            SetVerticesDirty();
        }

        /// <summary>Returns the local rect position for an original full-sheet UV.</summary>
        public Vector2 ProjectPoint(Vector2 paperUvPoint)
        {
            Rect rect = rectTransform.rect;
            if (rect.width <= 0f || rect.height <= 0f) return rect.center;
            Vector3 point = WorldMapPaperGeometry.EvaluatePoint(paperUvPoint, progress, rect.height / rect.width);
            return rect.center + WorldMapPaperGeometry.Project(point) * rect.width;
        }

        /// <summary>Copies the evaluated 3D sheet for diagnostics or a standalone OBJ export.</summary>
        public void CopyGeometry(Vector3[] vertexBuffer, Vector3[] normalBuffer, Vector2[] uvBuffer, int[] triangleBuffer)
        {
            EnsureGeometry();
            Array.Copy(positions, vertexBuffer, WorldMapPaperGeometry.VertexCount);
            Array.Copy(normals, normalBuffer, WorldMapPaperGeometry.VertexCount);
            Array.Copy(paperUv, uvBuffer, WorldMapPaperGeometry.VertexCount);
            Array.Copy(indices, triangleBuffer, WorldMapPaperGeometry.TriangleCount * 3);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            raycastTarget = false;
            EnableVertexChannels();
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            EnableVertexChannels();
        }

        void EnableVertexChannels()
        {
            Canvas owner = canvas;
            if (owner != null) owner.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            if (rect.width <= 0f || rect.height <= 0f) return;
            EnsureGeometry();
            int count = WorldMapPaperGeometry.VertexCount;
            Vector3 light = new Vector3(-.32f, .48f, .82f).normalized;
            float foldAmount = 1f - progress;
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < count; i++)
                {
                    Vector3 normal = side == 0 ? normals[i] : -normals[i];
                    float lighting = .72f + .28f * Mathf.Clamp01(Vector3.Dot(normal, light));
                    float crease = CreaseShade(paperUv[i], foldAmount, creaseScale);
                    float shade = lighting * crease * (side == 0 ? 1f : .89f);
                    var vertex = UIVertex.simpleVert;
                    Vector2 screen = rect.center + projected[i] * rect.width;
                    vertex.position = new Vector3(screen.x, screen.y, 0f);
                    vertex.normal = normal;
                    vertex.color = new Color(color.r * shade, color.g * shade, color.b * shade, color.a);
                    vertex.uv0 = new Vector4(paperUv[i].x, paperUv[i].y, 0f, 0f);
                    vertex.uv1 = new Vector4(side, foldAmount, positions[i].z, 0f);
                    vh.AddVert(vertex);
                }

            // UI materials generally use Cull Off and ZWrite Off. Submit only the
            // viewer-facing side, with whole folded quadrants ordered far-to-near.
            // Individual triangles remain depth-sorted inside their own quadrant.
            for (int i = 0; i < triangleOrder.Length; i++)
            {
                PaperTriangle triangle = triangleOrder[i];
                int first = triangle.Index * 3;
                int offset = triangle.Back ? count : 0;
                int a = indices[first] + offset;
                int b = indices[first + 1] + offset;
                int c = indices[first + 2] + offset;
                if (triangle.Back) vh.AddTriangle(a, c, b);
                else vh.AddTriangle(a, b, c);
            }
        }

        void EnsureGeometry()
        {
            if (positions == null)
            {
                int count = WorldMapPaperGeometry.VertexCount;
                paperUv = new Vector2[count]; positions = new Vector3[count]; normals = new Vector3[count];
                projected = new Vector2[count];
                indices = new int[WorldMapPaperGeometry.TriangleCount * 3];
                triangleOrder = new PaperTriangle[WorldMapPaperGeometry.TriangleCount];
                quadrantDepth = new float[4];
                WorldMapPaperGeometry.CreateGrid(paperUv, indices);
            }
            Rect rect = rectTransform.rect;
            float aspect = rect.width > 0f ? Mathf.Max(.2f, rect.height / rect.width) : 1f;
            if (Mathf.Approximately(evaluatedProgress, progress) && Mathf.Approximately(evaluatedAspect, aspect)) return;
            evaluatedProgress = progress; evaluatedAspect = aspect;
            WorldMapPaperGeometry.Evaluate(progress, aspect, paperUv, positions, normals);
            for (int i = 0; i < positions.Length; i++)
                projected[i] = WorldMapPaperGeometry.Project(positions[i]);

            // Nearly parallel folded layers have offset grids. Comparing every
            // triangle's centroid globally lets a covered layer's neighboring
            // triangles jump in front, producing bright triangular wedges. Keep
            // each continuous quadrant together before sorting its own triangles.
            for (int quadrant = 0; quadrant < 4; quadrant++)
            {
                var centerUv = new Vector2((quadrant & 1) == 0 ? .25f : .75f,
                    (quadrant & 2) == 0 ? .25f : .75f);
                quadrantDepth[quadrant] = WorldMapPaperGeometry.EvaluatePoint(centerUv, progress, aspect).z;
            }

            float frontArea = 0f, totalArea = 0f;
            for (int i = 0; i < triangleOrder.Length; i++)
            {
                int first = i * 3;
                int a = indices[first], b = indices[first + 1], c = indices[first + 2];
                Vector2 ab = projected[b] - projected[a], ac = projected[c] - projected[a];
                float signedArea = ab.x * ac.y - ab.y * ac.x;
                bool back = signedArea < 0f;
                float area = Mathf.Abs(signedArea);
                if (!back) frontArea += area;
                totalArea += area;
                int cell = i / 2;
                int column = cell % WorldMapPaperGeometry.Columns;
                int row = cell / WorldMapPaperGeometry.Columns;
                int quadrant = (column < WorldMapPaperGeometry.Columns / 2 ? 0 : 1) |
                    (row < WorldMapPaperGeometry.Rows / 2 ? 0 : 2);
                triangleOrder[i] = new PaperTriangle
                {
                    Index = i, Back = back, Quadrant = quadrant, GroupDepth = quadrantDepth[quadrant],
                    Depth = (positions[a].z + positions[b].z + positions[c].z) / 3f
                };
            }
            frontVisibility = totalArea > .000001f ? frontArea / totalArea : 0f;
            SortTriangles(0, triangleOrder.Length - 1);
        }

        static float CreaseShade(Vector2 uv, float fold, Vector2 scale)
        {
            float vertical = Mathf.Exp(-Mathf.Abs(uv.x - .5f) * 110f * Mathf.Max(.01f, scale.x));
            float horizontal = Mathf.Exp(-Mathf.Abs(uv.y - .5f) * 110f * Mathf.Max(.01f, scale.y));
            float grain = Mathf.Sin(uv.x * 139f + uv.y * 51f) * Mathf.Sin(uv.y * 113f) * .012f;
            return Mathf.Clamp01(1f - (vertical + horizontal) * (.07f + fold * .13f) + grain);
        }

        void SortTriangles(int left, int right)
        {
            // In-place quicksort has no delegates, temporary arrays, or frame GC.
            while (left < right)
            {
                int low = left, high = right;
                PaperTriangle pivot = triangleOrder[(left + right) >> 1];
                while (low <= high)
                {
                    while (ComesBefore(triangleOrder[low], pivot)) low++;
                    while (ComesBefore(pivot, triangleOrder[high])) high--;
                    if (low <= high)
                    {
                        PaperTriangle swap = triangleOrder[low];
                        triangleOrder[low] = triangleOrder[high]; triangleOrder[high] = swap;
                        low++; high--;
                    }
                }
                // Recurse into the smaller partition to bound stack use.
                if (high - left < right - low)
                {
                    if (left < high) SortTriangles(left, high);
                    left = low;
                }
                else
                {
                    if (low < right) SortTriangles(low, right);
                    right = high;
                }
            }
        }

        static bool ComesBefore(PaperTriangle a, PaperTriangle b)
        {
            if (a.GroupDepth != b.GroupDepth) return a.GroupDepth < b.GroupDepth;
            if (a.Quadrant != b.Quadrant) return a.Quadrant < b.Quadrant;
            return a.Depth < b.Depth || (a.Depth == b.Depth && a.Index < b.Index);
        }

        struct PaperTriangle
        {
            public int Index;
            public int Quadrant;
            public float Depth;
            public float GroupDepth;
            public bool Back;
        }
    }
}
