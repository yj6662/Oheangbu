using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Stage 1b of the #308 cliff ledger (SPEC-WORLD-CLIFF-BOUNDARY-308, D308-9c Q4 one-way drops, D308-9e). Read-only additions:
    //   - the stage-1b keys of the check data (Data/check_<stage>.json, format cb308.check.2: expected[], pieces[], rims[]) and of the
    //     config (packages[], rim{}), read beside the stage-1a classes of CliffCore308 (no signature of the core changes);
    //   - `check`: a probe line that matches a recorded exception (ops check_dispositions) inside its bounds, or that was stopped by a
    //     declared lift / descent piece, is listed EXPECTED instead of failed / inconclusive;
    //   - rim:<scene>[:stage=<s>][:id=<top>][:every=N][:max=N][:rule=coded|walkable]  (#308 D308-18: the fall rule is the scene profile's
    //     FallReferenceWalkableOnly unless rule= forces one; the text below describes rule=coded)  from every rim station of a reachable cliff top the capsule is pushed off the
    //     rim under the motor rules and the session fall rule AS CODED (WorldMacroPlaytestSession.Terrain.cs: the fall reference is
    //     refreshed whenever the motor is grounded - PlayerMotor.Locomotion: CollisionFlags.Below with a non-positive vertical velocity,
    //     steep ground included). Verdict per station: BLOCKED (a lip or anything else keeps the feet on the top) / LETHAL / SURVIVES +
    //     the side it lands on -> Out/cb308-rim-<scene>.json + .txt. Nothing is saved, no object stays behind.
    // Automated Edit-mode physics, not manual play: Play decides whether a real player can hold the face while sliding.
    public static partial class CliffBoundary308
    {
        [Serializable] sealed class Expected1b { public string id, segment, kind, variant, reason; public float x, z, within, maxGain, maxPast; public string[] verdicts; }
        [Serializable] sealed class Piece1b { public string id, kind, source; public float x, z, y, r; }
        [Serializable] sealed class RimLine1b { public string id, segment, state; public float step; public bool closed; public float[] sx, sz, nx, nz, drop; public int[] inBay, lipRequired, footInReach; }
        [Serializable] sealed class CheckData1b { public string version; public Expected1b[] expected; public Piece1b[] pieces; public RimLine1b[] rims; }
        [Serializable] sealed class Package1b { public string name, tool; }
        [Serializable] sealed class RimSpec1b { public float inset, push, tick, seconds, creepSpeed, lethalFall, lipClimb, pastRim, fallPast; public int landedTicks; }
        [Serializable] sealed class Config1b { public Package1b[] packages; public RimSpec1b rim; }
        [Serializable] sealed class PackageLedger1b { public string state, stage, utc; }

        static CheckData1b LoadCheck1b(CliffCore308.CheckData data)
        {
            var d = JsonUtility.FromJson<CheckData1b>(File.ReadAllText(data.File, Encoding.UTF8)) ?? new CheckData1b();
            if (d.expected == null) d.expected = new Expected1b[0]; if (d.pieces == null) d.pieces = new Piece1b[0]; if (d.rims == null) d.rims = new RimLine1b[0];
            return d;
        }

        static Config1b LoadConfig1b()
        {
            var c = JsonUtility.FromJson<Config1b>(File.ReadAllText(PostLedger308.RepoPath(CliffCore308.ConfigPath), Encoding.UTF8)) ?? new Config1b();
            if (c.packages == null) c.packages = new Package1b[0];
            return c;
        }

        static string Or(string s) => string.IsNullOrEmpty(s) ? "-" : s;

        /// <summary>Stage-1b ledgers of the other packages (Out/cb308-&lt;name&gt;-&lt;scene&gt;.json), by file name: state, stage, utc.</summary>
        static void AppendPackages(CliffCore308.Config cfg, StringBuilder sb)
        {
            string dir = PostLedger308.RepoPath(cfg.outDir);
            foreach (var p in LoadConfig1b().packages)
            {
                var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "cb308-" + p.name + "-*.json").OrderBy(f => f).ToArray() : new string[0];
                sb.AppendLine("package " + p.name + " (" + p.tool + "): " + (files.Length == 0 ? "no ledger" : files.Length + " ledger file(s)"));
                foreach (string f in files)
                {
                    PackageLedger1b l = null;
                    try { l = JsonUtility.FromJson<PackageLedger1b>(File.ReadAllText(f, Encoding.UTF8)); } catch (Exception) { }
                    sb.AppendLine("  " + Path.GetFileName(f) + ": " + (l == null ? "unreadable" : "state " + Or(l.state) + ", stage " + Or(l.stage) + ", utc " + Or(l.utc)));
                }
            }
        }

        /// <summary>Probe lines that are recorded exceptions or declared pieces: relabelled EXPECTED, taken out of the fail / inconclusive
        /// counts, and the probe verdict is recomputed. A line beyond the bounds of its exception stays a failure.</summary>
        static int ApplyExpected(CliffCore308.ProbeReport probes, CheckData1b extra, List<string> listed)
        {
            if (probes == null || probes.lines == null) return 0;
            int n = 0;
            foreach (var line in probes.lines)
            {
                bool failing = line.verdict == "TOP" || line.verdict == "CLIMB" || line.verdict == "GAIN" || (line.verdict == "OPEN" && line.kind == "push");
                if (failing)
                {
                    var e = extra.expected.FirstOrDefault(x => x.segment == line.segment && x.kind == line.kind && (x.variant == "*" || x.variant == line.variant)
                        && (line.x - x.x) * (line.x - x.x) + (line.z - x.z) * (line.z - x.z) <= x.within * x.within && x.verdicts != null && x.verdicts.Contains(line.verdict));
                    if (e == null) continue;
                    if (line.maxGain > e.maxGain || line.maxPast > e.maxPast) { listed.Add("NOT expected (beyond the bounds of " + e.id + ": gain " + line.maxGain.ToString("F2", Inv) + " > " + e.maxGain.ToString("F2", Inv) + " or past " + line.maxPast.ToString("F2", Inv) + " > " + e.maxPast.ToString("F2", Inv) + "): " + line.segment + " " + line.kind + " " + line.variant); continue; }
                    listed.Add(e.id + ": " + line.verdict + " " + line.segment + " " + line.kind + " " + line.variant + " (" + line.x.ToString("F0", Inv) + "," + line.z.ToString("F0", Inv) + ") gain " + line.maxGain.ToString("F2", Inv) + " m, past " + line.maxPast.ToString("F2", Inv) + " m, end (" + line.endX.ToString("F1", Inv) + "," + line.endZ.ToString("F1", Inv) + ") y " + line.endY.ToString("F1", Inv) + " end gain " + line.endGain.ToString("F1", Inv) + " m - recorded exception");
                    line.end += " | expected: " + e.id + " (was " + line.verdict + ")"; line.verdict = "EXPECTED"; probes.fails = Mathf.Max(0, probes.fails - 1); n++;
                }
                else if (line.verdict == "INCONCLUSIVE" && !string.IsNullOrEmpty(line.by) && !line.by.StartsWith("terrain", StringComparison.Ordinal) && line.by != "nothing")
                {
                    var p = extra.pieces.FirstOrDefault(x => (line.x - x.x) * (line.x - x.x) + (line.z - x.z) * (line.z - x.z) <= x.r * x.r);
                    if (p == null) continue;
                    listed.Add(p.id + ": INCONCLUSIVE " + line.segment + " " + line.kind + " " + line.variant + " (" + line.x.ToString("F0", Inv) + "," + line.z.ToString("F0", Inv) + ") touching " + line.by + " - declared " + p.kind);
                    line.end += " | expected: declared " + p.kind + " " + p.id; line.verdict = "EXPECTED"; probes.inconclusive = Mathf.Max(0, probes.inconclusive - 1); n++;
                }
            }
            probes.verdict = probes.fails > 0 || probes.leaks > 0 ? "FAIL" : probes.grammarFlags > 0 ? "GRAMMAR" : probes.inconclusive > 0 ? "INCONCLUSIVE" : "CLEAR";
            return n;
        }

        // ---------------------------------------------------------------- rim:<scene>

        [Serializable] sealed class RimRun { public string variant, verdict, end, by; public float maxPast, maxGain, fall, worstUngroundedFall, endX, endY, endZ; public int ticks; }
        [Serializable] sealed class RimRow { public string rim, verdict, landing; public int station; public float s, x, z, plannedDrop; public bool inBay, lipRequired; public RimRun[] runs; }
        [Serializable] sealed class RimReport
        {
            public string format = "cb308.rim.1", utc, scene, stage, verdict, walker, fallRule, checkData, checkDataSha256;
            public float lethalFall, sceneFatalFall; public int stations, blocked, lethal, survives, survivesBlockedSide, inconclusive, lipRequiredNotBlocked, bayStations;
            public RimRow[] rows; public string[] notes;
        }

        /// <summary>PlayerMotor.ProbeGround + the 4.5 cm gap of UpdateLocomotion: walkable support right under the feet (grounded without a contact flag).</summary>
        static bool CloseSupport(CliffCore308.Walker w)
        {
            var feet = w.Feet; var cc = w.CC; float radius = Mathf.Max(.05f, cc.radius * .85f), limit = Mathf.Cos(cc.slopeLimit * Mathf.Deg2Rad);
            foreach (var h in Physics.SphereCastAll(feet + Vector3.up * (radius + .08f), radius, Vector3.down, .08f + .045f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider != cc && h.normal.y >= limit) return true;
            return false;
        }

        /// <summary>#308 D308-18 (3): the numbers of the walkable-support test, read from the scene's traversal profile (class defaults when the
        /// scene has none) and the player's locomotion profile. null = rule as coded (every grounded tick).</summary>
        sealed class FallRule1b { public float maxGap, fieldDeg; public int layers; public string source; }
        static FallRule1b FallRuleOf(WorldMacroPlaytestSession session)
        {
            var rules = session != null && session.Traversal != null ? session.Traversal.Rules : null; var made = rules == null ? ScriptableObject.CreateInstance<WorldTraversalTestProfile>() : null;
            try
            {
                var p = rules != null ? rules : made; var motor = session != null && session.Walker != null ? session.Walker.Motor : null;
                var loco = motor != null ? motor.LocomotionProfile : null;
                return new FallRule1b { maxGap = p.FallReferenceSupportGap, fieldDeg = p.FallReferenceMaxSlopeDeg, layers = loco != null ? (int)loco.GroundLayers : ~0,
                    source = (rules != null ? "profile " + rules.name : "class defaults (no traversal profile on the scene)") + (loco != null ? ", ground layers of " + loco.name : ", all layers (no locomotion profile)") };
            }
            finally { if (made != null) UnityEngine.Object.DestroyImmediate(made); }
        }
        /// <summary>WorldMacroPlaytestSession.FallSupport308 on the probe capsule, test for test: the motor's ground probe origin and radius, a cast of
        /// 0.08 + maxGap on the ground layers, a hit that starts inside the sphere (distance 0) is no support, the normal must pass
        /// FallReferenceRule308 with the effective slope limit, and the gap feet - hit point must be within maxGap.
        /// (CliffCore308.Walker.Supported is the motor's JUMP probe: longer cast, every layer, no gap test - not the fall rule.)</summary>
        static bool FallSupport1b(CliffCore308.Walker w, FallRule1b rule)
        {
            var cc = w.CC; var feet = w.Feet; float radius = Mathf.Max(.05f, cc.radius * .85f), limitDeg = FallReferenceRule308.EffectiveSlopeDeg(rule.fieldDeg, cc.slopeLimit);
            bool found = false; float gap = float.PositiveInfinity, normalY = 0f;
            foreach (var h in Physics.SphereCastAll(feet + Vector3.up * (radius + .08f), radius, Vector3.down, .08f + rule.maxGap, rule.layers, QueryTriggerInteraction.Ignore))
            {
                if (h.collider == null || h.collider == cc || h.distance <= 0f) continue;
                if (!FallReferenceRule308.Walkable(h.normal.y, limitDeg)) continue;
                float g = Vector3.Dot(feet - h.point, Vector3.up);
                if (g < gap) { gap = g; normalY = h.normal.y; found = true; }
            }
            return FallReferenceRule308.Refreshes(true, true, false, found, normalY, gap, rule.maxGap, rule.fieldDeg, cc.slopeLimit);
        }

        /// <summary>One run off the rim. variant: run (outward at run speed), creep (outward at creepSpeed), hug (creep out, then push back
        /// INTO the face once the feet are under the rim: the slide that keeps contact). The fall rule is the session's, as coded.</summary>
        static RimRun RunRim(CliffCore308.Walker w, CliffCore308.Config cfg, RimSpec1b spec, CliffCore308.Ground ground, string variant, Vector2 rim, Vector2 inward, float lethal, FallRule1b rule)
        {
            var r = new RimRun { variant = variant, verdict = "INCONCLUSIVE", end = "", by = "nothing", maxPast = float.NegativeInfinity };
            var start = rim + inward * spec.inset;
            if (!CliffCore308.TerrainY(ground, start.x, start.y, out float gy)) { r.end = "no terrain at the start"; return r; }
            var feet0 = new Vector3(start.x, gy + cfg.probe.startLift, start.y);
            w.Place(feet0);
            try
            {
                if (w.Around(feet0, -.02f).Length > 0) { r.end = "started inside a collider"; return r; }
                float dt = spec.tick, vy = 0f, recent = feet0.y, startY = feet0.y, speed = variant == "run" ? w.Speed : spec.creepSpeed, lastY = feet0.y, airFrom = feet0.y;
                bool grounded = false, known = true; int budget = Mathf.CeilToInt(spec.seconds / dt), rest = 0;
                for (int tick = 0; tick < budget; tick++)
                {
                    r.ticks = tick + 1;
                    var f = w.Feet; float past = -((f.x - rim.x) * inward.x + (f.z - rim.y) * inward.y);
                    r.maxPast = Mathf.Max(r.maxPast, past); r.maxGain = Mathf.Max(r.maxGain, f.y - startY);
                    // WorldMacroPlaytestSession.TickTraversal, in its order: death first, then the refresh while the motor is grounded
                    if (known && recent - f.y >= lethal) { r.verdict = "LETHAL"; r.fall = recent - f.y; r.end = "the fall rule fired " + (startY - f.y).ToString("F1", Inv) + " m under the start"; break; }
                    // D308-18 (3): the reference follows the feet only on walkable support (FallSupport1b = the session's FallSupport308)
                    if (grounded && (rule == null || FallSupport1b(w, rule))) { recent = f.y; known = true; airFrom = f.y; }
                    else r.worstUngroundedFall = Mathf.Max(r.worstUngroundedFall, airFrom - f.y);
                    // at rest: grounded, no longer descending
                    if (grounded && Mathf.Abs(f.y - lastY) < .002f) rest++; else rest = 0;
                    lastY = f.y;
                    if (rest >= spec.landedTicks && (past >= spec.pastRim || tick > budget / 3))
                    {
                        bool under = startY - f.y > spec.fallPast;
                        if (past < spec.pastRim) { r.verdict = "BLOCKED"; r.end = "stopped " + (-past).ToString("F1", Inv) + " m inside the rim line"; }
                        else if (under) { r.verdict = "SURVIVES"; r.end = "at rest " + (startY - f.y).ToString("F1", Inv) + " m under the start, " + (w.Supported() ? "on walkable ground" : "on steep ground (a perch)"); }
                        else { r.verdict = "SURVIVES"; r.end = "at rest beyond the rim line, " + (startY - f.y).ToString("F1", Inv) + " m under the start (a ledge or the rim itself)"; }
                        break;
                    }
                    bool back = variant == "hug" && startY - f.y > .5f && past > 0f;
                    var dir = back ? inward : -inward;
                    if (!back && past >= spec.push) dir = Vector2.zero;      // far enough out: only gravity from here
                    var planar = new Vector3(dir.x, 0f, dir.y) * speed;
                    float vertical;
                    if (grounded && vy <= 0f) { vy = -w.Stick; vertical = vy * dt; }
                    else { vertical = vy * dt + .5f * w.Gravity * dt * dt; vy += w.Gravity * dt; }
                    var flags = w.CC.Move(planar * dt + Vector3.up * vertical);
                    if ((flags & CollisionFlags.Above) != 0 && vy > 0f) vy = 0f;
                    grounded = (flags & CollisionFlags.Below) != 0 && vy <= 0f;
                    if (!grounded && vy <= 0f && CloseSupport(w)) grounded = true;
                }
                var last = w.Feet; r.endX = last.x; r.endY = last.y; r.endZ = last.z;
                if (r.maxGain > spec.lipClimb) r.end += " (gained " + r.maxGain.ToString("F1", Inv) + " m on the way out: the capsule climbed onto something)";
                if (r.verdict == "INCONCLUSIVE" && r.end == "") r.end = "no rest inside " + spec.seconds.ToString("0.#", Inv) + " s, " + (startY - last.y).ToString("F1", Inv) + " m under the start";
                var around = w.Around(last, cfg.probe.touchPad);
                r.by = around.Length == 0 ? "nothing" : string.Join(",", around.OrderBy(c => ground.Is(c) ? 1 : 0).Take(3).Select(c => ground.Is(c) ? "terrain " + c.name : c.name + "@" + (c.transform.parent != null ? c.transform.parent.name : "-")));
                if (float.IsInfinity(r.maxPast)) r.maxPast = 0f;
                return r;
            }
            finally { w.Rest(); }
        }

        static string Rim(CliffCore308.Config cfg, string token, Dictionary<string, string> opt)
        {
            string path = CliffCore308.ScenePath(cfg, token);
            var scene = OpenForRead(cfg, path);
            var ledger = ReadLedger(cfg, path);
            string stageId = opt != null && opt.TryGetValue("stage", out string so) && so.Length > 0 ? so : ledger.stage != "" ? ledger.stage : StageOption(cfg, opt, ledger);
            var data = CliffCore308.LoadCheckData(cfg, stageId); var extra = LoadCheck1b(data);
            var spec = LoadConfig1b().rim;
            if (spec == null || spec.tick <= 0f || spec.push <= 0f || spec.seconds <= 0f || spec.lethalFall <= 0f || spec.creepSpeed <= 0f || spec.landedTicks < 1)
                throw new PostLedger308.Refused("config " + CliffCore308.ConfigPath + " has no rim block (inset, push, tick, seconds, creepSpeed, lethalFall, landedTicks, lipClimb, pastRim, fallPast): install the stage-1b config (Tools/Unity/Stage308_cliff1b/terrain/install1b.py)");
            if (extra.rims.Length == 0) throw new PostLedger308.Refused("the check data " + data.File + " names no rim (format cb308.check.2 of Tools/Unity/Stage308_cliff1b/terrain/Offline/cb308_stations.py)");
            string only = opt != null && opt.TryGetValue("id", out string io) ? io : "";
            int every = opt != null && opt.TryGetValue("every", out string eo) && int.TryParse(eo, out int ev) && ev > 0 ? ev : 1;
            int max = opt != null && opt.TryGetValue("max", out string mo) && int.TryParse(mo, out int mv) ? mv : int.MaxValue;
            var root = CliffCore308.TerrainRoot(scene, cfg); CliffCore308.Ground ground = root;
            var notes = new List<string>(); var rows = new List<RimRow>();
            var report = new RimReport { utc = PostLedger308.Utc(), scene = path, stage = stageId, lethalFall = spec.lethalFall, checkData = data.File, checkDataSha256 = data.Sha256,
                fallRule = "session rule as coded: death when (last grounded height - feet) >= lethalFall; the reference is refreshed on every grounded tick (contact below with a non-positive vertical velocity, steep ground included)" };
            var session = CliffCore308.Session(scene);
            if (session != null && session.Traversal != null && session.Traversal.Rules != null) report.sceneFatalFall = session.Traversal.Rules.FatalFallHeight;
            // #308 D308-18 (3): the probe follows the rule of the scene's traversal profile (FallReferenceWalkableOnly; a scene without one = the class default).
            // rule=coded / rule=walkable forces one of the two for a comparison run (the report says which).
            bool walkableOnly = session != null && session.Traversal != null && session.Traversal.Rules != null ? session.Traversal.Rules.FallReferenceWalkableOnly : true;
            if (opt != null && opt.TryGetValue("rule", out string ro) && ro.Length > 0)
            {
                if (ro != "coded" && ro != "walkable") throw new PostLedger308.Refused("rule=" + ro + ": use rule=coded or rule=walkable");
                walkableOnly = ro == "walkable"; notes.Add("fall rule forced by the command: rule=" + ro);
            }
            var rule = walkableOnly ? FallRuleOf(session) : null;
            if (rule != null) notes.Add("walkable-support test = the session's (WorldMacroPlaytestSession.FallSupport308): gap <= " + rule.maxGap.ToString("0.###", Inv) + " m, slope field " + rule.fieldDeg.ToString("0.#", Inv) + " deg (0 = the capsule's limit), " + rule.source);
            if (walkableOnly) report.fallRule = "D308-18 (3): death when (reference - feet) >= lethalFall; the reference follows the feet only while grounded ON WALKABLE SUPPORT (the walker's slope limit); contact with a steep face does not refresh it";
            if (report.sceneFatalFall > 0f && Mathf.Abs(report.sceneFatalFall - spec.lethalFall) > .001f) notes.Add("the scene's FatalFallHeight is " + report.sceneFatalFall.ToString("0.##", Inv) + " m, the config says " + spec.lethalFall.ToString("0.##", Inv) + " m: the verdicts use the config value");
            if (report.sceneFatalFall <= 0f) notes.Add("no WorldTerrainQuery rules on the session of this scene: without Traversal the session uses the last SAFE feet instead (WorldMacroPlaytestSession.cs:259) - a different rule, not probed here");
            CliffCore308.Stage stage = null; try { stage = CliffCore308.LoadStage(cfg, stageId, false); } catch (PostLedger308.Refused) { }
            string stale = CliffCore308.StaleCheckData(data, stage);
            if (stale != null) notes.Add("STALE CHECK DATA: " + stale);
            if (ledger.state != "applied" || ledger.stage != stageId) notes.Add("the scene ledger is '" + ledger.state + "' stage '" + ledger.stage + "': the rim of stage " + stageId + " does not stand in this scene");
            Physics.SyncTransforms();
            using (var w = new CliffCore308.Walker(scene, cfg))
            {
                report.walker = w.Source;
                foreach (var rim in extra.rims)
                {
                    if (only != "" && rim.id != only) continue;
                    int n = rim.sx.Length, done = 0;
                    for (int i = 0; i < n && done < max; i += every, done++)
                    {
                        var at = new Vector2(rim.sx[i], rim.sz[i]); var inward = new Vector2(rim.nx[i], rim.nz[i]).normalized;
                        var row = new RimRow { rim = rim.id, station = i, s = i * rim.step, x = at.x, z = at.y, plannedDrop = rim.drop != null && i < rim.drop.Length ? rim.drop[i] : 0f,
                            inBay = rim.inBay != null && i < rim.inBay.Length && rim.inBay[i] != 0, lipRequired = rim.lipRequired != null && i < rim.lipRequired.Length && rim.lipRequired[i] != 0 };
                        int foot = rim.footInReach != null && i < rim.footInReach.Length ? rim.footInReach[i] : -1;
                        row.landing = foot == 1 ? "EA side (inside the reach of " + rim.state + ")" : foot == 0 ? "BLOCKED SIDE (outside the reach of " + rim.state + ")" : "unknown";
                        var runs = new[] { "run", "creep", "hug" }.Select(v => RunRim(w, cfg, spec, ground, v, at, inward, spec.lethalFall, rule)).ToArray();
                        row.runs = runs;
                        // the station is as open as its most permissive run: SURVIVES > INCONCLUSIVE > LETHAL > BLOCKED
                        row.verdict = runs.Any(x => x.verdict == "SURVIVES") ? "SURVIVES" : runs.Any(x => x.verdict == "INCONCLUSIVE") ? "INCONCLUSIVE" : runs.Any(x => x.verdict == "LETHAL") ? "LETHAL" : "BLOCKED";
                        rows.Add(row); report.stations++;
                        if (row.inBay) report.bayStations++;
                        if (row.verdict == "BLOCKED") report.blocked++; else if (row.verdict == "LETHAL") report.lethal++; else if (row.verdict == "INCONCLUSIVE") report.inconclusive++;
                        else { report.survives++; if (foot == 0 && !row.inBay) report.survivesBlockedSide++; }
                        if (row.lipRequired && row.verdict != "BLOCKED") report.lipRequiredNotBlocked++;
                    }
                }
            }
            report.rows = rows.ToArray(); report.notes = notes.ToArray();
            // FAIL = a survivable way down to a blocked side, or a required lip that does not block; outside the declared lift window
            report.verdict = (report.survivesBlockedSide > 0 || report.lipRequiredNotBlocked > 0 ? "FAIL" : report.inconclusive > 0 ? "INCONCLUSIVE" : report.stations == 0 ? "NO STATIONS" : "CLEAR") + (stale != null ? " (STALE CHECK DATA)" : "");
            string json = LedgerFile(cfg, "rim-" + ledger.key), txt = LedgerFile(cfg, "rim-" + ledger.key, "txt");
            CliffCore308.WriteText(json, JsonUtility.ToJson(report, true));
            var sb = new StringBuilder();
            sb.AppendLine("#308 cliff boundary rim probe - " + path + " | stage " + stageId + " | ledger " + ledger.state + " | " + report.utc + " | automated Edit-mode capsule (" + report.walker + "), not manual play");
            sb.AppendLine("VERDICT " + report.verdict + ": stations " + report.stations + " (in the declared lift window " + report.bayStations + "), BLOCKED " + report.blocked + ", LETHAL " + report.lethal + ", SURVIVES " + report.survives
                + " (onto a blocked side, outside the lift window: " + report.survivesBlockedSide + "), INCONCLUSIVE " + report.inconclusive + ", required lip not blocking " + report.lipRequiredNotBlocked);
            sb.AppendLine("fall rule: " + report.fallRule + "; lethalFall " + spec.lethalFall.ToString("0.##", Inv) + " m (scene " + (report.sceneFatalFall > 0f ? report.sceneFatalFall.ToString("0.##", Inv) : "n/a") + ")");
            sb.AppendLine("per station three runs from " + spec.inset.ToString("0.#", Inv) + " m inside the rim: run (outward at run speed), creep (outward at " + spec.creepSpeed.ToString("0.#", Inv) + " m/s), hug (creep out, then push into the face). The station takes its most permissive run.");
            foreach (var row in rows.Where(x => x.verdict != "LETHAL" && !(x.verdict == "BLOCKED" && x.lipRequired)).Take(400))
                sb.AppendLine((row.verdict == "SURVIVES" && row.landing.StartsWith("BLOCKED", StringComparison.Ordinal) && !row.inBay ? "FAIL " : "     ") + row.verdict + " " + row.rim + " s=" + row.s.ToString("F0", Inv) + " (" + row.x.ToString("F1", Inv) + "," + row.z.ToString("F1", Inv) + ")"
                    + (row.inBay ? " [lift window]" : "") + (row.lipRequired ? " [lip required]" : "") + " planned drop " + row.plannedDrop.ToString("F1", Inv) + " m, lands " + row.landing + ": "
                    + string.Join("; ", row.runs.Select(x => x.variant + " " + x.verdict + " (" + x.end + ", past " + x.maxPast.ToString("F1", Inv) + " m, worst fall without contact " + x.worstUngroundedFall.ToString("F1", Inv) + " m, touching " + x.by + ")")));
            foreach (string note in notes) sb.AppendLine("NOTE " + note);
            CliffCore308.WriteText(txt, sb.ToString().TrimEnd());
            return "rim " + ledger.key + " stage " + stageId + ": " + report.verdict + " | stations " + report.stations + ", blocked " + report.blocked + ", lethal " + report.lethal + ", survives " + report.survives + " (blocked side " + report.survivesBlockedSide
                + "), inconclusive " + report.inconclusive + ", required lip not blocking " + report.lipRequiredNotBlocked + " | " + json + " (+ .txt) | scene dirty=" + scene.isDirty;
        }
    }
}
