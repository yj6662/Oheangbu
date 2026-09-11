using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;

namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120FrostDetailBuilder
    {
        const string Folder = "Assets/_Project/Art/SpellVFX120/Meshes/MetalDetails";
        [Serializable] sealed class Report
        {
            public string status = "AUTHORED_AWAITING_USER_VISUAL_REVIEW", beforeJson, afterJson;
            public string glyph = "상", snapshot, needlePath, cordPath;
            public int needleVertices, needleTris, cordVertices, cordTris, cordInstances = 3;
            public Vector3 needleBounds, cordBounds;
            public string technicalCueCheck, art = "AWAITING_USER_REVIEW", gameplay = "UNCONNECTED";
        }
        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required");
            var p = AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot + "/Profiles/078_C0C1.asset");
            if (p == null || p.Glyph != "상") throw new InvalidOperationException("Missing 상 profile");
            string source = AssetDatabase.GetAssetPath(p);
            var report = new Report { beforeJson = JsonUtility.ToJson(p) };
            string snapshots = Path.Combine(Vfx120Editor.Output, "MetalDetailOriginals");
            Directory.CreateDirectory(snapshots);
            report.snapshot = Path.Combine(snapshots, "078_C0C1.asset.txt");
            if (!File.Exists(report.snapshot)) File.Copy(source, report.snapshot);
            report.needlePath = Folder + "/VFX120_FrostNeedle_078.asset";
            report.cordPath = Folder + "/" + Vfx120FrostCordMotion.CordName + ".asset";
            var needle = Save(Needle(), report.needlePath);
            var cord = Save(Cord(), report.cordPath);
            p.BodyMesh = needle; p.AccentMesh = cord; p.Count = 1; p.RibbonCount = 0;
            p.PartScale = Vector3.one; p.NativeImpactScale = .45f;
            EditorUtility.SetDirty(p); AssetDatabase.SaveAssetIfDirty(p);
            report.afterJson = JsonUtility.ToJson(p);
            report.needleVertices = needle.vertexCount; report.needleTris = needle.triangles.Length / 3;
            report.cordVertices = cord.vertexCount; report.cordTris = cord.triangles.Length / 3;
            report.needleBounds = needle.bounds.size; report.cordBounds = cord.bounds.size;
            report.technicalCueCheck = CheckCue();
            File.WriteAllText(Path.Combine(Vfx120Editor.Output, "frost_detail_078_build.json"), JsonUtility.ToJson(report, true));
            return report.status + "; " + report.technicalCueCheck;
        }
        static string CheckCue()
        {
            var c = Vfx120CueMotion.Context.Create("상");
            c.FrostCords = true; c.Duration = 1.9f; c.ImpactTime = .2f;
            int samples = 0;
            foreach (int fps in new[] { 30, 60, 120 }) for (int f = 0; f <= fps * 2; f++)
            {
                c.Age = f / (float)fps; c.HitAt = -1;
                for (int i = 0; i < 3; i++)
                {
                    Vfx120CueMotion.TrySample(c, Vfx120CueMotion.PartRole.Accent, i, 3, out var absent);
                    if (absent.Visible) throw new InvalidOperationException("Frost appeared without a hit cue");
                    c.HitAt = .2f;
                    Vfx120CueMotion.TrySample(c, Vfx120CueMotion.PartRole.Accent, i, 3, out var pose);
                    if (c.Age < c.HitAt && pose.Visible) throw new InvalidOperationException("Frost before hit");
                    if (c.Age > 1.3f && pose.Visible) throw new InvalidOperationException("Residual frost");
                    if (float.IsNaN(pose.Position.sqrMagnitude + pose.Scale.sqrMagnitude)) throw new InvalidOperationException("Invalid frost pose");
                    c.HitAt = -1; samples++;
                }
            }
            c.HitAt = .2f; c.Age = .3f;
            Vfx120CueMotion.TrySample(c, Vfx120CueMotion.PartRole.Accent, 0, 3, out var contact);
            c.Age = .85f;
            Vfx120CueMotion.TrySample(c, Vfx120CueMotion.PartRole.Accent, 0, 3, out var fallen);
            if (!contact.Visible || !fallen.Visible || fallen.Position.y >= contact.Position.y)
                throw new InvalidOperationException("Contact/fall phase missing");
            return "PASS_SCOPED_CUE_TIMING_ONLY samples=" + samples;
        }
        static Mesh Save(Mesh mesh, string path)
        {
            mesh.name = Path.GetFileNameWithoutExtension(path);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear(); existing.SetVertices(mesh.vertices); existing.SetUVs(0, mesh.uv);
                existing.SetTriangles(mesh.triangles, 0); UnityEngine.Object.DestroyImmediate(mesh); mesh = existing;
            }
            else AssetDatabase.CreateAsset(mesh, path);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents(); mesh.UploadMeshData(false);
            if (mesh.vertexBufferCount < 1) throw new InvalidOperationException("No vertex buffer");
            EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh); return mesh;
        }
        static Mesh Needle()
        {
            var v = new List<Vector3> { new Vector3(0, 0, -.12f), new Vector3(0, 0, .12f) };
            var t = new List<int>();
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3;
                v.Add(new Vector3(Mathf.Cos(a) * .007f, Mathf.Sin(a) * .007f, -.035f));
            }
            for (int i = 0; i < 6; i++) { int a = 2 + i, b = 2 + (i + 1) % 6; t.AddRange(new[] { 0, b, a, 1, a, b }); }
            return Finish(v, t);
        }
        static Mesh Cord()
        {
            // Angular, open half-wrap. Three rotations make a loose bound bundle;
            // the narrow rectangular strand never becomes a broad tooth-like plate.
            var p = new[] { new Vector3(-.06f, -.02f, -.10f), new Vector3(-.10f, .03f, -.035f),
                new Vector3(-.05f, .07f, .065f), new Vector3(.035f, .045f, .10f), new Vector3(.09f, -.02f, .035f) };
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < p.Length; i++)
            {
                Vector3 tangent = (p[Mathf.Min(i + 1, p.Length - 1)] - p[Mathf.Max(0, i - 1)]).normalized;
                Vector3 a = Vector3.Cross(tangent, Vector3.up).normalized;
                Vector3 b = Vector3.Cross(tangent, a).normalized;
                float radius = i == 0 || i == p.Length - 1 ? .0015f : .003f;
                for (int side = 0; side < 4; side++)
                {
                    float angle = side * Mathf.PI * .5f;
                    v.Add(p[i] + (a * Mathf.Cos(angle) + b * Mathf.Sin(angle)) * radius);
                }
                if (i == 0) continue;
                for (int side = 0; side < 4; side++)
                {
                    int a0 = (i - 1) * 4 + side, a1 = (i - 1) * 4 + (side + 1) % 4;
                    t.AddRange(new[] { a0, a1, a0 + 4, a1, a1 + 4, a0 + 4 });
                }
            }
            t.AddRange(new[] { 0, 2, 1, 0, 3, 2, 16, 17, 18, 16, 18, 19 });
            return Finish(v, t);
        }
        static Mesh Finish(List<Vector3> v, List<int> t)
        {
            var mesh = new Mesh(); mesh.SetVertices(v); mesh.SetUVs(0, v.Select(x => new Vector2(x.x, x.z)).ToList());
            mesh.SetTriangles(t, 0); return mesh;
        }
    }
}
