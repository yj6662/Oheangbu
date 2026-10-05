using System;
using System.Collections.Generic;
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
                if(DemoCampaignActive)return DemoObjective;
                if(!OpeningJourneyActive)return null;
                if(!OpeningCommissionReceived)return Opening.BeforeCommissionObjective;
                if(Progress.ledger.completed.Contains(Opening.EvidenceInteractionId))return null;
                return OpeningVehicleSummoned?Opening.BeforeEvidenceObjective:Opening.BeforeVehicleObjective;
            }
        }
        public string CheckpointDisplayName=>WorldMacroCheckpointRules.Label(Content,Progress,Progress?.ledger?.checkpoint);

        void BindOpeningPoints()
        {
            var authored=Content.Points??Array.Empty<PrologueContentSO.Point>();
            boundAuthoredPoints=Content.Points;
            boundOpeningPoint=HasOpening?Opening.Commission:null;
            interactionPoints=HasOpening?
                authored.Where(p=>p!=null&&p.Id!=WorldMacroOpeningProfileSO.CommissionId).Concat(new[]{Opening.Commission}).ToArray():
                authored.Where(p=>p!=null).ToArray();
            IndexInteractionPoints();
        }
        // #307 phase 1 item 2: Id -> first point with that Id in interactionPoints, the answer Array.Find gave (same rebind rule:
        // rebuilt with interactionPoints; re-indexed if the array was swapped). A null entry keeps the original scan (and its throw).
        readonly Dictionary<string,PrologueContentSO.Point> pointById=new Dictionary<string,PrologueContentSO.Point>(StringComparer.Ordinal);
        PrologueContentSO.Point[] pointByIdSource;PrologueContentSO.Point firstNullIdPoint;bool pointsHaveNull;
        void IndexInteractionPoints()
        {
            pointById.Clear();firstNullIdPoint=null;pointsHaveNull=false;var points=interactionPoints??Array.Empty<PrologueContentSO.Point>();
            foreach(var p in points)
            {
                if(p==null){pointsHaveNull=true;continue;}
                if(p.Id==null){if(firstNullIdPoint==null)firstNullIdPoint=p;continue;}
                if(!pointById.ContainsKey(p.Id))pointById.Add(p.Id,p);
            }
            pointByIdSource=interactionPoints;
        }
        PrologueContentSO.Point RawInteractionPoint(string id)
        {
            var points=InteractionPoints;
            if(!ReferenceEquals(pointByIdSource,points))IndexInteractionPoints();
            if(pointsHaveNull)return Array.Find(points,p=>p.Id==id);
            if(id==null)return firstNullIdPoint;
            return pointById.TryGetValue(id,out var point)?point:null;
        }
        // CanInteract geometry without building a view: DemoInteractionPoint copies Position/Radius unchanged and
        // LiveEscortInteractionPoint only moves wangso_w1 to the companion's feet (same condition as there).
        bool TryInteractionGeometry(string id,out Vector3 position,out float radius)
        {
            var p=RawInteractionPoint(id);
            if(p==null){position=default;radius=0;return false;}
            position=p.Id=="wangso_w1"&&DemoEscortCompanion!=null&&DemoCampaignActive?DemoEscortCompanion.position:p.Position;
            radius=p.Radius;return true;
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
        PrologueContentSO.Point FindInteractionPoint(string id)=>LiveEscortInteractionPoint(DemoInteractionPoint(RawInteractionPoint(id)));

        WorldMacroProgress CreateFreshProgress()
        {
            // A corrupt save remains an unsaved fallback at the original mine, never a new office journey.
            bool office=LoadStatus=="new"&&Opening!=null&&Opening.Enabled;
            if(office&&!Opening.IsConfigured)throw new InvalidOperationException("Village opening profile needs finite spawn/commission data, conversation ID village_commission, and zero reward.");
            if(office&&!TrySafeFeet(Opening.StartFeet,out _))throw new InvalidOperationException("Village opening has no safe authored spawn; original mine start was not substituted.");
            var progress=WorldMacroOpeningProgress.CreateNew(Content,LoadStatus=="new");
            if(Content.Campaign!=null)
            {
                if(!Content.Campaign.IsValid)throw new InvalidOperationException("Invalid demo campaign profile.");
                progress.campaign.CampaignId=Content.Campaign.CampaignId;
                if(Content.Campaign.UseExplicitPrerequisites)
                    foreach(var id in Content.Campaign.InitialCompletedIds??Array.Empty<string>())
                    {
                        if(!progress.campaign.Completed.Contains(id))progress.campaign.Completed.Add(id);
                        var stage=Array.Find(Content.Campaign.Stages,s=>s.Id==id);
                        foreach(var fact in stage.GrantedFacts??Array.Empty<string>())if(!progress.campaign.Facts.Contains(fact))progress.campaign.Facts.Add(fact);
                    }
            }
            EnrollMineTutorial306(progress);   // #306 #11: only a brand-new save sees the mine tutorial cards
            return progress;
        }

        Vector3 AuthoredCheckpointFeet()=>WorldMacroCheckpointRules.TryResolve(Content,Progress,Progress?.ledger?.checkpoint,out var checkpoint)?checkpoint.Feet:Content.StartFeet;
        float CheckpointYaw()=>WorldMacroCheckpointRules.TryResolve(Content,Progress,Progress?.ledger?.checkpoint,out var checkpoint)?checkpoint.Yaw:Content.StartYaw;

        public bool TryMarkOpeningIntroductionSeen(out string error)
        {
            if(!OpeningJourneyActive){error=null;return true;}
            return TrySaveOpeningMarker(OpeningIntroductionSeenId,out error);
        }
        public bool TryRecordOpeningVehicleSummoned(out string error)
        {
            if(!OpeningJourneyActive){error=null;return true;}
            if(!OpeningCommissionReceived){error="먼저 관청 아전에게 폐광 조사 의뢰를 확인한다.";return false;}
            return TrySaveOpeningMarker(OpeningVehicleSummonedId,out error);
        }
        bool TrySaveOpeningMarker(string id,out string error)
        {
            if(!ready||Progress?.ledger?.completed==null){error="진행을 준비하는 중이다.";return false;}
            if(SaveBlocked){error=SaveError??"저장 원본을 복구한 뒤 진행한다.";return false;}
            if(Progress.ledger.completed.Contains(id)){error=null;return true;}
            Progress.ledger.completed.Add(id);
            if(SaveNow(out error))return true;
            Progress.ledger.completed.Remove(id);
            return false;
        }
    }
}
