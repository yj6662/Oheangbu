using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 props that float over the ground or lie under it are seated on it (D308-67). Numbers: Tools/Unity/Stage308_ground/ground308.json.
    // A unit = the smallest group of drawn things no wider than unitMaxM. Its ground = the terrain under its footprint (five rays that hit
    // only Reworld292_Terrain). It floats when its lowest point is above the HIGHEST of those by floatM, it is buried when its top is below the
    // lowest. A unit that has something else under it (a floor, a roof, a deck, a rock) is not touched: it stands on that.
    //   stats                  per root of the open scenes: renderers, triangles drawn once, lights (and how many cast shadows)   (read only)
    //   scan                   every root that is not denied -> Tools/Unity/Stage308_ground/scan.json and a table per root        (read only)
    //   apply                  the units of the roots in `roots` are moved down / up onto the ground (ledger; revert)
    //   off:<root/path>        an object is switched off (ledger; revert switches it on again)
    //   cull:<root>:<h>        child groups of a root are not drawn when smaller than h of the screen height (LODGroup; revert removes it)
    //   shadows:<root>         point / spot lights of a root stop casting shadows (revert restores)
    //   rp:<urp asset>:<0|1>   point / spot light shadows of a pipeline asset off / on (not in the ledger: run it again with the other value)
    //   revert
    // Only the play scene is written, backed up first. Protected roots and the tools' own generated roots are never touched.
    public static class Ground308
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string Stage => Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..")), "Tools", "Unity", "Stage308_ground");
        [Serializable] sealed class Cfg { public string[] roots = Array.Empty<string>(), deny = Array.Empty<string>(); public float unitMaxM = 9f, floatM = .3f, sinkM = .05f, maxMoveM = 12f; }
        [Serializable] sealed class Move { public string path = ""; public Vector3 before; public float dy; }
        [Serializable] sealed class Ledger { public List<Move> moves = new List<Move>(); public List<string> off = new List<string>(), cull = new List<string>(), shadows = new List<string>(); }
        static Cfg Read() => JsonUtility.FromJson<Cfg>(File.ReadAllText(Path.Combine(Stage, "ground308.json")));
        static string LedgerFile => Path.Combine(Stage, "Ledger", "ground308.json");
        static Ledger ReadLedger() => File.Exists(LedgerFile) ? JsonUtility.FromJson<Ledger>(File.ReadAllText(LedgerFile)) : new Ledger();
        static void WriteLedger(Ledger l) { Directory.CreateDirectory(Path.GetDirectoryName(LedgerFile)); File.WriteAllText(LedgerFile, JsonUtility.ToJson(l, true)); }

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (c == "stats") return Stats();
                var scene = SceneManager.GetActiveScene(); if (scene.name != "W_Demo_Main") return "REFUSED open W_Demo_Main first";
                if (c == "scan") return Scan(scene, false, null);
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit mode only (Play is running)";
                if (scene.isDirty) return "REFUSED the play scene has unsaved changes";
                if (c != "apply" && c != "revert" && !c.StartsWith("off:", StringComparison.Ordinal) && !c.StartsWith("offat:", StringComparison.Ordinal) && !c.StartsWith("rp:", StringComparison.Ordinal) && !c.StartsWith("rpset:", StringComparison.Ordinal) && !c.StartsWith("cull:", StringComparison.Ordinal) && !c.StartsWith("shadows:", StringComparison.Ordinal)) return "REFUSED unknown command '" + c + "' (stats | scan | apply | off:<root/path> | revert)";
                string dir = Path.Combine(Stage, "Backup", DateTime.Now.ToString("yyyyMMddTHHmmss", Inv) + "_" + c.Split(':')[0]); Directory.CreateDirectory(dir); File.Copy(Path.GetFullPath(scene.path), Path.Combine(dir, Path.GetFileName(scene.path)), true);
                var led = ReadLedger(); string answer;
                if (c == "apply") answer = Scan(scene, true, led);
                else if (c == "revert")
                {
                    int n = 0; foreach (var m in led.moves) { var t = Resolve(scene, m.path); if (t != null) { t.position = m.before; n++; } }
                    foreach (string p in led.off) { var t = Resolve(scene, p); if (t != null) { t.gameObject.SetActive(true); n++; } }
                    foreach (string p in led.cull) { var t = Resolve(scene, p); var lod = t != null ? t.GetComponent<LODGroup>() : null; if (lod != null) { Object.DestroyImmediate(lod); n++; } }
                    foreach (string row in led.shadows) { var a = row.Split('|'); var t = Resolve(scene, a[0]); var l = t != null ? t.GetComponent<Light>() : null; if (l != null) { l.shadows = (LightShadows)int.Parse(a[1]); n++; } }
                    led.cull.Clear(); led.shadows.Clear(); led.moves.Clear(); led.off.Clear(); answer = "Ground308 revert: " + n + " objects put back";
                }
                else if (c.StartsWith("offat:", StringComparison.Ordinal))      // offat:<root>:x,z;x,z;...  the small thing that stands at each spot is switched off
                {
                    var a = c.Split(':'); var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == a[1]); if (root == null) return "REFUSED no root " + a[1]; int n = 0;
                    foreach (string spot in a[2].Split(';'))
                    {
                        var xz = spot.Split(',').Select(v => float.Parse(v, Inv)).ToArray(); Transform best = null; float bd = 1.5f;
                        foreach (var t in root.GetComponentsInChildren<Transform>(false))
                        {
                            if (t == root.transform || !BoundsOf(t, out var b) || Mathf.Max(b.size.x, b.size.z) > 6f) continue; float d = Vector2.Distance(new Vector2(b.center.x, b.center.z), new Vector2(xz[0], xz[1]));
                            if (d < bd - .01f || (d < bd + .01f && best != null && best.IsChildOf(t))) { bd = Mathf.Min(bd, d); best = t; }
                        }
                        if (best == null) continue; while (best.parent != root.transform && best.parent != null && BoundsOf(best.parent, out var pb) && Mathf.Max(pb.size.x, pb.size.z) <= 6f) best = best.parent;
                        best.gameObject.SetActive(false); led.off.Add(PathOf(best)); n++;
                    }
                    answer = "Ground308 offat: " + n + " objects under " + a[1] + " switched off";
                }
                else if (c.StartsWith("cull:", StringComparison.Ordinal))      // cull:<root>:<screen height 0..1>  every child group of the root is not drawn when it is smaller than that on screen
                {
                    var a = c.Split(':'); var where = scene; string rootName = a[1]; int slash = a[1].IndexOf('/');
                    if (slash > 0) { where = SceneManager.GetSceneByName(a[1].Substring(0, slash)); rootName = a[1].Substring(slash + 1); if (!where.IsValid() || !where.isLoaded) return "REFUSED scene not open: " + a[1].Substring(0, slash) + " (RealmStream308 open-all)"; }
                    var root = where.GetRootGameObjects().FirstOrDefault(g => g.name == rootName); if (root == null) return "REFUSED no root " + a[1]; float h = float.Parse(a[2], Inv); int n = 0;
                    foreach (Transform t in root.transform)
                    {
                        if (t.GetComponent<LODGroup>() != null || t.GetComponentInChildren<LODGroup>() != null) continue; var rs = t.GetComponentsInChildren<Renderer>(false).Where(r => !(r is ParticleSystemRenderer)).ToArray(); if (rs.Length == 0 || !BoundsOf(t, out var b) || Mathf.Max(b.size.x, b.size.z) > 120f) continue;
                        var lod = t.gameObject.AddComponent<LODGroup>(); lod.SetLODs(new[] { new LOD(h, rs) }); lod.RecalculateBounds(); if (where == scene) led.cull.Add(PathOf(t)); n++;
                    }
                    if (where != scene) { EditorSceneManager.MarkSceneDirty(where); if (!EditorSceneManager.SaveScene(where)) return "FAILED " + where.name + " was not saved"; }      // realm scenes: undone with cullclear:<Scene/Root>
                    answer = "Ground308 cull: " + n + " groups under " + a[1] + " are culled below " + h + " of the screen height";
                }
                else if (c.StartsWith("rp:", StringComparison.Ordinal))      // rp:<asset path>:<0|1>  point / spot light shadows of a render pipeline asset
                {
                    var a = c.Split(':'); var asset = AssetDatabase.LoadMainAssetAtPath(a[1]); if (asset == null) return "REFUSED no asset " + a[1]; var so = new SerializedObject(asset); var pr = so.FindProperty("m_AdditionalLightShadowsSupported"); if (pr == null) return "REFUSED not a URP asset";
                    bool before = pr.boolValue; pr.boolValue = a[2] == "1"; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); answer = "Ground308 rp: additional light shadows " + before + " -> " + pr.boolValue + " in " + a[1];
                }
                else if (c.StartsWith("rpset:", StringComparison.Ordinal))      // rpset:<asset path>:<serialized float property>:<value>   e.g. m_ShadowDistance
                {
                    var a = c.Split(':'); var asset = AssetDatabase.LoadMainAssetAtPath(a[1]); if (asset == null) return "REFUSED no asset " + a[1]; var so = new SerializedObject(asset); var pr = so.FindProperty(a[2]); if (pr == null || pr.propertyType != SerializedPropertyType.Float) return "REFUSED no float property " + a[2];
                    float before = pr.floatValue; pr.floatValue = float.Parse(a[3], Inv); so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); answer = "Ground308 rpset: " + a[2] + " " + before.ToString(Inv) + " -> " + pr.floatValue.ToString(Inv) + " in " + a[1];
                }
                else if (c.StartsWith("shadows:", StringComparison.Ordinal))      // shadows:<root>  point / spot lights of the root stop casting shadows
                {
                    string name = c.Substring(8); var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == name); if (root == null) return "REFUSED no root " + name; int n = 0;
                    foreach (var l in root.GetComponentsInChildren<Light>(true)) if (l.type != LightType.Directional && l.shadows != LightShadows.None) { led.shadows.Add(PathOf(l.transform) + "|" + (int)l.shadows); l.shadows = LightShadows.None; n++; }
                    answer = "Ground308 shadows: " + n + " lights under " + name + " no longer cast shadows";
                }
                else { string p = c.Substring(4); var t = Resolve(scene, p); if (t == null) return "REFUSED no object at " + p; t.gameObject.SetActive(false); if (!led.off.Contains(p)) led.off.Add(p); answer = "Ground308 off: " + p; }
                WriteLedger(led); EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) return "FAILED the play scene was not saved";
                return answer + ", backup " + dir;
            }
            catch (Exception e) { return "REFUSED " + e.Message; }
        }

        static Transform Resolve(Scene scene, string path)
        {
            var parts = path.Split('/'); var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == parts[0]); if (root == null) return null; var t = root.transform;
            for (int i = 1; i < parts.Length && t != null; i++) { string p = parts[i]; int hash = p.LastIndexOf('#'); int index = hash >= 0 && int.TryParse(p.Substring(hash + 1), out int k) ? k : -1; t = index >= 0 && index < t.childCount ? t.GetChild(index) : t.Find(p); }
            return t;
        }
        static string PathOf(Transform t) { var parts = new List<string>(); for (; t.parent != null; t = t.parent) parts.Add(t.name + "#" + t.GetSiblingIndex()); parts.Add(t.name); parts.Reverse(); return string.Join("/", parts); }
        static bool BoundsOf(Transform t, out Bounds b) { b = default; bool any = false; foreach (var r in t.GetComponentsInChildren<Renderer>(false)) { if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue; if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; } } return any; }

        static string Stats()
        {
            var sb = new StringBuilder("Ground308 stats (triangles = every drawn mesh counted once per renderer)\n"); var rows = new List<(string name, int r, long tri, int lights, int shadow)>();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s); if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (!root.activeInHierarchy) continue; long tri = 0; int r = 0;
                    foreach (var f in root.GetComponentsInChildren<MeshFilter>(false)) { var mr = f.GetComponent<MeshRenderer>(); if (f.sharedMesh == null || mr == null || !mr.enabled) continue; var lod = f.GetComponentInParent<LODGroup>(); if (lod != null && lod.GetLODs().Length > 0 && !lod.GetLODs()[0].renderers.Contains(mr)) continue; r++; for (int k = 0; k < f.sharedMesh.subMeshCount; k++) tri += f.sharedMesh.GetIndexCount(k) / 3; }
                    foreach (var sk in root.GetComponentsInChildren<SkinnedMeshRenderer>(false)) if (sk.sharedMesh != null && sk.enabled) { r++; tri += sk.sharedMesh.triangles.Length / 3; }
                    var lights = root.GetComponentsInChildren<Light>(false).Where(l => l.enabled).ToArray(); rows.Add((scene.name + "/" + root.name, r, tri, lights.Length, lights.Count(l => l.shadows != LightShadows.None && l.type != LightType.Directional)));
                }
            }
            sb.Append("  total renderers ").Append(rows.Sum(x => x.r)).Append(", triangles ").Append(rows.Sum(x => x.tri)).Append(", lights ").Append(rows.Sum(x => x.lights)).Append(", point/spot lights casting shadows ").Append(rows.Sum(x => x.shadow)).Append('\n');
            foreach (var x in rows.OrderByDescending(x => x.tri).Take(28)) sb.Append("  ").Append(x.name.PadRight(52)).Append(" renderers ").Append(x.r.ToString(Inv).PadLeft(6)).Append("  triangles ").Append(x.tri.ToString(Inv).PadLeft(10)).Append("  lights ").Append(x.lights).Append(" (shadow ").Append(x.shadow).Append(")\n");
            sb.Append("  -- most lights\n"); foreach (var x in rows.Where(x => x.lights > 0).OrderByDescending(x => x.lights).Take(12)) sb.Append("  ").Append(x.name.PadRight(52)).Append(" lights ").Append(x.lights).Append(" (shadow ").Append(x.shadow).Append(")\n");
            return sb.ToString();
        }

        static string Scan(Scene scene, bool apply, Ledger led)
        {
            var cfg = Read(); Physics.SyncTransforms(); var sb = new StringBuilder("Ground308 " + (apply ? "apply" : "scan") + "\n"); int moved = 0; var table = new List<string>();
            if (apply && led.moves.Count > 0) return "REFUSED already applied (revert first)";
            foreach (var root in scene.GetRootGameObjects().OrderBy(g => g.name, StringComparer.Ordinal))
            {
                if (!root.activeInHierarchy || cfg.deny.Any(d => root.name.StartsWith(d, StringComparison.Ordinal))) continue; if (apply && !cfg.roots.Contains(root.name)) continue;
                var units = new List<Transform>(); Collect(root.transform, cfg.unitMaxM, units, 0); int floating = 0, buried = 0, onSomething = 0, noGround = 0; float worst = 0f;
                foreach (var u in units)
                {
                    if (!BoundsOf(u, out var b)) continue; float lo = float.MaxValue, hi = float.MinValue; bool ground = true, other = false;
                    foreach (var o in new[] { Vector2.zero, new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(-1, -1) })
                    {
                        var from = new Vector3(b.center.x + o.x * b.extents.x * .6f, b.max.y + 400f, b.center.z + o.y * b.extents.z * .6f); float terrainY = float.NaN;
                        foreach (var hit in Physics.RaycastAll(from, Vector3.down, 900f, ~0, QueryTriggerInteraction.Ignore))
                        {
                            if (hit.transform.IsChildOf(u)) continue; bool terrain = hit.transform.root.name.StartsWith("Reworld292_Terrain", StringComparison.Ordinal);
                            if (terrain) { if (float.IsNaN(terrainY) || hit.point.y > terrainY) terrainY = hit.point.y; }
                            else if (hit.point.y < b.min.y + .15f && hit.point.y > b.min.y - 1.5f) other = true;      // a floor, a deck, a rock right under it
                        }
                        if (float.IsNaN(terrainY)) { ground = false; break; } lo = Mathf.Min(lo, terrainY); hi = Mathf.Max(hi, terrainY);
                    }
                    if (!ground) { noGround++; continue; } float dy = 0f;
                    if (b.min.y - hi > cfg.floatM) { if (other) { onSomething++; continue; } dy = -(b.min.y - (lo + hi) * .5f + cfg.sinkM); floating++; }
                    else if (b.max.y < lo) { dy = (lo + hi) * .5f - b.min.y - cfg.sinkM; buried++; }
                    else continue;
                    worst = Mathf.Max(worst, Mathf.Abs(dy)); if (!apply || Mathf.Abs(dy) > cfg.maxMoveM) continue;
                    led.moves.Add(new Move { path = PathOf(u), before = u.position, dy = dy }); u.position += Vector3.up * dy; moved++;
                }
                if (floating + buried > 0) table.Add("  " + root.name.PadRight(40) + " units " + units.Count.ToString(Inv).PadLeft(5) + "  floating " + floating.ToString(Inv).PadLeft(4) + "  buried " + buried.ToString(Inv).PadLeft(4) + "  on something " + onSomething.ToString(Inv).PadLeft(4) + "  worst " + worst.ToString("F1", Inv) + " m");
            }
            foreach (string row in table) sb.Append(row).Append('\n');
            if (!apply) File.WriteAllText(Path.Combine(Stage, "scan.txt"), sb.ToString());
            return sb.Append(apply ? "moved " + moved + " units onto the ground" : table.Count + " roots have floating or buried units").ToString();
        }
        static void Collect(Transform t, float max, List<Transform> units, int depth)
        {
            if (!t.gameObject.activeInHierarchy) return; if (!BoundsOf(t, out var b)) return;
            bool groups = false; foreach (Transform c in t) if (c.gameObject.activeInHierarchy && c.GetComponentInChildren<Renderer>(false) != null) { groups = true; break; }
            if (depth > 0 && (Mathf.Max(b.size.x, b.size.z) <= max || !groups || t.GetComponent<Renderer>() != null || t.GetComponent<LODGroup>() != null)) { units.Add(t); return; }
            foreach (Transform c in t) Collect(c, max, units, depth + 1);
        }
    }
}
