namespace Oheangbu.App
{
    /// <summary>TEMPORARY EXCEPTIONS of slice 5 (Exit Blocking), apart from slice 3's TE-S3-n (Vol2ShapeTuning) and slice 4's TE-S4-n
    /// (Vol2VerbTuning): what the 심는다 executor needs and NumbersV3 does not hold. None is spell balance beyond the stated reading.
    ///  TE-S5-1 PauseCapUs: 信 stops the wait clock of the cast's un-triggered seeds while a letter is drawn, "시전당 누적 4 s" - the 4 s is in the
    ///          table sentence of all 20 rows, not in the numbers file.
    ///  TE-S5-2 InitialOccupantsTrigger (false): a fan / ring seed waits for a body to ENTER ("적이 면에 들어올 때", "고리를 넘을 때"); a body already
    ///          in the footprint when the seed is laid is not an entry (it must leave and come back). When the seed fires it works on EVERY body in
    ///          the footprint at that moment (it is one swing id for the whole cast), so the ones that were already there are hit too.
    ///  TE-S5-3 CasterBodyRadius: the caster stepping onto the 뭄 band (a point has no radius; 0.3 m is the usual body).
    ///  TE-S5-4 PressMoveMultiplier (0): 맘 "이동 불가" = movement x0 for 1.5 s (boss x0.5 of the time). The data gives the length (combat.bind.maxDuration),
    ///          not the multiplier of a full stop.
    ///  TE-S5-5 HardGroundDisc (true): 뭄 "고리 안 적 돌진 · 도약 차단" - an enemy standing inside the outer edge of a live hardened ring (the disc,
    ///          not only the band) cannot dash or jump (<see cref="Vol2SeedWorld.BlocksDashOrJump"/>). The enemy movement layer must ask it.
    ///  TE-S5-6 HostGoneEnd: a path seed whose host died / was reborn ends with HitCancelled (Vol2MaterialEnd has no "host gone").
    ///  TE-S5-7 SeedMaterialKind: a waiting seed is a Mark (PaysPool false, holds 0: nothing is paid until it acts), Tag "seed" / "giant".
    ///          Vol2MaterialKind has no Seed value (the registry file is not slice 5's).
    ///  TE-S5-8 GiantReplaces (true): "동시 1" - a new 몸 giant replaces the one standing (that one ends Replaced); the new cast already paid its ink.
    ///  TE-S5-9 PauseShiftsExpiryInPlace: 信 moves Vol2Material.ExpiresUs through its internal setter (the registry has no "shift the expiry" call).</summary>
    public sealed class Vol2SeedTuning
    {
        public long PauseCapUs = 4000000;
        public bool InitialOccupantsTrigger = false;
        public float CasterBodyRadius = 0.3f;
        public float PressMoveMultiplier = 0f;
        public bool HardGroundDisc = true;
        public bool GiantReplaces = true;
    }
}
