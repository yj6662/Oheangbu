using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>
    /// SPEC-DEV-CODEX-WORLD: a low, broad matbae-roofed inn, facing local -Z.
    /// The supplied palette materials own the look; this builder adds no colors or lights.
    /// All dimensions are comparison-scene construction values, persisted in the scene.
    /// </summary>
    public static class CodexWorldArchitecture
    {
        private const float HalfWidth = 5.6f;
        private const float RoofHalfWidth = 7f;
        private const float RoofHalfDepth = 4.65f;
        private const float PorchLevel = 0.25f;
        private const float WallTop = 3.95f;

        public static GameObject BuildInn(Transform parent, Vector3 position, float yaw,
            Material wood, Material plaster, Material roof, Material lantern, string meshAssetFolder)
        {
            if (string.IsNullOrWhiteSpace(meshAssetFolder) ||
                !meshAssetFolder.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("An Assets/ mesh folder is required.", nameof(meshAssetFolder));

            EnsureFolder(meshAssetFolder);
            var root = new GameObject("JoseonInn");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            var structure = new GameObject("TimberStructure").transform;
            structure.SetParent(root.transform, false);
            var walls = new GameObject("PaperAndPlaster").transform;
            walls.SetParent(root.transform, false);
            var roofGroup = new GameObject("CurvedMatbaeRoof").transform;
            roofGroup.SetParent(root.transform, false);

            // A single 0.25 m step is within the existing CharacterController's step offset.
            // Front porch remains open and the fourth bay is a real walk-through doorway.
            Cube(structure, "Foundation", new Vector3(0f, 0.085f, 0f),
                new Vector3(12.0f, 0.17f, 8.25f), plaster, true);
            Cube(structure, "WoodenPorchAndRoomFloor", new Vector3(0f, 0.21f, 0f),
                new Vector3(11.75f, 0.08f, 8.0f), wood, true);
            Cube(structure, "Threshold", new Vector3(1.4f, 0.275f, -0.3f),
                new Vector3(2.55f, 0.05f, 0.25f), wood, true);

            var joinery = new MeshParts();
            var rafters = new MeshParts();
            var roofMesh = new MeshParts();
            var tileMesh = new MeshParts();
            var gableMesh = new MeshParts();

            float[] columns = { -HalfWidth, -2.8f, 0f, 2.8f, HalfWidth };
            foreach (float x in columns)
            {
                Cube(structure, "FrontPorchPost", new Vector3(x, 2.07f, -3.5f),
                    new Vector3(0.22f, 3.64f, 0.22f), wood, true);
                Cube(structure, "FacadePost", new Vector3(x, 2.07f, -0.3f),
                    new Vector3(0.18f, 3.64f, 0.18f), wood, true);
                Cube(structure, "RearPost", new Vector3(x, 2.07f, 3.5f),
                    new Vector3(0.22f, 3.64f, 0.22f), wood, true);

                joinery.Box(new Vector3(x, 0.335f, -3.5f), new Vector3(0.36f, 0.17f, 0.36f));
                joinery.Box(new Vector3(x, 3.77f, -3.5f), new Vector3(0.48f, 0.19f, 0.48f));
                joinery.Box(new Vector3(x, 3.9f, 0f), new Vector3(0.19f, 0.22f, 7.65f));
                // Small diagonal brackets, quiet enough for the broad silhouette to dominate.
                joinery.Beam(new Vector3(x, 3.2f, -3.5f),
                    new Vector3(x, 3.72f, -2.98f), 0.10f, 0.11f);
            }

            joinery.Box(new Vector3(0f, 3.87f, -3.5f), new Vector3(11.9f, 0.25f, 0.24f));
            joinery.Box(new Vector3(0f, 3.87f, -0.3f), new Vector3(11.8f, 0.24f, 0.22f));
            joinery.Box(new Vector3(0f, 3.87f, 3.5f), new Vector3(11.9f, 0.25f, 0.24f));
            joinery.Box(new Vector3(0f, 0.47f, 3.52f), new Vector3(11.25f, 0.15f, 0.2f));
            Cube(walls, "RearPlasterWall", new Vector3(0f, 2.05f, 3.52f),
                new Vector3(11.2f, 3.60f, 0.16f), plaster, true);

            for (int bay = 0; bay < 4; bay++)
            {
                float x = -4.2f + 2.8f * bay;
                if (bay != 2)
                {
                    Cube(walls, "PlasterSill_" + bay, new Vector3(x, 0.805f, -0.29f),
                        new Vector3(2.62f, 1.11f, 0.15f), plaster, true);
                    Cube(walls, "PaperWindow_" + bay, new Vector3(x, 2.48f, -0.32f),
                        new Vector3(2.56f, 2.11f, 0.09f), plaster, true);
                    AddLattice(joinery, new Vector3(x, 2.48f, -0.385f), 2.6f, 2.15f, 6, 5);
                }
                else
                {
                    // A narrow stacked sliding panel leaves a 2.2 m opening into the room.
                    Cube(walls, "OpenSlidingDoor", new Vector3(2.48f, 1.88f, -0.39f),
                        new Vector3(0.4f, 3.18f, 0.10f), plaster, true);
                    AddLattice(joinery, new Vector3(2.48f, 1.88f, -0.46f), 0.44f, 3.2f, 1, 7);
                }
                Cube(walls, "FacadeTransom_" + bay, new Vector3(x, 3.68f, -0.31f),
                    new Vector3(2.62f, 0.23f, 0.12f), plaster, false);
            }

            // Side walls close the room only; the veranda can be entered from both sides.
            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * HalfWidth;
                Cube(walls, side < 0f ? "WestSideWall" : "EastSideWall",
                    new Vector3(x, 2.05f, 1.6f), new Vector3(0.16f, 3.60f, 3.8f), plaster, true);
                joinery.Box(new Vector3(x + side * 0.09f, 1.38f, 1.6f), new Vector3(0.08f, 0.10f, 3.7f));
                joinery.Box(new Vector3(x + side * 0.09f, 3.55f, 1.6f), new Vector3(0.08f, 0.10f, 3.7f));
                for (int j = 0; j < 7; j++)
                    joinery.Box(new Vector3(x + side * 0.095f, 2.46f, -0.10f + j * 0.56f),
                        new Vector3(0.07f, 2.17f, 0.045f));

                AddGable(gableMesh, side);
                // Exposed cross-beams and the king-post retain a Korean timber silhouette.
                joinery.Box(new Vector3(x + side * 0.12f, 4.02f, 0f), new Vector3(0.18f, 0.20f, 7.75f));
                joinery.Box(new Vector3(x + side * 0.12f, 5.22f, 0f), new Vector3(0.16f, 2.30f, 0.17f));
                joinery.Beam(new Vector3(x + side * 0.12f, 4.08f, -3.45f),
                    new Vector3(x + side * 0.12f, 6.32f, 0f), 0.12f, 0.15f);
                joinery.Beam(new Vector3(x + side * 0.12f, 6.32f, 0f),
                    new Vector3(x + side * 0.12f, 4.08f, 3.45f), 0.12f, 0.15f);
            }

            AddRoofSurface(roofMesh, -1f);
            AddRoofSurface(roofMesh, 1f);

            // Twenty-five full-depth tile ridges share one mesh and one renderer.
            // The cylindrical crowns produce small shade breaks without textured tiles.
            const int tileRibs = 25;
            for (int rib = 0; rib < tileRibs; rib++)
            {
                float x = Mathf.Lerp(-RoofHalfWidth + 0.075f, RoofHalfWidth - 0.075f, rib / (tileRibs - 1f));
                var points = new List<Vector3>();
                const int steps = 32;
                for (int j = 0; j <= steps; j++)
                {
                    float z = Mathf.Lerp(-RoofHalfDepth, RoofHalfDepth, j / (float)steps);
                    points.Add(new Vector3(x, RoofHeight(x, Mathf.Abs(z) / RoofHalfDepth) + 0.045f, z));
                }
                tileMesh.Tube(points, 0.061f, 6);
            }

            // A restrained clay ridge cap with slightly raised terminals, not a pagoda finial.
            var ridge = new List<Vector3>();
            for (int i = 0; i <= 24; i++)
            {
                float x = Mathf.Lerp(-7.08f, 7.08f, i / 24f);
                ridge.Add(new Vector3(x, 6.70f + 0.13f * Mathf.Pow(Mathf.Abs(x) / 7.08f, 10f), 0f));
            }
            tileMesh.Tube(ridge, 0.14f, 8);

            // Eave fascia follows the sweep and conceals the thin mesh perimeter.
            foreach (float side in new[] { -1f, 1f })
            {
                var edge = new List<Vector3>();
                for (int i = 0; i <= 28; i++)
                {
                    float x = Mathf.Lerp(-RoofHalfWidth, RoofHalfWidth, i / 28f);
                    edge.Add(new Vector3(x, RoofHeight(x, 1f) - 0.075f, side * RoofHalfDepth));
                }
                tileMesh.Tube(edge, 0.11f, 6);
            }

            // Visible rafter tails sit beneath the eaves and are batched into one wood mesh.
            for (int rafter = 0; rafter < 19; rafter++)
            {
                float x = Mathf.Lerp(-6.6f, 6.6f, rafter / 18f);
                foreach (float side in new[] { -1f, 1f })
                {
                    var points = new List<Vector3>();
                    for (int i = 0; i <= 6; i++)
                    {
                        float v = Mathf.Lerp(0.66f, 1.02f, i / 6f);
                        points.Add(new Vector3(x, RoofHeight(x, v) - 0.19f, side * RoofHalfDepth * v));
                    }
                    rafters.Tube(points, 0.09f, 6);
                }
            }

            // Quiet floor joints and hand-height porch rails on the outer ends only.
            for (int i = 0; i < 17; i++)
                joinery.Box(new Vector3(-5.44f + i * 0.68f, PorchLevel + 0.003f, -1.84f),
                    new Vector3(0.014f, 0.007f, 3.52f));
            foreach (float side in new[] { -1f, 1f })
            {
                joinery.Box(new Vector3(side * HalfWidth, 1.00f, -1.91f), new Vector3(0.10f, 0.12f, 2.85f));
                for (int i = 0; i < 4; i++)
                    joinery.Box(new Vector3(side * HalfWidth, 0.69f, -3.02f + i * 0.70f),
                        new Vector3(0.065f, 0.71f, 0.065f));
            }

            // One hanging lantern: its material, not an arbitrary point light, supplies the warm cue.
            var lanternRoot = new GameObject("PorchLantern_Attraction").transform;
            lanternRoot.SetParent(root.transform, false);
            lanternRoot.localPosition = new Vector3(2.30f, 3.11f, -3.57f);
            Cube(lanternRoot, "PaperLantern", Vector3.zero, new Vector3(0.38f, 0.64f, 0.38f), lantern, false);
            var lanternFrame = new MeshParts();
            lanternFrame.Box(new Vector3(0f, 0.34f, 0f), new Vector3(0.45f, 0.065f, 0.45f));
            lanternFrame.Box(new Vector3(0f, -0.34f, 0f), new Vector3(0.43f, 0.055f, 0.43f));
            lanternFrame.Box(new Vector3(0f, 0.55f, 0f), new Vector3(0.03f, 0.4f, 0.03f));
            foreach (float x in new[] { -0.2f, 0.2f })
                foreach (float z in new[] { -0.2f, 0.2f })
                    lanternFrame.Box(new Vector3(x, 0f, z), new Vector3(0.035f, 0.64f, 0.035f));

            MeshObject(structure, "JoinedTimberAndLattice", joinery, wood, meshAssetFolder, "Inn_TimberDetails");
            MeshObject(roofGroup, "RoofWash", roofMesh, roof, meshAssetFolder, "Inn_RoofSurface");
            MeshObject(roofGroup, "RoundedTileRibsAndRidge", tileMesh, roof, meshAssetFolder, "Inn_RoofTileRibs");
            MeshObject(roofGroup, "ExposedRafterTails", rafters, wood, meshAssetFolder, "Inn_Rafters");
            MeshObject(walls, "TwoFilledGables", gableMesh, plaster, meshAssetFolder, "Inn_Gables");
            MeshObject(lanternRoot, "LanternTimberFrame", lanternFrame, wood, meshAssetFolder, "Inn_LanternFrame");
            return root;
        }

        private static float RoofHeight(float x, float v)
        {
            // A gently descending slope flattens, then turns up only at its outermost edge.
            float endSweep = 0.30f * Mathf.Pow(Mathf.Abs(x) / RoofHalfWidth, 8f) * v * v;
            return 6.6f - 3.2f * v + 0.75f * Mathf.Pow(v, 4f) + endSweep;
        }

        private static void AddRoofSurface(MeshParts mesh, float side)
        {
            const int across = 40;
            const int down = 18;
            int start = mesh.Vertices.Count;
            for (int row = 0; row <= down; row++)
            {
                float v = row / (float)down;
                for (int column = 0; column <= across; column++)
                {
                    float u = column / (float)across;
                    float x = Mathf.Lerp(-RoofHalfWidth, RoofHalfWidth, u);
                    mesh.Vertex(new Vector3(x, RoofHeight(x, v), side * RoofHalfDepth * v), new Vector2(u, v));
                }
            }
            for (int row = 0; row < down; row++)
            {
                for (int column = 0; column < across; column++)
                {
                    int a = start + row * (across + 1) + column;
                    int b = a + 1;
                    int c = a + across + 2;
                    int d = a + across + 1;
                    if (side < 0f) mesh.QuadIndices(a, b, c, d);
                    else mesh.QuadIndices(d, c, b, a);
                }
            }
        }

        private static void AddGable(MeshParts mesh, float side)
        {
            // Explicit side faces keep the attic closed when the player walks around the inn.
            const int steps = 24;
            float x = side * (HalfWidth + 0.025f);
            for (int i = 0; i < steps; i++)
            {
                float z0 = Mathf.Lerp(-3.85f, 3.85f, i / (float)steps);
                float z1 = Mathf.Lerp(-3.85f, 3.85f, (i + 1f) / steps);
                var a = new Vector3(x, WallTop, z0);
                var b = new Vector3(x, WallTop, z1);
                var c = new Vector3(x, RoofHeight(x, Mathf.Abs(z1) / RoofHalfDepth) - 0.16f, z1);
                var d = new Vector3(x, RoofHeight(x, Mathf.Abs(z0) / RoofHalfDepth) - 0.16f, z0);
                if (side < 0f) mesh.Quad(a, b, c, d);
                else mesh.Quad(d, c, b, a);
            }
        }

        private static void AddLattice(MeshParts mesh, Vector3 center, float width, float height, int columns, int rows)
        {
            mesh.Box(center + Vector3.left * (width * 0.5f), new Vector3(0.08f, height + 0.10f, 0.07f));
            mesh.Box(center + Vector3.right * (width * 0.5f), new Vector3(0.08f, height + 0.10f, 0.07f));
            mesh.Box(center + Vector3.up * (height * 0.5f), new Vector3(width + 0.08f, 0.08f, 0.07f));
            mesh.Box(center + Vector3.down * (height * 0.5f), new Vector3(width + 0.08f, 0.08f, 0.07f));
            for (int i = 1; i < columns; i++)
                mesh.Box(center + Vector3.right * Mathf.Lerp(-width * 0.5f, width * 0.5f, i / (float)columns),
                    new Vector3(0.038f, height, 0.045f));
            for (int i = 1; i < rows; i++)
                mesh.Box(center + Vector3.up * Mathf.Lerp(-height * 0.5f, height * 0.5f, i / (float)rows),
                    new Vector3(width, 0.038f, 0.045f));
        }

        private static GameObject Cube(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static void MeshObject(Transform parent, string name, MeshParts parts, Material material, string folder, string assetName)
        {
            var mesh = parts.ToMesh(assetName);
            string path = folder.TrimEnd('/') + "/" + assetName + ".asset";
            var persisted = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (persisted != null)
            {
                EditorUtility.CopySerialized(mesh, persisted);
                Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(persisted);
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, path);
                persisted = mesh;
            }
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = persisted;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static void EnsureFolder(string folder)
        {
            folder = folder.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folder)) return;
            int separator = folder.LastIndexOf('/');
            if (separator <= 0) throw new ArgumentException("Invalid asset folder: " + folder);
            string parent = folder.Substring(0, separator);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(separator + 1));
        }

        private sealed class MeshParts
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            private readonly List<Vector2> _uv = new List<Vector2>();
            private readonly List<int> _triangles = new List<int>();

            public void Vertex(Vector3 point, Vector2 uv)
            {
                Vertices.Add(point);
                _uv.Add(uv);
            }

            public void QuadIndices(int a, int b, int c, int d)
            {
                _triangles.Add(a); _triangles.Add(b); _triangles.Add(c);
                _triangles.Add(a); _triangles.Add(c); _triangles.Add(d);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int start = Vertices.Count;
                Vertex(a, Vector2.zero); Vertex(b, Vector2.right);
                Vertex(c, Vector2.one); Vertex(d, Vector2.up);
                QuadIndices(start, start + 1, start + 2, start + 3);
            }

            public void Box(Vector3 center, Vector3 size)
            {
                Box(center, size, Quaternion.identity);
            }

            private void Box(Vector3 center, Vector3 size, Quaternion rotation)
            {
                Vector3 h = size * 0.5f;
                var p = new Vector3[8];
                p[0] = center + rotation * new Vector3(-h.x, -h.y, -h.z);
                p[1] = center + rotation * new Vector3(h.x, -h.y, -h.z);
                p[2] = center + rotation * new Vector3(h.x, h.y, -h.z);
                p[3] = center + rotation * new Vector3(-h.x, h.y, -h.z);
                p[4] = center + rotation * new Vector3(-h.x, -h.y, h.z);
                p[5] = center + rotation * new Vector3(h.x, -h.y, h.z);
                p[6] = center + rotation * new Vector3(h.x, h.y, h.z);
                p[7] = center + rotation * new Vector3(-h.x, h.y, h.z);
                Quad(p[3], p[2], p[1], p[0]);
                Quad(p[4], p[5], p[6], p[7]);
                Quad(p[0], p[4], p[7], p[3]);
                Quad(p[2], p[6], p[5], p[1]);
                Quad(p[7], p[6], p[2], p[3]);
                Quad(p[0], p[1], p[5], p[4]);
            }

            public void Beam(Vector3 a, Vector3 b, float width, float height)
            {
                Vector3 direction = b - a;
                Box((a + b) * 0.5f, new Vector3(width, height, direction.magnitude), Quaternion.LookRotation(direction));
            }

            public void Tube(List<Vector3> points, float radius, int sides)
            {
                int start = Vertices.Count;
                for (int p = 0; p < points.Count; p++)
                {
                    Vector3 tangent = (points[Mathf.Min(p + 1, points.Count - 1)] - points[Mathf.Max(p - 1, 0)]).normalized;
                    Vector3 helper = Mathf.Abs(Vector3.Dot(tangent, Vector3.right)) > 0.9f ? Vector3.forward : Vector3.right;
                    Vector3 up = Vector3.Cross(tangent, helper).normalized;
                    Vector3 right = Vector3.Cross(up, tangent).normalized;
                    for (int s = 0; s < sides; s++)
                    {
                        float angle = s * Mathf.PI * 2f / sides;
                        Vector3 radial = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                        Vertex(points[p] + radial * radius, new Vector2(s / (float)sides, p / (float)(points.Count - 1)));
                    }
                }
                for (int p = 0; p < points.Count - 1; p++)
                {
                    for (int s = 0; s < sides; s++)
                    {
                        int next = (s + 1) % sides;
                        int a = start + p * sides + s;
                        int b = start + p * sides + next;
                        int c = start + (p + 1) * sides + next;
                        int d = start + (p + 1) * sides + s;
                        QuadIndices(a, b, c, d);
                    }
                }
                // Eave ends are visible from eye level: close each tube instead of leaving dark holes.
                for (int cap = 0; cap < 2; cap++)
                {
                    int ring = start + (cap == 0 ? 0 : points.Count - 1) * sides;
                    int center = Vertices.Count;
                    Vertex(points[cap == 0 ? 0 : points.Count - 1], new Vector2(0.5f, 0.5f));
                    for (int s = 0; s < sides; s++)
                    {
                        int next = (s + 1) % sides;
                        _triangles.Add(center);
                        _triangles.Add(ring + (cap == 0 ? next : s));
                        _triangles.Add(ring + (cap == 0 ? s : next));
                    }
                }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                if (Vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(Vertices);
                mesh.SetUVs(0, _uv);
                var colors = new List<Color>(Vertices.Count);
                for (int i = 0; i < Vertices.Count; i++) colors.Add(Color.white);
                mesh.SetColors(colors);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
