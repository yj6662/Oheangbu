using System;
using System.IO;
using UnityEngine;

namespace Oheangbu.App.World
{
    public sealed partial class WorldMacroPlaytestSession
    {
        public bool RenAvailable=>DemoCampaignActive&&Progress.ui.knownVirtues.Contains("仁")&&!Progress.renUsed;

        void BindDemoRen(){if(vitals!=null)vitals.BindLethalDamageGuard(TrySurviveWithRen);}
        void UnbindDemoRen(){if(vitals!=null)vitals.UnbindLethalDamageGuard(TrySurviveWithRen);}

        bool TrySurviveWithRen(float survivingHpFraction)
        {
            if(!ready||respawning||SaveBlocked||!RenAvailable||store==null||
                !float.IsFinite(survivingHpFraction)||survivingHpFraction<=0||survivingHpFraction>1)return false;
            var candidate=WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(Progress)));
            if(candidate==null)return false;
            candidate.renUsed=true;
            candidate.ledger.position=lastSafe;candidate.ledger.hasPosition=true;
            if(Walker!=null&&Walker.Body!=null)candidate.ledger.yaw=Walker.Body.transform.eulerAngles.y;
            if(ink!=null)candidate.ledger.ink=ink.Value;
            // Vitals still contain pre-hit HP. SaveNow would overwrite this committed surviving HP.
            candidate.ledger.hp=survivingHpFraction;
            string snapshot=JsonUtility.ToJson(candidate);
            try
            {
                store.Save(candidate);
                Progress=candidate;lastSuccessfulSnapshot=snapshot;SaveError=null;
                return true;
            }
            catch(Exception exception)when(exception is IOException||exception is UnauthorizedAccessException||exception is ArgumentException)
            {
                // No HP benefit or consumed/recharged state is published when persistence fails.
                SaveError="仁 사용 저장 실패: "+exception.Message;
                return false;
            }
        }
    }
}
