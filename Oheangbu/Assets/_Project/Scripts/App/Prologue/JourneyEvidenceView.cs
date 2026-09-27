using UnityEngine;

namespace Oheangbu.App.Prologue
{
    // Only committed quest progress changes the physical evidence presentation.
    public sealed class JourneyEvidenceView : MonoBehaviour
    {
        public PrologueSession Session;
        public string EvidenceId;
        public string ReportedId;
        public GameObject AtSite;
        public GameObject AtRecipient;

        void LateUpdate() => RefreshFromProgress();

        public void RefreshFromProgress()
        {
            if (Session == null || Session.Progress == null) return;
            bool reported = Session.Progress.completed.Contains(ReportedId);
            bool collected = Session.Progress.completed.Contains(EvidenceId);
            if (AtSite != null && AtSite.activeSelf != (!collected && !reported))
                AtSite.SetActive(!collected && !reported);
            if (AtRecipient != null && AtRecipient.activeSelf != reported)
                AtRecipient.SetActive(reported);
        }
    }
}
