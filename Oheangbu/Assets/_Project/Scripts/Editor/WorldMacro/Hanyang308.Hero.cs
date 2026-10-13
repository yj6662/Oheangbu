using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 Hanyang phase 5a (SPEC-WORLD-HANYANG-308 §14): composites of imported light copies.
    //   hero:compose     Tools/Unity/Stage308_hanyang/hero308.json -> one merged mesh + one-renderer prefab per composite (many small copies of one model, e.g. 품계석 rows)
    //                    pts = x, y, z, yaw per copy, relative to the composite origin. New asset names per version (stale GPU buffer trap).
    public static partial class Hanyang308
    {
        static string HeroFile => Path.Combine(StageDir, "hero308.json");
        [Serializable] sealed class HComp { public string name = "", src = ""; public int lod; public float cull = 0.01f; public float[] pts = new float[0]; }
        [Serializable] sealed class HFile { public string version = "p5"; public List<HComp> composites = new List<HComp>(); }

        static string Hero(string[] a)
        {
            string verb = a.Length > 1 ? a[1] : "";
            switch (verb)
            {
                case "compose": return HeroCompose();
                case "hide": return HeroHide(a.Length > 2 ? a[2] : "");
                default: throw new Refuse("hero:" + verb + "?");
            }
        }

        //   hero:hide:off | hero:hide:on    renderers (only renderers: objects, colliders, components stay) of the kept objects listed in hide308.json, in the realm scene; backup + save
        [Serializable] sealed class HHide { public List<string> keys = new List<string>(); }
        static string HeroHide(string mode)
        {
            if (mode != "off" && mode != "on") throw new Refuse("use hero:hide:off | hero:hide:on"); string file = Path.Combine(StageDir, "hide308.json"); if (!File.Exists(file)) throw new Refuse("no " + file);
            var hh = JsonUtility.FromJson<HHide>(File.ReadAllText(file)); var main = Main(); if (main.isDirty) throw new Refuse("the play scene is dirty");
            var opened = new List<UnityEngine.SceneManagement.Scene>(); var realm = OpenAdditive(RealmScene, main, opened); var sb = new StringBuilder("Hanyang308 hero:hide:" + mode + "\n"); int n = 0, miss = 0;
            try
            {
                if (realm.isDirty) throw new Refuse("the realm scene is dirty"); string backup = BackupFiles("hide", Abs(RealmScene));
                foreach (var key in hh.keys)
                {
                    var t = Find(realm, key); if (t == null) { sb.AppendLine("  MISSING " + key); miss++; continue; }
                    foreach (var r in t.GetComponentsInChildren<Renderer>(true)) { r.enabled = mode == "on"; n++; }
                }
                if (miss > 0) throw new Refuse(sb + "nothing saved");
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(realm); bool ok = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(realm);
                sb.Append("renderers " + n + " -> " + (mode == "on" ? "enabled" : "disabled") + (ok ? " | saved" : " | FAILED not saved") + " | backup " + backup);
            }
            finally { foreach (var s in opened) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(s, true); }
            return sb.ToString();
        }

        static string HeroCompose()
        {
            if (!File.Exists(HeroFile)) throw new Refuse("no " + HeroFile); var hf = JsonUtility.FromJson<HFile>(File.ReadAllText(HeroFile)); var sb = new StringBuilder("Hanyang308 hero:compose " + hf.version + "\n");
            foreach (var c in hf.composites)
            {
                LiteMeshOf(c.src, c.lod, out var mesh, out var mats, out var M); var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv; int copies = c.pts.Length / 4;
                var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>(); var subs = new List<List<int>>(); for (int s = 0; s < mesh.subMeshCount; s++) subs.Add(new List<int>());
                for (int k = 0; k < copies; k++)
                {
                    var T = Matrix4x4.TRS(new Vector3(c.pts[k * 4], c.pts[k * 4 + 1], c.pts[k * 4 + 2]), Quaternion.Euler(0, c.pts[k * 4 + 3], 0), Vector3.one) * M; int b0 = nv.Count;
                    for (int i = 0; i < v.Length; i++) { nv.Add(T.MultiplyPoint3x4(v[i])); var q = T.MultiplyVector(i < n.Length ? n[i] : Vector3.up); nn.Add(q.sqrMagnitude < 1e-8f ? Vector3.up : q.normalized); nu.Add(i < uv.Length ? uv[i] : Vector2.zero); }
                    for (int s = 0; s < mesh.subMeshCount; s++) foreach (int i in mesh.GetTriangles(s)) subs[s].Add(b0 + i);
                }
                string mname = "H308_" + c.name + "_" + hf.version; var m = new Mesh { name = mname, indexFormat = nv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                m.SetVertices(nv); m.SetNormals(nn); m.SetUVs(0, nu); m.subMeshCount = subs.Count; for (int s = 0; s < subs.Count; s++) m.SetTriangles(subs[s], s); m.RecalculateTangents(); m.RecalculateBounds();
                SaveMesh(m, CityMeshDir + "/" + mname + ".asset");
                var root = new GameObject(c.name); root.AddComponent<MeshFilter>().sharedMesh = m; var mr = root.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats;
                var lg = root.AddComponent<LODGroup>(); lg.fadeMode = LODFadeMode.None; lg.SetLODs(new[] { new LOD(c.cull, new Renderer[] { mr }) }); lg.RecalculateBounds();
                PrefabUtility.SaveAsPrefabAsset(root, CityPrefabDir + "/" + c.name + ".prefab"); UnityEngine.Object.DestroyImmediate(root);
                sb.AppendLine("  " + c.name + " <- " + c.src + " LOD" + c.lod + " copies " + copies + " tris " + Tris(m) + " subs " + m.subMeshCount + " bounds " + m.bounds.min.ToString("F2") + ".." + m.bounds.max.ToString("F2"));
            }
            return sb.ToString();
        }
    }
}
