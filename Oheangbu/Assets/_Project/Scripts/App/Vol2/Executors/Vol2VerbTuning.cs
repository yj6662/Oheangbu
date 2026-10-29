namespace Oheangbu.App
{
    /// <summary>TEMPORARY EXCEPTIONS of slice 4 (Exit Blocking): numbers the 남긴다 / 번진다 executors need that NumbersV3 does not hold. None is
    /// spell balance beyond the stated reading; each is a stand-in to be replaced by a data key (or confirmed and moved to the data) before the
    /// vol.2 runtime leaves the lab. They sit apart from <see cref="Vol2ShapeTuning"/> (slice 3's, TE-S3-n) so the slices do not edit one class.
    ///  TE-S4-1 ResidueShareFraction: the data gives the residue 0.9 "shared by the touching targets" but not the size of one touch. Reading:
    ///          each body takes 1/3 of the residue's initial amount the FIRST time it touches the footprint (three bodies drain it; a fourth finds
    ///          what is left). The pool is drawn from the material registry, never re-paid per tick (design section 14).
    ///  TE-S4-2 PathResidueRadius: a path leaves its residue as a disc at the landing point ("착점 한 줄기"); the data has no radius.
    ///  TE-S4-3 MarkRadius: the disc of a spread mark (3 s); no radius in the data.
    ///  TE-S4-4 BackSpreadDelayUs: 논's back-spread follows the forward one after this pause (the data gives the amounts, not the beat).
    ///  TE-S4-5 SpreadStopsPathAtFirst: a path that lands something (남긴다, 번진다) ends at the first body, like 한 번 does by rule (plan.FirstOnly);
    ///          the rules only name 한 번 and 가른다 as first-only, so the landing verbs take it here.</summary>
    public sealed class Vol2VerbTuning
    {
        public decimal ResidueShareFraction = 1m / 3m;
        public float PathResidueRadius = 0.6f;
        public float MarkRadius = 0.6f;
        public long BackSpreadDelayUs = 400000;
        public bool SpreadStopsPathAtFirst = true;
    }
}
