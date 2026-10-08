using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 editor memory fixes (SPEC-EDITOR-MEMORY-308). Every write is a targeted save of the mesh assets it names; no SaveAssets flush.
    //   rw:probe:<mesh asset path>     — turn Read/Write off on ONE mesh asset and report its size before and after
    //   rw:off:<folder>[:dry]          — turn Read/Write off on the mesh assets under a folder; meshes a MeshCollider in the open scene uses are skipped
    //   rw:on:<list file>              — turn it back on for the asset paths in a list file (the list rw:off wrote)
    //   dupes:apply[:dry]              — in the open scene, point every MeshFilter / MeshCollider that uses a copy at the kept mesh, then save that scene only
    //   scene:open:<path> | scene:     — open one scene alone (refused while unsaved changes exist) | name the open scene
    //   colliders:simplify:<cell>[:<floor>[:dry]] | tangents:audit[:<path part>]
    //   tex:audit | tex:cap:<max>[:dry] | tex:uncap:<list file>
    //   mesh:pack:<folder | mesh asset>[:dry] — smaller vertex layout, same geometry
    //   unload:                        — unload unused assets (so the copies leave memory without a restart)
    //   dupes:scan:<folder>            — group the folder's mesh assets by content (vertex + index data + submeshes); writes logs/mem/dupes.json
    public static class MemoryFix308
    {
        static string Logs => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Tools/Unity/Stage308_ops/logs/mem"));

        public static string Run(string command)
        {
            string[] a = (command ?? "").Split(':');
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: edit mode only";
            switch (a[0] + ":" + (a.Length > 1 ? a[1] : ""))
            {
                case "rw:probe": return Probe(a[2]);
                case "rw:off": return ReadWriteOff(a[2], a.Length > 3 && a[3] == "dry");
                case "rw:on": return ReadWriteOn(a[2]);
                case "dupes:scan": return Dupes(a[2]);
                case "dupes:apply": return DupesApply(a.Length > 2 && a[2] == "dry");
                case "scene:open": return OpenScene(a[2]);
                case "scene:": return UnityEngine.SceneManagement.SceneManager.GetActiveScene().path + " dirty " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty;
                case "colliders:simplify": return SimplifyColliders(float.Parse(a[2], CultureInfo.InvariantCulture), a.Length > 3 ? int.Parse(a[3], CultureInfo.InvariantCulture) : 50000, a.Length > 4 && a[4] == "dry");
                case "tangents:audit": return TangentAudit(a.Length > 2 ? a[2] : "");
                case "tex:audit": return TexAudit();
                case "tex:cap": return TexCap(int.Parse(a[2], CultureInfo.InvariantCulture), a.Length > 3 && a[3] == "dry");
                case "tex:uncap": return TexUncap(a[2]);
                case "mesh:pack": return MeshPack(a[2], a.Length > 3 && a[3] == "dry", a.Length > 3 && a[3] == "one");
                case "unload:": EditorUtility.UnloadUnusedAssetsImmediate(true); GC.Collect(); return "unloaded unused assets";
                default: return "commands: rw:probe:<asset> | rw:off:<folder>[:dry] | rw:on:<list file> | dupes:scan:<folder>";
            }
        }

        static string Mb(long b) => (b / 1048576.0).ToString("F1", CultureInfo.InvariantCulture) + " MB";

        static bool SetReadable(Mesh mesh, bool readable)
        {
            var so = new SerializedObject(mesh);
            SerializedProperty p = so.FindProperty("m_IsReadable");
            if (p == null || p.boolValue == readable) return false;
            p.boolValue = readable;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(mesh);
            return true;
        }

        static string Probe(string path)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) return "refused: no mesh at " + path;
            long before = Profiler.GetRuntimeMemorySizeLong(mesh); bool was = mesh.isReadable;
            bool changed = SetReadable(mesh, false);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            return path + "\nreadable " + was + " -> " + mesh.isReadable + " (changed " + changed + ") | " + Mb(before) + " -> " + Mb(Profiler.GetRuntimeMemorySizeLong(mesh)) + " | vertices " + mesh.vertexCount;
        }

        static IEnumerable<string> MeshAssets(string folder) =>
            AssetDatabase.FindAssets("t:Mesh", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)).Distinct();

        static string ReadWriteOff(string folder, bool dry)
        {
            var collider = new HashSet<Mesh>(Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(c => c.sharedMesh).Where(m => m != null));
            int done = 0, skippedCollider = 0, already = 0; long bytes = 0; var list = new List<string>();
            foreach (string path in MeshAssets(folder))
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null) continue;
                if (!mesh.isReadable) { already++; continue; }
                if (collider.Contains(mesh)) { skippedCollider++; continue; }
                bytes += Profiler.GetRuntimeMemorySizeLong(mesh); done++; list.Add(path);
                if (!dry) SetReadable(mesh, false);
            }
            if (!dry)
            {
                Directory.CreateDirectory(Logs);
                File.WriteAllLines(Path.Combine(Logs, "rw_off_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt"), list);
                AssetDatabase.Refresh();
            }
            return (dry ? "DRY " : "") + "folder " + folder + " | turned off " + done + " (" + Mb(bytes) + " before) | skipped collider " + skippedCollider + " | already off " + already;
        }

        static string ReadWriteOn(string listFile)
        {
            int n = 0;
            foreach (string path in File.ReadAllLines(listFile)) { var m = AssetDatabase.LoadAssetAtPath<Mesh>(path.Trim()); if (m != null && SetReadable(m, true)) n++; }
            AssetDatabase.Refresh();
            return "turned back on " + n;
        }

        // scene:open:<path> — open one scene alone in the editor; refuses while the open scene has unsaved changes
        static string OpenScene(string path)
        {
            var now = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (now.isDirty) return "refused: " + now.path + " has unsaved changes";
            var s = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Single);
            EditorUtility.UnloadUnusedAssetsImmediate(true); GC.Collect();
            return "open " + s.path;
        }

        // colliders:simplify:<cell m>[:<min vertices>[:dry]] — collider meshes above the vertex floor are rebuilt by vertex clustering on a grid
        // (positions only, degenerate and repeated triangles dropped), saved under Architecture296/Meshes/Collision308, and the open scene's
        // MeshColliders are pointed at them. Writes logs/mem/colliders.json (old path -> new path) for the other scenes and for undoing.
        static string SimplifyColliders(float cell, int floor, bool dry)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.isDirty && !dry) return "refused: " + scene.path + " has unsaved changes";
            const string outFolder = "Assets/_Project/Art/World/Architecture296/Meshes/Collision308";
            var made = new Dictionary<Mesh, Mesh>(); var lines = new List<string>(); long before = 0, after = 0; int repointed = 0; long vBefore = 0, vAfter = 0;
            foreach (var c in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Mesh m = c.sharedMesh;
                if (m == null || m.vertexCount < floor) continue;
                string path = AssetDatabase.GetAssetPath(m);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) continue;
                if (!made.TryGetValue(m, out Mesh simple))
                {
                    simple = Cluster(m, cell);
                    string tag = "_c" + Mathf.RoundToInt(cell * 100f);
                    simple.name = m.name + tag;
                    before += Profiler.GetRuntimeMemorySizeLong(m); vBefore += m.vertexCount; vAfter += simple.vertexCount;
                    string outPath = outFolder + "/" + Path.GetFileNameWithoutExtension(path) + tag + ".asset";
                    if (!dry)
                    {
                        if (!AssetDatabase.IsValidFolder(outFolder)) AssetDatabase.CreateFolder("Assets/_Project/Art/World/Architecture296/Meshes", "Collision308");
                        AssetDatabase.CreateAsset(simple, outPath);
                    }
                    after += Profiler.GetRuntimeMemorySizeLong(simple);
                    lines.Add("  {\"old\": \"" + path + "\", \"new\": \"" + outPath + "\", \"v0\": " + m.vertexCount + ", \"v1\": " + simple.vertexCount + "}");
                    made[m] = simple;
                }
                repointed++;
                if (!dry) { c.sharedMesh = simple; EditorUtility.SetDirty(c); }
            }
            bool saved = false;
            if (!dry && repointed > 0)
            {
                Directory.CreateDirectory(Logs);
                File.WriteAllText(Path.Combine(Logs, "colliders.json"), "{\n \"cell\": " + cell.ToString(CultureInfo.InvariantCulture) + ",\n \"pairs\": [\n" + string.Join(",\n", lines) + "\n ]\n}\n");
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                saved = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            }
            return (dry ? "DRY " : "") + "cell " + cell.ToString(CultureInfo.InvariantCulture) + " m | meshes " + made.Count + " | colliders repointed " + repointed + " | vertices " + vBefore.ToString("N0", CultureInfo.InvariantCulture) + " -> " + vAfter.ToString("N0", CultureInfo.InvariantCulture) + " | " + Mb(before) + " -> " + Mb(after) + " | saved " + saved;
        }

        // 2026-10-08 (ColliderCheck308): the repeat test keeps the winding. A thin slab (both faces land on the same three points) used to
        // lose one face and became one-sided: floors with holes from above, walls open from one side.
        internal static Mesh Cluster(Mesh source, float cell)
        {
            Vector3[] v = source.vertices; int[] t = source.triangles;
            var index = new Dictionary<Vector3Int, int>(); var sum = new List<Vector3>(); var count = new List<int>(); var map = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(v[i].x / cell), Mathf.RoundToInt(v[i].y / cell), Mathf.RoundToInt(v[i].z / cell));
                if (!index.TryGetValue(key, out int n)) { index[key] = n = sum.Count; sum.Add(Vector3.zero); count.Add(0); }
                sum[n] += v[i]; count[n]++; map[i] = n;
            }
            var points = new Vector3[sum.Count];
            for (int i = 0; i < points.Length; i++) points[i] = sum[i] / count[i];
            var seen = new HashSet<(int, int, int)>(); var tris = new List<int>(t.Length / 4);
            for (int i = 0; i < t.Length; i += 3)
            {
                int a = map[t[i]], b = map[t[i + 1]], c = map[t[i + 2]];
                if (a == b || b == c || a == c) continue;
                // same triangle = same three points in the same turning order (rotated so the smallest index leads)
                var key = a < b && a < c ? (a, b, c) : b < c ? (b, c, a) : (c, a, b);
                if (!seen.Add(key)) continue;
                tris.Add(a); tris.Add(b); tris.Add(c);
            }
            var mesh = new Mesh { indexFormat = points.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.vertices = points; mesh.triangles = tris.ToArray(); mesh.RecalculateBounds();
            return mesh;
        }

        // tangents:audit[:<path part>] — vertices on drawn meshes whose materials have no normal map: their tangents (16 B a vertex) are unused
        static string TangentAudit(string part)
        {
            long with = 0, without = 0; var seenMesh = new HashSet<Mesh>(); var need = new HashSet<Mesh>(); var shaders = new Dictionary<string, long>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var f = r.GetComponent<MeshFilter>(); Mesh m = f != null ? f.sharedMesh : null;
                if (m == null || !m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent)) continue;
                if (AssetDatabase.GetAssetPath(m).IndexOf(part, StringComparison.OrdinalIgnoreCase) < 0) continue;
                bool normalMap = false;
                foreach (Material mat in r.sharedMaterials)
                {
                    if (mat == null) continue;
                    foreach (string prop in new[] { "_BumpMap", "_NormalMap", "_DetailNormalMap", "_Normal" }) if (mat.HasProperty(prop) && mat.GetTexture(prop) != null) normalMap = true;
                    shaders.TryGetValue(mat.shader.name, out long n); shaders[mat.shader.name] = n + 1;
                }
                if (normalMap) need.Add(m);
                seenMesh.Add(m);
            }
            foreach (Mesh m in seenMesh) { if (need.Contains(m)) with += m.vertexCount; else without += m.vertexCount; }
            return "tangent vertices on meshes with a normal-mapped material " + with.ToString("N0", CultureInfo.InvariantCulture) + " | without " + without.ToString("N0", CultureInfo.InvariantCulture) + " (" + Mb(without * 16) + " of tangents)\n" +
                   string.Join("\n", shaders.OrderByDescending(k => k.Value).Take(8).Select(k => "  " + k.Value + " material slots  " + k.Key));
        }

        static readonly string[] ProtectedTrees = { "/Watershed295/", "/Reworld292/", "/MountainTrail285/" };
        static bool Protected(string path) => ProtectedTrees.Any(t => path.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);

        // tex:audit — loaded asset textures by size class and by compression, with bytes
        static string TexAudit()
        {
            var rows = new Dictionary<string, (long bytes, int count)>();
            foreach (Texture2D x in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                string path = AssetDatabase.GetAssetPath(x);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                int side = Mathf.Max(x.width, x.height);
                string size = side >= 4096 ? "4096+" : side >= 2048 ? "2048" : side >= 1024 ? "1024" : "<1024";
                bool compressed = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCompressedFormat(x.graphicsFormat);
                string key = size.PadRight(6) + (compressed ? " compressed  " : " uncompressed") + (Protected(path) ? " protected tree" : "");
                rows.TryGetValue(key, out var v); rows[key] = (v.bytes + Profiler.GetRuntimeMemorySizeLong(x), v.count + 1);
            }
            return string.Join("\n", rows.OrderByDescending(k => k.Value.bytes).Select(k => Mb(k.Value.bytes).PadLeft(10) + k.Value.count.ToString(CultureInfo.InvariantCulture).PadLeft(6) + "  " + k.Key));
        }

        // tex:cap:<max>[:dry] — importer max size for every LOADED asset texture larger than <max> (protected trees left alone),
        // and compression turned on for loaded uncompressed colour textures of 2048 or more that are not Read/Write.
        // Writes logs/mem/tex_cap_<time>.txt (path | old max | old compression) for tex:uncap.
        static string TexCap(int max, bool dry)
        {
            var done = new List<string>(); long before = 0; int capped = 0, compressedNow = 0, skipped = 0;
            foreach (Texture2D x in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                string path = AssetDatabase.GetAssetPath(x);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || !AssetDatabase.IsMainAsset(x)) continue;
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;
                if (Protected(path)) { if (Mathf.Max(x.width, x.height) > max) skipped++; continue; }
                bool tooBig = Mathf.Max(x.width, x.height) > max && imp.maxTextureSize > max;
                bool raw = Mathf.Max(x.width, x.height) >= 2048 && imp.textureCompression == TextureImporterCompression.Uncompressed && !imp.isReadable
                           && imp.textureType != TextureImporterType.SingleChannel;
                if (!tooBig && !raw) continue;
                before += Profiler.GetRuntimeMemorySizeLong(x);
                done.Add(path + "|" + imp.maxTextureSize + "|" + imp.textureCompression);
                if (tooBig) capped++; if (raw) compressedNow++;
                if (dry) continue;
                if (tooBig) imp.maxTextureSize = max;
                if (raw) imp.textureCompression = TextureImporterCompression.Compressed;
                imp.SaveAndReimport();
            }
            if (!dry && done.Count > 0) { Directory.CreateDirectory(Logs); File.WriteAllLines(Path.Combine(Logs, "tex_cap_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt"), done); }
            return (dry ? "DRY " : "") + "max " + max + " | textures " + done.Count + " (size capped " + capped + ", compression on " + compressedNow + ") | " + Mb(before) + " before | protected-tree textures over the size left alone " + skipped;
        }

        static string TexUncap(string listFile)
        {
            int n = 0;
            foreach (string line in File.ReadAllLines(listFile))
            {
                string[] f = line.Split('|'); var imp = AssetImporter.GetAtPath(f[0]) as TextureImporter; if (imp == null) continue;
                imp.maxTextureSize = int.Parse(f[1], CultureInfo.InvariantCulture);
                imp.textureCompression = (TextureImporterCompression)Enum.Parse(typeof(TextureImporterCompression), f[2]);
                imp.SaveAndReimport(); n++;
            }
            return "restored " + n;
        }

        // mesh:pack:<folder | one mesh asset>[:dry] — smaller vertex layout for static mesh assets, same geometry and same indices:
        // normal Float32x3 -> SNorm16x4, tangent Float32x4 -> SNorm8x4, uv Float32x2 -> Float16x2 when every |uv| <= 8.
        // 48 B a vertex -> 28 or 32. Skipped: protected trees, skinned meshes, vertex colours, more than one stream, fewer than 2,000 vertices.
        // The mesh object is rewritten in place (same asset, same references). Writes logs/mem/mesh_pack_<time>.txt.
        static string MeshPack(string target, bool dry, bool _)
        {
            IEnumerable<string> paths = target.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) ? new[] { target } : MeshAssets(target);
            int done = 0, skipped = 0; long before = 0, after = 0, verts = 0; var log = new List<string>(); var why = new Dictionary<string, int>();
            void Skip(string reason) { skipped++; why.TryGetValue(reason, out int n); why[reason] = n + 1; }
            foreach (string path in paths)
            {
                if (Protected(path)) { Skip("protected tree"); continue; }
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null) continue;
                if (mesh.vertexCount < 2000) { Skip("small"); continue; }
                if (mesh.vertexBufferCount != 1) { Skip("streams"); continue; }
                var attrs = mesh.GetVertexAttributes();
                if (attrs.Any(d => d.attribute == UnityEngine.Rendering.VertexAttribute.Color || d.attribute == UnityEngine.Rendering.VertexAttribute.BlendWeight
                                   || d.attribute == UnityEngine.Rendering.VertexAttribute.BlendIndices || (d.attribute >= UnityEngine.Rendering.VertexAttribute.TexCoord0 && d.dimension != 2))) { Skip("colours, skin or wide uv"); continue; }
                if (!attrs.Any(d => d.format == UnityEngine.Rendering.VertexAttributeFormat.Float32 && d.attribute != UnityEngine.Rendering.VertexAttribute.Position)) { Skip("already packed"); continue; }
                long b0 = Profiler.GetRuntimeMemorySizeLong(mesh);
                if (!dry && !PackOne(mesh)) { Skip("failed"); continue; }
                done++; verts += mesh.vertexCount; before += b0; after += dry ? 0 : Profiler.GetRuntimeMemorySizeLong(mesh);
                log.Add(path);
                if (!dry) { EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh); }
            }
            if (!dry && done > 0) { Directory.CreateDirectory(Logs); File.WriteAllLines(Path.Combine(Logs, "mesh_pack_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt"), log); }
            return (dry ? "DRY " : "") + target + " | packed " + done + " meshes, " + verts.ToString("N0", CultureInfo.InvariantCulture) + " vertices | " + Mb(before) + " -> " + (dry ? "?" : Mb(after))
                   + " | skipped " + skipped + " (" + string.Join(", ", why.Select(k => k.Key + " " + k.Value)) + ")";
        }

        static bool PackOne(Mesh mesh)
        {
            int n = mesh.vertexCount;
            var pos = mesh.vertices; var nor = mesh.normals; var tan = mesh.tangents;
            // 2026-10-08: a normal shorter than one SNorm16 step packs to zero, a lit shader normalises zero into NaN, and Bloom turns that
            // pixel into a white glow hundreds of pixels wide (seen at the house skirts by the Geumpyo inn). Every normal is made unit
            // length before it is packed; one with no direction takes the nearest sound normal, else straight up (LightDiag308 normals: repairs old packs).
            for (int ni = 0; ni < nor.Length; ni++)
            {
                // divided by hand: Vector3.normalized returns zero under a length of 1e-5
                float nlen = nor[ni].magnitude; if (nlen > 1e-7f && !float.IsNaN(nlen)) { nor[ni] /= nlen; continue; }
                Vector3 pick = Vector3.up; float bestD = float.MaxValue;
                for (int nj = Mathf.Max(0, ni - 64); nj < Mathf.Min(nor.Length, ni + 64); nj++) { if (nj == ni || nor[nj].sqrMagnitude < .25f) continue; float dd = (pos[nj] - pos[ni]).sqrMagnitude; if (dd < bestD) { bestD = dd; pick = nor[nj].normalized; } }
                nor[ni] = pick;
            }
            var uvs = new List<Vector2>[4]; var uvHalf = new bool[4];
            for (int c = 0; c < 4; c++)
            {
                if (!mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0 + c)) continue;
                uvs[c] = new List<Vector2>(n); mesh.GetUVs(c, uvs[c]);
                if (uvs[c].Count != n) return false;
                float m = 0f; foreach (var u in uvs[c]) m = Mathf.Max(m, Mathf.Max(Mathf.Abs(u.x), Mathf.Abs(u.y)));
                uvHalf[c] = m <= 8f;
            }
            bool hasNor = nor != null && nor.Length == n, hasTan = tan != null && tan.Length == n;
            int subs = mesh.subMeshCount; var index = new int[subs][]; var topo = new MeshTopology[subs]; var baseVertex = new uint[subs];
            for (int i = 0; i < subs; i++) { index[i] = mesh.GetIndices(i, false); topo[i] = mesh.GetTopology(i); baseVertex[i] = mesh.GetBaseVertex(i); }
            Bounds bounds = mesh.bounds; var indexFormat = mesh.indexFormat; string name = mesh.name;

            var layout = new List<UnityEngine.Rendering.VertexAttributeDescriptor> { new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Position, UnityEngine.Rendering.VertexAttributeFormat.Float32, 3) };
            int stride = 12;
            if (hasNor) { layout.Add(new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Normal, UnityEngine.Rendering.VertexAttributeFormat.SNorm16, 4)); stride += 8; }
            if (hasTan) { layout.Add(new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Tangent, UnityEngine.Rendering.VertexAttributeFormat.SNorm8, 4)); stride += 4; }
            for (int c = 0; c < 4; c++)
            {
                if (uvs[c] == null) continue;
                layout.Add(new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.TexCoord0 + c, uvHalf[c] ? UnityEngine.Rendering.VertexAttributeFormat.Float16 : UnityEngine.Rendering.VertexAttributeFormat.Float32, 2));
                stride += uvHalf[c] ? 4 : 8;
            }
            var data = new byte[(long)stride * n]; var f = new float[1]; var b4 = new byte[4];
            void F32(float v, int at) { f[0] = v; Buffer.BlockCopy(f, 0, data, at, 4); }
            void S16(float v, int at) { short q = (short)Mathf.RoundToInt(Mathf.Clamp(v, -1f, 1f) * 32767f); data[at] = (byte)(q & 0xFF); data[at + 1] = (byte)((q >> 8) & 0xFF); }
            void S8(float v, int at) { data[at] = unchecked((byte)(sbyte)Mathf.RoundToInt(Mathf.Clamp(v, -1f, 1f) * 127f)); }
            void H16(float v, int at) { ushort h = Mathf.FloatToHalf(v); data[at] = (byte)(h & 0xFF); data[at + 1] = (byte)(h >> 8); }
            for (int i = 0; i < n; i++)
            {
                int at = i * stride;
                F32(pos[i].x, at); F32(pos[i].y, at + 4); F32(pos[i].z, at + 8); at += 12;
                if (hasNor) { S16(nor[i].x, at); S16(nor[i].y, at + 2); S16(nor[i].z, at + 4); at += 8; }
                if (hasTan) { S8(tan[i].x, at); S8(tan[i].y, at + 1); S8(tan[i].z, at + 2); S8(tan[i].w, at + 3); at += 4; }
                for (int c = 0; c < 4; c++)
                {
                    if (uvs[c] == null) continue;
                    if (uvHalf[c]) { H16(uvs[c][i].x, at); H16(uvs[c][i].y, at + 2); at += 4; }
                    else { F32(uvs[c][i].x, at); F32(uvs[c][i].y, at + 4); at += 8; }
                }
            }
            mesh.Clear();
            mesh.name = name; mesh.indexFormat = indexFormat;
            mesh.SetVertexBufferParams(n, layout.ToArray());
            mesh.SetVertexBufferData(data, 0, 0, data.Length, 0, UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds | UnityEngine.Rendering.MeshUpdateFlags.DontValidateIndices);
            mesh.subMeshCount = subs;
            for (int i = 0; i < subs; i++) mesh.SetIndices(index[i], topo[i], i, false, (int)baseVertex[i]);
            mesh.bounds = bounds;
            return true;
        }

        [Serializable] sealed class DupeGroup { public string keep = ""; public long bytes; public string[] dupes = new string[0]; }
        [Serializable] sealed class DupeFile { public DupeGroup[] groups = new DupeGroup[0]; }

        static string DupesApply(bool dry)
        {
            var file = JsonUtility.FromJson<DupeFile>(File.ReadAllText(Path.Combine(Logs, "dupes.json")));
            var keepOf = new Dictionary<string, string>();
            foreach (var g in file.groups) foreach (string d in g.dupes) keepOf[d] = g.keep;
            var cache = new Dictionary<string, Mesh>();
            Mesh Keep(Mesh m)
            {
                if (m == null) return null;
                string path = AssetDatabase.GetAssetPath(m);
                if (string.IsNullOrEmpty(path) || !keepOf.TryGetValue(path, out string keep)) return null;
                if (!cache.TryGetValue(keep, out Mesh k)) cache[keep] = k = AssetDatabase.LoadAssetAtPath<Mesh>(keep);
                return k;
            }
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.isDirty && !dry) return "refused: " + scene.path + " has unsaved changes that are not mine";
            int filters = 0, colliders = 0;
            foreach (var f in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Mesh k = Keep(f.sharedMesh); if (k == null) continue;
                filters++; if (!dry) { f.sharedMesh = k; EditorUtility.SetDirty(f); }
            }
            foreach (var c in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Mesh k = Keep(c.sharedMesh); if (k == null) continue;
                colliders++; if (!dry) { c.sharedMesh = k; EditorUtility.SetDirty(c); }
            }
            bool saved = false;
            if (!dry && filters + colliders > 0)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                saved = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            }
            return (dry ? "DRY " : "") + scene.path + " | mesh filters repointed " + filters + " | mesh colliders repointed " + colliders + " | saved " + saved;
        }

        static string Dupes(string folder)
        {
            var groups = new Dictionary<string, List<string>>(); var size = new Dictionary<string, long>();
            foreach (string path in MeshAssets(folder))
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null || mesh.vertexCount < 500) continue;
                string key;
                using (var data = Mesh.AcquireReadOnlyMeshData(mesh))
                {
                    var d = data[0]; var h = new Hash128();
                    for (int s = 0; s < d.vertexBufferCount; s++) { var v = d.GetVertexData<byte>(s); h.Append(v); }
                    var idx = d.GetIndexData<byte>(); h.Append(idx);
                    h.Append(d.subMeshCount); for (int s = 0; s < d.subMeshCount; s++) { var sm = d.GetSubMesh(s); h.Append(sm.indexStart); h.Append(sm.indexCount); h.Append((int)sm.topology); }
                    key = h + "|" + mesh.vertexCount;
                }
                if (!groups.TryGetValue(key, out var l)) groups[key] = l = new List<string>();
                l.Add(path); size[path] = Profiler.GetRuntimeMemorySizeLong(mesh);
            }
            var multi = groups.Values.Where(g => g.Count > 1).OrderByDescending(g => size[g[0]] * (g.Count - 1)).ToList();
            long save = multi.Sum(g => g.Skip(1).Sum(p => size[p])); int extra = multi.Sum(g => g.Count - 1);
            var sb = new StringBuilder("{\n \"groups\": [\n");
            for (int i = 0; i < multi.Count; i++)
                sb.Append("  {\"keep\": \"" + multi[i][0] + "\", \"bytes\": " + size[multi[i][0]] + ", \"dupes\": [" + string.Join(", ", multi[i].Skip(1).Select(p => "\"" + p + "\"")) + "]}" + (i < multi.Count - 1 ? ",\n" : "\n"));
            sb.Append(" ]\n}\n");
            Directory.CreateDirectory(Logs);
            File.WriteAllText(Path.Combine(Logs, "dupes.json"), sb.ToString());
            var top = string.Join("\n", multi.Take(8).Select(g => "  x" + g.Count + " " + Mb(size[g[0]]) + " " + Path.GetFileName(g[0])));
            return "folder " + folder + " | groups " + multi.Count + " | extra copies " + extra + " | bytes in extra copies " + Mb(save) + "\n" + top;
        }
    }
}
