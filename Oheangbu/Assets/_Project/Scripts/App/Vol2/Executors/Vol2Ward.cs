using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    // D308-156 (SPEC-VOL2-WARD-308): ㅜ + 받침 없음 (구 누 무 수 우) is a ward that stays a while, not a ring that touches once. The ward stands where it was cast
    // (it does not follow the caster), for the ward life, with the band of the 둘레 frame (space.ring.radius / thickness) as its wall:
    //   - an enemy shot (IVol2Projectile) that crosses the band from outside, or is on it, is shot down (Intercept); no reflection;
    //   - while the caster is within space.ring.radius of the centre, an enemy MELEE hit on the caster is blocked (0 damage) - no parry judgement, no reward, no groggy;
    //   - an enemy that touches the band gets the glyph's own one-time touch (the table's 한 번 effect, damage included) once per body.
    // This file holds the ward's pieces that are not the verb run itself (that is Vol2OnceExecutor.WardRun): the emitter, the event record, the temporary number,
    // and the melee port over the player's incoming-damage scale.

    /// <summary>TEMPORARY EXCEPTION of D308-156 (Exit Blocking): the ward life. The Spec puts ward.life (2.0 s, TEST) in the data, but the NumbersV3 contract is frozen
    /// with the E0 golden (414 rows: Golden/source.json and Docs/Research/e0/numbers_v3.csv are pinned by the pure suite), so a new key there means re-freezing the
    /// E0 research golden - a contract decision of its own.
    ///  TE-W1 LifeSeconds: used ONLY when the loaded numbers have no "ward.life" row. The executor reads "ward.life" from the numbers first, so moving the number
    ///        into the data later is a data change with no code change. Every ward cast notes which source it used (cast log Notes: "ward life ... from numbers|TE-W1").</summary>
    public sealed class Vol2WardTuning
    {
        public const string LifeKey = "ward.life";
        public float LifeSeconds = 2.0f;

        /// <summary>The ward life in microseconds and where it came from ("numbers" or "TE-W1").</summary>
        public long LifeUs(NumbersV3Dto numbers, out string source)
        {
            if (numbers != null)
                foreach (var row in numbers.Rows)
                    if (row.Key == LifeKey && row.Value.HasValue && row.Value.Value > 0m) { source = "numbers"; return RuleTypesV3.Microseconds(row.Value.Value); }
            source = "TE-W1";
            return Vol2Time.FromSeconds(Math.Max(0f, LifeSeconds));
        }
    }

    /// <summary>One thing the ward did besides touching a body: a shot taken ("Intercept") or a melee hit stopped ("Block"). Kept on the cast log.</summary>
    public sealed class Vol2WardEvent
    {
        public string Kind = "";
        public long AtUs;
        public Vector3 Point;
        public string Note = "";
    }

    /// <summary>The ward's band for the ward life: fixed where it was cast, it reports a body ONCE, the first tick the body's path of that tick crosses the band
    /// (standing on it at the cast, walking or dashing through it later). Finished when the life is over; it reports nothing at or after the end.</summary>
    public sealed class Vol2WardEmitter : IVol2ShapeEmitter
    {
        readonly HashSet<long> _touched = new HashSet<long>();
        readonly List<(float d, IVol2Target t)> _found = new List<(float, IVol2Target)>();
        public Vol2ShapeKind Kind => Vol2ShapeKind.Ring;
        public Vol2ShapeSpec Spec { get; }
        public long StartUs { get; }
        public long EndUs { get; }
        public bool Finished { get; private set; }
        public int Touched => _touched.Count;

        public Vol2WardEmitter(Vol2ShapeSpec spec, long startUs, long endUs) { Spec = spec; StartUs = startUs; EndUs = endUs; }

        public bool Standing(long nowUs) => !Finished && nowUs >= StartUs && nowUs < EndUs;

        public void End() { Finished = true; }

        public void Tick(long nowUs, IReadOnlyList<IVol2Target> targets, List<Vol2ShapeContact> output)
        {
            if (Finished) return;
            if (nowUs >= EndUs) { Finished = true; return; }
            if (nowUs < StartUs) return;
            _found.Clear();
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || !t.Alive || _touched.Contains(t.Id)) continue;
                if (!Vol2ShapeGeometry.RingCrossed(Spec.Origin, Spec.RingInner, Spec.RingOuter, t.Previous, t.Position, t.Radius)) continue;
                Vector3 a = t.Position - Spec.Origin; a.y = 0f;
                _found.Add((a.magnitude, t));
            }
            _found.Sort((a, b) => { int c = a.d.CompareTo(b.d); return c != 0 ? c : a.t.Id.CompareTo(b.t.Id); });
            foreach (var (d, t) in _found)
            {
                _touched.Add(t.Id);
                output.Add(new Vol2ShapeContact { Shape = Vol2ShapeKind.Ring, Target = t, TargetId = t.Id, LifeRevision = t.LifeRevision, NowUs = nowUs, Along = d, Point = t.Position, Note = "ward" });
            }
        }

        /// <summary>A shot's step from <paramref name="from"/> to <paramref name="to"/> against the ward's wall (a cylinder of the band, any height): true when it crosses
        /// the band coming from outside or is on it (not moving outward); a shot from inside the hollow (fired within the ward) is not taken. <paramref name="point"/> = where it
        /// first meets the outer face (or its start, when it was already on the band), at the shot's own height there.</summary>
        public static bool ShotTouches(Vol2ShapeSpec spec, Vector3 from, Vector3 to, float shotRadius, out Vector3 point)
        {
            point = to;
            float inner = spec.RingInner, outer = spec.RingOuter;
            if (!Vol2ShapeGeometry.RingCrossed(spec.Origin, inner, outer, from, to, shotRadius)) return false;
            float startDist = Flat(from, spec.Origin);
            if (startDist + shotRadius < inner) return false;                   // fired from inside the hollow: the ward faces outward
            float reach = outer + shotRadius;
            if (startDist <= reach)
            {
                // already on the band: taken unless it is on its way out (a shot fired from within the ward leaves through the band)
                Vector3 step = to - from, radial = from - spec.Origin; step.y = 0f; radial.y = 0f;
                if (Vector3.Dot(step, radial) > 0f) return false;
                point = from; return true;
            }
            // the first time the step's flat distance to the centre comes down to reach
            Vector2 c = new Vector2(spec.Origin.x, spec.Origin.z), a = new Vector2(from.x, from.z), b = new Vector2(to.x, to.z);
            Vector2 d = b - a, m = a - c;
            float A = Vector2.Dot(d, d), B = 2f * Vector2.Dot(m, d), C = Vector2.Dot(m, m) - reach * reach;
            float t = 1f;
            if (A > 1e-10f)
            {
                float disc = B * B - 4f * A * C;
                if (disc >= 0f) t = Mathf.Clamp01((-B - Mathf.Sqrt(disc)) / (2f * A));
                else t = Mathf.Clamp01(-B / (2f * A));                       // grazes the band from outside: the closest approach
            }
            point = Vector3.Lerp(from, to, t);
            return true;
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return (a - b).magnitude; }
    }

    /// <summary>The player's side of the ward's melee block (D308-156). The coordinator hands it <c>tryBlock</c>: at the moment an enemy melee hit would really land
    /// on the player (after the dodge / alive checks), the port asks it; true = this hit is blocked (no damage, no hit event). A port with no player does nothing.</summary>
    public interface IVol2MeleeWardPort
    {
        /// <summary>Makes sure the player's incoming melee hits ask <paramref name="tryBlock"/> (idempotent; binds again when the player object changed).</summary>
        void Ensure(Func<string, bool> tryBlock);
        /// <summary>Lets go of the player (scope disposed).</summary>
        void Release();
        /// <summary>What it is bound to now (for the log).</summary>
        string Describe { get; }
    }

    /// <summary>The real port: ONE incoming-damage scale on the player (PlayerVitals.BindIncomingDamageScale, the same seam EAWardRuntime / DomainWardEffects308 use).
    /// The scale answers 1 for every kind but Melee / ElementalMelee, and for those 0 only when the coordinator says a standing ward covers the caster NOW - so a scale
    /// left bound after a ward ended blocks nothing (the coverage is read live from the running casts, never cached). Melee kinds reach the scales only from
    /// PlayerVitals.TakeAttackDamage (WouldLand308 asks for ranged kinds only), so one landing hit is asked once.</summary>
    public sealed class Vol2PlayerMeleeWardPort : IVol2MeleeWardPort
    {
        readonly Func<PlayerVitals> _player;
        readonly Func<IncomingDamageKind, float> _scale;
        PlayerVitals _bound;
        Func<string, bool> _tryBlock;
        public int Asked { get; private set; }
        public int Blocked { get; private set; }

        public Vol2PlayerMeleeWardPort(Func<PlayerVitals> player) { _player = player; _scale = Scale; }

        public string Describe => _bound != null ? "player " + _bound.name : "no player";

        public void Ensure(Func<string, bool> tryBlock)
        {
            if (tryBlock != null) _tryBlock = tryBlock;
            PlayerVitals p = null;
            try { p = _player != null ? _player() : null; } catch (Exception e) { Debug.LogException(e); }
            if (p != null && ReferenceEquals(p, _bound)) return;
            Release();
            if (p == null) return;
            p.BindIncomingDamageScale(_scale);
            _bound = p;
        }

        public void Release()
        {
            if (!ReferenceEquals(_bound, null) && _bound != null) _bound.UnbindIncomingDamageScale(_scale);
            _bound = null;
        }

        float Scale(IncomingDamageKind kind)
        {
            if (kind != IncomingDamageKind.Melee && kind != IncomingDamageKind.ElementalMelee) return 1f;
            Asked++;
            var ask = _tryBlock;
            bool blocked = false;
            try { blocked = ask != null && ask(kind.ToString()); } catch (Exception e) { Debug.LogException(e); }
            if (blocked) Blocked++;
            return blocked ? 0f : 1f;
        }
    }
}
