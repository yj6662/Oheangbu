using System;
using System.Linq;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    public sealed partial class WorldMacroPlaytestSession
    {
        public const string OpeningStartedId="opening.village_office.started";
        public const string OpeningIntroductionSeenId="opening.village_office.intro_seen";
        public const string OpeningVehicleSummonedId="opening.village_office.vehicle_summoned";
        PrologueContentSO.Point[] interactionPoints=Array.Empty<PrologueContentSO.Point>();
        PrologueContentSO.Point[] boundAuthoredPoints;
        PrologueContentSO.Point boundOpeningPoint;
        WorldMacroOpeningProfileSO Opening=>Content!=null?Content.Opening:null;
        bool HasOpening=>WorldMacroOpeningProgress.Configured(Opening);
        public bool OpeningJourneyActive=>WorldMacroOpeningProgress.Enrolled(Progress,Opening);
        // Existing saves never acquire a new mandatory commissioning step.
        public bool OpeningCommissionReceived=>!OpeningJourneyActive||Progress.ledger.completed.Contains(WorldMacroOpeningProfileSO.CommissionId);
        public bool OpeningVehicleSummoned=>!OpeningJourneyActive||Progress.ledger.completed.Contains(OpeningVehicleSummonedId);
        public bool OpeningIntroductionPending=>ready&&!SaveBlocked&&OpeningJourneyActive&&!OpeningCommissionReceived&&
            !Progress.ledger.completed.Contains(OpeningIntroductionSeenId);
        public string OpeningIntroductionTitle=>HasOpening?Opening.IntroductionTitle:"";
        public string OpeningIntroductionText=>HasOpening?Opening.IntroductionText:"";
        public string OpeningObjective
        {
            get
            {
                if(!OpeningJourneyActive)return null;
                if(!OpeningCommissionReceived)return Opening.BeforeCommissionObjective;
                if(Progress.ledger.completed.Contains(Opening.EvidenceInteractionId))return null;
                return OpeningVehicleSummoned?Opening.BeforeEvidenceObjective:Opening.BeforeVehicleObjective;
            }
        }
        public string CheckpointDisplayName=>Progress?.ledger?.checkpoint==WorldMacroOpeningProfileSO.CheckpointId?"마을 관청":
            Progress?.ledger?.checkpoint=="geumpyo_inn"?"금표 주막":"폐광";

        void BindOpeningPoints()
        {
            var authored=Content.Points??Array.Empty<PrologueContentSO.Point>();
            boundAuthoredPoints=Content.Points;
            boundOpeningPoint=HasOpening?Opening.Commission:null;
            interactionPoints=HasOpening?
                authored.Where(p=>p!=null&&p.Id!=WorldMacroOpeningProfileSO.CommissionId).Concat(new[]{Opening.Commission}).ToArray():
                authored.Where(p=>p!=null).ToArray();
        }
        PrologueContentSO.Point[] InteractionPoints
        {
            get
            {
                // Preserve the original live Content.Points contract for scoped authoring/review replacements.
                if(Content!=null&&(!ReferenceEquals(boundAuthoredPoints,Content.Points)||!ReferenceEquals(boundOpeningPoint,HasOpening?Opening.Commission:null)))BindOpeningPoints();
                return interactionPoints;
            }
        }
        PrologueContentSO.Point FindInteractionPoint(string id)=>Array.Find(InteractionPoints,p=>p.Id==id);

        WorldMacroProgress CreateFreshProgress()
        {
            // A corrupt save remains an unsaved fallback at the original mine, never a new office journey.
            bool office=LoadStatus=="new"&&Opening!=null&&Opening.Enabled;
            if(office&&!Opening.IsConfigured)throw new InvalidOperationException("Village opening profile needs finite spawn/commission data, conversation ID village_commission, and zero reward.");
            if(office&&!TrySafeFeet(Opening.StartFeet,out _))throw new InvalidOperationException("Village opening has no safe authored spawn; original mine start was not substituted.");
            return WorldMacroOpeningProgress.CreateNew(Content,LoadStatus=="new");
        }

        Vector3 AuthoredCheckpointFeet()=>WorldMacroOpeningProgress.CheckpointFeet(Content,Progress);
        float CheckpointYaw()=>WorldMacroOpeningProgress.CheckpointYaw(Content,Progress);

        public bool TryMarkOpeningIntroductionSeen(out string error)
        {
            if(!OpeningJourneyActive){error=null;return true;}
            return TrySaveOpeningMarker(OpeningIntroductionSeenId,out error);
        }
        public bool TryRecordOpeningVehicleSummoned(out string error)
        {
            if(!OpeningJourneyActive){error=null;return true;}
            if(!OpeningCommissionReceived){error="먼저 관청 아전에게 폐광 조사 의뢰를 확인하세요.";return false;}
            return TrySaveOpeningMarker(OpeningVehicleSummonedId,out error);
        }
        bool TrySaveOpeningMarker(string id,out string error)
        {
            if(!ready||Progress?.ledger?.completed==null){error="진행을 준비하고 있습니다.";return false;}
            if(SaveBlocked){error=SaveError??"저장 원본을 복구한 뒤 진행해 주세요.";return false;}
            if(Progress.ledger.completed.Contains(id)){error=null;return true;}
            Progress.ledger.completed.Add(id);
            if(SaveNow(out error))return true;
            Progress.ledger.completed.Remove(id);
            return false;
        }
    }
}
