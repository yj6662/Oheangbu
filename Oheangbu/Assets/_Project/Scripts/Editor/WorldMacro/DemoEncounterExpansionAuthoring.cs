using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Data.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Opt-in additive placement. Survey/Audit never create actors or alter terrain/navigation.</summary>
    public static class DemoEncounterExpansionAuthoring
    {
        public const string RootName = "Demo_EncounterExpansion";
        const string Folder = "Assets/_Project/Art/Demo/EncounterExpansion";
        const string Prefix = "demo_exp_";
        const string SheetPath = "Assets/_Project/Art/World/WorldMacro/WorldMacroSheet.asset";
        const string Beast = "Assets/_Project/Art/SpellVFX120/Models/Beast_FolkTiger_B2.fbx";
        static readonly string[] Humans = { "Assets/_Project/Art/Characters/Dosa/Models/SM_DosaCourier_Refined.fbx", "Assets/_Project/Art/Characters/DosaV2/Models/SM_DosaV2_LOD2.fbx" };
        static readonly string[] WoodFx = { "Assets/_Project/Art/SpellVFX120/AreaRift/Cast_고.prefab", "Assets/_Project/Art/SpellVFX120/AreaFive/Body_고.prefab", "Assets/_Project/Art/SpellVFX120/AreaFive/AreaContact_0.prefab" };
        static readonly string[] ExistingIds = { "mine_beast/0", "mine_beast/1", "mine_fire/0", "demo_forest_approach_01", "demo_forest_approach_02", "demo_forest_approach_03", "demo_logging_01", "demo_logging_02", "demo_logging_03", "demo_logging_branch_01", "demo_logging_branch_02", "demo_logging_branch_03" };
        static readonly string[][] ExistingGroups = { new[] { "mine_beast/0" }, new[] { "mine_beast/1", "mine_fire/0" }, ExistingIds.Skip(3).Take(3).ToArray(), ExistingIds.Skip(6).Take(3).ToArray(), ExistingIds.Skip(9).Take(3).ToArray() };
        static WorldMacroPlaytestSession Session => Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
        static WorldMacroSheetSO Sheet => AssetDatabase.LoadAssetAtPath<WorldMacroSheetSO>(SheetPath);
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Demo/EncounterExpansion"));
        static bool Owned(string id) => id != null && id.StartsWith(Prefix, StringComparison.Ordinal);
        static Vector3[] protectedPreviewPositions = Array.Empty<Vector3>();
        [Serializable] sealed class Plan
        {
            public string id, name, route, narrative; public float fraction; public bool elite, optional;
            public EnemyArchetype[] roles;
            public Plan(string id, string name, string route, float fraction, bool elite, bool optional, string narrative, params EnemyArchetype[] roles)
            { this.id = id; this.name = name; this.route = route; this.fraction = fraction; this.elite = elite; this.optional = optional; this.narrative = narrative; this.roles = roles; }
        }
        static readonly Plan[] Plans = {
            new Plan("g06_deep_entry", "심부 입구의 굶주린 무리", "Trail_Logging_Deep", .25f, false, false, "벌목장 밖에서 근접 압박과 목 지면 경고를 함께 읽는다.", EnemyArchetype.NeutralMelee, EnemyArchetype.NeutralMelee, EnemyArchetype.WoodVine),
            new Plan("g07_deep_water", "물든 저지대", "Trail_Logging_Deep", .63f, false, false, "수 직선탄을 옆으로 피한 뒤 접근한다. 금극목 학습 배우는 별도다.", EnemyArchetype.WaterRanged, EnemyArchetype.NeutralMelee, EnemyArchetype.WoodVine),
            new Plan("g08_tree_return", "큰나무 갈림길", "Trail_Deep_TreeBridge", .53f, false, true, "국 재탐험 방향에서 투사체 발사자와 근접 추격자 중 목표를 고른다.", EnemyArchetype.WaterRanged, EnemyArchetype.FireRanged, EnemyArchetype.NeutralMelee),
            new Plan("g09_pass_watch", "고개 바깥의 금 병사", "Road_Merchant_Pass", .50f, false, false, "호송 검문소 밖에서 좁고 긴 금 찌르기를 읽는다. 검문관과는 무관한 전투다.", EnemyArchetype.MetalSoldier, EnemyArchetype.MetalSoldier, EnemyArchetype.FireRanged),
            new Plan("g10_upper_road", "상경 가도의 토 중장", "Road_Pass_SouthPost", .36f, false, false, "토의 느린 넓은 공격과 수의 직선탄을 분리해 상대한다.", EnemyArchetype.EarthHeavy, EnemyArchetype.WaterRanged, EnemyArchetype.MetalSoldier),
            new Plan("g11_south_road", "성저 앞 도로변 순찰", "Road_Pass_SouthPost", .72f, false, false, "근접 순찰을 먼저 끌어내거나 후방 사수를 노릴 수 있게 같은 쪽 어깨에 둔다.", EnemyArchetype.MetalSoldier, EnemyArchetype.FireRanged, EnemyArchetype.NeutralMelee),
            new Plan("g12_capital_edge", "남문길의 마지막 두 병사", "Road_SouthPost_Gate", .55f, false, false, "안전한 최종 정비와 장수 전투장 밖에서 토·금 대응을 복습한다.", EnemyArchetype.EarthHeavy, EnemyArchetype.MetalSoldier),
            new Plan("e01_root_warden", "정예·뿌리 감시자", "Trail_TreeBridge_Tree", .38f, true, true, "단독 정예. 큰 지면 경고를 연속 배치해 자리 고수 대신 이동을 요구한다.", EnemyArchetype.WoodVine),
            new Plan("e02_road_marshal", "정예·가도 수문병", "Road_SouthPost_Gate", .82f, true, false, "단독 정예. 긴 횡타 범위와 짧은 재준비 간격으로 옆·뒤 위치를 요구한다.", EnemyArchetype.EarthHeavy)
        };
        [Serializable] sealed class Candidate
        {
            public string group, route, reason; public bool valid, localNavigationPresent; public float fraction, routeMetres, laneClearance, nearestProtected, visibleApproachMetres;
            public Vector3 centre, routePoint, routeGround, forward, approach; public Vector3[] feet, patrol;
        }
        [Serializable] sealed class ExistingRow { public string group; public string[] actors; public Vector3 centre; public float radius, closestOtherActorDistance, minimumDetectionMargin; }
        [Serializable] sealed class Report
        {
            public string status, scope; public int existingOrdinary, addedOrdinary, addedElite, totalOrdinary, classifiedOrdinaryGroups, lessons, bosses;
            public bool physicalIsolationVerified;
            public Plan[] plans; public Candidate[] candidates; public ExistingRow[] existingPhysicalGroups; public string[] checks, sourceHashes;
            public string[] unverified = { "Native combat, aggression overlap, steering and view-frustum visibility", "Temporary source-derived static enemy visuals and attack animation", "Whole-journey quality, 90–120 minute duration, rewards and CPU/GPU p95/p99", "Final intended 2 bosses belong to other authoring; this tool never creates them" };
        }
        public static string Execute(string command)
        {
            RequireEdit(); CacheProtectedPreviews(); Directory.CreateDirectory(Output);
            if (command == "survey") return Save("survey.json", Survey());
            if (command == "apply") return Apply();
            if (command == "audit") return Save("audit.json", Audit());
            throw new ArgumentException("survey/apply/audit only. No automatic or terrain-generation command.");
        }
        static void RequireEdit()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Session == null || Session.gameObject.scene.path != DemoFoundationAuthoring.Scene ||
                AssetDatabase.GetAssetPath(Session.Content) != DemoFoundationAuthoring.Folder + "/Content.asset" || Sheet == null || Session.Walker?.Wiring == null)
                throw new InvalidOperationException("Dedicated demo Edit scene, content, current Sheet and player wiring required.");
            if (ExistingIds.Any(id => Session.Actors.Count(a => a != null && a.Id == id) != 1 || Session.Content.Encounters.Count(e => e != null && e.Id == id) != 1))
                throw new InvalidOperationException("The original 12 ordinary actors/rows must each exist exactly once.");
            if (Session.Actors.Any(a => a == null) || Session.Actors.Select(a => a.Id).Distinct().Count() != Session.Actors.Length)
                throw new InvalidOperationException("Existing actor registration is not unique.");
        }
        static string Save(string name, Report report) { string json = JsonUtility.ToJson(report, true); File.WriteAllText(Path.Combine(Output, name), json); return json; }
        static string AssetFile(string path) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        static float Length(WorldMacroSheetSO.RouteSpec r) { float length = 0; for (int i = 1; i < r.Points.Length; i++) length += Vector3.Distance(r.Points[i - 1], r.Points[i]); return length; }
        static Vector3 At(WorldMacroSheetSO.RouteSpec route, float fraction, out Vector3 forward)
        {
            float left = Length(route) * Mathf.Clamp01(fraction);
            for (int i = 1; i < route.Points.Length; i++)
            {
                float length = Vector3.Distance(route.Points[i - 1], route.Points[i]);
                if (left <= length || i == route.Points.Length - 1)
                { forward = Vector3.ProjectOnPlane(route.Points[i] - route.Points[i - 1], Vector3.up).normalized; return Vector3.Lerp(route.Points[i - 1], route.Points[i], Mathf.Clamp01(left / Mathf.Max(.001f, length))); }
                left -= length;
            }
            throw new InvalidOperationException("Empty route " + route.Id);
        }
        static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        { a.y = b.y = p.y = 0; var edge = b - a; return Vector3.Distance(p, a + edge * Mathf.Clamp01(Vector3.Dot(p - a, edge) / Mathf.Max(.001f, edge.sqrMagnitude))); }
        static float LaneClearance(Vector3 p)
        {
            float result = float.MaxValue;
            foreach (var route in Sheet.Routes.Where(r => r.Carriage)) for (int i = 1; i < route.Points.Length; i++)
                result = Mathf.Min(result, SegmentDistance(p, route.Points[i - 1], route.Points[i]) - route.Width * .5f);
            return result;
        }
        static bool Terrain(Collider c) => c is TerrainCollider || c.name.StartsWith("Terrain_", StringComparison.Ordinal);
        static bool OwnedCollider(Collider c) => c.GetComponentInParent<DemoEncounterExpansionTag>() != null;
        static void CacheProtectedPreviews()
        {
            var ids = new HashSet<string>();
            if (Session.PreviewSheet != null && Session.PreviewSheet.Entries != null)
                foreach (var entry in Session.PreviewSheet.Entries)
                    if (entry != null && (entry.Kind == MacroContentKind.Npc || entry.Kind == MacroContentKind.Rest)) ids.Add(entry.Id);
            var scene = Session.gameObject.scene;
            protectedPreviewPositions = Object.FindObjectsByType<WorldMacroContentPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(marker => marker.gameObject.scene == scene && !marker.CombatConnected && ids.Contains(marker.Id))
                .Select(marker => marker.transform.position).ToArray();
        }
        static bool Ground(Vector3 p, out Vector3 feet)
        {
            feet = default;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, 2200, p.z), Vector3.down, 4400, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (h.collider.gameObject.scene != Session.gameObject.scene || !Terrain(h.collider) || Vector3.Angle(h.normal, Vector3.up) > 22) continue;
                feet = h.point + Vector3.up * .06f;
                if (Physics.OverlapCapsule(feet + Vector3.up * .38f, feet + Vector3.up * 1.65f, .35f, ~0, QueryTriggerInteraction.Ignore)
                    .Any(c => c.gameObject.scene == Session.gameObject.scene && !Terrain(c) && !OwnedCollider(c))) return false;
                foreach (var d in new[] { Vector3.right * .35f, Vector3.left * .35f, Vector3.forward * .35f, Vector3.back * .35f })
                    if (!Physics.Raycast(feet + d + Vector3.up * .35f, Vector3.down, .75f, 1, QueryTriggerInteraction.Ignore)) return false;
                return true;
            }
            return false;
        }
        static float ProtectedDistance(Vector3 point)
        {
            float margin = float.MaxValue;
            foreach (var p in Session.Content.Points ?? Array.Empty<PrologueContentSO.Point>())
                if (p != null) margin = Mathf.Min(margin, Flat(point, p.Position) - 55);
            foreach (var p in Session.Content.Checkpoints ?? Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>())
                if (p != null) margin = Mathf.Min(margin, Flat(point, p.Feet) - 55);
            foreach (var actor in Session.Actors.Where(a => a != null && !Owned(a.Id)))
            {
                float radius = actor.Id == "cheongryong" || actor.Id == "south_gate_general" ? 110 : actor.Id == "demo_growth_lesson" ? 70 : 65;
                margin = Mathf.Min(margin, Flat(point, actor.transform.position) - radius);
            }
            foreach (var position in protectedPreviewPositions) margin = Mathf.Min(margin, Flat(point, position) - 45);
            foreach (var site in Sheet.Sites.Where(s => s.Kind == "Bridge")) margin = Mathf.Min(margin, Flat(point, site.Position) - 28);
            return margin;
        }
        static bool Visible(Vector3 eye, Vector3 target)
        {
            Vector3 delta = target - eye;
            return !Physics.RaycastAll(eye, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore).Any(h =>
                h.collider.gameObject.scene == Session.gameObject.scene && !OwnedCollider(h.collider) &&
                !h.transform.IsChildOf(Session.Walker.Body.transform));
        }
        static Candidate Measure(Plan plan, float fraction, float side)
        {
            var r = Sheet.Routes.Single(v => v.Id == plan.route); var c = new Candidate { group = plan.id, route = plan.route, fraction = fraction, reason = "terrain/space" };
            c.routePoint = At(r, fraction, out c.forward); c.routeMetres = Length(r) * fraction;
            Vector3 right = Vector3.Cross(Vector3.up, c.forward); float offset = r.Carriage ? r.Width * .5f + 4.5f : r.Width * .5f + 5;
            if (!Ground(c.routePoint, out c.routeGround) || !Ground(c.routePoint + right * offset * side, out c.centre) ||
                !Ground(At(r, Mathf.Max(0, fraction - 24 / Length(r)), out _), out c.approach)) return c;
            if (Mathf.Abs(c.centre.y - c.routeGround.y) > 5) { c.reason = "shoulder is disconnected vertically from measured route ground"; return c; }
            c.feet = new Vector3[plan.roles.Length]; c.patrol = new Vector3[plan.roles.Length]; c.laneClearance = float.MaxValue; c.nearestProtected = float.MaxValue;
            for (int i = 0; i < plan.roles.Length; i++)
            {
                float along = (i - (plan.roles.Length - 1) * .5f) * 5;
                Vector3 p = c.centre + c.forward * along + right * side * (i % 2 == 1 ? 2 : 0);
                if (!Ground(p, out c.feet[i]) || !Ground(p + c.forward * 3, out c.patrol[i])) return c;
                c.laneClearance = Mathf.Min(c.laneClearance, LaneClearance(c.feet[i]), LaneClearance(c.patrol[i]));
                c.nearestProtected = Mathf.Min(c.nearestProtected, ProtectedDistance(c.feet[i]), ProtectedDistance(c.patrol[i]));
                if (c.laneClearance < 1.5f || c.nearestProtected < 0) { c.reason = "carriage lane or protected NPC/checkpoint/arena/encounter buffer"; return c; }
                if (!Visible(c.approach + Vector3.up * 1.6f, c.feet[i] + Vector3.up)) { c.reason = "no advance route sightline"; return c; }
            }
            c.visibleApproachMetres = Flat(c.approach, c.centre); c.localNavigationPresent = c.feet.All(p => NavMesh.SamplePosition(p, out _, 1.5f, NavMesh.AllAreas));
            c.valid = true; c.reason = "ground/clearance/advance sightline candidate; local complete navigation still required by Apply"; return c;
        }
        static Candidate[] SelectCandidates()
        {
            var selected = new List<Candidate>();
            foreach (var p in Plans)
            {
                Candidate chosen = null, last = null;
                foreach (float delta in new[] { 0f, -.035f, .035f, -.07f, .07f })
                {
                    foreach (float side in new[] { 1f, -1f })
                    {
                        var c = Measure(p, Mathf.Clamp(p.fraction + delta, .1f, .9f), side); last = c;
                        if (c.valid && selected.All(q => !q.valid || Flat(c.centre, q.centre) >= 80)) { chosen = c; break; }
                    }
                    if (chosen != null) break;
                }
                if (chosen == null && last != null && last.valid) { last.valid = false; last.reason = "candidate is too close to another selected physical encounter"; }
                selected.Add(chosen ?? last);
            }
            return selected.ToArray();
        }
        static ExistingRow[] ExistingClassification() => ExistingGroups.Select((ids, i) => {
            var p = ids.Select(id => Session.Content.Encounters.Single(e => e.Id == id).Feet).ToArray(); var centre = p.Aggregate(Vector3.zero, (a, b) => a + b) / p.Length;
            var own = ids.Select(id => Session.Content.Encounters.Single(e => e.Id == id)).ToArray();
            var others = Session.Content.Encounters.Where(e => ExistingIds.Contains(e.Id) && !ids.Contains(e.Id)).ToArray();
            return new ExistingRow { group = "g0" + (i + 1), actors = ids, centre = centre, radius = p.Max(v => Flat(v, centre)),
                closestOtherActorDistance = own.Min(a => others.Min(b => Flat(a.Feet, b.Feet))), minimumDetectionMargin = own.Min(a => others.Min(b => Flat(a.Feet, b.Feet) - a.Detection - b.Detection)) };
        }).ToArray();
        static string[] SourceHashes()
        {
            return new[] { SheetPath, Beast, DevSceneKit.DefaultConfigPath }.Concat(Humans).Concat(WoodFx)
                .Concat(new[] { "NeutralMelee", "FireRanged", "WoodVine" }.Select(n => DemoChapterTwoAuthoring.Folder + "/" + n + ".asset"))
                .Select(p => { string file = AssetFile(p); if (!File.Exists(file)) throw new FileNotFoundException("Owned source missing", file); using (var sha = SHA256.Create()) return p + "=" + BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-", "").ToLowerInvariant(); }).ToArray();
        }
        static Report Survey()
        {
            Physics.SyncTransforms(); var candidates = SelectCandidates();
            return new Report { status = candidates.All(c => c.valid) ? "CANDIDATES_ONLY" : "BLOCKED", scope = "Read-only current Sheet/scene terrain, safety buffers and approach sightlines. No actor, navmesh, profile or scene creation.",
                existingOrdinary = 12, addedOrdinary = 0, addedElite = 0, totalOrdinary = 12, classifiedOrdinaryGroups = 5, physicalIsolationVerified = false,
                lessons = Session.Actors.Count(a => a.Id == "demo_growth_lesson"), bosses = Session.Actors.Count(a => a.Id == "cheongryong" || a.Id == "south_gate_general"), plans = Plans, candidates = candidates,
                existingPhysicalGroups = ExistingClassification(), sourceHashes = SourceHashes(), checks = new[] { "Target: 7 added ordinary groups, 20 ordinary + 2 elite; growth lesson excluded", "Any rejected candidate blocks Apply; no forced coordinates or terrain rebuild", "Original logging/branch detection discs can overlap. Five original group labels are a placement classification, not a verified isolation claim." } };
        }
        static void EnsureFolder(string p)
        { if (AssetDatabase.IsValidFolder(p)) return; EnsureFolder(Path.GetDirectoryName(p).Replace('\\', '/')); AssetDatabase.CreateFolder(Path.GetDirectoryName(p).Replace('\\', '/'), Path.GetFileName(p)); }
        static void SetRef(Object target, string field, Object value)
        { var so = new SerializedObject(target); var p = so.FindProperty(field); if (p == null) throw new InvalidOperationException(field); p.objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
        static EnemyAttackProfileSO Profile(EnemyArchetype archetype, bool elite)
        {
            string name = (elite ? "Elite_" : "") + archetype; string path = Folder + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(path); if (existing != null) { if (!existing.TryValidate(out var e)) throw new InvalidOperationException(e); return existing; }
            var source = AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(DemoChapterTwoAuthoring.Folder + "/" + archetype + ".asset");
            var p = source != null ? Object.Instantiate(source) : ScriptableObject.CreateInstance<EnemyAttackProfileSO>(); if (source == null) p.ApplyDefaults(archetype);
            if (elite && archetype == EnemyArchetype.WoodVine) { p.Telegraph = 1.55f; p.Recovery = .65f; p.CooldownRange = new Vector2(.95f, 1.25f); p.Range = 11.5f; p.ImpactRadius = 1.9f; p.PreferredDistanceHint = 8.5f; p.MovementSpeedHint = 2.1f; }
            if (elite && archetype == EnemyArchetype.EarthHeavy) { p.Telegraph = 1.85f; p.Recovery = 1.45f; p.CooldownRange = new Vector2(.95f, 1.35f); p.Range = 4.2f; p.ArcDegrees = 175; p.PreferredDistanceHint = 3; p.MovementSpeedHint = 1.85f; }
            if (!p.TryValidate(out var error)) throw new InvalidOperationException(error); p.name = name; AssetDatabase.CreateAsset(p, path); return p;
        }
        static Mesh VisualMesh(bool human)
        {
            string path = Folder + (human ? "/Mesh_HumanTemporary.asset" : "/Mesh_BeastDerivative.asset"); var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (saved != null) return saved;
            string source = human ? Humans.OrderBy(p => TriangleCount(AssetDatabase.LoadAssetAtPath<GameObject>(p))).First() : Beast;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(source); if (asset == null) throw new InvalidOperationException(source);
            var temp = Object.Instantiate(asset); temp.name = "Temporary source mesh extraction"; var pieces = new List<Mesh>(); var combines = new List<CombineInstance>();
            try
            {
                foreach (var r in temp.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh = null; if (r is SkinnedMeshRenderer skinned) { mesh = new Mesh(); skinned.BakeMesh(mesh); pieces.Add(mesh); }
                    else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
                    if (mesh == null) continue;
                    for (int sub = 0; sub < mesh.subMeshCount; sub++) combines.Add(new CombineInstance { mesh = mesh, subMeshIndex = sub, transform = temp.transform.worldToLocalMatrix * r.transform.localToWorldMatrix });
                }
                var result = new Mesh { name = human ? "Temporary owned humanoid" : "Owned folk-beast enemy derivative", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                result.CombineMeshes(combines.ToArray(), true, true); result.RecalculateBounds(); var b = result.bounds;
                if (b.size.y <= .01f || result.triangles.Length / 3 > 70000) { Object.DestroyImmediate(result); throw new InvalidOperationException("Source derivative requires bounded geometry review"); }
                float scale = (human ? 1.65f : .95f) / b.size.y; var vertices = result.vertices;
                for (int i = 0; i < vertices.Length; i++) { vertices[i] -= new Vector3(b.center.x, b.min.y, b.center.z); vertices[i] *= scale; if (!human) vertices[i].z *= 1.18f; }
                result.vertices = vertices; result.RecalculateNormals(); result.RecalculateBounds(); AssetDatabase.CreateAsset(result, path); return result;
            }
            finally { foreach (var mesh in pieces) Object.DestroyImmediate(mesh); Object.DestroyImmediate(temp); }
        }
        static int TriangleCount(GameObject source) => source == null ? int.MaxValue : source.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).Sum(f => f.sharedMesh.triangles.Length / 3) + source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null).Sum(r => r.sharedMesh.triangles.Length / 3);
        static Material Material(EnemyArchetype type)
        {
            string path = Folder + "/M_" + type + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path); if (m != null) return m;
            var shader = Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Universal Render Pipeline/Lit"); if (shader == null) throw new InvalidOperationException("URP material shader missing");
            m = new Material(shader); Color[] colors = { new Color(.28f, .23f, .18f), new Color(.3f, .13f, .08f), new Color(.13f, .24f, .13f), new Color(.12f, .22f, .27f), new Color(.31f, .34f, .35f), new Color(.30f, .25f, .14f) };
            m.SetColor("_BaseColor", colors[(int)type]); AssetDatabase.CreateAsset(m, path); return m;
        }
        static void BuildNavigation(Transform parent, Candidate c)
        {
            var go = new GameObject("Navigation_" + c.group); go.transform.SetParent(parent); go.transform.position = c.centre;
            var surface = go.AddComponent<NavMeshSurface>(); surface.collectObjects = CollectObjects.Volume; surface.center = Vector3.zero; surface.size = new Vector3(88, 60, 88);
            surface.layerMask = 1; surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders; surface.overrideVoxelSize = true; surface.voxelSize = .2f;
            surface.overrideTileSize = true; surface.tileSize = 128; surface.BuildNavMesh(); if (surface.navMeshData == null) throw new InvalidOperationException("Local navigation failed " + c.group);
            var generated = surface.navMeshData; surface.RemoveData(); string path = Folder + "/Navigation_" + c.group + ".asset";
            var saved = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
            if (saved == null) { saved = Object.Instantiate(generated); AssetDatabase.CreateAsset(saved, path); } else { EditorUtility.CopySerialized(generated, saved); EditorUtility.SetDirty(saved); }
            Object.DestroyImmediate(generated); surface.navMeshData = saved; surface.AddData();
            for (int i = 0; i < c.feet.Length; i++)
            {
                if (!NavMesh.SamplePosition(c.feet[i], out var spawn, 1, NavMesh.AllAreas) || !NavMesh.SamplePosition(c.patrol[i], out var end, 1, NavMesh.AllAreas) ||
                    !NavMesh.SamplePosition(c.approach, out var approach, 1.5f, NavMesh.AllAreas)) throw new InvalidOperationException("Disconnected navigation points " + c.group);
                var pathCheck = new NavMeshPath();
                if (!NavMesh.CalculatePath(spawn.position, approach.position, NavMesh.AllAreas, pathCheck) || pathCheck.status != NavMeshPathStatus.PathComplete ||
                    !NavMesh.CalculatePath(spawn.position, end.position, NavMesh.AllAreas, pathCheck) || pathCheck.status != NavMeshPathStatus.PathComplete)
                    throw new InvalidOperationException("Incomplete local approach/patrol route " + c.group);
                c.feet[i] = spawn.position; c.patrol[i] = end.position;
            }
        }
        static PrologueEncounter CreateActor(Transform parent, Plan plan, Candidate c, int i, CombatConfigSO config)
        {
            var profile = Profile(plan.roles[i], plan.elite); string id = Prefix + plan.id + "_" + (i + 1).ToString("00");
            var go = DevSceneKit.CreateEnemy(id, c.feet[i] + Vector3.up * .875f, config, profile.Element, Session.Walker.Body.transform, Session.Walker.Body.GetComponent<PlayerVitals>(), false);
            go.transform.SetParent(parent, true); go.transform.rotation = Quaternion.LookRotation(-c.forward); go.layer = 2;
            Object.DestroyImmediate(go.GetComponent<MeshRenderer>()); Object.DestroyImmediate(go.GetComponent<MeshFilter>());
            var capsule = go.GetComponent<CapsuleCollider>(); capsule.radius = .3f; capsule.height = 1.75f;
            var visual = new GameObject("Temporary source-derived enemy visual", typeof(MeshFilter), typeof(MeshRenderer)); visual.transform.SetParent(go.transform, false); visual.transform.localPosition = Vector3.down * .875f; visual.layer = 2;
            bool human = plan.roles[i] == EnemyArchetype.MetalSoldier || plan.roles[i] == EnemyArchetype.EarthHeavy;
            visual.GetComponent<MeshFilter>().sharedMesh = VisualMesh(human); visual.GetComponent<MeshRenderer>().sharedMaterial = Material(plan.roles[i]);
            if (human) visual.transform.localScale = new Vector3(plan.roles[i] == EnemyArchetype.EarthHeavy ? 1.22f : .9f, 1, 1);
            if (plan.elite) visual.transform.localScale = Vector3.Scale(visual.transform.localScale, new Vector3(1.12f, 1.06f, 1.12f));
            var ec = go.GetComponent<EnemyController>(); SetRef(ec, "_renderer", visual.GetComponent<Renderer>()); ec.Configure(profile); ec.AttackEnabled = false;
            var so = new SerializedObject(ec); so.FindProperty("_environmentOcclusion").boolValue = true; so.ApplyModifiedPropertiesWithoutUndo();
            var tag = go.AddComponent<DemoEncounterExpansionTag>(); tag.GroupId = plan.id; tag.IsElite = plan.elite; tag.Archetype = plan.roles[i]; tag.Role = (plan.elite ? "정예 " : "일반 ") + plan.roles[i]; tag.RouteId = plan.route; tag.Narrative = plan.narrative; tag.RouteFraction = c.fraction; tag.ApproachFeet = c.approach;
            if (profile.Delivery == EnemyAttackDelivery.GroundEruption)
            { var fx = go.AddComponent<EnemyAttackPresentation>(); fx.Configure(ec, AssetDatabase.LoadAssetAtPath<GameObject>(WoodFx[0]), AssetDatabase.LoadAssetAtPath<GameObject>(WoodFx[1]), AssetDatabase.LoadAssetAtPath<GameObject>(WoodFx[2])); if (!fx.HasAllSources) throw new InvalidOperationException("Owned root VFX missing"); }
            var nav = go.AddComponent<NavMeshAgent>(); nav.enabled = false; nav.radius = .3f; nav.height = 1.75f; nav.baseOffset = .875f; nav.speed = profile.MovementSpeedHint; nav.acceleration = 7; nav.angularSpeed = 160;
            var actor = go.AddComponent<PrologueEncounter>(); actor.Id = id; actor.Player = Session.Walker.Body.transform; actor.Session = null;
            actor.PatrolPoints = new[] { c.feet[i] + Vector3.up * .875f, c.patrol[i] + Vector3.up * .875f }; actor.Ranged = profile.Mode == EnemyController.AttackMode.RangedOnly;
            actor.DetectionRange = plan.elite ? 14 : 12; actor.Leash = plan.elite ? 27 : 24; actor.Speed = profile.MovementSpeedHint; actor.PreferredDistance = profile.PreferredDistanceHint;
            return actor;
        }
        static string Apply()
        {
            if (GameObject.Find(RootName) != null) return Save("audit.json", Audit());
            if (GameObject.Find(RootName + "_Pending") != null) throw new InvalidOperationException("A prior pending expansion root requires review before another Apply.");
            if (Session.gameObject.scene.isDirty || EditorUtility.IsDirty(Session.Content)) throw new InvalidOperationException("Save the current demo scene/content before Apply so its backup exactly matches the loaded baseline.");
            if (Session.Actors.Any(a => Owned(a.Id)) || Session.Content.Encounters.Any(e => Owned(e.Id))) throw new InvalidOperationException("Partial expansion registration requires review, not another spawn.");
            var survey = Survey(); if (survey.status == "BLOCKED") return Save("survey.json", survey);
            string[] hashes = SourceHashes(); var oldActors = Session.Actors; var oldRows = Session.Content.Encounters;
            string[] protectedActors = oldActors.Select(a => EditorJsonUtility.ToJson(a) + EditorJsonUtility.ToJson(a.transform)).ToArray();
            string protectedRows = string.Join("\n", oldRows.Select(e => JsonUtility.ToJson(e))); var wiring = new SerializedObject(Session.Walker.Wiring); var enemies = wiring.FindProperty("_enemies");
            var oldTargets = Enumerable.Range(0, enemies.arraySize).Select(i => enemies.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
            string backup = Path.Combine(Output, "Backups", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff")); Directory.CreateDirectory(backup);
            foreach (string path in new[] { Session.gameObject.scene.path, AssetDatabase.GetAssetPath(Session.Content) }) File.Copy(AssetFile(path), Path.Combine(backup, Path.GetFileName(path)), false);
            EnsureFolder(Folder); var root = new GameObject(RootName + "_Pending"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, Session.gameObject.scene);
            try
            {
                var config = AssetDatabase.LoadAssetAtPath<CombatConfigSO>(DevSceneKit.DefaultConfigPath); if (config == null) throw new InvalidOperationException("Owned combat config missing");
                var added = new List<PrologueEncounter>(); var rows = new List<WorldMacroPlaytestSO.Encounter>();
                for (int group = 0; group < Plans.Length; group++)
                {
                    var plan = Plans[group]; var c = survey.candidates[group]; var host = new GameObject(plan.id + "_" + plan.name); host.transform.SetParent(root.transform, false);
                    BuildNavigation(host.transform, c);
                    for (int i = 0; i < plan.roles.Length; i++)
                    {
                        var actor = CreateActor(host.transform, plan, c, i, config); added.Add(actor);
                        rows.Add(new WorldMacroPlaytestSO.Encounter { Id = actor.Id, ContentId = plan.id, Feet = c.feet[i], Patrol = new[] { c.feet[i], c.patrol[i] },
                            Ranged = actor.Ranged, RespawnOnRest = true, Detection = actor.DetectionRange, Leash = actor.Leash, Speed = actor.Speed, Activation = 115 });
                    }
                }
                if (!hashes.SequenceEqual(SourceHashes()) || protectedRows != string.Join("\n", oldRows.Select(e => JsonUtility.ToJson(e))) ||
                    !protectedActors.SequenceEqual(oldActors.Select(a => EditorJsonUtility.ToJson(a) + EditorJsonUtility.ToJson(a.transform)))) throw new InvalidOperationException("An original source or registered actor changed");
                Session.Actors = oldActors.Concat(added).ToArray(); Session.Content.Encounters = oldRows.Concat(rows).ToArray();
                var targets = oldTargets.Concat(added.Select(a => (Object)a.GetComponent<EnemyVitals>())).ToArray(); enemies.arraySize = targets.Length;
                for (int i = 0; i < targets.Length; i++) enemies.GetArrayElementAtIndex(i).objectReferenceValue = targets[i]; wiring.ApplyModifiedPropertiesWithoutUndo(); root.name = RootName;
                var audit = Audit(); if (audit.status != "PASS") throw new InvalidOperationException("Expansion audit rejected: " + string.Join("; ", audit.checks));
                EditorUtility.SetDirty(Session); EditorUtility.SetDirty(Session.Content); EditorUtility.SetDirty(Session.Walker.Wiring);
                AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(Session.gameObject.scene);
                if (!EditorSceneManager.SaveScene(Session.gameObject.scene)) throw new IOException("Scene save was rejected; backup at " + backup);
                Save("applied_candidates.json", survey); return Save("audit.json", audit);
            }
            catch
            {
                Session.Actors = oldActors; Session.Content.Encounters = oldRows;
                wiring.Update(); enemies = wiring.FindProperty("_enemies"); enemies.arraySize = oldTargets.Length;
                for (int i = 0; i < oldTargets.Length; i++) enemies.GetArrayElementAtIndex(i).objectReferenceValue = oldTargets[i]; wiring.ApplyModifiedPropertiesWithoutUndo();
                Object.DestroyImmediate(root); EditorUtility.SetDirty(Session.Content); AssetDatabase.SaveAssetIfDirty(Session.Content); throw;
            }
        }
        static Report Audit()
        {
            var checks = new List<string>(); bool pass = true; void C(bool ok, string text) { checks.Add((ok ? "PASS " : "FAIL ") + text); pass &= ok; }
            var actors = Session.Actors.Where(a => Owned(a.Id)).ToArray(); var tags = actors.Select(a => a.GetComponent<DemoEncounterExpansionTag>()).ToArray();
            C(actors.Length == 22 && tags.All(t => t != null), "22 uniquely registered new actors with explicit group/elite metadata");
            C(tags.Count(t => t != null && !t.IsElite) == 20 && tags.Count(t => t != null && t.IsElite) == 2, "20 ordinary plus 2 elite; lesson/boss excluded");
            C(tags.Where(t => t != null && !t.IsElite).Select(t => t.GroupId).Distinct().Count() == 7, "7 new ordinary placement groups plus 5 preserved original classifications; isolation not verified");
            C(Session.Actors.All(a => Owned(a.Id) || ExistingIds.Contains(a.Id) || a.Id == "demo_growth_lesson" || a.Id == "cheongryong" || a.Id == "south_gate_general"), "No unclassified actor silently counted as ordinary, elite or boss");
            C(tags.Where(t => t != null && !t.IsElite).Select(t => t.Archetype).Distinct().Count() == 6, "All 6 ordinary archetypes have actual registered actors");
            var wiring = new SerializedObject(Session.Walker.Wiring); var targets = wiring.FindProperty("_enemies"); var wired = Enumerable.Range(0, targets.arraySize).Select(i => targets.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
            foreach (var actor in actors)
            {
                var tag = actor.GetComponent<DemoEncounterExpansionTag>(); var controller = actor.GetComponent<EnemyController>(); var p = controller != null ? controller.AttackProfile : null;
                var rows = Session.Content.Encounters.Where(e => e.Id == actor.Id).ToArray();
                C(rows.Length == 1 && p != null && p.TryValidate(out _) && wired.Count(v => v == actor.GetComponent<EnemyVitals>()) == 1, actor.Id + " profile, content and combat target registration");
                if (rows.Length != 1 || tag == null) continue;
                C(ProtectedDistance(rows[0].Feet) >= 0 && LaneClearance(rows[0].Feet) >= 1.5f && LaneClearance(rows[0].Patrol.Last()) >= 1.5f, actor.Id + " protected-area/carriage-lane clearance");
                C(actor.GetComponentsInChildren<MeshFilter>().Any(f => AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith(Folder, StringComparison.Ordinal)), actor.Id + " saved owned-source mesh derivative, not capsule-only presentation");
                var routeCheck = new NavMeshPath();
                C(NavMesh.SamplePosition(rows[0].Feet, out var spawn, 1, NavMesh.AllAreas) && NavMesh.SamplePosition(tag.ApproachFeet, out var approach, 1.5f, NavMesh.AllAreas) &&
                    NavMesh.CalculatePath(spawn.position, approach.position, NavMesh.AllAreas, routeCheck) && routeCheck.status == NavMeshPathStatus.PathComplete,
                    actor.Id + " complete local route from visible approach");
                C(Visible(tag.ApproachFeet + Vector3.up * 1.6f, rows[0].Feet + Vector3.up), actor.Id + " advance approach sightline");
                C(actor.GetComponent<CheongryongCombatController>() == null && actor.GetComponent<SouthGateGeneralController>() == null, actor.Id + " has no cloned boss logic");
                if (tag.IsElite) C(p != null && ((tag.Archetype == EnemyArchetype.WoodVine && p.Damage == 17 && p.ImpactRadius >= 1.9f && p.CooldownRange.y <= 1.25f && p.PreferredDistanceHint >= 8.5f) ||
                    (tag.Archetype == EnemyArchetype.EarthHeavy && p.Damage == 24 && p.Range >= 4.2f && p.ArcDegrees >= 175 && p.CooldownRange.y <= 1.35f)), actor.Id + " elite changes spacing/position pressure, not damage");
            }
            return new Report { status = pass ? "PASS" : "FAIL", scope = "Temporary placement and configuration audit, not art approval or whole-journey completion.",
                existingOrdinary = 12, addedOrdinary = tags.Count(t => t != null && !t.IsElite), addedElite = tags.Count(t => t != null && t.IsElite), totalOrdinary = 12 + tags.Count(t => t != null && !t.IsElite),
                classifiedOrdinaryGroups = 5 + tags.Where(t => t != null && !t.IsElite).Select(t => t.GroupId).Distinct().Count(), physicalIsolationVerified = false, lessons = Session.Actors.Count(a => a.Id == "demo_growth_lesson"),
                bosses = Session.Actors.Count(a => a.Id == "cheongryong" || a.Id == "south_gate_general"), plans = Plans, existingPhysicalGroups = ExistingClassification(), checks = checks.ToArray(), sourceHashes = SourceHashes() };
        }
    }
}
