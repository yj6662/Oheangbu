using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>Empirical native collision capacity, independent of the character or acceptance thresholds.</summary>
    public static class DosaV2ClothCapacityProbe
    {
        [Serializable] private sealed class Particle
        { public int physicalCapsule, arraySlot; public Vector3 before, after; public float travel, radialDistance; public bool collisionResponded; }
        [Serializable] private sealed class Condition
        { public int requestedCapsules, getterCapsules, frames; public bool reversed; public int responding; public List<Particle> centerParticles = new List<Particle>(); }
        [Serializable] private sealed class Report
        {
            public string status, error, unityVersion, utc, path;
            public bool restored;
            public List<Condition> conditions = new List<Condition>();
            public string method = "32 independent flat 9x9 patches, one static identity skin, no gravity, each initially inside its own radius0.1m capsule. No pins or gameplay collisions. Measure actual native center-particle escape after30 real Play frames. None/1/16/17/32/32-reversed bound collider arrays distinguish physical response from the property getter. No character assets, Time or global physics changes.";
        }
        private static Session _active;
        private static Report _last;
        public static string Begin()
        {
            if (_active != null) return Status();
            if (!Application.isPlaying || EditorApplication.isPaused || DosaV2ClothDiagnostics.IsRunning || DosaV2LodValidation.IsRunning)
                return "WAIT: idle unpaused Play required.";
            _active = new Session();
            try { _active.Start(); } catch (Exception e) { _active.Finish("FAIL", e.ToString()); }
            return Status();
        }
        public static string Status()
        {
            var r = _active?.Result ?? _last;
            return r == null ? "NOT_STARTED" : JsonUtility.ToJson(r, true);
        }
        public static string Stop() { _active?.Finish("STOPPED", null); return Status(); }
        private sealed class Session
        {
            public readonly Report Result = new Report();
            private Scene _scene;
            private GameObject _fixture;
            private Mesh _mesh;
            private Cloth _cloth;
            private Vector3[] _before;
            private int[] _centerIndices;
            private int _index, _spawn, _lastFrame;
            private readonly int[] _counts = { 0, 1, 16, 17, 32, 32 };
            private bool _finished;
            public void Start()
            {
                Result.status = "RUNNING"; Result.utc = DateTimeOffset.UtcNow.ToString("O"); Result.unityVersion = Application.unityVersion;
                Result.path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2/cloth-capacity-probe.json"));
                _scene = SceneManager.CreateScene("DosaV2NativeCapacityProbe");
                EditorApplication.update += Update;
                EditorApplication.playModeStateChanged += PlayChanged;
                AssemblyReloadEvents.beforeAssemblyReload += Reload;
                Spawn();
            }
            private void Spawn()
            {
                _fixture = new GameObject("Disposable_CapsuleCapacity_" + _index); SceneManager.MoveGameObjectToScene(_fixture, _scene);
                _fixture.transform.position = new Vector3(50, 0, 50);
                var vertices = new List<Vector3>(); var triangles = new List<int>();
                const int side = 9;
                for (int c = 0; c < 32; c++)
                {
                    int offset = vertices.Count;
                    for (int y = 0; y < side; y++) for (int x = 0; x < side; x++)
                        vertices.Add(new Vector3(c * .6f + (x - 4) * .0125f, (y - 4) * .0125f, .01f));
                    for (int y = 0; y < side - 1; y++) for (int x = 0; x < side - 1; x++)
                    {
                        int a = offset + y * side + x, b = a + 1, d = a + side, e = d + 1;
                        triangles.AddRange(new[] { a, b, e, a, e, d });
                    }
                }
                _mesh = new Mesh { name = "CapacityOnly_UnpinnedPatches" };
                _mesh.SetVertices(vertices); _mesh.SetTriangles(triangles, 0);
                _mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, vertices.Count).ToArray();
                _mesh.bindposes = new[] { Matrix4x4.identity }; _mesh.RecalculateNormals(); _mesh.RecalculateBounds();
                var renderer = _fixture.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = _mesh;
                renderer.bones = new[] { _fixture.transform }; renderer.rootBone = _fixture.transform; renderer.updateWhenOffscreen = true;
                _cloth = _fixture.AddComponent<Cloth>(); _cloth.useGravity = false; _cloth.externalAcceleration = Vector3.zero;
                _cloth.randomAcceleration = Vector3.zero; _cloth.clothSolverFrequency = 120f;
                _cloth.stretchingStiffness = 1f; _cloth.bendingStiffness = .1f; _cloth.damping = .5f;
                _cloth.useTethers = false; _cloth.enableContinuousCollision = true;
                _cloth.worldVelocityScale = 0; _cloth.worldAccelerationScale = 0;
                _before = _cloth.vertices;
                if (_before.Length != vertices.Count) throw new InvalidOperationException("Native probe vertex count changed; no assumed mapping.");
                _centerIndices = new int[32];
                for (int c = 0; c < 32; c++)
                {
                    Vector3 center = new Vector3(c * .6f, 0, .01f);
                    int best = -1; float distance = float.MaxValue;
                    for (int i = 0; i < _before.Length; i++) if ((_before[i] - center).sqrMagnitude < distance)
                    { best = i; distance = (_before[i] - center).sqrMagnitude; }
                    if (best < 0 || distance > 1e-10f) throw new InvalidOperationException("Native probe rest frame was not identity local.");
                    _centerIndices[c] = best;
                }
                _cloth.coefficients = Enumerable.Repeat(new ClothSkinningCoefficient { maxDistance = .5f, collisionSphereDistance = 0 }, _before.Length).ToArray();
                var capsules = new List<CapsuleCollider>();
                for (int i = 0; i < _counts[_index]; i++)
                {
                    int physical = _index == 5 ? 31 - i : i;
                    var go = new GameObject("CapacityCapsule_" + physical); go.transform.SetParent(_fixture.transform, false); go.layer = 2;
                    var capsule = go.AddComponent<CapsuleCollider>(); capsule.direction = 1; capsule.radius = .1f; capsule.height = .3f;
                    capsule.center = new Vector3(physical * .6f, 0, 0); capsule.isTrigger = true; capsule.excludeLayers = ~0;
                    capsules.Add(capsule);
                }
                _cloth.capsuleColliders = capsules.ToArray(); _cloth.ClearTransformMotion();
                _spawn = Time.frameCount; _lastFrame = _spawn;
            }
            private void Update()
            {
                if (_finished || Time.frameCount == _lastFrame) return;
                _lastFrame = Time.frameCount;
                if (Time.frameCount - _spawn < 30) return;
                try
                {
                    var after = _cloth.vertices;
                    if (after.Length != _before.Length) throw new InvalidOperationException("Native probe changed particle order/count after simulation.");
                    var condition = new Condition { requestedCapsules = _counts[_index], getterCapsules = _cloth.capsuleColliders.Length,
                        reversed = _index == 5, frames = Time.frameCount - _spawn };
                    for (int c = 0; c < 32; c++)
                    {
                        Vector3 p = after[_centerIndices[c]], prior = _before[_centerIndices[c]];
                        if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z)) throw new InvalidOperationException("Nonfinite native probe particle.");
                        float radial = new Vector2(p.x - c * .6f, p.z).magnitude;
                        bool responded = radial >= .095f;
                        condition.centerParticles.Add(new Particle { physicalCapsule = c, arraySlot = _index == 5 ? 31 - c : c,
                            before = prior, after = p, travel = (p - prior).magnitude, radialDistance = radial, collisionResponded = responded });
                        if (responded) condition.responding++;
                    }
                    Result.conditions.Add(condition); Save();
                    Object.DestroyImmediate(_fixture); Object.DestroyImmediate(_mesh); _fixture = null; _mesh = null;
                    _index++; if (_index == _counts.Length) Finish("COMPLETE_MEASURED", null); else Spawn();
                }
                catch (Exception e) { Finish("FAIL", e.ToString()); }
            }
            private void PlayChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) Finish("STOPPED", "Play exit"); }
            private void Reload() { Finish("STOPPED", "Assembly reload"); }
            public void Finish(string status, string error)
            {
                if (_finished) return; _finished = true; Result.status = status; Result.error = error;
                EditorApplication.update -= Update; EditorApplication.playModeStateChanged -= PlayChanged; AssemblyReloadEvents.beforeAssemblyReload -= Reload;
                if (_fixture != null) Object.DestroyImmediate(_fixture); if (_mesh != null) Object.DestroyImmediate(_mesh);
                if (_scene.IsValid() && _scene.isLoaded)
                {
                    var operation = SceneManager.UnloadSceneAsync(_scene);
                    if (operation != null) { Result.status = "RESTORING"; Save(); operation.completed += _ => Complete(status); return; }
                }
                Complete(status);
            }
            private void Complete(string status) { Result.status = status; Result.restored = true; _last = Result; _active = null; Save(); }
            private void Save() { Directory.CreateDirectory(Path.GetDirectoryName(Result.path)); File.WriteAllText(Result.path, JsonUtility.ToJson(Result, true)); }
        }
    }
}
