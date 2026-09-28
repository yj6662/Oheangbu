using System;
using System.Linq;
using Oheangbu.App.Demo;
using Oheangbu.Data.Demo;
using UnityEngine;

namespace Oheangbu.App.World
{
    public sealed partial class WorldMacroPlaytestSession
    {
        public bool TryCompleteGrowthLesson(DemoGrowthLessonLink proof)
        {
            if (!ready || !DemoCampaignActive || proof == null || proof.Session != this ||
                proof.gameObject.scene != gameObject.scene || !proof.HasProof ||
                !Actors.Any(actor => actor != null && actor.gameObject == proof.gameObject && actor.Id == proof.EncounterId) ||
                !Content.Encounters.Any(spec => spec.Id == proof.EncounterId && spec.ContentId == DemoGrowthLessonLink.LessonId))
                return false;
            if (!TryPrepareGrowthLesson(Progress, Content.Campaign, out var proposal, out string error)) return false;
            if (!TryCommitInteraction(proposal, out error)) { Show(error); return false; }
            Show("금 술식이 나무의 생장을 끊었다.");
            return true;
        }

        // Progression/reward remains detached until the normal session store accepts it.
        static bool TryPrepareGrowthLesson(WorldMacroProgress source, DemoCampaignProfile profile,
            out WorldMacroProgress proposal, out string error)
        {
            proposal = null; error = null;
            if (!WorldMacroProgress.Valid(source) || profile == null || !profile.IsValid ||
                !DemoCampaignProgression.TryAdvance(profile, source.campaign, DemoEventKind.GrowthInterrupted,
                    DemoGrowthLessonLink.LessonId, out var next, out int reward, source.defeated)) return false;
            if (reward > int.MaxValue - source.ledger.currency) { error = "통보 보유량을 확인한다."; return false; }
            var candidate = WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(source)));
            candidate.campaign = next; candidate.ledger.currency += reward;
            if (!candidate.ledger.completed.Contains(DemoGrowthLessonLink.LessonId))
                candidate.ledger.completed.Add(DemoGrowthLessonLink.LessonId);
            proposal = candidate; return true;
        }

        bool TryRetryGrowthLessonInteraction(string id, out bool success)
        {
            success = false;
            if (!DemoCampaignActive || id != DemoGrowthLessonLink.LessonId) return false;
            if (Progress.campaign.Completed.Contains("deep_forest"))
            { Show("금 술식으로 생장을 끊었던 자리다."); success = true; return true; }
            var lesson = Actors.Where(actor => actor != null).Select(actor => actor.GetComponent<DemoGrowthLessonLink>())
                .FirstOrDefault(link => link != null && link.Session == this && link.HasProof);
            if (lesson != null) success = TryCompleteGrowthLesson(lesson);
            if (!success) Show("나무가 다시 자라려 할 때 금 술식으로 생장을 끊어 보자.");
            return true;
        }
    }
}
