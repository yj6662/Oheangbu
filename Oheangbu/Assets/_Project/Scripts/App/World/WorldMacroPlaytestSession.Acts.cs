using Oheangbu.Data.Demo;
using UnityEngine;
namespace Oheangbu.App.World
{
    public sealed partial class WorldMacroPlaytestSession
    {
        float nextActNotice,nextShortcutCheck;
        WorldActShortcut[] actShortcuts;
        public string AmbientObjective=>Content?.Campaign!=null&&Content.Campaign.EnvironmentalGuidance?null:OpeningObjective;
        public DemoCampaignProfile.Stage KnownDestination
        {
            get
            {
                if(!DemoCampaignActive||!Content.Campaign.EnvironmentalGuidance||Content.Campaign.UseExplicitPrerequisites)return null;
                var stage=DemoCampaignProgression.CurrentRequired(Content.Campaign,Progress.campaign);
                if(stage==null||!stage.Implemented||string.IsNullOrEmpty(stage.DestinationId)||
                    !DemoCampaignProgression.RequiredPrefixComplete(Content.Campaign,Progress.campaign,stage.Id))return null;
                return stage;
            }
        }
        public string DemoTravelAdvice=>!string.IsNullOrEmpty(KnownDestination?.TravelHint)?KnownDestination.TravelHint:DemoObjective;
        public string CurrentActTitle=>DemoCampaignActive?DemoCampaignProgression.CurrentAct(Content.Campaign,Progress.campaign)?.Title:null;
        void TickActShortcuts()
        {
            if(vitals==null||vitals.Hp01<=0||!DemoCampaignActive||!Content.Campaign.EnvironmentalGuidance||GameplayInputBlocked||Walker.Seated||!Walker.Motor.IsLocomotionGrounded||Time.time<nextShortcutCheck)return;
            nextShortcutCheck=Time.time+.4f;
            actShortcuts??=System.Array.FindAll(FindObjectsByType<WorldActShortcut>(FindObjectsSortMode.None),s=>s.gameObject.scene==gameObject.scene);
            foreach(var shortcut in actShortcuts)
            {
                if(shortcut==null||Progress.campaign.DiscoveredShortcuts.Contains(shortcut.Id)||!Progress.campaign.Completed.Contains(shortcut.RequiredStageId))continue;
                Vector3 delta=Walker.Body.transform.position-shortcut.DiscoveryPoint;
                if(Mathf.Abs(delta.y)>.35f||new Vector2(delta.x,delta.z).magnitude>shortcut.DiscoveryRadius)continue;
                var proposal=WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(Progress)));
                proposal.campaign.DiscoveredShortcuts.Add(shortcut.Id);
                if(TryCommitInteraction(proposal,out _))Show(shortcut.Label+"을 확인했다.");
                break;
            }
        }
        void TickActEntrance()
        {
            if(vitals==null||vitals.Hp01<=0||!DemoCampaignActive||Content.Campaign.UseExplicitPrerequisites||!Content.Campaign.EnvironmentalGuidance||GameplayInputBlocked||Time.unscaledTime<until||Time.unscaledTime<nextActNotice)return;
            var act=DemoCampaignProgression.CurrentAct(Content.Campaign,Progress.campaign);
            if(act==null||Progress.campaign.SeenActs.Contains(act.Id))return;
            nextActNotice=Time.unscaledTime+2;
            var proposal=WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(Progress)));
            proposal.campaign.SeenActs.Add(act.Id);
            if(TryCommitInteraction(proposal,out _))Show(act.Title);
        }
    }
}
