using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 Hanyang phases 3-4 (SPEC-WORLD-HANYANG-308 §13): 종묘 · 사직단 · 종각 · 시전 · 주거 from Tools/Unity/Stage308_hanyang/town308.json
    // (written by Tools/Art/hanyang308_town.py). Edit mode, queue-safe, no dialog.
    //   town:import            gen modules of the plan ("prefabs") -> prefabs with LODGroup
    //   town:lite1             <name>_L1 prefabs (LOD1 as the top level) of the plan's "lite1" list
    //   town:mats              tinted copies of the masonry materials (plan "tints": h308_*) - no emission
    //   town:meshes            merged solids of the plan -> Hanyang308/Meshes/H308_T_*   (always town:apply afterwards)
    //   town:blocks[:<prefix>] block prefabs TB_<id>: the block's buildings merged per material into one mesh per LOD step (step 0 = each building's
    //                          chosen level, step 1 = the palette proxies), LODGroup; one renderer per step
    //   town:apply[:<unit>] | town:revert[:<unit>]     same builder as city:apply
    public static partial class Hanyang308
    {
        static string TownFile => Path.Combine(StageDir, "town308.json");
        const string TownMatDir = "Assets/_Project/Art/World/Hanyang308/Materials";
        [Serializable] sealed class THouse { public string a = ""; public float[] p = new float[3]; public float yaw; public float[] s = { 1, 1, 1 }; public int l = -1; }
        [Serializable] sealed class TBlock { public string id = ""; public float[] o = new float[3]; public int top = 1; public float cut = 0.4f, cull = 0.02f; public List<THouse> houses = new List<THouse>(); }
        [Serializable] sealed class TTint { public string name = "", src = ""; public float[] c = { 1, 1, 1 }; }
        [Serializable] sealed class TPlan { public string version = ""; public List<string> lite1 = new List<string>(); public List<PSolid> solids = new List<PSolid>(); public List<TBlock> blocks = new List<TBlock>(); public List<TTint> tints = new List<TTint>(); }
        static CPlan LoadTown() { if (!File.Exists(TownFile)) throw new Refuse("no " + TownFile); return JsonUtility.FromJson<CPlan>(File.ReadAllText(TownFile)); }
        static TPlan LoadTownT() { if (!File.Exists(TownFile)) throw new Refuse("no " + TownFile); return JsonUtility.FromJson<TPlan>(File.ReadAllText(TownFile)); }

        static string Town(string[] a)
        {
            string verb = a.Length > 1 ? a[1] : "", arg = a.Length > 2 ? a[2] : "";
            switch (verb)
            {
                case "import": return CityImport(LoadTown());
                case "lite1": return TownLite1();
                case "mats": return TownMats();
                case "meshes": return TownMeshes();
                case "blocks": return TownBlocks(arg);
                case "apply": return CityApply(arg, true, LoadTown(), "town");
                case "revert": return CityApply(arg, false, LoadTown(), "town");
                default: throw new Refuse("town:" + verb + "?");
            }
        }

        static Material TownMat(string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(TownMatDir + "/" + name + ".mat"); if (m == null) throw new Refuse("material " + name + " missing (town:mats first)"); return m;
        }

        static string TownMats()
        {
            var tp = LoadTownT(); var sb = new StringBuilder("Hanyang308 town:mats\n");
            foreach (var t in tp.tints)
            {
                var src = Mat(t.src); string path = TownMatDir + "/" + t.name + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(src) { name = t.name }; AssetDatabase.CreateAsset(m, path); } else { m.CopyPropertiesFromMaterial(src); }
                var c = new Color(t.c[0], t.c[1], t.c[2], 1); if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black); m.DisableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); sb.AppendLine("  " + t.name + " <- " + t.src + " " + c);
            }
            return sb.ToString();
        }

        static string TownLite1()
        {
            var tp = LoadTownT(); var sb = new StringBuilder("Hanyang308 town:lite1\n");
            foreach (var name in tp.lite1)
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

        static string TownMeshes()
        {
            var tp = LoadTownT(); Directory.CreateDirectory(Abs(CityMeshDir)); var sb = new StringBuilder("Hanyang308 town:meshes " + tp.version + "\n"); long tris = 0; int zero = 0; var keep = new HashSet<string>();
            foreach (var s in tp.solids) { PMat(s.mat); var m = SolidMesh(s); zero += m.normals.Count(n => n.sqrMagnitude < 0.25f); tris += Tris(m); string path = MeshPath(s.id); keep.Add(path); SaveMesh(m, path); }
            int gone = 0; foreach (var guid in AssetDatabase.FindAssets("H308_T_ t:Mesh", new[] { CityMeshDir })) { string p = AssetDatabase.GUIDToAssetPath(guid); if (Path.GetFileName(p).StartsWith("H308_T_") && !keep.Contains(p)) { AssetDatabase.DeleteAsset(p); gone++; } }
            return sb.Append("solids " + tp.solids.Count + " tris " + tris + " zero normals " + zero + " stale removed " + gone).ToString();
        }

        sealed class TBuf { public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>(); public readonly List<int> t = new List<int>(); }

        static void TownAdd(Dictionary<Material, TBuf> into, List<Material> order, Mesh m, Material[] mats, Matrix4x4 M)
        {
            using (var data = MeshUtility.AcquireReadOnlyMeshData(m))
            {
                var d = data[0]; int vc = d.vertexCount;
                var av = new Unity.Collections.NativeArray<Vector3>(vc, Unity.Collections.Allocator.Temp); d.GetVertices(av);
                var an = new Unity.Collections.NativeArray<Vector3>(vc, Unity.Collections.Allocator.Temp); bool hasN = d.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal); if (hasN) d.GetNormals(an);
                var au = new Unity.Collections.NativeArray<Vector2>(vc, Unity.Collections.Allocator.Temp); bool hasU = d.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0); if (hasU) d.GetUVs(0, au);
                var NM = M.inverse.transpose; bool flip = M.determinant < 0;
                for (int s = 0; s < d.subMeshCount; s++)
                {
                    var mat = s < mats.Length ? mats[s] : mats[mats.Length - 1]; if (mat == null) continue;
                    if (!into.TryGetValue(mat, out var b)) { b = new TBuf(); into[mat] = b; order.Add(mat); }
                    var sm = d.GetSubMesh(s); var ai = new Unity.Collections.NativeArray<int>(sm.indexCount, Unity.Collections.Allocator.Temp); d.GetIndices(ai, s);
                    var remap = new Dictionary<int, int>();
                    for (int i = 0; i + 2 < ai.Length; i += 3)
                    {
                        for (int k = 0; k < 3; k++)
                        {
                            int src = ai[i + (flip ? 2 - k : k)];
                            if (!remap.TryGetValue(src, out int dst))
                            {
                                dst = b.v.Count; remap[src] = dst; b.v.Add(M.MultiplyPoint3x4(av[src]));
                                var nn = hasN ? NM.MultiplyVector(an[src]) : Vector3.up; b.n.Add(nn.sqrMagnitude < 1e-10f ? Vector3.up : nn.normalized); b.uv.Add(hasU ? au[src] : Vector2.zero);
                            }
                            b.t.Add(dst);
                        }
                    }
                    ai.Dispose();
                }
                av.Dispose(); an.Dispose(); au.Dispose();
            }
        }

        static Mesh TownMerged(Dictionary<Material, TBuf> bufs, List<Material> order, string name)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var subs = new List<int[]>();
            foreach (var mat in order) { var b = bufs[mat]; int b0 = v.Count; v.AddRange(b.v); n.AddRange(b.n); uv.AddRange(b.uv); subs.Add(b.t.Select(i => i + b0).ToArray()); }
            var m = new Mesh { name = name }; if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.subMeshCount = subs.Count; for (int s = 0; s < subs.Count; s++) m.SetTriangles(subs[s], s);
            m.RecalculateTangents(); m.RecalculateBounds(); return m;
        }

        static string TownBlocks(string prefix)
        {
            var tp = LoadTownT(); var sb = new StringBuilder("Hanyang308 town:blocks " + tp.version + "\n"); Directory.CreateDirectory(Abs(CityMeshDir)); var cache = new Dictionary<string, GameObject>(); long t0 = 0, t1 = 0; int nb = 0, missing = 0;
            foreach (var blk in tp.blocks)
            {
                if (prefix != "" && !blk.id.StartsWith(prefix)) continue; var o = new Vector3(blk.o[0], blk.o[1], blk.o[2]);
                var root = new GameObject("TB_" + blk.id); var lods = new List<LOD>(); bool bad = false;
                for (int step = 0; step < 2 && !bad; step++)
                {
                    var bufs = new Dictionary<Material, TBuf>(); var order = new List<Material>();
                    foreach (var h in blk.houses)
                    {
                        if (!cache.TryGetValue(h.a, out var pf)) { pf = AssetDatabase.LoadAssetAtPath<GameObject>(CityPrefabDir + "/" + h.a + ".prefab"); cache[h.a] = pf; }
                        if (pf == null) { sb.AppendLine("  MISSING prefab " + h.a + " (block " + blk.id + ")"); missing++; bad = true; break; }
                        int lod = step == 0 ? (h.l >= 0 ? h.l : blk.top) : 2; var ch = pf.transform.Find(h.a + "_LOD" + lod); if (ch == null) { sb.AppendLine("  no " + h.a + "_LOD" + lod); missing++; bad = true; break; }
                        var place = Matrix4x4.TRS(new Vector3(h.p[0], h.p[1], h.p[2]) - o, Quaternion.Euler(0, h.yaw, 0), new Vector3(h.s[0], h.s[1], h.s[2]));
                        foreach (var mf in ch.GetComponentsInChildren<MeshFilter>())
                        {
                            if (mf.sharedMesh == null) continue; var r = mf.GetComponent<Renderer>(); if (r == null) continue;
                            TownAdd(bufs, order, mf.sharedMesh, r.sharedMaterials, place * pf.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix);
                        }
                    }
                    if (bad || order.Count == 0) { bad = true; break; }
                    string id = "TB_" + blk.id + "_L" + step; var m = TownMerged(bufs, order, "H308_" + id + "_" + MeshVer); SaveMesh(m, MeshPath(id)); long tr = Tris(m); if (step == 0) t0 += tr; else t1 += tr;
                    var go = new GameObject(root.name + "_LOD" + step); go.transform.SetParent(root.transform, false); go.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(id));
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = order.ToArray(); if (step == 1) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    lods.Add(new LOD(step == 0 ? blk.cut : blk.cull, new Renderer[] { mr }));
                    sb.AppendLine("  " + id + " houses " + blk.houses.Count + " tris " + tr + " subs " + order.Count);
                }
                if (!bad)
                {
                    var lg = root.AddComponent<LODGroup>(); lg.fadeMode = LODFadeMode.None; lg.SetLODs(lods.ToArray()); lg.RecalculateBounds();
                    PrefabUtility.SaveAsPrefabAsset(root, CityPrefabDir + "/TB_" + blk.id + ".prefab"); nb++;
                }
                UnityEngine.Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
            return sb.Append("blocks " + nb + " top tris " + t0 + " proxy tris " + t1 + " missing " + missing).ToString();
        }
    }
}
