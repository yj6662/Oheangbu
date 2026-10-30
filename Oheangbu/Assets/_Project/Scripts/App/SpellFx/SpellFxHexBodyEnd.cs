using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    // 2026-10-30 (lane E, PLAN-308-MEMBRANE-SPELLS 3.2, the user: "'ㅜ'술식과 같은 톤의 술식들을 계속 구현해"): how a hex cell of each of the five bodies ENDS - the CSV's 몸거동 "끝:" column, per cell, far cells first.
    //   목 잔뿌리로 풀림 (판정 "끝 터짐 없음")   -> the cell narrows and drifts out a little while 2 - 3 small ink drops leave it; no body burst
    //   화 끝부터 타 재로 날림(재가 위로)         -> seams warm up 0.15 s, then the cell burns away FROM ITS LOWER EDGE up (vertex alpha); embers and ash go up
    //   토 가라앉으며 둘레 땅이 판으로 들림        -> the cell sinks 0.2 m and clears; the ground under it is lifted as a flat plate 8 - 10 cm and set back; plate chips + a little dust (nothing that falls)
    //   금 쨍하게 끊기며 파편이 옆으로 튐          -> a white seam flash for 2 frames, then gone within 3 frames (stepped, no fade); bright shards fly out to the side
    //   수 아래로 흘러내림                          -> the cell slides down 0.3 m, thinning, while ink runs down from its lower edge
    // The kit and the parts are NOT touched by this file: a part calls Begin(...) at the moment the cell's end starts (the ink and the burst) and Pose(...) every tick of the end for the cell's
    // offset / size / alpha. Nothing here holds state: the tables are readonly and every member is a pure function (Begin only spawns the short-lived particle holders of SpellFxLabShot.InkBurst).
    // All numbers are TEST (no measured look yet): they sit in the consts below, one place.

    /// <summary>One cell's end at one moment (see <see cref="HexBodyEnd.Pose"/>). Offsets are in metres: <see cref="Out"/> along the cell's own outward direction, <see cref="Up"/> along world up
    /// (negative = down). <see cref="ScaleH"/> / <see cref="ScaleV"/> multiply the cell's horizontal / vertical corner radius; <see cref="Alpha"/> multiplies the whole cell (the shader's vertex alpha,
    /// _VertexAlpha = 1); for fire <see cref="Burn"/> says how far up the cell the burn has got - feed it to <see cref="HexBodyEnd.VertexAlpha"/> for each corner.</summary>
    public readonly struct HexEndPose
    {
        public readonly float Out, Up, ScaleH, ScaleV, Alpha;
        /// <summary>Fire: height fraction (0 = the cell's bottom edge .. 1 = its top) below which the cell has burnt away; 0 = untouched, <see cref="HexBodyEnd.BurnTravel"/> = all gone.</summary>
        public readonly float Burn;
        /// <summary>Earth: metres the ground plate under the cell is raised now (rises quickly, settles back) and its alpha.</summary>
        public readonly float PlateLift, PlateAlpha;
        /// <summary>0..1: how strongly the cell's seams glow in the body's accent colour right now (fire: warming up; metal: the 2-frame white flash). The shader's own struck-cell flash dies in about
        /// 0.1 s, so while this is above 0.5 a part holds one of the shader's extra hit slots (_HitCells / _HitAges) at age ~0.02 on this cell.</summary>
        public readonly float Seam;
        /// <summary>The end is over: the cell can be dropped.</summary>
        public readonly bool Done;

        public HexEndPose(float outM, float upM, float scaleH, float scaleV, float alpha, float burn, float plateLift, float plateAlpha, float seam, bool done)
        { Out = outM; Up = upM; ScaleH = scaleH; ScaleV = scaleV; Alpha = alpha; Burn = burn; PlateLift = plateLift; PlateAlpha = plateAlpha; Seam = seam; Done = done; }

        /// <summary>The cell's world-space displacement for the cell's outward horizontal-ish direction <paramref name="outward"/> (unit).</summary>
        public Vector3 Offset(Vector3 outward) => outward * Out + Vector3.up * Up;
    }

    /// <summary>The five bodies' cell endings. <see cref="SpellFxBurstBody.Armour"/> is not a body: it is treated as water here.</summary>
    public static class HexBodyEnd
    {
        /// <summary>The fan's cell radius (circumradius, m): the size every ink scale below is quoted for; a part passes its own cell radius and the burst grows / shrinks with it (0.5x .. 2x).</summary>
        public const float CellRef = 0.275f;
        public const float Frame60 = 1f / 60f;
        /// <summary>Fire: <see cref="HexEndPose.Burn"/> ends here (1.3 = the top corner is gone by then, the front is 0.3 soft).</summary>
        public const float BurnTravel = 1.3f;

        // ---- timing and reach (all TEST) ----
        const float WoodLen = 0.30f, WoodReach = 0.10f, WoodThin = 0.45f;                       // the cell narrows to 45 % while it drifts 10 cm out
        const float FireHeat = 0.15f, FireBurn = 0.45f, FireLift = 0.05f;                       // 0.15 s warming, then 0.45 s burning up from the lower edge
        const float EarthLen = 0.50f, EarthSink = 0.20f, EarthPlate = 0.09f;                    // sinks 0.2 m; the plate under it rises 9 cm and sets back
        const float MetalFlashFrames = 2f, MetalGoneFrames = 3f;                                // 2 frames of white, then gone within 3 (stepped)
        const float WaterLen = 0.50f, WaterSlide = 0.30f, WaterThin = 0.25f;                    // slides 0.3 m down and thins to 25 % of its height
        const float BurnSoft = 0.30f;                                                           // the burn front's soft edge (fraction of the cell's height)

        // ---- ink (scales are SpellFxLabShot.InkBurst's: x the count, x sqrt the size; 1 = a full burst) ----
        const float WoodInk = 0.08f;                                                            // water look: 40 drops x 0.08 = 3 small ink drops + 1 puff
        const float FireScale = 0.5f;                                                           // 23 embers, 4 ash puffs, rising
        const float EarthPlates = 0.4f, EarthDust = 0.12f;                                      // ~4 plate chips (ochre rim) thrown up; ~3 clods and a puff of dust - nothing heavy
        const float MetalScale = 0.25f;                                                         // ~10 bright shards
        const float WaterScale = 0.3f;                                                          // 12 drops running down
        static readonly Color EarthEdge = new Color(0.78f, 0.58f, 0.30f, 1f);                   // the ground plate's ochre rim (the earth body's _Rim, a little deeper)

        /// <summary>Seconds the end of one cell lasts (<see cref="Pose"/> is <see cref="HexEndPose.Done"/> from there on).</summary>
        public static float Duration(SpellFxBurstBody body)
        {
            switch (body)
            {
                case SpellFxBurstBody.Wood: return WoodLen;
                case SpellFxBurstBody.Fire: return FireHeat + FireBurn;
                case SpellFxBurstBody.Earth: return EarthLen;
                case SpellFxBurstBody.Metal: return (MetalFlashFrames + MetalGoneFrames) * Frame60;
                default: return WaterLen;
            }
        }

        /// <summary>The delay (seconds) of a cell's end so the ends run far cells first: <paramref name="far01"/> = 0 for the cell nearest the caster .. 1 for the farthest; the farthest has delay 0.</summary>
        public static float Stagger(float far01, float span = 0.3f) => Mathf.Clamp01(1f - far01) * span;

        /// <summary>The cell's pose <paramref name="t"/> seconds after its end began (t below 0 is the untouched cell).</summary>
        public static HexEndPose Pose(SpellFxBurstBody body, float t)
        {
            t = Mathf.Max(t, 0f);
            bool done = t >= Duration(body);
            switch (body)
            {
                case SpellFxBurstBody.Wood:
                {
                    float u = Mathf.Clamp01(t / WoodLen), e = Smooth(u);
                    return new HexEndPose(WoodReach * e, 0f, Mathf.Lerp(1f, WoodThin, e), Mathf.Lerp(1f, 0.85f, e), done ? 0f : 1f - Smooth((u - 0.3f) / 0.7f), 0f, 0f, 0f, 0f, done);
                }
                case SpellFxBurstBody.Fire:
                {
                    float ub = Mathf.Clamp01((t - FireHeat) / FireBurn), e = Smooth(ub);
                    float seam = t < FireHeat ? Smooth(t / FireHeat) : 1f - Smooth((t - FireHeat) / 0.12f);          // warms up for 0.15 s, glows as the burn starts, then goes
                    return new HexEndPose(0f, FireLift * e, 1f, 1f, done ? 0f : 1f, BurnTravel * e, 0f, 0f, Mathf.Clamp01(seam), done);
                }
                case SpellFxBurstBody.Earth:
                {
                    float u = Mathf.Clamp01(t / EarthLen);
                    float lift = EarthPlate * Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.7f));                              // up quickly (peak at ~0.38 u), settles back to 0 at u = 1
                    float k = Mathf.Lerp(1f, 0.92f, u);
                    return new HexEndPose(0f, -EarthSink * Smooth(u), k, k, done ? 0f : 1f - Smooth((u - 0.15f) / 0.85f), 0f, lift, 1f - Smooth((u - 0.7f) / 0.3f), 0f, done);
                }
                case SpellFxBurstBody.Metal:
                {
                    float f = t / Frame60;                                                                           // frames since the end began
                    float a = f < MetalFlashFrames ? 1f : f < MetalFlashFrames + 1f ? 0.5f : f < MetalFlashFrames + 2f ? 0.2f : 0f;
                    return new HexEndPose(0f, 0f, 1f, 1f, a, 0f, 0f, 0f, f < MetalFlashFrames ? 1f : 0f, done);      // no shrink, no drift, no smooth fade: it is cut
                }
                default:
                {
                    float u = Mathf.Clamp01(t / WaterLen);
                    return new HexEndPose(0f, -WaterSlide * u * u, Mathf.Lerp(1f, 0.88f, u), Mathf.Lerp(1f, WaterThin, Smooth(u)), done ? 0f : 1f - Smooth((u - 0.35f) / 0.65f), 0f, 0f, 0f, 0f, done);
                }
            }
        }

        /// <summary>The vertex alpha of one corner of the cell: <paramref name="heightFrac"/> = 0 at the cell's lowest corner .. 1 at its highest (see <see cref="HeightFrac"/>). Equal to
        /// <see cref="HexEndPose.Alpha"/> everywhere except for fire, whose burn front climbs from the lower edge: corners below the front are gone, the front is <see cref="BurnSoft"/> soft.</summary>
        public static float VertexAlpha(in HexEndPose pose, float heightFrac) => pose.Alpha * Smooth((heightFrac - pose.Burn) / BurnSoft + 1f);

        /// <summary>A corner's height as a 0..1 fraction of its cell (<paramref name="yBottom"/> / <paramref name="yTop"/> = the lowest / highest corner).</summary>
        public static float HeightFrac(float y, float yBottom, float yTop) => yTop - yBottom < 1e-5f ? 0.5f : Mathf.Clamp01((y - yBottom) / (yTop - yBottom));

        /// <summary>The seconds after the end began at which a part should flash the cell's seams in the body's accent colour (hold an extra hit slot of the shader on it), or -1 for none
        /// (fire: the heat, metal: the white flash; <see cref="HexEndPose.Seam"/> says how long to hold it).</summary>
        public static float SeamFlashAt(SpellFxBurstBody body) => body == SpellFxBurstBody.Fire || body == SpellFxBurstBody.Metal ? 0f : -1f;

        /// <summary>The ink and the burst of one cell's end, spawned now at its world centre <paramref name="centre"/> (nothing outside Play). <paramref name="normal"/> = the cell's outward direction,
        /// <paramref name="size"/> = its circumradius (the burst grows with it; <see cref="CellRef"/> is the reference). Earth lifts the ground under the cell at <paramref name="groundY"/> (world y);
        /// without one it is found by a ray down from the cell (bodies, steep faces and triggers are not ground). Mid-cast ink (a throw, a landing, a contact) is NOT this: it stays
        /// <see cref="SpellFxHexKit.Spit"/>, plain and small whatever the body.</summary>
        public static void Begin(SpellFxBurstBody body, Vector3 centre, Vector3 normal, float size, float? groundY = null)
        {
            if (!Application.isPlaying) return;
            float k = Mathf.Clamp(size / CellRef, 0.5f, 2f);
            Vector3 n = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            switch (body)
            {
                case SpellFxBurstBody.Wood:       // 2 - 3 small plain ink drops, no body burst
                    SpellFxLabShot.InkBurst(centre, n, SpellFxBurstBody.Water, null, WoodInk * k);
                    break;
                case SpellFxBurstBody.Fire:       // embers and ash going UP from where the burn starts (the lower edge)
                    SpellFxLabShot.InkBurst(centre - Vector3.up * (size * 0.5f), Vector3.up, SpellFxBurstBody.Fire, null, FireScale * k);
                    break;
                case SpellFxBurstBody.Earth:
                {
                    // the ground plate: chips of it thrown UP from the ground under the cell (the armour plates, ochre rim) and a little dust - not clods falling from the cell
                    float gy = groundY ?? GroundBelow(centre, size);
                    var g = new Vector3(centre.x, gy + 0.02f, centre.z);
                    SpellFxLabShot.InkBurst(g, Vector3.up, SpellFxBurstBody.Armour, EarthEdge, EarthPlates * k);
                    SpellFxLabShot.InkBurst(g, Vector3.up, SpellFxBurstBody.Earth, null, EarthDust * k);
                    break;
                }
                case SpellFxBurstBody.Metal:      // bright shards out to the side (and a little outward), alternating left / right by the cell's place
                {
                    Vector3 side = Vector3.Cross(Vector3.up, n);
                    if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
                    side.Normalize();
                    float sign = ((Mathf.FloorToInt(centre.x * 37f) + Mathf.FloorToInt(centre.z * 53f)) & 1) == 0 ? 1f : -1f;
                    SpellFxLabShot.InkBurst(centre, (side * (sign * 0.75f) + n * 0.45f).normalized, SpellFxBurstBody.Metal, null, MetalScale * k);
                    break;
                }
                default:                          // water: ink running DOWN from the cell's lower edge
                    SpellFxLabShot.InkBurst(centre - Vector3.up * (size * 0.8f), Vector3.down, SpellFxBurstBody.Water, null, WaterScale * k);
                    break;
            }
        }

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>World y of the ground under <paramref name="p"/>: the highest walkable surface within 5 m below (bodies and steep faces ignored, as <see cref="HexGround"/> does); 0.6 m under the cell when none.</summary>
        static float GroundBelow(Vector3 p, float size)
        {
            var hits = new RaycastHit[8];
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 0.2f, Vector3.down, hits, 5f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.collider == null || h.collider.attachedRigidbody != null || h.collider is CharacterController || h.normal.y < 0.5f) continue;
                best = Mathf.Max(best, h.point.y);
            }
            return best > float.NegativeInfinity ? best : p.y - Mathf.Max(0.6f, size * 2f);
        }
    }
}
