using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.Demo;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 cliff boundary 1b - the `guk:<scene>` step of SPEC-WORLD-CLIFF-BOUNDARY-308 (design 8, D308-9c Q4): the authored pieces of the
    // cliff-top field UP-1 = one GukLiftSite (foot bed, shelf), the one-way drop ledges with their lips and chockstone gates, and the rim
    // lips the terrain package asks for. Data: Art/World/Compact/Rebuild/CliffBoundary308/small_1b/guk308.json (Tools/Art/small308.py guk);
    // this class holds no number of the design. Every structural piece is a jointed-granite prism: the visible prisms of a group are one
    // combined mesh, and the BoxCollider of a piece IS its prism (inside the visible mass by construction). Caps are render only.
    // Queue: Oheangbu.EditorTools.WorldMacro.CliffGuk308 Run "<command>"      (<scene> = arch296 | folk298 | main)
    //   status                    data, sources, ledgers, what stands in the three scenes (reads ledgers and files only)
    //   plan:<scene>              dry: what apply would build, with the gates it would be refused on; nothing changes
    //   apply:<scene>             byte backup -> build CliffBoundary308/UP1 and /Lips -> save -> ledger. Second run = "변경 없음".
    //                             Refused unless the scene's CliffBoundary308 ledger says the required stage (1b) on the same height.
    //                             The GukLiftSite is authored DISABLED.
    //   verify:<scene>            read only: lift rules, risers, the capsule walks of the data (descent / climb back / lips), standable
    //                             list (AC-B10b), colliders against the prisms, emission / lights 0, counts -> Out/cb308-guk-verify-<scene>.*
    //   lift-on:<scene>:joints=ok:rim=ok:content=ok   enables the GukLiftSite; needs a green verify of this very scene file and the
    //                             three tokens (forest joints FAIL 0, cb308 rim probe, the cliff-top content of AC-B23)
    //   lift-off:<scene>          disables it again
    //   revert:<scene>            removes exactly what apply built (the root only when nothing else is under it; Skins belongs to
    //                             another package) and the mesh assets no other scene ledger still names
    // Order: #296 candidate -> #298 candidate -> W_Demo_Main, after `tiles:<scene>:stage=1b`, the forest v305.4 build and joints-scene.
    public static partial class CliffGuk308
    {
        const string DataFile = Pieces308.DataDir + "/guk308.json";
        const string Usage = "status | plan:<scene> | apply:<scene> | verify:<scene> | lift-on:<scene>:joints=ok:rim=ok:content=ok | lift-off:<scene> | revert:<scene>";

        public static string Run(string command)
        {
            string[] a = (command ?? "").Trim().Split(':'); string verb = a[0]; string target = a.Length > 1 ? a[1] : "";
            try
            {
                if (verb == "status") return Status();
                var cfg = CliffCore308.LoadConfig();
                if (target.Length == 0) throw new PostLedger308.Refused("use " + Usage);
                string scene = CliffCore308.ScenePath(cfg, target); var opt = PostLedger308.Options(a.Skip(2));
                switch (verb)
                {
                    case "plan": return Apply(cfg, scene, true);
                    case "apply": return Apply(cfg, scene, false);
                    case "verify": return Verify(cfg, scene);
                    case "lift-on": return Lift(cfg, scene, true, opt);
                    case "lift-off": return Lift(cfg, scene, false, opt);
                    case "revert": return Revert(cfg, scene);
                    default: throw new PostLedger308.Refused("unknown command '" + command + "' (" + Usage + ")");
                }
            }
            catch (PostLedger308.Refused r) { return "refused: " + r.Message; }
            catch (Exception e) { Debug.LogException(e); return "error: " + e.GetType().Name + ": " + e.Message + " (see the console; the ledger shows how far the command got)"; }
        }

        // ---------------------------------------------------------------- plan (the data, typed)

        sealed class Piece { public string Id, Group, Role, Belongs; public bool Standable; public Vector3 Centre, Size; public float Yaw, Top, Bottom; }
        sealed class Cap { public string Id, On, Lod0Guid, Lod1Guid, MatGuid; public long Lod0File, Lod1File; public Vector3 Position, Scale; public float Yaw, Screen0, Screen1; }
        sealed class Plan
        {
            public JObject Data; public string Sha, MaterialGuid, RootName, Up1Name, LipsName, LiftId, RequiredStage, HeightSha;
            public List<Piece> Pieces = new List<Piece>(); public List<Cap> Caps = new List<Cap>();
            public Vector3 Lower, Upper; public float NodeLift, CastRadius, RiseSeconds; public bool LiftEnabledOnApply;
            public string[] Failed, Pending;
            public IEnumerable<string> Groups => Pieces.Select(p => p.Group).Distinct().OrderBy(g => g, StringComparer.Ordinal);
        }

        static Plan Load()
        {
            var d = Pieces308.Json(DataFile, out string sha); var p = new Plan { Data = d, Sha = sha };
            var roots = Pieces308.Req(d, "roots"); p.RootName = Pieces308.Str(roots, "root"); p.Up1Name = Pieces308.Str(roots, "up1"); p.LipsName = Pieces308.Str(roots, "lips");
            p.MaterialGuid = Pieces308.Str(Pieces308.Req(d, "material"), "guid");
            p.RequiredStage = Pieces308.Str(Pieces308.Req(d, "requires"), "cb308_stage");
            p.HeightSha = Pieces308.Str(Pieces308.Req(Pieces308.Req(d, "sources"), "height"), "sha256");
            var lift = Pieces308.Req(d, "lift"); p.LiftId = Pieces308.Str(lift, "id");
            // lower_node / upper_node = where the nodes stand (bed top, shelf top + node lift); "lower" / "upper" are the declared coordinates
            p.Lower = Pieces308.V3(Pieces308.Req(lift, "lower_node")); p.Upper = Pieces308.V3(Pieces308.Req(lift, "upper_node"));
            p.NodeLift = 0f; p.CastRadius = Pieces308.Num(lift, "cast_radius"); p.RiseSeconds = Pieces308.Num(lift, "rise_seconds");
            p.LiftEnabledOnApply = Pieces308.Flag(lift, "enabled_on_apply");
            foreach (var r in Pieces308.Arr(d, "pieces"))
            {
                if (Pieces308.Str(r, "kind") != "prism") throw new PostLedger308.Refused("data: piece " + Pieces308.Str(r, "id") + " is not a prism");
                p.Pieces.Add(new Piece { Id = Pieces308.Str(r, "id"), Group = Pieces308.Str(r, "group"), Role = Pieces308.Str(r, "role"), Belongs = Pieces308.Opt(r, "belongs_to"), Standable = Pieces308.Flag(r, "standable"),
                    Centre = Pieces308.V3(Pieces308.Req(r, "centre")), Size = Pieces308.V3(Pieces308.Req(r, "size")), Yaw = Pieces308.Num(r, "yaw_deg"), Top = Pieces308.Num(r, "top_y"), Bottom = Pieces308.Num(r, "bottom_y") });
            }
            foreach (var r in Pieces308.Arr(d, "dressing"))
            {
                var l0 = Pieces308.Req(r, "lod0"); var l1 = Pieces308.Req(r, "lod1"); var sc = Pieces308.Req(r, "lod_screen");
                p.Caps.Add(new Cap { Id = Pieces308.Str(r, "id"), On = Pieces308.Str(r, "on"), Lod0Guid = Pieces308.Str(l0, "guid"), Lod0File = Pieces308.Req(l0, "file_id").Value<long>(), Lod1Guid = Pieces308.Str(l1, "guid"), Lod1File = Pieces308.Req(l1, "file_id").Value<long>(),
                    MatGuid = Pieces308.Str(Pieces308.Req(r, "material"), "guid"), Position = Pieces308.V3(Pieces308.Req(r, "position")), Scale = Pieces308.V3(Pieces308.Req(r, "scale")), Yaw = Pieces308.Num(r, "yaw_deg"),
                    Screen0 = Pieces308.At(sc, 0), Screen1 = Pieces308.At(sc, 1) });
            }
            if (p.Pieces.Count == 0 || p.Pieces.Select(x => x.Id).Distinct().Count() != p.Pieces.Count) throw new PostLedger308.Refused("data: no pieces, or piece ids repeat");
            if (p.Pieces.Any(x => x.Group != p.Up1Name && x.Group != p.LipsName)) throw new PostLedger308.Refused("data: a piece names a group other than " + p.Up1Name + " / " + p.LipsName);
            var checks = Pieces308.Req(d, "checks");
            p.Failed = Pieces308.Arr(checks, "failed").Select(t => t.Value<string>()).ToArray(); p.Pending = Pieces308.Arr(checks, "pending").Select(t => t.Value<string>()).ToArray();
            return p;
        }

        /// <summary>The files the data was built from must still be the files on disk (else the offline checks say nothing).</summary>
        static List<string> Stale(Plan p)
        {
            var list = new List<string>(); var src = Pieces308.Req(p.Data, "sources");
            foreach (string key in new[] { "ops", "height", "config" })
            {
                var s = Pieces308.Req(src, key); string rel = "Art/World/Compact/Rebuild/CliffBoundary308/" + Pieces308.Str(s, "path");
                string now = CliffCore308.ShaFile(PostLedger308.RepoPath(rel));
                if (now != Pieces308.Str(s, "sha256")) list.Add(key + " " + rel + " is " + (now == "" ? "missing" : Pieces308.Short(now)) + ", the data was built on " + Pieces308.Short(Pieces308.Str(s, "sha256")));
            }
            var rim = Pieces308.Req(p.Data, "rim_lips"); string lipsRel = "Art/World/Compact/Rebuild/CliffBoundary308/" + Pieces308.Str(rim, "source");
            string lipsNow = CliffCore308.ShaFile(PostLedger308.RepoPath(lipsRel));
            if (lipsNow != Pieces308.Opt(rim, "sha256")) list.Add("rim lips " + lipsRel + " is " + (lipsNow == "" ? "missing" : Pieces308.Short(lipsNow)) + ", the data was built on " + (Pieces308.Opt(rim, "sha256") == "" ? "no file" : Pieces308.Short(Pieces308.Opt(rim, "sha256"))));
            return list;
        }

        // ---------------------------------------------------------------- ledger

        [Serializable] sealed class Ledger
        {
            public string format = "cb308.guk.1", scene = "", key = "", state = "none", utc = "", dataSha256 = "", heightSha256 = "", sceneShaBefore = "", sceneShaAfter = "", backup = "", liftId = "";
            public bool rootCreated, liftEnabled; public int prisms, caps;
            public string[] created = new string[0], assets = new string[0], history = new string[0];
        }
        [Serializable] sealed class VerifyStamp { public string scene = "", utc = "", sceneSha256 = "", dataSha256 = ""; public bool green; public int fail, notRun; }
        static string LedgerName(string key) => "cb308-guk-" + key + ".json";
        static string VerifyName(string key, string ext) => "cb308-guk-verify-" + key + "." + ext;
        static void Note(Ledger l, string text) { l.utc = PostLedger308.Utc(); l.history = l.history.Concat(new[] { l.utc + " " + text }).ToArray(); }

        // ---------------------------------------------------------------- status

        static string Status()
        {
            var sb = new StringBuilder("CliffGuk308 status | playing=" + EditorApplication.isPlaying + "\n");
            Plan p;
            try { p = Load(); } catch (PostLedger308.Refused r) { return sb.Append("data: " + r.Message).ToString(); }
            sb.AppendLine("data " + DataFile + " sha " + Pieces308.Short(p.Sha) + ": " + p.Pieces.Count + " prisms (" + string.Join(", ", p.Groups.Select(g => g + " " + p.Pieces.Count(x => x.Group == g))) + "), " + p.Caps.Count + " caps, lift " + p.LiftId);
            sb.AppendLine("offline checks: failed [" + string.Join(", ", p.Failed) + "], pending [" + string.Join(", ", p.Pending) + "]");
            var stale = Stale(p); sb.AppendLine(stale.Count == 0 ? "sources: the data matches the files on disk" : "sources STALE: " + string.Join("; ", stale));
            var cfg = CliffCore308.LoadConfig();
            foreach (var sc in cfg.scenes)
            {
                var l = Pieces308.ReadLedger<Ledger>(LedgerName(sc.key)); var cb = CliffBoundary308.ReadLedger(cfg, sc.path);
                var v = Pieces308.ReadLedger<VerifyStamp>(VerifyName(sc.key, "stamp.json"));
                sb.AppendLine("  " + sc.key + ": cliff ledger " + cb.state + (cb.stage != "" ? " stage " + cb.stage : "") + " | guk " + (l == null ? "none" : l.state + " data " + Pieces308.Short(l.dataSha256) + (l.dataSha256 == p.Sha ? "" : " (NOT the present data)") + ", lift " + (l.liftEnabled ? "ON" : "off"))
                    + " | verify " + (v == null ? "never" : (v.green ? "green" : "RED") + " " + v.utc + (v.sceneSha256 == Pieces308.ShaAsset(sc.path) ? "" : " (the scene changed since)")));
            }
            return sb.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- geometry

        static readonly (Vector3 u, Vector3 v)[] Faces =
        {
            (Vector3.up, Vector3.forward), (Vector3.forward, Vector3.up), (Vector3.forward, Vector3.right),
            (Vector3.right, Vector3.forward), (Vector3.right, Vector3.up), (Vector3.up, Vector3.right),
        };

        /// <summary>All prisms of a group as one mesh in world coordinates (24 vertices and 12 triangles each, flat faces).</summary>
        static Mesh PrismMesh(IEnumerable<Piece> pieces)
        {
            var vs = new List<Vector3>(); var ns = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            foreach (var p in pieces)
            {
                var rot = Quaternion.Euler(0f, p.Yaw, 0f); var h = p.Size * .5f;
                foreach (var (u, v) in Faces)
                {
                    var n = Vector3.Cross(u, v); int at = vs.Count;
                    float hu = Mathf.Abs(Vector3.Dot(u, h)), hv = Mathf.Abs(Vector3.Dot(v, h)), hn = Mathf.Abs(Vector3.Dot(n, h));
                    foreach (var (su, sv) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                    {
                        vs.Add(p.Centre + rot * (n * hn + u * (su * hu) + v * (sv * hv))); ns.Add(rot * n); uv.Add(new Vector2(su * hu, sv * hv));
                    }
                    tri.AddRange(new[] { at, at + 1, at + 2, at, at + 2, at + 3 });
                }
            }
            var mesh = new Mesh { indexFormat = vs.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(vs); mesh.SetNormals(ns); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents();
            return mesh;
        }
        static string GroupSha(Plan p, string group)
        {
            var sb = new StringBuilder();
            foreach (var x in p.Pieces.Where(x => x.Group == group))
                foreach (float f in new[] { x.Centre.x, x.Centre.y, x.Centre.z, x.Size.x, x.Size.y, x.Size.z, x.Yaw }) sb.Append(x.Id).Append('|').Append(f.ToString("R", Pieces308.Inv)).Append(';');
            return CliffCore308.Sha(Encoding.UTF8.GetBytes(sb.ToString()));
        }
        static string MeshAsset(CliffCore308.Config cfg, Plan p, string group) => cfg.assetRoot + "/Meshes/Small/" + group + "_prisms_" + GroupSha(p, group).Substring(0, 8) + ".asset";
        static T ByGuid<T>(string guid, string what) where T : Object
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var o = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
            if (o == null) throw new PostLedger308.Refused(what + ": guid " + guid + " does not resolve to a " + typeof(T).Name);
            return o;
        }
        static GameObject Child(Transform parent, string name) { var go = new GameObject(name); go.transform.SetParent(parent, false); return go; }

        // ---------------------------------------------------------------- plan / apply

        static string Apply(CliffCore308.Config cfg, string scenePath, bool dry)
        {
            var p = Load(); string key = CliffCore308.SceneKey(cfg, scenePath);
            var scene = Pieces308.OpenTarget(scenePath, p.Data["preview_roots"], !dry);
            var sb = new StringBuilder((dry ? "plan " : "apply ") + scenePath + "\n  data " + DataFile + " sha " + Pieces308.Short(p.Sha) + "\n");
            // gates (all reported in a plan; the first one refuses an apply)
            var gates = new List<string>();
            gates.AddRange(Stale(p).Select(s => "stale data: " + s + " - run `python Tools/Art/small308.py guk` and read its checks"));
            if (p.Failed.Length > 0) gates.Add("the offline checks of the data failed: " + string.Join(", ", p.Failed));
            var cb = CliffBoundary308.ReadLedger(cfg, scenePath);
            if (cb.state != "applied" || cb.stage != p.RequiredStage) gates.Add("the cliff ledger of this scene says '" + cb.state + (cb.stage != "" ? " " + cb.stage : "") + "', the pieces stand on stage " + p.RequiredStage + " (run tiles:" + key + ":stage=" + p.RequiredStage + " first)");
            else if (cb.heightSha256 != p.HeightSha) gates.Add("the scene's tiles were built from height " + Pieces308.Short(cb.heightSha256) + ", the pieces were designed on " + Pieces308.Short(p.HeightSha));
            var material = ByGuid<Material>(p.MaterialGuid, "prism material");
            if (PostLedger308.IsProtectedPath(AssetDatabase.GetAssetPath(material))) gates.Add("the prism material sits under a protected path");
            // what stands now
            var ledger = Pieces308.ReadLedger<Ledger>(LedgerName(key));
            var root = Pieces308.FindAll(scene, p.RootName); if (root.Count > 1) throw new PostLedger308.Refused(root.Count + " roots named " + p.RootName);
            var have = new[] { p.Up1Name, p.LipsName }.Where(g => root.Count == 1 && root[0].Find(g) != null).ToArray();
            if (have.Length > 0)
            {
                if (ledger != null && ledger.state == "applied" && ledger.dataSha256 == p.Sha && Matches(root[0], p, out string why))
                    return sb.Append("  변경 없음 (the pieces of this data stand in the scene; nothing written, scene not saved)").ToString();
                throw new PostLedger308.Refused(p.RootName + "/" + string.Join(", ", have) + " already stand in " + scenePath + (ledger == null ? " without a ledger" : " from data " + Pieces308.Short(ledger.dataSha256)) + ": run revert:" + key + " first (pieces are never moved silently)");
            }
            if (root.Count == 1 && PostLedger308.UnderProtectedTree(root[0])) throw new PostLedger308.Refused(p.RootName + " is under a protected tree");
            foreach (string g in p.Groups) sb.AppendLine("  group " + p.RootName + "/" + g + ": " + p.Pieces.Count(x => x.Group == g) + " prisms -> " + MeshAsset(cfg, p, g) + " (" + p.Pieces.Count(x => x.Group == g) * 12 + " triangles, 1 renderer, " + p.Pieces.Count(x => x.Group == g) + " BoxColliders)");
            foreach (var x in p.Pieces) sb.AppendLine("    " + x.Id + " [" + x.Role + (x.Standable ? ", standable" : "") + "] centre " + Pieces308.V(x.Centre) + " size " + Pieces308.V(x.Size) + " yaw " + Pieces308.F(x.Yaw, "F1") + " y " + Pieces308.F(x.Bottom) + ".." + Pieces308.F(x.Top));
            foreach (var c in p.Caps) sb.AppendLine("    cap " + c.Id + " on " + c.On + " at " + Pieces308.V(c.Position) + " scale " + Pieces308.V(c.Scale) + " (render only, LODGroup, no collider)");
            sb.AppendLine("  lift " + p.LiftId + ": Lower " + Pieces308.V(p.Lower + Vector3.up * p.NodeLift) + " Upper " + Pieces308.V(p.Upper + Vector3.up * p.NodeLift) + " rise " + Pieces308.F(p.Upper.y - p.Lower.y) + " m, component " + (p.LiftEnabledOnApply ? "enabled" : "DISABLED until lift-on"));
            foreach (string g in gates) sb.AppendLine("  GATE " + g);
            if (dry) return sb.Append(gates.Count == 0 ? "  apply would build this; nothing changed" : "  apply would be REFUSED on " + gates.Count + " gate(s); nothing changed").ToString();
            if (gates.Count > 0) throw new PostLedger308.Refused(gates[0] + (gates.Count > 1 ? " (+" + (gates.Count - 1) + " more: see plan:" + key + ")" : ""));

            string utc = PostLedger308.Utc(); string before = Pieces308.ShaAsset(scenePath);
            string backup = Pieces308.Backup(utc + "-guk-" + key, scenePath);
            var l = new Ledger { scene = scenePath, key = key, dataSha256 = p.Sha, heightSha256 = p.HeightSha, sceneShaBefore = before, backup = backup, liftId = p.LiftId, prisms = p.Pieces.Count, caps = p.Caps.Count };
            var created = new List<string>(); var assets = new List<string>();
            try
            {
                Transform rootT;
                if (root.Count == 1) rootT = root[0];
                else { var go = new GameObject(p.RootName); SceneManager.MoveGameObjectToScene(go, scene); rootT = go.transform; l.rootCreated = true; created.Add(p.RootName); }
                if (rootT.position != Vector3.zero || rootT.rotation != Quaternion.identity || rootT.lossyScale != Vector3.one) throw new PostLedger308.Refused(p.RootName + " is not at identity");
                foreach (string g in p.Groups)
                {
                    var group = Child(rootT, g).transform; created.Add(p.RootName + "/" + g);
                    string asset = MeshAsset(cfg, p, g); var mesh = Pieces308.SaveMesh(PrismMesh(p.Pieces.Where(x => x.Group == g)), asset, out _); assets.Add(asset);
                    var visual = Child(group, "Visual"); visual.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = visual.AddComponent<MeshRenderer>(); mr.sharedMaterial = material; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    var cols = Child(group, "Colliders").transform;
                    foreach (var x in p.Pieces.Where(x => x.Group == g))
                    {
                        var c = Child(cols, x.Id); c.transform.SetPositionAndRotation(x.Centre, Quaternion.Euler(0f, x.Yaw, 0f));
                        c.AddComponent<BoxCollider>().size = x.Size;
                    }
                    if (g != p.Up1Name) continue;
                    var site = Child(group, "Guk_" + p.LiftId).AddComponent<GukLiftSite>(); site.Id = p.LiftId; site.CastRadius = p.CastRadius; site.RiseSeconds = p.RiseSeconds;
                    site.Lower = Child(site.transform, "Lower").transform; site.Lower.position = p.Lower + Vector3.up * p.NodeLift;
                    site.Upper = Child(site.transform, "Upper").transform; site.Upper.position = p.Upper + Vector3.up * p.NodeLift;
                    if (!site.Valid) throw new PostLedger308.Refused("the lift site is not Valid on the declared coordinates (height " + Pieces308.F(site.Height) + " m)");
                    site.enabled = p.LiftEnabledOnApply; l.liftEnabled = p.LiftEnabledOnApply;
                    if (p.Caps.Count > 0)
                    {
                        var dress = Child(group, "Dressing").transform;
                        foreach (var c in p.Caps)
                        {
                            var cap = Child(dress, c.Id); cap.transform.SetPositionAndRotation(c.Position, Quaternion.Euler(0f, c.Yaw, 0f)); cap.transform.localScale = c.Scale;
                            var capMat = ByGuid<Material>(c.MatGuid, "cap material"); var lods = new List<LOD>();
                            foreach (var (name, guid, file, screen) in new[] { ("LOD0", c.Lod0Guid, c.Lod0File, c.Screen0), ("LOD1", c.Lod1Guid, c.Lod1File, c.Screen1) })
                            {
                                var m = CliffCore308.LoadMesh(guid, file); if (m == null) throw new PostLedger308.Refused("cap " + c.Id + ": mesh " + guid + ":" + file + " not found");
                                if (PostLedger308.IsProtectedPath(AssetDatabase.GetAssetPath(m))) throw new PostLedger308.Refused("cap mesh under a protected path");
                                var lod = Child(cap.transform, name); lod.AddComponent<MeshFilter>().sharedMesh = m; var r = lod.AddComponent<MeshRenderer>(); r.sharedMaterial = capMat;
                                lods.Add(new LOD(screen, new Renderer[] { r }));
                            }
                            var lg = cap.AddComponent<LODGroup>(); lg.SetLODs(lods.ToArray()); lg.RecalculateBounds();
                        }
                    }
                }
                Physics.SyncTransforms();
                Pieces308.SaveOrReload(scene);
            }
            catch
            {
                // nothing half-built stays in memory (objects made by script do not mark the scene dirty, so the reload is unconditional;
                // the scene was clean when the command started). A mesh asset already created is listed below.
                Pieces308.Reload(scene);
                foreach (string a in assets) Debug.LogWarning("CliffGuk308: apply failed after creating " + a + " (unreferenced; revert or a later apply reuses it)");
                throw;
            }
            l.state = "applied"; l.sceneShaAfter = Pieces308.ShaAsset(scenePath); l.created = created.ToArray(); l.assets = assets.ToArray();
            Note(l, "apply: " + p.Pieces.Count + " prisms, " + p.Caps.Count + " caps, lift " + p.LiftId + (l.liftEnabled ? " enabled" : " disabled"));
            Pieces308.WriteLedger(LedgerName(key), l);
            sb.AppendLine("  built: " + p.Pieces.Count + " prisms in " + p.Groups.Count() + " group(s), " + p.Caps.Count + " caps, lift site " + (l.liftEnabled ? "enabled" : "disabled"));
            sb.AppendLine("  scene sha " + Pieces308.Short(before) + " -> " + Pieces308.Short(l.sceneShaAfter) + " | backup " + backup + " | ledger " + Pieces308.OutFile(LedgerName(key)));
            sb.Append("  next: verify:" + key + " (the lift stays off until lift-on)");
            return sb.ToString();
        }

        /// <summary>The scene objects equal the data (transforms, collider sizes, lift nodes): the idempotence test of apply.</summary>
        static bool Matches(Transform root, Plan p, out string why)
        {
            why = "";
            foreach (string g in p.Groups)
            {
                var cols = root.Find(g + "/Colliders"); if (cols == null) { why = g + "/Colliders missing"; return false; }
                var want = p.Pieces.Where(x => x.Group == g).ToList();
                if (cols.childCount != want.Count) { why = g + ": " + cols.childCount + " colliders, data " + want.Count; return false; }
                foreach (var x in want)
                {
                    var t = cols.Find(x.Id); var b = t != null ? t.GetComponent<BoxCollider>() : null;
                    if (b == null || Vector3.Distance(t.position, x.Centre) > 1e-3f || Vector3.Distance(b.size, x.Size) > 1e-3f || Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y, x.Yaw)) > 1e-2f) { why = x.Id + " differs from the data"; return false; }
                }
                var mf = root.Find(g + "/Visual")?.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount != want.Count * 24) { why = g + "/Visual mesh differs"; return false; }
            }
            var site = root.Find(p.Up1Name + "/Guk_" + p.LiftId)?.GetComponent<GukLiftSite>();
            if (site == null || site.Lower == null || site.Upper == null || Vector3.Distance(site.Lower.position, p.Lower + Vector3.up * p.NodeLift) > 1e-3f || Vector3.Distance(site.Upper.position, p.Upper + Vector3.up * p.NodeLift) > 1e-3f) { why = "lift site differs"; return false; }
            return true;
        }

        // ---------------------------------------------------------------- lift on / off

        static string Lift(CliffCore308.Config cfg, string scenePath, bool on, Dictionary<string, string> opt)
        {
            var p = Load(); string key = CliffCore308.SceneKey(cfg, scenePath);
            var ledger = Pieces308.ReadLedger<Ledger>(LedgerName(key));
            if (ledger == null || ledger.state != "applied") throw new PostLedger308.Refused("no applied guk ledger for " + key);
            if (on)
            {
                foreach (string token in new[] { "joints", "rim", "content" })
                    if (!opt.TryGetValue(token, out string v) || v != "ok") throw new PostLedger308.Refused("lift-on needs " + token + "=ok (joints = forest joints-scene FAIL 0 in this scene; rim = the cb308 rim probe of the cliff-top field; content = the reward, the boundary stone and the view point of AC-B23 are in the content). They are the operator's statement: this command cannot measure them.");
                var rim = Pieces308.Req(p.Data, "rim_lips");
                if (!Pieces308.Flag(rim, "present") || !Pieces308.Arr(rim, "lips").Any()) throw new PostLedger308.Refused("no rim lip is authored (plan/lips308_1b.json): the E305_005 joint lip is the exit condition of the 1a exception");
                var stamp = Pieces308.ReadLedger<VerifyStamp>(VerifyName(key, "stamp.json"));
                if (stamp == null || !stamp.green) throw new PostLedger308.Refused("verify:" + key + " is not green (" + (stamp == null ? "never run" : stamp.fail + " FAIL") + ")");
                if (stamp.sceneSha256 != Pieces308.ShaAsset(scenePath) || stamp.dataSha256 != p.Sha) throw new PostLedger308.Refused("the scene or the data changed after the last green verify: run verify:" + key + " again");
            }
            var scene = Pieces308.OpenTarget(scenePath, p.Data["preview_roots"], true);
            var site = Pieces308.FindOne(scene, p.RootName + "/" + p.Up1Name + "/Guk_" + p.LiftId, "lift site").GetComponent<GukLiftSite>();
            if (site == null) throw new PostLedger308.Refused("no GukLiftSite on the lift object");
            if (site.enabled == on) return "변경 없음: lift " + p.LiftId + " is already " + (on ? "enabled" : "disabled") + " in " + scenePath + " (scene not saved)";
            if (on && !site.Valid) throw new PostLedger308.Refused("the lift site is not Valid");
            string utc = PostLedger308.Utc(); string backup = Pieces308.Backup(utc + "-lift-" + key, scenePath);
            site.enabled = on; EditorUtility.SetDirty(site); Pieces308.SaveOrReload(scene);
            ledger.liftEnabled = on; ledger.sceneShaAfter = Pieces308.ShaAsset(scenePath); Note(ledger, (on ? "lift-on" : "lift-off") + " (backup " + backup + ")");
            Pieces308.WriteLedger(LedgerName(key), ledger);
            return (on ? "lift-on " : "lift-off ") + scenePath + ": GukLiftSite " + p.LiftId + " " + (on ? "enabled" : "disabled") + " | scene sha " + Pieces308.Short(ledger.sceneShaAfter) + " | backup " + backup;
        }

        // ---------------------------------------------------------------- revert

        static string Revert(CliffCore308.Config cfg, string scenePath)
        {
            var p = Load(); string key = CliffCore308.SceneKey(cfg, scenePath);
            var ledger = Pieces308.ReadLedger<Ledger>(LedgerName(key));
            if (ledger == null) throw new PostLedger308.Refused("no ledger " + LedgerName(key) + " (nothing applied, or already reverted)");
            var scene = Pieces308.OpenTarget(scenePath, p.Data["preview_roots"], true);
            var sb = new StringBuilder("revert " + scenePath + "\n");
            if (Pieces308.ShaAsset(scenePath) != ledger.sceneShaAfter) sb.AppendLine("  note: the scene changed after this ledger's last write (another ledger wrote it); only the objects this ledger created are removed");
            string utc = PostLedger308.Utc(); string backup = Pieces308.Backup(utc + "-guk-revert-" + key, scenePath);
            int gone = 0, missing = 0;
            // children first (longest path first); the root only when this ledger created it and nothing else is under it
            foreach (string path in ledger.created.OrderByDescending(s => s.Count(ch => ch == '/')))
            {
                var hit = Pieces308.FindAll(scene, path);
                if (hit.Count == 0) { missing++; continue; }
                if (hit.Count > 1) throw new PostLedger308.Refused(hit.Count + " objects at " + path);
                if (PostLedger308.UnderProtectedTree(hit[0])) throw new PostLedger308.Refused(path + " is under a protected tree");
                if (!path.Contains("/"))
                {
                    if (hit[0].childCount > 0) { sb.AppendLine("  kept " + path + ": " + hit[0].childCount + " child object(s) of another package are under it"); continue; }
                }
                Object.DestroyImmediate(hit[0].gameObject); gone++;
            }
            Pieces308.SaveOrReload(scene);
            // mesh assets: only those no other scene ledger names
            var others = cfg.scenes.Where(s => s.key != key).Select(s => Pieces308.ReadLedger<Ledger>(LedgerName(s.key))).Where(l => l != null).SelectMany(l => l.assets).ToHashSet();
            foreach (string asset in ledger.assets)
            {
                if (others.Contains(asset)) { sb.AppendLine("  kept " + asset + " (another scene ledger names it)"); continue; }
                if (!asset.StartsWith(cfg.assetRoot + "/Meshes/Small/", StringComparison.Ordinal) || PostLedger308.IsProtectedPath(asset)) { sb.AppendLine("  kept " + asset + " (outside the small mesh folder)"); continue; }
                sb.AppendLine(AssetDatabase.DeleteAsset(asset) ? "  deleted " + asset : "  could not delete " + asset);
            }
            string archived = Pieces308.Archive(LedgerName(key));
            foreach (string ext in new[] { "stamp.json" }) { string f = Pieces308.OutFile(VerifyName(key, ext)); if (File.Exists(f)) File.Delete(f); }
            sb.AppendLine("  removed " + gone + " object(s), " + missing + " already gone; scene saved, sha " + Pieces308.Short(Pieces308.ShaAsset(scenePath)) + (Pieces308.ShaAsset(scenePath) == ledger.sceneShaBefore ? " (= the bytes before apply)" : " (before apply " + Pieces308.Short(ledger.sceneShaBefore) + ")"));
            sb.Append("  backup of the state before the revert " + backup + " | bytes before apply " + ledger.backup + " | ledger archived " + archived);
            return sb.ToString();
        }
    }
}
