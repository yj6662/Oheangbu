using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityEngine.SceneManagement;

namespace Oheangbu.App.SpellVFX120
{
    // Today's ISpellPresenter (SPEC-SPELL-120-308 section 9): the glyph's catalogue prefab from SpellVisualSet_120, driven
    // with the clocks the rule made. This is interim wiring of existing prefabs to newly implemented glyphs (Q9 default,
    // waiting for the user), not an art verdict. Rules never read anything back from here.
    // Per-glyph translation lives in Vfx120SpellPresenter308.WPnn.cs (partial), one file per work package.
    // A catalogue body is a cast cue only (SpellCueLife308): it is never stretched to the life of a buff, a zone, a mark
    // or a rock. Persistence is not drawn by this presenter; the deploy-layer presenter that replaces it draws it.
    public sealed partial class Vfx120SpellPresenter308 : ISpellPresenter, ISpellPresenterBinding, ISpellPresenterProbe
    {
        sealed class Live
        {
            public SpellFxRequest Request;
            public GameObject Instance;
            public SpellSequenceEffect Sequence;
            public Vfx120Effect Fx;
            public Vfx120Profile ProfileSource, ProfileCopy;
        }

        // Spare profile copies kept per catalogue profile (a bound of bookkeeping: more than this are destroyed).
        const int SparesPerProfile = 4;

        private readonly Dictionary<int, Live> _live = new Dictionary<int, Live>();
        private readonly List<int> _dead = new List<int>();
        private readonly Dictionary<Vfx120Profile, Stack<Vfx120Profile>> _spares = new Dictionary<Vfx120Profile, Stack<Vfx120Profile>>();
        private SpellVisualSetSO _visuals;
        private Transform _owner;
        private int _next;

        public int LiveCount { get { Sweep(); return _live.Count; } }
        public int SpareProfiles { get { int n = 0; foreach (var stack in _spares.Values) n += stack.Count; return n; } }

        public void Bind(SpellVisualSetSO visuals, Transform owner)
        {
            _visuals = visuals; _owner = owner;
        }

        // Does the catalogue hold a body for this request? A lookup only: nothing is created.
        public bool CanShow(in SpellFxRequest request)
        {
            return _visuals != null && _visuals.TryGet(request.Letter, out var visual) && visual.FxPrefab != null;
        }

        public SpellFxHandle Begin(in SpellFxRequest request)
        {
            Sweep();
            if (_visuals == null || !_visuals.TryGet(request.Letter, out var visual) || visual.FxPrefab == null) return default;
            var live = new Live { Request = request };
            try
            {
                var instance = Object.Instantiate(visual.FxPrefab);
                live.Instance = instance;
                instance.name = "Spell308_" + request.Letter + "_" + request.Role;
                if (_owner != null && instance.scene != _owner.gameObject.scene) SceneManager.MoveGameObjectToScene(instance, _owner.gameObject.scene);
                live.Sequence = instance.GetComponent<SpellSequenceEffect>();
                live.Fx = live.Sequence as Vfx120Effect;
                Color tint = Color.white;
                if (live.Fx != null && live.Fx.Profile != null)
                {
                    var source = live.Fx.Profile;
                    // The body is a cast cue: never longer than it was authored for, and a lasting effect gets a short cue
                    // only (SpellCueLife308). The shared asset is never written: a body that keeps its authored life reads
                    // the asset itself, any other life goes into a private copy taken from the spares.
                    float life = SpellCueLife308.Seconds(request.Duration, source.Duration);
                    if (life != source.Duration)
                    {
                        live.ProfileSource = source; live.ProfileCopy = Rent(source);
                        live.ProfileCopy.Duration = life;
                        live.Fx.Profile = live.ProfileCopy;
                    }
                    live.Fx.PreviewControlled = false; live.Fx.DemonstrationCues = false;
                    tint = live.Fx.Profile.Pigment;
                }
                if (live.Sequence != null)
                {
                    if (request.ImpactClock > 0f) live.Sequence.SetImpactClock(request.ImpactClock);
                    if (request.Area != null) live.Sequence.SetAreaPlan(request.Area);
                    BeforeBegin(live.Fx, request);
                    live.Sequence.Begin(request.Origin, request.Target, request.FallbackPoint, tint);
                }
                else instance.transform.position = request.Origin;
                if (request.Follow != null) instance.transform.SetParent(request.Follow, true);
            }
            catch (Exception exception)
            {
                // nothing half-made stays behind: the body is destroyed and its profile copy goes back to the spares
                Debug.LogException(exception);
                Release(live);
                return default;
            }
            int id = ++_next; if (id == 0) id = ++_next;
            _live.Add(id, live);
            return new SpellFxHandle(id);
        }

