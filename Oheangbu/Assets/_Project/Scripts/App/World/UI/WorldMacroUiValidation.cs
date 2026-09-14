using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.Prologue;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    public static class WorldMacroUiValidation
    {
        public static string Execute()
        {
            var v2=new WorldMacroProgress{version=2,terrainRevision="terrain",ledger=new PrologueProgress{currency=17,dropCurrency=3,hp=.7f,ink=.4f,completed=new List<string>{"mine_inquiry"}},defeated=new List<string>{"enemy/1"}};
            var originalLedger=v2.ledger;var originalDefeated=v2.defeated;
            var migrated=WorldMacroProgress.MigrateToCurrent(v2);
            Require(migrated!=null&&migrated.version==3,"v2 migration failed");
            Require(ReferenceEquals(migrated.ledger,originalLedger)&&ReferenceEquals(migrated.defeated,originalDefeated),"v2 owned fields were replaced");
            Require(migrated.terrainRevision=="terrain"&&migrated.ledger.currency==17&&migrated.ledger.dropCurrency==3&&migrated.ledger.completed.SequenceEqual(new[]{"mine_inquiry"})&&migrated.defeated.SequenceEqual(new[]{"enemy/1"}),"v2 fields changed");
            Require(migrated.ui.records.Contains("record.mine_inquiry"),"completed interaction record was not migrated");

            var letters=new HashSet<string>(StringComparer.Ordinal);var fragmentIds=new HashSet<string>(StringComparer.Ordinal);int count=0;
            Require(WorldMacroCollectionCatalog.AllBundles.Count==5,"bundle count");
            foreach(var bundle in WorldMacroCollectionCatalog.AllBundles)foreach(var fragment in bundle.Fragments){count++;Require(letters.Add(fragment.Letter),"duplicate letter "+fragment.Letter);Require(fragmentIds.Add(fragment.Id),"duplicate fragment "+fragment.Id);}
            Require(count==20&&WorldMacroCollectionCatalog.AllSpells.Count==20,"fragment/spell count");
            string[] expected={"아어","가거나너","사서마머","고노소모오","곰놈몸솜옴"};
            for(int i=0;i<expected.Length;i++)Require(string.Join("",WorldMacroCollectionCatalog.AllBundles[i].Fragments.Select(x=>x.Letter))==expected[i],"bundle letters "+i);

            var ui=new UiProgress();Require(WorldMacroInventoryService.TryCollectBundle(ui,"exit",out _),"first collection");
            Require(!WorldMacroInventoryService.TryCollectBundle(ui,"exit",out _),"collection was not idempotent");
            Require(ui.items.Count==4&&ui.knownSpellLetters.Count==4,"collection contents");
            byte[] cells={0,1,2,127,255};ui.SetCellBytes(cells);Require(ui.TryGetCellBytes(out var restored)&&cells.SequenceEqual(restored),"cell base64 roundtrip");
            Require(ui.IsValid(),"normalized ui invalid");
            string savedSnapshot=JsonUtility.ToJson(migrated);
            Require(!WorldMacroPlaytestSession.RequiresSnapshotWrite(savedSnapshot,JsonUtility.ToJson(migrated)),"unchanged snapshot would rewrite");
            migrated.ledger.currency++;
            Require(WorldMacroPlaytestSession.RequiresSnapshotWrite(savedSnapshot,JsonUtility.ToJson(migrated)),"changed snapshot would be skipped");
            ValidateTemporaryRecovery();
            return "PASS: v2->v3, 5 bundles/20 fragments, idempotent collection, Base64 discovery, duplicate save skip, temporary save recovery";
        }
        static void ValidateTemporaryRecovery()
        {
            string directory=Path.Combine(Path.GetTempPath(),"oheangbu-save-validation-"+Guid.NewGuid().ToString("N"));
            string slot="slot",primary=WorldMacroSaveSlot.GetPrimaryPath(directory,slot);
            try
            {
                Directory.CreateDirectory(directory);
                var state=WorldMacroProgress.CreateNew("terrain",new Vector3(1,2,3),45);state.ledger.currency=31;
                File.WriteAllText(primary+".tmp",JsonUtility.ToJson(state,true));
                var info=WorldMacroSaveSlot.Inspect(directory,slot);
                Require(info.Status==WorldMacroSaveSlotStatus.Temporary&&info.LoadedPath==primary+".tmp"&&info.Progress.ledger.currency==31,"temporary slot inspection");
                var store=new AtomicJsonStore<WorldMacroProgress>(primary,WorldMacroProgress.Valid);
                var loaded=store.Load();Require(store.LoadStatus=="temporary"&&loaded!=null&&loaded.ledger.currency==31,"temporary repository recovery");
                store.Save(loaded);Require(File.Exists(primary)&&!File.Exists(primary+".tmp"),"temporary promotion");
                File.WriteAllText(primary+".tmp",JsonUtility.ToJson(WorldMacroProgress.CreateNew("other",Vector3.zero,0)));
                info=WorldMacroSaveSlot.Inspect(directory,slot);Require(info.Status==WorldMacroSaveSlotStatus.Primary&&info.Progress.ledger.currency==31,"primary precedes temporary");
                File.WriteAllText(primary,"not json");
                var backup=WorldMacroProgress.CreateNew("backup",Vector3.zero,0);backup.ledger.currency=47;
                File.WriteAllText(primary+".bak",JsonUtility.ToJson(backup,true));
                info=WorldMacroSaveSlot.Inspect(directory,slot);Require(info.Status==WorldMacroSaveSlotStatus.Backup&&info.Progress.ledger.currency==47,"backup precedes temporary");
                loaded=store.Load();Require(store.LoadStatus=="backup"&&loaded!=null&&loaded.ledger.currency==47,"repository backup precedence");
            }
            finally
            {
                if(Directory.Exists(directory))Directory.Delete(directory,true);
            }
        }
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException("WorldMacro UI validation: "+message);}
    }
}
