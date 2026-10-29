using System.Collections.Generic;
using System.Globalization;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>한 번 (slice 3), moved behind the verb registry unchanged in behaviour: the shape's contact is the whole effect. A ㅓ 한 번 is the
    /// parry: its attachment window is exposed to the defence judge and its swing contacts are recorded with no effect.
    /// D308-156 (SPEC-VOL2-WARD-308): a ㅜ 한 번 (구 누 무 수 우) is the WARD - <see cref="WardRun"/>: it stands for the ward life where it was cast, shoots down the
    /// enemy shots that reach its band, blocks the melee hits on a caster inside it, and gives each body that touches its band the glyph's one-time touch once.
    /// The other 한 번 frames (ㅏ ㅗ ㅓ) and every 받침 ㅜ glyph are untouched.</summary>
    public sealed class Vol2OnceExecutor : IVol2VerbExecutor
    {
        public Verb Verb => Verb.Once;
        public string Name => "Once(한 번)";

        /// <summary>D308-156: the ward life's temporary number (TE-W1; the numbers' "ward.life" wins when the data has it).</summary>
        public Vol2WardTuning WardTuning { get; } = new Vol2WardTuning();
        /// <summary>D308-156: where the ward finds enemy shots. Null (default) = the scope's one source, the 돌아온다 executor's (<see cref="Vol2ReturnExecutor.Projectiles"/>,
        /// its hub: what the lab and the fixtures register their shots on, and the scans of the scene). A fixture without a Return executor sets its own here; with neither,
        /// <see cref="OwnHub"/> (scans on) is used.</summary>
        public IVol2ProjectileSource Projectiles { get; set; }
        public Vol2ProjectileHub OwnHub { get; } = new Vol2ProjectileHub();
        readonly Vol2ReturnTuning _ownShotTuning = new Vol2ReturnTuning();

        public IVol2VerbRun Begin(Vol2CastRun run)
        {
            if (IsWard(run.Glyph)) return new WardRun(run, this);
            return new OnceRun(run);
        }

        /// <summary>ㅜ + 받침 없음 (and not a parry, not the recovery): the five ward glyphs.</summary>
        public static bool IsWard(Glyph g) => g.Frame == Frame.Ring && g.Verb == Verb.Once && !g.IsParry;

        /// <summary>The scope's shot source and the shot size / search reach it is judged with (the 돌아온다 executor's TE-S7-5 numbers, so ONE shot size holds in the scope).</summary>
        internal IVol2ProjectileSource ShotSource(Vol2CastCoordinator host, out Vol2ReturnTuning shotTuning)
        {
            Vol2ReturnExecutor ret = null;
            if (host != null && host.Verbs.TryGet(Verb.Return, out var e)) ret = e as Vol2ReturnExecutor;
            shotTuning = ret != null ? ret.Tuning : _ownShotTuning;
            if (Projectiles != null) return Projectiles;
            if (ret != null && ret.Projectiles != null) return ret.Projectiles;
            return OwnHub;
        }

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

        /// <summary>D308-156: the ward of one ㅜ 한 번 cast. The emitter (<see cref="Vol2WardEmitter"/>) carries the band for the ward life and reports each body once;
        /// a contact is the ordinary 한 번 touch (the rules' budget: FirstContact once per body, then the damage and the body's action). Every tick it also looks for
        /// enemy shots that reach the band and takes them (Intercept). The melee block is asked by the coordinator (<see cref="Covers"/>) when a hit would land.
        /// The cast ends with the ward (emitter finished); a reset (Abort) takes the ward down at once.</summary>
        internal sealed class WardRun : Vol2VerbRunBase, IVol2WardRun
        {
            readonly Vol2OnceExecutor _ex;
            readonly List<IVol2Projectile> _found = new List<IVol2Projectile>();
            readonly Dictionary<object, Vector3> _shotPrev = new Dictionary<object, Vector3>();
            readonly HashSet<object> _shotDone = new HashSet<object>();
            Vol2WardEmitter _emitter;
            string _lifeSource = "";
            bool _aborted;

            public WardRun(Vol2CastRun run, Vol2OnceExecutor ex) : base(run) { _ex = ex; }

            public override IVol2ShapeEmitter CreateEmitter(Vol2ShapeSpec spec, long nowUs)
            {
                long life = _ex.WardTuning.LifeUs(Numbers, out _lifeSource);
                _emitter = new Vol2WardEmitter(spec, nowUs, nowUs + life);
                Run.Log.WardUntilUs = _emitter.EndUs;
                return _emitter;
            }

            public override void Start(long nowUs, IReadOnlyList<IVol2Target> targets)
            {
                var inv = CultureInfo.InvariantCulture;
                Run.Log.Notes.Add("ward " + Vol2Time.ToSecondsF(_emitter.EndUs - _emitter.StartUs).ToString("0.###", inv) + " s from " + _lifeSource + ", r " + Run.Spec.RingRadius.ToString("0.##", inv)
                    + " m (band " + Run.Spec.RingInner.ToString("0.##", inv) + "-" + Run.Spec.RingOuter.ToString("0.##", inv) + " m) at " + Run.Spec.Origin.ToString("F2"));
                Host.EnsureMeleeWard();
            }

            public override void OnContact(Vol2ShapeContact c, long nowUs) { if (!_aborted) Touch(c, nowUs); }

            public override void Tick(long nowUs, IReadOnlyList<IVol2Target> targets)
            {
                if (_aborted || _emitter == null || !_emitter.Standing(nowUs)) return;
                SweepShots(nowUs);
            }

            public override void Abort(long nowUs) { _aborted = true; _emitter?.End(); }

            // ---- IVol2WardRun (the coordinator asks while the cast is active) ----

            public bool Covers(long nowUs, Vector3 casterPosition)
                => !_aborted && _emitter != null && _emitter.Standing(nowUs) && Flat(casterPosition, Run.Spec.Origin) <= Run.Spec.RingRadius;

            public Vector3 BlockPoint(Vector3 casterPosition, IReadOnlyList<IVol2Target> targets)
            {
                // the hit carries no attacker to the incoming-damage scale: the wall point toward the nearest living body is where the blow is drawn (presentation only)
                IVol2Target nearest = null; float best = float.MaxValue;
                for (int i = 0; targets != null && i < targets.Count; i++)
                {
                    var t = targets[i];
                    if (t == null || !t.Alive) continue;
                    float d = Flat(t.Position, casterPosition);
                    if (d < best) { best = d; nearest = t; }
                }
                Vector3 origin = Run.Spec.Origin;
                Vector3 dir = nearest != null ? Vol2ShapeGeometry.FlatUnit(nearest.Position - origin) : Run.Spec.Forward;
                return origin + dir * Run.Spec.RingRadius + Vector3.up * Host.Tuning.ChestHeight;
            }

            public void RecordBlock(long nowUs, Vector3 point, string note)
            {
                Run.Log.WardEvents.Add(new Vol2WardEvent { Kind = "Block", AtUs = nowUs, Point = point, Note = note ?? "" });
                Run.Log.Notes.Add("melee blocked (" + note + ") at " + nowUs + " us");
            }

            // ---- enemy shots ----

            void SweepShots(long nowUs)
            {
                var source = _ex.ShotSource(Host, out var shotTuning);
                if (source == null) return;
                _found.Clear();
                source.Gather(Host, Run.Spec.Origin, Mathf.Max(shotTuning.ProjectileReach, Run.Spec.RingOuter + shotTuning.ProjectileRadius), _found);
                for (int i = 0; i < _found.Count; i++)
                {
                    var p = _found[i];
                    if (p == null || !p.InFlight || _shotDone.Contains(p.Identity)) continue;
                    Vector3 pos = p.Position;                                  // read BEFORE Intercept: the old seam moves Position to the shooter afterwards
                    Vector3 prev = _shotPrev.TryGetValue(p.Identity, out var pv) ? pv : pos;
                    _shotPrev[p.Identity] = pos;
                    if (!Vol2WardEmitter.ShotTouches(Run.Spec, prev, pos, shotTuning.ProjectileRadius, out Vector3 point)) continue;
                    if (!p.Intercept(Run.Cast.Value)) continue;
                    _shotDone.Add(p.Identity);
                    Run.Log.WardEvents.Add(new Vol2WardEvent { Kind = "Intercept", AtUs = nowUs, Point = point, Note = p.PowerHp > 0f ? "power " + p.PowerHp.ToString("0.##", CultureInfo.InvariantCulture) + " hp" : "" });
                    Run.Log.Notes.Add("shot taken by the ward at " + nowUs + " us");
                    Host.NotifyWardIntercept(Run, point);
                }
            }
        }
    }
}
