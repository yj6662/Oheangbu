using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 editor memory audit (read-only). Why the editor sits at 12-18 GB with W_Demo_Main open.
    //   totals                      — Unity allocators, Mono heap, graphics driver, object counts per type
    //   top:<Type>[:n]              — the n largest loaded objects of a type (Texture2D, Mesh, RenderTexture, AudioClip, AnimationClip, ...)
    //   folders:<Type>[:depth[:n]]  — loaded objects of a type summed by asset folder (depth path segments)
    //   flags                       — textures and meshes that keep a CPU copy (Read/Write), with their size
    public static class MemoryAudit308
    {
        static readonly Type[] Types =
        {
            typeof(Texture2D), typeof(RenderTexture), typeof(Cubemap), typeof(Texture3D), typeof(Texture2DArray), typeof(Mesh), typeof(AudioClip),
            typeof(AnimationClip), typeof(Material), typeof(Shader), typeof(ComputeShader), typeof(TerrainData), typeof(Font), typeof(ScriptableObject),
            typeof(GameObject), typeof(Transform), typeof(MeshRenderer), typeof(MeshFilter), typeof(MeshCollider), typeof(MonoBehaviour)
        };

        public static string Run(string command)
        {
            string[] a = (command ?? "").Split(':');
            switch (a[0])
            {
                case "totals": return Totals();
                case "top": return Top(Find(a[1]), a.Length > 2 ? int.Parse(a[2], CultureInfo.InvariantCulture) : 25);
                case "folders": return Folders(Find(a[1]), a.Length > 2 ? int.Parse(a[2], CultureInfo.InvariantCulture) : 5, a.Length > 3 ? int.Parse(a[3], CultureInfo.InvariantCulture) : 25);
                case "flags": return Flags();
                case "meshes": return Meshes(a.Length > 1 ? a[1] : "");
                default: return "commands: totals | top:<Type>[:n] | folders:<Type>[:depth[:n]] | flags";
            }
        }

        static Type Find(string name) => Types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception("unknown type " + name);
        static string Gb(long b) => (b / 1073741824.0).ToString("F2", CultureInfo.InvariantCulture) + " GB";
        static string Mb(long b) => (b / 1048576.0).ToString("F1", CultureInfo.InvariantCulture) + " MB";

        static string Totals()
        {
            var sb = new StringBuilder();
            sb.AppendLine("unity allocated " + Gb(Profiler.GetTotalAllocatedMemoryLong()) + " | reserved " + Gb(Profiler.GetTotalReservedMemoryLong()) + " | unused reserved " + Gb(Profiler.GetTotalUnusedReservedMemoryLong()));
            sb.AppendLine("mono used " + Gb(Profiler.GetMonoUsedSizeLong()) + " | mono heap " + Gb(Profiler.GetMonoHeapSizeLong()) + " | graphics driver " + Gb(Profiler.GetAllocatedMemoryForGraphicsDriver()));
            sb.AppendLine("quality " + QualitySettings.names[QualitySettings.GetQualityLevel()] + " | playing " + EditorApplication.isPlaying + " | scenes " + UnityEngine.SceneManagement.SceneManager.sceneCount);
            long sum = 0;
            foreach (Type t in Types)
            {
                Object[] all = Resources.FindObjectsOfTypeAll(t);
                long bytes = 0;
                foreach (Object o in all) bytes += Profiler.GetRuntimeMemorySizeLong(o);
                if (t != typeof(Transform) && t != typeof(MeshRenderer) && t != typeof(MeshFilter) && t != typeof(MeshCollider) && t != typeof(MonoBehaviour)) sum += bytes;
                sb.AppendLine(t.Name.PadRight(16) + all.Length.ToString(CultureInfo.InvariantCulture).PadLeft(9) + "  " + Gb(bytes));
            }
            sb.AppendLine("sum of asset types " + Gb(sum));
            return sb.ToString();
        }

        static string Top(Type t, int n)
        {
            var rows = Resources.FindObjectsOfTypeAll(t).Select(o => (o, bytes: Profiler.GetRuntimeMemorySizeLong(o))).OrderByDescending(r => r.bytes).Take(n);
            var sb = new StringBuilder();
            foreach (var (o, bytes) in rows) sb.AppendLine(Mb(bytes).PadLeft(10) + "  " + Describe(o) + "  " + AssetDatabase.GetAssetPath(o));
            return sb.ToString();
        }

        static string Describe(Object o)
        {
            if (o is Texture2D x) return o.name + " " + x.width + "x" + x.height + " " + x.format + " mips " + x.mipmapCount + (x.isReadable ? " RW" : "");
            if (o is RenderTexture r) return o.name + " " + r.width + "x" + r.height + " " + r.format + " d" + r.depth + " aa" + r.antiAliasing;
            if (o is Mesh m) return o.name + " v" + m.vertexCount + (m.isReadable ? " RW" : "");
            return o.name;
        }

        static string Folders(Type t, int depth, int n)
        {
            var sums = new Dictionary<string, (long bytes, int count)>();
            foreach (Object o in Resources.FindObjectsOfTypeAll(t))
            {
                string path = AssetDatabase.GetAssetPath(o);
                string key = string.IsNullOrEmpty(path) ? "(no asset: scene or runtime)" : string.Join("/", path.Split('/').Take(depth));
                sums.TryGetValue(key, out var v);
                sums[key] = (v.bytes + Profiler.GetRuntimeMemorySizeLong(o), v.count + 1);
            }
            var sb = new StringBuilder();
            foreach (var kv in sums.OrderByDescending(k => k.Value.bytes).Take(n)) sb.AppendLine(Mb(kv.Value.bytes).PadLeft(10) + kv.Value.count.ToString(CultureInfo.InvariantCulture).PadLeft(7) + "  " + kv.Key);
            return sb.ToString();
        }

        // meshes[:<path part>] — where mesh bytes go: vertex layout, collider-only meshes, and meshes that repeat (same vertex count and bounds)
        static string Meshes(string part)
        {
            var all = Resources.FindObjectsOfTypeAll<Mesh>().Where(m => AssetDatabase.GetAssetPath(m).IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            var collider = new HashSet<Mesh>(Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(c => c.sharedMesh).Where(m => m != null));
            var drawn = new HashSet<Mesh>(Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(f => f.sharedMesh).Where(m => m != null));
            long total = 0, colliderOnly = 0, unused = 0, repeat = 0, vertexBytes = 0, indexBytes = 0, verts = 0; int colliderOnlyN = 0, unusedN = 0, repeatN = 0;
            var layout = new Dictionary<string, (long verts, int count)>();
            var seen = new Dictionary<string, int>();
            foreach (Mesh m in all)
            {
                long b = Profiler.GetRuntimeMemorySizeLong(m); total += b; verts += m.vertexCount;
                int stride = 0; for (int s = 0; s < m.vertexBufferCount; s++) stride += m.GetVertexBufferStride(s);
                vertexBytes += (long)stride * m.vertexCount;
                long idx = 0; for (int s = 0; s < m.subMeshCount; s++) idx += m.GetIndexCount(s);
                indexBytes += idx * (m.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32 ? 4 : 2);
                string key = stride + " B/vertex: " + string.Join(",", m.GetVertexAttributes().Select(d => d.attribute.ToString().Replace("TexCoord", "uv") + d.dimension + d.format.ToString().Replace("Float", "f").Replace("UNorm", "u")));
                layout.TryGetValue(key, out var l); layout[key] = (l.verts + m.vertexCount, l.count + 1);
                bool isCollider = collider.Contains(m), isDrawn = drawn.Contains(m);
                if (isCollider && !isDrawn) { colliderOnly += b; colliderOnlyN++; }
                if (!isCollider && !isDrawn) { unused += b; unusedN++; }
                string shape = m.vertexCount + "|" + m.bounds.size.ToString("F2") + "|" + idx;
                if (m.vertexCount > 2000) { if (seen.ContainsKey(shape)) { repeat += b; repeatN++; } else seen[shape] = 1; }
            }
            var sb = new StringBuilder();
            sb.AppendLine("meshes " + all.Length + " | " + Gb(total) + " | vertices " + verts.ToString("N0", CultureInfo.InvariantCulture) + " | vertex data " + Gb(vertexBytes) + " | index data " + Gb(indexBytes));
            sb.AppendLine("collider-only " + colliderOnlyN + " " + Gb(colliderOnly) + " | not referenced by the open scene " + unusedN + " " + Gb(unused) + " | repeats of an earlier mesh " + repeatN + " " + Gb(repeat));
            foreach (var kv in layout.OrderByDescending(k => k.Value.verts).Take(6)) sb.AppendLine(kv.Value.verts.ToString("N0", CultureInfo.InvariantCulture).PadLeft(14) + " v " + kv.Value.count.ToString(CultureInfo.InvariantCulture).PadLeft(5) + " meshes  " + kv.Key);
            return sb.ToString();
        }

        static string Flags()
        {
            long tex = 0, mesh = 0; int tn = 0, mn = 0;
            foreach (Texture2D x in Resources.FindObjectsOfTypeAll<Texture2D>()) if (x.isReadable && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(x))) { tex += Profiler.GetRuntimeMemorySizeLong(x); tn++; }
            foreach (Mesh m in Resources.FindObjectsOfTypeAll<Mesh>()) if (m.isReadable) { mesh += Profiler.GetRuntimeMemorySizeLong(m); mn++; }
            return "readable asset textures " + tn + " " + Gb(tex) + "\nreadable meshes " + mn + " " + Gb(mesh);
        }
    }
}