        public void Cue(SpellFxHandle handle, SpellFxCue cue, in SpellFxCueArgs args)
        {
            if (!handle.Shown || !_live.TryGetValue(handle.Id, out var live) || live.Instance == null || live.Fx == null) return;
            bool handled = false;
            CueWP02(live.Fx, live.Request, cue, args, ref handled); CueWP03(live.Fx, live.Request, cue, args, ref handled);
            CueWP04(live.Fx, live.Request, cue, args, ref handled); CueWP05(live.Fx, live.Request, cue, args, ref handled);
            CueWP06(live.Fx, live.Request, cue, args, ref handled); CueWP07(live.Fx, live.Request, cue, args, ref handled);
            CueWP08(live.Fx, live.Request, cue, args, ref handled); CueWP09(live.Fx, live.Request, cue, args, ref handled);
            CueWP10(live.Fx, live.Request, cue, args, ref handled); CueWP11(live.Fx, live.Request, cue, args, ref handled);
            CueWP12(live.Fx, live.Request, cue, args, ref handled); CueWP13(live.Fx, live.Request, cue, args, ref handled);
            CueWP14(live.Fx, live.Request, cue, args, ref handled);
            if (handled) return;
            switch (cue)
            {
                case SpellFxCue.Hit:
                case SpellFxCue.Secondary: live.Fx.Signal(Vfx120Effect.Cue.Hit, args.Target); break;
                case SpellFxCue.TargetDefeated: live.Fx.Signal(Vfx120Effect.Cue.TargetDefeated, args.Target); break;
                case SpellFxCue.Break: live.Fx.Signal(Vfx120Effect.Cue.Break); break;
                case SpellFxCue.Expire:
                case SpellFxCue.Consume:
                case SpellFxCue.Return: live.Fx.Signal(Vfx120Effect.Cue.Release, args.Target); break;
                case SpellFxCue.Split: if (args.Targets != null) live.Fx.SetSecondaryTargets(args.Target, args.Targets); break;
            }
        }

        public void End(SpellFxHandle handle, bool immediate = false)
        {
            if (!handle.Shown || !_live.TryGetValue(handle.Id, out var live)) return;
            _live.Remove(handle.Id);
            Release(live);
        }

        // Death, rest, scene leave: every body goes, and so does every profile copy (the spares too: nothing outlives the scope).
        public void EndAll()
        {
            foreach (var live in _live.Values) Release(live);
            _live.Clear();
            foreach (var stack in _spares.Values)
                while (stack.Count > 0) Destroy(stack.Pop());
            _spares.Clear();
        }

        // Effects that ran out destroy their own object (Vfx120Effect.Update): forget those handles and their profile copies.
        void Sweep()
        {
            _dead.Clear();
            foreach (var pair in _live) if (pair.Value.Instance == null) _dead.Add(pair.Key);
            foreach (int id in _dead) { Release(_live[id]); _live.Remove(id); }
        }

        void Release(Live live)
        {
            Destroy(live.Instance);
            Return(live.ProfileSource, live.ProfileCopy);
            live.Instance = null; live.ProfileSource = null; live.ProfileCopy = null; live.Fx = null; live.Sequence = null;
        }

        // A private copy of a catalogue profile: a spare when one is free, a new one otherwise.
        Vfx120Profile Rent(Vfx120Profile source)
        {
            if (_spares.TryGetValue(source, out var stack))
                while (stack.Count > 0)
                {
                    var spare = stack.Pop();
                    if (spare != null) return spare;
                }
            var copy = Object.Instantiate(source);
            copy.hideFlags = HideFlags.DontSave;
            return copy;
        }

