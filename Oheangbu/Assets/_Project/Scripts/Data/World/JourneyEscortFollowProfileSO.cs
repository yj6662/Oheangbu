using UnityEngine;
namespace Oheangbu.Data.World
{
    [CreateAssetMenu(menuName="Oheangbu/World/Journey Escort Follow")]
    public sealed class JourneyEscortFollowProfileSO : ScriptableObject
    {
        [Min(.1f)] public float Speed=3.6f;
        [Min(.5f)] public float StopDistance=2.4f;
        [Min(8)] public float MaximumDistance=25;
        [Min(.05f)] public float PathInterval=.35f;
        [Range(.02f,.35f)] public float MaximumStep=.2f;
        [Min(.3f)] public float ClearanceRadius=.65f;
        [Min(1.7f)] public float ClearanceHeight=2;
        public Vector3 CargoOffset=new Vector3(0,.65f,-.55f);
    }
}
