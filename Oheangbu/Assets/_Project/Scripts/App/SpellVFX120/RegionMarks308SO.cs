using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    public enum BodyRegion308 { None, Legs, Ankles, WeaponHand, Chest, Collar, Head }

    // #308 D308-127 (debate 11 A + g2, debate 14 - TEST): what a spell left on an enemy, drawn on the part of its body the reading
    // names - ink running down from that part. Presentation data only (the stage reads the enemy's state, never writes it):
    //   Slowed -> the shins (ink down both legs, >= 12 cm wide)       Bound -> rings round the ankles (각 · 앗)
    //   Weakened -> the weapon hand (a blot twice a head, drips)    Armour -> pale branching cracks on the chest (>= 3 cm)
    //   Exposed -> a seal on the collarbone (paper disc ~.3 m, dark angular seal, above the chest's install mark)
    //   Blind -> a dark band across the eyes
    // At most MaxRegions parts show on one enemy: the first ones of Priority it carries (Codex: the bound enemy first). The
    // guard, the broken guard, the root and the brand stay on the chest mark (InkPresentForms308) and are not counted.
    // Drawn with PigmentTrail308 (unlit alpha blend, no emission): the pigment is the paper, ink parts are the paper times a dark
    // vertex colour, so a crack can be lighter than the body (InkBurst308 cannot draw paper).
    [CreateAssetMenu(menuName = "Oheangbu/Spell/Region Marks 308", fileName = "RegionMarks308")]
    public sealed class RegionMarks308SO : ScriptableObject
    {
        public const PresentState308 RegionBits = PresentState308.Bound | PresentState308.Slowed | PresentState308.Weakened |
            PresentState308.Armour | PresentState308.Exposed | PresentState308.Blind;

        public Material Material;
        [ColorUsage(false, false)] public Color Paper = new Color(.902f, .886f, .843f, 1f);   // #E6E2D7 (UI 한지, the material's pigment)
        [ColorUsage(false, false)] public Color Ink = new Color(.118f, .110f, .102f, 1f);     // #1E1C1A
        public PresentState308[] Priority = { PresentState308.Bound, PresentState308.Exposed, PresentState308.Armour, PresentState308.Weakened, PresentState308.Slowed, PresentState308.Blind };
        [Range(1, 3)] public int MaxRegions = 2;
        [Min(1)] public int Capacity = 16;
        [Min(.01f)] public float FadeIn = .2f, FadeOut = .3f;
        [Tooltip("m: the body height the drawings are authored for (scale = height / this)")]
        [Min(.5f)] public float ReferenceHeight = 1.8f;

        /// <summary>The vertex colour that makes the paper pigment the ink (per channel, in linear values: the shader multiplies).</summary>
        public Color InkTint
        {
            get
            {
                Color p = Paper.linear, i = Ink.linear;
                return new Color(i.r / Mathf.Max(.001f, p.r), i.g / Mathf.Max(.001f, p.g), i.b / Mathf.Max(.001f, p.b), 1f);
            }
        }

        public static BodyRegion308 RegionOf(PresentState308 bit)
        {
            switch (bit)
            {
                case PresentState308.Slowed: return BodyRegion308.Legs;
                case PresentState308.Bound: return BodyRegion308.Ankles;
                case PresentState308.Weakened: return BodyRegion308.WeaponHand;
                case PresentState308.Armour: return BodyRegion308.Chest;
                case PresentState308.Exposed: return BodyRegion308.Collar;
                case PresentState308.Blind: return BodyRegion308.Head;
                default: return BodyRegion308.None;
            }
        }

        /// <summary>The parts one enemy shows: the first `max` readings of the priority it carries. Returns how many went into `into`.</summary>
        public static int Pick(PresentState308 state, PresentState308[] priority, int max, BodyRegion308[] into)
        {
            int n = 0;
            if (priority == null || into == null) return 0;
            foreach (var bit in priority)
            {
                if (n >= max || n >= into.Length) break;
                if ((state & bit) != 0 && RegionOf(bit) != BodyRegion308.None) into[n++] = RegionOf(bit);
            }
            return n;
        }
    }
}
