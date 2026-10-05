using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #307 foliage mesh reduction (user 2026-10-01: "본체 잎 메시도 확실하게 줄여"). Edit mode, queue-safe:
    //   info:<Assets/...mesh.asset>              vertex attributes, counts, submeshes, bounds
    //   export:<mesh.asset>:<file.obj>           positions / uv0 / normals / triangles (the only channels CompactNaturalVegetation reads)
    //   import:<mesh.asset>:<file.obj>           replaces that mesh asset's geometry in place (same GUID, so every sheet keeps its
    //                                            reference); the original .asset is copied to Tools/Unity/Stage307_backup_mesh/<utc>/
    //   restore:<mesh.asset>:<backup .asset>     puts a backup file back
    public static class MeshSlim307
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static string Run(string arg)
        {
            arg = (arg ?? "").Trim();
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode only";
            var parts = arg.Split(new[] { ':' }, 2); if (parts.Length < 2) return "refused: info|export|import|restore:<args>";
            try
            {
                switch (parts[0])
                {
                    case "info": return Info(parts[1]);
                    case "export": { var a = Split2(parts[1]); return Export(a[0], a[1]); }
                    case "import": { var a = Split2(parts[1]); return Import(a[0], a[1]); }
                    case "restore": { var a = Split2(parts[1]); File.Copy(a[1], Full(a[0]), true); AssetDatabase.ImportAsset(a[0], ImportAssetOptions.ForceUpdate); return "restored " + a[0]; }
                }
            }
            catch (Exception e) { return "FAIL " + e.Message; }
            return "refused: unknown " + parts[0];
        }
        // "Assets/x.asset:C:/y/z.obj" — split at the ':' that follows ".asset"
        static string[] Split2(string s) { int i = s.IndexOf(".asset:", StringComparison.Ordinal); return new[] { s.Substring(0, i + 6), s.Substring(i + 7) }; }
        static string Full(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));

        static Mesh Load(string path) { var m = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (m == null) throw new Exception("no Mesh at " + path); return m; }

        static string Info(string path)
        {
            var m = Load(path); var sb = new StringBuilder(path + " | verts " + m.vertexCount + " tris " + m.triangles.Length / 3 + " submeshes " + m.subMeshCount + " readable " + m.isReadable + " bounds " + m.bounds + "\n");
            foreach (var a in m.GetVertexAttributes()) sb.Append("  " + a.attribute + " " + a.format + " x" + a.dimension + " stream " + a.stream + "\n");
            return sb.ToString();
        }

        static string Export(string path, string obj)
        {
            var m = Load(path); var v = m.vertices; var n = m.normals; var uv = m.uv; var t = m.triangles;
            var sb = new StringBuilder("# #307 MeshSlim307 export " + path + "\n");
            foreach (var p in v) sb.Append("v ").Append(p.x.ToString("R", Inv)).Append(' ').Append(p.y.ToString("R", Inv)).Append(' ').Append(p.z.ToString("R", Inv)).Append('\n');
            foreach (var p in uv) sb.Append("vt ").Append(p.x.ToString("R", Inv)).Append(' ').Append(p.y.ToString("R", Inv)).Append('\n');
            foreach (var p in n) sb.Append("vn ").Append(p.x.ToString("R", Inv)).Append(' ').Append(p.y.ToString("R", Inv)).Append(' ').Append(p.z.ToString("R", Inv)).Append('\n');
            bool hasUv = uv.Length == v.Length, hasN = n.Length == v.Length;
            for (int i = 0; i < t.Length; i += 3)
            {
                sb.Append('f');
                for (int k = 0; k < 3; k++) { int x = t[i + k] + 1; sb.Append(' ').Append(x); if (hasUv || hasN) { sb.Append('/'); if (hasUv) sb.Append(x); if (hasN) sb.Append('/').Append(x); } }
                sb.Append('\n');
            }
            Directory.CreateDirectory(Path.GetDirectoryName(obj)); File.WriteAllText(obj, sb.ToString(), new UTF8Encoding(false));
            return "exported " + path + " -> " + obj + " (" + v.Length + " v, " + t.Length / 3 + " tris, uv " + hasUv + ", normals " + hasN + ")";
        }

        static string Import(string path, string obj)
        {
            var m = Load(path);
            var pos = new List<Vector3>(); var uvs = new List<Vector2>(); var nrm = new List<Vector3>();
            var outV = new List<Vector3>(); var outUv = new List<Vector2>(); var outN = new List<Vector3>(); var tris = new List<int>();
            var map = new Dictionary<(int, int, int), int>();
            foreach (var raw in File.ReadLines(obj))
            {
                var line = raw.Trim(); if (line.Length < 2) continue;
                var f = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (f[0] == "v") pos.Add(new Vector3(float.Parse(f[1], Inv), float.Parse(f[2], Inv), float.Parse(f[3], Inv)));
                else if (f[0] == "vt") uvs.Add(new Vector2(float.Parse(f[1], Inv), float.Parse(f[2], Inv)));
                else if (f[0] == "vn") nrm.Add(new Vector3(float.Parse(f[1], Inv), float.Parse(f[2], Inv), float.Parse(f[3], Inv)));
                else if (f[0] == "f")
                {
                    var corner = new int[f.Length - 1];
                    for (int k = 1; k < f.Length; k++)
                    {
                        var s = f[k].Split('/'); int pi = int.Parse(s[0]) - 1, ti = s.Length > 1 && s[1].Length > 0 ? int.Parse(s[1]) - 1 : -1, ni = s.Length > 2 && s[2].Length > 0 ? int.Parse(s[2]) - 1 : -1;
                        var key = (pi, ti, ni);
                        if (!map.TryGetValue(key, out int idx)) { idx = outV.Count; map[key] = idx; outV.Add(pos[pi]); outUv.Add(ti >= 0 ? uvs[ti] : Vector2.zero); outN.Add(ni >= 0 ? nrm[ni] : Vector3.up); }
                        corner[k - 1] = idx;
                    }
                    for (int k = 1; k + 1 < corner.Length; k++) { tris.Add(corner[0]); tris.Add(corner[k]); tris.Add(corner[k + 1]); }
                }
            }
            if (tris.Count == 0) return "refused: no faces in " + obj;
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", Inv);
            string backup = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Tools/Unity/Stage307_backup_mesh/" + stamp)); Directory.CreateDirectory(backup);
            File.Copy(Full(path), Path.Combine(backup, Path.GetFileName(path)), true); if (File.Exists(Full(path) + ".meta")) File.Copy(Full(path) + ".meta", Path.Combine(backup, Path.GetFileName(path) + ".meta"), true);
            int beforeT = m.triangles.Length / 3, beforeV = m.vertexCount; var bounds = m.bounds;
            m.Clear();
            m.indexFormat = outV.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.SetVertices(outV); m.SetNormals(outN); m.SetUVs(0, outUv); m.SetTriangles(tris, 0);
            m.RecalculateTangents(); m.RecalculateBounds();
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            return "imported " + obj + " -> " + path + " | tris " + beforeT + " -> " + tris.Count / 3 + ", verts " + beforeV + " -> " + outV.Count + " | bounds " + bounds + " -> " + m.bounds + " | backup " + backup;
        }
    }
}
