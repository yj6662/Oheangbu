using System;
using System.Collections.Generic;
using Oheangbu.App.World;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.App.SpellVFX120
{
    // #308 D308-127 step 2 (TEST): the sword form's weapon head on the great brushes the player holds (the world brush and
    // the close-up one), kept by presenter handle from Begin to End. SpellDeployPresenter308 owns one, so the head comes
    // whichever side (deploy layer or catalogue) draws the cast. Presentation only: a request without a rig, or a glyph
    // whose catalogue profile has no head (Vfx120Profile.WeaponHead308Prefab), shows nothing new.
    public sealed class SpellWeaponHeads308
    {
        private readonly Dictionary<int, GreatBrushWeapon308[]> _heads = new Dictionary<int, GreatBrushWeapon308[]>();
        private readonly Dictionary<int, SpellSwing308> _swings = new Dictionary<int, SpellSwing308>();

        public int Count => _heads.Count;
        public int Strikes { get; private set; }

        // A strike of the form (its Hit / Miss cue): the body plays the weapon's swing and every head shows its afterimage.
        // One strike sends a cue per enemy it met (the spear's two, the flail's three): only the first cue of a strike moves
        // the body (debate 15) - the rest carry the same rule time `at`. finish = the combo's last strike (its own swing).
        public void Strike(int id, float at = float.NaN, bool finish = false)
        {
            if (!_heads.TryGetValue(id, out var heads) || heads.Length == 0) return;
            if (!float.IsNaN(at))
            {
                if (_lastAt.TryGetValue(id, out float last) && last == at) return;
                _lastAt[id] = at;
            }
            Strikes++;
            var first = heads[0];
            bool finisher = finish && first != null && !string.IsNullOrEmpty(first.FinishState);
            if (first != null && _swings.TryGetValue(id, out var swing) && swing != null)
                swing.PlayStrike308(finisher ? first.FinishState : first.StrikeState, first.StrikeToApex, finisher ? first.FinishLock : first.StrikeLock);
            foreach (var head in heads) if (head != null) head.Strike(finisher);
        }

        private readonly Dictionary<int, float> _lastAt = new Dictionary<int, float>();

        // Debate 16, the bow's own cues: Tick = how far drawn (Value; negative = the draw ended without an arrow), Secondary = one
        // arrow (Target / Point / At = flight seconds, Value = the form's remaining life, re-sent because every shot spends ink).
        // true = the handle carries a bow and the cue was its (nothing else draws it).
        public bool Bow(int id, SpellFxCue cue, in SpellFxCueArgs args)
        {
            if (cue != SpellFxCue.Tick && cue != SpellFxCue.Secondary) return false;
            if (!_heads.TryGetValue(id, out var heads) || heads.Length == 0) return false;
            bool any = false;
            foreach (var head in heads)
            {
                var horn = head != null ? head.GetComponent<HornBow308>() : null;
                if (horn == null) continue;
                any = true;
                if (cue == SpellFxCue.Tick) horn.Draw(args.Value);
                else { horn.Shoot(args.Target, args.Point, args.At); head.SetLifeEnd(Time.time, args.Value); }
            }
            return any;
        }

        // The head prefab of a glyph: its catalogue body's profile (the cue profile the visual set points at).
        public static GameObject PrefabFor(SpellVisualSetSO visuals, char letter)
        {
            if (visuals == null || !visuals.TryGet(letter, out var visual) || visual.FxPrefab == null) return null;
            var fx = visual.FxPrefab.GetComponentInChildren<Vfx120Effect>(true);
            return fx != null && fx.Profile != null ? fx.Profile.WeaponHead308Prefab : null;
        }

        public void Begin(int id, in SpellFxRequest request, GameObject prefab)
        {
            if (request.Role != SpellFxRole.Attached || prefab == null || request.Follow == null) return;
            End(id);
            var made = new List<GreatBrushWeapon308>(2);
            try
            {
                var rig = request.Follow.root.GetComponentInChildren<WorldMacroPlayerGestureRig>(true);
                if (rig == null) return;
                var swing = rig.GetComponent<SpellSwing308>();
                if (swing != null) _swings[id] = swing;
                // debate 16: the bow is held by the world body only (the close-up arm shows only while drawing a glyph, which the
                // form keeps shut)
                bool bow = prefab.GetComponent<HornBow308>() != null;
                foreach (var brush in bow ? new[] { rig.WorldBrushRoot308 } : new[] { rig.WorldBrushRoot308, rig.NearBrushRoot308 })
                {
                    if (brush == null) continue;
                    var instance = Object.Instantiate(prefab);
                    var head = instance.GetComponent<GreatBrushWeapon308>();
                    // step 3: the head grows from now and dries over the last seconds of the form (Duration = the form's longest life)
                    if (head != null && head.Attach(brush))
                    {
                        head.SetLife(Time.time, request.Duration); made.Add(head);
                        var horn = instance.GetComponent<HornBow308>();
                        if (horn != null) horn.Bind(rig);
                    }
                    else if (Application.isPlaying) Object.Destroy(instance); else Object.DestroyImmediate(instance);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                foreach (var head in made) if (head != null) head.Detach();
                made.Clear();
            }
            if (made.Count > 0) _heads[id] = made.ToArray();
        }

        // The form ended (ink gone, put away): each head sinks back into its ferrule, then the bristles return (step 3).
        public void End(int id)
        {
            _swings.Remove(id); _lastAt.Remove(id);
            if (!_heads.TryGetValue(id, out var heads)) return;
            _heads.Remove(id);
            foreach (var head in heads) if (head != null) head.Detach(false);
        }

        public void EndAll()
        {
            foreach (var heads in _heads.Values)
                foreach (var head in heads) if (head != null) head.Detach();
            _heads.Clear(); _swings.Clear(); _lastAt.Clear();
        }

        // checks: the heads a handle put on (empty when none)
        public GreatBrushWeapon308[] Of(int id) => _heads.TryGetValue(id, out var heads) ? heads : Array.Empty<GreatBrushWeapon308>();
    }
}
