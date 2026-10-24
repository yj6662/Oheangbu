namespace Oheangbu.App
{
    /// <summary>TEMPORARY EXCEPTIONS of slice 6 (Exit Blocking): what the 가른다 executor needs that NumbersV3 does not hold. Technical stand-ins or
    /// readings, none is new spell balance; each is to be replaced by a data key (or confirmed and moved into the data) before the vol.2
    /// runtime leaves the lab. Apart from <see cref="Vol2ShapeTuning"/> (TE-S3-n) and <see cref="Vol2VerbTuning"/> (TE-S4-n).
    ///  TE-S6-1 HalfOffsetFraction / HalfRadiusScale: a split material is cut in two but the data gives no geometry for the halves. Reading: a DISC
    ///          footprint is cut by the line of the shape; the two halves stand HalfOffsetFraction x radius either side of it, each of radius
    ///          radius x HalfRadiusScale (half the area). Fan / ring / attached footprints are not partitioned (the halves keep the whole footprint).
    ///  TE-S6-2 SampleRadius, FanSampleRows / Cols, RingSamples: "does the shape touch this material" is answered with the exact body tests of
    ///          Vol2ShapeGeometry; a non-disc footprint is turned into sample discs of SampleRadius (a disc is its own sample).
    ///  TE-S6-3 CasterRadius: ㅓ+ㅅ splits the materials "I pass through" (내가 지나는 잔존물): read as the caster's own body path of the tick,
    ///          a disc of CasterRadius. The swing capsule is used only by 섯 (its front and back cut).
    ///  TE-S6-4 섯 rear cut: the rules book the rear term (0.35, once with the first and once with the second contact of a body) per target, so
    ///          it is applied as a second damage request on that same body ("Rear" contact); "뒤쪽" is also the mirrored swing capsule behind the
    ///          caster, which splits the materials standing there. No body is hurt by the rear capsule on its own.
    ///  TE-S6-5 StripShare: the data gives the length of the strip (3 s, 4.5 s), not how much armour goes; the whole of it goes (Vol2Strip.FullShare).
    ///  TE-S6-6 PierceStopsAtCount: 삿 (peak.삿.pierceCount = 3) strips the first three bodies the path meets; the path itself runs on (12 m / wall)
    ///          but a fourth body is not even logged as touched.
    ///  TE-S6-7 솟 draw: a body that enters the metal residue draws Host.VerbTuning.ResidueShareFraction of what it held at birth (TE-S4-1), once.
    ///  TE-S6-8 RecrossRestrips: a body crossing the ring AGAIN has its guard and armour stripped again (a new crossing); the damage and the 0.4
    ///          are only paid at its first.
    ///  TE-S6-9 BladeOnDirectHits: the 날붓 (posture 10) is asked for every paid Instant / Second contact of a ㅓ+ㅅ cast, once per AttackId (and
    ///          once per swing, body and kind); the rear cut and the reaction draw are not direct hits.
    ///  TE-S6-10 StripValueEventId: the rules want a positive MaterialId on any shared draw; the strip makes no material, so the strip's own
    ///          AttackId is passed.</summary>
    public sealed class Vol2SplitTuning
    {
        public float HalfOffsetFraction = 0.5f;
        public float HalfRadiusScale = 0.70710678f;
        public float SampleRadius = 0.6f;
        public int FanSampleRows = 3;
        public int FanSampleCols = 5;
        public int RingSamples = 16;
        public float CasterRadius = 0.4f;
        public bool PierceStopsAtCount = true;
        public bool RecrossRestrips = true;
        public bool BladeOnDirectHits = true;
    }
}
