using System;
using System.Collections.Generic;
using Oheangbu.App.Prologue;
using Oheangbu.App.World.UI;
using UnityEngine;

namespace Oheangbu.App.World
{
    [Serializable] public sealed class WorldMacroProgress
    {
        public const int CurrentVersion=8;
        public Oheangbu.Data.Demo.DemoEconomyState economy=new Oheangbu.Data.Demo.DemoEconomyState();
        static readonly Oheangbu.Data.Demo.DemoEconomyRules economySchema=Oheangbu.Data.Demo.DemoEconomyRules.ApprovedDefaults;
        public Oheangbu.Data.Demo.EquipmentState equipment=new Oheangbu.Data.Demo.EquipmentState();
        public int version;
        public string terrainRevision;
        public PrologueProgress ledger=new PrologueProgress();
        public List<string> defeated=new List<string>();
        public UiProgress ui=new UiProgress();
        // Acquisition is ui.knownVirtues; only a durably accepted rest clears consumption.
        public bool renUsed;
        public Oheangbu.Data.Demo.DemoCampaignState campaign=new Oheangbu.Data.Demo.DemoCampaignState();
        public Oheangbu.App.Demo.DemoEscortState escort=new Oheangbu.App.Demo.DemoEscortState();
        public static bool Valid(WorldMacroProgress p)=>p!=null&&(p.version>=2&&p.version<=CurrentVersion)&&p.ledger!=null&&p.defeated!=null&&
            p.ledger.completed!=null&&p.ledger.currency>=0&&p.ledger.dropCurrency>=0&&
            float.IsFinite(p.ledger.hp)&&p.ledger.hp>=0&&p.ledger.hp<=1&&
            float.IsFinite(p.ledger.ink)&&p.ledger.ink>=0&&p.ledger.ink<=1&&
            (p.version==2||p.ui!=null&&p.ui.IsValid())&&
            (p.version<4||p.campaign!=null&&p.campaign.IsValid()&&p.economy!=null&&p.economy.IsValid(economySchema))&&
            (p.version<6||p.escort!=null&&p.escort.IsValid())&&
            (p.version<8||p.equipment!=null&&p.equipment.IsValid());

        public static WorldMacroProgress MigrateToCurrent(WorldMacroProgress progress)
        {
            if(!Valid(progress))return null;
            if(progress.economy==null)progress.economy=new Oheangbu.Data.Demo.DemoEconomyState();
            if(progress.ui==null)progress.ui=new UiProgress();
            if(progress.campaign==null)progress.campaign=new Oheangbu.Data.Demo.DemoCampaignState();
            progress.campaign.Normalize();
            if(progress.equipment==null)progress.equipment=new Oheangbu.Data.Demo.EquipmentState();
            if(progress.version<7)
            {
                foreach(var id in progress.defeated)
                    if(!progress.campaign.EncounterEvidence.Contains(id))progress.campaign.EncounterEvidence.Add(id);
                // Both legacy records are durable proof of payment; never repay the new price or a difference.
                if(progress.ledger.completed.Contains("guk_high_reward")&&!progress.campaign.Completed.Contains("guk_return"))
                    progress.campaign.Completed.Add("guk_return");
                if(progress.campaign.Completed.Contains("guk_return")&&!progress.ledger.completed.Contains("guk_high_reward"))
                    progress.ledger.completed.Add("guk_high_reward");
            }
            if(progress.version<6)
            {
                // Old saves prove only the existing contract dialogue. Never infer
                // escort travel, inspections, delivery or rewards from visited markers.
                progress.escort=new Oheangbu.App.Demo.DemoEscortState();
                if(progress.campaign.Completed!=null&&progress.campaign.Completed.Contains("cargo_contract"))
                    progress.escort.Stage=Oheangbu.App.Demo.DemoEscortStage.Contracted;
            }
            progress.ui.Normalize();
            foreach(var completed in progress.ledger.completed)
                if(WorldMacroCollectionCatalog.TryGetRecordForInteraction(completed,out var record))progress.ui.AddRecord(record.Id);
            progress.version=CurrentVersion;
            return progress;
        }

        public static WorldMacroProgress CreateNew(string terrainRevision,Vector3 startFeet,float startYaw)
        {
            return new WorldMacroProgress{version=CurrentVersion,terrainRevision=terrainRevision,ledger=new PrologueProgress{checkpoint="mine_start",position=startFeet,checkpointPosition=startFeet,yaw=startYaw},ui=new UiProgress()};
        }
        // Coordinates/IDs are repaired against the current scene, independently of file integrity.
        public bool Defeat(string id,int reward){if(string.IsNullOrEmpty(id)||defeated.Contains(id))return false;defeated.Add(id);ledger.currency=checked(ledger.currency+reward);return true;}
    }
}
