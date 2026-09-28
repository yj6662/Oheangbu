using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.World;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactFolklore298
    {
        [Serializable] public sealed class BoundsFit298
        { public string id, utc; public int sampledPoses; public float[] localRadii; public string scope; }
        public static string FitBounds(string argument)
        {
            RequireEdit(); var results = new List<string>();
            foreach (var row in ReadManifest().rows.Where(r => argument == "all" || r.id == argument))
            {
                var path = PrefabPath(row.id); var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var animator = root.GetComponentInChildren<Animator>(true);
                    var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    var transforms = root.GetComponentsInChildren<Transform>(true);
                    var positions = transforms.Select(t => t.localPosition).ToArray();
                    var rotations = transforms.Select(t => t.localRotation).ToArray();
                    var scales = transforms.Select(t => t.localScale).ToArray();
                    void Restore() { for (int i = 0; i < transforms.Length; i++) { transforms[i].localPosition = positions[i]; transforms[i].localRotation = rotations[i]; transforms[i].localScale = scales[i]; } }
                    var radii = new float[skins.Length]; int samples = 0;
                    void Measure()
                    {
                        for (int i = 0; i < skins.Length; i++)
                        {
                            var skin = skins[i]; var anchor = skin.rootBone != null ? skin.rootBone : skin.transform;
                            foreach (var p in PhysicalSkinVertices298(skin))
                            {
                                // Account for both renderer and imported root-bone
                                // reference frames. A sphere remains valid as that
                                // bone rotates at clip transitions.
                                radii[i] = Mathf.Max(radii[i], Mathf.Max(anchor.InverseTransformPoint(p).magnitude, skin.transform.InverseTransformPoint(p).magnitude));
                            }
                        }
                        samples++;
                    }
                    Measure();
                    foreach (var role in row.clips)
                    {
                        var clip = Clip(row, role.role);
                        foreach (var phase in Enumerable.Range(0, 33).Select(i => i / 32f).Concat(new[] { role.peak01 }).Distinct())
                        { Restore(); SamplePoseGraph298(animator, clip, phase * clip.length); Measure(); }
                    }
                    Restore(); SamplePoseGraph298(animator, Clip(row, "idle"), 0);
                    var settings = root.GetComponent<FolkloreCullingBounds298>() ?? root.AddComponent<FolkloreCullingBounds298>();
                    settings.Entries = skins.Select((skin, i) =>
                    {
                        var anchor = skin.rootBone != null ? skin.rootBone : skin.transform;
                        float minScale = Mathf.Max(.0001f, Mathf.Min(Mathf.Abs(anchor.lossyScale.x), Mathf.Abs(anchor.lossyScale.y), Mathf.Abs(anchor.lossyScale.z), Mathf.Abs(skin.transform.lossyScale.x), Mathf.Abs(skin.transform.lossyScale.y), Mathf.Abs(skin.transform.lossyScale.z)));
                        radii[i] += .2f / minScale;
                        if (row.id == "imugi" || row.id == "cheongryong") radii[i] = Mathf.Max(radii[i], row.targetLength * 1.1f / minScale);
                        if (!float.IsFinite(radii[i]) || radii[i] <= 0) throw new Exception("Invalid measured culling envelope");
                        return new FolkloreCullingBounds298.Entry { Skin = skin, LocalBounds = new Bounds(Vector3.zero, Vector3.one * (2 * radii[i])) };
                    }).ToArray();
                    settings.Apply(); PrefabUtility.SaveAsPrefabAsset(root, path);
                    Directory.CreateDirectory(Path.Combine(OutputRoot, "Analysis"));
                    File.WriteAllText(Path.Combine(OutputRoot, "Analysis", "bounds-" + row.id + ".json"), JsonUtility.ToJson(new BoundsFit298 { id = row.id, utc = DateTime.UtcNow.ToString("O"), sampledPoses = samples, localRadii = radii, scope = "Conservative root-relative sphere from 33 samples per role plus contact phase and 0.2m margin. Serpents additionally reserve full body length for procedural follow. Runtime serialized OnEnable reapplies the bounds; geometry, skinning and distance LOD are unchanged." }, true));
                    results.Add(row.id + ": " + samples + " poses");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            if (results.Count == 0) throw new Exception("No matching model");
            AssetDatabase.SaveAssets(); return string.Join("\n", results);
        }
    }
}
