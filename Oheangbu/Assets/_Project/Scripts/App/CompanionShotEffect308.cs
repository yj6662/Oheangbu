using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.App
{
    // #308 WP-09, handler buff.companion (SPEC-SPELL-120-308 section 13): the five companion-shot buffs share this handler;
    // the element of the shots is the element of the buff glyph that was cast (row data, no glyph is named here).
    // While a buff lasts, every accepted cast (attack, parry, buff, ward, summon, field: old and new rows alike) is
    // accompanied by the row's small shots at the aimed enemy. No aimed enemy = no shot.
    // The shots are Companion-sourced hits: they are not scheduled through the host (ScheduleHit is always a direct spell
    // hit) but kept in this handler's own timetable and applied in Tick. Companion damage gives no five-element credit and
    // no groggy (EnemyVitals counts PlayerDirect only). The buff rewards nothing for being hit and changes no other spell.
    public sealed class CompanionShotEffect308 : ISpellEffect, ISpellAcceptHook
    {
        public const string HandlerId = "buff.companion";
        const string DurationKey = "duration", PowerKey = "companion.power", ShotsKey = "companion.shots", SpeedKey = "companion.speed",
            IntervalKey = "companion.interval";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("duration", 0.5f, 600f),
            new SpellParamSpec("companion.power", 0.01f, 1000f),
            new SpellParamSpec("companion.shots", 1f, 16f),
            new SpellParamSpec("companion.speed", 0.1f, 200f),
            new SpellParamSpec("companion.interval", 0f, 5f, false),
        };

        struct Pending
        {
            public EnemyVitals Target; public uint Life; public float At, Power; public Element Element;
            public AttackProvenance Attack; public char Letter; public Vector3 Origin; public int Index;
        }
        struct Aura { public char Letter; public SpellFxHandle Handle; }

        readonly CompanionShotRule308.State _state = new CompanionShotRule308.State();
        readonly List<CompanionShotRule308.Shot> _shots = new List<CompanionShotRule308.Shot>();
        readonly List<Pending> _pending = new List<Pending>();
        readonly List<Pending> _due = new List<Pending>();
        readonly List<Aura> _auras = new List<Aura>();
        readonly List<char> _ended = new List<char>();

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;
        public int ActiveCount => _state.Count;        // read only (checks)
        public int PendingShots => _pending.Count;

        public bool Prepare(in SpellCastContext ctx)
        {
            return ctx.Host != null && ctx.Row != null && ctx.Host.Buffs != null && (ctx.Host.PlayerVitals == null || ctx.Host.PlayerVitals.Hp01 > 0f);
        }

        public bool Commit(in SpellCastContext ctx)
        {
            var row = ctx.Row; var cast = ctx.Cast;
            float duration = row.F(DurationKey, 0f);
            var companion = new CompanionShotRule308.Companion(cast.Letter, cast.Element, row.F(PowerKey, 0f), Mathf.RoundToInt(row.F(ShotsKey, 1f)),
                row.F(SpeedKey, 0f), row.F(IntervalKey, 0f));
            if (!_state.Activate(ctx.Host.Buffs, companion, ctx.Now, duration)) return false;      // unusable row numbers: the host restores the ink

            // The buff's look starts at the player and follows him. Begin .. End brackets the buff (End comes on expiry, on a
            // recast and on a clear), but no duration is asked for: the look lives its own short authored life, so a
            // catalogue prefab is never stretched into a glow that lasts as long as the buff (spells light up for a moment
            // only). Rule state above never depends on what the presenter answers.
            EndAura(ctx.Fx, cast.Letter);
            if (ctx.Fx != null)
            {
                Vector3 at = ctx.Host.PlayerPosition;
                var handle = ctx.Fx.Begin(new SpellFxRequest
                {
                    Letter = cast.Letter, Role = SpellFxRole.Companion, Element = cast.Element, Origin = at, FallbackPoint = at + ctx.Host.PlayerForward,
                    Follow = ctx.Host.Player, Grade01 = cast.Brush01,
                });
                if (handle.Shown) ctx.Host.PresentationOwned();
                _auras.Add(new Aura { Letter = cast.Letter, Handle = handle });
            }
            return true;
        }

        // Every accepted cast, of any kind: the running buffs add their shots at the aimed enemy.
        public void OnCastAccepted(in SpellCastContext ctx)
        {
            if (_state.Count == 0 || ctx.Host == null) return;
            var aimed = ctx.Host.AimedTarget();
            if (aimed == null) return;
            var targets = ctx.Host.Targets;
            int aimedId = SpellSnapshots308.IndexOf(targets, aimed);
            if (aimedId < 0) return;
            _shots.Clear();
            _state.Accompany(ctx.Host.Buffs, SpellSnapshots308.Take(targets), aimedId, ctx.Host.PlayerPosition, ctx.Now, _shots);
            if (_shots.Count == 0) return;
            Object caster = ctx.Host.PlayerVitals != null ? (Object)ctx.Host.PlayerVitals : ctx.Host.Player;
            Vector3 origin = ctx.Host.PlayerPosition + Vector3.up * .4f;
            for (int i = 0; i < _shots.Count; i++)
            {
                var shot = _shots[i];
                _pending.Add(new Pending
                {
                    Target = aimed, Life = shot.Life, At = shot.At, Power = shot.Power, Element = shot.Element,
                    Attack = AttackProvenance.Create(caster, DamageSource.Companion, shot.Element), Letter = shot.Letter, Origin = origin, Index = shot.Index,
                });
                Cue(ctx.Fx, shot.Letter, SpellFxCue.Secondary, new SpellFxCueArgs
                { Target = aimed.transform, Point = aimed.transform.position + Vector3.up, At = shot.At - ctx.Now, Value = shot.Power, Index = shot.Index });
            }
            _shots.Clear();
        }

        public void Tick(in SpellTickContext ctx)
        {
            if (ctx.Host == null) return;
            if (_state.Count > 0)
            {
                _ended.Clear();
                _state.Expire(ctx.Host.Buffs, ctx.Now, _ended);
                for (int i = 0; i < _ended.Count; i++) EndAura(ctx.Fx, _ended[i]);
                _ended.Clear();
            }
            if (_pending.Count == 0) return;
            // A shot already in the air lands even when its buff has just ended. Due shots leave the timetable before any of
            // them is applied: a damage callback may clear this handler.
            _due.Clear();
            for (int i = 0; i < _pending.Count;)
            {
                if (ctx.Now >= _pending[i].At) { _due.Add(_pending[i]); _pending.RemoveAt(i); }
                else i++;
            }
            for (int i = 0; i < _due.Count; i++)
            {
                var shot = _due[i];
                var result = ctx.Host.ApplyDirectHit(shot.Target, shot.Life, shot.Power * ctx.Host.DamageScale(shot.Element), shot.Origin, shot.Attack, shot.Letter);
                if (result.AppliedDamage > 0f && shot.Target != null)
                    Cue(ctx.Fx, shot.Letter, SpellFxCue.Hit, new SpellFxCueArgs
                    { Target = shot.Target.transform, Point = shot.Target.transform.position + Vector3.up, Normal = Vector3.up, At = ctx.Now, Value = result.AppliedDamage, Index = shot.Index });
            }
            _due.Clear();
        }

        // The host ends every presentation right after this (ISpellPresenter.EndAll): the handles are only forgotten here.
        public void Clear(SpellClearReason reason)
        {
            _state.Clear();
            _pending.Clear();
            _due.Clear();
            _shots.Clear();
            _auras.Clear();
            _ended.Clear();
        }

        void Cue(ISpellPresenter fx, char letter, SpellFxCue cue, in SpellFxCueArgs args)
        {
            if (fx == null) return;
            for (int i = 0; i < _auras.Count; i++)
                if (_auras[i].Letter == letter) { fx.Cue(_auras[i].Handle, cue, args); return; }
        }

        void EndAura(ISpellPresenter fx, char letter)
        {
            for (int i = _auras.Count - 1; i >= 0; i--)
            {
                if (_auras[i].Letter != letter) continue;
                if (fx != null) fx.End(_auras[i].Handle);
                _auras.RemoveAt(i);
            }
        }
    }
}
