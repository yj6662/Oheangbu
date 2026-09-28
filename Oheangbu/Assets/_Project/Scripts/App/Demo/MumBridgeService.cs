using System;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Data.World;
using Oheangbu.Spellcraft;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.App.Demo
{
    public readonly struct MumBridgePlan
    {
        public readonly Vector3 Start,End;
        public readonly float Span,Width;
        public MumBridgePlan(Vector3 start,Vector3 end,float width)
        {Start=start;End=end;Width=width;Span=Vector2.Distance(new Vector2(start.x,start.z),new Vector2(end.x,end.z));}
    }

    // Owns temporary support only. Recognition, one ink payment and saved unlocks remain with their existing owners.
    [DisallowMultipleComponent]
    public sealed class MumBridgeService : MonoBehaviour
    {
        public MumBridgeProfileSO Profile {get;private set;}
        public string LastFailure {get;private set;}
        public int ActiveCount=>bridges.Count;
        public bool HasPrepared=>prepared!=null;
        public bool IsUnlocked=>isActiveAndEnabled&&Profile!=null&&unlocked!=null&&unlocked();
        public IReadOnlyList<MumBridgeBody> Bridges=>bridges;
        public Func<bool> CombatBlocked;
        public Func<string> CurrentArea;
        public Func<bool> PreviewRequested;
        public event Action<string> PlacementRejected;
        readonly List<MumBridgeBody> bridges=new List<MumBridgeBody>();
        readonly HashSet<string> visitedAreas=new HashSet<string>();
        readonly HashSet<Collider> landingSupports=new HashSet<Collider>();
        CharacterController body;
        PlayerMotor motor;
        PlayerVitals vitals;
        Camera view;
        WorldTerrainQuery traversal;
        Func<bool> unlocked,seated,paused;
        MumBridgeBody prepared;
        MumBridgePlan preparedPlan;
        Vector3 preparedTarget,preparedFeet;
        string currentArea;
        RaycastHit[] hits;
        readonly Collider[] overlaps=new Collider[128];
        bool destroying;
        LineRenderer preview;
        float nextPreview;

        public void Configure(CharacterController player,PlayerMotor playerMotor,PlayerVitals health,Camera camera,
            WorldTerrainQuery terrain,MumBridgeProfileSO profile,Func<bool> hasMum,Func<bool> isSeated,Func<bool> isPaused)
        {
            DetachAll();Unbind();body=player;motor=playerMotor;vitals=health;view=camera;traversal=terrain;
            Profile=profile;unlocked=hasMum;seated=isSeated;paused=isPaused;visitedAreas.Clear();currentArea=null;Bind();
        }
        void OnEnable()=>Bind();
        void OnDisable(){Unbind();DetachAll();if(preview!=null)preview.enabled=false;}
        void OnDestroy(){destroying=true;Unbind();DetachAll();if(preview!=null)FieldSpellService.Remove(preview.gameObject);}
        void Bind(){if(vitals!=null){vitals.Died-=OnDeath;vitals.Died+=OnDeath;}}
        void Unbind(){if(vitals!=null)vitals.Died-=OnDeath;}
        void OnDeath()=>ClearAll(true);
        bool CanCast=>IsUnlocked&&Profile.IsValid&&body!=null&&body.enabled&&body.gameObject.activeInHierarchy&&
            body.gameObject.scene==gameObject.scene&&vitals!=null&&vitals.Hp01>0&&!(seated?.Invoke()??false)&&
            !(paused?.Invoke()??false)&&!(CombatBlocked?.Invoke()??false)&&
            (motor==null||motor.isActiveAndEnabled&&motor.IsLocomotionGrounded&&!motor.IsSitting&&!motor.IsDodging&&!motor.EnvironmentalInputBlocked);

        public bool TryPrepare(SpellCast cast,out string failure)
        {
            if(!Aim(out var target))return Fail("반대편의 마른 지면을 바라보고 뭄을 쓰자.",out failure);
            return TryPrepareTo(cast,target,out failure);
        }
        // Explicit target is also used by detached physics fixtures; every gameplay and support check still applies.
        public bool TryPrepareTo(SpellCast cast,Vector3 target,out string failure)
        {
            LastFailure=null;
            if(cast.Letter!='뭄'||cast.Kind!=SpellKind.Field||cast.Element!=Element.Earth)return Fail("뭄 다리 술식이 아니다.",out failure);
            if(!CanCast)return Fail("뭄을 얻은 뒤, 전투 밖의 마른 지면에서 쓸 수 있다.",out failure);
            if(prepared!=null||bridges.Count>=Profile.MaximumActive)return Fail("이미 준비 중이거나 놓을 수 있는 다리가 가득 찼다.",out failure);
            if(!TryPlan(target,out var plan,out failure)){LastFailure=failure;PlacementRejected?.Invoke(failure);return false;}
            try
            {
                preparedPlan=plan;preparedTarget=target;preparedFeet=FieldSpellService.Feet(body);
                prepared=CreateBody(plan);prepared.gameObject.SetActive(false);failure=null;return true;
            }
            catch(Exception exception)
            {CancelPrepared();return Fail("다리를 만들 수 없다: "+exception.Message,out failure);}
        }
        public bool CommitPrepared()
        {
            if(prepared==null||!CanCast||bridges.Count>=Profile.MaximumActive||Vector3.Distance(FieldSpellService.Feet(body),preparedFeet)>.15f||
                !TryPlan(preparedTarget,out var now,out _)||Vector3.Distance(now.Start,preparedPlan.Start)>.03f||Vector3.Distance(now.End,preparedPlan.End)>.03f)
            {CancelPrepared();return false;}
            var accepted=prepared;prepared=null;accepted.AreaId=CurrentArea?.Invoke()??gameObject.scene.name;
            accepted.gameObject.SetActive(true);bridges.Add(accepted);Physics.SyncTransforms();return true;
        }
        public void CancelPrepared()
        {if(prepared==null)return;prepared.Dispose();prepared=null;}
        public bool TryPreview(out MumBridgePlan plan,out string failure)
        {
            plan=default;if(!CanCast){failure="지금은 뭄을 쓸 수 없다.";return false;}
            if(!Aim(out var target)){failure="반대편 지면이 보이지 않는다.";return false;}
            return TryPlan(target,out plan,out failure);
        }
        bool Aim(out Vector3 point)
        {
            point=default;if(view==null||Profile==null||body==null)return false;
            var ray=view.ViewportPointToRay(new Vector3(.5f,.5f,0));
            int count=ScenePhysicsQuery.RaycastAll(gameObject.scene,ray.origin,ray.direction,Profile.MaximumSpan+5,~0,ref hits);
            float nearest=float.PositiveInfinity;RaycastHit selected=default;
            for(int i=0;i<count;i++)if(!Ignore(hits[i].collider)&&hits[i].distance<nearest){nearest=hits[i].distance;selected=hits[i];}
            if(!float.IsFinite(nearest)||!PermanentDry(selected))return false;point=selected.point;return true;
        }
        bool TryPlan(Vector3 target,out MumBridgePlan plan,out string failure)
        {
            plan=default;failure=null;landingSupports.Clear();
            if(!Finite(target)||!Ground(FieldSpellService.Feet(body),out var start)||
                Mathf.Abs(FieldSpellService.Feet(body).y-start.point.y)>.16f||!Ground(target,out var end))
            {failure="다리 양끝에는 안정된 마른 지면이 필요하다.";return false;}
            Vector3 direction=end.point-start.point;direction.y=0;float length=direction.magnitude;
            if(length<Profile.MinimumSpan||length>Profile.MaximumSpan){failure="다리의 간격은 4~40미터여야 한다.";return false;}
            float rise=Mathf.Abs(end.point.y-start.point.y);
            if(rise>Profile.MaximumEndHeightDifference||Mathf.Atan2(rise,length)*Mathf.Rad2Deg>Profile.MaximumSlope)
            {failure="반대편 지면의 높이 차이가 너무 크다.";return false;}
            direction/=length;Vector3 right=Vector3.Cross(Vector3.up,direction);
            if(!Landing(start.point,direction,right)||!Landing(end.point,-direction,right))
            {failure="다리 양끝의 폭이나 착지 공간이 부족하다.";return false;}
            var a=start.point+Vector3.up*.025f;var b=end.point+Vector3.up*.025f;
            var forward=(b-a).normalized;var rotation=Quaternion.LookRotation(forward,Vector3.up);
            var up=rotation*Vector3.up;
            float playerHeight=body.height+.12f,deckLength=Vector3.Distance(a,b),halfWidth=Profile.Width*.5f+.07f;
            float landing=Mathf.Min(Profile.LandingOverlap,length*.25f)*deckLength/length;
            // The central walking surface/head space stays clear. At the two landing lips, the existing
            // bank height tolerance permits a small traversable step on only the proven ground collider.
            if(Blocked((a+b)*.5f+up*(playerHeight*.5f),new Vector3(halfWidth,playerHeight*.5f,(deckLength-landing*2)*.5f),rotation)||
                LandingHeadBlocked(a+forward*(landing*.5f),landing,halfWidth,playerHeight,up,rotation)||
                LandingHeadBlocked(b-forward*(landing*.5f),landing,halfWidth,playerHeight,up,rotation))
            {failure="다리가 지나갈 공간이나 머리 위가 막혀 있다.";return false;}
            // Earth can join the supporting bank below the top. Only colliders proven to support the dry
            // landing may intersect these bounded end zones; the central slab must remain wholly clear.
            float join=Mathf.Min(Profile.MaximumBankEmbedApproach,length*.25f)*deckLength/length;
            if(Blocked((a+b)*.5f-up*(Profile.Thickness*.5f),new Vector3(halfWidth,Profile.Thickness*.5f,(deckLength-join*2)*.5f),rotation)||
                join>0&&(BankJoinBlocked(a+forward*(join*.5f)-up*(Profile.Thickness*.5f),new Vector3(halfWidth,Profile.Thickness*.5f,join*.5f),rotation)||
                          BankJoinBlocked(b-forward*(join*.5f)-up*(Profile.Thickness*.5f),new Vector3(halfWidth,Profile.Thickness*.5f,join*.5f),rotation)))
            {failure="다리 아래가 막혀 있거나 강둑과 안전하게 이을 수 없다.";return false;}
            // A bridge must cross a genuine unsupported interval, rather than cover a road with a new floor.
            float gap=0,longest=0;
            for(float d=Profile.LandingOverlap;d<=length-Profile.LandingOverlap;d+=.25f)
            {
                var p=Vector3.Lerp(a,b,d/length);
                if(!Ground(p,out var support)||Mathf.Abs(support.point.y-p.y)>.18f){gap+=.25f;longest=Mathf.Max(longest,gap);}else gap=0;
            }
            if(longest<1f){failure="다리를 놓을 끊어진 지형이 없다.";return false;}
            plan=new MumBridgePlan(a,b,Profile.Width);return true;
        }
        bool Landing(Vector3 point,Vector3 towardsGap,Vector3 right)
        {
            foreach(float across in new[]{-.5f,0,.5f})foreach(float along in new[]{-Profile.LandingOverlap,0f})
            {
                var at=point+right*(Profile.Width*across)+towardsGap*along;
                if(!Ground(at,out var hit)||Mathf.Abs(hit.point.y-point.y)>Profile.BankHeightTolerance)return false;
                landingSupports.Add(hit.collider);
                if(Blocked(hit.point+Vector3.up*(body.height*.5f+.06f),new Vector3(.12f,body.height*.5f,.12f),Quaternion.identity))return false;
            }
            return true;
        }
        bool Ground(Vector3 point,out RaycastHit selected)
        {
            selected=default;int count=ScenePhysicsQuery.RaycastAll(gameObject.scene,point+Vector3.up*.3f,Vector3.down,.65f,~0,ref hits);
            float nearest=float.PositiveInfinity;
            for(int i=0;i<count;i++)if(!Ignore(hits[i].collider)&&hits[i].distance<nearest){selected=hits[i];nearest=hits[i].distance;}
            return float.IsFinite(nearest)&&PermanentDry(selected);
        }
        bool PermanentDry(RaycastHit hit)=>hit.collider!=null&&hit.collider.attachedRigidbody==null&&
            hit.collider.GetComponentInParent<WorldTemporarySupport>()==null&&hit.normal.y>=Mathf.Cos(Profile.MaximumBankSlope*Mathf.Deg2Rad)&&
            (traversal==null||traversal.IsPermanentDrySupport(hit.point,hit.collider));
        bool Blocked(Vector3 center,Vector3 half,Quaternion rotation)
        {
            int count=gameObject.scene.GetPhysicsScene().OverlapBox(center,half,overlaps,rotation,~0,QueryTriggerInteraction.Ignore);
            if(count==overlaps.Length)return true;
            for(int i=0;i<count;i++)if(!Ignore(overlaps[i]))return true;return false;
        }
        bool BankJoinBlocked(Vector3 center,Vector3 half,Quaternion rotation)
        {
            int count=gameObject.scene.GetPhysicsScene().OverlapBox(center,half,overlaps,rotation,~0,QueryTriggerInteraction.Ignore);
            if(count==overlaps.Length)return true;
            for(int i=0;i<count;i++)
            {
                var collider=overlaps[i];if(Ignore(collider))continue;
                if(!landingSupports.Contains(collider)||collider.attachedRigidbody!=null||collider.GetComponentInParent<WorldTemporarySupport>()!=null)return true;
            }
            return false;
        }
        bool LandingHeadBlocked(Vector3 center,float length,float halfWidth,float height,Vector3 up,Quaternion rotation)
        {
            float tolerance=Mathf.Min(Profile.BankHeightTolerance,body.stepOffset);
            return BankJoinBlocked(center+up*(tolerance*.5f),new Vector3(halfWidth,tolerance*.5f,length*.5f),rotation)||
                Blocked(center+up*((height+tolerance)*.5f),new Vector3(halfWidth,(height-tolerance)*.5f,length*.5f),rotation);
        }
        bool Ignore(Collider collider)=>collider==null||body!=null&&collider.transform.IsChildOf(body.transform)||prepared!=null&&collider.transform.IsChildOf(prepared.transform);
        static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
        bool Fail(string reason,out string failure){failure=LastFailure=reason;PlacementRejected?.Invoke(reason);return false;}

        MumBridgeBody CreateBody(MumBridgePlan plan)
        {
            var go=new GameObject("Mum295_EarthBridge");SceneManager.MoveGameObjectToScene(go,gameObject.scene);go.SetActive(false);
            try
            {
                go.transform.SetPositionAndRotation(plan.Start,Quaternion.LookRotation(plan.End-plan.Start,Vector3.up));
                var mesh=BuildMesh(Vector3.Distance(plan.Start,plan.End),plan.Width,Profile.Thickness);
                var result=go.AddComponent<MumBridgeBody>();result.Mesh=mesh;result.Plan=plan;
                go.AddComponent<WorldTemporarySupport>();
                var visible=new GameObject("Earth");visible.transform.SetParent(go.transform,false);
                visible.AddComponent<MeshFilter>().sharedMesh=mesh;visible.AddComponent<MeshRenderer>().sharedMaterial=Profile.DeckMaterial;
                if(Profile.SurfaceTile!=null&&Profile.SurfaceMaterials!=null&&Profile.SurfaceMaterials.Length==Profile.SurfaceTile.subMeshCount)
                {
                    var surface=new GameObject("SourceStonePaving296");surface.transform.SetParent(visible.transform,false);
                    result.SurfaceMesh=BuildSurfaceMesh(Profile.SurfaceTile,Vector3.Distance(plan.Start,plan.End),plan.Width,Profile.SurfaceTileLength);
                    surface.AddComponent<MeshFilter>().sharedMesh=result.SurfaceMesh;
                    surface.AddComponent<MeshRenderer>().sharedMaterials=Profile.SurfaceMaterials;
                }
                result.Visual=visible.transform;result.Support=go.AddComponent<MeshCollider>();result.Support.sharedMesh=mesh;result.Support.enabled=false;
                result.FormationSeconds=Profile.FormationSeconds;result.Sample(0);return result;
            }
            catch{FieldSpellService.Remove(go);throw;}
        }
        static Mesh BuildMesh(float length,float width,float thickness)
        {
            int sections=Mathf.CeilToInt(length/.8f);var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            for(int i=0;i<=sections;i++)
            {
                float z=length*i/sections;float chip=.025f+.045f*(.5f+.5f*Mathf.Sin(i*2.43f));
                vertices.Add(new Vector3(-width*.5f,0,z));vertices.Add(new Vector3(width*.5f,0,z));
                vertices.Add(new Vector3(-width*.5f-chip,-thickness,z));vertices.Add(new Vector3(width*.5f+chip,-thickness,z));
                uv.Add(new Vector2(0,z));uv.Add(new Vector2(width,z));uv.Add(new Vector2(0,z));uv.Add(new Vector2(width,z));
                if(i==0)continue;int k=i*4;
                triangles.AddRange(new[]{k-4,k,k+1,k-4,k+1,k-3, k-2,k+2,k,k-2,k,k-4, k-3,k+1,k+3,k-3,k+3,k-1, k-1,k+3,k+2,k-1,k+2,k-2});
            }
            triangles.AddRange(new[]{0,1,3,0,3,2});int last=sections*4;
            triangles.AddRange(new[]{last,last+2,last+3,last,last+3,last+1});
            var mesh=new Mesh{name="Mum295_GeneratedEarth"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        static Mesh BuildSurfaceMesh(Mesh tile,float length,float width,float tileLength)
        {
            var bounds=tile.bounds;int count=Mathf.CeilToInt(length/Mathf.Max(.1f,tileLength));
            var sources=new List<CombineInstance>();var pieces=new List<Mesh>();
            try
            {
                for(int sub=0;sub<tile.subMeshCount;sub++)
                {
                    var repeats=new List<CombineInstance>();
                    for(int i=0;i<count;i++)
                    {
                        float start=length*i/count,end=length*(i+1)/count;
                        var scale=new Vector3(width/Mathf.Max(.001f,bounds.size.x),1,(end-start)/Mathf.Max(.001f,bounds.size.z));
                        var matrix=Matrix4x4.TRS(new Vector3(0,.008f,(start+end)*.5f),Quaternion.identity,scale)*Matrix4x4.Translate(-new Vector3(bounds.center.x,bounds.max.y,bounds.center.z));
                        repeats.Add(new CombineInstance{mesh=tile,subMeshIndex=sub,transform=matrix});
                    }
                    var part=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};part.CombineMeshes(repeats.ToArray(),true,true);pieces.Add(part);
                    sources.Add(new CombineInstance{mesh=part,subMeshIndex=0,transform=Matrix4x4.identity});
                }
                var result=new Mesh{name="Mum296_SourceStoneSurface",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
                result.CombineMeshes(sources.ToArray(),false,true);result.RecalculateBounds();return result;
            }
            finally{foreach(var piece in pieces)FieldSpellService.Remove(piece);}
        }
        void Update(){Tick(Time.deltaTime);DrawPreview();}
        void DrawPreview()
        {
            bool visible=Profile!=null&&(PreviewRequested?.Invoke()??false)&&bridges.Count<Profile.MaximumActive;
            if(!visible){if(preview!=null)preview.enabled=false;return;}
            if(Time.unscaledTime<nextPreview)return;nextPreview=Time.unscaledTime+.08f;
            if(!TryPreview(out var plan,out _)){if(preview!=null)preview.enabled=false;return;}
            if(preview==null)
            {
                var go=new GameObject("Mum295_InkPlacementPreview");SceneManager.MoveGameObjectToScene(go,gameObject.scene);
                preview=go.AddComponent<LineRenderer>();preview.useWorldSpace=true;preview.loop=true;preview.positionCount=4;
                preview.widthMultiplier=.045f;preview.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                preview.receiveShadows=false;preview.sharedMaterial=Profile.PreviewMaterial!=null?Profile.PreviewMaterial:Profile.DeckMaterial;
            }
            var direction=(plan.End-plan.Start).normalized;var side=Vector3.Cross(Vector3.up,direction).normalized*plan.Width*.5f;
            var lift=Vector3.up*.07f;preview.SetPosition(0,plan.Start-side+lift);preview.SetPosition(1,plan.End-side+lift);
            preview.SetPosition(2,plan.End+side+lift);preview.SetPosition(3,plan.Start+side+lift);preview.enabled=true;
        }
        public void Tick(float delta)
        {
            if(!isActiveAndEnabled||destroying)return;
            NotifyArea(CurrentArea?.Invoke());
            if(paused?.Invoke()??false)return;
            if(!float.IsFinite(delta)||delta<=0)return;
            for(int i=bridges.Count-1;i>=0;i--)
            {
                var bridge=bridges[i];if(bridge==null){bridges.RemoveAt(i);continue;}
                bridge.Sample(bridge.Age+delta);
                if(bridge.PendingRemoval&&!bridge.Supports(body)){bridge.Dispose();bridges.RemoveAt(i);}
            }
        }
        public void NotifyArea(string area)
        {
            if(string.IsNullOrEmpty(area)||area==currentArea)return;
            currentArea=area;if(!visitedAreas.Add(area))
                for(int i=bridges.Count-1;i>=0;i--)if(bridges[i]!=null&&bridges[i].AreaId==area)RemoveAt(i,false);
        }
        public void ResetForWorldBoundary()=>ClearAll(false);
        void DetachAll()
        {
            CancelPrepared();
            foreach(var bridge in bridges)
            {
                if(bridge==null)continue;
                if(vitals!=null&&vitals.Hp01>0&&bridge.Supports(body))
                {var hold=bridge.GetComponent<MumBridgeSafetyHold>()??bridge.gameObject.AddComponent<MumBridgeSafetyHold>();hold.Configure(body,vitals,bridge);}
                else bridge.Dispose();
            }
            bridges.Clear();
        }
        void ClearAll(bool force)
        {
            CancelPrepared();for(int i=bridges.Count-1;i>=0;i--)RemoveAt(i,force);
        }
        void RemoveAt(int index,bool force)
        {
            var bridge=bridges[index];
            if(!force&&vitals!=null&&vitals.Hp01>0&&bridge!=null&&bridge.Supports(body))
            {
                if(isActiveAndEnabled&&!destroying){bridge.PendingRemoval=true;return;}
                var hold=bridge.gameObject.AddComponent<MumBridgeSafetyHold>();hold.Configure(body,vitals,bridge);
            }
            else if(bridge!=null)bridge.Dispose();
            bridges.RemoveAt(index);
        }
    }

    public sealed class MumBridgeBody : MonoBehaviour
    {
        [NonSerialized] public Mesh Mesh;
        [NonSerialized] public Mesh SurfaceMesh;
        [NonSerialized] public Transform Visual;
        [NonSerialized] public MeshCollider Support;
        [NonSerialized] public MumBridgePlan Plan;
        [NonSerialized] public string AreaId;
        [NonSerialized] public float FormationSeconds,Age;
        [NonSerialized] public bool PendingRemoval;
        public bool IsComplete=>Age>=FormationSeconds;
        public void Sample(float age)
        {
            Age=Mathf.Max(0,age);float t=Mathf.Clamp01(Age/Mathf.Max(.01f,FormationSeconds));
            if(Visual!=null)Visual.localScale=new Vector3(1,1,Mathf.Max(.001f,Mathf.SmoothStep(0,1,t)));
            if(Support!=null)Support.enabled=t>=1;
        }
        public bool Supports(CharacterController player)
        {
            if(player==null||!player.enabled||Support==null||!Support.enabled)return false;
            var feet=transform.InverseTransformPoint(FieldSpellService.Feet(player));
            return Mathf.Abs(feet.x)<=Plan.Width*.5f+player.radius&&feet.z>=-player.radius&&feet.z<=Vector3.Distance(Plan.Start,Plan.End)+player.radius&&feet.y>=-.15f&&feet.y<=3;
        }
        public void Dispose(){if(Support!=null)Support.enabled=false;gameObject.SetActive(false);FieldSpellService.Remove(gameObject);}
        void OnDestroy(){if(Mesh!=null)FieldSpellService.Remove(Mesh);if(SurfaceMesh!=null)FieldSpellService.Remove(SurfaceMesh);}
    }
    public sealed class MumBridgeSafetyHold : MonoBehaviour
    {
        CharacterController body;PlayerVitals vitals;MumBridgeBody bridge;
        public void Configure(CharacterController player,PlayerVitals health,MumBridgeBody support){body=player;vitals=health;bridge=support;}
        void LateUpdate(){if(bridge==null)return;if(vitals==null||vitals.Hp01<=0||body==null||!body.gameObject.activeInHierarchy||body.gameObject.scene!=gameObject.scene||!bridge.Supports(body))bridge.Dispose();}
    }
}
