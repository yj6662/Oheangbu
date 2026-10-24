namespace Oheangbu.App
{
    /// <summary>TEMPORARY EXCEPTIONS of slice 8 (reactions, world materials, field gates; Exit Blocking). Numbers and readings the reaction engine needs that NumbersV3 does not hold.
    /// None is new spell balance: every combat number of a reaction still comes from NumbersV3 through ReactionRulesV3 (reaction.*, timing.child.life); these are the technical
    /// stand-ins and the readings of an unspecified sentence. Each is to be replaced by a data key, or confirmed and moved into the data, before the vol.2 runtime leaves the lab.
    ///  TE-S8-1  GhostMinBalance: a MOVING SHAPE (the narrow path in flight, a fan or ring at the moment it lands) takes part in a reaction as an acting body that is not a material
    ///           (the rules allow it: only the contacted side must be a material). Its reaction balance is the cast plan's whole total (what the same ink would pay), at least this
    ///           much. The balance is spent by the shape's first reaction (consumption is one per body), so a shape reacts once; the shape itself flies on.
    ///  TE-S8-2  DirectBudget reading: the rules' DirectAreaBudget ("같은 먹 직접 광역 대비 반응 총 피해 ≤ 0.8배") is the acting cast's plan total for a shape, the acting material's
    ///           Initial amount for a standing material. The product's damage budget (x reaction.damageCap, x reaction.enemy.damageCap for an enemy igniter) is SHARED by the bodies it
    ///           reaches (not paid to each).
    ///  TE-S8-3  The product reaches the bodies of the other faction that stand in the CONTACTED material's footprint at the moment of the reaction (Vol2Footprint.Touches). Enemy-owned
    ///           reactions would reach the player, who is not a Vol2 target: no player port exists yet, so they reach nobody (recorded, AffectedBodies = 0).
    ///  TE-S8-4  Slow (발 빠짐 / 진창) uses the wood body's bind multiplier (body.wood.moveMultiplier) as the movement multiplier and the rules' duration; it is a bind request to the status port.
    ///  TE-S8-5  Lasting product (달군 쇠, reaction.fireMetal.damageDuration 2 s): ticks every DamageTickUs (the same cadence the burn uses, TE-S3-3); the budget share is split over the ticks.
    ///  TE-S8-6  Footprint contact between two standing materials is exact for disc / ring / disc-fan / disc-ring and SAMPLED (SampleStepMeters / polar samples) for fan-fan and fan-ring;
    ///           a moving path is the swept stadium (exact against a disc, sampled against a fan or ring). Body-attached materials have no world extent: they react only when a hostile
    ///           shape is submitted as touching the host (Vol2ReactionEngine.SubmitHostTouch); the host disc is HostRadius.
    ///  TE-S8-7  A material that holds nothing (a waiting seed, the hardened ground, the 넌 carrier: amount 0) holds no balance, so it is no reaction fuel (the rules refuse Remaining 0).
    ///           Open design question: should a waiting seed react?
    ///  TE-S8-8  Pair settling: a pair of materials the rules refused (same cast, depth, duplicate contact, thickened ...) is not tried again while both live.
    ///  TE-S8-9  A shape of a Return cast is read as its first footprint (the way out); the returning leg is not swept (Vol2ReactionEngine.SubmitShape(returning: true) is the entry once the
    ///           Return run reports its legs). The rules' ReturnSameMaterial refusal is fixture-tested through that entry.
    ///  TE-S8-10 Ring slot (둘레 1): a player holds ONE standing ring at a time (D308-121 "one standing domain", E5 "둘레 1개 규칙"): a new ring-footprint material of another cast of mine
    ///           ends the old one Replaced. EVERY ring-footprint material of a cast of mine counts - the 남긴다 / 가른다 residue, the 굼 ring SEED (a Mark), the 뭄 hardened ground - because a ring
    ///           is a ring (a 뭄 cast after a 국 replaces the 국 ring and the other way round). Not rings in this sense: the world's and the enemy's materials, reaction children, split halves
    ///           (same cast), the 넌 carrier, and the 몸 giant (a fan). Open design question: should a waiting ring seed take the slot?
    ///  TE-S8-11 World materials get CastIds from 8,000,000 up (cast ids of the issuer start at 1; the lab plants from 9,000,000) and live WorldLifeSeconds unless the spec says otherwise.
    ///  TE-S8-12 Field gates: the gate adapter finds a SpellFieldGate308 by physics overlap around the cast centre (as FieldGateEffect308 does), picks the nearest closed gate of the glyph's kind
    ///           the cast can reach (GateReachMeters beyond its edge) and opens it - outside combat only. The platform / ground (국, 뭄) is a temporary collider for the material's life.
    ///  TE-S8-14 A 가른다 cast's shape does not react (OnShapeStep returns early for Verb.Split): its contact with a material is the split. Its halves, its 솟 metal and the strip are read as usual.
    ///  TE-S8-13 Enemy dash / leap block (뭄): every tick each enemy standing on a live hardened ground gets an EnemyDomainState308 "dash 0" entry that lasts BlockHoldSeconds past the tick.
    /// </summary>
    public sealed class Vol2ReactionTuning
    {
        public decimal GhostMinBalance = 0.1m;
        public float HostRadius = 0.45f;
        public long DamageTickUs = 500000;
        public float SampleStepMeters = 0.2f;
        public int FanPolarSamples = 9;
        public int FanDepthSamples = 5;
        public int RingSamples = 36;
        public int MaxMaterialsScanned = 256;
        public int LedgerKeep = 1024;
        public int AttemptKeep = 512;
        // TE-S8-10
        public bool RingSlot = true;
        // TE-S8-11
        public long WorldCastBase = 8000000;
        public float WorldLifeSeconds = 3600f;
        // TE-S8-12
        public float GateReachMeters = 1.5f;
        public float PlatformThickness = 0.3f;
        // TE-S8-13
        public float BlockHoldSeconds = 0.25f;
    }
}
