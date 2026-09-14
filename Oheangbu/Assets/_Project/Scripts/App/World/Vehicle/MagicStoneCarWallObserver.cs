using UnityEngine;

namespace Oheangbu.App.World.Vehicle
{
    /// <summary>Temporary Play-only contact evidence; added and removed by the bounded diagnostic.</summary>
    [DisallowMultipleComponent]
    public sealed class MagicStoneCarWallObserver : MonoBehaviour
    {
        public int FrontContacts,RearContacts;
        void OnCollisionEnter(Collision collision){Record(collision);}
        void OnCollisionStay(Collision collision){Record(collision);}
        void Record(Collision collision)
        {
            if(collision.collider.name=="MagicStoneDiagnosticWall_Front")FrontContacts++;
            if(collision.collider.name=="MagicStoneDiagnosticWall_Rear")RearContacts++;
        }
    }
}
