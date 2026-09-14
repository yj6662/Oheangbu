using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Static scene-collider flow diagnosis; separate from route-sheet and camera tests.</summary>
    public static class WorldMacroTraversalAudit
    {
        const string RigPath = "Assets/_Project/Prefabs/Rig/PlayerRig.prefab";
        const float StationSpacing = 8f, BodyBandBottom = 1.2f, StepTolerance = .03f;
        const float WalkComfortGrade = 28f, RoadComfortGrade = 12f;
        const float WalkComfortCross = 15f, RoadComfortCross = 8f;
        const float MaxProbeSeconds = 8f, ProbeDt = 1f / 60f;
        static string PathOut => WorldMacroBuilder.Output + "/flow_audit.json";
        static string ProbePath => WorldMacroBuilder.Output + "/flow_move_probe.json";
        static Transform terrainRoot;
        static Scene scene;
        static int mask;
        static float castTop, castLength;
        static CapsuleSpec capsule;
        static readonly RaycastHit[] HitBuffer = new RaycastHit[64];
        static readonly Collider[] OverlapBuffer = new Collider[64];
        static int queryOverflows;

        [Serializable] public class CapsuleSpec
        {
            public string sourcePrefab, sourceController, sourceMotorConfig;
            public float height, radius, stepOffset, slopeLimit, skinWidth, minMoveDistance, moveSpeed, gravity;
            public Vector3 center, sourceScale;
            public int layer, collisionMask;
        }
        [Serializable] public class Criteria
        {
            public float stationSpacingM = StationSpacing, bodyOverlapMinHeightM = BodyBandBottom, stepToleranceM = StepTolerance;
            public float walkComfortGradeDeg = WalkComfortGrade, roadComfortGradeDeg = RoadComfortGrade;
            public float walkComfortCrossDeg = WalkComfortCross, roadComfortCrossDeg = RoadComfortCross;
            public string support = "Highest upward terrain, Bridge_Deck_TEST or Bridge_Apron_TEST collider surface. Deck end aprons are walkable support; bridge piers and other structures remain obstacles. No mathematical height fallback. Center and +/- actual capsule radius; authored road edges are separate comfort checks.";
            public string step = "Different supporting surface across 8m stations is bisected to <=0.04m. Subtract local slope contribution, capped at controller slope limit; residual above stepOffset is reported (WARN within 0.03m tolerance, otherwise HIGH bidirectional obstruction risk).";
            public string obstruction = "Upper body overlap capsule from ground+1.2m to controller height. Does not certify ankles or a full swept route.";
        }
        [Serializable] public class Issue
        {
            public string routeId, code, severity, collider, detail;
            public int sampleIndex;
            public float chainageM, measured, limit;
            public Vector3 position;
        }
        [Serializable] public class Sample
        {
            public string routeId, groundCollider, leftCollider, rightCollider;
            public int index;
            public bool hasGround, leftSupport, rightSupport, authoredLeftSupport, authoredRightSupport;
            public float chainageM, surfaceSlopeDeg, localForwardGradeDeg, capsuleCrossSlopeDeg, authoredCrossSlopeDeg;
            public float leftGroundY, rightGroundY, authoredLeftY, authoredRightY;
            public Vector3 position, forward, groundNormal;
            public string[] upperBodyObstacles = Array.Empty<string>();
        }
        [Serializable] public class RouteResult
        {
            public string id, from, to, status;
            public bool carriage;
            public int sampleCount, highCount, warningCount, missingGround, capsuleSupportMissing, upperBodyBlocked;
            public float planarLengthM, authoredWidthM, maxSurfaceSlopeDeg, maxLongitudinalGradeDeg, maxCrossSlopeDeg, maxStepResidualM;
        }
        [Serializable] public class Report
        {
            public string status, utc, scope = "Static Unity scene-collider diagnosis. Not full gameplay, not vehicle physics, not a rendered-visibility or route-completion claim.";
            public string scenePath, sceneFileSha256, sheetSha256;
            public bool activeSceneDirty;
            public CapsuleSpec capsule;
            public Criteria criteria = new Criteria();
            public int sceneColliderCount, sampleCount, highCount, warningCount, queryBufferOverflows;
            public double elapsedSeconds;
            public RouteResult[] routes = Array.Empty<RouteResult>();
            public Issue[] issues = Array.Empty<Issue>();
            public Sample[] samples = Array.Empty<Sample>();
            public string[] limitations = {
                "8m stations do not prove continuous traversability; small obstacles between stations may be missed. Bridge/terrain support changes receive a localized fine scan.",
                "Slope and upper-body overlap are obstruction indicators; the actual controller may slide or steer around a finding. The authored centerline remains the measured path.",
                "Comfort thresholds are TEST guidance, not CharacterController hard limits. Carriage routes use the player capsule for physical checks; vehicle clearance and vehicle physics remain unverified.",
                "Forward/backward 0.5m grades and capsule lateral samples use actual collider normals/heights, not smooth rendered normals. Authoring lattice and collider facets can differ from the painted road.",
                "No player input, enemy AI, combat, camera response, save/load or whole-world real-time run is exercised here. Optional short CharacterController.Move probes are reported separately."
            };
        }
        struct Ground
        {
            public bool found;
            public Vector3 point, normal;
            public Collider collider;
        }
        struct Station { public Vector3 point, forward; public float distance; }
        [Serializable] public class MoveResult
        {
            public string routeId, direction, status, reason, selectionCode;
            public Vector3 start, end;
            public float startChainageM, targetChainageM, reachedChainageM, targetDistanceM, durationSeconds, actualPlanarTravelM, maxLateralErrorM, selectionChainageM, finalFootClearanceM;
            public bool finalHasSupport, finalGrounded;
            public int frames, groundedFrames, sideCollisionFrames;
        }
        [Serializable] public class MoveReport
        {
            public string status, utc, scope = "Temporary bare CharacterController copied from PlayerRig, Move calls at explicit 1/60s, actual static scene colliders. Edit-mode diagnostic, not real input, PlayerMotor lifecycle, gameplay or full-route traversal.";
            public string sceneFileSha256, sheetSha256, auditUtc;
            public string selection = "At most four 20m windows, both directions. With HIGH findings: largest support step, steep collider/footprint, upper-body obstruction, and a control. Without HIGH findings: current CapitalSouthBridge apron-to-terrain boundary, Road_Gate_CapitalReservation gate-center endpoint, largest remaining warning-route surface/capsule cross slope, and one ordinary control. Gate window starts at its route endpoint and does not certify the whole inbound approach. Findings on the same physical place may belong to multiple routes; windows are not unique world-obstacle counts.";
            public CapsuleSpec capsule;
            public MoveResult[] segments;
        }

        static string Hash(string file)
        {
            if (!File.Exists(file)) return "MISSING";
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-", "").ToLowerInvariant();
        }
        static string Hierarchy(Transform t)
        {
            var names = new List<string>(); for (; t != null; t = t.parent) names.Add(t.name);
            names.Reverse(); return string.Join("/", names);
        }
        static void Prepare()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode required; no running player is modified.");
            scene = SceneManager.GetActiveScene();
            if (scene.path != WorldMacroBuilder.ScenePath) throw new InvalidOperationException("Open the existing macro blockout scene first.");
            var root = GameObject.Find("01_GlobalTerrain_IndependentOfRoads");
            if (root == null || WorldMacroBuilder.Sheet == null) throw new InvalidOperationException("Macro terrain and sheet required.");
            terrainRoot = root.transform;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
            var cc = prefab != null ? prefab.GetComponentInChildren<CharacterController>(true) : null;
            if (cc == null) throw new InvalidOperationException("PlayerRig CharacterController unavailable; no guessed dimensions.");
            var scale = cc.transform.lossyScale;
            if (Vector3.Angle(cc.transform.up, Vector3.up) > .1f) throw new InvalidOperationException("Tilted controller shape needs an explicit audit implementation.");
            var motor = prefab.GetComponentInChildren<PlayerMotor>(true);
            var config = motor != null ? new SerializedObject(motor).FindProperty("_config").objectReferenceValue as CombatConfigSO : null;
            if (config == null) throw new InvalidOperationException("PlayerRig motor config unavailable; no guessed move speed or gravity.");
            mask = 0; for (int layer = 0; layer < 32; layer++) if (!Physics.GetIgnoreLayerCollision(cc.gameObject.layer, layer)) mask |= 1 << layer;
            capsule = new CapsuleSpec { sourcePrefab = RigPath, sourceController = Hierarchy(cc.transform), sourceMotorConfig = AssetDatabase.GetAssetPath(config),
                height = cc.height * Mathf.Abs(scale.y), radius = cc.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)),
                center = Vector3.Scale(cc.center, scale), sourceScale = scale, stepOffset = cc.stepOffset * Mathf.Abs(scale.y), slopeLimit = cc.slopeLimit,
                skinWidth = cc.skinWidth, minMoveDistance = cc.minMoveDistance, layer = cc.gameObject.layer, collisionMask = mask,
                moveSpeed = config.MoveSpeed, gravity = config.Gravity };
            var colliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c => Valid(c)).ToArray();
            if (colliders.Length == 0) throw new InvalidOperationException("No active scene colliders.");
            castTop = colliders.Max(c => c.bounds.max.y) + 64f;
            castLength = castTop - colliders.Min(c => c.bounds.min.y) + 64f;
            queryOverflows = 0; Physics.SyncTransforms();
        }
        static bool Valid(Collider c) => c != null && c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy && c.gameObject.scene == scene && !c.name.StartsWith("Temporary_Traversal_");
        static bool IsGround(Collider c) => c.transform.IsChildOf(terrainRoot) || c.name == "Bridge_Deck_TEST" || c.name.StartsWith("Bridge_Apron_TEST");
        static Ground Support(Vector3 p)
        {
            int count = Physics.RaycastNonAlloc(new Vector3(p.x, castTop, p.z), Vector3.down, HitBuffer, castLength, mask, QueryTriggerInteraction.Ignore);
            if (count >= HitBuffer.Length) queryOverflows++;
            Ground result = default; float highest = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = HitBuffer[i]; if (!Valid(hit.collider) || !IsGround(hit.collider) || hit.normal.y <= .001f || hit.point.y <= highest) continue;
                highest = hit.point.y; result = new Ground { found = true, point = hit.point, normal = hit.normal, collider = hit.collider };
            }
            return result;
        }
        static float Slope(Ground g) => g.found ? Vector3.Angle(g.normal, Vector3.up) : 0f;
        static float Grade(Ground a, Ground b)
        {
            if (!a.found || !b.found) return 0;
            return Mathf.Atan2(Mathf.Abs(b.point.y - a.point.y), Mathf.Max(.001f, Planar(a.point, b.point))) * Mathf.Rad2Deg;
        }
        static float Planar(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
        static string[] Obstacles(Vector3 ground)
        {
            float radius = Mathf.Min(Mathf.Max(.05f, capsule.radius - capsule.skinWidth), (capsule.height - BodyBandBottom) * .5f);
            if (radius <= 0) return Array.Empty<string>();
            int count = Physics.OverlapCapsuleNonAlloc(ground + Vector3.up * (BodyBandBottom + radius), ground + Vector3.up * (capsule.height - radius), radius, OverlapBuffer, mask, QueryTriggerInteraction.Ignore);
            if (count >= OverlapBuffer.Length) queryOverflows++;
            var names = new HashSet<string>();
            for (int i = 0; i < count; i++) if (Valid(OverlapBuffer[i])) names.Add(Hierarchy(OverlapBuffer[i].transform));
            return names.OrderBy(n => n).ToArray();
        }
        static List<Station> Stations(WorldMacroSheetSO.RouteSpec route, out float length)
        {
            length = 0; var cumulative = new List<float> { 0 };
            for (int i = 1; i < route.Points.Length; i++) { length += Planar(route.Points[i - 1], route.Points[i]); cumulative.Add(length); }
            var result = new List<Station>(); if (route.Points.Length < 2 || length < .001f) return result;
            for (float d = 0; d < length; d += StationSpacing) result.Add(Along(route, cumulative, d));
            result.Add(Along(route, cumulative, length)); return result;
        }
        static Station Along(WorldMacroSheetSO.RouteSpec route, List<float> cumulative, float d)
        {
            for (int i = 1; i < route.Points.Length; i++) if (d <= cumulative[i] || i == route.Points.Length - 1)
            {
                float span = cumulative[i] - cumulative[i - 1]; if (span < .0001f) continue;
                var delta = route.Points[i] - route.Points[i - 1]; delta.y = 0;
                return new Station { point = Vector3.Lerp(route.Points[i - 1], route.Points[i], Mathf.Clamp01((d - cumulative[i - 1]) / span)), forward = delta.normalized, distance = d };
            }
            return default;
        }

        public static string Audit()
        {
            Prepare(); double started = EditorApplication.timeSinceStartup;
            var report = new Report { utc = DateTime.UtcNow.ToString("o"), scenePath = scene.path, activeSceneDirty = scene.isDirty,
                sceneFileSha256 = Hash(scene.path), sheetSha256 = Hash(WorldMacroBuilder.SheetPath), capsule = capsule,
                sceneColliderCount = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Count(Valid) };
            var issues = new List<Issue>(); var allSamples = new List<Sample>(); var routeResults = new List<RouteResult>();
            foreach (var route in WorldMacroBuilder.Sheet.Routes)
            {
                var stations = Stations(route, out float length);
                var rr = new RouteResult { id = route.Id, from = route.From, to = route.To, carriage = route.Carriage, authoredWidthM = route.Width, planarLengthM = length, sampleCount = stations.Count };
                Ground previous = default; Station previousStation = default;
                float comfortGrade = route.Carriage ? RoadComfortGrade : WalkComfortGrade, comfortCross = route.Carriage ? RoadComfortCross : WalkComfortCross;
                for (int i = 0; i < stations.Count; i++)
                {
                    var station = stations[i]; var center = Support(station.point);
                    var sample = new Sample { routeId = route.Id, index = i, chainageM = station.distance, position = center.found ? center.point : station.point, forward = station.forward, hasGround = center.found };
                    allSamples.Add(sample);
                    void Add(string code, string severity, float measured, float limit, string detail, string collider = "")
                    { issues.Add(new Issue { routeId = route.Id, sampleIndex = i, chainageM = station.distance, position = sample.position, code = code, severity = severity, measured = measured, limit = limit, detail = detail, collider = collider }); }
                    if (!center.found) { rr.missingGround++; Add("GROUND_MISSING", "HIGH", 0, 1, "No valid terrain/bridge support under route center."); previous = center; previousStation = station; continue; }
                    sample.groundCollider = Hierarchy(center.collider.transform); sample.groundNormal = center.normal; sample.surfaceSlopeDeg = Slope(center);
                    var rightVector = Vector3.Cross(Vector3.up, station.forward).normalized;
                    var left = Support(station.point - rightVector * capsule.radius); var right = Support(station.point + rightVector * capsule.radius);
                    var edgeLeft = Support(station.point - rightVector * route.Width * .5f); var edgeRight = Support(station.point + rightVector * route.Width * .5f);
                    sample.leftSupport = left.found; sample.rightSupport = right.found; sample.authoredLeftSupport = edgeLeft.found; sample.authoredRightSupport = edgeRight.found;
                    sample.leftGroundY = left.found ? left.point.y : 0; sample.rightGroundY = right.found ? right.point.y : 0;
                    sample.authoredLeftY = edgeLeft.found ? edgeLeft.point.y : 0; sample.authoredRightY = edgeRight.found ? edgeRight.point.y : 0;
                    sample.leftCollider = left.found ? Hierarchy(left.collider.transform) : "MISSING"; sample.rightCollider = right.found ? Hierarchy(right.collider.transform) : "MISSING";
                    sample.capsuleCrossSlopeDeg = Grade(left, right); sample.authoredCrossSlopeDeg = Grade(edgeLeft, edgeRight);
                    sample.localForwardGradeDeg = Grade(Support(station.point - station.forward * .5f), Support(station.point + station.forward * .5f));
                    rr.maxSurfaceSlopeDeg = Mathf.Max(rr.maxSurfaceSlopeDeg, sample.surfaceSlopeDeg); rr.maxCrossSlopeDeg = Mathf.Max(rr.maxCrossSlopeDeg, sample.capsuleCrossSlopeDeg);
                    rr.maxLongitudinalGradeDeg = Mathf.Max(rr.maxLongitudinalGradeDeg, sample.localForwardGradeDeg, Grade(previous, center));
                    if (sample.surfaceSlopeDeg > capsule.slopeLimit + .1f) Add("SURFACE_SLOPE_OVER_CONTROLLER", "HIGH", sample.surfaceSlopeDeg, capsule.slopeLimit, "Actual supporting collider face exceeds PlayerRig slope limit.", sample.groundCollider);
                    if (!left.found || !right.found) { rr.capsuleSupportMissing++; Add("CAPSULE_SIDE_SUPPORT_MISSING", "HIGH", 0, capsule.radius * 2, "Support absent inside required capsule footprint."); }
                    else if (sample.capsuleCrossSlopeDeg > capsule.slopeLimit + .1f) Add("CAPSULE_CROSS_SLOPE_OVER_CONTROLLER", "HIGH", sample.capsuleCrossSlopeDeg, capsule.slopeLimit, "Capsule-footprint lateral ground gradient exceeds controller limit.");
                    if (sample.localForwardGradeDeg > comfortGrade) Add("LONGITUDINAL_COMFORT", "WARN", sample.localForwardGradeDeg, comfortGrade, "TEST travel comfort, not a hard player/vehicle-physics failure.");
                    if (sample.authoredCrossSlopeDeg > comfortCross) Add("AUTHORED_WIDTH_CROSS_COMFORT", "WARN", sample.authoredCrossSlopeDeg, comfortCross, "Cross gradient across the full painted route width; independent of player capsule passage.");
                    if (!edgeLeft.found || !edgeRight.found) Add("AUTHORED_EDGE_SUPPORT_MISSING", "WARN", route.Width, capsule.radius * 2, "A painted road edge lacks terrain/bridge support.");
                    sample.upperBodyObstacles = Obstacles(center.point);
                    if (sample.upperBodyObstacles.Length > 0) { rr.upperBodyBlocked++; Add("UPPER_BODY_COLLIDER_OVERLAP", "HIGH", sample.upperBodyObstacles.Length, 0, "Collider overlaps ground+1.2m through capsule top; terrain floor deliberately excluded by height band.", string.Join(" | ", sample.upperBodyObstacles)); }
                    if (previous.found && previous.collider != center.collider)
                    {
                        var a = previousStation.point; var b = station.point; var ga = previous; var gb = center;
                        for (int iteration = 0; iteration < 12 && Planar(a, b) > .025f; iteration++)
                        { var mid = (a + b) * .5f; var gm = Support(mid); if (!gm.found) break; if (gm.collider == ga.collider) { a = mid; ga = gm; } else { b = mid; gb = gm; } }
                        float separation = Planar(a, b), raw = Mathf.Abs(gb.point.y - ga.point.y);
                        float smoothAllowance = separation * Mathf.Tan(Mathf.Min(capsule.slopeLimit, Mathf.Max(Slope(ga), Slope(gb))) * Mathf.Deg2Rad);
                        float residual = Mathf.Max(0, raw - smoothAllowance); rr.maxStepResidualM = Mathf.Max(rr.maxStepResidualM, residual);
                        if (residual > capsule.stepOffset)
                        {
                            issues.Add(new Issue { routeId = route.Id, sampleIndex = i, chainageM = station.distance - Planar(station.point, (a + b) * .5f), position = (ga.point + gb.point) * .5f,
                                code = "SUPPORT_TRANSITION_STEP", severity = residual > capsule.stepOffset + StepTolerance ? "HIGH" : "WARN", measured = residual, limit = capsule.stepOffset,
                                collider = Hierarchy(ga.collider.transform) + " -> " + Hierarchy(gb.collider.transform),
                                detail = $"Surface boundary raw height jump={raw:F3}m over {separation:F3}m; signed forward change={gb.point.y-ga.point.y:F3}m. Residual exceeds controller step; 0.03m tolerance band is WARN, larger residual is HIGH. May block one travel direction." });
                        }
                    }
                    previous = center; previousStation = station;
                }
                rr.highCount = issues.Count(x => x.routeId == route.Id && x.severity == "HIGH"); rr.warningCount = issues.Count(x => x.routeId == route.Id && x.severity == "WARN");
                rr.status = rr.highCount > 0 ? "SAMPLED_OBSTRUCTION_RISKS" : rr.warningCount > 0 ? "COMFORT_FINDINGS" : "NO_SAMPLED_FINDINGS"; routeResults.Add(rr);
            }
            report.routes = routeResults.ToArray(); report.samples = allSamples.ToArray();
            report.issues = issues.OrderByDescending(x => x.severity == "HIGH").ThenByDescending(x => x.measured / Mathf.Max(.1f, x.limit)).ToArray();
            report.sampleCount = allSamples.Count; report.highCount = issues.Count(i => i.severity == "HIGH"); report.warningCount = issues.Count(i => i.severity == "WARN");
            report.queryBufferOverflows = queryOverflows; report.elapsedSeconds = EditorApplication.timeSinceStartup - started;
            report.status = queryOverflows > 0 ? "INCOMPLETE_QUERY_OVERFLOW" : report.highCount > 0 ? "COMPLETED_WITH_OBSTRUCTION_RISKS" : "NO_SAMPLED_BLOCKERS_COMFORT_REVIEW_REQUIRED";
            Directory.CreateDirectory(WorldMacroBuilder.Output); File.WriteAllText(PathOut, JsonUtility.ToJson(report, true));
            return $"{report.status}: {report.sampleCount} actual-collider stations, {report.highCount} HIGH, {report.warningCount} WARN, {report.elapsedSeconds:F1}s; {PathOut}";
        }

        /// <summary>Four short route windows, both directions; no scene save or full game rig.</summary>
        public static string Probe()
        {
            Prepare(); if (!File.Exists(PathOut)) throw new InvalidOperationException("Run Audit first to select bounded windows.");
            var audit = JsonUtility.FromJson<Report>(File.ReadAllText(PathOut));
            if (audit.sheetSha256 != Hash(WorldMacroBuilder.SheetPath)) throw new InvalidOperationException("Sheet changed; rerun Audit before Probe.");
            if (audit.sceneFileSha256 != Hash(scene.path) || scene.isDirty || audit.activeSceneDirty) throw new InvalidOperationException("Scene changed or is unsaved; save the generated scene and rerun Audit before Probe.");
            if (audit.queryBufferOverflows > 0) throw new InvalidOperationException("Audit query overflow: resolve the incomplete audit before selecting Probe windows.");
            var picks = new List<Issue>();
            bool Distinct(Issue issue) => !picks.Any(p => (p.routeId == issue.routeId && Mathf.Abs(p.chainageM-issue.chainageM) < 25f) || (p.code != "CONTROL_NO_SAMPLED_HIGH" && Planar(p.position, issue.position) < 10f));
            var high = audit.issues.Where(i => i.severity == "HIGH").ToArray();
            if (high.Length == 0)
            {
                var bridgeSamples = audit.samples.Where(s => s.routeId == "Road_CapitalSouthBridge_SouthPost").OrderBy(s => s.chainageM).ToArray();
                for (int i = 1; i < bridgeSamples.Length; i++)
                {
                    var before = bridgeSamples[i-1]; var after = bridgeSamples[i];
                    if (before.hasGround && after.hasGround && before.groundCollider.Contains("/CapitalSouthBridge/Bridge_Apron_TEST") && after.groundCollider.Contains("/01_GlobalTerrain_IndependentOfRoads/"))
                    {
                        picks.Add(new Issue { routeId = after.routeId, chainageM = (before.chainageM+after.chainageM)*.5f, position = (before.position+after.position)*.5f, code = "REGRESSION_CAPITAL_SOUTH_APRON" });
                        break;
                    }
                }
                var gate = audit.samples.FirstOrDefault(s => s.routeId == "Road_Gate_CapitalReservation" && s.index == 0 && s.hasGround);
                if (gate != null) picks.Add(new Issue { routeId = gate.routeId, chainageM = gate.chainageM, position = gate.position, code = "REGRESSION_GATE_CENTER" });
                var warningRoutes = new HashSet<string>(audit.routes.Where(r => r.warningCount > 0).Select(r => r.id));
                var steep = audit.samples.Where(s => s.hasGround && warningRoutes.Contains(s.routeId)).OrderByDescending(s => Mathf.Max(s.surfaceSlopeDeg,s.capsuleCrossSlopeDeg)).FirstOrDefault();
                if (steep != null) picks.Add(new Issue { routeId = steep.routeId, chainageM = steep.chainageM, position = steep.position, code = "REGRESSION_MAX_REMAINING_SLOPE" });
                if (picks.Count != 3) throw new InvalidOperationException("A required no-HIGH regression window is unavailable; inspect the current apron, gate and warning-route samples instead of substituting ordinary controls.");
            }
            else
            {
                foreach (var category in new[] { new[] { "SUPPORT_TRANSITION_STEP" }, new[] { "SURFACE_SLOPE_OVER_CONTROLLER", "CAPSULE_CROSS_SLOPE_OVER_CONTROLLER" }, new[] { "UPPER_BODY_COLLIDER_OVERLAP" } })
                {
                    var candidate = high.Where(i => category.Contains(i.code) && Distinct(i)).OrderByDescending(i => i.measured/Mathf.Max(.1f,i.limit)).FirstOrDefault();
                    if (candidate != null) picks.Add(candidate);
                }
                foreach (var issue in high.OrderByDescending(i => i.measured/Mathf.Max(.1f,i.limit)))
                    if (picks.Count < 3 && Distinct(issue)) picks.Add(issue);
            }
            foreach (var id in new[] { "Trail_Inn_Logging", "Road_NorthPass_Bridge", "Trail_Mine_Inn", "Road_Post_Merchant" })
                if (picks.Count < 4 && !picks.Any(p => p.routeId == id) && audit.routes.Any(r => r.id == id && r.highCount == 0) && WorldMacroBuilder.Sheet.Routes.Any(r => r.Id == id))
                    picks.Add(new Issue { routeId = id, chainageM = 32f, code = "CONTROL_NO_SAMPLED_HIGH" });
            var results = new List<MoveResult>();
            foreach (var pick in picks)
            {
                var route = WorldMacroBuilder.Sheet.Routes.First(r => r.Id == pick.routeId); var cumulative = new List<float> { 0 };
                for (int i = 1; i < route.Points.Length; i++) cumulative.Add(cumulative[i-1] + Planar(route.Points[i-1], route.Points[i]));
                float length = cumulative.Last(), start = Mathf.Clamp(pick.chainageM - 10f, 0, Mathf.Max(0, length - 20f)), end = Mathf.Min(length, start + 20f);
                results.Add(MoveWindow(route, cumulative, start, end, pick)); results.Add(MoveWindow(route, cumulative, end, start, pick));
            }
            var report = new MoveReport { status = results.Count == 0 ? "NO_WINDOWS_AVAILABLE" : results.All(r => r.status == "REACHED_WINDOW_END") ? "BOUNDED_WINDOWS_REACHED_NOT_FULL_TRAVERSAL" : "BOUNDED_WINDOWS_WITH_FINDINGS", utc = DateTime.UtcNow.ToString("o"), capsule = capsule, segments = results.ToArray(), sceneFileSha256 = audit.sceneFileSha256, sheetSha256 = audit.sheetSha256, auditUtc = audit.utc };
            File.WriteAllText(ProbePath, JsonUtility.ToJson(report, true));
            return report.status + ": " + results.Count + " edit-mode controller windows; " + ProbePath;
        }
        static MoveResult MoveWindow(WorldMacroSheetSO.RouteSpec route, List<float> cumulative, float from, float to, Issue selection)
        {
            var result = new MoveResult { routeId = route.Id, direction = to > from ? "forward" : "reverse", startChainageM = from, targetChainageM = to, targetDistanceM = Mathf.Abs(to-from), status = "NOT_RUN", selectionCode = selection.code, selectionChainageM = selection.chainageM };
            var ground = Support(Along(route, cumulative, from).point); if (!ground.found) { result.status = "GROUND_MISSING"; return result; }
            GameObject go = null;
            try
            {
                go = new GameObject("Temporary_Traversal_Controller") { hideFlags = HideFlags.HideAndDontSave, layer = capsule.layer };
                var cc = go.AddComponent<CharacterController>(); cc.enabled = false; cc.height = capsule.height; cc.radius = capsule.radius; cc.center = capsule.center;
                cc.stepOffset = capsule.stepOffset; cc.slopeLimit = capsule.slopeLimit; cc.skinWidth = capsule.skinWidth; cc.minMoveDistance = capsule.minMoveDistance;
                go.transform.position = ground.point + Vector3.up * (capsule.height*.5f-capsule.center.y+.05f); cc.enabled = true; Physics.SyncTransforms();
                for (int i = 0; i < 12; i++) cc.Move(Vector3.down * .025f);
                result.start = go.transform.position; float vertical = -1f, command = from, stationary = 0;
                int frames = Mathf.CeilToInt(MaxProbeSeconds / ProbeDt);
                for (int i = 0; i < frames; i++)
                {
                    var prior = go.transform.position; command = Mathf.MoveTowards(command, to, capsule.moveSpeed * ProbeDt);
                    var target = Along(route, cumulative, command).point; var planar = target-prior; planar.y = 0;
                    vertical = cc.isGrounded ? -1f : vertical + capsule.gravity*ProbeDt;
                    var flags = cc.Move(Vector3.ClampMagnitude(planar, capsule.moveSpeed*ProbeDt) + Vector3.up*vertical*ProbeDt); Physics.SyncTransforms();
                    float actual = Planar(prior, go.transform.position); result.actualPlanarTravelM += actual; result.frames++; if (cc.isGrounded) result.groundedFrames++; if ((flags & CollisionFlags.Sides) != 0) result.sideCollisionFrames++;
                    float nearest = float.PositiveInfinity, chainage = from;
                    for (int k = 1; k < route.Points.Length; k++)
                    { float t; float distance = WorldMacroTerrain.SegmentDistance(go.transform.position.x, go.transform.position.z, route.Points[k-1], route.Points[k], out t); if (distance < nearest) { nearest = distance; chainage = Mathf.Lerp(cumulative[k-1], cumulative[k], t); } }
                    result.reachedChainageM = chainage; result.maxLateralErrorM = Mathf.Max(result.maxLateralErrorM, nearest);
                    if (Mathf.Abs(chainage-to) < .3f && nearest < .6f)
                    {
                        var endSupport = Support(go.transform.position);
                        float clearance = endSupport.found ? go.transform.position.y+capsule.center.y-capsule.height*.5f-endSupport.point.y : float.PositiveInfinity;
                        if (endSupport.found && (cc.isGrounded || Mathf.Abs(clearance) <= capsule.skinWidth+StepTolerance)) { result.status = "REACHED_WINDOW_END"; break; }
                    }
                    stationary = actual < capsule.moveSpeed*ProbeDt*.15f ? stationary + ProbeDt : 0;
                    if (stationary >= 1f) { result.status = "STALLED"; result.reason = "Less than 15% expected planar progress for one continuous simulated second."; break; }
                    if (nearest > 2f) { result.status = "LEFT_ROUTE"; result.reason = "Actual capsule drifted >2m from the authored centerline."; break; }
                    if (go.transform.position.y < ground.point.y - 12f) { result.status = "FELL_BELOW_START"; result.reason = "Capsule fell >12m below window start; stop diagnostic."; break; }
                }
                if (result.status == "NOT_RUN") { result.status = "WINDOW_NOT_REACHED"; result.reason = "8 simulated seconds elapsed; no forced teleport or grounded-height correction."; }
                result.durationSeconds = result.frames*ProbeDt; result.end = go.transform.position;
                var finalSupport = Support(result.end); result.finalHasSupport = finalSupport.found; result.finalGrounded = cc.isGrounded;
                result.finalFootClearanceM = finalSupport.found ? result.end.y+capsule.center.y-capsule.height*.5f-finalSupport.point.y : 0f;
            }
            finally { if (go != null) Object.DestroyImmediate(go); Physics.SyncTransforms(); }
            return result;
        }
    }
}
