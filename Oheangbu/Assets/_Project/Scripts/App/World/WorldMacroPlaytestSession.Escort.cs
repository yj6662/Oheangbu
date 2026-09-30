using System;
using System.IO;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    public sealed partial class WorldMacroPlaytestSession : IDemoEscortSessionOwner
    {
        public Transform DemoEscortCompanion, DemoEscortCargo, DemoEscortPassengerSocket, DemoEscortCargoSocket;
        public WorldMacroPalanquinSeat DemoEscortSeat;
        public WorldMacroPalanquinSummon DemoEscortSummon;
        public string DemoEscortStartCheckpointId = "escort_start";
        public event Action<DemoEscortState, DemoEscortReceipt> DemoEscortChanged;
        public event Action<DemoEscortCommand, DemoEscortStatus> DemoEscortAttempted;
        public DemoEscortState EscortSnapshot => Progress?.escort?.Copy();
        DemoEscortService demoEscortService;
        WorldMacroPalanquinSeat subscribedEscortSeat;
        string escortInteractionId, escortBoardEvent, escortExitEvent, escortRecallEvent, escortDeathEvent, escortReadSnapshot;
        DemoEscortEvidence escortCapturedProof;
        bool escortWasRecalled, escortDeathPending;
        float escortNextAttempt;
        PrologueContentSO.Point escortPointSource, escortPointView;

        // The authored contract point stays immutable; a travelling NPC remains
        // available at his real feet for later conversations and rejoining.
        PrologueContentSO.Point LiveEscortInteractionPoint(PrologueContentSO.Point point)
        {
            if(point==null||point.Id!="wangso_w1"||DemoEscortCompanion==null||!DemoCampaignActive)return point;
            if(!ReferenceEquals(point,escortPointSource))
            {
                escortPointSource=point;
                escortPointView=new PrologueContentSO.Point{Id=point.Id,Kind=point.Kind,Radius=point.Radius,
                    Currency=point.Currency,Prompt=point.Prompt,Text=point.Text,RequiredCompleted=point.RequiredCompleted,
                    RequiredDefeated=point.RequiredDefeated,LockedText=point.LockedText,Speaker=point.Speaker,Lines=point.Lines,Services=point.Services};
            }
            escortPointView.Position=DemoEscortCompanion.position;
            return escortPointView;
        }

        public bool ConfigureDemoEscort(Transform companion, Transform cargo, WorldMacroPalanquinSeat seat,
            Transform passengerSocket, Transform cargoSocket, WorldMacroPalanquinSummon summon = null, string startCheckpointId = "escort_start")
        {
            UnbindDemoEscort();
            if (companion == null || cargo == null || seat == null || seat.Vehicle == null || passengerSocket == null || cargoSocket == null ||
                companion.gameObject.scene != gameObject.scene || cargo.gameObject.scene != gameObject.scene || seat.gameObject.scene != gameObject.scene ||
                seat.CombatWalker != Walker || !passengerSocket.IsChildOf(seat.Vehicle.transform) || !cargoSocket.IsChildOf(seat.Vehicle.transform) ||
                summon != null && (summon.Seat != seat || summon.Vehicle != seat.Vehicle) || string.IsNullOrWhiteSpace(startCheckpointId)) return false;
            DemoEscortCompanion = companion; DemoEscortCargo = cargo; DemoEscortSeat = seat; DemoEscortPassengerSocket = passengerSocket;
            DemoEscortCargoSocket = cargoSocket; DemoEscortSummon = summon; DemoEscortStartCheckpointId = startCheckpointId;
            if (Application.isPlaying) BindDemoEscort(); return true;
        }
        void BindDemoEscort()
        {
            demoEscortService = new DemoEscortService(this);
            if (subscribedEscortSeat != DemoEscortSeat)
            {
                if (subscribedEscortSeat != null) { subscribedEscortSeat.Boarded -= EscortBoarded; subscribedEscortSeat.Exited -= EscortExited; }
                subscribedEscortSeat = DemoEscortSeat;
                if (subscribedEscortSeat != null) { subscribedEscortSeat.Boarded += EscortBoarded; subscribedEscortSeat.Exited += EscortExited; }
            }
            escortWasRecalled = DemoEscortSummon != null && DemoEscortSummon.IsRecalled;
            // Saved passenger ownership cannot keep the player seated across application reload.
            if (Progress?.escort?.CompanionMode == DemoEscortCompanionMode.Riding && DemoEscortSeat != null && !DemoEscortSeat.Occupied)
                escortExitEvent = Guid.NewGuid().ToString("N");
        }
        void UnbindDemoEscort()
        {
            if (subscribedEscortSeat != null) { subscribedEscortSeat.Boarded -= EscortBoarded; subscribedEscortSeat.Exited -= EscortExited; }
            subscribedEscortSeat = null; demoEscortService = null; escortCapturedProof = null;
            escortBoardEvent = escortExitEvent = escortRecallEvent = null;
        }
        void EscortBoarded()
        {
            var state=Progress?.escort;
            var current=DemoCampaignActive?DemoCampaignProgression.ForEvent(Content.Campaign,Progress.campaign,DemoEventKind.Interaction,"escort_start",Progress.defeated):null;
            bool journey=state!=null&&state.Stage>=DemoEscortStage.Escorting&&state.Stage<DemoEscortStage.Delivered;
            bool departure=state?.Stage==DemoEscortStage.Contracted&&current!=null&&current.Implemented&&current.TriggerId=="escort_start";
            escortBoardEvent=journey||departure?Guid.NewGuid().ToString("N"):null;escortNextAttempt=0;
        }
        void EscortExited() { escortExitEvent = Guid.NewGuid().ToString("N"); escortNextAttempt = 0; }

        static bool EscortInteraction(string id, out DemoEscortCommand command)
        {
            command = DemoEscortCommand.AcceptContract;
            switch (id)
            {
                case "wangso_w1": return true;
                case "escort_start": command = DemoEscortCommand.StartEscort; return true;
                case "checkpoint_1": command = DemoEscortCommand.FirstInspection; return true;
                case "checkpoint_2": command = DemoEscortCommand.SecondInspection; return true;
                case "cargo_delivery": command = DemoEscortCommand.Deliver; return true;
                default: return false;
            }
        }
        bool TryHandleDemoEscortInteraction(PrologueContentSO.Point point, out bool success)
        {
            success = false;
            if (point == null || !EscortInteraction(point.Id, out var command)) return false;
            // This owner consumes blocked requests too. They must never reach generic campaign/visited progression.
            if (command == DemoEscortCommand.StartEscort)
            { if(!string.IsNullOrEmpty(EscortVoice306.StartBoardNotice))Show(EscortVoice306.StartBoardNotice); return true; }
            // #306: the companion's lines are data (Content.EscortVoice) and go to the dialogue surface (Present when none is bound)
            string voice=EscortVoice306.Speaker;
            if(EquipmentEnabled&&point.Id=="wangso_w1"&&!Progress.campaign.Facts.Contains("met:jeongdam")){
                Speak306(point,voice,point.Text,null,voice);success=true;return true;
            }
            if(EquipmentEnabled&&point.Id=="wangso_w1"&&Progress.escort.Stage>=DemoEscortStage.Contracted)
            {
                Speak306(point,voice,EscortStageLine306(),null,voice);
                success=true;return true;
            }
            escortInteractionId = point.Id;
            try
            {
                var rewardingStage=DemoCampaignProgression.ForEvent(Content.Campaign,Progress.campaign,command==DemoEscortCommand.Deliver?DemoEventKind.CargoDelivered:DemoEventKind.Interaction,point.Id,Progress.defeated);
                int paid=rewardingStage!=null&&rewardingStage.TriggerId==point.Id?rewardingStage.TongboReward:0;
                var status = ExecuteDemoEscort(command, out _, out string error);
                success = status == DemoEscortStatus.Saved;
                if (!success&&EquipmentEnabled&&point.Id=="wangso_w1"&&status==DemoEscortStatus.Duplicate){Speak306(point,voice,EscortStageLine306(),null,voice);success=true;return true;}
                if (!success) { Show(error ?? (status == DemoEscortStatus.Duplicate ? "이미 확인한 호송 절차다." : "왕소·화물과 현재 의뢰 위치를 확인하자.")); return true; }
                Speak306(point, point.Prompt, point.Text, paid>0?"조선통보 +"+paid:null); InteractionResolved?.Invoke(point.Kind, point.Position); return true;
            }
            finally { escortInteractionId = null; }
        }
        DemoEscortStatus ExecuteDemoEscort(DemoEscortCommand command, out DemoEscortReceipt receipt, out string error)
        {
            if (demoEscortService == null) demoEscortService = new DemoEscortService(this);
            var status = demoEscortService.Execute(command, out receipt, out error);
            try { DemoEscortAttempted?.Invoke(command, status); }
            catch (Exception observerError) { Debug.LogException(observerError); }
            return status;
        }

        void TickDemoEscort()
        {
            if (!ready || !DemoCampaignActive || Progress.escort == null) return;
            if (escortDeathPending)
            { if (Time.unscaledTime >= escortNextAttempt) TryHandleDemoEscortDeath(); return; }
            if (GameplayInputBlocked || vitals == null || vitals.Hp01 <= 0 || Time.unscaledTime < escortNextAttempt) return;
            bool recalled = DemoEscortSummon != null && DemoEscortSummon.IsRecalled;
            if (recalled && !escortWasRecalled) escortRecallEvent = Guid.NewGuid().ToString("N");
            escortWasRecalled = recalled;
            var state = Progress.escort;
            if (state.Stage < DemoEscortStage.Contracted || state.Stage >= DemoEscortStage.Delivered) return;
            DemoEscortCommand command;
            if (escortExitEvent != null && state.CompanionMode == DemoEscortCompanionMode.Riding) command = DemoEscortCommand.Disembark;
            else if (escortRecallEvent != null && state.Stage >= DemoEscortStage.Escorting) command = DemoEscortCommand.VehicleRecalled;
            else if (escortBoardEvent != null && DemoEscortSeat != null && DemoEscortSeat.Occupied)
                command = state.Stage == DemoEscortStage.Contracted ? DemoEscortCommand.StartEscort : DemoEscortCommand.Board;
            else return;
            escortNextAttempt = Time.unscaledTime + .5f;
            var status = ExecuteDemoEscort(command, out _, out string error);
            if (status == DemoEscortStatus.Saved || status == DemoEscortStatus.Duplicate)
            { if (command == DemoEscortCommand.Disembark) escortExitEvent = null; else if (command == DemoEscortCommand.VehicleRecalled) escortRecallEvent = null; else escortBoardEvent = null; }
            else if (status == DemoEscortStatus.SaveFailed) Show(error);
        }

        DemoEscortState IDemoEscortSessionOwner.ReadEscort()
        { escortReadSnapshot = Progress != null ? JsonUtility.ToJson(Progress) : null; return Progress?.escort?.Copy(); }
        bool IDemoEscortSessionOwner.TryCaptureEscortEvidence(DemoEscortCommand command, DemoEscortState expected, out DemoEscortEvidence proof)
        {
            proof = null; escortCapturedProof = null;
            bool death = command == DemoEscortCommand.RecoverAfterDeath;
            if (!ready || !DemoCampaignActive || SaveBlocked || HasPendingDefeats || expected == null || Walker == null || Walker.Body == null ||
                !death && (GameplayInputBlocked || vitals == null || vitals.Hp01 <= 0) ||
                DemoEscortCompanion == null || DemoEscortCompanion.gameObject.scene != gameObject.scene) return false;
            bool seated = DemoEscortSeat != null && DemoEscortSeat.Occupied;
            Vector3 player = seated && DemoEscortSeat.SeatSocket != null ? DemoEscortSeat.SeatSocket.position : Walker.Body.transform.position;
            var p = new DemoEscortEvidence { Command = command, ExpectedRevision = expected.Revision, SessionVerified = true,
                EventId = Guid.NewGuid().ToString("N"), PlayerFeet = player, CompanionFeet = DemoEscortCompanion.position,
                CompanionPresent = DemoEscortCompanion.gameObject.activeInHierarchy, PlayerInVehicle = seated,
                CargoPresent = DemoEscortCargo != null && DemoEscortCargo.gameObject.activeInHierarchy && DemoEscortCargo.gameObject.scene == gameObject.scene &&
                    Vector3.Distance(DemoEscortCargo.position, player) <= DemoEscortRules.MaximumCompanionDistance };
            if (DemoEscortSeat != null && DemoEscortSeat.Vehicle != null)
            {
                p.VehiclePresent = DemoEscortSeat.isActiveAndEnabled && DemoEscortSeat.CombatWalker == Walker && DemoEscortSeat.Vehicle.isActiveAndEnabled &&
                    DemoEscortSeat.Vehicle.IsConfigured && DemoEscortSeat.gameObject.scene == gameObject.scene;
                p.VehicleSpeed = DemoEscortSeat.Vehicle.Speed;
                p.CompanionLoaded = DemoEscortPassengerSocket != null && DemoEscortPassengerSocket.IsChildOf(DemoEscortSeat.Vehicle.transform) && DemoEscortCompanion.IsChildOf(DemoEscortPassengerSocket);
                p.CargoLoaded = DemoEscortCargo != null && DemoEscortCargoSocket != null && DemoEscortCargoSocket.IsChildOf(DemoEscortSeat.Vehicle.transform) && DemoEscortCargo.IsChildOf(DemoEscortCargoSocket);
            }
            string interaction = command == DemoEscortCommand.AcceptContract ? "wangso_w1" : command == DemoEscortCommand.StartEscort ? "escort_start" :
                command == DemoEscortCommand.FirstInspection ? "checkpoint_1" : command == DemoEscortCommand.SecondInspection ? "checkpoint_2" : command == DemoEscortCommand.Deliver ? "cargo_delivery" : null;
            if (interaction != null)
            {
                var current = DemoCampaignProgression.ForEvent(Content.Campaign, Progress.campaign,command==DemoEscortCommand.Deliver?DemoEventKind.CargoDelivered:DemoEventKind.Interaction,interaction,Progress.defeated);
                var point = FindInteractionPoint(interaction);
                if (current == null || !current.Implemented || current.TriggerId != interaction || point == null ||
                    current.Event != (command == DemoEscortCommand.Deliver ? DemoEventKind.CargoDelivered : DemoEventKind.Interaction)) return false;
                if (command == DemoEscortCommand.StartEscort)
                {
                    if (escortBoardEvent == null || !seated || !CanReachEscortPoint(point, player)) return false;
                    p.EventId = escortBoardEvent;
                    if (!EscortCheckpoint(DemoEscortStartCheckpointId, p)) return false;
                }
                else if (escortInteractionId != interaction || Walker.Motor == null || Walker.Drawing == null || !CanInteract(interaction)) return false;
                p.InteractionId = interaction; p.PointFeet = point.Position;
            }
            switch (command)
            {
                case DemoEscortCommand.Board: if (escortBoardEvent == null) return false; p.EventId = escortBoardEvent; break;
                case DemoEscortCommand.Disembark:
                    if (escortExitEvent == null || seated || !EscortSafeCompanionExit(expected, out p.SafeExitFeet)) return false;
                    p.EventId = escortExitEvent; p.SafeExitVerified = true; break;
                case DemoEscortCommand.VehicleRecalled:
                    if (escortRecallEvent == null || seated || DemoEscortSummon == null || !DemoEscortSummon.IsRecalled || !EscortSafeCompanionExit(expected, out p.SafeExitFeet)) return false;
                    p.EventId = escortRecallEvent; p.VehicleRecallVerified = p.SafeExitVerified = true; break;
                case DemoEscortCommand.RecoverAfterDeath:
                    if (!escortDeathPending || escortDeathEvent == null || vitals == null || vitals.Hp01 > 0 || !EscortCheckpoint(expected.CheckpointId, p)) return false;
                    p.EventId = escortDeathEvent; p.DeathRecoveryVerified = true; break;
                case DemoEscortCommand.Rejoin: case DemoEscortCommand.SaveCheckpoint: return false;
            }
            escortCapturedProof = p; proof = p; return true;
        }
        bool EscortCheckpoint(string id, DemoEscortEvidence p)
        {
            if (!WorldMacroCheckpointRules.TryResolve(Content, Progress, id, out var checkpoint) || !TrySafeFeet(checkpoint.Feet, out var safe)) return false;
            p.CheckpointId = checkpoint.Id; p.CheckpointFeet = safe; p.CheckpointYaw = checkpoint.Yaw; p.CheckpointVerified = true; return true;
        }
        bool EscortSafeCompanionExit(DemoEscortState state, out Vector3 feet)
        {
            Vector3 player = Walker.Body.transform.position;
            if (Vector3.Distance(DemoEscortCompanion.position, player) <= DemoEscortRules.MaximumCompanionDistance &&
                TrySafeFeet(DemoEscortCompanion.position, out feet)) return true;
            if (TrySafeFeet(player + Walker.Body.transform.right * .8f, out feet)) return true;
            // Ordinary exit/recall never grants remote checkpoint travel. Only verified death recovery may do that.
            feet = default; return false;
        }
        bool CanReachEscortPoint(PrologueContentSO.Point point, Vector3 player)
        {
            if (point.Radius <= 0 || Vector3.Distance(player, point.Position) > Mathf.Min(point.Radius, DemoEscortRules.MaximumInteractionDistance)) return false;
            Vector3 origin = player + Vector3.up * .5f, delta = point.Position + Vector3.up - origin;
            foreach (var hit in Physics.RaycastAll(origin, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                var t = hit.transform;
                if (t.IsChildOf(Walker.Body.transform) || DemoEscortSeat != null && t.IsChildOf(DemoEscortSeat.Vehicle.transform) ||
                    t.IsChildOf(DemoEscortCompanion) || DemoEscortCargo != null && t.IsChildOf(DemoEscortCargo)) continue;
                var ownedPoint = t.GetComponentInParent<WorldMacroContentPoint>(); if (ownedPoint != null && ownedPoint.Id == point.Id) continue;
                return false;
            }
            return true;
        }

        DemoEscortStatus IDemoEscortSessionOwner.TryCommitEscort(DemoEscortState expected, DemoEscortState candidate, DemoEscortReceipt receipt, out string error)
        {
            error = null; var proof = escortCapturedProof; escortCapturedProof = null;
            if (Progress == null || escortReadSnapshot != JsonUtility.ToJson(Progress) || JsonUtility.ToJson(Progress.escort) != JsonUtility.ToJson(expected)) return DemoEscortStatus.Conflict;
            if (proof == null || receipt == null || DemoEscortRules.TryPrepare(expected, receipt.Command, proof, out var rechecked, out var checkedReceipt) != DemoEscortStatus.Prepared ||
                JsonUtility.ToJson(rechecked) != JsonUtility.ToJson(candidate) || receipt.CampaignTriggerId != checkedReceipt.CampaignTriggerId ||
                receipt.RewardKey != checkedReceipt.RewardKey || receipt.RestoreAtCheckpoint != checkedReceipt.RestoreAtCheckpoint ||
                receipt.RestoreFeet != checkedReceipt.RestoreFeet || receipt.RestoreYaw != checkedReceipt.RestoreYaw) return DemoEscortStatus.InvalidEvidence;
            if (!TryPrepareDemoEscortProgress(Progress, Content, candidate, receipt, lastSafe, out var proposal, out error)) return DemoEscortStatus.InvalidState;
            if (receipt.RestoreAtCheckpoint)
            {
                if (!TryPersistDemoEscortCandidate(proposal, store.Save, out var accepted, out error)) { SaveError = error; return DemoEscortStatus.SaveFailed; }
                Progress = accepted; lastSuccessfulSnapshot = JsonUtility.ToJson(accepted); SaveError = null;
            }
            else if (!TryCommitInteraction(proposal, out error)) return DemoEscortStatus.SaveFailed;
            try { DemoEscortChanged?.Invoke(Progress.escort.Copy(), receipt); }
            catch (Exception observerError) { Debug.LogException(observerError); }
            return DemoEscortStatus.Saved;
        }

        static bool TryPrepareDemoEscortProgress(WorldMacroProgress source, Oheangbu.Data.World.WorldMacroPlaytestSO content,
            DemoEscortState escort, DemoEscortReceipt receipt, Vector3 deathDropFeet, out WorldMacroProgress proposal, out string error)
        {
            proposal = null; error = null;
            if (!WorldMacroProgress.Valid(source) || content?.Campaign == null || !content.Campaign.IsValid || escort == null || !escort.IsValid() ||
                receipt == null || escort.Revision != source.escort.Revision + 1) return false;
            var candidate = WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(source)));
            candidate.escort = escort.Copy();
            if (!string.IsNullOrEmpty(receipt.CampaignTriggerId))
            {
                DemoEventKind kind = receipt.Command == DemoEscortCommand.Deliver ? DemoEventKind.CargoDelivered : DemoEventKind.Interaction;
                if (!DemoCampaignProgression.TryAdvance(content.Campaign, source.campaign, kind, receipt.CampaignTriggerId, out var campaign, out int reward, source.defeated)) return false;
                var point = content.Points == null ? null : Array.Find(content.Points, p => p != null && p.Id == receipt.CampaignTriggerId);
                if (point == null) return false;
                bool first = !candidate.ledger.completed.Contains(point.Id);
                try { candidate.ledger.currency = checked(candidate.ledger.currency + reward + (first ? Math.Max(0, point.Currency) : 0)); }
                catch (OverflowException) { error = "통보 보유량을 확인한다."; return false; }
                candidate.campaign = campaign; if (first) candidate.ledger.completed.Add(point.Id);
            }
            if (receipt.RestoreAtCheckpoint)
            {
                if (!DemoEscortState.Finite(deathDropFeet)) return false;
                PrologueProgressStore.Drop(candidate.ledger, deathDropFeet);
                candidate.ledger.checkpoint = escort.CheckpointId; candidate.ledger.checkpointPosition = receipt.RestoreFeet;
                candidate.ledger.position = receipt.RestoreFeet; candidate.ledger.yaw = receipt.RestoreYaw; candidate.ledger.hasPosition = true;
                candidate.ledger.hp = candidate.ledger.ink = 1;
                foreach (var encounter in content.Encounters ?? Array.Empty<Oheangbu.Data.World.WorldMacroPlaytestSO.Encounter>())
                    if (encounter != null && encounter.RespawnOnRest) candidate.defeated.Remove(encounter.Id);
            }
            proposal = candidate; return WorldMacroProgress.Valid(candidate);
        }
        static bool TryPersistDemoEscortCandidate(WorldMacroProgress proposal, Action<WorldMacroProgress> persist,
            out WorldMacroProgress accepted, out string error)
        {
            accepted = null; error = null;
            if (!WorldMacroProgress.Valid(proposal) || persist == null) return false;
            try { persist(proposal); accepted = proposal; return true; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            { error = "호송 저장 실패: " + e.Message; return false; }
        }

        bool TryHandleDemoEscortDeath()
        {
            var escort = Progress?.escort;
            if (!ready || !DemoCampaignActive || escort == null || escort.Stage < DemoEscortStage.Escorting || escort.Stage >= DemoEscortStage.Delivered) return false;
            escortDeathPending = true; if (escortDeathEvent == null) escortDeathEvent = Guid.NewGuid().ToString("N");
            escortNextAttempt = Time.unscaledTime + .5f;
            var status = ExecuteDemoEscort(DemoEscortCommand.RecoverAfterDeath, out var receipt, out var error);
            if (status != DemoEscortStatus.Saved) { Show(error ?? "최근 호송 쉼터의 안전한 복구 위치와 저장을 확인하는 중이다."); return true; }
            escortDeathPending = false; escortDeathEvent = null; respawning = true;
            if (DemoEscortSeat != null && DemoEscortSeat.Occupied)
            { DemoEscortSeat.ForceExitForRecovery(receipt.RestoreFeet,receipt.RestoreYaw); }
            if(Traversal!=null)DemoEscortSummon?.RecallAfterRecovery();
            ResetCombat(); Teleport(receipt.RestoreFeet, receipt.RestoreYaw); vitals.Restore(); ink.Restore(); RefreshDrop(); respawning = false;
            escortBoardEvent = escortExitEvent = escortRecallEvent = null;
            Show("최근 호송 쉼터에서 왕소와 화물을 다시 만났다. 검문 기록은 보존되었다."); return true;
        }

        void IncludeDemoEscortCheckpointInRest(WorldMacroProgress proposal, string pointId, Vector3 safeFeet)
        {
            if (proposal?.escort == null || DemoEscortCompanion == null || Walker == null || Walker.Body == null || DemoEscortSeat != null && DemoEscortSeat.Occupied) return;
            var proof = new DemoEscortEvidence { Command = DemoEscortCommand.SaveCheckpoint, ExpectedRevision = proposal.escort.Revision,
                EventId = Guid.NewGuid().ToString("N"), SessionVerified = true, CheckpointVerified = true, CheckpointId = pointId,
                CheckpointFeet = safeFeet, CheckpointYaw = Walker.Body.transform.eulerAngles.y, PlayerFeet = Walker.Body.transform.position,
                CompanionFeet = DemoEscortCompanion.position, CompanionPresent = DemoEscortCompanion.gameObject.activeInHierarchy };
            if (DemoEscortRules.TryPrepare(proposal.escort, proof.Command, proof, out var next, out _) == DemoEscortStatus.Prepared) proposal.escort = next;
        }
    }
}
