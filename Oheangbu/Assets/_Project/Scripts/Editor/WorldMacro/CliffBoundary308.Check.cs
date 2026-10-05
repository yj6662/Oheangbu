using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // check:<scene> / walk:<scene>. Read-only on the scene and on every asset: they open the scene (when it is not the active one), measure,
    // and write a JSON report + a short text summary under Out/. No object stays behind (one hidden CharacterController, destroyed), nothing
    // is created that glows, lights or prompts. Automated Edit-mode physics, not manual play.
    public static partial class CliffBoundary308
    {
        [Serializable] sealed class TileCheck { public string tile, state; public bool colliderOnLod0; public string[] problems; }
        [Serializable] sealed class HeightRow { public string scene, asset, sha256, source; }
        [Serializable] sealed class CheckReport
        {
            public string format = "cb308.check.1", utc, scene, key, stage, ledgerState, verdict, colliderSetSha256, ledgerColliderSetSha256, layoutSurface, layoutSurfaceSha256, baseSha256, stageHeightSha256;
            public bool sceneDirty; public int tiles, tileProblems, createdRoots, createdLights, createdEmissive, createdPrompts, createdRendererlessColliders;
            public TileCheck[] tile; public HeightRow[] heights; public CliffCore308.GroundCheck ground; public CliffCore308.ProbeReport probes; public string[] knownRedRegressions, notes;
            // stage 1b: probe lines listed as EXPECTED (recorded exceptions inside their bounds, declared lift / descent pieces)
            public int expected; public string[] expectedLines;
        }

        static Scene OpenForRead(CliffCore308.Config cfg, string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new PostLedger308.Refused("Edit mode only (Play is running)");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new PostLedger308.Refused("the editor is compiling or importing");
            var active = SceneManager.GetActiveScene();
            if (active.path == path && SceneManager.sceneCount == 1) return active;   // a dirty active target scene can still be measured
            PostLedger308.RequireEditable(); RequireNoPreview();
            return PostLedger308.Open(path);
        }

        static string Check(CliffCore308.Config cfg, string token, Dictionary<string, string> opt)
        {
            string path = CliffCore308.ScenePath(cfg, token);
            var scene = OpenForRead(cfg, path);
            var ledger = ReadLedger(cfg, path);
            string stageId = opt != null && opt.TryGetValue("stage", out string so) && so.Length > 0 ? so : ledger.stage != "" ? ledger.stage : StageOption(cfg, opt, ledger);
            int max = opt != null && opt.TryGetValue("max", out string mo) && int.TryParse(mo, out int mv) ? mv : int.MaxValue;
            // :walkablejump = the what-if capsule that may only jump from walkable support (this call only; the report says so and gets its own file)
            bool whatIf = opt != null && opt.ContainsKey("walkablejump"); if (whatIf) cfg.probe.jumpNeedsWalkable = true;
            var root = CliffCore308.TerrainRoot(scene, cfg);
            var notes = new List<string>();
            var report = new CheckReport { utc = PostLedger308.Utc(), scene = path, key = ledger.key, stage = stageId, ledgerState = ledger.state, sceneDirty = scene.isDirty, knownRedRegressions = cfg.knownRedRegressions ?? new string[0] };

            // 1. tiles against the ledger (references, collider on LOD0, counts)
            var tiles = new List<TileCheck>();
            foreach (var kt in ledger.tiles)
            {
                var tc = new TileCheck { tile = kt.tile }; var problems = new List<string>();
                if (!CliffCore308.ParseTileKey(kt.key, out int col, out int row)) { problems.Add("bad key " + kt.key); tc.problems = problems.ToArray(); tiles.Add(tc); continue; }
                var t = CliffCore308.FindTile(root, cfg, col, row, false);
                if (t == null) { problems.Add("missing in the scene"); tc.problems = problems.ToArray(); tiles.Add(tc); continue; }
                int onApplied = 0, onOriginal = 0;
                foreach (var kl in kt.lods)
                {
                    var l = t.Lod(kl.lod);
                    if (l == null) { problems.Add("no " + kl.lod); continue; }
                    if (Same(l.Mesh, kl.applied)) onApplied++; else if (Same(l.Mesh, kl.original)) onOriginal++; else problems.Add(kl.lod + " references " + l.Path + " (neither original nor applied)");
                    if (l.Mesh != null && l.Mesh.vertexCount != kl.original.vertices) problems.Add(kl.lod + " has " + l.Mesh.vertexCount + " vertices, the original " + kl.original.vertices);
                    if (Same(l.Mesh, kl.applied) && kl.applied.fileSha256 != "" && CliffCore308.ShaFile(PostLedger308.Abs(l.Path)) != kl.applied.fileSha256) problems.Add(kl.lod + " stage mesh file changed since tiles");
                    var info = CliffCore308.Analyse(l.Mesh, l.Quads, cfg.tileSize);
                    if (info.Indices != kl.original.indices) problems.Add(kl.lod + " has " + info.Indices + " indices, the original " + kl.original.indices);
                }
                tc.state = onApplied == kt.lods.Length ? "applied" : onOriginal == kt.lods.Length ? "original" : "mixed";
                if (tc.state == "mixed") problems.Add("LODs are on different stages");
                if ((tc.state == "applied") != kt.applied) problems.Add("the ledger says " + (kt.applied ? "applied" : "original"));
                tc.colliderOnLod0 = t.ColliderIsLod0; if (!t.ColliderIsLod0) problems.Add("the collider is not on the " + cfg.colliderLod + " mesh");
                tc.problems = problems.ToArray(); tiles.Add(tc); report.tileProblems += problems.Count;
            }
            report.tile = tiles.ToArray(); report.tiles = tiles.Count;
            report.colliderSetSha256 = ColliderSet(cfg, root); report.ledgerColliderSetSha256 = ledger.colliderSetSha256;
            if (ledger.colliderSetSha256 != "" && ledger.colliderSetSha256 != report.colliderSetSha256) notes.Add("collider set sha differs from the ledger (a tile collider changed since tiles / revert)");

            // 2. height sources (AC-B24)
            var rows = new List<HeightRow>();
            foreach (var sc in cfg.scenes) { string asset = CliffHeight308.For(sc.path, out string source); rows.Add(new HeightRow { scene = sc.key, asset = asset, sha256 = CliffHeight308.Sha(asset), source = source }); }
            rows.Add(new HeightRow { scene = "other (W_Demo_Compact)", asset = cfg.baseHeight, sha256 = CliffHeight308.Sha(cfg.baseHeight), source = "base" });
            report.heights = rows.ToArray(); report.baseSha256 = CliffHeight308.Sha(cfg.baseHeight);
            var session = CliffCore308.Session(scene); var layoutSurface = session != null && session.MountainLayout != null ? session.MountainLayout.FinalSurface : null;
            report.layoutSurface = PostLedger308.AssetRef(layoutSurface); report.layoutSurfaceSha256 = layoutSurface != null ? CliffCore308.Sha(layoutSurface.bytes) : "";
            if (report.baseSha256 != cfg.baseHeightSha256) notes.Add("the base height file is not the config's base sha");

            // 3. physics ground against the field the scene should stand on (changed lattice vertices of the stage)
            CliffCore308.Stage stage = null;
            try { stage = CliffCore308.LoadStage(cfg, stageId, false); report.stageHeightSha256 = stage.HeightSha; }
            catch (PostLedger308.Refused r) { notes.Add("stage field not readable: " + r.Message); }
            if (stage != null)
            {
                bool applied = ledger.state == "applied" && ledger.stage == stageId;
                if (applied && ledger.heightSha256 != stage.HeightSha) notes.Add("the stage field on disk is not the one this scene was applied from");
                if (applied && report.layoutSurfaceSha256 != stage.HeightSha) notes.Add("layout FinalSurface sha is not the stage field (AC-B24)");
                if (!applied && report.layoutSurfaceSha256 != "" && report.layoutSurfaceSha256 != report.baseSha256) notes.Add("layout FinalSurface is neither the base nor an applied stage");
                var g = CliffCore308.GroundAgainst(cfg, root, stage, applied ? stage.New : stage.Base, applied ? "stage" : "base");
                report.ground = g;
                if (g.over > 0 || g.missing > 0) notes.Add("physics ground differs from the " + (applied ? "stage" : "base") + " field at " + g.over + " of " + g.samples + " changed vertices (no terrain " + g.missing + ")");
            }

            // 4. face heights and capsule probes along the op lines
            string stale = null;
            try
            {
                var data = CliffCore308.LoadCheckData(cfg, stageId);
                stale = CliffCore308.StaleCheckData(data, stage);
                if (stale != null) notes.Add("STALE CHECK DATA: " + stale);
                report.probes = CliffCore308.ProbeLines(cfg, scene, data, max, null, null, stage);
                report.probes.utc = report.utc; report.probes.scene = path;
                // stage 1b (check data cb308.check.2): recorded exceptions and declared pieces are expected, not failures. A 1a data file has neither
                var listed = new List<string>(); report.expected = ApplyExpected(report.probes, LoadCheck1b(data), listed); report.expectedLines = listed.ToArray();
            }
            catch (PostLedger308.Refused r) { notes.Add("probes skipped: " + r.Message); }

            // 5. nothing of #308 may glow, light or prompt: objects under roots this ledger family creates (none in 1a)
            foreach (var created in scene.GetRootGameObjects().Where(o => CliffCore308.Saved(o) && o.name.StartsWith("CliffBoundary308", StringComparison.Ordinal)))
            {
                report.createdRoots++;
                report.createdLights += created.GetComponentsInChildren<Light>(true).Length;
                foreach (var r in created.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                        if (m != null && (m.IsKeywordEnabled("_EMISSION") || (m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > 0f))) report.createdEmissive++;
                report.createdPrompts += created.GetComponentsInChildren<Oheangbu.App.World.WorldMacroContentPoint>(true).Length;
                report.createdRendererlessColliders += created.GetComponentsInChildren<Collider>(true).Count(c => !c.isTrigger && c.GetComponent<Renderer>() == null);
            }

            bool hardFail = report.tileProblems > 0 || (report.ground != null && (report.ground.over > 0 || report.ground.missing > 0)) || report.createdLights + report.createdEmissive + report.createdPrompts > 0;
            string probeVerdict = report.probes != null ? report.probes.verdict : "NO PROBES";
            // a verdict from stale lines / points is not a verdict on this stage: it is labelled, never reported as CLEAR
            report.verdict = (hardFail ? "FAIL" : probeVerdict) + (stale != null ? " (STALE CHECK DATA)" : ""); report.notes = notes.ToArray();
            string tag = "check-" + ledger.key + (whatIf ? "-walkablejump" : "");
            string json = LedgerFile(cfg, tag), txt = LedgerFile(cfg, tag, "txt");
            CliffCore308.WriteText(json, JsonUtility.ToJson(report, true));

            var sb = new StringBuilder();
            sb.AppendLine("#308 cliff boundary check - " + path + " | stage " + stageId + " | ledger " + ledger.state + " | " + report.utc + " | automated Edit-mode physics, not manual play");
            sb.AppendLine("VERDICT " + report.verdict + " (FAIL = a capsule ended on raised ground (TOP), went through a line (OPEN), gained height on an outer rise (GAIN), an end leaked, or a tile / the ground is off;");
            sb.AppendLine("  GRAMMAR = face heights off the height grammar; INCONCLUSIVE = another collider stopped a capsule before the face; OPEN at a joint / point is listed for the closure check, not counted)");
            sb.AppendLine("tiles in the ledger " + report.tiles + ", problems " + report.tileProblems + "; collider set sha " + report.colliderSetSha256 + (report.ledgerColliderSetSha256 != "" ? " (ledger " + (report.ledgerColliderSetSha256 == report.colliderSetSha256 ? "same" : "DIFFERENT") + ")" : ""));
            foreach (var tcheck in report.tile.Where(x => x.problems.Length > 0)) sb.AppendLine("  " + tcheck.tile + ": " + string.Join("; ", tcheck.problems));
            sb.AppendLine("height sources (AC-B24):");
            foreach (var row in report.heights) sb.AppendLine("  " + row.scene + ": " + row.asset + " sha256 " + row.sha256 + " [" + row.source + "]");
            sb.AppendLine("  this scene's layout FinalSurface: " + report.layoutSurface + " sha256 " + report.layoutSurfaceSha256);
            if (report.ground != null) sb.AppendLine("physics ground vs field at changed vertices: " + report.ground.samples + " samples, |d| median " + report.ground.median.ToString("F3", Inv) + " p95 " + report.ground.p95.ToString("F3", Inv) + " max " + report.ground.max.ToString("F3", Inv)
                + " m, over tolerance " + report.ground.over + ", no terrain " + report.ground.missing + (report.ground.worstAt != null ? ", worst " + report.ground.worstAt : ""));
            if (report.probes != null)
            {
                sb.AppendLine("capsule jump rule: " + report.probes.jumpRule);
                sb.AppendLine("probes: " + CliffCore308.Summary(report.probes).Replace(" | ", "\n  "));
                if (report.expectedLines != null && report.expectedLines.Length > 0)
                {
                    sb.AppendLine("expected (not counted; the counts above are after them, the per-segment tallies are before): " + report.expected);
                    foreach (string e in report.expectedLines) sb.AppendLine("  " + e);
                }
                foreach (var seg in report.probes.segments) foreach (string flag in seg.faceFlags.Take(8)) sb.AppendLine("  " + seg.id + " " + flag);
                foreach (var line in report.probes.lines.Where(l => l.verdict != "PASS" && l.verdict != "BLOCKED" && l.verdict != "SKIP" && l.verdict != "EXPECTED").OrderBy(l => l.verdict == "INCONCLUSIVE" ? 1 : 0).Take(120))
                    sb.AppendLine("  " + line.verdict + " " + line.segment + " " + line.kind + " " + line.variant + " s=" + line.s.ToString("F0", Inv) + " (" + line.x.ToString("F0", Inv) + "," + line.z.ToString("F0", Inv) + "): past " + line.maxPast.ToString("F1", Inv)
                        + " m, gain " + line.maxGain.ToString("F1", Inv) + " m, end (" + line.endX.ToString("F1", Inv) + "," + line.endZ.ToString("F1", Inv) + ") y " + line.endY.ToString("F1", Inv) + " end gain " + line.endGain.ToString("F1", Inv) + " m, " + line.end + ", touching " + line.by);
            }
            sb.AppendLine("#308 objects in the scene: roots " + report.createdRoots + ", lights " + report.createdLights + ", emissive materials " + report.createdEmissive + ", prompt points " + report.createdPrompts + ", renderer-less colliders " + report.createdRendererlessColliders);
            sb.AppendLine("excluded from the check batch (red before #308): " + string.Join("; ", report.knownRedRegressions));
            foreach (string n in notes) sb.AppendLine("NOTE " + n);
            CliffCore308.WriteText(txt, sb.ToString().TrimEnd());
            return "check " + ledger.key + " stage " + stageId + ": " + report.verdict + " | tiles " + report.tiles + " problems " + report.tileProblems + " | ground " + (report.ground != null ? "max |d| " + report.ground.max.ToString("F3", Inv) + " m over " + report.ground.over : "n/a")
                + " | " + (report.probes != null ? "probe fails " + report.probes.fails + " leaks " + report.probes.leaks + " open " + report.probes.open + " inconclusive " + report.probes.inconclusive + " grammar flags " + report.probes.grammarFlags + " expected " + report.expected + " (" + report.probes.runs + " runs)" : "no probes")
                + " | collider set " + report.colliderSetSha256.Substring(0, 12) + " | notes " + notes.Count + " | " + json + " (+ .txt) | scene dirty=" + scene.isDirty;
        }

        [Serializable] sealed class WalkRun { public string id, variant, expect, verdict, end, by, note; public float endX, endZ, endY, maxGain, distanceToTarget; public int ticks; }
        [Serializable] sealed class WalkReport { public string format = "cb308.walk.1", utc, scene, stage, verdict, walker; public int walks, fails, inconclusive; public WalkRun[] runs; public string[] notes; }

        static string Walk(CliffCore308.Config cfg, string token, Dictionary<string, string> opt)
        {
            string path = CliffCore308.ScenePath(cfg, token);
            var scene = OpenForRead(cfg, path);
            var ledger = ReadLedger(cfg, path);
            string stageId = opt != null && opt.TryGetValue("stage", out string so) && so.Length > 0 ? so : ledger.stage != "" ? ledger.stage : StageOption(cfg, opt, ledger);
            bool whatIf = opt != null && opt.ContainsKey("walkablejump"); if (whatIf) cfg.probe.jumpNeedsWalkable = true;
            var data = CliffCore308.LoadCheckData(cfg, stageId);
            var root = CliffCore308.TerrainRoot(scene, cfg);
            var report = new WalkReport { utc = PostLedger308.Utc(), scene = path, stage = stageId }; var runs = new List<WalkRun>(); var notes = new List<string>();
            CliffCore308.Stage walkStage = null; try { walkStage = CliffCore308.LoadStage(cfg, stageId, false); } catch (PostLedger308.Refused) { }
            string stale = CliffCore308.StaleCheckData(data, walkStage);
            if (stale != null) notes.Add("STALE CHECK DATA: " + stale);
            if (data.walks == null || data.walks.Length == 0) notes.Add("the check data declares no walks");
            Physics.SyncTransforms();
            using (var w = new CliffCore308.Walker(scene, cfg))
            {
                report.walker = w.Source;
                foreach (var walk in data.walks ?? new CliffCore308.WalkData[0])
                {
                    if (walk.from == null || walk.from.Length != 2 || walk.way == null || walk.way.Length < 2 || walk.way.Length % 2 != 0) { notes.Add("walk " + walk.id + " has no from / way"); continue; }
                    var way = new Vector2[walk.way.Length / 2]; for (int i = 0; i < way.Length; i++) way[i] = new Vector2(walk.way[i * 2], walk.way[i * 2 + 1]);
                    var target = way[way.Length - 1]; report.walks++;
                    foreach (var variant in cfg.probe.variants)
                    {
                        var run = CliffCore308.RunWalker(w, cfg, variant, root, new Vector3(walk.from[0], 0f, walk.from[1]), false, way, f => 0f, 0f, cfg.probe.climbSeconds);
                        float left = Vector2.Distance(new Vector2(run.Last.x, run.Last.z), target);
                        bool reached = run.Reached || left < cfg.probe.endReach;
                        string verdict = !run.Ground ? "INCONCLUSIVE" : walk.expect == "reach" ? (reached ? "PASS" : "FAIL") : (reached ? "FAIL" : "PASS");
                        if (verdict == "FAIL") report.fails++; else if (verdict == "INCONCLUSIVE") report.inconclusive++;
                        runs.Add(new WalkRun { id = walk.id, variant = variant.name, expect = walk.expect, verdict = verdict, end = run.End + run.Start, by = run.By, note = walk.note, endX = run.Last.x, endZ = run.Last.z, endY = run.Last.y,
                            maxGain = float.IsInfinity(run.MaxGain) || float.IsNaN(run.MaxGain) ? 0f : run.MaxGain, distanceToTarget = left, ticks = run.Ticks });
                    }
                }
            }
            report.runs = runs.ToArray(); report.notes = notes.ToArray();
            report.verdict = (report.fails > 0 ? "FAIL" : report.inconclusive > 0 ? "INCONCLUSIVE" : report.walks == 0 ? "NO WALKS" : "CLEAR") + (stale != null ? " (STALE CHECK DATA)" : "");
            string tag = "walk-" + ledger.key + (whatIf ? "-walkablejump" : "");
            string json = LedgerFile(cfg, tag), txt = LedgerFile(cfg, tag, "txt");
            CliffCore308.WriteText(json, JsonUtility.ToJson(report, true));
            var sb = new StringBuilder();
            sb.AppendLine("#308 cliff boundary walk - " + path + " | stage " + stageId + " | " + report.utc + " | automated Edit-mode capsule (" + report.walker + "), not manual play");
            sb.AppendLine("VERDICT " + report.verdict + ": " + report.walks + " walks x " + cfg.probe.variants.Length + " variants, fails " + report.fails + ", inconclusive " + report.inconclusive
                + " | capsule jump rule: " + (cfg.probe.jumpNeedsWalkable ? "motor (D308-9d): jump only from walkable support" : "motor (jump from any contact below)"));
            foreach (var r in runs) sb.AppendLine((r.verdict == "PASS" ? "ok   " : r.verdict + " ") + r.id + " " + r.variant + " (expect " + r.expect + "): " + r.end + ", " + r.distanceToTarget.ToString("F1", Inv) + " m from the target, feet (" + r.endX.ToString("F1", Inv) + "," + r.endY.ToString("F1", Inv) + "," + r.endZ.ToString("F1", Inv)
                + "), gain " + r.maxGain.ToString("F1", Inv) + " m, touching " + r.by + " - " + r.note);
            foreach (string n in notes) sb.AppendLine("NOTE " + n);
            CliffCore308.WriteText(txt, sb.ToString().TrimEnd());
            return "walk " + ledger.key + " stage " + stageId + ": " + report.verdict + " | " + report.walks + " walks, fails " + report.fails + ", inconclusive " + report.inconclusive + " | " + json + " (+ .txt) | scene dirty=" + scene.isDirty;
        }
    }
}
