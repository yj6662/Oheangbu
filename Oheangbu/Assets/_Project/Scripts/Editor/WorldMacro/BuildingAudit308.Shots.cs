using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 §A/§C: the capture list for the still sweep and the wall probe. Read only (raycasts and casts only, no GameObject).
    //   shots[:budget=300][:scene=<alias>] — priority 1: one eye-level still (+1.7 m over the ground, 12–25 m away) and one high
    //     oblique per FAIL/WARN cluster of the newest scan (checks in config shots.shotChecks); priority 2: config clusters and
    //     Architecture296 cameras.json; priority 3: config clusters marked 3. Budget truncates by priority (eye stills first).
    //     Each shot carries the Presentation297 command (shot:<name>:<eye>:<target>:fov=60:w=1600:h=900:hideplayer) that
    //     Tools/Unity/buildingaudit308_sweep.py sends one at a time. Eyes inside an InteriorSight307 sealed volume are marked.
    //   wallprobe:<path> — the player CC capsule (h 1.75, r .28) cast from 8 directions toward the target's centre.
    public static partial class BuildingAudit308
    {
        [Serializable] internal sealed class Shot308
        {
            public string name = "", group = "", kind = "", command = "", note = "";
            public int priority; public Vector3 eye, target; public float fov; public int w, h; public bool interior;
            public string[] findings = Array.Empty<string>();
        }
        [Serializable] internal sealed class ShotList308
        {
            public string scene = "", alias = "", audit = "", utc = "", quality = "";
            public int budget, planned, findingsToCover, findingsCovered;
            public List<Shot308> shots = new List<Shot308>();
            public List<string> uncovered = new List<string>();
            public List<string> notes = new List<string>();
        }
        [Serializable] sealed class View295Json { public string Id = "", Label = "", Scale = ""; public Vector3 Eye, Target; }
        [Serializable] sealed class Views295Json { public View295Json[] Views = Array.Empty<View295Json>(); }

        static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "") sb.Append(char.IsLetterOrDigit(c) && c < 128 ? c : '_');
            var t = sb.ToString().Trim('_');
            while (t.Contains("__")) t = t.Replace("__", "_");
            return t.Length > 48 ? t.Substring(t.Length - 48) : t;
        }

        static string ShotCommand(Shot308 s) => "shot:" + s.name + ":" + V(s.eye) + ":" + V(s.target) + ":fov=" + s.fov.ToString("0.#", Inv) + ":w=" + s.w + ":h=" + s.h + ":hideplayer";

        // an eye-level eye 12–25 m from the target, on the ground, with a clear line of sight (target's own colliders allowed)
        static Vector3 EyeFor(Vector3 target, Vector3 away, Shots308 cfg, out bool clear)
        {
            clear = false; Vector3 best = target + Vector3.back * cfg.eyeDistanceMin + Vector3.up * cfg.eyeHeight;
            var dirs = new List<Vector3>();
            var a0 = away; a0.y = 0; if (a0.sqrMagnitude < 1e-4f) a0 = Vector3.back; a0.Normalize();
            for (int k = 0; k < 8; k++) dirs.Add(Quaternion.Euler(0, (k % 2 == 0 ? 1 : -1) * ((k + 1) / 2) * 45f, 0) * a0);
            foreach (float d in new[] { (cfg.eyeDistanceMin + cfg.eyeDistanceMax) * .5f, cfg.eyeDistanceMin, cfg.eyeDistanceMax })
                foreach (var dir in dirs)
                {
                    var e = target + dir * d;
                    float g = TerrainTop(e); if (float.IsNaN(g)) continue;
                    // a walkable roof/deck over the terrain counts as ground
                    if (Physics.Raycast(new Vector3(e.x, target.y + 30f, e.z), Vector3.down, out var h, 60f, ~0, QueryTriggerInteraction.Ignore) && h.point.y > g && h.normal.y > .7f && h.point.y < target.y + 4f) g = h.point.y;
                    e.y = g + cfg.eyeHeight;
                    if (Physics.CheckSphere(e, .3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    var aim = target + Vector3.up * .8f;
                    if (Physics.Linecast(e, aim, out var block, ~0, QueryTriggerInteraction.Ignore) && Vector3.Distance(block.point, aim) > 4f) { best = e; continue; }
                    clear = true; return e;
                }
            return best;
        }

        static string Shots(string[] args)
        {
            var cfg = LoadConfig(out string err);
            if (cfg == null) return "refused: " + err;
            var sc = cfg.shots;
            int budget = int.TryParse(Opt(args, "budget") ?? "", System.Globalization.NumberStyles.Integer, Inv, out int bb) ? bb : sc.budget;
            var scene = SceneManager.GetActiveScene();
            string alias = Opt(args, "scene") ?? cfg.AliasOf(scene.path) ?? "main";
            string dir = NewestScan(alias);
            if (dir == null) return "refused: no scan of " + alias + " yet (run scan:scene=" + alias + ")";
            if (cfg.ScenePath(alias) != scene.path) return "refused: shots measure eyes against the open scene; open " + cfg.ScenePath(alias) + " first (active " + scene.path + ")";
            var report = LoadReport(dir);
            Physics.SyncTransforms();
            var list = new ShotList308 { scene = scene.path, alias = alias, audit = dir, utc = Utc(), budget = budget, quality = QualityName() };
            var sealedVolumes = AssetDatabase.FindAssets("t:InteriorSight307SO").Select(g => AssetDatabase.LoadAssetAtPath<InteriorSight307SO>(AssetDatabase.GUIDToAssetPath(g))).Where(x => x != null).ToArray();
            bool Interior(Vector3 e) => sealedVolumes.Any(v => { try { return v.IsSealed(e); } catch { return false; } });
            var all = new List<Shot308>();
            int n = 0;
            Shot308 Make(int priority, string group, string kind, Vector3 eye, Vector3 target, string note, string[] findings)
            {
                var s = new Shot308 { priority = priority, group = group, kind = kind, eye = eye, target = target, fov = sc.fov, w = sc.w, h = sc.h, note = note, findings = findings ?? Array.Empty<string>() };
                s.name = "b308_p" + priority + "_" + (++n).ToString("D4") + "_" + Safe(group) + "_" + kind;
                s.interior = Interior(eye); s.command = ShotCommand(s);
                return s;
            }
            // priority 1: FAIL/WARN clusters (findings within clusterMerge of each other share stills)
            var targets = report.findings.Where(f => (f.level == "FAIL" || f.level == "WARN") && sc.shotChecks.Contains(f.check) && f.position != Vector3.zero).ToList();
            list.findingsToCover = targets.Count;
            var clusters = new List<(Vector3 c, List<Finding308> f, bool fail)>();
            foreach (var f in targets.OrderByDescending(f => f.level == "FAIL"))
            {
                int k = clusters.FindIndex(c => Vector3.Distance(c.c, f.position) <= sc.clusterMerge);
                if (k < 0) clusters.Add((f.position, new List<Finding308> { f }, f.level == "FAIL"));
                else clusters[k].f.Add(f);
            }
            var eyes = new List<Shot308>(); var obliques = new List<Shot308>();
            foreach (var c in clusters.OrderByDescending(c => c.fail).ThenByDescending(c => c.f.Count))
            {
                var unit = report.units.FirstOrDefault(u => c.f.Any(f => f.unit == u.key));
                Vector3 centre = unit != null ? unit.centre : c.c;
                var away = c.c - centre; if (away.sqrMagnitude < .25f) away = Vector3.back;
                var ids = c.f.Select(f => f.check + "|" + f.level + "|" + f.path + "|" + V(f.position)).ToArray();
                string group = c.f[0].check + "_" + (string.IsNullOrEmpty(c.f[0].unit) ? c.f[0].path : c.f[0].unit).Split('/').Last();
                var eye = EyeFor(c.c, away, sc, out bool clear);
                eyes.Add(Make(1, group, c.fail ? "fail_eye" : "warn_eye", eye, c.c + Vector3.up * .8f, (clear ? "" : "line of sight partly blocked; ") + string.Join("; ", c.f.Take(3).Select(f => f.check + " " + f.detail)), ids));
                var a = away; a.y = 0; a = a.sqrMagnitude > 1e-4f ? a.normalized : Vector3.back;
                obliques.Add(Make(1, group, c.fail ? "fail_high" : "warn_high", c.c + a * sc.obliqueDistance + Vector3.up * sc.obliqueHeight, c.c, "high oblique", ids));
            }
            all.AddRange(eyes.Where(s => s.kind == "fail_eye")); all.AddRange(eyes.Where(s => s.kind != "fail_eye"));
            all.AddRange(obliques.Where(s => s.kind == "fail_high")); all.AddRange(obliques.Where(s => s.kind != "fail_high"));
            // priority 2/3: config clusters, then the 21 Architecture296 camera views
            foreach (var c in sc.clusters.OrderBy(c => c.priority))
            {
                if (c.target.Length < 3) continue;
                var t = Vec(c.target);
                if (float.IsNaN(t.y) || t.y == 0) { float g = TerrainTop(t); if (!float.IsNaN(g)) t.y = g; }
                if (c.eye.Length >= 3) { all.Add(Make(c.priority, c.name, "view", Vec(c.eye), t, c.note, null)); continue; }
                var dist = c.distance > 0 ? c.distance : (sc.eyeDistanceMin + sc.eyeDistanceMax) * .5f;
                var copy = new Shots308 { eyeDistanceMin = dist, eyeDistanceMax = dist, eyeHeight = sc.eyeHeight };
                var eye = c.height > 0 ? t + Vector3.back * dist + Vector3.up * c.height : EyeFor(t, Vector3.back, copy, out _);
                all.Add(Make(c.priority, c.name, c.height > 0 ? "high" : "eye", eye, t + Vector3.up * .8f, c.note, null));
            }
            string cams = string.IsNullOrEmpty(sc.camerasFile) ? null : Path.Combine(RepoRoot, sc.camerasFile);
            if (cams != null && File.Exists(cams))
                foreach (var v in JsonUtility.FromJson<Views295Json>(File.ReadAllText(cams).TrimStart('﻿')).Views)
                    all.Add(Make(sc.camerasPriority, "cam_" + v.Id, "view", v.Eye, v.Target, v.Label, null));
            else list.notes.Add("cameras file missing: " + cams);
            // budget: stable by priority, eye stills of priority 1 first (already ordered)
            var chosen = all.Select((s, i) => (s, i)).OrderBy(x => x.s.priority).ThenBy(x => x.i).Take(Mathf.Max(0, budget)).Select(x => x.s).ToList();
            list.shots = chosen; list.planned = chosen.Count;
            var covered = new HashSet<string>(chosen.SelectMany(s => s.findings));
            list.findingsCovered = targets.Count(f => covered.Contains(f.check + "|" + f.level + "|" + f.path + "|" + V(f.position)));
            list.uncovered = targets.Where(f => !covered.Contains(f.check + "|" + f.level + "|" + f.path + "|" + V(f.position))).Select(f => f.check + " " + f.level + " " + f.path + " @" + V(f.position)).Take(200).ToList();
            if (all.Count > chosen.Count) list.notes.Add((all.Count - chosen.Count) + " planned stills fell outside the budget " + budget);
            string outDir = Path.Combine(Folder, "Shots");
            Directory.CreateDirectory(outDir);
            string json = JsonUtility.ToJson(list, true);
            File.WriteAllText(Path.Combine(outDir, "shots-" + alias + "-" + list.utc + ".json"), json, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, "shots.json"), json, new UTF8Encoding(false));
            return "shots " + alias + ": " + list.planned + " planned of " + all.Count + " (budget " + budget + "), findings covered " + list.findingsCovered + "/" + list.findingsToCover + ", interior-marked " + chosen.Count(s => s.interior) + " -> " + Path.Combine(outDir, "shots.json");
        }

        // ------------------------------------------------------------------ wall probe

        [Serializable] internal sealed class Probe308 { public float yaw; public bool blocked, wallCrossed, passThrough; public string hit = ""; public float distance; public Vector3 start; }
        [Serializable] internal sealed class ProbeReport308 { public string target = "", scene = "", utc = "", quality = ""; public float height, radius, floorTop; public int directions, passThrough, blocked; public List<Probe308> probes = new List<Probe308>(); }

        // floor top of the target (its plate colliders) so the capsule rides over a raised stone base and meets the walls
        internal static float FloorTop(Transform target, Config308 cfg)
        {
            if (cfg == null) return float.NaN;
            var rc = cfg.roots.FirstOrDefault(r => r.name == target.root.name) ?? new Root308 { name = target.root.name, depth = 1 };
            var u = MakeUnit(target, rc, false);
            if (!u.hasBounds) return float.NaN;
            var plates = Plates(cfg.thresholds, cfg.tokens, u, out _);
            if (plates.Length == 0) return float.NaN;
            return PlateTop(plates, u.bounds.center.x, u.bounds.center.z, plates.Max(c => c.bounds.max.y) + .5f, plates.Min(c => c.bounds.min.y) - .5f);
        }

        internal static ProbeReport308 Probe(Transform target, float height, float radius, MeshCache308 cache, float floorTop = float.NaN)
        {
            var rs = target.GetComponentsInChildren<Renderer>(false).Where(x => x.enabled && !(x is ParticleSystemRenderer)).ToArray();
            var report = new ProbeReport308 { target = KeyOf(target), scene = target.gameObject.scene.path, utc = Utc(), quality = QualityName(), height = height, radius = radius, floorTop = floorTop };
            if (rs.Length == 0) return report;
            var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds);
            var soup = new Soup308(); foreach (var x in rs) soup.Add(x, cache);
            float reach = Mathf.Max(b.extents.x, b.extents.z) + 1.5f;
            Physics.SyncTransforms();
            for (int k = 0; k < 8; k++)
            {
                float yaw = k * 45f;
                var dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                var start = b.center + dir * reach; float g = TerrainTop(start);
                if (Physics.Raycast(new Vector3(start.x, b.max.y + 2f, start.z), Vector3.down, out var gh, b.size.y + 30f, ~0, QueryTriggerInteraction.Ignore) && gh.normal.y > .6f && (float.IsNaN(g) || gh.point.y > g)) g = gh.point.y;
                if (float.IsNaN(g)) g = b.min.y;
                if (!float.IsNaN(floorTop) && floorTop > g) g = floorTop;   // over the stone base, at floor level
                var feet = new Vector3(start.x, g + .05f, start.z);
                var p1 = feet + Vector3.up * radius; var p2 = feet + Vector3.up * (height - radius);
                var travel = -dir; float dist = reach - .3f;
                var probe = new Probe308 { yaw = yaw, start = feet };
                // the swept path crosses a visible wall when a horizontal ray at 1 m over the feet meets the target's faces
                probe.wallCrossed = soup.Ray(feet + Vector3.up * 1f, travel, dist, out float wallAt, out _);
                if (Physics.CapsuleCast(p1, p2, radius * .98f, travel, out var hit, dist, ~0, QueryTriggerInteraction.Ignore))
                {
                    probe.blocked = true; probe.hit = PathOf(hit.collider.transform); probe.distance = hit.distance;
                    // a hit beyond the visible wall still means the capsule went through that wall
                    if (probe.wallCrossed && hit.distance > wallAt + radius + .1f) probe.passThrough = true;
                }
                else probe.passThrough = probe.wallCrossed;
                report.probes.Add(probe);
            }
            report.directions = report.probes.Count; report.blocked = report.probes.Count(p => p.blocked); report.passThrough = report.probes.Count(p => p.passThrough);
            return report;
        }

        static string WallProbe(string path)
        {
            var scene = SceneManager.GetActiveScene();
            var t = Resolve(scene, path) ?? Harness303.Find(scene, path);
            if (t == null) return "refused: no " + path + " in " + scene.path;
            var cfg = LoadConfig(out _);
            float height = cfg?.fix?.f4 != null && cfg.fix.f4.probeHeight > 0 ? cfg.fix.f4.probeHeight : 1.75f, radius = cfg?.fix?.f4 != null && cfg.fix.f4.probeRadius > 0 ? cfg.fix.f4.probeRadius : .28f;
            var r = Probe(t, height, radius, new MeshCache308(), FloorTop(t, cfg));
            string dir = Path.Combine(Folder, "WallProbe"); Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, Safe(path) + "-" + r.utc + ".json");
            File.WriteAllText(file, JsonUtility.ToJson(r, true), new UTF8Encoding(false));
            var sb = new StringBuilder("wallprobe " + r.target + " (h " + height + " r " + radius + "): pass-through " + r.passThrough + "/" + r.directions + ", blocked " + r.blocked + " -> " + file + "\n");
            foreach (var p in r.probes) sb.AppendLine("  yaw " + p.yaw.ToString("F0", Inv) + ": " + (p.passThrough ? "PASS-THROUGH" : p.blocked ? "blocked by " + p.hit + " at " + F(p.distance, "F2") + " m" : "open (no visible wall on the line)") + (p.wallCrossed ? "" : " [no wall face on the line]"));
            return sb.ToString();
        }
    }
}
