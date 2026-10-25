using UnityEngine;

namespace Oheangbu.App
{
    // D308-150 V9 step 4 (legacy bridge): read-only access to the old main game's bodies of the ㄱ-final attacks 각 낙 삭 악. The old route did not fly their visual-set
    // prefab (the pre-#308 catalogue body); EAGiyeokRuntime instantiated the EA profile's Root / Burn / Pierce / Return prefab (SPEC 11.45, PF_V308j_giyeok_*). The bridge
    // reads the same prefab here. Nothing is written.
    public sealed partial class EAGiyeokRuntime
    {
        /// <summary>The prefab the old route showed for <paramref name="letter"/> (null for a letter it does not own or an empty profile field).</summary>
        public GameObject PresentationPrefab308(char letter)
        {
            if (profile == null || !Owns(letter)) return null;
            return letter == '각' ? profile.RootPrefab : letter == '낙' ? profile.BurnPrefab : letter == '삭' ? profile.PiercePrefab : profile.ReturnPrefab;
        }
    }
}
