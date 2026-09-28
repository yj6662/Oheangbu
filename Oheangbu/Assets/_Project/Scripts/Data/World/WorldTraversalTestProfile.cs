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
        public bool IsValid=>MaximumWadingDepth>0&&DrowningSeconds>0&&FatalFallHeight>0&&MaximumWadingSlowdown>=0&&MaximumWadingSlowdown<=1;
    }
}
