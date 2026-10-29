using System;
using System.Collections.Generic;
using System.Globalization;
using Oheangbu.Combat;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>심는다 (S5-b slice 5, table rows ㅁ): nothing is struck when the shape is cast - a SEED is planted and WAITS (timing.seed.life, 8 s of the
    /// cast), and it works ONCE when its own condition comes (the activation term 1.2 per body; 몸 1.8). The condition depends on the frame:
    ///   ㅏ (P3) the path passes through bodies and plants a seed on each (the first three; a fourth is refused TargetCap); a seed works when the OWNER's
    ///           later direct hit (another cast's paid first / second contact) or 갈무리 reaches that body - and then the body takes posture 60 (ㅏ+ㅁ only);
    ///   ㅓ (A3) one seed on the caster's body (no place in the world); it works the next time the player really loses HP (not a parry, not a dodge): on up
    ///           to three enemies within 2 m, nearest first. 응보 gives nothing back (no HP, ink, posture). 멈 also exempts THAT hit's body stagger
    ///           (decided inside the hit confirmation, before the stagger plays - <see cref="Vol2SeedHitGate"/>);
    ///   ㅗ (F3) the seed is the fan, standing for its life; it works when a body ENTERS (TE-S5-2), on every body in the fan at that moment;
    ///   ㅜ (R3) the same on the ring band: a body crossing the band triggers it.
    /// Peaks (earth): 맘 (path) the struck body is pressed to the ground - movement x0 for 1.5 s (boss 0.75 s) + the 1.2 hit; 멈 (attachment) the retribution is
    /// the earth push on up to three enemies + the stagger exemption of that hit only; 몸 (fan) the seed is a giant (concurrent 1: a new giant replaces
    /// the standing one) that strikes once on entry (1.8) and collapses into an earth material fan (amount 0, 10 s); 뭄 (ring) works when the CASTER steps
    /// onto the band (not an enemy): the ring hardens for 8 s, a non-damage axis - enemies inside it cannot dash or jump (<see cref="Vol2SeedWorld"/>), and the
    /// field is asked for the bridge / ground (OnPlatform).
    /// 信: while a letter is being drawn the cast's un-triggered seeds do not wait (their expiry and the budget's clock move together), at most 4 s per cast.
    /// A seed that is never triggered just ends (Expired). The budget is the rules' (Plant / Activate contacts); the material registry holds the seeds.</summary>
    public sealed class Vol2SeedExecutor : IVol2VerbExecutor
    {
        public const string TagSeed = "seed", TagGiant = "giant", TagRubble = "giant-rubble", TagGround = "hardground";

        readonly Dictionary<Vol2CastCoordinator, Vol2SeedHub> _hubs = new Dictionary<Vol2CastCoordinator, Vol2SeedHub>();

        public Verb Verb => Verb.Seed;
        public string Name => "Seed(심는다)";

        /// <summary>The per-scope state and ports of this executor for one coordinator (made on first use).</summary>
        public Vol2SeedHub HubFor(Vol2CastCoordinator host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (!_hubs.TryGetValue(host, out var hub)) { hub = new Vol2SeedHub(host); _hubs[host] = hub; }
            return hub;
        }

        /// <summary>The hub of the coordinator's registered 심는다 executor; null when the coordinator has none.</summary>
        public static Vol2SeedHub HubOf(Vol2CastCoordinator coordinator)
            => coordinator != null && coordinator.Verbs.TryGet(Verb.Seed, out var ex) && ex is Vol2SeedExecutor se ? se.HubFor(coordinator) : null;

        public IVol2VerbRun Begin(Vol2CastRun run) => new SeedRun(run, HubFor(run.Host));

        sealed class Seed
        {
            public Vol2Material Mat;
            public IVol2Target Host;
            public long HostRev;
            public long PlantedBudgetUs;
            public bool Fired, Gone;
            public HashSet<long> Inside = new HashSet<long>();
            public bool Waiting => !Fired && !Gone && Mat != null && Mat.Alive;
        }

        sealed class SeedRun : Vol2VerbRunBase, IVol2SeedListener
        {
            readonly Vol2SeedHub _hub;
            readonly Frame _frame;
            readonly char _letter;
            readonly bool _press, _retaliate, _giant, _ground;
            readonly List<Seed> _seeds = new List<Seed>();
            readonly List<Vol2Material> _after = new List<Vol2Material>();
            long _paused, _lastTickUs, _budgetFloor;
            bool _registered;
            Vector3 _casterPrev; bool _casterPrevSet;

            public SeedRun(Vol2CastRun run, Vol2SeedHub hub) : base(run)
            {
                _hub = hub; _frame = run.Glyph.Frame; _letter = run.Glyph.Letter;
                _press = _letter == '맘'; _retaliate = _letter == '멈'; _giant = _letter == '몸'; _ground = _letter == '뭄';
            }

            public CastId CastOf => Run.Cast;

            // ---- the budget's clock: real time minus what 信 held, never backwards ----

            long Budget(long realUs, long atLeast = 0)
            {
                long t = Math.Max(Math.Max(realUs - _paused, _budgetFloor), atLeast);
                if (t > _budgetFloor) _budgetFloor = t;
                return t;
            }

            void Fact(string kind, string trigger, IVol2Target target, long nowUs, string detail, decimal value = 0m, long materialId = 0, Vol2SeedPosture posture = default)
            {
                _hub.Record(Run.Cast, new Vol2SeedRecord { Kind = kind, Trigger = trigger ?? "", TargetId = target != null ? target.Id : 0, TargetName = target != null ? target.Name : "", AtUs = nowUs, Detail = detail ?? "", Value = value, MaterialId = materialId, Posture = posture });
                if (kind != "Posture") Run.Log.Notes.Add(kind + (target != null ? " " + target.Name : "") + (detail != null && detail.Length > 0 ? ": " + detail : ""));
            }

            // ---- the shape ----

            public override IVol2ShapeEmitter CreateEmitter(Vol2ShapeSpec spec, long nowUs)
            {
                switch (spec.Kind)
                {
                    case Vol2ShapeKind.Path: return new Vol2PathEmitter(spec, nowUs, false, Host.Wall, Host.Tuning.ChestHeight);
                    case Vol2ShapeKind.Fan:
                    case Vol2ShapeKind.Ring: return new Vol2TimedEmitter(spec, nowUs, Run.Plan.LifeUs);
                    default: return new Vol2AttachmentHost(spec, nowUs, Run.Plan.LifeUs);
                }
            }

            public override void Start(long nowUs, IReadOnlyList<IVol2Target> targets)
            {
                _lastTickUs = nowUs;
                Vol2SeedWiring.EnsureBound(_hub);       // Play: take the kit, the player and the drawing input from the scope if nobody bound them yet
                _hub.Register(this); _registered = true;
                switch (_frame)
                {
                    case Frame.Attachment:
                        MakeSeed(Vol2Footprint.Attached(Run.Spec.Origin), nowUs, null, TagSeed);
                        break;
                    case Frame.Fan:
                        {
                            if (_giant) ReplaceGiants(nowUs);
                            var s = MakeSeed(Vol2Footprint.Fan(Run.Spec), nowUs, null, _giant ? TagGiant : TagSeed);
                            s.Inside = TouchingIds(s, targets, nowUs);
                            break;
                        }
                    case Frame.Ring:
                        {
                            var s = MakeSeed(Vol2Footprint.Ring(Run.Spec), nowUs, null, TagSeed);
                            s.Inside = TouchingIds(s, targets, nowUs);
                            _casterPrev = _hub.CasterPosition; _casterPrevSet = true;
                            break;
                        }
                }
            }

            Seed MakeSeed(Vol2Footprint footprint, long nowUs, IVol2Target host, string tag)
            {
                // the seed waits as long as the CAST lives (plan.LifeUs from the cast): the budget refuses an activation after that, so the material must not outlive it
                long life = Math.Max(1, Run.StartUs + Run.Plan.LifeUs - nowUs);
                var m = Host.Materials.Create(Run.Cast, _letter, Run.Glyph.Body, Vol2MaterialKind.Mark, 0m, life, footprint, Vol2Faction.Player, Vol2Faction.Player, false, nowUs, 0, 0, tag);
                Run.Log.Materials.Add(m);
                var s = new Seed { Mat = m, Host = host, HostRev = host != null ? host.LifeRevision : 0, PlantedBudgetUs = Budget(nowUs) };
                _seeds.Add(s);
                return s;
            }

            void ReplaceGiants(long nowUs)
            {
                if (!_hub.Tuning.GiantReplaces) return;
                foreach (var m in Host.Materials.AliveSnapshot())
                    if (m.Tag == TagGiant && m.Cast != Run.Cast && Host.Materials.TryEnd(m.Id, Vol2MaterialEnd.Replaced, nowUs))
                        Fact("Replaced", "", null, nowUs, "the standing giant " + m.Id + " (cast " + m.Cast + ") is replaced: 동시 1", 0m, m.Id.Value);
            }

            // ---- ㅏ: planting ----

            public override void OnContact(Vol2ShapeContact c, long nowUs)
            {
                if (_frame != Frame.Path) return;     // ㅓ: a swing touches nothing (the seed is not thrown); ㅗ / ㅜ never report contacts
                var attack = Host.NewAttack(Run, 0, nowUs);
                var rec = Host.NewRecord(Run, c, attack, "Plant");
                long bt = Budget(nowUs);
                var step = Run.Budget.Pay(attack.Value, ContactKind.Plant, EffectKind.Activation, 0m, TriggerKind.None, c.TargetId, c.LifeRevision, 0, bt, 0);
                rec.BudgetAccepted = step.Accepted; rec.BudgetReason = step.Reason;
                if (step.Accepted)
                {
                    var s = MakeSeed(Vol2Footprint.Disc(c.Target.Position, Host.VerbTuning.MarkRadius), nowUs, c.Target, TagSeed);
                    s.PlantedBudgetUs = bt;
                    rec.MaterialId = s.Mat.Id.Value;
                    Fact("Plant", "", c.Target, nowUs, "seed " + s.Mat.Id, 0m, s.Mat.Id.Value);
                }
                else Fact("PlantRefused", "", c.Target, nowUs, step.Reason);
                Host.NotifyContact(Run.Log, rec);
            }

            // ---- the per-tick part: 信, expiry, followers, entries ----

            public override void Tick(long nowUs, IReadOnlyList<IVol2Target> targets)
            {
                AccruePause(nowUs);
                for (int i = 0; i < _seeds.Count; i++)
                {
                    var s = _seeds[i];
                    if (s.Fired || s.Gone) continue;
                    if (!s.Mat.Alive) { Ended(s, nowUs); continue; }
                    switch (_frame)
                    {
                        case Frame.Path: FollowHost(s, nowUs); break;
                        case Frame.Fan: TickArea(s, targets, nowUs); break;
                        case Frame.Ring: if (_ground) TickGround(s, nowUs); else TickArea(s, targets, nowUs); break;
                    }
                }
            }

            public override bool Idle(long nowUs)
            {
                foreach (var s in _seeds) if (!s.Fired && !s.Gone && !s.Mat.Alive) Ended(s, nowUs);
                foreach (var s in _seeds) if (s.Mat != null && s.Mat.Alive) return false;
                foreach (var m in _after) if (m.Alive) return false;
                Leave();
                return true;
            }

            public override void Abort(long nowUs) { Leave(); }

            void Leave() { if (_registered) { _hub.Unregister(this); _registered = false; } }

            void Ended(Seed s, long nowUs)
            {
                s.Gone = true;
                Fact(s.Mat.End == Vol2MaterialEnd.Expired ? "Expired" : "Ended", "", s.Host, nowUs, "seed " + s.Mat.Id + " ended " + s.Mat.End + " without working", 0m, s.Mat.Id.Value);
            }

            /// <summary>信: while a letter is drawn the waiting seeds do not wait. The expiry of each moves by the same amount the budget's clock stands still; while
            /// drawing is on and the cap is not used up the expiry is held at its latest possible value so the registry's sweep cannot take a seed mid-letter.</summary>
            void AccruePause(long nowUs)
            {
                long dt = Math.Max(0, nowUs - _lastTickUs); _lastTickUs = nowUs;
                bool waiting = false;
                foreach (var s in _seeds) if (s.Waiting) { waiting = true; break; }
                if (!waiting) return;
                bool drawing = _hub.IsDrawing;
                long cap = _hub.Tuning.PauseCapUs;
                if (drawing && dt > 0 && _paused < cap) _paused += Math.Min(dt, cap - _paused);
                long baseExpiry = Run.StartUs + Run.Plan.LifeUs + _paused + (drawing ? cap - _paused : 0);
                foreach (var s in _seeds)
                    if (s.Waiting && s.Mat.ExpiresUs != baseExpiry) s.Mat.ExpiresUs = baseExpiry;       // TE-S5-9: the registry has no shift call
                _hub.SetPaused(Run.Cast, _paused);
            }

            // ---- ㅏ: followers and triggers ----

            void FollowHost(Seed s, long nowUs)
            {
                var t = s.Host;
                if (t == null) return;
                if (!t.Alive || t.LifeRevision != s.HostRev)
                {
                    if (Host.Materials.TryEnd(s.Mat.Id, Vol2MaterialEnd.HitCancelled, nowUs)) { s.Gone = true; Fact("HostGone", "", t, nowUs, "the host is gone: the seed ends HitCancelled (TE-S5-6)", 0m, s.Mat.Id.Value); }
                    return;
                }
                if (Flat(s.Mat.Footprint.Origin, t.Position) > 0.05f) Host.Materials.TryMove(s.Mat.Id, Vol2Footprint.Disc(t.Position, Host.VerbTuning.MarkRadius), nowUs);
            }

            Seed SeedOn(long targetId, long lifeRevision)
            {
                foreach (var s in _seeds) if (s.Waiting && s.Host != null && s.Host.Id == targetId && s.HostRev == lifeRevision) return s;
                return null;
            }

            public void OnOwnerHit(long targetId, long lifeRevision, long swingId, long nowUs)
            {
                if (_frame != Frame.Path) return;
                var s = SeedOn(targetId, lifeRevision);
                if (s != null) ActivatePath(s, nowUs, swingId, "direct");
            }

            public void OnGatherContact(long castId, long materialId, long stableTargetId, long swingId, long nowUs)
            {
                if (_frame != Frame.Path || castId != Run.Cast.Value) return;
                foreach (var s in _seeds)
                {
                    if (!s.Waiting || s.Mat.Id.Value != materialId) continue;
                    if (!_hub.GatherMatches(s.Host, stableTargetId)) { Fact("ActivateRefused", "gather", s.Host, nowUs, "the swing met another body (stable id " + stableTargetId + ")"); return; }
                    ActivatePath(s, nowUs, swingId, "gather");
                    return;
                }
            }

            public void CollectCarries(List<NoInkSwingV3.Carry> into, long ownerId)
            {
                if (_frame != Frame.Path) return;
                foreach (var s in _seeds) if (s.Waiting) into.Add(new NoInkSwingV3.Carry(Run.Cast.Value, s.Mat.Id.Value, ownerId, _letter, true));
            }

            void ActivatePath(Seed s, long nowUs, long swingId, string trigger)
            {
                var t = s.Host;
                if (t == null || !t.Alive || t.LifeRevision != s.HostRev) return;
                if (swingId <= 0) swingId = Host.Services.Ids.NextSwing().Value;       // a path cast reports no swing: the trigger event still needs one id
                var attack = Host.NewAttack(Run, swingId, nowUs);
                var rec = Host.NewRecord(Run, MakeContact(t, nowUs, swingId), attack, "Activation");
                rec.MaterialId = s.Mat.Id.Value;
                var step = Run.Budget.Pay(attack.Value, ContactKind.Activate, EffectKind.Activation, 0m, TriggerKind.OwnerAttack, t.Id, t.LifeRevision, swingId, Budget(nowUs, s.PlantedBudgetUs + 1), s.Mat.Id.Value);
                rec.BudgetAccepted = step.Accepted; rec.BudgetReason = step.Reason;
                if (!step.Accepted)
                {
                    Fact("ActivateRefused", trigger, t, nowUs, step.Reason);
                    Host.NotifyContact(Run.Log, rec);
                    return;
                }
                s.Fired = true;
                int ordinal = 0;
                foreach (var cmd in step.Commands) if (cmd.Effect == EffectKind.Activation) Strike(t, rec, attack, nowUs, swingId, cmd.ActionAmount, ordinal++);
                Host.Materials.TryEnd(s.Mat.Id, Vol2MaterialEnd.Consumed, nowUs);
                Fact("Activate", trigger, t, nowUs, "action " + rec.Amount.ToString(CultureInfo.InvariantCulture), rec.Amount, s.Mat.Id.Value);
                var posture = _hub.CallPosture(t, _letter);        // ㅏ+ㅁ active seed: the owner's direct hit / 갈무리 on a seed is the 능동 상합 (posture 60)
                Fact("Posture", trigger, t, nowUs, posture.ToString(), posture.After, s.Mat.Id.Value, posture);
                Host.NotifyContact(Run.Log, rec);
            }

            // ---- ㅗ / ㅜ: an entry ----

            List<(float d, IVol2Target t)> Touching(Seed s, IReadOnlyList<IVol2Target> targets, long nowUs)
            {
                var l = new List<(float d, IVol2Target t)>();
                if (targets == null) return l;
                foreach (var t in targets)
                    if (t != null && t.Alive && s.Mat.Footprint.Touches(t, nowUs)) l.Add((Flat(Run.Spec.Origin, t.Position), t));
                l.Sort((a, b) => { int c = a.d.CompareTo(b.d); return c != 0 ? c : a.t.Id.CompareTo(b.t.Id); });
                return l;
            }

            HashSet<long> TouchingIds(Seed s, IReadOnlyList<IVol2Target> targets, long nowUs)
            {
                var ids = new HashSet<long>();
                if (!_hub.Tuning.InitialOccupantsTrigger) foreach (var p in Touching(s, targets, nowUs)) ids.Add(p.t.Id);
                return ids;
            }

            void TickArea(Seed s, IReadOnlyList<IVol2Target> targets, long nowUs)
            {
                var touching = Touching(s, targets, nowUs);
                bool entered = false;
                var ids = new HashSet<long>();
                foreach (var p in touching) { ids.Add(p.t.Id); if (!s.Inside.Contains(p.t.Id)) entered = true; }
                s.Inside = ids;
                if (entered) FireArea(s, touching, nowUs);
            }

            void FireArea(Seed s, List<(float d, IVol2Target t)> bodies, long nowUs)
            {
                long swing = Host.Services.Ids.NextSwing().Value;     // ONE trigger id for the whole cast: the rules allow the seed to work once
                var trigger = _frame == Frame.Fan ? TriggerKind.EnterFan : TriggerKind.CrossRing;
                s.Fired = true;
                int paid = 0;
                foreach (var (d, t) in bodies)
                {
                    var attack = Host.NewAttack(Run, swing, nowUs);
                    var rec = Host.NewRecord(Run, MakeContact(t, nowUs, swing, d), attack, "Activation");
                    rec.MaterialId = s.Mat.Id.Value;
                    var step = Run.Budget.Pay(attack.Value, ContactKind.Activate, EffectKind.Activation, 0m, trigger, t.Id, t.LifeRevision, swing, Budget(nowUs), s.Mat.Id.Value);
                    rec.BudgetAccepted = step.Accepted; rec.BudgetReason = step.Reason;
                    if (step.Accepted)
                    {
                        paid++;
                        int ordinal = 0;
                        foreach (var cmd in step.Commands) if (cmd.Effect == EffectKind.Activation) Strike(t, rec, attack, nowUs, swing, cmd.ActionAmount, ordinal++);
                        Fact("Activate", _frame == Frame.Fan ? "enter" : "cross", t, nowUs, "action " + rec.Amount.ToString(CultureInfo.InvariantCulture), rec.Amount, s.Mat.Id.Value);
                    }
                    else Fact("ActivateRefused", _frame == Frame.Fan ? "enter" : "cross", t, nowUs, step.Reason);
                    Host.NotifyContact(Run.Log, rec);
                }
                Host.Materials.TryEnd(s.Mat.Id, Vol2MaterialEnd.Consumed, nowUs);
                if (_giant) Collapse(s, nowUs, paid);
            }

            /// <summary>몸: the giant's strike is done; it falls apart into an earth material fan (amount 0: only the material qualification), 10 s.</summary>
            void Collapse(Seed s, long nowUs, int struck)
            {
                long life = RuleTypesV3.Microseconds(Numbers.Get("peak.몸.residueLife"));
                decimal amount = Numbers.Get("peak.몸.residueAmount");
                var m = Host.Materials.Create(Run.Cast, _letter, Body.Earth, Vol2MaterialKind.Residue, amount, life, s.Mat.Footprint, Vol2Faction.Player, Vol2Faction.Player, false, nowUs, 0, 0, TagRubble);
                _after.Add(m); Run.Log.Materials.Add(m);
                Fact("Collapse", "", null, nowUs, "the giant struck " + struck + " and falls apart into an earth fan " + m.Id + " (" + life + "us, amount " + amount.ToString(CultureInfo.InvariantCulture) + ")", amount, m.Id.Value);
            }

            // ---- ㅜ + 뭄: the caster steps onto the band ----

            void TickGround(Seed s, long nowUs)
            {
                Vector3 cur = _hub.CasterPosition, prev = _casterPrevSet ? _casterPrev : cur;
                _casterPrev = cur; _casterPrevSet = true;
                var spec = Run.Spec;
                if (!Vol2ShapeGeometry.RingCrossed(spec.Origin, spec.RingInner, spec.RingOuter, prev, cur, _hub.Tuning.CasterBodyRadius)) return;
                long swing = Host.Services.Ids.NextSwing().Value;
                var attack = Host.NewAttack(Run, swing, nowUs);
                var contact = new Vol2ShapeContact { Shape = Vol2ShapeKind.Ring, Target = null, TargetId = WorldTargetId, LifeRevision = 0, NowUs = nowUs, SwingId = swing, Point = cur, Along = Flat(spec.Origin, cur) };
                var rec = Host.NewRecord(Run, contact, attack, "Activation");
                rec.MaterialId = s.Mat.Id.Value;
                var step = Run.Budget.Pay(attack.Value, ContactKind.Activate, EffectKind.Activation, 0m, TriggerKind.CrossRing, WorldTargetId, 0, swing, Budget(nowUs), s.Mat.Id.Value);
                rec.BudgetAccepted = step.Accepted; rec.BudgetReason = step.Reason;
                if (!step.Accepted) { Fact("ActivateRefused", "caster", null, nowUs, step.Reason); Host.NotifyContact(Run.Log, rec); return; }
                s.Fired = true;
                rec.Amount = step.Commands.Count > 0 ? step.Commands[0].ActionAmount : 0m;
                rec.DamageNote = "non-damage axis: hardened ground";
                Host.Materials.TryEnd(s.Mat.Id, Vol2MaterialEnd.Consumed, nowUs);
                long groundLife = RuleTypesV3.Microseconds(Numbers.Get("peak.뭄.groundLife"));
                var ground = Host.Materials.Create(Run.Cast, _letter, Body.Earth, Vol2MaterialKind.Residue, 0m, groundLife, s.Mat.Footprint, Vol2Faction.Player, Vol2Faction.Player, false, nowUs, 0, 0, TagGround);
                _after.Add(ground); Run.Log.Materials.Add(ground);
                Fact("Ground", "caster", null, nowUs, "the ring hardens " + groundLife + "us: no dash, no jump inside (TE-S5-5)", rec.Amount, ground.Id.Value);
                var sp = spec;
                Host.FieldRequest(Run, "ground r=" + sp.RingRadius.ToString("0.##", CultureInfo.InvariantCulture) + " life=" + groundLife + "us", f => f.OnPlatform(Run.Cast, sp.Origin, sp.RingRadius, groundLife));
                Host.NotifyContact(Run.Log, rec);
            }

            // ---- ㅓ: the player really loses HP ----

            public void OnPlayerHit(Vol2PlayerHit hit, Vol2SeedHitGate gate, long nowUs)
            {
                if (_frame != Frame.Attachment) return;
                Seed s = null;
                foreach (var x in _seeds) if (x.Waiting) { s = x; break; }
                if (s == null) return;
                s.Fired = true;
                long swing = Host.Services.Ids.NextSwing().Value;      // this hit is the one trigger event
                float radius = (float)Numbers.Get("space.seed.attachmentRadius");
                Vector3 centre = _hub.CasterPosition;
                var near = new List<(float d, IVol2Target t)>();
                foreach (var t in Host.Targets)
                {
                    if (t == null || !t.Alive) continue;
                    float d = Flat(centre, t.Position);
                    if (d <= radius + t.Radius) near.Add((d, t));
                }
                near.Sort((a, b) => { int c = a.d.CompareTo(b.d); return c != 0 ? c : a.t.Id.CompareTo(b.t.Id); });
                int struck = 0;
                foreach (var (d, t) in near)
                {
                    var attack = Host.NewAttack(Run, swing, nowUs);
                    var rec = Host.NewRecord(Run, MakeContact(t, nowUs, swing, d), attack, "Activation");
                    rec.MaterialId = s.Mat.Id.Value;
                    var step = Run.Budget.Pay(attack.Value, ContactKind.Activate, EffectKind.Activation, (decimal)hit.Amount, TriggerKind.DamageReceived, t.Id, t.LifeRevision, swing, Budget(nowUs), s.Mat.Id.Value);
                    rec.BudgetAccepted = step.Accepted; rec.BudgetReason = step.Reason;
                    if (step.Accepted)
                    {
                        struck++;
                        int ordinal = 0;
                        foreach (var cmd in step.Commands) if (cmd.Effect == EffectKind.Activation) Strike(t, rec, attack, nowUs, swing, cmd.ActionAmount, ordinal++);
                        Fact("Activate", "hit", t, nowUs, "action " + rec.Amount.ToString(CultureInfo.InvariantCulture) + " (the player lost " + hit.Amount.ToString("0.##", CultureInfo.InvariantCulture) + " HP)", rec.Amount, s.Mat.Id.Value);
                    }
                    else Fact("ActivateRefused", "hit", t, nowUs, step.Reason);
                    Host.NotifyContact(Run.Log, rec);
                }
                if (struck == 0 && near.Count == 0) Fact("NoTarget", "hit", null, nowUs, "the seed worked with no enemy within " + radius.ToString("0.##", CultureInfo.InvariantCulture) + " m");
                Host.Materials.TryEnd(s.Mat.Id, Vol2MaterialEnd.Consumed, nowUs);
                if (_retaliate && gate != null)
                {
                    gate.GrantStaggerExemption();         // inside the confirmation: the stagger executor has not played yet
                    Fact("Exempt", "hit", null, nowUs, "the body stagger of hit#" + hit.HitId + " is exempt (damage and the letter interruption stay)");
                }
            }

            // ---- what a struck body takes ----

            void Strike(IVol2Target t, Vol2ContactRecord rec, AttackId attack, long nowUs, long swing, decimal amount, int ordinal)
            {
                Host.Hit(Run, t, rec, amount, attack, nowUs, t.Position, swing, ordinal);
                FirstBody(t, rec, attack, nowUs);
            }

            protected override void ApplyBody(IVol2Target t, Vol2ContactRecord rec, AttackId attack, long nowUs)
            {
                if (_press) Press(t, rec, attack, nowUs);
                else base.ApplyBody(t, rec, attack, nowUs);
            }

            /// <summary>맘: the body is pressed into the ground - no movement for 1.5 s (a boss half of that).</summary>
            void Press(IVol2Target t, Vol2ContactRecord rec, AttackId attack, long nowUs)
            {
                if (!Host.Services.Commands.TryBegin(new CommandKey(Host.Services.Ids.ScopeId, CommandRuleKind.Status, attack.Value, 0))) { rec.DuplicateCommands++; return; }
                long us = RuleTypesV3.Microseconds(Numbers.Get("combat.bind.maxDuration"));
                if (Host.BossTest != null && Host.BossTest(t)) us = (long)(us * (double)Numbers.Get("combat.bind.bossMultiplier"));
                float mult = _hub.Tuning.PressMoveMultiplier;
                Vector3 dir = Vol2ShapeGeometry.FlatUnit(t.Position - Run.Spec.Origin);
                var inv = CultureInfo.InvariantCulture;
                string note = "press x" + mult.ToString("0.##", inv) + " " + Vol2Time.ToSecondsF(us).ToString("0.##", inv) + "s";
                string sink = Host.Status != null ? Host.Status.Apply(new Vol2BodyEffectRequest(Vol2BodyActionKind.Bind, Run.Cast, attack, t, nowUs, us, mult, dir, 0f)) : "NoStatusSink";
                rec.BodyEffect = note + (sink.Length > 0 && sink != note ? " [" + sink + "]" : "");
                Fact("Press", "", t, nowUs, note, us);
            }
        }
    }

    /// <summary>The world side of 뭄: is this body inside a live hardened ring (cannot dash or jump)? The enemy movement layer asks it before it lets a dash
    /// or a jump start (open item: nothing in the game asks yet). Pure; reads the material registry.</summary>
    public static class Vol2SeedWorld
    {
        /// <param name="inclusive">TE-S5-5: the whole disc inside the ring's outer edge counts (true), or only the band (false).</param>
        public static bool BlocksDashOrJump(Vol2MaterialRegistry materials, Vector3 position, long nowUs, float bodyRadius = 0f, bool inclusive = true)
        {
            if (materials == null) return false;
            foreach (var m in materials.Alive)
            {
                if (m.Tag != Vol2SeedExecutor.TagGround || nowUs >= m.ExpiresUs || m.Footprint == null || m.Footprint.Kind != Vol2FootprintKind.Ring) continue;
                Vector3 a = m.Footprint.Origin, b = position; a.y = 0f; b.y = 0f;
                float d = (a - b).magnitude;
                if (inclusive ? d - bodyRadius <= m.Footprint.RingOuter : d + bodyRadius >= m.Footprint.RingInner && d - bodyRadius <= m.Footprint.RingOuter) return true;
            }
            return false;
        }
    }
}
