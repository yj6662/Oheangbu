using UnityEngine;

namespace Oheangbu.App
{
    // [SPEC-DEV-TEST-HUB §2] 방 포탈·귀환 포탈 — F로 대상 씬을 Single 로드한다
    public sealed class DevScenePortal : DevInteractable
    {
        [Tooltip("대상 씬 경로(Assets/…/*.unity)")]
        [SerializeField] private string _scenePath = "";
        [SerializeField] private string _hint = "F: 진입";

        protected override string HintLine => _hint;

        public override void Interact()
        {
            Debug.Log($"[Hub] 이동 → {_scenePath}");
            DevSceneFlow.Load(_scenePath);
        }
    }
}
