using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    // #308 WP-05, question Q5 (USER_ANSWERS, DECISIONS D308-13): "the spell rule plus a TEST target; real enemy AI and world
    // placement are separate work". The water path glyph pulls an enemy that is in the air down to the ground. No enemy of
    // the game flies yet, so this file holds the seam the spell talks to and one TEST target that implements it.
    // A flying enemy built later implements the same three members on (or next to) its EnemyVitals.
    public interface ISpellAirborne308
    {
        // In the air at that time of the wiring's scaled clock.
        bool IsAirborne(float now);
        // Forced to the ground until the given time. owner = the attack id of the hit that pulled it (also the owner id of
        // the slow that comes with it). false = this target cannot be in the air at all; nothing changed.
        bool PullDown(long owner, float until);
        // The pull of that owner ends early (the spell effects were cleared: death, rest, scene leave).
        void Release(long owner);
    }

    // TEST target for the pull-down clause: a dummy that counts as airborne until a spell pulls it down, and is airborne
    // again when every pull has run out. It carries no AI, takes no decision and changes no damage. The optional body is
    // only this dummy's own picture of the state (a child that hovers); spell rules never read it.
    [DisallowMultipleComponent]
    public sealed class SpellAirborneTestTarget308 : MonoBehaviour, ISpellAirborne308
    {
        struct Pull { public long Owner; public float Until; }

        [Tooltip("TEST: whether this dummy is an airborne target at all")]
        [SerializeField] private bool _airborne = true;
        [Tooltip("Optional child that hovers while airborne and sits on the ground while pulled down (picture only)")]
        [SerializeField] private Transform _body;
        [Tooltip("TEST: local height of the body while airborne")]
        [SerializeField, Min(0f)] private float _hoverHeight = 2f;

        private readonly List<Pull> _pulls = new List<Pull>();

        public int PullCount { get; private set; }       // how many pulls it has received (checks read this)
        public int ActivePulls => _pulls.Count;

        public bool IsAirborne(float now)
        {
            if (!_airborne) return false;
            for (int i = 0; i < _pulls.Count; i++) if (now < _pulls[i].Until) return false;
            return true;
        }

        public bool PullDown(long owner, float until)
        {
            if (!_airborne || owner <= 0 || float.IsNaN(until) || float.IsInfinity(until)) return false;
            var pull = new Pull { Owner = owner, Until = until };
            int at = IndexOf(owner);
            if (at >= 0) _pulls[at] = pull; else _pulls.Add(pull);
            PullCount++;
            return true;
        }

        public void Release(long owner)
        {
            int at = IndexOf(owner);
            if (at >= 0) _pulls.RemoveAt(at);
        }

        private int IndexOf(long owner)
        {
            for (int i = 0; i < _pulls.Count; i++) if (_pulls[i].Owner == owner) return i;
            return -1;
        }

        private void Update()
        {
            float now = Time.time;
            for (int i = _pulls.Count - 1; i >= 0; i--) if (now >= _pulls[i].Until) _pulls.RemoveAt(i);
            if (_body == null) return;
            Vector3 local = _body.localPosition;
            local.y = IsAirborne(now) ? _hoverHeight : 0f;
            _body.localPosition = local;
        }

        private void OnDisable() { _pulls.Clear(); }
    }
}
