using System;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App.Demo;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools
{
    public static class DemoCheongryongColliderSyncChecks
    {
        [Serializable] public sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public string[] unverified = { "Runtime global physics-flush CPU cost at 120fps",
                "Real imported dragon collider coverage and input timing", "Other scripts moving colliders after order 12000" };
        }

        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run collider synchronization checks in Edit mode.");
            var report = new Report(); void Check(bool pass, string name) => (pass ? report.passed : report.failed).Add(name);
            Scene scene = EditorSceneManager.NewPreviewScene();
            FieldInfo frameGuard = typeof(CheongryongColliderPoseSync).GetField("_lastGlobalSyncFrame", BindingFlags.Static | BindingFlags.NonPublic);
            int savedGuard = (int)frameGuard.GetValue(null); bool automatic = Physics.autoSyncTransforms;
            // Editor preview tests do not advance Time.frameCount. Reset only the batching guard
            // between simulated frames, while using actual colliders and actual physics queries.
            void NextFrame() => frameGuard.SetValue(null, int.MinValue);
            try
            {
                Check(!automatic, "Project automatic physics transform sync remains disabled");
                var owner = new GameObject("CheongryongSync_Isolated"); SceneManager.MoveGameObjectToScene(owner, scene);
                var bone = new GameObject("Head").transform; bone.SetParent(owner.transform, false); bone.localPosition = Vector3.up;
                var damage = new GameObject("Damage_Head").transform; damage.SetParent(bone, false);
                var collider = damage.gameObject.AddComponent<BoxCollider>(); collider.size = Vector3.one;
                var sync = owner.AddComponent<CheongryongColliderPoseSync>();
                Check(sync.Configure(new Collider[] { collider }), "Damage colliders belonging to this actor configure");
                NextFrame(); Check(sync.SynchronizeIfChanged(), "First active pose is explicitly synchronized");
                PhysicsScene physics = scene.GetPhysicsScene();
                bool Hit(Vector3 point, Collider expected) => physics.Raycast(point + Vector3.up * 3, Vector3.down, out RaycastHit hit, 5, ~0, QueryTriggerInteraction.Ignore) && hit.collider == expected;
                Check(Hit(Vector3.zero, collider) && Mathf.Abs(collider.bounds.center.x) < .0001f, "Initial world bounds and raycast match the authored collider");
                int count = sync.SynchronizationCount;
                for (int i = 0; i < 20; i++) { NextFrame(); sync.SynchronizeIfChanged(); }
                Check(sync.SynchronizationCount == count, "Twenty frozen-pose frames perform no physics flush");
                bone.position = new Vector3(4, 1, 0);
                Check(!Hit(Vector3.right * 4, collider), "With automatic sync off, a bone move is not prematurely reported at its new physics location");
                NextFrame(); Check(sync.SynchronizeIfChanged(), "Changed final bone pose requests one synchronization");
                Check(Hit(Vector3.right * 4, collider) && !Hit(Vector3.zero, collider) && Mathf.Abs(collider.bounds.center.x - 4) < .0001f,
                    "Actual physics raycast and bounds follow the moved bone after synchronization");
                count = sync.SynchronizationCount;
                bone.position = new Vector3(6, 1, 0);
                Check(!sync.SynchronizeIfChanged() && sync.SynchronizationCount == count, "Same-frame later mutation cannot cause a second global flush");
                NextFrame(); Check(sync.SynchronizeIfChanged() && Hit(Vector3.right * 6, collider), "A mutation deferred by the frame guard remains dirty and is flushed next frame");
                collider.enabled = false; bone.position += Vector3.right;
                NextFrame(); count = sync.SynchronizationCount;
                Check(!sync.SynchronizeIfChanged() && sync.SynchronizationCount == count, "No active tracked colliders means no synchronization");
                collider.enabled = true; NextFrame();
                Check(sync.SynchronizeIfChanged() && Hit(Vector3.right * 7, collider), "Reactivated collider gets a fresh synchronized pose");
                sync.enabled = false; bone.position += Vector3.right; NextFrame(); count = sync.SynchronizationCount;
                Check(!sync.SynchronizeIfChanged() && sync.SynchronizationCount == count, "Disabled component never flushes physics");
                sync.enabled = true;
                typeof(CheongryongColliderPoseSync).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(sync, null);
                NextFrame(); Check(sync.SynchronizeIfChanged(), "Explicit re-enable lifecycle refreshes its physics snapshot");

                var other = new GameObject("CheongryongSync_Other"); SceneManager.MoveGameObjectToScene(other, scene); other.transform.position = Vector3.right * 15;
                var otherCollider = other.AddComponent<BoxCollider>(); var otherSync = other.AddComponent<CheongryongColliderPoseSync>();
                Check(otherSync.Configure(new Collider[] { otherCollider }), "Second independent dragon sync owner configures");
                bone.position += Vector3.right; NextFrame(); int secondCount = otherSync.SynchronizationCount;
                Check(sync.SynchronizeIfChanged() && !otherSync.SynchronizeIfChanged() && otherSync.SynchronizationCount == secondCount,
                    "Multiple owners share a maximum of one global flush per frame");
                Check(Hit(Vector3.right * 15, otherCollider), "The batched global flush updates the second actual collider too");
                Check(!sync.Configure(new Collider[] { otherCollider }), "An unrelated actor collider cannot be tracked by this dragon");
                Check(Physics.autoSyncTransforms == automatic, "Checks and component never change the global automatic-sync setting");
            }
            catch (Exception e) { report.failed.Add(e.ToString()); }
            finally { frameGuard.SetValue(null, savedGuard); EditorSceneManager.ClosePreviewScene(scene); }
            report.status = report.failed.Count == 0 ? "PASS" : "FAIL"; return JsonUtility.ToJson(report, true);
        }
    }
}
