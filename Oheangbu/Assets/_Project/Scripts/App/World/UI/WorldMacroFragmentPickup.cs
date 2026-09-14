using UnityEngine;
using Oheangbu.App.World;

namespace Oheangbu.App.World.UI
{
    [DisallowMultipleComponent]
    public sealed class WorldMacroFragmentPickup : MonoBehaviour
    {
        public string BundleId;
        public WorldMacroPlaytestSession Session;
        public Transform VisualRoot;
        [Min(.5f)] public float Radius=2.5f;
        public string Prompt="석경 조각 살피기";
        public bool IsCollected {get;private set;}

        public Vector3 InteractionPosition=>transform.position;

        void OnEnable()
        {
            if(Session==null)Session=FindFirstObjectByType<WorldMacroPlaytestSession>();
            Session?.RegisterFragmentPickup(this);
        }
        void OnDisable(){Session?.UnregisterFragmentPickup(this);}

        public void RefreshCollectedState(bool collected)
        {
            IsCollected=collected;
            if(VisualRoot!=null&&VisualRoot!=transform){VisualRoot.gameObject.SetActive(!collected);return;}
            foreach(var renderer in GetComponentsInChildren<Renderer>(true))renderer.enabled=!collected;
            foreach(var collider in GetComponentsInChildren<Collider>(true))collider.enabled=!collected;
        }
    }
}
