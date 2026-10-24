using System;
using System.Collections.Generic;
using Oheangbu.Core.Domain;
using Oheangbu.Vol2.Rules;
using UnityEngine;
using VolSpellKind = Oheangbu.Vol2.Rules.SpellKind;

namespace Oheangbu.Spellcraft
{
    // S5-b slice 1 (D308-149, design s5b_design_v1 sections 2 and 3). The Spellcraft side of the vol.2 route: a recognised letter
    // becomes a decoded glyph and a built plan, or a refusal with a reason. NO effect is created here (slices 2 to 8 hang the
    // executors on CastAccepted). Not a singleton: one adapter per combat scope, built by the installer.

    /// <summary>Which spell runtime a combat scope runs. Chosen when the scope is built; default Legacy308 everywhere.</summary>
    public enum SpellRuntimeMode { Legacy308 = 0, Vol2 = 1 }

    public enum Vol2CastRejectReason
    {
        None = 0,
        /// <summary>The letter is not one of the 120 glyphs (not Hangul, or a syllable outside body x frame x verb).</summary>
        NotInVocabulary,
        /// <summary>The glyph exists but this scope has not opened it.</summary>
        Locked,
        /// <summary>The numbers / table could not be loaded against the trusted manifest. Every letter is refused: no cast, no legacy fallback.</summary>
        RulebookUnavailable,
        /// <summary>The rules threw while building the plan (a data problem; Detail has the message).</summary>
        RulesError,
    }

    /// <summary>The validated vol.2 numbers and the 120-row table, plus where they came from. An unavailable rulebook carries the loader's error.</summary>
    public sealed class Vol2Rulebook
    {
        public NumbersV3Dto Numbers { get; }
        public IReadOnlyList<Vol2SpellRowDto> Spells { get; }
        public string DtoHash { get; }
        public string NumbersSha256 { get; }
        public string TableSha256 { get; }
        public string Error { get; }
        public bool Ok => Error == null && Numbers != null && Spells != null;
        public Vol2Rulebook(NumbersV3Dto numbers, IReadOnlyList<Vol2SpellRowDto> spells, string dtoHash, string numbersSha256, string tableSha256)
        { Numbers = numbers; Spells = spells; DtoHash = dtoHash; NumbersSha256 = numbersSha256; TableSha256 = tableSha256; }
        Vol2Rulebook(string error) { Error = error; }
        public static Vol2Rulebook Unavailable(string error) => new Vol2Rulebook(string.IsNullOrEmpty(error) ? "rulebook not loaded" : error);
    }

    /// <summary>What the adapter decided about one recognised letter. Immutable after Receive returns; carries the plan the executors will use.</summary>
    public sealed class Vol2CastDecision
    {
        public char Letter { get; internal set; }
        public bool Accepted { get; internal set; }
        public Vol2CastRejectReason Reason { get; internal set; }
        public string Detail { get; internal set; } = "";
        public long DecidedUs { get; internal set; }
        /// <summary>Issued only for an accepted cast (design section 4); invalid (0) for a refusal.</summary>
        public CastId CastId { get; internal set; }
        /// <summary>True when the letter decoded to a glyph (also true for a Locked or RulesError refusal).</summary>
        public bool Decoded { get; internal set; }
        public Glyph Glyph { get; internal set; }
        public Vol2TemplateId Template { get; internal set; }
        public bool IsPeak { get; internal set; }
        public bool IsRecovery { get; internal set; }
        public bool IsParry { get; internal set; }
        /// <summary>The built plan (cost, terms, life). Null unless Accepted.</summary>
        public GlyphRulesV3.Plan Plan { get; internal set; }
        // Recognition facts of the DrawnLetter, copied untouched (the vol.2 rules do not read them in slice 1).
        public float WorstJamoDistance { get; internal set; }
        public float AverageDistance { get; internal set; }
        public float StrokeDuration { get; internal set; }
        public float HoldDuration { get; internal set; }
        public int StrokeCount { get; internal set; }
        public string TemplateName => Decoded ? Template.Name : "";
        public string PeakLetter => Decoded && IsPeak ? Letter.ToString() : "";
    }

    public sealed class Vol2GlyphAdapter
    {
        readonly NumbersV3Dto _numbers;
        readonly string _unavailable;
        readonly IVol2Clock _clock;
        readonly Vol2IdIssuer _ids;
        readonly Func<Glyph, bool> _isOpen;

        /// <summary>Raised once per received letter, accepted or refused, after the decision is final. A throwing subscriber is logged and isolated.</summary>
        public event Action<Vol2CastDecision> Decided;
        public event Action<Vol2CastDecision> CastAccepted;
        public event Action<Vol2CastDecision> CastRejected;

