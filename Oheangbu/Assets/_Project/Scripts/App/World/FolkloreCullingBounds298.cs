using System;
using UnityEngine;

namespace Oheangbu.App.World
{
    // Imported animation files are separate from the body FBX. Persist the
    // measured envelope explicitly, including procedural serpent motion.
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class FolkloreCullingBounds298 : MonoBehaviour
    {
        [Serializable] public sealed class Entry { public SkinnedMeshRenderer Skin; public Bounds LocalBounds; }
        public Entry[] Entries = Array.Empty<Entry>();
        public void Apply()
        {
            foreach (var entry in Entries)
                if (entry != null && entry.Skin != null)
                { entry.Skin.updateWhenOffscreen = false; entry.Skin.localBounds = entry.LocalBounds; }
        }
        void OnEnable() => Apply();
        void OnValidate() => Apply();
    }
}
