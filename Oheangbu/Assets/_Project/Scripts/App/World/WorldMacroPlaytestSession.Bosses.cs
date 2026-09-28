using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Data.Demo;
using UnityEngine;

namespace Oheangbu.App.World
{
    public sealed partial class WorldMacroPlaytestSession
    {
        public const string CheongryongId = "cheongryong";
        public const string SinmokId = "sinmok263";
        public const string GiyeokUnlockId = "demo_unlock_coda_giyeok";
        readonly HashSet<string> pendingBossDefeats = new HashSet<string>();
        float nextBossCommit;
        public bool HasDemoGuk => DemoCampaignActive && Progress.ledger.completed.Contains(GiyeokUnlockId);

        bool DemoEncounterAvailable(string actorId, string contentId)
        {
            if(!MountainEncounterAvailable(actorId))return false;
            if(actorId==SouthGateGeneralId)return DemoSouthGateEncounterAvailable;
            string required = actorId == CheongryongId ? "cheongryong" :
                contentId == DemoGrowthLessonLink.LessonId ? "deep_forest" : null;
            if (required == null) return true;
            if (!DemoCampaignActive) return false;
            return DemoCampaignProgression.RequiredPrefixComplete(Content.Campaign,Progress.campaign,required);
        }

        void BindDemoBosses()
        {
            foreach (var actor in Actors ?? Array.Empty<Oheangbu.App.Prologue.PrologueEncounter>())
            {
                if (actor == null) continue;
                var boss = actor.GetComponent<CheongryongCombatController>();
                if (boss == null) continue;
                boss.Configure(Walker.Body.transform, vitals, parry);
                var bones = actor.GetComponentsInChildren<Transform>(true);
                boss.ConfigureSockets(bones.FirstOrDefault(t => t.name == "MouthOrigin"),
                    bones.FirstOrDefault(t => t.name == "Body_12"));
                actor.GetComponent<EnemyController>().enabled = false;
            }
        }

        bool TryHandleDemoBossDefeat(string id)
        {
            if (!DemoCampaignActive || (id != CheongryongId && id != SinmokId)) return false;
            // Only the scene's registered, actually defeated boss may supply proof.
            var actor = Actors.FirstOrDefault(a => a != null && a.Id == id && a.gameObject.scene == gameObject.scene);
            if (actor == null || actor.GetComponent<CheongryongCombatController>() == null ||
                actor.GetComponent<EnemyVitals>().IsAlive) return true;
            if (!Progress.defeated.Contains(id)) pendingBossDefeats.Add(id);
            TryCommitPendingDemoBosses(true);
            return true;
        }

        void TryCommitPendingDemoBosses(bool force = false)
        {
            if (!ready || !DemoCampaignActive || pendingBossDefeats.Count == 0 ||
                (!force && Time.unscaledTime < nextBossCommit)) return;
            nextBossCommit = Time.unscaledTime + 1f;
            foreach (string id in pendingBossDefeats.ToArray())
            {
                var actor = Actors.FirstOrDefault(a => a != null && a.Id == id);
                if (Progress.defeated.Contains(id) || actor == null || actor.GetComponent<EnemyVitals>().IsAlive)
                { pendingBossDefeats.Remove(id); continue; }
                WorldMacroProgress proposal;string error;
                bool prepared=id==SinmokId?TryPrepareSinmokDefeat(Progress,Content.Campaign,out proposal,out error):TryPrepareCheongryongDefeat(Progress, Content.Campaign, id, out proposal, out error);
                if(!prepared)continue;
                if (!TryCommitInteraction(proposal, out error)) { Show(error); continue; }
                pendingBossDefeats.Remove(id);
                if(id==SinmokId){DetailRequested?.Invoke("신목의 매듭","검게 굳은 뿌리 사이에 오래된 쇠못이 박혀 있다. 잘려 나간 가지 끝에는 아직 새순이 돋는다.");continue;}
                // Boss/석경 acquisition is a single reading surface — fold the receipt into the lore folio (SPEC-PLAYTEST-TEXT-DIET).
                DetailRequested?.Invoke("청룡의 석경", "청룡을 물리쳤다. 종성 ㄱ · 국 · 仁를 얻었다. 조선통보 +"+Array.Find(Content.Campaign.Stages,s=>s.Id=="cheongryong").TongboReward+"\n\n국으로 높은 곳에 오를 수 있다. 仁는 치명적인 일격을 한 번 견디고, 쉼터에서 다시 충전된다.\n\n"+DemoTravelAdvice);
            }
        }

        public static bool TryPrepareSinmokDefeat(WorldMacroProgress source,DemoCampaignProfile profile,out WorldMacroProgress proposal,out string error)
        {
            proposal=null;error=null;
            if(!WorldMacroProgress.Valid(source)||profile==null||!profile.IsValid||source.defeated.Contains(SinmokId)||source.campaign.Facts.Contains("defeated:"+SinmokId))return false;
            if(!DemoCampaignProgression.TryAdvance(profile,source.campaign,DemoEventKind.BossDefeated,SinmokId,out var next,out int reward,source.defeated))return false;
            if(reward>int.MaxValue-source.ledger.currency){error="통보 보유량을 확인한다.";return false;}
            var copy=WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(source)));
            copy.campaign=next;copy.defeated.Add(SinmokId);copy.ledger.currency+=reward;
            if(!copy.campaign.Facts.Contains("defeated:"+SinmokId))copy.campaign.Facts.Add("defeated:"+SinmokId);
            if(!copy.campaign.EncounterEvidence.Contains(SinmokId))copy.campaign.EncounterEvidence.Add(SinmokId);
            proposal=copy;return true;
        }

        static bool TryPrepareCheongryongDefeat(WorldMacroProgress source, DemoCampaignProfile profile,
            string id, out WorldMacroProgress proposal, out string error)
        {
            proposal = null; error = null;
            if (id != CheongryongId || !WorldMacroProgress.Valid(source) || profile == null || !profile.IsValid ||
                source.defeated.Contains(id) || !DemoCampaignProgression.TryAdvance(profile, source.campaign,
                    DemoEventKind.BossDefeated, id, out var next, out int reward, source.defeated)) return false;
            if (reward > int.MaxValue - source.ledger.currency) { error = "통보 보유량을 확인한다."; return false; }
            var candidate = WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(source)));
            candidate.campaign = next; candidate.defeated.Add(id); candidate.ledger.currency += reward;
            // Explicit gameplay unlock, separate from the UI-only spell discovery list.
            if (!candidate.ledger.completed.Contains(GiyeokUnlockId)) candidate.ledger.completed.Add(GiyeokUnlockId);
            candidate.ui.LearnSpellLetter("국");
            if (candidate.ui.LearnVirtue("仁")) candidate.renUsed = false;
            proposal = candidate; return true;
        }
    }
}
