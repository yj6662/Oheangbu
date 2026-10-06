using Oheangbu.Core.Domain;
using Oheangbu.Data.Spell;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    public struct PresentBodyInput308
    {
        public SpellDeploy308ProfileSO Profile;
        public SpellPresent308SheetSO Sheet;
        public PresentBody308 Body;
        public PresentState308 States;    // MarkState: which readings the enemy carries
        public Element Element;
        public bool Tinted;               // a mark of a cast takes its element wash; a state mark is plain ink
        public int Grade, Seed;
        public bool Hit;                  // Slash: the strike landed (a needle star where it ends)
        public float Reach;               // Slash: metres from the eye to where the strike went
        public float Height, StandOff;    // marks: chest height above the carrier's root, metres toward the eye
        public float Radius;              // ZoneRing: the zone's radius (m)
        public DeployTier308 Tier;        // ZoneRing: fewer arcs and rings on the Mobile tier
    }

    public struct PresentBodyStats308
    {
        public int Bold, Fine, Needles, Sprites, LastBirthCel;
        public float Extent;              // farthest primitive from the body's root (m)
        public float Nearest;             // nearest primitive to the eye, for the bodies drawn from the eye (m)
        public int Primitives => Bold + Fine + Needles + Sprites;
    }

    // #308 present add-on: the forms the deploy layer did not have, written with the layer's own builder
    // (InkBurstMeshBuilder308) and drawn with its burst material, so they are ink of the same hand:
    //   Slash      one sword strike: a bold sweep across the view at the strike's reach, dry at its start
    //   Blade      sword form entry: one pointed stroke drawn out ahead of the brush hand - the stroke becomes the blade
    //   MarkState  what a spell left on an enemy (bound / slowed / exposed / weakened), carried by the enemy
    //   MarkInstall, MarkCarried   an installed mark, an ember or a shot that sits in the enemy
    //   ZoneRing   forms3 (D9): where a zone ends - one ring stroke lying on the ground, 3-4 broken dry-brush arcs
    // Pure: the same input writes the same geometry. Body frame: +Z away from the eye, +X right, +Y up. Slash and Blade are
    // authored from the eye point; marks from the carrier's root (the stage turns the body so that -Z looks at the eye); the
    // zone ring from the zone's centre on the ground (+Y = the ground's normal as the stage fitted it).
    // Nothing here glows on its own: a stroke glows on its birth cels by the layer's shader, and BodyGlow gives the amount
    // 0 from the end of the birth beat on, however long the body is carried.
    public static class InkPresentForms308
    {
        struct Ctx
        {
            public InkBurstBuffer308 B;
            public PresentBodyStats308 S;
            public DeployRng308 R;
            public float Tint;
        }

        public static PresentBodyStats308 Compose(in PresentBodyInput308 input, InkBurstBuffer308 buffer)
        {
            var c = new Ctx { B = buffer, R = new DeployRng308(input.Seed) };
            c.S.Nearest = float.PositiveInfinity;
            if (input.Profile == null || input.Sheet == null || buffer == null) return c.S;
            c.Tint = input.Tinted ? input.Profile.ElementOf(input.Element).Tint : 0f;
            switch (input.Body)
            {
                case PresentBody308.Slash: Slash(ref c, input); break;
                case PresentBody308.Blade: Blade(ref c, input); break;
                case PresentBody308.MarkInstall: Install(ref c, input); break;
                case PresentBody308.MarkCarried: Carried(ref c, input); break;
                case PresentBody308.ZoneRing: Ring(ref c, input); break;
                default: State(ref c, input); break;
            }
            if (float.IsPositiveInfinity(c.S.Nearest)) c.S.Nearest = 0f;
            return c.S;
        }

        /// <summary>Seconds of a body's birth beat: its strokes are born inside it, and only inside it do they glow.</summary>
        public static float BodyBeat(SpellPresent308SheetSO sheet, int lastBirthCel, float celSeconds) =>
            Mathf.Max(sheet != null ? sheet.Pool.BodyBeat : 0f, (lastBirthCel + 1) * celSeconds);

        /// <summary>The first cel on which nothing of a body glows (the layer's own rule for a burst, InkDeployForms308.GlowEndCel).</summary>
        public static int BodyGlowEndCel(SpellPresent308SheetSO sheet, int lastBirthCel, float celSeconds)
        {
            float beat = BodyBeat(sheet, lastBirthCel, celSeconds);
            return InkDeployForms308.GlowEndCel(beat, 0f, beat, celSeconds, false);
        }

        /// <summary>The glow amount a body's material gets on a cel: the layer's amount inside the birth beat, 0 afterwards and
        /// whenever the body melts. A carried mark is ink for all the time it is carried.</summary>
        public static float BodyGlow(SpellDeploy308ProfileSO profile, int cel, int glowEndCel, float melt) => InkDeployForms308.GlowAmount(profile, cel, glowEndCel, melt);

        // ---- sword

        // One sweep on a circle about the eye, low on one side and higher on the other. Seen from the eye it crosses the view.
        static void Slash(ref Ctx c, in PresentBodyInput308 input)
        {
            var p = input.Profile; var sword = input.Sheet.Sword;
            float d = Mathf.Max(p.Stroke.NearFade + .25f, Mathf.Max(.5f, input.Reach) * sword.SlashReachShare);
            float half = Mathf.Clamp(sword.SlashArcDeg * .5f, 10f, 70f) * Mathf.Deg2Rad;
            float mirror = (input.Seed & 1) == 0 ? 1f : -1f;
            float sin = Mathf.Sin(half), cos = Mathf.Cos(half);
            Vector3 from = new Vector3(-d * sin * mirror, sword.SlashFromY, d * cos), to = new Vector3(d * sin * mirror, sword.SlashToY, d * cos);
            float width = sword.SlashWidth * (1f + sword.SlashGradeWiden * Mathf.Clamp(input.Grade, 0, 2));
            // forms3 (D12): the sweep ends in the pressed head; its straight edge is the upper one whichever way it runs
            AddArc(ref c, p, from, to, width, sword.SlashTailShare, 0, 0f, sword.TipTaper, sword.RootRamp, sword.SlashHead, -mirror, sword.Segments);
            // dry hairs that run with the sweep
            Vector3 chord = (to - from).normalized;
            for (int i = 0; i < sword.SlashHairs; i++)
            {
                float u = c.R.Range(sword.SlashHairFrom.x, sword.SlashHairFrom.y);
                Vector3 at = Vector3.Lerp(from, to, u) + Vector3.forward * (d * sword.SlashHairDepth) + Vector3.up * c.R.Range(-sword.SlashHairLift, sword.SlashHairLift);
                Vector3 side = Side(at, chord);
                // look2 round 2: a dry stroke's root is the tail cell's solid end - a straight-cut block. It lay at the START of
                // these hairs, in the open beside the sweep's tail (the "square block on the tail"). Turned round: the root is
                // at the far end, behind the sweep's wet body, and the hairs trail back toward the tail.
                float run = d * c.R.Range(sword.SlashHairRun.x, sword.SlashHairRun.y);
                if (InkBurstMeshBuilder308.AddDryStroke(c.B, at + chord * run, -chord, side, run, width * sword.SlashHairWidth, (int)(c.R.Next() * 3.99f), i, c.R.Next(), c.Tint, Vector3.zero, 0f))
                { c.S.Fine++; Born(ref c, i); Reach(ref c, at); }
            }
            if (input.Hit) Star(ref c, p, to, 5, .5f, 1f, 1);
        }

        // One pointed stroke drawn out from the lower right of the view, ahead and up: the blade.
        static void Blade(ref Ctx c, in PresentBodyInput308 input)
        {
            var p = input.Profile; var sword = input.Sheet.Sword;
            // (the tail is never nearer than the near fade, whatever the sheet says)
            Vector3 tail = new Vector3(sword.BladeFrom.x, sword.BladeFrom.y, p.Stroke.NearFade + Mathf.Max(0f, sword.BladeFrom.z));
            Vector3 axis = sword.BladeAxis.sqrMagnitude > 1e-4f ? sword.BladeAxis.normalized : Vector3.forward;
            float length = Mathf.Max(.3f, sword.BladeLength);
            Vector3 middle = tail + axis * (length * .5f);
            Vector3 side = Side(middle, axis);
            var s = new InkStroke308
            {
                Tail = tail, Axis = axis, Side = side, Bend = side, Length = length, HalfWidth = sword.BladeWidth * (1f + sword.BladeGradeWiden * Mathf.Clamp(input.Grade, 0, 2)), Curve = sword.BladeCurve, SCurve = 0f,
                TailShare = sword.BladeTailShare, Split = 1, Segments = sword.Segments > 0 ? sword.Segments : p.Stroke.CapsuleSegments, TailCell = (int)(c.R.Next() * 3.99f), BirthCel = 0f, Seed01 = c.R.Next(), TintWeight = c.Tint, MeltBias = c.R.Next(),
                DryStart = true, HeadTaper = sword.TipTaper, RootRamp = sword.RootRamp,   // look2 round 2: combed out of its dry tail, one point at its end
            };
            s.DryTailU = p.Stroke.DryStartTailU;
            InkBurstMeshBuilder308.PressHead(ref s, sword.BladeHead);   // forms3 (D12): a pressed head, not a point
            // fix pass: thin at the hand, widest near the head (a blade, not a feather) - only with the pressed head
            if (s.Press > 0f) { s.RootWidth = sword.BladeRootWidth; s.RootRamp = sword.BladeRootRamp; }
            if (InkBurstMeshBuilder308.AddStroke(c.B, s)) { c.S.Bold++; Born(ref c, 0); Reach(ref c, tail); Reach(ref c, InkBurstMeshBuilder308.HeadTip(s)); }
            // the edge: needle lines along the back of the blade, a cel later (forms3: none by default - a straight line
            // standing beside the stroke read as a pole)
            for (int i = 0; i < sword.BladeNeedles; i++)
            {
                Vector3 at = tail + axis * (length * (.25f + .3f * i)) + side * (s.HalfWidth * 1.4f);
                if (InkBurstMeshBuilder308.AddNeedle(c.B, at, axis, length * .45f, p.Stroke.NeedleWidth * .5f, -middle.normalized, 1f, c.R.Next(), Vector3.zero, 0f))
                { c.S.Needles++; Born(ref c, 1); Reach(ref c, at + axis * (length * .45f)); }
            }
        }

        // ---- marks an enemy carries (body root = the carrier's root; -Z looks at the eye)

        static Vector3 Chest(in PresentBodyInput308 input) => new Vector3(0f, Mathf.Max(.2f, input.Height), -Mathf.Max(0f, input.StandOff));

        static void Install(ref Ctx c, in PresentBodyInput308 input)
        {
            float s = input.Sheet.Mark.Size;
            Vector3 at = Chest(input);
            // look2: no square frame (a box on an enemy's chest reads as a target reticle) - a brushed ring with a blot in it
            Sprite(ref c, at, s * 1.15f, s * 1.15f, InkBurstMeshBuilder308.CellRing, 0);
            Sprite(ref c, at + Vector3.back * .01f, s * .6f, s * .6f, InkBurstMeshBuilder308.CellIgnite, 1);
            Needle(ref c, input.Profile, at - new Vector3(s, s, 0f) * .8f, new Vector3(1f, 1f, 0f).normalized, s * 2.2f, 1);
            Needle(ref c, input.Profile, at - new Vector3(-s, s, 0f) * .8f, new Vector3(-1f, 1f, 0f).normalized, s * 2.2f, 1);
        }

        static void Carried(ref Ctx c, in PresentBodyInput308 input)
        {
            float s = input.Sheet.Mark.Size;
            Vector3 at = Chest(input);
            Sprite(ref c, at, s * .6f, s * .6f, InkBurstMeshBuilder308.CellCluster, 0);
            Sprite(ref c, at + new Vector3(s * .35f, -s * .45f, -.01f), s * .3f, s * .3f, InkBurstMeshBuilder308.CellDrop, 1);
        }

        static void State(ref Ctx c, in PresentBodyInput308 input)
        {
            float s = input.Sheet.Mark.Size;
            Vector3 chest = Chest(input);
            var p = input.Profile;
            if ((input.States & PresentState308.Bound) != 0)
            {
                // a seal: a brushed ring and four lines laid across it like a cage (look2: not a square frame)
                Sprite(ref c, chest, s * 1.15f, s * 1.15f, InkBurstMeshBuilder308.CellRing, 0);
                for (int k = -1; k <= 1; k += 2)
                {
                    Needle(ref c, p, chest + new Vector3(-s * 1.1f, s * .45f * k, -.01f), Vector3.right, s * 2.2f, 1);
                    Needle(ref c, p, chest + new Vector3(s * .45f * k, -s * 1.1f, -.01f), Vector3.up, s * 2.2f, 1);
                }
            }
            if ((input.States & PresentState308.Slowed) != 0)
            {
                // under the feet: a puddle that clings, and dry drag lines trailing toward the eye
                Vector3 foot = new Vector3(0f, .04f, 0f);
                if (InkBurstMeshBuilder308.AddSprite(c.B, foot, Vector3.right, Vector3.forward, s * 1.5f, s * 1.5f, InkBurstMeshBuilder308.CellPuddle, 0f, c.R.Next(), c.Tint * .5f, Vector3.zero, 0f))
                { c.S.Sprites++; Born(ref c, 0); Reach(ref c, foot + Vector3.right * (s * 1.5f)); }
                for (int k = -1; k <= 1; k++)
                {
                    Vector3 axis = new Vector3(k * .45f, 0f, -1f).normalized;
                    Vector3 from = foot + new Vector3(k * s * .5f, .02f, -s * .4f);
                    if (InkBurstMeshBuilder308.AddDryStroke(c.B, from, axis, Vector3.Cross(Vector3.up, axis).normalized, s * 2.2f, s * .22f, (int)(c.R.Next() * 3.99f), 1f, c.R.Next(), c.Tint, Vector3.zero, 0f))
                    { c.S.Fine++; Born(ref c, 1); Reach(ref c, from + axis * (s * 2.2f)); }
                }
            }
            if ((input.States & PresentState308.Exposed) != 0)
            {
                // an opened cut beside the chest: two crossed lines and ink running out of it
                Vector3 at = chest + new Vector3(s * 1.1f, s * .25f, -.02f);
                Needle(ref c, p, at - new Vector3(1f, 1f, 0f).normalized * (s * .8f), new Vector3(1f, 1f, 0f).normalized, s * 1.6f, 0);
                Needle(ref c, p, at - new Vector3(-1f, 1f, 0f).normalized * (s * .8f), new Vector3(-1f, 1f, 0f).normalized, s * 1.6f, 0);
                Sprite(ref c, at + new Vector3(0f, -s * .9f, 0f), s * .28f, s * .42f, InkBurstMeshBuilder308.CellDrop, 1);
            }
            if ((input.States & PresentState308.Weakened) != 0)
            {
                // over the head: ink that sags and one line pressing down
                Vector3 at = new Vector3(0f, chest.y * 1.55f, chest.z);
                // look2: the atlas's drip cell is a fringe that fills its cell edge to edge (a hard square as a single mark) -
                // a tailed drop turned head down sags instead
                if (InkBurstMeshBuilder308.AddSprite(c.B, at, Vector3.down, Vector3.right, s * .8f, s * .45f, InkBurstMeshBuilder308.CellDropTailed, 0f, c.R.Next(), c.Tint * .5f, Vector3.zero, 0f))
                { c.S.Sprites++; Born(ref c, 0); Reach(ref c, at + Vector3.up * (s * .8f)); }
                Needle(ref c, p, at + new Vector3(0f, s * 1.3f, -.01f), Vector3.down, s * 1.1f, 1);
            }
        }

        // ---- the zone's edge (body root = the zone's centre on the ground, +Y = the ground's normal)

        // forms3 (D9): one ring stroke lying on the ground, made of 3-4 broken arcs. Every arc is one brush stroke: it is
        // drawn out of a dry tail, runs round the ring at the zone's radius and ends where the brush is lifted - a pressed head
        // whose outer edge runs on and whose inner edge curves in. All arcs run the same way round. An arc's spine is the
        // parabola through its two ends and its middle point on the ring (the builder's own bend), so it never leaves the
        // ring by more than a few centimetres; its width is laid across the ring at its middle.
        static void Ring(ref Ctx c, in PresentBodyInput308 input)
        {
            var ring = input.Sheet.Ring;
            float radius = Mathf.Max(ring.MinRadius, input.Radius);
            // forms4 (P5): the arcs follow the ring's length (a large ring of four was four long lines), uneven in length by ArcJitter
            int arcs = input.Sheet.RingArcs(input.Tier, radius);
            bool mobile = input.Tier == DeployTier308.Mobile;
            float half = input.Sheet.RingHalfWidth(radius);
            float even = Mathf.PI * 2f / arcs, start = c.R.Next() * Mathf.PI * 2f;
            int most = mobile ? ring.SegmentsMobile : ring.Segments;
            float segmentDeg = Mathf.Max(4f, mobile ? ring.SegmentDegMobile : ring.SegmentDeg) * (arcs > ring.ManyArcs ? Mathf.Clamp(ring.ManySegmentMul, 1f, 2f) : 1f);
            float from = start;
            for (int i = 0; i < arcs; i++)
            {
                float sector = even;
                // pass 4b (Q4): the arcs are of unequal length by a WRITTEN order of shares (the breaks between them differ: no even
                // dashes), and none is longer than ArcMaxLength m on a ring of any size (a large zone is short strokes with
                // wide breaks, never a few long lines)
                // (a ring so small that a sector is under ArcUnevenFrom m keeps the even share: a shorter arc there is a comma)
                float share = (sector * radius >= ring.ArcUnevenFrom ? Pick(ring.ArcShares, i, ring.ArcShare) : ring.ArcShare) * (1f + c.R.Range(-ring.ArcJitter, ring.ArcJitter));
                float sweep = Mathf.Min(Mathf.Min(sector * Mathf.Clamp(share, .3f, 1.05f), Mathf.Clamp(ring.ArcMaxDeg, 40f, 100f) * Mathf.Deg2Rad), Mathf.Max(.5f, ring.ArcMaxLength) / Mathf.Max(.01f, radius));
                // (a shortened arc stands somewhere in its sector, not always at its start: the breaks are unequal on a large ring too)
                // pass 4c (rule review F1): a CUT arc is placed in the whole of what its sector leaves free (it was placed in the
                // few centimetres between its share and the cut: on a 6 m ring every break came out the same length)
                bool cut = sweep < sector * Mathf.Clamp(share, .3f, 1.05f) - 1e-5f;
                float begin = from + (cut ? sector - sweep : 0f) * Mathf.Clamp01(Pick(ring.ArcPlaces, i, 0f));
                float mid = begin + sweep * .5f;
                int segments = Mathf.Clamp(Mathf.CeilToInt(sweep * Mathf.Rad2Deg / segmentDeg), Mathf.Min(ring.SegmentsMin, most), most);
                // the arc leans a little in or out of the ring, turn about: its middle stays on the radius within the wobble
                float r = radius + half * ring.Wobble * ((i & 1) == 0 ? 1f : -1f) * c.R.Range(Mathf.Clamp01(ring.WobbleLeast), 1f);
                Vector3 a = new Vector3(Mathf.Cos(begin), 0f, Mathf.Sin(begin)) * r, b = new Vector3(Mathf.Cos(begin + sweep), 0f, Mathf.Sin(begin + sweep)) * r;
                Vector3 outward = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
                Vector3 chord = b - a;
                float sagitta = r * (1f - Mathf.Cos(sweep * .5f));
                Vector3 run = chord + outward * (4f * sagitta);
                float length = Mathf.Max(.05f, run.magnitude);
                // pass 4c (rule review F1): an arc's width follows ITS OWN length - its wet body is ArcWidths widths long at least
                // (the width followed the radius alone: on rings of 3.5 - 5.5 m the shorter arcs of the written order were commas)
                float ringHalf = half;
                half = Mathf.Min(ringHalf, length * (1f - Mathf.Clamp(ring.TailShare, 0f, .6f)) / (2f * Mathf.Max(1f, ring.ArcWidths) * (1f + (ring.Head != null && ring.Head.Share > 0f ? Mathf.Clamp(ring.Head.Swell, 0f, .5f) : 0f))));
                var s = new InkStroke308
                {
                    Tail = a + Vector3.up * ring.Lift, Axis = run / length, Side = outward, Bend = -outward, Length = length, HalfWidth = half, Curve = 4f * sagitta / length, SCurve = 0f,
                    TailShare = Mathf.Clamp(ring.TailShare, 0f, .6f), Split = 0, Segments = segments, TailCell = (int)(c.R.Next() * 3.99f), BirthCel = Mathf.Min(1, (i & 1) * ring.BirthStep), Seed01 = c.R.Next(),
                    TintWeight = c.Tint, MeltBias = c.R.Next(), DryStart = true, DryTailU = input.Profile.Stroke.DryStartTailU, DryShare = ring.DryShare, RootWidth = ring.RootWidth, RootRamp = ring.RootRamp,
                };
                // the head's straight edge is the outer one (the ring's own line), the inner edge curves in to meet it
                InkBurstMeshBuilder308.PressHead(ref s, ring.Head, -1f);
                if (mobile && s.PressRings > 0) s.PressRings = Mathf.Min(s.PressRings, ring.HeadRingsMobile);
                if (arcs > ring.ManyArcs && s.PressRings > 0) s.PressRings = Mathf.Min(s.PressRings, ring.HeadRingsMany);
                from += sector;
                if (!InkBurstMeshBuilder308.AddStroke(c.B, s)) continue;
                c.S.Bold++; Born(ref c, (int)s.BirthCel); Reach(ref c, a); Reach(ref c, b);
                // pass 4b (Q4): seen from INSIDE, a ring on the ground is always a level band - what breaks the line is VERTICAL
                // brush. Every arc carries 5 .. 8 standing dry strands (a written order of counts), of uneven height (a written
                // order between StrandMin and StrandMax), irregularly spaced along it (a written order of gaps), leaning the way
                // the arc runs. Dry brush, born with the arc, no glow of their own. (Three strands of one height were grass tufts.)
                int strands = RingStrands(ring, mobile, arcs, i);
                float gapSum = 0f, along = 0f;
                for (int k = 0; k < strands; k++) gapSum += Pick(ring.StrandGaps, i * 3 + k, 1f);
                for (int k = 0; k < strands; k++)
                {
                    float gap = Pick(ring.StrandGaps, i * 3 + k, 1f) / Mathf.Max(.01f, gapSum);
                    // (it stands at the start of its own share of the arc, a little on: the spacing is the written order's)
                    float at = begin + sweep * Mathf.Clamp(ring.StrandAlong, .05f, 1f) * (along + gap * c.R.Range(ring.StrandPlaceMin, Mathf.Max(ring.StrandPlaceMin, ring.StrandPlaceMax)));
                    along += gap;
                    Vector3 spoke = new Vector3(Mathf.Cos(at), 0f, Mathf.Sin(at)), tangent = new Vector3(-Mathf.Sin(at), 0f, Mathf.Cos(at));
                    float lean = ring.StrandLeanDeg * Mathf.Deg2Rad * c.R.Range(Mathf.Clamp01(ring.StrandLeanLeast), 1f), turn = (k * ring.StrandTurnStep + c.R.Range(-ring.StrandTurnJitter, ring.StrandTurnJitter)) * Mathf.Deg2Rad;
                    Vector3 axis = Vector3.up * Mathf.Cos(lean) + tangent * Mathf.Sin(lean);
                    Vector3 side = tangent * Mathf.Cos(turn) + spoke * Mathf.Sin(turn);   // level: the strand's foot lies on the stroke's plane
                    // (the height is the strand's TOP over the ground, whatever it leans)
                    float tall = Mathf.Lerp(ring.StrandMin, Mathf.Max(ring.StrandMin, ring.StrandMax), Mathf.Clamp01(Pick(ring.StrandHighs, i * 5 + k, .5f))) / Mathf.Max(.5f, Mathf.Cos(lean));
                    Vector3 foot = spoke * (r + half * c.R.Range(-ring.StrandAcross, ring.StrandAcross)) + Vector3.up * ring.Lift;
                    if (InkBurstMeshBuilder308.AddDryStroke(c.B, foot, axis, side, tall, ring.StrandHalf * c.R.Range(ring.StrandWidthMin, Mathf.Max(ring.StrandWidthMin, ring.StrandWidthMax)), (i + k) % 4, s.BirthCel, c.R.Next(), c.Tint, Vector3.zero, 0f)) c.S.Fine++;
                }
                half = ringHalf;
            }
        }

        /// <summary>pass 4b (Q4): standing strands on the i-th arc of a ring of `arcs` arcs - the sheet's written order (5 .. 8), or the
        /// least (StrandsLeast) on every arc of a ring of many arcs (the body's vertex room).</summary>
        public static int RingStrands(SpellPresent308SheetSO.RingSet ring, bool mobile, int arcs, int i)
        {
            if (ring == null || ring.StrandsLeast <= 0) return 0;
            int least = Mathf.Clamp(ring.StrandsLeast, 1, 8);
            if (arcs > (mobile ? ring.ManyArcsMobile : ring.ManyArcs)) return least;
            return Mathf.Clamp((int)Pick(mobile ? ring.StrandCountsMobile : ring.StrandCounts, i, least), least, 8);
        }

        // the i-th entry of a written order, read round and round (a missing or empty order = `none`)
        static float Pick(float[] order, int i, float none) => order != null && order.Length > 0 ? order[((i % order.Length) + order.Length) % order.Length] : none;

        // ---- helpers

        static void Born(ref Ctx c, int cel) { if (cel > c.S.LastBirthCel) c.S.LastBirthCel = cel; }

        static void Reach(ref Ctx c, Vector3 at)
        {
            float distance = at.magnitude;
            if (distance > c.S.Extent) c.S.Extent = distance;
            if (distance < c.S.Nearest) c.S.Nearest = distance;
        }

        // across a stroke, in the plane the eye sees (the eye is the body's origin for the forms that use this)
        static Vector3 Side(Vector3 middle, Vector3 axis)
        {
            Vector3 side = Vector3.Cross(middle.sqrMagnitude > 1e-6f ? middle.normalized : Vector3.forward, axis);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(Vector3.forward, axis);
            return side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.up;
        }

        // a bold stroke along a circle about the eye from `from` to `to`: it leaves `from` along the circle's tangent and its
        // own bend brings it round to `to` (the same parabola the layer's cone sweeps use)
        static void AddArc(ref Ctx c, SpellDeploy308ProfileSO p, Vector3 from, Vector3 to, float halfWidth, float tailShare, int birth, float curveBias, float tipTaper = 0f, float rootRamp = 0f,
            SpellDeploy308ProfileSO.BrushHeadSet head = null, float headFlip = 1f, int segments = 0)
        {
            Vector3 chord = to - from;
            Vector3 flat = new Vector3(chord.x, 0f, chord.z);
            float flatLength = Mathf.Max(.2f, flat.magnitude);
            flat /= flatLength;
            Vector3 outward = (from + to) * .5f; outward.y = 0f;
            outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.forward;
            Vector3 spoke = new Vector3(from.x, 0f, from.z);
            float sin = Mathf.Clamp(flatLength * .5f / Mathf.Max(.01f, spoke.magnitude), 0f, .9f), cos = Mathf.Sqrt(1f - sin * sin);
            Vector3 run = (flat * cos + outward * sin) * (flatLength * cos) + Vector3.up * chord.y;
            float length = Mathf.Max(.2f, run.magnitude);
            Vector3 axis = run / length;
            var s = new InkStroke308
            {
                Tail = from, Axis = axis, Side = Side((from + to) * .5f + outward * (flatLength * .2f * sin), axis), Bend = flat * sin - outward * cos, Length = length, HalfWidth = halfWidth,
                Curve = flatLength * sin / length + curveBias, SCurve = 0f, TailShare = Mathf.Clamp(tailShare, 0f, .6f), Split = 0, Segments = segments > 0 ? segments : p.Stroke.CapsuleSegments,
                TailCell = (int)(c.R.Next() * 3.99f), BirthCel = birth, Seed01 = c.R.Next(), TintWeight = c.Tint, MeltBias = c.R.Next(),
            };
            // look2 round 2: the wet body is combed out of the dry tail (no straight cut where it starts) and ends in one point
            if (tipTaper > 0f) { s.DryStart = true; s.DryTailU = p.Stroke.DryStartTailU; s.HeadTaper = tipTaper; s.RootRamp = rootRamp; s.Split = 1; }
            InkBurstMeshBuilder308.PressHead(ref s, head, headFlip);   // forms3 (D12): replaces the point when the sheet gives a head
            if (!InkBurstMeshBuilder308.AddStroke(c.B, s)) return;
            c.S.Bold++; Born(ref c, birth); Reach(ref c, from); Reach(ref c, InkBurstMeshBuilder308.HeadTip(s));
        }

        static void Sprite(ref Ctx c, Vector3 at, float halfWidth, float halfHeight, int cell, int birth)
        {
            if (!InkBurstMeshBuilder308.AddSprite(c.B, at, Vector3.right, Vector3.up, halfWidth, halfHeight, cell, birth, c.R.Next(), c.Tint * .5f, Vector3.zero, 0f)) return;
            c.S.Sprites++; Born(ref c, birth); Reach(ref c, at + Vector3.up * halfHeight);
        }

        static void Needle(ref Ctx c, SpellDeploy308ProfileSO p, Vector3 from, Vector3 direction, float length, int birth)
        {
            if (!InkBurstMeshBuilder308.AddNeedle(c.B, from, direction, length, p.Stroke.NeedleWidth * .5f, Vector3.back, birth, c.R.Next(), Vector3.zero, 0f)) return;
            c.S.Needles++; Born(ref c, birth); Reach(ref c, from + direction * length);
        }

        static void Star(ref Ctx c, SpellDeploy308ProfileSO p, Vector3 at, int count, float minLength, float maxLength, int birth)
        {
            float phase = c.R.Next() * Mathf.PI * 2f;
            Vector3 toEye = at.sqrMagnitude > 1e-6f ? -at.normalized : Vector3.back;
            Vector3 right = Vector3.Cross(Vector3.up, toEye);
            right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;
            Vector3 up = Vector3.Cross(toEye, right).normalized;
            for (int i = 0; i < count; i++)
            {
                float a = phase + (i + c.R.Range(-.3f, .3f)) / Mathf.Max(1, count) * Mathf.PI * 2f;
                Vector3 direction = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                if (!InkBurstMeshBuilder308.AddNeedle(c.B, at + direction * (minLength * .2f), direction, c.R.Range(minLength, maxLength), p.Stroke.NeedleWidth * .5f, toEye, birth, c.R.Next(), Vector3.zero, 0f)) continue;
                c.S.Needles++; Born(ref c, birth); Reach(ref c, at + direction * maxLength);
            }
        }
    }
}
