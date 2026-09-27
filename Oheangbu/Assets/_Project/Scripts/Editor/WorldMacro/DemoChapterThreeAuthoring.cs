using System;
using System.IO;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Only verified pieces are exposed here; no stage is enabled by running diagnostics.
    public static class DemoChapterThreeAuthoring
    {
        public static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Demo/Chapter3"));
        public static string Execute(string command)
        {
            Directory.CreateDirectory(Output);
            if (command.StartsWith("compact:")) return WorldMacroCompactAuthoring.Execute(command.Substring("compact:".Length));
            if (command.StartsWith("tongbo-budget:")) return DemoTongboBudgetAuthoring.Execute(command.Substring("tongbo-budget:".Length));
            if (command.StartsWith("npc-roles:")) return DemoNpcRolesAuthoring.Execute(command.Substring("npc-roles:".Length));
            if (command.StartsWith("encounter-expansion:")) return DemoEncounterExpansionAuthoring.Execute(command.Substring("encounter-expansion:".Length));
            if (command.StartsWith("guk-revisit:")) return DemoGukRevisitAuthoring.Execute(command.Substring(12));
            if (command.StartsWith("escort-scene:")) return DemoEscortSceneAuthoring.Execute(command.Substring(13));
            if (command.StartsWith("escort-navigation:")) return DemoEscortNavigationAuthoring.Execute(command.Substring("escort-navigation:".Length));
            if (command.StartsWith("escort-runtime:")) return DemoEscortRuntimeChecks.Execute(command.Substring(15));
            if (command.StartsWith("south-gate-scene:")) return DemoSouthGateSceneAuthoring.Execute(command.Substring(17));
            if (command.StartsWith("south-gate-runtime:")) return DemoSouthGateRuntimeChecks.Execute(command.Substring(19));
            if (command.StartsWith("runtime:")) return DemoChapterThreeRuntimeChecks.Execute(command.Substring(8));
            if (command.StartsWith("scene:")) return DemoChapterThreeSceneAuthoring.Execute(command.Substring(6));
            if (command == "presentation-apply") return DemoChapterThreePresentationAuthoring.Apply();
            if (command == "activate") return DemoChapterThreeActivation.Apply();
            if (command == "activate-guk") return DemoChapterThreeActivation.Apply(true);
            string result, name;
            if (command == "growth-tests") { result = DemoCheongryongGrowthChecks.Run(); name = "growth_tests.json"; }
            else if (command == "ren-tests") { result = DemoRenSurvivalChecks.Run(); name = "ren_tests.json"; }
            else if (command == "lesson-tests") { result = DemoGrowthLessonChecks.Run(); name = "lesson_tests.json"; }
            else if (command == "boss-reward-tests") { result = DemoBossRewardChecks.Run(); name = "boss_reward_tests.json"; }
            else if (command == "attack-tests") { result = DemoCheongryongAttackChecks.Run(); name = "attack_tests.json"; }
            else if (command == "attack-probe") { result = DemoCheongryongAttackChecks.RunProbe(); name = "attack_probe.json"; }
            else if (command == "body-tests") { result = DemoCheongryongBodyChecks.Run(); name = "body_tests.json"; }
            else if (command == "rig-animation-tests") { result = DemoCheongryongRigAnimationChecks.Run(); name = "rig_animation_tests.json"; }
            else if (command == "tail-tests") { result = DemoCheongryongTailSweepChecks.Run(); name = "tail_tests.json"; }
            else if (command == "presentation-tests") { result = DemoCheongryongPresentationChecks.Run(); name = "presentation_tests.json"; }
            else if (command == "collider-sync-tests") { result = DemoCheongryongColliderSyncChecks.Run(); name = "collider_sync_tests.json"; }
            else if (command == "field-tests") { result = DemoFieldSpellChecks.Run(); name = "field_tests.json"; }
            else if (command == "escort-state-tests") { result = DemoEscortStateChecks.Run(); name = "escort_state_tests.json"; }
            else if (command == "escort-session-tests") { result = DemoEscortSessionChecks.Run(); name = "escort_session_tests.json"; }
            else if (command == "escort-resume-tests") { result = DemoEscortResumeChecks.Run(); name = "escort_resume_tests.json"; }
            else if (command == "south-gate-tests") { result = DemoSouthGateGeneralChecks.Run(); name = "south_gate_tests.json"; }
            else if (command == "south-gate-completion-tests") { result = DemoSouthGateCompletionChecks.Run(); name = "south_gate_completion_tests.json"; }
            else if (command == "south-gate-integration-tests") { result = DemoSouthGateIntegrationChecks.Run(); name = "south_gate_integration_tests.json"; }
            else if (command == "guk-reward-tests") { result = DemoGukRewardChecks.Run(); name = "guk_reward_tests.json"; }
            else throw new ArgumentException("chapter3: growth-tests/ren-tests/lesson-tests");
            File.WriteAllText(Path.Combine(Output, name), result); return result;
        }
    }
}
