using UnityEngine;
namespace Oheangbu.App.World
{
    // Authored identity survives culling and later combat/quest wiring.
    public sealed class WorldMacroContentPoint:MonoBehaviour
    {
        public string Id;
        public Transform Visual;
        public Vector3[] InspectionPath;
        public string CombatRole;
        public bool CombatConnected;
        public float LeashRadius=24;
        void OnDrawGizmosSelected()
        {
            Gizmos.color=new Color(.65f,.45f,.22f,.8f);Gizmos.DrawWireSphere(transform.position,LeashRadius);
            if(InspectionPath!=null)for(int i=1;i<InspectionPath.Length;i++)Gizmos.DrawLine(transform.TransformPoint(InspectionPath[i-1]),transform.TransformPoint(InspectionPath[i]));
        }
    }
}
