using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    // Authored proof source: a real, confirmed interruption, not proximity or an interaction button.
    [RequireComponent(typeof(CheongryongGrowthController), typeof(PrologueEncounter))]
    public sealed class DemoGrowthLessonLink : MonoBehaviour
    {
        public const string LessonId = "metal_growth_lesson";
        public WorldMacroPlaytestSession Session;
        CheongryongGrowthController growth;
        PrologueEncounter encounter;
        bool interruptedProof, committed;
        float nextRetry;
        // A later death or rest must not erase a confirmed lesson while its save is retrying.
        public bool HasProof => interruptedProof;
        public string EncounterId => encounter != null ? encounter.Id : GetComponent<PrologueEncounter>().Id;
        void OnEnable()
        {
            growth = GetComponent<CheongryongGrowthController>();
            encounter = GetComponent<PrologueEncounter>();
            growth.GrowthInterrupted += OnInterrupted;
        }
        void OnDisable() { if (growth != null) growth.GrowthInterrupted -= OnInterrupted; }
        void OnInterrupted() { interruptedProof = true; TryCommit(); }
        void Update()
        {
            if (!interruptedProof || committed || Time.unscaledTime < nextRetry) return;
            nextRetry = Time.unscaledTime + 1f; TryCommit();
        }
        public bool TryCommit()
        {
            if (Session == null || !HasProof) return false;
            if (committed) return false;
            if (Session.Progress != null && Session.Progress.ledger.completed.Contains(LessonId))
            { committed = true; return false; }
            committed = Session.TryCompleteGrowthLesson(this);
            return committed;
        }
    }
}