        public long Received { get; private set; }
        public long AcceptedCount { get; private set; }
        public long RejectedCount { get; private set; }

        /// <param name="numbers">validated numbers, or null when the rulebook could not be loaded (then <paramref name="unavailable"/> says why and every letter is refused)</param>
        /// <param name="isOpen">which glyphs this scope has opened; null = all 120 (the spell lab)</param>
        public Vol2GlyphAdapter(NumbersV3Dto numbers, string unavailable, IVol2Clock clock, Vol2IdIssuer ids, Func<Glyph, bool> isOpen = null)
        {
            _numbers = numbers; _unavailable = unavailable;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _ids = ids ?? throw new ArgumentNullException(nameof(ids));
            _isOpen = isOpen;
        }

        public Vol2CastDecision Receive(DrawnLetter drawn)
        {
            var d = Decide(drawn.Letter);
            d.WorstJamoDistance = drawn.WorstJamoDistance; d.AverageDistance = drawn.AverageDistance;
            d.StrokeDuration = drawn.StrokeDuration; d.HoldDuration = drawn.HoldDuration; d.StrokeCount = drawn.StrokeCount;
            Publish(d);
            return d;
        }

        /// <summary>Decide a bare letter (no recognition facts) and publish it.</summary>
        public Vol2CastDecision Receive(char letter)
        {
            var d = Decide(letter);
            Publish(d);
            return d;
        }

        Vol2CastDecision Decide(char letter)
        {
            var d = new Vol2CastDecision { Letter = letter, DecidedUs = _clock.NowUs };
            if (_numbers == null || !_numbers.IsValidated)
            { d.Reason = Vol2CastRejectReason.RulebookUnavailable; d.Detail = _unavailable ?? "numbers not validated"; return d; }
            if (!GlyphRulesV3.TryDecode(letter, out var glyph))
            { d.Reason = Vol2CastRejectReason.NotInVocabulary; d.Detail = "U+" + ((int)letter).ToString("X4"); return d; }
            d.Decoded = true; d.Glyph = glyph; d.Template = Vol2TemplateId.Of(glyph); d.IsPeak = glyph.Kind == VolSpellKind.Peak; d.IsParry = glyph.IsParry;
            if (_isOpen != null && !_isOpen(glyph)) { d.Reason = Vol2CastRejectReason.Locked; d.Detail = "glyph not open in this scope"; return d; }
            GlyphRulesV3.Plan plan;
            try { plan = GlyphRulesV3.Build(glyph, _numbers); }
            catch (Exception e) { d.Reason = Vol2CastRejectReason.RulesError; d.Detail = e.GetType().Name + ": " + e.Message; return d; }
            d.Plan = plan; d.IsRecovery = plan.IsRecovery;
            d.Accepted = true; d.CastId = _ids.NextCast();
            return d;
        }

        void Publish(Vol2CastDecision d)
        {
            Received++;
            if (d.Accepted) AcceptedCount++; else RejectedCount++;
            Raise(Decided, d);
            Raise(d.Accepted ? CastAccepted : CastRejected, d);
        }

        static void Raise(Action<Vol2CastDecision> handlers, Vol2CastDecision d)
        {
            if (handlers == null) return;
            foreach (Action<Vol2CastDecision> h in handlers.GetInvocationList())
            {
                try { h(d); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }

    /// <summary>The route a combat scope was built with. Legacy308 carries no adapter and changes nothing; Vol2 hands every recognised
    /// letter to the adapter and to nothing else. A Vol2 route never falls back to the old resolver: a missing rulebook is a refusal.</summary>
    public sealed class SpellRoute
    {
        public SpellRuntimeMode Mode { get; }
        public Vol2GlyphAdapter Adapter { get; }
        public bool IsVol2 => Mode == SpellRuntimeMode.Vol2;
        SpellRoute(SpellRuntimeMode mode, Vol2GlyphAdapter adapter) { Mode = mode; Adapter = adapter; }
        public static SpellRoute CreateLegacy() => new SpellRoute(SpellRuntimeMode.Legacy308, null);
        public static SpellRoute CreateVol2(Vol2GlyphAdapter adapter) => new SpellRoute(SpellRuntimeMode.Vol2, adapter ?? throw new ArgumentNullException(nameof(adapter)));
        public Vol2CastDecision RouteVol2(DrawnLetter letter)
        {
            if (!IsVol2) throw new InvalidOperationException("route is not Vol2");
            return Adapter.Receive(letter);
        }
    }
}
