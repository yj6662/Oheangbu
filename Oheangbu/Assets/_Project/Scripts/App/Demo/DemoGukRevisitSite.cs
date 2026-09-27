using UnityEngine;

namespace Oheangbu.App.Demo
{
    // Scene-authored coordinates only. Session owns unlocks, proof and reward transactions.
    public sealed class DemoGukRevisitSite : MonoBehaviour
    {
        public const string Id = "guk_high_reward";
        public const string PreviewId = "guk_high_preview";
        public string RewardId=Id;
        public Transform LiftPad, UpperSurface, DescentExit;
    }
}
