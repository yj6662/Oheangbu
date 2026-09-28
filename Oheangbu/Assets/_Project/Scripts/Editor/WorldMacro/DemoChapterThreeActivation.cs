using System;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.App.Demo;
using Oheangbu.Data.Demo;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class DemoChapterThreeActivation
    {
        [Serializable] sealed class Check { public string name,status; }
        [Serializable] sealed class Evidence { public string status; public bool restored; public Check[] checks; }
        static void RequireReport(string name,string status,bool restored=false)
        {
            var path=Path.Combine(DemoChapterThreeAuthoring.Output,name);
            if(!File.Exists(path))throw new InvalidOperationException("Missing integration evidence: "+path);
            var evidence=JsonUtility.FromJson<Evidence>(File.ReadAllText(path));
            if(evidence==null||evidence.status!=status||restored&&!evidence.restored)
                throw new InvalidOperationException("Integration evidence did not pass/restore: "+name);
        }
        public static string Apply(bool includeGuk=false)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)
                throw new InvalidOperationException("Edit mode required.");
            var session=UnityEngine.Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            string path=DemoFoundationAuthoring.Folder+"/Campaign.asset";
            var profile=AssetDatabase.LoadAssetAtPath<DemoCampaignProfile>(path);
            if(session==null||session.gameObject.scene.path!=DemoFoundationAuthoring.Scene||session.Content.Campaign!=profile||!profile.IsValid)
                throw new InvalidOperationException("Dedicated demo scene/profile required.");
            var ids=new[]{"commission","mine_evidence","office_report","inn_rest","relay","cargo_contract","logging","deep_forest","cheongryong"};
            int enabledThrough=includeGuk?10:9;
            if(profile.Stages.Length<10||!ids.SequenceEqual(profile.Stages.Take(9).Select(s=>s.Id))||profile.Stages.Skip(enabledThrough).Any(s=>s.Implemented)||profile.Stages.Take(7).Any(s=>!s.Implemented))
                throw new InvalidOperationException("Unexpected campaign prefix; no asset changes made.");
            RequireReport("runtime_tests.json","PASS_API_INTEGRATION",true);
            RequireReport("scene_audit.json","PASS_AUTHORING_ONLY");
            RequireReport("attack_tests.json","PASS"); RequireReport("growth_tests.json","PASS");
            RequireReport("boss_reward_tests.json","PASS"); RequireReport("field_tests.json","PASS");
            if(includeGuk)
            {
                RequireReport("guk_revisit_audit.json","PASS");RequireReport("guk_reward_tests.json","PASS");
                var evidence=JsonUtility.FromJson<Evidence>(File.ReadAllText(Path.Combine(DemoChapterThreeAuthoring.Output,"runtime_tests.json")));
                foreach(string name in new[]{"real controller rides Guk to 2.4m Holding","actual authored Guk interaction commits 60 and stage ten","actual repeated Guk interaction cannot repay","field reward and prior Ren use persisted together"})
                    if(evidence.checks==null||!evidence.checks.Any(c=>c.name==name&&c.status=="PASS"))throw new InvalidOperationException("Missing actual Guk evidence: "+name);
                if(profile.Stages[9].Id!="guk_return"||profile.Stages[9].TriggerId!=DemoGukRevisitSite.Id)
                    throw new InvalidOperationException("Unexpected Guk stage identity.");
            }
            var lesson=profile.Stages[7];var boss=profile.Stages[8];
            if(lesson.TriggerId!=DemoGrowthLessonLink.LessonId||boss.TriggerId!=WorldMacroPlaytestSession.CheongryongId)
                throw new InvalidOperationException("Unexpected content identities.");
            var backup=Path.Combine(DemoChapterThreeAuthoring.Output,"Backups","before-activation");
            Directory.CreateDirectory(backup);
            var original=Path.Combine(backup,"Campaign.asset");if(!File.Exists(original))File.Copy(path,original);
            Undo.RecordObject(profile,"Enable implemented deep forest and Cheongryong");
            lesson.Event=DemoEventKind.GrowthInterrupted;lesson.TongboReward=40;lesson.Implemented=true;
            lesson.Objective="심부에서 금 술식으로 나무의 생장을 끊는다";
            boss.Event=DemoEventKind.BossDefeated;boss.TongboReward=160;boss.Implemented=true;
            if(includeGuk)
            {
                var guk=profile.Stages[9];guk.Event=DemoEventKind.FieldUsed;guk.TongboReward=60;guk.Implemented=true;
                guk.Objective="벌목장 쉼터 옆 높은 석대에 국으로 올라 보따리를 살핀다 [F]";
            }
            profile.WorkInProgressText="다음 여정은 제작 중이다. 현재 표시된 목표까지 진행할 수 있다.";
            EditorUtility.SetDirty(profile);AssetDatabase.SaveAssets();
            string report="Implemented prefix "+enabledThrough+"/16. Deep forest 40 / Cheongryong 160"+(includeGuk?" / Guk revisit 60. Escort and later stages remain disabled.":". Guk revisit and later stages remain disabled.")+" Native traversal, visual approval and balance are unverified.";
            File.WriteAllText(Path.Combine(DemoChapterThreeAuthoring.Output,"activation.txt"),report);
            return report;
        }
    }
}
