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
        public void Strike(int id)
        {
            if (!_heads.TryGetValue(id, out var heads) || heads.Length == 0) return;
            Strikes++;
            var first = heads[0];
            if (first != null && _swings.TryGetValue(id, out var swing) && swing != null) swing.PlayStrike308(first.StrikeState, first.StrikeToApex, first.StrikeLock);
            foreach (var head in heads) if (head != null) head.Strike();
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
                foreach (var brush in new[] { rig.WorldBrushRoot308, rig.NearBrushRoot308 })
                {
                    if (brush == null) continue;
                    var instance = Object.Instantiate(prefab);
                    var head = instance.GetComponent<GreatBrushWeapon308>();
                    if (head != null && head.Attach(brush)) made.Add(head);
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

        public void End(int id)
        {
            _swings.Remove(id);
            if (!_heads.TryGetValue(id, out var heads)) return;
            _heads.Remove(id);
            foreach (var head in heads) if (head != null) head.Detach();
        }

        public void EndAll()
        {
            foreach (var heads in _heads.Values)
                foreach (var head in heads) if (head != null) head.Detach();
            _heads.Clear(); _swings.Clear();
        }

        // checks: the heads a handle put on (empty when none)
        public GreatBrushWeapon308[] Of(int id) => _heads.TryGetValue(id, out var heads) ? heads : Array.Empty<GreatBrushWeapon308>();
    }
}
