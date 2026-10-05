using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #307 perf: where the non-vegetation triangles come from. Read-only census of the loaded scenes (Edit or Play): every enabled
    // MeshRenderer / SkinnedMeshRenderer, grouped by mesh, with triangle totals, LOD membership, shadow casting and the distance
    // to a probe point. Writes Art/Performance/Perf307/tri-census[-<name>].txt. Nothing is modified or saved.
    //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.TriCensus307 Run "census[:x=..:z=..][:r=metres][:name=..]"
    public static class TriCensus307
    {
        sealed class Row { public string Mesh, Path, Example; public long Tris; public int Count, Lod, NoLod, Shadow; public float Nearest = float.MaxValue; public long TrisNear; }

        public static string Run(string arg)
        {
            arg = (arg ?? "").Trim();
            if (arg == "terrain")
            {
                var t = new StringBuilder();
                foreach (var tr in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    var d = tr.terrainData; if (d == null) continue;
                    t.AppendLine($"{Path(tr.transform)} | enabled {tr.enabled} | size {d.size} | heightmap {d.heightmapResolution} | pixelError {tr.heightmapPixelError} | basemapDist {tr.basemapDistance} | shadows {tr.shadowCastingMode} | drawHeightmap {tr.drawHeightmap} | drawInstanced {tr.drawInstanced} | maxLOD {tr.heightmapMaximumLOD} | trees {d.treeInstanceCount} | details {tr.drawTreesAndFoliage} dist {tr.detailObjectDistance} | pos {tr.transform.position}");
                }
                t.AppendLine("LOD bias " + QualitySettings.lodBias + " maxLOD " + QualitySettings.maximumLODLevel + " quality " + QualitySettings.names[QualitySettings.GetQualityLevel()]);
                return t.Length > 0 ? t.ToString() : "no terrains";
            }
            var opts = arg.Split(':').Skip(1).Select(p => p.Split('=')).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
            float F(string k, float d) => opts.TryGetValue(k, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : d;
            var probe = new Vector3(F("x", 3261f), 0, F("z", 1878f)); float radius = F("r", 400f);
            string name = opts.TryGetValue("name", out var n) ? n : "";
            var rows = new Dictionary<Mesh, Row>();
            long total = 0, totalNear = 0, lodded = 0, shadowTris = 0; int renderers = 0;
            var inLod = new HashSet<Renderer>();
            var lodTop = new Dictionary<Renderer, int>();
            foreach (var g in Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var lods = g.GetLODs();
                for (int i = 0; i < lods.Length; i++) foreach (var r in lods[i].renderers) if (r != null) { inLod.Add(r); lodTop[r] = i; }
            }
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                Mesh mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r is MeshRenderer ? r.GetComponent<MeshFilter>()?.sharedMesh : null;
                if (mesh == null) continue;
                long tris = 0; for (int i = 0; i < mesh.subMeshCount; i++) tris += (long)mesh.GetIndexCount(i) / 3;
                // a renderer in a LOD group counts only when it is LOD0 (the census asks what the near view can draw)
                if (lodTop.TryGetValue(r, out int level) && level > 0) continue;
                renderers++;
                if (!rows.TryGetValue(mesh, out var row)) rows[mesh] = row = new Row { Mesh = mesh.name, Path = AssetDatabase.GetAssetPath(mesh), Example = Path(r.transform) };
                row.Tris += tris; row.Count++; total += tris;
                if (inLod.Contains(r)) { row.Lod++; lodded += tris; } else row.NoLod++;
                if (r.shadowCastingMode != ShadowCastingMode.Off) { row.Shadow++; shadowTris += tris; }
                var c = r.bounds.center; float d = Vector2.Distance(new Vector2(c.x, c.z), new Vector2(probe.x, probe.z));
                row.Nearest = Mathf.Min(row.Nearest, d);
                if (d <= radius) { row.TrisNear += tris; totalNear += tris; }
            }
            var sb = new StringBuilder();
            sb.AppendLine("#307 tri-census " + DateTime.Now.ToString("s") + " | play " + Application.isPlaying + " | scenes " + string.Join(",", Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i).name)));
            sb.AppendLine($"renderers {renderers}, meshes {rows.Count}, LOD0-or-plain triangles {total / 1e6:0.0} M (in LOD groups {lodded / 1e6:0.0} M, shadow casting {shadowTris / 1e6:0.0} M); within {radius} m of ({probe.x},{probe.z}) {totalNear / 1e6:0.0} M");
            sb.AppendLine("tris_M\tnear_M\tcount\tlod\tnolod\tshadow\tnearest_m\tmesh\tasset\texample");
            foreach (var row in rows.Values.OrderByDescending(x => x.Tris).Take(80))
                sb.AppendLine($"{row.Tris / 1e6:0.000}\t{row.TrisNear / 1e6:0.000}\t{row.Count}\t{row.Lod}\t{row.NoLod}\t{row.Shadow}\t{row.Nearest:0}\t{row.Mesh}\t{row.Path}\t{row.Example}");
            string folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../Art/Performance/Perf307"));
            Directory.CreateDirectory(folder);
            string file = System.IO.Path.Combine(folder, "tri-census" + (name.Length > 0 ? "-" + name : "") + ".txt");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            return string.Join("\n", sb.ToString().Split('\n').Take(22)) + "\n(report " + file + ")";
        }

        static string Path(Transform t)
        {
            var parts = new List<string>();
            for (int i = 0; t != null && i < 5; i++, t = t.parent) parts.Add(t.name);
            parts.Reverse(); return string.Join("/", parts);
        }
    }
}
