using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // Every invocation touches one selected profile only. Existing materials and all
    // gameplay/native motif fields remain intact; original profile bytes are saved.
    public static class Vfx120MetalDetailBuilder
    {
        const string MeshFolder = "Assets/_Project/Art/SpellVFX120/Meshes/MetalDetails";
        [Serializable] sealed class Entry
        {
            public string glyph, profile, originalSnapshot, beforeJson, afterJson;
            public string previousBodyMesh, previousBodyMeshGuid, bodyMesh, bodyMeshGuid;
            public string retainedMaterial, retainedMaterialGuid;
            public int count, vertices, triangles, vertexBuffers;
            public Vector3 boundsSize, boundsCenter, worldDimensions;
        }
        [Serializable] sealed class Report
        {
            public string status = "AUTHORED_REQUIRES_UNITY_RENDER_REVIEW";
            public string generatedUtc;
            public string attachmentCheck;
            public string changedFields = "BodyMesh, Count, PartScale, RibbonCount only";
            public string contract = "PartScale is per-piece local-axis world dimensions. Motion helper preserves top-anchor pivots. No predicted trajectory, extra buff stack or gameplay event is fabricated. Original materials/native motifs and all timings retained.";
            public Entry[] entries;
        }

        public static string Build() => BuildSingle("손");
        public static string BuildChime() => BuildSingle("석");
        public static string BuildFocusArcs() => BuildSingle("섬");
        public static string BuildSpeedFeathers() => BuildSingle("선");
        private static string BuildSingle(string glyph)
        {
            bool chime = glyph == "석", arcs = glyph == "섬", feathers = glyph == "선";
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Edit Mode required.");
            var catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot + "/Data/VFX120_Catalog.asset");
            if (catalog == null) throw new InvalidOperationException("VFX120 catalog is missing.");
            string[] glyphs = { glyph };
            string[] ids = { feathers ? "081_C120" : arcs ? "082_C12C" : chime ? "080_C11D" : "087_C190" };
            Vector3[] dimensions = { feathers ? new Vector3(.035f, .006f, .23f) : arcs ? new Vector3(.56f, .22f, .014f) :
                chime ? new Vector3(.15f, .20f, .018f) : new Vector3(.19f, .15f, .024f) };
            int[] counts = { feathers ? 2 : arcs ? 1 : 3 };
            var profiles = glyphs.Select(g => catalog.Entries.Single(e => e.Glyph == g).Profile).ToArray();
            foreach (var p in profiles)
                if (p == null || !p.Assigned || p.BodyMesh == null || p.BodyMaterial == null)
                    throw new InvalidOperationException("Assigned metal profile, original mesh and material required.");

            EnsureFolder(MeshFolder);
            Directory.CreateDirectory(Vfx120Editor.Output);
            string snapshotFolder = Path.Combine(Vfx120Editor.Output, "MetalDetailOriginals");
            Directory.CreateDirectory(snapshotFolder);
            Mesh[] meshes = { feathers ? SpeedFeather() : arcs ? OpenArcs() : chime ? Chime() : OpenFerrule() };
            string[] suffixes = { feathers ? "081_SpeedFeather" : arcs ? "082_OpenArcs" : chime ? "080_Chime" : "087_OpenFerrule" };
            for (int i = 0; i < meshes.Length; i++)
                meshes[i] = SaveMesh(meshes[i], MeshFolder + "/" + Vfx120MetalDetailMotion.MeshPrefix + suffixes[i] + ".asset",
                    Vfx120MetalDetailMotion.MeshPrefix + suffixes[i]);

            var records = new Entry[profiles.Length];
            for (int i = 0; i < profiles.Length; i++)
            {
                var p = profiles[i];
                string path = AssetDatabase.GetAssetPath(p);
                string snapshot = Path.Combine(snapshotFolder, ids[i] + ".asset.txt");
                string physicalPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, path);
                if (!File.Exists(snapshot)) File.Copy(physicalPath, snapshot, false);
                string originalMesh = AssetDatabase.GetAssetPath(p.BodyMesh);
                var entry = new Entry { glyph = p.Glyph, profile = path, originalSnapshot = snapshot,
                    beforeJson = JsonUtility.ToJson(p), previousBodyMesh = originalMesh,
                    previousBodyMeshGuid = AssetDatabase.AssetPathToGUID(originalMesh),
                    retainedMaterial = AssetDatabase.GetAssetPath(p.BodyMaterial),
                    worldDimensions = dimensions[i], count = counts[i] };
                entry.retainedMaterialGuid = AssetDatabase.AssetPathToGUID(entry.retainedMaterial);
                string unchanged = UnchangedFields(p);
                p.BodyMesh = meshes[i]; p.Count = counts[i]; p.PartScale = dimensions[i]; p.RibbonCount = 0;
                if (unchanged != UnchangedFields(p))
                    throw new InvalidOperationException("Unexpected metal profile field changed: " + p.Glyph);
                EditorUtility.SetDirty(p); AssetDatabase.SaveAssetIfDirty(p);
                entry.afterJson = JsonUtility.ToJson(p);
                entry.bodyMesh = AssetDatabase.GetAssetPath(meshes[i]);
                entry.bodyMeshGuid = AssetDatabase.AssetPathToGUID(entry.bodyMesh);
                entry.vertices = meshes[i].vertexCount; entry.triangles = meshes[i].triangles.Length / 3;
                entry.vertexBuffers = meshes[i].vertexBufferCount;
                entry.boundsSize = meshes[i].bounds.size; entry.boundsCenter = meshes[i].bounds.center;
                records[i] = entry;
            }
            File.WriteAllText(Path.Combine(Vfx120Editor.Output, feathers ? "metal_detail_081_build.json" : arcs ? "metal_detail_082_build.json" :
                chime ? "metal_detail_080_build.json" : "metal_detail_build.json"),
                JsonUtility.ToJson(new Report { generatedUtc = DateTime.UtcNow.ToString("o"), entries = records,
                    attachmentCheck = feathers ? CheckFeatherAttachment(profiles[0]) : "NOT_APPLICABLE" }, true));
            return "AUTHORED_" + ids[0] + "_AWAITING_USER_VISUAL_REVIEW";
        }

        static string UnchangedFields(Vfx120Profile source)
        {
            var clone = UnityEngine.Object.Instantiate(source);
            clone.name = source.name;
            clone.BodyMesh = null; clone.Count = 0; clone.PartScale = Vector3.zero; clone.RibbonCount = 0;
            string json = JsonUtility.ToJson(clone);
            UnityEngine.Object.DestroyImmediate(clone);
            return json;
        }

        static Mesh OpenFerrule()
        {
            var shape = new Shape();
            shape.Arc(Vector2.zero, new Vector2(.50f, .39f), 55f, 305f, .047f, .048f, 32, false);
            return shape.Finish();
        }
        static Mesh SpeedFeather()
        {
            var shape = new Shape();
            // Small asymmetric notches along a narrow metal vane, rather than the
            // previous broad leaf/shard silhouette. Local +Z is the shot direction.
            shape.Prism(new[] { new Vector2(0, .50f), new Vector2(.10f, .27f),
                new Vector2(.36f, .13f), new Vector2(.25f, .08f), new Vector2(.43f, -.03f),
                new Vector2(.30f, -.08f), new Vector2(.46f, -.21f), new Vector2(.26f, -.34f),
                new Vector2(.04f, -.50f), new Vector2(-.15f, -.39f), new Vector2(-.34f, -.22f),
                new Vector2(-.23f, -.16f), new Vector2(-.36f, -.07f), new Vector2(-.24f, 0),
                new Vector2(-.32f, .11f), new Vector2(-.11f, .30f) }, .02f);
            var mesh = shape.Finish(); var q = Quaternion.Euler(90, 0, 0);
            mesh.vertices = mesh.vertices.Select(v => q * v).ToArray(); mesh.RecalculateBounds(); return mesh;
        }
        static string CheckFeatherAttachment(Vfx120Profile p)
        {
            Vector3 shift = new Vector3(.7f, .2f, 1.8f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 a = Vector3.zero, b = Vector3.zero, local = Vector3.zero, axis = Vector3.forward, scale = Vector3.one;
                Vfx120MetalDetailMotion.TryApply(p, 1, p.Flight, p.Duration, i, 2, Vector3.forward * 4,
                    ref a, ref local, ref axis, ref scale, p.Size, -1, -1, true, Vector3.forward, Vector3.forward, .4f);
                Vfx120MetalDetailMotion.TryApply(p, 1, p.Flight, p.Duration, i, 2, Vector3.forward * 4,
                    ref b, ref local, ref axis, ref scale, p.Size, -1, -1, true, Vector3.forward + shift, Vector3.forward, .4f);
                if ((b - a - shift).sqrMagnitude > .0000001f || Vector3.Dot(axis, Vector3.forward) < .999f)
                    throw new InvalidOperationException("Feathers do not follow the supplied anchor in parallel");
            }
            return "PASS_SUPPLIED_ANCHOR_TRANSLATION_AND_PARALLEL_AXES_ONLY";
        }
        static Mesh OpenArcs()
        {
            var shape = new Shape();
            // Two broken, unequal side strokes; no closed ring or trajectory line.
            shape.Arc(new Vector2(-.012f, .010f), new Vector2(.27f, .12f), 112f, 248f, .0044f, .007f, 24, true);
            shape.Arc(new Vector2(.008f, -.010f), new Vector2(.25f, .10f), -64f, 59f, .0044f, .007f, 24, true);
            return shape.Finish();
        }
        static Mesh Chime()
        {
            var shape = new Shape();
            // An angled, hollow-corner chime silhouette. (0,0,0) is the fixed top
            // suspension point, so micro-swing cannot turn it into an orbiting blade.
            var outline = new[] { new Vector2(0f, 0f), new Vector2(.13f, -.09f),
                new Vector2(.43f, -.41f), new Vector2(.31f, -.53f),
                new Vector2(-.01f, -.21f), new Vector2(-.33f, -.44f),
                new Vector2(-.43f, -.30f), new Vector2(-.10f, -.065f) };
            shape.Prism(outline, .025f);
            return shape.Finish();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        static Mesh SaveMesh(Mesh created, string path, string name)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = created; AssetDatabase.CreateAsset(mesh, path); }
            else
            {
                // Native setters are required: CopySerialized previously left the
                // catalog Ribbon with vertexBufferCount=0 despite valid CPU arrays.
                mesh.Clear(); mesh.SetVertices(created.vertices); mesh.SetUVs(0, created.uv);
                mesh.SetColors(created.colors); mesh.SetTriangles(created.triangles, 0);
                UnityEngine.Object.DestroyImmediate(created);
            }
            mesh.name = name; mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            Validate(mesh);
            EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh);
            return mesh;
        }
        static void Validate(Mesh mesh)
        {
            if (mesh.vertexCount < 3 || mesh.vertexBufferCount < 1 || mesh.subMeshCount != 1)
                throw new InvalidOperationException("Incomplete native metal geometry: " + mesh.name);
            Vector3[] v = mesh.vertices; int[] t = mesh.triangles;
            foreach (Vector3 x in v)
                if (float.IsNaN(x.x + x.y + x.z) || float.IsInfinity(x.x + x.y + x.z))
                    throw new InvalidOperationException("Non-finite metal vertex.");
            for (int i = 0; i < t.Length; i += 3)
                if (t[i] < 0 || t[i] >= v.Length || t[i + 1] < 0 || t[i + 1] >= v.Length ||
                    t[i + 2] < 0 || t[i + 2] >= v.Length ||
                    Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).sqrMagnitude < 1e-16f)
                    throw new InvalidOperationException("Invalid or degenerate metal triangle.");
        }

        sealed class Shape
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<Color> colors = new List<Color>();
            readonly List<int> indices = new List<int>();
            int Add(Vector3 p, Vector2 texture)
            { int i = vertices.Count; vertices.Add(p); uv.Add(texture); colors.Add(Color.white); return i; }
            void Tri(int a, int b, int c) { indices.Add(a); indices.Add(b); indices.Add(c); }
            public void Arc(Vector2 center, Vector2 radius, float start, float end,
                float width, float depth, int steps, bool tapered)
            {
                const int sides = 8;
                int first = vertices.Count;
                for (int j = 0; j <= steps; j++)
                {
                    float u = j / (float)steps, angle = Mathf.Lerp(start, end, u) * Mathf.Deg2Rad;
                    Vector2 radial = new Vector2(Mathf.Cos(angle) / radius.x, Mathf.Sin(angle) / radius.y).normalized;
                    Vector2 point = center + new Vector2(Mathf.Cos(angle) * radius.x, Mathf.Sin(angle) * radius.y);
                    float taper = tapered ? Mathf.Lerp(.42f, 1f, Mathf.Sin(u * Mathf.PI)) : 1f;
                    for (int s = 0; s < sides; s++)
                    {
                        float a = s * Mathf.PI * 2f / sides;
                        Vector2 xy = point + radial * (Mathf.Cos(a) * width * taper);
                        Add(new Vector3(xy.x, xy.y, Mathf.Sin(a) * depth * taper), new Vector2(u, s / (float)sides));
                    }
                }
                for (int j = 0; j < steps; j++) for (int s = 0; s < sides; s++)
                {
                    int a = first + j * sides + s, b = first + j * sides + (s + 1) % sides;
                    Tri(a, a + sides, b); Tri(b, a + sides, b + sides);
                }
                for (int s = 1; s < sides - 1; s++)
                {
                    Tri(first, first + s, first + s + 1);
                    int last = first + steps * sides;
                    Tri(last, last + s + 1, last + s);
                }
            }
            public void Prism(Vector2[] polygon, float halfDepth)
            {
                // Ear clipping retains the intentionally concave elbow.
                int n = polygon.Length, first = vertices.Count;
                float area = 0f;
                for (int i = 0; i < n; i++) area += Cross(polygon[i], polygon[(i + 1) % n]);
                if (area < 0) Array.Reverse(polygon);
                for (int side = 0; side < 2; side++) for (int i = 0; i < n; i++)
                    Add(new Vector3(polygon[i].x, polygon[i].y, side == 0 ? -halfDepth : halfDepth), polygon[i]);
                var remaining = Enumerable.Range(0, n).ToList();
                int guard = n * n;
                while (remaining.Count > 2 && guard-- > 0)
                {
                    bool clipped = false;
                    for (int j = 0; j < remaining.Count; j++)
                    {
                        int a = remaining[(j + remaining.Count - 1) % remaining.Count], b = remaining[j],
                            c = remaining[(j + 1) % remaining.Count];
                        if (Cross(polygon[b] - polygon[a], polygon[c] - polygon[b]) <= .0000001f) continue;
                        bool inside = false;
                        foreach (int k in remaining)
                            if (k != a && k != b && k != c && InTriangle(polygon[k], polygon[a], polygon[b], polygon[c]))
                            { inside = true; break; }
                        if (inside) continue;
                        Tri(first + a, first + c, first + b);
                        Tri(first + n + a, first + n + b, first + n + c);
                        remaining.RemoveAt(j); clipped = true; break;
                    }
                    if (!clipped) throw new InvalidOperationException("Chime outline cannot be triangulated.");
                }
                for (int i = 0; i < n; i++)
                {
                    int a = first + i, b = first + (i + 1) % n;
                    Tri(a, b, a + n); Tri(b, b + n, a + n);
                }
            }
            static float Cross(Vector2 a, Vector2 b) { return a.x * b.y - a.y * b.x; }
            static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
            { return Cross(b - a, p - a) >= 0 && Cross(c - b, p - b) >= 0 && Cross(a - c, p - c) >= 0; }
            public Mesh Finish()
            {
                var mesh = new Mesh();
                mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetColors(colors); mesh.SetTriangles(indices, 0);
                mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
