using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 Hanyang phase 2 (SPEC-WORLD-HANYANG-308 §12): 경복궁 · 해태 · 육조거리 관아 from Tools/Unity/Stage308_hanyang/palace308.json
    // (written by Tools/Art/hanyang308_palace.py). Edit mode, queue-safe, no dialog.
    //   palace:import           gen modules of the plan ("prefabs") -> prefabs with LODGroup
    //   palace:derive           Fort_Munru2C (2-storey Janganmun tower without the stray plates of the light copy)
    //   palace:lite1            <name>_L1 prefabs (LOD1 as the top level) of the plan's "lite1" list
    //   palace:haetae           stone 해태: the summon's body mesh baked static, unit height, stone material, no emission
    //   palace:meshes           merged masonry solids of the plan -> Hanyang308/Meshes/H308_P_*  (always palace:apply afterwards)
    //   palace:apply[:<unit>] | palace:revert[:<unit>]     same builder as city:apply (units under Realm_Hwanggyeong, manifest + sheet + ledger + backup)
    public static partial class Hanyang308
    {
        static string PalaceFile => Path.Combine(StageDir, "palace308.json");
        const string WaterMat = "Assets/_Project/Art/World/Architecture296/Materials/38d197422cf2c6146b97da064ee495ad_Water.mat";
        const string HaetaeSrc = "Assets/_Project/Art/Demo/Summons/FireHaetae/SM_FireHaetae_Combat.fbx";
        [Serializable] sealed class PSolid { public string id = "", mat = ""; public float[] o = new float[3]; public float[] f = new float[0]; }
        [Serializable] sealed class PPlan { public string version = ""; public List<string> lite1 = new List<string>(); public List<PSolid> solids = new List<PSolid>(); }
        static CPlan LoadPalace() { if (!File.Exists(PalaceFile)) throw new Refuse("no " + PalaceFile); return JsonUtility.FromJson<CPlan>(File.ReadAllText(PalaceFile)); }

        static string Palace(string[] a)
        {
            string verb = a.Length > 1 ? a[1] : "", arg = a.Length > 2 ? a[2] : "";
            switch (verb)
            {
                case "import": return CityImport(LoadPalace());
                case "derive": return PalaceDerive();
                case "lite1": return PalaceLite1();
                case "haetae": return PalaceHaetae();
                case "meshes": return PalaceMeshes();
                case "apply": return CityApply(arg, true, LoadPalace(), "palace");
                case "revert": return CityApply(arg, false, LoadPalace(), "palace");
                default: throw new Refuse("palace:" + verb + "?");
            }
        }

        static Material PMat(string name)
        {
            if (name == "water") { var w = AssetDatabase.LoadAssetAtPath<Material>(WaterMat); if (w == null) throw new Refuse("water material missing"); return w; }
            if (name == "road") return RoadMaterial(false);
            if (name.StartsWith("h308_")) return TownMat(name);
            return Mat(name);
        }
        static bool PFlat(string name) => name == "water" || name == "road" || name == "paving" || name == "fieldstone";

        static void PTri(MB b, Vector3 p, Vector3 q, Vector3 r, Vector3 hint)
        {
            var n = Vector3.Cross(q - p, r - p); if (n.sqrMagnitude < 1e-8f) return; if (Vector3.Dot(n, hint) < 0) { var x = q; q = r; r = x; n = -n; }
            int i = b.v.Count; b.v.Add(p); b.v.Add(q); b.v.Add(r);
            foreach (var w in new[] { p, q, r }) b.uv.Add(Mathf.Abs(n.normalized.y) > 0.7f ? new Vector2(w.x, w.z) * b.S : new Vector2(Mathf.Abs(n.x) > Mathf.Abs(n.z) ? w.z : w.x, w.y) * b.S);
            b.t[0].AddRange(new[] { i, i + 1, i + 2 });
        }
        static void PQuad(MB b, Vector3 p, Vector3 q, Vector3 r, Vector3 s, Vector3 hint)
        {
            if (Vector3.Cross(q - p, r - p).sqrMagnitude < 1e-8f || Vector3.Cross(r - p, s - p).sqrMagnitude < 1e-8f) { PTri(b, p, q, r, hint); PTri(b, p, r, s, hint); return; }
            b.Quad(p, q, r, s, hint);
        }
        static Mesh SolidMesh(PSolid s)
        {
            var b = new MB(); var f = s.f; int i = 0; bool roof = s.mat.StartsWith("tile");
            b.S = roof ? 1f / 3.2f : 1f / 2.4f;
            while (i < f.Length)
            {
                int k = Mathf.RoundToInt(f[i]); i++;
                if (k == 5)
                {
                    var p = new Vector3[6]; for (int j = 0; j < 6; j++) p[j] = new Vector3(f[i + j * 3], f[i + j * 3 + 1], f[i + j * 3 + 2]); i += 18;
                    var mid = (p[0] + p[2] + p[3] + p[5]) * 0.25f;   // eave centre
                    PQuad(b, p[0], p[3], p[4], p[1], ((p[0] + p[3]) * 0.5f - mid).normalized + Vector3.up); PQuad(b, p[2], p[1], p[4], p[5], ((p[2] + p[5]) * 0.5f - mid).normalized + Vector3.up);
                    var ax = (p[4] - p[1]).normalized; PTri(b, p[0], p[1], p[2], -ax); PTri(b, p[3], p[4], p[5], ax);
                    continue;
                }
                if (k == 6)
                {
                    float x = f[i], y0 = f[i + 1], z = f[i + 2], r = f[i + 3], y1 = f[i + 4]; int n = Mathf.Max(3, Mathf.RoundToInt(f[i + 5])); i += 6; var c = new Vector3(x, 0, z);
                    for (int j = 0; j < n; j++)
                    {
                        float a0 = (j + 0.5f) * Mathf.PI * 2 / n, a1 = (j + 1.5f) * Mathf.PI * 2 / n; var p0 = c + new Vector3(Mathf.Cos(a0) * r, 0, Mathf.Sin(a0) * r); var p1 = c + new Vector3(Mathf.Cos(a1) * r, 0, Mathf.Sin(a1) * r);
                        b.Quad(p0 + Vector3.up * y0, p1 + Vector3.up * y0, p1 + Vector3.up * y1, p0 + Vector3.up * y1, ((p0 + p1) * 0.5f - c).normalized); PTri(b, c + Vector3.up * y1, p0 + Vector3.up * y1, p1 + Vector3.up * y1, Vector3.up);
                    }
                    continue;
                }
                var lo = new Vector3(f[i], f[i + 1], f[i + 2]); var hi = new Vector3(f[i + 3], f[i + 4], f[i + 5]); i += 6;
                switch (k)
                {
                    case 0: b.Box(lo, hi); break;
                    case 7: b.Box(lo, hi, 0, true); break;
                    case 3: b.Quad(new Vector3(lo.x, lo.y, lo.z), new Vector3(hi.x, lo.y, lo.z), new Vector3(hi.x, lo.y, hi.z), new Vector3(lo.x, lo.y, hi.z), Vector3.up); break;
                    case 1:
                    {
                        float zm = (lo.z + hi.z) * 0.5f; Vector3 r0 = new Vector3(lo.x, hi.y, zm), r1 = new Vector3(hi.x, hi.y, zm);
                        b.Quad(new Vector3(lo.x, lo.y, lo.z), new Vector3(hi.x, lo.y, lo.z), r1, r0, new Vector3(0, 1, -1)); b.Quad(new Vector3(lo.x, lo.y, hi.z), new Vector3(hi.x, lo.y, hi.z), r1, r0, new Vector3(0, 1, 1));
                        PTri(b, new Vector3(lo.x, lo.y, lo.z), new Vector3(lo.x, lo.y, hi.z), r0, Vector3.left); PTri(b, new Vector3(hi.x, lo.y, lo.z), new Vector3(hi.x, lo.y, hi.z), r1, Vector3.right); break;
                    }
                    case 2:
                    {
                        float xm = (lo.x + hi.x) * 0.5f; Vector3 r0 = new Vector3(xm, hi.y, lo.z), r1 = new Vector3(xm, hi.y, hi.z);
                        b.Quad(new Vector3(lo.x, lo.y, lo.z), new Vector3(lo.x, lo.y, hi.z), r1, r0, new Vector3(-1, 1, 0)); b.Quad(new Vector3(hi.x, lo.y, lo.z), new Vector3(hi.x, lo.y, hi.z), r1, r0, new Vector3(1, 1, 0));
                        PTri(b, new Vector3(lo.x, lo.y, lo.z), new Vector3(hi.x, lo.y, lo.z), r0, Vector3.back); PTri(b, new Vector3(lo.x, lo.y, hi.z), new Vector3(hi.x, lo.y, hi.z), r1, Vector3.forward); break;
                    }
                    case 4:
                    {
                        var ap = new Vector3((lo.x + hi.x) * 0.5f, hi.y, (lo.z + hi.z) * 0.5f); Vector3 c0 = new Vector3(lo.x, lo.y, lo.z), c1 = new Vector3(hi.x, lo.y, lo.z), c2 = new Vector3(hi.x, lo.y, hi.z), c3 = new Vector3(lo.x, lo.y, hi.z);
                        PTri(b, c0, c1, ap, new Vector3(0, 1, -1)); PTri(b, c1, c2, ap, new Vector3(1, 1, 0)); PTri(b, c2, c3, ap, new Vector3(0, 1, 1)); PTri(b, c3, c0, ap, new Vector3(-1, 1, 0)); break;
                    }
                    default: throw new Refuse("solid " + s.id + ": prim kind " + k);
                }
            }
            var m = new Mesh { name = "H308_" + s.id + "_" + MeshVer }; if (b.v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(b.v); m.SetUVs(0, b.uv); m.subMeshCount = 1; m.SetTriangles(b.t[0], 0); m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds(); return m;
        }

        static string PalaceMeshes()
        {
            var pp = JsonUtility.FromJson<PPlan>(File.ReadAllText(PalaceFile)); Directory.CreateDirectory(Abs(CityMeshDir)); var sb = new StringBuilder("Hanyang308 palace:meshes " + pp.version + "\n"); long tris = 0; int zero = 0; var keep = new HashSet<string>();
            foreach (var s in pp.solids) { PMat(s.mat); var m = SolidMesh(s); zero += m.normals.Count(n => n.sqrMagnitude < 0.25f); tris += Tris(m); string path = MeshPath(s.id); keep.Add(path); SaveMesh(m, path); }
            int gone = 0; foreach (var guid in AssetDatabase.FindAssets("H308_P_ t:Mesh", new[] { CityMeshDir })) { string p = AssetDatabase.GUIDToAssetPath(guid); if (Path.GetFileName(p).StartsWith("H308_P_") && !keep.Contains(p)) { AssetDatabase.DeleteAsset(p); gone++; } }
            return sb.Append("solids " + pp.solids.Count + " tris " + tris + " zero normals " + zero + " stale removed " + gone).ToString();
        }

        static string PalaceDerive()
        {
            var keep = new Keep { tri = (a, b, c, mn) => { foreach (var p in new[] { a, b, c }) if (Mathf.Abs(p.z) > 7.95f || Mathf.Abs(p.x) > 15.4f) return false; return true; } };
            var root = new GameObject("Fort_Munru2C"); var lods = new List<LOD>(); var sb = new StringBuilder("Hanyang308 palace:derive\n"); float[] cut = { 0.30f, 0.012f };
            for (int k = 0; k < 2; k++)
            {
                var m = FilterMesh("Fort_Munru2", k, "H308_Fort_Munru2C_L" + k + "_p2", keep, out var mats); var go = new GameObject("Fort_Munru2C_LOD" + k); go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = m; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats; lods.Add(new LOD(cut[k], new Renderer[] { mr }));
                sb.AppendLine("  " + m.name + " tris " + Tris(m) + " subs " + m.subMeshCount + " bounds " + m.bounds.min.ToString("F2") + ".." + m.bounds.max.ToString("F2"));
            }
            var lg = root.AddComponent<LODGroup>(); lg.fadeMode = LODFadeMode.None; lg.SetLODs(lods.ToArray()); lg.RecalculateBounds();
            PrefabUtility.SaveAsPrefabAsset(root, CityPrefabDir + "/Fort_Munru2C.prefab"); UnityEngine.Object.DestroyImmediate(root); return sb.ToString();
        }

        static string PalaceLite1()
        {
            var pp = JsonUtility.FromJson<PPlan>(File.ReadAllText(PalaceFile)); var sb = new StringBuilder("Hanyang308 palace:lite1\n");
            foreach (var name in pp.lite1)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(CityPrefabDir + "/" + name + ".prefab"); if (pf == null) { sb.AppendLine("  MISSING " + name + " (lite:import first)"); continue; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf); PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction); inst.name = name + "_L1";
                var l0 = inst.transform.Find(name + "_LOD0"); var l1 = inst.transform.Find(name + "_LOD1"); var l2 = inst.transform.Find(name + "_LOD2"); if (l0 == null || l1 == null || l2 == null) { sb.AppendLine("  no LOD children in " + name); UnityEngine.Object.DestroyImmediate(inst); continue; }
                UnityEngine.Object.DestroyImmediate(l0.gameObject); var lg = inst.GetComponent<LODGroup>();
                lg.SetLODs(new[] { new LOD(0.07f, l1.GetComponentsInChildren<Renderer>()), new LOD(0.012f, l2.GetComponentsInChildren<Renderer>()) }); lg.RecalculateBounds();
                PrefabUtility.SaveAsPrefabAsset(inst, CityPrefabDir + "/" + name + "_L1.prefab"); UnityEngine.Object.DestroyImmediate(inst); sb.AppendLine("  " + name + "_L1");
            }
            return sb.ToString();
        }

        // the summon's body in its bind pose, one static mesh: unit height, feet on y 0, centred in XZ; stone material (no emission)
        static string PalaceHaetae()
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(HaetaeSrc); if (src == null) throw new Refuse("no " + HaetaeSrc);
            var inst = UnityEngine.Object.Instantiate(src); inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); var sb = new StringBuilder("Hanyang308 palace:haetae\n");
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            void Add(Mesh m, Matrix4x4 M, string who)
            {
                if (m == null) return; Vector3[] mv; Vector2[] mu; int[] mt;
                if (m.isReadable) { mv = m.vertices; mu = m.uv; mt = m.triangles; }
                else using (var data = MeshUtility.AcquireReadOnlyMeshData(m))
                {
                    var d = data[0]; var av = new Unity.Collections.NativeArray<Vector3>(d.vertexCount, Unity.Collections.Allocator.Temp); d.GetVertices(av); mv = av.ToArray(); av.Dispose();
                    var au = new Unity.Collections.NativeArray<Vector2>(d.vertexCount, Unity.Collections.Allocator.Temp); if (d.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0)) d.GetUVs(0, au); mu = au.ToArray(); au.Dispose();
                    var tl = new List<int>(); for (int s = 0; s < d.subMeshCount; s++) { var sm = d.GetSubMesh(s); var ai = new Unity.Collections.NativeArray<int>(sm.indexCount, Unity.Collections.Allocator.Temp); d.GetIndices(ai, s); tl.AddRange(ai.ToArray()); ai.Dispose(); } mt = tl.ToArray();
                }
                int b0 = v.Count; foreach (var p in mv) v.Add(M.MultiplyPoint3x4(p)); for (int i = 0; i < mv.Length; i++) uv.Add(i < mu.Length ? mu[i] : Vector2.zero); foreach (int i in mt) t.Add(b0 + i);
                sb.AppendLine("  part " + who + " verts " + mv.Length + " tris " + mt.Length / 3);
            }
            foreach (var sk in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { var baked = new Mesh(); sk.BakeMesh(baked, true); Add(baked, sk.transform.localToWorldMatrix, "skinned " + sk.name); UnityEngine.Object.DestroyImmediate(baked); }
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true)) Add(mf.sharedMesh, mf.transform.localToWorldMatrix, "static " + mf.name);
            UnityEngine.Object.DestroyImmediate(inst); if (v.Count == 0) throw new Refuse("no mesh in the haetae model");
            var bb = new Bounds(v[0], Vector3.zero); foreach (var p in v) bb.Encapsulate(p); float k = 1f / Mathf.Max(0.01f, bb.size.y); var sh = new Vector3(-bb.center.x, -bb.min.y, -bb.center.z);
            for (int i = 0; i < v.Count; i++) { v[i] = (v[i] + sh) * k; uv[i] = new Vector2(v[i].x + v[i].z, v[i].y) * 1.3f; }
            var mesh = new Mesh { name = "H308_Haetae_p2", indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 }; mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0); mesh.RecalculateNormals();
            var nr = mesh.normals; int zero = 0; for (int i = 0; i < nr.Length; i++) if (nr[i].sqrMagnitude < 0.25f) { nr[i] = Vector3.up; zero++; } mesh.normals = nr; mesh.RecalculateTangents(); mesh.RecalculateBounds();
            SaveMesh(mesh, CityMeshDir + "/H308_Haetae_p2.asset");
            var root = new GameObject("H308_Haetae"); root.AddComponent<MeshFilter>().sharedMesh = mesh; root.AddComponent<MeshRenderer>().sharedMaterial = Mat("stone_plain");
            var lg = root.AddComponent<LODGroup>(); lg.fadeMode = LODFadeMode.None; lg.SetLODs(new[] { new LOD(0.01f, new Renderer[] { root.GetComponent<MeshRenderer>() }) }); lg.RecalculateBounds();
            PrefabUtility.SaveAsPrefabAsset(root, CityPrefabDir + "/H308_Haetae.prefab"); UnityEngine.Object.DestroyImmediate(root);
            return sb.Append("source bounds " + bb.size.ToString("F2") + " -> unit height, tris " + t.Count / 3 + " zero normals fixed " + zero).ToString();
        }
    }
}
