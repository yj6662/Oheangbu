using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 ledger recovery (Adopt308.cs): ContentSeat308 Fix2 "adopt:<alias>".
    // Fix2 poses objects that were in the scene before it: its ledger is the place where "this object stands on the pose Fix2 gave it"
    // is written down. Without it plan / apply judge every op from the scene alone - and an op with seat_once (the wreck's body: its
    // seat was taken once, with the roof and the wheels still attached) cannot be re-read that way: it is neither on its expect_before
    // nor provably on the target, so the pass is BLOCKED. ContentSeat308 verify (D8) leaves a piece to Fix2 only while Fix2 holds a
    // live pose row for it. adopt writes ONE op (status applied, command "adopt:<alias>") with a row per enabled op of the data whose
    // object stands on its data target:
    //   pose / scale rows   after = the local pose / scale as it stands; before = "original unknown"
    //   material rows       after = the materials as they stand (every slot = the data's "to"); before = "original unknown"
    // only when (1) the tool's own verify of that scene is GREEN on the present data (no ledger row is needed for verify: it judges the
    // scene as it stands), (2) every such op passes the target test plan / apply use (rotation, scale, local_xz / world xz), and (3) the
    // ledger has no live op yet. Whether Fix2 posed an object or it already stood there, and where it stood before, cannot be known:
    // revert therefore REFUSES an adopted op (ContentSeat308.Fix2.cs) - it never invents a pose to go back to. Clones are not
    // adopted (none is switched on in the data; an enabled clone that stands refuses the command).
    public static partial class ContentSeat308
    {
        static string Fix2Adopt(string alias)
        {
            var d = Fix2Data(); var tol = Req(d, "tolerances");
            // 1. the tool's own verify first: the scene must be what the present data poses
            string verify = Fix2Verify(alias);
            if (verify.IndexOf(": GREEN (", StringComparison.Ordinal) < 0) return "refused: " + alias + " - Fix2 verify is not GREEN: the scene is not what the present data poses, no ledger row is written for it\n" + verify;
            var k = Begin(alias, false, out string previous, out bool opened);
            try
            {
                string file = Fix2LedgerFile(d, k.ScenePath); var ledger = Fix2Load(d, k.ScenePath);
                var live = ledger.ops.Where(o => !o.reverted && (o.status == "applied" || o.status == "applying")).ToList();
                string head = "ContentSeat308 Fix2 adopt " + k.Alias + " (data " + Short(ShaOf(Fix2File)) + " " + Opt(d, "version") + ")\n";
                if (live.Count > 0)
                {
                    GoBack(previous, k.Scene, opened);
                    var mine = live.LastOrDefault(o => Adopt308.Is(o.detail));
                    if (mine != null) return head + "  " + Adopt308.Already(file, mine.detail, mine.changes.Count);
                    return "refused: " + k.Alias + " - " + Adopt308.Taken(file, live.Sum(o => o.changes.Count(c => c.state == "apply")));
                }
                var changes = new List<Change>(); var bad = new List<string>(); int groups = 0;
                foreach (var g in Arr(d, "groups"))
                {
                    string gid = Str(g, "id"); if (!Flag(g, "enabled")) continue; groups++;
                    foreach (var c in Arr(d, "clones").Where(x => Opt(x, "group") == gid))
                    {
                        var croot = k.Scene.GetRootGameObjects().FirstOrDefault(x => x.name == Str(d, "clone_root"));
                        if (croot != null && croot.transform.Find(Str(c, "id")) != null) bad.Add(Str(c, "id") + ": a clone of this data stands under " + Str(d, "clone_root") + " - clones are not adopted by this command");
                    }
                    foreach (var o in Arr(d, "ops").Where(x => Opt(x, "group") == gid))
                    {
                        string id = Str(o, "id"), key = Str(o, "key"); var t = Strict(k, key, false);
                        if (t == null) { bad.Add(id + ": " + key + " is not in this scene"); continue; }
                        if (ProtectedObject(k, t)) { bad.Add(id + ": " + key + " stands under a protected tree"); continue; }
                        if (!Arr(d, "key_roots").Any(r => key.StartsWith(r.Value<string>(), StringComparison.Ordinal))) { bad.Add(id + ": " + key + " is outside key_roots[] of the data"); continue; }
                        if (o["material"] != null)
                        {
                            var to = AssetDatabase.LoadAssetAtPath<Material>(Str(o["material"], "to")); var r = t.GetComponent<Renderer>();
                            if (r == null || to == null || !r.sharedMaterials.All(x => x == to)) { bad.Add(id + ": the slots do not all carry " + Str(o["material"], "to") + " (now " + (r != null ? F2Mats(r.sharedMaterials) : "-") + ")"); continue; }
                            changes.Add(new Change { step = Fix2Step, key = key, kind = "material", before = Adopt308.Unknown, after = F2Mats(r.sharedMaterials), at = t.position, state = "apply", note = id + "|adopted" });
                            continue;
                        }
                        // the target test of Fix2Pass (rotation, scale, local_xz / world xz); the seat and the checks are verify's lines, read above
                        var p0 = t.localPosition; var q0 = t.localRotation; var s0 = t.localScale;
                        var qT = o["euler"] != null ? Quaternion.Euler(V3At(o["euler"])) : q0; var sT = o["scale"] != null ? V3At(o["scale"]) : s0;
                        bool worldOp = o["world"] != null && o["world"].Type != JTokenType.Null;
                        bool rotOk = Quaternion.Angle(q0, qT) <= Num(tol, "angle_deg") && (s0 - sT).magnitude <= Num(tol, "scale");
                        bool xzOk = o["local_xz"] == null || new Vector2(p0.x - At(o["local_xz"], 0), p0.z - At(o["local_xz"], 1)).magnitude <= Num(tol, "pose_m");
                        if (worldOp && o["world"]["euler"] != null) rotOk = Quaternion.Angle(t.rotation, Quaternion.Euler(V3At(o["world"]["euler"]))) <= Num(tol, "angle_deg") && (s0 - sT).magnitude <= Num(tol, "scale");
                        if (worldOp && o["world"]["xz"] != null) xzOk = xzOk && new Vector2(t.position.x - At(o["world"]["xz"], 0), t.position.z - At(o["world"]["xz"], 1)).magnitude <= Num(tol, "pose_m");
                        if (!rotOk || !xzOk) { bad.Add(id + ": " + key + " stands at " + PoseText(p0, q0) + " scale " + V(s0) + ", not on the data target (rotation / scale " + (rotOk ? "ok" : "OFF") + ", xz " + (xzOk ? "ok" : "OFF") + ")"); continue; }
                        changes.Add(new Change { step = Fix2Step, key = key, kind = "pose", before = Adopt308.Unknown, after = PoseText(p0, q0), at = t.position, state = "apply", note = id + "|scale " + Adopt308.Unknown + " -> " + Vec(s0) + "|adopted" });
                        if (o["scale"] != null) changes.Add(new Change { step = Fix2Step, key = key, kind = "scale", before = Adopt308.Unknown, after = Vec(s0), at = t.position, state = "apply", note = id });
                    }
                }
                if (bad.Count > 0) { GoBack(previous, k.Scene, opened); return "refused: " + k.Alias + " - " + Adopt308.NotEqual(k.ScenePath, bad, 6); }
                if (changes.Count == 0) { GoBack(previous, k.Scene, opened); return head + "  nothing to adopt: no enabled op of the data in " + k.ScenePath + ". Nothing written"; }
                if (k.Scene.isDirty) { GoBack(previous, k.Scene, opened); return "refused: " + k.Alias + " - the scene is dirty after a read-only pass (bug): it was reloaded. Nothing written"; }
                string sha = ShaOf(Harness303.Abs(k.ScenePath)), stamp = Adopt308.Stamp(Adopt308.Utc(), sha), attraction = BuildingFix308.AttractionHash(k.Scene, k.Cfg.attractionRoot);
                var op = new Op
                {
                    utc = BuildingAudit308.Utc(), command = "adopt:" + k.Alias, scene = k.ScenePath, alias = k.Alias, status = "applied", backup = Adopt308.NoBackup, shaBefore = "", shaAfter = sha,
                    attractionBefore = attraction, attractionAfter = attraction, dataSha = ShaOf(Fix2File), sceneDataSha = k.SceneDataSha, steps = new[] { Fix2Step }, changes = changes,
                    detail = stamp + ": " + changes.Count + " row(s), every before value " + Adopt308.Unknown
                };
                ledger.ops.Add(op); Fix2Save(d, ledger);
                GoBack(previous, k.Scene, opened);
                return head + "  verify: " + verify.Split('\n')[0] + "\n"
                    + "  wrote one op of " + changes.Count + " row(s) (" + string.Join(", ", changes.GroupBy(c => c.kind).Select(x => x.Key + " " + x.Count())) + ") for " + groups + " enabled group(s); " + stamp + "\n"
                    + "  " + Adopt308.Unknown + ": every before value (pose, scale, material) and the scene bytes before Fix2 - revert refuses this op; plan / apply / verify read its after values\n"
                    + "  ledger " + file + " (the scene was not changed)";
            }
            catch { GoBack(previous, k.Scene, opened); throw; }
        }
    }
}
