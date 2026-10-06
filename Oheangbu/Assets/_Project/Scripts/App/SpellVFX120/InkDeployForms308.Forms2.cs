using Oheangbu.Core.Domain;
using Oheangbu.Data.Spell;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // #308 forms2 (Tools/Unity/Stage308_forms2/DESIGN.md section 4): the forms that replace the catalogue bodies.
    //   Guard  - parry: one stroke held across the lower view; wet while the parry window is open, dry afterwards
    //   Fence  - ward: posts and rails on the ward's own ring, low enough that every enemy tell stays visible over it
    //   Comet  - single attack: a head that faces the eye and a tail laid in the screen plane toward the brush tip
    //   Loose  - an install drawn without a target: the mark slides off and falls (nothing is installed, nothing is held)
    //   Emerge - summon: the rising curtain, at the summon's real place when the caller knows it
    // Pure like the rest of the class: the same input writes the same geometry. Which form a cast gets is decided by
    // InkForms2Rules308.FormOf (the map row's Retire + the profile switches), never here.
    public static partial class InkDeployForms308
    {
        /// <summary>Horizontal field of view of the 60 degree, 16:9 eye as a share of a full turn: how much of a ring is on screen.</summary>
        private const float RingViewShare = .254f;

        private static void Forms2(ref Ctx c, in DeployFormInput308 input, SpellDeploy308ProfileSO.TierSet tier, int ground, int air)
        {
            int first = c.B.VertexCount;
            Vector3 eye = input.Camera;
            // look2 (BODY IS INK): the standing forms are wide strokes that stay for seconds - their bodies take only a share
            // of the element wash (the comet is an attack stroke and keeps the attack bursts' wash)
            // round 2: one share for all of them took the five elements apart from each other - every form has its own
            // (a thin stroke keeps the attack wash, only the wide bodies are held back)
            c.Tint *= Mathf.Clamp01(input.Form == DeployForm308.Guard ? c.P.Guard.Wash : input.Form == DeployForm308.Fence ? c.P.FenceOf(input.Element).Wash
                : input.Form == DeployForm308.Emerge ? c.P.Summon.Wash : input.Form == DeployForm308.Loose ? c.P.Loose.Wash : 1f);
            switch (input.Form)
            {
                case DeployForm308.Guard:
                    Guard(ref c, input, tier);
                    break;
                case DeployForm308.Fence:
                    Ignite(ref c, input, Vector3.zero);
                    first = c.B.VertexCount;
                    Fence(ref c, input, tier);
                    eye = new Vector3(0f, input.OriginGroundY + c.P.Fence.EyeHeight, 0f);   // judged from the middle of the ring
                    break;
                case DeployForm308.Comet:
                    Ignite(ref c, input, Vector3.zero);
                    Comet(ref c, input, tier);
                    c.Bold = Mathf.Max(0, c.Bold - 1);   // the comet is the main stroke
                    SingleBurst(ref c, input, false);
                    Smear(ref c, input.Target, c.P.Residue.ImpactSmear);
                    GroundDrops(ref c, input, input.Target, .9f, ground, false);
                    AirDrops(ref c, input, input.Target, air);
                    break;
                case DeployForm308.Loose:
                    Loose(ref c, input);
                    break;
                case DeployForm308.Emerge:
                    Ignite(ref c, input, Vector3.zero);
                    first = c.B.VertexCount;
                    Emerge(ref c, input, tier, ground);
                    break;
            }
            Measure(ref c, first, eye, input.OriginGroundY);
        }

        // highest point of what was written from `first` on: against the ground and as seen from `eye`
        private static void Measure(ref Ctx c, int first, Vector3 eye, float groundY)
        {
            float top = float.NegativeInfinity, elevation = -90f;
            for (int i = first; i < c.B.VertexCount; i++)
            {
                Vector3 p = c.B.Positions[i];
                top = Mathf.Max(top, p.y - groundY);
                float flat = new Vector2(p.x - eye.x, p.z - eye.z).magnitude;
                elevation = Mathf.Max(elevation, Mathf.Atan2(p.y - eye.y, Mathf.Max(.01f, flat)) * Mathf.Rad2Deg);
            }
            c.S.TopHeight = c.B.VertexCount > first ? top : 0f;
            c.S.TopElevationDeg = c.B.VertexCount > first ? elevation : -90f;
            for (int i = 0; i < c.S.Drops && c.Drops != null && i < c.Drops.Length; i++)
            {
                if (c.Drops[i].Air) continue;
                c.S.GroundMarks++;
                if (c.Drops[i].Held) c.S.HeldDrops++;
            }
        }

        private static void FreeDrop(ref Ctx c, Vector3 local, float size, int cell, int cel, bool air, Vector3 velocity)
        {
            if (c.Drops == null || c.S.Drops >= c.Drops.Length) return;
            c.Drops[c.S.Drops++] = new DeployDrop308 { Local = local, Size = size, Cell = cell, Cel = cel, Air = air, Velocity = velocity, Free = true };
        }

        // ---- guard stroke. Local frame (the runtime holds the root in front of the eye): +X right, +Y up, the eye at -Z.
        // look2: ONE bold brush stroke standing in the world - it crosses the view at an angle (its tail end nearer the eye),
        // arched, with a dry-brush tail and a pointed end. No braces and no needles by default (a straight band with spikes
        // and legs read as a plank / an HP bar). After the window it is a split dry brush, never a straight bar.

        private static InkStroke308 Bar(ref Ctx c, Vector3 from, Vector3 to, float arch, float wave, float half, int split, int segments, float tailShare, float birth)
        {
            Vector3 chord = to - from;
            float span = Mathf.Max(.05f, chord.magnitude);
            Vector3 flat = chord / span;
            Vector3 lift = Vector3.up - flat * Vector3.Dot(Vector3.up, flat);   // across the stroke, upward (the stroke may run into the depth)
            lift = lift.sqrMagnitude > 1e-6f ? lift.normalized : Vector3.right;
            // an arch: leave upward and bend back down, so that the middle stands `arch` above the chord. The bend is taken
            // along the chord's own normal (not the leaving direction's): spine(u) = from + chord u + lift 4 arch u (1 - u),
            // which ends exactly on `to`.
            Vector3 run = flat * span + lift * (4f * arch);
            float length = run.magnitude;
            Vector3 axis = run / length;
            // the band is turned about its own axis to face the eye
            Vector3 side = Vector3.Cross(c.Camera - (from + to) * .5f, axis);
            side = side.sqrMagnitude > 1e-6f ? side.normalized : lift;
            return new InkStroke308
            {
                Tail = from, Axis = axis, Side = side, Bend = lift, Length = length, HalfWidth = half, Curve = -4f * arch / length, SCurve = wave,
                TailShare = tailShare, Split = split, Segments = segments, TailCell = (int)(c.R.Next() * 3.99f), BirthCel = birth, Seed01 = c.R.Next(),
                TintWeight = c.Tint, MeltBias = c.R.Next(),
            };
        }

        private static void Guard(ref Ctx c, in DeployFormInput308 input, SpellDeploy308ProfileSO.TierSet tier)
        {
            var g = c.P.Guard;
            var ge = c.P.GuardOf(input.Element);
            float span = g.Span * ge.SpanMul, half = g.BarHalfWidth * ge.WidthMul;
            int lines = Mathf.Clamp(ge.Lines, 1, 2);
            float distance = Mathf.Max(.3f, input.Camera.magnitude);
            float share = (lines * span * 2f * half + (g.Braces ? 2f * g.TickLength * 2f * half : 0f)) / (2.37f * distance * distance);
            if (share > g.ScreenShareMax && share > 0f) { half *= g.ScreenShareMax / share; share = g.ScreenShareMax; }
            c.S.ScreenShare = share;
            float gap = half * g.LineGap, rise = ge.Rise * span, arch = ge.Rise > 0f ? g.Sag * g.RiseArchShare : g.Sag;
            int birth = Mathf.Max(0, input.PhaseCel);
            // the tail end (left) stands nearer the eye than the head end: a stroke drawn across the space ahead
            float nearShare = Mathf.Clamp01(g.DepthTailShare);
            Vector3 left = new Vector3(-span * .5f, 0f, -g.Depth * nearShare), right = new Vector3(span * .5f, rise, g.Depth * (1f - nearShare));
            Vector3 chord = right - left;
            Vector3 flat = chord.normalized;
            Vector3 lift = (Vector3.up - flat * Vector3.Dot(Vector3.up, flat)).normalized;
            int tip = ge.Split > 0 ? ge.Split : 1;   // one prong = a pointed end

            switch (input.GuardPhase)
            {
                case GuardPhase308.Window:
                {
                    for (int k = 0; k < lines; k++)
                    {
                        Vector3 offset = lift * ((k - (lines - 1) * .5f) * gap);
                        var bar = Bar(ref c, left + offset, right + offset, arch, ge.Wave, half, tip, g.Segments, g.WetTailShare, birth);
                        // round 2: every element ends the same way (the element shows in the wash, not in a cut-out end),
                        // and the wet body is combed out of the dry tail
                        bar.DryStart = true; bar.DryTailU = c.P.Stroke.DryStartTailU; bar.HeadTaper = g.TipTaper; bar.RootRamp = g.RootRamp;
                        // forms3 (D12): the end is the brush's PRESSED head - blunt, its upper edge running on straight and
                        // the lower one curving up into it. (A point here and the thin tail there made a leaf.) An element
                        // may close longer and smaller (metal: sharper), never to a point.
                        InkBurstMeshBuilder308.PressHead(ref bar, g.Head);
                        if (bar.Press > 0f) { bar.Press = Mathf.Clamp(bar.Press * ge.HeadShareMul, .02f, 1f); bar.PressEnd = Mathf.Clamp(bar.PressEnd * ge.HeadEndMul, .05f, 1f); }
                        if (InkBurstMeshBuilder308.AddStroke(c.B, bar)) { c.S.Bold++; Born(ref c, birth); }
                    }
                    // the braces at both ends: short strokes turned down (off by default)
                    for (int k = -1; g.Braces && k <= 1; k += 2)
                    {
                        Vector3 top = k < 0 ? left : right;
                        var tick = Bar(ref c, top, top + Vector3.down * g.TickLength, 0f, 0f, half * .8f, 0, 3, 0f, birth + 1);
                        if (InkBurstMeshBuilder308.AddStroke(c.B, tick)) { c.S.Bold++; Born(ref c, birth + 1); }
                    }
                    // needles stand on the stroke only while the window is open; they never rise over the elevation limit
                    int needles = Mathf.RoundToInt(tier.GuardNeedles * ge.NeedleMul);
                    float barTop = (lines - 1) * .5f * gap + half;
                    for (int i = 0; i < needles; i++)
                    {
                        float u = (i + .5f) / needles;
                        Vector3 at = left + chord * u + lift * (4f * arch * u * (1f - u) + barTop);
                        float room = InkForms2Rules308.GuardNeedleRoom(c.P, at.y, new Vector2(at.x - input.Camera.x, at.z - input.Camera.z).magnitude);
                        float length = Mathf.Min(c.R.Range(g.NeedleMin, Mathf.Max(g.NeedleMin, g.NeedleMax)), room);
                        if (length < .01f) continue;
                        if (InkBurstMeshBuilder308.AddNeedle(c.B, at - lift * (half * .5f), Vector3.up, length + half * .5f, g.NeedleHalfWidth, Vector3.back, birth + 1, c.R.Next(), Vector3.zero, 0f))
                            c.S.Needles++;
                    }
                    // drops hanging under the stroke: a tailed drop turned head down (the atlas's drip cell is a fringe that
                    // fills its cell edge to edge - used as a single drop it printed a hard square)
                    for (int i = 0; i < ge.HangDrops; i++)
                    {
                        float u = (i + 1f) / (ge.HangDrops + 1f), run = c.P.Fence.HangDropLength * c.R.Range(.8f, 1.2f);
                        Vector3 at = left + chord * u + lift * (4f * arch * u * (1f - u)) + Vector3.down * (half * .6f + run * .5f);
                        if (InkBurstMeshBuilder308.AddSprite(c.B, at, Vector3.down, Vector3.right, run * .5f, c.P.Fence.HangDropHalfWidth, InkBurstMeshBuilder308.CellDropTailed, birth + 1, c.R.Next(), c.Tint * .5f, Vector3.zero, 0f))
                            c.S.Sprites++;
                    }
                    if (ge.DryTailOut)
                    {
                        // the dry end flicks out and down (30 deg under the stroke's own line): upward the elevation limit leaves it no length
                        Vector3 out30 = (flat * .866f - lift * .5f).normalized;
                        Vector3 flickSide = Vector3.Cross(c.Camera - right, out30);
                        flickSide = flickSide.sqrMagnitude > 1e-6f ? flickSide.normalized : lift;
                        if (InkBurstMeshBuilder308.AddDryStroke(c.B, right - out30 * half, out30, flickSide, g.TickLength * 1.5f, half * .6f, 1, birth + 1, c.R.Next(), c.Tint, Vector3.zero, 0f))
                            c.S.Fine++;
                    }
                    break;
                }
                case GuardPhase308.Dry:
                {
                    // the window has closed: the stroke is what a dry, split brush leaves - thin strands along the same arch, each
                    // mostly dry tail with a small pointed head, uneven in where they start and stop. Born as ink (no glow here).
                    // round 2: ONE dry-brush stroke along the wet stroke's own line - mostly flying white, the ink only toward
                    // its pointed end - and the hairs of the split brush, which leave the SAME root and part from it downward,
                    // each shorter. (Strands laid side by side were two floating parallel bars.) An element drawn as two lines
                    // (metal) dries as one stroke too.
                    int strands = Mathf.Clamp(g.DryStrands, 1, 3);
                    float thin = half * (lines > 1 ? 1f : g.DryWidthMul) * Mathf.Max(.2f, ge.DryWidthMul);   // fix pass: a thin element's dry stroke may be wider (metal was a faint hair)
                    for (int s = 0; s < strands; s++)
                    {
                        float part = s == 0 ? 1f : g.DryHairShare / s, angle = g.DrySplitDeg * Mathf.Deg2Rad * s;
                        // forms3 (D12): a hair leaves the main strand ON its line, part of the way along it (the brush splits as
                        // it is drawn) - it does not start beside it at the root
                        float leave = s == 0 ? 0f : Mathf.Clamp(g.DryHairFrom, 0f, .8f);
                        part *= 1f - leave;
                        Vector3 root = left + chord * leave + lift * (4f * arch * leave * (1f - leave));
                        // downward only: nothing rises over the wet stroke's line
                        Vector3 to = root + chord * part - lift * (chord.magnitude * part * Mathf.Tan(angle));
                        var strand = Bar(ref c, root, to, arch * part * part, s == 0 ? ge.Wave * g.DryWaveShare : 0f, thin * (s == 0 ? 1f : g.DryHairWidth), 1, s == 0 ? g.DrySegments : g.DryHairSegments, g.DryTailShare, birth);
                        strand.TailCell = s % 4; strand.DryStart = true; strand.DryTailU = c.P.Stroke.DryStartTailU; strand.DryShare = g.DryShare; strand.HeadTaper = g.DryTaper; strand.RootRamp = g.RootRamp;
                        if (InkBurstMeshBuilder308.AddStroke(c.B, strand)) { c.S.Fine++; Born(ref c, birth); }
                    }
                    if (g.Braces && tier.GuardDryTicks)
                        for (int k = -1; k <= 1; k += 2)
                        {
                            Vector3 top = k < 0 ? left : right;
                            if (InkBurstMeshBuilder308.AddDryStroke(c.B, top, Vector3.down, Vector3.right, g.TickLength, thin * .8f, 2, birth, c.R.Next(), c.Tint, Vector3.zero, 0f)) c.S.Fine++;
                        }
                    // the end of the guard: tailed drops fall from the stroke when it starts to melt
                    int endCel = Mathf.Max(birth, input.GuardEndCel - Mathf.Max(1, input.MeltCels));
                    for (int i = 0; i < g.EndDrops; i++)
                        FreeDrop(ref c, Vector3.Lerp(left, right, (i + .5f) / g.EndDrops), c.P.Residue.DropMid * g.EndDropSizeMul, InkBurstMeshBuilder308.CellDropTailed, endCel, true, Vector3.zero);
                    break;
                }
                default:
                {
                    // a parry consumed the guard: the stroke is cut in two and the halves part along its own line (the runtime
                    // drives the advance). Each half keeps the arch; the cut ends are the dry ones.
                    // forms3 (D12): the pieces keep only a share of the stroke's depth - both stand nearly as far from the eye,
                    // so both are seen (the far one was a speck beside a near one as big as a leaf)
                    float flatten = Mathf.Clamp01(g.BreakDepthShare);
                    Vector3 cutLeft = new Vector3(left.x, left.y, left.z * flatten), cutRight = new Vector3(right.x, right.y, right.z * flatten);
                    Vector3 cutFlat = (cutRight - cutLeft).normalized;
                    Vector3 cutLift = (Vector3.up - cutFlat * Vector3.Dot(Vector3.up, cutFlat)).normalized;
                    Vector3 middle = (cutLeft + cutRight) * .5f + cutLift * arch;
                    for (int k = -1; k <= 1; k += 2)
                    {
                        Vector3 shift = cutFlat * (k * g.BreakSpread) - cutLift * (g.BreakSpread * g.BreakDrop);   // they part and drop: the far half never rises in the view
                        // round 2: each piece is a short dry-brush stroke - frayed where it was cut; forms3: a pressed head at its far end
                        var part = Bar(ref c, middle + shift, (k < 0 ? cutLeft : cutRight) + shift, arch * g.BreakArchShare, 0f, half * g.BreakWidthMul, 1, g.BreakSegments, g.BreakTailShare, birth);
                        part.DryStart = true; part.DryTailU = c.P.Stroke.DryStartTailU; part.DryShare = g.BreakDryShare; part.HeadTaper = g.BreakTaper; part.RootRamp = g.RootRamp;
                        InkBurstMeshBuilder308.PressHead(ref part, g.BreakHead, k < 0 ? -1f : 1f);   // the upper edge stays straight on both
                        part.Advance = shift;
                        if (InkBurstMeshBuilder308.AddStroke(c.B, part)) { c.S.Bold++; Born(ref c, birth); }
                    }
                    break;
                }
            }
        }

        // ---- fence. Local frame: the ward's centre on the ground, +Z = where the caster looked.

        private static int RingCel(ref Ctx c, float degreesFromFront) =>
            c.Impact + Mathf.Min(c.BurstCels - 1, Mathf.FloorToInt(Mathf.Clamp01(Mathf.Abs(degreesFromFront) / 180f) * (c.BurstCels - 1) + .5f));

        private static void Fence(ref Ctx c, in DeployFormInput308 input, SpellDeploy308ProfileSO.TierSet tier)
        {
            var f = c.P.Fence;
            var fe = c.P.FenceOf(input.Element);
            float radius = input.WardRadius > 0f ? input.WardRadius : 3f, y0 = input.OriginGroundY, area = 0f;
            Vector3 centre = new Vector3(0f, y0, 0f);

            int posts = Mathf.RoundToInt(tier.FencePosts * fe.PostMul);
            float postHalf = f.PostHalfWidth * c.E.WidthMul;
            for (int i = 0; i < posts; i++)
            {
                // the post straight ahead first, then round both sides
                float degrees = (i + .5f) / posts * 360f; if (degrees > 180f) degrees -= 360f;
                float a = degrees * Mathf.Deg2Rad;
                Vector3 outward = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)), tangent = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));
                float height = f.PostHeight * Mathf.Lerp(Mathf.Clamp(fe.MinHeightShare, .3f, 1f), 1f, c.R.Next());
                Vector3 axis = (Vector3.up + tangent * (Mathf.Tan(fe.LeanDeg * Mathf.Deg2Rad) * (i % 2 == 0 ? 1f : -1f))).normalized;
                // the forked tip of a wooden post reaches a little past the stroke's length: the top never passes PostHeight
                float length = Mathf.Max(.1f, (height - postHalf * .5f) / Mathf.Max(.5f, axis.y));
                Vector3 foot = centre + outward * radius;
                var s = Stroke(ref c, foot + axis * length, axis, tangent, length, postHalf, RingCel(ref c, degrees));
                s.Curve = Mathf.Min(s.Curve, .12f); s.Bend = tangent; s.Tail = foot;
                s.Segments = posts > 14 ? 4 : 6; s.TailShare = Mathf.Min(s.TailShare, .2f);
                if (fe.Tongues > 0) { s.Split = 1; s.Segments = 4; }   // a flame fence's wet strokes end in a point (a blunt one read as a stake)
                // fix pass: a post ends where the brush was pressed - blunt and lopsided, turn about (a wooden post's forked tip was a row of W-shaped teeth)
                InkBurstMeshBuilder308.PressHead(ref s, f.PostHead, i % 2 == 0 ? 1f : -1f);
                if (!InkBurstMeshBuilder308.AddStroke(c.B, s)) continue;
                c.S.Bold++; c.S.Posts++; Born(ref c, s.BirthCel);
                area += 2f * postHalf * length;
            }

            int rails = fe.Rail && fe.RailSpindles <= 0 ? tier.FenceRails * (fe.RailLow ? 2 : 1) : 0;   // a low rail is laid in shorter arcs: a long parabola leaves the ring
            float railHalf = f.RailHalfWidth * c.E.WidthMul * Mathf.Max(.2f, fe.RailWidthMul);
            float railY = fe.RailAtTop ? f.PostHeight - railHalf * 2f : fe.RailLow ? f.LowRailHeight : f.RailHeight;
            for (int i = 0; i < rails; i++)
            {
                float mid = (i + .5f) / rails * 360f; if (mid > 180f) mid -= 360f;
                // a low rail is the one stroke that joins the fence: its arcs meet (each next one starts under the last one's head)
                float halfArc = 180f / rails - (fe.RailLow ? -f.RailOverlapDeg : 3f);
                Vector3 from = OnRing(centre, mid - halfArc, radius) + Vector3.up * railY;
                Vector3 to = OnRing(centre, mid + halfArc, radius) + Vector3.up * railY;
                var s = RingArc(ref c, centre + Vector3.up * railY, from, to, radius, railHalf, RingCel(ref c, mid));
                // round 2: each arc of a low rail is a spindle (combed start, narrowing to a point) lying over the next one:
                // one line whose thickness swells and thins, with no seam and no cut end
                if (fe.RailLow) { s.TailShare = 0f; s.Segments = 4; s.RootWidth = f.SpindleRoot; s.RootRamp = f.SpindleRamp; s.HeadTaper = f.SpindleTaper; s.Split = 1; }
                if (!InkBurstMeshBuilder308.AddStroke(c.B, s)) continue;
                c.S.Bold++; c.S.Rails++; Born(ref c, s.BirthCel);
                area += 2f * railHalf * s.Length;
            }

            // forms3 (D8, wood): the rails are bending brush strokes tied from post to post - each starts thin (drawn out of
            // the last one's head, under which it lies), swells as it bows and ends in a pressed head at the next post.
            // They bow up and down in turn, and the ring closes. (The old arcs left a gap between them and read as ruled
            // planks; the dry chords under them floated; a stroke narrow at both ends is a leaf.)
            int spindles = fe.Rail ? Mathf.Max(0, fe.RailSpindles) : 0;
            int spindleRows = spindles > 0 ? (tier.Fine ? Mathf.Clamp(fe.SpindleRows, 1, 2) : 1) : 0;
            for (int row = 0; row < spindleRows; row++)
                for (int i = 0; i < spindles; i++)
                {
                    // it starts SpindleRailOverlapDeg before its share of the ring (under the last head) and ends on its end
                    float mid = (i + row * .5f) / spindles * 360f - f.SpindleRailOverlapDeg * .5f; if (mid > 180f) mid -= 360f;
                    float half = railHalf * (row == 0 ? 1f : f.SpindleLowWidthMul);
                    float sag = f.SpindleSag * Mathf.Clamp(fe.SpindleSagMul, 0f, 2f);
                    var s = RingBow(ref c, centre, mid, 180f / spindles + f.SpindleRailOverlapDeg * .5f, radius, row == 0 ? (fe.RailAtTop ? railY - sag : railY) : f.LowRailHeight,
                        sag * ((i + row) % 2 == 0 ? 1f : -1f), half, RingCel(ref c, mid));
                    s.Segments = !tier.Fine ? f.SpindleSegmentsCoarse : row == 0 ? f.SpindleSegments : f.SpindleLowSegments; s.RootWidth = f.SpindleRoot; s.RootRamp = f.SpindleRamp;
                    InkBurstMeshBuilder308.PressHead(ref s, f.SpindleHead, (i + row) % 2 == 0 ? 1f : -1f);
                    if (s.Press <= 0f) { s.HeadTaper = f.SpindleTaper; s.Split = 1; }
                    // forms4 (P9): the element's own strong and weak along the rail (metal: thin where it sets off, swelling into the pressed end)
                    if (fe.RailRoot > 0f) s.RootWidth = fe.RailRoot;
                    if (fe.RailSwell > 0f && s.Press > 0f) { s.PressSwell = fe.RailSwell; s.PressSwellSpan = Mathf.Clamp(fe.RailSwellSpan, .1f, 1f); }
                    if (!InkBurstMeshBuilder308.AddStroke(c.B, s)) continue;
                    c.S.Bold++; c.S.Rails++; Born(ref c, s.BirthCel);
                    area += 2f * half * s.Length;
                }

            // pass 4b (main decision Q3, fire): RING COHESION FIRST. (1) A low ink band on the ground closes the ring: a chain of
            // smeared dabs of unequal length, each starting thin and combed dry, swelling to where the brush was pressed (its
            // height changes all along - no rail of one width), broken by short gaps of unequal length that a dry smear bridges.
            // No dab stands over FlameBaseTop. (2) Flame tongues grow out of it in PAIRS and THREES of unequal height: the
            // tongues of a group lean into each other and CROSS (a tall one curls over the low one that leans the other way
            // under it). A tongue is one brush flick pressed at the ground and drawn up - belly low, bending, its upper part dry
            // hairs - and a short dry lick leaves its body beside the tip: the tip is split, there is no single point.
            // (History: even upright masses = haystacks; pairs leaning as A = teeth; all leaning one way with gaps between the
            // bundles = fourteen standing stones, the ring fell apart.)
            if (fe.Tongues > 0)
            {
                float around = 2f * Mathf.PI * radius;
                // ---- the groups: two and three tongues (a written order), unevenly apart
                int perRail = tier.Fine ? fe.Tongues : Mathf.Max(1, Mathf.CeilToInt(fe.Tongues * f.TongueCoarseShare));
                int masses = Mathf.Max(1, tier.FenceRails) * perRail;
                int groups = 0, left = masses; float tightSum = 0f, apartSum = 0f;
                while (left > 0)
                {
                    int size = Mathf.Clamp((int)Pick(tier.Fine ? f.FlameGroups : f.FlameGroupsCoarse, groups, 2f), 2, 3);
                    if (left - size == 1) size = left;       // nobody stands alone: the last group takes the odd one
                    size = Mathf.Min(size, left);
                    tightSum += (size - 1) * f.FlameGroupTight; apartSum += Pick(f.FlameGroupApart, groups, 1f);
                    left -= size; groups++;
                }
                float free = Mathf.Max(0f, around - tightSum);
                float place = c.R.Next() * around;           // metres round the ring
                int m = 0; left = masses;
                for (int g = 0; g < groups; g++)
                {
                    int size = Mathf.Clamp((int)Pick(tier.Fine ? f.FlameGroups : f.FlameGroupsCoarse, g, 2f), 2, 3);
                    if (left - size == 1) size = left;
                    size = Mathf.Min(size, left);
                    bool turned = (g / 2) % 2 == 1;          // every other group OF ITS KIND is the mirror (the kinds alternate)
                    float span = (size - 1) * f.FlameGroupTight, gapAfter = free * Pick(f.FlameGroupApart, g, 1f) / Mathf.Max(.01f, apartSum);

                    // ---- (1) the base band between this group and the next. pass 4c (director 4b: the band was ONE line bent in three
                    // or four straight pieces - a rope with knots). It is short DABS now: each starts low, thin and combed dry and is
                    // pressed tall and blunt (its height changes all along), no dab longer than FlameDabMax, its two ends stand in
                    // and out of the ring turn about (within the fence's 6 cm), neighbours' thin ends lie over each other. A written
                    // order says whether a stretch has ONE dab standing free in its middle (pressed end toward this group or the
                    // next, turn about) or TWO, each pressed in under its own group and drawn toward the middle.
                    {
                        float from = place + span, to = place + span + gapAfter, reachIn = f.FlameBedReach;   // (metres round the ring: the last foot of this group, the first of the next)
                        float under = Mathf.Min(reachIn, f.FlameGroupTight * .5f);       // how far a pressed end lies in under a group
                        int dabs = Mathf.Clamp((int)Pick(tier.Fine ? f.FlameDabs : f.FlameDabsCoarse, g, 1f), 1, 2);
                        float most = Mathf.Max(.2f, tier.Fine ? f.FlameDabMax : f.FlameDabMaxCoarse), holeFrom = from, holeTo = to; bool near = true;
                        for (int d = 0; d < dabs; d++)
                        {
                            float length = Mathf.Min(most * Mathf.Clamp(Pick(f.FlameBaseRuns, g * 2 + d, 1f), .4f, 1f), to - from + under);
                            float thin, pressed;
                            if (dabs == 1)
                            {
                                // its pressed end lies FlameDabClear from a group (negative: in UNDER the group's last foot - the bed the
                                // tongues stand in; a dab standing free between two groups read as a stick laid on the ground) and it is
                                // drawn away from that group, thinning: the hole it leaves before the other group is the one a dry
                                // smear bridges. Every dab trails the same way round the ring (FlameDabTurnAbout: turn about).
                                near = !f.FlameDabTurnAbout || g % 2 == 0;
                                float clear = Mathf.Min(tier.Fine ? f.FlameDabClear : f.FlameDabClearCoarse, Mathf.Max(0f, (to - from - length) * .5f));
                                pressed = near ? from + clear : to - clear; thin = near ? pressed + length : pressed - length;
                                if (near) { holeFrom = thin; holeTo = to; } else { holeFrom = from; holeTo = thin; }
                            }
                            else
                            {
                                bool back = d == 0;
                                pressed = back ? from - under : to + under; thin = back ? pressed + length : pressed - length;
                                if (back) holeFrom = thin; else holeTo = thin;
                            }
                            float high = Mathf.Lerp(f.FlameBaseHalfMin, f.FlameBaseHalf, Mathf.Clamp01(Pick(f.FlameBaseHighs, g * 2 + d, 1f)));
                            int born = RingCel(ref c, Mathf.DeltaAngle(0f, (thin + pressed) * .5f / around * 360f));
                            var s = RingSmear(ref c, centre, thin / around * 360f, pressed / around * 360f, radius, high, f.FlameBaseOff * ((g + d) % 2 == 0 ? 1f : -1f), f, born, tier.Fine);
                            s.TailCell = (g * 3 + d) % 4;
                            if (InkBurstMeshBuilder308.AddStroke(c.B, s)) { c.S.Bold++; c.S.Rails++; Born(ref c, born); area += high * 1.4f * s.Length; }
                        }
                        // a hole the dabs leave: a dry smear over it when it is wider than FlameBridgeLeast (its solid end in the dab before it)
                        float wide = holeTo - holeFrom, lap = f.FlameBridgeLap;
                        if (wide > f.FlameBridgeLeast && (tier.Fine || f.FlameBridgeCoarse))
                        {
                            // (a chord that straddles the ring: its ends as far outside as its middle is inside; its hairs run on from the dab's thin end)
                            float straddle = radius * (1f - Mathf.Cos((wide + 2f * lap) * .5f / radius)) * .5f;
                            Vector3 p0 = OnRing(centre, (holeFrom - lap) / around * 360f, radius + straddle) + Vector3.up * f.FlameBridgeHalf, p1 = OnRing(centre, (holeTo + lap) / around * 360f, radius + straddle) + Vector3.up * f.FlameBridgeHalf;
                            Vector3 across = p1 - p0; float reachOver = across.magnitude;
                            int bridgeBorn = RingCel(ref c, Mathf.DeltaAngle(0f, (holeFrom + wide * .5f) / around * 360f));
                            bool hairsOn = near;
                            if (reachOver > .05f && InkBurstMeshBuilder308.AddDryStroke(c.B, hairsOn ? p0 : p1, (hairsOn ? across : -across) / reachOver, Vector3.up, reachOver, f.FlameBridgeHalf * c.R.Range(f.FlameBridgeThin, 1f), (g + 1) % 4, bridgeBorn, c.R.Next(), c.Tint, Vector3.zero, 0f))
                            { c.S.Fine++; Born(ref c, bridgeBorn); area += f.FlameBridgeHalf * reachOver; }
                        }
                        // small dry flickers stand in the band between the groups (a written order of places and heights, leaning
                        // turn about): the band is a low fire, not a line on the ground
                        int flickers = Mathf.Max(0, tier.Fine ? f.FlameFlickers : f.FlameFlickersCoarse);
                        for (int k = 0; k < flickers; k++)
                        {
                            int n = g * flickers + k;
                            float where = Mathf.Lerp(from + f.FlameBaseLeave, to - reachIn, Mathf.Clamp01((k + Pick(f.FlameFlickerAt, n, .5f)) / flickers));
                            float deg = Mathf.DeltaAngle(0f, where / around * 360f), b2 = deg * Mathf.Deg2Rad;
                            Vector3 along = new Vector3(Mathf.Cos(b2), 0f, -Mathf.Sin(b2)), away = new Vector3(Mathf.Sin(b2), 0f, Mathf.Cos(b2));
                            float tilt = f.FlameFlickerLeanDeg * Mathf.Deg2Rad * (n % 2 == 0 ? 1f : -1f) * c.R.Range(f.FlameFlickerLeanLeast, 1f);
                            Vector3 axis = Vector3.up * Mathf.Cos(tilt) + along * Mathf.Sin(tilt);
                            float tallF = Mathf.Lerp(f.FlameFlickerMin, f.FlameFlickerMax, Mathf.Clamp01(Pick(f.FlameFlickerHighs, n, .5f)));
                            int born = RingCel(ref c, deg);
                            if (InkBurstMeshBuilder308.AddDryStroke(c.B, OnRing(centre, deg, radius) + Vector3.up * .01f, axis, Vector3.Cross(axis, away).normalized, tallF, f.FlameFlickerHalf * c.R.Range(f.FlameFlickerThin, f.FlameFlickerWide), n % 4, born, c.R.Next(), c.Tint, Vector3.zero, 0f))
                            { c.S.Fine++; Born(ref c, born); area += f.FlameFlickerHalf * tallF; }
                        }
                    }

                    // ---- (2) the tongues of the group
                    for (int k = 0; k < size; k++, m++)
                    {
                        float degrees = Mathf.DeltaAngle(0f, (place + k * f.FlameGroupTight + (k == 0 ? 0f : c.R.Range(-1f, 1f) * f.FlameJitter * f.FlameGroupTight)) / around * 360f);
                        int born = RingCel(ref c, degrees);
                        float a = degrees * Mathf.Deg2Rad;
                        Vector3 tangent = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a)), outward = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                        // heights by the group's written order (a pair: tall + low; three: mid, tall, low), mirrored in every other group
                        int slot = turned ? size - 1 - k : k;
                        float share = Mathf.Clamp01(size == 2 ? Pick(f.FlamePairHeights, slot, 1f) : Pick(f.FlameTrioHeights, slot, 1f));
                        float tall = Mathf.Lerp(f.TongueMin, Mathf.Max(f.TongueMin, f.TongueMax), share) * c.R.Range(1f - f.FlameTallWobble, 1f);
                        // it leans INTO its group: the first toward the last and the last toward the first (they cross); the tall one
                        // in the middle of three leans toward its mid-high neighbour, which leans back under its tip - those two cross
                        float way = k == 0 ? 1f : k == size - 1 ? -1f : (turned ? 1f : -1f);
                        float lean = f.TongueLeanDeg * Mathf.Deg2Rad * way * c.R.Range(f.FlameLeanMin, 1f) * (size == 3 && k == 1 ? f.FlameMidLean : 1f);
                        Vector3 up = Vector3.up * Mathf.Cos(lean) + tangent * Mathf.Sin(lean);
                        float halfWide = Mathf.Clamp(f.TongueHalfWidth * Mathf.Lerp(f.FlameLowWidth, 1f, share), .04f, f.FlameBaseMax) * c.R.Range(f.FlameWidthMin, Mathf.Max(f.FlameWidthMin, f.FlameWidthMax));
                        // a tall tongue curls more (its tip goes over the low one beside it)
                        float curl = f.FlameCurve * c.R.Range(f.FlameCurlMin, 1f) * Mathf.Lerp(f.FlameLowCurl, 1f, share);
                        Vector3 basePoint = OnRing(centre, degrees, radius) + Vector3.down * f.FlameSink;
                        // the top never passes PostHeight (the tail's hairs are as wide as the body's start)
                        float tipHalf = halfWide * f.FlameTipWidth * InkBurstMeshBuilder308.TailSpread;
                        float reach = Mathf.Max(halfWide * 2.5f, Mathf.Min(tall, (f.PostHeight - tipHalf * Mathf.Abs(Mathf.Sin(lean)) - f.FlameTopMargin) / Mathf.Max(.5f, up.y)) + f.FlameSink);
                        // a tongue stays on the ward's ring: its tip stands no farther than FlameRingSlack along the ring from its base
                        curl = Mathf.Clamp(curl, 0f, Mathf.Max(0f, (f.FlameRingSlack - tipHalf) / reach - Mathf.Abs(Mathf.Sin(lean))));
                        // drawn from the top down (the builder's tail = the hairs at the top, its head = the pressed base): the tip
                        // curls the way the tongue leans - sideways by curl x (height above the base)^2
                        Vector3 run = -up * reach - tangent * (2f * way * curl * reach);
                        float length = run.magnitude;
                        var lick = new InkStroke308
                        {
                            Tail = basePoint + up * reach + tangent * (way * curl * reach), Axis = run / length, Side = Vector3.Cross(up, outward).normalized, Bend = tangent * way, Length = length, HalfWidth = halfWide, Curve = curl * reach / length, SCurve = f.FlameWave,
                            TailShare = f.FlameTailShare, Split = 0, Segments = share >= f.FlameTallFrom ? (tier.Fine ? f.FlameSegments : f.FlameSegmentsCoarse) : tier.Fine ? f.FlameSegmentsLow : f.FlameSegmentsLowCoarse, TailCell = (m * 3 + 1) % 4, BirthCel = born, Seed01 = c.R.Next(), TintWeight = c.Tint, MeltBias = c.R.Next(),
                            DryStart = true, DryShare = f.FlameDryShare, RootWidth = f.FlameTipWidth, RootRamp = f.FlameRamp,
                        };
                        lick.DryTailU = c.P.Stroke.DryStartTailU;
                        InkBurstMeshBuilder308.PressHead(ref lick, f.FlameBase);
                        if (InkBurstMeshBuilder308.AddStroke(c.B, lick)) { c.S.Bold++; c.S.Rails++; Born(ref c, born); area += halfWide * (1f + f.FlameTipWidth) * reach; }
                        // the split tip: a short dry lick leaves the tongue's body FlameSideFrom of the way up, on the side it leans
                        // away from, fanned a little - its hairs end beside the tongue's own
                        if (f.FlameSideLicks > 0)
                        {
                            // (it sets off the way the tongue runs where it leaves it - lean and curl - and parts from it by FlameFanDeg)
                            float sideWay = -way, fan = lean + way * Mathf.Atan(2f * curl * f.FlameSideFrom) + sideWay * f.FlameFanDeg * Mathf.Deg2Rad;
                            Vector3 axis = Vector3.up * Mathf.Cos(fan) + tangent * Mathf.Sin(fan);
                            float sideHalf = halfWide * f.FlameSideWidth;
                            Vector3 root = basePoint + up * (reach * f.FlameSideFrom) + tangent * (way * curl * reach * f.FlameSideFrom * f.FlameSideFrom + sideWay * halfWide * f.FlameRootSpread);
                            float rootHigh = root.y - y0, rootAlong = Mathf.Abs(Vector3.Dot(root - basePoint, tangent));
                            float sideLength = Mathf.Min(tall * c.R.Range(f.FlameSideMin, Mathf.Max(f.FlameSideMin, f.FlameSideMax)), (f.PostHeight - rootHigh - sideHalf * Mathf.Abs(Mathf.Sin(fan)) - f.FlameTopMargin) / Mathf.Max(.5f, axis.y));
                            sideLength = Mathf.Min(sideLength, Mathf.Max(.05f, f.FlameRingSlack - rootAlong - sideHalf) / Mathf.Max(.05f, Mathf.Abs(Mathf.Sin(fan))));
                            if (sideLength >= sideHalf && InkBurstMeshBuilder308.AddDryStroke(c.B, root, axis, Vector3.Cross(axis, outward).normalized, sideLength, sideHalf, (m + 1) % 4, born, c.R.Next(), c.Tint, Vector3.zero, 0f))
                            { c.S.Fine++; Born(ref c, born); area += sideHalf * sideLength; }
                        }
                    }
                    place += span + gapAfter;
                    left -= size;
                }
            }

            if (fe.LowRail && (tier.Fine || fe.LowRailAlways))
            {
                // low dry rails: straight chords short enough to stay on the ring (16 of them: the middle of a chord is 4 cm inside)
                int chords = Mathf.Max(8, tier.FenceRails * Mathf.Max(1, f.LowRailChordsPerRail));
                for (int i = 0; i < chords; i++)
                {
                    float mid = (i + .5f) / chords * 360f; if (mid > 180f) mid -= 360f;
                    float halfArc = 180f / chords - 1.5f;
                    Vector3 from = OnRing(centre, mid - halfArc, radius) + Vector3.up * f.LowRailHeight;
                    Vector3 to = OnRing(centre, mid + halfArc, radius) + Vector3.up * f.LowRailHeight;
                    Vector3 chord = to - from; float length = chord.magnitude;
                    int birth = RingCel(ref c, mid);
                    if (InkBurstMeshBuilder308.AddDryStroke(c.B, from, chord / length, Vector3.up, length, railHalf * .8f, i % 4, birth, c.R.Next(), c.Tint, Vector3.zero, 0f))
                    { c.S.Fine++; Born(ref c, birth); }
                }
            }

            // a stone wall: two courses of long thick strokes laid ALONG the ring, the upper one set off by half a stone. The
            // stones are counted from the share of the ring a course must cover (the same on every tier: a wall with holes a
            // stone wide does not read as a wall), not from the tier's post count.
            // look2: a DRY-STONE wall. A stone is one rounded ink lump (the atlas's puddle cell: a wet body inside a pooled
            // rim, so every stone keeps its own outline where they overlap), not a long stroke - long strokes read as logs.
            // BrickLength / BrickHalfWidth are the lump's quad (the lump itself fills about .84 x .57 of it); CourseCover
            // over 1.2 makes neighbours overlap, so a course has no gap. Four vertices a stone: the wall fits the Mobile slot.
            float stoneLength = Mathf.Min(f.BrickLength, radius * 1.2f);
            int stones = fe.Courses > 0 ? Mathf.Max(3, Mathf.CeilToInt(2f * Mathf.PI * radius * f.CourseCover / stoneLength)) : 0;
            for (int course = 0; course < Mathf.Min(2, fe.Courses); course++)
                for (int i = 0; i < stones; i++)
                {
                    float degrees = (i + .5f + course * .5f + c.R.Range(-.1f, .1f)) / stones * 360f; if (degrees > 180f) degrees -= 360f;
                    float a = degrees * Mathf.Deg2Rad;
                    Vector3 tangent = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));
                    // forms3 (D8): stones of three sizes, in an order that never puts two small ones side by side (a course of
                    // even stones read as a chain). A stone of the lower course rests on the ground whatever its size; the upper
                    // course takes no stone larger than StoneUpperMax (the wall's height limit).
                    var sizes = f.StoneSizes;
                    float scale = sizes != null && sizes.Length > 0 ? Mathf.Clamp(sizes[(i * 2 + course) % sizes.Length], .3f, 2f) : 1f;
                    if (course > 0) scale = Mathf.Min(scale, f.StoneUpperMax);
                    float halfWide = stoneLength * .5f * scale * c.R.Range(1f - f.StoneWobble, 1f + f.StoneWobble), halfHigh = f.BrickHalfWidth * scale * c.R.Range(1f - f.StoneWobble * 2f, 1f + f.StoneWobble);
                    // the upper course stands a finger nearer the middle: it lies ON the lower one, seen from inside the ring
                    Vector3 at = OnRing(centre, degrees, radius - course * f.StoneInset) + Vector3.up * ((course == 0 ? f.CourseLow * scale : f.CourseHigh) + c.R.Range(-f.StoneLift, f.StoneLift));
                    int born = Mathf.Min(RingCel(ref c, degrees) + course, c.Impact + c.BurstCels - 1);
                    if (!InkBurstMeshBuilder308.AddSprite(c.B, at, tangent * (i % 2 == 0 ? 1f : -1f), Vector3.up, halfWide, halfHigh, InkBurstMeshBuilder308.CellPuddle, born, c.R.Next(), c.Tint, Vector3.zero, 0f)) continue;
                    c.S.Sprites++; c.S.Rails++; Born(ref c, born);
                    area += 4f * halfWide * halfHigh * .45f / Mathf.Max(1f, f.CourseCover * .8f);   // the lump fills under half of its quad; the overlapped part is counted once
                }

            // waves. forms3 (D8): ONE line whose thickness follows the wave - thick on the crest, thin in the trough. The line
            // is laid in half waves: a stroke rising from a trough to the next crest (thin, swelling to its full width),
            // then one falling from that crest into the next trough (full, thinning). Their ends lie in each other where
            // the line runs level, so there is no seam, no crossing and no point. (Two rows of one width crossed each other
            // and read as a tangled tape.) A second row is drawn only when the element asks for two and the tier draws
            // fine strokes.
            int waves = fe.Wave ? tier.FenceRails * Mathf.Max(1, tier.Fine ? f.WavesPerRail : f.WavesPerRailCoarse) : 0;
            int rows = fe.Wave ? (tier.Fine ? Mathf.Clamp(fe.WaveRows, 1, 2) : 1) : 0;
            float waveHalf = railHalf * f.WaveWidthMul;
            for (int row = 0; row < rows; row++)
                for (int i = 0; i < waves * 2; i++)
                {
                    bool rising = i % 2 == 0;
                    float mid = (i + .5f + row) / (waves * 2f) * 360f; if (mid > 180f) mid -= 360f;
                    float halfArc = 90f / waves;
                    // a chord that straddles the ring: its ends as far outside as its middle is inside
                    float straddle = radius * (1f - Mathf.Cos(halfArc * Mathf.Deg2Rad)) * .5f, low = row == 0 ? f.WaveBase : f.WaveLow;   // forms4 (P9): the line lies near the ground (it floated at the stone wall's upper course)
                    // (every half starts WaveOverlapDeg back, inside the end of the one before it)
                    Vector3 from = OnRing(centre, mid - halfArc - f.WaveOverlapDeg, radius + straddle) + Vector3.up * (low + (rising ? 0f : f.WaveCrest));
                    Vector3 to = OnRing(centre, mid + halfArc, radius + straddle) + Vector3.up * (low + (rising ? f.WaveCrest : 0f));
                    Vector3 run = to - from; float length = Mathf.Max(.05f, run.magnitude); Vector3 axis = run / length;
                    Vector3 lift = (Vector3.up - axis * Vector3.Dot(Vector3.up, axis)).normalized;
                    var s = new InkStroke308
                    {
                        // level where it starts and where it ends: height(u) = u - sin(2 pi u) / (2 pi) of the way up (or down)
                        // forms4 (P9): the width is laid UPRIGHT on both halves (it was laid across each half's own chord: where a rising
                        // half met a falling one on the crest the two cross-sections stood at different angles - a folded ribbon)
                        Tail = from, Axis = axis, Side = Vector3.up, Bend = lift, Length = length, HalfWidth = waveHalf, Curve = 0f, SCurve = (rising ? -1f : 1f) * f.WaveCrest / (Mathf.PI * length),
                        TailShare = 0f, Split = 0, Segments = tier.Fine ? f.WaveSegments : f.WaveSegmentsCoarse, TailCell = i % 4, BirthCel = RingCel(ref c, mid), Seed01 = c.R.Next(), TintWeight = c.Tint, MeltBias = c.R.Next(),
                        RootWidth = rising ? f.WaveTrough : f.WaveJoin, RootRamp = rising ? 1f : f.WaveJoinRamp, HeadTaper = rising ? 0f : 1f, HeadTaperEnd = f.WaveTrough * f.WaveEndShare,
                    };
                    if (!InkBurstMeshBuilder308.AddStroke(c.B, s)) continue;
                    c.S.Bold++; c.S.Rails++; Born(ref c, s.BirthCel);
                    area += waveHalf * (1f + f.WaveTrough) * length;
                    // a drop hanging under each crest of the upper row: a tailed drop turned head down (the drip cell is a
                    // fringe that fills its cell edge to edge - as a single drop it printed a hard square)
                    for (int k = 0; rising && row == 0 && k < fe.HangDrops; k++)
                    {
                        float drop = f.WaveDropLength * c.R.Range(1f - f.WaveDropWobble, 1f + f.WaveDropWobble);
                        if (InkBurstMeshBuilder308.AddSprite(c.B, to - axis * (waveHalf * (1f + k)) + Vector3.down * (waveHalf * f.WaveDropUnder + drop * .5f), Vector3.down, axis, drop * .5f, f.WaveDropHalfWidth, InkBurstMeshBuilder308.CellDropTailed, s.BirthCel + 1, c.R.Next(), c.Tint * f.WaveDropWash, Vector3.zero, 0f))
                            c.S.Sprites++;
                    }
                }

            // the ground ring: wet while the ward lives (held), then it dries
            int ring = tier.FenceRingDrops;
            for (int i = 0; i < ring; i++)
            {
                float a = (i + c.R.Next() * .6f) / Mathf.Max(1, ring) * Mathf.PI * 2f;
                float size = DropSize(ref c, out int cell);
                Drop(ref c, new Vector3(Mathf.Cos(a) * radius, y0, Mathf.Sin(a) * radius), size, cell, c.Impact + c.BurstCels - 1, false, Vector3.zero, true);
            }

            c.S.Reach = radius;
            float eyeToRing = Mathf.Sqrt(radius * radius + f.EyeHeight * f.EyeHeight);
            c.S.ScreenShare = area * RingViewShare / (2.37f * eyeToRing * eyeToRing);
        }

        // pass 4b: the i-th entry of a written order, read round and round (a missing or empty order = `none`)
        private static float Pick(float[] order, int i, float none) => order != null && order.Length > 0 ? order[((i % order.Length) + order.Length) % order.Length] : none;

        // the point of the ring `degrees` round from straight ahead (+Z), clockwise seen from above
        private static Vector3 OnRing(Vector3 centre, float degrees, float radius)
        {
            float a = degrees * Mathf.Deg2Rad;
            return centre + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius;
        }

        // a stroke along the ring from `from` to `to` (both at `radius` from `pivot`, same height): the bend draws the arc.
        // The parabola goes through both ends AND through the ring's own point between them (its middle stands the arc's
        // sagitta outside the chord): spine(u) = from + chord u + outward 4 sag u (1 - u). For a half arc of 42 deg on a 3 m
        // ring it leaves the ring by 2 cm at most (a parabola that only leaves `from` along the tangent sags 0.3 - 0.85 m inside).
        private static InkStroke308 RingArc(ref Ctx c, Vector3 pivot, Vector3 from, Vector3 to, float radius, float halfWidth, float birth)
        {
            Vector3 chord = to - from;
            float span = Mathf.Max(.2f, chord.magnitude);
            Vector3 flat = chord / span;
            Vector3 outward = (from + to) * .5f - pivot; outward.y = 0f;
            outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.Cross(flat, Vector3.up);
            float halfSpan = Mathf.Min(span * .5f, Mathf.Max(.01f, radius) * .95f);
            float sag = radius - Mathf.Sqrt(Mathf.Max(0f, radius * radius - halfSpan * halfSpan));
            Vector3 run = flat * span + outward * (4f * sag);
            float length = Mathf.Max(.2f, run.magnitude);
            var s = Stroke(ref c, to, run / length, Vector3.up, length, halfWidth, birth);
            s.Axis = run / length; s.Length = length; s.Side = Vector3.up;
            s.Bend = outward;
            s.Curve = -4f * sag / length; s.SCurve = 0f; s.Split = 0;
            s.Tail = from; s.Segments = 8; s.TailShare = Mathf.Min(s.TailShare, .15f);
            return s;
        }

        // pass 4b (Q3): one smear of the flame fence's base band, along the ring from `fromDeg` (where it starts: thin, combed dry,
        // low) to `toDeg` (where the brush was pressed: its full height `high` x 2, swollen, closing blunt). Its lower edge lies
        // FlameBaseSink under the ground all along - only its top rises and falls (a band of one width is a rail).
        private static InkStroke308 RingSmear(ref Ctx c, Vector3 centre, float fromDeg, float toDeg, float radius, float high, float off, SpellDeploy308ProfileSO.FenceSet f, float birth, bool fine)
        {
            var head = f.FlameBaseHead;
            float swell = head != null && head.Share > 0f ? Mathf.Clamp(head.Swell, 0f, .5f) : 0f, root = Mathf.Clamp(f.FlameBaseRoot, .2f, 1f);
            high = Mathf.Min(high, (f.FlameBaseTop + f.FlameBaseSink) / (2f * (1f + swell)));   // never over FlameBaseTop (the pressed end is 2 x high x (1 + swell) tall)
            float mid = (fromDeg + toDeg) * .5f, halfArc = Mathf.Abs(toDeg - fromDeg) * .5f;
            // the spine rises with the smear's height, so that the lower edge stays where it is
            // (each end stands a little off the ring, in or out: a band that follows the circle exactly is a ruled hoop)
            Vector3 from = OnRing(centre, fromDeg, radius - off * c.R.Range(f.FlameBaseOffLeast, 1f)) + Vector3.up * (high * root - f.FlameBaseSink), to = OnRing(centre, toDeg, radius + off * c.R.Range(f.FlameBaseOffLeast, 1f)) + Vector3.up * (high - f.FlameBaseSink);
            Vector3 chord = to - from;
            Vector3 outward = OnRing(Vector3.zero, mid, 1f);
            float sagitta = radius * (1f - Mathf.Cos(halfArc * Mathf.Deg2Rad));
            Vector3 bend = outward * (4f * sagitta);
            Vector3 run = chord + bend;
            float length = Mathf.Max(.05f, run.magnitude), bent = bend.magnitude;
            var s = new InkStroke308
            {
                Tail = from, Axis = run / length, Side = Vector3.up, Bend = bent > 1e-6f ? bend / bent : Vector3.up, Length = length, HalfWidth = high, Curve = -bent / length, SCurve = 0f,
                TailShare = 0f, Split = 0, Segments = Mathf.Clamp(Mathf.CeilToInt(halfArc * 2f / Mathf.Max(4f, fine ? f.FlameBaseSegmentDeg : f.FlameBaseSegmentDegCoarse)), 2, 6), BirthCel = birth, Seed01 = c.R.Next(), TintWeight = c.Tint, MeltBias = c.R.Next(),
                DryStart = true, DryShare = f.FlameBaseDry, RootWidth = root, RootRamp = f.FlameBaseRamp,
            };
            s.DryTailU = c.P.Stroke.DryStartTailU;
            // (+Side is up: the top edge swells and closes; drawn the other way round the ring the same edge is still the top)
            InkBurstMeshBuilder308.PressHead(ref s, head, 1f);
            return s;
        }

        // forms3: a short stroke along the ring, `halfArc` degrees to each side of `mid`, at height `y` over the ring's centre,
        // whose middle stands `bow` m over (negative: under) its ends. One parabola through both ends and through the ring's
        // own point between them, lifted by `bow`: spine(u) = from + chord u + (outward 4 sag + up 4 bow) u (1 - u).
        // It has no tail, no head and no bend of its own - the caller shapes it (spindle, wave, dab).
        private static InkStroke308 RingBow(ref Ctx c, Vector3 centre, float mid, float halfArc, float radius, float y, float bow, float halfWidth, float birth)
        {
            Vector3 from = OnRing(centre, mid - halfArc, radius) + Vector3.up * y, to = OnRing(centre, mid + halfArc, radius) + Vector3.up * y;
            Vector3 chord = to - from;
            Vector3 outward = OnRing(Vector3.zero, mid, 1f);
            float sag = radius * (1f - Mathf.Cos(halfArc * Mathf.Deg2Rad));
            Vector3 bend = outward * (4f * sag) + Vector3.up * (4f * bow);
            Vector3 run = chord + bend;
            float length = Mathf.Max(.05f, run.magnitude), bent = bend.magnitude;
            return new InkStroke308
            {
                Tail = from, Axis = run / length, Side = Vector3.up, Bend = bent > 1e-6f ? bend / bent : Vector3.up, Length = length, HalfWidth = halfWidth, Curve = -bent / length, SCurve = 0f,
                TailShare = 0f, Split = 0, Segments = 6, TailCell = (int)(c.R.Next() * 3.99f), BirthCel = birth, Seed01 = c.R.Next(), TintWeight = c.Tint, MeltBias = c.R.Next(),
            };
        }

        // ---- comet. Authored at the target with the flight as its advance: the runtime moves it with the judged flight clock
        // (every frame). It lives from cel 0 to the impact cel (an ignition-style mark), then the burst is the impact.

        private static void Comet(ref Ctx c, in DeployFormInput308 input, SpellDeploy308ProfileSO.TierSet tier)
        {
            var ce = c.P.CometOf(input.Element);
            Vector3 target = input.Target;
            float distance = Mathf.Max(.5f, target.magnitude);
            Vector3 flight = target.sqrMagnitude > 1e-6f ? target : Vector3.forward * distance;
            float death = -Mathf.Max(1, c.Impact);
            // screen plane at the target: the viewer's right is -ScreenRight (ScreenRight = up x toward-the-eye)
            Vector3 viewRight = -c.ScreenRight, viewUp = c.ScreenUp;
            Vector2 lay = c.P.Comet.TailScreen.sqrMagnitude > 1e-4f ? c.P.Comet.TailScreen.normalized : new Vector2(.8f, -.6f);
            Vector3 tail = (viewRight * lay.x + viewUp * lay.y).normalized;          // from the head toward the brush tip
            Vector3 across = Vector3.Cross(c.ToCamera, tail).normalized;
            // the comet is smallest when it arrives: its head keeps a least size on screen at the target's distance, its tails
            // a least width (a dark stroke against the world, not the old bolt's bright additive body)
            float eyeDistance = (target - c.Camera).magnitude;
            float authored = Mathf.Max(.02f, ce.HeadSize);
            float size = authored * InkForms2Rules308.CometScale(c.P, authored, eyeDistance);
            c.S.CometHead = size; c.S.CometTailHalf = 0f;

            if (ce.HeadCell == 0)
            {
                // a forked stroke tip pointing away from the tail
                var s = Stroke(ref c, target - tail * size, -tail, across, size * 2f, size * .35f, death);
                s.Curve = 0f; s.SCurve = 0f; s.Bend = across; s.Tail = target + tail * size; s.Segments = 4; s.TailShare = 0f; s.Split = 3; s.Advance = flight;
                if (InkBurstMeshBuilder308.AddStroke(c.B, s)) { c.S.Bold++; Born(ref c, 0f); }
            }
            else if (InkBurstMeshBuilder308.AddSprite(c.B, target + tail * (ce.HeadCell == InkBurstMeshBuilder308.CellDropTailed ? size * .45f : 0f), -tail, across,
                size * (ce.HeadCell == InkBurstMeshBuilder308.CellDropTailed ? 1.1f : .5f), size * (ce.HeadCell == InkBurstMeshBuilder308.CellDropTailed ? .8f : .5f), ce.HeadCell, death, c.R.Next(), c.Tint * .5f, flight, 1f))
            { c.S.Sprites++; Born(ref c, 0f); }

            int tails = Mathf.Min(ce.TailCount, tier.CometTails);
            for (int k = 0; k < tails; k++)
            {
                switch (input.Element)
                {
                    case Element.Fire:
                    {
                        // short strokes curling upward, one to each side of the tail line
                        Vector3 axis = (tail + viewUp * (k == 0 ? .55f : .15f) + across * (k == 0 ? .2f : -.25f)).normalized;
                        float half = InkForms2Rules308.CometTailHalf(c.P, size * .3f, eyeDistance);
                        if (InkBurstMeshBuilder308.AddDryStroke(c.B, target, axis, Vector3.Cross(c.ToCamera, axis).normalized, ce.TailLength, half, k, death, c.R.Next(), c.Tint, flight, 1f)) { c.S.Fine++; c.S.CometTailHalf = half; }
                        break;
                    }
                    case Element.Earth:
                        // no tail: smaller lumps follow the head
                        if (InkBurstMeshBuilder308.AddSprite(c.B, target + tail * (ce.TailLength * (k + 1.2f)), viewRight, viewUp, size * (.28f - .05f * k), size * (.28f - .05f * k),
                            InkBurstMeshBuilder308.CellCluster, death, c.R.Next(), c.Tint * .5f, flight, 1f)) c.S.Sprites++;
                        break;
                    case Element.Metal:
                    {
                        // the thinnest of the five, but a dry stroke: a needle of this length is under a pixel wide at 10 m
                        float half = InkForms2Rules308.CometTailHalf(c.P, ce.TailHalf, eyeDistance);
                        if (InkBurstMeshBuilder308.AddDryStroke(c.B, target, tail, across, ce.TailLength, half, 3, death, c.R.Next(), 0f, flight, 1f)) { c.S.Fine++; c.S.CometTailHalf = half; }
                        // look2: the dry stroke's root is a straight cut - a short wet point covers it (the needle's tip)
                        var point = Stroke(ref c, target - tail * (half * 3f), -tail, across, half * 4f, half, death);
                        point.Curve = 0f; point.SCurve = 0f; point.Bend = across; point.Tail = target + tail * half; point.Segments = 2; point.TailShare = 0f; point.Split = 1; point.Advance = flight; point.TintWeight = 0f;
                        if (InkBurstMeshBuilder308.AddStroke(c.B, point)) c.S.Bold++;
                        break;
                    }
                    case Element.Water:
                    {
                        float half = InkForms2Rules308.CometTailHalf(c.P, size * .2f, eyeDistance);
                        var s = Stroke(ref c, target, -tail, across, ce.TailLength, half, death);
                        s.Curve = 0f; s.Bend = across; s.SCurve = .22f; s.Split = 0; s.Tail = target + tail * ce.TailLength; s.Segments = 8; s.TailShare = .3f; s.Advance = flight;
                        if (InkBurstMeshBuilder308.AddStroke(c.B, s)) { c.S.Bold++; c.S.CometTailHalf = half; }
                        break;
                    }
                    default:
                    {
                        float half = InkForms2Rules308.CometTailHalf(c.P, size * .28f, eyeDistance);
                        if (InkBurstMeshBuilder308.AddDryStroke(c.B, target, tail, across, ce.TailLength, half, 0, death, c.R.Next(), c.Tint, flight, 1f)) { c.S.Fine++; c.S.CometTailHalf = half; }
                        break;
                    }
                }
            }
            // the jamo's end treatment is born at the target on the impact cel, as it was for the main stroke
            Final(ref c, input, target, -tail, across, Mathf.Max(size * 4f, ce.TailLength), flight);

            // ink shed on the way: marks on the ground under the flight line, each on the cel the head passes over it
            int shed = Mathf.Min(ce.ShedDrops, tier.CometDrops);
            for (int i = 0; i < shed; i++)
            {
                float u = (i + 1f) / (shed + 1f);
                Vector3 under = new Vector3(target.x * u, Mathf.Lerp(input.OriginGroundY, input.GroundY, u), target.z * u);
                FreeDrop(ref c, under, c.P.Residue.DropMid * c.E.DropSizeMul * 1.6f, InkBurstMeshBuilder308.CellDrop, Mathf.RoundToInt(u * c.Impact), false, Vector3.zero);
            }
            c.S.Comet = true; c.S.HasAdvance = true;
        }

        // ---- loose mark: an install drawn at nothing. look2: no square - an ink blot with two or three drops running down
        // from it; the runs let go one after another and fall, the blot dries. Nothing is installed, nothing is held.

        private static void Loose(ref Ctx c, in DeployFormInput308 input)
        {
            var l = c.P.Loose;
            int slide = Mathf.Max(1, l.SlideCel), fall = Mathf.Max(slide + 1, l.FallCel);
            float half = l.SealSize * .5f;
            // the blot (the atlas's smear with its satellite drops): it stays and melts with the burst
            Sprite(ref c, Vector3.zero, half * l.BlotMul, InkBurstMeshBuilder308.CellIgnite, 0f, false);
            if (l.Needles > 0) NeedleStar(ref c, Vector3.zero, l.Needles, half * l.NeedleMin, half * l.NeedleMax, -slide, Vector3.zero, 0f);
            // the runs: tailed drops turned head down under the blot, uneven in length; each is gone on the cel it lets go ...
            int runs = Mathf.Clamp(l.Runs, 1, 3);
            Vector3 down = -c.ScreenUp;
            Vector3 lowest = Vector3.zero;
            for (int r = 0; r < runs; r++)
            {
                float x = (r - (runs - 1) * .5f) * half * .75f + c.R.Range(-.02f, .02f);
                float run = l.RunLength * (r == runs / 2 ? 1.25f : c.R.Range(.65f, 1f));
                Vector3 at = c.ScreenRight * x + down * (half * .55f + run * .5f);
                int lets = fall + r;
                if (InkBurstMeshBuilder308.AddSprite(c.B, at, down, c.ScreenRight, run * .5f, run * .2f, InkBurstMeshBuilder308.CellDropTailed, -lets, c.R.Next(), c.Tint * .5f, Vector3.zero, 0f))
                { c.S.Sprites++; Born(ref c, 0f); }
                // ... and falls as a tailed drop
                Vector3 from = at + down * (run * .4f);
                FreeDrop(ref c, from, c.P.Residue.DropMid * (r == runs / 2 ? 1.4f : 1f), InkBurstMeshBuilder308.CellDropTailed, lets, true, Vector3.zero);
                if (r == runs / 2) lowest = from;
            }
            float height = Mathf.Max(0f, lowest.y - input.OriginGroundY);
            int land = fall + runs / 2 + Mathf.CeilToInt(Mathf.Sqrt(2f * height / Mathf.Max(.1f, c.P.Residue.Gravity)) / Mathf.Max(.01f, input.CelSeconds));
            FreeDrop(ref c, new Vector3(0f, input.OriginGroundY, 0f), l.GroundSize, InkBurstMeshBuilder308.CellPuddle, Mathf.Min(land, fall + c.BurstCels), false, Vector3.zero);
        }

        // ---- emerge: the summon's curtain, at the summon's own place (when the caller gave it), set a little toward the eye so
        // that the model appears from behind it. look2: the earlier accepted Rise again - 3 or 5 bold ink strokes pulled
        // straight up out of the ground, the middle one first and tallest, the outer ones lower and fanned a little outward;
        // their feet span Summon.FootSpan, so with the strokes' own width the curtain is at least shoulder wide. (The leaning
        // bundle of the first forms2 look was half a metre wide and read as a tuft of leaves.) The top stays under the eye
        // line (Summon.RiseHeight), so the head and shoulders of an enemy behind the summon stay in view.

        private static void Emerge(ref Ctx c, in DeployFormInput308 input, SpellDeploy308ProfileSO.TierSet tier, int ground)
        {
            var st = c.P.Stroke; var su = c.P.Summon;
            Vector3 place = input.HasAnchor ? input.Anchor : new Vector3(0f, input.OriginGroundY, st.SummonForward);
            Vector3 foot = place;
            if (input.HasAnchor)
            {
                Vector3 toEye = new Vector3(c.Camera.x - place.x, 0f, c.Camera.z - place.z);
                if (toEye.sqrMagnitude > su.StandOff * su.StandOff * 4f) foot = place + toEye.normalized * su.StandOff;
            }
            // the strokes stand in a row across the line from the eye to the summon (not across the cast's own focus)
            Vector3 fromEye = new Vector3(foot.x - c.Camera.x, 0f, foot.z - c.Camera.z);
            Vector3 across = fromEye.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, fromEye.normalized) : new Vector3(c.ScreenRight.x, 0f, c.ScreenRight.z);
            across = across.sqrMagnitude > 1e-4f ? across.normalized : Vector3.right;
            // round 2: always the same few strokes - the grade shows in their weight, not in their number (five read as a hand).
            // forms3 (D6): the accepted 10c rising stroke again. Each stroke is THICK (height to width about 4 : 1), keeps its
            // width from the foot up and ends in the brush's pressed head, whose tip alone is drawn to a side. Its foot is
            // frayed and starts under the ground: the hairs stand IN the ground mark. The feet lie over each other (the
            // strokes stand closer than they are wide), so the three read as one curtain; they fan apart only upward.
            // (Thin strokes that narrowed at both ends and stood apart, their feet in the air, were three aloe leaves.)
            int count = Mathf.Min(Mathf.Clamp(su.Strokes, 1, 5), tier.BoldMax >= 5 ? 5 : 3);
            float flip = (input.Seed & 1) == 0 ? 1f : -1f;                       // which side the second stroke rises on
            float half0 = su.CurtainWidth * su.StrokeHalfShare * (1f + su.GradeWiden * Mathf.Clamp(input.Grade, 0, 2));
            float step = count > 1 ? su.FootSpan / (count - 1) : 0f;
            Vector3 away = fromEye.sqrMagnitude > 1e-4f ? fromEye.normalized : Vector3.Cross(across, Vector3.up);
            for (int i = 0; i < count; i++)
            {
                // the middle stroke first, then outward to both sides: k = 0, +1, -1, +2, -2
                int rank = (i + 1) / 2;
                float k = i == 0 ? 0f : rank * (i % 2 == 1 ? flip : -flip);
                // fix pass 4b: the fan is uneven - the stroke on the side the strokes bend toward opens by FanDeg, the other by FanOtherDeg
                float degrees = (k * flip > 0f ? su.FanDeg : su.FanOtherDeg) * k * Mathf.Deg2Rad;
                Vector3 axis = Vector3.up * Mathf.Cos(degrees) + across * Mathf.Sin(degrees);
                // the middle stroke's TOP stands at RiseHeight; each step outward is lower
                // forms4 (P4): the outer strokes are smaller, each by its own share (three equal strokes were three cucumbers)
                float tall = i == 0 ? 1f : su.OuterHeights != null && i - 1 < su.OuterHeights.Length ? Mathf.Clamp(su.OuterHeights[i - 1], .4f, 1f) : Mathf.Max(.4f, 1f - su.RiseStep * rank);
                float wide = i == 0 ? 1f : su.OuterWidths != null && i - 1 < su.OuterWidths.Length ? Mathf.Clamp(su.OuterWidths[i - 1], .3f, 1f) : su.OuterWidthShare;
                float top = su.RiseHeight * tall * c.R.Range(1f - su.TopJitter, 1f);
                float half = half0 * wide;
                float length = Mathf.Max(.3f, (top - half * su.HeadRoom) / Mathf.Max(.5f, axis.y));
                // each step outward stands a little behind the middle stroke: the feet overlap without lying in one plane
                Vector3 start = foot + across * (k * step + c.R.Range(-su.FootJitter, su.FootJitter)) + away * (rank * su.DepthStep);
                // the foot starts under the ground: what is seen at the ground is the frayed part, already dense
                Vector3 sunk = start - axis * su.RootSink;
                float full = length + su.RootSink;
                Vector3 side = FacingSide(ref c, start + axis * (length * .5f), axis);
                // forms4 (P2): a leaf of a folding screen - each step outward is turned about its upright, its outer edge away from the eye
                // (fix pass 4b: the middle one too, by MidTurnDeg, the way the strokes bend)
                if (i > 0 ? su.TurnDeg > 0f : su.MidTurnDeg > 0f)
                {
                    float turn = (i > 0 ? su.TurnDeg : su.MidTurnDeg) * Mathf.Deg2Rad;
                    Vector3 flatSide = Vector3.Dot(side, across) >= 0f ? across : -across;
                    side = (flatSide * Mathf.Cos(turn) + away * (Mathf.Sin(turn) * (i > 0 ? Mathf.Sign(k) : flip) * Mathf.Sign(Vector3.Dot(flatSide, across)))).normalized;
                    side = (side - axis * Vector3.Dot(side, axis)).normalized;
                }
                var s = Stroke(ref c, sunk + axis * full, axis, side, full, half, c.Impact + Mathf.Min(c.BurstCels - 1, rank * su.BirthStep));
                s.Split = 1; s.HeadTaper = su.HeadTaper; s.DryStart = true; s.DryTailU = c.P.Stroke.DryStartTailU; s.DryShare = su.DryShare; s.RootWidth = su.RootWidth; s.RootRamp = su.RootRamp; s.TailShare = su.TailShare; s.Segments = su.Segments;
                // forms4 (P4): the wet body itself is planted BodySink under the ground - the frayed foot is what lies deeper
                if (su.BodySink > 0f) s.TailShare = Mathf.Clamp(Mathf.Max(0f, su.RootSink - su.BodySink) / Mathf.Max(.01f, full), 0f, .6f);
                // whatever the element bends, the head never stands over RiseHeight: the bend goes sideways, never up. All the
                // strokes bend the same way (one hand drew them), the outer ones a little more.
                s.Tail = sunk; s.Length = full; s.Bend = across * flip; s.Curve = su.Curve * (1f + su.OuterCurveStep * rank * (Mathf.Sign(k) == flip ? 1f : -1f)); s.SCurve = su.SCurve * (i % 2 == 0 ? 1f : -1f);
                // the pressed head: the edge away from the bend comes in, the tip is drawn toward the bend
                InkBurstMeshBuilder308.PressHead(ref s, su.Head, Vector3.Dot(side, across) * flip >= 0f ? -1f : 1f);
                AddBold(ref c, s, foot, Vector3.zero);
            }
            // the needles of the burst stay under the curtain's top too
            float needle = Mathf.Min(st.NeedleLength * .5f, su.RiseHeight * .5f);
            // round 2: no needle star by default (thin lines crossing the strokes as an X)
            if (su.Needles > 0) NeedleStar(ref c, foot + Vector3.up * (su.RiseHeight * su.NeedleHeightShare), Mathf.Min(su.Needles, tier.NeedleMax), needle * .5f, needle, c.Impact, Vector3.zero, 0f);
            Drop(ref c, new Vector3(place.x, input.OriginGroundY, place.z), su.Puddle, InkBurstMeshBuilder308.CellPuddle, c.Impact + c.BurstCels - 1, false, Vector3.zero, false);
            GroundDrops(ref c, input, new Vector3(place.x, input.OriginGroundY, place.z), 1.1f, ground, false);
            c.S.Reach = new Vector2(foot.x, foot.z).magnitude;
        }

        // ---- cover (fix pass, director 3): the standing wall the spell presenter shows for a cover (its Stand act; the cast
        // says Cover). It was the ward category's ring of ink columns: a 0.3 - 0.5 m saw-toothed crown that covered nothing and
        // broke the no-thorn rule. Now: a few THICK rising strokes of the curtain's family standing on the cover's ring - each
        // planted in the ground mark (frayed foot under the ground), about a metre tall, ending in the brush's pressed head.
        // They face the eye, lean a little to and fro and differ in height; the ward's held ground marks lie under them.
        // Local frame: the cover's centre on the ground. The ward category's own form (a cast that does not say Cover) is
        // untouched.
        /// <summary>forms4 (P8): the `index`-th end drop of a buff differs from the others - its size (returned as a multiple),
        /// where it starts and how it is tossed. Deterministic: the same seed and index give the same drop.</summary>
        public static float BuffShedVary(SpellDeploy308ProfileSO profile, int seed, int index, ref Vector3 point, ref Vector3 velocity)
        {
            if (profile == null) return 1f;
            var b = profile.Buff;
            // the seed gives each property a phase; the drops of one end are then spread by the golden ratio, so two drops of
            // one end never come out alike (plain random numbers do, now and then)
            var rng = new DeployRng308(seed * 31 + 17);
            float Spread(float phase, float step) { float u = phase + index * step; return (u - Mathf.Floor(u)) * 2f - 1f; }
            float size = 1f + Spread(rng.Next(), .618034f) * Mathf.Clamp(b.ShedSizeVary, 0f, .6f);
            float high = Spread(rng.Next(), .381966f);
            point += Vector3.up * (high * b.ShedHeightVary);
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 side = flat.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;
            // pass 4c: the drop that starts higher is tossed harder - the three stay apart in height all the way down (they fell level: three dashes)
            float lift = Spread(rng.Next(), .618034f);
            if (b.ShedLiftWithHeight) lift = high;
            velocity = new Vector3(velocity.x, velocity.y * (1f + lift * Mathf.Clamp(b.ShedLiftVary, 0f, .8f)), velocity.z) + side * (Spread(rng.Next(), .381966f) * b.ShedDrift);
            return size;
        }

        /// <summary>forms4 (P3): the first cel on which nothing of a cover wall glows - its birth cels only (GlowCels after the
        /// cel it rises on), never later than the burst beat allowed.</summary>
        public static int CoverGlowEndCel(SpellDeploy308ProfileSO profile, int burstGlowEndCel, int impactCel) =>
            Mathf.Min(burstGlowEndCel, impactCel + (profile != null ? profile.GlowCels : 0));

        /// <summary>pass 4b (Q1): the largest ring (m) a cover wall of this tier can CLOSE - on a larger one the strokes would stand
        /// apart and hide nothing, so the wall is not stood at all (the cover keeps its held ground marks and its ring stroke).</summary>
        public static float CoverClosesTo(SpellDeploy308ProfileSO profile, bool fine)
        {
            if (profile == null) return 0f;
            var cv = profile.Cover;
            return Mathf.Max(0f, fine ? cv.ClosesTo : cv.ClosesToCoarse);
        }

        // pass 4b (main decision Q1): a row of brush strokes PULLED UPWARD. Each stroke is pressed into the ground mark (its
        // blunt, lopsided, swollen foot is sunk under the ground), keeps its width up to the height the wall must hide
        // (Cover.HideHeight) and ENDS above it: the brush is lifted, the stroke narrows and dries into a tip. The tips stand at
        // written, uneven heights between TipLow and TopMax (a tall tip is drawn out long and thin, a low one ends short and
        // broad), all swaying the same way round the ring by different amounts. (Strokes closed with a pressed head at one
        // level were planks / a stockade.)
        /// <summary>The burst shader's combed start (InkBurst308.shader: keep = hairs + t x 1.5 - .72): of the share of a body the hairs
        /// run over, the ink is solid from this part of it on. An identity of that shader line, not a tuning number.</summary>
        public const float CombSolid = .48f;

        private static void Cover(ref Ctx c, in DeployFormInput308 input, SpellDeploy308ProfileSO.TierSet tier)
        {
            var cv = c.P.Cover; var st = c.P.Stroke;
            float radius = input.WardRadius > 0f ? input.WardRadius : 3f, y0 = input.OriginGroundY;
            c.S.Reach = radius;
            // a ring the strokes cannot close: no wall (it would hide nothing) - the ground marks and the ring stroke remain
            if (radius > CoverClosesTo(c.P, tier.Fine) + 1e-4f) return;
            Vector3 centre = new Vector3(0f, y0, 0f);
            float tint = c.Tint;
            c.Tint *= Mathf.Clamp01(cv.Wash);
            int count = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * radius / Mathf.Max(.2f, cv.Apart)), cv.StrokesMin, Mathf.Max(cv.StrokesMin, tier.Fine ? cv.StrokesMax : cv.StrokesMaxCoarse));
            // the wall's own cap (main decision Q1: 1.45 m, a hand under the 1.55 m eye). The ward category's column ring keeps Stroke.WardColumnMaxHeight.
            float cap = Mathf.Min(cv.Height, cv.TopMax), hide = Mathf.Min(cv.HideHeight, cap - cv.WetOver), low = Mathf.Clamp(cv.TipLow, hide + cv.WetOver + cv.TipLeast, cap);   // (a tip under that leaves its dry hairs no room over the hide height)
            float room = 2f * Mathf.PI * radius / count;
            for (int i = 0; i < count; i++)
            {
                float degrees = (i + .5f + c.R.Range(-1f, 1f) * cv.Jitter) / count * 360f; if (degrees > 180f) degrees -= 360f;
                float a = degrees * Mathf.Deg2Rad;
                Vector3 tangent = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a)), outward = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                // forms4 (P2): no card turned to the caster - a stroke stands ON the ring facing outward, turned about its upright
                // by TurnDeg, turn about (a folding screen: from the side and from 45 degrees the wall shows width)
                float way = i % 2 == 0 ? 1f : -1f, turn = cv.TurnDeg * Mathf.Deg2Rad * way;
                Vector3 side = tangent * Mathf.Cos(turn) + outward * Mathf.Sin(turn);
                // how tall this tip stands: a written order of shares between TipLow (0) and the cap (1)
                int order = cv.Tips != null ? cv.Tips.Length : 0, slot = order > 0 ? i % order : 0;
                float tipShare = order > 0 ? Mathf.Clamp01(cv.Tips[slot]) : .5f;
                // (where the ring's count is no multiple of the order's length the LAST stroke stands beside the first: it takes the
                // first later share of the order that is level with neither of its neighbours)
                if (i == count - 1 && i > 1 && order > 2)
                {
                    float before = Mathf.Clamp01(cv.Tips[(i - 1) % order]), after = Mathf.Clamp01(cv.Tips[0]), apart = Mathf.Clamp01(cv.TipsApart);
                    for (int k = 0; k < order && (Mathf.Abs(tipShare - before) < apart || Mathf.Abs(tipShare - after) < apart); k++) { slot = (i + 1 + k) % order; tipShare = Mathf.Clamp01(cv.Tips[slot]); }
                }
                float top = Mathf.Lerp(low, cap, tipShare) - c.R.Range(0f, cv.HeightWobble);
                // it sways along the ring: the foot stands upright, the tip is drawn to one side (the same side all round - one
                // hand drew them -, a tall tip farther than a low one). spine(u), u from the tip down: tip + axis u L + bend
                // (sin sway / 2) u^2 L, which arrives upright.
                float sway = Mathf.Lerp(cv.SwayLowDeg, cv.SwayDeg, tipShare) * Mathf.Deg2Rad * c.R.Range(cv.Least, 1f) * Mathf.Clamp01(room / Mathf.Max(.2f, cv.Apart)) * Mathf.Clamp01(radius / Mathf.Max(.1f, cv.SwayFullAt));   // (less on a ring so small that the strokes stand closer than Apart, and on a small ring)
                Vector3 lean = tangent;   // (the sway runs along the ring itself, whichever way the stroke is turned)
                Vector3 rise = Vector3.up * Mathf.Cos(sway) + lean * Mathf.Sin(sway);
                float full = (top + cv.RootSink) / Mathf.Max(.5f, Mathf.Cos(sway));
                Vector3 foot = OnRing(centre, degrees, radius) + Vector3.down * cv.RootSink;
                float half = Mathf.Clamp(room * cv.RingCover * .5f / Mathf.Max(.5f, Mathf.Cos(turn)) * c.R.Range(1f - cv.WidthWobble, 1f + cv.WidthWobble), cv.HalfWidth, Mathf.Max(cv.HalfWidth, cv.HalfMax));
                // pass 4c (director 4b: a drum with a 9 cm fringe). Where the WET body ends is written stroke by stroke too (TipWets,
                // the share of the way from the hide height up to the tip): most strokes are lifted just over the hide height and
                // run on as a LONG dry tip, a few stay wet nearly to their tip (a thin wet point with a short dry end) - the skyline
                // is dry brush of unequal length, the wet ink ends at unequal heights. Never under WetOver above the hide height
                // (the dry tail's quads reach a little into the body: kept over the hide height - what hides is wet).
                float wetTop = Mathf.Max(Mathf.Lerp(hide, top, Mathf.Clamp01(Pick(cv.TipWets, slot, cv.TipWet))), hide + cv.WetOver);
                float tail = Mathf.Clamp((top - wetTop) / Mathf.Max(.01f, top + cv.RootSink), .02f, .5f);
                // a stroke lifted low ends broad, one that stays wet to its tip thin; the body is full again TipRun under where it starts, at the latest RampUnder below the hide height
                float wetHigh = Mathf.InverseLerp(hide, cap, wetTop), bodyLong = full * (1f - tail);
                float root = Mathf.Lerp(cv.TipWidthLow, cv.TipWidth, wetHigh);
                float ramp = Mathf.Clamp(Mathf.Min(wetTop - (hide - cv.RampUnder), cv.TipRun) / Mathf.Max(.01f, (top + cv.RootSink) * (1f - tail)), .05f, 1f);
                // pass 4c (rule review F2): the body's combed start (the shader combs the first DryShare of the body into hairs; the ink is
                // solid from CombSolid of that share on) ends SolidOver above the hide height at the least - what hides is solid ink.
                // (twentieths: the builder's own step; one twentieth is the least the shader combs)
                float comb = Mathf.Min(cv.DryShare * Mathf.Lerp(cv.DryLow, 1f, wetHigh), (wetTop - hide - cv.SolidOver) / Mathf.Max(.01f, CombSolid * bodyLong * Mathf.Cos(sway)));
                comb = Mathf.Max(1, Mathf.FloorToInt(comb * 20f + 1e-4f)) / 20f;
                var s = new InkStroke308
                {
                    Tail = foot + rise * full - lean * (Mathf.Sin(sway) * .5f * full), Axis = -rise, Side = side, Bend = lean, Length = full, HalfWidth = half, Curve = Mathf.Sin(sway) * .5f, SCurve = 0f,
                    TailShare = tail, Split = 0, Segments = tier.Fine ? cv.Segments : cv.SegmentsCoarse, TailCell = (i * 3 + 1) % 4, BirthCel = RingCel(ref c, degrees), Seed01 = c.R.Next(), TintWeight = c.Tint, MeltBias = c.R.Next(),
                    DryStart = true, DryShare = comb, RootWidth = root, RootRamp = ramp,
                };
                s.DryTailU = st.DryStartTailU;
                // the pressed foot: blunt, swollen and lopsided - every foot the same way round the ring
                InkBurstMeshBuilder308.PressHead(ref s, cv.Foot, 1f);
                // pass 4c: no row of equal knobs - each foot is pressed a little differently (shorter, more or less swollen)
                if (s.Press > 0f && cv.FootVary > 0f) { s.Press *= c.R.Range(1f - Mathf.Clamp01(cv.FootVary), 1f); s.PressSwell = Mathf.Clamp(s.PressSwell * c.R.Range(1f - cv.FootVary, 1f + cv.FootVary), 0f, .5f); }
                if (!InkBurstMeshBuilder308.AddStroke(c.B, s)) continue;
                c.S.Bold++; c.S.Posts++; Born(ref c, s.BirthCel);
            }
            c.Tint = tint;
        }

        // ---- combo burst: the triggered burst of an installed mark. (History: the top grade's single burst - seven long forked
        // strokes crossing in the target; then, fix pass 3, a few strokes that started .35 m off the point and ended in a pressed
        // head OUTSIDE, the middle open: six paddles round a hole - a propeller, and by the layer's own rule (a stroke starts
        // pressed and ends drawn out) strokes running INTO the point.)
        // pass 4b (main decision Q2): the accepted single burst's family. The ink MASS is at the centre - every stroke is
        // PRESSED there (its blunt head lies a little past the point, so the heads lie over each other and pool) and thrown
        // OUTWARD, thinning into a long dry tail. Uneven angles and lengths (a written order), the centre covered.
        // Profile: Trigger (Own off = the old burst).
        private static void ComboBurst(ref Ctx c, in DeployFormInput308 input)
        {
            var tr = c.P.Trigger; var st = c.P.Stroke;
            Vector3 target = input.Target;
            float distance = Mathf.Max(.5f, target.magnitude);
            Vector3 axis = target.sqrMagnitude > 1e-6f ? target / distance : Vector3.forward;
            Vector3 atTarget = axis * distance;
            Vector3 lead = axis - c.ToCamera * Vector3.Dot(axis, c.ToCamera);
            lead = lead.sqrMagnitude > .04f ? lead.normalized : c.ScreenRight;
            Vector3 leadUp = Vector3.Cross(c.ToCamera, lead).normalized;
            int bold = Mathf.Min(c.Bold, Mathf.Max(1, tr.Bold));
            float phase = c.R.Next() * Mathf.PI * 2f, open = Mathf.Max(0f, tr.Open);
            for (int i = 0; i < bold; i++)
            {
                // no even star - each stroke stands off its even place by its own written share, and has its own length
                float off = tr.Offsets != null && i < tr.Offsets.Length ? Mathf.Clamp(tr.Offsets[i], -.45f, .45f) : 0f;
                float a = phase + (i + off + c.R.Range(-tr.Scatter, tr.Scatter)) / bold * Mathf.PI * 2f;
                Vector3 direction = lead * Mathf.Cos(a) + leadUp * Mathf.Sin(a);
                float own = tr.Lengths != null && i < tr.Lengths.Length ? Mathf.Clamp(tr.Lengths[i], .3f, 1f) : i % 2 == 1 ? Mathf.Clamp(tr.Short, .4f, 1f) : 1f;
                float length = c.Length * tr.LengthMul * c.R.Range(tr.LengthLeast, 1f) * own;
                // the pressed head lies a little PAST the point (the heads lie over each other: the mass)
                float through = Mathf.Max(0f, tr.Through) * c.R.Range(tr.ThroughLeast, 1f);
                // (a stroke that runs up the view ends inside it)
                float rise = Vector3.Dot(direction, c.ScreenUp);
                if (rise > .05f) length = Mathf.Max(c.HalfWidth * 3f, Mathf.Min(length, tr.ViewHalf * (target - c.Camera).magnitude / rise + through));
                // forms4 (P7): the bold strokes are born within the first BirthCels cels (their two glowing cels are over early: what is looked at is ink)
                int born = c.Impact + (bold <= 1 ? 0 : Mathf.Min(Mathf.Min(c.BurstCels, Mathf.Max(1, tr.BirthCels)) - 1, i * Mathf.Min(c.BurstCels, Mathf.Max(1, tr.BirthCels)) / bold));
                // drawn from its dry outer end to the head at the point (the builder's tail = the dry end, its head = the pressed one)
                var s = Stroke(ref c, target - direction * through, -direction, Vector3.Cross(c.ToCamera, direction).normalized, length, c.HalfWidth * tr.WidthMul * c.R.Range(tr.WidthLeast, 1f), born);
                s.Split = 0; s.TailShare = Mathf.Clamp(tr.TailShare, 0f, .6f); s.DryStart = true; s.DryTailU = st.DryStartTailU; s.DryShare = tr.DryShare; s.RootWidth = tr.RootWidth; s.RootRamp = tr.RootRamp;
                s.Advance = atTarget;
                InkBurstMeshBuilder308.PressHead(ref s, tr.Head, i % 2 == 0 ? 1f : -1f);
                AddBold(ref c, s, target, Vector3.zero);
            }
            int fine = Mathf.RoundToInt(c.Fine * Mathf.Clamp01(tr.FineShare));
            for (int i = 0; i < fine; i++)
            {
                float a = c.R.Next() * Mathf.PI * 2f;
                Vector3 direction = lead * Mathf.Cos(a) + leadUp * Mathf.Sin(a);
                int birth = BurstCel(ref c, i, fine);
                if (InkBurstMeshBuilder308.AddDryStroke(c.B, target + direction * (open * Mathf.Clamp01(tr.FineOpen) * c.R.Range(1f - tr.OpenWobble, 1f + tr.OpenWobble)), direction, Vector3.Cross(c.ToCamera, direction).normalized,
                    c.Length * tr.LengthMul * st.FineLengthMul * c.R.Range(.6f, 1.2f), c.HalfWidth * st.FineWidthMul, (int)(c.R.Next() * 3.99f), birth, c.R.Next(), c.Tint, atTarget, 1f))
                { c.S.Fine++; Born(ref c, birth); }
            }
            int needles = Mathf.RoundToInt(c.Needles * Mathf.Clamp01(tr.NeedleShare));
            if (needles > 0) NeedleStar(ref c, target, needles, st.NeedleLength * tr.LengthMul * .6f, st.NeedleLength * tr.LengthMul * 1.3f, c.Impact, atTarget, 1f);
        }
    }
}
