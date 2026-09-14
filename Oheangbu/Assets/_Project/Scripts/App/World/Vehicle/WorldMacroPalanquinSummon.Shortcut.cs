using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Oheangbu.App.World.Vehicle
{
    public sealed partial class WorldMacroPalanquinSummon
    {
        public WorldMacroPlaytestSession Session;
        public WorldMacroPlayerGestureRig Gesture;
        public Transform TemporaryPendant;
        [Min(.8f)] public float GestureSeconds = 1.8f;
        [Range(.35f,.7f)] public float CallMoment = .52f;
        [Min(10)] public float RecallDistance = 30f;
        [Min(0)] public float ExitGraceSeconds = 2f;
        public bool Calling { get; private set; }
        public bool IsRecalled { get; private set; }
        public bool RecallArmed { get; private set; }
        public int Recalls { get; private set; }
        public float GestureProgress => Mathf.Clamp01(elapsed/Mathf.Max(.8f,GestureSeconds));
        float elapsed, exitGrace, awaySeconds;
        bool attempted, ownsGate;
        GameplayUiGate actionGate;
        PlayerVitals callVitals;
        PauseCoordinator pause;

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
                if(!TryBeginShortcut(out var message)) Notify(message);
            }
            TickRecall(Time.deltaTime);
        }

        public bool TryBeginShortcut(out string message)
        {
            message=null;
            if(!Application.isPlaying||!isActiveAndEnabled||Calling)return Fail("오행부를 조작하는 중입니다.",out message);
            var ui=PlaytestUiRoot.Instance;
            if(ui==null||ui.IsTitle||ui.Pause==null||ui.Pause.Gate==null||ui.Pause.IsPaused||
                ui.Pause.Gate.InputBlocked||Time.timeScale<=0f)
                return Fail("메뉴를 닫고 보행 중 G를 눌러 주세요.",out message);
            if(Session==null||!Session.OpeningCommissionReceived)return Fail("먼저 관청 아전에게 조사 의뢰를 확인하세요.",out message);
            if(!Ready(out message)){LastResult=message;return false;}
            if(Seat.Occupied||Vehicle.DriverPresent||Walker.Seated)return Fail("차에서 내린 뒤 호출할 수 있습니다.",out message);
            if(Vehicle.Speed>.15f||Vehicle.Body.angularVelocity.magnitude>.1f||Mathf.Abs(Vehicle.AppliedMotorTorque)>.1f)
                return Fail("자동차가 완전히 멈춘 뒤 호출할 수 있습니다.",out message);
            if((callVitals!=null&&callVitals.Hp01<=0)||!Walker.Body.enabled||!Walker.Motor.enabled||
                !Walker.Motor.IsLocomotionGrounded||Walker.Motor.IsDrawing||Walker.Drawing.InDrawMode||
                Walker.Motor.IsDodging||Walker.Motor.IsHarvesting||Walker.Motor.IsSitting||Walker.Motor.IsCrouching)
                return Fail("땅에 서서 작도와 갈무리를 마친 뒤 G를 눌러 주세요.",out message);
            if(Time.unscaledTimeAsDouble<nextCall)return Fail("자동차가 자리를 잡는 중입니다.",out message);
            if(Gesture==null||TemporaryPendant==null||!Gesture.CanPresentPendant)
                return Fail("오행부 손동작 연결을 확인해야 합니다.",out message);
            Physics.SyncTransforms();
            if(!TryFindPlacement(Walker.Body.transform.position,Walker.Body.transform.forward,out _,out message))
            {LastResult=message;return false;}
            // The real pose is queried again at the tap; no destination is reserved through an interruption.
            if(!Gesture.BeginPendant(TemporaryPendant))return Fail("손동작을 준비하지 못했습니다.",out message);
            pause=ui.Pause;actionGate=pause.Gate;
            pause.PausedChanged+=OnPause;
            ownsGate=true;actionGate.Block();
            elapsed=0;attempted=false;Calling=true;
            message=LastResult="오행부로 자동차를 부릅니다.";
            return true;
        }

        void AdvanceCall(float dt)
        {
            if(!Calling)return;
            elapsed+=Mathf.Max(0,dt);
            Gesture.SetPendantProgress(GestureProgress);
            if(!attempted&&GestureProgress>=CallMoment)
            {
                attempted=true;
                Physics.SyncTransforms();
                if(!Seat.Occupied&&!Vehicle.DriverPresent&&Vehicle.Speed<=.15f&&
                    Vehicle.Body.angularVelocity.magnitude<=.1f&&
                    TryFindPlacement(Walker.Body.transform.position,Walker.Body.transform.forward,out var placement,out var reason))
                {
                    CommitPlacement(placement);
                    LastResult="큰길에 자동차를 불렀습니다. E로 탑승하세요.";
                    if(!Session.TryRecordOpeningVehicleSummoned(out var saveError))LastResult+="\n"+saveError;
                }
                else LastResult="호출할 자리가 바뀌었습니다. 넓은 큰길에서 다시 시도하세요.";
                Notify(LastResult);
            }
            if(GestureProgress>=1)EndCall();
        }

        public void CancelCall()
        {
            if(!Calling)return;
            LastResult=attempted?"오행부를 집어넣었습니다.":"자동차 호출을 취소했습니다.";
            EndCall();
        }
        void EndCall()
        {
            Calling=false;
            Gesture?.EndPendant();
            if(TemporaryPendant!=null)TemporaryPendant.gameObject.SetActive(false);
            if(pause!=null)pause.PausedChanged-=OnPause;
            // A newly opened menu owns this same gate now. Never release its block.
            if(ownsGate&&actionGate!=null&&(pause==null||!pause.IsPaused))actionGate.ReleaseWhenNeutral();
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
            if(!RecallArmed||IsRecalled||Calling||dt<=0||Session==null||Session.GameplayInputBlocked||
                Vehicle==null||Seat==null||Seat.Occupied||Vehicle.DriverPresent||Walker==null||Walker.Seated||
                !Walker.Body.enabled||(callVitals!=null&&callVitals.Hp01<=0))return;
            exitGrace=Mathf.Max(0,exitGrace-dt);
            Vector3 delta=Walker.Body.transform.position-Vehicle.transform.position;delta.y=0;
            if(delta.sqrMagnitude<RecallDistance*RecallDistance){awaySeconds=0;return;}
            if(exitGrace>0)return;
            awaySeconds+=dt;
            if(awaySeconds<.75f)return;
            Vehicle.StopDriverInputForUi();
            Vehicle.Body.linearVelocity=Vector3.zero;Vehicle.Body.angularVelocity=Vector3.zero;
            IsRecalled=true;RecallArmed=false;Recalls++;
            // Keep the one configured instance, but stop all renderers, physics, seats and drive VFX together.
            Vehicle.gameObject.SetActive(false);
            LastResult="멀어진 자동차를 오행부로 회수했습니다. 큰길에서 G로 다시 부를 수 있습니다.";
            Notify(LastResult);
        }
        void Notify(string message){if(!string.IsNullOrEmpty(message))PlaytestUiRoot.Instance?.ShowNotice(message,5);}
        void OnDisable(){CancelCall();}
        void OnDestroy()
        {
            if(Seat!=null){Seat.Boarded-=OnBoarded;Seat.Exited-=OnExited;}
            if(callVitals!=null){callVitals.Damaged-=OnDamage;callVitals.Died-=OnDeath;}
        }
    }
}
