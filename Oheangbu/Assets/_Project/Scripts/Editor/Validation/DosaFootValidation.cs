using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    public static class DosaFootValidation
    {
        [Serializable] private sealed class FootResult
        {
            public string foot;
            public int stanceSamples, stanceIntervals, groundRayMisses;
            public float maxStanceSlide, maxPenetration, maxContactSoleGap, minSoleHeight = float.MaxValue;
            public float medianSourceStanceSpeed;
            public int runtimeLockedStanceSamples;
            public float worstSlideSourceGap, worstSlideReachRatio;
            public bool worstSlideWasLocked, worstSlideWasContactTransfer;
            public bool worstGapWasLocked;
            public float worstGapReachRatio, worstGapSourceGap, worstGapPelvisLowering;
            public Vector3 worstGapAnchor, worstGapContact;
            public int sourceStanceEpisodes, sourceTwoFrameIntervals, singleFrameSourceEpisodes, contactRollTransfers;
            public float maxRollTransitionDisplacement;
            public int groundedRollTransitionChecks;
            public List<ContactSample> contactTrace;
        }
        [Serializable] private sealed class ContactSample
        {
            public int frame, contactVertex, sourceEpisodeFrames, materialContactFrames;
            public bool sourceStance, runtimeLocked, contactRolled;
            public float sourceMinimum, sourceBackwardSpeed, correctedMinimum;
        }
        [Serializable] private sealed class Scenario
        {
            public string gait, direction;
            public float actualSpeed, sourceRootClearance;
            public int frames, simulatedFps;
            public FootResult left = new FootResult { foot = "Left" };
            public FootResult right = new FootResult { foot = "Right" };
            public bool passed;
        }
        [Serializable] private sealed class Report
        {
            public string status;
            public string note = "Actual imported Animator and skinned-foot vertices on a temporary flat collider. Root moves along a normalized direction at the selected 1.6m/s walk-validation or 4.5m/s run speed; diagonals retain the same total speed as cardinal directions. Stance uses the uncorrected BAKED sole's low height and backward source foot travel, not the runtime lock flag or an ankle/centroid height (heel roll can lift those while toes contact). Track a material sole vertex from contact start; only replace it when it rolls >15mm above the lowest patch. Ray length is 5m so a normally lifted running foot does not falsely miss the floor. Limits remain 10cm stance displacement and 2cm skin penetration. No scene player or global clocks are changed.";
            public string inputEvidence = "BeginFull: 20260907_225832_620_full_visuals/frames.jsonl, frames 170/195. Actual diagonal input produced x/z components of magnitude 3.1819816m/s, total 4.5m/s. The prior unnormalized 6.364m/s fixture is preserved separately as foot-validation-diagonal-stress-6.364mps.json.";
            public string contactGapContract = "Also require the corrected sole to stay within 3cm of the actual collider during source-defined stance. Test 30/60/120 fps; do not pass slipping by lifting the foot. Source sole velocity is recorded separately from the actual skin contact. Runtime sole sensors do sparse CPU skinning, never BakeMesh.";
            public string intervalContract = "Require at least one source-defined stance episode lasting two sampled frames. stanceIntervals is retained as a diagnostic count of two-frame material-vertex segments; heel/toe contact transfer must not reset the physical episode. On a transfer, the incoming vertex is also compared with its previous corrected-frame position; when both are within the ground contact band, its XY displacement contributes to maxStanceSlide.";
            public float slideLimitMeters = .1f, penetrationLimitMeters = .02f;
            public float contactGapLimitMeters = .03f;
            public int passed, failed;
            public List<string> failures = new List<string>();
            public List<Scenario> scenarios = new List<Scenario>();
        }

        public static string Run() => RunAudit(false);
        public static string RunProblemCase() => RunAudit(true);

        private static string RunAudit(bool problemCaseOnly)
        {
            if (!Application.isPlaying) return "NOT_RUN: Play Mode required";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DosaPlayerBuilder.VisualPrefabPath);
            if (prefab == null) return "NOT_RUN: missing PF_DosaVisual";
            // 설치기는 CC 피부 여유만큼 시각 자식을 내린다. CC 바닥 .08m - 시각 보정 .08m = 평지 발바닥 0m.
            const float rootClearance = 0f;
            var report = new Report();
            var randomState = UnityEngine.Random.state;
            Vector2[] directions = { Vector2.up, Vector2.down, Vector2.left, Vector2.right,
                new Vector2(-1f,1f), new Vector2(1f,1f), new Vector2(-1f,-1f), new Vector2(1f,-1f) };
            string[] labels = { "Forward", "Back", "Left", "Right", "ForwardLeft", "ForwardRight", "BackLeft", "BackRight" };
            try
            {
                foreach (float speed in new[] { 1.6f, 4.5f })
                foreach (int fps in new[] { 30, 60, 120 })
                for (int direction = 0; direction < directions.Length; direction++)
                {
                    if (problemCaseOnly && (speed != 4.5f || fps != 30 || direction != 5)) continue;
                    Vector2 moveDirection = directions[direction].normalized;
                    Vector3 velocity = new Vector3(moveDirection.x, 0f, moveDirection.y) * speed;
                    var scenario = new Scenario { gait = speed < 2f ? "Walk" : "Run", direction = labels[direction],
                        actualSpeed = velocity.magnitude, sourceRootClearance = rootClearance, simulatedFps = fps };
                    report.scenarios.Add(scenario);
                    if (problemCaseOnly)
                    {
                        scenario.left.contactTrace = new List<ContactSample>();
                        scenario.right.contactTrace = new List<ContactSample>();
                    }
                    try
                    {
                        using (var f = new Fixture(prefab, rootClearance))
                        {
                            float dt = 1f / fps;
                            int warmup = fps / 2;
                            for (int frame = 0; frame < warmup + fps * 2; frame++)
                            {
                                f.Step(velocity, dt, frame >= warmup, scenario);
                                if (frame >= warmup) scenario.frames++;
                            }
                            f.Complete(scenario);
                        }
                        scenario.passed = Valid(scenario.left, report, scenario) & Valid(scenario.right, report, scenario);
                    }
                    catch (Exception ex)
                    {
                        report.failures.Add(scenario.gait + scenario.direction + ": " + ex.GetBaseException().Message);
                    }
                    if (scenario.passed) report.passed++; else report.failed++;
                }
            }
            finally { UnityEngine.Random.state = randomState; }
            report.status = report.failed == 0 ? "PASS" : "FAIL";
            string directory = Path.GetFullPath("Screenshots/PlayerDosa"); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, problemCaseOnly ? "foot-short-stance-diagnostic.json" : "foot-validation.json");
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            float maxSlide = report.scenarios.Count > 0 ? report.scenarios.Max(s => Mathf.Max(s.left.maxStanceSlide, s.right.maxStanceSlide)) : 0f;
            float maxPenetration = report.scenarios.Count > 0 ? report.scenarios.Max(s => Mathf.Max(s.left.maxPenetration, s.right.maxPenetration)) : 0f;
            float maxGap = report.scenarios.Count > 0 ? report.scenarios.Max(s => Mathf.Max(s.left.maxContactSoleGap, s.right.maxContactSoleGap)) : 0f;
            string episodes = problemCaseOnly && report.scenarios.Count > 0
                ? $"; Right source episodes={report.scenarios[0].right.sourceStanceEpisodes}, source two-frame intervals={report.scenarios[0].right.sourceTwoFrameIntervals}, single-frame episodes={report.scenarios[0].right.singleFrameSourceEpisodes}, contact roll transfers={report.scenarios[0].right.contactRollTransfers}" : "";
            return $"{report.status}: scenarios={report.passed}/{report.scenarios.Count}; max stance slide={maxSlide:F5}m; max skin penetration={maxPenetration:F5}m; max stance sole gap={maxGap:F5}m; source root clearance={rootClearance:F3}m; report={path}; first failures={string.Join(" | ", report.failures.Take(6))}" + episodes;
        }

        private static bool Valid(FootResult foot, Report report, Scenario scenario)
        {
            bool valid = foot.stanceSamples >= 4 && foot.sourceTwoFrameIntervals >= 1 && foot.groundRayMisses == 0
                && foot.maxStanceSlide <= report.slideLimitMeters && foot.maxPenetration <= report.penetrationLimitMeters
                && foot.maxContactSoleGap <= report.contactGapLimitMeters;
            if (!valid) report.failures.Add($"{scenario.gait}{scenario.direction}@{scenario.simulatedFps}/{foot.foot}: stance={foot.stanceSamples}/{foot.sourceTwoFrameIntervals}, materialSegments={foot.stanceIntervals}, rayMiss={foot.groundRayMisses}, slide={foot.maxStanceSlide:F5}m, penetration={foot.maxPenetration:F5}m, stanceSoleGap={foot.maxContactSoleGap:F5}m");
            return valid;
        }

        private sealed class Foot
        {
            public Transform Bone;
            public Vector3 SoleLocal, PreviousSourceLocal, StanceStart;
            public int[] VertexIndices;
            public bool HasPrevious, WasStance;
            public int StanceFrames;
            public int ContactVertex = -1;
            public readonly List<float> SourceStanceSpeeds = new List<float>();
            public int SourceEpisodeFrames;
            public float SourceMinimum, SourceBackwardSpeed;
            public Vector3[] PreviousCorrectedVertices;
            public bool HasCorrectedPrevious;
        }

        private sealed class Fixture : IDisposable
        {
            private readonly GameObject _root, _floor, _cameraObject;
            private readonly PlayerVisualRig _rig;
            private readonly Animator _world, _near;
            private readonly Camera _camera;
            private readonly SkinnedMeshRenderer _skin;
            private readonly Mesh _baked;
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly Foot _left, _right;

            public Fixture(GameObject prefab, float rootClearance)
            {
                try
                {
                    _floor = GameObject.CreatePrimitive(PrimitiveType.Cube); _floor.name = "DosaFootAudit_Floor";
                    _floor.hideFlags = HideFlags.HideAndDontSave;
                    _floor.transform.position = new Vector3(1000f,-.05f,1000f); _floor.transform.localScale = new Vector3(100f,.1f,100f);
                    _floor.GetComponent<Renderer>().enabled = false;
                    _root = Object.Instantiate(prefab); _root.name = "DosaFootAudit_Temporary"; _root.hideFlags = HideFlags.HideAndDontSave;
                    _root.transform.SetPositionAndRotation(new Vector3(1000f,rootClearance,1000f), Quaternion.identity);
                    _rig = _root.GetComponent<PlayerVisualRig>();
                    _world = _root.transform.Find("WorldBody").GetComponent<Animator>();
                    _near = _root.transform.Find("NearArms").GetComponent<Animator>();
                    _cameraObject = new GameObject("DosaFootAudit_Camera"); _cameraObject.hideFlags = HideFlags.HideAndDontSave;
                    _camera = _cameraObject.AddComponent<Camera>(); _camera.enabled = false;
                    _skin = _world.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(r => r.sharedMesh.vertexCount).First();
                    _baked = new Mesh { name = "DosaFootAudit_Bake" };
                    _left = BuildFoot("Left", _world.GetBoneTransform(HumanBodyBones.LeftFoot));
                    _right = BuildFoot("Right", _world.GetBoneTransform(HumanBodyBones.RightFoot));
                    _world.Rebind(); _near.Rebind(); _world.Update(0f); _near.Update(0f);
                    Physics.SyncTransforms();
                }
                catch { Dispose(); throw; }
            }

            private Foot BuildFoot(string side, Transform bone)
            {
                if (bone == null) throw new InvalidOperationException(side + " foot bone missing");
                var mesh = _skin.sharedMesh; var weights = mesh.boneWeights; var vertices = mesh.vertices;
                var indices = new List<int>(); float minimum = float.MaxValue;
                var footBones = new HashSet<int>();
                for (int i = 0; i < _skin.bones.Length; i++)
                    if (_skin.bones[i].name.EndsWith(side + "Foot", StringComparison.Ordinal)
                        || _skin.bones[i].name.EndsWith(side + "ToeBase", StringComparison.Ordinal)) footBones.Add(i);
                for (int i = 0; i < vertices.Length; i++)
                {
                    var w = weights[i];
                    float weight = (footBones.Contains(w.boneIndex0) ? w.weight0 : 0f)
                        + (footBones.Contains(w.boneIndex1) ? w.weight1 : 0f)
                        + (footBones.Contains(w.boneIndex2) ? w.weight2 : 0f)
                        + (footBones.Contains(w.boneIndex3) ? w.weight3 : 0f);
                    if (weight < .35f) continue;
                    indices.Add(i);
                    minimum = Mathf.Min(minimum, _root.transform.InverseTransformPoint(_skin.transform.TransformPoint(vertices[i])).y);
                }
                if (indices.Count < 8) throw new InvalidOperationException(side + " foot has no trustworthy weighted mesh patch");
                Vector3 sum = Vector3.zero; int count = 0;
                foreach (int i in indices)
                {
                    Vector3 world = _skin.transform.TransformPoint(vertices[i]);
                    if (_root.transform.InverseTransformPoint(world).y > minimum + .02f) continue;
                    sum += world; count++;
                }
                if (count == 0) throw new InvalidOperationException(side + " sole mesh patch is empty");
                return new Foot { Bone = bone, SoleLocal = bone.InverseTransformPoint(sum / count), VertexIndices = indices.ToArray(),
                    PreviousCorrectedVertices = new Vector3[indices.Count] };
            }

            public void Step(Vector3 velocity, float dt, bool measure, Scenario result)
            {
                _root.transform.position += velocity * dt;
                _camera.transform.SetPositionAndRotation(_root.transform.position + new Vector3(0f,1.7f,-2.8f), Quaternion.identity);
                var frame = new PlayerVisualFrame { Camera = _camera, WorldBodyVisible = true, Grounded = true,
                    LocalVelocity = velocity, HasPointer = false, Drawing = false };
                _rig.UpdateMotion(frame, dt);
                _world.Update(dt); _near.Update(dt);
                Vector3 sourceLeft = _root.transform.InverseTransformPoint(_left.Bone.TransformPoint(_left.SoleLocal));
                Vector3 sourceRight = _root.transform.InverseTransformPoint(_right.Bone.TransformPoint(_right.SoleLocal));
                _skin.BakeMesh(_baked); _baked.GetVertices(_vertices);
                float leftMinimum = Minimum(_left, out _) - _root.transform.position.y;
                float rightMinimum = Minimum(_right, out _) - _root.transform.position.y;
                bool leftStance = IsSourceStance(_left, sourceLeft, leftMinimum, velocity, dt, measure);
                bool rightStance = IsSourceStance(_right, sourceRight, rightMinimum, velocity, dt, measure);
                _rig.ApplyFrame(frame, dt, dt);
                if (!measure)
                {
                    _left.WasStance = _right.WasStance = false;
                    _left.StanceFrames = _right.StanceFrames = 0;
                    _left.SourceEpisodeFrames = _right.SourceEpisodeFrames = 0;
                    return;
                }
                _skin.BakeMesh(_baked); _baked.GetVertices(_vertices);
                Sample(_left, leftStance, result.left, _rig.FootDiagnostics(true));
                Sample(_right, rightStance, result.right, _rig.FootDiagnostics(false));
            }

            public void Complete(Scenario result)
            {
                if (_left.SourceEpisodeFrames == 1) result.left.singleFrameSourceEpisodes++;
                if (_right.SourceEpisodeFrames == 1) result.right.singleFrameSourceEpisodes++;
                _left.SourceStanceSpeeds.Sort(); _right.SourceStanceSpeeds.Sort();
                result.left.medianSourceStanceSpeed = _left.SourceStanceSpeeds.Count > 0 ? _left.SourceStanceSpeeds[_left.SourceStanceSpeeds.Count / 2] : 0f;
                result.right.medianSourceStanceSpeed = _right.SourceStanceSpeeds.Count > 0 ? _right.SourceStanceSpeeds[_right.SourceStanceSpeeds.Count / 2] : 0f;
            }

            private static bool IsSourceStance(Foot foot, Vector3 local, float minimum, Vector3 direction, float dt, bool measure)
            {
                Vector3 velocity = foot.HasPrevious ? (local - foot.PreviousSourceLocal) / dt : Vector3.zero;
                foot.PreviousSourceLocal = local; foot.HasPrevious = true;
                float backwardSpeed = -Vector3.Dot(velocity, direction.normalized);
                foot.SourceMinimum = minimum; foot.SourceBackwardSpeed = backwardSpeed;
                bool stance = minimum < .065f && backwardSpeed > .05f;
                if (measure && stance) foot.SourceStanceSpeeds.Add(backwardSpeed);
                return stance;
            }

            private float Minimum(Foot foot, out int minimumIndex)
            {
                float minimum = float.MaxValue; minimumIndex = -1;
                foreach (int index in foot.VertexIndices)
                {
                    float y = _skin.transform.TransformPoint(_vertices[index]).y;
                    if (y >= minimum) continue;
                    minimum = y; minimumIndex = index;
                }
                return minimum;
            }

            private void Sample(Foot foot, bool stance, FootResult result, PlayerFootDiagnostics diagnostics)
            {
                float minimumWorld = Minimum(foot, out int minimumIndex);
                Vector3 lowest = _skin.transform.TransformPoint(_vertices[minimumIndex]);
                bool ray = Physics.Raycast(lowest + Vector3.up, Vector3.down, out RaycastHit hit, 5f, ~0, QueryTriggerInteraction.Ignore)
                    && hit.collider.gameObject == _floor;
                if (!ray) result.groundRayMisses++;
                float groundY = ray ? hit.point.y : 0f;
                float minimum = minimumWorld - groundY;
                result.minSoleHeight = Mathf.Min(result.minSoleHeight, minimum);
                result.maxPenetration = Mathf.Max(result.maxPenetration, -minimum);
                bool lifted = false;
                if (stance)
                {
                    if (!foot.WasStance) { foot.SourceEpisodeFrames = 0; result.sourceStanceEpisodes++; }
                    foot.SourceEpisodeFrames++;
                    if (foot.SourceEpisodeFrames == 2) result.sourceTwoFrameIntervals++;
                    lifted = foot.ContactVertex >= 0 && _skin.transform.TransformPoint(_vertices[foot.ContactVertex]).y > minimumWorld + .015f;
                    if (foot.WasStance && lifted) result.contactRollTransfers++;
                    if (foot.WasStance && lifted && foot.HasCorrectedPrevious)
                    {
                        int incoming = Array.IndexOf(foot.VertexIndices, minimumIndex);
                        Vector3 previous = foot.PreviousCorrectedVertices[incoming];
                        float displacement = Vector3.ProjectOnPlane(lowest - previous, Vector3.up).magnitude;
                        result.maxRollTransitionDisplacement = Mathf.Max(result.maxRollTransitionDisplacement, displacement);
                        if (Mathf.Abs(previous.y - groundY) <= .03f && Mathf.Abs(minimum) <= .03f)
                        {
                            result.groundedRollTransitionChecks++;
                            if (displacement > result.maxStanceSlide)
                            {
                                result.maxStanceSlide = displacement;
                                result.worstSlideWasLocked = diagnostics.Locked;
                                result.worstSlideWasContactTransfer = true;
                                result.worstSlideSourceGap = diagnostics.SourceSoleGap;
                                result.worstSlideReachRatio = diagnostics.TargetReachRatio;
                            }
                        }
                    }
                    if (!foot.WasStance || lifted)
                    {
                        foot.ContactVertex = minimumIndex;
                        foot.StanceStart = lowest;
                        foot.StanceFrames = 0;
                    }
                    Vector3 sole = _skin.transform.TransformPoint(_vertices[foot.ContactVertex]);
                    foot.StanceFrames++; result.stanceSamples++;
                    if (diagnostics.Locked) result.runtimeLockedStanceSamples++;
                    if (foot.StanceFrames == 2) result.stanceIntervals++;
                    float slide = Vector3.ProjectOnPlane(sole - foot.StanceStart, Vector3.up).magnitude;
                    if (slide > result.maxStanceSlide)
                    {
                        result.maxStanceSlide = slide;
                        result.worstSlideWasContactTransfer = false;
                        result.worstSlideWasLocked = diagnostics.Locked;
                        result.worstSlideSourceGap = diagnostics.SourceSoleGap;
                        result.worstSlideReachRatio = diagnostics.TargetReachRatio;
                    }
                    if (minimum > result.maxContactSoleGap)
                    {
                        result.maxContactSoleGap = minimum;
                        result.worstGapWasLocked = diagnostics.Locked;
                        result.worstGapReachRatio = diagnostics.TargetReachRatio;
                        result.worstGapSourceGap = diagnostics.SourceSoleGap;
                        result.worstGapPelvisLowering = diagnostics.PelvisLowering;
                        result.worstGapAnchor = diagnostics.AnchorWorld;
                        result.worstGapContact = diagnostics.ContactWorld;
                    }
                }
                else
                {
                    if (foot.SourceEpisodeFrames == 1) result.singleFrameSourceEpisodes++;
                    foot.SourceEpisodeFrames = 0;
                }
                if (result.contactTrace != null) result.contactTrace.Add(new ContactSample {
                    frame = result.contactTrace.Count, contactVertex = foot.ContactVertex,
                    sourceEpisodeFrames = foot.SourceEpisodeFrames, materialContactFrames = foot.StanceFrames,
                    sourceStance = stance, runtimeLocked = diagnostics.Locked, contactRolled = lifted,
                    sourceMinimum = foot.SourceMinimum, sourceBackwardSpeed = foot.SourceBackwardSpeed,
                    correctedMinimum = minimum });
                for (int i = 0; i < foot.VertexIndices.Length; i++)
                    foot.PreviousCorrectedVertices[i] = _skin.transform.TransformPoint(_vertices[foot.VertexIndices[i]]);
                foot.HasCorrectedPrevious = true;
                foot.WasStance = stance;
            }

            public void Dispose()
            {
                if (_root != null) Object.DestroyImmediate(_root);
                if (_floor != null) Object.DestroyImmediate(_floor);
                if (_cameraObject != null) Object.DestroyImmediate(_cameraObject);
                if (_baked != null) Object.DestroyImmediate(_baked);
            }
        }
    }
}
