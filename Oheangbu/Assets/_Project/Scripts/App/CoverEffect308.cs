using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // Unity half shared by the two cover handlers (SPEC-SPELL-120-308 WP-13: single.cover, path.cover). A handler owns the
    // pieces of cover its casts put into the world. Per frame: the handler moves or lands its pieces, pieces that ran out
    // are retired, and for every enemy the rule says whether a standing piece cuts the line from that enemy to the player
    // (CoverRule308). If one does, a screen is hung on the enemy's strike veil (its ranged strikes are stopped at the
    // enemy -> player doorway); the frame it no longer does, the screen is taken back. Cover never damages, controls or
    // rewards: it only stops ranged strikes.
    public abstract class CoverEffect308 : ISpellEffect
    {
        readonly List<SpellCover308> _pieces = new List<SpellCover308>();
        readonly VeilHolds308 _holds = new VeilHolds308();

        public abstract string Id { get; }
        public abstract IReadOnlyList<SpellParamSpec> Params { get; }
        public int PieceCount => _pieces.Count;
        public int ScreenedEnemies => _holds.Count;        // enemies whose ranged strikes a piece stops right now (checks read this)

        public virtual bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null;
        public abstract bool Commit(in SpellCastContext ctx);

        public void Tick(in SpellTickContext ctx)
        {
            Advance(ctx);
            for (int i = _pieces.Count - 1; i >= 0; i--)
                if (ctx.Now >= _pieces[i].Until) Retire(i, ctx.Fx, false);
            Screen(ctx);
        }

        // The host ends every presentation right after the effects are cleared, so nothing is cued here.
        public virtual void Clear(SpellClearReason reason)
        {
            _pieces.Clear();
            _holds.Clear();
        }

        // Called first every frame: land what is due, move what moves.
        protected abstract void Advance(in SpellTickContext ctx);
        // A piece left the list (it ran out or made room).
        protected virtual void OnRetired(in SpellCover308 piece) { }

        protected SpellCover308 PieceAt(int index) => _pieces[index];
        protected void SetPiece(int index, in SpellCover308 piece) { _pieces[index] = piece; }

        // Adds a piece for this handler, which may hold max pieces: the oldest makes room.
        protected void Add(SpellCover308 piece, int max, ISpellPresenter fx)
        {
            while (max > 0 && _pieces.Count >= max) Retire(0, fx, true);
            _pieces.Add(piece);
        }

        // One identity per piece: the owner id of the screens it hangs on enemies.
        protected static long NewToken(ISpellCastHost host, Element element)
        {
            Object caster = host.PlayerVitals != null ? (Object)host.PlayerVitals : host.Player;
            return AttackProvenance.Create(caster, DamageSource.PersistentSpell, element).AttackId;
        }

        void Retire(int index, ISpellPresenter fx, bool replaced)
        {
            var piece = _pieces[index];
            _pieces.RemoveAt(index);
            _holds.ReleaseAll(piece.Token);
            OnRetired(piece);
            if (piece.Fx == 0 || fx == null) return;
            var handle = new SpellFxHandle(piece.Fx);
            if (replaced) fx.End(handle, true);
            else fx.Cue(handle, SpellFxCue.Expire, new SpellFxCueArgs { Point = piece.Centre, At = piece.Until, Value = piece.Radius });
        }

        void Screen(in SpellTickContext ctx)
        {
            var host = ctx.Host;
            var targets = host != null ? host.Targets : null;
            if (targets == null) return;
            Vector3 player = host.PlayerPosition;
            for (int p = 0; p < _pieces.Count; p++)
            {
                var piece = _pieces[p];
                for (int i = 0; i < targets.Count; i++)
                {
                    var enemy = targets[i];
                    if (enemy == null) continue;
                    bool cut = enemy.IsAlive && enemy.isActiveAndEnabled && CoverRule308.Blocks(piece, enemy.transform.position, player, ctx.Now);
                    if (cut) _holds.Screen(enemy, piece.Token, piece.Until);
                    else _holds.Release(enemy, piece.Token);
                }
            }
        }
    }
}
