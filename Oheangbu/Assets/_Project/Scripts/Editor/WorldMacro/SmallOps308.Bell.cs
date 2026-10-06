using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // (b) temple bell of 청림 산사 (D308-9e, DEFECTS_308 row 0013: the bell hangs in the air). The bell is a prefab instance under
    // the protected tree root Reworld292_Sansa. This op changes ONE thing: the local position override of that one instance, named by
    // its exact hierarchy path AND its source prefab GUID, inside the three candidate scene files. It writes no file of a protected
    // tree and refuses everything else under a protected root. That is a second by-name exception next to the D308-9c terrain tiles:
    // it is switched on by `bell.user_confirmed` in the data, which stays false until the user has said yes.
    // Where the bell goes is MEASURED: the lowest surface of the host (its renderers' triangles - beams carry no colliders) over a
    // probe point, the bell's top a hair under it, and enough room over the floor. No beam found = refused (the data holds a
    // fallback place for the user to choose; it is never applied silently).
    public static partial class SmallOps308
    {
        [Serializable] sealed class BellLedger
        {
            public string format = "cb308.small.bell.1", scene = "", key = "", state = "none", utc = "", configSha256 = "", path = "", sourceGuid = "", host = "", sceneShaBefore = "", sceneShaAfter = "", backup = "";
            public Vector3 localBefore, localAfter, worldAfter; public int bx, by, bz; public float beamY, floorY; public string[] history = new string[0];
        }

        sealed class BellPlan
        {
            public Scene Scene; public Transform Bell; public Bounds Bounds; public bool Found; public Vector3 World, Local; public float BeamY, FloorY;
            public readonly List<string> Lines = new List<string>(), Refuse = new List<string>();
        }

        /// <summary>The named instance and nothing else: exact path, a prefab instance root, the source prefab of the data.</summary>
        static Transform BellObject(Scene scene, JToken b)
        {
            var t = Pieces308.FindOne(scene, Pieces308.Str(b, "path"), "temple bell");
            if (!PrefabUtility.IsPartOfPrefabInstance(t.gameObject) || PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject) != t.gameObject) throw new PostLedger308.Refused(Pieces308.Str(b, "path") + " is not a prefab instance root");
            var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject); string guid = src != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(src)) : "";
            if (guid != Pieces308.Str(b, "source_guid")) throw new PostLedger308.Refused("the object at " + Pieces308.Str(b, "path") + " comes from prefab " + guid + ", the data names " + Pieces308.Str(b, "source_guid") + ": not the bell this exception is for");
            return t;
        }

        static BellPlan BellMeasure(JObject data, string scenePath, bool write)
        {
            var b = Pieces308.Req(data, "bell"); var pr = Pieces308.Req(b, "probe"); var k = new BellPlan();
            k.Scene = Pieces308.OpenTarget(scenePath, data["preview_roots"], write);
            k.Bell = BellObject(k.Scene, b);
            var renderers = k.Bell.GetComponentsInChildren<MeshRenderer>(true); if (renderers.Length == 0) throw new PostLedger308.Refused("the bell has no renderer");
            k.Bounds = renderers[0].bounds; foreach (var r in renderers) k.Bounds.Encapsulate(r.bounds);
            float scale = Pieces308.Num(b, "expect_scale");
            if (Mathf.Abs(k.Bell.localScale.x - scale) > 1e-3f) k.Refuse.Add("the bell's scale is " + Pieces308.F(k.Bell.localScale.x, "F3") + ", the data expects " + Pieces308.F(scale, "F1") + " (this op never changes the scale)");
            k.Lines.Add("bell " + Pieces308.Str(b, "path") + ": local " + Pieces308.V(k.Bell.localPosition) + ", world " + Pieces308.V(k.Bell.position) + ", bounds y " + Pieces308.F(k.Bounds.min.y) + ".." + Pieces308.F(k.Bounds.max.y) + " (" + Pieces308.F(k.Bounds.size.y) + " m tall)");

            var frame = Pieces308.FindOne(k.Scene, Pieces308.Str(pr, "frame"), "probe frame"); var host = Pieces308.FindOne(k.Scene, Pieces308.Str(pr, "host"), "bell host");
            var lxz = Pieces308.Req(pr, "local_xz"); var at = frame.TransformPoint(new Vector3(Pieces308.At(lxz, 0), 0f, Pieces308.At(lxz, 1)));
            var rule = new Pieces308.GroundRule(k.Scene, k.Bell);
            if (!Pieces308.Ground(rule, at.x, at.z, Pieces308.Num(pr, "support_window_m"), out var floor)) { k.Refuse.Add("no floor under the probe point (" + Pieces308.F(at.x) + ", " + Pieces308.F(at.z) + ")"); return k; }
            k.FloorY = floor.point.y;
            // the lowest surface of the host over the bell's footprint, between the search heights (renderer triangles, both faces)
            float lo = Pieces308.At(pr["search_up_m"], 0), hi = Pieces308.At(pr["search_up_m"], 1), rad = Pieces308.Num(pr, "footprint_radius_m");
            var cache = new BuildingAudit308.MeshCache308(); float beam = float.PositiveInfinity; string beamOwner = ""; int rays = 0, hits = 0;
            var meshes = host.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null && f.GetComponent<MeshRenderer>() != null && f.GetComponent<MeshRenderer>().enabled && f.gameObject.activeInHierarchy).ToArray();
            foreach (var off in new[] { Vector3.zero, Vector3.right * rad, Vector3.left * rad, Vector3.forward * rad, Vector3.back * rad })
            {
                var o = new Vector3(at.x + off.x, k.FloorY + lo, at.z + off.z); rays++; float best = float.PositiveInfinity; string owner = "";
                foreach (var f in meshes)
                {
                    var bb = f.GetComponent<MeshRenderer>().bounds; if (o.x < bb.min.x || o.x > bb.max.x || o.z < bb.min.z || o.z > bb.max.z || bb.max.y < o.y) continue;
                    var tri = cache.Get(f.sharedMesh); if (tri == null) continue; var m = f.transform.localToWorldMatrix;
                    for (int i = 0; i + 2 < tri.t.Length; i += 3)
                        if (BuildingAudit308.RayTri(o, Vector3.up, m.MultiplyPoint3x4(tri.v[tri.t[i]]), m.MultiplyPoint3x4(tri.v[tri.t[i + 1]]), m.MultiplyPoint3x4(tri.v[tri.t[i + 2]]), out float t) && t < best) { best = t; owner = f.name; }
                }
                if (best <= hi - lo) { hits++; if (o.y + best < beam) { beam = o.y + best; beamOwner = owner; } }
            }
            k.Lines.Add("probe at " + Pieces308.V(new Vector3(at.x, k.FloorY, at.z)) + " (floor on " + floor.collider.name + "), host " + Pieces308.Str(pr, "host") + ": " + meshes.Length + " renderer meshes, " + cache.Failed + " unreadable, " + hits + "/" + rays + " rays met a surface " + Pieces308.F(lo, "F1") + "-" + Pieces308.F(hi, "F1") + " m over the floor");
            if (hits < rays) { k.Refuse.Add("no beam over the whole bell footprint at the probe point (" + hits + " of " + rays + " rays): the host has no surface there between " + Pieces308.F(lo, "F1") + " and " + Pieces308.F(hi, "F1") + " m. Choose another probe point (bell.probe.local_xz) or the fallback with the user"); return k; }
            k.BeamY = beam; k.Found = true;
            float top = beam - Pieces308.Num(pr, "hang_gap_m"), topOffset = k.Bounds.max.y - k.Bell.position.y, bottomOffset = k.Bounds.min.y - k.Bell.position.y;
            var centreOffset = new Vector3(k.Bounds.center.x - k.Bell.position.x, 0f, k.Bounds.center.z - k.Bell.position.z);
            k.World = new Vector3(at.x - centreOffset.x, top - topOffset, at.z - centreOffset.z); k.Local = k.Bell.parent.InverseTransformPoint(k.World);
            float clear = k.World.y + bottomOffset - k.FloorY;
            k.Lines.Add("beam underside " + Pieces308.F(beam, "F3") + " (" + beamOwner + "), bell top " + Pieces308.F(top, "F3") + ", bottom " + Pieces308.F(k.World.y + bottomOffset, "F3") + " = " + Pieces308.F(clear) + " m over the floor " + Pieces308.F(k.FloorY, "F3"));
            k.Lines.Add("target: world " + Pieces308.V(k.World) + ", local " + Pieces308.V(k.Local) + " (moves " + Pieces308.F(Vector3.Distance(k.Bell.position, k.World)) + " m)");
            if (clear < Pieces308.Num(pr, "floor_clear_m")) k.Refuse.Add("the bell would hang " + Pieces308.F(clear) + " m over the floor (rule " + Pieces308.F(Pieces308.Num(pr, "floor_clear_m")) + "): the beam is too low for a " + Pieces308.F(k.Bounds.size.y) + " m bell");
            return k;
        }

        static string Bell(JObject data, string dataSha, string scenePath, string key, string verb)
        {
            string name = LedgerName("bell", key); var b = Pieces308.Req(data, "bell");
            if (verb == "revert")
            {
                var l0 = Pieces308.ReadLedger<BellLedger>(name); if (l0 == null || l0.state != "applied") throw new PostLedger308.Refused("no applied ledger " + name);
                var scene = Pieces308.OpenTarget(scenePath, data["preview_roots"], true); var t = BellObject(scene, b);
                string backup0 = Pieces308.Backup(PostLedger308.Utc() + "-bell-revert-" + key, scenePath);
                try { t.localPosition = new Vector3(FromBits(l0.bx), FromBits(l0.by), FromBits(l0.bz)); PrefabUtility.RecordPrefabInstancePropertyModifications(t); Pieces308.SaveOrReload(scene); }
                catch { Pieces308.Reload(scene); throw; }
                string now = Pieces308.ShaAsset(scenePath); string archived = Pieces308.Archive(name);
                return "bell revert " + scenePath + ": local position back to " + Pieces308.V(t.localPosition) + " | scene sha " + Pieces308.Short(now) + (now == l0.sceneShaBefore ? " (= the bytes before apply)" : " (before apply " + Pieces308.Short(l0.sceneShaBefore) + ")") + " | backup " + backup0 + " | ledger archived " + archived;
            }
            bool dry = verb != "apply";
            var k = BellMeasure(data, scenePath, !dry);
            var sb = new StringBuilder("bell " + verb + " " + scenePath + "\n"); foreach (string s in k.Lines) sb.AppendLine("  " + s);
            sb.AppendLine("  by-name exception: " + Pieces308.Str(b, "path") + " (prefab " + Pieces308.Str(b, "source_guid") + ") stands under a protected tree root; user_confirmed = " + Pieces308.Flag(b, "user_confirmed"));
            foreach (string r in k.Refuse) sb.AppendLine("  REFUSE " + r);
            var ledger = Pieces308.ReadLedger<BellLedger>(name);
            if (verb == "verify")
            {
                int fail = 0; void Row(bool ok, string text) { if (!ok) fail++; sb.AppendLine("  " + (ok ? "PASS " : "FAIL ") + text); }
                Row(ledger != null && ledger.state == "applied" && Vector3.Distance(k.Bell.localPosition, ledger.localAfter) <= 1e-3f, "the bell stands where the ledger put it (" + (ledger == null ? "no ledger" : Pieces308.V(ledger.localAfter)) + ")");
                Row(k.Found && Mathf.Abs(k.BeamY - k.Bounds.max.y - Pieces308.Num(Pieces308.Req(b, "probe"), "hang_gap_m")) <= .03f, "its top " + Pieces308.F(k.Bounds.max.y, "F3") + " hangs under the beam " + (k.Found ? Pieces308.F(k.BeamY, "F3") : "(none found)"));
                Row(k.Bounds.min.y - k.FloorY >= Pieces308.Num(Pieces308.Req(b, "probe"), "floor_clear_m"), "its bottom is " + Pieces308.F(k.Bounds.min.y - k.FloorY) + " m over the floor");
                Row(!k.Scene.isDirty, "the scene is clean after the read-only command");
                sb.Insert(0, (fail == 0 ? "GREEN " : "RED "));
                return Pieces308.Report("cb308-small-bell-verify-" + key + ".txt", sb);
            }
            bool there = k.Found && Vector3.Distance(k.Bell.position, k.World) <= Pieces308.Num(b, "expect_tol_m");
            if (there) return sb.Append("  변경 없음 (the bell hangs under the beam; nothing written, scene not saved)").ToString();
            if (ledger != null && ledger.state == "applied") k.Refuse.Add("a ledger says the bell was already moved to " + Pieces308.V(ledger.localAfter) + " but it does not hang under the beam now: run bell:revert:" + key + " first");
            else if (Vector3.Distance(k.Bell.localPosition, Pieces308.V3(Pieces308.Req(b, "expect_local"))) > Pieces308.Num(b, "expect_tol_m")) k.Refuse.Add("the bell is at local " + Pieces308.V(k.Bell.localPosition) + ", not at the recorded " + Pieces308.V(Pieces308.V3(Pieces308.Req(b, "expect_local"))) + ": someone moved it; check before applying");
            if (!Pieces308.Flag(b, "user_confirmed")) k.Refuse.Add("the user has not confirmed this by-name exception (SPEC_PATCH section 4): set bell.user_confirmed in " + Pieces308.OpsConfigFile + " after the user says yes");
            if (dry) return sb.Append(k.Refuse.Count == 0 ? "  apply would move the bell; nothing changed" : "  apply would be REFUSED (" + string.Join(" | ", k.Refuse) + "); nothing changed").ToString();
            if (k.Refuse.Count > 0) throw new PostLedger308.Refused(k.Refuse[0]);
            string utc = PostLedger308.Utc(); string backup = Pieces308.Backup(utc + "-bell-" + key, scenePath);
            var l = new BellLedger { scene = scenePath, key = key, state = "applied", utc = utc, configSha256 = dataSha, path = Pieces308.Str(b, "path"), sourceGuid = Pieces308.Str(b, "source_guid"), host = Pieces308.Str(Pieces308.Req(b, "probe"), "host"),
                sceneShaBefore = Pieces308.ShaAsset(scenePath), backup = backup, localBefore = k.Bell.localPosition, bx = Bits(k.Bell.localPosition.x), by = Bits(k.Bell.localPosition.y), bz = Bits(k.Bell.localPosition.z), beamY = k.BeamY, floorY = k.FloorY };
            try { k.Bell.localPosition = k.Local; PrefabUtility.RecordPrefabInstancePropertyModifications(k.Bell); Pieces308.SaveOrReload(k.Scene); }
            catch { Pieces308.Reload(k.Scene); throw; }
            l.localAfter = k.Bell.localPosition; l.worldAfter = k.Bell.position; l.sceneShaAfter = Pieces308.ShaAsset(scenePath); l.history = Add(l.history, "apply (backup " + backup + ")");
            Pieces308.WriteLedger(name, l);
            return sb.Append("  moved: local " + Pieces308.V(l.localBefore) + " -> " + Pieces308.V(l.localAfter) + " | scene sha " + Pieces308.Short(l.sceneShaBefore) + " -> " + Pieces308.Short(l.sceneShaAfter) + " | backup " + backup + " | ledger " + Pieces308.OutFile(name)).ToString();
        }
    }
}