        void Return(Vfx120Profile source, Vfx120Profile copy)
        {
            if (copy == null) return;
            if (source == null) { Destroy(copy); return; }
            if (!_spares.TryGetValue(source, out var stack)) { stack = new Stack<Vfx120Profile>(); _spares.Add(source, stack); }
            if (stack.Count >= SparesPerProfile) Destroy(copy); else stack.Push(copy);
        }

        static void Destroy(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target); else Object.DestroyImmediate(target);
        }

        void BeforeBegin(Vfx120Effect fx, in SpellFxRequest request)
        {
            if (fx == null) return;
            BeginWP02(fx, request); BeginWP03(fx, request); BeginWP04(fx, request); BeginWP05(fx, request); BeginWP06(fx, request);
            BeginWP07(fx, request); BeginWP08(fx, request); BeginWP09(fx, request); BeginWP10(fx, request); BeginWP11(fx, request);
            BeginWP12(fx, request); BeginWP13(fx, request); BeginWP14(fx, request);
        }

        // Work-package translation hooks. A package implements its pair in Vfx120SpellPresenter308.WPnn.cs; it reads
        // request.Role / request.Element (never a glyph list) and sets handled = true when it consumed the cue.
        partial void BeginWP02(Vfx120Effect fx, in SpellFxRequest request);
        partial void BeginWP03(Vfx120Effect fx, in SpellFxRequest request);
        partial void BeginWP04(Vfx120Effect fx, in SpellFxRequest request);
        partial void BeginWP05(Vfx120Effect fx, in SpellFxRequest request);
        partial void BeginWP06(Vfx120Effect fx, in SpellFxRequest request);
        partial void BeginWP07(Vfx120Effect fx, in SpellFxRequest request);
        partial void BeginWP08(Vfx120Effect fx, in SpellFxRequest request);
        partial void BeginWP09(Vfx120Effect fx, in SpellFxRequest request);
        partial void BeginWP10(Vfx120Effect fx, in SpellFxRequest request);
        partial void BeginWP11(Vfx120Effect fx, in SpellFxRequest request);
        partial void BeginWP12(Vfx120Effect fx, in SpellFxRequest request);
        partial void BeginWP13(Vfx120Effect fx, in SpellFxRequest request);
        partial void BeginWP14(Vfx120Effect fx, in SpellFxRequest request);
        partial void CueWP02(Vfx120Effect fx, in SpellFxRequest request, SpellFxCue cue, in SpellFxCueArgs args, ref bool handled);
        partial void CueWP03(Vfx120Effect fx, in SpellFxRequest request, SpellFxCue cue, in SpellFxCueArgs args, ref bool handled);
        partial void CueWP04(Vfx120Effect fx, in SpellFxRequest request, SpellFxCue cue, in SpellFxCueArgs args, ref bool handled);
        partial void CueWP05(Vfx120Effect fx, in SpellFxRequest request, SpellFxCue cue, in SpellFxCueArgs args, ref bool handled);
        partial void CueWP06(Vfx120Effect fx, in SpellFxRequest request, SpellFxCue cue, in SpellFxCueArgs args, ref bool handled);
        partial void CueWP07(Vfx120Effect fx, in SpellFxRequest request, SpellFxCue cue, in SpellFxCueArgs args, ref bool handled);
        partial void CueWP08(Vfx120Effect fx, in SpellFxRequest request, SpellFxCue cue, in SpellFxCueArgs args, ref bool handled);
        partial void CueWP09(Vfx120Effect fx, in SpellFxRequest request, SpellFxCue cue, in SpellFxCueArgs args, ref bool handled);
        partial void CueWP10(Vfx120Effect fx, in SpellFxRequest request, SpellFxCue cue, in SpellFxCueArgs args, ref bool handled);
        partial void CueWP11(Vfx120Effect fx, in SpellFxRequest request, SpellFxCue cue, in SpellFxCueArgs args, ref bool handled);
        partial void CueWP12(Vfx120Effect fx, in SpellFxRequest request, SpellFxCue cue, in SpellFxCueArgs args, ref bool handled);
        partial void CueWP13(Vfx120Effect fx, in SpellFxRequest request, SpellFxCue cue, in SpellFxCueArgs args, ref bool handled);
        partial void CueWP14(Vfx120Effect fx, in SpellFxRequest request, SpellFxCue cue, in SpellFxCueArgs args, ref bool handled);
    }
}
