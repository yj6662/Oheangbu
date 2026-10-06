using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 flat build (D308-31 "건물 수정 ... 차라리 모든 건물을 평지에 세우도록", D308-29 answer 3 "디딤 단 · 경사로").
    // No terrain edit: a house is moved onto flatter ground or keeps its place on a drawn stone podium (석축) that reaches below the
    // ground on every side, with steps on its entry side; attached parts (stalls, fences, stacks) are put back on the ground.
    // Every number comes from the data file (Art/Playtest308/FlatBuild/flatbuild308.json, written offline by
    // Tools/Unity/Stage308_flatbuild/_Tools/flatbuild_plan.py). Without that file every command refuses.
    // Queue: Oheangbu.EditorTools.WorldMacro.FlatBuild308 Run "<command>"        (<alias> = arch296 | folk298 | main)
    //   status              data sha, ledgers
    //   plan:<alias>        READ ONLY. Per op: apply / already / mismatch / blocked, the terrain probes against the editor's own terrain ray.
    //   apply:<alias>       refuses as a whole when one op is mismatch / blocked. Scene backup + ledger BEFORE the save. Second run = 변경 없음.
    //   apply-all           plans the three scenes first; one refusal anywhere = nothing changes anywhere.
    //   verify:<alias>      READ ONLY post-conditions: every op on its "to" value, every block there and reaching under the terrain.
    //   revert:<alias>[:force]   ledger "before" values back, the created root taken out (mesh assets stay on disk, unreferenced).
    // Ops: pose (world position of one object), stretch (a leg cube made longer: world position + localScale.y),
    //      blocks (one mesh + one BoxCollider per block under root data.root; material / layer / static flags of data.material_from).
    // Never: a protected tree (Watershed295* / Reworld292* / MountainTrail285*), a dialog, AssetDatabase.SaveAssets, a NavMesh bake
    // (the one bake after the three scenes is CompactArchitecture296 Run "nav" on the #296 scene - RUN_ORDER_flatbuild.md).
    public static class FlatBuild308
    {
        const string Usage = "status | plan:<alias> | apply:<alias> | apply-all | verify:<alias> | revert:<alias>[:force]";
        const string DataPath = "Art/Playtest308/FlatBuild/flatbuild308.json";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        sealed class Refuse : Exception { public Refuse(string m) : base(m) { } }

        sealed class OpState
        {
            public JObject op; public string kind, key, state, why = ""; public Transform t;
            public Vector3 from, to; public float scaleFrom, scaleTo;
        }

        public static string Run(string command)
        {
            string c = (command ?? "").Trim(); var a = c.Split(':');
            try
            {
                switch (a[0])
                {
                    case "status": return Status();
                    case "plan": return Plan(Arg(a));
                    case "apply": return Apply(Arg(a));
                    case "apply-all": return ApplyAll();
                    case "verify": return Verify(Arg(a));
                    case "revert": return Revert(Arg(a), a.Length > 2 && a[2].Trim() == "force");
                    default: return "REFUSED unknown FlatBuild308 command '" + c + "' (" + Usage + ")";
                }
            }
            catch (Refuse r) { return "REFUSED " + r.Message; }
            catch (PostLedger308.Refused r) { return "REFUSED " + r.Message; }
            catch (Exception e) { Debug.LogException(e); return "FAILED " + e.GetType().Name + ": " + e.Message; }
        }

        static string Arg(string[] a) { if (a.Length < 2 || a[1].Trim().Length == 0) throw new Refuse("use " + Usage); return a[1].Trim(); }

        // ------------------------------------------------------------------ data / ledger

        static JObject Load(out string sha)
        {
            string f = PostLedger308.RepoPath(DataPath);
            if (!File.Exists(f)) throw new Refuse("data missing: " + f + " (python Tools/Unity/Stage308_flatbuild/flatbuild_copy.py --apply)");
            JObject j;
            try { j = JObject.Parse(File.ReadAllText(f)); } catch (Exception e) { throw new Refuse("data does not parse: " + f + " (" + e.Message + ")"); }
            foreach (string k in new[] { "root", "ledger_dir", "mesh_folder", "scenes", "tolerance", "podium", "ops", "protected_prefix", "terrain_root" }) Req(j, k);
            foreach (string k in new[] { "pose_m", "ground_check_m", "reach_min_m" }) Num(j["tolerance"], k);
            Num(j["podium"], "uv_m");
            string mesh = Str(j, "mesh_folder");
            if (!mesh.StartsWith("Assets/", StringComparison.Ordinal) || PostLedger308.IsProtectedPath(mesh + "/")) throw new Refuse("mesh_folder '" + mesh + "' is not a writable asset folder");
            sha = BuildingAudit308.Sha(f);
            return j;
        }
        static JToken Req(JToken o, string key) { var t = o?[key]; if (t == null || t.Type == JTokenType.Null) throw new Refuse("flatbuild308.json: missing '" + key + "'"); return t; }
        static float Num(JToken o, string key) => Req(o, key).Value<float>();
        static string Str(JToken o, string key) => Req(o, key).Value<string>();
        static string Opt(JToken o, string key) { var t = o?[key]; return t == null || t.Type == JTokenType.Null ? "" : t.Value<string>(); }
        static Vector3 Vec(JToken o, string key) { var v = (JArray)Req(o, key); if (v.Count != 3) throw new Refuse("flatbuild308.json: '" + key + "' is not [x, y, z]"); return new Vector3(v[0].Value<float>(), v[1].Value<float>(), v[2].Value<float>()); }
        static string F(float v, string f = "F3") => float.IsNaN(v) ? "NaN" : v.ToString(f, Inv);
        static string V(Vector3 v) => string.Format(Inv, "({0:F3}, {1:F3}, {2:F3})", v.x, v.y, v.z);
        static string Short(string sha) => string.IsNullOrEmpty(sha) ? "-" : sha.Substring(0, Math.Min(12, sha.Length));

        static string ScenePath(JObject d, string alias)
        {
            if (!((JArray)d["scenes"]).Any(x => x.Value<string>() == alias)) throw new Refuse("alias '" + alias + "' is not in the data's scenes[]");
            foreach (var s in PostLedger308.Scenes) if (PostLedger308.Short(s) == alias) return s;
            throw new Refuse("alias '" + alias + "' (arch296 | folk298 | main)");
        }
        static string LedgerDir(JObject d) => PostLedger308.RepoPath(Str(d, "ledger_dir"));
        static string LedgerFile(JObject d, string alias) => Path.Combine(LedgerDir(d), "ledger_flatbuild308_" + alias + ".json");
        static JObject LedgerLoad(JObject d, string alias)
        {
            string f = LedgerFile(d, alias);
            if (!File.Exists(f)) return null;
            try { return JObject.Parse(File.ReadAllText(f)); } catch (Exception e) { throw new Refuse("ledger does not parse: " + f + " (" + e.Message + ")"); }
        }
        static void LedgerSave(JObject d, string alias, JObject ledger) { Directory.CreateDirectory(LedgerDir(d)); File.WriteAllText(LedgerFile(d, alias), ledger.ToString(), Utf8); }

        static string Status()
        {
            var d = Load(out string sha); var sb = new StringBuilder();
            sb.AppendLine("FlatBuild308 status | data " + Str(d, "version") + " sha " + Short(sha) + " | ops " + ((JArray)d["ops"]).Count + " | playing=" + EditorApplication.isPlaying);
            foreach (var s in (JArray)d["scenes"])
            {
                var l = LedgerLoad(d, s.Value<string>());
                sb.AppendLine("  " + s + ": " + (l == null ? "no ledger" : Opt(l, "state") + " " + Opt(l, "utc") + " data " + Short(Opt(l, "data_sha")) + " changes " + ((l["changes"] as JArray)?.Count ?? 0)));
            }
            return sb.ToString().TrimEnd();
        }

        // ------------------------------------------------------------------ scene helpers

        static Scene Open(string path, out string previous)
        {
            PostLedger308.RequireEditable();
            previous = SceneManager.GetActiveScene().path;
            return PostLedger308.Open(path);
        }
        static void GoBack(string previous, Scene scene)
        {
            if (string.IsNullOrEmpty(previous) || previous == scene.path) return;
            if (scene.isDirty) return;                 // never throw a change away by leaving
            EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
        }

        static bool Protected(JObject d, string key)
        {
            string root = key.Split('/')[0];
            return ((JArray)d["protected_prefix"]).Any(p => root.StartsWith(p.Value<string>(), StringComparison.Ordinal));
        }

        static Transform Strict(JObject d, Scene scene, string key, out string why)
        {
            why = "";
            if (Protected(d, key)) { why = "protected root: " + key; return null; }
            var t = BuildingAudit308.Resolve(scene, key);
            if (t == null) { why = "object not found: " + key; return null; }
            if (PostLedger308.UnderProtectedTree(t)) { why = "under a protected tree: " + key; return null; }
            string real = BuildingAudit308.KeyOf(t);
            if (real != key) { why = "ambiguous key " + key + " (the object's own key is " + real + ")"; return null; }
            return t;
        }

        static float TerrainY(JObject d, float x, float z, float nearY, RaycastHit[] hits)
        {
            string root = Str(d, "terrain_root");
            int n = Physics.RaycastNonAlloc(new Vector3(x, nearY + 200f, z), Vector3.down, hits, 600f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NaN;
            for (int i = 0; i < n; i++)
                if (hits[i].collider != null && hits[i].collider.transform.root.name == root && (float.IsNaN(best) || Mathf.Abs(hits[i].point.y - nearY) < Mathf.Abs(best - nearY))) best = hits[i].point.y;
            return best;
        }

        static string BlocksHash(JObject op)
        {
            var sb = new StringBuilder();
            foreach (var b in (JArray)op["blocks"]) sb.Append(Str(b, "n")).Append('|').Append(V(Vec(b, "c"))).Append('|').Append(V(Vec(b, "s"))).Append('|').Append(F(Num(b, "yaw"))).Append(';');
            return BuildingAudit308.ShaText(sb.ToString()).Substring(0, 8);
        }
        static string MeshName(JObject op) => "FlatBuild308_" + Str(op, "name") + "_" + BlocksHash(op);

        // ------------------------------------------------------------------ plan

        static List<OpState> MakePlan(JObject d, Scene scene, List<string> lines, out bool blocked)
        {
            float tolPose = Num(d["tolerance"], "pose_m"), tolGround = Num(d["tolerance"], "ground_check_m");
            var list = new List<OpState>(); var poseState = new Dictionary<string, OpState>(); var hits = new RaycastHit[64]; blocked = false;
            Physics.SyncTransforms();
            foreach (var token in (JArray)d["ops"])
            {
                var op = (JObject)token; var s = new OpState { op = op, kind = Str(op, "op") };
                if (s.kind == "pose" || s.kind == "stretch")
                {
                    s.key = Str(op, "key"); s.from = Vec(op, "from"); s.to = Vec(op, "to");
                    s.t = Strict(d, scene, s.key, out s.why);
                    if (s.t == null) s.state = "blocked";
                    else
                    {
                        Vector3 expect = s.from; bool scaleOk = true, scaleDone = true;
                        if (s.kind == "stretch")
                        {
                            s.scaleFrom = Num(op, "scale_y_from"); s.scaleTo = Num(op, "scale_y_to");
                            string after = Opt(op, "after_pose_of");
                            if (after.Length > 0 && poseState.TryGetValue(after, out var po) && po.state == "apply") expect = s.from - (po.to - po.from);   // its group has not moved yet
                            scaleOk = Mathf.Abs(s.t.localScale.y - s.scaleFrom) <= 1e-3f; scaleDone = Mathf.Abs(s.t.localScale.y - s.scaleTo) <= 1e-3f;
                            if (s.t.GetComponent<MeshFilter>() == null) { s.state = "blocked"; s.why = "stretch target has no mesh: " + s.key; }
                        }
                        if (s.state == null)
                        {
                            if ((s.t.position - s.to).magnitude <= tolPose && scaleDone) s.state = "already";
                            else if ((s.t.position - expect).magnitude <= tolPose && scaleOk) s.state = "apply";
                            else { s.state = "mismatch"; s.why = s.key + " stands at " + V(s.t.position) + " scale.y " + F(s.t.localScale.y) + " (data from " + V(expect) + " to " + V(s.to) + ")"; }
                        }
                    }
                    if (s.kind == "pose") poseState[s.key] = s;
                }
                else if (s.kind == "blocks")
                {
                    s.key = Str(d, "root") + "/" + Str(op, "name");
                    var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == Str(d, "root"));
                    var child = root != null ? root.transform.Find(Str(op, "name")) : null;
                    string from = Opt(op, "material_from"); string why = "";
                    var src = from.Length > 0 ? Strict(d, scene, from, out why) : null;
                    var mr = src != null ? src.GetComponent<MeshRenderer>() : null;
                    if (child != null)
                    {
                        var mf = child.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name == MeshName(op)) s.state = "already";
                        else { s.state = "mismatch"; s.why = s.key + " exists with another mesh (revert first)"; }
                    }
                    else if (mr == null || mr.sharedMaterial == null) { s.state = "blocked"; s.why = "material_from '" + from + "' has no renderer / material" + (why.Length > 0 ? " (" + why + ")" : ""); }
                    else
                    {
                        s.state = "apply"; float worst = 0f; int miss = 0;
                        foreach (var p in (JArray)Req(op, "probes"))
                        {
                            float x = p[0].Value<float>(), y = p[1].Value<float>(), z = p[2].Value<float>(); float g = TerrainY(d, x, z, y, hits);
                            if (float.IsNaN(g)) { miss++; continue; }
                            worst = Mathf.Max(worst, Mathf.Abs(g - y));
                        }
                        if (miss > 0 || worst > tolGround) { s.state = "blocked"; s.why = Str(op, "name") + ": the terrain differs from the plan's (" + miss + " probes without terrain, worst " + F(worst) + " m, limit " + F(tolGround) + ")"; }
                        else s.why = "probes " + ((JArray)op["probes"]).Count + " worst " + F(worst) + " m";
                    }
                }
                else { s.state = "blocked"; s.why = "unknown op '" + s.kind + "'"; }
                if (s.state == "mismatch" || s.state == "blocked") blocked = true;
                list.Add(s);
            }
            int n(string st) => list.Count(x => x.state == st);
            lines.Add("ops " + list.Count + " | apply " + n("apply") + " | already " + n("already") + " | mismatch " + n("mismatch") + " | blocked " + n("blocked"));
            foreach (var s in list.Where(x => x.state == "mismatch" || x.state == "blocked").Take(40)) lines.Add("  " + s.state + " " + s.kind + ": " + s.why);
            foreach (var s in list.Where(x => x.kind == "blocks" && x.state == "apply")) lines.Add("  blocks " + Str(s.op, "name") + " x" + ((JArray)s.op["blocks"]).Count + " " + s.why);
            return list;
        }

        static string Plan(string alias)
        {
            var d = Load(out string sha); string path = ScenePath(d, alias); var scene = Open(path, out string previous);
            try
            {
                var lines = new List<string> { "FlatBuild308 plan " + alias + " | data " + Short(sha) + " | scene " + Short(BuildingAudit308.Sha(PostLedger308.Abs(path))) + " | READ ONLY" };
                MakePlan(d, scene, lines, out bool blocked);
                lines.Add(blocked ? "BLOCKED: apply would refuse" : "plan ok");
                string text = string.Join("\n", lines);
                Directory.CreateDirectory(LedgerDir(d)); File.WriteAllText(Path.Combine(LedgerDir(d), "plan_flatbuild308_" + alias + ".txt"), text, Utf8);
                return text;
            }
            finally { GoBack(previous, scene); }
        }

        // ------------------------------------------------------------------ apply

        static string Apply(string alias)
        {
            var d = Load(out string sha); string path = ScenePath(d, alias); var scene = Open(path, out string previous);
            try { return ApplyOpen(d, sha, alias, path, scene); }
            finally { GoBack(previous, scene); }
        }

        static string ApplyAll()
        {
            var d = Load(out string sha); var sb = new StringBuilder(); string previous = null; Scene scene = default;
            PostLedger308.RequireEditable(); string start = SceneManager.GetActiveScene().path;
            try
            {
                foreach (var s in (JArray)d["scenes"])
                {
                    string alias = s.Value<string>(); scene = Open(ScenePath(d, alias), out previous);
                    var lines = new List<string>(); MakePlan(d, scene, lines, out bool blocked);
                    if (blocked) return "REFUSED nothing changed in any scene - " + alias + ": " + string.Join(" | ", lines);
                }
                foreach (var s in (JArray)d["scenes"])
                {
                    string alias = s.Value<string>(); string path = ScenePath(d, alias); scene = Open(path, out previous);
                    string r = ApplyOpen(d, sha, alias, path, scene); sb.AppendLine(r);
                    if (r.StartsWith("REFUSED", StringComparison.Ordinal) || r.StartsWith("FAILED", StringComparison.Ordinal)) break;
                }
                return sb.ToString().TrimEnd();
            }
            finally { if (scene.IsValid()) GoBack(start, scene); }
        }

        static string ApplyOpen(JObject d, string sha, string alias, string path, Scene scene)
        {
            var lines = new List<string>(); var plan = MakePlan(d, scene, lines, out bool blocked);
            if (blocked) return "REFUSED nothing changed - " + alias + ": " + string.Join(" | ", lines);
            if (plan.All(p => p.state == "already")) return "FlatBuild308 apply " + alias + ": 변경 없음 (ops " + plan.Count + " already)";
            var old = LedgerLoad(d, alias);
            if (old != null && Opt(old, "state") == "applied" && plan.Any(p => p.state == "apply") && plan.Any(p => p.state == "already") && Opt(old, "data_sha") != sha)
                return "REFUSED " + alias + ": a ledger of other data (" + Short(Opt(old, "data_sha")) + ") is applied - revert:" + alias + " first";
            string utc = PostLedger308.Utc(); string shaBefore = BuildingAudit308.Sha(PostLedger308.Abs(path));
            string backup = PostLedger308.Backup(Path.Combine(LedgerDir(d), "SceneBackup"), utc, path);
            var changes = old != null && Opt(old, "state") == "applied" && old["changes"] is JArray prior ? prior : new JArray();
            var ledger = new JObject { ["alias"] = alias, ["state"] = "applying", ["utc"] = utc, ["data_sha"] = sha, ["scene"] = path, ["scene_sha_before"] = shaBefore, ["backup"] = backup, ["changes"] = changes };
            LedgerSave(d, alias, ledger);
            int done = 0;
            foreach (var s in plan.Where(p => p.state == "apply" && p.kind == "pose"))
            {
                changes.Add(new JObject { ["op"] = "pose", ["key"] = s.key, ["before"] = Pos(s.t.position), ["after"] = Pos(s.to) });
                Undo.RecordObject(s.t, "FlatBuild308"); s.t.position = s.to; EditorUtility.SetDirty(s.t); done++;
            }
            foreach (var s in plan.Where(p => p.state == "apply" && p.kind == "stretch"))
            {
                var ls = s.t.localScale;
                changes.Add(new JObject { ["op"] = "stretch", ["key"] = s.key, ["before"] = Pos(s.t.position), ["after"] = Pos(s.to), ["scale_y_before"] = ls.y.ToString("R", Inv), ["scale_y_after"] = s.scaleTo.ToString("R", Inv) });
                Undo.RecordObject(s.t, "FlatBuild308"); s.t.position = s.to; s.t.localScale = new Vector3(ls.x, s.scaleTo, ls.z); EditorUtility.SetDirty(s.t); done++;
            }
            foreach (var s in plan.Where(p => p.state == "apply" && p.kind == "blocks"))
            {
                string mesh = Build(d, scene, s.op);
                changes.Add(new JObject { ["op"] = "blocks", ["key"] = s.key, ["mesh"] = mesh, ["boxes"] = ((JArray)s.op["blocks"]).Count }); done++;
            }
            LedgerSave(d, alias, ledger);                         // the ledger knows every change before the scene is saved
            PostLedger308.SaveScene(scene);
            ledger["state"] = "applied"; ledger["scene_sha_after"] = BuildingAudit308.Sha(PostLedger308.Abs(path)); LedgerSave(d, alias, ledger);
            return "FlatBuild308 apply " + alias + ": " + done + " ops written (" + string.Join(", ", lines.Take(1)) + ") | scene " + Short(shaBefore) + " -> " + Short(Opt(ledger, "scene_sha_after")) + " | backup " + backup
                   + " | NavMesh NOT baked (one bake after the three scenes: CompactArchitecture296 Run \"nav\")";
        }

        static JArray Pos(Vector3 v) => new JArray(v.x.ToString("R", Inv), v.y.ToString("R", Inv), v.z.ToString("R", Inv));
        static Vector3 PosOf(JToken a) => new Vector3(float.Parse(a[0].Value<string>(), Inv), float.Parse(a[1].Value<string>(), Inv), float.Parse(a[2].Value<string>(), Inv));

        // one mesh (all boxes, world-metre UVs) + one BoxCollider child per block
        static string Build(JObject d, Scene scene, JObject op)
        {
            string rootName = Str(d, "root"); float uv = Mathf.Max(.01f, Num(d["podium"], "uv_m"));
            var rootGo = scene.GetRootGameObjects().FirstOrDefault(g => g.name == rootName);
            if (rootGo == null) { rootGo = new GameObject(rootName); SceneManager.MoveGameObjectToScene(rootGo, scene); rootGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); }
            var src = BuildingAudit308.Resolve(scene, Str(op, "material_from")); var srcRenderer = src.GetComponent<MeshRenderer>();
            string folder = Str(d, "mesh_folder"); EnsureFolder(folder);
            string assetPath = folder + "/" + MeshName(op) + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (mesh == null)
            {
                var v = new List<Vector3>(); var nrm = new List<Vector3>(); var uvs = new List<Vector2>(); var tri = new List<int>();
                foreach (var b in (JArray)op["blocks"]) AddBox(v, nrm, uvs, tri, Vec(b, "c"), Vec(b, "s"), Num(b, "yaw"), uv);
                mesh = new Mesh { name = MeshName(op) };
                mesh.SetVertices(v); mesh.SetNormals(nrm); mesh.SetUVs(0, uvs); mesh.SetTriangles(tri, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents();
                AssetDatabase.CreateAsset(mesh, assetPath); AssetDatabase.SaveAssetIfDirty(mesh);
            }
            var go = new GameObject(Str(op, "name")); go.transform.SetParent(rootGo.transform, false);
            go.layer = src.gameObject.layer; GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(src.gameObject));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = srcRenderer.sharedMaterial; mr.shadowCastingMode = srcRenderer.shadowCastingMode; mr.receiveShadows = srcRenderer.receiveShadows;
            int i = 0;
            foreach (var b in (JArray)op["blocks"])
            {
                var c = new GameObject("box" + (i++).ToString(Inv) + "_" + Str(b, "role")); c.transform.SetParent(go.transform, false); c.layer = go.layer;
                GameObjectUtility.SetStaticEditorFlags(c, GameObjectUtility.GetStaticEditorFlags(src.gameObject));
                c.transform.SetPositionAndRotation(Vec(b, "c"), Quaternion.Euler(0f, Num(b, "yaw"), 0f));
                c.AddComponent<BoxCollider>().size = Vec(b, "s");
            }
            Undo.RegisterCreatedObjectUndo(go, "FlatBuild308");
            return assetPath;
        }

        static void AddBox(List<Vector3> v, List<Vector3> nrm, List<Vector2> uvs, List<int> tri, Vector3 c, Vector3 s, float yaw, float uv)
        {
            var q = Quaternion.Euler(0f, yaw, 0f); Vector3 h = s * .5f;
            // face: normal, then the two in-plane axes (u, w) with u x w = normal
            var faces = new[] { (Vector3.up, Vector3.forward, Vector3.right), (Vector3.down, Vector3.right, Vector3.forward), (Vector3.right, Vector3.up, Vector3.forward),
                                (Vector3.left, Vector3.forward, Vector3.up), (Vector3.forward, Vector3.right, Vector3.up), (Vector3.back, Vector3.up, Vector3.right) };
            foreach (var (n, u, w) in faces)
            {
                float hn = Mathf.Abs(Vector3.Dot(n, h)), hu = Mathf.Abs(Vector3.Dot(u, h)), hw = Mathf.Abs(Vector3.Dot(w, h)); int b0 = v.Count;
                for (int k = 0; k < 4; k++)
                {
                    float su = (k == 1 || k == 2) ? 1f : -1f, sw = (k >= 2) ? 1f : -1f;
                    Vector3 local = n * hn + u * (hu * su) + w * (hw * sw); Vector3 world = c + q * local;
                    v.Add(world); nrm.Add(q * n);
                    // world-metre UVs: side faces run along the horizontal axis and up, top / bottom faces on the ground plane
                    uvs.Add(Mathf.Abs(n.y) > .5f ? new Vector2(world.x, world.z) / uv : new Vector2(Vector3.Dot(world, q * (Mathf.Abs(u.y) > .5f ? w : u)), world.y) / uv);
                }
                tri.Add(b0); tri.Add(b0 + 1); tri.Add(b0 + 2); tri.Add(b0); tri.Add(b0 + 2); tri.Add(b0 + 3);
            }
        }

        static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder)) return;
            string parent = Path.GetDirectoryName(assetFolder).Replace('\\', '/'); EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetFolder));
        }

        // ------------------------------------------------------------------ verify / revert

        static string Verify(string alias)
        {
            var d = Load(out string sha); string path = ScenePath(d, alias); var scene = Open(path, out string previous);
            try
            {
                var lines = new List<string>(); var plan = MakePlan(d, scene, lines, out _); int pass = 0, fail = 0; var bad = new List<string>(); var hits = new RaycastHit[64];
                float reach = Num(d["tolerance"], "reach_min_m");
                foreach (var s in plan)
                {
                    if (s.state == "already") pass++; else { fail++; bad.Add(s.kind + " " + s.key + ": " + s.state + " " + s.why); }
                    if (s.kind != "blocks" || s.state != "already") continue;
                    var child = scene.GetRootGameObjects().First(g => g.name == Str(d, "root")).transform.Find(Str(s.op, "name"));
                    int boxes = child.GetComponentsInChildren<BoxCollider>(true).Length, want = ((JArray)s.op["blocks"]).Count;
                    if (boxes == want) pass++; else { fail++; bad.Add("blocks " + s.key + ": " + boxes + " colliders, data " + want); }
                    float worst = float.PositiveInfinity;
                    foreach (var b in (JArray)s.op["blocks"])
                    {
                        Vector3 c = Vec(b, "c"), sz = Vec(b, "s"); var q = Quaternion.Euler(0f, Num(b, "yaw"), 0f); float bottom = c.y - sz.y * .5f;
                        for (int k = 0; k < 4; k++)
                        {
                            Vector3 p = c + q * new Vector3((k == 0 || k == 3 ? -1 : 1) * sz.x * .5f, 0f, (k < 2 ? -1 : 1) * sz.z * .5f); float g = TerrainY(d, p.x, p.z, c.y, hits);
                            if (!float.IsNaN(g)) worst = Mathf.Min(worst, g - bottom);
                        }
                    }
                    if (worst >= reach) pass++; else { fail++; bad.Add("blocks " + s.key + ": a corner bottom is only " + F(worst) + " m under the terrain (min " + F(reach) + ")"); }
                }
                string text = "FlatBuild308 verify " + alias + ": " + (fail == 0 ? "GREEN" : "RED") + " (PASS " + pass + ", FAIL " + fail + ") data " + Short(sha) + (bad.Count > 0 ? "\n  " + string.Join("\n  ", bad.Take(40)) : "");
                Directory.CreateDirectory(LedgerDir(d)); File.WriteAllText(Path.Combine(LedgerDir(d), "verify_flatbuild308_" + alias + ".txt"), text, Utf8);
                return text;
            }
            finally { GoBack(previous, scene); }
        }

        static string Revert(string alias, bool force)
        {
            var d = Load(out _); string path = ScenePath(d, alias);
            var ledger = LedgerLoad(d, alias);
            if (ledger == null || !(ledger["changes"] is JArray changes) || changes.Count == 0) return "FlatBuild308 revert " + alias + ": 변경 없음 (no ledger)";
            if (Opt(ledger, "state") == "reverted") return "FlatBuild308 revert " + alias + ": 변경 없음 (already reverted " + Opt(ledger, "reverted_utc") + ")";
            var scene = Open(path, out string previous);
            try
            {
                float tol = Num(d["tolerance"], "pose_m"); var moved = new List<string>();
                foreach (var c in changes.Reverse())
                {
                    string kind = Opt(c, "op"); if (kind == "blocks") continue;
                    var t = BuildingAudit308.Resolve(scene, Opt(c, "key"));
                    if (t == null) { moved.Add(Opt(c, "key") + " not found"); continue; }
                    if ((t.position - PosOf(c["after"])).magnitude > tol && (t.position - PosOf(c["before"])).magnitude > tol) moved.Add(Opt(c, "key") + " stands at " + V(t.position));
                }
                if (moved.Count > 0 && !force) return "REFUSED revert " + alias + ": " + moved.Count + " objects moved after the apply (" + string.Join("; ", moved.Take(5)) + ") - revert:" + alias + ":force puts the recorded values back anyway";
                string utc = PostLedger308.Utc(); PostLedger308.Backup(Path.Combine(LedgerDir(d), "SceneBackup"), utc + "-revert", path); int n = 0;
                foreach (var c in changes.Reverse())
                {
                    string kind = Opt(c, "op");
                    if (kind == "blocks")
                    {
                        var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == Str(d, "root")); string name = Opt(c, "key").Substring(Opt(c, "key").IndexOf('/') + 1);
                        var child = root != null ? root.transform.Find(name) : null;
                        if (child != null) { Object.DestroyImmediate(child.gameObject); n++; }
                        if (root != null && root.transform.childCount == 0) Object.DestroyImmediate(root);
                        continue;
                    }
                    var t = BuildingAudit308.Resolve(scene, Opt(c, "key")); if (t == null) continue;
                    if (kind == "stretch") { var ls = t.localScale; t.localScale = new Vector3(ls.x, float.Parse(Opt(c, "scale_y_before"), Inv), ls.z); }
                    t.position = PosOf(c["before"]); EditorUtility.SetDirty(t); n++;
                }
                PostLedger308.SaveScene(scene);
                ledger["state"] = "reverted"; ledger["reverted_utc"] = utc; ledger["scene_sha_reverted"] = BuildingAudit308.Sha(PostLedger308.Abs(path)); LedgerSave(d, alias, ledger);
                return "FlatBuild308 revert " + alias + ": " + n + " changes taken back | scene sha " + Short(Opt(ledger, "scene_sha_reverted")) + " (before the apply: " + Short(Opt(ledger, "scene_sha_before")) + ") | mesh assets stay on disk | NavMesh NOT baked";
            }
            finally { GoBack(previous, scene); }
        }
    }
}
