using System;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using UnityEngine;
namespace Oheangbu.App.World
{
    // SPEC-PLAYTEST-306 #5 / PLAN §2-5: work -> glance (head IK only) -> talk (turn + talk clip, unscaled: the dialogue pauses the
    // game) -> back to work ReturnDelaySeconds after the conversation ends. Lives on the Animator object (OnAnimatorIK); moves and
    // turns Body (the OwnedActorAppearance holder) only, never the interaction point or its hit volume, and never farther than the
    // point radius - RadiusMargin from the point so CanInteract distance / sight keep holding. Appearance only: no gameplay state.
    // Talk starts from the conversation itself: DialogueRequested with SourceId == PointId (always subscribed - safe: the session's
    // DialogueSurfaceBound reads the view's own DialogueViewBound flag, not the subscriber count) or InteractionResolved at the point,
    // and ends on DialogueEnded or when the pause lifts. ListenToDialogueRequests also matches a source within MatchDistance. Only with
    // no dialogue surface bound (legacy Present()) does the focused-then-paused edge stand in for F (Esc / I / M / death pause the game
    // too, so with the surface bound that edge is never a conversation).
    [DisallowMultipleComponent]
    public sealed class NpcJobActor:MonoBehaviour
    {
        public enum Phase { Work, Walk, Talk, Return }
        public WorldMacroPlaytestSession Session;
        public NpcJobProfileSO Profile;
        public string PointId="";
        public Animator Animator;
        [Tooltip("Moved and turned; the point, its colliders and world props stay put.")]
        public Transform Body;
        public OwnedActorMotion Motion;
        [Tooltip("Controller before NpcJobs306 author (revert restores it).")]
        public RuntimeAnimatorController OriginalController;
        [Tooltip("Hand tool per behaviour index (null = none); only the current behaviour's tool is shown.")]
        public GameObject[] Tools=Array.Empty<GameObject>();
        public int Seed;
        [Tooltip("Also start talking on a session.DialogueRequested whose SourcePosition is within MatchDistance (SourceId == PointId always matches).")]
        public bool ListenToDialogueRequests;
        [Tooltip("#307 [TEST] while this job actor runs, an AlwaysAnimate Animator without root motion uses CullUpdateTransforms (poses differ only while no renderer of it is drawn; the state machine, timers and job keep running).")]
        public bool CullWhenOffscreen=true;
        public Phase State {get;private set;}
        public int Current {get;private set;}=-1;
        public float LookWeight {get;private set;}
        public bool Glancing {get;private set;}
        public bool Humanoid {get;private set;}
        public bool Carried {get;private set;}
        public string TalkingTo {get;private set;}
        public float Allowed {get;private set;}
        public string Playing=>playing;
        readonly int hIdle=Animator.StringToHash("Idle"),hWalk=Animator.StringToHash("Walk"),hTalk=Animator.StringToHash("Talk"),hGlance=Animator.StringToHash("Glance"),
            hSit=Animator.StringToHash("Sit"),hSitDown=Animator.StringToHash("SitDown"),hStandUp=Animator.StringToHash("StandUp");
        readonly RaycastHit[] hits=new RaycastHit[16];
        Vector3 homePos,spot,look,pointPos,parentWas;Quaternion homeRot,workRot;Transform pointRoot;System.Random rng;AnimatorUpdateMode baseMode;
        bool subscribed,started,motionWas,dialogueTalk,sawBlocked,seated,gestureDone,standing,hasPoint,wasBlocked,wasFocused,replay;
        float until,returnAt,talkStart,glanceUntil,standSince;int pendingNext=-1;string playing;
        float appliedLook=-1;bool culledByJob;AnimatorCullingMode cullingWas;
        // #307 phase 1 item 11: off-screen animator culling while the job runs (restored on disable)
        void ApplyOffscreenCulling()
        {
            if(!CullWhenOffscreen||culledByJob||Animator==null||Animator.applyRootMotion||Animator.cullingMode!=AnimatorCullingMode.AlwaysAnimate)return;
            cullingWas=Animator.cullingMode;Animator.cullingMode=AnimatorCullingMode.CullUpdateTransforms;culledByJob=true;
        }
        void RestoreOffscreenCulling(){if(culledByJob&&Animator!=null&&Animator.cullingMode==AnimatorCullingMode.CullUpdateTransforms)Animator.cullingMode=cullingWas;culledByJob=false;}

