using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #303 roadside stories and side commissions (SPEC-ROADSIDE-COMMISSIONS-303): wrecked carts and collapsed houses on
    // road shoulders, three 의뢰 (giver → evidence or target → report → reward) in 청림 and on the 상경 가도.
    //   build                          — see Roadside303.Build.cs
    //   dressing                       — art renderers and their sheets in the open scene
    //   describe-prefab:<asset path>   — child meshes, triangles, local bounds and materials of a prefab
    //   describe:<scene object path>   — components and child renderers of a scene object
    public static partial class Roadside303
    {
        public const string Output = "../Art/World/Compact/Rebuild/Roadside303";   // repo Art/ (Assets/.. is the Unity project, one more up is the repo)

        public static string Run(string command)
        {
            int c = command.IndexOf(':'); string head = c < 0 ? command : command.Substring(0, c), arg = c < 0 ? "" : command.Substring(c + 1);
            switch (head)
            {
                case "build": return Build();
                case "build-scene": return BuildScene(arg);
                case "play-test": return PlayTestStart();
                case "play-test-status": return PlayTestStatus();
                case "describe-prefab": return DescribePrefab(arg);
                case "describe": return DescribeScene(arg);
                case "player":
                {
                    var sb = new StringBuilder();
                    foreach (var ap in Object.FindObjectsByType<Oheangbu.App.World.WorldMacroPlayerAppearance>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        var an = ap.Animator;
                        sb.AppendLine("appearance " + PathOf(ap.transform, null) + " animator " + (an != null ? PathOf(an.transform, null) : "none") + " avatar " + (an != null && an.avatar != null ? an.avatar.name + " human " + an.avatar.isHuman + " path " + AssetDatabase.GetAssetPath(an.avatar) : "none") +
                            " controller " + (an != null && an.runtimeAnimatorController != null ? AssetDatabase.GetAssetPath(an.runtimeAnimatorController) : "none") + " profile " + (ap.Profile != null ? AssetDatabase.GetAssetPath(ap.Profile) + " ctrl " + AssetDatabase.GetAssetPath(ap.Profile.Controller) : "none"));
                        if (an != null && an.isHuman) foreach (var b in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Head, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot }) sb.AppendLine("   " + b + " " + (an.GetBoneTransform(b) != null ? an.GetBoneTransform(b).name : "-"));
                    }
                    return sb.ToString();
                }
                case "grass":
                {
                    var sb = new StringBuilder();
                    foreach (var g in Object.FindObjectsByType<Oheangbu.App.World.CompactGrassRenderer266>(FindObjectsSortMode.None))
                    {
                        sb.AppendLine("grass " + PathOf(g.transform, null) + " field " + AssetDatabase.GetAssetPath(g.Field) + " seeds " + (g.Field != null ? g.Field.Count : 0) + " cells " + (g.Field != null ? g.Field.Cells.Length : 0) +
                            " near mesh " + (g.Field?.NearMesh != null ? V(g.Field.NearMesh.bounds.size) : "-") + " far mesh " + (g.Field?.FarMesh != null ? V(g.Field.FarMesh.bounds.size) : "-"));
                        if (arg.Length > 0 && g.Field != null)
                        {
                            var a = arg.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray(); var at = new Vector3(a[0], 0, a[1]); float r = a[2];
                            var cc = g.Field.Coordinate(at); int n = 0; float nearest = float.MaxValue;
                            for (int z = cc.y - 1; z <= cc.y + 1; z++) for (int x = cc.x - 1; x <= cc.x + 1; x++) { int i = g.Field.Index(x, z); if (i < 0 || g.Field.Cells[i] == null) continue; foreach (var sd in g.Field.Cells[i].Seeds) { float d = new Vector2(sd.Position.x - at.x, sd.Position.z - at.z).magnitude; nearest = Mathf.Min(nearest, d); if (d < r) n++; } }
                            sb.AppendLine("  seeds within " + r + " m: " + n + ", nearest " + nearest.ToString("F2", CultureInfo.InvariantCulture));
                        }
                    }
                    return sb.ToString();
                }
                case "dressing-near":
                {
                    var a = arg.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray(); var at = new Vector3(a[0], 0, a[1]); float r = a[2];
                    var sb = new StringBuilder();
                    foreach (var art in Object.FindObjectsByType<Oheangbu.App.World.CompactRebuildArtRenderer>(FindObjectsSortMode.None))
                    {
                        if (art.Sheet == null) continue; var byProto = art.Sheet.Prototypes.ToDictionary(p => p.Id, p => p);
                        var near = art.Sheet.FixedPlacements.Where(f => f != null && new Vector2(f.Position.x - at.x, f.Position.z - at.z).magnitude < r).ToArray();
                        sb.AppendLine(PathOf(art.transform, null) + " pos " + V(art.transform.position) + " rot " + V(art.transform.eulerAngles) + " near " + near.Length + " kinds " +
                            string.Join(",", near.GroupBy(f => byProto.TryGetValue(f.PrototypeId, out var p) ? p.Category.ToString() : "?").Select(g => g.Key + ":" + g.Count())));
                        foreach (var f in near.Take(6)) sb.AppendLine("   " + f.PrototypeId + " " + V(f.Position) + " scale " + f.Scale.ToString("F2", CultureInfo.InvariantCulture));
                    }
                    return sb.ToString();
                }
                case "dressing":
                {
                    var sb = new StringBuilder();
                    foreach (var r in Object.FindObjectsByType<Oheangbu.App.World.CompactRebuildArtRenderer>(FindObjectsSortMode.None))
                        sb.AppendLine("art " + PathOf(r.transform, null) + " sheet " + AssetDatabase.GetAssetPath(r.Sheet) + " fixed " + (r.Sheet != null ? r.Sheet.FixedPlacements.Length : 0) + " preserves " + (r.Sheet != null ? r.Sheet.PreservedAreas.Length : 0));
                    foreach (var r in Object.FindObjectsByType<Oheangbu.App.World.Dressing.WorldMacroDressingRenderer>(FindObjectsSortMode.None))
                        sb.AppendLine("dressing " + PathOf(r.transform, null) + " sheet " + AssetDatabase.GetAssetPath(r.Sheet) + " fixed " + (r.Sheet != null ? r.Sheet.FixedPlacements.Length : 0) + " preserves " + (r.Sheet != null ? r.Sheet.PreservedAreas.Length : 0));
                    return sb.ToString();
                }
                default: throw new ArgumentException("Roadside303: build | dressing | describe-prefab:<path> | describe:<scene path>");
            }
        }

        static string DescribePrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new Exception("no prefab " + path);
            var sb = new StringBuilder();
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                long tris = 0; if (mesh != null) for (int i = 0; i < mesh.subMeshCount; i++) tris += mesh.GetIndexCount(i) / 3;
                var m = prefab.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                Bounds b = mesh != null ? mesh.bounds : default;
                Vector3 centre = m.MultiplyPoint3x4(b.center);
                sb.AppendLine($"{PathOf(r.transform, prefab.transform)} | {r.GetType().Name} | mesh {mesh?.name} tris {tris} | centre {V(centre)} size {V(Vector3.Scale(b.size, m.lossyScale))} | {string.Join(",", r.sharedMaterials.Select(x => x != null ? x.name : "null"))}");
            }
            return sb.ToString();
        }

        static string DescribeScene(string path)
        {
            var t = Find(path) ?? throw new Exception("no scene object " + path);
            var sb = new StringBuilder(PathOf(t, null) + " at " + V(t.position) + " rot " + V(t.eulerAngles) + " scale " + V(t.lossyScale) + "\n");
            foreach (var comp in t.GetComponents<Component>()) sb.AppendLine("  component " + comp.GetType().FullName);
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                sb.AppendLine("  renderer " + PathOf(r.transform, t) + " " + r.GetType().Name + " " + (r is SkinnedMeshRenderer s ? s.sharedMesh?.name : r.GetComponent<MeshFilter>()?.sharedMesh?.name) + " mats " + string.Join(",", r.sharedMaterials.Select(x => x != null ? x.name : "null")));
            foreach (var a in t.GetComponentsInChildren<Animator>(true)) sb.AppendLine("  animator " + PathOf(a.transform, t) + " controller " + (a.runtimeAnimatorController != null ? AssetDatabase.GetAssetPath(a.runtimeAnimatorController) : "none") + " avatar " + (a.avatar != null ? a.avatar.name : "none"));
            foreach (var col in t.GetComponentsInChildren<Collider>(true)) sb.AppendLine("  collider " + PathOf(col.transform, t) + " " + col.GetType().Name + " trigger " + col.isTrigger);
            return sb.ToString();
        }

        internal static Transform Find(string path)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == path) return root.transform;
                if (path.StartsWith(root.name + "/")) { var t = root.transform.Find(path.Substring(root.name.Length + 1)); if (t != null) return t; }
            }
            return null;
        }

        internal static string PathOf(Transform t, Transform relativeTo)
        {
            string p = t.name;
            while (t.parent != null && t.parent != relativeTo) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        internal static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "({0:F2}, {1:F2}, {2:F2})", v.x, v.y, v.z);
        internal static string AbsOutput => Path.GetFullPath(Path.Combine(Application.dataPath, "..", Output));
    }
}
