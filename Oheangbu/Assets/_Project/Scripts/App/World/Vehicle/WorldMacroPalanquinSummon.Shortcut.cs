using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Oheangbu.App.World.Vehicle
{
    // #308 D308-8 (SPEC-VEHICLE-UX-308) [TEST]: supersedes the Stage308_session copy of this file.
    //   · G = 소환/회수 토글: 차가 살아 있고 RecallToggleDistance 안이면 회수, 아니면(멀거나 회수됨) 앞으로 부른다. 둘 다 같은 오행부 손동작.
    //   · 청룡 뒤 잠금(D308-2 §3 VehicleRequiredFact)은 없앴다: 문은 Session.VehicleAvailable(= 첫 의뢰 수령)뿐이다.
    //   · 보스 필드(D308-8b): 플레이어가 안에 있거나 앞자리(차 발자국 포함)가 안이면 G는 보이는 일을 하지 않는다 — 손동작·입력
    //     잠금·글 없음, 기존 실패 소리(vehicle_call_fail) 한 번뿐. 필드 안에서는 다른 이유의 거절도 같다. 필드 밖 거절은 그대로
    //     거절 손동작이다. 손동작 도중 조작 순간에 앞자리가 필드 안이면 차 없이 글 없이 끝난다(실패 소리). 회수는 소환이 아니라 그대로 된다.
    //   · 소환·회수·자동 회수는 먹 연출(VehicleInkPresentation308)로 차가 서고 흩어진다. 회복 회수(사망·지형)는 즉시다.
    //   · 호출 모션(D308-8c): 프로필이 CallStroke면 패 들어 보이기 대신 손에 든 붓으로 앞 허공에 짧은 획 하나(CallStrokeSeconds, 기본 1.0 s)를
    //     긋는다(WorldMacroPlayerGestureRig.BeginAirStroke308). 획이 허공을 떠나는 순간(StrokeContactEnd)이 조작 순간이다: 차를 놓고(소환)·
    //     먹 덮임을 시작하고(회수) 획의 먹이 차 자리로 날아가 먹 모임·덮임에 넘긴다(VehicleCallStroke308, lead = FlowSeconds × GatherAfterFlow).
    //     회수는 같은 획을 거꾸로(되감는 획) 긋는다. 거절 손동작(필드 밖)도 같은 획이고 먹은 그 자리에서 마른다. 행동 게이트는 획 동안만
    //     잠긴다. 피격·메뉴·사망은 획을 취소한다(조작 순간 전이면 차 없음). 표현 전용 — 인식기·필세 계산에 아무것도 넣지 않는다.
    //     프로필이 없거나 리그가 획을 못 그리면 예전 패 손동작(GestureSeconds·CallMoment)이다.
    // 플레이어는 옮기지 않는다(CONST-ANTIVISION 2). 정적 가변 필드 없음.
    public sealed partial class WorldMacroPalanquinSummon
    {
        public WorldMacroPlaytestSession Session;
        public WorldMacroPlayerGestureRig Gesture;
        public Transform TemporaryPendant;
        [Min(.8f)] public float GestureSeconds = 1.8f;
        [Range(.35f,.7f)] public float CallMoment = .52f;
        [Min(10)] public float RecallDistance = 30f;
        [Min(0)] public float ExitGraceSeconds = 2f;
        public bool NaturalPresentation;
        public WorldMacroPlaytestAudio Soundscape;
        bool declined, recalling;
        public bool Calling { get; private set; }
        public bool IsRecalled { get; private set; }
        public bool RecallArmed { get; private set; }
        public int Recalls { get; private set; }
        /// <summary>#308: G recalls taken through the gesture (subset of Recalls).</summary>
        public int ToggleRecalls { get; private set; }
        /// <summary>#308: true while the current gesture is a recall, not a summon.</summary>
        public bool RecallGesture => Calling && recalling;
        public float GestureProgress => Mathf.Clamp01(elapsed/GestureLength308);
        float elapsed, exitGrace, awaySeconds;
        bool attempted, ownsGate;
        // #308 D308-8c: the running gesture is the brush call stroke (else the legacy pendant); its call moment; the gather lead
        bool stroke308;
        float strokeCallMoment308, materializeLead308;
        VehicleCallStroke308 callStroke308;
        /// <summary>#308 D308-8c: the running gesture is the brush call stroke in the air.</summary>
        public bool StrokeGesture308 => Calling && stroke308;
        public VehicleCallStroke308 CallStroke308 => callStroke308;
        /// <summary>#308 D308-8c: seconds of the last finished or cancelled gesture (the action gate is held exactly this long).</summary>
        public float LastGestureSeconds308 { get; private set; }
        float GestureLength308 => stroke308 && UxProfile308 != null ? Mathf.Max(.3f, UxProfile308.CallStrokeSeconds) : Mathf.Max(.8f, GestureSeconds);
        float CallMomentNow308 => stroke308 ? strokeCallMoment308 : CallMoment;
        GameplayUiGate actionGate;
        PlayerVitals callVitals;
        PauseCoordinator pause;
        VehicleInkPresentation308 ink308;

        void Start()
        {
            if(Session==null)Session=GetComponent<WorldMacroPlaytestSession>();
            if(Seat!=null){Seat.Boarded+=OnBoarded;Seat.Exited+=OnExited;}
            if(Walker!=null&&Walker.Motor!=null)
            {
                callVitals=Walker.Motor.GetComponent<PlayerVitals>();
                if(callVitals!=null){callVitals.Damaged+=OnDamage;callVitals.Died+=OnDeath;}
            }
            if(TemporaryPendant!=null)TemporaryPendant.gameObject.SetActive(false);
        }

        void Update()
        {
            if(Calling)
            {
                if(Seat==null||Seat.Occupied||Walker==null||Walker.Seated||!Walker.Motor.enabled||
                   (callVitals!=null&&callVitals.Hp01<=0)) { CancelCall(); return; }
                if(Time.timeScale<=0f || (pause!=null&&pause.IsPaused)) { CancelCall(); return; }
                AdvanceCall(Time.deltaTime);
            }
            else if(Keyboard.current!=null&&Keyboard.current.gKey.wasPressedThisFrame)
            {
                // an ink summon / recall still running swallows G silently (no second gesture over it)
                if(InkBusy308)LastResult="G ignored: ink presentation running";
                else if(!TryToggleShortcut(out var message))
                {
                    // #308 D308-8b: in a boss field (player or front spot) the refusal shows nothing — no gesture, no gate, no text
                    if(SilentBossRefusal308())PresentSilentRefusal308();
                    else if(NaturalPresentation)TryPresentDeclinedCall();else Notify(message);
                }
            }
            TickJuice308();   // #308 juice C-4 / D-2b: the car's visual settle follows the ink presentation's phases (WorldMacroPalanquinSummon.Juice308.cs)
            TickRecall(Time.deltaTime);
        }

        // ---------- #308 data, boss fields, ink ----------

        VehicleUx308ProfileSO UxProfile308=>Session!=null?Session.VehicleUxProfile308:null;
        // #308 review: without the profile the seat keeps its E key (missing data = pre-#308 behaviour), so the old hint stays too
        string SummonedText308=>UxProfile308==null?"앞에 자동차를 불렀다. E로 탑승.":"앞에 자동차를 불렀다.";
        /// <summary>#308: the car is forming out of ink or scattering into ink right now (boarding and G wait).</summary>
        public bool InkBusy308=>ink308!=null&&ink308.Busy;
        public VehicleInkPresentation308 Ink308=>ink308;
        public bool BossFieldAt308(Vector3 point,float extra,out string field)
        {
            field=null;
            return Session!=null&&Session.InVehicleBossField308(point,extra,out field);
        }

        // ---------- #308 D308-8b silent boss-field refusal ----------

        // set by the latest TryToggleShortcut / TryBeginShortcut / TryBeginRecallShortcut when it refused for a boss field
        bool bossRefusal308;
        /// <summary>#308 D308-8b: the latest G entry was refused for a boss field (player or front spot).</summary>
        public bool LastRefusalBossField308=>bossRefusal308;
        /// <summary>#308 D308-8b: G refusals answered with nothing visible (no gesture, no action gate, no text).</summary>
        public int SilentRefusals308 { get; private set; }
        /// <summary>#308 D308-8b: true when the latest refusal was a boss field, or the player stands in one now. No declined
        /// gesture is shown then (the Update path and TryPresentDeclinedCall both ask this).</summary>
        public bool SilentBossRefusal308()=>bossRefusal308||Walker!=null&&Walker.Body!=null&&BossFieldAt308(Walker.Body.transform.position,0f,out _);
        // nothing visible: LastResult keeps the reason set by the refusal; the existing failure cue (vehicle_call_fail through
        // PresentVehicleCall) is the only answer, natural presentation only, and only where the declined gesture used to answer
        // (the same walker conditions as TryPresentDeclinedCall: on foot, grounded, not dodging / drawing / harvesting / sitting,
        // alive, no menu / pause / block) — G in a menu, in the car or mid-action stays fully silent. No gate, no Calling, no Notify.
        void PresentSilentRefusal308()
        {
            SilentRefusals308++;
            var ui=PlaytestUiRoot.Instance;
            if(!NaturalPresentation||Soundscape==null||ui==null||ui.IsMenuOpen||ui.Pause==null||ui.Gate==null||ui.Gate.InputBlocked||Time.timeScale<=0||
               Walker==null||Walker.Body==null||Walker.Seated||Walker.Motor==null||!Walker.Motor.enabled||!Walker.Motor.IsLocomotionGrounded||
               Walker.Motor.IsDodging||Walker.Motor.IsDrawing||Walker.Motor.IsHarvesting||Walker.Motor.IsSitting||
               (callVitals!=null&&callVitals.Hp01<=0))return;
            Soundscape.PresentVehicleCall(Walker.Body.transform.position,false);
        }
        /// <summary>#308 D308-8b: the front spot G / the menu would use (TryFindPlacement's candidate, hull footprint included) lies in
        /// a boss field. Read-only; the XZ of the candidate is final (the pose only settles its height), so this equals the check
        /// TryFindPlacement makes on the placed pose.</summary>
        public bool FrontSpotInBossField308(Vector3 playerFeet,Vector3 preferredForward,out string field)
        {
            field=null;
            if(Vehicle==null||Vehicle.Hull==null||Walker==null||Walker.Body==null)return false;
            FrontCandidate308(playerFeet,preferredForward,out var point,out _);
            return BossFieldAt308(point,FrontFootprint308,out field);
        }
        VehicleInkPresentation308 EnsureInk308()
        {
            var profile=UxProfile308;if(profile==null)return null;
            if(ink308==null)ink308=GetComponent<VehicleInkPresentation308>();
            if(ink308==null)ink308=gameObject.AddComponent<VehicleInkPresentation308>();
            ink308.Profile=profile;return ink308;
        }
        Transform[] InkExclusions308()
        {
            if(Session==null)return null;
            return new[]{Session.DemoEscortCompanion,Session.DemoEscortCargo,Walker!=null&&Walker.Body!=null?Walker.Body.transform:null};
        }
        void BeginMaterialize308()
        {
            var presentation=EnsureInk308();
            if(presentation!=null&&Vehicle!=null)presentation.BeginMaterialize(Vehicle,InkExclusions308(),materializeLead308);
        }
        /// <summary>Stops the car, then lets it scatter into ink; the car is deactivated (IsRecalled) when the ink is gone.
        /// Without a profile (other scenes) the recall is immediate, as before.</summary>
        void BeginInkRecall308(bool toggle,float lead=0f)
        {
            Vehicle.StopDriverInputForUi();
            Vehicle.Body.linearVelocity=Vector3.zero;Vehicle.Body.angularVelocity=Vector3.zero;
            RecallArmed=false;awaySeconds=0;
            if(toggle)ToggleRecalls++;
            var presentation=EnsureInk308();
            if(presentation!=null)presentation.BeginDissolve(Vehicle,InkExclusions308(),CompleteRecall308,lead);
            else CompleteRecall308();
        }
        void CompleteRecall308()
        {
            if(Vehicle==null||IsRecalled)return;
            if(Seat!=null&&Seat.Occupied)return;   // boarded during the scatter: impossible while busy (boarding hook), kept as a guard
            Vehicle.StopDriverInputForUi();
            if(Vehicle.Body!=null){Vehicle.Body.linearVelocity=Vector3.zero;Vehicle.Body.angularVelocity=Vector3.zero;}
            IsRecalled=true;RecallArmed=false;Recalls++;
            // Keep the one configured instance, but stop all renderers, physics, seats and drive VFX together.
            Vehicle.gameObject.SetActive(false);
        }

        /// <summary>#308 G: recall when the car is active and near (RecallToggleDistance), otherwise the existing front summon.</summary>
        public bool TryToggleShortcut(out string message)
        {
            bossRefusal308=false;
            if(ShouldRecallOnToggle308())return TryBeginRecallShortcut(out message);
            return TryBeginShortcut(out message);
        }
        public bool ShouldRecallOnToggle308()
        {
            if(Vehicle==null||IsRecalled||!Vehicle.gameObject.activeInHierarchy||Walker==null||Walker.Body==null)return false;
            // without the #308 profile (other scenes / data not authored) G keeps the legacy behaviour: always the front summon
            var profile=UxProfile308;if(profile==null)return false;float near=profile.RecallToggleDistance;
            Vector3 delta=Walker.Body.transform.position-Vehicle.transform.position;delta.y=0;
            return delta.sqrMagnitude<=near*near;
        }

        bool GestureGates(out string message,out PlaytestUiRoot ui)
        {
            message=null;ui=PlaytestUiRoot.Instance;
            if(!Application.isPlaying||!isActiveAndEnabled||Calling)return Fail("오행부를 조작하는 중입니다.",out message);
            if(ui==null||ui.IsTitle||ui.Pause==null||ui.Pause.Gate==null||ui.Pause.IsPaused||
                ui.Pause.Gate.InputBlocked||Time.timeScale<=0f)
                return Fail("메뉴를 닫고 보행 중 G를 누른다.",out message);
            return true;
        }
        bool WalkerReady(out string message)
        {
            message=null;
            if((callVitals!=null&&callVitals.Hp01<=0)||!Walker.Body.enabled||!Walker.Motor.enabled||
                !Walker.Motor.IsLocomotionGrounded||Walker.Motor.IsDrawing||Walker.Drawing.InDrawMode||
                Walker.Motor.IsDodging||Walker.Motor.IsHarvesting||Walker.Motor.IsSitting||Walker.Motor.IsCrouching)
                return Fail("땅에 서서 작도와 갈무리를 마친 뒤 G를 누른다.",out message);
            return true;
        }
        bool BeginGesture(PlaytestUiRoot ui,bool recall,out string message)
        {
            message=null;
            if(Gesture==null||(!CanStroke308()&&(TemporaryPendant==null||!Gesture.CanPresentPendant)))
                return Fail("오행부 손동작 연결을 확인해야 한다.",out message);
            if(!StartGesturePresentation308(recall))return Fail("손동작을 준비하지 못했다.",out message);
            pause=ui.Pause;actionGate=pause.Gate;
            pause.PausedChanged+=OnPause;
            ownsGate=true;actionGate.Block();
            elapsed=0;attempted=false;declined=false;recalling=recall;Calling=true;
            return true;
        }

        // ---------- #308 D308-8c call stroke ----------

        bool CanStroke308(){var p=UxProfile308;return p!=null&&p.CallStroke&&Gesture!=null&&Gesture.CanPresentAirStroke308;}
        // Starts the visible gesture: the brush call stroke when the profile asks for it and the rig can present it (recall = the same
        // stroke drawn backwards when RecallReversed), else the legacy pendant. Presentation only: nothing reaches the recognizer.
        bool StartGesturePresentation308(bool recall)
        {
            stroke308=false;
            if(CanStroke308())
            {
                var profile=UxProfile308;
                var shape=StrokeShape308(profile,recall&&profile.RecallReversed);
                if(!Gesture.BeginAirStroke308(shape))return false;
                stroke308=true;strokeCallMoment308=shape.ContactEnd;
                EnsureCallStroke308()?.Begin(Gesture);
                return true;
            }
            return Gesture!=null&&TemporaryPendant!=null&&Gesture.CanPresentPendant&&Gesture.BeginPendant(TemporaryPendant);
        }
        // the rig clamps the same way, so strokeCallMoment308 equals the moment the brush leaves the air
        static WorldMacroPlayerGestureRig.AirStroke308 StrokeShape308(VehicleUx308ProfileSO p,bool reverse)
        {
            float raise=Mathf.Clamp(p.StrokeRaiseEnd,.05f,.9f);
            return new WorldMacroPlayerGestureRig.AirStroke308
            {
                From=p.StrokeFrom,To=p.StrokeTo,Arc=p.StrokeArc,Reach=p.StrokeReach,RaiseEnd=raise,
                ContactEnd=Mathf.Clamp(p.StrokeContactEnd,raise+.05f,.98f),Ease=p.StrokeEase,Reverse=reverse,
                BodyParticipation=p.StrokeBodyParticipation,BodyTurnSpeed=p.StrokeBodyTurnSpeed,
                Seconds=Mathf.Max(.3f,p.CallStrokeSeconds)   // #308 juice C-1: the length GestureLength308 counts the progress with
            };
        }
        VehicleCallStroke308 EnsureCallStroke308()
        {
            var profile=UxProfile308;if(profile==null)return null;
            if(callStroke308==null)callStroke308=GetComponent<VehicleCallStroke308>();
            if(callStroke308==null)callStroke308=gameObject.AddComponent<VehicleCallStroke308>();
            callStroke308.Profile=profile;return callStroke308;
        }
        // the call moment of a stroke: its ink flies to the car (target) or dries where it is (null); legacy pendant: nothing
        // #308 juice C-3 / D-2a: the rig hears the answer of the call moment (target = the car comes or is taken back). Presentation only.
        void ReleaseStroke308(Vector3? target){if(stroke308&&Gesture!=null)Gesture.NotifyCallMoment308(target.HasValue);if(stroke308&&callStroke308!=null)callStroke308.Release(target);}
        float FlowLead308=>stroke308&&UxProfile308!=null?Mathf.Max(0f,UxProfile308.FlowSeconds*UxProfile308.GatherAfterFlow):0f;
        Vector3 CarCentre308(Vector3 fallback)=>ink308!=null&&ink308.Busy?ink308.CarBounds.center:Vehicle!=null&&Vehicle.Hull!=null?Vehicle.Hull.bounds.center:fallback;

        public bool TryBeginShortcut(out string message)
        {
            bossRefusal308=false;
            if(!GestureGates(out message,out var ui))return false;
            // #308 D308-8: the post-cheongryong gate (D308-2 §3) is gone; VehicleAvailable is the opening commission again.
            // No text: the natural presentation shows the declined pendant gesture (Update) — outside boss fields only (D308-8b);
            // LastResult keeps a diagnostic only.
            if(Session==null||!Session.VehicleAvailable){message=null;LastResult="vehicle locked: VehicleAvailable false";return false;}
            if(InkBusy308){message=null;LastResult="vehicle refused: ink presentation running";return false;}
            if(!Ready(out message)){LastResult=message;return false;}
            // #308 D308-8b boss field: right after the session / ink / wiring gates (Ready supplies the refs the test needs) and before
            // the seat / speed / walker / cooldown gates and any gesture. The player's feet or the front spot (hull footprint included)
            // in a field refuses without text; Update answers with nothing visible (SilentBossRefusal308). A refusal by the earlier
            // gates is silent too while the player stands in a field (SilentBossRefusal308's feet test).
            if(BossFieldAt308(Walker.Body.transform.position,0f,out var field)){bossRefusal308=true;message=null;LastResult="vehicle refused: boss field "+field;return false;}
            if(FrontSpotInBossField308(Walker.Body.transform.position,Walker.Body.transform.forward,out field)){bossRefusal308=true;message=null;LastResult="vehicle refused: boss field "+field+" (front spot)";return false;}
            if(Seat.Occupied||Vehicle.DriverPresent||Walker.Seated)return Fail("차에서 내린 뒤 부를 수 있다.",out message);
            if(Vehicle.Speed>.15f||Vehicle.Body.angularVelocity.magnitude>.1f||Mathf.Abs(Vehicle.AppliedMotorTorque)>.1f)
                return Fail("자동차가 완전히 멈춘 뒤 부를 수 있다.",out message);
            if(!WalkerReady(out message))return false;
            if(Time.unscaledTimeAsDouble<nextCall)return Fail("자동차가 자리를 잡는 중이다.",out message);
            Physics.SyncTransforms();
            if(!NaturalPresentation&&!TryFindPlacement(Walker.Body.transform.position,Walker.Body.transform.forward,out _,out message))
            {LastResult=message??LastResult;return false;}
            // The real pose is queried again at the tap; no destination is reserved through an interruption.
            if(!BeginGesture(ui,false,out message))return false;
            message=LastResult=stroke308?"붓으로 자동차를 부른다.":"오행부로 자동차를 부른다.";
            return true;
        }

        /// <summary>#308 G recall: the same pendant gesture; at the call moment the parked car scatters into ink. Recall is not a
        /// summon, so boss fields do not refuse it (a recall refused for another reason inside a field is still silent, D308-8b).
        /// The player is never moved.</summary>
        public bool TryBeginRecallShortcut(out string message)
        {
            bossRefusal308=false;
            if(!GestureGates(out message,out var ui))return false;
            if(InkBusy308){message=null;LastResult="recall refused: ink presentation running";return false;}
            if(!Ready(out message)){LastResult=message;return false;}
            if(IsRecalled||!Vehicle.gameObject.activeInHierarchy){message=null;LastResult="recall refused: no car out";return false;}
            if(Seat.Occupied||Vehicle.DriverPresent||Walker.Seated)return Fail("차에서 내린 뒤 회수할 수 있다.",out message);
            if(Vehicle.Speed>.15f||Vehicle.Body.angularVelocity.magnitude>.1f||Mathf.Abs(Vehicle.AppliedMotorTorque)>.1f)
                return Fail("자동차가 완전히 멈춘 뒤 회수할 수 있다.",out message);
            if(!WalkerReady(out message))return false;
            if(!BeginGesture(ui,true,out message))return false;
            message=LastResult=stroke308?"붓으로 자동차를 거둔다.":"오행부로 자동차를 거둔다.";
            return true;
        }

        /// <summary>The declined gesture for refusals outside boss fields (#308 D308-8c: the same call stroke, its ink dries in place;
        /// the legacy pendant without the profile). #308 D308-8b: never inside a boss field (or after a boss-field refusal): returns
        /// false there and shows nothing.</summary>
        public bool TryPresentDeclinedCall(){
            var ui=PlaytestUiRoot.Instance;
            if(SilentBossRefusal308())return false;
            if(!NaturalPresentation||Calling||ui==null||ui.IsMenuOpen||ui.Pause==null||ui.Gate.InputBlocked||Time.timeScale<=0||
                Walker==null||Walker.Seated||Walker.Motor==null||!Walker.Motor.enabled||!Walker.Motor.IsLocomotionGrounded||
                Walker.Motor.IsDodging||Walker.Motor.IsDrawing||Walker.Motor.IsHarvesting||Walker.Motor.IsSitting||
                (callVitals!=null&&callVitals.Hp01<=0)||Gesture==null||!StartGesturePresentation308(false))return false;
            pause=ui.Pause;actionGate=ui.Gate;pause.PausedChanged+=OnPause;ownsGate=true;actionGate.Block();
            elapsed=0;attempted=false;declined=true;recalling=false;Calling=true;DeclinedGestures308++;return true;
        }
        /// <summary>#308: declined pendant gestures shown (diagnostic for the boss-field AC).</summary>
        public int DeclinedGestures308 { get; private set; }

        void AdvanceCall(float dt)
        {
            if(!Calling)return;
            elapsed+=Mathf.Max(0,dt);
            if(stroke308)Gesture.SetAirStrokeProgress308(GestureProgress);else Gesture.SetPendantProgress(GestureProgress);
            if(!attempted&&GestureProgress>=CallMomentNow308)
            {
                attempted=true;
                Physics.SyncTransforms();
                if(recalling)
                {
                    if(!declined&&!IsRecalled&&Vehicle.gameObject.activeInHierarchy&&!Seat.Occupied&&!Vehicle.DriverPresent&&
                        Vehicle.Speed<=.15f&&Vehicle.Body.angularVelocity.magnitude<=.1f&&!InkBusy308)
                    {
                        Vector3 at=Vehicle.transform.position;
                        BeginInkRecall308(true,FlowLead308);
                        ReleaseStroke308(CarCentre308(at+Vector3.up*.8f));   // D308-8c: the stroke's ink flies onto the car, then it covers and scatters
                        if(NaturalPresentation)Soundscape?.PresentVehicleCall(at,true);
                        LastResult=stroke308?"자동차를 붓으로 거두었다.":"자동차를 오행부로 거두었다.";
                    }
                    else {ReleaseStroke308(null);LastResult="자동차를 거두지 못했다.";if(NaturalPresentation)Soundscape?.PresentVehicleCall(Walker.Body.transform.position,false);}
                    Notify(LastResult);
                }
                else if(declined)
                {
                    // the declined gesture (refusals outside boss fields, D308-8b): LastResult keeps the refusal reason set before it;
                    // nothing is placed and no text is shown (natural presentation only); D308-8c: the stroke's ink dries in place
                    ReleaseStroke308(null);
                    if(NaturalPresentation)Soundscape?.PresentVehicleCall(Walker.Body.transform.position,false);
                }
                else
                {
                    LastPlacementDiagnostic=null;
                    materializeLead308=FlowLead308;   // D308-8c: the placed car waits hidden while the stroke's ink flies in
                    Placement placement=default;
                    bool placed=!Seat.Occupied&&!Vehicle.DriverPresent&&Vehicle.Speed<=.15f&&
                        Vehicle.Body.angularVelocity.magnitude<=.1f&&!InkBusy308&&
                        TryFindPlacement(Walker.Body.transform.position,Walker.Body.transform.forward,out placement,out _)&&CommitPlacement(placement);
                    materializeLead308=0f;
                    if(placed)
                    {
                        ReleaseStroke308(CarCentre308(placement.Position+Vector3.up*.8f));
                        if(NaturalPresentation)Soundscape?.PresentVehicleCall(placement.Position,true);
                        LastResult=SummonedText308;
                        if(!Session.TryRecordOpeningVehicleSummoned(out var saveError))LastResult+="\n"+saveError;
                        Notify(LastResult);
                    }
                    else
                    {
                        // a boss-field refusal at the call moment (the front spot entered a field during the gesture — G already
                        // refuses a field before the gesture) stays wordless: no car, no text, the failure cue only, and the summon
                        // gesture runs out as it would (an abrupt EndPendant would snap the arm). The other failures keep their old
                        // notice (non-natural presentation only).
                        ReleaseStroke308(null);
                        bool boss=LastPlacementDiagnostic!=null&&LastPlacementDiagnostic.StartsWith("boss field",System.StringComparison.Ordinal);
                        if(boss)bossRefusal308=true;
                        LastResult=boss?"vehicle refused: "+LastPlacementDiagnostic:"앞에 자동차를 놓을 공간이 부족하다.";
                        if(NaturalPresentation)Soundscape?.PresentVehicleCall(Walker.Body.transform.position,false);
                        if(!boss)Notify(LastResult);
                    }
                }
            }
            if(GestureProgress>=1)EndCall();
        }

        public void CancelCall()
        {
            if(!Calling)return;
            LastResult=attempted?(stroke308?"자동차 호출 획을 거두었다.":"오행부를 집어넣었다."):recalling?"자동차 회수를 취소했다.":"자동차 호출을 취소했다.";
            EndCall();
        }
        void EndCall()
        {
            Calling=false;recalling=false;LastGestureSeconds308=elapsed;
            Gesture?.EndPendant();
            Gesture?.EndAirStroke308();
            if(stroke308)callStroke308?.Cancel();   // before the call moment the trail dries in place; after it this is a no-op
            // #308 D308-8c [TEST, user decision pending — profile StrokeGateWaitsForNeutral, default true = the rule below unchanged]:
            // a call stroke may hand the input back at once, so the lock is exactly the stroke and a move key held through it resumes.
            // Never on death (the death presentation takes this gate) or without focus (the gate's own focus block decides).
            bool releaseNow=stroke308&&UxProfile308!=null&&!UxProfile308.StrokeGateWaitsForNeutral&&
                Application.isFocused&&(callVitals==null||callVitals.Hp01>0);
            stroke308=false;
            if(TemporaryPendant!=null)TemporaryPendant.gameObject.SetActive(false);
            if(pause!=null)pause.PausedChanged-=OnPause;
            // A newly opened menu owns this same gate now. Never release its block.
            if(ownsGate&&actionGate!=null&&(pause==null||!pause.IsPaused))
            {
                // SPEC-VEHICLE-CALL: held G / combat / move keys return to neutral before input resumes (the default)
                if(releaseNow)actionGate.ReleaseImmediately();else actionGate.ReleaseWhenNeutral();
            }
            ownsGate=false;actionGate=null;pause=null;
        }
        void OnPause(bool paused){if(paused)CancelCall();}
        void OnDamage(float amount){if(amount>0)CancelCall();}
        void OnDeath(){CancelCall();}
        void OnApplicationFocus(bool focused){if(!focused)CancelCall();}
        void OnBoarded(){RecallArmed=false;awaySeconds=0;CancelCall();}
        void OnExited(){RecallArmed=true;exitGrace=ExitGraceSeconds;awaySeconds=0;}

        void TickRecall(float dt)
        {
            if(!RecallArmed||IsRecalled||Calling||InkBusy308||dt<=0||Session==null||Session.GameplayInputBlocked||
                Vehicle==null||Seat==null||Seat.Occupied||Vehicle.DriverPresent||Walker==null||Walker.Seated||
                !Walker.Body.enabled||(callVitals!=null&&callVitals.Hp01<=0))return;
            exitGrace=Mathf.Max(0,exitGrace-dt);
            Vector3 delta=Walker.Body.transform.position-Vehicle.transform.position;delta.y=0;
            if(delta.sqrMagnitude<RecallDistance*RecallDistance){awaySeconds=0;return;}
            if(exitGrace>0)return;
            awaySeconds+=dt;
            if(awaySeconds<.75f)return;
            // #308: the far car scatters into ink, then is deactivated (IsRecalled at the end of the presentation)
            BeginInkRecall308(false);
            LastResult="멀어진 자동차를 거두었다. G로 앞에 다시 부른다.";
            Notify(LastResult);
        }
        public bool RecallAfterRecovery()
        {
            if(Vehicle==null||Seat==null||Seat.Occupied)return false;
            CancelCall();
            if(ink308!=null&&ink308.Busy)ink308.Cancel();   // recovery never waits for ink: renderers back to their state first
            callStroke308?.Clear();                          // and no stroke ink keeps flying toward a car that is gone
            Vehicle.StopDriverInputForUi();
            if(Vehicle.Body!=null){Vehicle.Body.linearVelocity=Vector3.zero;Vehicle.Body.angularVelocity=Vector3.zero;}
            if(!IsRecalled)Recalls++;
            IsRecalled=true;RecallArmed=false;Vehicle.gameObject.SetActive(false);return true;
        }
        void Notify(string message){if(NaturalPresentation)return;if(!string.IsNullOrEmpty(message))PlaytestUiRoot.Instance?.ShowNotice(message,5);}
        void OnDisable(){CancelCall();}
        void OnDestroy()
        {
            if(Seat!=null){Seat.Boarded-=OnBoarded;Seat.Exited-=OnExited;}
            if(callVitals!=null){callVitals.Damaged-=OnDamage;callVitals.Died-=OnDeath;}
        }
    }
}
