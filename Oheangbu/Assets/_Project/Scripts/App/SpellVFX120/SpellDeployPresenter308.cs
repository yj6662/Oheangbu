using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // #308 present add-on: the spell presenter on the deploy layer (SPEC-SPELL-120-308 section 9: "the deploy layer implements
    // ISpellPresenter, or wraps Vfx120SpellPresenter308, and changes only the registration line"). It does both:
    //   - a glyph the layer has switched on (SpellDeploy308ProfileSO.EnabledLetters, the existing data switch deploy308-enable)
    //     is presented by the layer: the call goes to the director's SpellPresentStage308, which routes it by the sheet
    //     (SpellPresent308SheetSO) into ink - bursts, ground marks, thrown drops, carried marks;
    //   - every other glyph, and every glyph while the layer is not up, goes to the interim catalogue presenter unchanged.
    // The switch is read per call, so a glyph can be switched while a scene runs; a handle keeps the side it began on.
    // Rules never read anything back: handlers get a handle and pass it back for cues. The wiring calls this class through
    // SpellPresenterGuard308, which isolates every call (a failure here never reaches a rule).
    public sealed class SpellDeployPresenter308 : ISpellPresenter, ISpellPresenterBinding, ISpellPresenterProbe
    {
        // Handles of the interim presenter that may still be alive. A bound of bookkeeping: the oldest is forgotten first.
        private const int FallbackCapacity = 256;

        private readonly Vfx120SpellPresenter308 _fallback = new Vfx120SpellPresenter308();
        private readonly Dictionary<int, SpellFxHandle> _fallbackHandles = new Dictionary<int, SpellFxHandle>();
        private readonly Queue<int> _fallbackOrder = new Queue<int>();
        private CombatLoopWiring _wiring;
        private SpellVisualSetSO _visuals;
        private int _next;

        /// <summary>Calls begun on the deploy layer and on the interim presenter (checks and the status command read these).</summary>
        public int DeployBegun { get; private set; }
        public int FallbackBegun { get; private set; }
        public Vfx120SpellPresenter308 Fallback => _fallback;

        public void Bind(SpellVisualSetSO visuals, Transform owner)
        {
            _visuals = visuals;
            _wiring = owner != null ? owner.GetComponent<CombatLoopWiring>() : null;
            _fallback.Bind(visuals, owner);
        }

        // The stage lives on the layer's director, which the wiring boots after this object was injected: looked up per call.
        private SpellPresentStage308 Stage
        {
            get
            {
                var director = _wiring != null ? _wiring.DeployDirector308 : null;
                var stage = director != null ? director.Present : null;
                if (stage == null || !stage.Ready) return null;
                stage.BindVisuals(_visuals);
                return stage;
            }
        }

        public bool CanShow(in SpellFxRequest request)
        {
            var stage = Stage;
            return (stage != null && stage.Uses(request.Letter)) || _fallback.CanShow(request);
        }

        public SpellFxHandle Begin(in SpellFxRequest request)
        {
            var stage = Stage;
            if (stage != null && stage.Uses(request.Letter))
            {
                int id = NextId();
                stage.Begin(id, request);
                DeployBegun++;
                return new SpellFxHandle(id);
            }
            var inner = _fallback.Begin(request);
            if (!inner.Shown) return default;
            int key = NextId();
            while (_fallbackOrder.Count >= FallbackCapacity) _fallbackHandles.Remove(_fallbackOrder.Dequeue());
            _fallbackHandles[key] = inner; _fallbackOrder.Enqueue(key);
            FallbackBegun++;
            return new SpellFxHandle(key);
        }

        public void Cue(SpellFxHandle handle, SpellFxCue cue, in SpellFxCueArgs args)
        {
            if (!handle.Shown) return;
            if (_fallbackHandles.TryGetValue(handle.Id, out var inner)) { _fallback.Cue(inner, cue, args); return; }
            var stage = Stage;
            if (stage != null) stage.Cue(handle.Id, cue, args);
        }

        public void End(SpellFxHandle handle, bool immediate = false)
        {
            if (!handle.Shown) return;
            if (_fallbackHandles.TryGetValue(handle.Id, out var inner))
            {
                _fallbackHandles.Remove(handle.Id);
                _fallback.End(inner, immediate);
                return;
            }
            var stage = Stage;
            if (stage != null) stage.End(handle.Id, immediate);
        }

        // Death, rest, scene leave: both sides let go of everything.
        public void EndAll()
        {
            _fallbackHandles.Clear(); _fallbackOrder.Clear();
            _fallback.EndAll();
            var director = _wiring != null ? _wiring.DeployDirector308 : null;
            var stage = director != null ? director.Present : null;
            if (stage != null) stage.EndAll();
        }

        private int NextId()
        {
            int id = ++_next;
            if (id == 0) id = ++_next;
            return id;
        }
    }
}
