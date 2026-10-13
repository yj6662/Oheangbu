using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 Hanyang phase 1b (SPEC-WORLD-HANYANG-308 §10): city wall, gates, bridges from Tools/Unity/Stage308_hanyang/city308.json
    // (written by Tools/Art/hanyang308_city.py). Edit mode, queue-safe, no dialog.
    //   city:import            importer settings + prefabs (LODGroup) of the PackLite308/gen models listed in the plan
    //   city:meshes            generated wall modules -> Assets/_Project/Art/World/Hanyang308/Meshes (shared by every instance)
    //   city:probe             kept wall chunks at the joins (top, width) -> Out/city_probe.json
    //   city:apply[:<unit>]    build the units of the plan under Realm_Hwanggyeong (replaces a unit of the same name), manifest + sheet + ledger
    //   city:revert[:<unit>]   remove them again
    //   city:rays              seal rays across the wall line (plan "rays") -> Out/city_rays.json
    //   city:stats | city:open | city:close
    public static partial class Hanyang308
    {
        const string CityMeshDir = "Assets/_Project/Art/World/Hanyang308/Meshes", CityPrefabDir = "Assets/_Project/Art/World/Hanyang308/Prefabs", LiteDir = "Assets/_Project/Art/World/PackLite308", MatDir = "Assets/_Project/Art/World/Finish297/Materials/";
        const string MeshVer = "v2", MainParent = "Hanyang308";
        const float WH = 6.5f, WD = 6f, TA = 10f / 3f, PAR = 1.2f;
        static string CityFile => Path.Combine(StageDir, "city308.json");
        static string CityLedgerFile => Path.Combine(StageDir, "Ledger", "city.json");

        [Serializable] sealed class CItem { public string n = "", a = "", col = ""; public float[] p = new float[3]; public float yaw; public float[] s = { 1, 1, 1 }; public float pitch; }
        [Serializable] sealed class CGroup { public string name = ""; public float cull = 0.012f; public List<CItem> items = new List<CItem>(); }
        [Serializable] sealed class CUnit { public string name = ""; public List<CGroup> groups = new List<CGroup>(); }
        [Serializable] sealed class CRay { public float x, z, nx, nz, y; public string want = "block", tag = ""; }
        [Serializable] sealed class CJoin { public string id = "", chunk = ""; public float x, z; }
        [Serializable] sealed class CRib { public string name = "", mat = "road"; public int n, m; public float[] v = new float[0]; public float[] a = new float[0]; }
        [Serializable] sealed class CBr { public string id = ""; public float L, w, pierH, step, camber; public int rail, cols; }
        [Serializable] sealed class CPlan { public List<CRib> ribbons = new List<CRib>(); public List<CBr> bridges = new List<CBr>(); public string version = ""; public List<string> prefabs = new List<string>(); public List<CUnit> units = new List<CUnit>(); public List<CRay> rays = new List<CRay>(); public List<CJoin> joins = new List<CJoin>(); }
        [Serializable] sealed class CLedger { public string stamp = ""; public List<string> units = new List<string>(); public List<string> backups = new List<string>(); }

        static CPlan LoadCity() { if (!File.Exists(CityFile)) throw new Refuse("no " + CityFile); return JsonUtility.FromJson<CPlan>(File.ReadAllText(CityFile)); }

        static string City(string[] a)
        {
            string verb = a.Length > 1 ? a[1] : "stats", arg = a.Length > 2 ? a[2] : "";
            switch (verb)
            {
                case "import": return CityImport(LoadCity());
                case "meshes": return CityMeshes();
                case "probe": return CityProbe();
                case "apply": return CityApply(arg, true);
                case "revert": return CityApply(arg, false);
                case "rays": return CityRays();
                case "stats": return CityStats();
                case "open": { var o = new List<Scene>(); OpenAdditive(RealmScene, Main(), o); return "opened " + o.Count; }
                case "close": { var s = SceneManager.GetSceneByPath(RealmScene); if (s.IsValid() && s.isLoaded) { if (s.isDirty) throw new Refuse("realm scene is dirty"); EditorSceneManager.CloseScene(s, true); return "closed"; } return "not open"; }
                default: throw new Refuse("city:" + verb + "?");
            }
        }

        // ---------- materials ----------
        static Material Mat(string name) { var m = AssetDatabase.LoadAssetAtPath<Material>(MatDir + name + ".mat"); if (m == null) throw new Refuse("material " + name + " missing"); return m; }
        static readonly Dictionary<string, string> PlgMap = new Dictionary<string, string> { { "PLG_Stone", "stone_dressed" }, { "PLG_Brick", "stone_plain" }, { "PLG_Wood", "wood_red" }, { "PLG_Plaster", "plaster" }, { "PLG_Tile", "tile_dark" }, { "PLG_Earth", "fieldstone" }, { "PLG_Paper", "plaster" } };

        // ---------- import ----------
        static string CityImport(CPlan plan)
        {
            var sb = new StringBuilder("Hanyang308 city:import\n"); Directory.CreateDirectory(Abs(CityPrefabDir));
            AssetDatabase.ImportAsset(CityPrefabDir, ImportAssetOptions.ForceSynchronousImport);
            foreach (var name in plan.prefabs)
            {
                string cls = name.StartsWith("Gen_") ? "gen" : "fortress"; var models = new GameObject[3]; bool ok = true;
                for (int k = 0; k < 3; k++)
                {
                    string path = LiteDir + "/" + cls + "/" + name + "_LOD" + k + ".fbx";
                    if (!File.Exists(Abs(path))) { sb.AppendLine("  MISSING " + path); ok = false; break; }
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    var imp = AssetImporter.GetAtPath(path) as ModelImporter; if (imp == null) { sb.AppendLine("  no importer " + path); ok = false; break; }
                    imp.globalScale = 1f; imp.useFileUnits = true; imp.bakeAxisConversion = false; imp.importNormals = ModelImporterNormals.Import; imp.importTangents = ModelImporterTangents.CalculateMikk;
                    imp.isReadable = false; imp.meshCompression = k == 0 ? ModelImporterMeshCompression.Low : ModelImporterMeshCompression.Medium; imp.optimizeMeshPolygons = true; imp.optimizeMeshVertices = true; imp.weldVertices = true;
                    imp.importBlendShapes = false; imp.importCameras = false; imp.importLights = false; imp.importAnimation = false; imp.animationType = ModelImporterAnimationType.None; imp.addCollider = false;
                    imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription; imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
                    foreach (var kv in PlgMap) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), Mat(kv.Value));
                    imp.SaveAndReimport(); models[k] = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                }
                if (!ok) continue;
                var root = new GameObject(name); var lods = new LOD[3]; float[] cut = { 0.25f, 0.08f, 0.015f }; string said = "";
                for (int k = 0; k < 3; k++)
                {
                    var child = (GameObject)PrefabUtility.InstantiatePrefab(models[k]); child.name = name + "_LOD" + k; child.transform.SetParent(root.transform, false);
                    var rs = child.GetComponentsInChildren<Renderer>(); foreach (var r in rs) { if (k == 2) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; foreach (var m in r.sharedMaterials) if (m == null || !AssetDatabase.GetAssetPath(m).StartsWith(MatDir)) said += " UNMAPPED:" + (m ? m.name : "null"); }
                    lods[k] = new LOD(cut[k], rs);
                    var mf = child.GetComponentInChildren<MeshFilter>(); if (k == 0 && mf) { var b = mf.sharedMesh.bounds; said += " bounds " + F(b.size.x) + "x" + F(b.size.y) + "x" + F(b.size.z) + " min.y " + F(b.min.y, "F2") + " tris " + Enumerable.Range(0, mf.sharedMesh.subMeshCount).Sum(q => (long)mf.sharedMesh.GetIndexCount(q)) / 3 + " rot " + mf.transform.localEulerAngles + " scl " + mf.transform.lossyScale; }
                }
                var lg = root.AddComponent<LODGroup>(); lg.fadeMode = LODFadeMode.None; lg.SetLODs(lods); lg.RecalculateBounds();
                PrefabUtility.SaveAsPrefabAsset(root, CityPrefabDir + "/" + name + ".prefab"); UnityEngine.Object.DestroyImmediate(root);
                sb.AppendLine("  " + name + said);
            }
            return sb.ToString();
        }

        // ---------- generated meshes ----------
        sealed class MB
        {
            public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>(); public readonly List<int>[] t = { new List<int>(), new List<int>() }; public float S = 1f / 2.4f;
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, int sub = 0)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0) { var x = b; b = d; d = x; }
                int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                foreach (var p in new[] { a, b, c, d }) uv.Add(Mathf.Abs(n.y) > 0.7f ? new Vector2(p.x, p.z) * S : new Vector2(Mathf.Abs(n.x) > Mathf.Abs(n.z) ? p.z : p.x, p.y) * S);
                t[sub].AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            public void Box(Vector3 lo, Vector3 hi, int sub = 0, bool bottom = false)
            {
                Vector3 P(int x, int y, int z) => new Vector3(x == 0 ? lo.x : hi.x, y == 0 ? lo.y : hi.y, z == 0 ? lo.z : hi.z);
                Quad(P(0, 0, 0), P(1, 0, 0), P(1, 1, 0), P(0, 1, 0), Vector3.back, sub); Quad(P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1), Vector3.forward, sub);
                Quad(P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0), Vector3.left, sub); Quad(P(1, 0, 0), P(1, 0, 1), P(1, 1, 1), P(1, 1, 0), Vector3.right, sub);
                Quad(P(0, 1, 0), P(1, 1, 0), P(1, 1, 1), P(0, 1, 1), Vector3.up, sub); if (bottom) Quad(P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1), Vector3.down, sub);
            }
            public Mesh Build(string name)
            {
                var m = new Mesh { name = name }; m.SetVertices(v); m.SetUVs(0, uv); int subs = t[1].Count > 0 ? 2 : 1; m.subMeshCount = subs; for (int k = 0; k < subs; k++) m.SetTriangles(t[k], k);
                m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds(); return m;
            }
        }
        // a parapet run along +X from x0 to x1 at depth z0..z1 (outer face = z0 when outerNeg), merlons of length ~TA with a crenel gap at each end
        static void Parapet(MB b, float x0, float x1, float y, float z0, float z1, Vector3 outer, bool alongZ = false)
        {
            float len = x1 - x0; int n = Mathf.Max(1, Mathf.RoundToInt(len / TA)); float lt = len / n, gap = 0.25f;
            for (int i = 0; i < n; i++)
            {
                float a = x0 + i * lt, c = a + lt - gap, e = a + lt;
                Vector3 L(float u, float yy, float w) => alongZ ? new Vector3(w, yy, u) : new Vector3(u, yy, w);
                Vector3 lo = L(a, y, z0), hi = L(c, y + PAR, z1); b.Box(Vector3.Min(lo, hi), Vector3.Max(lo, hi));
                lo = L(a - 0.02f, y + PAR, z0 - 0.07f * Mathf.Sign(z1 - z0)); hi = L(c + 0.02f, y + PAR + 0.13f, z1 + 0.07f * Mathf.Sign(z1 - z0)); b.Box(Vector3.Min(lo, hi), Vector3.Max(lo, hi));
                lo = L(c, y, z0); hi = L(e, y + 0.55f, z1); b.Box(Vector3.Min(lo, hi), Vector3.Max(lo, hi));
                float[] cx = { 0.2f, 0.5f, 0.8f };
                for (int k = 0; k < 3; k++)
                {
                    float u = a + (lt - gap) * cx[k], hw = 0.11f, y0 = y + (k == 1 ? 0.18f : 0.42f), y1 = y + (k == 1 ? 0.62f : 0.74f), w = z0 - 0.012f * Mathf.Sign(z1 - z0);
                    b.Quad(L(u - hw, y0, w), L(u + hw, y0, w), L(u + hw, y1, w), L(u - hw, y1, w), outer, 1);
                }
            }
        }
        // v2: the faces in two bands (big blocks low, 45 cm blocks above: 세종 / 숙종 courses), 미석, 여장 with 옥개석
        static void Face(MB b, float x0, float x1, float zB, float zT, Vector3 n, float split)
        {
            float t = (split + WD) / (WH + WD), zM = Mathf.Lerp(zB, zT, t);
            b.S = 1f / 4.8f; b.Quad(new Vector3(x0, -WD, zB), new Vector3(x1, -WD, zB), new Vector3(x1, split, zM), new Vector3(x0, split, zM), n);
            b.S = 1f / 2.4f; b.Quad(new Vector3(x0, split, zM), new Vector3(x1, split, zM), new Vector3(x1, WH, zT), new Vector3(x0, WH, zT), n);
        }
        static Mesh WallMesh(int n)
        {
            var b = new MB(); float L = n * TA, zoT = -2f, zoB = -2f - (WH + WD) * 0.08f, ziT = 2f, ziB = 2f + (WH + WD) * 0.04f;
            Face(b, 0, L, zoB, zoT, new Vector3(0, 0.08f, -1), 1.9f); Face(b, 0, L, ziB, ziT, new Vector3(0, 0.04f, 1), 3.2f);
            b.Quad(new Vector3(0, WH, zoT), new Vector3(L, WH, zoT), new Vector3(L, WH, ziT), new Vector3(0, WH, ziT), Vector3.up);
            b.Quad(new Vector3(0, -WD, zoB), new Vector3(0, -WD, ziB), new Vector3(0, WH, ziT), new Vector3(0, WH, zoT), Vector3.left);
            b.Quad(new Vector3(L, -WD, zoB), new Vector3(L, -WD, ziB), new Vector3(L, WH, ziT), new Vector3(L, WH, zoT), Vector3.right);
            b.Box(new Vector3(0, WH - 0.2f, zoT - 0.16f), new Vector3(L, WH, zoT));                                   // 미석
            Parapet(b, 0, L, WH, zoT, zoT + 0.75f, Vector3.back);
            b.Box(new Vector3(0, WH, ziT - 0.3f), new Vector3(L, WH + 0.25f, ziT));                                    // inner kerb of the walk
            return b.Build("H308_W" + n + "_" + MeshVer);
        }
        static Mesh ChiMesh()
        {
            var b = new MB(); float x0 = -4f, x1 = 4f, z0 = -8.5f, z1 = -1.5f;
            b.Box(new Vector3(x0, -WD, z0), new Vector3(x1, WH, z1)); b.Box(new Vector3(x0 - 0.14f, WH - 0.16f, z0 - 0.14f), new Vector3(x1 + 0.14f, WH, z1));
            Parapet(b, x0, x1, WH, z0, z0 + 0.75f, Vector3.back);
            Parapet(b, z0 + 0.75f, z1, WH, x0, x0 + 0.75f, Vector3.left, true); Parapet(b, z0 + 0.75f, z1, WH, x1, x1 - 0.75f, Vector3.right, true);
            return b.Build("H308_Chi_" + MeshVer);
        }
        static Mesh DrumMesh()
        {
            var b = new MB(); const int N = 12; float r = 3.2f, top = WH + PAR;
            for (int i = 0; i < N; i++)
            {
                float a0 = i * Mathf.PI * 2 / N, a1 = (i + 1) * Mathf.PI * 2 / N; var p0 = new Vector3(Mathf.Cos(a0) * r, 0, Mathf.Sin(a0) * r); var p1 = new Vector3(Mathf.Cos(a1) * r, 0, Mathf.Sin(a1) * r); var n = (p0 + p1).normalized;
                b.Quad(p0 + Vector3.down * WD, p1 + Vector3.down * WD, p1 + Vector3.up * top, p0 + Vector3.up * top, n);
                b.Quad(Vector3.up * top, p0 + Vector3.up * top, p1 + Vector3.up * top, (p0 + p1) * 0.25f + Vector3.up * top, Vector3.up);
            }
            return b.Build("H308_Drum_" + MeshVer);
        }
        static Mesh PierMesh()
        {
            var b = new MB(); float h = 3.6f; b.Box(new Vector3(-h, -16f, -h), new Vector3(h, 0, h)); b.Box(new Vector3(-h - 0.14f, -0.16f, -h - 0.14f), new Vector3(h + 0.14f, 0, h + 0.14f));
            Parapet(b, -h, h, 0, -h, -h + 0.75f, Vector3.back); Parapet(b, -h, h, 0, h, h - 0.75f, Vector3.forward);
            Parapet(b, -h + 0.75f, h - 0.75f, 0, -h, -h + 0.75f, Vector3.left, true); Parapet(b, -h + 0.75f, h - 0.75f, 0, h, h - 0.75f, Vector3.right, true);
            return b.Build("H308_Pier_" + MeshVer);
        }
        // long low stone-slab bridge (살곶이다리 · 수표교 family): rows of square stone posts set as lozenges, a cap beam (멍엣돌) on each row, long beams (귀틀석), deck slabs (청판석) with open joints,
        // a slight camber; rail 1 = 수표교 rail (엄지기둥 + 동자기둥 + 돌난대). +Z along the bridge, y 0 = deck top at the ends.
        static void Lozenge(MB b, float x, float z, float r, float y0, float y1)
        {
            var c = new[] { new Vector3(x - r, 0, z), new Vector3(x, 0, z - r), new Vector3(x + r, 0, z), new Vector3(x, 0, z + r) };
            for (int i = 0; i < 4; i++) { var p = c[i]; var q = c[(i + 1) % 4]; var n = ((p + q) * 0.5f - new Vector3(x, 0, z)).normalized; b.Quad(p + Vector3.up * y0, q + Vector3.up * y0, q + Vector3.up * y1, p + Vector3.up * y1, n); }
        }
        static Mesh BridgeMesh2(CBr d)
        {
            var b = new MB(); float h = d.L * 0.5f, hw = d.w * 0.5f; int rows = Mathf.Max(2, Mathf.RoundToInt(d.L / d.step)); float dz = d.L / rows; int cols = Mathf.Max(2, d.cols);
            float Cam(float z) => d.camber * (1f - (z / h) * (z / h));
            for (int r = 0; r <= rows; r++)
            {
                float z = Mathf.Clamp(-h + r * dz, -h + 0.5f, h - 0.5f), y = Cam(z);
                b.Box(new Vector3(-hw - 0.25f, y - 1.3f, z - 0.42f), new Vector3(hw + 0.25f, y - 0.82f, z + 0.42f), 0, true);
                for (int k = 0; k < cols; k++) { float x = -hw + 0.55f + k * (d.w - 1.1f) / (cols - 1); Lozenge(b, x, z, 0.5f, -d.pierH, y - 1.3f); }
                if (r == rows) break;
                float za = -h + r * dz, zb = za + dz, ym = Cam((za + zb) * 0.5f);
                for (int k = 0; k < 3; k++) { float x = (k - 1) * (hw - 0.35f); b.Box(new Vector3(x - 0.3f, ym - 0.82f, za), new Vector3(x + 0.3f, ym - 0.36f, zb), 0, true); }
                int slabs = Mathf.Max(2, Mathf.RoundToInt(dz / 1.15f)); float sl = dz / slabs;
                for (int q = 0; q < slabs; q++) b.Box(new Vector3(-hw, ym - 0.36f, za + q * sl + 0.025f), new Vector3(hw, ym, za + (q + 1) * sl - 0.025f), 0, true);
                if (d.rail == 1) foreach (float sx in new[] { -1f, 1f })
                {
                    float x = sx * (hw - 0.22f);
                    b.Box(new Vector3(x - 0.17f, ym, za - 0.17f), new Vector3(x + 0.17f, ym + 1.25f, za + 0.17f)); b.Box(new Vector3(x - 0.2f, ym + 1.25f, za - 0.2f), new Vector3(x + 0.2f, ym + 1.42f, za + 0.2f));
                    b.Box(new Vector3(x - 0.13f, ym, za + dz * 0.5f - 0.13f), new Vector3(x + 0.13f, ym + 0.72f, za + dz * 0.5f + 0.13f));
                    b.Box(new Vector3(x - 0.1f, ym + 0.72f, za + 0.17f), new Vector3(x + 0.1f, ym + 0.92f, zb - 0.17f), 0, true);
                    b.Box(new Vector3(x - 0.07f, ym + 0.02f, za + 0.17f), new Vector3(x + 0.07f, ym + 0.2f, zb - 0.17f), 0, true);
                }
            }
            return b.Build("H308_Br_" + d.id + "_" + MeshVer);
        }
        // a draped strip: n stations x m cross points (road surface with soft edges in vertex alpha, kerbs, paving)
        static Mesh RibMesh(CRib r, string name)
        {
            var m = new Mesh { name = name }; int n = r.n, k = r.m; var v = new Vector3[n * k]; var c = new Color[n * k]; var uv = new Vector2[n * k]; var t = new List<int>();
            for (int i = 0; i < n; i++) { float across = 0; for (int j = 0; j < k; j++) { int q = i * k + j; v[q] = new Vector3(r.v[q * 3], r.v[q * 3 + 1], r.v[q * 3 + 2]); c[q] = new Color(1, 1, 1, j < r.a.Length ? r.a[j] : 1f); if (j > 0) across += (v[q] - v[q - 1]).magnitude; uv[q] = new Vector2(across / 2.4f, 0); } }
            float along = 0; for (int i = 0; i < n; i++) { if (i > 0) along += (v[i * k + k / 2] - v[(i - 1) * k + k / 2]).magnitude; for (int j = 0; j < k; j++) uv[i * k + j].y = along / 2.4f; }
            for (int i = 0; i + 1 < n; i++) for (int j = 0; j + 1 < k; j++) { int a = i * k + j, b2 = a + 1, d = a + k, e = d + 1; t.AddRange(new[] { a, d, b2, b2, d, e }); }
            if (v.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = v; m.colors = c; m.uv = uv; m.SetTriangles(t, 0); m.RecalculateNormals();
            var nr = m.normals; int flip = 0; for (int i = 0; i < nr.Length; i++) if (nr[i].y < 0) flip++;
            if (flip > nr.Length / 2) { for (int i = 0; i < t.Count; i += 3) { int x = t[i]; t[i] = t[i + 1]; t[i + 1] = x; } m.SetTriangles(t, 0); m.RecalculateNormals(); }
            m.RecalculateTangents(); m.RecalculateBounds(); return m;
        }
        static Mesh BlockMesh(string name, Vector3 lo, Vector3 hi) { var b = new MB(); b.Box(lo, hi, 0, true); return b.Build("H308_" + name + "_" + MeshVer); }

        sealed class MDef { public string id; public Func<Mesh> make; public string[] mats; public Vector3 colC, colS; }
        static List<MDef> MeshDefs()
        {
            var d = new List<MDef>(); float zc = (-2f - (WH + WD) * 0.08f + 2f + (WH + WD) * 0.04f) * 0.5f, zs = 4f + (WH + WD) * 0.12f, yc = (WH + PAR - WD) * 0.5f, ys = WH + PAR + WD;
            for (int n = 1; n <= 3; n++) { int k = n; d.Add(new MDef { id = "W" + k, make = () => WallMesh(k), mats = new[] { "stone_dressed", "iron" }, colC = new Vector3(k * TA * 0.5f, yc, zc), colS = new Vector3(k * TA, ys, zs) }); }
            d.Add(new MDef { id = "Chi", make = ChiMesh, mats = new[] { "stone_dressed", "iron" }, colC = new Vector3(0, yc, -5f), colS = new Vector3(8f, ys, 7f) });
            d.Add(new MDef { id = "Drum", make = DrumMesh, mats = new[] { "stone_dressed" }, colC = new Vector3(0, yc, 0), colS = new Vector3(5.2f, ys, 5.2f) });
            d.Add(new MDef { id = "Pier", make = PierMesh, mats = new[] { "stone_dressed", "iron" }, colC = new Vector3(0, -7.4f, 0), colS = new Vector3(7.2f, 17.2f, 7.2f) });
            d.Add(new MDef { id = "Block", make = () => BlockMesh("Block", new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 1, 0.5f)), mats = new[] { "stone_dressed" }, colC = new Vector3(0, 0.5f, 0), colS = Vector3.one });
            d.Add(new MDef { id = "Leaf", make = () => BlockMesh("Leaf", new Vector3(0, 0, -0.09f), new Vector3(1, 1, 0.09f)), mats = new[] { "wood_dark" }, colC = new Vector3(0.5f, 0.5f, 0), colS = new Vector3(1, 1, 0.18f) });
            d.Add(new MDef { id = "Band", make = () => BlockMesh("Band", new Vector3(0, 0, -0.11f), new Vector3(1, 1, 0.11f)), mats = new[] { "iron" }, colC = new Vector3(0.5f, 0.5f, 0), colS = new Vector3(1, 1, 0.22f) });
            d.Add(new MDef { id = "Plaque", make = () => BlockMesh("Plaque", new Vector3(-0.5f, 0, -0.06f), new Vector3(0.5f, 1, 0.06f)), mats = new[] { "wood_dark" }, colC = new Vector3(0, 0.5f, 0), colS = new Vector3(1, 1, 0.12f) });
            return d;
        }
        static string MeshPath(string id) => CityMeshDir + "/H308_" + id + "_" + MeshVer + ".asset";
        static string CityMeshes()
        {
            Directory.CreateDirectory(Abs(CityMeshDir)); AssetDatabase.ImportAsset("Assets/_Project/Art/World/Hanyang308", ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ImportRecursive);
            var sb = new StringBuilder("Hanyang308 city:meshes\n");
            foreach (var d in MeshDefs())
            {
                string path = MeshPath(d.id); var m = d.make(); int zero = m.normals.Count(n => n.sqrMagnitude < 0.25f);
                var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (old != null) { old.Clear(); EditorUtility.CopySerialized(m, old); EditorUtility.SetDirty(old); AssetDatabase.SaveAssetIfDirty(old); } else { AssetDatabase.CreateAsset(m, path); AssetDatabase.SaveAssetIfDirty(m); }
                sb.AppendLine("  " + d.id + " verts " + m.vertexCount + " tris " + Enumerable.Range(0, m.subMeshCount).Sum(q => (long)m.GetIndexCount(q)) / 3 + " zero normals " + zero + " bounds " + m.bounds.min + " .. " + m.bounds.max);
            }
            var plan = LoadCity();
            foreach (var br in plan.bridges) { var m = BridgeMesh2(br); SaveMesh(m, MeshPath("Br_" + br.id)); sb.AppendLine("  Br_" + br.id + " tris " + Tris(m) + " bounds " + m.bounds.min + " .. " + m.bounds.max); }
            int rt = 0; foreach (var r in plan.ribbons) { var m = RibMesh(r, "H308_Rib_" + r.name + "_" + MeshVer); SaveMesh(m, MeshPath("Rib_" + r.name)); rt += (int)Tris(m); }
            sb.AppendLine("  ribbons " + plan.ribbons.Count + " tris " + rt);
            var road = RoadMaterial(true); sb.AppendLine("  road material " + road.name + " queue " + road.renderQueue + "");
            return sb.ToString();
        }
        // packed-earth road: URP Lit with the terrain's soil texture, tinted to the worn earth of the realm; opaque queue 2050 (after the ground), no depth / shadow pass so the ink post only sees the ground under it
        const string RoadEarth = "Assets/_Project/Art/World/Hanyang308/Materials/H308_road_earth2.mat";
        static Material RoadMaterial(bool create)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(RoadEarth); if (m != null && !create) return m;
            if (m == null) { Directory.CreateDirectory(Abs("Assets/_Project/Art/World/Hanyang308/Materials")); AssetDatabase.ImportAsset("Assets/_Project/Art/World/Hanyang308/Materials", ImportAssetOptions.ForceSynchronousImport); var sh = Shader.Find("Universal Render Pipeline/Lit"); if (sh == null) throw new Refuse("URP Lit missing"); m = new Material(sh) { name = "H308_road_earth2" }; AssetDatabase.CreateAsset(m, RoadEarth); }
            var tx = AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath("db9d4b04be04881438a584f7ee930f4e")); var nm = AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath("94a314ce50e18904baf7c040d28bfd8f"));
            m.SetTexture("_BaseMap", null); m.SetTexture("_MainTex", null); if (nm != null) { m.SetTexture("_BumpMap", nm); m.SetFloat("_BumpScale", 0.9f); m.SetTextureScale("_BumpMap", new Vector2(0.5f, 0.5f)); m.EnableKeyword("_NORMALMAP"); }
            var c = new Color(RoadTint[0], RoadTint[1], RoadTint[2], 1); m.SetColor("_BaseColor", c); m.SetColor("_Color", c); m.SetFloat("_Smoothness", 0.04f); m.SetFloat("_Metallic", 0f); m.SetTextureScale("_BaseMap", new Vector2(0.5f, 0.5f)); m.renderQueue = 2050;
            foreach (string pass in new[] { "DepthOnly", "DepthNormals", "DepthNormalsOnly", "ShadowCaster", "SHADOWCASTER" }) m.SetShaderPassEnabled(pass, false);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); return m;
        }
        static readonly float[] RoadTint = { 0.58f, 0.48f, 0.34f };
        static void SaveMesh(Mesh m, string path) { if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(m, path); AssetDatabase.SaveAssetIfDirty(m); }
        const string RoadMat = "Assets/_Project/Art/World/Finish297/GroundFit299/Soft/_Ground_095b1f479413443489c18b0f0198e8f7_country_soft299.mat";

        // ---------- probe: kept chunks at the joins ----------
        static string CityProbe()
        {
            var plan = LoadCity(); var main = Main(); var opened = new List<Scene>(); var realm = OpenAdditive(RealmScene, main, opened); var root = RealmRootOf(realm); var sb = new StringBuilder("Hanyang308 city:probe\n"); var outp = new StringBuilder("{\"joins\":[");
            try
            {
                bool first = true;
                foreach (var j in plan.joins)
                {
                    float top = float.MinValue, low = float.MaxValue; int n = 0; var lo = new Vector2(1e9f, 1e9f); var hi = -lo; string layer = "?", active = "?";
                    for (int c = 0; c < root.childCount; c++)
                    {
                        var ch = root.GetChild(c); if (ch.name != j.chunk) continue; layer = LayerMask.LayerToName(ch.gameObject.layer) + "/" + ch.gameObject.layer; active = ch.gameObject.activeInHierarchy.ToString();
                        foreach (var mf in ch.GetComponentsInChildren<MeshFilter>(true))
                        {
                            if (mf.sharedMesh == null || !mf.name.StartsWith("LOD0")) continue; string ap = AssetDatabase.GetAssetPath(mf.sharedMesh);
                            Vector3[] vs; try { vs = mf.sharedMesh.isReadable ? mf.sharedMesh.vertices : ReadVerts(mf.sharedMesh); } catch (Exception e) { sb.AppendLine("   unreadable " + ap + " " + e.Message); continue; }
                            var m = mf.transform.localToWorldMatrix;
                            foreach (var v in vs) { var w = m.MultiplyPoint3x4(v); float d = Mathf.Sqrt((w.x - j.x) * (w.x - j.x) + (w.z - j.z) * (w.z - j.z)); if (d > 5f) continue; n++; top = Mathf.Max(top, w.y); low = Mathf.Min(low, w.y); lo = Vector2.Min(lo, new Vector2(w.x, w.z)); hi = Vector2.Max(hi, new Vector2(w.x, w.z)); }
                        }
                        foreach (var col in ch.GetComponentsInChildren<Collider>(true)) sb.AppendLine("   collider " + col.GetType().Name + " on " + col.name + " enabled " + col.enabled + " bounds " + col.bounds.min + " .. " + col.bounds.max);
                    }
                    sb.AppendLine("  " + j.id + " " + j.chunk + " verts within 5 m " + n + " y " + F(low, "F2") + " .. " + F(top, "F2") + " xz " + lo + " .. " + hi + " layer " + layer + " active " + active);
                    outp.Append((first ? "" : ",") + "{\"id\":\"" + j.id + "\",\"top\":" + F(n > 0 ? top : 0, "F2") + ",\"low\":" + F(n > 0 ? low : 0, "F2") + ",\"n\":" + n + "}"); first = false;
                }
                foreach (string nm in new[] { "KoreanGateArchitecture" }) for (int c = 0; c < root.childCount; c++) { var ch = root.GetChild(c); if (ch.name != nm) continue; var bb = new Bounds(ch.position, Vector3.zero); foreach (var r in ch.GetComponentsInChildren<Renderer>()) bb.Encapsulate(r.bounds); sb.AppendLine("  gate arch #" + c + " pos " + ch.position + " yaw " + F(ch.eulerAngles.y) + " bounds " + bb.min + " .. " + bb.max + " layer " + ch.gameObject.layer); }
            }
            finally { foreach (var s in opened) EditorSceneManager.CloseScene(s, true); }
            outp.Append("]}"); File.WriteAllText(Path.Combine(OutDir, "city_probe.json"), outp.ToString());
            return sb.ToString();
        }
        static Vector3[] ReadVerts(Mesh m)
        {
            using (var data = MeshUtility.AcquireReadOnlyMeshData(m)) { var d = data[0]; var arr = new Unity.Collections.NativeArray<Vector3>(d.vertexCount, Unity.Collections.Allocator.Temp); d.GetVertices(arr); var r = arr.ToArray(); arr.Dispose(); return r; }
        }

        // ---------- apply / revert ----------
        [Serializable] sealed class MFileC { public string mainScene = "", stamp = ""; public List<MUnit> units = new List<MUnit>(); }
        static string PathOfT(Transform t) { string p = t.name; for (var x = t.parent; x != null; x = x.parent) p = x.name + "/" + p; return p; }

        static string CityApply(string only, bool build, CPlan plan = null, string tag = "city")
        {
            plan = plan ?? LoadCity(); var main = Main(); if (main.isDirty) throw new Refuse("the play scene is dirty");
            var units = plan.units.Where(u => only == "" || u.name == only || u.name == "Hanyang308_" + only).ToList(); if (units.Count == 0) throw new Refuse("no unit '" + only + "' in the plan");
            var defs = MeshDefs().ToDictionary(d => d.id); var meshes = new Dictionary<string, Mesh>();
            if (build) foreach (var d in defs.Values) { var m = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(d.id)); if (m == null) throw new Refuse("mesh " + d.id + " missing (city:meshes first)"); meshes[d.id] = m; }
            var opened = new List<Scene>(); var realm = OpenAdditive(RealmScene, main, opened); var root = RealmRootOf(realm); var sb = new StringBuilder("Hanyang308 city:" + (build ? "apply" : "revert") + "\n");
            try
            {
                if (root == null) throw new Refuse("no Realm_Hwanggyeong root"); if (realm.isDirty) throw new Refuse("the realm scene is dirty");
                var manifest = JsonUtility.FromJson<MFileC>(File.ReadAllText(ManifestFile)); var mine = manifest.units.Where(u => u.realm == "Hwanggyeong").ToList();
                if (mine.Count != root.childCount) throw new Refuse("manifest Hwanggyeong units " + mine.Count + " != realm children " + root.childCount);
                string backup = BackupFiles(tag, Abs(PlayScene), Abs(RealmScene), ManifestFile, CityLedgerFile, Abs("Assets/_Project/Scenes/World/Stream/RealmStreamSheet308.asset"));
                var ledger = File.Exists(CityLedgerFile) ? JsonUtility.FromJson<CLedger>(File.ReadAllText(CityLedgerFile)) : new CLedger(); ledger.backups.Add(backup); ledger.stamp = Stamp();
                // the empty parent in the play scene the units belong to when merged
                GameObject parent = main.GetRootGameObjects().FirstOrDefault(g => g.name == MainParent); bool mainChanged = false;
                if (parent == null && build) { parent = new GameObject(MainParent); SceneManager.MoveGameObjectToScene(parent, main); mainChanged = true; }
                // remove existing units of the same names (children of the realm root + manifest rows)
                foreach (var u in units)
                {
                    for (int c = root.childCount - 1; c >= 0; c--)
                    {
                        var ch = root.GetChild(c); if (ch.name != u.name) continue; var row = mine[c];
                        if (row.name != u.name || row.parentPath != MainParent) throw new Refuse("realm child " + c + " '" + ch.name + "' is not a Hanyang308 unit in the manifest");
                        manifest.units.Remove(row); mine.RemoveAt(c); UnityEngine.Object.DestroyImmediate(ch.gameObject); sb.AppendLine("  removed " + u.name); ledger.units.Remove(u.name);
                    }
                }
                if (build)
                {
                    if (mainChanged) { EditorSceneManager.MarkSceneDirty(main); if (!EditorSceneManager.SaveScene(main)) throw new Refuse("play scene not saved"); }
                    string gid = GlobalObjectId.GetGlobalObjectIdSlow(parent).ToString(); int layer = 0; Transform kept = null; for (int c = 0; c < root.childCount && kept == null; c++) if (root.GetChild(c).name.StartsWith("CapitalWall_0_")) kept = root.GetChild(c); if (kept != null) layer = kept.gameObject.layer;
                    var prefabs = new Dictionary<string, GameObject>(); var mats = new Dictionary<string, Material[]>(); foreach (var d in defs.Values) mats[d.id] = d.mats.Select(Mat).ToArray();
                    foreach (var u in units)
                    {
                        var ur = new GameObject(u.name); ur.layer = layer; SceneManager.MoveGameObjectToScene(ur, realm); ur.transform.SetParent(root, true); int items = 0;
                        foreach (var g in u.groups)
                        {
                            var gr = new GameObject(g.name); gr.layer = layer; gr.transform.SetParent(ur.transform, false); var own = new List<Renderer>(); var centre = Vector3.zero; int nc = 0;
                            foreach (var it in g.items) { centre += new Vector3(it.p[0], it.p[1], it.p[2]); nc++; }
                            if (nc > 0) gr.transform.position = centre / nc;
                            foreach (var it in g.items)
                            {
                                var pos = new Vector3(it.p[0], it.p[1], it.p[2]); var rot = Quaternion.Euler(it.pitch, it.yaw, 0); var scl = new Vector3(it.s[0], it.s[1], it.s[2]); GameObject go; items++;
                                if (it.a.StartsWith("mesh:"))
                                {
                                    string id = it.a.Substring(5);
                                    if (!defs.TryGetValue(id, out var d))
                                    {
                                        var fm = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(id)); if (fm == null) throw new Refuse("mesh id " + id + " (city:meshes first)");
                                        go = new GameObject(it.n == "" ? id : it.n); go.AddComponent<MeshFilter>().sharedMesh = fm; var fr = go.AddComponent<MeshRenderer>(); own.Add(fr);
                                        if (id.StartsWith("Rib_")) { var rm = RoadMaterial(false); fr.sharedMaterial = it.col == "road" ? rm : Mat(it.col == "pave" ? "paving" : "stone_dressed"); fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
                                        else if (id.StartsWith("P_") || id.StartsWith("T_") || id.StartsWith("L_")) { fr.sharedMaterial = PMat(it.col); if (PFlat(it.col)) fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
                                        else fr.sharedMaterial = Mat("stone_dressed");
                                        go.transform.SetParent(gr.transform, false); go.transform.SetPositionAndRotation(pos, rot); go.transform.localScale = scl;
                                        foreach (var t in go.GetComponentsInChildren<Transform>(true)) { t.gameObject.layer = layer; GameObjectUtility.SetStaticEditorFlags(t.gameObject, (StaticEditorFlags)0); }
                                        continue;
                                    }
                                    go = new GameObject(it.n == "" ? id : it.n); go.AddComponent<MeshFilter>().sharedMesh = meshes[id]; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = meshes[id].subMeshCount == 1 ? new[] { mats[id][0] } : mats[id]; own.Add(mr);
                                    if (it.col == "box") { var bc = go.AddComponent<BoxCollider>(); bc.center = d.colC; bc.size = d.colS; }
                                }
                                else if (it.a.StartsWith("prefab:"))
                                {
                                    string pn = it.a.Substring(7); if (!prefabs.TryGetValue(pn, out var pf)) { pf = AssetDatabase.LoadAssetAtPath<GameObject>(CityPrefabDir + "/" + pn + ".prefab"); if (pf == null) throw new Refuse("prefab " + pn + " missing (city:import first)"); prefabs[pn] = pf; }
                                    go = (GameObject)PrefabUtility.InstantiatePrefab(pf, realm); go.name = it.n == "" ? pn : it.n;
                                    if (it.col == "mesh") { var lod1 = go.transform.Find(pn + "_LOD1"); var mf = lod1 ? lod1.GetComponentInChildren<MeshFilter>() : null; if (mf == null) throw new Refuse("no LOD1 mesh in " + pn); var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; mc.convex = false; }
                                    else if (it.col == "box") { var lod0 = go.transform.Find(pn + "_LOD0"); var mf = lod0.GetComponentInChildren<MeshFilter>(); var bc = mf.gameObject.AddComponent<BoxCollider>(); bc.center = mf.sharedMesh.bounds.center; bc.size = mf.sharedMesh.bounds.size; }
                                }
                                else if (it.a == "col") { go = new GameObject(it.n == "" ? "Blocker" : it.n); go.AddComponent<BoxCollider>().size = Vector3.one; }
                                else throw new Refuse("item kind " + it.a);
                                go.transform.SetParent(gr.transform, false); go.transform.SetPositionAndRotation(pos, rot); go.transform.localScale = scl;
                                foreach (var t in go.GetComponentsInChildren<Transform>(true)) { t.gameObject.layer = layer; GameObjectUtility.SetStaticEditorFlags(t.gameObject, (StaticEditorFlags)0); }
                            }
                            if (own.Count > 0) { var lg = gr.AddComponent<LODGroup>(); lg.fadeMode = LODFadeMode.None; lg.SetLODs(new[] { new LOD(g.cull, own.ToArray()) }); lg.RecalculateBounds(); }
                        }
                        Count(ur.transform, out int rn, out long vn);
                        manifest.units.Add(new MUnit { realm = "Hwanggyeong", name = u.name, parentGid = gid, parentPath = MainParent, sibling = ledger.units.Count, renderers = rn, vertices = vn }); ledger.units.Add(u.name);
                        sb.AppendLine("  built " + u.name + " groups " + u.groups.Count + " items " + items + " renderers " + rn + " vertices " + vn);
                    }
                }
                manifest.stamp = Stamp(); File.WriteAllText(ManifestFile, JsonUtility.ToJson(manifest, true));
                Directory.CreateDirectory(Path.GetDirectoryName(CityLedgerFile)); File.WriteAllText(CityLedgerFile, JsonUtility.ToJson(ledger, true));
                EditorSceneManager.MarkSceneDirty(realm); bool ok = EditorSceneManager.SaveScene(realm);
                sb.Append(ok ? "saved" : "FAILED realm scene not saved").Append(" | backup " + backup + " | " + SyncSheet());
            }
            finally { foreach (var s in opened) EditorSceneManager.CloseScene(s, true); }
            return sb.ToString();
        }

        // ---------- seal rays ----------
        static string CityRays()
        {
            var plan = LoadCity(); var main = Main(); var opened = new List<Scene>(); var realm = OpenAdditive(RealmScene, main, opened); var sb = new StringBuilder("Hanyang308 city:rays " + plan.rays.Count + " stations\n"); int miss = 0, openHit = 0, blocked = 0, open = 0, doorShut = 0; var rows = new StringBuilder("{\"miss\":[");
            try
            {
                Physics.SyncTransforms(); float[] hs = { 0.6f, 2.0f, 3.9f };
                foreach (var r in plan.rays)
                {
                    var n = new Vector3(r.nx, 0, r.nz); bool allHit = true, anyHit = false; string who = "";
                    foreach (float h in hs)
                    {
                        var a = new Vector3(r.x, r.y + h, r.z) - n * 9f; bool hit = false;
                        foreach (var hinfo in Physics.RaycastAll(a, n, 18f, ~0, QueryTriggerInteraction.Ignore).Concat(Physics.RaycastAll(a + n * 18f, -n, 18f, ~0, QueryTriggerInteraction.Ignore)))
                        {
                            var t = hinfo.collider.transform; if (t.gameObject.scene != realm && t.root.name != DoorRootName) continue; string top = t.name; for (var x = t; x != null && x.parent != null && x.parent.parent != null; x = x.parent) top = x.parent.name;
                            for (var x = t; x != null; x = x.parent) if (x.name.StartsWith("Hanyang308_") || x.name.StartsWith("CapitalWall_") || x.name.StartsWith("KoreanGateArchitecture")) { hit = true; who = x.name; break; }
                            if (hit) break;
                        }
                        allHit &= hit; anyHit |= hit;
                    }
                    if (r.want == "block") { if (allHit) blocked++; else { miss++; rows.Append((miss > 1 ? "," : "") + "[" + F(r.x) + "," + F(r.z) + ",\"" + r.tag + "\"]"); if (miss <= 30) sb.AppendLine("  MISS " + r.tag + " at " + F(r.x) + "," + F(r.z)); } }
                    else { if (allHit && who == DoorRootName) doorShut++; else if (anyHit && who != "") openHit++; else open++; }
                }
            }
            finally { foreach (var s in opened) EditorSceneManager.CloseScene(s, true); }
            rows.Append("]}"); File.WriteAllText(Path.Combine(OutDir, "city_rays.json"), rows.ToString());
            return sb.Append("checks, blocked " + blocked + " | MISS " + miss + " | open stations clear " + open + " | open stations with a collider in the way " + openHit + " | gate stations shut by their victory door (" + DoorRootName + ") " + doorShut + (miss == 0 ? " | PASS" : " | FAIL")).ToString();
        }

        static string CityStats()
        {
            var main = Main(); var opened = new List<Scene>(); var realm = OpenAdditive(RealmScene, main, opened); var root = RealmRootOf(realm); var sb = new StringBuilder("Hanyang308 city:stats\n"); long allT = 0, allV = 0; int allR = 0, allC = 0;
            try
            {
                for (int c = 0; c < root.childCount; c++)
                {
                    var ch = root.GetChild(c); if (!ch.name.StartsWith("Hanyang308_")) continue; long tr = 0, vt = 0; int rn = 0, col = 0, objs = 0; var shared = new HashSet<Mesh>();
                    foreach (var t in ch.GetComponentsInChildren<Transform>(true)) objs++;
                    foreach (var mf in ch.GetComponentsInChildren<MeshFilter>(true)) { if (mf.sharedMesh == null) continue; bool lod0 = !(mf.name.EndsWith("_LOD1") || mf.name.EndsWith("_LOD2") || (mf.transform.parent && (mf.transform.parent.name.EndsWith("_LOD1") || mf.transform.parent.name.EndsWith("_LOD2")))); if (lod0) { for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) tr += mf.sharedMesh.GetIndexCount(s) / 3; rn++; } vt += mf.sharedMesh.vertexCount; shared.Add(mf.sharedMesh); }
                    col = ch.GetComponentsInChildren<Collider>(true).Length; long bytes = shared.Sum(m => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m));
                    sb.AppendLine("  " + ch.name.PadRight(22) + " objects " + objs + " LOD0 renderers " + rn + " LOD0 tris " + tr + " vertices(all LOD) " + vt + " colliders " + col + " unique meshes " + shared.Count + " mesh memory " + F(bytes / 1048576f, "F2") + " MB"); allT += tr; allV += vt; allR += rn; allC += col;
                }
            }
            finally { foreach (var s in opened) EditorSceneManager.CloseScene(s, true); }
            return sb.Append("total LOD0 tris " + allT + " | vertices " + allV + " | LOD0 renderers " + allR + " | colliders " + allC).ToString();
        }
    }
}
