using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // [SPEC-WORLD-REALM-STREAM-308] Skin streaming, stage 1: the heavy static look of the play scene (renderers and their colliders, no
    // scripts) is moved into one additive scene per realm; the runtime loader (App/World/RealmStream308) brings them in by distance.
    //   roots[:<path prefix>]   what the open scene holds, by root (or by child of a path): objects, renderers, vertices, scripts
    //   plan                    choose what can move and where (no writes except the report file)
    //   split                   move it: Stream/Realm_<Id>.unity x5, loader object, sheet, build settings, loading profile (backup first)
    //   merge                   put everything back under its former parent and take the loader out (backup first)
    //   open-all | close-all    the realm scenes next to the play scene in the editor (stills, bakes, world tools)
    //   check                   counts against the manifest, nothing but static parts in the realm scenes, no broken reference
    //   status                  Play: the loader's state; edit mode: which realm scenes are open
    // Rules (data, not code): Tools/Unity/Stage308_stream/realmstream308.json. Queue-safe: no dialog, refusals come back as text.
    // Saves only the play scene and the realm scenes (SaveScene) and its own sheet / the loading profile (SaveAssetIfDirty).
    public static partial class RealmStream308
    {
        [Serializable] sealed class Rules
        {
            public string mainScene, streamFolder, layout;
            public float loadDistance = 700, unloadDistance = 1000, anchorDistance = 200, tickSeconds = .5f, purgeSeconds = 20, maxUnitSpan = 260, landmarkHeight = 14, landmarkSpan = 60,
                farMinHeight = 14, farMinSpan = 60, farCell = .75f, farBudgetMb = 200;
            public string farFolder = "Assets/_Project/Art/World/RealmStream308/Far";
            public string[] includeRoots = Array.Empty<string>(), keepPathParts = Array.Empty<string>(), protectedNameParts = Array.Empty<string>(),
                protectedAssetParts = Array.Empty<string>(), runtimeNames = Array.Empty<string>(), runtimeNamePrefixes = Array.Empty<string>();
        }
        [Serializable] sealed class Layout { public LayoutRealm[] Realms; }
        [Serializable] sealed class LayoutRealm { public string Id; public Vector2[] Polygon; }
        [Serializable] sealed class Manifest { public string mainScene, stamp; public List<ManifestUnit> units = new List<ManifestUnit>(); }
        [Serializable] sealed class ManifestUnit { public string realm, name, parentGid, parentPath; public int sibling, renderers; public long vertices; }

        sealed class Unit
        {
            public Transform Root; public string Path; public int Realm; public Bounds Bounds; public bool HasBounds;
            public int Renderers, Colliders, Objects; public long Vertices; public HashSet<Mesh> Meshes = new HashSet<Mesh>(); public string Refused;
        }
        sealed class Plan
        {
            public List<Unit> Units = new List<Unit>(); public Dictionary<string, (int count, long bytes, string sample)> Kept = new Dictionary<string, (int, long, string)>();
            public List<string> Warnings = new List<string>(); public LayoutRealm[] Realms; public int Broken; public int Lightmapped;
            public IEnumerable<Unit> Moving => Units.Where(u => u.Refused == null);
        }
        sealed class Refuse : Exception { public Refuse(string m) : base(m) { } }

        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        static string Stage => Path.Combine(Repo, "Tools", "Unity", "Stage308_stream");
        static string RulesFile => Path.Combine(Stage, "realmstream308.json");
        static string ManifestFile => Path.Combine(Stage, "manifest.json");
        static string SheetPath(Rules r) => r.streamFolder + "/RealmStreamSheet308.asset";
        static string ScenePath(Rules r, string id) => r.streamFolder + "/Realm_" + id + ".unity";
        const string LoaderName = "RealmStream308";
        static string F(float v, string f = "F0") => v.ToString(f, CultureInfo.InvariantCulture);
        static string Mb(long b) => (b / 1048576.0).ToString("F1", CultureInfo.InvariantCulture) + " MB";
        static string N(long n) => n.ToString("N0", CultureInfo.InvariantCulture);

        public static string Run(string command)
        {
            try
            {
                string[] a = (command ?? "").Split(new[] { ':' }, 2);
                switch (a[0])
                {
                    case "roots": return Roots(a.Length > 1 ? a[1] : "");
                    case "plan": return Report(Analyse(LoadRules(), MainScene(LoadRules(), false)), LoadRules(), true);
                    case "split": return Split();
                    case "merge": return Merge();
                    case "open-all": return OpenAll();
                    case "close-all": return CloseAll();
                    case "check": return Check();
                    case "status": return Status();
                    case "far": return FarCommand(a.Length > 1 ? a[1] : "");
                    default: return "commands: roots[:<path prefix>] | plan | split | merge | open-all | close-all | check | status | far:plan|build|clear|check";
                }
            }
            catch (Refuse e) { return "REFUSED " + e.Message; }
        }

        static Rules LoadRules()
        {
            if (!File.Exists(RulesFile)) throw new Refuse("rules file missing: " + RulesFile);
            var r = JsonUtility.FromJson<Rules>(File.ReadAllText(RulesFile).TrimStart('﻿'));
            if (string.IsNullOrEmpty(r.mainScene) || string.IsNullOrEmpty(r.streamFolder) || string.IsNullOrEmpty(r.layout)) throw new Refuse("rules need mainScene, streamFolder and layout");
            if (r.unloadDistance <= r.loadDistance) throw new Refuse("unloadDistance must be larger than loadDistance");
            return r;
        }

        static LayoutRealm[] LoadRealms(Rules r)
        {
            string file = Path.Combine(Repo, "Oheangbu", r.layout);
            if (!File.Exists(file)) throw new Refuse("layout missing: " + file);
            var l = JsonUtility.FromJson<Layout>(File.ReadAllText(file).TrimStart('﻿'));
            if (l?.Realms == null || l.Realms.Length == 0) throw new Refuse("layout has no Realms");
            return l.Realms;
        }

        /// <summary>The play scene, open. alone = it must be the only open scene and clean (before a write).</summary>
        static Scene MainScene(Rules r, bool alone)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Refuse("not in Play");
            var scene = SceneManager.GetSceneByPath(r.mainScene);
            if (!scene.IsValid() || !scene.isLoaded) throw new Refuse("open " + r.mainScene + " first (MemoryFix308 scene:open:<path>)");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (alone && s.isDirty) throw new Refuse("scene " + s.path + " has unsaved changes that are not this tool's - nothing written");
                if (alone && s != scene) throw new Refuse("close the other open scenes first (" + s.path + ")");
            }
            return scene;
        }

        static string PathOf(Transform t)
        {
            var names = new List<string>();
            for (; t != null; t = t.parent) names.Add(t.name);
            names.Reverse(); return string.Join("/", names);
        }

        // ---------------------------------------------------------------- roots

        static string Roots(string prefix)
        {
            var scene = SceneManager.GetActiveScene();
            IEnumerable<Transform> nodes = scene.GetRootGameObjects().Select(g => g.transform);
            if (!string.IsNullOrEmpty(prefix))
            {
                Transform at = null;
                foreach (var root in scene.GetRootGameObjects()) { if (root.name == prefix.Split('/')[0]) { at = prefix.Contains("/") ? root.transform.Find(prefix.Substring(prefix.IndexOf('/') + 1)) : root.transform; if (at != null) break; } }
                if (at == null) return "no object at " + prefix;
                nodes = at.Cast<Transform>();
            }
            var sb = new StringBuilder("RealmStream308 roots " + scene.path + (prefix == "" ? "" : " under " + prefix) + "\n   objects renderers     vertices   mesh MB colliders scripts other  name\n");
            foreach (var t in nodes)
            {
                int objects = 0, renderers = 0, colliders = 0, scripts = 0, other = 0; long vertices = 0; var meshes = new HashSet<Mesh>();
                foreach (var c in t.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) { scripts++; continue; }
                    if (c is Transform) { objects++; continue; }
                    if (c is MeshFilter f) { if (f.sharedMesh != null) { vertices += f.sharedMesh.vertexCount; meshes.Add(f.sharedMesh); } continue; }
                    if (c is MeshRenderer) { renderers++; continue; }
                    if (c is Collider col) { colliders++; if (col is MeshCollider mc && mc.sharedMesh != null) meshes.Add(mc.sharedMesh); continue; }
                    if (c is MonoBehaviour) scripts++; else if (!(c is LODGroup)) other++;
                }
                long bytes = meshes.Sum(m => Profiler.GetRuntimeMemorySizeLong(m));
                sb.AppendLine(N(objects).PadLeft(10) + N(renderers).PadLeft(10) + N(vertices).PadLeft(13) + F(bytes / 1048576f, "F1").PadLeft(10) + N(colliders).PadLeft(10) + N(scripts).PadLeft(8) + N(other).PadLeft(6) + "  " + t.name + (t.gameObject.activeInHierarchy ? "" : " (inactive)"));
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- analysis

        static bool Allowed(Component c) => c is Transform || c is MeshFilter || c is MeshRenderer || c is MeshCollider || c is BoxCollider || c is SphereCollider || c is CapsuleCollider || c is LODGroup;

        /// <summary>null when the object itself may live in a realm scene, else the reason it may not.</summary>
        static string OwnReason(GameObject go)
        {
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) return "missing script";
                if (!Allowed(c)) return c is MonoBehaviour ? "script " + c.GetType().Name : "component " + c.GetType().Name;
                if (c is Collider col && col.isTrigger) return "trigger collider";
            }
            if (!go.CompareTag("Untagged")) return "tag " + go.tag;
            return null;
        }

        sealed class Node { public string Reason; public bool Payload; }

        static Node Scan(Transform t, Dictionary<Transform, Node> into)
        {
            var node = new Node { Reason = OwnReason(t.gameObject) };
            node.Payload = t.GetComponent<MeshRenderer>() != null || t.GetComponent<Collider>() != null;
            foreach (Transform child in t)
            {
                var c = Scan(child, into);
                if (node.Reason == null && c.Reason != null) node.Reason = "holds " + c.Reason;
                node.Payload |= c.Payload;
            }
            into[t] = node; return node;
        }

        static bool OnlyTransform(GameObject go) => go.GetComponents<Component>().Length == 1;

        static Plan Analyse(Rules r, Scene scene)
        {
            var plan = new Plan { Realms = LoadRealms(r) };
            var nodes = new Dictionary<Transform, Node>();
            foreach (var root in scene.GetRootGameObjects()) Scan(root.transform, nodes);

            void Keep(string reason, Transform t)
            {
                long bytes = 0; var seen = new HashSet<Mesh>();
                foreach (var f in t.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh != null && seen.Add(f.sharedMesh)) bytes += Profiler.GetRuntimeMemorySizeLong(f.sharedMesh);
                plan.Kept.TryGetValue(reason, out var k); plan.Kept[reason] = (k.count + 1, k.bytes + bytes, k.sample ?? PathOf(t));
            }

            void Walk(Transform t, bool included)
            {
                string path = PathOf(t);
                if (!included && r.includeRoots.Length > 0)
                {
                    // includeRoots name whole subtrees ("Root" or "Root/Child"); above them only plain grouping objects are crossed
                    if (r.includeRoots.Any(p => path == p)) included = true;
                    else if (r.includeRoots.Any(p => p.StartsWith(path + "/", StringComparison.Ordinal))) { foreach (Transform c in t) Walk(c, false); return; }
                    else return;
                }
                var node = nodes[t];
                if (!node.Payload) return;
                if (r.keepPathParts.Any(p => path.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)) { Keep("kept by rule (keepPathParts)", t); return; }
                // a switched-off object keeps its state when it moves as a whole (its parents are on, or the walk would not be here)
                bool off = !t.gameObject.activeSelf;
                if (off && node.Reason != null) { Keep("switched off, and not all static below", t); return; }
                if (node.Reason == null)
                {
                    bool prefabPart = PrefabUtility.IsPartOfPrefabInstance(t.gameObject);
                    if (prefabPart && PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject) != t.gameObject) { Keep("inside a prefab instance whose root holds more than static parts", t); return; }
                    var unit = Measure(t, path);
                    bool wide = unit.HasBounds && Mathf.Max(unit.Bounds.size.x, unit.Bounds.size.z) > r.maxUnitSpan;
                    if (wide && !off && !prefabPart && t.childCount > 0 && OnlyTransform(t.gameObject)) { foreach (Transform c in t) Walk(c, true); return; }
                    plan.Units.Add(unit); return;
                }
                // not all static below: cross it only when it is a plain active grouping object
                if (OnlyTransform(t.gameObject) && !PrefabUtility.IsPartOfPrefabInstance(t.gameObject)) { foreach (Transform c in t) Walk(c, true); return; }
                if (OnlyTransform(t.gameObject)) { Keep("inside a prefab instance whose root holds more than static parts", t); return; }
                Keep("under or on " + (OwnReason(t.gameObject) ?? node.Reason), t);
            }
            foreach (var root in scene.GetRootGameObjects()) Walk(root.transform, false);

            // own rules per unit
            foreach (var u in plan.Units)
            {
                string hit = r.protectedNameParts.FirstOrDefault(p => u.Path.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hit != null) { u.Refused = "protected tree (name " + hit + ")"; continue; }
                string prefab = PrefabUtility.IsPartOfPrefabInstance(u.Root.gameObject) ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(u.Root.gameObject) : "";
                foreach (string asset in u.Meshes.Select(AssetDatabase.GetAssetPath).Append(prefab))
                {
                    if (asset == null) continue;
                    hit = r.protectedAssetParts.FirstOrDefault(p => asset.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (hit != null) { u.Refused = "protected tree (asset " + hit + ")"; break; }
                }
                if (u.Refused != null) continue;
                if (u.Meshes.Any(m => string.IsNullOrEmpty(AssetDatabase.GetAssetPath(m)))) { u.Refused = "a mesh that is not an asset (lives in the scene file)"; continue; }
                foreach (var t in u.Root.GetComponentsInChildren<Transform>(true).Concat(Ancestors(u.Root)))
                    if (r.runtimeNames.Contains(t.name) || r.runtimeNamePrefixes.Any(p => t.name.StartsWith(p, StringComparison.Ordinal))) { u.Refused = "runtime code looks up the name " + t.name; break; }
                if (u.Refused != null) continue;
                foreach (var mr in u.Root.GetComponentsInChildren<MeshRenderer>(true)) if (mr.lightmapIndex >= 0 && mr.lightmapIndex < 65534) plan.Lightmapped++;
            }

            // references across the cut (serialized, every component of the scene)
            var owner = new Dictionary<int, int>(); var inScene = new HashSet<int>(); var above = new Dictionary<int, string>();
            for (int i = 0; i < plan.Units.Count; i++)
            {
                if (plan.Units[i].Refused != null) continue;
                foreach (var c in plan.Units[i].Root.GetComponentsInChildren<Component>(true)) { owner[c.GetInstanceID()] = i; owner[c.gameObject.GetInstanceID()] = i; }
                foreach (var t in Ancestors(plan.Units[i].Root)) { above[t.GetInstanceID()] = PathOf(t); above[t.gameObject.GetInstanceID()] = PathOf(t); }
            }
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Component>(true)).Where(c => c != null).ToArray();
            foreach (var c in all) { inScene.Add(c.GetInstanceID()); inScene.Add(c.gameObject.GetInstanceID()); }
            var aboveHits = new Dictionary<string, int>();
            foreach (var c in all)
            {
                if (c is Transform) continue;
                owner.TryGetValue(c.GetInstanceID(), out int from); if (!owner.ContainsKey(c.GetInstanceID())) from = -1;
                var it = new SerializedObject(c).GetIterator(); bool enter = true;
                while (it.Next(enter))
                {
                    enter = true;
                    if (it.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        enter = false; int id = it.objectReferenceInstanceIDValue; if (id == 0) continue;
                        if (it.objectReferenceValue == null) { plan.Broken++; continue; }
                        if (it.propertyPath == "m_GameObject" || !inScene.Contains(id)) continue;
                        bool to = owner.TryGetValue(id, out int target);
                        if (to && target != from) plan.Units[target].Refused ??= "referenced by " + c.GetType().Name + " on " + PathOf(c.transform) + " (" + it.propertyPath + ")";
                        if (from >= 0 && (!to || target != from)) plan.Units[from].Refused ??= "its " + c.GetType().Name + " references " + it.objectReferenceValue.name + " outside it (" + it.propertyPath + ")";
                        if (from < 0 && above.TryGetValue(id, out string group)) { string key = group + " <- " + c.GetType().Name + " on " + PathOf(c.transform); aboveHits.TryGetValue(key, out int n); aboveHits[key] = n + 1; }
                    }
                    else if (it.propertyType == SerializedPropertyType.String) enter = false;
                    else if (it.isArray)
                    {
                        if (it.arraySize == 0) enter = false;
                        else { var kind = it.GetArrayElementAtIndex(0).propertyType; if (kind != SerializedPropertyType.Generic && kind != SerializedPropertyType.ObjectReference && kind != SerializedPropertyType.ManagedReference) enter = false; }
                    }
                }
            }
            foreach (var kv in aboveHits.OrderByDescending(k => k.Value).Take(40)) plan.Warnings.Add("a grouping object above moving units is referenced: " + kv.Key);
            foreach (var u in plan.Units.Where(u => u.Refused != null)) Keep(u.Refused.StartsWith("referenced by", StringComparison.Ordinal) ? "referenced by a component that stays" : u.Refused.StartsWith("its ", StringComparison.Ordinal) ? "references something outside itself" : u.Refused.StartsWith("runtime code", StringComparison.Ordinal) ? "runtime code looks up a name in it" : u.Refused, u.Root);

            // realm of each moving unit: the polygon its centre is in, else the nearest
            foreach (var u in plan.Moving)
            {
                Vector3 centre = u.HasBounds ? u.Bounds.center : u.Root.position; float best = float.PositiveInfinity;
                for (int i = 0; i < plan.Realms.Length; i++)
                {
                    float d = RealmStreamSheet308.Distance(new RealmStreamSheet308.Realm { Outline = plan.Realms[i].Polygon }, centre);
                    if (d < best) { best = d; u.Realm = i; }
                }
            }
            return plan;
        }

        static IEnumerable<Transform> Ancestors(Transform t) { for (t = t.parent; t != null; t = t.parent) yield return t; }

        static Unit Measure(Transform t, string path)
        {
            var u = new Unit { Root = t, Path = path };
            foreach (var x in t.GetComponentsInChildren<Transform>(true)) u.Objects++;
            // bounds from the mesh and the transform: a switched-off renderer or collider reports none
            foreach (var f in t.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh != null) { u.Vertices += f.sharedMesh.vertexCount; u.Meshes.Add(f.sharedMesh); Grow(u, World(f.sharedMesh.bounds, f.transform.localToWorldMatrix)); }
            foreach (var m in t.GetComponentsInChildren<MeshRenderer>(true)) u.Renderers++;
            foreach (var c in t.GetComponentsInChildren<Collider>(true))
            {
                u.Colliders++;
                if (c is MeshCollider mc) { if (mc.sharedMesh != null) { u.Meshes.Add(mc.sharedMesh); Grow(u, World(mc.sharedMesh.bounds, c.transform.localToWorldMatrix)); } }
                else if (c is BoxCollider box) Grow(u, World(new Bounds(box.center, box.size), c.transform.localToWorldMatrix));
                else Grow(u, new Bounds(c.transform.position, Vector3.one));
            }
            return u;
        }

        static Bounds World(Bounds local, Matrix4x4 m)
        {
            Vector3 c = m.MultiplyPoint3x4(local.center), e = local.extents;
            var size = new Vector3(
                Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z,
                Mathf.Abs(m.m10) * e.x + Mathf.Abs(m.m11) * e.y + Mathf.Abs(m.m12) * e.z,
                Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z) * 2;
            return new Bounds(c, size);
        }

        static void Grow(Unit u, Bounds b) { if (b.size == Vector3.zero) return; if (!u.HasBounds) { u.Bounds = b; u.HasBounds = true; } else u.Bounds.Encapsulate(b); }

        static string Report(Plan plan, Rules r, bool write)
        {
            var scene = SceneManager.GetSceneByPath(r.mainScene);
            var moving = plan.Moving.ToList();
            var movingMeshes = new HashSet<Mesh>(moving.SelectMany(u => u.Meshes));
            var movingObjects = new HashSet<Transform>(moving.Select(u => u.Root));
            // meshes something that stays still uses (they are not freed when a realm is let go)
            var staying = new HashSet<Mesh>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var f in root.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh != null && movingMeshes.Contains(f.sharedMesh) && !Under(f.transform, movingObjects)) staying.Add(f.sharedMesh);
                foreach (var c in root.GetComponentsInChildren<MeshCollider>(true)) if (c.sharedMesh != null && movingMeshes.Contains(c.sharedMesh) && !Under(c.transform, movingObjects)) staying.Add(c.sharedMesh);
            }
            var sb = new StringBuilder("RealmStream308 plan " + r.mainScene + "\n");
            sb.AppendLine("moving " + N(moving.Count) + " units | objects " + N(moving.Sum(u => u.Objects)) + " | renderers " + N(moving.Sum(u => u.Renderers)) + " | colliders " + N(moving.Sum(u => u.Colliders)) + " | vertices " + N(moving.Sum(u => u.Vertices))
                + " | distinct meshes " + N(movingMeshes.Count) + " " + Mb(movingMeshes.Sum(m => Profiler.GetRuntimeMemorySizeLong(m))) + " (of them still used by what stays " + N(staying.Count) + " " + Mb(staying.Sum(m => Profiler.GetRuntimeMemorySizeLong(m))) + ")");
            sb.AppendLine("broken references in the scene now " + plan.Broken + " | lightmapped renderers among the moving " + plan.Lightmapped);
            for (int i = 0; i < plan.Realms.Length; i++)
            {
                var mine = moving.Where(u => u.Realm == i).ToList(); var meshes = new HashSet<Mesh>(mine.SelectMany(u => u.Meshes));
                var others = new HashSet<Mesh>(moving.Where(u => u.Realm != i).SelectMany(u => u.Meshes)); others.UnionWith(staying);
                sb.AppendLine("  " + plan.Realms[i].Id.PadRight(12) + " units " + N(mine.Count).PadLeft(6) + " renderers " + N(mine.Sum(u => u.Renderers)).PadLeft(6) + " colliders " + N(mine.Sum(u => u.Colliders)).PadLeft(5) + " vertices " + N(mine.Sum(u => u.Vertices)).PadLeft(11)
                    + " meshes " + Mb(meshes.Sum(m => Profiler.GetRuntimeMemorySizeLong(m))).PadLeft(10) + " only here " + Mb(meshes.Where(m => !others.Contains(m)).Sum(m => Profiler.GetRuntimeMemorySizeLong(m))).PadLeft(10));
            }
            sb.AppendLine("stays (reason: groups, mesh bytes, one example)");
            foreach (var kv in plan.Kept.OrderByDescending(k => k.Value.bytes).Take(30)) sb.AppendLine("  " + N(kv.Value.count).PadLeft(6) + " " + Mb(kv.Value.bytes).PadLeft(10) + "  " + kv.Key + "  e.g. " + kv.Value.sample);
            sb.AppendLine("moving, by top object");
            foreach (var g in moving.GroupBy(u => string.Join("/", u.Path.Split('/').Take(2))).OrderByDescending(g => g.Sum(u => u.Vertices)).Take(30))
                sb.AppendLine("  " + N(g.Count()).PadLeft(6) + " units " + N(g.Sum(u => u.Vertices)).PadLeft(12) + " v  " + g.Key);
            foreach (string w in plan.Warnings) sb.AppendLine("WARNING " + w);
            var marks = moving.Where(u => u.HasBounds && (u.Bounds.size.y >= r.landmarkHeight || Mathf.Max(u.Bounds.size.x, u.Bounds.size.z) >= r.landmarkSpan)).OrderByDescending(u => u.Bounds.size.y).ToList();
            sb.AppendLine("tall or wide units (seen from far, gone beyond the load distance): " + marks.Count);
            foreach (var u in marks.Take(40)) sb.AppendLine("  " + plan.Realms[u.Realm].Id.PadRight(12) + " h " + F(u.Bounds.size.y).PadLeft(4) + " m span " + F(Mathf.Max(u.Bounds.size.x, u.Bounds.size.z)).PadLeft(4) + " m at " + F(u.Bounds.center.x) + "," + F(u.Bounds.center.z) + "  " + u.Path);
            if (write)
            {
                Directory.CreateDirectory(Stage);
                var full = new StringBuilder(sb.ToString()); full.AppendLine("---- every unit");
                foreach (var u in plan.Units.OrderBy(u => u.Path)) full.AppendLine((u.Refused == null ? plan.Realms[u.Realm].Id : "STAYS").PadRight(12) + N(u.Renderers).PadLeft(5) + " r " + N(u.Vertices).PadLeft(10) + " v  " + u.Path + (u.Refused == null ? "" : "  <- " + u.Refused));
                File.WriteAllText(Path.Combine(Stage, "plan.txt"), full.ToString());
                sb.AppendLine("full list: Tools/Unity/Stage308_stream/plan.txt");
            }
            return sb.ToString();
        }

        static bool Under(Transform t, HashSet<Transform> roots) { for (; t != null; t = t.parent) if (roots.Contains(t)) return true; return false; }

        static Vector2[] Hull(IEnumerable<Unit> units)
        {
            var points = new List<Vector2>();
            foreach (var u in units)
            {
                if (!u.HasBounds) { points.Add(new Vector2(u.Root.position.x, u.Root.position.z)); continue; }
                Vector3 a = u.Bounds.min, b = u.Bounds.max;
                points.Add(new Vector2(a.x, a.z)); points.Add(new Vector2(a.x, b.z)); points.Add(new Vector2(b.x, a.z)); points.Add(new Vector2(b.x, b.z));
            }
            points = points.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToList();
            if (points.Count < 3) return points.ToArray();
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            var hull = new List<Vector2>();
            foreach (var p in points) { while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
            int lower = hull.Count + 1;
            for (int i = points.Count - 2; i >= 0; i--) { var p = points[i]; while (hull.Count >= lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
            hull.RemoveAt(hull.Count - 1);
            return hull.ToArray();
        }
    }
}
