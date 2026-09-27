using UnityEngine;
namespace Oheangbu.App.World
{
    // Only the walker and vehicle receive callbacks; props have no per-frame scripts.
    public sealed class CompactContactRelay265 : MonoBehaviour
    {
        public CompactEnvironmentContact265 Hub;
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if(Hub==null||Hub.Session==null||Hub.Session.GameplayInputBlocked||Mathf.Abs(hit.normal.y)>.65f)return;
            Hub.TryContact(hit.collider,hit.point,hit.moveLength/Mathf.Max(.001f,Time.deltaTime),Time.time);
        }
        void OnCollisionEnter(Collision hit)
        {
            if(Hub==null||Hub.Session==null||Hub.Session.GameplayInputBlocked||hit.contactCount==0)return;
            Hub.TryContact(hit.collider,hit.GetContact(0).point,hit.relativeVelocity.magnitude,Time.time);
        }
    }
}
