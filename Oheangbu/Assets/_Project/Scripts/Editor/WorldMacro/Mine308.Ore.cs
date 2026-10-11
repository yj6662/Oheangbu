using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 the first mine: traces of ore being cut and used (D308-64). Step `ore` of Mine308 (ore:apply | ore:revert), numbers in mine308.json.
    //   under every wall seam that is drawn: stubs of cut ore beside it and chips on the floor below
    //   at the listed spots: a heap of sorted ore with a basket | a stone mortar with its powder stain | a heap of burnt-out slag on soot
    // Nothing here glows: the seams already in the mine are the only ore light. Drawn only, except one box inside each heap and the mortar.
    public static partial class Mine308
    {
        [Serializable] sealed class OreSpot { public string kind = ""; public float x, z; }
        [Serializable] sealed class OreCfg { public int seed = 308; public string oreSeamRoot = "", stoneMaterial = "", timberMaterial = ""; public float[] oreColor = Array.Empty<float>(), slagColor = Array.Empty<float>(); public float oreMatte = .5f, powderMatte = .7f, floorGuess = 170f; public OreSpot[] oreSpots = Array.Empty<OreSpot>(); }

        static string Ore(Transform root, bool apply)
        {
            CleanMeshes("Mine308_Ore"); var home = LookHome(root, "Ore"); if (!apply) { Object.DestroyImmediate(home.gameObject); return "ore traces removed"; }
            var cfg = JsonUtility.FromJson<OreCfg>(File.ReadAllText(Path.Combine(StageDir, "mine308.json"))); if (cfg.oreColor.Length != 3 || cfg.slagColor.Length != 3) throw new Exception("mine308.json needs oreColor and slagColor");
            Folder(AssetDir + "/Materials");
            Material Plain(string name, Color colour, float smooth)
            {
                string path = AssetDir + "/Materials/" + name + ".mat"; var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name }; AssetDatabase.CreateAsset(mat, path); }
                mat.SetColor("_BaseColor", colour); mat.SetFloat("_Smoothness", smooth); EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); return mat;
            }
            var ore = new Color(cfg.oreColor[0], cfg.oreColor[1], cfg.oreColor[2], 1f); var oreMat = Plain("Mine308_OreChip", ore * cfg.oreMatte, .35f); var powderMat = Plain("Mine308_OrePowder", ore * cfg.powderMatte, 0f);
            var slagMat = Plain("Mine308_Slag", new Color(cfg.slagColor[0], cfg.slagColor[1], cfg.slagColor[2], 1f), .1f); var sootMat = Plain("Mine308_Soot", new Color(.03f, .03f, .03f, 1f), 0f);
            var stoneMat = AssetDatabase.LoadAssetAtPath<Material>(cfg.stoneMaterial); var woodMat = AssetDatabase.LoadAssetAtPath<Material>(cfg.timberMaterial); if (stoneMat == null || woodMat == null) throw new Exception("stone or timber material missing");
            Physics.SyncTransforms(); var rng = new System.Random(cfg.seed * 23 + 9); float R() => (float)rng.NextDouble();
            var lists = new Dictionary<Material, (List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tri)>();
            (List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tri) L(Material m) { if (!lists.TryGetValue(m, out var l)) lists[m] = l = (new List<Vector3>(), new List<Vector3>(), new List<Vector2>(), new List<int>()); return l; }
            void St(Material m, Vector3 at, float size) { var l = L(m); Stone(l.v, l.n, l.uv, l.tri, at, size, rng); }
            void Disc(Material m, Vector3 centre, float radius)      // a flat stain on the floor
            {
                var l = L(m); int k0 = l.v.Count; const int sides = 10; l.v.Add(centre); l.n.Add(Vector3.up); l.uv.Add(Vector2.zero);
                for (int s = 0; s < sides; s++) { float a = s * Mathf.PI * 2f / sides; float r = radius * (.7f + R() * .5f); l.v.Add(centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r)); l.n.Add(Vector3.up); l.uv.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a))); }
                for (int s = 0; s < sides; s++) { l.tri.Add(k0); l.tri.Add(k0 + 1 + (s + 1) % sides); l.tri.Add(k0 + 1 + s); }
            }
            void Solid(string name, Vector3 centre, Vector3 size) { var g = new GameObject(name); g.transform.SetParent(home, false); g.transform.position = centre; g.AddComponent<BoxCollider>().size = size; }
            int seams = 0, heaps = 0, mortars = 0, slags = 0, missed = 0; var seamRoot = Resolve(root, cfg.oreSeamRoot);
            // the seam strips of #297 hang outside the cave (they are switched off by `marks`); the ore LIGHTS are in it. Each lit one gets what
            // it shines from: a stretch of living vein on the nearest wall (the only ore that glows), cut stubs beside it, chips under it.
            var veinPath = AssetDir + "/Materials/Mine308_Ore_Vein.mat"; var veinMat = AssetDatabase.LoadAssetAtPath<Material>(veinPath);
            if (veinMat == null) { veinMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Mine308_Ore_Vein" }; AssetDatabase.CreateAsset(veinMat, veinPath); }
            veinMat.SetColor("_BaseColor", ore * .85f); EditorUtility.SetDirty(veinMat); AssetDatabase.SaveAssetIfDirty(veinMat);
            if (seamRoot != null)
                foreach (var glow in seamRoot.GetComponentsInChildren<Light>(false).Where(x => x.enabled))
                {
                    var p0 = glow.transform.position; RaycastHit wall = default; float best = 7f;
                    for (int ray = 0; ray < 12; ray++)
                    {
                        float a = ray * Mathf.PI / 6f; var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        foreach (var h in Physics.RaycastAll(p0, dir, best, ~0, QueryTriggerInteraction.Ignore)) if (!h.transform.IsChildOf(home) && h.distance < best) { best = h.distance; wall = h; }
                    }
                    if (wall.collider == null) continue; seams++; var n0 = wall.normal; var side = Vector3.Cross(Vector3.up, n0).normalized;
                    for (int i = 0; i < 14; i++) St(veinMat, wall.point + side * ((R() - .5f) * 1.8f) + Vector3.up * ((R() - .5f) * .9f) + n0 * .03f, .14f + R() * .18f);
                    for (int i = 0; i < 10; i++) St(oreMat, wall.point + side * ((R() - .5f) * 3.2f) + Vector3.up * ((R() - .5f) * 1.4f) + n0 * .03f, .07f + R() * .12f);
                    if (!GroundUnder(wall.point + n0 * .7f + Vector3.up * .3f, 5f, home, null, out var hit)) continue;
                    for (int i = 0; i < 16; i++) { float a = R() * Mathf.PI * 2f, d = R() * 1.3f; St(oreMat, hit.point + new Vector3(Mathf.Cos(a) * d, .04f, Mathf.Sin(a) * d), .05f + R() * .11f); }
                }
            foreach (var s in cfg.oreSpots)
            {
                if (!GroundUnder(new Vector3(s.x, cfg.floorGuess, s.z), 4f, home, null, out var hit)) { missed++; continue; }
                var p = hit.point;
                if (s.kind == "heap")
                {
                    for (int i = 0; i < 34; i++) { float a = R() * Mathf.PI * 2f, d = .75f * Mathf.Sqrt(R()), sz = .14f + R() * .2f; St(oreMat, p + new Vector3(Mathf.Cos(a) * d, .45f * (1f - d / .75f) * R() + sz * .3f, Mathf.Sin(a) * d), sz); }
                    var basket = p + new Vector3(1f + R() * .2f, 0f, (R() - .5f) * .8f); var wl = L(woodMat); Log(wl.v, wl.n, wl.uv, wl.tri, basket + Vector3.up * .44f, basket, .34f, rng);
                    for (int i = 0; i < 6; i++) St(oreMat, basket + new Vector3((R() - .5f) * .34f, .44f + R() * .08f, (R() - .5f) * .34f), .12f + R() * .08f);
                    Solid("Heap_" + heaps, p + Vector3.up * .2f, new Vector3(1f, .4f, 1f)); heaps++;
                }
                else if (s.kind == "mortar")
                {
                    var sl = L(stoneMat); Log(sl.v, sl.n, sl.uv, sl.tri, p, p + Vector3.up * .44f, .32f, rng); var wl = L(woodMat); Log(wl.v, wl.n, wl.uv, wl.tri, p + new Vector3(.5f, .95f, .25f), p + new Vector3(.05f, .3f, 0f), .05f, rng);
                    Disc(powderMat, p + new Vector3((R() - .5f) * .4f, .02f, (R() - .5f) * .4f), .95f); for (int i = 0; i < 8; i++) St(oreMat, p + new Vector3((R() - .5f) * 1.6f, .04f, (R() - .5f) * 1.6f), .06f + R() * .08f);
                    Solid("Mortar_" + mortars, p + Vector3.up * .22f, new Vector3(.6f, .44f, .6f)); mortars++;
                }
                else if (s.kind == "slag")
                {
                    Disc(sootMat, p + Vector3.up * .02f, 1.2f);
                    for (int i = 0; i < 26; i++) { float a = R() * Mathf.PI * 2f, d = .65f * Mathf.Sqrt(R()), sz = .08f + R() * .17f; St(slagMat, p + new Vector3(Mathf.Cos(a) * d, .25f * (1f - d / .65f) * R() + sz * .3f, Mathf.Sin(a) * d), sz); }
                    slags++;
                }
            }
            long verts = 0; int k = 0;
            foreach (var kv in lists)
            {
                if (kv.Value.v.Count == 0) continue; var mesh = new Mesh { indexFormat = kv.Value.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 }; mesh.SetVertices(kv.Value.v); mesh.SetNormals(kv.Value.n); mesh.SetUVs(0, kv.Value.uv); mesh.SetTriangles(kv.Value.tri, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents();
                SaveMesh(mesh, "Mine308_Ore_" + k++); var g = new GameObject(kv.Key.name + "_" + mesh.name); g.transform.SetParent(home, false); g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterial = kv.Key; verts += kv.Value.v.Count;
            }
            return "seams dressed " + seams + ", ore heaps " + heaps + ", mortars " + mortars + ", slag heaps " + slags + ", spots without ground " + missed + ", vertices " + verts;
        }
    }
}
