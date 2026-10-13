using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 mountain temples (protected root Reworld292_Sansa) - scoped repair, DECISIONS D308-76.
    //   sansa:diag     read only: near/far hall pose and LOD levels, colliders, forecourt stones, gaps to the terrain, content points nearby
    public static partial class Assembly308
    {
        const string SansaRoot = "Reworld292_Sansa", SansaNear = "KoreanHall_SourceAdaptation", SansaFar = "KoreanHall_Distant";
        static float TerrainY(float x, float z)
        {
            float y = float.NaN;
            foreach (var hit in Physics.RaycastAll(new Vector3(x, 3000f, z), Vector3.down, 6000f, ~0, QueryTriggerInteraction.Ignore)) if (IsTerrain(hit.transform) && (float.IsNaN(y) || hit.point.y > y)) y = hit.point.y;
            return y;
        }
        static string CompsOf(Transform c) => string.Join("+", c.GetComponents<Component>().Where(x => x != null && !(x is Transform)).Select(x => x.GetType().Name + (x is Renderer rr && !rr.enabled ? "[disabled]" : "")));
        /// <summary>Paving (Floor_stone*) footprint of a hall in hall space, and its lowest world height.</summary>
        static bool SansaPaving(Transform near, out Vector3 lo, out Vector3 hi, out float baseY)
        {
            lo = Vector3.one * 1e9f; hi = Vector3.one * -1e9f; baseY = float.MaxValue; bool any = false;
            foreach (var r in near.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.name.StartsWith("Floor_stone", StringComparison.Ordinal)) continue; var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue; var mb = mf.sharedMesh.bounds; any = true;
                for (int k = 0; k < 8; k++) { var w = r.transform.TransformPoint(mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) * 2 - 1, (k >> 1 & 1) * 2 - 1, (k >> 2 & 1) * 2 - 1))); var l = near.InverseTransformPoint(w); lo = Vector3.Min(lo, l); hi = Vector3.Max(hi, l); baseY = Mathf.Min(baseY, w.y); }
            }
            return any;
        }
        static string Sansa(Scene main, string op)
        {
            if (op == "diag") return SansaDiag(main);
            if (op == "streamverts") return StreamVerts(main);
            if (op == "apply") return SansaApply(main);
            if (op == "revert") return SansaRevert(main);
            return "commands: sansa:diag | sansa:apply | sansa:revert";
        }
        [Serializable] sealed class SvUnit { public string realm = "", name = ""; public int renderers; public long vertices; }
        [Serializable] sealed class SvManifest { public List<SvUnit> units = new List<SvUnit>(); }
        /// <summary>Read only: units of the realm scenes whose vertex or renderer count differs from Stage308_stream/manifest.json (RealmStream308 AC-S4).</summary>
        static string StreamVerts(Scene main)
        {
            var man = JsonUtility.FromJson<SvManifest>(File.ReadAllText(Path.Combine(Repo, "Tools", "Unity", "Stage308_stream", "manifest.json")).TrimStart((char)0xFEFF)); var sb = new StringBuilder("Assembly308 sansa:streamverts" + (char)10);
            EachScene(main, s =>
            {
                string id = s.name.Substring(6); var mine = man.units.Where(u => u.realm == id).ToList(); var root = s.GetRootGameObjects().OrderByDescending(g => g.transform.childCount).First().transform;
                sb.Append(s.name).Append(" units ").Append(root.childCount).Append(" manifest ").Append(mine.Count).Append((char)10);
                for (int k = 0; k < Math.Min(root.childCount, mine.Count); k++)
                {
                    var c = root.GetChild(k); long v = 0; int r = 0; var by = new Dictionary<string, long>();
                    foreach (var f in c.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh != null) { v += f.sharedMesh.vertexCount; string key = f.sharedMesh.name + " (" + AssetDatabase.GetAssetPath(f.sharedMesh) + ")"; by[key] = (by.TryGetValue(key, out long o) ? o : 0) + f.sharedMesh.vertexCount; }
                    r = c.GetComponentsInChildren<MeshRenderer>(true).Length; if (v == mine[k].vertices && r == mine[k].renderers && c.name == mine[k].name) continue;
                    sb.Append("  DIFF ").Append(c.name).Append(" (manifest ").Append(mine[k].name).Append(") vertices ").Append(v).Append(" vs ").Append(mine[k].vertices).Append(" (").Append(v - mine[k].vertices).Append("), renderers ").Append(r).Append(" vs ").Append(mine[k].renderers).Append(", meshes ").Append(by.Count).Append((char)10);
                    foreach (var kv in by.OrderBy(x => x.Value).Take(12)) sb.Append("      ").Append(kv.Value).Append("  ").Append(kv.Key).Append((char)10);
                }
            }, false);
            File.WriteAllText(Path.Combine(OutDir, "stream_verts.txt"), sb.ToString()); return sb.ToString();
        }
        static string SansaDiag(Scene main)
        {
            var root = main.GetRootGameObjects().FirstOrDefault(g => g.name == SansaRoot); if (root == null) return "REFUSED no " + SansaRoot;
            Physics.SyncTransforms(); var sb = new StringBuilder("Assembly308 sansa:diag\n");
            var points = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(mb => mb != null && mb.gameObject.scene == main && (mb.GetType().Name.IndexOf("Point", StringComparison.Ordinal) >= 0 || mb.GetType().Name.IndexOf("Interact", StringComparison.Ordinal) >= 0 || mb.GetType().Name.IndexOf("Spawn", StringComparison.Ordinal) >= 0)).ToList();
            foreach (Transform t in root.transform)
            {
                var near = t.Find(SansaNear); var far = t.Find(SansaFar); sb.Append("== ").Append(t.name).Append(" pos ").Append(t.position.ToString("F2")).Append(" children ").Append(t.childCount).Append('\n');
                if (near == null) { sb.Append("   NO near hall\n"); continue; }
                BoundsOf(near, out var nb); var nr = near.GetComponentsInChildren<Renderer>(true);
                sb.Append("   near pos ").Append(near.position.ToString("F2")).Append(" yaw ").Append(F1(near.eulerAngles.y)).Append(" active ").Append(near.gameObject.activeInHierarchy).Append(" renderers ").Append(nr.Length).Append(" enabled ").Append(nr.Count(r => r.enabled)).Append(" batching-static ").Append(nr.Count(r => GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.BatchingStatic))).Append('\n');
                if (far != null) { var fr = far.GetComponentsInChildren<Renderer>(true); sb.Append("   far  pos delta ").Append((far.position - near.position).magnitude.ToString("F3")).Append(" yaw delta ").Append(F1(Mathf.DeltaAngle(near.eulerAngles.y, far.eulerAngles.y))).Append(" scale ").Append(far.lossyScale.ToString("F2")).Append(" active ").Append(far.gameObject.activeInHierarchy).Append(" renderers ").Append(fr.Length).Append(" enabled ").Append(fr.Count(r => r.enabled)).Append(" verts ").Append(fr.Sum(r => { var mf = r.GetComponent<MeshFilter>(); return mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : 0; })).Append('\n'); }
                else sb.Append("   NO far hall\n");
                foreach (var g in t.GetComponentsInChildren<LODGroup>(true))
                {
                    if (g.transform != near) continue; var lods = g.GetLODs(); sb.Append("   LODGroup on ").Append(g.name).Append(" enabled ").Append(g.enabled).Append(" size ").Append(F(g.size)).Append(" fade ").Append(g.fadeMode);
                    for (int i = 0; i < lods.Length; i++)
                    {
                        var rs = lods[i].renderers; int nul = rs.Count(r => r == null), inNear = rs.Count(r => r != null && UnderRoot(r.transform, near)), inFar = rs.Count(r => r != null && far != null && UnderRoot(r.transform, far));
                        sb.Append(" | LOD").Append(i).Append(" h ").Append(lods[i].screenRelativeTransitionHeight.ToString("0.000", Inv)).Append(" n ").Append(rs.Length).Append(" null ").Append(nul).Append(" near ").Append(inNear).Append(" far ").Append(inFar);
                    }
                    sb.Append('\n');
                }
                var cols = t.GetComponentsInChildren<Collider>(true); sb.Append("   colliders under temple ").Append(cols.Length).Append(": ").Append(string.Join(", ", cols.GroupBy(c => c.transform.parent == t ? c.name : c.transform.parent.name + "/" + c.name).Select(g => g.Key + " x" + g.Count()).Take(8))).Append('\n');
                var hits = Physics.OverlapBox(nb.center, nb.extents * .5f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Where(c => !IsTerrain(c.transform)).ToList();
                sb.Append("   solid colliders inside the hall body ").Append(hits.Count).Append(": ").Append(string.Join(", ", hits.Take(6).Select(c => IdOf(c.transform)))).Append('\n');
                foreach (string grp in new[] { "WallSet", "RedPillarSquare", "RedPillarThinStone", "OuterWall", "Floor_Wood", "Roof_Frame" }) if (LocalBounds(near, near.GetComponentsInChildren<Renderer>(false).Where(r => r.name.StartsWith(grp, StringComparison.Ordinal)), out var glo, out var ghi)) sb.Append("   group ").Append(grp).Append(" local ").Append(glo.ToString("F2")).Append(" .. ").Append(ghi.ToString("F2")).Append('\n');
                if (SansaPaving(near, out var lo, out var hi, out float baseY))
                {
                    float maxGap = -1e9f, minGap = 1e9f; int n = 0, over = 0;
                    for (int ix = 0; ix <= 12; ix++) for (int iz = 0; iz <= 8; iz++) { var w = near.TransformPoint(new Vector3(Mathf.Lerp(lo.x, hi.x, ix / 12f), lo.y, Mathf.Lerp(lo.z, hi.z, iz / 8f))); float ty = TerrainY(w.x, w.z); if (float.IsNaN(ty)) continue; float gap = baseY - ty; maxGap = Mathf.Max(maxGap, gap); minGap = Mathf.Min(minGap, gap); n++; if (gap > .06f) over++; }
                    sb.Append("   paving local ").Append(lo.ToString("F2")).Append(" .. ").Append(hi.ToString("F2")).Append(" base y ").Append(F(baseY)).Append(" gap to terrain max ").Append(F(maxGap)).Append(" min ").Append(F(minGap)).Append(" samples ").Append(n).Append(" hanging ").Append(over).Append('\n');
                }
                foreach (Transform c in t)
                {
                    if (c == near || c == far) continue; if (c.name == "FoundationStone") { float top = c.position.y + c.localScale.y * .5f, mt = TerrainMin(new[] { c.position, c.TransformPoint(new Vector3(.5f, 0f, .5f)), c.TransformPoint(new Vector3(-.5f, 0f, .5f)), c.TransformPoint(new Vector3(.5f, 0f, -.5f)), c.TransformPoint(new Vector3(-.5f, 0f, -.5f)) }); sb.Append("   - stone#").Append(c.GetSiblingIndex()).Append(" yaw ").Append(F1(c.eulerAngles.y)).Append(" h ").Append(F(c.localScale.y)).Append(" hang ").Append(F(top - c.localScale.y - mt)).Append('\n'); continue; } bool has = BoundsOf(c, out var b); float ty = TerrainY(c.position.x, c.position.z);
                    sb.Append("   - ").Append(c.name).Append('#').Append(c.GetSiblingIndex()).Append(c.gameObject.activeSelf ? "" : " [OFF]").Append(" pos ").Append(c.position.ToString("F2")).Append(" yaw ").Append(F1(c.eulerAngles.y)).Append(" scale ").Append(c.localScale.ToString("F2"));
                    if (has) sb.Append(" bounds y ").Append(F(b.min.y)).Append("..").Append(F(b.max.y)).Append(" s ").Append(b.size.ToString("F1"));
                    sb.Append(" terrain ").Append(F(ty)); if (has) sb.Append(" bottom-terrain ").Append(F(b.min.y - ty));
                    sb.Append(" comps ").Append(CompsOf(c));
                    if (c.childCount > 0) sb.Append(" kids ").Append(string.Join(",", c.Cast<Transform>().Take(3).Select(k => k.name + (k.gameObject.activeSelf ? "" : "[off]") + "(" + CompsOf(k) + ")")));
                    sb.Append('\n');
                }
                int pts = 0;
                foreach (var mb in points)
                {
                    if (UnderRoot(mb.transform, t)) continue; var d = mb.transform.position - nb.center; d.y = 0; if (d.magnitude > 30f || pts++ > 10) continue;
                    sb.Append("   near: ").Append(mb.GetType().Name).Append(' ').Append(IdOf(mb.transform)).Append(" pos ").Append(mb.transform.position.ToString("F1")).Append('\n');
                }
            }
            File.WriteAllText(Path.Combine(OutDir, "sansa_diag.txt"), sb.ToString()); return sb.ToString();
        }
    }
}
