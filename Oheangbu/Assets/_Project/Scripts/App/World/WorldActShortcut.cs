using UnityEngine;
namespace Oheangbu.App.World
{
    public sealed class WorldActShortcut:MonoBehaviour
    {
        public string Id="guk_descent_to_capital_path";
        public string RequiredStageId="cheongryong";
        public string Label="상경길로 이어지는 내리막";
        public Vector3 DiscoveryPoint;
        public Vector3[] Path;
        public float DiscoveryRadius=1.8f;
    }
}
