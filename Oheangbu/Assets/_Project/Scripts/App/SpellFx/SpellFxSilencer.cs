using System;
using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>D308-153: keeps the OLD looks (the legacy bridge and the free showcase) off a letter the new catalogue covers. One hook, in Vol2VfxModel.OnBegan:
    /// when <see cref="IsCovered"/> answers true for the cast's letter the model gives that cast a <see cref="SpellFxSilentRecipe"/> instead of the template's recipe, so
    /// neither the showcase recipe (never reached, nothing to relay) nor the bridge recipe it wraps (its Inner) ever sees the cast. Everything else of the model stays:
    /// the instance exists (SpawnedCount, TryGet, Instances), <c>Covers(log)</c> is unchanged (the debug lines still skip the cast), the cast camera still installs
    /// (Vol2VfxPresenter.Attach is untouched). An uncovered letter takes the old path exactly as before.
    /// "Covered" is asked at CAST time of whoever registered (the director: the catalogue has a playable prefab for the variant chosen now), so a tool that switches
    /// the variant between two casts changes the answer with it, and a missing catalogue covers nothing.</summary>
    public static class SpellFxSilencer
    {
        static readonly List<Func<char, bool>> Providers = new List<Func<char, bool>>();

        /// <summary>true = a covered cast still plays the caster's brush swing (motion + tip ink of the old stage) and nothing else. Default false: the literal "draw nothing".
        /// A tool may set it for a run; it is cleared when the runtime starts (domain reload is off).</summary>
        public static bool KeepBrushSwing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Providers.Clear(); KeepBrushSwing = false; }

        public static void Register(Func<char, bool> covers) { if (covers != null && !Providers.Contains(covers)) Providers.Add(covers); }
        public static void Unregister(Func<char, bool> covers) { Providers.Remove(covers); }
        public static int ProviderCount => Providers.Count;

        public static bool IsCovered(char letter)
        {
            for (int i = 0; i < Providers.Count; i++)
            {
                try { if (Providers[i](letter)) return true; }
                catch (Exception e) { Debug.LogException(e); }
            }
            return false;
        }
    }

    /// <summary>The recipe a covered letter gets: it draws nothing. All hooks are the base class's empty defaults; the instance finishes with the cast (the base IsFinished
    /// has no part to wait for). With <see cref="SpellFxSilencer.KeepBrushSwing"/> it asks the bridge stage for the caster's swing only.</summary>
    public sealed class SpellFxSilentRecipe : Vol2VfxRecipe
    {
        readonly Vol2Recipe _kind;
        public SpellFxSilentRecipe(Vol2Recipe kind) { _kind = kind; }
        public override Vol2Recipe Kind => _kind;

        public override void OnBegan(Vol2VfxInstance inst)
        {
            if (!SpellFxSilencer.KeepBrushSwing) return;
            var stage = inst.Model.Legacy;
            if (stage == null) return;
            var g = inst.Log.Glyph;
            // the same category mapping the free showcase used for the swing (Vol2FreeShowcaseRecipe.OnBegan): a path swings Single, a fan Area, a ring Ward, the parry Guard,
            // every other attachment Self
            var kind = g.Frame == Frame.Fan ? Vol2LegacyKind.Area : g.Frame == Frame.Attachment ? (g.Verb == Verb.Once ? Vol2LegacyKind.Guard : Vol2LegacyKind.Self)
                : g.Frame == Frame.Ring ? Vol2LegacyKind.Ward : Vol2LegacyKind.Single;
            bool fromStart = kind == Vol2LegacyKind.Guard || kind == Vol2LegacyKind.Ward || kind == Vol2LegacyKind.Self;
            float toApex = Mathf.Max(Vol2LegacyRecipeBase.MinToApex, (float)((Vol2LegacyRecipeBase.LaunchUs(inst) - inst.StartUs) / 1000000.0));
            stage.Swing(new Vol2LegacySwing { Letter = g.Letter, Body = g.Body, Kind = kind, ToApex = toApex, FromClipStart = fromStart, Distance = fromStart ? 0f : 4.6f });
        }
    }
}
