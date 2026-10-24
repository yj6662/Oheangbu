using System;
using System.Collections.Generic;
namespace Oheangbu.Vol2.Rules
{
    /// <summary>What the existing AttackProvenance cannot carry (design section 4): the cast, the verb and the reaction depth of a hit.
    /// Keyed by AttackId in <see cref="Vol2AttackLedger"/>. A seed that a gathering bursts keeps the ORIGINAL CastId; the trigger is
    /// recorded separately in TriggerAttackId / IgniterId.</summary>
    public readonly struct Vol2AttackMetadata
    {
        public AttackId AttackId { get; }
        public CastId CastId { get; }
        public SwingId SwingId { get; }
        public char Letter { get; }
        public Body Body { get; }
        public Frame Frame { get; }
        public Verb Verb { get; }
        public int ReactionDepth { get; }
        public long OriginalOwnerId { get; }
        public long IgniterId { get; }
        public AttackId TriggerAttackId { get; }
        public long IssuedUs { get; }
        public Vol2AttackMetadata(AttackId attackId, CastId castId, SwingId swingId, Glyph glyph, long issuedUs,
            int reactionDepth = 0, long originalOwnerId = 0, long igniterId = 0, AttackId triggerAttackId = default)
        {
            AttackId = attackId; CastId = castId; SwingId = swingId; Letter = glyph.Letter; Body = glyph.Body; Frame = glyph.Frame; Verb = glyph.Verb;
            IssuedUs = issuedUs; ReactionDepth = reactionDepth; OriginalOwnerId = originalOwnerId; IgniterId = igniterId; TriggerAttackId = triggerAttackId;
        }
    }

    public sealed class Vol2AttackLedger
    {
        readonly Dictionary<long, Vol2AttackMetadata> _byAttack = new Dictionary<long, Vol2AttackMetadata>();
        public int Count => _byAttack.Count;
        public void Register(Vol2AttackMetadata meta)
        {
            if (!meta.AttackId.IsValid) throw new ArgumentException("AttackId required");
            if (!meta.CastId.IsValid) throw new ArgumentException("CastId required");
            if (_byAttack.ContainsKey(meta.AttackId.Value)) throw new InvalidOperationException("AttackId already registered: " + meta.AttackId);
            _byAttack.Add(meta.AttackId.Value, meta);
        }
        public bool TryGet(AttackId id, out Vol2AttackMetadata meta) => _byAttack.TryGetValue(id.Value, out meta);
        public bool Release(AttackId id) => _byAttack.Remove(id.Value);
        /// <summary>A finished cast: every attack it issued is dropped (children and delayed work have ended first).</summary>
        public int ReleaseCast(CastId cast)
        {
            var drop = new List<long>();
            foreach (var p in _byAttack) if (p.Value.CastId == cast) drop.Add(p.Key);
            foreach (long k in drop) _byAttack.Remove(k);
            return drop.Count;
        }
        public void Clear() => _byAttack.Clear();
    }
}
