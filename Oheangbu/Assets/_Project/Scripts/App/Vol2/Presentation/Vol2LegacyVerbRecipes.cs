using System;
using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    // D308-150 V9 step 4 (2026-10-25, "기존 투사체 방식이 전부 낫다 ... 계속 디벨럽"): the 100 batchim glyphs (P1 - P5, A1 - A5, F1 - F5, R1 - R5 x five bodies) LOOK like the old
    // main game too. The cast part is the bridge's (Vol2LegacyRecipeBase: the old category's swing, the brush-tip ink, the glyph's OLD body from the main game's glyph
    // table); the vol.2 verb's lasting state is drawn with OLD parts borrowed from other glyphs' old profiles (Vol2LegacyField: 결계 rims, install seals, zone floors,
    // foot stains, buff auras, guard strokes, final-marked hits, derived shots), laid and ended on the vol.2 executor's events - a material's creation / move / split /
    // end reason, a strip's UntilUs, the return report's legs, the emitter's end. Nothing here owns a lifetime: a part is closed only by such an event (the mapping table
    // is in Docs/Handoff/HANDOFF-308-VFX-LEGACY-BRIDGE-20261025.md, step 4). PURE: no Unity object is made here; the stage (Vol2LegacyStage) draws.

    /// <summary>The donors of step 4: which glyph's old profile lends which part, per element (목 화 토 금 수 = Body order).</summary>
    public static class Vol2LegacyDonors
    {
        /// <summary>The ㅜ batchim glyphs whose old 결계 rim (Zone308Prefab = PF_V308d_&lt;element&gt;) is lent.</summary>
        public const string Rim = "군눅묵숙욱";
        /// <summary>The install seals (Mark308Prefab = PF_V308m_&lt;element&gt;): the ㅏ+ㅁ glyphs' own.</summary>
        public const string Seal = "감남맘삼암";
        /// <summary>The foot stains (AreaStainPrefab).</summary>
        public const string Stain = "곡녹목속옥";
        /// <summary>The buff bands (Aura308Prefab = PF_V308f_buff_&lt;element&gt;).</summary>
        public const string Aura = "건넌먼선언";
        /// <summary>The guard membranes (Guard308Prefab).</summary>
        public const string Guard = "거너머서어";
        /// <summary>The hits with the final's marker (AreaContactPrefab): ㄱ a pool and stuck strokes, ㄴ a second wider bleed, ㅅ an X of two dry strokes, ㅇ a spreading ring; none = the plain hit.</summary>
        public const string HitK = "곡녹목속옥", HitN = "곤논몬손온", HitS = "곳놋못솟옷", HitNg = "공농몽송옹", HitPlain = "고노모소오";
        /// <summary>The derived shots (Derived308Prefab; earth has none: 마's own body flies instead).</summary>
        public const string Derived = "간낫마속안";
        /// <summary>The single bolts of the elements (the fallback body).</summary>
        public const string Single = "가나마사아";
        /// <summary>The zone floor a fan state stands on (wood 곡 vines, fire 녹 pool, earth 몽 mud fog; metal / water have no floor: their 결계 rim).</summary>
        public const string FanFloor = "곡녹몽숙욱";
        /// <summary>The ㅓ batchim glyphs whose old body IS a lasting aura (buff band, tree, sword form, companion wrap). 걱 넉 먹 석 억 were EA buffs: their band is lent from <see cref="Aura"/>.</summary>
        /// <summary>The ward letters whose old membrane (EAWardRuntime's presentation prefab) stands for a state on a ring or a fan (step 4 probe: the 결계 rims and zone floors, built
        /// knee-high and faint for a 3 m domain, were not read at 2 m from the shoulder camera; the R0 membrane was).</summary>
        public const string Ward = "구누무수우";
        /// <summary>The floor a disc residue (ㅏ 남긴다 at the struck body) stands as: wood 곳 thorns, fire 녹 pool, earth 막 rock (막's own), metal 곳 thorns (no metal floor), water 농 steam.</summary>
        public const string DiscFloor = "곳녹막곳농";
        public const string OwnAura = "건넌먼선언검넘멈섬엄것넛멋섯엇겅넝멍성엉";

        public static char Of(string table, Body b) { int i = (int)b; return i >= 0 && i < table.Length ? table[i] : '\0'; }
        public static string HitFor(Verb v) => v == Verb.Leave ? HitK : v == Verb.Spread ? HitN : v == Verb.Split ? HitS : v == Verb.Return ? HitNg : HitPlain;
    }

    /// <summary>One recipe for the 20 batchim templates: the frame decides the cast part (ㅏ single body, ㅓ the aura on the caster, ㅗ area body, ㅜ the 결계 cue body), the verb
    /// decides the lasting parts. Registered once per template (Kind = the template's recipe).</summary>
    public sealed class Vol2LegacyVerbRecipe : Vol2LegacyRecipeBase
    {
        /// <summary>The fallback warning lead before a scheduled end, for an old part with no LateBefore of its own (seconds, presentation; the end itself is the executor's).</summary>
        public const float WarnLeadSeconds = 1.0f;
        /// <summary>Flight clock bounds of a derived shot (the old presenter's DerivedMinFlight / MaxFlight / speed).</summary>
        public const float DerivedMin = 0.2f, DerivedMax = 0.9f, DerivedSpeed = 16f;
        /// <summary>TEST (presentation): the radius of the following rim an ㅓ state is carried in (metres).</summary>
        public const float CarryRadius = 1.3f;

        readonly Vol2Recipe _kind;
        public readonly Frame Frame;
        public readonly Verb Verb;

        public Vol2LegacyVerbRecipe(Frame frame, Verb verb)
        {
            if (verb == Verb.Once) throw new ArgumentException("the batchim recipe is for the five verbs, not 한 번");
            Frame = frame; Verb = verb;
            _kind = Vol2VfxDefaults.RecipeOf(new Vol2TemplateId(frame, verb));
        }

        public override Vol2Recipe Kind => _kind;
        public override Vol2LegacyKind LegacyKind => Frame == Frame.Path ? Vol2LegacyKind.Single : Frame == Frame.Fan ? Vol2LegacyKind.Area : Frame == Frame.Attachment ? Vol2LegacyKind.Self : Vol2LegacyKind.Domain;
        protected override bool HasBody => Frame != Frame.Attachment;

        // ---- per-instance state (pure; dropped when the instance retires) ----

        sealed class State
        {
            public Vector3 Source; public bool HasSource;
            public readonly Dictionary<long, Vol2VfxPart> Strips = new Dictionary<long, Vol2VfxPart>();
            public readonly List<(string role, Vol2VfxPart part)> Laid = new List<(string, Vol2VfxPart)>();
            public Vol2VfxPart Cage, Aura, Ring, Trip;
            public bool BackFired, AuraDone;
            public int Pulses;
            public readonly List<string> PulseRoles = new List<string>();
        }

        readonly Dictionary<Vol2VfxInstance, State> _st = new Dictionary<Vol2VfxInstance, State>();
        State St(Vol2VfxInstance inst) { if (!_st.TryGetValue(inst, out var s)) _st[inst] = s = new State(); return s; }

        /// <summary>What lasting parts this cast laid, with their roles (the fixture reads it; empty after the instance retired).</summary>
        public IReadOnlyList<(string role, Vol2VfxPart part)> PartsOf(Vol2VfxInstance inst) => _st.TryGetValue(inst, out var s) ? s.Laid : (IReadOnlyList<(string, Vol2VfxPart)>)Array.Empty<(string, Vol2VfxPart)>();
        /// <summary>The roles of the pulses asked so far.</summary>
        public IReadOnlyList<string> PulsesOf(Vol2VfxInstance inst) => _st.TryGetValue(inst, out var s) ? s.PulseRoles : (IReadOnlyList<string>)Array.Empty<string>();

        Body B(Vol2VfxInstance inst) => inst.Log.Glyph.Body;
        char L(Vol2VfxInstance inst) => inst.Log.Glyph.Letter;

        Vol2VfxPart Lay(Vol2VfxInstance inst, string role, char donor, char fallback, Vol2LegacyField field, Vector3 point, float radius, Vol2Material mat, long nowUs, long hostId = 0, bool onCaster = false, float seconds = 0f, Vol2Footprint shape = null)
        {
            var def = new Vol2LegacyPart { Donor = donor, Fallback = fallback, Field = field, Body = B(inst), Point = point, Facing = inst.Forward, Radius = radius, HostId = hostId, OnCaster = onCaster, Seconds = seconds, Role = role };
            // step 5a: the footprint's shape, for a pattern the stage lays on the state (a ring's band, a fan clipped to its sector)
            var f = shape ?? (mat != null ? mat.Footprint : null);
            if (f != null && f.Kind == Vol2FootprintKind.Ring) def.IsRing = true;
            else if (f != null && f.Kind == Vol2FootprintKind.Fan)
            {
                def.IsFan = true; def.FanApex = Ground(inst, f.Origin); def.FanForward = f.Forward;
                def.FanDepth = f.FanDepth * f.ScaleAt(nowUs); def.FanHalfAngle = f.FanHalfAngleRad * Mathf.Rad2Deg;
            }
            var p = LayPart(inst, def, mat, nowUs);
            St(inst).Laid.Add((role, p));
            return p;
        }

        void Pulse(Vol2VfxInstance inst, string role, char donor, char fallback, Vol2LegacyField field, Vector3 from, Vector3 to, long targetId, float seconds = 0f)
        {
            var s = St(inst); s.Pulses++; s.PulseRoles.Add(role);
            Pulse(inst, new Vol2LegacyPulse { Donor = donor, Fallback = fallback, Field = field, Body = B(inst), From = from, To = to, TargetId = targetId, Seconds = seconds, Role = role });
        }

        Vector3 Ground(Vol2VfxInstance inst, Vector3 p) { p.y = inst.Origin.y; return p; }

        Vector3 TargetPos(Vol2VfxInstance inst, long id, Vector3 fallback)
        {
            var t = inst.Model.Coordinator != null ? inst.Model.Coordinator.Registry.Find(id) : null;
            return t != null ? t.Position : fallback;
        }

        /// <summary>The body standing nearest to a material's footprint (the host a seed / mark sits on), 0 when none within 1.5 m.</summary>
        long HostAt(Vol2VfxInstance inst, Vector3 at)
        {
            var c = inst.Model.Coordinator; if (c == null) return 0;
            long best = 0; float bd = 1.5f;
            foreach (var t in c.Targets) { if (t == null || !t.Alive) continue; float d = Flat(t.Position - at).magnitude; if (d < bd) { bd = d; best = t.Id; } }
            return best;
        }

        /// <summary>The radius of a part that shows a footprint: a disc's own, a ring's middle line, half a fan's depth (its growth included).</summary>
        static float RadiusOf(Vol2Footprint fp, long nowUs)
        {
            if (fp == null) return 1f;
            switch (fp.Kind)
            {
                case Vol2FootprintKind.Disc: return Mathf.Clamp(fp.Radius, 0.9f, 1.8f);
                case Vol2FootprintKind.Ring: return Mathf.Max(0.5f, (fp.RingInner + fp.RingOuter) * 0.5f);
                case Vol2FootprintKind.Fan: return Mathf.Clamp(fp.FanDepth * fp.ScaleAt(nowUs) * 0.5f, 1f, 3.5f);
                default: return 1f;
            }
        }

        static Vector3 CentreOf(Vol2Footprint fp, long nowUs)
        {
            if (fp == null) return Vector3.zero;
            return fp.Kind == Vol2FootprintKind.Fan ? fp.Origin + fp.Forward * (fp.FanDepth * fp.ScaleAt(nowUs) * 0.5f) : fp.Origin;
        }

        // ---- the cast part ----

        protected override Vol2LegacyCue Cue(Vol2VfxInstance inst, long nowUs)
        {
            var cue = Base(inst);
            if (Frame == Frame.Path) return Vol2LegacyPathRecipe.SingleCue(inst, cue, nowUs);
            if (Frame == Frame.Fan) return Vol2LegacyFanRecipe.AreaCue(inst, cue);
            return cue;
        }

        public override void OnBegan(Vol2VfxInstance inst)
        {
            base.OnBegan(inst);
            var s = St(inst);
            long now = inst.StartUs;
            var spec = inst.Log.Spec;
            Body body = B(inst); char letter = L(inst);
            // ㅓ: the cast IS the aura the old route put on the caster (the glyph's own when it has one, else the element's buff band), for the attachment's life
            if (Frame == Frame.Attachment)
            {
                char own = Vol2LegacyDonors.OwnAura.IndexOf(letter) >= 0 ? letter : Vol2LegacyDonors.Of(Vol2LegacyDonors.Aura, body);
                s.Aura = Lay(inst, "aura", own, Vol2LegacyDonors.Of(Vol2LegacyDonors.Aura, body), Vol2LegacyField.Aura, inst.Origin, 1f, null, now, 0, true);
                // 돌아온다 ㅓ: the touch ring circling the caster (the old following 결계 rim) at the orbit radius
                // and the old following 결계 rim (궁's) at the caster's feet: the buff band alone is hardly seen from the shoulder (step 4 probe) - the 돌아온다 ring is the
                // touch ring circling the caster at the orbit radius, the others carry it close round the feet
                float r = Verb == Verb.Return ? (ReturnTuning(inst)?.OrbitRadius ?? 1.6f) : CarryRadius;
                s.Ring = Lay(inst, Verb == Verb.Return ? "orbit" : "carry", Vol2LegacyDonors.Of(Vol2LegacyDonors.Rim, body), '\0', Vol2LegacyField.Zone, inst.Origin, r, null, now, 0, true);
            }
            // ㅜ the timed rings of 번진다 / 가른다 (the band lives timing.n.life / timing.s.ringLife, not a material) and the 돌아온다 ring going out and back: the glyph's own 결계 rim
            if (Frame == Frame.Ring && (Verb == Verb.Spread || Verb == Verb.Split || Verb == Verb.Return))
            {
                var p = Lay(inst, Verb == Verb.Return ? "trip" : "ring", Vol2LegacyDonors.Of(Vol2LegacyDonors.Ward, body), '\0', Vol2LegacyField.Ward, spec.Origin, Mathf.Max(0.5f, spec.RingRadius), null, now, shape: Vol2Footprint.Ring(spec));
                if (Verb == Verb.Return) s.Trip = p; else s.Ring = p;
            }
            // ㅗ 돌아온다: the fan's floor that goes out with the fan and comes back
            if (Frame == Frame.Fan && Verb == Verb.Return)
                s.Trip = Lay(inst, "trip", Vol2LegacyDonors.Of(Vol2LegacyDonors.Ward, body), '\0', Vol2LegacyField.Ward, spec.Origin + spec.Forward * (spec.FanDepth * 0.5f), Mathf.Clamp(spec.FanDepth * 0.5f, 1f, 3.5f), null, now, shape: Vol2Footprint.Fan(spec));
        }

        // ---- materials ----

        public override void OnMaterialCreated(Vol2VfxInstance inst, Vol2Material m)
        {
            var s = St(inst);
            long now = m.CreatedUs;
            Body body = B(inst); char letter = L(inst);
            var fp = m.Footprint;
            if (m.Kind == Vol2MaterialKind.Carrier || fp == null) return;                       // 넌's carrier: the aura already shows it
            if (m.Tag == Vol2SeedExecutor.TagRubble)
            {
                // 몸: the giant collapsed into an earth fan - the old rock 막 left where it landed
                Lay(inst, "rubble", '막', Vol2LegacyDonors.Of(Vol2LegacyDonors.FanFloor, Body.Earth), Vol2LegacyField.Zone, Ground(inst, CentreOf(fp, now)), Mathf.Clamp(RadiusOf(fp, now), 1f, 2.2f), m, now);
                return;
            }
            if (m.Tag == Vol2SeedExecutor.TagGround)
            {
                // 뭄: the ring hardened (the ground) - an earth 결계 rim on the band and 뭄's own field-birth picture at the caster
                Lay(inst, "ground", '무', '\0', Vol2LegacyField.Ward, fp.Origin, RadiusOf(fp, now), m, now);
                Pulse(inst, "ground-birth", '뭄', '\0', Vol2LegacyField.Burst, inst.Origin, inst.Origin, 0);
                return;
            }
            if (fp.Kind == Vol2FootprintKind.Attached) return;                                   // on the caster: the ㅓ aura is the picture
            if (m.Kind == Vol2MaterialKind.Mark && (m.Tag == Vol2SeedExecutor.TagSeed || m.Tag == Vol2SeedExecutor.TagGiant))
            {
                if (fp.Kind == Vol2FootprintKind.Disc)
                {
                    // ㅏ 심는다: the install seal on the body it was planted on (the glyph's own: 감 남 맘 삼 암)
                    long host = HostAt(inst, fp.Origin);
                    // (Seconds = the seed's scheduled life: the old seal ignores it; a step 5a stake fits its held strokes' look to it, the end is still the executor's)
                    Lay(inst, "seed", letter, Vol2LegacyDonors.Of(Vol2LegacyDonors.Seal, body), Vol2LegacyField.Mark, fp.Origin, 1f, m, now, host, false, Vol2Time.ToSecondsF(m.ExpiresUs - now));
                    // the seal alone was not read on a dark body (step 4 probe): the element's plain hit is held on the body's near side while the seed waits
                    Lay(inst, "seed-ink", Vol2LegacyDonors.Of(Vol2LegacyDonors.HitPlain, body), '\0', Vol2LegacyField.Contact, fp.Origin, 1f, m, now, host, false, Vol2Time.ToSecondsF(m.ExpiresUs - now));
                }
                else if (fp.Kind == Vol2FootprintKind.Fan)
                    Lay(inst, "seed", Vol2LegacyDonors.Of(Vol2LegacyDonors.Ward, body), '\0', Vol2LegacyField.Ward, Ground(inst, CentreOf(fp, now)), RadiusOf(fp, now), m, now);
                else
                    Lay(inst, "seed", Vol2LegacyDonors.Of(Vol2LegacyDonors.Ward, body), '\0', Vol2LegacyField.Ward, fp.Origin, RadiusOf(fp, now), m, now);
                return;
            }
            if (m.Kind == Vol2MaterialKind.Mark)
            {
                // 번진다: the spread mark on the neighbour - a derived shot flies there from where the spread came, a foot stain stays while the mark lives
                Vector3 to = fp.Origin; long host = HostAt(inst, to);
                Vector3 from = SpreadSource(inst, to);
                float flight = Mathf.Clamp(Flat(to - from).magnitude / DerivedSpeed, DerivedMin, DerivedMax);
                Pulse(inst, "spread-shot", Vol2LegacyDonors.Of(Vol2LegacyDonors.Derived, body), Vol2LegacyDonors.Of(Vol2LegacyDonors.Single, body), Vol2LegacyField.Derived, from + Vector3.up * 0.9f, to + Vector3.up * 0.9f, host, flight);
                Lay(inst, "mark", Vol2LegacyDonors.Of(Vol2LegacyDonors.Stain, body), '\0', Vol2LegacyField.Stain, Ground(inst, to), 1.1f, m, now, host);
                return;
            }
            if (m.Kind == Vol2MaterialKind.Residue)
            {
                // 남긴다 (and 솟's metal residue, a split half): the residue stands as the glyph's own old floor / rim when it has one, else the element's
                if (s.Cage != null) ClosePart(inst, s.Cage, now, Vol2EndCode.Consume, inst.Forward);       // 각: the cage lets go where the residue appears
                switch (fp.Kind)
                {
                    case Vol2FootprintKind.Fan:
                        Lay(inst, "residue", Vol2LegacyDonors.Of(Vol2LegacyDonors.Ward, body), '\0', Vol2LegacyField.Ward, Ground(inst, CentreOf(fp, now)), RadiusOf(fp, now), m, now); break;
                    case Vol2FootprintKind.Ring:
                        Lay(inst, "residue", Vol2LegacyDonors.Of(Vol2LegacyDonors.Ward, body), '\0', Vol2LegacyField.Ward, fp.Origin, RadiusOf(fp, now), m, now); break;
                    default:
                        {
                            Lay(inst, "residue", letter, Vol2LegacyDonors.Of(Vol2LegacyDonors.DiscFloor, body), Vol2LegacyField.Zone, Ground(inst, fp.Origin), RadiusOf(fp, now), m, now);
                            // the floor alone was hardly read (step 4 probe): the element's ㄱ hit (a pool and short strokes stuck in the ground) is held on the caster's side of it
                            Vector3 near = Flat(inst.Origin - fp.Origin); near = near.sqrMagnitude > 1e-4f ? near.normalized : -inst.Forward;
                            Lay(inst, "residue-ink", Vol2LegacyDonors.Of(Vol2LegacyDonors.HitK, body), '\0', Vol2LegacyField.Contact, Ground(inst, fp.Origin) + near * 0.7f + Vector3.up * 0.05f, 1f, m, now, 0, false, Vol2Time.ToSecondsF(m.ExpiresUs - now));
                            break;
                        }
                }
            }
        }

        readonly List<Vol2VfxPart> _of = new List<Vol2VfxPart>(4);
        /// <summary>Every lasting part that shows this material (a state may be drawn with two old parts).</summary>
        List<Vol2VfxPart> PartsOf(Vol2VfxInstance inst, Vol2Material m)
        {
            _of.Clear();
            foreach (var p in inst.Parts) if (p.Kind == KindLay && p.Mat == m) _of.Add(p);
            return _of;
        }

        public override void OnMaterialChanged(Vol2VfxInstance inst, Vol2Material m, string what)
        {
            long now = inst.Model.Coordinator != null ? inst.Model.Coordinator.Services.Clock.NowUs : inst.StartUs;
            if (what == "moved" && m.Footprint != null)
                foreach (var p in PartsOf(inst, m))
                    Place(inst, p, m.Footprint.Kind == Vol2FootprintKind.Fan ? Ground(inst, CentreOf(m.Footprint, now)) : m.Footprint.Origin, p.F0);       // 앙's carry, a split half pushed aside
            else if (what == "split" && m.Footprint != null)
            {
                // another cast cut this material in two: the cut is drawn where it lies (the element's ㅅ hit)
                Vector3 at = Ground(inst, CentreOf(m.Footprint, now));
                Pulse(inst, "cut", Vol2LegacyDonors.Of(Vol2LegacyDonors.HitS, m.Body), '\0', Vol2LegacyField.Contact, at, at, 0);
            }
        }

        public override void OnMaterialEnded(Vol2VfxInstance inst, Vol2Material m, Vol2MaterialEnd why)
        {
            Vector3 dir = m.Footprint != null ? Flat(CentreOf(m.Footprint, m.EndedUs) - inst.Origin) : inst.Forward;
            foreach (var p in PartsOf(inst, m)) ClosePart(inst, p, m.EndedUs, Vol2EndCodes.From(why), dir.sqrMagnitude > 1e-6f ? dir.normalized : inst.Forward);
        }

        /// <summary>Where a spread came from (the struck body / the fan's edge / the ring's crossing), for the derived shot.</summary>
        Vector3 SpreadSource(Vol2VfxInstance inst, Vector3 to)
        {
            var s = St(inst); var spec = inst.Log.Spec;
            if (Frame == Frame.Fan)
            {
                // the nearest point of the fan sector to the neighbour (the spread runs from the fan's edge)
                Vector3 d = Flat(to - spec.Origin); float dist = d.magnitude;
                if (dist < 1e-3f) return spec.Origin;
                float ang = Vector3.SignedAngle(spec.Forward, d, Vector3.up), half = spec.FanHalfAngleRad * Mathf.Rad2Deg;
                Vector3 dir = Quaternion.AngleAxis(Mathf.Clamp(ang, -half, half), Vector3.up) * spec.Forward;
                return spec.Origin + dir * Mathf.Min(dist, spec.FanDepth);
            }
            return s.HasSource ? s.Source : spec.Origin + spec.Forward * 2f;
        }

        // ---- contacts ----

        public override void OnContact(Vol2VfxInstance inst, Vol2ContactRecord rec)
        {
            base.OnContact(inst, rec);
            var s = St(inst);
            Body body = B(inst); char letter = L(inst);
            Vector3 at = TargetPos(inst, rec.TargetId, inst.Origin + inst.Forward * rec.Along);
            bool direct = rec.Kind == "Instant" || rec.Kind == "Second";
            if (direct && (rec.Kind == "Instant" || !s.HasSource)) { s.Source = Ground(inst, at); s.HasSource = true; }
            char hit = Vol2LegacyDonors.Of(Vol2LegacyDonors.HitFor(Verb), body);
            // 각: the vine holds the first body as a cage (the wood seal on it) until the residue appears where the cage lets go
            if (letter == '각' && Frame == Frame.Path && rec.Kind == "Instant" && s.Cage == null)
                s.Cage = Lay(inst, "cage", '감', '\0', Vol2LegacyField.Mark, at, 1f, null, rec.NowUs, rec.TargetId);
            switch (rec.Kind)
            {
                case "Residue": Pulse(inst, "residue-touch", hit, '\0', Vol2LegacyField.Contact, at, at, rec.TargetId); return;
                case "Spread": case "ReturnSpread": Pulse(inst, "spread-hit", hit, '\0', Vol2LegacyField.Contact, at, at, rec.TargetId); return;
                case "Activation":
                    {
                        // the seed worked: the glyph's own short picture where it has one (멈 holds the attacker back, 넘 keeps the stroke; 맘 presses = 멈's), else the element's plain hit
                        char burst = letter == '멈' || letter == '맘' ? '멈' : letter == '넘' ? '넘' : '\0';
                        if (burst != '\0') Pulse(inst, "seed-burst", burst, '\0', Vol2LegacyField.Burst, at, at, rec.TargetId);
                        Pulse(inst, "seed-hit", Vol2LegacyDonors.Of(Vol2LegacyDonors.HitPlain, body), '\0', Vol2LegacyField.Contact, at, at, rec.TargetId);
                        return;
                    }
                case "Return": Pulse(inst, "return-hit", hit, '\0', Vol2LegacyField.Contact, at, at, rec.TargetId); return;
            }
            // a direct touch: on ㅓ (no flying body: the swing's touch) and ㅜ (the 결계 body has no impact) the verb's hit is the only picture of it; 가른다 / 돌아온다 draw theirs everywhere
            if (direct && (Frame == Frame.Attachment || Frame == Frame.Ring || Verb == Verb.Split || Verb == Verb.Return))
                Pulse(inst, "hit", hit, '\0', Vol2LegacyField.Contact, at, at, rec.TargetId);
        }

        // ---- the clock: scheduled ends the executors gave, the trip, the aura ----

        static Vol2ReturnTuning ReturnTuning(Vol2VfxInstance inst)
            => inst.Model.Coordinator != null && inst.Model.Coordinator.Verbs.TryGet(Verb.Return, out var e) && e is Vol2ReturnExecutor r ? r.Tuning : null;

        static Vol2ReturnReport Report(Vol2VfxInstance inst)
            => inst.Model.Coordinator != null && inst.Model.Coordinator.Verbs.TryGet(Verb.Return, out var e) && e is Vol2ReturnExecutor r ? r.ReportOf(inst.Cast) : null;

        public override void Update(Vol2VfxInstance inst, long nowUs)
        {
            base.Update(inst, nowUs);
            var s = St(inst);
            var spec = inst.Log.Spec;
            Body body = B(inst); char letter = L(inst);
            // 가른다: every strip of this cast stands as the element's guard strokes on the stripped body until the strip book's UntilUs (a recrossing renews it)
            if (Verb == Verb.Split && inst.Model.Coordinator != null && inst.Model.Coordinator.Verbs.TryGet(Verb.Split, out var se) && se is Vol2SplitExecutor split)
                foreach (var e in split.Strips.Entries)
                {
                    if (e.Cast != inst.Cast) continue;
                    if (!s.Strips.TryGetValue(e.TargetId, out var sp) || sp.Ended)
                    {
                        if (sp != null && sp.Ended && e.UntilUs <= sp.EndSchedule) continue;
                        Vector3 at = TargetPos(inst, e.TargetId, inst.Origin + inst.Forward * 3f);
                        Vector3 toCaster = Flat(inst.Origin - at); toCaster = toCaster.sqrMagnitude > 1e-4f ? toCaster.normalized : -inst.Forward;
                        sp = Lay(inst, "strip", Vol2LegacyDonors.Of(Vol2LegacyDonors.Guard, body), '\0', Vol2LegacyField.Guard, Ground(inst, at) + toCaster * 0.65f, 1f, null, e.AtUs, e.TargetId, false,
                            Vol2Time.ToSecondsF(e.UntilUs - Math.Max(e.AtUs, nowUs)));
                        sp.EndSchedule = e.UntilUs; s.Strips[e.TargetId] = sp;
                        Pulse(inst, "strip-hit", Vol2LegacyDonors.Of(Vol2LegacyDonors.HitS, body), '\0', Vol2LegacyField.Contact, at, at, e.TargetId);
                    }
                    else if (e.UntilUs > sp.EndSchedule) { sp.EndSchedule = e.UntilUs; sp.F1 = 0f; }
                }
            // the return report's legs: the back leg's body (ㅏ), the moving floor (ㅗ), the breathing ring (ㅜ)
            if (Verb == Verb.Return)
            {
                var rep = Report(inst);
                if (rep != null)
                {
                    if (Frame == Frame.Path && !s.BackFired && rep.BackStartUs > 0 && nowUs >= rep.BackStartUs)
                    {
                        s.BackFired = true;
                        float headEnd = inst.Path != null ? inst.Path.HeadAlong : spec.PathLength;
                        Vector3 far = spec.Origin + spec.Forward * headEnd + Vector3.up * 1.1f, home = inst.Origin + Vector3.up * 1.2f;
                        float back = rep.BackEndUs > rep.BackStartUs ? Vol2Time.ToSecondsF(rep.BackEndUs - rep.BackStartUs) : headEnd / Mathf.Max(1f, spec.PathSpeed);
                        // the turn: the element's ㅇ ring where the head stopped; the way back: the old 풀림 - the path's ink comes apart and is pulled into the brush tip
                        // (the glyph's whole body flown back burst its impact on the caster: rejected in the step 4 probe)
                        Pulse(inst, "turn", Vol2LegacyDonors.Of(Vol2LegacyDonors.HitNg, body), '\0', Vol2LegacyField.Contact, far, far, 0);
                        Pulse(inst, "back-leg", letter, Vol2LegacyDonors.Of(Vol2LegacyDonors.Single, body), Vol2LegacyField.Unravel, far, home, 0, Mathf.Max(0.12f, back));
                    }
                    if (s.Trip != null && !s.Trip.Ended && rep.TripEndUs > 0)
                    {
                        float k = TripFraction(rep, nowUs);
                        if (Frame == Frame.Fan)
                        {
                            float travel = ReturnTuning(inst) is Vol2ReturnTuning rt && rt.FanTravel > 0f ? rt.FanTravel : spec.FanDepth;
                            Place(inst, s.Trip, Ground(inst, spec.Origin + spec.Forward * (spec.FanDepth * 0.5f + travel * k)), s.Trip.F0);
                        }
                        else if (Frame == Frame.Ring)
                        {
                            float r1 = inst.Model.Coordinator != null ? (float)inst.Model.Coordinator.Numbers.Get("space.returnRing.outer") : spec.RingRadius * 2f;
                            Place(inst, s.Trip, spec.Origin, Mathf.Lerp(Mathf.Max(0.5f, spec.RingRadius), r1, k));
                        }
                        if (nowUs >= rep.TripEndUs)
                        {
                            ClosePart(inst, s.Trip, rep.TripEndUs, Vol2EndCode.Expire, inst.Forward);
                            // home: what went out comes apart into the brush (the old 풀림)
                            Pulse(inst, "home", letter, Vol2LegacyDonors.Of(Vol2LegacyDonors.Single, body), Vol2LegacyField.Unravel, s.Trip.Pos + Vector3.up * 0.4f, inst.Origin + Vector3.up * 1.2f, 0, 0.35f);
                        }
                    }
                }
            }
            // 곡: the fan footprint grows over its life (the footprint's own scale on the scope clock)
            foreach (var (role, p) in s.Laid)
            {
                if (p.Ended) continue;
                if (p.Mat != null)
                {
                    p.BuiltExpiresUs = p.Mat.ExpiresUs;                                         // the model reads it live; this part draws no mesh
                    if (role == "residue" && p.Mat.Footprint != null && p.Mat.Footprint.Kind == Vol2FootprintKind.Fan && p.Mat.Footprint.GrowLinear != 1f)
                        Place(inst, p, Ground(inst, CentreOf(p.Mat.Footprint, nowUs)), RadiusOf(p.Mat.Footprint, nowUs));
                    // a material that ended without this instance hearing it (the model routes its events only while the instance lives): the executor's end, read back
                    if (!p.Mat.Alive && p.Mat.End != Vol2MaterialEnd.Aborted) { ClosePart(inst, p, p.Mat.EndedUs, Vol2EndCodes.From(p.Mat.End), inst.Forward); continue; }
                    WarnIfDue(inst, p, p.Mat.ExpiresUs, nowUs, WarnLeadSeconds);
                }
                else if (role == "strip")
                {
                    WarnIfDue(inst, p, p.EndSchedule, nowUs, WarnLeadSeconds);
                    if (p.EndSchedule > 0 && nowUs >= p.EndSchedule) ClosePart(inst, p, p.EndSchedule, Vol2EndCode.Expire, inst.Forward);
                }
            }
            // the cast-tied parts: the ㅓ aura (and the orbit ring) end with the attachment - its host's end, after every material it carries on the caster ended;
            // the ㅜ timed ring ends with its emitter
            var em = inst.Log.Emitter;
            if (Frame == Frame.Attachment && s.Aura != null && !s.Aura.Ended)
            {
                bool hostOver = em == null || em.Finished || inst.Log.Finished;
                bool carried = false; Vol2Material lastAttached = null;
                foreach (var m in inst.Log.Materials)
                    if (m.Attached) { if (m.Alive) carried = true; else if (lastAttached == null || m.EndedUs >= lastAttached.EndedUs) lastAttached = m; }
                // a state the caster carried (a residue / seed on the body) is over when it ended (worked, ran out); otherwise the attachment's own end
                if ((hostOver || lastAttached != null) && !carried)
                {
                    long at = Math.Min(nowUs, Math.Max(em is Vol2AttachmentHost h ? h.EndUs : nowUs, lastAttached != null ? lastAttached.EndedUs : 0));
                    var code = lastAttached != null ? Vol2EndCodes.From(lastAttached.End) : Vol2EndCode.Expire;
                    ClosePart(inst, s.Aura, at, code == Vol2EndCode.None ? Vol2EndCode.Expire : code, inst.Forward);
                    if (s.Ring != null) ClosePart(inst, s.Ring, at, Vol2EndCode.Expire, inst.Forward);
                }
                else if (em is Vol2AttachmentHost host2 && !carried) WarnIfDue(inst, s.Aura, host2.EndUs, nowUs, WarnLeadSeconds);
            }
            if (Frame == Frame.Ring && s.Ring != null && !s.Ring.Ended && (em == null || em.Finished))
                ClosePart(inst, s.Ring, nowUs, Vol2EndCode.Expire, inst.Forward);
            if (Frame == Frame.Ring && s.Ring != null && em is Vol2TimedEmitter te) WarnIfDue(inst, s.Ring, te.EndUs, nowUs, WarnLeadSeconds);
            // the cast is over and something is still open with nothing left to end it (the cast was refused / ran out before its state appeared): it dries now
            // (a material-tied part waits for its material's end; a strip for its UntilUs)
            if (inst.Log.Finished || inst.CastIsEnded)
                foreach (var (role, p) in s.Laid)
                {
                    if (p.Ended || p.Mat != null) continue;
                    if (role == "strip" && p.EndSchedule > 0) continue;
                    ClosePart(inst, p, nowUs, Vol2EndCode.Expire, inst.Forward);
                }
            inst.Dirty = false;
        }

        /// <summary>0 at the start of a trip, 1 at its turn, 0 again at its end (the report's legs on the scope clock).</summary>
        static float TripFraction(Vol2ReturnReport rep, long nowUs)
        {
            long start = rep.OutEndUs - rep.LegUs;
            if (nowUs <= rep.OutEndUs) return Mathf.Clamp01((float)((nowUs - start) / (double)Math.Max(1, rep.OutEndUs - start)));
            if (nowUs < rep.BackStartUs) return 1f;
            return 1f - Mathf.Clamp01((float)((nowUs - rep.BackStartUs) / (double)Math.Max(1, rep.BackEndUs - rep.BackStartUs)));
        }

        public override void OnRetired(Vol2VfxInstance inst, bool aborted)
        {
            base.OnRetired(inst, aborted);
            _st.Remove(inst);
        }
    }
}
