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
    // (c) Face_0 collider (D308-9e, SPEC-WORLD-BUILDING-AUDIT-308: the visible face of the 적로 잔도 rock wall stands up to 0.88 m
    // outside its collider). The op re-points ONE MeshCollider (Finish297_CliffPath/CliffPath_jeokro_crag_Face_0/Collision) to the
    // mesh small_1b/face0_308.json chose by measurement (an existing mesh asset: no new asset is made). The ledger keeps the original
    // GUID + fileID; revert puts it back. The trail / lip / footing colliders are recorded at apply and must be the same at verify.
    // The rim and enterable-gap rows are the existing read-only probe: BuildingFix308 Meshes "probe:<alias>:0459".
    public static partial class SmallOps308
    {
        [Serializable] sealed class MeshRef { public string path = "", guid = "", asset = ""; public long fileId; public int triangles; }
        [Serializable] sealed class Face0Ledger
        {
            public string format = "cb308.small.face0.1", scene = "", key = "", state = "none", utc = "", configSha256 = "", choiceSha256 = "", sceneShaBefore = "", sceneShaAfter = "", backup = "";
            public MeshRef original = new MeshRef(), applied = new MeshRef(); public List<MeshRef> untouched = new List<MeshRef>(); public string[] history = new string[0];
        }

        static MeshRef Ref(string path, Mesh m)
        {
            CliffCore308.MeshIdentity(m, out string guid, out long file, out string asset);
            return new MeshRef { path = path, guid = guid, fileId = file, asset = asset, triangles = m != null && m.isReadable ? m.triangles.Length / 3 : 0 };
        }

        /// <summary>How far the visible LOD0 stands from the collider mesh (every LOD0 vertex against every collider triangle, world space).</summary>
        static float Face0Gap(Mesh visible, Matrix4x4 vm, Mesh collider, Matrix4x4 cm, out int over, float target)
        {
            over = 0; var cache = new BuildingAudit308.MeshCache308(); var v = cache.Get(visible); var c = cache.Get(collider);
            if (v == null || c == null) return float.NaN;
            if (visible == collider && vm == cm) return 0f;
            var cw = c.v.Select(p => cm.MultiplyPoint3x4(p)).ToArray(); float max = 0f;
            foreach (var lp in v.v)
            {
                var p = vm.MultiplyPoint3x4(lp); float best = float.MaxValue;
                for (int k = 0; k + 2 < c.t.Length; k += 3) { float s = (BuildingAudit308.ClosestOnTri(p, cw[c.t[k]], cw[c.t[k + 1]], cw[c.t[k + 2]]) - p).sqrMagnitude; if (s < best) best = s; }
                float d = Mathf.Sqrt(best); if (d > max) max = d; if (d > target) over++;
            }
            return max;
        }

        static string Face0(CliffCore308.Config cfg, JObject data, string dataSha, string scenePath, string key, string verb)
        {
            string name = LedgerName("face0", key); var f = Pieces308.Req(data, "face0");
            var choiceFile = Pieces308.Json(Pieces308.DataDir + "/face0_308.json", out string choiceSha); var choice = Pieces308.Req(choiceFile, "choice");
            float target = Pieces308.Num(f, "target_gap_m");
            bool write = verb == "apply" || verb == "revert";
            var scene = Pieces308.OpenTarget(scenePath, data["preview_roots"], write);
            string ownerPath = Pieces308.Str(f, "owner"), colPath = ownerPath + "/" + Pieces308.Str(f, "collider_child");
            var owner = Pieces308.FindOne(scene, ownerPath, "Face_0"); var col = Pieces308.FindOne(scene, colPath, "Face_0 collider").GetComponent<MeshCollider>();
            if (col == null) throw new PostLedger308.Refused(colPath + " has no MeshCollider");
            if (PostLedger308.UnderProtectedTree(col.transform)) throw new PostLedger308.Refused(colPath + " is under a protected tree");
            var lod0 = owner.Find("LOD0")?.GetComponent<MeshFilter>(); if (lod0 == null || lod0.sharedMesh == null) throw new PostLedger308.Refused(ownerPath + "/LOD0 has no mesh");
            var want = CliffCore308.LoadMesh(Pieces308.Str(choice, "guid"), Pieces308.Req(choice, "file_id").Value<long>());
            if (want == null) throw new PostLedger308.Refused("the chosen mesh " + Pieces308.Str(choice, "guid") + " does not load");
            if (PostLedger308.IsProtectedPath(AssetDatabase.GetAssetPath(want))) throw new PostLedger308.Refused("the chosen mesh sits under a protected path");
            var now = Ref(colPath, col.sharedMesh); var ledger = Pieces308.ReadLedger<Face0Ledger>(name);
            var sb = new StringBuilder("face0 " + verb + " " + scenePath + "\n");
            float gapNow = Face0Gap(lod0.sharedMesh, lod0.transform.localToWorldMatrix, col.sharedMesh, col.transform.localToWorldMatrix, out int overNow, target);
            float gapWant = Face0Gap(lod0.sharedMesh, lod0.transform.localToWorldMatrix, want, col.transform.localToWorldMatrix, out int overWant, target);
            sb.AppendLine("  collider now " + now.asset + " [" + now.guid + ":" + now.fileId + "], " + now.triangles + " triangles: the visible LOD0 stands up to " + Pieces308.F(gapNow, "F3") + " m from it (" + overNow + " vertices over " + Pieces308.F(target) + ")");
            sb.AppendLine("  chosen       " + AssetDatabase.GetAssetPath(want) + " [" + Pieces308.Str(choice, "guid") + ":" + Pieces308.Req(choice, "file_id").Value<long>() + "], " + (want.isReadable ? want.triangles.Length / 3 : 0) + " triangles: " + Pieces308.F(gapWant, "F3") + " m (" + overWant + " over; offline choice " + Pieces308.Str(choice, "candidate") + ", target " + Pieces308.F(target) + ")");
            var others = Pieces308.Arr(f, "untouched").Select(t => t.Value<string>()).Select(p => (p, c: Pieces308.FindAll(scene, p).Select(t => t.GetComponent<MeshCollider>()).FirstOrDefault(c => c != null))).ToList();

            if (verb == "revert")
            {
                if (ledger == null || ledger.state != "applied") throw new PostLedger308.Refused("no applied ledger " + name);
                var back = CliffCore308.LoadMesh(ledger.original.guid, ledger.original.fileId); if (back == null) throw new PostLedger308.Refused("the recorded original " + ledger.original.asset + " does not load");
                string b0 = Pieces308.Backup(PostLedger308.Utc() + "-face0-revert-" + key, scenePath);
                try { col.sharedMesh = back; EditorUtility.SetDirty(col); Pieces308.SaveOrReload(scene); }
                catch { Pieces308.Reload(scene); throw; }
                string sha = Pieces308.ShaAsset(scenePath); string archived = Pieces308.Archive(name);
                return sb.Append("  reverted to " + ledger.original.asset + " | scene sha " + Pieces308.Short(sha) + (sha == ledger.sceneShaBefore ? " (= the bytes before apply)" : " (before apply " + Pieces308.Short(ledger.sceneShaBefore) + ")") + " | backup " + b0 + " | ledger archived " + archived).ToString();
            }
            if (verb == "verify")
            {
                int fail = 0; void Row(bool ok, string text) { if (!ok) fail++; sb.AppendLine("  " + (ok ? "PASS " : "FAIL ") + text); }
                Row(col.sharedMesh == want, "the collider is the chosen mesh");
                Row(!float.IsNaN(gapNow) && gapNow <= target, "visible LOD0 to collider " + Pieces308.F(gapNow, "F3") + " m (target " + Pieces308.F(target) + ")");
                Row(!col.convex && col.enabled, "MeshCollider enabled, not convex");
                foreach (var (p, c) in others)
                {
                    var rec = ledger != null ? ledger.untouched.FirstOrDefault(u => u.path == p) : null; var cur = c != null ? Ref(p, c.sharedMesh) : null;
                    Row(cur != null && (rec == null || rec.guid == cur.guid && rec.fileId == cur.fileId), "untouched " + p + ": " + (cur == null ? "missing" : cur.asset + (rec == null ? " (no ledger row to compare)" : "")));
                }
                Row(!scene.isDirty, "the scene is clean after the read-only command");
                sb.AppendLine("  INFO rim buried / enterable gap / protrusion on the scene ground: BuildingFix308 Meshes \"probe:<alias>:0459\" (read only; expect rim enterable 0, protrudeMax <= " + Pieces308.F(target) + ")");
                sb.Insert(0, (fail == 0 ? "GREEN " : "RED "));
                return Pieces308.Report("cb308-small-face0-verify-" + key + ".txt", sb);
            }
            if (col.sharedMesh == want) return sb.Append("  변경 없음 (the collider already is the chosen mesh; nothing written, scene not saved)").ToString();
            var refuse = new List<string>();
            if (float.IsNaN(gapWant) || gapWant > target) refuse.Add("the chosen mesh does not meet the target in this scene (" + Pieces308.F(gapWant, "F3") + " m)");
            if (ledger != null && ledger.state == "applied") refuse.Add("a ledger says the collider was already re-pointed, but it is " + now.asset + " now: run face0:revert:" + key + " first");
            if (verb == "plan") return sb.Append(refuse.Count == 0 ? "  apply would re-point the MeshCollider (+" + ((want.isReadable ? want.triangles.Length / 3 : 0) - now.triangles) + " collider triangles); nothing changed" : "  apply would be REFUSED (" + string.Join(" | ", refuse) + "); nothing changed").ToString();
            if (refuse.Count > 0) throw new PostLedger308.Refused(refuse[0]);
            string utc = PostLedger308.Utc(); string backup = Pieces308.Backup(utc + "-face0-" + key, scenePath);
            var l = new Face0Ledger { scene = scenePath, key = key, state = "applied", utc = utc, configSha256 = dataSha, choiceSha256 = choiceSha, sceneShaBefore = Pieces308.ShaAsset(scenePath), backup = backup, original = now,
                untouched = others.Where(o => o.c != null).Select(o => Ref(o.p, o.c.sharedMesh)).ToList() };
            try { col.sharedMesh = want; EditorUtility.SetDirty(col); Physics.SyncTransforms(); Pieces308.SaveOrReload(scene); }
            catch { Pieces308.Reload(scene); throw; }
            l.applied = Ref(colPath, col.sharedMesh); l.sceneShaAfter = Pieces308.ShaAsset(scenePath); l.history = Add(l.history, "apply (backup " + backup + ")");
            Pieces308.WriteLedger(name, l);
            return sb.Append("  re-pointed: " + l.original.asset + " -> " + l.applied.asset + " | scene sha " + Pieces308.Short(l.sceneShaBefore) + " -> " + Pieces308.Short(l.sceneShaAfter) + " | backup " + backup + " | ledger " + Pieces308.OutFile(name)).ToString();
        }
    }
}
