using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.Demo;
using Oheangbu.App.World;

namespace Oheangbu.EditorTools.WorldMacro
{
    // verify:<scene> - read only. Everything is measured on the colliders that are in the scene (the prisms, the terrain tiles, whatever
    // else stands there); the data only says where to look and what the rule is. A row is PASS / FAIL / INFO / SKIP. The run is green when
    // no row is FAIL; `lift-on` needs a green run on the very scene file. Capsule runs use the CliffCore308 walker (the player's own
    // CharacterController and motor numbers, variants walk / jump / guk+jump of cb308.json).
    public static partial class CliffGuk308
    {
        sealed class Rows
        {
            public readonly List<string> Lines = new List<string>(); public int Pass, Fail, Info, Skip;
            public void Add(bool ok, string id, string text) { if (ok) Pass++; else Fail++; Lines.Add((ok ? "PASS " : "FAIL ") + id + ": " + text); }
            public void Note(string id, string text) { Info++; Lines.Add("INFO " + id + ": " + text); }
            public void Skipped(string id, string text) { Skip++; Lines.Add("SKIP " + id + ": " + text); }
        }

        static string Verify(CliffCore308.Config cfg, string scenePath)
        {
            var p = Load(); string key = CliffCore308.SceneKey(cfg, scenePath);
            var scene = Pieces308.OpenTarget(scenePath, p.Data["preview_roots"], false);
            var roots = Pieces308.FindAll(scene, p.RootName);
            if (roots.Count != 1 || roots[0].Find(p.Up1Name) == null) throw new PostLedger308.Refused("nothing to verify: " + p.RootName + "/" + p.Up1Name + " is not in " + scenePath + " (run apply:" + key + ")");
            var root = roots[0]; var rows = new Rows(); var player = Pieces308.Req(p.Data, "player");
            Physics.SyncTransforms();

            // ---- the scene carries this data
            rows.Add(Matches(root, p, out string why), "data", why.Length == 0 ? "prisms, colliders and lift nodes equal guk308.json " + Pieces308.Short(p.Sha) : why);
            foreach (string s in Stale(p)) rows.Add(false, "sources", s);

            // ---- colliders inside the visible mass: the visible mesh of a group is exactly the union of its prisms, and every BoxCollider is one of them
            foreach (string g in p.Groups)
            {
                var mf = root.Find(g + "/Visual")?.GetComponent<MeshFilter>(); var want = PrismMesh(p.Pieces.Where(x => x.Group == g));
                bool same = mf != null && mf.sharedMesh != null && CliffCore308.MeshContentSha(mf.sharedMesh) == CliffCore308.MeshContentSha(want);
                UnityEngine.Object.DestroyImmediate(want);
                var cols = root.Find(g).GetComponentsInChildren<Collider>(true); int foreign = cols.Count(c => !(c is BoxCollider) || p.Pieces.All(x => x.Id != c.name));
                rows.Add(same && foreign == 0, "inside-visible " + g, (same ? "the visible mesh is the prisms of the data" : "the visible mesh is NOT the prisms of the data") + "; " + cols.Length + " collider(s), " + foreign + " that are not a prism of the data");
            }

            // ---- lift: GukLiftSite.Valid and what FieldSpellService.TryPrepare asks of the place
            var site = root.Find(p.Up1Name + "/Guk_" + p.LiftId)?.GetComponent<GukLiftSite>();
            float radius, height;
            using (var w = new CliffCore308.Walker(scene, cfg)) { radius = w.CC.radius; height = w.CC.height; }
            if (site == null) rows.Add(false, "lift", "no GukLiftSite " + p.LiftId);
            else
            {
                float lateral = Vector2.Distance(new Vector2(site.Lower.position.x, site.Lower.position.z), new Vector2(site.Upper.position.x, site.Upper.position.z));
                rows.Add(site.Valid, "lift.valid", "GukLiftSite.Valid " + site.Valid + ": rise " + Pieces308.F(site.Height) + " m, Lower / Upper " + Pieces308.F(lateral, "F3") + " m apart, CastRadius " + Pieces308.F(site.CastRadius) + ", RiseSeconds " + Pieces308.F(site.RiseSeconds, "F1") + ", component " + (site.enabled ? "ENABLED" : "disabled"));
                bool lowerOk = LiftGround(site.Lower.position, out var lowerHit); float dx = Pieces308.Num(player, "deck_half_x"), dz = Pieces308.Num(player, "deck_half_z"); int edges = 0;
                foreach (var off in new[] { Vector3.right * dx, Vector3.left * dx, Vector3.forward * dz, Vector3.back * dz })
                    if (LiftGround(site.Lower.position + off, out var e) && Mathf.Abs(e.point.y - lowerHit.point.y) <= .1f) edges++;
                rows.Add(lowerOk && Mathf.Abs(lowerHit.point.y - site.Lower.position.y) <= .16f && edges == 4, "lift.lower", "ground under Lower " + (lowerOk ? Pieces308.F(lowerHit.point.y, "F3") + " on " + lowerHit.collider.name : "none") + " (node " + Pieces308.F(site.Lower.position.y, "F3") + ", rule 0.16), deck edges level " + edges + "/4 (rule 0.10)");
                bool upperOk = LiftGround(site.Upper.position, out var upperHit);
                rows.Add(upperOk && Mathf.Abs(upperHit.point.y - site.Upper.position.y) <= .16f, "lift.upper", "landing under Upper " + (upperOk ? Pieces308.F(upperHit.point.y, "F3") + " on " + upperHit.collider.name : "none") + " (node " + Pieces308.F(site.Upper.position.y, "F3") + ", rule 0.16)");
                float pad = Pieces308.Num(player, "clear_capsule_pad"); int blocked = 0; string first = "";
                int n = Mathf.CeilToInt(site.Height / .1f);
                for (int i = 0; i <= n; i++)
                {
                    var hit = CapsuleAt(site.Lower.position + Vector3.up * (site.Height * i / n), radius + pad, height);
                    if (hit != null) { blocked++; if (first.Length == 0) first = hit.name + " at +" + Pieces308.F(site.Height * i / n) + " m"; }
                }
                rows.Add(blocked == 0, "lift.column", "capsule sweep up the Lower column (" + (n + 1) + " samples, radius " + Pieces308.F(radius + pad, "F3") + "): " + (blocked == 0 ? "clear" : blocked + " blocked, first " + first));
                var up = CapsuleAt(site.Upper.position, radius + pad, height);
                rows.Add(up == null, "lift.landing", "capsule at Upper: " + (up == null ? "clear" : "blocked by " + up.name));
            }

            // ---- standable pieces (AC-B10b) and risers
            var rule = new Pieces308.GroundRule(scene);
            var tops = new Dictionary<string, float>();
            foreach (var s in Pieces308.Arr(p.Data, "standable"))
            {
                string id = Pieces308.Str(s, "id"); var c = Pieces308.V3(Pieces308.Req(s, "top_centre"));
                var piece = p.Pieces.First(x => x.Id == id); bool hit = TopOf(root, piece, out float y, out string by);
                tops[id] = y;
                rows.Add(hit && Mathf.Abs(y - c.y) <= .02f, "standable " + id, "top centre " + Pieces308.V(c) + ", measured top " + (hit ? Pieces308.F(y, "F3") + " (" + by + ")" : "no hit") + ", belongs to " + Pieces308.Str(s, "belongs_to"));
            }
            var desc = Pieces308.Req(p.Data, "descent"); var ruleM = Pieces308.Req(Pieces308.Req(desc, "rule"), "drop_m"); float lo = Pieces308.At(ruleM, 0), hi = Pieces308.At(ruleM, 1);
            var dt = (Pieces308.Req(desc, "tops") as JArray).Select(t => t.Value<float>()).ToArray();
            for (int i = 0; i + 1 < dt.Length; i++) rows.Add(dt[i] - dt[i + 1] >= lo && dt[i] - dt[i + 1] <= hi, "riser " + i, Pieces308.F(dt[i] - dt[i + 1]) + " m between the tops " + Pieces308.F(dt[i]) + " and " + Pieces308.F(dt[i + 1]) + " (rule " + Pieces308.F(lo, "F1") + "-" + Pieces308.F(hi, "F1") + ")");
            var landing = Pieces308.Req(desc, "landing"); var lxz = Pieces308.Req(landing, "xz");
            if (Pieces308.Ground(rule, Pieces308.At(lxz, 0), Pieces308.At(lxz, 1), 1.5f, out var lg))
                rows.Add(dt[dt.Length - 1] - lg.point.y >= lo && dt[dt.Length - 1] - lg.point.y <= hi, "riser ground", Pieces308.F(dt[dt.Length - 1] - lg.point.y) + " m from the last top to the measured ground " + Pieces308.F(lg.point.y) + " at the declared landing (declared " + Pieces308.F(Pieces308.Num(landing, "ground_y")) + ")");
            else rows.Add(false, "riser ground", "no ground at the declared landing");

            // ---- capsule runs
            Probes(cfg, scene, p, rule, rows);

            // ---- rules and counts
            var renderers = root.Find(p.Up1Name).GetComponentsInChildren<MeshRenderer>(true).Concat(root.Find(p.LipsName) != null ? root.Find(p.LipsName).GetComponentsInChildren<MeshRenderer>(true) : new MeshRenderer[0]).ToArray();
            int tris = 0, batches = 0, casters = 0, emissive = 0;
            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>(); bool lower = r.name == "LOD1";
                if (!lower) { batches++; if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3; if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) casters++; }
                foreach (var m in r.sharedMaterials) if (m != null && (m.IsKeywordEnabled("_EMISSION") || m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > 0f)) emissive++;
            }
            var mine = new[] { root.Find(p.Up1Name), root.Find(p.LipsName) }.Where(t => t != null).ToArray();
            int colliders = mine.Sum(t => t.GetComponentsInChildren<Collider>(true).Length), lights = mine.Sum(t => t.GetComponentsInChildren<Light>(true).Length), points = mine.Sum(t => t.GetComponentsInChildren<WorldMacroContentPoint>(true).Length);
            var limit = Pieces308.Req(Pieces308.Req(p.Data, "budget"), "limits");
            rows.Add(emissive == 0 && lights == 0 && points == 0, "rules", "emissive materials " + emissive + ", lights " + lights + ", content points (prompt) " + points);
            rows.Add(tris <= Pieces308.Num(limit, "lod0_triangles") && batches <= Pieces308.Num(limit, "batches") && colliders <= Pieces308.Num(limit, "colliders"), "budget",
                "LOD0 triangles " + tris + " / " + Pieces308.F(Pieces308.Num(limit, "lod0_triangles"), "F0") + ", batches (renderers at LOD0) " + batches + " / " + Pieces308.F(Pieces308.Num(limit, "batches"), "F0") + ", colliders " + colliders + " / " + Pieces308.F(Pieces308.Num(limit, "colliders"), "F0") + ", shadow casters " + casters);
            foreach (var c in Pieces308.Arr(Pieces308.Req(p.Data, "content_needed"), "rows"))
                rows.Note("content " + Pieces308.Str(c, "id"), "proposal " + Pieces308.Str(c, "kind") + " at " + Pieces308.V(Pieces308.V3(Pieces308.Req(c, "position"))) + ", " + Pieces308.Opt(c, "rim_distance_m") + " m from the rim (content track; lift-on needs content=ok)");
            if (scene.isDirty) rows.Add(false, "read-only", "the scene is dirty after a read-only command (bug): reload it");

