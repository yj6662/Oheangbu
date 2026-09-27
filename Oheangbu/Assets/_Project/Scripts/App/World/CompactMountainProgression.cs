using System;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    public static class CompactMountainProgression
    {
        public static bool TryPrepare(WorldMacroProgress source,CompactWorldLayoutSO.MountainAction action,
            out WorldMacroProgress proposal,out string error)
        {
            proposal=null;error=null;
            if(!WorldMacroProgress.Valid(source)||action==null||string.IsNullOrWhiteSpace(action.Id)||action.Reward<0)
            {error="기록을 읽을 수 없다.";return false;}
            if(source.ledger.completed.Contains(action.Id)){error="이미 살펴본 자리다.";return false;}
            foreach(var id in action.RequiredCompleted)if(!source.ledger.completed.Contains(id))
            {error="아직 움직이지 않는 장치다.";return false;}
            foreach(var id in action.RequiredDefeated)if(!source.campaign.EncounterEvidence.Contains(id)&&!source.defeated.Contains(id))
            {error="주변의 위협이 남아 있다.";return false;}
            if(action.Reward>int.MaxValue-source.ledger.currency){error="통보를 보관할 수 없다.";return false;}
            var copy=WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(source)));
            copy.ledger.completed.Add(action.Id);copy.ledger.currency+=action.Reward;
            string fact="mountain:"+action.Id;if(!copy.campaign.Facts.Contains(fact))copy.campaign.Facts.Add(fact);
            if(!string.IsNullOrEmpty(action.RecordId))copy.ui.AddRecord(action.RecordId);
            proposal=copy;return true;
        }
    }
}