        void OnEnable()
        {
            if(Motion!=null){motionWas=Motion.enabled;Motion.JobDriven=true;Motion.enabled=false;}
            Subscribe();
            // the Animator restarts in its default state on enable: play the current behaviour again
            if(started){playing=null;replay=true;wasBlocked=false;wasFocused=false;if(Body!=null&&Body.parent!=null)parentWas=Body.parent.position;ApplyOffscreenCulling();}
            appliedLook=-1;
        }
        void OnDisable()
        {
            Unsubscribe();
            if(Animator!=null&&started)Animator.updateMode=baseMode;
            RestoreOffscreenCulling();
            if(Motion!=null){Motion.JobDriven=false;Motion.enabled=motionWas;}
            if(State==Phase.Talk||State==Phase.Return){State=Phase.Work;TalkingTo=null;}
            LookWeight=0;
        }
        void Start()
        {
            if(Session==null)Session=FindFirstObjectByType<WorldMacroPlaytestSession>();
            Subscribe();
            if(Animator==null)Animator=GetComponent<Animator>();
            if(Body==null)Body=transform.parent!=null?transform.parent:transform;
            if(Animator!=null)baseMode=Animator.updateMode;
            ApplyOffscreenCulling();
            Humanoid=Animator!=null&&Animator.isHuman&&Animator.runtimeAnimatorController!=null&&Animator.HasState(0,hIdle);
            homePos=Body.localPosition;homeRot=Body.localRotation;workRot=homeRot;spot=Body.position;parentWas=Body.parent!=null?Body.parent.position:Vector3.zero;
            PrologueContentSO.Point point=null;var list=Session!=null&&Session.Content!=null?Session.Content.Points:null;
            if(list!=null&&!string.IsNullOrEmpty(PointId))for(int i=0;i<list.Length;i++)if(list[i]!=null&&list[i].Id==PointId){point=list[i];break;}
            hasPoint=point!=null;pointPos=hasPoint?point.Position:Body.position;
            // world metres: the point radius is world space whatever the holder's parent scale is
            Allowed=!hasPoint||Profile==null?0:Mathf.Max(0,point.Radius-Profile.RadiusMargin-Flat(Body.position,point.Position));
            var owner=GetComponentInParent<WorldMacroContentPoint>();pointRoot=owner!=null?owner.transform:Body.parent;
            rng=new System.Random(Seed^StableHash(PointId));started=true;
            for(int i=0;i<Tools.Length;i++)if(Tools[i]!=null)Tools[i].SetActive(false);
            State=Phase.Work;if(Profile!=null)Next();
        }
        void Subscribe()
        {
            if(Session==null||subscribed)return;
            Session.DialogueEnded+=OnDialogueEnded;Session.InteractionResolved+=OnInteraction;Session.DialogueRequested+=OnDialogue;subscribed=true;
        }
        void Unsubscribe()
        {
            if(Session==null||!subscribed)return;
            Session.DialogueEnded-=OnDialogueEnded;Session.InteractionResolved-=OnInteraction;Session.DialogueRequested-=OnDialogue;subscribed=false;
        }
        // DialogueRequest306: this point's id, or (ListenToDialogueRequests) a source within MatchDistance of the actor
        void OnDialogue(DialogueRequest306 r)
        {
            if(r==null||Profile==null||!started||!isActiveAndEnabled)return;
            bool mine=!string.IsNullOrEmpty(PointId)&&r.SourceId==PointId||ListenToDialogueRequests&&Flat(r.SourcePosition,Body.position)<=Profile.MatchDistance;
            if(mine)BeginTalk(r.SourceId,true);
        }
        void OnDialogueEnded(string id){if(State==Phase.Talk&&(dialogueTalk?TalkingTo==id||string.IsNullOrEmpty(TalkingTo):true))BeginReturn();}
        // the resolved interaction at this point (conversation, or a paid commission report resolved as Currency at the giver)
        void OnInteraction(PrologueInteractionKind kind,Vector3 at)
        {
            if(kind!=PrologueInteractionKind.Conversation&&kind!=PrologueInteractionKind.Currency||Profile==null||!started||State==Phase.Talk)return;
            // Currency: only at this point itself (a pickup or a dropped purse beside the NPC is not a word with it)
            bool here=hasPoint&&Flat(at,pointPos)<=.25f||Flat(at,Body.position)<=Profile.MatchDistance&&(kind==PrologueInteractionKind.Conversation||!hasPoint);
            if(here)BeginTalk(PointId,false);
        }
        void BeginTalk(string id,bool viaDialogue)
        {
            if(State==Phase.Talk){dialogueTalk|=viaDialogue;return;}
            State=Phase.Talk;dialogueTalk=viaDialogue;TalkingTo=id;talkStart=Time.unscaledTime;sawBlocked=Blocked;gestureDone=false;glanceUntil=0;
            if(Animator!=null)Animator.updateMode=AnimatorUpdateMode.UnscaledTime;
            if(Humanoid&&!seated&&!standing)Play(hTalk,"Talk");
        }
        void BeginReturn(){State=Phase.Return;returnAt=Time.unscaledTime+Profile.ReturnDelaySeconds;}
        void Resume()
        {
            if(Animator!=null)Animator.updateMode=baseMode;
            TalkingTo=null;dialogueTalk=false;
            // a stand-up the talk interrupted finishes first (Work's standing branch then starts the pending behaviour)
            if(standing){State=Phase.Work;return;}
            playing=null;
            if(Current<0){State=Phase.Work;Next();return;}
            if(Flat(Body.position,spot)>.15f){State=Phase.Walk;Play(hWalk,"Walk");}
            else{State=Phase.Work;PlayBehaviour();}
        }
        bool Blocked=>Session!=null&&Session.GameplayInputBlocked;
        void Update()
        {
            using(Perf307Markers.NpcJob.Auto()){if(!started||Profile==null||Body==null)return;
            var walker=Session!=null?Session.Walker:null;
            var player=walker!=null&&walker.Body!=null?walker.Body.transform:null;
            float d=player!=null?Flat(player.position,Body.position):float.MaxValue;
            if(player!=null)look=player.position+Vector3.up*walker.EyeHeight;
            float udt=Time.unscaledDeltaTime,dt=Time.deltaTime;
            // carried (escort companion walking or riding): the mover owns the facing; no glance turn, no workplace walk
            if(Body.parent!=null){var pp=Body.parent.position;if(dt>0)Carried=Flat(pp,parentWas)>Profile.CarriedSpeed*dt;parentWas=pp;}
            // legacy Present() only (no dialogue surface bound): the session focused this point last frame and the game paused this
            // frame = F here. With the surface bound the conversation arrives as DialogueRequested, and a pause is not a talk.
            bool blocked=Blocked;
            if(blocked&&!wasBlocked&&wasFocused&&State!=Phase.Talk&&!Session.DialogueSurfaceBound)BeginTalk(PointId,false);
            if(!blocked)wasFocused=Session!=null&&!string.IsNullOrEmpty(PointId)&&Session.FocusedId==PointId;
            wasBlocked=blocked;
            if(!Glancing&&d<Profile.ApproachDistance){Glancing=true;BeginGlance();}
            else if(Glancing&&d>Profile.LeaveDistance)Glancing=false;
            if(replay&&State==Phase.Work&&!standing){replay=false;PlayBehaviour();}
            switch(State)
            {
                case Phase.Talk:
                    TurnToPlayer(player,udt);
                    if(Humanoid&&!gestureDone&&playing=="Talk"){var st=Animator.GetCurrentAnimatorStateInfo(0);if(st.shortNameHash==hTalk&&!st.loop&&st.normalizedTime>=1){gestureDone=true;Play(hIdle,"Idle");}}
                    // the pause lifted (conversation over, or a view that never sent DialogueEnded); never paused = a short receipt
                    sawBlocked|=blocked;
                    if(sawBlocked&&!blocked||!sawBlocked&&!dialogueTalk&&Time.unscaledTime-talkStart>Profile.FallbackTalkSeconds)BeginReturn();
                    if(State==Phase.Talk&&Time.unscaledTime-talkStart>Profile.TalkTimeoutSeconds)BeginReturn();
                    break;
                case Phase.Return:
                    TurnToPlayer(player,udt);
                    if(Time.unscaledTime>=returnAt)Resume();
                    break;
                case Phase.Walk:
                    if(Carried){State=Phase.Work;PlayBehaviour();break;}
                    Walk(dt);
                    break;
                default:
                    if(standing)
                    {
                        if(dt<=0)break;
                        // done: StandUp near its end, or already handed to Idle by its exit transition (a talk ran it unscaled)
                        var st=Animator.GetCurrentAnimatorStateInfo(0);bool through=Animator.IsInTransition(0);
                        if(st.shortNameHash==hStandUp&&st.normalizedTime>=.95f||st.shortNameHash==hIdle&&!through||Time.time-standSince>3){standing=false;Begin(pendingNext);}
                        break;
                    }
                    // wary / hostile never square up on a glance (D306); a Generic rig (no head IK) turns the body instead of the head
                    bool bodyGlance=Glancing&&!Carried&&!seated&&Profile.Attitude==NpcAttitude306.Neutral&&(!Humanoid||Profile.GlanceTurnsBody);
                    if(bodyGlance)TurnToPlayer(player,dt);else Turn(workRot,dt);
                    if(glanceUntil>0&&Time.time>=glanceUntil){glanceUntil=0;PlayBehaviour();}
                    if(Current>=0&&Time.time>=until&&!Carried)Next();
                    break;
            }
            float target=(Glancing||State==Phase.Talk||State==Phase.Return)&&player!=null&&Humanoid?Profile.HeadWeight:0;
            if(target>0&&Vector3.Angle(Body.forward,Vector3.ProjectOnPlane(look-Body.position,Vector3.up))>110)target=0;
            LookWeight=Mathf.MoveTowards(LookWeight,target,udt/Mathf.Max(.05f,Profile.HeadBlendSeconds));}
        }
        void OnAnimatorIK(int layer)
        {
            if(!Humanoid||Animator==null)return;
            if(LookWeight<=0&&appliedLook==0)return;   // #307 item 11: weight 0 is already set; nothing to aim
            Animator.SetLookAtWeight(LookWeight,0,1,0,Profile!=null?Profile.HeadClamp:.5f);appliedLook=LookWeight;
            if(LookWeight>0)Animator.SetLookAtPosition(look);
        }
        void BeginGlance()
        {
            if(State!=Phase.Work||!Humanoid||seated||standing||Carried||Profile.GlanceHoldSeconds<=0||!Animator.HasState(0,hGlance))return;
            Play(hGlance,"Glance");glanceUntil=Time.time+Profile.GlanceHoldSeconds;
        }
        void Next()
        {
            var list=Profile.Behaviours??Array.Empty<NpcJobProfileSO.Behaviour>();
            float total=0;foreach(var b in list)if(b!=null&&b.Weight>0)total+=b.Weight;
            if(total<=0){Current=-1;until=float.MaxValue;if(Humanoid)Play(hIdle,"Idle");return;}
            float pick=(float)rng.NextDouble()*total;int next=-1;
            for(int i=0;i<list.Length;i++){if(list[i]==null||list[i].Weight<=0)continue;next=i;pick-=list[i].Weight;if(pick<=0)break;}
            // leave a seat through StandUp before the next behaviour starts
            if(seated&&Humanoid&&list[next].State!=NpcJobProfileSO.Slot.Sit&&Animator.HasState(0,hStandUp))
            {seated=false;standing=true;standSince=Time.time;pendingNext=next;Play(hStandUp,"StandUp");ShowTool(-1);return;}
            Begin(next);
        }
        void Begin(int i)
        {
            if(i<0||Profile.Behaviours==null||i>=Profile.Behaviours.Length||Profile.Behaviours[i]==null){Current=-1;until=float.MaxValue;if(Humanoid)Play(hIdle,"Idle");return;}
            var b=Profile.Behaviours[i];Current=i;
            // workplace offset in world metres along the home facing, clamped so the NPC stays inside radius - margin of its point
            Vector3 o=b.Offset;o.y=0;if(o.magnitude>Allowed)o=o.normalized*Allowed;
            var parent=Body.parent;Vector3 home=parent!=null?parent.TransformPoint(homePos):homePos;
            Vector3 w=(parent!=null?parent.rotation:Quaternion.identity)*homeRot*o;w.y=0;if(w.sqrMagnitude>.0001f)w=w.normalized*o.magnitude;
            spot=home+w;workRot=homeRot*Quaternion.Euler(0,b.Yaw,0);
            ShowTool(i);
            if(Flat(Body.position,spot)>.15f&&!Carried){State=Phase.Walk;seated=false;Play(hWalk,"Walk");return;}
            State=Phase.Work;PlayBehaviour();
        }
        void PlayBehaviour()
        {
            if(Current<0){if(Humanoid)Play(hIdle,"Idle");return;}
            var b=Profile.Behaviours[Current];until=Time.time+Mathf.Lerp(b.MinSeconds,Mathf.Max(b.MinSeconds,b.MaxSeconds),(float)rng.NextDouble());
            if(!Humanoid)return;
            if(b.State==NpcJobProfileSO.Slot.Sit){if(!seated){if(Animator.HasState(0,hSitDown))Play(hSitDown,"SitDown");else Play(hSit,"Sit");}seated=true;return;}
            seated=false;string n=SlotName(b.State);Play(Animator.StringToHash(n),n);
        }
        static string SlotName(NpcJobProfileSO.Slot s)=>s==NpcJobProfileSO.Slot.Work0?"Work0":s==NpcJobProfileSO.Slot.Work1?"Work1":s==NpcJobProfileSO.Slot.Work2?"Work2":s==NpcJobProfileSO.Slot.Sit?"Sit":"Idle";
        void Walk(float dt)
        {
            if(dt<=0)return;
            Vector3 to=spot-Body.position;to.y=0;float step=Profile.WalkSpeed*dt;
            if(to.magnitude<=step){Body.position=new Vector3(spot.x,Body.position.y,spot.z);Ground();State=Phase.Work;PlayBehaviour();return;}
            Body.position+=to.normalized*step;Ground();
            var face=Quaternion.LookRotation(to.normalized,Vector3.up);Turn(Body.parent!=null?Quaternion.Inverse(Body.parent.rotation)*face:face,dt);
        }
        // keep the feet on whatever is below (terrain, floor), ignoring this point's own volumes and props and the player
        void Ground()
        {
            var p=Body.position;float best=float.MaxValue;float y=p.y;
            var walker=Session!=null&&Session.Walker!=null&&Session.Walker.Body!=null?Session.Walker.Body.transform:null;
            int n=Physics.RaycastNonAlloc(p+Vector3.up*1.2f,Vector3.down,hits,2.6f,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<n;i++)
            {
                var t=hits[i].transform;
                if(pointRoot!=null&&t.IsChildOf(pointRoot)||t.IsChildOf(Body)||walker!=null&&t.IsChildOf(walker))continue;
                if(hits[i].distance<best){best=hits[i].distance;y=hits[i].point.y;}
            }
            if(best<float.MaxValue&&Mathf.Abs(y-p.y)<.8f)Body.position=new Vector3(p.x,y,p.z);
        }
        void TurnToPlayer(Transform player,float dt)
        {
            if(player==null||seated||standing||dt<=0)return;
            Vector3 dir=Vector3.ProjectOnPlane(player.position-Body.position,Vector3.up);if(dir.sqrMagnitude<.0001f)return;
            var world=Quaternion.LookRotation(dir,Vector3.up);var local=Body.parent!=null?Quaternion.Inverse(Body.parent.rotation)*world:world;
            float baseYaw=workRot.eulerAngles.y,delta=Mathf.Clamp(Mathf.DeltaAngle(baseYaw,local.eulerAngles.y),-Profile.MaxTurnDegrees,Profile.MaxTurnDegrees);
            Turn(Quaternion.Euler(0,baseYaw+delta,0),dt);
        }
        void Turn(Quaternion localTarget,float dt)
        {
            if(dt<=0)return;
            var e=Body.localEulerAngles;float y=Mathf.MoveTowardsAngle(e.y,localTarget.eulerAngles.y,Profile.TurnDegreesPerSecond*dt);
            if(y==e.y)return;   // #307 item 11: settled - re-writing Euler(localEulerAngles) only re-rounded it and dirtied the whole rig
            Body.localRotation=Quaternion.Euler(e.x,y,e.z);
        }
        void ShowTool(int i){for(int k=0;k<Tools.Length;k++)if(Tools[k]!=null)Tools[k].SetActive(k==i);}
        void Play(int hash,string name)
        {
            if(Animator==null||!Animator.isActiveAndEnabled||playing==name)return;
            if(!Animator.HasState(0,hash)){if(name=="Idle"||!Animator.HasState(0,hIdle))return;hash=hIdle;name="Idle";if(playing==name)return;}
            Animator.CrossFadeInFixedTime(hash,Profile!=null?Profile.CrossFadeSeconds:.25f);playing=name;
        }
        static int StableHash(string s){unchecked{int h=17;foreach(char ch in s??"")h=h*31+ch;return h;}}
        static float Flat(Vector3 a,Vector3 b){a.y=0;b.y=0;return Vector3.Distance(a,b);}
    }
}
