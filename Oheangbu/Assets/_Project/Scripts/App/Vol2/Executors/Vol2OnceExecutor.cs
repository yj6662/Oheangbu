using System.Collections.Generic;
using Oheangbu.Vol2.Rules;

namespace Oheangbu.App
{
    /// <summary>한 번 (slice 3), moved behind the verb registry unchanged in behaviour: the shape's contact is the whole effect. A ㅓ 한 번 is the
    /// parry: its attachment window is exposed to the defence judge and its swing contacts are recorded with no effect.</summary>
    public sealed class Vol2OnceExecutor : IVol2VerbExecutor
    {
        public Verb Verb => Verb.Once;
        public string Name => "Once(한 번)";
        public IVol2VerbRun Begin(Vol2CastRun run) => new OnceRun(run);

        sealed class OnceRun : Vol2VerbRunBase
        {
            public OnceRun(Vol2CastRun run) : base(run) { }

            public override IVol2ShapeEmitter CreateEmitter(Vol2ShapeSpec spec, long nowUs)
            {
                switch (spec.Kind)
                {
                    case Vol2ShapeKind.Path: return new Vol2PathEmitter(spec, nowUs, Run.Plan.FirstOnly, Host.Wall, Host.Tuning.ChestHeight);
                    case Vol2ShapeKind.Fan: return new Vol2FanEmitter(spec, nowUs);
                    case Vol2ShapeKind.Ring: return new Vol2RingEmitter(spec, nowUs);
                    default: return new Vol2AttachmentHost(spec, nowUs, Host.Defence.ParryWindowUs);
                }
            }

            public override void OnContact(Vol2ShapeContact c, long nowUs)
            {
                if (Run.Glyph.IsParry)
                {
                    var attack = Host.NewAttack(Run, c.SwingId, nowUs);
                    var rec = Host.NewRecord(Run, c, attack, "Touch");
                    rec.BudgetReason = "ParryNoEffect";
                    Host.NotifyContact(Run.Log, rec);
                    return;
                }
                Touch(c, nowUs);
            }
        }
    }
}
