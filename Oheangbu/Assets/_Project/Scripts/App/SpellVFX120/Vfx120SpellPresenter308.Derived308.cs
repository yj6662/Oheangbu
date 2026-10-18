using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.App.SpellVFX120
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.56: the derived shots of the second-strike letters (낫 split, 안 droplets, 속 ricochet,
    // 간 ember spread). The rule already says where each one goes (Split targets / the next target); this flies an ink body
    // there so the second strike is seen. A profile without Derived308Prefab never enters the table (old behaviour). The cue
    // is never swallowed: the default handling (SetSecondaryTargets and the rest) still runs after this. Presentation only:
    // the rule's clocks, targets and damage are read, never changed.
    public sealed partial class Vfx120SpellPresenter308
    {
        sealed class DerivedBody
        {
            public SpellFxRequest Request;
            public GameObject Prefab;
            public float Until;
        }

        // how long a handle is remembered (the effects never End their handle); flight clock bounds; fallback speed
        const float DerivedRemember = 30f, DerivedMinFlight = .2f, DerivedMaxFlight = .9f, DerivedMaxClock = 2f, DerivedSpeed = 16f;

        readonly Dictionary<int, DerivedBody> _derived = new Dictionary<int, DerivedBody>();
        readonly List<int> _staleDerived = new List<int>();

        void NoteDerived308(int id, Live live)
        {
            var profile = live.Fx != null ? live.Fx.Profile : null;
            if (profile == null || profile.Derived308Prefab == null || live.Request.Role != SpellFxRole.Projectile) return;
            float now = Time.time;
            _staleDerived.Clear();
            foreach (var pair in _derived) if (pair.Value.Until < now) _staleDerived.Add(pair.Key);
            foreach (int stale in _staleDerived) _derived.Remove(stale);
            _derived[id] = new DerivedBody { Request = live.Request, Prefab = profile.Derived308Prefab, Until = now + DerivedRemember };
        }

        // never swallows the cue
        void Derived308(int id, SpellFxCue cue, in SpellFxCueArgs args)
        {
            if (!_derived.TryGetValue(id, out var body) || body.Prefab == null) return;
            Vector3 from = args.Point + Vector3.up * .4f;
            if (cue == SpellFxCue.Split)
            {
                if (args.Targets == null) return;
                for (int i = 0; i < args.Targets.Length; i++)
                    if (args.Targets[i] != null) LaunchDerived308(body, from, args.Targets[i], args.At, i);
                return;
            }
            // 간: the ember moves on to the next enemy (Target null = it went out)
            if ((cue == SpellFxCue.TargetDefeated || cue == SpellFxCue.Expire) && args.Target != null)
                LaunchDerived308(body, from, args.Target, args.At, 0);
        }

        void LaunchDerived308(DerivedBody body, Vector3 from, Transform target, float at, int index)
        {
            var eye = Camera.main;
            if (eye != null && (from - eye.transform.position).sqrMagnitude < EyeClearMeters * EyeClearMeters) return;
            Vector3 to = target.position + Vector3.up * .9f;
            float clock = at - Time.time;
            clock = clock > .05f ? Mathf.Min(clock, DerivedMaxClock) : Mathf.Clamp(Vector3.Distance(from, to) / DerivedSpeed, DerivedMinFlight, DerivedMaxFlight);
            Sweep();
            var request = body.Request;
            var live = new Live { Request = request };
            try
            {
                var instance = Object.Instantiate(body.Prefab);
                live.Instance = instance;
                instance.name = "Spell308_" + request.Letter + "_Derived" + index;
                if (_owner != null && instance.scene != _owner.gameObject.scene) SceneManager.MoveGameObjectToScene(instance, _owner.gameObject.scene);
                live.Sequence = instance.GetComponent<SpellSequenceEffect>();
                live.Fx = live.Sequence as Vfx120Effect;
                if (live.Fx == null || live.Fx.Profile == null) { Release(live); return; }
                live.Fx.PreviewControlled = false; live.Fx.DemonstrationCues = false;
                live.Fx.SetImpactClock(clock);
                live.Fx.Begin(from, target, to, live.Fx.Profile.Pigment);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                Release(live);
                return;
            }
            int id = ++_next; if (id == 0) id = ++_next;
            _live.Add(id, live);
        }

        void ForgetDerived308(int id) => _derived.Remove(id);

        void ClearDerived308() => _derived.Clear();
    }
}
