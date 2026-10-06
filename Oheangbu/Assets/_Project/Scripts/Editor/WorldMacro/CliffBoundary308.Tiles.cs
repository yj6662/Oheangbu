using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
    // tiles:<scene> / revert:<scene> (D308-9c Q1). The only scene change is the sharedMesh of the MeshFilters / MeshCollider of the tiles
    // the surface ledger names; the only asset change is the FinalSurface reference of that scene's own layout asset. Both are recorded
    // (GUID + fileID + name + vertex count + sha256 of the asset file) the first time a tile is touched and restored exactly by revert.
    // The LODGroup of a tile is left alone (its serialized bounds are not a reference; see the README for the consequence).
    public static partial class CliffBoundary308
    {
        sealed class Change { public CliffCore308.TileRef Tile; public CliffCore308.LodRef Lod; public Mesh To; public string Why; }

        static bool Same(Mesh mesh, CliffCore308.MeshRecord rec)
        {
            if (mesh == null || rec == null || string.IsNullOrEmpty(rec.guid)) return false;
            CliffCore308.MeshIdentity(mesh, out string guid, out long id, out _);
            return guid == rec.guid && id == rec.fileId;
        }

        static string Tiles(CliffCore308.Config cfg, string token, Dictionary<string, string> opt, bool dry)
        {
            string path = CliffCore308.ScenePath(cfg, token);
            PostLedger308.RequireEditable();
            var ledger = ReadLedger(cfg, path);
            string stageId = StageOption(cfg, opt, ledger);
            var surface = ReadSurfaceLedger(cfg, stageId, out string surfaceSha);
            if (surface == null) throw new PostLedger308.Refused("no surface ledger for stage " + stageId + " (run surface:" + stageId + " first)");
            if (!surface.written) throw new PostLedger308.Refused("the surface ledger of stage " + stageId + " is a dry record");

            // the stage data on disk must be what the surface command wrote: base sha, ops sha, stage, field sha (plan/Stage/FORMAT.md)
            string baseSha = CliffCore308.ShaFile(PostLedger308.Abs(cfg.baseHeight));
            if (baseSha != surface.baseSha256) throw new PostLedger308.Refused("base height sha " + baseSha + " != the surface ledger's " + surface.baseSha256);
            string fieldNow = CliffCore308.ShaFile(CliffCore308.StageHeightPath(cfg, stageId));
            if (fieldNow != surface.heightSha256) throw new PostLedger308.Refused("the stage field on disk (sha " + Short(fieldNow) + ") is not the one surface:" + stageId + " was built from (" + Short(surface.heightSha256) + "); re-run surface:" + stageId);
            var stageNow = CliffCore308.LoadStage(cfg, stageId, true);
            if ((stageNow.OpsSha ?? "") != surface.opsSha256) throw new PostLedger308.Refused("the stage report's ops sha (" + Short(stageNow.OpsSha) + ") is not the one the surface ledger records (" + Short(surface.opsSha256) + "); re-run surface:" + stageId);
            if (ledger.state == "applied" && ledger.stage == stageId && (ledger.baseSha256 != surface.baseSha256 || ledger.opsSha256 != surface.opsSha256 || ledger.heightSha256 != surface.heightSha256))
                throw new PostLedger308.Refused("this scene was applied from base " + Short(ledger.baseSha256) + " / ops " + Short(ledger.opsSha256) + " / field " + Short(ledger.heightSha256) + ", the surface ledger now records " + Short(surface.baseSha256) + " / "
                    + Short(surface.opsSha256) + " / " + Short(surface.heightSha256) + "; revert:" + ledger.key + " first");
            if (ledger.state == "applied" && ledger.baseSha256 != "" && ledger.baseSha256 != surface.baseSha256) throw new PostLedger308.Refused("the scene ledger was applied from base " + ledger.baseSha256 + ", the surface ledger from " + surface.baseSha256);
            if (CliffCore308.ShaFile(PostLedger308.Abs(surface.heightAsset)) != surface.heightSha256) throw new PostLedger308.Refused("the height asset " + surface.heightAsset + " does not match the surface ledger sha " + surface.heightSha256);
            if (PostLedger308.IsProtectedPath(surface.heightAsset) || !surface.heightAsset.StartsWith(cfg.assetRoot + "/", StringComparison.Ordinal)) throw new PostLedger308.Refused("unexpected height asset path " + surface.heightAsset);
            var heightAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(surface.heightAsset);
            if (heightAsset == null) throw new PostLedger308.Refused("the height asset is not imported: " + surface.heightAsset);
            var stageMeshes = new Dictionary<string, Mesh>();
            foreach (var t in surface.tile)
                foreach (var l in t.lods)
                {
                    if (!l.mesh.path.StartsWith(cfg.assetRoot + "/", StringComparison.Ordinal) || PostLedger308.IsProtectedPath(l.mesh.path)) throw new PostLedger308.Refused("the surface ledger names a mesh outside " + cfg.assetRoot + ": " + l.mesh.path);
                    string fileSha = CliffCore308.ShaFile(PostLedger308.Abs(l.mesh.path));
                    if (fileSha == "" || fileSha != l.mesh.fileSha256) throw new PostLedger308.Refused("stage mesh " + l.mesh.path + " is missing or changed since surface (file sha " + Short(fileSha) + " != ledger " + Short(l.mesh.fileSha256) + "); re-run surface:" + stageId);
                    var m = CliffCore308.LoadMesh(l.mesh.guid, l.mesh.fileId);
                    if (m == null) throw new PostLedger308.Refused("stage mesh " + l.mesh.path + " cannot be loaded by its recorded GUID");
                    if (m.vertexCount != l.source.vertices) throw new PostLedger308.Refused("stage mesh " + l.mesh.path + " has " + m.vertexCount + " vertices, its source had " + l.source.vertices);
                    stageMeshes[t.tile + "/" + l.lod] = m;
                }

            RequireNoPreview();
            var scene = PostLedger308.Open(path);
            var root = CliffCore308.TerrainRoot(scene, cfg);
            var session = CliffCore308.Session(scene);
            var layout = session != null ? session.MountainLayout : null;
            if (layout == null) throw new PostLedger308.Refused("no session layout in " + path);
            string layoutPath = AssetDatabase.GetAssetPath(layout);
            if (PostLedger308.IsProtectedPath(layoutPath)) throw new PostLedger308.Refused("the scene layout is a protected asset: " + layoutPath);
            foreach (var other in cfg.scenes.Where(s => s.path != path))
            {
                var ol = ReadLedger(cfg, other.path);
                if (ol.layout == layoutPath && ol.layout != "") throw new PostLedger308.Refused("layout " + layoutPath + " is also the layout of " + other.key + " (shared layouts are not supported by tiles)");
            }

            // classify every reference before the first change: stage mesh (done) / recorded original / earlier stage / first touch
            var known = ledger.tiles.ToDictionary(t => t.tile, t => t);
            var changes = new List<Change>(); var refusals = new List<string>(); var tiles = new List<CliffCore308.TileRef>(); int already = 0, firstTouch = 0;
            foreach (var st in surface.tile)
            {
                if (!CliffCore308.ParseTileKey(st.key, out int col, out int row)) throw new PostLedger308.Refused("bad tile key in the surface ledger: " + st.key);
                var t = CliffCore308.FindTile(root, cfg, col, row, true); tiles.Add(t);
                if (t.Name != st.tile) throw new PostLedger308.Refused("tile " + st.key + " is " + t.Name + " in the scene but " + st.tile + " in the surface ledger");
                if (!PostLedger308.UnderProtectedTree(t.Tile)) refusals.Add(t.Name + " is not under the terrain root the Q1 exception names");
                known.TryGetValue(t.Name, out var kt);
                foreach (var sl in st.lods)
                {
                    var l = t.Lod(sl.lod); var target = stageMeshes[t.Name + "/" + sl.lod];
                    if (l == null) { refusals.Add(t.Name + " has no " + sl.lod); continue; }
                    var kl = kt != null ? kt.lods.FirstOrDefault(x => x.lod == sl.lod) : null;
                    if (l.Mesh == target) { already++; continue; }
                    if (kl != null && Same(l.Mesh, kl.original)) changes.Add(new Change { Tile = t, Lod = l, To = target, Why = "recorded original" });
                    else if (kl != null && Same(l.Mesh, kl.applied)) changes.Add(new Change { Tile = t, Lod = l, To = target, Why = "earlier stage mesh" });
                    else if (kl == null && Same(l.Mesh, sl.source)) { changes.Add(new Change { Tile = t, Lod = l, To = target, Why = "first touch" }); firstTouch++; }
                    else refusals.Add(t.Name + "/" + sl.lod + " references " + (l.Mesh != null ? l.Path + " [" + l.Guid + ":" + l.FileId + "]" : "nothing") + ": neither the recorded original"
                        + (kl != null ? " (" + kl.original.path + ")" : " (surface source " + sl.source.path + ")") + " nor the stage mesh " + sl.mesh.path);
                }
                var lod0 = t.Lod(cfg.colliderLod);
                if (lod0 != null && t.Collider.sharedMesh != lod0.Mesh) refusals.Add(t.Name + " collider references " + PostLedger308.AssetRef(t.Collider.sharedMesh) + ", not its " + cfg.colliderLod + " mesh");
            }
            // tiles of an earlier stage that this stage no longer names go back to their originals
            var named = new HashSet<string>(surface.tile.Select(t => t.tile)); var restore = new List<TileLedger>();
            foreach (var kt in ledger.tiles.Where(t => t.applied && !named.Contains(t.tile))) restore.Add(kt);

            var surfaceNow = layout.FinalSurface; string surfaceNowPath = surfaceNow != null ? AssetDatabase.GetAssetPath(surfaceNow) : "", surfaceNowGuid = "";
            if (surfaceNow != null && !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(surfaceNow, out surfaceNowGuid, out long _)) surfaceNowGuid = "";
            bool layoutDone = surfaceNow == heightAsset;
            if (!layoutDone && ledger.layoutOriginalSurfaceGuid != "" && surfaceNowGuid != ledger.layoutOriginalSurfaceGuid && surfaceNowGuid != ledger.layoutAppliedSurfaceGuid)
                refusals.Add("layout " + layoutPath + " FinalSurface is " + surfaceNowPath + ": neither the recorded original (" + ledger.layoutOriginalSurface + ") nor a recorded stage field");
            if (!layoutDone && ledger.layoutOriginalSurfaceGuid == "" && CliffCore308.Sha(surfaceNow != null ? surfaceNow.bytes : new byte[0]) != surface.baseSha256)
                refusals.Add("layout " + layoutPath + " FinalSurface (" + surfaceNowPath + ") is not the base field the stage was made from");
            if (refusals.Count > 0) throw new PostLedger308.Refused(refusals.Count + " references match neither the recorded originals nor the stage meshes; nothing changed: " + string.Join(" | ", refusals.Take(12)) + (refusals.Count > 12 ? " | ..." : ""));

            string plan = "stage " + stageId + " in " + path + ": tiles " + tiles.Count + " [" + string.Join(" ", surface.tileKeys) + "], mesh references to re-point " + changes.Count + " (first touch " + firstTouch + "), already on the stage mesh " + already
                + ", tiles of an earlier stage to restore " + restore.Count + ", layout FinalSurface " + (layoutDone ? "already " : surfaceNowPath + " -> ") + surface.heightAsset;
            if (changes.Count == 0 && restore.Count == 0 && layoutDone && ledger.state == "applied" && ledger.stage == stageId) return "tiles " + plan + " | no-op (already applied, ledger " + LedgerFile(cfg, ledger.key) + ")";
            if (dry) return "tiles-dry " + plan + " | nothing written";

            // ---- write: backup, ledger (applying), references, layout, save, verify, ledger (applied)
            string utc = PostLedger308.Utc(), backupDir = CliffCore308.OutFile(cfg, "Backup");
            ledger.sceneShaBefore = CliffCore308.ShaFile(PostLedger308.Abs(path));
            // "pre-#308" = the scene file while the ledger knows it carries no stage reference (never touched, or reverted). An apply that was
            // interrupted ('applying') keeps the sha recorded before it: the file may already hold stage meshes
            if (ledger.sceneShaPristine == "" || ledger.state == "none" || ledger.state == "reverted") ledger.sceneShaPristine = ledger.sceneShaBefore;
            ledger.backup = PostLedger308.Backup(backupDir, utc + "-tiles-" + ledger.key, path).Replace('\\', '/');
            PostLedger308.Backup(backupDir, utc + "-tiles-" + ledger.key, layoutPath);
            var list = ledger.tiles.ToList();
            foreach (var t in tiles)
            {
                var st = surface.tile.First(x => x.tile == t.Name);
                var kt = list.FirstOrDefault(x => x.tile == t.Name);
                if (kt == null)
                {
                    // the original is what the filter references now; a filter that already sits on the stage mesh without a ledger entry
                    // (ledger file lost) takes the source the surface ledger recorded for it
                    CliffCore308.MeshRecord Original(CliffCore308.LodRef l)
                    {
                        var sl = st.lods.FirstOrDefault(x => x.lod == l.Lod);
                        return sl != null && stageMeshes.TryGetValue(t.Name + "/" + l.Lod, out var target) && l.Mesh == target ? sl.source : CliffCore308.Record(l.Mesh, true);
                    }
                    var collide = t.Lod(cfg.colliderLod);
                    kt = new TileLedger { tile = t.Name, key = st.key, firstTouchedUtc = utc, firstTouchedStage = stageId, colliderOriginal = collide != null ? Original(collide) : CliffCore308.Record(t.Collider.sharedMesh, true),
                        colliderApplied = new CliffCore308.MeshRecord(), lods = t.Lods.Select(l => new LodLedger { lod = l.Lod, filter = PostLedger308.PathOf(l.Filter.transform), original = Original(l), applied = new CliffCore308.MeshRecord() }).ToArray() };
                    list.Add(kt);
                }
            }
            ledger.tiles = list.ToArray(); ledger.layout = layoutPath; ledger.layoutGuid = AssetDatabase.AssetPathToGUID(layoutPath);
            if (ledger.layoutOriginalSurfaceGuid == "")
            {
                // the original is what the layout references now; a layout that already sits on this stage field without a ledger entry
                // (ledger file lost) takes the base field the stage was made from, never the stage field itself
                var original = layoutDone ? AssetDatabase.LoadAssetAtPath<TextAsset>(cfg.baseHeight) : surfaceNow;
                if (original == null) throw new PostLedger308.Refused("the layout's pre-#308 FinalSurface cannot be determined (" + cfg.baseHeight + " is not imported); nothing changed");
                ledger.layoutOriginalSurface = AssetDatabase.GetAssetPath(original); ledger.layoutOriginalSurfaceGuid = AssetDatabase.AssetPathToGUID(ledger.layoutOriginalSurface); ledger.layoutOriginalSurfaceSha256 = CliffCore308.Sha(original.bytes);
            }
            ledger.state = "applying"; ledger.stage = stageId; ledger.baseSha256 = surface.baseSha256; ledger.opsSha256 = surface.opsSha256; ledger.heightSha256 = surface.heightSha256; ledger.surfaceLedgerSha256 = surfaceSha; ledger.configSha256 = cfg.Sha256;
            WriteLedger(cfg, ledger, "tiles " + stageId + ": applying (" + changes.Count + " references)");

            try
            {
                foreach (var c in changes) c.Lod.Filter.sharedMesh = c.To;
                foreach (var kt in restore)
                {
                    if (!CliffCore308.ParseTileKey(kt.key, out int col, out int row)) continue;
                    var t = CliffCore308.FindTile(root, cfg, col, row, true);
                    foreach (var kl in kt.lods) { var l = t.Lod(kl.lod); var m = CliffCore308.LoadMesh(kl.original.guid, kl.original.fileId); if (l != null && m != null) l.Filter.sharedMesh = m; }
                    var lod0 = t.Lod(cfg.colliderLod); if (lod0 != null) { t.Collider.sharedMesh = null; t.Collider.sharedMesh = lod0.Filter.sharedMesh; }
                    kt.applied = false; foreach (var kl in kt.lods) kl.applied = new CliffCore308.MeshRecord(); kt.colliderApplied = new CliffCore308.MeshRecord();
                }
                foreach (var t in tiles)
                {
                    var lod0 = t.Lod(cfg.colliderLod).Filter.sharedMesh;
                    t.Collider.sharedMesh = null; t.Collider.sharedMesh = lod0;
                    var kt = ledger.tiles.First(x => x.tile == t.Name); kt.applied = true; kt.colliderApplied = CliffCore308.Record(lod0, true);
                    foreach (var kl in kt.lods) { var l = t.Lod(kl.lod); if (l != null) kl.applied = CliffCore308.Record(l.Filter.sharedMesh, true); }
                }
                layout.FinalSurface = heightAsset; EditorUtility.SetDirty(layout); AssetDatabase.SaveAssetIfDirty(layout);
                ledger.layoutApplied = true; ledger.layoutAppliedSurface = surface.heightAsset; ledger.layoutAppliedSurfaceGuid = AssetDatabase.AssetPathToGUID(surface.heightAsset);
                Physics.SyncTransforms();
                PostLedger308.SaveScene(scene);
            }
            catch (Exception e)
            {
                // some references may already be re-pointed in memory: drop them, so that no later save (of any session) persists half of a stage
                ReloadFromDisk(path);
                throw new Exception("tiles " + stageId + " stopped before the scene was saved (" + e.GetType().Name + ": " + e.Message + "); the scene was reloaded from disk, the ledger stays 'applying' with every original recorded (the layout asset may"
                    + " already reference the stage field) - run tiles:" + ledger.key + " again to finish or revert:" + ledger.key + " to restore", e);
            }

            // verify on the saved scene objects
            int wrong = 0;
            foreach (var t in tiles)
            {
                var kt = ledger.tiles.First(x => x.tile == t.Name);
                foreach (var kl in kt.lods) { var f = t.Lod(kl.lod); if (f == null || f.Filter.sharedMesh != stageMeshes[t.Name + "/" + kl.lod]) wrong++; }
                if (t.Collider.sharedMesh != stageMeshes[t.Name + "/" + cfg.colliderLod]) wrong++;
            }
            ledger.colliderSetSha256 = ColliderSet(cfg, root);
            ledger.sceneShaAfter = CliffCore308.ShaFile(PostLedger308.Abs(path));
            ledger.state = wrong == 0 ? "applied" : "applying";
            WriteLedger(cfg, ledger, "tiles " + stageId + ": " + ledger.state + " (" + changes.Count + " references, " + restore.Count + " tiles restored, wrong after save " + wrong + ")");
            if (wrong > 0) return "error: " + wrong + " references are not on the stage meshes after the save; ledger left in 'applying' (" + LedgerFile(cfg, ledger.key) + ")";
            return "tiles " + plan + " | applied and saved; originals recorded for " + ledger.tiles.Length + " tiles; collider set sha " + ledger.colliderSetSha256.Substring(0, 12) + "; scene sha " + Short(ledger.sceneShaBefore) + " -> " + Short(ledger.sceneShaAfter)
                + "; backup " + ledger.backup + "; ledger " + LedgerFile(cfg, ledger.key) + " (+ .txt). LODGroup bounds untouched. Next: Enclosure305 build-scene, then check:" + ledger.key;
        }

        /// <summary>A scene save while another session's in-memory preview is up would persist the renderers it switched off.</summary>
        static void RequireNoPreview()
        {
            var up = Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g != null && !EditorUtility.IsPersistent(g) && g.transform.parent == null && (g.hideFlags & HideFlags.DontSave) != 0
                && (g.hideFlags & HideFlags.HideInHierarchy) == 0 && g.name.EndsWith("_Preview", StringComparison.Ordinal)).Select(g => g.name).Distinct().ToArray();
            if (up.Length > 0) throw new PostLedger308.Refused("an in-memory preview is up (" + string.Join(", ", up) + "); run its preview:off first - saving or switching the scene now would keep its hidden renderers off");
        }

        internal static string ColliderSet(CliffCore308.Config cfg, Transform root) => CliffCore308.ColliderSet(cfg, root);

        /// <summary>After a write that stopped half way the scene in memory may hold some re-pointed references without being saved (a
        /// reference set from script does not mark the scene dirty, so the next save of ANY session would persist it). Reload the scene
        /// from disk; OpenScene from script discards the in-memory state without a dialog. The ledger keeps what it recorded.</summary>
        static void ReloadFromDisk(string path)
        {
            try { UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Single); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>A mesh this ledger family made (any stage): Meshes/Terrain/&lt;stage&gt;/ under the config's asset root.</summary>
        static bool IsStageMesh(CliffCore308.Config cfg, Mesh mesh) => mesh != null && AssetDatabase.GetAssetPath(mesh).StartsWith(cfg.assetRoot + "/Meshes/Terrain/", StringComparison.Ordinal);

        static string Revert(CliffCore308.Config cfg, string token, Dictionary<string, string> opt, bool dry)
        {
            string path = CliffCore308.ScenePath(cfg, token);
            PostLedger308.RequireEditable();
            var ledger = ReadLedger(cfg, path);
            string part = opt != null && opt.TryGetValue("part", out string pv) && pv.Length > 0 ? pv : "all";
            if (part != "all" && part != "refs" && part != "layout") throw new PostLedger308.Refused("part must be refs or layout");
            bool refs = part != "layout", lay = part != "refs";
            if (ledger.tiles.Length == 0 && !ledger.layoutApplied && ledger.layoutOriginalSurfaceGuid == "") return "revert " + path + ": nothing recorded for this scene - no-op";

            RequireNoPreview();
            var scene = PostLedger308.Open(path);
            var root = CliffCore308.TerrainRoot(scene, cfg);
            var changes = new List<Change>(); var refusals = new List<string>(); var touched = new List<(CliffCore308.TileRef tile, TileLedger ledger)>(); int already = 0; var notes = new List<string>();
            if (refs)
                foreach (var kt in ledger.tiles)
                {
                    if (!CliffCore308.ParseTileKey(kt.key, out int col, out int row)) throw new PostLedger308.Refused("bad tile key in the ledger: " + kt.key);
                    var t = CliffCore308.FindTile(root, cfg, col, row, true); touched.Add((t, kt));
                    foreach (var kl in kt.lods)
                    {
                        var l = t.Lod(kl.lod);
                        if (l == null) { refusals.Add(t.Name + " has no " + kl.lod); continue; }
                        if (Same(l.Mesh, kl.original)) { already++; continue; }
                        // restorable: the mesh this ledger applied; any #308 stage mesh (an apply that stopped after the scene save and before
                        // the ledger recorded it leaves the 'applied' record empty); a missing mesh (a stage asset deleted by hand)
                        if (!Same(l.Mesh, kl.applied) && !IsStageMesh(cfg, l.Mesh) && l.Mesh != null)
                        {
                            refusals.Add(t.Name + "/" + kl.lod + " references " + l.Path + ": neither the recorded original (" + kl.original.path + ") nor a #308 stage mesh (" + cfg.assetRoot + "/Meshes/Terrain/)"); continue;
                        }
                        var original = CliffCore308.LoadMesh(kl.original.guid, kl.original.fileId);
                        if (original == null) { refusals.Add("the recorded original of " + t.Name + "/" + kl.lod + " (" + kl.original.path + ", guid " + kl.original.guid + ") cannot be loaded"); continue; }
                        if (kl.original.fileSha256 != "" && CliffCore308.ShaFile(PostLedger308.Abs(AssetDatabase.GetAssetPath(original))) != kl.original.fileSha256) notes.Add(kl.original.path + " changed on disk since it was recorded (the reference is still restored)");
                        changes.Add(new Change { Tile = t, Lod = l, To = original, Why = "original" });
                    }
                }
            var session = CliffCore308.Session(scene); var layout = session != null ? session.MountainLayout : null;
            TextAsset originalSurface = null; bool layoutTodo = false;
            // the layout is judged by what it references NOW, not by the ledger's layoutApplied flag: an apply that stopped between the layout
            // save and the final ledger write leaves the flag false on a layout that already carries the stage field
            if (lay && ledger.layoutOriginalSurfaceGuid != "")
            {
                if (layout == null || AssetDatabase.GetAssetPath(layout) != ledger.layout) refusals.Add("the scene layout is not the recorded " + ledger.layout);
                else
                {
                    var now = layout.FinalSurface; string nowGuid = "", nowPath = now != null ? AssetDatabase.GetAssetPath(now) : "";
                    if (now != null && !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(now, out nowGuid, out long _)) nowGuid = "";
                    originalSurface = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(ledger.layoutOriginalSurfaceGuid));
                    bool stageField = now == null || (ledger.layoutAppliedSurfaceGuid != "" && nowGuid == ledger.layoutAppliedSurfaceGuid) || nowPath.StartsWith(cfg.assetRoot + "/Surface/", StringComparison.Ordinal);
                    if (originalSurface == null) refusals.Add("the recorded original FinalSurface (" + ledger.layoutOriginalSurface + ") cannot be loaded");
                    else if (nowGuid == ledger.layoutOriginalSurfaceGuid) { }
                    else if (!stageField) refusals.Add("layout FinalSurface is " + nowPath + ": neither the recorded original nor a #308 stage field (" + cfg.assetRoot + "/Surface/)");
                    else layoutTodo = true;
                }
            }
            if (refusals.Count > 0) throw new PostLedger308.Refused(refusals.Count + " references match neither the recorded originals nor the applied stage meshes; nothing changed: " + string.Join(" | ", refusals.Take(12)) + (refusals.Count > 12 ? " | ..." : ""));
            // a collider left on a stage mesh under an already restored LOD0 filter is work too (never a silent no-op)
            int colliders = touched.Count(x => !Same(x.tile.Collider.sharedMesh, x.ledger.colliderOriginal));
            string plan = "revert " + path + " (part " + part + "): mesh references to restore " + changes.Count + ", already original " + already + ", colliders to restore " + colliders + ", layout FinalSurface " + (layoutTodo ? "-> " + ledger.layoutOriginalSurface : "unchanged");
            if (changes.Count == 0 && colliders == 0 && !layoutTodo)
            {
                if (!dry && ledger.state != "reverted" && ledger.state != "none" && part == "all") { foreach (var kt in ledger.tiles) kt.applied = false; ledger.state = "reverted"; ledger.layoutApplied = false; WriteLedger(cfg, ledger, "revert: nothing left to restore"); }
                return plan + " | no-op";
            }
            if (dry) return "revert-dry " + plan.Substring(7) + " | nothing written";

            string utc = PostLedger308.Utc(), backupDir = CliffCore308.OutFile(cfg, "Backup");
            ledger.sceneShaBefore = CliffCore308.ShaFile(PostLedger308.Abs(path));
            ledger.backup = PostLedger308.Backup(backupDir, utc + "-revert-" + ledger.key, path).Replace('\\', '/');
            if (layoutTodo) PostLedger308.Backup(backupDir, utc + "-revert-" + ledger.key, ledger.layout);
            int wrong = 0;
            try
            {
                foreach (var c in changes) c.Lod.Filter.sharedMesh = c.To;
                foreach (var (t, kt) in touched)
                {
                    var lod0 = t.Lod(cfg.colliderLod); if (lod0 == null) continue;
                    var want = lod0.Filter.sharedMesh;
                    if (t.Collider.sharedMesh != want) { t.Collider.sharedMesh = null; t.Collider.sharedMesh = want; }
                }
                if (layoutTodo) { layout.FinalSurface = originalSurface; EditorUtility.SetDirty(layout); AssetDatabase.SaveAssetIfDirty(layout); }
                Physics.SyncTransforms();
                PostLedger308.SaveScene(scene);
            }
            catch (Exception e)
            {
                ReloadFromDisk(path);
                throw new Exception("revert stopped before the scene was saved (" + e.GetType().Name + ": " + e.Message + "); the scene was reloaded from disk, the ledger is unchanged - run revert:" + ledger.key + " again", e);
            }
            foreach (var (t, kt) in touched)
            {
                foreach (var kl in kt.lods) { var l = t.Lod(kl.lod); if (l == null || !Same(l.Filter.sharedMesh, kl.original)) wrong++; }
                if (!Same(t.Collider.sharedMesh, kt.colliderOriginal)) wrong++;
            }
            if (layoutTodo && layout.FinalSurface != originalSurface) wrong++;
            ledger.colliderSetSha256 = ColliderSet(cfg, root);
            ledger.sceneShaAfter = CliffCore308.ShaFile(PostLedger308.Abs(path));
            if (wrong == 0)
            {
                foreach (var (t, kt) in touched) kt.applied = false;
                if (lay) ledger.layoutApplied = false;   // the layout is on its recorded original (restored now or found there)
                ledger.state = part == "all" || (!ledger.layoutApplied && ledger.tiles.All(t => !t.applied)) ? "reverted" : ledger.state;
            }
            WriteLedger(cfg, ledger, "revert (part " + part + "): " + changes.Count + " references, layout " + (layoutTodo ? "restored" : "unchanged") + ", wrong after save " + wrong);
            if (wrong > 0) return "error: " + wrong + " references are not on the recorded originals after the save; the ledger state stays '" + ledger.state + "' (" + LedgerFile(cfg, ledger.key) + ")";
            return plan + " | restored and saved; every tile reference equals its recorded original (GUID + fileID); scene sha " + Short(ledger.sceneShaBefore) + " -> " + Short(ledger.sceneShaAfter)
                + (ledger.sceneShaPristine != "" ? (ledger.sceneShaAfter == ledger.sceneShaPristine ? " = the pre-#308 scene file byte for byte" : " (pre-#308 file was " + Short(ledger.sceneShaPristine) + "; other saved changes or serialization order differ)") : "") + "; backup " + ledger.backup
                + (notes.Count > 0 ? " | WARN " + string.Join("; ", notes.Distinct().Take(5)) : "") + " | ledger " + LedgerFile(cfg, ledger.key);
        }
    }
}
