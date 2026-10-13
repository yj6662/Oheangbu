using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 Hanyang phase 1c (SPEC-WORLD-HANYANG-308 §11): light copies of the owned packs (Art/World/PackLite308, copied by _tools/lite_prep.py).
    //   lite:import[:<name>]   textures + URP Lit materials (PackLite308/_materials) + model import + prefab with LODGroup (Hanyang308/Prefabs)
    //   lite:probe:<name>      mesh facts of LOD0 (sub-meshes, long triangles, level surfaces, storey profile) -> text
    //   lite:derive            cleaned / cut meshes made from the imported models (Hanyang308/Meshes) + their prefabs
    public static partial class Hanyang308
    {
        const string LiteMatDir = LiteDir + "/_materials";
        static string LiteFile => Path.Combine(StageDir, "lite_import.json");
        [Serializable] sealed class LMat { public string name = "", albedo = "", normal = ""; public float[] tint = { 1, 1, 1 }; public bool clip; }
        [Serializable] sealed class LItem { public string name = "", cls = "", palette = ""; public List<LMat> mats = new List<LMat>(); public float[] size = new float[3]; }
        [Serializable] sealed class LFile { public List<LItem> items = new List<LItem>(); }
        static LFile LoadLite() { if (!File.Exists(LiteFile)) throw new Refuse("no " + LiteFile); return JsonUtility.FromJson<LFile>(File.ReadAllText(LiteFile)); }

        static string Lite(string[] a)
        {
            string verb = a.Length > 1 ? a[1] : "", arg = a.Length > 2 ? a[2] : "";
            switch (verb)
            {
                case "import": return LiteImport(arg);
                case "probe": return LiteProbe(arg, a.Length > 3 ? a[3] : "0");
                case "derive": return LiteDerive();
                default: throw new Refuse("lite:" + verb + "?");
            }
        }

        static Texture2D LiteTex(string rel, bool normal, bool palette, int max)
        {
            if (string.IsNullOrEmpty(rel)) return null; string path = LiteDir + "/" + rel; if (!File.Exists(Abs(path))) return null;
            var imp = AssetImporter.GetAtPath(path) as TextureImporter; if (imp == null) { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport); imp = AssetImporter.GetAtPath(path) as TextureImporter; }
            if (imp == null) return null; bool ch = false;
            var want = normal ? TextureImporterType.NormalMap : TextureImporterType.Default; if (imp.textureType != want) { imp.textureType = want; ch = true; }
            if (imp.maxTextureSize != max) { imp.maxTextureSize = max; ch = true; }
            if (palette) { if (imp.filterMode != FilterMode.Point || imp.mipmapEnabled || imp.textureCompression != TextureImporterCompression.Uncompressed) { imp.filterMode = FilterMode.Point; imp.mipmapEnabled = false; imp.textureCompression = TextureImporterCompression.Uncompressed; ch = true; } }
            else if (!imp.mipmapEnabled) { imp.mipmapEnabled = true; ch = true; }
            if (ch) imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static Material LiteMat(string name, Texture2D albedo, Texture2D normal, Color tint, bool clip)
        {
            string path = LiteMatDir + "/" + name + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path); bool isNew = m == null;
            if (isNew) { var sh = Shader.Find("Universal Render Pipeline/Lit"); if (sh == null) throw new Refuse("URP Lit shader missing"); m = new Material(sh) { name = name }; }
            m.SetTexture("_BaseMap", albedo); m.SetTexture("_MainTex", albedo); m.SetColor("_BaseColor", tint); m.SetColor("_Color", tint); m.SetFloat("_Smoothness", 0.12f); m.SetFloat("_Metallic", 0f);
            if (normal != null) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); } else m.DisableKeyword("_NORMALMAP");
            if (clip) { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = 2450; }
            m.DisableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack; m.enableInstancing = true;
            if (isNew) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m); return m;
        }

        static string LiteImport(string only)
        {
            var lf = LoadLite(); var sb = new StringBuilder("Hanyang308 lite:import\n"); Directory.CreateDirectory(Abs(LiteMatDir)); Directory.CreateDirectory(Abs(CityPrefabDir));
            AssetDatabase.ImportAsset(LiteMatDir, ImportAssetOptions.ForceSynchronousImport);
            foreach (var it in lf.items)
            {
                if (only != "" && it.name != only) continue; var map = new Dictionary<string, Material>(); int texMissing = 0;
                foreach (var lm in it.mats)
                {
                    var al = LiteTex(lm.albedo, false, false, 1024); var no = LiteTex(lm.normal, true, false, 1024); if (al == null) texMissing++;
                    map[lm.name] = LiteMat(lm.name, al, no, new Color(lm.tint[0], lm.tint[1], lm.tint[2], 1), lm.clip);
                }
                var pal = LiteTex(it.palette, false, true, 32); string palName = "PL_" + it.name + "_Palette"; map[palName] = LiteMat(palName, pal, null, Color.white, false);
                var models = new GameObject[3]; bool ok = true; string said = "";
                for (int k = 0; k < 3; k++)
                {
                    string path = LiteDir + "/" + it.cls + "/" + it.name + "_LOD" + k + ".fbx"; if (!File.Exists(Abs(path))) { sb.AppendLine("  MISSING " + path); ok = false; break; }
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport); var imp = AssetImporter.GetAtPath(path) as ModelImporter; if (imp == null) { ok = false; break; }
                    imp.globalScale = 1f; imp.useFileUnits = true; imp.bakeAxisConversion = false; imp.importNormals = ModelImporterNormals.Import; imp.importTangents = ModelImporterTangents.CalculateMikk;
                    imp.isReadable = k < 2; imp.meshCompression = ModelImporterMeshCompression.Off; imp.optimizeMeshPolygons = true; imp.optimizeMeshVertices = true; imp.weldVertices = true;
                    imp.importBlendShapes = false; imp.importCameras = false; imp.importLights = false; imp.importAnimation = false; imp.animationType = ModelImporterAnimationType.None; imp.addCollider = false;
                    imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription; imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
                    foreach (var kv in map) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
                    imp.SaveAndReimport(); models[k] = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                }
                if (!ok) continue;
                var root = new GameObject(it.name); var lods = new LOD[3]; float[] cut = { 0.30f, 0.10f, 0.02f };
                for (int k = 0; k < 3; k++)
                {
                    var child = (GameObject)PrefabUtility.InstantiatePrefab(models[k]); child.name = it.name + "_LOD" + k; child.transform.SetParent(root.transform, false);
                    var rs = child.GetComponentsInChildren<Renderer>();
                    foreach (var r in rs) { if (k == 2) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; foreach (var m in r.sharedMaterials) if (m == null || !AssetDatabase.GetAssetPath(m).StartsWith(LiteMatDir)) said += " UNMAPPED(" + k + "):" + (m ? m.name : "null"); }
                    lods[k] = new LOD(cut[k], rs);
                    foreach (var mf in child.GetComponentsInChildren<MeshFilter>()) { var b = mf.sharedMesh.bounds; said += " L" + k + " " + mf.sharedMesh.name + " tris " + Tris(mf.sharedMesh) + " subs " + mf.sharedMesh.subMeshCount + (k == 0 ? " bounds " + b.min + ".." + b.max + " rot " + mf.transform.eulerAngles + " scl " + mf.transform.lossyScale : ""); }
                }
                var lg = root.AddComponent<LODGroup>(); lg.fadeMode = LODFadeMode.None; lg.SetLODs(lods); lg.RecalculateBounds();
                PrefabUtility.SaveAsPrefabAsset(root, CityPrefabDir + "/" + it.name + ".prefab"); UnityEngine.Object.DestroyImmediate(root);
                sb.AppendLine("  " + it.name + " materials " + map.Count + " textures missing " + texMissing + said);
            }
            return sb.ToString();
        }
        static long Tris(Mesh m) { long n = 0; for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s) / 3; return n; }

        static void LiteMeshOf(string name, int lod, out Mesh mesh, out Material[] mats, out Matrix4x4 toRoot)
        {
            var lf = LoadLite(); var it = lf.items.FirstOrDefault(i => i.name == name); if (it == null) throw new Refuse("no lite item " + name);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(LiteDir + "/" + it.cls + "/" + name + "_LOD" + lod + ".fbx"); if (go == null) throw new Refuse(name + " LOD" + lod + " not imported");
            var mfs = go.GetComponentsInChildren<MeshFilter>(); if (mfs.Length != 1) throw new Refuse(name + " has " + mfs.Length + " meshes"); mesh = mfs[0].sharedMesh; mats = mfs[0].GetComponent<Renderer>().sharedMaterials; toRoot = go.transform.worldToLocalMatrix * mfs[0].transform.localToWorldMatrix;
            if (!mesh.isReadable) throw new Refuse(name + " mesh is not readable");
        }

        static string LiteProbe(string name, string lodS)
        {
            int lod = int.Parse(lodS, Inv); LiteMeshOf(name, lod, out var mesh, out var mats, out var M); var v = mesh.vertices.Select(p => M.MultiplyPoint3x4(p)).ToArray(); var sb = new StringBuilder("Hanyang308 lite:probe " + name + " LOD" + lod + " verts " + v.Length + "\n");
            var upArea = new SortedDictionary<int, float>(); var prof = new SortedDictionary<int, float[]>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var t = mesh.GetTriangles(s); int nLong = 0; var lb = new Bounds(); bool has = false; var b = new Bounds(v[t[0]], Vector3.zero); float area = 0, longArea = 0;
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 p = v[t[i]], q = v[t[i + 1]], r = v[t[i + 2]]; b.Encapsulate(p); b.Encapsulate(q); b.Encapsulate(r); var cr = Vector3.Cross(q - p, r - p); float ar = cr.magnitude * 0.5f; area += ar;
                    float le = Mathf.Max((q - p).magnitude, Mathf.Max((r - q).magnitude, (p - r).magnitude));
                    if (le > 7f) { nLong++; longArea += ar; if (!has) { lb = new Bounds(p, Vector3.zero); has = true; } lb.Encapsulate(p); lb.Encapsulate(q); lb.Encapsulate(r); }
                    if (ar > 1e-6f && cr.y / (2 * ar) > 0.95f) { int k = Mathf.RoundToInt(((p.y + q.y + r.y) / 3f) * 4f); upArea.TryGetValue(k, out float u); upArea[k] = u + ar; }
                    foreach (var w in new[] { p, q, r }) { int k = Mathf.FloorToInt(w.y); if (!prof.TryGetValue(k, out var e)) prof[k] = e = new[] { 1e9f, -1e9f, 1e9f, -1e9f }; e[0] = Mathf.Min(e[0], w.x); e[1] = Mathf.Max(e[1], w.x); e[2] = Mathf.Min(e[2], w.z); e[3] = Mathf.Max(e[3], w.z); }
                }
                sb.AppendLine("  sub " + s + " " + (s < mats.Length && mats[s] ? mats[s].name.Replace("PL_HwaseongForteressGate_", "") : "?") + " tris " + t.Length / 3 + " area " + F(area) + " bounds " + b.min.ToString("F2") + ".." + b.max.ToString("F2") + " | edge>7m " + nLong + " area " + F(longArea) + (has ? " in " + lb.min.ToString("F1") + ".." + lb.max.ToString("F1") : ""));
            }
            sb.AppendLine("  level (up-facing) area by height: " + string.Join(" ", upArea.Where(k => k.Value > 12f).Select(k => F(k.Key / 4f, "F2") + "m=" + F(k.Value, "F0"))));
            sb.AppendLine("  profile y: x0..x1 z0..z1: " + string.Join(" | ", prof.Select(k => k.Key + ": " + F(k.Value[0]) + ".." + F(k.Value[1]) + " " + F(k.Value[2]) + ".." + F(k.Value[3]))));
            return sb.ToString();
        }

        // ---- derived meshes: filtered copies of the imported models (new asset names: stale GPU buffer trap) ----
        sealed class Keep { public Func<string, bool> sub = _ => true; public Func<Vector3, Vector3, Vector3, string, bool> tri = (a, b, c, m) => true; public Vector3 shift; }
        static Mesh FilterMesh(string src, int lod, string outName, Keep keep, out Material[] outMats)
        {
            LiteMeshOf(src, lod, out var mesh, out var mats, out var M); var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv; var tg = mesh.tangents;
            var map = new int[v.Length]; for (int i = 0; i < map.Length; i++) map[i] = -1; var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>(); var nt = new List<Vector4>(); var subs = new List<List<int>>(); var om = new List<Material>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                string mn = s < mats.Length && mats[s] ? mats[s].name : ""; if (!keep.sub(mn)) continue; var t = mesh.GetTriangles(s); var o = new List<int>();
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 p = M.MultiplyPoint3x4(v[t[i]]), q = M.MultiplyPoint3x4(v[t[i + 1]]), r = M.MultiplyPoint3x4(v[t[i + 2]]); if (!keep.tri(p, q, r, mn)) continue;
                    for (int k = 0; k < 3; k++) { int vi = t[i + k]; if (map[vi] < 0) { map[vi] = nv.Count; nv.Add(M.MultiplyPoint3x4(v[vi]) + keep.shift); nn.Add(M.MultiplyVector(n[vi]).normalized); nu.Add(vi < uv.Length ? uv[vi] : Vector2.zero); if (tg.Length == v.Length) { var tv = M.MultiplyVector(tg[vi]); nt.Add(new Vector4(tv.x, tv.y, tv.z, tg[vi].w)); } } o.Add(map[vi]); }
                }
                if (o.Count > 0) { subs.Add(o); om.Add(mats[s]); }
            }
            var m = new Mesh { name = outName, indexFormat = nv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 }; m.SetVertices(nv); m.SetNormals(nn); m.SetUVs(0, nu); if (nt.Count == nv.Count) m.SetTangents(nt);
            m.subMeshCount = subs.Count; for (int s = 0; s < subs.Count; s++) m.SetTriangles(subs[s], s); m.RecalculateBounds(); m.UploadMeshData(false); outMats = om.ToArray();
            string path = CityMeshDir + "/" + outName + ".asset"; if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(m, path); AssetDatabase.SaveAssetIfDirty(m); return m;
        }
        sealed class DDef { public string name, src; public Keep keep; public float[] cut = { 0.30f, 0.10f, 0.02f }; }
        static bool Long(Vector3 a, Vector3 b, Vector3 c, float lim) => Mathf.Max((b - a).magnitude, Mathf.Max((c - b).magnitude, (a - c).magnitude)) > lim;
        static List<DDef> Derived()
        {
            float cutY = LiteCutY;
            return new List<DDef>
            {
                // the gate base without the shut door leaves (open gates)
                new DDef { name = "Fort_GateBaseOpen", src = "Fort_GateBase", keep = new Keep { sub = mn => !mn.Contains("MetalDoor"), tri = (a, b, c, mn) => { foreach (var p in new[] { a, b, c }) if (Mathf.Abs(p.x) > 2.8f || p.y > 6.35f || p.z < -4.8f || p.z > -4.15f) return true; return false; } } },
                // 1-storey tower = the upper storey of the Janganmun tower (우진각, 판문), set down on y 0; stray plates / spikes of the light copy and the top band of the lower roof dropped
                new DDef { name = "Fort_Munru1", src = "Fort_Munru2", keep = new Keep { tri = (a, b, c, mn) => { float lo = Mathf.Min(a.y, Mathf.Min(b.y, c.y)); if (lo < cutY) return false; foreach (var p in new[] { a, b, c }) if (Mathf.Abs(p.z) > 7.95f || Mathf.Abs(p.x) > 15.4f) return false; var g = (a + b + c) / 3f; if (g.y < cutY + 1.4f && (Mathf.Abs(g.x) > 12.4f || Mathf.Abs(g.z) > 5.0f)) return false; return true; }, shift = new Vector3(0, -cutY, 0) } },
            };
        }
        const float LiteCutY = 5.9f, LiteLong = 7f;
        static string LiteDerive()
        {
            var sb = new StringBuilder("Hanyang308 lite:derive\n"); string ver = "c2";
            foreach (var d in Derived())
            {
                var root = new GameObject(d.name); var lods = new List<LOD>();
                for (int k = 0; k < 2; k++)
                {
                    var m = FilterMesh(d.src, k, "H308_" + d.name + "_L" + k + "_" + ver, d.keep, out var mats); var go = new GameObject(d.name + "_LOD" + k); go.transform.SetParent(root.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = m; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats; lods.Add(new LOD(d.cut[k], new Renderer[] { mr }));
                    sb.AppendLine("  " + m.name + " tris " + Tris(m) + " subs " + m.subMeshCount + " bounds " + m.bounds.min.ToString("F2") + ".." + m.bounds.max.ToString("F2"));
                }
                var l = lods[1]; l.screenRelativeTransitionHeight = 0.012f; lods[1] = l;
                var lg = root.AddComponent<LODGroup>(); lg.fadeMode = LODFadeMode.None; lg.SetLODs(lods.ToArray()); lg.RecalculateBounds();
                PrefabUtility.SaveAsPrefabAsset(root, CityPrefabDir + "/" + d.name + ".prefab"); UnityEngine.Object.DestroyImmediate(root);
            }
            return sb.ToString();
        }
    }
}
