using System;
using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>남긴다 (S5-b slice 4, table rows ㄱ): the shape hits what it touches NOW (the instant term, per body, like 한 번), then leaves a RESIDUE
    /// where it landed - one material holding the plan's residue amount (0.9) for the plan's life (10 s; 6 s on the caster's body). Anything that
    /// touches the residue's footprint draws a share of what it holds. The pool is ONE: the cast's budget books the creation as a Transfer
    /// (EffectBudgetV3 ContactKind.Transfer, so the cast's Transferred is exactly 0.9); every draw after that is a TryDebit in the material
    /// registry, which is the truth of what the residue still holds - so a touch, a tick and (slice 8) a reaction cannot pay the 0.9 twice, and a
    /// life stretched by 국 is not refused as "Expired" by the cast's own life.
    /// Where it lands: path = at the first body (else where the path ended); fan / ring = the shape itself, standing for the residue's life; attachment
    /// = on the caster (touched through the swing, no place in the world). 걱 is not here (the defence kit's recovery).
    /// Peaks: 각 (path) the vine follows the target as a cage for 1.5 s (boss x0.5), the residue appears where the cage lets go and its life
    /// counts from there; 곡 (fan) the footprint grows to area x1.5 (radius x sqrt 1.5, angle fixed) and binds each body that enters, once, 1.5 s;
    /// 국 (ring) stretches the life of MY residues inside the ring x1.5, once each (no stacking), and asks the field for a platform.</summary>
    public sealed class Vol2ResidueExecutor : IVol2VerbExecutor
    {
        public Verb Verb => Verb.Leave;
        public string Name => "Residue(남긴다)";
        public IVol2VerbRun Begin(Vol2CastRun run) => new ResidueRun(run);

        sealed class ResidueRun : Vol2VerbRunBase
        {
            readonly bool _cage, _grow, _extend;
            readonly HashSet<long> _touched = new HashSet<long>();
            Vol2Material _res;
            bool _created;
            IVol2Target _landing;
            IVol2Target _caged; long _cageRev, _cageUntilUs; Vector3 _cagePos;

            public ResidueRun(Vol2CastRun run) : base(run)
            {
                char c = run.Glyph.Letter;
                _cage = c == '각'; _grow = c == '곡'; _extend = c == '국';
            }

            public override IVol2ShapeEmitter CreateEmitter(Vol2ShapeSpec spec, long nowUs)
            {
                switch (spec.Kind)
                {
                    case Vol2ShapeKind.Path: return new Vol2PathEmitter(spec, nowUs, Host.VerbTuning.SpreadStopsPathAtFirst, Host.Wall, Host.Tuning.ChestHeight);
                    case Vol2ShapeKind.Fan: return new Vol2FanEmitter(spec, nowUs);
                    case Vol2ShapeKind.Ring: return new Vol2RingEmitter(spec, nowUs);
                    default: return new Vol2AttachmentHost(spec, nowUs, Run.Plan.LifeUs);
                }
            }

            public override void Start(long nowUs, IReadOnlyList<IVol2Target> targets)
            {
                var spec = Run.Spec;
                switch (spec.Kind)
                {
                    case Vol2ShapeKind.Attachment: Make(Vol2Footprint.Attached(spec.Origin), nowUs, null); break;
                    case Vol2ShapeKind.Fan:
                        {
                            float linear = _grow ? Mathf.Sqrt((float)Numbers.Get("peak.곡.area")) : 1f;
                            Make(Vol2Footprint.Fan(spec, linear, nowUs, nowUs + Run.Plan.LifeUs), nowUs, null); break;
                        }
                    case Vol2ShapeKind.Ring:
                        Make(Vol2Footprint.Ring(spec), nowUs, null);
                        if (_extend) ExtendMine(nowUs);
                        break;
                }
            }

            public override void OnContact(Vol2ShapeContact c, long nowUs)
            {
                if (Run.Spec.Kind == Vol2ShapeKind.Path && _landing == null) _landing = c.Target;
                var rec = Touch(c, nowUs);
                if (!rec.BudgetAccepted) return;
                if (Run.Spec.Kind == Vol2ShapeKind.Attachment) Touch1(c.Target, nowUs, c.SwingId);
                else if (_cage && Run.Spec.Kind == Vol2ShapeKind.Path && _caged == null && !_created) BeginCage(c.Target, nowUs);
            }

            public override void Tick(long nowUs, IReadOnlyList<IVol2Target> targets)
            {
                if (!_created && Run.Spec.Kind == Vol2ShapeKind.Path)
                {
                    if (_caged != null)
                    {
                        bool gone = !_caged.Alive || _caged.LifeRevision != _cageRev;
                        if (!gone) _cagePos = _caged.Position;
                        if (gone || nowUs >= _cageUntilUs)
                        {
                            Run.Log.Notes.Add("cage released at " + nowUs + " us" + (gone ? " (target gone)" : "") + " at (" + _cagePos.x.ToString("0.##") + ", " + _cagePos.z.ToString("0.##") + ")");
                            Make(Vol2Footprint.Disc(Level(_cagePos), Host.VerbTuning.PathResidueRadius), nowUs, _caged);
                        }
                    }
                    else if (Run.Emitter.Finished)
                    {
                        Vector3 p = _landing != null ? _landing.Position : Run.Spec.Origin + Run.Spec.Forward * ((Vol2PathEmitter)Run.Emitter).HeadAlong;
                        Make(Vol2Footprint.Disc(Level(p), Host.VerbTuning.PathResidueRadius), nowUs, _landing);
                    }
                }
                if (_res != null && _res.Alive && !_res.Attached)
                    for (int i = 0; i < targets.Count && _res.Alive; i++)
                    {
                        var t = targets[i];
                        if (t == null || !t.Alive || _touched.Contains(t.Id)) continue;
                        if (_res.Footprint.Touches(t, nowUs)) Touch1(t, nowUs, 0);
                    }
            }

            public override bool Idle(long nowUs) => _created && (_res == null || !_res.Alive);

            Vector3 Level(Vector3 p) { p.y = Run.Spec.Origin.y; return p; }

            // ---- the residue ----

            void Make(Vol2Footprint footprint, long nowUs, IVol2Target landing)
            {
                _created = true;
                var attack = Host.NewAttack(Run, 0, nowUs);
                long materialId = Host.Services.Ids.NextMaterial().Value;
                decimal amount = Run.Plan.Term(EffectKind.Residue);
                var step = Run.Budget.Pay(attack.Value, ContactKind.Transfer, EffectKind.Residue, amount, TriggerKind.None,
                    landing != null ? landing.Id : WorldTargetId, landing != null ? landing.LifeRevision : 0, 0, nowUs, materialId);
                if (!step.Accepted || step.Commands.Count == 0) { Run.Log.Notes.Add("residue refused by the budget: " + step.Reason); return; }
                _res = Host.Materials.Create(Run.Cast, Run.Glyph.Letter, Run.Glyph.Body, Vol2MaterialKind.Residue, step.Commands[0].ChildBudget, Run.Plan.LifeUs, footprint,
                    Vol2Faction.Player, Vol2Faction.Player, true, nowUs, 0, materialId);
                Run.Log.Materials.Add(_res);
            }

            /// <summary>One body touches the residue: it draws its share, once. Damage and the body action as for any hit.</summary>
            void Touch1(IVol2Target t, long nowUs, long swing)
            {
                if (_res == null || !_res.Alive || t == null || !t.Alive || !_touched.Add(t.Id)) return;
                var attack = Host.NewAttack(Run, swing, nowUs);
                var rec = Host.NewRecord(Run, MakeContact(t, nowUs, swing), attack, "Residue");
                rec.MaterialId = _res.Id.Value;
                decimal want = Math.Min(_res.Initial * Host.VerbTuning.ResidueShareFraction, _res.Remaining);
                if (!Host.Materials.TryDebit(_res.Id, want, nowUs, out decimal taken)) { rec.BudgetReason = "ResidueGone"; Host.NotifyContact(Run.Log, rec); return; }
                rec.BudgetAccepted = true; rec.BudgetReason = "ResidueShare";
                Host.Hit(Run, t, rec, taken, attack, nowUs, t.Position, swing, 0);
                FirstBody(t, rec, attack, nowUs);
                Host.NotifyContact(Run.Log, rec);
            }

            // ---- peaks ----

            long BindUs(IVol2Target t)
            {
                long us = RuleTypesV3.Microseconds(Numbers.Get("combat.bind.maxDuration"));
                if (Host.BossTest != null && Host.BossTest(t)) us = (long)(us * (double)Numbers.Get("combat.bind.bossMultiplier"));
                return Math.Min(us, Run.Body.DurationUs);
            }

            protected override void ApplyBody(IVol2Target t, Vol2ContactRecord rec, AttackId attack, long nowUs)
            {
                if ((_cage || _grow) && Run.Body.Action == Vol2BodyActionKind.Bind) Host.BodyEffect(Run, t, rec, attack, nowUs, BindUs(t));
                else base.ApplyBody(t, rec, attack, nowUs);
            }

            void BeginCage(IVol2Target t, long nowUs)
            {
                _caged = t; _cageRev = t.LifeRevision; _cagePos = t.Position; _cageUntilUs = nowUs + BindUs(t);
                Run.Log.Notes.Add("cage on " + t.Name + " until " + _cageUntilUs + " us (" + (BindUs(t)) + " us)");
            }

            /// <summary>국: the life of MY residues standing inside the ring grows x1.5, once each. Material is updated, not remade.</summary>
            void ExtendMine(long nowUs)
            {
                decimal factor = Numbers.Get("peak.국.lifeMultiplier");
                foreach (var m in Host.Materials.AliveSnapshot())
                {
                    if (m.Cast == Run.Cast || m.FactionId != Vol2Faction.Player || m.Kind != Vol2MaterialKind.Residue || m.Attached || m.Letter == '걱') continue;
                    if (Flat(m.Footprint.Centre, Run.Spec.Origin) > Run.Spec.RingOuter) continue;
                    if (Host.Materials.TryExtend(m.Id, factor, nowUs, out long extra)) Run.Log.Extended.Add(m.Id.Value + ":+" + extra);
                    else Run.Log.Extended.Add(m.Id.Value + ":refused");
                }
                var spec = Run.Spec;
                Host.FieldRequest(Run, "platform r=" + spec.RingRadius.ToString("0.##") + " life=" + Run.Plan.LifeUs + "us", f => f.OnPlatform(Run.Cast, spec.Origin, spec.RingRadius, Run.Plan.LifeUs));
            }
        }
    }
}
