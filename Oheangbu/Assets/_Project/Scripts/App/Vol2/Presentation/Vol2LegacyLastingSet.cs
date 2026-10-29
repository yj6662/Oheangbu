using System;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>D308-150 V9 step 5a (TEST, presentation only): the vol.2-owned variants of the old lasting parts the legacy bridge draws a verb state with. The old parts
    /// (결계 rims, ward membranes, install seals, buff bands - built faint, knee-high, "dark 3 s then pale") were hardly read on the 적로 soil under the ink-wash look from
    /// the pitch-0 shoulder camera (step 4). The variants are COPIES of those prefabs (Vol2Vfx/LegacyLasting/, built by the editor command
    /// Oheangbu.EditorTools.SpellVFX120.Vol2LastingBuild Run "build"; the originals and the glyph table are never written), darker and denser ink in the same family:
    ///  - Growth: a 결계 rim grown into a standing thicket of ink strands and branches (남긴다's residue, any frame);
    ///  - Variants: rim -> a darker carried ring with rising motes (ㅓ), ward membrane -> a continuous ink band with an undulating film and rising motes (ㅜ / ㅗ ring states),
    ///    install seal -> an upright seal plate with its post, floating above the body (심는다 on a body), buff band -> a dark wrap up the great brush (ㅓ).
    /// No emission, no additive, no light. <see cref="Enabled"/> is the switch (on = the variants in the lab, off = the step 4 look as built); the capture's lasting=0|1
    /// overrides it for one run. Read by Vol2LegacyStage from Resources (<see cref="ResourcePath"/>); without the asset the stage draws the old parts.</summary>
    [CreateAssetMenu(menuName = "Oheangbu/Vol2/Legacy Lasting Set (TEST)", fileName = "Vol2LegacyLastingSet")]
    public sealed class Vol2LegacyLastingSet : ScriptableObject
    {
        public const string ResourcePath = "Vol2LegacyLastingSet";

        [Serializable]
        public struct Pair
        {
            public GameObject From;
            public GameObject To;
        }

        [Tooltip("TEST: draw the verb states with the vol.2 variants (on) or with the old parts as built (off, the step 4 look).")]
        public bool Enabled = true;

        [Tooltip("Old part prefab -> its vol.2 variant (rims, the ward membranes' Aura308 prefabs, seals, buff bands).")]
        public Pair[] Variants = new Pair[0];

        [Tooltip("The standing ink growth of 남긴다's residue, per element (목 화 토 금 수).")]
        public GameObject[] Growth = new GameObject[5];

        [Tooltip("The dark wrap up the great brush, per element (목 화 토 금 수): an ㅓ state whose own aura is not a buff band gets it beside its own aura.")]
        public GameObject[] Wrap = new GameObject[5];

        [Tooltip("A ring / fan state (ㅜ bands, 띠 seeds, the trip ring, the fan seeds): the element's 결계 rim as a continuous dark band with undulating strands and rising motes, per element (목 화 토 금 수). It replaces the ward membrane, whose standing sheets did not show in the ink-wash look (step 5a probe).")]
        public GameObject[] Band = new GameObject[5];

        [Tooltip("심는다 on a body: the element's guard strokes made a planted ink stake standing at the body's side, rising over its silhouette, per element (목 화 토 금 수).")]
        public GameObject[] Stake = new GameObject[5];

        [Header("KCISA patterns (ritual language on the states; the pack is read only, these are vol.2-owned decals)")]
        [Tooltip("A ring state's pattern band on the ground (Pattern_79, the toothed ring), per element: under ring bands, ring / disc residues and the ㅓ carried ring.")]
        public GameObject[] PatRing = new GameObject[5];
        [Tooltip("A fan state's pattern clipped to the fan (Pattern_57, the flourished roundel, mapped from the apex), per element.")]
        public GameObject[] PatFan = new GameObject[5];
        [Tooltip("A seed's seal standing above the body it waits on (Pattern_25, the 回 seal), per element.")]
        public GameObject[] PatSeal = new GameObject[5];
        [Tooltip("The seal plate's half size (m) and how far over the body's top it floats.")]
        public float PatSealSize = .32f, PatSealAbove = .25f;

        [Tooltip("The stake stands this far (m) out from the body's side.")]
        public float StakeOut = 0.35f;

        [Tooltip("A seed's seal floats this far (m) above the top of the body it was planted on.")]
        public float SealAbove = 0.3f;

        [Tooltip("The seal's size (1 = the old 1 m seal).")]
        public float SealScale = 1f;

        [Tooltip("Step 4 also held the element's plain hit on the body's chest while a seed waits (it sat on the fur). Off with the variants: the seal above the body reads.")]
        public bool HeldSeedInk = false;

        public GameObject VariantOf(GameObject from)
        {
            if (from == null || Variants == null) return null;
            foreach (var p in Variants) if (p.From == from && p.To != null) return p.To;
            return null;
        }

        public bool IsVariant(GameObject go)
        {
            if (go == null || Variants == null) return false;
            foreach (var p in Variants) if (p.To == go) return true;
            return Array.IndexOf(Growth, go) >= 0 || Array.IndexOf(Wrap, go) >= 0 || Array.IndexOf(Stake, go) >= 0 || Array.IndexOf(Band, go) >= 0;
        }

        public GameObject BandOf(Body b) { int i = (int)b; return Band != null && i >= 0 && i < Band.Length ? Band[i] : null; }

        public static GameObject Of(GameObject[] table, Body b) { int i = (int)b; return table != null && i >= 0 && i < table.Length ? table[i] : null; }

        public GameObject StakeOf(Body b) { int i = (int)b; return Stake != null && i >= 0 && i < Stake.Length ? Stake[i] : null; }

        public GameObject GrowthOf(Body b) { int i = (int)b; return Growth != null && i >= 0 && i < Growth.Length ? Growth[i] : null; }
        public GameObject WrapOf(Body b) { int i = (int)b; return Wrap != null && i >= 0 && i < Wrap.Length ? Wrap[i] : null; }
    }
}
