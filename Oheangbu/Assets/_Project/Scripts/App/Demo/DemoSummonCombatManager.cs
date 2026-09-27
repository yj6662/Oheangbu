using System;
using System.Collections.Generic;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Oheangbu.App.Demo
{
    /// <summary>One combat summon. Preparation never spends ink or removes the active actor.</summary>
    [DisallowMultipleComponent]
    public sealed partial class DemoSummonCombatManager : MonoBehaviour
    {
        public SummonCombatProfile[] Profiles=Array.Empty<SummonCombatProfile>();
        public CombatLoopWiring Wiring;
        public PlayerVitals PlayerVitals;
        public WorldMacroPalanquinSeat VehicleSeat;
        public GameObject Active=>active?.Root;
        public SummonCombatClock ActiveClock=>active?.Clock;
        public EnemyVitals ActiveTarget=>active?.Target;
        public char ActiveLetter=>active==null?'\0':active.Cast.Letter;
        public float DamageSnapshot=>active?.Power??0;
        public float RootDamageSnapshot=>active?.RootPower??0;
        public SummonRootAttackPlan ActiveRootPlan=>active?.RootPlan;
        public bool ActiveAttackIsRoot=>active!=null&&active.AttackIsRoot;
        public SummonFlameAttackPlan ActiveFlamePlan=>active?.FlamePlan;
        public bool ActiveMovingBackward=>active!=null&&active.MovingBackward;
        public float FlameDamageSnapshot=>active?.FlamePower??0;
        public SummonTigerAttackPlan ActiveTigerPlan=>active?.TigerPlan;
        public float TigerDamageSnapshot=>active?.TigerPower??0;
        public SummonClubAttackPlan ActiveClubPlan=>active?.ClubPlan;
        public float ClubDamageSnapshot=>active?.ClubPower??0;
        public SummonWaterAttackPlan ActiveWaterPlan=>active?.WaterPlan;
        public float WaterDamageSnapshot=>active?.WaterPower??0;
        public bool HasPrepared=>prepared!=null&&prepared.Root!=null;
        public string LastFailure {get;private set;}
        public int PrepareAttempts {get;private set;}
        public int PrepareFailures {get;private set;}
        public int AcceptedSummons {get;private set;}
        public int ReleasedSummons {get;private set;}
        public int PathQueries {get;private set;}
        public int PathFailures {get;private set;}
        public int BlockedMovementSteps {get;private set;}
        public int BlockedRotationSteps {get;private set;}
        public int AttackStarts {get;private set;}
        public int ConsumedStrikes {get;private set;}
        public int ConfirmedHits {get;private set;}
        public float ConfirmedDamage {get;private set;}
        public int RootAttackStarts {get;private set;}
        public int RootAttackCancellations {get;private set;}
        public int FlameAttackStarts {get;private set;}
        public int FlameAttackCancellations {get;private set;}
        public int TigerAttackStarts {get;private set;}
        public int TigerLeapStarts {get;private set;}
        public int TigerAttackCancellations {get;private set;}
        public int TigerClawContacts {get;private set;}
        public int ClubAttackStarts {get;private set;}
        public int ClubAttackCancellations {get;private set;}
        public int ClubContacts {get;private set;}
        public int WaterAttackStarts {get;private set;}
        public int WaterAttackCancellations {get;private set;}
        public int WaterContacts {get;private set;}

        sealed class RootTarget
        {
            public EnemyVitals Target;
            public uint LifeRevision;
            public bool Resolved;
        }

        sealed class Actor
        {
            public GameObject Root;
            public DemoSummonPresentation Presentation;
            public SummonCombatProfile Profile;
            public SummonCombatClock Clock;
            public SpellCast Cast;
            public float Power,RootPower,FlamePower,NextPath,NextTarget,RecoveryUntil,AttackStarted,AttackDuration,PreviousRootFront;
            public EnemyVitals Target,StrikeTarget;
            public uint StrikeRevision;
            public AttackProvenance Strike;
            public readonly NavMeshPath Path=new NavMeshPath();
            public Vector3[] Corners=Array.Empty<Vector3>();
            public int Corner;
            public Vector3 Destination;
            public bool HasPath;
            public SummonRootAttackPlan RootPlan;
            public RootTarget[] RootTargets=Array.Empty<RootTarget>();
            public bool AttackIsRoot,RootReleased;
            public long RootReleaseTargetId;
            public SummonFlameAttackPlan FlamePlan;
            public RootTarget[] FlameTargets=Array.Empty<RootTarget>();
            public bool FlameReleased,KeepingDistance,MovingBackward;
            public long FlameReleaseTargetId;
            public SummonTigerAttackPlan TigerPlan;
            public EnemyVitals TigerTarget;
            public float TigerPower;
            public int TigerNextClaw;
            public bool TigerClawArmed;
            public SummonClubAttackPlan ClubPlan;
            public ClubTarget[] ClubTargets=Array.Empty<ClubTarget>();
            public bool ClubReleased;
            public float ClubPower;
            public long ClubReleaseTargetId;
            public SummonWaterAttackPlan WaterPlan;
            public WaterTarget[] WaterTargets=Array.Empty<WaterTarget>();
            public bool WaterReleased;
            public float WaterPower;
            public long WaterReleaseTargetId;
        }
        Actor active,prepared;
        readonly SummonCombatSlot slot=new SummonCombatSlot();
        NavMeshPath placementPath;
        RaycastHit[] groundHits=new RaycastHit[16],sightHits=new RaycastHit[16];
        readonly Collider[] overlaps=new Collider[64];
        CombatLoopWiring hookedWiring;
        PlayerVitals hookedVitals;
        WorldMacroPalanquinSeat hookedSeat;
        static readonly float[] SpawnAngles={0,30,-30,60,-60,90,-90};

        void OnEnable(){Bind();}
        void Update(){Bind();Tick(Time.deltaTime);}
        void OnDisable(){Clear();Unbind();}
        void OnDestroy(){Clear();Unbind();}
        void Bind()
        {
            // Native Unity objects must be constructed on the main thread, never during field deserialization.
            if(placementPath==null)placementPath=new NavMeshPath();
            if(Wiring==null)Wiring=GetComponent<CombatLoopWiring>();
            if(PlayerVitals==null&&Wiring!=null)PlayerVitals=Wiring.SummonPlayer.GetComponent<PlayerVitals>();
            if(hookedWiring!=Wiring)
            {
                if(hookedWiring!=null){hookedWiring.SummonAccepted-=Accept;if(hookedWiring.SummonCombat==this)hookedWiring.SummonCombat=null;}
                hookedWiring=Wiring;
                if(hookedWiring!=null)
                {
                    if(hookedWiring.SummonCombat!=null&&hookedWiring.SummonCombat!=this)
                    {LastFailure="Another summon manager already owns this combat wiring.";hookedWiring=null;return;}
                    hookedWiring.SummonCombat=this;hookedWiring.SummonAccepted+=Accept;
                }
            }
            if(hookedVitals!=PlayerVitals){if(hookedVitals!=null)hookedVitals.Died-=Clear;hookedVitals=PlayerVitals;if(hookedVitals!=null)hookedVitals.Died+=Clear;}
            if(hookedSeat!=VehicleSeat){if(hookedSeat!=null)hookedSeat.Boarded-=Clear;hookedSeat=VehicleSeat;if(hookedSeat!=null)hookedSeat.Boarded+=Clear;}
        }
        void Unbind()
        {
            if(hookedWiring!=null){hookedWiring.SummonAccepted-=Accept;if(hookedWiring.SummonCombat==this)hookedWiring.SummonCombat=null;}
            if(hookedVitals!=null)hookedVitals.Died-=Clear;
            if(hookedSeat!=null)hookedSeat.Boarded-=Clear;
            hookedWiring=null;hookedVitals=null;hookedSeat=null;
        }
        SummonCombatProfile ProfileFor(char letter)
        {
            SummonCombatProfile found=null;
            foreach(var profile in Profiles??Array.Empty<SummonCombatProfile>())
                if(profile!=null&&profile.Letter!=null&&profile.Letter.Length==1&&profile.Letter[0]==letter)
                {if(found!=null)return null;found=profile;}
            return found;
        }
        // An invalid configured profile still routes to preparation failure instead of silently using legacy VFX.
        public bool Supports(char letter)
        {
            foreach(var profile in Profiles??Array.Empty<SummonCombatProfile>())if(profile!=null&&profile.Letter==letter.ToString())return true;
            return false;
        }
        public bool TryPrepare(SpellCast cast,Vector3 origin,Vector3 forward,out string reason)
        {
            PrepareAttempts++;CancelPrepared();Bind();
            var profile=ProfileFor(cast.Letter);
            if(!isActiveAndEnabled||Wiring==null||hookedWiring!=Wiring||!Wiring.isActiveAndEnabled||Wiring.SummonPlayer==null||
                PlayerVitals!=null&&PlayerVitals.Hp01<=0||VehicleSeat!=null&&VehicleSeat.Occupied)
                return Reject("지금은 소환할 수 없다.",out reason);
            if(profile==null||cast.Kind!=SpellKind.Summon||cast.Element!=profile.Element||!Finite(origin)||!Finite(forward)||!float.IsFinite(cast.Power)||cast.Power<0)
                return Reject("소환 설정을 확인할 수 없다.",out reason);
            if(!profile.TryValidate(out reason,true))return Reject(reason,out reason);
            float basePower=cast.Power*Wiring.SummonDamageScale(cast.Element);
            float power=basePower*profile.DamageMultiplier,rootPower=basePower*profile.RootDamageMultiplier,flamePower=basePower*profile.FlameDamageMultiplier;
            float tigerPower=basePower*profile.TigerClawDamageMultiplier;
            float clubPower=basePower*profile.ClubDamageMultiplier;
            float waterPower=basePower*profile.WaterDamageMultiplier;
            if(!float.IsFinite(power)||power<0||profile.RootAttackEnabled&&(!float.IsFinite(rootPower)||rootPower<0)||
                profile.FlameAttackEnabled&&(!float.IsFinite(flamePower)||flamePower<0)||
                profile.TigerAttackEnabled&&(!float.IsFinite(tigerPower)||tigerPower<0)||
                profile.ClubAttackEnabled&&(!float.IsFinite(clubPower)||clubPower<0)||
                profile.WaterAttackEnabled&&(!float.IsFinite(waterPower)||waterPower<0))return Reject("소환 위력을 계산할 수 없다.",out reason);
            forward=Vector3.ProjectOnPlane(forward,Vector3.up);
            if(forward.sqrMagnitude<.001f)return Reject("소환 방향을 확인할 수 없다.",out reason);
            forward.Normalize();
            if(!TryPlacement(profile,origin,forward,out Vector3 feet,out Quaternion rotation))
                return Reject("앞쪽에 소환수가 설 수 있는 연결된 땅과 공간이 없다.",out reason);
            Actor candidate=null;
            try
            {
                var root=new GameObject("DemoSummon_"+cast.Letter);candidate=new Actor{Root=root};root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root,gameObject.scene);root.transform.SetPositionAndRotation(feet,rotation);
                candidate.Profile=profile;candidate.Cast=cast;candidate.Power=power;candidate.RootPower=rootPower;candidate.FlamePower=flamePower;
                candidate.TigerPower=tigerPower;
                candidate.ClubPower=clubPower;
                candidate.WaterPower=waterPower;
                candidate.Clock=new SummonCombatClock(profile.FormationSeconds,profile.ActivitySeconds,profile.DissolveSeconds);
                candidate.Presentation=root.AddComponent<DemoSummonPresentation>();
                candidate.Presentation.Configure(profile);
                // Only the manager controls motion. Imported physics cannot block traffic or acquire aggro.
                foreach(var collider in root.GetComponentsInChildren<Collider>(true)){collider.enabled=false;SafeDestroy(collider);}
                foreach(var agent in root.GetComponentsInChildren<NavMeshAgent>(true)){agent.enabled=false;SafeDestroy(agent);}
                foreach(var body in root.GetComponentsInChildren<Rigidbody>(true)){body.isKinematic=true;body.detectCollisions=false;SafeDestroy(body);}
                candidate.Presentation.Sample(candidate.Clock,0,0);
                prepared=candidate;LastFailure=null;reason=null;return true;
            }
            catch(Exception exception)
            {
                if(candidate?.Root!=null)SafeDestroy(candidate.Root);
                return Reject("소환 외형을 준비하지 못했다: "+exception.Message,out reason);
            }
        }
        bool Reject(string message,out string reason){PrepareFailures++;LastFailure=reason=message;return false;}
        public void CancelPrepared(){if(prepared?.Root!=null)SafeDestroy(prepared.Root);prepared=null;}
        void Accept(SpellCast cast,Vector3 origin,Vector3 forward)
        {
            if(prepared==null)return;
            if(prepared.Cast.Letter!=cast.Letter||prepared.Cast.Element!=cast.Element||prepared.Cast.Power!=cast.Power)
            {CancelPrepared();return;}
            var candidate=prepared;prepared=null;
            if(candidate.Root==null||!slot.TryReplace(candidate.Clock,true,true)){if(candidate.Root!=null)SafeDestroy(candidate.Root);return;}
            var previous=active;active=candidate;
            candidate.Root.SetActive(true);candidate.Presentation.Sample(candidate.Clock,0,0);
            Release(previous);AcceptedSummons++;
            Wiring.NotifyCombatSummonStarted(cast.Letter,candidate.Root.transform.position);
        }
        public void Clear()
        {
            CancelPrepared();slot.Clear();var previous=active;active=null;Release(previous);
        }
        void Release(Actor actor)
        {
            if(actor==null)return;
            actor.RootPlan?.Cancel();
            actor.FlamePlan?.Cancel();
            actor.TigerPlan?.Cancel();
            actor.ClubPlan?.Cancel();
            actor.WaterPlan?.Cancel();
            actor.Clock.EndImmediately();
            if(actor.Root!=null)
            {
                if(Wiring!=null)Wiring.NotifyCombatSummonReleased(actor.Cast.Letter,actor.Root.transform.position);
                actor.Root.SetActive(false);SafeDestroy(actor.Root);
            }
            ReleasedSummons++;
        }
        public void Tick(float scaledDelta)
        {
            if(!float.IsFinite(scaledDelta)||scaledDelta<0)throw new ArgumentOutOfRangeException(nameof(scaledDelta));
            if(active==null||scaledDelta==0)return;
            if(!isActiveAndEnabled||Wiring==null||!Wiring.isActiveAndEnabled||Wiring.SummonPlayer==null||PlayerVitals!=null&&PlayerVitals.Hp01<=0||VehicleSeat!=null&&VehicleSeat.Occupied)
            {Clear();return;}
            // Small physical steps keep hitch recovery from jumping across thin walls or missing a strike boundary.
            float remaining=scaledDelta;
            while(active!=null&&remaining>0)
            {
                float physicalStep=Mathf.Min(active.Profile.FootprintWidth,active.Profile.FootprintLength)*.25f;
                float speed=active.Profile.FollowSpeed;
                if(active.Profile.TigerAttackEnabled)speed=Mathf.Max(speed,active.Profile.TigerRunSpeed,
                    active.Profile.TigerLeapMaxRange/(active.Profile.TigerLeapLandSeconds-active.Profile.TigerLeapPrepareSeconds));
                float dt=Mathf.Min(.05f,physicalStep/Mathf.Max(.01f,speed),remaining);remaining-=dt;
                TickActor(dt);
            }
        }
        void TickActor(float dt)
        {
            var actor=active;
            if(actor.Root==null){Clear();return;}
            Vector3 before=actor.Root.transform.position;
            actor.MovingBackward=false;
            slot.Advance(dt);
            if(actor.Clock.Phase==SummonPhase.Ended){active=null;Release(actor);return;}
            if(actor.Clock.IsCombatActive)
            {
                if(actor.WaterPlan!=null)TickWaterAttack(actor);
                else if(actor.ClubPlan!=null)TickClubAttack(actor);
                else if(actor.TigerPlan!=null)TickTigerAttack(actor);
                else if(actor.FlamePlan!=null)TickFlameAttack(actor);
                else if(actor.AttackIsRoot&&actor.RootPlan!=null)TickRootAttack(actor);
                else if(actor.Clock.PendingAttackId!=0)ResolveStrike(actor);
                // Shared hit/death events may synchronously rest, unload, or clear the summon.
                if(active!=actor||actor.Root==null)return;
                if(actor.RootPlan!=null&&actor.RootPlan.IsFinished){actor.RootPlan=null;actor.RootTargets=Array.Empty<RootTarget>();actor.AttackIsRoot=false;}
                if(actor.FlamePlan!=null&&actor.FlamePlan.IsFinished){actor.FlamePlan=null;actor.FlameTargets=Array.Empty<RootTarget>();}
                if(actor.TigerPlan!=null&&actor.TigerPlan.IsFinished){actor.TigerPlan=null;actor.TigerTarget=null;}
                if(actor.ClubPlan!=null&&actor.ClubPlan.IsFinished){actor.ClubPlan=null;actor.ClubTargets=Array.Empty<ClubTarget>();}
                if(actor.WaterPlan!=null&&actor.WaterPlan.IsFinished){actor.WaterPlan=null;actor.WaterTargets=Array.Empty<WaterTarget>();}
                if(actor.RootPlan==null&&actor.FlamePlan==null&&actor.TigerPlan==null&&actor.ClubPlan==null&&actor.WaterPlan==null&&actor.Clock.Elapsed>=actor.RecoveryUntil&&actor.Clock.PendingAttackId==0)
                {
                    Vector3 player=Wiring.SummonPlayer.position;
                    bool returning=HorizontalDistance(before,player)>actor.Profile.LeashRange;
                    if(returning){actor.Target=null;actor.KeepingDistance=false;actor.Clock.SetActivityPhase(SummonPhase.Returning);}
                    else if(actor.Clock.Elapsed>=actor.NextTarget)
                    {actor.Target=FindTarget(actor);actor.NextTarget=actor.Clock.Elapsed+Mathf.Min(.25f,actor.Profile.RepathSeconds);}
                    if(!returning&&Eligible(actor,actor.Target))
                    {
                        if(actor.Profile.WaterAttackEnabled)HandleWaterTarget(actor,dt);
                        else if(actor.Profile.ClubAttackEnabled)HandleClubTarget(actor,dt);
                        else if(actor.Profile.TigerAttackEnabled)HandleTigerTarget(actor,dt);
                        else if(actor.Profile.FlameAttackEnabled)HandleFlameTarget(actor,dt);
                        else if(InAttackRange(actor,actor.Target)&&Sight(actor,actor.Target))BeginAttack(actor,dt);
                        else if(actor.Profile.RootAttackEnabled&&HorizontalDistance(before,actor.Target.transform.position)<=actor.Profile.RootAttackRange&&Sight(actor,actor.Target))
                            BeginRootAttack(actor,dt);
                        else {actor.Clock.SetActivityPhase(SummonPhase.Approaching);Navigate(actor,actor.Target.transform.position,dt,actor.Profile.AttackRange*.8f);}
                    }
                    else
                    {
                        actor.Target=null;
                        actor.KeepingDistance=false;
                        if(!returning)actor.Clock.SetActivityPhase(SummonPhase.Following);
                        Vector3 forward=Vector3.ProjectOnPlane(Wiring.SummonPlayer.forward,Vector3.up).normalized;
                        Vector3 follow=player-forward*actor.Profile.FollowDistance;
                        if(HorizontalDistance(before,player)>actor.Profile.FollowDistance+.4f||returning)Navigate(actor,follow,dt,.35f);
                        else actor.HasPath=false;
                    }
                }
            }
            else
            {
                if(actor.RootPlan!=null&&!actor.RootPlan.IsCancelled)CancelRootAttack(actor);
                if(actor.FlamePlan!=null&&!actor.FlamePlan.IsCancelled)CancelFlameAttack(actor);
                if(actor.TigerPlan!=null&&!actor.TigerPlan.IsCancelled)CancelTigerAttack(actor);
                if(actor.ClubPlan!=null&&!actor.ClubPlan.IsCancelled)CancelClubAttack(actor);
                if(actor.WaterPlan!=null&&!actor.WaterPlan.IsCancelled)CancelWaterAttack(actor);
            }
            float speed=(actor.Root.transform.position-before).magnitude/dt;
            float attackProgress=actor.AttackDuration>0&&actor.Clock.Elapsed<actor.RecoveryUntil?
                Mathf.Clamp01((actor.Clock.Elapsed-actor.AttackStarted)/actor.AttackDuration):0;
            actor.Presentation.Sample(actor.Clock,speed,attackProgress,actor.RootPlan,actor.AttackIsRoot,actor.FlamePlan,actor.MovingBackward,actor.TigerPlan,actor.WaterPlan);
        }
        EnemyVitals FindTarget(Actor actor)
        {
            var locked=Wiring.SummonLockTarget;
            if(Registered(locked)&&Eligible(actor,locked)&&Sight(actor,locked))return locked;
            EnemyVitals nearest=null;float distance=float.MaxValue;
            var targets=Wiring.SummonTargets;
            for(int i=0;i<targets.Count;i++)
            {
                var target=targets[i];if(!Eligible(actor,target))continue;
                float current=(target.transform.position-actor.Root.transform.position).sqrMagnitude;
                if(current<distance&&Sight(actor,target)){nearest=target;distance=current;}
            }
            return nearest;
        }
        bool Registered(EnemyVitals target)
        {
            if(target==null)return false;
            var targets=Wiring.SummonTargets;
            for(int i=0;i<targets.Count;i++)if(targets[i]==target)return true;
            return false;
        }
        bool Eligible(Actor actor,EnemyVitals target)
        {
            return target!=null&&target.isActiveAndEnabled&&target.IsAlive&&
                target.gameObject.scene.GetPhysicsScene()==gameObject.scene.GetPhysicsScene()&&
                HorizontalDistance(target.transform.position,Wiring.SummonPlayer.position)<=actor.Profile.LeashRange&&
                HorizontalDistance(target.transform.position,actor.Root.transform.position)<=actor.Profile.DetectionRange;
        }
        bool InAttackRange(Actor actor,EnemyVitals target)=>target!=null&&HorizontalDistance(actor.Root.transform.position,target.transform.position)<=actor.Profile.AttackRange&&
            Mathf.Abs(actor.Root.transform.position.y-target.transform.position.y)<=actor.Profile.BodyHeight;
        Vector3 StrikeOrigin(Actor actor)=>actor.Root.transform.position+Vector3.up*Mathf.Clamp(actor.Profile.BodyHeight*.55f,.45f,1.5f);
        bool Sight(Actor actor,EnemyVitals target)
        {
            if(target==null)return false;
            Vector3 from=StrikeOrigin(actor),delta=target.transform.position+Vector3.up*.4f-from;
            int count=ScenePhysicsQuery.RaycastAll(gameObject.scene,from,delta,delta.magnitude,~0,ref sightHits);
            for(int i=0;i<count;i++)
            {
                var hit=sightHits[i].transform;
                if(hit.IsChildOf(actor.Root.transform)||hit.IsChildOf(Wiring.SummonPlayer)||hit.GetComponentInParent<EnemyVitals>()!=null)continue;
                return false;
            }
            return true;
        }
        void BeginAttack(Actor actor,float dt)
        {
            if(!actor.Clock.CanAttack)return;
            // Face gradually inside the same swept clearance used by locomotion. Never snap a long body through a wall.
            if(!TryFace(actor,actor.Target.transform.position,dt*240))return;
            actor.AttackIsRoot=false;
            var attack=AttackProvenance.Create(actor.Root,DamageSource.Summon,actor.Cast.Element);
            if(!actor.Clock.TryBeginAttack(attack.AttackId,actor.Target.GetInstanceID(),actor.Target.LifeRevision,actor.Profile.WindupSeconds,actor.Profile.CooldownSeconds))return;
            actor.Strike=attack;actor.StrikeTarget=actor.Target;actor.StrikeRevision=actor.Target.LifeRevision;
            actor.AttackStarted=actor.Clock.Elapsed;
            actor.AttackDuration=Mathf.Max(actor.Profile.WindupSeconds+actor.Profile.CooldownSeconds,actor.Profile.AttackClip!=null?actor.Profile.AttackClip.length:0);
            actor.RecoveryUntil=actor.Clock.Elapsed+actor.AttackDuration;actor.HasPath=false;
            AttackStarts++;
        }
        void ResolveStrike(Actor actor)
        {
            var target=actor.StrikeTarget;long pending=actor.Clock.PendingAttackId;
            bool alive=target!=null&&target.isActiveAndEnabled&&target.IsAlive;
            bool range=alive&&InAttackRange(actor,target)&&HorizontalDistance(target.transform.position,Wiring.SummonPlayer.position)<=actor.Profile.LeashRange;
            bool sight=range&&Sight(actor,target);
            bool hit=actor.Clock.TryConsumeHit(pending,target!=null?target.GetInstanceID():0,target!=null?target.LifeRevision:0,alive,range,sight);
            if(actor.Clock.PendingAttackId==0)ConsumedStrikes++;
            if(!hit)return;
            var result=Wiring.ApplySummonHit(target,actor.StrikeRevision,actor.Power,StrikeOrigin(actor),actor.Strike,actor.Cast.Letter);
            if(result.AppliedDamage>0){ConfirmedHits++;ConfirmedDamage+=result.AppliedDamage;}
        }
        void BeginRootAttack(Actor actor,float dt)
        {
            if(!actor.Clock.CanAttack||!TryFace(actor,actor.Target.transform.position,dt*240))return;
            if(!TryBuildRootPlan(actor,out var plan))return;
            if(actor.Clock.ActivityRemaining<=actor.Profile.RootWindupSeconds+plan.Length/actor.Profile.RootTravelSpeed)return;
            var attack=AttackProvenance.Create(actor.Root,DamageSource.Summon,actor.Cast.Element);
            long targetId=actor.Target.GetInstanceID();uint revision=actor.Target.LifeRevision;
            if(!actor.Clock.TryBeginAttack(attack.AttackId,targetId,revision,actor.Profile.RootWindupSeconds,actor.Profile.RootCooldownSeconds))return;
            actor.Strike=attack;actor.StrikeTarget=actor.Target;actor.StrikeRevision=revision;actor.RootReleaseTargetId=targetId;
            actor.AttackIsRoot=true;actor.RootReleased=false;actor.RootPlan=plan;actor.PreviousRootFront=0;
            actor.AttackStarted=actor.Clock.Elapsed;actor.RecoveryUntil=plan.EndAt;actor.AttackDuration=plan.EndAt-plan.CastStartedAt;actor.HasPath=false;
            var captured=new List<RootTarget>();var candidates=Wiring.SummonTargets;
            for(int i=0;i<candidates.Count;i++)
            {
                var target=candidates[i];
                if(!Eligible(actor,target)||HorizontalDistance(actor.Root.transform.position,target.transform.position)>actor.Profile.RootAttackRange||
                    !plan.ContainsWidth(target.transform.position))continue;
                captured.Add(new RootTarget{Target=target,LifeRevision=target.LifeRevision});
            }
            actor.RootTargets=captured.ToArray();AttackStarts++;RootAttackStarts++;
        }
        bool TryBuildRootPlan(Actor actor,out SummonRootAttackPlan plan)
        {
            plan=null;var profile=actor.Profile;
            Vector3 direction=Vector3.ProjectOnPlane(actor.Target.transform.position-actor.Root.transform.position,Vector3.up).normalized;
            Vector3 seed=actor.Root.transform.position+actor.Root.transform.rotation*profile.RootStartOffset;
            if(!RootStripSupported(profile,seed,direction,out var origin)||Mathf.Abs(origin.y-seed.y)>profile.RootMaxStepHeight)return false;
            var points=new List<Vector3>{origin};
            int count=Mathf.CeilToInt(profile.RootAttackRange/profile.RootGroundSampleSpacing);
            for(int i=1;i<=count;i++)
            {
                float distance=Mathf.Min(profile.RootAttackRange,i*profile.RootGroundSampleSpacing);
                Vector3 sample=origin+direction*distance;sample.y=points[points.Count-1].y;
                if(!RootStripSupported(profile,sample,direction,out var ground)||
                    Mathf.Abs(ground.y-points[points.Count-1].y)>profile.RootMaxStepHeight||
                    !RootSegmentClear(profile,points[points.Count-1],ground,direction))break;
                points.Add(ground);
            }
            if(points.Count<2)return false;
            float length=HorizontalDistance(points[0],points[points.Count-1]);
            float travel=length/profile.RootTravelSpeed;
            float clip=profile.RootAttackClip!=null?profile.RootAttackClip.length:0;
            float cleanup=Mathf.Max(profile.RootSegmentHoldSeconds+profile.RootSegmentRetractSeconds,profile.RootRecoverySeconds,
                clip-profile.RootWindupSeconds-travel);
            plan=new SummonRootAttackPlan(points,profile.RootAttackWidth,actor.Clock.Elapsed,profile.RootWindupSeconds,profile.RootTravelSpeed,cleanup);
            return true;
        }
        bool RootStripSupported(SummonCombatProfile profile,Vector3 point,Vector3 direction,out Vector3 ground)
        {
            if(!Ground(profile,point,out ground))return false;
            var right=Vector3.Cross(Vector3.up,direction)*profile.RootAttackWidth*.5f;
            return Ground(profile,ground+right,out var left)&&Ground(profile,ground-right,out var other)&&
                Mathf.Abs(left.y-ground.y)<=profile.RootMaxStepHeight&&Mathf.Abs(other.y-ground.y)<=profile.RootMaxStepHeight;
        }
        bool RootSegmentClear(SummonCombatProfile profile,Vector3 from,Vector3 to,Vector3 direction)
        {
            Vector3 right=Vector3.Cross(Vector3.up,direction)*profile.RootAttackWidth*.45f;
            for(int offset=-1;offset<=1;offset++)
            {
                Vector3 start=from+right*offset+Vector3.up*.12f;Vector3 delta=to-from;
                if(delta.sqrMagnitude<.000001f)continue;
                int count=ScenePhysicsQuery.RaycastAll(gameObject.scene,start,delta,delta.magnitude,~0,ref sightHits);
                for(int i=0;i<count;i++)if(!IgnoreCharacter(sightHits[i].collider))return false;
            }
            return true;
        }
        bool RootTravelClear(Actor actor,float from,float to)
        {
            var plan=actor.RootPlan;var profile=actor.Profile;
            Vector3 previous=plan.GroundAt(from);
            int steps=Mathf.Max(1,Mathf.CeilToInt((to-from)/profile.RootGroundSampleSpacing));
            for(int i=1;i<=steps;i++)
            {
                Vector3 expected=plan.GroundAt(Mathf.Lerp(from,to,(float)i/steps));
                if(!RootStripSupported(profile,expected,plan.Direction,out var actual)||
                    Mathf.Abs(expected.y-actual.y)>profile.RootMaxStepHeight||
                    !RootSegmentClear(profile,previous,expected,plan.Direction))return false;
                previous=expected;
            }
            return true;
        }
        void TickRootAttack(Actor actor)
        {
            var plan=actor.RootPlan;plan.AdvanceTo(actor.Clock.Elapsed);
            bool justReleased=false;
            if(!actor.RootReleased)
            {
                if(!plan.IsReleased)return;
                // The shared clock owns the cast's release gate, not one homing target. A primary target
                // dying during windup does not erase this already fixed route or other captured lives.
                if(!actor.Clock.TryConsumeHit(actor.Strike.AttackId,actor.RootReleaseTargetId,actor.StrikeRevision,true,true,true))
                {
                    // Public plan time is float, while the combat clock keeps its due boundary in
                    // double. A rounded release time can become visible one tick before that gate.
                    // The same pending ID means "not due yet", not a cancelled cast.
                    if(actor.Clock.PendingAttackId==actor.Strike.AttackId)return;
                    CancelRootAttack(actor);return;
                }
                actor.RootReleased=true;justReleased=true;ConsumedStrikes++;
            }
            float front=plan.FrontDistance,previous=actor.PreviousRootFront;
            if(front<=previous+.00001f&&!justReleased)return;
            if(!RootTravelClear(actor,previous,front)){CancelRootAttack(actor);return;}
            actor.PreviousRootFront=front;
            foreach(var snapshot in actor.RootTargets)
            {
                if(snapshot.Resolved)continue;
                var target=snapshot.Target;
                if(target==null||!target.isActiveAndEnabled||!target.IsAlive||target.LifeRevision!=snapshot.LifeRevision)
                {snapshot.Resolved=true;continue;}
                float along=plan.Longitudinal(target.transform.position);
                if(along>front+.0001f)continue;
                snapshot.Resolved=true;
                if(along<previous-.001f||!plan.ContainsWidth(target.transform.position)||!Registered(target)||
                    HorizontalDistance(target.transform.position,Wiring.SummonPlayer.position)>actor.Profile.LeashRange||
                    !Ground(actor.Profile,target.transform.position,out var targetGround))continue;
                Vector3 rootGround=plan.GroundAt(along);
                if(Mathf.Abs(rootGround.y-targetGround.y)>actor.Profile.RootMaxStepHeight+.05f)continue;
                var result=Wiring.ApplySummonHit(target,snapshot.LifeRevision,actor.RootPower,rootGround+Vector3.up*.2f,actor.Strike,actor.Cast.Letter);
                if(result.AppliedDamage>0){ConfirmedHits++;ConfirmedDamage+=result.AppliedDamage;}
                if(active!=actor||actor.Root==null)return;
            }
        }
        void CancelRootAttack(Actor actor)
        {
            if(actor.RootPlan==null||actor.RootPlan.IsCancelled)return;
            actor.RootPlan.Cancel();actor.Clock.CancelAttack();RootAttackCancellations++;
            actor.RecoveryUntil=Mathf.Max(actor.Clock.Elapsed+actor.Profile.RootRecoverySeconds,actor.RecoveryUntil);
        }
        void HandleFlameTarget(Actor actor,float dt)
        {
            var profile=actor.Profile;Vector3 target=actor.Target.transform.position;
            float distance=HorizontalDistance(actor.Root.transform.position,target);
            if(distance<profile.FlameMinimumDistance&&!actor.KeepingDistance)
            {actor.KeepingDistance=true;actor.HasPath=false;actor.NextPath=actor.Clock.Elapsed;}
            if(actor.KeepingDistance&&distance>=profile.FlamePreferredDistance-.1f)
            {actor.KeepingDistance=false;actor.HasPath=false;}
            if(actor.KeepingDistance)
            {
                Vector3 away=Vector3.ProjectOnPlane(actor.Root.transform.position-target,Vector3.up);
                if(away.sqrMagnitude<.0001f)away=-actor.Root.transform.forward;
                Vector3 destination=target+away.normalized*profile.FlamePreferredDistance;
                destination.y=actor.Root.transform.position.y;
                actor.Clock.SetActivityPhase(SummonPhase.Following);
                // Backpedal while looking at the enemy; each step must stay on supported terrain,
                // inside the player's leash and must not move closer while circling a NavMesh corner.
                if(HorizontalDistance(destination,Wiring.SummonPlayer.position)<=profile.LeashRange&&
                    Navigate(actor,destination,dt,.1f,target,true))
                {actor.MovingBackward=true;return;}
                // An unsafe retreat never causes a forced step or teleport. A valid cone can still fire here.
            }
            if(distance>profile.FlameRange||!Sight(actor,actor.Target))
            {
                actor.KeepingDistance=false;actor.Clock.SetActivityPhase(SummonPhase.Approaching);
                Navigate(actor,target,dt,profile.FlamePreferredDistance);return;
            }
            BeginFlameAttack(actor,dt);
        }
        void BeginFlameAttack(Actor actor,float dt)
        {
            var profile=actor.Profile;
            if(!actor.Clock.CanAttack||actor.Clock.ActivityRemaining<=profile.FlameWindupSeconds+profile.FlameSpraySeconds||
                !TryFace(actor,actor.Target.transform.position,dt*240))return;
            Vector3 origin=actor.Root.transform.TransformPoint(profile.FlameOriginOffset);
            if(!FlameOriginClear(origin))return;
            float clip=profile.FlameAttackClip!=null?profile.FlameAttackClip.length:0;
            float recovery=Mathf.Max(profile.FlameRecoverySeconds,clip-profile.FlameWindupSeconds-profile.FlameSpraySeconds);
            var plan=new SummonFlameAttackPlan(origin,actor.Root.transform.forward,profile.FlameRange,profile.FlameHalfAngleDegrees,
                profile.FlameVerticalTolerance,actor.Clock.Elapsed,profile.FlameWindupSeconds,profile.FlameSpraySeconds,recovery);
            if(!plan.Contains(FlameAimPoint(actor.Target)))return;
            var attack=AttackProvenance.Create(actor.Root,DamageSource.Summon,actor.Cast.Element);
            long targetId=actor.Target.GetInstanceID();uint revision=actor.Target.LifeRevision;
            if(!actor.Clock.TryBeginAttack(attack.AttackId,targetId,revision,profile.FlameWindupSeconds,profile.FlameSpraySeconds+recovery))return;
            actor.Strike=attack;actor.StrikeTarget=actor.Target;actor.StrikeRevision=revision;actor.FlameReleaseTargetId=targetId;
            actor.FlamePlan=plan;actor.FlameReleased=false;actor.AttackIsRoot=false;actor.MovingBackward=false;
            actor.AttackStarted=actor.Clock.Elapsed;actor.RecoveryUntil=plan.EndAt;actor.AttackDuration=plan.EndAt-plan.StartedAt;actor.HasPath=false;
            var captured=new List<RootTarget>();var candidates=Wiring.SummonTargets;
            for(int i=0;i<candidates.Count;i++)
            {
                var target=candidates[i];if(!Eligible(actor,target)||!plan.Contains(FlameAimPoint(target)))continue;
                captured.Add(new RootTarget{Target=target,LifeRevision=target.LifeRevision});
            }
            actor.FlameTargets=captured.ToArray();AttackStarts++;FlameAttackStarts++;
        }
        void TickFlameAttack(Actor actor)
        {
            var plan=actor.FlamePlan;plan.AdvanceTo(actor.Clock.Elapsed);
            if(actor.FlameReleased||!plan.IsReleased)return;
            if(!FlameOriginClear(plan.Origin)||!VolumeClear(actor.Profile,actor.Root.transform.position,actor.Root.transform.rotation,true))
            {CancelFlameAttack(actor);return;}
            if(!actor.Clock.TryConsumeHit(actor.Strike.AttackId,actor.FlameReleaseTargetId,actor.StrikeRevision,true,true,true))
            {
                // As with roots, the float presentation time may round to release before the double gate.
                if(actor.Clock.PendingAttackId==actor.Strike.AttackId)return;
                CancelFlameAttack(actor);return;
            }
            actor.FlameReleased=true;ConsumedStrikes++;
            foreach(var snapshot in actor.FlameTargets)
            {
                if(snapshot.Resolved)continue;snapshot.Resolved=true;
                var target=snapshot.Target;
                if(target==null||!target.isActiveAndEnabled||!target.IsAlive||target.LifeRevision!=snapshot.LifeRevision||
                    !Registered(target)||!plan.Contains(FlameAimPoint(target))||
                    HorizontalDistance(target.transform.position,Wiring.SummonPlayer.position)>actor.Profile.LeashRange)continue;
                var result=Wiring.ApplySummonHit(target,snapshot.LifeRevision,actor.FlamePower,plan.Origin,actor.Strike,actor.Cast.Letter);
                if(result.AppliedDamage>0){ConfirmedHits++;ConfirmedDamage+=result.AppliedDamage;}
                if(active!=actor||actor.Root==null)return;
            }
        }
        void CancelFlameAttack(Actor actor)
        {
            if(actor.FlamePlan==null||actor.FlamePlan.IsCancelled)return;
            actor.FlamePlan.Cancel();actor.Clock.CancelAttack();FlameAttackCancellations++;
            actor.RecoveryUntil=Mathf.Max(actor.Clock.Elapsed+actor.Profile.FlameRecoverySeconds,actor.RecoveryUntil);
        }
        bool FlameOriginClear(Vector3 origin)
        {
            var physics=gameObject.scene.GetPhysicsScene();if(!physics.IsValid())return false;
            int count=physics.OverlapSphere(origin,.06f,overlaps,~0,QueryTriggerInteraction.Ignore);
            if(count==overlaps.Length)return false;
            for(int i=0;i<count;i++)if(!IgnoreCharacter(overlaps[i]))return false;
            return true;
        }
        static Vector3 FlameAimPoint(EnemyVitals target)=>target.transform.position+Vector3.up*.4f;
        bool Navigate(Actor actor,Vector3 destination,float dt,float stopDistance,Vector3? facingTarget=null,bool retreatOnly=false,float speedOverride=0)
        {
            if(HorizontalDistance(actor.Root.transform.position,destination)<=stopDistance){actor.HasPath=false;return false;}
            // A target/mode change must never continue an old path towards the previous destination.
            if(actor.HasPath&&HorizontalDistance(actor.Destination,destination)>1f)actor.HasPath=false;
            if(actor.Clock.Elapsed>=actor.NextPath)
            {
                actor.NextPath=actor.Clock.Elapsed+actor.Profile.RepathSeconds;
                actor.HasPath=CalculatePath(actor.Profile,actor.Root.transform.position,destination,actor.Path,out _);
                if(actor.HasPath){actor.Corners=actor.Path.corners;actor.Corner=1;actor.Destination=destination;}
            }
            if(!actor.HasPath||actor.Corner>=actor.Corners.Length)return false;
            Vector3 position=actor.Root.transform.position;
            while(actor.Corner<actor.Corners.Length&&HorizontalDistance(position,actor.Corners[actor.Corner])<.15f)actor.Corner++;
            if(actor.Corner>=actor.Corners.Length){actor.HasPath=false;return false;}
            Vector3 corner=actor.Corners[actor.Corner];Vector3 horizontal=Vector3.ProjectOnPlane(corner-position,Vector3.up);
            float step=Mathf.Min((speedOverride>0?speedOverride:actor.Profile.FollowSpeed)*dt,horizontal.magnitude);
            if(step<=.00001f)return false;
            Vector3 proposed=position+horizontal.normalized*step;
            Vector3 lookDirection=facingTarget.HasValue?Vector3.ProjectOnPlane(facingTarget.Value-position,Vector3.up):horizontal;
            if(lookDirection.sqrMagnitude<.0001f)lookDirection=actor.Root.transform.forward;
            var rotation=Quaternion.RotateTowards(actor.Root.transform.rotation,Quaternion.LookRotation(lookDirection.normalized,Vector3.up),dt*240);
            if(retreatOnly&&facingTarget.HasValue&&HorizontalDistance(proposed,facingTarget.Value)<HorizontalDistance(position,facingTarget.Value)-.001f)
            {BlockedMovementSteps++;actor.HasPath=false;return false;}
            if(!Ground(actor.Profile,proposed,out var ground)||Mathf.Abs(ground.y-position.y)>actor.Profile.FootHeightTolerance+.05f||
                !FootSupport(actor.Profile,ground,rotation)||!SweptPoseClear(actor.Profile,position,actor.Root.transform.rotation,ground,rotation)||
                !NavMesh.SamplePosition(ground,out var nav,.35f,actor.Profile.NavMeshAreaMask)||Vector3.Distance(nav.position,ground)>.4f)
            {BlockedMovementSteps++;actor.HasPath=false;return false;}
            actor.Root.transform.SetPositionAndRotation(ground,rotation);
            return true;
        }
        bool TryFace(Actor actor,Vector3 target,float maxDegrees)
        {
            Vector3 direction=Vector3.ProjectOnPlane(target-actor.Root.transform.position,Vector3.up);
            if(direction.sqrMagnitude<=.0001f)return true;
            var desired=Quaternion.LookRotation(direction);var old=actor.Root.transform.rotation;
            var proposed=Quaternion.RotateTowards(old,desired,maxDegrees);var feet=actor.Root.transform.position;
            if(!FootSupport(actor.Profile,feet,proposed)||!SweptPoseClear(actor.Profile,feet,old,feet,proposed))
            {BlockedRotationSteps++;return false;}
            actor.Root.transform.rotation=proposed;
            return Quaternion.Angle(proposed,desired)<=1f;
        }
        bool SweptPoseClear(SummonCombatProfile profile,Vector3 from,Quaternion fromRotation,Vector3 to,Quaternion toRotation)
        {
            float angle=Quaternion.Angle(fromRotation,toRotation);
            int steps=Mathf.Max(1,Mathf.CeilToInt(angle/4f));
            float radius=new Vector2(profile.FootprintWidth*.5f,profile.FootprintLength*.5f).magnitude+
                new Vector2(profile.FootprintOffset.x,profile.FootprintOffset.z).magnitude;
            // Midpoint boxes are expanded by a bound on the corner arc plus translation. Thin obstacles
            // between samples therefore fail closed instead of hiding in a discrete angular sample gap.
            float arc=2f*radius*Mathf.Sin(angle*Mathf.Deg2Rad/(steps*4f));
            float travel=HorizontalDistance(from,to)/(steps*2f);
            float vertical=Mathf.Abs(to.y-from.y)/(steps*2f);
            for(int i=0;i<steps;i++)
            {
                float midpoint=(i+.5f)/steps;
                var feet=Vector3.Lerp(from,to,midpoint);var rotation=Quaternion.Slerp(fromRotation,toRotation,midpoint);
                if(!FootSupport(profile,feet,rotation)||!VolumeClear(profile,feet,rotation,true,arc+travel,vertical))return false;
            }
            return true;
        }
        bool TryPlacement(SummonCombatProfile profile,Vector3 origin,Vector3 forward,out Vector3 feet,out Quaternion rotation)
        {
            feet=default;rotation=Quaternion.LookRotation(forward);
            foreach(float angle in SpawnAngles)
            {
                Vector3 direction=Quaternion.AngleAxis(angle,Vector3.up)*forward;
                Vector3 candidate=origin+direction*profile.SpawnDistance;
                if(!Ground(profile,candidate,out candidate)||!FootSupport(profile,candidate,rotation)||!VolumeClear(profile,candidate,rotation,false))continue;
                if(!CalculatePath(profile,Wiring.SummonPlayer.position,candidate,placementPath,out var navPoint))continue;
                if(Vector3.Distance(candidate,navPoint)>.4f)continue;
                float length=0;var corners=placementPath.corners;for(int i=1;i<corners.Length;i++)length+=Vector3.Distance(corners[i-1],corners[i]);
                if(length>profile.SpawnDistance*2.5f)continue;
                feet=candidate;return true;
            }
            return false;
        }
        bool CalculatePath(SummonCombatProfile profile,Vector3 from,Vector3 to,NavMeshPath path,out Vector3 destination)
        {
            destination=default;PathQueries++;
            // Encounter transforms can be capsule centres (feet + .875m). Navigation destinations must
            // use owned ground, not that visual/combat pivot. Character colliders are ignored by Ground.
            if(!Ground(profile,to,out var destinationFeet)||
                !NavMesh.SamplePosition(from,out var start,1f,profile.NavMeshAreaMask)||!NavMesh.SamplePosition(destinationFeet,out var end,.6f,profile.NavMeshAreaMask)||
                Mathf.Abs(start.position.y-from.y)>1f||Vector3.Distance(end.position,destinationFeet)>.65f||
                !NavMesh.CalculatePath(start.position,end.position,profile.NavMeshAreaMask,path)||path.status!=NavMeshPathStatus.PathComplete||
                !OwnedPathGround(profile,path))
            {PathFailures++;return false;}
            destination=end.position;return true;
        }
        bool OwnedPathGround(SummonCombatProfile profile,NavMeshPath path)
        {
            // NavMesh queries are global. Reject data from another loaded/preview scene or an unsupported bridge.
            var corners=path.corners;if(corners.Length<2)return false;
            float total=0;
            for(int i=1;i<corners.Length;i++)
            {
                float length=Vector3.Distance(corners[i-1],corners[i]);total+=length;
                if(total>profile.LeashRange*4)return false;
                int samples=Mathf.Max(1,Mathf.CeilToInt(length/1.5f));
                for(int j=0;j<=samples;j++)
                {
                    Vector3 point=Vector3.Lerp(corners[i-1],corners[i],(float)j/samples);
                    if(!Ground(profile,point,out var ground)||Mathf.Abs(ground.y-point.y)>.45f)return false;
                }
            }
            return true;
        }
        bool Ground(SummonCombatProfile profile,Vector3 candidate,out Vector3 feet)
        {
            feet=default;float nearest=float.PositiveInfinity;
            int count=ScenePhysicsQuery.RaycastAll(gameObject.scene,candidate+Vector3.up*profile.GroundProbeUp,Vector3.down,
                profile.GroundProbeUp+profile.GroundProbeDown,profile.GroundMask,ref groundHits);
            for(int i=0;i<count;i++)
            {
                var hit=groundHits[i];if(IgnoreCharacter(hit.collider)||Vector3.Angle(hit.normal,Vector3.up)>profile.MaxSlope||hit.distance>=nearest)continue;
                nearest=hit.distance;feet=hit.point+Vector3.up*.035f;
            }
            return float.IsFinite(nearest);
        }
        bool FootSupport(SummonCombatProfile profile,Vector3 centre,Quaternion rotation)
        {
            centre+=rotation*profile.FootprintOffset;
            for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)
            {
                Vector3 sample=centre+rotation*new Vector3(x*profile.FootprintWidth*.4f,0,z*profile.FootprintLength*.4f);
                if(!Ground(profile,sample,out var foot)||Mathf.Abs(foot.y-centre.y)>profile.FootHeightTolerance)return false;
            }
            return true;
        }
        bool VolumeClear(SummonCombatProfile profile,Vector3 feet,Quaternion rotation,bool allowCharacters,float horizontalPadding=0,float verticalPadding=0)
        {
            var physics=gameObject.scene.GetPhysicsScene();if(!physics.IsValid())return false;
            Vector3 half=new Vector3(profile.FootprintWidth*.5f+horizontalPadding,Mathf.Max(.05f,profile.BodyHeight*.5f-.08f)+verticalPadding,profile.FootprintLength*.5f+horizontalPadding);
            int count=physics.OverlapBox(feet+rotation*profile.FootprintOffset+Vector3.up*(profile.BodyHeight*.5f+.08f),half,overlaps,rotation,profile.GroundMask,QueryTriggerInteraction.Ignore);
            if(count==overlaps.Length)return false;
            for(int i=0;i<count;i++)if(overlaps[i]!=null&&overlaps[i].enabled&&!(allowCharacters&&IgnoreCharacter(overlaps[i])))return false;
            return true;
        }
        bool IgnoreCharacter(Collider collider)=>collider==null||Wiring!=null&&collider.transform.IsChildOf(Wiring.SummonPlayer)||
            collider.GetComponentInParent<EnemyVitals>()!=null||active?.Root!=null&&collider.transform.IsChildOf(active.Root.transform)||prepared?.Root!=null&&collider.transform.IsChildOf(prepared.Root.transform);
        static float HorizontalDistance(Vector3 a,Vector3 b)=>Vector3.ProjectOnPlane(a-b,Vector3.up).magnitude;
        static bool Finite(Vector3 value)=>float.IsFinite(value.x)&&float.IsFinite(value.y)&&float.IsFinite(value.z);
        static void SafeDestroy(UnityEngine.Object value)
        {
            if(value==null)return;
            if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);
        }
    }
}
