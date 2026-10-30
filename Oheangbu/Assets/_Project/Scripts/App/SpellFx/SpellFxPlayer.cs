using System;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>The root of a catalogue prefab (D308-153). The director builds the prefab at a cast's begin, calls these methods from the executor's events and removes it when
    /// <see cref="ExpireUs"/> passes on the scope clock. The player owns no timer and no Time call: every moment it knows is a scope-clock microsecond given to it.
    /// It relays each call to every <see cref="ISpellFxPart"/> under its root (found once, inactive ones included).</summary>
    public sealed class SpellFxPlayer : MonoBehaviour
    {
        /// <summary>No prefab outlives its launch by more than this (the cast is then ended for the look: <see cref="SpellFxEndReason.TimedOut"/>).</summary>
        public const float HardCapSeconds = 15f;

        [Tooltip("The least time (from the launch) the prefab stays after the executor ended the cast: the length of its own animation.")]
        [SerializeField, Min(0f)] float playSeconds = 2f;
        [Tooltip("How long it stays after the executor's end (the look-tail); the later of this and the play length wins.")]
        [SerializeField, Min(0f)] float tailSeconds = 0.5f;

        ISpellFxPart[] _parts;
        ISpellFxPreLaunch[] _pre;
        bool _ended;

        public SpellFxCast Cast { get; private set; }
        public bool Launched { get; private set; }
        public bool Ended => _ended;
        public SpellFxEndReason EndReason { get; private set; }
        public float PlaySeconds => playSeconds;
        public float TailSeconds => tailSeconds;
        /// <summary>Calls relayed so far (bookkeeping for the fixture / status, read by nothing in the game).</summary>
        public int BeginCount, LaunchCount, TickCount, HitCount, MaterialCount, ReactionCount, EndCount, InterceptCount, BlockCount;

        /// <summary>Gives the prefab its cast and puts it at the cast point looking along the aim. Safe before the prefab is active (the director builds it inactive).</summary>
        public void Begin(SpellFxCast cast)
        {
            Cast = cast;
            _ended = false; Launched = false;
            Gather();
            transform.SetPositionAndRotation(cast.Origin, Quaternion.LookRotation(cast.Forward.sqrMagnitude > 1e-6f ? cast.Forward : Vector3.forward, Vector3.up));
            BeginCount++;
            Each(p => p.OnBegin(cast));
        }

        /// <summary>One director step: the first one at or after the launch fires OnLaunch, then every step ticks the parts with the age - also through the tail after the
        /// executor's end (a graph driven by Age keeps running; <c>cast.Ended</c> tells a part the cast is over).</summary>
        public void Tick(long nowUs)
        {
            if (Cast == null) return;
            if (!Launched)
            {
                if (nowUs < Cast.LaunchUs) { PreLaunch(nowUs); return; }
                Launch();
            }
            float age = Cast.AgeAt(nowUs);
            TickCount++;
            Each(p => p.OnTick(Cast, age));
        }

        /// <summary>The commit-to-launch stretch (a 한 번 fan / path waits for the swing's apex): the parts that asked for it (<see cref="ISpellFxPreLaunch"/>) are told how long is left.</summary>
        void PreLaunch(long nowUs)
        {
            Gather();
            if (_pre.Length == 0 || _ended) return;
            float left = (float)((Cast.LaunchUs - nowUs) / 1000000.0);
            for (int i = 0; i < _pre.Length; i++)
            {
                var part = _pre[i];
                if (part == null || (part is UnityEngine.Object o && o == null)) continue;
                try { part.OnPreLaunch(Cast, left); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        void Launch()
        {
            if (Launched || Cast == null) return;
            Launched = true; LaunchCount++;
            Each(p => p.OnLaunch(Cast));
        }

        public void Hit(SpellFxHit hit)
        {
            if (Cast == null || _ended) return;
            if (!Launched) Launch();          // a contact in the commit frame: the shape has left by definition
            HitCount++;
            Each(p => p.OnHit(Cast, hit));
        }

        public void Material(SpellFxMaterial material)
        {
            if (Cast == null || _ended) return;
            MaterialCount++;
            Each(p => p.OnMaterial(Cast, material));
        }

        public void Reaction(SpellFxReaction reaction)
        {
            if (Cast == null || _ended) return;
            ReactionCount++;
            Each(p => p.OnReaction(Cast, reaction));
        }

        /// <summary>D308-156: the cast's ward took an enemy shot at <paramref name="point"/>.</summary>
        public void Intercept(Vector3 point)
        {
            if (Cast == null || _ended) return;
            if (!Launched) Launch();          // the ward stands from its commit
            InterceptCount++;
            Each(p => p.OnIntercept(Cast, point));
        }

        /// <summary>D308-156: the cast's ward stopped a melee hit (drawn at <paramref name="point"/>).</summary>
        public void Block(Vector3 point)
        {
            if (Cast == null || _ended) return;
            if (!Launched) Launch();
            BlockCount++;
            Each(p => p.OnBlock(Cast, point));
        }

        /// <summary>The cast is over for the look (the executor's end, a reset, the cap). Once.</summary>
        public void End(SpellFxEndReason reason)
        {
            if (Cast == null || _ended) return;
            _ended = true; EndReason = reason; EndCount++;
            Each(p => p.OnEnd(Cast, reason));
        }

        /// <summary>Scope-clock microsecond at which the director removes the prefab. A cast the executor has not ended lives to the cap; an ended one to the later of
        /// (launch + play length) and (end + tail), never past the cap.</summary>
        public long ExpireUs()
        {
            if (Cast == null) return 0;
            long cap = Cast.LaunchUs + Vol2Time.FromSeconds(HardCapSeconds);
            if (!Cast.Ended) return cap;
            long byPlay = Cast.LaunchUs + Vol2Time.FromSeconds(playSeconds);
            long byTail = Cast.EndUs + Vol2Time.FromSeconds(tailSeconds);
            return Math.Min(cap, Math.Max(byPlay, byTail));
        }

        void Gather()
        {
            if (_parts == null) _parts = GetComponentsInChildren<ISpellFxPart>(true);
            if (_pre == null) _pre = GetComponentsInChildren<ISpellFxPreLaunch>(true);
        }

        void Each(Action<ISpellFxPart> call)
        {
            Gather();
            for (int i = 0; i < _parts.Length; i++)
            {
                var part = _parts[i];
                if (part == null || (part is UnityEngine.Object o && o == null)) continue;
                try { call(part); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
