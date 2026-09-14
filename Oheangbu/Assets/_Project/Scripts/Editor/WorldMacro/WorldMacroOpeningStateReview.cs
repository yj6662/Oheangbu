using System;
using System.Collections.Generic;
using System.IO;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class WorldMacroOpeningStateReview
    {
        [Serializable] sealed class Check { public string name; public bool passed; }
        [Serializable] sealed class Report
        {
            public string utc,status;
            public string scope="Isolated in-memory save and menu-route policies. No player, scene, input, physics, user save, reward, capture or build is touched.";
            public List<Check> checks=new List<Check>();
            public string exception;
        }
        public static string Execute(string command="policy-check")
        {
            if(command!="policy-check")throw new ArgumentException("Expected policy-check.");
            var report=new Report{utc=DateTime.UtcNow.ToString("O")};
            var content=ScriptableObject.CreateInstance<WorldMacroPlaytestSO>();
            var opening=ScriptableObject.CreateInstance<WorldMacroOpeningProfileSO>();
            void Check(string name,bool passed)=>report.checks.Add(new Check{name=name,passed=passed});
            try
            {
                content.TerrainRevision="isolated-opening-check";
                content.StartFeet=new Vector3(250,40,350);content.StartYaw=31;
                content.InnCheckpointFeet=new Vector3(400,50,700);
                opening.StartFeet=new Vector3(-20,12,-40);opening.StartYaw=147;
                opening.Commission.Position=new Vector3(-18,12,-39);
                var baseline=WorldMacroOpeningProgress.CreateNew(content,true);
                Check("null optional profile keeps mine start",baseline.ledger.checkpoint=="mine_start"&&baseline.ledger.position==content.StartFeet&&baseline.ledger.yaw==31);
                content.Opening=opening;opening.Enabled=false;
                var disabled=WorldMacroOpeningProgress.CreateNew(content,true);
                Check("disabled profile keeps mine start",disabled.ledger.checkpoint=="mine_start"&&!WorldMacroOpeningProgress.Enrolled(disabled,opening));
                opening.Enabled=true;
                var fresh=WorldMacroOpeningProgress.CreateNew(content,true);
                Check("new game uses injected office spawn and yaw",fresh.ledger.position==opening.StartFeet&&fresh.ledger.yaw==147&&fresh.ledger.checkpoint=="village_office");
                Check("new office journey enrolled without free commission",WorldMacroOpeningProgress.Enrolled(fresh,opening)&&!fresh.ledger.completed.Contains("village_commission")&&fresh.ledger.currency==0);
                Check("mine authoring coordinates unchanged",content.StartFeet==new Vector3(250,40,350)&&content.StartYaw==31);
                Check("office checkpoint resolves injected position and yaw",WorldMacroOpeningProgress.CheckpointFeet(content,fresh)==opening.StartFeet&&WorldMacroOpeningProgress.CheckpointYaw(content,fresh)==147);
                var serialized=JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(fresh));
                Check("office marker and checkpoint survive serialization",WorldMacroOpeningProgress.Enrolled(serialized,opening)&&serialized.ledger.checkpoint=="village_office");
                content.Opening=null;
                Check("office save retains checkpoint when optional profile absent",WorldMacroOpeningProgress.CheckpointFeet(content,serialized)==serialized.ledger.checkpointPosition&&WorldMacroOpeningProgress.CheckpointYaw(content,serialized)==serialized.ledger.yaw);
                content.Opening=opening;
                foreach(int version in new[]{2,WorldMacroProgress.CurrentVersion})
                {
                    var old=WorldMacroProgress.CreateNew(content.TerrainRevision,new Vector3(999,66,555),79);
                    old.version=version;old.ledger.hasPosition=true;old.ledger.checkpoint="geumpyo_inn";
                    old.ledger.currency=83;old.ledger.dropCurrency=19;old.ledger.dropPosition=new Vector3(830,62,410);
                    old.ledger.hp=.63f;old.ledger.ink=.24f;old.ledger.completed.Add("mine_inquiry");old.ledger.completed.Add("legacy.unknown.reward");old.defeated.Add("legacy.enemy");
                    string ledger=JsonUtility.ToJson(old.ledger);
                    var migrated=WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(old)));
                    Check("v"+version+" ledger including position/drop/completion preserved",migrated!=null&&JsonUtility.ToJson(migrated.ledger)==ledger&&migrated.defeated.Contains("legacy.enemy"));
                    Check("v"+version+" existing save not enrolled or moved to office",!WorldMacroOpeningProgress.Enrolled(migrated,opening)&&migrated.ledger.checkpoint=="geumpyo_inn"&&migrated.ledger.position==old.ledger.position);
                    Check("v"+version+" existing inn checkpoint retained",WorldMacroOpeningProgress.CheckpointFeet(content,migrated)==content.InnCheckpointFeet);
                }
                var fallback=WorldMacroOpeningProgress.CreateNew(content,false);
                Check("invalid-load unsaved fallback does not enroll",fallback.ledger.checkpoint=="mine_start"&&!WorldMacroOpeningProgress.Enrolled(fallback,opening));
                opening.Commission.Currency=1;
                bool rejected=false;try{WorldMacroOpeningProgress.CreateNew(content,true);}catch(InvalidOperationException){rejected=true;}
                Check("commission reward cannot be injected",rejected);opening.Commission.Currency=0;
                opening.Commission.Id="other";rejected=false;try{WorldMacroOpeningProgress.CreateNew(content,true);}catch(InvalidOperationException){rejected=true;}
                Check("commission must use stable interaction ID",rejected);opening.Commission.Id="village_commission";
                Check("checkpoint policy retains three known IDs only",WorldMacroOpeningProgress.KnownCheckpoint("mine_start")&&WorldMacroOpeningProgress.KnownCheckpoint("geumpyo_inn")&&WorldMacroOpeningProgress.KnownCheckpoint("village_office")&&!WorldMacroOpeningProgress.KnownCheckpoint("unknown"));
                Check("journal and unknown menu routes rejected",!PlaytestUiRoot.IsSupportedPage("기록",false)&&!PlaytestUiRoot.IsSupportedPage("records",false)&&!PlaytestUiRoot.IsSupportedPage("unknown",false));
                Check("existing detail and menu routes stay available",PlaytestUiRoot.IsSupportedPage("상세",false)&&PlaytestUiRoot.IsSupportedPage("소지품",false)&&PlaytestUiRoot.IsSupportedPage("지도",false)&&PlaytestUiRoot.IsSupportedPage("옵션",true));
            }
            catch(Exception exception){report.exception=exception.ToString();}
            finally{UnityEngine.Object.DestroyImmediate(content);UnityEngine.Object.DestroyImmediate(opening);}
            report.status=report.exception==null&&report.checks.TrueForAll(c=>c.passed)?"PASS_POLICY_ONLY":"FAIL_POLICY";
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestPolish/Opening/Validation"));
            Directory.CreateDirectory(folder);string json=JsonUtility.ToJson(report,true);
            string file=Path.Combine(folder,"opening_policy.json");File.WriteAllText(file,json);
            File.WriteAllText(Path.Combine(folder,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"_opening_policy.json"),json);
            return report.status+" checks="+report.checks.Count+" report="+file;
        }
    }
}
