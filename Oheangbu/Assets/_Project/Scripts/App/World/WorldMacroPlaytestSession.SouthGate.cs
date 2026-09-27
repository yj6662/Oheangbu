using System;
using System.Linq;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using Oheangbu.Data.Demo;
using UnityEngine;

namespace Oheangbu.App.World
{
    public sealed partial class WorldMacroPlaytestSession
    {
        public const string SouthGateGeneralId = "south_gate_general";
        public const string SouthGateOpenedId = "hwanggyeong_south_gate";
        public SouthGateGeneralController DemoSouthGateGeneral;
        public event Action DemoSouthGateOpened;
        public bool DemoSouthGateOpen => DemoCampaignActive && SouthGateCompletionRecorded(Progress);
        public bool DemoCampaignCompleted => DemoSouthGateOpen && DemoCampaignProgression.Current(Content.Campaign, Progress.campaign) == null;
        public bool DemoSouthGateSavePending => southGateDeathWitness != null;
        public bool DemoSouthGateEncounterAvailable => DemoCampaignActive && SouthGateStagesAvailable(Progress, Content.Campaign);
        EnemyVitals subscribedSouthGateVitals, southGateDeathWitness;
        uint southGateDeathLife;
        float nextSouthGateCommit;

        public bool ConfigureDemoSouthGate(SouthGateGeneralController controller)
        {
            var prior = DemoSouthGateGeneral; DemoSouthGateGeneral = controller;
            if (!TryResolveSouthGateActor(out _, out _)) { DemoSouthGateGeneral = prior; return false; }
            if (Application.isPlaying) BindDemoSouthGate();
            return true;
        }
        bool TryResolveSouthGateActor(out PrologueEncounter actor, out EnemyVitals life)
        {
            actor = null; life = null;
            if (DemoSouthGateGeneral == null || DemoSouthGateGeneral.gameObject.scene != gameObject.scene || Content == null) return false;
            var actors = (Actors ?? Array.Empty<PrologueEncounter>()).Where(a => a != null && a.Id == SouthGateGeneralId).ToArray();
            var specs = (Content.Encounters ?? Array.Empty<Oheangbu.Data.World.WorldMacroPlaytestSO.Encounter>()).Where(e => e != null && e.Id == SouthGateGeneralId).ToArray();
            if (actors.Length != 1 || specs.Length != 1 || specs[0].RespawnOnRest || actors[0].gameObject.scene != gameObject.scene ||
                actors[0].GetComponent<SouthGateGeneralController>() != DemoSouthGateGeneral) return false;
            actor = actors[0]; life = actor.GetComponent<EnemyVitals>(); return life != null;
        }
        void BindDemoSouthGate()
        {
            UnbindDemoSouthGate();
            if (DemoSouthGateGeneral == null)
            {
                var candidates = (Actors ?? Array.Empty<PrologueEncounter>()).Where(a => a != null && a.Id == SouthGateGeneralId &&
                    a.gameObject.scene == gameObject.scene).ToArray();
                if (candidates.Length == 1) DemoSouthGateGeneral = candidates[0].GetComponent<SouthGateGeneralController>();
            }
            if (!TryResolveSouthGateActor(out var actor, out var life)) return;
            DemoSouthGateGeneral.Configure(Walker != null && Walker.Body != null ? Walker.Body.transform : null, vitals, parry);
            var generic = actor.GetComponent<EnemyController>(); if (generic != null) { generic.AttackEnabled = false; generic.enabled = false; }
            subscribedSouthGateVitals = life; life.Died += OnRegisteredSouthGateDied;
        }
        void UnbindDemoSouthGate()
        {
            if (subscribedSouthGateVitals != null) subscribedSouthGateVitals.Died -= OnRegisteredSouthGateDied;
            subscribedSouthGateVitals = null;
            // A session pause must not erase an already witnessed, still-dead pending save.
            // Retry separately revalidates this object and its life revision after rebinding.
        }
        void OnRegisteredSouthGateDied()
        {
            if (!ready || !DemoSouthGateEncounterAvailable || !TryResolveSouthGateActor(out _, out var life) ||
                life != subscribedSouthGateVitals || life.IsAlive) return;
            southGateDeathWitness = life; southGateDeathLife = life.LifeRevision;
            TryCommitPendingDemoSouthGate(true);
        }
        // Consumes this ID even without proof. Calling EnemyDefeated with a string never supplies a death witness.
        bool TryHandleDemoSouthGateDefeat(string id)
        {
            if (id != SouthGateGeneralId) return false;
            TryCommitPendingDemoSouthGate(true); return true;
        }
        void TryCommitPendingDemoSouthGate(bool force = false)
        {
            if (!ready || !DemoCampaignActive || southGateDeathWitness == null || (!force && Time.unscaledTime < nextSouthGateCommit)) return;
            nextSouthGateCommit = Time.unscaledTime + 1;
            if (DemoSouthGateOpen || !TryResolveSouthGateActor(out _, out var life) || life != southGateDeathWitness ||
                life.IsAlive || life.LifeRevision != southGateDeathLife)
            { southGateDeathWitness = null; return; }
            if (!TryPrepareSouthGateCompletion(Progress, Content.Campaign, out var proposal, out string error))
            { if (!string.IsNullOrEmpty(error)) Show(error); return; }
            if (!TryCommitInteraction(proposal, out error)) { Show(error); return; }
            southGateDeathWitness = null;
            // Presentation reads the accepted state. Its failure cannot undo or repay a durable completion.
            try { DemoSouthGateOpened?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
            Show("남문 장수를 물리쳤다. 황경 남문이 열렸다.");
        }
        static bool SouthGateCompletionRecorded(WorldMacroProgress source) => WorldMacroProgress.Valid(source) &&
            source.campaign?.Completed != null &&
            source.defeated.Contains(SouthGateGeneralId) && source.ledger.completed.Contains(SouthGateOpenedId) &&
            source.campaign.Completed.Contains("south_gate") && source.campaign.Completed.Contains("ending");
        static bool SouthGateStagesAvailable(WorldMacroProgress source, DemoCampaignProfile profile)
        {
            if (!WorldMacroProgress.Valid(source) || profile == null || !profile.IsValid || source.escort == null ||
                source.escort.Stage != DemoEscortStage.Delivered || !source.escort.DeliveryRewardRecorded ||
                source.defeated.Contains(SouthGateGeneralId) || source.ledger.completed.Contains(SouthGateOpenedId)) return false;
            var current = DemoCampaignProgression.ForEvent(profile, source.campaign,DemoEventKind.BossDefeated,SouthGateGeneralId,source.defeated);
            if (current == null || current.Id != "south_gate" || !current.Implemented || current.Event != DemoEventKind.BossDefeated || current.TriggerId != SouthGateGeneralId) return false;
            int index = Array.IndexOf(profile.Stages, current);
            var ending = profile.UseExplicitPrerequisites?Array.Find(profile.Stages,s=>s.Id=="ending"):(index >= 0 && index + 1 < profile.Stages.Length ? profile.Stages[index + 1] : null);
            return ending != null && ending.Id == "ending" && ending.Implemented && ending.Event == DemoEventKind.GateOpened && ending.TriggerId == SouthGateOpenedId;
        }
        static bool TryPrepareSouthGateCompletion(WorldMacroProgress source, DemoCampaignProfile profile, out WorldMacroProgress proposal, out string error)
        {
            proposal = null; error = null;
            if (!SouthGateStagesAvailable(source, profile) || !DemoCampaignProgression.TryAdvance(profile, source.campaign, DemoEventKind.BossDefeated,
                SouthGateGeneralId, out var defeated, out int bossReward, source.defeated)) return false;
            var candidate = WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(source)));
            candidate.defeated.Add(SouthGateGeneralId);
            if (!DemoCampaignProgression.TryAdvance(profile, defeated, DemoEventKind.GateOpened, SouthGateOpenedId, out var completed, out int gateReward, candidate.defeated)) return false;
            try { candidate.ledger.currency = checked(candidate.ledger.currency + bossReward + gateReward); }
            catch (OverflowException) { error = "통보 보유량을 확인한다."; return false; }
            candidate.campaign = completed; candidate.ledger.completed.Add(SouthGateOpenedId);
            proposal = candidate; return WorldMacroProgress.Valid(candidate);
        }
    }
}
