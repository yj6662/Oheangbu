using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // A scene-only restoration. The C2 prefab, materials, meshes and source scene are read-only.
    public static class WorldMacroOriginalInn
    {
        public const string RootName = "Playtest_OriginalC2Inn";
        public const string SourcePrefab = "Assets/_Project/Art/CodexWorld/ThatchedInn/ThatchedInn.prefab";
        const string SourceScene = "Assets/_Project/Scenes/Dev/C2_CodexWorld.unity";
        static string Output => WorldMacroPlaytestAuthoring.Output + "/NaturalCave";
        static WorldMacroPlaytestSession Session => Object.FindFirstObjectByType<WorldMacroPlaytestSession>();

        public static string Execute(string command)
        {
            Require();
            if (command == "inspect") return Inspect();
            if (command == "apply") return Apply();
            if (command == "validate") return Validate();
            throw new ArgumentException(command);
        }

        static void Require()
        {
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().path != WorldMacroPlaytestAuthoring.ScenePath)
                throw new InvalidOperationException("Original inn restoration requires the playtest scene in Edit mode.");
            if (Session == null || Session.Content == null) throw new InvalidOperationException("Playtest content is missing.");
            Directory.CreateDirectory(Output);
        }

        public static string Inspect()
        {
            Require();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefab);
            if (source == null) throw new InvalidOperationException("Original C2 inn prefab is missing.");
            var lines = new List<string> { "Source scene: " + SourceScene, "Source prefab: " + SourcePrefab,
                "Original model: Assets/House_1/house.fbx", "Prefab scale: " + source.transform.localScale.ToString("F3") };
            foreach (var f in source.GetComponentsInChildren<MeshFilter>(true))
                lines.Add(f.name + " | " + AssetDatabase.GetAssetPath(f.sharedMesh) + " | " + (f.sharedMesh == null ? "MISSING" : f.sharedMesh.bounds.ToString("F3")));
            var floor = GameObject.Find("Inn").transform.Find("Playtest_Inn_Floor").GetComponent<Renderer>().bounds;
            lines.Add("Old inn floor " + floor.ToString("F3"));
            foreach (string id in new[] { "geumpyo_inn", "logger", "herbalist" })
                lines.Add("Preserved point " + id + " " + Session.Content.Points.First(p => p.Id == id).Position.ToString("F3"));
            lines.Add("Checkpoint " + Session.Content.InnCheckpointFeet.ToString("F3"));
            File.WriteAllLines(Output + "/inn_source_inspection.txt", lines);
            return string.Join("\n", lines);
        }

        public static string Apply()
        {
            Require();
            if (GameObject.Find(RootName) != null) throw new InvalidOperationException("Original C2 inn already restored; use targeted adjustments.");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefab);
            if (source == null) throw new InvalidOperationException("Original C2 inn prefab is missing.");
            var old = GameObject.Find("Playtest_OwnedAssets")?.transform.Find("Geumpyo_ThatchedInn");
            if (old == null || !old.gameObject.activeSelf) throw new InvalidOperationException("Expected active House_2 inn is missing.");
            var legacy = GameObject.Find("Inn").transform;
            var oldFloor = legacy.Find("Playtest_Inn_Floor");
            var oldFloorBounds = oldFloor.GetComponent<Renderer>().bounds;
            var snapshot = EditorJsonUtility.ToJson(Session.Content, true);
            File.WriteAllText(Output + "/inn_content_before.json", snapshot);

            var root = new GameObject(RootName).transform;
            var building = (GameObject)PrefabUtility.InstantiatePrefab(source, SceneManager.GetActiveScene());
            building.name = "Thatched_Inn_C2_Original";
            building.transform.SetParent(root, true);
            // In C2 the courtyard opens towards local +Z. Here the approach is from world -Z.
            building.transform.rotation = Quaternion.Euler(0, 180, 0);
            building.transform.localScale = source.transform.localScale;
            var checkpoint = Session.Content.InnCheckpointFeet;
            building.transform.position = new Vector3(checkpoint.x - 1.2f, oldFloorBounds.max.y - .12f, checkpoint.z + 7.7f);

            // Existing interaction points stay in the forecourt. Preserve the actual source-built table.
            CloneSourcePart(old.Find("Rest_Low_Table"), root, "Rest_Low_Table");
            var deck = new GameObject("Forecourt_Maru_FromExistingAsset").transform;
            deck.SetParent(root, false);
            for (int x = 2; x <= 5; x++)
                for (int z = 0; z <= 2; z++) CloneSourcePart(old.Find("Floor_" + x + "_" + z), deck, "Maru_" + x + "_" + z);
            var deckBounds = BoundsOf(deck.gameObject);
            var support = deck.gameObject.AddComponent<BoxCollider>();
            support.center = deck.InverseTransformPoint(deckBounds.center);
            support.size = deckBounds.size;
            // Narrow the former broad platform to the source maru footprint; its old entrance ramp remains usable.
            foreach (var r in oldFloor.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var c in oldFloor.GetComponentsInChildren<Collider>(true)) c.enabled = false;

            // The old source surfaces include their own material batches. Archive the entire building only,
            // never the sibling cave/transport assets or their batched renderers.
            old.gameObject.SetActive(false);
            Physics.SyncTransforms();
            if (EditorJsonUtility.ToJson(Session.Content, true) != snapshot) throw new InvalidOperationException("Inn content changed unexpectedly.");
            File.WriteAllLines(Output + "/inn_restoration.txt", new[] {
                "C2 source scene preserved: " + SourceScene,
                "C2 source prefab preserved: " + SourcePrefab,
                "C2 source model: Assets/House_1/house.fbx; whole L-shaped thatched house reused, not reconstructed from wall panels.",
                "Original 11 muted material groups, mesh colliders, 5 entry treads and 2 porch lanterns retained.",
                "House_2 Geumpyo_ThatchedInn archived inactive with its Material_Batches and colliders.",
                "Rest table and existing Seyeonjeong SM_Maru_2 source floor tiles reused in the forecourt.",
                "Inn/NPC/checkpoint IDs, positions, text, reward and rest rules unchanged.",
                "C2 placement " + building.transform.position.ToString("F3") + " scale " + building.transform.localScale.ToString("F3"),
                "Parent must save the scene after reviewing validation. No build or global regeneration was performed."
            });
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return Validate();
        }

        static Transform CloneSourcePart(Transform source, Transform parent, string name)
        {
            if (source == null) throw new InvalidOperationException("A retained source furnishing is missing: " + name);
            var clone = Object.Instantiate(source.gameObject, parent);
            clone.name = name;
            clone.transform.SetPositionAndRotation(source.position, source.rotation);
            clone.transform.localScale = source.lossyScale;
            // AssetReuse's batching disabled the source renderers; this clone is intentionally unbatched.
            foreach (var r in clone.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            clone.SetActive(true);
            return clone.transform;
        }

        static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
            if (renderers.Length == 0) throw new InvalidOperationException("No active source surfaces: " + root.name);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        public static string Validate()
        {
            Require();
            Physics.SyncTransforms();
            var root = GameObject.Find(RootName);
            if (root == null) return "FAIL Original C2 inn is not installed.";
            var building = root.transform.Find("Thatched_Inn_C2_Original");
            var lines = new List<string>();
            void Check(bool result, string label) => lines.Add((result ? "PASS " : "FAIL ") + label);
            var old = GameObject.Find("Playtest_OwnedAssets").transform.Find("Geumpyo_ThatchedInn");
            Check(!old.gameObject.activeSelf, "previous rectangular inn and its batches/colliders archived inactive");
            Check(PrefabUtility.GetCorrespondingObjectFromSource(building.gameObject) != null, "whole original C2 prefab instantiated");
            var renderers = building.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled).ToArray();
            Check(renderers.All(r => r.sharedMaterials.All(m => m != null && m.shader != null && !ShaderUtil.ShaderHasError(m.shader))), "C2 materials resolve without shader errors");
            Check(building.GetComponentsInChildren<Light>().Length == 2, "two original porch lanterns retained");
            Check(building.Cast<Transform>().Count(t => t.name.StartsWith("Inn_Entry_Step_")) == 5, "five original entry treads retained");
            Check(File.ReadAllText(Output + "/inn_content_before.json") == EditorJsonUtility.ToJson(Session.Content, true), "all gameplay content unchanged during inn restoration");
            Check(Session.TrySafeFeet(Session.Content.InnCheckpointFeet, out var safe), "unchanged checkpoint supports safe player restore (" + safe.ToString("F3") + ")");
            foreach (string id in new[] { "geumpyo_inn", "logger", "herbalist" })
            {
                var point = Session.Content.Points.First(p => p.Id == id);
                bool reachable = false;
                for (int i = 0; i < 24; i++)
                {
                    float angle = i * Mathf.PI / 12;
                    var candidate = point.Position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 1.4f;
                    if (Session.TrySafeFeet(candidate, out var feet) && !Physics.Linecast(feet + Vector3.up * 1.3f, point.Position + Vector3.up * .8f, 1, QueryTriggerInteraction.Ignore)) reachable = true;
                }
                Check(reachable, "clear capsule approach and line of sight for " + id);
            }
            // Sample the C2's intended entrance centreline, including the raised porch; do not fake a pass if
            // current terrain obstructs the entry. Parent can adjust only this copied scene instance.
            int entranceSamples = 0, entranceClear = 0;
            for (float z = 4.6f; z >= -1.9f; z -= .25f)
            {
                var candidate = building.TransformPoint(new Vector3(-1.5f, 1.0f, z));
                entranceSamples++;
                if (Session.TrySafeFeet(candidate, out _)) entranceClear++;
            }
            Check(entranceSamples == entranceClear, "C2 staircase/porch capsule samples " + entranceClear + "/" + entranceSamples);
            foreach (string child in new[] { "Inn_Entry_Step_0", "Inn_Entry_Step_4", "house_Re_Stone" })
            {
                var t = building.Find(child);
                if (t != null) lines.Add("INFO " + child + " " + BoundsOf(t.gameObject).ToString("F3"));
            }
            lines.Add("INFO original building bounds " + BoundsOf(building.gameObject).ToString("F3"));
            lines.Add("INFO renderers=" + root.GetComponentsInChildren<Renderer>().Count(r => r.enabled) + "; original building tris=" + renderers.Sum(r => (long)r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3));
            lines.Add("UNVERIFIED actual user walking, final visual acceptance and full-frame moving performance; no new build.");
            File.WriteAllLines(Output + "/inn_validation.txt", lines);
            return string.Join("\n", lines);
        }
    }
}
