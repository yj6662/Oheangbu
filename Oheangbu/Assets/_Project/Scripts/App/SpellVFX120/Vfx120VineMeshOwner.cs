using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // Preview scenes destroy regular MonoBehaviours without playing their lifecycle.
    // This owner has no Update; it only releases this instance's fitted mesh in both modes.
    [ExecuteAlways]
    public sealed class Vfx120VineMeshOwner : MonoBehaviour
    {
        public Mesh Owned;
        public void Release()
        {
            if(Owned==null)return;
            var mesh=Owned;Owned=null;
            if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);
        }
        private void OnDestroy()=>Release();
    }
}
