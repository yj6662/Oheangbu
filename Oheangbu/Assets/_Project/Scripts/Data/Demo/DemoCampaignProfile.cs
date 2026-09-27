using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.Data.Demo
{
    public enum DemoEventKind { Interaction, Rest, BossDefeated, FieldUsed, CargoDelivered, GateOpened, GrowthInterrupted }

    [CreateAssetMenu(menuName="Oheangbu/Demo/Campaign")]
    public sealed class DemoCampaignProfile : ScriptableObject
    {
        [Serializable] public sealed class Act
        {
            public string Id, Title;
            [TextArea] public string Journey, RewardSummary;
            public bool Reserved;
        }
        [Serializable] public sealed class Stage
        {
            public string Id, Objective, TriggerId, Prompt;
            [TextArea] public string Dialogue;
            public DemoEventKind Event;
            public int TongboReward;
            public bool Implemented;
            public string[] RequiredDefeatedIds=Array.Empty<string>();
            public string ActId, DestinationId, DestinationLabel;
            public Vector3 Destination;
            [Min(1)] public float DestinationRadius=40;
            [TextArea] public string TravelHint;
            public bool Optional;
            public string[] PrerequisiteIds=Array.Empty<string>();
            public string[] RequiredFacts=Array.Empty<string>();
            public string[] GrantedFacts=Array.Empty<string>();
        }
        [Serializable] public sealed class Testimony
        {
            public string TriggerId;
            public string[] RequiredFacts=Array.Empty<string>();
            [TextArea] public string Text;
        }
        public string CampaignId="hwanggyeong-demo-v1";
        public Stage[] Stages=Array.Empty<Stage>();
        public Act[] Acts=Array.Empty<Act>();
        public bool EnvironmentalGuidance;
        public bool UseExplicitPrerequisites;
        public string[] InitialCompletedIds=Array.Empty<string>();
        public Testimony[] Testimonies=Array.Empty<Testimony>();
        public string DialogueFor(string triggerId,DemoCampaignState state,string fallback)
        {
            foreach(var testimony in Testimonies??Array.Empty<Testimony>())
            {
                if(testimony.TriggerId!=triggerId)continue;
                bool eligible=true;
                foreach(var fact in testimony.RequiredFacts??Array.Empty<string>())
                    if(state?.Facts==null||!state.Facts.Contains(fact)){eligible=false;break;}
                if(eligible)return testimony.Text;
            }
            return fallback;
        }
        public string WorkInProgressText="금표 주막에 도착했다. 다음 여정은 제작 중이다.";
        public bool IsValid
        {
            get
            {
                if(string.IsNullOrWhiteSpace(CampaignId)||Stages==null||Stages.Length==0)return false;
                var ids=new HashSet<string>();
                foreach(var s in Stages)
                    if(s==null||string.IsNullOrWhiteSpace(s.Id)||!ids.Add(s.Id)||string.IsNullOrWhiteSpace(s.TriggerId)||
                       string.IsNullOrWhiteSpace(s.Objective)||s.TongboReward<0||!Enum.IsDefined(typeof(DemoEventKind),s.Event))return false;
                var actIds=new HashSet<string>();
                foreach(var act in Acts??Array.Empty<Act>())
                    if(act==null||string.IsNullOrWhiteSpace(act.Id)||!actIds.Add(act.Id)||string.IsNullOrWhiteSpace(act.Title))return false;
                var earlier=new HashSet<string>();
                foreach(var s in Stages)
                {
                    if(!string.IsNullOrEmpty(s.ActId)&&!actIds.Contains(s.ActId))return false;
                    foreach(var id in s.PrerequisiteIds??Array.Empty<string>())if(!(UseExplicitPrerequisites?ids:earlier).Contains(id))return false;
                    foreach(var fact in s.RequiredFacts??Array.Empty<string>())if(string.IsNullOrWhiteSpace(fact))return false;
                    foreach(var fact in s.GrantedFacts??Array.Empty<string>())if(string.IsNullOrWhiteSpace(fact))return false;
                    earlier.Add(s.Id);
                }
                foreach(var id in InitialCompletedIds??Array.Empty<string>())if(!ids.Contains(id))return false;
                if(UseExplicitPrerequisites)
                {
                    var visiting=new HashSet<string>();var visited=new HashSet<string>();
                    bool Visit(string id)
                    {
                        if(visited.Contains(id))return true;
                        if(!visiting.Add(id))return false;
                        var stage=Array.Find(Stages,s=>s.Id==id);
                        foreach(var dependency in stage.PrerequisiteIds??Array.Empty<string>())if(!Visit(dependency))return false;
                        visiting.Remove(id);visited.Add(id);return true;
                    }
                    foreach(var stage in Stages)if(!Visit(stage.Id))return false;
                }
                return true;
            }
        }
    }

    [Serializable] public sealed class DemoCampaignState
    {
        public string CampaignId="";
        public List<string> Completed=new List<string>();
        public List<string> EncounterEvidence=new List<string>();
        public List<string> Facts=new List<string>();
        public List<string> DiscoveredShortcuts=new List<string>();
        public List<string> SeenActs=new List<string>();
        static bool ListValid(List<string> values)=>values==null||values.TrueForAll(x=>!string.IsNullOrWhiteSpace(x))&&new HashSet<string>(values).Count==values.Count;
        public bool IsValid()=>CampaignId!=null&&Completed!=null&&ListValid(Completed)&&ListValid(EncounterEvidence)&&ListValid(Facts)&&ListValid(DiscoveredShortcuts)&&ListValid(SeenActs);
        public void Normalize(){EncounterEvidence??=new List<string>();Facts??=new List<string>();DiscoveredShortcuts??=new List<string>();SeenActs??=new List<string>();}
        public DemoCampaignState Copy()=>new DemoCampaignState{CampaignId=CampaignId,Completed=new List<string>(Completed),
            EncounterEvidence=new List<string>(EncounterEvidence??new List<string>()),
            Facts=new List<string>(Facts??new List<string>()),
            DiscoveredShortcuts=new List<string>(DiscoveredShortcuts??new List<string>()),SeenActs=new List<string>(SeenActs??new List<string>())};
    }

    /// <summary>Pure event progression. It has no position/POI visitation or global state.</summary>
    public static class DemoCampaignProgression
    {
        public static DemoCampaignProfile.Stage Current(DemoCampaignProfile profile,DemoCampaignState state)
            =>CurrentRequired(profile,state);
        public static DemoCampaignProfile.Stage CurrentRequired(DemoCampaignProfile profile,DemoCampaignState state)
        {
            if(profile==null||profile.Stages==null||state?.Completed==null||state.CampaignId!=profile.CampaignId)return null;
            foreach(var stage in profile.Stages)if(!stage.Optional&&!state.Completed.Contains(stage.Id))return stage;
            return null;
        }
        public static bool PrerequisitesMet(DemoCampaignProfile.Stage stage,DemoCampaignState state)
        {
            if(stage==null||state?.Completed==null)return false;
            foreach(var id in stage.PrerequisiteIds??Array.Empty<string>())if(!state.Completed.Contains(id))return false;
            foreach(var fact in stage.RequiredFacts??Array.Empty<string>())if(state.Facts==null||!state.Facts.Contains(fact))return false;
            return true;
        }
        public static bool CanComplete(DemoCampaignProfile profile,DemoCampaignState state,string stageId,ICollection<string> defeated=null)
        {
            if(profile?.Stages==null||state?.Completed==null||profile.CampaignId!=state.CampaignId)return false;
            var stage=Array.Find(profile.Stages,s=>s.Id==stageId);
            if(stage==null||!stage.Implemented||state.Completed.Contains(stageId)||!PrerequisitesMet(stage,state))return false;
            foreach(var id in stage.RequiredDefeatedIds??Array.Empty<string>())
                if((defeated==null||!defeated.Contains(id))&&(state.EncounterEvidence==null||!state.EncounterEvidence.Contains(id)))return false;
            return true;
        }
        public static IEnumerable<DemoCampaignProfile.Stage> AvailableStages(DemoCampaignProfile profile,DemoCampaignState state,ICollection<string> defeated=null)
        {
            if(profile?.Stages==null)yield break;
            foreach(var stage in profile.Stages)if(CanComplete(profile,state,stage.Id,defeated))yield return stage;
        }
        // A live transaction selects one eligible stage. Past clicks are never replayed as reports.
        public static DemoCampaignProfile.Stage ForEvent(DemoCampaignProfile profile,DemoCampaignState state,DemoEventKind kind,string triggerId,ICollection<string> defeated=null)
        {
            if(profile==null)return null;
            if(!profile.UseExplicitPrerequisites)
            {
                var current=CurrentRequired(profile,state);
                return current!=null&&current.Event==kind&&current.TriggerId==triggerId?current:null;
            }
            DemoCampaignProfile.Stage result=null;
            foreach(var stage in AvailableStages(profile,state,defeated))
                if(stage.Event==kind&&stage.TriggerId==triggerId){if(result!=null)return null;result=stage;}
            return result;
        }
        static DemoCampaignState Complete(DemoCampaignState state,DemoCampaignProfile.Stage stage)
        {
            var next=state.Copy();next.Completed.Add(stage.Id);
            foreach(var fact in stage.GrantedFacts??Array.Empty<string>())if(!next.Facts.Contains(fact))next.Facts.Add(fact);
            return next;
        }
        public static bool RequiredPrefixComplete(DemoCampaignProfile profile,DemoCampaignState state,string stageId)
        {
            if(profile?.Stages==null||state?.Completed==null||profile.CampaignId!=state.CampaignId)return false;
            if(profile.UseExplicitPrerequisites)return CanComplete(profile,state,stageId);
            foreach(var stage in profile.Stages)
            {
                if(stage.Id==stageId)return stage.Implemented&&PrerequisitesMet(stage,state);
                if(!stage.Optional&&!state.Completed.Contains(stage.Id))return false;
            }
            return false;
        }
        public static DemoCampaignProfile.Act CurrentAct(DemoCampaignProfile profile,DemoCampaignState state)
        {
            var stage=CurrentRequired(profile,state);
            string id=stage?.ActId;
            if(stage==null&&profile?.Stages!=null&&state?.Completed!=null)
                for(int i=profile.Stages.Length-1;i>=0;i--)if(!profile.Stages[i].Optional&&state.Completed.Contains(profile.Stages[i].Id)){id=profile.Stages[i].ActId;break;}
            return Array.Find(profile?.Acts??Array.Empty<DemoCampaignProfile.Act>(),a=>a!=null&&a.Id==id&&!a.Reserved);
        }
        public static bool TryAdvance(DemoCampaignProfile profile,DemoCampaignState state,DemoEventKind kind,string triggerId,
            out DemoCampaignState next,out int reward,ICollection<string> defeated=null)
        {
            next=null;reward=0;
            var stage=ForEvent(profile,state,kind,triggerId,defeated);
            if(stage==null||!stage.Implemented||stage.Event!=kind||stage.TriggerId!=triggerId||!PrerequisitesMet(stage,state))return false;
            foreach(string id in stage.RequiredDefeatedIds??Array.Empty<string>())
                if((defeated==null||!defeated.Contains(id))&&(state.EncounterEvidence==null||!state.EncounterEvidence.Contains(id)))return false;
            next=Complete(state,stage);reward=stage.TongboReward;return true;
        }
        public static bool TryCompleteOptional(DemoCampaignProfile profile,DemoCampaignState state,DemoEventKind kind,string triggerId,
            out DemoCampaignState next,out int reward)
        {
            next=null;reward=0;
            if(profile?.Stages==null||state?.Completed==null||state.CampaignId!=profile.CampaignId)return false;
            var stage=Array.Find(profile.Stages,s=>s.Optional&&s.Event==kind&&s.TriggerId==triggerId);
            if(stage==null||!stage.Implemented||state.Completed.Contains(stage.Id)||!PrerequisitesMet(stage,state))return false;
            foreach(var id in stage.RequiredDefeatedIds??Array.Empty<string>())if(state.EncounterEvidence==null||!state.EncounterEvidence.Contains(id))return false;
            next=Complete(state,stage);reward=stage.TongboReward;return true;
        }
    }
}
