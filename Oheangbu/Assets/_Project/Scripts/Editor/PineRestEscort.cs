using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    public static partial class PineRestGameBuilder
    {
        static string EscortContractSite()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != Scene || scene.isDirty) throw new Exception("Saved Journey edit required");
            var session = Object.FindFirstObjectByType<PrologueSession>();
            if (GameObject.Find("Journey Merchant Branch") != null) return "Existing merchant branch retained";
            var appearance = GameObject.Find("Jeongdam appearance");
            if (appearance == null) throw new Exception("Existing humanoid visual required");
            var ground = Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Single(c => c.name == "Valley");
            Vector3 Ground(float x, float z)
            {
                if (!ground.Raycast(new Ray(new Vector3(x, 200, z), Vector3.down), out var h, 400)) throw new Exception("Merchant support absent");
                return h.point;
            }
            var point = Ground(13, 44);
            var root = new GameObject("Journey Merchant Branch").transform;
            var npc = Object.Instantiate(appearance, root);
            npc.name = "Wangso provisional merchant";
            npc.transform.position = Ground(13, 44);
            npc.transform.rotation = Quaternion.Euler(0, 210, 0);
            var body = npc.AddComponent<CapsuleCollider>(); body.center = Vector3.up * .875f; body.height = 1.75f; body.radius = .3f;
            Place("Merchant shelter", "Assets/HwaseongHaenggung/Prefabs/SM_Naeposa.prefab", root, Ground(20, 52), 4.5f);
            var shelter = root.Find("Merchant shelter");
            foreach (var filter in shelter.GetComponentsInChildren<MeshFilter>())
                if (filter.sharedMesh != null) filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            Place("Sealed cargo", "Assets/HwaseongHaenggung/Prefabs/SM_M_WoodenBox.prefab", root, Ground(15, 44), .85f);
            var cargo = root.Find("Sealed cargo");
            var rs = cargo.GetComponentsInChildren<Renderer>(); var bounds = rs[0].bounds;
            foreach (var r in rs.Skip(1)) bounds.Encapsulate(r.bounds);
            var collision = cargo.gameObject.AddComponent<BoxCollider>();
            collision.center = cargo.InverseTransformPoint(bounds.center);
            collision.size = new Vector3(bounds.size.x / cargo.lossyScale.x, bounds.size.y / cargo.lossyScale.y, bounds.size.z / cargo.lossyScale.z);
            // A blank paper band keeps the contents concealed without adding any UI lettering.
            var paper = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/Materials/PaleStone.mat");
            foreach (bool across in new[] { false, true })
            {
                var band = GameObject.CreatePrimitive(PrimitiveType.Cube); band.name = "Unbroken seal band";
                Object.DestroyImmediate(band.GetComponent<Collider>());
                band.transform.position = new Vector3(bounds.center.x, bounds.max.y + .009f, bounds.center.z);
                band.transform.localScale = across ? new Vector3(bounds.size.x * .95f, .014f, .09f) : new Vector3(.09f, .014f, bounds.size.z * .95f);
                band.transform.SetParent(cargo, true); band.GetComponent<Renderer>().sharedMaterial = paper;
            }
            session.EscortCompanion = npc.transform; session.EscortCargo = cargo;
            session.Content.Points = session.Content.Points.Concat(new[] { new PrologueContentSO.Point
            {
                Id = "wangso_w1", Kind = PrologueInteractionKind.Conversation, Position = point, Radius = 2.5f,
                Prompt = "왕소와 이야기", Text = "봉인을 열지 말고 황경 객주 본점까지 옮겨 주시오.",
                RequiredCompleted = new[] { "j1:accepted" }
            }}).ToArray();
            Physics.SyncTransforms();
            var surface = Object.FindFirstObjectByType<Unity.AI.Navigation.NavMeshSurface>();
            var previous = surface.navMeshData; surface.BuildNavMesh(); var next = surface.navMeshData;
            if (next == null) throw new Exception("Merchant navigation bake failed");
            surface.RemoveData(); EditorUtility.CopySerialized(next, previous); surface.navMeshData = previous; surface.AddData();
            EditorUtility.SetDirty(previous); Object.DestroyImmediate(next);
            EditorUtility.SetDirty(session); EditorUtility.SetDirty(session.Content);
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
            return "Merchant branch, provisional humanoid, sealed cargo and persistent W1 contract connected";
        }

        static string EscortContractReloadCheck()
        {
            var s = Object.FindFirstObjectByType<PrologueSession>();
            if (!EditorApplication.isPlaying || s?.Progress == null || !s.TestSaveSuffix.StartsWith("-audit-")) throw new Exception("Isolated Play required");
            bool pass = s.Progress.escort != null && s.Progress.escort.Stage == DemoEscortStage.Contracted && s.Progress.escort.Revision == 1 && s.Progress.completed.Contains("wangso_w1");
            string result = (pass ? "PASS " : "FAIL ") + "new Play session restores contracted W1 and revision 1; not an OS restart";
            File.WriteAllText("../Art/World/PineRest/escort_contract_reload.txt", result); return result;
        }

        static string EscortPromptAnchor()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != Scene || scene.isDirty) throw new Exception("Saved Journey required");
            var s = Object.FindFirstObjectByType<PrologueSession>();
            s.Content.Points.Single(p => p.Id == "wangso_w1").Position = s.EscortCompanion.position;
            EditorUtility.SetDirty(s.Content); AssetDatabase.SaveAssets(); return "W1 prompt anchored above companion";
        }

        static string EscortContractCheck()
        {
            var s = Object.FindFirstObjectByType<PrologueSession>();
            if (!EditorApplication.isPlaying || s?.Progress == null || !s.TestSaveSuffix.StartsWith("-audit-") || s.Progress.completed.Contains("j1:accepted")) throw new Exception("Fresh isolated Play required");
            var results = new List<string>(); void Check(bool pass, string label) => results.Add((pass ? "PASS " : "FAIL ") + label);
            var p = s.Content.Points.Single(p => p.Id == "wangso_w1");
            void Talk() { s.Teleport(p.Position + Vector3.up, 0); s.Interact(p.Id); }
            Talk(); Check(s.Progress.escort.Stage == DemoEscortStage.None, "W1 waits for J1 acceptance");
            var j1 = s.Content.Points.Single(p => p.Id == "jeongdam_j1"); s.Teleport(j1.Position + Vector3.up, 0); s.Interact(j1.Id);
            s.Interact(p.Id); Check(s.Progress.escort.Stage == DemoEscortStage.None, "remote interaction cannot accept contract");
            s.EscortCargo.gameObject.SetActive(false);
            try { Talk(); Check(s.Progress.escort.Stage == DemoEscortStage.None, "missing cargo blocks contract"); }
            finally { s.EscortCargo.gameObject.SetActive(true); }
            var originalPosition = s.EscortCompanion.position; s.EscortCompanion.position += Vector3.right * 50;
            try { Talk(); Check(s.Progress.escort.Stage == DemoEscortStage.None, "distant companion blocks contract"); }
            finally { s.EscortCompanion.position = originalPosition; }
            var field = typeof(PrologueSession).GetField("store", BindingFlags.NonPublic | BindingFlags.Instance); var originalStore = field.GetValue(s);
            string blocker = Path.GetFullPath("../Art/World/PineRest/escort_block_" + Guid.NewGuid().ToString("N")); File.WriteAllText(blocker, "audit only");
            try
            {
                field.SetValue(s, new PrologueProgressStore(Path.Combine(blocker, "state.json"))); Talk();
                Check(s.Progress.escort.Stage == DemoEscortStage.None && !s.Progress.completed.Contains(p.Id), "IOException publishes neither escort nor quest acceptance");
            }
            finally { field.SetValue(s, originalStore); File.Delete(blocker); }
            int balance = s.Progress.currency;
            Talk(); Check(s.Progress.escort.Stage == DemoEscortStage.Contracted && s.Progress.completed.Contains(p.Id) && !s.Progress.defeated.Contains("cheongryong"), "contract accepted before boss defeat with shared escort rules");
            long revision = s.Progress.escort.Revision; Talk();
            Check(s.Progress.escort.Revision == revision && s.Progress.currency == balance, "repeat conversation adds neither revision nor currency");
            var disk = new PrologueProgressStore(Path.Combine(Application.persistentDataPath, s.Content.SaveSlot + s.TestSaveSuffix + ".json")).Load();
            Check(disk != null && disk.escort.Stage == DemoEscortStage.Contracted && disk.completed.Contains(p.Id), "contract and quest marker persisted together");
            Check(disk != null && disk.escort.CompanionMode == DemoEscortCompanionMode.Waiting && !disk.escort.HasCheckpoint, "contract does not fabricate departure or delivery");
            string legacyPath = Path.GetFullPath("../Art/World/PineRest/escort_legacy_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var repository = new PrologueProgressStore(legacyPath);
                File.WriteAllText(legacyPath, "{\"version\":1,\"hp\":1,\"ink\":1,\"completed\":[],\"defeated\":[]}");
                var legacy = repository.Load(); Check(legacy != null && legacy.escort != null && legacy.escort.Stage == DemoEscortStage.None, "pre-escort save migrates without false contract");
                repository.Save(disk); var invalid = JsonUtility.FromJson<PrologueProgress>(JsonUtility.ToJson(disk));
                invalid.escort.Stage = DemoEscortStage.Delivered; invalid.escort.DeliveryRewardRecorded = false;
                repository.Save(invalid); var recovered = repository.Load();
                Check(recovered != null && recovered.escort.Stage == DemoEscortStage.Contracted, "invalid escort state falls back to prior valid atomic save");
            }
            finally { foreach (string suffix in new[] { "", ".bak", ".tmp" }) if (File.Exists(legacyPath + suffix)) File.Delete(legacyPath + suffix); }
            var path = new NavMeshPath();
            bool route = NavMesh.SamplePosition(j1.Position, out var a, 3, NavMesh.AllAreas) && NavMesh.SamplePosition(p.Position, out var b, 3, NavMesh.AllAreas) && NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            Check(route, "relay to merchant navigation path exists");
            results.Add("Direct session interaction and actual IOException; not manual F input, walking, vehicle transport or final NPC art approval.");
            string report = string.Join("\n", results); File.WriteAllText("../Art/World/PineRest/escort_contract_checks.txt", report); return report;
        }
    }
}
