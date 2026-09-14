using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools
{
    /// <summary>Read-only imported source-index evidence for Blender cloth authoring.</summary>
    public static class DosaV2ClothSourceSnapshot
    {
        [Serializable] private sealed class Source
        {
            public string renderer, guid;
            public long localId;
            public Vector3[] vertices;
            public int[] triangles;
            public Matrix4x4 rendererLocalToWorld;
            public Matrix4x4[] bindposes;
            public string[] bones;
            public byte[] counts;
            public Influence[] influences;
            public Vector2[] mobility;
        }
        [Serializable] private struct Influence { public int bone; public float weight; }
        [Serializable] private sealed class Report
        {
            public string status = "IMPORTED_SOURCE_ONLY", path, modelPath, modelSha256, utc;
            public List<Source> surfaces = new List<Source>();
        }
        public static string Export()
        {
            string path = DosaV2PlayerBuilder.WorldModel;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) return "WAIT: imported V2 source is missing.";
            var report = new Report { modelPath = path, utc = DateTimeOffset.UtcNow.ToString("O") };
            using (var sha = SHA256.Create()) report.modelSha256 = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.name != "DosaV2_Robe_Combined" && !renderer.name.StartsWith("DosaV2_SleeveOuter_", StringComparison.Ordinal)) continue;
                var mesh = renderer.sharedMesh;
                if (mesh == null || !mesh.isReadable) throw new InvalidOperationException("Readable source required.");
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long localId);
                var mobility = new List<Vector2>(); mesh.GetUVs(2, mobility);
                var row = new Source { renderer = renderer.name, guid = guid, localId = localId,
                    vertices = mesh.vertices, triangles = mesh.triangles, rendererLocalToWorld = renderer.localToWorldMatrix,
                    bindposes = mesh.bindposes, bones = renderer.bones.Select(b => b.name).ToArray(), mobility = mobility.ToArray() };
                using (var counts = mesh.GetBonesPerVertex()) row.counts = counts.ToArray();
                using (var weights = mesh.GetAllBoneWeights()) row.influences = weights.ToArray().Select(w => new Influence { bone = w.boneIndex, weight = w.weight }).ToArray();
                report.surfaces.Add(row);
            }
            if (report.surfaces.Count != 3) throw new InvalidOperationException("Expected the current three authored cloth surfaces.");
            report.path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2/cloth-source-" + report.modelSha256.Substring(0, 12) + ".json"));
            File.WriteAllText(report.path, JsonUtility.ToJson(report, true));
            return "IMPORTED_SOURCE_ONLY: " + report.path;
        }
    }
}
