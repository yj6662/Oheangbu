using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Explicit incremental Editor authoring. No runtime baking, terrain edits or visual changes.
    public static class DemoEscortNavigationAuthoring
    {
        public const string Folder="Assets/_Project/Art/Demo/EscortNavigation",RootName="Demo_EscortNavigation";
        const string StateKey="DemoEscortNavigationAuthoring.Work";
        const float CellSize=128,CorridorRadius=40,MaskStep=8;
        static readonly string[] RouteIds={"Road_Post_Merchant","Road_Merchant_Pass","Road_Pass_SouthPost","Road_SouthPost_Gate"};
        static string Output=>System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../Art/Demo/Chapter4"));
        static WorldMacroPlaytestSession Session=>Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
        static DemoEscortSceneRoute Route=>Object.FindObjectsByType<DemoEscortSceneRoute>(FindObjectsSortMode.None).Single(s=>s.gameObject.scene==Session.gameObject.scene);
        [Serializable] sealed class Edge{public Vector3 a,b;}
        [Serializable] sealed class Cell{public int x,z,sources,excluded,maskBoxes;public Vector3 centre,size;public string path;public bool baked;}
        [Serializable] sealed class Check{public string name,status,detail;}
        [Serializable] sealed class ExistingSurface
        {public string name,asset;public int agentTypeId,overlappingRegions;public bool active,sameAgentType,nearCorridor;public Vector3 centre,size;}
        [Serializable] sealed class ColliderProbe
        {public string path,type;public bool excludedDynamic;public Vector3 point,normal,centre,size;public float expectedHeightDelta;}
        [Serializable] sealed class NavProbe
        {public float radius,distance,heightDelta;public bool found;public Vector3 point;public string pathFromCompanion;}
        [Serializable] sealed class PointProbe
        {public string label;public Vector3 expected,floor;public bool foundFloor,strictSample;public ColliderProbe[] verticalHits,nearbyColliders,blockingCapsule;public NavProbe[] navigation;public string[] containingRegions;}
        [Serializable] sealed class ProbeReport
        {public string scope="Read-only actual physical/navigation evidence. Wider samples and lateral candidates are diagnostic only; runtime and authoring safety thresholds unchanged.",generation;public bool pendingInitiallyActive,pendingRestored;public PointProbe[] points;}
        [Serializable] sealed class RoadCoverage
        {public string routeId;public Vector3[] actualWaypoints;}
        [Serializable] sealed class Work
        {
            public string status,id,error;public int next,seams,agentTypeId;public float radius,height,slope,climb,commitRatio;
            public bool committed,relayOnly;public Cell[] cells;public Edge[] edges;public List<Check> checks=new List<Check>();
            public ExistingSurface[] existingSurfaces;
            public string excludedBuilding;public Bounds excludedBuildingBounds,buildingNavigationMask;
            public bool reservedRoadOriginInsideHanok;public Vector3 reservedRoadOrigin,actualEntryAnchor;
            public float actualEntryDistanceAlongRoad;public int actualEntrySegment;
            public string[] reservedOriginBlockingColliders;public Vector3[] entryConnectorWaypoints;public RoadCoverage[] actualRoadCoverage;
            public string scope="Editor-only 128m grid regions, approximately 80m corridor. PhysicsColliders preserve actual bridge/road heights. NPC/vehicle/cargo sources excluded without changing their settings. No source scene/terrain/visual changes until verified commit.";
        }
        static Work work;
        static NavMeshQueryFilter Filter=>new NavMeshQueryFilter{agentTypeID=work.agentTypeId,areaMask=NavMesh.AllAreas};
        static Transform Pending=>GameObject.Find(RootName+"_Pending")?.transform??FindInactive(RootName+"_Pending");
        static Transform FindInactive(string name)=>Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(t=>t.name==name&&t.gameObject.scene==Session.gameObject.scene);
        static void RequireEdit()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||Session==null||Session.gameObject.scene.path!=DemoFoundationAuthoring.Scene)
                throw new InvalidOperationException("Compiled dedicated demo Edit scene required.");
            if(Session.DemoEscortCompanion==null||Session.DemoEscortCargo==null||Route==null)throw new InvalidOperationException("Actual escort scene references required.");
        }
        static void Headroom()
        {float value=Oheangbu.EditorTools.Prologue.PrologueAudit.CommitRatio();if(work!=null)work.commitRatio=value;if(value>=.85f)throw new InvalidOperationException("System commit >=85%; no additional collection/bake is started.");}
        static void Persist()
        {Directory.CreateDirectory(Output);string json=JsonUtility.ToJson(work,true);SessionState.SetString(StateKey,json);File.WriteAllText(System.IO.Path.Combine(Output,"escort_navigation.json"),json);}
        static void Load(){if(work==null){string json=SessionState.GetString(StateKey,"");if(!string.IsNullOrEmpty(json))work=JsonUtility.FromJson<Work>(json);}}
        static void CheckThat(bool pass,string name,string detail)
        {work.checks.Add(new Check{name=name,status=pass?"PASS":"FAIL",detail=detail});if(!pass)throw new InvalidOperationException(name+": "+detail);}
        public static string Execute(string command)
        {
            RequireEdit();Load();
            if(command=="status")return work==null?"NOT_STARTED":JsonUtility.ToJson(work,true);
            if(command=="probe")return Probe();
            if(command=="audit")
            {
                if(work==null||!work.committed||FindInactive(RootName)==null)throw new InvalidOperationException("Committed navigation generation required.");
                RecordExistingSurfaces();AuditPaths();Persist();return JsonUtility.ToJson(work,true);
            }
            if(command=="survey"||command=="survey-relay"){var old=work;work=Plan(command=="survey-relay");string json=JsonUtility.ToJson(work,true);Directory.CreateDirectory(Output);File.WriteAllText(System.IO.Path.Combine(Output,"escort_navigation_plan.json"),json);work=old;return json;}
            if(command=="begin"||command=="begin-relay")
            {
                if(Pending!=null)throw new InvalidOperationException("Pending authoring exists. Continue step/seal/commit or abort explicitly.");
                Headroom();work=Plan(command=="begin-relay");CaptureRelayFootprint();EnsureFolder(Folder+"/"+work.id);var root=new GameObject(RootName+"_Pending");root.SetActive(false);work.status="BAKING";Persist();return JsonUtility.ToJson(work,true);
            }
            if(work==null||Pending==null)throw new InvalidOperationException("Begin an incremental navigation build first.");
            try
            {
                if(command=="step")
                {if(work.next>=work.cells.Length)return "ALL_REGIONS_BAKED; run seal";Headroom();Bake(work.cells[work.next]);work.next++;Persist();return $"BAKED {work.next}/{work.cells.Length}; one region only";}
                if(command=="seal"){Seal();Persist();return JsonUtility.ToJson(work,true);}
                if(command=="commit"){Commit();return JsonUtility.ToJson(work,true);}
                if(command=="abort"){Object.DestroyImmediate(Pending.gameObject);work.status="ABORTED";Persist();return "Pending scene objects removed; generation assets retained for review, original navigation unchanged.";}
                throw new ArgumentException("Use survey[-relay], begin[-relay], step, status, seal, commit, audit or abort.");
            }
            catch(Exception e){work.error=e.ToString();work.status="FINDINGS";Persist();throw;}
        }
        static bool Dynamic(Transform t)
        {
            var body=t.GetComponentInParent<Rigidbody>();var point=t.GetComponentInParent<WorldMacroContentPoint>();
            return t.GetComponentInParent<WorldMacroPalanquinController>()!=null||t.GetComponentInParent<CharacterController>()!=null||t.GetComponentInParent<NavMeshAgent>()!=null||
                body!=null&&!body.isKinematic||t.IsChildOf(Session.DemoEscortCompanion)||t.IsChildOf(Session.DemoEscortCargo)||
                t.GetComponentInParent<Oheangbu.App.Prologue.PrologueEncounter>()!=null||point!=null&&point.GetComponent<CapsuleCollider>()!=null||
                t.GetComponent<CapsuleCollider>()!=null&&t.name.EndsWith("_ExistingProxy",StringComparison.Ordinal);
        }
        static string Hierarchy(Transform t)=>t.parent==null?t.name:Hierarchy(t.parent)+"/"+t.name;
        static ColliderProbe Inspect(Collider collider,Vector3 expected,Vector3 point,Vector3 normal)=>new ColliderProbe
        {path=Hierarchy(collider.transform),type=collider.GetType().Name,excludedDynamic=Dynamic(collider.transform),point=point,normal=normal,
            centre=collider.bounds.center,size=collider.bounds.size,expectedHeightDelta=point.y-expected.y};
        static PointProbe InspectPoint(string label,Vector3 expected)
        {
            var result=new PointProbe{label=label,expected=expected};result.foundFloor=Floor(expected,out var support);result.floor=result.foundFloor?support.point:expected;
            result.strictSample=SampleGround(expected,out _);
            result.verticalHits=Physics.RaycastAll(expected+Vector3.up*12,Vector3.down,24,~0,QueryTriggerInteraction.Ignore)
                .Where(h=>h.collider.gameObject.scene==Session.gameObject.scene).OrderBy(h=>Mathf.Abs(h.point.y-expected.y))
                .Select(h=>Inspect(h.collider,expected,h.point,h.normal)).ToArray();
            result.nearbyColliders=Physics.OverlapSphere(result.floor,4,~0,QueryTriggerInteraction.Ignore).Where(c=>c.gameObject.scene==Session.gameObject.scene)
                .OrderBy(c=>Vector3.Distance(result.floor,c.ClosestPoint(result.floor))).Take(50).Select(c=>Inspect(c,expected,c.ClosestPoint(result.floor),Vector3.zero)).ToArray();
            result.blockingCapsule=Physics.OverlapCapsule(result.floor+Vector3.up*.3f,result.floor+Vector3.up*1.4f,.28f,~0,QueryTriggerInteraction.Ignore)
                .Where(c=>c.gameObject.scene==Session.gameObject.scene).Select(c=>Inspect(c,expected,c.ClosestPoint(result.floor),Vector3.zero)).ToArray();
            result.navigation=new[]{.4f,1.2f,2f,5f,10f}.Select(radius=>
            {
                var probe=new NavProbe{radius=radius};probe.found=NavMesh.SamplePosition(result.floor,out var nav,radius,Filter);
                if(probe.found)
                {probe.point=nav.position;probe.distance=Vector3.Distance(result.floor,nav.position);probe.heightDelta=nav.position.y-result.floor.y;
                    if(SampleGround(Session.DemoEscortCompanion.position,out var from)){var path=new NavMeshPath();probe.pathFromCompanion=NavMesh.CalculatePath(from,nav.position,Filter,path)?path.status.ToString():"CalculatePath=false";}}
                return probe;
            }).ToArray();
            result.containingRegions=work.cells.Where(c=>new Bounds(c.centre,c.size).Contains(result.floor)).Select(c=>c.path).ToArray();
            return result;
        }
        static string Probe()
        {
            if(work==null)throw new InvalidOperationException("Existing authoring generation required; probe never creates or bakes one.");
            var pending=Pending;var committed=FindInactive(RootName);if(pending==null&&committed==null)throw new InvalidOperationException("Existing pending or committed navigation required.");
            bool pendingActive=pending!=null&&pending.gameObject.activeSelf,committedActive=committed!=null&&committed.gameObject.activeSelf;
            var report=new ProbeReport{generation=work.id,pendingInitiallyActive=pendingActive};
            try
            {
                if(pending!=null){if(committed!=null)committed.gameObject.SetActive(false);pending.gameObject.SetActive(true);}Physics.SyncTransforms();
                var route=WorldMacroBuilder.Sheet.Routes.Single(r=>r.Id==RouteIds[0]);var a=route.Points[0];
                var direction=Vector3.ProjectOnPlane(route.Points[1]-a,Vector3.up).normalized;var side=Vector3.Cross(Vector3.up,direction);
                var points=new List<PointProbe>{InspectPoint("actual companion",Session.DemoEscortCompanion.position),InspectPoint("actual staging",Route.Stops.Single(s=>s.Id=="escort_start").CompanionWait.position),InspectPoint("first authored road point",a)};
                foreach(float along in new[]{0f,8f,16f})foreach(float lateral in new[]{-8f,-4f,-2f,0f,2f,4f,8f})
                    if(along!=0||lateral!=0)points.Add(InspectPoint("diagnostic offset along="+along+" side="+lateral,a+direction*along+side*lateral));
                report.points=points.ToArray();
            }
            finally
            {if(pending!=null)pending.gameObject.SetActive(pendingActive);if(committed!=null)committed.gameObject.SetActive(committedActive);report.pendingRestored=pending==null||pending.gameObject.activeSelf==pendingActive;}
            string json=JsonUtility.ToJson(report,true);Directory.CreateDirectory(Output);File.WriteAllText(System.IO.Path.Combine(Output,"escort_navigation_probe.json"),json);return json;
        }
        static bool Floor(Vector3 expected,out RaycastHit chosen)
        {
            chosen=default;
            // Choose the physical level nearest the authored road/stop, so a roof above it cannot replace bridge or ground support.
            foreach(var hit in Physics.RaycastAll(expected+Vector3.up*12,Vector3.down,24,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>Mathf.Abs(h.point.y-expected.y)))
                if(hit.collider.gameObject.scene==Session.gameObject.scene&&!Dynamic(hit.transform)&&hit.normal.y>=.85f)
                {chosen=hit;return true;}
            return false;
        }
        static float Distance(Vector3 p,Edge e,out Vector3 near)
        {
            var delta=e.b-e.a;delta.y=0;var q=p-e.a;q.y=0;float t=delta.sqrMagnitude>.001f?Mathf.Clamp01(Vector3.Dot(q,delta)/delta.sqrMagnitude):0;
            near=Vector3.Lerp(e.a,e.b,t);return new Vector2(p.x-near.x,p.z-near.z).magnitude;
        }
        static float Corridor(Vector3 p,out Vector3 near)
        {float best=float.PositiveInfinity;near=p;foreach(var edge in work.edges){float d=Distance(p,edge,out var candidate);if(d<best){best=d;near=candidate;}}return best;}
        static Work Plan(bool relayOnly)
        {
            var previous=work;var result=new Work{id="Generation_"+Guid.NewGuid().ToString("N"),status="SURVEY_ONLY",relayOnly=relayOnly};
            var edges=new List<Edge>();
            foreach(string id in relayOnly?RouteIds.Take(1):RouteIds)
            {var route=WorldMacroBuilder.Sheet.Routes.Single(r=>r.Id==id);if(!route.Carriage)throw new InvalidOperationException("Non-carriage route refused: "+id);
                float distance=0;
                for(int i=1;i<route.Points.Length;i++){edges.Add(new Edge{a=route.Points[i-1],b=route.Points[i]});distance+=Vector3.Distance(route.Points[i-1],route.Points[i]);if(relayOnly&&distance>=150)break;}}
            var start=Route.Stops.Single(s=>s.Id=="escort_start");edges.Add(new Edge{a=Session.DemoEscortCompanion.position,b=Session.DemoEscortCargo.position});
            edges.Add(new Edge{a=Session.DemoEscortCargo.position,b=start.CompanionWait.position});
            foreach(var stop in Route.Stops.Where(s=>!relayOnly||s.Id=="escort_start"))foreach(var t in new[]{stop.Interaction,stop.CompanionWait,stop.CargoWait,stop.Checkpoint})edges.Add(new Edge{a=stop.Parking.position,b=t.position});
            result.edges=edges.ToArray();work=result;
            try
            {
            var cells=new Dictionary<string,Cell>();
            foreach(var edge in edges)
            {
                int steps=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(edge.a,edge.b)/24));
                for(int i=0;i<=steps;i++)
                {var p=Vector3.Lerp(edge.a,edge.b,(float)i/steps);int cx=Mathf.FloorToInt(p.x/CellSize),cz=Mathf.FloorToInt(p.z/CellSize);
                    for(int x=cx-1;x<=cx+1;x++)for(int z=cz-1;z<=cz+1;z++)
                    {string key=x+"_"+z;if(cells.ContainsKey(key))continue;var centre=new Vector3((x+.5f)*CellSize,p.y,(z+.5f)*CellSize);
                        if(Corridor(centre,out _)<=CorridorRadius+CellSize*.7072f)cells.Add(key,new Cell{x=x,z=z});}}
            }
            foreach(var cell in cells.Values)
            {
                float min=float.PositiveInfinity,max=float.NegativeInfinity;var centre=new Vector3((cell.x+.5f)*CellSize,0,(cell.z+.5f)*CellSize);
                foreach(var edge in edges)if(Distance(centre,edge,out var near)<=CorridorRadius+CellSize*.7072f)
                {min=Mathf.Min(min,near.y,edge.a.y,edge.b.y);max=Mathf.Max(max,near.y,edge.a.y,edge.b.y);if(Floor(near,out var hit)){min=Mathf.Min(min,hit.point.y);max=Mathf.Max(max,hit.point.y);}}
                cell.centre=new Vector3(centre.x,(min+max)*.5f,centre.z);cell.size=new Vector3(CellSize-.04f,max-min+28,CellSize-.04f);
                cell.path=Folder+"/"+result.id+"/Cell_"+cell.x+"_"+cell.z+".asset";
            }
            result.cells=cells.Values.OrderBy(c=>Vector3.Distance(c.centre,start.CompanionWait.position)).ToArray();
            result.agentTypeId=Session.DemoEscortCompanion.GetComponent<NavMeshAgent>()?.agentTypeID??0;
            var settings=NavMesh.GetSettingsByID(result.agentTypeId);result.radius=settings.agentRadius;result.height=settings.agentHeight;result.slope=settings.agentSlope;result.climb=settings.agentClimb;
            RecordExistingSurfaces();
            return result;
            }
            finally{work=previous;}
        }
        static void EnsureFolder(string path)
        {if(AssetDatabase.IsValidFolder(path))return;string parent=System.IO.Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,System.IO.Path.GetFileName(path));}
        static void RecordExistingSurfaces()
        {
            var results=new List<ExistingSurface>();
            foreach(var surface in Object.FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                if(surface.gameObject.scene!=Session.gameObject.scene||surface.transform.root.name==RootName||surface.transform.root.name==RootName+"_Pending")continue;
                Bounds local=surface.navMeshData!=null?surface.navMeshData.sourceBounds:new Bounds(surface.center,surface.size);
                Bounds world=new Bounds(surface.transform.position+surface.transform.rotation*local.center,Vector3.zero);
                for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
                    world.Encapsulate(surface.transform.position+surface.transform.rotation*(local.center+Vector3.Scale(local.extents,new Vector3(x,y,z))));
                int overlaps=work.cells.Count(c=>new Bounds(c.centre,c.size).Intersects(world));
                results.Add(new ExistingSurface{name=surface.name,asset=AssetDatabase.GetAssetPath(surface.navMeshData),agentTypeId=surface.agentTypeID,
                    active=surface.isActiveAndEnabled,sameAgentType=surface.agentTypeID==work.agentTypeId,overlappingRegions=overlaps,
                    centre=world.center,size=world.size,nearCorridor=Corridor(world.center,out _)<=CorridorRadius+new Vector2(world.extents.x,world.extents.z).magnitude});
            }
            work.existingSurfaces=results.ToArray();
            // Bounds overlap is diagnostic, not proof that the same walkable polygons overlap. Full path checks remain mandatory.
            foreach(var surface in results.Where(s=>s.active&&s.sameAgentType&&s.overlappingRegions>0&&s.nearCorridor))
                work.checks.Add(new Check{name="existing navigation overlap: "+surface.name,status="REVIEW",detail=surface.asset+" overlaps "+surface.overlappingRegions+" planned regions. Existing surface retained; same-agent complete path checks must pass."});
        }
        static void CaptureRelayFootprint()
        {
            const string hierarchy="Demo_Chapter2_RelayAndLogging/OwnedStructures/Demo_Relay_Hanok";
            var building=GameObject.Find(hierarchy);
            if(building==null||building.scene!=Session.gameObject.scene)throw new InvalidOperationException("Actual relay building required for the recorded exterior-only escort route.");
            var colliders=building.GetComponentsInChildren<Collider>().Where(c=>c.enabled&&!c.isTrigger&&!Dynamic(c.transform)).ToArray();
            if(colliders.Length==0)throw new InvalidOperationException("Relay building has no physical footprint.");
            Bounds bounds=colliders[0].bounds;foreach(var collider in colliders.Skip(1))bounds.Encapsulate(collider.bounds);
            work.excludedBuilding=hierarchy;work.excludedBuildingBounds=bounds;
            // Navigation-only exterior routing: the existing high climb allowance must not route the
            // standing cargo carrier over this building's raised platform. Player geometry stays intact.
            bounds.Expand(new Vector3(.7f,8f,.7f));work.buildingNavigationMask=bounds;
        }
        static void Bake(Cell cell)
        {
            Physics.SyncTransforms();var bounds=new Bounds(cell.centre,cell.size);var sources=new List<NavMeshBuildSource>();var markups=new List<NavMeshBuildMarkup>();
            foreach(var modifier in NavMeshModifier.activeModifiers.Where(m=>m.gameObject.scene==Session.gameObject.scene&&m.AffectsAgentType(work.agentTypeId)))
                markups.Add(new NavMeshBuildMarkup{root=modifier.transform,overrideArea=modifier.overrideArea,area=modifier.area,ignoreFromBuild=modifier.ignoreFromBuild,
                    applyToChildren=modifier.applyToChildren,overrideGenerateLinks=true,generateLinks=false});
            NavMeshBuilder.CollectSources(bounds,~0,NavMeshCollectGeometry.PhysicsColliders,0,markups,sources);
            cell.excluded=sources.RemoveAll(s=>s.component!=null&&(s.component.gameObject.scene!=Session.gameObject.scene||Dynamic(s.component.transform)));
            if(!string.IsNullOrEmpty(work.excludedBuilding)&&bounds.Intersects(work.buildingNavigationMask))
            {sources.Add(new NavMeshBuildSource{shape=NavMeshBuildSourceShape.ModifierBox,area=1,transform=Matrix4x4.TRS(work.buildingNavigationMask.center,Quaternion.identity,Vector3.one),size=work.buildingNavigationMask.size});cell.maskBoxes++;}
            foreach(var modifier in NavMeshModifierVolume.activeModifiers.Where(m=>m.gameObject.scene==Session.gameObject.scene&&m.AffectsAgentType(work.agentTypeId)))
            {
                var scale=modifier.transform.lossyScale;scale=new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
                sources.Add(new NavMeshBuildSource{shape=NavMeshBuildSourceShape.ModifierBox,area=modifier.area,transform=Matrix4x4.TRS(modifier.transform.TransformPoint(modifier.center),modifier.transform.rotation,Vector3.one),size=Vector3.Scale(modifier.size,scale)});
            }
            // Navigation-only masks clip the collected terrain meshes; no source mesh/renderer/collider is edited.
            for(int x=0;x<CellSize/MaskStep;x++)for(int z=0;z<CellSize/MaskStep;z++)
            {var p=new Vector3(cell.x*CellSize+(x+.5f)*MaskStep,cell.centre.y,cell.z*CellSize+(z+.5f)*MaskStep);
                if(Corridor(p,out _)<=CorridorRadius+MaskStep*.7072f)continue;
                sources.Add(new NavMeshBuildSource{shape=NavMeshBuildSourceShape.ModifierBox,area=1,transform=Matrix4x4.TRS(p,Quaternion.identity,Vector3.one),size=new Vector3(MaskStep+.02f,cell.size.y+2,MaskStep+.02f)});cell.maskBoxes++;}
            cell.sources=sources.Count;Headroom();var settings=NavMesh.GetSettingsByID(work.agentTypeId);
            settings.overrideVoxelSize=true;settings.voxelSize=.18f;settings.overrideTileSize=true;settings.tileSize=128;settings.buildHeightMesh=true;
            var data=NavMeshBuilder.BuildNavMeshData(settings,sources,bounds,Vector3.zero,Quaternion.identity);
            if(data==null)throw new InvalidOperationException("NavMesh builder produced no data: "+cell.path);
            try
            {
                data.name="Escort_Cell_"+cell.x+"_"+cell.z;AssetDatabase.CreateAsset(data,cell.path);
                var node=new GameObject(data.name);node.transform.SetParent(Pending,false);var surface=node.AddComponent<NavMeshSurface>();
                surface.agentTypeID=work.agentTypeId;surface.collectObjects=CollectObjects.Volume;surface.center=cell.centre;surface.size=cell.size;surface.useGeometry=NavMeshCollectGeometry.PhysicsColliders;
                surface.navMeshData=data;surface.overrideVoxelSize=true;surface.voxelSize=.18f;surface.overrideTileSize=true;surface.tileSize=128;surface.buildHeightMesh=true;
                cell.baked=true;AssetDatabase.SaveAssets();
            }
            catch{if(!AssetDatabase.Contains(data))Object.DestroyImmediate(data);throw;}
        }
        static bool SampleGround(Vector3 expected,out Vector3 point)
        {
            point=default;if(!Floor(expected,out var hit)||Mathf.Abs(hit.point.y-expected.y)>1.5f||!NavMesh.SamplePosition(hit.point,out var nav,.4f,Filter)||Mathf.Abs(hit.point.y-nav.position.y)>.2f)return false;
            point=nav.position;return true;
        }
        static bool GroundConnection(Vector3 a,Vector3 b)
        {
            if(Vector3.Distance(a,b)>3)return false;Vector3 previous=a;
            int steps=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b)/.2f));
            for(int i=0;i<=steps;i++)
            {Vector3 p=Vector3.Lerp(a,b,(float)i/steps);if(!Floor(p,out var hit)||Mathf.Abs(hit.point.y-p.y)>.15f)return false;
                if(Physics.OverlapCapsule(hit.point+Vector3.up*.3f,hit.point+Vector3.up*1.4f,.28f,~0,QueryTriggerInteraction.Ignore)
                    .Any(c=>c.gameObject.scene==Session.gameObject.scene&&!Dynamic(c.transform)&&!c.name.StartsWith("Terrain_",StringComparison.Ordinal)))return false;
                if(i>0&&Mathf.Abs(hit.point.y-previous.y)>.16f)return false;previous=hit.point;}
            return true;
        }
        static void Seal()
        {
            if(work.cells.Any(c=>!c.baked))throw new InvalidOperationException("Bake every region with step before seam validation.");
            Headroom();var old=FindInactive(RootName);bool oldActive=old!=null&&old.gameObject.activeSelf;var root=Pending;
            if(old!=null)old.gameObject.SetActive(false);root.gameObject.SetActive(true);
            try
            {
                var prior=root.Find("Seams");if(prior!=null)Object.DestroyImmediate(prior.gameObject);var links=new GameObject("Seams").transform;links.SetParent(root,false);work.seams=0;
                var cells=work.cells.ToDictionary(c=>c.x+"_"+c.z);
                foreach(var cell in work.cells)foreach(bool east in new[]{true,false})
                {
                    if(!cells.ContainsKey((cell.x+(east?1:0))+"_"+(cell.z+(east?0:1))))continue;
                    for(float offset=4;offset<CellSize;offset+=8)
                    {
                        Vector3 seam=east?new Vector3((cell.x+1)*CellSize,cell.centre.y,cell.z*CellSize+offset):new Vector3(cell.x*CellSize+offset,cell.centre.y,(cell.z+1)*CellSize);
                        if(Corridor(seam,out var nearest)>CorridorRadius)continue;seam.y=nearest.y;Vector3 axis=east?Vector3.right:Vector3.forward;
                        if(!SampleGround(seam-axis*1.2f,out var a)||!SampleGround(seam+axis*1.2f,out var b)||!GroundConnection(a,b))continue;
                        var node=new GameObject("GroundSeam_"+work.seams);node.transform.SetParent(links,false);node.transform.position=a;
                        var proof=node.AddComponent<DemoEscortNavigationSeam>();proof.StartWorld=a;proof.EndWorld=b;
                        var link=node.AddComponent<NavMeshLink>();link.agentTypeID=work.agentTypeId;link.startPoint=Vector3.zero;link.endPoint=b-a;link.bidirectional=true;link.width=0;link.area=0;link.costModifier=-1;
                        work.seams++;
                    }
                }
                CheckThat(work.seams>0,"supported seam links",work.seams+" short, physically continuous ground connections; no cliff jump links.");RecordExistingSurfaces();AuditPaths();work.error=null;work.status="VERIFIED_PENDING_COMMIT";
            }
            finally{root.gameObject.SetActive(false);if(old!=null)old.gameObject.SetActive(oldActive);}
        }
        static void Path(Vector3 a,Vector3 b,string name)
        {
            bool sa=SampleGround(a,out var from),sb=SampleGround(b,out var to);CheckThat(sa&&sb,name+" endpoints","Actual surface + unchanged .4m nav sample at both endpoints.");
            var path=new NavMeshPath();CheckThat(NavMesh.CalculatePath(from,to,Filter,path)&&path.status==NavMeshPathStatus.PathComplete,name,"Complete loaded regional path, including verified ground seams.");
            if(work.relayOnly)
            {
                bool supported=true;Vector3 failed=default;
                for(int i=1;i<path.corners.Length&&supported;i++)
                {
                    Vector3 cornerA=path.corners[i-1],cornerB=path.corners[i];int pieces=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(cornerA,cornerB)/12));
                    for(int j=0;j<pieces;j++)
                    {
                        Vector3 start=Vector3.Lerp(cornerA,cornerB,(float)j/pieces),end=Vector3.Lerp(cornerA,cornerB,(float)(j+1)/pieces);
                        if(!ClearSupportedConnector(start,end,out _)){supported=false;failed=start;break;}
                    }
                }
                CheckThat(supported,name+" physical path",supported?"Actual path corners have continuous support and clear capsules/sweeps every <=.3m.":"Obstructed or unsupported segment starting at "+failed);
            }
        }
        static void AuditPaths()
        {
            var start=Route.Stops.Single(s=>s.Id=="escort_start");var actor=Session.DemoEscortCompanion.position;var cargo=Session.DemoEscortCargo.position;
            Path(actor,cargo-(cargo-actor).normalized*1.1f,"contract to actual cargo approach");Path(actor,start.CompanionWait.position,"contract to actual staging");
            Vector3 previous=start.CompanionWait.position;
            foreach(var stop in Route.Stops.Where(s=>!work.relayOnly||s.Id=="escort_start"))
            {Path(previous,stop.CompanionWait.position,"continuous escort to "+stop.Id);Path(stop.CompanionWait.position,stop.Checkpoint.position,"safe disembark/rest "+stop.Id);
                Path(stop.CompanionWait.position,stop.CargoWait.position,"cargo waiting "+stop.Id);previous=stop.CompanionWait.position;}
            AuthorActualRoadEntry();
            // Cover every route segment, not merely four disconnected stop islands.
            var coverage=new List<RoadCoverage>();
            foreach(string id in work.relayOnly?RouteIds.Take(1):RouteIds)
            {
                var route=WorldMacroBuilder.Sheet.Routes.Single(r=>r.Id==id);Vector3? last=null;float distance=0;int count=0;var actual=new List<Vector3>();
                for(int i=1;i<route.Points.Length;i++)
                {
                    var a=route.Points[i-1];var b=route.Points[i];float length=Vector3.Distance(a,b);
                    float authoredLength=length;
                    if(id==RouteIds[0])
                    {
                        if(i<work.actualEntrySegment){distance+=authoredLength;continue;}
                        if(i==work.actualEntrySegment){a=work.actualEntryAnchor;distance=work.actualEntryDistanceAlongRoad;length=Vector3.Distance(a,b);}
                    }
                    if(work.relayOnly&&distance>=140)break;
                    float fraction=work.relayOnly&&distance+length>140?(140-distance)/length:1;
                    int steps=Mathf.Max(1,Mathf.CeilToInt(length*fraction/24));
                    for(int j=0;j<=steps;j++)
                    {
                        var expected=Vector3.Lerp(a,b,fraction*j/steps);
                        if(!SampleGround(expected,out var sample))throw new InvalidOperationException("Road support/nav missing: "+id+" segment "+i+" at "+expected);
                        actual.Add(sample);if(last.HasValue){var path=new NavMeshPath();if(!NavMesh.CalculatePath(last.Value,sample,Filter,path)||path.status!=NavMeshPathStatus.PathComplete)
                            throw new InvalidOperationException("Regional road connection broken: "+id+" segment "+i+" at "+expected);}last=sample;count++;
                    }
                    distance+=length;
                }
                CheckThat(count>=2,"road corridor coverage "+id,count+" actual support samples with spacing <=24m and complete adjoining paths; no runtime bake.");
                coverage.Add(new RoadCoverage{routeId=id,actualWaypoints=actual.ToArray()});
            }
            work.actualRoadCoverage=coverage.ToArray();
        }
        static bool ClearSupportedConnector(Vector3 from,Vector3 to,out Vector3[] waypoints)
        {
            var points=new List<Vector3>();waypoints=Array.Empty<Vector3>();float length=Vector3.Distance(from,to);if(length>24)return false;
            int steps=Mathf.Max(1,Mathf.CeilToInt(length/.3f));Vector3 previous=from;
            for(int i=0;i<=steps;i++)
            {
                Vector3 expected=Vector3.Lerp(from,to,(float)i/steps);
                if(!Floor(expected,out var hit)||Mathf.Abs(hit.point.y-expected.y)>.2f)return false;
                Vector3 feet=hit.point+Vector3.up*.03f;
                if(Physics.OverlapCapsule(feet+Vector3.up*.3f,feet+Vector3.up*1.4f,.28f,~0,QueryTriggerInteraction.Ignore)
                    .Any(c=>c.gameObject.scene==Session.gameObject.scene&&!Dynamic(c.transform)&&!(c is TerrainCollider)))return false;
                Vector3 delta=feet-previous;
                if(i>0&&Mathf.Abs(delta.y)>.16f)return false;
                if(i>0&&delta.sqrMagnitude>.000001f&&Physics.CapsuleCastAll(previous+Vector3.up*.3f,previous+Vector3.up*1.4f,.27f,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore)
                    .Any(h=>h.collider.gameObject.scene==Session.gameObject.scene&&!Dynamic(h.transform)&&h.normal.y<.65f))return false;
                points.Add(feet);previous=feet;
            }
            waypoints=points.ToArray();return true;
        }
        static void AuthorActualRoadEntry()
        {
            var road=WorldMacroBuilder.Sheet.Routes.Single(r=>r.Id==RouteIds[0]);var start=Route.Stops.Single(s=>s.Id=="escort_start");
            work.reservedRoadOrigin=road.Points[0];Floor(work.reservedRoadOrigin,out var originFloor);
            work.reservedOriginBlockingColliders=Physics.OverlapCapsule(originFloor.point+Vector3.up*.3f,originFloor.point+Vector3.up*1.4f,.28f,~0,QueryTriggerInteraction.Ignore)
                .Where(c=>c.gameObject.scene==Session.gameObject.scene&&!Dynamic(c.transform)).Select(c=>Hierarchy(c.transform)).ToArray();
            work.reservedRoadOriginInsideHanok=work.reservedOriginBlockingColliders.Any(p=>p.Contains("/Demo_Relay_Hanok/"));
            // This is a distinct authored route entry from the real approved parking stop. The Sheet origin remains intact as a reserved landmark coordinate.
            float best=float.PositiveInfinity,travelled=0;Vector3 projection=default;int segment=0;
            for(int i=1;i<road.Points.Length&&travelled<150;i++)
            {
                Vector3 a=road.Points[i-1],b=road.Points[i];float length=Vector3.Distance(a,b);
                Vector3 bounded=travelled+length>150?Vector3.Lerp(a,b,(150-travelled)/length):b;
                float distance=Distance(start.Parking.position,new Edge{a=a,b=bounded},out var candidate);
                if(distance<best){best=distance;projection=candidate;segment=i;work.actualEntryDistanceAlongRoad=travelled+Vector3.Distance(a,candidate);}
                travelled+=length;
            }
            CheckThat(segment>0&&best<=12,"actual entry projects onto first 150m of carriage road","Parking-to-road lateral distance="+best+"; no expanded nav sample radius.");
            CheckThat(SampleGround(projection,out var sampled),"actual road entry anchor has real navigation","Projected reserved route point="+projection+"; strict .4m sample and actual support required.");
            CheckThat(Floor(sampled,out var floor),"actual entry collision support","Anchor uses the physical floor, separate from reserved road height.");
            work.actualEntryAnchor=floor.point+Vector3.up*.03f;work.actualEntrySegment=segment;
            CheckThat(ClearSupportedConnector(start.CompanionWait.position,start.Parking.position,out var toParking),"staging to real parking physical connector","<=24m explicit connector; support, capsule and sweep every <=.3m.");
            CheckThat(ClearSupportedConnector(start.Parking.position,work.actualEntryAnchor,out var toRoad),"parking to actual road entry physical connector","No building/terrain changes, no unchecked teleport or route-origin exception.");
            work.entryConnectorWaypoints=toParking.Concat(toRoad).ToArray();
            Path(start.CompanionWait.position,start.Parking.position,"staging to actual parking navigation");
            Path(start.Parking.position,work.actualEntryAnchor,"actual parking joins original carriage route");
        }
        static void Commit()
        {
            if(work.status!="VERIFIED_PENDING_COMMIT")throw new InvalidOperationException("seal must verify all required paths before commit.");Headroom();
            if(typeof(DemoEscortPresentation).GetField("SupportsGroundSeamWalker")==null)throw new InvalidOperationException("Install the reviewed minimal Presenter ground-seam integration before commit.");
            var presentation=Object.FindObjectsByType<DemoEscortPresentation>(FindObjectsSortMode.None).Single(p=>p.Session==Session);
            var root=Pending;var old=FindInactive(RootName);if(old!=null)Undo.DestroyObjectImmediate(old.gameObject);
            root.name=RootName;root.gameObject.SetActive(true);Undo.RegisterCreatedObjectUndo(root.gameObject,"Commit bounded escort navigation");
            var walker=Session.DemoEscortCompanion.GetComponent<DemoEscortSeamWalker>();if(walker==null)walker=Session.DemoEscortCompanion.gameObject.AddComponent<DemoEscortSeamWalker>();walker.Session=Session;
            walker.Presentation=presentation;
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(root.gameObject.scene);
            work.committed=true;work.status=work.relayOnly?"COMMITTED_RELAY_PILOT_ONLY":"COMMITTED_EDITOR_NAVIGATION";Persist();
        }
    }
}
