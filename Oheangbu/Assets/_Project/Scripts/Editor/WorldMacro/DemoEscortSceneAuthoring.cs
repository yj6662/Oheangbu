using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class DemoEscortSceneAuthoring
    {
        public const string RootName="Demo_EscortRoad";
        const string BoxSource="Assets/HwaseongHaenggung/Prefabs/SM_M_WoodenBox.prefab";
        const string PavilionSource="Assets/HwaseongHaenggung/Prefabs/SM_Naeposa.prefab";
        const string SackSource="Assets/KoreanTraditionalFestival/Prefabs/SM_SackOfRice.prefab";
        static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/Demo/Chapter4"));
        static WorldMacroPlaytestSession Session=>Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
        static WorldMacroPalanquinSeat Seat=>Object.FindObjectsByType<WorldMacroPalanquinSeat>(FindObjectsInactive.Include,FindObjectsSortMode.None)
            .SingleOrDefault(s=>s.gameObject.scene==Session.gameObject.scene && s.CombatWalker==Session.Walker);
        static WorldMacroPalanquinSummon Summon=>Object.FindObjectsByType<WorldMacroPalanquinSummon>(FindObjectsInactive.Include,FindObjectsSortMode.None)
            .SingleOrDefault(s=>s.gameObject.scene==Session.gameObject.scene && s.Seat==Seat);
        static Transform ChapterTwo=>GameObject.Find(DemoChapterTwoAuthoring.RootName)?.transform;
        static Transform Companion=>ChapterTwo?.Find("NPCs/wangso_w1");
        static Transform Cargo=>ChapterTwo?.Find("OwnedStructures/Demo_SealedCargo");
        static readonly string[] Ids={"escort_start","checkpoint_1","checkpoint_2","cargo_delivery"};
        static readonly string[] CheckpointIds={"escort_start","road_rest_1","road_rest_2","capital_escort_rest"};
        static readonly string[] Routes={"Road_Post_Merchant","Road_Merchant_Pass","Road_Pass_SouthPost","Road_SouthPost_Gate"};
        [Serializable] sealed class Check {public string name,status,detail;}
        [Serializable] sealed class Candidate
        {
            public string id,route,rejection;public bool suitable;public float fraction,side,slope,score;
            public Vector3 reservedRoutePoint,measuredInteraction,measuredCheckpoint,measuredCompanion,measuredCargo,measuredOffice,parkingPosition;
            public Quaternion parkingRotation;public float yaw,officeSpread;
        }
        [Serializable] sealed class Report
        {
            public string status,scope,scene,companionSource,cargoSource;public Candidate[] selected,candidates;
            public Check[] checks;public Vector3 originalCompanion,originalCargo,driverSeatLocal,passengerLocal,cargoLocal;
            public string[] sources={BoxSource,PavilionSource,SackSource};public float roadMetres;
            public string visualLimit="Wangso and attendants reuse the existing replaceable NPC proxy. Standing passenger/cargo sockets are provisional; live boarding, interior clipping and driving remain unverified.";
        }
        public static string Execute(string command)
        {
            RequireEdit();Directory.CreateDirectory(Output);
            if(command=="survey")return Save("escort_scene_survey.json",Survey());
            if(command=="apply")return Apply();
            if(command=="audit")return Save("escort_scene_audit.json",Audit());
            throw new ArgumentException("Expected survey, apply or audit. No automatic scene mutation is registered.");
        }
        static void RequireEdit()
        {
            if(EditorApplication.isPlaying||EditorApplication.isPlayingOrWillChangePlaymode||Session==null||Session.gameObject.scene.path!=DemoFoundationAuthoring.Scene||
                AssetDatabase.GetAssetPath(Session.Content)!=DemoFoundationAuthoring.Folder+"/Content.asset")throw new InvalidOperationException("Dedicated W_Demo_Campaign Edit scene/content required.");
            if(Seat==null||Summon==null||Companion==null||Cargo==null)throw new InvalidOperationException("Existing Wangso, sealed cargo, unique player vehicle seat and summon are required.");
        }
        static string Save(string name,Report report){string json=JsonUtility.ToJson(report,true);File.WriteAllText(Path.Combine(Output,name),json);return json;}
        static bool IsTerrain(Collider c)=>c.name.StartsWith("Terrain_",StringComparison.Ordinal);
        static bool Ground(Vector3 p,out RaycastHit hit)
        {
            hit=default;
            foreach(var h in Physics.RaycastAll(new Vector3(p.x,2200,p.z),Vector3.down,4400,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
                if(h.collider.gameObject.scene==Session.gameObject.scene && IsTerrain(h.collider) && h.normal.y>.1f){hit=h;return true;}
            return false;
        }
        static bool Empty(Vector3 centre,Vector3 half,Quaternion rotation)
        {
            return !Physics.OverlapBox(centre,half,rotation,~0,QueryTriggerInteraction.Ignore).Any(c=>
                c.gameObject.scene==Session.gameObject.scene&&!IsTerrain(c)&&c.GetComponentInParent<DemoEscortSceneRoute>()==null &&
                !c.transform.IsChildOf(Seat.Vehicle.transform));
        }
        static bool Walkable(Vector3 p,out Vector3 feet)
        {
            feet=default;if(!Ground(p,out var h)||Vector3.Angle(h.normal,Vector3.up)>18)return false;
            feet=h.point+Vector3.up*.06f;
            return Empty(feet+Vector3.up*.95f,new Vector3(.4f,.85f,.4f),Quaternion.identity);
        }
        static float Length(WorldMacroSheetSO.RouteSpec route)
        {float d=0;for(int i=1;i<route.Points.Length;i++)d+=Vector3.Distance(route.Points[i-1],route.Points[i]);return d;}
        static Vector3 At(WorldMacroSheetSO.RouteSpec route,float fraction,out Vector3 forward)
        {
            float target=Length(route)*Mathf.Clamp01(fraction);forward=Vector3.forward;
            for(int i=1;i<route.Points.Length;i++)
            {
                Vector3 edge=route.Points[i]-route.Points[i-1];float length=edge.magnitude;
                if(target<=length||i==route.Points.Length-1){forward=Vector3.ProjectOnPlane(edge,Vector3.up).normalized;return Vector3.Lerp(route.Points[i-1],route.Points[i],Mathf.Clamp01(target/Mathf.Max(.001f,length)));}target-=length;
            }
            throw new InvalidOperationException("Route contains no segments: "+route.Id);
        }
        static Candidate Examine(int index,WorldMacroSheetSO.RouteSpec route,float fraction,float side)
        {
            var reserved=At(route,fraction,out var forward);var q=Quaternion.LookRotation(forward,Vector3.up);var right=q*Vector3.right;
            var c=new Candidate{id=Ids[index],route=route.Id,fraction=fraction,side=side,reservedRoutePoint=reserved,yaw=q.eulerAngles.y};
            Vector3 shoulder=reserved+right*side*(route.Width*.5f+1.3f);
            if(!Walkable(shoulder,out var interaction)){c.rejection="No clear grounded road shoulder";return c;}
            // Reuse the vehicle's actual four-wheel/hull/exit/river placement checks; no guessed wheel Y.
            if(!Summon.TryFindPlacement(interaction,forward,out var parking,out var reason)) {c.rejection="Vehicle placement: "+reason;return c;}
            if(parking.Route!=route.Id||Vector2.Distance(new Vector2(parking.Position.x,parking.Position.z),new Vector2(reserved.x,reserved.z))>35)
            {c.rejection="Placement moved outside this bounded route stop";return c;}
            c.parkingPosition=parking.Position;c.parkingRotation=parking.Rotation;c.slope=parking.Slope;
            Vector3 parkingForward=Vector3.ProjectOnPlane(parking.Rotation*Vector3.forward,Vector3.up).normalized;
            q=Quaternion.LookRotation(parkingForward,Vector3.up);right=q*Vector3.right;c.yaw=q.eulerAngles.y;
            shoulder=parking.Position+right*side*(route.Width*.5f+1.3f);
            if(!Walkable(shoulder,out interaction)||!Walkable(shoulder-parkingForward*2.1f,out c.measuredCheckpoint)||
                !Walkable(shoulder+parkingForward*.7f-right*side*.7f,out c.measuredCompanion)||
                !Walkable(shoulder-parkingForward*.7f-right*side*.7f,out c.measuredCargo))
            {c.rejection="No separate passenger/cargo/checkpoint waiting spaces";return c;}
            // The start's F point is at the vehicle, while its recovery checkpoint is safely on foot at the shoulder.
            if(index==0)
            {if(!Ground(parking.Position,out var h)){c.rejection="Missing start support";return c;}c.measuredInteraction=h.point+Vector3.up*.06f;}
            else c.measuredInteraction=interaction;
            for(int i=0;i<=8;i++)
                if(!Walkable(Vector3.Lerp(parking.Position+right*side*2.2f,interaction,i/8f),out _))
                {c.rejection="Door-to-interaction walking corridor is blocked";return c;}
            // Reserve actual attendant/desk space behind the shoulder, without blocking the 8m road.
            if(index>0&&!Empty(interaction+parkingForward*2.2f+Vector3.up*1.1f,new Vector3(1.3f,1f,1.4f),q))
            {c.rejection="Inspection/office desk footprint occupied";return c;}
            if(index==3)
            {
                // The first survey found usable delivery shoulders but no free 12x10m building at
                // exactly one 9m offset. Search a bounded lateral/backward frontage without relaxing
                // floor relief, obstruction or road-clearance limits and without moving terrain.
                bool found=false;float best=float.PositiveInfinity;c.officeSpread=float.PositiveInfinity;
                foreach(float lateral in new[]{9f,12f,15f})foreach(float longitudinal in new[]{0f,-5f,5f,-10f,10f})
                {
                    Vector3 office=interaction+right*side*lateral+parkingForward*longitudinal;
                    float low=float.PositiveInfinity,high=float.NegativeInfinity;bool supported=true;
                    for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)
                    {if(!Ground(office+q*new Vector3(x*6,0,z*5),out var h)){supported=false;continue;}low=Mathf.Min(low,h.point.y);high=Mathf.Max(high,h.point.y);}
                    if(!supported)continue;float spread=high-low;c.officeSpread=Mathf.Min(c.officeSpread,spread);office.y=high+.03f;
                    if(spread>1.2f||!Empty(office+Vector3.up*3,new Vector3(6.5f,3,5.5f),q))continue;
                    Vector3 frontage=office-right*side*6.8f;bool connected=true;
                    for(int p=0;p<=12;p++)if(!Walkable(Vector3.Lerp(interaction,frontage,p/12f),out _)){connected=false;break;}
                    if(!connected)continue;float value=spread*3+Mathf.Abs(longitudinal)*.1f+lateral*.1f;
                    if(value>=best)continue;found=true;best=value;c.measuredOffice=office;c.officeSpread=spread;
                }
                if(!found){c.rejection="No supported, unoccupied headquarters frontage in bounded 9-15m side / +/-10m search";return c;}
            }
            c.suitable=true;c.score=Mathf.Abs(fraction-new[]{.015f,.7f,.82f,.08f}[index])*100+c.slope+c.officeSpread*3;return c;
        }
        static Report Survey()
        {
            Physics.SyncTransforms();var all=new List<Candidate>();var selected=new List<Candidate>();float distance=0;
            var centres=new[]{.015f,.7f,.82f,.08f};var ranges=new[]{.025f,.25f,.13f,.1f};
            for(int i=0;i<4;i++)
            {
                var route=WorldMacroBuilder.Sheet.Routes.Single(r=>r.Id==Routes[i]);
                if(!route.Carriage||route.Width<7.5f)throw new InvalidOperationException("Escort requires the existing wide carriage route: "+route.Id);
                distance+=Length(route);
                for(int n=-6;n<=6;n++)foreach(float side in new[]{-1f,1f})
                {float f=Mathf.Clamp(centres[i]+ranges[i]*n/6,.001f,.99f);all.Add(Examine(i,route,f,side));}
                var best=all.Where(c=>c.id==Ids[i]&&c.suitable).OrderBy(c=>c.score).FirstOrDefault();if(best!=null)selected.Add(best);
            }
            return new Report{status=selected.Count==4?"SURVEY_ONLY":"NO_SAFE_STOP",scope="Read-only actual vehicle placement, terrain/door/waiting/office checks. Reserved route Y is distinct from measured ground. No drive-through proof.",
                scene=Session.gameObject.scene.path,selected=selected.ToArray(),candidates=all.ToArray(),roadMetres=distance,
                companionSource="Existing Chapter2 wangso_w1 / Replaceable_NPC_Visual",cargoSource="Existing Chapter2 Demo_SealedCargo / "+BoxSource,
                originalCompanion=Companion.position,originalCargo=Cargo.position,driverSeatLocal=Seat.Vehicle.transform.InverseTransformPoint(Seat.SeatSocket.position)};
        }
        static Transform Node(Transform parent,string name,Vector3 point,float yaw=0)
        {var t=new GameObject(name).transform;t.SetParent(parent,false);t.SetPositionAndRotation(point,Quaternion.Euler(0,yaw,0));return t;}
        static Transform Socket(Transform parent,string name,Vector3 local)
        {var t=parent.Find(name);if(t==null){t=new GameObject(name).transform;t.SetParent(parent,false);}Undo.RecordObject(t,"Escort socket");t.localPosition=local;t.localRotation=Quaternion.identity;t.localScale=Vector3.one;return t;}
        static void Reference(string field,Object value)
        {var so=new SerializedObject(Session);var p=so.FindProperty(field);if(p==null)throw new InvalidOperationException("Session escort API not compiled: "+field);p.objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
        static void RequireApi()
        {
            var so=new SerializedObject(Session);foreach(string f in new[]{"DemoEscortCompanion","DemoEscortCargo","DemoEscortSeat","DemoEscortPassengerSocket","DemoEscortCargoSocket","DemoEscortSummon","DemoEscortStartCheckpointId"})
                if(so.FindProperty(f)==null)throw new InvalidOperationException("Compile runtime escort binding before applying: "+f);
        }
        static string Apply()
        {
            RequireApi();var survey=Survey();Save("escort_scene_survey.json",survey);
            if(survey.selected.Length!=4)throw new InvalidOperationException("All four safe stops required; nothing applied. Inspect escort_scene_survey.json.");
            foreach(string p in new[]{BoxSource,PavilionSource,SackSource})if(AssetDatabase.LoadAssetAtPath<GameObject>(p)==null)throw new InvalidOperationException("Existing source missing: "+p);
            var visual=Companion.GetComponent<WorldMacroContentPoint>()?.Visual;
            if(visual==null)throw new InvalidOperationException("Actual existing Wangso visual required");
            var old=GameObject.Find(RootName);var root=new GameObject(RootName+"_Pending");var route=root.AddComponent<DemoEscortSceneRoute>();
            var stops=new List<DemoEscortStop>();
            try
            {
                foreach(var c in survey.selected)
                {
                    int index=Array.IndexOf(Ids,c.id);var stopRoot=Node(root.transform,c.id,c.measuredInteraction,c.yaw);var q=Quaternion.Euler(0,c.yaw,0);
                    var stop=new DemoEscortStop{Id=c.id,RouteId=c.route,CheckpointId=CheckpointIds[index],
                        Interaction=Node(stopRoot,"Interaction",c.measuredInteraction,c.yaw),Parking=Node(stopRoot,"Parking",c.parkingPosition,c.yaw),
                        CompanionWait=Node(stopRoot,"CompanionWait",c.measuredCompanion,c.yaw),CargoWait=Node(stopRoot,"CargoWait",c.measuredCargo,c.yaw),
                        Checkpoint=Node(stopRoot,"Checkpoint",c.measuredCheckpoint,c.yaw)};
                    stop.Parking.rotation=c.parkingRotation;stops.Add(stop);
                    if(index==0)continue;
                    Vector3 attendant=c.measuredInteraction+q*Vector3.forward*1.55f;
                    if(!Ground(attendant,out var attendantGround))throw new InvalidOperationException("Attendant floor lost");
                    var npc=Node(stopRoot,index==3?"GuesthouseClerk_ExistingProxy":"InspectionOfficer_ExistingProxy",attendantGround.point,c.yaw+180);
                    var copy=Object.Instantiate(visual,npc);copy.name="Replaceable_NPC_Visual";copy.localPosition=Vector3.up*.85f;copy.localRotation=Quaternion.identity;copy.gameObject.SetActive(true);
                    foreach(var collider in copy.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
                    var capsule=npc.gameObject.AddComponent<CapsuleCollider>();capsule.height=1.7f;capsule.radius=.27f;capsule.center=Vector3.up*.85f;
                    Vector3 desk=c.measuredInteraction+q*new Vector3(c.side*.9f,0,2.3f);Ground(desk,out var deskGround);
                    WorldMacroVisualCorridorAuthoring.PlaceSource(stopRoot,"ExistingInspectionDesk",BoxSource,deskGround.point,new Vector3(.75f,.8f,.75f),c.yaw,true);
                    if(index==3)
                    {
                        var office=WorldMacroVisualCorridorAuthoring.PlaceSource(stopRoot,"ExistingGuesthouseHeadquarters",PavilionSource,c.measuredOffice,new Vector3(12,0,10),c.yaw,false);
                        foreach(var filter in office.GetComponentsInChildren<MeshFilter>())if(filter.GetComponent<MeshCollider>()==null){var collider=filter.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=filter.sharedMesh;}
                        Vector3 supplies=c.measuredInteraction+q*new Vector3(c.side*2.6f,0,2.3f);Ground(supplies,out var supplyGround);
                        WorldMacroVisualCorridorAuthoring.PlaceSource(stopRoot,"ExistingGuesthouseSupplies",SackSource,supplyGround.point,new Vector3(.8f,1,.8f),c.yaw,false);
                    }
                }
                route.Stops=stops.ToArray();Physics.SyncTransforms();
                // Verify the introduced props did not consume the actual interaction/checkpoint space.
                foreach(var stop in stops)if(!FloorClearOfNewProps(stop.Interaction,root.transform)||!FloorClearOfNewProps(stop.Checkpoint,root.transform))
                    throw new InvalidOperationException("New prop occupies authored interaction/checkpoint: "+stop.Id);
                var passenger=Socket(Seat.Vehicle.BodyVisualRoot,"DemoEscortPassengerSocket",new Vector3(-.48f,.92f,-.9f));
                var cargo=Socket(Seat.Vehicle.BodyVisualRoot,"DemoEscortCargoSocket",new Vector3(.48f,.98f,-1.05f));
                Undo.RecordObject(Session,"Bind existing escort actors and vehicle");
                Reference("DemoEscortCompanion",Companion);Reference("DemoEscortCargo",Cargo);Reference("DemoEscortSeat",Seat);
                Reference("DemoEscortPassengerSocket",passenger);Reference("DemoEscortCargoSocket",cargo);Reference("DemoEscortSummon",Summon);
                var so=new SerializedObject(Session);so.FindProperty("DemoEscortStartCheckpointId").stringValue="escort_start";so.ApplyModifiedPropertiesWithoutUndo();
                // The disabled agent is authored now so adding an enabled component never races the runtime NavMesh load.
                var navigator=Companion.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if(navigator==null)navigator=Companion.gameObject.AddComponent<UnityEngine.AI.NavMeshAgent>();
                navigator.enabled=false;navigator.height=1.7f;navigator.radius=.27f;navigator.baseOffset=0;
                var presentation=root.AddComponent<DemoEscortPresentation>();
                if(!presentation.Configure(Session,route))throw new InvalidOperationException("Actual escort presentation references rejected.");
                var content=Session.Content;Undo.RecordObject(content,"Register escort interactions and recovery checkpoints");
                var points=content.Points.Where(p=>!Ids.Contains(p.Id)&&!CheckpointIds.Skip(1).Contains(p.Id)).ToList();
                var labels=new[]{"왕소와 봉인 화물을 태우고 출발","첫 검문에서 차패로 보증","황경 앞 검문에서 차패 제시","객주 본점에 봉인 화물 인도"};
                foreach(var stop in stops)
                {
                    int index=Array.IndexOf(Ids,stop.Id);points.Add(new PrologueContentSO.Point{Id=stop.Id,Kind=PrologueInteractionKind.Conversation,Position=stop.Interaction.position,Radius=index==0?4.5f:3f,Prompt=labels[index],Text=labels[index]});
                    if(index>0)points.Add(new PrologueContentSO.Point{Id=stop.CheckpointId,Kind=PrologueInteractionKind.Rest,Position=stop.Checkpoint.position,Radius=1.6f,Prompt="왕소와 길가에서 잠시 쉬기",Text="봉인 화물을 확인하고 다시 길을 나선다."});
                }
                content.Points=points.ToArray();content.Checkpoints=(content.Checkpoints??Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>()).Where(p=>!CheckpointIds.Contains(p.Id))
                    .Concat(stops.Select(s=>new WorldMacroPlaytestSO.CheckpointSpec{Id=s.CheckpointId,Label=s.Id=="escort_start"?"상경 출발 정차소":"호송 길가 쉼터",Feet=s.Checkpoint.position,Yaw=s.Checkpoint.eulerAngles.y,Shop=false})).ToArray();
                if(old!=null)Undo.DestroyObjectImmediate(old);root.name=RootName;Undo.RegisterCreatedObjectUndo(root,"Author bounded escort road stops");
                EditorUtility.SetDirty(Session);EditorUtility.SetDirty(content);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);
                return Save("escort_scene_audit.json",Audit());
            }
            catch{if(root!=null&&root.name.EndsWith("_Pending",StringComparison.Ordinal))Object.DestroyImmediate(root);throw;}
        }
        static bool FloorClearOfNewProps(Transform marker,Transform ownedRoot)
        {
            return !Physics.OverlapCapsule(marker.position+Vector3.up*.34f,marker.position+Vector3.up*1.45f,.28f,~0,QueryTriggerInteraction.Ignore)
                .Any(c=>c.transform.IsChildOf(ownedRoot));
        }
        static Report Audit()
        {
            Physics.SyncTransforms();var checks=new List<Check>();void Check(string name,bool pass,string detail)=>checks.Add(new Check{name=name,status=pass?"PASS":"FAIL",detail=detail});
            var root=GameObject.Find(RootName);var marker=root!=null?root.GetComponent<DemoEscortSceneRoute>():null;
            Check("owned-route",marker!=null&&marker.Stops.Length==4,"Exactly four ordered stops; existing geography unchanged.");
            if(marker!=null)
            {
                foreach(var stop in marker.Stops)
                {
                    var p=Session.Content.Points.Where(p=>p.Id==stop.Id).ToArray();var cp=Session.Content.Checkpoints.Where(c=>c.Id==stop.CheckpointId).ToArray();
                    Check(stop.Id+"-content",p.Length==1&&Vector3.Distance(p[0].Position,stop.Interaction.position)<.01f,"Unique actual Content interaction matches scene reference.");
                    Check(stop.Id+"-checkpoint",cp.Length==1&&Vector3.Distance(cp[0].Feet,stop.Checkpoint.position)<.01f,"Recovery uses actual safe on-foot checkpoint, not vehicle root Y.");
                    Check(stop.Id+"-wait-spaces",Walkable(stop.CompanionWait.position,out _)&&Walkable(stop.CargoWait.position,out _)&&FloorClearOfNewProps(stop.Interaction,root.transform)&&FloorClearOfNewProps(stop.Checkpoint,root.transform),"Ground support and introduced-prop clearance.");
                    Check(stop.Id+"-carriage",WorldMacroBuilder.Sheet.Routes.Any(r=>r.Id==stop.RouteId&&r.Carriage&&r.Width>=7.5f),stop.RouteId);
                }
                Check("inspection-spacing",Vector3.Distance(marker.Stops[1].Interaction.position,marker.Stops[2].Interaction.position)>100,"Two geographically separate inspections.");
                Check("delivery-before-gate",Vector3.Distance(marker.Stops[3].Interaction.position,WorldMacroBuilder.Sheet.Routes.Single(r=>r.Id=="Road_SouthPost_Gate").Points[0])<200,"Bounded SouthPost roadside headquarters; South Gate boss not modified.");
            }
            var serialized=new SerializedObject(Session);bool Ref(string field,Object expected)=>serialized.FindProperty(field)?.objectReferenceValue==expected;
            Check("actual-actors",Ref("DemoEscortCompanion",Companion)&&Ref("DemoEscortCargo",Cargo),"Original scene actor/cargo, not marker proxies, are wired to Session.");
            Check("actual-seat-summon",Ref("DemoEscortSeat",Seat)&&Ref("DemoEscortSummon",Summon),"Existing vehicle driving/seat/recall services retained.");
            var presentation=root!=null?root.GetComponent<DemoEscortPresentation>():null;
            Check("presentation-bound",presentation!=null&&presentation.Session==Session&&presentation.Route==marker,
                "Independent root owns real following/attachment/recall presentation; no actor is moved during authoring.");
            var passenger=Seat.Vehicle.BodyVisualRoot.Find("DemoEscortPassengerSocket");var cargo=Seat.Vehicle.BodyVisualRoot.Find("DemoEscortCargoSocket");
            Check("exclusive-sockets",passenger!=null&&cargo!=null&&passenger!=Seat.SeatSocket&&cargo!=Seat.SeatSocket&&Vector3.Distance(passenger.position,cargo.position)>.8f,
                "Dedicated passenger/cargo sockets preserve the existing driver camera seat.");
            return new Report{status=checks.All(c=>c.status=="PASS")?"PASS_AUTHORING_ONLY":"FAIL",scene=Session.gameObject.scene.path,
                scope="Static authoring only. No campaign stage enabled. Live staging, seated attachment, safe exit, recall/death/save and exact visual fit need runtime verification.",checks=checks.ToArray(),
                originalCompanion=Companion.position,originalCargo=Cargo.position,passengerLocal=passenger!=null?passenger.localPosition:Vector3.zero,cargoLocal=cargo!=null?cargo.localPosition:Vector3.zero,
                driverSeatLocal=Seat.Vehicle.transform.InverseTransformPoint(Seat.SeatSocket.position)};
        }
    }
}
