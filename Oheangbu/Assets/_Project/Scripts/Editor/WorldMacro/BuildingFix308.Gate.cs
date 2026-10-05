using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-1 gate track (DEFECTS_308 "게이트 문틈 빛샘" 10 stills: capital_exit_2/3/5 + CapitalSouthGate253, and 0239 the
    // general's polearm). Own queue entry so BuildingFix308.cs stays untouched:
    //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.BuildingFix308 Gate "<command>"
    //   status                            config + ledger state (read only, no scene)
    //   plan:<alias>[:G1,G2,G3,G4]        read only -> Fix/gate-plan-<alias>.json
    //   apply:<alias>[:G1,…]              refused while an earlier scene of the 3-scene order lacks the step; backs the scene file up to SceneBackup/<alias>-<utc>-gate.unity, saves only that scene,
    //                                     appends Fix/gate-ledger-<alias>.json (SHA before/after). A second apply = no change.
    //   apply-all[:G1,…]                  plans the three scenes first (any blocker = nothing changes), then applies in the order
    //                                     architecture296 -> folklore298 -> main
    //   verify:<alias>                    read only post-conditions -> Fix/gate-verify-<alias>-<utc>.json
    //   revert:<alias>[:G1|G2|G3|G4]      ledger "before" values back, change by change (generated mesh assets stay on disk,
    //                                     unreferenced); composes with the other tracks' ledgers in any order
    // Finding (2026-10-03, offline, all three scenes; the gates are scene objects, there is no prefab):
    //   · the "#1 duplicates" of C4 are not copies. CompactArchitecture296.GateInfill builds one renderer per door material, so
    //     OriginalWood_ArchTransom/#1 and OriginalWood_HorizontalLintel/#1 share a name and an identity TRS but hold different
    //     meshes (gateN_panel_0/1, gateN_lintel_2/3). Deleting #1 would remove the iron-band half of the infill. G1 renames it.
    //   · the light gap is 0.285 m, not 0.11 m: the generator measured the leaf top at the hinge pin (y 8.205) while the leaf
    //     body ends at y 8.03, and the lintel starts at y 8.315. The leaves also stop 0.15 m short of each jamb (leaf edge
    //     x ±4.23, opening ±4.38). Both gaps are on every gate (same leaf, same arch).
    //   · the C4 coplanar faces of CAP_GATE_ARCH are contact faces of its three material shells (pier bottoms on the ground
    //     plane, parapet underside on the deck, two end plates), 24 triangles in all.
    //   · the polearm shaft mesh (VFX120_BambooBolt_001) has a stalk submesh and a leaf submesh; only the stalk has a material
    //     (C1 "null material slot 1"), and the stalk was scaled from the bounds that include the leaves, so it is 2.4 cm thick
    //     instead of the authored 6.5 cm — that is why the still shows a blade and no shaft.
    // Steps (values from BuildingAudit308/gate308.json, TEST):
    //   G1 names     the same-name infill siblings get unique names (<name>_Mat<k>); nothing is deleted.
    //   G2 frame     DoorFrame308 under each gate: a header and two jamb posts on the outer (non swing) face of the leaves,
    //                measured per gate from the arch, leaf and infill meshes (projected coverage of the opening must reach 0
    //                gap cells) and kept clear of every leaf pose 0..openDegrees. Renderer only: no collider, no NavMesh
    //                flag, the door's own material, no emission. One mesh asset per gate, shared by the three scenes.
    //   G3 arch      (the four gates + the leafless arches listed in arch.extra) private copies of the secondary arch shells with the coplanar contact faces pushed arch.offset along their
    //                normal (welded by position, so the shells stay closed); only the MeshFilter changes, the MeshColliders
    //                keep the source meshes (physics identical). The shared source meshes are never edited.
    //   G4 polearm   ReusedBambooShaft gets a stalk-only copy of its mesh (C1 passes without drawing bamboo leaves on a
    //                polearm) and the authored diameter.
    // Never touched: protected roots, Finish297_Attraction (hashed before/after), colliders, NavMeshObstacles, hinges and the
    // door components of the gates (hashed before/after; a difference fails the apply before the scene is saved).
    // AssetDatabase.SaveAssets is never called (new meshes use SaveAssetIfDirty); no dialog; no runtime (App) change.
    public static partial class BuildingFix308
    {
        static string GateConfigFile => Path.Combine(BuildingAudit308.Folder, "gate308.json");
        static string GateLedgerFile(string alias) => Path.Combine(FixDir, "gate-ledger-" + alias + ".json");
        static readonly string[] GateSteps = { "G1", "G2", "G3", "G4" };
        static readonly string[] GateOrder = { "architecture296", "folklore298", "main" };
        const string GateUsage = "refused: BuildingFix308.Gate status | plan:<alias>[:G1,G2,G3,G4] | apply:<alias>[:G1,…] | apply-all[:G1,…] | verify:<alias> | revert:<alias>[:G1|G2|G3|G4]";

        // ------------------------------------------------------------------ config (BuildingAudit308/gate308.json)

        [Serializable] internal sealed class GateSite308 { public string id = "", gate = "", architecture = "", arch = ""; }
        [Serializable] internal sealed class GateNames308 { public string holder = "", suffix = ""; }
        [Serializable] internal sealed class GateFrame308
        {
            public string name = "", meshFolder = "", material = "", leftHinge = "", rightHinge = "";
            public float cell, halfWidth, floor, top, seedY, overlapY, overlapTop, overlapX, embed, sink, thickness, clearance, sweepStep, sweepMargin, openDegrees, maxHeader, matchTolerance;
        }
        // extra = arches without leaves that use the same shells (the open precinct gates): G3 only
        [Serializable] internal sealed class GateArch308 { public string reference = "", meshFolder = "", suffix = ""; public string[] others = Array.Empty<string>(); public GateSite308[] extra = Array.Empty<GateSite308>(); public float offset, distance, normalTol, weld; }
        [Serializable] internal sealed class GatePolearm308 { public string root = "", part = "", meshFolder = ""; public int keepSubmesh; public float diameter, diameterTolerance; }
        [Serializable] internal sealed class GateCfg308
        {
            public string version = "", status = "";
            public GateSite308[] gates = Array.Empty<GateSite308>();
            public GateNames308 names = new GateNames308(); public GateFrame308 frame = new GateFrame308(); public GateArch308 arch = new GateArch308(); public GatePolearm308 polearm = new GatePolearm308();
        }

        static GateCfg308 GateLoad(out string error)
        {
            error = null;
            if (!File.Exists(GateConfigFile)) { error = "config missing: " + GateConfigFile; return null; }
            GateCfg308 g;
            try { g = JsonUtility.FromJson<GateCfg308>(File.ReadAllText(GateConfigFile).TrimStart('\uFEFF')); }
            catch (Exception e) { error = "gate308.json unreadable: " + e.Message; return null; }
            if (g == null || g.gates.Length == 0 || g.gates.Any(s => string.IsNullOrEmpty(s.id) || string.IsNullOrEmpty(s.gate))) { error = "gate308.json has no gates (id + gate path)"; return null; }
            if (g.gates.Select(s => s.id).Distinct().Count() != g.gates.Length) { error = "gate308.json gate ids are not unique"; return null; }
            var f = g.frame; var a = g.arch;
            if (string.IsNullOrEmpty(g.names.holder) || string.IsNullOrEmpty(g.names.suffix)) { error = "gate308.json names.holder / names.suffix missing"; return null; }
            if (string.IsNullOrEmpty(f.name) || string.IsNullOrEmpty(f.meshFolder) || f.cell <= 0 || f.halfWidth <= 0 || f.top <= f.floor || f.seedY <= f.floor || f.seedY >= f.top || f.thickness <= 0 || f.clearance <= 0 || f.sweepStep <= 0 || f.maxHeader <= 0 || f.matchTolerance <= 0)
            { error = "gate308.json frame values incomplete (name, meshFolder, cell, halfWidth, floor < seedY < top, thickness, clearance, sweepStep, maxHeader, matchTolerance)"; return null; }
            if (string.IsNullOrEmpty(a.reference) || string.IsNullOrEmpty(a.meshFolder) || string.IsNullOrEmpty(a.suffix) || a.distance <= 0 || a.offset <= a.distance * 2f || a.weld <= 0 || a.normalTol <= 0)
            { error = "gate308.json arch values incomplete (reference, meshFolder, suffix, distance, offset > 2 x distance, weld, normalTol)"; return null; }
            if (a.extra.Any(x => string.IsNullOrEmpty(x.id) || string.IsNullOrEmpty(x.architecture) || string.IsNullOrEmpty(x.arch)) || a.extra.Select(x => x.id).Concat(g.gates.Select(x => x.id)).Distinct().Count() != a.extra.Length + g.gates.Length)
            { error = "gate308.json arch.extra entries need a unique id, architecture and arch"; return null; }
            if (!string.IsNullOrEmpty(g.polearm.root) && (string.IsNullOrEmpty(g.polearm.part) || string.IsNullOrEmpty(g.polearm.meshFolder))) { error = "gate308.json polearm part / meshFolder missing"; return null; }
            return g;
        }

        sealed class GateCtx308
        {
            public BuildingAudit308.Config308 cfg; public GateCfg308 g; public Scene scene;
            // the measurement of each gate made by the plan, reused by the apply of the same call (nothing survives the call)
            public readonly Dictionary<string, GateFrameDesign308> frames = new Dictionary<string, GateFrameDesign308>();
        }

        static Ledger308 GateLoadLedger(string alias)
        {
            string f = GateLedgerFile(alias);
            return File.Exists(f) ? JsonUtility.FromJson<Ledger308>(File.ReadAllText(f)) ?? new Ledger308 { alias = alias } : new Ledger308 { alias = alias };
        }
        static void GateSaveLedger(Ledger308 l) { Directory.CreateDirectory(FixDir); File.WriteAllText(GateLedgerFile(l.alias), JsonUtility.ToJson(l, true), new UTF8Encoding(false)); }

        // ------------------------------------------------------------------ entry

        public static string Gate(string command)
        {
            command = (command ?? "").Trim();
            try
            {
                var a = command.Split(':');
                string[] Steps(int i) => a.Length > i && a[i].Length > 0 ? a[i].Split(',').Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0).ToArray() : GateSteps;
                switch (a[0])
                {
                    case "status": return GateStatusCommand();
                    case "plan": return a.Length > 1 ? GatePlanCommand(a[1], Steps(2)) : GateUsage;
                    case "apply": return a.Length > 1 ? GateApply(a[1], Steps(2), command) : GateUsage;
                    case "apply-all": return GateApplyAll(Steps(1));
                    case "verify": return a.Length > 1 ? GateVerify(a[1]) : GateUsage;
                    case "revert": return a.Length > 1 ? GateRevert(a[1], a.Length > 2 && a[2].Length > 0 ? a[2].Trim().ToUpperInvariant() : null) : GateUsage;
                    default: return GateUsage;
                }
            }
            catch (Exception e) { return "FAILED: " + e; }
        }

        // the gate steps whose newest ledger state in a scene is applied / already / clean / absent (replayed in time order; a
        // later revert takes its steps back out)
        static HashSet<string> GateStepsDone(Ledger308 l)
        {
            var done = new HashSet<string>();
            foreach (var op in l.ops)
            {
                if (op.command.StartsWith("gate revert:", StringComparison.Ordinal)) { if (op.status == "reverted") foreach (var s in op.steps) done.Remove(s); continue; }
                if (op.status != "applied" && op.status != "already") continue;
                foreach (var st in GateSteps)
                    if (op.stepResults.Any(r => r.StartsWith(st + " applied", StringComparison.Ordinal) || r.StartsWith(st + " already", StringComparison.Ordinal) || r.StartsWith(st + " clean", StringComparison.Ordinal) || r.StartsWith(st + " absent", StringComparison.Ordinal)))
                        done.Add(st);
            }
            return done;
        }

        // 3-scene order (#296 candidate -> #298 candidate -> W_Demo_Main): a scene takes a step only after every earlier scene has it
        static string GateOrderRefusal(string alias, string[] steps)
        {
            int at = Array.IndexOf(GateOrder, alias);
            for (int i = 0; i < at; i++)
            {
                var done = File.Exists(GateLedgerFile(GateOrder[i])) ? GateStepsDone(GateLoadLedger(GateOrder[i])) : new HashSet<string>();
                var missing = steps.Where(s => !done.Contains(s)).ToArray();
                if (missing.Length > 0) return "refused: 3-scene order — " + GateOrder[i] + " has no applied gate op for " + string.Join(",", missing) + "; run Gate apply:" + GateOrder[i] + " first, or Gate apply-all";
            }
            return null;
        }

        static string GateBadSteps(string[] steps)
        {
            var bad = steps.Where(s => !GateSteps.Contains(s)).ToArray();
            return bad.Length > 0 ? "refused: gate steps are G1 G2 G3 G4 (" + string.Join(",", bad) + ")" : null;
        }

        static string GateStatusCommand()
        {
            var g = GateLoad(out string err);
            var sb = new StringBuilder("BuildingFix308.Gate status " + BuildingAudit308.Utc() + "\n");
            sb.AppendLine(g == null ? "config: " + err : "config " + g.version + " (" + GateConfigFile + "): " + g.gates.Length + " gate(s) " + string.Join(", ", g.gates.Select(s => s.id)));
            foreach (var alias in GateOrder)
            {
                if (!File.Exists(GateLedgerFile(alias))) { sb.AppendLine("  " + alias + ": no ledger"); continue; }
                var l = GateLoadLedger(alias);
                int live = l.ops.Where(o => !o.reverted).Sum(o => o.changes.Count(c => c.state == "apply"));
                var last = l.ops.LastOrDefault();
                sb.AppendLine("  " + alias + ": " + l.ops.Count + " op(s), " + live + " live change(s)" + (last != null ? ", last " + last.utc + " " + last.command + " -> " + last.status : ""));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ plan

        static string GatePlanCommand(string alias, string[] steps)
        {
            string bad = GateBadSteps(steps); if (bad != null) return bad;
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var g = GateLoad(out err); if (g == null) return "refused: " + err;
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            try
            {
                var plan = GateMakePlan(new GateCtx308 { cfg = cfg, g = g, scene = scene }, alias, steps);
                GateWritePlan(plan);
                return GatePlanText(plan);
            }
            finally { GoBack(previous, scene, opened, true); }
        }

        static Plan308 GateMakePlan(GateCtx308 ctx, string alias, string[] steps)
        {
            var plan = new Plan308 { alias = alias, scene = ctx.scene.path, utc = BuildingAudit308.Utc(), sceneSha = BuildingAudit308.Sha(BuildingAudit308.Abs(ctx.scene.path)), configVersion = ctx.g.version, quality = BuildingAudit308.QualityName() };
            foreach (var s in steps)
            {
                StepPlan308 sp;
                try
                {
                    switch (s)
                    {
                        case "G1": sp = GatePlanNames(ctx); break;
                        case "G2": sp = GatePlanFrame(ctx); break;
                        case "G3": sp = GatePlanArch(ctx); break;
                        case "G4": sp = GatePlanPolearm(ctx); break;
                        default: sp = new StepPlan308 { step = s, status = "blocked", detail = "unknown step (G1 names, G2 door frame, G3 arch coplanar faces, G4 polearm shaft)" }; break;
                    }
                }
                catch (Exception e) { sp = new StepPlan308 { step = s, status = "blocked", detail = e.GetType().Name + ": " + e.Message }; }
                // protected objects and assets are never written
                foreach (var c in sp.changes.Where(c => c.state == "apply"))
                {
                    string root = c.key.Split('/')[0]; int h = root.LastIndexOf('#'); if (h > 0) root = root.Substring(0, h);
                    bool prot = ctx.cfg.ProtectedRoot(root) || (!string.IsNullOrEmpty(ctx.cfg.attractionRoot) && root == ctx.cfg.attractionRoot) || (c.kind == "create-mesh" && ctx.cfg.ProtectedAsset(c.after));
                    if (prot) { c.state = "blocked"; sp.notes.Add("protected: " + c.key + (c.kind == "create-mesh" ? " -> " + c.after : "")); }
                }
                GateFinish(sp);
                plan.steps.Add(sp);
                if (sp.status == "blocked" || sp.status == "mismatch") { plan.blocked = true; plan.blockers.Add(s + " " + sp.status + ": " + sp.detail); }
            }
            return plan;
        }

        // status from the change states unless the planner already set blocked / mismatch / absent
        static void GateFinish(StepPlan308 sp)
        {
            int apply = sp.changes.Count(c => c.state == "apply"), already = sp.changes.Count(c => c.state == "already");
            if (sp.changes.Any(c => c.state == "blocked")) sp.status = "blocked";
            else if (sp.status == "blocked") { }
            else if (sp.changes.Any(c => c.state == "mismatch") || sp.status == "mismatch") sp.status = "mismatch";
            else if (apply > 0) sp.status = "ready";
            else if (already > 0) sp.status = "already";
            else if (string.IsNullOrEmpty(sp.status)) sp.status = "clean";
            string counts = apply + " to apply, " + already + " already";
            sp.detail = string.IsNullOrEmpty(sp.detail) ? counts : sp.detail + " (" + counts + ")";
        }

        static void GateWritePlan(Plan308 p) { Directory.CreateDirectory(FixDir); File.WriteAllText(Path.Combine(FixDir, "gate-plan-" + p.alias + ".json"), JsonUtility.ToJson(p, true), new UTF8Encoding(false)); }

        static string GatePlanText(Plan308 p)
        {
            var sb = new StringBuilder("gate plan " + p.alias + " (" + p.scene + ") sha " + p.sceneSha + " config " + p.configVersion + (p.blocked ? " — BLOCKED" : "") + "\n");
            foreach (var s in p.steps)
            {
                sb.AppendLine("  " + s.step + " " + s.status + ": " + s.detail);
                foreach (var n in s.notes.Take(16)) sb.AppendLine("     · " + n);
            }
            foreach (var b in p.blockers) sb.AppendLine("  blocker: " + b);
            sb.AppendLine("  total " + p.steps.Sum(s => s.changes.Count(c => c.state == "apply")) + " change(s) to apply");
            sb.AppendLine("-> " + Path.Combine(FixDir, "gate-plan-" + p.alias + ".json"));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ G1 names

        // key = the holder; the child is found by its mesh (two children share the name before the rename)
        static StepPlan308 GatePlanNames(GateCtx308 ctx)
        {
            var nc = ctx.g.names;
            var sp = new StepPlan308 { step = "G1", target = nc.holder };
            int holders = 0;
            foreach (var site in ctx.g.gates)
            {
                var gate = BuildingAudit308.Resolve(ctx.scene, site.gate);
                var holder = gate != null ? gate.Find(nc.holder) : null;
                if (holder == null) { sp.notes.Add(site.id + ": no " + nc.holder + " in this scene"); continue; }
                holders++;
                string hk = BuildingAudit308.KeyOf(holder);
                var kids = new List<Transform>();
                for (int i = 0; i < holder.childCount; i++) kids.Add(holder.GetChild(i));
                var names = new HashSet<string>(kids.Select(k => k.name));
                foreach (var grp in kids.GroupBy(k => k.name).Where(x => x.Count() > 1))
                {
                    var list = grp.ToList();
                    for (int k = 1; k < list.Count; k++)
                    {
                        var t = list[k];
                        string id = MeshState(t.GetComponent<MeshFilter>()), to = grp.Key + nc.suffix + k;
                        var c = new Change308 { step = "G1", key = hk, kind = "name", before = grp.Key, after = to, note = id, at = t.position, state = "apply" };
                        if (names.Contains(to)) { c.state = "mismatch"; sp.notes.Add(site.id + ": " + to + " already exists next to a second " + grp.Key); }
                        // same mesh as a same-name sibling = a real copy: that is a delete case, not a rename (review by hand)
                        else if (list.Count(x => MeshState(x.GetComponent<MeshFilter>()) == id) != 1) { c.state = "mismatch"; sp.notes.Add(site.id + ": " + grp.Key + "#" + k + " shares its mesh with a same-name sibling (a real copy — not renamed)"); }
                        sp.changes.Add(c);
                    }
                    sp.notes.Add(site.id + ": " + list.Count + " x " + grp.Key + " with different meshes (" + string.Join(", ", list.Select(x => { var f = x.GetComponent<MeshFilter>(); return f != null && f.sharedMesh != null ? f.sharedMesh.name : "no mesh"; })) + ")");
                }
                foreach (var t in kids)
                {
                    int at = t.name.LastIndexOf(nc.suffix, StringComparison.Ordinal);
                    if (at <= 0 || !int.TryParse(t.name.Substring(at + nc.suffix.Length), NumberStyles.Integer, Inv, out _)) continue;
                    string baseName = t.name.Substring(0, at);
                    if (!names.Contains(baseName)) continue;
                    sp.changes.Add(new Change308 { step = "G1", key = hk, kind = "name", before = baseName, after = t.name, note = MeshState(t.GetComponent<MeshFilter>()), at = t.position, state = "already" });
                }
            }
            if (holders == 0) sp.status = "absent";
            sp.detail = "same-name siblings under " + nc.holder + " get <name>" + nc.suffix + "<k> (nothing deleted)";
            return sp;
        }

        // ------------------------------------------------------------------ G4 polearm

        static string GateTrs(Transform t) => GateTrs(t.localPosition, t.localRotation, t.localScale);
        static string GateTrs(Vector3 p, Quaternion q, Vector3 s) => string.Format(Inv, "{0:R},{1:R},{2:R}|{3:R},{4:R},{5:R},{6:R}|{7:R},{8:R},{9:R}", p.x, p.y, p.z, q.x, q.y, q.z, q.w, s.x, s.y, s.z);
        static bool GateParseTrs(string text, out Vector3 p, out Quaternion q, out Vector3 s)
        {
            p = Vector3.zero; q = Quaternion.identity; s = Vector3.one;
            var parts = (text ?? "").Split('|'); if (parts.Length != 3) return false;
            var a = parts[0].Split(',').Select(x => float.Parse(x, Inv)).ToArray(); var b = parts[1].Split(',').Select(x => float.Parse(x, Inv)).ToArray(); var c = parts[2].Split(',').Select(x => float.Parse(x, Inv)).ToArray();
            if (a.Length != 3 || b.Length != 4 || c.Length != 3) return false;
            p = new Vector3(a[0], a[1], a[2]); q = new Quaternion(b[0], b[1], b[2], b[3]); s = new Vector3(c[0], c[1], c[2]); return true;
        }
        static string GateMats(Material[] mats) => string.Join("|", mats.Select(m => m == null ? "" : AssetDatabase.GetAssetPath(m)));
        static Mesh GateLoadMesh(string state)
        {
            if (string.IsNullOrEmpty(state)) return null;
            string path = state.Split('#')[0];
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault(m => state.EndsWith("#" + m.name, StringComparison.Ordinal)) ?? AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        // largest cross-section of the shaft in its parent's units (the long axis is the one DemoSouthGateSceneAuthoring.MeshPart picked)
        static float GateShaftDiameter(Transform t, Bounds b, int axis)
        {
            var s = t.localScale;
            return axis == 0 ? Mathf.Max(b.size.y * Mathf.Abs(s.y), b.size.z * Mathf.Abs(s.z)) : axis == 1 ? Mathf.Max(b.size.x * Mathf.Abs(s.x), b.size.z * Mathf.Abs(s.z)) : Mathf.Max(b.size.x * Mathf.Abs(s.x), b.size.y * Mathf.Abs(s.y));
        }
        static int GateLongAxis(Bounds b) => b.size.y >= b.size.x && b.size.y >= b.size.z ? 1 : b.size.z >= b.size.x ? 2 : 0;

        static StepPlan308 GatePlanPolearm(GateCtx308 ctx)
        {
            var p = ctx.g.polearm;
            var sp = new StepPlan308 { step = "G4", target = p.root + "/…/" + p.part };
            if (string.IsNullOrEmpty(p.root)) { sp.status = "absent"; sp.detail = "no polearm configured"; return sp; }
            var root = BuildingAudit308.Resolve(ctx.scene, p.root);
            if (root == null) { sp.status = "absent"; sp.detail = p.root + " is not in this scene"; return sp; }
            var parts = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == p.part).ToList();
            if (parts.Count == 0) { sp.status = "absent"; sp.detail = "no " + p.part + " under " + p.root; return sp; }
            if (parts.Count > 1) { sp.status = "blocked"; sp.detail = parts.Count + " objects named " + p.part + " under " + p.root; return sp; }
            var shaft = parts[0]; var mf = shaft.GetComponent<MeshFilter>(); var mr = shaft.GetComponent<MeshRenderer>();
            if (mf == null || mr == null || mf.sharedMesh == null) { sp.status = "blocked"; sp.detail = p.part + " has no mesh / renderer"; return sp; }
            var src = mf.sharedMesh; var mats = mr.sharedMaterials; string key = BuildingAudit308.KeyOf(shaft), srcPath = AssetDatabase.GetAssetPath(src), folder = p.meshFolder.TrimEnd('/');
            bool slots = mats.Length >= src.subMeshCount && mats.Take(src.subMeshCount).All(m => m != null);
            sp.detail = "stalk-only shaft mesh (every submesh has a material)" + (p.diameter > 0 ? " + authored diameter " + BuildingAudit308.F(p.diameter) + " m" : "");
            if (srcPath.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
            {
                var c = new Change308 { step = "G4", key = key, kind = "mesh", before = "", after = MeshState(mf), at = shaft.position, state = "already" };
                float now = GateShaftDiameter(shaft, src.bounds, GateLongAxis(src.bounds));
                c.note = "diameter " + BuildingAudit308.F(now) + " m, " + mats.Length + " material(s) for " + src.subMeshCount + " submesh(es)";
                if (!slots) { c.state = "mismatch"; sp.notes.Add("the stalk mesh is in place but a material slot is empty"); }
                sp.changes.Add(c);
                return sp;
            }
            if (slots) { sp.notes.Add("every submesh of " + src.name + " has a material — nothing to do"); return sp; }
            int keep = p.keepSubmesh;
            if (keep < 0 || keep >= src.subMeshCount) { sp.status = "blocked"; sp.detail = "keepSubmesh " + keep + " is outside " + src.name + " (" + src.subMeshCount + " submeshes)"; return sp; }
            var keepMat = keep < mats.Length ? mats[keep] : null;
            if (keepMat == null) { sp.status = "blocked"; sp.detail = "the kept submesh " + keep + " has no material either"; return sp; }
            var stalk = GateSubmeshMesh(src, keep);
            try
            {
                string path = folder + "/" + src.name + "_sub" + keep + "_308.asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing != null)
                {
                    if (existing.vertexCount != stalk.vertexCount || existing.subMeshCount != 1)
                    { sp.changes.Add(new Change308 { step = "G4", key = key, kind = "create-mesh", after = path, state = "mismatch", note = "stalk:" + srcPath + "#" + src.name }); sp.notes.Add(path + " exists with " + existing.vertexCount + " vertices, expected " + stalk.vertexCount); return sp; }
                }
                else sp.changes.Add(new Change308 { step = "G4", key = key, kind = "create-mesh", after = path, state = "apply", at = shaft.position, note = "stalk:" + srcPath + "#" + src.name });
                sp.changes.Add(new Change308 { step = "G4", key = key, kind = "mesh", before = MeshState(mf), after = path + "#" + Path.GetFileNameWithoutExtension(path), state = "apply", at = shaft.position, note = "submesh " + keep + " of " + src.subMeshCount + " (" + stalk.vertexCount + " of " + src.vertexCount + " vertices)" });
                if (mats.Length != 1) sp.changes.Add(new Change308 { step = "G4", key = key, kind = "materials", before = GateMats(mats), after = GateMats(new[] { keepMat }), state = "apply", at = shaft.position });
                int axis = GateLongAxis(src.bounds);
                float was = GateShaftDiameter(shaft, stalk.bounds, axis);
                sp.notes.Add(src.name + ": " + src.subMeshCount + " submeshes, " + mats.Length + " material(s); stalk diameter now " + BuildingAudit308.F(was) + " m");
                if (p.diameter > 0)
                {
                    var nb = stalk.bounds; var sb = src.bounds;
                    float cross = axis == 0 ? Mathf.Max(nb.size.y, nb.size.z) : axis == 1 ? Mathf.Max(nb.size.x, nb.size.z) : Mathf.Max(nb.size.x, nb.size.y);
                    var s0 = shaft.localScale; var s1 = Vector3.one * (p.diameter / Mathf.Max(.0001f, cross)); s1[axis] = s0[axis];
                    // MeshPart: position = anchor - rotation * (bounds centre * scale); the anchor on the hand stays where it is
                    var p1 = shaft.localPosition + shaft.localRotation * (Vector3.Scale(sb.center, s0) - Vector3.Scale(nb.center, s1));
                    if (Vector3.Distance(p1, shaft.localPosition) > 1e-5f || Vector3.Distance(s1, s0) > 1e-5f)
                        sp.changes.Add(new Change308 { step = "G4", key = key, kind = "trs", before = GateTrs(shaft), after = GateTrs(p1, shaft.localRotation, s1), state = "apply", at = shaft.position, note = "diameter " + BuildingAudit308.F(was) + " -> " + BuildingAudit308.F(p.diameter) + " m, length unchanged" });
                }
            }
            finally { Object.DestroyImmediate(stalk); }
            return sp;
        }

        // ------------------------------------------------------------------ guard: colliders, obstacles, hinges, door components

        static string GateGuard(GateCtx308 ctx)
        {
            var sb = new StringBuilder();
            foreach (var site in ctx.g.gates.Concat(ctx.g.arch.extra))
                foreach (var path in new[] { site.gate, site.architecture })
                {
                    var root = string.IsNullOrEmpty(path) ? null : BuildingAudit308.Resolve(ctx.scene, path);
                    if (root == null) { sb.Append(path).Append("|absent\n"); continue; }
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.parent == root && (t.name == ctx.g.frame.leftHinge || t.name == ctx.g.frame.rightHinge)) sb.Append(t.name).Append('|').Append(GateTrs(t)).Append('\n');
                        foreach (var c in t.GetComponents<Component>())
                            if (c is Collider || c is NavMeshObstacle || c is MonoBehaviour)
                                sb.Append(c.GetType().Name).Append('|').Append(EditorJsonUtility.ToJson(c)).Append('\n');
                    }
                }
            return BuildingAudit308.ShaText(sb.ToString());
        }

        // ------------------------------------------------------------------ apply

        static string GateApply(string alias, string[] steps, string commandText)
        {
            string bad = GateBadSteps(steps); if (bad != null) return bad;
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var g = GateLoad(out err); if (g == null) return "refused: " + err;
            string order = GateOrderRefusal(alias, steps); if (order != null) return order;
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            var op = new Op308 { utc = BuildingAudit308.Utc(), command = "gate " + commandText, scene = scene.path, alias = alias, steps = steps, quality = BuildingAudit308.QualityName() };
            try
            {
                var ctx = new GateCtx308 { cfg = cfg, g = g, scene = scene };
                var plan = GateMakePlan(ctx, alias, steps);
                GateWritePlan(plan);
                if (plan.blocked) { GoBack(previous, scene, opened, true); return "refused: nothing changed — " + string.Join(" | ", plan.blockers) + "\n" + GatePlanText(plan); }
                var todo = plan.steps.SelectMany(s => s.changes).Where(c => c.state == "apply").ToList();
                op.attractionBefore = AttractionHash(scene, cfg.attractionRoot);
                op.shaBefore = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path));
                op.changes.AddRange(plan.steps.SelectMany(s => s.changes));
                op.stepResults = plan.steps.Select(s => s.step + " " + (s.changes.Any(c => c.state == "apply") ? "applied" : s.status) + ": " + s.detail).ToList();
                if (todo.Count == 0)
                {
                    op.status = "already"; op.detail = "no change (idempotent); scene not saved"; op.shaAfter = op.shaBefore; op.attractionAfter = op.attractionBefore;
                    var ledger0 = GateLoadLedger(alias); ledger0.scene = scene.path; ledger0.ops.Add(op); GateSaveLedger(ledger0);
                    GoBack(previous, scene, opened, true);
                    return "already: gate " + alias + " — no change, scene SHA " + op.shaBefore + "\n" + string.Join("\n", op.stepResults);
                }
                string backupDir = Path.Combine(BuildingAudit308.Folder, "SceneBackup"); Directory.CreateDirectory(backupDir);
                op.backup = Path.Combine(backupDir, alias + "-" + op.utc + "-gate.unity");
                File.Copy(BuildingAudit308.Abs(scene.path), op.backup, false);
                string guard = GateGuard(ctx);
                var created = new List<Object>();
                foreach (var c in todo) GateExecute(ctx, c, created);
                foreach (var o in created) if (o != null && AssetDatabase.Contains(o)) AssetDatabase.SaveAssetIfDirty(o);
                // colliders, NavMeshObstacles, hinges and door components of the gates must come out exactly as they went in
                if (GateGuard(ctx) != guard) throw new InvalidOperationException("gate colliders / hinges / door components changed during the apply (guard hash differs)");
                ClearStaleSuffix(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) { op.status = "FAILED"; op.detail = "SaveScene returned false; the scene stays open and dirty; backup " + op.backup; }
                else
                {
                    op.shaAfter = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path));
                    op.attractionAfter = AttractionHash(scene, cfg.attractionRoot);
                    op.status = op.attractionAfter == op.attractionBefore ? "applied" : "FAILED";
                    op.detail = todo.Count + " change(s) saved; gate guard hash " + guard.Substring(0, 12) + " unchanged" + (op.attractionAfter == op.attractionBefore ? "" : "; Finish297_Attraction block CHANGED — revert with Gate revert:" + alias);
                }
                var ledger = GateLoadLedger(alias); ledger.scene = scene.path; ledger.ops.Add(op); GateSaveLedger(ledger);
                GoBack(previous, scene, opened);
                return op.status + ": gate " + alias + " sha " + op.shaBefore + " -> " + op.shaAfter + ", backup " + op.backup + "\n" + string.Join("\n", op.stepResults) + "\n" + op.detail;
            }
            catch (Exception e)
            {
                op.status = "FAILED"; op.detail = e.ToString();
                // nothing was saved: drop the half-applied in-memory edits by reloading the file so the editor is left clean
                string discarded = GateDiscard(scene, previous, opened, string.IsNullOrEmpty(op.shaAfter));
                op.detail += discarded;
                var ledger = GateLoadLedger(alias); ledger.ops.Add(op); GateSaveLedger(ledger);
                return "FAILED: gate " + alias + " — " + e.Message + discarded + (string.IsNullOrEmpty(op.backup) ? "" : " (backup " + op.backup + ")");
            }
        }

        static string GateDiscard(Scene scene, string previous, bool opened, bool unsaved)
        {
            try
            {
                if (scene.IsValid() && scene.isDirty && unsaved)
                {
                    string path = scene.path;
                    EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    if (opened && !string.IsNullOrEmpty(previous) && previous != path && File.Exists(BuildingAudit308.Abs(previous))) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                    return "; unsaved edits discarded (scene reloaded from disk, SHA " + BuildingAudit308.Sha(BuildingAudit308.Abs(path)) + ")";
                }
            }
            catch (Exception e2) { return "; reload failed, the scene is still open and dirty: " + e2.Message; }
            return "";
        }

        static string GateApplyAll(string[] steps)
        {
            string bad = GateBadSteps(steps); if (bad != null) return bad;
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var g = GateLoad(out err); if (g == null) return "refused: " + err;
            var lines = new List<string>();
            // plan every scene first: one blocker anywhere = nothing changes anywhere
            foreach (var alias in GateOrder)
            {
                string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
                if (refuse != null) return refuse;
                Plan308 plan;
                try { plan = GateMakePlan(new GateCtx308 { cfg = cfg, g = g, scene = scene }, alias, steps); GateWritePlan(plan); }
                finally { GoBack(previous, scene, opened, true); }
                lines.Add(GatePlanText(plan).TrimEnd());
                if (plan.blocked) return "refused: gate apply-all stopped before any change — " + alias + " has blockers\n" + string.Join("\n", lines);
            }
            foreach (var alias in GateOrder)
            {
                string r = GateApply(alias, steps, "apply-all");
                lines.Add(alias + ": " + r.Split('\n')[0]);
                if (r.StartsWith("refused", StringComparison.Ordinal) || r.StartsWith("FAILED", StringComparison.Ordinal)) { lines.Add("stopped at " + alias); break; }
            }
            return string.Join("\n", lines);
        }

        // ------------------------------------------------------------------ execute / undo one change

        static Transform GateNamedChild(Transform holder, string name, string meshState)
        {
            for (int i = 0; i < holder.childCount; i++)
            {
                var ch = holder.GetChild(i);
                if (ch.name == name && MeshState(ch.GetComponent<MeshFilter>()) == meshState) return ch;
            }
            return null;
        }

        static void GateSetMaterials(Transform t, string state)
        {
            var mr = t.GetComponent<MeshRenderer>() ?? throw new InvalidOperationException("no MeshRenderer on " + BuildingAudit308.PathOf(t));
            mr.sharedMaterials = (state ?? "").Split('|').Select(p => string.IsNullOrEmpty(p) ? null : AssetDatabase.LoadAssetAtPath<Material>(p) ?? throw new InvalidOperationException("material missing " + p)).ToArray();
            EditorUtility.SetDirty(mr);
        }

        static void GateExecute(GateCtx308 ctx, Change308 c, List<Object> created)
        {
            var t = BuildingAudit308.Resolve(ctx.scene, c.key) ?? throw new InvalidOperationException("missing " + c.key);
            switch (c.kind)
            {
                case "name":
                {
                    var child = GateNamedChild(t, c.before, c.note) ?? throw new InvalidOperationException("no child " + c.before + " with mesh " + c.note + " under " + c.key);
                    child.name = c.after; EditorUtility.SetDirty(child.gameObject);
                    break;
                }
                case "create-mesh":
                {
                    if (AssetDatabase.LoadAssetAtPath<Mesh>(c.after) != null) break;
                    Mesh mesh;
                    if (c.note.StartsWith("frame:", StringComparison.Ordinal))
                    {
                        if (!ctx.frames.TryGetValue(c.key, out var design) || design == null || design.error.Length > 0 || design.rects.Count == 0) throw new InvalidOperationException("no frame measurement for " + c.key);
                        mesh = GateFrameMesh(design);
                    }
                    else if (c.note.StartsWith("arch:", StringComparison.Ordinal))
                    {
                        var mf = t.GetComponent<MeshFilter>(); var reference = t.parent != null ? t.parent.Find(ctx.g.arch.reference) : null;
                        if (mf == null || mf.sharedMesh == null || reference == null) throw new InvalidOperationException("arch surface or reference missing at " + c.key);
                        var fix = GateArchOffset(reference, t, mf.sharedMesh, ctx.g.arch);
                        if (fix.error.Length > 0) throw new InvalidOperationException(fix.error);
                        mesh = GateCopyMesh(mf.sharedMesh, fix.vertices);
                    }
                    else if (c.note.StartsWith("stalk:", StringComparison.Ordinal))
                    {
                        var mf = t.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) throw new InvalidOperationException("no mesh on " + c.key);
                        mesh = GateSubmeshMesh(mf.sharedMesh, ctx.g.polearm.keepSubmesh);
                    }
                    else throw new InvalidOperationException("unknown create-mesh source " + c.note);
                    mesh.name = Path.GetFileNameWithoutExtension(c.after);
                    EnsureFolder(Path.GetDirectoryName(c.after).Replace('\\', '/'));
                    AssetDatabase.CreateAsset(mesh, c.after); created.Add(mesh);
                    break;
                }
                case "frame":
                {
                    if (t.Find(ctx.g.frame.name) != null) throw new InvalidOperationException(c.key + " already has " + ctx.g.frame.name);
                    var note = c.note.Split('|');
                    var mesh = GateLoadMesh(c.after); if (mesh == null || mesh.vertexCount == 0) throw new InvalidOperationException("frame mesh missing or empty " + c.after);
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(note[0]) ?? throw new InvalidOperationException("frame material missing " + note[0]);
                    var go = new GameObject(ctx.g.frame.name);
                    go.transform.SetParent(t, false);
                    go.layer = note.Length > 1 && int.TryParse(note[1], NumberStyles.Integer, Inv, out int layer) ? layer : t.gameObject.layer;
                    // render only: batching / occludee static, never navigation or occluder static, no collider
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true;
                    EditorUtility.SetDirty(t.gameObject);
                    break;
                }
                case "mesh":
                {
                    var f = t.GetComponent<MeshFilter>() ?? throw new InvalidOperationException("no MeshFilter on " + c.key);
                    var mesh = GateLoadMesh(c.after); if (mesh == null || mesh.vertexCount == 0) throw new InvalidOperationException("mesh asset missing or empty " + c.after);
                    f.sharedMesh = mesh; EditorUtility.SetDirty(f);
                    break;
                }
                case "materials": GateSetMaterials(t, c.after); break;
                case "trs":
                    if (!GateParseTrs(c.after, out var p, out var q, out var s)) throw new FormatException(c.after);
                    t.localPosition = p; t.localRotation = q; t.localScale = s; EditorUtility.SetDirty(t);
                    break;
                default: throw new InvalidOperationException("unknown gate change kind " + c.kind);
            }
        }

        static void GateUndo(GateCtx308 ctx, Change308 c)
        {
            if (c.kind == "create-mesh") return;   // the asset stays on disk: nothing references it after the scene revert
            var t = BuildingAudit308.Resolve(ctx.scene, c.key) ?? throw new InvalidOperationException("missing " + c.key);
            switch (c.kind)
            {
                case "name":
                {
                    var child = GateNamedChild(t, c.after, c.note) ?? throw new InvalidOperationException("no child " + c.after + " with mesh " + c.note + " under " + c.key);
                    child.name = c.before; EditorUtility.SetDirty(child.gameObject);
                    break;
                }
                case "frame":
                {
                    var child = t.Find(ctx.g.frame.name);
                    if (child != null) Object.DestroyImmediate(child.gameObject);
                    EditorUtility.SetDirty(t.gameObject);
                    break;
                }
                case "mesh":
                {
                    var f = t.GetComponent<MeshFilter>() ?? throw new InvalidOperationException("no MeshFilter on " + c.key);
                    var mesh = GateLoadMesh(c.before);
                    if (!string.IsNullOrEmpty(c.before) && mesh == null) throw new InvalidOperationException("the original mesh is missing " + c.before);
                    f.sharedMesh = mesh; EditorUtility.SetDirty(f);
                    break;
                }
                case "materials": GateSetMaterials(t, c.before); break;
                case "trs":
                    if (!GateParseTrs(c.before, out var p, out var q, out var s)) throw new FormatException(c.before);
                    t.localPosition = p; t.localRotation = q; t.localScale = s; EditorUtility.SetDirty(t);
                    break;
                default: throw new InvalidOperationException("unknown gate change kind " + c.kind);
            }
        }

        // ------------------------------------------------------------------ revert

        static string GateRevert(string alias, string step)
        {
            if (step != null && !GateSteps.Contains(step)) return "refused: gate steps are G1 G2 G3 G4 (" + step + ")";
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var g = GateLoad(out err); if (g == null) return "refused: " + err;
            var ledger = GateLoadLedger(alias);
            // a FAILED apply counts only when it was saved (shaAfter recorded); an unsaved failure never reached the file
            var ops = ledger.ops.Where(o => !o.reverted && (o.status == "applied" || (o.status == "FAILED" && !string.IsNullOrEmpty(o.shaAfter))) && o.changes.Any(c => c.state == "apply" && c.kind != "create-mesh" && (step == null || c.step == step))).ToList();
            if (ops.Count == 0) return "refused: nothing to revert in " + GateLedgerFile(alias) + (step != null ? " for " + step : "");
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            var ctx = new GateCtx308 { cfg = cfg, g = g, scene = scene };
            string sha = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path));
            var rev = new Op308 { utc = BuildingAudit308.Utc(), command = "gate revert:" + alias + (step != null ? ":" + step : ""), scene = scene.path, alias = alias, shaBefore = sha, steps = step != null ? new[] { step } : GateSteps, quality = BuildingAudit308.QualityName() };
            string backupDir = Path.Combine(BuildingAudit308.Folder, "SceneBackup"); Directory.CreateDirectory(backupDir);
            rev.backup = Path.Combine(backupDir, alias + "-" + rev.utc + "-gate-prerevert.unity"); File.Copy(BuildingAudit308.Abs(scene.path), rev.backup, false);
            rev.attractionBefore = AttractionHash(scene, cfg.attractionRoot);
            int n = 0;
            try
            {
                string guard = GateGuard(ctx);
                foreach (var op in Enumerable.Reverse(ops))
                    foreach (var c in Enumerable.Reverse(op.changes).Where(c => c.state == "apply" && (step == null || c.step == step)).ToList()) { GateUndo(ctx, c); n++; }
                if (GateGuard(ctx) != guard) throw new InvalidOperationException("gate colliders / hinges / door components changed during the revert (guard hash differs)");
            }
            catch (Exception e)
            {
                // the ledger file is untouched; drop the half-reverted in-memory scene so the editor stays clean
                string discarded = GateDiscard(scene, previous, opened, true);
                return "FAILED: gate revert " + alias + " after " + n + " change(s) — " + e.Message + discarded + " (pre-revert copy " + rev.backup + ")";
            }
            ClearStaleSuffix(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            // the ledger follows the file: changes count as reverted only once the scene is on disk
            if (saved)
                foreach (var op in ops)
                {
                    foreach (var c in op.changes.Where(c => c.state == "apply" && (step == null || c.step == step))) c.state = "reverted";
                    if (op.changes.All(c => c.state != "apply")) { op.reverted = true; op.revertedUtc = rev.utc; }
                }
            rev.shaAfter = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path));
            rev.attractionAfter = AttractionHash(scene, cfg.attractionRoot);
            rev.status = saved ? "reverted" : "FAILED"; rev.detail = n + " change(s) set back to their ledger values" + (saved ? "" : "; SaveScene returned false — the file and the ledger still hold the applied state, the scene stays open and dirty");
            ledger.ops.Add(rev); GateSaveLedger(ledger);
            GoBack(previous, scene, opened);
            return rev.status + ": gate " + alias + " sha " + sha + " -> " + rev.shaAfter + " — " + rev.detail + " (pre-revert backup " + rev.backup + ")";
        }

        // ------------------------------------------------------------------ verify (read only)

        [Serializable] sealed class GateVerify308 { public string alias = "", scene = "", utc = "", sceneSha = "", guard = "", quality = ""; public List<string> rows = new List<string>(); public int fail; }

        static string GateVerify(string alias)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var g = GateLoad(out err); if (g == null) return "refused: " + err;
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            var ctx = new GateCtx308 { cfg = cfg, g = g, scene = scene };
            var v = new GateVerify308 { alias = alias, scene = scene.path, utc = BuildingAudit308.Utc(), sceneSha = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path)), quality = BuildingAudit308.QualityName() };
            void Row(bool ok, string text) { v.rows.Add((ok ? "PASS " : "FAIL ") + text); if (!ok) v.fail++; }
            void Info(string text) => v.rows.Add("INFO " + text);
            try
            {
                v.guard = GateGuard(ctx);
                foreach (var site in g.gates)
                {
                    var gate = BuildingAudit308.Resolve(scene, site.gate);
                    if (gate == null) { Info(site.id + ": gate not in this scene"); continue; }
                    // G1
                    var holder = gate.Find(g.names.holder);
                    if (holder != null)
                    {
                        var names = new List<string>(); for (int i = 0; i < holder.childCount; i++) names.Add(holder.GetChild(i).name);
                        var dup = names.GroupBy(x => x).Where(x => x.Count() > 1).Select(x => x.Key).ToList();
                        Row(dup.Count == 0, "G1 " + site.id + ": " + names.Count + " infill renderers, unique names" + (dup.Count > 0 ? " — shared: " + string.Join(", ", dup) : ""));
                    }
                    else Info("G1 " + site.id + ": no " + g.names.holder);
                    // G2
                    var d = GateMeasure(ctx, site, gate);
                    if (d.error.Length > 0) Row(false, "G2 " + site.id + ": " + d.error);
                    else
                    {
                        var child = gate.Find(g.frame.name);
                        Info("G2 " + site.id + ": opening " + d.open + " cells (" + BuildingAudit308.F(d.open * d.cell * d.cell, "F2") + " m²); without the frame " + d.missBefore + " gap cells (" + BuildingAudit308.F(d.missBefore * d.cell * d.cell, "F2") + " m²: top " + d.missUpper + ", left jamb " + d.missLeft + ", right jamb " + d.missRight + ")");
                        if (child == null) Row(d.missBefore == 0, "G2 " + site.id + ": no " + g.frame.name + ", " + d.missBefore + " gap cells");
                        else
                        {
                            var cm = child.GetComponent<MeshFilter>(); var cr = child.GetComponent<MeshRenderer>();
                            Row(d.missChild == 0, "G2 " + site.id + ": projected coverage of the opening with " + g.frame.name + " — " + d.missChild + " gap cells");
                            Row(child.GetComponentsInChildren<Collider>(true).Length == 0, "G2 " + site.id + ": " + g.frame.name + " carries no collider");
                            bool mat = cr != null && cr.sharedMaterial != null && !cr.sharedMaterial.IsKeywordEnabled("_EMISSION");
                            Row(mat, "G2 " + site.id + ": frame material " + (cr != null && cr.sharedMaterial != null ? cr.sharedMaterial.name : "none") + " (no emission keyword)");
                            if (cm != null && cm.sharedMesh != null && !float.IsInfinity(d.childClear))
                                Row(d.childClear >= g.frame.clearance - .001f, "G2 " + site.id + ": leaf swing 0.." + BuildingAudit308.F(d.openDegrees, "F0") + "° clears every frame part by " + BuildingAudit308.F(d.childClear) + " m or more (>= " + BuildingAudit308.F(g.frame.clearance) + ")");
                        }
                    }
                }
                // G3 (the gates and the leafless arches of arch.extra)
                foreach (var site in g.gates.Concat(g.arch.extra))
                {
                    var arch = GateArchOf(ctx, site);
                    var reference = arch != null ? arch.Find(g.arch.reference) : null;
                    if (reference == null) { Info("G3 " + site.id + ": no " + site.arch + "/" + g.arch.reference); continue; }
                    foreach (var name in g.arch.others)
                    {
                        var t = arch.Find(name); var mf = t != null ? t.GetComponent<MeshFilter>() : null;
                        if (mf == null || mf.sharedMesh == null) { Info("G3 " + site.id + "/" + name + ": absent"); continue; }
                        var fix = GateArchOffset(reference, t, mf.sharedMesh, g.arch);
                        if (fix.error.Length > 0) { Row(false, "G3 " + site.id + "/" + name + ": " + fix.error); continue; }
                        Row(fix.area <= cfg.thresholds.c4CoplanarArea, "G3 " + site.id + "/" + name + " (" + mf.sharedMesh.name + "): " + BuildingAudit308.F(fix.area, "F2") + " m² coplanar with " + g.arch.reference + " (<= " + BuildingAudit308.F(cfg.thresholds.c4CoplanarArea, "F2") + ")");
                        // physics untouched: the collider still holds a mesh outside the generated folder
                        var mc = t.GetComponent<MeshCollider>();
                        if (mc != null) Row(mc.sharedMesh != null && !AssetDatabase.GetAssetPath(mc.sharedMesh).StartsWith(g.arch.meshFolder.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase), "G3 " + site.id + "/" + name + ": MeshCollider keeps the source mesh");
                    }
                }
                // G4
                var root = string.IsNullOrEmpty(g.polearm.root) ? null : BuildingAudit308.Resolve(scene, g.polearm.root);
                if (root == null) Info("G4: " + g.polearm.root + " not in this scene");
                else
                    foreach (var shaft in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == g.polearm.part))
                    {
                        var mf = shaft.GetComponent<MeshFilter>(); var mr = shaft.GetComponent<MeshRenderer>();
                        if (mf == null || mr == null || mf.sharedMesh == null) { Row(false, "G4 " + g.polearm.part + ": no mesh / renderer"); continue; }
                        var mats = mr.sharedMaterials; var mesh = mf.sharedMesh;
                        Row(mats.Length >= mesh.subMeshCount && mats.Take(mesh.subMeshCount).All(m => m != null), "G4 " + g.polearm.part + ": " + mesh.name + " " + mesh.subMeshCount + " submesh(es), " + mats.Count(m => m != null) + " material(s)");
                        if (g.polearm.diameter > 0)
                        {
                            float dia = GateShaftDiameter(shaft, mesh.bounds, GateLongAxis(mesh.bounds));
                            Row(Mathf.Abs(dia - g.polearm.diameter) <= Mathf.Max(.001f, g.polearm.diameterTolerance), "G4 " + g.polearm.part + ": diameter " + BuildingAudit308.F(dia) + " m (authored " + BuildingAudit308.F(g.polearm.diameter) + ")");
                        }
                    }
                Info("gate guard hash (colliders, obstacles, hinges, door components) " + v.guard);
            }
            finally { GoBack(previous, scene, opened, true); }
            Directory.CreateDirectory(FixDir);
            string file = Path.Combine(FixDir, "gate-verify-" + alias + "-" + v.utc + ".json");
            File.WriteAllText(file, JsonUtility.ToJson(v, true), new UTF8Encoding(false));
            return "gate verify " + alias + ": " + (v.fail == 0 ? "PASS" : v.fail + " FAIL") + " -> " + file + "\n" + string.Join("\n", v.rows);
        }
    }
}
