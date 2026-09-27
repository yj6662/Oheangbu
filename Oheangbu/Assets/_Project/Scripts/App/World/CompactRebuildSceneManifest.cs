using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    // Authoring provenance. This does not activate a gameplay session or migrate saves.
    public sealed class CompactRebuildSceneManifest : MonoBehaviour
    {
        public string Generation;
        public CompactWorldLayoutSO Layout;
        public WorldMacroPlaytestSO Content;
        public string SourceScene;
        public bool TraversalVerified;
        public WorldMacroDressingSheetSO Art;
    }
}
