using UnityEngine;
namespace Oheangbu.Data.World
{
    [CreateAssetMenu(menuName="Oheangbu/World/Traversal TEST")]
    public sealed class WorldTraversalTestProfile:ScriptableObject
    {
        [Min(.01f)] public float MaximumWadingDepth=.55f;
        [Range(0,1)] public float MaximumWadingSlowdown=.35f;
        [Min(.01f)] public float DrowningSeconds=.65f;
        [Min(1)] public float FatalFallHeight=6f;
        [Range(0,60)] public float MaximumSlope=45f;
        // #308 D308-18 (3) "걸을 수 있는 땅에서만 갱신": the fall reference height follows the feet only on walkable support.
        // false = the rule as coded before (every grounded tick, steep faces included) - kept for comparing scenes.
        public bool FallReferenceWalkableOnly=true;
        // 0 = the walker's own CharacterController.slopeLimit; a value above that limit is cut to it (FallReferenceRule308).
        [Range(0,60)] public float FallReferenceMaxSlopeDeg=0f;
        // widest gap between the feet and the walkable support under them that still counts as standing on it
        [Range(.02f,.4f)] public float FallReferenceSupportGap=.12f;
        public bool IsValid=>MaximumWadingDepth>0&&DrowningSeconds>0&&FatalFallHeight>0&&MaximumWadingSlowdown>=0&&MaximumWadingSlowdown<=1;
    }
}
