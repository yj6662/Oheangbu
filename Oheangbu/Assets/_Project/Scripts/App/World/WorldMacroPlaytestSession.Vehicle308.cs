using System;
using System.Collections.Generic;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #308 D308-8 (SPEC-VEHICLE-UX-308) [TEST] — supersedes the Stage308_session copy of this file (D308-2 §3 vehicle gate).
    //   · 청룡 뒤 잠금을 없앤다: VehicleAvailable = 첫 의뢰 수령(OpeningCommissionReceived)뿐. Content.VehicleRequiredFact는 LEGACY(읽지 않음).
    //     호송은 그대로다 — 청룡 전 탑승해도 EscortBoarded가 출발을 만들지 않는다(escort stage 선행 미충족).
    //   · 보스 필드: VehicleBossFieldSO(세션 필드 → Resources "Vehicle308/VehicleBossFields308"). 중심은 이 씬 콘텐츠의 Encounter.Feet.
    //   · 탑승 훅(WorldMacroPalanquinSeat.BoardingAllowed, PineRestTransportCheck 선례): 차나 플레이어가 보스 필드 안이면, 또는 먹 연출
    //     중이면 탑승하지 않는다. 앞서 걸린 훅은 지키고 함께 묻는다(&&). 탑승 문에 첫 의뢰 조건은 더하지 않는다(#308 이전과 같음).
    //   · F 하나: 이 워커의 좌석은 InteractKeyExternal(E 끔). 세션 Update가 #306 새 누름 규칙(TakeFreshInteractPress)으로 탑승·하차를
    //     부른다. 상호작용 지점·조각과 겨루면 더 가까운 쪽(또는 시선이 10° 이상 더 향한 좌석)이 F를 받는다. 프롬프트는 HUD 상호작용 프롬프트("[F] 탑승"/"[F] 하차").
    //   · 보이는 탑승: 좌석마다 VehicleRider308(런타임 추가, 씬 저장 없음).
    // 정적 필드 없음. 묶기는 BindDemoEscort 끝(Start·ConfigureDemoEscort), 풀기는 OnDestroy.
    public sealed partial class WorldMacroPlaytestSession
    {
        [Header("#308 마석 자동차 UX [TEST]")]
        [Tooltip("Empty = Resources Vehicle308/VehicleUx308Profile.")]
        public VehicleUx308ProfileSO VehicleUxProfile;
        [Tooltip("Empty = Resources Vehicle308/VehicleBossFields308.")]
        public VehicleBossFieldSO VehicleBossFields;

        public bool VehicleAvailable=>OpeningCommissionReceived;

        // the serialized field wins; otherwise Resources is asked once per session instance (a miss is not retried every frame)
        VehicleUx308ProfileSO uxProfile308;VehicleBossFieldSO bossFields308;bool uxLooked308,fieldsLooked308;
        public VehicleUx308ProfileSO VehicleUxProfile308
        {
            get
            {
                if(VehicleUxProfile!=null)return uxProfile308=VehicleUxProfile;
                if(!uxLooked308){uxLooked308=true;uxProfile308=Resources.Load<VehicleUx308ProfileSO>(VehicleUx308ProfileSO.ResourcesPath);}
                return uxProfile308;
            }
        }
        public VehicleBossFieldSO VehicleBossFields308
        {
            get
            {
                if(VehicleBossFields!=null)return bossFields308=VehicleBossFields;
                if(!fieldsLooked308){fieldsLooked308=true;bossFields308=Resources.Load<VehicleBossFieldSO>(VehicleUx308ProfileSO.BossFieldsResourcesPath);}
                return bossFields308;
            }
        }

        /// <summary>#308 D308-8: true when <paramref name="point"/> (+ <paramref name="extra"/> m of footprint) lies in an enabled boss field.</summary>
        public bool InVehicleBossField308(Vector3 point,float extra,out string fieldId)
        {
            fieldId=null;var fields=VehicleBossFields308;
            if(fields==null||fields.Fields==null)return false;
            foreach(var f in fields.Fields)
            {
                if(f==null||!f.Enabled)continue;
                if(f.OpenAfterDefeat&&Progress?.defeated!=null&&Progress.defeated.Contains(string.IsNullOrEmpty(f.EncounterId)?f.Id:f.EncounterId))continue;
                if(VehicleBossFieldSO.Contains(f,BossFieldCentre308(f),point,extra)){fieldId=f.Id;return true;}
            }
            return false;
        }
        public Vector3 BossFieldCentre308(VehicleBossFieldSO.Field f)
        {
            if(f!=null&&!string.IsNullOrEmpty(f.EncounterId)&&Content?.Encounters!=null)
                foreach(var e in Content.Encounters)if(e!=null&&e.Id==f.EncounterId)return e.Feet;
            return f!=null?f.Centre:default;
        }

        sealed class VehicleSeatHook308{public WorldMacroPalanquinSeat Seat;public Func<bool> Prior,Gate;public bool PriorExternal;}
        readonly List<VehicleSeatHook308> vehicleHooks308=new List<VehicleSeatHook308>();
        WorldMacroPalanquinSeat vehicleSeat308;WorldMacroPalanquinSummon vehicleSummon308;
        // 0 none, 1 board ([F] 탑승), 2 exit ([F] 하차) — the HUD prompt and the F routing of this frame
        int vehicleFocus308;
        public int VehicleFocus308=>vehicleFocus308;
        public WorldMacroPalanquinSeat VehicleSeat308=>vehicleSeat308;
        public int VehicleBoards308{get;private set;}
        public int VehicleExits308{get;private set;}

        // Called at the end of BindDemoEscort (Start, and ConfigureDemoEscort in Play); re-binding replaces the previous hooks.
        void BindVehicle308()
        {
            UnbindVehicle308();
            var profile=VehicleUxProfile308;
            foreach(var seat in VehicleSeats308())
            {
                var hook=new VehicleSeatHook308{Seat=seat,Prior=seat.BoardingAllowed,PriorExternal=seat.InteractKeyExternal};
                var prior=hook.Prior;var bound=seat;
                hook.Gate=prior==null?(Func<bool>)(()=>VehicleBoardingAllowed308(bound)):()=>VehicleBoardingAllowed308(bound)&&prior();
                seat.BoardingAllowed=hook.Gate;
                if(profile!=null)seat.InteractKeyExternal=true;   // without the #308 profile the scene keeps the legacy E path
                vehicleHooks308.Add(hook);
                if(vehicleSeat308==null||seat==DemoEscortSeat)vehicleSeat308=seat;
                if(vehicleSummon308==null)vehicleSummon308=SummonFor308(seat);
                if(profile!=null&&Walker!=null)
                {
                    var rider=seat.GetComponent<VehicleRider308>()??seat.gameObject.AddComponent<VehicleRider308>();
                    rider.Bind(seat,Walker,profile);
                }
            }
        }
        void UnbindVehicle308()
        {
            foreach(var hook in vehicleHooks308)
            {
                if(hook.Seat==null)continue;
                if(hook.Seat.BoardingAllowed==hook.Gate)hook.Seat.BoardingAllowed=hook.Prior;
                hook.Seat.InteractKeyExternal=hook.PriorExternal;
                var rider=hook.Seat.GetComponent<VehicleRider308>();if(rider!=null)rider.Unbind();
            }
            vehicleHooks308.Clear();vehicleSeat308=null;vehicleSummon308=null;vehicleFocus308=0;focusedVehicleSeat308=null;
        }
        // The escort seat, the summoned car's seat, and any seat of this scene that this walker drives (there is one car).
        List<WorldMacroPalanquinSeat> VehicleSeats308()
        {
            var seats=new List<WorldMacroPalanquinSeat>();
            void Add(WorldMacroPalanquinSeat seat){if(seat!=null&&seat.gameObject.scene==gameObject.scene&&!seats.Contains(seat))seats.Add(seat);}
            Add(DemoEscortSeat);
            if(DemoEscortSummon!=null)Add(DemoEscortSummon.Seat);
            foreach(var seat in FindObjectsByType<WorldMacroPalanquinSeat>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if(seat!=null&&seat.CombatWalker!=null&&seat.CombatWalker==Walker)Add(seat);
            return seats;
        }
        WorldMacroPalanquinSummon SummonFor308(WorldMacroPalanquinSeat seat)
        {
            if(DemoEscortSummon!=null&&(DemoEscortSummon.Seat==seat||seat!=null&&DemoEscortSummon.Vehicle==seat.Vehicle))return DemoEscortSummon;
            foreach(var s in FindObjectsByType<WorldMacroPalanquinSummon>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
                if(s!=null&&(s.Seat==seat||seat!=null&&s.Vehicle==seat.Vehicle))return s;
            return null;
        }

        /// <summary>#308 boarding rule: not while the car or the player stands in a boss field, not while the car forms / scatters in ink.</summary>
        public bool VehicleBoardingAllowed308(WorldMacroPalanquinSeat seat)
        {
            if(seat==null||seat.Vehicle==null)return true;
            // the bound summon (one car per scene); a scene search only for a seat that was not bound here
            var summon=vehicleSummon308!=null&&vehicleSummon308.Vehicle==seat.Vehicle?vehicleSummon308:BoundSeat308(seat)?null:SummonFor308(seat);
            if(summon!=null&&summon.InkBusy308)return false;
            if(InVehicleBossField308(seat.Vehicle.transform.position,0f,out _))return false;
            if(Walker!=null&&Walker.Body!=null&&InVehicleBossField308(Walker.Body.transform.position,0f,out _))return false;
            return true;
        }

        bool BoundSeat308(WorldMacroPalanquinSeat seat){for(int i=0;i<vehicleHooks308.Count;i++)if(vehicleHooks308[i].Seat==seat)return true;return false;}   // no per-frame closure

        // ---------- F routing (called from Update after ResolveFocus / while seated) ----------

        string VehicleInteractTarget308=>uxProfile308!=null&&!string.IsNullOrEmpty(uxProfile308.InteractTarget)?uxProfile308.InteractTarget:"vehicle308";
        string VehiclePromptText308=>vehicleFocus308==1&&uxProfile308!=null?PromptText(uxProfile308.BoardPrompt):vehicleFocus308==2&&uxProfile308!=null?PromptText(uxProfile308.ExitPrompt):null;

        // Walking: a parked car takes the focus when it can be boarded and its seat is nearer than the focused point / pickup, or
        // (#308 review) when the view looks more squarely at the seat than at that point — at the escort station the escort_start
        // point stands within 4.5 m of the seat, and plain distance must not leave the car unboardable with F.
        // Every bound seat is a candidate (each one has its E path off). Returns true when this frame's F belongs to a car
        // (FocusedId / bundle are cleared then).
        WorldMacroPalanquinSeat focusedVehicleSeat308;
        bool ResolveVehicleFocus308(Vector3 feet)
        {
            vehicleFocus308=0;focusedVehicleSeat308=null;
            if(Walker==null||Walker.Body==null)return false;
            WorldMacroPalanquinSeat best=null;float bestDistance=float.MaxValue;
            for(int i=0;i<vehicleHooks308.Count;i++)
            {
                var seat=vehicleHooks308[i].Seat;
                if(seat==null||!seat.InteractKeyExternal||seat.Occupied||seat.Vehicle==null||seat.Vehicle.Profile==null||seat.SeatSocket==null||
                   !seat.Vehicle.isActiveAndEnabled)continue;
                float d=Vector3.Distance(Walker.Body.bounds.center,seat.SeatSocket.position);
                if(d>seat.Vehicle.Profile.BoardingDistance||d>=bestDistance)continue;   // cheap reject before the full CanBoard
                if(!seat.CanBoard)continue;
                best=seat;bestDistance=d;
            }
            if(best==null)return false;
            Vector3 other=default;bool hasOther=false;
            if(!string.IsNullOrEmpty(FocusedCollectionBundleId)){var pickup=FindFragmentPickup(FocusedCollectionBundleId);if(pickup!=null){other=pickup.InteractionPosition;hasOther=true;}}
            else if(!string.IsNullOrEmpty(FocusedId)){if(FocusedId=="CurrencyDrop"){other=Progress.ledger.dropPosition;hasOther=true;}else if(TryInteractionGeometry(FocusedId,out var at,out _)){other=at;hasOther=true;}}
            // #308 review: both sides are measured the same way — flat metres from the feet. (bestDistance is body centre → the
            // seat socket at eye height in the cabin, ~2 m up; comparing it with a feet → point distance favoured whichever was lower.)
            if(hasOther&&Flat308(other,feet)<=Flat308(best.SeatSocket.position,feet)&&!ViewPrefersSeat308(best.SeatSocket.position,other))return false;
            FocusedId=null;FocusedCollectionBundleId=null;vehicleFocus308=1;focusedVehicleSeat308=best;return true;
        }
        static float Flat308(Vector3 a,Vector3 b){a.y=0;b.y=0;return Vector3.Distance(a,b);}
        // true when the view direction is at least 10° closer to the seat than to the competing point (flat angles)
        bool ViewPrefersSeat308(Vector3 seatAt,Vector3 otherAt)
        {
            var cam=Walker.ViewCamera!=null?Walker.ViewCamera.transform:null;if(cam==null)return false;
            Vector3 view=Vector3.ProjectOnPlane(cam.forward,Vector3.up),toSeat=Vector3.ProjectOnPlane(seatAt-cam.position,Vector3.up),toOther=Vector3.ProjectOnPlane(otherAt-cam.position,Vector3.up);
            if(view.sqrMagnitude<1e-6f||toSeat.sqrMagnitude<1e-6f||toOther.sqrMagnitude<1e-6f)return false;
            return Vector3.Angle(view,toSeat)+10f<Vector3.Angle(view,toOther);
        }
        void BoardFocusedVehicle308()
        {
            var seat=focusedVehicleSeat308!=null?focusedVehicleSeat308:vehicleSeat308;
            if(seat!=null&&seat.TryBoard())VehicleBoards308++;
        }
        // Seated: the stopped car shows "[F] 하차"; a fresh F exits (the press that boarded can never exit: it must be released first).
        void TickSeatedVehicle308()
        {
            vehicleFocus308=0;
            WorldMacroPalanquinSeat seat=null;
            foreach(var hook in vehicleHooks308)if(hook.Seat!=null&&hook.Seat.Occupied){seat=hook.Seat;break;}
            if(seat==null||!seat.InteractKeyExternal||seat.Vehicle==null||seat.Vehicle.Profile==null)return;
            bool stopped=seat.Vehicle.Speed<=seat.Vehicle.Profile.ExitMaximumSpeed;
            if(stopped)vehicleFocus308=2;
            if(TakeFreshInteractPress(VehicleInteractTarget308)&&stopped&&seat.TryExit())VehicleExits308++;
        }
    }
}
