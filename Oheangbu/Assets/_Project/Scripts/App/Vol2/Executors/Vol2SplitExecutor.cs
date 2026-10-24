using System;
using System.Collections.Generic;
using System.Globalization;
using Oheangbu.Combat;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>가른다 (S5-b slice 6, table rows ㅅ): the shape touches what it touches NOW (the instant term, per body, like 한 번) and SPLITS.
    /// Three things happen, none of them damage beyond the instant term:
    ///  - the MATERIALS the shape passes over are cut in two (Vol2MaterialRegistry.TrySplit, reaction.split.parts = 2): the parent keeps its id, life and
    ///    lineage and holds half, the child is a new material of the same cast / owner / expiry holding the other half - the halves add up to what it
    ///    held, no amount is made or lost, no other material is created (the 솟 residue is the one exception). Gate doors, the body-attached
    ///    carriers and the cast's own materials are not split; one material is split once per cast.
    ///  - the touched body's guard and armour are STRIPPED for combat.strip.duration (3 s; 숫 4.5 s): a timed state (Vol2StripBook on the scope
    ///    clock, mirrored to the enemy's own armour state by the strip port), never HP. The 0.4 of the plan is a value conversion the budget
    ///    books ONCE (ContactKind.Shared / EffectKind.Strip, the first stripped body): "첫 대상 한 번만 환산". Every other stripped body is state only.
    ///  - the body's own action (bind / burn / push / cut / wet) lands on the first body the cast hits, once, like every verb.
    /// Frames: path = the head's swept span splits the materials it crosses and strips the first body (the path stops there; 삿 goes on and strips
    /// up to three); fan = every material in the fan, every body in it; attachment = the materials the caster passes through in the 6 s, every body
    /// the swing touches (three at most, the rules' target cap), the 날붓 posture on each direct hit; ring = a 10 s line (timing.s.ringLife, NOT a
    /// material): every material on the band, every body that crosses it (a recrossing strips again, TE-S6-8).
    /// Peaks: 삿 the first body takes the cut x1.5 (1.05), strips up to three; 섯 two swings each cut front AND back: the back cut is the rules' rear
    /// term (0.35 = half of 0.7) applied to the same body with each of its two contacts (total 2.1 with the strip), and the swing's mirrored capsule
    /// behind the caster splits the materials standing there; 솟 after the split leaves a metal residue (0.35, 4 s, a fan) that cuts what walks into
    /// it once; 숫 strips 4.5 s and asks the field to cut a rock / iron gate (recorded: IVol2FieldPort has no gate method yet).
    /// What a verb run may not do and does not: create a material (except 솟), reset a life, split a child, pay the 0.4 twice, damage by the strip.</summary>
    public sealed class Vol2SplitExecutor : IVol2VerbExecutor
    {
        public Verb Verb => Verb.Split;
        public string Name => "Split(가른다)";

        public Vol2SplitTuning Tuning { get; }
        /// <summary>The scope clock's strip state (fixtures and the lab read it).</summary>
        public Vol2StripBook Strips { get; } = new Vol2StripBook();
        /// <summary>The game's side of a strip. Default: the enemy's own armour state.</summary>
        public IVol2StripPort StripPort { get; set; } = new Vol2EnemyStripPort();
        /// <summary>The posture side of the 날붓. null = resolved lazily from the combat wiring's defence kit (<see cref="ResolveBlade"/>); the main agent may bind it
        /// explicitly (<c>new Vol2PostureBladePort(kit.Posture)</c>), a fixture binds a fake.</summary>
        public IVol2BladePort Blade { get; set; }

        readonly Dictionary<long, Vol2SplitCastData> _data = new Dictionary<long, Vol2SplitCastData>();
        readonly Queue<long> _order = new Queue<long>();
        Vol2PostureBridge _autoFor; Vol2PostureBladePort _auto;

        public Vol2SplitExecutor() : this(null) { }
        public Vol2SplitExecutor(Vol2SplitTuning tuning) { Tuning = tuning ?? new Vol2SplitTuning(); }

        public IVol2VerbRun Begin(Vol2CastRun run) => new SplitRun(this, run, NewData(run.Cast));

        /// <summary>What a 가른다 cast did besides its contacts. null = unknown cast (or forgotten: the last 512 are kept).</summary>
        public Vol2SplitCastData DataOf(CastId cast) => _data.TryGetValue(cast.Value, out var d) ? d : null;

        Vol2SplitCastData NewData(CastId cast)
        {
            var d = new Vol2SplitCastData { Cast = cast };
            _data[cast.Value] = d; _order.Enqueue(cast.Value);
            while (_order.Count > 512) _data.Remove(_order.Dequeue());
            return d;
        }

        /// <summary>The 날붓 port: the explicit one, else the defence kit of the combat wiring in the scene (found lazily, never cached past the kit). null in an
        /// edit-mode fixture without a wiring.</summary>
        internal IVol2BladePort ResolveBlade()
        {
            if (Blade != null) return Blade;
            var wiring = UnityEngine.Object.FindFirstObjectByType<CombatLoopWiring>();
            var kit = wiring != null ? wiring.Vol2Defence : null;
            if (kit == null) return null;
            if (_auto == null || !ReferenceEquals(_autoFor, kit.Posture)) { _autoFor = kit.Posture; _auto = new Vol2PostureBladePort(kit.Posture); }
            return _auto;
        }

        sealed class SplitRun : Vol2VerbRunBase
        {
            readonly Vol2SplitExecutor _ex;
            readonly Vol2SplitCastData _data;
            readonly bool _pierce, _double, _metal, _gate;
            readonly long _stripUs;
            readonly HashSet<long> _splitIds = new HashSet<long>();
            readonly HashSet<long> _stripped = new HashSet<long>();
            readonly HashSet<long> _ringTouched = new HashSet<long>();
            readonly HashSet<long> _onBand = new HashSet<long>();
            readonly HashSet<long> _resTouched = new HashSet<long>();
            readonly HashSet<long> _bladeAttacks = new HashSet<long>();
            readonly HashSet<(long swing, long target, string kind)> _bladeSeen = new HashSet<(long, long, string)>();
            bool _valuePaid, _casterInit, _subscribed;
            int _bodies;
            float _lastAlong;
            Vector3 _prevCaster;
            Vol2Material _res;
            IVol2BladePort _blade;
            Action<Vol2SwingEvent> _swing;

            public SplitRun(Vol2SplitExecutor ex, Vol2CastRun run, Vol2SplitCastData data) : base(run)
            {
                _ex = ex; _data = data;
                char c = run.Glyph.Letter;
                _pierce = c == '삿'; _double = c == '섯'; _metal = c == '솟'; _gate = c == '숫';
                _stripUs = RuleTypesV3.Microseconds(Numbers.Get(_gate ? "combat.strip.peakDuration" : "combat.strip.duration"));
            }

            Vol2SplitTuning T => _ex.Tuning;

            public override IVol2ShapeEmitter CreateEmitter(Vol2ShapeSpec spec, long nowUs)
            {
                switch (spec.Kind)
                {
                    case Vol2ShapeKind.Path: return new Vol2PathEmitter(spec, nowUs, Run.Plan.FirstOnly, Host.Wall, Host.Tuning.ChestHeight);
                    case Vol2ShapeKind.Fan: return new Vol2FanEmitter(spec, nowUs);
                    case Vol2ShapeKind.Ring: return new Vol2TimedEmitter(spec, nowUs, Run.Plan.LifeUs);
                    default: return new Vol2AttachmentHost(spec, nowUs, Run.Plan.LifeUs);
                }
            }

            public override void Start(long nowUs, IReadOnlyList<IVol2Target> targets)
            {
                var spec = Run.Spec;
                switch (spec.Kind)
                {
                    case Vol2ShapeKind.Fan:
                        SplitWhere(nowUs, "fan", s => Vol2SplitGeometry.FanHit(spec, s), m => spec.Forward);
                        if (_metal) MakeMetal(nowUs);
                        break;
                    case Vol2ShapeKind.Ring:
                        if (_gate)
                            Host.FieldRequest(Run, "cut rock/iron gate r=" + spec.RingRadius.ToString("0.##", CultureInfo.InvariantCulture) + " at (" + spec.Origin.x.ToString("0.##", CultureInfo.InvariantCulture) + ", " + spec.Origin.z.ToString("0.##", CultureInfo.InvariantCulture) + ")",
                                f => "no gate API on IVol2FieldPort: recorded only");
                        break;
                    case Vol2ShapeKind.Attachment:
                        _prevCaster = Host.Caster.Position; _casterInit = true;
                        if (T.BladeOnDirectHits) _blade = _ex.ResolveBlade();
                        if (_double) { _swing = OnSwing; Host.Swings.Contact += _swing; _subscribed = true; }
                        break;
                }
            }

            public override void OnContact(Vol2ShapeContact c, long nowUs)
            {
                switch (Run.Spec.Kind)
                {
                    case Vol2ShapeKind.Path:
                        if (_pierce)
                        {
                            if (T.PierceStopsAtCount && _bodies >= (int)Numbers.Get("peak.삿.pierceCount")) return;
                            _bodies++;
                            var first = TouchFull(c, nowUs);          // body 1 is paid (1.05); bodies 2 and 3 are refused FirstDamageOnly by the rules and are stripped all the same
                            if (first.BudgetAccepted || _bodies > 1) StripBody(c.Target, c, nowUs, false);
                        }
                        else if (TouchFull(c, nowUs).BudgetAccepted) StripBody(c.Target, c, nowUs, false);
                        break;
                    case Vol2ShapeKind.Fan:
                        if (TouchFull(c, nowUs).BudgetAccepted) StripBody(c.Target, c, nowUs, false);
                        break;
                    case Vol2ShapeKind.Attachment:
                        {
                            var rec = TouchFull(c, nowUs);
                            if (!rec.BudgetAccepted) break;
                            StripBody(c.Target, c, nowUs, false);       // once per body (the set)
                            BladeHit(c, rec, nowUs);
                            break;
                        }
                }
            }

            public override void Tick(long nowUs, IReadOnlyList<IVol2Target> targets)
            {
                var spec = Run.Spec;
                switch (spec.Kind)
                {
                    case Vol2ShapeKind.Path:
                        {
                            float head = ((Vol2PathEmitter)Run.Emitter).HeadAlong;
                            if (head >= _lastAlong)
                            {
                                float from = _lastAlong;
                                SplitWhere(nowUs, "path", s => Vol2SplitGeometry.PathHit(spec, from, head, s), m => spec.Forward);
                                _lastAlong = head;
                            }
                            break;
                        }
                    case Vol2ShapeKind.Ring:
                        if (!Run.Emitter.Finished)
                        {
                            SplitWhere(nowUs, "ring", s => Vol2SplitGeometry.RingHit(spec, s), m => Vol2ShapeGeometry.FlatUnit(m.Footprint.Centre - spec.Origin));
                            RingBodies(nowUs, targets);
                        }
                        break;
                    case Vol2ShapeKind.Attachment:
                        if (!Run.Emitter.Finished)
                        {
                            Vector3 pos = Host.Caster.Position;
                            if (!_casterInit) { _prevCaster = pos; _casterInit = true; }
                            Vector3 a = _prevCaster; _prevCaster = pos;
                            SplitWhere(nowUs, "caster", s => Vol2SplitGeometry.SegmentHit(a, pos, T.CasterRadius, s), m => spec.Forward);
                        }
                        break;
                    case Vol2ShapeKind.Fan:
                        if (_metal && _res != null && _res.Alive)
                            for (int i = 0; i < targets.Count && _res.Alive; i++)
                            {
                                var t = targets[i];
                                if (t != null && t.Alive && !_resTouched.Contains(t.Id) && _res.Footprint.Touches(t, nowUs)) MetalTouch(t, nowUs);
                            }
                        break;
                }
            }

            public override bool Idle(long nowUs)
            {
                if (_metal && _res != null && _res.Alive) return false;
                Unsubscribe();
                return true;
            }

            public override void Abort(long nowUs) { Unsubscribe(); }

            void Unsubscribe()
            {
                if (_subscribed && _swing != null) Host.Swings.Contact -= _swing;
                _subscribed = false;
            }

            // ---- the touch (the base one drops the rear term, so this one pays it) ----

            Vol2ContactRecord TouchFull(Vol2ShapeContact c, long nowUs)
            {
                var attack = Host.NewAttack(Run, c.SwingId, nowUs);
                var rec = Host.NewRecord(Run, c, attack, "Touch");
                var step = Run.Budget.Pay(attack.Value, ContactKind.Touch, EffectKind.Instant, 0m, TriggerKind.None, c.TargetId, c.LifeRevision, c.SwingId, nowUs, 0);
                rec.BudgetAccepted = step.Accepted; rec.BudgetReason = step.Reason;
                if (step.Accepted)
                {
                    int ordinal = 0; decimal rear = 0m;
                    foreach (var cmd in step.Commands)
                    {
                        if (cmd.Effect == EffectKind.Rear) { rear += cmd.ActionAmount; continue; }
                        if (cmd.Effect != EffectKind.Instant && cmd.Effect != EffectKind.Second) continue;
                        rec.Kind = cmd.Effect == EffectKind.Second ? "Second" : "Instant";
                        Host.Hit(Run, c.Target, rec, cmd.ActionAmount, attack, nowUs, c.Point, c.SwingId, ordinal++);
                        FirstBody(c.Target, rec, attack, nowUs);
                    }
                    if (rear > 0m) RearHit(c, rear, nowUs);
                }
                Host.NotifyContact(Run.Log, rec);
                return rec;
            }

            /// <summary>섯: the back cut of the swing (TE-S6-4), a hit of its own on the same body.</summary>
            void RearHit(Vol2ShapeContact c, decimal amount, long nowUs)
            {
                var attack = Host.NewAttack(Run, c.SwingId, nowUs);
                var rec = Host.NewRecord(Run, c, attack, "Rear");
                rec.BudgetAccepted = true; rec.BudgetReason = "RearShare";
                Host.Hit(Run, c.Target, rec, amount, attack, nowUs, c.Point, c.SwingId, 0);
                _data.RearHits++;
                Host.NotifyContact(Run.Log, rec);
            }

            // ---- the strip ----

            /// <summary>Strips one body: a timed state, never HP. The first strip of the cast books the plan's 0.4 as a value (Shared / Strip); a
            /// <paramref name="restrip"/> (a second ring crossing) only renews the state.</summary>
            void StripBody(IVol2Target t, Vol2ShapeContact c, long nowUs, bool restrip)
            {
                if (t == null || !t.Alive || t.LifeRevision != c.LifeRevision) return;
                if (!restrip && !_stripped.Add(t.Id)) return;
                var attack = Host.NewAttack(Run, c.SwingId, nowUs);
                var rec = Host.NewRecord(Run, c, attack, "Strip");
                if (!Host.Services.Commands.TryBegin(new CommandKey(Host.Services.Ids.ScopeId, CommandRuleKind.Status, attack.Value, 1))) { rec.DuplicateCommands++; Host.NotifyContact(Run.Log, rec); return; }
                decimal value = 0m;
                if (!restrip && !_valuePaid)
                {
                    var step = Run.Budget.Pay(attack.Value, ContactKind.Shared, EffectKind.Strip, Run.Plan.Term(EffectKind.Strip), TriggerKind.None, t.Id, t.LifeRevision, c.SwingId, nowUs, attack.Value);
                    rec.BudgetAccepted = step.Accepted; rec.BudgetReason = step.Reason;
                    if (step.Accepted && step.Commands.Count > 0) { _valuePaid = true; value = step.Commands[0].UtilityValue; rec.Amount = value; }
                }
                else { rec.BudgetAccepted = true; rec.BudgetReason = restrip ? "StripRenewed" : "StripState"; }
                string note = _ex.StripPort != null ? _ex.StripPort.Apply(t, _stripUs, nowUs) : "NoStripPort";
                var entry = _ex.Strips.Apply(t, Run.Cast, nowUs, _stripUs, value, restrip, note);
                _data.Strips.Add(entry);
                rec.DamageNote = "strip " + Vol2Time.ToSecondsF(_stripUs).ToString("0.##", CultureInfo.InvariantCulture) + " s until " + entry.UntilUs + " us [" + note + "]";
                Host.NotifyContact(Run.Log, rec);
            }

            void RingBodies(long nowUs, IReadOnlyList<IVol2Target> targets)
            {
                var spec = Run.Spec;
                for (int i = 0; i < targets.Count; i++)
                {
                    var t = targets[i];
                    if (t == null || !t.Alive) continue;
                    bool on = Vol2ShapeGeometry.RingCrossed(spec.Origin, spec.RingInner, spec.RingOuter, t.Previous, t.Position, t.Radius);
                    if (!on) { _onBand.Remove(t.Id); continue; }
                    if (!_onBand.Add(t.Id)) continue;                                     // still on the band: not a new crossing
                    var c = MakeContact(t, nowUs, 0, Flat(spec.Origin, t.Position));
                    if (_ringTouched.Add(t.Id)) { if (TouchFull(c, nowUs).BudgetAccepted) StripBody(t, c, nowUs, false); }
                    else if (T.RecrossRestrips && _stripped.Contains(t.Id)) StripBody(t, c, nowUs, true);
                }
            }

            // ---- the 날붓 ----

            void BladeHit(Vol2ShapeContact c, Vol2ContactRecord rec, long nowUs)
            {
                if (!T.BladeOnDirectHits || (rec.Kind != "Instant" && rec.Kind != "Second")) return;
                if (!_bladeAttacks.Add(rec.Attack.Value) || !_bladeSeen.Add((c.SwingId, c.TargetId, rec.Kind))) { _data.BladeDuplicates++; return; }
                var port = _blade ?? (_blade = _ex.ResolveBlade());
                var ev = new Vol2BladeEvent
                {
                    Attack = rec.Attack, TargetId = c.TargetId, TargetName = c.Target != null ? c.Target.Name : "", Kind = rec.Kind,
                    Result = port != null && c.Target != null ? port.Hit(c.Target, Run.Glyph, nowUs) : Vol2BladeResult.None("NoBladePort")
                };
                _data.Blades.Add(ev);
                Run.Log.Notes.Add("blade " + ev.TargetName + " " + ev.Kind + " " + ev.Result);
            }

            // ---- 섯: the swing cuts front and back ----

            void OnSwing(Vol2SwingEvent e)
            {
                var host = Run.Emitter as Vol2AttachmentHost;
                if (host == null || !host.Open(e.NowUs)) return;
                Vector3 rear = e.Sleeve - (e.Tip - e.Sleeve);
                float rad = Run.Spec.SwingRadius;
                SplitWhere(e.NowUs, "swing-front", s => Vol2SplitGeometry.SegmentHit(e.Sleeve, e.Tip, rad, s), m => e.Tip - e.Sleeve);
                SplitWhere(e.NowUs, "swing-rear", s => Vol2SplitGeometry.SegmentHit(e.Sleeve, rear, rad, s), m => e.Tip - e.Sleeve);
            }

            // ---- 솟: the metal that stands on the split ----

            void MakeMetal(long nowUs)
            {
                var attack = Host.NewAttack(Run, 0, nowUs);
                long materialId = Host.Services.Ids.NextMaterial().Value;
                var step = Run.Budget.Pay(attack.Value, ContactKind.Transfer, EffectKind.Residue, Run.Plan.Term(EffectKind.Residue), TriggerKind.None, WorldTargetId, 0, 0, nowUs, materialId);
                if (!step.Accepted || step.Commands.Count == 0) { Run.Log.Notes.Add("metal residue refused by the budget: " + step.Reason); return; }
                long life = RuleTypesV3.Microseconds(Numbers.Get("peak.솟.residueLife"));
                _res = Host.Materials.Create(Run.Cast, Run.Glyph.Letter, Run.Glyph.Body, Vol2MaterialKind.Residue, step.Commands[0].ChildBudget, life, Vol2Footprint.Fan(Run.Spec),
                    Vol2Faction.Player, Vol2Faction.Player, true, nowUs, 0, materialId);
                Run.Log.Materials.Add(_res);
            }

            /// <summary>A body walks into the metal: it draws its share once (TE-S6-7) and is cut.</summary>
            void MetalTouch(IVol2Target t, long nowUs)
            {
                if (_res == null || !_res.Alive || !_resTouched.Add(t.Id)) return;
                var attack = Host.NewAttack(Run, 0, nowUs);
                var rec = Host.NewRecord(Run, MakeContact(t, nowUs), attack, "Residue");
                rec.MaterialId = _res.Id.Value;
                decimal want = Math.Min(_res.Initial * Host.VerbTuning.ResidueShareFraction, _res.Remaining);
                if (!Host.Materials.TryDebit(_res.Id, want, nowUs, out decimal taken)) { rec.BudgetReason = "ResidueGone"; Host.NotifyContact(Run.Log, rec); return; }
                rec.BudgetAccepted = true; rec.BudgetReason = "ResidueShare";
                Host.Hit(Run, t, rec, taken, attack, nowUs, t.Position, 0, 0);
                FirstBody(t, rec, attack, nowUs);
                Host.NotifyContact(Run.Log, rec);
            }

            // ---- splitting materials ----

            bool Splittable(Vol2Material m, long nowUs)
                => m.Alive && m.Cast != Run.Cast && !m.Attached && !m.GateDoor && m.Kind != Vol2MaterialKind.Carrier && m.Remaining > 0m && nowUs < m.ExpiresUs && !_splitIds.Contains(m.Id.Value);

            void SplitWhere(long nowUs, string where, Func<List<Vol2Sample>, bool> touches, Func<Vol2Material, Vector3> cutDir)
            {
                var mats = Host.Materials.AliveSnapshot();
                if (mats.Count == 0) return;
                mats.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
                foreach (var m in mats)
                {
                    if (!Splittable(m, nowUs)) continue;
                    var samples = Vol2SplitGeometry.Samples(m.Footprint, nowUs, T);
                    if (samples.Count == 0 || !touches(samples)) continue;
                    SplitOne(m, cutDir(m), nowUs, where);
                }
            }

            void SplitOne(Vol2Material m, Vector3 dir, long nowUs, string where)
            {
                int parts = (int)Numbers.Get("reaction.split.parts");
                decimal before = m.Remaining;
                var fp = m.Footprint;
                if (!Host.Materials.TrySplit(m.Id, parts, nowUs, out var kids) || kids == null) return;
                _splitIds.Add(m.Id.Value);
                var rec = new Vol2SplitRecord { Cast = Run.Cast, AtUs = nowUs, Parent = m, Before = before, Where = where };
                foreach (var k in kids) { _splitIds.Add(k.Id.Value); rec.Children.Add(k); }
                if (fp.Kind == Vol2FootprintKind.Disc) PlaceHalves(m, kids, fp, dir, nowUs);
                _data.Splits.Add(rec);
                var inv = CultureInfo.InvariantCulture;
                Run.Log.Notes.Add("split " + m.Id + (m.Tag.Length > 0 ? "[" + m.Tag + "]" : "") + " by " + where + ": " + before.ToString(inv) + " -> " + m.Remaining.ToString(inv) + " + " + string.Join(" + ", System.Linq.Enumerable.Select(kids, k => k.Id + ":" + k.Remaining.ToString(inv))));
            }

            /// <summary>TE-S6-1: the halves of a disc stand either side of the cut line.</summary>
            void PlaceHalves(Vol2Material parent, List<Vol2Material> kids, Vol2Footprint fp, Vector3 dir, long nowUs)
            {
                Vector3 d = Vol2ShapeGeometry.FlatUnit(dir), perp = Vector3.Cross(Vector3.up, d);
                int n = kids.Count + 1;
                float off = fp.Radius * T.HalfOffsetFraction, hr = n == 2 ? fp.Radius * T.HalfRadiusScale : fp.Radius / Mathf.Sqrt(n);
                for (int i = 0; i < n; i++)
                {
                    var part = i == 0 ? parent : kids[i - 1];
                    float lateral = (i / (float)(n - 1) - 0.5f) * 2f * off;
                    Host.Materials.TryMove(part.Id, Vol2Footprint.Disc(fp.Origin + perp * lateral, hr), nowUs);
                }
            }
        }
    }
}
