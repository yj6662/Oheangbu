using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 the first mine: two leftovers the arcane survey found (Tools/Unity/Stage308_minearcane/SURVEY.md 1 and 4). Own ledger, own revert.
    //   look:arcane:fix:apply    (1) the abandoned haul cart of the stope is no longer drawn (D308-60 "no carts": renderers and colliders off, the object stays)
    //                            (2) the slag heap that `ore` seated on TOP of a pack wall is moved down to the cave floor at the foot of that wall
    //                                (its vertices inside the two combined ore meshes are translated; nothing else in those meshes moves)
    //   look:arcane:fix:revert   put both back exactly (ledger: renderer / collider indices, vertex indices + the translation)
    //   look:arcane:fix:status | look:arcane:fix:check   read only
    // Data: Tools/Unity/Stage308_minearcane/mine_arcane_fix308.json. Ledger: Ledger/arcanefix308.json. Adds nothing; no collider, no light.
    // `Mine308 ore` applied again rebuilds its meshes and puts the heap back on the wall: revert this step before, apply it again after.
    public static partial class Mine308
    {
        static string FixFile => Path.Combine(ArcStage, "mine_arcane_fix308.json");
        static string FixLedgerFile => Path.Combine(ArcStage, "Ledger", "arcanefix308.json");
        [Serializable] sealed class FixCfg { public string cart = "", oreHome = ""; public float slagX, slagZ, slagRadius = 1.75f, slagAbove = .8f, footRing = .6f, searchMax = 4f, keepOutMargin = 1.5f; public float[] keepOut = Array.Empty<float>(); public string[] slagMaterials = Array.Empty<string>(); }
        [Serializable] sealed class FixMesh { public string renderer = "", asset = ""; public int vertexCount; public int[] idx = Array.Empty<int>(); }
        [Serializable] sealed class FixLedger
        {
            public bool applied; public string appliedUtc = ""; public int[] cartRenderers = Array.Empty<int>(), cartColliders = Array.Empty<int>(); public string[] cartNames = Array.Empty<string>();
            public List<FixMesh> slag = new List<FixMesh>(); public float[] slagFrom = Array.Empty<float>(), slagTo = Array.Empty<float>(), delta = Array.Empty<float>();
        }
        static FixCfg FixLoad() { if (!File.Exists(FixFile)) throw new Exception("no " + FixFile); var c = JsonUtility.FromJson<FixCfg>(File.ReadAllText(FixFile)); if (c.cart.Length == 0 || c.oreHome.Length == 0 || c.slagMaterials.Length == 0) throw new Exception("mine_arcane_fix308.json is incomplete"); return c; }
        static FixLedger FixRead() => File.Exists(FixLedgerFile) ? JsonUtility.FromJson<FixLedger>(File.ReadAllText(FixLedgerFile)) : new FixLedger();
        static void FixWrite(FixLedger l) { Directory.CreateDirectory(Path.GetDirectoryName(FixLedgerFile)); File.WriteAllText(FixLedgerFile, JsonUtility.ToJson(l, true)); }

        static string ArcFix(string verb)
        {
            switch (verb)
            {
                case "apply": return FixApply();
                case "revert": return FixRevert();
                case "status": case "check": return FixCheck();
                default: throw new Exception("look:arcane:fix:" + verb + "? (apply | revert | status | check)");
            }
        }

        // the highest thing under (x, z): true only when that is the cave floor and no drawn rubble stands over it
        static bool FixOpenFloor(ArcWorld w, Transform oreHome, float x, float z, out float y)
        {
            y = 0f; float top = w.plan.floorGuess + 2f; RaycastHit best = default; bool any = false;
            foreach (var h in Physics.RaycastAll(new Vector3(x, top, z), Vector3.down, 7f, ~0, QueryTriggerInteraction.Ignore)) { if (ArcOwn(w, h.transform)) continue; if (!any || h.point.y > best.point.y) { best = h; any = true; } }
            if (!any || !ArcIs(w, best.transform, w.plan.floorKeys)) return false; y = best.point.y;
            float rubble = ArcMeshRay(w, new Vector3(x, top, z), Vector3.down, 7f); return rubble == float.MaxValue || top - rubble < y + .25f;
        }
        // corridors and the boss room (x0, z0, x1, z1 boxes): the heap and its soot stay `keepOutMargin` clear of them
        static bool FixKeptOut(FixCfg cfg, float x, float z)
        {
            for (int i = 0; i + 3 < cfg.keepOut.Length; i += 4) if (x > cfg.keepOut[i] - cfg.keepOutMargin && x < cfg.keepOut[i + 2] + cfg.keepOutMargin && z > cfg.keepOut[i + 1] - cfg.keepOutMargin && z < cfg.keepOut[i + 3] + cfg.keepOutMargin) return true;
            return false;
        }
        static List<(MeshRenderer r, Mesh mesh, List<int> idx, Vector3[] world)> FixSlagVerts(Transform root, FixCfg cfg, Vector2 at, float minY)
        {
            var list = new List<(MeshRenderer, Mesh, List<int>, Vector3[])>(); var home = root.Find(cfg.oreHome); if (home == null) throw new Exception("no " + cfg.oreHome + " under `mine` (Mine308 ore:apply)");
            foreach (var r in home.GetComponentsInChildren<MeshRenderer>(true))
            {
                var m = r.sharedMaterial; var mf = r.GetComponent<MeshFilter>(); if (m == null || mf == null || mf.sharedMesh == null || !cfg.slagMaterials.Contains(m.name)) continue;
                var v = mf.sharedMesh.vertices; var wv = new Vector3[v.Length]; var idx = new List<int>();
                for (int i = 0; i < v.Length; i++) { wv[i] = r.transform.TransformPoint(v[i]); if (wv[i].y > minY && Vector2.Distance(new Vector2(wv[i].x, wv[i].z), at) < cfg.slagRadius) idx.Add(i); }
                if (idx.Count > 0) list.Add((r, mf.sharedMesh, idx, wv));
            }
            return list;
        }

        static string FixApply()
        {
            var scene = PlayScene(true); var root = MineRoot(scene).transform; var cfg = FixLoad(); var plan = ArcLoadPlan(); if (FixRead().applied) return "REFUSED already applied (look:arcane:fix:revert first)";
            var look = root.Find(LookRoot); var arcHome = look != null ? look.Find(ArcHomeName) : null; var w = ArcSee(root, arcHome, plan); var oreHome = root.Find(cfg.oreHome);
            var cart = root.Find(cfg.cart); if (cart == null) throw new Exception("no " + cfg.cart + " under `mine`");
            // ---- measure first, change nothing until both parts are known to work
            var from = new Vector2(cfg.slagX, cfg.slagZ); if (!ArcGround(w, cfg.slagX, cfg.slagZ, out float floorAtSpot)) floorAtSpot = plan.floorGuess - 1.7f;      // the floor mesh may stop under a pack wall
            var sel = FixSlagVerts(root, cfg, from, floorAtSpot + cfg.slagAbove); if (sel.Count == 0) throw new Exception("no slag / soot vertices above the floor at the spot: is the heap already on the floor?");
            float oldBase = sel.Min(s => s.idx.Min(i => s.world[i].y)); bool found = false; Vector2 to = from; float toY = 0f;
            for (float d = .4f; d <= cfg.searchMax && !found; d += .2f)
                for (int k = 0; k < 24 && !found; k++)
                {
                    float a = k * Mathf.PI / 12f; var c = from + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d; if (FixKeptOut(cfg, c.x, c.y)) continue; if (!FixOpenFloor(w, oreHome, c.x, c.y, out float y)) continue; bool ring = true;
                    for (int j = 0; j < 6 && ring; j++) { float b = j * Mathf.PI / 3f; ring = FixOpenFloor(w, oreHome, c.x + Mathf.Cos(b) * cfg.footRing, c.y + Mathf.Sin(b) * cfg.footRing, out _); }
                    if (!ring || Physics.CheckSphere(new Vector3(c.x, y + .6f, c.y), .45f, ~0, QueryTriggerInteraction.Ignore)) continue; found = true; to = c; toY = y;
                }
            if (!found) throw new Exception("no open floor within " + cfg.searchMax.ToString("F1", Inv) + " m of the slag spot");
            var delta = new Vector3(to.x - from.x, toY + .02f - oldBase, to.y - from.y);
            string dir = Path.Combine(ArcStage, "Backup", DateTime.Now.ToString("yyyyMMddTHHmmss", Inv) + "_arcanefix_apply"); Directory.CreateDirectory(dir); File.Copy(Path.GetFullPath(scene.path), Path.Combine(dir, Path.GetFileName(scene.path)), true);
            var led = new FixLedger { applied = true, appliedUtc = DateTime.UtcNow.ToString("s", Inv), slagFrom = new[] { from.x, oldBase, from.y }, slagTo = new[] { to.x, toY, to.y }, delta = new[] { delta.x, delta.y, delta.z } };
            foreach (var (r, mesh, idx, world) in sel)
            {
                string asset = AssetDatabase.GetAssetPath(mesh); if (string.IsNullOrEmpty(asset)) throw new Exception(r.name + ": its mesh is not an asset"); File.Copy(Path.GetFullPath(asset), Path.Combine(dir, Path.GetFileName(asset)), true);
                var v = mesh.vertices; foreach (int i in idx) v[i] = r.transform.InverseTransformPoint(world[i] + delta); mesh.vertices = v; mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh);
                led.slag.Add(new FixMesh { renderer = PathOf(r.transform, root), asset = asset, vertexCount = v.Length, idx = idx.ToArray() });
            }
            var rs = cart.GetComponentsInChildren<Renderer>(true); var cs = cart.GetComponentsInChildren<Collider>(true); var ri = new List<int>(); var ci = new List<int>();
            for (int i = 0; i < rs.Length; i++) if (rs[i].enabled) { rs[i].enabled = false; EditorUtility.SetDirty(rs[i]); ri.Add(i); }
            for (int i = 0; i < cs.Length; i++) if (cs[i].enabled) { cs[i].enabled = false; EditorUtility.SetDirty(cs[i]); ci.Add(i); }
            led.cartRenderers = ri.ToArray(); led.cartColliders = ci.ToArray(); led.cartNames = rs.Select(x => x.name).ToArray();
            FixWrite(led); EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) throw new Exception("the play scene was not saved");
            return "Mine308 look:arcane:fix:apply, backup " + dir + "\n  cart " + cfg.cart + ": renderers off " + ri.Count + "/" + rs.Length + ", colliders off " + ci.Count + "/" + cs.Length
                 + "\n  slag heap (" + from.x.ToString("F1", Inv) + ", " + oldBase.ToString("F2", Inv) + ", " + from.y.ToString("F1", Inv) + ") -> (" + to.x.ToString("F1", Inv) + ", " + toY.ToString("F2", Inv) + ", " + to.y.ToString("F1", Inv) + "), moved " + delta.ToString("F2") + ", meshes " + led.slag.Count + ", vertices " + led.slag.Sum(s => s.idx.Length);
        }

        static string FixRevert()
        {
            var scene = PlayScene(true); var root = MineRoot(scene).transform; var cfg = FixLoad(); var led = FixRead(); if (!led.applied) return "Mine308 look:arcane:fix:revert: nothing to put back";
            string dir = Path.Combine(ArcStage, "Backup", DateTime.Now.ToString("yyyyMMddTHHmmss", Inv) + "_arcanefix_revert"); Directory.CreateDirectory(dir); File.Copy(Path.GetFullPath(scene.path), Path.Combine(dir, Path.GetFileName(scene.path)), true);
            var delta = new Vector3(led.delta[0], led.delta[1], led.delta[2]); int moved = 0, skipped = 0;
            foreach (var s in led.slag)
            {
                var t = root.Find(s.renderer); var mf = t != null ? t.GetComponent<MeshFilter>() : null; var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null || mesh.vertexCount != s.vertexCount || AssetDatabase.GetAssetPath(mesh) != s.asset) { skipped++; continue; }      // rebuilt since (Mine308 ore): the heap is where `ore` put it
                File.Copy(Path.GetFullPath(s.asset), Path.Combine(dir, Path.GetFileName(s.asset)), true);
                var v = mesh.vertices; foreach (int i in s.idx) v[i] = t.InverseTransformPoint(t.TransformPoint(v[i]) - delta); mesh.vertices = v; mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh); moved++;
            }
            var cart = root.Find(cfg.cart); int on = 0;
            if (cart != null)
            {
                var rs = cart.GetComponentsInChildren<Renderer>(true); var cs = cart.GetComponentsInChildren<Collider>(true);
                foreach (int i in led.cartRenderers) if (i < rs.Length) { rs[i].enabled = true; EditorUtility.SetDirty(rs[i]); on++; }
                foreach (int i in led.cartColliders) if (i < cs.Length) { cs[i].enabled = true; EditorUtility.SetDirty(cs[i]); on++; }
            }
            FixWrite(new FixLedger()); EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) throw new Exception("the play scene was not saved");
            return "Mine308 look:arcane:fix:revert, backup " + dir + "\n  slag meshes moved back " + moved + (skipped > 0 ? ", skipped " + skipped + " (rebuilt since)" : "") + ", cart renderers / colliders on again " + on;
        }

        static string FixCheck()
        {
            var scene = PlayScene(false); var root = MineRoot(scene).transform; var cfg = FixLoad(); var plan = ArcLoadPlan(); var led = FixRead(); if (!led.applied) return "Mine308 look:arcane:fix:check FAIL: not applied";
            int fails = 0; var sb = new StringBuilder(); void Line(bool ok, string text) { if (!ok) fails++; sb.Append("\n  ").Append(ok ? "ok   " : "FAIL ").Append(text); }
            var cart = root.Find(cfg.cart); Line(cart != null && cart.gameObject.activeInHierarchy, "the cart object is still there (old checks look it up by name)");
            if (cart != null) { int drawn = cart.GetComponentsInChildren<Renderer>(true).Count(r => r.enabled), solid = cart.GetComponentsInChildren<Collider>(true).Count(c => c.enabled); Line(drawn == 0, "cart renderers still drawn " + drawn); Line(solid == 0, "cart colliders still on " + solid); }
            var look = root.Find(LookRoot); var w = ArcSee(root, look != null ? look.Find(ArcHomeName) : null, plan); var to = new Vector2(led.slagTo[0], led.slagTo[2]);
            bool floor = ArcGround(w, to.x, to.y, out float y); var now = FixSlagVerts(root, cfg, to, -1e9f); float lo = now.Count > 0 ? now.Min(s => s.idx.Min(i => s.world[i].y)) : 0f, hi = now.Count > 0 ? now.Max(s => s.idx.Max(i => s.world[i].y)) : 0f;
            Line(floor && now.Count == led.slag.Count && Mathf.Abs(lo - y) < .2f && hi - y < .9f, "slag heap at (" + to.x.ToString("F1", Inv) + ", " + to.y.ToString("F1", Inv) + "): floor " + y.ToString("F2", Inv) + ", heap " + lo.ToString("F2", Inv) + " - " + hi.ToString("F2", Inv));
            var old = FixSlagVerts(root, cfg, new Vector2(cfg.slagX, cfg.slagZ), y + cfg.slagAbove + .3f); Line(old.Count == 0, "slag / soot left on the wall top " + old.Sum(s => s.idx.Count) + " vertices");
            Line(FixOpenFloor(w, root.Find(cfg.oreHome), to.x, to.y, out _), "nothing stands over the heap's new place (pack wall, rubble, prop)");
            Line(!FixKeptOut(cfg, to.x, to.y), "the heap keeps " + cfg.keepOutMargin.ToString("F1", Inv) + " m from the corridors and the boss room");
            return "Mine308 look:arcane:fix:check " + (fails == 0 ? "PASS" : "FAIL (" + fails + ")") + sb;
        }
    }
}
