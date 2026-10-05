using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // The three path-opening field glyphs (SPEC-SPELL-120-308 WP-12, opened by D308-13 Q5): field.burn opens a vine gate,
    // field.cut a boulder gate, field.purify a polluted patch. One rule for the three (FieldGateRule308): the nearest closed
    // gate of the handler's kind that the caster faces and can reach, and only outside combat. The gate opens gate.delay
    // seconds after the cast (the burn, the cut and the cleansing take a moment); a cast with nothing to open, or one made
    // in combat, is no cast at all (Prepare refuses: no ink, like the wood lift and the earth bridge).
    // Where gates stand in the world and whether an opened gate is remembered are not decided here.
    public abstract class FieldGateEffect308 : ISpellEffect
    {
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("gate.reach", 0.5f, 30f), new SpellParamSpec("gate.angle", 1f, 180f),
            new SpellParamSpec("gate.delay", 0f, 30f), new SpellParamSpec("combat.radius", 0f, 100f),
        };

        struct Opening { public SpellFieldGate308 Gate; public float At; public int Fx; }
        readonly List<Opening> _opening = new List<Opening>();              // casts whose gate has not opened yet
        readonly List<SpellFieldGate308> _found = new List<SpellFieldGate308>();
        readonly List<FieldGateSnap> _snaps = new List<FieldGateSnap>();
        readonly List<bool> _engaged = new List<bool>();
        Collider[] _overlaps = new Collider[SpellFieldGate308.QueryCapacity];

        public abstract string Id { get; }
        protected abstract SpellFieldGateKind Kind { get; }
        public IReadOnlyList<SpellParamSpec> Params => Specs;
        public int OpeningCount => _opening.Count;

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null && Find(ctx.Host, ctx.Row) != null;

        public bool Commit(in SpellCastContext ctx)
        {
            var gate = Find(ctx.Host, ctx.Row);
            if (gate == null) return false;                                 // the host gives the ink back
            float delay = Mathf.Max(0f, ctx.Row.F("gate.delay", 0f));
            ctx.Host.PresentationOwned();
            var handle = ctx.Fx.Begin(new SpellFxRequest
            {
                Letter = ctx.Cast.Letter, Role = SpellFxRole.Field, Element = ctx.Cast.Element, Origin = ctx.Host.PlayerPosition,
                FallbackPoint = gate.transform.position, Target = gate.transform, ImpactClock = delay, Radius = gate.Radius,
                Grade01 = ctx.Cast.Brush01,
            });
            _opening.Add(new Opening { Gate = gate, At = ctx.Now + delay, Fx = handle.Id });
            Open(ctx.Now, ctx.Fx);                                          // a row without a delay opens at once
            return true;
        }

        public void Tick(in SpellTickContext ctx) { Open(ctx.Now, ctx.Fx); }

        // Death, rest or scene leave before the gate opened: the cast is dropped and the gate stays as it was.
        public void Clear(SpellClearReason reason) { _opening.Clear(); }

        void Open(float now, ISpellPresenter fx)
        {
            for (int i = _opening.Count - 1; i >= 0; i--)
            {
                var opening = _opening[i];
                if (now < opening.At) continue;
                _opening.RemoveAt(i);
                if (opening.Gate == null || !opening.Gate.Open() || opening.Fx == 0 || fx == null) continue;
                fx.Cue(new SpellFxHandle(opening.Fx), SpellFxCue.Hit,
                    new SpellFxCueArgs { Target = opening.Gate.transform, Point = opening.Gate.transform.position, At = now, Value = opening.Gate.Radius });
            }
        }

        // The gate this cast would open, or null. Gates are found through their colliders in the caster's own physics scene.
        SpellFieldGate308 Find(ISpellCastHost host, SpellRow row)
        {
            if (host.Player == null) return null;
            Vector3 player = host.PlayerPosition;
            if (InCombat(host, player, row.F("combat.radius", 0f))) return null;
            float reach = row.F("gate.reach", 0f);
            var scene = host.Player.gameObject.scene;
            PhysicsScene physics = scene.IsValid() ? scene.GetPhysicsScene() : Physics.defaultPhysicsScene;
            if (!physics.IsValid()) return null;
            int count = physics.OverlapSphere(player, reach, _overlaps, ~0, QueryTriggerInteraction.Collide);
            while (count >= _overlaps.Length)
            {
                Array.Resize(ref _overlaps, _overlaps.Length * 2);
                count = physics.OverlapSphere(player, reach, _overlaps, ~0, QueryTriggerInteraction.Collide);
            }
            _found.Clear(); _snaps.Clear();
            for (int i = 0; i < count; i++)
            {
                var gate = _overlaps[i] != null ? _overlaps[i].GetComponentInParent<SpellFieldGate308>() : null;
                if (gate == null || !gate.isActiveAndEnabled || _found.Contains(gate)) continue;
                _snaps.Add(new FieldGateSnap(_found.Count, (int)gate.Kind, gate.transform.position, gate.Radius, gate.IsOpen, Busy(gate)));
                _found.Add(gate);
            }
            Array.Clear(_overlaps, 0, count);
            int pick = FieldGateRule308.Pick(_snaps, (int)Kind, player, host.PlayerForward, reach, row.F("gate.angle", 0f));
            return pick == FieldGateRule308.None ? null : _found[pick];
        }

        bool Busy(SpellFieldGate308 gate)
        {
            for (int i = 0; i < _opening.Count; i++) if (_opening[i].Gate == gate) return true;
            return false;
        }

        bool InCombat(ISpellCastHost host, Vector3 player, float radius)
        {
            var targets = host.Targets;
            _engaged.Clear();
            for (int i = 0; targets != null && i < targets.Count; i++)
                _engaged.Add(targets[i] != null && targets[i].TryGetComponent(out Prologue.PrologueEncounter encounter) &&
                    encounter.isActiveAndEnabled && encounter.Current == Prologue.PrologueEncounter.Behaviour.Chase);
            return FieldGateRule308.InCombat(SpellSnapshots308.Take(targets), _engaged, player, radius);
        }
    }

    public sealed class FieldBurnEffect308 : FieldGateEffect308
    {
        public const string HandlerId = "field.burn";
        public override string Id => HandlerId;
        protected override SpellFieldGateKind Kind => SpellFieldGateKind.Vine;
    }

    public sealed class FieldCutEffect308 : FieldGateEffect308
    {
        public const string HandlerId = "field.cut";
        public override string Id => HandlerId;
        protected override SpellFieldGateKind Kind => SpellFieldGateKind.Boulder;
    }

    public sealed class FieldPurifyEffect308 : FieldGateEffect308
    {
        public const string HandlerId = "field.purify";
        public override string Id => HandlerId;
        protected override SpellFieldGateKind Kind => SpellFieldGateKind.Pollution;
    }
}
