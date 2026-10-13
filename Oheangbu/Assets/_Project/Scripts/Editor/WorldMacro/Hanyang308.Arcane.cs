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
    // #308 Hanyang phase 7 (SPEC-WORLD-HANYANG-308 §17, D308-64 + D308-79): the ore-craft dressing of the capital - lamps on a conduit line, instruments, fittings.
    // ADDITIVE: only units named Hanyang308_Arcane_*. Plan Tools/Unity/Stage308_hanyang/arcane308.json (Tools/Art/hanyang308_arcane.py: kit meshes + blocks of instances).
    //   arcane:mats             the ore-egg material (a copy of the mine lamp's paper material, the realm's amber, never brighter than it) + two plain lacquer / unlit-egg materials
    //   arcane:blocks[:<prefix>] seat every instance on the ground the physics finds (own units ignored), drop what would stand in a collider, merge a block into one mesh per LOD
    //                           (prefab AB_<block>), write arcane308_units.json (the unit plan: one prefab + the post colliders per block) and Out/arcane_seat.json
    //   arcane:apply[:<unit>] | arcane:clear[:<unit>]   same builder as city:apply
    //   arcane:status | arcane:check | arcane:prune
    // Only material "core" glows. No Light component, no collider on the wall top, no static flags (CityApply clears them).
    public static partial class Hanyang308
    {
        const string ArcDir = "Assets/_Project/Art/World/Arcane308", ArcCore = ArcDir + "/Materials/MaseokCore_Hwanggyeong.mat", ArcUnit = "Hanyang308_Arcane_";
        static string ArcFile => Path.Combine(StageDir, "arcane308.json");
        static string ArcUnitsFile => Path.Combine(StageDir, "arcane308_units.json");
        static string ArcSeatFile => Path.Combine(OutDir, "arcane_seat.json");
        [Serializable] sealed class ASub { public string mat = ""; public float[] v = new float[0], n = new float[0]; public int[] t = new int[0]; }
        [Serializable] sealed class ALod { public List<ASub> subs = new List<ASub>(); }
        [Serializable] sealed class AKit { public string id = ""; public int lit; public List<ALod> lods = new List<ALod>(); }
        [Serializable] sealed class AInst { public string k = "", mode = "g", cls = ""; public float x, z, y, yaw, fit, s = 1, x2, z2; public bool seg; public float[] col = new float[0]; public int lit; }
        [Serializable] sealed class ABlock { public string id = "", unit = ""; public float[] o = new float[3]; public float cut = 0.3f, cull = 0.012f; public List<AInst> inst = new List<AInst>(); }
        [Serializable] sealed class ACore { public string src = ""; public float[] color = { 1, .5f, .1f }; }
        [Serializable] sealed class APlan { public string version = ""; public ACore core = new ACore(); public List<AKit> kit = new List<AKit>(); public List<ABlock> blocks = new List<ABlock>(); }
        [Serializable] sealed class ASeat { public string block = "", k = "", cls = "", why = ""; public float x, y, z, yaw; public bool placed; public int lit; public bool grounded; }
        [Serializable] sealed class ASeatFile { public string version = ""; public int placed, dropped, lit; public List<ASeat> rows = new List<ASeat>(); }

        static APlan LoadArc() { if (!File.Exists(ArcFile)) throw new Refuse("no " + ArcFile + " (python Tools/Art/hanyang308_arcane.py)"); return JsonUtility.FromJson<APlan>(File.ReadAllText(ArcFile)); }
        static CPlan LoadArcUnits()
        {
            if (!File.Exists(ArcUnitsFile)) throw new Refuse("no " + ArcUnitsFile + " (arcane:blocks first)"); var p = JsonUtility.FromJson<CPlan>(File.ReadAllText(ArcUnitsFile));
            foreach (var u in p.units) if (!u.name.StartsWith(ArcUnit)) throw new Refuse("unit '" + u.name + "' is not a " + ArcUnit + "* unit"); return p;
        }
        static string ArcTag(string version) { int i = version.LastIndexOf('.'); return i < 0 ? version : version.Substring(i + 1); }

        static string Arcane(string[] a)
        {
            string verb = a.Length > 1 ? a[1] : "", arg = a.Length > 2 ? a[2] : "";
            switch (verb)
            {
                case "mats": return ArcMats();
                case "blocks": return ArcBlocks(arg);
                case "apply": return CityApply(arg, true, LoadArcUnits(), "arcane");
                case "clear": return CityApply(arg, false, LoadArcUnits(), "arcane");
                case "status": return ArcStatus();
                case "check": return ArcCheck();
                case "prune": return ArcPrune();
                default: throw new Refuse("arcane:" + verb + "?");
            }
        }

        static float ArcLum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        static void ArcFolder(string path) { if (AssetDatabase.IsValidFolder(path)) return; string parent = Path.GetDirectoryName(path).Replace('\\', '/'); ArcFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path)); }

        static Dictionary<string, Material> ArcMaterials(bool create, StringBuilder sb = null)
        {
            var plan = LoadArc(); var jo = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(ArcFile))["materials"] as Newtonsoft.Json.Linq.JObject; var d = new Dictionary<string, Material>();
            if (create) { ArcFolder(ArcDir + "/Materials"); ArcFolder(ArcDir + "/Meshes"); }
            foreach (var kv in jo)
            {
                Material m;
                if (kv.Value.Type == Newtonsoft.Json.Linq.JTokenType.String && (string)kv.Value == "CORE")
                {
                    var src = AssetDatabase.LoadAssetAtPath<Material>(plan.core.src); if (src == null) throw new Refuse("core source material missing: " + plan.core.src);
                    m = AssetDatabase.LoadAssetAtPath<Material>(ArcCore);
                    if (create)
                    {
                        if (m == null) { m = new Material(src) { name = "MaseokCore_Hwanggyeong" }; AssetDatabase.CreateAsset(m, ArcCore); } else { m.shader = src.shader; m.CopyPropertiesFromMaterial(src); }
                        var c = new Color(plan.core.color[0], plan.core.color[1], plan.core.color[2], 1f); if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                        EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
                        if (sb != null) sb.AppendLine("  core <- " + plan.core.src + " shader " + m.shader.name + " colour " + c + " luminance " + F(ArcLum(c), "F3") + " (source " + F(ArcLum(src.GetColor("_BaseColor")), "F3") + ")");
                    }
                }
                else if (kv.Value.Type == Newtonsoft.Json.Linq.JTokenType.String) m = AssetDatabase.LoadAssetAtPath<Material>((string)kv.Value);
                else
                {
                    string path = ArcDir + "/Materials/A308_" + kv.Key + ".mat"; m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (create)
                    {
                        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "A308_" + kv.Key }; AssetDatabase.CreateAsset(m, path); }
                        var col = (Newtonsoft.Json.Linq.JArray)kv.Value["color"]; m.SetColor("_BaseColor", new Color((float)col[0], (float)col[1], (float)col[2], 1f)); m.SetFloat("_Smoothness", (float)kv.Value["smooth"]); m.SetFloat("_Metallic", (float)kv.Value["metallic"]);
                        m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); m.DisableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                        EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); if (sb != null) sb.AppendLine("  " + kv.Key + " -> " + path);
                    }
                }
                if (m == null) throw new Refuse("material '" + kv.Key + "' missing (" + kv.Value + ")" + (create ? "" : " - arcane:mats first")); d[kv.Key] = m;
            }
            return d;
        }
        static string ArcMats() { var sb = new StringBuilder("Hanyang308 arcane:mats\n"); var d = ArcMaterials(true, sb); return sb.Append("materials " + d.Count).ToString(); }

        static bool ArcOwn(Transform t) { for (var x = t; x != null; x = x.parent) if (x.name.StartsWith(ArcUnit) || x.name.StartsWith("Hanyang308_Walkers")) return true; return false; }
        // the ground under a point: the highest upward-facing hit of a ray that starts 2.4 m over the hint
        static bool ArcGround(float x, float z, float hint, out float y, out string why)
        {
            y = 0; why = ""; float best = float.NegativeInfinity; Vector3 nb = Vector3.up;
            foreach (var h in Physics.RaycastAll(new Vector3(x, hint + 2.4f, z), Vector3.down, 7f, ~0, QueryTriggerInteraction.Ignore)) { if (ArcOwn(h.collider.transform)) continue; if (h.point.y > best) { best = h.point.y; nb = h.normal; } }
            if (float.IsNegativeInfinity(best)) { why = "no ground"; return false; }
            if (nb.y < 0.6f) { why = "steep"; return false; }
            y = best; return true;
        }
        static bool ArcFree(Vector3 foot, float yaw, float[] col, out string who)
        {
            who = ""; Vector3 half, c;
            if (col != null && col.Length >= 3) { half = new Vector3(col[0] * 0.5f, Mathf.Max(0.1f, col[1] * 0.5f - 0.2f), col[2] * 0.5f); c = foot + Vector3.up * (col[1] * 0.5f + 0.2f); }
            else { half = new Vector3(0.1f, 0.12f, 0.1f); c = foot + Vector3.up * 0.42f; }
            foreach (var o in Physics.OverlapBox(c, half, Quaternion.Euler(0, yaw, 0), ~0, QueryTriggerInteraction.Ignore)) { if (ArcOwn(o.transform)) continue; who = o.name; return false; }
            return true;
        }

        static string ArcBlocks(string prefix)
        {
            // a mesh path is never reused (time tag): a re-created asset at an old path came back as a null reference in the block prefab
            var plan = LoadArc(); var mats = ArcMaterials(false); string tag = ArcTag(plan.version) + "_" + DateTime.UtcNow.ToString("HHmmss", Inv); var main = Main(); var opened = new List<Scene>(); var realm = OpenAdditive(RealmScene, main, opened);
            var sb = new StringBuilder("Hanyang308 arcane:blocks " + plan.version + "\n"); var temp = new List<Mesh>(); var seat = new ASeatFile { version = plan.version }; var units = new Dictionary<string, CUnit>(); long t0 = 0, t1 = 0; int nb = 0, zero = 0;
            var kits = new Dictionary<string, KeyValuePair<Mesh, Material[]>[]>();
            try
            {
                foreach (var k in plan.kit)
                {
                    var arr = new KeyValuePair<Mesh, Material[]>[k.lods.Count];
                    for (int l = 0; l < k.lods.Count; l++)
                    {
                        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int[]>(); var ms = new List<Material>();
                        foreach (var s in k.lods[l].subs)
                        {
                            int b0 = v.Count; if (!mats.TryGetValue(s.mat, out var mm)) throw new Refuse("kit " + k.id + ": material key " + s.mat);
                            for (int i = 0; i + 2 < s.v.Length; i += 3)
                            {
                                var p = new Vector3(s.v[i], s.v[i + 1], s.v[i + 2]); var nn = new Vector3(s.n[i], s.n[i + 1], s.n[i + 2]); float len = nn.magnitude; if (len < 0.5f) { zero++; nn = Vector3.up; } else nn /= len;
                                v.Add(p); n.Add(nn); uv.Add(Mathf.Abs(nn.y) > 0.7f ? new Vector2(p.x, p.z) / 2.4f : new Vector2(Mathf.Abs(nn.x) > Mathf.Abs(nn.z) ? p.z : p.x, p.y) / 2.4f);
                            }
                            tris.Add(s.t.Select(i => i + b0).ToArray()); ms.Add(mm);
                        }
                        var m = new Mesh { name = "kit_" + k.id + "_" + l }; m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.subMeshCount = tris.Count; for (int s = 0; s < tris.Count; s++) m.SetTriangles(tris[s], s); m.RecalculateBounds();
                        temp.Add(m); arr[l] = new KeyValuePair<Mesh, Material[]>(m, ms.ToArray());
                    }
                    kits[k.id] = arr;
                }
                Physics.SyncTransforms(); Directory.CreateDirectory(Abs(ArcDir + "/Meshes")); Directory.CreateDirectory(Abs(CityPrefabDir));
                foreach (var blk in plan.blocks)
                {
                    if (prefix != "" && !blk.id.StartsWith(prefix)) continue; var o = new Vector3(blk.o[0], blk.o[1], blk.o[2]); var places = new List<KeyValuePair<string, Matrix4x4>>();
                    if (!units.TryGetValue(blk.unit, out var unit)) { unit = new CUnit { name = blk.unit }; units[blk.unit] = unit; }
                    var grp = new CGroup { name = blk.id, cull = blk.cull };
                    foreach (var q in blk.inst)
                    {
                        var row = new ASeat { block = blk.id, k = q.k, cls = q.cls, lit = q.lit, x = q.x, z = q.z, yaw = q.yaw }; seat.rows.Add(row);
                        if (!kits.ContainsKey(q.k)) { row.why = "no kit"; continue; }
                        if (q.mode == "f") { row.placed = true; row.y = q.y; places.Add(new KeyValuePair<string, Matrix4x4>(q.k, Matrix4x4.TRS(new Vector3(q.x, q.y, q.z) - o, Quaternion.Euler(0, q.yaw, 0), Vector3.one * q.s))); continue; }
                        if (q.seg)
                        {
                            if (!ArcGround(q.x, q.z, q.y, out float ya, out string wa) || !ArcGround(q.x2, q.z2, q.y, out float yb, out wa)) { row.why = wa; continue; }
                            var pa = new Vector3(q.x, ya, q.z); var pb = new Vector3(q.x2, yb, q.z2); float len = (pb - pa).magnitude; if (len < 0.3f || Mathf.Abs(yb - ya) > len * 0.45f) { row.why = "grade"; continue; }
                            bool hitSeg = false; foreach (var h in Physics.RaycastAll(pa + Vector3.up * 0.3f, (pb - pa).normalized, len, ~0, QueryTriggerInteraction.Ignore)) if (!ArcOwn(h.collider.transform)) { hitSeg = true; row.why = "in " + h.collider.name; break; }
                            if (hitSeg) continue; if (!ArcFree((pa + pb) * 0.5f, q.yaw, null, out string whoS)) { row.why = "in " + whoS; continue; }
                            row.placed = true; row.grounded = true; row.y = ya; places.Add(new KeyValuePair<string, Matrix4x4>(q.k, Matrix4x4.TRS(pa - o, Quaternion.LookRotation(pb - pa, Vector3.up), new Vector3(1, 1, len)))); continue;
                        }
                        bool ok = false; string why = "";
                        for (int c = 0; c < (q.fit > 0 ? 17 : 1) && !ok; c++)
                        {
                            float r = c == 0 ? 0 : (c <= 8 ? q.fit * 0.5f : q.fit), ang = c * Mathf.PI / 4f; float x = q.x + Mathf.Cos(ang) * r, z = q.z + Mathf.Sin(ang) * r;
                            if (!ArcGround(x, z, q.y, out float y, out why)) continue; var foot = new Vector3(x, y, z);
                            if (!ArcFree(foot, q.yaw, q.col.Length >= 3 ? q.col : null, out string who)) { why = "in " + who; continue; }
                            ok = true; row.x = x; row.y = y; row.z = z; row.grounded = true; places.Add(new KeyValuePair<string, Matrix4x4>(q.k, Matrix4x4.TRS(foot - o, Quaternion.Euler(0, q.yaw, 0), Vector3.one * q.s)));
                            if (q.col.Length >= 3) grp.items.Add(new CItem { n = "Col_" + q.k, a = "col", p = new[] { x, y + q.col[1] * 0.5f, z }, yaw = q.yaw, s = new[] { q.col[0], q.col[1], q.col[2] } });
                        }
                        row.placed = ok; if (!ok) row.why = why;
                    }
                    if (places.Count == 0) { sb.AppendLine("  " + blk.id + ": nothing placed"); continue; }
                    var root = new GameObject("AB_" + blk.id); var lods = new List<LOD>(); long tr0 = 0; int subs = 0;
                    for (int step = 0; step < 2; step++)
                    {
                        var bufs = new Dictionary<Material, TBuf>(); var order = new List<Material>();
                        foreach (var pl in places) { var kv = kits[pl.Key][Mathf.Min(step, kits[pl.Key].Length - 1)]; TownAdd(bufs, order, kv.Key, kv.Value, pl.Value); }
                        string path = ArcDir + "/Meshes/A308_" + blk.id + "_L" + step + "_" + tag + ".asset"; var m = TownMerged(bufs, order, "A308_" + blk.id + "_L" + step + "_" + tag); zero += m.normals.Count(nn => nn.sqrMagnitude < 0.25f); SaveMesh(m, path);
                        long tr = Tris(m); if (step == 0) { t0 += tr; tr0 = tr; subs = order.Count; } else t1 += tr;
                        var go = new GameObject(root.name + "_LOD" + step); go.transform.SetParent(root.transform, false); go.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = order.ToArray(); if (step == 1) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        lods.Add(new LOD(step == 0 ? blk.cut : blk.cull, new Renderer[] { mr }));
                    }
                    var lg = root.AddComponent<LODGroup>(); lg.fadeMode = LODFadeMode.None; lg.SetLODs(lods.ToArray()); lg.RecalculateBounds();
                    PrefabUtility.SaveAsPrefabAsset(root, CityPrefabDir + "/AB_" + blk.id + ".prefab"); UnityEngine.Object.DestroyImmediate(root); nb++;
                    grp.items.Insert(0, new CItem { n = "AB_" + blk.id, a = "prefab:AB_" + blk.id, p = new[] { o.x, o.y, o.z } }); unit.groups.Add(grp);
                    sb.AppendLine("  AB_" + blk.id + " placed " + places.Count + "/" + blk.inst.Count + " tris " + tr0 + " subs " + subs + " colliders " + (grp.items.Count - 1));
                }
            }
            finally { foreach (var m in temp) UnityEngine.Object.DestroyImmediate(m); foreach (var s in opened) EditorSceneManager.CloseScene(s, true); }
            seat.placed = seat.rows.Count(r => r.placed); seat.dropped = seat.rows.Count - seat.placed; seat.lit = seat.rows.Where(r => r.placed).Sum(r => r.lit);
            if (prefix == "")
            {
                var cp = new CPlan { version = plan.version, units = units.Values.Where(u => u.groups.Count > 0).OrderBy(u => u.name).ToList() }; File.WriteAllText(ArcUnitsFile, JsonUtility.ToJson(cp, true));
                Directory.CreateDirectory(OutDir); File.WriteAllText(ArcSeatFile, JsonUtility.ToJson(seat, true));
            }
            foreach (var g in seat.rows.Where(r => !r.placed).GroupBy(r => r.cls + ": " + (r.why.StartsWith("in ") ? "in a collider" : r.why))) sb.AppendLine("  dropped " + g.Count() + " x " + g.Key);
            return sb.Append("blocks " + nb + " | placed " + seat.placed + " dropped " + seat.dropped + " | lit eggs " + seat.lit + " | LOD0 tris " + t0 + " LOD1 tris " + t1 + " | zero normals " + zero + (prefix == "" ? " | unit plan written" : " | partial: unit plan NOT written")).ToString();
        }

        static string ArcStatus()
        {
            var plan = LoadArc(); var led = File.Exists(CityLedgerFile) ? JsonUtility.FromJson<CLedger>(File.ReadAllText(CityLedgerFile)) : new CLedger();
            var sb = new StringBuilder("Hanyang308 arcane:status " + plan.version + " | kit " + plan.kit.Count + " blocks " + plan.blocks.Count + " instances " + plan.blocks.Sum(b => b.inst.Count) + "\n");
            if (File.Exists(ArcUnitsFile)) foreach (var u in LoadArcUnits().units) sb.AppendLine("  " + u.name + " groups " + u.groups.Count + " colliders " + u.groups.Sum(g => g.items.Count(i => i.a == "col")) + (led.units.Contains(u.name) ? " | APPLIED" : " | not applied"));
            if (File.Exists(ArcSeatFile)) { var st = JsonUtility.FromJson<ASeatFile>(File.ReadAllText(ArcSeatFile)); sb.AppendLine("  seat " + st.version + ": placed " + st.placed + " dropped " + st.dropped + " lit " + st.lit); }
            return sb.Append("ledger units named Arcane: " + string.Join(", ", led.units.Where(n => n.StartsWith(ArcUnit)))).ToString();
        }

        static string ArcPrune()
        {
            var plan = LoadArc(); var keep = new HashSet<string>(); int gone = 0;
            foreach (var b in plan.blocks) { string pp = CityPrefabDir + "/AB_" + b.id + ".prefab"; keep.Add(pp); if (File.Exists(Abs(pp))) foreach (string d in AssetDatabase.GetDependencies(pp, false)) keep.Add(d); }
            foreach (var guid in AssetDatabase.FindAssets("AB_ t:Prefab", new[] { CityPrefabDir })) { string p = AssetDatabase.GUIDToAssetPath(guid); if (Path.GetFileName(p).StartsWith("AB_") && !keep.Contains(p)) { AssetDatabase.DeleteAsset(p); gone++; } }
            if (AssetDatabase.IsValidFolder(ArcDir + "/Meshes")) foreach (var guid in AssetDatabase.FindAssets("A308_ t:Mesh", new[] { ArcDir + "/Meshes" })) { string p = AssetDatabase.GUIDToAssetPath(guid); if (!keep.Contains(p)) { AssetDatabase.DeleteAsset(p); gone++; } }
            return "Hanyang308 arcane:prune removed " + gone + " kept " + keep.Count;
        }

        static string ArcCheck()
        {
            var plan = LoadArc(); var city = LoadCity(); var main = Main(); var opened = new List<Scene>(); var realm = OpenAdditive(RealmScene, main, opened); var root = RealmRootOf(realm); var sb = new StringBuilder("Hanyang308 arcane:check " + plan.version + "\n"); int pass = 0, fail = 0;
            void Say(bool ok, string what) { if (ok) pass++; else fail++; sb.AppendLine((ok ? "PASS " : "FAIL ") + what); }
            try
            {
                var core = AssetDatabase.LoadAssetAtPath<Material>(ArcCore); var src = AssetDatabase.LoadAssetAtPath<Material>(plan.core.src); var units = new List<Transform>();
                for (int c = 0; c < root.childCount; c++) if (root.GetChild(c).name.StartsWith(ArcUnit)) units.Add(root.GetChild(c));
                Say(units.Count > 0, "A1 units applied: " + string.Join(", ", units.Select(u => u.name.Substring(ArcUnit.Length))));
                long tris = 0; int rn = 0, cols = 0, lights = 0, zero = 0, badEm = 0, coreOutside = 0, staticFlags = 0; var emNames = new HashSet<string>();
                foreach (var u in units)
                {
                    foreach (var mf in u.GetComponentsInChildren<MeshFilter>(true)) { if (mf.sharedMesh == null) continue; if (!mf.name.EndsWith("_LOD1")) { tris += Tris(mf.sharedMesh); rn++; } zero += mf.sharedMesh.normals.Count(n => n.sqrMagnitude < 0.25f); }
                    cols += u.GetComponentsInChildren<Collider>(true).Length; lights += u.GetComponentsInChildren<Light>(true).Length;
                    foreach (var t in u.GetComponentsInChildren<Transform>(true)) if (GameObjectUtility.GetStaticEditorFlags(t.gameObject) != 0) staticFlags++;
                    foreach (var r in u.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) { if (m == null) { badEm++; continue; } if (m == core) continue; bool hot = m.IsKeywordEnabled("_EMISSION") || (m.shader == core.shader && m.HasProperty("_BaseColor") && m.GetColor("_BaseColor").maxColorComponent > 1.001f); if (hot) { badEm++; emNames.Add(m.name); } }
                }
                foreach (var r in root.GetComponentsInChildren<Renderer>(true)) if (r.sharedMaterials.Contains(core) && !units.Any(u => r.transform.IsChildOf(u))) coreOutside++;
                var st = File.Exists(ArcSeatFile) ? JsonUtility.FromJson<ASeatFile>(File.ReadAllText(ArcSeatFile)) : new ASeatFile();
                sb.AppendLine("     LOD0 renderers " + rn + " | LOD0 tris " + tris + " | colliders " + cols + " | placed " + st.placed + " dropped " + st.dropped);
                foreach (var g in st.rows.Where(r => r.placed).GroupBy(r => r.cls).OrderBy(g => g.Key)) sb.Append("     " + g.Key + " " + g.Count()); sb.AppendLine();
                Say(rn > 0 && tris <= 110000 && rn <= 50, "A2 budget: LOD0 tris " + tris + " <= 110000, LOD0 renderers " + rn + " <= 50");
                Say(badEm == 0 && coreOutside == 0, "A3 only the ore egg glows: other emissive / missing materials in the units " + badEm + (emNames.Count > 0 ? " (" + string.Join(",", emNames) + ")" : "") + ", egg material outside the units " + coreOutside);
                var cc = core.GetColor("_BaseColor"); var sc = src.GetColor("_BaseColor");
                Say(ArcLum(cc) <= ArcLum(sc) + 1e-4f && cc.maxColorComponent <= sc.maxColorComponent + 1e-4f && core.shader == src.shader, "A4 egg brightness <= the mine lamp: luminance " + F(ArcLum(cc), "F3") + " <= " + F(ArcLum(sc), "F3") + ", max channel " + F(cc.maxColorComponent, "F2") + " <= " + F(sc.maxColorComponent, "F2") + ", same shader " + core.shader.name);
                Say(st.lit <= 90, "A5 lit eggs " + st.lit + " <= 90 (lamps + beacons + the plaza instrument + the lamp seller's sample)");
                Say(lights == 0, "A6 Light components " + lights + " (none: the eggs are the glow)");
                Say(zero == 0 && staticFlags == 0, "A7 zero-length normals " + zero + ", objects with static flags " + staticFlags);
                // the seal line: no arcane collider within 3.5 m of a wall station
                Physics.SyncTransforms(); int near = 0; var mine = units.SelectMany(u => u.GetComponentsInChildren<Collider>(true)).ToList();
                foreach (var r in city.rays) { if (r.want != "block") continue; var p = new Vector3(r.x, r.y, r.z); foreach (var c in mine) { var b = c.bounds; float dx = Mathf.Max(0, Mathf.Abs(b.center.x - p.x) - b.extents.x), dz = Mathf.Max(0, Mathf.Abs(b.center.z - p.z) - b.extents.z); if (dx * dx + dz * dz < 3.5f * 3.5f) { near++; break; } } }
                Say(near == 0, "A8 wall stations with an arcane collider within 3.5 m: " + near + " of " + city.rays.Count(r => r.want == "block"));
                // seat: every grounded row still stands on the ground the physics finds (own units ignored)
                int off = 0, n = 0; float worst = 0;
                foreach (var r in st.rows) { if (!r.placed || !r.grounded) continue; n++; if (!ArcGround(r.x, r.z, r.y, out float y, out _)) { off++; continue; } float d = Mathf.Abs(y - r.y); worst = Mathf.Max(worst, d); if (d > 0.06f) off++; }
                Say(off == 0, "A9 seated on the ground: " + n + " rows, off by > 0.06 m " + off + ", worst " + F(worst, "F3") + " m");
                int inside = 0; foreach (var c in mine) { var bc = c as BoxCollider; if (bc == null) continue; var half = Vector3.Scale(bc.size, bc.transform.lossyScale) * 0.5f; half.y = Mathf.Max(0.05f, half.y - 0.25f); foreach (var o in Physics.OverlapBox(bc.transform.position + Vector3.up * 0.2f, half, bc.transform.rotation, ~0, QueryTriggerInteraction.Ignore)) if (!ArcOwn(o.transform)) { inside++; break; } }
                Say(inside == 0, "A10 arcane colliders standing in another collider: " + inside + " of " + mine.Count);
            }
            finally { foreach (var s in opened) EditorSceneManager.CloseScene(s, true); }
            return sb.Append("checks, " + pass + " PASS / " + fail + " FAIL").ToString();
        }
    }
}
