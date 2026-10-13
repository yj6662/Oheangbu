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

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 Hanyang (SPEC-WORLD-HANYANG-308), phase 1a: ground preparation of the capital. Edit mode only, queue-safe (no dialog; refusals are text).
    //   status
    //   inv:<x0>,<z0>,<x1>,<z1>[:depth=N][:root=<name part>][:realm][:tag=<file>]   what stands in a box, by hierarchy node (realm = Realm_Hwanggyeong is opened next to the play scene for the call)
    //   park:plan | park:apply | park:revert      Tools/Unity/Stage308_hanyang/park308.json -> switch off ("off") or move into Stream/HanyangParked308.unity ("out")
    //   content:plan | content:apply | content:revert   Tools/Unity/Stage308_hanyang/content308.json -> Points / Checkpoints / Encounters coordinates of the content asset
    //   final:<layout|vitals|roof>:plan|apply|revert   final integration fixes (Hanyang308.Final.cs, final308.json)
    //   ground:<file>          physics ground under the points of a json file -> Out/<file>.ground.json
    // Ledgers + backups: Tools/Unity/Stage308_hanyang/{Ledger,Backup}. Scenes are saved with SaveScene; AssetDatabase.SaveAssets is never called.
    public static partial class Hanyang308
    {
        const string PlayScene = "Assets/_Project/Scenes/World/W_Demo_Main.unity", RealmScene = "Assets/_Project/Scenes/World/Stream/Realm_Hwanggyeong.unity";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly string[] ProtectedParts = { "Watershed295", "Reworld292", "MountainTrail285" };
        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string StageDir => Path.Combine(Repo, "Tools", "Unity", "Stage308_hanyang");
        static string OutDir => Path.Combine(StageDir, "Out");
        static string F(float v, string f = "F1") => v.ToString(f, Inv);
        sealed class Refuse : Exception { public Refuse(string m) : base(m) { } }

        public static string Run(string command)
        {
            string c = (command ?? "").Trim(); string[] a = c.Split(':');
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Refuse("Play mode");
                switch (a[0])
                {
                    case "status": return Status();
                    case "inv": return Inventory(a);
                    case "park": return Park(a.Length > 1 ? a[1] : "plan");
                    case "content": return Content(a.Length > 1 ? a[1] : "plan");
                    case "ground": return Ground(a.Length > 1 ? a[1] : "");
                    case "veg": return Veg(a.Length > 1 ? a[1] : "plan");
                    case "grass": return Grass(a.Length > 1 ? a[1] : "plan");
                    case "city": return City(a);
                    case "lite": return Lite(a);
                    case "c2": return City2(a);
                    case "palace": return Palace(a);
                    case "town": return Town(a);
                    case "hero": return Hero(a);
                    case "life": return Life(a);
                    case "arcane": return Arcane(a);
                    case "final": return Final(a);
                    case "fix1": return Fix1(a);
                    default: return "REFUSED unknown command '" + c + "'";
                }
            }
            catch (Refuse r) { return "REFUSED " + r.Message; }
            catch (Exception e) { return "error " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace; }
        }

        static Scene Main()
        {
            var s = SceneManager.GetSceneByPath(PlayScene);
            if (!s.IsValid() || !s.isLoaded) throw new Refuse(PlayScene + " is not open");
            return s;
        }
        internal static bool IsProtected(Transform t)
        {
            for (var x = t; x != null; x = x.parent) foreach (var p in ProtectedParts) if (x.name.IndexOf(p, StringComparison.Ordinal) >= 0) return true;
            return false;
        }
        internal static string KeyOf(Transform t) => BuildingAudit308.KeyOf(t);

        // key -> transform inside a scene ("#k" = order among same-named siblings)
        internal static Transform Find(Scene scene, string key)
        {
            string[] parts = key.Split('/'); Transform cur = null;
            for (int i = 0; i < parts.Length; i++)
            {
                string seg = parts[i]; int order = 0; int hash = seg.LastIndexOf('#');
                if (hash > 0 && int.TryParse(seg.Substring(hash + 1), NumberStyles.Integer, Inv, out int k)) { order = k; seg = seg.Substring(0, hash); }
                Transform next = null; int seen = 0;
                if (cur == null) { foreach (var g in scene.GetRootGameObjects()) if (g.name == seg) { if (seen == order) { next = g.transform; break; } seen++; } }
                else for (int j = 0; j < cur.childCount; j++) { var ch = cur.GetChild(j); if (ch.name != seg) continue; if (seen == order) { next = ch; break; } seen++; }
                if (next == null) return null; cur = next;
            }
            return cur;
        }

        static string Status()
        {
            var sb = new StringBuilder("Hanyang308 status | open scenes " + SceneManager.sceneCount);
            for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); sb.Append("\n  " + s.path + " roots " + s.rootCount + " dirty=" + s.isDirty); }
            sb.Append("\n  park ledger " + (File.Exists(ParkLedgerFile) ? "present" : "none") + " | content ledger " + (File.Exists(ContentLedgerFile) ? "present" : "none"));
            return sb.ToString();
        }

        [Serializable] sealed class InvNode { public string scene = "", key = ""; public bool active, shown, prot; public int renderers, colliders, objects; public long vertices; public float x0, x1, y0, y1, z0, z1; public string comps = ""; }
        [Serializable] sealed class InvFile { public string box = ""; public List<InvNode> nodes = new List<InvNode>(); }

        static string Inventory(string[] a)
        {
            if (a.Length < 2) throw new Refuse("use inv:<x0>,<z0>,<x1>,<z1>[:depth=N][:root=<part>][:realm][:tag=<file>]");
            var v = a[1].Split(',').Select(s => float.Parse(s, Inv)).ToArray(); int depth = 2; string rootPart = ""; bool realm = false; string tag = "inv";
            foreach (var o in a.Skip(2)) { if (o.StartsWith("depth=")) depth = int.Parse(o.Substring(6), Inv); else if (o.StartsWith("root=")) rootPart = o.Substring(5); else if (o == "realm") realm = true; else if (o.StartsWith("tag=")) tag = o.Substring(4); }
            var main = Main(); var scenes = new List<Scene> { main }; Scene opened = default; bool didOpen = false;
            if (realm)
            {
                var rs = SceneManager.GetSceneByPath(RealmScene);
                if (!rs.IsValid() || !rs.isLoaded) { rs = EditorSceneManager.OpenScene(RealmScene, OpenSceneMode.Additive); opened = rs; didOpen = true; SceneManager.SetActiveScene(main); }
                scenes.Add(rs);
            }
            var box = new Bounds(); box.SetMinMax(new Vector3(v[0], -1000, v[1]), new Vector3(v[2], 5000, v[3]));
            var file = new InvFile { box = a[1] };
            foreach (var s in scenes) foreach (var g in s.GetRootGameObjects()) Walk(g.transform, 0);
            void Walk(Transform t, int d)
            {
                if (rootPart.Length > 0 && d == 0 && t.name.IndexOf(rootPart, StringComparison.Ordinal) < 0) return;
                var rs = t.GetComponentsInChildren<Renderer>(true); var cs = t.GetComponentsInChildren<Collider>(true); if (rs.Length == 0 && cs.Length == 0) return;
                bool has = false; var b = new Bounds(); long verts = 0; int rn = 0;
                foreach (var r in rs)
                {
                    if (r is ParticleSystemRenderer) continue; var rb = r.bounds; if (!rb.Intersects(box)) continue;
                    if (!has) { b = rb; has = true; } else b.Encapsulate(rb); rn++;
                    var mf = r.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) verts += mf.sharedMesh.vertexCount;
                    else if (r is SkinnedMeshRenderer sk && sk.sharedMesh != null) verts += sk.sharedMesh.vertexCount;
                }
                int cn = 0;
                foreach (var col in cs) { if (!col.bounds.Intersects(box)) continue; cn++; if (!has) { b = col.bounds; has = true; } else b.Encapsulate(col.bounds); }
                if (!has) return;
                bool leaf = d >= depth || t.childCount == 0;
                if (!leaf) { int before = file.nodes.Count; for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), d + 1); if (file.nodes.Count > before && t.GetComponent<Renderer>() == null) return; }
                var types = new HashSet<string>(); foreach (var cmp in t.GetComponents<Component>()) if (cmp != null && !(cmp is Transform)) types.Add(cmp.GetType().Name);
                file.nodes.Add(new InvNode { scene = t.gameObject.scene.name, key = KeyOf(t), active = t.gameObject.activeSelf, shown = t.gameObject.activeInHierarchy, prot = IsProtected(t), renderers = rn, colliders = cn, objects = t.GetComponentsInChildren<Transform>(true).Length, vertices = verts,
                    x0 = b.min.x, x1 = b.max.x, y0 = b.min.y, y1 = b.max.y, z0 = b.min.z, z1 = b.max.z, comps = string.Join(",", types) });
            }
            if (didOpen) EditorSceneManager.CloseScene(opened, true);
            Directory.CreateDirectory(OutDir); string path = Path.Combine(OutDir, tag + ".json"); File.WriteAllText(path, JsonUtility.ToJson(file, true));
            var byRoot = file.nodes.GroupBy(n => n.scene + ":" + n.key.Split('/')[0]).Select(g => g.Key + " nodes " + g.Count() + " shown " + g.Count(n => n.shown) + " r " + g.Sum(n => n.renderers) + " v " + g.Sum(n => n.vertices));
            return "Hanyang308 inv " + a[1] + " depth " + depth + ": nodes " + file.nodes.Count + " -> " + path + "\n  " + string.Join("\n  ", byRoot);
        }

        [Serializable] sealed class GroundPoint { public string id = ""; public float x, y, z, ground, field; public bool hit; public string collider = ""; }
        [Serializable] sealed class GroundFile { public List<GroundPoint> points = new List<GroundPoint>(); }
        // physics ground (terrain tiles only) under listed points
        static string Ground(string name)
        {
            string path = Path.Combine(StageDir, name); if (!File.Exists(path)) throw new Refuse("no file " + path);
            var gf = JsonUtility.FromJson<GroundFile>(File.ReadAllText(path)); var main = Main(); Physics.SyncTransforms();
            var terr = main.GetRootGameObjects().FirstOrDefault(g => g.name == "Reworld292_Terrain"); if (terr == null) throw new Refuse("no Reworld292_Terrain root");
            int miss = 0, off = 0; float worst = 0;
            foreach (var p in gf.points)
            {
                float best = float.NegativeInfinity; string who = "";
                foreach (var h in Physics.RaycastAll(new Vector3(p.x, 600f, p.z), Vector3.down, 1200f, ~0, QueryTriggerInteraction.Ignore)) if (h.collider.transform.IsChildOf(terr.transform) && h.point.y > best) { best = h.point.y; who = h.collider.name; }
                p.hit = !float.IsNegativeInfinity(best); p.ground = p.hit ? best : 0; p.collider = who;
                if (!p.hit) miss++; else { float d = Mathf.Abs(best - p.field); if (d > 0.3f) off++; if (d > worst) worst = d; }
            }
            string outPath = Path.Combine(OutDir, Path.GetFileNameWithoutExtension(name) + ".ground.json"); Directory.CreateDirectory(OutDir); File.WriteAllText(outPath, JsonUtility.ToJson(gf, true));
            return "Hanyang308 ground " + name + ": points " + gf.points.Count + " no terrain " + miss + " off the field by > 0.3 m " + off + " worst " + F(worst, "F3") + " -> " + outPath;
        }
    }
}
