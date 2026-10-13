using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 Hanyang phase 5b (SPEC-WORLD-HANYANG-308 §15): street life - market goods, lane ground, the empty quarters, small touches.
    // ADDITIVE: only units named Hanyang308_Life_* of Tools/Unity/Stage308_hanyang/life308.json (written by Tools/Art/hanyang308_life.py). Edit mode, queue-safe, no dialog.
    //   life:inv:<asset folder>[:<tag>]   prefabs of a folder -> Out/life_inv_<tag>.json (triangles, materials, bounds) - read only
    //   life:mats              tinted copies of the masonry materials (plan "tints": h308_*) - no emission
    //   life:meshes            plan solids (ids L_*) and ribbons (Rib_L_*) -> Hanyang308/Meshes
    //   life:blocks[:<prefix>] block prefabs LB_<id>: pack props (PackLite name or an Assets/ prefab path) + stall kits (generated solids, instanced) merged per material,
    //                          one renderer per LOD step
    //   life:apply[:<unit>] | life:clear[:<unit>]     same builder as city:apply (clear = remove the units; nothing else is touched)
    //   life:status | life:prune (deletes LB_* / H308_LB_* / H308_L_* / H308_Rib_L_* assets the plan no longer names - only after life:apply)
    public static partial class Hanyang308
    {
        static string LifeFile => Path.Combine(StageDir, "life308.json");
        [Serializable] sealed class LPart { public string mat = ""; public float[] f = new float[0]; }
        [Serializable] sealed class LKit { public string id = ""; public List<LPart> parts = new List<LPart>(); }
        [Serializable] sealed class LInst { public string k = ""; public float[] p = new float[3]; public float yaw; public float[] s = { 1, 1, 1 }; }
        [Serializable] sealed class LBlock { public string id = ""; public float[] o = new float[3]; public int top = 1; public bool single; public float cut = 0.4f, cull = 0.02f; public List<THouse> houses = new List<THouse>(); public List<LInst> inst = new List<LInst>(); }
        [Serializable] sealed class LPlan { public string version = ""; public List<TTint> tints = new List<TTint>(); public List<PSolid> solids = new List<PSolid>(); public List<LKit> kits = new List<LKit>(); public List<LBlock> blocks = new List<LBlock>(); public List<CRib> ribbons = new List<CRib>(); }
        static CPlan LoadLife() { if (!File.Exists(LifeFile)) throw new Refuse("no " + LifeFile); return JsonUtility.FromJson<CPlan>(File.ReadAllText(LifeFile)); }
        static LPlan LoadLifeL() { if (!File.Exists(LifeFile)) throw new Refuse("no " + LifeFile); return JsonUtility.FromJson<LPlan>(File.ReadAllText(LifeFile)); }

        static string Life(string[] a)
        {
            string verb = a.Length > 1 ? a[1] : "", arg = a.Length > 2 ? a[2] : "";
            switch (verb)
            {
                case "inv": return LifeInv(arg, a.Length > 3 ? a[3] : "x");
                case "mats": return LifeMats();
                case "meshes": return LifeMeshes();
                case "blocks": return LifeBlocks(arg);
                case "apply": LifeGuard(); return CityApply(arg, true, LoadLife(), "life");
                case "clear": LifeGuard(); return CityApply(arg, false, LoadLife(), "life");
                case "status": return LifeStatus();
                case "prune": return LifePrune();
                default: throw new Refuse("life:" + verb + "?");
            }
        }

        static void LifeGuard()
        {
            foreach (var u in LoadLife().units) if (!u.name.StartsWith("Hanyang308_Life_")) throw new Refuse("life plan unit '" + u.name + "' is not a Hanyang308_Life_* unit");
        }

        static string LifeInv(string folder, string tag)
        {
            if (folder == "" || !AssetDatabase.IsValidFolder(folder)) throw new Refuse("folder '" + folder + "'?");
            var sb = new StringBuilder("["); int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid); var pf = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (pf == null) continue;
                var lg = pf.GetComponent<LODGroup>(); IEnumerable<MeshFilter> mfs = pf.GetComponentsInChildren<MeshFilter>(); int lods = 0;
                if (lg != null) { var ls = lg.GetLODs(); lods = ls.Length; if (ls.Length > 0) mfs = ls[0].renderers.Where(r => r != null).Select(r => r.GetComponent<MeshFilter>()).Where(x => x != null); }
                long tris = 0; var mats = new List<string>(); bool any = false; var bb = new Bounds(); bool emis = false;
                foreach (var mf in mfs)
                {
                    if (mf.sharedMesh == null) continue; tris += Tris(mf.sharedMesh); var r = mf.GetComponent<Renderer>();
                    if (r != null) foreach (var m in r.sharedMaterials) if (m != null) { if (!mats.Contains(m.name)) mats.Add(m.name); if (m.IsKeywordEnabled("_EMISSION")) emis = true; }
                    var M = pf.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix; var b = mf.sharedMesh.bounds;
                    for (int k = 0; k < 8; k++) { var c = M.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1))); if (!any) { bb = new Bounds(c, Vector3.zero); any = true; } else bb.Encapsulate(c); }
                }
                if (!any) continue;
                sb.Append(n++ > 0 ? ",\n" : "\n").Append("{\"name\":\"" + pf.name + "\",\"path\":\"" + path + "\",\"tris\":" + tris + ",\"lods\":" + lods + ",\"emis\":" + (emis ? "true" : "false") + ",\"min\":[" + F(bb.min.x, "F2") + "," + F(bb.min.y, "F2") + "," + F(bb.min.z, "F2") + "],\"max\":[" + F(bb.max.x, "F2") + "," + F(bb.max.y, "F2") + "," + F(bb.max.z, "F2") + "],\"mats\":[" + string.Join(",", mats.Select(m => "\"" + m + "\"")) + "]}");
            }
            sb.Append("\n]"); string outp = Path.Combine(OutDir, "life_inv_" + tag + ".json"); File.WriteAllText(outp, sb.ToString());
            return "Hanyang308 life:inv " + folder + " prefabs " + n + " -> " + outp;
        }

        static string LifeMats()
        {
            var lp = LoadLifeL(); var sb = new StringBuilder("Hanyang308 life:mats\n");
            foreach (var t in lp.tints)
            {
                var src = Mat(t.src); string path = TownMatDir + "/" + t.name + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(src) { name = t.name }; AssetDatabase.CreateAsset(m, path); } else { m.CopyPropertiesFromMaterial(src); }
                var c = new Color(t.c[0], t.c[1], t.c[2], 1); if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black); m.DisableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); sb.AppendLine("  " + t.name + " <- " + t.src + " " + c);
            }
            return sb.ToString();
        }

        static string LifeMeshes()
        {
            var lp = LoadLifeL(); Directory.CreateDirectory(Abs(CityMeshDir)); var sb = new StringBuilder("Hanyang308 life:meshes " + lp.version + "\n"); long tris = 0; int zero = 0;
            foreach (var s in lp.solids) { if (!s.id.StartsWith("L_")) throw new Refuse("solid id " + s.id + " is not L_*"); PMat(s.mat); var m = SolidMesh(s); zero += m.normals.Count(n => n.sqrMagnitude < 0.25f); tris += Tris(m); SaveMesh(m, MeshPath(s.id)); }
            long rt = 0; int sets = 0;
            foreach (var grp in lp.ribbons.GroupBy(LifeRibSet))      // parts named <set>__<k> share one origin and become one mesh Rib_<set>
            {
                if (!grp.Key.StartsWith("L_")) throw new Refuse("ribbon " + grp.Key + " is not L_*"); var parts = grp.Select(r => RibMesh(r, "part")).ToList(); var m = new Mesh { name = "H308_Rib_" + grp.Key + "_" + MeshVer };
                if (parts.Sum(q => q.vertexCount) > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.CombineMeshes(parts.Select(q => new CombineInstance { mesh = q, transform = Matrix4x4.identity }).ToArray(), true, true); m.RecalculateBounds(); foreach (var q in parts) UnityEngine.Object.DestroyImmediate(q);
                zero += m.normals.Count(n => n.sqrMagnitude < 0.25f); SaveMesh(m, MeshPath("Rib_" + grp.Key)); rt += Tris(m); sets++;
            }
            RoadMaterial(false);
            return sb.Append("solids " + lp.solids.Count + " tris " + tris + " | ribbon parts " + lp.ribbons.Count + " sets " + sets + " tris " + rt + " | zero normals " + zero).ToString();
        }

        static string LifeRibSet(CRib r) { int i = r.name.IndexOf("__", StringComparison.Ordinal); return i < 0 ? r.name : r.name.Substring(0, i); }

        static bool LifeAddPrefab(Dictionary<Material, TBuf> bufs, List<Material> order, GameObject pf, string name, int lod, Matrix4x4 place, bool proxy)
        {
            IEnumerable<MeshFilter> mfs; var ch = pf.transform.Find(name + "_LOD" + lod);
            if (ch != null) mfs = ch.GetComponentsInChildren<MeshFilter>();
            else
            {
                var lg = pf.GetComponent<LODGroup>(); var ls = lg != null ? lg.GetLODs() : null;
                if (ls != null && ls.Length > 0) mfs = ls[Mathf.Min(lod, ls.Length - 1)].renderers.Where(r => r != null).Select(r => r.GetComponent<MeshFilter>()).Where(x => x != null);
                else { if (proxy) return true; mfs = pf.GetComponentsInChildren<MeshFilter>(); }
            }
            bool any = false;
            foreach (var mf in mfs)
            {
                if (mf.sharedMesh == null) continue; var r = mf.GetComponent<Renderer>(); if (r == null) continue; var ms = r.sharedMaterials; if (ms.Length == 0 || ms.All(x => x == null)) continue;
                TownAdd(bufs, order, mf.sharedMesh, ms, place * pf.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix); any = true;
            }
            return any;
        }

        static string LifeBlocks(string prefix)
        {
            var lp = LoadLifeL(); var sb = new StringBuilder("Hanyang308 life:blocks " + lp.version + "\n"); Directory.CreateDirectory(Abs(CityMeshDir)); var cache = new Dictionary<string, GameObject>(); long t0 = 0, t1 = 0; int nb = 0, missing = 0, maxSubs = 0;
            var kits = new Dictionary<string, List<KeyValuePair<Mesh, Material>>>(); var temp = new List<Mesh>();
            foreach (var k in lp.kits) { var l = new List<KeyValuePair<Mesh, Material>>(); foreach (var p in k.parts) { var m = SolidMesh(new PSolid { id = "L_kit", mat = p.mat, f = p.f }); temp.Add(m); l.Add(new KeyValuePair<Mesh, Material>(m, PMat(p.mat))); } kits[k.id] = l; }
            try
            {
                foreach (var blk in lp.blocks)
                {
                    if (prefix != "" && !blk.id.StartsWith(prefix)) continue; var o = new Vector3(blk.o[0], blk.o[1], blk.o[2]);
                    var root = new GameObject("LB_" + blk.id); var lods = new List<LOD>(); bool bad = false;
                    for (int step = 0; step < (blk.single ? 1 : 2) && !bad; step++)
                    {
                        var bufs = new Dictionary<Material, TBuf>(); var order = new List<Material>();
                        foreach (var h in blk.houses)
                        {
                            bool raw = h.a.StartsWith("Assets/"); string name = raw ? Path.GetFileNameWithoutExtension(h.a) : h.a;
                            if (!cache.TryGetValue(h.a, out var pf)) { pf = AssetDatabase.LoadAssetAtPath<GameObject>(raw ? h.a : CityPrefabDir + "/" + h.a + ".prefab"); cache[h.a] = pf; }
                            if (pf == null) { sb.AppendLine("  MISSING prefab " + h.a + " (block " + blk.id + ")"); missing++; bad = true; break; }
                            int lod = step == 0 ? (h.l >= 0 ? h.l : blk.top) : 2;
                            var place = Matrix4x4.TRS(new Vector3(h.p[0], h.p[1], h.p[2]) - o, Quaternion.Euler(0, h.yaw, 0), new Vector3(h.s[0], h.s[1], h.s[2]));
                            if (!LifeAddPrefab(bufs, order, pf, name, lod, place, step == 1)) { sb.AppendLine("  no mesh in " + h.a + " LOD" + lod); missing++; bad = true; break; }
                        }
                        if (bad) break;
                        foreach (var q in blk.inst)
                        {
                            if (!kits.TryGetValue(q.k, out var parts)) { sb.AppendLine("  MISSING kit " + q.k + " (block " + blk.id + ")"); missing++; bad = true; break; }
                            var place = Matrix4x4.TRS(new Vector3(q.p[0], q.p[1], q.p[2]) - o, Quaternion.Euler(0, q.yaw, 0), new Vector3(q.s[0], q.s[1], q.s[2]));
                            foreach (var kv in parts) TownAdd(bufs, order, kv.Key, new[] { kv.Value }, place);
                        }
                        if (bad) break; if (order.Count == 0) { if (step == 0) bad = true; break; }
                        string id = "LB_" + blk.id + "_L" + step; var m = TownMerged(bufs, order, "H308_" + id + "_" + MeshVer); SaveMesh(m, MeshPath(id)); long tr = Tris(m); if (step == 0) { t0 += tr; maxSubs = Mathf.Max(maxSubs, order.Count); } else t1 += tr;
                        var go = new GameObject(root.name + "_LOD" + step); go.transform.SetParent(root.transform, false); go.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(id));
                        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = order.ToArray(); if (step == 1) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        lods.Add(new LOD(step == 0 ? blk.cut : blk.cull, new Renderer[] { mr }));
                        sb.AppendLine("  " + id + " props " + blk.houses.Count + " kits " + blk.inst.Count + " tris " + tr + " subs " + order.Count);
                    }
                    if (!bad && lods.Count > 0)
                    {
                        if (lods.Count == 1) lods[0] = new LOD(blk.cull, lods[0].renderers);
                        var lg = root.AddComponent<LODGroup>(); lg.fadeMode = LODFadeMode.None; lg.SetLODs(lods.ToArray()); lg.RecalculateBounds();
                        PrefabUtility.SaveAsPrefabAsset(root, CityPrefabDir + "/LB_" + blk.id + ".prefab"); nb++;
                    }
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
            finally { foreach (var m in temp) UnityEngine.Object.DestroyImmediate(m); }
            return sb.Append("blocks " + nb + " top tris " + t0 + " proxy tris " + t1 + " missing " + missing + " max submeshes " + maxSubs).ToString();
        }

        static string LifeStatus()
        {
            var plan = LoadLife(); var lp = LoadLifeL(); var led = File.Exists(CityLedgerFile) ? JsonUtility.FromJson<CLedger>(File.ReadAllText(CityLedgerFile)) : new CLedger();
            var sb = new StringBuilder("Hanyang308 life:status " + plan.version + " | kits " + lp.kits.Count + " blocks " + lp.blocks.Count + " solids " + lp.solids.Count + " ribbons " + lp.ribbons.Count + "\n");
            foreach (var u in plan.units) sb.AppendLine("  " + u.name + " groups " + u.groups.Count + " items " + u.groups.Sum(g => g.items.Count) + " cols " + u.groups.Sum(g => g.items.Count(i => i.a == "col")) + (led.units.Contains(u.name) ? " | APPLIED" : " | not applied"));
            sb.Append("ledger units named Life: " + string.Join(", ", led.units.Where(n => n.StartsWith("Hanyang308_Life_"))));
            return sb.ToString();
        }

        static string LifePrune()
        {
            var lp = LoadLifeL(); var keep = new HashSet<string>(); int gone = 0;
            foreach (var b in lp.blocks) { keep.Add(CityPrefabDir + "/LB_" + b.id + ".prefab"); keep.Add(MeshPath("LB_" + b.id + "_L0")); keep.Add(MeshPath("LB_" + b.id + "_L1")); }
            foreach (var s in lp.solids) keep.Add(MeshPath(s.id)); foreach (var r in lp.ribbons) keep.Add(MeshPath("Rib_" + LifeRibSet(r)));
            foreach (var guid in AssetDatabase.FindAssets("LB_ t:Prefab", new[] { CityPrefabDir })) { string p = AssetDatabase.GUIDToAssetPath(guid); if (Path.GetFileName(p).StartsWith("LB_") && !keep.Contains(p)) { AssetDatabase.DeleteAsset(p); gone++; } }
            foreach (var guid in AssetDatabase.FindAssets("H308_ t:Mesh", new[] { CityMeshDir })) { string p = AssetDatabase.GUIDToAssetPath(guid); string fn = Path.GetFileName(p); if ((fn.StartsWith("H308_LB_") || fn.StartsWith("H308_L_") || fn.StartsWith("H308_Rib_L_")) && !keep.Contains(p)) { AssetDatabase.DeleteAsset(p); gone++; } }
            return "Hanyang308 life:prune removed " + gone + " kept " + keep.Count;
        }
    }
}
