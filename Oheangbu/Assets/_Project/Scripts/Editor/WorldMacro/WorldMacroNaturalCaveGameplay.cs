using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Targeted gameplay/map integration of the separately authored natural cave.</summary>
    public static class WorldMacroNaturalCaveGameplay
    {
        public const string RootName = "Playtest_NaturalCave";
        public const string Folder = WorldMacroPlaytestAuthoring.Folder + "/NaturalCave";
        public const string MapOverridePath = Folder + "/CaveMapOverride.json";
        public static string Output => WorldMacroPlaytestAuthoring.Output + "/NaturalCave";
        const string Revision = "macro-natural-cave-20260915";
        static WorldMacroPlaytestSession Session => Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
        static readonly Vector3[] ExitToStart = {
            new Vector3(0,.13f,-26), new Vector3(-9,.13f,-15), new Vector3(-26,.13f,-1),
            new Vector3(-43,.13f,5), new Vector3(-74,.13f,5), new Vector3(-103,.13f,-3),
            new Vector3(-134,.13f,2), new Vector3(-136,.13f,4) };

        [Serializable] sealed class IntegrationRecord
        {
            public string utc, oldRevision, newRevision, saveSlot, navigationBefore, navigationAfter;
            public int oldMainCount, oldJoinIndex, prefixCount;
            public Vector3 oldStart, newStart, oldInquiry, newInquiry;
            public Vector3[] exteriorTail;
            public string[] pointIds, actorIds;
        }
        [Serializable] sealed class MapOverride
        {
            public int version = 1;
            public string source = "Playtest_NaturalCave actual upward floor boundary";
            public WorldMapZoneSpec zone;
        }

        public static string Execute(string command)
        {
            if (command == "apply") return Apply();
            if (command == "validate") return Validate();
            if (command == "navigation") { Require(); BuildNavigation(); Save(); return "Natural cave local navigation rebaked; other navigation preserved."; }
            if (command == "map") { Require(); UpdateMap(); Save(); return "Natural cave detail map refreshed from authored floor."; }
            throw new ArgumentException(command);
        }
        static Transform Require()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != WorldMacroPlaytestAuthoring.ScenePath)
                throw new InvalidOperationException("Saved playtest scene in Edit mode required.");
            var root = GameObject.Find(RootName);
            if (root == null || Session == null || Session.Content == null) throw new InvalidOperationException("Natural cave geometry and connected playtest session must exist first.");
            DevSceneKit.EnsureFolder(Folder); Directory.CreateDirectory(Output + "/Backups");
            return root.transform;
        }
        static void Backup(Object asset, string key)
        {
            if (asset == null) return;
            string file = AssetDatabase.GetAssetPath(asset);
            if (!string.IsNullOrEmpty(file) && File.Exists(file))
            {
                string target = Output + "/Backups/" + key + Path.GetExtension(file);
                if (!File.Exists(target)) File.Copy(file, target);
            }
            string json = Output + "/Backups/" + key + ".json";
            if (!File.Exists(json)) File.WriteAllText(json, EditorJsonUtility.ToJson(asset, true));
        }
        static void Save()
        {
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
        public static string Apply()
        {
            Transform cave = Require(); var s = Session; var content = s.Content;
            if (content.TerrainRevision == Revision)
            {
                // A map or navigation failure after relocating objects can be resumed without moving them twice.
                var existingNav = Object.FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(n => n.name == "Playtest_Local_Navigation");
                if (existingNav == null || AssetDatabase.GetAssetPath(existingNav.navMeshData) != Folder + "/Navigation.asset") BuildNavigation();
                UpdateMap(); Save(); return "Natural cave integration resumed without duplicate relocation. " + Validate();
            }
            if (content.MainPath == null || content.MainPath.Length < 2) throw new InvalidOperationException("Existing exterior path is missing.");
            Backup(content, "Playtest_before_natural_cave"); Backup(s.PreviewSheet, "ContentPositions_before_natural_cave");
            Backup(AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>(WorldMapRuntimeBaker.DataPath), "WorldMap_before_natural_cave");
            var nav = Object.FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(n => n.name == "Playtest_Local_Navigation");
            if (nav == null || nav.navMeshData == null) throw new InvalidOperationException("Existing local navigation missing.");
            Backup(nav.navMeshData, "Navigation_before_natural_cave");
            string backupScene = Output + "/Backups/BeforeCaveGameplay.unity";
            if (!File.Exists(backupScene)) File.Copy(WorldMacroPlaytestAuthoring.ScenePath, backupScene);

            Vector3 entrance = cave.TransformPoint(ExitToStart[0]);
            // Keep the old exterior tail verbatim, including its terrain-conformed height samples.
            int join = Enumerable.Range(0, content.MainPath.Length)
                .Where(i => cave.InverseTransformPoint(content.MainPath[i]).z <= -25.5f)
                .OrderBy(i => (content.MainPath[i] - entrance).sqrMagnitude).FirstOrDefault();
            if (Vector3.Distance(content.MainPath[join], entrance) > 12) throw new InvalidOperationException("Could not locate the old route at the preserved entrance.");
            var point = content.Points.First(p => p.Id == "mine_inquiry");
            var record = new IntegrationRecord { utc = DateTime.UtcNow.ToString("o"), oldRevision = content.TerrainRevision,
                newRevision = Revision, saveSlot = content.SaveSlot, oldStart = content.StartFeet, oldInquiry = point.Position,
                oldMainCount = content.MainPath.Length, oldJoinIndex = join, exteriorTail = content.MainPath.Skip(join).ToArray(),
                pointIds = content.Points.Select(p => p.Id).ToArray(), actorIds = content.Encounters.Select(e => e.Id).ToArray(),
                navigationBefore = AssetDatabase.GetAssetPath(nav.navMeshData) };
            Vector3 start = cave.TransformPoint(new Vector3(-136,.13f,4));
            Vector3 inquiry = cave.TransformPoint(new Vector3(-131,.13f,-2));
            var prefix = Dense(ExitToStart.Reverse().Select(cave.TransformPoint).ToArray(), 1f).ToList();
            prefix.AddRange(Dense(new[] {prefix.Last(), record.exteriorTail[0]}, 1f).Skip(1).SkipLast(1));
            record.prefixCount = prefix.Count;
            content.MainPath = prefix.Concat(record.exteriorTail).ToArray(); content.StartFeet = start;
            content.StartYaw = Quaternion.LookRotation(cave.right).eulerAngles.y;
            point.Position = inquiry; record.newStart = start; record.newInquiry = inquiry;
            MovePreview("mine_inquiry", inquiry);
            Vector3 inquiryDelta = inquiry - record.oldInquiry;
            foreach (string path in new[] { "Playtest_OwnedAssets/Mine_Work_Basket", "Playtest_EarlyArt/Mine_BlastDebris", "Playtest_EarlyArt/Blast_Cord" })
            {
                var item = FindPath(path);
                if (item != null && item.gameObject.activeInHierarchy && !item.IsChildOf(cave)) item.position += inquiryDelta;
            }
            foreach (var pickup in Object.FindObjectsByType<WorldMacroFragmentPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (pickup.BundleId == "start") { pickup.transform.position += inquiryDelta; EditorUtility.SetDirty(pickup); }

            var beast = content.Encounters.First(e => e.Id == "mine_beast/0");
            beast.Feet = cave.TransformPoint(new Vector3(-77,.13f,5));
            beast.Patrol = new[] {beast.Feet, cave.TransformPoint(new Vector3(-71,.13f,7))};
            foreach (var actor in s.Actors)
            {
                var spec = content.Encounters.First(e => e.Id == actor.Id); var agent = actor.GetComponent<NavMeshAgent>();
                agent.enabled = false; actor.transform.position = spec.Feet + Vector3.up * agent.baseOffset;
                actor.PatrolPoints = spec.Patrol.Select(p => p + Vector3.up * agent.baseOffset).ToArray();
                EditorUtility.SetDirty(actor); EditorUtility.SetDirty(agent);
            }
            MovePreview("mine_beast", beast.Feet);
            content.TerrainRevision = Revision;
            if (s.Walker != null && s.Walker.Body != null)
            {
                bool enabled = s.Walker.Body.enabled; s.Walker.Body.enabled = false;
                s.Walker.Body.transform.SetPositionAndRotation(start, Quaternion.Euler(0, content.StartYaw, 0)); s.Walker.Body.enabled = enabled;
            }
            EditorUtility.SetDirty(content); EditorUtility.SetDirty(s.PreviewSheet); EditorUtility.SetDirty(s);
            Physics.SyncTransforms();
            record.navigationAfter = Folder + "/Navigation.asset";
            File.WriteAllText(Output + "/gameplay_integration.json", JsonUtility.ToJson(record, true));
            File.WriteAllText(Output + "/save_migration.txt", "Save slot is unchanged. No user save file was opened, reset or deleted. TerrainRevision changed from " + record.oldRevision + " to " + Revision +
                ". Existing Session.RepairProgress restores an older geography save at its registered mine/inn checkpoint, preserving currency, completion IDs, discoveries and inventory; an unsupported drop is moved safely to that checkpoint. Actual old-slot relaunch remains a runtime verification item.");
            BuildNavigation(); UpdateMap(); Save();
            return "Natural cave spawn, inquiry, start fragments, neutral encounter and route connected. Local navigation and irregular detail map saved. " + Validate();
        }
        static Transform FindPath(string path)
        {
            var parts = path.Split('/'); var root = GameObject.Find(parts[0]);
            return root == null ? null : root.transform.Find(string.Join("/", parts.Skip(1)));
        }
        static void MovePreview(string id, Vector3 position)
        {
            var s = Session; var p = s.PreviewPoints.FirstOrDefault(x => x != null && x.Id == id);
            if (p != null) { p.transform.position = position; EditorUtility.SetDirty(p); }
            var entry = s.PreviewSheet.Entries.FirstOrDefault(e => e.Id == id); if (entry != null) entry.Position = position;
        }
        static Vector3[] Dense(Vector3[] points, float step)
        {
            var values = new List<Vector3>();
            for (int i = 1; i < points.Length; i++)
            {
                int count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(points[i - 1], points[i]) / step));
                for (int n = 0; n < count; n++) values.Add(Vector3.Lerp(points[i - 1], points[i], n / (float)count));
            }
            values.Add(points.Last()); return values.ToArray();
        }
        static void BuildNavigation()
        {
            Transform cave = Require(); var s = Session;
            var surface = Object.FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(n => n.name == "Playtest_Local_Navigation");
            var points = ExitToStart.Select(cave.TransformPoint).Concat(s.Content.Encounters.SelectMany(e => e.Patrol.Concat(new[] {e.Feet}))).ToArray();
            var bounds = new Bounds(points[0], Vector3.zero); foreach (var p in points.Skip(1)) bounds.Encapsulate(p);
            bounds.Expand(new Vector3(56,40,56));
            foreach (var actor in s.Actors) actor.GetComponent<NavMeshAgent>().enabled = false;
            surface.RemoveData(); surface.navMeshData = null;
            surface.transform.SetPositionAndRotation(bounds.center, Quaternion.identity); surface.transform.localScale = Vector3.one;
            surface.collectObjects = CollectObjects.Volume; surface.center = Vector3.zero; surface.size = bounds.size;
            surface.layerMask = 1; surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.overrideVoxelSize = true; surface.voxelSize = .18f; surface.overrideTileSize = true; surface.tileSize = 128;
            Physics.SyncTransforms(); surface.BuildNavMesh();
            if (surface.navMeshData == null) throw new InvalidOperationException("Natural cave local NavMesh bake failed.");
            var generated = surface.navMeshData; surface.RemoveData();
            string path = Folder + "/Navigation.asset"; var saved = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
            if (saved == null) { saved = Object.Instantiate(generated); saved.name = "NaturalCave_LocalNavigation"; AssetDatabase.CreateAsset(saved, path); }
            else { EditorUtility.CopySerialized(generated, saved); EditorUtility.SetDirty(saved); }
            surface.navMeshData = saved; surface.AddData();
            if (generated != saved && !AssetDatabase.Contains(generated)) Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(surface);
        }

        static void UpdateMap()
        {
            var cave = Require(); var data = AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>(WorldMapRuntimeBaker.DataPath);
            if (data == null) throw new InvalidOperationException("Existing runtime map data is missing.");
            Backup(data, "WorldMap_before_natural_cave");
            Vector2[] footprint = FloorBoundary(cave);
            var zone = new WorldMapZoneSpec { Id = "cave", Label = "폐광 내부", Polygon = footprint,
                MinimumY = cave.position.y - 1, MaximumY = cave.position.y + 4 };
            zone.DetailPath = WorldMapGeometry.Simplify(Session.Content.MainPath.Where(zone.Contains).Select(WorldMapGeometry.XZ).ToArray(), .3f);
            var outline = footprint.Concat(new[] {footprint[0]}).ToArray();
            var lines = new List<WorldMapLineSpec> { new WorldMapLineSpec { Id = "NaturalCave_WalkableBoundary", Kind = WorldMapLineKind.DetailOutline, Points = outline, PixelWidth = 2.4f } };
            // Scan-line fill respects the concave corridor; no convex-hull shortcut across rock.
            int index = 0;
            for (float y = footprint.Min(p => p.y) + .4f; y < footprint.Max(p => p.y); y += 1.2f)
            {
                var crossings = new List<float>();
                for (int i = 0; i < footprint.Length; i++)
                {
                    var a = footprint[i]; var b = footprint[(i + 1) % footprint.Length];
                    if ((a.y <= y && b.y > y) || (b.y <= y && a.y > y)) crossings.Add(Mathf.Lerp(a.x, b.x, (y - a.y) / (b.y - a.y)));
                }
                crossings.Sort();
                for (int i = 1; i < crossings.Count; i += 2) lines.Add(new WorldMapLineSpec { Id = "NaturalCave_Fill_" + index++, Kind = WorldMapLineKind.DetailFill,
                    Points = new[] {new Vector2(crossings[i - 1],y), new Vector2(crossings[i],y)}, PixelWidth = 4 });
            }
            zone.DetailLines = lines.ToArray();
            if (!zone.Contains(Session.Content.StartFeet) || lines.Count < 3) throw new InvalidOperationException("Actual new cave floor failed detail-map validation.");
            var envelope = new MapOverride {zone = zone}; File.WriteAllText(MapOverridePath, JsonUtility.ToJson(envelope, true));
            AssetDatabase.ImportAsset(MapOverridePath, ImportAssetOptions.ForceSynchronousImport);
            TryApplyMapOverride(data); EditorUtility.SetDirty(data);
        }
        public static bool TryApplyMapOverride(WorldMapBakedDataSO data)
        {
            if (data == null || !File.Exists(MapOverridePath)) return false;
            var content = AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(WorldMacroPlaytestAuthoring.Folder + "/Playtest.asset");
            if (content == null || content.TerrainRevision != Revision) return false;
            var envelope = JsonUtility.FromJson<MapOverride>(File.ReadAllText(MapOverridePath));
            if (envelope?.zone?.Polygon == null || envelope.zone.Polygon.Length < 3) throw new InvalidOperationException("Natural cave map override is malformed.");
            data.Zones = (data.Zones ?? Array.Empty<WorldMapZoneSpec>()).Where(z => z != null && z.Id != "cave").Concat(new[] {envelope.zone}).ToArray();
            data.Lines = WorldMapRuntimeDataFactory.BuildLines(WorldMacroBuilder.Sheet, content, data.Zones);
            var marker = (data.Markers ?? Array.Empty<WorldMapMarkerSpec>()).FirstOrDefault(m => m != null && m.Id == "Mine");
            if (marker != null) { marker.WorldXZ = WorldMapGeometry.XZ(content.StartFeet); marker.Label = "폐광"; }
            data.Revision = WorldMacroBuilder.Sheet.Seed + ":" + content.TerrainRevision;
            return true;
        }

        static Vector2[] FloorBoundary(Transform cave)
        {
            var filters = cave.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null &&
                (f.name == "Cave_Walkable_Floor" || f.name == "Natural_Cave_Floor" || f.name == "NaturalCave_Floor")).ToArray();
            if (filters.Length == 0) throw new InvalidOperationException("Name the new independent floor Cave_Walkable_Floor or Natural_Cave_Floor for exact map extraction.");
            var positions = new List<Vector3>(); var welded = new Dictionary<Vector3Int,int>(); var edges = new Dictionary<ulong,int>();
            int Vertex(Vector3 p)
            {
                var key = new Vector3Int(Mathf.RoundToInt(p.x*1000),Mathf.RoundToInt(p.y*1000),Mathf.RoundToInt(p.z*1000));
                if (welded.TryGetValue(key,out int id)) return id; id = positions.Count; welded[key] = id; positions.Add(p); return id;
            }
            void Edge(int a,int b) { if (a == b) return; ulong key = ((ulong)(uint)Math.Min(a,b)<<32)|(uint)Math.Max(a,b); edges[key] = edges.TryGetValue(key,out int n) ? n+1 : 1; }
            foreach (var f in filters)
            {
                var mesh = f.sharedMesh; var vertices = mesh.vertices; var indices = mesh.triangles;
                for (int i = 0; i < indices.Length; i += 3)
                {
                    var a=f.transform.TransformPoint(vertices[indices[i]]);var b=f.transform.TransformPoint(vertices[indices[i+1]]);var c=f.transform.TransformPoint(vertices[indices[i+2]]);
                    Vector3 n=Vector3.Cross(b-a,c-a); if (n.sqrMagnitude<1e-10f || n.normalized.y<.65f) continue;
                    int ai=Vertex(a),bi=Vertex(b),ci=Vertex(c);Edge(ai,bi);Edge(bi,ci);Edge(ci,ai);
                }
            }
            var adjacency = new Dictionary<int,List<int>>(); var remaining = new HashSet<ulong>(edges.Where(p=>p.Value==1).Select(p=>p.Key));
            foreach (ulong edge in remaining) { int a=(int)(edge>>32),b=(int)(edge&0xffffffff); if(!adjacency.ContainsKey(a))adjacency[a]=new List<int>();if(!adjacency.ContainsKey(b))adjacency[b]=new List<int>();adjacency[a].Add(b);adjacency[b].Add(a); }
            if (adjacency.Count<3 || adjacency.Values.Any(v=>v.Count!=2)) throw new InvalidOperationException("New floor boundary must form closed manifold loops; refusing approximate cave map.");
            var loops = new List<Vector2[]>();
            while (remaining.Count>0)
            {
                ulong first=remaining.First();int start=(int)(first>>32),previous=-1,current=start;var loop=new List<Vector2>();
                do { loop.Add(WorldMapGeometry.XZ(positions[current]));int next=adjacency[current].First(v=>v!=previous);ulong edge=((ulong)(uint)Math.Min(current,next)<<32)|(uint)Math.Max(current,next);remaining.Remove(edge);previous=current;current=next;if(loop.Count>adjacency.Count+1)throw new InvalidOperationException("Cave boundary traversal failed."); } while(current!=start);
                loops.Add(loop.ToArray());
            }
            float Area(Vector2[] line) {float area=0;for(int i=0;i<line.Length;i++){var a=line[i];var b=line[(i+1)%line.Length];area+=a.x*b.y-b.x*a.y;}return Mathf.Abs(area*.5f);}
            var ordered=loops.OrderByDescending(Area).ToArray();var largest=ordered[0];
            // Other disconnected walkable floors are not silently represented as one joined chamber.
            if (ordered.Length>1 && ordered.Skip(1).Any(l=>Area(l)>8)) throw new InvalidOperationException("Multiple disconnected cave floor islands require explicit map zones.");
            return largest;
        }
        public static string Validate()
        {
            Transform cave = Require(); Physics.SyncTransforms(); var s = Session; var rows = new List<string>();
            void Check(bool pass,string detail)=>rows.Add((pass?"PASS ":"FAIL ")+detail);
            Check(s.Content.TerrainRevision==Revision,"explicit geography revision; user save slot retained");
            var recordPath=Output+"/gameplay_integration.json";IntegrationRecord record=File.Exists(recordPath)?JsonUtility.FromJson<IntegrationRecord>(File.ReadAllText(recordPath)):null;
            if(record!=null){Check(s.Content.SaveSlot==record.saveSlot,"save slot unchanged");Check(s.Content.Points.Select(p=>p.Id).SequenceEqual(record.pointIds),"interaction/reward identities unchanged");Check(s.Content.Encounters.Select(e=>e.Id).SequenceEqual(record.actorIds),"encounter identities unchanged");Check(s.Content.MainPath.Skip(record.prefixCount).SequenceEqual(record.exteriorTail),"exterior route tail positions unchanged");}
            Check(s.TrySafeFeet(s.Content.StartFeet,out _),"new mine checkpoint supports player capsule");
            Check(s.TrySafeFeet(s.Content.InnCheckpointFeet,out _),"inn checkpoint still supports player capsule");
            int samples=0,unsupported=0,blocked=0;var failures=new List<string>();
            var prefix=s.Content.MainPath.Take(record!=null?record.prefixCount+1:Math.Min(180,s.Content.MainPath.Length)).ToArray();
            foreach(var expected in Dense(prefix,.5f))
            {
                samples++;
                var hits=Physics.RaycastAll(expected+Vector3.up*.8f,Vector3.down,2,1,QueryTriggerInteraction.Ignore).Where(h=>h.normal.y>.65f&&!h.transform.IsChildOf(s.Walker.Body.transform)).OrderBy(h=>h.distance).ToArray();
                if(hits.Length==0){unsupported++;if(failures.Count<30)failures.Add("unsupported "+expected);continue;}
                var feet=hits[0].point+Vector3.up*.06f;
                var collisions=Physics.OverlapCapsule(feet+Vector3.up*.30f,feet+Vector3.up*1.47f,.27f,1,QueryTriggerInteraction.Ignore).Where(c=>!c.transform.IsChildOf(s.Walker.Body.transform)).ToArray();
                if(collisions.Length>0){blocked++;if(failures.Count<30)failures.Add("blocked "+expected+" "+string.Join(",",collisions.Select(c=>c.name)));}
            }
            Check(unsupported==0&&blocked==0,"new cave path structural capsule samples="+samples+" unsupported="+unsupported+" blocked="+blocked);
            var inquiry=s.Content.Points.First(p=>p.Id=="mine_inquiry");bool approach=false;
            for(int i=0;i<12;i++){float a=i*Mathf.PI/6;var candidate=inquiry.Position+new Vector3(Mathf.Cos(a)*1.6f,0,Mathf.Sin(a)*1.6f);if(s.TrySafeFeet(candidate,out var feet)&&!Physics.Linecast(feet+Vector3.up*1.3f,inquiry.Position+Vector3.up*.7f,1))approach=true;}
            Check(approach,"inquiry has a supported, unoccluded interaction approach");
            var pickup=Object.FindObjectsByType<WorldMacroFragmentPickup>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(p=>p.BundleId=="start");
            Check(pickup!=null&&Vector3.Distance(pickup.transform.position,inquiry.Position)<8,"start fragment pickup moved with inquiry; BundleId retained");
            foreach(var actor in s.Actors){var agent=actor.GetComponent<NavMeshAgent>();var from=actor.transform.position-Vector3.up*agent.baseOffset;var to=actor.PatrolPoints.Last()-Vector3.up*agent.baseOffset;var path=new NavMeshPath();bool ok=NavMesh.SamplePosition(from,out var a,2f,agent.areaMask)&&NavMesh.SamplePosition(to,out var b,2f,agent.areaMask)&&NavMesh.CalculatePath(a.position,b.position,agent.areaMask,path)&&path.status==NavMeshPathStatus.PathComplete;Check(ok&&!agent.enabled,"local navigation and saved-disabled startup "+actor.Id);}
            var map=AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>(WorldMapRuntimeBaker.DataPath);var zone=map?.Zones.FirstOrDefault(z=>z.Id=="cave");
            Check(zone!=null&&zone.Contains(s.Content.StartFeet)&&zone.Contains(inquiry.Position),"irregular detail map contains deep start and inquiry");
            Check(zone!=null&&!zone.Contains(cave.TransformPoint(new Vector3(0,.13f,-40))),"preserved exterior approach remains surface map");
            Check(zone!=null&&!zone.Contains(cave.TransformPoint(new Vector3(-26,.13f,-1))),"former exposed gallery before X=-43 portal remains surface map");
            Check(zone!=null&&!zone.Contains(cave.TransformPoint(new Vector3(-134,115,2))),"mountain above chamber is not classified as cave");
            Check(map!=null&&WorldMapValidation.Validate(map,WorldMacroBuilder.Sheet,s.Content).Length==0,"map projection, marker, detail and discovery contract");
            rows.AddRange(failures);rows.Add("UNVERIFIED actual input traversal, combat/AI in Play, old user-save relaunch, runtime frame time and visual approval. Structural casts are not automatic walking completion.");
            File.WriteAllLines(Output+"/gameplay_validation.txt",rows);return string.Join("\n",rows);
        }
    }
}
