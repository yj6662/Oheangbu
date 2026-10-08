using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 steam-temple kit: how the parts go on a building (D308-46 · D308-47, SPEC-ARCH-TEMPLE-308 §13c · §14). Every value [TEST].
    // Parts: Meshy (part_*: fire-box, vessel, canopy, collar, flywheel, gear, cylinder, bell, case, stand) and Blender-made plain pieces
    // (plain_*: pipe, joint, bracket, flue, footing, base, rod, ram, shaft, valve, rail). One rule for every hall, sized by the hall:
    //   outside  lotus collars on the outer pillars · a double pipe run under the eave, front and back · on the +X end a boiler on its own
    //            granite base outside the eave, its flue beside the roof to a canopy cap · on that end wall a steam cylinder driving a
    //            flywheel and gear on one shaft · flue caps on the ridge · two steam lanterns on the plinth
    //   inside   (large halls with inner pillars) the centre front bay is opened · an engine shrine against the back wall, its flue
    //            through the roof · a flywheel engine on each side of it · pipe runs along the inner pillar rows with drops and valves ·
    //            four steam lanterns as cover · the floor between the pillar rows stays clear for fighting
    //   light    (D308-48) a steam lantern is a lamp: its porthole glass glows (the canon lamp material InnLantern297, no new colour) and the
    //            stand-alone lantern, the lanterns and hanging lamps inside a hall and the engine shrine carry one warm point light, no shadow
    public static partial class KitDress308
    {
        sealed class Builder
        {
            public Transform root; Mesh cube;
            public Builder(Transform root) { this.root = root; cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx"); }
            public bool Exists(string part) => AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Prefabs/Kit_" + part + ".prefab") != null;
            GameObject Kit(string part, string name = null)
            {
                // every Meshy part is used through its light copy (Kit_<part>s, Tools/Blender/Temple308/lite_parts.py) when there is one
                var p = part.StartsWith("part_", StringComparison.Ordinal) ? AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Prefabs/Kit_" + part + "s.prefab") : null;
                if (p == null) p = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Prefabs/Kit_" + part + ".prefab"); if (p == null) throw new InvalidOperationException("no Kit_" + part);
                var g = (GameObject)PrefabUtility.InstantiatePrefab(p, root); if (name != null) g.name = name; return g;
            }
            static Bounds MB(GameObject g) => g.GetComponent<MeshFilter>().sharedMesh.bounds;

            // a stack part stretched to an outer size (across = widest horizontal, tall = height; tall <= 0 keeps the part's own proportion); returns its top
            public float Stack(string part, float across, float tall, Vector3 at, float yaw = 180f, string name = null)
            {
                var g = Kit(part, name); var mb = MB(g); float k = across / Mathf.Max(mb.size.x, mb.size.z), ky = tall > 0f ? tall / mb.size.y : k;
                g.transform.localPosition = at - new Vector3(0f, mb.min.y * ky, 0f); g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f); g.transform.localScale = new Vector3(k, ky, k);
                return at.y + mb.size.y * ky;
            }
            // a wheel part of a given diameter; axis = the direction its shaft points
            public GameObject Wheel(string part, float diameter, Vector3 centre, Vector3 axis, string name = null)
            {
                var g = Kit(part, name); var mb = MB(g); float k = diameter / Mathf.Max(mb.size.x, mb.size.y);
                g.transform.localPosition = centre; g.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, axis.normalized); g.transform.localScale = Vector3.one * k; return g;
            }
            // the steam cylinder lying along a direction, its middle at a point; length = its long side
            public GameObject Lying(string part, float length, Vector3 centre, Vector3 along, string name = null)
            {
                var g = Kit(part, name); var mb = MB(g); bool longX = mb.size.x >= mb.size.z; float k = length / Mathf.Max(mb.size.x, mb.size.z);
                g.transform.localRotation = Quaternion.LookRotation(longX ? Vector3.Cross(Vector3.up, along.normalized) : along.normalized, Vector3.up); g.transform.localScale = Vector3.one * k;
                g.transform.localPosition = centre - g.transform.localRotation * (mb.center * k); return g;
            }
            // a piece that runs along its own +Y (pipe 2 m, flue 2 m, shaft 1 m, rod 1 m, ram 1 m), repeated from a to b; thick = sideways scale
            public void Along(string part, Vector3 a, Vector3 b, float unit, float thick, string name = null, bool single = false)
            {
                float len = (b - a).magnitude; if (len < .02f) return; var dir = (b - a) / len; int n = single ? 1 : Mathf.Max(1, Mathf.RoundToInt(len / unit)); float seg = len / n;
                var rot = Quaternion.FromToRotation(Vector3.up, dir);
                for (int i = 0; i < n; i++) { var g = Kit(part, name); g.transform.localPosition = a + dir * (seg * i); g.transform.localRotation = rot; g.transform.localScale = new Vector3(thick, seg / unit, thick); }
            }
            public void Pipe(float radius, params Vector3[] pts)
            {
                float t = radius / .045f;
                for (int i = 0; i + 1 < pts.Length; i++) Along("plain_pipe", pts[i], pts[i + 1], 2f, t, "Pipe");
                for (int i = 1; i + 1 < pts.Length; i++) { var g = Kit("plain_joint", "PipeJoint"); g.transform.localPosition = pts[i]; g.transform.localScale = Vector3.one * t; }
            }
            // two parallel pipes (.15 m apart) through the same points; wall = the side the brackets fasten to (zero = no brackets)
            public void Pipes(float radius, Vector3 wall, params Vector3[] pts)
            {
                Pipe(radius, pts); Pipe(radius, pts.Select(q => q + Vector3.down * .15f).ToArray());
                if (wall.sqrMagnitude < .01f) return;
                for (int i = 0; i + 1 < pts.Length; i++)
                {
                    var d = pts[i + 1] - pts[i]; if (Mathf.Abs(d.y) > .2f || d.magnitude < 1.2f) continue; int n = Mathf.Max(1, Mathf.RoundToInt(d.magnitude / 2.4f));
                    for (int j = 0; j < n; j++) { var g = Kit("plain_bracket", "PipeBracket"); g.transform.localPosition = pts[i] + d * ((j + .5f) / n) + Vector3.down * .075f; g.transform.localRotation = Quaternion.LookRotation(Vector3.Cross(d.normalized, Vector3.up) * Mathf.Sign(Vector3.Dot(Vector3.Cross(d.normalized, Vector3.up), wall)), Vector3.up); }
                }
            }
            // lamp glass: a flat glowing lens (canon lamp material) just proud of a porthole, facing `out`
            public void Glow(Vector3 centre, Vector3 outward, float radius)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/World/Finish297/Materials/InnLantern297.mat"); if (m == null) return;
                var g = new GameObject("LampGlow"); g.transform.SetParent(root, false); var mesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx"); var sb = mesh.bounds.size;
                g.AddComponent<MeshFilter>().sharedMesh = mesh; var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                g.transform.localPosition = centre; g.transform.localRotation = Quaternion.LookRotation(outward.normalized, Vector3.up); g.transform.localScale = new Vector3(radius * 2f / sb.x, radius * 2f / sb.y, radius * .5f / sb.z);
            }
            // lamp light: warm (the rest-lantern colour), no shadow
            public void Light(Vector3 at, float range, float intensity)
            {
                var g = new GameObject("LampLight"); g.transform.SetParent(root, false); g.transform.localPosition = at; var l = g.AddComponent<Light>();
                l.type = LightType.Point; l.color = new Color(1f, .58f, .26f); l.range = range; l.intensity = intensity; l.shadows = LightShadows.None;
            }
            public void Valve(Vector3 at, Vector3 along) { var g = Kit("plain_valve", "Valve"); g.transform.localPosition = at; g.transform.localRotation = Quaternion.FromToRotation(Vector3.right, along.normalized); }
            public void Footing(Vector3 bottom, float sx, float h, float sz, string name = "Footing") { var g = Kit("plain_footing", name); g.transform.localPosition = bottom; g.transform.localScale = new Vector3(sx, h / .25f, sz); }
            public void Base(Vector3 bottom, float sx, float h, float sz) { if (h < .04f) return; var g = Kit("plain_base", "Base"); g.transform.localPosition = bottom; g.transform.localScale = new Vector3(sx, h, sz); }
            public void Rail(Vector3 a, Vector3 b)
            {
                float len = (b - a).magnitude; int n = Mathf.Max(1, Mathf.RoundToInt(len / 2f)); var dir = (b - a) / len; var rot = Quaternion.FromToRotation(Vector3.right, dir);
                for (int i = 0; i < n; i++) { var g = Kit("plain_rail", "Rail"); g.transform.localPosition = a + dir * (len / n * i); g.transform.localRotation = rot; g.transform.localScale = new Vector3(len / n / 2f, 1f, 1f); }
            }
            // the cast A-frame pedestal under a shaft: its bearing takes the shaft at `hub`, feet on `floor`
            public void Stand(Vector3 hub, float floor, Vector3 axis)
            {
                var g = Kit("part_stand", "Stand"); var mb = MB(g); float k = (hub.y - floor) / (mb.size.y * .775f);
                g.transform.localPosition = new Vector3(hub.x, floor - mb.min.y * k, hub.z); g.transform.localRotation = Quaternion.LookRotation(axis.normalized, Vector3.up); g.transform.localScale = Vector3.one * k;
            }
            public void Box(string name, Vector3 centre, Vector3 size, Material m)
            {
                var g = new GameObject(name); g.transform.SetParent(root, false); g.AddComponent<MeshFilter>().sharedMesh = cube; g.AddComponent<MeshRenderer>().sharedMaterial = m; var qb = cube.bounds.size;
                g.transform.localPosition = centre; g.transform.localScale = new Vector3(size.x / qb.x, size.y / qb.y, size.z / qb.z);
            }
            // boiler: granite footing, fire-box, lotus collar, pressure vessel; yaw 180 = stoke door to +Z; returns the vessel's top
            public float Boiler(Vector3 at, float size, float yaw, bool lit = false)
            {
                Footing(at, 1.5f * size, .22f * size, 1.5f * size);
                float y = Stack("part_firebox", 1.15f * size, 1.05f * size, at + new Vector3(0f, .22f * size, 0f), yaw, "FireBox");
                y = Stack("part_collar", .9f * size, .22f * size, new Vector3(at.x, y - .03f, at.z), 0f, "Collar") - .04f;
                float top = Stack("part_vessel", 1.2f * size, 1.15f * size, new Vector3(at.x, y, at.z), yaw - 90f, "Vessel");
                if (lit)
                {
                    var c = new Vector3(at.x, y + 1.15f * size * .52f, at.z); var d = Quaternion.Euler(0f, yaw - 90f, 0f) * Vector3.right;
                    Glow(c + d * (.6f * size * .97f), d, .2f * size); Glow(c - d * (.6f * size * .97f), -d, .2f * size); Light(c + Quaternion.Euler(0f, yaw, 0f) * Vector3.back * (.9f * size), 11f, 3.2f);
                }
                return top;
            }
            // flue tube from a point up to a height, a lotus collar every two metres, ending in the copper canopy cap; returns the cap's top
            public float Flue(Vector3 from, float top, float radius, float cap)
            {
                Along("plain_flue", from, new Vector3(from.x, top, from.z), 2f, radius / .2f, "Flue");
                for (float y = from.y + 1.6f; y < top - .8f; y += 2.1f) Stack("part_collar", radius * 3.3f, radius * 1.3f, new Vector3(from.x, y, from.z), 0f, "FlueCollar");
                return Stack("part_canopy", cap, cap * .9f, new Vector3(from.x, top - .05f, from.z), 0f, "FlueCap");
            }
            public void Lantern(Vector3 at, float yaw, float g = 1f, float lightRange = 0f)
            {
                float z = at.y; Footing(at, 1.0f * g, .24f * g, 1.0f * g, "LanternFoot"); z += .24f * g;
                z = Stack("part_firebox", .62f * g, .44f * g, new Vector3(at.x, z, at.z), yaw + 180f, "LanternBox");
                Stack("part_collar", .40f * g, .12f * g, new Vector3(at.x, z - .01f, at.z), 0f, "LanternCollar"); z += .10f * g;
                Along("plain_flue", new Vector3(at.x, z, at.z), new Vector3(at.x, z + .44f * g, at.z), 2f, .13f * g / .2f, "LanternColumn", true); z += .44f * g;
                Stack("part_collar", .46f * g, .12f * g, new Vector3(at.x, z - .01f, at.z), 0f, "LanternCollar"); z += .10f * g;
                var lens = new Vector3(at.x, z + .42f * g * .52f, at.z); var face = Quaternion.Euler(0f, yaw + 90f, 0f) * Vector3.right;
                z = Stack("part_vessel", .70f * g, .42f * g, new Vector3(at.x, z, at.z), yaw + 90f, "LanternVessel");
                Glow(lens + face * (.35f * g * .97f), face, .115f * g); Glow(lens - face * (.35f * g * .97f), -face, .115f * g);
                if (lightRange > 0f) Light(lens + Vector3.up * (.1f * g), lightRange, 3f);
                Stack("part_canopy", 1.08f * g, .58f * g, new Vector3(at.x, z - .03f * g, at.z), yaw, "LanternCap");
            }
            // hanging lamp: a small porthole vessel on a rod from `from` down to `lampY`, glowing, with its light
            public void HangingLamp(Vector3 from, float lampY, float g)
            {
                Along("plain_shaft", new Vector3(from.x, lampY + .3f * g, from.z), from, 1f, .35f, "LampRod", true);
                Stack("part_collar", .34f * g, .1f * g, new Vector3(from.x, lampY + .28f * g, from.z), 0f, "LampCollar");
                Stack("part_vessel", .5f * g, .3f * g, new Vector3(from.x, lampY, from.z), 0f, "LampVessel");
                var c = new Vector3(from.x, lampY + .3f * g * .52f, from.z); Glow(c + Vector3.right * (.25f * g * .97f), Vector3.right, .085f * g); Glow(c + Vector3.left * (.25f * g * .97f), Vector3.left, .085f * g);
                Light(c + Vector3.down * (.25f * g), 12f, 3.4f);
            }
            // engine: flywheel and gear on one shaft over `under` (a floor point), the shaft along `axis`; a steam cylinder on its footing
            // `along` from the wheel, joined by a connecting rod. wallSide = the shaft's inner end rests in a wall (one pedestal), else two.
            public void Engine(Vector3 under, Vector3 axis, Vector3 along, float g, bool wallSide)
            {
                axis.Normalize(); along.Normalize(); var hub = under + Vector3.up * (1.5f * g);
                Wheel("part_flywheel", 2.6f * g, hub + axis * (.2f * g), axis, "Flywheel"); Wheel("part_gear", 1.9f * g, hub - axis * (.32f * g), axis, "Gear");
                Along("plain_shaft", hub - axis * (wallSide ? .9f * g : .85f * g), hub + axis * (.85f * g), 1f, .07f * g / .06f, "Shaft", true);
                Stand(hub + axis * (.66f * g), under.y, axis); Footing(under + axis * (.66f * g), 1.5f * g, .14f, .7f * g);
                if (!wallSide) { Stand(hub - axis * (.72f * g), under.y, axis); Footing(under - axis * (.72f * g), 1.5f * g, .14f, .7f * g); }
                var cc = under + along * (2.5f * g) + axis * (.1f * g); float cy = under.y + .2f + .62f * g;
                bool alongX = Mathf.Abs(along.x) > Mathf.Abs(along.z); Footing(cc, alongX ? 2.4f * g : 1.2f * g, .2f, alongX ? 1.2f * g : 2.4f * g);
                Lying("part_cylinder", 2.0f * g, new Vector3(cc.x, cy, cc.z), along, "Cylinder");
                var crank = hub + axis * (.46f * g) + Vector3.up * (.55f * g) + along * (.35f * g); var gland = new Vector3(cc.x, cy, cc.z) - along * (.95f * g) + axis * (.36f * g);
                Along("plain_rod", gland, crank, 1f, 1.3f * g, "ConnectingRod", true);
            }
        }

        static GameObject Holder(string name, UnityEngine.SceneManagement.Scene scene, out Builder k)
        {
            var holder = new GameObject(name); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, scene);
            var kit = new GameObject("Kit"); kit.transform.SetParent(holder.transform, false); k = new Builder(kit.transform); return holder;
        }

        // Pieces that will turn, slide or swing stay objects of their own; lamp glass and lamp lights too (the LODGroup and the checks look for them).
        static readonly HashSet<string> Moving = new HashSet<string> { "Flywheel", "Gear", "BigGear", "Pinion", "Shaft", "PinionShaft", "CrossShaft", "ConnectingRod", "Ram", "RamRod", "SutraCase", "SutraCap", "LampGlow", "LampLight", "Interior" };
        // Small fittings: drawn near only, never a collider (KitSmall_<material>); everything else merges into KitLarge_<material>.
        static readonly HashSet<string> Small = new HashSet<string> { "CollarFoot", "CollarWaist", "CollarHead", "FlueCollar", "RidgeCollar", "RoofCollar", "LanternCollar", "LampCollar", "LampRod", "LampVessel", "Pipe", "PipeJoint", "PipeBracket", "Valve" };

        // merge - the still pieces of one prefab's kit become one mesh per material and size class (SPEC-ARCH-TEMPLE-308 17): the same picture
        // in far fewer draws. Mesh assets: Kit/Merged/<prefab>_<class>_<material>.asset (rewritten by every dress).
        // OFF (user, 2026-10-08): measured -0.2 ... -0.6 ms toward the halls for +282 MB of mesh memory (99.6 -> 382.1 MB) - reverted, the
        // parts stay shared instances. Kept for a later look; with the switch off, dress also removes the merged mesh assets.
        const bool MergeOn = false;
        static int Merge(GameObject holder)
        {
            if (!MergeOn) return 0;
            var kit = holder.transform.Find("Kit"); Folder(Dir + "/Merged"); var groups = new Dictionary<string, (Material m, List<CombineInstance> list, List<GameObject> gone)>(); var inv = kit.worldToLocalMatrix;
            foreach (Transform t in kit)
            {
                if (Moving.Contains(t.name)) continue; var f = t.GetComponent<MeshFilter>(); var r = t.GetComponent<MeshRenderer>(); if (f == null || r == null || f.sharedMesh == null || r.sharedMaterial == null) continue;
                string key = (Small.Contains(t.name) ? "KitSmall_" : "KitLarge_") + r.sharedMaterial.name;
                if (!groups.TryGetValue(key, out var g)) groups[key] = g = (r.sharedMaterial, new List<CombineInstance>(), new List<GameObject>());
                g.list.Add(new CombineInstance { mesh = f.sharedMesh, subMeshIndex = 0, transform = inv * t.localToWorldMatrix }); g.gone.Add(t.gameObject);
            }
            foreach (var kv in groups)
            {
                var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = holder.name + "_" + kv.Key }; mesh.CombineMeshes(kv.Value.list.ToArray(), true, true); mesh.RecalculateBounds();
                string path = Dir + "/Merged/" + mesh.name + ".asset"; AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(mesh, path);
                var go = new GameObject(kv.Key); go.transform.SetParent(kit, false); go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = kv.Value.m;
                foreach (var old in kv.Value.gone) Object.DestroyImmediate(old);
            }
            return groups.Count;
        }

        static readonly string[] Halls = { "Kirim_Dae", "Ssang_Dae", "Geumsan_Mi", "Sun_Dae", "Sun_Bul", "Sun_Pal", "Sun_Won", "Sun_Toilet", "Sun_Kak", "Sun_Gate" };

        static string Dress()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Prefabs/Kit_part_firebox.prefab") == null || AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Prefabs/Kit_plain_pipe.prefab") == null) return "REFUSED run import first";
            var sb = new StringBuilder("KitDress308 dress\n"); var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            if (!MergeOn && AssetDatabase.IsValidFolder(Dir + "/Merged")) { AssetDatabase.DeleteAsset(Dir + "/Merged"); sb.Append("  merged meshes removed").Append((char)10); }
            void Save(GameObject holder) { int pieces = holder.transform.Find("Kit").childCount, merged = Merge(holder); PrefabUtility.SaveAsPrefabAsset(holder, TempleDir + "/" + holder.name + ".prefab"); sb.Append("  ").Append(holder.name).Append(": ").Append(pieces).Append(" pieces -> ").Append(holder.transform.Find("Kit").childCount).Append(" objects, ").Append(merged).Append(" merged meshes").Append((char)10); Object.DestroyImmediate(holder); }
            try
            {
                { var h = Holder("Temple308K_Lantern", scene, out var k); k.Lantern(Vector3.zero, 0f, 1.2f, 7f); Save(h); }
                { var h = Holder("Temple308K_SutraWheel", scene, out var k); SutraWheel(k, Vector3.zero); Save(h); }
                { var h = Holder("Temple308K_Boiler", scene, out var k); float vt = k.Boiler(Vector3.zero, 1.6f, 180f); k.Flue(new Vector3(0f, vt - .1f, 0f), vt + 5.5f, .3f, 2.3f); Save(h); }
                { var h = Holder("Temple308K_Engine", scene, out var k); k.Engine(Vector3.zero, Vector3.right, Vector3.back, 1f, false); Save(h); }
                foreach (string name in Halls)
                {
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(TempleDir + "/Temple308_" + name + ".prefab") == null) { sb.Append("  missing Temple308_").Append(name).Append('\n'); continue; }
                    var h = Holder("Temple308K_" + name, scene, out var k); Body(name, h.transform, out var all, out var B); h.transform.Find("Kit").SetAsLastSibling(); Hall(k, all, B, sb, name); Save(h);
                }
                { var h = Holder("Temple308K_Song_Jong", scene, out var k); Body("Song_Jong", h.transform, out var all, out var B); h.transform.Find("Kit").SetAsLastSibling(); BellPavilion(k, all, B, sb); Save(h); }
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
            return sb.ToString().TrimEnd();
        }

        // Geared sutra wheel: the case rides on the big gear; a pinion on an upright shaft and a flywheel on a cross shaft drive it.
        static void SutraWheel(Builder k, Vector3 at)
        {
            k.Base(at, 3.6f, .5f, 3.2f); k.Stack("part_collar", 1.5f, .5f, at + new Vector3(0f, .5f, 0f), 0f, "Bearing");
            float gz = 1.12f; k.Wheel("part_gear", 2.0f, at + new Vector3(0f, gz, 0f), Vector3.up, "BigGear");
            float px = 1.22f; k.Wheel("part_gear", .54f, at + new Vector3(px, gz, 0f), Vector3.up, "Pinion");
            k.Along("plain_shaft", at + new Vector3(px, .5f, 0f), at + new Vector3(px, gz + .15f, 0f), 1f, .8f, "PinionShaft", true);
            var hub = at + new Vector3(px, 1.2f, -.85f); k.Wheel("part_flywheel", 1.1f, hub, Vector3.forward, "Flywheel");
            k.Along("plain_shaft", hub + Vector3.back * .25f, at + new Vector3(px, 1.2f, .05f), 1f, .75f, "CrossShaft", true); k.Stand(hub + Vector3.back * .2f, at.y + .5f, Vector3.forward);
            float top = k.Stack("part_case", 1.5f, 1.6f, at + new Vector3(0f, gz + .2f, 0f), 0f, "SutraCase");
            k.Stack("part_canopy", 2.2f, 1.5f, at + new Vector3(0f, top - .1f, 0f), 0f, "SutraCap");
        }

        static void Hall(Builder k, Renderer[] all, Bounds B, StringBuilder sb, string name)
        {
            var ps = Pillars(all, out float foot); var roof = Verts(all.Where(r => Has(r.name, "Roof"))); float roofTop = roof.Count > 0 ? roof.Max(v => v.y) : B.max.y, roofLow = roof.Count > 0 ? roof.Min(v => v.y) : B.max.y - 2f;
            var W = BoundsOf(all.Where(r => (Has(r.name, "Wall") || Has(r.name, "Pillar")) && r.bounds.min.y < foot + 2.5f && r.bounds.size.y < 7f)) ?? B;
            var plinthB = BoundsOf(all.Where(r => Has(r.name, "kidan"))); float floor = plinthB != null && Mathf.Abs(plinthB.Value.max.y - foot) < .7f ? plinthB.Value.max.y : foot - .05f;
            var plinth = plinthB ?? new Bounds(new Vector3(W.center.x, floor / 2f, W.center.z), new Vector3(W.size.x + 2f, floor, W.size.z + 2f));
            var ground = ps.Where(p => p.c.y < foot + .6f).ToList(); bool walls = all.Any(r => Has(r.name, "Wall"));
            float g = Mathf.Clamp(W.size.x / 20f, .5f, 1.1f);
            sb.Append("    ").Append(name).Append(": pillars ").Append(ground.Count).Append('/').Append(ps.Count).Append(" floor ").Append(floor.ToString("F2")).Append(" wall ").Append(W.size.x.ToString("F1")).Append(" x ").Append(W.size.z.ToString("F1")).Append(" roof ").Append(roofLow.ToString("F1")).Append("..").Append(roofTop.ToString("F1")).Append(" g ").Append(g.ToString("F2"));
            if (ground.Count < 2) { sb.Append(" (no pillars)\n"); return; }
            float zf = ground.Max(p => p.c.z), zb = ground.Min(p => p.c.z); var front = ground.Where(p => p.c.z > zf - .5f).OrderBy(p => p.c.x).ToList(); var back = ground.Where(p => p.c.z < zb + .5f).OrderBy(p => p.c.x).ToList();
            float head = Mathf.Min(front.Min(p => p.top), foot + 4.2f), yp = head - .28f, r0 = front[0].r;
            // 1. lotus collars on the outer pillars: foot and waist
            foreach (var p in (zf - zb > 1f ? front.Concat(back) : front)) { float d = p.r * 2f; k.Stack("part_collar", d * 1.85f, d * .55f, new Vector3(p.c.x, p.c.y + .02f, p.c.z), 0f, "CollarFoot"); k.Stack("part_collar", d * 1.5f, d * .42f, new Vector3(p.c.x, p.c.y + (head - p.c.y) * .56f, p.c.z), 0f, "CollarWaist"); }
            // 2. flue caps on the ridge (one, two or three by its length)
            if (roof.Count > 0)
            {
                var crest = roof.Where(v => v.y > roofTop - .45f).ToList(); float x0 = crest.Min(v => v.x), x1 = crest.Max(v => v.x), zc = (crest.Min(v => v.z) + crest.Max(v => v.z)) / 2f, len = x1 - x0;
                foreach (float t in len > 14f ? new[] { .25f, .5f, .75f } : len > 6f ? new[] { .3f, .7f } : new[] { .5f })
                {
                    float x = Mathf.Lerp(x0, x1, t), y = SurfaceY(roof, x, zc, .35f, roofTop);
                    k.Stack("part_collar", 1.15f * g, .32f * g, new Vector3(x, y - .12f, zc), 0f, "RidgeCollar"); k.Along("plain_flue", new Vector3(x, y, zc), new Vector3(x, y + .6f * g, zc), 2f, .3f * g / .2f, "RidgeFlue", true); k.Stack("part_canopy", 2.0f * g, 1.8f * g, new Vector3(x, y + .5f * g, zc), 0f, "RidgeCap");
                }
            }
            // a gate (no walls): collars, a ridge cap and a lantern on each side - no engine
            if (!walls || W.size.x < 5f) { k.Lantern(new Vector3(W.min.x - 1.2f, B.min.y, zf + 1.2f), 0f, 1.1f); k.Lantern(new Vector3(W.max.x + 1.2f, B.min.y, zf + 1.2f), 0f, 1.1f); sb.Append(" gate\n"); return; }
            // 3. the machine end (+X): boiler outside the eave on its own granite base, flue beside the roof
            float gx = W.max.x + .78f * g, bxo = (roof.Count > 0 ? roof.Max(v => v.x) : B.max.x) + 1.25f * g; bool engine = W.size.z > 6.2f * g + 1.5f;
            float zB = engine ? W.min.z + 1.5f * g : W.center.z;
            k.Base(new Vector3(bxo - .4f * g, B.min.y, zB), 3.6f * g, floor - B.min.y, 3.0f * g);
            float vt = k.Boiler(new Vector3(bxo, floor, zB), 1.8f * g, 90f); k.Flue(new Vector3(bxo, vt - .1f, zB), Mathf.Min(roofTop + 1.4f * g, roofLow + 8f), .33f * g, 2.6f * g);
            // 4. on that end wall: steam cylinder, then flywheel and gear on one shaft (only where the wall is long enough)
            if (engine)
            {
                var under = new Vector3(gx, floor, zB + 5.0f * g); k.Engine(under, Vector3.right, Vector3.back, g, true);
                var cyl = under + Vector3.back * (2.5f * g) + Vector3.right * (.1f * g); float fy = vt - .9f * g;
                k.Pipe(.05f, new Vector3(bxo - .9f * g, fy, zB), new Vector3(cyl.x, fy, zB), new Vector3(cyl.x, fy, cyl.z), new Vector3(cyl.x, floor + .2f + 1.15f * g, cyl.z)); k.Valve(new Vector3(cyl.x, fy, (zB + cyl.z) / 2f), Vector3.forward);
            }
            // 5. double pipe run under the eave: down the far front pillar, along the front, round the machine end to the boiler, on along the back
            float zp = zf + r0 + .1f, zq = zb - r0 - .1f, xa = front[0].c.x - r0 - .12f, xc = W.max.x + .18f;
            k.Pipes(.045f, Vector3.back, new Vector3(xa, foot + .4f, zp), new Vector3(xa, yp, zp), new Vector3(xc, yp, zp)); k.Pipes(.045f, Vector3.left, new Vector3(xc, yp, zp), new Vector3(xc, yp, zB), new Vector3(bxo - .9f * g, yp, zB));
            if (zf - zb > 1f) { k.Pipes(.045f, Vector3.left, new Vector3(xc, yp, zB), new Vector3(xc, yp, zq)); k.Pipes(.045f, Vector3.forward, new Vector3(xc, yp, zq), new Vector3(xa, yp, zq), new Vector3(xa, foot + .4f, zq)); }
            k.Valve(new Vector3(xa, foot + 1.25f, zp), Vector3.up);
            // 6. steam lanterns on the two front corners of the plinth (on the ground before a small hall)
            if (plinth.size.z > W.size.z + 2.4f) { k.Lantern(new Vector3(plinth.min.x + 1.1f, floor, plinth.max.z - 1.1f), 0f, 1.35f * Mathf.Max(.75f, g)); k.Lantern(new Vector3(plinth.max.x - 1.1f, floor, plinth.max.z - 1.1f), 0f, 1.35f * Mathf.Max(.75f, g)); }
            else { k.Lantern(new Vector3(W.min.x + .6f, B.min.y, B.max.z + .4f), 0f, 1.1f); k.Lantern(new Vector3(W.max.x - .6f, B.min.y, B.max.z + .4f), 0f, 1.1f); }
            // 7. inside (large halls with inner pillars)
            var inner = ground.Where(p => p.c.z < zf - .6f && p.c.z > zb + .6f && p.c.x > front[0].c.x + .6f && p.c.x < front[front.Count - 1].c.x - .6f).ToList();
            if (g >= .8f && inner.Count >= 4 && W.size.z > 8f) { Interior(k, all, W, roof, inner, floor, foot, zf, zb, g, sb); sb.Append(" +interior"); }
            sb.Append('\n');
        }

        // Inside a large hall (D308-47): the centre front bay is opened; an engine shrine stands against the back wall with a flywheel engine
        // on each side; pipes run along the inner pillar rows; four lanterns give cover. The floor between the pillar rows stays clear.
        static void Interior(Builder k, Renderer[] all, Bounds W, List<Vector3> roof, List<Pillar> inner, float floor, float foot, float zf, float zb, float g, StringBuilder sb)
        {
            var marker = new GameObject("Interior"); marker.transform.SetParent(k.root, false); marker.transform.localPosition = new Vector3(W.center.x, floor, W.center.z);
            // the centre front bay opens (the wall panel is switched off, not removed)
            var panels = all.Where(r => Has(r.name, "Wall") && r.bounds.size.x > 1.5f && r.bounds.min.y < foot + 1.2f && r.bounds.size.y < 5f).ToList(); float wz = panels.Count > 0 ? panels.Max(r => r.bounds.center.z) : zf;
            var door = panels.Where(r => r.bounds.center.z > wz - .5f).OrderBy(r => Mathf.Abs(r.bounds.center.x - W.center.x)).FirstOrDefault();
            if (door != null) { door.gameObject.SetActive(false); sb.Append(" open ").Append(door.name); }
            // engine shrine against the back wall, its flue out through the roof
            float zs = zb + 1.9f * g, sx = W.center.x; k.Base(new Vector3(sx, floor, zs), 3.6f * g, .45f, 2.8f * g);
            float vt = k.Boiler(new Vector3(sx, floor + .45f, zs), 1.7f * g, 180f, true); float tiles = SurfaceY(roof, sx, zs, .6f, W.max.y + 2f);
            k.Stack("part_collar", 1.2f * g, .34f * g, new Vector3(sx, tiles - .05f, zs), 0f, "RoofCollar"); k.Flue(new Vector3(sx, vt - .1f, zs), tiles + 1.3f * g, .3f * g, 2.2f * g);
            k.Rail(new Vector3(sx - 2.4f * g, floor, zs + 2.0f * g), new Vector3(sx + 2.4f * g, floor, zs + 2.0f * g));
            // a flywheel engine on each side of the shrine, wheels facing the hall
            foreach (float s in new[] { -1f, 1f }) k.Engine(new Vector3(sx + s * 3.6f * g, floor, zs + .2f * g), Vector3.forward, new Vector3(s, 0f, 0f), g * .8f, false);
            // pipe runs along the inner pillar rows, a drop with a valve at each end, collars on the inner pillar feet
            foreach (var row in inner.GroupBy(p => Mathf.Round(p.c.z)).Where(r => r.Count() >= 2))
            {
                var list = row.OrderBy(p => p.c.x).ToList(); var a = list[0]; var b = list[list.Count - 1]; float y = Mathf.Min(list.Min(p => p.top) - .7f, foot + 5.2f), z = a.c.z + (a.c.z > W.center.z ? -1f : 1f) * (a.r + .1f);
                k.Pipes(.045f, new Vector3(0f, 0f, a.c.z > W.center.z ? 1f : -1f), new Vector3(a.c.x - a.r - .12f, foot + .4f, z), new Vector3(a.c.x - a.r - .12f, y, z), new Vector3(b.c.x + b.r + .12f, y, z), new Vector3(b.c.x + b.r + .12f, foot + .4f, z));
                k.Valve(new Vector3(a.c.x - a.r - .12f, foot + 1.25f, z), Vector3.up); k.Valve(new Vector3(b.c.x + b.r + .12f, foot + 1.25f, z), Vector3.up);
                foreach (var p in list) { float d = p.r * 2f; k.Stack("part_collar", d * 1.85f, d * .55f, new Vector3(p.c.x, p.c.y + .02f, p.c.z), 0f, "CollarFoot"); }
            }
            // four steam lanterns as cover, off the centre line
            foreach (float sxn in new[] { -1f, 1f }) foreach (float szn in new[] { -.05f, .78f }) k.Lantern(new Vector3(sx + sxn * W.size.x * .36f, floor, Mathf.Lerp(zb, zf, .5f + szn * .5f) - 1.2f), 0f, 1.2f, 9f);
            // two hanging lamps over the clear floor, from the height of the pipe runs
            float hang = Mathf.Min(inner.Min(p => p.top) - .3f, foot + 5.6f); foreach (float sxn in new[] { -1f, 1f }) k.HangingLamp(new Vector3(sx + sxn * W.size.x * .17f, hang, Mathf.Lerp(zb, zf, .55f)), foot + 3.1f, 1.3f);
        }

        // Built after Concepts/Buildings2/concept_Song_Jong.png: the pack's own bell stays; on the deck arm beside it a steam cylinder on a granite footing
        // drives the ram, flywheel and gear on one shaft behind it; the boiler stands on its own base outside that arm's eave; lotus collars on the deck pillars.
        static void BellPavilion(Builder k, Renderer[] all, Bounds B, StringBuilder sb)
        {
            var ps = Pillars(all, out float foot); var roof = Verts(all.Where(r => Has(r.name, "Roof")));
            var bell = BoundsOf(all.Where(r => Has(r.name, "_Bell") && !Has(r.name, "Tower"))); var deckB = BoundsOf(all.Where(r => Has(r.name, "Floor"))); var plinth = BoundsOf(all.Where(r => Has(r.name, "kidan")));
            if (bell == null || deckB == null) { sb.Append("    pavilion: no bell or deck found").Append((char)10); return; }
            float deck = deckB.Value.max.y, beam = ps.Count > 0 ? ps.Min(p => p.top) : deck + 2f, ground = plinth != null ? plinth.Value.max.y : B.min.y, roofTop = roof.Count > 0 ? roof.Max(v => v.y) : beam + 4f; var c = bell.Value.center;
            sb.Append("    Song_Jong: pillars ").Append(ps.Count).Append(" deck ").Append(deck.ToString("F2")).Append(" beam ").Append(beam.ToString("F2")).Append((char)10);
            // 1. steam cylinder on a granite footing, its ram toward the bell's striking point
            float hy = Mathf.Max(bell.Value.min.y + bell.Value.size.y * .22f, deck + .2f + .45f), bx = bell.Value.min.x, len = 1.3f, cx = bx - .7f - len / 2f;
            k.Footing(new Vector3(cx, deck, c.z), len + .3f, .2f, .8f); k.Lying("part_cylinder", len, new Vector3(cx, hy, c.z), Vector3.right, "Cylinder");
            k.Along("plain_shaft", new Vector3(cx + len / 2f - .1f, hy, c.z), new Vector3(bx - .5f, hy, c.z), 1f, .6f, "RamRod", true); k.Along("plain_ram", new Vector3(bx - .62f, hy, c.z), new Vector3(bx - .05f, hy, c.z), 1f, 1f, "Ram", true);
            // 2. flywheel and gear on one shaft behind the cylinder, on two cast pedestals
            float wx = cx - len / 2f - .75f, wy = deck + .98f; var hub = new Vector3(wx, wy, c.z);
            k.Wheel("part_flywheel", 1.5f, hub + Vector3.forward * .42f, Vector3.forward, "Flywheel"); k.Wheel("part_gear", 1.7f, hub + Vector3.back * .1f, Vector3.forward, "Gear");
            k.Along("plain_shaft", hub + Vector3.back * .85f, hub + Vector3.forward * .95f, 1f, .85f, "Shaft", true); k.Stand(hub + Vector3.back * .6f, deck, Vector3.forward); k.Stand(hub + Vector3.forward * .82f, deck, Vector3.forward);
            k.Along("plain_rod", new Vector3(cx - len / 2f + .1f, hy, c.z + .22f), hub + new Vector3(.4f, .3f, .22f), 1f, 1f, "ConnectingRod", true);
            // 3. boiler on its own base outside the end of that arm, flue beside the eave to a canopy cap, steam pipes over the rail to the cylinder
            float ax = (roof.Count > 0 ? roof.Min(v => v.x) : B.min.x) - .95f, az = c.z + 1.3f;
            k.Base(new Vector3(ax + .4f, B.min.y, az), 3.2f, ground - B.min.y, 2.5f);
            float vt = k.Boiler(new Vector3(ax, ground, az), 1.5f, -90f); k.Flue(new Vector3(ax, vt - .1f, az), Mathf.Lerp(beam, roofTop, .9f), .24f, 2.0f);
            float py = beam - .25f;
            k.Pipes(.045f, Vector3.zero, new Vector3(ax + .75f, vt - .7f, az - .6f), new Vector3(ax + .75f, py, az - .6f), new Vector3(ax + .75f, py, c.z), new Vector3(cx, py, c.z), new Vector3(cx, hy + .35f, c.z)); k.Valve(new Vector3(cx, hy + .9f, c.z), Vector3.up);
            // 4. lotus collars at the foot and the head of every deck pillar
            foreach (var p in ps) { float d = p.r * 2f; k.Stack("part_collar", d * 1.9f, d * .55f, new Vector3(p.c.x, p.c.y + .02f, p.c.z), 0f, "CollarFoot"); k.Stack("part_collar", d * 1.6f, d * .45f, new Vector3(p.c.x, p.top - d * .9f, p.c.z), 0f, "CollarHead"); }
        }
    }
}
