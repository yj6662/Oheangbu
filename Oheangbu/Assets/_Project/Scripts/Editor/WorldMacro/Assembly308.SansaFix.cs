using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 mountain temples - the repair itself (DECISIONS D308-76, second scoped exception for the protected root Reworld292_Sansa).
    //   sansa:apply    1) forecourt stone row turned to the hall's own yaw (the generator lays it on world axes, two halls are turned 12 deg)
    //                  2) forecourt stones stretched down to today's terrain  3) the bell lowered onto the ground
    //                  4) stone plinth under a hall whose paving hangs over the slope  5) box colliders for the hall room, its stone walls and paving
    //                  Everything new lives in the play scene root Assembly308Sansa (outside the protected tree); the only edits inside the
    //                  tree are transforms of FoundationStone / KoreanTempleBell, all in Ledger/sansa.json. Safe to run again (it first puts
    //                  ledger rows back, then works from the generator state) - run it again after any Reworld292 temple rebuild.
    //   sansa:revert   ledger rows back, root and plinth meshes removed.
    public static partial class Assembly308
    {
        const string SansaFixRoot = "Assembly308Sansa", SansaAssets = "Assets/_Project/Art/World/Assembly308Sansa";
        [Serializable] sealed class SansaRow { public string id = ""; public Vector3 posBefore, posAfter, scaleBefore, scaleAfter; public float yawBefore, yawAfter; }
        [Serializable] sealed class SansaLedger { public string state = "none", stamp = "", backup = ""; public List<SansaRow> rows = new List<SansaRow>(); public List<string> created = new List<string>(), meshes = new List<string>(); }
        [Serializable] sealed class SansaCfg { public string sansaStone = "Assets/_Project/Art/World/Finish297/Materials/stone_dressed.mat", sansaBody = "^(WallSet|RedPillarSquare)", sansaPieces = "^OuterWall"; public float sansaMinGap = .06f, sansaStoneSink = .08f, sansaBellSink = .03f, sansaMaxH = 6f; }
        static string SansaFile => Path.Combine(Stage, "Ledger", "sansa.json");

        static float TerrainMin(IEnumerable<Vector3> pts) { float m = float.NaN; foreach (var p in pts) { float y = TerrainY(p.x, p.z); if (!float.IsNaN(y) && (float.IsNaN(m) || y < m)) m = y; } return m; }
        static bool LocalBounds(Transform frame, IEnumerable<Renderer> rs, out Vector3 lo, out Vector3 hi)
        {
            lo = Vector3.one * 1e9f; hi = Vector3.one * -1e9f; bool any = false;
            foreach (var r in rs) { var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue; var mb = mf.sharedMesh.bounds; any = true; for (int k = 0; k < 8; k++) { var l = frame.InverseTransformPoint(r.transform.TransformPoint(mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) * 2 - 1, (k >> 1 & 1) * 2 - 1, (k >> 2 & 1) * 2 - 1)))); lo = Vector3.Min(lo, l); hi = Vector3.Max(hi, l); } }
            return any;
        }
        static void SansaRestore(SansaLedger led, StringBuilder sb)
        {
            int back = 0, gone = 0, other = 0;
            foreach (var r in led.rows)
            {
                var t = ResolveId(r.id); if (t == null) { gone++; continue; }
                if ((t.position - r.posAfter).sqrMagnitude < 1e-4f) { t.position = r.posBefore; t.localScale = r.scaleBefore; t.rotation = Quaternion.Euler(0f, r.yawBefore, 0f); back++; } else other++;
            }
            if (led.rows.Count > 0) sb.Append("  ledger rows put back ").Append(back).Append(", already at another pose (rebuilt by the generator?) ").Append(other).Append(", missing ").Append(gone).Append('\n');
        }
        static void SansaClear(Scene main, SansaLedger led)
        {
            var old = main.GetRootGameObjects().FirstOrDefault(g => g.name == SansaFixRoot); if (old != null) Object.DestroyImmediate(old);
            if (AssetDatabase.IsValidFolder(SansaAssets)) foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { SansaAssets })) AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
        }
        static string SansaRevert(Scene main)
        {
            if (!File.Exists(SansaFile)) return "Assembly308 sansa:revert: no ledger"; var led = JsonUtility.FromJson<SansaLedger>(File.ReadAllText(SansaFile)); var sb = new StringBuilder();
            string dir = Backup(main, "sansa"); SansaRestore(led, sb); SansaClear(main, led); led.rows.Clear(); led.created.Clear(); led.meshes.Clear(); led.state = "reverted"; led.backup = dir; led.stamp = DateTime.Now.ToString("yyyyMMddTHHmmss", Inv);
            EditorSceneManager.MarkSceneDirty(main); if (!EditorSceneManager.SaveScene(main)) return "FAILED the play scene was not saved"; File.WriteAllText(SansaFile, JsonUtility.ToJson(led, true));
            return "Assembly308 sansa:revert: done, backup " + dir + "\n" + sb;
        }
        static string SansaApply(Scene main)
        {
            var root = main.GetRootGameObjects().FirstOrDefault(g => g.name == SansaRoot); if (root == null) return "REFUSED no " + SansaRoot;
            string json = File.ReadAllText(Path.Combine(Stage, "assembly308.json")).TrimStart('﻿'); var cfg = JsonUtility.FromJson<SansaCfg>(json); var fc = ReadRep();
            var stone = AssetDatabase.LoadAssetAtPath<Material>(cfg.sansaStone); if (stone == null) return "REFUSED no material " + cfg.sansaStone;
            var bodyRe = new Regex(cfg.sansaBody); var pieceRe = new Regex(cfg.sansaPieces); var sb = new StringBuilder();
            var led = File.Exists(SansaFile) ? JsonUtility.FromJson<SansaLedger>(File.ReadAllText(SansaFile)) : new SansaLedger();
            string dir = Backup(main, "sansa"); SansaRestore(led, sb); SansaClear(main, led); Physics.SyncTransforms();
            led = new SansaLedger { state = "applied", stamp = DateTime.Now.ToString("yyyyMMddTHHmmss", Inv), backup = dir };
            if (!AssetDatabase.IsValidFolder(SansaAssets)) AssetDatabase.CreateFolder("Assets/_Project/Art/World", "Assembly308Sansa");
            var fix = new GameObject(SansaFixRoot); SceneManager.MoveGameObjectToScene(fix, main); float cs = fc.footingCell;
            foreach (Transform t in root.transform)
            {
                var near = t.Find(SansaNear); if (near == null) { sb.Append("== ").Append(t.name).Append(" SKIPPED no near hall\n"); continue; }
                BoundsOf(near, out var nb); float yawD = Mathf.DeltaAngle(180f, near.eulerAngles.y); var pivot = nb.center; var turn = Quaternion.Euler(0f, yawD, 0f); int turned = 0, fitted = 0; float deepest = 0f;
                sb.Append("== ").Append(t.name).Append(" hall yaw offset ").Append(F1(yawD)).Append('\n');
                var holder = new GameObject(t.name); holder.transform.SetParent(fix.transform, false); holder.layer = near.gameObject.layer;
                // 1 + 2: forecourt stones
                foreach (Transform c in t)
                {
                    if (c.name != "FoundationStone") continue; var row = new SansaRow { id = IdOf(c), posBefore = c.position, scaleBefore = c.localScale, yawBefore = c.eulerAngles.y }; bool rot = false;
                    if (Mathf.Abs(yawD) > .1f && Mathf.Abs(Mathf.DeltaAngle(c.eulerAngles.y, yawD)) > .1f)
                    {
                        var rel = c.position - pivot; float y = c.position.y; rel.y = 0f; var p = pivot + turn * rel; c.position = new Vector3(p.x, y, p.z); c.rotation = Quaternion.Euler(0f, yawD, 0f); rot = true; turned++;
                    }
                    float top = c.position.y + c.localScale.y * .5f, bottom = top - c.localScale.y;
                    float minT = TerrainMin(new[] { c.position, c.TransformPoint(new Vector3(.5f, 0f, .5f)), c.TransformPoint(new Vector3(-.5f, 0f, .5f)), c.TransformPoint(new Vector3(.5f, 0f, -.5f)), c.TransformPoint(new Vector3(-.5f, 0f, -.5f)) });
                    if (!float.IsNaN(minT) && (rot || bottom - minT > cfg.sansaMinGap))
                    {
                        float nbm = Mathf.Min(minT - cfg.sansaStoneSink, top - .1f);
                        if (top - nbm > cfg.sansaMaxH) sb.Append("  stone ").Append(c.GetSiblingIndex()).Append(" would be ").Append(F(top - nbm)).Append(" m tall - left as is\n");
                        else { var s = c.localScale; s.y = top - nbm; c.localScale = s; var p = c.position; p.y = (top + nbm) * .5f; c.position = p; fitted++; deepest = Mathf.Max(deepest, top - nbm); }
                    }
                    if ((c.position - row.posBefore).sqrMagnitude > 1e-6f || (c.localScale - row.scaleBefore).sqrMagnitude > 1e-6f || rot) { row.posAfter = c.position; row.scaleAfter = c.localScale; row.yawAfter = c.eulerAngles.y; led.rows.Add(row); }
                }
                sb.Append("  forecourt stones turned ").Append(turned).Append(", refitted to the terrain ").Append(fitted).Append(", tallest ").Append(F(deepest)).Append(" m\n");
                // 3: bell
                var bell = t.Find("KoreanTempleBell");
                if (bell != null && BoundsOf(bell, out var bb))
                {
                    float r = Mathf.Min(bb.extents.x, bb.extents.z) * .8f; var c0 = bb.center;
                    float minT = TerrainMin(new[] { c0, c0 + new Vector3(r, 0, 0), c0 - new Vector3(r, 0, 0), c0 + new Vector3(0, 0, r), c0 - new Vector3(0, 0, r) }); float dy = minT - cfg.sansaBellSink - bb.min.y;
                    if (!float.IsNaN(minT) && Mathf.Abs(dy) > cfg.sansaMinGap)
                    {
                        var row = new SansaRow { id = IdOf(bell), posBefore = bell.position, scaleBefore = bell.localScale, yawBefore = bell.eulerAngles.y }; bell.position += Vector3.up * dy; row.posAfter = bell.position; row.scaleAfter = bell.localScale; row.yawAfter = row.yawBefore; led.rows.Add(row);
                        sb.Append("  bell moved ").Append(F(dy)).Append(" m (its rim was ").Append(F(-dy - cfg.sansaBellSink)).Append(" m over the ground)\n");
                    }
                }
                // 4: plinth under the hall paving
                if (SansaPaving(near, out var lo, out var hi, out float baseY))
                {
                    float maxGap = -1e9f; for (int ix = 0; ix <= 12; ix++) for (int iz = 0; iz <= 8; iz++) { var w = near.TransformPoint(new Vector3(Mathf.Lerp(lo.x, hi.x, ix / 12f), lo.y, Mathf.Lerp(lo.z, hi.z, iz / 8f))); float ty = TerrainY(w.x, w.z); if (!float.IsNaN(ty)) maxGap = Mathf.Max(maxGap, baseY - ty); }
                    if (maxGap > cfg.sansaMinGap)
                    {
                        var origin = near.position; var rotq = near.rotation; var bottom = new Dictionary<long, float>(); float topL = baseY + .03f - origin.y, lowest = topL;
                        for (int ix = Mathf.FloorToInt(lo.x / cs); ix < Mathf.CeilToInt(hi.x / cs); ix++) for (int iz = Mathf.FloorToInt(lo.z / cs); iz < Mathf.CeilToInt(hi.z / cs); iz++)
                        {
                            float mt = TerrainMin(new[] { origin + rotq * new Vector3(ix * cs, 0, iz * cs), origin + rotq * new Vector3((ix + 1) * cs, 0, iz * cs), origin + rotq * new Vector3(ix * cs, 0, (iz + 1) * cs), origin + rotq * new Vector3((ix + 1) * cs, 0, (iz + 1) * cs), origin + rotq * new Vector3((ix + .5f) * cs, 0, (iz + .5f) * cs) });
                            if (float.IsNaN(mt)) continue; float b = mt - fc.footingSink - origin.y; bottom[Key(ix, iz)] = b; lowest = Mathf.Min(lowest, b);
                        }
                        if (topL - lowest > cfg.sansaMaxH + fc.footingSink) sb.Append("  plinth would be ").Append(F(topL - lowest)).Append(" m tall - SKIPPED\n");
                        else
                        {
                            var mesh = PlinthMesh(bottom, topL, cs, fc, "SansaPlinth_" + t.name);
                            if (mesh != null)
                            {
                                string mp = SansaAssets + "/" + mesh.name + ".asset"; AssetDatabase.CreateAsset(mesh, mp); AssetDatabase.SaveAssetIfDirty(mesh); led.meshes.Add(mp);
                                var go = new GameObject("HallPlinth"); go.transform.SetParent(holder.transform, false); go.transform.SetPositionAndRotation(origin, rotq); go.layer = near.gameObject.layer; go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = stone; go.AddComponent<MeshCollider>().sharedMesh = mesh;
                                sb.Append("  hall plinth: paving hung up to ").Append(F(maxGap)).Append(" m over the slope, plinth tallest ").Append(F(topL - lowest)).Append(" m, triangles ").Append(mesh.GetIndexCount(0) / 3).Append('\n');
                            }
                        }
                    }
                    else sb.Append("  hall paving sits on the ground (largest gap ").Append(F(maxGap)).Append(" m) - no plinth\n");
                    var pv = new GameObject("HallPaving"); pv.transform.SetParent(holder.transform, false); pv.transform.SetPositionAndRotation(near.TransformPoint((lo + hi) * .5f), near.rotation); pv.layer = near.gameObject.layer; pv.AddComponent<BoxCollider>().size = hi - lo;
                }
                // 5: colliders of the hall room and of its stone walls
                var rs = near.GetComponentsInChildren<Renderer>(false);
                if (LocalBounds(near, rs.Where(r => bodyRe.IsMatch(r.name)), out var blo, out var bhi))
                {
                    var frame = rs.FirstOrDefault(r => r.name == "Roof_Frame"); if (frame != null && LocalBounds(near, new[] { frame }, out var flo, out _)) bhi.y = Mathf.Max(bhi.y, flo.y);
                    var body = new GameObject("HallRoom"); body.transform.SetParent(holder.transform, false); body.transform.SetPositionAndRotation(near.TransformPoint((blo + bhi) * .5f), near.rotation); body.layer = near.gameObject.layer; body.AddComponent<BoxCollider>().size = bhi - blo;
                    sb.Append("  room collider local ").Append(blo.ToString("F2")).Append(" .. ").Append(bhi.ToString("F2")).Append('\n');
                }
                int pieces = 0;
                foreach (var r in rs)
                {
                    if (!pieceRe.IsMatch(r.name)) continue; var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue; var mb = mf.sharedMesh.bounds;
                    var pc = new GameObject("Wall_" + r.name); pc.transform.SetParent(holder.transform, false); pc.transform.SetPositionAndRotation(r.transform.TransformPoint(mb.center), r.transform.rotation); pc.layer = near.gameObject.layer; pc.AddComponent<BoxCollider>().size = Vector3.Scale(mb.size, r.transform.lossyScale); pieces++;
                }
                sb.Append("  stone wall colliders ").Append(pieces).Append('\n'); led.created.Add(SansaFixRoot + "/" + t.name);
            }
            Physics.SyncTransforms(); EditorSceneManager.MarkSceneDirty(main); if (!EditorSceneManager.SaveScene(main)) return "FAILED the play scene was not saved";
            Directory.CreateDirectory(Path.GetDirectoryName(SansaFile)); File.WriteAllText(SansaFile, JsonUtility.ToJson(led, true));
            string text = "Assembly308 sansa:apply: ledger rows " + led.rows.Count + ", plinth meshes " + led.meshes.Count + ", backup " + dir + "\n" + sb; File.WriteAllText(Path.Combine(OutDir, "sansa_apply.txt"), text); return text;
        }
    }
}