            bool green = rows.Fail == 0;
            var sb = new StringBuilder("verify " + scenePath + " | data " + Pieces308.Short(p.Sha) + " | " + (green ? "GREEN" : "RED") + ": PASS " + rows.Pass + ", FAIL " + rows.Fail + ", INFO " + rows.Info + ", SKIP " + rows.Skip + "\n");
            foreach (string l in rows.Lines) sb.AppendLine("  " + l);
            Pieces308.WriteLedger(VerifyName(key, "stamp.json"), new VerifyStamp { scene = scenePath, utc = PostLedger308.Utc(), sceneSha256 = Pieces308.ShaAsset(scenePath), dataSha256 = p.Sha, green = green, fail = rows.Fail, notRun = rows.Skip });
            return Pieces308.Report(VerifyName(key, "txt"), sb);
        }

        /// <summary>FieldSpellService.Ground: the nearest support within 0.25 m above / 0.35 m below the point, no steeper than 20 degrees.</summary>
        static bool LiftGround(Vector3 point, out RaycastHit best)
        {
            best = default; float nearest = float.PositiveInfinity; bool found = false;
            foreach (var h in Physics.RaycastAll(point + Vector3.up * .25f, Vector3.down, .6f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider is CharacterController || !Pieces308.Saved(h.collider.transform) || h.collider.attachedRigidbody != null || h.normal.y < Mathf.Cos(20f * Mathf.Deg2Rad) || h.distance >= nearest) continue;
                nearest = h.distance; best = h; found = true;
            }
            return found;
        }
        /// <summary>FieldSpellService.ClearCapsule at a feet point: the first collider inside, or null.</summary>
        static Collider CapsuleAt(Vector3 feet, float radius, float height)
        {
            Vector3 low = feet + Vector3.up * (radius + .04f), high = feet + Vector3.up * (Mathf.Max(radius, height - radius) + .04f);
            foreach (var c in Physics.OverlapCapsule(low, high, radius, ~0, QueryTriggerInteraction.Ignore))
                if (!(c is CharacterController) && Pieces308.Saved(c.transform)) return c;
            return null;
        }
        /// <summary>The top of a standable prism: the first hit under a point 1 m above its centre, where the top is open.</summary>
        static bool TopOf(Transform root, Piece piece, out float y, out string by)
        {
            y = float.NaN; by = "";
            var rot = Quaternion.Euler(0f, piece.Yaw, 0f);
            // the centre may lie under the terrain (the inner part of a level is buried): try the centre, then points toward the outer side
            foreach (float f in new[] { 0f, .25f, .4f })
            {
                var at = piece.Centre + rot * new Vector3(piece.Size.x * f, 0f, 0f); at.y = piece.Top + 1f;
                if (!Physics.Raycast(at, Vector3.down, out var h, 3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (h.collider.name != piece.Id) { by = h.collider.name + " covers the centre"; continue; }
                y = h.point.y; by = "collider " + h.collider.name; return true;
            }
            return false;
        }

        static void Probes(CliffCore308.Config cfg, Scene scene, Plan p, Pieces308.GroundRule rule, Rows rows)
        {
            var list = Pieces308.Arr(p.Data, "probes").ToList();
            if (list.Count == 0) { rows.Add(false, "probes", "the data holds no probe"); return; }
            CliffCore308.Ground terrain = CliffCore308.TerrainRoot(scene, cfg);
            float fatal = Pieces308.Num(Pieces308.Req(p.Data, "player"), "fatal_fall_m");
            using (var w = new CliffCore308.Walker(scene, cfg))
            {
                rows.Note("walker", w.Source + ": capsule " + Pieces308.F(w.CC.height) + " x " + Pieces308.F(w.CC.radius) + ", run " + Pieces308.F(w.Speed) + " m/s, jump " + Pieces308.F(w.JumpHeight) + " m");
                foreach (var pr in list)
                {
                    string id = Pieces308.Str(pr, "id"), kind = Pieces308.Str(pr, "kind"); var from = Pieces308.Req(pr, "frm") as JArray;
                    float x = from[0].Value<float>(), z = from[2].Value<float>(); Vector3 feet;
                    if (from[1].Type == JTokenType.Null)
                    {
                        if (!Pieces308.Ground(rule, x, z, 1.5f, out var g) || g.normal.y < Mathf.Cos(w.CC.slopeLimit * Mathf.Deg2Rad)) { rows.Skipped(id, "no walkable ground under the start (" + Pieces308.F(x) + ", " + Pieces308.F(z) + "): this side is the face or the void"); continue; }
                        feet = g.point + Vector3.up * cfg.probe.startLift;
                    }
                    else feet = new Vector3(x, from[1].Value<float>(), z);
                    var way = (Pieces308.Req(pr, "way") as JArray).Select(t => new Vector2(Pieces308.At(t, 0), Pieces308.At(t, 1))).ToArray();
                    var dir = (way[0] - new Vector2(feet.x, feet.z)).normalized; float limit = pr["limit_m"] != null && pr["limit_m"].Type != JTokenType.Null ? pr["limit_m"].Value<float>() : float.PositiveInfinity;
                    Func<Vector3, float> past = pos => Vector2.Dot(new Vector2(pos.x - feet.x, pos.z - feet.z), dir) - limit;
                    var must = new HashSet<string>(Pieces308.Arr(pr, "must").Select(t => t.Value<string>()));
                    foreach (var name in Pieces308.Arr(pr, "variants").Select(t => t.Value<string>()))
                    {
                        var variant = cfg.probe.variants.FirstOrDefault(v => v.name == name);
                        if (variant == null) { rows.Skipped(id + " " + name, "cb308.json has no variant of that name"); continue; }
                        var r = CliffCore308.RunWalker(w, cfg, variant, terrain, feet, true, way, past, kind == "descent" ? 0f : cfg.probe.gainSeconds);
                        bool ok; string text;
                        if (kind == "descent")
                        {
                            // a jumping capsule may be in the air when the run ends: it counts up to one jump above the level
                            float fall = feet.y - r.Last.y, above = variant.jump ? w.JumpHeight + .35f : .35f; bool level;
                            if (Pieces308.Flag(pr, "expect_ground")) level = Pieces308.Ground(rule, r.Last.x, r.Last.z, 1.5f, out var g) && r.Last.y - g.point.y <= above && r.Last.y - g.point.y >= -.35f && r.Reached;
                            else { float want = Pieces308.Num(pr, "expect_y"); level = r.Last.y - want <= above && r.Last.y - want >= -.35f; }
                            ok = level && fall < fatal && r.End != "fell";
                            text = "from " + Pieces308.Str(pr, "from_piece") + " to " + Pieces308.Str(pr, "to_piece") + ": ends at " + Pieces308.V(r.Last) + ", fall " + Pieces308.F(fall) + " m (< " + Pieces308.F(fatal, "F0") + "), " + r.End + (level ? "" : " - NOT on the level below");
                        }
                        else if (kind == "climb")
                        {
                            float riser = Pieces308.Num(pr, "riser_m"); ok = r.MaxGain < riser - .3f;
                            text = "from " + Pieces308.Str(pr, "from_piece") + " back up to " + Pieces308.Str(pr, "to_piece") + ": gained " + Pieces308.F(r.MaxGain) + " m of the " + Pieces308.F(riser) + " m riser (" + r.Jumps + " jumps, " + r.Lifts + " lifts), stopped by " + r.By;
                        }
                        else
                        {
                            float top = Pieces308.Num(pr, "top_y"); ok = r.MaxPast <= 0f && r.MaxGain < top - feet.y - .2f;
                            text = "against " + Pieces308.Str(pr, "to_piece") + " from " + Pieces308.Str(pr, "from_piece") + ": " + (r.MaxPast <= 0f ? "stayed " + Pieces308.F(-r.MaxPast) + " m short of the far face" : "PASSED the far face by " + Pieces308.F(r.MaxPast) + " m") + ", gained " + Pieces308.F(r.MaxGain) + " m of " + Pieces308.F(top - feet.y) + ", stopped by " + r.By;
                        }
                        if (must.Contains(name)) rows.Add(ok, id + " " + name, text);
                        else rows.Note(id + " " + name, (ok ? "ok: " : "NOT ok: ") + text);
                    }
                }
            }
        }
    }
}
