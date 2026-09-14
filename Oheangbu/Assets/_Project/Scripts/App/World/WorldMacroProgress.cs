using System;
using System.Collections.Generic;
using Oheangbu.App.Prologue;
using Oheangbu.App.World.UI;
using UnityEngine;

namespace Oheangbu.App.World
{
    [Serializable] public sealed class WorldMacroProgress
    {
        public const int CurrentVersion=3;
        public int version;
        public string terrainRevision;
        public PrologueProgress ledger=new PrologueProgress();
        public List<string> defeated=new List<string>();
        public UiProgress ui=new UiProgress();
        public static bool Valid(WorldMacroProgress p)=>p!=null&&(p.version==2||p.version==CurrentVersion)&&p.ledger!=null&&p.defeated!=null&&
            p.ledger.completed!=null&&p.ledger.currency>=0&&p.ledger.dropCurrency>=0&&
            float.IsFinite(p.ledger.hp)&&p.ledger.hp>=0&&p.ledger.hp<=1&&
            float.IsFinite(p.ledger.ink)&&p.ledger.ink>=0&&p.ledger.ink<=1&&
            (p.version==2||p.ui!=null&&p.ui.IsValid());

        public static WorldMacroProgress MigrateToCurrent(WorldMacroProgress progress)
        {
            if(!Valid(progress))return null;
            if(progress.ui==null)progress.ui=new UiProgress();
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
