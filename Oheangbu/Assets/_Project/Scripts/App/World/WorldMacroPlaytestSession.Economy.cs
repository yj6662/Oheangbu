using System;
using Oheangbu.App.Demo;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    public sealed partial class WorldMacroPlaytestSession
    {
        public DemoEconomyService DemoEconomy {get;private set;}
        public bool AtDemoShop
        {
            get
            {
                if(!ready||!DemoCampaignActive||DemoEconomy==null||Walker==null||Walker.Seated||vitals==null||vitals.Hp01<=0)return false;
                return FindNearbyDemoShop()!=null;
            }
        }
        public string DemoShopDisplayName
        {
            get {var point=FindNearbyDemoShop();return point==null?"쉼터":WorldMacroCheckpointRules.Label(Content,Progress,point.Id);}
        }
        PrologueContentSO.Point FindNearbyDemoShop()
        {
            if(!DemoCampaignActive||Walker==null||Walker.Body==null)return null;
            return WorldMacroCheckpointRules.FindShop(Content,Progress,Walker.Body.transform.position);
        }
        void InitializeDemoEconomy()
        {
            if(!DemoCampaignActive||Content.Economy==null)return;
            DemoEconomy=new DemoEconomyService(Content.Economy.CreateRules(),new EconomyStore(this));
            ApplyDemoStats();
        }
        void ApplyDemoStats()
        {
            if(DemoEconomy==null)return;
            var stats=new RuntimePlayerStats(Progress.economy,Content.Economy.CreateRules(),EquipmentEnabled?Progress.equipment:null,Content.EquipmentCatalog);
            vitals.SetMaxHpMultiplier(stats.MaxHpMultiplier);ink.SetCapacityMultiplier(stats.MaxInkMultiplier);
            Walker.Wiring.PlayerDamageScale=stats.DamageMultiplier;
        }
        public bool TryBuyDemoUpgrade(DemoUpgradeTrack track,int expectedLevel,string requestId,out string message)
        {
            if(!AtDemoShop){message="정비가 가능한 쉼터 가까이에서 이용한다.";return false;}
            if(!DemoEconomy.TryPurchase(track,expectedLevel,requestId,out var receipt)){message=receipt.Error;return false;}
            ApplyDemoStats();message="정비를 마쳤다. 조선통보 -"+receipt.Charged;Show(message);return true;
        }
        sealed class EconomyStore : IDemoEconomyStore
        {
            readonly WorldMacroPlaytestSession owner;
            public EconomyStore(WorldMacroPlaytestSession owner){this.owner=owner;}
            public DemoEconomySnapshot Read()=>new DemoEconomySnapshot(owner.Progress.ledger.currency,owner.Progress.economy);
            public DemoEconomyCommitStatus TryCommit(DemoEconomySnapshot expected,DemoEconomySnapshot next,out string error)
            {
                if(!owner.ready||owner.SaveBlocked){error=owner.SaveError??"진행 저장을 준비하는 중이다.";return DemoEconomyCommitStatus.Failed;}
                if(!Read().SameAs(expected)){error="보유 통보가 변경되었다. 다시 확인한다.";return DemoEconomyCommitStatus.Conflict;}
                var old=owner.Progress.economy;int currency=owner.Progress.ledger.currency;
                owner.Progress.economy=next.State;owner.Progress.ledger.currency=next.Tongbo;
                try
                {
                    if(owner.SaveNow(out error))return DemoEconomyCommitStatus.Saved;
                }
                catch(Exception e){error="강화 저장 실패: "+e.Message;}
                owner.Progress.economy=old;owner.Progress.ledger.currency=currency;
                return DemoEconomyCommitStatus.Failed;
            }
        }
    }
}
