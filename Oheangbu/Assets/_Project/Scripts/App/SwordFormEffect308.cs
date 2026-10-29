using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // What one sword strike did (returned to the caller of Strike; rules never read it back).
    public readonly struct SwordStrike308
    {
        public readonly bool Swung;          // false = no form, or still inside the strike interval
        public readonly EnemyVitals Target;  // the first enemy the strike met (null = the strike cut air)
        public readonly float Damage;        // damage applied to that first enemy
        public readonly bool Counter;        // the sword's element overcomes that enemy's element
        public readonly bool GroggyGained;   // this strike raised that enemy's groggy
        public readonly int Struck;          // enemies the strike was judged on (a line weapon may pass through to more)
        public readonly float TotalDamage;   // damage applied to all of them
        public readonly int Step;            // its place in the combo (1 = first, combo.hits = finisher; 0 = a row without one)
        public SwordStrike308(bool swung, EnemyVitals target, float damage, bool counter, bool groggyGained, int struck, float totalDamage, int step = 0)
        {
            Swung = swung; Target = target; Damage = damage; Counter = counter; GroggyGained = groggyGained;
            Struck = struck; TotalDamage = totalDamage; Step = step;
        }
    }

    // #308 WP-14 sword form (SPELL-BUFF sword column; the five eo + siot glyphs share this handler, the element is the row's).
    //   form      the cast turns the brush into a sword of the glyph's element. One form at a time.
    //   no spell  while the form lasts every spell spend is refused through the cost hook (parry and ward included): a letter
    //             finished in sword form casts nothing and costs nothing. The draw mode itself is kept shut through the
    //             host's input seam (ISwordFormSeam308, held every frame the form lasts), so no stroke is even started.
    //   strike    Strike() = one sword strike on one enemy inside sword.reach / sword.arc, at most one per sword.interval.
    //             The strike power is the cast's power (row base power x brush), so a well drawn glyph is a sharper sword.
    //             The click comes from the same seam: Tick asks it once a frame and calls Strike.
    //   counter   a landed strike whose element overcomes the enemy's element is a counter strike: every groggy.hits-th one
    //             on the same enemy raises its groggy by groggy.steps (GroggySource.SwordCounter, the third canon source).
    //   upkeep    ink drains by sword.drain per second. Income is handed back the moment the pool reports it (intake stop),
    //             and natural regeneration cannot start behind a spend made every frame. The form ends when the ink is
    //             gone (the upkeep is the form's time limit), on death / rest / scene leave (Clear), or by the release
    //             press of the seam (ReleaseByPlayer) when release.manual allows it.
    //   safety    the form is a rule state. Presentation and the input seam are best effort and isolated: whatever they
    //             throw, a committed form stays on with its ink spent and an ended form is off. Commit never fails once
    //             the form is on, so the host can never refund the ink of a form that is still held.
    // Every number is a row parameter. The judgement is SwordFormRule308 (pure); this class holds the Unity half.
    public sealed class SwordFormEffect308 : ISpellEffect, ISpellCostHook
    {
        public const string HandlerId = "sword.form";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("sword.reach", .1f, 20f),
            new SpellParamSpec("sword.arc", 1f, 180f),
            new SpellParamSpec("sword.interval", 0f, 10f),
            new SpellParamSpec("sword.drain", .001f, 1f),
            new SpellParamSpec("groggy.hits", 1f, 100f),
            new SpellParamSpec("groggy.steps", 0f, 10f),
            new SpellParamSpec("release.manual", 0f, 1f),
            new SpellParamSpec("draw.lock", 0f, 100000f),
            // D308-127 (TEST) weapon strikes; a row without them is the plain sword (one enemy in the cone, full power)
            new SpellParamSpec("strike.width", 0f, 5f, false),
            new SpellParamSpec("strike.count", 1f, 8f, false, 1f),
            new SpellParamSpec("strike.through", 0f, 1f, false, 1f),
            new SpellParamSpec("strike.near", 0f, 20f, false),
            new SpellParamSpec("strike.inner", 0f, 1f, false, 1f),
            new SpellParamSpec("strike.aim", 0f, 90f, false),
            new SpellParamSpec("break.defence", 0f, 1f, false),
            new SpellParamSpec("break.duration", 0f, 120f, false),
            // D308-127 debate 15 (TEST): the two-handed sword's combo, the flail's ink hold
            new SpellParamSpec("combo.hits", 0f, 8f, false),
            new SpellParamSpec("combo.rest", 0f, 10f, false),
            new SpellParamSpec("combo.window", 0f, 10f, false),
            new SpellParamSpec("combo.buffer", 0f, 1f, false),
            new SpellParamSpec("combo.finish", 0f, 5f, false, 1f),
            new SpellParamSpec("hold.per", 0f, 10f, false),
            new SpellParamSpec("hold.cap", 0f, 10f, false),
            // D308-127 debate 16 (TEST): the horn bow (shot.draw > 0 = the row is a bow; sword.interval is its recovery)
            new SpellParamSpec("shot.draw", 0f, 5f, false),
            new SpellParamSpec("shot.speed", 0f, 200f, false),
            new SpellParamSpec("shot.range", 0f, 100f, false),
            new SpellParamSpec("shot.ink", 0f, 1f, false),
            new SpellParamSpec("shot.move", 0f, 1f, false, 1f),
        };

        struct Shredded { public EnemyVitals Enemy; public float Until; }
        // an arrow in the air (debate 16): everything it needs to land is fixed when it leaves the bow
        struct Arrow
        {
            public EnemyVitals Target; public uint Life; public float At, Power; public Vector3 Origin;
            public AttackProvenance Attack; public Element Element; public char Letter;
        }

        bool _active, _manual;
        char _letter;
        Element _element;
        float _power, _reach, _arc, _interval, _drain, _lock, _nextStrikeAt;
        int _groggyHits, _groggySteps;
        // weapon strike (D308-127): line half width (0 = cone), enemies per strike, share behind the first, the near zone
        // and its share, the line's aim correction, the defence a landed strike takes away and for how long
        float _width, _through, _near, _inner, _aim, _shred, _shredFor;
        int _count;
        // debate 15: the combo (hits < 2 = none), the step of the last strike, a click kept for the next allowed moment;
        // the flail's ink hold (its owner token for the player's domain state, the hold's running end)
        int _comboHits, _comboStep;
        float _comboRest, _comboWindow, _comboBuffer, _comboFinish, _holdPer, _holdCap, _holdUntil;
        bool _pending;
        long _holdOwner;
        // debate 16, the bow (a row with shot.draw > 0): drawing since _drawStart, the shot numbers, the arrows in the air
        bool _bow, _drawing;
        float _drawStart, _shotDraw, _shotSpeed, _shotRange, _shotInk, _shotMove;
        readonly List<Arrow> _arrows = new List<Arrow>();
        readonly List<Arrow> _landing = new List<Arrow>();
        IPlayerDomainHost308 _domain;
        int[] _hitIds = Array.Empty<int>();
        float[] _hitDistances = Array.Empty<float>();
        // enemies whose defence a strike lowered (taken back on Clear; a form that ran out leaves the running shred alone)
        readonly List<Shredded> _shredded = new List<Shredded>();
        SpellFxHandle _fx;
        // the pool the form listens to while it lasts (set in Commit, dropped when the form ends)
        InkPool _ink;
        // the input side of the host that carries the form (null = a host without one: the form still runs, nobody clicks)
        ISwordFormSeam308 _seam;
        // counter-strike progress per target slot (ISpellCastHost.Targets order), bound to the life it was earned on
        int[] _progress = Array.Empty<int>();
        uint[] _progressLife = Array.Empty<uint>();

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;

        // Read-only state for the input seams and for checks. Rules never read anything back from a presenter.
        public bool Active => _active;
        public Element SwordElement => _element;
        public bool BlocksDrawing => _active;
        public bool ManualReleaseAllowed => _active && _manual;
        public float NextStrikeAt => _nextStrikeAt;
        public bool IsBow => _active && _bow;
        public bool Drawing => _active && _drawing;
        public int ArrowsInFlight => _arrows.Count;

        // A sword without strike power is a data error, not a cast.
        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null && ctx.Host.Ink != null && ctx.Cast.Power > 0f;

        public bool Commit(in SpellCastContext ctx)
        {
            var host = ctx.Host; var row = ctx.Row;
            if (_active) End(ctx.Fx, SpellFxCue.Expire, false);
            // every rule value is read before the form is switched on: nothing below this block can refuse the cast
            _letter = ctx.Cast.Letter; _element = ctx.Cast.Element; _power = ctx.Cast.Power;
            _reach = row.F("sword.reach", 0f); _arc = row.F("sword.arc", 0f); _interval = row.F("sword.interval", 0f);
            _drain = row.F("sword.drain", 0f); _lock = row.F("draw.lock", 0f); _manual = row.F("release.manual", 0f) > 0f;
            _groggyHits = Mathf.RoundToInt(row.F("groggy.hits", 1f)); _groggySteps = Mathf.RoundToInt(row.F("groggy.steps", 0f));
            _width = row.F("strike.width", 0f); _count = Mathf.Max(1, Mathf.RoundToInt(row.F("strike.count", 1f)));
            _through = row.F("strike.through", 1f); _near = row.F("strike.near", 0f); _inner = row.F("strike.inner", 1f);
            _aim = row.F("strike.aim", 0f); _shred = row.F("break.defence", 0f); _shredFor = row.F("break.duration", 0f);
            _comboHits = Mathf.RoundToInt(row.F("combo.hits", 0f)); _comboRest = row.F("combo.rest", 0f); _comboWindow = row.F("combo.window", 0f);
            _comboBuffer = row.F("combo.buffer", 0f); _comboFinish = row.F("combo.finish", 1f); _comboStep = 0; _pending = false;
            _holdPer = row.F("hold.per", 0f); _holdCap = row.F("hold.cap", 0f); _holdUntil = float.NegativeInfinity;
            _shotDraw = row.F("shot.draw", 0f); _bow = _shotDraw > 0f; _drawing = false;
            _shotSpeed = row.F("shot.speed", 0f); _shotRange = row.F("shot.range", 0f); _shotInk = row.F("shot.ink", 0f); _shotMove = row.F("shot.move", 1f);
            _domain = host as IPlayerDomainHost308;
            // the hold's owner token: a fresh attack id (only the counter moves). It lies inside this cast's attack window
            // (D308-123), but it never lands a hit, so the empower charge's "first confirmed hit in the window" never sees it
            _holdOwner = _holdPer > 0f ? AttackProvenance.Create(host.PlayerVitals != null ? (UnityEngine.Object)host.PlayerVitals : host.Player, DamageSource.PlayerDirect, _element).AttackId : 0L;
            if (_hitIds.Length < _count) { Array.Resize(ref _hitIds, _count); Array.Resize(ref _hitDistances, _count); }
            _nextStrikeAt = ctx.Now; _active = true;
            _ink = host.Ink; _ink.Gained += OnInkGained;
            _seam = host as ISwordFormSeam308;
            HoldSeam();
            // Best effort from here on: the form is on and its ink is spent whatever the presentation does.
            _fx = default;
            try
            {
                host.PresentationOwned();
                if (ctx.Fx != null)
                    _fx = ctx.Fx.Begin(new SpellFxRequest
                    {
                        Letter = _letter, Role = SpellFxRole.Attached, Element = _element, Origin = host.PlayerPosition,
                        FallbackPoint = host.PlayerPosition, Follow = host.Player, Grade01 = ctx.Cast.Brush01,
                        Duration = SwordFormRule308.LongestLife(host.Ink.Value, host.Ink.CapacityMultiplier, _drain),
                    });
            }
            catch (Exception exception) { Debug.LogException(exception); _fx = default; }
            return true;
        }

        // While the form lasts no spell can be paid for (the form's own cast is spent before Commit turns it on).
        public float CostScale(in SpellCastInfo cast, float now) => SwordFormRule308.CostScale(_active, _lock);

        public void Tick(in SpellTickContext ctx)
        {
            // a shred that ran out is no longer this form's to take back (also after the form itself ended)
            for (int i = _shredded.Count - 1; i >= 0; i--)
                if (_shredded[i].Enemy == null || ctx.Now >= _shredded[i].Until) _shredded.RemoveAt(i);
            // an arrow already in the air lands even when the form has just ended (debate 16)
            if (_arrows.Count > 0) Land(ctx);
            if (!_active) return;
            var ink = _ink;
            if (ink == null) { End(ctx.Fx, SpellFxCue.Expire, false); return; }
            float drain = SwordFormRule308.DrainAmount(_drain, ctx.Delta);
            bool drainHeld = ctx.Host is IPlayerDomainHost308 domain && domain.PlayerDomain308.DrainHeld(ctx.Now);   // #308 WP-17 (눔)
            if (drain > 0f && !drainHeld) ink.SpendClamped(drain);
            if (SwordFormRule308.Exhausted(ink.Value)) { End(ctx.Fx, SpellFxCue.Expire, false); return; }
            // the input side: the draw mode stays shut, the release press puts the sword away, a click is one strike
            var seam = _seam;
            if (seam == null) return;
            HoldSeam();
            bool release = false, strike = false, held = false, released = false;
            // only a form the player may put away reads the release key: the same press may fire the 앞잡 volley, and a volley
            // always takes the sword with it (D308-123). Accepted, it also ends a draw: no arrow leaves on that frame.
            try
            {
                release = _manual && seam.SwordReleasePressed308;
                if (_bow) { held = !release && seam.SwordStrikeHeld308; released = !release && seam.SwordStrikeReleased308; }
                else strike = !release && seam.SwordStrikePressed308;
            }
            catch (Exception exception) { Debug.LogException(exception); }
            if (release && ReleaseByPlayer(ctx)) return;
            if (release) _pending = false;
            if (_bow) { BowInput(ctx, held, released); return; }
            if (strike && !SwordFormRule308.CanStrike(ctx.Now, _nextStrikeAt))
            {
                // debate 15: a click just before the next strike is allowed is kept for it (combo.buffer; 0 = dropped as before)
                if (SwordFormRule308.Buffered(ctx.Now, _nextStrikeAt, _comboBuffer)) _pending = true;
            }
            else if (strike || (_pending && SwordFormRule308.CanStrike(ctx.Now, _nextStrikeAt))) Strike(ctx);
        }

        // Intake stop: income that arrives while the form lasts is handed straight back. Nothing is gained and nothing is lost.
        void OnInkGained(float received)
        {
            if (_active && _ink != null) _ink.Restore(SwordFormRule308.IntakeStop(_ink.Value, received));
        }

        // One sword strike. The caller is the input seam (a click while Active) or a check.
        public SwordStrike308 Strike(in SpellTickContext ctx)
        {
            var host = ctx.Host;
            if (!_active || _bow || host == null || !SwordFormRule308.CanStrike(ctx.Now, _nextStrikeAt)) return default;   // the bow shoots (BowInput)
            // debate 15: the step of the combo (0 = a row without one) sets the wait after this strike and its share
            int step = SwordFormRule308.ComboStep(_comboStep, ctx.Now, _nextStrikeAt, _comboHits, _comboWindow);
            _comboStep = step; _pending = false;
            _nextStrikeAt = SwordFormRule308.NextStrikeAt(ctx.Now, SwordFormRule308.ComboGap(step, _comboHits, _interval, _comboRest));
            float comboShare = SwordFormRule308.ComboShare(step, _comboHits, _comboFinish);
            var targets = host.Targets;
            var actors = SpellSnapshots308.Take(targets);
            int aimed = SpellSnapshots308.IndexOf(targets, host.AimedTarget());
            Vector3 origin = host.PlayerPosition, forward = host.PlayerForward;
            Vector3 direction = _width > 0f ? SwordFormRule308.StrikeDirection(actors, origin, forward, _reach, _aim, aimed) : forward;
            int struck = SwordFormRule308.SelectTargets(actors, origin, direction, _reach, _arc, _width, aimed, _count, _hitIds, _hitDistances);
            if (struck == 0)
            {
                Cue(ctx.Fx, SpellFxCue.Miss, new SpellFxCueArgs { Point = origin + direction * _reach, At = ctx.Now, Index = step, Finish = step > 0 && step >= _comboHits });
                return new SwordStrike308(true, null, 0f, false, false, 0, 0f, step);
            }
            int landed = 0;
            UnityEngine.Object instigator = host.PlayerVitals != null ? (UnityEngine.Object)host.PlayerVitals : host.Player;
            EnemyVitals first = null; float firstDamage = 0f, total = 0f; bool firstCounter = false, firstGained = false;
            for (int k = 0; k < struck; k++)
            {
                int id = _hitIds[k];
                var target = targets[id]; uint life = target.LifeRevision;
                // the player's own elemental attack: PlayerDirect (the only source a counter strike may carry), one per enemy
                var attack = AttackProvenance.CreateMelee(instigator, DamageSource.PlayerDirect, _element);   // WP-16: melee passes a guard
                // D308-124: the same element the counter-element damage scale reads (EnemyVitals.TryAttackElement)
                bool counter = SwordFormRule308.IsCounter(_element, target.TryAttackElement(out Element theirs), theirs);
                float share = SwordFormRule308.TargetShare(k, _hitDistances[k], _through, _near, _inner);
                var hit = host.ApplyDirectHit(target, life, _power * share * comboShare * host.DamageScale(_element), origin + Vector3.up * .4f, attack, _letter);
                if (hit.AppliedDamage > 0f) landed++;
                bool gained = false;
                if (hit.AppliedDamage > 0f && !hit.Killed)
                {
                    if (counter)
                    {
                        int steps = SwordFormRule308.CounterGain(ref Progress(id, life, targets.Count), _groggyHits, _groggySteps);
                        if (steps > 0) gained = target.AddGroggy(GroggySource.SwordCounter, attack, steps);
                    }
                    Shred(target, ctx.Now);
                }
                Cue(ctx.Fx, hit.AppliedDamage > 0f ? SpellFxCue.Hit : SpellFxCue.Miss,
                    new SpellFxCueArgs { Target = target.transform, Point = target.transform.position + Vector3.up * .4f, At = ctx.Now, Value = hit.AppliedDamage, Index = step, Finish = step > 0 && step >= _comboHits });
                total += hit.AppliedDamage;
                if (k == 0) { first = target; firstDamage = hit.AppliedDamage; firstCounter = counter; firstGained = gained; }
            }
            Hold(landed, ctx.Now);
            return new SwordStrike308(true, first, firstDamage, firstCounter, firstGained, struck, total, step);
        }

        // The bow (debate 16): this frame's click (held = down behind the action doors, released = came up). The seam calls it
        // from Tick; checks call it with their own clock. Returns what the bow did. The presentation hears the draw as Tick cues
        // (Value = how far drawn, -1 = the draw ended without an arrow) and the arrow as one Secondary cue (Shoot).
        public BowStep308 BowInput(in SpellTickContext ctx, bool held, bool released)
        {
            if (!_active || !_bow || ctx.Host == null) return BowStep308.None;
            var step = SwordFormRule308.BowStep(_drawing, ctx.Now, _drawStart, _shotDraw, held, released, _nextStrikeAt);
            switch (step)
            {
                case BowStep308.Begin: _drawing = true; _drawStart = ctx.Now; break;
                case BowStep308.Fizzle:
                case BowStep308.Cancel: _drawing = false; Cue(ctx.Fx, SpellFxCue.Tick, new SpellFxCueArgs { Value = -1f, At = ctx.Now }); break;
                case BowStep308.Fire: _drawing = false; Shoot(ctx); break;
            }
            if (_drawing) Cue(ctx.Fx, SpellFxCue.Tick, new SpellFxCueArgs { Value = SwordFormRule308.Drawn01(ctx.Now, _drawStart, _shotDraw), At = ctx.Now });
            var seam = _seam;
            if (seam != null)
                try { seam.HoldMovementScale308(SwordFormRule308.MoveScale(_drawing, _shotMove)); }
                catch (Exception exception) { Debug.LogException(exception); }
            return step;
        }

        // One arrow: at the aimed enemy inside shot.range, or straight ahead to a miss. It flies distance / speed (the haste of
        // this moment included) and lands in Land. shot.ink is paid here once; the form ends when that empties the pool.
        void Shoot(in SpellTickContext ctx)
        {
            var host = ctx.Host;
            _nextStrikeAt = SwordFormRule308.NextStrikeAt(ctx.Now, _interval);
            var targets = host.Targets;
            var actors = SpellSnapshots308.Take(targets);
            int aimed = SpellSnapshots308.IndexOf(targets, host.AimedTarget());
            Vector3 feet = host.PlayerPosition, origin = feet + Vector3.up * .4f;
            int id = SwordFormRule308.ShotTarget(actors, feet, _shotRange, aimed);
            float haste = host.Buffs != null ? host.Buffs.SpeedScale(ctx.Now) : 1f;
            if (_shotInk > 0f && _ink != null) _ink.SpendClamped(_shotInk);
            float life = _ink != null ? SwordFormRule308.LongestLife(_ink.Value, _ink.CapacityMultiplier, _drain) : 0f;
            if (id < 0)
            {
                Cue(ctx.Fx, SpellFxCue.Secondary, new SpellFxCueArgs
                { Point = origin + host.PlayerForward * _shotRange, At = SwordFormRule308.FlightTime(_shotRange, _shotSpeed, haste), Value = life });
            }
            else
            {
                var target = targets[id];
                Vector3 point = target.transform.position + Vector3.up * .4f;
                float flight = SwordFormRule308.FlightTime(Vector3.Distance(feet, target.transform.position), _shotSpeed, haste);
                UnityEngine.Object instigator = host.PlayerVitals != null ? (UnityEngine.Object)host.PlayerVitals : host.Player;
                _arrows.Add(new Arrow
                {
                    Target = target, Life = target.LifeRevision, At = ctx.Now + flight, Power = _power, Origin = origin,
                    Attack = AttackProvenance.Create(instigator, DamageSource.PlayerDirect, _element),   // ranged: a guard takes it
                    Element = _element, Letter = _letter,
                });
                Cue(ctx.Fx, SpellFxCue.Secondary, new SpellFxCueArgs { Target = target.transform, Point = point, At = flight, Value = life });
            }
            if (_ink != null && SwordFormRule308.Exhausted(_ink.Value)) End(ctx.Fx, SpellFxCue.Expire, false);
        }

        // Arrows whose time has come. They leave the list before any is applied: a damage callback may clear this handler.
        // The hit itself is the host's direct hit (a dead or new life misses, a wall in the line misses); the look of the
        // landing belongs to the arrow the presenter flies (no Hit cue: the form may be gone by now).
        void Land(in SpellTickContext ctx)
        {
            var host = ctx.Host;
            _landing.Clear();
            for (int i = 0; i < _arrows.Count;)
            {
                if (ctx.Now >= _arrows[i].At) { _landing.Add(_arrows[i]); _arrows.RemoveAt(i); }
                else i++;
            }
            if (host == null) { _landing.Clear(); return; }
            for (int i = 0; i < _landing.Count; i++)
            {
                var arrow = _landing[i];
                if (arrow.Target == null) continue;
                try { host.ApplyDirectHit(arrow.Target, arrow.Life, arrow.Power * host.DamageScale(arrow.Element), arrow.Origin, arrow.Attack, arrow.Letter); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
            _landing.Clear();
        }

        // The flail's ink hold (debate 15): a swing that landed holds the upkeep for hold.per per enemy, at most hold.cap ahead.
        void Hold(int landed, float now)
        {
            float until = SwordFormRule308.HoldUntil(_holdUntil, now, landed, _holdPer, _holdCap);
            if (!(until > _holdUntil) || _domain == null || _holdOwner == 0L) return;
            _holdUntil = until;
            try { _domain.PlayerDomain308.HoldDrain(_holdOwner, until); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        void ReleaseHold()
        {
            var domain = _domain; long owner = _holdOwner;
            _domain = null; _holdOwner = 0L; _holdUntil = float.NegativeInfinity; _pending = false; _comboStep = 0;
            if (domain == null || owner == 0L) return;
            try { domain.PlayerDomain308.End(owner); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        // The axe's armour break (the 맛 path: EnemyModifierState308.ShredDefence, the strongest running shred counts).
        // A row without break.defence / break.duration lowers nothing.
        void Shred(EnemyVitals target, float now)
        {
            if (_shred <= 0f || _shredFor <= 0f) return;
            float until = now + _shredFor;
            if (!target.Modifiers.ShredDefence(until, _shred)) return;
            for (int i = 0; i < _shredded.Count; i++)
                if (_shredded[i].Enemy == target) { if (until > _shredded[i].Until) _shredded[i] = new Shredded { Enemy = target, Until = until }; return; }
            _shredded.Add(new Shredded { Enemy = target, Until = until });
        }

        // The player puts the sword away (the release press of the seam, or a check). false = no form, or the row forbids it.
        public bool ReleaseByPlayer(in SpellTickContext ctx)
        {
            if (!_active || !_manual) return false;
            End(ctx.Fx, SpellFxCue.Expire, true);
            return true;
        }

        public void Clear(SpellClearReason reason)
        {
            // the host ends every presentation right after (ISpellPresenter.EndAll)
            _active = false; _fx = default; _drawing = false;
            _arrows.Clear(); _landing.Clear();   // death, rest, leaving: the arrows in the air are gone too
            StopListening();
            ReleaseHold();
            DropSeam(false);
            Array.Clear(_progress, 0, _progress.Length); Array.Clear(_progressLife, 0, _progressLife.Length);
            // the caster died, rested or left: the shred the axe hung on an enemy is taken back. EndShred ends every running
            // shred of that enemy (a 맛 / 놋 one included), like ArmorBreakEffect308.AtClear does for its own carriers.
            for (int i = 0; i < _shredded.Count; i++)
                if (_shredded[i].Enemy != null) _shredded[i].Enemy.Modifiers.EndShred();
            _shredded.Clear();
        }

        void StopListening()
        {
            if (_ink != null) _ink.Gained -= OnInkGained;
            _ink = null;
        }

        // The rule state goes first: once this returns the form is off, whatever the seam or the presenter did.
        void End(ISpellPresenter fx, SpellFxCue cue, bool byPlayer)
        {
            if (!_active) return;
            _active = false; _drawing = false;
            StopListening();
            ReleaseHold();
            DropSeam(byPlayer);
            var handle = _fx; _fx = default;
            if (fx == null) return;
            try { fx.Cue(handle, cue, new SpellFxCueArgs()); fx.End(handle); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        // Presentation of a strike: best effort, never part of its result.
        void Cue(ISpellPresenter fx, SpellFxCue cue, in SpellFxCueArgs args)
        {
            if (fx == null) return;
            try { fx.Cue(_fx, cue, args); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        void HoldSeam()
        {
            var seam = _seam;
            if (seam == null) return;
            try { seam.HoldSwordForm308(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        // The draw mode and the click go back to what they were. byPlayer = the release press is still down.
        void DropSeam(bool byPlayer)
        {
            var seam = _seam; _seam = null;
            if (seam == null) return;
            try { seam.DropSwordForm308(byPlayer); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        // Counter progress of one target slot. A new life starts from nothing (groggy itself resets with the encounter).
        ref int Progress(int id, uint life, int slots)
        {
            if (_progress.Length < slots) { Array.Resize(ref _progress, slots); Array.Resize(ref _progressLife, slots); }
            if (_progressLife[id] != life) { _progressLife[id] = life; _progress[id] = 0; }
            return ref _progress[id];
        }
    }
}
