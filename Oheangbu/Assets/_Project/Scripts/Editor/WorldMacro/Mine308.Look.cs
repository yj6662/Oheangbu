using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 the first mine, look pass (D308-60). Numbers: Tools/Unity/Stage308_mine/mine308.json. Ledger: Tools/Unity/Stage308_mine/Ledger/look308.json.
    //   look:apply | look:revert     every step below, in order (each step is also a command of its own, with :apply or :revert):
    //     rubble   the pack walls (Mine306_Interior/Chunks/Packwall_*) read as cut masonry: rough stones are set over every face of each wall
    //              (drawn only - the wall's own collider stays what blocks) and leaning props hold them; the wall mesh behind takes the dark cave rock
    //     rock     the cave rock material is copied for the mine and deepened (relief, contrast, darker fill): every renderer of the mine that used it
    //     marks    the black herringbone "pick mark" strips are switched off
    //     lights   lanterns along the way and the ore veins are lit by the list in the config (attraction lights only: lanterns and ore)
    //     rails | floor | warp | timber | lanterns | falls     Mine308.Rough.cs
    //     ore      traces of ore being cut and used (D308-64)     Mine308.Ore.cs
    // Only the play scene and this tool's own assets (Assets/_Project/Art/World/Mine308) are written; the scene is backed up first.
    public static partial class Mine308
    {
        const string AssetDir = "Assets/_Project/Art/World/Mine308", LookRoot = "Mine308_Look";
        [Serializable] sealed class LightRow { public string path = ""; public float intensity = -1f, range = -1f; public bool on = true; }
        [Serializable] sealed class LookCfg
        {
            public int seed = 308; public string rockMaterial = "", stoneMaterial = "", timberMaterial = "", wallPrefix = ""; public float stonesPerM2 = 5f, stoneMin = 0.3f, stoneMax = 0.8f, topScale = 0.6f, embed = 0.3f, propEveryM2 = 14f;
            public float rockHeight = 0.1f, rockNormal = 1.6f, rockScale = 0.18f, rockAmbient = 0.16f; public float[] rockColor = Array.Empty<float>(); public string[] marks = Array.Empty<string>(), oreRoots = Array.Empty<string>(), jitter = Array.Empty<string>();
            public LightRow[] lights = Array.Empty<LightRow>();
        }
        [Serializable] sealed class Swap { public string path = "", before = ""; }
        [Serializable] sealed class LightState { public string path = ""; public bool enabled, active; public float intensity, range; }
        [Serializable] sealed class Ledger308 { public List<Swap> wallMaterials = new List<Swap>(), rockMaterials = new List<Swap>(), meshes = new List<Swap>(); public List<string> marks = new List<string>(), activated = new List<string>(); public List<LightState> lights = new List<LightState>(); public bool rubble; }
        static string LedgerFile => Path.Combine(StageDir, "Ledger", "look308.json");
        static LookCfg ReadCfg() => JsonUtility.FromJson<LookCfg>(File.ReadAllText(Path.Combine(StageDir, "mine308.json")));
        static Ledger308 ReadLedger() => File.Exists(LedgerFile) ? JsonUtility.FromJson<Ledger308>(File.ReadAllText(LedgerFile)) : new Ledger308();
        static void WriteLedger(Ledger308 l) { Directory.CreateDirectory(Path.GetDirectoryName(LedgerFile)); File.WriteAllText(LedgerFile, JsonUtility.ToJson(l, true)); }
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        static Transform Resolve(Transform root, string path) { var t = root.Find(path); return t; }

        static partial void ExtraCommand(string c, ref string answer)
        {
            string[] steps = { "rubble", "rock", "marks", "lights", "rails", "floor", "warp", "timber", "lanterns", "falls", "ore" }; bool revert = c.EndsWith(":revert", StringComparison.Ordinal); string verb = revert ? c.Substring(0, c.Length - 7) : c.EndsWith(":apply", StringComparison.Ordinal) ? c.Substring(0, c.Length - 6) : c;
            if (verb != "look" && !steps.Contains(verb)) return;
            var scene = PlayScene(true); var root = MineRoot(scene).transform; var cfg = ReadCfg(); var led = ReadLedger();
            string dir = Path.Combine(StageDir, "Backup", DateTime.Now.ToString("yyyyMMddTHHmmss", Inv) + "_" + c.Replace(':', '_')); Directory.CreateDirectory(dir);
            File.Copy(Path.GetFullPath(scene.path), Path.Combine(dir, Path.GetFileName(scene.path)), true);
            var sb = new StringBuilder("Mine308 " + c + ", backup " + dir);
            foreach (string step in verb == "look" ? (revert ? steps.Reverse().ToArray() : steps) : new[] { verb })
            {
                switch (step)
                {
                    case "rubble": sb.Append("\n  rubble: ").Append(revert ? RubbleRevert(root, led) : Rubble(root, cfg, led)); break;
                    case "rock": sb.Append("\n  rock: ").Append(revert ? RockRevert(root, led) : Rock(root, cfg, led)); break;
                    case "marks": sb.Append("\n  marks: ").Append(Marks(root, cfg, led, !revert)); break;
                    case "lights": sb.Append("\n  lights: ").Append(revert ? LightsRevert(root, led) : Lights(root, cfg, led)); break;
                    default: sb.Append("\n  ").Append(step).Append(": ").Append(Rough(step, revert, root, led)); break;      // Mine308.Rough.cs
                }
            }
            WriteLedger(led); EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) throw new Exception("the play scene was not saved");
            answer = sb.ToString();
        }

        // ---------------------------------------------------------------- rubble
        static readonly Vector3[] Ico = BuildIco();
        static readonly int[] IcoTri = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
        static Vector3[] BuildIco()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f; var v = new[] { new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0), new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t), new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) };
            return v.Select(x => x.normalized).ToArray();
        }
        static void Stone(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tri, Vector3 centre, float size, System.Random rng)
        {
            float R() => (float)rng.NextDouble(); var rot = Quaternion.Euler(R() * 360f, R() * 360f, R() * 360f); var sc = new Vector3(.75f + R() * .5f, .5f + R() * .45f, .75f + R() * .5f) * size * .5f;
            var p = new Vector3[12]; for (int i = 0; i < 12; i++) p[i] = centre + rot * Vector3.Scale(Ico[i] * (.82f + R() * .36f), sc);
            // one shared normal per corner (pointing out of the stone): the ink outline pass then draws the stone's silhouette, not every facet
            int k0 = v.Count; for (int i = 0; i < 12; i++) { v.Add(p[i]); n.Add((p[i] - centre).normalized); uv.Add(new Vector2(p[i].x + p[i].y, p[i].z) * .5f); }
            for (int t = 0; t < IcoTri.Length; t++) tri.Add(k0 + IcoTri[t]);
        }
        static void Log(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tri, Vector3 a, Vector3 b, float r, System.Random rng)
        {
            var axis = (b - a).normalized; var u = Vector3.Cross(axis, Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.right).normalized; var w = Vector3.Cross(axis, u); const int sides = 7; float ph = (float)rng.NextDouble() * 6f;
            for (int s = 0; s < sides; s++)
            {
                float a0 = ph + s * Mathf.PI * 2f / sides, a1 = ph + (s + 1) * Mathf.PI * 2f / sides; Vector3 d0 = u * Mathf.Cos(a0) + w * Mathf.Sin(a0), d1 = u * Mathf.Cos(a1) + w * Mathf.Sin(a1), nn = (d0 + d1).normalized; int k = v.Count;
                v.Add(a + d0 * r); v.Add(a + d1 * r); v.Add(b + d1 * r * .85f); v.Add(b + d0 * r * .85f); for (int q = 0; q < 4; q++) n.Add(nn);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(.3f, 0)); uv.Add(new Vector2(.3f, 2)); uv.Add(new Vector2(0, 2)); tri.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 });
            }
        }
        static string Rubble(Transform root, LookCfg cfg, Ledger308 led)
        {
            if (led.rubble) return "already applied (rubble:revert first)";
            var stoneMat = AssetDatabase.LoadAssetAtPath<Material>(cfg.stoneMaterial); var backMat = AssetDatabase.LoadAssetAtPath<Material>(cfg.rockMaterial); var woodMat = AssetDatabase.LoadAssetAtPath<Material>(cfg.timberMaterial);
            if (stoneMat == null || backMat == null || woodMat == null) throw new Exception("config material missing (stone / rock / timber)");
            var old = root.Find(LookRoot + "/Rubble"); if (old != null) Object.DestroyImmediate(old.gameObject);
            var look = root.Find(LookRoot); if (look == null) { look = new GameObject(LookRoot).transform; look.SetParent(root, false); look.position = Vector3.zero; look.rotation = Quaternion.identity; }
            var home = new GameObject("Rubble").transform; home.SetParent(look, false); Folder(AssetDir + "/Meshes");
            foreach (string guid in AssetDatabase.FindAssets("Mine308_Rubble t:Mesh", new[] { AssetDir + "/Meshes" })) AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
            int walls = 0, stones = 0, props = 0; long verts = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null && f.name.StartsWith(cfg.wallPrefix, StringComparison.Ordinal) && f.name.EndsWith("_LOD0", StringComparison.Ordinal)).OrderBy(f => f.name))
            {
                var rng = new System.Random(cfg.seed * 7919 + mf.name.GetHashCode()); var src = mf.sharedMesh; var sv = src.vertices; var st = src.triangles; var m = mf.transform.localToWorldMatrix;
                var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>(); var pv = new List<Vector3>(); var pn = new List<Vector3>(); var puv = new List<Vector2>(); var ptri = new List<int>(); float area = 0f; float carry = 0f, pcarry = 0f;
                for (int t = 0; t < st.Length; t += 3)
                {
                    Vector3 a = m.MultiplyPoint3x4(sv[st[t]]), b = m.MultiplyPoint3x4(sv[st[t + 1]]), c = m.MultiplyPoint3x4(sv[st[t + 2]]); var cr = Vector3.Cross(b - a, c - a); float ar = cr.magnitude * .5f; if (ar < 1e-5f) continue; var nn = cr.normalized; area += ar;
                    if (nn.y < -.5f) continue;      // nothing on the underside
                    carry += ar * cfg.stonesPerM2;
                    while (carry >= 1f)
                    {
                        carry -= 1f; float r1 = Mathf.Sqrt((float)rng.NextDouble()), r2 = (float)rng.NextDouble(); var p = a * (1 - r1) + b * (r1 * (1 - r2)) + c * (r1 * r2);
                        float size = Mathf.Lerp(cfg.stoneMin, cfg.stoneMax, Mathf.Pow((float)rng.NextDouble(), 1.6f)) * (nn.y > .7f ? cfg.topScale : 1f);
                        Stone(v, n, uv, tri, p - nn * size * cfg.embed + Vector3.down * (nn.y > .7f ? 0f : (float)rng.NextDouble() * .05f), size, rng); stones++;
                    }
                    if (Mathf.Abs(nn.y) < .35f)
                    {
                        pcarry += ar / cfg.propEveryM2;
                        if (pcarry >= 1f)
                        {
                            pcarry -= 1f; var p = (a + b + c) / 3f; var flat = new Vector3(nn.x, 0f, nn.z).normalized; var along = Vector3.Cross(Vector3.up, flat); float low = Mathf.Min(a.y, Mathf.Min(b.y, c.y)), high = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                            var foot = new Vector3(p.x, low - .15f, p.z) + flat * (.55f + (float)rng.NextDouble() * .3f) + along * ((float)rng.NextDouble() - .5f); var head = new Vector3(p.x, high + .1f + (float)rng.NextDouble() * .5f, p.z) + flat * .12f + along * ((float)rng.NextDouble() - .5f) * .4f;
                            Log(pv, pn, puv, ptri, foot, head, .07f + (float)rng.NextDouble() * .05f, rng); props++;
                        }
                    }
                }
                if (v.Count == 0) continue;
                GameObject Make(string name, List<Vector3> vv, List<Vector3> nn2, List<Vector2> uu, List<int> tt, Material mat)
                {
                    var mesh = new Mesh { name = name, indexFormat = vv.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 }; mesh.SetVertices(vv); mesh.SetNormals(nn2); mesh.SetUVs(0, uu); mesh.SetTriangles(tt, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents();
                    AssetDatabase.CreateAsset(mesh, AssetDir + "/Meshes/" + name + ".asset"); AssetDatabase.SaveAssetIfDirty(mesh);
                    var g = new GameObject(name); g.transform.SetParent(home, false); g.AddComponent<MeshFilter>().sharedMesh = mesh; var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.On; verts += vv.Count; return g;
                }
                string key = mf.transform.parent != null ? mf.transform.parent.name : mf.name; Make("Mine308_Rubble_" + key, v, n, uv, tri, stoneMat); if (pv.Count > 0) Make("Mine308_RubbleProps_" + key, pv, pn, puv, ptri, woodMat);
                foreach (var r in mf.transform.parent.GetComponentsInChildren<MeshRenderer>(true))      // the wall behind the stones: dark cave rock instead of cut blocks
                {
                    string path = PathOf(r.transform, root); if (Resolve(root, path) != r.transform) continue;
                    led.wallMaterials.Add(new Swap { path = path, before = AssetDatabase.GetAssetPath(r.sharedMaterial) }); r.sharedMaterial = backMat;
                }
                walls++;
            }
            led.rubble = true; return walls + " walls, " + stones + " stones, " + props + " props, vertices " + verts;
        }
        static string RubbleRevert(Transform root, Ledger308 led)
        {
            int n = 0; foreach (var s in led.wallMaterials) { var t = Resolve(root, s.path); var r = t != null ? t.GetComponent<MeshRenderer>() : null; var mat = AssetDatabase.LoadAssetAtPath<Material>(s.before); if (r != null && mat != null) { r.sharedMaterial = mat; n++; } }
            led.wallMaterials.Clear(); led.rubble = false; var old = root.Find(LookRoot + "/Rubble"); if (old != null) Object.DestroyImmediate(old.gameObject); return n + " wall materials restored, stones removed";
        }

        // ---------------------------------------------------------------- rock
        static string Rock(Transform root, LookCfg cfg, Ledger308 led)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(cfg.rockMaterial); if (source == null) throw new Exception("rock material missing: " + cfg.rockMaterial);
            Folder(AssetDir + "/Materials"); string path = AssetDir + "/Materials/Mine308_Rock.mat"; var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(source) { name = "Mine308_Rock" }; AssetDatabase.CreateAsset(mat, path); } else mat.CopyPropertiesFromMaterial(source);
            mat.SetFloat("_Height", cfg.rockHeight); mat.SetFloat("_NormalStrength", cfg.rockNormal); mat.SetFloat("_Scale", cfg.rockScale); mat.SetFloat("_Ambient", cfg.rockAmbient);
            if (cfg.rockColor.Length == 3) mat.SetColor("_BaseColor", new Color(cfg.rockColor[0], cfg.rockColor[1], cfg.rockColor[2], 1f));
            EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); int n = 0, skipped = 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.sharedMaterials.Length != 1 || r.sharedMaterial != source) continue; string p = PathOf(r.transform, root);
                if (Resolve(root, p) != r.transform) { skipped++; continue; }
                if (!led.rockMaterials.Any(s => s.path == p)) led.rockMaterials.Add(new Swap { path = p, before = cfg.rockMaterial }); r.sharedMaterial = mat; n++;
            }
            return n + " renderers on " + path + " (ambiguous paths left on the shared material: " + skipped + ")";
        }
        static string RockRevert(Transform root, Ledger308 led)
        {
            int n = 0; foreach (var s in led.rockMaterials) { var t = Resolve(root, s.path); var r = t != null ? t.GetComponent<MeshRenderer>() : null; var mat = AssetDatabase.LoadAssetAtPath<Material>(s.before); if (r != null && mat != null) { r.sharedMaterial = mat; n++; } }
            led.rockMaterials.Clear(); return n + " renderers back on the shared rock";
        }

        // ---------------------------------------------------------------- marks
        static string Marks(Transform root, LookCfg cfg, Ledger308 led, bool hide)
        {
            int n = 0;
            foreach (string path in hide ? cfg.marks : led.marks.ToArray())
            {
                var t = Resolve(root, path); var r = t != null ? t.GetComponent<Renderer>() : null; if (r == null) continue;
                if (hide && r.enabled) { r.enabled = false; led.marks.Add(path); n++; } else if (!hide) { r.enabled = true; n++; }
            }
            if (!hide) led.marks.Clear(); return n + (hide ? " renderers off" : " renderers on");
        }

        // ---------------------------------------------------------------- lights
        static string Lights(Transform root, LookCfg cfg, Ledger308 led)
        {
            if (led.lights.Count > 0 || led.activated.Count > 0) return "already applied (lights:revert first)";
            foreach (string path in cfg.oreRoots) { var t = Resolve(root, path); if (t != null && !t.gameObject.activeSelf) { t.gameObject.SetActive(true); led.activated.Add(path); } }
            int on = 0, off = 0, missing = 0;
            foreach (var row in cfg.lights)
            {
                var t = Resolve(root, row.path); var l = t != null ? t.GetComponent<Light>() : null; if (l == null) { missing++; continue; }
                led.lights.Add(new LightState { path = row.path, enabled = l.enabled, active = t.gameObject.activeSelf, intensity = l.intensity, range = l.range });
                l.enabled = row.on; if (row.intensity >= 0f) l.intensity = row.intensity; if (row.range >= 0f) l.range = row.range; if (row.on) on++; else off++;
            }
            return on + " on, " + off + " off, missing " + missing + ", ore roots activated " + led.activated.Count;
        }
        static string LightsRevert(Transform root, Ledger308 led)
        {
            int n = 0; foreach (var s in led.lights) { var t = Resolve(root, s.path); var l = t != null ? t.GetComponent<Light>() : null; if (l == null) continue; l.enabled = s.enabled; l.intensity = s.intensity; l.range = s.range; n++; }
            foreach (string path in led.activated) { var t = Resolve(root, path); if (t != null) t.gameObject.SetActive(false); }
            led.lights.Clear(); led.activated.Clear(); return n + " lights restored";
        }
    }
}
