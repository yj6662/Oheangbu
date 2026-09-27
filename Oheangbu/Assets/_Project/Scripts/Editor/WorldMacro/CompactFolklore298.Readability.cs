using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactFolklore298
    {
        [Serializable] sealed class ReadabilityRow298 { public string id, prefab; public int renderers; public float diffuseFill; public bool materialsUnchanged, boundsUnchanged; }
        [Serializable] sealed class ReadabilityReport298
        {
            public string utc, status, scope;
            public float ambientIntensity, reflectionIntensity;
            public bool globalsUnchanged;
            public ReadabilityRow298[] prefabs, sceneActors;
        }
        static ReadabilityRow298 SetReadability298(GameObject root, string id)
        {
            var targets = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Cast<Renderer>().ToArray();
            if (targets.Length == 0) throw new Exception(id + " has no body skin");
            if (targets.Any(r => r.sharedMaterials.Any(m => m == null || m.shader.name != "Universal Render Pipeline/Lit"))) throw new Exception(id + " expected preserved URP Lit PBR materials");
            var materials = targets.Select(r => r.sharedMaterials).ToArray();
            var bounds = targets.Select(r => ((SkinnedMeshRenderer)r).localBounds).ToArray();
            var component = root.GetComponent<FolkloreReadability298>();
            if (component == null)
            {
                var prior = targets.Select(r => r.lightProbeUsage).ToArray();
                component = root.AddComponent<FolkloreReadability298>(); component.RestoreUsage = prior;
            }
            component.Targets = targets; component.DiffuseFill = .30f; component.Apply();
            if (component.AppliedRenderers != targets.Length || component.LastError != null) throw new Exception(id + " local SH apply failed: " + component.LastError);
            var result = new ReadabilityRow298 { id = id, prefab = PrefabPath(id), renderers = targets.Length, diffuseFill = component.DiffuseFill,
                materialsUnchanged = targets.Select((r,i) => r.sharedMaterials.SequenceEqual(materials[i])).All(b=>b),
                boundsUnchanged = targets.Select((r,i) => ((SkinnedMeshRenderer)r).localBounds == bounds[i]).All(b=>b) };
            if (!result.materialsUnchanged || !result.boundsUnchanged) throw new Exception(id + " material or culling bounds changed");
            EditorUtility.SetDirty(component); return result;
        }
        public static string ApplyReadability298()
        {
            RequireEdit(); var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || scene.isDirty) throw new Exception("Saved298 candidate required for readability authoring");
            var s = Session(); var rows = ReadManifest().rows;
            if (s.Content.SaveSlot != "world-folklore-298" || s.Actors.Length != 15) throw new Exception("Built private298 candidate required");
            float ambient = RenderSettings.ambientIntensity, reflection = RenderSettings.reflectionIntensity;
            var ambientMode = RenderSettings.ambientMode; var sky = RenderSettings.ambientSkyColor; var equator = RenderSettings.ambientEquatorColor; var ground = RenderSettings.ambientGroundColor;
            var prefabs = new List<ReadabilityRow298>(); var actors = new List<ReadabilityRow298>();
            foreach (var row in rows)
            {
                var path = PrefabPath(row.id); var root = PrefabUtility.LoadPrefabContents(path);
                try { prefabs.Add(SetReadability298(root,row.id)); PrefabUtility.SaveAsPrefabAsset(root,path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            foreach (var row in rows)
            {
                string actorId = row.id == "cheongryong" || row.id == "south_gate_general" ? row.id : "folklore298/" + row.id;
                var actor = s.Actors.Single(a => a.Id == actorId);
                var visual = actor.transform.Find("Folklore298_Visual");
                if (visual == null) throw new Exception(actorId + " generated visual missing");
                actors.Add(SetReadability298(visual.gameObject,row.id));
            }
            bool globalsSame = ambient == RenderSettings.ambientIntensity && reflection == RenderSettings.reflectionIntensity && ambientMode == RenderSettings.ambientMode && sky == RenderSettings.ambientSkyColor && equator == RenderSettings.ambientEquatorColor && ground == RenderSettings.ambientGroundColor;
            if (!globalsSame) throw new Exception("Unexpected global lighting mutation");
            EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene,ScenePath)) throw new Exception("Candidate readability scene save failed");
            string directory=Path.Combine(OutputRoot,"Analysis");Directory.CreateDirectory(directory);
            string output=Path.Combine(directory,"readability.json");
            File.WriteAllText(output,JsonUtility.ToJson(new ReadabilityReport298 {utc=DateTime.UtcNow.ToString("O"),status="APPLIED_VISUAL_AND_PERF_REVIEW_REQUIRED",scope="Eight298 body skins only. Existing probe plus0.30 linear diffuse fill via CustomProvided SH/MPB. URP Lit textures/PBR/materials, geometry, bounds, global ambient/reflection and other actors unchanged. MPB excludes these renderers from SRP Batcher; no new lights or emission. Pure-metal indirect reflection remains governed by existing scene reflection.",ambientIntensity=ambient,reflectionIntensity=reflection,globalsUnchanged=globalsSame,prefabs=prefabs.ToArray(),sceneActors=actors.ToArray()},true));
            return output + " candidate skins=" + actors.Sum(r=>r.renderers) + " globals/materials/bounds unchanged; actual shadow-view QA required";
        }
    }
}
