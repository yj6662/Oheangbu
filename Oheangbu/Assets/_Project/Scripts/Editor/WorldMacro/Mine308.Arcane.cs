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
    // #308 the first mine: the ore-craft of the capital (D308-64 / D308-79) as what an ABANDONED mine left behind (D308-60). Step `look:arcane` of Mine308.
    //   look:arcane:apply    seat every piece of the plan with rays (floor = the cave floor collider, wall = rock / pack wall colliders AND the rubble drawn over the
    //                        pack walls), drop what would stand in something, merge each section into one mesh per LOD (LODGroup), write the ledger, save the scene
    //   look:arcane:revert   remove exactly what the ledger lists (the Arcane home, its meshes, the materials this step created), save the scene
    //   look:arcane:status   read only: what is applied
    //   look:arcane:check    read only: counts, lit count, what glows and how bright, corridor / boss room / interior cell, colliders, lights, budget
    // Plan: Tools/Unity/Stage308_minearcane/mine_arcane308.json (Tools/Unity/Stage308_minearcane/mine_arcane_plan.py), kit meshes mine_arcane308_kit.json
    // (Tools/Art/arcane308_mine_kit.py). Ledger: Tools/Unity/Stage308_minearcane/Ledger/arcane308.json. Assets: Assets/_Project/Art/World/Mine308/Arcane.
    // Adds NO collider, NO Light, NO static flag. The only thing that glows is the living ore egg (material key `egglit` = the mine's own vein material).
    // Runs AFTER floor / warp / rubble (it measures them): re-run revert + apply when one of those is applied again.
    public static partial class Mine308
    {
        const string ArcDir = AssetDir + "/Arcane", ArcHomeName = "Arcane", ArcPrefix = "Mine308_Arcane_";
        static string ArcStage => Path.Combine(RepoRoot, "Tools", "Unity", "Stage308_minearcane");
        static string ArcPlanFile => Path.Combine(ArcStage, "mine_arcane308.json");
        static string ArcLedgerFile => Path.Combine(ArcStage, "Ledger", "arcane308.json");

        [Serializable] sealed class ArcSub { public string mat = ""; public float[] v = Array.Empty<float>(), n = Array.Empty<float>(); public int[] t = Array.Empty<int>(); }
        [Serializable] sealed class ArcLod { public List<ArcSub> subs = new List<ArcSub>(); }
        [Serializable] sealed class ArcKitObj { public string id = "", seat = "floor"; public int lit; public List<ArcLod> lods = new List<ArcLod>(); }
        [Serializable] sealed class ArcKit { public string version = ""; public List<ArcKitObj> kit = new List<ArcKitObj>(); }
        [Serializable] sealed class ArcPiece { public string k = "", seat = "floor", variant = "", note = ""; public float x, z, dx, dz, mountY = .5f, off, outw, half, yawOff; public int lit; public float[] expect = Array.Empty<float>(), alt = Array.Empty<float>(); }      // alt: x,z,dx,dz of further anchors, tried in order when the first one cannot be seated
        [Serializable] sealed class ArcSection { public string id = "", doc = ""; public List<ArcPiece> pieces = new List<ArcPiece>(); public int tris; }
        [Serializable] sealed class ArcBox { public float[] min = Array.Empty<float>(), max = Array.Empty<float>(); }
        [Serializable] sealed class ArcArena { public float[] min = Array.Empty<float>(), max = Array.Empty<float>(), boss = Array.Empty<float>(), eyes = Array.Empty<float>(); public float litKeepOut = 30f; }
        [Serializable] sealed class ArcBudget { public int tris = 45000, renderers = 14, lit = 6, lights; }
        [Serializable] sealed class ArcLum { public string lantern = ""; public float max = .56f; }
        [Serializable] sealed class ArcLodCfg { public float lod0 = .22f, cull = .02f; }
        [Serializable] sealed class ArcPlan
        {
            public string version = "", kit = "", meshTag = ""; public float floorGuess = 170f, groundReach = 4f, reach = 9f, clearMin = 2.6f, halfMin = 1f, propGap = .9f; public string[] wallKeys = Array.Empty<string>(), floorKeys = Array.Empty<string>(), rubbleKeys = Array.Empty<string>(), propSkip = Array.Empty<string>(), shellKeys = Array.Empty<string>();
            public ArcBox cell = new ArcBox(); public ArcArena arena = new ArcArena(); public ArcBudget budget = new ArcBudget(); public ArcLum lum = new ArcLum(); public ArcLodCfg lod = new ArcLodCfg(); public List<ArcSection> sections = new List<ArcSection>();
        }
        [Serializable] sealed class ArcRow { public string section = "", k = "", placedAs = "", why = ""; public bool placed; public int lit, anchor; public float x, y, z, yaw, wall, opp, clear, slide, bossDistance, dx, dz, fromExpect = -1f; }
        [Serializable] sealed class ArcLedger
        {
            public string version = "", appliedUtc = "", home = ""; public bool applied; public int placed, dropped, lit, tris0, tris1, renderers, collidersBefore, lightsBefore, litTris;
            public List<string> objects = new List<string>(), meshes = new List<string>(), materials = new List<string>(); public List<ArcRow> rows = new List<ArcRow>();
        }
        sealed class ArcBuf { public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>(); public readonly List<int> t = new List<int>(); }
        sealed class ArcWorld { public Transform root, home; public ArcPlan plan; public List<(Bounds box, Vector3[] v, int[] t)> rubble = new List<(Bounds, Vector3[], int[])>(); public List<(Bounds box, string path)> props = new List<(Bounds, string)>(); }

        static ArcPlan ArcLoadPlan()
        {
            if (!File.Exists(ArcPlanFile)) throw new Exception("no " + ArcPlanFile + " (python Tools/Unity/Stage308_minearcane/mine_arcane_plan.py)");
            var p = JsonUtility.FromJson<ArcPlan>(File.ReadAllText(ArcPlanFile));
            if (p.sections.Count == 0 || p.wallKeys.Length == 0 || p.floorKeys.Length == 0 || p.cell.min.Length != 3 || p.cell.max.Length != 3 || p.arena.min.Length != 2 || p.arena.max.Length != 2 || p.arena.boss.Length != 3) throw new Exception("the plan is incomplete (sections / wallKeys / floorKeys / cell / arena)");
            return p;
        }
        static ArcLedger ArcReadLedger() => File.Exists(ArcLedgerFile) ? JsonUtility.FromJson<ArcLedger>(File.ReadAllText(ArcLedgerFile)) : new ArcLedger();
        static void ArcWriteLedger(ArcLedger l) { Directory.CreateDirectory(Path.GetDirectoryName(ArcLedgerFile)); File.WriteAllText(ArcLedgerFile, JsonUtility.ToJson(l, true)); }
        static float ArcLuminance(Color c) => .2126f * c.r + .7152f * c.g + .0722f * c.b;
        static Color ArcBase(Material m) => m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.black;
        // what a material sends out without being lit: an unlit shader shows its base colour as it is; a lit one only its emission
        static float ArcGlow(Material m)
        {
            if (m == null || m.shader == null) return 0f; if (m.shader.name.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) >= 0) return ArcLuminance(ArcBase(m));
            return m.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionColor") ? ArcLuminance(m.GetColor("_EmissionColor")) : 0f;
        }

        static string Arcane(string verb)
        {
            if (verb.StartsWith("fix:", StringComparison.Ordinal)) return ArcFix(verb.Substring(4));      // Mine308.ArcaneFix.cs (the two leftovers; its own ledger)
            switch (verb)
            {
                case "status": return ArcStatus();
                case "check": return ArcCheck();
                case "apply": return ArcApply();
                case "revert": return ArcRevert();
                default: throw new Exception("look:arcane:" + verb + "? (apply | revert | status | check)");
            }
        }

        // ---------------------------------------------------------------- materials
        static Dictionary<string, Material> ArcMaterials(bool create, ArcLedger led)
        {
            var jo = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(ArcPlanFile))["materials"] as Newtonsoft.Json.Linq.JObject; if (jo == null) throw new Exception("the plan has no materials"); var d = new Dictionary<string, Material>();
            foreach (var kv in jo)
            {
                Material m;
                if (kv.Value.Type == Newtonsoft.Json.Linq.JTokenType.String) m = AssetDatabase.LoadAssetAtPath<Material>((string)kv.Value);
                else
                {
                    string path = ArcDir + "/Materials/" + ArcPrefix + kv.Key + ".mat"; m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (create)
                    {
                        Folder(ArcDir + "/Materials"); var srcPath = (string)kv.Value["copyOf"]; var src = string.IsNullOrEmpty(srcPath) ? null : AssetDatabase.LoadAssetAtPath<Material>(srcPath); if (!string.IsNullOrEmpty(srcPath) && src == null) throw new Exception("material source missing: " + srcPath);
                        if (m == null) { m = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit")); m.name = ArcPrefix + kv.Key; AssetDatabase.CreateAsset(m, path); }
                        else if (src != null) { m.shader = src.shader; m.CopyPropertiesFromMaterial(src); }
                        var col = (Newtonsoft.Json.Linq.JArray)kv.Value["color"]; var c = new Color((float)col[0], (float)col[1], (float)col[2], 1f); if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", (float)kv.Value["smooth"]); if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", (float)kv.Value["metallic"]);
                        // nothing of this kit may shine by itself: no sky reflection in a dark gallery, no emission
                        if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); m.DisableKeyword("_EMISSION"); if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack; EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); if (led != null && !led.materials.Contains(path)) led.materials.Add(path);
                    }
                }
                if (m == null) throw new Exception("material '" + kv.Key + "' missing (" + kv.Value.ToString(Newtonsoft.Json.Formatting.None) + ")"); d[kv.Key] = m;
            }
            if (!d.ContainsKey("egglit") || !d.ContainsKey("eggdead")) throw new Exception("the plan needs the materials egglit and eggdead");
            return d;
        }

        // ---------------------------------------------------------------- the world as the rays see it
        static ArcWorld ArcSee(Transform root, Transform home, ArcPlan plan)
        {
            var w = new ArcWorld { root = root, home = home, plan = plan }; Physics.SyncTransforms();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(false))
            {
                var r = mf.GetComponent<MeshRenderer>(); if (r == null || !r.enabled || mf.sharedMesh == null || home != null && mf.transform.IsChildOf(home)) continue; string path = PathOf(mf.transform, root);
                if (plan.rubbleKeys.Any(k => path.Contains(k)))
                {
                    var m = mf.transform.localToWorldMatrix; var sv = mf.sharedMesh.vertices; var wv = new Vector3[sv.Length]; for (int i = 0; i < sv.Length; i++) wv[i] = m.MultiplyPoint3x4(sv[i]); w.rubble.Add((r.bounds, wv, mf.sharedMesh.triangles)); continue;
                }
                var b = r.bounds; if (Mathf.Max(b.size.x, b.size.y, b.size.z) >= 5f || plan.propSkip.Any(k => path.Contains(k)) || plan.wallKeys.Any(k => path.Contains(k)) || plan.floorKeys.Any(k => path.Contains(k))) continue; w.props.Add((b, path));
            }
            return w;
        }
        static bool ArcIs(ArcWorld w, Transform t, string[] keys) { string p = PathOf(t, w.root); foreach (var k in keys) if (p.Contains(k)) return true; return false; }
        static bool ArcOwn(ArcWorld w, Transform t) => w.home != null && t.IsChildOf(w.home);
        // the cave floor under a point (only the floor: a pack wall's top or a heap is not a seat)
        static bool ArcGround(ArcWorld w, float x, float z, out float y)
        {
            y = 0f; bool any = false;
            foreach (var hit in Physics.RaycastAll(new Vector3(x, w.plan.floorGuess, z), Vector3.down, w.plan.groundReach, ~0, QueryTriggerInteraction.Ignore))
            {
                if (ArcOwn(w, hit.transform) || !ArcIs(w, hit.transform, w.plan.floorKeys)) continue; if (!any || hit.point.y > y) { y = hit.point.y; any = true; }
            }
            return any;
        }
        static float ArcMeshRay(ArcWorld w, Vector3 o, Vector3 d, float reach)
        {
            float best = float.MaxValue; var ray = new Ray(o, d);
            foreach (var (box, v, t) in w.rubble)
            {
                if (!box.IntersectRay(ray, out float enter) || enter > reach) continue;
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = v[t[i]], e1 = v[t[i + 1]] - a, e2 = v[t[i + 2]] - a, p = Vector3.Cross(d, e2); float det = Vector3.Dot(e1, p); if (det > -1e-9f && det < 1e-9f) continue; float inv = 1f / det; var tv = o - a; float u = Vector3.Dot(tv, p) * inv; if (u < 0f || u > 1f) continue;
                    var q = Vector3.Cross(tv, e1); float vv = Vector3.Dot(d, q) * inv; if (vv < 0f || u + vv > 1f) continue; float dist = Vector3.Dot(e2, q) * inv; if (dist > 1e-4f && dist < best) best = dist;
                }
            }
            return best <= reach ? best : float.MaxValue;
        }
        // the nearest wall along a ray: rock and pack wall colliders, and the rubble that is DRAWN over the pack walls (it stands up to 0.6 m in front of their colliders)
        static float ArcWall(ArcWorld w, Vector3 from, Vector3 dir, float reach)
        {
            float best = ArcMeshRay(w, from, dir, reach);
            foreach (var hit in Physics.RaycastAll(from, dir, reach, ~0, QueryTriggerInteraction.Ignore)) if (!ArcOwn(w, hit.transform) && ArcIs(w, hit.transform, w.plan.wallKeys) && hit.distance < best) best = hit.distance;
            return best;
        }
        static bool ArcSeen(ArcWorld w, Vector3 from, Vector3 to) { var d = to - from; float len = d.magnitude; return len < .01f || ArcWall(w, from, d / len, len) == float.MaxValue; }

        static bool ArcSeat(ArcWorld w, ArcPiece p, float slide, Bounds kitBox, out Vector3 pos, out float yaw, out float wall, out float opp, out string why)
        {
            pos = default; yaw = 0f; wall = 0f; opp = 0f; why = ""; var plan = w.plan; var dir = new Vector3(p.dx, 0f, p.dz); if (dir.sqrMagnitude < .5f) { why = "no direction"; return false; }
            dir.Normalize(); var tan = new Vector3(dir.z, 0f, -dir.x); float ax = p.x + tan.x * slide, az = p.z + tan.z * slide;
            if (!ArcGround(w, ax, az, out float y0)) { why = "no floor under the anchor"; return false; }
            float s = Mathf.Clamp(p.half * .6f, .2f, .8f); var ds = new List<float>(); bool centre = false;
            for (int k = -1; k <= 1; k++) { float d = ArcWall(w, new Vector3(ax, y0 + p.mountY, az) + tan * (k * s), dir, plan.reach); if (d < float.MaxValue) { ds.Add(d); if (k == 0) centre = true; } }
            if (!centre) { why = "no wall within " + plan.reach.ToString("F0", Inv) + " m"; return false; }
            ds.Sort(); wall = p.seat == "wall" ? ds[ds.Count / 2] : ds[0];      // a wall piece is built INTO an uneven wall (median), a floor piece stays in front of it (nearest)
            opp = ArcWall(w, new Vector3(ax, y0 + .9f, az), -dir, 14f); if (opp == float.MaxValue) opp = 14f;
            var at = new Vector3(ax, 0f, az) + dir * (wall - p.off); float y = y0;
            if (!ArcGround(w, at.x, at.z, out y)) { if (p.seat != "wall") { why = "seat off the floor"; return false; } y = y0; }
            pos = new Vector3(at.x, y, at.z); yaw = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg + p.yawOff;
            float clear = wall + opp - (p.off + p.outw), half = (wall + opp) * .5f - (p.off + p.outw);
            if (clear < plan.clearMin) { why = "corridor: " + clear.ToString("F1", Inv) + " m would stay clear (< " + plan.clearMin.ToString("F1", Inv) + ")"; return false; }
            if (half < plan.halfMin) { why = "corridor: reaches to " + half.ToString("F1", Inv) + " m of the walking line (< " + plan.halfMin.ToString("F1", Inv) + ")"; return false; }
            var a = plan.arena; if (pos.x > a.min[0] - p.half && pos.x < a.max[0] + p.half && pos.z > a.min[1] - p.half && pos.z < a.max[1] + p.half) { why = "inside the boss room"; return false; }
            // the footprint against what already lies there: colliders (heaps, lanterns, props, posts) and small drawn props without a collider
            var rot = Quaternion.Euler(0f, yaw, 0f); var c = pos + rot * kitBox.center; var he = kitBox.extents * .85f; float lift = Mathf.Min(.25f, he.y * .5f); c.y += lift; he.y -= lift; var world = new Bounds(c, Vector3.zero);
            for (int i = 0; i < 8; i++) world.Encapsulate(pos + rot * (kitBox.center + Vector3.Scale(kitBox.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            var cell = plan.cell; if (world.min.x < cell.min[0] || world.min.y < cell.min[1] || world.min.z < cell.min[2] || world.max.x > cell.max[0] || world.max.y > cell.max[1] || world.max.z > cell.max[2]) { why = "outside the interior cell"; return false; }
            foreach (var o in Physics.OverlapBox(c, he, rot, ~0, QueryTriggerInteraction.Ignore)) { if (ArcOwn(w, o.transform) || ArcIs(w, o.transform, w.plan.wallKeys) || ArcIs(w, o.transform, w.plan.floorKeys) || ArcIs(w, o.transform, w.plan.shellKeys)) continue; why = "in " + PathOf(o.transform, w.root); return false; }
            var foot = new Bounds(c, he * 2f); foreach (var (box, path) in w.props) if (box.Intersects(foot)) { why = "in " + path; return false; }
            // it must stand on the corridor side of the wall: nothing between it and its anchor
            var eye = new Vector3(pos.x, y + Mathf.Max(.6f, p.mountY), pos.z) - dir * (p.outw + .1f); if (!ArcSeen(w, eye, new Vector3(ax, y0 + Mathf.Max(.6f, p.mountY), az))) { why = "behind a wall"; return false; }
            return true;
        }
        // a living egg may not be near the boss nor be seen from the boss room (attacks are read by the boss's own glow)
        static bool ArcLitAllowed(ArcWorld w, Vector3 pos, Vector3 dir, out float bossDistance, out string why)
        {
            var a = w.plan.arena; why = ""; bossDistance = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(a.boss[0], a.boss[2]));
            if (bossDistance < a.litKeepOut) { why = "lit " + bossDistance.ToString("F0", Inv) + " m from the boss (< " + a.litKeepOut.ToString("F0", Inv) + ")"; return false; }
            var egg = pos + Vector3.up * 1.2f - dir * .2f;
            for (int i = 0; i + 1 < a.eyes.Length; i += 2) if (ArcSeen(w, egg, new Vector3(a.eyes[i], a.boss[1] + 1.6f, a.eyes[i + 1]))) { why = "lit and seen from the boss room at (" + a.eyes[i].ToString("F0", Inv) + ", " + a.eyes[i + 1].ToString("F0", Inv) + ")"; return false; }
            return true;
        }

        // ---------------------------------------------------------------- apply
        static string ArcApply()
        {
            var scene = PlayScene(true); var root = MineRoot(scene).transform; var plan = ArcLoadPlan(); var look = root.Find(LookRoot);
            if (look != null && look.Find(ArcHomeName) != null || ArcReadLedger().applied) return "REFUSED already applied (look:arcane:revert first)";
            string kitFile = Path.Combine(RepoRoot, plan.kit.Replace('/', Path.DirectorySeparatorChar)); if (!File.Exists(kitFile)) throw new Exception("no " + kitFile + " (python Tools/Art/arcane308_mine_kit.py export)");
            var kit = JsonUtility.FromJson<ArcKit>(File.ReadAllText(kitFile)).kit.ToDictionary(k => k.id); if (kit.Values.Any(k => k.lods.Count < 2)) throw new Exception("every kit object needs two LODs");
            foreach (var s in plan.sections) foreach (var p in s.pieces) if (!kit.ContainsKey(p.k)) throw new Exception("the plan asks for kit object '" + p.k + "'");
            int planLit = plan.sections.Sum(s => s.pieces.Count(p => p.lit != 0)); if (planLit > plan.budget.lit) throw new Exception("the plan lights " + planLit + " eggs (budget " + plan.budget.lit + ")");
            string dir = Path.Combine(ArcStage, "Backup", DateTime.Now.ToString("yyyyMMddTHHmmss", Inv) + "_arcane_apply"); Directory.CreateDirectory(dir); File.Copy(Path.GetFullPath(scene.path), Path.Combine(dir, Path.GetFileName(scene.path)), true);
            var led = new ArcLedger { version = plan.version, appliedUtc = DateTime.UtcNow.ToString("s", Inv), collidersBefore = root.GetComponentsInChildren<Collider>(true).Length, lightsBefore = root.GetComponentsInChildren<Light>(true).Length };
            var sb = new StringBuilder("Mine308 look:arcane:apply " + plan.version + ", backup " + dir); Transform home = null;
            try
            {
                var mats = ArcMaterials(true, led); var litMat = mats["egglit"]; var lantern = AssetDatabase.LoadAssetAtPath<Material>(plan.lum.lantern); if (lantern == null) throw new Exception("lantern paper material missing: " + plan.lum.lantern);
                float litGlow = ArcGlow(litMat), cap = Mathf.Min(plan.lum.max, ArcGlow(lantern)); if (litGlow <= 0f || litGlow > cap + 1e-3f) throw new Exception("the living egg material glows " + litGlow.ToString("F3", Inv) + " (must be > 0 and <= the lantern paper " + cap.ToString("F3", Inv) + ")");
                foreach (var kv in mats) if (kv.Key != "egglit" && ArcGlow(kv.Value) > 0f) throw new Exception("material '" + kv.Key + "' (" + kv.Value.name + ") glows; only the living egg may");
                var boxes = new Dictionary<string, Bounds>();
                foreach (var k in kit.Values) { var b = new Bounds(); bool first = true; foreach (var s in k.lods[0].subs) for (int i = 0; i + 2 < s.v.Length; i += 3) { var q = new Vector3(s.v[i], s.v[i + 1], s.v[i + 2]); if (first) { b = new Bounds(q, Vector3.zero); first = false; } else b.Encapsulate(q); } boxes[k.id] = b; }
                home = LookHome(root, ArcHomeName); led.home = PathOf(home, root); var world = ArcSee(root, home, plan); Folder(ArcDir + "/Meshes"); float[] slides = { 0f, .7f, -.7f, 1.4f, -1.4f };
                foreach (var sec in plan.sections)
                {
                    var places = new List<(ArcKitObj kit, Matrix4x4 m)>(); var centre = Vector3.zero;
                    foreach (var p in sec.pieces)
                    {
                        var row = new ArcRow { section = sec.id, k = p.k, lit = 0 }; led.rows.Add(row); string k = p.k; Vector3 pos = default; float yaw = 0f, wall = 0f, opp = 0f; string why = ""; bool ok = false; float used = 0f;
                        float x0 = p.x, z0 = p.z, dx0 = p.dx, dz0 = p.dz; string firstWhy = "";
                        for (int an = 0; an <= p.alt.Length / 4 && !ok; an++)
                        {
                            if (an > 0) { p.x = p.alt[an * 4 - 4]; p.z = p.alt[an * 4 - 3]; p.dx = p.alt[an * 4 - 2]; p.dz = p.alt[an * 4 - 1]; }
                            foreach (float slide in slides) { if (ArcSeat(world, p, slide, boxes[k], out pos, out yaw, out wall, out opp, out why)) { ok = true; used = slide; row.anchor = an; break; } if (why.StartsWith("inside the boss room", StringComparison.Ordinal) || why.StartsWith("outside the interior cell", StringComparison.Ordinal)) break; }
                            if (an == 0) firstWhy = why;
                        }
                        if (!ok) { p.x = x0; p.z = z0; p.dx = dx0; p.dz = dz0; row.why = why; sb.Append("\n    dropped ").Append(sec.id).Append('/').Append(p.k).Append(": ").Append(why); continue; }
                        if (row.anchor > 0) sb.Append("\n    ").Append(sec.id).Append('/').Append(p.k).Append(" seated at its alternative anchor ").Append(row.anchor).Append(" (").Append(p.x.ToString("F1", Inv)).Append(", ").Append(p.z.ToString("F1", Inv)).Append("): the first was ").Append(firstWhy);
                        if (kit[k].lit != 0)
                        {
                            string dark = "not marked lit in the plan"; bool alive = p.lit != 0 && ArcLitAllowed(world, pos, new Vector3(p.dx, 0f, p.dz).normalized, out row.bossDistance, out dark);
                            if (!alive)
                            {
                                string dead = k.Replace("_lit", "_dead"); if (!kit.ContainsKey(dead) || kit[dead].lit != 0) { row.why = "no unlit variant of " + k; continue; }
                                sb.Append("\n    ").Append(sec.id).Append('/').Append(k).Append(" placed DEAD: ").Append(dark); k = dead;
                            }
                            else row.lit = 1;
                        }
                        row.placed = true; row.placedAs = k; row.x = pos.x; row.y = pos.y; row.z = pos.z; row.yaw = yaw; row.wall = wall; row.opp = opp; row.clear = wall + opp - (p.off + p.outw); row.slide = used; row.dx = p.dx; row.dz = p.dz; if (p.expect.Length >= 3) row.fromExpect = Vector2.Distance(new Vector2(p.expect[0], p.expect[2]), new Vector2(pos.x, pos.z));
                        places.Add((kit[k], Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), Vector3.one))); centre += pos;
                    }
                    if (places.Count == 0) { sb.Append("\n  ").Append(sec.id).Append(": nothing placed"); continue; }
                    centre /= places.Count; var unit = new GameObject("Arcane_" + sec.id); unit.transform.SetParent(home, false); unit.transform.position = centre; var lods = new List<LOD>(); long tr0 = 0;
                    for (int step = 0; step < 2; step++)
                    {
                        var bufs = new Dictionary<string, ArcBuf>(); var order = new List<string>();
                        foreach (var (obj, m) in places)
                            foreach (var s in obj.lods[step].subs)
                            {
                                if (!mats.ContainsKey(s.mat)) throw new Exception("kit " + obj.id + ": material key " + s.mat); if (!bufs.TryGetValue(s.mat, out var b)) { bufs[s.mat] = b = new ArcBuf(); order.Add(s.mat); }
                                int b0 = b.v.Count;
                                for (int i = 0; i + 2 < s.v.Length; i += 3)
                                {
                                    var q = new Vector3(s.v[i], s.v[i + 1], s.v[i + 2]); var nn = new Vector3(s.n[i], s.n[i + 1], s.n[i + 2]); nn = nn.sqrMagnitude < .25f ? Vector3.up : nn.normalized;      // never a zero normal (it blooms white far away)
                                    b.v.Add(m.MultiplyPoint3x4(q) - centre); b.n.Add(m.MultiplyVector(nn).normalized); b.uv.Add(Mathf.Abs(nn.y) > .7f ? new Vector2(q.x, q.z) / 1.2f : new Vector2(Mathf.Abs(nn.x) > Mathf.Abs(nn.z) ? q.z : q.x, q.y) / 1.2f);
                                }
                                foreach (int i in s.t) b.t.Add(b0 + i);
                            }
                        var mesh = new Mesh { name = ArcPrefix + sec.id + "_L" + step + (plan.meshTag.Length > 0 ? "_" + plan.meshTag : "") }; /* a new name per iteration: a mesh asset re-created under an old name can keep a stale GPU buffer */ var av = new List<Vector3>(); var an = new List<Vector3>(); var auv = new List<Vector2>(); var subs = new List<int[]>();
                        foreach (string key in order) { var b = bufs[key]; int b0 = av.Count; av.AddRange(b.v); an.AddRange(b.n); auv.AddRange(b.uv); subs.Add(b.t.Select(i => i + b0).ToArray()); }
                        mesh.indexFormat = av.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16; mesh.SetVertices(av); mesh.SetNormals(an); mesh.SetUVs(0, auv); mesh.subMeshCount = subs.Count; for (int i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i], i); mesh.RecalculateBounds(); mesh.RecalculateTangents();
                        string path = ArcDir + "/Meshes/" + mesh.name + ".asset"; AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(mesh, path); AssetDatabase.SaveAssetIfDirty(mesh); led.meshes.Add(path);
                        var go = new GameObject(unit.name + "_LOD" + step); go.transform.SetParent(unit.transform, false); go.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = order.Select(k => mats[k]).ToArray();
                        mr.shadowCastingMode = step == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off; mr.lightProbeUsage = LightProbeUsage.BlendProbes; lods.Add(new LOD(step == 0 ? plan.lod.lod0 : plan.lod.cull, new Renderer[] { mr }));
                        long tr = subs.Sum(x => (long)x.Length) / 3; if (step == 0) { tr0 = tr; led.tris0 += (int)tr; int li = order.IndexOf("egglit"); if (li >= 0) led.litTris += subs[li].Length / 3; } else led.tris1 += (int)tr; led.renderers++;
                    }
                    var lg = unit.AddComponent<LODGroup>(); lg.fadeMode = LODFadeMode.None; lg.SetLODs(lods.ToArray()); lg.RecalculateBounds(); led.objects.Add(PathOf(unit.transform, root));
                    sb.Append("\n  Arcane_").Append(sec.id).Append(": placed ").Append(places.Count).Append('/').Append(sec.pieces.Count).Append(", LOD0 triangles ").Append(tr0);
                }
                led.placed = led.rows.Count(r => r.placed); led.dropped = led.rows.Count - led.placed; led.lit = led.rows.Count(r => r.placed && r.lit != 0); led.applied = true;
                int colliders = home.GetComponentsInChildren<Collider>(true).Length, lights = home.GetComponentsInChildren<Light>(true).Length;
                if (colliders != 0 || lights != 0) throw new Exception("the step made " + colliders + " colliders and " + lights + " lights (must be 0)");
                if (led.tris0 > plan.budget.tris || led.renderers > plan.budget.renderers || led.lit > plan.budget.lit) throw new Exception("over budget: triangles " + led.tris0 + "/" + plan.budget.tris + ", renderers " + led.renderers + "/" + plan.budget.renderers + ", lit " + led.lit + "/" + plan.budget.lit);
                if (led.placed == 0) throw new Exception("nothing could be seated (is the mine's floor / wall there? Mine308 dump)");
            }
            catch
            {
                if (home != null) Object.DestroyImmediate(home.gameObject); foreach (string m in led.meshes) AssetDatabase.DeleteAsset(m); foreach (string m in led.materials) AssetDatabase.DeleteAsset(m); throw;      // nothing half-applied: the scene was not saved
            }
            ArcWriteLedger(led); EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) throw new Exception("the play scene was not saved");
            return sb.Append("\n  placed ").Append(led.placed).Append(", dropped ").Append(led.dropped).Append(", lit ").Append(led.lit).Append(" (plan ").Append(planLit).Append("), LOD0 triangles ").Append(led.tris0).Append(", renderers ").Append(led.renderers).Append(", colliders 0, lights 0").ToString();
        }

        // ---------------------------------------------------------------- revert
        static string ArcRevert()
        {
            var scene = PlayScene(true); var root = MineRoot(scene).transform; var led = ArcReadLedger(); var look = root.Find(LookRoot); var home = look != null ? look.Find(ArcHomeName) : null;
            if (home == null && !led.applied) return "Mine308 look:arcane:revert: nothing to remove";
            string dir = Path.Combine(ArcStage, "Backup", DateTime.Now.ToString("yyyyMMddTHHmmss", Inv) + "_arcane_revert"); Directory.CreateDirectory(dir); File.Copy(Path.GetFullPath(scene.path), Path.Combine(dir, Path.GetFileName(scene.path)), true);
            int objects = 0, strangers = 0, meshes = 0, mats = 0;
            foreach (string path in led.objects) { var t = root.Find(path); if (t != null && home != null && t.IsChildOf(home)) { Object.DestroyImmediate(t.gameObject); objects++; } }
            if (home != null) { strangers = home.childCount; if (strangers == 0) Object.DestroyImmediate(home.gameObject); }      // what the ledger does not list is not this step's to delete
            foreach (string m in led.meshes) if (m.StartsWith(ArcDir + "/", StringComparison.Ordinal) && AssetDatabase.DeleteAsset(m)) meshes++;
            foreach (string m in led.materials) if (m.StartsWith(ArcDir + "/", StringComparison.Ordinal) && AssetDatabase.DeleteAsset(m)) mats++;
            foreach (string sub in new[] { "/Meshes", "/Materials", "" }) { string f = ArcDir + sub; if (AssetDatabase.IsValidFolder(f) && AssetDatabase.FindAssets("", new[] { f }).Length == 0) AssetDatabase.DeleteAsset(f); }
            ArcWriteLedger(new ArcLedger { version = led.version }); EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) throw new Exception("the play scene was not saved");
            return "Mine308 look:arcane:revert, backup " + dir + "\n  units removed " + objects + ", meshes " + meshes + ", materials " + mats + (strangers > 0 ? "\n  WARNING " + strangers + " objects under " + LookRoot + "/" + ArcHomeName + " are not in the ledger: left in place" : "");
        }

        // ---------------------------------------------------------------- status / check (read only)
        static string ArcStatus()
        {
            var scene = PlayScene(false); var root = MineRoot(scene).transform; var led = ArcReadLedger(); var look = root.Find(LookRoot); var home = look != null ? look.Find(ArcHomeName) : null;
            var sb = new StringBuilder("Mine308 look:arcane:status: ledger " + (led.applied ? "applied " + led.version + " at " + led.appliedUtc + "Z" : "not applied") + ", scene " + (home != null ? "has " + home.childCount + " units" : "has no Arcane home"));
            if (led.applied) sb.Append("\n  placed ").Append(led.placed).Append(", dropped ").Append(led.dropped).Append(", lit ").Append(led.lit).Append(", LOD0 triangles ").Append(led.tris0).Append(", LOD1 ").Append(led.tris1).Append(", renderers ").Append(led.renderers);
            foreach (var g in led.rows.GroupBy(r => r.section)) sb.Append("\n  ").Append(g.Key).Append(": ").Append(g.Count(r => r.placed)).Append('/').Append(g.Count()).Append(g.Any(r => r.lit != 0) ? " lit " + g.Count(r => r.lit != 0) : "");
            foreach (var r in led.rows.Where(r => !r.placed)) sb.Append("\n    dropped ").Append(r.section).Append('/').Append(r.k).Append(": ").Append(r.why);
            foreach (var r in led.rows.Where(r => r.placed && r.lit != 0)) sb.Append("\n    lit ").Append(r.section).Append('/').Append(r.placedAs).Append(" (").Append(r.x.ToString("F1", Inv)).Append(", ").Append(r.y.ToString("F2", Inv)).Append(", ").Append(r.z.ToString("F1", Inv)).Append("), boss ").Append(r.bossDistance.ToString("F0", Inv)).Append(" m");
            return sb.ToString();
        }

        static string ArcCheck()
        {
            var scene = PlayScene(false); var root = MineRoot(scene).transform; var plan = ArcLoadPlan(); var led = ArcReadLedger(); var look = root.Find(LookRoot); var home = look != null ? look.Find(ArcHomeName) : null; int fails = 0; var sb = new StringBuilder();
            void Line(bool ok, string text) { if (!ok) fails++; sb.Append("\n  ").Append(ok ? "ok   " : "FAIL ").Append(text); }
            if (home == null || !led.applied) return "Mine308 look:arcane:check FAIL: not applied (ledger " + led.applied + ", home " + (home != null) + ")";
            var units = new List<Transform>(); foreach (Transform t in home) units.Add(t); var listed = new HashSet<string>(led.objects);
            Line(units.Count == led.objects.Count && units.All(u => listed.Contains(PathOf(u, root))), "units in the scene " + units.Count + " = ledger " + led.objects.Count);
            Line(led.dropped == 0, "placed " + led.placed + ", dropped " + led.dropped + (led.dropped > 0 ? " (" + string.Join("; ", led.rows.Where(r => !r.placed).Select(r => r.section + "/" + r.k + ": " + r.why)) + ")" : ""));
            int colliders = home.GetComponentsInChildren<Collider>(true).Length, lights = home.GetComponentsInChildren<Light>(true).Length; Line(colliders == 0, "colliders added " + colliders); Line(lights == plan.budget.lights, "Light components added " + lights);
            int rootColliders = root.GetComponentsInChildren<Collider>(true).Length, rootLights = root.GetComponentsInChildren<Light>(true).Length;
            Line(rootColliders == led.collidersBefore, "colliders under `mine` " + rootColliders + " (at apply " + led.collidersBefore + ")"); Line(rootLights == led.lightsBefore, "lights under `mine` " + rootLights + " (at apply " + led.lightsBefore + ")");
            var renderers = home.GetComponentsInChildren<MeshRenderer>(true); long tris0 = 0, litTris = 0, zero = 0; var mats = ArcMaterials(false, null); var allowed = new HashSet<Material>(mats.Values); var litMat = mats["egglit"]; var strangers = new HashSet<string>(); var glowing = new HashSet<string>(); int flagged = 0;
            var cell = new Bounds(); cell.SetMinMax(new Vector3(plan.cell.min[0], plan.cell.min[1], plan.cell.min[2]), new Vector3(plan.cell.max[0], plan.cell.max[1], plan.cell.max[2])); int outside = 0;
            foreach (var r in renderers)
            {
                var mesh = r.GetComponent<MeshFilter>().sharedMesh; if (mesh == null) { Line(false, r.name + " has no mesh"); continue; } bool lod0 = r.name.EndsWith("_LOD0", StringComparison.Ordinal);
                if (GameObjectUtility.GetStaticEditorFlags(r.gameObject) != 0) flagged++; if (!cell.Contains(r.bounds.min) || !cell.Contains(r.bounds.max)) outside++;
                zero += mesh.normals.Count(n => n.sqrMagnitude < .25f);
                for (int i = 0; i < r.sharedMaterials.Length; i++)
                {
                    var m = r.sharedMaterials[i]; if (m == null || !allowed.Contains(m)) { strangers.Add(m != null ? m.name : "null"); continue; }
                    long tr = (long)mesh.GetIndexCount(i) / 3; if (lod0) tris0 += tr; if (m == litMat) { if (lod0) litTris += tr; } else if (ArcGlow(m) > 0f) glowing.Add(m.name);
                }
            }
            Line(renderers.Length <= plan.budget.renderers, "renderers " + renderers.Length + " (budget " + plan.budget.renderers + ")"); Line(tris0 <= plan.budget.tris, "LOD0 triangles " + tris0 + " (budget " + plan.budget.tris + ")");
            Line(home.GetComponentsInChildren<LODGroup>(true).Length == units.Count, "every unit has a LODGroup"); Line(flagged == 0, "static flags on " + flagged + " renderers"); Line(zero == 0, "zero-length normals " + zero);
            Line(strangers.Count == 0, "materials outside the plan: " + (strangers.Count == 0 ? "none" : string.Join(", ", strangers))); Line(glowing.Count == 0, "materials that glow besides the living egg: " + (glowing.Count == 0 ? "none" : string.Join(", ", glowing)));
            int planLit = plan.sections.Sum(s => s.pieces.Count(p => p.lit != 0)), lit = led.rows.Count(r => r.placed && r.lit != 0); Line(lit <= planLit && lit <= plan.budget.lit, "living eggs " + lit + " (plan " + planLit + ", budget " + plan.budget.lit + ")");
            Line(litTris == led.litTris && (lit > 0) == (litTris > 0), "triangles on the living-egg material " + litTris + " (ledger " + led.litTris + ")");
            var lantern = AssetDatabase.LoadAssetAtPath<Material>(plan.lum.lantern); float cap = lantern != null ? Mathf.Min(plan.lum.max, ArcGlow(lantern)) : plan.lum.max, glow = ArcGlow(litMat);
            Line(lantern != null && glow > 0f && glow <= cap + 1e-3f, "living egg luminance " + glow.ToString("F3", Inv) + " <= lantern paper " + cap.ToString("F3", Inv) + " (" + litMat.name + ", " + litMat.shader.name + ")");
            Line(outside == 0, "renderers reaching outside the interior cell " + outside);
            var a = plan.arena; var inArena = led.rows.Where(r => r.placed && r.x > a.min[0] && r.x < a.max[0] && r.z > a.min[1] && r.z < a.max[1]).ToList(); Line(inArena.Count == 0, "pieces on the boss room floor " + inArena.Count);
            var tight = led.rows.Where(r => r.placed && r.clear < plan.clearMin).ToList(); Line(tight.Count == 0, "pieces leaving less than " + plan.clearMin.ToString("F1", Inv) + " m of corridor " + tight.Count + (tight.Count > 0 ? " (" + string.Join(", ", tight.Select(r => r.section + "/" + r.k)) + ")" : ""));
            var world = ArcSee(root, home, plan);
            foreach (var r in led.rows.Where(r => r.placed && r.lit != 0))
            {
                bool ok = ArcLitAllowed(world, new Vector3(r.x, r.y, r.z), new Vector3(r.dx, 0f, r.dz).normalized, out float boss, out string why); Line(ok, "living egg " + r.section + " (" + r.x.ToString("F1", Inv) + ", " + r.z.ToString("F1", Inv) + "): boss " + boss.ToString("F0", Inv) + " m, unseen from the boss room" + (ok ? "" : " - " + why));
            }
            var moved = led.rows.Where(r => r.placed && r.fromExpect > 1f).ToList();
            sb.Append("\n  note seats more than 1 m from the offline expectation: ").Append(moved.Count).Append(moved.Count > 0 ? " (" + string.Join(", ", moved.Select(r => r.section + "/" + r.k + " " + r.fromExpect.ToString("F1", Inv) + " m")) + ") - look at them in the stills" : "");
            return "Mine308 look:arcane:check " + (fails == 0 ? "PASS" : "FAIL (" + fails + ")") + sb;
        }
    }
}
